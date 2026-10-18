# Decompile one MDL (and its .ani) with Crowbar 0.74 without touching the GUI by hand.
# Crowbar has no CLI: launching it with an MDL path pre-fills the Decompile tab (OptionsAutoOpenMdlFileForDecompile),
# then this script presses the tab's "Decompile" button through Win32 BM_CLICK and waits for the output.
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

$before = (Get-ChildItem $OutDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
$p = Start-Process $Crowbar -ArgumentList "`"$Mdl`"" -PassThru
Start-Sleep -Seconds 6

$root = [System.Windows.Automation.AutomationElement]::RootElement
$byPid = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $byPid)
$byName = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Decompile")
$buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $byName)
$clicked = $false
foreach ($b in $buttons) {
    $h = [IntPtr]$b.Current.NativeWindowHandle
    if ($h -ne [IntPtr]::Zero -and $b.Current.ClassName -match "BUTTON" -and $b.Current.IsEnabled -and -not $b.Current.IsOffscreen) {  # the tab page is also named "Decompile"
        [S4DWin32]::SendMessage($h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # BM_CLICK
        $clicked = $true
        break
    }
}
if (-not $clicked) { Write-Error "Decompile button not found"; Stop-Process $p; exit 1 }

# Wait until the output stops growing.
$last = -1; $stable = 0; $t = 0
while ($t -lt $TimeoutSeconds) {
    Start-Sleep -Seconds 5; $t += 5
    $n = (Get-ChildItem $OutDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($n -gt $before -and $n -eq $last) { $stable++ } else { $stable = 0 }
    if ($stable -ge 3) { break }
    $last = $n
}
$p.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 2
if (-not $p.HasExited) { Stop-Process $p }
"decompiled files: $($last - $before)"
