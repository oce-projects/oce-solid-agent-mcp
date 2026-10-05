# Accende i ganci di git di questo repository.
#
#     .\Checks\install-hooks.ps1
#
# Va lanciato UNA VOLTA per clone. Git non versiona `.git\hooks`, quindi un
# gancio scritto nel repository non e' acceso finche' qualcuno non lo dice a
# git - ed e' esattamente il passo che si dimentica.
#
# Non si copiano i file dentro `.git\hooks`: si punta `core.hooksPath` alla
# cartella versionata. Cosi' un gancio corretto domani vale subito per tutti,
# invece di restare una copia vecchia dentro il clone di ognuno.
#
# Per spegnerli:  git config --unset core.hooksPath

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$hooks = Join-Path $here 'hooks'

if (-not (Test-Path (Join-Path $root '.git'))) {
    Write-Host "Qui non c'e' un repository git: niente da accendere." -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path $hooks)) { throw "non trovo $hooks" }

# Il percorso si scrive RELATIVO alla radice e con le barre in avanti: git lo
# risolve rispetto alla radice del lavoro, e un percorso assoluto di questa
# macchina finirebbe nel `.git\config` di chi lo copia.
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
