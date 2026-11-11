#!/bin/bash
set -euo pipefail

# Run by the modsdude-backup@ systemd units: backup.sh <hourly|daily|deploy>. Dumps the database, checks the
# dump can be read back, uploads it to the backup container and pings the tier's healthchecks.io check, or
# its /fail endpoint if any step fails. The daily tier also restores its dump into a scratch database, which
# proves the data loads and not only that the archive is whole. The deploy tier is run by deploy.sh before
# it migrates, and has no check to ping: a failure fails the deploy.
#
# The settings come from .env through the unit's EnvironmentFile. BACKUP_SAS_URL may only create blobs, and
# the server's own Azure login may only read the backup account (see README.md), so nothing on this server
# can overwrite or delete a backup.

tier="${1:-}"

case "$tier" in
  hourly) ping_url="${BACKUP_PING_HOURLY:?is not set in .env}" ;;
  daily) ping_url="${BACKUP_PING_DAILY:?is not set in .env}" ;;
  deploy) ping_url="" ;;
  *) echo "Usage: backup.sh <hourly|daily|deploy>" >&2; exit 2 ;;
esac

sas_url="${BACKUP_SAS_URL:?is not set in .env}"

ping_check() {
  if [ -n "$ping_url" ]; then
    curl --fail --silent --show-error --max-time 10 --retry 3 "$ping_url$1" > /dev/null
  fi
}

trap 'ping_check /fail || true' ERR

cd "$(dirname "$0")"

# Read back by the admin page, which takes the time from the name: when the dump started, in UTC.
name="$tier/modsdude-$(date -u +%Y%m%dT%H%M%SZ).dump"

# In the home folder rather than /tmp, which can be a small tmpfs.
dump=$(mktemp --tmpdir="$HOME" .modsdude-backup.XXXXXX)
trap 'rm -f "$dump"' EXIT

docker compose exec -T db pg_dump --username modsdude --format custom modsdude > "$dump"

if [ ! -s "$dump" ]; then
  echo "pg_dump wrote nothing." >&2
  false
fi

# Reads the archive's table of contents, so a dump that isn't a readable archive is never uploaded. Only
# the daily tier's restore below reads the data as well.
docker compose exec -T db pg_restore --list < "$dump" > /dev/null

# Azure checks the upload against it and rejects one that arrived damaged.
md5=$(openssl dgst -md5 -binary "$dump" | base64)

# The container URL with the blob's name spliced in before the SAS query. Not retried on every error: a
# Create-only SAS answers a repeat of an upload that did land with 409, which would fail a good backup.
curl --fail --silent --show-error --retry 3 \
  --upload-file "$dump" \
  --header "x-ms-blob-type: BlockBlob" \
  --header "x-ms-version: 2023-11-03" \
  --header "Content-Type: application/octet-stream" \
  --header "Content-MD5: $md5" \
  "${sas_url%%\?*}/$name?${sas_url#*\?}"

echo "Uploaded $name ($(stat --format %s "$dump") bytes)."

# After the upload, so a dump that fails to restore is still kept for whoever investigates.
if [ "$tier" = "daily" ]; then
  scratch=modsdude_restore_check

  drop_scratch() {
    docker compose exec -T db dropdb --username modsdude --if-exists "$scratch"
  }
  trap 'drop_scratch || true; rm -f "$dump"' EXIT

  drop_scratch
  docker compose exec -T db createdb --username modsdude "$scratch"
  docker compose exec -T db pg_restore --username modsdude --dbname "$scratch" --no-owner --exit-on-error < "$dump"
  drop_scratch

  echo "Restored $name into a scratch database."
fi

ping_check ""
