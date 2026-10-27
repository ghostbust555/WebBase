# The database

The site stores its data in **PostgreSQL** unless you change it. The rest of
the app never talks to PostgreSQL directly. It goes through a few small C#
interfaces, the **stores**. So you can change where the data lives, a managed
cloud Postgres or a different database altogether such as Azure Cosmos DB,
without touching the controllers, the sign-in rules or the website.

This page covers:

- [Where your data should live](#where-your-data-should-live)
- [How the database layer is built](#how-the-database-layer-is-built)
- [The contract every database must keep](#the-contract-every-database-must-keep)
- [Switching to another database (Cosmos DB as the example)](#switching-to-another-database)

## Where your data should live

| Where | Use |
|---|---|
| **Development** (`./dev.sh`) | A PostgreSQL container on your computer. Nothing to set up |
| **Production in a cloud** | **The cloud's managed PostgreSQL**: Azure Database for PostgreSQL, Amazon RDS, Google Cloud SQL, or a hosted one such as Neon or Supabase. The cloud scripts set one up for you ([CLOUD.md](CLOUD.md)) |
| **Production on your own server** | A managed PostgreSQL is recommended here too: set `PROD_DATABASE_URL` ([DEPLOYING.md](DEPLOYING.md#a-managed-database)). The database container that `./deploy.sh` runs by default is fine for a small site, **as long as you take backups and copy them elsewhere** |

Why managed? A database container keeps your data on one disk, and you're the
one who has to take backups, test them, and apply updates. A managed database
takes backups every day, can restore to any minute of the last week or more,
installs security updates, and can keep a standby copy ready if a machine
fails. It costs roughly $0-30/month at small sizes, and it's the safest place
for the one part of the site you can't rebuild from git.

**Never run the database as a container on a cloud container host** (Azure
Container Apps, ECS, Cloud Run, Render, Fly machines...). Those containers
are replaced on every release and whenever the host moves them, and their
disks go with them. Containers there are for the app. The data goes in the
managed database.

## How the database layer is built

```
 Controllers, AuthService                  only know the interfaces
        │
        ▼
 src/WebBase.Core/Data/                    the contract: IUserStore, IPageStore, IDatabase
        │
        ▼  chosen by Database__Provider (src/WebBase.Api/Modules.cs)
 modules/WebBase.Database.Postgres/        one implementation: EF Core + PostgreSQL
   AppDbContext.cs, PostgresUserStore.cs, PostgresPageStore.cs,
   PostgresDatabase.cs (migrations, health), Migrations/
```

| File | What it is |
|---|---|
| `src/WebBase.Core/Models/*.cs` | The records (`User`, `Page`): plain C# classes, with no database code |
| `src/WebBase.Core/Data/IUserStore.cs`, `IPageStore.cs` | What the app can ask for: "find a user by email", "list the published pages"... |
| `src/WebBase.Core/Data/IDatabase.cs` | Getting the database ready at startup, and "can I reach it?" for `/api/health`. Also `DuplicateKeyException` |
| `src/WebBase.Api/Modules.cs` | The database modules the app is built with; reads `Database__Provider` and registers the matching one |
| `modules/WebBase.Database.Postgres/` | The PostgreSQL implementation, and the only code that uses Entity Framework |

Each implementation is a **module**: its own project, with its own packages
([MODULES.md](MODULES.md)). The app only carries the ones listed in `Modules.cs`.

Two things keep this honest:

- **Entity Framework is only visible in the Postgres module.** The app doesn't
  reference it at all, and a test (`tests/WebBase.Tests/ArchitectureTests.cs`)
  fails if the app gains a reference to EF, Npgsql or a cloud SDK, or a
  constructor asks for a module's own type.
- **The contract is tested, not just written down.** Every implementation
  runs the same tests (next section). `./scripts/test.sh` runs them against a
  real PostgreSQL that it starts for the purpose. The API's own tests use an
  in-memory implementation (`tests/WebBase.Testing/InMemory/`) that passes the
  same contract.

## The contract every database must keep

`tests/WebBase.Testing/StoreContractTests.cs` spells it out as
tests. In short:

- **Reads return copies.** Changing an object you were given saves nothing
  until you call `UpdateAsync`. (No surprise saves, whatever the database.)
- **Uniqueness**: two users can't share an email, or a Google account.
  Breaking that throws `DuplicateKeyException`, even when two requests race
  each other. Each implementation turns its own database's error into this one.
- **Ownership is part of every call on user data.** A store for one user's data
  can't be asked for "item 123", only for "item 123 *of user X*", and someone
  else's item behaves exactly like a missing one (the per-user example,
  [EXAMPLE_NOTES.md](EXAMPLE_NOTES.md), shows it end to end).
  **The exception is content that belongs to the site**, such as the pages authors
  write ([PAGES.md](PAGES.md)): it has no owner, so `IPageStore` takes no owner id,
  and the author check on the endpoints (`[RequireAuthor]`,
  [AUTHORS.md](AUTHORS.md)) is its guard. A store without an owner id must only be
  reached from endpoints with that guard.
- **Order and time**: lists of one user's data come newest first; lists of site
  content promise no order (the caller sorts). Times come back in UTC, to at
  least the millisecond.
- **`PrepareAsync`** creates or upgrades whatever the database needs (tables,
  containers, indexes), and is safe to run on every startup.

**Adding a feature** means adding a store for it: an interface in
`src/WebBase.Core/Data/`, its implementation in the Postgres module (and the
in-memory one used by tests), and contract tests for it.
[DEVELOPING.md](DEVELOPING.md#adding-a-feature-step-by-step) walks through it.

## Switching to another database

`modules/WebBase.Database.CosmosDB/` is a **complete, tested Azure Cosmos DB
implementation**. It passes every contract test against the Cosmos DB
emulator, and the whole app (sign-up, sign-in, pages) runs on it. The app
isn't built with it unless you ask, so projects that don't need it don't
carry the Cosmos package. Its README has the details:

1. `./scripts/module.sh add cosmos` (and `remove postgres`, if you like), and commit. On Azure you can skip this: the free setup uses Cosmos DB by default, builds the image with its module and creates the account, on Cosmos DB's free tier ([AZURE.md](AZURE.md#the-database-cosmos-db-or-postgresql)).
2. Set `Database__Provider=cosmos` and `Cosmos__ConnectionString=...`.
3. Run its contract tests against the Cosmos DB emulator (the module's README
   has the commands). `./scripts/test.sh` skips them, as it has no emulator.

Nothing else changes: not the controllers, not `AuthService`, not the website.

The same recipe works for any other database:

| Database | How much work |
|---|---|
| **SQL Server, MySQL, SQLite** | Small. Copy `modules/WebBase.Database.Postgres/` to `WebBase.Database.SqlServer/` (say, [MODULES.md](MODULES.md#writing-a-new-one)), swap `UseNpgsql` for `UseSqlServer`, change the duplicate-key error check in `AppDbContext.SaveAsync` to that database's error, and make a fresh `Initial` migration. Entity Framework does the rest |
| **Cosmos DB, MongoDB, DynamoDB** (document databases) | Moderate, like the Cosmos module: there are no tables or joins, so each store decides how records are laid out and how uniqueness is kept |

Things to know before you switch:

- **Migrations are PostgreSQL's.** `./scripts/ef.sh` and `modules/WebBase.Database.Postgres/Migrations`
  belong to the Postgres implementation. A document database has no schema to
  migrate: `PrepareAsync` creates its containers, and a field added to a model
  simply arrives as its default value on older documents.
- **`./scripts/backup.sh` and `restore.sh` are PostgreSQL tools.** With
  another database, use its own backups (Cosmos DB has continuous backup
  built in).
- **Moving existing data** between two kinds of database is a one-off job: a
  small program that reads from one set of stores and writes to the other.
- Every implementation must pass the contract tests. That's what makes
  "switch the setting" safe.
