# How it fits together

There are three programs, a handful of settings, and two ways of running
them. This page explains each piece and then follows one click from the
browser to the database and back.

## The three pieces

```
  ┌───────────────┐      ┌────────────────────┐      ┌──────────────┐
  │   Website     │ HTTP │       API          │  SQL │   Database   │
  │  (Vue, in the │─────▶│ (C#, ASP.NET Core) │─────▶│ (PostgreSQL) │
  │   browser)    │ JSON │                    │      │              │
  └───────────────┘      └────────────────────┘      └──────────────┘
     client/                src/WebBase.Api/            tables made by
                                                        Data/Postgres/Migrations/
```

**The website** (`client/`) is what people see. It's a *single-page app*: the
browser downloads it once, and from then on it draws every page itself and
only asks the server for data. It's written in [Vue](https://vuejs.org), and
each page is a `.vue` file in `client/src/views/`.

**The API** (`src/WebBase.Api/`) is the program on the server. The website
asks it things like "who is signed in?" or "save this note", and it answers in
JSON (text a program can read). The API is the only piece allowed to touch the
database, and it decides who may do what. **Never trust the website to
enforce a rule.** Anyone can send the API whatever they like, so every rule
belongs in the API.

**The database** (PostgreSQL) stores everything that must survive a restart:
accounts, notes, and whatever you add. You don't write SQL by hand. Records
are plain C# classes (`Models/`), and the API reaches the database only
through small interfaces called *stores* (`Data/IUserStore.cs`,
`Data/INoteStore.cs`). The PostgreSQL implementation behind them
(`Data/Postgres/`) uses Entity Framework, which turns those classes into
tables and queries. Because nothing else knows about PostgreSQL, the database
can be swapped (a managed cloud Postgres, or Cosmos DB) without changing the
rest. See [DATABASE.md](DATABASE.md).

## Development: three containers, your live code

`./dev.sh` starts `compose.dev.yaml`:

```
 your browser
     │  http://localhost:5173
     ▼
 ┌─────────────────────┐   /api/*   ┌──────────────────────┐        ┌────────────┐
 │ web: Vite dev server │──────────▶│ api: dotnet watch    │───────▶│ db:        │
 │ hot-reloads client/  │           │ rebuilds src/ on save │        │ PostgreSQL │
 └─────────────────────┘           └──────────────────────┘        └────────────┘
   container "web"                    container "api"                container "db"
   reads ./client                     reads ./src                    data in .docker-data/dev-postgres
```

- The containers read your files directly, so a save shows up straight away:
  the page updates in place, and the API recompiles in a second or two.
- The browser only ever talks to `web` (port 5173). `web` forwards anything
  starting with `/api` to `api`. Because the browser sees one address, there's
  no cross-site (CORS) setup to get wrong.
- `api` is also on port 5000, and `db` on 5432, both visible only from your
  own computer, for when you want to connect a tool to them directly.
- API docs, where you can try every endpoint, are at
  <http://localhost:5173/scalar> (development only).

## Production: one container, a built release

`./deploy.sh` builds `ops/Dockerfile` and starts `compose.prod.yaml`:

```
 visitors ──▶ Cloudflare ──tunnel──▶ ┌──────────────────────────────┐      ┌────────────┐
                                     │ app: the release image       │─────▶│ db:        │
 you ──▶ http://localhost:8080 ─────▶│ API + the built website      │      │ PostgreSQL │
                                     │ (in wwwroot) in one program  │      │ no ports   │
                                     └──────────────────────────────┘      └────────────┘
```

- The Dockerfile builds the website into plain files, compiles the API, and
  puts both into one small image. The API hands out the website's files
  itself, so production is a single program. That makes it easy to run
  anywhere: here, or on any cloud (see [CLOUD.md](CLOUD.md)).
- The image is a fixed snapshot. Editing code doesn't change production until
  you run `./deploy.sh` again. Every release is kept, so `./deploy.sh rollback`
  can go back.
- The database has no open port at all. Only `app` can reach it. And `app`
  connects as a database user that can read and write rows but can't create
  or delete tables. Only the startup migration runs as the table owner.
