using System.Collections.Generic;
using Straft4Dead.Zombies;
using UnityEngine;

namespace Straft4Dead.Survivors
{
    /// <summary>L4D2 health rules on top of STRAFTAT's single health value (Phase 2).
    ///
    /// STRAFTAT keeps one synced float (4 units = 100 HP); we split it into permanent and temporary (pill) health and
    /// keep the game's value equal to their sum, so every STRAFTAT system (guns, HUD, death) still works. Damage eats
    /// temporary health first, as in L4D2. Temporary health decays at 0.27 HP/s. Pills add 50 temporary health; a
    /// medkit, held for 5 s, restores 80% of missing health and clears temporary health. At 0 HP the survivor is
    /// incapacitated for a few seconds, then the game's own death runs and the take ends (HealthPatches holds the
    /// health above zero meanwhile). Host-authoritative; single-player only until Phase 8.</summary>
    internal sealed class SurvivorHealth : MonoBehaviour
    {
        internal static readonly List<SurvivorHealth> All = new List<SurvivorHealth>();

        internal PlayerHealth Player { get; private set; }
        internal float Permanent { get; private set; }
        internal float Temporary { get; private set; }
        internal float Max => 100f * Plugin.Settings.PlayerHealthMultiplier.Value;
        internal int Medkits { get; private set; }
        internal int Pills { get; private set; }
        internal bool Incapacitated { get; private set; }
        internal float IncapProgress => Incapacitated ? Mathf.Clamp01((Time.time - _incapStart) / Plugin.Settings.IncapSeconds.Value) : 0f;
        internal float HealProgress => _healStart < 0f ? 0f : Mathf.Clamp01((Time.time - _healStart) / Plugin.Settings.MedkitSeconds.Value);

        /// <summary>Set while we write health ourselves, so HealthPatches doesn't treat it as damage or incap.</summary>
        internal static bool Writing;
        /// <summary>Set when the incap is over and the game's death should run.</summary>
        internal bool Finishing { get; private set; }

        private uint _generation = uint.MaxValue;
        private float _lastGameHp;
        private float _incapStart;
        private float _healStart = -1f;

        internal static SurvivorHealth For(PlayerHealth p)
        {
            if (p == null) return null;
            var s = p.GetComponent<SurvivorHealth>();
            if (s == null)
            {
                s = p.gameObject.AddComponent<SurvivorHealth>();
                s.Player = p;
            }
            return s;
        }

        internal static SurvivorHealth Local
        {
            get
            {
                foreach (var s in All) if (s != null && s.Player != null && s.Player.IsOwner) return s;
                return null;
            }
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            if (Player == null || !GameAccess.IsServer)
                return;

            uint gen = Player.sync___get_value_spawnGeneration();
            if (gen != _generation)
                OnSpawn(gen);
            if (Incapacitated)
            {
                if (Time.time - _incapStart >= Plugin.Settings.IncapSeconds.Value)
                    FinishIncap();
                return;
            }

            // Reconcile with the game: whatever changed the synced value since last frame was damage (or a heal).
            float game = GameAccess.ToL4D2(Player.sync___get_value_health());
            float delta = game - _lastGameHp;
            if (delta < -0.001f)
            {
                float dmg = -delta;
                float fromTemp = Mathf.Min(Temporary, dmg);
                Temporary -= fromTemp;
                Permanent = Mathf.Max(0f, Permanent - (dmg - fromTemp));
                _healStart = -1f; // taking damage interrupts healing
                SurvivorVoice.Hurt(this, dmg);
                Zombies.ZombieDirector.OnSurvivorHurt(dmg);
            }
            else if (delta > 0.001f)
                Permanent = Mathf.Min(Max, Permanent + delta);

            if (Temporary > 0f)
                Temporary = Mathf.Max(0f, Temporary - Plugin.Settings.TempHealthDecay.Value * Time.deltaTime);

            if (_healStart >= 0f && HealProgress >= 1f)
            {
                Permanent = Mathf.Min(Max, Permanent + (Max - Permanent) * 0.8f);
                Temporary = 0f;
                Medkits--;
                _healStart = -1f;
                Plugin.Logger.LogInfo($"[health] medkit used: {Permanent:F0} HP");
            }
            Push();
        }

        private void OnSpawn(uint gen)
        {
            _generation = gen;
            Incapacitated = false;
            Finishing = false;
            _healStart = -1f;
            float spawned = GameAccess.ToL4D2(Player.sync___get_value_health());
            Permanent = Max; // [Players] HealthMultiplier: 100 HP x multiplier
            Temporary = 0f;
            Medkits = Plugin.Settings.StartMedkits.Value;
            Pills = Plugin.Settings.StartPills.Value;
            _lastGameHp = spawned;
            Push();
            Plugin.Logger.LogInfo($"[health] {Player.transform.root.name} spawned: game {spawned:F0} HP -> {Permanent:F0} HP, {Medkits} medkit, {Pills} pills");
        }

        /// <summary>Write perm + temp back into the game's synced health.</summary>
        private void Push()
        {
            float total = Mathf.Max(Incapacitated ? 0.01f : 0f, Permanent + Temporary);
            if (Mathf.Abs(total - _lastGameHp) > 0.001f)
            {
                Writing = true;
                try { Player.sync___set_value_health(GameAccess.ToGame(total), true); }
                finally { Writing = false; }
            }
            _lastGameHp = total;
        }

        internal bool TakePills()
        {
            if (Incapacitated || Pills <= 0 || Permanent + Temporary >= Max - 0.5f)
                return false;
            Pills--;
            Temporary = Mathf.Min(Max - Permanent, Temporary + 50f);
            Push();
            SurvivorVoice.Say(this, "pills", interrupt: true);
            Plugin.Logger.LogInfo($"[health] pills: {Permanent:F0} + {Temporary:F0} temp");
            return true;
        }

        internal void SetHealing(bool holding)
        {
            if (!holding || Incapacitated || Medkits <= 0 || Permanent >= Max - 0.5f)
                _healStart = -1f;
            else if (_healStart < 0f)
            {
                _healStart = Time.time;
                SurvivorVoice.Say(this, "firstaid", interrupt: true);
            }
        }

        /// <summary>Called by HealthPatches when a write would take health to zero or below.</summary>
        internal void BeginIncap()
        {
            if (Incapacitated)
                return;
            Incapacitated = true;
            _incapStart = Time.time;
            _healStart = -1f;
            Permanent = 0f;
            Temporary = 0f;
            _lastGameHp = 0.01f;
            SurvivorVoice.Say(this, "incap", interrupt: true);
            Zombies.ZombieDirector.OnSurvivorIncap();
            Plugin.Logger.LogInfo($"[health] {Player.transform.root.name} incapacitated for {Plugin.Settings.IncapSeconds.Value:F1} s");
        }

        private void FinishIncap()
        {
            Finishing = true;
            SurvivorVoice.Say(this, "death", interrupt: true);
            Writing = true;
            try { Player.sync___set_value_health(0f, true); } // PlayerHealth.Update now runs the normal death: the take ends
            finally { Writing = false; }
            Incapacitated = false;
            Plugin.Logger.LogInfo($"[health] {Player.transform.root.name} bled out");
        }
    }
}
