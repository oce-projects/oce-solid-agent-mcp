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
#      .\.github\steps\how-much-did-not-run.ps1
# =============================================================================

[CmdletBinding()]
param(
    [string] $File = 'Tests\bin\prove.xml'
)

$ErrorActionPreference = 'Stop'

function WriteLine($lines) {
    $lines | ForEach-Object { Write-Output $_ }
    if ($env:GITHUB_STEP_SUMMARY) {
        ($lines -join "`n") | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
}

if (-not (Test-Path $File)) {
    WriteLine @("### prove.xml non e' stato scritto ($File)")
    exit 0
}

[xml] $x = Get-Content $File
$all   = @($x.testsuites.testsuite.testcase)
$skippedOnes = @($all | Where-Object { $_.skipped })
$red   = @($all | Where-Object { $_.failure })
$done   = $all.Count - $skippedOnes.Count

$r = @()
$r += "### Oce.Solid.Agent.ContractTests"
$r += ""
$r += "- **$done** controlli fatti davvero"
$r += "- **$($red.Count)** falliti"
$r += "- **$($skippedOnes.Count)** non eseguibili qui"
if ($skippedOnes.Count -gt 0) {
    $r += ""
    # Apici singoli: dentro ci sono le virgolette della citazione, e in una
    # stringa fra virgolette doppie sarebbero la fine della stringa.
    $r += '"Non eseguibile qui" **non vuol dire passato**: su questo runner SOLIDWORKS'
    $r += "non c'e', quindi il ponte non si poteva nemmeno compilare e i controlli che"
    $r += "gli parlano non hanno girato."
    $r += ""
    foreach ($s in $skippedOnes) { $r += "- ``$($s.name)``" }
}
WriteLine $r
