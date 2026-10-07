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
| `content/levels/tilesets/<id>.json` | Tile prefabs per tile kind (variants are equally likely), each prefab's 9x9 walkability grid, and the tileset's colour palettes | `Levels/TileSets/*.asset`, each tile prefab's `CombatPrefabPathfinding`, `Levels/TileSets/*/_Colors*.asset` |
| `content/level.json` | Which tileset, layout, seed and (optionally) palette to stamp, wall collision height, background | authored here |
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

## Palettes

The old game gave each level one of its tileset's palettes (`PrefabMaterialsColors`), picked by the level
seed, and recoloured the materials it lists after the level was built:
- each listed material's main colour (`Material.color`, which is the shader's `_Color`) was set to the entry's `baseColor`;
- its `_EmissionColor` was set to the entry's `emissive` when that colour's alpha is above 0.

The palettes are kept under `palettes` in each tileset file as Unity stored them: gamma-encoded rgba, with
materials named by their Assets-relative `.mat` paths. Entries naming a material that does not exist are kept
under `missingMaterials`. The old loader resolved those names by path, so they recoloured nothing. That
covers all of Cybertubes' entries and a few ForestMap and UndergroundPalace ones.

The level scene does the same at load time, using the palette named in `level.json`, or otherwise one picked
by the seed:
1. asset-pipeline records on every converted glTF material which Unity material and properties its factors
   came from (`extras.unityMaterial`, `extras.unityProperties`);
2. `Art/MaterialRecolor` reads each GLB's JSON chunk (not its binary chunk) and builds Engine material factor
   overrides for the matching slots: base colour when the factor came from `_Color`, emissive factor and
   strength when it came from `_EmissionColor`. Colours are converted to linear as the converter does. The
   Engine numbers a GLB's slots by the glTF material indices its primitives use, in ascending order;
3. `Animation.UpdateAnimatedMeshMaterialFactors` applies them to that appearance. The GLB opens once, and its
   textures, maps and texture transforms still multiply the new factors.

The toon shaders most tilesets use name their emission property `Color_EC47A898`, not `_EmissionColor`. So,
as in Unity, palette emission only changes materials whose shader really has `_EmissionColor`.

