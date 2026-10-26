# WebBase

A starting point for a new website of any kind. Copy it, give it a name, and
you have a working site with accounts and a database on day one, ready to
grow into whatever you're building.

| Piece | What it is |
|---|---|
| **Website** | [Vue 3](https://vuejs.org) + TypeScript, built with [Vite](https://vite.dev) - `client/` |
| **API** | C# / [ASP.NET Core 10](https://learn.microsoft.com/aspnet/core) - `src/WebBase.Api/` |
| **Database** | [PostgreSQL 18](https://www.postgresql.org), via Entity Framework Core |
| **Sign-in** | Email + password built in; "Sign in with Google" switched on by one setting |
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
| Deploy to a cloud instead | `./scripts/cloud/azure.sh` / `aws.sh` / `gcp.sh` - see [docs/CLOUD.md](docs/CLOUD.md) |

## The guides

1. **[Getting started](docs/GETTING_STARTED.md)** - from nothing to a running site.
2. **[How it fits together](docs/HOW_IT_FITS_TOGETHER.md)** - what each piece does and how a click becomes a database row.
3. **[Developing](docs/DEVELOPING.md)** - making changes, adding a feature (with a worked example), tests.
4. **[Deploying on your own computer](docs/DEPLOYING.md)** - production, releases, rollback, backups.
5. **[Your own domain with Cloudflare Tunnel](docs/CLOUDFLARE_TUNNEL.md)** - public, with no router changes.
6. **[Deploying to a cloud](docs/CLOUD.md)** - Azure, AWS, Google Cloud, Fly.io, Render, Railway, a VPS.
7. **[Google sign-in](docs/GOOGLE_SIGNIN.md)** - optional.
8. **[Settings reference](docs/CONFIGURATION.md)** - every setting and secret.
9. **[Troubleshooting](docs/TROUBLESHOOTING.md)** - when something doesn't work.

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
4. Delete the example "Notes" feature once you've used it as a pattern
   ([docs/DEVELOPING.md](docs/DEVELOPING.md#removing-the-example)).

## What's in the folder

```
client/              the website (Vue). Pages are in client/src/views/
src/WebBase.Api/     the API (C#). Endpoints are in Controllers/
tests/WebBase.Tests/ automated tests for the API
ops/                 the production Dockerfile and database setup
docs/                the guides above
scripts/             helpers: tests, migrations, backups, cloud deploys
compose.dev.yaml     what ./dev.sh runs
compose.prod.yaml    what ./deploy.sh and ./tunnel.sh run
.env.example         every setting, explained; ./setup.sh copies it to .env.local
setup.sh dev.sh deploy.sh tunnel.sh   the four commands you'll use most
CLAUDE.md            notes for AI coding assistants working in this repository
```
