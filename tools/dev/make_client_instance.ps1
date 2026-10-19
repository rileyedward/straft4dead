# Dev only: a second STRAFTAT install on the same PC for two-player testing without a second person.
#
#   powershell -ExecutionPolicy Bypass -File tools\dev\make_client_instance.ps1 -Straftat "<STRAFTAT dir>" -Out "<new folder>"
#
# STRAFTAT forces a single instance with a mutex named after the exe's path, so a second exe in another folder runs.
# The game data, Mono and BepInEx core/plugins are directory junctions to the real install (deploys reach both, nothing
# is copied but the small exe/dlls); BepInEx config, cache and logs are separate so each copy has its own role.
# Launch the copy with:  <Out>\STRAFTAT.exe --s4d-lan client   (and the Steam copy with --s4d-lan host, see DevNet.cs)
param(
    [Parameter(Mandatory)] [string] $Straftat,
    [Parameter(Mandatory)] [string] $Out
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Out, "$Out\BepInEx", "$Out\BepInEx\config", "$Out\BepInEx\cache" | Out-Null
foreach ($f in "STRAFTAT.exe", "UnityPlayer.dll", "winhttp.dll", "doorstop_config.ini", ".doorstop_version") {
    Copy-Item "$Straftat\$f" "$Out\$f" -Force
}
Set-Content "$Out\steam_appid.txt" "2386720" -NoNewline   # lets the exe start outside Steam's launcher
function Junction($link, $target) {
    if (-not (Test-Path $link)) { cmd /c mklink /J "$link" "$target" | Out-Null }
}
Junction "$Out\STRAFTAT_Data" "$Straftat\STRAFTAT_Data"
Junction "$Out\MonoBleedingEdge" "$Straftat\MonoBleedingEdge"
Junction "$Out\Straftat_SupporterEdition" "$Straftat\Straftat_SupporterEdition"
Junction "$Out\BepInEx\core" "$Straftat\BepInEx\core"
Junction "$Out\BepInEx\plugins" "$Straftat\BepInEx\plugins"
if (Test-Path "$Straftat\BepInEx\patchers") { Junction "$Out\BepInEx\patchers" "$Straftat\BepInEx\patchers" }
foreach ($c in Get-ChildItem "$Straftat\BepInEx\config" -Filter *.cfg) {
    if (-not (Test-Path "$Out\BepInEx\config\$($c.Name)")) { Copy-Item $c.FullName "$Out\BepInEx\config\" }
}
Write-Host "Client instance ready in $Out"
