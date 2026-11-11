#!/bin/bash
set -euo pipefail

# Run by deploy-server in ~/modsdude on the server, after it uploaded this folder's files and the build's
# modsdude-api.tar and migrate.sql next to them: deploy.sh <build number>.
#
# The running build is stopped before the database is backed up and migrated, so neither it nor its jobs
# ever run against a schema it was not built for, and the backup is the database exactly as it was. A
# failed backup or migration leaves the database as it was and starts the running build again. There is
# no rollback past that point: a later failure is fixed by deploying a fixed build.

build="$1"

cd "$(dirname "$0")"

# Checked before anything changes. Without linger the backup timers stop whenever nobody is logged in.
if [ "$(loginctl show-user "$USER" --property=Linger --value)" != "yes" ]; then
  echo "Linger is off for $USER, so the backup timers would not run. See README.md." >&2
  exit 1
fi

for setting in DEPLOY_TOKEN BACKUP_SAS_URL BACKUP_PING_HOURLY BACKUP_PING_DAILY; do
  if ! grep --quiet "^$setting=." .env; then
    echo "$setting is not set in .env. See README.md." >&2
    exit 1
  fi
done

docker load --input modsdude-api.tar
rm modsdude-api.tar

# User units, so every deploy can update them without root. Before the backup below, which runs as one.
mkdir -p ~/.config/systemd/user
cp systemd/* ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now modsdude-backup-hourly.timer modsdude-backup-daily.timer

docker compose up --detach --wait --remove-orphans db caddy
docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile

restart_running_build() {
  echo "The deploy failed before b$build was started; starting the running build again." >&2
  # Nothing to start on the first deploy, which has no running build.
  docker compose start api || true
}

# EXIT rather than ERR, so a deploy cut off part way - its ssh connection lost - tries to as well.
docker compose stop api
trap restart_running_build EXIT

# Waits for the backup to finish and fails if it did. Through systemd, which reads .env the way the
# timers' backups do.
if ! systemctl --user start modsdude-backup@deploy.service; then
  echo "The pre-deploy backup failed: journalctl --user --unit modsdude-backup@deploy" >&2
  false
fi

# Idempotent: applying it again, or with nothing pending, changes nothing. In one transaction, so a
# failure applies none of the pending migrations.
docker compose exec -T db psql --username modsdude --dbname modsdude --set ON_ERROR_STOP=1 --single-transaction --quiet < migrate.sql

trap - EXIT

# Only now, so that nothing - not even a later `docker compose up` by hand - starts this build against a
# database it has not been migrated for.
docker tag "modsdude-api:b$build" modsdude-api:current
docker compose up --detach --force-recreate api
