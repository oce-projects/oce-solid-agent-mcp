# =============================================================================
#  prepare-release.ps1 - tutto quello che serve per una Release, tranne
#                        pubblicarla.
# =============================================================================
#
#  QUESTO FILE E' IN ASCII PURO, come gli altri .ps1 del progetto.
#
#  PERCHE' UN RITO A MANO E NON UNA WORKFLOW
#
#  Nessun runner di GitHub puo' compilare il ponte: `Oce.Solid.Agent.csproj`
#  compila contro l'interop di SOLIDWORKS, e su windows-latest l'interop non
#  c'e' (misurato: CS0246 IModelDoc2). La CI compila solo la suite di contratto,
#  e lo dice. Un runner self-hosted con una licenza SOLIDWORKS e' una spesa e
#  una manutenzione; il 24/9/2026 il proprietario ha scelto il rito a mano.
#
#  Il rischio del rito a mano e' uno solo, ed e' il solito: che un passo si
#  salti il giorno che si ha fretta. Quindi i passi che si possono controllare
#  li controlla questo script, e si ferma al primo che non torna.
#
#  COSA NON FA: non crea il tag, non spinge niente, non pubblica. Stampa i tre
#  comandi. Pubblicare e' un'azione verso l'esterno, e la fa una persona.
#
#  USO
#      .\Checks\prepare-release.ps1
#
#  ESCE 0 se la release e' pronta, 1 al primo controllo che non passa.
# =============================================================================

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Stop($why) {
    Write-Output ""
    Write-Output "FERMO: $why"
    exit 1
}
function Ok($what) { Write-Output "  ok  $what" }

function RunGit($arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & git.exe -C $root @arguments 2>$null } finally { $ErrorActionPreference = $old }
}

Write-Output "=== la versione"
$version = (Get-Content (Join-Path $root "VERSION.txt") -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Stop "VERSION.txt non e' una versione X.Y.Z: '$version'" }
Ok "VERSION.txt dice $version"
$tag = "v$version"

# UNA VOCE NEL CHANGELOG, e non una qualunque: quella di QUESTA versione. Le
# note della Release si prendono da li', e una Release senza note e' un file
# che nessuno sa cosa cambia.
$cl = Get-Content (Join-Path $root "CHANGELOG.md") -Encoding UTF8
$start = -1
for ($i = 0; $i -lt $cl.Count; $i++) {
    if ($cl[$i] -match ('^## ' + [regex]::Escape($version) + '\b')) { $start = $i; break }
}
if ($start -lt 0) { Stop "CHANGELOG.md non ha una voce '## $version'" }
$fine = $cl.Count
for ($i = $start + 1; $i -lt $cl.Count; $i++) { if ($cl[$i] -match '^## \d') { $fine = $i; break } }
Ok ("CHANGELOG.md ha la voce, " + ($fine - $start) + " righe")

Write-Output ""
Write-Output "=== il repository"
# NIENTE DI NON COMMITTATO: il binario deve essere quello di un commit, o
# nessuno potra' mai dire da che codice viene.
$dirty = @(RunGit @('status', '--porcelain') | Where-Object { $_ })
if ($dirty.Count -gt 0) {
    $dirty | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Stop "l'albero di lavoro ha modifiche non committate"
}
Ok "albero di lavoro pulito"

# E SPINTO: una Release che punta a un commit che il remoto non ha e' un tag
# che nessun altro puo' risolvere.
$null = RunGit @('fetch', '--quiet', 'origin')
$locale = (RunGit @('rev-parse', 'HEAD')) | Select-Object -First 1
$remote = (RunGit @('rev-parse', 'origin/main')) | Select-Object -First 1
if ($locale -ne $remote) { Stop "HEAD ($locale) non e' origin/main ($remote): prima si spinge" }
Ok "HEAD e' origin/main ($($locale.Substring(0,7)))"

$exists = @(RunGit @('tag', '-l', $tag) | Where-Object { $_ })
if ($exists.Count -gt 0) { Stop "il tag $tag esiste gia': una versione si rilascia una volta" }
Ok "il tag $tag non esiste ancora"

Write-Output ""
Write-Output "=== la compilazione"
& (Join-Path $root "build.ps1") | Out-Null
if ($LASTEXITCODE -ne 0) { Stop "build.ps1 e' uscito $LASTEXITCODE" }
$exe = Join-Path $root "dist\Installer.exe"
$sha = Join-Path $root "dist\Installer.exe.sha256"
if (-not (Test-Path $exe)) { Stop "manca dist\Installer.exe" }
if (-not (Test-Path $sha)) { Stop "manca dist\Installer.exe.sha256" }
Ok ("dist\Installer.exe, " + [int]((Get-Item $exe).Length / 1KB) + " kB")

# LO SHA256 SI RICALCOLA, NON SI CREDE AL FILE ACCANTO. E' il valore che chi
# scarica confrontera': se il file .sha256 e' rimasto di una compilazione
# precedente, la verifica che QUICKSTART insegna direbbe "manomesso" a tutti.
$real = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$declared = ((Get-Content $sha -Raw).Trim() -split '\s+')[0].ToLower()
if ($real -ne $declared) { Stop "lo sha256 accanto all'eseguibile non e' il suo ($declared contro $real)" }
Ok "sha256 ricalcolato e uguale: $($real.Substring(0,16))..."

# LA SUITE DI CONTRATTO, VERDE. Dentro ci sono i due controlli che una Release
# deve avere: la versione che il ponte annuncia e' quella di VERSION.txt, e
# l'ultimo giro di banco e' posteriore all'ultima modifica della libreria e del
# banco. Una Release su un banco vecchio e' un binario che nessuno ha misurato.
$tests = Join-Path $root "Tests\bin\Oce.Solid.Agent.ContractTests.exe"
if (-not (Test-Path $tests)) { Stop "manca Tests\bin\Oce.Solid.Agent.ContractTests.exe: si compila Tests\Oce.Solid.Agent.ContractTests.csproj" }
$old = $ErrorActionPreference; $ErrorActionPreference = "Continue"
$outcome = & $tests 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = $old
$line = ($outcome -split "`n" | Where-Object { $_ -match 'controlli, \d+ falliti' } | Select-Object -Last 1)
if ($code -ne 0) {
    ($outcome -split "`n" | Where-Object { $_ -match '^\s+NO ' }) | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Stop ("la suite di contratto non e' verde: " + $line)
}
Ok ("suite di contratto: " + $line.Trim())

Write-Output ""
Write-Output "=== le note della Release"
$note = Join-Path $root ("dist\note-di-rilascio-" + $version + ".md")
$cl[$start..($fine - 1)] | Out-File $note -Encoding utf8
Ok ("dist\note-di-rilascio-" + $version + ".md, dalla voce del CHANGELOG")

Write-Output ""
Write-Output "PRONTA. Non e' stato pubblicato niente. I tre passi, da fare a mano:"
Write-Output ""
Write-Output "  git tag -a $tag -m ""$tag"""
Write-Output "  git push origin $tag"
Write-Output "  gh release create $tag dist\Installer.exe dist\Installer.exe.sha256 --title ""$tag"" --notes-file ""$note"""
Write-Output ""
Write-Output "Il repository e' privato: la Release lo e' con lui."
exit 0
