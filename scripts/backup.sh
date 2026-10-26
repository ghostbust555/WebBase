#!/usr/bin/env bash
# Save a copy of a database to backups/.
#
#   ./scripts/backup.sh            prod (the default)
#   ./scripts/backup.sh dev        development
#
# Prod means its managed database if PROD_DATABASE_URL is set, otherwise the
# database container.
#
# Each backup is one compressed SQL file, named by date. Copy them somewhere
# other than this computer - a backup on the same disk dies with the disk.
# Put it on a schedule with cron:  0 3 * * *  /path/to/project/scripts/backup.sh
. "$(dirname "$0")/lib.sh"
need_docker

ENVIRONMENT="${1:-prod}"
case "$ENVIRONMENT" in
  prod) USER_NAME="${SLUG}_owner" ;;
  dev)  USER_NAME="${SLUG}" ;;
  *)    die "Usage: ./scripts/backup.sh [prod|dev]" ;;
esac

mkdir -p backups
FILE="backups/${SLUG}-${ENVIRONMENT}-$(date +%Y%m%d-%H%M%S).sql.gz"
# Written under a temporary name and renamed only once it's complete, so a
# failed backup never leaves a file that looks like a good one.
PARTIAL="${FILE}.partial"
trap 'rm -f "$PARTIAL"' EXIT

say "Backing up ${ENVIRONMENT}"
# --clean: the file drops and recreates everything, so restoring it gives
# exactly this state, whatever the database holds at the time.
# --no-owner --no-privileges: it restores into any database, e.g. from the
# container here into a managed one (whose users have other names).
URL=""
[ "$ENVIRONMENT" = prod ] && URL="$(env_value PROD_DATABASE_URL)"
if [ -n "$URL" ]; then
  # A managed database: its provider takes backups too, but a copy of your
  # own costs nothing.
  docker run --rm -i "$PG_IMAGE" pg_dump "$URL" --clean --if-exists --no-owner --no-privileges | gzip > "$PARTIAL"
else
  dc "$ENVIRONMENT" exec -T db pg_dump -U "$USER_NAME" -d "$SLUG" --clean --if-exists --no-owner --no-privileges | gzip > "$PARTIAL"
fi
# grep -c reads to the end (grep -q would stop early and upset pipefail).
[ "$(gunzip -c "$PARTIAL" | grep -c "PostgreSQL database dump complete")" -gt 0 ] \
  || die "The backup came out incomplete - is the ${ENVIRONMENT} database running and reachable?"
mv "$PARTIAL" "$FILE"
ok "Saved ${FILE} ($(du -h "$FILE" | cut -f1))"
