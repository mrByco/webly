// webly-browser-mcp
//
// The browser tools: the Playwright MCP server, started for this sandbox. The second way for the agent to look at
// the site beside `webly-screenshot`, and the one for when seeing a page means doing something to it first —
// opening the phone menu, unfolding a section, following a link. The agent picks whichever it needs; both agent
// CLIs start this themselves, from the MCP configuration Webly hands them (ClaudeCodeAgent, OpenCodeAgent).
//
// The server as it ships, with five things changed, each because a sandbox is not a developer's machine:
//
//  - The browser and the flags are decided here. The same Chromium `webly-screenshot` uses, headless, without its
//    own sandbox (that is built from the namespaces this one is already inside), with an in-memory profile.
//  - Everything it writes goes under `.webly/browser`, which the sandbox agent never lets into a commit. Left to
//    itself it writes `.playwright-mcp/` into the working directory and resolves a `filename` the model chose
//    against it — and the working directory is the website, so a screenshot called `home.png` would have been
//    published. Rewritten here rather than asked for in AGENTS.md, because a rule a model can forget is not the
//    same thing as a path it cannot reach.
//  - A screenshot always comes back as an image. Given a `filename`, the server saves the picture and sends the
//    model only a sentence saying where — so the first real turn that took one named it, never saw it, and worked
//    out the colour of a button from computed styles instead. A screenshot is for looking at, so the parameter is
//    taken off the tool, and dropped if a model passes it anyway; the picture is still saved, under a name of the
//    server's choosing, in the same place.
//  - `browser_navigate` says where the site is, and takes a path. The dev server is at a port and a base path no
//    model could guess, and a Claude turn cannot `echo $WEBLY_DEV_URL` to find out: two commands are pre-approved
//    there and that is not one of them.
//  - `browser_run_code_unsafe` is withheld. Its own description calls it RCE-equivalent — arbitrary JavaScript in
//    the server's process — which would make this a shell for a model that is deliberately not given one.
//
// It does that by sitting between the CLI and the server on the newline-delimited JSON-RPC that MCP's stdio
// transport is, and rewriting those messages; everything else passes through untouched, both ways. Each of the
// five is asserted by tools/e2e/run.mjs, which talks to this the way a CLI does.
//
// Not installed is not broken: without the server or a browser this says why on stderr and exits, the CLI reports
// the server as failed, and the agent still has `webly-screenshot`.

import { basename, join } from 'node:path';
import { createInterface } from 'node:readline';
import { findBrowser, which } from './browser.js';
import { run } from './processes.js';

const WITHHELD = new Set(['browser_run_code_unsafe']);

function unavailable(reason) {
  process.stderr.write(`The browser tools are not available here: ${reason}. webly-screenshot still works.\n`);
  process.exit(1);
}

const server = which('playwright-mcp') ?? unavailable('the Playwright MCP server is not installed');
const browser = findBrowser() ?? unavailable('there is no browser in this sandbox');
const site = (process.env.WEBLY_DEV_URL ?? '').replace(/\/+$/, '') || unavailable('the dev server address is not known');
const output = join(process.env.WEBLY_WORKSPACE || process.cwd(), '.webly', 'browser');

// `run`, not `spawn`: on Windows `playwright-mcp` is an npm wrapper (`playwright-mcp.cmd`) that spawn cannot start.
const child = run(server, [
  '--headless', '--browser', 'chromium', '--executable-path', browser, '--no-sandbox', '--isolated',
  '--output-dir', output,
], { stdio: ['pipe', 'pipe', 'inherit'] });

child.on('error', error => unavailable(`the server did not start (${error.message})`));
child.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));

for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => child.kill(signal));

/** Each JSON-RPC message on a stream, rewritten by `rewrite`; a line that is not JSON passes as it is. */
function relay(from, to, rewrite) {
  const lines = createInterface({ input: from, crlfDelay: Infinity });

  lines.on('line', line => {
    let message;

    try {
      message = JSON.parse(line);
    } catch {
      to.write(`${line}\n`);
      return;
    }

    const rewritten = rewrite(message);
    if (rewritten) to.write(`${JSON.stringify(rewritten)}\n`);
  });

  return lines;
}

// The CLI to the server: calls, with their arguments put right — or answered here, for a withheld tool.
relay(process.stdin, child.stdin, message => {
  if (message.method !== 'tools/call' || !message.params) return message;

  const { name, arguments: args = {} } = message.params;

  if (WITHHELD.has(name)) {
    process.stdout.write(`${JSON.stringify({
      jsonrpc: '2.0',
      id: message.id,
      result: { isError: true, content: [{ type: 'text', text: `${name} is not available in this sandbox.` }] },
    })}\n`);
    return null;
  }

  if (name === 'browser_navigate' && typeof args.url === 'string' && args.url.startsWith('/')) args.url = `${site}${args.url}`;

  if (name === 'browser_take_screenshot') delete args.filename;

  // Every other `filename` these tools take is somewhere to write their output; the name is kept, the place is not.
  if (typeof args.filename === 'string' && args.filename) args.filename = join(output, basename(args.filename));

  return { ...message, params: { ...message.params, arguments: args } };
}).on('close', () => child.stdin.end());

// The server to the CLI: the tool list, without what is withheld and with the site's address where it is needed.
relay(child.stdout, process.stdout, message => {
  if (!Array.isArray(message.result?.tools)) return message;

  const tools = message.result.tools
    .filter(tool => !WITHHELD.has(tool.name))
    .map(tool => {
      if (tool.name === 'browser_navigate') {
        return {
          ...tool,
          description: `${tool.description}. The site you are editing is running at ${site}/ — pass a path on it, `
            + 'such as "/" or "/contact", and it is resolved against that address.',
        };
      }

      if (tool.name === 'browser_take_screenshot' && tool.inputSchema?.properties?.filename) {
        const { filename, ...properties } = tool.inputSchema.properties;
        return { ...tool, inputSchema: { ...tool.inputSchema, properties } };
      }

      return tool;
    });

  return { ...message, result: { ...message.result, tools } };
});
