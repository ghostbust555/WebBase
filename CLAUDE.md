# Working in this repository

Operating notes for AI coding assistants (and humans in a hurry). The guides
in `docs/` explain the design for people; this is the short version plus the
traps.

## Shape

- `client/`: Vue 3 + TypeScript + Vite single-page app. `src/api.ts` is the only
  place that calls the API; `src/router.ts` lists every page.
- `src/WebBase.Api/`: ASP.NET Core 10 controllers + EF Core on PostgreSQL.
  `Program.cs` wires everything in numbered sections.
- `tests/WebBase.Tests/`: xUnit, WebApplicationFactory + EF InMemory (`TestApp.cs`).
- Prod = one image (`ops/Dockerfile`): the API serves the built SPA from wwwroot.
  Dev = three containers (`compose.dev.yaml`) running source with hot reload;
  Vite proxies `/api` to the API.

## Commands

Everything runs in Docker; no host SDKs are required.

| Task | Command |
|---|---|
| Start dev (hot reload) | `./dev.sh` → :5173 (port from `.env.local`) |
| All checks | `./scripts/test.sh` (C# tests + `vue-tsc` + vite build) |
| New migration | `./scripts/ef.sh migrations add <Name>` (applied automatically at startup) |
| End-to-end check of a running stack | `./scripts/smoke.sh [base-url]` |
| Release prod locally / undo | `./deploy.sh` / `./deploy.sh rollback` |
| Public via Cloudflare | `./tunnel.sh on` |
| Cloud | `scripts/cloud/{azure,aws,gcp}.sh setup|deploy`; generic `push-image.sh` |
| Shell scripts lint | `docker run --rm -v "$PWD:/m" -w /m koalaman/shellcheck:v0.10.0 -x -S warning *.sh scripts/*.sh scripts/cloud/*.sh` |

## Rules

- **Every query on user data filters by `User.GetUserId()`.** Return a 404 for
  someone else's row, never a 403 (don't confirm it exists). Copy the isolation
  test in `NotesApiTests.cs` for each new resource.
- **Return DTOs from `Contracts/`, never EF models.**
- **Errors are `{ "error": "<human sentence>" }`**. The client shows them verbatim.
- **No secrets in `appsettings.json`, compose files or the client.** Secrets go
  in `.env.local` and are mapped in `compose.*.yaml`; see docs/CONFIGURATION.md.
  `/api/config` is public: nothing secret goes in it.
- **Model change ⇒ migration in the same commit.** Use `ef.sh`, not a host
  `dotnet ef`, so the generated files aren't root-owned and the tool version matches.
- Prod's app role (`<slug>_app`) cannot run DDL. Migrations run on
  `ConnectionStrings__Migration` (the owner). Anything that needs DDL at runtime
  will fail in prod while working in dev, where one role owns everything.
- Rollback swaps images, not schemas: keep migrations backwards-compatible
  (expand, then contract in a later release).
- `npm run build` runs `vue-tsc --noEmit` first; a type error fails the build
  and therefore the Docker build.

## Traps

- EF InMemory (tests) doesn't support `ExecuteUpdate/ExecuteDelete` or raw SQL.
  Either avoid them or test those paths against Postgres.
- `PORT` env var overrides the 8080 listen port (for cloud hosts); don't set it locally.
- In dev, `bin/` and `obj/` of the API live in Docker volumes, separate from any
  host build. If a container build acts stale: `docker compose -f compose.dev.yaml down -v`
  (drops those volumes, **not** the database, which is in `.docker-data/`).
- `.docker-data/` is written by containers as other uids; delete it via
  `./dev.sh reset-db` or a container, not plain `rm`.
- Postgres 18 images mount at `/var/lib/postgresql`, not `.../data`.
- `scripts/rename.sh` rewrites `WebBase`/`webbase` everywhere; run it before any
  data exists (database names change).
