#!/usr/bin/env bash
# Put a database back to the state in a backup. REPLACES what's there now.
#
#   ./scripts/restore.sh backups/webbase-prod-20261025-030000.sql.gz          into prod
#   ./scripts/restore.sh backups/webbase-prod-20261025-030000.sql.gz dev      into development
. "$(dirname "$0")/lib.sh"
need_docker

FILE="${1:-}"; ENVIRONMENT="${2:-prod}"
[ -f "$FILE" ] || die "Usage: ./scripts/restore.sh <backup file> [prod|dev]"
case "$ENVIRONMENT" in
  prod) USER_NAME="${SLUG}_owner" ;;
  dev)  USER_NAME="${SLUG}" ;;
  *)    die "The environment is prod or dev." ;;
esac

warn "This replaces EVERYTHING in the ${ENVIRONMENT} database with ${FILE}."
confirm "Continue?" || exit 1

if [ "$ENVIRONMENT" = prod ]; then
  say "Taking a safety backup of prod first"
  ./scripts/backup.sh prod
fi

say "Restoring"
gunzip -c "$FILE" | dc "$ENVIRONMENT" exec -T db psql -q -v ON_ERROR_STOP=1 -U "$USER_NAME" -d "$SLUG" >/dev/null
ok "Restored ${ENVIRONMENT} from ${FILE}"
