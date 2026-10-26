<script setup lang="ts">
// The guides in docs/, with a menu of all of them beside the one being read.
import { computed } from 'vue'
import MarkdownView from './MarkdownView.vue'
import NotFoundView from '../views/NotFoundView.vue'
import { docs, findDoc, resolveDocLink } from '../lib/docs'

const props = defineProps<{ name: string }>()
const doc = computed(() => findDoc(props.name))
</script>

<template>
  <div v-if="doc" class="docs">
    <nav class="docs-menu" aria-label="Guides">
      <RouterLink v-for="d in docs" :key="d.name" :to="`/docs/${d.name}`"
                  :class="{ active: d.name === doc.name }">{{ d.title }}</RouterLink>
    </nav>
    <MarkdownView :source="doc.source" :resolve="resolveDocLink" />
  </div>
  <NotFoundView v-else />
</template>
