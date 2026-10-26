# Developing

How to change things, add features, and check you haven't broken anything.
Read [HOW_IT_FITS_TOGETHER.md](HOW_IT_FITS_TOGETHER.md) first if you haven't.

## The loop

1. `./dev.sh`, then open <http://localhost:5173>.
2. Edit files. The website updates the moment you save. The API takes a
   second or two to rebuild.
3. If something doesn't update or shows an error: `./dev.sh logs api` (or
   `web`). A compile error in C# appears there.
4. Before committing: `./scripts/test.sh`.

Most C# edits apply without a restart. A few changes (adding a class to
`Program.cs`, changing constructor parameters) need a full restart, and
`dotnet watch` does that by itself. After editing `.env.local`, run
`./dev.sh restart`.

## Your home page

A new project's home page shows the getting-started guide, so the first thing
anyone sees is how to get going. Replace it when you're ready, in one of two
ways:

- **Write it as a page of the app:** replace everything in
  `client/src/views/HomeView.vue` with your own template. Any `.vue` page is a
  good starting point to copy.
- **Write it as text:** turn on the [CMS](CMS.md) and edit `content/index.md`.
  While that file exists, it's the home page.

The guides stay at `/docs` (the **Guide** link in the menu). They're built from
the `docs/` folder, so editing a guide updates that page too. For a site
where visitors shouldn't see them, remove the `/docs` route from
`client/src/router.ts` and the **Guide** link from `App.vue`. Optionally
delete `DocsView.vue`, `components/DocsReader.vue` and `lib/docs.ts` too.

## Adding a feature, step by step

The **Notes** feature is a complete example of every layer. To build your
own, say "Recipes", copy it layer by layer:

### 1. The record: a model

`src/WebBase.Api/Models/Recipe.cs`, copied from `Note.cs`. A plain class:
no database code in it.

```csharp
namespace WebBase.Api.Models;

public class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }          // who owns it
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
```

### 2. The store: what the app may ask the database

The app reaches the database only through *stores* ([DATABASE.md](DATABASE.md)),
so it doesn't depend on which database is behind them.

`src/WebBase.Api/Data/IRecipeStore.cs`, copied from `INoteStore.cs`. Every
method takes the owner's id, so no caller can reach someone else's recipe:

```csharp
namespace WebBase.Api.Data;

public interface IRecipeStore
{
    Task<IReadOnlyList<Recipe>> ListAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Recipe recipe, CancellationToken ct = default);
    Task<bool> RemoveAsync(Guid userId, Guid recipeId, CancellationToken ct = default);
}
```

Its PostgreSQL version, `Data/Postgres/PostgresRecipeStore.cs`, is a copy of
`PostgresNoteStore.cs` with *Note* changed to *Recipe*. Then, in the same folder:

- `AppDbContext.cs`: add the table, `public DbSet<Recipe> Recipes => Set<Recipe>();`,
  and in `OnModelCreating` its limits and indexes (copy the `Note` block).
- `PostgresDatabase.cs`: register it, next to the others in `AddPostgresDatabase`:
  `services.AddScoped<IRecipeStore, PostgresRecipeStore>();`

### 3. The migration

```bash
./scripts/ef.sh migrations add AddRecipes
```

This writes a file in `src/WebBase.Api/Data/Postgres/Migrations/` that
describes the change (here, "create table Recipes"). **Commit it.** You never
run it by hand: the API applies any new migrations every time it starts, in
development and in production alike.

Made a mistake, and haven't started the app since? `./scripts/ef.sh migrations remove`
(with `./dev.sh` running) deletes the last one. If it has already been
applied, change the model again and add another migration instead.

### 4. The endpoints: a controller

`Controllers/RecipesController.cs`, copied from `NotesController.cs`. It asks
for `IRecipeStore` in its constructor, never for the database itself. Keep the
two habits that file shows:

- **`[Authorize]`** on the class, if only signed-in users may use it.
- **Pass `User.GetUserId()` to every store call**, so nobody can read or
  change someone else's rows by guessing an id.

Request and response shapes go in `Contracts/` (copy `NoteContracts.cs`).
Return those, not the model itself, so a field you add later can't leak out
by accident.

The new endpoints appear at <http://localhost:5173/scalar> straight away, and
you can try them there.

### 5. The website: api.ts and a page

In `client/src/api.ts`, add the type and the calls (copy `Note` and `notesApi`):

```ts
export interface Recipe { id: string; title: string; instructions: string; createdAtUtc: string }

export const recipesApi = {
  list: () => get<Recipe[]>('/api/recipes'),
  create: (title: string, instructions: string) => post<Recipe>('/api/recipes', { title, instructions }),
  remove: (id: string) => del<void>(`/api/recipes/${id}`)
}
```

