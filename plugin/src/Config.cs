using BepInEx.Configuration;

namespace Straft4Dead
{
    /// <summary>Bound to BepInEx\config\dev.riley.straft4dead.cfg (created on first launch; new keys are added automatically).
    /// Every feature gets its own toggle so any piece can be switched off without rebuilding.</summary>
    internal sealed class PluginConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> LogDiagnostics { get; }

        public PluginConfig(ConfigFile file)
        {
            Enabled = file.Bind("General", "Enabled", true,
                "Master switch. When false the plugin loads, logs, and does nothing else.");
            LogDiagnostics = file.Bind("General", "LogDiagnostics", true,
                "On each scene load, log the scene name, spawn points, and whether a NavMesh is present (Phase 0 recon aid).");
        }
    }
}
