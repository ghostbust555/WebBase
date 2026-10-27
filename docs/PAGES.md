# Pages written in the browser

The [CMS](CMS.md) turns markdown **files** into pages: you edit them with an
editor and release them with git. This is the other kind: pages that
**authors** ([AUTHORS.md](AUTHORS.md)) create and edit in the browser. They
live in the database, and a saved change is on the site at once, with no
release.

It is also the worked example for **content that belongs to the site**: it has
no owner, so [authors](AUTHORS.md#guarding-your-own-features) guard it instead
of an owner id. The worked example for content that belongs to *one user* is
[the per-user example](EXAMPLE_NOTES.md). Most sites use one pattern or the
other for each new feature.

- [Writing a page](#writing-a-page)
- [Files and written pages together](#files-and-written-pages-together)
- [Rules](#rules)
- [The API](#the-api)
- [How it is stored](#how-it-is-stored)
- [Where it lives in the code](#where-it-lives-in-the-code)
- [Copying the pattern](#copying-the-pattern)
- [What it does not do](#what-it-does-not-do)

## Writing a page

Sign in as an author and open **Pages** in the menu.

- The list shows every page with a **Draft** or **Published** label.
- **New page** opens the editor: the title, the address, whether it is in the
  menu (and its position), and the text in markdown. The preview beside it is
  live. On a phone the two are tabs: **Write** and **Preview**.
- **Save draft** keeps it private. **Publish** puts it on the site. A published
  page offers **Save** and **Unpublish** (back to a draft). **Delete** removes it.
- The first save changes the address in the browser from `/write/new` to
  `/write/<id>`; the page stays open.
- Leaving with unsaved changes (a link, the back button, closing the tab) asks
  first.
- Leave the address empty and it is made from the title: "What we do" becomes
  `what-we-do`. An address may have up to four parts (`blog/first-post`).
- **In the menu** lists a published page in the header, ordered by its
  position (lower first), then by title. The header updates when you publish.

Drafts are invisible to everyone but authors: the page answers 404, and it is not
in the menu.

## Files and written pages together

Both kinds answer at the site's addresses, so there has to be an order:

> **Files win.** If the CMS is on and `content/` has a page (or a folder that gets
> a list) at an address, that is what visitors see.

The website looks the address up in `content/` first, then in written pages
(`client/src/views/ContentView.vue`). The editor also **refuses to create a page at
an address a file already serves** (409, with a message), so you find out when you
write it, not later. If a file appears *after* a written page took the address, the file
shows and the written page keeps working for editing (saving it where it is is
allowed; moving it onto the file's address is not). The header combines both
kinds, files first, deduplicated by address (`client/src/stores/menu.ts`).

Written pages work with the CMS switched off, which is the default.

## Rules

- **Title**: required, up to 120 characters.
- **Address**: lower-case letters, digits, `-` and `_`, up to four parts separated
  by `/`, up to 100 characters. Two pages cannot share one (409: "Another page
  already uses that address").
- **Reserved addresses**: the app's own pages and server paths (`login`,
  `register`, `account`, `authors`, `write`, `docs`, `api`, `content`, `assets`,
  `scalar`...) cannot be the *first part* of a page's address, so a page can't
  hide a real one. The list is `PageRules.Reserved`
  in `src/WebBase.Api/Pages/PageRules.cs`. **When you add a route to
  `client/src/router.ts`, add its first word there**; a test
  (`Every_page_of_the_client_router_is_reserved`) fails until you do.
- **Text**: up to 80,000 characters, and no control characters other than tab and
  line breaks.
- **Menu position**: between -10,000 and 10,000.

**The size limit and `Abuse:MaxBodyKb`.** The flood guard refuses request bodies
over `Abuse:MaxBodyKb` (256 KB by default, [CONFIGURATION.md](CONFIGURATION.md)).
A character takes at most 3 bytes in a request, so the longest allowed page needs
about 240 KB, which fits. If you lower `MaxBodyKb`, long pages stop saving: the app
logs a warning at startup, and a test (`The_biggest_page_fits_the_request_body_limit`)
fails if you change one number without the other.

## The API

| Endpoint | Who | What |
|---|---|---|
| `GET /api/pages/menu` | everyone | the published pages that are in the menu, in order |
| `GET /api/pages/published?path=blog/first-post` | everyone | one published page; 404 for a draft or a missing one |
| `GET /api/editor/pages` | authors | every page, drafts too, newest change first (no text) |
| `GET /api/editor/pages/{id}` | authors | one page, whole |
| `POST /api/editor/pages` | authors | create |
| `PUT /api/editor/pages/{id}` | authors | save the whole page; `published` publishes or unpublishes |
| `DELETE /api/editor/pages/{id}` | authors | delete |

Errors are `{ "error": "..." }`: 400 for a bad title, address or text; 401 signed
out; 403 not an author; 404 no such page; 409 the address is taken (by a page or
a file). The editor endpoints are under `[Authorize, RequireAuthor]`.

## How it is stored

A `Page` has an id, the address (`Path`, unique), the title, the markdown `Body`,
`ShowInMenu` and `Order`, `PublishedAtUtc` (empty = a draft), `CreatedAtUtc`,
`UpdatedAtUtc` and `UpdatedById` (the author who saved last; just an id, so
removing an author never removes their pages).

- **PostgreSQL**: a `Pages` table with a unique index on `Path`.
- **Cosmos DB**: a `pages` container where every page is in one logical partition
  (`/site`, always `"site"`), with a **unique key on `/path`**. Cosmos only enforces
  a unique key inside one partition, so one partition is what makes the path unique
  for the whole site. A site has tens of pages, far below a partition's limits, and
  a list is a single-partition query. The unique key can only be set when a container
  is made; this one is new, so there is nothing to migrate. It passes the store contract
  against the emulator ([the module's README](../modules/WebBase.Database.CosmosDB/README.md)).

## Where it lives in the code

| Piece | File |
|---|---|
| The record | `src/WebBase.Core/Models/Page.cs` |
| The store contract | `src/WebBase.Core/Data/IPageStore.cs`; no owner id, on purpose ([AUTHORS.md](AUTHORS.md#guarding-your-own-features)) |
| PostgreSQL | `modules/WebBase.Database.Postgres/PostgresPageStore.cs`, `Pages` in `AppDbContext.cs`, the `AddPages` migration |
| Cosmos DB | `modules/WebBase.Database.CosmosDB/CosmosPageStore.cs`, the container in `CosmosDatabase.cs` |
| In-memory (tests) | `InMemoryPageStore` in `tests/WebBase.Testing/InMemory/InMemoryDatabase.cs` |
| The rules | `src/WebBase.Api/Pages/PageRules.cs` |
| The endpoints | `src/WebBase.Api/Controllers/PagesController.cs` (public), `PageEditorController.cs` (authors); shapes in `Contracts/PageContracts.cs` |
| The website | `views/PagesView.vue` (list), `views/PageEditorView.vue` (editor), `stores/menu.ts` (header), `views/ContentView.vue` (showing a page), `lib/markdown.ts` (the only place markdown becomes HTML) |
| Tests | `tests/WebBase.Tests/PagesApiTests.cs`; the page tests in `tests/WebBase.Testing/StoreContractTests.cs`; `scripts/smoke.sh` |

Markdown is rendered in the browser through `lib/markdown.ts` only (marked, then
DOMPurify), in the editor's preview and on the public page alike, so scripts and
`javascript:` links in a page do nothing. Images: use a full `https://` address,
or a file served by the [CMS](CMS.md) (`/content/...`); there is no upload.

## Copying the pattern

For another kind of site-owned content, say "Banners":

1. A model and `IBannerStore` in `WebBase.Core`, **without an owner id**.
2. The store for each database module you use, an in-memory one for the tests,
   a migration, and contract tests (copy the page ones).
3. A controller under `[Authorize, RequireAuthor]`, and public endpoints for what
   visitors may read. Return DTOs.
4. Website: a route with `meta: { requiresAuthor: true }`, a menu link with
   `v-if="auth.user?.isAuthor"`, a view, and `api.ts` calls. Keep the
   unsaved-changes guard from `PageEditorView.vue` for anything long.
5. Tests: the 401/403/200 matrix for the author endpoints, as in `PagesApiTests.cs`.

## What it does not do

- **No history.** A save replaces the previous text.
- **No protection against two authors editing the same page at once.** The last
  save wins.
- **No images or files**, no scheduled publishing, no search.
- **No translation** of the markdown beyond what the renderer does.
