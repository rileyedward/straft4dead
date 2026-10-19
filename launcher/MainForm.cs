using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Straft4Dead.Launcher
{
    /// <summary>The launcher window: a checklist of what's needed, one main button (Set up → Play), the Unity helper,
    /// a progress bar and a log.</summary>
    public sealed class MainForm : Form
    {
        private readonly Launcher launcher;
        private readonly string zipOverride;

        private readonly Label[] stepLabels = new Label[6];
        private readonly Label updateLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        private readonly Button mainButton = new Button { Width = 170, Height = 42, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
        private readonly Button updateButton = new Button { Text = "Update", Width = 90, Height = 42 };
        private readonly Button rebuildButton = new Button { Text = "Rebuild assets", Width = 110, Height = 42 };
        private readonly Button uninstallButton = new Button { Text = "Uninstall", Width = 90, Height = 42 };
        private readonly GroupBox unityBox = new GroupBox { Text = "Unity needs you once", Width = 600, Height = 140, Visible = false };
        private readonly Button editorButton = new Button { Width = 200, Height = 30 };
        private readonly Button hubButton = new Button { Width = 200, Height = 30 };
        private readonly Label unityText = new Label { AutoSize = false, Width = 580, Height = 52 };
        // While the Unity box is up, notices the sign-in finishing in Unity Hub and carries on by itself.
        private readonly Timer unityWatch = new Timer { Interval = 3000 };
        private readonly ProgressBar progressBar = new ProgressBar { Width = 600, Height = 16, Maximum = AssetBuild.Steps };
        private readonly Label progressLabel = new Label { AutoSize = true };
        private readonly TextBox logBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, MinimumSize = new Size(0, 200),
            BackColor = SystemColors.Window, Font = new Font(FontFamily.GenericMonospace, 8.5f),
        };
        private bool busy;

        public MainForm(LauncherPaths paths, string straftatOverride, string l4d2Override, string zipOverride)
        {
            this.zipOverride = zipOverride == null ? null : Path.GetFullPath(zipOverride);
            launcher = new Launcher(paths, Log, straftatOverride, l4d2Override);
            launcher.Sources.Progress = (file, done, total) => BeginInvoke(new Action(() =>
                progressLabel.Text = total > 0 ? $"Downloading {file}: {done * 100 / total}% of {total / (1 << 20)} MB" : $"Downloading {file}: {done / (1 << 20)} MB"));

            Text = $"Straft 4 Dead Launcher {LauncherConfig.LauncherVersion.ToString(3)}";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 600);
            MinimumSize = new Size(560, 520);
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label { Text = "Straft 4 Dead", AutoSize = true, Font = new Font("Segoe UI", 18f, FontStyle.Bold) };
            var subtitle = new Label { Text = "Left 4 Dead 2 inside STRAFTAT", AutoSize = true, ForeColor = SystemColors.GrayText };
            var steps = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
            steps.Controls.AddRange(new Control[] { title, subtitle });
            for (int i = 0; i < stepLabels.Length; i++)
            {
                stepLabels[i] = new Label { AutoSize = false, AutoEllipsis = true, Width = 600, Height = 20 };
                steps.Controls.Add(stepLabels[i]);
            }
            steps.Controls.Add(updateLabel);

            // Unity: the one step the launcher can't do alone.
            var checkButton = new Button { Text = "Check again", Width = 100, Height = 30 };
            var pickUnity = new LinkLabel { Text = "Pick Unity.exe…", AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            var unityButtons = new FlowLayoutPanel { AutoSize = true };
            unityButtons.Controls.AddRange(new Control[] { hubButton, editorButton, checkButton, pickUnity });
            var unityLayout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false };
            unityLayout.Controls.AddRange(new Control[] { unityText, unityButtons });
            unityBox.Controls.Add(unityLayout);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
            buttons.Controls.AddRange(new Control[] { mainButton, updateButton, rebuildButton, uninstallButton });

            var progress = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            progress.Controls.AddRange(new Control[] { progressBar, progressLabel });

            var zipLink = new LinkLabel { Text = "Install from zip…", AutoSize = true };
            var straftatLink = new LinkLabel { Text = "STRAFTAT folder…", AutoSize = true };
            var l4d2Link = new LinkLabel { Text = "L4D2 folder…", AutoSize = true };
            var folderLink = new LinkLabel { Text = "Open launcher folder", AutoSize = true };
            var releasesLink = new LinkLabel { Text = "Releases page", AutoSize = true };
            var links = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            links.Controls.AddRange(new Control[] { zipLink, straftatLink, l4d2Link, folderLink, releasesLink });

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
            for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(steps);
            layout.Controls.Add(unityBox);
            layout.Controls.Add(buttons);
            layout.Controls.Add(progress);
            layout.Controls.Add(links);
            layout.Controls.Add(logBox);
            Controls.Add(layout);

            mainButton.Click += async (s, e) => await Run(OnMain);
            updateButton.Click += async (s, e) => await Run(async () => { await Install(); await SetUp(); });
            rebuildButton.Click += async (s, e) => await Run(() => launcher.BuildAssets(OnProgress, force: true));
            uninstallButton.Click += async (s, e) => await Run(OnUninstall);
            editorButton.Click += async (s, e) => await Run(async () => { await launcher.Tools.InstallUnityEditor(launcher.Sources); await ContinueIfUnityReady(); });
            hubButton.Click += async (s, e) => await Run(() => launcher.Tools.OpenUnityHub(launcher.Sources));
            checkButton.Click += async (s, e) => await Run(async () =>
            {
                if (!await ContinueIfUnityReady())
                    Log(launcher.UnityPath == null ? $"Unity {LauncherConfig.UnityVersion} isn't installed yet (step 1)." : "No Unity licence yet: sign in to Unity Hub and add the free Personal licence (step 2).");
            });
            unityWatch.Tick += async (s, e) =>
            {
                if (busy || !unityBox.Visible || launcher.UnityPath == null || !Tools.HasUnityLicence) return;
                Log("Unity is ready.");
                await Run(() => ContinueIfUnityReady());
            };
            unityWatch.Start();
            pickUnity.LinkClicked += (s, e) => PickFile("Unity.exe|Unity.exe", "Pick Unity.exe (Unity " + LauncherConfig.UnityVersion + "\\Editor\\Unity.exe)", launcher.SetUnityPath);
            zipLink.LinkClicked += async (s, e) => await Run(OnInstallFromZip);
            straftatLink.LinkClicked += (s, e) => PickFolder("Pick the STRAFTAT folder (the one with STRAFTAT.exe)", Steam.IsStraftatFolder, launcher.SetStraftatPath);
            l4d2Link.LinkClicked += (s, e) => PickFolder("Pick the Left 4 Dead 2 folder (the one with left4dead2\\pak01_dir.vpk)", Steam.IsL4D2Folder, launcher.SetL4D2Path);
            folderLink.LinkClicked += (s, e) => { Directory.CreateDirectory(paths.Home); Steam.Open(paths.Home); };
            releasesLink.LinkClicked += (s, e) => Steam.Open(launcher.Latest?.PageUrl ?? LauncherConfig.ReleasesPage);
            Shown += async (s, e) => await Run(OnStart);
            FormClosed += (s, e) => launcher.Dispose();
        }

        private async Task OnStart()
        {
            launcher.Find();
            Log(launcher.StraftatPath != null ? $"STRAFTAT: {launcher.StraftatPath}" : "STRAFTAT wasn't found in your Steam libraries.");
            Log(launcher.L4D2Path != null ? $"Left 4 Dead 2: {launcher.L4D2Path}" : "Left 4 Dead 2 wasn't found in your Steam libraries.");
            if (zipOverride != null) Log($"Testing with a local zip: {zipOverride}");
            RefreshView();
            await launcher.CheckForUpdates();
        }

        private async Task OnMain()
        {
            launcher.Find();
            if (launcher.StraftatPath == null) { Steam.OpenStorePage(LauncherConfig.StraftatAppId); return; }
            if (launcher.L4D2Path == null) { Steam.OpenStorePage(LauncherConfig.L4D2AppId); return; }
            if (launcher.ReadyToPlay) { launcher.Play(); return; }
            await SetUp();
        }

        private async Task SetUp()
        {
            var missing = await launcher.SetUpEverything(zipOverride, OnProgress);
            if (missing == "unity")
            {
                Log("Everything else is ready. Unity needs you once: see the box above.");
                unityBox.Visible = true;
            }
            else if (missing != null) Log(missing);
            else
            {
                unityBox.Visible = false;
                Log("Ready! Press Play. To play with friends, host a private lobby in STRAFTAT and invite them through Steam (they need this launcher too).");
            }
        }

        /// <summary>Carries on with the setup once Unity is installed and licensed.</summary>
        private async Task<bool> ContinueIfUnityReady()
        {
            launcher.Find();
            if (!launcher.UnityReady) return false;
            await SetUp();
            return true;
        }

        private async Task Install()
        {
            if (zipOverride != null) await launcher.InstallFromZip(zipOverride);
            else await launcher.InstallLatest();
        }

        private void OnProgress(int step, string what)
        {
            if (InvokeRequired) { BeginInvoke(new Action<int, string>(OnProgress), step, what); return; }
            progressBar.Value = Math.Min(step, progressBar.Maximum);
            progressLabel.Text = $"Building L4D2 assets ({step}/{AssetBuild.Steps}): {what}";
        }

        private async Task OnInstallFromZip()
        {
            using var dialog = new OpenFileDialog { Filter = "Straft 4 Dead zip (*.zip)|*.zip", Title = "Pick a Straft 4 Dead release zip" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await launcher.InstallFromZip(dialog.FileName);
        }

        private Task OnUninstall()
        {
            var answer = MessageBox.Show(this,
                "Remove Straft 4 Dead and BepInEx from the launcher's folder, and the launcher's files from the STRAFTAT folder?\n\n" +
                "Keep the built L4D2 assets, the tools and your settings (so a reinstall is quick)?\n\nYes: keep them    No: delete everything",
                "Uninstall Straft 4 Dead", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.Cancel) launcher.Uninstall(keepConfig: answer == DialogResult.Yes, keepAssets: answer == DialogResult.Yes);
            return Task.CompletedTask;
        }

        private void PickFolder(string description, Func<string, bool> valid, Action<string> set)
        {
            using var dialog = new FolderBrowserDialog { Description = description };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!valid(dialog.SelectedPath)) { MessageBox.Show(this, "That isn't the right folder.", "Straft 4 Dead", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            set(dialog.SelectedPath);
            RefreshView();
        }

        private void PickFile(string filter, string title, Action<string> set)
        {
            using var dialog = new OpenFileDialog { Filter = filter, Title = title };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            set(dialog.FileName);
            RefreshView();
        }

        /// <summary>Runs an action with the buttons disabled, showing any error.</summary>
        private async Task Run(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            RefreshView();
            try
            {
                await action();
            }
            catch (Exception e)
            {
                var message = e.GetBaseException().Message;
                Log($"Error: {message}");
                MessageBox.Show(this, message, "Straft 4 Dead", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false;
                launcher.Installer.LoadState();
                launcher.Find();
                RefreshView();
            }
        }

        private static string Mark(bool ok) => ok ? "✔" : "•";

        private void RefreshView()
        {
            var state = launcher.State;
            stepLabels[0].Text = $"{Mark(launcher.StraftatPath != null)}  STRAFTAT: {launcher.StraftatPath ?? "not found (get it on Steam, or pick its folder below)"}";
            stepLabels[1].Text = $"{Mark(launcher.L4D2Path != null)}  Left 4 Dead 2: {launcher.L4D2Path ?? "not found (you need your own copy, the mod is built from it)"}";
            stepLabels[2].Text = $"{Mark(state.IsInstalled)}  Straft 4 Dead {(state.IsInstalled ? state.ModVersion + " and BepInEx installed" : "and BepInEx: will be installed")}";
            stepLabels[3].Text = $"{Mark(launcher.ToolsReady)}  Python, Blender + SourceIO, Crowbar: {(launcher.ToolsReady ? "ready" : "will be downloaded (portable, about 450 MB)")}";
            stepLabels[4].Text = $"{Mark(launcher.UnityReady)}  Unity {LauncherConfig.UnityVersion}: {(launcher.UnityReady ? "ready" : launcher.UnityPath != null ? "installed, sign in to Unity Hub for the free licence" : Tools.HasUnityLicence ? "signed in, Unity needs installing" : "needs you once (free Unity account)")}";
            stepLabels[5].Text = $"{Mark(launcher.AssetsReady)}  L4D2 zombies, survivors, sounds and HUD: {(launcher.AssetsReady ? "built" : "will be built from your L4D2 install")}";

            var latest = launcher.Latest;
            if (zipOverride != null) updateLabel.Text = $"Installing from {Path.GetFileName(zipOverride)}";
            else if (latest == null) updateLabel.Text = launcher.UpdateCheckError ?? "Checking GitHub for updates…";
            else if (launcher.UpdateAvailable && state.IsInstalled) updateLabel.Text = $"Straft 4 Dead {latest.Version.ToString(3)} is available";
            else updateLabel.Text = $"Latest on GitHub: {latest.Version.ToString(3)}";
            if (launcher.LauncherUpdateAvailable) updateLabel.Text += ". A newer launcher is on the Releases page.";

            mainButton.Text = launcher.StraftatPath == null ? "Get STRAFTAT" : launcher.L4D2Path == null ? "Get Left 4 Dead 2" : launcher.ReadyToPlay ? "Play" : "Set up";
            mainButton.Enabled = !busy;
            updateButton.Enabled = !busy && state.IsInstalled && (launcher.UpdateAvailable || zipOverride != null);
            rebuildButton.Enabled = !busy && state.IsInstalled && launcher.UnityReady;
            uninstallButton.Enabled = !busy && (state.IsInstalled || Directory.Exists(launcher.Paths.Profile));
            if (launcher.UnityReady) unityBox.Visible = false;
            else if (state.IsInstalled && launcher.ToolsReady) unityBox.Visible = true;
            RefreshUnityBox();
            UseWaitCursor = busy;
        }

        /// <summary>The Unity box: install Unity (the launcher does it), then sign in to Unity Hub for the free licence.</summary>
        private void RefreshUnityBox()
        {
            var installed = launcher.UnityPath != null;
            var licensed = Tools.HasUnityLicence;
            unityText.Text =
                "Unity packs the L4D2 models into files the game can load. It's free, and only needs you for two clicks:\n" +
                $"1. Install Unity {LauncherConfig.UnityVersion} (about 3 GB; Windows asks once for permission).\n" +
                "2. Sign in to Unity Hub with a free Unity account and add the free Personal licence. The launcher carries on by itself.";
            editorButton.Text = installed ? "✔ Unity installed" : $"1. Install Unity {LauncherConfig.UnityVersion}";
            editorButton.Enabled = !busy && !installed;
            hubButton.Text = licensed ? "✔ Signed in" : Tools.HasUnityHub ? "2. Open Unity Hub to sign in" : "2. Get Unity Hub and sign in";
            hubButton.Enabled = !busy && !licensed;
        }

        private void Log(string message)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), message); return; }
            logBox.AppendText(message + Environment.NewLine);
        }
    }
}
