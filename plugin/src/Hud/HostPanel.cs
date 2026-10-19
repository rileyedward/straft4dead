using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Straft4Dead.Hud
{
    /// <summary>Phase 7 in-game settings panel ([General] PanelKey, F9 by default): the Straft 4 Dead mode switch, your
    /// survivor, and the host's zombie settings (horde and wave sizes, wanderers, max alive, wave timing, map-size
    /// scaling, specials). Every control writes the BepInEx config entry, so changes apply at once and are saved.
    /// Zombie settings are the host's: in Phase 8 the host's values are the ones a lobby plays with.</summary>
    internal sealed class HostPanel : MonoBehaviour
    {
        private static readonly (string id, string name)[] SurvivorChoices =
            { ("mechanic", "Ellis"), ("gambler", "Nick"), ("producer", "Rochelle"), ("coach", "Coach") };

        private bool _open;
        private Rect _rect = new Rect(40, 40, 430, 640);
        private Vector2 _scroll;
        private CursorLockMode _prevLock;
        private bool _prevVisible;
        private GUIStyle _header;

        private void Update()
        {
            var kb = Keyboard.current;
            var key = ParseKey(Plugin.Settings.PanelKey.Value);
            if (kb == null || key == Key.None || !kb[key].wasPressedThisFrame) return;
            _open = !_open;
            if (_open) { _prevLock = Cursor.lockState; _prevVisible = Cursor.visible; }
            else { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; }
        }

        private void LateUpdate()
        {
            if (!_open) return;
            // STRAFTAT re-locks the cursor every frame while you play; keep it free while the panel is up.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Which L4D2 asset sets loaded; empty when everything is there.</summary>
        private static string MissingAssets()
        {
            var missing = new System.Collections.Generic.List<string>();
            var inf = Zombies.InfectedAssets.Instance;
            if (inf != null && inf.Loaded && inf.Outfits.Count == 0) missing.Add("common infected");
            if (inf != null && inf.Loaded && inf.Special("hulk") == null) missing.Add("specials");
            var sur = Survivors.SurvivorAssets.Instance;
            if (sur != null && sur.Loaded && sur.Prefab(Plugin.Settings.SurvivorName.Value, "Arms") == null) missing.Add("survivors");
            if (string.IsNullOrEmpty(Plugin.Settings.HudTextureDir.Value) || !System.IO.Directory.Exists(Plugin.Settings.HudTextureDir.Value)) missing.Add("HUD textures");
            return string.Join(", ", missing);
        }

        private void OnGUI()
        {
            // First run: say so on the main menu when the L4D2 assets haven't been built yet.
            if (!_open && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu")
            {
                string missing = MissingAssets();
                if (missing.Length > 0)
                    GUI.Box(new Rect(20, 20, 560, 48), $"Straft 4 Dead: L4D2 assets not set up yet ({missing}).\nRun tools\\setup\\setup.ps1 with your L4D2 install, then restart. {Plugin.Settings.PanelKey.Value}: settings.");
            }
            if (!_open) return;
            if (_header == null) _header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            _rect = GUI.Window(0x54D4, _rect, Draw, "Straft 4 Dead");
        }

        private void Draw(int id)
        {
            var c = Plugin.Settings;
            _scroll = GUILayout.BeginScrollView(_scroll);

            Toggle(c.Enabled, "Straft 4 Dead mode (off = plain STRAFTAT)");
            string missingNow = MissingAssets();
            if (missingNow.Length > 0) GUILayout.Label($"Not set up: {missingNow} (run tools\\setup\\setup.ps1).");

            GUILayout.Space(6);
            GUILayout.Label("Your survivor", _header);
            GUILayout.BeginHorizontal();
            foreach (var (sid, name) in SurvivorChoices)
                if (GUILayout.Toggle(c.SurvivorName.Value == sid, name, GUI.skin.button)) c.SurvivorName.Value = sid;
            GUILayout.EndHorizontal();
            Toggle(c.SurvivorArms, "L4D2 arms");
            Toggle(c.SurvivorBodies, "L4D2 bodies");
            Toggle(c.SurvivorVoice, "Voice lines");
            Toggle(c.HudEnabled, "L4D2 HUD");

            GUILayout.Space(6);
            GUILayout.Label("Zombies (host)", _header);
            if (!Zombies.GameAccess.IsServer && Zombies.GameAccess.InLobby)
                GUILayout.Label("You're not the host: the host's zombie settings apply.");
            Toggle(c.ZombiesEnabled, "Zombies");
            Toggle(c.DirectorEnabled, "Director pacing (off = steady trickle)");
            IntSlider(c.HordeSize, "Horde size", 0, 40);
            IntSlider(c.Wanderers, "Wanderers between waves", 0, 30);
            IntSlider(c.MaxAlive, "Max zombies alive", 5, 80);
            Slider(c.FirstHorde, "Opening horde after (s)", 0f, 30f, "F0");
            Slider(c.HordeIntervalMin, "Next wave after at least (s)", 10f, 180f, "F0");
            Slider(c.HordeIntervalMax, "...and at most (s)", 10f, 240f, "F0");
            if (c.HordeIntervalMax.Value < c.HordeIntervalMin.Value) c.HordeIntervalMax.Value = c.HordeIntervalMin.Value;
            bool scaling = GUILayout.Toggle(c.FullSizeArea.Value > 0f, "Scale numbers to map size");
            if (scaling != c.FullSizeArea.Value > 0f) c.FullSizeArea.Value = scaling ? (float)c.FullSizeArea.DefaultValue : 0f;
            Slider(c.ZombieHealth, "Common health (L4D2 HP)", 10f, 200f, "F0");
            Slider(c.WeaponDamageToZombies, "Gun damage vs zombies (x)", 0.5f, 8f, "F1");
            Slider(c.ZombieStrength, "Zombie strength (how hard they hit)", 0.5f, 4f, "F1");
            Slider(c.ZombieDamage, "Common hit damage (before strength)", 1f, 25f, "F0");

            GUILayout.Space(6);
            GUILayout.Label("Special infected (host)", _header);
            Toggle(c.SpecialsEnabled, "Specials");
            Slider(c.TankChance, "Tank chance per take", 0f, 1f, "P0");
            Slider(c.WitchChance, "Witch chance per take", 0f, 1f, "P0");
            Slider(c.BoomerInterval, "Boomer every ~(s, 0 = never)", 0f, 120f, "F0");
            Slider(c.TankHealth, "Tank health", 200f, 4000f, "F0");

            GUILayout.Space(6);
            if (GUILayout.Button("Reset zombie settings to defaults"))
                foreach (var e in new ConfigEntryBase[] { c.HordeSize, c.Wanderers, c.MaxAlive, c.FirstHorde, c.HordeIntervalMin, c.HordeIntervalMax,
                             c.FullSizeArea, c.ZombieHealth, c.WeaponDamageToZombies, c.ZombieStrength, c.ZombieDamage, c.TankChance, c.WitchChance, c.BoomerInterval, c.TankHealth })
                    e.BoxedValue = e.DefaultValue;
            GUILayout.Label($"{Plugin.Settings.PanelKey.Value.ToUpperInvariant()} closes this panel. Changes are saved to the mod's config.");
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private static void Toggle(ConfigEntry<bool> e, string label) => e.Value = GUILayout.Toggle(e.Value, " " + label);

        private static void Slider(ConfigEntry<float> e, string label, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(220));
            float v = GUILayout.HorizontalSlider(e.Value, min, max, GUILayout.Width(130));
            GUILayout.Label(fmt == "P0" ? v.ToString("P0") : v.ToString(fmt), GUILayout.Width(45));
            GUILayout.EndHorizontal();
            if (Math.Abs(v - e.Value) > 1e-4f) e.Value = fmt == "F0" ? Mathf.Round(v) : fmt == "P0" ? Mathf.Round(v * 100f) / 100f : Mathf.Round(v * 10f) / 10f;
        }

        private static void IntSlider(ConfigEntry<int> e, string label, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(220));
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(e.Value, min, max, GUILayout.Width(130)));
            GUILayout.Label(v.ToString(), GUILayout.Width(45));
            GUILayout.EndHorizontal();
            if (v != e.Value) e.Value = v;
        }

        internal static Key ParseKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return Key.None;
            if (name.Length == 1 && char.IsDigit(name[0])) name = "Digit" + name;
            return Enum.TryParse(name, true, out Key k) ? k : Key.None;
        }

        internal bool IsOpen => _open;
        internal void SetOpen(bool open) { if (open != _open) { _open = open; if (open) { _prevLock = Cursor.lockState; _prevVisible = Cursor.visible; } } }
    }
}
