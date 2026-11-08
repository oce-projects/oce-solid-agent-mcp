# preflight.ps1 - read-only check of the machine before a build, a bench or a deploy.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version. Every line is a trap that already cost a run; the remedy is in
#         Ops\KNOWN-ERRORS.md under the same name.
#
# It changes nothing and attaches to nothing (it reads processes, files, the registry of scheduled
# tasks and the event logs). Each line starts with OK, WARN or BLOCK:
#   BLOCK  the operation named by -For cannot run as things are
#   WARN   it can run, but a known trap is armed (read the line before going on)
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\preflight.ps1 [-For build|contracts|bench|deploy]
#
# Exit codes: 0 only OK lines, 1 at least one WARN, 2 at least one BLOCK.
param([ValidateSet('build', 'contracts', 'bench', 'deploy')] [string] $For = 'bench')
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
$script:worst = 0
function Say([string]$level, [string]$text) {
    Write-Output ("{0,-5} {1}" -f $level, $text)
    $rank = @{ OK = 0; WARN = 1; BLOCK = 2 }[$level]
    if ($rank -gt $script:worst) { $script:worst = $rank }
}
$needsSw = $For -in 'bench'
$root = $script:RepoRoot

# --- repository -------------------------------------------------------------
$head = Get-OpsHead
$branch = Invoke-OpsGit @('rev-parse', '--abbrev-ref', 'HEAD')
$dirty = @(Invoke-OpsGit @('status', '--porcelain', '--untracked-files=no') | Where-Object { $_ }).Count
Say 'OK' "repository $root, branch $branch, head $head, VERSION.txt $(Get-OpsVersion)"
if ($dirty -gt 0) { Say $(if ($For -eq 'deploy') { 'BLOCK' } else { 'WARN' }) "$dirty tracked files changed (a deploy takes a commit, not a working tree)" }
if ($cfg.ConfigFile) { Say 'OK' "configuration: $($cfg.ConfigFile)" } else { Say 'WARN' 'no local configuration: copy Ops\ops.config.example.psd1 to Ops\ops.config.local.psd1' }

# --- tools ------------------------------------------------------------------
$msb = Find-OpsMSBuild
if ($msb) { Say 'OK' "MSBuild: $msb" } else { Say $(if ($For -ne 'bench') { 'BLOCK' } else { 'WARN' }) 'MSBuild 2022 not found (the bench builds unless -SkipCompilation)' }
foreach ($t in 'git', 'node') { if (Get-Command $t -ErrorAction SilentlyContinue) { Say 'OK' "$t found" } else { Say 'BLOCK' "$t not found" } }
if (-not (Test-Path -LiteralPath $cfg.SolidWorksInterop)) { Say 'BLOCK' "SOLIDWORKS interop not found: $($cfg.SolidWorksInterop) (the bridge does not build without it, and the contracts skip the bridge checks)" }
if ($env:OCE_NO_ATTACH) { Say $(if ($needsSw) { 'BLOCK' } else { 'WARN' }) "OCE_NO_ATTACH=$env:OCE_NO_ATTACH in this shell: a bench or a measurement started from here answers 'not attached' in a few ms" }

# --- SOLIDWORKS ---------------------------------------------------------------
$sw = @(Get-Process SLDWORKS -ErrorAction SilentlyContinue)
if ($sw.Count -eq 0) { Say $(if ($needsSw) { 'BLOCK' } else { 'OK' }) 'SOLIDWORKS is not running' }
elseif ($sw.Count -gt 1) { Say 'BLOCK' "$($sw.Count) SOLIDWORKS processes: the bench attaches to one session only" }
foreach ($p in $sw) {
    $hours = [math]::Round(((Get-Date) - $p.StartTime).TotalHours, 1)
    Say 'OK' ("SOLIDWORKS pid {0}, up {1} h, responding {2}, handles {3}, memory {4} MB" -f $p.Id, $hours, $p.Responding, $p.HandleCount, [int]($p.WorkingSet64 / 1MB))
    if (-not $p.Responding) { Say 'BLOCK' 'SOLIDWORKS does not respond: look for a modal (Ops\ops.ps1 modals) before anything else' }
    if ($hours -gt 24 -and $For -eq 'bench') { Say 'WARN' 'SOLIDWORKS has been up for more than a day: restart it before a full bench (short of window resources it skips rebuilds silently)' }
}
if ($sw.Count -gt 0) {
    $dialogs = @(Get-OpsDialogs)
    if ($dialogs.Count -gt 0) { Say 'BLOCK' ("{0} dialogs open in SOLIDWORKS: {1}" -f $dialogs.Count, (($dialogs | ForEach-Object { "'" + $_.Title + "'" }) -join ', ')) }
}

