# =============================================================================
#  Does the catalogue declare operations that the executor can perform?
# =============================================================================
#
#  Exits 0 if everything adds up, 1 if not. It is meant to be run before a
#  delivery and by an automatic build.
#
#  WHY IT EXISTS. The catalogue (`catalog.json`) is the list of what the
#  model can ask for; the switch of `SwOperations.Run` is the list of
#  what can really be done. They are two places, and two places always diverge. Today
#  they diverge by three names, and those three names the model can ask for: it gets
#  «operazione sconosciuta» after having written a whole plan.
#
#  A CHECK MUST NOT BE ABLE TO EXIT 0 BECAUSE IT DID NOT LOOK.
#  It is the defect this check risked more than any other: reading the `case`
#  with a regex over 4,700 lines, finding none, and declaring «nessun
#  disallineamento». Here we defend in three ways:
#    1. if the catalogue has fewer than 20 operations we exit 1 saying it was not
#       read, instead of comparing two empty sets;
#    2. if fewer than 20 `case` are read from the source, same;
#    3. the list declared by the library (`SwOperations.ExecutableNames`) and
#       the one read from the source must MATCH each other: they are two
#       independent roads to the same fact, and if they diverge one of the two is
#       rotten.
#
#  EXPECTED KEYS (W3.1-bis.2, item 5). A property of an object read with
#  ConvertFrom-Json is $null when it is missing, without an error: after a
#  rename of the catalogue keys every comparison below would run on empty
#  sets and say that everything matches. So before anything else the
#  catalogue must have `operations` and `families`, and every operation `name`
#  and `family`; otherwise this exits 1 and says which key is missing.
#  `families` is an object (family -> description): until 30/9/2026 it was
#  read as a list, gave 0 declared families, and check 6 passed on nothing.
#
# =============================================================================

[CmdletBinding()]
param(
    [string]$Catalog,
    [string]$Library,
    [string]$Source,
    [string]$SwInstallPath
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)

if (-not $Catalog) { $Catalog = Join-Path $root "bin\catalog.json" }
if (-not $Library) { $Library = Join-Path $root "Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll" }
if (-not $Source) { $Source = Join-Path $root "Oce.Solid.Agent.Core\SwOperations.cs" }

$problems = New-Object System.Collections.Generic.List[string]
function Male($m) { $problems.Add($m) | Out-Null; Write-Host "  [X] $m" -ForegroundColor Red }
function Fine($m) { Write-Host "  [ok] $m" -ForegroundColor Green }
function Note($m) { Write-Host "       $m" -ForegroundColor DarkGray }

Write-Host ""
Write-Host "=== The catalog against the executor" -ForegroundColor Cyan

# --- 1. the catalogue --------------------------------------------------------
if (-not (Test-Path -LiteralPath $Catalog)) {
    Write-Host "  [X] cannot find the catalog: $Catalog" -ForegroundColor Red
    Write-Host ""
    Write-Host "1 problem: the catalog is not there. Build with build.ps1." -ForegroundColor Red
    exit 1
}
$cat = Get-Content -LiteralPath $Catalog -Raw -Encoding UTF8 | ConvertFrom-Json

