using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Straft4Dead.Launcher
{
    /// <summary>
    /// Straft4Dead-Launcher.exe                 opens the launcher window
    /// Straft4Dead-Launcher.exe &lt;command&gt;       runs one step without the window and exits:
    ///   status                                 what's found, installed and built
    ///   install                                install or update the mod from the latest GitHub release
    ///   setup                                  install, download the tools and build the L4D2 assets (Unity must be ready)
    ///   build [--force]                        (re)build the L4D2 assets
    ///   play [--dry-run]                       start STRAFTAT with Straft 4 Dead
    ///   uninstall [--keep-config] [--keep-assets]
    /// Options: --zip &lt;file&gt; (install this release zip instead of GitHub's), --home &lt;folder&gt; (instead of
    /// %APPDATA%\Straft4Dead), --straftat &lt;folder&gt;, --l4d2 &lt;folder&gt;, --unity &lt;Unity.exe&gt;.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string command = null, zip = null, home = null, straftat = null, l4d2 = null, unity = null;
            bool keepConfig = false, keepAssets = false, dryRun = false, force = false;
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i].ToLowerInvariant();
                switch (a)
                {
                    case "--zip": case "--home": case "--straftat": case "--l4d2": case "--unity":
                        if (i + 1 >= args.Length) return Fail($"{args[i]} needs a value.");
                        var value = args[++i];
                        if (a == "--zip") zip = value; else if (a == "--home") home = value; else if (a == "--straftat") straftat = value; else if (a == "--unity") unity = value; else l4d2 = value;
                        break;
                    case "--keep-config": keepConfig = true; break;
                    case "--keep-assets": keepAssets = true; break;
                    case "--dry-run": dryRun = true; break;
                    case "--force": force = true; break;
                    default:
                        if (command == null && !args[i].StartsWith("-")) command = a;
                        else return Fail($"Unknown argument: {args[i]}");
                        break;
                }
            }
            var paths = new LauncherPaths(home ?? LauncherPaths.DefaultHome);

            if (command == null)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(paths, straftat, l4d2, zip));
                return 0;
            }

            AttachConsole(-1);
            Console.WriteLine();
            using var launcher = new Launcher(paths, Console.WriteLine, straftat, l4d2);
            if (unity != null) launcher.SetUnityPath(System.IO.Path.GetFullPath(unity));
            try
            {
                Run(launcher, command, zip, keepConfig, keepAssets, dryRun, force).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.GetBaseException().Message}");
                return 1;
            }
        }

        private static async Task Run(Launcher launcher, string command, string zip, bool keepConfig, bool keepAssets, bool dryRun, bool force)
        {
            launcher.Find();
            void Progress(int step, string what) => Console.WriteLine($"  [{step}/{AssetBuild.Steps}] {what}");
            switch (command)
            {
                case "status":
                    Console.WriteLine($"Launcher:   {LauncherConfig.LauncherVersion.ToString(3)} (releases from {LauncherConfig.GitHubRepo})");
                    Console.WriteLine($"Home:       {launcher.Paths.Home}");
                    Console.WriteLine($"Steam:      {launcher.SteamPath ?? "not found"}");
                    Console.WriteLine($"STRAFTAT:   {launcher.StraftatPath ?? "not found"}");
                    Console.WriteLine($"L4D2:       {launcher.L4D2Path ?? "not found"}");
                    Console.WriteLine($"Mod:        {(launcher.State.IsInstalled ? launcher.State.ModVersion : "not installed")}");
                    Console.WriteLine($"Tools:      {(launcher.ToolsReady ? "ready" : "missing")}");
                    Console.WriteLine($"Unity:      {launcher.UnityPath ?? "not found"}{(Tools.HasUnityLicence ? "" : " (no licence: sign in to Unity Hub)")}");
                    Console.WriteLine($"L4D2 assets:{(launcher.AssetsReady ? " built" : " not built")}");
                    await launcher.CheckForUpdates();
                    if (launcher.Latest != null) Console.WriteLine($"GitHub:     {launcher.Latest.Version.ToString(3)}{(launcher.UpdateAvailable ? " (update available)" : "")}");
                    break;
                case "install":
                case "update":
                    if (zip != null) await launcher.InstallFromZip(zip); else await launcher.InstallLatest();
                    break;
                case "setup":
                    var missing = await launcher.SetUpEverything(zip, Progress);
                    if (missing == "unity") throw new InvalidOperationException($"Unity isn't ready: install Unity Hub, sign in, then install Unity {LauncherConfig.UnityVersion} ({LauncherConfig.UnityHubInstallLink}).");
                    if (missing != null) throw new InvalidOperationException(missing);
                    Console.WriteLine("Ready to play.");
                    break;
                case "build":
                    await launcher.BuildAssets(Progress, force);
                    break;
                case "play":
                    launcher.Play(dryRun);
                    break;
                case "uninstall":
                    launcher.Uninstall(keepConfig, keepAssets);
                    break;
                default:
                    throw new ArgumentException($"Unknown command '{command}'. Use status, install, setup, build, play or uninstall.");
            }
        }

        private static int Fail(string message)
        {
            AttachConsole(-1);
            Console.WriteLine();
            Console.WriteLine(message);
            return 2;
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);
    }
}