Make `client/src/views/RecipesView.vue` (copy `NotesView.vue`), add its address
in `client/src/router.ts`:

```ts
{ path: '/recipes', component: () => import('./views/RecipesView.vue'), meta: { requiresAuth: true } },
```

and a link in the menu in `client/src/App.vue`:

```html
<RouterLink to="/recipes">Recipes</RouterLink>
```

### 6. Tests

- **The API**: copy `tests/WebBase.Tests/NotesApiTests.cs` to
  `RecipesApiTests.cs`. Keep the "one user cannot see another user's rows"
  test above all.
- **The store**: copy the notes tests in
  `tests/WebBase.Tests/StoreContract/StoreContractTests.cs` for recipes. They
  run against real PostgreSQL, and against any other database you add later.

Then `./scripts/test.sh`.

## Removing the example

Once you no longer need Notes as a pattern, delete:

- `src/WebBase.Api/Models/Note.cs`, `Controllers/NotesController.cs`, `Contracts/NoteContracts.cs`
- `Data/INoteStore.cs` and `Data/Postgres/PostgresNoteStore.cs`, and the `Note`
  lines in `Data/Postgres/AppDbContext.cs` and `Data/Postgres/PostgresDatabase.cs`
- `client/src/views/NotesView.vue`, the `notesApi`/`Note` part of `client/src/api.ts`,
  its route in `router.ts`, and its menu link in `App.vue`
- `tests/WebBase.Tests/NotesApiTests.cs`, the notes tests (and the one note
  line in `Changing_a_returned_user_saves_nothing_until_update`) in
  `StoreContract/StoreContractTests.cs`, and the notes checks in `scripts/smoke.sh`

then `./scripts/ef.sh migrations add RemoveNotes` and `./scripts/test.sh`.

## Tests

`./scripts/test.sh` runs:

- **C# tests** (`tests/WebBase.Tests/`): the whole API started in memory, with
  an in-memory database, called over HTTP like the browser does. `TestApp.cs`
  sets it up, and `SignedInClientAsync()` gives you a client that's already
  signed in.
- **The store contract tests** (`tests/WebBase.Tests/StoreContract/`): every
  store against a real PostgreSQL, which `test.sh` starts and removes again.
  Each test gets a fresh database, built by the migrations, so those are
  tested too. Run from an IDE without `TEST_POSTGRES` set, they show as skipped.
- **The website's type check and build.** TypeScript catches calling the API
  with the wrong shape, typos in property names, and so on.

`./scripts/smoke.sh [address]` checks a *running* site end to end: sign up,
sign in, notes. Use it after `./dev.sh`, after `./deploy.sh`
(`./scripts/smoke.sh http://localhost:8080`), and after a cloud deploy.

## The database

- `./dev.sh psql`: a SQL prompt on the development database. Try `\dt` to
  list tables, `SELECT * FROM "Users";` (quotes needed, since the table names
  have capitals), and `\q` to leave.
- Any database tool (DBeaver, TablePlus, DataGrip, pgAdmin) can connect to
  development at `localhost:5432`, database `webbase`, user `webbase`, password `webbase`.
- `./dev.sh reset-db` deletes the development database entirely. The next
  start makes a fresh one.

## Running pieces without Docker (optional)

If you'd rather use your own tools, for example to debug C# in Rider or
Visual Studio, install the [.NET 10 SDK](https://dotnet.microsoft.com/download)
and [Node 22](https://nodejs.org). Then:

```bash
./dev.sh && docker compose -f compose.dev.yaml stop api web   # keep only the database
cd src/WebBase.Api && dotnet run                              # API on :5000
cd client && npm install && npm run dev                       # website on :5173
```

The API's default settings already point at the development database on
localhost:5432.

## Conventions worth keeping

- **Errors are `{ "error": "message a person can read" }`.** The website shows
  `message` as-is, so write it for the user.
- **Secrets never go in git.** A new secret goes in `.env.local` and into
  `compose.*.yaml` (see [CONFIGURATION.md](CONFIGURATION.md#adding-a-setting)).
- **Every rule is enforced in the API**, even if the page also checks it.
- **Small commits, each working.** `./scripts/test.sh` before each one.

## Adding packages

- Website: `docker compose -f compose.dev.yaml exec web npm install <package>`
  (or `cd client && npm install <package>` if you have Node). Commit
  `package.json` and `package-lock.json`.
- API: add a `<PackageReference>` line to `src/WebBase.Api/WebBase.Api.csproj`
  (find the name and version on <https://www.nuget.org>), then `./dev.sh restart`.
