#!/usr/bin/env bash
# Make THIS MACHINE able to build, test and run the project. Safe to run again:
# it finds what is already there and changes nothing the second time.
#
#   ./setupenvironment.sh                  detect, then install what's missing (asks first)
#   ./setupenvironment.sh --yes            ...without asking (CI, cloud coding agents)
#   ./setupenvironment.sh --check          only report; change nothing; exit 1 if something is missing
#   ./setupenvironment.sh --docker         require Docker (fail if no daemon answers)
#   ./setupenvironment.sh --no-docker      use the host's own tools even if Docker is there
#   ./setupenvironment.sh --with-postgres  host mode: install PostgreSQL if it is missing (no question)
#   ./setupenvironment.sh --without-postgres   host mode: leave PostgreSQL alone
#
# setup.sh is different: it names the project and makes its secrets.
#
# DOCKER MODE (a Docker daemon answers `docker info`): checks Docker and compose;
# everything else runs in containers. Nothing to install.
#
# HOST MODE (no daemon: cloud coding-agent containers, CI runners, a stopped
# Docker Desktop): makes the host able to run the same things, without root where
# it can:
#   - the .NET 10 SDK: the official install script (into ~/.dotnet), else the
#     distro package dotnet-sdk-10.0; then `dotnet tool restore` (dotnet-ef) and
#     the NuGet restore
#   - Node 20.19+/22.12+ (a checked download from nodejs.org into
#     ~/.local/share/<project>-env if missing) and `npm ci` in client/
#   - PostgreSQL 14+: uses one that is installed, else installs it (apt / brew);
#     starts it; creates the development role and database that
#     ConnectionStrings__Default names (never touching an existing role or database);
#     and a superuser role for the tests, written as TEST_POSTGRES
# What it learns goes in .env.host (shell syntax, never committed), read by
# scripts/test.sh, scripts/dev-host.sh and the other scripts. It never touches
# .env.local, your shell profile, or an existing database.
#
# Downloads are checked against the system certificate store; verification is never
# switched off. If a host is blocked (by a proxy, say) the message names it.
# docs/ENVIRONMENT.md has the details.

# --- Arguments first: --check must change nothing, even sourcing the helpers ---
MODE=auto; CHECK=0; PG=auto; YES=0
for arg in "$@"; do
  case "$arg" in
    --docker) MODE=docker ;;
    --no-docker) MODE=host ;;
    --check) CHECK=1 ;;
    --with-postgres) PG=with ;;
    --without-postgres) PG=without ;;
    --yes|-y) YES=1 ;;
    -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg (see ./setupenvironment.sh --help)" >&2; exit 2 ;;
  esac
done
[ "$CHECK" = 1 ] && export WEBBASE_READONLY=1

. "$(dirname "$0")/scripts/lib.sh"
# Most failures below are expected and handled (a blocked host, no root): report them
# in the summary instead of stopping at the first.
set +e

# What happened, for the summary at the end.
FOUND=(); DONE=(); SKIPPED=(); NEEDS=(); WOULD=()
found()   { FOUND+=("$1");   ok "$1"; }
did()     { DONE+=("$1");    ok "$1"; }
skipped() { SKIPPED+=("$1"); printf '   - %s\n' "$1"; }
needs()   { NEEDS+=("$1");   warn "$1"; }       # something required is still missing
would()   { WOULD+=("$1");   printf '   ? would: %s\n' "$1"; }

# (Empty arrays and `set -u` don't mix in bash 3.2, macOS: they are expanded as "${a[@]+"${a[@]}"}".)

# ---- Helpers ------------------------------------------------------------------------

have() { command -v "$1" >/dev/null 2>&1; }
INTERACTIVE=0; [ -t 0 ] && [ -t 1 ] && INTERACTIVE=1

# May we change the machine for this item? --yes, or an answer at the keyboard.
# Never in --check, and never silently when nobody is there to ask.
consent() {
  [ "$CHECK" = 1 ] && return 1
  [ "$YES" = 1 ] && return 0
  [ "$INTERACTIVE" = 1 ] && confirm "    $1"
}

# Run a command as root: we are root, or sudo needs no password, or (at a keyboard,
# after consent) sudo may ask for one.
SUDO_ASK=0
as_root() {
  if [ "$(id -u)" = 0 ]; then "$@"
  elif have sudo && sudo -n true 2>/dev/null; then sudo -n "$@"
  elif have sudo && [ "$INTERACTIVE" = 1 ] && [ "$SUDO_ASK" = 1 ]; then sudo "$@"
  else return 1
  fi
}
can_root() {
  [ "$(id -u)" = 0 ] && return 0
  have sudo || return 1
  sudo -n true 2>/dev/null && return 0
  [ "$INTERACTIVE" = 1 ]
}

# Download over verified TLS only. The system certificate store decides what is
# trusted (curl's default); there is no switch here to turn that off.
fetch() { curl -fsSL --proto '=https' --tlsv1.2 --retry 3 --retry-delay 2 --connect-timeout 15 -o "$2" "$1"; }

