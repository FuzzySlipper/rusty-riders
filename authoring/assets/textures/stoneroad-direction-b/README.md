# StoneRoad Direction B — "Métal Hurlant" Necro-Cathedral Texture Pack and Kit Blueprint

Generated for Patch (2026-11-12) to replace the first-pass `stoneroad/` textures (`floor-6`, `wall-6`, `trim-3`), which read as a "pastel funhouse" in first person due to per-stone pink/lavender/cream hue confetti, wall-to-wall Voronoi crazy-paving, and an upside-down value hierarchy (brightest surface underfoot).

## 1. Core French-Comics (`Ligne Claire` / Moebius / Druillet) Surface Rules

In French *Bande Dessinée*—and in Rift Riders' own original 2D interior art (`BuildingBgHall.png`, `BuildingBgChambers.png`, `BuildingBgIntricate.png`, `Freedom/1FT_Left_fg.png`)—**the scene as a whole is richly colourful, but each architectural element owns ONE stable pigment family**:

1. **Element-Stable Color-Blocking (Zero Per-Stone Hue Confetti):**
   - Never ask a single stone texture for *"grey stone with faint violet and dusky red tints"*—Z-Image interprets that by painting adjacent cobblestones salmon-pink, lavender, and cream.
   - Instead, each texture prompt specifies **one uniform pigment across every stone**, and colour contrast happens **between structural elements** (calm slate-indigo floor vs. cool grey-violet ashlar wall vs. deep oxblood-burgundy pilasters/arches/ribs vs. comic-black obsidian ceiling vaults).
   - This also allows dynamic 3D lighting (warm amber sconces/braziers, cyan/magenta rift glow) to multiply cleanly against stable cel planes without turning into rainbow soup.
2. **Rectilinear Ashlar and Calm Planes Over Voronoi Crazy-Paving:**
   - Avoid `"irregular stone slabs / uneven courses"` on built architecture, which triggers round river-boulder Voronoi cells.
   - Prompt for **horizontal hand-cut ashlar courses** on walls and **interlocking cut basalt-slate slabs** on floors, with **mostly calm, flat cel-shaded faces** and sparse Moebius hatch ticks only near chipped edges.
3. **Grounded Value Hierarchy:**
   - **Ceiling / Deep Alcove Recesses (`ceiling-obsidian-vault`):** Luminance mean `~0.08` (`#1a1824` comic-black obsidian stone).
   - **Floor Runner (`runner-crimson-inlay`):** Luminance mean `~0.14` (`#782638` deep crimson-porphyry with muted ochre-gold geometric ritual inlay).
   - **Structural Trims / Pilasters / Arch Rings / Vault Ribs (`trim-oxblood-dressed`):** Luminance mean `~0.17`, saturation `0.76` (`#6e293e` deep oxblood-burgundy dressed stone—carries the Open Hand faction colour and matches the old mauve props).
   - **Primary Arena Floor (`floor-slate-basalt`):** Luminance mean `~0.28` (`#2d2b3a` calm dark desaturated slate-indigo slabs—dark enough underfoot that held weapons, enemies, and projectiles pop).
   - **Main Wall (`wall-ashlar-slate`):** Luminance mean `~0.47` (`#5e5b6e` muted cool grey-violet horizontal ashlar courses).

## 2. Generated Texture Pack (in this folder)

All 5 surfaces were generated on `den-m5` and `den-nimo` with `z_image_bf16.safetensors` + `rrink_zimage_v5_seg6_3000_steps_00001_.safetensors` (`1.0`, `--seamless`, 25 steps `res_multistep`, `cfg 4`, `shift 3`, `1024×1024`), verified with `authoring/tools/textures/checks.py` (`flags: []` on all 5), and paired with tangent-space normal maps (`strength=3.0`):

