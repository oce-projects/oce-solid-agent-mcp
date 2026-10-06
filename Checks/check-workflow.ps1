# =============================================================================
#  The CI PowerShell stands up, and we know it HERE (entry F9-bis)
# =============================================================================
#
#  The defect that gave birth to entry F9-bis is "the CI never ran".
#  The remedy added three steps in PowerShell: if they sat inside the YAML,
#  a syntax error would be discovered at the first run -- that is potentially
#  never, which is exactly the defect we came from.
#
#  So the steps live in `.github\steps\*.ps1`, and this makes the PowerShell parser
#  read them without executing them. It does not prove that they do the right thing:
#  it proves that they can be read, which is the minimum that a CI that never ran
#  did not have.
#
#  IT ALSO CHECKS THAT THE WORKFLOW CALLS THEM. A perfect script that
#  no step invokes is the twin of the remedy documented and not executed.
#
#      .\Checks\check-workflow.ps1
#
#  Exits 0 if everything is in order, 1 otherwise.
# =============================================================================

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)
$trouble = 0

$folder = Join-Path $root '.github\steps'
$workflow = Join-Path $root '.github\workflows\tests.yml'

Write-Host "=== The CI's PowerShell steps"

if (-not (Test-Path $folder)) {
    Write-Host "  XX  .github\steps is not there" -ForegroundColor Red
    exit 1
}

$steps = @(Get-ChildItem $folder -Filter *.ps1 | Sort-Object Name)
if ($steps.Count -eq 0) {
    # A CHECK THAT FINDS NOTHING IS NOT A GREEN CHECK. If one day
    # the folder empties, this script would say "all fine" about zero files.
    Write-Host "  XX  no .ps1 in .github\steps: either they are gone, or they are" -ForegroundColor Red
    Write-Host "      back inside the YAML, and in both cases this" -ForegroundColor Red
    Write-Host "      check no longer looks at anything." -ForegroundColor Red
    exit 1
}

foreach ($p in $steps) {
    $errors = $null
    $token = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        $p.FullName, [ref] $token, [ref] $errors)
    if ($errors -and $errors.Count -gt 0) {
        $trouble++
        Write-Host ("  XX  " + $p.Name) -ForegroundColor Red
        foreach ($e in $errors) {
            Write-Host ("        riga " + $e.Extent.StartLineNumber + ": " + $e.Message)
        }
    } else {
        Write-Host ("  ok  " + $p.Name + " can be read")
    }
}

Write-Host ""
Write-Host "=== And the workflow calls them"

if (-not (Test-Path $workflow)) {
    Write-Host "  XX  .github\workflows\tests.yml is not there" -ForegroundColor Red
    exit 1
}
$text = Get-Content $workflow -Raw

foreach ($p in $steps) {
    if ($text -like ('*' + $p.Name + '*')) {
        Write-Host ("  ok  " + $p.Name + " is invoked")
    } else {
        $trouble++
        Write-Host ("  XX  " + $p.Name + " does not appear in tests.yml: it is a") -ForegroundColor Red
        Write-Host  "      script nobody runs, that is, nothing." -ForegroundColor Red
    }
}

Write-Host ""
if ($trouble -eq 0) { Write-Host "All good." -ForegroundColor Green; exit 0 }
Write-Host ("$trouble to fix.") -ForegroundColor Red
exit 1
