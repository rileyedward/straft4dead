using System.Collections.Generic;
using System.Linq;
using Straft4Dead.Zombies;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Straft4Dead.Survivors
{
    /// <summary>2v2 revives (Phase 8). In team games an incapped survivor bleeds out over [Players] TeamIncapSeconds
    /// instead of the short solo incap; a teammate standing over them holding [Players] ReviveKey for ReviveSeconds
    /// gets them back up with 30 temporary health, as in L4D2. Bleeding out pauses while being revived; taking hits
    /// while down still drains them. The host runs the revive; clients send "holding the key" and show prompts.</summary>
    internal sealed class Revives : MonoBehaviour
    {
        internal static Revives Instance;
        private const float Reach = 2.2f;

        /// <summary>Host: reviver → the incapped teammate they're working on.</summary>
        private static readonly Dictionary<PlayerHealth, SurvivorHealth> Reviving = new Dictionary<PlayerHealth, SurvivorHealth>();
        /// <summary>Everyone: who is down right now (FishNet owner id → revive progress), from the host.</summary>
        internal static readonly Dictionary<int, float> Downed = new Dictionary<int, float>();

        private bool _holding;
        internal static string Prompt { get; private set; }
        internal static float LocalReviveProgress { get; private set; }

        private void Awake() => Instance = this;

        /// <summary>Team id from STRAFTAT's ScoreManager (synced to everyone); -1 outside team games.</summary>
        internal static int TeamOf(PlayerHealth p)
        {
            if (p == null || GameManager.Instance == null || !GameManager.Instance.sync___get_value_playingTeams() || ScoreManager.Instance == null) return -1;
            var client = PlayerClient(p);
            if (client == null) return -1;
            return ScoreManager.Instance.PlayerIdToTeamId.TryGetValue(client.PlayerId, out int team) ? team : -1;
        }

        private static ClientInstance PlayerClient(PlayerHealth p)
        {
            var values = p.sync___get_value_playerValues();
            return values != null ? values.sync___get_value_playerClient() : null;
        }

        internal static bool AreTeammates(PlayerHealth a, PlayerHealth b)
        {
            if (a == null || b == null || a == b) return false;
            int ta = TeamOf(a);
            return ta >= 0 && ta == TeamOf(b);
        }

        internal static bool HasLivingTeammate(PlayerHealth p) =>
            FindObjectsOfType<PlayerHealth>().Any(o => AreTeammates(p, o) && GameAccess.IsAlive(o));

        /// <summary>Host: this player started or stopped holding the revive key.</summary>
        internal static void SetReviving(PlayerHealth reviver, bool holding)
        {
            if (reviver == null) return;
            if (!holding) { Stop(reviver); return; }
            if (Reviving.ContainsKey(reviver)) return;
            var target = FindObjectsOfType<PlayerHealth>()
                .Where(o => AreTeammates(reviver, o) && Vector3.Distance(o.transform.position, reviver.transform.position) < Reach)
                .Select(SurvivorHealth.For).FirstOrDefault(sh => sh != null && sh.Incapacitated);
            if (target == null || !GameAccess.IsAlive(reviver))
            {
                Plugin.Logger.LogInfo($"[revive] {reviver.transform.root.name}: nobody to revive (alive {GameAccess.IsAlive(reviver)}, downed nearby: " +
                    string.Join(", ", FindObjectsOfType<PlayerHealth>().Where(o => o != reviver).Select(o => $"{o.OwnerId} team {TeamOf(o)}/{TeamOf(reviver)} {Vector3.Distance(o.transform.position, reviver.transform.position):F1} m incap {SurvivorHealth.For(o).Incapacitated}")) + ")");
                return;
            }
            Reviving[reviver] = target;
            SurvivorVoice.Say(SurvivorHealth.For(reviver), "revive", interrupt: true);
            Plugin.Logger.LogInfo($"[revive] {reviver.transform.root.name} reviving {target.Player.transform.root.name}");
        }

        private static void Stop(PlayerHealth reviver)
        {
            if (Reviving.TryGetValue(reviver, out var t) && t != null) t.ReviveProgress = 0f;
            Reviving.Remove(reviver);
        }

        private void Update()
        {
            if (Net.NetSync.IsHost) HostUpdate();
            LocalInput();
        }

        private void HostUpdate()
        {
            Downed.Clear();
            foreach (var s in SurvivorHealth.All)
                if (s != null && s.Incapacitated && s.Player != null) Downed[s.Player.OwnerId] = s.ReviveProgress;
            Net.NetSync.SendDowned();

            foreach (var kv in Reviving.ToList())
            {
                var reviver = kv.Key; var target = kv.Value;
                bool ok = reviver != null && target != null && target.Incapacitated && GameAccess.IsAlive(reviver)
                          && Vector3.Distance(reviver.transform.position, target.Player.transform.position) < Reach + 0.5f;
                if (!ok) { Stop(reviver); continue; }
                target.ReviveProgress += Time.deltaTime / Mathf.Max(0.5f, Plugin.Settings.ReviveSeconds.Value);
                if (target.ReviveProgress >= 1f)
                {
                    target.Revive();
                    Reviving.Remove(reviver);
                }
            }
        }

        /// <summary>Every player: "hold E to revive" when standing over a downed teammate; send the key state.</summary>
        private void LocalInput()
        {
            Prompt = null;
            LocalReviveProgress = 0f;
            var me = SurvivorHealth.Local;
            if (me == null || me.Incapacitated || !Plugin.Settings.Enabled.Value) { SetHolding(false, me); return; }

            var downed = FindObjectsOfType<PlayerHealth>().FirstOrDefault(o =>
                Downed.ContainsKey(o.OwnerId) && AreTeammates(me.Player, o) &&
                Vector3.Distance(o.transform.position, me.Player.transform.position) < Reach);
            if (downed == null) { SetHolding(false, me); return; }

            var key = KeyOf(Plugin.Settings.ReviveKey.Value);
            bool holding = key != null && key.isPressed;
            SetHolding(holding, me);
            string name = PlayerClient(downed)?.PlayerNameTag ?? "your teammate";
            Prompt = holding ? $"Reviving {name}..." : $"Hold {Plugin.Settings.ReviveKey.Value.ToUpperInvariant()} to revive {name}";
            if (holding) LocalReviveProgress = Downed.TryGetValue(downed.OwnerId, out float p) ? p : 0f;
        }

        private void SetHolding(bool holding, SurvivorHealth me)
        {
            if (holding == _holding) return;
            _holding = holding;
            if (me == null) return;
            if (Net.NetSync.IsRemoteClient) Net.NetSync.SendRevive(holding);
            else SetReviving(me.Player, holding);
        }

        private static KeyControl KeyOf(string name)
        {
            var kb = Keyboard.current;
            if (kb == null || string.IsNullOrEmpty(name)) return null;
            if (name.Length == 1 && char.IsDigit(name[0])) name = "Digit" + name;
            return System.Enum.TryParse<Key>(name, true, out var key) ? kb[key] : null;
        }
    }
}
