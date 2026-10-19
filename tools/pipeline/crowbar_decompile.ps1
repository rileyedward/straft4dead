# Decompile one MDL (and its .ani) with Crowbar 0.74 without touching the GUI by hand.
# Crowbar has no CLI: launching it with an MDL path pre-fills the Decompile tab (OptionsAutoOpenMdlFileForDecompile),
# then this script presses the tab's "Decompile" button through Win32 BM_CLICK and closes Crowbar as soon as the button
# comes back (Crowbar greys it out while it works).
#
#   powershell -ExecutionPolicy Bypass -File tools\pipeline\crowbar_decompile.ps1 -Crowbar <Crowbar.exe> -Mdl <file.mdl> -OutDir <dir>
# Output folder is whatever "Crowbar Settings.xml" DecompileOutputFullPath says (set it to -OutDir beforehand).
param(
    [Parameter(Mandatory)] [string] $Crowbar,
    [Parameter(Mandatory)] [string] $Mdl,
    [Parameter(Mandatory)] [string] $OutDir,
    [int] $TimeoutSeconds = 600
)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class S4DWin32 {
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
}
"@

# Files written since the start (a -Force rebuild overwrites the same names, so counting files isn't enough).
$since = (Get-Date).AddSeconds(-1)
function Count-Output { (Get-ChildItem $OutDir -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $since } | Measure-Object).Count }

$clock = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process $Crowbar -ArgumentList "`"$Mdl`"" -PassThru

# Wait for the window and its Decompile button (the tab page is also named "Decompile", so match the button class).
$root = [System.Windows.Automation.AutomationElement]::RootElement
$byPid = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$p.Id)
$byName = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Decompile")
$button = $null
while (-not $button -and $clock.Elapsed.TotalSeconds -lt 60) {
    Start-Sleep -Milliseconds 250
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $byPid)
    if (-not $win) { continue }
    foreach ($b in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $byName)) {
        if ($b.Current.NativeWindowHandle -ne 0 -and $b.Current.ClassName -match "BUTTON" -and $b.Current.IsEnabled -and -not $b.Current.IsOffscreen) { $button = $b; break }
    }
}
if (-not $button) { Write-Error "Decompile button not found"; Stop-Process $p -ErrorAction SilentlyContinue; exit 1 }
[S4DWin32]::SendMessage([IntPtr]$button.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # BM_CLICK

# Crowbar greys the button out while it decompiles. Done = the button is back and new files exist; a decompile can
# finish before the first look, so "back" also counts once output has appeared.
$started = $clock.Elapsed.TotalSeconds
$sawBusy = $false
while ($clock.Elapsed.TotalSeconds - $started -lt $TimeoutSeconds) {
    Start-Sleep -Milliseconds 250
    $enabled = $button.Current.IsEnabled
    if (-not $enabled) { $sawBusy = $true; continue }
    if ((Count-Output) -gt 0 -and ($sawBusy -or $clock.Elapsed.TotalSeconds - $started -gt 2)) { break }
}
$p.CloseMainWindow() | Out-Null
if (-not $p.WaitForExit(3000)) { Stop-Process $p -ErrorAction SilentlyContinue }
$made = Count-Output
if ($made -le 0) { Write-Error "Crowbar wrote nothing for $Mdl"; exit 1 }
"decompiled files: $made ($([int]$clock.Elapsed.TotalSeconds) s)"
