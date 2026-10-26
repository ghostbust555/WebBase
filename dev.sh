#!/usr/bin/env bash
# The development site: your code, running live, with hot reload.
#
#   ./dev.sh                start it (or bring it up to date) and print the addresses
#   ./dev.sh logs [api|web|db]   watch what it's printing (Ctrl+C to stop watching)
#   ./dev.sh restart        restart everything (after editing .env.local)
#   ./dev.sh stop           stop it (your data is kept)
#   ./dev.sh status         what's running
#   ./dev.sh psql           a SQL prompt on the development database
#   ./dev.sh reset-db       DELETE the development database and start it fresh
. "$(dirname "$0")/scripts/lib.sh"
need_docker

WEB_PORT="$(env_value DEV_WEB_PORT)"; WEB_PORT="${WEB_PORT:-5173}"
API_PORT="$(env_value DEV_API_PORT)"; API_PORT="${API_PORT:-5000}"

case "${1:-up}" in
  up|start)
    say "Starting development (the first time downloads and builds things - a few minutes)"
    dc dev up -d
    say "Waiting for the site to answer"
    if ! wait_for_url "http://localhost:${WEB_PORT}/api/health" 600; then
      warn "It hasn't come up yet. See what's happening with: ./dev.sh logs"
      exit 1
    fi
    ok "Development is running"
    cat <<EOF

    Website     http://localhost:${WEB_PORT}
    API docs    http://localhost:${WEB_PORT}/scalar
    On your LAN http://$(hostname):${WEB_PORT}

    Edit files in client/src or src/ and the site updates by itself.
    ./dev.sh logs    to watch it      ./dev.sh stop    to stop it
EOF
    ;;
  logs)
    shift; dc dev logs -f --tail 100 "$@" ;;
  restart)
    dc dev up -d --force-recreate; ok "Restarted" ;;
  stop|down)
    dc dev down; ok "Development stopped. Your data is kept; ./dev.sh starts it again." ;;
  status|ps)
    dc dev ps ;;
  psql)
    dc dev exec db psql -U webbase -d webbase ;;
  reset-db)
    confirm "This deletes EVERY account and row in the development database. Continue?" || exit 1
    dc dev down
    # The data folder was written by the database container; remove it from inside one.
    docker run --rm -v "$ROOT/.docker-data:/d" alpine rm -rf /d/dev-postgres
    ok "Development database deleted. ./dev.sh starts with an empty one."
    ;;
  *)
    sed -n '2,12p' "$0"; exit 1 ;;
esac
