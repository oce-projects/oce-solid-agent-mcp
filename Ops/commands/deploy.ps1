# deploy.ps1 - puts the build of the bridge into the bin folder the MCP clients start it from.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) ported from the maintainer's deploy script 1.1.0 (31/10/2026), with the gates
#         written inside it instead of in a checklist:
#         G1 no tracked file changed (what is deployed is a commit, not a working tree);
#         G2 Bench\report\last.json is a FULL run (batch, family and cases empty), failed 0,
#            errors 0, exit code 0, on the commit being deployed (only the report itself may have
#            changed since: it is committed after the run);
#         G3 build and contract suite green (contracts.ps1: OCE_NO_ATTACH=1, language gate with new
#            files);
#         G4 the built executable carries the version of VERSION.txt.
#         Kept from 1.1.0 of the original: backup in <parent>\bin-backup-<date-time> (without the .old
#         files); a file in use (a bridge started by an open MCP session) is renamed to
#         <name>.old-<HHmmss> and then copied; SHA-256 of every copied file compared at the end;
#         *.jsonl never copied (questions-without-answer.jsonl in bin is a log the PRODUCTION bridge
#         writes while it works: the build had 878 lines of tests, production 22 real ones).
#         New: start test of the deployed executable (smoke-bridge.ps1), AUTOMATIC ROLLBACK from the
#         backup when the hashes or the start test fail, -Rollback <backup> to go back by hand, and a
#         JSON record of every deploy with the files that changed.
#
# THE RULE OF THE PROJECT: deploy only when the owner asks for it, after a full green bench and
# green contracts. -Reason records who asked and where. Bridges already running keep the old version
# until their MCP session restarts: the script lists them, and the report must say so.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\deploy.ps1 -DryRun
#   ... deploy.ps1 -Reason "<who asked, when>" [-Target <bin>] [-Source <bin>] [-SkipBuild] [-Exclude "*.jsonl,*.log"]
#   ... deploy.ps1 -Rollback <bin-backup-folder> -Reason "<why>"
#
# Exit codes:
#   0  deployed (or rolled back by hand), hashes equal, start test passed; with -DryRun: every gate passes
#   1  deployed but hashes or start test failed: ROLLED BACK automatically, and the old version starts
#   2  refused by a gate or by the configuration: NOTHING was touched
#   3  rollback failed: the target is in an unknown state. Manual intervention, tell the owner at once.
param(
    [string] $Source = '',
    [string] $Target = '',
    [switch] $DryRun,
    [string] $Exclude = '*.jsonl',
    [string] $Rollback = '',
    [switch] $SkipBuild,
    [string] $Reason = ''
)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
$Target = Resolve-OpsValue $Target $cfg 'DeployTarget' -Required
if (-not $Source) { $Source = Join-Path $script:RepoRoot 'bin' }
$patterns = @($Exclude -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$out = New-OpsOutDir $cfg
$stamp = Get-OpsStamp
$commands = $PSScriptRoot
$exeName = 'oce-solid-agent-mcp.exe'
if (-not $DryRun -and -not $Reason) { Stop-Ops 2 '-Reason is required: who asked for this deploy, and where' }

function Get-Files($dir) {
    @(Get-ChildItem -LiteralPath $dir -File -Recurse | Where-Object {
        $n = $_.Name
        ($n -notmatch '\.old-\d+$') -and ($n -notmatch '\.removed-\d+') -and -not ($patterns | Where-Object { $n -like $_ })
    })
}
function Get-Rel($file, $dir) { $file.FullName.Substring($dir.TrimEnd('\').Length + 1) }
function Get-ExeVersion($dir) {
    $e = Join-Path $dir $exeName
    if (Test-Path -LiteralPath $e) { (Get-Item -LiteralPath $e).VersionInfo.ProductVersion } else { '' }
}
# Copies every file of $files (relative to $from) into $to. A file in use is renamed first.
function Copy-Files($from, $to, $files) {
    $renamed = New-Object System.Collections.Generic.List[string]
    foreach ($f in $files) {
        $rel = Get-Rel $f $from
        $dest = Join-Path $to $rel
        New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
        try { Copy-Item -LiteralPath $f.FullName $dest -Force -ErrorAction Stop }
        catch [System.IO.IOException] {
            $old = "$dest.old-" + (Get-Date -Format 'HHmmss')
            Rename-Item -LiteralPath $dest $old
            Copy-Item -LiteralPath $f.FullName $dest -Force
            $renamed.Add($rel)
            Write-Ops "  in use, renamed: $rel -> $(Split-Path $old -Leaf)"
        }
    }
    , $renamed
}
function Compare-Hashes($from, $to, $files) {
    $bad = New-Object System.Collections.Generic.List[string]
    foreach ($f in $files) {
        $rel = Get-Rel $f $from
        $h1 = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
        $d = Join-Path $to $rel
        $h2 = if (Test-Path -LiteralPath $d) { (Get-FileHash -LiteralPath $d -Algorithm SHA256).Hash } else { '' }
        if ($h1 -ne $h2) { $bad.Add($rel) }
    }
    , $bad
}
function Invoke-Smoke($dir, $expect) {
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $commands 'smoke-bridge.ps1'), '-Exe', (Join-Path $dir $exeName))
    if ($expect) { $a += @('-ExpectVersion', $expect) }
    $o = @(& powershell @a 2>&1)
    $code = $LASTEXITCODE
    foreach ($l in $o) { [Console]::Out.WriteLine('  ' + $l) }   # not into the return value
    $code
}
function Get-RunningBridges {
    @(Get-Process oce-solid-agent-mcp -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Target, [StringComparison]::OrdinalIgnoreCase) })
}
function Backup-Target {
    $backup = Join-Path (Split-Path $Target -Parent) "bin-backup-$((Get-Date -Format 'yyyy-MM-dd-HHmmss'))"
    New-Item -ItemType Directory -Path $backup | Out-Null
    $all = @(Get-ChildItem -LiteralPath $Target -File -Recurse | Where-Object { $_.Name -notmatch '\.old-\d+$|\.removed-\d+' })
    foreach ($f in $all) {
        $dest = Join-Path $backup (Get-Rel $f $Target)
        New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $f.FullName $dest
    }
    Write-Ops "backup: $backup ($($all.Count) files)"
    $backup
}
# Puts the target back as the backup was: every backed-up file copied back, every file the
# deploy added renamed out of the way (never deleted).
function Restore-Target($backup, $added) {
    $files = Get-Files $backup
    $null = Copy-Files $backup $Target $files
    foreach ($rel in $added) {
        $p = Join-Path $Target $rel
        if (Test-Path -LiteralPath $p) { Rename-Item -LiteralPath $p ("$p.removed-" + (Get-Date -Format 'HHmmss')) }
    }
    $bad = Compare-Hashes $backup $Target $files
    , $bad
}
function Save-Record($r) {
    $file = Join-Path $out "deploy_$stamp.json"
    $r | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $file -Encoding UTF8
    Write-Ops "record: $file"
}

