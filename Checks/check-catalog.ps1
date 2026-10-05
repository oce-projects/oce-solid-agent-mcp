# =============================================================================
#  Il catalogo dichiara operazioni che l'esecutore sa fare?
# =============================================================================
#
#  Esce 0 se tutto torna, 1 se no. E' pensato per essere lanciato prima di una
#  consegna e da una build automatica.
#
#  PERCHE' ESISTE. Il catalogo (`catalog.json`) e' l'elenco di cio' che il
#  modello puo' chiedere; lo switch di `SwOperations.Run` e' l'elenco di cio'
#  che si puo' davvero fare. Sono due posti, e due posti divergono sempre. Oggi
#  divergono di tre nomi, e quei tre nomi il modello puo' chiederli: si prende
#  «operazione sconosciuta» dopo aver scritto un piano intero.
#
#  UN CONTROLLO NON DEVE POTER USCIRE 0 PER NON AVER GUARDATO.
#  E' il difetto che questo controllo rischiava piu' di tutti: leggere i `case`
#  con una regex su 4.700 righe, non trovarne nessuno, e dichiarare «nessun
#  disallineamento». Qui ci si difende in tre modi:
#    1. se il catalogo ha meno di 20 operazioni si esce 1 dicendo che non e'
#       stato letto, invece di confrontare due insiemi vuoti;
#    2. se dal sorgente si leggono meno di 20 `case`, idem;
#    3. l'elenco dichiarato dalla libreria (`SwOperations.ExecutableNames`) e
#       quello letto dal sorgente devono COMBACIARE fra loro: sono due strade
#       indipendenti verso lo stesso fatto, e se divergono e' uno dei due a
#       essere marcio.
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
Write-Host "=== Il catalogo contro l'esecutore" -ForegroundColor Cyan

# --- 1. il catalogo ----------------------------------------------------------
if (-not (Test-Path -LiteralPath $Catalog)) {
    Write-Host "  [X] non trovo il catalogo: $Catalog" -ForegroundColor Red
    Write-Host ""
    Write-Host "1 problema: il catalogo non c'e'. Compila con build.ps1." -ForegroundColor Red
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
    Write-Host ("  [X] il catalogo ha solo " + $catalogNames.Count + " operazioni: " +
                "non l'ho letto bene, e confrontare due insiemi vuoti direbbe " +
                "che va tutto bene.") -ForegroundColor Red
    exit 1
}
Fine ("catalogo: " + $catalogNames.Count + " operazioni")

# --- 2. cio' che l'esecutore DICHIARA di saper fare --------------------------
if (-not (Test-Path -LiteralPath $Library)) {
    Write-Host "  [X] non trovo la libreria: $Library" -ForegroundColor Red
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
# Load sui byte e non LoadFrom: la libreria puo' stare su un'unita' di rete,
# dove LoadFrom fallisce con 0x80131515 nominando una dipendenza che non esiste.
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Library))
$typ = $asm.GetType("Oce.Solid.Agent.Core.SwOperations")
if (-not $typ) {
    Write-Host "  [X] nella libreria non c'e' Oce.Solid.Agent.Core.SwOperations" -ForegroundColor Red
    exit 1
}
$theField = $typ.GetField("ExecutableNames")
if (-not $theField) {
    Write-Host ("  [X] SwOperations non espone ExecutableNames: senza, questo " +
                "controllo dovrebbe indovinare.") -ForegroundColor Red
    exit 1
}
$declared = @($theField.GetValue($null) | Sort-Object -Unique)
Fine ("l'esecutore dichiara: " + $declared.Count + " operazioni")

