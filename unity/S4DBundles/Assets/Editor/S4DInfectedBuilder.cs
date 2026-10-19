// Phase 3 builder: common infected outfits + shared animation set -> AssetBundle "s4d_infected".
//
//   Unity.exe -batchmode -quit -projectPath unity/S4DBundles -executeMethod S4DInfectedBuilder.Build
//       -s4dSource <straft4dead-research\fbx> -s4dOut <bundle folder>
//
// Input comes from tools/pipeline/build_infected.py: one folder per outfit (infected_<outfit>/<outfit>.fbx plus
// textures/<material>.png and ci_gradient_palette.png) and infected_anim/common_anims.fbx.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class S4DInfectedBuilder
{
    const string Root = "Assets/L4D2/Infected";
    const string Bundle = "s4d_infected";

    // Animator state -> source SMD/action name (matches CLIPS in build_infected.py). Looping states first.
    static readonly (string state, string action, bool loop)[] States =
    {
        ("Idle", "Idle_Neutral_01", true), ("Walk", "Shamble_01", true), ("Run", "a_Run_01", true), ("Attack", "Melee_01", true),
        ("Flinch", "Shoved_Backward_01", false), ("Death", "Death_01", false), ("DeathRunning", "DeathRunning_01", false),
    };

    public static void Build()
    {
        string src = Arg("-s4dSource"), outDir = Arg("-s4dOut");
        var outfits = Directory.GetDirectories(src, "infected_*")
            .Select(Path.GetFileName)
            .Where(d => d != "infected_anim" && File.Exists(Path.Combine(src, d, d.Substring("infected_".Length) + ".fbx")))
            .ToList();
        Directory.CreateDirectory(Root + "/Prefabs");
        foreach (var d in outfits.Concat(new[] { "infected_anim" }))
            CopyTree(Path.Combine(src, d), $"{Root}/{d}");
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/S4DInfected.shader");
        AnimatorController controller = null;
        foreach (var d in outfits)
        {
            string outfit = d.Substring("infected_".Length);
            string fbx = $"{Root}/{d}/{outfit}.fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            // Generic, not Humanoid: every outfit shares the ValveBiped hierarchy under "S4D_Armature", so the clips
            // bind by path. Humanoid retargeting moved the commons metres off their root.
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; // only to read material names
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.importAnimation = false;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().First();
            Debug.Log($"S4D infected {outfit}: avatar valid={avatar.isValid} generic");

            if (controller == null)
                controller = BuildController(avatar);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            go.name = "Infected_" + outfit;
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var palette = Texture($"{Root}/{d}/textures/ci_gradient_palette.png", true);
            var pngs = Directory.GetFiles($"{Root}/{d}/textures", "*.png").Select(Path.GetFileNameWithoutExtension).ToList();
            int mats = 0, missing = 0;
            var made = new Dictionary<string, Material>();
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    string full = shared[i] != null ? shared[i].name : r.name;
                    // FBX material names keep SourceIO's material path in some form; match the texture by suffix.
                    string matName = pngs.Where(n => full.EndsWith(n, StringComparison.OrdinalIgnoreCase)).OrderByDescending(n => n.Length).FirstOrDefault();
                    var mask = matName != null ? Texture($"{Root}/{d}/textures/{matName}.png", false) : null;
                    if (mask == null) Debug.Log($"S4D infected {outfit}: no texture for material '{full}' on {r.name}");
                    if (mask == null) { missing++; shared[i] = null; continue; }
                    // One material asset per texture: several bodygroup meshes share it, and re-creating the asset
                    // at the same path would orphan the earlier references (they'd render magenta).
                    if (!made.TryGetValue(matName, out var m))
                    {
                        m = new Material(shader) { name = $"{outfit}_{matName}" };
                        m.SetTexture("_MainTex", mask);
                        m.SetTexture("_Palette", palette);
                        AssetDatabase.CreateAsset(m, $"{Root}/Prefabs/{m.name}.mat");
                        made[matName] = m;
                    }
                    shared[i] = m;
                    mats++;
                }
                r.sharedMaterials = shared;
                r.updateWhenOffscreen = false;
                // The bind pose lies down (Source armature space); the clips stand it up. Bounds around the pelvis.
                r.localBounds = new Bounds(Vector3.zero, Vector3.one * 2.4f);
            }
            Debug.Log($"S4D infected {outfit}: {mats} materials, {missing} without texture, renderers [{string.Join(", ", go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.name))}]");
            string prefab = $"{Root}/Prefabs/{go.name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefab);
            UnityEngine.Object.DestroyImmediate(go);
            AssetImporter.GetAtPath(prefab).SetAssetBundleNameAndVariant(Bundle, "");
        }

        Directory.CreateDirectory(outDir);
        var manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Debug.Log("S4D built bundles: " + string.Join(", ", manifest.GetAllAssetBundles()));
    }

    static AnimatorController BuildController(Avatar avatar)
    {
        string fbx = $"{Root}/infected_anim/common_anims.fbx";
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            var st = States.FirstOrDefault(s => c.takeName.EndsWith(s.action) || c.name.EndsWith(s.action));
            c.loopTime = st.loop;
            // In place already: the SMD bake pins the pelvis horizontally (blender_export.py), no root motion here.
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        var all = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
        Debug.Log($"S4D infected clips: {string.Join(", ", all.Select(c => $"{c.name} {c.length:F2}s"))}");

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath($"{Root}/Prefabs/Infected.controller");
        var sm = ctrl.layers[0].stateMachine;
        foreach (var (state, action, _) in States)
        {
            var clip = all.FirstOrDefault(c => c.name.EndsWith(action));
            if (clip == null) { Debug.LogWarning($"S4D infected: no clip for {state} ({action})"); continue; }
            var s = sm.AddState(state);
            s.motion = clip;
            if (state == "Run") sm.defaultState = s;
        }
        return ctrl;
    }

    static Texture2D Texture(string path, bool srgb)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return null;
        ti.sRGBTexture = srgb;                       // the mask channels are data, the palette is colour
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = !srgb;
        ti.textureCompression = srgb ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
            string dst = Path.Combine(to, f.Substring(from.Length).TrimStart('\\', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(f, dst, true);
        }
    }
}

