#!/usr/bin/env bash
# Run prod on Microsoft Azure: Azure Container Apps (the website + API), Azure
# Database for PostgreSQL, and optionally Blob Storage for the CMS's pages.
# The step-by-step guide, from a browser only: docs/AZURE.md.
#
#   ./scripts/cloud/azure.sh setup     create (or update) everything and deploy; safe to repeat.
#                                      Run it again after changing a setting below.
#   ./scripts/cloud/azure.sh deploy    build what's here and roll it out
#   ./scripts/cloud/azure.sh url       the site's address
#   ./scripts/cloud/azure.sh logs      the app's output, live
#   ./scripts/cloud/azure.sh destroy   DELETE everything this created (and its data)
#
# Needs the Azure CLI, logged in (az login). Azure Cloud Shell
# (https://shell.azure.com) has it, already logged in, plus git: nothing to
# install. No Docker needed: without it, the image is built in Azure.
#
# Cost, set by AZURE_TIER:
#   free      (default) scales to zero, the smallest size, inside Container Apps' free
#             monthly grant, with the database on Cosmos DB's free tier, which never
#             expires. The container registry has no free tier: Basic is about $5/month.
#             Blob storage costs cents.
#   standard  always on (no cold starts), twice the size, up to 5 copies, and a General
#             Purpose PostgreSQL database. Roughly $150-200/month.
#
# Settings (environment variables, or lines in .env.local; none are required):
#   AZURE_TIER            free or standard                      default free
#   AZURE_LOCATION        region                                default eastus2 (East US 2)
#   AZURE_RESOURCE_GROUP  the folder everything is created in   default <name>-rg
#   AZURE_DATABASE        cosmos or postgres. Default: cosmos on the free tier, postgres on
#                         standard or with AZURE_DATABASE_URL. Cosmos DB's free tier never expires
#                         (1000 RU/s and 25 GB, one account per subscription); if that is taken,
#                         AZURE_COSMOS_SERVERLESS=true pays per use instead (cents). PostgreSQL
#                         on Azure is free only for a new account's first 12 months.
#   AZURE_DATABASE_URL    postgres://... to use your own PostgreSQL (e.g. a free Neon one)
#                         instead of creating Azure's. Kept in the app's secrets.
# The image is built with the database's module and the CMS storage's: nothing to add to the code.
#   APP_NAME, GOOGLE_CLIENT_ID, CMS_ENABLED, CMS_SOURCE, CMS_AZURE_CONTAINER...   the app's own
#                         settings. Keep them in cloud.env (committed): every deploy applies them.
#                         CMS_SOURCE=azure makes a storage account and container for the pages.
. "$(dirname "$0")/common.sh"

case "${1:-}" in
  setup|deploy|url|logs|destroy) ;;
  *) sed -n '2,39p' "$0"; exit 1 ;;
esac
need_cli az "Install it (https://aka.ms/installazurecli), or run this in Azure Cloud Shell (https://shell.azure.com)."
az account show >/dev/null 2>&1 || die "Not logged in to Azure. Run: az login"

