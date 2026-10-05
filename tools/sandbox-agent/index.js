#!/usr/bin/env node
// Webly sandbox agent.
//
// The one process Webly talks to inside a sandbox. Every sandbox provider — E2B, Fly, a local Docker
// container — has a different API for uploading files, running commands and exposing ports, and none of
// them has a .NET SDK. So the providers' job shrinks to "start this image and give me a URL", and
// everything else is this contract:
//
//   GET  /health                      → { ok, devServer, node }
//   POST /files            (tar body) → extract into the workspace, replacing it
//   GET  /files                       → tar of the workspace, minus the ignored paths
//   POST /exec             (json)     → run a command, streaming NDJSON lines back as it goes
//   POST /dev/start        (json)     → start the dev server (once)
//   ANY  /preview/*                   → proxy to the dev server, WebSockets included
//
// Zero dependencies, on purpose: this file is baked into a sandbox image, and an `npm install` in the
// image is a supply chain plus a build step for something that is 300 lines of node:http. `tar` does the
// archive work because it is already in every image that can run Next.js.

import { createServer, request as httpRequest } from 'node:http';
import { connect } from 'node:net';
import { spawn } from 'node:child_process';
import { mkdirSync, existsSync, readdirSync, rmSync, writeFileSync, lstatSync, readlinkSync, realpathSync } from 'node:fs';
import { basename, dirname } from 'node:path';

const PORT = Number(process.env.WEBLY_AGENT_PORT ?? 8080);
const TOKEN = process.env.WEBLY_AGENT_TOKEN ?? '';
const WORKSPACE = process.env.WEBLY_WORKSPACE ?? '/workspace';
const DEV_PORT = Number(process.env.WEBLY_DEV_PORT ?? 3000);

/**
 * Where to record the dev server's process id, if whoever started this agent asked for it.
 *
 * **Outside the workspace**, always: a file inside it would travel into the site's next commit and vanish on
 * the next re-seed. It exists for the case the agent cannot clean up after itself — being killed with SIGKILL,
 * or dying with the machine's memory — where the dev server is in its own process group and survives both. The
 * starter can then take it down on the way past. In a container nobody sets it and nothing writes it, because
 * stopping the container takes everything in it.
 */
const DEV_PIDFILE = process.env.WEBLY_DEV_PIDFILE ?? '';

/**
 * This process's environment without the agent's own credential in it.
 *
 * Every command runs as a child of this process, so `...process.env` handed each of them `WEBLY_AGENT_TOKEN` —
 * the bearer token Webly authenticates to *this* service with. The coding agent has a shell in here and can
 * already write the workspace and run anything, so nothing about the site was at risk; what it gained was the
 * ability to speak as the control plane on its own machine, which is one step it should not have towards
 * speaking as the control plane anywhere else. The token is this service's, not the workload's.
 */
const INHERITED_ENV = (() => {
  const { WEBLY_AGENT_TOKEN: _token, ...rest } = process.env;

  return rest;
})();

/**
 * A command's environment, with `PWD` saying where it really is.
 *
 * `spawn`'s `cwd` moves the process and leaves `PWD` as the parent had it, and OpenCode believes `PWD` over the
 * directory it is actually in. Under the local provider that was the directory the backend was launched from —
 * a Webly checkout — so the first turns it ran through the app treated the whole of Webly as the site: one went
 * looking through `.run` before finding its workspace, and the next checked a site's bare repository out on its
 * own and committed to its branch behind Webly's back. In the image nobody sets `PWD`, which is why that is the
 * only place it could not happen; it is set here so that it is true everywhere rather than absent by luck.
 */
function environmentFor(directory, env) {
  return { ...INHERITED_ENV, ...env, PWD: directory };
}