$record = [ordered]@{ time = (Get-Date -Format s); reason = $Reason; target = $Target; source = $Source; head = (Get-OpsHead);
    version_before = (Get-ExeVersion $Target); version_after = ''; backup = ''; changed = @(); added = @(); renamed_in_use = @();
    hash_mismatch = @(); smoke_exit = $null; rolled_back = $false; rollback_mismatch = @(); exit = $null }
if (-not (Test-Path -LiteralPath $Target)) { Stop-Ops 2 "target does not exist: $Target" }

# ---- rollback by hand ---------------------------------------------------------
if ($Rollback) {
    if (-not (Test-Path -LiteralPath $Rollback)) { Stop-Ops 2 "no backup folder: $Rollback" }
    $files = Get-Files $Rollback
    Write-Ops "rollback from $Rollback ($($files.Count) files), version $(Get-ExeVersion $Rollback)"
    if ($DryRun) { exit 0 }
    $record.backup = Backup-Target
    $bad = Restore-Target $Rollback @()
    $record.rollback_mismatch = @($bad)
    $record.smoke_exit = Invoke-Smoke $Target ''
    $record.version_after = Get-ExeVersion $Target
    $record.rolled_back = $true
    $record.exit = if ($bad.Count -eq 0 -and $record.smoke_exit -eq 0) { 0 } else { 3 }
    Save-Record $record
    exit $record.exit
}

# ---- gates (read-only) -----------------------------------------------------------
$refused = New-Object System.Collections.Generic.List[string]
$dirty = @(Invoke-OpsGit @('status', '--porcelain', '--untracked-files=no') | Where-Object { $_ })
if ($dirty.Count -gt 0) { $refused.Add("G1 tracked files changed: $($dirty.Count) (deploy a commit, not a working tree)") } else { Write-Ops 'G1 ok: no tracked file changed' }
$reportFile = Join-Path $script:RepoRoot 'Bench\report\last.json'
if (-not (Test-Path -LiteralPath $reportFile)) { $refused.Add('G2 no Bench\report\last.json') }
else {
    $rep = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
    $full = ("$($rep.batch)" -eq '') -and ("$($rep.family)" -eq '') -and ("$($rep.cases)" -eq '')
    $green = ($rep.failed -eq 0) -and ($rep.errors -eq 0) -and ($rep.exit_code -eq 0)
    $since = @()
    $known = Invoke-OpsGit @('cat-file', '-t', "$($rep.commit)")
    if ($known -ne 'commit') { $refused.Add("G2 the bench commit $($rep.commit) is not in this repository") }
    else { $since = @(Invoke-OpsGit @('diff', '--name-only', "$($rep.commit)", 'HEAD') | Where-Object { $_ -and $_ -ne 'Bench/report/last.json' }) }
    if (-not $full) { $refused.Add('G2 last.json is a PARTIAL run (batch, family or cases set)') }
    if (-not $green) { $refused.Add("G2 last.json is not green: failed $($rep.failed), errors $($rep.errors), exit $($rep.exit_code)") }
    if ($since.Count -gt 0) { $refused.Add("G2 $($since.Count) files changed since the bench commit $($rep.commit): " + (($since | Select-Object -First 5) -join ', ')) }
    if ($full -and $green -and $since.Count -eq 0 -and $known -eq 'commit') { Write-Ops "G2 ok: full bench green on $($rep.commit) ($($rep.summary))" }
}
$version = Get-OpsVersion
if (-not $version) { $refused.Add('VERSION.txt is empty') }

