# Turns on the git hooks of this repository.
#
#     .\Checks\install-hooks.ps1
#
# It must be run ONCE per clone. Git does not version `.git\hooks`, so a
# hook written in the repository is not on until somebody tells
# git - and it is exactly the step that gets forgotten.
#
# The files are not copied inside `.git\hooks`: `core.hooksPath` is pointed at the
# versioned folder. This way a hook corrected tomorrow holds at once for everybody,
# instead of remaining an old copy inside everybody's clone.
#
# To turn them off:  git config --unset core.hooksPath

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$hooks = Join-Path $here 'hooks'

if (-not (Test-Path (Join-Path $root '.git'))) {
    Write-Host "Qui non c'e' un repository git: niente da accendere." -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path $hooks)) { throw "non trovo $hooks" }

# The path is written RELATIVE to the root and with forward slashes: git
# resolves it with respect to the root of the working tree, and an absolute path of this
# machine would end up in the `.git\config` of whoever copies it.
$relative = 'Checks/hooks'

Push-Location $root
try {
    & git config core.hooksPath $relative
    if ($LASTEXITCODE -ne 0) { throw "git config ha risposto $LASTEXITCODE" }
    $read = (& git config core.hooksPath)
}
finally { Pop-Location }

Write-Host ""
Write-Host "  core.hooksPath = $read" -ForegroundColor Green
Get-ChildItem $hooks -File | ForEach-Object {
    Write-Host ("    " + $_.Name) -ForegroundColor DarkGray
}
Write-Host ""
Write-Host "  Da adesso `git push` lancia check-publishability.ps1 e si" -ForegroundColor White
Write-Host "  ferma se quello esce 1. Per saltarlo una volta: git push --no-verify" -ForegroundColor DarkGray
