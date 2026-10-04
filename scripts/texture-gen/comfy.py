"""Tiny ComfyUI API client for the 9373 texture trials: submit an API graph, wait, fetch outputs.

Usage: python3 comfy.py <graph.json> <out-prefix> [upload images...]
Uploads any given images first (as input files named by basename); the graph refers to them by that name.
"""
import json, sys, time, urllib.request, urllib.parse, uuid, os

URL = os.environ.get("COMFY_URL", "http://192.168.1.20:8188")


def post(path, body):
    req = urllib.request.Request(URL + path, data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
    return json.load(urllib.request.urlopen(req, timeout=60))


def upload(path):
    boundary = uuid.uuid4().hex
    name = os.path.basename(path)
    data = open(path, "rb").read()
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{name}\"\r\n"
            f"Content-Type: image/png\r\n\r\n").encode() + data + f"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(URL + "/upload/image", data=body, headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    return json.load(urllib.request.urlopen(req, timeout=120))


def run(graph, prefix, timeout=1800):
    started = time.time()
    pid = post("/prompt", {"prompt": graph, "client_id": "riders-9373"})["prompt_id"]
    while True:
        time.sleep(3)
        hist = json.load(urllib.request.urlopen(f"{URL}/history/{pid}", timeout=60))
        if pid in hist:
            entry = hist[pid]
            status = entry.get("status", {})
            if status.get("completed") or status.get("status_str") in ("success", "error"):
                break
        if time.time() - started > timeout:
            raise SystemExit(f"timeout waiting for {pid}")
    elapsed = time.time() - started
    if status.get("status_str") == "error":
        print(json.dumps(status, indent=1)[:3000])
        raise SystemExit(f"{pid} failed after {elapsed:.0f}s")
    paths = []
    for node, out in entry.get("outputs", {}).items():
        for i, img in enumerate(out.get("images", [])):
            q = urllib.parse.urlencode({"filename": img["filename"], "subfolder": img.get("subfolder", ""), "type": img.get("type", "output")})
            path = f"{prefix}-{node}-{i}.png"
            open(path, "wb").write(urllib.request.urlopen(f"{URL}/view?{q}", timeout=120).read())
            paths.append(path)
    print(json.dumps({"prompt_id": pid, "seconds": round(elapsed, 1), "outputs": paths}))
    return paths


if __name__ == "__main__":
    import urllib.parse  # noqa: F401
    for image in sys.argv[3:]:
        upload(image)
    run(json.load(open(sys.argv[1])), sys.argv[2])
