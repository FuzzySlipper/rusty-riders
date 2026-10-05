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


def check(path: str) -> dict:
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
    }
    flags = []
    if result["seam"] > SEAM_LIMIT: flags.append("seam")
    if result["repeat"] > REPEAT_LIMIT: flags.append("repeats")
    if result["gradient"] > GRADIENT_LIMIT: flags.append("gradient")
    if result["saturation"] > SATURATION_LIMIT: flags.append("colour")
    if result["spread"] < .2: flags.append("flat")
    result["flags"] = flags
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("images", nargs="+")
    parser.add_argument("--json", help="also write the results here")
    args = parser.parse_args()
    results = [check(path) for path in args.images]
    for r in results:
        print(f"{r['path']}: seam {r['seam']} repeat {r['repeat']} gradient {r['gradient']} mean {r['mean']} "
              f"spread {r['spread']} saturation {r['saturation']} {' '.join(r['flags']) or 'ok'}")
    if args.json:
        json.dump(results, open(args.json, "w"), indent=1)


if __name__ == "__main__":
    main()
