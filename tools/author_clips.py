# blender -b -P tools/author_clips.py -- <rig-dir> <out-render-dir> [clip,clip,...]
# Hand-authored clips for the Meshy rig (Mixamo-style bone names): keyframed pose-bone rotations on rigged.glb,
# exported as <clip>.glb into rig-dir (so tools/merge_clips.py takes them like any library clip) and rendered as a
# strip of frames for the user to approve. Rotations are degrees in the bone's local space (X = bend forward/back
# for limbs, Z = swing sideways, Y = twist), applied on top of the rest pose.
import bpy, sys, os, math, mathutils
argv = sys.argv[sys.argv.index("--")+1:]; rigdir, out = argv[0], argv[1]
only = argv[2].split(",") if len(argv) > 2 else None
os.makedirs(out, exist_ok=True)

FPS = 30
def lerp(a, b, t): return a + (b - a) * t
def smooth(t): return t * t * (3 - 2 * t)

# ---- the clips: name -> (length_frames, loop, keys) ; keys = [(frame, {bone: (x, y, z)})], interpolated smoothly
def breathe(base, amp=2.0, arm=1.5, frames=60):
    """A looping breath on top of a base pose: chest lifts, shoulders rise, arms drift."""
    keys = []
    for f in (0, frames // 2, frames):
        ph = 0.0 if f in (0, frames) else 1.0
        p = dict(base)
        def add(b, dx=0, dy=0, dz=0):
            x, y, z = p.get(b, (0, 0, 0)); p[b] = (x + dx * ph, y + dy * ph, z + dz * ph)
        add("Spine01", dx=-amp); add("Spine", dx=-amp * 0.6); add("neck", dx=amp * 0.5)
        add("LeftShoulder", dz=arm); add("RightShoulder", dz=-arm)
        add("LeftArm", dz=-arm); add("RightArm", dz=arm)
        keys.append((f, p))
    return keys

# ---- side-aware helpers (axes measured on this rig with the pL/pR/pF probes)
def arm(side, fwd=0, raise_=0, twist=0):
    """upper arm: fwd = swing forward (deg), raise_ = lift away from the body in the coronal plane (rest is the A-pose, ~45 deg down)."""
    return {"L": (-fwd, twist, -raise_), "R": (-fwd, twist, raise_)}[side]
def forearm(side, flex):  return {"L": (0, 0, flex), "R": (0, 0, -flex)}[side]
def thigh(side, fwd=0, out=0): return {"L": (-fwd, 0, out), "R": (-fwd, 0, -out)}[side]
def knee(flex): return (flex, 0, 0)
def foot(flex): return (flex, 0, 0)
def pose(hips=0, spine=(0, 0, 0), neck=0, head=0, thighF=0, thighO=0, kneeF=0, footF=0, armF=(0, 0), armR=(0, 0), elbow=(0, 0), hand=(0, 0)):
    """hips: lean back (+) / forward (-). spine: (Spine02, Spine01, Spine) forward bend. neck/head: look up (+).
    armF/armR/elbow/hand are (left, right)."""
    return {
        "Hips": (-hips, 0, 0), "Spine02": (spine[0], 0, 0), "Spine01": (spine[1], 0, 0), "Spine": (spine[2], 0, 0),
        "neck": (-neck, 0, 0), "Head": (-head, 0, 0),
        "LeftUpLeg": thigh("L", thighF, thighO), "RightUpLeg": thigh("R", thighF, thighO),
        "LeftLeg": knee(kneeF), "RightLeg": knee(kneeF), "LeftFoot": foot(footF), "RightFoot": foot(footF),
        "LeftArm": arm("L", armF[0], armR[0]), "RightArm": arm("R", armF[1], armR[1]),
        "LeftForeArm": forearm("L", elbow[0]), "RightForeArm": forearm("R", elbow[1]),
        "LeftHand": (hand[0], 0, 0), "RightHand": (hand[1], 0, 0),
    }

# Wall perch: back to the wall, feet planted flat on it, knees bent, hips low, torso leaning out, blades low and ready.
WALLPERCH = pose(hips=18, spine=(8, 6, 4), neck=14, head=6, thighF=80, thighO=8, kneeF=95, footF=-25, armF=(20, 20), armR=(-30, -30), elbow=(25, 25), hand=(-10, -10))
# Kick-off: sink deeper, then explode off the wall legs straight, arms forward with the blades.
WALLKICK_A = pose(hips=24, spine=(12, 8, 4), neck=14, head=6, thighF=100, thighO=8, kneeF=125, footF=-30, armF=(10, 10), armR=(-35, -35), elbow=(30, 30))
WALLKICK_B = pose(hips=6, spine=(14, 10, 6), neck=10, head=6, thighF=10, thighO=6, kneeF=10, footF=25, armF=(70, 70), armR=(-20, -20), elbow=(20, 20))
# Nape ride: kneeling on the back of his neck, hunched, left blade buried low, right blade raised over the head.
NAPERIDE = pose(hips=-20, spine=(16, 14, 10), neck=18, head=10, thighF=100, thighO=14, kneeF=130, footF=30, armF=(60, 120), armR=(-10, 20), elbow=(15, 70), hand=(-20, -20))
# Stab: the right arm cocks, then plunges down in front, the torso dips with it, then returns to the ride pose.
NAPESTAB_COCK = pose(hips=-20, spine=(8, 8, 6), neck=22, head=10, thighF=100, thighO=14, kneeF=130, footF=30, armF=(60, 150), armR=(-10, 30), elbow=(15, 80), hand=(-20, -20))
NAPESTAB_HIT  = pose(hips=-20, spine=(30, 24, 14), neck=6, head=4, thighF=100, thighO=14, kneeF=130, footF=30, armF=(60, 40), armR=(-10, -5), elbow=(15, 15), hand=(-20, -35))
# Final blow: both blades up, both plunge, hold buried.
NAPEFINAL_UP   = pose(hips=-20, spine=(2, 2, 2), neck=28, head=12, thighF=100, thighO=14, kneeF=130, footF=30, armF=(150, 150), armR=(30, 30), elbow=(80, 80), hand=(-20, -20))
NAPEFINAL_DOWN = pose(hips=-20, spine=(34, 26, 14), neck=2, head=2, thighF=100, thighO=14, kneeF=130, footF=30, armF=(40, 40), armR=(-5, -5), elbow=(12, 12), hand=(-35, -35))

CLIPS = {
    "pL":  (30, False, [(0, {"LeftArm": (0, 0, 60)}), (9, {"LeftArm": (0, 0, 60)}), (10, {"LeftArm": (0, 0, -60)}), (19, {"LeftArm": (0, 0, -60)}), (20, {"LeftArm": (60, 0, 0)}), (29, {"LeftArm": (60, 0, 0)}), (30, {"LeftArm": (-60, 0, 0)})]),
    "pR":  (30, False, [(0, {"RightArm": (0, 0, 60)}), (9, {"RightArm": (0, 0, 60)}), (10, {"RightArm": (0, 0, -60)}), (19, {"RightArm": (0, 0, -60)}), (20, {"RightArm": (60, 0, 0)}), (29, {"RightArm": (60, 0, 0)}), (30, {"RightArm": (-60, 0, 0)})]),
    "pF":  (30, False, [(0, {"LeftForeArm": (0, 0, 60)}), (9, {"LeftForeArm": (0, 0, 60)}), (10, {"LeftForeArm": (0, 0, -60)}), (19, {"LeftForeArm": (0, 0, -60)}), (20, {"RightForeArm": (0, 0, 60)}), (29, {"RightForeArm": (0, 0, 60)}), (30, {"RightForeArm": (0, 0, -60)})]),
    "probe2":    (30, False, [(0, {"LeftForeArm": (-70, 0, 0), "RightForeArm": (0, 0, 70), "Spine02": (30, 0, 0), "LeftFoot": (-40, 0, 0)}), (15, {"LeftForeArm": (-70, 0, 0), "RightForeArm": (0, 0, 70), "Spine02": (30, 0, 0), "LeftFoot": (-40, 0, 0)}), (16, {"LeftForeArm": (0, 0, -70), "RightForeArm": (70, 0, 0), "Spine02": (0, 0, 30), "LeftFoot": (40, 0, 0), "Hips": (-30, 0, 0)}), (30, {"LeftForeArm": (0, 0, -70), "RightForeArm": (70, 0, 0), "Spine02": (0, 0, 30), "LeftFoot": (40, 0, 0), "Hips": (-30, 0, 0)})]),
    "probe":     (30, False, [(0, {"LeftArm": (70, 0, 0), "RightArm": (0, 0, 70), "LeftUpLeg": (0, 0, 40)}), (15, {"LeftArm": (0, 70, 0), "RightArm": (0, 0, -70), "LeftUpLeg": (0, 40, 0)}), (30, {"LeftArm": (0, 0, 70), "RightArm": (-70, 0, 0), "LeftUpLeg": (40, 0, 0)})]),
    "wallperch": (60, True, breathe(WALLPERCH)),
    "wallkick":  (18, False, [(0, WALLPERCH), (7, WALLKICK_A), (13, WALLKICK_B), (18, WALLKICK_B)]),
    "naperide":  (60, True, breathe(NAPERIDE, amp=2.5, arm=2.0)),
    "napestab":  (16, False, [(0, NAPERIDE), (4, NAPESTAB_COCK), (8, NAPESTAB_HIT), (11, NAPESTAB_HIT), (16, NAPERIDE)]),
    "napefinal": (30, False, [(0, NAPERIDE), (12, NAPEFINAL_UP), (18, NAPEFINAL_DOWN), (30, NAPEFINAL_DOWN)]),
}

def load_rig():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(rigdir, "rigged.glb"))
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    arm.animation_data_create()
    return arm

