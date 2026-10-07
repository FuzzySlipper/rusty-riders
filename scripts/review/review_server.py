#!/usr/bin/env python3
"""A small LAN web app for a human pass over a folder of generated or source images.

    scripts/review/review_server.py <image-dir> [--port 8790] [--bind 0.0.0.0] [--title "..."]
        [--info checks.json ...] [--order ranking.txt]

Open http://<this host>:<port>/ and work through pages of thumbnails:
- **Flag.** Click a thumbnail to toggle reject, or press x (reject), k (keep) or u (clear) on the selected one.
  Arrow keys move the selection.
- **Inspect.** Space or Enter opens the selected image full size, tiled 2x2 if you press t, with a note field.
- **Crop.** Drag rectangles on the full-size view to mark crops; there can be several per image (sprite sheets).
  Backspace removes the last one.
- **Filter.** Show all, unmarked, rejected, kept or cropped images.

Every change is saved at once to <image-dir>/review.json:
`{"items": {"<relative path>": {"mark": "reject"|"keep"|null, "note": str, "crops": [[x, y, w, h], ...]}}}`, with crops
in source-image pixels. scripts/review/apply_review.py turns that into a filtered set and cut-out crops.

--info takes JSON files of per-image facts: a list of {"path": ..., ...} objects such as
scripts/texture-gen/checks.py --json writes, matched to images by file name. Their "flags" show as red badges,
"rank", "score", "total" and "judge" as plain badges, and every fact in the file-name tooltip.
--order takes a text file of image paths or names, best first (such as merge_rankings.py output), to sort by.

The server serves only images found under <image-dir> (png, jpg, jpeg, webp, gif) and writes only review.json.
Thumbnails are made in the background at start-up and cached in <image-dir>/.review-thumbs. Needs Pillow.
"""
import argparse
import html
import http.server
import io
import json
import os
import re
import threading
import urllib.parse

from PIL import Image

EXTENSIONS = (".png", ".jpg", ".jpeg", ".webp", ".gif")
THUMB = 384