/**
 * Whether what this agent runs is confined to the workspace: `bubblewrap`, or `none`.
 *
 * **A command can reach nothing of this machine but its workspace.** The coding agent and everything it starts —
 * the CLI, `npm ci` and the packages' install scripts, the dev server running the site's own code — run in a
 * bubblewrap sandbox where the workspace is the only thing they can write and the rest of the filesystem does not
 * exist: no home directory, no other site's workspace, no Webly checkout, no `.run/repositories`. The system's
 * own directories and the toolchain (node, git, the agent CLIs) are there read-only, because a program has to be
 * readable to run; that is the whole of what is visible. The environment is an allow-list too, so the backend's
 * connection string and keys, which a child process used to inherit, are not in it.
 *
 * It exists because the local provider is a process on somebody's machine, and the first real agent run through
 * it went looking outside its workspace and found a site's bare repository to commit to. Moving the workspace and
 * fixing `PWD` took away its reasons; this takes away the ability. A container is already this boundary, so the
 * image leaves it `none` — the same flag would confine inside a microVM, where bubblewrap can create namespaces.
 */
const CONFINE = process.env.WEBLY_CONFINE ?? 'none';

/**
 * The agent CLI's home: beside the workspace, never inside it, for the reason the pid files are. It is where
 * Claude Code and OpenCode keep the sessions a warm workspace resumes, so it lives as long as the workspace and
 * is private to it — one site's agent cannot read another's conversation.
 */
const AGENT_HOME = `${WORKSPACE}.home`;

/** What a confined command may know of this process's environment, by name. */
const CONFINED_ENV = new Set([
  'PATH', 'LANG', 'LANGUAGE', 'TZ', 'TERM',
  // A network that only works through a proxy, with its own certificate authority, is a network the agent's
  // model calls and `npm ci` need. The files these name are mounted read-only below.
  'HTTP_PROXY', 'HTTPS_PROXY', 'NO_PROXY', 'ALL_PROXY', 'http_proxy', 'https_proxy', 'no_proxy', 'all_proxy',
  'NODE_EXTRA_CA_CERTS', 'SSL_CERT_FILE', 'SSL_CERT_DIR', 'REQUESTS_CA_BUNDLE', 'CURL_CA_BUNDLE', 'GIT_SSL_CAINFO',
  // Whatever the provider declared the workload's own (SandboxSpec.Environment).
  ...(process.env.WEBLY_WORKLOAD_ENV ?? '').split(',').filter(Boolean),
]);

const isSystemPath = path => ['/usr', '/bin', '/sbin', '/lib', '/lib32', '/lib64', '/libx32', '/etc']
  .some(root => path === root || path.startsWith(`${root}/`));

/** A tool on PATH, as the shell would find it, or null. */
function which(tool) {
  for (const directory of (process.env.PATH ?? '').split(':')) {
    const candidate = `${directory}/${tool}`;

    if (directory && existsSync(candidate)) return candidate;
  }

  return null;
}

/**
 * The smallest directory a tool needs to run: the `node_modules` an npm-installed CLI lives in, since it loads
 * its siblings (OpenCode execs a platform binary from one), or the prefix of a node install, for npm and npx.
 */
function installRoot(file) {
  const packages = file.lastIndexOf('/node_modules/');

  if (packages >= 0) return file.slice(0, packages + '/node_modules'.length);

  const directory = dirname(file);

  return basename(directory) === 'bin' ? dirname(directory) : directory;
}

/**
 * The bubblewrap arguments every confined command shares, worked out once. Order matters: `/tmp` is a fresh
 * tmpfs *before* anything is bound into it, because the workspace, its home and a toolchain can all live under
 * the host's `/tmp` and have to appear on top of the empty one rather than be hidden by it.
 */
