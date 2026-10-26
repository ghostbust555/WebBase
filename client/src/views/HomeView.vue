<script setup lang="ts">
import { useAuthStore } from '../stores/auth'
import { useConfigStore } from '../stores/config'

const auth = useAuthStore()
const site = useConfigStore()
</script>

<template>
  <!-- Your landing page. Replace all of this with what your site is about. -->
  <section class="hero">
    <h1>{{ site.config.name }}</h1>
    <p class="lede">
      A starting point for a website: a C# API, a Vue front end, a PostgreSQL
      database and sign-in, all running in Docker.
    </p>
    <div class="actions">
      <RouterLink v-if="auth.signedIn" to="/notes" class="button">Open your notes</RouterLink>
      <template v-else>
        <RouterLink to="/register" class="button">Create an account</RouterLink>
        <RouterLink to="/login" class="button secondary">Sign in</RouterLink>
      </template>
    </div>
  </section>

  <section class="cards">
    <div class="card">
      <h2>Edit this page</h2>
      <p>It is <code>client/src/views/HomeView.vue</code>. Save it and the browser updates by itself.</p>
    </div>
    <div class="card">
      <h2>See the API</h2>
      <p>In development, every endpoint is listed and can be tried at <a href="/scalar">/scalar</a>.</p>
    </div>
    <div class="card">
      <h2>Read the guide</h2>
      <p><code>docs/</code> explains how the pieces fit, how to add a feature and how to go live.</p>
    </div>
  </section>
</template>
