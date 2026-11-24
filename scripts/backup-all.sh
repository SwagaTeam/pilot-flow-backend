#!/usr/bin/env bash
set -euo pipefail
umask 077
base=/opt/pilot-flow
release=$base
[[ ! -L "$base/current" ]] || release=$(readlink -f "$base/current")
PILOTFLOW_ENV_FILE="$base/deploy/.env" PILOTFLOW_BACKUP_DIR="$base/backups" python3 "$release/scripts/deploy.py" backup
stamp=$(date -u +%Y%m%dT%H%M%SZ)
partial=$(mktemp "$base/backups/.auth-partial-XXXXXX")
trap 'rm -f "$partial"' EXIT
docker compose --project-directory "$base" --env-file "$base/deploy/auth.env" -f "$base/compose.auth.yaml" exec -T auth-db pg_dump -U keycloak -d keycloak -Fc --no-owner --no-privileges > "$partial"
docker compose --project-directory "$base" --env-file "$base/deploy/auth.env" -f "$base/compose.auth.yaml" exec -T auth-db pg_restore --list < "$partial" > /dev/null
mv "$partial" "$base/backups/keycloak-$stamp.dump"
echo "Keycloak backup saved."
