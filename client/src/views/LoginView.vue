<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import GoogleSignInButton from '../components/GoogleSignInButton.vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const email = ref('')
const password = ref('')
const error = ref('')
const busy = ref(false)

/** Back to wherever they were headed - but only ever a path on this site. */
function goOn() {
  const next = typeof route.query.next === 'string' && route.query.next.startsWith('/') && !route.query.next.startsWith('//')
    ? route.query.next : '/'
  router.replace(next)
}

async function run(action: () => Promise<void>) {
  error.value = ''
  busy.value = true
  try {
    await action()
    goOn()
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="auth-card">
    <h1>Sign in</h1>
    <GoogleSignInButton @credential="(c) => run(() => auth.google(c))" />
    <form @submit.prevent="run(() => auth.login(email, password))">
      <label>Email <input v-model="email" type="email" autocomplete="email" required /></label>
      <label>Password <input v-model="password" type="password" autocomplete="current-password" required /></label>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <button class="button" type="submit" :disabled="busy">{{ busy ? 'Signing in…' : 'Sign in' }}</button>
    </form>
    <p class="muted">New here? <RouterLink :to="{ path: '/register', query: route.query }">Create an account</RouterLink></p>
  </section>
</template>
