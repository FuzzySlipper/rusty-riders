#!/usr/bin/env python3
"""Draw tileable surface layouts for a ControlNet: white ink lines on black that wrap at every edge.

    authoring/tools/textures/layouts.py <out.png> --kind stones|slabs|tiles|flagstones|runner [--size 1024] [--seed 1] [--cells 40]

- stones: irregular rounded stones (a wrapping Voronoi diagram with its cells shrunk and rounded), for cave or
  riverbed floors.
- slabs: a few large broken slabs with gravel between them (two Voronoi scales).
- tiles: square floor tiles in a grid with an inlay border inside each and random cracks.
- flagstones: rectilinear running-bond courses of varied rectangular cut stone slabs with staggered vertical joints.
- runner: a vertically-wrapping carved stone strip with straight left/right border curbs and geometric runic inlay.

The ControlNet (Z-Image Fun Union, scribble/HED-like lines) keeps the composition a flat surface; the style LoRA
and prompt paint it. Do not combine --control with generate.py --seamless.
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


def flagstones(size: int, rng: np.random.Generator, count: int) -> np.ndarray:
    """Rectilinear running-bond cut flagstones wrapping at all four edges."""
    rows = max(3, int(round(np.sqrt(count * 0.75))))
    # Vary row heights subtly while summing exactly to size so top/bottom wrap cleanly.
    weights = rng.uniform(0.82, 1.18, rows)
    heights = weights / weights.sum() * size
    ys = np.concatenate(([0.0], np.cumsum(heights)))
    image = Image.new("L", (size, size))
    draw = ImageDraw.Draw(image)
    prev_joints: list[float] = []
    for r in range(rows):
        y0, y1 = float(ys[r]), float(ys[r + 1])
        h = y1 - y0
        # Horizontal course line (draw wrapped at 0 and size so both borders carry the joint).
        for oy in (-size, 0, size):
            if -6 <= y0 + oy <= size + 6:
                draw.line((0, y0 + oy, size, y0 + oy), fill=255, width=4)
        # Pick 3..5 varied slab widths per course, staggered away from the previous course's joints.
        cols = int(rng.integers(max(2, rows - 2), max(4, rows)))
        w_weights = rng.uniform(0.65, 1.45, cols)
        widths = w_weights / w_weights.sum() * size
        offset = float(rng.uniform(0, size))
        # Nudge offset if any joint lands too close to the row above.
        best_offset, best_min_dist = offset, -1.0
        for _ in range(12):
            cand = float(rng.uniform(0, size))
            joints = [(cand + x) % size for x in np.cumsum(widths)]
            if not prev_joints:
                best_offset = cand
                break
            min_d = min(min(abs(j - pj), size - abs(j - pj)) for j in joints for pj in prev_joints)
            if min_d > best_min_dist:
                best_min_dist, best_offset = min_d, cand
        xs = [(best_offset + x) % size for x in np.cumsum(widths)]
        prev_joints = xs
        for x in xs:
            for ox in (-size, 0, size):
                if -6 <= x + ox <= size + 6:
                    draw.line((x + ox, y0, x + ox, y1), fill=255, width=4)
                    # Occasional small corner bevel / chip at T-junction.
                    if rng.random() < 0.35:
                        ch = float(rng.uniform(8, 16)) * (size / 1024)
                        s = -1 if rng.random() < 0.5 else 1
                        draw.line((x + ox, y0 + ch, x + ox + s * ch, y0), fill=255, width=2)
        # Sparse delicate short cracks inside a few slabs (never long diagonal slashes).
        for _ in range(max(1, cols // 2)):
            if rng.random() < 0.55:
                cx = float(rng.uniform(0, size))
                cy = float(rng.uniform(y0 + h * 0.2, y1 - h * 0.2))
                cracks(draw, rng, size, cx - h * 0.3, cy - h * 0.3, h * 0.6, 1)
    return np.asarray(image, dtype=np.float32) / 255


def runner(size: int, rng: np.random.Generator) -> np.ndarray:
    """Vertically-wrapping carved stone floor runner with crisp straight border rails and geometric runic inlay."""
    image = Image.new("L", (size, size))
    draw = ImageDraw.Draw(image)
    s = size / 1024.0
    # Outer and inner border rail lines (parallel straight vertical rails along left and right edges).
    outer_l, inner_l = int(round(18 * s)), int(round(112 * s))
    inner_r, outer_r = size - inner_l, size - outer_l
    mid_l, mid_r = int(round(92 * s)), size - int(round(92 * s))
    for x, w in ((outer_l, 4), (mid_l, 2), (inner_l, 5), (inner_r, 5), (mid_r, 2), (outer_r, 4)):
        draw.line((x, 0, x, size), fill=255, width=max(1, int(round(w * s))))

    # Subtle stone joint lines across the border rails every quarter-tile (wraps vertically).
    for k in range(4):
        y = int(round((k + 0.15) * size / 4)) % size
        draw.line((outer_l, y, inner_l, y), fill=255, width=max(1, int(round(3 * s))))
        draw.line((inner_r, y, outer_r, y), fill=255, width=max(1, int(round(3 * s))))

    # Central channel: bold angular Moebius/alien geometric runic circuit linework wrapping cleanly at y=0 / y=size.
    def wline(x0: float, y0: float, x1: float, y1: float, w: int = 4) -> None:
        pw = max(1, int(round(w * s)))
        for oy in (-size, 0, size):
            draw.line((x0 * s, y0 * s + oy, x1 * s, y1 * s + oy), fill=255, width=pw)

    # Inner frame tracks flanking the central runes.
    wline(156, 0, 156, 1024, 3)
    wline(868, 0, 868, 1024, 3)

    # Modular repeating geometric glyph bands along the vertical axis (2 bands per 1024 tile, asymmetric yet balanced).
    cx = 512.0
    # Spine segments connecting across the top/bottom wrap at x=512 (and offset returns).
    wline(cx, 0, cx, 110, 5)
    wline(cx, 110, 310, 110, 5)
    wline(310, 110, 310, 260, 5)
    wline(310, 260, 440, 260, 5)
    wline(440, 260, 440, 180, 4)
    wline(440, 180, 710, 180, 5)
    wline(710, 180, 710, 360, 5)
    wline(710, 360, 560, 360, 5)
    wline(560, 360, 560, 470, 5)
    wline(560, 470, 240, 470, 5)
    wline(240, 470, 240, 330, 4)
    wline(240, 330, 410, 330, 4)

    # Small nested stepped boxes / sigils (evoking the angular glyph paths in the concept art).
    wline(210, 60, 420, 60, 4)
    wline(210, 60, 210, 200, 4)
    wline(210, 200, 265, 200, 4)
    wline(610, 70, 790, 70, 4)
    wline(790, 70, 790, 270, 4)
    wline(790, 270, 755, 270, 4)
    wline(620, 250, 620, 315, 4)
    wline(620, 315, 495, 315, 4)
    wline(210, 410, 360, 410, 4)
    wline(780, 420, 780, 560, 4)
    wline(780, 420, 650, 420, 4)

    # Lower half band (y = 512..1024), connecting back cleanly to (cx, 1024) == (cx, 0).
    wline(560, 470, 560, 580, 5)
    wline(560, 580, 730, 580, 5)
    wline(730, 580, 730, 740, 5)
    wline(730, 740, 580, 740, 5)
    wline(580, 740, 580, 660, 4)
    wline(580, 660, 310, 660, 5)
    wline(310, 660, 310, 860, 5)
    wline(310, 860, 470, 860, 5)
    wline(470, 860, 470, 945, 5)
    wline(470, 945, cx, 945, 5)
    wline(cx, 945, cx, 1024, 5)

    # Secondary glyph branches in lower half.
    wline(230, 540, 460, 540, 4)
    wline(230, 540, 230, 770, 4)
    wline(230, 770, 270, 770, 4)
    wline(400, 725, 400, 795, 4)
    wline(400, 795, 520, 795, 4)
    wline(640, 810, 800, 810, 4)
    wline(800, 810, 800, 960, 4)
    wline(800, 960, 610, 960, 4)
    wline(610, 960, 610, 895, 4)
    wline(210, 920, 390, 920, 4)
    wline(210, 920, 210, 985, 4)

    # Subtle transverse stone slab seams inside the runner channel so it reads as carved stone, not fabric.
    for y in (128, 384, 640, 896):
        wline(156, y, 210, y, 2)
        wline(814, y, 868, y, 2)

    return np.asarray(image, dtype=np.float32) / 255


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("out")
    parser.add_argument("--kind", choices=["stones", "slabs", "tiles", "flagstones", "runner"], required=True)
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
    elif args.kind == "flagstones":
        lines = flagstones(args.size, rng, args.cells)
    elif args.kind == "runner":
        lines = runner(args.size, rng)
    else:
        lines = tiles(args.size, rng, args.cells)
    Image.fromarray((np.clip(lines, 0, 1) * 255).astype(np.uint8)).convert("RGB").save(args.out)


if __name__ == "__main__":
    main()
