---
name: texture-batch
description: Generate a batch of tiling textures in the Rusty Riders house style and pick the best automatically. Generate with GPT image (best quality) or ComfyUI on the user's 5090 (fast batches), make them tile, filter them with deterministic checks, rank the survivors with parallel Sonnet vision judges, then show the user the top few. Use when a level, tileset or prop needs a new texture, or to compare generators or LoRAs.
---

# Texture batch: generate, filter, judge, pick

The house style and the judging rubric are in `docs/texture-style.md`. Read both sections before writing
prompts or judging. The tools are in `scripts/texture-gen/`. The 5090's ComfyUI is reached only through its
HTTP API (`comfy.py`, `COMFY_URL`, default `http://192.168.1.20:8188`); check `/queue` first and never
disturb the owner's own jobs.

## 1. Generate

Write one brief: what surface it is, where it sits, its scale (forms 0.3–1.5 m at a 4–6 m repeat), and
greyscale values for palette tinting. Attach a style reference; the Cave's is `wall01`.

| Route | When | How |
| --- | --- | --- |
| GPT image | Quality: hero textures, gold examples | `codex-image-gen` skill with the reference image attached. About 5 min each, so run several in the background with distinct output paths. |
| Qwen-Image 2.1 edit (5090) | Volume with a reference, 6–12 s each | API graph as in `content/art/textures/cave/sources.json` (`TextEncodeQwenImage21` with the reference as `images.image_1`), several seeds |
| Z-Image base + house LoRA | Volume in the ink-toon style, about 18 s each on the 5090 (110 s on a Strix Halo) | `generate.py --model zimage-base --lora rrink_zimage_v2_seg4_2000_steps_00001_.safetensors`, prompt starting "rrink style, top-down view of …" |
| Z-Image Turbo (5090) | Fast text-only drafts, 3 s each | 8 steps, `res_multistep`, shift 3 |

`scripts/texture-gen/generate.py` runs Z-Image (turbo or base, with or without a LoRA) and Qwen edit batches over
seeds and records provenance; point `COMFY_URL` at the 5090 (fastest when free), den-nimo (192.168.1.23) or
den-m5 (192.168.1.24). Never write "seamless" or "tileable" in a prompt; "tiling surface" also makes Z-Image
draw panel grids, while leaving it out drifts toward perspective photos, so prefer "top-down view of". 

## 2. Make them tile

```bash
scripts/texture-gen/make_tileable.py <raw.png> <albedo.png> <normal.png> --prompt "<what the texture shows>"
```

The script rolls the image by half a tile, has Qwen-Image 2.1 repaint the seam cross with the rolled image as its
reference, saves a greyscale 8-bit RGBA albedo (the Engine admits only RGBA PNGs), and derives a normal map.
GPT outputs are often nearly seamless already, but run them through it anyway.

## 3. Filter with checks

```bash
scripts/texture-gen/checks.py <albedo.png>... --json checks.json
```

Drop anything flagged `seam`, `repeats`, `gradient`, `colour` or `flat`. Vision judges are not trusted with
tiling: in calibration, Haiku reported seams on seamless textures and ranked a visibly seamed one first.

## 4. Judge

```bash
scripts/texture-gen/contact_sheet.py <out>/order1 <survivors>... --tile 1 --cell 560 --per-sheet 9 --seed 1
scripts/texture-gen/contact_sheet.py <out>/order2 <survivors>... --tile 1 --cell 560 --per-sheet 9 --seed 2
```

For every sheet, start one **Sonnet** judge per shuffled order, in parallel and in the background (Agent tool,
`model: sonnet`). Haiku was inconsistent across orders in calibration; Sonnet gave identical rankings. Use
this prompt:

> You are a texture judge for a stylized first-person game. Do only this: 1. Read
> /home/agent/dev/rusty-riders/docs/texture-style.md, sections "House style" and "Judging" (the part "Judges
> rank style and appeal"). 2. Look at the style reference: <reference path>. 3. Look at the candidates for
> <surface and place>: <sheet path> (lettered; tiling has already been checked, do not judge seams). Rank all
> of them best to worst as the doc says. Reply with ONLY the JSON object {"ranking": [...], "why": {...}}. No
> other text, no files.

Save each reply as JSON, then merge them:

```bash
scripts/texture-gen/merge_rankings.py <out>/order1=<judge1.json> <out>/order2=<judge2.json>
```

With more than one sheet, take each sheet's top three into a final sheet and judge that the same way.

## 5. Pick and land

Look at the top three yourself, in a contact sheet and, for a level surface, in game (the generated builds'
`floorTextures` and V key). Show the user the finalists. Add the winner under `content/art/textures/<set>/`,
record its brief, route, seed and reference in that folder's `sources.json`, and wire it into the recipe.

## Calibration

Five Cave floors (Den #9420). Sonnet over two shuffled orders ranked gpt-cells, qwen-cells, gpt-rock,
qwen-rock, zimage-rock. That matches the human ranking except that it swaps gpt-rock and qwen-cells, with a
defensible reason. Recalibrate when the rubric or judge model changes.
