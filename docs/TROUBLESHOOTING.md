# Troubleshooting

First, look at what the program is saying:

- Development: `./dev.sh logs` (all), `./dev.sh logs api`, `./dev.sh logs web`
- Production: `./deploy.sh logs`
- In the browser: right-click → **Inspect** → **Console** for page errors, and
  **Network** for each API call and its answer.

The actual error message is nearly always the fastest route to an answer,
whether you're asking a person or an AI. Copy it in full.

## Starting up

| Symptom | Fix |
|---|---|
| `Docker is installed but not running` | Open Docker Desktop and wait for it to finish starting. Linux: `sudo systemctl start docker`. |
| `permission denied ... docker.sock` (Linux) | `sudo usermod -aG docker $USER`, then log out and back in. |
| `./setup.sh: Permission denied` | `chmod +x *.sh scripts/*.sh scripts/cloud/*.sh` |
| `bad interpreter` or `$'\r': command not found` (Windows) | The files got Windows line endings. In Ubuntu: `git config --global core.autocrlf input`, then clone again. Work inside Ubuntu, not in `C:\`. |
| `port is already allocated` / `address already in use` | Something else is using that port, often another project. Change `DEV_WEB_PORT` (etc.) in `.env.local`. On Linux/Mac, `lsof -i :5173` shows who has it. |
| `./dev.sh` waits a long time the first time | Normal: it's downloading and building. `./dev.sh logs` shows progress. Later starts are quick. |
| `It hasn't come up yet` | `./dev.sh logs api`. A C# compile error appears there with a file and line number. |
| The page says "Cannot reach the server" | The API isn't running or has crashed. `./dev.sh logs api`. |

## Changes not showing up

| Symptom | Fix |
|---|---|
| Edited a `.vue` file, page didn't change | Check `./dev.sh logs web` for an error. A hard refresh (Ctrl+Shift+R / Cmd+Shift+R) helps. |
| Edited C#, nothing changed | `./dev.sh logs api`: it may be waiting on a compile error, or need `./dev.sh restart`. |
| Changed `.env.local`, nothing changed | `./dev.sh restart` / `./deploy.sh restart`. Settings are read at start. |
| Production doesn't have my change | Production only changes on `./deploy.sh`. CMS content in a bucket is the exception: it shows within `Cms__RefreshSeconds` (a minute) without a deploy. |
| `TypeScriptContractsTests` fails: "api-types.ts is out of date" | A C# type the API sends or receives changed. `./scripts/gen-types.sh` (or start `./dev.sh`), then commit `client/src/api-types.ts`. |
| `npm run build` fails in `api.ts` or a page after a C# change | Working as intended: a field was renamed or retyped in C#. Update the website code it names. |
| Generation fails: "... returns IActionResult, which doesn't say what's in it" | Add `[ProducesResponseType<YourDto>(200)]` (or `(204)`) to that action, or return `ActionResult<YourDto>`. |

## Database

| Symptom | Fix |
|---|---|
| `relation "X" does not exist` | A model changed without a migration: `./scripts/ef.sh migrations add <Name>`, then restart. |
| `password authentication failed for user "webbase_app"` (prod) | `.env.local`'s database passwords no longer match the ones prod's database was created with. Put the originals back. |
| Want a clean development database | `./dev.sh reset-db` |
| `pending model changes` warning in the logs | Same as the first row: add a migration. |
| Prod with `PROD_DATABASE_URL`: "did not come up healthy", `Name or service not known`, `timeout`, or `no pg_hba.conf entry` | The managed database can't be reached from this computer, or refuses it. Check the URL, and the provider's firewall / allowed-addresses setting. `./deploy.sh logs` shows the exact error. |
| `Database:Provider is 'x', but this app is built with: ...` | A typo in `Database__Provider`, or a database module the app isn't built with yet: `./scripts/module.sh add <name>` ([MODULES.md](MODULES.md)). |
| `isn't a complete backup` from `restore.sh` | That file came from a backup that failed, or it's damaged. Use an older one. |

## Production

| Symptom | Fix |
|---|---|
| `Jwt:Key must be set` | `JWT_KEY` is missing or short in `.env.local`. Run `./setup.sh`. |
| `Set POSTGRES_PASSWORD in .env.local` | Run `./setup.sh`. |
| Deploy said "did not come up healthy" | It already switched back to the previous release. Read the error it printed, fix it, and deploy again. |
| Works on localhost, not on the domain | See the table at the end of [CLOUDFLARE_TUNNEL.md](CLOUDFLARE_TUNNEL.md#if-it-doesnt-work). |
| "Too many attempts" when signing in | The rate limit: 10 tries a minute per visitor. Wait a minute. |
| `Cms:Source is 'gcs', but this app is built with: files` (or `s3`, `azure`) | Build the app with that storage module: `./scripts/module.sh add gcs`, commit, deploy ([MODULES.md](MODULES.md)). |
| A cloud script: setup, deploy, or the live site | The table at the end of [GOOGLE_CLOUD.md](GOOGLE_CLOUD.md#if-something-goes-wrong), [AZURE.md](AZURE.md#if-something-goes-wrong) or [AWS.md](AWS.md#if-something-goes-wrong). |
| CMS pages say "The site's content can't be reached right now" (503) | The bucket can't be read: a wrong bucket, prefix or region, missing permission, or the service is down. The log line at startup names the store (`cms=on (s3://...)`), and `./deploy.sh logs` shows the error. |

## Signed in, then suddenly signed out

The token was refused: `JWT_KEY` changed, or the account was deleted, or the
person didn't visit for 30 days. Sign in again.

## Still stuck

Collect, and include when you ask for help:

1. The command you ran and **everything** it printed.
2. `./dev.sh logs api` (or `./deploy.sh logs`): the last 50 lines.
3. `docker --version` and your operating system.

**Never include `.env.local`.** It holds your secrets.
