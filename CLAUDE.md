# Working in this repository

Operating notes for AI coding assistants (and humans in a hurry). The guides
in `docs/` explain the design for people; this is the short version plus the
traps.

## Shape

- `client/`: Vue 3 + TypeScript + Vite single-page app. `src/api.ts` is the only
  place that calls the API; `src/router.ts` lists every page.
- `src/WebBase.Api/`: ASP.NET Core 10 controllers. `Program.cs` wires everything
  in numbered sections.
- `src/WebBase.Core/`: the contracts, no NuGet packages: `Models/` (plain
  classes, no navigation properties), `Data/` (`IUserStore`, `INoteStore`,
  `IDatabase`, `DuplicateKeyException`), `FileStorage/` (`IFileStore`,
  `LocalFileStore`, `FilePaths`), `Modules/` (`IDatabaseModule`, `IFileStorageModule`).
  Namespaces `WebBase.Models`, `WebBase.Data`, `WebBase.FileStorage`, `WebBase.Modules`.
- `modules/`: every external dependency is its own project, referenced by the
  Api **only if requested**: `WebBase.Database.Postgres` (default; EF Core +
  Npgsql, `AppDbContext`, `Migrations/`), `WebBase.Database.CosmosDB`,
  `WebBase.FileStorage.S3`, `.AzureBlob`, `.GoogleCloud`. The Api lists the ones
  it has in `src/WebBase.Api/Modules.cs` and `<ItemGroup Label="Modules">` of its
  csproj; `./scripts/module.sh add|remove|list <name>` edits both (markers
  `// (database modules)` etc.: keep them). `Database:Provider` / `Cms:Source`
  pick by `Name`; an unknown name fails startup pointing at docs/MODULES.md.
  A file storage module reads its settings from `Cms:{Name}`. See docs/MODULES.md.
- `tests/`: xUnit v3. `tests/Directory.Build.props` gives every `*.Tests` project
  the packages and a reference to `WebBase.Testing`, which holds the contract
  suites (`StoreContractTests`, `FileStoreContractTests`) and an in-memory
  database (`InMemory/`). `WebBase.Tests`: API tests on WebApplicationFactory +
  that in-memory database (`TestApp.cs`); `ArchitectureTests` fails if the Api
  references EF/Npgsql/a cloud SDK or a constructor asks for a module type, or
  Core gains a package. One `tests/<Module>.Tests/` per module, running the
  contract against a real server that `scripts/test.sh` starts (Postgres,
  S3Mock, Azurite, fake-gcs-server: `TEST_POSTGRES`, `TEST_S3_URL`,
  `TEST_AZURE_BLOB`, `TEST_GCS_URL`); skipped without it. Cosmos needs its
  emulator (`TEST_COSMOS`), which test.sh doesn't start.
- `content/`: optional CMS (`Cms__Enabled`, off by default). Code in `Content/`:
  `ContentService` maps addresses to `.md` files, read through `IFileStore`
  (`LocalFileStore` for `Cms:Source=files`, else the file storage module of that
  name, in `ContentRegistration.cs`) and kept in memory, refreshed every
  `Cms:RefreshSeconds` (0 in dev). Store down with no copy yet → 503. Every store
  checks `FilePaths.IsSafeRelative` itself (the Azure SDK and fake-gcs collapse `..`).
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
- Cloud: `scripts/cloud/{gcp,azure,aws}.sh` each take `*_TIER=free|standard` (free stays
  inside the free tier; AWS free = Lambda via `ops/Dockerfile.lambda` + a Neon database).
  They keep secrets in the cloud itself (Secret Manager, Container App secrets,
  Parameter Store), not in `.env.local`, so they run from Cloud Shell. Scripts read settings from the environment, then
  `.env.local`, then the committed `cloud.env` (public settings only;
  `check_cloud_env` in `common.sh` refuses secret-looking keys).
  `.github/workflows/tests.yml` runs `test.sh` on every push;
  `deploy-google-cloud.yml` runs after Tests succeed on `main`, only once the
  `GCP_*` repository variables exist, authenticating with Workload Identity
  Federation (no keys) and calling `gcp.sh deploy`. docs/GOOGLE_CLOUD.md.

## Commands

Everything runs in Docker; no host SDKs are required.

| Task | Command |
|---|---|
| Start dev (hot reload) | `./dev.sh` → :5173 (port from `.env.local`) |
| All checks | `./scripts/test.sh` (all C# tests in `WebBase.slnx`, modules on a throwaway Postgres, S3Mock, Azurite, fake-gcs-server + `vue-tsc` + vite build) |
| Add/remove a module | `./scripts/module.sh add|remove|list [name]` |
| Regenerate `client/src/api-types.ts` | `./scripts/gen-types.sh` (automatic while `./dev.sh` runs) |
| New migration | `./scripts/ef.sh migrations add <Name>` (applied automatically at startup) |
| End-to-end check of a running stack | `./scripts/smoke.sh [base-url]` |
| Release prod locally / undo | `./deploy.sh` / `./deploy.sh rollback` |
| Public via Cloudflare | `./tunnel.sh on` |
| Cloud | `scripts/cloud/{azure,aws,gcp}.sh setup|deploy|url|logs|destroy` (docs/AZURE.md, AWS.md, GOOGLE_CLOUD.md); `gcp.sh github` connects Actions; generic `push-image.sh` |
| Shell scripts lint | `docker run --rm -v "$PWD:/m" -w /m koalaman/shellcheck:v0.10.0 -x -S warning *.sh scripts/*.sh scripts/cloud/*.sh` |

## Rules

- **Every store method on user data takes the owner's id** (`INoteStore.RemoveAsync(userId, id)`),
  and controllers pass `User.GetUserId()`. Return a 404 for someone else's row,
  never a 403 (don't confirm it exists). Copy the isolation test in
  `NotesApiTests.cs` and the store tests in `StoreContractTests.cs` for each new resource.
- **No external packages in the Api or Core.** EF, cloud SDKs and the like
  live in a module under `modules/`; controllers and services ask for Core
  interfaces. A new store = interface in `src/WebBase.Core/Data/`, Postgres
  implementation + `DbSet` + registration in `PostgresModule.Register`, an
  in-memory one in `tests/WebBase.Testing/InMemory/` (the API tests need it),
  Cosmos if that module is used, contract tests. Store contract:
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

- The API tests' database is `WebBase.Testing/InMemory`, not EF: Postgres-specific
  behaviour is only covered by `tests/WebBase.Database.Postgres.Tests`.
- Migrations live in `modules/WebBase.Database.Postgres/Migrations/` (namespace
  `WebBase.Database.Postgres.Migrations`); `ef.sh` passes `--project` for that module.
- Build output goes to `.build/` (root `Directory.Build.props`, `UseArtifactsOutput`),
  not `bin/obj`. Containers mount an anonymous volume over `/src/.build`.
- The solution is `WebBase.slnx`; a new project must be added there to be tested.
- `cloud.env` is committed: never put a secret in it. Secrets go to the cloud's
  secret store via `setup`.
- Prod's database container is in `compose.prod-db.yaml`, added by `dc prod` in
  `scripts/lib.sh` only while `PROD_DATABASE_URL` is empty. A prod-only setting for
  the db container goes there, not in `compose.prod.yaml`.
- `PORT` env var overrides the 8080 listen port (for cloud hosts); don't set it locally.
- In dev, the build output (`.build/`) lives in a Docker volume, separate from any
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
