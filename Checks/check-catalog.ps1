# =============================================================================
#  Il catalogo dichiara operazioni che l'esecutore sa fare?
# =============================================================================
#
#  Esce 0 se tutto torna, 1 se no. E' pensato per essere lanciato prima di una
#  consegna e da una build automatica.
#
#  PERCHE' ESISTE. Il catalogo (`catalog.json`) e' l'elenco di cio' che il
#  modello puo' chiedere; lo switch di `OperazioniSw.Esegui` e' l'elenco di cio'
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
#    3. l'elenco dichiarato dalla libreria (`OperazioniSw.NomiEseguibili`) e
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
    [string]$Catalogo,
    [string]$Libreria,
    [string]$Sorgente,
    [string]$SwInstallPath
)

$ErrorActionPreference = "Stop"
$radice = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)

if (-not $Catalogo) { $Catalogo = Join-Path $radice "bin\catalog.json" }
if (-not $Libreria) { $Libreria = Join-Path $radice "Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll" }
if (-not $Sorgente) { $Sorgente = Join-Path $radice "Oce.Solid.Agent.Core\SwOperations.cs" }

$problemi = New-Object System.Collections.Generic.List[string]
function Male($m) { $problemi.Add($m) | Out-Null; Write-Host "  [X] $m" -ForegroundColor Red }
function Bene($m) { Write-Host "  [ok] $m" -ForegroundColor Green }
function Nota($m) { Write-Host "       $m" -ForegroundColor DarkGray }

Write-Host ""
Write-Host "=== Il catalogo contro l'esecutore" -ForegroundColor Cyan

# --- 1. il catalogo ----------------------------------------------------------
if (-not (Test-Path -LiteralPath $Catalogo)) {
    Write-Host "  [X] non trovo il catalogo: $Catalogo" -ForegroundColor Red
    Write-Host ""
    Write-Host "1 problema: il catalogo non c'e'. Compila con build.ps1." -ForegroundColor Red
    exit 1
}
$cat = Get-Content -LiteralPath $Catalogo -Raw -Encoding UTF8 | ConvertFrom-Json

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
$famiglieDichiarate = @()
if (Test-JsonKey $cat $keyFamilies) {
    $declaredFamilies = $cat.$keyFamilies
    if ($declaredFamilies -is [System.Management.Automation.PSCustomObject]) {
        $famiglieDichiarate = @($declaredFamilies.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object -Unique)
    } elseif ($declaredFamilies -is [array]) {
        $famiglieDichiarate = @(@(foreach ($f in $declaredFamilies) {
            if ($f -is [string]) { $f }
            elseif (Test-JsonKey $f $keyName) { $f.$keyName }
            else { $missingKeys.Add("an entry of '$keyFamilies' is neither a name nor an object with '$keyName'") }
        }) | Sort-Object -Unique)
    } else {
        $missingKeys.Add("'$keyFamilies' is neither an object nor a list")
    }
    if ($famiglieDichiarate.Count -eq 0) { $missingKeys.Add("'$keyFamilies' declares no family") }
}
if ($missingKeys.Count) {
    Write-Host ("  [X] expected keys missing in " + $Catalogo + ": " + $missingKeys.Count) -ForegroundColor Red
    $missingKeys | Select-Object -First 20 | ForEach-Object { Write-Host "       $_" -ForegroundColor DarkGray }
    Write-Host ""
    Write-Host ("A missing key would read as `$null and every check below would compare empty sets. " +
                "If a key was renamed, this reader is renamed in the same commit.") -ForegroundColor Red
    exit 1
}
Bene ("the catalogue has the expected keys ($keyOperations, $keyFamilies; $keyName and $keyFamily in every operation), " +
      $famiglieDichiarate.Count + " declared families")

$operazioni = @($cat.operations)
$nomiCatalogo = @($operazioni | ForEach-Object { $_.name } | Sort-Object -Unique)

if ($nomiCatalogo.Count -lt 20) {
    Write-Host ("  [X] il catalogo ha solo " + $nomiCatalogo.Count + " operazioni: " +
                "non l'ho letto bene, e confrontare due insiemi vuoti direbbe " +
                "che va tutto bene.") -ForegroundColor Red
    exit 1
}
Bene ("catalogo: " + $nomiCatalogo.Count + " operazioni")

