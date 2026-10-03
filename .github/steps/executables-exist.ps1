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
#      .\.github\passi\gli-eseguibili-ci-sono.ps1 -InteropCe si
# =============================================================================

[CmdletBinding()]
param(
    # 'si' oppure 'no': lo dice il passo `interop-ce.ps1`.
    [string] $InteropCe = 'no'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path 'Prove\bin\Prove.Contratti.exe')) {
    Write-Output "NON COMPILATO: Prove\bin\Prove.Contratti.exe"
    exit 1
}
Write-Output "Prove.Contratti.exe c'e'"

if ($InteropCe -eq 'si' -and -not (Test-Path 'bin\oce-solid-agent-mcp.exe')) {
    Write-Output "L'interop c'e' ma il ponte non e' stato prodotto: e' un difetto,"
    Write-Output "non una mancanza di questa macchina."
    exit 1
}

if ($InteropCe -eq 'si') { Write-Output "e c'e' anche il ponte" }
else { Write-Output "il ponte qui non si compila, ed e' atteso" }
