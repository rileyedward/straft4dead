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
        public ConfigEntry<string> InfectedBundle { get; }
        public ConfigEntry<float> PlayerHealthMultiplier { get; }
        public ConfigEntry<bool> IncapEnabled { get; }
        public ConfigEntry<float> IncapSeconds { get; }
        public ConfigEntry<float> TempHealthDecay { get; }
        public ConfigEntry<int> StartMedkits { get; }
        public ConfigEntry<int> StartPills { get; }
        public ConfigEntry<float> MedkitSeconds { get; }
        public ConfigEntry<string> PillsKey { get; }
        public ConfigEntry<string> MedkitKey { get; }

        public ConfigEntry<bool> HudEnabled { get; }
        public ConfigEntry<string> HudTextureDir { get; }
        public ConfigEntry<string> L4D2SoundDir { get; }
        public ConfigEntry<string> SurvivorName { get; }

        public ConfigEntry<bool> DirectorEnabled { get; }
        public ConfigEntry<int> Wanderers { get; }
        public ConfigEntry<int> HordeSize { get; }
        public ConfigEntry<float> FirstHorde { get; }
        public ConfigEntry<float> HordeIntervalMin { get; }
        public ConfigEntry<float> HordeIntervalMax { get; }
        public ConfigEntry<float> RelaxSeconds { get; }
        public ConfigEntry<bool> ClearOnNewTake { get; }
        public ConfigEntry<bool> HordeMusic { get; }

        public ConfigEntry<string> SurvivorBundle { get; }
        public ConfigEntry<bool> SurvivorArms { get; }
        public ConfigEntry<bool> SurvivorBodies { get; }
        public ConfigEntry<bool> SurvivorVoice { get; }

        public ConfigEntry<bool> DevAutoTest { get; }
        public ConfigEntry<string> DevAutoTestMode { get; }
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
            MaxAlive = file.Bind("Zombies", "MaxAlive", 30, "Most zombies alive at once, wanderers and hordes together.");
            SpawnInterval = file.Bind("Zombies", "SpawnInterval", 2f, "Seconds between spawn attempts while under MaxAlive.");
            SpawnMinDistance = file.Bind("Zombies", "SpawnMinDistance", 10f, "Closest a zombie spawns to a player (metres).");
            SpawnMaxDistance = file.Bind("Zombies", "SpawnMaxDistance", 35f, "Farthest a zombie spawns from a player (metres).");
            ZombieHealth = file.Bind("Zombies", "Health", 50f, "Zombie health in L4D2 HP (a common infected has 50).");
            ZombieDamage = file.Bind("Zombies", "Damage", 5f, "Damage per zombie swing in L4D2 HP (100 = full STRAFTAT bar).");
            ZombieSpeed = file.Bind("Zombies", "Speed", 6.5f, "Run speed in m/s.");
            ZombieReach = file.Bind("Zombies", "Reach", 1.5f, "Swing reach in metres.");
            ZombieAttackInterval = file.Bind("Zombies", "AttackInterval", 1f, "Seconds between swings.");
            WeaponDamageToZombies = file.Bind("Zombies", "WeaponDamageMultiplier", 1f, "Scales STRAFTAT weapon damage against zombies (1 = a default body shot does 25 HP).");
            InfectedBundle = file.Bind("Zombies", "InfectedBundle", "", "Full path to the s4d_infected AssetBundle (L4D2 commons built from your install). Empty or missing: placeholder capsules.");
            PlayerHealthMultiplier = file.Bind("Players", "HealthMultiplier", 1f, "Scales every player's health when they spawn (host only). 2 = 200 HP against bullets and zombies.");
            IncapEnabled = file.Bind("Players", "Incap", true, "At 0 HP go down for a few seconds (L4D2 incap: camera drop, red screen, sting) before the take ends.");
            IncapSeconds = file.Bind("Players", "IncapSeconds", 3f, "How long the incap lasts before the take ends.");
            TempHealthDecay = file.Bind("Players", "TempHealthDecay", 0.27f, "Temporary (pill) health lost per second (L4D2: 0.27).");
            StartMedkits = file.Bind("Players", "StartMedkits", 1, "First aid kits each player spawns with.");
            StartPills = file.Bind("Players", "StartPills", 1, "Pain pills each player spawns with.");
            MedkitSeconds = file.Bind("Players", "MedkitSeconds", 5f, "Hold time to heal with a first aid kit (L4D2: 5).");
            PillsKey = file.Bind("Players", "PillsKey", "5", "Key to take pain pills (+50 temporary health). Unity Input System key name.");
            MedkitKey = file.Bind("Players", "MedkitKey", "4", "Hold to heal with a first aid kit (80% of missing health). Unity Input System key name.");

            HudEnabled = file.Bind("HUD", "Enabled", true, "Replace STRAFTAT's health and ammo display with the L4D2 survivor HUD.");
            HudTextureDir = file.Bind("HUD", "TextureDir", "", "Folder with the L4D2 HUD textures converted to PNG (healthbar_*, s_panel_*, inventory_*). Extracted from your own L4D2 install.");
            L4D2SoundDir = file.Bind("HUD", "L4D2SoundDir", @"C:\Program Files (x86)\Steam\steamapps\common\Left 4 Dead 2\left4dead2\sound", "Your L4D2 install's loose sound folder (incap sting).");
            SurvivorName = file.Bind("HUD", "Survivor", "mechanic", "Which survivor you play (portrait, arms, body, voice): mechanic (Ellis), gambler (Nick), producer (Rochelle), coach.");

            DirectorEnabled = file.Bind("Director", "Enabled", true, "L4D2-style pacing: wanderers build up, a horde hits, then a calm spell; survivor stress (damage, close kills, incaps) brings the calm on early. Off: a constant trickle up to MaxAlive.");
            Wanderers = file.Bind("Director", "Wanderers", 8, "Wandering commons kept on the map during build-up. They chase once they see or hear you, get shot, or a neighbour does.");
            HordeSize = file.Bind("Director", "HordeSize", 20, "Commons in a horde (L4D2 mobs are 10-30).");
            FirstHorde = file.Bind("Director", "FirstHorde", 30f, "Seconds into a take before the first horde.");
            HordeIntervalMin = file.Bind("Director", "HordeIntervalMin", 60f, "Shortest gap between hordes (seconds of build-up).");
            HordeIntervalMax = file.Bind("Director", "HordeIntervalMax", 100f, "Longest gap between hordes.");
            RelaxSeconds = file.Bind("Director", "RelaxSeconds", 20f, "Calm after a peak: no new zombies for this long (and until stress drops).");
            ClearOnNewTake = file.Bind("Director", "ClearOnNewTake", true, "Remove every zombie when a new take starts (everyone respawned).");
            HordeMusic = file.Bind("Director", "HordeMusic", true, "Play L4D2's horde stinger and drums during a horde.");

            SurvivorBundle = file.Bind("Survivors", "Bundle", "", "Full path to the s4d_survivors AssetBundle (L4D2 survivors built from your install). Empty or missing: STRAFTAT's own character.");
            SurvivorArms = file.Bind("Survivors", "Arms", true, "Show the survivor's L4D2 first-person arms instead of STRAFTAT's.");
            SurvivorBodies = file.Bind("Survivors", "Bodies", true, "Show players as L4D2 survivor bodies in third person.");
            SurvivorVoice = file.Bind("Survivors", "Voice", true, "Survivor voice lines (hurt, incap, healing, kills).");

            DevAutoTest = file.Bind("Dev", "AutoTest", false, "Test run without input: from the main menu enter Dev.TestMap in exploration mode, check a weapon hit on a zombie, save screenshots.");
            DevAutoTestMode = file.Bind("Dev", "AutoTestMode", "zombies", "AutoTest script: zombies (Phase 1: spawn, chase, weapon hit) health (Phase 2: damage, pills, medkit, incap) or arms (Phase 4: dump the arm rig, screenshot the survivor arms).");
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
