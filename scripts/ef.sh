#!/usr/bin/env bash
# Entity Framework (database migrations), run inside Docker so nothing but
# Docker needs installing. With no Docker daemon it uses the host's dotnet
# (./setupenvironment.sh), against the development database from .env.host.
#
#   ./scripts/ef.sh migrations add AddRecipes     after changing a model
#   ./scripts/ef.sh migrations list               which exist
#   ./scripts/ef.sh migrations remove             undo the last one (if not yet applied)
#
# You never need `database update`: the app applies new migrations itself
# when it starts (./dev.sh restart, or the next ./deploy.sh).
. "$(dirname "$0")/lib.sh"

[ $# -gt 0 ] || { sed -n '2,12p' "$0"; exit 1; }

if [ "$(run_mode)" = host ]; then
  load_host_env
  command -v dotnet >/dev/null 2>&1 || die "dotnet isn't available. Run ./setupenvironment.sh."
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 App__Environment=development
  export ConnectionStrings__Default="${DEV_DATABASE:-Host=localhost;Port=5432;Database=${SLUG};Username=${SLUG};Password=${SLUG}}"
  dotnet tool restore >/dev/null && dotnet restore -v q "$POSTGRES_DIR" >/dev/null && dotnet ef "$@" --project "$POSTGRES_DIR"
  exit $?
fi

need_docker

# Join the development network, if it's up, so commands that read the
# database can reach it.
network=()
docker network inspect "${SLUG}-dev_default" >/dev/null 2>&1 && network=(--network "${SLUG}-dev_default")

tty=(); [ -t 0 ] && [ -t 1 ] && tty=(-t)

docker run --rm -i "${tty[@]}" "${network[@]}" \
  -v "$ROOT:/src" -w /src \
  -v /src/.build \
  -v "${SLUG}-dev_nuget:/root/.nuget/packages" \
  -e App__Environment=development \
  -e "ConnectionStrings__Default=Host=db;Port=5432;Database=${SLUG};Username=${SLUG};Password=${SLUG}" \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c 'dotnet tool restore >/dev/null && dotnet restore -v q '"${POSTGRES_DIR}"' >/dev/null && dotnet ef "$@" --project '"${POSTGRES_DIR}"'; status=$?
           # Files made in the container belong to you, not to root.
           [ -d '"${POSTGRES_DIR}"'/Migrations ] && chown -R '"$(id -u):$(id -g)"' '"${POSTGRES_DIR}"'/Migrations
           exit $status' ef "$@"
