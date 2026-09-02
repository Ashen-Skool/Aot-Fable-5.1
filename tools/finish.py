# blender -b -P tools/finish.py -- <in.glb> <out.glb> <height_m>
# Finish pass: join meshes, scale to height, origin at feet on the ground, face -Z... (glTF forward is +Z in Blender's -Y), export GLB.
import bpy, sys, mathutils
argv = sys.argv[sys.argv.index("--")+1:]
src, dst, H = argv[0], argv[1], float(argv[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
for o in bpy.data.objects: o.select_set(o in meshes)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.name = "Body"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# bounds in world (Blender Z-up)
pts = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
zmin, zmax = min(p.z for p in pts), max(p.z for p in pts)
xs = [p.x for p in pts]; ys = [p.y for p in pts]
s = H / (zmax - zmin)
obj.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)
pts = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
cx = (min(p.x for p in pts)+max(p.x for p in pts))/2; cy = (min(p.y for p in pts)+max(p.y for p in pts))/2; zmin = min(p.z for p in pts)
obj.location = (-cx, -cy, -zmin)
bpy.ops.object.transform_apply(location=True)
# Smooth shading + a little cel flavour: keep textures, bump roughness
bpy.ops.object.shade_smooth()
for m in obj.data.materials:
    if m and m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == "BSDF_PRINCIPLED":
                n.inputs["Roughness"].default_value = 0.85
                if "Specular IOR Level" in n.inputs: n.inputs["Specular IOR Level"].default_value = 0.2
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_apply=True, export_yup=True)
d = obj.dimensions
print("FINISH_OK", dst, "dims(m) x=%.2f y=%.2f z=%.2f tris=%d" % (d.x, d.y, d.z, sum(len(p.vertices)-2 for p in obj.data.polygons)))
