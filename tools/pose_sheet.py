# blender -b -P tools/pose_sheet.py -- <rig-dir> <out.jpg-dir>
# For each clip GLB in rig-dir, import, jump to the clip's peak (60%) frame, render one 3/4 view. Sheet = ffmpeg tile after.
import bpy, sys, os, math, mathutils, glob
argv = sys.argv[sys.argv.index("--")+1:]; rigdir, out = argv[0], argv[1]; os.makedirs(out, exist_ok=True)
clips = sorted(p for p in glob.glob(os.path.join(rigdir, "*.glb")) if not os.path.basename(p).startswith("rigged"))
for i, path in enumerate(clips):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    sc = bpy.context.scene
    acts = [a for a in bpy.data.actions]
    if acts:
        f0, f1 = acts[0].frame_range; sc.frame_set(int(f0 + (f1 - f0) * 0.6))
    objs = [o for o in bpy.data.objects if o.type == "MESH"]
    pts = [o.matrix_world @ mathutils.Vector(c) for o in objs for c in o.bound_box]
    lo = mathutils.Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = mathutils.Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (lo + hi) / 2; r = (hi - lo).length * 1.1 + 0.3
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.9, 0.9, 0.9, 1)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 45
    cam.location = c + mathutils.Vector((r * 0.7, -r * 0.7, r * 0.25)); d = c - cam.location; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 640; sc.render.resolution_y = 800
    sc.render.filepath = os.path.join(out, "%02d_%s.png" % (i, os.path.basename(path)[:-4])); bpy.ops.render.render(write_still=True)
    print("POSE", os.path.basename(path), "frames", [a.frame_range[:] for a in acts][:1])
print("POSE_SHEET_OK", len(clips))
