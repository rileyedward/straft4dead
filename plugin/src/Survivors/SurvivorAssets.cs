using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Straft4Dead.Survivors
{
    /// <summary>Phase 4 content from the player's own L4D2 install: the s4d_survivors bundle (bodies and first-person
    /// arms, built by tools/pipeline/build_survivors.py + unity/S4DBundles). Optional: without it players keep
    /// STRAFTAT's character.</summary>
    internal sealed class SurvivorAssets : MonoBehaviour
    {
        internal static SurvivorAssets Instance;
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        internal bool Loaded { get; private set; }

        private void Awake()
        {
            Instance = this;
            StartCoroutine(Load());
        }

        private IEnumerator Load()
        {
            string path = Plugin.Settings.SurvivorBundle.Value;
            if (File.Exists(path))
            {
                var req = AssetBundle.LoadFromFileAsync(path);
                yield return req;
                if (req.assetBundle != null)
                    foreach (var go in req.assetBundle.LoadAllAssets<GameObject>())
                        _prefabs[go.name] = go;
            }
            Loaded = true;
            Plugin.Logger.LogInfo($"[survivors] {_prefabs.Count} prefabs from '{path}': {string.Join(", ", _prefabs.Keys.OrderBy(k => k))}");
        }

        /// <summary>Survivor_&lt;name&gt;_Arms or _Body; null when the bundle doesn't have it.</summary>
        internal GameObject Prefab(string survivor, string part)
        {
            return _prefabs.TryGetValue($"Survivor_{survivor}_{part}", out var p) ? p : null;
        }
    }
}
