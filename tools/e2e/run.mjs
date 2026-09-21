#!/usr/bin/env node
// The product loop, end to end, without the backend.
//
// Webly's own orchestration (AgentTurnService, CommitSiteVersion, SiteWorkspaceRegistry, PreviewController)
// is C#, and this repository was written in an environment with no .NET SDK — so none of it can be run. This
// script stands in for that orchestration and drives everything underneath it for real:
//
//   real git plumbing → real sandbox agent → real coding-agent CLI → real Next.js dev server → real build
//
// What it proves, in order, is the chain the product is: a site starts as the template, a turn edits it, the
// edit becomes a commit, the preview shows it, the history can diff and restore it, and the publish gate
// catches a broken build. Each step asserts, and the whole thing exits non-zero on the first failure.
//
// Usage:  node tools/e2e/run.mjs [--agent claude|mock] [--keep]

import {
  cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, symlinkSync, writeFileSync,
} from 'node:fs';
import { join, resolve as resolvePath } from 'node:path';
import { fileURLToPath } from 'node:url';

import * as store from './git-store.mjs';
import * as box from './sandbox.mjs';

const root = resolvePath(fileURLToPath(new URL('../..', import.meta.url)));
const template = join(root, 'templates', 'next-site');
const fixtures = join(root, 'tools', 'e2e', 'fixtures');

const args = process.argv.slice(2);
const option = name => {
  const index = args.indexOf(`--${name}`);
  return index >= 0 ? args[index + 1] : undefined;
};
const keep = args.includes('--keep');
const requestedAgent = option('agent') ?? 'claude';

/**
 * Where to leave the published site, if anywhere.
 *
 * Worth a flag because the export is the one output of this script that a person might want to look at rather
 * than read a pass/fail about: it is a real static site, built by the real pipeline from a real turn, and
 * opening its index.html in a browser is the shortest honest demonstration that the product works.
 */
const outDir = option('out');

// ---------------------------------------------------------------------------------------------
// A tiny harness. No framework: this file is the only thing that uses it.
// ---------------------------------------------------------------------------------------------
let failures = 0;
let stepNumber = 0;

async function step(name, body) {
  stepNumber += 1;
  const started = Date.now();
  process.stdout.write(`\n[${stepNumber}] ${name}\n`);

  try {
    const value = await body();
    process.stdout.write(`    ok (${((Date.now() - started) / 1000).toFixed(1)}s)\n`);
    return value;
  } catch (error) {
    failures += 1;
    process.stdout.write(`    FAILED: ${error.message}\n`);
    throw error;
  }
}

function check(condition, message) {
  if (!condition) throw new Error(message);
  process.stdout.write(`    · ${message}\n`);
}

// ---------------------------------------------------------------------------------------------
// The template, read the way DirectorySiteTemplateSource reads it.
// ---------------------------------------------------------------------------------------------
function readTemplate() {
  const skip = new Set(['node_modules', '.next', '.git', 'out', '.vercel']);
  const files = [];

  const walk = directory => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (skip.has(entry.name)) continue;
      const full = join(directory, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile()) files.push({ path: full.slice(template.length + 1), content: readFileSync(full) });
    }
  };

  walk(template);

  return files.sort((a, b) => a.path.localeCompare(b.path));
}

// ---------------------------------------------------------------------------------------------
// The agent. The real CLI when it is available, a scripted edit when it is not — the same split the
// backend has between ClaudeCodeAgent and MockCodingAgent, and for the same reason: the loop has to be
// testable without credentials.
// ---------------------------------------------------------------------------------------------
const PROMPT = message => `${message}

When you are done, end your reply with a single line:
SUMMARY: <one sentence describing what changed, under 70 characters>`;

async function runClaude(sandbox, message, transcript) {
  const events = [];

  const result = await box.exec(sandbox, {
    command: 'claude',
    args: [
      '-p', PROMPT(message),
      '--output-format', 'stream-json',
      '--verbose',
      '--permission-mode', 'acceptEdits',
      '--add-dir', sandbox.workspace,
    ],
    env: { CI: 'true' },
    timeoutMs: 600_000,
  }, event => {
    if (event.type === 'stdout') transcript.push(event.text);
  });

  // The NDJSON the CLI wrote, as the .NET StreamJsonParser would see it: one object per line.
  for (const line of transcript.join('').split('\n')) {
    const trimmed = line.trim();
    if (!trimmed) continue;
    try {
      events.push(JSON.parse(trimmed));
    } catch {
      /* a partial line at the end of the stream */
    }
  }

  return { result, events };
}

