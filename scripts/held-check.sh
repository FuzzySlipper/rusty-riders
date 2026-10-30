#!/usr/bin/env bash
# Steps every held look (or the named ones) at rest on a running rusty dev host with live debug, and records the arms
# readouts and a 1920x1080 frame of each: the arms (rig, wrist shortfall), each wrist's bend and clamp, the knuckles,
# each main digit joint's distance from the main grip's line (a joint nearer than the handle's radius is inside it),
# and the grip landmarks. Prints one wrist line per look and writes everything to OUT/held-check.txt.
#
#   rusty dev start --live-debug [--headless --chromium CHROME] --instance check --label 'held check'
#   scripts/held-check.sh http://127.0.0.1:PORT OUT [--rig shared] [LOOK...]
#   rusty dev stop --instance check
#
# The world is put in held (manual) time; each look is shown by the motion viewer with its motion's first action at
# 0 s and advanced half a second, so the arms re-reach and every readout is of a fresh frame.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
origin=${1:?usage: held-check.sh ORIGIN OUT [--rig RIG] [LOOK...]}
out=${2:?usage: held-check.sh ORIGIN OUT [--rig RIG] [LOOK...]}
shift 2
rig=""
if [[ ${1:-} == --rig ]]; then rig=$2; shift 2; fi
pair=$(sed -n 's:.*<RustyEnginePackageVersion>\(.*\)</RustyEnginePackageVersion>.*:\1:p' "$root/Directory.Build.props")
dbg=${RUSTY_LIVE_DEBUG:-$HOME/.cache/rusty-engine/pairs/$pair/runtime-pack/bin/rusty-live-debug}
[[ -x $dbg ]] || { echo "rusty-live-debug not found at $dbg (rusty install, or set RUSTY_LIVE_DEBUG)" >&2; exit 1; }
mkdir -p "$out"

# Each look with its motion's first action, from the content.
mapfile -t pairs < <(python3 - "$root/content/held" "$@" <<'EOF'
import json, sys
held, wanted = sys.argv[1], set(sys.argv[2:])
motions = json.load(open(f"{held}/motion.json"))["motions"]
for look in json.load(open(f"{held}/looks.json"))["looks"]:
    if wanted and look["id"] not in wanted:
        continue
    print(look["id"], next(iter(motions[look["motion"]]["tracks"])))
EOF
)

run() { echo ">> $*"; "$dbg" --origin "$origin" --command "$*" 2>&1 || true; }  # a readout a look lacks is reported, not fatal
advance() { "$dbg" --origin "$origin" --command "engine.time.advance 250" > /dev/null; }
capture() {
  curl -s -H "Origin: $origin" "$origin/__rusty/product/runtime/frames/capture?format=png&width=1920&height=1080" \
    | python3 -c "import sys;b=sys.stdin.buffer.read();open(sys.argv[1],'wb').write(b[b.find(b'\x89PNG'):])" "$out/$1.png"
}

{
  run engine.time.mode manual
  [[ -n $rig ]] && run riders.dev.arms.rig "$rig"
  for entry in "${pairs[@]}"; do
    read -r look action <<< "$entry"
    echo "=== $look ($action)"
    run riders.dev.motion "$look" "$action" | head -2 || true
    run riders.dev.motion.at 0
    advance; advance
    run riders.arms
    run riders.dev.arms.wrist
    run riders.dev.hands.knuckles
    run riders.dev.hands.haft
    run riders.dev.hands.contact
    capture "$look"
  done
} > "$out/held-check.txt"

# One line per look: its wrists' clamps.
awk '/^=== /{look=$2} /^  main \(/{main=$0} /^  off \(/{print look ":" main " |" $0}' "$out/held-check.txt" \
  | sed -E 's/WristBend \{ //g; s/ \}//g; s/  +/ /g'
echo "Readouts: $out/held-check.txt; frames: $out/<look>.png"
