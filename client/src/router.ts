import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from './stores/auth'

// Every page of the website, by address. Add a page: make a view in views/,
// add a line here, and (if it should be in the menu) a link in App.vue.
//
// `meta: { requiresAuth: true }` sends anyone not signed in to /login first,
// and brings them back afterwards. `requiresAuthor: true` includes that, and sends a
// signed-in user who is not an author to the home page (docs/AUTHORS.md). Both are conveniences, not
// security: the API checks on every request ([Authorize], [RequireAuthor]).
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: () => import('./views/HomeView.vue') },
    { path: '/login', component: () => import('./views/LoginView.vue') },
    { path: '/register', component: () => import('./views/RegisterView.vue') },
    { path: '/account', component: () => import('./views/AccountView.vue'), meta: { requiresAuth: true } },
    { path: '/notes', component: () => import('./views/NotesView.vue'), meta: { requiresAuth: true } },
    // Authors write the site's own pages in the browser (docs/PAGES.md). One route for the
    // editor, "new" or an id, so saving a new page just changes the address under it.
    { path: '/write', component: () => import('./views/PagesView.vue'), meta: { requiresAuthor: true } },
    { path: '/write/:id', component: () => import('./views/PageEditorView.vue'), meta: { requiresAuthor: true } },
    { path: '/authors', component: () => import('./views/AuthorsView.vue'), meta: { requiresAuthor: true } },
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
  if ((to.meta.requiresAuth || to.meta.requiresAuthor) && !auth.signedIn) {
    return { path: '/login', query: { next: to.fullPath } }
  }
  if (to.meta.requiresAuthor && !auth.user?.isAuthor) return { path: '/' }
})

export default router
