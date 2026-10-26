<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import GoogleSignInButton from '../components/GoogleSignInButton.vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const name = ref('')
const email = ref('')
const password = ref('')
const error = ref('')
const busy = ref(false)

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
    <h1>Create an account</h1>
    <GoogleSignInButton @credential="(c) => run(() => auth.google(c))" />
    <form @submit.prevent="run(() => auth.register(email, password, name))">
      <label>Your name <input v-model="name" autocomplete="name" /></label>
      <label>Email <input v-model="email" type="email" autocomplete="email" required /></label>
      <label>Password <input v-model="password" type="password" autocomplete="new-password" minlength="8" required /></label>
      <p class="muted small">At least 8 characters.</p>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <button class="button" type="submit" :disabled="busy">{{ busy ? 'Creating…' : 'Create account' }}</button>
    </form>
    <p class="muted">Already have one? <RouterLink :to="{ path: '/login', query: route.query }">Sign in</RouterLink></p>
  </section>
</template>
