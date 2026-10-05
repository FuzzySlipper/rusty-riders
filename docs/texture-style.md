# Texture style and judging

What generated textures for Rusty Riders should look like, and the rubric vision judges score them with.
The style comes from the old Unity game's level textures. Their source is in the ignored `old-game/`
(`Art/Models/GridMaps/*`); the Cave's `wall01` is the reference used so far. Generated textures and how they
were made are in `content/art/textures/` (`sources.json` per set). The batch workflow is in the
`texture-batch` project skill.

## House style

- **Hand-painted toon.** Bold dark ink outlines around every form, flat cel-shaded fills, and a soft pale inner
  highlight on rounded forms. No photographic detail, film grain or realistic noise.
- **Biomechanical and organic.** Rounded cells, ribs, blisters and membranes rather than square masonry or
  geometric tiles. Rock reads as smooth rounded stones in the same ink style.
- **Neutral values for tinting.** Mid greys, near-black lines and light-grey highlights. Level palettes tint
  materials, so a texture should carry value and form, not hue.
- **Readable at a walking distance.** Forms about 0.3–1.5 m across at the texture's intended repeat length (one
  tile is 4–6 m). Detail much finer than that turns to noise in first person.

## What makes a good tiling texture

- **Seamless.** No visible line or step where the tile meets itself, and no smeared or ghosted band where a
  seam was repaired.
- **No grid.** Nothing in the image repeats inside it, and nothing is distinctive enough to stand out each time
  the tile repeats: no single large feature, odd blob or bright spot. Lay it out 2x2 and the eye should not find
  the tile.
- **Even.** Density and brightness are spread evenly to the edges. There is no focal point, no vignette and no
  lighting from one side.
- **Clean.** No text, letters, frames, borders, watermarks or perspective.

## Judging

**Checks come first.** `scripts/texture-gen/checks.py` measures seams, in-image repetition, lighting gradients
and colour, and candidates it flags are dropped before any vision judge sees them. Vision judges are not
trusted with tiling. In calibration, Haiku reported seams on textures that measure seamless, and passed one with
a visible repaired band.

**Judges rank style and appeal.** A judge sees one contact sheet of candidates, each shown once and lettered,
next to the style reference. It ranks them best to worst as a floor for that place, by:
- **Style:** how unmistakably the house style it is (ink outlines, cel fills, soft highlights, organic or
  biomechanical forms).
- **Fit:** whether it would sit well with the reference as the same world's floor.
- **Scale:** whether its forms read at a 4–6 m repeat seen from eye height (1.7 m).
- **Appeal:** whether you would want to walk on it.

The judge replies with JSON:

```json
{"ranking": ["B", "A", "C"], "why": {"A": "...", "B": "...", "C": "..."}}
```

The judges' rankings are merged by mean rank across shuffled orders. Calibration results are in Den #9420.
