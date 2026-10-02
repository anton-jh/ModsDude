#!/bin/bash
set -euo pipefail

# Run by deploy-server once the new build is up, so a failed deploy never removes anything. Keeps the
# current API image and the two newest others.

current=$(docker image inspect --format '{{.Id}}' modsdude-api:current)
kept=0

for number in $(docker image ls modsdude-api --format '{{.Tag}}' | sed -n 's/^b\([0-9]\+\)$/\1/p' | sort -rn); do
  if [ "$(docker image inspect --format '{{.Id}}' "modsdude-api:b$number")" = "$current" ]; then
    continue
  fi

  if [ "$kept" -lt 2 ]; then
    kept=$((kept + 1))
    continue
  fi

  echo "Removing b$number."
  docker image rm "modsdude-api:b$number"
done
