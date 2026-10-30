#!/usr/bin/env python3
"""Fits a held look's tuning so both wrists hold it within the drawing rig's wrist range, on a running rusty dev host
with live debug, by the game's own readout (riders.dev.arms.wrist).

    scripts/held-wrist-fit.py http://127.0.0.1:PORT LOOK [--rig shared] [--tune hand|offhand|nudge] [--margin 5]

The look is shown at rest by the motion viewer (its motion's first action at 0 s, in held time). The tuned value is the
look's main hold (grips.hand, riders.dev.held.hand), its off hold on a look held in both hands (grips.offHand,
riders.dev.held.offhand), or its nudge's turn with its grip kept (riders.dev.held.nudge). A coordinate search turns it,
in steps from 16 degrees down to 1, to bring each wrist's asked bend (with the elbow at its pole, before the Engine's
elbow swing and clamp) inside the rig's range less --margin, while keeping the look's grip landmarks
(riders.dev.hands.contact) within their tolerances, preferring the smallest change. It prints the JSON to put
in content/held/looks.json; the loaded content keeps the fitted value until the host restarts or the file reloads.
"""
import argparse
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def live_debug():
    if os.environ.get("RUSTY_LIVE_DEBUG"):
        return os.environ["RUSTY_LIVE_DEBUG"]
    props = open(os.path.join(ROOT, "Directory.Build.props")).read()
    pair = re.search(r"<RustyEnginePackageVersion>(.*)</RustyEnginePackageVersion>", props).group(1)
    return os.path.expanduser(f"~/.cache/rusty-engine/pairs/{pair}/runtime-pack/bin/rusty-live-debug")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("origin")
    p.add_argument("look")
    p.add_argument("--rig")
    p.add_argument("--tune", choices=("hand", "offhand", "nudge"), default="hand")
    p.add_argument("--margin", type=float, default=5.0, help="degrees kept inside each edge of the range")
    p.add_argument("--landmarks", type=float, default=1.0,
                   help="weight per millimetre a grip landmark (riders.dev.hands.contact) lies past its tolerance, "
                        "against a degree past the wrist range; 0 ignores them")
    a = p.parse_args()
    dbg = live_debug()

    def run(command):
        r = subprocess.run([dbg, "--origin", a.origin, "--command", command], capture_output=True, text=True)
        return r.stdout + r.stderr

    held = os.path.join(ROOT, "content", "held")
    looks = {l["id"]: l for l in json.load(open(os.path.join(held, "looks.json")))["looks"]}
    look = looks[a.look]
    action = next(iter(json.load(open(os.path.join(held, "motion.json")))["motions"][look["motion"]]["tracks"]))
    run("engine.time.mode manual")
    if a.rig:
        print(run(f"riders.dev.arms.rig {a.rig}").strip())
    print(run(f"riders.dev.motion {a.look} {action}").splitlines()[0])
    run("riders.dev.motion.at 0")

    nudge = look.get("nudge", {"grip": [0, 0, 0], "turn": [0, 0, 0]})
    start = {"hand": look["grips"].get("hand"), "offhand": look["grips"].get("offHand"), "nudge": nudge["turn"]}[a.tune]
    if start is None:
        raise SystemExit(f"'{a.look}' has no {a.tune} to tune")

    def put(v):
        v = [round(c, 1) for c in v]
        if a.tune == "nudge":
            g = nudge["grip"]
            return run(f"riders.dev.held.nudge {g[0]} {g[1]} {g[2]} {v[0]} {v[1]} {v[2]}")
        return run(f"riders.dev.held.{a.tune} {v[0]} {v[1]} {v[2]}")

    bend = re.compile(r"Flexion = (\S+), Ulnar = (\S+), Roll = (\S+), Clamped = (\S+), Elbow = (\S+) }")
    rng = None

    mark = re.compile(r"([\d.]+) mm from .*\(within ([\d.]+)\)")

    def misses():
        return sum(max(0.0, float(d) - float(t)) for d, t in mark.findall(run("riders.dev.hands.contact")))

    def read():
        nonlocal rng
        run("engine.time.advance 50")
        out = run("riders.dev.arms.wrist")
        if rng is None:
            m = re.search(r"Flexion = (\S+), Extension = (\S+), Radial = (\S+), Ulnar = (\S+), Roll = (\S+),", out)
            rng = [float(m.group(i).rstrip(",")) for i in range(1, 6)]
        hands = [[float(x) for x in m] for m in bend.findall(out)]
        return hands, out

    def excess(h):
        flex, ext, radial, ulnar, roll = (r - a.margin for r in rng)
        f, u, r = h[0], h[1], h[2]
        return max(0, f - flex) + max(0, -f - ext) + max(0, u - ulnar) + max(0, -u - radial) + max(0, abs(r) - roll)

    def cost(v):
        put(v)
        hands, _ = read()
        over = sum(excess(h) for h in hands)
        miss = misses() if a.landmarks else 0.0
        return over + a.landmarks * miss + 0.01 * sum(abs(x - s) for x, s in zip(v, start)), over, hands

    best = list(start)
    c, over, hands = cost(best)
    print("start", best, "over", round(over, 1), [[round(x, 1) for x in h] for h in hands])
    step = 16.0
    while step >= 1.0:
        improved = False
        for i in range(3):
            for d in (-step, step):
                v = list(best)
                v[i] += d
                cv, ov, hv = cost(v)
                if cv < c - 1e-6:
                    best, c, over, hands, improved = v, cv, ov, hv, True
        if not improved:
            step /= 2
    put(best)
    hands, out = read()
    print("fitted", [round(x, 1) for x in best], "over", round(over, 1))
    print("\n".join(out.strip().splitlines()[1:]))
    if a.landmarks:
        print(run("riders.dev.hands.contact").strip())
    key = {"hand": '"grips": {"hand"', "offhand": '"grips": {"offHand"', "nudge": '"nudge": {"turn"'}[a.tune]
    print(f"{key}: {json.dumps([round(x, 1) for x in best])}  (look '{a.look}' in content/held/looks.json)")
    return 0 if over == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
