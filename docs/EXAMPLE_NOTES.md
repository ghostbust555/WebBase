# The Notes example

WebBase ships with one small, complete feature to read and copy: **Notes**.
A signed-in person writes short notes, and only they can see them. It exists
to show every layer a feature touches, for *data that belongs to one user*.
(Data that belongs to the site, written by its authors, has its own example:
[PAGES.md](PAGES.md).)

This is the only file that talks about Notes. When you have copied the
pattern, delete the feature and then this file: [Removing it](#removing-it).

- [Every file it touches](#every-file-it-touches)
- [Following one click](#following-one-click)
- [Copying it for a new feature](#copying-it-for-a-new-feature)
- [Removing it](#removing-it)

## Every file it touches

| Layer | Files |
|---|---|
| Record | `src/WebBase.Core/Models/Note.cs` (a plain class: an id, the owner's `UserId`, the text, a time) |
| Store contract | `src/WebBase.Core/Data/INoteStore.cs`: every method that reads or removes takes the owner's id |
| PostgreSQL | `modules/WebBase.Database.Postgres/PostgresNoteStore.cs`; the `Notes` table and its index in `AppDbContext.cs`; the registration in `PostgresModule.cs`; the table is created by the `Initial` migration |
| Cosmos DB | `modules/WebBase.Database.CosmosDB/CosmosNoteStore.cs`; the `notes` container (partitioned by `/userId`) in `CosmosDatabase.cs`; the registration in `CosmosModule` |
| In-memory (for tests) | `InMemoryNoteStore`, the `Notes` list in `InMemoryData`, and the registration, all in `tests/WebBase.Testing/InMemory/InMemoryDatabase.cs` |
| Endpoints | `src/WebBase.Api/Controllers/NotesController.cs`; the request and response shapes in `src/WebBase.Api/Contracts/NoteContracts.cs` |
| Generated types | `client/src/api-types.ts` (regenerates by itself from the C# above) |
| Website | `client/src/api.ts` (`notesApi`, the `Note` type), `client/src/views/NotesView.vue`, the `/notes` route in `client/src/router.ts`, the menu link in `client/src/App.vue`, the `.notes` rules in `client/src/style.css` |
| Tests | `tests/WebBase.Tests/NotesApiTests.cs` (including *one user cannot see another's rows*); the notes tests, and the one note line in `Changing_a_returned_user_saves_nothing_until_update`, in `tests/WebBase.Testing/StoreContractTests.cs` |
| Smoke test | the lines between `The example feature` and `end of the example feature` in `scripts/smoke.sh` |
| This guide | `docs/EXAMPLE_NOTES.md`, and its place in the `ORDER` list in `client/src/lib/docs.ts` |

## Following one click

What happens when a signed-in person adds a note:

1. **The page**: `client/src/views/NotesView.vue`. Pressing *Add* calls
   `notesApi.create(text)`.
2. **The API client**: `client/src/api.ts`. That sends
   `POST /api/notes` with `{"text": "..."}`, and attaches the sign-in token as
   `Authorization: Bearer eyJ...`.
3. **The address**: in development the Vite server forwards `/api/...` to the
   API container. In production the API already is that address.
4. **The pipeline**: `src/WebBase.Api/Program.cs`, section 4. The request
   passes through the flood guard, security headers, rate limiting, and then
   *authentication*, which checks the token's signature with `Jwt:Key` and
   works out which user sent it.
5. **The controller**: `Controllers/NotesController.cs`. `[Authorize]` refuses
   anyone without a valid token (401). `Create` checks the text, makes a `Note`
   with `UserId = User.GetUserId()`, and hands it to the note store
   (`INoteStore.AddAsync`).
6. **The database**: the PostgreSQL store (`PostgresNoteStore.cs`) saves it
   through Entity Framework, which sends an `INSERT` into the `Notes` table.
   (With the Cosmos DB module, `CosmosNoteStore.cs` writes a document instead.)
7. **Back**: the controller returns the new note as JSON. The page adds it
   to the list.

The rule to take from it: **every store call passes `User.GetUserId()`**, taken
from the token, never from anything the browser sends. A note asked for under
someone else's id is simply not found (404, not 403: no confirming it exists).
That is what `NotesApiTests.One_user_cannot_see_or_delete_another_users_notes`
checks.

## Copying it for a new feature

Say the new feature is "Recipes". Copy each file, rename it, and change what the
last column says. [DEVELOPING.md](DEVELOPING.md#adding-a-feature-step-by-step) explains
each step; this table is the shortcut.

| Copy | To | Change |
|---|---|---|
| `Models/Note.cs` | `Models/Recipe.cs` | the fields |
| `Data/INoteStore.cs` | `Data/IRecipeStore.cs` | the method names; keep the owner id on every read and remove |
| `PostgresNoteStore.cs` | `PostgresRecipeStore.cs` | the entity; the sort order |
| the `Note` block in `AppDbContext.cs` | a `Recipe` block, plus `DbSet<Recipe> Recipes` | lengths, indexes |
| the registration line in `PostgresModule.Register` | one for `IRecipeStore` | |
| `CosmosNoteStore.cs` | `CosmosRecipeStore.cs` | the document shape; keep the partition key on the owner |
| `InMemoryNoteStore` and its `InMemoryData` list | the same for recipes | |
| `Controllers/NotesController.cs` | `Controllers/RecipesController.cs` | the route and the checks |
| `Contracts/NoteContracts.cs` | `Contracts/RecipeContracts.cs` | the records |
| `notesApi` in `client/src/api.ts` | `recipesApi` | the paths |
| `views/NotesView.vue` | `views/RecipesView.vue` | the form and the list |
| the route and the menu link | the same for `/recipes` | |
| `NotesApiTests.cs`, the notes contract tests | `RecipesApiTests.cs`, recipe contract tests | the data; keep the isolation test |

Then `./scripts/ef.sh migrations add AddRecipes` and `./scripts/test.sh`.

## Removing it

Delete these, in this order, and run `./scripts/test.sh` at the end:

1. **Code**
   - `src/WebBase.Core/Models/Note.cs` and `src/WebBase.Core/Data/INoteStore.cs`
   - `src/WebBase.Api/Controllers/NotesController.cs` and `src/WebBase.Api/Contracts/NoteContracts.cs`
   - In `modules/WebBase.Database.Postgres/`: `PostgresNoteStore.cs`, and the `Notes`
     `DbSet`, the `Note` block of `OnModelCreating` in `AppDbContext.cs`, and the `INoteStore`
     registration in `PostgresModule.cs`
   - In `modules/WebBase.Database.CosmosDB/`: `CosmosNoteStore.cs`, the `Notes` constant and its
     `CreateContainerIfNotExistsAsync` line (and the mention in the comment) in `CosmosDatabase.cs`,
     and the `INoteStore` registration in `CosmosModule`
   - In `tests/WebBase.Testing/InMemory/InMemoryDatabase.cs`: `InMemoryNoteStore`, the `Notes`
     list in `InMemoryData`, and its `Register` line
2. **Migration**: `./scripts/ef.sh migrations add RemoveNotes`. It drops the `Notes` table, and
   with it every note. (Don't edit the `Initial` migration: databases that already ran it would not
   notice.)
3. **Cosmos DB container**: the code above no longer creates `notes`, but an account that has it
   keeps it. Delete the container in the Azure portal (*Data Explorer*) when its notes are not
   wanted any more.
4. **Website**
   - `client/src/views/NotesView.vue`
   - in `client/src/api.ts`: `notesApi`, the `Note` type, and `CreateNoteRequest` and `NoteDto` in the import
   - in `client/src/router.ts`: the `/notes` route; in `client/src/App.vue`: the `Notes` link
   - in `client/src/style.css`: the `.notes` rules
   - `client/src/api-types.ts` loses the note types by itself: run `./scripts/gen-types.sh`
5. **Tests**
   - `tests/WebBase.Tests/NotesApiTests.cs`
   - in `tests/WebBase.Testing/StoreContractTests.cs`: the notes tests under `// ---- Notes`,
     and the note line in `Changing_a_returned_user_saves_nothing_until_update`
6. **Smoke test**: in `scripts/smoke.sh`, the lines between `The example feature` and
   `end of the example feature`
7. **Optional**: `notes` can come out of `PageRules.Reserved` in
   `src/WebBase.Api/Pages/PageRules.cs`, now that no `/notes` page exists. Leaving it only keeps
   that address from being used for a written page.
8. **Last, this file**: delete `docs/EXAMPLE_NOTES.md`. Nothing depends on it. The guides that
   point here only say where to find the example, and the `ORDER` list in `client/src/lib/docs.ts`
   may keep or lose its name (a name without a file is ignored). Links to it from the other
   guides then lead nowhere; take them out when you tidy up.

`ExampleDocIsolationTests` fails if another guide starts talking about Notes, so the
guides stay readable after the deletion.
