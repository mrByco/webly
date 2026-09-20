// The client side of the sandbox contract, in node — the same calls SandboxAgentClient makes from .NET.
//
// Paired with git-store.mjs: together they let the whole product loop run in an environment with no .NET
// SDK. See tools/e2e/README.md.

import { spawn } from 'node:child_process';
import { mkdirSync, rmSync, createWriteStream } from 'node:fs';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { randomBytes } from 'node:crypto';
import { once } from 'node:events';

const AGENT = new URL('../sandbox-agent/index.js', import.meta.url).pathname;

/**
 * Starts the sandbox agent as a local child process.
 *
 * This is exactly what LocalSandboxProvider does on the .NET side, and for the same reason: there is no
 * Docker daemon in every environment, and the contract does not care what the machine is. It is NOT
 * isolation — the workspace is a directory on this host and the agent runs as this user.
 */
export async function startSandbox({ port = 0, devPort = 0 } = {}) {
  const workspace = mkdtempSync(join(tmpdir(), 'webly-workspace-'));
  const token = randomBytes(24).toString('hex');

  const agentPort = port || (await freePort());
  const devServerPort = devPort || (await freePort());

  const child = spawn(process.execPath, [AGENT], {
    env: {
      ...process.env,
      WEBLY_WORKSPACE: workspace,
      WEBLY_AGENT_PORT: String(agentPort),
      WEBLY_AGENT_TOKEN: token,
      WEBLY_DEV_PORT: String(devServerPort),
    },
    stdio: ['ignore', 'pipe', 'pipe'],
    // Its own process group, so disposing can kill the group. The dev server is a *grandchild* — the agent
    // spawns it — and signalling only the agent leaves `next dev` running, holding its port and a few hundred
    // megabytes, once per run. Eight of them accumulated before this was noticed.
    // LocalSandboxProvider has the same problem and solves it the same way, with Kill(entireProcessTree: true).
    detached: true,
  });

  let log = '';
  child.stdout.setEncoding('utf8');
  child.stderr.setEncoding('utf8');
  child.stdout.on('data', text => (log += text));
  child.stderr.on('data', text => (log += text));

  const sandbox = {
    url: `http://127.0.0.1:${agentPort}/`,
    token,
    workspace,
    devPort: devServerPort,
    get log() { return log; },
    async dispose() {
      // The negative pid is the process group: the agent and whatever it started.
      try {
        process.kill(-child.pid, 'SIGKILL');
      } catch {
        // Already gone.
      }

      // Waited for before the directory goes. A dev server that is still alive when its workspace disappears
      // does not exit — it spins at 100% of a core retrying files that are not there any more, indefinitely.
      // Several of those is what a wedged machine looks like, and nothing says why.
      await Promise.race([
        once(child, 'exit'),
        new Promise(resolve => setTimeout(resolve, 5000)),
      ]).catch(() => undefined);

      rmSync(workspace, { recursive: true, force: true });
    },
  };

  await waitFor(() => health(sandbox), 20_000, 'the sandbox agent never became reachable');

  return sandbox;
}

function freePort() {
  return new Promise((resolve, reject) => {
    import('node:net').then(({ createServer }) => {
      const server = createServer();
      server.on('error', reject);
      server.listen(0, '127.0.0.1', () => {
        const { port } = server.address();
        server.close(() => resolve(port));
      });
    });
  });
}

function request(sandbox, path, { method = 'GET', body, headers = {}, signal } = {}) {
  return fetch(new URL(path, sandbox.url), {
    method,
    body,
    signal,
    duplex: body ? 'half' : undefined,
    headers: { authorization: `Bearer ${sandbox.token}`, ...headers },
  });
}

export async function health(sandbox) {
  const response = await request(sandbox, 'health');
  if (!response.ok) throw new Error(`health answered ${response.status}`);

  return response.json();
}

/** POST /files — the tree in, as a gzipped tar, replacing what is there. */
export async function writeTree(sandbox, files) {
  const staging = mkdtempSync(join(tmpdir(), 'webly-tree-'));

  try {
    for (const file of files) {
      const target = join(staging, file.path);
      mkdirSync(dirname(target), { recursive: true });
      await writeFile(target, file.content);
    }

    const archive = join(staging, '..', `tree-${randomBytes(6).toString('hex')}.tgz`);
    await run('tar', ['-c', '-z', '-C', staging, '-f', archive, '.']);

    const { readFileSync } = await import('node:fs');
    const response = await request(sandbox, 'files', {
      method: 'POST',
      body: readFileSync(archive),
      headers: { 'content-type': 'application/gzip' },
    });

    rmSync(archive, { force: true });

    if (!response.ok) throw new Error(`writeTree answered ${response.status}: ${await response.text()}`);
  } finally {
    rmSync(staging, { recursive: true, force: true });
  }
}

