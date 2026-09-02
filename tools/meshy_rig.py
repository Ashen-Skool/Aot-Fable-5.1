#!/usr/bin/env python3
"""Rig a Meshy image-to-3d result and fetch library clips.
usage: meshy_rig.py <meshy-raw/task.json> <out-dir> <height_m> <clipname=id> [clipname=id ...]
Downloads rigged GLB/FBX + free walk/run + each requested clip as <clip>.glb. Never prints the key."""
import sys, os, json, time, urllib.request, pathlib
task = json.load(open(sys.argv[1])); out = pathlib.Path(sys.argv[2]); H = float(sys.argv[3])
clips = [c.split("=") for c in sys.argv[4:]]
key = open(os.path.expanduser("~/.claude/secrets/meshy.key")).read().strip()
Hd = {"Authorization": f"Bearer {key}", "Content-Type": "application/json"}; B = "https://api.meshy.ai"
def call(m, p, body=None):
    r = urllib.request.Request(B+p, method=m, headers=Hd, data=json.dumps(body).encode() if body else None)
    with urllib.request.urlopen(r, timeout=60) as x: return json.load(x)
def poll(p):
    while True:
        t = call("GET", p); st = t["status"]; print(f"  {p.split('/')[-2]} {st} {t.get('progress',0)}%", flush=True)
        if st in ("SUCCEEDED","FAILED","CANCELED"): return t
        time.sleep(12)
out.mkdir(parents=True, exist_ok=True)
state_p = out/"rig_state.json"; state = json.load(open(state_p)) if state_p.exists() else {}
if "rig_task" not in state:
    state["rig_task"] = call("POST", "/openapi/v1/rigging", {"input_task_id": task["id"], "height_meters": H})["result"]
    json.dump(state, open(state_p,"w"))
rig = poll(f"/openapi/v1/rigging/{state['rig_task']}")
if rig["status"] != "SUCCEEDED": print("RIG_FAIL", rig.get("task_error")); sys.exit(1)
res = rig.get("result", rig)
def dl(u, name):
    if u: urllib.request.urlretrieve(u, out/name); print("  saved", name, flush=True)
dl(res.get("rigged_character_glb_url"), "rigged.glb"); dl(res.get("rigged_character_fbx_url"), "rigged.fbx")
for k, v in (res.get("basic_animations") or {}).items():
    if isinstance(v, dict):
        dl(v.get("glb_url") or v.get("glb"), f"{k}.glb")
    elif isinstance(v, str) and ".glb" in v: dl(v, f"{k}.glb")
for name, aid in clips:
    if (out/f"{name}.glb").exists(): continue
    key_ = f"anim_{name}"
    if key_ not in state:
        state[key_] = call("POST", "/openapi/v1/animations", {"rig_task_id": state["rig_task"], "action_id": int(aid)})["result"]
        json.dump(state, open(state_p,"w"))
    a = poll(f"/openapi/v1/animations/{state[key_]}")
    if a["status"] == "SUCCEEDED":
        ar = a.get("result", a); dl(ar.get("animation_glb_url"), f"{name}.glb")
    else: print("ANIM_FAIL", name, a.get("task_error"))
json.dump({k: v for k, v in rig.items() if "url" not in k}, open(out/"rig_task.json","w"), indent=1)
print("RIG_OK", out, sorted(p.name for p in out.iterdir()))
