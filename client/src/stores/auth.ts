import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { authApi, getToken, setToken, whenSignedOut, type AuthResponse, type User } from '../api'

/**
 * Who is signed in, for the whole website. Any component can read
 * `useAuthStore().user` or call `signOut()`.
 *
 * The token lives in localStorage so a sign-in survives closing the tab. The
 * user is fetched fresh from /api/auth/me on load, so a deleted account or a
 * changed name never lingers.
 */
export const useAuthStore = defineStore('auth', () => {
  const user = ref<User | null>(null)
  const ready = ref(false)
  const signedIn = computed(() => user.value !== null)

  function accept(response: AuthResponse) {
    setToken(response.token)
    user.value = response.user
  }

  function signOut() {
    setToken(null)
    user.value = null
  }

  /** Run once at startup: restore the sign-in, and renew it if it is half used. */
  async function load() {
    whenSignedOut(signOut)
    if (getToken()) {
      try {
        user.value = await authApi.me()
        await renewIfHalfUsed()
      } catch {
        signOut()
      }
    }
    ready.value = true
  }

  async function renewIfHalfUsed() {
    const token = getToken()
    if (!token) return
    try {
      const payload = token.split('.')[1] ?? ''
      const claims = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')))
      const now = Date.now() / 1000
      if (now > claims.iat + (claims.exp - claims.iat) / 2) accept(await authApi.refresh())
    } catch { /* an unreadable token is replaced at the next sign-in */ }
  }

  return {
    user, ready, signedIn, load, signOut,
    login: async (email: string, password: string) => accept(await authApi.login(email, password)),
    register: async (email: string, password: string, displayName?: string) =>
      accept(await authApi.register(email, password, displayName)),
    google: async (credential: string) => accept(await authApi.google(credential)),
    updateName: async (name: string) => { user.value = await authApi.updateMe(name) }
  }
})
