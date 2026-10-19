using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Straft4Dead.Zombies
{
    /// <summary>Named groups of L4D2 sounds loaded from the install's loose sound folder ([HUD] L4D2SoundDir), e.g.
    /// "tank_yell" = player/tank/voice/yell/tank_yell_*.wav. Loads in the background on first use of a bank; a
    /// missing file or folder just means silence.</summary>
    internal sealed class SoundBank : MonoBehaviour
    {
        internal static SoundBank Instance;
        private readonly Dictionary<string, List<AudioClip>> _clips = new Dictionary<string, List<AudioClip>>();
        private readonly HashSet<string> _requested = new HashSet<string>();

        // name -> (folder under sound/, file-name prefix)
        private static readonly Dictionary<string, (string dir, string prefix)> Groups = new Dictionary<string, (string, string)>
        {
            { "tank_yell", ("player/tank/voice/yell", "tank_yell_") }, { "tank_throw", ("player/tank/voice/yell", "tank_throw_") },
            { "tank_attack", ("player/tank/voice/attack", "") }, { "tank_growl", ("player/tank/voice/idle", "tank_growl_") },
            { "tank_pain", ("player/tank/voice/pain", "tank_pain_") }, { "tank_die", ("player/tank/voice/die", "") },
            { "tank_punch", ("player/tank/hit", "hulk_punch_") }, { "tank_rock_hit", ("player/tank/hit", "thrown_projectile_hit_") },
            { "tank_rip_rock", ("player/tank/attack", "rip_up_rock_") }, { "tank_music", ("music/tank", "") },
            { "boomer_lurk", ("player/boomer/voice/idle", "male_boomer_lurk_") }, { "boomer_alert", ("player/boomer/voice/alert", "male_boomer_alert_") },
            { "boomer_vomit", ("player/boomer/voice/vomit", "female_boomer_vomit_") }, { "boomer_vomit_fx", ("player/boomer/vomit/attack", "") },
            { "boomer_explode", ("player/boomer/explode", "explo_medium_") }, { "boomer_pain", ("player/boomer/voice/pain", "male_boomer_pain_") },
            { "witch_cry", ("npc/witch/voice/idle", "female_cry_") }, { "witch_mad", ("npc/witch/voice/mad", "female_ls_d_madscream") },
            { "witch_attack", ("npc/witch/voice/attack", "female_shriek_") }, { "witch_die", ("npc/witch/voice/die", "") },
            { "witch_pain", ("npc/witch/voice/pain", "witch_pain_") }, { "witch_music", ("music/witch", "lost_little_witch_") },
            { "witch_rage_music", ("music/witch", "psychowitch") },
        };

        private void Awake() => Instance = this;

        /// <summary>Start loading these groups (call when a special is about to appear).</summary>
        internal static void Preload(params string[] names)
        {
            if (Instance == null) return;
            foreach (var n in names)
                if (Instance._requested.Add(n)) Instance.StartCoroutine(Instance.Load(n));
        }

        internal static AudioClip Get(string name)
        {
            if (Instance == null) return null;
            Preload(name);
            return Instance._clips.TryGetValue(name, out var l) && l.Count > 0 ? l[Random.Range(0, l.Count)] : null;
        }

        private IEnumerator Load(string name)
        {
            if (!Groups.TryGetValue(name, out var g)) yield break;
            string dir = Path.Combine(Plugin.Settings.L4D2SoundDir.Value, g.dir.Replace('/', Path.DirectorySeparatorChar));
            var list = new List<AudioClip>();
            _clips[name] = list;
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*.wav", SearchOption.AllDirectories).Where(f => Path.GetFileName(f).ToLowerInvariant().StartsWith(g.prefix.ToLowerInvariant())))
                using (var r = UnityWebRequestMultimedia.GetAudioClip("file:///" + f.Replace('\\', '/'), AudioType.WAV))
                {
                    yield return r.SendWebRequest();
                    if (r.result == UnityWebRequest.Result.Success) list.Add(DownloadHandlerAudioClip.GetContent(r));
                }
        }
    }
}
