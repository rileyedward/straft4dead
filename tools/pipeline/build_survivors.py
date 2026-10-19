"""Build the survivor FBX set from the player's own L4D2 install (Phase 4).

usage: python -I build_survivors.py <L4D2 left4dead2 dir> <work dir> <blender.exe>

For each L4D2 survivor in SURVIVORS: extract the third-person body and the first-person arms from the VPK, export
both as FBX with SourceIO (the body stood up and grounded for a humanoid avatar, the arms raw: the plugin poses them
bone by bone onto STRAFTAT's arm rig), and convert every material's base texture to PNG named after the material.
Outputs: <work>/fbx/survivor_<name>/{body.fbx, arms.fbx, textures/}.
"""
import os, re, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SURVIVORS = ["mechanic", "gambler", "producer", "coach"]  # Ellis, Nick, Rochelle, Coach

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


def blender_exports(jobs):
    """Runs several blender_export(mdl, fbx, extra) jobs side by side (each is its own Blender process, mostly
    single-threaded) and returns their results in order."""
    from concurrent.futures import ThreadPoolExecutor
    workers = max(1, min(len(jobs), (os.cpu_count() or 2) // 2, 4))
    with ThreadPoolExecutor(workers) as pool:
        return list(pool.map(lambda job: blender_export(*job), jobs))


def find_vmt(name):
    for root, _, files in os.walk(os.path.join(extracted, "materials")):
        for f in files:
            if f.lower() == name.lower() + ".vmt":
                return os.path.join(root, f)
    return None


def base_texture(vmt_path):
    text = open(vmt_path, encoding="utf-8", errors="replace").read()
    # Eyes (EyeRefract) have no $basetexture; their iris map is the closest thing.
    m = re.search(r'"?\$basetexture"?\s+"?([^"\s]+)"?', text, re.I) or re.search(r'"?\$iris"?\s+"?([^"\s]+)"?', text, re.I)
    return m.group(1).replace("\\", "/") if m else None


print(run([sys.executable, "-I", os.path.join(HERE, "vpk.py"), "extract", vpk, extracted] +
          ["models/survivors/survivor_%s." % s for s in SURVIVORS] +
          ["models/weapons/arms/v_arms_%s_new." % s for s in SURVIVORS] + ["materials/models/survivors/"]).strip())

textures_to_convert = {}  # png path -> vtf path
jobs = []
for s in SURVIVORS:
    out_dir = os.path.join(work, "fbx", "survivor_" + s)
    os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
    jobs.append((os.path.join(extracted, "models", "survivors", "survivor_%s.mdl" % s), os.path.join(out_dir, "body.fbx"), ["--no-anims"]))
    jobs.append((os.path.join(extracted, "models", "weapons", "arms", "v_arms_%s_new.mdl" % s), os.path.join(out_dir, "arms.fbx"), ["--no-anims", "--raw"]))
# Body and arms share a folder, so they go in separate rounds.
exported = [r for pair in zip(blender_exports(jobs[0::2]), blender_exports(jobs[1::2])) for r in pair]
for i, s in enumerate(SURVIVORS):
    out_dir = os.path.join(work, "fbx", "survivor_" + s)
    mats = exported[2 * i] + exported[2 * i + 1]
    print(s, "materials:", ", ".join(sorted(set(mats))))
    for mat in set(mats):
        mat = mat.replace("\\", "/").split("/")[-1]
        vmt = find_vmt(mat)
        tex = base_texture(vmt) if vmt else None
        vtf = os.path.join(extracted, "materials", *tex.split("/")) + ".vtf" if tex else None
        if vtf and os.path.exists(vtf):
            textures_to_convert[os.path.join(out_dir, "textures", mat + ".png")] = vtf
        else:
            print("  no base texture for", mat)

tmp = os.path.join(work, "fbx", "_vtf_tmp")
os.makedirs(tmp, exist_ok=True)
vtfs = sorted(set(textures_to_convert.values()))
print(run([blender, "-b", "--factory-startup", "--addons", "SourceIO", "--python",
           os.path.join(HERE, "vtf_to_png.py"), "--", tmp] + vtfs).count("S4D png"), "textures converted")
for png, vtf in textures_to_convert.items():
    src = os.path.join(tmp, os.path.splitext(os.path.basename(vtf))[0] + ".png")
    if os.path.exists(src):
        shutil.copyfile(src, png)
