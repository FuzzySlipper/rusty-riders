#!/usr/bin/env python3
"""Apply a review_server.py pass: list what survived, and cut out the marked crops.

    scripts/review/apply_review.py <image-dir> [--out DIR] [--keep-only] [--copy]

Reads <image-dir>/review.json. Prints the surviving images, one path per line: every image not rejected, or with
--keep-only only those marked keep. With --out, writes each crop box as <stem>_crop<N>.png there. With --copy as
well, it also copies the surviving images that have no crops, so --out holds the reviewed set.
"""
import argparse
import json
import os
import shutil
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from review_server import EXTENSIONS  # noqa: E402


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("directory")
    parser.add_argument("--out")
    parser.add_argument("--keep-only", action="store_true", help="survivors are only images marked keep")
    parser.add_argument("--copy", action="store_true", help="with --out, also copy survivors that have no crops")
    args = parser.parse_args()
    root = os.path.abspath(args.directory)
    state = json.load(open(os.path.join(root, "review.json")))["items"]
    images = sorted(os.path.relpath(os.path.join(d, n), root) for d, dirs, names in os.walk(root)
                    if not os.path.basename(d).startswith(".") for n in names if n.lower().endswith(EXTENSIONS))
    if args.out:
        os.makedirs(args.out, exist_ok=True)
    for path in images:
        entry = state.get(path, {})
        mark = entry.get("mark")
        if mark == "reject" or (args.keep_only and mark != "keep"):
            continue
        print(path)
        if not args.out:
            continue
        stem = os.path.splitext(path.replace(os.sep, "_"))[0]
        crops = entry.get("crops", [])
        if crops:
            image = Image.open(os.path.join(root, path))
            for number, (x, y, w, h) in enumerate(crops, 1):
                image.crop((x, y, x + w, y + h)).save(os.path.join(args.out, f"{stem}_crop{number}.png"))
        elif args.copy:
            shutil.copy(os.path.join(root, path), os.path.join(args.out, os.path.basename(path)))


if __name__ == "__main__":
    main()
