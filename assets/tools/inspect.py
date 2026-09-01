# blender --background --python inspect.py -- <files...>   prints json lines: file, dims(x,y,z blender Z-up), tris
import bpy, sys, json
from mathutils import Vector
argv = sys.argv[sys.argv.index("--")+1:]
for src in argv:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        if src.lower().endswith((".glb",".gltf")):
            bpy.ops.import_scene.gltf(filepath=src)
        elif src.lower().endswith((".usda",".usd",".usdc")):
            bpy.ops.wm.usd_import(filepath=src)
        elif src.lower().endswith(".fbx"):
            bpy.ops.import_scene.fbx(filepath=src)
    except Exception as e:
        print(json.dumps({"file":src,"error":str(e)})); continue
    bpy.context.view_layer.update()
    mn=Vector((1e9,)*3); mx=Vector((-1e9,)*3); tris=0
    for o in bpy.data.objects:
        if o.type!="MESH": continue
        m=o.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
        tris+=sum(len(p.vertices)-2 for p in m.polygons)
        for v in m.vertices:
            w=o.matrix_world@v.co
            mn=Vector(map(min,mn,w)); mx=Vector(map(max,mx,w))
    d=[round(x,3) for x in (mx-mn)] if tris else None
    print(json.dumps({"file":src,"dims":d,"min":[round(x,3) for x in mn] if tris else None,"tris":tris,"anims":len(bpy.data.actions),"arm":sum(1 for o in bpy.data.objects if o.type=="ARMATURE")}))