public static class S4DInfectedProbe
{
    // Unity.exe -batchmode -quit -projectPath unity/S4DBundles -executeMethod S4DInfectedProbe.Run
    // Bind pose and animated pose of one infected prefab: where do hips, head and feet end up?
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/L4D2/Infected/Prefabs/Infected_common_male_tshirt_cargos.prefab");
        var go = UnityEngine.Object.Instantiate(prefab);
        var a = go.GetComponent<Animator>();
        Report("bind", a);
        var ctrl = a.runtimeAnimatorController;
        foreach (var clip in ctrl.animationClips)
            foreach (var t in new[] { 0f, 0.5f, 0.99f })
            {
                clip.SampleAnimation(go, clip.length * t);
                Report($"{clip.name.Split('|').Last()}@{t:F2}", a);
            }
        var rs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
        Debug.Log($"S4D probe root children: {string.Join(", ", Enumerable.Range(0, go.transform.childCount).Select(i => go.transform.GetChild(i).name + " pos " + go.transform.GetChild(i).localPosition + " rot " + go.transform.GetChild(i).localEulerAngles + " scale " + go.transform.GetChild(i).localScale))}");
        Debug.Log($"S4D probe renderer0 bounds {rs[0].bounds} rootBone {rs[0].rootBone?.name}");
    }

    static void Report(string label, Animator a)
    {
        Vector3 P(string bone) { var t = a.GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name == "ValveBiped." + bone); return t ? t.position : Vector3.negativeInfinity; }
        Debug.Log($"S4D probe {label}: hips {P("Bip01_Pelvis"):F2} head {P("Bip01_Head1"):F2} lfoot {P("Bip01_L_Foot"):F2}");
    }
}

public static class S4DInfectedMaterials
{
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/L4D2/Infected/Prefabs/Infected_common_male_polo_jeans.prefab");
        foreach (var r in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Debug.Log($"S4D mats {r.name}: mesh submeshes {r.sharedMesh.subMeshCount}, materials [{string.Join(", ", r.sharedMaterials.Select(m => m == null ? "null" : m.name + "/" + m.shader.name + " tex " + (m.GetTexture("_MainTex") ? m.GetTexture("_MainTex").name : "none")))}]");
    }
}
