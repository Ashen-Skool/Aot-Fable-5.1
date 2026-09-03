# blender -b -P tools/prop_finish.py -- <in.glb> <out.fbx> <length_m>
# Prop finish: join, find the long axis, pivot 12% from the grip end (the end with the bigger cross-section), long axis -> +Y (tip at +Y), scale to length, export FBX.
import bpy, sys, mathutils
argv = sys.argv[sys.argv.index("--")+1:]; src, dst, L = argv[0], argv[1], float(argv[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
for o in bpy.data.objects: o.select_set(o in meshes)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active; obj.name = "Prop"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
vs = [v.co.copy() for v in obj.data.vertices]
lo = mathutils.Vector([min(v[i] for v in vs) for i in range(3)]); hi = mathutils.Vector([max(v[i] for v in vs) for i in range(3)])
ext = hi - lo; axis = max(range(3), key=lambda i: ext[i])
# which end is the grip: bigger cross-section in the first 20% vs last 20%
def cross(sel):
    pts = [v for v in vs if sel(v[axis])]
    others = [i for i in range(3) if i != axis]
    return sum(max(p[i] for p in pts) - min(p[i] for p in pts) for i in others) if pts else 0
a0 = lo[axis] + ext[axis]*0.2; a1 = hi[axis] - ext[axis]*0.2
grip_at_low = cross(lambda a: a < a0) > cross(lambda a: a > a1)
# rotate so long axis -> +Y with tip toward +Y
from math import radians
rot = mathutils.Matrix.Identity(4)
src_dir = mathutils.Vector([0,0,0]); src_dir[axis] = -1 if grip_at_low else 1  # points grip->tip
rot = src_dir.rotation_difference(mathutils.Vector((0,1,0))).to_matrix().to_4x4()
obj.data.transform(rot)
vs = [v.co.copy() for v in obj.data.vertices]
lo = mathutils.Vector([min(v[i] for v in vs) for i in range(3)]); hi = mathutils.Vector([max(v[i] for v in vs) for i in range(3)])
s = L / (hi.y - lo.y)
obj.data.transform(mathutils.Matrix.Scale(s, 4))
vs = [v.co.copy() for v in obj.data.vertices]
lo = mathutils.Vector([min(v[i] for v in vs) for i in range(3)]); hi = mathutils.Vector([max(v[i] for v in vs) for i in range(3)])
# pivot: 12% from the grip end along Y, centred in X/Z of the grip section
grip_pts = [v for v in vs if v.y < lo.y + (hi.y-lo.y)*0.2]
cx = sum(p.x for p in grip_pts)/len(grip_pts); cz = sum(p.z for p in grip_pts)/len(grip_pts)
pivot = mathutils.Vector((cx, lo.y + (hi.y-lo.y)*0.12, cz))
obj.data.transform(mathutils.Matrix.Translation(-pivot))
bpy.ops.object.shade_smooth()
bpy.ops.object.select_all(action="DESELECT"); obj.select_set(True)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, apply_scale_options="FBX_SCALE_ALL", apply_unit_scale=True, mesh_smooth_type="FACE", path_mode="COPY", embed_textures=False, bake_anim=False)
print("PROP_OK", dst, "length", round(hi.y-lo.y,3), "grip_at_low", grip_at_low, "axis", axis)
