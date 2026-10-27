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

As a worked example, we'll add **Recipes**: each signed-in person keeps their own
recipes, and nobody else can see them. Every layer is below, with the code to
write. (The repository may also ship a small finished example of this same
shape; if it is still there, [EXAMPLE_NOTES.md](EXAMPLE_NOTES.md) lists its
files to copy from. For content that belongs to the *site* instead of to one
user, [PAGES.md](PAGES.md) is the example.)

### 1. The record: a model

`src/WebBase.Core/Models/Recipe.cs`. A plain class: no database code in it.
(`WebBase.Core` is the small project of contracts that the app and every
database module share, [MODULES.md](MODULES.md).)

```csharp
namespace WebBase.Models;

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

`src/WebBase.Core/Data/IRecipeStore.cs`. Every method that reads or removes
takes the owner's id, so no caller can reach someone else's recipe:

```csharp
using WebBase.Models;

namespace WebBase.Data;

public interface IRecipeStore
{
    /// <summary>The user's recipes, newest first.</summary>
    Task<IReadOnlyList<Recipe>> ListAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Recipe recipe, CancellationToken ct = default);
    /// <summary>False if there is no such recipe belonging to this user.</summary>
    Task<bool> RemoveAsync(Guid userId, Guid recipeId, CancellationToken ct = default);
}
```

The contract every store keeps: reads return copies, lists are newest first,
times are UTC, and removing someone else's recipe behaves exactly like removing a
missing one.

Its PostgreSQL version goes in the Postgres module,
`modules/WebBase.Database.Postgres/PostgresRecipeStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace WebBase.Database.Postgres;

public class PostgresRecipeStore(AppDbContext db) : IRecipeStore
{
    public async Task<IReadOnlyList<Recipe>> ListAsync(Guid userId, CancellationToken ct = default) =>
        await db.Recipes.AsNoTracking().Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);

    public async Task AddAsync(Recipe recipe, CancellationToken ct = default)
    {
        db.Recipes.Add(recipe);
        await db.SaveAsync(ct);          // SaveAsync turns database errors into the contract's exceptions
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid recipeId, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId && r.UserId == userId, ct);
        if (recipe == null) return false;
        db.Recipes.Remove(recipe);
        await db.SaveAsync(ct);
        return true;
    }
}
```

Then, in the same folder:

- `AppDbContext.cs`: add the table, `public DbSet<Recipe> Recipes => Set<Recipe>();`,
  and in `OnModelCreating` its limits and indexes (an index on `{ UserId, CreatedAtUtc }`,
  a maximum length for `Title`, and a cascading delete from the user).
- `PostgresModule.cs`: register it, next to the others in `Register`:
  `services.AddScoped<IRecipeStore, PostgresRecipeStore>();`

And one for the API tests, which run on an in-memory database: in
`tests/WebBase.Testing/InMemory/InMemoryDatabase.cs`, an `InMemoryRecipeStore`
(a list in `InMemoryData`, every access under `data.Lock`, copies on the way in
and out with `InMemoryData.Copy`) and its line in `Register`. Using the Cosmos DB
module too? It needs a `CosmosRecipeStore` the same way, with the container
partitioned by `/userId`.

### 3. The migration

```bash
./scripts/ef.sh migrations add AddRecipes
```

This writes a file in `modules/WebBase.Database.Postgres/Migrations/` that
describes the change (here, "create table Recipes"). **Commit it.** You never
run it by hand: the API applies any new migrations every time it starts, in
development and in production alike.

Made a mistake, and haven't started the app since? `./scripts/ef.sh migrations remove`
(with `./dev.sh` running) deletes the last one. If it has already been
applied, change the model again and add another migration instead.

### 4. The endpoints: a controller

`src/WebBase.Api/Controllers/RecipesController.cs`. It asks for `IRecipeStore`
in its constructor, never for the database itself. Keep the two habits shown here:

- **`[Authorize]`** on the class, if only signed-in users may use it.
- **Pass `User.GetUserId()` to every store call**, so nobody can read or
  change someone else's rows by guessing an id.

```csharp
[ApiController]
[Route("api/recipes")]
[Authorize]
public class RecipesController(IRecipeStore recipes) : ControllerBase
{
    [HttpGet]
    public async Task<List<RecipeDto>> List(CancellationToken ct) =>
        (await recipes.ListAsync(User.GetUserId(), ct)).Select(RecipeDto.From).ToList();

