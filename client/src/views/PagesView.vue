<script setup lang="ts">
// The authors' pages, drafts included (docs/PAGES.md). Authors only: the route
// says requiresAuthor, and the API refuses everyone else regardless.
import { onMounted, ref } from 'vue'
import { editorApi, type PageSummaryDto } from '../api'

const pages = ref<PageSummaryDto[]>([])
const error = ref('')
const loading = ref(true)

onMounted(async () => {
  try {
    pages.value = await editorApi.list()
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <section class="narrow wide">
    <div class="title-row">
      <h1>Pages</h1>
      <RouterLink to="/write/new" class="button small">New page</RouterLink>
    </div>
    <p class="muted">Pages written here show up on the site at their address. Drafts are visible only to authors.</p>
    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <p v-if="loading" class="muted">Loading…</p>
    <p v-else-if="pages.length === 0 && !error" class="muted">No pages yet. Write the first one.</p>
    <ul v-else class="rows">
      <li v-for="page in pages" :key="page.id">
        <span>
          <RouterLink :to="`/write/${page.id}`">{{ page.title }}</RouterLink>
          <small class="muted"> /{{ page.path }}</small>
          <small v-if="page.showInMenu" class="muted"> · in the menu</small>
        </span>
        <span class="badge" :class="page.published ? 'published' : 'draft-badge'">{{ page.published ? 'Published' : 'Draft' }}</span>
      </li>
    </ul>
  </section>
</template>
