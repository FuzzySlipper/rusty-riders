# Authoring

Everything that makes the game's content and is not itself loaded by the game. The product stages `content/`;
nothing here is staged. Generic image, mesh and texture tooling stays in the asset-pipeline checkout
(`../asset-pipeline`); what is specific to Rift Riders is here. Each tool's usage is in its own header.

```
authoring/
  tools/        tracked
    old-game/     converters from the old game: its art (asset-pipeline unity-import), level data and UI sprites
    textures/     generated textures: Z-Image/Qwen generation, tiling, checks, judging, LoRA datasets and training
    held/         first-person arms and held models: imports, held-motion bakes, arm clip packs (arm-clips/)
    review/       a LAN web app for a human pass over an image folder, and applying its marks
    assets-sync.sh
  assets/       ignored but for their text, backed up to the den NAS; mirrors tools/ by area
    old-game/          the old Unity project (Assets/, ProjectSettings/, ...): the donor source, ignored whole
    old-game-fbx2013/  upgraded FBX copies of its models, unity-import's overlay, ignored whole
    textures/          generated candidates not in use (each set's sources.json), GPT gold examples, lora/ training sets
    held/              arms recipes and provenance (arms/), held-motion bake recipe (motion/), arms screenshots (lineup/)
    enemies/           playtest screenshots
```

Three kinds of art, three homes:

| Kind | Home | In git |
| --- | --- | --- |
| Old source assets | `authoring/assets/old-game/`, `old-game-fbx2013/` | No; NAS through `assets-sync.sh` |
| Intermediate generated assets (candidates, training sets, references, screenshots) | `authoring/assets/<area>/` | Their text only (`*.json`, `*.md`, `*.txt`); the rest on the NAS |
| Final assets the game draws | `content/art/` | Each folder's `sources.json`; the art through `scripts/art-sync.sh` |

## Assets

- After adding or changing assets, back them up: `authoring/tools/assets-sync.sh push` (adds and updates on
  `/mnt/den-nas-backup/data/rusty-riders/assets`, deletes nothing). A fresh checkout fills them with
  `authoring/tools/assets-sync.sh pull`; only the tools here need them, not the game.
- A finished file the game loads goes to `content/art/`, with its provenance in that folder's `sources.json`, and
  `scripts/art-sync.sh push`. Candidates that lose stay here under the same area, with their own `sources.json`.

## Running tools

- Run tools from the repository root; they find the root from their own place and take their inputs and outputs as
  arguments.
- Blender tools: `blender -b --factory-startup --python authoring/tools/<area>/<tool>.py -- ...`.
- Older Den tasks and documents name tools by their earlier homes: `scripts/texture-gen/` is now
  `authoring/tools/textures/`; `scripts/review/` is `authoring/tools/review/`; `scripts/import-old-art.sh`,
  `prune-old-art.py`, `extract-level-data.py` and `import-ui-art.py` are in `authoring/tools/old-game/`;
  `scripts/import-fp-arms.sh`, `build-fp-arms.py`, `import-hotel-held.sh`, `bake-held-motion(s)` and `arm-clips/` are
  in `authoring/tools/held/`. The old game moved from `old-game/` to `authoring/assets/old-game/`, `art/arms/` to
  `authoring/assets/held/arms/`, `art/held-motion/` to `authoring/assets/held/motion/`, and `artifacts/` to
  `authoring/assets/` (`textures/lora/`, `held/lineup/`, `enemies/playtest/`).
