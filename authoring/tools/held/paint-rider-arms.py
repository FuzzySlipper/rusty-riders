"""Upgrades painted.glb's Hand_Recolor base-colour texture to Rift Riders' house Metal Hurlant / Moebius
ligne claire style while preserving exact UV alignment, knuckle-stud placement, wrist cuff geometry, and skeleton.

Outputs:
  - authoring/assets/held/arms/painted-rider-gauntlet.png (2048x2048 sRGB PNG)
  - updates the embedded baseColor image in content/art/fp-arms/painted.glb (with roughnessFactor 0.82)
"""
import io
import json
import struct
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[3]
PAINTED_GLB = ROOT / "content/art/fp-arms/painted.glb"
ORIG_TEX = ROOT / "authoring/assets/old-game/Assets/MoreArt/FirstPersonArms/Hand_Recolor.png"
OUT_TEX = ROOT / "authoring/assets/held/arms/painted-rider-gauntlet.png"


def load_source_texture() -> Image.Image:
    if ORIG_TEX.exists():
        return Image.open(ORIG_TEX).convert("RGB").resize((2048, 2048), Image.Resampling.LANCZOS)
    # Fallback: read directly from painted.glb
    data = PAINTED_GLB.read_bytes()
    json_len = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20 : 20 + json_len])
    bin_start = 20 + json_len + 8
    bv = doc["bufferViews"][doc["images"][0]["bufferView"]]
    raw = data[bin_start + bv.get("byteOffset", 0) : bin_start + bv.get("byteOffset", 0) + bv["byteLength"]]
    return Image.open(io.BytesIO(raw)).convert("RGB").resize((2048, 2048), Image.Resampling.LANCZOS)


def build_uv_masks(glb_path: Path, size: int = 2048):
    data = glb_path.read_bytes()
    json_len = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20 : 20 + json_len])
    bin_start = 20 + json_len + 8

    def read_acc(idx):
        acc = doc["accessors"][idx]
        bv = doc["bufferViews"][acc["bufferView"]]
        st = bin_start + bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
        comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[acc["type"]]
        dt = {5121: "<u1", 5123: "<u2", 5125: "<u4", 5126: "<f4"}[acc["componentType"]]
        arr = np.frombuffer(data[st : st + np.dtype(dt).itemsize * comps * acc["count"]], dtype=dt)
        return arr.reshape(acc["count"], comps) if comps > 1 else arr

    prim = doc["meshes"][0]["primitives"][0]
    uvs = read_acc(prim["attributes"]["TEXCOORD_0"])
    indices = read_acc(prim["indices"])
    joints = read_acc(prim["attributes"]["JOINTS_0"])
    weights = read_acc(prim["attributes"]["WEIGHTS_0"])
    joint_names = [doc["nodes"][j].get("name", "") for j in doc["skins"][0]["joints"]]
    dom = joints[np.arange(len(joints)), np.argmax(weights, axis=1)]

    sleeve_im = Image.new("L", (size, size), 0)
    hand_im = Image.new("L", (size, size), 0)
    ds, dh = ImageDraw.Draw(sleeve_im), ImageDraw.Draw(hand_im)

    for t in range(0, len(indices), 3):
        v0, v1, v2 = indices[t], indices[t + 1], indices[t + 2]
        pts = [(float(uvs[v, 0]) * size, float(uvs[v, 1]) * size) for v in (v0, v1, v2)]
        avg_v = (uvs[v0, 1] + uvs[v1, 1] + uvs[v2, 1]) / 3.0
        name = joint_names[dom[v0]]
        if avg_v < 0.625 and ("Arm1" in name or "Arm2" in name):
            ds.polygon(pts, fill=255)
        else:
            dh.polygon(pts, fill=255)

    # Dilate masks slightly so UV gutter padding stays clean across mip levels
    sleeve_mask = np.asarray(sleeve_im.filter(ImageFilter.MaxFilter(25)), dtype=np.float32) / 255.0
    hand_mask = np.asarray(hand_im.filter(ImageFilter.MaxFilter(25)), dtype=np.float32) / 255.0
    return sleeve_mask, hand_mask


