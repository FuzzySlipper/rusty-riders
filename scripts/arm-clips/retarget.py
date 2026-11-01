"""A joint track -> a PSX-arms clip pack (skeleton-only GLB with one animation).

The track is either sample.py's (any authored clip: Mixamo, UAL, BVH mocap...) or extract.py's MediaPipe landmarks
from a video; both give each arm's shoulder, elbow, wrist and index/pinky knuckles, the hips and a point in front.

The camera is one fixed frame for the whole clip, from the body's facing over the first frames (a first-person view
looks at the target, it does not turn with the swing). Each arm's upper arm and forearm point where the video's do,
bent in the video's elbow plane; the hand turns to the video's wrist->knuckles line and index->pinky side. Everything
else (clavicles, fingers, root) holds the base clip's first frame (knife_idle: the fist closed on a grip).

    retarget.py TRACK.json ARMS.glb PACK_IN.glb PACK_OUT.glb CLIP_NAME [--start F] [--end F] [--sigma S]
        [--facing F0:F1] [--pitch DEGREES] [--forward BIAS] [--cone DEGREES] [--aim YAW,PITCH] [--reach FRACTION] [--clear METRES] [--camera X,Y,Z] [--sides R,L]
        [--report REPORT.json]

--facing picks the frames whose body facing is the view (where the cut is aimed). --pitch and --forward are FPS
staging: a real cut finishes near the body, below and beside a first-person view, so the view looks down by --pitch
and each arm segment leans toward the view's forward by --forward (direction plus BIAS times forward, its length
kept). --cone is the general staging: each frame the whole arm turns rigidly about its shoulder so the
shoulder-wrist line lies within a soft cone of that half-angle around --aim (degrees right and up of the view's
forward); the elbow's bend and plane and the wrist's turn ride along, so a third-person wind-up behind the head is
pulled into view with its shape kept. --reach opens the elbow so the wrist is at least that fraction of the arm's
length from the shoulder (FPS arms hold the hands out where they can be seen). --clear keeps the elbow and
forearm at least that far from the view (--camera, in the arms model's space; the PSX pack's own camera by default):
a frame whose arm passes closer turns, again rigidly about the shoulder, away from the view until it clears, so an
overhead wind-up does not sweep the elbow across the lens. --sides names the arms the track drives (the others hold the base pose). The report gives the right wrist's speed and height per frame, for placing an action's ClipMarks.
"""
import argparse, json, struct
import numpy as np

ap = argparse.ArgumentParser()
ap.add_argument("track"); ap.add_argument("arms"); ap.add_argument("pack_in"); ap.add_argument("pack_out"); ap.add_argument("clip")
ap.add_argument("--base", default="knife_idle"); ap.add_argument("--start", type=int, default=0); ap.add_argument("--end", type=int, default=-1)
ap.add_argument("--sigma", type=float, default=1.2); ap.add_argument("--facing", default="0:8"); ap.add_argument("--pitch", type=float, default=0); ap.add_argument("--forward", type=float, default=0); ap.add_argument("--report"); ap.add_argument("--sides", default="R,L"); ap.add_argument("--cone", type=float, default=0); ap.add_argument("--aim", default="0,0"); ap.add_argument("--reach", type=float, default=0); ap.add_argument("--clear", type=float, default=0); ap.add_argument("--camera", default="0,1.743,0")
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


def rotation(axis, angle):
    k = unit(axis); K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    return np.eye(3) + np.sin(angle) * K + (1 - np.cos(angle)) * K @ K


yaw, pitch = (np.radians(float(v)) for v in a.aim.split(","))
AIM = np.array([np.sin(yaw) * np.cos(pitch) * -1, np.sin(pitch), np.cos(yaw) * np.cos(pitch)])  # model space: right is -X


CAMERA = np.array([float(v) for v in a.camera.split(",")])


def nearest(p0, p1, c):
    """The point of segment p0-p1 nearest c."""
    d = p1 - p0; t = np.clip(np.dot(c - p0, d) / max(np.dot(d, d), 1e-12), 0, 1); return p0 + t * d


