# bench-run.ps1 - runs Bench\bench.ps1 detached, with a watchdog and a state file an agent can read.
#
# Version 1.1.0
# Changelog:
#   1.1.0 (16/11/2026) the relaunch of -Detach passes every value as one argument "-Name:value",
#         quoted with the rules of Windows (Join-OpsArguments, lib\Common.ps1). Measured before
#         the fix: -Root "C:\repo\" (trailing backslash) reached the hidden process as
#         'C:\repo" -Extra -Group' and stopped it with "no bench"; -Extra:-DryRun reached it
#         as -Extra -DryRun and failed with a missing argument. -Detach -DryRun now prints the
#         command line it would start and runs it in the foreground (it carries -DryRun): the
#         relaunch is tested without a bench.
#   1.0.0 (08/11/2026) ported from the maintainer's detached bench script 1.8.0 (03/11/2026), in
#         English, with the machine values moved to the local configuration, -Detach to start itself
#         hidden, a JSON result next to the state file, and -RestartSolidWorks that closes the test
#         leftovers first and refuses to restart over documents that are not the bench's.
#         Behaviour kept from 1.0.0-1.8.0 of the original, each one paid for:
#         - DETACHED: a tool call that waits on the bench dies with the call (a background job of
#           the tool is killed at one hour, and a PowerShell Start-Job dies with the call that
#           started it); the full bench lasts 60-70 minutes. Start it with -Detach.
#         - HIDDEN, NEVER MINIMIZED: a minimized launcher window was closed from the taskbar by the
#           person connected over remote desktop; the bench (hidden child) finished, but state,
#           copies and watchdog were lost.
#         - NEVER IN A PIPELINE: Select-Object -First or Tee-Object around bench.ps1 kill it halfway
#           (exit 255, no report). This script redirects to files instead.
#         - WATCHDOG: Bench\in-progress.txt unchanged for -StallMinutes = stalled, the round is
#           stopped (with its children).
#         - OCE_WELDMENT_PROFILES set for the bench process only (never setx): without it the bench
#           exits 2 within a minute and tests nothing.
#         - -LocalCopies mirrors the template folders of a network share with robocopy and sets
#           OCE_LOCAL_COPIES "share|copy" for the bench only (library 0.179.1 and later): every new
#           part reads a template, and a hiccup of the share hangs NewDocument. If one copy fails
#           the variable is NOT set and the state file says so.
#         - -Repeat N, -Variants "NAME=VALUE;NAME=VALUE|...": round i uses variant (i-1) mod count;
#           EXTRA=<arguments> in a variant is added to the bench arguments of that round; {round}
#           in a value becomes the output name of the round without extension.
#         - after every round: copy of Bench\in-progress.txt, of the SOLIDWORKS journal (swxJRNL.swj,
#           reset at every start of SOLIDWORKS) and of the add-in log (reset at every start), next
#           to the output; on exit 5 (a call that does not come back) a minidump of every live
#           SLDWORKS BEFORE any restart, so that what there is to look at survives.
#
# Usage (an agent always uses -Detach and then bench-status):
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\bench-run.ps1 -Detach
#       [-Out <file>] [-Extra "<bench.ps1 arguments>"] [-Profiles <folder>] [-StallMinutes 25]
#       [-Repeat N] [-Variants "<v1>|<v2>"] [-RestartSolidWorks] [-ContinueIfRed]
#       [-LocalCopies] [-Share <folder>] [-CopyRoot <folder>] [-CopyFolders "a|b"] [-DryRun]
#   -DryRun prints what it would run; with -Detach it also prints the hidden command line and
#   runs that line in the foreground. A value that is exactly the name of a parameter of THIS
#   script needs the form -Extra:-DryRun (powershell.exe -File reads a lone "-DryRun" as the
#   switch, and -Extra is left without a value); -Extra "-SkipCompilation" is fine.
#
# State file <Out>.state, one line per event, in this order:
#   start ... / copy ... / profiles ... / round i of N pid P ... / stalled ... / end round i ... exit N /
#   dump ... / journal copied ... / run journal copied ... / add-in log copied ... / error ... / closed ...
# "closed" is ALWAYS the last line: until it is there, the run is not over.
# Result file <Out>.result.json: rounds with exit code, stalled flag and output file.
#
# Exit codes (of the detached process; with -Detach the launcher exits 0 once started):
#   the exit code of the last round, 6 when a round stalled, 2 for a usage or configuration error.
param(
    [string] $Root = '',
    [string] $Out = '',
    [int] $StallMinutes = 0,
    [string] $Extra = '',
    [string] $Profiles = '',
    [int] $Repeat = 1,
    [switch] $LocalCopies,
    [string] $Share = '',
    [string] $CopyRoot = '',
    [string] $CopyFolders = '',
    [string] $Variants = '',
    [switch] $RestartSolidWorks,
    [switch] $ContinueIfRed,
    [switch] $Detach,
    [switch] $DryRun
)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
if (-not $Root) { $Root = $script:RepoRoot }
if (-not $Out) { $Out = Join-Path (New-OpsOutDir $cfg) ('bench_' + (Get-OpsStamp) + '.log') }
if ($StallMinutes -le 0) { $StallMinutes = [int]$cfg.StallMinutes }
$Profiles = Resolve-OpsValue $Profiles $cfg 'WeldmentProfiles'
if (-not $Profiles) { $Profiles = $env:OCE_WELDMENT_PROFILES }
if (-not $Profiles) { Stop-Ops 2 'no weld profile folder: set WeldmentProfiles in the local configuration (the bench exits 2 without OCE_WELDMENT_PROFILES)' }
$folders = @($CopyFolders -split '\|' | Where-Object { $_.Trim() })
if ($folders.Count -eq 0) { $folders = @($cfg.LocalCopiesFolders) }
if (-not $Share) { $Share = $cfg.LocalCopiesShare }
if (-not $CopyRoot) { $CopyRoot = $cfg.LocalCopiesRoot }
if ($LocalCopies -and (-not $Share -or $folders.Count -eq 0)) { Stop-Ops 2 '-LocalCopies needs LocalCopiesShare and LocalCopiesFolders (configuration) or -Share and -CopyFolders' }
$bench = Join-Path $Root 'Bench\bench.ps1'
if (-not (Test-Path -LiteralPath $bench)) { Stop-Ops 2 "no bench in $Root" }
$state = "$Out.state"

