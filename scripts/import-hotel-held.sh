#!/usr/bin/env bash
# Copies rusty-hotel's held models that riders draws into the ignored content/art/hotel/ (docs/reuse.md): the coach
# gun, the proof that one held format carries both games' looks (docs/held-format-convergence.md). Needs a sibling
# ../rusty-hotel (or RUSTY_HOTEL=); it is read, never changed.
#
#   scripts/import-hotel-held.sh
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
hotel=${RUSTY_HOTEL:-$root/../rusty-hotel}
out=$root/content/art/hotel
models=(coach-gun.glb)
[[ -d $hotel/content/models/held ]] || { echo "rusty-hotel not found at $hotel (set RUSTY_HOTEL)" >&2; exit 1; }

pin=$(sed -n 's:.*<RustyEnginePackageVersion>\(.*\)</RustyEnginePackageVersion>.*:\1:p' "$root/Directory.Build.props")
rusty=${RUSTY_ENGINE_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/rusty-engine}/pairs/$pin/runtime-pack/bin/rusty
mkdir -p "$out"
for model in "${models[@]}"; do
  cp "$hotel/content/models/held/$model" "$out/$model"
  "$rusty" asset check "$out/$model"
done
echo "copied ${models[*]} from rusty-hotel $(git -C "$hotel" rev-parse HEAD)"
