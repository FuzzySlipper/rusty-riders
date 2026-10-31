"""MediaPipe world landmarks -> a PSX-arms clip pack (skeleton-only GLB with one animation).

The camera is one fixed frame for the whole clip, from the body's facing over the first frames (a first-person view
looks at the target, it does not turn with the swing). Each arm's upper arm and forearm point where the video's do,
bent in the video's elbow plane; the hand turns to the video's wrist->knuckles line and index->pinky side. Everything
else (clavicles, fingers, root) holds the base clip's first frame (knife_idle: the fist closed on a grip).

    retarget.py LANDMARKS.json ARMS.glb PACK_IN.glb PACK_OUT.glb CLIP_NAME [--start F] [--end F] [--sigma S]
        [--facing F0:F1] [--pitch DEGREES] [--forward BIAS] [--report REPORT.json]

--facing picks the frames whose body facing is the view (where the cut is aimed). --pitch and --forward are FPS
staging: a real cut finishes near the body, below and beside a first-person view, so the view looks down by --pitch
and each arm segment leans toward the view's forward by --forward (direction plus BIAS times forward, its length
kept). The report gives the right wrist's speed and height per frame, for placing an action's ClipMarks.
"""
import argparse, json, struct
import numpy as np

ap = argparse.ArgumentParser()
ap.add_argument("landmarks"); ap.add_argument("arms"); ap.add_argument("pack_in"); ap.add_argument("pack_out"); ap.add_argument("clip")
ap.add_argument("--base", default="knife_idle"); ap.add_argument("--start", type=int, default=0); ap.add_argument("--end", type=int, default=-1)
ap.add_argument("--sigma", type=float, default=1.2); ap.add_argument("--facing", default="0:8"); ap.add_argument("--pitch", type=float, default=0); ap.add_argument("--forward", type=float, default=0); ap.add_argument("--report")
a = ap.parse_args()


def read_glb(path):
    data = open(path, "rb").read()
    n = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + n])
    off = 20 + n
    blen = struct.unpack_from("<I", data, off)[0]
    return doc, bytearray(data[off + 8: off + 8 + blen])


def write_glb(path, doc, blob):
    blob = bytes(blob) + b"\0" * (-len(blob) % 4)
    doc["buffers"] = [{"byteLength": len(blob)}]
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    with open(path, "wb") as out:
        out.write(b"glTF" + struct.pack("<II", 2, 12 + 8 + len(text) + 8 + len(blob)))
        out.write(struct.pack("<I", len(text)) + b"JSON" + text)
        out.write(struct.pack("<I", len(blob)) + b"BIN\0" + blob)


def accessor(doc, blob, i):
    acc = doc["accessors"][i]; bv = doc["bufferViews"][acc["bufferView"]]
    comps = {"SCALAR": 1, "VEC3": 3, "VEC4": 4}[acc["type"]]
    start = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    arr = np.frombuffer(bytes(blob[start:start + 4 * comps * acc["count"]]), dtype="<f4")
    return arr.reshape(acc["count"], comps) if comps > 1 else arr


