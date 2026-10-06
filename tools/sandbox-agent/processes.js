// How the sandbox agent starts, finds and stops programs — the one place that knows which operating system it is on.
//
// Everything else in this directory was written for Linux, which is where a sandbox normally runs: the image, and
// the local provider under bubblewrap. Windows is a developer's machine running the local provider unconfined
// (`Sandbox:Local:Confinement = none`), and three things there are different enough to break every turn:
//
// - **A program is often a `.cmd` file**, and Node refuses to spawn one without a shell. `npm`, `npx`, `opencode` and
//   `claude` are all npm wrappers of that kind, so `spawn('npm', …)` answered ENOENT and no site ever seeded. A shell
//   is not the answer: `cmd.exe` cuts an argument at its first newline, and the agent's prompt — the customer's own
//   message — is a multi-line argument. So a wrapper npm wrote is *read*, and the program it would have run is
//   started directly with the same arguments. A `.cmd` that is not one of those still goes through `cmd.exe`, quoted,
//   and refuses an argument it cannot carry rather than silently truncating it.
// - **There are no process groups or signals.** A dev server is `npm` → `next` → the server, and stopping it means
//   the whole tree: `taskkill /T` there, a group kill here.
// - **PATH is separated by `;`**, and a program is found by trying each `PATHEXT` extension.
//
// Two implementations behind one small surface, chosen once. The Linux one is exactly what index.js did before it
// was moved here, so the image and the confined local sandbox behave as they always have.

import { spawn, spawnSync } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { delimiter, dirname, extname, isAbsolute, join, resolve } from 'node:path';

const WINDOWS = process.platform === 'win32';

/** `directory` in front of an existing PATH, with this platform's separator. */
export function withPath(directory, path = '') {
  return path ? `${directory}${delimiter}${path}` : directory;
}

/** A program on PATH as a shell would find it, or null. On Windows that includes every PATHEXT extension. */
export function which(name, path = process.env.PATH ?? process.env.Path ?? '') {
  const extensions = WINDOWS && !extname(name)
    ? (process.env.PATHEXT ?? '.COM;.EXE;.BAT;.CMD').split(';').filter(Boolean).map(extension => extension.toLowerCase())
    : [''];

  const candidates = isAbsolute(name) || name.includes('/') || name.includes('\\')
    ? [resolve(name)]
    : path.split(delimiter).filter(Boolean).map(directory => join(directory, name));

  for (const candidate of candidates) {
    for (const extension of extensions) {
      const file = `${candidate}${extension}`;

      if (isFile(file)) return file;
    }
  }

  return null;
}

function isFile(file) {
  try {
    return statSync(file).isFile();
  } catch {
    return false;
  }
}

/**
 * Starts a program with these arguments, never through a shell where it can be avoided.
 *
 * `options` are `spawn`'s. `group` asks for a process that can later be stopped with everything it started — see
 * `killTree`. The returned child behaves like any other; when the command cannot be found it emits `error` with
 * ENOENT, exactly as `spawn` would, so callers have one failure path.
 */
export function run(command, args, { group = false, ...options } = {}) {
  if (!WINDOWS) {
    // Its own process group when asked, so it can be killed as a group on the way out: `npm run dev` spawns `next`,
    // which spawns the server, and signalling the npm process alone leaves the actual dev server running.
    return spawn(command, args, { ...options, detached: group });
  }

  const pathValue = options.env?.PATH ?? options.env?.Path ?? process.env.PATH ?? process.env.Path ?? '';
  const [file, fileArgs, extra] = resolveWindows(command, args, pathValue);

  // Never detached here: a detached child on Windows gets a console window of its own, and the tree kill below finds
  // it through its parent anyway. `windowsHide` stops every command of every turn flashing one up.
  return spawn(file, fileArgs, { ...options, ...extra, windowsHide: true });
}

/** Stops a child and everything it started. Synchronous, because it is also called on the way out of the process. */
export function killTree(child) {
  if (!child?.pid) return;

  if (WINDOWS) {
    // `/T` is the tree, `/F` is the kill rather than the request. A process that has already gone answers 128,
    // which is the outcome being asked for.
    spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true });
    return;
  }

  try {
    process.kill(-child.pid, 'SIGKILL');
  } catch {
    // Not a group leader (it was started without `group`), or already gone.
    try {
      child.kill('SIGKILL');
    } catch {
      // Gone.
    }
  }
}

/**
 * The `tar` this platform's archives should go through.
 *
 * On Windows it is the one Windows ships (bsdtar, in System32), named in full: Git's GNU tar is often on a developer's
 * PATH too, and it reads `C:\…` as a remote host called `C` — `tar -C C:\workspace` then fails with "Cannot connect
 * to C: resolve failed", which is a sentence about networking in answer to a question about a directory.
 */
export function tarCommand() {
  if (!WINDOWS) return 'tar';

  const system = join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'tar.exe');

  return existsSync(system) ? system : 'tar';
}

// -- Windows ----------------------------------------------------------------------------------------------------------

