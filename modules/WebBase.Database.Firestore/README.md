# WebBase.Database.Firestore

A database module: the app's stores (`IUserStore`, `IPageStore`, `IDatabase`, and one per feature)
on **Google Cloud Firestore** in Native mode, the database behind Firebase. Built and
tested with the rest of the repository, but **not part of the app until you add it**
(below). On Google Cloud you can skip that: `./scripts/cloud/gcp.sh setup` uses
Firestore by default and builds the image with this module
([docs/GOOGLE_CLOUD.md](../../docs/GOOGLE_CLOUD.md)). See
[docs/MODULES.md](../../docs/MODULES.md) for modules in general and
[docs/DATABASE.md](../../docs/DATABASE.md) for how the database layer works.

**Tested**: passes every contract test (`tests/WebBase.Testing/StoreContractTests.cs`,
run by `tests/WebBase.Database.Firestore.Tests/`, 25 tests, racing sign-ups for one
email included) against the Firestore emulator (`cloud-firestore-emulator` 1.19.8,
`Google.Cloud.Firestore` 4.6.0, October 2026). The whole API ran on the emulator with
`./scripts/smoke.sh` passing: sign-up and sign-in, pages, authors. **Not yet run
against a real Firestore database**; `gcp.sh`'s Firestore setup has not been run
against a real project either. `./scripts/test.sh` skips these tests unless
`TEST_FIRESTORE` is set (below).

## Why Firestore

- **Free** for a small site: Firestore's free quota (as published in October 2026:
  1 GiB stored, 50,000 document reads, 20,000 writes and 20,000 deletes a day; check
  [the current numbers](https://firebase.google.com/pricing)) covers the project's
  `(default)` database. Past it, you pay per operation: no monthly minimum.
- **Nothing to sign up for, no password**: on Cloud Run the app signs in as its own
  service account, which setup allows to use Firestore (`roles/datastore.user`).
  Nothing goes in Secret Manager for it.

## How it stores things

| Collection | Holds |
|---|---|
| `users` | one document per user, its id the user's id |
| `keys` | one tiny document per unique value: "this email belongs to user X", "this Google account belongs to user X", "this page address belongs to page Y". Its id is the kind of value and a hash of it (a document id can't contain `/`) |
| `pages` | the pages authors write ([docs/PAGES.md](../../docs/PAGES.md)), one document each |
| `users/{id}/<feature>` | per-user data lives *inside* its owner's document, so an item can only be reached by naming its owner: the per-user stores' rule, for free (the bundled per-user example has one: [docs/EXAMPLE_NOTES.md](../../docs/EXAMPLE_NOTES.md)) |

**Uniqueness is a transaction.** Adding or changing a user (or a page) reads its
`keys` documents, refuses with `DuplicateKeyException` if one belongs to someone
else, and writes the record and its keys together. When two transactions race,
Firestore makes one retry; the retry sees the winner's key. Unlike the Cosmos DB
module, nothing can be left half-written.

**No indexes to make.** Every query asks about one field (`email`, `isAuthor`,
`path`, `published`, or the time inside one user's items), and Firestore indexes
single fields by itself. Lists the app sorts are sorted by the app. Times are
Firestore Timestamps (microseconds, UTC).

`PrepareAsync` has nothing to do: Firestore makes a collection on its first write.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| `Database__Provider` | `postgres` | `firestore` selects this module |
| `Firestore__ProjectId` | the project the app runs in (Cloud Run knows it), else `GOOGLE_CLOUD_PROJECT` | The Google Cloud project whose Firestore to use |
| `Firestore__Database` | `(default)` | Another database in the project. Only `(default)` gets the free quota |
| `FIRESTORE_EMULATOR_HOST` | | `host:port` of a local emulator. Set, the app uses it with no sign-in; Google's libraries all read this variable |

How it signs in elsewhere: Google's *Application Default Credentials*. On Cloud Run,
the service's account. On your computer, `gcloud auth application-default login`
(and an account allowed to use the project's Firestore). No key file needed.

## Switching the app to it

Google Cloud: nothing to do; `gcp.sh setup` picks it for a new free site (or
`GCP_DATABASE=firestore` for any site; existing data in PostgreSQL is **not**
copied over).

Anywhere else:

```bash
./scripts/module.sh add firestore      # optionally: ./scripts/module.sh remove postgres
```

then set `Database__Provider=firestore` and `Firestore__ProjectId=...`, and commit
the two files `module.sh` changed.

## Running its tests: the Firestore emulator

The emulator needs Java 11 or newer. With Node, the Firebase CLI fetches and runs it:

```bash
npx firebase-tools@14 setup:emulators:firestore
java -jar ~/.cache/firebase/emulators/cloud-firestore-emulator-v*.jar --host 127.0.0.1 --port 8085
```

(Or `gcloud components install cloud-firestore-emulator` and
`gcloud emulators firestore start --host-port=127.0.0.1:8085`.) Then, in another
terminal:

```bash
TEST_FIRESTORE=127.0.0.1:8085 dotnet test tests/WebBase.Database.Firestore.Tests
```

Each test uses its own project id, which the emulator keeps apart, and clears it
afterwards. To run the whole app on the emulator: add the module (above), then
start the API with `Database__Provider=firestore`, `Firestore__ProjectId=demo`
(any name) and `FIRESTORE_EMULATOR_HOST=127.0.0.1:8085`.

## What to know

- **Native mode only.** A project whose Firestore was created in *Datastore mode*
  can't be switched; `gcp.sh setup` stops and says so. Use another project.
- **The location is permanent.** `gcp.sh` makes the database in `GCP_REGION`
  (`GCP_FIRESTORE_LOCATION` to choose another), and it can't move later.
- **Backups**: Firestore's scheduled backups and point-in-time recovery are paid
  features; for a free site, export now and then with
  `gcloud firestore export gs://YOUR-BUCKET` (costs a little storage).
- **Fixing the first author** ([docs/AUTHORS.md](../../docs/AUTHORS.md)): in the
  Google Cloud console's *Firestore* page, open the `users` collection, find the
  document whose `email` is yours, and set `isAuthor` to `true`.
