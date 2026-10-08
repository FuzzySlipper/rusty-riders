#!/usr/bin/env python3
"""Copies the old game's UI art the DOM HUD uses into src/ui/art/, downscaled for the browser.

Rift Riders' UI sprites are 1-4K textures with Unity 9-slice borders. This scales each one by its factor
(the CSS border-image slices in src/ui/riders.css are the Unity borders divided by the same factor) and
copies the pixel font. Rerunning it regenerates src/ui/art/ from old-game/ exactly.
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

# (output name, source path under old-game/Assets, scale factor or target size in px for icons)
FRAMES = [
    ("hotbar.png", f"{UI}/OH_UI_Hotbar_Bottom.png", 4),          # red horned bar with its brushed fill
    ("banner.png", f"{UI}/OH_UI_Hotbar_Top_Frame.png", 4),       # top bar frame, for the chase banner
    ("vitals.png", f"{UI}/UI_bar_gouche_brush_effect_Square.png", 4),  # skull corner with a horn arm
    ("fill.png", f"{UI}/OH_UI_Box_BG.png", 4),                   # dark brushed panel fill
    ("button.png", f"{UI}/OH_UI_Button.png", 2),                 # round skull ring
    ("panel.png", f"{UI}/UI SLICE BOX_border.png", 4),           # purple horned 9-slice, border 400
    ("slot.png", f"{UI}/UI SLICE BOX_border_small.png", 4),      # white horned 9-slice, border 310
    ("cartouche.png", f"{UI}/UI INVENTORY_2.png", 4),            # purple tall cartouche, border 940
    ("crest.png", f"{UI}/uiInventory_topper_only.png", 4),
    ("ring.png", f"{UI}/int_sphere_colored.png", 4),             # orange-purple horned ring
]
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


def main() -> int:
    if not OLD.is_dir():
        print(f"no {OLD}: the old game's assets are needed", file=sys.stderr)
        return 1
    (OUT / "icons").mkdir(parents=True, exist_ok=True)
    for name, source, factor in FRAMES:
        image = Image.open(OLD / source).convert("RGBA")
        image.resize((image.width // factor, image.height // factor), Image.LANCZOS).save(OUT / name, optimize=True)
    for name, source in ICONS:
        image = Image.open(OLD / source).convert("RGBA")
        image.thumbnail((ICON_SIZE, ICON_SIZE), Image.LANCZOS)
        image.save(OUT / "icons" / name, optimize=True)
    FONT[0].parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(OLD / FONT[1], FONT[0])
    print(f"wrote {len(FRAMES)} frames and {len(ICONS)} icons to {OUT.relative_to(ROOT)}, and the font to {FONT[0].relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
