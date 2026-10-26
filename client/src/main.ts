import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { useAuthStore } from './stores/auth'
import { useConfigStore } from './stores/config'
import './style.css'

// Start-up: learn about the deployment and who is signed in, then show the
// page. Doing both before mounting means no page ever flashes "signed out".
async function start() {
  const app = createApp(App)
  app.use(createPinia())

  await Promise.all([useConfigStore().load(), useAuthStore().load()])

  app.use(router)
  app.mount('#app')
}

start()
