<script setup lang="ts">
// THE HOME PAGE. Out of the box it shows the getting-started guide, so a new
// project opens on its own instructions.
//
// Make it yours: replace this file's contents with your landing page. Or turn
// on the CMS and write content/index.md - while that file exists, it is shown
// here instead (see docs/CMS.md).
import { onMounted, ref } from 'vue'
import ContentPageView from '../components/ContentPageView.vue'
import DocsReader from '../components/DocsReader.vue'
import { contentApi, type ContentPage } from '../api'
import { useConfigStore } from '../stores/config'

const site = useConfigStore()
const cmsHome = ref<ContentPage | null>(null)
const ready = ref(!site.config.cms)

onMounted(async () => {
  if (!site.config.cms) return
  try {
    cmsHome.value = await contentApi.page('')
  } catch { /* no content/index.md: show the guide */ }
  ready.value = true
})
</script>

<template>
  <template v-if="ready">
    <ContentPageView v-if="cmsHome" :page="cmsHome" />
    <template v-else>
      <aside class="starter">
        <strong>Your site is running.</strong>
        This is the getting-started guide, shown as the home page until you replace it: edit
        <code>client/src/views/HomeView.vue</code>, or turn on the CMS and write
        <code>content/index.md</code>. Every guide is also at <RouterLink to="/docs">/docs</RouterLink>.
      </aside>
      <DocsReader name="GETTING_STARTED" />
    </template>
  </template>
</template>