def paint_texture(orig_im: Image.Image) -> Image.Image:
    W, H = 2048, 2048
    orig = np.asarray(orig_im, dtype=np.float32) / 255.0
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    u = xx / W
    v = yy / H

    # Smooth luminance of original to isolate broad sculpted folds vs high-frequency muddy camo noise
    luma_raw = orig[..., 0] * 0.299 + orig[..., 1] * 0.587 + orig[..., 2] * 0.114
    luma_smooth = np.asarray(
        Image.fromarray((np.clip(luma_raw, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(14)),
        dtype=np.float32,
    ) / 255.0
    luma_med = np.asarray(
        Image.fromarray((np.clip(luma_raw, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(4)),
        dtype=np.float32,
    ) / 255.0

    # --- 1. SLEEVE PALETTE (v < 0.63): Tailored slate-indigo Metal Hurlant rider's coat ---
    # Use clean local-contrast fold extraction so UV boundary darkness never creates muddy blobs or rectangular edges.
    local_fold = luma_med - luma_smooth
    pleat = 0.5 + 0.5 * np.sin(v * 38.0 + np.sin(u * 12.0) * 1.1)
    fold_field = np.clip(0.55 + local_fold * 6.5 + (pleat - 0.5) * 0.22, 0.0, 1.0)

    s_dark = np.array([50, 56, 74], dtype=np.float32) / 255.0
    s_mid = np.array([78, 88, 110], dtype=np.float32) / 255.0
    s_high = np.array([106, 118, 142], dtype=np.float32) / 255.0

    b_s1 = np.clip((fold_field - 0.34) / 0.07, 0.0, 1.0)
    b_s2 = np.clip((fold_field - 0.70) / 0.07, 0.0, 1.0)
    sleeve_col = s_dark * (1.0 - b_s1)[..., None] + s_mid * b_s1[..., None]
    sleeve_col = sleeve_col * (1.0 - b_s2)[..., None] + s_high * b_s2[..., None]

    # Crisp dark ink fold lines from sharp local creases only (never background border darkness)
    orig_sleeve_ink = np.clip((-local_fold - 0.022) / 0.018, 0.0, 1.0) * (luma_smooth > 0.045)
    band_ink = np.clip(1.0 - np.abs(fold_field - 0.375) / 0.016, 0.0, 1.0) * 0.42
    sleeve_ink = np.clip(np.maximum(orig_sleeve_ink * 0.85, band_ink), 0.0, 1.0)

    # Tailored terracotta-crimson gauntlet cuff band along the sculpted wrist edge (u ~ 0.04..0.19)
    cuff_outer = (u >= 0.04) & (u <= 0.195)
    cuff_trim = (u >= 0.115) & (u <= 0.182)
    cuff_edge_piping = ((np.abs(u - 0.115) < 0.008) | (np.abs(u - 0.182) < 0.008) | (np.abs(u - 0.196) < 0.005))
    cuff_stitch = (np.abs(u - 0.128) < 0.003) | (np.abs(u - 0.169) < 0.003)
    cuff_stitch = cuff_stitch & ((np.sin(v * 260.0)) > 0.1)

    terracotta_mid = np.array([164, 72, 52], dtype=np.float32) / 255.0
    terracotta_dark = np.array([118, 48, 38], dtype=np.float32) / 255.0
    iron_dark = np.array([34, 36, 46], dtype=np.float32) / 255.0
    brass_mid = np.array([196, 156, 82], dtype=np.float32) / 255.0
    brass_hi = np.array([232, 198, 126], dtype=np.float32) / 255.0
    ink_col = np.array([18, 16, 24], dtype=np.float32) / 255.0

    # Inner dark leather cuff lining (u < 0.115)
    cuff_lining = (u < 0.115)
    sleeve_col = np.where(cuff_lining[..., None], np.array([58, 42, 38], dtype=np.float32) / 255.0, sleeve_col)
    # Terracotta cuff band with crisp cel shading
    tc_col = terracotta_dark * (1.0 - b_s1)[..., None] + terracotta_mid * b_s1[..., None]
    sleeve_col = np.where(cuff_trim[..., None], tc_col, sleeve_col)
    sleeve_col = np.where(cuff_stitch[..., None], brass_mid, sleeve_col)
    sleeve_col = np.where(cuff_edge_piping[..., None], iron_dark, sleeve_col)

    # Brass cuff clasp plate sitting right on the rectangular clasp UV island (u=0.145..0.180, v=0.22..0.34)
    clasp = (u >= 0.142) & (u <= 0.180) & (v >= 0.215) & (v <= 0.345)
    clasp_inner = (u >= 0.148) & (u <= 0.174) & (v >= 0.225) & (v <= 0.335)
    clasp_bar = (u >= 0.155) & (u <= 0.167) & (v >= 0.245) & (v <= 0.315)
    sleeve_col = np.where(clasp[..., None], ink_col, sleeve_col)
    sleeve_col = np.where(clasp_inner[..., None], brass_mid, sleeve_col)
    sleeve_col = np.where(clasp_bar[..., None], brass_hi, sleeve_col)

    # Apply crisp sleeve ink lines (outside the brass clasp)
    sleeve_ink = np.where(cuff_outer, sleeve_ink * 0.3, sleeve_ink)
    sleeve_col = sleeve_col * (1.0 - sleeve_ink * 0.85)[..., None] + ink_col * (sleeve_ink * 0.85)[..., None]

    # --- 2. GLOVED HAND PALETTE (v >= 0.60): Burnished russet-leather rider's gauntlet + brass studs ---
    # Separate back-of-hand (left half u < 0.51) vs reinforced darker palm (right half u >= 0.51)
    palm_side = np.clip((u - 0.49) / 0.05, 0.0, 1.0)
    # Clean broad leather shading without the noisy spiderweb cracks
    hand_luma_broad = np.asarray(
        Image.fromarray((np.clip(luma_raw, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(10)),
        dtype=np.float32,
    ) / 255.0
    hl = np.clip((hand_luma_broad - 0.08) / 0.16, 0.0, 1.0)
    hb1 = np.clip((hl - 0.35) / 0.09, 0.0, 1.0)
    hb2 = np.clip((hl - 0.72) / 0.09, 0.0, 1.0)

    # Back-of-hand rich russet/chestnut leather (warm, readable, Moebius cel tones)
    g_dark = np.array([82, 50, 42], dtype=np.float32) / 255.0
    g_mid = np.array([128, 78, 58], dtype=np.float32) / 255.0
    g_hi = np.array([162, 106, 78], dtype=np.float32) / 255.0
    back_col = g_dark * (1.0 - hb1)[..., None] + g_mid * hb1[..., None]
    back_col = back_col * (1.0 - hb2)[..., None] + g_hi * hb2[..., None]

    # Palm darker reinforced grip leather (slightly cooler plum-umber)
    p_dark = np.array([62, 40, 38], dtype=np.float32) / 255.0
    p_mid = np.array([96, 62, 52], dtype=np.float32) / 255.0
    p_hi = np.array([122, 82, 66], dtype=np.float32) / 255.0
    palm_col = p_dark * (1.0 - hb1)[..., None] + p_mid * hb1[..., None]
    palm_col = palm_col * (1.0 - hb2)[..., None] + p_hi * hb2[..., None]

    hand_col = back_col * (1.0 - palm_side)[..., None] + palm_col * palm_side[..., None]

    # Preserve ONLY the strong intentional painted ink creases on the glove (threshold out fine spiderweb noise!)
    # Detect bold dark lines using median-filtered darkness
    luma_clean = np.asarray(
        Image.fromarray((np.clip(luma_raw, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(5)),
        dtype=np.float32,
    ) / 255.0
    hand_ink = np.clip((0.044 - luma_clean) / 0.022, 0.0, 1.0) * (v > 0.62)

    # Detect exact original 4 metal knuckle studs (in Hand_Recolor.png, studs are cool bluish-grey: B > R in v=0.77..0.85, u=0.31..0.50)
    stud_blue = ((orig[..., 2] - orig[..., 0]) > 0.008) & (orig[..., 2] > 0.215)
    stud_region = stud_blue & (u > 0.31) & (u < 0.50) & (v > 0.77) & (v < 0.85)
    # Find centroids of the 4 knuckle stud blobs so we can redraw them as razor-crisp inked brass rivets!
    ys, xs = np.where(stud_region)
    stud_centers = []
    if len(xs) > 0:
        # Cluster along X into 4 studs
        order = np.argsort(xs)
        xs_s, ys_s = xs[order], ys[order]
        splits = np.where(np.diff(xs_s) > 20)[0] + 1
        for gx, gy in zip(np.split(xs_s, splits), np.split(ys_s, splits)):
            if len(gx) > 30:
                stud_centers.append((float(gx.mean()), float(gy.mean()), float(max(np.ptp(gx), np.ptp(gy)) * 0.56)))
    print("Detected knuckle studs (x, y, r):", [(round(x, 1), round(y, 1), round(r, 1)) for x, y, r in stud_centers])

    # Apply crisp hand ink lines
    hand_col = hand_col * (1.0 - hand_ink * 0.82)[..., None] + ink_col * (hand_ink * 0.82)[..., None]

    # Combine sleeve & hand cleanly using UV region boundary (v ~ 0.618)
    blend_v = np.clip((v - 0.605) / 0.025, 0.0, 1.0)
    final = sleeve_col * (1.0 - blend_v)[..., None] + hand_col * blend_v[..., None]

    # Render crisp vector overlays on top (leather wrist strap on back of hand, brass knuckle rivets, finger joint lines)
    out_im = Image.fromarray((np.clip(final, 0, 1) * 255.0 + 0.5).astype(np.uint8), mode="RGB")
    draw = ImageDraw.Draw(out_im)

    # Clean dark-leather wrist strap across back of hand (matching original strap at u=0.31..0.54, v=0.72..0.77)
    strap_poly = [
        (int(0.312 * W), int(0.720 * H)),
        (int(0.535 * W), int(0.750 * H)),
        (int(0.530 * W), int(0.778 * H)),
        (int(0.308 * W), int(0.746 * H)),
    ]
    draw.polygon(strap_poly, fill=(56, 34, 30), outline=(18, 16, 24))
    # Crisp 4px ink border around wrist strap
    draw.line(strap_poly + [strap_poly[0]], fill=(18, 16, 24), width=5)
    # Subtle brass rivet pin on the strap
    sx, sy = int(0.495 * W), int(0.758 * H)
    draw.ellipse([sx - 12, sy - 12, sx + 12, sy + 12], fill=(196, 156, 82), outline=(18, 16, 24), width=4)
    draw.ellipse([sx - 5, sy - 6, sx + 2, sy + 1], fill=(238, 212, 146))

    # Draw 4 razor-crisp Moebius ochre-brass knuckle studs at the exact detected mesh knuckle positions!
    for cx, cy, r in stud_centers:
        rr = max(r, 22.0)
        # Outer dark leather recessed collar
        draw.ellipse([cx - rr - 6, cy - rr - 6, cx + rr + 6, cy + rr + 6], fill=(32, 22, 24))
        # Main weathered brass dome
        draw.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=(196, 154, 78), outline=(18, 16, 24), width=5)
        # Warm shadow crescent on lower-right of stud
        draw.arc([cx - rr + 3, cy - rr + 3, cx + rr - 3, cy + rr - 3], start=15, end=155, fill=(138, 98, 44), width=6)
        # Crisp bright cel specular dot on upper-left
        draw.ellipse([cx - rr * 0.48, cy - rr * 0.50, cx - rr * 0.05, cy - rr * 0.12], fill=(242, 220, 158))

    # Crisp dark cuff rim seam at top of glove island (v ~ 0.64)
    return out_im


def embed_in_glb(glb_path: Path, new_img: Image.Image):
    data = bytearray(glb_path.read_bytes())
    magic, version, _ = struct.unpack_from("<III", data, 0)
    json_len, json_type = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20 : 20 + json_len].decode("utf-8"))
    bin_hdr_off = 20 + json_len
    bin_len, bin_type = struct.unpack_from("<II", data, bin_hdr_off)
    bin_start = bin_hdr_off + 8
    raw_bin = bytes(data[bin_start : bin_start + bin_len])

    bvs = [bytearray(raw_bin[bv.get("byteOffset", 0) : bv.get("byteOffset", 0) + bv["byteLength"]]) for bv in doc["bufferViews"]]
    img_bv = doc["images"][0]["bufferView"]
    buf = io.BytesIO()
    new_img.save(buf, format="PNG", optimize=True)
    bvs[img_bv] = bytearray(buf.getvalue())
    doc["images"][0]["mimeType"] = "image/png"

    # Matte comic-book roughness (zero plastic glare)
    for mat in doc.get("materials", []):
        pbr = mat.setdefault("pbrMetallicRoughness", {})
        pbr["roughnessFactor"] = 0.82
        pbr["metallicFactor"] = 0.0

    new_bin = bytearray()
    for i, bv_data in enumerate(bvs):
        while len(new_bin) % 4 != 0:
            new_bin.append(0)
        doc["bufferViews"][i]["byteOffset"] = len(new_bin)
        doc["bufferViews"][i]["byteLength"] = len(bv_data)
        new_bin.extend(bv_data)
    while len(new_bin) % 4 != 0:
        new_bin.append(0)
    doc["buffers"][0]["byteLength"] = len(new_bin)

    json_bytes = json.dumps(doc, separators=(",", ":")).encode("utf-8")
    while len(json_bytes) % 4 != 0:
        json_bytes += b" "

    out_total = 12 + 8 + len(json_bytes) + 8 + len(new_bin)
    out = bytearray()
    out += struct.pack("<III", magic, version, out_total)
    out += struct.pack("<II", len(json_bytes), json_type)
    out += json_bytes
    out += struct.pack("<II", len(new_bin), bin_type)
    out += new_bin
    glb_path.write_bytes(out)
    print(f"Updated {glb_path} ({len(out)} bytes)")


if __name__ == "__main__":
    orig_im = load_source_texture()
    painted = paint_texture(orig_im)
    OUT_TEX.parent.mkdir(parents=True, exist_ok=True)
    painted.save(OUT_TEX)
    arr = np.asarray(painted, dtype=np.float32)
    print(f"Saved {OUT_TEX} mean={arr.mean(axis=(0,1)).round(1)}")
    embed_in_glb(PAINTED_GLB, painted)
