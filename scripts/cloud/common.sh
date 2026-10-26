# shellcheck shell=bash
# Shared by the cloud scripts. Not run on its own.
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib.sh"

# A cloud setting: from the environment if given, else .env.local, else
# cloud.env (committed, public settings only), else the default.
setting() {
  local key="$1" default="${2:-}" value
  value="${!key:-}"
  [ -z "$value" ] && value="$(env_value "$key")"
  [ -z "$value" ] && value="$(cloud_env_value "$key")"
  echo "${value:-$default}"
}

cloud_env_value() {
  [ -f cloud.env ] || return 0
  grep -E "^$1=" cloud.env | tail -1 | cut -d= -f2- || true
}

# cloud.env is in git for everyone to read: refuse to go on with a secret in it.
check_cloud_env() {
  [ -f cloud.env ] || return 0
  local bad
  bad="$(grep -E '^[A-Z0-9_]*(KEY|SECRET|PASSWORD|TOKEN|DATABASE_URL|CONNECTION_STRING)[A-Z0-9_]*=.' cloud.env | cut -d= -f1 | paste -sd ' ' - || true)"
  [ -z "$bad" ] || die "cloud.env is committed to git, so it must not hold secrets: ${bad}. Move them to .env.local (the setup scripts put them in the cloud's secret store)."
}
check_cloud_env

# Remember a setting in .env.local so the next run reuses it. Without one
# (Cloud Shell, a fresh clone) nothing is lost: the scripts keep what they
# need to find again in the cloud itself.
remember() { [ -f .env.local ] || return 0; set_env_value "$1" "$2"; chmod 600 .env.local; }

# The cost level for a cloud: free (default, stays inside the free tier where
# the cloud has one) or standard (always-on and sturdier; costs money).
# Usage: tier AZURE_TIER
tier() {
  local value; value="$(setting "$1" free)"
  case "$value" in free|standard) echo "$value" ;; *) die "$1 is '${value}': use free or standard." ;;
  esac
}

# Is there a Docker that can build the image? Without one (Cloud Shell on
# some clouds, Claude Code on the web) the scripts build in the cloud instead.
can_build() { command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; }

# The app must be built with a module before a setting can use it.
# Usage: need_module <name as in module.sh> <project folder name> <setting that asks for it>
need_module() {
  grep -q "$2.csproj" "${API_DIR}/WebBase.Api.csproj" \
    || die "$3, but the app isn't built with that module yet: ./scripts/module.sh add $1, then commit and push."
}

# What the app is told about itself, as NAME=VALUE lines (empty ones left out):
# the same for every cloud, so a change to cloud.env means the same everywhere.
app_settings() {
  { echo "App__Environment=prod"
    echo "App__Name=$(setting APP_NAME WebBase)"
    echo "Auth__Google__ClientId=$(setting GOOGLE_CLIENT_ID)"
    cms_env
  } | grep -vE '=$' || true
}

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
  Cms__Azure__Prefix:CMS_AZURE_PREFIX
  Cms__Gcs__Bucket:CMS_GCS_BUCKET Cms__Gcs__Prefix:CMS_GCS_PREFIX)
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
