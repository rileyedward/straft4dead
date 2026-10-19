"""Build the special infected FBX set (Tank, Boomer, Witch) from the player's own L4D2 install (Phase 6).

usage: python -I build_specials.py <L4D2 left4dead2 dir> <work dir> <blender.exe>

Needs each special's animation MDL decompiled by Crowbar first, into <work>/crowbar/anim_<model>_anims
(crowbar_decompile.ps1 on models/infected/anim_hulk.mdl, anim_boomer.mdl, anim_witch.mdl).
For each special: a raw rest-pose FBX of the body with its armature renamed S4D_Armature, its base textures as PNG
named after the materials, and one FBX with the clips below baked from the SMDs (root pinned, played in place).
Outputs: <work>/fbx/special_<model>/{<model>.fbx, anims.fbx, textures/}.
"""
import os, re, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
# model -> {Unity state: SMD name}
SPECIALS = {
    "hulk": {"Idle": "Idle", "Walk": "a_WalkN", "Run": "a_RunN", "Attack": "Melee", "Throw": "a_HulkThrowN",
             "Yell": "Idle_Yell", "Flinch": "Flinch_chest", "Death": "Death"},
    "boomer": {"Idle": "Idle_Standing", "Walk": "a_Walk_Upper_KnifeN", "Run": "a_RunN", "Attack": "Melee_01",
               "Vomit": "a_Vomit", "Flinch": "Flinch_01"},
    "witch": {"Idle": "Idle_Sitting", "Agitated": "Agitated_Idlenoise", "Walk": "a_Agitated_wander1", "Run": "Run_SBlend",
              "Attack": "Melee_01", "Flinch": "Flinch_01", "Death": "Death_02"},
}

l4d2, work, blender = sys.argv[1], os.path.abspath(sys.argv[2]), sys.argv[3]  # Blender resolves relative paths elsewhere
extracted = os.path.join(work, "extracted", "l4d2")
vpk = os.path.join(l4d2, "pak01_dir.vpk")


def run(args):
    out = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return out.stdout + out.stderr


def blender_export(mdl, fbx, extra):
    log = run([blender, "-b", "--factory-startup", "--addons", "SourceIO", "--python",
               os.path.join(HERE, "blender_export.py"), "--", "--mdl", mdl, "--out", fbx] + extra)
    if "S4D wrote" not in log:
        raise SystemExit("blender export failed for %s:\n%s" % (mdl, log[-3000:]))
    return [l.split(" ", 2)[2].strip() for l in log.splitlines() if l.startswith("S4D material ")]


def find_vmt(name):
    for root, _, files in os.walk(os.path.join(extracted, "materials")):
        for f in files:
            if f.lower() == name.lower() + ".vmt":
                return os.path.join(root, f)
    return None


def base_texture(vmt_path):
    text = open(vmt_path, encoding="utf-8", errors="replace").read()
    m = re.search(r'"?\$basetexture"?\s+"?([^"\s]+)"?', text, re.I)
    return m.group(1).replace("\\", "/") if m else None


print(run([sys.executable, "-I", os.path.join(HERE, "vpk.py"), "extract", vpk, extracted] +
          ["models/infected/%s." % m for m in SPECIALS] + ["materials/models/infected/%s/" % m for m in SPECIALS]).strip())

textures_to_convert = {}
for model, clips in SPECIALS.items():
    out_dir = os.path.join(work, "fbx", "special_" + model)
    os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
    mdl = os.path.join(extracted, "models", "infected", model + ".mdl")
    mats = blender_export(mdl, os.path.join(out_dir, model + ".fbx"), ["--no-anims", "--raw", "--armature-name", "S4D_Armature"])
    print(model, "materials:", ", ".join(sorted(set(mats))))
    for mat in set(mats):
        mat = mat.replace("\\", "/").split("/")[-1]
        vmt = find_vmt(mat)
        tex = base_texture(vmt) if vmt else None
        vtf = os.path.join(extracted, "materials", *tex.split("/")) + ".vtf" if tex else None
        if vtf and os.path.exists(vtf):
            textures_to_convert[os.path.join(out_dir, "textures", mat + ".png")] = vtf
    smd_dir = os.path.join(work, "crowbar", "anim_%s_anims" % model)
    smd_args = []
    for state, smd in clips.items():
        path = os.path.join(smd_dir, smd + ".smd")
        if os.path.exists(path):
            smd_args += ["--smd", path]
        else:
            print("  missing clip", state, smd)
    blender_export(mdl, os.path.join(out_dir, "anims.fbx"), ["--no-anims", "--raw", "--armature-name", "S4D_Armature"] + smd_args)
    print(model, "clips:", ", ".join(clips))

tmp = os.path.join(work, "fbx", "_vtf_tmp")
os.makedirs(tmp, exist_ok=True)
vtfs = sorted(set(textures_to_convert.values()))
print(run([blender, "-b", "--factory-startup", "--addons", "SourceIO", "--python",
           os.path.join(HERE, "vtf_to_png.py"), "--", tmp] + vtfs).count("S4D png"), "textures converted")
for png, vtf in textures_to_convert.items():
    src = os.path.join(tmp, os.path.splitext(os.path.basename(vtf))[0] + ".png")
    if os.path.exists(src):
        shutil.copyfile(src, png)
