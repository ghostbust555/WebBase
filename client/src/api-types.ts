// GENERATED from the C# API's controllers by Infrastructure/TypeScriptContracts.cs.
// Don't edit: change the C# types instead. ./dev.sh keeps this file up to date,
// or run ./scripts/gen-types.sh. A test fails while it's out of date.

/** C# WebBase.Api.Contracts.AuthResponse */
export interface AuthResponse {
  token: string
  user: UserDto
}

/** C# WebBase.Api.Content.ContentPage */
export interface ContentPage {
  path: string
  title: string
  description: string | null
  date: string | null
  order: number | null
  menu: boolean
  draft: boolean
  markdown: string
  children: ContentSummary[] | null
}

/** C# WebBase.Api.Content.ContentSummary */
export interface ContentSummary {
  path: string
  title: string
  description: string | null
  date: string | null
  order: number | null
  menu: boolean
}

/** C# WebBase.Api.Contracts.CreateNoteRequest */
export interface CreateNoteRequest {
  text: string
}

/** C# WebBase.Api.Contracts.ErrorResponse */
export interface ErrorResponse {
  error: string
}

/** C# WebBase.Api.Contracts.GoogleSignInRequest */
export interface GoogleSignInRequest {
  credential: string
}

/** C# WebBase.Api.Contracts.HealthResponse */
export interface HealthResponse {
  status: string
  database: boolean
}

/** C# WebBase.Api.Contracts.LoginRequest */
export interface LoginRequest {
  email: string
  password: string
}

/** C# WebBase.Api.Contracts.NoteDto */
export interface NoteDto {
  id: string
  text: string
  createdAtUtc: string
}

/** C# WebBase.Api.Contracts.RegisterRequest */
export interface RegisterRequest {
  email: string
  password: string
  displayName?: string | null
}

/** C# WebBase.Api.Contracts.SiteConfig */
export interface SiteConfig {
  name: string
  environment: string
  build: string
  googleClientId: string | null
  cms: boolean
}

/** C# WebBase.Api.Contracts.UpdateProfileRequest */
export interface UpdateProfileRequest {
  displayName: string
}

/** C# WebBase.Api.Contracts.UserDto */
export interface UserDto {
  id: string
  email: string
  displayName: string
  hasPassword: boolean
  hasGoogle: boolean
  createdAtUtc: string
}
