# bench-status.ps1 - where a detached bench run is, and (with -Wait) waits ONCE until it is over.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version. It replaces the habit of reading the run journal every two
#         minutes: every look costs the agent context and does not make the bench faster. Wait once,
#         with a timeout below the limit of the tool call (55 minutes by default), and re-arm.
#         "Over" means the state file ends with "closed": a shell loop that only slept once said
#         "finished" while the bench was still running.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\bench-status.ps1 [-Out <file>]
#       [-Wait] [-TimeoutMinutes 55]
#   Without -Out it takes the newest *.state in the output folder of the configuration.
#
# It prints the state file, the last line of Bench\in-progress.txt and, when the run is closed, the
# summary lines of every round output ("N tests, N failed", "expectations verified").
# Before calling a run green, read Bench\report\last.json too: "cases", "family" and "batch" empty
# (a FULL run) and "failed" 0. A partial run once hid three red cases behind a green summary.
#
# Exit codes:
#   0  closed, every round exited 0
#   1  closed, a round did not exit 0
#   2  no state file
#   3  still running (with -Wait: the timeout was reached; call it again)
#   4  closed after a stall (the watchdog stopped a round)
param([string] $Out = '', [switch] $Wait, [int] $TimeoutMinutes = 55)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
if ($Out) { $state = "$Out.state" }
else {
    $newest = Get-ChildItem -LiteralPath $cfg.OutDir -Filter '*.state' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { Stop-Ops 2 "no *.state in $($cfg.OutDir)" }
    $state = $newest.FullName
    $Out = $state.Substring(0, $state.Length - '.state'.Length)
}
if (-not (Test-Path -LiteralPath $state)) { Stop-Ops 2 "no state file: $state" }

function Test-Closed { @(Get-Content -LiteralPath $state -ErrorAction SilentlyContinue | Where-Object { $_ -match '^closed ' }).Count -gt 0 }
$t0 = Get-Date
if ($Wait) {
    while (-not (Test-Closed) -and ((Get-Date) - $t0).TotalMinutes -lt $TimeoutMinutes) { Start-Sleep -Seconds 30 }
}
Write-Output "state: $state"
Get-Content -LiteralPath $state | ForEach-Object { '  ' + $_ }
$journal = Join-Path $script:RepoRoot 'Bench\in-progress.txt'
if (Test-Path -LiteralPath $journal) {
    $age = [int]((Get-Date) - (Get-Item -LiteralPath $journal).LastWriteTime).TotalMinutes
    $lastLine = Get-Content -LiteralPath $journal -Tail 1
    Write-Output ("run journal ({0} min ago): {1}" -f $age, $lastLine.Substring(0, [Math]::Min(200, $lastLine.Length)))
}
if (-not (Test-Closed)) { Write-Output 'still running'; exit 3 }

$lines = @(Get-Content -LiteralPath $state)
$ends = @($lines | Where-Object { $_ -match '^end round \d+ .* exit (-?\d+)$' })
$stalled = @($lines | Where-Object { $_ -match '^stalled ' }).Count -gt 0
$outs = @(Get-ChildItem -Path ((Split-Path $Out) + '\' + [IO.Path]::GetFileNameWithoutExtension($Out) + '*') -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.log', '.txt' -and $_.Name -notmatch '_run\.txt$|_addin\.txt$|\.err$' })
foreach ($o in $outs) {
    Select-String -LiteralPath $o.FullName -Pattern 'tests, \d+ failed|expectations verified|BENCH NOT STARTED|THE CALL' |
        Select-Object -Last 3 | ForEach-Object { '  ' + $o.Name + ': ' + $_.Line.Trim() }
}
if ($stalled) { exit 4 }
if ($ends.Count -eq 0) { exit 1 }
foreach ($e in $ends) { if ($e -notmatch ' exit 0$') { exit 1 } }
exit 0
