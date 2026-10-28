# Amazon Web Services

Your site on AWS, with HTTPS and an address included, from one script.
**All you need is a browser**: AWS CloudShell (the `>_` button at the top of
the AWS console) has the tools and Docker, already signed in.

Two ways to run it, and the free one is a different shape from the other
clouds, so read [Free or standard](#free-or-standard) first. This page covers:

- [What's free, and what isn't](#whats-free-and-what-isnt), [free or standard](#free-or-standard)
- [Step by step](#step-by-step)
- [Files for the CMS](#files-for-the-cms-in-s3), [changing settings](#changing-settings)
- [Releasing changes](#releasing-changes), [removing it](#removing-it), [if something goes wrong](#if-something-goes-wrong)

## What's free, and what isn't

AWS changed its free tier in July 2025: a new account gets **credits** (up to
$200, for 6 months) instead of a year of free services, and a few services are
**always free** (check [the current list](https://aws.amazon.com/free/)).
What matters here:

| Service | Used for | Free? |
|---|---|---|
| **Lambda** | The website and API (free setup) | **Always free**: 1 million requests and 400,000 GB-seconds a month. Includes the public address |
| **RDS PostgreSQL** | The data | **Not free after the credits.** About $15/month for the smallest. The free setup uses a [Neon](https://neon.tech) database instead |
| **ECS Express Mode** | The website (standard setup) | **Not free**: always-on containers and a load balancer, about $25-40/month |
| **ECR** (images), **Parameter Store** (secrets), **S3** (CMS pages) | | Cents a month, at this size |

So the free setup costs **about nothing**: Lambda is free, the images and
secrets are cents, and the database is a free Neon one. AWS needs a card even
for the free tier. **Set a budget alert** (below).

## Free or standard

`AWS_TIER` (default `free`):

| | `free` | `standard` |
|---|---|---|
| Runs on | AWS Lambda, as the same container | ECS Express Mode (containers + load balancer) |
| Database | **yours**: a free Neon one, `AWS_DATABASE_URL` | RDS PostgreSQL, created for you (private, encrypted, daily backups); or yours |
| Idle | Sleeps; the first visit after a quiet spell takes a few seconds | Always on |
| Roughly | free | $40-70/month |

How the free one works: the container gets a small extra program, AWS Lambda
Web Adapter (added by `ops/Dockerfile.lambda`), which
turns Lambda's calls into ordinary web requests to the app. Same app, same
image otherwise. Lambda can't reach a private RDS database without a paid
network gateway, so the free setup uses a database over the internet (Neon,
encrypted, with a password).

Choose before the first `setup` (`AWS_TIER=standard` in `cloud.env`). To
switch later, run `destroy` and `setup` again.

## Step by step

You need an AWS account (<https://aws.amazon.com/free>), a GitHub repository
with this code in it (**Use this template**, or a copy you've pushed), and for
the free setup a database:

1. **A database** (free setup): sign up at <https://neon.tech>, create a
   project, press **Connect**, turn **Connection pooling off**, and copy the
   connection string (`postgresql://user:password@ep-....neon.tech/neondb?sslmode=require`).
   You'll paste it once; it is kept in AWS Parameter Store (encrypted) after that.
2. **A budget alert**: <https://console.aws.amazon.com/billing/home#/budgets>
   → *Create budget* → *Zero spend budget* (an email the moment anything costs money).
3. **Open CloudShell**: the `>_` button in the AWS console's top bar. Pick the
   region you want first (top right), e.g. *N. Virginia*.
4. **Get the code and run setup**:
   ```bash
   git clone https://github.com/YOUR-NAME/YOUR-SITE.git
   cd YOUR-SITE
   AWS_REGION=us-east-1 ./scripts/cloud/aws.sh setup
   ```
   It asks for the connection string. (A **private** repository? GitHub asks
   for a username and a password: the password is a read-only token for this
   repository, [the token, step by step](CLOUD.md#a-private-repository-the-github-token).)
5. **Wait**: 5-10 minutes for the free setup (building the image), 15-25 for
   standard (the database and load balancer). It prints your address.
6. **Check it**: open the address and **sign up straight away**: the first account is an author ([AUTHORS.md](AUTHORS.md#sign-up-right-after-you-deploy)). Try **Pages** → **New page**.

What setup made, and how it's linked (free setup first):

| Piece | How it's linked |
|---|---|
| The **function** (Lambda) | Runs as its own IAM role, allowed only to write its logs (and read the CMS bucket, if any) |
| The **address** | A Lambda function URL: public, as a website must be |
| The **image** (ECR) | A private repository keeping the last 3 releases |
| The **secrets** | The sign-in key (made once) and the database address, encrypted in Parameter Store under `/webbase/`. Lambda receives them as its settings; people with permission to read the function's configuration can see them |
| **Standard**: the **service** | ECS Express Mode with an execution role (reads only `/webbase/*` secrets) and a task role (reads only the CMS bucket) |
| **Standard**: the **database** | RDS in your default VPC, **not reachable from the internet**; its firewall lets in PostgreSQL from inside that network only |
| **S3** (only with the CMS) | A private bucket (all public access blocked); only the app's role may read it |

You can run `setup` again at any time: it changes only what differs.

## Files for the CMS, in S3

The [CMS](CMS.md)'s pages can come with the code (`content/`, deployed with
each release: nothing to set up), or from a bucket, so they can change
without a release:

1. In `cloud.env`: `CMS_ENABLED=true`, `CMS_SOURCE=s3`, and `CMS_S3_BUCKET=`
   a name no one else in the world has used, e.g. `my-site-123456-content`.
   Commit and push.
2. In CloudShell: `git pull`, then `./scripts/cloud/aws.sh setup`. It makes the
   private bucket, lets the app read it, and builds the app with the S3 module
   ([MODULES.md](MODULES.md)): no code change.
3. Upload the pages: `aws s3 sync content s3://YOUR-BUCKET --delete`. Changes
   show on the site within a minute.

## Floods of requests

The app bans an address that sends too many requests, and caps the total. On
the free setup, at most 20 copies of the function run at once
(`AWS_MAX_CONCURRENCY`; a brand-new account may not be allowed to set this yet,
and the script says so), and the standard service runs at most 3 containers. Details and
limits: [CLOUD.md](CLOUD.md#floods-of-requests-and-your-bill). The "zero spend"
budget in step 2 is your early warning.

## Changing settings

| What | Where | Then |
|---|---|---|
| The site's name, Google sign-in's client id, the CMS | `cloud.env`, committed. Public settings only: the scripts refuse to run with a secret in it | Push, then `./scripts/cloud/aws.sh deploy` |
| The database | CloudShell: `AWS_DATABASE_URL='postgres://...' ./scripts/cloud/aws.sh setup` | Applies at once |
| Region, tier, database size | `AWS_REGION`, `AWS_TIER`, `AWS_DB_CLASS` in `cloud.env`, before the first setup | |

Put `AWS_REGION` in `cloud.env` if you don't want the default (`us-east-1`),
so a deploy from anywhere finds the same place. Google sign-in also needs your
address among the client's allowed origins ([GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)).

## Releasing changes

In CloudShell: `cd YOUR-SITE && git pull && ./scripts/cloud/aws.sh deploy`.
It builds the new version, rolls it out, and checks `/api/health`.

If Claude Code on the web is changing your code, it can't run this for you:
the script needs your AWS login, and giving a coding assistant that is a
bigger step than it looks. The split that works: Claude changes code and
pushes (its tests run in GitHub), and you run `deploy` in CloudShell. (The
[Google Cloud](GOOGLE_CLOUD.md) and [Azure](AZURE.md#releasing-changes) setups
can deploy by themselves on every push.)

## Removing it

`./scripts/cloud/aws.sh destroy` deletes the function or service, the image
repository and the secrets. With the standard setup it also deletes the RDS
database, after keeping a final snapshot (delete that in the RDS console to
stop paying for it). A Neon database and a CMS bucket are left alone. The IAM
roles stay; they cost nothing.

## If something goes wrong

| Symptom | Fix |
|---|---|
| `setup` says *AccessDenied* | The account or role you're signed in as needs permission to create IAM roles, Lambda, ECR and so on. The account's root user or an administrator has it |
| *The role defined for the function cannot be assumed* | A new role needs a minute to spread: run `setup` again |
| `Building needs Docker` | CloudShell has Docker in most regions; if yours doesn't, use a computer with Docker and `aws configure` |
| `doesn't answer` after a deploy | `./scripts/cloud/aws.sh logs` shows the app's own error, often a wrong database string or one with connection pooling on |
| The first visit is slow (free) | The function was idle and is starting: a few seconds. `AWS_TIER=standard` keeps it awake |
| `aws ecs create-express-gateway-service` isn't a command | The standard setup needs a recent AWS CLI (CloudShell's is). Update it: <https://aws.amazon.com/cli/> |
| A 502 or timeout on a big page (free) | Lambda answers within 30 seconds, with responses up to 6 MB |

More in [TROUBLESHOOTING.md](TROUBLESHOOTING.md). Your own domain (standard):
a certificate in AWS Certificate Manager on the load balancer, or put
Cloudflare in front. Other clouds: [CLOUD.md](CLOUD.md).