| Surface File | Kit Role | Repeat (`metres`) | `checks.py` Summary |
| --- | --- | --- | --- |
| `floor-slate-basalt.png` (`-11`) | `Floor` (Primary Arena Ground) | `4.0` | `seam 0.87, repeat 0.073, grad 0.018, persp 0.067, mean 0.284, flags []` |
| `wall-ashlar-slate.png` (`-12`) | `Wall` (Main Horizontal Ashlar) | `4.0` | `seam 0.59, repeat 0.332, grad 0.008, persp 0.029, mean 0.474, flags []` |
| `trim-oxblood-dressed.png` (`-12`) | `Trim` (Pilasters, Arch Rings, Vault Ribs, Cornices) | `2.5` | `seam 1.53, repeat 0.225, grad 0.018, persp 0.121, mean 0.169, flags []` |
| `ceiling-obsidian-vault.png` (`-12`) | `Ceiling` and Recessed Wall Bays | `3.0` | `seam 1.04, repeat 0.107, grad 0.003, persp 0.068, mean 0.078, flags []` |
| `runner-crimson-inlay.png` (`-12`) | `Runner` (New 5th Slot: Door-to-Center Floor Strip) | `3.0` | `seam 0.81, repeat 0.055, grad 0.008, persp 0.118, mean 0.141, flags []` |

Full prompts, negative prompt, seeds, and per-candidate check scores are recorded in `sources.json`.
Visual reference sheets:
- `reference-room-redesign-concepts.png` (Current screenshot vs. Directions A, B, C)
- `reference-direction-b-necro-cathedral.png` (Full-res Direction B target concept)
- `reference-direction-b-texture-pack-sheet.png` (2×2 tiled contact sheet of all 5 surfaces + Direction B target)

## 3. Blueprint for Wiring "Direction B" into `LevelKit.cs` and `StoneRoadMap.json`

1. **Bind Separate `Ceiling` and `Trim` Textures + Add `Runner` Slot (`KitSurface.Runner`):**
   - Point `wall` to `wall-ashlar-slate.png` (`4.0 m`), `floor` to `floor-slate-basalt.png` (`4.0 m`), `ceiling` to `ceiling-obsidian-vault.png` (`3.0 m`), and `trim` to `trim-oxblood-dressed.png` (`2.5 m`).
   - Add an optional 5th surface slot (`Runner` -> `runner-crimson-inlay.png`, `3.0 m`) for processional floor strips.
2. **Flared Arcade Capitals / Blind Wall Arches (`LevelKit.Pilasters`):**
   - Echoing `BuildingBgHall.png` and `reference-direction-b-necro-cathedral.png`: instead of pilasters stopping flat at the cornice, flare the top `2.2–2.5 m` of adjacent pilasters along a wall run into an **oxblood blind-arch arcade** beneath the cornice (or sweep an upper arch rib between pilaster tops), with the recessed upper bay or alcove backing taking the dark `Ceiling` / recess material.
3. **Transverse Ceiling Vault Ribs:**
   - From each pilaster capital (and hall column), project a `0.6 m wide × 0.45 m deep` rib in `KitSurface.Trim` (`trim-oxblood-dressed`) across the `ceiling-obsidian-vault` plane so rooms read as ribbed *Métal Hurlant* necro-cathedrals rather than flat lids.
4. **Door-to-Center Processional Floor Runners:**
   - Using the room plan's doorway midpoints and inward normals, lay a `2.5 m`-wide `Runner` strip (`runner-crimson-inlay.png`) leading from each doorway arch straight into the room's central hub (and around central colonnades/objectives) to break up the 45–75 m arena floor.
5. **Warm Amber Sconce / Brazier Lighting Against Cool Slate:**
   - Place warm golden-amber point lights (`#ffaa44`) at `y = 3.2 m` on the pilasters flanking each doorway arch. Because the wall and floor albedos are now stable, cool desaturated slate-indigo/violet cel planes, warm sconce light creates punchy French-comic chiaroscuro without rainbow muddiness.