# The expected keys, before anything reads them (see the header).
function Test-JsonKey($object, [string] $key) {
    return ($object -is [System.Management.Automation.PSCustomObject]) -and
           (@($object.PSObject.Properties | ForEach-Object { $_.Name }) -contains $key)
}
$missingKeys = New-Object System.Collections.Generic.List[string]
# The key names, once: the messages below name them through these.
$keyOperations = 'operations'
$keyFamilies = 'families'
$keyName = 'name'
$keyFamily = 'family'
foreach ($key in $keyOperations, $keyFamilies) {
    if (-not (Test-JsonKey $cat $key)) { $missingKeys.Add("the catalogue has no '$key'") }
}
if (Test-JsonKey $cat $keyOperations) {
    $index = 0
    foreach ($entry in @($cat.operations)) {
        foreach ($key in $keyName, $keyFamily) {
            if (-not (Test-JsonKey $entry $key) -or [string]::IsNullOrWhiteSpace([string]$entry.$key)) {
                $missingKeys.Add("operation $index has no '$key'")
            }
        }
        $index++
    }
}
# The declared families, read here and compared in step 6. `families` is an
# object, family -> description, as Oce.Solid.Agent.Catalog writes it; a list (of
# names, or of objects with `name`) is read too. The old reading, in step 6,
# took the object for one element without `name`, found 0 declared families,
# and the comparison passed on nothing.
$familiesDeclared = @()
if (Test-JsonKey $cat $keyFamilies) {
    $declaredFamilies = $cat.$keyFamilies
    if ($declaredFamilies -is [System.Management.Automation.PSCustomObject]) {
        $familiesDeclared = @($declaredFamilies.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object -Unique)
    } elseif ($declaredFamilies -is [array]) {
        $familiesDeclared = @(@(foreach ($f in $declaredFamilies) {
            if ($f -is [string]) { $f }
            elseif (Test-JsonKey $f $keyName) { $f.$keyName }
            else { $missingKeys.Add("an entry of '$keyFamilies' is neither a name nor an object with '$keyName'") }
        }) | Sort-Object -Unique)
    } else {
        $missingKeys.Add("'$keyFamilies' is neither an object nor a list")
    }
    if ($familiesDeclared.Count -eq 0) { $missingKeys.Add("'$keyFamilies' declares no family") }
}
if ($missingKeys.Count) {
    Write-Host ("  [X] expected keys missing in " + $Catalog + ": " + $missingKeys.Count) -ForegroundColor Red
    $missingKeys | Select-Object -First 20 | ForEach-Object { Write-Host "       $_" -ForegroundColor DarkGray }
    Write-Host ""
    Write-Host ("A missing key would read as `$null and every check below would compare empty sets. " +
                "If a key was renamed, this reader is renamed in the same commit.") -ForegroundColor Red
    exit 1
}
Fine ("the catalogue has the expected keys ($keyOperations, $keyFamilies; $keyName and $keyFamily in every operation), " +
      $familiesDeclared.Count + " declared families")

$operations = @($cat.operations)
$catalogNames = @($operations | ForEach-Object { $_.name } | Sort-Object -Unique)

if ($catalogNames.Count -lt 20) {
    Write-Host ("  [X] the catalog has only " + $catalogNames.Count + " operations: " +
                "I did not read it properly, and comparing two empty sets would say " +
                "that all is well.") -ForegroundColor Red
    exit 1
}
Fine ("catalogo: " + $catalogNames.Count + " operazioni")

# --- 2. what the executor DECLARES it can do --------------------------------
if (-not (Test-Path -LiteralPath $Library)) {
    Write-Host "  [X] cannot find the library: $Library" -ForegroundColor Red
    exit 1
}
# THE INTEROP OF SOLIDWORKS, found where it is installed (30/9/2026). The
# static initializer of SwOperations touches SolidWorks.Interop.swconst, and
# a library loaded from its bytes has no folder to look for it in: without
# this resolver the read of ExecutableNames below died with a
# TypeInitializationException on a machine where the interop is not in the
# GAC, and the check never got past this step. Same default and same
# variable as the .csproj files; -SwInstallPath wins.
if (-not $SwInstallPath) { $SwInstallPath = $env:SW_INSTALL_PATH }
if (-not $SwInstallPath) { $SwInstallPath = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS' }
if (-not ('CatalogueCheckInterop' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class CatalogueCheckInterop
{
    public static void Resolve(string redist)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            if (!name.StartsWith("SolidWorks.Interop.", StringComparison.OrdinalIgnoreCase)) return null;
            var file = Path.Combine(redist, name + ".dll");
            return File.Exists(file) ? Assembly.Load(File.ReadAllBytes(file)) : null;
        };
    }
}
'@
}
[CatalogueCheckInterop]::Resolve((Join-Path $SwInstallPath 'api\redist'))
# Load on the bytes and not LoadFrom: the library may sit on a network drive,
# where LoadFrom fails with 0x80131515 naming a dependency that does not exist.
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Library))
$typ = $asm.GetType("Oce.Solid.Agent.Core.SwOperations")
if (-not $typ) {
    Write-Host "  [X] Oce.Solid.Agent.Core.SwOperations is not in the library" -ForegroundColor Red
    exit 1
}
$theField = $typ.GetField("ExecutableNames")
if (-not $theField) {
    Write-Host ("  [X] SwOperations does not expose ExecutableNames: without it, this " +
                "check would have to guess.") -ForegroundColor Red
    exit 1
}
$declared = @($theField.GetValue($null) | Sort-Object -Unique)
Fine ("the executor declares: " + $declared.Count + " operations")

