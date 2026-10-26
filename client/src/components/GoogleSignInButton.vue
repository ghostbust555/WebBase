<script setup lang="ts">
// "Sign in with Google". Draws nothing at all unless the server says Google
// sign-in is on (Auth__Google__ClientId in .env.local) - and only then loads
// Google's script. See docs/GOOGLE_SIGNIN.md.
import { onMounted, ref } from 'vue'
import { useConfigStore } from '../stores/config'

const emit = defineEmits<{ credential: [string] }>()
const site = useConfigStore()
const target = ref<HTMLElement | null>(null)

interface GoogleIdApi {
  initialize(options: { client_id: string; callback: (r: { credential: string }) => void }): void
  renderButton(el: HTMLElement, options: Record<string, unknown>): void
}
declare global {
  interface Window { google?: { accounts: { id: GoogleIdApi } } }
}

let scriptLoading: Promise<void> | null = null
function loadScript(): Promise<void> {
  scriptLoading ??= new Promise((resolve, reject) => {
    const script = document.createElement('script')
    script.src = 'https://accounts.google.com/gsi/client'
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('Could not load Google sign-in'))
    document.head.appendChild(script)
  })
  return scriptLoading
}

onMounted(async () => {
  const clientId = site.config.googleClientId
  if (!clientId || !target.value) return
  await loadScript()
  const id = window.google!.accounts.id
  id.initialize({ client_id: clientId, callback: (r) => emit('credential', r.credential) })
  id.renderButton(target.value, { theme: 'outline', size: 'large', text: 'continue_with', width: 280 })
})
</script>

<template>
  <div v-if="site.config.googleClientId" class="google">
    <div ref="target"></div>
    <p class="divider"><span>or</span></p>
  </div>
</template>
