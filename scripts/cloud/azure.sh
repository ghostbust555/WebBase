#!/usr/bin/env bash
# Run prod on Microsoft Azure: Azure Container Apps (the website + API), Azure
# Database for PostgreSQL, and optionally Blob Storage for the CMS's pages.
# The step-by-step guide, from a browser only: docs/AZURE.md.
#
#   ./scripts/cloud/azure.sh setup     create (or update) everything and deploy; safe to repeat.
#                                      Run it again after changing a setting below.
#   ./scripts/cloud/azure.sh deploy    roll out this commit (git pull first): the image GitHub
#                                      published for it, or, with AZURE_REGISTRY=acr, built here
#   ./scripts/cloud/azure.sh github    let GitHub Actions deploy each image it publishes (no keys involved;
#                                      GITHUB_OIDC_SUBJECT=... trusts the exact subject a run reported)
#   ./scripts/cloud/azure.sh url       the site's address
#   ./scripts/cloud/azure.sh logs      the app's output, live
#   ./scripts/cloud/azure.sh destroy   DELETE everything this created (and its data)
#
# Needs the Azure CLI, logged in (az login). Azure Cloud Shell
# (https://shell.azure.com) has it, already logged in, plus git: nothing to
# install. No Docker needed: GitHub builds the image (.github/workflows/publish-image.yml)
# and keeps it in its own registry, ghcr.io, for free.
#
# Cost, set by AZURE_TIER:
#   free      (default) scales to zero, the smallest size, inside Container Apps' free
#             monthly grant, with the database on Cosmos DB's free tier, which never
#             expires, and the image on GitHub's registry: about $0. Blob storage costs cents.
#   standard  always on (no cold starts), twice the size, up to 5 copies, and a General
#             Purpose PostgreSQL database. Roughly $150-200/month.
#
# Settings (environment variables, or lines in .env.local; none are required):
#   AZURE_TIER            free or standard                      default free
#   AZURE_LOCATION        region                                default westus2 (West US 2)
#   AZURE_RESOURCE_GROUP  the folder everything is created in   default <name>-rg
#   AZURE_DATABASE        cosmos or postgres. Default: cosmos on the free tier, postgres on
#                         standard or with AZURE_DATABASE_URL. Cosmos DB's free tier never expires
#                         (1000 RU/s and 25 GB, one account per subscription); if that is taken,
#                         AZURE_COSMOS_SERVERLESS=true pays per use instead (cents). PostgreSQL
#                         on Azure is free only for a new account's first 12 months.
#   AZURE_DATABASE_URL    postgres://... to use your own PostgreSQL (e.g. a free Neon one)
#                         instead of creating Azure's. Kept in the app's secrets.
#   AZURE_REGISTRY        ghcr (default): run the image GitHub publishes, ghcr.io/<owner>/<name>.
#                         acr: make an Azure Container Registry (about $5/month) and build here or in it.
#   AZURE_IMAGE           the image, without a tag     default ghcr.io/<this clone's GitHub owner>/<name>
#   AZURE_IMAGE_TAG       which one                    default sha-<this commit>
#   GHCR_TOKEN            for a private repository's image: a GitHub token that may only read
#                         packages. Asked for by setup when needed; kept as the app's registry secret.
#   GHCR_USER             that token's GitHub user     default: the image's owner
# The image has the database's module and the CMS storage's: nothing to add to the code.
#   APP_NAME, GOOGLE_CLIENT_ID, CMS_ENABLED, CMS_SOURCE, CMS_AZURE_CONTAINER...   the app's own
#                         settings. Keep them in cloud.env (committed): every deploy applies them.
#                         CMS_SOURCE=azure makes a storage account and container for the pages.
. "$(dirname "$0")/common.sh"

case "${1:-}" in
  setup|deploy|github|url|logs|destroy) ;;
  *) sed -n '2,49p' "$0"; exit 1 ;;
esac
need_cli az "Install it (https://aka.ms/installazurecli), or run this in Azure Cloud Shell (https://shell.azure.com)."
az account show >/dev/null 2>&1 || die "Not logged in to Azure. Run: az login"

