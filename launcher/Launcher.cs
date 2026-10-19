using System;
using System.IO;
using System.Threading.Tasks;

namespace Straft4Dead.Launcher
{
    /// <summary>What the window and the command line both do: find the games, install, set up the tools, build the
    /// L4D2 assets, play, update and uninstall.</summary>
    public sealed class Launcher : IDisposable
    {
        private readonly Action<string> log;

        public Launcher(LauncherPaths paths, Action<string> log, string straftatOverride = null, string l4d2Override = null)
        {
            Paths = paths;
            this.log = message =>
            {
                try
                {
                    Directory.CreateDirectory(paths.Home);
                    File.AppendAllText(paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\r\n");
                }
                catch (IOException) { }
                log(message);
            };
            Installer = new Installer(paths, this.log);
            Sources = new Sources(paths, this.log);
            Tools = new Tools(paths, this.log);
            Assets = new AssetBuild(paths, this.log);
            Installer.LoadState();
            if (straftatOverride != null) StraftatOverride = Path.GetFullPath(straftatOverride);
            if (l4d2Override != null) L4D2Override = Path.GetFullPath(l4d2Override);
        }

        public LauncherPaths Paths { get; }
        public Installer Installer { get; }
        public Sources Sources { get; }
        public Tools Tools { get; }
        public AssetBuild Assets { get; }
        public LauncherState State => Installer.State;
        public string SteamPath { get; private set; }
        public string StraftatPath { get; private set; }
        public string L4D2Path { get; private set; }
        public string UnityPath { get; private set; }
        public string StraftatOverride { get; }
        public string L4D2Override { get; }
        public ModRelease Latest { get; private set; }
        public string UpdateCheckError { get; private set; }

        public void Dispose() => Sources.Dispose();

        public void Find()
        {
            Installer.LoadState();
            SteamPath = Steam.FindSteam();
            StraftatPath = Steam.IsStraftatFolder(StraftatOverride) ? StraftatOverride
                : Steam.IsStraftatFolder(State.StraftatPath) ? State.StraftatPath : Steam.FindStraftat(SteamPath);
            L4D2Path = Steam.IsL4D2Folder(L4D2Override) ? L4D2Override
                : Steam.IsL4D2Folder(State.L4D2Path) ? State.L4D2Path : Steam.FindL4D2(SteamPath);
            UnityPath = Tools.FindUnity(State.UnityPath);
        }

        // --- what's done ---
        public bool ToolsReady => Tools.HasPython && Tools.HasBlender && Tools.HasCrowbar;
        public bool UnityReady => UnityPath != null && Tools.HasUnityLicence;
        public bool AssetsReady => Assets.AssetsPresent && File.Exists(Paths.ModConfig);
        public bool ReadyToPlay => StraftatPath != null && State.IsInstalled && AssetsReady;

        public async Task CheckForUpdates()
        {
            try
            {
                Latest = await Sources.LatestModRelease();
                UpdateCheckError = Latest == null ? $"No Straft 4 Dead release found on GitHub ({LauncherConfig.GitHubRepo}): the repository is private or has no release yet." : null;
            }
            catch (Exception e)
            {
                Latest = null;
                UpdateCheckError = $"Couldn't check GitHub for updates: {e.GetBaseException().Message}";
            }
            if (UpdateCheckError != null) log(UpdateCheckError);
        }

        public Version InstalledVersion => Sources.ParseVersion(State.ModVersion);
        public bool UpdateAvailable => Latest != null && (InstalledVersion == null || Latest.Version > InstalledVersion);
        public bool LauncherUpdateAvailable => Latest?.LauncherUrl != null && Latest.Version > LauncherConfig.LauncherVersion;

        public async Task InstallLatest()
        {
            EnsureGameClosed();
            if (Latest == null) await CheckForUpdates();
            if (Latest == null) throw new InvalidOperationException(UpdateCheckError ?? "No Straft 4 Dead release found.");
            var zip = await Sources.DownloadModZip(Latest);
            await Installer.Install(zip, Sources);
        }

        public async Task InstallFromZip(string zip)
        {
            EnsureGameClosed();
            await Installer.Install(Path.GetFullPath(zip), Sources);
        }

        /// <summary>Downloads Python, Blender + SourceIO and Crowbar into tools\ if they're missing.</summary>
        public Task SetUpTools() => Tools.EnsurePortable(Sources, State, Installer.SaveState);

        /// <summary>Builds the L4D2 assets from the player's install (needs the tools, Unity and the mod's kit).</summary>
        public async Task BuildAssets(Action<int, string> progress, bool force = false)
        {
            EnsureGameClosed();
            if (StraftatPath == null) throw new InvalidOperationException("STRAFTAT wasn't found.");
            if (L4D2Path == null) throw new InvalidOperationException("Left 4 Dead 2 wasn't found. It's needed: the mod builds its zombies and survivors from your copy.");
            if (!ToolsReady) await SetUpTools();
            if (!UnityReady) throw new InvalidOperationException("Unity isn't ready yet: install Unity Hub, sign in, and install Unity " + LauncherConfig.UnityVersion + ".");
            await Assets.Run(StraftatPath, L4D2Path, UnityPath, progress, force);
            State.AssetsBuiltWith = State.ModVersion;
            Installer.SaveState();
        }

        /// <summary>Everything that can happen without the player: install the mod, the tools, and (when Unity is
        /// ready) the asset build. Returns what's still missing, or null when it's ready to play.</summary>
        public async Task<string> SetUpEverything(string zip, Action<int, string> progress)
        {
            Find();
            if (StraftatPath == null) return "STRAFTAT wasn't found.";
            if (L4D2Path == null) return "Left 4 Dead 2 wasn't found.";
            if (!State.IsInstalled) { if (zip != null) await InstallFromZip(zip); else await InstallLatest(); }
            if (!ToolsReady) await SetUpTools();
            Find();
            if (!UnityReady) return "unity";
            if (!AssetsReady) await BuildAssets(progress);
            return null;
        }

        public void Play(bool dryRun = false)
        {
            if (StraftatPath == null) throw new InvalidOperationException("STRAFTAT wasn't found.");
            EnsureGameClosed();
            var args = Installer.PrepareGameFolder(StraftatPath);
            if (dryRun)
            {
                log($"Dry run, not starting STRAFTAT. Steam arguments: -applaunch {LauncherConfig.StraftatAppId} {args}");
                return;
            }
            log("Starting STRAFTAT with Straft 4 Dead");
            Steam.LaunchStraftat(SteamPath, args);
        }

        public void Uninstall(bool keepConfig, bool keepAssets)
        {
            EnsureGameClosed();
            Installer.Uninstall(keepConfig, keepAssets);
        }

        public void SetStraftatPath(string path) { Installer.LoadState(); State.StraftatPath = path; Installer.SaveState(); Find(); }
        public void SetL4D2Path(string path) { Installer.LoadState(); State.L4D2Path = path; Installer.SaveState(); Find(); }
        public void SetUnityPath(string path) { Installer.LoadState(); State.UnityPath = path; Installer.SaveState(); Find(); }

        private static void EnsureGameClosed()
        {
            if (Steam.IsStraftatRunning()) throw new InvalidOperationException("STRAFTAT is running. Close it first.");
        }
    }
}