def clear_of_view(shoulder, u, f):
    """The rotation about the shoulder taking the elbow and forearm (upper arm u, forearm f) at least --clear from the
    camera. The upper arm's root is never far from the view (the shoulder itself is about 25 cm away), so the measure
    starts at the elbow."""
    turn = np.eye(3)
    if a.clear <= 0: return turn
    for _ in range(45):  # 2 degree steps, at most 90
        elbow = shoulder + turn @ u; wrist = elbow + turn @ f
        q = nearest(elbow, wrist, CAMERA)
        if np.linalg.norm(q - CAMERA) >= a.clear: break
        # Push mostly along the view's forward (+Z): the shortest way out of the lens is often out of the frame too.
        axis = np.cross(q - shoulder, unit(q - CAMERA) + 2 * np.array([0, 0, 1.0]))
        if np.linalg.norm(axis) < 1e-9: axis = np.array([1.0, 0, 0])
        turn = rotation(axis, np.radians(2)) @ turn
    return turn


def into_cone(d):
    """The rotation turning direction d into the soft cone around AIM (identity without --cone)."""
    if a.cone <= 0: return np.eye(3)
    dn = unit(d); theta = np.arccos(np.clip(np.dot(dn, AIM), -1, 1)); axis = np.cross(AIM, dn)
    if np.linalg.norm(axis) < 1e-6: return np.eye(3)
    limit = np.radians(a.cone); return rotation(axis, limit * np.tanh(theta / limit) - theta)


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
SIDES = [side for side in a.sides.split(",") if side]
rest = {}
for s in SIDES:
    up, fo, ha = idx[f"upper_arm.{s}"], idx[f"forearm.{s}"], idx[f"hand.{s}"]
    Ru, pu = world(up, R); Rf, pf = world(fo, R); Rh, ph = world(ha, R)
    _, pix = world(idx[f"f_index.01.{s}"], R); _, ppi = world(idx[f"f_pinky.01.{s}"], R)
    u0, f0 = pf - pu, ph - pf; n0 = unit(np.cross(u0, f0))
    rest[s] = dict(up=up, fo=fo, ha=ha, shoulder=pu,
                   u_axis=Ru.T @ u0, u_hinge=Ru.T @ n0, f_axis=Rf.T @ f0, f_hinge=Rf.T @ n0,
                   h_axis=Rh.T @ ((pix + ppi) / 2 - ph), h_side=Rh.T @ (pix - ppi))

# --- the track into the rig's model space ---
# MediaPipe Pose landmark indices for the track's points (extract.py output); "front" is the nose.
MEDIAPIPE = {"shoulder.R": 12, "elbow.R": 14, "wrist.R": 16, "pinky.R": 18, "index.R": 20,
             "shoulder.L": 11, "elbow.L": 13, "wrist.L": 15, "pinky.L": 17, "index.L": 19, "hip.R": 24, "hip.L": 23, "front": 0}
L = json.load(open(a.track))
if "points" in L:
    col = {k: L["points"].index(k) for k in MEDIAPIPE}
    rows = [[f[col[k]] for k in MEDIAPIPE] for f in L["frames"]]
else:
    rows = [[f["world"][i][:3] for i in MEDIAPIPE.values()] if f["world"] else [[np.nan] * 3] * len(MEDIAPIPE) for f in L["frames"]]
J = {k: i for i, k in enumerate(MEDIAPIPE)}
end = len(rows) if a.end < 0 else a.end
P = np.array(rows[a.start:end], float)
# Fill missing frames by holding the nearest seen one, then smooth over time.
for i in range(len(P)):
    if np.isnan(P[i]).any(): P[i] = P[i - 1] if i else P[next(j for j in range(len(P)) if not np.isnan(P[j]).any())]
