# blender -b -P tools/turntable.py -- <model.glb> <out-dir> [frames=8]
# Renders a turntable contact sheet + stats for a GLB so a human can approve it.
import bpy, sys, os, math, json
argv = sys.argv[sys.argv.index("--")+1:]
src, out = argv[0], argv[1]; n = int(argv[2]) if len(argv) > 2 else 8
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
objs = [o for o in bpy.data.objects if o.type == "MESH"]
# bounds
xs, ys, zs = [], [], []
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ __import__("mathutils").Vector(c); xs.append(w.x); ys.append(w.y); zs.append(w.z)
dims = (max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)); center = ((max(xs)+min(xs))/2, (max(ys)+min(ys))/2, (max(zs)+min(zs))/2)
tris = sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objs)
json.dump({"dims": dims, "tris": tris, "objects": [o.name for o in objs], "materials": [m.name for m in bpy.data.materials], "images": [i.name for i in bpy.data.images]}, open(f"{out}/stats.json", "w"), indent=1)
# lights + world
world = bpy.data.worlds.new("W"); bpy.context.scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.9, 0.9, 0.9, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(30)); bpy.context.collection.objects.link(sun)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); bpy.context.collection.objects.link(cam); bpy.context.scene.camera = cam
cam.data.lens = 50
r = max(dims) * 1.6 + 0.5
sc = bpy.context.scene; sc.render.engine = "BLENDER_EEVEE" if hasattr(bpy.types, "SCENE_OT_render") and "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
sc.render.resolution_x = 768; sc.render.resolution_y = 1024; sc.render.film_transparent = False
import mathutils
for i in range(n):
    a = 2*math.pi*i/n
    cam.location = (center[0] + r*math.sin(a), center[1] - r*math.cos(a), center[2] + dims[2]*0.15)
    d = mathutils.Vector(center) - cam.location; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = f"{out}/turn_{i:02d}.png"; bpy.ops.render.render(write_still=True)
# close-up head
cam.location = (center[0], center[1] - dims[2]*0.35, center[2] + dims[2]*0.38)
d = mathutils.Vector((center[0], center[1], center[2] + dims[2]*0.40)) - cam.location; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
sc.render.filepath = f"{out}/head.png"; bpy.ops.render.render(write_still=True)
print("TURNTABLE_OK", json.dumps({"dims": dims, "tris": tris}))
