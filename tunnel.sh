#!/usr/bin/env bash
# Put prod on your own domain through a Cloudflare Tunnel.
#
#   ./tunnel.sh on        go public (prod must be running: ./deploy.sh)
#   ./tunnel.sh off       stop being public
#   ./tunnel.sh status    is it connected?
#   ./tunnel.sh logs      watch the tunnel's output
#
# One-time setup (a token from Cloudflare's dashboard into .env.local) is in
# docs/CLOUDFLARE_TUNNEL.md. The tunnel dials out to Cloudflare, so nothing on
# your router needs opening, and only prod is ever exposed - never development.
. "$(dirname "$0")/scripts/lib.sh"
need_docker

PORT="$(env_value PROD_PORT)"; PORT="${PORT:-8080}"

container() { dc prod --profile tunnel ps -q cloudflared 2>/dev/null; }
connected() {
  local id; id="$(container)"
  [ -n "$id" ] && docker logs "$id" 2>&1 | grep -q "Registered tunnel connection"
}

case "${1:-status}" in
  on|up)
    [ -n "$(env_value CLOUDFLARE_TUNNEL_TOKEN)" ] \
      || die "CLOUDFLARE_TUNNEL_TOKEN is empty in .env.local. Follow docs/CLOUDFLARE_TUNNEL.md to get one."
    curl -fsS -o /dev/null --max-time 2 "http://127.0.0.1:${PORT}/api/health" \
      || die "Prod isn't running, so there's nothing to make public. Run ./deploy.sh first."
    dc prod --profile tunnel up -d cloudflared
    say "Connecting to Cloudflare"
    for _ in $(seq 1 30); do connected && break; sleep 1; done
    if connected; then
      ok "The tunnel is connected. Your domain now reaches prod."
    else
      warn "Not connected yet. ./tunnel.sh logs shows why (a wrong token is the usual reason)."
      exit 1
    fi
    ;;
  off|down)
    dc prod --profile tunnel rm -sf cloudflared >/dev/null
    ok "Tunnel off. Your domain no longer reaches this computer."
    ;;
  status)
    if connected; then ok "Tunnel connected - prod is public on the domain set in Cloudflare."
    elif [ -n "$(container)" ]; then warn "Tunnel running but not connected - see ./tunnel.sh logs"
    else echo "Tunnel off. Nothing on this computer is public."
    fi
    ;;
  logs)
    dc prod --profile tunnel logs -f --tail 50 cloudflared ;;
  *)
    sed -n '2,8p' "$0"; exit 1 ;;
esac
