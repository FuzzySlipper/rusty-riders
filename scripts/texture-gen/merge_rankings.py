#!/usr/bin/env python3
"""Merge vision judges' rankings across shuffled contact sheets into one ordering by mean rank.

    scripts/texture-gen/merge_rankings.py <sheet-dir>=<judge.json> [<sheet-dir>=<judge.json> ...]

Each argument pairs a contact_sheet.py output directory (its key.json maps letters to images) with a judge's
reply, a JSON file {"ranking": [...]} or {"sheet": "sheet-1.png", "ranking": [...]}. Images missing from a
ranking take the worst rank of that sheet. Prints images best first with their mean rank and how many judges saw them.
"""
import json
import os
import sys
from collections import defaultdict


def main() -> None:
    ranks: dict[str, list[float]] = defaultdict(list)
    for pair in sys.argv[1:]:
        sheet_dir, reply_path = pair.split("=", 1)
        key = json.load(open(os.path.join(sheet_dir, "key.json")))
        reply = json.load(open(reply_path))
        sheet = reply.get("sheet", next(iter(key)))
        letters = key[sheet]
        ranking = [letter for letter in reply["ranking"] if letter in letters]
        for letter, image in letters.items():
            position = ranking.index(letter) + 1 if letter in ranking else len(letters)
            ranks[image].append(position / len(letters))  # normalised, so sheets of different sizes compare
    merged = sorted(ranks.items(), key=lambda item: sum(item[1]) / len(item[1]))
    for image, values in merged:
        print(f"{sum(values) / len(values):.2f}  ({len(values)} judges)  {image}")


if __name__ == "__main__":
    main()