TIER="$(tier AZURE_TIER)"
LOCATION="$(setting AZURE_LOCATION westus2)"
RG="$(setting AZURE_RESOURCE_GROUP "${SLUG}-rg")"
APP="${SLUG}"
APP_ENV="${SLUG}-env"
IDENTITY="${SLUG}-id"
# Registry, storage and database names must be unique across all of Azure: a
# suffix made from the subscription's id gives the same one every time, from
# any computer, with nothing to remember.
SUFFIX="$(printf '%06x' $(( $(printf '%s' "$(az account show --query id -o tsv)${SLUG}" | cksum | cut -d' ' -f1) % 16777216 )))"
REGISTRY="${SLUG//-/}${SUFFIX}"
STORAGE="$(printf '%s' "${SLUG//-/}${SUFFIX}" | cut -c1-24)"
PG_SERVER="${SLUG}-db-${SUFFIX}"
COSMOS="${SLUG}-cosmos-${SUFFIX}"
# Cosmos DB for the free tier (its free account never expires; PostgreSQL's does
# after a year), PostgreSQL for standard, or whenever you bring a PostgreSQL URL.
if [ -n "$(setting AZURE_DATABASE_URL)" ] || [ "$TIER" = standard ]; then DEFAULT_DATABASE=postgres; else DEFAULT_DATABASE=cosmos; fi
DATABASE="$(setting AZURE_DATABASE "$DEFAULT_DATABASE")"
case "$DATABASE" in postgres|cosmos) ;; *) die "AZURE_DATABASE is '${DATABASE}': use postgres or cosmos." ;; esac
export DB_MODULE="$DATABASE"
CMS_CONTAINER="$(setting CMS_AZURE_CONTAINER content)"

# Where the image comes from: GitHub's registry (free), or Azure's own (about $5/month).
REGISTRY_KIND="$(setting AZURE_REGISTRY ghcr)"
case "$REGISTRY_KIND" in ghcr|acr) ;; *) die "AZURE_REGISTRY is '${REGISTRY_KIND}': use ghcr or acr." ;; esac
GH_OWNER="$(git remote get-url origin 2>/dev/null | sed -nE 's#^(git@github\.com:|https://github\.com/)([^/]+)/.*#\2#p' | tr '[:upper:]' '[:lower:]')"
GHCR_IMAGE="$(setting AZURE_IMAGE "${GH_OWNER:+ghcr.io/${GH_OWNER}/${SLUG}}")"
GHCR_USER="$(setting GHCR_USER "$GH_OWNER")"

if [ "$TIER" = free ]; then
  CPU=0.25 MEMORY=0.5Gi MIN=0 MAX=2
  PG_SKU=Standard_B1ms PG_TIER=Burstable PG_STORAGE=32
else
  CPU=0.5 MEMORY=1Gi MIN=1 MAX=5
  PG_SKU=Standard_D2ds_v5 PG_TIER=GeneralPurpose PG_STORAGE=64
fi

# With CMS_SOURCE=azure, the container is the one setup makes (unless the
# address of another is given).
if [ "$(setting CMS_SOURCE)" = azure ] && [ -z "$(setting CMS_AZURE_CONTAINER_URL)" ] && [ -z "$(setting CMS_AZURE_CONNECTION_STRING)" ]; then
  export CMS_AZURE_CONTAINER_URL="https://${STORAGE}.blob.core.windows.net/${CMS_CONTAINER}"
fi

exists() { "$@" >/dev/null 2>&1; }

# A newly created identity or role takes a moment to be known everywhere.
retry() {
  local n
  for n in 1 2 3 4 5 6; do "$@" && return 0; [ "$n" = 6 ] || sleep 10; done
  return 1
}

secret_get() {
  az containerapp secret show -g "$RG" -n "$APP" --secret-name "$1" --query value -o tsv 2>/dev/null || true
}

app_url() {
  local fqdn; fqdn="$(az containerapp show -g "$RG" -n "$APP" --query properties.configuration.ingress.fqdn -o tsv 2>/dev/null)"
  [ -n "$fqdn" ] || die "No app yet. Run ./scripts/cloud/azure.sh setup first."
  echo "https://${fqdn}"
}

