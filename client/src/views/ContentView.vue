<script setup lang="ts">
// Every address that isn't one of the app's own pages ends up here. It is looked
// up in two places, in this order (docs/PAGES.md):
//   1. content/, if the CMS is on (/about → content/about.md). Files win.
//   2. the pages authors write in the browser.
// If neither has it, it's "page not found".
import { ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import ContentPageView from '../components/ContentPageView.vue'
import NotFoundView from './NotFoundView.vue'
import { contentApi, pagesApi, type ContentPage } from '../api'
import { useConfigStore } from '../stores/config'

const route = useRoute()
const site = useConfigStore()
const page = ref<ContentPage | null>(null)
const state = ref<'loading' | 'found' | 'missing'>('loading')
let latest = 0 // a slow answer for the page we just left must not replace the one we're on

async function find(path: string): Promise<ContentPage | null> {
  if (site.config.cms) {
    try { return await contentApi.page(path) } catch { /* not a file: try the written pages */ }
  }
  try {
    const written = await pagesApi.published(path)
    return { path: written.path, title: written.title, description: null, date: null, order: null, menu: false, draft: false, markdown: written.markdown, children: null }
  } catch {
    return null
  }
}

watch(() => route.path, async (path) => {
  const mine = ++latest
  state.value = 'loading'
  const found = await find(path)
  if (mine !== latest) return
  page.value = found
  state.value = found ? 'found' : 'missing'
  if (found) document.title = `${found.title} · ${site.config.name}`
}, { immediate: true })
</script>

<template>
  <ContentPageView v-if="state === 'found' && page" :page="page" />
  <NotFoundView v-else-if="state === 'missing'" />
</template>