# --- bench ------------------------------------------------------------------
$journal = Join-Path $root 'Bench\in-progress.txt'
$benchProcs = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -match 'bench(-run)?\.ps1' })
if ($benchProcs.Count -gt 0) { Say 'BLOCK' "a bench is running (pid $(($benchProcs | ForEach-Object { $_.ProcessId }) -join ', ')): never two benches, never a build or a COM probe meanwhile" }
elseif (Test-Path -LiteralPath $journal) {
    $age = [int]((Get-Date) - (Get-Item -LiteralPath $journal).LastWriteTime).TotalMinutes
    Say 'OK' "no bench running (run journal written $age min ago)"
}
if ($For -eq 'bench') {
    $prof = if ($cfg.WeldmentProfiles) { $cfg.WeldmentProfiles } else { $env:OCE_WELDMENT_PROFILES }
    if (-not $prof) { Say 'BLOCK' 'no weld profile folder (WeldmentProfiles): the bench exits 2 within a minute' }
    else {
        $r = Test-OpsPathWithTimeout $prof 20
        if ($r -eq 'yes') { Say 'OK' "weld profiles readable: $prof" }
        elseif ($r -eq 'timeout') { Say 'BLOCK' "the weld profile folder does not answer within 20 s: the network share hiccups, the bench would hang on NewDocument" }
        else { Say 'BLOCK' "weld profile folder not found: $prof" }
    }
    if ($cfg.LocalCopiesShare) {
        $r = Test-OpsPathWithTimeout $cfg.LocalCopiesShare 20
        if ($r -ne 'yes') { Say 'WARN' "the share of the templates answers '$r': consider bench-run -LocalCopies" }
    }
}
$report = Join-Path $root 'Bench\report\last.json'
if (Test-Path -LiteralPath $report) {
    $rep = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    $full = ("$($rep.batch)$($rep.family)$($rep.cases)" -eq '')
    $line = "last bench report: commit $($rep.commit), $($rep.summary), full run $full"
    if ($For -eq 'deploy' -and (-not $full -or $rep.failed -ne 0 -or $rep.errors -ne 0)) { Say 'BLOCK' $line } else { Say 'OK' $line }
}

# --- session, reboots, scheduled cleanup ----------------------------------------------
$q = @(& qwinsta 2>$null | Where-Object { $_ -match "\b$([regex]::Escape($env:USERNAME))\b" })
if ($q.Count -gt 0) {
    $s = ($q[0] -replace '\s+', ' ').Trim()
    Say 'OK' "session: $s"
    if ($s -match '\bDisc\b') { Say 'WARN' 'the session is disconnected: drawing timings measured were about 3 times slower (cause not tested); do not compare timings across a change of state' }
}
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
Say 'OK' ("last boot {0:s}" -f $boot)
$crashes = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 41, 1001, 6008; StartTime = (Get-Date).AddDays(-1) } -ErrorAction SilentlyContinue)
if ($crashes.Count -gt 0) { Say 'WARN' "$($crashes.Count) unexpected shutdowns or blue screens in the last 24 h (Ops\ops.ps1 events)" }
if ($cfg.TempCleanupTask) {
    $task = Get-ScheduledTask -TaskName $cfg.TempCleanupTask -ErrorAction SilentlyContinue
    if ($task) {
        $info = $task | Get-ScheduledTaskInfo
        Say 'WARN' ("scheduled cleanup '{0}' empties %TEMP% (next run {1:s}): copies kept in %TEMP% must carry the time of now" -f $cfg.TempCleanupTask, $info.NextRunTime)
    }
}

# --- deploy -------------------------------------------------------------------
if ($cfg.DeployTarget) {
    $exe = Join-Path $cfg.DeployTarget 'oce-solid-agent-mcp.exe'
    $v = if (Test-Path -LiteralPath $exe) { (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion } else { 'missing' }
    $running = @(Get-Process oce-solid-agent-mcp -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($cfg.DeployTarget, [StringComparison]::OrdinalIgnoreCase) }).Count
    Say 'OK' "deployed bridge: $v, $running running from it (they keep their version until their MCP session restarts)"
}
elseif ($For -eq 'deploy') { Say 'BLOCK' 'DeployTarget is not set' }
exit $script:worst