Factors are per appearance (rusty-engine #9367), so one resource could carry several palettes at once. A level
uses one palette, so each level scene opens each GLB once with one appearance.

## The old source

All of the old generator lives in `old-game/Assets/Scripts_Main/` (namespace `PixelPhantasm`; an identical copy
is in `old-game/Submodules/RpgCodebase/Scripts_Main/`):

| Concern | Old code |
| --- | --- |
| Build entry point, chunk fitting, corridors | `Level/Procedural/ProceduralLevelBuilder.cs` |
| Walls, corners, tile kind and rotation | `Level/LevelCellMap.cs`, `Level/Structure/SimpleTiles/SimpleTileset.cs`, `SimpleTiles.cs` |
| Tile templates | `Level/Json/DetailGridConfig.cs`, `GameData/Levels/DetailGrid.asset` |
| Placement passes (detail map, objectives, combat grid) | `Level/PostProcess/{DetailMapProcess,LevelFeaturesProcess,CombatMapProcess}.cs`, `Level/Components/CombatPrefabPathfinding.cs` |
| Palettes | `Level/Structure/PrefabMaterialsColors.cs` (`PaintLevel`) |
| Registries the generator saw | `GameData/Resources/{LevelChunkFactory,LevelLayoutFactory,LevelStyleFactory,LevelPainterData,LazyDb}.asset` |

Quirks of the old code worth knowing (some are fixed here, as listed under Generation):
- Corridor ends may leave a one-cell stub.
- Side pockets could never start (`MinExpansionStart` exceeds any corridor).
- Lights were never placed (`FindLightType` is private and unused).
- `DetailGrid5.asset` and the Empty tileset are unused.
- Locks are one-sided in the data.
- A few layouts link nodes that are not adjacent (`LevelLayout 5`, `7`) or to a missing node (`LevelLayout 1`).
- The level tile art is in `Art/Models/GridMaps/`; `Art/Models/CombatEnvironment/` is only for the separate combat-arena tilesets.

## Generated shells

An exploration (Den #9373) of rebuilding level geometry from the same plan instead of stamping tile meshes.
`level.json`'s `build` (`shells` or `sweeps`), or B in a level (cycling tiles, shells, sweeps), switches to it for
tilesets with a recipe in `content/levels/shells/<tileset>.json`. Only CaveMap has one. Without a recipe the level stamps its tiles and
lists a problem.

`Levels/LevelShells.cs` treats the plan as measurements:
1. **Open ground.** Each placed tile prefab's 9x9 walk grid, turned to its tile, marks open cells across the
   level. Each open cell takes the room or the corridor ceiling height by its tile kind.
2. **Air boxes.** Open cells merge greedily into rectangles, one Engine implicit box each. The boxes reach from
   below the floor to their ceiling.
3. **Rock.** The air boxes are smoothly blended (`blendRadius`) and displaced by seeded waves (`wallNoise`), then
   carved out of rock. A separately displaced floor (`floorNoise`) fills the bottom.
4. **Extraction.** The whole level is one field, so pieces never seam. `extraction` picks how it becomes meshes:
   - `implicit` meshes the level in one adaptive pass over a cube as wide as the level's longest side, then
     partitions it into a drawn section per tile cell. It collides as one mesh.
   - `sampled` rasterizes the field onto a lattice over just the level's box, then meshes it in seamless blocks
     (`blockMetres`). Each block draws and collides.
5. **Collision.** Collision is the generated geometry itself (Engine mesh-reference collision assets), not the
   old grid's boxes.

Materials come from the converted art. `Art/HarvestedMaterials` finds a converted GLB that uses the recipe's
Unity material (`extras.unityMaterial`). It admits that material's embedded base colour and normal textures
through Engine Content and opens them as textures, keeping its base colour and emission. The level palette's
`_Color` recolour applies as it does to tile art. Triplanar materials blend three planes and repeat once per
mesh unit, so the field is extracted in units of `textureMetres` and its sections are placed at that scale.

Each tile prefab's models whose file names start with one of `props` stay as placed dressing. For CaveMap
these are stalagmites, stalactites and rocks; each tile's shell mesh (`road1`, `roomwall1`, `ground1` …) is
dropped.

The status line reports the air boxes, vertices, triangles, sections, Engine meshing time and the whole build
time.

A recipe's `floorTextures` are generated alternatives to the floor material's own texture: content albedo and
normal PNGs (8-bit RGBA, as Engine content textures must be) with their repeat length. `level.json`'s
`floorTexture`, or V, picks one by id. The floor keeps its harvested colour and palette recolour.
`scripts/texture-gen/make_tileable.py` made them tile and derived their normal maps;
`content/art/textures/<set>/sources.json` records how each was generated.

`sweeps` (`Levels/LevelSweeps.cs`) builds ordinary UV-mapped meshes from the same open ground instead:
1. **Walls.** Each outline of the open ground is traced and its corners rounded (`sweep.cornerRadius`). A wall is
   swept along it: U runs in metres along the wall, so a trim sheet flows round corners without seams, and V runs
   up it. Seeded waves can bulge the wall (`sweep.bulge`), fading out at the floor and ceiling.
2. **Ceilings and floor.** Ceilings are the merged open rectangles at their heights, with step faces where a room
   ceiling meets a lower corridor's. One floor plane lies under everything.
3. **Mesh.** It all goes into one `Graphics.CreateMeshResource` mesh, drawn in tile-cell sections and colliding
   whole. Its materials are the harvested ones without triplanar. Normal maps work without tangents, because the
   shader derives the frame per pixel. Findings so far are in [direction.md](direction.md#generated-shells-first-trial).

## Gameplay points and navigation

After its collision is admitted, a level derives Engine navigation from it (`Levels/LevelNavigation.cs`,
`content/levels/navigation.json`). It is a planar grid for the body enemies walk with, at 0.75 m cells. The old
grid's narrowest gaps are 1.67 m, so a 0.35 m body always has a cell centre that fits through them (at 1 m cells
some doorways had none).

The plan keeps each layout node's room cells and chunk objectives, and `Levels/LevelPoints.cs` places the
level's points from them by `content/levels/points.json`:

| Point | Where |
| --- | --- |
| Entry portal | The start room's primary objective. The player arrives `arrivalMetres` in front of it, facing into the level. Chasing enemies will come through it. |
| Rifts | The primary objective of rooms tagged with `riftTags`, in that order (never the start room), up to `maximumRifts`. Other rooms' objectives make up `minimumRifts` when too few rooms carry the tags. Each leads to another world (`content/levels/worlds.json`) and shows its colour. |
| Caches | Objectives named in `cacheObjectives`, plus the primary objective of rooms tagged `cacheTags` |
| Resident spots | The cell nearest the middle of rooms tagged `residentTags` |

Every point stands on navigation:
- A point is snapped to the nearest support. If the arrival cannot walk there, it moves to the nearest floor the
  arrival can reach, up to `reachSlackMetres` away.
- A spot that still cannot be reached is left out and listed as a level problem.
- A level whose arrival reaches fewer than `minimumRifts` rifts is rejected. The next seed is tried, up to
  `levelAttempts` levels.

Portals are placeholder primitives that pulse and spin on world time. Cache and resident spots show as small
cubes while `markers.show` is on. Walking into a rift builds a level of its destination world, on a layout and seed
drawn from the current seed.

Live-debug (`rusty dev --live-debug`, then `rusty-live-debug --origin URL --command "…"`) reads and drives this:
- `riders.level.inspect` lists the points and problems.
- `riders.level.route <rift>` explains a route from the arrival.
- `riders.dev.level <tileset> "<layout>" <seed>` builds a level.
- `riders.dev.rift <index>`, `riders.dev.entry` and `riders.dev.goto <x> <z> <yaw>` stand the walker somewhere.

## Not used yet

- **Gameplay data:** keys and lock colours are extracted but not used; locked links are open corridors.
- **Tile kinds:** `Start`, `Goal`, `Warp` and `Door` tiles are never chosen, as in the old game.
- **Unused old data:** the object database (lights, clutter, bonuses) and fog are not translated.
- **Refused art:** a few tile models fail Engine admission and are listed as problems. They include ForestMap `goal1`, `roadstop1` and `roadstop2`, and CyberTube `roomcornerC1`.
