# The Launcher

`Straft4Dead-Launcher.exe` gets a player from nothing to playing in one window. It's a .NET Framework 4.8 WinForms app (built into Windows 10 and 11, so there's nothing to install first) in `launcher/`, and it doesn't reference the plugin.

## What Set up does

| Step | What happens |
|------|--------------|
| Find the games | Reads Steam's install path from the registry and its `libraryfolders.vdf`, then looks for STRAFTAT (app 2386720) and Left 4 Dead 2 (app 550). Either folder can be picked by hand if Steam's files don't list it |
| Install the mod | Unpacks BepInEx 5.4.23.5 into the profile and installs the newest Straft 4 Dead release from GitHub: the plugin into the profile, the asset kit (setup script, pipeline, bundle-builder project) into `kit\` |
| Get the tools | Downloads portable Python 3.12, Blender 5.2.2 with the SourceIO 5.5.4 add-on and Crowbar 0.74 into `tools\`. Every download is pinned to an exact URL and SHA-256 and cached, and Blender falls back to two mirrors |
| Unity | Unity's licence can't be scripted, so this step is guided: **Get Unity Hub** runs Unity's own installer, the player signs in (a free Personal licence), and **Install Unity 2021.3.45f2** opens `unityhub://2021.3.45f2/88f88f591b2e` so the Hub installs the exact version. **Check again** looks for the editor and for `C:\ProgramData\Unity\Unity_lic.ulf` |
| Build the assets | Runs `kit\tools\setup\setup.ps1` with the found paths and the portable tools, and turns its `[ ok ]` / `[skip]` lines into the progress bar. The bundles land in `assets\` and the mod's config is pointed at them |

When everything is ready, the main button becomes **Play**.

## Where it puts things

Everything lives in `%APPDATA%\Straft4Dead`:

```text
profile\      BepInEx, its doorstop files, the plugin and its config
kit\          the release's setup script, asset pipeline and bundle-builder project
tools\        portable Python, Blender (with SourceIO) and Crowbar
assets\       the L4D2 assets built from your install
cache\        pinned downloads
game-backup\  anything of STRAFTAT's that the launcher replaced
state.json    found paths, installed version, what's been built
launcher.log  everything the launcher did
setup.log     the last asset build's output
```

## How Play works

STRAFTAT's folder only gets BepInEx's `winhttp.dll` and a `doorstop_config.ini` that's switched **off**, so starting the game from Steam stays vanilla. Play starts STRAFTAT through Steam with doorstop arguments that turn it on for that launch and point it at the profile's BepInEx:

```text
steam.exe -applaunch 2386720 --doorstop-enabled true --doorstop-target-assembly "%APPDATA%\Straft4Dead\profile\BepInEx\core\BepInEx.Preloader.dll"
```

If STRAFTAT's folder already has its own BepInEx, the launcher leaves its files alone and relies on the arguments. Uninstall removes the two files (restoring anything it backed up) and the launcher's folder, optionally keeping the settings, the built assets and the tools.

## Updates

On start the launcher checks the newest GitHub release of `rileyedward/straft4dead` (`LauncherConfig.GitHubRepo`) and shows **Update** when it's newer than the installed mod. Updating replaces the plugin and the kit and keeps the config and the built assets; press **Rebuild assets** when a release's notes say the assets changed.

## Command line

Every step also runs without the window, which is how it's tested:

```bash
Straft4Dead-Launcher.exe status
Straft4Dead-Launcher.exe install [--zip Straft4Dead-0.8.0.zip]
Straft4Dead-Launcher.exe setup   [--zip <file>] [--unity <Unity.exe>]
Straft4Dead-Launcher.exe build   [--force]
Straft4Dead-Launcher.exe play    [--dry-run]
Straft4Dead-Launcher.exe uninstall [--keep-config] [--keep-assets]
```

`--home <folder>` uses another folder instead of `%APPDATA%\Straft4Dead`, and `--straftat`, `--l4d2` and `--unity` override what's found.

## Building it

```bash
dotnet build launcher -c Release
```

`tools\release\build-release.ps1` builds it with the plugin and puts `Straft4Dead-Launcher.exe` and its `.sha256` next to the release zip.
