---
name: run-app
description: Start, stop, inspect and debug the Webly dev stack (ASP.NET backend on :5000 proxying the Angular dev server on :4200, hybrid docker-compose/Testcontainers Postgres). Use when asked to run the app, check whether it is up, read its logs, or when a change needs to be seen working in the browser.
---

# Running the Webly stack

## Prefer the `webly-dev` MCP tools

`tools/dev-mcp/index.js` is wired in `.mcp.json`. Use it before shelling out:

| Need | Tool |
|---|---|
| Is it up? Which DB is it on? | `app_status` |
| Start / stop / restart | `app_start`, `app_stop`, `app_restart` (target: backend, frontend, both) |
| Rebuild backend after a C# change | `app_build` (stops, builds, restarts) |
| Backend tests | `app_test` (optional `filter`) |
| Read logs | `app_logs` (target, lines, filter) |
| Persistent DB on/off | `db_compose_up`, `db_compose_down` |
| Look at the DB | `db_status`, `db_query` (read-only SQL) |
| After a controller/DTO change | `regen_api` |
| Angular checks | `client_typecheck`, `client_build` |

Fallback without MCP: `./run-app.ps1 start|stop|status|logs` and `./regen-api.ps1` from the repo root.

## Topology — read this before debugging "the frontend doesn't load"

- **The browser only ever talks to `https://localhost:5000`** (the backend). It serves `/api/*`,
  `/health`, `/swagger` itself and reverse-proxies everything else (YARP) to the Angular dev server.
- The Angular dev server (`ng serve`) runs on plain `http://localhost:4200` and is an internal
  detail. Do not point the browser at it and do not add CORS — same-origin is by construction.
- In production the same proxy points at the built Angular SSR node server (`node dist/client/server/server.mjs`, port 4000).
- Start order matters: backend first, because the frontend's `prestart` regenerates
  `client/src/app/api/` from the backend's live swagger.

## Database — hybrid, and `/health` tells you which

1. Backend tries whatever Postgres is on `localhost:5434` — the docker-compose one (`docker-compose.dev.yml`, container `webly-postgres-dev`) or one installed locally; the port is all it can tell apart. Not 5432, so a native Postgres install can't shadow it.
2. If nothing answers it falls back to an ephemeral Testcontainers Postgres (needs Docker Desktop). **That data is gone on the next restart.**
3. If Docker is missing too, startup fails with a sentence naming both remedies rather than a Testcontainers stack trace. Starting any Postgres on 5434 with the database, user and password from `ConnectionStrings:WeblyDb` is the other way out.
4. `GET https://localhost:5000/health` → `{ status, dbSource }` where `dbSource` is `persistent` or `testcontainers`. `app_status` shows the same.

To work with data that survives restarts: `db_compose_up`, then `app_restart`.

## Editing a site: what it needs, and what it needs nothing for

An agent turn does not run on the backend — it runs in a sandbox. The development defaults are picked so that
needs **node and git and nothing else**:

- `Sandbox:Provider` is `local`, so a turn spawns `tools/sandbox-agent` as a child process with a workspace
  under `.run/workspaces`. Not isolation, and refused outside Development. For real isolation set it to
  `docker` and build the image first:
  `docker build -f deploy/sandbox/Dockerfile -t byc0/margareta:webly_sandbox .` from the repository root (the
  context is the root, not `deploy/sandbox`). Without the image, the first message fails with "the sandbox
  container never became reachable".
- `Agent:Mock:Enabled` is true, so with no model key the chat still works — the mock agent makes one real
  edit to the home page's headline. For the real thing:
  `dotnet user-secrets set "Agent:ClaudeCode:ApiKey" "<key>" --project Webly.Api`, and it takes over with no
  settings change. With the mock off and no key the chat is *absent* rather than broken:
  `/api/sites/{nanoid}/chat/status` reports `enabled: false` and the client hides it, which is easy to
  mistake for a bug in the client.
- `Deployment:Provider` is `filesystem`, so publishing runs the real `next build` and writes the export to
  `.run/published/{slug}`, reachable at `https://localhost:5000/published/{slug}/`.

The preview is that sandbox's own `next dev`, proxied through
`https://localhost:5000/api/sites/{nanoid}/preview/`. So an empty preview pane usually means no workspace, not
a broken renderer — there is no renderer. A workspace only starts when a turn does, which is why a cold
editor shows "your preview is asleep".

Site repositories live in `.run/repositories/{nanoid}.git` (gitignored). To see what a turn actually did:
`git --git-dir .run/repositories/<nanoid>.git log --stat`.

## Driving it without the backend at all

`node tools/e2e/run.mjs --agent mock` runs the whole product loop — template, commit, sandbox, dev server,
preview, agent turn, commit, restore, build, publish — with no .NET, Docker, database or credentials. Use it
when the question is "does the loop work" rather than "does this screen look right", and read
`tools/e2e/README.md` first.

## Things that cost time if unknown

- The dev HTTPS cert must be trusted once: `dotnet dev-certs https --trust`.
- A force-killed backend can orphan a Testcontainers Postgres; `app_stop` clears those (never the compose container).
- Logs and pidfiles live in `.run/` (gitignored). `app_logs` reads the current run only.
- `Webly.Api` locks its build output while running — `app_build` handles the stop/build/start dance.
- The solution file is `Webly.slnx` (new .NET 10 format), not `.sln`.
