# Held format: riders and hotel

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

- **Per-weapon hand shapes** (`shape.main` / `shape.off`). Each finger has three joint curls, and each thumb joint has a
  curl and a swing, added over the grip clip. Riders has one `grip` bend per rig for every weapon. The pistol needs a
  trigger finger, so this is the most important thing missing here.
- **Grip landmarks** (`trigger`, `backstrapTop`, `butt`, `foreEnd`, model space). `hotel.dev.hands.contact` measures the
  hands against them with tolerances, and `hotel.dev.hands.fit` bakes `shape` by curling each digit until it touches the
  model's surface. Landmarks and the fitter belong with the upstream authoring tools.
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

## Conflicts to settle

1. **Grip space.** Hotel grips are in the model's raw GLB space × `scale`, with a per-look axis. Riders grips are in its
   fitted look space. Recommend hotel's: the landmarks and the fitter sample the model's surface in its own space. The
   fitted space can be derived from it (`length` becomes a scale, `rotation` becomes an axis). Riders' looks would then
   be converted once.
2. **Hand hold frame.** Hotel's `hand` is a rotation of the rig's own hand joint in the item's axis frame, so it holds
   only for the arm mesh it was tuned on. Riders' `hand` is in the canonical hand frame: +Y along the fingers, +Z out of
   the back, +X toward the pinky on the right hand. Recommend riders'. It is what makes arm meshes swappable, and the
   hotel arms become one more rig profile, whose measured frame converts each hotel `hand` value once.
3. **Hand closure.** Both use -1…1 around the grip clip. Riders keeps one scalar per hand; hotel keeps two per key.
   Renaming to `hands:[main,off]` or keeping `grip`/`offGrip` is cosmetic, so pick one.
4. **Content density.** Riders' baked motions are densely keyed (`motion.json` is about 13k lines of linear keys), while
   hotel's are sparse and hand-authored. Both are valid uses of one key format. Upstream, the baker should simplify
   with a tolerance (as transitions already do), so baked files stay reviewable.
5. **Required fields.** Hotel requires `shape` on every look; riders requires `box` (the fallback drawn without a model).
   Upstream, both should be optional, each with a stated fallback.

## Upstream shape this suggests

- **Engine:** the rig profile and two-bone reach, hand shapes over a grip clip, motion keys, loops and transitions as tween
  timelines, feedback layers, sway blending, landmarks and the contact fitter, and the baker as a tool.
- **Each game:** looks, stances, motion content, item wiring, dual-wield and two-handed policy, and the camera bob tuning.
- **Proof before proposing:** port one hotel look (the coach gun: two-handed, with shapes and landmarks) onto the
  converged format, and draw it in riders with each rig profile.
