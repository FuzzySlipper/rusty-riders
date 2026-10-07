#!/usr/bin/env python3
"""Cheap, deterministic checks for candidate tiling textures, before any vision judge looks at them.

    scripts/texture-gen/checks.py <image.png>... [--json out.json]

Per image (luminance at 512 px):
- seam: the step across the wrap (last column to first, last row to first) over the median step between
  neighbouring columns and rows inside the image. Near 1 tiles seamlessly; well above 1 shows a seam.
- repeat: the strongest autocorrelation peak away from zero lag (beyond an eighth of the size), relative to
  zero lag. Near 1 means the image repeats itself inside the frame, which tiles into an obvious grid.
- gradient: the brightness difference across the image from a plane fitted to its heavily blurred
  luminance, in 0..1 luminance. A lit-from-one-side image shows as stripes when tiled.
- value: the luminance mean and 2..98% spread, and the mean saturation; palettes tint greyscale-ish
  textures, so strong colour fights the tint.
- frame: how far a flat border (a comic-panel frame or margin) reaches in from each edge, in pixels of the
  512 px copy; flagged when all four sides have one. frame_box() gives the crop that removes it.
- perspective: the trend of feature size across four bands, top to bottom and left to right (log of the mean
  gap between edges, per band). A top-down texture keeps its size; a perspective view shrinks toward a horizon.
  Calibrated on 24 reviewed images (2026-10-07): |slope| > 0.13 caught 6 of 7 oblique views with one false
  alarm (a lava floor, on its left-right trend); the miss was framed, and the frame check catches it.

Flags use the thresholds below; they are filters for obvious failures, not a taste verdict.
"""
import argparse
import json

import numpy as np
from PIL import Image, ImageFilter

SIZE = 512
SEAM_LIMIT = 2.0
REPEAT_LIMIT = .45
GRADIENT_LIMIT = .12
SATURATION_LIMIT = .25
FRAME_DEVIATION = .04
PERSPECTIVE_LIMIT = .13


def luminance(image: Image.Image) -> np.ndarray:
    return np.asarray(image.convert("L").resize((SIZE, SIZE), Image.LANCZOS), dtype=np.float32) / 255


def seam(lum: np.ndarray) -> float:
    inside = np.median(np.concatenate([np.abs(np.diff(lum, axis=1)).mean(axis=0), np.abs(np.diff(lum, axis=0)).mean(axis=1)]))
    across = (np.abs(lum[:, -1] - lum[:, 0]).mean() + np.abs(lum[-1, :] - lum[0, :]).mean()) / 2
    return float(across / max(inside, 1e-6))


