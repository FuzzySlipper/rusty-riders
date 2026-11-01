#!/usr/bin/env bash
# In-game contact sheet of the main hand's action: builds, starts a private headless host (its own instance, stopped
# afterwards), selects an inventory slot, holds world time and captures the view at rest and every STEP milliseconds of
# world time (rounded up to whole 60 Hz updates; tiles show the time admitted, and the action's landing and event
# cues each step passed, from riders.combat.moments) through one use of the weapon in hand. Needs rusty dev's live debug and a Chromium for headless hosts (RIDERS_CHROMIUM).
#
#   scripts/arm-clips/in-game.sh OUT_DIR [SLOT=1] [FRAMES=20] [STEP=33] [use|reload]
#   (STEP 16 captures every update; reload fires once first, so the gun has a round to take)
set -euo pipefail
out=$1; slot=${2:-1}; frames=${3:-20}; step=${4:-33}; act=${5:-use}
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
chromium=${RIDERS_CHROMIUM:-$(ls -d ~/.cache/ms-playwright/chromium-*/chrome-linux64/chrome 2>/dev/null | tail -1)}
instance=arm-clips-$$
mkdir -p "$out"; cd "$root"
rusty build 2>&1 | grep -E " error " && exit 1
port=$(rusty dev start --live-debug --headless --chromium "$chromium" --instance $instance --label 'arm-clips in-game sheet' \
  --idle-timeout 30 2>&1 | grep -o '"port":[0-9]*' | cut -d: -f2)
trap 'rusty dev stop --instance $instance >/dev/null 2>&1' EXIT
origin=http://127.0.0.1:$port
debug=$(ls -d ~/.cache/rusty-engine/pairs/"$(sed -n 's:.*<RustyEnginePackageVersion>\(.*\)</RustyEnginePackageVersion>.*:\1:p' Directory.Build.props)"/runtime-pack/bin/rusty-live-debug)
run() { "$debug" --origin "$origin" --command "$*" >/dev/null 2>&1; }
capture() {
  curl -s -H "Origin: $origin" "$origin/__rusty/product/runtime/frames/capture?format=png&width=960&height=540" |
    python3 -c "import sys; b = sys.stdin.buffer.read(); open(sys.argv[1], 'wb').write(b[b.find(b'\x89PNG'):])" "$out/$1.png"
}
sleep 4; run riders.dev.select "$slot"; sleep 2
run engine.time.mode manual; run engine.time.advance 300
[[ $act == reload ]] && { run riders.dev.use; run engine.time.advance 1000; run riders.combat.moments; }
capture f00
run riders.dev.$act
# The world advances whole updates, so record the milliseconds each step really admitted, and what the action passed.
: > "$out/ms.txt"; : > "$out/moments.txt"
for i in $(seq 1 "$frames"); do
  "$debug" --origin "$origin" --command "engine.time.advance $step" 2>/dev/null | grep -o '"advancedMs":[0-9.]*' | cut -d: -f2 >> "$out/ms.txt"
  { "$debug" --origin "$origin" --command riders.combat.moments 2>/dev/null | grep -oE '[A-Za-z]+ at [0-9.]+ s' || true; } |
    paste -sd ';' >> "$out/moments.txt"
  capture "$(printf 'f%02d' "$i")"
done
python3 - "$out" "$frames" <<'PY'
import sys
from PIL import Image, ImageDraw
out, frames = sys.argv[1], int(sys.argv[2])
at = [0.0]
for line in open(f"{out}/ms.txt"): at.append(at[-1] + float(line))
moments = [""] + [line.strip().replace(";", ", ") for line in open(f"{out}/moments.txt")]
cols = 4; rows = -(-(frames + 1) // cols)
sheet = Image.new("RGB", (480 * cols, 270 * rows))
for i in range(frames + 1):
    tile = Image.open(f"{out}/f{i:02d}.png").resize((480, 270))
    draw = ImageDraw.Draw(tile)
    draw.text((8, 8), "rest" if i == 0 else f"+{at[i]:.0f} ms", fill="white")
    if i < len(moments) and moments[i]: draw.text((8, 24), moments[i], fill="yellow")
    sheet.paste(tile, ((i % cols) * 480, (i // cols) * 270))
sheet.save(f"{out}/sheet.png")
PY
echo "$out/sheet.png"
