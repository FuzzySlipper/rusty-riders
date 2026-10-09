#!/usr/bin/env python3
"""Draw tileable surface layouts for a ControlNet: white ink lines on black that wrap at every edge.

    scripts/texture-gen/layouts.py <out.png> --kind stones|slabs|tiles [--size 1024] [--seed 1] [--cells 40]

- stones: irregular rounded stones (a wrapping Voronoi diagram with its cells shrunk and rounded), for cave or
  riverbed floors.
- slabs: a few large broken slabs with gravel between them (two Voronoi scales).
- tiles: square floor tiles in a grid with an inlay border inside each and random cracks.

The ControlNet (Z-Image Fun Union, scribble/HED-like lines) keeps the composition a flat surface; the style LoRA
and prompt paint it. Because the layout wraps, it composes with generate.py --seamless.
"""
import argparse

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


def wrapped_voronoi(size: int, points: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Nearest and second-nearest distances to points on a torus, per pixel (small images only)."""
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32)
    first = np.full((size, size), np.inf, np.float32)
    second = np.full((size, size), np.inf, np.float32)
    for px, py in points:
        dx = np.abs(xs - px)
        dy = np.abs(ys - py)
        d = np.hypot(np.minimum(dx, size - dx), np.minimum(dy, size - dy))
        second = np.where(d < first, first, np.minimum(second, d))
        first = np.minimum(first, d)
    return first, second


def cells(size: int, rng: np.random.Generator, count: int, gap: float, interiors: bool = False) -> np.ndarray:
    """Outlines of rounded cells: edges where the gap between nearest and second-nearest point is small (or, with
    interiors, the filled cells themselves)."""
    work = 256
    points = rng.uniform(0, work, (count, 2))
    first, second = wrapped_voronoi(work, points)
    border = second - first
    filled = (border > gap * work / 64).astype(np.float32)
    image = Image.fromarray((filled * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
    # Tile 3x3, blur and threshold there so rounding wraps, then keep the middle.
    big = Image.new("L", (size * 3, size * 3))
    for i in range(3):
        for j in range(3):
            big.paste(image, (i * size, j * size))
    big = big.filter(ImageFilter.GaussianBlur(size / 160)).point(lambda v: 255 if v > 128 else 0)
    if interiors:
        return np.asarray(big.crop((size, size, size * 2, size * 2)), dtype=np.float32) / 255
    edges = big.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(3))
    return np.asarray(edges.crop((size, size, size * 2, size * 2)), dtype=np.float32) / 255


def cracks(draw: ImageDraw.ImageDraw, rng: np.random.Generator, size: int, x0: float, y0: float, span: float, count: int) -> None:
    for _ in range(count):
        x, y = x0 + rng.uniform(.1, .9) * span, y0 + rng.uniform(.1, .9) * span
        angle = rng.uniform(0, 2 * np.pi)
        for _ in range(int(rng.integers(3, 7))):
            angle += rng.normal(0, .6)
            step = span * rng.uniform(.05, .12)
            nx, ny = x + np.cos(angle) * step, y + np.sin(angle) * step
            for ox in (-size, 0, size):
                for oy in (-size, 0, size):
                    draw.line((x + ox, y + oy, nx + ox, ny + oy), fill=255, width=2)
            x, y = nx, ny


def tiles(size: int, rng: np.random.Generator, count: int) -> np.ndarray:
    per_side = max(2, int(round(np.sqrt(count))))
    span = size / per_side
    image = Image.new("L", (size, size))
    draw = ImageDraw.Draw(image)
    for i in range(per_side):
        for j in range(per_side):
            x, y = i * span, j * span
            draw.rectangle((x, y, x + span, y + span), outline=255, width=5)
            inset = span * .14
            draw.rectangle((x + inset, y + inset, x + span - inset, y + span - inset), outline=255, width=2)
            inset2 = span * .2
            draw.rectangle((x + inset2, y + inset2, x + span - inset2, y + span - inset2), outline=255, width=1)
            cracks(draw, rng, size, x, y, span, int(rng.integers(0, 3)))
    return np.asarray(image, dtype=np.float32) / 255


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("out")
    parser.add_argument("--kind", choices=["stones", "slabs", "tiles"], required=True)
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--cells", type=int, default=40, help="stones or slabs per image; tiles per image for tiles")
    args = parser.parse_args()
    rng = np.random.default_rng(args.seed)
    if args.kind == "stones":
        lines = cells(args.size, rng, args.cells, gap=1.5)
    elif args.kind == "slabs":
        # Big slabs, and gravel only in the gaps between them; the same seed draws the slabs' outlines and interiors.
        slab_seed = int(rng.integers(1 << 30))
        outlines = cells(args.size, np.random.default_rng(slab_seed), max(4, args.cells // 4), gap=6)
        inside = cells(args.size, np.random.default_rng(slab_seed), max(4, args.cells // 4), gap=6, interiors=True)
        gravel = cells(args.size, rng, args.cells * 8, gap=1.5) * (1 - inside)
        lines = np.maximum(outlines, gravel)
    else:
        lines = tiles(args.size, rng, args.cells)
    Image.fromarray((np.clip(lines, 0, 1) * 255).astype(np.uint8)).convert("RGB").save(args.out)


if __name__ == "__main__":
    main()
