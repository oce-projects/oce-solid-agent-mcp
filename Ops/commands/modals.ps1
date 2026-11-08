# modals.ps1 - lists the dialogs open in SOLIDWORKS (or in every process) WITHOUT closing anything.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version in the repository. It rewrites the maintainer's modal viewer
#         (its source was a session file and is lost); the behaviour is the one described in the
#         maintainer's notes: enumerate the visible windows of the process, print class, title and
#         the texts of the children, click nothing.
#
# When to use it: a bench, a probe or a bridge call that does not come back, and SOLIDWORKS that does
# not respond. A modal window (class #32770) blocks the COM call and leaves no trace in any log: an
# API that asks for a value (AddDimension opens "Modify"), an add-in message box (HTTP 401 of an
# expired token), the "window resources are very low" warning of a session open for days.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\modals.ps1 [-AllProcesses] [-AllWindows]
#   -AllWindows lists every visible top-level window, not only the dialogs.
#
# Exit codes: 0 no dialog, 1 at least one dialog listed, 2 SOLIDWORKS is not running.
param([switch] $AllProcesses, [switch] $AllWindows)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
if (-not $AllProcesses -and -not (Get-Process SLDWORKS -ErrorAction SilentlyContinue)) { Stop-Ops 2 'SOLIDWORKS is not running' }
Add-OpsWindows
if ($AllWindows) {
    $pids = if ($AllProcesses) { @() } else { @(Get-Process SLDWORKS | ForEach-Object { $_.Id }) }
    $windows = @([OpsWin]::Top([int[]]$pids))
}
else { $windows = @(Get-OpsDialogs -AllProcesses:$AllProcesses) }
foreach ($p in @(Get-Process SLDWORKS -ErrorAction SilentlyContinue)) {
    Write-Output ("SLDWORKS pid {0}: responding {1}, CPU {2:N1} s" -f $p.Id, $p.Responding, $p.CPU)
}
Write-Output "windows: $($windows.Count)"
foreach ($w in $windows) {
    Write-Output ("- pid {0} class {1} title '{2}'" -f $w.ProcessId, $w.Class, $w.Title)
    foreach ($t in $w.Texts) { Write-Output ("    text: " + ($t -replace '\s+', ' ')) }
    if ($w.ButtonTexts.Count -gt 0) { Write-Output ("    buttons: " + ($w.ButtonTexts -join ' | ')) }
}
if (@($windows | Where-Object { $_.Class -eq '#32770' }).Count -gt 0) { exit 1 }
exit 0
