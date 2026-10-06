# =============================================================================
#  bench.ps1 - one command only, and a report on disk   (entry F2)
# =============================================================================
#
#  THIS FILE IS PURE ASCII, AND IT IS NOT A WHIM. PowerShell 5.1 reads a
#  script without a BOM as ANSI: a guillemet or an em dash inside a
#  message comes out garbled on the screen of whoever runs it, and a
#  garbled message reads worse than an ugly one.
#
#  WHAT IT DOES, IN ORDER, AND WHY THIS ORDER:
#
#    1. builds the library and the bench, and STOPS if either one fails.
#       This is the silent defect that entry F2 named: a script that exits 0
#       because the build failed and nobody looked. Here msbuild is
#       checked on its exit code, not on its text.
#    2. checks whether SOLIDWORKS can be attached to, and stops with a message
#       if it cannot. Without this, the bench starts and dies thirty seconds
#       later with a line that talks about COM.
#    3. empties the documents left open. It is not tidying: six documents too
#       many are ~650 characters in the state, and the check "state under
#       16 KB" goes from 15.6k to 16.2k and FAILS without the code having
#       changed. It costs half an hour to hunt for a regression that is not there.
#    4. launches the bench and measures how long it takes.
#    5. writes Bench\report\YYYYMMDD_hhmm.md and .json, plus last.json.
#
#  WHY THE REPORT. "It only runs when somebody launches it by hand" is half
#  of the problem; the other half is that it LEAVES NO TRACE. A number that is
#  not in a file cannot be compared with yesterday's, nor put in a
#  README.
#
#  EXIT CODES:
#    0  all green
#    1  failed checks
#    2  bench error: build failed, SOLIDWORKS cannot be attached to,
#       group does not exist, or a case that COULD NOT BE MEASURED (a
#       missing field, a selector that does not resolve, a twin that is not there).
#       It is NOT the same thing as a red check and must not be counted with it: a
#       red check is a measurement, a bench error is a MISSED measurement.
#    3  the desk was not clean: documents already open in SOLIDWORKS that
#       are not the bench's (entry K12). The run does not start at all.
#    4  SOLIDWORKS went away mid-run: three new documents not born
#       IN A ROW (entry K17). The remaining checks are NOT red, they are skipped, and
#       the run is not valid. It wins over 2 and over 1: whoever reads a 1 starts
#       looking for what broke, and nothing broke.
#
#      .\Bench\bench.ps1
#      .\Bench\bench.ps1 -Group views          a single family (entry F13)
#      .\Bench\bench.ps1 -Family reanchor    only that folder of cases (E19)
#      .\Bench\bench.ps1 -Slow                 also the checks that cost minutes
#      .\Bench\bench.ps1 -Mutate                  perturbs the cases (entry F6)
#      .\Bench\bench.ps1 -Regression           the references under modification (F8)
#      .\Bench\bench.ps1 -SkipCompilation     the binary that is already there
# =============================================================================

param(
    [string] $Group = "",
    [string] $Family = "",
    [switch] $Slow,
    [switch] $Mutate,
    [switch] $Regression,
    [switch] $SkipCompilation,
    [switch] $NoRestart,
    [string] $Library = ""
)

$ErrorActionPreference = "Stop"

$Bench  = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $Bench

if ([string]::IsNullOrWhiteSpace($Library)) {
    $Library = Join-Path $Root "Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll"
}

function Die($code, $message) {
    Write-Output ""
    Write-Output ("BENCH NOT STARTED: " + $message)
    exit $code
}

# --- 1. build --------------------------------------------------------------

