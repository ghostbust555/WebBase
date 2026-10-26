#!/usr/bin/env bash
# Run prod on Microsoft Azure: Azure Container Apps (the website + API
# container) and Azure Database for PostgreSQL (the database).
#
#   ./scripts/cloud/azure.sh setup     one-time: create everything and deploy
#   ./scripts/cloud/azure.sh deploy    build what's here now and roll it out
#   ./scripts/cloud/azure.sh url       the site's address
#   ./scripts/cloud/azure.sh logs      watch the app's output
#   ./scripts/cloud/azure.sh destroy   DELETE everything this created (and its data)
#
# Needs the Azure CLI (https://aka.ms/installazurecli), logged in: az login
# Read docs/CLOUD.md first - it explains costs and every setting below.
#
# Settings (environment variables or lines in .env.local; all optional):
#   AZURE_LOCATION         region                  default eastus
#   AZURE_RESOURCE_GROUP   folder for everything   default webbase-rg
#   AZURE_DATABASE_URL     use your own Postgres instead of creating one
. "$(dirname "$0")/common.sh"
need_docker
need_cli az "Install it: https://aka.ms/installazurecli"

LOCATION="$(setting AZURE_LOCATION eastus)"
RG="$(setting AZURE_RESOURCE_GROUP "${SLUG}-rg")"
APP="${SLUG}"
ENVIRONMENT="${SLUG}-env"
# Registry and database server names must be unique across all of Azure, so
# a random suffix is chosen once and remembered.
SUFFIX="$(setting AZURE_SUFFIX)"
if [ -z "$SUFFIX" ]; then SUFFIX="$(random_secret 6 | tr '[:upper:]' '[:lower:]')"; remember AZURE_SUFFIX "$SUFFIX"; fi
REGISTRY="${SLUG//-/}${SUFFIX}"
PG_SERVER="${SLUG}-db-${SUFFIX}"

registry_login() {
  az acr login --name "$REGISTRY" >/dev/null
}

database_url() {
  local own; own="$(setting AZURE_DATABASE_URL)"
  if [ -n "$own" ]; then echo "$own"; return; fi
  echo "postgres://${SLUG}admin:$(setting AZURE_DB_PASSWORD)@${PG_SERVER}.postgres.database.azure.com:5432/${SLUG}?sslmode=require"
}

case "${1:-}" in
  setup)
    need_secrets
    az account show >/dev/null 2>&1 || die "Not logged in to Azure. Run: az login"
    say "Using subscription: $(az account show --query name -o tsv)"
    say "Registering the Azure services this needs (first time only, can take a few minutes)"
    for ns in Microsoft.App Microsoft.OperationalInsights Microsoft.ContainerRegistry Microsoft.DBforPostgreSQL; do
      az provider register --namespace "$ns" --wait >/dev/null
    done
    az extension add --name containerapp --upgrade --only-show-errors >/dev/null

    say "Resource group ${RG} in ${LOCATION}"
    az group create -n "$RG" -l "$LOCATION" -o none

    say "Container registry ${REGISTRY}"
    az acr create -g "$RG" -n "$REGISTRY" --sku Basic --admin-enabled true -o none

    if [ -z "$(setting AZURE_DATABASE_URL)" ]; then
      if [ -z "$(setting AZURE_DB_PASSWORD)" ]; then remember AZURE_DB_PASSWORD "$(random_secret 32)"; fi
      say "PostgreSQL server ${PG_SERVER} (the slowest step: 5-10 minutes)"
      # Burstable B1ms is the smallest and cheapest size. --public-access
      # 0.0.0.0 means "only from Azure services", not the whole internet.
      az postgres flexible-server create -g "$RG" -n "$PG_SERVER" -l "$LOCATION" \
        --admin-user "${SLUG}admin" --admin-password "$(setting AZURE_DB_PASSWORD)" \
        --tier Burstable --sku-name Standard_B1ms --storage-size 32 --version 17 \
        --public-access 0.0.0.0 --yes -o none
      az postgres flexible-server db create -g "$RG" -s "$PG_SERVER" -d "$SLUG" -o none
    fi

    BUILD="$(build_id)"
    IMAGE="${REGISTRY}.azurecr.io/${APP}:${BUILD}"
    registry_login
    build_and_push "$IMAGE" "$BUILD"

    say "Container Apps environment and app"
    az containerapp env create -g "$RG" -n "$ENVIRONMENT" -l "$LOCATION" -o none
    az containerapp create -g "$RG" -n "$APP" --environment "$ENVIRONMENT" \
      --image "$IMAGE" --target-port 8080 --ingress external \
      --registry-server "${REGISTRY}.azurecr.io" \
      --registry-username "$(az acr credential show -n "$REGISTRY" --query username -o tsv)" \
      --registry-password "$(az acr credential show -n "$REGISTRY" --query 'passwords[0].value' -o tsv)" \
      --min-replicas 1 --max-replicas 3 --cpu 0.5 --memory 1.0Gi \
      --secrets "jwt-key=$(setting JWT_KEY)" "database-url=$(database_url)" \
      --env-vars "App__Environment=prod" "App__Name=$(setting APP_NAME WebBase)" \
                 "Jwt__Key=secretref:jwt-key" "DATABASE_URL=secretref:database-url" \
                 "Auth__Google__ClientId=$(setting GOOGLE_CLIENT_ID)" \
                 "Cms__Enabled=$(setting CMS_ENABLED false)" \
      -o none
    ok "Deployed. Your site: https://$(az containerapp show -g "$RG" -n "$APP" --query properties.configuration.ingress.fqdn -o tsv)"
    echo "    Next time, just: ./scripts/cloud/azure.sh deploy"
    ;;

  deploy)
    BUILD="$(build_id)"
    IMAGE="${REGISTRY}.azurecr.io/${APP}:${BUILD}"
    registry_login
    build_and_push "$IMAGE" "$BUILD"
    say "Rolling out ${BUILD}"
    az containerapp update -g "$RG" -n "$APP" --image "$IMAGE" \
      --set-env-vars "Cms__Enabled=$(setting CMS_ENABLED false)" "Auth__Google__ClientId=$(setting GOOGLE_CLIENT_ID)" -o none
    ok "Deployed ${BUILD}: https://$(az containerapp show -g "$RG" -n "$APP" --query properties.configuration.ingress.fqdn -o tsv)"
    ;;

  url)
    echo "https://$(az containerapp show -g "$RG" -n "$APP" --query properties.configuration.ingress.fqdn -o tsv)" ;;
  logs)
    az containerapp logs show -g "$RG" -n "$APP" --follow ;;
  destroy)
    warn "This deletes resource group ${RG}: the app, the registry and the DATABASE with all its data."
    confirm "Really delete everything in ${RG}?" || exit 1
    az group delete -n "$RG" --yes
    ok "Deleted ${RG}."
    ;;
  *)
    sed -n '2,18p' "$0"; exit 1 ;;
esac
