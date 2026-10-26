#!/usr/bin/env bash
# One-time setup for a new copy of this project. Safe to run again.
#
#   ./setup.sh
#
# 1. checks Docker is installed and running
# 2. offers to rename the project (WebBase -> YourName) if it hasn't been yet
# 3. creates .env.local with freshly generated secrets
. "$(dirname "$0")/scripts/lib.sh"

bold "Setting up $(basename "$ROOT")"
echo

say "Checking Docker"
need_docker
ok "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null) is running"

# ---- Rename -----------------------------------------------------------------
if [ -d src/WebBase.Api ] && [ ! -f .env.local ]; then
  echo
  say "Naming your project"
  echo "    This project is still called WebBase. Give it its own name now (letters and"
  echo "    digits, starting with a capital, e.g. RecipeBox), or press Enter to keep it."
  read -r -p "    Project name: " NEW_NAME
  if [ -n "$NEW_NAME" ] && [ "$NEW_NAME" != "WebBase" ]; then
    ./scripts/rename.sh "$NEW_NAME"
    # Everything, including this script's helpers, now has the new name.
    exec ./setup.sh
  fi
fi

# ---- .env.local -------------------------------------------------------------
echo
say "Settings (.env.local)"
if [ ! -f .env.local ]; then
  cp .env.example .env.local
  chmod 600 .env.local
  ok "Created .env.local from .env.example"
fi

for key in JWT_KEY POSTGRES_PASSWORD POSTGRES_APP_PASSWORD; do
  if [ -z "$(env_value "$key")" ]; then
    length=40; [ "$key" = JWT_KEY ] && length=64
    set_env_value "$key" "$(random_secret "$length")"
    ok "Generated $key"
  else
    ok "$key already set - left alone"
  fi
done
chmod 600 .env.local

# ---- Next -------------------------------------------------------------------
echo
bold "All set. Next:"
cat <<EOF

  ./dev.sh            start the development site, then open http://localhost:$(env_value DEV_WEB_PORT || echo 5173)
  ./deploy.sh         build and start the production copy (http://localhost:$(env_value PROD_PORT || echo 8080))
  ./tunnel.sh on      put production on your own domain (docs/CLOUDFLARE_TUNNEL.md)

  Optional: Google sign-in -> docs/GOOGLE_SIGNIN.md
  Lost? Start with docs/GETTING_STARTED.md
EOF