# Can we reach HOST over verified HTTPS? If not, PROBE_WHY says why in words.
PROBE_WHY=""
probe_host() {
  local host="$1" out code=0
  out="$(curl -sS -o /dev/null -I --max-time 20 --connect-timeout 10 "https://${host}/" 2>&1)" || code=$?
  [ "$code" = 0 ] && return 0
  case "$code" in
    6)  PROBE_WHY="${host}: the name can't be looked up (DNS)." ;;
    7)  PROBE_WHY="${host}: the connection is refused or blocked (a firewall or proxy?)." ;;
    28) PROBE_WHY="${host}: timed out (a firewall or proxy dropping the connection?)." ;;
    35|51|58|59|60|77|82|83|90|91)
        PROBE_WHY="${host}: its certificate isn't trusted by the system certificate store (${out}). Behind a proxy that re-signs HTTPS, add the proxy's CA certificate to the system store (Debian/Ubuntu: copy it to /usr/local/share/ca-certificates/ and run update-ca-certificates). Verification is never turned off here." ;;
    56) PROBE_WHY="${host}: refused by a proxy (${out}). The host is probably not on its allow-list." ;;
    *)  PROBE_WHY="${host}: curl exit ${code} (${out})." ;;
  esac
  return 1
}
reachable() { # all hosts, or name the first blocked one
  local h
  for h in "$@"; do
    probe_host "$h" || { echo "$PROBE_WHY"; return 1; }
  done
}

# Versions, as numbers.
major() { echo "${1#v}" | cut -d. -f1; }
minor() { echo "${1#v}" | cut -d. -f2; }

# ---- 1. Which mode? ---------------------------------------------------------------------
say "Looking at this machine"
OS="$(uname -s)"; ARCH="$(uname -m)"
echo "    $OS $ARCH, $(id -un) (uid $(id -u))$( [ "$CHECK" = 1 ] && echo ', check only: nothing will be changed')"

DOCKER_CLIENT=0; DOCKER_UP=0
have docker && DOCKER_CLIENT=1
docker_ready && DOCKER_UP=1

case "$MODE" in
  docker) [ "$DOCKER_UP" = 1 ] || die "--docker was given, but no Docker daemon answers (\`docker info\` fails)$( [ "$DOCKER_CLIENT" = 1 ] && echo '. The docker command is installed; start the daemon: Docker Desktop, or sudo systemctl start docker' || echo ', and docker is not installed: https://docs.docker.com/get-docker/')." ;;
  auto)   if [ "$DOCKER_UP" = 1 ]; then MODE=docker; else MODE=host; fi ;;
esac

if [ "$MODE" = docker ]; then
  ok "Mode: Docker (a daemon answers \`docker info\`)"
elif [ "$DOCKER_CLIENT" = 1 ] && [ "$DOCKER_UP" = 0 ]; then
  ok "Mode: host tools. The docker command is installed, but no daemon answers \`docker info\`"
else
  ok "Mode: host tools ($( [ "$DOCKER_CLIENT" = 1 ] && echo 'Docker was not chosen' || echo 'Docker is not installed'))"
fi

# ---- 2a. Docker mode ----------------------------------------------------------------------
if [ "$MODE" = docker ]; then
  say "Docker"
  found "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '(version unknown)') is running"
  if docker compose version >/dev/null 2>&1; then
    found "docker compose $(docker compose version --short 2>/dev/null)"
  else
    needs "'docker compose' is missing. Update Docker Desktop, or install the compose plugin: https://docs.docker.com/compose/install/"
  fi
  if have git; then found "git $(git --version | cut -d' ' -f3)"; else skipped "git is not installed (only needed to download and commit the project)"; fi
  skipped "PostgreSQL, .NET and Node are not needed on this computer: they run in containers"
  if [ -f .env.local ]; then found ".env.local exists (left alone)"; else skipped ".env.local doesn't exist yet: ./setup.sh makes it, with the project's secrets"; fi

