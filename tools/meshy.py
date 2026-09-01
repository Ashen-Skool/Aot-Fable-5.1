#!/usr/bin/env python3
"""Image -> 3D via Meshy. usage: meshy.py <image.png> <out-dir> [--polycount N] [--pose t-pose|a-pose]
Reads the key from ~/.claude/secrets/meshy.key. Never prints the key."""
import sys, os, json, time, base64, urllib.request, argparse, pathlib
ap = argparse.ArgumentParser()
ap.add_argument("image"); ap.add_argument("out")
ap.add_argument("--polycount", type=int, default=30000)
ap.add_argument("--pose", default="a-pose")
ap.add_argument("--no-texture", action="store_true")
a = ap.parse_args()
key = open(os.path.expanduser("~/.claude/secrets/meshy.key")).read().strip()
H = {"Authorization": f"Bearer {key}", "Content-Type": "application/json"}
B = "https://api.meshy.ai"
def call(method, path, body=None):
    req = urllib.request.Request(B + path, method=method, headers=H,
                                 data=json.dumps(body).encode() if body else None)
    with urllib.request.urlopen(req, timeout=60) as r: return json.load(r)
img = pathlib.Path(a.image).read_bytes()
mime = "image/png" if a.image.endswith(".png") else "image/jpeg"
body = {"image_url": f"data:{mime};base64,{base64.b64encode(img).decode()}",
        "ai_model": "latest", "should_remesh": True, "topology": "triangle",
        "target_polycount": a.polycount, "pose_mode": a.pose,
        "should_texture": not a.no_texture, "enable_pbr": True,
        "symmetry_mode": "auto"}
tid = call("POST", "/openapi/v1/image-to-3d", body)["result"]
print("task", tid, flush=True)
while True:
    t = call("GET", f"/openapi/v1/image-to-3d/{tid}")
    st = t["status"]; print(f"  {st} {t.get('progress',0)}%", flush=True)
    if st in ("SUCCEEDED", "FAILED", "CANCELED"): break
    time.sleep(12)
if st != "SUCCEEDED":
    print("MESHY_FAIL", json.dumps(t.get("task_error"))); sys.exit(1)
out = pathlib.Path(a.out); out.mkdir(parents=True, exist_ok=True)
for k, u in t["model_urls"].items():
    if k in ("glb", "fbx") and u:
        urllib.request.urlretrieve(u, out / f"model.{k}")
for i, (k, u) in enumerate((t.get("texture_urls") or [{}])[0].items()):
    if u: urllib.request.urlretrieve(u, out / f"tex_{k}.png")
if t.get("thumbnail_url"): urllib.request.urlretrieve(t["thumbnail_url"], out / "thumb.png")
(out / "task.json").write_text(json.dumps({k: v for k, v in t.items() if "url" not in k}, indent=1))
print("MESHY_OK", out, sorted(p.name for p in out.iterdir()))
