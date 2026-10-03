# Generated levels

The old Unity game built its dungeons from authored pieces at run time. Rusty
Riders keeps those pieces as content and stamps levels from them the same way.

## Content

| Path | What it is | Source |
| --- | --- | --- |
| `content/levels/generator.json` | Cell size (15 m), sector size (8 cells per layout node), corridor wavering chance, corridor step cap | `ProceduralLevelConfig.asset` |
| `content/levels/tile-kinds.json` | The 18 tile kinds (`Start` … `Door`) with their 3x3 walkability templates | `DetailGrid.asset` |
| `content/levels/chunks.json` | 29 chunks: sparse room cells and door cells (`door` = the side the door opens away from the room), objectives (`primary`, `secondary`, `chest`) | `Levels/Chunks/*.asset` |
| `content/levels/layouts.json` | 14 layout graphs: nodes on a sector grid with tags (`start`, `exit`, `key`, `bonus`, `spawner`, `boss`…) and links (`locked` where set) | `Levels/Layouts/*.asset` |
| `content/levels/styles.json` | Styles: faction and tileset | `Levels/Styles/*.asset` |
| `content/levels/tilesets/<id>.json` | Tile prefabs per tile kind (variants are equally likely) and each prefab's 9x9 walkability grid | `Levels/TileSets/*.asset` and each tile prefab's `CombatPrefabPathfinding` |
| `content/level.json` | Which tileset, layout and seed to stamp, wall collision height, background | authored here |
| `content/old-art/placements/GameData/Levels/TileSets/...` | Each tile prefab's converted art: GLB placements inside the 15 m tile | generated, `scripts/import-old-art.sh` |

`scripts/extract-level-data.py` regenerates everything under
`content/levels/` from `old-game/`. The JSON is checked in and is the product's
copy from then on: edit it (or add new chunks and layouts) without Unity.
Rerunning the script overwrites it.

Conventions are the old game's, in Unity space:
- x is east, z is north and y is up.
- Directions are `forward` (+z), `right` (+x), `back` (-z) and `left` (-x).
- A rotation r turns r × 90° clockwise, seen from above.
- The 3x3 and 9x9 grids are rows of characters (`.` walkable, `#` blocked), with row 0 at north and column 0 at west.

The level scene converts to glTF space when it places art: Unity is the X mirror, so x flips and turns reverse.

## Generation

`Levels/LevelGenerator.cs` follows the old `ProceduralLevelBuilder` geometry. It is seeded by `level.json` and N.
1. **Chunks.** Each layout node gets a random chunk turned so that its doors face exactly the node's links. If none fits exactly, the generator accepts a chunk with extra doors.
   - Start, exit and spawner nodes need a chunk with a primary objective.
   - Bonus and key nodes need one with a secondary objective or a chest.
2. **Corridors.** Each link gets a door cell on each side and a one-cell corridor between them. The corridor greedily steps toward its target, sometimes taking the second-best step.
3. **Walls and corners.** Each cell's walls come from its neighbours: a missing neighbour, a different cell type (room or corridor), or open. Doorways open through door cells. Corners are inner (two walls meet) or outer (a missing diagonal). Together these choose the tile kind (the old `FindTile`).
4. **Rotation.** Each tile is turned by the first rotation whose template blocks every wall and corner of the cell (the old `FindRotation`).

Deliberate differences from the old code:
- Links are read from both nodes. The old code read only the owner's side.
- Tag tests match any flag. The old exact-equality test skipped combined tags such as `exit` + `boss`.
- Corridors do not step through room cells.
- Locked links get no door. The door art fails Engine admission (degenerate triangles, and an animated door), so it is listed as a level problem instead.

## Stamping

`Levels/LevelScene.cs` places one variant of each planned tile's prefab at its
cell. Only LOD0 renderers are drawn.

Collision is the ground plus one box per blocked cell of the prefab's 9x9
walkability grid, up to `wallHeight`. That grid is the old combat grid, so
walls stop the walker roughly where the art is. It is the old game's coarse
grid (1.67 m cells), not the art's real shape. Tiles without a grid use their
tile kind's 3x3 template.

## Not used yet

- **Gameplay data:** objectives, chests, spawners, keys and lock colours are extracted but not placed.
- **Tile kinds:** `Start`, `Goal`, `Warp` and `Door` tiles are never chosen, as in the old game.
- **Palettes:** each tileset's colour palettes (`_Colors*.asset`) are not extracted. The old game recoloured materials per level with them.
- **Unused old data:** the object database (lights, clutter, bonuses) and fog are not translated.
- **Refused art:** a few tile models fail Engine admission and are listed as problems. They include ForestMap `goal1`, `roadstop1` and `roadstop2`, and CyberTube `roomcornerC1`.
