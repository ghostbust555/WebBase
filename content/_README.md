# The content folder

Pages for the optional CMS. Each `.md` file here is a page on the website, and
the folders are the addresses:

| File | Address |
|---|---|
| `index.md` | `/` (the home page) |
| `about.md` | `/about` |
| `blog/index.md` | `/blog` |
| `blog/hello-world.md` | `/blog/hello-world` |
| `images/placeholder.svg` | `/content/images/placeholder.svg` (any non-markdown file) |
| `theme.css` | not a page: the site's colours, as CSS variables |

Names starting with `_` (like this file) or `.` are ignored and never served.
The CMS is off until `CMS_ENABLED=true` is in `.env.local`. This folder can
also live in an S3 bucket or Azure Blob container instead (`CMS_SOURCE`). The
full guide is docs/CMS.md.
