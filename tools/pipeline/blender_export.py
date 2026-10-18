"""Headless Blender: import one L4D2 MDL with SourceIO and export an FBX for Unity.

usage:
  blender -b --factory-startup --addons SourceIO --python blender_export.py -- \
      --mdl <file.mdl> --out <file.fbx> [--anim <sequence-name> ...] [--list-anims] [--no-anims]

--anim keeps only the named actions (substring match, case-insensitive); with none given, every
imported action is kept. --list-anims prints the actions SourceIO produced and exits.
"""
import argparse, os, sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--mdl", required=True)
ap.add_argument("--out")
ap.add_argument("--anim", action="append", default=[])
ap.add_argument("--list-anims", action="store_true")
ap.add_argument("--no-anims", action="store_true")
args = ap.parse_args(argv)

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
mdl = os.path.abspath(args.mdl)
bpy.ops.sourceio.mdl(
    filepath=mdl, directory=os.path.dirname(mdl), files=[{"name": os.path.basename(mdl)}],
    import_textures=True, discover_resources=True, write_qc=False,
    import_animations=not args.no_anims, import_include_animations=not args.no_anims,
    import_physics=False, create_flex_drivers=False)

arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
actions = list(bpy.data.actions)
print("S4D armatures=%s meshes=%d actions=%d materials=%d images=%d" % (
    [a.name for a in arms], len(meshes), len(actions), len(bpy.data.materials), len(bpy.data.images)))
if args.list_anims:
    for a in actions:
        print("S4D action", a.name, int(a.frame_range[1] - a.frame_range[0]))
    sys.exit(0)

if args.anim:
    want = [w.lower() for w in args.anim]
    for a in list(actions):
        if not any(w in a.name.lower() for w in want):
            bpy.data.actions.remove(a)
    actions = list(bpy.data.actions)
    print("S4D kept actions", [a.name for a in actions])
if arms and actions:
    arm = arms[0]
    arm.animation_data_create()
    arm.animation_data.action = actions[0]
    # one NLA track per action so the FBX exporter writes each as its own take
    for a in actions:
        tr = arm.animation_data.nla_tracks.new()
        tr.name = a.name
        tr.strips.new(a.name, int(a.frame_range[0]), a)

# Images: write packed / generated textures next to the FBX so Unity can pick them up.
tex_dir = os.path.join(os.path.dirname(os.path.abspath(args.out)), "textures")
os.makedirs(tex_dir, exist_ok=True)
for img in bpy.data.images:
    if img.size[0] == 0:
        continue
    dst = os.path.join(tex_dir, bpy.path.clean_name(img.name) + ".png")
    img.filepath_raw = dst
    img.file_format = "PNG"
    try:
        img.save()
    except RuntimeError as e:
        print("S4D image save failed", img.name, e)

bpy.ops.export_scene.fbx(
    filepath=os.path.abspath(args.out), use_selection=False, object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=False, bake_anim=bool(actions), bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
print("S4D wrote", args.out)
