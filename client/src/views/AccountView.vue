<script setup lang="ts">
import { ref } from 'vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const name = ref(auth.user?.displayName ?? '')
const message = ref('')
const error = ref('')

async function save() {
  message.value = ''
  error.value = ''
  try {
    await auth.updateName(name.value)
    message.value = 'Saved.'
  } catch (e) {
    error.value = (e as Error).message
  }
}
</script>

<template>
  <section class="auth-card" v-if="auth.user">
    <h1>Your account</h1>
    <dl class="facts">
      <dt>Email</dt><dd>{{ auth.user.email }}</dd>
      <dt>Signs in with</dt>
      <dd>{{ [auth.user.hasPassword && 'password', auth.user.hasGoogle && 'Google'].filter(Boolean).join(' and ') }}</dd>
      <dt>Member since</dt><dd>{{ new Date(auth.user.createdAtUtc).toLocaleDateString() }}</dd>
    </dl>
    <form @submit.prevent="save">
      <label>Display name <input v-model="name" maxlength="80" required /></label>
      <p v-if="message" class="success">{{ message }}</p>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <button class="button" type="submit">Save</button>
    </form>
  </section>
</template>
