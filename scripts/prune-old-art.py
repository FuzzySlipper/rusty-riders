#!/usr/bin/env python3
"""Keep only the GLBs some placement file draws in a unity-import output, then summarize it.

Whole-model GLBs that exist only to cut per-mesh parts from, and the lower levels of Unity LODGroups (rows with
lod > 0, which the product does not draw), are dropped, because the product copies its content root into every
staged build. Placement rows are left as written. Used by scripts/import-old-art.sh.
"""
import json
import sys
from pathlib import Path

out = Path(sys.argv[1])
used = {row["glb"] for path in out.glob("placements/**/*.placements.json")
        for row in json.loads(path.read_text())["placements"]
        if row.get("glb") and not row.get("error") and not row.get("lod")}
pruned = [glb for glb in out.glob("**/*.glb") if glb.relative_to(out).as_posix() not in used]
for glb in pruned:
    glb.unlink()
summary = json.loads((out / "summary.json").read_text())
print(f"content/old-art: {len(summary['converted'])} models, {summary['parts']} parts, {summary['placementFiles']} placement"
      f" files; {len(summary['failed'])} failed, {len(summary['engineRefused'])} refused by the Engine check;"
      f" {len(used)} GLBs placed, {len(pruned)} unplaced GLBs removed")
