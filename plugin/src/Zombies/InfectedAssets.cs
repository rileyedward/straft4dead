using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Straft4Dead.Zombies
{
    /// <summary>Phase 3 content, all from the player's own L4D2 install: the s4d_infected bundle (outfits with their
    /// animator) built by tools/pipeline/build_infected.py + unity/S4DBundles, and the infected sound folders, loaded
    /// on demand. Everything is optional: without it zombies stay Phase 1 capsules.</summary>
    internal sealed class InfectedAssets : MonoBehaviour
    {
        internal static InfectedAssets Instance;
        internal readonly List<GameObject> Outfits = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _specials = new Dictionary<string, GameObject>();

        /// <summary>Special_&lt;model&gt; from the s4d_specials bundle (hulk, boomer, witch), or null.</summary>
        internal GameObject Special(string model) => _specials.TryGetValue("Special_" + model, out var g) ? g : null;
        private readonly Dictionary<string, List<AudioClip>> _sounds = new Dictionary<string, List<AudioClip>>();

        // Sound category -> folders under <L4D2>/sound/npc/infected. Files with these words are special infected
        // variants (CEDA hazmat, mud men, clowns) and stay out of the common pool.
        private static readonly Dictionary<string, string[]> Categories = new Dictionary<string, string[]>
        {
            { "alert", new[] { "alert/becomeenraged", "alert/alert" } },
            { "rage_male", new[] { "action/rage/male" } },
            { "rage_female", new[] { "action/rage/female" } },
            { "idle", new[] { "idle/moaning", "idle/breathing", "idle/mumbling" } },
            { "hit_player", new[] { "hit" } },
            { "shot", new[] { "gore/bullets", "action/been_shot" } },
            { "die", new[] { "action/die" } },
        };
        private static readonly string[] Excluded = { "ceda", "mud", "clown", "jimmy", "riot", "fallen", "suit" };

        private void Awake()
        {
            Instance = this;
            StartCoroutine(Load());
        }

        private IEnumerator Load()
        {
            string bundlePath = Plugin.Settings.InfectedBundle.Value;
            if (File.Exists(bundlePath))
            {
                var req = AssetBundle.LoadFromFileAsync(bundlePath);
                yield return req;
                if (req.assetBundle != null)
                    Outfits.AddRange(req.assetBundle.LoadAllAssets<GameObject>().Where(g => g.name.StartsWith("Infected_")));
            }
            Plugin.Logger.LogInfo($"[infected] {Outfits.Count} outfits from '{bundlePath}': {string.Join(", ", Outfits.Select(o => o.name))}");
            string specialsPath = Plugin.Settings.SpecialsBundle.Value;
            if (File.Exists(specialsPath))
            {
                var sreq = AssetBundle.LoadFromFileAsync(specialsPath);
                yield return sreq;
                if (sreq.assetBundle != null)
                    foreach (var g in sreq.assetBundle.LoadAllAssets<GameObject>().Where(g => g.name.StartsWith("Special_")))
                        _specials[g.name] = g;
            }
            Plugin.Logger.LogInfo($"[infected] specials from '{specialsPath}': {string.Join(", ", _specials.Keys)}");

            string root = Path.Combine(Plugin.Settings.L4D2SoundDir.Value, "npc", "infected");
            int total = 0;
            foreach (var cat in Categories)
            {
                var list = new List<AudioClip>();
                foreach (var sub in cat.Value)
                {
                    string dir = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*.wav", SearchOption.AllDirectories))
                    {
                        string lower = f.ToLowerInvariant();
                        if (Excluded.Any(lower.Contains)) continue;
                        using (var r = UnityWebRequestMultimedia.GetAudioClip("file:///" + f.Replace('\\', '/'), AudioType.WAV))
                        {
                            yield return r.SendWebRequest();
                            if (r.result == UnityWebRequest.Result.Success)
                                list.Add(DownloadHandlerAudioClip.GetContent(r));
                        }
                    }
                }
                _sounds[cat.Key] = list;
                total += list.Count;
            }
            Plugin.Logger.LogInfo($"[infected] {total} sounds: {string.Join(", ", _sounds.Select(kv => kv.Key + "=" + kv.Value.Count))}");
        }

        internal AudioClip Sound(string category)
        {
            return _sounds.TryGetValue(category, out var l) && l.Count > 0 ? l[Random.Range(0, l.Count)] : null;
        }
    }
}
