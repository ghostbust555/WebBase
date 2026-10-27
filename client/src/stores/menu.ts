import { defineStore } from 'pinia'
import { ref } from 'vue'
import { contentApi, pagesApi } from '../api'
import { useConfigStore } from './config'

export interface MenuLink { path: string; title: string; order: number | null }

/**
 * The header's page links, from both kinds of page: markdown files (the CMS,
 * `menu: true`) and the pages authors write in the browser. A file wins when
 * both have an address (docs/PAGES.md). The editor calls refresh() after
 * publishing, so the header changes without a reload.
 */
export const useMenuStore = defineStore('menu', () => {
  const links = ref<MenuLink[]>([])

  async function refresh() {
    const site = useConfigStore()
    const [files, written] = await Promise.all([
      site.config.cms ? contentApi.pages().catch(() => []) : [],
      pagesApi.menu().catch(() => [])
    ])
    const seen = new Set<string>()
    const merged: MenuLink[] = []
    for (const item of [
      ...files.filter((p) => p.menu).map((p) => ({ path: p.path, title: p.title, order: p.order ?? null })),
      ...written.map((p) => ({ path: p.path, title: p.title, order: p.order }))
    ]) {
      if (seen.has(item.path)) continue
      seen.add(item.path)
      merged.push(item)
    }
    // Lower order first; pages without one come after; ties keep the order above (files first).
    links.value = merged
      .map((item, index) => ({ item, index }))
      .sort((a, b) => (a.item.order ?? Infinity) - (b.item.order ?? Infinity) || a.index - b.index)
      .map(({ item }) => item)
  }

  return { links, refresh }
})
