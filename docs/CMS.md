# The CMS (optional)

A small content system: every page is a **markdown file**, and the folders
are the addresses. No database, no admin screen. You write files, and they're
on the site. It's good for an "About" page, a blog, help pages, or any text
you'd rather write than code. The same files can also set the site's
**colours** ([theme.css](#the-sites-look-themecss)).

The files can live in the `content/` folder of this repository (the default),
or in an **S3 bucket**, an **Azure Blob Storage container** or a **Google
Cloud Storage bucket**, so writers can publish without a release
([Where the content lives](#where-the-content-lives)).

It's **off** until you turn it on, and while it's off nothing about it is
reachable.

## Turn it on

In `.env.local`:

```
CMS_ENABLED=true
```

then `./dev.sh restart`. With the example content that ships in `content/`,
the home page becomes `content/index.md`, and **About** and **Blog** appear
in the menu.

## Writing pages

| File | Address |
|---|---|
| `content/index.md` | `/` (replaces the home page while it exists) |
| `content/about.md` | `/about` |
| `content/blog/index.md` | `/blog`, followed by a list of the pages in `blog/` |
| `content/blog/hello-world.md` | `/blog/hello-world` |
| `content/help/` with no `index.md` | `/help`, just the list |

- File and folder names become addresses, so use **lowercase letters,
  digits and dashes** (`my-first-post.md`). Other names are ignored.
- Names starting with `_` or `.` are ignored and never served. Use `_` for
  reminders to yourself (`_ideas.md`).
- In development, saving a file and reloading the browser shows the change.

### Page settings

Optional, between `---` lines at the very top of the file:

```markdown
---
title: My first post
description: One line, shown in lists.
date: 2026-10-25
order: 1
menu: true
draft: true
---

The page itself starts here, in **markdown**.
```

| Setting | Meaning |
|---|---|
| `title` | The page's heading and browser tab. Without it, the file name: `my-first-post` → "My first post" |
| `description` | Shown under the title in folder lists |
| `date` | `YYYY-MM-DD`. Shown on the page. Lists show newest first |
| `order` | A number. Lower numbers come first in lists and the menu, ahead of pages without one |
| `menu` | `true` puts the page in the site's header menu |
| `draft` | `true` shows the page in development only. Remove it to publish |

### Markdown

Everything GitHub-style markdown does: `# headings`, **bold**, *italic*,
lists, tables, `code`, code blocks, > quotes, and links.

- **Links to other pages:** `[About](/about)`, or relative to this file,
  like on GitHub: `[the other post](other-post.md)`.
- **Pictures and downloads:** put the file next to the pages and refer to it
  relative to the page, e.g. `![A cat](images/cat.jpg)` from `about.md`
  means `images/cat.jpg`. Those files are served at `/content/...`, except
  the `.md` files, which are only ever shown as pages.
- HTML inside markdown is allowed, but anything that could run code
  (`<script>`, `onclick=`, `javascript:` links) is removed before it's shown.

## The site's look: theme.css

`theme.css` in the content (`content/theme.css` here) sets the site's colours
without touching code. It's optional: without it, or with every line
commented out as it ships, the site keeps the colours in
`client/src/style.css`.

```css
:root {
  --accent: #7c3aed;
  --radius: 4px;
  --font: Georgia, serif;
}

@media (prefers-color-scheme: dark) {
  :root {
    --accent: #c4b5fd;
  }
}
```

- **Only CSS variables are used**: the `--name: value` lines inside `:root`,
  and inside `:root` in the dark-mode block. Everything else in the file
  (other rules, `body { ... }`, imports) is dropped. So the file can't break
  the layout, or hide or add content, whoever edits it. The variables you
  can set are the ones at the top of `client/src/style.css`; the shipped
  `theme.css` lists them, commented out.
- The site loads it from `/api/content/theme.css` after its own styles, so
  its values win. Browsers check for a new version on every visit.
- While the CMS is off, that address is an empty stylesheet.

## Where the content lives

`CMS_SOURCE` in `.env.local` (`Cms__Source` on a host) says where to read
from. The layout inside is the same everywhere: the files and folders of
`content/`.

Every source but `files` is a **module**: its own project, with that cloud's
library, which the app is only built with when you ask
([MODULES.md](MODULES.md)). The cloud scripts build the app with the one `CMS_SOURCE` names, so in a cloud there is nothing to add. Elsewhere, add the one you need first, and commit:

```bash
./scripts/module.sh add s3      # or azure, or gcs
```

Without it, the app stops at startup and says so.

| `CMS_SOURCE` | The files are in | Settings |
|---|---|---|
| `files` (default) | A folder: `content/` in this repository | none |
| `s3` | An Amazon S3 bucket, or an **S3-compatible** one: Cloudflare R2, DigitalOcean Spaces, Backblaze B2, MinIO... | `CMS_S3_BUCKET`, and optionally `CMS_S3_PREFIX`, `CMS_S3_REGION`, `CMS_S3_SERVICE_URL`, `CMS_S3_ACCESS_KEY` + `CMS_S3_SECRET_KEY` |
| `azure` | An Azure Blob Storage container | `CMS_AZURE_CONTAINER_URL`, or `CMS_AZURE_CONNECTION_STRING` + `CMS_AZURE_CONTAINER`; optionally `CMS_AZURE_PREFIX` |
| `gcs` | A Google Cloud Storage bucket | `CMS_GCS_BUCKET`, optionally `CMS_GCS_PREFIX` |

The **prefix** is a folder inside the bucket, e.g. `www`, so one bucket can
hold several sites, or other things too. Empty means the whole bucket.

### S3, and S3-compatible storage

```
CMS_SOURCE=s3
CMS_S3_BUCKET=my-site-content
CMS_S3_PREFIX=www
CMS_S3_REGION=eu-west-1
```

- **Credentials**: with `CMS_S3_ACCESS_KEY` / `CMS_S3_SECRET_KEY` empty, the
  app uses the standard AWS ones: the role of the ECS task or EC2 machine,
  `AWS_*` environment variables, or `~/.aws`. Read-only access is enough:
  `s3:ListBucket` on the bucket and `s3:GetObject` on its objects.
- **Not AWS?** Set `CMS_S3_SERVICE_URL` to the provider's endpoint (for R2,
  `https://<account-id>.r2.cloudflarestorage.com`), and the access key and
  secret from its dashboard. The region can stay empty.
- The bucket doesn't need to be public, and shouldn't be: visitors get the
  files through the app.

### Azure Blob Storage

```
CMS_SOURCE=azure
CMS_AZURE_CONTAINER_URL=https://mystorageaccount.blob.core.windows.net/site-content
CMS_AZURE_PREFIX=www
```

- With a plain container URL, the app signs in as **itself**: its managed
  identity on Azure, or your `az login` on your computer. Give that identity
  the **Storage Blob Data Reader** role on the container.
- Or add a SAS token to the URL (`...site-content?sv=...&sig=...`), made with
  *read* and *list* permission. **It's a secret.**
- Or set `CMS_AZURE_CONNECTION_STRING` (storage account → *Access keys*) and
  `CMS_AZURE_CONTAINER`. **A secret too**, and it can write as well as read;
  prefer the other two.

### Google Cloud Storage

```
CMS_SOURCE=gcs
CMS_GCS_BUCKET=my-site-content
CMS_GCS_PREFIX=www
```

- No keys: the app signs in as itself, the Cloud Run service's own account.
  On Cloud Run, `./scripts/cloud/gcp.sh setup` creates the bucket and gives
  that account **Storage Object Viewer** on it ([GOOGLE_CLOUD.md](GOOGLE_CLOUD.md#the-cms-in-a-cloud-storage-bucket)).
- On your own computer, the app uses your `gcloud auth application-default login`.

### Publishing to a bucket

Upload the folder. Pages change on the site within a minute
([below](#how-quickly-changes-show)), with no release.

```bash
# S3 (--delete also removes pages you've deleted locally)
aws s3 sync content/ s3://my-site-content/www/ --delete

# S3-compatible: the same, with the provider's endpoint
aws s3 sync content/ s3://my-site-content/www/ --delete --endpoint-url https://<account-id>.r2.cloudflarestorage.com

# Google Cloud Storage (also removes pages you've deleted locally; Cloud Shell has gcloud)
gcloud storage rsync content gs://my-site-content/www --recursive --delete-unmatched-destination-objects

# Azure (upload-batch doesn't remove deleted files; delete those in the portal,
# or use azcopy sync with --delete-destination=true)
az storage blob upload-batch --auth-mode login --account-name mystorageaccount \
  --destination site-content --destination-path www --source content --overwrite
```

The account that **uploads** needs write access. The app only ever reads.

Things that change once content is in a bucket:

- `content/` in the repository is no longer what that site shows. Keep it as
  your working copy and sync it, or let writers work in the bucket directly
  (S3 console, Azure Storage Explorer).
- A release doesn't publish content, and `./deploy.sh rollback` doesn't roll
  it back. For a history of changes, turn on the bucket's **versioning**
  (S3) or **soft delete and versioning** (Azure).

### In a cloud

On a cloud host, give the app access with the cloud's own identity, not a key:

- **With the cloud scripts**: nothing to do. `gcp.sh`, `azure.sh` and `aws.sh`
  setup make the bucket or container and the app's permission to read it
  ([GOOGLE_CLOUD.md](GOOGLE_CLOUD.md#the-cms-in-a-cloud-storage-bucket),
  [AZURE.md](AZURE.md#files-for-the-cms-in-blob-storage), [AWS.md](AWS.md#files-for-the-cms-in-s3)).
- **AWS (ECS), by hand**: allow the task role `s3:ListBucket` and `s3:GetObject` on the bucket.
- **Azure (Container Apps), by hand**: turn on the app's managed identity
  (`az containerapp identity assign --system-assigned`) and give it
  *Storage Blob Data Reader* on the container.
- **Elsewhere** (Render, Fly, a non-AWS bucket...): an access key or SAS
  token, stored in that host's secret settings.

The cloud scripts copy `CMS_ENABLED`, `CMS_SOURCE` and the bucket, prefix,
region and URL settings from `.env.local` (or `cloud.env`, committed). They **don't** copy the access
keys or connection string: they warn instead, because those belong in the
host's secrets. See [CLOUD.md](CLOUD.md#the-cms-content).

### How quickly changes show

The app keeps the content in memory and looks for changed files every
`Cms__RefreshSeconds` seconds: **60** by default, **0** (every request) in
development. A look lists the files and re-reads only the changed ones, so
it's cheap even for a large site.

- If the bucket can't be reached, the site keeps showing the last copy it
  read. If it has never managed to read it (say, the first start with a wrong
  setting), content pages answer **503** "The site's content can't be
  reached right now", the startup log names the store it's using, and the
  rest of the app works normally.
- Browsers keep pictures and other files for 5 minutes, so a picture
  replaced under the same name can show the old version for that long.
  Pages and `theme.css` aren't affected.

## How it works

```
 browser: /blog/hello-world
     │  no app page has that address, so router.ts sends it to ContentView.vue
     ▼
 GET /api/content/page?path=blog/hello-world
     │  ContentController → ContentService → IFileStore (a folder, or a storage module)
     ▼
 { title, date, markdown, children } → ContentPageView.vue → markdown rendered and sanitized in the browser
```

| Piece | File |
|---|---|
| Pages, settings, drafts, the copy in memory | `src/WebBase.Api/Content/ContentService.cs` |
| Where the files come from | `IFileStore` (`src/WebBase.Core/FileStorage/`), with `LocalFileStore.cs` for a folder; the clouds are modules: `modules/WebBase.FileStorage.S3`, `.AzureBlob`, `.GoogleCloud` |
| Choosing the store from `Cms__Source` | `src/WebBase.Api/Content/ContentRegistration.cs`, and `src/WebBase.Api/Modules.cs` for the modules the app has |
| Which names are allowed and served | `src/WebBase.Api/Content/ContentPaths.cs` |
| theme.css → variables only | `src/WebBase.Api/Content/ThemeCss.cs` |
| The API (`/api/content/pages`, `/page`, `/theme.css`) and files under `/content/...` | `src/WebBase.Api/Controllers/ContentController.cs` |
| Address → page (a file first, then a page written in the browser, [PAGES.md](PAGES.md)), or "not found" | `client/src/views/ContentView.vue` |
| How a page looks | `client/src/components/ContentPageView.vue` and `.content-page` in `style.css` |
| Markdown → safe HTML | `client/src/lib/markdown.ts` |
| Menu items (files and written pages together) | `client/src/stores/menu.ts`, shown by `client/src/App.vue` |
| Tests | `tests/WebBase.Tests/ContentTests.cs`; each store's in `tests/WebBase.FileStorage.*.Tests/`, all running `tests/WebBase.Testing/FileStoreContractTests.cs` |

The app's own pages (`/login`, `/docs`, `/write`...) always win: a
`login.md` is never shown.

### Another kind of storage

Dropbox, a database, a headless CMS's API: a new file storage module, which
lists files and opens one. [MODULES.md](MODULES.md#writing-a-new-one) walks
through it; the contract tests tell you when it's done. `./scripts/test.sh`
runs the S3, Azure and Google ones against local stand-ins (S3Mock, Azurite,
fake-gcs-server), so no cloud account is needed.

## Limits, and where to go from here

This is deliberately the minimum. It doesn't have:

- **Editing these files in the browser.** The pages here are files. In `content/`
  that means git (history, review, rollback); in a bucket, the cloud's own tools.
  For pages that authors write in the browser, see [PAGES.md](PAGES.md): a second
  kind of page, kept in the database, which works alongside these (files win at
  an address both have).
- **Search-engine-friendly HTML.** Pages are drawn in the browser, which
  Google handles but some link previews don't. Rendering the markdown on the
  server instead (for example with the Markdig package) is the upgrade if you
  need it.
- **Huge sites.** Every page's text is held in memory, which is fine for
  thousands of pages. Other files pass through the app on each request, so
  large ones like videos are better served straight from a bucket or CDN,
  and linked by their full URL.

## Turning it off again

Set `CMS_ENABLED=false` (or remove the line) and restart. If you'll never
use it, you can delete `content/`, the files in the table above, and the
`Cms` lines in `appsettings.json`, the compose files and `.env.example`.