if ($DryRun) {
    Write-Ops "target $Target (now $($record.version_before)), source $Source, VERSION.txt $version"
    if (Test-Path -LiteralPath $Source) {
        foreach ($f in (Get-Files $Source)) {
            $rel = Get-Rel $f $Source
            $d = Join-Path $Target $rel
            if (-not (Test-Path -LiteralPath $d)) { Write-Output "  would add     $rel" }
            elseif ((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $d -Algorithm SHA256).Hash) { Write-Output "  would replace $rel" }
        }
        Write-Ops "excluded: $($patterns -join ', ')"
    }
    else { Write-Ops "no source yet ($Source): the build would create it" }
    Write-Ops ('G3 would run: contracts.ps1' + $(if ($SkipBuild) { ' -SkipBuild' } else { ' (build.ps1 first)' }))
    Write-Ops "running bridges from the target: $((Get-RunningBridges).Count) (they keep the old version until their session restarts)"
    foreach ($r in $refused) { Write-Output "REFUSED $r" }
    if ($refused.Count -gt 0) { exit 2 }
    exit 0
}
if ($refused.Count -gt 0) { foreach ($r in $refused) { Write-Output "REFUSED $r" }; Stop-Ops 2 'a gate refused the deploy: nothing touched' }

$c = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $commands 'contracts.ps1'))
if ($SkipBuild) { $c += '-SkipBuild' }
& powershell @c | ForEach-Object { '  ' + $_ }
if ($LASTEXITCODE -ne 0) { Stop-Ops 2 "G3 contracts.ps1 exit ${LASTEXITCODE}: nothing touched" }
Write-Ops 'G3 ok: build and contracts green'
$built = Get-ExeVersion $Source
if (-not "$built".StartsWith($version)) { Stop-Ops 2 "G4 the built bridge says $built, VERSION.txt says ${version}: nothing touched" }
Write-Ops "G4 ok: built version $built"

# ---- deploy --------------------------------------------------------------------------
$files = Get-Files $Source
foreach ($f in $files) {
    $rel = Get-Rel $f $Source
    $d = Join-Path $Target $rel
    if (-not (Test-Path -LiteralPath $d)) { $record.added += $rel }
    elseif ((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $d -Algorithm SHA256).Hash) { $record.changed += $rel }
}
$record.backup = Backup-Target
$record.renamed_in_use = @(Copy-Files $Source $Target $files)
$record.hash_mismatch = @(Compare-Hashes $Source $Target $files)
Write-Ops "copied $($files.Count) files, $($record.changed.Count) changed, $($record.added.Count) new, $($record.renamed_in_use.Count) renamed because in use, $($record.hash_mismatch.Count) hash mismatches"
if ($record.hash_mismatch.Count -eq 0) { $record.smoke_exit = Invoke-Smoke $Target $version }
$record.version_after = Get-ExeVersion $Target
if ($record.hash_mismatch.Count -gt 0 -or $record.smoke_exit -ne 0) {
    Write-Ops 'deploy FAILED: rolling back from the backup'
    $record.rollback_mismatch = @(Restore-Target $record.backup $record.added)
    $record.rolled_back = $true
    $back = Invoke-Smoke $Target ''
    $record.version_after = Get-ExeVersion $Target
    $record.exit = if ($record.rollback_mismatch.Count -eq 0 -and $back -eq 0) { 1 } else { 3 }
    Save-Record $record
    if ($record.exit -eq 3) { Write-Output 'ROLLBACK FAILED: the target is in an unknown state. Tell the owner now.' }
    exit $record.exit
}
$running = Get-RunningBridges
Write-Ops "deployed $($record.version_before) -> $($record.version_after). Bridges running from the target: $($running.Count); they keep the OLD version until their MCP session restarts."
$record.exit = 0
Save-Record $record
exit 0