if (-not $SkipCompilation) {
    $msbuild = $null
    $places = @(
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($p in $places) { if (Test-Path $p) { $msbuild = $p; break } }
    if ($msbuild -eq $null) {
        Die 2 "cannot find MSBuild. Run with -SkipCompilation if you already have the binaries."
    }

    foreach ($project in @("Oce.Solid.Agent.Core\Oce.Solid.Agent.Core.csproj",
                            "Bench\Oce.Solid.Agent.Bench.csproj")) {
        $full = Join-Path $Root $project
        Write-Output ("building " + $project)
        & $msbuild $full /p:Configuration=Release /v:m /nologo | Out-Null
        # THE EXIT CODE, NOT THE TEXT. It is the check that entry F2
        # asked for by name.
        if ($LASTEXITCODE -ne 0) { Die 2 ("build failed: " + $project) }
    }
}

if (-not (Test-Path $Library)) {
    Die 2 ("cannot find the library: " + $Library)
}

# --- 2. can SOLIDWORKS be attached to? -------------------------------------

try {
    $sw = [Runtime.InteropServices.Marshal]::GetActiveObject("SldWorks.Application")
    if ($sw -eq $null) { throw "no instance" }
} catch {
    Die 2 "SOLIDWORKS cannot be attached to. Open it (a single session) and run again."
}

# --- 3. away with the documents left open ----------------------------------

$close = Join-Path $Root "Checks\CloseLeftovers\bin\CloseLeftovers.exe"
$openAfterCleanup = -1
if (Test-Path $close) {
    # Its last line says how many documents are still open: the count decides
    # the restart below (late-bound ISldWorks calls fail from PowerShell with
    # TYPE_E_ELEMENTNOTFOUND, measured 2/10/2026).
    # The bench's work folder is local (see Bench/Expectations.cs); the old one
    # next to the repository is still closed too, for documents of older runs.
    $benchWork = if ($env:OCE_BENCH_WORK) { $env:OCE_BENCH_WORK } else { Join-Path $env:TEMP "oce-bench-work" }
    $cleanup = & $close --close $benchWork (Join-Path $Bench "work") 2>&1
    $countLine = @($cleanup | Where-Object { "$_" -match ':\s*(\d+)\s*$' }) | Select-Object -Last 1
    if ($countLine -and ("$countLine" -match ':\s*(\d+)\s*$')) { $openAfterCleanup = [int]$Matches[1] }
} else {
    Write-Output "CloseLeftovers is missing: the open documents stay open, and the state lists them."
}

# --- 3b. a fresh SOLIDWORKS for a full run (entry Z-C24) ---------------------
# A long SOLIDWORKS session degrades without raising errors: on 1-2/10/2026 two
# full runs went red on rebuilds that never happened and on centre marks and
# dimensions that were not inserted, after ~4,900 documents (window resources
# exhausted) and after ~7 hours (process handles 2,332 -> 16,281). The same
# families on a restarted SOLIDWORKS: 314/314, 492/492, 90/90. So a full run
# starts on a fresh session, unless documents the bench did not open are still
# there (those are someone's work: never killed) or -NoRestart is given.
$fullRun = [string]::IsNullOrWhiteSpace($Group) -and [string]::IsNullOrWhiteSpace($Family)
if ($fullRun -and -not $NoRestart) {
    $openDocuments = $openAfterCleanup
    $process = Get-Process SLDWORKS -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($openDocuments -ne 0 -or $process -eq $null) {
        Write-Output ("SOLIDWORKS not restarted: " + $openDocuments + " document(s) still open (or the process was not found).")
    } else {
        $exe = $process.Path
        Write-Output ("restarting SOLIDWORKS for a fresh session (it had " + $process.HandleCount + " handles)")
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($sw)
        $sw = $null
        Stop-Process -Id $process.Id -Force
        Start-Sleep -Seconds 5
        Start-Process $exe | Out-Null
        $ready = $false
        for ($i = 0; $i -lt 48 -and -not $ready; $i++) {
            Start-Sleep -Seconds 5
            try { $sw = [Runtime.InteropServices.Marshal]::GetActiveObject("SldWorks.Application"); $ready = ($sw -ne $null) } catch { }
        }
        if (-not $ready) { Die 2 "SOLIDWORKS did not come back within 4 minutes after the restart." }
        Start-Sleep -Seconds 20   # the add-ins finish loading after the COM object appears
    }
}

# --- 4. the run -------------------------------------------------------------

$arguments = @("--library", $Library)
if ($Slow) { $arguments += "--slow" }
# -Mutate is not a run with something extra: it is another run. It perturbs the cases
# one argument at a time and EXPECTS them to turn red (entry F6). It costs one
# SOLIDWORKS document per mutation: it is launched before a delivery.
if ($Mutate) { $arguments += "--mutate" }
# -Regression: the nineteen cases of the references (entry F8). It produces THREE NUMBERS
# that add up to the cases -- they hold, they come off and say so, they come off
# in silence -- and the third is the only one that counts.
if ($Regression) { $arguments += "--regression" }
if (-not [string]::IsNullOrWhiteSpace($Group)) { $arguments += @("--group", $Group) }
# -Family: within the declared cases, only those folders; it implies the
# «cases» group. Partial like -Group, and written in the summary.
if (-not [string]::IsNullOrWhiteSpace($Family)) { $arguments += @("--family", $Family) }

$executable = Join-Path $Bench "bin\Oce.Solid.Agent.Bench.exe"
if (-not (Test-Path $executable)) { Die 2 ("cannot find " + $executable) }

$start = Get-Date
# The live output goes to a LOCAL file and is copied next to the bench at the
# end. Written straight onto a network drive, one SMB hiccup («semaphore
# timeout») made Tee-Object throw and killed the whole run: on 24/9/2026 at 535
# checks and on 2/10/2026 after four hours.
$outputNextToBench = Join-Path $Bench "last-run.txt"
$output = Join-Path $env:TEMP "oce-bench-last-run.txt"
if (Test-Path $outputNextToBench) { Remove-Item $outputNextToBench -Force -ErrorAction SilentlyContinue }
# "Continue" ONLY HERE, AND FOR A MEASURED REASON. With $ErrorActionPreference at
# "Stop", the FIRST line the bench writes to stderr becomes a terminating
# PowerShell error: the script dies at this line with a
# NativeCommandError that names bench.ps1 and says nothing about the bench.
#
# It happened. The bench did not start because of a TypeInitializationException, and
# what one saw was:
#
#     Oce.Solid.Agent.Bench.exe :
#     In <la cartella del clone>\Bench\bench.ps1:138 car:1
#     + & $eseguibile $argomenti 2>&1 | Tee-Object -FilePath $uscita
#
# without a word about the exception, and with `last-run.txt` stuck at the
# PREVIOUS run -- which is the worst thing: an old report is read
# believing it new. Now stderr passes through, ends up in the file like all the
# rest, and the exit code is the only thing that decides.
# AWAY WITH YESTERDAY'S FILE BEFORE LAUNCHING. Tee-Object opens it at the first line
# that passes: if the bench dies before writing one, the file is NOT
# touched and stays there with the previous run inside, now included. Removing it
# now is what makes the check below true.
if (Test-Path $output) { Remove-Item $output -Force }

$wasBefore = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& $executable $arguments 2>&1 | Tee-Object -FilePath $output
$code = $LASTEXITCODE
$ErrorActionPreference = $wasBefore
$duration = (Get-Date) - $start

# The copy next to the bench, retried: the network drive may be busy for a few
# seconds; the local file stays the one this script reads.
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try { Copy-Item $output $outputNextToBench -Force -ErrorAction Stop; break }
    catch { if ($attempt -eq 5) { Write-Warning ("could not copy the run output to " + $outputNextToBench + ": " + $_.Exception.Message + " (it stays in " + $output + ")") } else { Start-Sleep -Seconds 10 } }
}

