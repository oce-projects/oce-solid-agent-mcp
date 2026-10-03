# Sorveglia le proprieta' di EsecutorePiano che il PONTE accende e l'ADD-IN no.
#
# Sono tre, e hanno la stessa forma: aprono una strada che nell'add-in deve
# restare chiusa.
#
#   DocumentoSenzaFile  lascia lavorare su un documento mai salvato su disco.
#                       Per il ponte e' il caso normale: si disegna su `Parte1`
#                       appena aperta. Per l'add-in no, perche' dentro un PDM un
#                       file non salvato non e' in check-out, e scriverci
#                       produce modifiche che al salvataggio falliscono o
#                       finiscono in una copia.
#
#   AncheSuTavole       lascia lavorare su una tavola. Il ponte ha cinque
#                       operazioni di tavola in catalogo; l'assistente
#                       dell'add-in disegna pezzi, e un piano che gli arriva su
#                       una tavola e' quasi sempre un errore di chi ha cambiato
#                       finestra.
#
#   AncheSuAssiemi      lascia lavorare su un assieme. Aggiunta il 18/9/2026:
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
$radice = Split-Path -Parent $PSScriptRoot
$nomi = @('DocumentoSenzaFile', 'AncheSuTavole', 'AncheSuAssiemi')
$guai = 0

function Male($riga) { Write-Host "  XX $riga"; $script:guai++ }
function Bene($riga) { Write-Host "  ok $riga" }

Write-Host "=== Dentro questo repository"

foreach ($nome in $nomi) {
    # Dove si DEFINISCE: una volta sola, nella libreria.
    $definizioni = @(Get-ChildItem -Path (Join-Path $radice 'Oce.Solid.Agent.Core') -Filter *.cs -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -SimpleMatch "public bool $nome")
    if ($definizioni.Count -eq 1) {
        Bene "$nome definita una volta sola: $($definizioni[0].Filename)"
    } else {
        Male "$($definizioni.Count) definizioni di $nome, attesa una"
    }

    # Dove si ACCENDE: solo il ponte. Si cerca l'ASSEGNAMENTO e non il nome
    # nudo, cosi' i commenti che ne parlano (compreso questo file) non contano.
    $accensioni = @(Get-ChildItem -Path $radice -Filter *.cs -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -Pattern "$nome\s*=\s*true")
    if ($accensioni.Count -eq 0) {
        Male "nessuno accende ${nome}: o la riga e' sparita, o questo controllo non guarda piu' dove serve"
    } elseif ($accensioni.Count -gt 1) {
        Male "$nome acceso in $($accensioni.Count) posti:"
        foreach ($a in $accensioni) { Write-Host "       $($a.Path):$($a.LineNumber)" }
    } else {
        $dove = $accensioni[0].Path
        if ($dove -like '*\Bridge\Session.cs') {
            Bene "$nome acceso solo in Bridge\Session.cs"
        } else {
            Male "$nome acceso in $dove, che non e' Bridge\Session.cs"
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
    foreach ($nome in $nomi) {
        $trovate = @(Get-ChildItem -Path $addin -Filter *.cs -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|esterni)\\' } |
            Select-String -SimpleMatch $nome)
        if ($trovate.Count -eq 0) {
            Bene "l'add-in non nomina $nome"
        } else {
            Male "l'add-in nomina $nome in $($trovate.Count) punti:"
            foreach ($t in $trovate) { Write-Host "       $($t.Path):$($t.LineNumber)" }
        }
    }
}

Write-Host ""
if ($guai -eq 0) { Write-Host 'Tutto torna.'; exit 0 }
Write-Host "$guai problemi."
exit 1
