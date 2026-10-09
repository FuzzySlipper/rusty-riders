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

`rrink_zimage_v4_seg4_2000_steps_00001_.safetensors` (v4 at 2000 steps) is the house Z-Image base LoRA for the
ink-toon family, at strength 1.0 with the house prompt below:
- **Trained on:** the same 43 crops of the old Cave, Lava, Forest and combat textures as v3, plus the 26 images
  the user kept in the first two review passes (mostly v2 output with Moebius wording), each whole and zoomed:
  95 images. The Palace, StoneRoad and CyberTube textures are a different style and were left out.
- **Settings:** 1024 px, rank 32, learning rate 3e-4, 3000 steps in six checkpointed segments, on den-m5
  (about 10.5 h; a first attempt on den-nimo died when another workload ran the machine out of memory).
- **Trigger:** "rrink style".
- **Copies:** v4-2000 and v4-3000 on den-m5; every checkpoint in den-m5's ComfyUI `output/loras`.

The user's blind review (2026-10-08, 24 images over cave rock, lava rock, forest ground and palace floor, seeds
3–4) called it the best result so far. They marked their favourites and two tentative rejects:

| Set | Favourites | Rejected |
| --- | --- | --- |
| v4-2000 | 5/8 | 1/8 |
| v4-3000 | 3/8 | 0/8 |
| v2-2000 | 4/8 | 1/8 |

- **By subject:** forest ground 6/6 favourites, lava 4/6, cave rock 1/6, palace floor 1/6.
- **Rejects:** both are palace floors with a strong cast shadow, drawn like a scene rather than a surface. They
  would suit sprites; for textures, try "even flat lighting" in the prompt and "cast shadow, drop shadow" in
  the negative. Both were also flagged `perspective` by `checks.py`, which flagged six kept images too.
- **Frames:** seed 4 drew a thin ink border on most images, for every LoRA (`checks.py` flags them `framed`). The
  user did not mind them, but trim them (`checks.frame_box`) before making a texture tile.

Earlier runs:

| Run | Settings | Result |
| --- | --- | --- |
| v1 | rank 16, learning rate 1e-4, 1500 steps | Barely moved anything but stone |
| v2 | 768 px, two golds, 2000 steps | Hand-inked detail the user liked; still competitive with v4 |
| v3 at 1500–2000 steps | 1024 px, six GPT golds | Uneven: sepia casts, and a forest that slid back toward photos |
| v3 at 3000 steps | as above | Consistent flat cel fills; judges tied it with the GPT golds, the user rejected 10/12 |

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

For Z-Image base with the house LoRA (v4 or v2 at 1.0; earlier v3 at 0.75), the user's second review (2026-10-07, 24 images)
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

For surfaces that come out as scenes (architecture especially), add ", even flat lighting, no shadows" to the
prompt and "cast shadow, drop shadow, walls, pillars, steps, scene" to the negative. In a round on v4-2000
(2026-10-08, seeds 5–6, cave and palace wordings with and without it):
- **Palace floors:** without it, pillars, a cast shadow or an angled camera appeared in 3 of 4 images; with it, 1 of
  4 (a stray column). "Square stone tiles with carved inlay bands" draws one large centred panel, which would repeat
  visibly; "inlaid coloured stone in geometric patterns" gives a colourful mosaic that fills the frame better.
- **Cave floors:** "broken rock slabs and scattered pebbles" gives a flat ink surface. "Rough cave bedrock with
  deep fissures" draws a pit or tunnel mouth seen from above, and "damp cave floor with puddles and lichen" a dark
  scene: the word "cave" pulls toward scenes unless the subject is clearly a flat surface.

**Generate surfaces with `generate.py --seamless`** (den-m5's ComfyUI-Universal-Seamless-Tiles nodes: the model and
VAE wrap at the image edges). Without it, about 14 of 16 cave and palace images in the round above were scenes, by
the user's marks. In the next round (2026-10-09, 18 images, seeds 5–6, viewed tiled 2x2 in review):

| Variant | Favourites | Rejected |
| --- | --- | --- |
| House wording + seamless | 5/6 | 0/6 |
| "Seamless repeating floor texture asset … material swatch" + seamless | 3/6 | 0/6 |
| Texture-asset wording, no seamless | 2/6 | 2/6 |

- Every image was a surface, not a scene. The seamless images tile exactly as generated (seam 0.8–1.2), so they
  need no seam repaint.
- The texture-asset wording draws small wallpaper-like repeats and thinner ink. Keep the house wording.
- Both rejects tiled oddly in 2x2 without the patch, though they looked fine alone. Review surfaces tiled
  (`t` in the review app).

**ControlNet layouts** (`layouts.py` → `generate.py --control`, Z-Image Fun ControlNet Union 2.1 on both Strix
Halos). A strength sweep (2026-10-09, seed 5, house wording):
- **Alone, at 0.35–0.5:** follows the layout and keeps the ink. Best for regular structure: a carved tile grid at
  0.35 came out as clean, evenly repeating palace tiles. Stones and slabs also hold their ink at 0.35. The
  output does not wrap, so it needs the seam repaint (`make_tileable.py --model zimage-lora`).
- **At 0.8:** exact shapes, but pale flat fills with almost no ink.
- **Combined with `--seamless`:** 5 of 6 images dissolved into a featureless smear. The two patches conflict, so do
  not combine them.

So use `--seamless` alone for organic surfaces (rock, slabs, mosaics, ground), and ControlNet at about 0.35
followed by a seam repaint when the structure must be regular (tiles, panels, brick).

## Keep-or-reject judge

Sonnet judges pre-screen batches with `scripts/review/prompts/keep-reject-judge.txt`, one judge per contact
sheet of 12 (`contact_sheet.py --tile 1 --cell 400 --per-sheet 12`), replacing SHEET with the sheet's path.
`scripts/review/agreement.py` scores their verdicts against a human review pass.

On the 72 images of the two reviews above, the first version agreed with the user on 81% (58/72):
- it caught 30 of 39 rejects (all 12 cave-cells images);
- it wrongly rejected 5 of 33 keeps;
- it missed rejects mostly among lava and forest.

The rubric was written from these same reviews, so validate it on the next fresh batch before relying on it.
As a pre-filter, the judges drop the obvious rejects, and the user's pass decides.

**Fresh validation (v4 review, 2026-10-08): 62% (15/24), no better than chance on that batch.** The first
version caught neither of the two rejects (shadowed palace floors: shadows were not in its rubric) and rejected
7 of 22 keeps, three of them user favourites. On a strong batch where nearly everything is a keep, its reject
cues (flat fills, murk) mostly hit acceptable images.

The second version (the current prompt file) adds "a scene rather than a surface" (walls, pillars or steps
casting strong shadows) as a reject, and says a plain but hand-inked image is a keep, and to keep when unsure:

| Version | First 72 (two reviews) | v4 review (24) | All 96 |
| --- | --- | --- | --- |
| 1 | 81%: 30/39 rejects caught, 5/33 keeps rejected | 62%: 0/2 caught, 7/22 rejected | 73/96 |
| 2 | 78%: 25/39 caught, 2/33 rejected | 75%: 2/2 caught, 6/22 rejected | 74/96 |

Both rows are in-sample: version 2's shadow cue was written from the v4 review's two rejects. Version 2 is more
lenient, which suits a pre-filter, but neither version ranks a good batch: use it to drop obvious failures from
weak or mixed batches, and leave the choice among good images to the user's pass.
