#!/usr/bin/env bash
# Converts the old Unity game's combat tilesets and levels into content/old-art/ with asset-pipeline's
# unity-import producer: GLBs, per-mesh part GLBs and placement files. The output is generated and ignored.
#
#   scripts/import-old-art.sh            # needs ../asset-pipeline, old-game/ and old-game-fbx2013/
#   ASSET_PIPELINE=/path/to/asset-pipeline MAX_TEXTURE=2048 scripts/import-old-art.sh
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
pipeline=${ASSET_PIPELINE:-$root/../asset-pipeline}
producer=$pipeline/tools/unity-import/unity_import.py
assets=$root/old-game/Assets
overlay=$root/old-game-fbx2013/Assets
out=$root/content/old-art
staging=$root/.runtime/old-art-import  # outside the watched content root until complete

[[ -f $producer ]] || { echo "unity-import not found at $producer (set ASSET_PIPELINE)" >&2; exit 1; }
[[ -d $assets ]] || { echo "old Unity project not found at $assets" >&2; exit 1; }
[[ -d $overlay ]] || { echo "upgraded FBX overlay not found at $overlay (see asset-pipeline docs/unity-import.md)" >&2; exit 1; }

python3 "$producer" install >/dev/null
rm -rf "$staging"; mkdir -p "$(dirname "$staging")"
python3 "$producer" convert --assets "$assets" --overlay "$overlay" --max-texture "${MAX_TEXTURE:-1024}" \
  --placement-prefabs GameData/Combat/Tilesets,GameData/Combat/Levels --out "$staging" >/dev/null
rm -rf "$out"
mv "$staging" "$out"
python3 - "$out/summary.json" <<'EOF'
import json, sys
s = json.load(open(sys.argv[1]))
print(f"content/old-art: {len(s['converted'])} models, {s['parts']} parts, {s['placementFiles']} placement files;"
      f" {len(s['failed'])} failed, {len(s['engineRefused'])} refused by the Engine check")
EOF
