#!/usr/bin/env bash
# Run prod on Google Cloud Run, inside its free tier for a small site.
# The step-by-step guide, from a browser only: docs/GOOGLE_CLOUD.md.
#
#   ./scripts/cloud/gcp.sh setup     create (or update) everything; safe to repeat.
#                                    Run it again after changing a setting below.
#   ./scripts/cloud/gcp.sh github    let GitHub Actions deploy every push to main (no keys involved)
#   ./scripts/cloud/gcp.sh deploy    build what's here and roll it out (needs Docker; GitHub Actions runs this)
#   ./scripts/cloud/gcp.sh url       the site's address
#   ./scripts/cloud/gcp.sh logs      the app's recent output
#   ./scripts/cloud/gcp.sh destroy   DELETE what this created
#
# Needs gcloud, logged in. Google Cloud Shell (https://shell.cloud.google.com)
# has it, already logged in, plus git and Docker: nothing to install.
#
# Settings (environment variables, or lines in .env.local; none are required):
#   GCP_TIER           free or standard                    default free. standard = always one copy
#                      running (no cold starts), up to 10, 1 GB, and Cloud SQL (roughly $35-45/month)
#   GCP_PROJECT        project id                  default: gcloud's current project
#   GCP_REGION         region                      default us-central1. Free tier: us-central1, us-east1, us-west1
#   GCP_DATABASE_URL   postgres://... for the app, e.g. a free Neon database. Asked for when
#                      setup first needs one; kept in Secret Manager after that
#   GCP_CLOUD_SQL      true = create Cloud SQL for PostgreSQL instead (NOT free: ~$10/month)
#   GITHUB_REPO        owner/name, for `github`    default: this clone's origin
#   APP_NAME, GOOGLE_CLIENT_ID, CMS_ENABLED, CMS_SOURCE, CMS_GCS_BUCKET...   the app's own
#                      settings. Keep them in cloud.env (committed): every deploy applies them.
. "$(dirname "$0")/common.sh"
need_cli gcloud "Install it (https://cloud.google.com/sdk/docs/install), or run this in Google Cloud Shell."

PROJECT="$(setting GCP_PROJECT "$(gcloud config get-value project 2>/dev/null)")"
[ -n "$PROJECT" ] || die "No Google Cloud project. Pick one first: gcloud config set project YOUR-PROJECT-ID"
REGION="$(setting GCP_REGION us-central1)"
TIER="$(tier GCP_TIER)"
SERVICE="${SLUG}"
export DB_MODULE=postgres   # what the image is built with (build_modules); the pages' storage is added by CMS_SOURCE
REPO="${SLUG}"
SQL_INSTANCE="${SLUG}-db"
REGISTRY="${REGION}-docker.pkg.dev/${PROJECT}/${REPO}"
RUN_SA="${SLUG}-run@${PROJECT}.iam.gserviceaccount.com"        # what the app runs as
DEPLOY_SA="${SLUG}-deploy@${PROJECT}.iam.gserviceaccount.com"  # what GitHub Actions deploys as
JWT_SECRET="${SLUG}-jwt-key"
DB_SECRET="${SLUG}-database-url"
G=(--project "$PROJECT" --quiet)

exists() { "$@" "${G[@]}" >/dev/null 2>&1; }

# A service account made a moment ago can take a little while to be visible
# everywhere (IAM is eventually consistent): try a few times before failing.
retry() {
  local attempt
  for attempt in 1 2 3 4 5 6; do
    "$@" && return 0
    [ "$attempt" = 6 ] || { warn "Not ready yet; trying again in 10 seconds"; sleep 10; }
  done
  return 1
}

# Secret Manager's free tier is 6 active versions: keep one per secret, and
# add a version only when the value really changed.
put_secret() {
  local name="$1" value="$2" old
  if exists gcloud secrets describe "$name"; then
    old="$(gcloud secrets versions access latest --secret "$name" "${G[@]}" 2>/dev/null || true)"
    [ "$old" = "$value" ] && return 0
    printf '%s' "$value" | gcloud secrets versions add "$name" --data-file=- "${G[@]}" >/dev/null
    gcloud secrets versions list "$name" --filter='state=ENABLED' --sort-by='~createTime' --format='value(name)' "${G[@]}" \
      | tail -n +2 | while read -r v; do gcloud secrets versions destroy "${v##*/}" --secret "$name" "${G[@]}" >/dev/null; done
  else
    printf '%s' "$value" | gcloud secrets create "$name" --data-file=- --replication-policy=automatic "${G[@]}" >/dev/null
  fi
}
secret_value() { gcloud secrets versions access latest --secret "$1" "${G[@]}" 2>/dev/null || true; }

