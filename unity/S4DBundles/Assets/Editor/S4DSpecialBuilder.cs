// Phase 6 builder: special infected (Tank, Boomer, Witch) -> AssetBundle "s4d_specials".
//
//   Unity.exe -batchmode -quit -projectPath unity/S4DBundles -executeMethod S4DSpecialBuilder.Build
//       -s4dSource <straft4dead-research\fbx> -s4dOut <bundle folder>
//
// Input comes from tools/pipeline/build_specials.py: special_<model>/{<model>.fbx, anims.fbx, textures/}. Same setup as
// the commons: Generic rigs on a shared armature name, clips played in place, one Standard material per texture.
// Prefabs are named Special_<model>, each with its own animator controller (states = the clip names in the script).
// Build without -nographics.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class S4DSpecialBuilder
{
    const string Root = "Assets/L4D2/Specials";
    const string Bundle = "s4d_specials";

    // Clips that play once (everything else loops). Matches the state names in build_specials.py.
    static readonly HashSet<string> OneShot = new HashSet<string> { "Attack", "Throw", "Yell", "Flinch", "Death", "Vomit" };
    static readonly Dictionary<string, string[]> States = new Dictionary<string, string[]>
    {
        { "hulk", new[] { "Idle:Idle", "Walk:a_WalkN", "Run:a_RunN", "Attack:Melee", "Throw:a_HulkThrowN", "Yell:Idle_Yell", "Flinch:Flinch_chest", "Death:Death" } },
        { "boomer", new[] { "Idle:Idle_Standing", "Walk:a_Walk_Upper_KnifeN", "Run:a_RunN", "Attack:Melee_01", "Vomit:a_Vomit", "Flinch:Flinch_01" } },
        { "witch", new[] { "Idle:Idle_Sitting", "Agitated:Agitated_Idlenoise", "Walk:a_Agitated_wander1", "Run:Run_SBlend", "Attack:Melee_01", "Flinch:Flinch_01", "Death:Death_02" } },
    };

    public static void Build()
    {
        string src = Arg("-s4dSource"), outDir = Arg("-s4dOut");
        Directory.CreateDirectory(Root + "/Prefabs");
        foreach (var model in States.Keys)
        {
            string from = Path.Combine(src, "special_" + model), to = $"{Root}/{model}";
            if (!File.Exists(Path.Combine(from, model + ".fbx"))) { Debug.LogWarning($"S4D special {model}: not exported"); continue; }
            Directory.CreateDirectory(to + "/textures");
            File.Copy(Path.Combine(from, model + ".fbx"), $"{to}/{model}.fbx", true);
            File.Copy(Path.Combine(from, "anims.fbx"), $"{to}/anims.fbx", true);
            foreach (var png in Directory.GetFiles(Path.Combine(from, "textures"), "*.png").Where(p => !Path.GetFileName(p).StartsWith("SIO")))
                File.Copy(png, $"{to}/textures/{Path.GetFileName(png)}", true);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        var shader = Shader.Find("Standard");

        foreach (var model in States.Keys)
        {
            string fbx = $"{Root}/{model}/{model}.fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (imp == null) continue;
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.importAnimation = false;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().First();

            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            go.name = "Special_" + model;
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.runtimeAnimatorController = BuildController(model);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var pngs = Directory.GetFiles($"{Root}/{model}/textures", "*.png").Select(Path.GetFileNameWithoutExtension).ToList();
            var made = new Dictionary<string, Material>();
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    string full = shared[i] != null ? shared[i].name : r.name;
                    string tex = pngs.Where(n => full.EndsWith(n, StringComparison.OrdinalIgnoreCase)).OrderByDescending(n => n.Length).FirstOrDefault();
                    if (tex == null) { Debug.Log($"S4D special {model}: no texture for '{full}'"); shared[i] = null; continue; }
                    if (!made.TryGetValue(tex, out var m))
                    {
                        m = new Material(shader) { name = $"{model}_{tex}" };
                        var ti = (TextureImporter)AssetImporter.GetAtPath($"{Root}/{model}/textures/{tex}.png");
                        ti.alphaIsTransparency = tex.ToLowerInvariant().Contains("hair");
                        ti.SaveAndReimport();
                        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{model}/textures/{tex}.png"));
                        m.SetFloat("_Glossiness", 0.15f);
                        if (ti.alphaIsTransparency)
                        {
                            m.SetFloat("_Mode", 1f); m.SetFloat("_Cutoff", 0.4f);
                            m.EnableKeyword("_ALPHATEST_ON"); m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = 2450;
                        }
                        AssetDatabase.CreateAsset(m, $"{Root}/Prefabs/{m.name}.mat");
                        made[tex] = m;
                    }
                    shared[i] = m;
                }
                r.sharedMaterials = shared;
                r.localBounds = new Bounds(Vector3.zero, Vector3.one * 3.5f); // bind pose lies down; the clips stand it up
            }
            Debug.Log($"S4D special {model}: materials [{string.Join(", ", made.Keys)}], renderers [{string.Join(", ", go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.name))}]");
            string prefab = $"{Root}/Prefabs/{go.name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefab);
            UnityEngine.Object.DestroyImmediate(go);
            AssetImporter.GetAtPath(prefab).SetAssetBundleNameAndVariant(Bundle, "");
        }

        Directory.CreateDirectory(outDir);
        var manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Debug.Log("S4D built bundles: " + string.Join(", ", manifest.GetAllAssetBundles()));
    }

    static AnimatorController BuildController(string model)
    {
        string fbx = $"{Root}/{model}/anims.fbx";
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        var states = States[model].Select(s => s.Split(':')).ToList();
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            var st = states.FirstOrDefault(s => c.takeName.EndsWith(s[1]) || c.name.EndsWith(s[1]));
            c.loopTime = st != null && !OneShot.Contains(st[0]);
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        var all = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath($"{Root}/Prefabs/{model}.controller");
        var sm = ctrl.layers[0].stateMachine;
        foreach (var s in states)
        {
            // Longest match first: "a_RunN" must not grab "a_RunN_2b"-style variants or vice versa.
            var clip = all.Where(c => c.name.EndsWith(s[1])).OrderBy(c => c.name.Length).FirstOrDefault();
            if (clip == null) { Debug.LogWarning($"S4D special {model}: no clip for {s[0]} ({s[1]})"); continue; }
            var state = sm.AddState(s[0]);
            state.motion = clip;
            if (s[0] == "Idle") sm.defaultState = state;
        }
        Debug.Log($"S4D special {model}: clips [{string.Join(", ", all.Select(c => $"{c.name} {c.length:F2}s"))}]");
        return ctrl;
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
}
