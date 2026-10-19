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
            { "revive", new[] { "revivefriend", "revivefrienda", "revivefriendb" } },
            { "thanks", new[] { "thanks" } },
            { "warn_tank", new[] { "warntank" } },
            { "warn_boomer", new[] { "warnboomer" } },
            { "warn_witch", new[] { "warnwitch" } },
        };

        private readonly Dictionary<string, List<AudioClip>> _lines = new Dictionary<string, List<AudioClip>>();
        private AudioSource _source;
        private float _busyUntil;
        private string _loadedFor;

        private void Awake()
        {
            Instance = this;
            _source = gameObject.AddComponent<AudioSource>();
            _source.spatialBlend = 0f; // your own voice: 2D
            _source.volume = 0.9f;
            EnsureLoaded();
        }

        /// <summary>(Re)load the lines when voice is on and the survivor changed (host panel).</summary>
        private void EnsureLoaded()
        {
            string who = Plugin.Settings.SurvivorName.Value;
            if (!Plugin.Settings.SurvivorVoice.Value || _loadedFor == who) return;
            _loadedFor = who;
            _lines.Clear();
            StartCoroutine(Load(who));
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

        /// <summary>A line for this survivor's concept. On the host, a survivor whose game runs elsewhere gets it sent
        /// to their client (Phase 8); everything else plays here if the local survivor is free (or interrupts).</summary>
        internal static void Say(SurvivorHealth who, string concept, bool interrupt = false, float chance = 1f)
        {
            if (Instance == null || who == null || !Plugin.Settings.SurvivorVoice.Value || !Plugin.Settings.Enabled.Value) return;
            if (Random.value > chance) return;
            if (who != SurvivorHealth.Local)
            {
                Net.NetSync.VoiceForRemote(who.Player, concept, interrupt);
                return;
            }
            PlayLocal(concept, interrupt);
        }

        /// <summary>Every survivor says it (the horde is coming, a Tank is here).</summary>
        internal static void SayEveryone(string concept, bool interrupt = false, float chance = 1f)
        {
            foreach (var s in SurvivorHealth.All.ToArray())
                Say(s, concept, interrupt, chance);
        }

        /// <summary>Play one of the local survivor's lines for this concept.</summary>
        internal static void PlayLocal(string concept, bool interrupt)
        {
            var v = Instance;
            if (v == null || !Plugin.Settings.SurvivorVoice.Value || !Plugin.Settings.Enabled.Value) return;
            v.EnsureLoaded();
            if (!interrupt && Time.time < v._busyUntil) return;
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

        private static readonly Dictionary<PlayerHealth, int> KillsSinceLine = new Dictionary<PlayerHealth, int>();

        /// <summary>Every few kills a survivor confirms one (or laughs on a streak).</summary>
        internal static void KillBy(PlayerHealth killer)
        {
            if (killer == null) return;
            KillsSinceLine.TryGetValue(killer, out int n);
            if (++n < Random.Range(4, 8)) { KillsSinceLine[killer] = n; return; }
            KillsSinceLine[killer] = 0;
            Say(SurvivorHealth.For(killer), Random.value < 0.75f ? "kill" : "laugh");
        }
    }
}
