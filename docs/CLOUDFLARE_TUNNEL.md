# Your own domain with Cloudflare Tunnel

This puts production at `https://yourdomain.com`, served from your own
computer, with:

- **no router or firewall changes.** The tunnel dials *out* to Cloudflare, so
  nothing on your network is opened up
- **a real HTTPS certificate**, handled by Cloudflare
- **your home IP address hidden** behind Cloudflare
- **free**, on Cloudflare's free plan

Only production can be reached this way. Development, the database and the
API docs are never exposed.

```
 visitor ──https──▶ Cloudflare ◀──outbound tunnel── cloudflared ──▶ app:8080 (prod)
                     (your domain)                   (container on your computer)
```

## What you need

- A **domain name**. Buy one from any registrar (Cloudflare itself,
  Namecheap, Porkbun, Google/Squarespace...). About $10-15 a year.
- A free **Cloudflare account**: <https://dash.cloudflare.com/sign-up>.
- Production running: `./deploy.sh`.

## 1. Put your domain on Cloudflare (once per domain)

Skip this if you bought the domain from Cloudflare.

1. In the Cloudflare dashboard, choose **Add a domain** (or "Add a site"),
   type your domain, and pick the **Free** plan.
2. Cloudflare shows two **nameservers**, like `ada.ns.cloudflare.com`.
3. At the company you bought the domain from, find **Nameservers** (sometimes
   under "DNS" or "Advanced"), choose "custom", and replace what's there with
   Cloudflare's two.
4. Wait. Usually it takes minutes, sometimes a few hours. Cloudflare emails
   you when the domain is active.

## 2. Create the tunnel

Cloudflare moves its menus around now and then. If a name below doesn't
match, look for **Tunnels** in the dashboard's search box.

1. In the Cloudflare dashboard, open **Zero Trust** (left menu). The first
   time, it asks you to pick a team name and the Free plan. A card is required
   but not charged.
2. Go to **Networks → Tunnels** and press **Create a tunnel**.
3. Choose **Cloudflared**, and name it after your project (e.g. `recipebox`).
4. On the "install connector" screen, pick **Docker**. It shows a command
   like:

   ```
   docker run cloudflare/cloudflared:latest tunnel --no-autoupdate run --token eyJhIjoi...
   ```

   **Don't run it.** Copy only the long token after `--token` (it starts with
   `eyJ`).
5. Put it in your project's `.env.local`:

   ```
   CLOUDFLARE_TUNNEL_TOKEN=eyJhIjoi...
   ```

   This token *is* the tunnel: anyone who has it can serve their own content
   on your domain. Keep it secret like the other values in `.env.local`.
6. Press **Next**. You're on **Public hostname** (newer dashboards call it
   "Published application routes"). Fill in:

   | Field | Value |
   |---|---|
   | Subdomain | blank for `yourdomain.com`, or e.g. `www` / `app` |
   | Domain | your domain |
   | Path | blank |
   | Service type | **HTTP** |
   | URL | **`app:8080`** |

   `app:8080` is the production container's name and port, as seen from the
   tunnel container. **Don't point it at anything else.** In particular, never
   use `db:5432`, which would expose the database.
7. Save. Cloudflare creates the DNS record for you.

## 3. Turn it on

```bash
./tunnel.sh on
```

```
 ✓ The tunnel is connected. Your domain now reaches prod.
```

Open `https://yourdomain.com`, then check it properly with
`./scripts/smoke.sh https://yourdomain.com`.

| Command | What it does |
|---|---|
| `./tunnel.sh status` | connected or not |
| `./tunnel.sh off` | stop being public, right away |
| `./tunnel.sh logs` | the tunnel's own messages |

The tunnel restarts with production after a reboot. `./deploy.sh` releases
don't interrupt it for more than the few seconds the app takes to restart.

## Also do

- **Google sign-in**: add `https://yourdomain.com` to the client ID's
  "Authorized JavaScript origins" ([GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)).
- **www and the bare domain**: to serve both, add a second public hostname
  (step 2.6) for the other one, also pointing at `app:8080`.
- In Cloudflare, under your domain → **SSL/TLS**, set the mode to **Full**,
  and turn on **Always Use HTTPS** under SSL/TLS → Edge Certificates.

## If it doesn't work

| What you see | What it is |
|---|---|
| `./tunnel.sh on` says not connected | The token is wrong or incomplete. Copy it again, the whole thing. `./tunnel.sh logs` shows Cloudflare's answer. |
| Cloudflare error 1033 | The tunnel isn't running: `./tunnel.sh on`. |
| Cloudflare error 502 / "Bad gateway" | The tunnel is up but can't reach the app. Is the URL in step 2.6 exactly `app:8080`, with type HTTP? Is prod running (`./deploy.sh status`)? |
| The domain doesn't load at all | DNS not active yet (step 1), or no public hostname for that exact name (step 2.6). |
| "Too many redirects" | SSL/TLS mode is "Flexible". Set it to **Full**. |

## Without the tunnel

Prefer something else? Production is a normal web server on
`127.0.0.1:8080`, so any reverse proxy in front of it works too: Caddy,
nginx, Tailscale Funnel. Change `PROD_PORT` in `.env.local` if 8080 is taken.
