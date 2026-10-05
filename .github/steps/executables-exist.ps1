# =============================================================================
#  Gli eseguibili esistono davvero, prima di credere a un esito
# =============================================================================
#
#  UNA CI CHE COMPILA IL VUOTO RESTA VERDE PER MESI. Si guarda che i file ci
#  siano, e soprattutto si guarda quello che DOVEVA esserci: su un runner con
#  SOLIDWORKS un ponte mancante e' un difetto vero, e senza questa riga la
#  suite lo chiamerebbe "non eseguibile qui" -- cioe' scuserebbe un difetto con
#  una limitazione della macchina.
#
#      .\.github\steps\executables-exist.ps1 -InteropIsThere si
# =============================================================================

[CmdletBinding()]
param(
    # 'si' oppure 'no': lo dice il passo `interop-is-there.ps1`.
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
