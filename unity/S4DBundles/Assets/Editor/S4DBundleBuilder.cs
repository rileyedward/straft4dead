// Batch-mode builder: imports FBX exported by tools/pipeline/blender_export.py, makes prefabs, builds one AssetBundle.
//
//   Unity.exe -batchmode -quit -projectPath unity/S4DBundles -executeMethod S4DBundleBuilder.Build
//       -s4dSource <folder with infected/, ellis/, ellis_arms/ FBX exports> -s4dOut <bundle output folder>
//
// Imported L4D2 assets land in Assets/L4D2/ and built bundles in -s4dOut; both are git-ignored.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class S4DBundleBuilder
{
    const string ImportRoot = "Assets/L4D2";
    const string PrefabRoot = "Assets/L4D2/Prefabs";
    const string BundleName = "s4d_phase0";

    // name, source subfolder, fbx file, humanoid?, clip to play (null = none), placeholder tint (null = use FBX materials)
    static readonly (string name, string dir, string fbx, bool human, string clip)[] Models =
    {
        ("Infected_Common", "infected", "common_male_tshirt_cargos.fbx", true, "a_Run_01Zero"),
        ("Ellis_Body", "ellis", "survivor_mechanic.fbx", true, "a_Run_RifleZero"),
        ("Ellis_Arms", "ellis_arms", "v_arms_mechanic_new.fbx", false, null),
    };

    // Source engine ValveBiped bone -> Unity human bone. Spine2 stays unmapped (Unity allows intermediate bones).
    static readonly (string human, string bone)[] ValveBipedMap =
    {
        ("Hips", "Bip01_Pelvis"), ("Spine", "Bip01_Spine"), ("Chest", "Bip01_Spine1"), ("UpperChest", "Bip01_Spine4"),
        ("Neck", "Bip01_Neck1"), ("Head", "Bip01_Head1"),
        ("LeftShoulder", "Bip01_L_Clavicle"), ("LeftUpperArm", "Bip01_L_UpperArm"), ("LeftLowerArm", "Bip01_L_Forearm"), ("LeftHand", "Bip01_L_Hand"),
        ("RightShoulder", "Bip01_R_Clavicle"), ("RightUpperArm", "Bip01_R_UpperArm"), ("RightLowerArm", "Bip01_R_Forearm"), ("RightHand", "Bip01_R_Hand"),
        ("LeftUpperLeg", "Bip01_L_Thigh"), ("LeftLowerLeg", "Bip01_L_Calf"), ("LeftFoot", "Bip01_L_Foot"), ("LeftToes", "Bip01_L_Toe0"),
        ("RightUpperLeg", "Bip01_R_Thigh"), ("RightLowerLeg", "Bip01_R_Calf"), ("RightFoot", "Bip01_R_Foot"), ("RightToes", "Bip01_R_Toe0"),
        ("Left Thumb Proximal", "Bip01_L_Finger0"), ("Left Thumb Intermediate", "Bip01_L_Finger01"), ("Left Thumb Distal", "Bip01_L_Finger02"),
        ("Left Index Proximal", "Bip01_L_Finger1"), ("Left Index Intermediate", "Bip01_L_Finger11"), ("Left Index Distal", "Bip01_L_Finger12"),
        ("Left Middle Proximal", "Bip01_L_Finger2"), ("Left Middle Intermediate", "Bip01_L_Finger21"), ("Left Middle Distal", "Bip01_L_Finger22"),
        ("Left Ring Proximal", "Bip01_L_Finger3"), ("Left Ring Intermediate", "Bip01_L_Finger31"), ("Left Ring Distal", "Bip01_L_Finger32"),
        ("Left Little Proximal", "Bip01_L_Finger4"), ("Left Little Intermediate", "Bip01_L_Finger41"), ("Left Little Distal", "Bip01_L_Finger42"),
        ("Right Thumb Proximal", "Bip01_R_Finger0"), ("Right Thumb Intermediate", "Bip01_R_Finger01"), ("Right Thumb Distal", "Bip01_R_Finger02"),
        ("Right Index Proximal", "Bip01_R_Finger1"), ("Right Index Intermediate", "Bip01_R_Finger11"), ("Right Index Distal", "Bip01_R_Finger12"),
        ("Right Middle Proximal", "Bip01_R_Finger2"), ("Right Middle Intermediate", "Bip01_R_Finger21"), ("Right Middle Distal", "Bip01_R_Finger22"),
        ("Right Ring Proximal", "Bip01_R_Finger3"), ("Right Ring Intermediate", "Bip01_R_Finger31"), ("Right Ring Distal", "Bip01_R_Finger32"),
        ("Right Little Proximal", "Bip01_R_Finger4"), ("Right Little Intermediate", "Bip01_R_Finger41"), ("Right Little Distal", "Bip01_R_Finger42"),
    };

    /// <summary>Explicit human description for a ValveBiped rig, built from the model's own rest pose.</summary>
    internal static HumanDescription ValveBipedHuman(GameObject modelAsset)
    {
        // Work on a scene copy so the rest pose can be bent into a T-pose before it becomes the avatar's reference:
        // Unity measures muscles from this pose, and Source reference poses are not T-poses.
        var model = UnityEngine.Object.Instantiate(modelAsset);
        model.name = modelAsset.name; // skeleton names must match the FBX hierarchy, including the root
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var byName = model.GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        EnforceTPose(byName);
        var human = ValveBipedMap
            .Where(p => byName.ContainsKey("ValveBiped." + p.bone))
            .Select(p => new HumanBone { humanName = p.human, boneName = "ValveBiped." + p.bone, limit = new HumanLimit { useDefaultValues = true } })
            .ToArray();
        var skeleton = model.GetComponentsInChildren<Transform>(true)
            .Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })
            .ToArray();
        UnityEngine.Object.DestroyImmediate(model);
        return new HumanDescription
        {
            human = human, skeleton = skeleton,
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
        };
    }

    // Point each limb bone along the T-pose direction (character faces +Z: left = -X, right = +X, legs down).
    static void EnforceTPose(System.Collections.Generic.Dictionary<string, Transform> t)
    {
        void Aim(string bone, string child, Vector3 dir)
        {
            if (!t.TryGetValue("ValveBiped." + bone, out var b) || !t.TryGetValue("ValveBiped." + child, out var c))
                return;
            Vector3 cur = c.position - b.position;
            if (cur.sqrMagnitude < 1e-8f)
                return;
            b.rotation = Quaternion.FromToRotation(cur, dir) * b.rotation;
        }
        foreach (var (side, x) in new[] { ("L", -1f), ("R", 1f) })
        {
            Aim($"Bip01_{side}_UpperArm", $"Bip01_{side}_Forearm", new Vector3(x, 0, 0));
            Aim($"Bip01_{side}_Forearm", $"Bip01_{side}_Hand", new Vector3(x, 0, 0));
            Aim($"Bip01_{side}_Thigh", $"Bip01_{side}_Calf", Vector3.down);
            Aim($"Bip01_{side}_Calf", $"Bip01_{side}_Foot", Vector3.down);
        }
        if (t.TryGetValue("ValveBiped.Bip01_Pelvis", out var pelvis) && t.TryGetValue("ValveBiped.Bip01_Head1", out var head))
            Debug.Log($"S4D tpose pelvis->head {(head.position - pelvis.position).normalized:F2}");
    }

    // Animation-only FBX (Crowbar SMD baked in Blender) retargeted through the body's own avatar.
    static readonly (string prefab, string dir, string fbx)[] AnimSources =
    {
        ("Infected_Common", "infected_anim", "common_run_01.fbx"),
        ("Ellis_Body", "ellis_anim", "ellis_anim.fbx"),
    };

    static AnimationClip ImportHumanClip(string fbxPath, Avatar avatar)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        imp.sourceAvatar = avatar;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.loopTime = true;
            c.lockRootRotation = true;       // bake root yaw, height and XZ travel into the pose: plays in place
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = true;
            c.keepOriginalPositionY = true;
            c.keepOriginalPositionXZ = true;
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
    }

    public static void Build()
    {
        string src = Arg("-s4dSource"), outDir = Arg("-s4dOut");
        if (src == null || outDir == null)
            throw new ArgumentException("pass -s4dSource and -s4dOut");

        Directory.CreateDirectory(PrefabRoot);
        foreach (var m in Models)
            CopyTree(Path.Combine(src, m.dir), Path.Combine(ImportRoot, m.dir));
        foreach (var a in AnimSources)
            if (Directory.Exists(Path.Combine(src, a.dir)))
                CopyTree(Path.Combine(src, a.dir), Path.Combine(ImportRoot, a.dir));
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        foreach (var m in Models)
        {
            string fbxPath = $"{ImportRoot}/{m.dir}/{m.fbx}";
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            imp.animationType = m.human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            if (m.human)
                imp.humanDescription = ValveBipedHuman(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.materialSearch = ModelImporterMaterialSearch.Everywhere;
            imp.materialName = ModelImporterMaterialName.BasedOnTextureName;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips) c.loopTime = true;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();

            var all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            Debug.Log($"S4D model {m.name}: takes=[{string.Join(", ", imp.importedTakeInfos.Select(t => t.name))}] " +
                      $"clips=[{string.Join(", ", all.OfType<AnimationClip>().Select(c => c.name))}] " +
                      $"transforms={all.OfType<Transform>().Count()} first=[{string.Join(", ", all.OfType<Transform>().Take(6).Select(t => t.name))}]");
            var avatar = all.OfType<Avatar>().FirstOrDefault();
            Debug.Log($"S4D model {m.name}: avatar={(avatar ? avatar.name : "none")} valid={(avatar && avatar.isValid)} human={(avatar && avatar.isHuman)}");
            if (m.human && avatar && avatar.isHuman)
            {
                foreach (var hb in imp.humanDescription.human)
                    Debug.Log($"S4D   {m.name} human {hb.humanName} <- {hb.boneName}");
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = m.name;
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.applyRootMotion = false;

            var animSrc = AnimSources.FirstOrDefault(x => x.prefab == m.name);
            string animPath = animSrc.fbx != null ? $"{ImportRoot}/{animSrc.dir}/{animSrc.fbx}" : null;
            if (animPath != null && File.Exists(animPath) && avatar != null && avatar.isHuman)
            {
                var clip = ImportHumanClip(animPath, avatar);
                Debug.Log($"S4D model {m.name}: anim {animPath} -> {(clip ? clip.name + " " + clip.length + "s humanMotion=" + clip.humanMotion : "MISSING")}");
                if (clip)
                {
                    var ctrl = AnimatorController.CreateAnimatorControllerAtPath($"{PrefabRoot}/{m.name}.controller");
                    ctrl.layers[0].stateMachine.AddState(clip.name).motion = clip;
                    anim.runtimeAnimatorController = ctrl;
                }
            }
            else if (m.clip != null)
            {
                // Blender writes the single exported action as the take "Scene"; prefer the named one if present.
                var takes = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview")).ToList();
                var clip = takes.FirstOrDefault(c => c.name.Contains(m.clip)) ?? takes.FirstOrDefault();
                Debug.Log($"S4D model {m.name}: clip {m.clip} -> {(clip ? clip.name + " " + clip.length + "s humanMotion=" + clip.humanMotion : "MISSING")}");
                if (clip)
                {
                    var ctrl = AnimatorController.CreateAnimatorControllerAtPath($"{PrefabRoot}/{m.name}.controller");
                    ctrl.layers[0].stateMachine.AddState(clip.name).motion = clip;
                    anim.runtimeAnimatorController = ctrl;
                }
            }

            if (m.name.StartsWith("Infected"))
            {
                // L4D2's "Infected" shader packs blood/grime/palette indices into the base texture; SourceIO can't
                // translate it. Phase 0 uses a flat placeholder; a palette shader is Phase 3 work.
                var mat = new Material(Shader.Find("Standard")) { color = new Color(0.42f, 0.40f, 0.36f) };
                AssetDatabase.CreateAsset(mat, $"{PrefabRoot}/{m.name}_Placeholder.mat");
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            }

            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabRoot}/{m.name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            AssetImporter.GetAtPath($"{PrefabRoot}/{m.name}.prefab").SetAssetBundleNameAndVariant(BundleName, "");
        }

        Directory.CreateDirectory(outDir);
        var manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Debug.Log("S4D built bundles: " + string.Join(", ", manifest.GetAllAssetBundles()));
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(from.Length).TrimStart('\\', '/');
            string dst = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(f, dst, true);
        }
    }
}

