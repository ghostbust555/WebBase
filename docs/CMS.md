# The CMS (optional)

A small content system: every page is a **markdown file** in the `content/`
folder, and the folders are the addresses. No database, no admin screen.
You write files, commit them, and they're on the site. It's good for an
"About" page, a blog, help pages, or any text you'd rather write than code.

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
- Names starting with `_` are ignored. Use them for notes to yourself
  (`_ideas.md`).
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
- **Pictures and downloads:** put the file in `content/` and refer to it
  relative to the page, e.g. `![A cat](images/cat.jpg)` from `content/about.md`
  means `content/images/cat.jpg`. Files in `content/` are served at
  `/content/...`, except the `.md` files, which are only ever shown as pages.
- HTML inside markdown is allowed, but anything that could run code
  (`<script>`, `onclick=`, `javascript:` links) is removed before it's shown.

## Publishing

`content/` is part of your code. Commit it, and `./deploy.sh` (or a cloud
deploy) builds it into the release, exactly like a code change. Rolling back
a release rolls back its content too.

Production needs `CMS_ENABLED=true` as well: in `.env.local` for
`./deploy.sh`, or as `Cms__Enabled=true` on a cloud host (the cloud scripts
pass `CMS_ENABLED` along by themselves).

## How it works

```
 browser: /blog/hello-world
     │  no app page has that address, so router.ts sends it to ContentView.vue
     ▼
 GET /api/content/page?path=blog/hello-world
     │  ContentController → ContentService reads content/blog/hello-world.md
     ▼
 { title, date, markdown, children } → ContentPageView.vue → markdown rendered and sanitized in the browser
```

| Piece | File |
|---|---|
| Reading the folder, the settings, drafts | `src/WebBase.Api/Services/ContentService.cs` |
| The API (`/api/content/pages`, `/api/content/page`) | `src/WebBase.Api/Controllers/ContentController.cs` |
| Files under `/content/...` | `src/WebBase.Api/Program.cs` (the "CMS's images" block) |
| Address → page, or "not found" | `client/src/views/ContentView.vue` |
| How a page looks | `client/src/components/ContentPageView.vue` and `.content-page` in `style.css` |
| Markdown → safe HTML | `client/src/lib/markdown.ts` |
| Menu items | `client/src/App.vue` |
| Tests | `tests/WebBase.Tests/ContentTests.cs` |

The app's own pages (`/login`, `/notes`, `/docs`...) always win: a
`content/login.md` is never shown.

## Limits, and where to go from here

This is deliberately the minimum. It doesn't have:

- **Editing in the browser.** Pages are files, and changes go through git.
  That's a feature (history, review, rollback), but it means writers need
  git. An admin editor would be a controller that writes files (or database
  rows) behind an `[Authorize]` check for admins.
- **Search-engine-friendly HTML.** Pages are drawn in the browser, which
  Google handles but some link previews don't. Rendering the markdown on the
  server instead (for example with the Markdig package) is the upgrade if you
  need it.
- **Caching.** Each request reads the file. That's fine for hundreds of pages
  and modest traffic.

## Turning it off again

Set `CMS_ENABLED=false` (or remove the line) and restart. If you'll never
use it, you can delete `content/`, the files in the table above, and the
`Cms` lines in `appsettings.json`, the compose files and `.env.example`.
