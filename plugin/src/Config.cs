using BepInEx.Configuration;

namespace Straft4Dead
{
    /// <summary>Bound to BepInEx\config\dev.riley.straft4dead.cfg (created on first launch; new keys are added automatically).
    /// Every feature gets its own toggle so any piece can be switched off without rebuilding.</summary>
    internal sealed class PluginConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> LogDiagnostics { get; }

        public ConfigEntry<bool> Phase0AssetProof { get; }
        public ConfigEntry<string> Phase0BundlePath { get; }
        public ConfigEntry<string> Phase0VoiceLine { get; }

        public PluginConfig(ConfigFile file)
        {
            Enabled = file.Bind("General", "Enabled", true,
                "Master switch. When false the plugin loads, logs, and does nothing else.");
            LogDiagnostics = file.Bind("General", "LogDiagnostics", true,
                "On each scene load, log the scene name, spawn points, and whether a NavMesh is present (Phase 0 recon aid).");

            Phase0AssetProof = file.Bind("Phase0", "AssetProof", false,
                "Phase 0 test: in the main menu, load the locally built L4D2 bundle, show the infected and Ellis (pose copied from the menu Aboubi), play a voice line, save a screenshot.");
            Phase0BundlePath = file.Bind("Phase0", "BundlePath", "",
                "Full path to the s4d_phase0 AssetBundle built by unity/S4DBundles (lives outside the game and the repo).");
            Phase0VoiceLine = file.Bind("Phase0", "VoiceLine", "",
                "Full path to one Ellis .wav in your L4D2 install (left4dead2\\sound\\player\\survivor\\voice\\mechanic\\...).");
        }
    }
}