TIER="$(tier AZURE_TIER)"
LOCATION="$(setting AZURE_LOCATION eastus2)"
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
    ENV_ARGS+=("DATABASE_URL=secretref:database-url")
  fi
}

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
    for ns in Microsoft.App Microsoft.OperationalInsights Microsoft.ContainerRegistry Microsoft.DocumentDB \
              Microsoft.DBforPostgreSQL Microsoft.ManagedIdentity Microsoft.Storage; do
      az provider register --namespace "$ns" --wait >/dev/null
    done
    az extension add --name containerapp --upgrade --only-show-errors >/dev/null

    say "Resource group ${RG}"
    # An existing group is kept wherever it is: its location is only a label, and its
    # resources go to AZURE_LOCATION. (Creating it again in another region would fail.)
    exists az group show -n "$RG" || az group create -n "$RG" -l "$LOCATION" -o none

    say "The app's identity, and a container registry for its images"
    exists az identity show -g "$RG" -n "$IDENTITY" || az identity create -g "$RG" -n "$IDENTITY" -l "$LOCATION" -o none
    exists az acr show -g "$RG" -n "$REGISTRY" \
      || az acr create -g "$RG" -n "$REGISTRY" --sku Basic --admin-enabled false -o none
    # The app may pull images from this registry, and do nothing else with it.
    retry az role assignment create --assignee-object-id "$(az identity show -g "$RG" -n "$IDENTITY" --query principalId -o tsv)" \
      --assignee-principal-type ServicePrincipal --role AcrPull \
      --scope "$(az acr show -g "$RG" -n "$REGISTRY" --query id -o tsv)" -o none \
      || die "Couldn't let the app pull images. Your account needs the Owner role on the resource group."

    if [ "$DATABASE" = cosmos ]; then
      DB_URL="$(cosmos_connection)" || die "Couldn't create the Cosmos DB account: Azure's message is above."
      case "$DB_URL" in AccountEndpoint=*) ;; *) die "Couldn't read the Cosmos DB account's connection string." ;; esac
    else
      DB_URL="$(database_url)"
      case "$DB_URL" in postgres://*|postgresql://*) ;; *) die "The database setting doesn't start with postgres://." ;; esac
    fi
    setup_cms_storage
    cms_secrets_notice

    build_image
    ID_RESOURCE="$(az identity show -g "$RG" -n "$IDENTITY" --query id -o tsv)"
    env_args
    exists az containerapp env show -g "$RG" -n "$APP_ENV" \
      || { say "Container Apps environment"; az containerapp env create -g "$RG" -n "$APP_ENV" -l "$LOCATION" -o none; }

    JWT="$(secret_get jwt-key)"
    [ -n "$JWT" ] || JWT="$(random_secret 64)"   # the key that signs sign-ins: made once, kept in the app's secrets
    if exists az containerapp show -g "$RG" -n "$APP"; then
      say "Updating the app"
      az containerapp secret set -g "$RG" -n "$APP" --secrets "jwt-key=${JWT}" "database-url=${DB_URL}" -o none
      az containerapp update -g "$RG" -n "$APP" --image "$IMAGE" --replace-env-vars "${ENV_ARGS[@]}" \
        --min-replicas "$MIN" --max-replicas "$MAX" --cpu "$CPU" --memory "$MEMORY" -o none
    else
      say "Creating the app (${CPU} CPU, ${MEMORY}, ${MIN}-${MAX} copies)"
      az containerapp create -g "$RG" -n "$APP" --environment "$APP_ENV" --image "$IMAGE" \
        --user-assigned "$ID_RESOURCE" --registry-server "${REGISTRY}.azurecr.io" --registry-identity "$ID_RESOURCE" \
        --target-port 8080 --ingress external \
        --min-replicas "$MIN" --max-replicas "$MAX" --cpu "$CPU" --memory "$MEMORY" \
        --secrets "jwt-key=${JWT}" "database-url=${DB_URL}" --env-vars "${ENV_ARGS[@]}" -o none
    fi
    check_health
    if [ "$TIER" = free ]; then
      echo "    Free tier: the app sleeps when idle, so the first visit after a quiet spell takes a few seconds."
      [ "$DATABASE" = cosmos ] && echo "    The database is on Cosmos DB's free tier: it never expires (one such account per subscription)."
      echo "    Not free: the container registry (about \$5/month). Set a budget alert: https://portal.azure.com/#view/Microsoft_Azure_CostManagement/Menu/~/budgets"
    fi
    echo "    Next time, just: ./scripts/cloud/azure.sh deploy"
    ;;

  deploy)
    exists az containerapp show -g "$RG" -n "$APP" || die "No app yet. Run ./scripts/cloud/azure.sh setup first."
    build_image
    env_args
    say "Rolling out ${BUILD}"
    az containerapp update -g "$RG" -n "$APP" --image "$IMAGE" --replace-env-vars "${ENV_ARGS[@]}" -o none
    check_health
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
