# Getting started

This takes you from nothing to your own website running on your computer. No
programming experience is assumed. It takes 20-40 minutes, most of it
waiting for downloads.

When something doesn't match what's described here, look in
[TROUBLESHOOTING.md](TROUBLESHOOTING.md), or ask someone and show them the
exact message you see. Error messages are meant to be read, and they usually
say what's wrong.

---

## Words you'll see

- **Terminal**: a window where you type commands instead of clicking. On a Mac
  it's the app called *Terminal*. On Windows you'll use *Ubuntu* (installed in
  step 1). On Linux it's usually called *Terminal* too. When this guide shows a
  grey box like `./dev.sh`, type that into the terminal and press Enter.
- **Docker**: a program that runs other programs in sealed boxes called
  *containers*. Your website runs in a few of them: one for the pages, one for
  the API, one for the database. Docker means you don't have to install any of
  those yourself.
- **Repository** (or *repo*): the project folder, tracked by **git** so every
  change is saved and can be undone.
- **localhost**: "this computer". `http://localhost:5173` is a website running
  on your own machine, visible only to you.
- **Development** vs **production** (*dev* and *prod*): development is the copy
  you work on, which changes every time you save a file. Production is the
  copy real visitors use, and it only changes when you release.

---

## 1. Install the tools

You need **Docker Desktop** and **git**.

### Mac

1. Install Docker Desktop: <https://www.docker.com/products/docker-desktop/>.
   Pick "Apple Silicon" or "Intel" to match your Mac (Apple menu → About This
   Mac: "Chip: Apple M..." means Apple Silicon). Open it once and let it
   finish starting. A whale icon appears at the top of the screen.
2. git: open Terminal and type `git --version`. If it isn't installed, macOS
   offers to install it. Say yes.

### Windows

The scripts here are written for Linux and macOS, so on Windows they run
inside **WSL** (Windows Subsystem for Linux), a real Linux built into Windows.

1. Open **PowerShell as Administrator** (right-click Start → Terminal (Admin))
   and run `wsl --install`. Restart when asked. After the restart an *Ubuntu*
   window opens and asks you to choose a username and password. Remember them.
2. Install Docker Desktop: <https://www.docker.com/products/docker-desktop/>.
   In its Settings → Resources → WSL integration, turn on **Ubuntu**.
3. From now on, use the **Ubuntu** app as your terminal. Inside it, run
   `sudo apt update && sudo apt install -y git` (it asks for the password from step 1).
4. Keep your project inside Ubuntu's own folders (the `cd ~` below does
   this), not under `C:\`. It runs far faster there.

### Linux

Install Docker Engine (<https://docs.docker.com/engine/install/>) and git
(`sudo apt install git` or your distribution's equivalent). Add yourself to
the docker group so you don't need `sudo` for every command:
`sudo usermod -aG docker $USER`, then log out and back in.

### Check

In your terminal:

```bash
docker --version
docker compose version
git --version
```

All three should print a version number. If `docker` says it can't connect,
Docker Desktop isn't running. Open it and wait for it to finish starting.

---

## 2. Get the project

```bash
cd ~
git clone <the repository's address> my-site
cd my-site
```

Replace `<the repository's address>` with the address from GitHub (green
**Code** button → copy the HTTPS address). `my-site` is the folder name, and
you can pick anything.

If you were given a GitHub *template*, press **Use this template** on GitHub
first. That makes a copy that's yours, and you clone the copy instead.

---

## 3. Set it up

```bash
./setup.sh
```

It checks Docker, then asks for a **project name**: one word, starting with a
capital letter, like `RecipeBox`. That name replaces "WebBase" everywhere.
Press Enter to keep "WebBase" for now.

Then it creates a file called `.env.local` holding your project's **secrets**:
randomly generated passwords and keys. That file stays on your computer and is
never uploaded to GitHub. Don't send it to anyone.

---

## 4. Start it

```bash
./dev.sh
```

The first time, this downloads a few hundred megabytes and compiles
everything, which takes a few minutes. Later starts take seconds. When it's
ready it prints:

```
 ✓ Development is running

    Website     http://localhost:5173
```

Open **<http://localhost:5173>** in your browser. That's your website. Its
home page is this very guide, so from here on you can read along in the
browser. Every guide is in the **Guide** menu.

Press **Sign up** and create an account. It is saved in your own database, and
because it is the first account on this site, it is an *author*: the menu shows
**Pages** (write pages in the browser) and **Authors**
([AUTHORS.md](AUTHORS.md)). Try **Pages** → **New page**.

---

## 5. Change something

1. Open the project folder in a code editor. [VS Code](https://code.visualstudio.com)
   is free and a good choice (File → Open Folder → `my-site`). On Windows,
   install VS Code's "WSL" extension, then type `code .` in the Ubuntu terminal
   from inside the project folder.
2. Open `client/src/views/HomeView.vue`. It's the home page.
3. Find the words **Your site is running.**, change them, and save.
4. Look at the browser. It has already updated.

When you're ready for a real home page, replace what's in that file. The
guide stays readable at `/docs`. [DEVELOPING.md](DEVELOPING.md#your-home-page)
explains how. For pages that are mostly text (an About page, a blog), the
optional [CMS](CMS.md) lets you write them as simple text files instead.

The same works for the C# code under `src/`. Save, and the API rebuilds itself
within a few seconds.

To see what's happening behind the scenes (useful when something breaks),
run `./dev.sh logs`. Press Ctrl+C to stop watching. The site keeps running.

---

## 6. Save your work with git

```bash
git add -A
git commit -m "Describe what you changed"
git push
```

`commit` saves a snapshot on your computer, and `push` uploads it to GitHub.
Do this every time you finish something that works.

---

## 7. Stop it

```bash
./dev.sh stop
```

Your accounts and data are kept, and `./dev.sh` brings everything back.
Docker Desktop has to be running whenever you use any of these commands.

---

## Where next

- **Understand what you're looking at**: [HOW_IT_FITS_TOGETHER.md](HOW_IT_FITS_TOGETHER.md)
- **Build your own pages and features**: [DEVELOPING.md](DEVELOPING.md)
- **Run the real, public copy**: [DEPLOYING.md](DEPLOYING.md), then
  [CLOUDFLARE_TUNNEL.md](CLOUDFLARE_TUNNEL.md) for your own domain, or
  [CLOUD.md](CLOUD.md) to host it in a cloud instead.
- **Add "Sign in with Google"**: [GOOGLE_SIGNIN.md](GOOGLE_SIGNIN.md)
