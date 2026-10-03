# =============================================================================
#  C'e' l'interop di Dassault su questa macchina?
# =============================================================================
#
#  Scrive `ce=si|no` (e `dove=...`) su $env:GITHUB_OUTPUT, cosi' che il passo
#  che compila il ponte possa saltarsi da solo dove non si puo' compilare.
#
#  PERCHE' STA IN UN FILE E NON DENTRO IL WORKFLOW. Il difetto che ha fatto
#  nascere la voce F9-bis e' "la CI non e' mai girata": scrivere PowerShell
#  dentro un YAML che nessuno esegue e' lo stesso difetto spostato di un
#  livello -- un errore di sintassi si scoprirebbe alla prima corsa, cioe' mai.
#  In un `.ps1` lo prende `Checks\check-workflow.ps1`, che gira in
#  locale, e lo si puo' lanciare a mano.
#
#  SI CERCA DOVE CERCA MSBUILD, NELLO STESSO ORDINE. La domanda e' esattamente
#  "msbuild l'avrebbe trovata?": cercarla altrove risponderebbe a un'altra
#  domanda.
# =============================================================================

$ErrorActionPreference = 'Stop'

$posti = @()
if ($env:SW_INSTALL_PATH) { $posti += (Join-Path $env:SW_INSTALL_PATH 'api\redist') }
if ($env:SW_REDIST)       { $posti += $env:SW_REDIST }
$posti += 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'

$trovata = $null
foreach ($p in $posti) {
    if (Test-Path (Join-Path $p 'SolidWorks.Interop.sldworks.dll')) { $trovata = $p; break }
}

# Fuori da GitHub (cioe' quando si lancia a mano per provarlo) non c'e'
# $env:GITHUB_OUTPUT: si stampa e basta, invece di esplodere. Uno script che
# si puo' provare solo dentro la CI e' uno script che non si prova.
function Dillo($riga) {
    Write-Output $riga
    if ($env:GITHUB_OUTPUT) { $riga | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
}

if ($trovata) {
    Write-Output "interop trovata: $trovata"
    Dillo "ce=si"
    Dillo "dove=$trovata"
} else {
    Write-Output "interop NON trovata: il ponte qui non si compila, ed e' atteso."
    Write-Output "cercata in:"
    $posti | ForEach-Object { Write-Output "  $_" }
    Dillo "ce=no"
}
