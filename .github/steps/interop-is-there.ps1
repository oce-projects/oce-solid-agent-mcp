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

$places = @()
if ($env:SW_INSTALL_PATH) { $places += (Join-Path $env:SW_INSTALL_PATH 'api\redist') }
if ($env:SW_REDIST)       { $places += $env:SW_REDIST }
$places += 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'

$found = $null
foreach ($p in $places) {
    if (Test-Path (Join-Path $p 'SolidWorks.Interop.sldworks.dll')) { $found = $p; break }
}

# Fuori da GitHub (cioe' quando si lancia a mano per provarlo) non c'e'
# $env:GITHUB_OUTPUT: si stampa e basta, invece di esplodere. Uno script che
# si puo' provare solo dentro la CI e' uno script che non si prova.
function SayIt($line) {
    Write-Output $line
    if ($env:GITHUB_OUTPUT) { $line | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
}

if ($found) {
    Write-Output "interop trovata: $found"
    SayIt "ce=si"
    SayIt "dove=$found"
} else {
    Write-Output "interop NON trovata: il ponte qui non si compila, ed e' atteso."
    Write-Output "cercata in:"
    $places | ForEach-Object { Write-Output "  $_" }
    SayIt "ce=no"
}
