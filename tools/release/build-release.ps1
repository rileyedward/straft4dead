# Build a Straft 4 Dead release zip: the plugin plus everything the player needs to build the L4D2 assets from their
# own install (setup script, asset pipeline, Unity bundle-builder project). No Valve or STRAFTAT files go in.
#
#   powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1 [-Downloads]
#
# Writes dist\Straft4Dead-<version>.zip and dist\Straft4Dead-Launcher.exe with their .sha256 files, and the folder
# "dist\Straft4Dead v<version> release" (upload\ = the 4 files to attach, release-notes.md = the steps and the
# description). -Downloads copies that folder to your Downloads folder.
param([switch] $Downloads)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
[xml]$proj = Get-Content "$repo\plugin\Straft4Dead.Plugin.csproj"
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

dotnet build "$repo\plugin" -c Release
if ($LASTEXITCODE -ne 0) { throw "plugin build failed" }
$dll = Get-ChildItem "$repo\plugin\bin\Release" -Recurse -Filter "Straft4Dead.Plugin.dll" | Select-Object -First 1
if (-not $dll) { throw "Straft4Dead.Plugin.dll not found after the build" }

$stage = Join-Path $env:TEMP "s4d-release-$version"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force "$stage\BepInEx\plugins\Straft4Dead", "$stage\tools", "$stage\unity\S4DBundles" | Out-Null
Copy-Item $dll.FullName "$stage\BepInEx\plugins\Straft4Dead\"
foreach ($t in "setup", "pipeline", "dev") { Copy-Item "$repo\tools\$t" "$stage\tools\$t" -Recurse }
# The bundle builder: editor scripts, shader and project settings only (Library/ is rebuilt by Unity).
foreach ($u in "Assets\Editor", "Assets\Shaders", "Packages", "ProjectSettings") {
    $src = "$repo\unity\S4DBundles\$u"
    if (Test-Path $src) { New-Item -ItemType Directory -Force (Split-Path "$stage\unity\S4DBundles\$u") | Out-Null; Copy-Item $src "$stage\unity\S4DBundles\$u" -Recurse }
}
Copy-Item "$repo\README.md" "$stage\"
if (Test-Path "$repo\LICENSE") { Copy-Item "$repo\LICENSE" "$stage\" }

New-Item -ItemType Directory -Force "$repo\dist" | Out-Null
$zip = "$repo\dist\Straft4Dead-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content "$zip.sha256" "$hash  Straft4Dead-$version.zip" -NoNewline
Remove-Item -Recurse -Force $stage
dotnet build "$repo\launcher" -c Release
if ($LASTEXITCODE -ne 0) { throw "launcher build failed" }
$exe = "$repo\dist\Straft4Dead-Launcher.exe"
Copy-Item "$repo\launcher\bin\Release\net48\Straft4Dead-Launcher.exe" $exe -Force
$exeHash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content "$exe.sha256" "$exeHash  Straft4Dead-Launcher.exe" -NoNewline

# The release folder: upload\ holds exactly the files to attach to the GitHub release, release-notes.md the rest.
$out = "$repo\dist\Straft4Dead v$version release"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force "$out\upload" | Out-Null
Copy-Item $exe, "$exe.sha256", $zip, "$zip.sha256" "$out\upload\"
$notes = @"
# Straft 4 Dead v$version

GitHub release, step by step:

1. Push main: ``git push``
2. Open https://github.com/rileyedward/straft4dead/releases/new
3. Tag: ``v$version`` (create it on main)
4. Title: ``Straft 4 Dead v$version``
5. Description: paste everything under the line below.
6. Attach the 4 files in the ``upload`` folder.
7. Publish release.

---

Left 4 Dead 2 inside STRAFTAT: hordes, the Tank, the Boomer and the Witch in STRAFTAT's arena fights, played as the L4D2 survivors on the L4D2 HUD.

**To play:** download ``Straft4Dead-Launcher.exe``, run it and press **Set up**, then **Play**. You need STRAFTAT and Left 4 Dead 2 on Steam and a free Unity account; the launcher walks you through the rest. To play with friends, start the game from the launcher, host a friends-only or private lobby and invite them through Steam. See the [README](https://github.com/rileyedward/straft4dead#getting-started).

| File | What it is |
|---|---|
| ``Straft4Dead-Launcher.exe`` | The launcher: sets everything up and starts the game. This is the only file players need |
| ``Straft4Dead-$version.zip`` | The mod itself; the launcher downloads it automatically. For manual installs |
| ``*.sha256`` | Checksums for both |

Windows may warn that the launcher is from an unknown publisher (it isn't signed): choose More info, then Run anyway.

This is a first test build: single player is tested, multiplayer has been tested on one PC but not yet over Steam with friends. No Left 4 Dead 2 or STRAFTAT files are included; the launcher builds them from your own install.
"@
Set-Content "$out\release-notes.md" $notes -Encoding UTF8
if ($Downloads) {
    $dl = Join-Path (Join-Path $env:USERPROFILE "Downloads") (Split-Path $out -Leaf)
    if (Test-Path $dl) { Remove-Item -Recurse -Force $dl }
    Copy-Item $out $dl -Recurse
    Write-Host "Copied to $dl"
}
Write-Host "Built $zip ($hash)"
