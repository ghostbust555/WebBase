# WebBase.Database.Postgres

A database module, and **the one the app uses by default**: the stores
(`IUserStore`, `IPageStore`, `IDatabase`, and one per feature) on **PostgreSQL**, through Entity
Framework Core. This project is the only code in the repository that uses
Entity Framework. See [docs/DATABASE.md](../../docs/DATABASE.md) and
[docs/MODULES.md](../../docs/MODULES.md).

| File | What it is |
|---|---|
| `PostgresModule.cs` | `Database:Provider` "postgres": registers the stores. Also the design-time factory `./scripts/ef.sh` uses |
| `AppDbContext.cs` | The tables (`DbSet`s), and Postgres errors turned into the store contract (`DuplicateKeyException`...) |
| `PostgresUserStore.cs`, `PostgresPageStore.cs`, and one per feature | The stores |
| `PostgresDatabase.cs` | Migrations at startup, and the health check |
| `DatabaseUrl.cs` | `postgres://user:pass@host/db` (as cloud hosts give it) → a .NET connection string |
| `Migrations/` | Made by `./scripts/ef.sh migrations add <Name>`, applied by the app when it starts |

## Settings

| Setting | Meaning |
|---|---|
| `DATABASE_URL` | `postgres://user:password@host:5432/db?sslmode=require`. Wins over the next one. **A secret** |
| `ConnectionStrings__Default` | A .NET connection string. The compose files set this one |
| `ConnectionStrings__Migration` | Optional: run migrations on this connection (the owner role) instead |

Any PostgreSQL 14 or newer works: the container `./dev.sh` runs, a managed one
(Azure, RDS, Cloud SQL), or a hosted one (Neon, Supabase).

## Tests

`tests/WebBase.Database.Postgres.Tests/` runs the store contract
(`tests/WebBase.Testing/StoreContractTests.cs`) against a real PostgreSQL,
each test in a fresh database built by the migrations. `./scripts/test.sh`
starts that server; without `TEST_POSTGRES` the tests are skipped.
