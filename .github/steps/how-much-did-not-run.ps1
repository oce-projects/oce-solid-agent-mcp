# =============================================================================
#  Quanto di questo giro NON ha potuto girare, scritto dove si vede
# =============================================================================
#
#  IL NUMERO CHE NON SI DEVE POTER NON VEDERE. Un verde di GitHub e' un segno
#  di spunta: chi lo guarda non apre il log. Il conto dei saltati va nel
#  riepilogo della corsa, dove si legge senza aprire niente.
#
#  Legge `prove.xml`, che e' JUnit e porta gia' `<skipped>` per caso e
#  `skipped=` per suite.
#
#      .\.github\passi\quanto-non-ha-girato.ps1
# =============================================================================

[CmdletBinding()]
param(
    [string] $File = 'Prove\bin\prove.xml'
)

$ErrorActionPreference = 'Stop'

function Scrivi($righe) {
    $righe | ForEach-Object { Write-Output $_ }
    if ($env:GITHUB_STEP_SUMMARY) {
        ($righe -join "`n") | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
}

if (-not (Test-Path $File)) {
    Scrivi @("### prove.xml non e' stato scritto ($File)")
    exit 0
}

[xml] $x = Get-Content $File
$tutti   = @($x.testsuites.testsuite.testcase)
$saltati = @($tutti | Where-Object { $_.skipped })
$rossi   = @($tutti | Where-Object { $_.failure })
$fatti   = $tutti.Count - $saltati.Count

$r = @()
$r += "### Prove.Contratti"
$r += ""
$r += "- **$fatti** controlli fatti davvero"
$r += "- **$($rossi.Count)** falliti"
$r += "- **$($saltati.Count)** non eseguibili qui"
if ($saltati.Count -gt 0) {
    $r += ""
    # Apici singoli: dentro ci sono le virgolette della citazione, e in una
    # stringa fra virgolette doppie sarebbero la fine della stringa.
    $r += '"Non eseguibile qui" **non vuol dire passato**: su questo runner SOLIDWORKS'
    $r += "non c'e', quindi il ponte non si poteva nemmeno compilare e i controlli che"
    $r += "gli parlano non hanno girato."
    $r += ""
    foreach ($s in $saltati) { $r += "- ``$($s.name)``" }
}
Scrivi $r
