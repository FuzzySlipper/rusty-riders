#!/usr/bin/env python3
"""Score keep/reject judges against a human review pass.

    scripts/review/agreement.py <sheet-dir> <verdict-dir> <reviewed-image-dir>... [--reviewer NAME]

With --reviewer NAME it reads each folder's review-NAME.json instead of review.json.
<sheet-dir> is a contact_sheet.py output (key.json maps sheet -> letter -> image path). <verdict-dir> holds one
judge reply per sheet, sheet-N.json = {"A": "keep" or "reject", ...}. Each reviewed image dir has the review_server.py
review.json for its images; an image not marked reject counts as a keep. Prints the agreement, how many human
rejects the judges caught, how many human keeps they rejected, and each disagreement.
"""
import collections
import json
import os
import sys


def main() -> None:
    args = sys.argv[1:]
    reviewer = None
    if "--reviewer" in args:
        at = args.index("--reviewer")
        reviewer = args[at + 1]
        del args[at:at + 2]
    sheet_dir, verdict_dir, *reviewed = args
    truth = {}
    for directory in reviewed:
        marks = json.load(open(os.path.join(directory, f"review-{reviewer}.json" if reviewer else "review.json")))["items"]
        for name in os.listdir(directory):
            if name.lower().endswith((".png", ".jpg", ".jpeg", ".webp")):
                truth[os.path.abspath(os.path.join(directory, name))] = "reject" if marks.get(name, {}).get("mark") == "reject" else "keep"
    key = json.load(open(os.path.join(sheet_dir, "key.json")))
    counts = collections.Counter()
    errors = []
    for sheet, letters in key.items():
        verdicts = json.load(open(os.path.join(verdict_dir, sheet.rsplit(".", 1)[0] + ".json")))
        for letter, path in letters.items():
            human, judge = truth[os.path.abspath(path)], verdicts[letter]
            counts[(human, judge)] += 1
            if human != judge:
                verb = "kept" if judge == "keep" else "rejected"
                errors.append(f"judge {verb} a human {human}: {os.path.basename(path)}")
    total = sum(counts.values())
    agree = counts[("keep", "keep")] + counts[("reject", "reject")]
    rejects = counts[("reject", "reject")] + counts[("reject", "keep")]
    keeps = counts[("keep", "keep")] + counts[("keep", "reject")]
    print(f"agreement {agree / total:.0%} ({agree}/{total}); human rejects caught {counts[('reject', 'reject')]}/{rejects}; "
          f"human keeps rejected {counts[('keep', 'reject')]}/{keeps}")
    for error in sorted(errors):
        print("  " + error)


if __name__ == "__main__":
    main()
