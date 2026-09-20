#!/usr/bin/env node
/**
 * One agent turn, driven through the running app.
 *
 * `run.mjs` proves the product loop with the C# orchestration stood in for. This proves the C#: it talks to
 * the real backend over the real hub, so what it exercises is `ChatRunLauncher`, `AgentTurnService`,
 * `SiteWorkspaceRegistry`, the sandbox provider, `CommitSiteVersion` and the preview proxy — the layer no
 * unit test covers and no harness can.
 *
 * It exists because a turn cannot be started over HTTP. `StartChat` is a hub method, deliberately: a turn is
 * started and then watched as two steps so a reload can re-attach to it, and a POST that returned a run id
 * would be a second way to start one. So the only way to press this button is a SignalR client, and this is
 * the smallest one that presses it.
 *
 *   node tools/e2e/turn.mjs --site <nanoid> --cookies <jar> "make the headline say ..."
 *
 * `--cookies` is a curl cookie jar from a signed-in session; the two Webly cookies are read out of it and
 * sent as a header, which a browser could not do on a WebSocket upgrade and a script can.
 */
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';

const require = createRequire(path.join(process.cwd(), 'client', 'package.json'));
const signalR = require('@microsoft/signalr');

const args = process.argv.slice(2);
const option = (name, fallback) => {
    const index = args.indexOf(`--${name}`);
    return index >= 0 ? args[index + 1] : fallback;
};

const origin = option('origin', 'https://localhost:5000');
const site = option('site');
const jar = option('cookies', '/tmp/jar.txt');
const message = args.filter((value, index) =>
    !value.startsWith('--') && !args[index - 1]?.startsWith('--')).join(' ')
    || 'Say we are Koopman Cycles, a bike repair shop in Utrecht.';

if (!site) {
    console.error('usage: node tools/e2e/turn.mjs --site <nanoid> [--cookies <jar>] "<message>"');
    process.exit(2);
}

/**
 * The two Webly cookies out of a curl jar, in one header.
 *
 * The `#HttpOnly_` prefix is stripped before anything else looks at the line, because both of Webly's cookies
 * carry it and a jar parser that treats `#` as a comment therefore finds nothing at all.
 */
function cookieHeader(file) {
    return readFileSync(file, 'utf8')
        .split('\n')
        .map(line => line.replace(/^#HttpOnly_/, ''))
        .filter(line => line && !line.startsWith('#'))
        .map(line => line.split('\t'))
        .filter(fields => fields.length >= 7 && fields[5].startsWith('webly_'))
        .map(fields => `${fields[5]}=${fields[6]}`)
        .join('; ');
}

const cookie = cookieHeader(jar);

if (!cookie) {
    console.error(`no webly_* cookies in ${jar}; sign in first`);
    process.exit(2);
}

const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${origin}/hubs/realtime`, {
        headers: { Cookie: cookie },
        // Node has no cookie jar and the hub's negotiate call needs the same credential as the socket.
        withCredentials: false
    })
    .build();

let finished;
const done = new Promise(resolve => { finished = resolve; });
let files = 0;

connection.on('RunEvent', envelope => {
    const event = envelope.event ?? envelope.Event;
    const type = event.type ?? event.Type;
    const detail = event.detail ?? event.Detail;

    switch (type) {
        case 'Text':
            process.stdout.write(event.text ?? event.Text ?? '');
            break;
        case 'FileChanged':
            files += 1;
            console.log(`\n  · wrote ${detail}`);
            break;
        case 'WorkspaceProgress':
            console.log(`  · ${detail}`);
            break;
        case 'Activity':
            console.log(`  · ${detail}`);
            break;
        case 'VersionCommitted':
            console.log(`\n  ✓ version ${event.versionNanoid ?? event.VersionNanoid}: ${detail}`);
            break;
        case 'BuildFailed':
            console.log(`\n  ! build failed:\n${detail}`);
            break;
        case 'Completed':
        case 'Failed':
            console.log(`\n  ${type === 'Completed' ? '✓' : '✗'} ${type}${event.error ?? event.Error ? `: ${event.error ?? event.Error}` : ''}`);
            finished({ type, files });
            break;
    }
});

await connection.start();
console.log(`connected; starting a turn on ${site}`);

const started = await connection.invoke('StartChat', { siteNanoid: site, message, agent: null });
const runId = started.runId ?? started.RunId;

console.log(`run ${runId}`);
await connection.invoke('Subscribe', 'Chat', runId, 0);

// Generous: a cold workspace installs the site's dependencies before the agent sees it.
const timeout = setTimeout(() => {
    console.error('\ntimed out waiting for a terminal event');
    process.exit(1);
}, 15 * 60 * 1000);

const outcome = await done;
clearTimeout(timeout);
await connection.stop();

console.log(`\n${outcome.files} file(s) changed`);
process.exit(outcome.type === 'Completed' ? 0 : 1);
