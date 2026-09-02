# blender --background --python convert2.py -- <jobs.json> <results.jsonl>
# job: {"src": path, "out": path, "scale": float, "texmax": int|0, "jpeg": bool}
import bpy, sys, json, os
from pathlib import Path
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
jobs = json.load(open(argv[0]))
res = open(argv[1], "a")

def measure():
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3); tris = 0
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        m = o.evaluated_get(dg).to_mesh()
        tris += sum(len(p.vertices) - 2 for p in m.polygons)
        for v in m.vertices:
            w = o.matrix_world @ v.co
            mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
    if not tris:
        return None, None, 0
    d = mx - mn
    # report glTF convention: [width(x), height(up), depth]
    return [round(d.x, 3), round(d.z, 3), round(d.y, 3)], [round(mn.x, 3), round(mn.z, 3), round(mn.y, 3)], tris

for j in jobs:
    src = j["src"]; dst = j["out"]
    Path(dst).parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        s = src.lower()
        if s.endswith((".usda", ".usd", ".usdc")):
            bpy.ops.wm.usd_import(filepath=src)
        elif s.endswith(".fbx"):
            bpy.ops.import_scene.fbx(filepath=src)
        else:
            bpy.ops.import_scene.gltf(filepath=src)
    except Exception as e:
        res.write(json.dumps({"src": src, "error": str(e)}) + "\n"); res.flush(); continue
    if j.get("drop"):
        import re as _re
        for o in [o for o in bpy.data.objects if _re.search(j["drop"], o.name)]:
            bpy.data.objects.remove(o, do_unlink=True)
        for m in [m for m in bpy.data.meshes if m.users == 0 or _re.search(j["drop"], m.name)]:
            bpy.data.meshes.remove(m)
    scale = float(j.get("scale", 1.0))
    if scale != 1.0:
        for o in bpy.data.objects:
            if o.parent is None:
                o.scale = [v * scale for v in o.scale]
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    texmax = int(j.get("texmax", 0) or 0)
    if texmax:
        for im in bpy.data.images:
            if im.size[0] > texmax or im.size[1] > texmax:
                f = max(im.size) / texmax
                im.scale(max(1, int(im.size[0] / f)), max(1, int(im.size[1] / f)))
    dims, mn, tris = measure()
    kw = dict(filepath=dst, export_format="GLB", export_yup=True, export_apply=True,
              export_animations=True, export_skins=True)
    if j.get("jpeg"):
        kw["export_image_format"] = "JPEG"
        kw["export_jpeg_quality"] = 80
    try:
        bpy.ops.export_scene.gltf(**kw)
    except Exception as e:
        res.write(json.dumps({"src": src, "error": "export: " + str(e)}) + "\n"); res.flush(); continue
    res.write(json.dumps({"src": src, "out": dst, "dims": dims, "min": mn, "tris": tris,
                          "anims": len(bpy.data.actions), "size": os.path.getsize(dst)}) + "\n")
    res.flush()