# --- 3. cio' che c'e' DAVVERO nello switch, letto dal sorgente ---------------
# Seconda strada verso lo stesso fatto: se le due divergono, e' una delle due a
# essere marcia, e va detto invece di scegliere quella comoda.
$fromSource = @()
if (Test-Path -LiteralPath $Source) {
    $text = Get-Content -LiteralPath $Source -Raw -Encoding UTF8

    # SI LEGGONO I «case», NON I «case ... return».
    #
    # La prima stesura cercava `case "x": return`, e perdeva il primo di una
    # coppia che ricade sull'altro:
    #
    #     case "structural_member":
    #     case "rectangular_frame": return StructuralMember(...);
    #
    # Il controllo denunciava `structural_member` come «dichiarato ma non
    # nello switch», che era falso. Un controllo che accusa il codice giusto
    # si smette di guardare dopo due volte.
    #
    # Si guarda solo dentro il corpo di `Run`, se lo si trova: altrove nel
    # file ci sono altri switch (nomi di piano, di vista) che non c'entrano.
    #
    # Since W3.1-bis.5 the switch sits in `ExecuteWithCurrentNames`: `Run`
    # translates the names of before the rename (Alias.cs) and hands over. The
    # method that holds the switch is read; `Run` when it holds it again
    # (W3.1-bis.11 removes the aliases).
    $body = $text
    $start = $text.LastIndexOf(" ExecuteWithCurrentNames(")   # the declaration, after the call
    if ($start -lt 0) { $start = $text.IndexOf("public Esito Esegui(") }
    if ($start -ge 0) {
        $fine = $text.IndexOf("`n        }", $start)
        if ($fine -gt $start) { $body = $text.Substring($start, $fine - $start) }
    }
    $fromSource = @([regex]::Matches($body, 'case\s+"([a-z_0-9]+)"\s*:') |
                     ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}
if ($fromSource.Count -lt 20) {
    Male ("dal sorgente ho letto solo " + $fromSource.Count + " 'case': la " +
          "lettura non ha funzionato, e un controllo che non ha guardato non " +
          "puo' dire che va tutto bene.")
} else {
    $declaredOnly = @($declared | Where-Object { $_ -notin $fromSource })
    $sourceOnly = @($fromSource | Where-Object { $_ -notin $declared })
    if ($declaredOnly.Count -or $sourceOnly.Count) {
        Male "ExecutableNames e i 'case' del sorgente non combaciano"
        $declaredOnly | ForEach-Object { Note "dichiarato ma non nello switch: $_" }
        $sourceOnly   | ForEach-Object { Note "nello switch ma non dichiarato: $_" }
    } else {
        Fine ("l'elenco dichiarato combacia con i " + $fromSource.Count + " 'case' del sorgente")
    }
}

# --- 4. il confronto che conta ----------------------------------------------
$declaredNotRunnable = @($catalogNames | Where-Object { $_ -notin $declared })
$undeclaredExecutables = @($declared | Where-Object { $_ -notin $catalogNames })

if ($declaredNotRunnable.Count) {
    Male ("il catalogo dichiara " + $declaredNotRunnable.Count +
          " operazioni che l'esecutore NON sa eseguire")
    foreach ($n in $declaredNotRunnable) {
        $op = $operations | Where-Object { $_.name -eq $n } | Select-Object -First 1
        Note ("$n   (famiglia: " + $op.family + ")")
    }
    Note "Il modello puo' chiederle, e si prende 'operazione sconosciuta'."
} else {
    Fine "ogni operazione del catalogo e' eseguibile"
}

if ($undeclaredExecutables.Count) {
    # NON e' un errore: un'operazione che c'e' ma non e' dichiarata e'
    # semplicemente invisibile al modello, che e' il comportamento voluto per
    # cio' che non e' ancora stato provato.
    Fine ("eseguibili ma non dichiarate: " + $undeclaredExecutables.Count +
          " (invisibili al modello, ed e' voluto)")
    $undeclaredExecutables | ForEach-Object { Note $_ }
}

# --- 5. la forma di ogni voce ------------------------------------------------
Write-Host ""
Write-Host "=== La forma del catalogo" -ForegroundColor Cyan

$mandatoryFields = @("name", "family", "summary", "details", "schema")
$withoutField = 0
foreach ($op in $operations) {
    foreach ($c in $mandatoryFields) {
        $v = $op.$c
        if ($null -eq $v -or ($v -is [string] -and [string]::IsNullOrWhiteSpace($v))) {
            Male ("'" + $op.name + "' non ha '" + $c + "'")
            $withoutField++
        }
    }
}
if ($withoutField -eq 0) { Fine "ogni operazione ha nome, famiglia, sommario, descrizione, schema" }

# --- 6. le famiglie ----------------------------------------------------------
# E' il difetto gia' riparato una volta in esporta_catalogo.py: una famiglia
# dichiarata e vuota arriva al modello come una porta che non apre su niente.
$usedFamilies = @($operations | ForEach-Object { $_.family } | Sort-Object -Unique)
# (the declared families were read, and required not empty, in step 1)
$empty = @($familiesDeclared | Where-Object { $_ -notin $usedFamilies })
$notDeclared = @($usedFamilies | Where-Object { $_ -notin $familiesDeclared })
if ($empty.Count) {
    Male ("famiglie dichiarate e VUOTE: " + ($empty -join ", "))
    Note "Al modello arrivano come una porta che non apre su niente."
}
if ($notDeclared.Count) {
    Male ("famiglie usate ma non dichiarate: " + ($notDeclared -join ", "))
}
if (-not $empty.Count -and -not $notDeclared.Count) {
    Fine ("le " + $usedFamilies.Count + " famiglie tornano")
}

# --- 7. cosa NON deve comparire nei testi ------------------------------------
# C2 dell'audit smette di essere un grep da fare a mano e diventa una prova.
Write-Host ""
Write-Host "=== Cosa non deve uscire dal catalogo" -ForegroundColor Cyan
# SI CERCA NEL TESTO DECODIFICATO, NON NEL FILE GREZZO.
#
# La prima stesura leggeva il JSON come testo e cercava «O:\» senza distinguere
# maiuscole. Nel catalogo c'e' la frase «...con il tipo:\n tubolare...», dove
# `\n` e' l'escape JSON di un a capo: la ricerca trovava `o:` + `\` e
# denunciava un percorso interno che non esiste. Decodificando, quell'escape
# diventa un a capo vero e il falso positivo sparisce.
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
    # I percorsi si cercano rispettando le maiuscole: «O:\» e' un'unita', «o:\»
    # in mezzo a una frase italiana non lo e' quasi mai. I nomi propri no,
    # quelli si cercano comunque.
    $mode = if ($v.EndsWith("\")) { [StringComparison]::Ordinal }
            else { [StringComparison]::OrdinalIgnoreCase }
    if ($text.IndexOf($v, $mode) -ge 0) { $foundItems += $v }
}
if ($foundItems.Count) {
    Male ("nel catalogo compaiono: " + ($foundItems -join ", "))
    Note "Il catalogo lo legge chi usa il ponte: qui dentro non ci va casa nostra."
} else {
    Fine "nessun percorso interno, nome di prodotto o indirizzo"
}

# --- esito -------------------------------------------------------------------
Write-Host ""
if ($problems.Count -eq 0) {
    Write-Host "Tutto torna." -ForegroundColor Green
    exit 0
}
Write-Host ($problems.Count.ToString() + " problemi.") -ForegroundColor Red
exit 1
