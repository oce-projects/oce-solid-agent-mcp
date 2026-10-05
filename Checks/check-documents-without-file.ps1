# Watches the PlanExecutor properties that the BRIDGE turns on and the ADD-IN does not.
#
# There are three, and they have the same shape: they open a road that in the add-in must
# stay closed.
#
#   DocumentWithoutFile  lets it work on a document never saved to disk.
#                       For the bridge it is the normal case: we draw on `Parte1`
#                       just opened. For the add-in it is not, because inside a PDM an
#                       unsaved file is not checked out, and writing to it
#                       produces changes that fail on save or
#                       end up in a copy.
#
#   AlsoOnDrawings       lets it work on a drawing. The bridge has five
#                       drawing operations in the catalogue; the add-in's
#                       assistant draws parts, and a plan that reaches it on
#                       a drawing is almost always a mistake of whoever changed
#                       window.
#
#   AlsoOnAssemblies      lets it work on an assembly. Added on 18/9/2026:
#                       until that day the executor refused every assembly
#                       while the served catalogue promised four
#                       operations on them. The bench could not see it, because it calls
#                       the library and not the bridge.
#
# The danger is written in entry B8 of the TODO and is precise: that somebody
# sets them to true in the add-in too "because they were annoying". A boolean that
# turns off a defence is an invitation to turn it off.
#
# WHAT IT CHECKS, AND WHAT IT CANNOT CHECK.
#   1. inside this repository: each one is defined once only and turned on
#      in ONE place only, and that place is Bridge\Session.cs;
#   2. inside the add-in, IF its sources are reachable from this
#      machine: they must not appear at all.
#
# The second depends on another repository. When it does not find it, it does NOT say "all
# fine": it says out loud that it did not look at that half. A check
# that passes because it did not look is worse than no check, because
# it reassures.
#
#   .\Checks\check-documents-without-file.ps1
#
# Exits 0 if everything adds up, 1 if not.
#
# NOTE: this file is pure ASCII, like all the .ps1 files of the project.
# PowerShell 5.1 reads a script without a BOM as ANSI, and a long dash
# inside a string makes it misparse.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$names = @('DocumentWithoutFile', 'AlsoOnDrawings', 'AlsoOnAssemblies')
$trouble = 0

function Male($line) { Write-Host "  XX $line"; $script:trouble++ }
function Fine($line) { Write-Host "  ok $line" }

Write-Host "=== Dentro questo repository"

foreach ($itemName in $names) {
    # Where it is DEFINED: once only, in the library.
    $defs = @(Get-ChildItem -Path (Join-Path $root 'Oce.Solid.Agent.Core') -Filter *.cs -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -SimpleMatch "public bool $itemName")
    if ($defs.Count -eq 1) {
        Fine "$itemName definita una volta sola: $($defs[0].Filename)"
    } else {
        Male "$($defs.Count) definizioni di $itemName, attesa una"
    }

    # Where it is TURNED ON: the bridge only. We search for the ASSIGNMENT and not the bare
    # name, so the comments that talk about it (this file included) do not count.
    $activations = @(Get-ChildItem -Path $root -Filter *.cs -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -Pattern "$itemName\s*=\s*true")
    if ($activations.Count -eq 0) {
        Male "nessuno accende ${itemName}: o la riga e' sparita, o questo controllo non guarda piu' dove serve"
    } elseif ($activations.Count -gt 1) {
        Male "$itemName acceso in $($activations.Count) posti:"
        foreach ($a in $activations) { Write-Host "       $($a.Path):$($a.LineNumber)" }
    } else {
        $location = $activations[0].Path
        if ($location -like '*\Bridge\Session.cs') {
            Fine "$itemName acceso solo in Bridge\Session.cs"
        } else {
            Male "$itemName acceso in $location, che non e' Bridge\Session.cs"
        }
    }
}

Write-Host ""
Write-Host "=== Dentro l'add-in"

# The add-in lives in another repository. The path is given with RISCONTRO_ADDIN.
#
# THERE USED TO BE A FALLBACK HERE, and the fallback was the path of THIS machine to
# ANOTHER repository that is not publishable. On every other machine it did not
# exist and the check skipped this half saying so - that is, the fallback was of
# no use to anybody except whoever wrote it, and meanwhile it published where
# it keeps its things. Without it, the message below already explains what to do.
$addin = $env:RISCONTRO_ADDIN

# `-not $addin` BEFORE `Test-Path`, and it is not a matter of style: with the
# fallback gone the variable can be empty, and `Test-Path $null` in PowerShell 5.1
# does not answer `$false` - it raises «Impossibile associare l'argomento al
# parametro Path perche' e' null», which is a non-terminating error. The script
# would carry on down the wrong branch after printing it.
if (-not $addin -or -not (Test-Path $addin)) {
    if ($addin) { Write-Host "  -- sorgenti dell'add-in non raggiungibili da qui ($addin)." }
    else        { Write-Host "  -- RISCONTRO_ADDIN non e' impostata." }
    Write-Host "     QUESTA META' NON E' STATA CONTROLLATA."
    Write-Host "     Indica dove sono con RISCONTRO_ADDIN per controllarla."
} else {
    foreach ($itemName in $names) {
        $foundOnes = @(Get-ChildItem -Path $addin -Filter *.cs -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|esterni)\\' } |
            Select-String -SimpleMatch $itemName)
        if ($foundOnes.Count -eq 0) {
            Fine "l'add-in non nomina $itemName"
        } else {
            Male "l'add-in nomina $itemName in $($foundOnes.Count) punti:"
            foreach ($t in $foundOnes) { Write-Host "       $($t.Path):$($t.LineNumber)" }
        }
    }
}

Write-Host ""
if ($trouble -eq 0) { Write-Host 'Tutto torna.'; exit 0 }
Write-Host "$trouble problemi."
exit 1
