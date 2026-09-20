#!/usr/bin/env node
/**
 * Dev-runtime MCP server for the Webly stack.
 *
 * Lifecycle is implemented natively rather than by shelling out to run-app.ps1, but it keeps
 * run-app.ps1's exact on-disk contract — same pidfiles, same logs, same ports — so the two
 * interoperate and either can stop what the other started.
 *
 * Topology reminder: the backend on :5000 is the single public origin. It serves /api, /health and
 * swagger itself and reverse-proxies everything else to the Angular dev server on :4200 (plain
 * HTTP, never exposed to the browser directly).
 */
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js'
import { z } from 'zod'
import { spawn } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'
import http from 'node:http'
import https from 'node:https'
import os from 'node:os'
import { fileURLToPath } from 'node:url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const REPO = path.resolve(HERE, '..', '..')
const RUN_DIR = path.join(REPO, '.run')
const BACKEND_DIR = path.join(REPO, 'Webly.Api')
const CLIENT_DIR = path.join(REPO, 'client')
const TESTS_PROJ = path.join(REPO, 'Webly.Tests', 'Webly.Tests.csproj')
const API_PROJ = path.join(BACKEND_DIR, 'Webly.Api.csproj')
const BACKEND_EXE = path.join(BACKEND_DIR, 'bin', 'Debug', 'net10.0', 'Webly.Api.exe')

const BACKEND_ORIGIN = 'https://localhost:5000'
const PG_IMAGE = 'postgres:17'
const COMPOSE_CONTAINER = 'webly-postgres-dev'
const COMPOSE_FILE = path.join(REPO, 'docker-compose.dev.yml')

const SERVICES = {
    backend: {
        name: 'backend',
        port: 5000,
        url: `${BACKEND_ORIGIN}/swagger/v1/swagger.json`,
        pidFile: path.join(RUN_DIR, 'backend.pid'),
        outLog: path.join(RUN_DIR, 'backend.out.log'),
        // The repository root, not Webly.Api. Three settings are paths relative to it —
        // Repositories:Root (.run/repositories), Templates:SitePath (templates/next-site) and
        // Sandbox:Local:AgentPath (tools/sandbox-agent/index.js) — and every one of them resolves against
        // the process's working directory. Started from Webly.Api, creating a site fails because the
        // template is not there and a turn fails because the sandbox agent is not either. Configuration
        // that says "templates/next-site" should mean the one in this repository.
        cwd: REPO,
        // The built exe, not `dotnet run`: `dotnet run` is a launcher whose child's stdout never
        // reaches a detached log. Spawning the exe directly gives a complete log. The build step
        // below keeps it from running stale code. It is an absolute path, so the cwd above is free to be
        // the repository root.
        command: BACKEND_EXE,
        args: [],
        env: {
            ASPNETCORE_ENVIRONMENT: 'Development',
            ASPNETCORE_URLS: 'https://localhost:5000;http://localhost:5001',
        },
        build: () => buildBackend(),
        readyTimeoutSec: 180,
    },
    frontend: {
        name: 'frontend',
        port: 4200,
        url: 'http://localhost:4200',
        pidFile: path.join(RUN_DIR, 'frontend.pid'),
        outLog: path.join(RUN_DIR, 'frontend.out.log'),
        cwd: CLIENT_DIR,
        // Not `cmd.exe /c yarn start`: detached, that chain gets no console and wedges silently.
        // Spawning node on Angular's own entry point is fine. The prestart hook `yarn start` would
        // have run (ng-openapi-gen) is therefore run explicitly as `prestart` below.
        command: 'node',
        args: [path.join(CLIENT_DIR, 'node_modules', '@angular', 'cli', 'bin', 'ng.js'), 'serve'],
        prestart: () => regenApiClient(),
        readyTimeoutSec: 240,
    },
}

