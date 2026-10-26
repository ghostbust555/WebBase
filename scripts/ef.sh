#!/usr/bin/env bash
# Entity Framework (database migrations), run inside Docker so nothing but
# Docker needs installing.
#
#   ./scripts/ef.sh migrations add AddRecipes     after changing a model
#   ./scripts/ef.sh migrations list               which exist
#   ./scripts/ef.sh migrations remove             undo the last one (if not yet applied)
#
# You never need `database update`: the app applies new migrations itself
# when it starts (./dev.sh restart, or the next ./deploy.sh).
. "$(dirname "$0")/lib.sh"
need_docker

[ $# -gt 0 ] || { sed -n '2,10p' "$0"; exit 1; }

# Join the development network, if it's up, so commands that read the
# database can reach it.
network=()
docker network inspect "${SLUG}-dev_default" >/dev/null 2>&1 && network=(--network "${SLUG}-dev_default")

tty=(); [ -t 0 ] && [ -t 1 ] && tty=(-t)

docker run --rm -i "${tty[@]}" "${network[@]}" \
  -v "$ROOT:/src" -w /src \
  -v /src/${API_DIR}/obj -v /src/${API_DIR}/bin \
  -v "${SLUG}-dev_nuget:/root/.nuget/packages" \
  -e App__Environment=development \
  -e "ConnectionStrings__Default=Host=db;Port=5432;Database=${SLUG};Username=${SLUG};Password=${SLUG}" \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c 'dotnet tool restore >/dev/null && dotnet restore -v q '"${API_DIR}"' >/dev/null && dotnet ef "$@" --project '"${API_DIR}"'; status=$?
           # Files made in the container belong to you, not to root.
           [ -d '"${API_DIR}"'/Data/Postgres/Migrations ] && chown -R '"$(id -u):$(id -g)"' '"${API_DIR}"'/Data/Postgres/Migrations
           exit $status' ef "$@"
