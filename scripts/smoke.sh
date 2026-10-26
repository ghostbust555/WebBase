#!/usr/bin/env bash
# Is a running copy actually working? Signs up a throwaway account, uses it,
# and checks the answers. Run it after starting or deploying anything.
#
#   ./scripts/smoke.sh                                  development (localhost:5173)
#   ./scripts/smoke.sh http://localhost:8080            prod on this computer
#   ./scripts/smoke.sh https://example.com              anywhere
#
# It leaves one test account behind (smoke-...@example.com).
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

EMAIL="smoke-$(date +%s)-$RANDOM@example.com"
REGISTER="$(json -X POST "$BASE/api/auth/register" -d "{\"email\":\"$EMAIL\",\"password\":\"smoke-test-password\",\"displayName\":\"Smoke\"}")"
check "register" '"token"' "$REGISTER"
TOKEN="$(printf '%s' "$REGISTER" | sed -E 's/.*"token":"([^"]+)".*/\1/')"
AUTH=(-H "Authorization: Bearer $TOKEN")

check "login"             '"token"'       "$(json -X POST "$BASE/api/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"smoke-test-password\"}")"
check "wrong password"    '401'           "$(curl -sS -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -X POST "$BASE/api/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"nope-nope-nope\"}")"
check "me"                "\"$EMAIL\""    "$(json "${AUTH[@]}" "$BASE/api/auth/me")"
check "me without token"  '401'           "$(curl -sS -o /dev/null -w '%{http_code}' "$BASE/api/auth/me")"
NOTE="$(json "${AUTH[@]}" -X POST "$BASE/api/notes" -d '{"text":"smoke test"}')"
check "create note"       '"smoke test"'  "$NOTE"
check "list notes"        '"smoke test"'  "$(json "${AUTH[@]}" "$BASE/api/notes")"
NOTE_ID="$(printf '%s' "$NOTE" | sed -E 's/.*"id":"([^"]+)".*/\1/')"
check "delete note"       '204'           "$(curl -sS -o /dev/null -w '%{http_code}' "${AUTH[@]}" -X DELETE "$BASE/api/notes/$NOTE_ID")"

echo
[ "$failures" -eq 0 ] && ok "Everything works." || die "${failures} check(s) failed."
