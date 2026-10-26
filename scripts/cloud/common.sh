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

# The CMS's settings (docs/CMS.md) as NAME=VALUE lines for the cloud's
# environment variables: CMS_ENABLED always, where its content is when set.
CMS_SETTINGS=(Cms__Source:CMS_SOURCE
  Cms__S3__Bucket:CMS_S3_BUCKET Cms__S3__Prefix:CMS_S3_PREFIX Cms__S3__Region:CMS_S3_REGION
  Cms__S3__ServiceUrl:CMS_S3_SERVICE_URL
  Cms__Azure__ContainerUrl:CMS_AZURE_CONTAINER_URL Cms__Azure__Container:CMS_AZURE_CONTAINER
  Cms__Azure__Prefix:CMS_AZURE_PREFIX)
cms_env() {
  echo "Cms__Enabled=$(setting CMS_ENABLED false)"
  local pair value
  for pair in "${CMS_SETTINGS[@]}"; do
    value="$(setting "${pair#*:}")"
    if [ -n "$value" ]; then echo "${pair%%:*}=${value}"; fi
  done
}

# Keys for the CMS's bucket aren't copied to the cloud: there, the app should
# reach it with the cloud's own identity (docs/CMS.md, "In a cloud").
cms_secrets_notice() {
  local key
  for key in CMS_S3_ACCESS_KEY CMS_S3_SECRET_KEY CMS_AZURE_CONNECTION_STRING; do
    if [ -n "$(setting "$key")" ]; then
      warn "${key} is set in .env.local but isn't sent to the cloud. See docs/CMS.md, \"In a cloud\"."
    fi
  done
}
