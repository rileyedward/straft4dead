# Straft 4 Dead first-run setup: builds every L4D2 asset the mod uses from YOUR OWN Left 4 Dead 2 install, locally,
# and points the mod's config at them. Nothing from Valve is downloaded or shipped; it all stays on this PC.
#
#   powershell -ExecutionPolicy Bypass -File tools\setup\setup.ps1 `
#       -L4D2 "C:\Program Files (x86)\Steam\steamapps\common\Left 4 Dead 2" `
#       -Straftat "C:\Program Files (x86)\Steam\steamapps\common\STRAFTAT" `
#       -Work "C:\Users\you\Straft4DeadAssets" `
#       -Blender "C:\Tools\blender\blender.exe" -Crowbar "C:\Tools\Crowbar\Crowbar.exe" `
#       -Unity "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe"
#
# Needs: Python 3, Blender (with the SourceIO add-on), Crowbar 0.74, Unity 2021.3 (a free Personal licence), and
# BepInEx 5 + the Straft 4 Dead plugin already in STRAFTAT. Close STRAFTAT first (it locks the bundles).
# Crowbar has no command line: this script points its Decompile output folder (%APPDATA%\ZeqMacaw\Crowbar 0.74\
# Crowbar Settings.xml) at <Work>\crowbar before decompiling. The launcher runs this script for players; -Config
# says which BepInEx config to write (default: the one in the STRAFTAT folder).
# Steps already done are skipped; pass -Force to redo everything.
param(
    [Parameter(Mandatory)] [string] $L4D2,
    [Parameter(Mandatory)] [string] $Straftat,
    [Parameter(Mandatory)] [string] $Work,
    [Parameter(Mandatory)] [string] $Blender,
    [Parameter(Mandatory)] [string] $Crowbar,
    [Parameter(Mandatory)] [string] $Unity,
    [string] $Python = "python",
    [string] $Config,
    [switch] $Force
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$pipe = "$repo\tools\pipeline"
$game = "$L4D2\left4dead2"
$bundles = "$Work\bundles"
New-Item -ItemType Directory -Force $Work, $bundles, "$Work\crowbar" | Out-Null

# Crowbar reads its options from its settings file: decompile into <Work>\crowbar and open MDLs given on the command
# line in the Decompile tab (crowbar_decompile.ps1 then presses the button).
function Set-CrowbarOutput($out) {
    $dir = Join-Path $env:APPDATA "ZeqMacaw\Crowbar 0.74"
    $file = Join-Path $dir "Crowbar Settings.xml"
    # Crowbar crashes on a settings file without its own defaults (the game list), so on a PC that has never run it,
    # open and close it once to let it write them.
    if (-not (Test-Path $file) -or -not (Select-String $file -Pattern "<GameSetups>" -Quiet)) {
        if (Test-Path $file) { Remove-Item $file }
        $p = Start-Process $Crowbar -PassThru
        for ($i = 0; $i -lt 120 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
        Start-Sleep -Seconds 1
        $p.CloseMainWindow() | Out-Null
        if (-not $p.WaitForExit(10000)) { Stop-Process $p -ErrorAction SilentlyContinue }
        if (-not (Test-Path $file)) { throw "Crowbar didn't write its settings file ($file)." }
    }
    [xml]$xml = Get-Content $file -Raw
    $values = [ordered]@{
        DecompileOutputFolderOption = "WorkFolder"; DecompileOutputFullPath = $out; DecompileQcFileIsChecked = "true"
        OptionsAutoOpenMdlFileIsChecked = "true"; OptionsAutoOpenMdlFileForDecompileIsChecked = "true"; OptionsAutoOpenMdlFileOption = "Decompile"
    }
    foreach ($k in $values.Keys) {
        $node = $xml.AppSettings.SelectSingleNode($k)
        if (-not $node) { $node = $xml.CreateElement($k); $xml.AppSettings.AppendChild($node) | Out-Null }
        $node.InnerText = $values[$k]
    }
    $xml.Save($file)
}

function Step($name, [scriptblock] $done, [scriptblock] $run) {
    if (-not $Force -and (& $done)) { Write-Host "[skip] $name"; return }
    Write-Host "[....] $name"
    & $run
    if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) { throw "$name failed (exit $LASTEXITCODE)" }
    Write-Host "[ ok ] $name"
}

foreach ($check in @($game, $Blender, $Crowbar, $Unity, $Straftat)) {
    if (-not (Test-Path $check)) { throw "Not found: $check" }
}
if (Get-Process STRAFTAT -ErrorAction SilentlyContinue) { throw "Close STRAFTAT first." }

# 1. Animation sets need Crowbar (SourceIO's own L4D2 animation import is broken).
Set-CrowbarOutput "$Work\crowbar"
foreach ($anim in "anim_common", "anim_hulk", "anim_boomer", "anim_witch") {
    Step "decompile $anim" { Test-Path "$Work\crowbar\${anim}_anims\*.smd" } {
        & $Python -I "$pipe\vpk.py" extract "$game\pak01_dir.vpk" "$Work\extracted\l4d2" "models/infected/$anim."
        powershell -NoProfile -ExecutionPolicy Bypass -File "$pipe\crowbar_decompile.ps1" -Crowbar $Crowbar -Mdl "$Work\extracted\l4d2\models\infected\$anim.mdl" -OutDir "$Work\crowbar"
    }
}

# 2. Models, clips and textures -> FBX/PNG (Blender + SourceIO).
Step "common infected" { Test-Path "$Work\fbx\infected_anim\common_anims.fbx" } { & $Python -I "$pipe\build_infected.py" $game $Work $Blender }
Step "survivors"       { Test-Path "$Work\fbx\survivor_coach\arms.fbx" }        { & $Python -I "$pipe\build_survivors.py" $game $Work $Blender }
Step "specials"        { Test-Path "$Work\fbx\special_witch\anims.fbx" }        { & $Python -I "$pipe\build_specials.py" $game $Work $Blender }
Step "HUD textures"    { Test-Path "$Work\hud\s_panel_coach_incap.png" }        { & $Python -I "$pipe\build_hud.py" $game $Work $Blender }

# 3. AssetBundles (Unity, with graphics: -nographics builds custom shaders without GPU programs).
foreach ($b in @(@("S4DInfectedBuilder", "s4d_infected"), @("S4DSurvivorBuilder", "s4d_survivors"), @("S4DSpecialBuilder", "s4d_specials"))) {
    Step "bundle $($b[1])" { Test-Path "$bundles\$($b[1])" } {
        $p = Start-Process $Unity -Wait -PassThru -ArgumentList @("-batchmode", "-quit", "-projectPath", "`"$repo\unity\S4DBundles`"",
            "-executeMethod", "$($b[0]).Build", "-s4dSource", "`"$Work\fbx`"", "-s4dOut", "`"$bundles`"", "-logFile", "`"$Work\unity-$($b[1]).log`"")
        if ($p.ExitCode -ne 0) { throw "Unity build of $($b[1]) failed, see $Work\unity-$($b[1]).log" }
    }
}

# 4. Point the mod at everything.
$cfg = if ($Config) { $Config } else { "$Straftat\BepInEx\config\dev.riley.straft4dead.cfg" }
& $Python -I "$repo\tools\dev\set_cfg.py" $cfg `
    "Zombies.InfectedBundle=$bundles\s4d_infected" "Survivors.Bundle=$bundles\s4d_survivors" "Specials.Bundle=$bundles\s4d_specials" `
    "HUD.TextureDir=$Work\hud" "HUD.L4D2SoundDir=$game\sound"
Write-Host "Done. Start STRAFTAT; F9 opens the Straft 4 Dead panel."
