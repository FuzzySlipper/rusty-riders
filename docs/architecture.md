# Product architecture

> The product decides. The Engine guarantees.

```text
content/levels/ (old generator data) + content/level.json + content/old-art/ (converted GLBs, placement files)
  -> level generation and stamping, gallery layout, walker and HUD facts (C#)
  -> Rusty.Engine safe SDK
  -> SDK-generated bind entry point and ABI
  -> packaged Rust host, input, renderer, UI transport, and browser shell
  -> DOM companion
```

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/RustyRiders.Game/RustyRidersProduct.cs` | Lifecycle callbacks, per-update composition (input, walker steps, HUD publication) and which scene is current |
| `src/RustyRiders.Game/Content/` | `Authored`: reading one content file through Engine Content into its domain's typed record, with errors naming the file and field, and validation helpers; `Template`: authored text with `{name}` placeholders |
| `src/RustyRiders.Game/Levels/LevelData.cs` | Typed `content/levels/*.json` and `content/level.json` |
| `src/RustyRiders.Game/Levels/LevelGenerator.cs` | The old generator's geometry: chunk fitting, corridors, walls, corners, tile kind and rotation per cell |
| `src/RustyRiders.Game/Levels/LevelScene.cs` | Stamping tile art per planned cell, wall and ground collision, the level's navigation and gameplay points, spawn at the arrival point, level problems, and whether the level is playable |
| `src/RustyRiders.Game/Levels/LevelRules.cs` | `points.json`, `navigation.json` and `worlds.json`: where a level's points go, how its navigation is derived, and the worlds rifts lead to |
| `src/RustyRiders.Game/Levels/LevelNavigation.cs` | Engine navigation derived from the level's collision (`ReplaceCollisionNavigation`) for the body enemies walk with; step, nearest-support and route queries |
| `src/RustyRiders.Game/Levels/LevelPoints.cs` | The level's gameplay points from the plan's rooms: entry portal and arrival, rifts with destination worlds, caches and resident spots, each snapped to floor the arrival can walk to |
| `src/RustyRiders.Game/Levels/Portals.cs` | Placeholder portal and marker primitives animated on world time, and which rift the walker's feet stand in |
| `src/RustyRiders.Game/Levels/WalkCells.cs` | The placed tiles' walk grids as one open-ground grid, and its merged rectangles, for the generated builds |
| `src/RustyRiders.Game/Levels/LevelShells.cs` | The `shells` build: one Engine implicit field carved from the placed tiles' walk grids, extracted (implicit or sampled) into drawn sections and collision |
| `src/RustyRiders.Game/Levels/LevelSweeps.cs` | The `sweeps` build: walls swept along the open ground's outlines, ceilings and floor as one UV-mapped retained mesh |
| `src/RustyRiders.Game/Gallery/` | `gallery.json` and the gallery scene's exhibit layout and ground |
| `src/RustyRiders.Game/Art/` | Placement files and converted GLBs: each read or opened once, problems recorded; `MaterialRecolor` turns a level palette into Engine material factor overrides for the appearances whose materials it names; `HarvestedMaterials` makes Engine materials for generated geometry from a converted Unity material's embedded textures and colours |
| `src/RustyRiders.Game/Player/` | Walker tuning, first-person controls, character steps, free flight and camera; `IWalkScene`, what a scene gives the walker and HUD; `PlayerVitals`, the player's stats, health and effects and what they do to the body (stun, slow, knockback) |
| `src/RustyRiders.Game/Mechanics/` | The stat vocabulary (`content/mechanics/`: attributes, derived stats, tracks, damage kinds, effects) and each actor's live Engine stats (`ActorStats`) and effects (`ActorEffects`), shared by the player and enemies |
| `src/RustyRiders.Game/Actions/` | The action catalog (`content/actions/actions.json`) and the one pipeline every hand and enemy uses: `ActionUser` (windup, commit, recovery and cooldown on world seconds, scaled by the user's `action-time`), `ActionResolution` (melee sweeps, hitscan rays, projectiles cast per step, areas and self, through Engine spatial queries; damage packets scaled and multiplied by the user's stats, contributions, resistances, effects) |
| `src/RustyRiders.Game/Items/` | The item catalog (`content/items/items.json`): typed item kinds. A weapon grants a hand its action, and a gun also its magazine and reload. |
| `src/RustyRiders.Game/Combat/` | `PlayerCombat`: the player's two hands of carried weapons (`content/player/kit.json`), one action at a time, costs, magazines and reloads, training dummies, and what combat shows (held weapons on the viewmodel layer, projectiles, impact bursts, HUD lines). A player's action returns its world-time cost, which the product buys from `TimeFlow`. `Combatants`: the player and dummies as action actors. |
| `src/RustyRiders.Game/Developer/` | Live-debug commands over the Engine command catalog (`rusty dev --live-debug`): `riders.level.*` and `riders.player.*` read state; `riders.dev.*` are developer overrides (build a level, stand somewhere, hurt or affect the player) |
| `src/RustyRiders.Game/Time/` | The gameplay-time policy over Engine `GameplayTime`, the one world clock: `TimeTuning` (`content/time.json`, with `heldRate` the single value for standing still) and `TimeFlow`, which holds the world while the player stands still, runs it while they move, jump or fall, and advances it for a wait or an action's cost. The gallery and free flight run realtime |
| `src/RustyRiders.Game/Ui/Hud.cs` | HUD facts for the DOM panel |
| `src/ui/main.js` | DOM panel: status, problems, what is underfoot, position, controls, the world's time state and clock |
| `scripts/extract-level-data.py` | Regenerating `content/levels/` from `old-game/` |
| `scripts/import-old-art.sh`, `scripts/prune-old-art.py` | Regenerating `content/old-art/` from `old-game/` with asset-pipeline `unity-import` |
| Engine SDK/runtime | Generated interop, admitted updates/input, GLB admission and drawing, collision and character steps, camera, UI transport, host and browser shell |

## Data flow

`scripts/import-old-art.sh` runs asset-pipeline's `unity-import` over the old
game's level tileset tiles (`GameData/Levels/TileSets`) and its combat
tilesets and levels. It writes GLBs and one placement file per prefab
(glTF-space transforms, LOD levels tagged, every GLB checked by the Engine's
`rusty asset check`) to the ignored `content/old-art/`, keeping only GLBs a
placement draws. `scripts/extract-level-data.py` translates the old
generator's data into the checked-in `content/levels/` (see
[levels.md](levels.md)).

The product starts in a generated level. `LevelGenerator` plans tiles from a
layout, the chunks and the tile templates with the seed in `level.json`
(N advances it). `LevelScene` picks the level's palette (named, or by seed), places one variant of each tile's prefab art,
turned to the planned rotation, and admits one static-mesh collision: the
ground plus boxes over each tile prefab's blocked walk-grid cells. G switches to
the gallery, which lays the combat prefabs out in rows on a ground that alone
collides. Switching disposes the current scene (facts, appearances, GLB
resources, spatial session) before building the next, and moves the walker to
its spawn.

`ConvertedArt` reads placement files through Engine Content and opens each
distinct GLB once with `Animation.OpenAnimatedMesh` (static GLBs use the same
admission), sharing one appearance across its placements; only LOD0 rows are
drawn. A missing placement file or a refused GLB is listed as a problem and
skipped, so the game starts without converted art.

Each admitted update, `Walker` reads Engine `FpsInput` and `Look`, then
proposes one Engine character step per admitted fixed step against the current
scene's session (or moves freely when flying), and samples its camera. Every
host observation delivers an update once gameplay time is selected: look and
controls read host time (so they stay live while the world holds), the body and
everything else in the world advance only per admitted step, and `TimeFlow`
then chooses the next rate from what the player did. The
product publishes HUD facts; the DOM shows them and holds no state.

Engine owns pause/resume/restart/shutdown admission. Restart returns the walker
to the spawn. Disposal clears published facts, then releases appearances, GLB
resources, the spatial session, the camera and the UI stream.

## Build and host

`Directory.Build.props` pins one immutable SDK/runtime pair. The Engine `rusty`
command installs it into its shared cache (`rusty install`), supplies its
package source to restores, and runs the product on its runtime (`rusty dev`,
which owns staging, watching, worker replacement and serving). `rusty build`
stages CoreCLR through `StageRustyEngineCoreClrProduct`; `rusty build --aot`
runs `VerifyRustyEngineAot` for explicit fidelity/release checks. Generated
bindings and the bind entry point are ignored output, never edited sources.
The product supplies only its C#, DOM UI and content; browser assets and host
binaries stay in the Engine runtime. The `engine-pair` workflow advances the
pin only after the product builds and serves on the new pair (without the
generated art, which CI does not have).

Before adding a mechanism, check both the installed safe SDK and the owners
above. Product meaning stays downstream. A missing Engine capability is an
upstream request, not another local host, transport, scheduler, or renderer.