if a.sigma > 0:
    k = np.arange(-3 * int(np.ceil(a.sigma)), 3 * int(np.ceil(a.sigma)) + 1); w = np.exp(-k * k / (2 * a.sigma ** 2)); w /= w.sum()
    pad = np.pad(P, ((len(k) // 2, len(k) // 2), (0, 0), (0, 0)), mode="edge")
    P = np.stack([np.tensordot(w, pad[i:i + len(k)], axes=1) for i in range(len(P))])
# One camera for the clip: the body's facing over --facing frames (shoulders' right, hips-to-shoulders up).
f0, f1 = (int(v) for v in a.facing.split(":")); head = P[f0 - a.start:f1 - a.start]
right = unit((head[:, J["shoulder.R"]] - head[:, J["shoulder.L"]]).mean(0))
upv = ((head[:, J["shoulder.L"]] + head[:, J["shoulder.R"]]) / 2 - (head[:, J["hip.L"]] + head[:, J["hip.R"]]) / 2).mean(0); upv = unit(upv - right * np.dot(upv, right))
fwd = np.cross(upv, right)
if np.dot(fwd, (head[:, J["front"]] - (head[:, J["shoulder.L"]] + head[:, J["shoulder.R"]]) / 2).mean(0)) < 0: fwd = -fwd  # the front point is ahead
to_model = np.stack([-right, upv, fwd])  # model space: right arm toward -X, up +Y, facing +Z
# The camera looks down by --pitch degrees: the arms' motion is raised by that much about the shoulder line (FPS staging).
th = np.radians(a.pitch); to_model = np.array([[1, 0, 0], [0, np.cos(th), np.sin(th)], [0, -np.sin(th), np.cos(th)]]) @ to_model

rot_frames = []; clearance = []
for p in P:
    rot = dict(R)
    for s in SIDES:
        r = rest[s]; m = {k: J[f"{k}.{s}"] for k in ("shoulder", "elbow", "wrist", "index", "pinky")}
        u = to_model @ (p[m["elbow"]] - p[m["shoulder"]]); f = to_model @ (p[m["wrist"]] - p[m["elbow"]])
        # FPS staging: each segment leans toward the view's forward by --forward (its length kept), so a cut stays in front.
        Z = np.array([0, 0, 1.0])
        u = (unit(u) + a.forward * Z) * np.linalg.norm(u); f = (unit(f) + a.forward * Z) * np.linalg.norm(f)
        lu, lf = np.linalg.norm(u), np.linalg.norm(f); hinge = np.cross(u, f)
        want = a.reach * (lu + lf)
        if np.linalg.norm(u + f) < want and np.linalg.norm(hinge) > 1e-9:
            inner = lambda d: np.arccos(np.clip((lu * lu + lf * lf - d * d) / (2 * lu * lf), -1, 1))
            f = rotation(hinge, -(inner(want) - inner(np.linalg.norm(u + f)))) @ f  # opening the elbow in its own plane
        C = into_cone(u + f); C = clear_of_view(r["shoulder"], C @ u, C @ f) @ C; u = C @ u; f = C @ f
        elbow = r["shoulder"] + u
        gap = np.linalg.norm(nearest(elbow, elbow + f, CAMERA) - CAMERA)
        clearance.append(gap)
        n = np.cross(u, f)
        if np.linalg.norm(n) < 1e-3 * np.linalg.norm(u) * np.linalg.norm(f) and rot_frames: n = rot_frames[-1][("n", s)]
        n = unit(n)
        # Upper arm, then forearm: world rotations taking the rest axis/hinge to the video's, then local under the parent.
        Wp, _ = world(parent[r["up"]], rot)
        Wu = basis(u, n) @ basis(r["u_axis"], r["u_hinge"]).T
        rot[r["up"]] = m2q(Wp.T @ Wu)
        Wf = basis(f, n) @ basis(r["f_axis"], r["f_hinge"]).T
        rot[r["fo"]] = m2q(Wu.T @ Wf)
        hand_axis = C @ to_model @ ((p[m["index"]] + p[m["pinky"]]) / 2 - p[m["wrist"]]); hand_side = C @ to_model @ (p[m["index"]] - p[m["pinky"]])
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

# Report: the right wrist's speed per frame (model space, m/s at the source body's scale), to place the strike.
wr = np.array([to_model @ (p[J["wrist.R"]] - p[J["shoulder.R"]]) for p in P])
speed = np.r_[0, np.linalg.norm(np.diff(wr, axis=0), axis=1) * fps]
rep = {"clearance": round(float(min(clearance)), 3) if clearance else None, "frames": len(P), "seconds": len(P) / fps, "peak": int(speed.argmax()), "speed": [round(float(v), 2) for v in speed],
       "wristHeight": [round(float(v[1]), 3) for v in wr]}
if a.report: json.dump(rep, open(a.report, "w"))
print(f"{a.clip}: {len(P)} frames ({len(P) / fps:.2f} s), peak wrist speed {speed.max():.1f} m/s at frame {speed.argmax()}, "
      f"elbow and forearm at least {min(clearance):.2f} m from the view")