/** GET /files — the tree back out, with the agent's own excludes already applied. */
export async function readTree(sandbox) {
  const response = await request(sandbox, 'files');
  if (!response.ok) throw new Error(`readTree answered ${response.status}`);

  const staging = mkdtempSync(join(tmpdir(), 'webly-out-'));
  const archive = join(staging, 'tree.tgz');

  const { writeFileSync, readdirSync, statSync, readFileSync } = await import('node:fs');
  writeFileSync(archive, Buffer.from(await response.arrayBuffer()));
  await run('tar', ['-x', '-z', '-C', staging, '-f', archive]);
  rmSync(archive, { force: true });

  const files = [];

  const walk = directory => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const full = join(directory, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile()) files.push({ path: full.slice(staging.length + 1).replace(/^\.\//, ''), content: readFileSync(full) });
    }
  };

  walk(staging);
  rmSync(staging, { recursive: true, force: true });

  return files.sort((a, b) => a.path.localeCompare(b.path));
}

/**
 * POST /exec — a command, streamed back as NDJSON. `onLine` sees each parsed object as it arrives, which is
 * what makes the agent's output a live stream rather than a wait.
 */
export async function exec(sandbox, { command, args = [], env = {}, timeoutMs = 600_000, cwd }, onLine) {
  const response = await request(sandbox, 'exec', {
    method: 'POST',
    body: JSON.stringify({ command, args, env, timeoutMs, cwd }),
    headers: { 'content-type': 'application/json' },
  });

  if (!response.ok) throw new Error(`exec answered ${response.status}: ${await response.text()}`);

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let exitCode = -1;
  const output = [];

  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    let newline;
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline).trim();
      buffer = buffer.slice(newline + 1);
      if (!line) continue;

      let event;
      try {
        event = JSON.parse(line);
      } catch {
        continue;
      }

      if (event.type === 'exit') exitCode = event.code;
      else output.push(event.text ?? '');

      if (onLine) await onLine(event);
    }
  }

  return { exitCode, output: output.join(''), succeeded: exitCode === 0 };
}

export async function startDevServer(sandbox, body = {}) {
  const response = await request(sandbox, 'dev/start', {
    method: 'POST',
    body: JSON.stringify(body),
    headers: { 'content-type': 'application/json' },
  });

  if (!response.ok) throw new Error(`dev/start answered ${response.status}`);

  return response.json();
}

/**
 * The dev server's output, and how far through it that reached.
 *
 * `since` is the reason this returns a pair rather than a string: the log is cumulative, so a caller asking
 * "did anything break just now" has to say when "now" started. AgentTurnService does exactly this.
 */
export async function devLog(sandbox, since = 0) {
  const response = await request(sandbox, since > 0 ? `dev/log?since=${since}` : 'dev/log');
  const body = await response.json();

  return { text: body.log ?? '', offset: body.offset ?? 0 };
}

/**
 * The preview, as the .NET proxy reaches it: through the agent, with the bearer token.
 *
 * Bounded, because a dev server compiling a cold page under load can hold a request open for minutes and
 * neither `fetch` nor the agent's proxy has a timeout of its own. Unbounded, one hung attempt blocks the
 * retry loop below for ever and the run never fails, it just stops — which is exactly what happened once.
 * In production YARP's `ActivityTimeout` is what bounds this.
 */
export async function preview(sandbox, path = '', { timeoutMs = 20_000 } = {}) {
  return request(sandbox, `preview/${path}`, { signal: AbortSignal.timeout(timeoutMs) });
}

/**
 * Retries a probe until it returns something truthy. The deadline is checked between attempts, so every probe
 * handed to this has to bound itself — see `preview` above.
 */
export async function waitFor(probe, timeoutMs, message) {
  const deadline = Date.now() + timeoutMs;
  let last;

  while (Date.now() < deadline) {
    try {
      const value = await probe();
      if (value) return value;
    } catch (error) {
      last = error;
    }

    await new Promise(resolve => setTimeout(resolve, 300));
  }

  throw new Error(`${message}${last ? `: ${last.message}` : ''}`);
}

function writeFile(path, content) {
  return new Promise((resolve, reject) => {
    const stream = createWriteStream(path);
    stream.on('error', reject);
    stream.on('finish', resolve);
    stream.end(content);
  });
}

export function run(command, args, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { stdio: ['ignore', 'pipe', 'pipe'], ...options });
    let out = '';
    let err = '';
    child.stdout?.setEncoding('utf8');
    child.stderr?.setEncoding('utf8');
    child.stdout?.on('data', text => (out += text));
    child.stderr?.on('data', text => (err += text));
    child.on('error', reject);
    child.on('close', code => (code === 0 ? resolve(out) : reject(new Error(`${command} failed (${code}): ${err || out}`))));
  });
}

export { once };