service_url() { gcloud run services describe "$SERVICE" --region "$REGION" "${G[@]}" --format 'value(status.url)' 2>/dev/null || true; }

# The app's public settings, from cloud.env (or .env.local, or the
# environment): every one, empty or not, so removing a value takes effect.
# "^@^" makes @ the separator, so values may contain commas.
app_env() {
  local pairs=("App__Environment=prod" "App__Name=$(setting APP_NAME WebBase)"
    "Auth__Google__ClientId=$(setting GOOGLE_CLIENT_ID)" "Auth__AuthorEmails=$(setting AUTHOR_EMAILS)" "Cms__Enabled=$(setting CMS_ENABLED false)") pair
  for pair in "${CMS_SETTINGS[@]}"; do pairs+=("${pair%%:*}=$(setting "${pair#*:}")"); done
  local IFS=@
  echo "^@^${pairs[*]}"
}

# Free: scale to zero, two copies at most (caps the bill), the smallest memory.
# Standard: one copy always running, so no cold starts.
if [ "$TIER" = free ]; then SIZE=(--min-instances 0 --max-instances 2 --memory 512Mi); SQL_TIER=db-f1-micro
else SIZE=(--min-instances 1 --max-instances 10 --memory 1Gi); SQL_TIER=db-g1-small; fi

# Everything about the service that setup decides.
service_config() {
  CONFIG=(--service-account "$RUN_SA" --port 8080
    # Request-based billing (CPU only while answering) and scale to zero:
    # what Cloud Run's free tier covers. Two instances at most caps the bill.
    # A request may take 60 seconds at most (the default is 5 minutes).
    --cpu-throttling --cpu 1 --timeout 60 "${SIZE[@]}"
    --set-env-vars "$(app_env)"
    --set-secrets "Jwt__Key=${JWT_SECRET}:latest,DATABASE_URL=${DB_SECRET}:latest")
  # Cloud SQL is reached through a socket Cloud Run mounts at /cloudsql/...
  if secret_value "$DB_SECRET" | grep -q '/cloudsql/'; then
    CONFIG+=(--add-cloudsql-instances "${PROJECT}:${REGION}:${SQL_INSTANCE}")
  fi
}

build_image() {
  gcloud auth configure-docker "${REGION}-docker.pkg.dev" --quiet >/dev/null 2>&1
  BUILD="$(build_id)"
  IMAGE="${REGISTRY}/${SERVICE}:${BUILD}"
  build_and_push "$IMAGE" "$BUILD"
}

check_health() {
  local url; url="$(service_url)"
  say "Checking ${url}/api/health"
  wait_for_url "${url}/api/health" 90 || die "The new version doesn't answer. See why: ./scripts/cloud/gcp.sh logs"
  ok "It works: ${url}"
}

setup_database() {
  local url; url="$(setting GCP_DATABASE_URL)"
  if [ -n "$url" ]; then
    put_secret "$DB_SECRET" "$url"
  elif [ "$(setting GCP_CLOUD_SQL)" = true ] || [ "$TIER" = standard ]; then
    setup_cloud_sql
  elif exists gcloud secrets describe "$DB_SECRET"; then
    ok "Database: keeping the one already set up"
  elif [ -t 0 ]; then
    echo
    bold "The app needs a PostgreSQL database."
    echo "  Free: create one at https://neon.tech (or https://supabase.com) and copy its"
    echo "  connection string, postgres://... It's kept in Secret Manager, not shown here."
    echo "  Or press Enter for Cloud SQL, Google's own (about \$10/month, not free)."
    read -r -s -p "  Connection string: " url; echo
    if [ -n "$url" ]; then put_secret "$DB_SECRET" "$url"; else setup_cloud_sql; fi
  else
    die "No database. Set GCP_DATABASE_URL=postgres://... (or GCP_CLOUD_SQL=true) and run setup again."
  fi
  if ! secret_value "$DB_SECRET" | grep -qE '^postgres(ql)?://'; then
    die "The database setting doesn't start with postgres://. Run setup again with GCP_DATABASE_URL=postgres://..."
  fi
}

setup_cloud_sql() {
  warn "Cloud SQL isn't in the free tier: about \$10/month (db-f1-micro) or \$26 (db-g1-small, GCP_TIER=standard) while it exists."
  gcloud services enable sqladmin.googleapis.com "${G[@]}"
  say "Cloud SQL instance ${SQL_INSTANCE} (the slowest step: 5-10 minutes)"
  exists gcloud sql instances describe "$SQL_INSTANCE" \
    || gcloud sql instances create "$SQL_INSTANCE" "${G[@]}" --region "$REGION" \
         --database-version POSTGRES_17 --edition ENTERPRISE --tier "$SQL_TIER" --storage-size 10
  exists gcloud sql databases describe "$SLUG" --instance "$SQL_INSTANCE" \
    || gcloud sql databases create "$SLUG" --instance "$SQL_INSTANCE" "${G[@]}"
  if ! secret_value "$DB_SECRET" | grep -q '/cloudsql/'; then
    local password; password="$(random_secret 32)"
    gcloud sql users create "${SLUG}app" --instance "$SQL_INSTANCE" --password "$password" "${G[@]}" 2>/dev/null \
      || gcloud sql users set-password "${SLUG}app" --instance "$SQL_INSTANCE" --password "$password" "${G[@]}"
    # Host is the socket folder Cloud Run mounts; the app passes it through as Host=/cloudsql/....
    put_secret "$DB_SECRET" "postgres://${SLUG}app:${password}@localhost/${SLUG}?host=/cloudsql/${PROJECT}:${REGION}:${SQL_INSTANCE}"
  fi
  retry gcloud projects add-iam-policy-binding "$PROJECT" --member "serviceAccount:${RUN_SA}" \
    --role roles/cloudsql.client --condition=None "${G[@]}" >/dev/null
}

# The CMS's content in a Cloud Storage bucket, read with the app's own identity (docs/CMS.md).
setup_cms_bucket() {
  [ "$(setting CMS_SOURCE)" = gcs ] || return 0
  local bucket; bucket="$(setting CMS_GCS_BUCKET)"
  [ -n "$bucket" ] || die "CMS_SOURCE is gcs, so set CMS_GCS_BUCKET to a bucket name (unique across all of Google)."
  say "Bucket gs://${bucket} for the CMS"
  exists gcloud storage buckets describe "gs://${bucket}" \
    || gcloud storage buckets create "gs://${bucket}" --location "$REGION" \
         --uniform-bucket-level-access --public-access-prevention "${G[@]}"
  retry gcloud storage buckets add-iam-policy-binding "gs://${bucket}" --member "serviceAccount:${RUN_SA}" \
    --role roles/storage.objectViewer "${G[@]}" >/dev/null
}

case "${1:-}" in
  setup)
    say "Project ${PROJECT}, region ${REGION}"
    case "$REGION" in us-central1|us-east1|us-west1) ;; *) warn "Cloud Storage's free tier only covers us-central1, us-east1 and us-west1." ;; esac
    say "Turning on the services this needs (the first time takes a minute)"
    gcloud services enable run.googleapis.com artifactregistry.googleapis.com secretmanager.googleapis.com \
      iam.googleapis.com storage.googleapis.com "${G[@]}"

    say "Image repository"
    exists gcloud artifacts repositories describe "$REPO" --location "$REGION" \
      || gcloud artifacts repositories create "$REPO" --repository-format=docker --location "$REGION" "${G[@]}"
    # The free tier stores 0.5 GB of images: keep the last 3 releases, delete older ones.
    policy="$(mktemp)"
    cat > "$policy" <<'JSON'
