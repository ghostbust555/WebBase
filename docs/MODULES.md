# Modules: databases and file storage

The app itself never talks to a particular database or cloud. Each of those
is a **module**: a small C# project of its own in `modules/`, with its own
packages, that the app is built with **only if you ask for it**. A site that
keeps its pages in Google Cloud Storage carries Google's storage library; one
that doesn't, doesn't.

| Module | Kind | In the app | What it is |
|---|---|---|---|
| `WebBase.Database.Postgres` | database | **yes, by default** | PostgreSQL via Entity Framework Core |
| `WebBase.Database.CosmosDB` | database | no | Azure Cosmos DB for NoSQL |
| `WebBase.FileStorage.S3` | file storage (`s3`) | no | Amazon S3, Cloudflare R2, DigitalOcean Spaces, Backblaze B2, MinIO |
| `WebBase.FileStorage.AzureBlob` | file storage (`azure`) | no | Azure Blob Storage |
| `WebBase.FileStorage.GoogleCloud` | file storage (`gcs`) | no | Google Cloud Storage |

Each folder in `modules/` has a `README.md` with its settings. A folder on
disk (`files`, the CMS's default) is built in: it needs no package.

Every module is compiled and tested by `./scripts/test.sh` whether the app
uses it or not, so one you add later still works.

## Adding or removing one

```bash
./scripts/module.sh list           # which exist, which the app uses
./scripts/module.sh add gcs        # build the app with Google Cloud Storage
./scripts/module.sh remove cosmos  # and without one again
```

Adding is two edits, which you can also make by hand:

1. A reference in `src/WebBase.Api/WebBase.Api.csproj`, in the group labelled `Modules`:
   ```xml
   <ProjectReference Include="..\..\modules\WebBase.FileStorage.GoogleCloud\WebBase.FileStorage.GoogleCloud.csproj" />
   ```
2. The module in its list in `src/WebBase.Api/Modules.cs`, with its `using`:
   ```csharp
   new GoogleCloudStorageModule(),
   ```

Neither needs Docker or an SDK, so it works from anywhere, Claude Code on the
web included. Commit both files. Then choose it with its setting: `Database__Provider`
for a database, `CMS_SOURCE` for the CMS's files. The module's README lists
the rest of its settings.

A setting that names a module the app isn't built with stops the app at
startup, with a message saying which modules it has and pointing here.

Removing one is the reverse, and the module's folder can stay: it's still
built and tested, just not part of the app. To be rid of it entirely, also
delete `modules/<it>/` and `tests/<it>.Tests/`, and their lines in `WebBase.slnx`.

## How it's put together

```
 src/WebBase.Core/       the contracts: models, IUserStore, INoteStore, IDatabase,
                         IFileStore, and what a module is. No packages at all.
        ▲      ▲
        │      └───────────── modules/WebBase.Database.*     (each: its own packages)
        │                     modules/WebBase.FileStorage.*
        │                            ▲
 src/WebBase.Api/        the app ────┘ references only the modules in Modules.cs
```

| Piece | File |
|---|---|
| What a database module provides | `src/WebBase.Core/Modules/IDatabaseModule.cs`: a name, and `Register` (adds `IDatabase`, `IUserStore`, `INoteStore`) |
| What a file storage module provides | `src/WebBase.Core/Modules/IFileStorageModule.cs`: a name, and `Create` (an `IFileStore` from its settings section) |
| Which modules the app has, and choosing one | `src/WebBase.Api/Modules.cs` |
| The contract every database keeps | `tests/WebBase.Testing/StoreContractTests.cs` |
| The contract every file store keeps | `tests/WebBase.Testing/FileStoreContractTests.cs` |
| Keeping the app free of SDKs | `tests/WebBase.Tests/ArchitectureTests.cs`: fails if the app references Entity Framework, Npgsql, or an AWS / Azure / Google storage library itself |

A file storage module's settings are the section named after it: the `s3`
module reads `Cms:S3` (`Cms__S3__Bucket`...), `gcs` reads `Cms:Gcs`.

## Writing a new one

Say, `WebBase.FileStorage.Dropbox`, or `WebBase.Database.MongoDB`:

1. **The project**: copy the closest module's folder in `modules/` and rename
   it (folder, `.csproj`, namespace). It references `src/WebBase.Core` and
   whatever packages it needs, nothing from the app.
2. **The code**: implement the interfaces (`IFileStore`, or the three stores)
   and a module class with its `Name`. Throw an `InvalidOperationException`
   that says which setting is missing, rather than failing later.
3. **The tests**: copy the matching folder in `tests/` (e.g.
   `tests/WebBase.FileStorage.S3.Tests/`). Its one class derives from the
   contract suite and says how to make an empty bucket or database. If the
   tests need a server, give `./scripts/test.sh` a stand-in container for it,
   as it has for S3, Azure and Google; otherwise they skip themselves.
4. Add both projects to `WebBase.slnx`, and the module to `scripts/module.sh`'s
   list (`info()`).
5. `./scripts/test.sh`. The module is done when the contract passes.

Things worth copying from the existing ones: every file store checks paths with
`FilePaths.IsSafeRelative` itself, because some clients quietly resolve
`../` and would read outside their folder; and every database turns its own
"already exists" error into `DuplicateKeyException`.