# --- 2. cio' che l'esecutore DICHIARA di saper fare --------------------------
if (-not (Test-Path -LiteralPath $Libreria)) {
    Write-Host "  [X] non trovo la libreria: $Libreria" -ForegroundColor Red
    exit 1
}
# THE INTEROP OF SOLIDWORKS, found where it is installed (30/9/2026). The
# static initializer of OperazioniSw touches SolidWorks.Interop.swconst, and
# a library loaded from its bytes has no folder to look for it in: without
# this resolver the read of NomiEseguibili below died with a
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
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Libreria))
$tipo = $asm.GetType("Oce.Solid.Agent.Core.OperazioniSw")
if (-not $tipo) {
    Write-Host "  [X] nella libreria non c'e' Oce.Solid.Agent.Core.OperazioniSw" -ForegroundColor Red
    exit 1
}
$campo = $tipo.GetField("NomiEseguibili")
if (-not $campo) {
    Write-Host ("  [X] OperazioniSw non espone NomiEseguibili: senza, questo " +
                "controllo dovrebbe indovinare.") -ForegroundColor Red
    exit 1
}
$dichiarati = @($campo.GetValue($null) | Sort-Object -Unique)
Bene ("l'esecutore dichiara: " + $dichiarati.Count + " operazioni")

# --- 3. cio' che c'e' DAVVERO nello switch, letto dal sorgente ---------------
# Seconda strada verso lo stesso fatto: se le due divergono, e' una delle due a
# essere marcia, e va detto invece di scegliere quella comoda.
$dalSorgente = @()
if (Test-Path -LiteralPath $Sorgente) {
    $testo = Get-Content -LiteralPath $Sorgente -Raw -Encoding UTF8

    # SI LEGGONO I «case», NON I «case ... return».
    #
    # La prima stesura cercava `case "x": return`, e perdeva il primo di una
    # coppia che ricade sull'altro:
    #
    #     case "structural_member":
    #     case "rectangular_frame": return MembroStrutturale(...);
    #
    # Il controllo denunciava `structural_member` come «dichiarato ma non
    # nello switch», che era falso. Un controllo che accusa il codice giusto
    # si smette di guardare dopo due volte.
    #
    # Si guarda solo dentro il corpo di `Esegui`, se lo si trova: altrove nel
    # file ci sono altri switch (nomi di piano, di vista) che non c'entrano.
    #
    # Since W3.1-bis.5 the switch sits in `ExecuteWithCurrentNames`: `Esegui`
    # translates the names of before the rename (Alias.cs) and hands over. The
    # method that holds the switch is read; `Esegui` when it holds it again
    # (W3.1-bis.11 removes the aliases).
    $corpo = $testo
    $inizio = $testo.LastIndexOf(" ExecuteWithCurrentNames(")   # the declaration, after the call
    if ($inizio -lt 0) { $inizio = $testo.IndexOf("public Esito Esegui(") }
    if ($inizio -ge 0) {
        $fine = $testo.IndexOf("`n        }", $inizio)
        if ($fine -gt $inizio) { $corpo = $testo.Substring($inizio, $fine - $inizio) }
    }
    $dalSorgente = @([regex]::Matches($corpo, 'case\s+"([a-z_0-9]+)"\s*:') |
                     ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}
if ($dalSorgente.Count -lt 20) {
    Male ("dal sorgente ho letto solo " + $dalSorgente.Count + " 'case': la " +
          "lettura non ha funzionato, e un controllo che non ha guardato non " +
          "puo' dire che va tutto bene.")
} else {
    $soloDichiarati = @($dichiarati | Where-Object { $_ -notin $dalSorgente })
    $soloSorgente = @($dalSorgente | Where-Object { $_ -notin $dichiarati })
    if ($soloDichiarati.Count -or $soloSorgente.Count) {
        Male "NomiEseguibili e i 'case' del sorgente non combaciano"
        $soloDichiarati | ForEach-Object { Nota "dichiarato ma non nello switch: $_" }
        $soloSorgente   | ForEach-Object { Nota "nello switch ma non dichiarato: $_" }
    } else {
        Bene ("l'elenco dichiarato combacia con i " + $dalSorgente.Count + " 'case' del sorgente")
    }
}

# --- 4. il confronto che conta ----------------------------------------------
$dichiaratiNonEseguibili = @($nomiCatalogo | Where-Object { $_ -notin $dichiarati })
$eseguibiliNonDichiarati = @($dichiarati | Where-Object { $_ -notin $nomiCatalogo })

if ($dichiaratiNonEseguibili.Count) {
    Male ("il catalogo dichiara " + $dichiaratiNonEseguibili.Count +
          " operazioni che l'esecutore NON sa eseguire")
    foreach ($n in $dichiaratiNonEseguibili) {
        $op = $operazioni | Where-Object { $_.name -eq $n } | Select-Object -First 1
        Nota ("$n   (famiglia: " + $op.family + ")")
    }
    Nota "Il modello puo' chiederle, e si prende 'operazione sconosciuta'."
} else {
    Bene "ogni operazione del catalogo e' eseguibile"
}

if ($eseguibiliNonDichiarati.Count) {
    # NON e' un errore: un'operazione che c'e' ma non e' dichiarata e'
    # semplicemente invisibile al modello, che e' il comportamento voluto per
    # cio' che non e' ancora stato provato.
    Bene ("eseguibili ma non dichiarate: " + $eseguibiliNonDichiarati.Count +
          " (invisibili al modello, ed e' voluto)")
    $eseguibiliNonDichiarati | ForEach-Object { Nota $_ }
}

# --- 5. la forma di ogni voce ------------------------------------------------
Write-Host ""
Write-Host "=== La forma del catalogo" -ForegroundColor Cyan

$campiObbligatori = @("name", "family", "summary", "details", "schema")
$senzaCampo = 0
foreach ($op in $operazioni) {
    foreach ($c in $campiObbligatori) {
        $v = $op.$c
        if ($null -eq $v -or ($v -is [string] -and [string]::IsNullOrWhiteSpace($v))) {
            Male ("'" + $op.name + "' non ha '" + $c + "'")
            $senzaCampo++
        }
    }
}
if ($senzaCampo -eq 0) { Bene "ogni operazione ha nome, famiglia, sommario, descrizione, schema" }

# --- 6. le famiglie ----------------------------------------------------------
# E' il difetto gia' riparato una volta in esporta_catalogo.py: una famiglia
# dichiarata e vuota arriva al modello come una porta che non apre su niente.
$famiglieUsate = @($operazioni | ForEach-Object { $_.family } | Sort-Object -Unique)
# (the declared families were read, and required not empty, in step 1)
$vuote = @($famiglieDichiarate | Where-Object { $_ -notin $famiglieUsate })
$nonDichiarate = @($famiglieUsate | Where-Object { $_ -notin $famiglieDichiarate })
if ($vuote.Count) {
    Male ("famiglie dichiarate e VUOTE: " + ($vuote -join ", "))
    Nota "Al modello arrivano come una porta che non apre su niente."
}
if ($nonDichiarate.Count) {
    Male ("famiglie usate ma non dichiarate: " + ($nonDichiarate -join ", "))
}
if (-not $vuote.Count -and -not $nonDichiarate.Count) {
    Bene ("le " + $famiglieUsate.Count + " famiglie tornano")
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
$vietati = @("S:\", "O:\", "W:\", "OpenPDM", "192.168.", "oscar")
$decodificato = New-Object System.Text.StringBuilder
function Raccogli($o) {
    if ($null -eq $o) { return }
    if ($o -is [string]) { [void]$decodificato.AppendLine($o); return }
    if ($o -is [System.Collections.IEnumerable]) {
        foreach ($x in $o) { Raccogli $x }
        return
    }
    if ($o -is [psobject] -and $o.PSObject.Properties) {
        foreach ($p in $o.PSObject.Properties) {
            [void]$decodificato.AppendLine($p.Name)
            Raccogli $p.Value
        }
    }
}
Raccogli $cat
$testo = $decodificato.ToString()
$trovati = @()
foreach ($v in $vietati) {
    # I percorsi si cercano rispettando le maiuscole: «O:\» e' un'unita', «o:\»
    # in mezzo a una frase italiana non lo e' quasi mai. I nomi propri no,
    # quelli si cercano comunque.
    $modo = if ($v.EndsWith("\")) { [StringComparison]::Ordinal }
            else { [StringComparison]::OrdinalIgnoreCase }
    if ($testo.IndexOf($v, $modo) -ge 0) { $trovati += $v }
}
if ($trovati.Count) {
    Male ("nel catalogo compaiono: " + ($trovati -join ", "))
    Nota "Il catalogo lo legge chi usa il ponte: qui dentro non ci va casa nostra."
} else {
    Bene "nessun percorso interno, nome di prodotto o indirizzo"
}

# --- esito -------------------------------------------------------------------
Write-Host ""
if ($problemi.Count -eq 0) {
    Write-Host "Tutto torna." -ForegroundColor Green
    exit 0
}
Write-Host ($problemi.Count.ToString() + " problemi.") -ForegroundColor Red
exit 1
