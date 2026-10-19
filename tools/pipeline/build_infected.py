"""Build the common-infected FBX set from the player's own L4D2 install (Phase 3).

usage: python -I build_infected.py <L4D2 left4dead2 dir> <work dir> <blender.exe>

For each outfit in MODELS: extract the MDL from the VPK, export a rest-pose FBX with SourceIO, and convert every
material's base texture (L4D2 "Infected" shader masks) plus the shared gradient palette to PNG next to it. Then bake
the shared animation set (Crowbar SMDs of anim_common, decompiled beforehand into <work>/crowbar/anim_common_anims)
onto one body and export it as a single animation FBX that every outfit plays through its humanoid avatar.
Outputs: <work>/fbx/infected_<outfit>/ and <work>/fbx/infected_anim/common_anims.fbx.
"""
import os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = [
    "common_male_tshirt_cargos", "common_male_polo_jeans", "common_male_dressshirt_jeans",
    "common_male_tanktop_jeans", "common_female_tanktop_jeans", "common_female_tshirt_skirt",
]
# Unity state name -> anim_common SMD (sequence names from anim_common.qc)
CLIPS = {
    "Idle": "Idle_Neutral_01", "Walk": "Shamble_01", "Run": "a_Run_01", "Attack": "Melee_01",
    "Flinch": "Shoved_Backward_01", "Death": "Death_01", "DeathRunning": "DeathRunning_01",
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
          ["models/infected/%s." % m for m in MODELS] + ["materials/models/infected/"]).strip())

textures_to_convert = {}  # png path -> vtf path
for model in MODELS:
    out_dir = os.path.join(work, "fbx", "infected_" + model)
    os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
    mats = blender_export(os.path.join(extracted, "models", "infected", model + ".mdl"), os.path.join(out_dir, model + ".fbx"), ["--no-anims", "--raw", "--armature-name", "S4D_Armature"])
    print(model, "materials:", ", ".join(mats))
    for mat in mats:
        mat = mat.replace("\\", "/").split("/")[-1]  # SourceIO keeps the material path in the name
        vmt = find_vmt(mat)
        tex = base_texture(vmt) if vmt else None
        if not tex:
            continue
        vtf = os.path.join(extracted, "materials", *tex.split("/")) + ".vtf"
        if os.path.exists(vtf):
            textures_to_convert[os.path.join(out_dir, "textures", mat + ".png")] = vtf
    pal = os.path.join(extracted, "materials", "models", "infected", "common", "l4d2", "ci_gradient_palette.vtf")
    textures_to_convert[os.path.join(out_dir, "textures", "ci_gradient_palette.png")] = pal

# vtf_to_png names outputs after the VTF, so convert into a temp dir and copy to material names.
tmp = os.path.join(work, "fbx", "_vtf_tmp")
os.makedirs(tmp, exist_ok=True)
vtfs = sorted(set(textures_to_convert.values()))
print(run([blender, "-b", "--factory-startup", "--addons", "SourceIO", "--python",
           os.path.join(HERE, "vtf_to_png.py"), "--", tmp] + vtfs).count("S4D png"), "textures converted")
import shutil
for png, vtf in textures_to_convert.items():
    src = os.path.join(tmp, os.path.splitext(os.path.basename(vtf))[0] + ".png")
    if os.path.exists(src):
        shutil.copyfile(src, png)

anims = os.path.join(work, "crowbar", "anim_common_anims")
anim_dir = os.path.join(work, "fbx", "infected_anim")
os.makedirs(anim_dir, exist_ok=True)
smd_args = []
for state, smd in CLIPS.items():
    smd_args += ["--smd", os.path.join(anims, smd + ".smd")]
blender_export(os.path.join(extracted, "models", "infected", MODELS[0] + ".mdl"), os.path.join(anim_dir, "common_anims.fbx"), ["--no-anims", "--raw", "--armature-name", "S4D_Armature"] + smd_args)
print("animation set:", ", ".join(CLIPS))
