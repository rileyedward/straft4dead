using BepInEx.Configuration;

namespace Straft4Dead
{
    /// <summary>Bound to BepInEx\config\dev.riley.straft4dead.cfg (created on first launch; new keys are added automatically).
    /// Every feature gets its own toggle so any piece can be switched off without rebuilding.</summary>
    internal sealed class PluginConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> LogDiagnostics { get; }

        public ConfigEntry<bool> ZombiesEnabled { get; }
        public ConfigEntry<int> MaxAlive { get; }
        public ConfigEntry<float> SpawnInterval { get; }
        public ConfigEntry<float> SpawnMinDistance { get; }
        public ConfigEntry<float> SpawnMaxDistance { get; }
        public ConfigEntry<float> ZombieHealth { get; }
        public ConfigEntry<float> ZombieDamage { get; }
        public ConfigEntry<float> ZombieSpeed { get; }
        public ConfigEntry<float> ZombieReach { get; }
        public ConfigEntry<float> ZombieAttackInterval { get; }
        public ConfigEntry<float> WeaponDamageToZombies { get; }
        public ConfigEntry<float> PlayerHealthMultiplier { get; }

        public ConfigEntry<bool> DevAutoTest { get; }
        public ConfigEntry<string> DevTestMap { get; }
        public ConfigEntry<float> DevScreenshotDelay { get; }
        public ConfigEntry<bool> DevDrawNavMesh { get; }

        public ConfigEntry<bool> Phase0AssetProof { get; }
        public ConfigEntry<string> Phase0BundlePath { get; }
        public ConfigEntry<string> Phase0VoiceLine { get; }

        public PluginConfig(ConfigFile file)
        {
            Enabled = file.Bind("General", "Enabled", true,
                "Master switch. When false the plugin loads, logs, and does nothing else.");
            LogDiagnostics = file.Bind("General", "LogDiagnostics", true,
                "On each scene load, log the scene name, spawn points, and whether a NavMesh is present (Phase 0 recon aid).");

            // Health and damage are in L4D2 HP: 100 = one full STRAFTAT health bar (4 game units).
            ZombiesEnabled = file.Bind("Zombies", "Enabled", true, "Spawn placeholder zombies on maps (host only, local single-player for now).");
            MaxAlive = file.Bind("Zombies", "MaxAlive", 8, "Most zombies alive at once.");
            SpawnInterval = file.Bind("Zombies", "SpawnInterval", 2f, "Seconds between spawn attempts while under MaxAlive.");
            SpawnMinDistance = file.Bind("Zombies", "SpawnMinDistance", 10f, "Closest a zombie spawns to a player (metres).");
            SpawnMaxDistance = file.Bind("Zombies", "SpawnMaxDistance", 35f, "Farthest a zombie spawns from a player (metres).");
            ZombieHealth = file.Bind("Zombies", "Health", 50f, "Zombie health in L4D2 HP (a common infected has 50).");
            ZombieDamage = file.Bind("Zombies", "Damage", 5f, "Damage per zombie swing in L4D2 HP (100 = full STRAFTAT bar).");
            ZombieSpeed = file.Bind("Zombies", "Speed", 6.5f, "Run speed in m/s.");
            ZombieReach = file.Bind("Zombies", "Reach", 1.5f, "Swing reach in metres.");
            ZombieAttackInterval = file.Bind("Zombies", "AttackInterval", 1f, "Seconds between swings.");
            WeaponDamageToZombies = file.Bind("Zombies", "WeaponDamageMultiplier", 1f, "Scales STRAFTAT weapon damage against zombies (1 = a default body shot does 25 HP).");
            PlayerHealthMultiplier = file.Bind("Players", "HealthMultiplier", 1f, "Scales every player's health when they spawn (host only). 2 = twice the health against bullets and zombies.");

            DevAutoTest = file.Bind("Dev", "AutoTest", false, "Test run without input: from the main menu enter Dev.TestMap in exploration mode, check a weapon hit on a zombie, save screenshots.");
            DevTestMap = file.Bind("Dev", "TestMap", "", "Scene name for F8 / AutoTest (the plugin logs all scene names at startup).");
            DevDrawNavMesh = file.Bind("Dev", "DrawNavMesh", false, "Draw the baked navmesh in cyan.");
            DevScreenshotDelay = file.Bind("Dev", "ScreenshotDelay", 12f, "AutoTest: seconds to wait before each screenshot.");

            Phase0AssetProof = file.Bind("Phase0", "AssetProof", false,
                "Phase 0 test: in the main menu, load the locally built L4D2 bundle, show the infected and Ellis (pose copied from the menu Aboubi), play a voice line, save a screenshot.");
            Phase0BundlePath = file.Bind("Phase0", "BundlePath", "",
                "Full path to the s4d_phase0 AssetBundle built by unity/S4DBundles (lives outside the game and the repo).");
            Phase0VoiceLine = file.Bind("Phase0", "VoiceLine", "",
                "Full path to one Ellis .wav in your L4D2 install (left4dead2\\sound\\player\\survivor\\voice\\mechanic\\...).");
        }
    }
}