def repeat(lum: np.ndarray) -> float:
    centred = lum - lum.mean()
    spectrum = np.fft.fft2(centred)
    auto = np.fft.fftshift(np.real(np.fft.ifft2(spectrum * np.conj(spectrum))))
    auto /= auto[SIZE // 2, SIZE // 2]
    y, x = np.mgrid[0:SIZE, 0:SIZE]
    far = np.hypot(x - SIZE // 2, y - SIZE // 2) > SIZE / 8
    return float(auto[far].max())


def gradient(image: Image.Image) -> float:
    blurred = np.asarray(image.convert("L").resize((64, 64), Image.LANCZOS).filter(ImageFilter.GaussianBlur(8)),
                         dtype=np.float32) / 255
    y, x = np.mgrid[0:64, 0:64] / 63
    a = np.stack([x.ravel(), y.ravel(), np.ones(64 * 64)], axis=1)
    (gx, gy, _), *_ = np.linalg.lstsq(a, blurred.ravel(), rcond=None)
    return float(np.hypot(gx, gy))


def saturation(image: Image.Image) -> float:
    hsv = np.asarray(image.convert("RGB").resize((SIZE, SIZE)).convert("HSV"), dtype=np.float32) / 255
    return float(hsv[..., 1].mean())


def frame(lum: np.ndarray) -> list[int]:
    """Flat rows or columns reaching in from the top, bottom, left and right edges (up to a quarter of the size)."""
    def run(stds: np.ndarray) -> int:
        k = 0
        while k < len(stds) // 4 and stds[k] < FRAME_DEVIATION:
            k += 1
        return k
    rows, columns = lum.std(axis=1), lum.std(axis=0)
    return [run(rows), run(rows[::-1]), run(columns), run(columns[::-1])]


def frame_box(image: Image.Image, margin: float = .015) -> tuple[int, int, int, int] | None:
    """The crop (left, top, right, bottom) that removes a frame on all four sides plus a small margin for its inner
    rule, or None when there is no frame."""
    top, bottom, left, right = frame(luminance(image))
    if min(top, bottom, left, right) == 0:
        return None
    scale_x, scale_y = image.width / SIZE, image.height / SIZE
    extra_x, extra_y = image.width * margin, image.height * margin
    return (round(left * scale_x + extra_x), round(top * scale_y + extra_y),
            round(image.width - right * scale_x - extra_x), round(image.height - bottom * scale_y - extra_y))


def perspective(image: Image.Image) -> float:
    """The steeper of the vertical and horizontal trends in log feature size across four bands."""
    lum = np.asarray(image.convert("L").resize((SIZE, SIZE), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1)),
                     dtype=np.float32) / 255
    gy, gx = np.gradient(lum)
    edges = np.hypot(gx, gy) > np.percentile(np.hypot(gx, gy), 80)

    def slope(mask: np.ndarray) -> float:
        sizes = []
        for band in np.array_split(mask, 4):
            gaps = []
            for row in ~band:
                bounds = np.flatnonzero(np.diff(np.concatenate(([0], row.astype(np.int8), [0]))))
                gaps.extend(bounds[1::2] - bounds[::2])
            sizes.append(np.mean(gaps) if gaps else 1.0)
        return float(np.polyfit(range(4), np.log(sizes), 1)[0])
    return max(abs(slope(edges)), abs(slope(edges.T)), key=abs)


def check(path: str, colour_ok: bool = False) -> dict:
    image = Image.open(path)
    lum = luminance(image)
    result = {
        "path": path,
        "seam": round(seam(lum), 2),
        "repeat": round(repeat(lum), 3),
        "gradient": round(gradient(image), 3),
        "mean": round(float(lum.mean()), 3),
        "spread": round(float(np.percentile(lum, 98) - np.percentile(lum, 2)), 3),
        "saturation": round(saturation(image), 3),
        "frame": frame(lum),
        "perspective": round(perspective(image), 3),
    }
    flags = []
    if result["seam"] > SEAM_LIMIT: flags.append("seam")
    if result["repeat"] > REPEAT_LIMIT: flags.append("repeats")
    if result["gradient"] > GRADIENT_LIMIT: flags.append("gradient")
    if result["saturation"] > SATURATION_LIMIT and not colour_ok: flags.append("colour")
    if result["spread"] < .2: flags.append("flat")
    if min(result["frame"]) > 0: flags.append("framed")
    if result["perspective"] > PERSPECTIVE_LIMIT: flags.append("perspective")
    result["flags"] = flags
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("images", nargs="+")
    parser.add_argument("--json", help="also write the results here")
    parser.add_argument("--colour-ok", action="store_true", help="the subject is meant to be coloured (lava, moss): no colour flag")
    args = parser.parse_args()
    results = [check(path, args.colour_ok) for path in args.images]
    for r in results:
        print(f"{r['path']}: seam {r['seam']} repeat {r['repeat']} gradient {r['gradient']} mean {r['mean']} "
              f"spread {r['spread']} saturation {r['saturation']} frame {r['frame']} perspective {r['perspective']} "
              f"{' '.join(r['flags']) or 'ok'}")
    if args.json:
        json.dump(results, open(args.json, "w"), indent=1)


if __name__ == "__main__":
    main()