const CONFINEMENT = CONFINE === 'bubblewrap' ? (() => {
  mkdirSync(AGENT_HOME, { recursive: true });

  const args = [
    '--die-with-parent', '--new-session',
    // Its own process table, so a killed bwrap takes everything inside with it: the dev server's grandchildren
    // included, which a process-group kill alone would miss across the session --new-session starts.
    '--unshare-pid', '--unshare-ipc', '--unshare-uts', '--unshare-cgroup-try',
    '--ro-bind', '/usr', '/usr',
  ];

  // Merged-/usr systems make these symlinks, and a symlink is recreated rather than bound so it keeps pointing
  // into the read-only /usr above.
  for (const path of ['/bin', '/sbin', '/lib', '/lib32', '/lib64', '/libx32']) {
    if (!existsSync(path)) continue;

    args.push(...(lstatSync(path).isSymbolicLink() ? ['--symlink', readlinkSync(path), path] : ['--ro-bind', path, path]));
  }

  // The parts of /etc a program reads to resolve a name, trust a certificate and know who it is — and nothing
  // else of it, because /etc is also where a machine keeps things that are nobody's business.
  for (const path of ['resolv.conf', 'hosts', 'nsswitch.conf', 'host.conf', 'gai.conf', 'ssl', 'ca-certificates',
    'pki', 'passwd', 'group', 'localtime', 'alternatives', 'ld.so.cache', 'ld.so.conf', 'ld.so.conf.d']) {
    args.push('--ro-bind-try', `/etc/${path}`, `/etc/${path}`);
  }

  args.push('--proc', '/proc', '--dev', '/dev', '--tmpfs', '/tmp');

  const toolchain = new Set([installRoot(realpathSync(process.execPath))]);

  for (const tool of ['npm', 'npx', 'git', 'claude', 'opencode', 'vercel']) {
    const found = which(tool);

    if (!found) continue;

    toolchain.add(dirname(found));
    toolchain.add(installRoot(realpathSync(found)));
  }

  for (const root of toolchain) {
    if (root !== '/' && !isSystemPath(root)) args.push('--ro-bind', root, root);
  }

  // Several variables commonly name the same bundle; it is mounted once.
  const certificates = new Set(['NODE_EXTRA_CA_CERTS', 'SSL_CERT_FILE', 'SSL_CERT_DIR', 'REQUESTS_CA_BUNDLE',
    'CURL_CA_BUNDLE', 'GIT_SSL_CAINFO'].map(name => process.env[name]).filter(Boolean));

  for (const path of certificates) {
    if (!isSystemPath(path)) args.push('--ro-bind-try', path, path);
  }

  // Read-only paths the starter declared: a dependency tree it already has, the way an image carries one
  // prebaked. The harness links the template's node_modules into each workspace, and a link to a directory the
  // sandbox cannot see is a missing dependency.
  for (const path of (process.env.WEBLY_CONFINE_READ ?? '').split(':').filter(Boolean)) {
    args.push('--ro-bind', path, path);
  }

  args.push('--bind', WORKSPACE, WORKSPACE, '--bind', AGENT_HOME, AGENT_HOME);

  return args;
})() : null;

/**
 * How to start a command: as asked, or inside the workspace's bubblewrap sandbox with an allow-listed
 * environment. The one place both `/exec` and the dev server start a process, so neither can be confined
 * without the other.
 */
function launch(command, args, directory, env) {
  if (!CONFINEMENT) return { file: command, args, env: environmentFor(directory, env) };

  const allowed = Object.fromEntries(Object.entries(INHERITED_ENV)
    .filter(([name]) => CONFINED_ENV.has(name) || name.startsWith('LC_')));

  return {
    file: 'bwrap',
    args: [...CONFINEMENT, '--chdir', directory, '--', command, ...args],
    env: { ...allowed, ...env, HOME: AGENT_HOME, TMPDIR: '/tmp', PWD: directory },
  };
}

// What never travels back to git. Build output and dependencies are reproducible from the tree, and a
// stray .env would put a customer's secret in their history for ever.
//
// `*.tsbuildinfo` is the one that is not obvious, and it is not hypothetical: `AGENTS.md` asks the agent to run
// `npm run typecheck` before finishing and Webly runs it too, and `tsc --noEmit --incremental` writes its cache
// next to the tsconfig. So every turn would have committed a machine-readable dump of the project into the
// customer's history and shown it in their diff. The template points that file into `.next` as well, which is
// belt and braces on purpose: this list is what decides what a commit *can* contain, and it must not depend on
// one setting in one file the agent is asked not to edit.
const IGNORED = [
  '.git', 'node_modules', '.next', '.vercel', '.turbo', 'dist', 'out',
  '.env', '.env.local', '.env.*.local', '*.log', '*.tsbuildinfo', '.DS_Store', '.claude', '.opencode',
];

mkdirSync(WORKSPACE, { recursive: true });

