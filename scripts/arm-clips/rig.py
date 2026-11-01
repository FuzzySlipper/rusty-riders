"""A first-person arms rig as the arm-clips scripts see it: its profile (content/held/rigs/<id>.json), its model's
skeleton posed at the first frame of the rig's own clip, and where the game's view is in the model's space.

The camera follows the game's framing: the Engine places the model so its right upper arm (at the rig clip's start)
sits at the arms' shoulder (content/held/arms.json) plus the rig's carry (content/held/clips.json), turned by the
rig's turn and scaled by its scale; the view is camera space's origin looking along -Z. In the model's glTF space that
is a point looking along the model's forward (+Z for the rigs turned 180 degrees about Y).
"""
import json, os, struct
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONTENT = os.path.join(ROOT, "content")


def read_glb(path):
    data = open(path, "rb").read()
    n = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + n])
    off = 20 + n
    blen = struct.unpack_from("<I", data, off)[0]
    return doc, bytearray(data[off + 8: off + 8 + blen])


def accessor(doc, blob, i):
    acc = doc["accessors"][i]; bv = doc["bufferViews"][acc["bufferView"]]
    comps = {"SCALAR": 1, "VEC3": 3, "VEC4": 4, "MAT4": 16}[acc["type"]]
    start = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    arr = np.frombuffer(bytes(blob[start:start + 4 * comps * acc["count"]]), dtype="<f4")
    return arr.reshape(acc["count"], comps) if comps > 1 else arr


# Quaternions as (x, y, z, w) like glTF.
def q2m(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def turn(degrees):
    """HeldFrames.Turn: degrees about X, Y, Z as System.Numerics' yaw (Y), pitch (X), roll (Z)."""
    x, y, z = np.radians(degrees)
    rx = np.array([[1, 0, 0], [0, np.cos(x), -np.sin(x)], [0, np.sin(x), np.cos(x)]])
    ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
    rz = np.array([[np.cos(z), -np.sin(z), 0], [np.sin(z), np.cos(z), 0], [0, 0, 1]])
    return ry @ rx @ rz


class Rig:
    """A rig profile and its model's skeleton at the start of a base clip (the rig's own clip by default)."""

    def __init__(self, rig_id, base=None):
        self.id = rig_id
        self.profile = json.load(open(os.path.join(CONTENT, "held/rigs", f"{rig_id}.json")))
        self.model = os.path.join(CONTENT, self.profile["model"])
        self.doc, self.blob = read_glb(self.model)
        nodes = self.doc["nodes"]
        self.names = [n.get("name", "") for n in nodes]
        self.idx = {n: i for i, n in enumerate(self.names)}
        self.parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
        self.T = {i: np.array(n.get("translation", [0, 0, 0]), float) for i, n in enumerate(nodes)}
        self.R = {i: np.array(n.get("rotation", [0, 0, 0, 1]), float) for i, n in enumerate(nodes)}
        self.S = {i: np.array(n.get("scale", [1, 1, 1]), float) for i, n in enumerate(nodes)}
        self.base = base or self.profile["clip"]
        clip = next((an for an in self.doc.get("animations", []) if an["name"] == self.base), None)
        if clip is None: raise SystemExit(f"rig {rig_id}: no clip {self.base!r} in {self.model}")
        for ch in clip["channels"]:
            smp = clip["samplers"][ch["sampler"]]; v = accessor(self.doc, self.blob, smp["output"])
            v = v[1] if smp.get("interpolation") == "CUBICSPLINE" else v[0]  # a cubic key's value sits between its tangents
            {"translation": self.T, "rotation": self.R, "scale": self.S}[ch["target"]["path"]][ch["target"]["node"]] = np.array(v, float)

    def arm(self, side):
        """An arm's joints by node index: 'main' (right) or 'off' (left), as the profile names them."""
        a = self.profile[side]; i = self.idx
        return dict(upper=i[a["upper"]], lower=i[a["lower"]], hand=i[a["hand"]],
                    fingers=[[i[j] for j in f] for f in a["fingers"]], thumb=[i[j] for j in a["thumb"]],
                    twist=[(i[t["joint"]], t["share"]) for t in a.get("twist", [])])

    def world(self, i, rot=None):
        """World rotation (3x3) and position of node i under local rotations rot (the base pose's by default)."""
        rot = rot or self.R
        chain = []
        while True:
            chain.append(i)
            if i not in self.parent: break
            i = self.parent[i]
        m = np.eye(4)
        for j in reversed(chain):
            l = np.eye(4); l[:3, :3] = q2m(rot[j]) * self.S[j]; l[:3, 3] = self.T[j]; m = m @ l
        return m[:3, :3] / np.linalg.norm(m[:3, 0]), m[:3, 3]

    def camera(self):
        """Where the game's view is in the model's space (see the module's note)."""
        arms = json.load(open(os.path.join(CONTENT, "held/arms.json")))
        clips = json.load(open(os.path.join(CONTENT, "held/clips.json")))
        carry = clips["rigs"].get(self.id, {}).get("carry", [0, 0, 0])
        at = np.array(arms["shoulder"], float) + np.array(carry, float)
        _, upper = self.world(self.arm("main")["upper"])
        return upper + turn(self.profile["turn"]).T @ -at / self.profile["scale"]

    def palm(self):
        """The palm's offset from the hand joint (the profile's, in the hand's canonical frame, metres)."""
        return np.array(self.profile["palm"], float)
