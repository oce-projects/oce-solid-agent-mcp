# contracts.ps1 - build, contract suite and language gate of this repository, with a short summary.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) ported from the maintainer's verification script 1.2.0 (26/10/2026):
#         - OCE_NO_ATTACH=1 for the contracts unless -Attach: the protocol checks start the bridge,
#           and with SOLIDWORKS open it would attach to the session a bench or a person is using;
#         - stops after a failed build: the contracts would run on the old binaries and say
#           "not in this library" instead of showing the compiler error;
#         new in 1.0.0:
#         - the language gate also judges files not yet added to git (a temporary index with
#           `git add -N`): the detector reads `git ls-files`, so a NEW file with Italian in it
#           passes unseen otherwise (measured on the orchestrator's test container);
#         - a JSON summary next to the logs, read by deploy.ps1.
#
# The variable is set in THIS process only. Run from another script, it does not stay behind in the
# caller's shell (it did, with the old script: a measurement launched after it in the same shell
# answered "The bridge is not attached to SolidWorks." in a few milliseconds).
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\contracts.ps1
#       [-SkipBuild] [-Attach] [-OutDir <folder>] [-Pattern <regex>] [-DryRun]
#
# Output files in <OutDir>: contracts-build.txt, contracts-test-build.txt, contracts.txt,
# contracts-language.txt, contracts-summary.json.
#
# Exit codes:
#   0  build, contracts and language gate green
#   1  contracts or language gate red
#   2  build failed (contracts not run), or a tool is missing
param([switch] $SkipBuild, [switch] $Attach, [string] $OutDir = '', [string] $Pattern = '', [switch] $DryRun)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
if ($OutDir) { $cfg.OutDir = $OutDir }
$out = New-OpsOutDir $cfg
$root = $script:RepoRoot
$msb = Find-OpsMSBuild
if (-not $msb) { Stop-Ops 2 'MSBuild 2022 not found (Build Tools or Community)' }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Stop-Ops 2 'node not found: the language gate needs it' }
if ($DryRun) {
    Write-Ops "root $root, head $(Get-OpsHead), version $(Get-OpsVersion)"
    Write-Ops ("would run: " + $(if ($SkipBuild) { '(no build)' } else { '.\build.ps1' }) + ", MSBuild Tests\Oce.Solid.Agent.ContractTests.csproj, Tests\bin\Oce.Solid.Agent.ContractTests.exe" + $(if ($Attach) { ' ATTACHED' } else { ' with OCE_NO_ATTACH=1' }) + ', language gate with the untracked files')
    Write-Ops "logs in $out"
    exit 0
}
if (-not $Attach) { $env:OCE_NO_ATTACH = '1' } else { Remove-Item Env:\OCE_NO_ATTACH -ErrorAction SilentlyContinue }
Set-Location $root
$summary = [ordered]@{ head = (Get-OpsHead); version = (Get-OpsVersion); time = (Get-Date -Format s); attached = [bool]$Attach;
    build_exit = $null; test_build_exit = $null; contracts_exit = $null; checks = ''; language_exit = $null; red = @() }

if (-not $SkipBuild) {
    & (Join-Path $root 'build.ps1') *> (Join-Path $out 'contracts-build.txt')
    $summary.build_exit = $LASTEXITCODE
    Write-Ops "build exit $($summary.build_exit)"
    $errors = @(Select-String -Path (Join-Path $out 'contracts-build.txt') -Pattern 'error CS|: error' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique -First 12)
    $errors
    if ($summary.build_exit -ne 0 -or $errors.Count -gt 0) {
        $summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'contracts-summary.json') -Encoding UTF8
        Stop-Ops 2 'build failed: contracts not run'
    }
}
& $msb Tests\Oce.Solid.Agent.ContractTests.csproj -p:Configuration=Release -v:q -nologo > (Join-Path $out 'contracts-test-build.txt')
$summary.test_build_exit = $LASTEXITCODE
Write-Ops "test build exit $($summary.test_build_exit)"
if ($summary.test_build_exit -ne 0) {
    Select-String -Path (Join-Path $out 'contracts-test-build.txt') -Pattern 'error CS' | ForEach-Object { $_.Line.Trim() } | Select-Object -First 8
    $summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'contracts-summary.json') -Encoding UTF8
    Stop-Ops 2 'the contract suite does not build'
}
& Tests\bin\Oce.Solid.Agent.ContractTests.exe > (Join-Path $out 'contracts.txt')
$summary.contracts_exit = $LASTEXITCODE
Write-Ops "contracts exit $($summary.contracts_exit)"
$lines = @(Select-String -Path (Join-Path $out 'contracts.txt') -Pattern '^\s+NO\s|checks, ' | ForEach-Object { $_.Line.Substring(0, [Math]::Min(260, $_.Line.Length)) })
$lines
$summary.checks = @($lines | Where-Object { $_ -match 'checks, ' } | Select-Object -Last 1) -join ''
$summary.red = @($lines | Where-Object { $_ -match '^\s+NO\s' })
if ($Pattern) { Select-String -Path (Join-Path $out 'contracts.txt') -Pattern $Pattern | ForEach-Object { $_.Line.Substring(0, [Math]::Min(260, $_.Line.Length)) } }

# Language gate on a temporary copy of the index, so that new files are judged too.
$index = Invoke-OpsGit @('rev-parse', '--git-path', 'index')
$tmpIndex = Join-Path $env:TEMP ('oce-ops-index-' + (Get-OpsStamp))
Copy-Item -LiteralPath (Join-Path $root $index) $tmpIndex
$env:GIT_INDEX_FILE = $tmpIndex
try {
    $new = @(Invoke-OpsGit @('ls-files', '--others', '--exclude-standard') | Where-Object { $_ })
    foreach ($f in $new) { $null = Invoke-OpsGit @('add', '-N', '--', $f) }
    node Checks/language/detect-italian.js *> (Join-Path $out 'contracts-language.txt')
    $summary.language_exit = $LASTEXITCODE
}
finally {
    Remove-Item Env:\GIT_INDEX_FILE -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tmpIndex -ErrorAction SilentlyContinue
}
Write-Ops "language exit $($summary.language_exit) ($($new.Count) untracked files judged too)"
Get-Content (Join-Path $out 'contracts-language.txt') -Tail 2
$summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'contracts-summary.json') -Encoding UTF8
if ($summary.contracts_exit -ne 0 -or $summary.language_exit -ne 0) { exit 1 }
exit 0
