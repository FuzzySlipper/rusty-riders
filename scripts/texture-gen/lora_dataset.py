#!/usr/bin/env python3
"""Build a style-LoRA training set from the old game's textures (ignored output; old-game/ is the source).

    scripts/texture-gen/lora_dataset.py <out-dir> [--size 1024] [--seed 1] [--gold IMAGE=CAPTION ...] [--trigger "rrink style"]

Sources are the old level and combat textures in the hand-painted ink-toon style (bold dark outlines, flat
cel fills): the Cave wall, Lava rock and lava, both Forest sets, and the combat tile atlases and cube maps.
The Palace marble, StoneRoad photo-stone and CyberTube sci-fi textures are a different style and are left out
so the LoRA learns one look.

Each source gives square crops: a tiling texture its whole image (or four quadrants when it is 2048 px or
more); an atlas or cube map random crops, rejecting any with more than a little flat, unpainted background. Crops are resized to --size and saved as PNGs named after their source, ready for captioning
(scripts/texture-gen/caption.py). Each --gold is a curated on-target example (a chosen generated texture):
it is added whole and as a zoomed quadrant, already captioned "<trigger>, <caption>", with any flat frame
trimmed off first (checks.frame_box).
"""
import argparse
import json
import os
import random

import numpy as np
from PIL import Image

from checks import frame_box

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "old-game", "Assets", "Art", "Models")

# (path under Art/Models, kind, crops): kind is "tile" (a whole tiling texture) or "atlas" (painted pieces on a
# flat background, sampled by random crops that are mostly painted).
SOURCES = [
    ("GridMaps/CaveMap/Model/Materials/wall01.png", "tile", 2),
    ("GridMaps/LavaMap/Model/Materials/lava01.png", "tile", 1),
    ("GridMaps/LavaMap/Model/Materials/lavarock01_alb.png", "tile", 2),
    ("GridMaps/LavaMap/Model/Materials/lavarock01_Wall_alb.png", "tile", 2),
    ("GridMaps/SadForest/Model/Materials/grass01_alb.png", "tile", 1),
    ("GridMaps/SadForest/Model/Materials/ground01_alb.png", "tile", 4),
    ("GridMaps/SadForest/Model/Materials/tree01_alb.png", "atlas", 2),
    ("GridMaps/ForestMap/Model/NewTextures/ground01.png", "tile", 1),
    ("GridMaps/ForestMap/Model/NewTextures/ground02.png", "tile", 1),
    ("GridMaps/ForestMap/Model/NewTextures/grass01.png", "tile", 1),
    ("GridMaps/ForestMap/Model/NewTextures/tree01.png", "tile", 1),
    ("CombatEnvironment/Lava/CT_LavaMap_Wall_Full_Rocks.png", "atlas", 6),
    ("CombatEnvironment/Palace/CT_UngrdPalace_Wall_Full.png", "atlas", 6),
    ("CombatEnvironment/Tube/TubeMap_Wall_FlatBack.png", "atlas", 4),
    ("CombatEnvironment/SadForest/CT_SadForest_Ground_1.png", "atlas", 2),
    ("CombatEnvironment/SadForest/CT_SadForest_Ground_2.png", "atlas", 2),
    ("CombatEnvironment/SadForest/CT_SadForest_Wall_1.png", "atlas", 2),
    ("CombatEnvironment/Textures/Tile_Cube_KOTG_Ground_1.png", "atlas", 2),
    ("CombatEnvironment/Textures/Tile_Cube_KOTG_Wall_1.png", "atlas", 2),
    ("CombatEnvironment/Textures/Tile_cube_UV_Ground.png", "atlas", 2),
]
FLAT_BLOCK = 16
FLAT_DEVIATION = 2.5
MAX_FLAT = .08


def flat_share(crop: np.ndarray) -> float:
    """The share of FLAT_BLOCK-pixel blocks with almost no variation: an atlas's unpainted background."""
    grey = crop.astype(np.float32).mean(axis=-1)
    h, w = (grey.shape[0] // FLAT_BLOCK) * FLAT_BLOCK, (grey.shape[1] // FLAT_BLOCK) * FLAT_BLOCK
    blocks = grey[:h, :w].reshape(h // FLAT_BLOCK, FLAT_BLOCK, w // FLAT_BLOCK, FLAT_BLOCK)
    return float((blocks.std(axis=(1, 3)) < FLAT_DEVIATION).mean())


def atlas_crops(image: Image.Image, count: int, rng: random.Random) -> list[Image.Image]:
    pixels = np.asarray(image.convert("RGB"))
    side = min(image.size) // 5
    crops = []
    for _ in range(2000):
        if len(crops) == count:
            break
        size = rng.randint(side, side * 2)
        x, y = rng.randint(0, image.width - size), rng.randint(0, image.height - size)
        crop = pixels[y:y + size, x:x + size]
        if flat_share(crop) <= MAX_FLAT:
            crops.append(Image.fromarray(crop))
    return crops


def tile_crops(image: Image.Image, count: int) -> list[Image.Image]:
    if count == 1 or min(image.size) < 2048:
        return [image.convert("RGB")]
    half = image.width // 2
    return [image.convert("RGB").crop((x, y, x + half, y + half)) for x in (0, half) for y in (0, half)][:count]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("out_dir")
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--gold", action="append", default=[], metavar="IMAGE=CAPTION")
    parser.add_argument("--trigger", default="rrink style")
    args = parser.parse_args()
    rng = random.Random(args.seed)
    os.makedirs(args.out_dir, exist_ok=True)
    manifest = []
    for path, kind, count in SOURCES:
        image = Image.open(os.path.join(ROOT, path))
        crops = atlas_crops(image, count, rng) if kind == "atlas" else tile_crops(image, count)
        stem = os.path.splitext(path.replace("/", "_"))[0]
        for index, crop in enumerate(crops):
            name = f"{stem}_{index}.png"
            crop.resize((args.size, args.size), Image.LANCZOS).save(os.path.join(args.out_dir, name))
            manifest.append({"image": name, "source": path, "kind": kind})
        if len(crops) < count:
            print(f"{path}: only {len(crops)} of {count} crops were mostly painted")
    for gold in args.gold:
        path, caption = gold.split("=", 1)
        image = Image.open(path).convert("RGB")
        if (box := frame_box(image)) is not None:
            image = image.crop(box)  # a comic-panel frame would teach the LoRA to draw frames
        stem = "gold_" + os.path.splitext(os.path.basename(path))[0]
        half = image.crop((0, 0, image.width // 2, image.height // 2))
        for name, crop, text in ((stem, image, caption), (stem + "_zoom", half, "close-up of " + caption)):
            crop.resize((args.size, args.size), Image.LANCZOS).save(os.path.join(args.out_dir, name + ".png"))
            with open(os.path.join(args.out_dir, name + ".txt"), "w") as out:
                out.write(f"{args.trigger}, {text}\n")
            manifest.append({"image": name + ".png", "source": path, "kind": "gold"})
    json.dump(manifest, open(os.path.join(args.out_dir, "manifest.json"), "w"), indent=1)
    print(f"{len(manifest)} images in {args.out_dir}")


if __name__ == "__main__":
    main()
