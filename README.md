# Rusty Riders

A Rusty Engine game inspired by an unfinished Unity game (kept, ignored, in
`authoring/assets/old-game/`). It is not a port. The current slice walks a level generated the old game's
way: its chunks, layouts and tilesets, translated into `content/levels/`, stamp
a dungeon of converted tile art. A gallery of the old combat tilesets and levels
is one key away.

The game is *Rift Riders*. Its settled design is on den-wiki,
[wiki/rift-riders](https://wiki.dragonden.stream/wiki/rift-riders); questions still being decided are on the crew
board `rusty-riders`; work is scheduled as Den tasks, and technical documents are Den documents, both in project
`rusty-riders` (see [AGENTS.md](AGENTS.md)).

## Setup

The supported runtime pair targets Linux x64. Install the .NET 10 SDK, `curl`
and `tar`. NativeAOT also needs the platform compiler/linker prerequisites
(Clang and zlib development headers on Linux). Get the Engine's `rusty`
command once:

```bash
curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash
```

Then, from this repository:

```bash
rusty status
rusty install
scripts/art-sync.sh pull
rusty dev --port 8787
```

The game's art is in the ignored `content/art/` (about 1.1 GB); `scripts/art-sync.sh pull` copies it from the den
NAS, and on Windows `scripts\art-sync.ps1` copies it from den-agents' checkout over `P:`. After changing art, run
`scripts/art-sync.sh push` (licences: [content/art/LICENSE.md](content/art/LICENSE.md)). The authoring sources
(the old Unity project, generated candidates, training sets) come with `authoring/tools/assets-sync.sh pull`; only the
tools that convert or generate art need them.

To rebuild the converted part instead, `authoring/tools/old-game/import-old-art.sh` converts the level tileset tiles and the combat
tilesets and levels into the ignored `content/art/`. That is about 1 GB at
the default 1024 px texture cap, copied again into each staged build;
`MAX_TEXTURE=512` roughly halves it. The import takes about ten minutes. It
needs a sibling `../asset-pipeline` checkout (or `ASSET_PIPELINE=`),
`authoring/assets/old-game/`, and the upgraded FBX copies in `authoring/assets/old-game-fbx2013/` (see
asset-pipeline `docs/unity-import.md`). Without it the game still starts and
lists what is missing. `authoring/tools/old-game/extract-level-data.py` regenerates the
checked-in `content/levels/` from `authoring/assets/old-game/`.

Open the URL printed by the host and click to capture the mouse:

| Key | Action |
| --- | --- |
| WASD | Move |
| Shift | Sprint |
| Space | Jump |
| F | Toggle flight (Space and Ctrl rise and descend) |
| N | New level with the next seed |
| B | Cycle the level build: stamped tiles, generated shells, swept meshes (tilesets with a shell recipe), the parametric kit (tilesets with a kit recipe) |
| V | Cycle the generated builds' floor texture through the recipe's generated candidates |
| G | Switch between the level and the gallery |
| R | Return to the start |
| Esc | Release the mouse |

The level collides with its ground and walls. In the gallery only the ground
collides, so fly to see raised floors.

The level's tileset, layout and seed are set in `content/level.json`. Its `build` is `tiles` (the default), `shells` or `sweeps`, generated level geometry for tilesets with a recipe in `content/levels/shells/` (CaveMap, LavaMap and ForestMap), or `kit`, a world's parametric kit over the room plan for tilesets with a recipe in `content/levels/kits/` (StoneRoadMap); see Den `technical/level-generation`. With `--live-debug`, `riders.level.plan` reads the room plan, `riders.dev.plan true` draws it over the level, and `riders.dev.build kit` rebuilds the level as another build. The
walker tuning is in `content/walker.json`, and the gallery's exhibits in
`content/gallery.json`.
To run it in the background instead, `rusty dev start` (same options) returns once the
game serves; `rusty dev status` and `rusty dev stop` report and end that
session. Stop sessions this way rather than by killing processes: other
products' hosts run on the same machine.

`rusty dev` runs the pinned pair's runtime: CoreCLR loads the product, and
changes to declared C#, UI, or content inputs rebuild and reload it. See
`rusty dev --help` for `--bind-host`, `--live-debug`, and `--debugger`.

Tuning held weapons and the arms in a running game is in
[src/RustyRiders.Game/Held/README.md](src/RustyRiders.Game/Held/README.md).

`Directory.Build.props` pins the exact SDK/runtime pair and names the product
(`RustyEngineProject`) that `rusty dev` and `rusty build` run when no
`--project` is given. `rusty install`
downloads it once into the shared Engine cache, and later builds and runs work
offline. No Engine source checkout is required.

To adopt the newest published pair deliberately:

```bash
rusty update
rusty build
```

`rusty update` lists the release notes to read; include the changed
`Directory.Build.props` in the resulting source change. This repository's
`engine-pair` workflow does the same every six hours: it moves the pin only
after the product builds and serves on the new pair, and otherwise opens an
`engine-pair-update` issue with the build output and the notes to read. For an explicit
NativeAOT fidelity/release check:

```bash
rusty build --aot
```

## Repository shape

| Path | Responsibility |
| --- | --- |
| `src/RustyRiders.Game/` | Ordinary safe C# product: level generation and stamping, gallery, walker, HUD facts, product metadata |
| `src/ui/` | DOM companion: HUD and screens |
| `content/levels/` | The old generator's chunks, layouts, styles, tilesets and tile templates as JSON |
| `content/level.json`, `content/walker.json`, `content/gallery.json` | Which level to stamp, walker tuning, gallery exhibits |
| `content/art/` | Ignored, synced to the NAS by `scripts/art-sync.sh`: only art the game draws, converted GLBs and placement files, arms, hotel's held models, textures, UI art (each `sources.json` stays in git) |
| `authoring/` | What makes the content and is not loaded by the game ([authoring/README.md](authoring/README.md)): `tools/` (tracked) and `assets/` (ignored but for their text, backed up to the NAS by `authoring/tools/assets-sync.sh`) |
| `authoring/assets/old-game/`, `old-game-fbx2013/` | The old Unity project and its upgraded FBX overlay, the donor source |
| `authoring/tools/old-game/` | Converters from the old game: art (`import-old-art.sh`, asset-pipeline unity-import), level data, UI sprites |
| `authoring/tools/textures/`, `.claude/skills/texture-batch/` | Generated textures: generation, tiling and normal maps, checks, judging, LoRA datasets and training; the house style and rubric in `style-and-judging.md` |
| `authoring/tools/held/` | First-person arms and held models: imports, held-motion bakes, arm clip packs |
| `authoring/tools/review/` | A LAN web app for a human pass over an image folder (flag, keep, note, crop), and applying its results |
| `scripts/` | Art sync and live held-tuning checks against a running game |
| `Directory.Build.props` | Matched Engine SDK/runtime pin |
| `src/ui/README.md` | DOM companion: modules, facts and intents, the UI art |
| `src/RustyRiders.Game/Held/README.md` | Tuning held weapons and the arms in a running game |
| `authoring/tools/held/arm-clips/README.md` | Arm clip packs from humanoid motion |
| `docs/agent-review/` | Reusable review workflow and lane packets |

The SDK generates the product's bind entry point inside its ordinary build;
there is no composition project. The Engine runtime supplies the host and
browser shell. Product metadata, input intents, content/UI
roots, and projection identity live in the ordinary `.csproj`.

Read [AGENTS.md](AGENTS.md) before extending the product. Keep instructions
about current behavior and ownership; exact dependency identities belong in
configuration, and task status belongs in the task system.
