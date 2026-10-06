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
    Write-Output "NOT BUILT: Tests\bin\Oce.Solid.Agent.ContractTests.exe"
    exit 1
}
Write-Output "Oce.Solid.Agent.ContractTests.exe is there"

if ($InteropIsThere -eq 'si' -and -not (Test-Path 'bin\oce-solid-agent-mcp.exe')) {
    Write-Output "The interop is there but the bridge was not built: that is a defect,"
    Write-Output "not a limitation of this machine."
    exit 1
}

if ($InteropIsThere -eq 'si') { Write-Output "and the bridge is there too" }
else { Write-Output "the bridge cannot be built here, and that is expected" }
