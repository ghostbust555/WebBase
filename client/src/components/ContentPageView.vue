<script setup lang="ts">
// One CMS page: its title, date, text, and - for a folder like blog/ - a list
// of the pages inside it.
import { computed } from 'vue'
import MarkdownView from './MarkdownView.vue'
import { isExternalOrAnchor, type Resolver } from '../lib/markdown'
import type { ContentPage } from '../api'

const props = defineProps<{ page: ContentPage }>()

// The folder the page's file is in. A folder page (blog/index.md) is inside
// "blog"; a file page (blog/first-post.md) is inside "blog" too.
const folder = computed(() => {
  if (props.page.children) return props.page.path
  const parts = props.page.path.split('/')
  return parts.slice(0, -1).join('/')
})

// Relative addresses in the markdown are relative to the page's own folder,
// like on GitHub: "images/cat.jpg" is a file in content/, "other.md" is a page.
const resolve: Resolver = (href) => {
  if (isExternalOrAnchor(href) || href.startsWith('/')) return href
  const base = folder.value ? `${folder.value}/` : ''
  const page = href.match(/^(.+?)\.md(#.*)?$/)
  if (page) return `/${base}${page[1]}${page[2] ?? ''}`.replace(/\/index$/, '') || '/'
  return `/content/${base}${href}`
}

function formatDate(date: string) {
  return new Date(`${date}T00:00:00`).toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' })
}
</script>

<template>
  <section class="content-page">
    <p v-if="page.draft" class="draft">Draft: visible in development only.</p>
    <h1>{{ page.title }}</h1>
    <p v-if="page.date" class="muted">{{ formatDate(page.date) }}</p>
    <MarkdownView v-if="page.markdown.trim()" :source="page.markdown" :resolve="resolve" />

    <ul v-if="page.children?.length" class="content-list">
      <li v-for="child in page.children" :key="child.path">
        <RouterLink :to="`/${child.path}`">{{ child.title }}</RouterLink>
        <small v-if="child.date" class="muted"> · {{ formatDate(child.date) }}</small>
        <p v-if="child.description" class="muted">{{ child.description }}</p>
      </li>
    </ul>
  </section>
</template>
