# =============================================================================
#  How much of this run could NOT run, written where it can be seen
# =============================================================================
#
#  THE NUMBER THAT MUST NOT BE POSSIBLE TO MISS. A green from GitHub is a tick
#  mark: whoever looks at it does not open the log. The count of skipped tests goes
#  into the summary of the run, where it is read without opening anything.
#
#  It reads `prove.xml`, which is JUnit and already carries `<skipped>` per case
#  and `skipped=` per suite.
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
    WriteLine @("### prove.xml was not written ($File)")
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
$r += "- **$done** checks really done"
$r += "- **$($red.Count)** failed"
$r += "- **$($skippedOnes.Count)** not runnable here"
if ($skippedOnes.Count -gt 0) {
    $r += ""
    # Single quotes: inside are the quotation marks of the quote, and in a
    # string between double quotes they would be the end of the string.
    $r += '"Not runnable here" **does not mean passed**: on this runner SOLIDWORKS'
    $r += "is not there, so the bridge could not even be built and the checks that"
    $r += "talk to it did not run."
    $r += ""
    foreach ($s in $skippedOnes) { $r += "- ``$($s.name)``" }
}
WriteLine $r
