# =============================================================================
#  Is the Dassault interop on this machine?
# =============================================================================
#
#  Writes `found=yes|no` (and `where=...`) to $env:GITHUB_OUTPUT, so that the step
#  that builds the bridge can skip itself where it cannot build.
#
#  WHY IT LIVES IN A FILE AND NOT INSIDE THE WORKFLOW. The defect that gave rise
#  to the entry F9-bis is "the CI has never run": writing PowerShell
#  inside a YAML that nobody executes is the same defect moved up one
#  level -- a syntax error would be found on the first run, that is, never.
#  In a `.ps1` it is caught by `Checks\check-workflow.ps1`, which runs
#  locally, and it can be launched by hand.
#
#  IT LOOKS WHERE MSBUILD LOOKS, IN THE SAME ORDER. The question is exactly
#  "would msbuild have found it?": looking for it elsewhere would answer a
#  different question.
# =============================================================================

$ErrorActionPreference = 'Stop'

$places = @()
if ($env:SW_INSTALL_PATH) { $places += (Join-Path $env:SW_INSTALL_PATH 'api\redist') }
if ($env:SW_REDIST)       { $places += $env:SW_REDIST }
$places += 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'

$found = $null
foreach ($p in $places) {
    if (Test-Path (Join-Path $p 'SolidWorks.Interop.sldworks.dll')) { $found = $p; break }
}

# Outside GitHub (that is, when it is launched by hand to try it) there is no
# $env:GITHUB_OUTPUT: it just prints, instead of blowing up. A script that
# can only be tried inside the CI is a script that is never tried.
function SayIt($line) {
    Write-Output $line
    if ($env:GITHUB_OUTPUT) { $line | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
}

if ($found) {
    Write-Output "interop found: $found"
    SayIt "found=yes"
    SayIt "where=$found"
} else {
    Write-Output "interop NOT found: the bridge cannot be built here, and that is expected."
    Write-Output "searched in:"
    $places | ForEach-Object { Write-Output "  $_" }
    SayIt "found=no"
}