class Review:
    """The image set, its facts, and the review state, saved after every change."""

    def __init__(self, root: str, info_paths: list[str], order_path: str | None, title: str):
        self.root = os.path.abspath(root)
        self.title = title
        self.state_path = os.path.join(self.root, "review.json")
        self.thumbs = os.path.join(self.root, ".review-thumbs")
        self.lock = threading.Lock()
        self.images = sorted(
            os.path.relpath(os.path.join(directory, name), self.root)
            for directory, dirs, names in os.walk(self.root)
            if not os.path.basename(directory).startswith(".")
            for name in names if name.lower().endswith(EXTENSIONS))
        self.state = json.load(open(self.state_path)) if os.path.exists(self.state_path) else {"items": {}}
        self.info = self._facts(info_paths)
        if order_path:
            wanted = [line.strip() for line in open(order_path) if line.strip()]
            rank = {}
            for position, line in enumerate(wanted):
                rank.setdefault(os.path.basename(line.split()[-1]), position)
            self.images.sort(key=lambda path: rank.get(os.path.basename(path), len(rank)))

    def _facts(self, paths: list[str]) -> dict[str, dict]:
        facts: dict[str, dict] = {}
        for path in paths:
            for row in json.load(open(path)):
                name = os.path.basename(row.get("path") or row.get("image") or "")
                facts.setdefault(name, {}).update({k: v for k, v in row.items() if k not in ("path", "image")})
        return facts

    def items(self) -> list[dict]:
        out = []
        for index, path in enumerate(self.images):
            entry = self.state["items"].get(path, {})
            # The version names the file and its age, so a browser never reuses a cached picture from another set.
            version = f"{os.path.getmtime(self.file(index)):.0f}-{abs(hash(path)) % 10**8}"
            out.append({"id": index, "path": path, "version": version, "mark": entry.get("mark"), "note": entry.get("note", ""),
                        "crops": entry.get("crops", []), "info": self.info.get(os.path.basename(path), {})})
        return out

    def update(self, index: int, change: dict) -> dict:
        path = self.images[index]
        with self.lock:
            entry = self.state["items"].setdefault(path, {})
            for key in ("mark", "note", "crops"):
                if key in change:
                    entry[key] = change[key]
            if not entry.get("mark") and not entry.get("note") and not entry.get("crops"):
                self.state["items"].pop(path, None)
            temp = self.state_path + ".tmp"
            with open(temp, "w") as out:
                json.dump(self.state, out, indent=1, sort_keys=True)
            os.replace(temp, self.state_path)
            return entry

    def warm(self) -> None:
        """Make every missing thumbnail in the background, so the first page opens from the cache."""
        def run():
            for index in range(len(self.images)):
                try:
                    self.thumbnail(index)
                except OSError:
                    pass  # an unreadable image shows as a broken thumbnail; the page still works
        threading.Thread(target=run, daemon=True).start()

    def file(self, index: int) -> str:
        return os.path.join(self.root, self.images[index])

    def thumbnail(self, index: int) -> bytes:
        source = self.file(index)
        cached = os.path.join(self.thumbs, re.sub(r"[^A-Za-z0-9_.-]", "_", self.images[index]) + ".jpg")
        if not os.path.exists(cached) or os.path.getmtime(cached) < os.path.getmtime(source):
            os.makedirs(self.thumbs, exist_ok=True)
            image = Image.open(source)
            image.draft("RGB", (THUMB, THUMB))  # JPEGs decode at a reduced size
            image.thumbnail((THUMB, THUMB), Image.BILINEAR)
            if image.mode not in ("RGB", "L"):
                background = Image.new("RGB", image.size, (128, 128, 128))
                background.paste(image.convert("RGBA"), mask=image.convert("RGBA").split()[-1])
                image = background
            image.convert("RGB").save(cached, quality=85)
        return open(cached, "rb").read()


def handler(review: Review):
    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def send(self, body: bytes, kind: str, status: int = 200):
            self.send_response(status)
            self.send_header("Content-Type", kind)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store" if kind.startswith(("application/json", "text/html")) else "max-age=3600")
            self.end_headers()
            self.wfile.write(body)

        def index_of(self, text: str) -> int | None:
            return int(text) if text.isdigit() and int(text) < len(review.images) else None

        def do_GET(self):
            url = urllib.parse.urlparse(self.path)
            parts = url.path.strip("/").split("/")
            if url.path == "/":
                self.send(PAGE.replace("__TITLE__", html.escape(review.title)).encode(), "text/html; charset=utf-8")
            elif url.path == "/api/items":
                self.send(json.dumps({"title": review.title, "items": review.items()}).encode(), "application/json")
            elif len(parts) == 2 and parts[0] in ("thumb", "image") and (index := self.index_of(parts[1])) is not None:
                if parts[0] == "thumb":
                    self.send(review.thumbnail(index), "image/jpeg")
                else:
                    path = review.file(index)
                    kind = {"jpg": "jpeg"}.get(path.rsplit(".", 1)[-1].lower(), path.rsplit(".", 1)[-1].lower())
                    self.send(open(path, "rb").read(), f"image/{kind}")
            else:
                self.send(b"not found", "text/plain", 404)

        def do_POST(self):
            parts = urllib.parse.urlparse(self.path).path.strip("/").split("/")
            if len(parts) == 3 and parts[:2] == ["api", "item"] and (index := self.index_of(parts[2])) is not None:
                change = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
                if change.get("mark") not in (None, "reject", "keep"):
                    self.send(b"bad mark", "text/plain", 400)
                    return
                self.send(json.dumps(review.update(index, change)).encode(), "application/json")
            else:
                self.send(b"not found", "text/plain", 404)

    return Handler