# The app's environment variables, as one array: the app's own settings, the
# two secrets by reference, and which identity to sign in to Azure with.
env_args() {
  local line
  ENV_ARGS=()
  while IFS= read -r line; do ENV_ARGS+=("$line"); done < <(app_settings)
  ENV_ARGS+=("Jwt__Key=secretref:jwt-key"
             "AZURE_CLIENT_ID=$(az identity show -g "$RG" -n "$IDENTITY" --query clientId -o tsv)")
  if [ "$DATABASE" = cosmos ]; then
    ENV_ARGS+=("Database__Provider=cosmos" "Cosmos__ConnectionString=secretref:database-url" "Cosmos__Database=${SLUG}")
    # Free tier: the 1000 RU/s is shared by the three containers. Serverless has none to share.
    if [ "$TIER" = free ] && [ "$(setting AZURE_COSMOS_SERVERLESS)" != true ]; then ENV_ARGS+=("Cosmos__DatabaseThroughput=1000"); fi
  else
    # The image from GitHub has both database modules: say which one.
    ENV_ARGS+=("Database__Provider=postgres" "DATABASE_URL=secretref:database-url")
  fi
}

# ---- The image from GitHub's registry (AZURE_REGISTRY=ghcr) --------------------------

# The token the app pulls with, if setup stored one: the app's registry secret.
ghcr_saved_token() {
  local ref
  ref="$(az containerapp registry list -g "$RG" -n "$APP" --query "[?server=='ghcr.io'].passwordSecretRef | [0]" -o tsv 2>/dev/null || true)"
  [ -z "$ref" ] || secret_get "$ref"
}

# Is IMAGE published? Asks ghcr.io the way a container host does: anonymously,
# or as GHCR_USER with the token when there is one (a private package).
ghcr_published() {
  local path="${IMAGE#ghcr.io/}" token
  path="${path%:*}"
  if [ -n "$GHCR_TOKEN" ]; then
    token="$(curl -fsS -u "${GHCR_USER}:${GHCR_TOKEN}" "https://ghcr.io/token?scope=repository:${path}:pull" 2>/dev/null || true)"
  else
    token="$(curl -fsS "https://ghcr.io/token?scope=repository:${path}:pull" 2>/dev/null || true)"
  fi
  token="$(printf '%s' "$token" | sed -nE 's/.*"token":"([^"]+)".*/\1/p')"
  [ -n "$token" ] || return 1
  curl -fsS -o /dev/null -H "Authorization: Bearer ${token}" \
    -H "Accept: application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json" \
    "https://ghcr.io/v2/${path}/manifests/${IMAGE##*:}" 2>/dev/null
}

# IMAGE for this commit, checked to exist. Setup may ask for a token (a private
# repository's image can't be seen without one); deploy uses the one the app has.
ghcr_image() {
  [ -n "$GHCR_IMAGE" ] || die "Which image? This clone's origin isn't on GitHub: set AZURE_IMAGE=ghcr.io/OWNER/NAME."
  BUILD="$(setting AZURE_IMAGE_TAG "sha-$(git rev-parse --short=12 HEAD)")"
  IMAGE="${GHCR_IMAGE}:${BUILD}"
  GHCR_TOKEN="$(setting GHCR_TOKEN)"
  [ -n "$GHCR_TOKEN" ] || GHCR_TOKEN="$(ghcr_saved_token)"
  say "Image ${IMAGE}, from GitHub's registry"
  ghcr_published && return 0
  if [ -z "$GHCR_TOKEN" ] && [ "$1" = setup ] && [ -t 0 ]; then
    echo
    bold "Azure needs a second GitHub token, to download the image (a private repository's images are private)."
    echo "  Not the one you cloned with: a classic token whose only box is read:packages. This opens the form that way:"
    echo "    https://github.com/settings/tokens/new?scopes=read:packages&description=${SLUG}-azure"
    echo "  Pick an expiration, Generate token, paste it here. It's kept as the app's registry secret (docs/AZURE.md)."
    read -r -s -p "  Token: " GHCR_TOKEN; echo
    [ -z "$GHCR_TOKEN" ] || ! ghcr_published || return 0
  fi
  die "Can't find ${IMAGE}. Either GitHub hasn't published this commit yet (the repository's Actions tab:
    Tests, then Publish image, about 5 minutes after a push; or Actions -> Publish image -> Run workflow),
    this clone has commits GitHub hasn't seen (git pull, or push them), or the image is private and needs
    GHCR_TOKEN (a GitHub token with read:packages) for ${GHCR_USER:-its owner}."
}

# How the app signs in to ghcr.io, for `containerapp create` (REGISTRY_ARGS) and for an
# existing app (REGISTRY_SET, `containerapp registry set`). Nothing for a public image.
ghcr_registry_args() {
  REGISTRY_ARGS=(); REGISTRY_SET=()
  [ -n "$GHCR_TOKEN" ] || return 0
  REGISTRY_ARGS=(--registry-server ghcr.io --registry-username "$GHCR_USER" --registry-password "$GHCR_TOKEN")
  REGISTRY_SET=(--server ghcr.io --username "$GHCR_USER" --password "$GHCR_TOKEN")
}

# ---- Or Azure's own registry (AZURE_REGISTRY=acr) -------------------------------------

build_image() {
  BUILD="$(build_id)"
  IMAGE="${REGISTRY}.azurecr.io/${APP}:${BUILD}"
  if can_build; then
    az acr login --name "$REGISTRY" >/dev/null
    build_and_push "$IMAGE" "$BUILD"
  else
    say "Building ${IMAGE} in Azure (no Docker here), 3-6 minutes"
    az acr build -r "$REGISTRY" -t "${APP}:${BUILD}" -f ops/Dockerfile --platform linux/amd64 \
      --build-arg "BUILD_ID=${BUILD}" --build-arg "MODULES=$(build_modules)" . \
      || die "Azure couldn't build it. Some subscriptions (free trials, for one) may not be allowed to use Azure's builders: run this where Docker is installed instead."
  fi
}

# Long Azure operations are started with --no-wait and watched here. Waiting inside az
# itself can fail in Cloud Shell ("Only the original caller of an asynchronous operation
# is allowed to retrieve the results") while the operation carries on fine in Azure.
wait_provisioned() { # what it is, then the `az ... show` command for it
  local what="$1" state="" i
  shift
  for i in $(seq 1 90); do
    state="$("$@" --query properties.provisioningState -o tsv 2>/dev/null || true)"
    case "$state" in
      Succeeded) return 0 ;;
      Failed|Canceled) die "${what}: Azure says ${state}. The resource group's Activity log in the portal says why." ;;
    esac
    [ "$i" = 1 ] && echo "    waiting for ${what} (${state:-starting})..."
    sleep 10
  done
  die "${what} still isn't ready after 15 minutes (${state:-unknown}). Run setup again in a while."
}