def apply_pose(arm, pose):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        x, y, z = pose.get(pb.name, (0, 0, 0))
        pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))

def build(arm, name, length, loop, keys):
    act = bpy.data.actions.new(name); arm.animation_data.action = act
    if getattr(act, "slots", None) is not None:
        slot = act.slots.new(id_type="OBJECT", name=name) if hasattr(act.slots, "new") else None
        if slot is not None: arm.animation_data.action_slot = slot
    bones = set(b for _, p in keys for b in p)
    for f in range(0, length + 1):
        # find the surrounding keys and blend smoothly
        prev = max((k for k in keys if k[0] <= f), key=lambda k: k[0]); nxt = min((k for k in keys if k[0] >= f), key=lambda k: k[0])
        t = 0.0 if nxt[0] == prev[0] else smooth((f - prev[0]) / (nxt[0] - prev[0]))
        pose = {}
        for b in bones:
            a = prev[1].get(b, (0, 0, 0)); c = nxt[1].get(b, (0, 0, 0)); pose[b] = tuple(lerp(a[i], c[i], t) for i in range(3))
        apply_pose(arm, pose)
        for pb in arm.pose.bones: pb.keyframe_insert("rotation_euler", frame=f)
    act.use_fake_user = True
    return act

def render_strip(arm, name, length, n=5):
    if name.startswith('p') and len(name) == 2: n = 4
    sc = bpy.context.scene; sc.frame_start = 0; sc.frame_end = length
    objs = [o for o in bpy.data.objects if o.type == "MESH"]
    pts = [o.matrix_world @ mathutils.Vector(c) for o in objs for c in o.bound_box]
    lo = mathutils.Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = mathutils.Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (lo + hi) / 2; r = (hi - lo).length * 0.8 + 0.2
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.9, 0.9, 0.9, 1)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 45
    sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 480; sc.render.resolution_y = 600
    views = {"front": mathutils.Vector((0, -r, r * 0.15)), "side": mathutils.Vector((r, 0, r * 0.15)), "quarter": mathutils.Vector((r * 0.7, -r * 0.7, r * 0.3))}
    for vn, off in views.items():
        cam.location = c + off; d = c - cam.location; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        for j in range(n):
            sc.frame_set(int(length * j / max(1, n - 1)))
            sc.render.filepath = os.path.join(out, "%s_%s_%d.png" % (name, vn, j)); bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True); bpy.data.objects.remove(sun, do_unlink=True)

for name, (length, loop, keys) in CLIPS.items():
    if only and name not in only: continue
    arm = load_rig()
    build(arm, name, length, loop, keys)
    render_strip(arm, name, length)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=os.path.join(rigdir, name + ".glb"), export_format="GLB", use_selection=True, export_animations=True, export_apply=False)
    print("AUTHORED", name, "frames", length, "loop", loop)
print("AUTHOR_OK")
