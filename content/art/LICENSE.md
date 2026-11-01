# Art licences

This repository is public. The art the game draws lives here in `content/art/`, which git ignores apart from this
file and each folder's `sources.json`; `scripts/art-sync.sh` backs it up to the den NAS.

## The old game's art

Everything taken from the old Unity game (`old-game/`, `old-game-fbx2013/`) is either Patch's original work or was
bought on the Unity Asset Store under its standard licence, which allows embedding the assets in a game built in
another engine. The one limit is that the raw assets may not be distributed. That covers the first-person arms
(`fp-arms/`: painted, leather), the converted levels, tilesets, props and combat art, and the UI art, including the
`OH_`-prefixed frames.

So these assets may be used anywhere in the game, shipped inside it and changed freely. They must never be committed
to a public repository, Git LFS included. Keep them under `content/art/` or another ignored folder. A derived file is
still the asset: a GLB, texture or clip pack converted from one stays out of git too. This settles the question; it
does not need confirming again before a release.

## Other sources

- Mixamo clips and PINOC library clips: usable in the game, not redistributed as files, so they and their packs stay
  out of git (`content/art/arms/sources.json`).
- CC0 (UAL, the PSX arms) and Patch's own work: may be committed, as under `content/held/models/`.
- Each folder's `sources.json` records where its files came from and on what terms.
