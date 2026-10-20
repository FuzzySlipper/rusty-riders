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
bone from the camera bone: `scripts/bake-held-motion.py` samples `weapon_r` (or `--hand l`) every frame, takes its move
and turn from the set's idle (`--rest`), splits the frames into windup (to the strike, the frame the grip moves
fastest), commit (until it slows below `--slow` of that) and recovery, keeps only the keys a straight blend between
them would miss by more than `--position` metres or `--angle` degrees, and writes them as poses and a track into
`content/held/motion.json`, each key timed by its phase so it lands on the same beat at the action's own speed. The
hand's closure comes from the source hand's finger curl. The arms are not baked: ours reach the grip by IK on any rig.

```bash
blender -b --factory-startup --python scripts/bake-held-motion.py -- \
  old-game/Assets/MoreArt/FpMelee/FPP_SnS/FPP_sns_Attack_RD.fbx \
  --rest old-game/Assets/MoreArt/FpMelee/FPP_SnS/FPP_sns_Idle.fbx --motion sns --action blade-slash --look rift-blade --seat
```

That is the rift blade's slash (motion `sns`, 30 keys from 121 frames), baked with `--seat`.

Two things keep the hands on a baked weapon:
- **The body leans.** The source character lunges: its shoulder moves 17 cm toward the blow by the strike, so its
  nearly straight arm reaches a grip 0.64 m ahead. Each baked key carries that move as `shoulder` (camera space, the
  main hand's; mirrored for the off hand), and the arms move with it. Beyond that, `ArmsView` leans the body in by
  itself when a grip still lies past an arm's length (at most 0.3 m), so a motion holds on rigs with shorter arms.
  `riders.arms` reports both leans and the wrists' shortfall.
- **The look rests where the source rests, held as the source holds it.** Turns are baked from rest, so they follow
  the source's path only if the look starts where the source weapon does. `--seat` sets the look's nudge to the source's
  rest and its `grips.hand` to the source hand's hold on the weapon (the hand's canonical frame from the look's, measured
  from the source's knuckles as `ArmsView` measures rigs). The blade is the weapon bone's axis leaving the fist past the
  index finger and its up side the axis along the fingers (`--blade`, `--edge` override): for the FPP set that is +Z,
  with +Y along the fingers. The axis that travels furthest in a swing is not the blade (it runs along the fingers);
  taking it turned the sword a quarter turn in the hand and folded the wrist back.
- **The elbow follows the source's.** Each key carries the source elbow's bend as `elbow`, the point the holding arm's
  elbow bends toward (camera space, mirrored for the off hand), so the forearm keeps the source's line.

The source's rest grip, (0.28, -0.19, -0.38) from its camera, sits within a few centimetres of the blade stance, so its
moves carry over at scale 1. Compared against a render of the source from its camera at the same moments (its blade drawn
along the weapon bone's +Z), the hand, wrist and blade agree at rest, at the strike and through recovery, and both
leave the view in the windup. Its 1.5 s recovery is
squeezed into the action's 0.25 s. The keys are ordinary held motion: step, retime and edit them in the workbench. A
clip set's own idle is the rest; the `_Start`, `_Loop` and `_End` parts of a heavy attack are separate clips, to bake
one at a time or join first.

## Backups of generated art

`content/old-art/` stays out of git, including Git LFS. LFS still keeps every uploaded version, and each
re-import is about 1 GB, more than GitHub's free LFS allowance has historically been. The output is fully
regenerable from `old-game/`, `old-game-fbx2013/` (the FBX 6 files re-saved as FBX 2013 on den-win11) and
the scripts, so back up those inputs. If the output itself must be kept, put a tarball outside git history,
such as on a NAS or attached to a GitHub release.
