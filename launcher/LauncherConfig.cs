using System;
using System.IO;

namespace Straft4Dead.Launcher
{
    /// <summary>A pinned download: one exact file, checked against its SHA-256 before it's used.</summary>
    public sealed class Pinned
    {
        public Pinned(string file, string sha256, params string[] urls)
        {
            File = file;
            Sha256 = sha256;
            Urls = urls;
        }

        public string File { get; }
        public string Sha256 { get; }
        /// <summary>Tried in order (mirrors).</summary>
        public string[] Urls { get; }
    }

    /// <summary>Fixed settings: where releases come from, the games, and every third-party download.</summary>
    public static class LauncherConfig
    {
        /// <summary>GitHub "owner/repo" the launcher downloads Straft 4 Dead releases from. Change it here if the repo moves.</summary>
        public const string GitHubRepo = "rileyedward/straft4dead";

        public const string StraftatAppId = "2386720";
        public const string L4D2AppId = "550";

        public const string ModZipPrefix = "Straft4Dead-";
        public const string LauncherAssetName = "Straft4Dead-Launcher.exe";

        public static string ReleasesPage => $"https://github.com/{GitHubRepo}/releases";

        public static Version LauncherVersion => typeof(LauncherConfig).Assembly.GetName().Version;

        // --- Third-party downloads, pinned to the versions the mod is built and tested with ---

        public static readonly Pinned BepInEx = new Pinned("BepInEx_win_x64_5.4.23.5.zip",
            "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4",
            "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip");

        public static readonly Pinned Python = new Pinned("python-3.12.10-embed-amd64.zip",
            "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3",
            "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip");

        public static readonly Pinned Blender = new Pinned("blender-5.2.2-windows-x64.zip",
            "3849d17a682cba006075aaa3f3597ecb5c9c30ec31035b2e092c53e40679b535",
            "https://download.blender.org/release/Blender5.2/blender-5.2.2-windows-x64.zip",
            "https://ftp.nluug.nl/pub/graphics/blender/release/Blender5.2/blender-5.2.2-windows-x64.zip",
            "https://mirrors.ocf.berkeley.edu/blender/release/Blender5.2/blender-5.2.2-windows-x64.zip");

        public static readonly Pinned SourceIO = new Pinned("SourceIO-5.5.4.zip",
            "c6da6d989120b430df0a74315307fc42b8f87fa3c1d98554ee589c7e93f79e4d",
            "https://github.com/REDxEYE/SourceIO/releases/download/5.5.4/SourceIO.zip");

        public static readonly Pinned Crowbar = new Pinned("Crowbar_2023-02-16_0.74.7z",
            "a33d16ea4894ec01686e53d01c6a2b8f816f4abdaead7ece41262e03ffa9c13f",
            "https://github.com/ZeqMacaw/Crowbar/releases/download/v0.74/Crowbar_2023-02-16_0.74.7z");

        /// <summary>7-Zip's standalone extractor, only to unpack Crowbar's .7z.</summary>
        public static readonly Pinned SevenZip = new Pinned("7zr.exe",
            "256feca8e274e5da655e2a284fabafd9f554365eb164862089dacd4e8276d282",
            "https://www.7-zip.org/a/7zr.exe");

        // --- Unity: the editor installs from Unity's own installer; only the free licence needs the player (Unity Hub) ---

        public const string UnityVersion = "2021.3.45f2";
        public const string UnityChangeset = "88f88f591b2e";
        /// <summary>Unity's official installer for exactly this version (about 3.2 GB), the same file Unity Hub fetches.</summary>
        public static string UnityEditorSetupUrl =>
            $"https://download.unity3d.com/download_unity/{UnityChangeset}/Windows64EditorInstaller/UnitySetup64-{UnityVersion}.exe";
        /// <summary>Unity Hub's installer; Unity has moved it before, so the old name is the fallback.</summary>
        public static readonly string[] UnityHubSetupUrls =
        {
            "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup-x64.exe",
            "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe",
        };
        public const string UnityDownloadPage = "https://unity.com/download";
    }

    /// <summary>
    /// Where the launcher keeps things. Home is %APPDATA%\Straft4Dead unless --home overrides it:
    ///   profile\   BepInEx, its doorstop files, the plugin and its config
    ///   kit\       the release's setup script, asset pipeline and Unity bundle-builder project
    ///   tools\     portable Python, Blender (with SourceIO) and Crowbar
    ///   assets\    the L4D2 assets built from the player's install (never shared)
    ///   cache\     downloads, so a reinstall doesn't download again
    ///   state.json, launcher.log, setup.log
    /// </summary>
    public sealed class LauncherPaths
    {
        public LauncherPaths(string home)
        {
            Home = Path.GetFullPath(home);
        }

        public static string DefaultHome =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Straft4Dead");

        public string Home { get; }
        public string Profile => Path.Combine(Home, "profile");
        public string Kit => Path.Combine(Home, "kit");
        public string Tools => Path.Combine(Home, "tools");
        public string Assets => Path.Combine(Home, "assets");
        public string Cache => Path.Combine(Home, "cache");
        public string Backup => Path.Combine(Home, "game-backup");
        public string StateFile => Path.Combine(Home, "state.json");
        public string LogFile => Path.Combine(Home, "launcher.log");
        public string SetupLog => Path.Combine(Home, "setup.log");

        public string BepInEx => Path.Combine(Profile, "BepInEx");
        public string Preloader => Path.Combine(BepInEx, "core", "BepInEx.Preloader.dll");
        public string Config => Path.Combine(BepInEx, "config");
        public string Plugins => Path.Combine(BepInEx, "plugins");
        public string ModConfig => Path.Combine(Config, "dev.riley.straft4dead.cfg");

        public string PythonExe => Path.Combine(Tools, "python", "python.exe");
        public string BlenderDir => Path.Combine(Tools, "blender");
        public string BlenderExe => Path.Combine(BlenderDir, "blender.exe");
        public string CrowbarExe => Path.Combine(Tools, "crowbar", "Crowbar.exe");
        public string SetupScript => Path.Combine(Kit, "tools", "setup", "setup.ps1");
    }
}
