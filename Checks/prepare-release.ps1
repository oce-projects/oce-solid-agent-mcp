# =============================================================================
#  prepara-release.ps1 - tutto quello che serve per una Release, tranne
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
#      .\Controlli\prepara-release.ps1
#
#  ESCE 0 se la release e' pronta, 1 al primo controllo che non passa.
# =============================================================================

$ErrorActionPreference = "Stop"
$radice = Split-Path -Parent $PSScriptRoot
Set-Location $radice

function Ferma($perche) {
    Write-Output ""
    Write-Output "FERMO: $perche"
    exit 1
}
function Ok($cosa) { Write-Output "  ok  $cosa" }

function EseguiGit($argomenti) {
    $vecchio = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & git.exe -C $radice @argomenti 2>$null } finally { $ErrorActionPreference = $vecchio }
}

Write-Output "=== la versione"
$versione = (Get-Content (Join-Path $radice "VERSION.txt") -Raw).Trim()
if ($versione -notmatch '^\d+\.\d+\.\d+$') { Ferma "VERSION.txt non e' una versione X.Y.Z: '$versione'" }
Ok "VERSION.txt dice $versione"
$tag = "v$versione"

# UNA VOCE NEL CHANGELOG, e non una qualunque: quella di QUESTA versione. Le
# note della Release si prendono da li', e una Release senza note e' un file
# che nessuno sa cosa cambia.
$cl = Get-Content (Join-Path $radice "CHANGELOG.md") -Encoding UTF8
$inizio = -1
for ($i = 0; $i -lt $cl.Count; $i++) {
    if ($cl[$i] -match ('^## ' + [regex]::Escape($versione) + '\b')) { $inizio = $i; break }
}
if ($inizio -lt 0) { Ferma "CHANGELOG.md non ha una voce '## $versione'" }
$fine = $cl.Count
for ($i = $inizio + 1; $i -lt $cl.Count; $i++) { if ($cl[$i] -match '^## \d') { $fine = $i; break } }
Ok ("CHANGELOG.md ha la voce, " + ($fine - $inizio) + " righe")

Write-Output ""
Write-Output "=== il repository"
# NIENTE DI NON COMMITTATO: il binario deve essere quello di un commit, o
# nessuno potra' mai dire da che codice viene.
$sporco = @(EseguiGit @('status', '--porcelain') | Where-Object { $_ })
if ($sporco.Count -gt 0) {
    $sporco | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Ferma "l'albero di lavoro ha modifiche non committate"
}
Ok "albero di lavoro pulito"

# E SPINTO: una Release che punta a un commit che il remoto non ha e' un tag
# che nessun altro puo' risolvere.
$null = EseguiGit @('fetch', '--quiet', 'origin')
$locale = (EseguiGit @('rev-parse', 'HEAD')) | Select-Object -First 1
$remoto = (EseguiGit @('rev-parse', 'origin/main')) | Select-Object -First 1
if ($locale -ne $remoto) { Ferma "HEAD ($locale) non e' origin/main ($remoto): prima si spinge" }
Ok "HEAD e' origin/main ($($locale.Substring(0,7)))"

$esiste = @(EseguiGit @('tag', '-l', $tag) | Where-Object { $_ })
if ($esiste.Count -gt 0) { Ferma "il tag $tag esiste gia': una versione si rilascia una volta" }
Ok "il tag $tag non esiste ancora"

Write-Output ""
Write-Output "=== la compilazione"
& (Join-Path $radice "build.ps1") | Out-Null
if ($LASTEXITCODE -ne 0) { Ferma "build.ps1 e' uscito $LASTEXITCODE" }
$exe = Join-Path $radice "dist\Installa.exe"
$sha = Join-Path $radice "dist\Installa.exe.sha256"
if (-not (Test-Path $exe)) { Ferma "manca dist\Installa.exe" }
if (-not (Test-Path $sha)) { Ferma "manca dist\Installa.exe.sha256" }
Ok ("dist\Installa.exe, " + [int]((Get-Item $exe).Length / 1KB) + " kB")

# LO SHA256 SI RICALCOLA, NON SI CREDE AL FILE ACCANTO. E' il valore che chi
# scarica confrontera': se il file .sha256 e' rimasto di una compilazione
# precedente, la verifica che QUICKSTART insegna direbbe "manomesso" a tutti.
$vero = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$dichiarato = ((Get-Content $sha -Raw).Trim() -split '\s+')[0].ToLower()
if ($vero -ne $dichiarato) { Ferma "lo sha256 accanto all'eseguibile non e' il suo ($dichiarato contro $vero)" }
Ok "sha256 ricalcolato e uguale: $($vero.Substring(0,16))..."

# LA SUITE DI CONTRATTO, VERDE. Dentro ci sono i due controlli che una Release
# deve avere: la versione che il ponte annuncia e' quella di VERSION.txt, e
# l'ultimo giro di banco e' posteriore all'ultima modifica della libreria e del
# banco. Una Release su un banco vecchio e' un binario che nessuno ha misurato.
$prove = Join-Path $radice "Prove\bin\Prove.Contratti.exe"
if (-not (Test-Path $prove)) { Ferma "manca Prove\bin\Prove.Contratti.exe: si compila Prove\Prove.Contratti.csproj" }
$vecchio = $ErrorActionPreference; $ErrorActionPreference = "Continue"
$esito = & $prove 2>&1 | Out-String
$codice = $LASTEXITCODE
$ErrorActionPreference = $vecchio
$riga = ($esito -split "`n" | Where-Object { $_ -match 'controlli, \d+ falliti' } | Select-Object -Last 1)
if ($codice -ne 0) {
    ($esito -split "`n" | Where-Object { $_ -match '^\s+NO ' }) | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Ferma ("la suite di contratto non e' verde: " + $riga)
}
Ok ("suite di contratto: " + $riga.Trim())

Write-Output ""
Write-Output "=== le note della Release"
$note = Join-Path $radice ("dist\note-di-rilascio-" + $versione + ".md")
$cl[$inizio..($fine - 1)] | Out-File $note -Encoding utf8
Ok ("dist\note-di-rilascio-" + $versione + ".md, dalla voce del CHANGELOG")

Write-Output ""
Write-Output "PRONTA. Non e' stato pubblicato niente. I tre passi, da fare a mano:"
Write-Output ""
Write-Output "  git tag -a $tag -m ""$tag"""
Write-Output "  git push origin $tag"
Write-Output "  gh release create $tag dist\Installa.exe dist\Installa.exe.sha256 --title ""$tag"" --notes-file ""$note"""
Write-Output ""
Write-Output "Il repository e' privato: la Release lo e' con lui."
exit 0
