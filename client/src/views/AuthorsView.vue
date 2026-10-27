<script setup lang="ts">
// Who may edit the site's own content (docs/AUTHORS.md). Authors only: the
// route says requiresAuthor, and the API refuses everyone else regardless.
import { onMounted, ref } from 'vue'
import { authorsApi, type Author } from '../api'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const authors = ref<Author[]>([])
const email = ref('')
const error = ref('')
const loading = ref(true)

onMounted(async () => {
  try {
    authors.value = await authorsApi.list()
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading.value = false
  }
})

async function add() {
  error.value = ''
  try {
    const added = await authorsApi.add(email.value)
    if (!authors.value.some((a) => a.id === added.id)) authors.value.push(added)
    email.value = ''
  } catch (e) {
    error.value = (e as Error).message
  }
}

async function remove(author: Author) {
  if (!confirm(`Stop ${author.displayName} (${author.email}) from being an author?`)) return
  error.value = ''
  try {
    await authorsApi.remove(author.id)
    authors.value = authors.value.filter((a) => a.id !== author.id)
  } catch (e) {
    error.value = (e as Error).message
  }
}
</script>

<template>
  <section class="narrow">
    <h1>Authors</h1>
    <p class="muted">Authors can edit the site's pages and manage this list. Add someone who has already signed up, by their email.</p>

    <form class="row" @submit.prevent="add">
      <input v-model="email" type="email" placeholder="their@email.com" aria-label="Email of the new author" />
      <button class="button" type="submit" :disabled="!email.trim()">Add</button>
    </form>
    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <p v-if="loading" class="muted">Loading…</p>
    <ul v-else class="rows">
      <li v-for="author in authors" :key="author.id">
        <span>{{ author.displayName }} <small class="muted">{{ author.email }}</small></span>
        <small v-if="author.id === auth.user?.id" class="muted">you</small>
        <button v-else class="link danger" @click="remove(author)" :aria-label="`Remove ${author.email}`">Remove</button>
      </li>
    </ul>
  </section>
</template>
