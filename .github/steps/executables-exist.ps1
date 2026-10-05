# =============================================================================
#  The executables really exist, before believing a result
# =============================================================================
#
#  A CI THAT BUILDS NOTHING STAYS GREEN FOR MONTHS. It checks that the files
#  are there, and above all it checks what MUST have been there: on a runner with
#  SOLIDWORKS a missing bridge is a real defect, and without this line the
#  suite would call it "not runnable here" -- that is, it would excuse a defect
#  with a limitation of the machine.
#
#      .\.github\steps\executables-exist.ps1 -InteropIsThere si
# =============================================================================

[CmdletBinding()]
param(
    # 'si' or 'no': the step `interop-is-there.ps1` says which.
    [string] $InteropIsThere = 'no'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path 'Tests\bin\Oce.Solid.Agent.ContractTests.exe')) {
    Write-Output "NON COMPILATO: Tests\bin\Oce.Solid.Agent.ContractTests.exe"
    exit 1
}
Write-Output "Oce.Solid.Agent.ContractTests.exe c'e'"

if ($InteropIsThere -eq 'si' -and -not (Test-Path 'bin\oce-solid-agent-mcp.exe')) {
    Write-Output "L'interop c'e' ma il ponte non e' stato prodotto: e' un difetto,"
    Write-Output "non una mancanza di questa macchina."
    exit 1
}

if ($InteropIsThere -eq 'si') { Write-Output "e c'e' anche il ponte" }
else { Write-Output "il ponte qui non si compila, ed e' atteso" }
