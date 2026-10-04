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

## Backups of generated art

`content/old-art/` stays out of git, including Git LFS. LFS still keeps every uploaded version, and each
re-import is about 1 GB, more than GitHub's free LFS allowance has historically been. The output is fully
regenerable from `old-game/`, `old-game-fbx2013/` (the FBX 6 files re-saved as FBX 2013 on den-win11) and
the scripts, so back up those inputs. If the output itself must be kept, put a tarball outside git history,
such as on a NAS or attached to a GitHub release.
