using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Straft4Dead.Launcher
{
    /// <summary>
    /// The tools the L4D2 asset build needs. Python, Blender (with the SourceIO add-on) and Crowbar are portable, so the
    /// launcher downloads them into its own tools\ folder; nothing is installed system-wide. Unity can only be
    /// detected: it needs the player's own (free) Unity sign-in, so the launcher guides them through Unity Hub.
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

        /// <summary>Unity 2021.3.45f2's Unity.exe, from a folder picked by hand or wherever Unity Hub installs editors.</summary>
        public string FindUnity(string picked)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(picked)) candidates.Add(picked);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(programFiles, "Unity", "Hub", "Editor", LauncherConfig.UnityVersion, "Editor", "Unity.exe"));
            candidates.Add(Path.Combine(programFiles, $"Unity {LauncherConfig.UnityVersion}", "Editor", "Unity.exe"));
            // Unity Hub's "Installs location" setting, if it was changed.
            var secondary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub", "secondaryInstallPath.json");
            if (File.Exists(secondary))
            {
                var dir = File.ReadAllText(secondary).Trim().Trim('"').Replace("\\\\", "\\");
                if (dir.Length > 0) candidates.Add(Path.Combine(dir, LauncherConfig.UnityVersion, "Editor", "Unity.exe"));
            }
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>A Unity licence is activated on this PC (Unity Hub writes it when you sign in with a Personal licence).</summary>
        public static bool HasUnityLicence =>
            File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Unity", "Unity_lic.ulf"));

        public static string UnityHubExe
        {
            get
            {
                var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity Hub", "Unity Hub.exe");
                return File.Exists(exe) ? exe : null;
            }
        }

        /// <summary>Downloads and starts the Unity Hub installer (the player clicks through it).</summary>
        public async Task InstallUnityHub(Sources sources)
        {
            var setup = await sources.GetUnpinned(LauncherConfig.UnityHubSetupUrl, "UnityHubSetup.exe");
            log("Starting the Unity Hub installer");
            Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true });
        }

        /// <summary>Asks Unity Hub to install the exact editor version (it shows its own sign-in and install screens).</summary>
        public void InstallUnityEditor()
        {
            log($"Asking Unity Hub to install Unity {LauncherConfig.UnityVersion}");
            Steam.Open(LauncherConfig.UnityHubInstallLink);
        }
    }
}
