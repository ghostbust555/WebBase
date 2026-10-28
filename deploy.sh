#!/usr/bin/env bash
# Production on this computer: build a release and run it.
#
#   ./deploy.sh             build what's in this folder now and switch prod to it
#   ./deploy.sh rollback    switch prod back to the release before
#   ./deploy.sh status      which release is running
#   ./deploy.sh logs        watch prod's output (Ctrl+C to stop watching)
#   ./deploy.sh restart     restart prod (after editing .env.local)
#   ./deploy.sh stop        stop prod (data is kept; the tunnel stops too)
#
# A release is a Docker image. Every build is kept under its own name
# (webbase-app:<build id>), "prod" is whichever is live, and "previous" is the
# one before - so a bad release is one command away from undone. If a new
# release doesn't come up healthy, this switches back by itself.
#
# For running in a cloud instead, see docs/CLOUD.md.
. "$(dirname "$0")/scripts/lib.sh"
need_docker

IMAGE="${SLUG}-app"
PORT="$(env_value PROD_PORT)"; PORT="${PORT:-8080}"
HEALTH="http://127.0.0.1:${PORT}/api/health"

image_exists() { docker image inspect "$1" >/dev/null 2>&1; }
build_of() { docker image inspect --format '{{index .Config.Labels "build"}}' "$1" 2>/dev/null || true; }

check_settings() {
  [ -f .env.local ] || die "No .env.local yet. Run ./setup.sh first."
  local keys=(JWT_KEY)
  # The passwords are for the database container; a managed database has its own.
  [ -z "$(env_value PROD_DATABASE_URL)" ] && keys+=(POSTGRES_PASSWORD POSTGRES_APP_PASSWORD)
  for key in "${keys[@]}"; do
    [ -n "$(env_value "$key")" ] || die "$key is empty in .env.local. Run ./setup.sh to generate it."
  done
}

start_prod() {
  # --remove-orphans: after switching to a managed database, stop the old
  # database container (its data stays in .docker-data/).
  dc prod up -d --remove-orphans app
  say "Waiting for prod to report healthy"
  wait_for_url "$HEALTH" 90
}

case "${1:-release}" in
  release|deploy)
    check_settings
    BUILD="$(build_id)"
    say "Building release ${BUILD}"
    docker build -f ops/Dockerfile --build-arg "BUILD_ID=${BUILD}" --build-arg "BUILD_TIME=$(build_time)" --label "build=${BUILD}" \
      -t "${IMAGE}:${BUILD}" .

    if image_exists "${IMAGE}:prod"; then docker tag "${IMAGE}:prod" "${IMAGE}:previous"; fi
    docker tag "${IMAGE}:${BUILD}" "${IMAGE}:prod"

    if start_prod; then
      ok "Prod is running ${BUILD}"
      echo "    http://localhost:${PORT}   (this computer)"
      if [ -n "$(env_value PROD_DATABASE_URL)" ]; then echo "    database: managed (PROD_DATABASE_URL)"
      else echo "    database: the container on this computer - back it up: ./scripts/backup.sh"; fi
      [ -n "$(env_value CLOUDFLARE_TUNNEL_TOKEN)" ] && echo "    ./tunnel.sh status  to check the public address"
    else
      warn "Release ${BUILD} did not come up healthy. Its last words:"
      dc prod logs --tail 30 app >&2 || true
      if image_exists "${IMAGE}:previous"; then
        warn "Switching back to $(build_of "${IMAGE}:previous")"
        docker tag "${IMAGE}:previous" "${IMAGE}:prod"
        start_prod && ok "Back on the previous release" || warn "The previous release isn't healthy either - see ./deploy.sh logs"
      fi
      exit 1
    fi
    # Old build layers pile up otherwise. Tagged images (every release) are kept.
    docker image prune -f >/dev/null 2>&1 || true
    ;;

  rollback)
    image_exists "${IMAGE}:previous" || die "There is no previous release to go back to."
    CURRENT="$(build_of "${IMAGE}:prod")"
    docker tag "${IMAGE}:previous" "${IMAGE}:prod"
    # Swap, so running rollback again undoes the rollback.
    [ -n "$CURRENT" ] && docker tag "${IMAGE}:${CURRENT}" "${IMAGE}:previous" 2>/dev/null || true
    start_prod && ok "Prod is back on $(build_of "${IMAGE}:prod")" || die "Not healthy - see ./deploy.sh logs"
    ;;

  status)
    if curl -fsS -o /dev/null --max-time 2 "$HEALTH" 2>/dev/null; then
      ok "Prod is up: $(build_of "${IMAGE}:prod") at http://localhost:${PORT}"
    else
      warn "Prod is not answering at http://localhost:${PORT}"
    fi
    image_exists "${IMAGE}:previous" && echo "    previous release: $(build_of "${IMAGE}:previous")"
    echo "    releases kept: $(docker images --format '{{.Tag}}' "${IMAGE}" | grep -vcE '^(prod|previous)$') (docker images ${IMAGE})"
    dc prod ps
    ;;

  logs)
    shift; dc prod logs -f --tail 100 "${@:-app}" ;;
  restart)
    check_settings; start_prod && ok "Restarted" || die "Not healthy - see ./deploy.sh logs" ;;
  stop|down)
    dc prod --profile tunnel down; ok "Prod stopped. Data is kept; ./deploy.sh restart starts it again." ;;
  *)
    sed -n '2,10p' "$0"; exit 1 ;;
esac
