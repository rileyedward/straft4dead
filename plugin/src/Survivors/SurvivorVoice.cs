using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Straft4Dead.Survivors
{
    /// <summary>L4D2 survivor voice lines for the local survivor, loaded from the L4D2 install's loose sound files
    /// (sound/player/survivor/voice/&lt;survivor&gt;). Lines are grouped by the file-name prefixes L4D2 uses for each
    /// concept; one line at a time, with a cooldown so the survivor doesn't chatter.</summary>
    internal sealed class SurvivorVoice : MonoBehaviour
    {
        internal static SurvivorVoice Instance;

        // Our event -> L4D2 file-name prefixes (the number suffix is the variant).
        private static readonly Dictionary<string, string[]> Concepts = new Dictionary<string, string[]>
        {
            { "hurt_minor", new[] { "hurtminor" } },
            { "hurt_major", new[] { "hurtmajor" } },
            { "hurt_critical", new[] { "hurtcritical" } },
            { "incap", new[] { "incapacitatedinjury" } },
            { "help", new[] { "help" } },
            { "death", new[] { "deathscream" } },
            { "pills", new[] { "painrelieftpills" } },
            { "firstaid", new[] { "painrelieftfirstaid" } },
            { "kill", new[] { "killconfirmation" } },
            { "laugh", new[] { "laughter", "hurrah" } },
            { "incoming", new[] { "incoming" } },
            { "biled", new[] { "boomerreaction" } },
            { "warn_tank", new[] { "warntank" } },
            { "warn_boomer", new[] { "warnboomer" } },
            { "warn_witch", new[] { "warnwitch" } },
        };

        private readonly Dictionary<string, List<AudioClip>> _lines = new Dictionary<string, List<AudioClip>>();
        private AudioSource _source;
        private float _busyUntil;
        private int _killsSinceLine;

        private void Awake()
        {
            Instance = this;
            _source = gameObject.AddComponent<AudioSource>();
            _source.spatialBlend = 0f; // your own voice: 2D
            _source.volume = 0.9f;
            if (Plugin.Settings.SurvivorVoice.Value)
                StartCoroutine(Load(Plugin.Settings.SurvivorName.Value));
        }

        private IEnumerator Load(string survivor)
        {
            string dir = Path.Combine(Plugin.Settings.L4D2SoundDir.Value, "player", "survivor", "voice", survivor);
            if (!Directory.Exists(dir))
            {
                Plugin.Logger.LogWarning($"[voice] no voice folder at {dir}");
                yield break;
            }
            var files = Directory.GetFiles(dir, "*.wav");
            foreach (var c in Concepts)
            {
                var list = new List<AudioClip>();
                foreach (var f in files.Where(f => c.Value.Any(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(f).ToLowerInvariant(), "^" + p + @"\d+$"))))
                    using (var r = UnityWebRequestMultimedia.GetAudioClip("file:///" + f.Replace('\\', '/'), AudioType.WAV))
                    {
                        yield return r.SendWebRequest();
                        if (r.result == UnityWebRequest.Result.Success)
                            list.Add(DownloadHandlerAudioClip.GetContent(r));
                    }
                _lines[c.Key] = list;
            }
            Plugin.Logger.LogInfo($"[voice] {survivor}: {string.Join(", ", _lines.Select(kv => kv.Key + "=" + kv.Value.Count))}");
        }

        /// <summary>Play a line for this concept if the survivor is free (or interrupt when it matters).</summary>
        internal static void Say(SurvivorHealth who, string concept, bool interrupt = false, float chance = 1f)
        {
            var v = Instance;
            if (v == null || !Plugin.Settings.SurvivorVoice.Value || who == null || who != SurvivorHealth.Local) return;
            if ((!interrupt && Time.time < v._busyUntil) || Random.value > chance) return;
            if (!v._lines.TryGetValue(concept, out var list) || list.Count == 0) return;
            var clip = list[Random.Range(0, list.Count)];
            v._source.Stop();
            v._source.clip = clip;
            v._source.Play();
            v._busyUntil = Time.time + clip.length + 1.5f;
            Plugin.Logger.LogInfo($"[voice] {concept}: {clip.name} ({clip.length:F1} s)");
        }

        internal static void Hurt(SurvivorHealth who, float damage)
        {
            float hp = who.Permanent + who.Temporary;
            string concept = hp < 25f ? "hurt_critical" : damage >= 20f ? "hurt_major" : "hurt_minor";
            Say(who, concept, chance: damage >= 10f ? 1f : 0.5f);
        }

        /// <summary>Every few common kills the survivor confirms one (or laughs on a streak).</summary>
        internal static void Kill()
        {
            if (Instance == null) return;
            if (++Instance._killsSinceLine < Random.Range(4, 8)) return;
            Instance._killsSinceLine = 0;
            Say(SurvivorHealth.Local, Random.value < 0.75f ? "kill" : "laugh");
        }
    }
}
