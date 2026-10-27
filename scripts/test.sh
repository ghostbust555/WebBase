#!/usr/bin/env bash
# Run every check: the C# tests of the app and of every module (WebBase.slnx),
# the databases and file stores against real PostgreSQL and S3 / Azure Blob /
# Google Cloud Storage stand-ins started just for this,
# that client/src/api-types.ts matches the C# types, and the website's type
# check and build.
# Inside Docker, so nothing but Docker needs installing. With no Docker daemon
# (cloud coding-agent containers, CI runners without Docker) it uses the host's own
# dotnet and node instead, set up by ./setupenvironment.sh (docs/ENVIRONMENT.md):
# the Postgres tests run when TEST_POSTGRES names a superuser connection (setupenvironment
# writes one to .env.host); the S3 / Azure / Google stand-ins are Docker containers, so
# those module tests are skipped there.
#
#   ./scripts/test.sh            Docker if a daemon answers, else host tools
#   ./scripts/test.sh --host     force the host's tools
#   ./scripts/test.sh --docker   force Docker
. "$(dirname "$0")/lib.sh"
case "${1:-}" in
  --host) export WEBBASE_MODE=host ;;
  --docker) export WEBBASE_MODE=docker ;;
  "") ;;
  *) die "Usage: ./scripts/test.sh [--host|--docker]" ;;
esac

if [ "$(run_mode)" = host ]; then
  load_host_env
  say "No Docker: running the checks with this computer's own tools"
  command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -qE '^10\.' \
    || die "The .NET 10 SDK isn't available. Run ./setupenvironment.sh (it needs no Docker)."
  command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1 || die "Node isn't available. Run ./setupenvironment.sh."
  if [ -n "${TEST_POSTGRES:-}" ]; then
    ok "The Postgres module tests run, against the server in TEST_POSTGRES"
  else
    warn "TEST_POSTGRES isn't set, so the Postgres module tests are skipped. ./setupenvironment.sh sets it up (needs PostgreSQL)."
  fi
  warn "The S3, Azure Blob and Google Cloud Storage module tests need their Docker stand-ins and are skipped here."
  if [ -n "${TEST_FIRESTORE:-}" ]; then
    ok "The Firestore module tests run, against the emulator at ${TEST_FIRESTORE}"
  else
    warn "TEST_FIRESTORE isn't set, so the Firestore module tests are skipped (modules/WebBase.Database.Firestore/README.md: start its emulator)."
  fi

  say "C# tests"
  DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet test WebBase.slnx --nologo

  say "Website: type check and build"
  if ! npm_deps_current; then npm_install_deps || die "npm ci failed. See docs/ENVIRONMENT.md."; fi
  (cd client && npm run build)

  ok "All checks passed (host mode)"
  exit 0
fi

need_docker

# Throwaway servers for the tests that need real ones, on their own network,
# removed again however this script ends:
#   PostgreSQL       the Postgres module (tests/WebBase.Database.Postgres.Tests)
#   s3mock           an S3 stand-in, for WebBase.FileStorage.S3
#   Azurite          Microsoft's Azure Storage emulator, for WebBase.FileStorage.AzureBlob
#   fake-gcs-server  a Google Cloud Storage stand-in, for WebBase.FileStorage.GoogleCloud
# (The Cosmos DB and Firestore emulators aren't started here: each module's README says how.)
NET="${SLUG}-test-$$"
DB="${SLUG}-test-db-$$"
S3="${SLUG}-test-s3-$$"
BLOB="${SLUG}-test-blob-$$"
GCS="${SLUG}-test-gcs-$$"
cleanup() { docker rm -f "$DB" "$S3" "$BLOB" "$GCS" >/dev/null 2>&1; docker network rm "$NET" >/dev/null 2>&1; true; }
trap cleanup EXIT
docker network create "$NET" >/dev/null
docker run -d --rm --name "$DB" --network "$NET" -e POSTGRES_PASSWORD=test --tmpfs /var/lib/postgresql \
  postgres:18.6-alpine >/dev/null
docker run -d --rm --name "$S3" --network "$NET" adobe/s3mock:5.2.3 >/dev/null
# Azurite's account key is made up on the spot: nothing to keep secret, nothing committed.
BLOB_KEY="$(head -c 32 /dev/urandom | base64)"
docker run -d --rm --name "$BLOB" --network "$NET" -e "AZURITE_ACCOUNTS=webbasetest:${BLOB_KEY}" \
  mcr.microsoft.com/azure-storage/azurite:3.35.0 \
  azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck --loose >/dev/null
docker run -d --rm --name "$GCS" --network "$NET" fsouza/fake-gcs-server:1.56.1 \
  -scheme http -port 4443 -public-host "${GCS}:4443" >/dev/null
for _ in $(seq 60); do
  docker exec "$DB" pg_isready -U postgres -h 127.0.0.1 >/dev/null 2>&1 && break
  sleep 1
done

say "C# tests"
docker run --rm --network "$NET" \
  -v "$ROOT:/src" -w /src \
  -v /src/.build \
  -v "${SLUG}-dev_nuget:/root/.nuget/packages" \
  -e "TEST_POSTGRES=Host=${DB};Username=postgres;Password=test" \
  -e "TEST_S3_URL=http://${S3}:9090" \
  -e "TEST_GCS_URL=http://${GCS}:4443" \
  -e "TEST_AZURE_BLOB=DefaultEndpointsProtocol=http;AccountName=webbasetest;AccountKey=${BLOB_KEY};BlobEndpoint=http://${BLOB}:10000/webbasetest;" \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test WebBase.slnx --nologo

say "Website: type check and build"
# The whole repository, read-only: the build reads docs/ and README.md too.
# node_modules and dist are the container's own, writable; their mount points
# must exist beforehand, since nothing can create them in a read-only folder.
mkdir -p "$ROOT/client/node_modules" "$ROOT/client/dist"
docker run --rm \
  -v "$ROOT:/src:ro" -w /src/client -v /src/client/node_modules -v /src/client/dist \
  node:22-alpine sh -c 'npm ci --no-audit --no-fund --loglevel=error && npm run build'

ok "All checks passed"
