#!/usr/bin/env python3
"""Copies the old game's UI art the DOM HUD uses into src/ui/art/, downscaled for the browser.

Rift Riders' UI sprites are 1-4K whole frames. This cuts them into ornaments (skull caps, corners, sockets, the ring
and crest), rails and a fill that repeat without a seam, exports each at twice its display size, and copies the icons
and the pixel font. Rerunning it regenerates src/ui/art/ from old-game/ exactly.
"""
import shutil
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
OLD = ROOT / "old-game" / "Assets"
OUT = ROOT / "src" / "ui" / "art"

UI = "Art/Sprites/UI/RiftRiders"
EQUIP = "GameData/Icons/Equipment"
OLD_ICONS = "GameData/Icons/Old"
JUNK = "GameData/Icons/Junk"
ABILITY = "GameData/Icons/Abilities"

# The art is decoration pinned at a fixed scale, never stretched (docs/ui.md). Each piece is exported at twice its
# display size at --u: 1px, so it stays sharp up to the largest UI scale step; riders.css gives the display sizes.
# (output name, source path under old-game/Assets, crop box or None for the whole sprite, export scale)
PIECES = [
    ("hotbar-left.png", f"{UI}/OH_UI_Hotbar_Bottom_Frame.png", (0, 0, 880, 512), 0.375),     # red skull leg and horns
    ("hotbar-right.png", f"{UI}/OH_UI_Hotbar_Bottom_Frame.png", (1120, 0, 2056, 512), 0.375),
    ("banner-left.png", f"{UI}/OH_UI_Hotbar_Top_Frame.png", (0, 0, 880, 512), 0.375),
    ("banner-right.png", f"{UI}/OH_UI_Hotbar_Top_Frame.png", (1120, 0, 2056, 512), 0.375),
    ("vitals-corner.png", f"{UI}/UI_bar_gouche_brush_effect_Square.png", (0, 0, 1100, 1024), 0.375),  # skull corner, horn arm
    ("socket.png", f"{UI}/OH_UI_Button.png", None, 0.4),                                    # round skull ring
    ("ring.png", f"{UI}/int_sphere_colored.png", None, 0.25),                               # orange-purple horned ring
    ("crest.png", f"{UI}/uiInventory_topper_only.png", None, 0.5),
    ("panel.png", f"{UI}/UI SLICE BOX_border.png", None, 0.5),  # violet horned 9-slice: fixed corners, repeating rails
]
# Rails that repeat along a bar's width: (output name, source, x, y0, y1, width, overlap, export scale). The strip is
# taken from a flat run of the rail and cross-faded over its overlap so it tiles without a seam.
RAILS = [
    ("hotbar-rail.png", f"{UI}/OH_UI_Hotbar_Bottom_Frame.png", 880, 0, 512, 200, 40, 0.375),
    ("banner-rail.png", f"{UI}/OH_UI_Hotbar_Top_Frame.png", 880, 0, 512, 200, 40, 0.375),
    ("vitals-rail.png", f"{UI}/UI_bar_gouche_brush_effect_Square.png", 1100, 0, 1024, 400, 100, 0.375),
]
# The brushed panel fill as a tile that repeats both ways: (output name, source, box, overlap, export scale).
FILL = ("fill.png", f"{UI}/OH_UI_Box_BG.png", (640, 540, 1960, 1024), 200, 0.5)
ICONS = [
    ("blade.png", f"{EQUIP}/W_Melee_1H_1H_Sword_A.png"),
    ("maul.png", f"{EQUIP}/W_Melee_2H_BattleHammer.png"),
    ("flintlock.png", f"{EQUIP}/W_Gun_1H_Flintlock.png"),
    ("wand.png", f"{OLD_ICONS}/W_Wand.png"),
    ("sigil.png", f"{ABILITY}/A_FrostCone.png"),
    ("coat.png", f"{EQUIP}/E-RPGLiteArmor-Chest.png"),
    ("ring-charm.png", f"{EQUIP}/E-Ring1.png"),
    ("necklace.png", f"{EQUIP}/E-Necklace1.png"),
    ("boots.png", f"{EQUIP}/E-SciFiArmor-Chest.png"),
    ("medkit.png", f"{OLD_ICONS}/I_square-bottle.png"),
    ("tonic.png", f"{ABILITY}/Old/A_Heal.png"),
    ("shield-cell.png", f"{JUNK}/Science_Junk_Electronic1.png"),
    ("tech-cell.png", f"{JUNK}/Alien_Junk_Container.png"),
    ("charge.png", f"{JUNK}/Misc_AlienMaterial1.png"),
    ("scrap.png", f"{JUNK}/Misc_LowGradeScrapMetal1.png"),
    ("shard.png", f"{OLD_ICONS}/W_Relic.png"),
    ("physical.png", f"{ABILITY}/A_AuraPhysicalBonus.png"),
    ("burn.png", f"{ABILITY}/A_AuraFireDef.png"),
    ("freeze.png", f"{ABILITY}/A_AuraFrostDef.png"),
    ("lightning.png", f"{ABILITY}/A_AuraLightningDef.png"),
    ("spirit.png", f"{ABILITY}/A_AuraSpiritDebuff.png"),
]
ICON_SIZE = 128
# The font goes to content/: the DOM receives fonts the product grants (Ui.OpenFont), not UI-root files.
FONT = (ROOT / "content" / "ui" / "alagard.ttf", "Art/Sprites/UI/Fonts/alagard_by_pix3m-d6awiwp.ttf")


def save(image, scale, name):
    size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    image.resize(size, Image.LANCZOS).save(OUT / name, optimize=True)


def seamless(image, length, overlap, horizontal):
    """The first `length` pixels along one axis, the start cross-faded with the `overlap` pixels past the end, so the
    result repeats without a seam."""
    span = (lambda a, b: (a, 0, b, image.height)) if horizontal else (lambda a, b: (0, a, image.width, b))
    out = image.crop(span(0, length))
    for i in range(overlap):
        t = i / overlap
        line = Image.blend(image.crop(span(length + i, length + i + 1)), image.crop(span(i, i + 1)), t)
        out.paste(line, span(i, i + 1)[:2])
    return out


def main() -> int:
    if not OLD.is_dir():
        print(f"no {OLD}: the old game's assets are needed", file=sys.stderr)
        return 1
    (OUT / "icons").mkdir(parents=True, exist_ok=True)
    for stale in OUT.glob("*.png"):
        stale.unlink()
    for name, source, box, scale in PIECES:
        image = Image.open(OLD / source).convert("RGBA")
        save(image.crop(box) if box else image, scale, name)
    for name, source, x, y0, y1, width, overlap, scale in RAILS:
        strip = Image.open(OLD / source).convert("RGBA").crop((x, y0, x + width + overlap, y1))
        save(seamless(strip, width, overlap, horizontal=True), scale, name)
    name, source, box, overlap, scale = FILL
    tile = Image.open(OLD / source).convert("RGBA").crop(box)
    tile = seamless(tile, tile.width - overlap, overlap, horizontal=True)
    save(seamless(tile, tile.height - overlap, overlap, horizontal=False), scale, name)
    for name, source in ICONS:
        image = Image.open(OLD / source).convert("RGBA")
        image.thumbnail((ICON_SIZE, ICON_SIZE), Image.LANCZOS)
        image.save(OUT / "icons" / name, optimize=True)
    FONT[0].parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(OLD / FONT[1], FONT[0])
    print(f"wrote {len(PIECES) + len(RAILS) + 1} pieces and {len(ICONS)} icons to {OUT.relative_to(ROOT)}, and the font to {FONT[0].relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
