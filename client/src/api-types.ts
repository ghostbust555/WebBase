// GENERATED from the C# API's controllers by Infrastructure/TypeScriptContracts.cs.
// Don't edit: change the C# types instead. ./dev.sh keeps this file up to date,
// or run ./scripts/gen-types.sh. A test fails while it's out of date.

/** C# WebBase.Api.Contracts.AddAuthorRequest */
export interface AddAuthorRequest {
  email: string
}

/** C# WebBase.Api.Contracts.AuthResponse */
export interface AuthResponse {
  token: string
  user: UserDto
}

/** C# WebBase.Api.Contracts.AuthorDto */
export interface AuthorDto {
  id: string
  email: string
  displayName: string
  createdAtUtc: string
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

/** C# WebBase.Api.Contracts.MenuItemDto */
export interface MenuItemDto {
  path: string
  title: string
  order: number
}

/** C# WebBase.Api.Contracts.NoteDto */
export interface NoteDto {
  id: string
  text: string
  createdAtUtc: string
}

/** C# WebBase.Api.Contracts.PageDto */
export interface PageDto {
  id: string
  path: string
  title: string
  body: string
  showInMenu: boolean
  order: number
  published: boolean
  publishedAtUtc: string | null
  createdAtUtc: string
  updatedAtUtc: string
  updatedById: string
}

/** C# WebBase.Api.Contracts.PageSummaryDto */
export interface PageSummaryDto {
  id: string
  path: string
  title: string
  showInMenu: boolean
  order: number
  published: boolean
  updatedAtUtc: string
}

/** C# WebBase.Api.Contracts.PublicPageDto */
export interface PublicPageDto {
  path: string
  title: string
  markdown: string
  publishedAtUtc: string
  updatedAtUtc: string
}

/** C# WebBase.Api.Contracts.RegisterRequest */
export interface RegisterRequest {
  email: string
  password: string
  displayName?: string | null
}

/** C# WebBase.Api.Contracts.SavePageRequest */
export interface SavePageRequest {
  title: string
  path?: string | null
  body: string
  showInMenu: boolean
  order: number
  published: boolean
}

/** C# WebBase.Api.Contracts.SiteConfig */
export interface SiteConfig {
  name: string
  environment: string
  build: string
  googleClientId: string | null
  cms: boolean
  builtAtUtc: string | null
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
  isAuthor: boolean
  createdAtUtc: string
}