# A RUN THAT WROTE NOTHING IS NOT A RUN. If the bench dies before the
# first line -- a library that does not load, an interop that is not there
# -- the file stays yesterday's and every count that follows is a lie.
$written = 0
if (Test-Path $output) { $written = (Get-Item $output).Length }
if ($written -lt 100) {
    Die 2 ("the bench wrote nothing (exit code " + $code + "). " +
             "Run the executable by hand to see the exception: " + $executable)
}

# --- 5. the report ----------------------------------------------------------

$lines = Get-Content $output

# THE SUMMARY IS TAKEN FROM THE LINE THE BENCH WRITES, it is not recalculated:
# two counts of the same number, made in two places, sooner or later diverge and
# then nobody knows which one is the true one.
$fine = ($lines | Where-Object { $_ -match "^(PARTIAL RUN|[0-9]+ tests,)" } | Select-Object -Last 1)
if ($fine -eq $null) { $fine = "(the bench did not write the summary)" }
# [string] AND IT IS NOT ENOUGH THAT IT BE A STRING. Get-Content returns lines
# with PowerShell's notes on them (PSPath, PSDrive, the provider...), and
# ConvertTo-Json follows them all: the first .json that came out of here weighed 498 kB
# of tree of the S: drive around one summary line.
$fine = [string]$fine

