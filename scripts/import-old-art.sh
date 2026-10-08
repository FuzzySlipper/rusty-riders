#!/usr/bin/env bash
# Converts the old Unity game's art the product uses into content/old-art/ with asset-pipeline's unity-import
# producer: the combat tilesets and levels, the procedural-level tileset tiles, the starter weapons (their item
# prefabs) and one animated creature per starter enemy kind. It writes the GLBs placement files draw (whole models
# and per-mesh parts), the animated enemy models, the placement files and per-model reports. The output is
# generated and ignored. docs/direction.md "Converted weapons and enemies" says which old models were chosen.
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
view=$root/.runtime/old-art-view       # hard-linked Assets with the enemy clips beside their models

[[ -f $producer ]] || { echo "unity-import not found at $producer (set ASSET_PIPELINE)" >&2; exit 1; }
[[ -d $assets ]] || { echo "old Unity project not found at $assets" >&2; exit 1; }
[[ -d $overlay ]] || { echo "upgraded FBX overlay not found at $overlay (see asset-pipeline docs/unity-import.md)" >&2; exit 1; }

# Starter weapons: an item prefab each (rift-blade, maul, laser-flintlock, ember-wand, frost-sigil).
weapons=(
  GameData/Items/Weapons/Models/Prefabs/1H_sword_C_ShortSword.prefab
  GameData/Items/Weapons/Models/Prefabs/BattleHammer_Maul.prefab
  GameData/Items/Weapons/Models/Prefabs/Flintlock_Flintlock.prefab
  Art/Models/Weapons/Mandragora.prefab
  Art/Models/Weapons/Ravenwood.prefab
)
# Starter enemies (ripper, gunner, brute): the model folder, its FBX base name, the NPC prefab. Most of each
# creature's clips (idle, getHit, death, in-place walk/run) live in Art/Animations/Character, not beside the model.
enemies=(
  "Art/Models/Enemies/KOTG HARPY/FBX FILES|HARPY|GameData/NPCs/Models/KOTG HARPY.prefab"
  "Art/Models/Enemies/HC Weresquid|WERESQUID|GameData/NPCs/Models/HC Weresquid.prefab"
  "Art/Models/Enemies/OH Demon Brute|DemonBrute|GameData/NPCs/Models/OH DemonBrute.prefab"
)
clips=Art/Animations/Character

# unity-import merges a Model@Clip file only from its model's own folder, so the conversion reads a hard-linked
# copy of Assets (no data copied; links must stay inside the Assets root, which rules out symlinks) with each
# enemy's Character clips linked beside its model. Unity bound those clips through the creature's animator.
rm -rf "$view"; mkdir -p "$view"
cp -al "$assets" "$view/Assets" || { echo "hard-linking $assets into $view failed (same filesystem needed)" >&2; exit 1; }
prefabs=$(IFS=,; echo "GameData/Combat/Tilesets,GameData/Combat/Levels,GameData/Levels/TileSets,${weapons[*]}")
selects=() npcs=() keep=()
for enemy in "${enemies[@]}"; do
  IFS='|' read -r folder base npc <<<"$enemy"
  for clip in "$view/Assets/$clips/$base"@*; do
    [[ -e "$view/Assets/$folder/${clip##*/}" ]] || ln "$clip" "$view/Assets/$folder/${clip##*/}"
  done
  # The base model and its in-place clips; root-motion (_RM) duplicates are left out.
  while IFS= read -r -d '' model; do
    selects+=(--select "${model#"$view/Assets/"}")
    [[ ${model##*/} == *@* ]] || keep+=(--keep "${model#"$view/Assets/"}")  # the animated model no placement draws
  done < <(find "$view/Assets/$folder" -maxdepth 1 -type f \( -iname "$base.fbx" -o -iname "$base@*.fbx" \) ! -iname '*_RM.fbx' -print0)
  npcs+=("$npc")
  prefabs+=",$npc"
done

# Check every GLB against the product's pinned Engine pair rather than asset-pipeline's.
rusty_args=()
if pack=$(rusty status 2>/dev/null | awk '/^runtime pack/ {print $3}') && [[ -x $pack/bin/rusty ]]; then
  rusty_args=(--rusty "$pack/bin/rusty")
fi

python3 "$producer" install >/dev/null
rm -rf "$staging"; mkdir -p "$(dirname "$staging")"
python3 "$producer" convert --assets "$view/Assets" --overlay "$overlay" --max-texture "${MAX_TEXTURE:-1024}" \
  --placement-prefabs "$prefabs" --material-prefabs "$(IFS=,; echo "${npcs[*]}")" "${selects[@]}" \
  "${rusty_args[@]}" --out "$staging" >/dev/null
python3 "$root/scripts/prune-old-art.py" "$staging" "${keep[@]}"
rm -rf "$view" "$out"
mv "$staging" "$out"
