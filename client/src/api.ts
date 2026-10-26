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

// ---- Types: generated from the C# API (api-types.ts) ----
// Never written by hand: change the C# record and the TypeScript follows
// (./dev.sh rewrites api-types.ts; ./scripts/gen-types.sh otherwise). A field
// renamed in C# then fails `npm run build` wherever the website still uses it.

import type {
  AuthResponse, ContentPage, ContentSummary, CreateNoteRequest, GoogleSignInRequest, LoginRequest,
  NoteDto, RegisterRequest, SiteConfig, UpdateProfileRequest, UserDto
} from './api-types'

export type { AuthResponse, ContentPage, ContentSummary, SiteConfig }
export type User = UserDto
export type Note = NoteDto

// ---- Endpoints ----

export const configApi = {
  get: () => get<SiteConfig>('/api/config')
}

// Request bodies are checked against the C# request records with `satisfies`.
export const authApi = {
  register: (email: string, password: string, displayName?: string) =>
    post<AuthResponse>('/api/auth/register', { email, password, displayName } satisfies RegisterRequest),
  login: (email: string, password: string) =>
    post<AuthResponse>('/api/auth/login', { email, password } satisfies LoginRequest),
  google: (credential: string) =>
    post<AuthResponse>('/api/auth/google', { credential } satisfies GoogleSignInRequest),
  me: () => get<User>('/api/auth/me'),
  updateMe: (displayName: string) => put<User>('/api/auth/me', { displayName } satisfies UpdateProfileRequest),
  refresh: () => post<AuthResponse>('/api/auth/refresh')
}

// The optional CMS. See docs/CMS.md. (Its theme.css is linked in main.ts.)
export const contentApi = {
  pages: () => get<ContentSummary[]>('/api/content/pages'),
  page: (path: string) => get<ContentPage>(`/api/content/page?path=${encodeURIComponent(path)}`)
}

// The example feature. Delete along with NotesView.vue when you remove it.
export const notesApi = {
  list: () => get<Note[]>('/api/notes'),
  create: (text: string) => post<Note>('/api/notes', { text } satisfies CreateNoteRequest),
  remove: (id: string) => del<void>(`/api/notes/${id}`)
}
