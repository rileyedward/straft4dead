using HarmonyLib;
using Straft4Dead.Survivors;
using Straft4Dead.Zombies;

namespace Straft4Dead.Patches
{
    /// <summary>Incap instead of instant death (Phase 2). Every damage source in STRAFTAT ends in the FishNet-generated
    /// health setter; when a write would take a living player to zero or below, start the incap and hold health at a
    /// sliver so guns' own "health &lt;= 0 → kill" checks and PlayerHealth.Update's death branch don't fire yet.
    /// Large negative writes (-1000, -2000: void, suicide, the game's own post-death bookkeeping) pass through.</summary>
    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.sync___set_value_health))]
    internal static class HealthPatches
    {
        private const float Sliver = 0.01f;

        private static void Prefix(PlayerHealth __instance, ref float __0)
        {
            if (SurvivorHealth.Writing || !Plugin.Settings.IncapEnabled.Value || !Plugin.Settings.Enabled.Value || !GameAccess.IsServer)
                return;
            if (__0 > 0f || __0 <= -500f)
                return;
            var s = SurvivorHealth.For(__instance);
            if (s.Finishing || __instance.sync___get_value_isKilled())
                return;
            if (!s.Incapacitated)
            {
                if (__instance.sync___get_value_health() <= 0f)
                    return; // already dead (e.g. a write during the game's own death handling)
                s.BeginIncap();
            }
            __0 = Sliver;
        }
    }
}
