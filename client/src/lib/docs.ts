// The guides in docs/ (and the README), built into the website so the first
// page a new project shows is its own getting-started guide. Read at build
// time: editing a guide in development updates the page straight away.
//
// Building your real site? Point the home page somewhere else
// (client/src/views/HomeView.vue) and, if you like, delete the /docs route in
// router.ts, DocsView.vue, DocsReader.vue and this file.
import { isExternalOrAnchor, type Resolver } from './markdown'

const files = {
  ...import.meta.glob<string>('../../../docs/*.md', { query: '?raw', import: 'default', eager: true }),
  ...import.meta.glob<string>('../../../README.md', { query: '?raw', import: 'default', eager: true })
}

export interface Doc {
  name: string
  title: string
  source: string
}

// Reading order for the side menu. Anything not listed comes after, A-Z.
const ORDER = [
  'GETTING_STARTED', 'ENVIRONMENT', 'HOW_IT_FITS_TOGETHER', 'DEVELOPING', 'DATABASE', 'MODULES', 'CMS', 'DEPLOYING',
  'CLOUDFLARE_TUNNEL', 'GOOGLE_CLOUD', 'AZURE', 'AWS', 'CLOUD', 'GOOGLE_SIGNIN', 'AUTHORS', 'PAGES', 'EXAMPLE_NOTES', 'CONFIGURATION', 'TROUBLESHOOTING', 'README'
]

export const docs: Doc[] = Object.entries(files)
  .map(([path, source]) => {
    const name = path.split('/').pop()!.replace(/\.md$/, '')
    const heading = source.match(/^#\s+(.+)$/m)?.[1]?.trim()
    return { name, title: name === 'README' ? 'Overview' : heading ?? name, source }
  })
  .sort((a, b) => rank(a.name) - rank(b.name) || a.name.localeCompare(b.name))

function rank(name: string) {
  const i = ORDER.indexOf(name)
  return i < 0 ? ORDER.length : i
}

export function findDoc(name: string): Doc | undefined {
  return docs.find((d) => d.name.toLowerCase() === name.toLowerCase())
}

/** "CLOUD.md#azure", "docs/CLOUD.md" and "../README.md" all become pages of the viewer. */
export const resolveDocLink: Resolver = (href) => {
  if (isExternalOrAnchor(href)) return href
  const match = href.match(/^(?:\.\.?\/)*(?:docs\/)?([A-Za-z0-9_-]+)\.md(#.*)?$/)
  return match ? `/docs/${match[1]}${match[2] ?? ''}` : href
}
