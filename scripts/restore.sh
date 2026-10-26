#!/usr/bin/env bash
# Put a database back to the state in a backup. REPLACES what's there now.
#
#   ./scripts/restore.sh backups/webbase-prod-20261025-030000.sql.gz          into prod
#   ./scripts/restore.sh backups/webbase-prod-20261025-030000.sql.gz dev      into development
#
# Prod means its managed database if PROD_DATABASE_URL is set. That's also how
# to move to one: back up the container, set PROD_DATABASE_URL, restore.
. "$(dirname "$0")/lib.sh"
need_docker

FILE="${1:-}"; ENVIRONMENT="${2:-prod}"
[ -f "$FILE" ] || die "Usage: ./scripts/restore.sh <backup file> [prod|dev]"
case "$ENVIRONMENT" in
  prod) USER_NAME="${SLUG}_owner" ;;
  dev)  USER_NAME="${SLUG}" ;;
  *)    die "The environment is prod or dev." ;;
esac

[ "$( (gunzip -c "$FILE" 2>/dev/null || true) | grep -c "PostgreSQL database dump complete")" -gt 0 ] \
  || die "${FILE} isn't a complete backup (damaged, or from a backup that failed). Nothing was changed."

warn "This replaces EVERYTHING in the ${ENVIRONMENT} database with ${FILE}."
confirm "Continue?" || exit 1

if [ "$ENVIRONMENT" = prod ]; then
  say "Taking a safety backup of prod first"
  ./scripts/backup.sh prod
fi

say "Restoring"
URL=""
[ "$ENVIRONMENT" = prod ] && URL="$(env_value PROD_DATABASE_URL)"
if [ -n "$URL" ]; then
  gunzip -c "$FILE" | docker run --rm -i "$PG_IMAGE" psql -q -v ON_ERROR_STOP=1 "$URL" >/dev/null
else
  gunzip -c "$FILE" | dc "$ENVIRONMENT" exec -T db psql -q -v ON_ERROR_STOP=1 -U "$USER_NAME" -d "$SLUG" >/dev/null
fi
ok "Restored ${ENVIRONMENT} from ${FILE}"
