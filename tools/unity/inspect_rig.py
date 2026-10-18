"""Answer "is STRAFTAT's player rig Humanoid?" from data.unity3d with UnityPy.

usage: python -P inspect_rig.py <STRAFTAT_Data dir> [clip-name-substring ...]
Prints each Avatar's human description summary (mapped human bones, skeleton size) and, for matching
AnimationClips, whether their curves are humanoid muscle curves or generic transform curves.
"""
import os, sys
import UnityPy

data_dir = sys.argv[1]
wanted = [s.lower() for s in sys.argv[2:]] or ["aboubi"]
env = UnityPy.load(os.path.join(data_dir, "data.unity3d"))


def walk_keys(d, prefix="", depth=0, out=None):
    out = [] if out is None else out
    if depth > 4 or not isinstance(d, dict):
        return out
    for k, v in d.items():
        out.append("%s%s : %s" % ("  " * depth, k, type(v).__name__ + (" len=%d" % len(v) if isinstance(v, (list, str)) else "")))
        walk_keys(v, prefix, depth + 1, out)
    return out


for obj in env.objects:
    t = obj.type.name
    if t == "Avatar":
        tree = obj.read_typetree()
        print("== Avatar", tree.get("m_Name"))
        human = tree["m_Avatar"]["m_Human"]["data"]
        idx = human.get("m_HumanBoneIndex", [])
        print("   m_HumanBoneIndex mapped=%d of %d" % (sum(1 for i in idx if i >= 0), len(idx)))
        print("   human skeleton nodes=%d  avatar skeleton nodes=%d" % (
            len(human["m_Skeleton"]["data"]["m_Node"]),
            len(tree["m_Avatar"]["m_AvatarSkeleton"]["data"]["m_Node"])))
        print("   hasLeftHand=%s hasRightHand=%s scale=%s" % (human.get("m_HasLeftHand"), human.get("m_HasRightHand"), human.get("m_Scale")))
    elif t == "AnimationClip":
        name = obj.peek_name() or ""
        if not any(w in name.lower() for w in wanted):
            continue
        tree = obj.read_typetree()
        gb = tree.get("m_ClipBindingConstant", {}).get("genericBindings", [])
        muscle = sum(1 for b in gb if b.get("typeID") == 95)       # Animator: humanoid muscle / root curves
        xform = sum(1 for b in gb if b.get("typeID") == 4)         # Transform: generic bone curves
        print("-- clip %-40s bindings=%4d animator(muscle)=%4d transform=%4d legacy=%s" % (
            name, len(gb), muscle, xform, tree.get("m_Legacy")))
