# Build a Straft 4 Dead release zip: the plugin plus everything the player needs to build the L4D2 assets from their
# own install (setup script, asset pipeline, Unity bundle-builder project). No Valve or STRAFTAT files go in.
#
#   powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1 [-Downloads]
#
# Writes dist\Straft4Dead-<version>.zip and its .sha256, plus dist\Straft4Dead-Launcher.exe and its .sha256
# (all four copied to your Downloads folder too with -Downloads).
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
if ($Downloads) { Copy-Item $zip, "$zip.sha256", $exe, "$exe.sha256" (Join-Path $env:USERPROFILE "Downloads") }
Write-Host "Built $zip ($hash)"