# THE OUTCOME LINES COME OUT TWICE: once while the run goes, once in the final
# list after the ==== line. The ones after are counted, otherwise everything
# doubles.
$cut = 0
for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^={40,}") { $cut = $i + 1 } }
$list = if ($cut -gt 0) { $lines[$cut..($lines.Count - 1)] } else { $lines }

$passed = @($list | Where-Object { $_ -match "^  OK   " })
$failed = @($list | Where-Object { $_ -match "^  NO   " })
$skipped = @($list | Where-Object { $_ -match "^  --   " })
# BENCH ERRORS ARE COUNTED SEPARATELY (entry F4): "I could not measure"
# is not "I measured and it does not add up", and adding them would make a suite
# that has emptied itself read like a suite that found something.
$errors  = @($list | Where-Object { $_ -match "^  !!   " })

function Take($regex) {
    $r = ($lines | Where-Object { $_ -match $regex } | Select-Object -First 1)
    if ($r -eq $null) { return "(not declared)" }
    return ([regex]::Match($r, $regex)).Groups[1].Value
}

$sw_ver  = Take "^SolidWorks (.+)$"
$lib_ver = Take "^library ([0-9.]+)"
# TWO NUMBERS, with their name (entry F15): the ones executable from the library and the ones
# served in the catalog. The «catalog» field of the JSON stays, with the same value
# as before (the executable ones), for whoever was reading it.
$executables = Take "^runnable: ([0-9]+) operations"
$inCatalog  = Take "^runnable: [0-9]+ operations \(in catalog: ([0-9]+)\)"
# EXPECTATIONS VERIFIED N OUT OF M: if the two numbers diverge, something is measuring
# nothing. The bench writes it, it is not recalculated here.
$expectations = ($lines | Where-Object { $_ -match "^expectations verified " } | Select-Object -Last 1)
$expectations = [string]$expectations
if ([string]::IsNullOrWhiteSpace($expectations)) { $expectations = "expectations verified: (not declared)" }

# THE GREEN CASES, BY NAME (entry Z-C6). The bench writes them on a single line; here
# they become a list (`green_cases`) in `last.json`, which the contract suite crosses with the
# catalog: every operation served must have a green case in the last run.
# [string] for the same reason as $fine: no PowerShell notes in the JSON.
$greenLine = [string]($lines | Where-Object { $_ -match "^green cases: " } | Select-Object -Last 1)
$greenCases = @()
if (-not [string]::IsNullOrWhiteSpace($greenLine)) {
    $greenCases = @($greenLine.Substring("green cases: ".Length).Split(
        [char[]]@(' '), [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [string]$_ })
}

$commit = "(not in a repository)"
try {
    Push-Location $Root
    $c = & git rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0) { $commit = $c }
    Pop-Location
} catch { }

$timestamp = Get-Date
$itemName = $timestamp.ToString("yyyyMMdd_HHmm")
$folder = Join-Path $Bench "report"
if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }

# TWO RUNS IN THE SAME MINUTE DO NOT OVERWRITE EACH OTHER.
# A run with `-Group` lasts seconds, so it happens right away: on 15/9/2026 a green
# report was erased by a red run started twenty seconds
# later, and the only sign was the count that changed. A report that disappears
# is worse than a report that is not there: it makes one believe that run went
# the way the one that replaced it says.
$nth = 2
while (Test-Path (Join-Path $folder ($itemName + ".md"))) {
    $itemName = $timestamp.ToString("yyyyMMdd_HHmm") + "_" + $nth
    $nth++
}

