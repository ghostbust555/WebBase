#!/usr/bin/env bash
# Is a running copy actually working? Signs up a throwaway account, uses it,
# and checks the answers. Run it after starting or deploying anything.
#
#   ./scripts/smoke.sh                                  development (localhost:5173)
#   ./scripts/smoke.sh http://localhost:8080            prod on this computer
#   ./scripts/smoke.sh https://example.com              anywhere
#
# It leaves two test accounts behind (smoke-...@example.com, with random passwords).
#
# Careful on a brand-new real site: the FIRST account ever created is an author
# (docs/AUTHORS.md), and this script creates accounts. Sign up yourself first.
. "$(dirname "$0")/lib.sh"

BASE="${1:-http://localhost:$(env_value DEV_WEB_PORT || true)}"
[ "$BASE" = "http://localhost:" ] && BASE="http://localhost:5173"
BASE="${BASE%/}"
failures=0

check() { # name, expected text, actual text
  if [[ "$3" == *"$2"* ]]; then ok "$1"; else warn "$1 - expected '$2', got: ${3:0:200}"; failures=$((failures + 1)); fi
}
json() { curl -sS --max-time 10 -H 'Content-Type: application/json' "$@"; }

say "Checking ${BASE}"
check "health"            '"status":"ok"'   "$(json "$BASE/api/health")"
check "config"            '"environment"'   "$(json "$BASE/api/config")"
check "website served"    '<div id="app">'  "$(curl -sS --max-time 10 "$BASE/")"
check "deep link served"  '<div id="app">'  "$(curl -sS --max-time 10 "$BASE/account")"
check "unknown API is 404" '404'            "$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "$BASE/api/nope")"

