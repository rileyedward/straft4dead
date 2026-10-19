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
        internal static bool InLobby => InstanceFinder.IsClient;

        internal static bool IsAlive(PlayerHealth p) =>
            p != null && p.isActiveAndEnabled && !p.sync___get_value_isKilled() && p.sync___get_value_health() > 0f &&
            !(p.GetComponent<Survivors.SurvivorHealth>()?.Incapacitated ?? false);

        /// <summary>Server-side damage through the game's own RemoveHealth logic (syncs health, plays hit feedback).
        /// Death and the end of the take then run through PlayerHealth.Update as for any other death.</summary>
        internal static void DamagePlayer(PlayerHealth p, float l4d2Hp, string source)
        {
            if (!IsServer || !IsAlive(p))
                return;
            float before = p.sync___get_value_health();
            // Write the synced value directly rather than RemoveHealth: that also fires STRAFTAT's PvP hit feedback
            // (a full-screen red flash), which a horde clawing at you every second turns into a solid red screen.
            // A write that reaches zero still goes through HealthPatches (incap) and the game's death.
            p.sync___set_value_health(before - ToGame(l4d2Hp), true);
            Plugin.Logger.LogInfo($"[zombies] {source} hit {p.transform.root.name} for {l4d2Hp:F0} HP: {ToL4D2(before):F0} -> {ToL4D2(p.sync___get_value_health()):F0}");
        }

    }
}