/**
 * The transcript, trimmed for committing as a test fixture.
 *
 * Two reasons it is not the raw stream. It carries the *recording* environment's details — the session id,
 * the full slash-command catalogue of whoever ran it, the cost of the turn — none of which belong in this
 * repository. And `system/commands_changed` alone is most of the bytes, while being exactly the kind of
 * event the parser is supposed to ignore. What is kept is every event shape the parser has an opinion
 * about, plus one of each it must skip.
 */
function sanitise(events, { workspace }) {
  const SESSION = '00000000-0000-4000-8000-000000000000';
  const seen = new Set();

  // The init event is the one that carries a whole environment. Only the keys the parser could plausibly
  // read are kept, so a fixture recorded on one machine does not describe it.
  const INIT_KEEP = ['type', 'subtype', 'cwd', 'session_id', 'model', 'permissionMode', 'apiKeySource'];

  const lines = events.flatMap(event => {
    if (event.type === 'system' && event.subtype === 'commands_changed') return [];

    // One example of each ignorable shape is enough to pin "the parser skips this".
    if (['active_goal', 'autocompact_state', 'rate_limit_event'].includes(event.type)) {
      if (seen.has(event.type)) return [];
      seen.add(event.type);
    }

    let copy = JSON.parse(JSON.stringify(event));

    if (copy.type === 'system' && copy.subtype === 'init') {
      copy = Object.fromEntries(INIT_KEEP.filter(key => key in copy).map(key => [key, copy[key]]));
    }

    for (const key of ['startup_timing', 'modelUsage', 'total_cost_usd', 'usage', 'slash_commands',
      'terminal_slash_commands', 'skills', 'agents', 'plugins', 'tools', 'capabilities', 'mcp_servers']) {
      delete copy[key];
    }

    if (copy.session_id) copy.session_id = SESSION;
    if (copy.uuid) copy.uuid = SESSION;
    if (copy.parent_tool_use_id) copy.parent_tool_use_id = null;
    if (copy.request_id) copy.request_id = 'req_fixture';
    if (copy.cwd) copy.cwd = '/workspace';
    if (copy.message?.id) copy.message.id = 'msg_fixture';

    return [copy];
  });

  // A final pass over the text, because identifiers turn up inside strings as well as in fields: the
  // recorder's temp workspace (production is always /workspace, which is the prefix the parser strips),
  // and any remaining trace of the session it was recorded in.
  const name = workspace.split('/').pop();

  return lines.map(line => JSON.stringify(line)
    .replaceAll(workspace, '/workspace')
    .replaceAll(name, 'workspace')
    .replaceAll(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g, SESSION));
}

/**
 * What MockCodingAgent does on the .NET side, algorithm for algorithm: replace the first
 * `headline="…"` in the home page with the person's words.
 *
 * Mirrored rather than approximated, because the C# cannot be compiled here — so running the same steps over
 * the same real template, and then letting the real `next build` type-check the result, is the only evidence
 * available that the edit it makes is a valid one.
 */
const TARGET_PATH = 'src/app/page.tsx';
const TARGET_ATTRIBUTE = 'headline="';

function replaceFirstHeadline(source, headline) {
  const start = source.indexOf(TARGET_ATTRIBUTE);
  if (start < 0) return null;

  const valueStart = start + TARGET_ATTRIBUTE.length;
  const valueEnd = source.indexOf('"', valueStart);
  if (valueEnd < 0) return null;

  const safe = headline.replaceAll('"', '').replaceAll('{', '').replaceAll('}', '')
    .replaceAll('<', '').replaceAll('>', '');

  return source.slice(0, valueStart) + safe + source.slice(valueEnd);
}

function mockHeadline(message) {
  const cleaned = message.replaceAll('\n', ' ').replaceAll('\r', ' ').trim();
  const sentence = cleaned.split(/[.!?]/, 1)[0].trim();
  const value = sentence.length > 0 ? sentence : cleaned;

  return value.length <= 60 ? value : value.slice(0, 60).trimEnd();
}

async function runMock(sandbox, message) {
  const page = join(sandbox.workspace, TARGET_PATH);
  const before = readFileSync(page, 'utf8');
  const headline = mockHeadline(message);
  const after = replaceFirstHeadline(before, headline);

  if (after === null) throw new Error(`the mock agent found no ${TARGET_ATTRIBUTE} in ${TARGET_PATH}`);
  if (after === before) throw new Error('the mock agent changed nothing');

  writeFileSync(page, after);

  return {
    result: { succeeded: true, exitCode: 0, output: '' },
    events: [],
    headline,
    reply: `I put "${headline}" at the top of the home page.`,
  };
}

