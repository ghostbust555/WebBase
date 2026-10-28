import { defineStore } from 'pinia'
import { ref } from 'vue'
import { configApi, type SiteConfig } from '../api'

/**
 * Facts about the deployment the page is talking to: its name, whether it is
 * development (which shows a banner), its build, and whether Google sign-in is
 * on. Read once at startup from GET /api/config.
 */
export const useConfigStore = defineStore('config', () => {
  const config = ref<SiteConfig>({ name: 'WebBase', environment: 'prod', build: '', googleClientId: null, cms: false, builtAtUtc: null })

  async function load() {
    try {
      config.value = await configApi.get()
    } catch { /* the API is down; the pages that need it will say so */ }
    document.documentElement.dataset.env = config.value.environment
  }

  return { config, load }
})
