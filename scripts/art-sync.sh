#!/usr/bin/env bash
# Copies the ignored content/art/ to or from its store on the den NAS: a backup and the way a new checkout gets the
# art, without keeping versions. content/art holds every art file the game draws (converted GLBs and placements,
# arms, hotel's held models, textures, UI art); only each folder's sources.json is in git.
#
#   scripts/art-sync.sh push            # this checkout's art to the store (adds and updates, deletes nothing)
#   scripts/art-sync.sh pull            # the store's art into this checkout (adds and updates, deletes nothing)
#   scripts/art-sync.sh push --mirror   # also delete what the destination has and the source lacks
#   RIDERS_ART_STORE=/path scripts/art-sync.sh pull
#
# Windows checkouts pull with scripts/art-sync.ps1.
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
art=$root/content/art
store=${RIDERS_ART_STORE:-/mnt/den-nas-backup/data/rusty-riders/art}

direction=${1:-}
mirror=()
[[ ${2:-} == --mirror ]] && mirror=(--delete)
case $direction in
  push) from=$art; to=$store ;;
  pull) from=$store; to=$art ;;
  *) echo "usage: scripts/art-sync.sh push|pull [--mirror]" >&2; exit 2 ;;
esac
[[ -d $from ]] || { echo "art-sync: $from does not exist" >&2; exit 1; }
mkdir -p "$to"
rsync -rt --info=stats1 "${mirror[@]}" "$from/" "$to/"