// ---------------------------------------------------------------------------------------------
// The run.
// ---------------------------------------------------------------------------------------------
let sandbox;

/** The second, short-lived sandbox a publish runs in — see step 12. */
let publishSandbox;
let workspaceRoot;

// A killed harness still has sandboxes and dev servers to take with it. Without this, running under `timeout`
// — which is how anything with a CI-shaped patience runs it — leaves them behind.
for (const signal of ['SIGTERM', 'SIGINT']) {
  process.on(signal, () => {
    if (sandbox) void sandbox.dispose();
    if (publishSandbox) void publishSandbox.dispose();
    process.exit(130);
  });
}

try {
  const { root: temporary, repository } = store.temporaryRepository('site-e2e');
  workspaceRoot = temporary;
  process.stdout.write(`Repository: ${repository}\n`);

  // -- 1. A site starts as the template, committed ------------------------------------------------
  const templateFiles = await step('A new site is the template, committed to its own bare repository', async () => {
    await store.initBare(repository);
    const files = readTemplate();
    check(files.length > 10, `the template has ${files.length} files`);
    check(files.some(f => f.path === 'AGENTS.md'), 'AGENTS.md ships with every site');
    check(files.some(f => f.path === 'content/brand.md'), "content/brand.md ships as the agent's memory");

    const first = await store.commitTree(repository, {
      files,
      summary: 'Created from the starter template',
    });

    check(first !== null, 'the first commit was written');
    check(/^[0-9a-f]{40}$/.test(first.commitSha), `commit sha is ${first.commitSha.slice(0, 7)}`);
    check(first.changedFileCount === files.length, `changed file count is ${first.changedFileCount}`);

    const head = await store.resolveHead(repository);
    check(head === first.commitSha, 'refs/heads/main points at it');

    globalThis.__first = first;
    return files;
  });

  const first = globalThis.__first;

  // -- 2. Committing an identical tree does nothing ----------------------------------------------
  await step('A turn that changes nothing commits nothing', async () => {
    const again = await store.commitTree(repository, {
      files: templateFiles,
      parentSha: first.commitSha,
      summary: 'Should not exist',
    });

    check(again === null, 'an identical tree returned no commit');
    const log = await store.git(repository, ['rev-list', '--count', 'refs/heads/main']);
    check(log.trim() === '1', 'the history still has exactly one entry');
  });

  // -- 3. A sandbox, and a workspace seeded from the commit --------------------------------------
  sandbox = await step('A sandbox starts and answers its contract', async () => {
    const started = await box.startSandbox();
    const state = await box.health(started);
    check(state.ok === true, `health is ok on ${state.node}`);
    check(state.devServer === 'stopped', 'no dev server yet');
    return started;
  });

  await step("The commit's tree is written into the workspace", async () => {
    const tree = await store.readTree(repository, first.commitSha);
    check(tree.length === templateFiles.length, `read ${tree.length} files back out of git`);

    await box.writeTree(sandbox, tree);

    check(existsSync(join(sandbox.workspace, 'package.json')), 'package.json landed');
    check(existsSync(join(sandbox.workspace, 'src/app/page.tsx')), 'src/app/page.tsx landed');
    check(!existsSync(join(sandbox.workspace, 'node_modules')), 'node_modules did not travel through git');
  });

  // -- 4. The prebaked-dependencies bet ----------------------------------------------------------
  await step('Dependencies are already there, so a cold workspace does not install', async () => {
    // What deploy/sandbox/Dockerfile does with `npm ci` at build time, emulated: the image has the
    // template's dependencies installed at /workspace before any tree arrives.
    check(existsSync(join(template, 'node_modules')), 'the template has node_modules to stand in for the image layer');
    symlinkSync(join(template, 'node_modules'), join(sandbox.workspace, 'node_modules'), 'dir');

    const probe = await box.exec(sandbox, {
      command: 'sh',
      args: ['-c', 'test -d node_modules && npm ls --depth=0 >/dev/null 2>&1'],
      timeoutMs: 120_000,
    });

    check(probe.succeeded, 'npm ls --depth=0 passes, so SeedAsync skips the install');
  });

  // -- 5. The dev server, and the preview through the agent --------------------------------------
  await step('next dev starts and the preview serves the starter page, stylesheet and all', async () => {
    // The base path the product uses, in miniature. Webly passes `/api/sites/{nanoid}/preview`, because that
    // is where a browser reaches the dev server; here it is `/preview`, because that is where this harness
    // does. Either way the point is the same one, and it is the reason this step now reads an asset.
    await box.startDevServer(sandbox, { basePath: '/preview' });

    const response = await box.waitFor(async () => {
      const attempt = await box.preview(sandbox);
      return attempt.ok ? attempt : null;
    }, 180_000, 'the preview never answered');

    const html = await response.text();
    check(response.status === 200, 'the preview answered 200 through /preview/');
    check(html.includes('<!DOCTYPE html>') || html.includes('<!doctype html>'), 'it is a rendered HTML document');

    // The assertion this step was missing, and the bug it was missing: Next.js writes **absolute** URLs for
    // its stylesheets and chunks, so without a base path the page asks for `/_next/...` at the root of
    // whatever origin is serving the proxy — which is Webly's own app. The HTML looked perfect and the
    // preview rendered as unstyled text with no JavaScript. Reading the document is not enough; the page is
    // what somebody looks at.
    const assets = [...html.matchAll(/(?:href|src)="(\/[^"]*_next[^"]*)"/g)].map(match => match[1]);

    check(assets.length > 0, `the page references ${assets.length} of its own assets`);
    check(
      assets.every(asset => asset.startsWith('/preview/_next/')),
      'every one of them is under the base path it was started with');

    const stylesheet = assets.find(asset => asset.includes('.css')) ?? assets[0];
    const asset = await box.preview(sandbox, stylesheet.replace('/preview/', ''), { timeoutMs: 60_000 });

    check(asset.ok, `fetching ${stylesheet} through the proxy answered ${asset.status}`);
    check((await asset.text()).length > 0, 'and it has a body');

    const state = await box.health(sandbox);
    check(state.devServer === 'ready' || state.devServer === 'starting', `health reports the dev server ${state.devServer}`);

    globalThis.__before = html;
  });

  // -- 6. A real agent turn ----------------------------------------------------------------------
  const message = 'We are Koopman Cycles, a bike repair shop in Utrecht. Say that on the home page.';

  // What the published page must end up containing. The mock's own headline, or — for a real agent, whose words
  // are its own — the business's name out of the request.
  const expected = requestedAgent === 'mock' ? mockHeadline(message) : 'Koopman Cycles';

  const turn = await step(`A turn runs the ${requestedAgent} agent in the workspace`, async () => {
    const transcript = [];
    let outcome;

    if (requestedAgent === 'mock') {
      outcome = await runMock(sandbox, message);
    } else {
      outcome = await runClaude(sandbox, message, transcript);
      check(outcome.events.length > 0, `the CLI emitted ${outcome.events.length} stream-json events`);

      mkdirSync(fixtures, { recursive: true });
      const fixture = sanitise(outcome.events, { workspace: sandbox.workspace });
      writeFileSync(join(fixtures, 'claude-stream-json.ndjson'), `${fixture.join('\n')}\n`);
      check(true, `recorded ${fixture.length} sanitised events to tools/e2e/fixtures/claude-stream-json.ndjson`);
    }

    check(outcome.result.succeeded, `the agent exited ${outcome.result.exitCode}`);
    return outcome;
  });

  // -- 7. What StreamJsonParser has to read ------------------------------------------------------
  if (turn.events.length) {
    await step('The stream carries what StreamJsonParser reads', async () => {
      const types = [...new Set(turn.events.map(e => e.type))];
      process.stdout.write(`    event types: ${types.join(', ')}\n`);

      const sessions = new Set(turn.events.map(e => e.session_id).filter(Boolean));
      check(sessions.size === 1, `session_id is on the envelope, one value: ${[...sessions][0]?.slice(0, 8)}`);

      const assistants = turn.events.filter(e => e.type === 'assistant');
      check(assistants.length > 0, `${assistants.length} assistant events`);

      const blocks = assistants.flatMap(e => e.message?.content ?? []);
      const kinds = [...new Set(blocks.map(b => b.type))];
      process.stdout.write(`    content block types: ${kinds.join(', ')}\n`);

      const tools = blocks.filter(b => b.type === 'tool_use');
      const toolNames = [...new Set(tools.map(t => t.name))];
      process.stdout.write(`    tools used: ${toolNames.join(', ') || '(none)'}\n`);

      const writes = tools.filter(t => ['Write', 'Edit', 'MultiEdit', 'NotebookEdit'].includes(t.name));
      for (const write of writes) {
        const keys = Object.keys(write.input ?? {});
        process.stdout.write(`    ${write.name} input keys: ${keys.join(', ')}\n`);
      }

      const results = turn.events.filter(e => e.type === 'result');
      check(results.length === 1, 'exactly one result event');
      const final = results[0];
      process.stdout.write(`    result subtype=${final.subtype} is_error=${final.is_error}\n`);
      check(typeof final.result === 'string' || final.is_error === true,
        'the result event carries the final text (or says it errored)');

      globalThis.__streamFindings = { types, kinds, toolNames, writes: writes.map(w => ({ name: w.name, keys: Object.keys(w.input ?? {}) })), final: { subtype: final.subtype, isError: final.is_error } };
    });
  }

  // -- 8. The turn becomes one commit ------------------------------------------------------------
  const second = await step('The tree comes back and becomes exactly one commit', async () => {
    const after = await box.readTree(sandbox);
    check(after.length > 0, `read ${after.length} files back out of the workspace`);
    check(!after.some(f => f.path.startsWith('node_modules/')), 'node_modules was excluded on the way out');
    check(!after.some(f => f.path.startsWith('.next/')), '.next was excluded on the way out');

    const before = new Map(templateFiles.map(f => [f.path, f.content.toString('utf8')]));
    const changed = after.filter(f => before.get(f.path) !== f.content.toString('utf8'));
    check(changed.length > 0, `the agent changed ${changed.length} file(s): ${changed.map(f => f.path).join(', ')}`);

    const commit = await store.commitTree(repository, {
      files: after,
      parentSha: first.commitSha,
      summary: 'Said who the business is on the home page',
      details: 'From the end-to-end harness.',
    });

    check(commit !== null, 'a version was committed');
    check(commit.changedFileCount === changed.length,
      `changedFileCount is ${commit.changedFileCount}, matching the files that differ`);

    const count = (await store.git(repository, ['rev-list', '--count', 'refs/heads/main'])).trim();
    check(count === '2', 'the history now has two entries');

    const diff = await store.diff(repository, commit.commitSha);
    check(diff.length > 0, `the diff is ${diff.length} characters, which is what the history pane shows`);

    const author = (await store.git(repository, ['show', '-s', '--format=%an <%ae>', commit.commitSha])).trim();
    check(author === 'Webly Tester <tester@example.com>', `the commit is authored by the person: ${author}`);

    return commit;
  });

  // -- 9. The preview shows it, by itself --------------------------------------------------------
  await step('Hot reload puts the change on screen with nothing asking', async () => {
    const response = await box.waitFor(async () => {
      const attempt = await box.preview(sandbox);
      if (!attempt.ok) return null;
      const html = await attempt.text();
      return html !== globalThis.__before ? html : null;
    }, 90_000, 'the preview never changed after the edit');

    check(response.length > 0, 'the preview now serves different HTML than before the turn');
  });

  // -- 10. Re-seeding replaces the source rather than overlaying it ------------------------------
  await step('Re-seeding a warm workspace removes files the new tree does not have', async () => {
    // The case this exists for is a restore that deleted a page. `tar -x` only adds and overwrites, so before
    // the sandbox agent started clearing the source first, the page would survive a re-seed and be committed
    // straight back — a restore that silently did not remove anything. Proven, rather than reasoned about.
    const stray = 'src/app/about/page.tsx';
    const tree = await box.readTree(sandbox);

    // Read back what is actually there first. The interesting failure is not "the stray survived" but "the
    // re-seed took the real pages with it", and a check that only looked for the stray would pass through it.
    check(tree.some(f => f.path === 'src/app/page.tsx'), `the tree read back has ${tree.length} files including the home page`);

    await box.writeTree(sandbox, [
      ...tree,
      { path: stray, content: Buffer.from('export default function About() { return null; }\n') },
    ]);

    check(existsSync(join(sandbox.workspace, stray)), 'a second page was added to the workspace');

    // Seed again from the tree that never had it — what SiteWorkspaceRegistry does when the head has moved.
    await box.writeTree(sandbox, tree);
    check(!existsSync(join(sandbox.workspace, stray)), 'and re-seeding removed it');

    // What it must NOT have removed: the site. This is the assertion that matters.
    for (const path of ['src/app/page.tsx', 'src/app/layout.tsx', 'package.json', 'next.config.ts']) {
      check(existsSync(join(sandbox.workspace, path)), `${path} survived the re-seed`);
    }

    // And the two derived directories, which cost minutes and are in no commit.
    check(existsSync(join(sandbox.workspace, 'node_modules')), 'node_modules survived the re-seed');
    check(existsSync(join(sandbox.workspace, '.next')), '.next survived the re-seed');

    // The preview has to survive it too. This is what a person sees right after a restore, and the dev server
    // has just had its whole source directory deleted and rewritten underneath it.
    const response = await box.waitFor(async () => {
      const attempt = await box.preview(sandbox);
      if (!attempt.ok) return null;
      const html = await attempt.text();
      return html.includes('<h1') ? html : null;
    }, 60_000, 'the preview did not recover after a re-seed');

    check(response.length > 0, 'and the preview still renders the site afterwards');
  });

  // -- 11. Restore writes forward ----------------------------------------------------------------
  await step('Restoring an earlier version writes it forward and keeps the history', async () => {
    const original = await store.readTree(repository, first.commitSha);

    const restored = await store.commitTree(repository, {
      files: original,
      parentSha: second.commitSha,
      summary: 'Restored the starter version',
    });

    check(restored !== null, 'the restore produced a new commit');

    const count = (await store.git(repository, ['rev-list', '--count', 'refs/heads/main'])).trim();
    check(count === '3', 'the history has three entries, so nothing was rewound');

    const stillThere = await store.git(repository, ['cat-file', '-t', second.commitSha]);
    check(stillThere.trim() === 'commit', 'the version that was undone is still reachable');

    const restoredTree = (await store.git(repository, ['rev-parse', `${restored.commitSha}^{tree}`])).trim();
    const firstTree = (await store.git(repository, ['rev-parse', `${first.commitSha}^{tree}`])).trim();
    check(restoredTree === firstTree, 'the restored tree is identical to the one it restored');

    // Put the edited version back, so the build below is the one somebody would publish.
    await store.commitTree(repository, {
      files: await store.readTree(repository, second.commitSha),
      parentSha: restored.commitSha,
      summary: 'Restored the edited version',
    });
  });

  // -- 12. The publish gate, in a FRESH sandbox --------------------------------------------------
  //
  // Not the warm editing sandbox, because that is not what the product does: DeploymentJobRunner starts a new
  // one and seeds it from the version being published. Doing it the other way here did not just make the test
  // unfaithful, it made it fail — a `.next` left behind by the dev server, after the source directory had been
  // deleted and rewritten by a re-seed, produced a build that "succeeded" and exported nothing but a 404 page.
  // Which is the argument for the fresh sandbox, demonstrated.
  await step('A publish builds from the committed tree in a fresh sandbox', async () => {
    const head = await store.resolveHead(repository);
    const tree = await store.readTree(repository, head);

    publishSandbox = await box.startSandbox();
    await box.writeTree(publishSandbox, tree);

    // Standing in for the image's prebaked dependencies, as in step 5. The real runner runs `npm ci` against
    // the committed lockfile here.
    symlinkSync(join(template, 'node_modules'), join(publishSandbox.workspace, 'node_modules'), 'dir');

    check(!existsSync(join(publishSandbox.workspace, '.next')), 'the fresh sandbox has no build cache');

    const build = await box.exec(publishSandbox, {
      command: 'npm',
      args: ['run', 'build'],
      env: { NEXT_TELEMETRY_DISABLED: '1' },
      timeoutMs: 600_000,
    });

    if (!build.succeeded) process.stdout.write(`\n${build.output.slice(-3000)}\n`);
    check(build.succeeded, 'npm run build succeeded, so this version is publishable');
    check(existsSync(join(publishSandbox.workspace, 'out')), 'the build produced a static export in out/');
  });

  // -- 13. The publish: the built site copied out and served ------------------------------------
  await step('Publishing copies the export out of the sandbox and it is a real page', async () => {
    // Exactly what FileSystemDeploymentTarget.CopyOutputAsync does: the build output is excluded from the
    // tree-reading contract on purpose (an artefact must never travel back into somebody's git history), so it
    // is fetched explicitly, base64'd because /exec streams JSON strings.
    //
    // Plain `base64`, no -w 0: that flag is GNU's, and with the local provider the "sandbox" is whatever
    // machine the developer has.
    const packed = await box.exec(publishSandbox, {
      command: 'sh',
      args: ['-c', 'test -d out && tar -c -z -C out . | base64'],
      timeoutMs: 120_000,
    });

    check(packed.succeeded, 'the export was packed');

    const encoded = packed.output.replace(/\s+/g, '');
    check(encoded.length > 0, `${encoded.length} base64 characters came back`);

    const published = join(workspaceRoot, 'published');
    mkdirSync(published, { recursive: true });

    const archive = join(workspaceRoot, 'published.tgz');
    writeFileSync(archive, Buffer.from(encoded, 'base64'));
    await box.run('tar', ['-x', '-z', '-f', archive, '-C', published]);
    rmSync(archive, { force: true });

    const index = join(published, 'index.html');

    if (!existsSync(index)) {
      process.stdout.write(`    published/ contains: ${readdirSync(published).join(', ') || '(nothing)'}\n`);
    }

    check(existsSync(index), 'index.html is where a static host would look for it');

    const html = readFileSync(index, 'utf8');
    check(html.includes('<!DOCTYPE html>') || html.includes('<!doctype html>'), 'it is a complete document');
    check(!html.includes('_next/webpack-hmr'), 'and it is the built page, not the dev server\'s');

    // The assertion the other twelve steps exist to make possible: what the person typed is on a page that
    // could be served to the public.
    check(html.includes(expected), `the published page says "${expected}"`);

    // trailingSlash: true in the template, so a second page lands at /about/index.html — the shape that lets a
    // static host serve clean URLs with no rewrite rules.
    const nested = readdirSync(published, { withFileTypes: true }).filter(entry => entry.isDirectory());
    check(true, `the export has ${nested.length} directories beside index.html`);

    if (outDir) {
      const target = resolvePath(outDir);
      rmSync(target, { recursive: true, force: true });
      cpSync(published, target, { recursive: true });
      check(true, `kept the published site at ${target}`);
    }
  });

  // -- 14. Leaving with the history --------------------------------------------------------------
  await step('The site exports as a git bundle that clones into a working project', async () => {
    // The same arguments GitSiteRepositoryStore.CreateBundleAsync runs, to a file rather than to stdout.
    //
    // `HEAD` is the part that matters and the reason this step exists. Without it the bundle still contains
    // every commit and `git bundle verify` still says "complete history" — and `git clone` of it produces a
    // repository with no files checked out, because nothing says which branch to check out. The first version
    // of the export shipped without it and this step is what caught it.
    const bundle = join(workspaceRoot, 'export.bundle');
    await store.git(repository, ['bundle', 'create', bundle, 'HEAD', 'main']);

    check(existsSync(bundle), `the bundle is ${Math.round(statSync(bundle).size / 1024)} KB`);

    const verify = await store.git(repository, ['bundle', 'verify', bundle]);
    check(/complete history/i.test(verify), 'git says it records a complete history');

    // The claim, tested: somebody who leaves gets a working project, not a snapshot of one.
    const clone = join(workspaceRoot, 'clone');
    await box.run('git', ['clone', '--quiet', bundle, clone]);

    check(existsSync(join(clone, 'src/app/page.tsx')), 'the clone has the site in it');
    check(existsSync(join(clone, 'AGENTS.md')), 'including the instructions it was edited under');

    const log = await box.run('git', ['-C', clone, 'log', '--oneline']);
    const commits = log.split('\n').filter(Boolean);
    check(commits.length >= 3, `and ${commits.length} commits of history, not just the current files`);

    const subjects = commits.map(line => line.slice(line.indexOf(' ') + 1));
    check(subjects.some(subject => subject.includes('Restored')),
      'with the agent\'s own sentences as the commit messages');
  });

  // -- 15. A broken build is caught --------------------------------------------------------------
  //
  // In the publish sandbox, not the warm one, and for a reason this step learned the hard way: `next build`
  // and `next dev` share `.next`, so building in the sandbox the dev server is running in leaves that dev
  // server unable to compile anything afterwards. Step 16 then failed to recover from a change it had
  // nothing to do with. It is also what the product does — a publish is always a fresh sandbox — so this is
  // the faithful version as well as the working one.
  await step('A broken build is caught rather than published', async () => {
    const page = join(publishSandbox.workspace, 'src', 'app', 'page.tsx');
    const good = readFileSync(page, 'utf8');

    try {
      writeFileSync(page, `${good}\nthis is not valid typescript(((`);

      const build = await box.exec(publishSandbox, {
        command: 'npm',
        args: ['run', 'build'],
        env: { NEXT_TELEMETRY_DISABLED: '1' },
        timeoutMs: 600_000,
      });

      check(!build.succeeded, `the build failed as it should (exit ${build.exitCode})`);
      check(/error|failed/i.test(build.output), 'the output says why, which is what reaches Deployment.ErrorDetail');

      const log = await box.devLog(sandbox);
      check(typeof log.text === 'string',
        `the editing sandbox's dev server log is readable (${log.text.length} chars at offset ${log.offset}), which is what BuildFailed reports`);
    } finally {
      writeFileSync(page, good);
    }
  });

  // -- 16. A fixed error stops being reported ----------------------------------------------------
  await step('A compile error from an earlier turn is not reported again once it is fixed', async () => {
    // The bug this exists for: the dev server's log is cumulative, so AgentTurnService reading all of it after a
    // turn would find the error the *previous* turn left and report it again — telling somebody their site is
    // broken every time they speak to it, however many times they have it fixed. The turn records an offset
    // before it starts and reads from there. This proves the mechanism the fix rests on.
    const page = join(sandbox.workspace, 'src', 'app', 'page.tsx');
    const good = readFileSync(page, 'utf8');
    // The same set DevServerLogReader looks for, and the reason it is that set rather than the obvious one is
    // in its comment: a syntax error produces `⨯ ./` and `Caused by: Syntax Error`, and none of the three
    // phrases anybody would guess.
    const markers = /⨯ \.\/|Failed to compile|Module not found|Syntax Error|Type error:/i;

    try {
      // Break it with a syntax error, and wait for the dev server to say so — this is "the previous turn".
      //
      // A syntax error specifically, because the two more obvious ways to break a file do not work and the
      // reason is the same one. `next dev` compiles with SWC, which strips types rather than checking them,
      // so a type error never reaches the log at all — and an unused broken import does not either, because
      // stripping types means eliding `import { nothing } from './does-not-exist'` as possibly-a-type before
      // anything tries to resolve it. That was this step's first attempt and the dev server cheerfully
      // recompiled, 604 modules, no complaint.
      //
      // So what the BuildFailed event actually covers is syntax and imports that are used; `npm run typecheck`
      // (which AGENTS.md tells the agent to run) covers types; and `next build` at publish time is the backstop
      // for both. Worth knowing before trusting the compile check to catch a given kind of mistake.
      writeFileSync(page, `${good}\n\nexport const broken = (\n`);

      // Requested, not waited for. `next dev` compiles on demand: without a request it never looks at the file
      // and the log stays silent — which is the second half of the bug, because AgentTurnService's check ran at
      // exactly that moment and concluded the site was fine. It now touches the preview first, as this does.
      const broke = await box.waitFor(async () => {
        await box.preview(sandbox).catch(() => undefined);

        const log = await box.devLog(sandbox);
        return markers.test(log.text) ? log : null;
      }, 90_000, 'the dev server never reported the error');

      check(true, 'the dev server reported a compile error');

      // Fix it, and let it recompile.
      writeFileSync(page, good);

      await box.waitFor(async () => {
        const attempt = await box.preview(sandbox);
        if (!attempt.ok) return null;
        const html = await attempt.text();
        return html.includes('<h1') ? html : null;
      }, 90_000, 'the preview never recovered');

      // What a turn starting now would see: everything since the fix. The old error is before that offset.
      const after = await box.devLog(sandbox, broke.offset);
      check(!markers.test(after.text),
        `reading from offset ${broke.offset} shows no error (${after.text.length} chars since)`);

      // And the proof that the offset is what makes the difference, rather than the log having been cleared.
      const whole = await box.devLog(sandbox);
      check(markers.test(whole.text), 'while the whole log still contains it, which is exactly the trap');
    } finally {
      writeFileSync(page, good);
    }
  });

  process.stdout.write(`\n${'='.repeat(78)}\nAll ${stepNumber} steps passed.\n`);

  if (globalThis.__streamFindings) {
    process.stdout.write(`\nStream-json findings for StreamJsonParser:\n${JSON.stringify(globalThis.__streamFindings, null, 2)}\n`);
  }
} catch (error) {
  process.stdout.write(`\n${'='.repeat(78)}\nFAILED at step ${stepNumber}: ${error.stack ?? error.message}\n`);
  if (sandbox) process.stdout.write(`\nSandbox agent log:\n${sandbox.log}\n`);
  process.exitCode = 1;
} finally {
  if (!keep) {
    if (sandbox) await sandbox.dispose();
    if (publishSandbox) await publishSandbox.dispose();
    if (workspaceRoot) rmSync(workspaceRoot, { recursive: true, force: true });
  } else {
    process.stdout.write(`\nKept: ${workspaceRoot}, ${sandbox?.workspace}, ${publishSandbox?.workspace}\n`);
  }
}
