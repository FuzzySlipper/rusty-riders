#!/usr/bin/env bash
# Builds the old game's first-person arms into the ignored content/art/fp-arms/ (docs/direction.md, "First-person
# arms"): painted.glb (the Hands pack's arms with the hand-painted texture) and leather.glb (MedievalFPArms' male
# leather armour arms). Their rig profiles are content/held/rigs/. Needs old-game/Assets/MoreArt, Blender 5 and a
# sibling ../asset-pipeline (or ASSET_PIPELINE=) for unity-import, which reads the Hands pack's ASCII FBX.
#
#   scripts/import-fp-arms.sh
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
pipeline=${ASSET_PIPELINE:-$root/../asset-pipeline}
producer=$pipeline/tools/unity-import/unity_import.py
assets=$root/old-game/Assets
out=$root/content/art/fp-arms
[[ -d $assets/MoreArt/FirstPersonArms && -d $assets/MoreArt/FpMelee/MedievalFPArms ]] || { echo "old-game/Assets/MoreArt is missing" >&2; exit 1; }
[[ -f $producer ]] || { echo "unity-import not found at $producer (set ASSET_PIPELINE)" >&2; exit 1; }

pin=$(sed -n 's:.*<RustyEnginePackageVersion>\(.*\)</RustyEnginePackageVersion>.*:\1:p' "$root/Directory.Build.props")
rusty=${RUSTY_ENGINE_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/rusty-engine}/pairs/$pin/runtime-pack/bin/rusty
staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT

python3 "$producer" install >/dev/null
python3 "$producer" convert --assets "$assets" --select MoreArt/FirstPersonArms/Animations/Hands_Gun_03_Mesh.FBX \
  --max-texture 1024 --rusty "$rusty" --out "$staging/import" >/dev/null
blender -b --factory-startup --python "$root/scripts/build-fp-arms.py" -- \
  "$assets" "$staging/import/models/MoreArt/FirstPersonArms/Animations/Hands_Gun_03_Mesh.glb" "$out" | grep '^BUILT'
for glb in "$out"/painted.glb "$out"/leather.glb; do
  "$rusty" asset check "$glb"
done
