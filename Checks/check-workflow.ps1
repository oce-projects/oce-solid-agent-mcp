# =============================================================================
#  Il PowerShell della CI sta in piedi, e lo si sa QUI (voce F9-bis)
# =============================================================================
#
#  Il difetto che ha fatto nascere la voce F9-bis e' "la CI non e' mai girata".
#  Il rimedio ha aggiunto tre passi in PowerShell: se stessero dentro lo YAML,
#  un errore di sintassi si scoprirebbe alla prima corsa -- cioe' potenzialmente
#  mai, che e' esattamente il difetto da cui si veniva.
#
#  Quindi i passi stanno in `.github\passi\*.ps1`, e questo li fa leggere al
#  parser di PowerShell senza eseguirli. Non prova che facciano la cosa giusta:
#  prova che si possano leggere, che e' il minimo che una CI mai girata non
#  aveva.
#
#  SI CONTROLLA ANCHE CHE IL WORKFLOW LI CHIAMI. Uno script perfetto che
#  nessun passo invoca e' il gemello del rimedio documentato e non eseguito.
#
#      .\Controlli\controlla-workflow.ps1
#
#  Esce 0 se e' tutto a posto, 1 altrimenti.
# =============================================================================

$ErrorActionPreference = 'Stop'
$radice = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)
$guai = 0

$cartella = Join-Path $radice '.github\passi'
$workflow = Join-Path $radice '.github\workflows\prove.yml'

Write-Host "=== I passi in PowerShell della CI"

if (-not (Test-Path $cartella)) {
    Write-Host "  XX  non c'e' .github\passi" -ForegroundColor Red
    exit 1
}

$passi = @(Get-ChildItem $cartella -Filter *.ps1 | Sort-Object Name)
if ($passi.Count -eq 0) {
    # UN CONTROLLO CHE NON TROVA NIENTE NON E' UN CONTROLLO VERDE. Se un giorno
    # la cartella si svuota, questo script direbbe "tutto a posto" su zero file.
    Write-Host "  XX  nessun .ps1 in .github\passi: o sono spariti, o sono" -ForegroundColor Red
    Write-Host "      tornati dentro lo YAML, e in tutti e due i casi questo" -ForegroundColor Red
    Write-Host "      controllo non guarda piu' niente." -ForegroundColor Red
    exit 1
}

foreach ($p in $passi) {
    $errori = $null
    $token = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        $p.FullName, [ref] $token, [ref] $errori)
    if ($errori -and $errori.Count -gt 0) {
        $guai++
        Write-Host ("  XX  " + $p.Name) -ForegroundColor Red
        foreach ($e in $errori) {
            Write-Host ("        riga " + $e.Extent.StartLineNumber + ": " + $e.Message)
        }
    } else {
        Write-Host ("  ok  " + $p.Name + " si legge")
    }
}

Write-Host ""
Write-Host "=== E il workflow li chiama"

if (-not (Test-Path $workflow)) {
    Write-Host "  XX  non c'e' .github\workflows\prove.yml" -ForegroundColor Red
    exit 1
}
$testo = Get-Content $workflow -Raw

foreach ($p in $passi) {
    if ($testo -like ('*' + $p.Name + '*')) {
        Write-Host ("  ok  " + $p.Name + " e' invocato")
    } else {
        $guai++
        Write-Host ("  XX  " + $p.Name + " non compare in prove.yml: e' uno") -ForegroundColor Red
        Write-Host  "      script che nessuno esegue, cioe' niente." -ForegroundColor Red
    }
}

Write-Host ""
if ($guai -eq 0) { Write-Host "A posto." -ForegroundColor Green; exit 0 }
Write-Host ("$guai da sistemare.") -ForegroundColor Red
exit 1
