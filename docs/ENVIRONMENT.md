# Setting up a machine: with Docker, or without

`./setupenvironment.sh` makes **this computer** able to build, test and run the
project. (`./setup.sh` is different: it names the project and creates its secrets.)
It works with Docker, and also on machines that have **no Docker daemon**: cloud
coding-agent containers (Claude Code on the web and the like), CI runners, a laptop
with Docker Desktop stopped.

```bash
./setupenvironment.sh --yes      # find what's there, install what's missing
./scripts/dev-host.sh up         # (no Docker) the site on :5173, the API on :5000
./scripts/test.sh                # every check
./scripts/smoke.sh http://localhost:5173
```

- [The options](#the-options)
- [What it does](#what-it-does)
- [Without Docker, step by step](#without-docker-step-by-step)
- [When a download is blocked](#when-a-download-is-blocked)
- [Running the site without Docker](#running-the-site-without-docker)
- [Tests without Docker](#tests-without-docker)
- [In a cloud coding agent or CI](#in-a-cloud-coding-agent-or-ci)
- [What this does not cover](#what-this-does-not-cover)

## The options

| Option | Meaning |
|---|---|
| *(none)* | Detect, then install what is missing, asking before each install |
| `--yes` | Don't ask: for CI and agents. Without it, and with nobody at the keyboard, nothing is installed and the summary says what to do |
| `--check` | Only report. Changes nothing (not even a folder), and exits 1 if anything is missing |
| `--docker` | Require Docker: fail if no daemon answers |
| `--no-docker` | Use the computer's own tools even if Docker is there |
| `--with-postgres` / `--without-postgres` | Host mode: install PostgreSQL if it is missing without asking / leave PostgreSQL alone |

It prefers Docker **when a daemon answers `docker info`**. Having the `docker` command
is not enough: the client is often installed with nothing behind it.

**Safe to run again.** It detects what is installed or configured and skips it, so the
second run changes nothing and says so. It never overwrites `.env.local`, an existing
database, or an existing role. At the end it prints what it found, what it installed, what
it skipped, what still needs you, and the exact commands to run next.

## What it does

**With Docker** it checks that the daemon and `docker compose` work, and stops there:
.NET, Node and PostgreSQL run in containers. Next: `./setup.sh`, `./dev.sh`.

**Without Docker** (host mode) it prepares the computer itself, as below, and writes what
it learned to `.env.host`.

## Without Docker, step by step

| Needed | Found if | Otherwise |
|---|---|---|
| **.NET 10 SDK** | `dotnet --list-sdks` has a 10.x | The official install script (`dot.net`) into `~/.dotnet`: no root. If that can't be reached or fails, the distro package `dotnet-sdk-10.0` (Ubuntu 24.04 and newer have it), which needs root |
| `dotnet tool restore`, NuGet restore | `dotnet-ef` at the version in `dotnet-tools.json`, and every project restored | Runs them (needs `api.nuget.org`) |
| **Node** 20.19+ or 22.12+, npm | `node --version` | A download from nodejs.org into `~/.local/share/<project>-env/`, checked against nodejs.org's published SHA-256, no root |
| `npm ci` in `client/` | `client/node_modules` was installed from the current `package-lock.json` | Runs it (needs `registry.npmjs.org`). Skipped when nothing changed, because `npm ci` always deletes and reinstalls |
| **PostgreSQL** 14+ | An installed server of any recent version: `psql`, or the Debian/Homebrew program folders | `apt-get install postgresql` (or `brew install postgresql@17`), which needs root. Then it is started (`pg_ctlcluster`, `systemctl`, `service` or `brew services`) |
| The **development role and database** | The role and database named in `ConnectionStrings__Default` (`webbase` / `webbase` / password `webbase`, on localhost) | Created, using `createdb`: `CREATE DATABASE` can't run inside a multi-statement `psql -c`. An existing role or database is **left alone**, even if it is different |
| A **superuser for the tests** (`TEST_POSTGRES`) | `TEST_POSTGRES` in the environment or `.env.host` names a working superuser connection | Creates the role `webbase_admin` with a random password, kept in `.env.host` |

PostgreSQL needs a superuser to be set up. It tries, in this order: the connection already in
`TEST_POSTGRES`; `PGUSER`/`PGPASSWORD`; your own OS user over the local socket; the `postgres`
OS user (root or passwordless `sudo`; at a keyboard `sudo` may ask for your password). If none
works it says so, and how to make the role and database by hand.

### `.env.host`

What it learned, so the other scripts need no flags: where `dotnet`, Node and PostgreSQL are
(a `PATH` addition, `DOTNET_ROOT`), `DEV_DATABASE` (the connection the development API uses),
and `TEST_POSTGRES` (a superuser connection; **it holds a password**). It is shell syntax:

```bash
export TEST_POSTGRES='Host=localhost;Port=5432;Database=postgres;Username=webbase_admin;Password=...'
```

Every value is single-quoted, because connection strings contain `;`, which a shell would
otherwise take for the end of a command. To use the same tools in your own shell:
`. ./.env.host`. It is not committed (`.gitignore`), the file is mode 600, and the script only
rewrites it when something in it changes. It does not touch your shell profile. The tools it
installed for you are not on your `PATH` outside the project's scripts, unless you source it.

## When a download is blocked

Cloud sandboxes often reach the internet only through a proxy with an allow-list. The script
checks each host before it needs it and **names the one that is blocked**:

| For | Hosts |
|---|---|
| .NET (official script) | `dot.net`, `builds.dotnet.microsoft.com` |
| NuGet packages and `dotnet-ef` | `api.nuget.org` |
| Node | `nodejs.org` |
| npm packages | `registry.npmjs.org` |
| apt packages | the archive named in `/etc/apt/sources.list*` |

If a host is blocked, the .NET step falls back to the distro package, and the message tells you
what to allow.

**It never switches certificate checking off**: no `curl -k`, no `NODE_TLS_REJECT_UNAUTHORIZED=0`,
no `GIT_SSL_NO_VERIFY`. Downloads are checked against the system certificate store. Behind a proxy
that re-signs HTTPS, the fix is to put the proxy's CA certificate **in** that store (Debian/Ubuntu:
copy it to `/usr/local/share/ca-certificates/` and run `update-ca-certificates`). Node and npm use
their own list of certificates, so the script adds the system store to them
(`NODE_EXTRA_CA_CERTS`); that only adds trust the system already has.

## Running the site without Docker

`./scripts/dev-host.sh` is `./dev.sh` for a machine without Docker:

| | |
|---|---|
| `up` | Starts the API with `dotnet watch run` (hot reload) on `:5000` (`DEV_API_PORT`) and Vite on `:5173` (`DEV_WEB_PORT`) in the background, waits for `/api/health`, and prints the addresses. `up --no-watch` uses plain `dotnet run` |
| `status`, `logs [api\|web]`, `stop`, `restart` | As for `./dev.sh`. Logs and process ids are in `.dev-host/` |

It gives the API the same settings `compose.dev.yaml` does (development mode, the CMS and Google
settings from `.env.local`, `Dev__TypeScriptOutput`), with `Host=localhost` for the database, so
`client/src/api-types.ts` is rewritten whenever the C# types change, exactly as in `./dev.sh`. Vite
forwards `/api` to the API. The database is the PostgreSQL on this machine, which
`./setupenvironment.sh` started; if the machine was restarted (a cloud container sleeping), run it
again: it starts PostgreSQL and does nothing else.

`./scripts/ef.sh` and `./scripts/gen-types.sh` work the same way without Docker (they use the
host's `dotnet` and the database in `.env.host`).

## Tests without Docker

`./scripts/test.sh` uses Docker when a daemon answers and the host's tools when none does
(`--host` and `--docker` force one). In host mode:

- The C# tests and the website's type check and build run as usual.
- The **PostgreSQL module tests run** when `TEST_POSTGRES` is set (`.env.host` sets it).
  Without it they show as skipped.
- The **S3, Azure Blob and Google Cloud Storage module tests are skipped**: their stand-ins
  (S3Mock, Azurite, fake-gcs-server) are Docker containers. They run on every push in GitHub
  ([the workflow](../.github/workflows/tests.yml)).
- Cosmos DB's tests need its emulator in either mode ([its README](../modules/WebBase.Database.CosmosDB/README.md)).

## In a cloud coding agent or CI

Put `./setupenvironment.sh --yes` in the environment's setup script. Because it is safe to repeat,
it can also run at the start of every session: it restores PostgreSQL after the container slept, and
otherwise says everything is already there. In CI that has Docker (GitHub's runners do),
`./scripts/test.sh` needs no setup at all.

Agents can't normally use `sudo` with a password, so PostgreSQL needs the container to run as root,
passwordless `sudo`, or PostgreSQL already installed. Without any of those the script tells you
exactly what to run, and `--without-postgres` skips it.

## What this does not cover

- **Windows** without WSL. Use Docker Desktop, or WSL.
- **A PostgreSQL started without root.** If the server programs are installed but the system
  can't start the server for this user, the script says how to; it does not create a private
  cluster of its own.
- **A different PostgreSQL port**: it follows the first cluster `pg_lsclusters` lists, or `PGPORT`.
- Package managers other than apt, dnf and Homebrew.
