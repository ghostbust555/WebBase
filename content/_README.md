# The content folder

Pages for the optional CMS. Each `.md` file here is a page on the website, and
the folders are the addresses:

| File | Address |
|---|---|
| `index.md` | `/` (the home page) |
| `about.md` | `/about` |
| `blog/index.md` | `/blog` |
| `blog/hello-world.md` | `/blog/hello-world` |

Names starting with `_` (like this file) are ignored. The CMS is off until
`CMS_ENABLED=true` is in `.env.local`. The full guide is docs/CMS.md.
