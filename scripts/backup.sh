#!/usr/bin/env bash
# Save a copy of a database to backups/.
#
#   ./scripts/backup.sh            prod (the default)
#   ./scripts/backup.sh dev        development
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

say "Backing up ${ENVIRONMENT}"
# --clean: the file drops and recreates everything, so restoring it gives
# exactly this state, whatever the database holds at the time.
dc "$ENVIRONMENT" exec -T db pg_dump -U "$USER_NAME" -d "$SLUG" --clean --if-exists | gzip > "$FILE"
[ -s "$FILE" ] || die "The backup came out empty - is ${ENVIRONMENT} running?"
ok "Saved ${FILE} ($(du -h "$FILE" | cut -f1))"
