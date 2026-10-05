# =============================================================================
#  error-enum.ps1 - quali enum di errore di SOLIDWORKS hanno
#                       "nessun errore" DIVERSO da zero        (voce Z-C7)
# =============================================================================
#
#  QUESTO FILE E' IN ASCII PURO, come bench.ps1 e per la stessa ragione.
#
#  PERCHE' ESISTE
#
#  `swAddMateError_NoError` vale 1, e `ErrorUknown` vale 0. Un `if (errore != 0)`
#  su quell'API e' esattamente rovesciato: dichiara fallito un accoppiamento
#  riuscito, e non da' nessun sintomo -- l'accoppiamento c'e', il messaggio dice
#  che non c'e'.
#
#  Non e' un caso isolato, e questo script serve a dire QUANTI sono invece di
#  ricordarselo. Li legge per riflessione dalle interop installate su questa
#  postazione: non serve che SOLIDWORKS sia aperto, serve che sia installato.
#
#  QUANDO RILANCIARLO
#
#  Quando cambia la versione di SOLIDWORKS. L'elenco misurato sta scritto in
#  `Tests/ErrorEnumTests.cs`, che e' un controllo di contratto e gira anche
#  in CI, dove le interop NON ci sono: li' l'elenco e' un dato, e un dato
#  copiato invecchia da solo. Questo script e' il modo di rinfrescarlo.
#
#  USO
#      .\Checks\error-enum.ps1
#      .\Checks\error-enum.ps1 -All     anche quelli che valgono 0
# =============================================================================

param([switch]$All)

$ErrorActionPreference = "Stop"

$candidates = @(
    "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\SolidWorks.Interop.swconst.dll"
)
if ($env:SW_INSTALL_PATH) {
    $candidates = @((Join-Path $env:SW_INSTALL_PATH "api\redist\SolidWorks.Interop.swconst.dll")) + $candidates
}

$dll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) {
    Write-Output "non trovo SolidWorks.Interop.swconst.dll. Cercato in:"
    $candidates | ForEach-Object { Write-Output ("  " + $_) }
    Write-Output "Indicala con la variabile d'ambiente SW_INSTALL_PATH."
    exit 2
}

Write-Output ("interop: " + $dll)
Write-Output ""

$asm = [Reflection.Assembly]::LoadFrom($dll)

# COSA CONTA COME "NIENTE ERRORE". I nomi non sono uniformi: NoError, _Ok,
# _None, Success. Cercarne uno solo vorrebbe dire non vedere gli altri, e un
# elenco che non vede meta' dei casi e' peggio di nessun elenco -- perche' si
# legge come completo.
$good = 'NoError|_Ok$|_None$|Success'

$lines = @()
foreach ($t in $asm.GetTypes()) {
    if (-not $t.IsEnum) { continue }
    if ($t.Name -notmatch 'Error|State') { continue }
    foreach ($n in [Enum]::GetNames($t)) {
        if ($n -notmatch $good) { continue }
        $lines += [pscustomobject]@{
            Enum   = $t.Name
            Buono  = $n
            Valore = [int][Enum]::Parse($t, $n)
        }
    }
}

$dangerous = $lines | Where-Object { $_.Valore -ne 0 } | Sort-Object Enum

Write-Output ("enum di errore o stato guardati: " + ($lines | Select-Object -ExpandProperty Enum -Unique).Count)
Write-Output ("di cui con 'niente errore' DIVERSO da zero: " + $dangerous.Count)
Write-Output ""

if ($All) {
    $lines | Sort-Object Valore, Enum | Format-Table -AutoSize | Out-String -Width 120 | Write-Output
} else {
    $dangerous | Format-Table -AutoSize | Out-String -Width 120 | Write-Output
}

Write-Output "Su questi, `if (errore != 0)` e' ROVESCIATO."
Write-Output "Si confronta col membro che ha il nome, mai con una cifra."
