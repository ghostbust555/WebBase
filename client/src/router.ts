import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from './stores/auth'

// Every page of the website, by address. Add a page: make a view in views/,
// add a line here, and (if it should be in the menu) a link in App.vue.
//
// `meta: { requiresAuth: true }` sends anyone not signed in to /login first,
// and brings them back afterwards. That is a convenience, not security: the
// API checks the token on every request ([Authorize] in the controllers).
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: () => import('./views/HomeView.vue') },
    { path: '/login', component: () => import('./views/LoginView.vue') },
    { path: '/register', component: () => import('./views/RegisterView.vue') },
    { path: '/account', component: () => import('./views/AccountView.vue'), meta: { requiresAuth: true } },
    { path: '/notes', component: () => import('./views/NotesView.vue'), meta: { requiresAuth: true } },
    // The guides in docs/, readable in the browser.
    { path: '/docs/:name?', component: () => import('./views/DocsView.vue') },
    // Everything else: a CMS page from content/ if the CMS is on and has one,
    // otherwise "page not found".
    { path: '/:pathMatch(.*)*', component: () => import('./views/ContentView.vue') }
  ],
  // Back/forward returns to where you were; a new page starts at the top. A
  // "#section" link is left to the page, which scrolls once its text is there
  // (components/MarkdownView.vue).
  scrollBehavior: (to, _from, saved) => saved ?? (to.hash ? false : { top: 0 })
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (to.meta.requiresAuth && !auth.signedIn) {
    return { path: '/login', query: { next: to.fullPath } }
  }
})

export default router
