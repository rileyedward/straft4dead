using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Straft4Dead.Launcher
{
    /// <summary>Finds Steam, STRAFTAT and Left 4 Dead 2, and starts STRAFTAT through Steam.</summary>
    public static class Steam
    {
        /// <summary>Steam's install folder from the registry, or null.</summary>
        public static string FindSteam()
        {
            var candidates = new[]
            {
                Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string,
            };
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                var path = Path.GetFullPath(candidate.Replace('/', '\\'));
                if (File.Exists(Path.Combine(path, "steam.exe"))) return path;
            }
            return null;
        }

        /// <summary>Every Steam library folder listed in steamapps\libraryfolders.vdf, starting with Steam's own.</summary>
        public static List<string> LibraryFolders(string steam)
        {
            var folders = new List<string> { steam };
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                // "path"  "D:\\SteamLibrary" (current format); older files used "1" "D:\\SteamLibrary".
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "^\\s*\"(?:path|\\d+)\"\\s+\"([^\"]+)\"", RegexOptions.Multiline))
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path)) folders.Add(path);
                }
            }
            return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>A game's folder in any Steam library (from its app manifest), or null.</summary>
        private static string FindGame(string steam, string appId, string defaultDir, Func<string, bool> isGame)
        {
            if (steam == null) return null;
            foreach (var library in LibraryFolders(steam))
            {
                var apps = Path.Combine(library, "steamapps");
                var installDir = defaultDir;
                var manifest = Path.Combine(apps, $"appmanifest_{appId}.acf");
                if (File.Exists(manifest))
                {
                    var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (match.Success) installDir = match.Groups[1].Value;
                }
                var dir = Path.Combine(apps, "common", installDir);
                if (isGame(dir)) return dir;
            }
            return null;
        }

        public static string FindStraftat(string steam) => FindGame(steam, LauncherConfig.StraftatAppId, "STRAFTAT", IsStraftatFolder);
        public static string FindL4D2(string steam) => FindGame(steam, LauncherConfig.L4D2AppId, "Left 4 Dead 2", IsL4D2Folder);

        public static bool IsStraftatFolder(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "STRAFTAT.exe"));

        public static bool IsL4D2Folder(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "left4dead2", "pak01_dir.vpk"));

        public static bool IsStraftatRunning() => Process.GetProcessesByName("STRAFTAT").Length > 0;

        public static void OpenStorePage(string appId) => Open($"steam://store/{appId}");

        /// <summary>Starts STRAFTAT through Steam with extra command-line arguments.</summary>
        public static void LaunchStraftat(string steam, string arguments)
        {
            var exe = steam == null ? null : Path.Combine(steam, "steam.exe");
            if (exe == null || !File.Exists(exe)) throw new InvalidOperationException("Steam wasn't found, so STRAFTAT can't be started.");
            Process.Start(new ProcessStartInfo(exe, $"-applaunch {LauncherConfig.StraftatAppId} {arguments}") { UseShellExecute = false });
        }

        public static void Open(string target)
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }
}
