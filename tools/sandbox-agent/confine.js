#!/usr/bin/env node
// Webly sandbox agent, confined.
//
// The same agent as index.js, started inside a bubblewrap sandbox of its own — which is what a container is to
// the Docker provider, built from the kernel's own pieces on a machine that has no container runtime. The local
// provider runs this instead of index.js unless Sandbox:Local:Confinement is "none".
//
// Inside, the agent, every command it runs and the dev server share one sandbox, and the whole of it sees:
//
//   - the workspace, writable, and a private home beside it, writable (the agent CLI's sessions);
//   - the system's /usr, the parts of /etc that name resolution, certificates and fonts need, and the toolchain
//     — node, git, the agent CLIs, a browser and the Playwright MCP server that drives it — all read-only;
//   - no network at all except loopback, and one door out: an HTTP proxy that admits only the hosts on
//     WEBLY_EGRESS_ALLOW (the model's API, the npm registry, the font host) and refuses everything else with a
//     sentence saying so.
//
// Nothing else of the machine exists for it: no home directory, no Webly checkout, no other site's workspace, no
// site repositories, and no environment it was not given. It exists because the first real agent through the
// local provider went looking outside its workspace, found a site's bare repository and committed to it.
//
// One sandbox rather than one per command, because the network has to be shared: the agent's commands reach the
// dev server on loopback, and so does the screenshot browser. Separate namespaces would each have had their own.
//
// Outside, this process does three things and nothing else: it starts bubblewrap, it forwards Webly's TCP
// connection to the agent's Unix socket (the agent has no network to listen on), and it runs the egress proxy.
//
// Zero dependencies, like index.js.

