#!/usr/bin/env bash
# Bakes every old first-person clip named in art/held-motion/bakes.json into content/held/motion.json (and seats the
# looks they move), with scripts/bake-held-motion.py under Blender. Clips on the Hands pack's CAT rig are ASCII FBX,
# which Blender does not read, so unity-import (a sibling ../asset-pipeline, or ASSET_PIPELINE=) converts them first.
#
#   scripts/bake-held-motions.sh [NAME]     # one recipe entry by name, or all
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
pipeline=${ASSET_PIPELINE:-$root/../asset-pipeline}
producer=$pipeline/tools/unity-import/unity_import.py
recipe=$root/art/held-motion/bakes.json
[[ -d $root/old-game/Assets/MoreArt ]] || { echo "old-game/Assets/MoreArt is missing" >&2; exit 1; }
staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT

mapfile -t cat_clips < <(python3 -c '
import json, sys
for e in json.load(open(sys.argv[1])):
    if e.get("source") == "cat":
        print(e["clip"]); print(e["rest"])' "$recipe" | sort -u)
if ((${#cat_clips[@]})); then
  [[ -f $producer ]] || { echo "unity-import not found at $producer (set ASSET_PIPELINE)" >&2; exit 1; }
  python3 "$producer" install >/dev/null
  python3 "$producer" convert --assets "$root/old-game/Assets" $(printf -- '--select %s ' "${cat_clips[@]}") \
    --max-texture 64 --out "$staging" >/dev/null
fi
cd "$root"
blender -b --factory-startup --python scripts/bake-held-motion.py -- --recipe "$recipe" --converted "$staging" \
  ${1:+--only "$1"} | grep '^BAKED'
