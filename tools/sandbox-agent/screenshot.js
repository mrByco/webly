// webly-screenshot [path] [--desktop | --phone]
//
// A full-page screenshot of the site being edited, for the agent to look at before it finishes. `AGENTS.md`
// asks for it after any change to how a page looks, because a page that compiles and type-checks can still have
// a card cut off on a phone, a headline over its own image, or white text on a pale background — and none of
// that is in any log. The agent reads the PNG with its own file tool; both CLIs Webly runs can see images.
//
// Writes into the workspace's `.webly/screenshots`, which the sandbox agent never lets into a commit — inside the
// workspace because a CLI reading a file outside its project asks permission first, and in a headless run nobody
// is there to give it. And it prints what went wrong while loading — a request that failed, a script that threw — because a 404 on a
// stylesheet is easier to read in a sentence than to spot in a picture.
//
// Zero dependencies, like the rest of this directory: Chromium over its own DevTools protocol, with node's
// built-in WebSocket (node 22 or later). The browser is whatever WEBLY_BROWSER names, or the first Chromium on
// PATH; without one this says so and exits 3, and the agent carries on without looking.

import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const VIEWPORTS = {
  desktop: { width: 1280, height: 800, mobile: false },
  phone: { width: 390, height: 844, mobile: true },
};

/** Long enough for any real page; a cap, because an image is paid for by the pixel when a model reads it. */
const MAX_HEIGHT = 6000;

const args = process.argv.slice(2);
const path = args.find(arg => !arg.startsWith('--')) ?? '/';
const only = Object.keys(VIEWPORTS).find(name => args.includes(`--${name}`));
const base = process.env.WEBLY_DEV_URL;

function unavailable(reason) {
  console.log(`Screenshots are not available here: ${reason}. Carry on without one.`);
  process.exit(3);
}

function findBrowser() {
  if (process.env.WEBLY_BROWSER) return existsSync(process.env.WEBLY_BROWSER) ? process.env.WEBLY_BROWSER : null;

  for (const name of ['chromium', 'chromium-browser', 'chrome-headless-shell', 'google-chrome', 'google-chrome-stable', 'chrome']) {
    for (const directory of (process.env.PATH ?? '').split(':')) {
      if (directory && existsSync(`${directory}/${name}`)) return `${directory}/${name}`;
    }
  }

  return null;
}

if (!base) unavailable('the dev server address is not known');
if (typeof WebSocket === 'undefined') unavailable(`node ${process.version} has no WebSocket`);

const browserPath = findBrowser() ?? unavailable('there is no browser in this sandbox');
const profile = mkdtempSync(join(tmpdir(), 'webly-browser-'));

// --no-sandbox because the browser's own sandbox is built from the same namespaces this one is already inside;
// --no-proxy-server because the only thing it loads is the dev server, on loopback.
const browser = spawn(browserPath, [
  '--headless=new', '--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage', '--no-first-run',
  '--no-default-browser-check', '--hide-scrollbars', '--mute-audio', '--no-proxy-server',
  `--user-data-dir=${profile}`, '--remote-debugging-port=0', 'about:blank',
], { stdio: ['ignore', 'ignore', 'pipe'] });

function finish(code) {
  try {
    browser.kill('SIGKILL');
  } catch {
    // Gone already.
  }

  rmSync(profile, { recursive: true, force: true });
  process.exit(code);
}

setTimeout(() => {
  console.log('The screenshot took longer than a minute and was abandoned.');
  finish(1);
}, 60_000).unref();

const endpoint = await new Promise((resolve, reject) => {
  let output = '';

  browser.stderr.setEncoding('utf8');
  browser.stderr.on('data', text => {
    output += text;
    const match = output.match(/DevTools listening on (ws:\/\/\S+)/);
    if (match) resolve(match[1]);
  });
  browser.on('exit', () => reject(new Error(output.trim().split('\n').slice(-3).join(' '))));
}).catch(error => unavailable(`the browser did not start (${error.message})`));

// ---------------------------------------------------------------------------------------------
// The DevTools protocol: numbered calls, and events by name.
// ---------------------------------------------------------------------------------------------

const socket = new WebSocket(endpoint);
await new Promise((resolve, reject) => {
  socket.onopen = resolve;
  socket.onerror = () => reject(new Error('could not connect to the browser'));
});

let nextId = 1;
const pending = new Map();
const listeners = new Set();

