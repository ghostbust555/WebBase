#!/usr/bin/env bash
# Run prod on Google Cloud: Cloud Run (the website + API container) and Cloud
# SQL for PostgreSQL (the database).
#
#   ./scripts/cloud/gcp.sh setup     one-time: create everything and deploy
#   ./scripts/cloud/gcp.sh deploy    build what's here now and roll it out
#   ./scripts/cloud/gcp.sh url       the site's address
#   ./scripts/cloud/gcp.sh logs      the app's recent output
#   ./scripts/cloud/gcp.sh destroy   DELETE what this created (and its data)
#
# Needs the gcloud CLI (https://cloud.google.com/sdk/docs/install), logged in
# (gcloud auth login) with a project that has billing turned on.
# Read docs/CLOUD.md first - it explains costs and every setting below.
#
# Settings (environment variables or lines in .env.local):
#   GCP_PROJECT        project id               default: gcloud's current project
#   GCP_REGION         region                   default us-central1
#   GCP_DATABASE_URL   use your own Postgres instead of creating Cloud SQL
. "$(dirname "$0")/common.sh"
need_docker
need_cli gcloud "Install it: https://cloud.google.com/sdk/docs/install"

PROJECT="$(setting GCP_PROJECT "$(gcloud config get-value project 2>/dev/null)")"
[ -n "$PROJECT" ] || die "No Google Cloud project. Set GCP_PROJECT=your-project-id in .env.local."
REGION="$(setting GCP_REGION us-central1)"
SERVICE="${SLUG}"
REPO="${SLUG}"
SQL_INSTANCE="${SLUG}-db"
REGISTRY="${REGION}-docker.pkg.dev/${PROJECT}/${REPO}"
G=(--project "$PROJECT")

# Secrets live in Secret Manager; Cloud Run reads them at start. They are
# created from .env.local and updated whenever this runs.
put_secret() {
  local name="$1" value="$2"
  if gcloud secrets describe "$name" "${G[@]}" >/dev/null 2>&1; then
    printf '%s' "$value" | gcloud secrets versions add "$name" --data-file=- "${G[@]}" >/dev/null
  else
    printf '%s' "$value" | gcloud secrets create "$name" --data-file=- --replication-policy=automatic "${G[@]}" >/dev/null
  fi
}

deploy_service() {
  local image="$1" sql=()
  # Cloud SQL is reached through a socket Cloud Run mounts at /cloudsql/...
  [ -z "$(setting GCP_DATABASE_URL)" ] && sql=(--add-cloudsql-instances "${PROJECT}:${REGION}:${SQL_INSTANCE}")
  # "^@^" makes @ the separator, so values may contain commas.
  gcloud run deploy "$SERVICE" "${G[@]}" --region "$REGION" --image "$image" \
    --port 8080 --allow-unauthenticated --min-instances 0 --max-instances 3 --memory 512Mi \
    "${sql[@]}" \
    --set-env-vars "^@^App__Environment=prod@App__Name=$(setting APP_NAME WebBase)@Auth__Google__ClientId=$(setting GOOGLE_CLIENT_ID)@$(cms_env | paste -sd@ -)" \
    --set-secrets "Jwt__Key=${SLUG}-jwt-key:latest,DATABASE_URL=${SLUG}-database-url:latest" \
    --quiet
}

cms_secrets_notice