# --- 3. what is REALLY in the switch, read from the source -------------------
# A second road to the same fact: if the two diverge, one of the two is
# rotten, and it must be said instead of choosing the convenient one.
$fromSource = @()
if (Test-Path -LiteralPath $Source) {
    $text = Get-Content -LiteralPath $Source -Raw -Encoding UTF8

    # WE READ THE «case» STATEMENTS, NOT THE «case ... return» ONES.
    #
    # The first draft looked for `case "x": return`, and missed the first of a
    # pair that falls through to the other:
    #
    #     case "structural_member":
    #     case "rectangular_frame": return StructuralMember(...);
    #
    # The check reported `structural_member` as «dichiarato ma non
    # nello switch», which was false. A check that accuses the right code
    # stops being looked at after twice.
    #
    # We only look inside the body of `Run`, if it is found: elsewhere in the
    # file there are other switches (plane names, view names) that are beside the point.
    #
    # Since W3.1-bis.5 the switch sits in `ExecuteWithCurrentNames`: `Run`
    # translates the names of before the rename (Alias.cs) and hands over. The
    # method that holds the switch is read; `Run` when it holds it again
    # (W3.1-bis.11 removes the aliases).
    $body = $text
    $start = $text.LastIndexOf(" ExecuteWithCurrentNames(")   # the declaration, after the call
    if ($start -lt 0) { $start = $text.IndexOf("public Outcome Run(") }
    if ($start -ge 0) {
        $fine = $text.IndexOf("`n        }", $start)
        if ($fine -gt $start) { $body = $text.Substring($start, $fine - $start) }
    }
    $fromSource = @([regex]::Matches($body, 'case\s+"([a-z_0-9]+)"\s*:') |
                     ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}
if ($fromSource.Count -lt 20) {
    Male ("from the source I read only " + $fromSource.Count + " 'case' labels: the " +
          "reading did not work, and a check that did not look cannot " +
          "say that all is well.")
} else {
    $declaredOnly = @($declared | Where-Object { $_ -notin $fromSource })
    $sourceOnly = @($fromSource | Where-Object { $_ -notin $declared })
    if ($declaredOnly.Count -or $sourceOnly.Count) {
        Male "ExecutableNames and the 'case' labels of the source do not match"
        $declaredOnly | ForEach-Object { Note "declared but not in the switch: $_" }
        $sourceOnly   | ForEach-Object { Note "in the switch but not declared: $_" }
    } else {
        Fine ("the declared list matches the " + $fromSource.Count + " 'case' labels of the source")
    }
}

# --- 4. the comparison that counts ------------------------------------------
$declaredNotRunnable = @($catalogNames | Where-Object { $_ -notin $declared })
$undeclaredExecutables = @($declared | Where-Object { $_ -notin $catalogNames })

if ($declaredNotRunnable.Count) {
    Male ("the catalog declares " + $declaredNotRunnable.Count +
          " operations that the executor does NOT know how to run")
    foreach ($n in $declaredNotRunnable) {
        $op = $operations | Where-Object { $_.name -eq $n } | Select-Object -First 1
        Note ("$n   (family: " + $op.family + ")")
    }
    Note "The model can ask for them, and gets 'unknown operation'."
} else {
    Fine "every operation in the catalog is executable"
}

if ($undeclaredExecutables.Count) {
    # It is NOT an error: an operation that exists but is not declared is
    # simply invisible to the model, which is the intended behaviour for
    # what has not been tested yet.
    Fine ("executable but not declared: " + $undeclaredExecutables.Count +
          " (invisible to the model, and that is intended)")
    $undeclaredExecutables | ForEach-Object { Note $_ }
}

# --- 5. the shape of every entry ---------------------------------------------
Write-Host ""
Write-Host "=== The shape of the catalog" -ForegroundColor Cyan

$mandatoryFields = @("name", "family", "summary", "details", "schema")
$withoutField = 0
foreach ($op in $operations) {
    foreach ($c in $mandatoryFields) {
        $v = $op.$c
        if ($null -eq $v -or ($v -is [string] -and [string]::IsNullOrWhiteSpace($v))) {
            Male ("'" + $op.name + "' has no '" + $c + "'")
            $withoutField++
        }
    }
}
if ($withoutField -eq 0) { Fine "every operation has name, family, summary, description, schema" }

# --- 6. the families ---------------------------------------------------------
# It is the defect already repaired once in esporta_catalogo.py: a family
# declared and empty reaches the model as a door that opens onto nothing.
$usedFamilies = @($operations | ForEach-Object { $_.family } | Sort-Object -Unique)
# (the declared families were read, and required not empty, in step 1)
$empty = @($familiesDeclared | Where-Object { $_ -notin $usedFamilies })
$notDeclared = @($usedFamilies | Where-Object { $_ -notin $familiesDeclared })
if ($empty.Count) {
    Male ("families declared and EMPTY: " + ($empty -join ", "))
    Note "To the model they look like a door that opens onto nothing."
}
if ($notDeclared.Count) {
    Male ("families used but not declared: " + ($notDeclared -join ", "))
}
if (-not $empty.Count -and -not $notDeclared.Count) {
    Fine ("the " + $usedFamilies.Count + " families add up")
}

# --- 7. what must NOT appear in the texts ------------------------------------
# C2 of the audit stops being a grep to run by hand and becomes a check.
Write-Host ""
Write-Host "=== What must not come out of the catalog" -ForegroundColor Cyan
# WE SEARCH THE DECODED TEXT, NOT THE RAW FILE.
#
# The first draft read the JSON as text and searched for «O:\» without distinguishing
# case. In the catalogue there is the sentence «...con il tipo:\n tubolare...», where
# `\n` is the JSON escape of a newline: the search found `o:` + `\` and
# reported an internal path that does not exist. Once decoded, that escape
# becomes a real newline and the false positive disappears.
$forbidden = @("S:\", "O:\", "W:\", "OpenPDM", "192.168.", "oscar")
$decoded = New-Object System.Text.StringBuilder
function Collect($o) {
    if ($null -eq $o) { return }
    if ($o -is [string]) { [void]$decoded.AppendLine($o); return }
    if ($o -is [System.Collections.IEnumerable]) {
        foreach ($x in $o) { Collect $x }
        return
    }
    if ($o -is [psobject] -and $o.PSObject.Properties) {
        foreach ($p in $o.PSObject.Properties) {
            [void]$decoded.AppendLine($p.Name)
            Collect $p.Value
        }
    }
}
Collect $cat
$text = $decoded.ToString()
$foundItems = @()
foreach ($v in $forbidden) {
    # Paths are searched respecting case: «O:\» is a drive, «o:\»
    # in the middle of an Italian sentence almost never is. Proper names are not,
    # those are searched anyway.
    $mode = if ($v.EndsWith("\")) { [StringComparison]::Ordinal }
            else { [StringComparison]::OrdinalIgnoreCase }
    if ($text.IndexOf($v, $mode) -ge 0) { $foundItems += $v }
}
if ($foundItems.Count) {
    Male ("these appear in the catalog: " + ($foundItems -join ", "))
    Note "The catalog is read by whoever uses the bridge: our own internals do not belong in it."
} else {
    Fine "no internal path, product name or address"
}

# --- outcome -----------------------------------------------------------------
Write-Host ""
if ($problems.Count -eq 0) {
    Write-Host "Everything adds up." -ForegroundColor Green
    exit 0
}
Write-Host ($problems.Count.ToString() + " problemi.") -ForegroundColor Red
exit 1
