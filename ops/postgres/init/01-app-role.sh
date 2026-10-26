#!/bin/bash
# Runs ONCE, the first time prod's database starts with an empty data folder.
#
# Creates the role the app connects as for normal requests: it may read and
# write rows, but not create, change or drop tables. Migrations run as the
# owner instead (ConnectionStrings__Migration). If a bug ever let an attacker
# run SQL, this caps the damage at rows rather than the whole database.
set -euo pipefail

if [ -z "${APP_DB_PASSWORD:-}" ]; then
  echo "APP_DB_PASSWORD is empty; refusing to create a passwordless role." >&2
  exit 1
fi

# The password is handed to psql as a variable and quoted by psql, never
# pasted into the SQL text.
psql -v ON_ERROR_STOP=1 \
     -v app_password="$APP_DB_PASSWORD" \
     -v db_name="$POSTGRES_DB" \
     -v owner="$POSTGRES_USER" \
     --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
CREATE ROLE webbase_app LOGIN PASSWORD :'app_password';
GRANT CONNECT ON DATABASE :"db_name" TO webbase_app;
GRANT USAGE ON SCHEMA public TO webbase_app;

-- Rows in every table, now and in every table a future migration creates.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO webbase_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO webbase_app;
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO webbase_app;
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO webbase_app;
SQL
