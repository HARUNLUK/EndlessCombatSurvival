# Dumps the rest pose (local bone rotation + position, converted to Unity axes) of Mixamo FBX files.
# Usage: blender -b --factory-startup -P mixamo_rest_pose_dump.py -- <fbx files...>  -> tpose.txt next to this script
import bpy, sys, glob, os
files = sys.argv[sys.argv.index("--")+1:]
out = open(os.path.join(os.path.dirname(__file__), "tpose.txt"), "w")
for path in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
    base = os.path.basename(path)
    for b in arm.data.bones:
        m = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
        q = m.to_quaternion(); t = m.to_translation()
        out.write("%s|%s|%.6f|%.6f|%.6f|%.6f|%.6f|%.6f|%.6f\n" % (base, b.name, q.x, -q.y, -q.z, q.w, -t.x, t.y, t.z))
out.close()
