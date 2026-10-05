#!/usr/bin/env python3
"""Contact sheets for vision judges: candidates as labelled 2x2 tilings, so seams and repeats show.

    scripts/texture-gen/contact_sheet.py <out-dir> <image.png>... [--per-sheet 9] [--seed 1] [--cell 384] [--tile 2]

Candidates are shuffled (by --seed, so a second judge can see another order), split into sheets of up to
--per-sheet, and laid out in a square-ish grid. Each candidate is shown tiled --tile x --tile (1 for the image
alone) in a --cell square with a large letter label. Writes sheet-N.png and key.json (sheet -> letter -> image path) to the out dir.
"""
import argparse
import json
import math
import os
import random

from PIL import Image, ImageDraw, ImageFont

LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"


def tiled(path: str, cell: int, repeats: int) -> Image.Image:
    part = cell // repeats
    tile = Image.open(path).convert("RGB").resize((part, part), Image.LANCZOS)
    out = Image.new("RGB", (cell, cell))
    for x in range(repeats):
        for y in range(repeats):
            out.paste(tile, (x * part, y * part))
    return out


def font(size: int) -> ImageFont.ImageFont:
    for name in ("DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("out_dir")
    parser.add_argument("images", nargs="+")
    parser.add_argument("--per-sheet", type=int, default=9)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--cell", type=int, default=384)
    parser.add_argument("--tile", type=int, default=2, help="show each candidate tiled this many times per side")
    args = parser.parse_args()
    images = list(args.images)
    random.Random(args.seed).shuffle(images)
    os.makedirs(args.out_dir, exist_ok=True)
    label = font(args.cell // 7)
    key = {}
    gap = 12
    for number, start in enumerate(range(0, len(images), args.per_sheet), 1):
        batch = images[start:start + args.per_sheet]
        columns = math.ceil(math.sqrt(len(batch)))
        rows = math.ceil(len(batch) / columns)
        sheet = Image.new("RGB", (columns * (args.cell + gap) + gap, rows * (args.cell + gap) + gap), (255, 0, 255))
        draw = ImageDraw.Draw(sheet)
        names = {}
        for index, path in enumerate(batch):
            x, y = gap + (index % columns) * (args.cell + gap), gap + (index // columns) * (args.cell + gap)
            sheet.paste(tiled(path, args.cell, args.tile), (x, y))
            letter = LETTERS[index]
            draw.rectangle((x, y, x + args.cell // 5, y + args.cell // 5), fill=(255, 255, 255))
            draw.text((x + args.cell // 40, y), letter, fill=(200, 0, 0), font=label)
            names[letter] = path
        name = f"sheet-{number}.png"
        sheet.save(os.path.join(args.out_dir, name))
        key[name] = names
    json.dump(key, open(os.path.join(args.out_dir, "key.json"), "w"), indent=1)
    print(json.dumps({sheet: list(names) for sheet, names in key.items()}))


if __name__ == "__main__":
    main()
