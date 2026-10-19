using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Straft4Dead.Launcher
{
    /// <summary>
    /// Installs BepInEx and Straft 4 Dead into the launcher's profile and prepares the STRAFTAT folder for launching
    /// from it. Plain STRAFTAT stays as it was: the only files added to the game folder are doorstop's loader
    /// (winhttp.dll) and a doorstop_config.ini that keeps it switched off unless the launcher's Play turns it on.
    /// The release's setup kit (setup script, asset pipeline, Unity project) goes to kit\.
    /// </summary>
    public sealed class Installer
    {
        private readonly LauncherPaths paths;
        private readonly Action<string> log;

        public Installer(LauncherPaths paths, Action<string> log)
        {
            this.paths = paths;
            this.log = log;
        }

        public LauncherState State { get; private set; }

        public void LoadState() => State = LauncherState.Load(paths.StateFile);

        public void SaveState() => State.Save(paths.StateFile);

        /// <summary>Installs or updates from a Straft 4 Dead release zip (BepInEx first, from its pinned download).</summary>
        public async Task Install(string modZip, Sources sources)
        {
            LoadState();
            var version = Sources.ParseVersion(Path.GetFileNameWithoutExtension(modZip).Replace(LauncherConfig.ModZipPrefix, ""));
            log($"Installing Straft 4 Dead {version?.ToString(3) ?? "(local zip)"} into {paths.Profile}");
            Directory.CreateDirectory(paths.Profile);

            if (!File.Exists(paths.Preloader) || !State.Installed.Contains(LauncherConfig.BepInEx.File))
            {
                var bepinex = await sources.Get(LauncherConfig.BepInEx);
                ExtractAll(bepinex, paths.Profile, keepPrefix: "BepInEx/config/");
                State.Installed.Add(LauncherConfig.BepInEx.File);
                SaveState();
                log("BepInEx installed");
            }

            // The plugin, then the kit (everything but BepInEx/), replacing old copies so updates leave no stale files.
            var pluginDir = Path.Combine(paths.Plugins, "Straft4Dead");
            DeleteDirectory(pluginDir);
            DeleteDirectory(paths.Kit);
            using (var zip = ZipFile.OpenRead(modZip))
            {
                if (!zip.Entries.Any(e => e.FullName.Replace('\\', '/').EndsWith("Straft4Dead.Plugin.dll", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"{Path.GetFileName(modZip)} has no Straft4Dead.Plugin.dll, so it isn't a Straft 4 Dead release zip.");
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith("/")) continue;
                    if (name.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) Extract(entry, paths.Profile, name, overwrite: true);
                    else Extract(entry, paths.Kit, name, overwrite: true);
                }
            }
            State.ModVersion = version?.ToString(3) ?? "local";
            SaveState();
            log($"Straft 4 Dead {State.ModVersion} installed");
        }

        /// <summary>Unpacks a zip into a folder; files under <paramref name="keepPrefix"/> are never overwritten.</summary>
        public static void ExtractAll(string zipPath, string root, string keepPrefix = null, string stripPrefix = null)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith("/")) continue;
                if (stripPrefix != null)
                {
                    if (!name.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    name = name.Substring(stripPrefix.Length);
                }
                var keep = keepPrefix != null && name.StartsWith(keepPrefix, StringComparison.OrdinalIgnoreCase);
                Extract(entry, root, name, overwrite: !keep);
            }
        }

        private static void Extract(ZipArchiveEntry entry, string root, string relative, bool overwrite)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            var target = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', '\\')));
            if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Zip entry {entry.FullName} points outside the install folder.");
            if (!overwrite && File.Exists(target)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            entry.ExtractToFile(target, overwrite: true);
        }

        /// <summary>
        /// Puts doorstop's loader in the STRAFTAT folder (switched off by its config file, so plain Steam launches stay
        /// vanilla) and returns the Steam launch arguments that switch it on and point it at the profile's BepInEx.
        /// </summary>
        public string PrepareGameFolder(string gameDir)
        {
            LoadState();
            if (!File.Exists(paths.Preloader)) throw new InvalidOperationException("Straft 4 Dead isn't installed yet.");
            var packIni = Path.Combine(paths.Profile, "doorstop_config.ini");
            var packIniText = File.Exists(packIni) ? File.ReadAllText(packIni) : "";

            if (State.GameFilesDir != null && !string.Equals(State.GameFilesDir, gameDir, StringComparison.OrdinalIgnoreCase))
                RemoveGameFiles();
            State.GameFilesDir = gameDir;

            // A BepInEx installed straight into the game folder uses the same two files; leave them alone and rely on
            // the launch arguments instead.
            if (Directory.Exists(Path.Combine(gameDir, "BepInEx", "core")))
                log("STRAFTAT's folder has its own BepInEx; leaving its doorstop files alone.");
            else
            {
                PlaceGameFile(gameDir, "winhttp.dll", File.ReadAllBytes(Path.Combine(paths.Profile, "winhttp.dll")));
                PlaceGameFile(gameDir, "doorstop_config.ini", Encoding.UTF8.GetBytes(GameFolderIni(packIniText)));
            }
            SaveState();
            // BepInEx 5.4.23 ships doorstop 4.
            return $"--doorstop-enabled true --doorstop-target-assembly \"{paths.Preloader}\"";
        }

        /// <summary>The pack's doorstop config, switched off, with its paths made absolute into the profile.</summary>
        private string GameFolderIni(string packIni)
        {
            if (packIni.Length == 0)
                return $"[General]\r\nenabled = false\r\ntarget_assembly = {paths.Preloader}\r\n";
            var lines = Regex.Split(packIni, "\r?\n").Select(line =>
            {
                var match = Regex.Match(line, @"^(\s*)([A-Za-z_]+)(\s*=\s*)(.*)$");
                if (!match.Success) return line;
                var key = match.Groups[2].Value;
                var value = match.Groups[4].Value.Trim();
                if (key.Equals("enabled", StringComparison.OrdinalIgnoreCase)) value = "false";
                else if (IsPathKey(key) && value.Length > 0 && !Path.IsPathRooted(value))
                    value = Path.GetFullPath(Path.Combine(paths.Profile, value));
                return match.Groups[1].Value + key + match.Groups[3].Value + value;
            });
            return string.Join("\r\n", lines);
        }

        private static bool IsPathKey(string key)
        {
            var k = key.Replace("_", "").ToLowerInvariant();
            return k == "targetassembly" || k == "dllsearchpathoverride" || k == "bootconfigoverride";
        }

        private void PlaceGameFile(string gameDir, string name, byte[] content)
        {
            var target = Path.Combine(gameDir, name);
            var hash = Sha256(content);
            var record = State.GameFiles.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(target) && Sources.Sha256(target) == hash)
            {
                if (record == null) State.GameFiles.Add(new GameFile { Name = name, Sha256 = hash, WasThere = true });
                return;
            }
            var backedUp = record?.BackedUp ?? false;
            if (File.Exists(target) && (record == null || Sources.Sha256(target) != record.Sha256))
            {
                Directory.CreateDirectory(paths.Backup);
                File.Copy(target, Path.Combine(paths.Backup, name), overwrite: true);
                backedUp = true;
                log($"Saved STRAFTAT's existing {name} to {paths.Backup}");
            }
            File.WriteAllBytes(target, content);
            State.GameFiles.RemoveAll(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            State.GameFiles.Add(new GameFile { Name = name, Sha256 = hash, BackedUp = backedUp });
            log($"Wrote {name} to the STRAFTAT folder");
        }

        /// <summary>Takes the launcher's files back out of the STRAFTAT folder, restoring any it replaced.</summary>
        public void RemoveGameFiles()
        {
            if (State.GameFilesDir == null) return;
            foreach (var file in State.GameFiles)
            {
                var target = Path.Combine(State.GameFilesDir, file.Name);
                var backup = Path.Combine(paths.Backup, file.Name);
                if (file.WasThere || (File.Exists(target) && Sources.Sha256(target) != file.Sha256)) continue;
                if (file.BackedUp && File.Exists(backup))
                {
                    File.Copy(backup, target, overwrite: true);
                    log($"Restored STRAFTAT's original {file.Name}");
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                    log($"Removed {file.Name} from the STRAFTAT folder");
                }
            }
            State.GameFiles.Clear();
            State.GameFilesDir = null;
            DeleteDirectory(paths.Backup);
        }

        /// <summary>Removes everything the launcher installed. With keepConfig, the mod's settings stay; with keepAssets,
        /// the built L4D2 assets and the tools stay too (they take a while to rebuild).</summary>
        public void Uninstall(bool keepConfig, bool keepAssets)
        {
            LoadState();
            RemoveGameFiles();
            string savedConfig = null;
            if (keepConfig && File.Exists(paths.ModConfig)) savedConfig = File.ReadAllText(paths.ModConfig);
            DeleteDirectory(paths.Profile);
            DeleteDirectory(paths.Kit);
            DeleteDirectory(paths.Cache);
            if (!keepAssets)
            {
                DeleteDirectory(paths.Assets);
                DeleteDirectory(paths.Tools);
            }
            if (savedConfig != null)
            {
                Directory.CreateDirectory(paths.Config);
                File.WriteAllText(paths.ModConfig, savedConfig);
            }
            var keep = new LauncherState
            {
                StraftatPath = State.StraftatPath, L4D2Path = State.L4D2Path, UnityPath = State.UnityPath,
                AssetsBuiltWith = keepAssets ? State.AssetsBuiltWith : null,
                Installed = keepAssets ? State.Installed.Where(f => f != LauncherConfig.BepInEx.File).ToList() : new System.Collections.Generic.List<string>(),
            };
            State = keep;
            SaveState();
            log("Uninstalled." + (keepConfig ? " Settings kept." : "") + (keepAssets ? " L4D2 assets and tools kept." : ""));
        }

        private static string Sha256(byte[] content)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
        }

        internal static void DeleteDirectory(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