import { spawn } from 'node:child_process';
import { createServer, connect } from 'node:net';
import { existsSync, lstatSync, mkdtempSync, mkdirSync, readlinkSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const AGENT_DIR = dirname(fileURLToPath(import.meta.url));
const WORKSPACE = process.env.WEBLY_WORKSPACE ?? '';
const PORT = Number(process.env.WEBLY_AGENT_PORT ?? 8080);

if (!WORKSPACE) {
  console.error('confine.js: WEBLY_WORKSPACE is required');
  process.exit(2);
}

/**
 * Where this directory appears inside: the image's own path for it. Mounted there rather than at its path in a
 * checkout, because a mount brings its parent directories with it, and an agent listing `/home/someone/webly`
 * and finding `tools` there is an agent with a reason to go looking.
 */
const INSIDE_AGENT_DIR = '/opt/webly-agent';

/** The agent CLI's home: beside the workspace, never inside it, for the reason the pid files are. */
const HOME = `${WORKSPACE}.home`;

/**
 * Where the two Unix sockets live. A short directory of its own rather than beside the workspace, because a
 * socket path has a limit of about a hundred bytes and a workspace's path is most of that already. Inside, it is
 * always /run/webly.
 */
const SOCKETS = mkdtempSync(join(tmpdir(), 'webly-'));
const INSIDE_SOCKETS = '/run/webly';

/** Exact hosts, or `*.example.com` for a domain's subdomains. Lower-cased; empty means nothing gets out. */
const ALLOWED = (process.env.WEBLY_EGRESS_ALLOW ?? '')
  .split(',').map(entry => entry.trim().toLowerCase()).filter(Boolean);

function allowed(host) {
  const name = host.toLowerCase().replace(/\.$/, '');

  return ALLOWED.some(entry => entry.startsWith('*.')
    ? name.endsWith(entry.slice(1)) || name === entry.slice(2)
    : name === entry);
}

// ---------------------------------------------------------------------------------------------
// The sandbox
// ---------------------------------------------------------------------------------------------

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
 * The screenshot browser: the one configured, or the first Chromium on PATH. Found here rather than only by
 * `webly-screenshot`, because inside the sandbox a browser nobody mounted does not exist.
 */
const BROWSER = process.env.WEBLY_BROWSER
  || ['chromium', 'chromium-browser', 'chrome-headless-shell', 'google-chrome', 'google-chrome-stable'].map(which).find(Boolean)
  || '';

/** What the agent may know of this process's environment, by name. */
const PASSED_ENV = new Set([
  'PATH', 'LANG', 'LANGUAGE', 'TZ', 'TERM',
  'NODE_EXTRA_CA_CERTS', 'SSL_CERT_FILE', 'SSL_CERT_DIR', 'REQUESTS_CA_BUNDLE', 'CURL_CA_BUNDLE', 'GIT_SSL_CAINFO',
  'WEBLY_AGENT_TOKEN', 'WEBLY_DEV_PORT',
  // Whatever the provider declared the workload's own (SandboxSpec.Environment).
  ...(process.env.WEBLY_WORKLOAD_ENV ?? '').split(',').filter(Boolean),
]);

function bubblewrapArguments() {
  const args = [
    '--die-with-parent', '--new-session',
    // No network but loopback, its own process table (so a killed sandbox takes the dev server's grandchildren
    // with it), and nothing else shared.
    '--unshare-net', '--unshare-pid', '--unshare-ipc', '--unshare-uts', '--unshare-cgroup-try',
    '--ro-bind', '/usr', '/usr',
  ];

  // Merged-/usr systems make these symlinks, and a symlink is recreated rather than bound so it keeps pointing
  // into the read-only /usr above.
  for (const path of ['/bin', '/sbin', '/lib', '/lib32', '/lib64', '/libx32']) {
    if (!existsSync(path)) continue;

    args.push(...(lstatSync(path).isSymbolicLink() ? ['--symlink', readlinkSync(path), path] : ['--ro-bind', path, path]));
  }

  // The parts of /etc a program reads to resolve a name, trust a certificate, draw a font and know who it is —
  // and nothing else of it, because /etc is also where a machine keeps things that are nobody's business.
  for (const path of ['resolv.conf', 'hosts', 'nsswitch.conf', 'host.conf', 'gai.conf', 'ssl', 'ca-certificates',
    'pki', 'passwd', 'group', 'localtime', 'alternatives', 'fonts', 'ld.so.cache', 'ld.so.conf', 'ld.so.conf.d']) {
    args.push('--ro-bind-try', `/etc/${path}`, `/etc/${path}`);
  }

  // A fresh /tmp first, so that anything bound below that lives under the host's /tmp — a workspace, a toolchain
  // — appears on top of the empty one rather than being hidden by it.
  args.push('--proc', '/proc', '--dev', '/dev', '--tmpfs', '/tmp');

  const toolchain = new Set([installRoot(realpathSync(process.execPath))]);

  for (const tool of ['npm', 'npx', 'git', 'claude', 'opencode', 'vercel', 'playwright-mcp']) {
    const found = which(tool);

    if (!found) continue;

    toolchain.add(dirname(found));
    toolchain.add(installRoot(realpathSync(found)));
  }

  // The screenshot browser, if this machine has one: the directory it runs from, since Chromium loads its
  // resources from beside the binary.
  if (BROWSER && existsSync(BROWSER)) {
    toolchain.add(dirname(BROWSER));
    toolchain.add(dirname(realpathSync(BROWSER)));
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

  args.push(
    '--ro-bind', AGENT_DIR, INSIDE_AGENT_DIR,
    '--bind', WORKSPACE, WORKSPACE,
    '--bind', HOME, HOME,
    '--bind', SOCKETS, INSIDE_SOCKETS,
    '--chdir', WORKSPACE);

  return args;
}

function agentEnvironment() {
  const passed = Object.fromEntries(Object.entries(process.env)
    .filter(([name]) => PASSED_ENV.has(name) || name.startsWith('LC_')));

  return {
    ...passed,
    HOME,
    TMPDIR: '/tmp',
    ...(BROWSER ? { WEBLY_BROWSER: BROWSER } : {}),
    WEBLY_WORKSPACE: WORKSPACE,
    WEBLY_AGENT_SOCKET: `${INSIDE_SOCKETS}/agent.sock`,
    WEBLY_EGRESS_SOCKET: `${INSIDE_SOCKETS}/egress.sock`,
    // Phoning home is traffic the allow-list would refuse anyway; saying so up front keeps the logs quiet.
    NEXT_TELEMETRY_DISABLED: '1',
    CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: '1',
    OPENCODE_DISABLE_AUTOUPDATE: '1',
  };
}

// ---------------------------------------------------------------------------------------------
// The egress proxy: CONNECT (and plain HTTP) to allow-listed hosts, through this machine's own proxy if it has
// one, and a 403 with a sentence for everything else.
// ---------------------------------------------------------------------------------------------

/** This machine's own way out, if it has to use one. Inside the sandbox there is no route to it. */
const UPSTREAM = (() => {
  const value = process.env.HTTPS_PROXY ?? process.env.https_proxy ?? process.env.HTTP_PROXY ?? process.env.http_proxy;

  if (!value) return null;

  const url = new URL(value);

  return {
    host: url.hostname,
    port: Number(url.port || 80),
    authorization: url.username
      ? `Basic ${Buffer.from(`${decodeURIComponent(url.username)}:${decodeURIComponent(url.password)}`).toString('base64')}`
      : null,
  };
})();

/** Reads up to the end of an HTTP head, then hands over the head and whatever arrived after it. */
function readHead(socket, then) {
  let buffer = Buffer.alloc(0);

  const onData = chunk => {
    buffer = Buffer.concat([buffer, chunk]);
    const end = buffer.indexOf('\r\n\r\n');

    if (end < 0) {
      if (buffer.length > 64 * 1024) socket.destroy();
      return;
    }

    socket.off('data', onData);
    then(buffer.subarray(0, end).toString('latin1'), buffer.subarray(end + 4));
  };

  socket.on('data', onData);
}

/** A TCP connection to host:port — direct, or tunnelled through the upstream proxy. */
function open(host, port, then, fail) {
  if (!UPSTREAM) {
    const direct = connect(port, host, () => then(direct));
    direct.on('error', fail);
    return;
  }

  const tunnel = connect(UPSTREAM.port, UPSTREAM.host, () => {
    const authorization = UPSTREAM.authorization ? `Proxy-Authorization: ${UPSTREAM.authorization}\r\n` : '';
    tunnel.write(`CONNECT ${host}:${port} HTTP/1.1\r\nHost: ${host}:${port}\r\n${authorization}\r\n`);

    readHead(tunnel, (head, rest) => {
      if (!/^HTTP\/1\.[01] 200/.test(head)) {
        tunnel.destroy();
        return fail(new Error(head.split('\r\n')[0]));
      }

      if (rest.length) tunnel.unshift(rest);
      then(tunnel);
    });
  });

  tunnel.on('error', fail);
}

function refuse(client, host) {
  console.error(`egress: refused ${host}`);

  const body = `Webly's sandbox does not connect to ${host}. Sites are edited with the network limited to: `
    + `${ALLOWED.join(', ') || 'nothing'}.\n`;

  client.end(`HTTP/1.1 403 Forbidden\r\ncontent-type: text/plain\r\nx-webly-egress: refused\r\n`
    + `content-length: ${Buffer.byteLength(body)}\r\nconnection: close\r\n\r\n${body}`);
}

const egress = createServer(client => {
  client.on('error', () => {});

  readHead(client, (head, rest) => {
    const [requestLine, ...headerLines] = head.split('\r\n');
    const [method, target] = requestLine.split(' ');

    if (method === 'CONNECT') {
      const separator = target.lastIndexOf(':');
      const host = target.slice(0, separator).replace(/^\[|\]$/g, '');
      const port = Number(target.slice(separator + 1)) || 443;

      if (!allowed(host)) return refuse(client, host);

      return open(host, port, upstream => {
        client.write('HTTP/1.1 200 Connection established\r\n\r\n');
        if (rest.length) upstream.write(rest);
        client.pipe(upstream);
        upstream.pipe(client);
        upstream.on('error', () => client.destroy());
      }, () => client.end('HTTP/1.1 502 Bad Gateway\r\nconnection: close\r\n\r\n'));
    }

    // Plain HTTP, in absolute form. Rare — everything worth reaching is HTTPS — and handled so that it is refused
    // or passed for the same reason rather than failing differently.
    let url;

    try {
      url = new URL(target);
    } catch {
      return client.end('HTTP/1.1 400 Bad Request\r\nconnection: close\r\n\r\n');
    }

    if (!allowed(url.hostname)) return refuse(client, url.hostname);

    return open(url.hostname, Number(url.port || 80), upstream => {
      // Through an upstream proxy the absolute form is what it expects; straight to the origin it is the path.
      const line = UPSTREAM ? requestLine : `${method} ${url.pathname}${url.search} ${requestLine.split(' ')[2]}`;
      const headers = headerLines.filter(header => !/^proxy-/i.test(header));
      const authorization = UPSTREAM?.authorization ? [`Proxy-Authorization: ${UPSTREAM.authorization}`] : [];

      upstream.write(`${[line, ...headers, ...authorization].join('\r\n')}\r\n\r\n`);
      if (rest.length) upstream.write(rest);
      client.pipe(upstream);
      upstream.pipe(client);
      upstream.on('error', () => client.destroy());
    }, () => client.end('HTTP/1.1 502 Bad Gateway\r\nconnection: close\r\n\r\n'));
  });
});

// ---------------------------------------------------------------------------------------------
// The door in: Webly's TCP connection, forwarded byte for byte to the agent's socket. Byte for byte is what
// makes a WebSocket upgrade — the preview's hot reload — work with no code of its own.
// ---------------------------------------------------------------------------------------------

const front = createServer(client => {
  const agent = connect(join(SOCKETS, 'agent.sock'));

  client.pipe(agent);
  agent.pipe(client);
  agent.on('error', () => client.destroy());
  client.on('error', () => agent.destroy());
});

// ---------------------------------------------------------------------------------------------

mkdirSync(HOME, { recursive: true });

let sandbox = null;

function cleanUp() {
  try {
    rmSync(SOCKETS, { recursive: true, force: true });
  } catch {
    // A directory in /tmp holding two dead sockets is not worth failing over.
  }
}

egress.listen(join(SOCKETS, 'egress.sock'), () => {
  sandbox = spawn('bwrap', [...bubblewrapArguments(), '--', process.execPath, `${INSIDE_AGENT_DIR}/index.js`], {
    env: agentEnvironment(),
    stdio: ['ignore', 'inherit', 'inherit'],
  });

  sandbox.on('error', error => {
    console.error(`confine.js: could not start bubblewrap: ${error.message}`);
    cleanUp();
    process.exit(127);
  });

  sandbox.on('exit', (code, signal) => {
    cleanUp();
    process.exit(code ?? (signal ? 1 : 0));
  });

  front.listen(PORT, '127.0.0.1', () =>
    console.log(`webly sandbox agent (confined) on :${PORT}, workspace ${WORKSPACE}, egress to ${ALLOWED.join(', ') || 'nothing'}`));
});

for (const signal of ['SIGTERM', 'SIGINT']) {
  process.on(signal, () => {
    // --die-with-parent takes everything inside down when this process goes; the signal is the polite version.
    sandbox?.kill('SIGTERM');
    cleanUp();
    process.exit(0);
  });
}