check_health() {
  local url; url="$(app_url)"
  say "Checking ${url}/api/health (a scaled-to-zero app needs a few seconds to start)"
  wait_for_url "${url}/api/health" 120 || die "The new version doesn't answer. See why: ./scripts/cloud/azure.sh logs"
  ok "It works: ${url}"
}

# Cosmos DB for NoSQL: one account with the free tier (1000 RU/s and 25 GB, never
# expires, one per subscription), or a serverless one. Prints its connection string.
cosmos_connection() {
  local mode=(--enable-free-tier true) other
  if [ "$TIER" != free ] || [ "$(setting AZURE_COSMOS_SERVERLESS)" = true ]; then
    mode=(--capabilities EnableServerless)
  fi
  if ! exists az cosmosdb show -g "$RG" -n "$COSMOS"; then
    if [ "${mode[0]}" = --enable-free-tier ]; then
      other="$(az cosmosdb list --query "[?enableFreeTier].name | [0]" -o tsv 2>/dev/null || true)"
      [ -z "$other" ] || die "This subscription's one free Cosmos DB account is already used by '${other}'. Use AZURE_COSMOS_SERVERLESS=true (pay per use, cents), or AZURE_DATABASE=postgres."
    fi
    say "Cosmos DB account ${COSMOS} (${mode[*]}), 3-5 minutes" >&2
    az cosmosdb create -g "$RG" -n "$COSMOS" --locations "regionName=${LOCATION}" failoverPriority=0 isZoneRedundant=False \
      --default-consistency-level Session "${mode[@]}" -o none >&2 || return 1
  fi
  az cosmosdb keys list -g "$RG" -n "$COSMOS" --type connection-strings \
    --query "connectionStrings[0].connectionString" -o tsv
}

