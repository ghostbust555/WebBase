# Deploying to a cloud

Production is **one container image plus a PostgreSQL database**, which is
about the most portable shape a web app can have. Every container host can
run it. This page covers:

- [What every host needs](#what-every-host-needs), which is the whole story if your host isn't listed
- Scripted: [Azure](#microsoft-azure), [AWS](#amazon-web-services-aws), [Google Cloud](#google-cloud)
- Click-through: [Render](#render), [Railway](#railway), [Fly.io](#flyio), [DigitalOcean](#digitalocean-app-platform)
- [Any Linux server](#any-linux-server-vps), the same as running at home

**Which should I pick?** For a first site, a VPS (about $5/month) or Render or
Railway (both click-through, with the database included) are the simplest.
Pick Azure, AWS or Google Cloud when you already use one of them, need its
other services, or expect to need to scale. Their scripts here do the setup
in one command, but each platform has more parts to understand and its own
billing to watch.

> **Cost**: everything below costs money once free tiers run out, mostly for
> the database, which runs all the time. Each section gives a rough idea.
> Check the provider's current pricing, and **set a budget alert** in its
> billing settings before you start.

---

## What every host needs

| | |
|---|---|
| **Image** | built from `ops/Dockerfile`, with the repository root as the build context |
| **Port** | the container listens on **8080**, or on `$PORT` if the host sets it (Render, Railway, Cloud Run, Heroku do) |
| **Health check** | `GET /api/health` returns 200 when the app and its database are fine |
| **Database** | any PostgreSQL 14 or newer |
| **Instances** | one is plenty to start. More is fine too: they share nothing but the database |

Environment variables (set as **secrets** where the host offers it):

| Variable | Value |
|---|---|
| `App__Environment` | `prod` |
| `Jwt__Key` | a random string of 64 characters. Use the `JWT_KEY` from your `.env.local`, or make one: `openssl rand -base64 48` |
| `DATABASE_URL` | `postgres://user:password@host:5432/dbname?sslmode=require`. Most hosts give you this exact string. Or set `ConnectionStrings__Default` to a .NET connection string instead |
| `App__Name` | your site's name (optional) |
| `Auth__Google__ClientId` | for Google sign-in (optional). Add the cloud address to the client's allowed origins ([GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)) |

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
Runner, the older option, stopped taking new customers in 2026.) Roughly
$20-40/month, much of it the load balancer, plus the database.

### A database on AWS

The script doesn't create the database, because that's the part with the
most choices. Pick one:

- **Easiest**: a hosted Postgres outside AWS, like [Neon](https://neon.tech) or
  [Supabase](https://supabase.com) (both have free tiers). Create a project,
  copy its connection string, and use it as `AWS_DATABASE_URL`.
- **Amazon RDS**: in the AWS console, open **Aurora and RDS → Create database**.
  Pick PostgreSQL, the **Free tier** / smallest template, and set a master
  password. Under *Connectivity*, keep the **default VPC**. Once it's
  available, edit its **security group** to allow inbound PostgreSQL (5432)
  from the security group of your ECS service (create the service first, or
  temporarily allow the default VPC's range, `172.31.0.0/16`). Then:
  `AWS_DATABASE_URL=postgres://postgres:<password>@<endpoint>:5432/postgres?sslmode=require`

### Deploy

1. Install the AWS CLI v2 (<https://aws.amazon.com/cli/>). It must be recent
   enough to know `aws ecs create-express-gateway-service`; update it if not.
2. `aws configure` (or `aws sso login` if your organisation uses SSO)
3. Add `AWS_DATABASE_URL=...` to `.env.local` (optionally `AWS_REGION=...`; the default is `us-east-1`)
4. `./scripts/cloud/aws.sh setup`

It stores the secrets in SSM Parameter Store (encrypted), creates two IAM
roles, uploads the image to ECR and creates the service. It prints an address
like `https://webbase-xxxx.ecs.us-east-1.on.aws`.

| Later | |
|---|---|
| Release a new version | `./scripts/cloud/aws.sh deploy`. AWS shifts traffic gradually and rolls back by itself if the new version fails |
| The address | `./scripts/cloud/aws.sh url` |
| Logs | AWS console → CloudWatch → Log groups |
| Delete it | `./scripts/cloud/aws.sh destroy` (your database is left alone) |

**Your own domain**: request a certificate in AWS Certificate Manager, add it
to the load balancer's HTTPS listener, and point a DNS record at the load
balancer. Or put Cloudflare in front: a proxied CNAME to the `on.aws` address.

---

## Google Cloud

**Cloud Run** runs the container. It scales to zero when nobody visits, and
HTTPS and an address are included. **Cloud SQL** holds the data. Roughly
$10-15/month for the smallest Cloud SQL instance, and the app is often within
Cloud Run's free tier.

1. Install the gcloud CLI: <https://cloud.google.com/sdk/docs/install>
2. `gcloud auth login`, then `gcloud config set project <your-project-id>`.
   The project needs **billing enabled** (console → Billing).
3. `./scripts/cloud/gcp.sh setup`

It turns on the APIs it needs, creates an image repository, a Cloud SQL
instance and database (5-10 minutes), and stores the secrets in Secret
Manager, then deploys. The address looks like `https://webbase-xxxx.a.run.app`.

| Later | |
|---|---|
| Release a new version | `./scripts/cloud/gcp.sh deploy` |
| The address | `./scripts/cloud/gcp.sh url` |
| Recent logs | `./scripts/cloud/gcp.sh logs` |
| Delete everything (data too) | `./scripts/cloud/gcp.sh destroy` |

Options in `.env.local`: `GCP_PROJECT`, `GCP_REGION` (the default is
`us-central1`), and `GCP_DATABASE_URL` to use your own database instead of
Cloud SQL.

**Your own domain**: Cloud Run → your service → *Integrations / Custom
domains*, or put Cloudflare in front.

---

## Render

Click-through, with the database included. The smallest web service plus
Postgres is roughly $7-15/month. There are free tiers, but the free database
expires.

1. Push this repository to GitHub.
2. On <https://render.com>: **New → Postgres**. Pick a name and the smallest
   plan, and create it. Copy its **Internal Database URL**.
3. **New → Web Service** → connect the repository. Then:
   - Language: **Docker**
   - Dockerfile path: `./ops/Dockerfile`, Docker build context: `.`
   - Health check path: `/api/health`
   - Environment variables: `App__Environment` = `prod`, `Jwt__Key` = (64
     random characters), `DATABASE_URL` = the internal URL from step 2
4. Create. Render builds and deploys on every push to the main branch.

## Railway

1. On <https://railway.com>: **New Project → Deploy from GitHub repo**, and pick this repository.
2. In the same project: **New → Database → PostgreSQL**.
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
3. A database: create one with Fly's Postgres offering (see `fly postgres --help`
   / Fly's docs), or use Neon or Supabase. Then:
   `fly secrets set DATABASE_URL='postgres://...' Jwt__Key="$(openssl rand -base64 48)" App__Environment=prod`
4. `fly deploy`. Later releases are just `fly deploy`.

## DigitalOcean App Platform

**Create → Apps** → pick the GitHub repository. DigitalOcean detects the
Dockerfile. If it doesn't, set the Dockerfile path to `ops/Dockerfile`. Set the
HTTP port to 8080, add a **Dev Database** or **Managed Database** (PostgreSQL)
component, and set `DATABASE_URL` to `${db.DATABASE_URL}` along with the
other variables from [the table above](#what-every-host-needs).

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

The database runs on the same server, so **set up backups**
([DEPLOYING.md → Backups](DEPLOYING.md#backups)) and copy them off the server.

---

## After you've moved to a cloud

- Your data stays where it was created. To move production data from your
  own computer to a cloud database, take a backup (`./scripts/backup.sh`) and
  load it into the new database with `psql`:
  `gunzip -c backups/<file>.sql.gz | psql "<the cloud DATABASE_URL>"`.
- Keep `JWT_KEY` the same everywhere a given site runs, or everyone gets
  signed out when you switch.
- The scripts call each cloud's own command-line tool, and those tools change
  over time. If a step fails with an "unknown argument" style error, update
  the CLI first, then check that provider's documentation for the step it
  names.
