using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Straft4Dead.Launcher
{
    /// <summary>
    /// The tools the L4D2 asset build needs. Python, Blender (with the SourceIO add-on) and Crowbar are portable, so the
    /// launcher downloads them into its own tools\ folder; nothing is installed system-wide. Unity is installed
    /// with Unity's own installer; only its free licence needs the player, who signs in to Unity Hub.
    /// </summary>
    public sealed class Tools
    {
        private readonly LauncherPaths paths;
        private readonly Action<string> log;

        public Tools(LauncherPaths paths, Action<string> log)
        {
            this.paths = paths;
            this.log = log;
        }

        public bool HasPython => File.Exists(paths.PythonExe);
        public bool HasBlender => File.Exists(paths.BlenderExe) && File.Exists(Path.Combine(SourceIODir, "__init__.py"));
        public bool HasCrowbar => File.Exists(paths.CrowbarExe);

        /// <summary>Blender looks for add-ons in "portable" next to blender.exe when that folder exists.</summary>
        private string SourceIODir => Path.Combine(paths.BlenderDir, "portable", "scripts", "addons", "SourceIO");

        /// <summary>Downloads and unpacks whatever portable tool is missing.</summary>
        public async Task EnsurePortable(Sources sources, LauncherState state, Action save)
        {
            if (!HasPython || !state.Installed.Contains(LauncherConfig.Python.File))
            {
                var zip = await sources.Get(LauncherConfig.Python);
                Installer.DeleteDirectory(Path.GetDirectoryName(paths.PythonExe));
                Installer.ExtractAll(zip, Path.GetDirectoryName(paths.PythonExe));
                Mark(state, LauncherConfig.Python.File, save);
                log("Python ready");
            }
            if (!File.Exists(paths.BlenderExe) || !state.Installed.Contains(LauncherConfig.Blender.File))
            {
                var zip = await sources.Get(LauncherConfig.Blender);
                log("Unpacking Blender (a few hundred MB, this takes a minute)");
                Installer.DeleteDirectory(paths.BlenderDir);
                await Task.Run(() => Installer.ExtractAll(zip, paths.BlenderDir, stripPrefix: "blender-5.2.2-windows-x64/"));
                Mark(state, LauncherConfig.Blender.File, save);
                state.Installed.Remove(LauncherConfig.SourceIO.File); // the add-on went with the old folder
            }
            if (!HasBlender || !state.Installed.Contains(LauncherConfig.SourceIO.File))
            {
                var zip = await sources.Get(LauncherConfig.SourceIO);
                Installer.DeleteDirectory(SourceIODir);
                Installer.ExtractAll(zip, SourceIODir, stripPrefix: "SourceIO-master/");
                Mark(state, LauncherConfig.SourceIO.File, save);
                log("Blender and SourceIO ready");
            }
            if (!HasCrowbar || !state.Installed.Contains(LauncherConfig.Crowbar.File))
            {
                var archive = await sources.Get(LauncherConfig.Crowbar);
                var sevenZip = await sources.Get(LauncherConfig.SevenZip);
                var dir = Path.GetDirectoryName(paths.CrowbarExe);
                Directory.CreateDirectory(dir);
                var p = Process.Start(new ProcessStartInfo(sevenZip, $"x \"{archive}\" -o\"{dir}\" -y") { UseShellExecute = false, CreateNoWindow = true });
                p.WaitForExit();
                if (!HasCrowbar) throw new InvalidOperationException("Couldn't unpack Crowbar.");
                Mark(state, LauncherConfig.Crowbar.File, save);
                log("Crowbar ready");
            }
        }

        private static void Mark(LauncherState state, string file, Action save)
        {
            if (!state.Installed.Contains(file)) state.Installed.Add(file);
            save();
        }

        // --- Unity ---

        /// <summary>Unity 2021.3.45f2's Unity.exe: picked by hand, installed by the launcher, or wherever Unity Hub
        /// (the classic install or the Microsoft Store one) or Unity's own installer put it.</summary>
        public string FindUnity(string picked)
        {
            var v = LauncherConfig.UnityVersion;
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(picked)) candidates.Add(picked);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(UnityInstallDir, "Editor", "Unity.exe"));
            candidates.Add(Path.Combine(programFiles, "Unity", "Hub", "Editor", v, "Editor", "Unity.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Unity", "Editors", v, "Editor", "Unity.exe"));
            // Unity Hub's "Installs location" setting, if it was changed (the Store Hub keeps its settings elsewhere).
            foreach (var hubData in HubDataDirs)
            {
                var secondary = Path.Combine(hubData, "secondaryInstallPath.json");
                if (!File.Exists(secondary)) continue;
                var dir = File.ReadAllText(secondary).Trim().Trim('"').Replace("\\\\", "\\");
                if (dir.Length > 0) candidates.Add(Path.Combine(dir, v, "Editor", "Unity.exe"));
            }
            // Unity's installer registers an uninstaller named after the version.
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try
                {
                    using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = hklm.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Unity {v}");
                    if (key?.GetValue("DisplayIcon") is string icon) candidates.Add(icon.Trim('"').Split(',')[0]);
                }
                catch { }
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>Where the launcher installs Unity: the installer's own default folder.</summary>
        public static string UnityInstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity " + LauncherConfig.UnityVersion);

        /// <summary>A Unity licence is activated on this PC: Unity Hub writes one when you sign in and add the free
        /// Personal licence (the classic licence file, or the newer licensing client's).</summary>
        public static bool HasUnityLicence =>
            File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Unity", "Unity_lic.ulf")) ||
            File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "licenses", "UnityEntitlementLicense.xml"));

        /// <summary>Unity Hub's settings folders: the classic install's, and the Microsoft Store install's.</summary>
        private static IEnumerable<string> HubDataDirs
        {
            get
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub");
                if (StoreHubPackage is string package)
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", package, "LocalCache", "Roaming", "UnityHub");
            }
        }

        /// <summary>The Microsoft Store (MSIX) Unity Hub's package family name, if it's installed.</summary>
        private static string StoreHubPackage
        {
            get
            {
                var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                if (!Directory.Exists(packages)) return null;
                return Directory.GetDirectories(packages, "UnityTechnologies.UnityHub_*").Select(Path.GetFileName).FirstOrDefault();
            }
        }

        private static string ClassicHubExe
        {
            get
            {
                var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity Hub", "Unity Hub.exe");
                return File.Exists(exe) ? exe : null;
            }
        }

        public static bool HasUnityHub => ClassicHubExe != null || StoreHubPackage != null;

        /// <summary>Opens Unity Hub so the player can sign in, installing it first if it's missing.</summary>
        public async Task OpenUnityHub(Sources sources)
        {
            if (ClassicHubExe is string exe)
            {
                log("Opening Unity Hub");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                return;
            }
            if (StoreHubPackage is string package)
            {
                log("Opening Unity Hub");
                Process.Start(new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{package}!UnityHub") { UseShellExecute = true });
                return;
            }
            Exception last = null;
            foreach (var url in LauncherConfig.UnityHubSetupUrls)
                try
                {
                    var setup = await sources.GetUnpinned(url, "UnityHubSetup.exe");
                    log("Starting the Unity Hub installer. When it's done, open Unity Hub and sign in.");
                    Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true });
                    return;
                }
                catch (Exception e) { last = e; log($"  {new Uri(url).AbsolutePath} failed ({e.GetBaseException().Message})"); }
            log($"Couldn't download Unity Hub; opening {LauncherConfig.UnityDownloadPage} instead.");
            Steam.Open(LauncherConfig.UnityDownloadPage);
        }

        /// <summary>Downloads Unity's own installer for exactly this version and runs it silently into
        /// <see cref="UnityInstallDir"/> (Windows asks once for permission). The installer is deleted afterwards.</summary>
        public async Task InstallUnityEditor(Sources sources)
        {
            var setup = await sources.GetUnpinned(LauncherConfig.UnityEditorSetupUrl, $"UnitySetup64-{LauncherConfig.UnityVersion}.exe");
            log($"Installing Unity {LauncherConfig.UnityVersion} into {UnityInstallDir}. Windows asks for permission; this takes a few minutes.");
            Process process;
            try
            {
                process = Process.Start(new ProcessStartInfo(setup, $"/S /D={UnityInstallDir}") { UseShellExecute = true, Verb = "runas" });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException("Unity wasn't installed because Windows' permission prompt was declined. Press Install Unity to try again.");
            }
            await Task.Run(() => process.WaitForExit());
            try { File.Delete(setup); } catch { }
            if (!File.Exists(Path.Combine(UnityInstallDir, "Editor", "Unity.exe")))
                throw new InvalidOperationException($"Unity's installer finished (exit code {process.ExitCode}) but Unity.exe isn't in {UnityInstallDir}.");
            log($"Unity {LauncherConfig.UnityVersion} installed");
        }
    }
}
