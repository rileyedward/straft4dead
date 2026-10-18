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

    public static void Build()
    {
        string src = Arg("-s4dSource"), outDir = Arg("-s4dOut");
        if (src == null || outDir == null)
            throw new ArgumentException("pass -s4dSource and -s4dOut");

        Directory.CreateDirectory(PrefabRoot);
        foreach (var m in Models)
            CopyTree(Path.Combine(src, m.dir), Path.Combine(ImportRoot, m.dir));
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        foreach (var m in Models)
        {
            string fbxPath = $"{ImportRoot}/{m.dir}/{m.fbx}";
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            imp.animationType = m.human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.materialSearch = ModelImporterMaterialSearch.Everywhere;
            imp.materialName = ModelImporterMaterialName.BasedOnTextureName;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips) c.loopTime = true;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
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

            if (m.clip != null)
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview") && c.name.Contains(m.clip));
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
