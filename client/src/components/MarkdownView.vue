<script setup lang="ts">
// Shows markdown as a formatted page. Links to other pages of this site move
// within the app (no full reload), and "#section" in the address scrolls to
// that heading.
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { renderMarkdown, type Resolver } from '../lib/markdown'

const props = defineProps<{ source: string; resolve?: Resolver }>()
const html = computed(() => renderMarkdown(props.source, props.resolve))
const root = ref<HTMLElement | null>(null)
const route = useRoute()
const router = useRouter()

function scrollToHash() {
  const id = decodeURIComponent(route.hash.slice(1))
  const target = id ? root.value?.querySelector(`[id="${CSS.escape(id)}"]`) : null
  if (target) target.scrollIntoView()
}
watch([html, () => route.hash], () => nextTick(scrollToHash), { immediate: true })

function onClick(event: MouseEvent) {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey) return
  const link = (event.target as HTMLElement).closest('a')
  const href = link?.getAttribute('href')
  if (!link || !href || link.target) return
  // Files (pictures, downloads) and the API are real requests, not pages.
  if (href.startsWith('/') && !href.startsWith('//') && !href.startsWith('/content/') && !href.startsWith('/api/')) {
    event.preventDefault()
    router.push(href)
  } else if (href.startsWith('#')) {
    event.preventDefault()
    router.replace({ hash: href })
  }
}
</script>

<template>
  <!-- Safe: renderMarkdown() sanitizes everything it returns. -->
  <article ref="root" class="prose" v-html="html" @click="onClick"></article>
</template>
