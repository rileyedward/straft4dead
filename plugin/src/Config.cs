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
        public ConfigEntry<float> MaxBakeSize { get; }
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
        public ConfigEntry<float> FullSizeArea { get; }

        public ConfigEntry<bool> SpecialsEnabled { get; }
        public ConfigEntry<string> SpecialsBundle { get; }
        public ConfigEntry<float> TankChance { get; }
        public ConfigEntry<float> TankHealth { get; }
        public ConfigEntry<float> TankPunchDamage { get; }
        public ConfigEntry<float> BoomerInterval { get; }
        public ConfigEntry<float> WitchChance { get; }
        public ConfigEntry<float> WitchHealth { get; }

        public ConfigEntry<string> SurvivorBundle { get; }
        public ConfigEntry<bool> SurvivorArms { get; }
        public ConfigEntry<bool> SurvivorBodies { get; }
        public ConfigEntry<bool> SurvivorVoice { get; }

        public ConfigEntry<bool> DevAutoTest { get; }
        public ConfigEntry<string> DevAutoTestMode { get; }
        public ConfigEntry<string> DevTestMap { get; }
        public ConfigEntry<float> DevScreenshotDelay { get; }
        public ConfigEntry<string> DevSweepMaps { get; }
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
            WeaponDamageToZombies = file.Bind("Zombies", "WeaponDamageMultiplier", 4f, "Scales STRAFTAT weapon damage against zombies. 4: a body shot that would take a quarter of a player's health does 100 HP, so most guns one-shot a common (50 HP); 1 = player-equivalent damage.");
            MaxBakeSize = file.Bind("Zombies", "MaxBakeSize", 140f, "Largest area (metres per side) the zombie navmesh covers, centred on the spawn points. Keeps load time sane on huge maps; 0 = whole map.");
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
            HordeSize = file.Bind("Director", "HordeSize", 12, "Commons in a horde: enough to worry about, not a wall (L4D2 mobs are 10-30).");
            FirstHorde = file.Bind("Director", "FirstHorde", 2f, "Seconds into a take before the opening horde storms in (takes are short, so it comes right away).");
            HordeIntervalMin = file.Bind("Director", "HordeIntervalMin", 40f, "If the take goes on: shortest gap before the next wave (seconds of build-up).");
            HordeIntervalMax = file.Bind("Director", "HordeIntervalMax", 60f, "Longest gap before the next wave.");
            RelaxSeconds = file.Bind("Director", "RelaxSeconds", 10f, "Calm after a peak: no new zombies for this long (and until stress drops).");
            ClearOnNewTake = file.Bind("Director", "ClearOnNewTake", true, "Remove every zombie when a new take starts (everyone respawned).");
            FullSizeArea = file.Bind("Director", "FullSizeArea", 1500f, "Walkable floor (m²) at which a map gets the full Wanderers and HordeSize; smaller maps get proportionally fewer (at least 40%). 0 = no scaling.");
            HordeMusic = file.Bind("Director", "HordeMusic", true, "Play L4D2's horde stinger and drums during a horde.");

            SpecialsEnabled = file.Bind("Specials", "Enabled", true, "Tank, Boomer and Witch (needs [Director] Enabled).");
            SpecialsBundle = file.Bind("Specials", "Bundle", "", "Full path to the s4d_specials AssetBundle (Tank, Boomer, Witch built from your L4D2 install). Empty or missing: capsule stand-ins.");
            TankChance = file.Bind("Specials", "TankChance", 0.35f, "Chance per take that a Tank shows up (20-40 s in, if the take lasts).");
            TankHealth = file.Bind("Specials", "TankHealth", 1000f, "Tank health in L4D2 HP (L4D2 normal: 4000; scaled for short takes).");
            TankPunchDamage = file.Bind("Specials", "TankPunchDamage", 24f, "Damage of a Tank punch or rock (L4D2 normal: 24).");
            BoomerInterval = file.Bind("Specials", "BoomerInterval", 30f, "Average seconds between Boomers (one at a time). 0 = none.");
            WitchChance = file.Bind("Specials", "WitchChance", 0.5f, "Chance per take that a Witch sits somewhere on the map.");
            WitchHealth = file.Bind("Specials", "WitchHealth", 500f, "Witch health in L4D2 HP (L4D2: 1000).");

            SurvivorBundle = file.Bind("Survivors", "Bundle", "", "Full path to the s4d_survivors AssetBundle (L4D2 survivors built from your install). Empty or missing: STRAFTAT's own character.");
            SurvivorArms = file.Bind("Survivors", "Arms", true, "Show the survivor's L4D2 first-person arms instead of STRAFTAT's.");
            SurvivorBodies = file.Bind("Survivors", "Bodies", true, "Show players as L4D2 survivor bodies in third person.");
            SurvivorVoice = file.Bind("Survivors", "Voice", true, "Survivor voice lines (hurt, incap, healing, kills).");

            DevAutoTest = file.Bind("Dev", "AutoTest", false, "Test run without input: from the main menu enter Dev.TestMap in exploration mode, check a weapon hit on a zombie, save screenshots.");
            DevAutoTestMode = file.Bind("Dev", "AutoTestMode", "zombies", "AutoTest script: zombies (Phase 1: spawn, chase, weapon hit) health (Phase 2: damage, pills, medkit, incap) or arms (Phase 4: dump the arm rig, screenshot the survivor arms).");
            DevTestMap = file.Bind("Dev", "TestMap", "", "Scene name for F8 / AutoTest (the plugin logs all scene names at startup).");
            DevDrawNavMesh = file.Bind("Dev", "DrawNavMesh", false, "Draw the baked navmesh in cyan.");
            DevScreenshotDelay = file.Bind("Dev", "ScreenshotDelay", 12f, "AutoTest: seconds to wait before each screenshot.");
            DevSweepMaps = file.Bind("Dev", "SweepMaps", "Backrooms_00,Chateaux_01,HK_02", "AutoTestMode=maps: comma-separated scene names to check (empty = every map, which takes a long time).");

            Phase0AssetProof = file.Bind("Phase0", "AssetProof", false,
                "Phase 0 test: in the main menu, load the locally built L4D2 bundle, show the infected and Ellis (pose copied from the menu Aboubi), play a voice line, save a screenshot.");
            Phase0BundlePath = file.Bind("Phase0", "BundlePath", "",
                "Full path to the s4d_phase0 AssetBundle built by unity/S4DBundles (lives outside the game and the repo).");
            Phase0VoiceLine = file.Bind("Phase0", "VoiceLine", "",
                "Full path to one Ellis .wav in your L4D2 install (left4dead2\\sound\\player\\survivor\\voice\\mechanic\\...).");
        }
    }
}
