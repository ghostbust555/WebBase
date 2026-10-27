# WebBase

A starting point for a new website of any kind. Copy it, give it a name, and
you have a working site with accounts and a database on day one, ready to
grow into whatever you're building.

| Piece | What it is |
|---|---|
| **Website** | [Vue 3](https://vuejs.org) + TypeScript, built with [Vite](https://vite.dev) - `client/`. The API's types are generated from the C#, so the two can't drift apart |
| **API** | C# / [ASP.NET Core 10](https://learn.microsoft.com/aspnet/core) - `src/WebBase.Api/` |
| **Database** | [PostgreSQL 18](https://www.postgresql.org), via Entity Framework Core, behind swappable stores (Cosmos DB and Firestore modules are included, used only if you add them; Google Cloud's free setup uses Firestore). Managed Postgres recommended for production |
| **Sign-in** | Email + password built in; "Sign in with Google" switched on by one setting |
| **Authors and pages** | The first account is an *author*; authors write and publish pages in the browser (markdown with a live preview) and manage who else is an author |
| **Content** | Optional CMS: markdown files become pages, and a `theme.css` sets the colours. Kept in `content/`, an S3 bucket or Azure Blob Storage; off until switched on |
| **Running it** | [Docker](https://www.docker.com) for everything - development and production |
| **Going public** | [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) from your own computer, or a cloud (Azure, AWS, Google Cloud, Fly, Render...) |

**The only thing you need installed is Docker** (plus git to download this).
The C# compiler, Node, the database and every other tool run inside
containers.

## Quick start

```bash
git clone <this repository> my-site
cd my-site
./setup.sh        # names your project and creates your secrets
./dev.sh          # starts it - then open http://localhost:5173
```

The new site's home page is the getting-started guide. Every guide in
`docs/` can be read in the browser at `/docs`.

No Docker (a cloud coding agent, a locked-down machine)? `./setupenvironment.sh --yes`, then `./scripts/dev-host.sh up`: see **[docs/ENVIRONMENT.md](docs/ENVIRONMENT.md)**.

Never used a terminal, or don't have Docker yet? Start with
**[docs/GETTING_STARTED.md](docs/GETTING_STARTED.md)** - it goes step by step.

## Everyday commands

| Want to... | Run |
|---|---|
| Work on the site (live reload) | `./dev.sh` → <http://localhost:5173> |
| See what development is doing | `./dev.sh logs` |
| Stop development | `./dev.sh stop` |
| Run all the tests | `./scripts/test.sh` |
| Add a database change | `./scripts/ef.sh migrations add <Name>` |
| Release to production on this computer | `./deploy.sh` → <http://localhost:8080> |
| Undo the last release | `./deploy.sh rollback` |
| Put production on your domain | `./tunnel.sh on` |
| Back up the production database | `./scripts/backup.sh` |
| Check a running site actually works | `./scripts/smoke.sh <address>` |
| Deploy to Google Cloud Run, free, from GitHub on every push | [docs/GOOGLE_CLOUD.md](docs/GOOGLE_CLOUD.md): a browser is all you need |
| Deploy to Azure or AWS, free setup or always-on | `./scripts/cloud/azure.sh setup` / `aws.sh setup` - see [docs/AZURE.md](docs/AZURE.md), [docs/AWS.md](docs/AWS.md) |
| Use Cosmos DB or Firestore, or S3 / Azure / Google storage for the CMS | `./scripts/module.sh add <name>` - see [docs/MODULES.md](docs/MODULES.md) |

## The guides

1. **[Getting started](docs/GETTING_STARTED.md)** - from nothing to a running site.
2. **[Setting up a machine](docs/ENVIRONMENT.md)** - `setupenvironment.sh`: with Docker or without it.
3. **[How it fits together](docs/HOW_IT_FITS_TOGETHER.md)** - what each piece does and how a click becomes a database row.
4. **[Developing](docs/DEVELOPING.md)** - making changes, adding a feature step by step, tests.
5. **[The database](docs/DATABASE.md)** - where your data should live, and switching to another database (e.g. Cosmos DB).
6. **[Modules](docs/MODULES.md)** - databases and cloud storage as separate projects, added only when you need them.
7. **[Deploying on your own computer](docs/DEPLOYING.md)** - production, releases, rollback, backups.
8. **[Your own domain with Cloudflare Tunnel](docs/CLOUDFLARE_TUNNEL.md)** - public, with no router changes.
9. **[Google Cloud Run, for free](docs/GOOGLE_CLOUD.md)** - deployed from GitHub on every push; set up from a browser.
10. **[Azure Container Apps](docs/AZURE.md)** - about $5/month, or always-on; set up from Cloud Shell.
11. **[Amazon Web Services](docs/AWS.md)** - free on Lambda, or always-on with RDS; set up from CloudShell.
12. **[Deploying to a cloud](docs/CLOUD.md)** - the three above compared, plus Fly.io, Render, Railway, a VPS.
13. **[Google sign-in](docs/GOOGLE_SIGNIN.md)** - optional.
14. **[The CMS](docs/CMS.md)** - optional: pages written as markdown files, in the repository or a bucket.
15. **[Authors](docs/AUTHORS.md)** - who may edit the site, and why to sign up right after deploying.
16. **[Pages written in the browser](docs/PAGES.md)** - the pattern for content that belongs to the site.
17. **[The bundled example feature](docs/EXAMPLE_NOTES.md)** - the pattern for content that belongs to one user, and how to remove it.
18. **[Settings reference](docs/CONFIGURATION.md)** - every setting and secret.
19. **[Troubleshooting](docs/TROUBLESHOOTING.md)** - when something doesn't work.

## Starting your next project from this one

Each new site gets its own copy:

1. On GitHub, mark this repository as a **template** (Settings → "Template
   repository"). Then for each new site press **Use this template**, and clone
   the new repository. Or just copy the folder.
2. In the new copy, run `./setup.sh` and give it a name when asked (e.g.
   `RecipeBox`). That renames everything: the C# project, the Docker images,
   the databases. You can also do it directly: `./scripts/rename.sh RecipeBox`.
3. If several projects will run on the same computer, give each one its own
   ports in `.env.local` (`DEV_WEB_PORT`, `DEV_API_PORT`, `DEV_DB_PORT`, `PROD_PORT`).
4. Replace the home page, which shows the guide until you do
   ([docs/DEVELOPING.md](docs/DEVELOPING.md#your-home-page)). The site also comes
   with a small example feature to learn from; delete it once you've used it as a
   pattern (its guide, [docs/EXAMPLE_NOTES.md](docs/EXAMPLE_NOTES.md), lists everything to remove).
5. Deploy, then **sign up straight away**: the first account becomes an author
   ([docs/AUTHORS.md](docs/AUTHORS.md#sign-up-right-after-you-deploy)).

## What's in the folder

```
client/              the website (Vue). Pages are in client/src/views/
src/WebBase.Api/     the API (C#). Endpoints are in Controllers/
src/WebBase.Core/    the records (Models/) and the interfaces the API and modules share
modules/             databases and cloud storage, one project each; the app uses only those
                     listed in src/WebBase.Api/Modules.cs (docs/MODULES.md)
tests/               automated tests: WebBase.Tests for the API, one project per module
ops/                 the production Dockerfile and database setup
content/             pages for the optional CMS (markdown files, theme.css)
docs/                the guides above (also shown in the site at /docs)
scripts/             helpers: tests, migrations, backups, cloud deploys
compose.dev.yaml     what ./dev.sh runs
compose.prod.yaml    what ./deploy.sh and ./tunnel.sh run (+ compose.prod-db.yaml: its database container)
.env.example         every setting, explained; ./setup.sh copies it to .env.local
cloud.env            public settings for cloud deploys (committed; no secrets)
.github/workflows/   tests on every push; the Google Cloud deploy once it's set up
setup.sh dev.sh deploy.sh tunnel.sh   the four commands you'll use most
CLAUDE.md            instructions for AI coding assistants working in this repository
```