[{"name": "keep-last-3", "action": {"type": "Keep"}, "mostRecentVersions": {"keepCount": 3}},
 {"name": "delete-older", "action": {"type": "Delete"}, "condition": {"tagState": "any", "olderThan": "1d"}}]
JSON
    gcloud artifacts repositories set-cleanup-policies "$REPO" --location "$REGION" --policy "$policy" --no-dry-run "${G[@]}" >/dev/null
    rm -f "$policy"

    say "The app's own account (${RUN_SA%%@*}), allowed only what it needs"
    exists gcloud iam service-accounts describe "$RUN_SA" \
      || gcloud iam service-accounts create "${SLUG}-run" --display-name "${SLUG} website" "${G[@]}"

    say "Secrets"
    jwt="$(setting JWT_KEY)"
    if [ -n "$jwt" ]; then put_secret "$JWT_SECRET" "$jwt"
    elif ! exists gcloud secrets describe "$JWT_SECRET"; then put_secret "$JWT_SECRET" "$(random_secret 64)"; fi
    setup_database
    for s in "$JWT_SECRET" "$DB_SECRET"; do
      retry gcloud secrets add-iam-policy-binding "$s" --member "serviceAccount:${RUN_SA}" \
        --role roles/secretmanager.secretAccessor "${G[@]}" >/dev/null
    done
    setup_cms_bucket
    cms_secrets_notice

    service_config
    if can_build; then
      build_image
      say "Deploying to Cloud Run"
      gcloud run deploy "$SERVICE" --image "$IMAGE" --region "$REGION" --allow-unauthenticated "${CONFIG[@]}" "${G[@]}"
      check_health
    elif exists gcloud run services describe "$SERVICE" --region "$REGION"; then
      say "Updating the service's settings (no Docker here, so the app itself stays as it is)"
      gcloud run services update "$SERVICE" --region "$REGION" "${CONFIG[@]}" "${G[@]}"
    else
      say "Creating the service, with Google's sample page until the first real deploy"
      gcloud run deploy "$SERVICE" --image us-docker.pkg.dev/cloudrun/container/hello --region "$REGION" \
        --allow-unauthenticated "${CONFIG[@]}" "${G[@]}"
      ok "Set up. The address is ready: $(service_url)"
      echo "    It shows a placeholder until the first deploy. Next: ./scripts/cloud/gcp.sh github"
      exit 0
    fi
    echo "    Deploy each push to GitHub automatically: ./scripts/cloud/gcp.sh github"
    ;;

  github)
    origin="$(git remote get-url origin 2>/dev/null || true)"
    GITHUB_REPO="$(setting GITHUB_REPO "$(echo "$origin" | sed -E 's#^(git@github\.com:|https://github\.com/)##; s#\.git$##')")"
    [[ "$GITHUB_REPO" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] \
      || die "Which GitHub repository? Run with GITHUB_REPO=owner/name ./scripts/cloud/gcp.sh github"
    exists gcloud run services describe "$SERVICE" --region "$REGION" || die "Run ./scripts/cloud/gcp.sh setup first."
    say "Letting GitHub Actions in ${GITHUB_REPO} deploy to ${PROJECT}"
    gcloud services enable iamcredentials.googleapis.com sts.googleapis.com "${G[@]}"
    number="$(gcloud projects describe "$PROJECT" --format 'value(projectNumber)')"
    POOL=github
    PROVIDER="gh-${SLUG}"; PROVIDER="${PROVIDER:0:32}"
    # Workload Identity Federation: GitHub proves "this is a workflow run in
    # GITHUB_REPO", Google trusts exactly that. No key is ever created or stored.
    state="$(gcloud iam workload-identity-pools describe "$POOL" --location global "${G[@]}" --format 'value(state)' 2>/dev/null || true)"
    if [ -z "$state" ]; then
      gcloud iam workload-identity-pools create "$POOL" --location global --display-name "GitHub Actions" "${G[@]}"
    elif [ "$state" = DELETED ]; then
      gcloud iam workload-identity-pools undelete "$POOL" --location global "${G[@]}"
    fi
    condition="assertion.repository=='${GITHUB_REPO}'"
    state="$(gcloud iam workload-identity-pools providers describe "$PROVIDER" --workload-identity-pool "$POOL" --location global "${G[@]}" --format 'value(state)' 2>/dev/null || true)"
    if [ -z "$state" ]; then
      gcloud iam workload-identity-pools providers create-oidc "$PROVIDER" --workload-identity-pool "$POOL" --location global \
        --issuer-uri https://token.actions.githubusercontent.com \
        --attribute-mapping 'google.subject=assertion.sub,attribute.repository=assertion.repository' \
        --attribute-condition "$condition" "${G[@]}"
    else
      [ "$state" = DELETED ] && gcloud iam workload-identity-pools providers undelete "$PROVIDER" --workload-identity-pool "$POOL" --location global "${G[@]}"
      gcloud iam workload-identity-pools providers update-oidc "$PROVIDER" --workload-identity-pool "$POOL" --location global \
        --attribute-condition "$condition" "${G[@]}" >/dev/null
    fi

    exists gcloud iam service-accounts describe "$DEPLOY_SA" \
      || gcloud iam service-accounts create "${SLUG}-deploy" --display-name "${SLUG} deploys from GitHub" "${G[@]}"
    # Deploy new versions of the service, upload images, and run them as the app's account. Nothing else.
    retry gcloud projects add-iam-policy-binding "$PROJECT" --member "serviceAccount:${DEPLOY_SA}" \
      --role roles/run.developer --condition=None "${G[@]}" >/dev/null
    retry gcloud artifacts repositories add-iam-policy-binding "$REPO" --location "$REGION" \
      --member "serviceAccount:${DEPLOY_SA}" --role roles/artifactregistry.writer "${G[@]}" >/dev/null
    retry gcloud iam service-accounts add-iam-policy-binding "$RUN_SA" --member "serviceAccount:${DEPLOY_SA}" \
      --role roles/iam.serviceAccountUser "${G[@]}" >/dev/null
    retry gcloud iam service-accounts add-iam-policy-binding "$DEPLOY_SA" --role roles/iam.workloadIdentityUser \
      --member "principalSet://iam.googleapis.com/projects/${number}/locations/global/workloadIdentityPools/${POOL}/attribute.repository/${GITHUB_REPO}" \
      "${G[@]}" >/dev/null

    VARS=(GCP_PROJECT="$PROJECT" GCP_REGION="$REGION"
      GCP_WORKLOAD_IDENTITY_PROVIDER="projects/${number}/locations/global/workloadIdentityPools/${POOL}/providers/${PROVIDER}"
      GCP_SERVICE_ACCOUNT="$DEPLOY_SA")
    if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
      for v in "${VARS[@]}"; do gh variable set "${v%%=*}" --body "${v#*=}" -R "$GITHUB_REPO" >/dev/null; done
      ok "Done, and the repository's variables are set. The next push to main deploys."
    else
      ok "Done on Google's side. One step left, on GitHub:"
      echo "    https://github.com/${GITHUB_REPO}/settings/variables/actions -> New repository variable, four times."
      echo "    (Variables, not secrets: none of these is secret.)"
      echo
      for v in "${VARS[@]}"; do printf '      %-32s %s\n' "${v%%=*}" "${v#*=}"; done
      echo
      echo "    Then the next push to main deploys, or: Actions -> Deploy to Google Cloud -> Run workflow."
    fi
    ;;

  deploy)
    can_build || die "Building needs Docker. (GitHub Actions has it: ./scripts/cloud/gcp.sh github sets that up.)"
    exists gcloud run services describe "$SERVICE" --region "$REGION" || die "Run ./scripts/cloud/gcp.sh setup first."
    build_image
    say "Rolling out ${BUILD}"
    # The new image, and cloud.env's settings; secrets and sizes stay as setup left them.
    gcloud run services update "$SERVICE" --region "$REGION" --image "$IMAGE" --update-env-vars "$(app_env)" "${G[@]}"
    check_health
    ;;

  url)
    service_url ;;
  logs)
    gcloud run services logs read "$SERVICE" --region "$REGION" --limit 100 "${G[@]}" ;;
  destroy)
    warn "This deletes the Cloud Run service, its images, its secrets (including the database connection string),"
    warn "both service accounts, the GitHub connection, and Cloud SQL WITH ITS DATA if setup made one."
    warn "Not deleted: a CMS bucket, and any database outside Google (Neon...): delete those yourself."
    confirm "Really delete them?" || exit 1
    gcloud run services delete "$SERVICE" --region "$REGION" "${G[@]}" || true
    exists gcloud sql instances describe "$SQL_INSTANCE" && { gcloud sql instances delete "$SQL_INSTANCE" "${G[@]}" || true; }
    gcloud artifacts repositories delete "$REPO" --location "$REGION" "${G[@]}" || true
    for s in "$JWT_SECRET" "$DB_SECRET"; do gcloud secrets delete "$s" "${G[@]}" || true; done
    gcloud iam workload-identity-pools providers delete "gh-${SLUG}" --workload-identity-pool github --location global "${G[@]}" 2>/dev/null || true
    for sa in "$DEPLOY_SA" "$RUN_SA"; do gcloud iam service-accounts delete "$sa" "${G[@]}" 2>/dev/null || true; done
    ok "Deleted."
    ;;
  *)
    sed -n '2,26p' "$0"; exit 1 ;;
esac