$md = New-Object System.Collections.Generic.List[string]
$md.Add("# Bench run - " + $timestamp.ToString("yyyy-MM-dd HH:mm"))
$md.Add("")
$md.Add("| | |")
$md.Add("|---|---|")
$md.Add("| SOLIDWORKS | " + $sw_ver + " |")
$md.Add("| library | " + $lib_ver + " |")
$md.Add("| operations | " + $executables + " runnable, " + $inCatalog + " in catalog |")
$md.Add("| commit | " + $commit + " |")
$md.Add("| duration | " + [int]$duration.TotalSeconds + " s |")
if (-not [string]::IsNullOrWhiteSpace($Group)) { $md.Add("| group | " + $Group + " |") }
if (-not [string]::IsNullOrWhiteSpace($Family)) { $md.Add("| family | " + $Family + " |") }
$md.Add("")
$md.Add("**" + $fine + "**")
$md.Add("")
$md.Add("| | |")
$md.Add("|---|---|")
$md.Add("| passed | " + $passed.Count + " |")
$md.Add("| failed | " + $failed.Count + " |")
$md.Add("| skipped | " + $skipped.Count + " |")
$md.Add("| bench errors | " + $errors.Count + " |")
$md.Add("| exit code | " + $code + " |")
$md.Add("")
$md.Add("**" + $expectations + "**")
$md.Add("")
$md.Add("> If the two numbers diverge, something is measuring nothing: a case")
$md.Add("> that reads a field that no longer exists. A bench error is not")
$md.Add("> a red check - it means the check WAS NOT DONE - and")
$md.Add("> makes the run exit with code 2.")
$md.Add("")

if ($failed.Count -gt 0) {
    $md.Add("## The red ones")
    $md.Add("")
    $md.Add("The detail on the right is what the check wrote: where there is")
    $md.Add("a number, the expected and the measured values are there.")
    $md.Add("")
    # SINGLE QUOTES: the backtick is PowerShell's escape character, and
    # three backticks inside a double-quoted string are a syntax error.
    $md.Add('```')
    foreach ($r in $failed) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

if ($errors.Count -gt 0) {
    $md.Add("## Bench errors")
    $md.Add("")
    $md.Add("They are not red checks: they are checks that WERE NOT DONE.")
    $md.Add("")
    $md.Add('```')
    foreach ($r in $errors) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

if ($skipped.Count -gt 0) {
    $md.Add("## The skipped ones")
    $md.Add("")
    $md.Add('```')
    foreach ($r in $skipped) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

# UTF-8 WITHOUT BOM. `Out-File -Encoding utf8` on PowerShell 5.1 puts the BOM,
# and the three bytes in front of the `#` make the markdown title look like an
# ordinary paragraph to whoever renders it.
$withoutBom = New-Object System.Text.UTF8Encoding($false)

$fileMd = Join-Path $folder ($itemName + ".md")
[System.IO.File]::WriteAllText($fileMd, ($md -join "`r`n") + "`r`n", $withoutBom)

$obj = [ordered]@{
    timestamp    = $timestamp.ToString("s")
    solidworks   = $sw_ver
    library      = $lib_ver
    catalog      = $(if ($executables -match "^[0-9]+$") { [int]$executables } else { $executables })
    runnable     = $(if ($executables -match "^[0-9]+$") { [int]$executables } else { $executables })
    in_catalog   = $(if ($inCatalog -match "^[0-9]+$") { [int]$inCatalog } else { $inCatalog })
    commit       = $commit
    batch        = $Group
    family       = $Family
    slow         = [bool]$Slow
    duration_s   = [int]$duration.TotalSeconds
    summary      = $fine
    expectations = $expectations
    passed       = $passed.Count
    failed       = $failed.Count
    skipped      = $skipped.Count
    errors       = $errors.Count
    exit_code    = $code
    red          = @($failed | ForEach-Object { $_.TrimEnd() })
    bench_errors = @($errors | ForEach-Object { $_.TrimEnd() })
    green_cases  = $greenCases
}
$json = $obj | ConvertTo-Json -Depth 4
$fileJson = Join-Path $folder ($itemName + ".json")
[System.IO.File]::WriteAllText($fileJson, $json, $withoutBom)
[System.IO.File]::WriteAllText((Join-Path $folder "last.json"), $json, $withoutBom)

Write-Output ""
Write-Output ("report: " + $fileMd)
Write-Output ("           " + $fileJson)
Write-Output ("exit: " + $code)
exit $code
