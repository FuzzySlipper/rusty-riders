# Rusty Riders

A Rusty Engine game inspired by an unfinished Unity game (kept, ignored, in
`old-game/`). It is not a port. The current slice is a first-person gallery:
the old combat tilesets and levels, converted by asset-pipeline's
`unity-import` producer, laid out in rows on a flat ground to walk or fly
around.

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
scripts/import-old-art.sh
rusty dev --project src/RustyRiders.Game/RustyRiders.Game.csproj --port 8787
```

`scripts/import-old-art.sh` converts the combat tilesets and levels into the
ignored `content/old-art/` (about 0.5 GB at the default 1024 px texture cap;
`MAX_TEXTURE=512` halves it). It needs a sibling `../asset-pipeline` checkout
(or `ASSET_PIPELINE=`), `old-game/` and the upgraded FBX copies in
`old-game-fbx2013/` (see asset-pipeline `docs/unity-import.md`). Without it the
game starts on an empty ground and says what is missing.

Open the URL printed by the host and click to capture the mouse: WASD moves,
Shift sprints, Space jumps, F toggles flight (Space/Ctrl rise and descend), R
returns to the start and Esc releases the mouse. Only the ground collides; the
converted meshes are drawn, not collided with, so fly to see raised floors.
Which exhibits appear, their spacing and the walker tuning are in
`content/gallery.json`.
`rusty dev` runs the pinned pair's runtime: CoreCLR loads the product, and
changes to declared C#, UI, or content inputs rebuild and reload it. See
`rusty dev --help` for `--bind-host`, `--live-debug`, and `--debugger`.

`Directory.Build.props` pins the exact SDK/runtime pair. `rusty install`
downloads it once into the shared Engine cache, and later builds and runs work
offline. No Engine source checkout is required.

To adopt the newest published pair deliberately:

```bash
rusty update
rusty build --project src/RustyRiders.Game/RustyRiders.Game.csproj
```

`rusty update` lists the release notes to read; include the changed
`Directory.Build.props` in the resulting source change. This repository's
`engine-pair` workflow does the same every six hours: it moves the pin only
after the product builds and serves on the new pair, and otherwise opens an
`engine-pair-update` issue with the build output and the notes to read. For an explicit
NativeAOT fidelity/release check:

```bash
rusty build --project src/RustyRiders.Game/RustyRiders.Game.csproj --aot
```

## Repository shape

| Path | Responsibility |
| --- | --- |
| `src/RustyRiders.Game/` | Ordinary safe C# product: gallery layout, walker, HUD facts, product metadata |
| `src/ui/main.js` | DOM panel: load status, nearest exhibit, controls |
| `content/gallery.json` | Authored gallery: exhibits per row, spacing, walker tuning |
| `content/old-art/` | Generated, ignored: converted GLBs and placement files |
| `scripts/import-old-art.sh` | Regenerates `content/old-art/` with asset-pipeline unity-import |
| `Directory.Build.props` | Matched Engine SDK/runtime pin |
| `docs/architecture.md` | Current ownership and data flow |
| `docs/ui.md` | DOM companion contract |
| `docs/agent-review/` | Reusable review workflow and lane packets |

The SDK generates the product's bind entry point inside its ordinary build;
there is no composition project. The Engine runtime supplies the host and
browser shell. Product metadata, input intents, content/UI
roots, and projection identity live in the ordinary `.csproj`.

Read [AGENTS.md](AGENTS.md) before extending the product. Keep instructions
about current behavior and ownership; exact dependency identities belong in
configuration, and task status belongs in the task system.