function env() {
    const home = os.homedir()
    const extra = [path.join(home, '.dotnet')]
    return {
        ...process.env,
        DOTNET_ROOT: process.env.DOTNET_ROOT || path.join(home, '.dotnet'),
        PATH: [...extra, process.env.PATH ?? ''].join(path.delimiter),
    }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const text = (s) => ({ content: [{ type: 'text', text: String(s) }] })

/** Emits progress notifications so slow tools don't trip the client's 60s default timeout. */
async function withProgress(extra, label, work) {
    const token = extra?._meta?.progressToken
    let ticks = 0
    const timer = token !== undefined
        ? setInterval(() => {
            extra.sendNotification({
                method: 'notifications/progress',
                params: { progressToken: token, progress: ++ticks, message: `${label}…` },
            }).catch(() => { /* client went away; the work still finishes */ })
        }, 10_000)
        : null
    try {
        return await work()
    } finally {
        if (timer) clearInterval(timer)
    }
}

/** Runs a command to completion and captures both streams. Never throws on a non-zero exit. */
function sh(command, args, { cwd = REPO, timeoutMs = 600_000 } = {}) {
    return new Promise((resolve) => {
        const child = spawn(command, args, { cwd, env: env(), shell: false, windowsHide: true })
        let stdout = '', stderr = ''
        const timer = setTimeout(() => { child.kill(); resolve({ code: -1, stdout, stderr: stderr + '\n[timed out]' }) }, timeoutMs)
        child.stdout.on('data', (d) => { stdout += d })
        child.stderr.on('data', (d) => { stderr += d })
        child.on('error', (e) => { clearTimeout(timer); resolve({ code: -1, stdout, stderr: String(e) }) })
        child.on('close', (code) => { clearTimeout(timer); resolve({ code: code ?? -1, stdout, stderr }) })
    })
}

function diagnose(matched, stdout, stderr, limit = 30) {
    if (matched.length) return matched.slice(0, limit).join('\n')
    const tail = [stdout, stderr].join('\n').split(/\r?\n/).filter((l) => l.trim()).slice(-limit)
    return tail.length ? `(no recognised error lines; last output)\n${tail.join('\n')}` : '(no output at all)'
}

async function regenApiClient() {
    if (await probe(SERVICES.backend.url) !== 200) throw new Error('Backend is not serving swagger; start it first.')
    const { code, stdout, stderr } = await sh(
        'cmd.exe', ['/c', 'set NODE_TLS_REJECT_UNAUTHORIZED=0&& npx ng-openapi-gen --config open-api-gen.json'],
        { cwd: CLIENT_DIR, timeoutMs: 600_000 })
    const removed = stdout.split(/\r?\n/).filter((l) => l.includes('Removed stale file'))
    const tail = (stdout.trim().split(/\r?\n/).slice(-2).join('\n')) || stderr.trim()
    if (code !== 0) throw new Error(`API client regen FAILED (exit ${code})\n${diagnose([], stdout, stderr)}`)
    return `${tail}${removed.length ? `\n\nREMOVED ${removed.length} file(s) — check this is intended:\n${removed.join('\n')}` : ''}`
}

// --- process / port helpers -------------------------------------------------------------------

const readPid = (svc) => {
    try { return parseInt(fs.readFileSync(svc.pidFile, 'utf8').trim(), 10) || null } catch { return null }
}

async function isAlive(pid) {
    if (!pid) return false
    const { stdout } = await sh('tasklist', ['/FI', `PID eq ${pid}`, '/NH'])
    return stdout.includes(String(pid))
}

/** PIDs listening on a port. No `-p tcp`: that is IPv4-only and `ng serve` binds [::1] alone. */
async function portOwners(port) {
    const { stdout } = await sh('netstat', ['-ano'])
    const pids = new Set()
    for (const line of stdout.split(/\r?\n/)) {
        const m = line.match(/^\s*TCP\s+\S+:(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$/)
        if (m && Number(m[1]) === port) pids.add(Number(m[2]))
    }
    return [...pids]
}

/** HTTP(S) probe that tolerates the Kestrel dev certificate. */
function probe(url, timeoutMs = 4000) {
    return new Promise((resolve) => {
        const lib = url.startsWith('https') ? https : http
        const req = lib.get(url, { rejectUnauthorized: false, timeout: timeoutMs }, (res) => {
            res.resume()
            resolve(res.statusCode ?? 0)
        })
        req.on('error', () => resolve(0))
        req.on('timeout', () => { req.destroy(); resolve(0) })
    })
}

function getJson(url) {
    return new Promise((resolve, reject) => {
        const req = https.get(url, { rejectUnauthorized: false, timeout: 5000 }, (res) => {
            let data = ''
            res.on('data', (d) => { data += d })
            res.on('end', () => { try { resolve(JSON.parse(data)) } catch (e) { reject(e) } })
        })
        req.on('error', reject)
        req.on('timeout', () => { req.destroy(); reject(new Error('timed out')) })
    })
}

async function killTree(pid) {
    await sh('taskkill', ['/PID', String(pid), '/T', '/F'])
}

/**
 * A force-killed backend leaves its Testcontainers Postgres (and ryuk sidecar) behind. Remove
 * them so stop -> start is repeatable. The docker-compose container is never touched here.
 */
async function stopTestcontainers() {
    const notes = []
    const { stdout } = await sh('docker', ['ps', '--filter', `ancestor=${PG_IMAGE}`, '--format', '{{.ID}} {{.Names}}'])
    const ids = stdout.trim().split(/\r?\n/).filter(Boolean)
        .map((l) => l.split(/\s+/)).filter(([, name]) => name !== COMPOSE_CONTAINER).map(([id]) => id)
    if (ids.length) {
        await sh('docker', ['rm', '-f', ...ids], { timeoutMs: 120_000 })
        notes.push(`removed ${ids.length} testcontainers postgres container(s)`)
    }
    // By name: an untagged `ancestor=testcontainers/ryuk` filter only matches :latest.
    const ryuk = await sh('docker', ['ps', '-q', '--filter', 'name=testcontainers-ryuk'])
    const ryukIds = ryuk.stdout.trim().split(/\r?\n/).filter(Boolean)
    if (ryukIds.length) {
        await sh('docker', ['rm', '-f', ...ryukIds], { timeoutMs: 120_000 })
        notes.push(`removed ${ryukIds.length} ryuk container(s)`)
    }
    return notes
}

async function stopService(svc) {
    const notes = []
    const pid = readPid(svc)
    if (pid && await isAlive(pid)) {
        await killTree(pid)
        notes.push(`killed tracked pid ${pid}`)
    }
    for (const owner of await portOwners(svc.port)) {
        await killTree(owner)
        notes.push(`cleared pid ${owner} squatting on :${svc.port}`)
    }
    try { fs.unlinkSync(svc.pidFile) } catch { /* already gone */ }
    await sleep(1000)

    if (svc.name === 'backend') notes.push(...await stopTestcontainers())

    if (notes.length === 0) notes.push('was not running')
    return notes.join('; ')
}

async function buildBackend() {
    const { code, stdout } = await sh('dotnet', ['build', API_PROJ, '-c', 'Debug', '-v', 'q', '--nologo'])
    const errors = stdout.split(/\r?\n/).filter((l) => / error [A-Z]+\d+/.test(l))
    return {
        ok: code === 0,
        note: code === 0 ? 'build OK' : `build FAILED (exit ${code})\n${errors.slice(0, 25).join('\n')}`,
    }
}

async function startService(svc, { build = true } = {}) {
    if (await probe(svc.url) === 200) return `${svc.name} already serving on :${svc.port}`

    await stopService(svc)
    if (svc.build && build) {
        const built = await svc.build()
        if (!built.ok) return `${svc.name} not started: ${built.note}`
    }
    if (svc.prestart) await svc.prestart()
    fs.mkdirSync(RUN_DIR, { recursive: true })
    // 'w' so app_logs tails only the run being asked about.
    const out = fs.openSync(svc.outLog, 'w')
    const child = spawn(svc.command, svc.args, {
        cwd: svc.cwd, env: { ...env(), ...(svc.env ?? {}) }, detached: true, windowsHide: true,
        stdio: ['ignore', out, out],
    })
    child.unref()
    fs.writeFileSync(svc.pidFile, String(child.pid))

    const deadline = Date.now() + svc.readyTimeoutSec * 1000
    while (Date.now() < deadline) {
        if (await probe(svc.url) === 200) {
            return `${svc.name} ready on :${svc.port} (pid ${child.pid})`
        }
        if (!(await isAlive(child.pid)) && (await portOwners(svc.port)).length === 0) {
            return `${svc.name} FAILED to start (pid ${child.pid} exited). Check ${svc.outLog} — try app_logs.`
        }
        await sleep(2000)
    }
    return `${svc.name} did not become ready within ${svc.readyTimeoutSec}s. Check ${svc.outLog}.`
}

// --- database ---------------------------------------------------------------------------------

/**
 * Which Postgres is the running backend actually using? Asks /health rather than guessing from
 * `docker ps`, because both the compose container and a Testcontainers one can be up at once.
 */
async function activeDb() {
    let source
    try { ({ dbSource: source } = await getJson(`${BACKEND_ORIGIN}/health`)) }
    catch { throw new Error('Backend is not answering /health. Is it up? Try app_status.') }

    if (source === 'docker-compose') return { source, container: COMPOSE_CONTAINER, user: 'webly', db: 'webly' }

    const { stdout } = await sh('docker', ['ps', '--filter', `ancestor=${PG_IMAGE}`, '--format', '{{.Names}}'])
    const names = stdout.trim().split(/\r?\n/).map((n) => n.trim()).filter((n) => n && n !== COMPOSE_CONTAINER)
    if (names.length === 0) throw new Error(`Backend reports '${source}' but no matching ${PG_IMAGE} container is running.`)
    if (names.length > 1) {
        throw new Error(`${names.length} testcontainers ${PG_IMAGE} containers running (${names.join(', ')}) — an orphan from a force-killed backend. Run app_stop to clear it.`)
    }
    return { source, container: names[0], user: 'webly', db: 'webly' }
}

async function psql(sql) {
    const { container, user, db } = await activeDb()
    const { code, stdout, stderr } = await sh('docker', ['exec', container, 'psql', '-U', user, '-d', db, '-t', '-A', '-c', sql])
    if (code !== 0) throw new Error(stderr.trim() || `psql exited ${code}`)
    return stdout.trim()
}

// --- server -----------------------------------------------------------------------------------

const server = new McpServer({ name: 'webly-dev', version: '1.0.0' })

const LONG_RUNNING = new Set([
    'app_start', 'app_stop', 'app_restart', 'app_build', 'app_test',
    'client_build', 'client_typecheck', 'regen_api', 'db_compose_up', 'db_compose_down',
])

const baseRegisterTool = server.registerTool.bind(server)
server.registerTool = (name, config, handler) =>
    baseRegisterTool(name, config, LONG_RUNNING.has(name)
        ? (args, extra) => withProgress(extra, name, () => handler(args, extra))
        : handler)

server.registerTool('app_status', {
    description: 'Backend/frontend pids, listening ports, a real HTTP probe of each, and which Postgres the backend is using.',
    inputSchema: {},
}, async () => {
    const lines = []
    for (const svc of Object.values(SERVICES)) {
        const pid = readPid(svc)
        const alive = await isAlive(pid)
        const owners = await portOwners(svc.port)
        const status = await probe(svc.url)
        lines.push(
            `${svc.name}: pidfile=${pid ?? '-'}${pid ? (alive ? ' (alive)' : ' (DEAD)') : ''} ` +
            `port ${svc.port}=${owners.length ? owners.join(',') : 'free'} probe=${status || 'no response'}`)
    }
    try {
        const { source, container } = await activeDb()
        lines.push(`db: ${source} (container ${container})`)
    } catch (e) {
        lines.push(`db: ${e.message}`)
    }
    return text(lines.join('\n'))
})

const targetSchema = { target: z.enum(['backend', 'frontend', 'both']).default('backend') }
const targetsOf = (t) => (t === 'both' ? [SERVICES.backend, SERVICES.frontend] : [SERVICES[t]])

server.registerTool('app_start', {
    description: 'Start the backend and/or frontend, waiting until each actually serves. Open https://localhost:5000 afterwards.',
    inputSchema: targetSchema,
}, async ({ target }) => {
    const out = []
    for (const svc of targetsOf(target)) out.push(await startService(svc))
    return text(out.join('\n'))
})

server.registerTool('app_stop', {
    description: 'Stop the backend and/or frontend, including any process squatting on the port and any orphaned Testcontainers Postgres.',
    inputSchema: targetSchema,
}, async ({ target }) => {
    const out = []
    for (const svc of targetsOf(target).reverse()) out.push(`${svc.name}: ${await stopService(svc)}`)
    return text(out.join('\n'))
})

server.registerTool('app_restart', {
    description: 'Stop then start. If the backend was on the Testcontainers fallback, its data is gone after this; the docker-compose DB persists.',
    inputSchema: targetSchema,
}, async ({ target }) => {
    const out = []
    for (const svc of targetsOf(target).reverse()) out.push(`${svc.name}: ${await stopService(svc)}`)
    for (const svc of targetsOf(target)) out.push(await startService(svc))
    return text(out.join('\n'))
})

server.registerTool('app_logs', {
    description: 'Tail a service log, optionally filtered to lines containing a substring.',
    inputSchema: {
        target: z.enum(['backend', 'frontend']).default('backend'),
        lines: z.number().int().min(1).max(2000).default(80),
        filter: z.string().optional().describe('Only lines containing this substring'),
    },
}, async ({ target, lines, filter }) => {
    const svc = SERVICES[target]
    let content
    try { content = fs.readFileSync(svc.outLog, 'utf8') } catch { return text(`No log at ${svc.outLog}`) }
    let rows = content.split(/\r?\n/)
    if (filter) rows = rows.filter((r) => r.includes(filter))
    return text(rows.slice(-lines).join('\n') || '(no matching lines)')
})

server.registerTool('app_build', {
    description: 'Build the backend, stopping it first if it holds the output files, then restoring its previous state.',
    inputSchema: {
        restart: z.boolean().default(true).describe('Restart the backend afterwards if it was running'),
    },
}, async ({ restart }) => {
    const wasRunning = (await probe(SERVICES.backend.url)) === 200
    const notes = []
    if (wasRunning) notes.push(`stopped backend first (${await stopService(SERVICES.backend)})`)

    const built = await buildBackend()
    notes.push(built.note)

    if (wasRunning && restart && built.ok) notes.push(await startService(SERVICES.backend, { build: false }))
    else if (wasRunning && restart) notes.push('not restarting: build failed')
    return text(notes.join('\n'))
})

server.registerTool('app_test', {
    description: 'Run the backend test suite (needs Docker for Testcontainers) and report counts plus the names of any failures.',
    inputSchema: {
        filter: z.string().optional().describe('dotnet test --filter expression'),
        restart: z.boolean().default(true).describe('Restart the backend afterwards if it was running'),
    },
}, async ({ filter, restart }) => {
    const wasRunning = (await probe(SERVICES.backend.url)) === 200
    if (wasRunning) await stopService(SERVICES.backend) // the running app locks the test binaries

    const args = ['test', TESTS_PROJ, '--nologo', '--logger', 'console;verbosity=normal']
    if (filter) args.push('--filter', filter)
    const { stdout } = await sh('dotnet', args, { timeoutMs: 900_000 })

    const failed = [...stdout.matchAll(/^\s*Failed\s+(\S+)/gm)].map((m) => m[1])
    const passedCount = [...stdout.matchAll(/^\s*Passed\s+\S+/gm)].length
    const out = [`${passedCount} passed, ${failed.length} failed`]
    if (failed.length) out.push(`failing: ${failed.join(', ')}`)

    if (wasRunning && restart) out.push(await startService(SERVICES.backend))
    return text(out.join('\n'))
})

server.registerTool('db_compose_up', {
    description: 'Start the persistent docker-compose Postgres (docker-compose.dev.yml). Restart the backend afterwards to switch it off the Testcontainers fallback.',
    inputSchema: {},
}, async () => {
    const { code, stdout, stderr } = await sh('docker', ['compose', '-f', COMPOSE_FILE, 'up', '-d'], { timeoutMs: 300_000 })
    return text(code === 0 ? `compose up OK\n${stderr.trim() || stdout.trim()}` : `compose up FAILED\n${diagnose([], stdout, stderr)}`)
})

server.registerTool('db_compose_down', {
    description: 'Stop the docker-compose Postgres. Data stays in its named volume; pass wipe=true to delete it.',
    inputSchema: { wipe: z.boolean().default(false) },
}, async ({ wipe }) => {
    const args = ['compose', '-f', COMPOSE_FILE, 'down']
    if (wipe) args.push('-v')
    const { code, stdout, stderr } = await sh('docker', args, { timeoutMs: 300_000 })
    return text(code === 0 ? `compose down OK${wipe ? ' (volume deleted)' : ''}` : `compose down FAILED\n${diagnose([], stdout, stderr)}`)
})

server.registerTool('db_status', {
    description: 'Which Postgres the backend is using, how many tables it has, and the largest tables.',
    inputSchema: {},
}, async () => {
    const { source, container } = await activeDb()
    const tables = await psql(`select count(*) from information_schema.tables where table_schema='public';`)
    const top = await psql(`select relname || '=' || n_live_tup from pg_stat_user_tables order by n_live_tup desc limit 6;`)
    return text(`source: ${source}\ncontainer: ${container}\ntables: ${tables}\n${top || '(no user tables yet)'}`)
})

server.registerTool('db_query', {
    description: 'Run read-only SQL against whichever Postgres the backend is currently using.',
    inputSchema: { sql: z.string().describe('A SELECT/WITH/EXPLAIN statement') },
}, async ({ sql }) => {
    if (!/^\s*(select|with|explain|show|table)\b/i.test(sql)) {
        throw new Error('db_query is read-only.')
    }
    return text(await psql(sql) || '(no rows)')
})

server.registerTool('client_typecheck', {
    description: 'Typecheck the Angular client (tsconfig.app.json).',
    inputSchema: {},
}, async () => {
    const { code, stdout, stderr } = await sh('cmd.exe', ['/c', 'yarn typecheck'], { cwd: CLIENT_DIR })
    const errors = stdout.split(/\r?\n/).filter((l) => l.includes('error TS'))
    return text(code === 0 ? 'typecheck clean' : `typecheck FAILED\n${diagnose(errors, stdout, stderr)}`)
})

server.registerTool('client_build', {
    description: 'Build the Angular client (browser + SSR server bundles) in the development configuration.',
    inputSchema: {},
}, async () => {
    const { code, stdout, stderr } = await sh('cmd.exe', ['/c', 'yarn build --configuration development'],
        { cwd: CLIENT_DIR, timeoutMs: 900_000 })
    const errs = stdout.split(/\r?\n/).filter((l) => /ERROR|error TS/.test(l))
    return text(code === 0 ? 'client build OK' : `client build FAILED\n${diagnose(errs, stdout, stderr)}`)
})

server.registerTool('regen_api', {
    description: 'Regenerate the Angular API client (client/src/app/api) from the running backend swagger.',
    inputSchema: {},
}, async () => text(await regenApiClient()))

await server.connect(new StdioServerTransport())
