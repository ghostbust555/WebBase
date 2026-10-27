# Azure Container Apps

Your site on Microsoft Azure, with HTTPS and an address included, from one
script. **All you need is a browser**: Azure Cloud Shell has the tools, already
signed in, and no Docker is needed (the image is built in Azure).

About 30 minutes, once. This page covers:

- [What's free, and what isn't](#whats-free-and-what-isnt)
- [Step by step](#step-by-step)
- [Free or standard](#free-or-standard), [files for the CMS](#files-for-the-cms-in-blob-storage), [changing settings](#changing-settings)
- [Releasing changes](#releasing-changes), [removing it](#removing-it), [if something goes wrong](#if-something-goes-wrong)

## What's free, and what isn't

Per month, as published in October 2026 (check [the current list](https://azure.microsoft.com/pricing/free-services/)):

| Service | Used for | Free? |
|---|---|---|
| **Container Apps** | The website and API | **Always free**: 180,000 vCPU-seconds, 360,000 GiB-seconds and 2 million requests per subscription. The free setup scales to zero when nobody visits |
| **Cosmos DB** | The data (the free setup's default) | **Always free, one account per subscription**: 1000 RU/s and 25 GB. [About it](#the-database-cosmos-db-or-postgresql) |
| **Database for PostgreSQL** | The data, if you pick it (`AZURE_DATABASE=postgres`) | **Free for the first 12 months** of a new Azure account: 750 hours of the smallest size (B1ms), 32 GB, 32 GB of backups. About $13/month after. Or bring a free [Neon](https://neon.tech) database: `AZURE_DATABASE_URL` |
| **Blob Storage** | The CMS's pages, if you use them | **Free for the first 12 months**: 5 GB (locally redundant, hot), 20,000 reads and 10,000 writes a month. Cents a month after, at this size |
| **Outbound traffic** | Pages sent to visitors | The first 100 GB a month is free |
| **Container Registry** | The app's images | **Not on the free lists I could find**: the Basic tier is about $5/month. It's the one cost the free setup can't avoid. If your Azure free-services page lists it, you're ahead |

So the free setup costs about **$5/month**, the registry. The database is
free for good, on Cosmos DB. (With Azure's PostgreSQL instead, it is $5 for
the first year and about $18 after.) Everything else is free. Azure
needs a card even for the free tier; a new account's trial credit is not
charged unless you upgrade. **Set a budget alert** (below).

The first visit after a quiet spell takes a few seconds: the app was asleep.

## Step by step

You need an Azure account (<https://azure.microsoft.com/free>) and a GitHub
repository with this code in it (**Use this template**, or a copy you've pushed).

1. **A budget alert**: <https://portal.azure.com/#view/Microsoft_Azure_CostManagement/Menu/~/budgets>
   → *Add*, for your subscription, amount **$20**, with an email alert. It
   warns you; it doesn't stop anything.
2. **Open Cloud Shell**: <https://shell.azure.com> (choose *Bash*; it asks to
   create a small storage account the first time).
3. **Get the code and run setup**:
   ```bash
   git clone https://github.com/YOUR-NAME/YOUR-SITE.git
   cd YOUR-SITE
   ./scripts/cloud/azure.sh setup
   ```
   A **private** repository? GitHub asks for a username and a password: the
   password is a read-only token for this repository, [the token, step by step](CLOUD.md#a-private-repository-the-github-token).
4. **Wait**: 10-20 minutes, most of it the database server. It prints your
   address, ending in `.azurecontainerapps.io`.
5. **Check it**: open the address and **sign up straight away**: the first account is an author ([AUTHORS.md](AUTHORS.md#sign-up-right-after-you-deploy)). Try **Pages** → **New page**.

What setup made, all in one *resource group* named `webbase-rg` (a folder
holding everything, so removing it removes all of it):

| Piece | How it's linked |
|---|---|
| The **app** (Container Apps) | Runs as its own *managed identity*, an account that exists only for this app |
| The **registry** | That identity may pull images from it (the *AcrPull* role) and do nothing else with it. No registry password exists |
| The **database** | The app gets its connection string as a *secret*. Cosmos DB: an account whose key is in that secret. PostgreSQL: a server that accepts connections from Azure's own services, with the password, over TLS |
| The **sign-in key** | Made once, kept as a secret of the app. Never printed, never in a file |
| **Blob Storage** (only with the CMS) | That identity may *read* the one container (*Storage Blob Data Reader*), nothing more |

You can run `setup` again at any time: it changes only what differs.

## The database: Cosmos DB or PostgreSQL

The free setup uses **Cosmos DB**, because its free tier **never expires**:
1000 RU/s and 25 GB, plenty for a small site, for one account per
subscription. Azure's PostgreSQL is free only for a new account's first year.
Both are modules of the app ([MODULES.md](MODULES.md)), and the script builds
the image with the one you choose, so there's nothing to change in the code.
`setup` makes the free account (3-5 minutes, instead of PostgreSQL's 10) and
keeps its connection string as a secret of the app. The three containers
share the free 1000 RU/s.

What to know about Cosmos DB:

- **It isn't PostgreSQL.** The app's data goes through the same stores
  either way, but there are no migrations, `backup.sh` doesn't apply, and
  tools that expect SQL won't work. Cosmos DB has its own backups: turn on
  *continuous backup* in the portal if the data matters.
- **One free account per subscription.** If another account already uses it,
  `setup` stops and says so. Then: `AZURE_COSMOS_SERVERLESS=true` makes a
  pay-per-use account (cents a month at this size), or `AZURE_DATABASE=postgres`.
- **The connection string includes a key**, kept in the app's secrets.
  (The CMS storage uses the identity instead: no key.)
- Going over 1000 RU/s doesn't charge you: requests are slowed (429) instead,
  since the throughput is a fixed amount.

To use PostgreSQL instead, put `AZURE_DATABASE=postgres` in `cloud.env`
before the first `setup` (it creates a server, free for 12 months on a new
account, about $13/month after), or `AZURE_DATABASE_URL` for one you already
have, such as a free [Neon](https://neon.tech) one. `AZURE_TIER=standard`
defaults to PostgreSQL too.

An existing site keeps the database it has: a deploy reads it from the app's
secret. Moving data between the two kinds is a one-off job
([DATABASE.md](DATABASE.md)), so choose before you have users.

## Free or standard

`AZURE_TIER` (default `free`):

| | `free` | `standard` |
|---|---|---|
| App size | 0.25 CPU, 0.5 GB | 0.5 CPU, 1 GB |
| Copies | 0 to 2 (sleeps when idle) | 1 to 5 (always on, no cold starts) |
| Database | Cosmos DB free account (or smallest PostgreSQL) | PostgreSQL General Purpose (2 vCores), 64 GB (or Cosmos DB serverless) |
| Roughly | $5/month (see above) | $150-200/month |

Choose before the first `setup`, by putting `AZURE_TIER=standard` in
`cloud.env` (below) and running it. Switching later changes the app's size at
once, but not an existing database server's: for that, make a new one.

## Files for the CMS, in Blob Storage

The [CMS](CMS.md)'s pages can come with the code (`content/`, deployed with
each release: nothing to set up), or from Blob Storage, so they can change
without a release:

1. In `cloud.env`: `CMS_ENABLED=true` and `CMS_SOURCE=azure`. Commit and push.
2. In Cloud Shell: `git pull`, then `./scripts/cloud/azure.sh setup`. It makes
   the storage account and a private container named `content`, lets the
   app read it, and builds the app with the Blob Storage module.
3. Upload the pages (Cloud Shell, in your clone):
   ```bash
   az storage blob upload-batch --auth-mode login --overwrite \
     --account-name "$(az storage account list -g webbase-rg --query '[0].name' -o tsv)" \
     --destination content --source content
   ```
   (Your own account needs the *Storage Blob Data Contributor* role on the
   storage account for `--auth-mode login`: Portal → the storage account →
   *Access control (IAM)* → *Add role assignment*.) Changes show on the site
   within a minute.

## Floods of requests

The app bans an address that sends too many requests, and caps the total; the
app runs at most 2 copies (5 with `AZURE_TIER=standard`), so a flood can't make
the compute bill grow without limit. Details and limits:
[CLOUD.md](CLOUD.md#floods-of-requests-and-your-bill).

## Changing settings

| What | Where | Then |
|---|---|---|
| The site's name, Google sign-in's client id, the CMS | `cloud.env`, committed. Public settings only: the scripts refuse to run with a secret in it | Push, then `./scripts/cloud/azure.sh deploy` |
| The database | Cloud Shell: `AZURE_DATABASE_URL='postgres://...' ./scripts/cloud/azure.sh setup` | Applies at once |
| Region, resource group, tier | `AZURE_LOCATION`, `AZURE_RESOURCE_GROUP`, `AZURE_TIER` in `cloud.env`, before the first setup | |

Put `AZURE_LOCATION` in `cloud.env` if you don't want the default (`eastus`),
so a deploy from anywhere finds the same place. Google sign-in also needs
your address among the client's allowed origins ([GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)).

## Releasing changes

In Cloud Shell: `cd YOUR-SITE && git pull && ./scripts/cloud/azure.sh deploy`.
It builds the new version in Azure, rolls it out, and checks `/api/health`.

If Claude Code on the web is changing your code, it can't run this for you:
the script needs your Azure login, and giving a coding assistant that is a
bigger step than it looks. The split that works: Claude changes code and
pushes (its tests run in GitHub), and you run `deploy` in Cloud Shell. (Only
the [Google Cloud setup](GOOGLE_CLOUD.md) deploys by itself on every push.)

## Removing it

`./scripts/cloud/azure.sh destroy` deletes the resource group: the app, the
registry, the storage and Azure's database **with all its data**. A Neon
database you brought is left alone.

## If something goes wrong

| Symptom | Fix |
|---|---|
| `setup` says *AuthorizationFailed*, or can't assign a role | Your account needs the **Owner** role on the subscription (Portal → Subscriptions → Access control). A work account may forbid it: use a personal one |
| *ACR Tasks* / the build fails with no Docker | Some subscriptions can't use Azure's builders. Run `deploy` on a computer with Docker, signed in with `az login` |
| `doesn't answer` after a deploy | `./scripts/cloud/azure.sh logs` shows the app's own error, often a wrong database string |
| Cosmos DB says *high demand in East US region* (*ServiceUnavailable*) | Azure has no room for new Cosmos DB accounts in that region for now. Delete the half-made group (`az group delete -n <name>-rg --yes`), put another region in `.env.local` (`AZURE_LOCATION=eastus2`, or `westus2`, `centralus`) and run `setup` again |
| *The subscription is not registered to use namespace* | Run `setup` again: it registers them, and the first time can be slow |
| The site is slow on the first visit | It was idle and is starting: a few seconds, then normal. `AZURE_TIER=standard` keeps it awake |
| Name already taken (registry, storage, database) | Names include a code from your subscription, so this means you already made one: run `setup` again, or remove the old resource group |

More in [TROUBLESHOOTING.md](TROUBLESHOOTING.md). Your own domain: Portal →
the Container App → *Custom domains* (a free certificate is included).
Other clouds: [CLOUD.md](CLOUD.md).
