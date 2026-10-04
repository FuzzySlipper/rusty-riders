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

Captures: crew-playtest sessions `af817b80-a918-4cbc-a914-18485bb68d9d` (tiles, shells),
`dcf08c11-e5c9-4634-a624-a524ce53cf72` (floor textures) and
`94395057-13a6-4930-b617-2d542f9575b5` (sweeps), retained 14 days.

## Backups of generated art

`content/old-art/` stays out of git, including Git LFS. LFS still keeps every uploaded version, and each
re-import is about 1 GB, more than GitHub's free LFS allowance has historically been. The output is fully
regenerable from `old-game/`, `old-game-fbx2013/` (the FBX 6 files re-saved as FBX 2013 on den-win11) and
the scripts, so back up those inputs. If the output itself must be kept, put a tarball outside git history,
such as on a NAS or attached to a GitHub release.
