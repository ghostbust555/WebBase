# Working in this repository

Operating notes for AI coding assistants (and humans in a hurry). The guides
in `docs/` explain the design for people; this is the short version plus the
traps.

## Shape

- `client/`: Vue 3 + TypeScript + Vite single-page app. `src/api.ts` is the only
  place that calls the API; `src/router.ts` lists every page.
- `src/WebBase.Api/`: ASP.NET Core 10 controllers. `Program.cs` wires everything
  in numbered sections.
- Data access goes only through store interfaces in `Data/` (`IUserStore`,
  `INoteStore`, `IDatabase`); `Data/DatabaseRegistration.cs` picks the
  implementation from `Database:Provider`. `Data/Postgres/` (EF Core + Npgsql,
  `AppDbContext`, `Migrations/`) is the only code allowed to use EF. Models are
  plain classes with no navigation properties. See docs/DATABASE.md.
- `examples/cosmos-db/`: a tested Cosmos DB implementation of the stores, not
  compiled until copied into `src/` (its README).
- `tests/WebBase.Tests/`: xUnit v3. API tests: WebApplicationFactory + EF InMemory
  (`TestApp.cs`). Store tests: `StoreContract/StoreContractTests.cs`, one abstract
  suite every implementation runs (`PostgresStoreTests` needs `TEST_POSTGRES`,
  which `scripts/test.sh` provides; skipped without it). `ArchitectureTests`
  fails if anything outside `Data/` takes an EF/Npgsql/concrete-store dependency.
  `ContentStores/`: one contract suite for every `IContentStore`; S3 and Azure
  run against S3Mock / Azurite that `test.sh` starts (`TEST_S3_URL`, `TEST_AZURE_BLOB`).
- `content/`: optional CMS (`Cms__Enabled`, off by default). Code in `Content/`:
  `ContentService` maps addresses to `.md` files, read through `IContentStore`
  (`FileContentStore`, `S3ContentStore`, `AzureBlobContentStore`, picked by
  `Cms:Source` in `ContentRegistration.cs`) and kept in memory, refreshed every
  `Cms:RefreshSeconds` (0 in dev). Store down with no copy yet → 503. Every store
  checks `ContentPaths.IsSafeRelative` itself (the Azure SDK collapses `..`).
  `ContentController` also serves `/content/{**path}` (`ContentPaths.IsPublicFile`:
  never `.md`, `_` or `.` names) and `/api/content/theme.css` (`ThemeCss`: only
  `--var` declarations from `:root` survive; linked from `main.ts`). The SPA's
  catch-all route (`ContentView.vue`) asks `/api/content/page`. Markdown is rendered
  client-side via `lib/markdown.ts` (marked + DOMPurify), the only sanctioned path
  to `v-html`. See docs/CMS.md.
- `client/src/api-types.ts` is **generated** from the controllers' C# types by
  `Infrastructure/TypeScriptContracts.cs` (dev rewrites it on start and hot
  reload; else `./scripts/gen-types.sh`). Never edit it; `api.ts` imports from it
  and checks request bodies with `satisfies`. `TypeScriptContractsTests` fails
  while it's stale.
- The home page (`HomeView.vue`) shows docs/GETTING_STARTED.md until replaced, or
  `content/index.md` when the CMS is on. `docs/*.md` and `README.md` are bundled
  into the client at build time (`lib/docs.ts`, `import.meta.glob`), so the
  client build needs the repo root, not just `client/` (Dockerfile and
  compose.dev.yaml provide it).
- Prod = one image (`ops/Dockerfile`): the API serves the built SPA from wwwroot.
  Dev = three containers (`compose.dev.yaml`) running source with hot reload;
  Vite proxies `/api` to the API.

## Commands

Everything runs in Docker; no host SDKs are required.

| Task | Command |
|---|---|
| Start dev (hot reload) | `./dev.sh` → :5173 (port from `.env.local`) |
| All checks | `./scripts/test.sh` (C# tests incl. stores on a throwaway Postgres, S3Mock, Azurite + `vue-tsc` + vite build) |
| Regenerate `client/src/api-types.ts` | `./scripts/gen-types.sh` (automatic while `./dev.sh` runs) |
| New migration | `./scripts/ef.sh migrations add <Name>` (applied automatically at startup) |
| End-to-end check of a running stack | `./scripts/smoke.sh [base-url]` |
| Release prod locally / undo | `./deploy.sh` / `./deploy.sh rollback` |
| Public via Cloudflare | `./tunnel.sh on` |
| Cloud | `scripts/cloud/{azure,aws,gcp}.sh setup|deploy`; generic `push-image.sh` |
| Shell scripts lint | `docker run --rm -v "$PWD:/m" -w /m koalaman/shellcheck:v0.10.0 -x -S warning *.sh scripts/*.sh scripts/cloud/*.sh` |

## Rules

- **Every store method on user data takes the owner's id** (`INoteStore.RemoveAsync(userId, id)`),
  and controllers pass `User.GetUserId()`. Return a 404 for someone else's row,
  never a 403 (don't confirm it exists). Copy the isolation test in
  `NotesApiTests.cs` and the store tests in `StoreContractTests.cs` for each new resource.
- **No EF outside `Data/Postgres/`.** Controllers and services ask for store
  interfaces. A new store = interface in `Data/`, Postgres implementation +
  `DbSet` + registration in `Data/Postgres/`, contract tests. Store contract:
  reads return copies (AsNoTracking; `AppDbContext.SaveAsync` clears the tracker),
  uniqueness violations throw `DuplicateKeyException`, UTC times.
- **Return DTOs from `Contracts/`, never EF models.** Actions return
  `ActionResult<T>`/`T`, or carry `[ProducesResponseType<T>(200)]` / `(204)` when
  they return `IActionResult`; otherwise type generation fails. Commit
  `api-types.ts` with any contract change. Enums serialize as strings.
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

- EF InMemory (API tests) doesn't support `ExecuteUpdate/ExecuteDelete` or raw SQL,
  and doesn't enforce unique indexes. The store contract tests cover those on real Postgres.
- Migrations live in `Data/Postgres/Migrations/` (namespace `WebBase.Api.Data.Postgres.Migrations`);
  `ef.sh migrations add` puts new ones there by itself.
- Prod's database container is in `compose.prod-db.yaml`, added by `dc prod` in
  `scripts/lib.sh` only while `PROD_DATABASE_URL` is empty. A prod-only setting for
  the db container goes there, not in `compose.prod.yaml`.
- `PORT` env var overrides the 8080 listen port (for cloud hosts); don't set it locally.
- In dev, `bin/` and `obj/` of the API live in Docker volumes, separate from any
  host build. If a container build acts stale: `docker compose -f compose.dev.yaml down -v`
  (drops those volumes, **not** the database, which is in `.docker-data/`).
- `.docker-data/` is written by containers as other uids; delete it via
  `./dev.sh reset-db` or a container, not plain `rm`.
- Postgres 18 images mount at `/var/lib/postgresql`, not `.../data`.
- Links inside docs/*.md must be plain `OTHER_DOC.md` or `OTHER_DOC.md#anchor`
  (GitHub-style slugs) so they work both on GitHub and in the /docs viewer.
- Adding a cloud SDK package: Azure.Identity conflicts with Azure.Core 1.60+
  (both define `DefaultAzureCredential`); use the one in Azure.Core.
- In dev, `/content/...` reaches the API only through the `^/content/` proxy
  rule in vite.config.ts; a new server-side path prefix needs its own rule there.
- `scripts/rename.sh` rewrites `WebBase`/`webbase` everywhere; run it before any
  data exists (database names change).
