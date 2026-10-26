// Every call the website makes to the C# API goes through this file.
//
// Paths are relative ("/api/notes"), so the browser always asks the same
// address the page came from. In development Vite forwards them to the API;
// in prod the API is that address. Either way there is no CORS to configure.

const TOKEN_KEY = 'auth.token'

export function getToken(): string | null {
  try { return localStorage.getItem(TOKEN_KEY) } catch { return null }
}
export function setToken(token: string | null) {
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token)
    else localStorage.removeItem(TOKEN_KEY)
  } catch { /* private mode: the sign-in lasts until the tab closes */ }
}

/** Thrown for any failed call. `message` is always fit to show a person. */
export class ApiError extends Error {
  constructor(message: string, public status: number) { super(message) }
}

/** Called when the server says our token is no good (set by the auth store). */
let onSignedOut: () => void = () => {}
export function whenSignedOut(handler: () => void) { onSignedOut = handler }

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const token = getToken()
  if (token) headers.Authorization = `Bearer ${token}`

  let response: Response
  try {
    response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  } catch {
    throw new ApiError('Cannot reach the server. Check that it is running.', 0)
  }

  if (response.status === 401 && token && !path.startsWith('/api/auth/')) onSignedOut()

  if (!response.ok) {
    // The API always answers errors as { "error": "..." }.
    let message = `Something went wrong (${response.status}).`
    try {
      const data = await response.json()
      if (typeof data?.error === 'string') message = data.error
    } catch { /* not JSON */ }
    if (response.status === 401 && message.startsWith('Something')) message = 'Please sign in.'
    throw new ApiError(message, response.status)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

const get = <T>(path: string) => request<T>('GET', path)
const post = <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {})
const put = <T>(path: string, body: unknown) => request<T>('PUT', path, body)
const del = <T>(path: string) => request<T>('DELETE', path)

// ---- Types: the same shapes as src/WebBase.Api/Contracts/*.cs ----

export interface User {
  id: string
  email: string
  displayName: string
  hasPassword: boolean
  hasGoogle: boolean
  createdAtUtc: string
}

export interface AuthResponse {
  token: string
  user: User
}

export interface SiteConfig {
  name: string
  environment: 'development' | 'prod'
  build: string
  googleClientId: string | null
  /** Whether the file-based CMS is switched on (CMS_ENABLED). */
  cms: boolean
}

/** A CMS page in a list or menu. */
export interface ContentSummary {
  path: string
  title: string
  description: string | null
  date: string | null
  order: number | null
  menu: boolean
}

/** A whole CMS page. `children` is set when the page is a folder (blog/ → its posts). */
export interface ContentPage extends ContentSummary {
  draft: boolean
  markdown: string
  children: ContentSummary[] | null
}

export interface Note {
  id: string
  text: string
  createdAtUtc: string
}

// ---- Endpoints ----

export const configApi = {
  get: () => get<SiteConfig>('/api/config')
}

export const authApi = {
  register: (email: string, password: string, displayName?: string) =>
    post<AuthResponse>('/api/auth/register', { email, password, displayName }),
  login: (email: string, password: string) =>
    post<AuthResponse>('/api/auth/login', { email, password }),
  google: (credential: string) =>
    post<AuthResponse>('/api/auth/google', { credential }),
  me: () => get<User>('/api/auth/me'),
  updateMe: (displayName: string) => put<User>('/api/auth/me', { displayName }),
  refresh: () => post<AuthResponse>('/api/auth/refresh')
}

// The optional CMS (content/ folder). See docs/CMS.md.
export const contentApi = {
  pages: () => get<ContentSummary[]>('/api/content/pages'),
  page: (path: string) => get<ContentPage>(`/api/content/page?path=${encodeURIComponent(path)}`)
}

// The example feature. Delete along with NotesView.vue when you remove it.
export const notesApi = {
  list: () => get<Note[]>('/api/notes'),
  create: (text: string) => post<Note>('/api/notes', { text }),
  remove: (id: string) => del<void>(`/api/notes/${id}`)
}