if ($Detach) {
    # The same parameters again, without -Detach. A value goes as ONE argument "-Name:value":
    # after a separate "-Extra", a value that is itself a parameter name ("-DryRun") is read
    # as a parameter and -Extra has no value (measured 16/11/2026), and a value written by
    # hand between quotes broke on a trailing backslash ("-Root "C:\repo\"" swallowed the
    # rest of the line). Join-OpsArguments (lib\Common.ps1) quotes with the rules of Windows.
    $argList = New-Object System.Collections.Generic.List[string]
    foreach ($a in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)) { $argList.Add($a) }
    foreach ($k in $PSBoundParameters.Keys) {
        if ($k -eq 'Detach') { continue }
        $v = $PSBoundParameters[$k]
        if ($v -is [System.Management.Automation.SwitchParameter]) { if ($v.IsPresent) { $argList.Add('-' + $k) } }
        else { $argList.Add('-' + $k + ':' + "$v") }
    }
    if (-not $PSBoundParameters.ContainsKey('Out')) { $argList.Add('-Out:' + $Out) }
    $line = Join-OpsArguments $argList
    $exe = Join-Path $PSHOME 'powershell.exe'
    if ($DryRun) {
        # The test of the relaunch without a bench: the same program and command line, in
        # the foreground; it carries -DryRun, so it only prints what it would run.
        Write-Ops "would start hidden: $exe $line"
        Write-Ops 'dry run: the same command line runs now in the foreground'
        exit (Invoke-OpsPowerShell $argList)
    }
    $p = Start-Process $exe -ArgumentList $line -WindowStyle Hidden -PassThru
    Write-Output "started: pid $($p.Id)"
    Write-Output "output: $Out"
    Write-Output "state:  $state"
    Write-Output "wait with: powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\bench-status.ps1 -Out `"$Out`" -Wait"
    exit 0
}

# variant -> list of (name, value) pairs; and every name, removed before each round.
$pairs = New-Object System.Collections.Generic.List[object]
$names = New-Object System.Collections.Generic.List[string]
foreach ($v in @($Variants -split '\|' | Where-Object { $_.Trim() })) {
    $one = New-Object System.Collections.Generic.List[object]
    foreach ($piece in ($v -split ';')) {
        if (-not $piece.Trim()) { continue }
        $kv = $piece -split '=', 2
        if ($kv.Count -ne 2) { Stop-Ops 2 "variant piece without '=': $piece" }
        $one.Add(@($kv[0].Trim(), $kv[1]))
        if (-not $names.Contains($kv[0].Trim())) { $names.Add($kv[0].Trim()) }
    }
    $pairs.Add($one)
}
if ($DryRun) {
    Write-Ops "would run $bench $Extra, $Repeat round(s), watchdog $StallMinutes min, profiles $Profiles"
    if ($pairs.Count -gt 0) { Write-Ops "variants: $($pairs.Count)" }
    if ($LocalCopies) { Write-Ops ("would mirror " + ($folders -join '; ') + " of $Share into $CopyRoot") }
    if ($RestartSolidWorks) { Write-Ops 'would close the leftovers and restart SOLIDWORKS before every round' }
    Write-Ops "output $Out, state $state"
    exit 0
}

function Add-State([string]$t) { $t | Add-Content -LiteralPath $state -Encoding UTF8 }
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class OpsHungDump
{
    [DllImport("dbghelp.dll", SetLastError = true)]
    static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
        uint dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
    // HandleData 0x4 | UnloadedModules 0x20 | ProcessThreadData 0x100 | FullMemoryInfo 0x800 | ThreadInfo 0x1000
    public static string Write(System.Diagnostics.Process p, string path)
    {
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            bool ok = MiniDumpWriteDump(p.Handle, (uint)p.Id, fs.SafeFileHandle, 0x4 | 0x20 | 0x100 | 0x800 | 0x1000,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return ok ? "written" : "NOT written, error " + Marshal.GetLastWin32Error();
        }
    }
}
'@
$runJournal = Join-Path $Root 'Bench\in-progress.txt'
$swJournal = Join-Path $env:APPDATA ("SolidWorks\" + $cfg.SolidWorksYearFolder + "\swxJRNL.swj")
$opsCommands = $PSScriptRoot
"start $(Get-Date -Format s) root $Root extra '$Extra' repeat $Repeat local-copies $([bool]$LocalCopies) variants '$Variants' restart $([bool]$RestartSolidWorks)" | Set-Content -LiteralPath $state -Encoding UTF8

Remove-Item Env:\OCE_LOCAL_COPIES -ErrorAction SilentlyContinue
Remove-Item Env:\OCE_NO_ATTACH -ErrorAction SilentlyContinue   # a bench with this set measures nothing
if ($LocalCopies) {
    $local = Join-Path $CopyRoot (Split-Path $Share -Leaf)
    $t0 = Get-Date; $ok = $true
    foreach ($f in $folders) {
        $null = & robocopy (Join-Path $Share $f) (Join-Path $local $f) /MIR /R:2 /W:2 /NP /NFL /NDL /NJH /NJS
        $rc = $LASTEXITCODE
        $n = @(Get-ChildItem (Join-Path $local $f) -Recurse -File -ErrorAction SilentlyContinue).Count
        Add-State "copy '$f': robocopy $rc, $n files local"
        if ($rc -ge 8) { $ok = $false }
    }
    if ($ok) {
        $env:OCE_LOCAL_COPIES = "$Share|$local"
        if ($Profiles.StartsWith($Share + '\', [StringComparison]::OrdinalIgnoreCase)) { $Profiles = $local + $Profiles.Substring($Share.Length) }
        Add-State "copies ready in $([int]((Get-Date) - $t0).TotalSeconds) s: OCE_LOCAL_COPIES=$env:OCE_LOCAL_COPIES"
    }
    else { Add-State "copy FAILED: OCE_LOCAL_COPIES not set, the bench reads from $Share" }
}
$env:OCE_WELDMENT_PROFILES = $Profiles
Add-State "profiles: $Profiles"
$rounds = New-Object System.Collections.Generic.List[object]
$final = 0
try {
    for ($i = 1; $i -le $Repeat; $i++) {
        $roundOut = if ($Repeat -gt 1) { [IO.Path]::ChangeExtension($Out, ".$i.txt") } else { $Out }
        $roundBase = Join-Path (Split-Path $roundOut) ([IO.Path]::GetFileNameWithoutExtension($roundOut))
        $roundExtra = if ($i -gt 1 -and $Extra -notmatch '-SkipCompilation') { "$Extra -SkipCompilation" } else { $Extra }
        $label = ''
        if ($pairs.Count -gt 0) {
            foreach ($n in $names) { if ($n -ne 'EXTRA') { Remove-Item "Env:\$n" -ErrorAction SilentlyContinue } }
            $done = New-Object System.Collections.Generic.List[string]
            foreach ($c in $pairs[($i - 1) % $pairs.Count]) {
                $value = $c[1].Replace('{round}', $roundBase)
                if ($c[0] -eq 'EXTRA') { $roundExtra = "$roundExtra $value" } else { Set-Item "Env:\$($c[0])" $value }
                $done.Add("$($c[0])=$value")
            }
            $label = ' variant ' + ($done -join ';')
        }
        if ($RestartSolidWorks) {
            $cl = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $opsCommands 'close-leftovers.ps1') -Close 2>&1 | Select-Object -Last 1
            Add-State "round $i, leftovers: $cl"
            $rv = @(& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $opsCommands 'restart-solidworks.ps1') 2>&1)
            $rvExit = $LASTEXITCODE
            Add-State ("round $i, restart of SOLIDWORKS (exit $rvExit): " + (@($rv | Where-Object { "$_" -match '^reopened:|^STOP' }) -join ' / '))
            if ($rvExit -ne 0) { Add-State "round $i NOT started: the restart did not succeed"; $final = 2; break }
        }
        $argLine = "-NoProfile -ExecutionPolicy Bypass -File `"$bench`" $roundExtra"
        $p = Start-Process powershell -ArgumentList $argLine -WorkingDirectory $Root -RedirectStandardOutput $roundOut -RedirectStandardError "$roundOut.err" -PassThru -WindowStyle Hidden
        $null = $p.Handle   # without reading Handle at once, ExitCode stays empty once the process ends (measured)
        Add-State "round $i of $Repeat pid $($p.Id) $(Get-Date -Format s)$label"
        $last = $null; $seen = Get-Date; $stalled = $false
        while (-not $p.HasExited) {
            Start-Sleep -Seconds 30
            $now = if (Test-Path -LiteralPath $runJournal) { (Get-Item -LiteralPath $runJournal).LastWriteTime } else { $null }
            if ($now -ne $last) { $last = $now; $seen = Get-Date }
            elseif (((Get-Date) - $seen).TotalMinutes -ge $StallMinutes) {
                Add-State "stalled $(Get-Date -Format s): run journal unchanged for $StallMinutes min"
                Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
                $stalled = $true
                break
            }
        }
        $p.WaitForExit()
        $code = $p.ExitCode
        Add-State "end round $i $(Get-Date -Format s) exit $code"
        if ($code -eq 5) {
            foreach ($sw in @(Get-Process SLDWORKS -ErrorAction SilentlyContinue)) {
                $dmp = $roundBase + "_hung_$($sw.Id).dmp"
                try { $r = [OpsHungDump]::Write($sw, $dmp) } catch { $r = "NOT written: $($_.Exception.Message)" }
                $mb = if (Test-Path -LiteralPath $dmp) { [math]::Round((Get-Item -LiteralPath $dmp).Length / 1MB, 1) } else { 0 }
                Add-State "dump of SLDWORKS $($sw.Id) (responding: $($sw.Responding)): $r, $dmp, $mb MB"
            }
        }
        if (Test-Path -LiteralPath $swJournal) {
            Copy-Item -LiteralPath $swJournal ($roundBase + '_swjournal.swj') -Force
            Add-State "journal copied: $($roundBase)_swjournal.swj, $((Get-Item -LiteralPath ($roundBase + '_swjournal.swj')).Length) bytes"
        }
        if (Test-Path -LiteralPath $runJournal) {
            Copy-Item -LiteralPath $runJournal ($roundBase + '_run.txt') -Force
            Add-State "run journal copied: $($roundBase)_run.txt, $((Get-Item -LiteralPath ($roundBase + '_run.txt')).Length) bytes"
        }
        if ($cfg.AddInLog -and (Test-Path -LiteralPath $cfg.AddInLog)) {
            Copy-Item -LiteralPath $cfg.AddInLog ($roundBase + '_addin.txt') -Force
            Add-State "add-in log copied: $($roundBase)_addin.txt, $((Get-Item -LiteralPath ($roundBase + '_addin.txt')).Length) bytes"
        }
        $rounds.Add([ordered]@{ round = $i; exit = $code; stalled = $stalled; output = $roundOut })
        $final = if ($stalled) { 6 } else { $code }
        if ($stalled -or ($code -ne 0 -and -not $ContinueIfRed)) { break }
    }
}
catch { Add-State "error $(Get-Date -Format s): $($_.Exception.Message)"; $final = 2 }
finally {
    [ordered]@{ output = $Out; final_exit = $final; rounds = $rounds; closed = (Get-Date -Format s) } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath "$Out.result.json" -Encoding UTF8
    Add-State "closed $(Get-Date -Format s)"
}
exit $final
