# Deploying to a cloud

Production is **one container image plus a PostgreSQL database**, which is
about the most portable shape a web app can have. Every container host can
run it. This page covers:

- [The database: use a managed one](#the-database-use-a-managed-one)
- [The CMS content](#the-cms-content), if you use it
- [What every host needs](#what-every-host-needs), which is the whole story if your host isn't listed
- Scripted: [Azure](#microsoft-azure), [AWS](#amazon-web-services-aws), [Google Cloud](#google-cloud) (free tier, browser only: [GOOGLE_CLOUD.md](GOOGLE_CLOUD.md))
- Click-through: [Render](#render), [Railway](#railway), [Fly.io](#flyio), [DigitalOcean](#digitalocean-app-platform)
- [Any Linux server](#any-linux-server-vps), the same as running at home

**Which should I pick?** For a first site: **Google Cloud Run**, which is free
for a small site and needs only a browser to set up ([GOOGLE_CLOUD.md](GOOGLE_CLOUD.md)).
Render or Railway (both click-through, with a database included) or a VPS
(about $5/month, plus a managed database once the data matters) are simple too.
Pick Azure or AWS when you already use one of them, need its other
services, or expect to need to scale. Their scripts here do the setup
in one command, but each platform has more parts to understand and its own
billing to watch.

> **Cost**: everything below costs money once free tiers run out, mostly for
> the database, which runs all the time. Each section gives a rough idea.
> Check the provider's current pricing, and **set a budget alert** in its
> billing settings before you start.

---

## The database: use a managed one

In a cloud, **the app runs in a container, and the data lives in the cloud's
managed PostgreSQL**, never in a database container next to the app.

- Cloud container hosts replace containers on every release, and move them
  whenever they like. A database inside one would lose its data.
- A managed database takes daily backups, can restore to a point in time,
  applies security updates, and can keep a standby copy. You get all of that
  without running any of it.

Every option on this page does it that way:

| Host | Managed PostgreSQL | Set up by |
|---|---|---|
| Azure | Azure Database for PostgreSQL (Flexible Server) | `azure.sh setup` |
| AWS | Amazon RDS for PostgreSQL | `aws.sh setup` |
| Google Cloud | Cloud SQL for PostgreSQL (or a free Neon database, [GOOGLE_CLOUD.md](GOOGLE_CLOUD.md)) | `gcp.sh setup` |
| Render, Railway, DigitalOcean, Fly.io | the host's own PostgreSQL | a few clicks, below |
| Anything | [Neon](https://neon.tech), [Supabase](https://supabase.com) (both have free tiers) | paste its connection string as `DATABASE_URL` |

The app doesn't care which. It needs one connection string (`DATABASE_URL`),
and it creates its own tables on first start. Each script can also use a
database you already have instead of creating one: `AZURE_DATABASE_URL`,
`AWS_DATABASE_URL`, `GCP_DATABASE_URL`.

Prefer a different kind of database, such as Azure Cosmos DB? The app only
reaches its data through swappable *stores*, see [DATABASE.md](DATABASE.md).

## The CMS content

If you use the [CMS](CMS.md), its pages are built into the image from
`content/` by default, and published with each deploy. To publish pages
without a release, put them in the cloud's storage instead: an **S3 bucket**
(AWS, or an S3-compatible one such as Cloudflare R2), an **Azure Blob
container** or a **Google Cloud Storage bucket** ([CMS.md](CMS.md#where-the-content-lives)).
Each is a module the app has to be built with first: `./scripts/module.sh add s3`
(or `azure`, `gcs`), see [MODULES.md](MODULES.md).

The scripts below pass `CMS_ENABLED`, `CMS_SOURCE` and the bucket settings
(from `.env.local`, or `cloud.env`) on to the app. `gcp.sh setup` also creates
the Google bucket and lets the app read it. On AWS and Azure, give the app
access once yourself ([CMS.md](CMS.md#in-a-cloud)): the ECS task role, or the
container app's managed identity. The scripts never copy an access key or
connection string to the cloud; they warn if `.env.local` has one, since in
the cloud it belongs in the host's secrets.

---

## What every host needs

| | |
|---|---|
| **Image** | built from `ops/Dockerfile`, with the repository root as the build context |
| **Port** | the container listens on **8080**, or on `$PORT` if the host sets it (Render, Railway, Cloud Run, Heroku do) |
| **Health check** | `GET /api/health` returns 200 when the app and its database are fine |
| **Database** | any PostgreSQL 14 or newer: the host's managed one ([above](#the-database-use-a-managed-one)) |
| **Instances** | one is plenty to start. More is fine too: they share nothing but the database |

Environment variables (set as **secrets** where the host offers it):

| Variable | Value |
|---|---|
| `App__Environment` | `prod` |
| `Jwt__Key` | a random string of 64 characters. Use the `JWT_KEY` from your `.env.local`, or make one: `openssl rand -base64 48` |
| `DATABASE_URL` | `postgres://user:password@host:5432/dbname?sslmode=require`. Most hosts give you this exact string. Or set `ConnectionStrings__Default` to a .NET connection string instead |
| `App__Name` | your site's name (optional) |
| `Auth__Google__ClientId` | for Google sign-in (optional). Add the cloud address to the client's allowed origins ([GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)) |
| `Cms__Enabled`, `Cms__Source`, `Cms__S3__*`, `Cms__Azure__*` | the CMS (optional), and where its content is ([above](#the-cms-content)) |

The app creates and updates its own tables when it starts, so an empty
database is all it needs.

To upload the image to a registry yourself (Docker Hub, GitHub, or a host
that takes a pre-built image), first `docker login <registry>`, then:

```bash
./scripts/cloud/push-image.sh ghcr.io/yourname/yoursite
```

This always builds for `linux/amd64`, the kind of machine clouds run, even on
an Apple Silicon Mac.

**After any cloud deploy**, check it: `./scripts/smoke.sh https://<the address it gave you>`.

---

## Microsoft Azure

**Azure Container Apps** runs the container, with HTTPS and an address
included. **Azure Database for PostgreSQL** (Flexible Server) holds the data.
Roughly $15-30/month at the smallest sizes. The app can scale to zero, but
the database doesn't.

1. Install the Azure CLI: <https://aka.ms/installazurecli>
2. `az login`
3. `./scripts/cloud/azure.sh setup`

Setup takes 10-15 minutes, most of it creating the database server. It
creates one **resource group** (a folder holding everything), named
`webbase-rg`, containing the image registry, the database, and the app. At
the end it prints your address: `https://webbase.<something>.azurecontainerapps.io`.

| Later | |
|---|---|
| Release a new version | `./scripts/cloud/azure.sh deploy` |
| The address | `./scripts/cloud/azure.sh url` |
| Live logs | `./scripts/cloud/azure.sh logs` |
| Delete everything (data too) | `./scripts/cloud/azure.sh destroy` |

Options, as lines in `.env.local`: `AZURE_LOCATION=westeurope` (the default is
`eastus`), `AZURE_RESOURCE_GROUP=...`, and `AZURE_DATABASE_URL=...` to use a
database you already have instead of creating one. The database password it
generates is saved in `.env.local` as `AZURE_DB_PASSWORD`.

**Your own domain**: in the Azure portal, open the Container App → *Custom
domains* → *Add*, and follow its instructions for the DNS record. It issues a
certificate for free.

---

## Amazon Web Services (AWS)

**Amazon ECS Express Mode** runs the container. One command creates the
service, an HTTPS load balancer, an address and auto-scaling. (AWS App
Runner, the older option, stopped taking new customers in 2026.) **Amazon
RDS for PostgreSQL** holds the data. Roughly $20-40/month for the app, much
of it the load balancer, plus about $15/month for the smallest database.

### The database

`aws.sh setup` creates an RDS PostgreSQL instance for you (`webbase-db`,
size `db.t4g.micro`, daily backups kept for 7 days, encrypted). It sits in
your account's **default VPC** (the network ECS Express Mode runs the app
in), and is reachable only from inside it, never from the internet. Its
password is saved in `.env.local` as `AWS_DB_PASSWORD`.

To use a database you already have instead, put its address in `.env.local`
before setup, and none is created:

```
AWS_DATABASE_URL=postgres://user:password@host:5432/dbname?sslmode=require
```

That can be another RDS instance (allow PostgreSQL, port 5432, from your
default VPC in its security group), or a hosted Postgres outside AWS like
[Neon](https://neon.tech) or [Supabase](https://supabase.com).

### Deploy

1. Install the AWS CLI v2 (<https://aws.amazon.com/cli/>). It must be recent
   enough to know `aws ecs create-express-gateway-service`; update it if not.
2. `aws configure` (or `aws sso login` if your organisation uses SSO)
3. Optionally, in `.env.local`: `AWS_REGION=...` (the default is `us-east-1`),
   `AWS_DATABASE_URL=...` (above), `AWS_DB_CLASS=...` for a bigger database.
4. `./scripts/cloud/aws.sh setup`

It creates the database (5-15 minutes, the slowest step), stores the secrets
in SSM Parameter Store (encrypted), creates two IAM roles, uploads the image
to ECR and creates the service. It prints an address like
`https://webbase-xxxx.ecs.us-east-1.on.aws`.

| Later | |
|---|---|
| Release a new version | `./scripts/cloud/aws.sh deploy`. AWS shifts traffic gradually and rolls back by itself if the new version fails |
| The address | `./scripts/cloud/aws.sh url` |
| Logs | AWS console → CloudWatch → Log groups |
| Delete it | `./scripts/cloud/aws.sh destroy`. The RDS database it created goes too, after a final snapshot is taken. A database you brought (`AWS_DATABASE_URL`) is left alone |

**Your own domain**: request a certificate in AWS Certificate Manager, add it
to the load balancer's HTTPS listener, and point a DNS record at the load
balancer. Or put Cloudflare in front: a proxied CNAME to the `on.aws` address.

---

## Google Cloud

**Cloud Run** runs the container. It scales to zero when nobody visits, HTTPS
and an address are included, and a small site fits in its **free tier**.

**The full guide is [GOOGLE_CLOUD.md](GOOGLE_CLOUD.md)**: browser only (Google
Cloud Shell, no installs), a free Neon database, and GitHub Actions deploying
every push to `main` without any key. In short:

```bash
# in Cloud Shell (https://shell.cloud.google.com), in a clone of your repository
gcloud config set project <your-project-id>
./scripts/cloud/gcp.sh setup     # asks for the database's connection string
./scripts/cloud/gcp.sh github    # then add the four variables it prints to GitHub
```

| Later | |
|---|---|
| Release a new version | Push to `main` (tested, then deployed), or `./scripts/cloud/gcp.sh deploy` where Docker runs |
| Change a setting | `cloud.env` (public settings), then push; secrets: `setup` again |
| The address | `./scripts/cloud/gcp.sh url` |
| Recent logs | `./scripts/cloud/gcp.sh logs` |
| Delete it | `./scripts/cloud/gcp.sh destroy` |

**Cloud SQL** (Google's own PostgreSQL, about $10/month, not free):
`GCP_CLOUD_SQL=true ./scripts/cloud/gcp.sh setup` creates one (5-10 minutes)
and connects the service to it.

**Your own domain**: Cloud Run → your service → *Integrations / Custom
domains*, or put Cloudflare in front.

---

## Render

Click-through, with the database included. The smallest web service plus
Postgres is roughly $7-15/month. There are free tiers, but the free database
expires.

1. Push this repository to GitHub.
2. On <https://render.com>: **New → Postgres** (Render's managed PostgreSQL,
   with daily backups on paid plans). Pick a name and the smallest paid plan,
   and create it. Copy its **Internal Database URL**.
3. **New → Web Service** → connect the repository. Then:
   - Language: **Docker**
   - Dockerfile path: `./ops/Dockerfile`, Docker build context: `.`
   - Health check path: `/api/health`
   - Environment variables: `App__Environment` = `prod`, `Jwt__Key` = (64
     random characters), `DATABASE_URL` = the internal URL from step 2
4. Create. Render builds and deploys on every push to the main branch.

## Railway

1. On <https://railway.com>: **New Project → Deploy from GitHub repo**, and pick this repository.
2. In the same project: **New → Database → PostgreSQL**. Railway runs it for
   you, but check its **Backups** tab and turn scheduled backups on. For data
   that matters, a managed provider like Neon also works here: use its URL as
   `DATABASE_URL` instead.
3. Open the app service → **Variables** and add:
   - `RAILWAY_DOCKERFILE_PATH` = `ops/Dockerfile`
   - `App__Environment` = `prod`
   - `Jwt__Key` = 64 random characters
   - `DATABASE_URL` = `${{Postgres.DATABASE_URL}}` (Railway fills this in)
4. **Settings → Networking → Generate Domain**. Under **Deploy**, set the
   healthcheck path to `/api/health`.

## Fly.io

1. Install `flyctl` (<https://fly.io/docs/flyctl/install/>), then `fly auth login`.
2. `fly launch --dockerfile ops/Dockerfile --internal-port 8080 --no-deploy`.
   Say no to any databases it offers if you'll bring your own.
3. A database: **Fly Managed Postgres** (`fly mpg create`, see Fly's docs),
   or Neon or Supabase. Avoid the older `fly postgres create`: that's a
   database running in a Fly machine that you look after yourself. Then:
   `fly secrets set DATABASE_URL='postgres://...' Jwt__Key="$(openssl rand -base64 48)" App__Environment=prod`
4. `fly deploy`. Later releases are just `fly deploy`.

## DigitalOcean App Platform

**Create → Apps** → pick the GitHub repository. DigitalOcean detects the
Dockerfile. If it doesn't, set the Dockerfile path to `ops/Dockerfile`. Set the
HTTP port to 8080, add a **Managed Database** (PostgreSQL) component (not a
*Dev Database*, which is meant for trying things out), and set
`DATABASE_URL` to `${db.DATABASE_URL}` along with the other variables from
[the table above](#what-every-host-needs).

---

## Any Linux server (VPS)

The simplest and cheapest option, and it works exactly like your own computer.
Rent a small server (Hetzner, DigitalOcean, Linode, Vultr, Lightsail, or a VM
on any big cloud; 1-2 GB of memory is plenty), install Docker on it
(<https://docs.docker.com/engine/install/>), then follow
[DEPLOYING.md → Running it on a separate server](DEPLOYING.md#running-it-on-a-separate-server):

```bash
git clone <your repository> site && cd site
./setup.sh && ./deploy.sh && ./tunnel.sh on
```

By default the database runs in a container on the same server. That's
cheap, but its backups are your job. Better, and recommended once the site
matters: a **managed PostgreSQL** (your VPS provider's, e.g. DigitalOcean,
Hetzner or Linode managed databases, or Neon / Supabase), with one line in
`.env.local` on the server:

```
PROD_DATABASE_URL=postgres://user:password@host:5432/dbname?sslmode=require
```

`./deploy.sh` then uses it and runs no database container. See
[DEPLOYING.md → A managed database](DEPLOYING.md#a-managed-database). If you
keep the container, **set up backups**
([DEPLOYING.md → Backups](DEPLOYING.md#backups)) and copy them off the server.

---

## After you've moved to a cloud

- Your data stays where it was created. To move production data from your
  own computer to a cloud database, take a backup (`./scripts/backup.sh`),
  set `PROD_DATABASE_URL` in `.env.local` to the cloud database's URL, and
  `./scripts/restore.sh <the backup>`. The cloud database needs to be
  reachable from your computer for that step (Azure and Neon/Supabase allow
  it with a firewall rule; RDS in a VPC doesn't, so load it from inside AWS).
  Then set `PROD_DATABASE_URL` back to empty.
- Keep `JWT_KEY` the same everywhere a given site runs, or everyone gets
  signed out when you switch.
- The scripts call each cloud's own command-line tool, and those tools change
  over time. If a step fails with an "unknown argument" style error, update
  the CLI first, then check that provider's documentation for the step it
  names.
