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

import { createServer } from 'node:http';
import { connect } from 'node:net';
import { spawn } from 'node:child_process';
import { mkdirSync, existsSync } from 'node:fs';

const PORT = Number(process.env.WEBLY_AGENT_PORT ?? 8080);
const TOKEN = process.env.WEBLY_AGENT_TOKEN ?? '';
const WORKSPACE = process.env.WEBLY_WORKSPACE ?? '/workspace';
const DEV_PORT = Number(process.env.WEBLY_DEV_PORT ?? 3000);

// What never travels back to git. Build output and dependencies are reproducible from the tree, and a
// stray .env would put a customer's secret in their history for ever.
const IGNORED = [
  '.git', 'node_modules', '.next', '.vercel', '.turbo', 'dist', 'out',
  '.env', '.env.local', '.env.*.local', '*.log', '.DS_Store', '.claude', '.opencode',
];

mkdirSync(WORKSPACE, { recursive: true });

let devServer = null;
let devReady = false;

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
 * Runs a command in the workspace and streams one NDJSON object per event:
 *   {"type":"stdout","text":"…"} | {"type":"stderr","text":"…"} | {"type":"exit","code":0}
 *
 * NDJSON rather than raw bytes because the caller needs to tell the two streams apart and know the exit
 * code, and rather than a WebSocket because a streaming HTTP response is something every HTTP client
 * already does — including the one in .NET, without a library.
 */
function exec(response, { command, args = [], env = {}, timeoutMs = 600_000, cwd }) {
  response.writeHead(200, { 'content-type': 'application/x-ndjson', 'cache-control': 'no-cache' });

  const child = spawn(command, args, {
    cwd: cwd ? `${WORKSPACE}/${cwd}` : WORKSPACE,
    env: { ...process.env, ...env },
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  const write = (type, text) => response.write(`${JSON.stringify({ type, text })}\n`);

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

function extractTar(request, response) {
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

function startDevServer(response, { command = 'npm', args = ['run', 'dev'], env = {} }) {
  if (devServer) return json(response, 200, { ok: true, alreadyRunning: true });

  devServer = spawn(command, args, {
    cwd: WORKSPACE,
    env: { ...process.env, ...env, PORT: String(DEV_PORT) },
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  // Kept in memory and served on /dev/log: a compile error in the dev server is the most useful thing
  // the editor can show, and it is the thing the next agent turn has to be told about.
  devServer.log = '';
  const record = text => {
    devServer.log = (devServer.log + text).slice(-16_000);
    if (/ready|started server|compiled/i.test(text)) devReady = true;
  };

  devServer.stdout.setEncoding('utf8');
  devServer.stderr.setEncoding('utf8');
  devServer.stdout.on('data', record);
  devServer.stderr.on('data', record);

  devServer.on('close', () => {
    devServer = null;
    devReady = false;
  });

  json(response, 200, { ok: true });
}

/**
 * Proxies /preview/* to the dev server, including the WebSocket upgrade that hot reload runs on.
 *
 * Hand-rolled over a TCP socket rather than with a proxy library, for the same zero-dependency reason —
 * and it is genuinely small: rewrite the path, pipe both ways, done.
 */
function proxy(request, response) {
  const path = request.url.slice('/preview'.length) || '/';

  const upstream = connect(DEV_PORT, '127.0.0.1', () => {
    const headers = { ...request.headers, host: `127.0.0.1:${DEV_PORT}` };
    delete headers.authorization;

    const lines = Object.entries(headers).map(([key, value]) => `${key}: ${value}`).join('\r\n');
    upstream.write(`${request.method} ${path} HTTP/1.1\r\n${lines}\r\n\r\n`);
    request.pipe(upstream);
  });

  upstream.pipe(response.socket ?? response);

  upstream.on('error', () => {
    if (!response.headersSent) json(response, 502, { ok: false, error: 'The dev server is not answering yet.' });
  });
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
        node: process.version,
      });
    }

    if (url === '/dev/log') return json(response, 200, { ok: true, log: devServer?.log ?? '' });

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

  const path = request.url.slice('/preview'.length) || '/';
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