socket.onmessage = ({ data }) => {
  const message = JSON.parse(data);

  if (message.id && pending.has(message.id)) {
    const { resolve, reject } = pending.get(message.id);
    pending.delete(message.id);
    return message.error ? reject(new Error(message.error.message)) : resolve(message.result);
  }

  for (const listener of listeners) listener(message);
};

function call(method, params = {}, sessionId) {
  const id = nextId++;

  socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));

  return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
}

async function capture(name, viewport) {
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
  const on = (method, handle) => listeners.add(message =>
    message.sessionId === sessionId && message.method === method && handle(message.params));

  const problems = [];
  const inflight = new Set();
  let documentStatus = 0;
  let loaded = false;
  let lastActivity = Date.now();

  on('Network.requestWillBeSent', ({ requestId }) => { inflight.add(requestId); lastActivity = Date.now(); });
  on('Network.loadingFinished', ({ requestId }) => { inflight.delete(requestId); lastActivity = Date.now(); });
  on('Network.loadingFailed', ({ requestId, errorText, canceled }) => {
    inflight.delete(requestId);
    lastActivity = Date.now();
    if (!canceled) problems.push(`a request failed: ${errorText}`);
  });
  on('Network.responseReceived', ({ type, response }) => {
    if (type === 'Document' && !documentStatus) documentStatus = response.status;
    else if (response.status >= 400) problems.push(`${response.status} for ${new URL(response.url).pathname}`);
  });
  on('Runtime.exceptionThrown', ({ exceptionDetails }) =>
    problems.push(`a script threw: ${(exceptionDetails.exception?.description ?? exceptionDetails.text).split('\n')[0]}`));
  on('Runtime.consoleAPICalled', ({ type, args: values }) => {
    if (type === 'error') problems.push(`console error: ${values.map(value => value.value ?? value.description ?? '').join(' ').slice(0, 200)}`);
  });
  on('Page.loadEventFired', () => { loaded = true; });

  await call('Page.enable', {}, sessionId);
  await call('Network.enable', {}, sessionId);
  await call('Runtime.enable', {}, sessionId);
  await call('Emulation.setDeviceMetricsOverride', {
    width: viewport.width, height: viewport.height, deviceScaleFactor: 1, mobile: viewport.mobile,
  }, sessionId);

  const url = `${base}${path.startsWith('/') ? path : `/${path}`}`;
  await call('Page.navigate', { url }, sessionId);

  // Loaded, then quiet: a dev server compiles a page on its first request, and the first load is not the last
  // request — fonts and images follow it.
  const started = Date.now();
  while (Date.now() - started < 45_000 && !(loaded && inflight.size === 0 && Date.now() - lastActivity > 500)) {
    await new Promise(resolve => setTimeout(resolve, 100));
  }

  await call('Runtime.evaluate', { expression: 'document.fonts.ready.then(() => true)', awaitPromise: true }, sessionId);

  const metrics = await call('Page.getLayoutMetrics', {}, sessionId);
  const fullHeight = Math.ceil((metrics.cssContentSize ?? metrics.contentSize).height);
  const height = Math.min(Math.max(fullHeight, viewport.height), MAX_HEIGHT);

  const { data } = await call('Page.captureScreenshot', {
    format: 'png',
    captureBeyondViewport: true,
    clip: { x: 0, y: 0, width: viewport.width, height, scale: 1 },
  }, sessionId);

  const directory = join(process.env.WEBLY_WORKSPACE ?? process.cwd(), '.webly', 'screenshots');
  mkdirSync(directory, { recursive: true });

  const slug = path.replace(/^\/+|\/+$/g, '').replace(/[^a-zA-Z0-9]+/g, '-') || 'home';
  const file = join(directory, `${slug}-${name}.png`);
  writeFileSync(file, Buffer.from(data, 'base64'));

  await call('Target.closeTarget', { targetId });

  console.log(`Saved ${file} — ${name}, ${viewport.width}×${height}px${fullHeight > MAX_HEIGHT ? ` (the page is ${fullHeight}px; cut at ${MAX_HEIGHT})` : ''}.`);

  if (documentStatus >= 400) console.log(`  The page itself answered ${documentStatus}.`);

  const unique = [...new Set(problems)];
  console.log(unique.length ? `  Problems while loading:\n${unique.map(problem => `   - ${problem}`).join('\n')}` : '  No failed requests and no script errors.');
}

try {
  for (const name of only ? [only] : Object.keys(VIEWPORTS)) await capture(name, VIEWPORTS[name]);

  console.log('Open each image with your file-reading tool and look at it before you finish.');
  finish(0);
} catch (error) {
  console.log(`The screenshot failed: ${error.message}`);
  finish(1);
}
