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
 Data/IUserStore.cs  Data/INoteStore.cs  Data/IDatabase.cs      the contract
        │
        ▼  chosen by Database__Provider (Data/DatabaseRegistration.cs)
 Data/Postgres/                            one implementation: EF Core + PostgreSQL
   AppDbContext.cs, PostgresUserStore.cs, PostgresNoteStore.cs,
   PostgresDatabase.cs (migrations, health), Migrations/
```

| File | What it is |
|---|---|
| `Models/*.cs` | The records (`User`, `Note`): plain C# classes, with no database code |
| `Data/IUserStore.cs`, `Data/INoteStore.cs` | What the app can ask for: "find a user by email", "list this user's notes"... |
| `Data/IDatabase.cs` | Getting the database ready at startup, and "can I reach it?" for `/api/health`. Also `DuplicateKeyException` |
| `Data/DatabaseRegistration.cs` | Reads `Database__Provider` and registers the matching implementation |
| `Data/Postgres/` | The PostgreSQL implementation, and the only code that uses Entity Framework |

Two things keep this honest:

- **Entity Framework is only visible in `Data/Postgres/`.** It isn't a global
  `using`, and a test (`tests/WebBase.Tests/StoreContract/ArchitectureTests.cs`)
  fails if anything outside `Data/` asks for a database-specific type.
- **The contract is tested, not just written down.** Every implementation
  runs the same tests (next section). `./scripts/test.sh` runs them against a
  real PostgreSQL that it starts for the purpose.

## The contract every database must keep

`tests/WebBase.Tests/StoreContract/StoreContractTests.cs` spells it out as
tests. In short:

- **Reads return copies.** Changing an object you were given saves nothing
  until you call `UpdateAsync`. (No surprise saves, whatever the database.)
- **Uniqueness**: two users can't share an email, or a Google account.
  Breaking that throws `DuplicateKeyException`, even when two requests race
  each other. Each implementation turns its own database's error into this one.
- **Ownership is part of every call on user data.** `INoteStore` can't be
  asked for "note 123", only for "note 123 *of user X*". Someone else's note
  behaves exactly like a missing one.
- **Order and time**: lists come newest first. Times come back in UTC, to at
  least the millisecond.
- **`PrepareAsync`** creates or upgrades whatever the database needs (tables,
  containers, indexes), and is safe to run on every startup.

**Adding a feature** means adding a store for it: an interface in `Data/`,
its implementation in `Data/Postgres/`, and contract tests for it.
[DEVELOPING.md](DEVELOPING.md#adding-a-feature-step-by-step) walks through it.

## Switching to another database

`examples/cosmos-db/` holds a **complete, tested Azure Cosmos DB
implementation**. It passes every contract test against the Cosmos DB
emulator, and the whole app (sign-up, sign-in, notes) runs on it. It isn't
compiled into the app until you copy it in, so projects that don't need it
don't carry it. Its README (`examples/cosmos-db/README.md`) has the steps:

1. Copy `examples/cosmos-db/Data/Cosmos/` into `src/WebBase.Api/Data/`, and
   the test file into `tests/WebBase.Tests/StoreContract/`.
2. Add the Cosmos package, and one `case "cosmos":` line to `Data/DatabaseRegistration.cs`.
3. Set `Database__Provider=cosmos` and `Cosmos__ConnectionString=...`.
4. Run the contract tests against it.

Nothing else changes: not the controllers, not `AuthService`, not the website.

The same recipe works for any other database:

| Database | How much work |
|---|---|
| **SQL Server, MySQL, SQLite** | Small. Copy `Data/Postgres/` to `Data/SqlServer/` (say), swap `UseNpgsql` for `UseSqlServer`, change the duplicate-key error check in `AppDbContext.SaveAsync` to that database's error, and make a fresh `Initial` migration. Entity Framework does the rest |
| **Cosmos DB, MongoDB, DynamoDB** (document databases) | Moderate, like the Cosmos example: there are no tables or joins, so each store decides how records are laid out and how uniqueness is kept |

Things to know before you switch:

- **Migrations are PostgreSQL's.** `./scripts/ef.sh` and `Data/Postgres/Migrations`
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
