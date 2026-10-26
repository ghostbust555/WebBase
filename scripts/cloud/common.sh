# shellcheck shell=bash
# Shared by the cloud scripts. Not run on its own.
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib.sh"

# A cloud setting: from the environment if given, else .env.local, else the default.
setting() {
  local key="$1" default="${2:-}" value
  value="${!key:-}"
  [ -z "$value" ] && value="$(env_value "$key")"
  echo "${value:-$default}"
}

# Remember a setting in .env.local so the next run reuses it.
remember() { [ -f .env.local ] || die "Run ./setup.sh first."; set_env_value "$1" "$2"; chmod 600 .env.local; }

need_cli() { command -v "$1" >/dev/null 2>&1 || die "The '$1' command isn't installed. $2"; }

need_secrets() {
  [ -n "$(setting JWT_KEY)" ] || die "JWT_KEY is empty. Run ./setup.sh first."
}

# Clouds run Intel/AMD (amd64) machines, whatever this computer is: an Apple
# Silicon Mac builds arm64 by default, which would not start there.
build_and_push() {
  local image="$1" build="$2"
  say "Building ${image} for linux/amd64"
  docker build --platform linux/amd64 -f ops/Dockerfile \
    --build-arg "BUILD_ID=${build}" --label "build=${build}" -t "$image" .
  say "Uploading ${image}"
  docker push "$image"
}
