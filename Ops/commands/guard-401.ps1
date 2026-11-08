# guard-401.ps1 - while a bench runs, closes ONLY the add-in dialogs of an expired token (HTTP 401).
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version in the repository. It rewrites the maintainer's 401 guard (its
#         source was a session file and is lost) from the behaviour written in the maintainer's notes:
#         close ONLY the dialogs whose title contains ModalTitle ("OpenPDM") and whose text contains
#         ModalText ("HTTP 401"), and write each one to a log. Everything else stays open.
#
# Why: on 26/9/2026 a full bench stopped (exit 5, "THE CALL DOES NOT COME BACK" on save_document,
# Shared\Leash.cs) after 623 green checks. The add-in loaded in SOLIDWORKS had an expired token and
# opened THREE modal message boxes at every save. A modal of an add-in blocks the COM call of the bench
# exactly like one of SOLIDWORKS. The real cure is the person signing in again, or SOLIDWORKS restarted
# with a fixed add-in; this guard only keeps a run alive meanwhile.
#
# How a dialog is closed: a click on its OK button (BM_CLICK, posted, never sent: a hung window must
# not hang the guard), or WM_CLOSE when it has no OK button.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\guard-401.ps1 [-Minutes 90]
#       [-IntervalS 5] [-Log <file>] [-Detach] [-DryRun]
#   -DryRun only writes what it WOULD close. -Detach starts itself hidden and returns.
#
# Exit codes: 0 at the end of the period (the log says how many were closed), 2 configuration error.
param([int] $Minutes = 90, [int] $IntervalS = 5, [string] $Log = '', [switch] $Detach, [switch] $DryRun)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
if (-not $cfg.ModalTitle -or -not $cfg.ModalText) { Stop-Ops 2 'ModalTitle and ModalText must both be set: the guard never closes on one condition alone' }
if (-not $Log) { $Log = Join-Path (New-OpsOutDir $cfg) 'guard-401.log' }
if ($Detach) {
    $a = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Minutes $Minutes -IntervalS $IntervalS -Log `"$Log`"" + $(if ($DryRun) { ' -DryRun' } else { '' })
    $p = Start-Process powershell -ArgumentList $a -WindowStyle Hidden -PassThru
    Write-Output "guard started: pid $($p.Id), log $Log"
    exit 0
}
function Add-Log([string]$t) { ("{0} {1}" -f (Get-Date -Format s), $t) | Add-Content -LiteralPath $Log -Encoding UTF8 }
Add-Log "start: $Minutes min, title '$($cfg.ModalTitle)' and text '$($cfg.ModalText)', dry run $([bool]$DryRun)"
$closed = 0
$seen = @{}
$t0 = Get-Date
while (((Get-Date) - $t0).TotalMinutes -lt $Minutes) {
    foreach ($w in @(Get-OpsDialogs)) {
        if ($w.Title -notlike "*$($cfg.ModalTitle)*") { continue }
        $all = (@($w.Title) + @($w.Texts)) -join ' '
        if ($all -notlike "*$($cfg.ModalText)*") { continue }
        $key = "$($w.Handle)"
        if ($DryRun) {
            if (-not $seen.ContainsKey($key)) { Add-Log ("would close: pid {0} '{1}' {2}" -f $w.ProcessId, $w.Title, (($w.Texts -join ' ') -replace '\s+', ' ')); $seen[$key] = $true }
            continue
        }
        $ok = -1
        for ($i = 0; $i -lt $w.ButtonTexts.Count; $i++) { if ($w.ButtonTexts[$i] -match '^\s*&?OK\s*$') { $ok = $i; break } }
        $how = if ($ok -ge 0) { 'OK clicked: ' + [OpsWin]::Click($w.Buttons[$ok]) } else { 'WM_CLOSE: ' + [OpsWin]::Close($w.Handle) }
        $closed++
        Add-Log ("closed #{0}: pid {1} '{2}' {3} ({4})" -f $closed, $w.ProcessId, $w.Title, (($w.Texts -join ' ') -replace '\s+', ' '), $how)
    }
    Start-Sleep -Seconds $IntervalS
}
Add-Log "end: $closed closed"
Write-Output "closed: $closed (log $Log)"
exit 0
