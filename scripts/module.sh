#!/usr/bin/env bash
# Build the app with, or without, an optional module (docs/MODULES.md).
#
#   ./scripts/module.sh list            which modules exist, and which the app uses
#   ./scripts/module.sh add gcs         build the app with Google Cloud Storage
#   ./scripts/module.sh remove cosmos   and without one again
#   ./scripts/module.sh set cosmos azure   exactly these (the image build uses this: ops/Dockerfile)
#
# Modules: postgres, cosmos (databases); s3, azure, gcs (file storage).
# Adding one makes two edits, which you can also make by hand: a
# <ProjectReference> in src/WebBase.Api/WebBase.Api.csproj, and a line in
# src/WebBase.Api/Modules.cs. Neither needs Docker. Commit both.
. "$(dirname "$0")/lib.sh"

CSPROJ="${API_DIR}/WebBase.Api.csproj"
MODULES_CS="${API_DIR}/Modules.cs"

# name -> project (also its namespace) | module class | which list in Modules.cs
info() {
  case "$1" in
    postgres) echo "WebBase.Database.Postgres|PostgresModule|database" ;;
    cosmos)   echo "WebBase.Database.CosmosDB|CosmosModule|database" ;;
    s3)       echo "WebBase.FileStorage.S3|S3Module|file storage" ;;
    azure)    echo "WebBase.FileStorage.AzureBlob|AzureBlobModule|file storage" ;;
    gcs)      echo "WebBase.FileStorage.GoogleCloud|GoogleCloudStorageModule|file storage" ;;
    *) die "No module called '$1'. Try: postgres, cosmos, s3, azure, gcs." ;;
  esac
}

in_app() { grep -q "modules\\\\$1\\\\$1.csproj" "$CSPROJ"; }

case "${1:-}" in
  list)
    for name in postgres cosmos s3 azure gcs; do
      IFS='|' read -r project _ kind <<<"$(info "$name")"
      if in_app "$project"; then mark="in the app"; else mark="-"; fi
      printf '  %-9s %-13s %-41s %s\n' "$name" "$kind" "modules/${project}" "$mark"
    done
    ;;

  add)
    [ -n "${2:-}" ] || die "Which one? ./scripts/module.sh add <name>"
    IFS='|' read -r project class kind <<<"$(info "$2")"
    [ -d "modules/${project}" ] || die "modules/${project} isn't there (deleted?)."
    if in_app "$project"; then ok "${2} is already in the app."; exit 0; fi
    # Same \ separators as the existing references, for Visual Studio's sake.
    perl -0pi -e 's|(<ItemGroup Label="Modules">\n)|$1    <ProjectReference Include="..\\..\\modules\\'"${project}"'\\'"${project}"'.csproj" />\n|' "$CSPROJ"
    perl -0pi -e 's|(// \(module usings\))|using '"${project}"';\n$1|' "$MODULES_CS"
    perl -0pi -e 's|(\n(\s+)// \('"${kind}"' modules\))|\n$2new '"${class}"'(),$1|' "$MODULES_CS"
    ok "Added ${2}: the app is now built with modules/${project}."
    echo "    Its settings are in modules/${project}/README.md. Commit the two changed files:"
    echo "    ${CSPROJ} and ${MODULES_CS}"
    ;;

  remove)
    [ -n "${2:-}" ] || die "Which one? ./scripts/module.sh remove <name>"
    IFS='|' read -r project class kind <<<"$(info "$2")"
    if ! in_app "$project"; then ok "${2} isn't in the app."; exit 0; fi
    perl -ni -e 'print unless /modules\\'"${project}"'\\'"${project}"'\.csproj/' "$CSPROJ"
    perl -ni -e 'print unless /^using '"${project//./\\.}"';$/ || /^\s+new '"${class}"'\(\),$/' "$MODULES_CS"
    ok "Removed ${2} from the app. (modules/${project} is still there, built and tested, just not used.)"
    if [ "$kind" = database ] && ! grep -qE '^\s+new \w+Module\(\),$' <(sed -n '/Databases/,/database modules/p' "$MODULES_CS"); then
      warn "The app has no database module now and won't start. Add one: ./scripts/module.sh add postgres"
    fi
    ;;

  set)
    shift
    [ $# -gt 0 ] || die "Which ones? ./scripts/module.sh set cosmos azure"
    for name in "$@"; do info "$name" >/dev/null; done   # unknown names stop here
    [[ " $* " == *" postgres "* || " $* " == *" cosmos "* ]] || die "The app needs a database module: postgres or cosmos."
    for name in "$@"; do "$0" add "$name" >/dev/null; done
    for name in postgres cosmos s3 azure gcs; do
      [[ " $* " == *" $name "* ]] || "$0" remove "$name" >/dev/null
    done
    ok "The app is built with: $*"
    ;;

  *)
    sed -n '2,13p' "$0"; exit 1 ;;
esac
