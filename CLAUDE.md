# Working in this repository

Operating instructions for AI coding assistants (and humans in a hurry). The guides
in `docs/` explain the design for people; this is the short version plus the
traps.

## Shape

- `client/`: Vue 3 + TypeScript + Vite single-page app. `src/api.ts` is the only
  place that calls the API; `src/router.ts` lists every page.
- `src/WebBase.Api/`: ASP.NET Core 10 controllers. `Program.cs` wires everything
  in numbered sections.
- `src/WebBase.Core/`: the contracts, no NuGet packages: `Models/` (plain
  classes, no navigation properties), `Data/` (`IUserStore`, `IPageStore`,
  `IDatabase`, `DuplicateKeyException`), `FileStorage/` (`IFileStore`,
  `LocalFileStore`, `FilePaths`), `Modules/` (`IDatabaseModule`, `IFileStorageModule`).
  Namespaces `WebBase.Models`, `WebBase.Data`, `WebBase.FileStorage`, `WebBase.Modules`.
- `modules/`: every external dependency is its own project, referenced by the
  Api **only if requested**: `WebBase.Database.Postgres` (default; EF Core +
  Npgsql, `AppDbContext`, `Migrations/`), `WebBase.Database.CosmosDB`, `WebBase.Database.Firestore`,
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
  emulator (`TEST_COSMOS`), which test.sh doesn't start; so does Firestore (`TEST_FIRESTORE`,
  host:port of its Java emulator, runs without Docker: the module's README).
- **Authors** (docs/AUTHORS.md): `User.IsAuthor`. The first account ever created is an author
  (`AuthService`: `IUserStore.AnyAsync()`; a tiny race between two simultaneous first sign-ups is
  accepted and documented), and so is a *Google-verified* email in `Auth:AuthorEmails` (never a
  password sign-up: its email is unverified). `[Authorize, RequireAuthor]`
  (`Infrastructure/RequireAuthor.cs`) reads the user from the database on every request, not the
  token, so revoking is immediate; 401 signed out, 403 `{error}` otherwise. `/api/authors` lists,
  adds (an existing account, by email) and removes (never yourself, so one always remains).
  Client: `meta: { requiresAuthor: true }` in `router.ts`, `auth.user?.isAuthor` for links.
