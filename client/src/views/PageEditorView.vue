<script setup lang="ts">
// Write one of the site's own pages (docs/PAGES.md): markdown on one side, the
// result on the other (tabs, on a phone). The route is /write/new or /write/<id>;
// after the first save the address changes from new to the id, in place.
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router'
import MarkdownView from '../components/MarkdownView.vue'
import { editorApi, type PageDto, type SavePageRequest } from '../api'
import { useMenuStore } from '../stores/menu'

// Mirrors PageRules.cs. The server enforces them; these only help while typing.
const MAX_TITLE = 120
const MAX_BODY = 80_000

const route = useRoute()
const router = useRouter()
const menu = useMenuStore()

const id = ref<string | null>(null) // null = not saved yet
const title = ref('')
const path = ref('')
const body = ref('')
const showInMenu = ref(false)
const order = ref(0)
const published = ref(false)

const tab = ref<'write' | 'preview'>('write') // phones show one at a time
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const message = ref('')

// What was last saved (or loaded), to tell whether there is anything to lose.
const form = () => JSON.stringify([title.value, path.value, body.value, showInMenu.value, order.value])
const saved = ref(form())
const dirty = computed(() => form() !== saved.value)

function show(page: PageDto | null) {
  id.value = page?.id ?? null
  title.value = page?.title ?? ''
  path.value = page?.path ?? ''
  body.value = page?.body ?? ''
  showInMenu.value = page?.showInMenu ?? false
  order.value = page?.order ?? 0
  published.value = page?.published ?? false
  saved.value = form()
}

async function load(routeId: string) {
  error.value = ''
  message.value = ''
  if (routeId === 'new') return show(null)
  loading.value = true
  try {
    show(await editorApi.get(routeId))
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading.value = false
  }
}

onMounted(() => load(String(route.params.id)))
// A different page arrived through a link in this same screen (e.g. "New page" while editing).
watch(() => route.params.id, (next) => { if (next && next !== (id.value ?? 'new')) load(String(next)) })

// ---- Don't lose work ----------------------------------------------------------
const LEAVE_WARNING = 'You have unsaved changes. Leave without saving?'
const keepOrAsk = () => !dirty.value || confirm(LEAVE_WARNING)
onBeforeRouteLeave(() => keepOrAsk())
onBeforeRouteUpdate((to) => (to.params.id === (id.value ?? 'new') ? true : keepOrAsk()))

// Closing the tab or reloading: the browser shows its own wording.
function warnBeforeUnload(event: BeforeUnloadEvent) {
  if (!dirty.value) return
  event.preventDefault()
  event.returnValue = ''
}
window.addEventListener('beforeunload', warnBeforeUnload)
onBeforeUnmount(() => window.removeEventListener('beforeunload', warnBeforeUnload))

// ---- Saving ---------------------------------------------------------------------
async function save(publish: boolean, doneMessage: string) {
  busy.value = true
  error.value = ''
  message.value = ''
  const request: SavePageRequest = {
    title: title.value, path: path.value || null, body: body.value,
    showInMenu: showInMenu.value, order: Number(order.value) || 0, published: publish
  }
  try {
    const wasNew = id.value === null
    const page = id.value ? await editorApi.save(id.value, request) : await editorApi.create(request)
    show(page)
    message.value = doneMessage
    // The address under the editor follows the page: /write/new becomes /write/<id>.
    if (wasNew) await router.replace(`/write/${page.id}`)
    await menu.refresh()
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}

const saveDraft = () => save(false, 'Draft saved.')
const publish = () => save(true, 'Published.')
const saveChanges = () => save(true, 'Saved.')
const unpublish = () => save(false, 'Unpublished. Only authors can see it now.')

async function remove() {
  if (!id.value || !confirm(`Delete "${title.value}" for good?`)) return
  busy.value = true
  error.value = ''
  try {
    await editorApi.remove(id.value)
    saved.value = form() // nothing left to lose
    await menu.refresh()
    await router.replace('/write')
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}

const bodyLength = computed(() => body.value.length)
</script>

<template>
  <section class="editor">
    <div class="title-row">
      <h1>{{ id ? 'Edit page' : 'New page' }}</h1>
      <span v-if="id" class="badge" :class="published ? 'published' : 'draft-badge'">{{ published ? 'Published' : 'Draft' }}</span>
      <span v-if="dirty" class="muted unsaved">Unsaved changes</span>
      <RouterLink to="/write" class="back">All pages</RouterLink>
    </div>

    <p v-if="loading" class="muted">Loading…</p>
    <template v-else>
      <div class="fields">
        <label>Title
          <input v-model="title" :maxlength="MAX_TITLE" placeholder="About us" />
        </label>
        <label>Address
          <input v-model="path" maxlength="100" placeholder="made from the title if left empty" autocapitalize="none" spellcheck="false" />
        </label>
        <label class="inline"><input v-model="showInMenu" type="checkbox" /> Show in the menu</label>
        <label v-if="showInMenu" class="inline">Position <input v-model.number="order" type="number" step="1" class="short" /></label>
      </div>

      <div class="tabs" role="tablist">
        <button role="tab" :aria-selected="tab === 'write'" :class="{ active: tab === 'write' }" @click="tab = 'write'">Write</button>
        <button role="tab" :aria-selected="tab === 'preview'" :class="{ active: tab === 'preview' }" @click="tab = 'preview'">Preview</button>
      </div>

      <div class="panes" :data-tab="tab">
        <div class="pane pane-write">
          <textarea v-model="body" aria-label="Page text (markdown)" placeholder="Write in markdown…" spellcheck="true"></textarea>
          <small class="muted" :class="{ over: bodyLength > MAX_BODY }">{{ bodyLength.toLocaleString() }} / {{ MAX_BODY.toLocaleString() }}</small>
        </div>
        <div class="pane pane-preview" aria-label="Preview">
          <h2 v-if="title" class="preview-title">{{ title }}</h2>
          <MarkdownView v-if="body.trim()" :source="body" />
          <p v-else class="muted">Nothing to preview yet.</p>
        </div>
      </div>

      <p v-if="message" class="success" role="status">
        {{ message }}
        <RouterLink v-if="published && path" :to="`/${path}`">View the page</RouterLink>
      </p>
      <p v-if="error" class="error" role="alert">{{ error }}</p>

      <div class="buttons">
        <template v-if="published">
          <button class="button" :disabled="busy || !title.trim()" @click="saveChanges">Save</button>
          <button class="button secondary" :disabled="busy" @click="unpublish">Unpublish</button>
        </template>
        <template v-else>
          <button class="button secondary" :disabled="busy || !title.trim()" @click="saveDraft">Save draft</button>
          <button class="button" :disabled="busy || !title.trim()" @click="publish">Publish</button>
        </template>
        <button v-if="id" class="link danger" :disabled="busy" @click="remove">Delete</button>
      </div>
    </template>
  </section>
</template>