# Quaternions as (x, y, z, w) like glTF.
def q2m(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def m2q(m):
    t = np.trace(m)
    if t > 0:
        s = np.sqrt(t + 1) * 2; w = s / 4; x = (m[2, 1] - m[1, 2]) / s; y = (m[0, 2] - m[2, 0]) / s; z = (m[1, 0] - m[0, 1]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = np.sqrt(1 + m[0, 0] - m[1, 1] - m[2, 2]) * 2; w = (m[2, 1] - m[1, 2]) / s; x = s / 4; y = (m[0, 1] + m[1, 0]) / s; z = (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = np.sqrt(1 + m[1, 1] - m[0, 0] - m[2, 2]) * 2; w = (m[0, 2] - m[2, 0]) / s; x = (m[0, 1] + m[1, 0]) / s; y = s / 4; z = (m[1, 2] + m[2, 1]) / s
    else:
        s = np.sqrt(1 + m[2, 2] - m[0, 0] - m[1, 1]) * 2; w = (m[1, 0] - m[0, 1]) / s; x = (m[0, 2] + m[2, 0]) / s; y = (m[1, 2] + m[2, 1]) / s; z = s / 4
    q = np.array([x, y, z, w]); return q / np.linalg.norm(q)


def unit(v): return v / max(np.linalg.norm(v), 1e-9)


def basis(axis, side):
    """Orthonormal basis with columns axis, side (made perpendicular), axis x side."""
    y = unit(axis); s = unit(side - y * np.dot(side, y)); return np.column_stack([y, s, np.cross(y, s)])


# --- base pose: the arms GLB's nodes with the base clip's first frame ---
arms, ablob = read_glb(a.arms)
nodes = arms["nodes"]; names = [n.get("name", "") for n in nodes]; idx = {n: i for i, n in enumerate(names)}
parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
T = {i: np.array(n.get("translation", [0, 0, 0]), float) for i, n in enumerate(nodes)}
R = {i: np.array(n.get("rotation", [0, 0, 0, 1]), float) for i, n in enumerate(nodes)}
S = {i: np.array(n.get("scale", [1, 1, 1]), float) for i, n in enumerate(nodes)}
base = next(an for an in arms["animations"] if an["name"] == a.base)
for ch in base["channels"]:
    smp = base["samplers"][ch["sampler"]]; v = accessor(arms, ablob, smp["output"])[0]
    {"translation": T, "rotation": R, "scale": S}[ch["target"]["path"]][ch["target"]["node"]] = np.array(v, float)


def world(i, rot):
    """World rotation (3x3) and position of node i under local rotations rot."""
    chain = []
    while True:
        chain.append(i)
        if i not in parent: break
        i = parent[i]
    m = np.eye(4)
    for j in reversed(chain):
        l = np.eye(4); l[:3, :3] = q2m(rot[j]) * S[j]; l[:3, 3] = T[j]; m = m @ l
    return m[:3, :3] / np.linalg.norm(m[:3, 0]), m[:3, 3]


# Rest relations measured on the base pose, per side.
SIDES = {"R": dict(sh=12, el=14, wr=16, pi=18, ix=20), "L": dict(sh=11, el=13, wr=15, pi=17, ix=19)}
rest = {}
for s in SIDES:
    up, fo, ha = idx[f"upper_arm.{s}"], idx[f"forearm.{s}"], idx[f"hand.{s}"]
    Ru, pu = world(up, R); Rf, pf = world(fo, R); Rh, ph = world(ha, R)
    _, pix = world(idx[f"f_index.01.{s}"], R); _, ppi = world(idx[f"f_pinky.01.{s}"], R)
    u0, f0 = pf - pu, ph - pf; n0 = unit(np.cross(u0, f0))
    rest[s] = dict(up=up, fo=fo, ha=ha,
                   u_axis=Ru.T @ u0, u_hinge=Ru.T @ n0, f_axis=Rf.T @ f0, f_hinge=Rf.T @ n0,
                   h_axis=Rh.T @ ((pix + ppi) / 2 - ph), h_side=Rh.T @ (pix - ppi))

# --- landmarks into the rig's model space ---
L = json.load(open(a.landmarks)); frames = L["frames"]
end = len(frames) if a.end < 0 else a.end
P = np.array([[p[:3] for p in f["world"]] if f["world"] else [[np.nan] * 3] * 33 for f in frames[a.start:end]], float)
# Fill missing frames by holding the nearest seen one, then smooth over time.
for i in range(len(P)):
    if np.isnan(P[i]).any(): P[i] = P[i - 1] if i else P[next(j for j in range(len(P)) if not np.isnan(P[j]).any())]
if a.sigma > 0:
    k = np.arange(-3 * int(np.ceil(a.sigma)), 3 * int(np.ceil(a.sigma)) + 1); w = np.exp(-k * k / (2 * a.sigma ** 2)); w /= w.sum()
    pad = np.pad(P, ((len(k) // 2, len(k) // 2), (0, 0), (0, 0)), mode="edge")
    P = np.stack([np.tensordot(w, pad[i:i + len(k)], axes=1) for i in range(len(P))])
# One camera for the clip: the body's facing over --facing frames (shoulders' right, hips-to-shoulders up).
f0, f1 = (int(v) for v in a.facing.split(":")); head = P[f0 - a.start:f1 - a.start]
right = unit((head[:, 12] - head[:, 11]).mean(0))
upv = ((head[:, 11] + head[:, 12]) / 2 - (head[:, 23] + head[:, 24]) / 2).mean(0); upv = unit(upv - right * np.dot(upv, right))
fwd = np.cross(upv, right)
if np.dot(fwd, (head[:, 0] - (head[:, 11] + head[:, 12]) / 2).mean(0)) < 0: fwd = -fwd  # the nose is in front
to_model = np.stack([-right, upv, fwd])  # model space: right arm toward -X, up +Y, facing +Z
# The camera looks down by --pitch degrees: the arms' motion is raised by that much about the shoulder line (FPS staging).
th = np.radians(a.pitch); to_model = np.array([[1, 0, 0], [0, np.cos(th), np.sin(th)], [0, -np.sin(th), np.cos(th)]]) @ to_model

rot_frames = []
for p in P:
    rot = dict(R)
    for s, m in SIDES.items():
        r = rest[s]
        u = to_model @ (p[m["el"]] - p[m["sh"]]); f = to_model @ (p[m["wr"]] - p[m["el"]])
        # FPS staging: each segment leans toward the view's forward by --forward (its length kept), so a cut stays in front.
        Z = np.array([0, 0, 1.0])
        u = (unit(u) + a.forward * Z) * np.linalg.norm(u); f = (unit(f) + a.forward * Z) * np.linalg.norm(f)
        n = np.cross(u, f)
        if np.linalg.norm(n) < 1e-3 * np.linalg.norm(u) * np.linalg.norm(f) and rot_frames: n = rot_frames[-1][("n", s)]
        n = unit(n)
        # Upper arm, then forearm: world rotations taking the rest axis/hinge to the video's, then local under the parent.
        Wp, _ = world(parent[r["up"]], rot)
        Wu = basis(u, n) @ basis(r["u_axis"], r["u_hinge"]).T
        rot[r["up"]] = m2q(Wp.T @ Wu)
        Wf = basis(f, n) @ basis(r["f_axis"], r["f_hinge"]).T
        rot[r["fo"]] = m2q(Wu.T @ Wf)
        hand_axis = to_model @ ((p[m["ix"]] + p[m["pi"]]) / 2 - p[m["wr"]]); hand_side = to_model @ (p[m["ix"]] - p[m["pi"]])
        Wh = basis(hand_axis, hand_side) @ basis(r["h_axis"], r["h_side"]).T
        rot[r["ha"]] = m2q(Wf.T @ Wh)
        rot[("n", s)] = n
    rot_frames.append(rot)

# Keep quaternions on one hemisphere frame to frame.
joints = [i for i in range(len(nodes)) if i in R]
for i in joints:
    for k in range(1, len(rot_frames)):
        if np.dot(rot_frames[k][i], rot_frames[k - 1][i]) < 0: rot_frames[k][i] = -rot_frames[k][i]

# --- write the pack: its skeleton, this one clip ---
pack, pblob = read_glb(a.pack_in)
pidx = {n.get("name", ""): i for i, n in enumerate(pack["nodes"])}
pack["animations"] = []; keep_acc = set()
for sk in pack.get("skins", []):
    if "inverseBindMatrices" in sk: keep_acc.add(sk["inverseBindMatrices"])
# Drop every accessor and view but the skin's, rebuild the blob.
old_acc = pack["accessors"]; old_bv = pack["bufferViews"]; new_blob = bytearray(); pack["accessors"] = []; pack["bufferViews"] = []


def add(arr, typ, minmax=False):
    raw = np.ascontiguousarray(arr, dtype="<f4").tobytes()
    while len(new_blob) % 4: new_blob.append(0)
    pack["bufferViews"].append({"buffer": 0, "byteOffset": len(new_blob), "byteLength": len(raw)}); new_blob.extend(raw)
    acc = {"bufferView": len(pack["bufferViews"]) - 1, "componentType": 5126, "count": len(arr), "type": typ}
    if minmax: acc["min"] = [float(np.min(arr))]; acc["max"] = [float(np.max(arr))]
    pack["accessors"].append(acc); return len(pack["accessors"]) - 1


for sk in pack.get("skins", []):
    if "inverseBindMatrices" in sk:
        o = old_acc[sk["inverseBindMatrices"]]; bv = old_bv[o["bufferView"]]
        st = bv.get("byteOffset", 0) + o.get("byteOffset", 0)
        m = np.frombuffer(bytes(pblob[st:st + 64 * o["count"]]), dtype="<f4")
        while len(new_blob) % 4: new_blob.append(0)
        pack["bufferViews"].append({"buffer": 0, "byteOffset": len(new_blob), "byteLength": m.nbytes}); new_blob.extend(m.tobytes())
        pack["accessors"].append({**{k: v for k, v in o.items() if k not in ("bufferView", "byteOffset")}, "bufferView": len(pack["bufferViews"]) - 1})
        sk["inverseBindMatrices"] = len(pack["accessors"]) - 1
fps = L["fps"]; times = add(np.arange(len(rot_frames)) / fps, "SCALAR", minmax=True)
channels, samplers = [], []
for i in joints:
    name = names[i]
    if name not in pidx: continue
    samplers.append({"input": times, "output": add(np.array([f[i] for f in rot_frames]), "VEC4"), "interpolation": "LINEAR"})
    channels.append({"sampler": len(samplers) - 1, "target": {"node": pidx[name], "path": "rotation"}})
    samplers.append({"input": times, "output": add(np.array([T[i]] * len(rot_frames)), "VEC3"), "interpolation": "LINEAR"})
    channels.append({"sampler": len(samplers) - 1, "target": {"node": pidx[name], "path": "translation"}})
pack["animations"] = [{"name": a.clip, "channels": channels, "samplers": samplers}]
write_glb(a.pack_out, pack, new_blob)

# Report: the right wrist's speed per frame (model space, m/s scaled to the video body), to place the strike.
wr = np.array([to_model @ (p[16] - p[12]) for p in P])
speed = np.r_[0, np.linalg.norm(np.diff(wr, axis=0), axis=1) * fps]
rep = {"frames": len(P), "seconds": len(P) / fps, "peak": int(speed.argmax()), "speed": [round(float(v), 2) for v in speed],
       "wristHeight": [round(float(v[1]), 3) for v in wr]}
if a.report: json.dump(rep, open(a.report, "w"))
print(f"{a.clip}: {len(P)} frames ({len(P) / fps:.2f} s), peak wrist speed {speed.max():.1f} m/s at frame {speed.argmax()}")
