#!/usr/bin/env bash
# Bakes a clip-led melee action's sweep path from its clip as drawn in game: builds, starts a private headless host
# (its own instance, stopped afterwards), selects an inventory slot, holds world time, uses the main hand once and reads
# the drawn blade (riders.dev.held.blade) on every update through the commit. The tip's bearing from the eye, resampled
# to POINTS evenly spaced steps of the commit, becomes delivery.hit.path of ACTION in content/actions (with --write;
# otherwise it is only printed). The bearings follow the look's grip and length as well as the clip, so bake with the
# look the action is drawn with. Needs rusty dev's live debug and a Chromium for headless hosts (RIDERS_CHROMIUM).
#
#   scripts/arm-clips/bake-sweep.sh ACTION [SLOT=1] [POINTS=5] [--write]
set -euo pipefail
write=0; args=()
for a in "$@"; do [[ $a == --write ]] && write=1 || args+=("$a"); done
action=${args[0]}; slot=${args[1]:-1}; points=${args[2]:-5}
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
chromium=${RIDERS_CHROMIUM:-$(ls -d ~/.cache/ms-playwright/chromium-*/chrome-linux64/chrome 2>/dev/null | tail -1)}
instance=bake-sweep-$$
cd "$root"
rusty build 2>&1 | grep -E " error " && exit 1
port=$(rusty dev start --live-debug --headless --chromium "$chromium" --instance $instance --label 'arm-clips sweep bake' \
  --idle-timeout 30 2>&1 | grep -o '"port":[0-9]*' | cut -d: -f2)
trap 'rusty dev stop --instance $instance >/dev/null 2>&1' EXIT
origin=http://127.0.0.1:$port
debug=$(ls -d ~/.cache/rusty-engine/pairs/"$(sed -n 's:.*<RustyEnginePackageVersion>\(.*\)</RustyEnginePackageVersion>.*:\1:p' Directory.Build.props)"/runtime-pack/bin/rusty-live-debug)
run() { "$debug" --origin "$origin" --command "$*" 2>/dev/null; }
sleep 4; run riders.dev.select "$slot" >/dev/null; sleep 2
run engine.time.mode manual >/dev/null; run engine.time.advance 300 >/dev/null
run riders.dev.use main >/dev/null
samples=$(mktemp); trap 'rm -f "$samples"; rusty dev stop --instance $instance >/dev/null 2>&1' EXIT
# One update at a time until the blade is past the commit.
for _ in $(seq 1 240); do
  run engine.time.advance 16 >/dev/null
  line=$(run riders.dev.held.blade | grep -o '{"action".*}' || true)
  [[ -z $line ]] && { echo "bake-sweep: the main hand is not clip-led" >&2; exit 1; }
  echo "$line" >> "$samples"
  python3 -c "import json,sys; d=json.loads(sys.argv[1]); sys.exit(0 if d['action'] is None or d['commit'] > 1 else 1)" "$line" && break
done
python3 - "$samples" "$action" "$points" "$write" <<'PY'
import json, re, sys, glob
samples, action, points, write = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4] == "1"
rows = [json.loads(l) for l in open(samples)]
rows = [r for r in rows if r["action"] == action]
if not rows: sys.exit(f"bake-sweep: the main hand did not use {action}")
s = [(r["commit"], r["right"], r["up"]) for r in rows]
if s[0][0] > 0 or s[-1][0] < 1: sys.exit(f"bake-sweep: the samples do not span the commit ({s[0][0]:.2f} to {s[-1][0]:.2f})")
for c, r, u in s: print(f"  commit {c:6.3f}: right {r:7.2f}, up {u:7.2f}")
def at(t):
    for (c0, r0, u0), (c1, r1, u1) in zip(s, s[1:]):
        if c0 <= t <= c1:
            k = 0 if c1 == c0 else (t - c0) / (c1 - c0)
            return r0 + (r1 - r0) * k, u0 + (u1 - u0) * k
path = [at(i / (points - 1)) for i in range(points)]
text = "[" + ", ".join(f'{{ "right": {r:.1f}, "up": {u:.1f} }}' for r, u in path) + "]"
print(f'"path": {text}')
if not write: sys.exit(0)
for f in glob.glob("content/actions/**/*.json", recursive=True):
    src = open(f).read()
    m = re.search(r'"id":\s*"%s"' % re.escape(action), src)
    if not m: continue
    hit = re.compile(r'("hit":\s*\{[^{}]*?"path":\s*)\[[^\]]*\]', re.S).search(src, m.end())
    if not hit: sys.exit(f"bake-sweep: {f} gives {action} no sweep path to replace")
    open(f, "w").write(src[:hit.start()] + hit.group(1) + text + src[hit.end():])
    print(f"bake-sweep: wrote {f}")
    break
else: sys.exit(f"bake-sweep: no action {action} in content/actions")
PY