/**
 * What to spawn for `command`: `[file, args, extraSpawnOptions]`.
 *
 * An executable is started as itself. A `.cmd` or `.bat` that npm wrote is read and the program it names is started
 * instead — see the file comment for why a shell will not do. Anything else is left to `spawn`, which reports ENOENT
 * for a program that is not there.
 */
function resolveWindows(command, args, pathValue) {
  const found = which(command, pathValue);

  if (!found) return [command, args, {}];

  const extension = extname(found).toLowerCase();

  // `.JS` is in the default PATHEXT, where it means Windows Script Host — never what a command here wants.
  if (['.js', '.mjs', '.cjs'].includes(extension)) return [process.execPath, [found, ...args], {}];

  if (extension !== '.cmd' && extension !== '.bat') return [found, args, {}];

  const target = readNpmWrapper(found);

  if (target) return [target.program, [...target.args, ...args], {}];

  return viaCmd(found, args);
}

/**
 * The program an npm-written wrapper runs, or null when this is not one.
 *
 * Two shapes are in the wild, and both end in one line that runs a program with `%*`:
 *
 *   "%dp0%\node_modules\opencode-ai\bin\opencode.exe"   %*            (cmd-shim, for a native binary)
 *   "%_prog%"  "%dp0%\node_modules\some-cli\cli.js" %*                 (cmd-shim, for a node script)
 *   "%NODE_EXE%" "%NPM_CLI_JS%" %*                                     (npm's own npm.cmd and npx.cmd)
 *
 * Variables are resolved from the wrapper's own `SET "NAME=value"` lines, `%dp0%`/`%~dp0` is the wrapper's directory,
 * and a program that resolves to `node` — or to a `node.exe` that is not there, which is what cmd-shim falls back
 * from — is this very node. Anything that does not resolve to files that exist is not a shape this understands, and
 * the caller falls back to `cmd.exe`.
 */
function readNpmWrapper(file) {
  let text;

  try {
    text = readFileSync(file, 'utf8');
  } catch {
    return null;
  }

  const directory = dirname(file);
  const variables = { dp0: `${directory}\\`, '~dp0': `${directory}\\` };

  // First assignment wins: the later ones in npm.cmd are conditional overrides (a global prefix) that would need the
  // batch file's own logic to decide, and the first is the copy of npm that sits beside this node.
  for (const match of text.matchAll(/^\s*SET\s+"?([A-Za-z_][\w]*)=([^"\r\n]*)"?\s*$/gim)) {
    if (!(match[1] in variables)) variables[match[1]] = match[2];
  }

  const line = text.split(/\r?\n/).reverse().find(candidate => /%\*\s*$/.test(candidate) && candidate.includes('"'));

  if (!line) return null;

  const expand = value => value.replace(/%(~?\w+)%|%~dp0/g, (whole, name) =>
    whole === '%~dp0' ? variables['~dp0'] : (variables[name] ?? whole));

  const tokens = [...line.matchAll(/"([^"]*)"/g)].map(match => normalize(expand(expand(match[1]))));

  if (tokens.length === 0 || tokens.some(token => token.includes('%'))) return null;

  const [program, ...rest] = tokens;

  // `node "%~dp0..\screenshot.js" %*` — the unquoted `node` is not a token, so a script comes first. That is this
  // directory's own `bin/*.cmd`, and any wrapper shaped like it.
  if (/\.[cm]?js$/i.test(program)) return isFile(program) ? { program: process.execPath, args: tokens } : null;

  if (isNode(program)) {
    return rest.length > 0 && rest.every(isFile) ? { program: process.execPath, args: rest } : null;
  }

  return isFile(program) ? { program, args: rest } : null;
}

function normalize(path) {
  return path.replace(/\\\\+/g, '\\');
}

function isNode(program) {
  const name = program.split(/[\\/]/).pop()?.toLowerCase();

  return name === 'node' || name === 'node.exe';
}

/**
 * A batch file through `cmd.exe`, quoted the way cmd reads it.
 *
 * Each argument is wrapped for the C runtime's rules (backslashes before a quote doubled, the quote escaped), then
 * every character cmd treats specially is caret-escaped so the outer parse leaves it alone. A newline cannot be
 * carried at all — cmd ends the command there — so it is refused with a sentence rather than truncated into a
 * different command.
 */
function viaCmd(file, args) {
  const unsafe = args.find(arg => /[\r\n]/.test(arg));

  if (unsafe !== undefined) {
    throw new Error(`${file} is a batch file, and an argument containing a line break cannot be passed through cmd.exe`);
  }

  const meta = /([()\][%!^"`<>&|;, *?])/g;
  const quote = arg => `"${String(arg).replace(/(\\*)"/g, '$1$1\\"').replace(/(\\*)$/, '$1$1')}"`.replace(meta, '^$1');
  const line = [file.replace(meta, '^$1'), ...args.map(quote)].join(' ');

  return [process.env.ComSpec ?? 'cmd.exe', ['/d', '/s', '/c', `"${line}"`], { windowsVerbatimArguments: true }];
}
