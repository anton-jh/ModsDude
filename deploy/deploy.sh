#!/bin/bash
set -euo pipefail

# Run by deploy-server in ~/modsdude on the server, after it uploaded this folder's files and the build's
# modsdude-api.tar and migrate.sql next to them: deploy.sh <build number>.
#
# A failed migration stops it before the API is touched, so the running build keeps serving. There is no
# rollback past that point: a later failure is fixed by deploying a fixed build.

build="$1"

cd "$(dirname "$0")"

# Checked before anything changes. Without linger the backup timers stop whenever nobody is logged in.
if [ "$(loginctl show-user "$USER" --property=Linger --value)" != "yes" ]; then
  echo "Linger is off for $USER, so the backup timers would not run. See README.md." >&2
  exit 1
fi

for setting in BACKUP_SAS_URL BACKUP_PING_HOURLY BACKUP_PING_DAILY; do
  if ! grep --quiet "^$setting=." .env; then
    echo "$setting is not set in .env. See README.md." >&2
    exit 1
  fi
done

docker load --input modsdude-api.tar
rm modsdude-api.tar

docker compose up --detach --wait --remove-orphans db caddy
docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile

# Idempotent: applying it again, or with nothing pending, changes nothing.
docker compose exec -T db psql --username modsdude --dbname modsdude --set ON_ERROR_STOP=1 --quiet < migrate.sql

# Only now, so that nothing - not even a later `docker compose up` by hand - starts this build against a
# database it has not been migrated for.
docker tag "modsdude-api:b$build" modsdude-api:current
docker compose up --detach --force-recreate api

# User units, so every deploy can update them without root.
mkdir -p ~/.config/systemd/user
cp systemd/* ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now modsdude-backup-hourly.timer modsdude-backup-daily.timer
