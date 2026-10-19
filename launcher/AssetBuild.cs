using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Straft4Dead.Launcher
{
    /// <summary>
    /// Runs the release's setup script (kit\tools\setup\setup.ps1) with the launcher's tools and folders: it builds the
    /// L4D2 assets from the player's own install into assets\ and points the profile's mod config at them. Its
    /// "[ ok ]"/"[skip]" lines drive the progress bar.
    /// </summary>
    public sealed class AssetBuild
    {
        /// <summary>Steps setup.ps1 prints: 4 animation sets, commons, survivors, specials, HUD, 3 bundles.</summary>
        public const int Steps = 11;

        private readonly LauncherPaths paths;
        private readonly Action<string> log;

        public AssetBuild(LauncherPaths paths, Action<string> log)
        {
            this.paths = paths;
            this.log = log;
        }

        public bool AssetsPresent =>
            File.Exists(Path.Combine(paths.Assets, "bundles", "s4d_infected")) &&
            File.Exists(Path.Combine(paths.Assets, "bundles", "s4d_survivors")) &&
            File.Exists(Path.Combine(paths.Assets, "bundles", "s4d_specials")) &&
            File.Exists(Path.Combine(paths.Assets, "hud", "s_panel_coach_incap.png"));

        public async Task Run(string straftat, string l4d2, string unity, Action<int, string> progress, bool force = false)
        {
            if (!File.Exists(paths.SetupScript)) throw new InvalidOperationException("The setup kit is missing; reinstall Straft 4 Dead.");
            var args = new StringBuilder();
            args.Append($"-NoProfile -ExecutionPolicy Bypass -File \"{paths.SetupScript}\"");
            args.Append($" -L4D2 \"{l4d2}\" -Straftat \"{straftat}\" -Work \"{paths.Assets}\"");
            args.Append($" -Blender \"{paths.BlenderExe}\" -Crowbar \"{paths.CrowbarExe}\" -Unity \"{unity}\"");
            args.Append($" -Python \"{paths.PythonExe}\" -Config \"{paths.ModConfig}\"");
            if (force) args.Append(" -Force");

            var psi = new ProcessStartInfo("powershell.exe", args.ToString())
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            Directory.CreateDirectory(paths.Home);
            using var setupLog = new StreamWriter(paths.SetupLog, append: false, Encoding.UTF8) { AutoFlush = true };
            int done = 0;
            string lastError = null;
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var exited = new TaskCompletionSource<int>();
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (setupLog) setupLog.WriteLine(e.Data);
                var line = e.Data.Trim();
                if (line.StartsWith("[ ok ]") || line.StartsWith("[skip]")) { done++; progress(done, line.Substring(6).Trim()); }
                else if (line.StartsWith("[....]")) { log($"Building: {line.Substring(6).Trim()}"); progress(done, line.Substring(6).Trim() + "…"); }
                else if (line.StartsWith("Done.")) progress(Steps, "Done");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (setupLog) setupLog.WriteLine("ERR " + e.Data);
                if (e.Data.Trim().Length > 0) lastError = e.Data.Trim();
            };
            process.Exited += (s, e) => exited.TrySetResult(process.ExitCode);
            log("Building the L4D2 assets from your Left 4 Dead 2 install. The first time takes a while (Unity imports everything once).");
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var code = await exited.Task;
            process.WaitForExit();
            if (code != 0) throw new InvalidOperationException($"The asset build stopped: {lastError ?? "exit code " + code}. Details are in {paths.SetupLog}.");
            log("L4D2 assets built");
        }
    }
}
