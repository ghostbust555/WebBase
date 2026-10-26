# WebBase.Database.CosmosDB

A database module: the app's stores (`IUserStore`, `INoteStore`, `IDatabase`)
on **Azure Cosmos DB for NoSQL**. Built and tested with the rest of the
repository, but **not part of the app until you add it** (below). See
[docs/MODULES.md](../../docs/MODULES.md) for modules in general and
[docs/DATABASE.md](../../docs/DATABASE.md) for how the database layer works.

**Tested**: passes every contract test (`tests/WebBase.Testing/StoreContractTests.cs`,
run by `tests/WebBase.Database.CosmosDB.Tests/`) against the Cosmos DB
emulator (`vnext-preview`, October 2026, SDK 3.54.0). The whole API has run
on it with `./scripts/smoke.sh` passing.

## How it stores things

| Container | Partitioned by | Holds |
|---|---|---|
| `users` | `/id` | one document per user |
| `keys` | `/id` | one small document per unique value: "this email belongs to user X", "this Google account belongs to user X" |
| `notes` | `/userId` | the notes, each user's together |

Cosmos DB only guarantees an id is unique within its partition. So a unique
email is a `keys` document whose id is (a hash of) the email: creating a
second one is refused, which is exactly the rule. Registration claims the
keys first, then writes the user, and gives the keys back if anything fails.

Notes are partitioned by owner, so "this user's notes" is a cheap one-partition
query, and a note can only be found by naming its owner, the same rule
`INoteStore` asks for.

## Switching the app to it

From the project root:

```bash
./scripts/module.sh add cosmos
```

That builds the app with this project: a `<ProjectReference>` in
`src/WebBase.Api/WebBase.Api.csproj` and `new CosmosModule()` in
`src/WebBase.Api/Modules.cs` (make those two edits by hand if you prefer).
Commit both.

Then set (in `compose.*.yaml`, or your cloud's settings):

| Setting | Value |
|---|---|
| `Database__Provider` | `cosmos` |
| `Cosmos__ConnectionString` | from the Azure portal: your Cosmos account → *Keys* → *Primary connection string*. **A secret** |
| `Cosmos__Database` | the database name, default `webbase` (created on first start) |

The Postgres module can stay in the app; it's simply not used while the
provider is `cosmos`. To drop it: `./scripts/module.sh remove postgres`.

## The Cosmos account

- **Pick "Serverless"** when creating the account (Azure portal → *Azure Cosmos
  DB* → *Create* → *Azure Cosmos DB for NoSQL* → Capacity mode: *Serverless*).
  You pay per request, which is close to nothing for a small site. With
  *provisioned throughput* instead, each of the three containers reserves
  400 RU/s by default, which costs money every hour whether anyone visits or not.
- Turn on **continuous backup** (point-in-time restore). `scripts/backup.sh`
  is for PostgreSQL only.
- On Azure Container Apps, store the connection string as a secret, as
  `scripts/cloud/azure.sh` does for `Jwt__Key`. That script creates a
  PostgreSQL server: skip it with `AZURE_DATABASE_URL` and change the
  environment variables it sets, or create the app by hand.
- Without a computer of your own: `./scripts/test.sh` skips these tests
  (the emulator is too heavy to start on every run), so run them once with
  the steps below wherever Docker is available, before relying on a change.

## Testing it locally, with the emulator

```bash
docker network create cosmos-test
docker run -d --rm --name cosmos --network cosmos-test \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview

# EMULATOR_KEY: the emulator's fixed development key, the same for everyone,
# from Microsoft's docs ("Azure Cosmos DB emulator", section "Authentication").
docker run --rm --network cosmos-test -v "$PWD:/src" -w /src \
  -v /src/.build \
  -e "TEST_COSMOS=AccountEndpoint=http://cosmos:8081/;AccountKey=${EMULATOR_KEY}" \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/WebBase.Database.CosmosDB.Tests

docker rm -f cosmos && docker network rm cosmos-test
```

`Cosmos__Gateway=true` is for the emulator only. It connects the way the
emulator needs, and works around query answers in a binary format that the
SDK couldn't read from it. Leave it unset against a real account.

## Limits worth knowing

- A user's email change or Google link writes up to three documents, which
  Cosmos can't do in one transaction across partitions. A failure part-way
  is undone where possible. A key left behind by a crash stops blocking its
  value after a minute.
- There are no migrations. A new field on a model just reads as its default
  on older documents. Renaming a field means handling both names in code
  until old documents are rewritten.