- Or, recommended for anything that matters, no database container at all:
  set `PROD_DATABASE_URL` to a managed PostgreSQL and `app` uses that
  ([DEPLOYING.md](DEPLOYING.md#a-managed-database)).
- `app` listens on 127.0.0.1, which is this computer only. The public
  reaches it only if you turn on the Cloudflare Tunnel
  ([CLOUDFLARE_TUNNEL.md](CLOUDFLARE_TUNNEL.md)).

Development and production are completely separate: separate containers,
separate databases, separate ports. You can run both at once.

## Following one click

What happens when a signed-in person adds a note:

1. **The page** - `client/src/views/NotesView.vue`. Pressing *Add* calls
   `notesApi.create(text)`.
2. **The API client** - `client/src/api.ts`. That sends
   `POST /api/notes` with `{"text": "..."}`, and attaches the sign-in token as
   `Authorization: Bearer eyJ...`.
3. **The address** - in development the Vite server forwards `/api/...` to the
   API container. In production the API already is that address.
4. **The pipeline** - `src/WebBase.Api/Program.cs`, section 4. The request
   passes through security headers, rate limiting, and then *authentication*,
   which checks the token's signature with `Jwt:Key` and works out which user
   sent it.
5. **The controller** - `Controllers/NotesController.cs`. `[Authorize]` refuses
   anyone without a valid token (401). `Create` checks the text, makes a `Note`
   with `UserId = User.GetUserId()`, and hands it to the note store
   (`INoteStore.AddAsync`).
6. **The database** - the PostgreSQL store (`Data/Postgres/PostgresNoteStore.cs`)
   saves it through Entity Framework, which sends an `INSERT` into the `Notes` table.
7. **Back** - the controller returns the new note as JSON. The page adds it
   to the list.

Every feature you build follows the same path: page → `api.ts` → controller →
store → database.

## Sign-in, in a few sentences

- **Register or log in** (`/api/auth/register`, `/api/auth/login`): the API
  checks the password against a salted hash (the password itself is never
  stored) and returns a **token**.
- The **token** is a signed note saying "this is user 1234, valid until
  such-and-such". The website keeps it in the browser (`localStorage`) and
  sends it with every request. The server needs no session table, because it
  checks the signature instead. Anyone holding `Jwt:Key` could forge one,
  which is why that key is a secret.
- Tokens last 30 days, and the website quietly swaps in a fresh one once the
  current one is half used. So anyone who visits at least every couple of
  weeks stays signed in.
- **Google sign-in** (optional): Google's button gives the browser a
  credential signed by Google, and the website passes it to
  `/api/auth/google`. The API checks it with Google's public keys, finds or
  creates the matching account, and returns the same kind of token. See
  [GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md).
- **Pages that need sign-in**: in `client/src/router.ts`,
  `meta: { requiresAuth: true }` sends signed-out visitors to the login page.
  That's only for convenience. The real protection is `[Authorize]` on the
  controller.

## Settings and secrets

```
 .env.local  ──(read by docker compose)──▶  compose.*.yaml  ──(environment variables)──▶  the API
 (yours, never committed)                    maps names, e.g.                               reads them as
                                              JWT_KEY → Jwt__Key                             Jwt:Key
```

- `appsettings.json` holds the defaults, and it's committed, so **no secrets
  there**.
- `.env.local` holds this machine's secrets and choices. `./setup.sh` creates
  it, and git ignores it.
- An environment variable named `Section__Key` overrides `Section:Key` from
  `appsettings.json`. That's how .NET works everywhere, including on every
  cloud host.

The full list is in [CONFIGURATION.md](CONFIGURATION.md).

## Where things live

| You want to change... | Look in |
|---|---|
| The home page | `client/src/views/HomeView.vue` (or `content/index.md` with the CMS on) |
| A page's content or layout | `client/src/views/*.vue` |
| Text pages: about, blog, help (optional CMS) | `content/*.md`, see [CMS.md](CMS.md) |
| The guides shown at `/docs` | `docs/*.md` |
| The menu, banner, overall frame | `client/src/App.vue` |
| Colours, fonts, spacing | `client/src/style.css` (variables at the top) |
| Which address shows which page | `client/src/router.ts` |
| How the website calls the API | `client/src/api.ts` |
| An API endpoint | `src/WebBase.Api/Controllers/` |
| Business rules | `src/WebBase.Api/Services/` |
| A kind of record (a database table) | `src/WebBase.Api/Models/`, a store in `Data/` + `Data/Postgres/`, then a migration ([DEVELOPING.md](DEVELOPING.md#adding-a-feature-step-by-step)) |
| Which database, and where it runs | `Database__Provider`, `DATABASE_URL` / `PROD_DATABASE_URL`, see [DATABASE.md](DATABASE.md) |
| How the server is wired together | `src/WebBase.Api/Program.cs` |
| Production's container | `ops/Dockerfile`, `compose.prod.yaml` |
