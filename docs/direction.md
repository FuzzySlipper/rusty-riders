# Direction and what the old art is worth

Decisions and observations that shape the work but aren't in code. Den tasks hold the open work
(rusty-riders #9373 for the rebuild exploration).

## Game direction

- **Genre:** Rusty Riders reworks the unfinished Unity game *Rift Riders* (`old-game/`). It is not a port: the old game was a
  slower, grid-constrained party game, and this one is a solo character, more action-oriented, moving freely.
  Level generation may stay grid-based (the old chunks and tiles fit that way); the player is not held to the grid.
- **Camera:** first person, for now. A third-person camera would suit the art (it was made for an overhead
  camera) but brings character animation work that is deliberately avoided at this stage.
- **Spaces:** mildly open and stylized rather than realistically modelled: aesthetic clutter is fine, and spaces
  only need to work for a free-roaming character, not to look like real architecture.

## Scale of the old level kit

Measured from `content/levels/` (cell 15 m) and the converted tiles:

| Space | Size |
| --- | --- |
| Room (one chunk) | 45–75 m across, median 10 cells |
| Corridor tile | 15 m cell, about 5 m walkable path |
| Ceiling (LavaMap) | about 10 m |

At a 5 m/s walk a room takes 9–15 s to cross (4–6 s sprinting at 12 m/s): arena-sized for a handful of
enemies. If it ever feels too big, tune walk speed and field of view first. The art is stylized enough that
scaling the kit to 0.75–0.8 would probably hold up; `generator.json`'s cell size is one value, but the tile
art and wall collision would need the same scale.

## What the old art is worth

The valuable part is the **stylized textures** (toon-painted rock, lava, brick, palace inlay, cybertube
panels). The meshes were made for an isometric camera and are nothing special. Harvesting textures onto new
geometry is preferred over preserving meshes. Cutting meshes up, feeding them to generators, or converting
them to voxels are all acceptable.

First-person assessment from one generated level per tileset (2026-10-03, captures cited in Den #9373):

| Tileset | First-person verdict |
| --- | --- |
| UndergroundPalace | Strongest: arches, columns and inlaid floors hold up at eye level |
| StoneRoad, LavaMap | Solid as converted meshes; palettes and lava emission work |
| CaveMap | Walls fine; floor texture repeats obviously at eye level |
| Cybertubes | Walls fine; floor repeats; no working palette (its palette names materials that don't exist) |
| ForestMap | Reads as a clearing at a distance; backdrops are flat painted cards (`distantview*`) that look like a film backlot up close; a few tile models fail Engine admission |

Not everything has to be usable. The rebuild idea (Den #9373), modelled loosely on rusty-doom's recipe and
room-study approach, has three parts:
- keep the level data as the layout;
- generate shells (implicit surfaces or voxels) textured with harvested textures and palettes;
- keep the good meshes as props.

Caves and Forest are the first trial candidates.

## Generated shells: first trial

CaveMap was rebuilt as generated shells: LevelLayout 3, seed 1, 37 tiles, Engine pair `0.1.0-dev.1b37097a0fa4`,
local RX 9070 XT, on 2026-10-04. It was compared with the stamped tiles from the same spawn. How it is built is in
[levels.md](levels.md#generated-shells).

**Cost:**

| Extraction | Spacing | Result | Time |
| --- | --- | --- | --- |
| Implicit (whole level) | 0.5 m | Did not finish | Over the runtime's 30 s product-load limit |
| Implicit (whole level) | 1 m | 84.7k triangles | 23.4 s of meshing |
| Sampled | 0.5 m | 187.6k triangles in 60 seamless blocks | 6–8.3 s to build, of which meshing is 0.3 s |

- Implicit extraction pays for the cube grown to the level's longest side.
- Most of the sampled time is evaluating the blended, wave-displaced field.
- Sampled is the better fit for whole levels.
- Either way the build is a load-time hitch, not something to redo per frame.

**What it gains:**
- Collision is the visible surface: walls stop the walker where they are drawn (at the old grid's lines here).
- Ceilings are closed, and there are no wall backs or seams between tiles.
- Outlines and heights come from content values rather than authored meshes. Corridors already get lower
  ceilings than rooms.

**What it loses with the current texture:**
- The Cave's only texture (`wall01`, shared by its floor and wall materials) is a structured trim sheet, not a
  stochastic rock.
- On the old meshes its UVs bend with the forms. Projected triplanar onto a flat floor, its grid is plain, so
  floor repetition is *more* obvious than in the stamped tiles.
- The walls hold up better, but planar blending smears the pattern where faces turn.

So generated shells only pay off with textures that tolerate projection: stochastic surfaces (rock, dirt,
moss) or larger-scale patterns. Useful next steps:
- Harvest unused source textures from `old-game/`, such as CaveMap's `ground01_alb` rock, which no converted
  material uses (that needs a script or an asset-pipeline export mode, not the GLB route).
- Try tilesets whose textures are already stochastic.
- Add authored height changes per room.

**Answers to the questions in #9373:**
- **Surfaces.** Use Engine implicit recipes for authoring and sampled-volume blocks for extraction.
  - Triplanar materials and normal maps work on retained meshes.
  - Triplanar always repeats once per mesh unit, with no texture scale on `MaterialRequest`, so the shells are
    extracted in texture units and placed scaled. That gives one scale for the whole shell, not one per material.
- **Textures.** Reading the converted GLBs' embedded images needs no new pipeline. It keeps the import's
  texture cap and the Unity material names palettes use. It cannot reach textures no converted material uses.
- **Palettes.** Palettes still apply unchanged. Harvested materials keep their Unity material identity, so the
  palette's `_Color` entries tint them as they tint tile art.

**Swept meshes** (`sweeps`) are the same open ground built as ordinary UV-mapped meshes (see levels.md):
- **Cost:** 35k triangles in 0.06 s, against 6–8 s for the sampled shells.
- **Walls:** the clearest of the three builds, because the trim sheet now runs continuously along them.
- **Floor:** unchanged; a planar trim sheet still shows its grid.
- **Character:** the result reads as built architecture more than as a cave; bulge and rounding only soften it.

None of the three builds is grid-free: a projected texture still repeats on any flat floor. That points at the
texture, through stochastic textures or blending, rather than the geometry.

**Generated floor textures** fix the floor. Five candidates were made with `wall01` as the style reference
where the route allows one, made to tile, and compared on the swept Cave floor. Every one removes the grid.

| Route | Time per image | Result |
| --- | --- | --- |
| GPT image (`codex-image-gen` skill, reference attached) | ~5 min | Best style match and instruction following. `gpt-cells` reads as the walls' world; `gpt-rock` is a clean natural floor. |
| Qwen-Image 2.1 edit (5090 ComfyUI, reference as `image_1`) | 6–12 s | Dense, even, irregular coverage that tiles easily. Lighter outlines than the reference; rock is bland at distance. |
| Z-Image Turbo (5090 ComfyUI, text only) | 3 s | Generic cartoon pebbles. "Tileable" in the prompt repeats the pattern inside the image. Big shapes at the edges defeat the seam repaint. |

Notes on the process:
- Making a texture tile works by rolling it half a tile and repainting the seam cross.
- Qwen-Image 2.1 is the better seam painter, because it sees the rolled image as its reference. Z-Image painted
  off-style stripes along the seams.
- `gpt-cells` at 5 m is the current pick (V cycles all five). Blending two floor textures in a product shader is
  an optional next step: textures alone already remove the grid.
- Qwen-Image 2.1 was installed on the 5090 for this through ComfyUI-Manager's API
  (`Comfy-Org/Qwen-Image-2.1`: int8 model, Qwen3-VL 8B encoder, VAE, and the t2i/i2i prompt enhancers).

**Lava and Forest** got generated builds too (2026-10-08, `content/levels/shells/LavaMap.json` and `ForestMap.json`,
copied from the Cave's recipe with each tileset's floor, wall and prop names). Their floors are the user's favourites
from the v4 LoRA review (`content/art/textures/lava/`, `forest/`; see texture-style.md):
- They are colour textures (`"colour": true`), drawn without the material's palette tint and glow. The Lava ground
  material's red tint and glow turned them into red mush; greyscale textures keep the tint.
- In the swept builds they replace a red wash (Lava) and a noisy ground texture (Forest), and read as the house style
  at eye level. Forest `ink-c` is the strongest up close.
- The walls are still the old harvested textures: Lava's is a bright red wash and Forest's a busy camouflage. They
  need the same treatment next. Forest under a closed ceiling also reads as a cave rather than a clearing.

**Walls** (2026-10-11): each recipe's optional `wallTexture` replaces the wall material's own texture on walls and
ceilings. The Cave, Lava and Forest walls are seamless house-style textures picked from a wall review in which the
user kept all 18. Those without a strong diagonal grain were chosen:
- **Lava:** vertical basalt with glowing seams, in place of the red wash.
- **Forest:** horizontal mossy ledges, in place of the camouflage noise.
- **Cave:** folded strata.

In the swept builds the walls now match the floors' style. The Cave also gained two seamless slab floors
(`ink-a`, `ink-b`, first in V's cycle; not yet checked in game). Implicit and sampled shells draw the wall texture
triplanar at the recipe's `textureMetres`.

Captures: crew-playtest sessions `af817b80-a918-4cbc-a914-18485bb68d9d` (tiles, shells),
`dcf08c11-e5c9-4634-a624-a524ce53cf72` (floor textures) and
`94395057-13a6-4930-b617-2d542f9575b5` (sweeps), retained 14 days.

## Converted weapons and enemies

`scripts/import-old-art.sh` also converts the starter weapons (Den #9645) and one animated creature per
starter enemy kind (Den #9646). Each weapon is an old item prefab, so it gets a placement file. Each enemy is
its whole animated model, `models/<model>.glb`, plus its NPC prefab's placement file. That file holds only
static per-mesh parts and the old prefab scale. Picked by look from rendered previews (2026-10-08):

| Use | Old source | Why |
| --- | --- | --- |
| rift-blade | `GameData/Items/Weapons/Models/Prefabs/1H_sword_C_ShortSword` | Wavy blade, red hilt |
| maul | `.../Prefabs/BattleHammer_Maul` | Two-handed hammer, textured |
| laser-flintlock | `.../Prefabs/Flintlock_Flintlock` | The only textured pistol; the sci-fi guns have no Unity materials |
| ember-wand | `Art/Models/Weapons/Mandragora` | Twisted staff with a red crystal |
| frost-sigil | `Art/Models/Weapons/Ravenwood` | Dark staff with a blue crystal; its `crystal` row alone can be the sigil |
| ripper | `Art/Models/Enemies/KOTG HARPY` | Fast clawed flyer |
| gunner | `Art/Models/Enemies/HC Weresquid` | Staff caster with six `castSpell` clips |
| brute | `Art/Models/Enemies/OH Demon Brute` | Heavy, with a big weapon |

Most enemy clips (idle, getHit, death, in-place walk and run) are in `Art/Animations/Character`, not beside
the model. unity-import merges `Model@Clip` files only from the model's own folder. So the script converts from
a hard-linked copy of `Assets` with those clips linked beside each model, which Unity bound through the
creature's animator. It leaves out the root-motion (`_RM`) duplicates.

Other creatures that were tried:
- The FBX 2013 re-saves suggested first did not work out.
  - `OH HobgoblinNew` matches the Character Hobgoblin clips, but its own one-key take converts to a
    zero-length clip, which the Engine refuses (`InvalidClipDuration`).
  - `OH Hobgoblin` is admitted, but its `plate_armor` skin deforms into a blob.
  - `OH Watcher` is refused (its clip timestamps collapse at microsecond quantization).
  - `HC EVIL WATCHER` has the zero-length take, and its Character clips are FBX 6 that have not been re-saved.
  - The `KOTG ORC` and `OH SKELETON KNIGHT` Character clips are FBX 6 that have not been re-saved either.
- `OH Succubus` with every clip exceeds the Engine's 4,096 animation channels.

## First-person arms

The old arms turned up in `old-game/Assets/MoreArt` (2026-10-19), with a lot of first-person animation:
- `FpMelee/MedievalFPArms`: the arms pack the old `ArmsCopy.prefab` used (its mesh GUIDs match): five arm sets
  (male and female leather armour, cloth, chainmail) on the UE4 mannequin skeleton, with 4K maps and a few unarmed
  clips. Built as `leather.glb` with its unarmed idle as the clip the Engine needs.
- `FirstPersonArms`: a gun-hands pack on a 3ds Max CAT rig, one ASCII FBX per weapon (arms and weapon together) with
  its clips, plus the old game's own hand-painted texture `Hand_Recolor.png`, the arms in the old screenshots. Built
  as `painted.glb` from `Hands_Gun_03_Mesh` (both arms; the revolver is removed). Blender does not read ASCII FBX, so
  unity-import (Assimp) converts it first.
- `FpMelee/FPP_*`: melee clips (sword and shield, longsword, dagger, halberd, spear) on the same mannequin skeleton,
  with `weapon_l`/`weapon_r` bones, attack start/loop/end, blocks, equips, hits and locomotion. They are not played as
  skeletal clips; `scripts/bake-held-motion.py` bakes one into held motion (below).
- `Animations/`: third-person character clips (Mixamo and others), not first person.

`scripts/import-fp-arms.sh` builds both into the ignored `content/old-art/fp-arms/`. Each is a rig profile in
`content/held/rigs/`: the model, the clip played under the IK, its turn and scale, each arm's joints, and per-rig palm
and base grip. Hand frames and digit axes are measured from the drawn model, so the arms' hold and the motion keys'
closure (`content/held/arms.json`) serve every rig unchanged. The packs are third-party store assets the old game
used; confirm their licences before any public release.

### Baking old clips into held motion

A held look's motion is the weapon alone in camera space, so an old clip becomes held motion by following its weapon
from the eye: `scripts/bake-held-motion.py` (Blender) samples the weapon's grip and turn every frame, takes them from
the rest pose, splits the frames into windup (to the strike, the frame the grip moves fastest), commit (until it slows)
and recovery, keeps only the keys a straight blend between them would miss by more than 2 cm or 8 degrees, and writes
them as poses and a track into `content/held/motion.json`, each key timed by its phase so it lands on the same beat at
the action's own speed. Beside each key it bakes what the source arm does that our IK cannot infer: the hand's closure
(the source fingers' curl), the body's lean (`shoulder`) and the elbow's bend (`elbow`). The arms are not baked as a
skeleton: ours reach the grip by IK on any rig.

Every bake is an entry in `art/held-motion/bakes.json`, and `scripts/bake-held-motions.sh [NAME]` runs them all (or
one), converting the CAT clips with unity-import first. An entry names:
- `clip` and `rest` (the set's idle; without one, the clip's first baked frame), relative to `old-game/Assets`, and
  optionally `from` and `to` frames to bake part of a clip;
- `source`: `mannequin` (FpMelee/FPP_*, FBX, a camera bone) or `cat` (the Hands pack, no camera bone: the eye is a
  fixed point over its shoulders);
- `weapon`: the bone the weapon rides (the mannequin's `weapon_r`/`weapon_l` by default, the Hands pack's gun body such
  as `B_Gun_03_Body`), or `palm` for a casting hand, which rides the look in the palm with the hand's own turn (a
  weapon bone is not always at its hand: the dagger set's `weapon_l` hangs half a metre away, beside the other hand);
- `hand`: `l` bakes a left hand, mirrored into the main hand's space;
- `motion`, `action` and `look`; `seat` seats the look where the source weapon rests and holds it as the source hand
  does (below); `twoHanded` bakes the other hand on the same weapon too (a two-handed source such as the FPP longsword):
  at rest its palm's place on the seated look becomes `grips.off` (look space) and its canonical frame from the look's
  `grips.offHand`, and each key gains its closure (`offGrip`) and elbow's bend (`offElbow`); `hold` (`fist` or `pistol`) says how; `strike` and `commitEnd` (a frame, or a fraction below 1) set the
  phases where the grip's speed does not (a reload); `noClosure` keeps the hand closed through an open-handed gesture
  (a wand would drop).

Two things keep the hands on a baked weapon:
- **The body leans.** The source character lunges: its shoulder moves 17 cm toward the slash by the strike, so its
  nearly straight arm reaches a grip 0.64 m ahead. Each key carries that move as `shoulder` (camera space, the main
  hand's; mirrored for the off hand), and the arms move with it. Beyond that, `ArmsView` leans the body in by itself
  when a grip still lies past an arm's length (at most 0.3 m), so a motion holds on rigs with shorter arms.
  `riders.arms` reports both leans and the wrists' shortfall.
- **The look rests where the source rests, held as the source holds it.** Turns are baked from rest, so they follow the
  source's path only if the look starts where the source weapon does. `seat` sets the look's nudge to the source's rest
  and its `grips.hand` to the source hand's hold on the weapon (the hand's canonical frame from the look's, measured
  from the source's knuckles as `ArmsView` measures rigs). Which weapon-bone axes are the look's length and up side
  follows from the hold: a `fist`'s blade leaves past the index finger with its edges along the fingers; a `pistol`'s
  barrel runs along the hand with its sights toward the index finger (`blade` and `edge` name axes outright). The axis
  that travels furthest in a swing is not the blade (on the FPP set it runs along the fingers); taking it turned the
  sword a quarter turn in the hand and folded the wrist back.

The current bakes, each compared against its source rendered from its eye at the same moments (a stick drawn on the
baked weapon axis), with no wrist shortfall over any of them:

| Look and action | Source | Notes |
| --- | --- | --- |
| rift-blade `blade-slash` | `FPP_sns_Attack_RD` (motion `sns`) | 30 keys; rests blade-up as the sword-and-shield idle |
| maul `maul-smash` | `FPP_Longs_Attack_D` (`longsword`), `twoHanded` | Both hands on the handle, as the longsword is held: raise overhead, low strike, return |
| ember-wand `ember-bolt` | `FPP_Dag_Magic`, left hand, `palm` (`dagger-magic`) | The dagger set's casting hand; the staff lies across the view |
| frost-sigil `frost-nova` | `FPP_Longs_Magic`, left hand, `palm` (`longsword-magic`) | The off hand leaves the hilt to cast; `FPP_sns_Magic1` starts and ends out of view, so its sigil would be hidden at rest |
| laser-flintlock `flintlock-shot`, `flintlock-reload` | Hands pack `Hands_Gun_03_Shot`, `_Reload` (`revolver`) | The painted arms' own revolver set; the reload's strike at 80 % |

The maul's main grip sits 0.15 m behind its centre so both hands fit on its handle: on the longsword the off hand holds
15 cm below the main hand, toward the pommel, and with the main grip at 0.30 the off grip fell past the handle's end.

A source's 1 to 2 s clip is squeezed into the action's own timing (the slash's 1.5 s recovery into 0.25 s). The keys
are ordinary held motion: step, retime and edit them in the workbench. A rebake that gives a look another motion, or
adds or removes a motion, is a structural change: restart the game to see it (an edited key or pose reloads in place).
The heavy attacks' `_Start`, `_Loop` and `_End` parts are separate clips, to bake one at a time or join first.

### Sway and swapping

A motion may also say how its looks sway and how they are drawn and put away, both baked from the same sets' clips:
- **Loops** (`loops.idle`, `.walk`, `.sprint`): offsets from rest around one cycle, played as a smooth closed curve.
  The standing loop runs on time (seconds); walking and sprinting run on distance walked (metres), so the bob keeps step
  at any pace: a baked loop's cycle is its clip's seconds at our walking or sprinting speed, keeping the source's step
  cadence. `HeldSway` blends the three by speed (eased over `content/held/sway.json` `blendSeconds`), fades walking out
  in the air, and keeps `acting` of it under a hand's own action. The sway is part of where the look is published, so
  actions, punches and the arms' reach compose over it; it runs in real time (the rider breathes while the world holds).
- **Transitions** (`equip`, `unequip`): seconds and keys through the motion's poses, in real time. Swapping what a hand
  holds puts the old look away (its last key holds a moment past the end), shows the new look hidden for an update, then
  draws it from its first key to rest. A motion without one swaps at once; an action begun mid-swap shows its weapon at
  once.

Recipe entries with `kind: loop` (`state`, and `speed` m/s for walking and sprinting), `equip` or `unequip` (`pace` to
quicken the source's) bake them: every set has its Idle, Walk, Sprint (the revolver's Run), Equip (Get) and Unequip
(Hide) baked, the melee swaps at about half the source's two seconds. `riders.arms` reports each hand's sway and where
it is in a swap; the workbench's Sway group tunes the blend, what stays under an action, and each loop's size and cycle.

The camera bobs too (`Player/HeadBob`, `content/player/bob.json`): a step every `stepMetres` walked, the eye dropping
`rise` at each footfall, swaying `sway` over the planted foot and rolling `rollDegrees` with it, walking and sprinting
gaits blended by speed and faded in the air, and a dip on landing sized by the fall speed past `softest`. It is camera
presentation only: actions aim from the steady eye, and the held weapons ride the camera, so they bob with the view and
sway on top. `riders.player.bob` reads it.

### Feedback over baked motion

A baked motion is a tween timeline on the weapon's object, so feedback composes with it rather than replacing it. As a
player's action lands, the acting hand feels it (`content/held/feedback.json`): a blow that strikes a body (`struck`),
one stopped by a surface (`blocked`), a shot or cast fired (`fired`). Each is a damped Engine punch started as a layer
on that hand's object: a move (camera space, mirrored for the off hand) and a turn, swinging back `frequency` times over
`seconds`. The arms aim at the weapon as drawn, so `HeldView` samples the motion and every running layer together; a
punch never pulls the hand off the hilt. A punch starts after the hand's motion for that update (an action can start
and land in one update, and a motion's replacing start would end it). The two hands' motions and punches are
independent; only one action runs at a time, a combat rule, not an animation one.

### Hand shapes, grip landmarks and the coach gun

A look may shape each hand's digits (`shape.main`, and `shape.off` on a look held in both hands): three curls per
finger and [curl, swing] per thumb joint, added over the drawing rig's `grip` bend through that rig's digit frames, as
rusty-hotel's `HeldShape` does over its grip clip. Opening blends the shape toward the open bend; squeezing adds to it.
A look may also name grip `landmarks` in look space (`trigger`, `backstrapTop`, `butt`, `foreEnd`). The main hand's look
publishes them as Engine landmark pairs (overlay set `held.landmarks`) against the drawn hands, which are named once per
rig as an Engine `HandRig`. `riders.dev.hands.contact` reads the Engine's measure. The tolerances and the hand points
(pad, web, palm) are `content/held/arms.json` `landmarks`, copied from hotel.

rusty-hotel's coach gun is ported as the proof that one format carries both games' looks (`coach-gun` in
`looks.json`, item `coach-gun`, `riders.dev.give coach-gun 1`, then 3). The model is copied into the ignored
`content/old-art/hotel/` by `scripts/import-hotel-held.sh`. The conversion was mechanical:

- **Grips and landmarks:** hotel's raw model space minus the model's bounds centre (0, 0.00993, -0.14). The model
  needs no turn, and its 0.7 m length is hotel's scale 1, so riders' fitted look space keeps its size. Riders' look
  space stays; whether it should become hotel's raw space is still open (convergence doc, conflict 1).
- **Holds:** hotel's `hand`/`offHand` turn its own hand joint, so each is multiplied by the inverse of the hotel arms'
  hand frame, measured from its knuckles as riders measures a rig. That gives main [0, 82, -132] → [0.8, 81.4, -126.9]
  and off [90, 90, 0] → [89, 5.1, -90] (the same turn near pitch 90).
- **Shapes:** taken as written. Motion: hotel's `shot` poses and its `coach-gun-blast` track, with `hands` as
  `grip`/`offGrip`, in riders' `longarm` motion.
- **Stance:** hotel's `longarm` puts the gun below riders' view, so riders' `longarm` stance is higher. A stance is
  each game's own.

In play on 2026-10-20 (`docs/images/held-coach-gun-rigs.jpg`, world frames without the HUD; coach gun above,
flintlock below), both hands hold the coach gun on all three rigs, with no wrist shortfall. The contact readout:

| rig | coach gun index pad → trigger | off palm → fore-end (facing) | flintlock index pad → trigger |
|---|---|---|---|
| generated | 18 mm (tolerance 15) | 29 mm ok (45°) | 20 mm (70 mm without the shape) |
| painted | 118 mm | 23 mm ok (26°) | 100 mm (38 mm without) |
| leather | 28 mm | 23 mm ok (23°) | 54 mm (81 mm without) |

The generated arms come from the same asset-pipeline generator as hotel's, and hotel's holds and shapes land within
a few millimetres of hotel's own result there. On the other rigs the off hand still passes, but the trigger finger
does not. The flintlock's trigger shape (index [-35, -25, -15]) helps generated and leather and hurts painted, whose
gun clip already points the index forward; no single index curl tried served all three. A shape is relative to each
rig's base hand, so a per-look shape is only rig-neutral once every rig's `grip` brings its clip to the same base
pose. The flintlock's `trigger` and `butt` landmarks are estimated from a side silhouette of its vertices.

## Backups of generated art

`content/old-art/` stays out of git, including Git LFS. LFS still keeps every uploaded version, and each
re-import is about 1 GB, more than GitHub's free LFS allowance has historically been. The output is fully
regenerable from `old-game/`, `old-game-fbx2013/` (the FBX 6 files re-saved as FBX 2013 on den-win11) and
the scripts, so back up those inputs. If the output itself must be kept, put a tarball outside git history,
such as on a NAS or attached to a GitHub release.