# The database: your own (AZURE_DATABASE_URL), the one already set up, or a new
# Azure Database for PostgreSQL. Prints the URL for the app.
database_url() {
  local own existing password
  own="$(setting AZURE_DATABASE_URL)"
  if [ -n "$own" ]; then echo "$own"; return; fi
  existing="$(secret_get database-url)"
  if [ -n "$existing" ] && exists az postgres flexible-server show -g "$RG" -n "$PG_SERVER"; then
    echo "$existing"; return
  fi
  password="$(random_secret 32)"
  if exists az postgres flexible-server show -g "$RG" -n "$PG_SERVER"; then
    # A server from an earlier run whose password wasn't kept: set a new one.
    az postgres flexible-server update -g "$RG" -n "$PG_SERVER" --admin-password "$password" -o none >&2
  else
    say "PostgreSQL server ${PG_SERVER}: ${PG_SKU}, ${PG_STORAGE} GB (the slowest step: 5-10 minutes)" >&2
    # --public-access 0.0.0.0 means "Azure's own services", not the whole
    # internet; the connection needs the password and is encrypted.
    az postgres flexible-server create -g "$RG" -n "$PG_SERVER" -l "$LOCATION" \
      --admin-user "${SLUG}admin" --admin-password "$password" \
      --tier "$PG_TIER" --sku-name "$PG_SKU" --storage-size "$PG_STORAGE" --version 17 \
      --public-access 0.0.0.0 --yes -o none >&2
  fi
  exists az postgres flexible-server db show -g "$RG" -s "$PG_SERVER" -n "$SLUG" \
    || az postgres flexible-server db create -g "$RG" -s "$PG_SERVER" -n "$SLUG" -o none >&2
  echo "postgres://${SLUG}admin:${password}@${PG_SERVER}.postgres.database.azure.com:5432/${SLUG}?sslmode=require"
}

# A storage account and container for the CMS's pages, readable by the app's
# identity and by no one else.
setup_cms_storage() {
  [ "$(setting CMS_SOURCE)" = azure ] || return 0
  case "$(setting CMS_AZURE_CONTAINER_URL)" in
    "https://${STORAGE}.blob.core.windows.net/"*) ;;
    *) warn "CMS_AZURE_CONTAINER_URL points at storage this script didn't make, so none is created."; return 0 ;;
  esac
  say "Storage account ${STORAGE}, container ${CMS_CONTAINER}, for the CMS"
  exists az storage account show -g "$RG" -n "$STORAGE" \
    || az storage account create -g "$RG" -n "$STORAGE" -l "$LOCATION" --sku Standard_LRS --kind StorageV2 \
         --access-tier Hot --allow-blob-public-access false --min-tls-version TLS1_2 -o none
  exists az storage container-rm show --storage-account "$STORAGE" -g "$RG" -n "$CMS_CONTAINER" \
    || az storage container-rm create --storage-account "$STORAGE" -g "$RG" -n "$CMS_CONTAINER" -o none
  local scope principal
  scope="$(az storage account show -g "$RG" -n "$STORAGE" --query id -o tsv)/blobServices/default/containers/${CMS_CONTAINER}"
  principal="$(az identity show -g "$RG" -n "$IDENTITY" --query principalId -o tsv)"
  retry az role assignment create --assignee-object-id "$principal" --assignee-principal-type ServicePrincipal \
    --role "Storage Blob Data Reader" --scope "$scope" -o none \
    || die "Couldn't let the app read the container. Your account needs the Owner role on the resource group."
}

# A deploy from somewhere that doesn't have AZURE_DATABASE set must not change an
# existing app's database: its own secret says which one it has.
if [ -z "$(setting AZURE_DATABASE)" ]; then
  case "$(secret_get database-url)" in
    AccountEndpoint=*) DATABASE=cosmos ;;
    postgres://*|postgresql://*) DATABASE=postgres ;;
  esac
  export DB_MODULE="$DATABASE"
fi

