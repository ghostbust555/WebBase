# Google Cloud Run, for free

Your site on Google's servers, with HTTPS and an address included, at no
cost for a small site. Every push to `main` is tested and, if the tests
pass, deployed. **All you need is a browser**: nothing to install, no Docker,
no computer set up for development. It works the same whether you change
the code yourself or have Claude Code on the web do it
([below](#working-from-claude-code-on-the-web)).

About 30 minutes, once. This page covers:

- [What's free, and what isn't](#whats-free-and-what-isnt)
- [Step by step](#step-by-step): a Google Cloud project, a free database, one command, and connecting GitHub
- [Working from Claude Code on the web](#working-from-claude-code-on-the-web)
- [Changing settings](#changing-settings), [the CMS in a bucket](#the-cms-in-a-cloud-storage-bucket), [removing it](#removing-it), [if something goes wrong](#if-something-goes-wrong)

## What's free, and what isn't

Google's **Always Free** tier, per month (as published in October 2026;
check [the current list](https://cloud.google.com/free/docs/free-cloud-features)):

| Service | Used for | Free each month |
|---|---|---|
| **Cloud Run** | The website and API | 2 million requests, 180,000 vCPU-seconds, 360,000 GB-seconds of memory, 1 GB of traffic out of North America |
| **Artifact Registry** | The app's images | 0.5 GB stored. Setup keeps only the last 3 releases |
| **Secret Manager** | The sign-in key and database password | 6 secret versions, 10,000 reads. Setup keeps one version of each |
| **Cloud Storage** | The CMS's pages, if you put them in a bucket | 5 GB in `us-central1`, `us-east1` or `us-west1` |

The database isn't Google's: Google's own (Cloud SQL) has no free tier and
costs about $10/month. Instead, a free PostgreSQL from **[Neon](https://neon.tech)**
(or [Supabase](https://supabase.com)) works fine for a small site. You can
switch to Cloud SQL later.

What "small" means: the app stops when nobody visits and starts again on the
next visit, so it uses almost nothing while idle. **The first visit after a
quiet spell waits a few seconds** while it starts. It's capped at 2 copies
running at once, which caps the bill too.

Two things to know before you start:

- **Google needs a billing account (a card) even for the free tier.** New
  accounts get a free trial with credit, and a trial account isn't charged
  unless you choose to upgrade it.
- **Set a budget alert** (step 1): an email if anything ever starts to cost
  money. It warns you; it doesn't stop anything.

## Step by step

You need a Google account, and a GitHub repository with this code in it
(on GitHub, **Use this template**, or a copy you've pushed).

### 1. A Google Cloud project

1. Go to <https://console.cloud.google.com/projectcreate>, give the project a
   name, and create it. Note its **project ID** (shown under the name, like
   `my-site-123456`).
2. Billing: <https://console.cloud.google.com/billing>. Create a billing
   account (or start the free trial) and link it to the project.
3. A budget alert: <https://console.cloud.google.com/billing/budgets> →
   **Create budget**, for this project, amount **$1**, with the email alerts
   it suggests.

### 2. A free database

1. Sign up at <https://neon.tech> and create a project. Pick the region
   nearest your Google region (`us-central1` is Iowa: a US East region is close).
2. On the project's dashboard, press **Connect**, turn **Connection pooling
   off**, and copy the connection string. It looks like
   `postgresql://user:password@ep-....neon.tech/neondb?sslmode=require`.

That string is a password: you'll paste it once, in step 3, and it's kept in
Google's Secret Manager after that.

### 3. Set it up, from Cloud Shell

**Cloud Shell** is a terminal in your browser, with Google's tools already
logged in as you: <https://shell.cloud.google.com>. Paste these lines, with
your repository and project ID:

```bash
git clone https://github.com/YOUR-NAME/YOUR-SITE.git
cd YOUR-SITE
gcloud config set project YOUR-PROJECT-ID
./scripts/cloud/gcp.sh setup
```

- A **private** repository? GitHub asks for a username and password; the
  password is a token: <https://github.com/settings/personal-access-tokens> →
  *Generate new token*, only this repository, **Contents: Read-only**.
- When it asks for the **connection string**, paste the one from step 2. It
  isn't shown as you paste.

It turns on the services it needs, creates an image repository, a service
account for the app (allowed to read its own secrets and nothing else), and
the secrets, then builds the site and deploys it. The first run takes 5-10
minutes. At the end it prints your address, which ends in
`.run.app`.

### 4. Deploy every push from GitHub

Still in Cloud Shell:

```bash
./scripts/cloud/gcp.sh github
```

This lets **this repository's** GitHub Actions deploy, and nothing else. No
key or password is created: GitHub proves to Google which repository a run
belongs to (*Workload Identity Federation*).

It ends by printing four values, and a link to your repository's
**Settings → Secrets and variables → Actions → Variables**. Add each as a
**repository variable** (they're not secret): `GCP_PROJECT`, `GCP_REGION`,
`GCP_WORKLOAD_IDENTITY_PROVIDER`, `GCP_SERVICE_ACCOUNT`.

From now on, each push to `main` runs the **Tests** workflow, then **Deploy
to Google Cloud**, which builds the new version, rolls it out, and checks
`/api/health`. To deploy without a push: your repository's **Actions** tab →
*Deploy to Google Cloud* → **Run workflow**.

### 5. Check it

Open the address. Sign up, add a note. (Or let a script do that:
`./scripts/smoke.sh https://your-address`. It creates a test account.)

## Working from Claude Code on the web

Once steps 1-4 are done, everything else can happen from Claude Code on the
web, without a development computer:

| You ask | What happens |
|---|---|
| "Add a page that...", "fix..." | Claude changes the code, and pushes a branch. The **Tests** workflow runs on it; Claude can read the result, with `gh` or the GitHub tools. That's how it checks its work when its environment can't run Docker |
| "Merge it" / "push to main" | Tested again, then deployed. Claude can follow the deploy in the Actions tab the same way |
| "Change the site's name", "turn on the CMS" | A change to `cloud.env` ([below](#changing-settings)); the next deploy applies it |
| "Is the site up?" | Claude asks `/api/health`, if its environment's network policy lets it reach your `run.app` address (see [Claude Code on the web](https://code.claude.com/docs/en/claude-code-on-the-web) for network settings) |

What stays with you, because it needs your Google login: steps 1-4, and the
few things that change **secrets** or **permissions** (the database, a new
bucket). Those are `./scripts/cloud/gcp.sh setup` again, in Cloud Shell.
Claude never needs your Google account or any key.

(Claude Code could run `gcloud` itself if you signed it in, but then its
environment would hold access to your whole Google project. Cloud Shell
keeps that in your own browser.)

## Changing settings

| What | Where | Then |
|---|---|---|
| The site's name, Google sign-in's client id, the CMS | `cloud.env`, committed. Public settings only: the scripts refuse to run with a secret in it | Push. The deploy applies it |
| The database | Cloud Shell: `GCP_DATABASE_URL='postgres://...' ./scripts/cloud/gcp.sh setup` | Applies at once |
| Cloud SQL instead of Neon (about $10/month) | Cloud Shell: `GCP_CLOUD_SQL=true ./scripts/cloud/gcp.sh setup`. Starts with an empty database | Applies at once |
| Always on, bigger, with Cloud SQL (about $35-45/month) | `GCP_TIER=standard` in `cloud.env`, then `setup` in Cloud Shell: one copy always running (no cold starts), up to 10, 1 GB | Applies at once |
| Region | `GCP_REGION=...` for `setup` and in the GitHub variable. Changing it later means a new service: set it up fresh | |

Google sign-in also needs your `run.app` address among the client's allowed
origins ([GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)).

## The CMS in a Cloud Storage bucket

The [CMS](CMS.md)'s pages can come with the code (`content/`, deployed
with each push: nothing to set up), or from a bucket, so they can change
without a deploy:

1. Build the app with that module: `./scripts/module.sh add gcs`
   ([MODULES.md](MODULES.md)), and commit.
2. In `cloud.env`: `CMS_ENABLED=true`, `CMS_SOURCE=gcs`, and
   `CMS_GCS_BUCKET=` a name no one else in the world has used, e.g.
   `my-site-123456-content`. Commit and push.
3. Once, in Cloud Shell (after `git pull`): `./scripts/cloud/gcp.sh setup`.
   It creates the bucket in your region and lets the app read it.
4. Upload the pages: `gcloud storage rsync content gs://YOUR-BUCKET --recursive`.
   Changes show on the site within a minute.

## Removing it

`./scripts/cloud/gcp.sh destroy` in Cloud Shell deletes the service, its
images, secrets and service accounts, and the GitHub connection. It doesn't
touch a CMS bucket or your Neon database: delete those yourself. To be sure
nothing is left, delete the whole project:
<https://console.cloud.google.com/iam-admin/settings> → **Shut down**.

## If something goes wrong

| Symptom | Fix |
|---|---|
| `setup` stops at "allow unauthenticated" or "allUsers" | Your Google account belongs to an organization whose policy forbids public services. Use a project outside it, or ask its admin |
| The deploy workflow says *Permission denied* or *Unable to acquire impersonated credentials* | The repository's name or variables don't match. Run `./scripts/cloud/gcp.sh github` again and compare the four values |
| The deploy never starts | The Tests workflow must pass on `main` first, and the four variables must be set. Actions → *Deploy to Google Cloud* shows whether it was skipped |
| "doesn't answer" after a deploy | `./scripts/cloud/gcp.sh logs` shows the app's own error, often a database string that's wrong or has pooling on |
| The site is slow on the first visit | It was idle and is starting up: a few seconds, then normal |

More in [TROUBLESHOOTING.md](TROUBLESHOOTING.md). For Cloud SQL, your own
domain, and the other clouds, see [CLOUD.md](CLOUD.md#google-cloud).
