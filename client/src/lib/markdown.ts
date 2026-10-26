// Markdown → safe HTML, used by the docs viewer and the CMS.
//
// `marked` turns markdown into HTML; DOMPurify then removes anything that
// could run code (scripts, onclick=..., javascript: links). Always render
// markdown through here, never straight into v-html.
import { Marked, type Tokens } from 'marked'
import DOMPurify from 'dompurify'

/** Rewrites a link or image address found in the markdown, e.g. "CLOUD.md" → "/docs/CLOUD". */
export type Resolver = (href: string, kind: 'link' | 'image') => string

/** GitHub's heading anchors: "If it doesn't work" → "if-it-doesnt-work". */
export function slugify(text: string): string {
  return text
    .replace(/<[^>]+>/g, '')
    .replace(/[`*~]/g, '')
    .toLowerCase()
    .trim()
    .replace(/[^\p{L}\p{N}\s_-]/gu, '')
    .replace(/\s/g, '-')
}

// Links to other sites open in a new tab.
DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  if (node.tagName === 'A' && /^https?:\/\//.test(node.getAttribute('href') ?? '')) {
    node.setAttribute('target', '_blank')
    node.setAttribute('rel', 'noopener noreferrer')
  }
})

export function renderMarkdown(source: string, resolve?: Resolver): string {
  const used = new Map<string, number>()
  const marked = new Marked({
    gfm: true,
    walkTokens(token) {
      if (!resolve) return
      if (token.type === 'link') (token as Tokens.Link).href = resolve((token as Tokens.Link).href, 'link')
      if (token.type === 'image') (token as Tokens.Image).href = resolve((token as Tokens.Image).href, 'image')
    },
    renderer: {
      // Every heading gets an id, so "page#section" links land on it.
      heading({ tokens, depth, text }) {
        let id = slugify(text)
        const seen = used.get(id) ?? 0
        used.set(id, seen + 1)
        if (seen) id = `${id}-${seen}`
        return `<h${depth} id="${id}">${this.parser.parseInline(tokens)}</h${depth}>\n`
      }
    }
  })
  return DOMPurify.sanitize(marked.parse(source, { async: false }))
}

/** True for addresses that leave the site or the page (https:, mailto:, #section...). */
export function isExternalOrAnchor(href: string): boolean {
  return /^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith('#') || href.startsWith('//')
}
