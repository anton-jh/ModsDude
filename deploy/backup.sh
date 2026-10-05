#!/bin/bash
set -euo pipefail

# Run by the modsdude-backup@ systemd units: backup.sh <hourly|daily>. Dumps the database, uploads the dump
# to the backup container and pings the tier's healthchecks.io check, or its /fail endpoint if any step fails.
#
# The settings come from .env through the unit's EnvironmentFile. BACKUP_SAS_URL may only create blobs, so
# nothing on this server can overwrite or delete a backup.

tier="${1:-}"

case "$tier" in
  hourly) ping_url="${BACKUP_PING_HOURLY:?is not set in .env}" ;;
  daily) ping_url="${BACKUP_PING_DAILY:?is not set in .env}" ;;
  *) echo "Usage: backup.sh <hourly|daily>" >&2; exit 2 ;;
esac

sas_url="${BACKUP_SAS_URL:?is not set in .env}"

report_failure() {
  curl --fail --silent --show-error --max-time 10 --retry 3 "$ping_url/fail" > /dev/null || true
}
trap report_failure ERR

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

curl --fail --silent --show-error --max-time 10 --retry 3 "$ping_url" > /dev/null
