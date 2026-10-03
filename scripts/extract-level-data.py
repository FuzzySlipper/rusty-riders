#!/usr/bin/env python3
"""Translate the old Unity game's procedural-level data into content/levels/*.json.

Reads old-game/Assets/GameData/Levels (chunks, layouts, styles, tilesets, DetailGrid, ProceduralLevelConfig)
and the walkability grid on each tileset tile prefab. The JSON is product content: checked in, readable, and
editable without Unity. Art is converted separately by scripts/import-old-art.sh.

Conventions kept from the old game (Unity space): x = east, z = north, y = up; directions are
forward(+z)=0, right(+x)=1, back(-z)=2, left(-x)=3; rotation r means r * 90 degrees clockwise seen from above;
3x3 and 9x9 grids are row-major with row 0 at north and column 0 at west.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "old-game" / "Assets"
LEVELS = ASSETS / "GameData" / "Levels"
OUT = ROOT / "content" / "levels"

TILE_KINDS = ["Start", "Goal", "Road", "Curve", "Forked", "Cross", "RoadStop", "RoomGateA", "RoomGateB", "RoomGateC",
              "RoomGateD", "RoomWall", "RoomCornerA", "RoomCornerB", "RoomCornerC", "RoomFlat", "Warp", "Door"]
NODE_TAGS = {1: "start", 2: "exit", 4: "key", 8: "bonus", 16: "midBoss", 32: "boss", 64: "spawner"}
FACTIONS = ["OpenHand", "AstralMandate", "KeeperGate", "HermeticCircle", "Maelstrom", "None"]
STEPS = [(0, 1), (1, 0), (0, -1), (-1, 0)]  # forward, right, back, left
ROOM_BRUSH, DOOR_BRUSH = 0, 1


def guid_paths() -> dict[str, Path]:
    paths = {}
    for meta in ASSETS.rglob("*.meta"):
        match = re.search(r"^guid: (\w+)", meta.read_text(errors="replace"), re.M)
        if match:
            paths[match.group(1)] = meta.with_suffix("")
    return paths


def scalar(text: str, key: str) -> str | None:
    match = re.search(rf"^\s*{key}: (.*)$", text, re.M)
    return match.group(1).strip() if match else None


def hex_bools(value: str) -> str:
    """Unity serializes bool[] as hex bytes; returned as '.' (walkable) / '#' (blocked)."""
    return "".join("." if value[i:i + 2] != "00" else "#" for i in range(0, len(value), 2))


def grid_rows(cells: str, width: int) -> list[str]:
    return [cells[i:i + width] for i in range(0, len(cells), width)]


def asset_name(path: Path) -> str:
    return path.relative_to(ASSETS).with_suffix("").as_posix()


def tile_kinds() -> list[dict]:
    text = (LEVELS / "DetailGrid.asset").read_text()
    templates = re.findall(r"- Walkable: ([0-9a-f]+)", text)
    if len(templates) != len(TILE_KINDS):
        sys.exit(f"DetailGrid.asset has {len(templates)} templates, expected {len(TILE_KINDS)}")
    return [{"kind": kind, "walkable": grid_rows(hex_bools(t), 3)} for kind, t in zip(TILE_KINDS, templates)]


def generator() -> dict:
    text = (LEVELS / "ProceduralLevelConfig.asset").read_text()
    return {"cellSize": float(scalar(text, "CellSize")), "sectorSize": int(scalar(text, "SectorSize")),
            "changeCorridorDirectionChance": int(scalar(text, "ChangeCorridorDirectionChance")) / 100,
            "maxCorridorSteps": 100}


def tileset(path: Path) -> dict:
    text = path.read_text()
    tiles, walkability = {}, {}
    for kind, block in zip(TILE_KINDS, re.split(r"\n  - Objects:", text.split("\n  Tiles:", 1)[1])[1:]):
        names = [n.strip() for n in re.findall(r"AssetName: (.*)", block) if n.strip()]
        prefabs = []
        for name in names:
            prefab = ASSETS / "GameData" / f"{name}.prefab"
            if not prefab.is_file():
                print(f"{path.name}: {kind} names missing prefab {name}", file=sys.stderr)
                continue
            prefabs.append(asset_name(prefab))
            walkable = scalar(prefab.read_text(), "Walkable")
            if walkable and len(walkable) == 162:
                walkability[asset_name(prefab)] = grid_rows(hex_bools(walkable), 9)
        tiles[kind] = prefabs
    return {"id": path.stem, "tiles": tiles, "walkability": walkability}


def chunk(path: Path) -> dict:
    text = path.read_text()
    references = {}
    for rid, body in re.findall(r"\n    (\d+):\n      type: \{class: (\w*)", text):
        references[int(rid)] = body
    primary = dict(re.findall(r"\n    (\d+):\n      type: \{class: ObjectiveChunkData[^\n]*\n      data:\n        Primary: (\d)", text))
    cells = []
    for block in re.split(r"\n  - Position:", text.split("\n  Cells:", 1)[1])[1:]:
        x, z = int(scalar(block, "x")), int(scalar(block, "z"))
        brush = int(scalar(block, "BrushIndex"))
        data = re.search(r"Data:\n\s+id: (\d+)", block)
        cells.append({"x": x, "z": z, "brush": brush, "ref": int(data.group(1)) if data else None})
    # A reference used by more than one cell is Unity's shared placeholder, not one cell's objective.
    uses = {}
    for cell in cells:
        uses[cell["ref"]] = uses.get(cell["ref"], 0) + 1
    rooms = {(c["x"], c["z"]) for c in cells if c["brush"] == ROOM_BRUSH}
    out = []
    for cell in cells:
        entry: dict = {"x": cell["x"], "z": cell["z"]}
        if cell["brush"] == DOOR_BRUSH:
            # A door cell faces away from the room cell beside it (the old DoorCalculate).
            facing = next((d for d, (dx, dz) in enumerate(STEPS) if (cell["x"] - dx, cell["z"] - dz) in rooms), None)
            if facing is None:
                print(f"{path.name}: door cell {cell['x']},{cell['z']} has no room beside it", file=sys.stderr)
                continue
            entry["door"] = ["forward", "right", "back", "left"][facing]
        elif cell["brush"] != ROOM_BRUSH:
            print(f"{path.name}: brush {cell['brush']} cell {cell['x']},{cell['z']} skipped", file=sys.stderr)
            continue
        ref = cell["ref"]
        if ref is not None and uses[ref] == 1:
            kind = references.get(ref)
            if kind == "ObjectiveChunkData":
                entry["objective"] = "primary" if primary.get(str(ref).zfill(8), primary.get(str(ref))) == "1" else "secondary"
            elif kind == "ChestChunkData":
                entry["objective"] = "chest"
        out.append(entry)
    return {"id": path.stem, "cells": out}


def layout(path: Path) -> dict:
    text = path.read_text()
    nodes = []
    for block in re.split(r"\n  - GridPosition:", text.split("\n  Nodes:", 1)[1])[1:]:
        tags = int(scalar(block, "Tags"))
        connections = []
        for target, direction, locked in re.findall(r"TargetId: (-?\d+)\n\s+Dir: (\d)\n\s+Locked: (\d)", block):
            if int(target) >= 0:
                connections.append({"dir": ["forward", "right", "back", "left"][int(direction)], "target": int(target),
                                    **({"locked": True} if locked == "1" else {})})
        nodes.append({"id": int(re.findall(r"^    Id: (-?\d+)", block, re.M)[-1]), "x": int(scalar(block, "x")),
                      "z": int(re.search(r"\n\s+z: (-?\d+)", block).group(1)),
                      "tags": [name for bit, name in NODE_TAGS.items() if tags & bit],
                      "connections": connections})
    return {"id": path.stem, "special": scalar(text, "IsSpecial") == "1", "nodes": nodes}


def styles(guids: dict[str, Path]) -> list[dict]:
    out = []
    for path in sorted((LEVELS / "Styles").glob("*.asset")):
        text = path.read_text()
        block = re.search(r"TileSets:\n((?:\s+- \{[^\n]*\}\n)+)", text)
        tilesets = [guids[g].stem for g in re.findall(r"guid: (\w+)", block.group(1) if block else "") if g in guids]
        out.append({"id": path.stem, "faction": FACTIONS[int(scalar(text, "Faction") or 5)], "tilesets": tilesets})
    return out


def write(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=1) + "\n")


def main() -> None:
    if not LEVELS.is_dir():
        sys.exit(f"old Unity project not found at {LEVELS}")
    guids = guid_paths()
    write(OUT / "generator.json", generator())
    write(OUT / "tile-kinds.json", tile_kinds())
    write(OUT / "styles.json", styles(guids))
    write(OUT / "chunks.json", [chunk(p) for p in sorted((LEVELS / "Chunks").glob("*.asset"))])
    write(OUT / "layouts.json", [layout(p) for p in sorted((LEVELS / "Layouts").glob("*.asset"))])
    for path in sorted((LEVELS / "TileSets").glob("*.asset")):
        set_ = tileset(path)
        if any(set_["tiles"].values()):
            write(OUT / "tilesets" / f"{path.stem}.json", set_)
    print(f"wrote {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
