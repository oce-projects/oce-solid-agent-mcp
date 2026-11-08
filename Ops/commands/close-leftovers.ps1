# close-leftovers.ps1 - closes the test documents left open in SOLIDWORKS, through Checks\CloseLeftovers.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version. A wrapper, not a copy: the rule of WHAT may be closed stays in
#         Checks\CloseLeftovers (never saved documents, plus those under the folders passed), and it
#         saves nothing. This script only adds the folders of the configuration and the bench work
#         folder, and says how to build the tool when it is missing.
#
# Why it matters: leftover documents are not only waste. Every state read walks through all of them,
# a contract that needs a clean SOLIDWORKS fails with one part active, and the bench refuses to start
# (exit 3) when documents that are not its own are open.
#
# NEVER use the bench for this (an option of the bench executable that closes leftovers starts the
# whole case group first: measured, it ran almost a full bench).
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\close-leftovers.ps1            only lists
#   ... close-leftovers.ps1 -Close [-Folders "<folder>,<folder>"]                                   closes
#   ... close-leftovers.ps1 -ListJson                                                               JSON list
#
# Exit codes: those of CloseLeftovers.exe, plus 2 when the tool is not built.
param([switch] $Close, [string] $Folders = '', [switch] $ListJson, [switch] $DryRun)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
$exe = Join-Path $script:RepoRoot 'Checks\CloseLeftovers\bin\CloseLeftovers.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Stop-Ops 2 "not built: $exe. Build it with: powershell -NoProfile -ExecutionPolicy Bypass -File Checks\build-checks.ps1"
}
$list = New-Object System.Collections.Generic.List[string]
foreach ($f in @($Folders -split ',') + @($cfg.LeftoverFolders)) { if ($f -and $f.Trim()) { $list.Add($f.Trim()) } }
$benchWork = if ($env:OCE_BENCH_WORK) { $env:OCE_BENCH_WORK } else { Join-Path $env:TEMP 'oce-bench-work' }
$list.Add($benchWork)
if ($ListJson) { & $exe --list-json; exit $LASTEXITCODE }
if (-not $Close -or $DryRun) {
    if ($Close) { Write-Ops ("would close: never saved documents and those under " + ($list -join '; ')) }
    & $exe
    exit $LASTEXITCODE
}
& $exe --close @list
exit $LASTEXITCODE
