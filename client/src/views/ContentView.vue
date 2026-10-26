<script setup lang="ts">
// Every address that isn't one of the app's own pages ends up here. With the
// CMS on, it's looked up in content/ (/about → content/about.md); otherwise,
// or if there's no such file, it's "page not found".
import { ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import ContentPageView from '../components/ContentPageView.vue'
import NotFoundView from './NotFoundView.vue'
import { contentApi, type ContentPage } from '../api'
import { useConfigStore } from '../stores/config'

const route = useRoute()
const site = useConfigStore()
const page = ref<ContentPage | null>(null)
const state = ref<'loading' | 'found' | 'missing'>('loading')

watch(() => route.path, async (path) => {
  if (!site.config.cms) { state.value = 'missing'; return }
  state.value = 'loading'
  try {
    page.value = await contentApi.page(path)
    state.value = 'found'
    document.title = `${page.value.title} · ${site.config.name}`
  } catch {
    state.value = 'missing'
  }
}, { immediate: true })
</script>

<template>
  <ContentPageView v-if="state === 'found' && page" :page="page" />
  <NotFoundView v-else-if="state === 'missing'" />
</template>
