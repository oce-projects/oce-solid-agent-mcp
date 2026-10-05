# =============================================================================
#  todo-signatures.ps1 - do the APIs cited in the TODO really exist?
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like bench.ps1 and for the same reason.
#
#  WHY IT EXISTS
#
#  Dozens of entries of the TODO rest on a signature copied from a documentation
#  page: "IModelDocExtension::InsertSurfaceFinishSymbol3",
#  "IFace2::GetCorrespondingEntity", "IEquationMgr". They are promises, and until
#  nobody verifies them they remain promises: **an entry that plans a job
#  around a method that does not exist is a job that starts badly.**
#
#  The house rule: we verify the thing, not what describes it. Here the
#  thing is the interop installed on THIS workstation -- that is exactly
#  the one against which the code will run.
#
#  SOLIDWORKS DOES NOT NEED TO BE OPEN: it needs to be installed.
#
#  ── WHAT "NOT FOUND" MEANS, AND WHAT IT DOES NOT MEAN ───────────────────────
#
#  It means: with that name, on that type, in THIS version, it is not there.
#  It does NOT mean that the entry is wrong: it may be a typo in the TODO, a
#  method that arrived in a newer version, or a member that sits on a type
#  different from the one cited. **It is a list of things to look at, not of faults.**
#
#  ── WHAT THIS PROBE DOES NOT SEE ────────────────────────────────────────────
#
#  The names written WITHOUT the type in front. On 23/9/2026 two wrong citations
#  slipped right under its nose exactly like that:
#
#      `AutoInsertCenterMarks2`   -- it is on IView, the TODO did not say so
#      `GetRemainingDOFs`         -- it is on IComponent2, ditto
#
#  Searching every word that looks like a method, without a type, would mean
#  collecting half of the prose and producing a list that nobody reads --
#  the disease that this file avoids by construction. The remedy is not a wider
#  regex: **it is writing the type in front of the method in the TODO.**
#
#  USAGE
#      .\Checks\todo-signatures.ps1
#      .\Checks\todo-signatures.ps1 -All     also those that are there
# =============================================================================

param([switch]$All)

$ErrorActionPreference = "Continue"

$redist = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"
if ($env:SW_INSTALL_PATH) { $redist = Join-Path $env:SW_INSTALL_PATH "api\redist" }
if (-not (Test-Path $redist)) { Write-Output "non trovo $redist"; exit 2 }

# ALL the interops, not only sldworks: the entries also cite swconst,
# swdocumentmgr, gtswutilities, cosworks.
$types = @{}
foreach ($dll in (Get-ChildItem $redist -Filter "SolidWorks.Interop.*.dll")) {
    try {
        $asm = [Reflection.Assembly]::LoadFrom($dll.FullName)
        foreach ($t in $asm.GetTypes()) { if (-not $types.ContainsKey($t.Name)) { $types[$t.Name] = $t } }
    } catch { }
}
Write-Output ""
Write-Output ("interop caricate: " + (Get-ChildItem $redist -Filter 'SolidWorks.Interop.*.dll').Count +
              ",  tipi visti: " + $types.Count)

# THE TODO IS NO LONGER IN THE PUBLIC REPOSITORY: it is a work journal, it lives in
# `note-interne/` and the .gitignore excludes it. It is searched where it is today, where
# it was before, and where whoever keeps it elsewhere says. If it is not there, this probe
# has NOT passed: it did not check, and says so by exiting 0 - because three probes
# of the TODO run in the pre-push hook, and making them die here would break the push
# for anyone who clones, over a file that they do not have and must not have.
$repoRoot = Split-Path $PSScriptRoot -Parent
$todo = $env:TODO_MD
if (-not $todo -or -not (Test-Path $todo)) {
    $todo = $null
    foreach ($c in @((Join-Path $repoRoot 'note-interne\TODO.md'),
                     (Join-Path $repoRoot 'TODO.md'))) {
        if (Test-Path $c) { $todo = $c; break }
    }
}
if (-not $todo) {
    Write-Output ""
    Write-Output "Il TODO non c'e' in questo clone: e' un documento interno e non"
    Write-Output "viene pubblicato. QUESTA SONDA NON HA CONTROLLATO NIENTE."
    Write-Output "Se ne hai una copia, indicala con TODO_MD."
    exit 0
}
$text = Get-Content $todo -Raw

