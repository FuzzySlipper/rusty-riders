# Tuning first-person weapons

Run `rusty dev --port 8787 --live-debug` and press F2 in play to dock the Engine developer shell: two views of the
hands' grips, the first-person workbench and the console. The workbench picks a weapon look, an action and a hand,
steps or plays the action's keys (in real time, though the world holds), and tunes the look's model fit, its stance,
nudge and grip, each key's timing, curve, hand closure and pose, and the arms' shoulder, elbow and hold, with
sliders, the drawing rig's model turn, scale, palm and base grip, and the feedback punches a hand feels as its action
lands (`content/held/feedback.json`: struck, blocked, fired; `riders.dev.held.feel <kind> <main|off>` tries one), and
the sway between and under actions (`content/held/sway.json`, and each motion's loops). `riders.arms` reports the rig, how far each wrist
falls short of its grip, the arms' lengths and the measured hand frames. Save writes the changed values into `content/held/`.
Editing those files by hand also reloads them into the running game. The same in the console: `riders.dev.motion
<look> <action>`, then `.key`, `.next`, `.play <speed>`, `.at <seconds>`, `.pose`, `.hand`, `riders.dev.held.nudge`,
`riders.dev.held.fit`, `riders.dev.stance.set`, `riders.dev.arms.hold`, `riders.dev.arms.reach`, `riders.dev.arms.grip` and
`riders.dev.arms.rig <generated|painted|leather|shared>`, which swaps the arms model live. `riders.dev.hands.knuckles` reads each hand's knuckle angles against the canonical holding pose (calibrating a rig's `grip`), and `riders.dev.hands.contact` reports how the drawn hands
meet the main look's grip landmarks (`trigger`, `fore-end`, ...), as the Engine measured them.
`riders.dev.held.hand` and `riders.dev.held.offhand` set the shown look's own holds (`grips.hand`, `grips.offHand`).

Each wrist bends only within its rig's range (the Engine's `ArmRig.Wrist`: 70° flexion, 60° extension, 20° radial,
35° ulnar, 90° roll by default); a hold that asks past it is drawn at the range's edge. `riders.dev.arms.wrist` shows
each wrist's asked bend and how far it was turned back (`Clamped`, 0 in range). A look can also be held by a grip
solved by contact around its model (asset-pipeline `tools/fps-arms/grip.py`, the Engine's `SolvedGrip`): its
`solvedGrips` names a file under `content/held/grips/` per rig, which places the look in the main hand in place of
`grips.hand` on that rig only (a solve fits the hand it was solved on). A grip file listed in the project's
`RustyEngineLiveContent` reloads in place, so a new solve shows at once. The maul's is solved on `shared`:

```sh
blender -b --factory-startup --python ../asset-pipeline/tools/fps-arms/grip.py -- \
  --params ../asset-pipeline/tools/fps-arms/sets/riders.json --engine-pose content/held/arms.json \
  --object content/art/parts/Art/Models/Weapons/Prefabs/BattleHammer/Battle_hammer-b2842784.glb \
  --fit '{"length": 0.75, "rotation": [-90, 0, 0]}' --grip 0,0,0.15 --roll 90 --out content/held/grips/maul-shared.json
```

`--fit` and `--grip` are the look's `model` and `grips.main`; `--roll 90` turns the look's up side (the maul's face)
the way the knuckles point, which keeps the heavy stance's wrist in range. `riders.dev.hands.haft` lists each main
digit joint's distance from the main grip's line, so a joint nearer than the handle's radius is inside it.

Two scripts drive a running host (`rusty dev start --live-debug ... --instance check`):
- `scripts/held-check.sh ORIGIN OUT [--rig RIG] [LOOK...]` steps every look (or those named) at rest in held time and
  writes the arms, wrist, knuckle, haft and landmark readouts and a 1920x1080 frame of each, with one wrist line per
  look.
- `scripts/held-wrist-fit.py ORIGIN LOOK [--rig RIG[,RIG]] --tune hand|offhand|nudge|nudge-grip` searches that value
  to bring both wrists inside the range (less `--margin`) with the landmarks kept, and prints the JSON for
  `content/held/looks.json`. Several rigs are judged together, since a hold applies to all of them. It reads the arms
  settled, as `held-check.sh` does.

A hold that puts the fingers on their landmarks may ask more of the wrist than its range, and the clamp then pulls the
fingers off again, so fitting the hold alone stalls. Separate the two: widen the rig's `wrist` range for a moment
(each angle 179) and fit the hold for the landmarks; restore the range, then turn and move the whole look (`nudge`)
so the wrist comes back in range, since the nudge carries the hands with the look. The coach gun was fitted this way;
its nudge turns it 16° and seats it 6 cm right and lower, which keeps it aimed down the view.

A look's `grips.main` and `landmarks` are points on its fitted model (look space: centred on its bounds, length along
-Z). Measure them on the model, not by eye in the game: a grip point off the handle shows as a hand held beside the
weapon that a `nudge` then hides. `butt` is where the little finger's middle joint wraps the front of the handle, not
the butt cap.

A rig's `palm` (the grip's offset from its hand joint, in the hand's canonical frame) is calibrated against `shared`'s
from where its knuckles sit: the canonical frame starts at the hand joint with +Y toward the middle knuckle, so a rig
whose knuckles sit further out puts its fingers further along the weapon unless its palm moves out with them. Its palm
is `shared`'s plus the difference between the two rigs' knuckle centroids in that frame (measured in Blender from the
GLB's bone heads, times the rig's `scale`): `painted` +23 mm and `leather` +26 mm along the fingers; `generated` matches
`shared`. A hold then lands the hand alike on every rig, apart from the hands' widths (`painted`'s knuckles span 86 mm
against `shared`'s 59), so holds were fitted on `shared,painted` together until Riders settled on the painted arms alone (2026-11-01).

The arms are drawn by a rig profile (`content/held/rigs/`, chosen in `content/held/arms.json`). Riders draws `painted`,
the old game's hand-painted gloves, built from `old-game/Assets/MoreArt` into the ignored `content/art/fp-arms/` by
`scripts/import-fp-arms.sh` (Blender 5 and `../asset-pipeline`); calibrate and fit on it alone. `leather`, `generated`
(kept in git, from asset-pipeline's `tools/fps-arms`, `art/arms/provenance.json`), `shared` (asset-pipeline
`tools/fps-arms/sets/riders.json`, `art/arms/shared-provenance.json`) and `psx` stay switchable with
`riders.dev.arms.rig` for comparison, but are not maintained. Without the import the game falls back to the generated
arms. `scripts/import-hotel-held.sh` copies rusty-hotel's coach gun into the ignored `content/art/hotel/`; without it
the coach gun is drawn as its box.

Clip packs that make a look clip-led on the painted arms are made by `scripts/arm-clips` (its README). How the held
motion is baked from the old clips, and the design behind it, are the Den document `technical/held-weapons` (project
`rusty-riders`) and the den-wiki page `wiki/rift-riders/first-person-presentation`.
