#!/usr/bin/env bash
# Run every check: the C# tests (including the database stores, against a
# real PostgreSQL started just for this) and the website's type check and build.
# Inside Docker, so nothing but Docker needs installing.
#
#   ./scripts/test.sh
. "$(dirname "$0")/lib.sh"
need_docker

# A throwaway PostgreSQL for the store contract tests (tests/WebBase.Tests/
# StoreContract), on its own network. Removed again however this script ends.
NET="${SLUG}-test-$$"
DB="${SLUG}-test-db-$$"
cleanup() { docker rm -f "$DB" >/dev/null 2>&1; docker network rm "$NET" >/dev/null 2>&1; true; }
trap cleanup EXIT
docker network create "$NET" >/dev/null
docker run -d --rm --name "$DB" --network "$NET" -e POSTGRES_PASSWORD=test --tmpfs /var/lib/postgresql \
  postgres:18.6-alpine >/dev/null
for _ in $(seq 60); do
  docker exec "$DB" pg_isready -U postgres -h 127.0.0.1 >/dev/null 2>&1 && break
  sleep 1
done

say "C# tests"
docker run --rm --network "$NET" \
  -v "$ROOT:/src" -w /src \
  -v /src/${API_DIR}/obj -v /src/${API_DIR}/bin \
  -v /src/tests/WebBase.Tests/obj -v /src/tests/WebBase.Tests/bin \
  -v "${SLUG}-dev_nuget:/root/.nuget/packages" \
  -e "TEST_POSTGRES=Host=${DB};Username=postgres;Password=test" \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test --nologo

say "Website: type check and build"
# The whole repository, read-only: the build reads docs/ and README.md too.
# node_modules and dist are the container's own, writable; their mount points
# must exist beforehand, since nothing can create them in a read-only folder.
mkdir -p "$ROOT/client/node_modules" "$ROOT/client/dist"
docker run --rm \
  -v "$ROOT:/src:ro" -w /src/client -v /src/client/node_modules -v /src/client/dist \
  node:22-alpine sh -c 'npm ci --no-audit --no-fund --loglevel=error && npm run build'

ok "All checks passed"
