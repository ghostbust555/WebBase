<script setup lang="ts">
// THE EXAMPLE FEATURE: a page that lists, adds and deletes the signed-in
// user's notes through /api/notes. Copy this shape for your own pages - load
// on mount, show errors, update the list after each change.
import { onMounted, ref } from 'vue'
import { notesApi, type Note } from '../api'

const notes = ref<Note[]>([])
const text = ref('')
const error = ref('')
const loading = ref(true)

onMounted(async () => {
  try {
    notes.value = await notesApi.list()
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading.value = false
  }
})

async function add() {
  error.value = ''
  try {
    notes.value.unshift(await notesApi.create(text.value))
    text.value = ''
  } catch (e) {
    error.value = (e as Error).message
  }
}

async function remove(note: Note) {
  error.value = ''
  try {
    await notesApi.remove(note.id)
    notes.value = notes.value.filter((n) => n.id !== note.id)
  } catch (e) {
    error.value = (e as Error).message
  }
}
</script>

<template>
  <section class="narrow">
    <h1>Your notes</h1>
    <p class="muted">Only you can see these. This page is the example feature - see docs/EXAMPLE_NOTES.md.</p>

    <form class="row" @submit.prevent="add">
      <input v-model="text" placeholder="Write a note…" maxlength="2000" aria-label="New note" />
      <button class="button" type="submit" :disabled="!text.trim()">Add</button>
    </form>
    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <p v-if="loading" class="muted">Loading…</p>
    <p v-else-if="notes.length === 0" class="muted">No notes yet.</p>
    <ul v-else class="notes">
      <li v-for="note in notes" :key="note.id">
        <span>{{ note.text }}</span>
        <small class="muted">{{ new Date(note.createdAtUtc).toLocaleString() }}</small>
        <button class="link danger" @click="remove(note)" aria-label="Delete note">Delete</button>
      </li>
    </ul>
  </section>
</template>
