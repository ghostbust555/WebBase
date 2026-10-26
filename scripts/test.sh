#!/usr/bin/env bash
# Run every check: the C# tests and the website's type check and build.
# Inside Docker, so nothing but Docker needs installing.
#
#   ./scripts/test.sh
. "$(dirname "$0")/lib.sh"
need_docker

say "C# tests"
docker run --rm \
  -v "$ROOT:/src" -w /src \
  -v /src/${API_DIR}/obj -v /src/${API_DIR}/bin \
  -v /src/tests/WebBase.Tests/obj -v /src/tests/WebBase.Tests/bin \
  -v "${SLUG}-dev_nuget:/root/.nuget/packages" \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test --nologo

say "Website: type check and build"
# The whole repository, read-only: the build reads docs/ and README.md too.
docker run --rm \
  -v "$ROOT:/src:ro" -w /src/client -v /src/client/node_modules -v /src/client/dist \
  node:22-alpine sh -c 'npm ci --no-audit --no-fund --loglevel=error && npm run build'

ok "All checks passed"
