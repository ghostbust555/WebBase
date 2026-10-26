<script setup lang="ts">
import { useRoute } from 'vue-router'
import DocsReader from '../components/DocsReader.vue'

const route = useRoute()
</script>

<template>
  <DocsReader :name="String(route.params.name || 'GETTING_STARTED')" />
</template>
