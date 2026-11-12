# Art licences

This repository is public. The art the game draws lives here in `content/art/`, which git ignores apart from this
file and each folder's `sources.json`; `scripts/art-sync.sh` backs it up to the den NAS.

## The old game's art

The old Unity game (`authoring/assets/old-game/`, `authoring/assets/old-game-fbx2013/`) mixes two kinds of art:

- **Original work.** All its textures, sprites, UI art and icons were made for it, including the textures on
  store meshes: its artist painted entirely new ones in Substance Painter rather than editing the packs'. The painted
  arms' hand-painted texture is one of these. Original art may be used, changed and committed freely, as the UI art
  under `src/ui/art/` is.
- **Unity Asset Store purchases: the meshes and the animations that came with them.** That covers the first-person
  arms' meshes and clips (`fp-arms/`: painted, leather, and the MedievalFPArms and FirstPersonArms packs), and the
  meshes of the converted levels, tilesets and props. The Asset Store's standard licence allows embedding them in a
  game built in another engine; the one limit is that the raw assets may not be distributed.

So a store mesh or clip may ship inside the game but must never be committed to a public repository, Git LFS
included. A file derived from one is still the asset: a GLB or clip pack converted from it stays under
`content/art/` or another ignored folder, even when it also carries original textures. This settles the question;
it does not need confirming again before a release. Held motion baked from a store clip (`authoring/tools/held/bake-held-motion.py`:
a few weapon keys in `content/held`) is the game's own data, not the clip, and is committed.

## Other sources

- Mixamo clips and PINOC library clips: usable in the game, not redistributed as files, so they and their packs stay
  out of git (`content/art/arms/sources.json`).
- CC0 (UAL, the PSX arms) and Patch's own work: may be committed, as under `content/held/models/`.
- Each folder's `sources.json` records where its files came from and on what terms.