PAGE = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__</title>
<style>
:root { --bg:#14161a; --panel:#1f232a; --text:#e8ebf0; --muted:#97a0ad; --reject:#e5484d; --keep:#46a758; --crop:#f5a623; --sel:#4aa3ff; }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--text); font:14px/1.4 system-ui, sans-serif; }
header { position:sticky; top:0; z-index:5; display:flex; flex-wrap:wrap; gap:10px; align-items:center; padding:10px 16px; background:var(--panel); border-bottom:1px solid #333; }
header h1 { font-size:16px; margin:0 12px 0 0; }
header .counts { color:var(--muted); }
button, select, input { background:#2b313a; color:var(--text); border:1px solid #3b4350; border-radius:6px; padding:5px 10px; font:inherit; }
button.on { border-color:var(--sel); background:#24405e; }
#grid { display:grid; gap:8px; padding:12px 16px; grid-template-columns:repeat(auto-fill, minmax(var(--size, 200px), 1fr)); }
.card { position:relative; background:#0d0f12; border:3px solid transparent; border-radius:6px; overflow:hidden; cursor:pointer; }
.card img { display:block; width:100%; aspect-ratio:1; object-fit:contain; background:#0d0f12; }
.card.reject { border-color:var(--reject); } .card.reject img { opacity:.35; }
.card.keep { border-color:var(--keep); }
.card.sel { outline:3px solid var(--sel); outline-offset:1px; }
.card .name { position:absolute; left:0; right:0; bottom:0; padding:2px 6px; font-size:11px; background:#000a; color:#ddd; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.badges { position:absolute; top:4px; left:4px; display:flex; flex-wrap:wrap; gap:3px; }
.badge { font-size:10px; padding:1px 5px; border-radius:4px; background:#000b; }
.badge.bad { background:var(--reject); } .badge.crop { background:var(--crop); color:#000; }
footer { display:flex; gap:8px; justify-content:center; padding:12px; }
#help { color:var(--muted); font-size:12px; }
#view { position:fixed; inset:0; z-index:10; display:none; background:#000e; }
#view.open { display:flex; flex-direction:column; }
#viewbar { display:flex; gap:10px; align-items:center; padding:8px 16px; background:var(--panel); }
#viewbar input { flex:1; }
#stage { position:relative; flex:1; display:flex; align-items:center; justify-content:center; overflow:hidden; }
#canvas { max-width:100%; max-height:100%; cursor:crosshair; }
</style></head><body>
<header>
  <h1>__TITLE__</h1>
  <span class="counts" id="counts"></span>
  <span>Show <select id="filter"><option value="all">all</option><option value="unmarked">unmarked</option>
    <option value="reject">rejected</option><option value="keep">kept</option><option value="crops">with crops</option></select></span>
  <span>Size <input id="size" type="range" min="120" max="420" value="200"></span>
  <span>Per page <select id="per"><option>48</option><option selected>96</option><option>192</option></select></span>
  <span id="help">click: reject · x reject · k keep · u clear · arrows move · space open · t tile · drag to crop · backspace undo crop · esc close</span>
</header>
<div id="grid"></div>
<footer><button id="prev">◀ Prev</button><span id="page"></span><button id="next">Next ▶</button></footer>
<div id="view"><div id="viewbar"><b id="vname"></b><button id="vtile">Tile 2×2 (t)</button>
  <button id="vreject">Reject (x)</button><button id="vkeep">Keep (k)</button><button id="vclear">Clear (u)</button>
  <input id="vnote" placeholder="note (saved on change)"><button id="vclose">Close (esc)</button></div>
  <div id="stage"><canvas id="canvas"></canvas></div></div>
<script>
const BADGE_KEYS = ['rank', 'score', 'total', 'judge'];
let items = [], page = 0, selected = 0, tile = false, drawing = null, viewImage = null;
const $ = id => document.getElementById(id);
const per = () => +$('per').value;
function visible() {
  const f = $('filter').value;
  return items.filter(i => f === 'all' || (f === 'unmarked' && !i.mark) || i.mark === f || (f === 'crops' && i.crops.length));
}
async function save(item, change) {
  Object.assign(item, change);
  await fetch('/api/item/' + item.id, {method: 'POST', body: JSON.stringify(change)});
  render();
}
function escapeAttr(text) { return text.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;'); }
function badgeHtml(item) {
  const b = [];
  for (const [k, v] of Object.entries(item.info || {})) {
    if (k === 'flags') { for (const f of v) b.push(`<span class="badge bad">${f}</span>`); continue; }
    if (BADGE_KEYS.includes(k) && (typeof v === 'number' || typeof v === 'string')) b.push(`<span class="badge">${k} ${v}</span>`);
  }
  if (item.crops.length) b.push(`<span class="badge crop">${item.crops.length} crop${item.crops.length > 1 ? 's' : ''}</span>`);
  if (item.note) b.push(`<span class="badge">✎</span>`);
  return b.join('');
}
function render() {
  const list = visible(), pages = Math.max(1, Math.ceil(list.length / per()));
  page = Math.min(page, pages - 1);
  const shown = list.slice(page * per(), (page + 1) * per());
  selected = Math.min(selected, Math.max(0, shown.length - 1));
  $('grid').style.setProperty('--size', $('size').value + 'px');
  $('grid').innerHTML = shown.map((i, n) => `<div class="card ${i.mark || ''} ${n === selected ? 'sel' : ''}" data-n="${n}">
      <img loading="lazy" src="/thumb/${i.id}?v=${i.version}" alt=""><div class="badges">${badgeHtml(i)}</div><div class="name" title="${i.path}\n${escapeAttr(JSON.stringify(i.info))}">${i.path}</div></div>`).join('');
  const c = k => items.filter(i => i.mark === k).length;
  $('counts').textContent = `${items.length} images · ${c('reject')} rejected · ${c('keep')} kept · ${items.filter(i => !i.mark).length} unmarked`;
  $('page').textContent = `page ${page + 1} / ${pages}`;
  window.current = shown;
}
$('grid').addEventListener('click', e => {
  const card = e.target.closest('.card'); if (!card) return;
  selected = +card.dataset.n; const item = window.current[selected];
  save(item, {mark: item.mark === 'reject' ? null : 'reject'});
});
$('grid').addEventListener('dblclick', e => { const card = e.target.closest('.card'); if (card) { selected = +card.dataset.n; openView(); } });
for (const id of ['filter', 'size', 'per']) $(id).addEventListener('input', () => { if (id !== 'size') page = 0; render(); });
$('prev').onclick = () => { page = Math.max(0, page - 1); selected = 0; render(); scrollTo(0, 0); };
$('next').onclick = () => { page++; selected = 0; render(); scrollTo(0, 0); };

function openView() {
  const item = window.current[selected]; if (!item) return;
  $('view').classList.add('open'); $('vname').textContent = item.path; $('vnote').value = item.note || '';
  viewImage = new Image(); viewImage.onload = draw; viewImage.src = '/image/' + item.id + '?v=' + item.version;
}
function closeView() { $('view').classList.remove('open'); viewImage = null; render(); }
function draw() {
  const item = window.current[selected], cv = $('canvas'), img = viewImage; if (!img || !img.complete) return;
  const reps = tile ? 2 : 1; cv.width = img.naturalWidth * reps; cv.height = img.naturalHeight * reps;
  const ctx = cv.getContext('2d');
  for (let x = 0; x < reps; x++) for (let y = 0; y < reps; y++) ctx.drawImage(img, x * img.naturalWidth, y * img.naturalHeight);
  ctx.lineWidth = Math.max(2, img.naturalWidth / 300);
  for (const [x, y, w, h] of item.crops.concat(drawing ? [drawing] : [])) { ctx.strokeStyle = '#f5a623'; ctx.strokeRect(x, y, w, h); }
  if (item.mark) { ctx.fillStyle = item.mark === 'reject' ? '#e5484d55' : '#46a75833'; ctx.fillRect(0, 0, cv.width, cv.height); }
}
function imagePoint(e) {
  const cv = $('canvas'), r = cv.getBoundingClientRect();
  return [Math.round((e.clientX - r.left) / r.width * cv.width), Math.round((e.clientY - r.top) / r.height * cv.height)];
}
$('canvas').addEventListener('mousedown', e => { if (tile) return; const [x, y] = imagePoint(e); drawing = [x, y, 0, 0, x, y]; });
$('canvas').addEventListener('mousemove', e => {
  if (!drawing) return; const [x, y] = imagePoint(e), [, , , , sx, sy] = drawing;
  drawing = [Math.min(sx, x), Math.min(sy, y), Math.abs(x - sx), Math.abs(y - sy), sx, sy]; draw();
});
addEventListener('mouseup', () => {
  if (!drawing) return; const [x, y, w, h] = drawing; drawing = null;
  const item = window.current[selected];
  if (w > 4 && h > 4) save(item, {crops: item.crops.concat([[x, y, w, h]])}).then(draw); else draw();
});
$('vnote').addEventListener('change', () => save(window.current[selected], {note: $('vnote').value}));
$('vtile').onclick = () => { tile = !tile; $('vtile').classList.toggle('on', tile); draw(); };
$('vreject').onclick = () => save(window.current[selected], {mark: 'reject'}).then(draw);
$('vkeep').onclick = () => save(window.current[selected], {mark: 'keep'}).then(draw);
$('vclear').onclick = () => save(window.current[selected], {mark: null}).then(draw);
$('vclose').onclick = closeView;

addEventListener('keydown', e => {
  if (e.target.tagName === 'INPUT') { if (e.key === 'Escape') e.target.blur(); return; }
  const open = $('view').classList.contains('open'), item = window.current && window.current[selected];
  const cols = Math.max(1, Math.floor($('grid').clientWidth / ($('size').value * 1 + 8)));
  const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
  const move = d => { selected = Math.max(0, Math.min(window.current.length - 1, selected + d)); if (open) openView(); else render();
    document.querySelector('.card.sel')?.scrollIntoView({block: 'nearest'}); };
  if (key === 'x' && item) save(item, {mark: 'reject'}).then(() => open && draw());
  else if (key === 'k' && item) save(item, {mark: 'keep'}).then(() => open && draw());
  else if (key === 'u' && item) save(item, {mark: null}).then(() => open && draw());
  else if (e.key === 'ArrowRight') move(1); else if (e.key === 'ArrowLeft') move(-1);
  else if (e.key === 'ArrowDown' && !open) move(cols); else if (e.key === 'ArrowUp' && !open) move(-cols);
  else if ((e.key === ' ' || e.key === 'Enter') && !open) { e.preventDefault(); openView(); }
  else if (e.key === 'Escape' && open) closeView();
  else if (key === 't' && open) $('vtile').click();
  else if (e.key === 'Backspace' && open && item && item.crops.length) save(item, {crops: item.crops.slice(0, -1)}).then(draw);
  else return;
  e.preventDefault();
});
fetch('/api/items').then(r => r.json()).then(d => { items = d.items; render(); });
</script></body></html>
"""


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("directory")
    parser.add_argument("--port", type=int, default=8790)
    parser.add_argument("--bind", default="0.0.0.0")
    parser.add_argument("--title", default=None)
    parser.add_argument("--info", action="append", default=[], help="JSON list of per-image facts to show as badges")
    parser.add_argument("--order", help="text file of image paths or names, best first")
    args = parser.parse_args()
    review = Review(args.directory, args.info, args.order, args.title or os.path.basename(os.path.abspath(args.directory)))
    review.warm()
    server = http.server.ThreadingHTTPServer((args.bind, args.port), handler(review))
    print(f"reviewing {len(review.images)} images in {review.root} at http://{args.bind}:{args.port}/ "
          f"(saving to {review.state_path})", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
