# blender -b -P tools/merge_clips.py -- <rig-dir> <out.fbx> <height_m> clip1 clip2 ...
# Imports rigged.glb, pulls each <clip>.glb's action onto that armature as an NLA strip, scales to height, exports FBX (Unity-ready, one clip per strip).
import bpy, sys, os, mathutils
argv = sys.argv[sys.argv.index("--")+1:]; rigdir, out, H = argv[0], argv[1], float(argv[2]); clips = argv[3:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(rigdir, "rigged.glb"))
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE"); arm.name = "Armature"
base_objs = set(bpy.data.objects)
for m in [o for o in bpy.data.objects if o.type == "MESH"]: m.name = "Body" if len([o for o in bpy.data.objects if o.type=="MESH"])==1 else m.name
arm.animation_data_create()
INPLACE = {"runfast", "sprint", "charge", "runfast4", "headdown", "running_glb_url", "walking_glb_url"}
def make_inplace(arm, act, clip):
    """Meshy locomotion clips travel (hips drift several metres over the loop, then snap back). Remove the linear
    horizontal drift of the hips in world space so the clip plays in place and the loop closes."""
    hips = next((b for b in arm.pose.bones if b.name.lower() in ("hips", "pelvis")), None)
    if hips is None: print("INPLACE: no hips bone for", clip); return
    arm.animation_data.action = act
    if getattr(act, "slots", None) and len(act.slots): arm.animation_data.action_slot = act.slots[0]   # Blender 4.4+/5: no slot, no evaluation
    f0, f1 = (int(act.frame_range[0]), int(act.frame_range[1]))
    sc = bpy.context.scene
    def world(f):
        sc.frame_set(f); return (arm.matrix_world @ hips.matrix).translation.copy()
    w0 = world(f0)
    disp = {f: (world(f) - w0) for f in range(f0, f1 + 1)}
    for d in disp.values(): d.z = 0.0                       # Blender world: Z up; horizontal only
    fmax = max(disp, key=lambda f: disp[f].length)
    if disp[fmax].length < 0.3: arm.animation_data.action = None; print("INPLACE", clip, "no drift"); return
    axis = disp[fmax].normalized()
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        m = arm.matrix_world @ hips.matrix
        along = (m.translation - w0); along.z = 0.0
        corr = m.copy(); corr.translation = m.translation - axis * along.dot(axis)   # strip the travel, keep the sway and the bob
        hips.matrix = arm.matrix_world.inverted() @ corr
        hips.keyframe_insert("location", frame=f)
    drift = disp[fmax]
    arm.animation_data.action = None
    print("INPLACE", clip, "removed drift", tuple(round(v, 2) for v in drift))
for clip in clips:
    p = os.path.join(rigdir, clip + ".glb")
    if not os.path.exists(p): print("MISSING", clip); continue
    before = set(bpy.data.objects); acts_before = set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=p)
    new_acts = [a for a in bpy.data.actions if a not in acts_before]
    if not new_acts: print("NOACTION", clip); continue
    act = new_acts[0]; act.name = clip; act.use_fake_user = True
    if clip in INPLACE: make_inplace(arm, act, clip)
    tr = arm.animation_data.nla_tracks.new(); tr.name = clip
    s = tr.strips.new(clip, int(act.frame_range[0]), act); s.name = clip
    for o in [o for o in bpy.data.objects if o not in before]: bpy.data.objects.remove(o, do_unlink=True)
    print("CLIP", clip, "frames", tuple(round(f) for f in act.frame_range))
# scale to height (armature + meshes are children of arm or siblings; scale the root)
roots = [o for o in bpy.data.objects if o.parent is None]
pts = [o.matrix_world @ mathutils.Vector(c) for o in bpy.data.objects if o.type == "MESH" for c in o.bound_box]
h = max(p.z for p in pts) - min(p.z for p in pts); s = H / h
for r in roots: r.scale = (s, s, s)
print("SCALE", round(h, 3), "->", H, "factor", round(s, 4))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options="FBX_SCALE_ALL", apply_unit_scale=True,
    add_leaf_bones=False, bake_anim=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    mesh_smooth_type="FACE", path_mode="COPY", embed_textures=True, armature_nodetype="NULL", primary_bone_axis="Y", secondary_bone_axis="X")
print("MERGE_OK", out, "clips", len(clips))
