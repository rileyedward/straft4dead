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
ap.add_argument("--smd", action="append", default=[], help="Crowbar-decompiled animation SMD to bake onto the armature (SourceIO's own .ani import is unreliable)")
ap.add_argument("--fps", type=float, default=30.0)
args = ap.parse_args(argv)

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
mdl = os.path.abspath(args.mdl)
bpy.ops.sourceio.mdl(
    filepath=mdl, directory=os.path.dirname(mdl), files=[{"name": os.path.basename(mdl)}],
    import_textures=True, discover_resources=True, write_qc=False,
    import_animations=not args.no_anims, import_include_animations=not args.no_anims,
    import_physics=False, create_flex_drivers=False,
    scale=0.0254)  # 1 Source unit = 1 inch for characters (SourceIO default 0.01905 is the map scale)

arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]

# Some L4D2 models (the commons) come in lying down. Stand every body up: pelvis -> head must point +Z.
# Skipped when baking SMD animation: SMD root motion is already in upright model space.
import mathutils
for arm in (arms if not args.smd else []):
    pb = arm.pose.bones
    if "ValveBiped.Bip01_Pelvis" in pb and "ValveBiped.Bip01_Head1" in pb:
        v = (arm.matrix_world @ pb["ValveBiped.Bip01_Head1"].head) - (arm.matrix_world @ pb["ValveBiped.Bip01_Pelvis"].head)
        if v.normalized().z < 0.9:
            q = v.normalized().rotation_difference(mathutils.Vector((0, 0, 1)))
            for o in bpy.data.objects:
                if o.parent is None:
                    o.matrix_world = q.to_matrix().to_4x4() @ o.matrix_world
            print("S4D stood up", arm.name, "was", tuple(round(c, 2) for c in v.normalized()))
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
actions = list(bpy.data.actions)
print("S4D armatures=%s meshes=%d actions=%d materials=%d images=%d" % (
    [a.name for a in arms], len(meshes), len(actions), len(bpy.data.materials), len(bpy.data.images)))
def load_smd_animation(arm, path, scale):
    """Bake an SMD 'skeleton' block onto arm as a new action. SMD bones are parent-relative: position + XYZ Euler (radians)."""
    from mathutils import Matrix, Euler, Vector
    nodes, frames, cur, mode = {}, [], None, None
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            t = line.strip()
            if not t or t.startswith("//"):
                continue
            if t in ("nodes", "skeleton", "triangles"):
                mode = t
                continue
            if t == "end":
                mode = None
                continue
            if mode == "nodes":
                i, rest = t.split(" ", 1)
                name = rest[rest.index('"') + 1:rest.rindex('"')]
                nodes[int(i)] = name
            elif mode == "skeleton":
                if t.startswith("time"):
                    cur = {}
                    frames.append(cur)
                else:
                    v = t.split()
                    cur[int(v[0])] = tuple(float(x) for x in v[1:7])
    rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
    act = bpy.data.actions.new(os.path.splitext(os.path.basename(path))[0])
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    last = {}
    for fi, fr in enumerate(frames):
        for idx, (px, py, pz, rx, ry, rz) in fr.items():
            last[idx] = (px, py, pz, rx, ry, rz)
        for idx, (px, py, pz, rx, ry, rz) in last.items():
            name = nodes.get(idx)
            pb = arm.pose.bones.get(name) if name else None
            if pb is None:
                continue
            local = Matrix.Translation(Vector((px, py, pz)) * scale) @ Euler((rx, ry, rz), "XYZ").to_matrix().to_4x4()
            par = parent[name]
            parent_rest = rest[par] if par else Matrix.Identity(4)
            basis = rest[name].inverted() @ parent_rest @ local
            loc, rot, _ = basis.decompose()
            pb.rotation_mode = "QUATERNION"
            pb.location, pb.rotation_quaternion = loc, rot
            pb.keyframe_insert("location", frame=fi + 1)
            pb.keyframe_insert("rotation_quaternion", frame=fi + 1)
    if hasattr(ad, "action_slot") and act.slots:
        ad.action_slot = act.slots[0]
    print("S4D smd", act.name, "frames", len(frames), "bones", len(nodes))
    return act


if args.smd and arms:
    actions = [load_smd_animation(arms[0], p, 0.0254) for p in args.smd]
    bpy.context.scene.render.fps = int(args.fps)

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
    # Export exactly one take: the first kept action as the armature's active action (Blender 5 slotted actions).
    arm = arms[0]
    ad = arm.animation_data or arm.animation_data_create()
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)
    act = actions[0]
    ad.action = act
    if hasattr(ad, "action_slot") and getattr(act, "slots", None):
        ad.action_slot = act.slots[0]
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = int(act.frame_range[0]), int(act.frame_range[1])
    print("S4D active action", act.name, act.frame_range[:], "slot", getattr(ad, "action_slot", None))

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
    add_leaf_bones=False, bake_anim=bool(actions), bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
print("S4D wrote", args.out)
