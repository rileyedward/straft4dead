using HarmonyLib;
using Straft4Dead.Zombies;
using UnityEngine;

namespace Straft4Dead.Patches
{
    /// <summary>Lets STRAFTAT weapons hurt zombies without making zombies look like players.</summary>
    [HarmonyPatch]
    internal static class WeaponPatches
    {
        /// <summary>Every hitscan gun (Gun, Shotgun, Minigun, LargeRaycastGun, BeamGun, ChargeGun) reports each
        /// environment hit through Weapon.TriggerEnvironment(hitObject, point, normal, direction) (systems-map §4).
        /// Zombies sit on the Default layer, so their colliders arrive here; turn those into zombie damage.</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Weapon), nameof(Weapon.TriggerEnvironment))]
        private static bool TriggerEnvironmentPrefix(Weapon __instance, GameObject __0, Vector3 __1, Vector3 __2, Vector3 __3)
        {
            if (__0 == null)
                return true;
            var zombie = __0.GetComponentInParent<Zombie>();
            if (zombie == null)
                return true;
            bool head = __0.name == Zombie.HeadName;
            float units = __instance.damage * (head ? __instance.headMultiplier : 1f);
            zombie.TakeDamage(GameAccess.ToL4D2(units) * Plugin.Settings.WeaponDamageToZombies.Value, __1, __3, head);
            return false; // not a prop, pig or glass: skip the game's environment handling
        }

        /// <summary>Melee weapons look for players only; add a short sweep in front of the camera for zombies.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MeleeWeapon), "Fire")] // private
        private static void MeleeFirePostfix(MeleeWeapon __instance)
        {
            Component cam = __instance.cam;
            if (cam == null || !__instance.IsOwner)
                return;
            var hits = Physics.SphereCastAll(cam.transform.position, 0.4f, cam.transform.forward, 2.6f, 1 << Zombie.Layer, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                var z = h.collider.GetComponentInParent<Zombie>();
                if (z == null || z.IsDead)
                    continue;
                bool head = h.collider.name == Zombie.HeadName;
                float units = __instance.damage * (head ? __instance.headMultiplier : 1f);
                z.TakeDamage(GameAccess.ToL4D2(units) * Plugin.Settings.WeaponDamageToZombies.Value, h.point, cam.transform.forward, head);
                break;
            }
        }
    }
}