- **Pages written in the browser** (docs/PAGES.md): `Page` + `IPageStore` (Postgres, Cosmos, Firestore, in-memory),
  public `/api/pages/menu` and `/published?path=`, authors' `/api/editor/pages` (CRUD). Rules in
  `Pages/PageRules.cs`: limits, address words, `Reserved` first words (**add a word when you add a
  client route; a test compares it with `router.ts`**), and `MaxBody` kept under `Abuse:MaxBodyKb`
  (a test checks). **Files in `content/` win** over a written page at the same address; creating one
  there is a 409. `ContentView.vue` tries the file, then the written page; `stores/menu.ts` merges
  both menus. Cosmos: one partition (`/site`) with a unique key on `/path`.
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
- Cloud images are built with `--build-arg MODULES=...` (`build_modules` in common.sh: the
  chosen database + `CMS_SOURCE`'s storage), which makes `module.sh set` run in ops/Dockerfile:
  a module is a setting, not a code change. Postgres is a module like the others; Azure free
  defaults to Cosmos (`AZURE_DATABASE`, region `westus2`); Google Cloud free defaults to Firestore for a new site
  (`GCP_DATABASE`; `pick_database` in gcp.sh: a site with a Postgres secret stays on Postgres, and
  `deploy` reads `Database__Provider` from the live service, never switching). Firestore needs no
  secret: Cloud Run's service account gets `roles/datastore.user`. Firestore uniqueness is a
  transaction over `keys` documents (hash ids); per-user data lives in `users/{id}/<collection>`.
- Azure's image comes from GitHub by default (`AZURE_REGISTRY=ghcr`): `.github/workflows/publish-image.yml`
  runs on every push to main (not after Tests), builds with `DB_MODULE="postgres cosmos"` (one image, either database;
  `Database__Provider` picks, so azure.sh sets it for postgres too) and pushes
  `ghcr.io/<owner>/webbase:sha-<12-char commit>` and `:latest` via `push-image.sh`, keeping 10 versions.
  `azure.sh setup|deploy` never builds: it checks that `sha-<HEAD>` exists on ghcr.io (`ghcr_published`,
  anonymous or with `GHCR_TOKEN`) and points the app at it. A private package needs `GHCR_TOKEN` (classic,
  read:packages), asked once by setup and stored as the app's registry secret (`ghcr_saved_token` reads it
  back). `AZURE_REGISTRY=acr` keeps the Azure Container Registry path (~$5/month).
  `azure.sh github` makes a `<slug>-deploy` user-assigned identity with a federated credential for
  `repo:<owner>/<repo>:ref:refs/heads/main` (Contributor on the app, Reader on the group) and prints
  the four `AZURE_*` repository variables; publish-image.yml's `deploy` job (only when
  `vars.AZURE_CLIENT_ID` is set) logs in with azure/login (OIDC) and runs `azure.sh deploy`.
  Long Azure creates use `--no-wait` + `wait_provisioned` (Cloud Shell's az can lose track of them).
- Cloud: `scripts/cloud/{gcp,azure,aws}.sh` each take `*_TIER=free|standard` (free stays
  inside the free tier; AWS free = Lambda via `ops/Dockerfile.lambda` + a Neon database).
  They keep secrets in the cloud itself (Secret Manager, Container App secrets,
  Parameter Store), not in `.env.local`, so they run from Cloud Shell. Scripts read settings from the environment, then
  `.env.local`, then the committed `cloud.env` (public settings only;
  `check_cloud_env` in `common.sh` refuses secret-looking keys).
  `.github/workflows/tests.yml` runs `test.sh` **only when started** (workflow_dispatch: Actions →
  Tests, or `gh workflow run Tests --ref <branch>`), never on push: the user asks for tests;
  `deploy-google-cloud.yml` runs on every push to `main`, only once the
  `GCP_*` repository variables exist, authenticating with Workload Identity
  Federation (no keys) and calling `gcp.sh deploy`. docs/GOOGLE_CLOUD.md.

## Commands

Docker is preferred, but not required: with no daemon, `./setupenvironment.sh --yes` installs .NET, Node and PostgreSQL on the host (writes `.env.host`, gitignored), and `dev.sh`-equivalent `scripts/dev-host.sh`, `test.sh`, `ef.sh` and `gen-types.sh` use them (docs/ENVIRONMENT.md). In host mode the S3/Azure/GCS module tests are skipped. Scripts use `$SLUG`, so `rename.sh` keeps working.

| Task | Command |
|---|---|
| Prepare this machine | `./setupenvironment.sh [--check|--yes|--docker|--no-docker|--with-postgres|--without-postgres]`; idempotent, never overwrites `.env.local`/databases/roles |
| Start dev (hot reload) | `./dev.sh` → :5173 (port from `.env.local`); no Docker: `./scripts/dev-host.sh up|stop|status|logs` |
| All checks | `./scripts/test.sh` (all C# tests in `WebBase.slnx`, modules on a throwaway Postgres, S3Mock, Azurite, fake-gcs-server + `vue-tsc` + vite build) |
| Add/remove a module | `./scripts/module.sh add|remove|list [name]` |
| Regenerate `client/src/api-types.ts` | `./scripts/gen-types.sh` (automatic while `./dev.sh` runs) |
| New migration | `./scripts/ef.sh migrations add <Name>` (applied automatically at startup) |
| End-to-end check of a running stack | `./scripts/smoke.sh [base-url]` |
| Release prod locally / undo | `./deploy.sh` / `./deploy.sh rollback` |
| Public via Cloudflare | `./tunnel.sh on` |
| Cloud | `scripts/cloud/{azure,aws,gcp}.sh setup|deploy|url|logs|destroy` (docs/AZURE.md, AWS.md, GOOGLE_CLOUD.md); `gcp.sh github` and `azure.sh github` connect Actions; generic `push-image.sh` |
| Shell scripts lint | `docker run --rm -v "$PWD:/m" -w /m koalaman/shellcheck:v0.10.0 -x -S warning *.sh scripts/*.sh scripts/cloud/*.sh` |

## Rules

- **Every store method on user data takes the owner's id** (`RemoveAsync(userId, id)`),
  and controllers pass `User.GetUserId()`. Return a 404 for someone else's row,
  never a 403 (don't confirm it exists). Write an isolation test (two users) and the store
  tests in `StoreContractTests.cs` for each new resource; docs/DEVELOPING.md walks through it.
  **Exception: site-owned content** (the pages authors write) has no owner, so its store takes
  no owner id and `[Authorize, RequireAuthor]` on every writing endpoint is the guard
  (docs/AUTHORS.md). The repository may also ship a finished per-user example; if it is still
  here, docs/EXAMPLE_NOTES.md lists its files and how to remove it.
- **That example has exactly one doc**, `docs/EXAMPLE_NOTES.md`: no other guide, README or
  CLAUDE.md may mention it by name, so deleting that file leaves everything readable.
  `ExampleDocIsolationTests` fails on a mention. Use a neutral example ("Recipes") elsewhere.
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
- **Every request passes `AbuseGuardMiddleware`** (first in the pipeline, prod only by default):
  per-address ban, total cap, body limit (`Abuse__*`). The client address is
  `RateLimits.ClientIp`: RemoteIpAddress after `UseForwardedHeaders`; a header only if
  `RateLimits:ClientIpHeader` names one (never trust `CF-Connecting-IP` by default: spoofable).
  docs/CLOUD.md, "Floods of requests".
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

- The first account made on a `TestApp` is an author, so `SignedInClientAsync()` first makes a
  throwaway account and everyone after is ordinary; `AuthorClientAsync()` makes an author. A test
  of the first-account rule itself needs a fresh `TestApp` and neither helper.
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
