# shellcheck shell=bash disable=SC2034
# Shared helpers for every script. Not run on its own: `. scripts/lib.sh`.
# Works with bash on Linux, macOS and Windows (WSL).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# C# build output (Directory.Build.props). Containers mount their own volume
# here; made now so Docker doesn't create it as root.
mkdir -p "$ROOT/.build"

# The project's short lowercase name: Docker image and database names.
# scripts/rename.sh changes it along with everything else.
SLUG="webbase"

# The C# project folder, from the solution layout.
# The PostgreSQL client tools, for databases outside the compose files.
PG_IMAGE="postgres:18.6-alpine"
API_DIR="src/WebBase.Api"
# The PostgreSQL module: Entity Framework and its migrations.
POSTGRES_DIR="modules/WebBase.Database.Postgres"

bold()  { printf '\033[1m%s\033[0m\n' "$*"; }
say()   { printf '\033[1;34m==>\033[0m \033[1m%s\033[0m\n' "$*"; }
ok()    { printf '\033[1;32m ✓\033[0m %s\n' "$*"; }
warn()  { printf '\033[1;33m !\033[0m %s\n' "$*" >&2; }
die()   { printf '\033[1;31m ✗ %s\033[0m\n' "$*" >&2; exit 1; }

# Docker must be installed and running before anything else can happen.
need_docker() {
  command -v docker >/dev/null 2>&1 \
    || die "Docker is not installed. Install Docker Desktop: https://www.docker.com/products/docker-desktop/"
  docker info >/dev/null 2>&1 \
    || die "Docker is installed but not running. Start Docker Desktop (or 'sudo systemctl start docker') and try again."
  docker compose version >/dev/null 2>&1 \
    || die "'docker compose' is missing. Update Docker Desktop, or install the compose plugin."
}

# `dc dev ...` / `dc prod ...` = docker compose for that environment, with the
# settings in .env.local filled in.
dc() {
  local environment="$1"; shift
  local args=(-f "compose.${environment}.yaml")
  # Prod's own database container, unless a managed one is configured.
  if [ "$environment" = prod ] && [ -z "$(env_value PROD_DATABASE_URL)" ]; then
    args+=(-f compose.prod-db.yaml)
  fi
  [ -f .env.local ] && args=(--env-file .env.local "${args[@]}")
  docker compose "${args[@]}" "$@"
}

# The value of one setting in .env.local (empty if unset).
env_value() {
  [ -f .env.local ] || return 0
  grep -E "^$1=" .env.local | tail -1 | cut -d= -f2- || true
}

# Set one setting in .env.local, adding it if it isn't there.
set_env_value() {
  local key="$1" value="$2" tmp
  tmp="$(mktemp)"
  if grep -qE "^${key}=" .env.local 2>/dev/null; then
    awk -v k="$key" -v v="$value" 'BEGIN{FS=OFS="="} $1==k {print k"="v; next} {print}' .env.local > "$tmp"
  else
    cat .env.local > "$tmp" 2>/dev/null || true
    printf '%s=%s\n' "$key" "$value" >> "$tmp"
  fi
  cat "$tmp" > .env.local
  rm -f "$tmp"
}

# A random password: letters and digits only, so it is safe inside a
# connection string and a URL without any escaping.
random_secret() {
  LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c "${1:-40}" || true
}

# Wait until a URL answers 200, or give up after N seconds.
wait_for_url() {
  local url="$1" seconds="${2:-60}"
  for _ in $(seq 1 "$seconds"); do
    if curl -fsS -o /dev/null --max-time 2 "$url" 2>/dev/null; then return 0; fi
    sleep 1
  done
  return 1
}

# Ask a yes/no question; default no.
confirm() {
  local answer
  read -r -p "$1 [y/N] " answer
  [[ "$answer" =~ ^[Yy] ]]
}

# An id for a build: when, which commit, and whether there were unsaved edits.
build_id() {
  local id
  id="$(date -u +%Y%m%d-%H%M%S)-$(git rev-parse --short HEAD 2>/dev/null || echo nogit)"
  if git rev-parse --git-dir >/dev/null 2>&1 && [ -n "$(git status --porcelain 2>/dev/null)" ]; then
    id="${id}-dirty"
  fi
  echo "$id"
}
