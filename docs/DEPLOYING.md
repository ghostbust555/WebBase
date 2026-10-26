# Deploying on your own computer

"Production" is the copy of the site real people use. This page runs it on
the same computer you develop on (or any always-on Linux machine: a home
server, a mini PC, a cloud VM). To make it public on your domain, follow
[CLOUDFLARE_TUNNEL.md](CLOUDFLARE_TUNNEL.md) afterwards. To run it on a cloud
service instead, see [CLOUD.md](CLOUD.md).

## First release

```bash
./setup.sh      # only if you haven't already: creates the passwords prod needs
./deploy.sh
```

`./deploy.sh` builds a **release** (a Docker image holding the compiled API
and the built website), starts it with its own database (a container, unless
you [use a managed one](#a-managed-database)), waits until it reports
healthy, and tells you where it is:

```
 ✓ Prod is running 20261025-1830-a1b2c3d
    http://localhost:8080   (this computer)
```

The name is the date, time and git commit it was built from, with `-dirty`
added if you had uncommitted changes. Commit first, so every release can be
traced back to the exact code it came from.

Check it: `./scripts/smoke.sh http://localhost:8080`.

Production is completely separate from development: its own database (empty
at first), its own port, no banner. Accounts you made in development don't
exist here.

## Releasing again

Make changes, test them in development, commit, then:

```bash
./deploy.sh
```

Database migrations run automatically as the new release starts. If you
use the [CMS](CMS.md), the `content/` folder goes into the release too. If the new
release doesn't come up healthy within 90 seconds, `deploy.sh` **switches back
to the previous release by itself** and shows you the error.

## A managed database

By default production's database is a PostgreSQL container on this
computer, with its data in `.docker-data/prod-postgres`. That's free and
simple. But your data then lives on one disk, and backups, testing them and
updates are all up to you.

**A managed PostgreSQL** does those for you: daily backups, restore to a
point in time, security updates, often a standby copy. For a site whose data
matters, it's the better choice, even when the app itself runs here. Any of
these work: [Neon](https://neon.tech) or [Supabase](https://supabase.com)
(free tiers), Azure Database for PostgreSQL, Amazon RDS, Google Cloud SQL,
or your server provider's managed databases (DigitalOcean, Hetzner, Linode...).

1. Create the database with the provider, and copy its **connection string**.
   It looks like `postgres://user:password@host:5432/dbname?sslmode=require`.
   Allow connections from this computer if the provider has a firewall
   setting (its *Networking* or *Connections* page).
2. In `.env.local`, set:
   ```
   PROD_DATABASE_URL=postgres://user:password@host:5432/dbname?sslmode=require
   ```
3. `./deploy.sh restart`

From then on `./deploy.sh` runs no database container, and the app creates
its tables in the managed database on first start. `./scripts/backup.sh` and
`restore.sh` work on it too. Treat their backups as an extra copy: the
provider's own backups are the main ones.

**Moving existing data across**: before step 2, `./scripts/backup.sh`. After
step 2, `./scripts/restore.sh backups/<that file>`, then step 3. The old
container's data stays in `.docker-data/` until you delete it, so emptying
`PROD_DATABASE_URL` again switches back.

## Undoing a release

```bash
./deploy.sh rollback
```

This switches to the release before. Run it again to switch forward.
`./deploy.sh status` shows what's running.

**A rollback doesn't undo database changes.** If the bad release added a
column, the column stays. That's usually harmless, because older code ignores
columns it doesn't know about. Renaming or deleting a column is the dangerous
kind of change: do it in two releases (first stop using it, then remove it),
so the release before still works.

## Watching it

| Command | What it does |
|---|---|
| `./deploy.sh status` | which release is live, which one rollback would go to |
| `./deploy.sh logs` | the app's output, live (Ctrl+C stops watching, not the app) |
| `./deploy.sh restart` | restart after changing `.env.local` |
| `./deploy.sh stop` | stop production (and the tunnel). Data is kept |

Production starts again by itself after the computer reboots, as long as
Docker starts at login (Docker Desktop: Settings → General → "Start Docker
Desktop when you sign in"; Linux: `sudo systemctl enable docker`).

## Backups

```bash
./scripts/backup.sh            # → backups/webbase-prod-<date>.sql.gz
```

**Copy backups off this computer.** A backup on the same disk is lost along
with the disk. Copy them to cloud storage, another machine, or a USB drive.
(Or [use a managed database](#a-managed-database), which keeps its own.)

A backup that fails part-way (the database stopped, a network error) leaves
no file behind, and `restore.sh` refuses any file that isn't a complete backup.

To back up every night at 3am, run `crontab -e` and add:

```
0 3 * * * /full/path/to/your/project/scripts/backup.sh >> /full/path/to/your/project/backups/cron.log 2>&1
```

Restoring replaces everything in the database with the backup's contents. It
takes a safety backup of the current state first:

```bash
./scripts/restore.sh backups/webbase-prod-20261025-030000.sql.gz
```

Test a restore occasionally, into development: `./scripts/restore.sh <file> dev`.

## The passwords

`./setup.sh` generated three secrets in `.env.local` (with a [managed
database](#a-managed-database), only `JWT_KEY` is used: the database's
password is in `PROD_DATABASE_URL`):

- `JWT_KEY` signs sign-in tokens. Changing it signs everybody out, but is
  otherwise harmless.
- `POSTGRES_PASSWORD` and `POSTGRES_APP_PASSWORD` are the database's two
  users. **The database records these the first time it starts.** Changing
  them in `.env.local` afterwards doesn't change them in the database: the
  app is locked out until they match again. To really change one, change it
  inside the database first (`./scripts/backup.sh`, then ask for help or see
  the PostgreSQL `ALTER ROLE` docs).

Keep a copy of `.env.local` in a password manager. Without it, a backup
restored onto a new machine can't be opened by the app until the passwords
are reset.

## Running it on a separate server

Any always-on Linux machine with Docker works the same way: a cloud VM
(DigitalOcean, Hetzner, Lightsail, an Azure/AWS/Google VM), a home server, a
Raspberry Pi 5.

```bash
# on the server
git clone <your repository> site && cd site
./setup.sh
./deploy.sh
./tunnel.sh on      # after following CLOUDFLARE_TUNNEL.md
```

To release a new version: `git pull && ./deploy.sh` on the server. Copy
`.env.local` from your own machine first if you want the same secrets (and
the same Google client ID).
