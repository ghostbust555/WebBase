#!/usr/bin/env bash
# The development site WITHOUT Docker: the same thing ./dev.sh runs, using the
# tools on this computer (./setupenvironment.sh makes them ready).
#
#   ./scripts/dev-host.sh up          start the API and the website in the background
#   ./scripts/dev-host.sh status      what's running
#   ./scripts/dev-host.sh logs [api|web]   watch what they print (Ctrl+C to stop watching)
#   ./scripts/dev-host.sh restart     stop, then start (after editing .env.local)
#   ./scripts/dev-host.sh stop        stop both (your data is kept)
#   ./scripts/dev-host.sh up --no-watch    run the API with plain `dotnet run`, no rebuild on save
#
# Like ./dev.sh: the API on :5000 (DEV_API_PORT, this computer only) with hot
# reload, Vite on :5173 (DEV_WEB_PORT) forwarding /api to it, a PostgreSQL on
# localhost, and client/src/api-types.ts rewritten whenever the C# types change.
# Logs and process ids are in .dev-host/. docs/ENVIRONMENT.md.
. "$(dirname "$0")/lib.sh"
load_host_env

STATE="$ROOT/.dev-host"
WEB_PORT="$(env_value DEV_WEB_PORT)"; WEB_PORT="${WEB_PORT:-5173}"
API_PORT="$(env_value DEV_API_PORT)"; API_PORT="${API_PORT:-5000}"
# The development connection setupenvironment.sh made (as compose.dev.yaml has it, with Host=localhost).
DB="${DEV_DATABASE:-Host=localhost;Port=5432;Database=${SLUG};Username=${SLUG};Password=${SLUG}}"

