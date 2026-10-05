# =============================================================================
#  error-enum.ps1 - which SOLIDWORKS error enums have
#                       "no error" DIFFERENT from zero        (entry Z-C7)
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like bench.ps1 and for the same reason.
#
#  WHY IT EXISTS
#
#  `swAddMateError_NoError` is 1, and `ErrorUknown` is 0. An `if (errore != 0)`
#  on that API is exactly upside down: it declares a successful mate as
#  failed, and gives no symptom -- the mate is there, the message says
#  it is not.
#
#  It is not an isolated case, and this script serves to say HOW MANY there are instead of
#  remembering it. It reads them by reflection from the interops installed on this
#  workstation: SOLIDWORKS does not need to be open, it needs to be installed.
#
#  WHEN TO RERUN IT
#
#  When the SOLIDWORKS version changes. The measured list is written in
#  `Tests/ErrorEnumTests.cs`, which is a contract check and also runs
#  in CI, where the interops are NOT there: there the list is data, and data
#  that is copied ages by itself. This script is the way to refresh it.
#
#  USAGE
#      .\Checks\error-enum.ps1
#      .\Checks\error-enum.ps1 -All     also those that are worth 0
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

# WHAT COUNTS AS "NO ERROR". The names are not uniform: NoError, _Ok,
# _None, Success. Looking for only one would mean not seeing the others, and a
# list that does not see half of the cases is worse than no list -- because it
# reads as complete.
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
