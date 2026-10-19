// Phase 4 builder: L4D2 survivor bodies (humanoid) and first-person arms (generic) -> AssetBundle "s4d_survivors".
//
//   Unity.exe -batchmode -quit -projectPath unity/S4DBundles -executeMethod S4DSurvivorBuilder.Build
//       -s4dSource <straft4dead-research\fbx> -s4dOut <bundle folder>
//
// Input comes from tools/pipeline/build_survivors.py: survivor_<name>/{body.fbx, arms.fbx, textures/<material>.png}.
// Prefabs: Survivor_<name>_Body (humanoid avatar via S4DBundleBuilder.ValveBipedHuman) and Survivor_<name>_Arms.
// Build without -nographics, like the infected bundle.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class S4DSurvivorBuilder
{
    const string Root = "Assets/L4D2/Survivors";
    const string Bundle = "s4d_survivors";

    public static void Build()
    {
        string src = Arg("-s4dSource"), outDir = Arg("-s4dOut");
        var survivors = Directory.GetDirectories(src, "survivor_*").Select(Path.GetFileName)
            .Where(d => File.Exists(Path.Combine(src, d, "body.fbx"))).ToList();
        Directory.CreateDirectory(Root + "/Prefabs");
        foreach (var d in survivors)
        {
            // Only the FBX and our material-named PNGs; SourceIO's own image dumps stay out of the project.
            Directory.CreateDirectory($"{Root}/{d}/textures");
            foreach (var f in new[] { "body.fbx", "arms.fbx" })
                if (File.Exists(Path.Combine(src, d, f))) File.Copy(Path.Combine(src, d, f), $"{Root}/{d}/{f}", true);
            foreach (var png in Directory.GetFiles(Path.Combine(src, d, "textures"), "*.png"))
                File.Copy(png, $"{Root}/{d}/textures/{Path.GetFileName(png)}", true);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        foreach (var d in survivors)
        {
            string name = d.Substring("survivor_".Length);
            var made = new Dictionary<string, Material>();
            MakePrefab(d, name, "body", true, made);
            if (File.Exists($"{Root}/{d}/arms.fbx"))
                MakePrefab(d, name, "arms", false, made);
        }
        Directory.CreateDirectory(outDir);
        var manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Debug.Log("S4D built bundles: " + string.Join(", ", manifest.GetAllAssetBundles()));
    }

    static void MakePrefab(string dir, string survivor, string part, bool human, Dictionary<string, Material> made)
    {
        string fbx = $"{Root}/{dir}/{part}.fbx";
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
        imp.animationType = human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        if (human)
            imp.humanDescription = S4DBundleBuilder.ValveBipedHuman(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; // only to read material names
        imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
        imp.importAnimation = false;
        imp.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"S4D survivor {survivor} {part}: avatar valid={avatar && avatar.isValid} human={avatar && avatar.isHuman}");

        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
        go.name = $"Survivor_{survivor}_{(human ? "Body" : "Arms")}";
        var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
        anim.avatar = avatar;
        anim.applyRootMotion = false;

        var pngs = Directory.GetFiles($"{Root}/{dir}/textures", "*.png").Select(Path.GetFileNameWithoutExtension).ToList();
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                string full = shared[i] != null ? shared[i].name : r.name;
                string tex = pngs.Where(n => full.EndsWith(n, StringComparison.OrdinalIgnoreCase)).OrderByDescending(n => n.Length).FirstOrDefault();
                if (tex == null)
                {
                    Debug.Log($"S4D survivor {survivor} {part}: no texture for material '{full}' on {r.name}");
                    shared[i] = null;
                    continue;
                }
                if (!made.TryGetValue(tex, out var m))
                {
                    m = new Material(Shader.Find("Standard")) { name = $"{survivor}_{tex}" };
                    m.SetTexture("_MainTex", Texture($"{Root}/{dir}/textures/{tex}.png"));
                    m.SetFloat("_Glossiness", 0.2f);
                    if (tex.Contains("hair"))
                    {
                        // L4D2 hair is $translucent; alpha test keeps it sorting-safe.
                        m.SetFloat("_Mode", 1f);
                        m.SetFloat("_Cutoff", 0.4f);
                        m.EnableKeyword("_ALPHATEST_ON");
                        m.SetOverrideTag("RenderType", "TransparentCutout");
                        m.renderQueue = 2450;
                    }
                    AssetDatabase.CreateAsset(m, $"{Root}/Prefabs/{m.name}.mat");
                    made[tex] = m;
                }
                shared[i] = m;
            }
            r.sharedMaterials = shared;
        }
        Debug.Log($"S4D survivor {survivor} {part}: renderers [{string.Join(", ", go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.name + ":" + string.Join("/", r.sharedMaterials.Select(m => m ? m.name : "null"))))}]");
        string prefab = $"{Root}/Prefabs/{go.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, prefab);
        UnityEngine.Object.DestroyImmediate(go);
        AssetImporter.GetAtPath(prefab).SetAssetBundleNameAndVariant(Bundle, "");
    }

    static Texture2D Texture(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return null;
        ti.sRGBTexture = true;
        ti.alphaIsTransparency = path.Contains("hair");
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
}