running() { # name -> true if its process group is alive
  local pidfile="$STATE/$1.pid"
  [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null
}

stop_one() {
  local name="$1" pid
  running "$name" || { rm -f "$STATE/$name.pid"; return 0; }
  pid="$(cat "$STATE/$name.pid")"
  # Each was started in its own session, so the process id is also the group id:
  # stopping the group stops dotnet watch's child and Vite's workers too.
  kill -- "-$pid" 2>/dev/null || kill "$pid" 2>/dev/null || true
  for _ in $(seq 1 20); do kill -0 "$pid" 2>/dev/null || break; sleep 0.5; done
  kill -9 -- "-$pid" 2>/dev/null || true
  rm -f "$STATE/$name.pid"
}

# Start a command detached, in its own session, with its output in a log.
start_one() { # name, directory, env/command...
  local name="$1" dir="$2"; shift 2
  mkdir -p "$STATE"
  : > "$STATE/$name.log"
  if command -v setsid >/dev/null 2>&1; then
    ( cd "$dir" && exec setsid "$@" >> "$STATE/$name.log" 2>&1 < /dev/null ) &
  else
    # No setsid (macOS): a plain background job; stop kills the process, not its group.
    ( cd "$dir" && exec "$@" >> "$STATE/$name.log" 2>&1 < /dev/null ) &
  fi
  echo $! > "$STATE/$name.pid"
}

need_tools() {
  command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -qE '^10\.' \
    || die "The .NET 10 SDK isn't available. Run ./setupenvironment.sh (it installs it without Docker)."
  command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1 \
    || die "Node isn't available. Run ./setupenvironment.sh."
}

db_up() {
  if command -v pg_isready >/dev/null 2>&1; then
    local host port
    host="$(printf '%s' "$DB" | tr ';' '\n' | sed -n 's/^[Hh]ost=//p' | head -n1)"
    port="$(printf '%s' "$DB" | tr ';' '\n' | sed -n 's/^[Pp]ort=//p' | head -n1)"
    pg_isready -q -h "${host:-localhost}" -p "${port:-5432}"
  else
    # No client programs: just see whether anything listens.
    local port; port="$(printf '%s' "$DB" | tr ';' '\n' | sed -n 's/^[Pp]ort=//p' | head -n1)"
    (exec 3<>"/dev/tcp/localhost/${port:-5432}") 2>/dev/null
  fi
}

up() {
  local watch=1
  [ "${1:-}" = "--no-watch" ] && watch=0
  need_tools
  running api && running web && { ok "Already running (./scripts/dev-host.sh status)"; return 0; }
  db_up || die "PostgreSQL isn't answering at the address in DEV_DATABASE. Run ./setupenvironment.sh: it starts PostgreSQL and makes the development role and database."
  if ! npm_deps_current; then
    say "Installing the website's packages (npm ci)"
    npm_install_deps || die "npm ci failed. See docs/ENVIRONMENT.md."
  fi

  # The same settings compose.dev.yaml gives the API container. Each NAME=value is
  # one array item, so a ';' in a connection string is never read by the shell.
  local env=(
    ASPNETCORE_ENVIRONMENT=Development
    "ASPNETCORE_URLS=http://127.0.0.1:${API_PORT}"
    App__Environment=development
    "App__Name=$(env_value APP_NAME)"
    App__Build=dev
    "ConnectionStrings__Default=${DB}"
    "Jwt__Key=$(env_value JWT_KEY)"
    "Auth__Google__ClientId=$(env_value GOOGLE_CLIENT_ID)"
    "Auth__AuthorEmails=$(env_value AUTHOR_EMAILS)"
    "Cms__Enabled=$(env_value CMS_ENABLED)"
    "Cms__ContentPath=${ROOT}/content"
    "Cms__Source=$(env_value CMS_SOURCE)"
    Cms__RefreshSeconds=0
    "Dev__TypeScriptOutput=${ROOT}/client/src/api-types.ts"
    DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1
    DOTNET_CLI_TELEMETRY_OPTOUT=1
    DOTNET_NOLOGO=1
  )
  [ -n "$(env_value APP_NAME)" ] || env+=("App__Name=WebBase")
  [ -n "$(env_value CMS_ENABLED)" ] || env+=("Cms__Enabled=false")
  [ -n "$(env_value CMS_SOURCE)" ] || env+=("Cms__Source=files")
  local pair
  for pair in Cms__S3__Bucket:CMS_S3_BUCKET Cms__S3__Prefix:CMS_S3_PREFIX Cms__S3__Region:CMS_S3_REGION \
              Cms__S3__ServiceUrl:CMS_S3_SERVICE_URL Cms__S3__AccessKey:CMS_S3_ACCESS_KEY Cms__S3__SecretKey:CMS_S3_SECRET_KEY \
              Cms__Azure__ConnectionString:CMS_AZURE_CONNECTION_STRING Cms__Azure__Container:CMS_AZURE_CONTAINER \
              Cms__Azure__ContainerUrl:CMS_AZURE_CONTAINER_URL Cms__Azure__Prefix:CMS_AZURE_PREFIX \
              Cms__Gcs__Bucket:CMS_GCS_BUCKET Cms__Gcs__Prefix:CMS_GCS_PREFIX; do
    [ -n "$(env_value "${pair#*:}")" ] && env+=("${pair%%:*}=$(env_value "${pair#*:}")")
  done

  local cmd=(dotnet watch run --no-launch-profile --non-interactive)
  [ "$watch" = 0 ] && cmd=(dotnet run --no-launch-profile)

  say "Starting the API (:${API_PORT}) - the first build takes a minute"
  running api || start_one api "$ROOT/$API_DIR" env "${env[@]}" "${cmd[@]}"
  say "Starting the website (:${WEB_PORT})"
  running web || start_one web "$ROOT/client" env "API_URL=http://127.0.0.1:${API_PORT}" "WEB_PORT=${WEB_PORT}" npm run dev

  say "Waiting for the site to answer"
  if ! wait_for_url "http://127.0.0.1:${WEB_PORT}/api/health" 600; then
    warn "It hasn't come up yet. See what's happening with: ./scripts/dev-host.sh logs"
    exit 1
  fi
  ok "Development is running (without Docker)"
  cat <<EOT

    Website     http://localhost:${WEB_PORT}
    API docs    http://localhost:${WEB_PORT}/scalar
    API         http://127.0.0.1:${API_PORT}

    Edit files in client/src or src/ and the site updates by itself.
    ./scripts/dev-host.sh logs    to watch it      ./scripts/dev-host.sh stop    to stop it
EOT
}

case "${1:-up}" in
  up|start)
    shift || true; up "$@" ;;
  stop|down)
    stop_one web; stop_one api; ok "Stopped. Your data is kept; ./scripts/dev-host.sh up starts it again." ;;
  restart)
    shift || true; stop_one web; stop_one api; up "$@" ;;
  status|ps)
    for n in api web; do
      if running "$n"; then ok "$n running (pid $(cat "$STATE/$n.pid"))"; else warn "$n not running"; fi
    done ;;
  logs)
    shift || true
    case "${1:-}" in
      api|web) tail -n 100 -f "$STATE/$1.log" ;;
      "") tail -n 50 -f "$STATE/api.log" "$STATE/web.log" ;;
      *) die "Usage: ./scripts/dev-host.sh logs [api|web]" ;;
    esac ;;
  *)
    sed -n '2,13p' "$0"; exit 1 ;;
esac
