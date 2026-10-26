#!/usr/bin/env bash
# Give this project its own name, everywhere: C# namespaces, project files,
# folders, Docker image and database names, docs.
#
#   ./scripts/rename.sh RecipeBox
#
# "WebBase" becomes "RecipeBox" and "webbase" becomes "recipebox". Do it
# before the first ./dev.sh: the databases are named after the project, so an
# existing database would be left behind under the old name.
. "$(dirname "$0")/lib.sh"

NEW="${1:-}"
[[ "$NEW" =~ ^[A-Z][A-Za-z0-9]{1,40}$ ]] \
  || die "Usage: ./scripts/rename.sh NewName   (letters and digits, starting with a capital: RecipeBox)"
OLD="WebBase"
OLD_SLUG="$SLUG"
NEW_SLUG="$(echo "$NEW" | tr '[:upper:]' '[:lower:]')"
[ "$NEW" = "$OLD" ] && die "It is already called $OLD."
[ -d "src/${OLD}.Api" ] || die "Can't find src/${OLD}.Api - has this project already been renamed?"
command -v perl >/dev/null || die "perl is needed for renaming (it comes with macOS and most Linux)."

if [ -d .docker-data ]; then
  warn ".docker-data has databases made under the old name; they won't be found after renaming."
  confirm "Delete .docker-data (all local data) and continue?" || exit 1
  docker run --rm -v "$ROOT:/r" alpine rm -rf /r/.docker-data
fi

say "Renaming ${OLD} -> ${NEW} (and ${OLD_SLUG} -> ${NEW_SLUG})"

# Every text file we own: not git's internals, dependencies or build output.
files() {
  find . \( -name .git -o -name node_modules -o -name bin -o -name obj -o -name dist -o -name .docker-data \) -prune -o \
    -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.sln' -o -name '*.json' -o -name '*.md' -o -name '*.sh' \
    -o -name '*.yaml' -o -name '*.yml' -o -name '*.ts' -o -name '*.vue' -o -name '*.html' -o -name '*.css' \
    -o -name 'Dockerfile' -o -name '.env.example' -o -name '*.http' -o -name '*.svg' \) -print
}
files | while IFS= read -r f; do
  if grep -qE "${OLD}|${OLD_SLUG}" "$f"; then
    perl -pi -e "s/${OLD}/${NEW}/g; s/${OLD_SLUG}/${NEW_SLUG}/g" "$f"
  fi
done

# Then files and folders with the name in them, deepest first.
find . \( -name .git -o -name node_modules -o -name .docker-data \) -prune -o -name "*${OLD}*" -print \
  | awk '{ print length, $0 }' | sort -rn | cut -d' ' -f2- \
  | while IFS= read -r path; do
      dir="$(dirname "$path")"; base="$(basename "$path")"
      mv "$path" "${dir}/${base//${OLD}/${NEW}}"
    done

# Build output from the old name would only confuse the next build.
find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true

ok "Renamed to ${NEW}."
echo "    Images will be called ${NEW_SLUG}-app, databases ${NEW_SLUG}."
echo "    If this is a git repository, commit the result: git add -A && git commit -m 'Rename to ${NEW}'"