let devServer = null;
let devReady = false;

// The dev server's output, and how much of it there has ever been. **Module state, not properties on the child**,
// so that both survive the child exiting. They used to hang off `devServer`, which is set to null when it closes —
// so a dev server that died took the only record of why with it, `/dev/log` answered with nothing, and the preview
// 502ed while the app could not tell "never started" from "crashed, and here is the reason". That is a diagnosis
// somebody then has to do by hand against a machine they may not have.
let devLog = '';
let devLogLength = 0;

// How it ended, once it has: `{ code, signal }`. Null while it is running or before it ever ran.
let devExit = null;

/**
 * The path the dev server believes it is served at, which the caller of /dev/start supplies because only
 * Webly knows it — it names the site. Everything under /preview/ is rewritten onto it, and the dev server is
 * started with WEBLY_PREVIEW_BASE set to the same value so that the URLs it writes into its HTML agree.
 * Empty when nobody said, which is the harness and any other caller that reaches the dev server directly.
 */
let devBasePath = '';

/** Bearer check on everything. The sandbox's URL is guessable; the token is not. */
function authorized(request) {
  if (!TOKEN) return true;

  return request.headers.authorization === `Bearer ${TOKEN}`;
}

function json(response, status, body) {
  const payload = JSON.stringify(body);
  response.writeHead(status, { 'content-type': 'application/json', 'content-length': Buffer.byteLength(payload) });
  response.end(payload);
}

function readJson(request) {
  return new Promise((resolve, reject) => {
    let body = '';
    request.on('data', chunk => (body += chunk));
    request.on('end', () => {
      try {
        resolve(body.length ? JSON.parse(body) : {});
      } catch (error) {
        reject(error);
      }
    });
    request.on('error', reject);
  });
}

/**
 * The workspace's own path taken out of a command's output.
 *
 * Everything this agent prints can end up in front of the site's owner — a compile error in the chat, a failed
 * build's log folded away under a deployment — and tools name files absolutely. `src/app/page.tsx` is the part
 * somebody can act on; `/home/someone/webly/.run/workspaces/AbC-20260921…/src/app/page.tsx` is that plus a
 * description of a machine they have never seen. This is the one place that knows what to remove, because it is
 * the one place that was told the root.
 */
function relative(text) {
  return text.split(`${WORKSPACE}/`).join('').split(WORKSPACE).join('.');
}

/**
 * Runs a command in the workspace and streams one NDJSON object per event:
 *   {"type":"stdout","text":"…"} | {"type":"stderr","text":"…"} | {"type":"exit","code":0}
 *
 * NDJSON rather than raw bytes because the caller needs to tell the two streams apart and know the exit
 * code, and rather than a WebSocket because a streaming HTTP response is something every HTTP client
 * already does — including the one in .NET, without a library.
 */
