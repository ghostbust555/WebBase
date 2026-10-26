# Google sign-in (optional)

The code is all there and switched off. With no client ID set, the button
isn't drawn, Google's script is never loaded, and `POST /api/auth/google`
answers 404. Turning it on takes about ten minutes of clicking in Google's
console and one line in `.env.local`.

There's **no client secret** anywhere. The button uses Google's ID-token
flow, which needs only a client ID, and the client ID is public by design
(it's written into the page that draws the button).

## 1. A Google Cloud project and consent screen

1. Go to <https://console.cloud.google.com/> and create a project (top bar →
   project picker → **New project**), or pick an existing one.
2. Open **APIs & Services → OAuth consent screen** (newer consoles call it
   **Google Auth Platform → Branding**). Choose **External**, then fill in the
   app name, a support email and a developer contact email.
3. Under **Audience**, leave it in **Testing** while you try it, and add your
   own Google account as a **test user**. In testing, only test users can sign
   in. **Publish** the app when you want everyone to be able to.
4. Scopes: add nothing. The defaults (name and email) are all this uses.

## 2. The client ID

1. **APIs & Services → Credentials → Create credentials → OAuth client ID**
   (or **Google Auth Platform → Clients → Create client**).
2. Application type: **Web application**.
3. **Authorized JavaScript origins**: every address the site is opened at,
   exactly, with no trailing slash:
   - `http://localhost:5173` for development
   - `http://localhost:8080` for production on this computer
   - `https://yourdomain.com` once it's on your domain, and any cloud address
     (`https://....azurecontainerapps.io` and so on)

   Google accepts only `localhost` or real domain names. An address like
   `http://my-computer:5173` or `http://192.168.1.20:5173` won't work.
   Google sign-in there shows an error, while email and password still work.
4. **Authorized redirect URIs**: leave empty. This flow doesn't redirect.
5. Create, then copy the **Client ID** (it ends in `.apps.googleusercontent.com`).

## 3. Switch it on

In `.env.local`:

```
GOOGLE_CLIENT_ID=1234567890-abc123.apps.googleusercontent.com
```

then `./dev.sh restart` (and `./deploy.sh restart` for production). Check:

```bash
curl -s http://localhost:5173/api/config
# ... "googleClientId":"1234567890-abc123.apps.googleusercontent.com" ...
```

Open the sign-in page. The Google button is above the email form.

On a cloud host, set `Auth__Google__ClientId` to the same value
([CLOUD.md](CLOUD.md#what-every-host-needs)). The cloud scripts pass along
`GOOGLE_CLIENT_ID` from `.env.local` by themselves.

## How accounts are matched

(`AuthService.SignInWithGoogleAsync`, and tested in `GoogleSignInTests.cs`.)

1. **This Google account has signed in before**: it's that person, even if
   their Gmail address has changed since.
2. **New to Google sign-in, but the email already has an account here**: it's
   the same person arriving a different way. The two are linked, and their
   password keeps working too. This happens only when Google says the email
   is **verified**, so nobody can take over an account by putting someone
   else's address on a Google account.
3. **Neither**: a new account, with no password. That person can only sign
   in with Google.

## If it doesn't work

| What you see | What it means |
|---|---|
| No Google button | The server doesn't have the client ID. Check `curl .../api/config`, check the spelling in `.env.local`, and restart. |
| `origin_mismatch` / "no registered origin" (browser console or popup) | The address you're browsing from isn't in the authorized origins exactly as typed. Check http vs https, and the port. Changes take a few minutes to apply. |
| "Access blocked: app has not completed verification" | The consent screen is in Testing and this Google account isn't a test user. Add it, or publish the app. |
| "That Google sign-in could not be confirmed" | The server's client ID differs from the one the button used, or the server's clock is badly wrong. |
