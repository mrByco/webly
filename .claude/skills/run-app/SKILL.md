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

1. Backend tries the persistent docker-compose Postgres (`docker-compose.dev.yml`, `localhost:5434` — not 5432, so a native Postgres install can't shadow it; container `webly-postgres-dev`).
2. If that is unreachable it falls back to an ephemeral Testcontainers Postgres (needs Docker Desktop). **That data is gone on the next restart.**
3. `GET https://localhost:5000/health` → `{ status, dbSource }` where `dbSource` is `docker-compose` or `testcontainers`. `app_status` shows the same.

To work with data that survives restarts: `db_compose_up`, then `app_restart`.

## Things that cost time if unknown

- The dev HTTPS cert must be trusted once: `dotnet dev-certs https --trust`.
- A force-killed backend can orphan a Testcontainers Postgres; `app_stop` clears those (never the compose container).
- Logs and pidfiles live in `.run/` (gitignored). `app_logs` reads the current run only.
- `Webly.Api` locks its build output while running — `app_build` handles the stop/build/start dance.
- The solution file is `Webly.slnx` (new .NET 10 format), not `.sln`.
