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

## Adding a feature, step by step

The **Notes** feature is a complete example of every layer. To build your
own, say "Recipes", copy it layer by layer:

### 1. The table: a model

`src/WebBase.Api/Models/Recipe.cs`, copied from `Note.cs`:

```csharp
namespace WebBase.Api.Models;

public class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }          // who owns it
    public User? User { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
```

Register it in `Data/AppDbContext.cs`:

```csharp
public DbSet<Recipe> Recipes => Set<Recipe>();
```

and, in `OnModelCreating`, any limits and indexes (copy the `Note` block).

### 2. The migration

```bash
./scripts/ef.sh migrations add AddRecipes
```

This writes a file in `src/WebBase.Api/Migrations/` that describes the change
(here, "create table Recipes"). **Commit it.** You never run it by hand: the
API applies any new migrations every time it starts, in development and in
production alike.

Made a mistake, and haven't started the app since? `./scripts/ef.sh migrations remove`
deletes the last one. If it has already been applied, change the model again
and add another migration instead.

### 3. The endpoints: a controller

`Controllers/RecipesController.cs`, copied from `NotesController.cs`. Keep the
two habits that file shows:

- **`[Authorize]`** on the class, if only signed-in users may use it.
- **Every query filters by `User.GetUserId()`**, so nobody can read or change
  someone else's rows by guessing an id.

Request and response shapes go in `Contracts/` (copy `NoteContracts.cs`).
Return those, not the model itself, so a column you add later can't leak out
by accident.

The new endpoints appear at <http://localhost:5173/scalar> straight away, and
you can try them there.

### 4. The website: api.ts and a page

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

### 5. A test

Copy `tests/WebBase.Tests/NotesApiTests.cs` to `RecipesApiTests.cs`. Keep the
"one user cannot see another user's rows" test above all. Then
`./scripts/test.sh`.

## Removing the example

Once you no longer need Notes as a pattern, delete:

- `src/WebBase.Api/Models/Note.cs`, `Controllers/NotesController.cs`, `Contracts/NoteContracts.cs`
- the `Notes` lines in `Data/AppDbContext.cs` and `Models/User.cs`
- `client/src/views/NotesView.vue`, the `notesApi`/`Note` part of `client/src/api.ts`,
  its route in `router.ts`, its menu link in `App.vue`, and the button on `HomeView.vue`
- `tests/WebBase.Tests/NotesApiTests.cs` and the notes checks in `scripts/smoke.sh`

then `./scripts/ef.sh migrations add RemoveNotes` and `./scripts/test.sh`.

## Tests

`./scripts/test.sh` runs:

- **C# tests** (`tests/WebBase.Tests/`): the whole API started in memory, with
  an in-memory database, called over HTTP like the browser does. `TestApp.cs`
  sets it up, and `SignedInClientAsync()` gives you a client that's already
  signed in.
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
