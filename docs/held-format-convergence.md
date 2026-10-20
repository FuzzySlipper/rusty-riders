# Held format: riders and hotel

> **Upstreamed.** The converged format and its mechanisms now live in the Engine as `Rusty.Engine.FirstPerson`
> (rusty-engine #10188, Engine docs `first-person-held.md`). Records there carry `Vector3` values (a product gives its
> JSON context a `[x, y, z]` converter, as `Content/Vector3Json`) and list their own problems by field. Grip space
> stays riders' fitted look space for now; the hand hold frame is the canonical one. Hotel can adopt it by loading its
> content into those records.

Rusty Hotel (`../rusty-hotel`, `content/combat/{held,stances,held-motion,arms}.json`, `src/Hotel.Game/Combat/`) and
riders (`content/held/`, `src/RustyRiders.Game/Held/`) grew from the same fptester port. Before the first-person arms and
held motion go upstream to the Engine, this records where the two formats agree, where each says more, and where their
conventions conflict, so one format can carry both games' content. Checked against hotel at its current main, 2026-10-20.

## Verdict

One format can carry both. Riders' side is the larger one for motion: hotel's keyed actions are a subset of ours, apart
from two small key fields. Hotel's side is the larger one for the hands on the weapon: per-weapon finger shapes, grip
landmarks, and the contact fitter have no counterpart here. Two conventions conflict: the space grips are given in, and
the frame a hand's hold is given in. A hotel look cannot be copied over as is; it converts mechanically once those two
are settled.

## Same concept, same or renamed field

| hotel | riders |
|---|---|
| `look` | `id` |
| `stance` + `stances{grip,toward,up}` | the same |
| `nudge{grip,turn}` | the same |
| `grips{main,off,hand,offHand}` | the same |
| `poses{position,rotation}`, `tracks`, key `phase`/`at`/`ease`, `settleSeconds`/`settleEase` | the same |
| key `hands:[main,off]` (-1 open, 0 grip, 1 squeezed) | key `grip` / `offGrip` |
| `arms.hands.close` / `.open` (`DigitBend`) | `arms.hands.close` / `.open` |
| `arms.mainHand.rotation` | `arms.hold` |
| `arms.mainHand.palm` | `rigs/*.json` `palm` |
| `arms.model` / `clip`, chains with a digit-name prefix | `rigs/*.json` `model` / `clip`, explicit joint-name arrays |
| `arms.shoulder`, `arms.pole` | the same |

Both games use camera-space metres (+X right, +Y up, +Z back), degrees about X, Y and Z, and named Engine tween eases on
action phases.

## Hotel has, riders lacks