case "$1" in
  setup)
    say "Subscription: $(az account show --query name -o tsv). Tier: ${TIER}, region ${LOCATION}"
    say "Turning on the Azure services this needs (the first time takes a few minutes)"
    namespaces=(Microsoft.App Microsoft.OperationalInsights Microsoft.DocumentDB Microsoft.DBforPostgreSQL
                Microsoft.ManagedIdentity Microsoft.Storage)
    [ "$REGISTRY_KIND" = acr ] && namespaces+=(Microsoft.ContainerRegistry)
    for ns in "${namespaces[@]}"; do
      az provider register --namespace "$ns" --wait >/dev/null
    done
    az extension add --name containerapp --upgrade --only-show-errors >/dev/null

    say "Resource group ${RG}"
    # An existing group is kept wherever it is: its location is only a label, and its
    # resources go to AZURE_LOCATION. (Creating it again in another region would fail.)
    exists az group show -n "$RG" || az group create -n "$RG" -l "$LOCATION" -o none

    say "The app's identity"
    exists az identity show -g "$RG" -n "$IDENTITY" || az identity create -g "$RG" -n "$IDENTITY" -l "$LOCATION" -o none
    if [ "$REGISTRY_KIND" = acr ]; then
      say "A container registry for its images (about \$5/month)"
      exists az acr show -g "$RG" -n "$REGISTRY" \
        || az acr create -g "$RG" -n "$REGISTRY" --sku Basic --admin-enabled false -o none
      # The app may pull images from this registry, and do nothing else with it.
      retry az role assignment create --assignee-object-id "$(az identity show -g "$RG" -n "$IDENTITY" --query principalId -o tsv)" \
        --assignee-principal-type ServicePrincipal --role AcrPull \
        --scope "$(az acr show -g "$RG" -n "$REGISTRY" --query id -o tsv)" -o none \
        || die "Couldn't let the app pull images. Your account needs the Owner role on the resource group."
    else
      # Checked before anything costs time: the image must be on GitHub already.
      ghcr_image setup
      ghcr_registry_args
      if exists az acr show -g "$RG" -n "$REGISTRY"; then
        warn "Registry ${REGISTRY} from an earlier setup isn't used any more, and costs about \$5/month."
        warn "Once this works, delete it: az acr delete -g ${RG} -n ${REGISTRY} --yes"
      fi
    fi

    if [ "$DATABASE" = cosmos ]; then
      DB_URL="$(cosmos_connection)" || die "Couldn't create the Cosmos DB account: Azure's message is above."
      case "$DB_URL" in AccountEndpoint=*) ;; *) die "Couldn't read the Cosmos DB account's connection string." ;; esac
    else
      DB_URL="$(database_url)"
      case "$DB_URL" in postgres://*|postgresql://*) ;; *) die "The database setting doesn't start with postgres://." ;; esac
    fi
    setup_cms_storage
    cms_secrets_notice

    [ "$REGISTRY_KIND" = acr ] && build_image
    ID_RESOURCE="$(az identity show -g "$RG" -n "$IDENTITY" --query id -o tsv)"
    if [ "$REGISTRY_KIND" = acr ]; then
      REGISTRY_ARGS=(--registry-server "${REGISTRY}.azurecr.io" --registry-identity "$ID_RESOURCE")
      REGISTRY_SET=()   # an existing app already pulls from it with its identity
    fi
    env_args
    exists az containerapp env show -g "$RG" -n "$APP_ENV" \
      || { say "Container Apps environment"; az containerapp env create -g "$RG" -n "$APP_ENV" -l "$LOCATION" --no-wait -o none; }
    # Also when an earlier run started it and lost track of it.
    wait_provisioned "the Container Apps environment" az containerapp env show -g "$RG" -n "$APP_ENV"

    JWT="$(secret_get jwt-key)"
    [ -n "$JWT" ] || JWT="$(random_secret 64)"   # the key that signs sign-ins: made once, kept in the app's secrets
    if exists az containerapp show -g "$RG" -n "$APP"; then
      say "Updating the app"
      az containerapp secret set -g "$RG" -n "$APP" --secrets "jwt-key=${JWT}" "database-url=${DB_URL}" -o none
      if [ "${#REGISTRY_SET[@]}" -gt 0 ]; then
        az containerapp registry set -g "$RG" -n "$APP" "${REGISTRY_SET[@]}" -o none
      fi
      az containerapp update -g "$RG" -n "$APP" --image "$IMAGE" --replace-env-vars "${ENV_ARGS[@]}" \
        --min-replicas "$MIN" --max-replicas "$MAX" --cpu "$CPU" --memory "$MEMORY" -o none
    else
      say "Creating the app (${CPU} CPU, ${MEMORY}, ${MIN}-${MAX} copies)"
      az containerapp create -g "$RG" -n "$APP" --environment "$APP_ENV" --image "$IMAGE" \
        --user-assigned "$ID_RESOURCE" "${REGISTRY_ARGS[@]+"${REGISTRY_ARGS[@]}"}" \
        --target-port 8080 --ingress external \
        --min-replicas "$MIN" --max-replicas "$MAX" --cpu "$CPU" --memory "$MEMORY" \
        --secrets "jwt-key=${JWT}" "database-url=${DB_URL}" --env-vars "${ENV_ARGS[@]}" --no-wait -o none
      wait_provisioned "the app" az containerapp show -g "$RG" -n "$APP"
    fi
    check_health
    if [ "$TIER" = free ]; then
      echo "    Free tier: the app sleeps when idle, so the first visit after a quiet spell takes a few seconds."
      [ "$DATABASE" = cosmos ] && echo "    The database is on Cosmos DB's free tier: it never expires (one such account per subscription)."
      [ "$REGISTRY_KIND" = acr ] && echo "    Not free: the container registry (about \$5/month)."
      echo "    Set a budget alert anyway: https://portal.azure.com/#view/Microsoft_Azure_CostManagement/Menu/~/budgets"
    fi
    if [ "$REGISTRY_KIND" = ghcr ]; then
      echo "    Next time: push to main, wait for GitHub's Publish image (Actions tab), then here:"
      echo "    git pull && ./scripts/cloud/azure.sh deploy"
    else
      echo "    Next time, just: ./scripts/cloud/azure.sh deploy"
    fi
    ;;

  deploy)
    exists az containerapp show -g "$RG" -n "$APP" || die "No app yet. Run ./scripts/cloud/azure.sh setup first."
    if [ "$REGISTRY_KIND" = acr ]; then build_image; else ghcr_image deploy; fi
    env_args
    say "Rolling out ${BUILD}"
    az containerapp update -g "$RG" -n "$APP" --image "$IMAGE" --replace-env-vars "${ENV_ARGS[@]}" -o none
    check_health
    ;;

  github)
    # GitHub Actions deploys each image it publishes (.github/workflows/publish-image.yml,
    # job "deploy"). No key or password: GitHub proves "a run in this repository, on main"
    # with a token it signs, and this identity trusts exactly that (a federated credential).
    [ "$REGISTRY_KIND" = ghcr ] || die "Deploying from GitHub needs the image on GitHub's registry: AZURE_REGISTRY=ghcr."
    origin="$(git remote get-url origin 2>/dev/null || true)"
    GITHUB_REPO="$(setting GITHUB_REPO "$(echo "$origin" | sed -E 's#^(git@github\.com:|https://github\.com/)##; s#\.git$##')")"
    [[ "$GITHUB_REPO" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] \
      || die "Which GitHub repository? Run with GITHUB_REPO=owner/name ./scripts/cloud/azure.sh github"
    exists az containerapp show -g "$RG" -n "$APP" || die "No app yet. Run ./scripts/cloud/azure.sh setup first."
    DEPLOYER="${SLUG}-deploy"
    say "An identity for GitHub Actions in ${GITHUB_REPO} to deploy with (${DEPLOYER})"
    exists az identity show -g "$RG" -n "$DEPLOYER" || az identity create -g "$RG" -n "$DEPLOYER" -l "$LOCATION" -o none
    # What GitHub says a run is: its "subject". The classic form is repo:owner/name:ref:...;
    # newer repositories send owner@<owner id>/name@<repo id> instead. Both are trusted, each
    # a credential of its own. The ids come from GitHub's API: public for a public
    # repository, through gh (signed in) for a private one, or GITHUB_OIDC_SUBJECT exactly as
    # the deploy job prints it (and as Azure's AADSTS700213 error quotes it).
    trust() { # credential name, subject
      local current
      current="$(az identity federated-credential show -g "$RG" --identity-name "$DEPLOYER" -n "$1" --query subject -o tsv 2>/dev/null || true)"
      [ "$current" = "$2" ] && return 0
      [ -z "$current" ] || az identity federated-credential delete -g "$RG" --identity-name "$DEPLOYER" -n "$1" --yes -o none
      az identity federated-credential create -g "$RG" --identity-name "$DEPLOYER" -n "$1" \
        --issuer https://token.actions.githubusercontent.com --subject "$2" --audiences api://AzureADTokenExchange -o none
      echo "    trusts: $2"
    }
    gh_id() { # API path -> its numeric id, or nothing
      local id
      id="$(curl -fsS "https://api.github.com/$1" 2>/dev/null | sed -nE 's/^  "id": ([0-9]+),$/\1/p' | head -n 1 || true)"
      if [ -z "$id" ] && command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
        id="$(gh api "$1" --jq .id 2>/dev/null || true)"
      fi
      echo "$id"
    }
    trust github-main "repo:${GITHUB_REPO}:ref:refs/heads/main"
    explicit="$(setting GITHUB_OIDC_SUBJECT)"
    owner_id="$(gh_id "users/${GITHUB_REPO%%/*}")"
    repo_id="$(gh_id "repos/${GITHUB_REPO}")"
    if [ -n "$explicit" ]; then
      [[ "$explicit" == repo:* ]] || die "GITHUB_OIDC_SUBJECT should start with repo: (copy it from the deploy job's summary or Azure's error)."
      trust github-main-ids "$explicit"
    elif [ -n "$owner_id" ] && [ -n "$repo_id" ]; then
      trust github-main-ids "repo:${GITHUB_REPO%%/*}@${owner_id}/${GITHUB_REPO#*/}@${repo_id}:ref:refs/heads/main"
    else
      warn "Couldn't look up this repository's id (a private repository, and no signed-in gh here)."
      warn "If the first deploy fails at Azure login (AADSTS700213), its summary shows the subject GitHub sent:"
      warn "  GITHUB_OIDC_SUBJECT='<that subject>' ./scripts/cloud/azure.sh github"
    fi
    # Change the app (a new image, its settings) and read the rest of the group (the app's
    # identity, for its settings). Not: the database, the storage, or making anything new.
    principal="$(az identity show -g "$RG" -n "$DEPLOYER" --query principalId -o tsv)"
    retry az role assignment create --assignee-object-id "$principal" --assignee-principal-type ServicePrincipal \
      --role Contributor --scope "$(az containerapp show -g "$RG" -n "$APP" --query id -o tsv)" -o none \
      || die "Couldn't let the deployer change the app. Your account needs the Owner role on the resource group."
    retry az role assignment create --assignee-object-id "$principal" --assignee-principal-type ServicePrincipal \
      --role Reader --scope "$(az group show -n "$RG" --query id -o tsv)" -o none \
      || die "Couldn't let the deployer read the resource group. Your account needs the Owner role on it."

    VARS=(AZURE_CLIENT_ID="$(az identity show -g "$RG" -n "$DEPLOYER" --query clientId -o tsv)"
      AZURE_TENANT_ID="$(az account show --query tenantId -o tsv)"
      AZURE_SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
      AZURE_RESOURCE_GROUP="$RG")
    if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
      for v in "${VARS[@]}"; do gh variable set "${v%%=*}" --body "${v#*=}" -R "$GITHUB_REPO" >/dev/null; done
      ok "Done, and the repository's variables are set. From the next push to main, GitHub deploys by itself."
    else
      ok "Done on Azure's side. One step left, on GitHub:"
      echo "    https://github.com/${GITHUB_REPO}/settings/variables/actions -> New repository variable, four times."
      echo "    (Variables, not secrets: none of these is secret; GitHub's signed token is what proves it's you.)"
      echo
      for v in "${VARS[@]}"; do printf '      %-24s %s\n' "${v%%=*}" "${v#*=}"; done
      echo
      echo "    Then each push to main is tested, published, and deployed. Or now: Actions -> Publish image -> Run workflow."
    fi
    ;;

  url) app_url ;;
  logs) az containerapp logs show -g "$RG" -n "$APP" --follow --tail 100 ;;

  destroy)
    if [ -n "$(setting AZURE_DATABASE_URL)" ]; then
      warn "This deletes resource group ${RG}: the app, the registry, the storage and Azure's database with all its data."
      warn "Your own database (AZURE_DATABASE_URL) is left alone."
    else
      warn "This deletes resource group ${RG}: the app, the registry, the storage and the DATABASE with all its data."
    fi
    confirm "Really delete everything in ${RG}?" || exit 1
    az group delete -n "$RG" --yes
    ok "Deleted ${RG}."
    ;;
esac
