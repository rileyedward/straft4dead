using FishNet;
using UnityEngine;

namespace Straft4Dead.Zombies
{
    /// <summary>Everything that reads or writes STRAFTAT's player health, in one place (systems-map §4).
    /// STRAFTAT health is a FishNet SyncVar where a full bar is <see cref="FullHealth"/> units, shown as "100".</summary>
    internal static class GameAccess
    {
        /// <summary>STRAFTAT units per full health bar. The minimalist HUD prints Ceil(health / 4 * 100), so 4.</summary>
        internal const float FullHealth = 4f;

        internal static float ToGame(float l4d2Hp) => l4d2Hp / 100f * FullHealth;
        internal static float ToL4D2(float gameUnits) => gameUnits / FullHealth * 100f;

        internal static bool IsServer => InstanceFinder.IsServer;

        internal static bool IsAlive(PlayerHealth p) =>
            p != null && p.isActiveAndEnabled && !p.sync___get_value_isKilled() && p.sync___get_value_health() > 0f;

        /// <summary>Server-side damage through the game's own RemoveHealth logic (syncs health, plays hit feedback).
        /// Death and the end of the take then run through PlayerHealth.Update as for any other death.</summary>
        internal static void DamagePlayer(PlayerHealth p, float l4d2Hp, string source)
        {
            if (!IsServer || !IsAlive(p))
                return;
            float before = p.sync___get_value_health();
            p.RpcLogic___RemoveHealth_431000436(ToGame(l4d2Hp));
            Plugin.Logger.LogInfo($"[zombies] {source} hit {p.transform.root.name} for {l4d2Hp:F0} HP: {ToL4D2(before):F0} -> {ToL4D2(p.sync___get_value_health()):F0}");
        }

        /// <summary>Scales a freshly spawned player's health (server only). Returns the new value in L4D2 HP.</summary>
        internal static float ScaleHealth(PlayerHealth p, float multiplier)
        {
            float h = p.sync___get_value_health() * multiplier;
            p.sync___set_value_health(h, true);
            return ToL4D2(h);
        }
    }
}
