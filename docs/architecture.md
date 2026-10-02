# Product architecture

> The product decides. The Engine guarantees.

```text
content/gallery.json + content/old-art/ (converted GLBs, placement files)
  -> gallery layout, walker and HUD facts (C#)
  -> Rusty.Engine safe SDK
  -> SDK-generated bind entry point and ABI
  -> packaged Rust host, input, renderer, UI transport, and browser shell
  -> DOM companion
```

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/RustyRiders.Game/RustyRidersProduct.cs` | Lifecycle callbacks, per-update composition: input, walker steps, HUD publication |
| `src/RustyRiders.Game/Gallery/GalleryDefinition.cs` | Typed `content/gallery.json`: rows of exhibits, spacing, colours, walker tuning |
| `src/RustyRiders.Game/Gallery/PlacementFile.cs` | Typed asset-pipeline placement files and each row's transform |
| `src/RustyRiders.Game/Gallery/GalleryScene.cs` | Opening each converted GLB once, exhibit layout, appearance facts, ground and its collision, load problems |
| `src/RustyRiders.Game/Player/Walker.cs` | First-person controls, character steps, free flight and camera |
| `src/RustyRiders.Game/Ui/GalleryHud.cs` | HUD facts for the DOM panel |
| `src/ui/main.js` | DOM panel: status, problems, nearest exhibit, position, controls |
| `scripts/import-old-art.sh` | Regenerating `content/old-art/` from `old-game/` with asset-pipeline `unity-import` |
| Engine SDK/runtime | Generated interop, admitted updates/input, GLB admission and drawing, character collision, camera, UI transport, host and browser shell |

## Data flow

`scripts/import-old-art.sh` runs asset-pipeline's `unity-import` over the old
game's `GameData/Combat/Tilesets` and `GameData/Combat/Levels` prefabs. It
writes self-contained GLBs, per-mesh part GLBs and one placement file per
prefab (glTF-space transforms, checked by the Engine's `rusty asset check`) to
the ignored `content/old-art/`. The placement files are source evidence of
where the old game put things, not a level format for this game.

At construction the product reads `gallery.json` and, for each exhibit, its
placement file through Engine Content. `GalleryScene` opens each distinct GLB
once with `Animation.OpenAnimatedMesh` (static GLBs use the same admission),
creates one appearance per GLB, and publishes one appearance fact per
placement. Each exhibit keeps its prefab's own arrangement and is moved as a
whole so its lowest point rests on the ground, beside the previous exhibit in
its row. A missing placement file or a refused GLB is listed as a problem and
skipped; with no converted art the scene is just the ground.

Only the ground collides: one quad in a spatial session. The Engine has no
collision from GLB resources, and level collision waits for the game's level
design, so the walker's flight mode is how raised floors are seen.

Each admitted update, `Walker` reads Engine `FpsInput` and `Look`, then
proposes one Engine character step per admitted fixed step (or moves freely
when flying), and samples its camera. The product publishes HUD facts; the DOM
shows them and holds no state.

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
