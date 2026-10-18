"""Scan STRAFTAT's data.unity3d with UnityPy and print a type census plus the facts Phase 0 needs:
Avatars (humanoid or generic), Animator controllers, NavMeshData, TagManager layers.

usage: python -I scan_assets.py <STRAFTAT_Data dir> <outdir>
Writes census.txt, avatars.txt, layers.txt, navmesh.txt into <outdir> (keep it outside the repo).
"""
import collections, os, sys
import UnityPy

data_dir, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
env = UnityPy.load(os.path.join(data_dir, "data.unity3d"))

census = collections.Counter()
avatars, navs, layers, controllers = [], [], [], []
for obj in env.objects:
    t = obj.type.name
    census[t] += 1
    try:
        if t == "Avatar":
            tree = obj.read_typetree()
            av = tree.get("m_Avatar", {})
            human = av.get("m_Human", {})
            skel = human.get("data", human).get("m_Skeleton", {}) if isinstance(human, dict) else {}
            bones = human.get("m_HumanBoneIndex") if isinstance(human, dict) else None
            n_human = sum(1 for i in (bones or []) if i >= 0)
            tos = tree.get("m_TOS", [])
            names = [v for _, v in tos] if tos and isinstance(tos[0], (list, tuple)) else [d.get("second") for d in tos]
            avatars.append((tree.get("m_Name"), n_human, len(names), names))
        elif t == "NavMeshData":
            tree = obj.read_typetree()
            navs.append((obj.assets_file.name, tree.get("m_Name"), len(tree.get("m_NavMeshTiles", []))))
        elif t == "TagManager":
            tree = obj.read_typetree()
            layers.append(("tags", tree.get("tags")))
            layers.append(("layers", list(enumerate(tree.get("layers", [])))))
        elif t == "AnimatorController":
            tree = obj.read_typetree()
            tos = tree.get("m_TOS", [])
            names = [v for _, v in tos] if tos and isinstance(tos[0], (list, tuple)) else [d.get("second") for d in tos]
            controllers.append((tree.get("m_Name"), len(tree.get("m_AnimationClips", [])), names))
    except Exception as e:  # keep scanning; record the failure
        census["!error:" + t] += 1

with open(os.path.join(out, "census.txt"), "w", encoding="utf-8") as fh:
    for k, v in census.most_common():
        fh.write("%7d %s\n" % (v, k))
with open(os.path.join(out, "avatars.txt"), "w", encoding="utf-8") as fh:
    for name, n_human, n_tos, names in avatars:
        fh.write("== %s  humanoid_bones_mapped=%d  transforms=%d\n" % (name, n_human, n_tos))
        for n in names:
            fh.write("   %s\n" % n)
with open(os.path.join(out, "navmesh.txt"), "w", encoding="utf-8") as fh:
    for row in navs:
        fh.write("%s %s tiles=%d\n" % row)
with open(os.path.join(out, "controllers.txt"), "w", encoding="utf-8") as fh:
    for name, n_clips, names in controllers:
        fh.write("== %s  clips=%d\n   %s\n" % (name, n_clips, ", ".join(n for n in names if n)))
with open(os.path.join(out, "layers.txt"), "w", encoding="utf-8") as fh:
    for k, v in layers:
        fh.write("%s: %s\n" % (k, v))
print("objects", sum(census.values()), "avatars", len(avatars), "navmeshdata", len(navs))