function exec(response, { command, args = [], env = {}, timeoutMs = 600_000, cwd }) {
  response.writeHead(200, { 'content-type': 'application/x-ndjson', 'cache-control': 'no-cache' });

  const directory = cwd ? `${WORKSPACE}/${cwd}` : WORKSPACE;

  const launched = launch(command, args, directory, env);

  const child = spawn(launched.file, launched.args, {
    cwd: directory,
    env: launched.env,
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  const write = (type, text) => response.write(`${JSON.stringify({ type, text: relative(text) })}\n`);

  child.stdout.setEncoding('utf8');
  child.stderr.setEncoding('utf8');
  child.stdout.on('data', text => write('stdout', text));
  child.stderr.on('data', text => write('stderr', text));

  // A kill rather than a hang: an agent CLI that wedges would otherwise hold a paid sandbox open until
  // the reaper notices, and the caller would be waiting on a stream that never ends.
  const timer = setTimeout(() => child.kill('SIGKILL'), timeoutMs);

  child.on('error', error => {
    clearTimeout(timer);
    write('stderr', `${command}: ${error.message}\n`);
    response.end(`${JSON.stringify({ type: 'exit', code: 127 })}\n`);
  });

  child.on('close', code => {
    clearTimeout(timer);
    response.end(`${JSON.stringify({ type: 'exit', code: code ?? -1 })}\n`);
  });

  // The caller gave up (a cancelled turn). Kill the command rather than leaking a process into a sandbox
  // that will be reused for the next turn.
  response.on('close', () => {
    if (!child.killed) child.kill('SIGKILL');
  });
}

/**
 * What is left alone when a tree is written: the expensive derived directories.
 *
 * They are not part of any commit — `streamTar` excludes them on the way out — and rebuilding them costs
 * minutes, which is the whole reason a workspace stays warm. Everything else is replaced.
 */
const PRESERVED = ['node_modules', '.next', '.vercel', '.turbo', '.claude', '.opencode'];

// Every preserved name is also in IGNORED, and that is the invariant rather than a coincidence: a directory
// kept across a re-seed must never be able to travel back out into a commit. `out` and `dist` are in IGNORED
// but deliberately not here — build output should be rebuilt from the tree that arrived, and the publish path
// builds after seeding anyway.
for (const name of PRESERVED) {
  if (!IGNORED.includes(name)) throw new Error(`${name} is preserved across a re-seed but is not ignored on the way out`);
}

/**
 * Replaces the workspace's source with the tree in the request body.
 *
 * <b>Replaces, not overlays.</b> `tar -x` on its own only adds and overwrites, so a file that the incoming tree
 * does not contain would survive — and then get committed again by the next turn. That matters most for the
 * case the seeding code exists for: re-seeding a warm workspace after a restore that *deleted* a page would
 * leave the page there, and the restore would silently not have removed it. So the source is cleared first,
 * keeping only the derived directories above.
 */
function extractTar(request, response) {
  try {
    for (const entry of readdirSync(WORKSPACE)) {
      if (PRESERVED.includes(entry)) continue;
      rmSync(`${WORKSPACE}/${entry}`, { recursive: true, force: true });
    }
  } catch (error) {
    return json(response, 500, { ok: false, error: `could not clear the workspace: ${error.message}` });
  }

  const tar = spawn('tar', ['-x', '-z', '-C', WORKSPACE], { stdio: ['pipe', 'ignore', 'pipe'] });
  let error = '';

  tar.stderr.setEncoding('utf8');
  tar.stderr.on('data', text => (error += text));

  request.pipe(tar.stdin);

  tar.on('close', code =>
    code === 0 ? json(response, 200, { ok: true }) : json(response, 500, { ok: false, error }));
}

function streamTar(response) {
  const excludes = IGNORED.flatMap(pattern => ['--exclude', `./${pattern}`]);
  const tar = spawn('tar', ['-c', '-z', '-C', WORKSPACE, ...excludes, '.'], { stdio: ['ignore', 'pipe', 'ignore'] });

  response.writeHead(200, { 'content-type': 'application/gzip' });
  tar.stdout.pipe(response);
}

function startDevServer(response, { command = 'npm', args = ['run', 'dev'], env = {}, basePath = '' }) {
  if (devServer) return json(response, 200, { ok: true, alreadyRunning: true });

  // A restart keeps the old output: what the last one said as it died is exactly what somebody needs, and a
  // caller reading from a recorded offset still sees only what happened after that offset.
  devExit = null;

  // No trailing slash, because it is concatenated with paths that start with one.
  devBasePath = basePath.replace(/\/+$/, '');

  const launched = launch(command, args, WORKSPACE, { ...env, PORT: String(DEV_PORT), WEBLY_PREVIEW_BASE: devBasePath });

  devServer = spawn(launched.file, launched.args, {
    cwd: WORKSPACE,
    env: launched.env,
    stdio: ['ignore', 'pipe', 'pipe'],
    // Its own process group, so it can be killed as a group on the way out. `npm run dev` spawns `next`, which
    // spawns the server, so signalling the npm process alone leaves the actual dev server running — see the
    // shutdown handler at the bottom of this file.
    detached: true,
  });

  // `devLog` is served on /dev/log: a compile error in the dev server is the most useful thing the editor can
  // show, and it is the thing the next agent turn has to be told about. `devLogLength` is how much the dev server
  // has ever said, not how much is in the buffer — which is what makes "did anything go wrong *during this turn*"
  // answerable. The buffer is a sliding window, so a caller reading it after a turn would see output from every
  // earlier turn too, and a compile error from three turns ago would read as a compile error now, for ever. A
  // caller that records the offset before it starts and passes it back afterwards gets only what happened in
  // between.
  const record = raw => {
    const text = relative(raw);

    devLog = (devLog + text).slice(-16_000);
    devLogLength += text.length;
    if (/ready|started server|compiled/i.test(text)) devReady = true;
  };

  devServer.stdout.setEncoding('utf8');
  devServer.stderr.setEncoding('utf8');
  devServer.stdout.on('data', record);
  devServer.stderr.on('data', record);

  if (DEV_PIDFILE) {
    try {
      writeFileSync(DEV_PIDFILE, String(devServer.pid));
    } catch (error) {
      // Not worth failing a dev server over: the pidfile is a second line of defence, not the first.
      console.error(`could not write ${DEV_PIDFILE}: ${error.message}`);
    }
  }

  devServer.on('close', (code, signal) => {
    devServer = null;
    devReady = false;
    devExit = { code, signal };

    if (DEV_PIDFILE) {
      try {
        rmSync(DEV_PIDFILE, { force: true });
      } catch {
        // Gone already, or never written.
      }
    }

    // Recorded into the log itself, so one read answers both "what did it say" and "is it still there". A dev
    // server killed by the machine (the out-of-memory killer, on a box running several) says nothing on its way
    // out, and a caller seeing only silence would report a healthy site with a broken preview.
    record(`\nwebly: the dev server exited (code ${code ?? 'none'}, signal ${signal ?? 'none'}).\n`);
  });

  json(response, 200, { ok: true });
}

/**
 * What to ask the dev server for, given what /preview was asked for.
 *
 * `/preview/x` becomes `${devBasePath}/x`, because the dev server was started believing it is served at that
 * base — see `startDevServer`. With no base it is the plain `/x` this always used to send.
 */
function devPath(url) {
  const rest = url.slice('/preview'.length);

  return `${devBasePath}${rest || '/'}`;
}

/**
 * Proxies /preview/* to the dev server.
 *
 * Through node's own HTTP client rather than a raw TCP relay, which is what this was first written as. The
 * relay looked smaller — rewrite the request line, pipe both ways — and it does not work: writing a
 * complete HTTP response into `response.socket` while the server's own response object still owns that
 * socket ends the connection without a parseable reply, so every preview fetch fails with "other side
 * closed". `http.request` is still zero-dependency, and it gets chunked encoding, content-length and
 * keep-alive right instead of re-deriving them.
 *
 * The WebSocket upgrade is the one case that genuinely is a raw relay, and it is handled separately in
 * `server.on('upgrade')` below.
 */
function proxy(request, response) {
  const path = devPath(request.url);
  const headers = { ...request.headers, host: `127.0.0.1:${DEV_PORT}` };
  delete headers.authorization;

  const upstream = httpRequest(
    { host: '127.0.0.1', port: DEV_PORT, method: request.method, path, headers },
    upstreamResponse => {
      // The dev server's own status and headers, passed through untouched: a 404 from the site has to read
      // as a 404, and Next.js's content types and cache headers are part of what the preview is for.
      response.writeHead(upstreamResponse.statusCode ?? 502, upstreamResponse.headers);
      upstreamResponse.pipe(response);
    });

  upstream.on('error', () => {
    // A sentence rather than a dead socket, because this reaches an <iframe> in somebody's editor — and one of
    // two sentences, because the two cases need different answers. A dev server that is starting or recompiling
    // will answer in a moment, so the caller should retry; one that has exited will not answer ever, and a page
    // that retries for ever in front of it tells somebody their site is compiling until they give up.
    //
    // The state also rides on a header, so the caller can tell them apart without reading the body — which is
    // what lets Webly's preview proxy put its own page there instead of forwarding this JSON into the frame.
    if (!response.headersSent) {
      const stopped = !devServer;

      response.setHeader('x-webly-dev', stopped ? 'stopped' : 'starting');

      json(response, stopped ? 503 : 502, {
        ok: false,
        error: stopped
          ? 'The dev server is not running.'
          : 'The dev server is not answering yet.',
      });
    } else {
      response.end();
    }
  });

  request.pipe(upstream);
}

const server = createServer(async (request, response) => {
  if (!authorized(request)) return json(response, 401, { ok: false });

  const url = request.url ?? '/';

  try {
    if (url.startsWith('/preview')) return proxy(request, response);

    if (url === '/health') {
      return json(response, 200, {
        ok: true,
        devServer: devServer ? (devReady ? 'ready' : 'starting') : 'stopped',
        devExit,
        node: process.version,
      });
    }

    // GET /dev/log[?since=N] → { ok, log, offset }
    //
    // `since` is an offset previously returned as `offset`, and the log comes back trimmed to what arrived
    // after it. `offset` is always the dev server's total output so far, so a caller records it before doing
    // something and reads from it afterwards. Without `since`, the whole buffer — which is what a person asking
    // "what is my site doing" wants.
    if (url.startsWith('/dev/log')) {
      const buffer = devLog;
      const offset = devLogLength;
      const since = Number(new URL(url, 'http://localhost').searchParams.get('since') ?? 0);

      // Where the sliding window starts, in total-output terms. A `since` older than that is clamped: the
      // output is gone, and returning the whole window is better than returning nothing.
      const windowStart = Math.max(0, offset - buffer.length);
      const log = since > windowStart ? buffer.slice(Math.min(buffer.length, since - windowStart)) : buffer;

      // `running` so that a caller can tell a quiet dev server from an absent one without a second request.
      return json(response, 200, { ok: true, log, offset, running: Boolean(devServer), devExit });
    }

    if (url === '/files' && request.method === 'POST') return extractTar(request, response);
    if (url === '/files' && request.method === 'GET') return streamTar(response);
    if (url === '/exec' && request.method === 'POST') return exec(response, await readJson(request));
    if (url === '/dev/start' && request.method === 'POST') return startDevServer(response, await readJson(request));

    return json(response, 404, { ok: false });
  } catch (error) {
    return json(response, 500, { ok: false, error: String(error?.message ?? error) });
  }
});

// The raw upgrade, for the dev server's hot-reload socket. Without this the editor's preview loads once
// and then never updates, which looks exactly like the agent's edits not working.
server.on('upgrade', (request, socket, head) => {
  if (!authorized(request) || !request.url?.startsWith('/preview')) return socket.destroy();

  const path = devPath(request.url);
  const upstream = connect(DEV_PORT, '127.0.0.1', () => {
    const headers = { ...request.headers, host: `127.0.0.1:${DEV_PORT}` };
    delete headers.authorization;

    const lines = Object.entries(headers).map(([key, value]) => `${key}: ${value}`).join('\r\n');
    upstream.write(`${request.method} ${path} HTTP/1.1\r\n${lines}\r\n\r\n`);

    if (head?.length) upstream.write(head);

    socket.pipe(upstream);
    upstream.pipe(socket);
  });

  upstream.on('error', () => socket.destroy());
});

server.listen(PORT, '0.0.0.0', () =>
  console.log(`webly sandbox agent on :${PORT}, workspace ${WORKSPACE}, dev server :${DEV_PORT}`));

/**
 * Take the dev server down with us.
 *
 * In a container this is irrelevant — stopping the container takes everything in it. It matters for the local
 * provider, where the "sandbox" is a process on a developer's machine: the dev server is a child of this
 * process, and a signal delivered here does not reach it. Without this, every stopped sandbox leaves a
 * `next dev` holding its port and a few hundred megabytes, and they accumulate silently until something else
 * on the machine fails.
 *
 * Only the polite signals can be handled, so the provider also kills the whole process group. Two
 * mechanisms for one leak, because the expensive half of the failure is invisible.
 */
for (const signal of ['SIGTERM', 'SIGINT']) {
  process.on(signal, () => {
    if (devServer) {
      try {
        process.kill(-devServer.pid, 'SIGKILL');
      } catch {
        devServer.kill('SIGKILL');
      }
    }

    process.exit(0);
  });
}