- **Per-weapon hand shapes** (`shape.main` / `shape.off`) and **grip landmarks** (`trigger`, `backstrapTop`, `butt`,
  `foreEnd`) with the contact readout: *ported to riders* (see the proof below; riders' landmarks are in look space).
- **The hand fitter**: `hotel.dev.hands.fit` bakes `shape` by curling each digit until it touches the model's surface.
  It belongs with the upstream authoring tools.
- **Per-look model `axis{tip,up}`**: the model's own tip and up axes. Riders fits each model into a look space
  (longest side = `length`, tip -Z) and turns it with `model.rotation` instead.
- **`muzzle` + `flashSize`**: where a shot's flash appears.
- **Key `visible`**: hides the item for a throw.
- **A per-stance `hand` hold**: used when a look gives none.
- **`offRest`, `offPole`, `reach.length`**: an idle off hand, its own elbow pole, and an out-of-reach check. Riders
  mirrors the main pole, and reaches with a lean instead of a check.

## Riders has, hotel lacks

- **Loops** (`loops.idle/walk/sprint`): idle runs on seconds, walk and sprint on metres walked. Blended by `HeldSway`
  (`sway.json`).
- **Transitions** (`equip`/`unequip`) and the swap sequence.
- **Feedback** (`feedback.json`): damped `struck`/`blocked`/`fired` punches layered over the motion.
- **Per-key `shoulder` lean and `elbow`/`offElbow` poles**, plus the automatic reach lean.
- **Rig profiles** (`rigs/*.json`): a model turn and scale, joint arrays, and digit frames, either native or measured from
  the knuckles. They put hand holds in a canonical hand frame, so one look fits any arm mesh.
- **Off-hand looks**: the off hand holds its own look, mirrored. Hotel's off hand only ever supports the main weapon.
- **Camera bob** (`player/bob.json`), outside the held format proper.
- **The FBX clip baker** and its recipe (`scripts/bake-held-motion.py`, `art/held-motion/bakes.json`).

## Proof: the coach gun in riders

Riders now carries hotel's per-look `shape`, grip `landmarks` and an Engine-measured contact readout
(`riders.dev.hands.contact`), and draws hotel's coach gun with both hands on all three rig profiles (see
[direction.md](direction.md#hand-shapes-grip-landmarks-and-the-coach-gun) for the conversion and the measures). The
port was mechanical: grips minus the model's bounds centre, and holds multiplied by the inverse of the hotel arms'
measured hand frame. The fitter, `axis`, `muzzle`, key `visible`, `offRest` and `reach` are still hotel-only.

## Conflicts to settle

1. **Grip space.** Hotel grips are in the model's raw GLB space × `scale`, with a per-look axis. Riders grips are in its
   fitted look space. Recommend hotel's: the landmarks and the fitter sample the model's surface in its own space. The
   fitted space can be derived from it (`length` becomes a scale, `rotation` becomes an axis). Riders' looks would then
   be converted once. *Still open.* The coach-gun port kept riders' fitted space and converted hotel's numbers into
   it, which took one subtraction per point, so the choice can wait for the upstream proposal.
2. **Hand hold frame.** Hotel's `hand` is a rotation of the rig's own hand joint in the item's axis frame, so it holds
   only for the arm mesh it was tuned on. Riders' `hand` is in the canonical hand frame: +Y along the fingers, +Z out of
   the back, +X toward the pinky on the right hand. Recommend riders'. It is what makes arm meshes swappable, and the
   hotel arms become one more rig profile, whose measured frame converts each hotel `hand` value once. *Settled for the proof:* the coach gun's holds were
   converted this way and land within a few millimetres of hotel's own result on the generated arms (the same
   generator as hotel's).
3. **Hand closure.** Both use -1…1 around the grip clip. Riders keeps one scalar per hand; hotel keeps two per key.
   Renaming to `hands:[main,off]` or keeping `grip`/`offGrip` is cosmetic, so pick one.
4. **Content density.** Hotel's motions are sparse and hand-authored; riders' are baked. Both are valid uses of one
   key format. *Settled in riders:* the baker keys every kind by tolerance against the curve the game draws (straight
   blends for actions and transitions, the closed Catmull-Rom for loops), over every channel a key carries (grip,
   turn, lean, elbows, closures), with per-kind millimetre and degree tolerances in the recipe, and writes number lists
   on one line. `motion.json` went from about 13k lines to 5.2k at about the same key count (roughly 700 keys,
   from 780): the earlier bakes were already reduced except the loops, whose sprints had been under-keyed. Upstream,
   the baker takes the same tolerances.
5. **Shapes are relative to each rig's base hand.** Hotel's shape is added over one grip clip. In riders it was
   added over each rig's `grip` bend, which ported the values but not their meaning: the same coach-gun and flintlock
   shapes put the index pad 18–20 mm from the trigger on the generated arms and 100–118 mm from it on the painted arms,
   whose gun clip already points the index forward. *Settled:* a shape is added over one **canonical holding pose**,
   `arms.json` `hands.pose`: knuckle angles per finger joint and [curl, swing] per thumb joint, read in the hand's
   canonical frame. That pose is the generated (hotel's) grip clip as drawn, so hotel's shapes keep their values. Each
   rig's `grip` is now a per-finger, per-hand shape that brings its own clip there. It is calibrated with
   `riders.dev.hands.knuckles`, which prints the clip's angles and the grip that closes the gap. Hands of other
   proportions keep a measured index trim, so the pad lands where the reference hand's does. The index pad then sits
   within about 20 mm of the trigger on every rig for both guns (see
   [direction.md](direction.md#hand-shapes-grip-landmarks-and-the-coach-gun)). Shapes as absolute curls from an open
   hand were rejected because they would have meant rewriting every hotel shape, and an open hand is no closer to
   rig-neutral: each rig's clip still has to be measured against it. Upstream, the pose belongs with the Engine's
   arms format, each rig profile carries its grip, and the knuckle readout is the calibration tool.
6. **Required fields.** Hotel requires `shape` on every look; riders requires `box` (the fallback drawn without a model).
   Upstream, both should be optional, each with a stated fallback.

## Upstream shape this suggests

- **Engine:** the rig profile and two-bone reach, hand shapes over a grip clip, motion keys, loops and transitions as tween
  timelines, feedback layers, sway blending, landmarks and the contact fitter, and the baker as a tool.
- **Each game:** looks, stances, motion content, item wiring, dual-wield and two-handed policy, and the camera bob tuning.
- **Proof before proposing:** port one hotel look (the coach gun: two-handed, with shapes and landmarks) onto the
  converged format, and draw it in riders with each rig profile.
