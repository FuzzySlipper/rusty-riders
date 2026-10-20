# Rusty Riders

A Rusty Engine game inspired by an unfinished Unity game (kept, ignored, in
`old-game/`). It is not a port. The current slice walks a level generated the old game's
way: its chunks, layouts and tilesets, translated into `content/levels/`, stamp
a dungeon of converted tile art (see [docs/levels.md](docs/levels.md)). A
gallery of the old combat tilesets and levels is one key away.

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
rusty dev --port 8787
```

`scripts/import-old-art.sh` converts the level tileset tiles and the combat
tilesets and levels into the ignored `content/old-art/`. That is about 1 GB at
the default 1024 px texture cap, copied again into each staged build;
`MAX_TEXTURE=512` roughly halves it. The import takes about ten minutes. It
needs a sibling `../asset-pipeline` checkout (or `ASSET_PIPELINE=`),
`old-game/`, and the upgraded FBX copies in `old-game-fbx2013/` (see
asset-pipeline `docs/unity-import.md`). Without it the game still starts and
lists what is missing. `scripts/extract-level-data.py` regenerates the
checked-in `content/levels/` from `old-game/`.

Open the URL printed by the host and click to capture the mouse:

| Key | Action |
| --- | --- |
| WASD | Move |
| Shift | Sprint |
| Space | Jump |
| F | Toggle flight (Space and Ctrl rise and descend) |
| N | New level with the next seed |
| B | Cycle the level build: stamped tiles, generated shells, swept meshes (tilesets with a shell recipe) |
| V | Cycle the generated builds' floor texture through the recipe's generated candidates |
| G | Switch between the level and the gallery |
| R | Return to the start |
| Esc | Release the mouse |

The level collides with its ground and walls. In the gallery only the ground
collides, so fly to see raised floors.

The level's tileset, layout, seed and optional palette are set in `content/level.json`; without a palette the seed picks one of the tileset's, as the old game did. Its `build` is `tiles` (the default), `shells` or `sweeps`, generated level geometry for tilesets with a recipe in `content/levels/shells/` (CaveMap so far; see [docs/levels.md](docs/levels.md#generated-shells)). The
walker tuning is in `content/walker.json`, and the gallery's exhibits in
`content/gallery.json`.
To run it in the background instead, `rusty dev start` (same options) returns once the
game serves; `rusty dev status` and `rusty dev stop` report and end that
session. Stop sessions this way rather than by killing processes: other
products' hosts run on the same machine.

`rusty dev` runs the pinned pair's runtime: CoreCLR loads the product, and
changes to declared C#, UI, or content inputs rebuild and reload it. See
`rusty dev --help` for `--bind-host`, `--live-debug`, and `--debugger`.

### Tuning first-person weapons

Run `rusty dev --port 8787 --live-debug` and press F2 in play to dock the Engine developer shell: two views of the
hands' grips, the first-person workbench and the console. The workbench picks a weapon look, an action and a hand,
steps or plays the action's keys (in real time, though the world holds), and tunes the look's model fit, its stance,
nudge and grip, each key's timing, curve, hand closure and pose, and the arms' shoulder, elbow and hold, with
sliders, the drawing rig's model turn, scale, palm and base grip, and the feedback punches a hand feels as its action
lands (`content/held/feedback.json`: struck, blocked, fired; `riders.dev.held.feel <kind> <main|off>` tries one), and
the sway between and under actions (`content/held/sway.json`, and each motion's loops). `riders.arms` reports the rig, how far each wrist
falls short of its grip, the arms' lengths and the measured hand frames. Save writes the changed values into `content/held/`.
Editing those files by hand also reloads them into the running game. The same in the console: `riders.dev.motion
<look> <action>`, then `.key`, `.next`, `.play <speed>`, `.at <seconds>`, `.pose`, `.hand`, `riders.dev.held.nudge`,
`riders.dev.held.fit`, `riders.dev.stance.set`, `riders.dev.arms.hold`, `riders.dev.arms.reach` and
`riders.dev.arms.rig <generated|painted|leather>`, which swaps the arms model live. `riders.dev.hands.contact` reports how the drawn hands
meet the main look's grip landmarks (`trigger`, `fore-end`, ...), as the Engine measured them.

The arms are drawn by a rig profile (`content/held/rigs/`, chosen in `content/held/arms.json`): `painted`, the old game's
hand-painted gloves, and `leather`, its MedievalFPArms leather armour, both built from `old-game/Assets/MoreArt` into the
ignored `content/old-art/fp-arms/` by `scripts/import-fp-arms.sh` (Blender 5 and `../asset-pipeline`); and `generated`,
kept in git, from asset-pipeline's `tools/fps-arms` (`art/arms/provenance.json`). Without the import the game falls
back to the generated arms. `scripts/import-hotel-held.sh` copies rusty-hotel's coach gun into the ignored
`content/old-art/hotel/` (re-run it after `scripts/import-old-art.sh`); without it the coach gun is drawn as its box.

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
| `src/ui/main.js` | DOM panel: scene status, problems, what is underfoot, controls |
| `content/levels/` | The old generator's chunks, layouts, styles, tilesets and tile templates as JSON |
| `content/level.json`, `content/walker.json`, `content/gallery.json` | Which level to stamp, walker tuning, gallery exhibits |
| `content/old-art/` | Generated, ignored: converted GLBs and placement files |
| `scripts/import-old-art.sh` | Regenerates `content/old-art/` with asset-pipeline unity-import |
| `scripts/texture-gen/` | Generated textures: tiling and normal maps, checks, contact sheets and judge merging, LoRA dataset, captions and training through ComfyUI |
| `scripts/review/` | A LAN web app for a human pass over an image folder (flag, keep, note, crop), and applying its results |
| `docs/texture-style.md`, `.claude/skills/texture-batch/` | House texture style, judging rubric, and the generate–filter–judge workflow |
| `content/art/textures/` | Generated textures, with their sources in `sources.json` |
| `scripts/extract-level-data.py` | Regenerates `content/levels/` from `old-game/` |
| `Directory.Build.props` | Matched Engine SDK/runtime pin |
| `docs/architecture.md` | Current ownership and data flow |
| `docs/direction.md` | Game direction, scale of the old kit, what the old art is worth, backups |
| `docs/held-format-convergence.md` | The held weapons' format against rusty-hotel's: what each expresses, conflicting conventions, the upstream shape |
| `docs/design.md` | Game design: pillars, run loop, gameplay time, combat, items, prototype scope |
| `docs/reuse.md` | Sibling repos as one-time code donors: where to look, copying procedure, incorporated provenance |
| `docs/levels.md` | Level data, generation, palettes and the old source |
| `docs/ui.md` | DOM companion contract |
| `docs/agent-review/` | Reusable review workflow and lane packets |

The SDK generates the product's bind entry point inside its ordinary build;
there is no composition project. The Engine runtime supplies the host and
browser shell. Product metadata, input intents, content/UI
roots, and projection identity live in the ordinary `.csproj`.

Read [AGENTS.md](AGENTS.md) before extending the product. Keep instructions
about current behavior and ownership; exact dependency identities belong in
configuration, and task status belongs in the task system.
