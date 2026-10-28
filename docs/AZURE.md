# Azure Container Apps

Your site on Microsoft Azure, with HTTPS and an address included, from one
script. **All you need is a browser**: Azure Cloud Shell has the tools, already
signed in, and no Docker is needed: GitHub builds the image after each push
and keeps it in its own registry (ghcr.io), and Azure runs it from there.
Once connected, GitHub also deploys every push to `main` by itself.

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
| **The app's image** | What Azure runs | **Kept on GitHub, not Azure**: GitHub's container registry (ghcr.io), whose storage and downloads GitHub's billing docs list as currently free, private images included (they promise a month's notice before that changes). The workflow keeps only the newest 10 versions. Azure's own registry would be about $5/month: `AZURE_REGISTRY=acr` if you want it anyway |

So the free setup costs about **$0**: the database is free for good on
Cosmos DB, and everything else is in the free amounts. (With Azure's
PostgreSQL instead, it's free for the first year and about $13/month after.)
Azure needs a card even for the free tier; a new account's trial credit is
not charged unless you upgrade. **Set a budget alert** (below).

The first visit after a quiet spell takes a few seconds: the app was asleep.

## Step by step

You need an Azure account (<https://azure.microsoft.com/free>) and a GitHub
repository with this code in it (**Use this template**, or a copy you've pushed).

1. **A budget alert**: <https://portal.azure.com/#view/Microsoft_Azure_CostManagement/Menu/~/budgets>
   → *Add*, for your subscription, amount **$20**, with an email alert. It
   warns you; it doesn't stop anything.
2. **Open Cloud Shell**: <https://shell.azure.com> (choose *Bash*; it asks to
   create a small storage account the first time).
3. **Check GitHub has published the image**: your repository's **Actions**
   tab shows *Publish image* after each push to `main` (about 5 minutes).
   Green: the image is there. Never run? **Actions → Publish
   image → Run workflow**.
4. **Get the code and run setup**:
   ```bash
   git clone https://github.com/YOUR-NAME/YOUR-SITE.git
   cd YOUR-SITE
   ./scripts/cloud/azure.sh setup
   ```
   A **private** repository needs **two GitHub tokens**, each able to do one
   read-only thing. (One token can't do both: GitHub's image registry only
   takes *classic* tokens, and a classic token that can read a private
   repository can also write to every repository you have.)

   **Token 1, for `git clone`** (a fine-grained token: reads this repository's
   code, nothing else). [The token, step by step](CLOUD.md#a-private-repository-the-github-token); in short:
   <https://github.com/settings/personal-access-tokens/new> → *Only select
   repositories* → this one → *Repositories* permissions → **Contents:
   Read-only** → *Generate token*. At `git clone`, the username is your GitHub
   username and the password is this token.

   **Token 2, for Azure to download the image** (a classic token: reads your
   packages, nothing else). This link opens the form with only that box ticked:
   <https://github.com/settings/tokens/new?scopes=read:packages&description=webbase-azure>.
   Pick an expiration, press *Generate token*, and paste it when setup asks
   for **Token**. Setup keeps it as the app's registry secret, so it isn't
   asked for again; when it expires, make a new one and run
   `GHCR_TOKEN=<new one> ./scripts/cloud/azure.sh setup`.
5. **Wait**: 5-20 minutes, most of it the database. It prints your
   address, ending in `.azurecontainerapps.io`.
6. **Check it**: open the address and **sign up straight away**: the first account is an author ([AUTHORS.md](AUTHORS.md#sign-up-right-after-you-deploy)). Try **Pages** → **New page**.
7. **Deploy every push automatically**: `./scripts/cloud/azure.sh github`, then
   add the four values it prints as repository variables
   ([Releasing changes](#releasing-changes)). From then on a push to `main` is
   all a release takes.

What setup made, all in one *resource group* named `webbase-rg` (a folder
holding everything, so removing it removes all of it):

| Piece | How it's linked |
|---|---|
| The **app** (Container Apps) | Runs as its own *managed identity*, an account that exists only for this app |
| The **image** | On GitHub (ghcr.io). The app pulls it with your read-only packages token, kept as its registry secret. (`AZURE_REGISTRY=acr`: an Azure registry instead, which the app's identity may only pull from, with no password) |
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
| Roughly | about $0 (see above) | $150-200/month |

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
| The site's name, Google sign-in's client id, the CMS | `cloud.env`, committed. Public settings only: the scripts refuse to run with a secret in it | Push. Once `github` is set up, that's all; otherwise wait for *Publish image*, then `git pull && ./scripts/cloud/azure.sh deploy` |
| The token Azure pulls the image with (expired, or replaced) | Cloud Shell: `GHCR_TOKEN=... ./scripts/cloud/azure.sh setup` | Applies at once |
| The database | Cloud Shell: `AZURE_DATABASE_URL='postgres://...' ./scripts/cloud/azure.sh setup` | Applies at once |
| Region, resource group, tier | `AZURE_LOCATION`, `AZURE_RESOURCE_GROUP`, `AZURE_TIER` in `cloud.env`, before the first setup | |

Put `AZURE_LOCATION` in `cloud.env` if you don't want the default (`westus2`, West US 2),
so a deploy from anywhere finds the same place. Google sign-in also needs
your address among the client's allowed origins ([GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)).

## Releasing changes

The tests don't run on a push: start them when you want them, **Actions →
Tests → Run workflow** (any branch). A push to `main` goes straight to the
image, which still type-checks and builds the website, so a broken page
stops there.

**Automatically, on every push (once):** in Cloud Shell,

```bash
cd YOUR-SITE && git pull && ./scripts/cloud/azure.sh github
```

It makes a second identity, `<name>-deploy`, that trusts exactly one thing: a
GitHub Actions run in your repository, on `main` (a *federated credential*:
GitHub signs a token saying which repository and branch, and Azure checks it;
no key or password exists). That identity may change the app and read the
resource group: not the database, not the storage, nothing new. The script
ends by printing four values: add each one under your repository's **Settings
→ Secrets and variables → Actions → Variables** (variables, not secrets: none
of them is secret).

From then on, each push to `main` is published and rolled out by the
*Publish image* workflow's *deploy* job, which checks `/api/health` too. The
run's summary shows the address. Settings changed in `cloud.env` go out the
same way.

**By hand** (or before you've run `github`):

1. Push to `main`. GitHub builds and publishes the image (**Actions** tab:
   *Publish image*, about 5 minutes).
2. In Cloud Shell: `cd YOUR-SITE && git pull && ./scripts/cloud/azure.sh deploy`.
   It rolls out the image GitHub published for that commit, and checks
   `/api/health`. If the image isn't there yet, it says so: wait for the
   workflow and run it again.

To go back to an earlier version: `git checkout <commit>` (or
`AZURE_IMAGE_TAG=sha-<12 characters of it>`) and `deploy`. The newest 10
versions are kept.

With `github` done, Claude Code on the web (or anyone who can push to `main`)
releases by pushing; nobody but GitHub's own run needs access to Azure.
Without it, the split is: Claude changes code and pushes (its tests run in
GitHub), and you run `deploy` in Cloud Shell.

## Removing it

`./scripts/cloud/azure.sh destroy` deletes the resource group: the app, the
storage and Azure's database **with all its data**. A Neon database you
brought is left alone, and so are the images on GitHub (your profile →
**Packages** to delete them).

## If something goes wrong

| Symptom | Fix |
|---|---|
| `setup` says *AuthorizationFailed*, or can't assign a role | Your account needs the **Owner** role on the subscription (Portal → Subscriptions → Access control). A work account may forbid it: use a personal one |
| `setup` or `deploy` says it can't find the image | GitHub hasn't published that commit yet (Actions tab: wait for *Publish image*, or run it), Cloud Shell has commits GitHub doesn't (`git pull`), or the token is missing or expired (`GHCR_TOKEN=...` with setup) |
| The app won't start: *unauthorized* pulling from ghcr.io | The packages token expired or was deleted: make a new one and run `GHCR_TOKEN=... ./scripts/cloud/azure.sh setup` |
| The *deploy* job fails at *Azure login* with *AADSTS700213* / *AADSTS70021* (*No matching federated identity record*) | Azure doesn't trust the identity the run presented. The run's summary shows it (*Azure must trust the subject ...*; newer repositories send `repo:owner@<id>/name@<id>:...`, which `github` can only look up itself for a public repository or with `gh` signed in). In Cloud Shell: `GITHUB_OIDC_SUBJECT='<that subject>' ./scripts/cloud/azure.sh github`, then **Re-run failed jobs** |
| The *deploy* job is skipped | The four `AZURE_*` variables aren't set (Settings → Secrets and variables → Actions → **Variables**), or *Publish image* didn't succeed |
| *ACR Tasks* / the build fails with no Docker | Only with `AZURE_REGISTRY=acr`: some subscriptions can't use Azure's builders. Run `deploy` on a computer with Docker, or use the default (GitHub builds it) |
| `doesn't answer` after a deploy | `./scripts/cloud/azure.sh logs` shows the app's own error, often a wrong database string |
| Cosmos DB says *high demand in ... region* (*ServiceUnavailable*) | Azure has no room for new Cosmos DB accounts in that region for now. Delete the half-made group (`az group delete -n <name>-rg --yes`), put another region in `.env.local` (`AZURE_LOCATION=centralus`, or `eastus2`, `eastus`) and run `setup` again |
| The Container Apps environment ends *Failed*: *ManagedEnvironmentProvisioningError ... creation of the managed cluster failed, please retry later* | Azure couldn't build it in that region for now. Delete it and its log workspace (`az containerapp env delete -g <name>-rg -n <name>-env --yes`, then `az monitor log-analytics workspace delete -g <name>-rg -n workspace-... --yes --force`), put another region in `.env.local` (`AZURE_LOCATION=centralus`) and run `setup` again. The database can stay where it is |
| *The subscription is not registered to use namespace* | Run `setup` again: it registers them, and the first time can be slow |
| The site is slow on the first visit | It was idle and is starting: a few seconds, then normal. `AZURE_TIER=standard` keeps it awake |
| Name already taken (storage, database) | Names include a code from your subscription, so this means you already made one: run `setup` again, or remove the old resource group |

More in [TROUBLESHOOTING.md](TROUBLESHOOTING.md). Your own domain: Portal →
the Container App → *Custom domains* (a free certificate is included).
Other clouds: [CLOUD.md](CLOUD.md).
