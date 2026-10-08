#!/usr/bin/env python3
"""Keep only the GLBs some placement file draws, plus named animated models, in a unity-import output; summarize it.

    prune-old-art.py OUT [--keep Assets/relative/Model.FBX ...]

Whole-model GLBs that exist only to cut per-mesh parts from, and the lower levels of Unity LODGroups (rows with
lod > 0, which the product does not draw), are dropped, because the product copies its content root into every
staged build. A `--keep` model (an enemy, whose NPC prefab places only static per-mesh parts) keeps its whole,
animated GLB. Placement rows are left as written. Used by scripts/import-old-art.sh.
"""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("out", type=Path)
parser.add_argument("--keep", action="append", default=[], help="Assets-relative model whose GLB is kept")
args = parser.parse_args()
out: Path = args.out

used = {row["glb"] for path in out.glob("placements/**/*.placements.json")
        for row in json.loads(path.read_text())["placements"]
        if row.get("glb") and not row.get("error") and not row.get("lod")}
reports = {report["source"]: report for path in out.glob("reports/**/*.report.json")
           if "output" in (report := json.loads(path.read_text()))}
kept = []
for source in args.keep:
    report = reports.get(source)
    if report is None or report.get("engineAdmission", {}).get("admitted") is False:
        raise SystemExit(f"prune-old-art: {source} did not convert to an admitted GLB; see its report under {out}/reports")
    kept.append(report["output"])
pruned = [glb for glb in out.glob("**/*.glb") if (rel := glb.relative_to(out).as_posix()) not in used and rel not in kept]
for glb in pruned:
    glb.unlink()
summary = json.loads((out / "summary.json").read_text())
print(f"content/old-art: {len(summary['converted'])} models, {summary['parts']} parts, {summary['placementFiles']} placement"
      f" files; {len(summary['failed'])} failed, {len(summary['engineRefused'])} refused by the Engine check;"
      f" {len(used)} GLBs placed, {len(kept)} animated models kept, {len(pruned)} unplaced GLBs removed")
