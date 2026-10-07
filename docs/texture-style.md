# Texture style and judging

What generated textures for Rusty Riders should look like, and the rubric vision judges score them with.
The style comes from the old Unity game's level textures. Their source is in the ignored `old-game/`
(`Art/Models/GridMaps/*`); the Cave's `wall01` is the reference used so far. Generated textures and how they
were made are in `content/art/textures/` (`sources.json` per set). The batch workflow is in the
`texture-batch` project skill.

## House style

- **Hand-painted ink, not vector clip art.** Dark ink outlines around forms with cel-shaded fills, but painted
  rather than flat: tone varies inside a form, and crack lines, hatching and ink detail carry texture. A user
  review (2026-10-06, below) rejected clean flat vector-like fills even where the judges ranked them first.
  Avoid photographic detail, film grain and realistic noise too.
- **Organic forms.** Rounded stones, cracks, ribs and roots rather than square masonry or geometric tiles.
  Busy fields of oval "cells" were rejected in every version, so treat that motif as off-style for floors.
- **Neutral values for tinting.** Mid greys, near-black lines and light-grey highlights. Level palettes tint
  materials, so a texture should carry value and form, not hue. Coloured subjects such as lava and moss keep
  their colour.
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
- **Style:** how unmistakably the house style it is (hand-painted ink outlines and detail over cel fills,
  organic forms). Clean flat vector fills are a fault, not a strength.
- **Fit:** whether it would sit well with the reference as the same world's floor.
- **Scale:** whether its forms read at a 4–6 m repeat seen from eye height (1.7 m).
- **Appeal:** whether you would want to walk on it.

The judge replies with JSON:

```json
{"ranking": ["B", "A", "C"], "why": {"A": "...", "B": "...", "C": "..."}}
```

The judges' rankings are merged by mean rank across shuffled orders. Calibration results are in Den #9420.

## House style LoRA

`rrink_zimage_v3_seg6_3000_steps_00001_.safetensors` is the house Z-Image base LoRA for the ink-toon family:
- **Trained on:** 43 crops of the old Cave, Lava, Forest and combat textures, plus six GPT gold examples (two
  Cave floors, a Lava floor, a forest floor, a ribbed cave wall and an eerie forest clearing), each whole and
  zoomed. The Palace, StoneRoad and CyberTube textures are a different style and were left out.
- **Settings:** 1024 px, rank 32, learning rate 3e-4, 3000 steps, on den-nimo (about 11.5 h).
- **Trigger:** "rrink style".
- **Copies:** den-nimo, den-m5 and the 5090.

It gives consistent flat cel fills with ink outlines on cave rock and cells, lava and forest ground. Sonnet
judges tied its best Cave rock floor with the GPT gold rock and placed it above the GPT cells, at 18 s an image.

Earlier runs:

| Run | Settings | Result |
| --- | --- | --- |
| v1 | rank 16, learning rate 1e-4, 1500 steps | Barely moved anything but stone |
| v2 | 768 px, two golds, 2000 steps | Worked, but its best floors now rank last against v3 |
| v3 at 1500–2000 steps | as above | Uneven: sepia casts, and a forest that slid back toward photos |

The comparisons are in Den #9420, and the tools are in `scripts/texture-gen/` (`lora_dataset.py`, `caption.py`,
`train_lora.py`).

## Human review calibration

The user's review pass (2026-10-06, review app) is the ground truth the judges are checked against. It covered
48 floors: v2-2000 and v3 at 1500, 2000 and 3000 steps, over cave cells, cave rock, lava and forest.

| Set | Rejected |
| --- | --- |
| v2-2000 | 4/12 |
| v3-1500 | 10/12 |
| v3-2000 | 8/12 |
| v3-3000 | 10/12 |
| Cave cells, all versions | 12/12 |

- **Kept:** all three v2 cave rocks; the painterly v2, v3-1500 and v3-2000 lava; most forest grounds.
- **Rejected:** v3-3000's clean flat rock and lava, which Sonnet judges had ranked level with the GPT golds.

So the judges over-rewarded flatness, and the house style above now says so. Recheck the judges against these
marks before trusting their rankings again.

## House prompt

For Z-Image base with the house LoRA (v2 at 1.0 or v3 at 0.75), the user's second review (2026-10-07, 24 images)
kept every image with the Moebius wording (12/12) and fewer than half of the plain ones (5/12):

| Prompt | v2 at 1.0 | v3 at 0.75 |
| --- | --- | --- |
| Moebius wording | 6/6 kept | 6/6 kept |
| Plain | 3/6 kept | 2/6 kept |

Use this template:

```text
rrink style, hand-inked in the manner of Moebius (Jean Giraud), 1970s European science-fiction illustration,
clean ink linework with fine hatching and painted cel tones, flat top-down view of <subject>, filling the whole image
```

with this negative prompt (`generate.py --negative`):

```text
text, letters, words, title, signature, watermark, border, frame, panel, margin, horizon, sky, perspective,
landscape, photograph, photorealistic, 3d render
```

What the wording does:
- **Moebius / Jean Giraud** is known to Z-Image's Qwen3 text encoder and pulls toward hand-inked hatching and
  painted tones.
- **Generic eras** ("1970s pulp", "book cover") also enrich the image, but paint titles and lettering.
- **The negative prompt** removes text and signatures. Comic-panel frames (3 of 12) and perspective views
  (seed-dependent) still slip through: crop frames, and drop perspective views before tiling.

