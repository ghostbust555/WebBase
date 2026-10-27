# Authors: who may edit the site

An **author** is an account that may change the site's own content: the pages
written in the browser ([PAGES.md](PAGES.md)), and the list of authors itself.
Out of the box that is all it guards. A site can build on it (any page of
editing tools can sit behind it) or ignore it: nothing else looks at it.

- [How someone becomes an author](#how-someone-becomes-an-author)
- [Sign up right after you deploy](#sign-up-right-after-you-deploy)
- [Managing authors](#managing-authors)
- [If the wrong person got in first](#if-the-wrong-person-got-in-first)
- [Guarding your own features](#guarding-your-own-features)
- [Where it lives in the code](#where-it-lives-in-the-code)

## How someone becomes an author

1. **The first account ever created.** Whoever signs up first (with a password or
   with Google) is an author. Nothing to configure.
2. **An author adds them.** On the **Authors** page (in the menu, for authors),
   enter the email of someone who has already signed up.
3. **`Auth:AuthorEmails`** (optional; `AUTHOR_EMAILS` in `.env.local`, or in the
   environment when a cloud script runs). A list of emails, separated by commas
   or spaces. An account with one of those emails becomes an author at its next
   sign-in **with Google**, because only then has anyone confirmed that the
   person owns the address.

Why not for password sign-ups? Anyone can type any address into a sign-up form;
the site never checks it (there is no email-verification step). If listed emails
were trusted there, a stranger could register `boss@your-site.com` before the
boss does and be made an author. So the list is matched only against an email
that Google has verified, and never against a password sign-up. If your site has
no Google sign-in ([GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)), the list does nothing,
and the first account plus the Authors page are the way in.

The list only ever *adds* authors, at sign-in. To remove someone who is on it,
remove them on the Authors page **and** take their email off the list, or they
become an author again at their next Google sign-in.

## Sign up right after you deploy

The rule "the first account is an author" has two sharp edges:

- **A new site has no accounts, so the first visitor to sign up is the author.**
  Between the moment you deploy and the moment you sign up, that could be a
  stranger who found the address. So: **deploy, then sign up immediately**, and
  check that your account has the **Authors** link. Don't point a public domain
  at the site first. (Local development doesn't matter.)
- **Two first sign-ups at the same instant can both become authors.** The code
  checks "is anyone here yet?" and then saves, which is not one atomic step in
  every database (the databases here can't offer that across the check and the
  save). The window is a few milliseconds long. Both would be authors, which is
  the harmless way to be wrong; an author can remove the other.

`./scripts/smoke.sh` signs up throwaway accounts. On a brand-new real site it
would take the first-author slot, so sign up yourself before running it. If it
does take it, it prints that account's email and password at the end, so you
can sign in with it and make yourself an author.

## Managing authors

The **Authors** page lists every author, adds one by email, and removes one.

- Adding needs an **existing account**: ask the person to sign up first.
  Adding someone who already is an author is not an error.
- **Nobody can remove themselves**, so there is always at least one author. To
  step down, add another author and ask them to remove you.
- Taking the role away is **immediate**: see [Where it lives](#where-it-lives-in-the-code).

## If the wrong person got in first

If the first account is not yours (or the owner has lost their sign-in), change
the database directly, once:

- **PostgreSQL**: `UPDATE "Users" SET "IsAuthor" = true WHERE "Email" = 'you@example.com';`
  (in development: `./dev.sh psql`; elsewhere any SQL client with the owner's
  connection string).
- **Cosmos DB**: in the portal's *Data Explorer*, open the `users` container, edit
  your document and set `"isAuthor": true`.
- **Firestore**: in the Google Cloud console's *Firestore* page, open the `users`
  collection, find the document with your `email`, and set `isAuthor` to `true`.

Or, with Google sign-in on, set `AUTHOR_EMAILS` to your address and sign in with
Google. Then remove the stranger on the Authors page.

## Guarding your own features

Put `[RequireAuthor]` next to `[Authorize]`:

```csharp
[ApiController]
[Route("api/editor/banners")]
[Authorize, RequireAuthor]
public class BannersController(IBannerStore banners) : ControllerBase { ... }
```

- Signed out: **401**. Signed in, not an author: **403** with `{ "error": "Only authors can do that." }`.
- In the website, `meta: { requiresAuthor: true }` on a route sends a signed-out
  visitor to `/login` and anyone else who isn't an author to `/`. That is only
  convenience; the filter on the API is the protection. Show author-only links
  with `v-if="auth.user?.isAuthor"`.
- **Site-owned content takes no owner id.** The usual rule is that every store
  method on user data takes the owner's id ([DEVELOPING.md](DEVELOPING.md#adding-a-feature-step-by-step),
  [DATABASE.md](DATABASE.md)). Content that belongs to the *site* has no owner,
  so its store takes none (`IPageStore`), and `[RequireAuthor]` on the endpoints
  is its guard instead. Keep that guard on every endpoint that writes.

## Where it lives in the code

| Piece | File |
|---|---|
| The flag | `User.IsAuthor` in `src/WebBase.Core/Models/User.cs` (a migration added the column) |
| Two store methods | `IUserStore.AnyAsync()` (is this the first account?) and `ListAuthorsAsync()`, in every database module and the in-memory test store |
| The first-account and `AuthorEmails` rules | `src/WebBase.Api/Services/AuthService.cs` |
| The filter | `src/WebBase.Api/Infrastructure/RequireAuthor.cs` |
| The Authors endpoints | `src/WebBase.Api/Controllers/AuthorsController.cs`: `GET /api/authors`, `POST /api/authors` (`{ "email": ... }`), `DELETE /api/authors/{id}` |
| What the browser learns | `UserDto.IsAuthor` (`/api/auth/me` and the sign-in answers) |
| The website | `requiresAuthor` in `client/src/router.ts`, the links in `client/src/App.vue`, `client/src/views/AuthorsView.vue` |
| Tests | `tests/WebBase.Tests/AuthorsApiTests.cs`; the store contract in `tests/WebBase.Testing/StoreContractTests.cs`; `TestApp.AuthorClientAsync()` makes a signed-in author |

**Why revocation is immediate.** The sign-in token says who someone *was* when it
was made, and lasts 30 days. If the role were in the token, a removed author
would stay one until it expired. So the token carries no role, and
`[RequireAuthor]` reads the user from the database on every request (one
lookup by id). `UserDto.IsAuthor` is read fresh too, but the website keeps its
copy until the page reloads, so a removed author may still *see* the links;
every call they then make is refused with 403.

**In the tests**, the first account made on a `TestApp` is an author, which would
make whichever test ran first one by accident. So `SignedInClientAsync()` first
creates a throwaway account and everyone after it is an ordinary user;
`AuthorClientAsync()` makes an author. A test of the first-account rule itself
uses a fresh `TestApp` and never calls those.
