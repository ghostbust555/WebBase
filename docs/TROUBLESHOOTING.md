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
| Production doesn't have my change | Production only changes on `./deploy.sh`. |

## Database

| Symptom | Fix |
|---|---|
| `relation "X" does not exist` | A model changed without a migration: `./scripts/ef.sh migrations add <Name>`, then restart. |
| `password authentication failed for user "webbase_app"` (prod) | `.env.local`'s database passwords no longer match the ones prod's database was created with. Put the originals back. |
| Want a clean development database | `./dev.sh reset-db` |
| `pending model changes` warning in the logs | Same as the first row: add a migration. |

## Production

| Symptom | Fix |
|---|---|
| `Jwt:Key must be set` | `JWT_KEY` is missing or short in `.env.local`. Run `./setup.sh`. |
| `Set POSTGRES_PASSWORD in .env.local` | Run `./setup.sh`. |
| Deploy said "did not come up healthy" | It already switched back to the previous release. Read the error it printed, fix it, and deploy again. |
| Works on localhost, not on the domain | See the table at the end of [CLOUDFLARE_TUNNEL.md](CLOUDFLARE_TUNNEL.md#if-it-doesnt-work). |
| "Too many attempts" when signing in | The rate limit: 10 tries a minute per visitor. Wait a minute. |

## Signed in, then suddenly signed out

The token was refused: `JWT_KEY` changed, or the account was deleted, or the
person didn't visit for 30 days. Sign in again.

## Still stuck

Collect, and include when you ask for help:

1. The command you ran and **everything** it printed.
2. `./dev.sh logs api` (or `./deploy.sh logs`): the last 50 lines.
3. `docker --version` and your operating system.

**Never include `.env.local`.** It holds your secrets.
