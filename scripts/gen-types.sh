#!/usr/bin/env bash
# Rewrite client/src/api-types.ts from the C# API's types: the TypeScript
# shapes of everything the controllers take and return. Run it after changing
# a record in Contracts/ (or anything else a controller returns) - unless
# ./dev.sh is running, which does it by itself. Then commit the file.
#
#   ./scripts/gen-types.sh
#
# How it's made: src/WebBase.Api/Infrastructure/TypeScriptContracts.cs.
. "$(dirname "$0")/lib.sh"

say "Generating client/src/api-types.ts"
if [ "$(run_mode)" = host ]; then
  load_host_env
  command -v dotnet >/dev/null 2>&1 || die "dotnet isn't available. Run ./setupenvironment.sh."
  WRITE_TYPESCRIPT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
    dotnet test tests/WebBase.Tests --nologo -v q --filter "FullyQualifiedName~TypeScriptContractsTests.The_committed" >/dev/null \
    || die "Generation failed. Run ./scripts/test.sh to see why."
  ok "client/src/api-types.ts is up to date"
  exit 0
fi
need_docker
docker run --rm \
  -v "$ROOT:/src" -w /src \
  -v /src/.build \
  -v "${SLUG}-dev_nuget:/root/.nuget/packages" \
  -e WRITE_TYPESCRIPT=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c 'dotnet test tests/WebBase.Tests --nologo -v q --filter "FullyQualifiedName~TypeScriptContractsTests.The_committed" >/dev/null; status=$?
           # Made in the container, owned by you.
           chown '"$(id -u):$(id -g)"' client/src/api-types.ts 2>/dev/null
           exit $status' \
  || die "Generation failed. Run ./scripts/test.sh to see why."
ok "client/src/api-types.ts is up to date"
