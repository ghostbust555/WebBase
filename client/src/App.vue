<script setup lang="ts">
import { onMounted, ref, watchEffect } from 'vue'
import { useRouter } from 'vue-router'
import { contentApi, type ContentSummary } from './api'
import { useAuthStore } from './stores/auth'
import { useConfigStore } from './stores/config'

const auth = useAuthStore()
const site = useConfigStore()
const router = useRouter()

watchEffect(() => { document.title = site.config.name })

// CMS pages with "menu: true" at the top of their file.
const menu = ref<ContentSummary[]>([])
onMounted(async () => {
  if (!site.config.cms) return
  try {
    menu.value = (await contentApi.pages()).filter((p) => p.menu)
  } catch { /* no menu, no harm */ }
})

function signOut() {
  auth.signOut()
  router.push('/')
}
</script>

<template>
  <!-- A banner on every page outside prod, so nobody mistakes one for the other. -->
  <div v-if="site.config.environment === 'development'" class="env-banner">
    Development{{ site.config.build ? ` · ${site.config.build}` : '' }}
  </div>

  <header class="nav">
    <RouterLink to="/" class="brand">{{ site.config.name }}</RouterLink>
    <nav>
      <RouterLink v-for="item in menu" :key="item.path" :to="`/${item.path}`">{{ item.title }}</RouterLink>
      <RouterLink to="/docs">Guide</RouterLink>
      <template v-if="auth.signedIn">
        <RouterLink to="/notes">Notes</RouterLink>
        <RouterLink to="/account">{{ auth.user?.displayName }}</RouterLink>
        <button class="link" @click="signOut">Sign out</button>
      </template>
      <template v-else>
        <RouterLink to="/login">Sign in</RouterLink>
        <RouterLink to="/register" class="button small">Sign up</RouterLink>
      </template>
    </nav>
  </header>

  <main class="page">
    <RouterView />
  </main>
</template>