# ---- 2b. Host mode ------------------------------------------------------------------------
else
  load_host_env

  # What goes in .env.host. Each KEY keeps its value unless told otherwise; a changed
  # or new one is recorded (and counts as a change in the summary).
  HOST_PATH=()       # directories .env.host puts on PATH
  declare -a ENV_KEYS ENV_VALS ENV_KEEP
  ENV_KEYS=(); ENV_VALS=(); ENV_KEEP=()
  want_env() { ENV_KEYS+=("$1"); ENV_VALS+=("$2"); ENV_KEEP+=("${3:-replace}"); }

  # ---- apt, for the steps that fall back to the distro's packages
  APT_UPDATED=0
  apt_install() { # packages... ; refreshes the package lists once if one has no candidate
    local pkg
    has_candidate() { local out; out="$(apt-cache policy "$1" 2>/dev/null)"; [[ "$out" == *"Candidate: "[!\(]* ]]; } # no pipe: grep -q + pipefail = SIGPIPE
    for pkg in "$@"; do
      if ! has_candidate "$pkg"; then
        if [ "$APT_UPDATED" = 0 ]; then
          as_root apt-get update -qq >/dev/null 2>&1 || warn "apt-get update failed (a blocked package archive? check the hosts in /etc/apt/sources.list*)"
          APT_UPDATED=1
        fi
        has_candidate "$pkg" || { warn "No package '$pkg' in this machine's package lists."; return 1; }
      fi
    done
    as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "$@" >/dev/null 2>&1 \
      || { warn "apt-get install $* failed."; return 1; }
  }


  # ---- Basics
  say "Basic tools"
  if have curl; then
    found "curl"
  elif [ "$CHECK" = 1 ]; then
    would "install curl and ca-certificates (apt-get)"; NEEDS+=("curl is missing")
  elif have apt-get && can_root && consent "Install curl and ca-certificates (it downloads everything else)?"; then
    SUDO_ASK=1
    apt_install curl ca-certificates && did "curl installed" || needs "curl couldn't be installed. Install it: sudo apt-get install -y curl ca-certificates"
  else
    needs "curl is not installed (it downloads everything else). Install it: sudo apt-get install -y curl ca-certificates   (macOS: it is built in)"
  fi
  if have tar; then found "tar"; else needs "tar is not installed"; fi
  if have git; then found "git $(git --version | cut -d' ' -f3)"; else skipped "git is not installed (only needed to download and commit the project)"; fi
  case "$OS" in Linux|Darwin) ;; *) needs "$OS isn't supported for host mode. Use Docker, or WSL on Windows." ;; esac

  # Node (and npm) trust their own bundled certificates; the system store is what a
  # proxy's CA lives in, so add it. This only ADDS trust to what the system already has.
  if [ -z "${NODE_EXTRA_CA_CERTS:-}" ]; then
    for bundle in /etc/ssl/certs/ca-certificates.crt /etc/pki/tls/certs/ca-bundle.crt /etc/ssl/cert.pem; do
      if [ -f "$bundle" ]; then export NODE_EXTRA_CA_CERTS="$bundle"; want_env NODE_EXTRA_CA_CERTS "$bundle"; break; fi
    done
  fi

  # ---- .NET SDK
  say ".NET 10 SDK"
  DOTNET_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
  dotnet_has_10() { have dotnet && dotnet --list-sdks 2>/dev/null | grep -qE '^10\.'; }
  if ! dotnet_has_10 && [ -x "$DOTNET_DIR/dotnet" ] && "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -qE '^10\.'; then
    # Installed here earlier, but not on the PATH of this shell.
    export PATH="$DOTNET_DIR:$PATH"
  fi

  install_dotnet() {
    local tmp why
    # 1. The official install script, into the user's own folder: no root needed.
    if why="$(reachable dot.net builds.dotnet.microsoft.com)"; then
      tmp="$(mktemp -d)"
      if fetch https://dot.net/v1/dotnet-install.sh "$tmp/dotnet-install.sh" \
         && bash "$tmp/dotnet-install.sh" --channel 10.0 --install-dir "$DOTNET_DIR" --no-path >"$tmp/log" 2>&1; then
        rm -rf "$tmp"
        export PATH="$DOTNET_DIR:$PATH"
        return 0
      fi
      warn "The official install script failed: $(tail -n 3 "$tmp/log" 2>/dev/null | tr '\n' ' ')"
      rm -rf "$tmp"
    else
      warn "The official install script can't be used: $why"
    fi
    # 2. The distro's package (Ubuntu 24.04 has dotnet-sdk-10.0 in its own archive).
    if have apt-get; then
      if can_root; then
        SUDO_ASK=1
        say "Trying the distro package dotnet-sdk-10.0 instead"
        apt_install dotnet-sdk-10.0 && return 0
        warn "The dotnet-sdk-10.0 package couldn't be installed."
      else
        warn "The distro package needs root, and there is no root or passwordless sudo here."
      fi
    fi
    return 1
  }

  if dotnet_has_10; then
    found ".NET SDK $(dotnet --version 2>/dev/null | head -n1) ($(command -v dotnet))"
  elif [ "$CHECK" = 1 ]; then
    would "install the .NET 10 SDK (the official script into $DOTNET_DIR, else apt-get install dotnet-sdk-10.0)"
    NEEDS+=(".NET 10 SDK is missing")
  elif ! have curl; then
    needs ".NET 10 SDK is missing (and curl is needed to install it)"
  elif consent "Install the .NET 10 SDK (about 250 MB, into $DOTNET_DIR; else the distro package)?"; then
    if install_dotnet && dotnet_has_10; then
      did ".NET SDK $(dotnet --version 2>/dev/null | head -n1) installed ($(command -v dotnet))"
    else
      needs ".NET 10 SDK is missing. Install it yourself, then run this again: either
        curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0     (needs dot.net and builds.dotnet.microsoft.com)
      or  sudo apt-get install -y dotnet-sdk-10.0                                      (Ubuntu 24.04 and newer)"
    fi
  else
    needs ".NET 10 SDK is missing. Run again with --yes to install it, or: curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0"
  fi

  if dotnet_has_10; then
    # A dotnet we installed into our own folder isn't on anybody's PATH.
    dotnet_path="$(command -v dotnet)"
    if [ "$(dirname "$dotnet_path")" = "$DOTNET_DIR" ]; then
      HOST_PATH+=("$DOTNET_DIR" "$DOTNET_DIR/tools")
      want_env DOTNET_ROOT "$DOTNET_DIR"
    fi
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
    # A bare container may have no ICU library: .NET then needs invariant globalization.
    if ! dotnet --version >/dev/null 2>&1 && [ "$CHECK" = 0 ] && have apt-get && can_root; then
      apt_install libicu-dev >/dev/null 2>&1 && did "ICU library installed (.NET needs it for accents and sorting)"
    fi
    if ! dotnet --version >/dev/null 2>&1; then
      export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
      if dotnet --version >/dev/null 2>&1; then
        want_env DOTNET_SYSTEM_GLOBALIZATION_INVARIANT 1
        warn "No ICU library: using invariant globalization for development (sudo apt-get install -y libicu-dev avoids it)."
      else
        unset DOTNET_SYSTEM_GLOBALIZATION_INVARIANT
        warn "dotnet is installed but won't start. Run 'dotnet --version' to see why."
      fi
    fi

    # ---- Local tools (dotnet-ef) and packages
    say "dotnet tools and packages"
    want_ef="$(grep -A2 '"dotnet-ef"' dotnet-tools.json | grep '"version"' | sed -E 's/.*"version": *"([^"]+)".*/\1/' || true)"
    have_ef="$(dotnet tool list 2>/dev/null | awk '$1=="dotnet-ef" {print $2}' || true)"
    if [ -n "$want_ef" ] && [ "$have_ef" = "$want_ef" ]; then
      found "dotnet-ef $have_ef (dotnet tool restore: nothing to do)"
    elif [ "$CHECK" = 1 ]; then
      would "dotnet tool restore (dotnet-ef ${want_ef:-?})"; NEEDS+=("dotnet-ef isn't restored")
    elif why="$(reachable api.nuget.org)" && dotnet tool restore >/dev/null 2>&1; then
      did "dotnet tool restore: dotnet-ef $want_ef"
    else
      needs "dotnet tool restore failed. ${why:-Run 'dotnet tool restore' to see why.} (It downloads from api.nuget.org.)"
    fi

    restored=1
    while IFS= read -r project; do
      name="$(basename "$project" .csproj)"
      [ -f "$ROOT/.build/obj/$name/project.assets.json" ] || restored=0
    done < <(find src modules tests -name '*.csproj' -not -path '*/node_modules/*' 2>/dev/null)
    if [ "$restored" = 1 ]; then
      found "NuGet packages restored for every project"
    elif [ "$CHECK" = 1 ]; then
      would "dotnet restore WebBase.slnx"; NEEDS+=("NuGet packages aren't restored")
    elif why="$(reachable api.nuget.org)" && dotnet restore WebBase.slnx -v q >/dev/null 2>&1; then
      did "dotnet restore: NuGet packages for every project"
    else
      needs "dotnet restore failed. ${why:-Run 'dotnet restore WebBase.slnx' to see why.}"
    fi
  fi

  # ---- Node and npm
  say "Node and npm"
  node_ok() {
    have node && have npm || return 1
    local v ma mi; v="$(node --version)"; ma="$(major "$v")"; mi="$(minor "$v")"
    # Vite 7 needs 20.19+ or 22.12+.
    [ "$ma" -ge 23 ] || { [ "$ma" = 22 ] && [ "$mi" -ge 12 ]; } || { [ "$ma" = 20 ] && [ "$mi" -ge 19 ]; }
  }
  NODE_HOME_PARENT="$HOME/.local/share/${SLUG}-env"
  if ! node_ok; then
    latest_local="$(ls -d "$NODE_HOME_PARENT"/node-v22.*/bin 2>/dev/null | sort -V | tail -n1 || true)"
    if [ -n "$latest_local" ]; then export PATH="$latest_local:$PATH"; fi
  fi

  install_node() {
    local os arch base file sha tmp dest
    case "$OS" in Linux) os=linux ;; Darwin) os=darwin ;; esac
    case "$ARCH" in x86_64|amd64) arch=x64 ;; aarch64|arm64) arch=arm64 ;; *) warn "No Node download for $ARCH."; return 1 ;; esac
    local why; why="$(reachable nodejs.org)" || { warn "Can't download Node: $why"; return 1; }
    base=https://nodejs.org/dist/latest-v22.x
    tmp="$(mktemp -d)"
    fetch "$base/SHASUMS256.txt" "$tmp/SHASUMS256.txt" || { warn "Couldn't read $base/SHASUMS256.txt"; rm -rf "$tmp"; return 1; }
    file="$(grep -oE "node-v22\\.[0-9.]+-${os}-${arch}\\.tar\\.gz" "$tmp/SHASUMS256.txt" | head -n1)"
    sha="$(grep " ${file}\$" "$tmp/SHASUMS256.txt" | cut -d' ' -f1)"
    [ -n "$file" ] && [ -n "$sha" ] || { warn "No Node 22 build for $os-$arch in $base"; rm -rf "$tmp"; return 1; }
    fetch "$base/$file" "$tmp/$file" || { warn "Couldn't download $base/$file"; rm -rf "$tmp"; return 1; }
    [ "$(lock_hash "$tmp/$file")" = "$sha" ] || { warn "The download's checksum doesn't match nodejs.org's SHASUMS256.txt; not using it."; rm -rf "$tmp"; return 1; }
    dest="$NODE_HOME_PARENT/${file%.tar.gz}"
    mkdir -p "$NODE_HOME_PARENT" && tar -xzf "$tmp/$file" -C "$NODE_HOME_PARENT" && rm -rf "$tmp" || return 1
    export PATH="$dest/bin:$PATH"
  }

  if node_ok; then
    found "Node $(node --version), npm $(npm --version) ($(command -v node))"
  elif [ "$CHECK" = 1 ]; then
    would "install Node 22 (a checked download from nodejs.org into $NODE_HOME_PARENT)"; NEEDS+=("Node 20.19+ or 22.12+ is missing")
  elif ! have curl || ! have tar; then
    needs "Node 20.19+ or 22.12+ is missing (and curl and tar are needed to install it)"
  elif consent "Install Node 22 (about 30 MB, into $NODE_HOME_PARENT)?"; then
    if install_node && node_ok; then did "Node $(node --version) installed ($(command -v node))"
    else needs "Node 20.19+ or 22.12+ is missing. Install it (https://nodejs.org, or your package manager's Node 22), then run this again."; fi
  else
    needs "Node 20.19+ or 22.12+ is missing. Run again with --yes to install it, or install Node 22 yourself."
  fi

  if node_ok; then
    node_dir="$(dirname "$(command -v node)")"
    case "$node_dir" in "$NODE_HOME_PARENT"/*) HOST_PATH+=("$node_dir") ;; esac
    if npm_deps_current; then
      found "client/node_modules matches package-lock.json (npm ci: nothing to do)"
    elif [ "$CHECK" = 1 ]; then
      would "npm ci in client/"; NEEDS+=("client/node_modules is missing or out of date")
    elif why="$(reachable registry.npmjs.org)" && npm_install_deps; then
      did "npm ci in client/"
    else
      needs "npm ci failed. ${why:-Run 'cd client && npm ci' to see why. Behind a proxy that re-signs HTTPS, NODE_EXTRA_CA_CERTS=/path/to/the/proxy-ca.pem makes npm trust it.} (It downloads from registry.npmjs.org.)"
    fi
  fi

  # ---- PostgreSQL
  say "PostgreSQL"
  PGPORT_USE="${PGPORT:-5432}"
  DEV_DB="$SLUG"; DEV_ROLE="$SLUG"; DEV_PASSWORD="$SLUG"      # as compose.dev.yaml and appsettings.json have it
  ADMIN_ROLE="${SLUG}_admin"

  pg_dirs() { # bin folders of installed PostgreSQL versions, newest first
    ls -d /usr/lib/postgresql/*/bin /usr/pgsql-*/bin /opt/homebrew/opt/postgresql*/bin /usr/local/opt/postgresql*/bin \
       "/Applications/Postgres.app/Contents/Versions/latest/bin" 2>/dev/null | sort -rV
  }
  pg_find_tools() { # puts a PostgreSQL bin folder on PATH if psql isn't on it
    have psql && have pg_isready && return 0
    local d
    for d in $(pg_dirs); do
      if [ -x "$d/psql" ]; then export PATH="$d:$PATH"; HOST_PATH+=("$d"); return 0; fi
    done
    return 1
  }
  pg_server_installed() { # server programs exist (not just the client)
    have pg_ctlcluster || have postgres || have pg_ctl || [ -n "$(ls /usr/lib/postgresql/*/bin/postgres /usr/pgsql-*/bin/postgres /opt/homebrew/opt/postgresql*/bin/postgres /usr/local/opt/postgresql*/bin/postgres 2>/dev/null | head -n1)" ]
  }
  pg_up() { pg_isready -q -h localhost -p "$PGPORT_USE" 2>/dev/null; }

  # The port the Debian clusters listen on, if we can tell.
  if have pg_lsclusters; then
    p="$(pg_lsclusters --no-header 2>/dev/null | awk 'NR==1 {print $3}' || true)"
    [ -n "$p" ] && [ -z "${PGPORT:-}" ] && PGPORT_USE="$p"
  fi

  pg_start() {
    pg_up && return 0
    SUDO_ASK=1
    if have pg_lsclusters; then
      pg_lsclusters --no-header 2>/dev/null | awk '$4 == "down" {print $1, $2}' | while read -r ver cluster; do
        as_root pg_ctlcluster "$ver" "$cluster" start >/dev/null 2>&1 || true
      done
    fi
    if ! pg_up && have brew; then
      brew services start "$(brew list --formula 2>/dev/null | grep -E '^postgresql(@[0-9]+)?$' | sort -V | tail -n1)" >/dev/null 2>&1 || true
    fi
    if ! pg_up && have systemctl && [ -d /run/systemd/system ]; then as_root systemctl start postgresql >/dev/null 2>&1 || true; fi
    if ! pg_up && have service; then as_root service postgresql start >/dev/null 2>&1 || true; fi
    local i
    for i in $(seq 1 30); do pg_up && return 0; sleep 1; done
    return 1
  }

  pg_install() {
    SUDO_ASK=1
    if have apt-get; then can_root && apt_install postgresql postgresql-client && return 0
    elif have dnf; then as_root dnf install -y postgresql-server postgresql >/dev/null 2>&1 && return 0
    elif have brew; then brew install postgresql@17 >/dev/null 2>&1 && return 0
    fi
    return 1
  }

  # How to run SQL as a PostgreSQL superuser. Tried in order: the settings from an earlier run
  # (.env.host), the PG* environment, our own OS user over the local socket, the postgres OS user.
  ADM=()    # a command prefix
  ADM_ENV=()
  export PGPORT="$PGPORT_USE"
  adm_psql() { "${ADM[@]+"${ADM[@]}"}" env "${ADM_ENV[@]+"${ADM_ENV[@]}"}" psql -X -q -v ON_ERROR_STOP=1 -d postgres "$@"; }
  is_super() { [ "$(adm_psql -tAc "SELECT rolsuper FROM pg_roles WHERE rolname = current_user" 2>/dev/null | tr -d '[:space:]')" = t ]; }
  pg_find_admin() {
    local parsed
    # a) A connection string from the environment or an earlier run, if it works and is a superuser.
    if [ -n "${TEST_POSTGRES:-}" ]; then
      parsed="$(dotnet_conn_to_env "$TEST_POSTGRES")"
      ADM=(); mapfile_env "$parsed"
      if pg_up && is_super; then ADM_METHOD="the existing TEST_POSTGRES connection"; return 0; fi
    fi
    ADM_ENV=(); ADM=()
    # b) PG* variables.
    if [ -n "${PGUSER:-}${PGPASSWORD:-}" ] && is_super; then ADM_METHOD="PGUSER/PGPASSWORD from the environment"; return 0; fi
    # c) The current OS user, over the local socket (Homebrew, Postgres.app, a user's own role).
    if is_super; then ADM_METHOD="the current user ($(id -un)) over the local socket"; return 0; fi
    # d) The postgres OS user.
    if [ "$(id -u)" = 0 ] && have runuser; then
      ADM=(runuser -u postgres --)
      if is_super; then ADM_METHOD="the postgres OS user (runuser)"; return 0; fi
    fi
    if have sudo && { sudo -n true 2>/dev/null || { [ "$SUDO_ASK" = 1 ] && [ "$INTERACTIVE" = 1 ]; }; }; then
      if sudo -n true 2>/dev/null; then ADM=(sudo -n -u postgres); else ADM=(sudo -u postgres); fi
      if is_super; then ADM_METHOD="the postgres OS user (sudo)"; return 0; fi
    fi
    ADM=(); ADM_ENV=()
    return 1
  }
  # "Host=h;Port=p;Username=u;Password=w" -> PGHOST=h PGPORT=p PGUSER=u PGPASSWORD=w
  dotnet_conn_to_env() {
    local kv key value out="" parts
    IFS=';' read -ra parts <<< "$1"
    for kv in "${parts[@]}"; do
      key="${kv%%=*}"; value="${kv#*=}"
      case "$(echo "$key" | tr '[:upper:]' '[:lower:]')" in
        host|server) out="$out PGHOST=$value" ;;
        port) out="$out PGPORT=$value" ;;
        username|user|userid|"user id") out="$out PGUSER=$value" ;;
        password) out="$out PGPASSWORD=$value" ;;
      esac
    done
    echo "$out"
  }
  mapfile_env() { ADM_ENV=(); local item; for item in $1; do ADM_ENV+=("$item"); done; }
  ADM_METHOD=""

  PG_WANTED=1
  [ "$PG" = without ] && PG_WANTED=0
  if [ "$PG_WANTED" = 0 ]; then
    skipped "PostgreSQL (--without-postgres): the Postgres contract tests will be skipped, and ./scripts/dev-host.sh needs a database you provide"
  else
    pg_find_tools || true
    PG_VERSION=""; have psql && PG_VERSION="$(psql --version | grep -oE '[0-9]+(\.[0-9]+)?' | head -n1)"

    if [ -z "$PG_VERSION" ] || ! pg_server_installed; then
      if pg_up; then
        found "A PostgreSQL server answers on localhost:$PGPORT_USE (no local programs to manage it)"
      elif [ "$CHECK" = 1 ]; then
        would "install PostgreSQL (apt-get install postgresql, or brew install postgresql@17), start it, make the role and database"
        NEEDS+=("PostgreSQL isn't installed")
      elif [ "$PG" = with ] || consent "Install PostgreSQL (needs root for apt; brew needs none)?"; then
        if pg_install; then
          pg_find_tools || true
          PG_VERSION="$(psql --version 2>/dev/null | grep -oE '[0-9]+(\.[0-9]+)?' | head -n1)"
          did "PostgreSQL ${PG_VERSION:-?} installed"
        else
          needs "PostgreSQL is not installed, and couldn't be installed here (needs root: you are $(id -un) without passwordless sudo). Install it, then run this again:
        sudo apt-get install -y postgresql postgresql-client       (macOS: brew install postgresql@17)
      or use --without-postgres, and the tests that need it are skipped"
        fi
      else
        needs "PostgreSQL is not installed. Run again with --yes (or --with-postgres) to install it, or: sudo apt-get install -y postgresql postgresql-client"
      fi
    else
      major_v="$(major "$PG_VERSION")"
      if [ "$major_v" -ge 14 ]; then found "PostgreSQL $PG_VERSION installed ($(command -v psql))"
      else needs "PostgreSQL $PG_VERSION is too old (14 or newer is needed)"; fi
    fi

    # Running? Start it.
    if have psql && { pg_server_installed || pg_up; }; then
      if pg_up; then
        found "PostgreSQL is accepting connections on localhost:$PGPORT_USE"
      elif [ "$CHECK" = 1 ]; then
        would "start PostgreSQL"; NEEDS+=("PostgreSQL isn't running")
      elif pg_start; then
        did "PostgreSQL started (localhost:$PGPORT_USE)"
      else
        needs "PostgreSQL is installed but won't start from here. Start it yourself (Debian/Ubuntu: sudo pg_ctlcluster \$(pg_lsclusters -h | awk '{print \$1, \$2}') start; macOS: brew services start postgresql@17), then run this again."
      fi
    fi

    # Roles and databases: only ever added, never changed.
    if pg_up && have psql; then
      if [ "$CHECK" = 1 ]; then
        if pg_find_admin; then
          found "A PostgreSQL superuser connection works (${ADM_METHOD})"
          [ "$(adm_psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='$DEV_ROLE'")" = 1 ] || { would "create role $DEV_ROLE"; NEEDS+=("role $DEV_ROLE is missing"); }
          [ "$(adm_psql -tAc "SELECT 1 FROM pg_database WHERE datname='$DEV_DB'")" = 1 ] || { would "create database $DEV_DB"; NEEDS+=("database $DEV_DB is missing"); }
          [ -n "${TEST_POSTGRES:-}" ] || { would "create the superuser role $ADMIN_ROLE for the tests"; NEEDS+=("TEST_POSTGRES isn't set up"); }
        else
          needs "No PostgreSQL superuser connection is available to this script (tried the environment, this OS user, and the postgres OS user)."
        fi
      elif pg_find_admin; then
        found "A PostgreSQL superuser connection works (${ADM_METHOD})"

        # 1. The development role and database, as ConnectionStrings__Default has them.
        if [ "$(adm_psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='$DEV_ROLE'")" = 1 ]; then
          found "Role $DEV_ROLE exists (left alone)"
        else
          adm_psql -c "CREATE ROLE \"$DEV_ROLE\" LOGIN PASSWORD '$DEV_PASSWORD'" >/dev/null && did "Created role $DEV_ROLE (development only: its password is its name)"
        fi
        if [ "$(adm_psql -tAc "SELECT 1 FROM pg_database WHERE datname='$DEV_DB'")" = 1 ]; then
          found "Database $DEV_DB exists (left alone)"
        else
          # CREATE DATABASE can't run in a multi-statement `psql -c`; createdb sends it on its own.
          "${ADM[@]+"${ADM[@]}"}" env "${ADM_ENV[@]+"${ADM_ENV[@]}"}" createdb -p "$PGPORT_USE" -O "$DEV_ROLE" "$DEV_DB" \
            && did "Created database $DEV_DB (owner $DEV_ROLE)"
        fi
        if PGPASSWORD="$DEV_PASSWORD" psql -X -q -h localhost -p "$PGPORT_USE" -U "$DEV_ROLE" -d "$DEV_DB" -tAc 'SELECT 1' >/dev/null 2>&1; then
          found "The development connection works (localhost:$PGPORT_USE, $DEV_DB, $DEV_ROLE)"
        else
          needs "The role $DEV_ROLE exists but can't log in to $DEV_DB with the development password. It was left alone; fix its password or the pg_hba.conf rule for localhost, or set DEV_DATABASE in .env.host to a connection that works."
        fi
        want_env DEV_DATABASE "Host=localhost;Port=$PGPORT_USE;Database=$DEV_DB;Username=$DEV_ROLE;Password=$DEV_PASSWORD" keep

        # 2. A superuser for the tests (they create and drop a database each).
        if [ -n "${TEST_POSTGRES:-}" ] && ( ADM=(); mapfile_env "$(dotnet_conn_to_env "$TEST_POSTGRES")"; is_super ) 2>/dev/null; then
          found "TEST_POSTGRES already names a working superuser connection (left alone)"
        else
          if [ "$(adm_psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='$ADMIN_ROLE'")" = 1 ]; then
            needs "The role $ADMIN_ROLE exists, but its password isn't known here (it isn't in .env.host). It was left alone. Set TEST_POSTGRES in .env.host to a superuser connection, e.g. 'Host=localhost;Port=$PGPORT_USE;Database=postgres;Username=$ADMIN_ROLE;Password=...'"
          else
            admin_password="$(random_secret 32)"
            adm_psql -c "CREATE ROLE \"$ADMIN_ROLE\" SUPERUSER LOGIN PASSWORD '$admin_password'" >/dev/null \
              && did "Created superuser role $ADMIN_ROLE for the tests (random password, in .env.host)"
            want_env TEST_POSTGRES "Host=localhost;Port=$PGPORT_USE;Database=postgres;Username=$ADMIN_ROLE;Password=$admin_password" keep
          fi
        fi
      else
        needs "PostgreSQL is running, but this script has no superuser access to it (tried the environment, the current OS user, and the postgres OS user). Either give it one:
        PGUSER=postgres PGPASSWORD=... ./setupenvironment.sh --no-docker
      or make the role and database yourself and put the connections in .env.host:
        DEV_DATABASE='Host=localhost;Port=$PGPORT_USE;Database=$DEV_DB;Username=$DEV_ROLE;Password=...'
        TEST_POSTGRES='Host=localhost;Port=$PGPORT_USE;Database=postgres;Username=...;Password=...'     (a superuser)"
      fi
    fi
  fi

  # ---- .env.host: what the other scripts read ------------------------------------------------
  say "Settings for host mode (.env.host)"
  joined=""
  for d in "${HOST_PATH[@]+"${HOST_PATH[@]}"}"; do case ":$joined:" in *":$d:"*) ;; *) joined="${joined:+$joined:}$d" ;; esac; done
  [ -n "$joined" ] && want_env WEBBASE_HOST_PATH "$joined"

  ENV_FILE="$ROOT/.env.host"
  # The file is rebuilt in one fixed shape from what it says now plus what we learned, and replaced
  # only if that differs: a second run with nothing new writes nothing.
  current_keys=(); current_vals=()
  if [ -f "$ENV_FILE" ]; then
    while IFS= read -r key; do
      current_keys+=("$key")
      # shellcheck disable=SC1090
      current_vals+=("$( . "$ENV_FILE" >/dev/null 2>&1; eval "printf '%s' \"\${$key-}\"" )")
    done < <(sed -nE 's/^export ([A-Za-z_][A-Za-z0-9_]*)=.*/\1/p' "$ENV_FILE")
  fi
  final_keys=("${current_keys[@]+"${current_keys[@]}"}"); final_vals=("${current_vals[@]+"${current_vals[@]}"}")
  notes=()
  if [ "${#ENV_KEYS[@]}" -gt 0 ]; then
    for i in "${!ENV_KEYS[@]}"; do
      key="${ENV_KEYS[$i]}"; value="${ENV_VALS[$i]}"; keep="${ENV_KEEP[$i]}"
      pos=-1
      for j in "${!final_keys[@]}"; do [ "${final_keys[$j]}" = "$key" ] && pos="$j"; done
      if [ "$pos" = -1 ]; then final_keys+=("$key"); final_vals+=("$value"); notes+=("set $key")
      elif [ "$keep" != keep ] && [ "${final_vals[$pos]}" != "$value" ]; then final_vals[pos]="$value"; notes+=("updated $key"); fi
    done
  fi

  if [ "${#final_keys[@]}" = 0 ]; then
    skipped ".env.host isn't needed: everything is already on the PATH"
  else
    new="$(mktemp)"
    {
      echo "# Written by ./setupenvironment.sh for host mode (no Docker). Shell syntax, every value"
      echo "# single-quoted (connection strings contain ';'). Never commit it: it holds a database"
      echo "# password. Read by scripts/test.sh, scripts/dev-host.sh and the others. To use the same"
      echo "# tools in your own shell:   . ./.env.host"
      echo "# Edit a value freely: setupenvironment.sh keeps the passwords and connections it finds here."
      for i in "${!final_keys[@]}"; do echo "export ${final_keys[$i]}=$(shell_quote "${final_vals[$i]}")"; done
      echo '_p="${WEBBASE_HOST_PATH:-}"; while [ -n "$_p" ]; do _d="${_p%%:*}"; case ":$PATH:" in *":$_d:"*) ;; *) PATH="$_d:$PATH" ;; esac; case "$_p" in *:*) _p="${_p#*:}" ;; *) _p="" ;; esac; done; export PATH; unset _p _d'
    } > "$new"
    if [ -f "$ENV_FILE" ] && cmp -s "$new" "$ENV_FILE"; then
      found ".env.host is up to date"; rm -f "$new"
    elif [ "$CHECK" = 1 ]; then
      would "write .env.host (${notes[*]:-new})"; NEEDS+=(".env.host needs writing"); rm -f "$new"
    else
      [ -f "$ENV_FILE" ] || did "Created .env.host (mode 600)"
      ( umask 077; cat "$new" > "$ENV_FILE" ); chmod 600 "$ENV_FILE"; rm -f "$new"
      for note in "${notes[@]+"${notes[@]}"}"; do did ".env.host: $note"; done
    fi
  fi