    [HttpPost]
    public async Task<ActionResult<RecipeDto>> Create(CreateRecipeRequest request)
    {
        var title = (request.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest(new ErrorResponse("Give the recipe a title."));

        var recipe = new Recipe { UserId = User.GetUserId(), Title = title, Instructions = request.Instructions ?? "" };
        await recipes.AddAsync(recipe);
        return CreatedAtAction(nameof(List), RecipeDto.From(recipe));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id) =>
        // Someone else's recipe answers exactly like a missing one.
        await recipes.RemoveAsync(User.GetUserId(), id) ? NoContent() : NotFound(new ErrorResponse("No such recipe."));
}
```

Request and response shapes go in `Contracts/RecipeContracts.cs`:
`public record CreateRecipeRequest(string Title, string? Instructions);` and a
`RecipeDto` with a static `From(Recipe)`. Return those, not the model itself, so
a field you add later can't leak out by accident. Give each action its real
return type (`ActionResult<RecipeDto>`), or, if it returns plain `IActionResult`,
a `[ProducesResponseType<RecipeDto>(200)]` (`[ProducesResponseType(204)]` for
"nothing"). The website's types are made from these
([next step](#5-the-website-apits-and-a-page)), and generation stops with the
action's name if it can't tell what one returns.

Content that belongs to the *site*, not to a user, has no owner id to pass: put
`[Authorize, RequireAuthor]` on the controller instead ([AUTHORS.md](AUTHORS.md#guarding-your-own-features)).

The new endpoints appear at <http://localhost:5173/scalar> straight away, and
you can try them there.

### 5. The website: api.ts and a page

**The types come from the C#; don't write them.** `client/src/api-types.ts`
holds a TypeScript interface for every record a controller takes or returns,
generated from the C# itself. While `./dev.sh` runs, the API rewrites it each
time it rebuilds, so your `RecipeDto` and `CreateRecipeRequest` are already
there. (Without `./dev.sh`: `./scripts/gen-types.sh`.) Commit it with the C#.

In `client/src/api.ts`, import them and add the calls:

```ts
import type { CreateRecipeRequest, RecipeDto } from './api-types'
export type Recipe = RecipeDto

export const recipesApi = {
  list: () => get<Recipe[]>('/api/recipes'),
  create: (title: string, instructions: string) =>
    post<Recipe>('/api/recipes', { title, instructions } satisfies CreateRecipeRequest),
  remove: (id: string) => del<void>(`/api/recipes/${id}`)
}
```

`satisfies` checks the body you send against the C# request record. So when
a field is renamed or changes type in C#, `npm run build` (and therefore
`./scripts/test.sh` and every deploy) fails at each place the website still
uses the old one, instead of the site breaking for visitors. If you forget to
commit the regenerated file, a test (`TypeScriptContractsTests`) fails and
says to run `./scripts/gen-types.sh`.

Make `client/src/views/RecipesView.vue`: load the list when the page opens,
show errors, and update the list after each change (any page in
`client/src/views/` is a good model; `AuthorsView.vue` is a small one). Add its
address in `client/src/router.ts`:

```ts
{ path: '/recipes', component: () => import('./views/RecipesView.vue'), meta: { requiresAuth: true } },
```

(`meta: { requiresAuthor: true }` for a page only authors may use.) Then add
`recipes` to `PageRules.Reserved` in `src/WebBase.Api/Pages/PageRules.cs`, so a
page written in the browser can't take the address; a test fails until you do.
And a link in the menu in `client/src/App.vue`:

```html
<RouterLink to="/recipes">Recipes</RouterLink>
```

### 6. Tests

- **The API**: a `RecipesApiTests.cs` in `tests/WebBase.Tests/`, using
  `SignedInClientAsync()` to get a signed-in client. Keep the "one user cannot see
  another user's rows" test above all: two clients, one creates a recipe, the
  other must get an empty list and a 404 when deleting it.
- **The store**: tests for recipes in `tests/WebBase.Testing/StoreContractTests.cs`
  (add, list newest first and only the owner's, remove someone else's returns
  false). They run against real PostgreSQL, the in-memory database, and any other
  database module.

Then `./scripts/test.sh`.

## Tests

`./scripts/test.sh` runs:

- **C# tests** (`tests/WebBase.Tests/`): the whole API started in memory, with
  an in-memory database, called over HTTP like the browser does. `TestApp.cs`
  sets it up, and `SignedInClientAsync()` gives you a client that's already
  signed in.
- **Each module's tests** (`tests/WebBase.Database.*.Tests/`,
  `tests/WebBase.FileStorage.*.Tests/`): the same contract suites
  (`tests/WebBase.Testing/`) against each implementation. Postgres runs on a
  real PostgreSQL that `test.sh` starts and removes again; each test gets a
  fresh database, built by the migrations, so those are tested too. S3, Azure
  Blob and Google Cloud Storage run against local stand-ins (S3Mock, Azurite,
  fake-gcs-server) that `test.sh` starts too. Cosmos DB needs its emulator
  (its README). Run from an IDE without these, they show as skipped.
- **The website's type check and build.** TypeScript catches calling the API
  with the wrong shape, typos in property names, and so on. The API's types
  come from the C# (`client/src/api-types.ts`), and a test fails if that file
  is out of date.

`./scripts/smoke.sh [address]` checks a *running* site end to end: sign up,
sign in, write and publish a page. Use it after `./dev.sh`, after `./deploy.sh`
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