# `ITipo::Membro` or `ITipo.Membro`. Only what starts with a
# capital I followed by a capital is taken: it is the convention of the SOLIDWORKS COM interfaces,
# and without that filter half of the prose would be collected.
# THE MEMBER IS THE LAST IDENTIFIER BEFORE THE PARENTHESIS.
#
# The first draft took the first one after the colons, and the TODO writes the
# signatures as the API prints them:
#
#     IModelDocExtension :: SFSymbol InsertSurfaceFinishSymbol3(Int32 ...)
#
# that is `Tipo :: Ritorno Metodo(`. Result: twenty-three "missing members"
# that were **return types** -- IView.Object, IDrawingDoc.Void,
# IFeatureManager.Feature. *A probe that shouts twenty-three false alarms is
# read by nobody*, and it is the way in which a check dies.
#
# Two forms, in this order: with the method and the parenthesis, or a
# property without a parenthesis.
$foundOnes = [ordered]@{}

# 1. `ITipo :: [Ritorno] Metodo(`
foreach ($m in [regex]::Matches($text, '\b(I[A-Z][a-z]\w*)\s*(?:::|\.)\s*(?:[A-Za-z_]\w*\s+)?([A-Za-z_]\w*)\s*\(')) {
    $key = $m.Groups[1].Value + "." + $m.Groups[2].Value
    if (-not $foundOnes.Contains($key)) { $foundOnes[$key] = $true }
}

# 2. `ITipo.Property` without a parenthesis. The filter `I[A-Z][a-z]` keeps out
#    the acronyms written in prose -- «ISO», «IGES» -- which are not interfaces.
# Two negations, and both are needed:
#   (?!\s*\()            it is not a method -- that is taken by form 1
#   (?!\s+\w+\s*\()     and it is not a RETURN TYPE followed by the method
# Without the second, `IView :: Object InsertHoleTable3(` produces
# «IView.Object», which is the false alarm from before in disguise.
foreach ($m in [regex]::Matches($text, '\b(I[A-Z][a-z]\w*)\s*(?:::|\.)\s*([A-Z]\w*)\b(?!\s*\()(?!\s+[A-Za-z_]\w*\s*\()')) {
    $key = $m.Groups[1].Value + "." + $m.Groups[2].Value
    if (-not $foundOnes.Contains($key)) { $foundOnes[$key] = $true }
}

# ── THE CITATIONS THAT TALK ABOUT A WRONG NAME ──────────────────────────────
#
# On 23/9/2026 this probe found four non-existent signatures in the TODO. The
# corrections were written IN THE TODO, and the corrections **name the wrong
# name** to explain that it is wrong -- so the next run the probe
# flagged its own documentation.
#
# The search is not narrowed: the exception is declared with its why, as
# in the list of entry Z-C7. **A probe that silences itself stops
# seeing the real cases too.**
$sorry = @{
    "IMate2.Name"            = "la voce Z-A8 la cita per dire che NON esiste: verificato a mano prima, e da questa sonda dopo"
    "IAssemblyDoc.ViewMates" = "citata nella correzione che spiega che non esiste (i membri veri sono AddMate4/5, EditMate3/4, CopyWithMates)"
    "INewGtol.NewGtol"       = "citata nella correzione che spiega che INewGtol e' un METODO di IDrawingDoc, non un tipo"
}

Write-Output ("firme citate nel TODO: " + $foundOnes.Count +
              ",  di cui dichiarate inesistenti apposta: " + $sorry.Count)
Write-Output ""

$missingItems = @()
$absentTypes = @()
$ok = 0
foreach ($f in $foundOnes.Keys) {
    $p = $f.Split(".")
    $typeName = $p[0]; $memberName = $p[1]
    if ($sorry.ContainsKey($f)) { continue }
    if (-not $types.ContainsKey($typeName)) { $absentTypes += $f; continue }
    $t = $types[$typeName]
    $c = @($t.GetMembers() | Where-Object { $_.Name -eq $memberName })
    # COM properties also appear as get_Xxx
    if ($c.Count -eq 0) { $c = @($t.GetMembers() | Where-Object { $_.Name -eq ("get_" + $memberName) }) }
    if ($c.Count -eq 0) { $missingItems += $f } else { $ok++; if ($All) { Write-Output ("  ok   " + $f) } }
}

Write-Output ("verificate e presenti: " + $ok)
Write-Output ""
if ($absentTypes.Count -gt 0) {
    Write-Output ("TIPI che non esistono in queste interop: " + $absentTypes.Count)
    foreach ($x in $absentTypes) { Write-Output ("  ?  " + $x) }
    Write-Output ""
}
if ($missingItems.Count -gt 0) {
    Write-Output ("MEMBRI che il loro tipo NON ha: " + $missingItems.Count)
    foreach ($x in $missingItems) { Write-Output ("  !  " + $x) }
    Write-Output ""
}
if ($absentTypes.Count -eq 0 -and $missingItems.Count -eq 0) {
    Write-Output "Tutte le firme citate nel TODO esistono in questa installazione."
}

Write-Output "«Non trovato» e' una cosa da guardare, non una colpa: puo' essere un"
Write-Output "refuso, una versione piu' nuova, o un membro che sta su un altro tipo."