case "${1:-}" in
  setup)
    need_secrets
    say "Project ${PROJECT}, region ${REGION}"
    say "Turning on the services this needs"
    gcloud services enable run.googleapis.com artifactregistry.googleapis.com \
      secretmanager.googleapis.com sqladmin.googleapis.com "${G[@]}"

    say "Image repository"
    gcloud artifacts repositories describe "$REPO" --location "$REGION" "${G[@]}" >/dev/null 2>&1 \
      || gcloud artifacts repositories create "$REPO" --repository-format=docker --location "$REGION" "${G[@]}"
    gcloud auth configure-docker "${REGION}-docker.pkg.dev" --quiet

    DB_URL="$(setting GCP_DATABASE_URL)"
    if [ -z "$DB_URL" ]; then
      if [ -z "$(setting GCP_DB_PASSWORD)" ]; then remember GCP_DB_PASSWORD "$(random_secret 32)"; fi
      say "Cloud SQL instance ${SQL_INSTANCE} (the slowest step: 5-10 minutes)"
      gcloud sql instances describe "$SQL_INSTANCE" "${G[@]}" >/dev/null 2>&1 \
        || gcloud sql instances create "$SQL_INSTANCE" "${G[@]}" --region "$REGION" \
             --database-version POSTGRES_17 --edition ENTERPRISE --tier db-f1-micro --storage-size 10
      gcloud sql databases describe "$SLUG" --instance "$SQL_INSTANCE" "${G[@]}" >/dev/null 2>&1 \
        || gcloud sql databases create "$SLUG" --instance "$SQL_INSTANCE" "${G[@]}"
      gcloud sql users create "${SLUG}app" --instance "$SQL_INSTANCE" --password "$(setting GCP_DB_PASSWORD)" "${G[@]}" 2>/dev/null \
        || gcloud sql users set-password "${SLUG}app" --instance "$SQL_INSTANCE" --password "$(setting GCP_DB_PASSWORD)" "${G[@]}"
      # Host is the socket folder Cloud Run mounts; the app's DATABASE_URL
      # reader passes it through as Host=/cloudsql/....
      DB_URL="postgres://${SLUG}app:$(setting GCP_DB_PASSWORD)@localhost/${SLUG}?host=/cloudsql/${PROJECT}:${REGION}:${SQL_INSTANCE}"
    fi

    say "Secrets"
    put_secret "${SLUG}-jwt-key" "$(setting JWT_KEY)"
    put_secret "${SLUG}-database-url" "$DB_URL"
    # Cloud Run runs as the default compute service account; let it read them.
    SA="$(gcloud projects describe "$PROJECT" --format='value(projectNumber)')-compute@developer.gserviceaccount.com"
    for s in jwt-key database-url; do
      gcloud secrets add-iam-policy-binding "${SLUG}-${s}" "${G[@]}" \
        --member "serviceAccount:${SA}" --role roles/secretmanager.secretAccessor >/dev/null
    done
    gcloud projects add-iam-policy-binding "$PROJECT" --member "serviceAccount:${SA}" \
      --role roles/cloudsql.client --condition=None >/dev/null

    BUILD="$(build_id)"
    build_and_push "${REGISTRY}/${SERVICE}:${BUILD}" "$BUILD"
    say "Deploying to Cloud Run"
    deploy_service "${REGISTRY}/${SERVICE}:${BUILD}"
    ok "Deployed. Your site: $(gcloud run services describe "$SERVICE" --region "$REGION" "${G[@]}" --format 'value(status.url)')"
    echo "    Next time, just: ./scripts/cloud/gcp.sh deploy"
    ;;

  deploy)
    BUILD="$(build_id)"
    build_and_push "${REGISTRY}/${SERVICE}:${BUILD}" "$BUILD"
    say "Rolling out ${BUILD}"
    deploy_service "${REGISTRY}/${SERVICE}:${BUILD}"
    ok "Deployed ${BUILD}: $(gcloud run services describe "$SERVICE" --region "$REGION" "${G[@]}" --format 'value(status.url)')"
    ;;

  url)
    gcloud run services describe "$SERVICE" --region "$REGION" "${G[@]}" --format 'value(status.url)' ;;
  logs)
    gcloud run services logs read "$SERVICE" --region "$REGION" "${G[@]}" --limit 100 ;;
  destroy)
    warn "This deletes the Cloud Run service, the image repository, the secrets and the Cloud SQL DATABASE with all its data."
    confirm "Really delete them?" || exit 1
    gcloud run services delete "$SERVICE" --region "$REGION" "${G[@]}" --quiet || true
    gcloud sql instances delete "$SQL_INSTANCE" "${G[@]}" --quiet || true
    gcloud artifacts repositories delete "$REPO" --location "$REGION" "${G[@]}" --quiet || true
    gcloud secrets delete "${SLUG}-jwt-key" "${G[@]}" --quiet || true
    gcloud secrets delete "${SLUG}-database-url" "${G[@]}" --quiet || true
    ok "Deleted."
    ;;
  *)
    sed -n '2,18p' "$0"; exit 1 ;;
esac
