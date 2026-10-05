# Sorveglia le proprieta' di PlanExecutor che il PONTE accende e l'ADD-IN no.
#
# Sono tre, e hanno la stessa forma: aprono una strada che nell'add-in deve
# restare chiusa.
#
#   DocumentWithoutFile  lascia lavorare su un documento mai salvato su disco.
#                       Per il ponte e' il caso normale: si disegna su `Parte1`
#                       appena aperta. Per l'add-in no, perche' dentro un PDM un
#                       file non salvato non e' in check-out, e scriverci
#                       produce modifiche che al salvataggio falliscono o
#                       finiscono in una copia.
#
#   AlsoOnDrawings       lascia lavorare su una tavola. Il ponte ha cinque
#                       operazioni di tavola in catalogo; l'assistente
#                       dell'add-in disegna pezzi, e un piano che gli arriva su
#                       una tavola e' quasi sempre un errore di chi ha cambiato
#                       finestra.
#
#   AlsoOnAssemblies      lascia lavorare su un assieme. Aggiunta il 18/9/2026:
#                       fino a quel giorno l'esecutore rifiutava ogni assieme
#                       mentre il catalogo servito ne prometteva quattro
#                       operazioni. Il banco non poteva vederlo, perche' chiama
#                       la libreria e non il ponte.
#
# Il pericolo e' scritto nella voce B8 del TODO ed e' preciso: che qualcuno le
# metta a vero anche nell'add-in "perche' davano fastidio". Un booleano che
# disattiva una difesa e' un invito a disattivarla.
#
# COSA CONTROLLA, E COSA NON PUO' CONTROLLARE.
#   1. dentro questo repository: ognuna si definisce una volta sola e si accende
#      in UN posto solo, e quel posto e' Bridge\Session.cs;
#   2. dentro l'add-in, SE le sue sorgenti sono raggiungibili da questa
#      macchina: non devono comparire affatto.
#
# Il secondo dipende da un altro repository. Quando non lo trova NON dice "tutto
# a posto": dice a voce alta che quella meta' non l'ha guardata. Un controllo
# che passa per non aver guardato e' peggio di nessun controllo, perche'
# rassicura.
#
#   .\Checks\check-documents-without-file.ps1
#
# Esce 0 se torna tutto, 1 se no.
#
# NOTA: questo file e' in ASCII puro, come tutti gli .ps1 del progetto.
# PowerShell 5.1 legge uno script senza BOM come ANSI, e un trattino lungo
# dentro una stringa gli fa sbagliare il parsing.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$names = @('DocumentWithoutFile', 'AlsoOnDrawings', 'AlsoOnAssemblies')
$trouble = 0

function Male($line) { Write-Host "  XX $line"; $script:trouble++ }
function Fine($line) { Write-Host "  ok $line" }

Write-Host "=== Dentro questo repository"

foreach ($itemName in $names) {
    # Dove si DEFINISCE: una volta sola, nella libreria.
    $defs = @(Get-ChildItem -Path (Join-Path $root 'Oce.Solid.Agent.Core') -Filter *.cs -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -SimpleMatch "public bool $itemName")
    if ($defs.Count -eq 1) {
        Fine "$itemName definita una volta sola: $($defs[0].Filename)"
    } else {
        Male "$($defs.Count) definizioni di $itemName, attesa una"
    }

    # Dove si ACCENDE: solo il ponte. Si cerca l'ASSEGNAMENTO e non il nome
    # nudo, cosi' i commenti che ne parlano (compreso questo file) non contano.
    $activations = @(Get-ChildItem -Path $root -Filter *.cs -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -Pattern "$itemName\s*=\s*true")
    if ($activations.Count -eq 0) {
        Male "nessuno accende ${itemName}: o la riga e' sparita, o questo controllo non guarda piu' dove serve"
    } elseif ($activations.Count -gt 1) {
        Male "$itemName acceso in $($activations.Count) posti:"
        foreach ($a in $activations) { Write-Host "       $($a.Path):$($a.LineNumber)" }
    } else {
        $location = $activations[0].Path
        if ($location -like '*\Bridge\Session.cs') {
            Fine "$itemName acceso solo in Bridge\Session.cs"
        } else {
            Male "$itemName acceso in $location, che non e' Bridge\Session.cs"
        }
    }
}

Write-Host ""
Write-Host "=== Dentro l'add-in"

# L'add-in vive in un altro repository. Il percorso si dice con RISCONTRO_ADDIN.
#
# QUI C'ERA UN RIPIEGO, e il ripiego era il percorso di QUESTA macchina verso
# un ALTRO repository che non e' pubblicabile. Su ogni altra macchina non
# esisteva e il controllo saltava questa meta' dicendolo - cioe' il ripiego non
# serviva a nessuno tranne che a chi l'aveva scritto, e intanto pubblicava dove
# tiene le cose. Senza, il messaggio qui sotto spiega gia' cosa fare.
$addin = $env:RISCONTRO_ADDIN

# `-not $addin` PRIMA di `Test-Path`, e non e' un dettaglio di stile: tolto il
# ripiego la variabile puo' essere vuota, e `Test-Path $null` in PowerShell 5.1
# non risponde `$false` - solleva «Impossibile associare l'argomento al
# parametro Path perche' e' null», che e' un errore non terminante. Lo script
# proseguirebbe nel ramo sbagliato dopo averlo stampato.
if (-not $addin -or -not (Test-Path $addin)) {
    if ($addin) { Write-Host "  -- sorgenti dell'add-in non raggiungibili da qui ($addin)." }
    else        { Write-Host "  -- RISCONTRO_ADDIN non e' impostata." }
    Write-Host "     QUESTA META' NON E' STATA CONTROLLATA."
    Write-Host "     Indica dove sono con RISCONTRO_ADDIN per controllarla."
} else {
    foreach ($itemName in $names) {
        $foundOnes = @(Get-ChildItem -Path $addin -Filter *.cs -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|esterni)\\' } |
            Select-String -SimpleMatch $itemName)
        if ($foundOnes.Count -eq 0) {
            Fine "l'add-in non nomina $itemName"
        } else {
            Male "l'add-in nomina $itemName in $($foundOnes.Count) punti:"
            foreach ($t in $foundOnes) { Write-Host "       $($t.Path):$($t.LineNumber)" }
        }
    }
}

Write-Host ""
if ($trouble -eq 0) { Write-Host 'Tutto torna.'; exit 0 }
Write-Host "$trouble problemi."
exit 1
