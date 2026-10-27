<script setup lang="ts">
import { onMounted, watchEffect } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from './stores/auth'
import { useConfigStore } from './stores/config'
import { useMenuStore } from './stores/menu'

const auth = useAuthStore()
const site = useConfigStore()
const menu = useMenuStore()
const router = useRouter()

watchEffect(() => { document.title = site.config.name })

// The pages in the header: CMS files with "menu: true" and published pages
// authors have put in the menu (stores/menu.ts).
onMounted(() => menu.refresh())

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
      <RouterLink v-for="item in menu.links" :key="item.path" :to="`/${item.path}`">{{ item.title }}</RouterLink>
      <RouterLink to="/docs">Guide</RouterLink>
      <template v-if="auth.signedIn">
        <RouterLink to="/notes">Notes</RouterLink>
        <RouterLink v-if="auth.user?.isAuthor" to="/write">Pages</RouterLink>
        <RouterLink v-if="auth.user?.isAuthor" to="/authors">Authors</RouterLink>
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