STAMP="$(date +%s)-$RANDOM"
EMAIL="smoke-$STAMP@example.com"
PASSWORD="$(random_secret | cut -c1-24)"
REGISTER="$(json -X POST "$BASE/api/auth/register" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"displayName\":\"Smoke\"}")"
check "register" '"token"' "$REGISTER"
TOKEN="$(printf '%s' "$REGISTER" | sed -E 's/.*"token":"([^"]+)".*/\1/')"
AUTH=(-H "Authorization: Bearer $TOKEN")

check "login"             '"token"'       "$(json -X POST "$BASE/api/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")"
check "wrong password"    '401'           "$(curl -sS -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -X POST "$BASE/api/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"nope-nope-nope\"}")"
check "me"                "\"$EMAIL\""    "$(json "${AUTH[@]}" "$BASE/api/auth/me")"
check "me without token"  '401'           "$(curl -sS -o /dev/null -w '%{http_code}' "$BASE/api/auth/me")"
# --- The example feature (docs/EXAMPLE_NOTES.md: delete these lines with it) ---
NOTE="$(json "${AUTH[@]}" -X POST "$BASE/api/notes" -d '{"text":"smoke test"}')"
check "create note"       '"smoke test"'  "$NOTE"
check "list notes"        '"smoke test"'  "$(json "${AUTH[@]}" "$BASE/api/notes")"
NOTE_ID="$(printf '%s' "$NOTE" | sed -E 's/.*"id":"([^"]+)".*/\1/')"
check "delete note"       '204'           "$(curl -sS -o /dev/null -w '%{http_code}' "${AUTH[@]}" -X DELETE "$BASE/api/notes/$NOTE_ID")"
# --- end of the example feature ---

# --- Authors and the authors' pages (docs/AUTHORS.md, docs/PAGES.md) ---
code() { curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "$@"; }
check "pages: menu is public"       '['     "$(json "$BASE/api/pages/menu")"
check "pages: unknown page is 404"  '404'   "$(code "$BASE/api/pages/published?path=smoke-no-such-page")"
check "editor: signed out is 401"   '401'   "$(code "$BASE/api/editor/pages")"

# A second account is never the first, so never an author: the editor must refuse it.
OTHER="$(json -X POST "$BASE/api/auth/register" -d "{\"email\":\"smoke-other-$STAMP@example.com\",\"password\":\"$PASSWORD\"}")"
OTHER_TOKEN="$(printf '%s' "$OTHER" | sed -E 's/.*"token":"([^"]+)".*/\1/')"
check "editor: a non-author is 403" '403'   "$(code -H "Authorization: Bearer $OTHER_TOKEN" "$BASE/api/editor/pages")"
check "editor: not for writing either" '403' "$(code -H "Authorization: Bearer $OTHER_TOKEN" -H 'Content-Type: application/json' -X POST "$BASE/api/editor/pages" -d '{"title":"x","body":"x","showInMenu":false,"order":0,"published":true}')"
check "authors: a non-author is 403" '403'  "$(code -H "Authorization: Bearer $OTHER_TOKEN" "$BASE/api/authors")"

if [[ "$REGISTER" == *'"isAuthor":true'* ]]; then
  warn "The smoke account became the site's first author (nobody had signed up yet)."
  SLUG_PATH="smoke-page-$STAMP"
  PAGE="$(json "${AUTH[@]}" -X POST "$BASE/api/editor/pages" -d "{\"title\":\"Smoke page\",\"path\":\"$SLUG_PATH\",\"body\":\"# Hello\",\"showInMenu\":true,\"order\":99,\"published\":false}")"
  check "editor: create a draft"        "\"path\":\"$SLUG_PATH\"" "$PAGE"
  PAGE_ID="$(printf '%s' "$PAGE" | sed -E 's/.*"id":"([^"]+)".*/\1/')"
  check "pages: a draft is hidden"      '404'   "$(code "$BASE/api/pages/published?path=$SLUG_PATH")"
  check "editor: publish"               '"published":true' "$(json "${AUTH[@]}" -X PUT "$BASE/api/editor/pages/$PAGE_ID" -d "{\"title\":\"Smoke page\",\"path\":\"$SLUG_PATH\",\"body\":\"# Hello\",\"showInMenu\":true,\"order\":99,\"published\":true}")"
  check "pages: published is visible"   '# Hello' "$(json "$BASE/api/pages/published?path=$SLUG_PATH")"
  check "pages: and in the menu"        "$SLUG_PATH" "$(json "$BASE/api/pages/menu")"
  check "editor: duplicate path is 409" '409'   "$(code -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -X POST "$BASE/api/editor/pages" -d "{\"title\":\"Again\",\"path\":\"$SLUG_PATH\",\"body\":\"\",\"showInMenu\":false,\"order\":0,\"published\":false}")"
  check "editor: reserved path refused" '400'   "$(code -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -X POST "$BASE/api/editor/pages" -d '{"title":"Login","path":"login","body":"","showInMenu":false,"order":0,"published":false}')"
  check "editor: unpublish"             '"published":false' "$(json "${AUTH[@]}" -X PUT "$BASE/api/editor/pages/$PAGE_ID" -d "{\"title\":\"Smoke page\",\"path\":\"$SLUG_PATH\",\"body\":\"# Hello\",\"showInMenu\":true,\"order\":99,\"published\":false}")"
  check "pages: unpublished is hidden"  '404'   "$(code "$BASE/api/pages/published?path=$SLUG_PATH")"
  check "editor: delete"                '204'   "$(code "${AUTH[@]}" -X DELETE "$BASE/api/editor/pages/$PAGE_ID")"
  check "authors: an author can list"   "\"$EMAIL\"" "$(json "${AUTH[@]}" "$BASE/api/authors")"
  check "authors: can't remove yourself" '400'  "$(code "${AUTH[@]}" -X DELETE "$BASE/api/authors/$(printf '%s' "$REGISTER" | sed -E 's/.*"user":\{"id":"([^"]+)".*/\1/')")"
else
  ok "(Authoring checks skipped: the smoke account isn't an author, because the site already had accounts. Use an author's sign-in to try the editor.)"
fi

echo
[ "$failures" -eq 0 ] && ok "Everything works." || die "${failures} check(s) failed."