fi

# ---- Summary --------------------------------------------------------------------------------
echo
bold "Summary ($MODE mode$( [ "$CHECK" = 1 ] && echo ', check only'))"
print_list() { # title, items...
  local title="$1"; shift
  [ "$#" -gt 0 ] || return 0
  printf '\n  %s\n' "$title"
  local item
  for item in "$@"; do printf '    %s\n' "$item"; done
}
print_list "Found (already there):" "${FOUND[@]+"${FOUND[@]}"}"
print_list "Installed or configured now:" "${DONE[@]+"${DONE[@]}"}"
print_list "Skipped:" "${SKIPPED[@]+"${SKIPPED[@]}"}"
print_list "Would change (--check):" "${WOULD[@]+"${WOULD[@]}"}"
print_list "Still needs you:" "${NEEDS[@]+"${NEEDS[@]}"}"
echo

if [ "${#NEEDS[@]}" -gt 0 ]; then
  warn "Not ready yet: fix the items under \"Still needs you\", then run ./setupenvironment.sh again (it only does what's left)."
  [ "$CHECK" = 1 ] && warn "Run without --check to install what can be installed."
  STATUS=1
else
  if [ "$CHECK" = 1 ]; then ok "Nothing to do: this machine is ready."; else ok "This machine is ready."; fi
  STATUS=0
fi

echo
bold "Next:"
if [ "$MODE" = docker ]; then
  cat <<EOF

  ./setup.sh             name the project and create .env.local with its secrets
  ./dev.sh               start the development site, then open http://localhost:${DEV_WEB_PORT:-5173}
  ./scripts/test.sh      run every check
EOF
else
  cat <<EOF

  ./scripts/dev-host.sh up      start the API (:${DEV_API_PORT:-5000}) and the website (:${DEV_WEB_PORT:-5173}) without Docker
  ./scripts/test.sh             run the checks with the host's tools (picks host mode by itself)
  ./scripts/smoke.sh http://localhost:${DEV_WEB_PORT:-5173}   check a running site end to end
  ./scripts/dev-host.sh stop    stop it again

  In your own shell, to use the tools it found:   . ./.env.host
  (Nothing was added to your shell profile.)
EOF
fi
echo
exit "$STATUS"