public static class S4DProbe
{
    // Unity.exe -batchmode -quit -projectPath unity/S4DBundles -executeMethod S4DProbe.Run
    // Reports body heights in bind pose, after sampling the clip, and after a neutral human pose.
    public static void Run()
    {
        foreach (var name in new[] { "Ellis_Body", "Infected_Common" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/L4D2/Prefabs/{name}.prefab");
            var go = UnityEngine.Object.Instantiate(prefab);
            var a = go.GetComponent<Animator>();
            Report(name + " bind", a);
            var ctrl = a.runtimeAnimatorController;
            if (ctrl != null && ctrl.animationClips.Length > 0)
            {
                ctrl.animationClips[0].SampleAnimation(go, 0.5f);
                Report(name + " clip@0.5", a);
            }
            var h = new HumanPoseHandler(a.avatar, a.transform);
            var pose = new HumanPose();
            h.GetHumanPose(ref pose);
            Debug.Log($"S4D probe {name} bodyPos={pose.bodyPosition} muscles[0..5]={string.Join(",", pose.muscles.Take(6).Select(x => x.ToString("F2")))}");
            pose.muscles = new float[pose.muscles.Length];
            h.SetHumanPose(ref pose);
            Report(name + " neutral", a);
            var root = go.transform.GetChild(0);
            Debug.Log($"S4D probe {name} children=[{string.Join(", ", Enumerable.Range(0, go.transform.childCount).Select(i => go.transform.GetChild(i).name + " rot " + go.transform.GetChild(i).localEulerAngles + " scale " + go.transform.GetChild(i).localScale))}]");
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static void Report(string label, Animator a)
    {
        var head = a.GetBoneTransform(HumanBodyBones.Head);
        var foot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
        var hips = a.GetBoneTransform(HumanBodyBones.Hips);
        Debug.Log($"S4D probe {label}: head {head.position:F2} foot {foot.position:F2} hips {hips.position:F2} hipsRot {hips.rotation.eulerAngles:F0}");
    }
}
