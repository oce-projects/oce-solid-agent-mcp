# =============================================================================
#  solidworks-modules.ps1 - which modules are REALLY on this
#                             workstation, and which are registered  (entries Z-H*)
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like bench.ps1 and for the same reason.
#
#  WHY IT EXISTS
#
#  Entries Z-H1..Z-H5 all ask the same thing: "is the module there or does it need
#  turning on?". It is a question answered with a MEASUREMENT, not by reading the
#  price list: a binary on the disk does not mean that it is registered, and a registered
#  CLSID does not mean that the licence makes it start.
#
#  Three levels, and they must be told apart:
#
#    1. THE FILE IS THERE         -> the module was installed at least once
#    2. THE INTEROP DESCRIBES IT  -> one can program against it
#    3. THE CLSID IS REGISTERED   -> the object can be CREATED
#
#  The third is the only one that counts to know whether a call will start. And not even
#  it is enough: the licence only shows by trying.
#
#  ── TWO TRAPS, MEASURED ─────────────────────────────────────────────────────
#
#  - **NEVER `Win32_Product`.** Querying it makes EVERY MSI package of the
#    machine be RECONFIGURED: on this workstation it broke the start of SOLIDWORKS. Here
#    we look at files and registry, and that is all.
#  - **THERE ARE TWO INSTALLED TREES** (`...\SOLIDWORKS` and `...\SOLIDWORKS (2)`)
#    and some CLSIDs point to the second. The script prints the path that the
#    registry declares, not the one that is expected: if they point elsewhere, it
#    shows.
#
#  USAGE
#      .\Checks\solidworks-modules.ps1
# =============================================================================

$ErrorActionPreference = "Continue"

$root = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
$redist = Join-Path $root "api\redist"

function Line($label, $val) {
    Write-Output ("  {0,-34} {1}" -f $label, $val)
}

function FileC($filePath) {
    if (Test-Path $filePath) {
        $b = (Get-Item $filePath).Length
        return ("SI'  " + "{0:N0}" -f $b + " byte")
    }
    return "NO"
}

# SEARCH, DO NOT ASSUME.
#
# The first draft looked for `cosworks.dll` in the root of SOLIDWORKS and
# answered **NO**. The file was there, in `SOLIDWORKS\Simulation\`. A probe that
# looks in the wrong place says «it is not there» with the same assurance as
# a true absence, and that answer ends up in a TODO as if it were a
# measurement.
#
# Here we search under the root and print WHERE it was found: the path is
# part of the measurement, not a detail.
function Find($itemName) {
    try {
        $f = Get-ChildItem -Path $root -Filter $itemName -Recurse -File -ErrorAction SilentlyContinue |
             Select-Object -First 1
        if ($f) {
            return ("SI'  " + "{0:N0}" -f $f.Length + " byte  ->  " +
                    $f.DirectoryName.Replace($root, "<radice>"))
        }
    } catch { }
    return "NON TROVATO sotto la radice"
}

# The coclasses of an interop: they are the classes that can be created with `new`.
# Taking them from the interop itself and not from a note is the point -- a note
# ages, the assembly does not.
function Coclass($dll) {
    if (-not (Test-Path $dll)) { return @() }
    try {
        $asm = [Reflection.Assembly]::LoadFrom($dll)
        return $asm.GetTypes() | Where-Object {
            $_.IsClass -and $_.GUID -ne [Guid]::Empty
        } | ForEach-Object {
            [pscustomobject]@{ Nome = $_.Name; Guid = $_.GUID }
        }
    } catch { return @() }
}

# THE .NET API IS USED, NOT THE `HKLM:` PROVIDER.
#
# Measured on 23/9/2026: with `Test-Path HKLM:\...` this probe did not finish. The
# Document Manager declares **more than sixty coclasses**, and each one cost
# a few seconds of provider -- two runs killed by the timeout, no measurement.
# `OpenSubKey` asks the same question in a thousandth of the time.
#
# A tool that does not finish is not slow: it is a tool that does not answer.
function Recorded($guid) {
    # HKCU BEFORE HKLM, as Windows merges HKCR. This function looked
    # only at HKLM: it is the mistake that this house has already paid for with the add-in
    # (HKCU\Software\Classes hides HKLM). Corrected on 24/9/2026 (entry K20).
    try {
        foreach ($root in @([Microsoft.Win32.Registry]::CurrentUser,
                              [Microsoft.Win32.Registry]::LocalMachine)) {
            foreach ($typ in @("InprocServer32", "LocalServer32")) {
                $k = $root.OpenSubKey("SOFTWARE\Classes\CLSID\{$guid}\$typ")
                if ($k) {
                    $v = $k.GetValue("")
                    $k.Close()
                    if ($v) { return $v }
                    return "(chiave senza valore)"
                }
            }
        }
    } catch { }
    return $null
}

# ONLY THE REGISTERED ONES ARE PRINTED, plus the count of the others.
#
# The first run listed 26 `SwDMDocumentNClass` one by one, all «non
# registrato», and the line that counted -- the factory, the only one created with
# `new` -- vanished in the middle. A list that reads badly is a list that is not
# read, and then the measurement has not been done.
function List($dll) {
    $cc = Coclass $dll
    $reg = 0
    foreach ($c in $cc) {
        $location = Recorded $c.Guid
        if ($location) { Line ("  " + $c.Nome) "REGISTRATO -> $location"; $reg++ }
    }
    Line "coclass nell'interop" ("" + $cc.Count + ", di cui registrate: " + $reg)
    if ($reg -eq 0 -and $cc.Count -gt 0) {
        # THIS LINE SAID «il modulo e' sul disco ma non si puo' creare», and it
        # was a blind conclusion: on 24/9/2026 Simulation ran (entry S3)
        # and the coclasses of ITS interop turn out 0 registered out of 80, as here.
        # The coclasses of the interop are not the add-in. The add-in is resolved further
        # down, from its ProgID, with a positive control (entry K20).
        Write-Output "    nessuna coclass dell'interop e' registrata. NON vuol dire che il"
        Write-Output "    modulo non si carichi: vedi la risoluzione dell'add-in in fondo."
    }
}

Write-Output ""
Write-Output "radice: $root"
Write-Output ""

# ── Z-H1: SOLIDWORKS Utilities -----------------------------------------------
Write-Output "=== Z-H1  SOLIDWORKS Utilities (IThicknessAnalysis) ==="
Line "cartella sldutils" $(if (Test-Path (Join-Path $root "sldutils")) {
    "SI'  " + (Get-ChildItem (Join-Path $root "sldutils") -File | Measure-Object).Count + " file" } else { "NO" })
Line "ThickChkswu.dll" (Find "ThickChkswu.dll")
Line "interop gtswutilities" (FileC (Join-Path $redist "SolidWorks.Interop.gtswutilities.dll"))
List (Join-Path $redist "SolidWorks.Interop.gtswutilities.dll")
Write-Output ""

# ── Z-H2: Document Manager ---------------------------------------------------
Write-Output "=== Z-H2  Document Manager (serve una CHIAVE, non una spunta) ==="
Line "swdocumentmgr.dll" (Find "swdocumentmgr.dll")
Line "interop swdocumentmgr" (FileC (Join-Path $redist "SolidWorks.Interop.swdocumentmgr.dll"))
List (Join-Path $redist "SolidWorks.Interop.swdocumentmgr.dll")
Write-Output "  NOTA: GetApplication vuole una chiave rilasciata da Dassault."
Write-Output "        Finche' non e' in mano, non si scrive una riga attorno (W2.14)."
Write-Output ""

# ── Z-H3: DimXpert ------------------------------------------------------------
Write-Output "=== Z-H3  DimXpert ==="
Line "slddimxpertclru.dll" (Find "slddimxpertclru.dll")
Line "interop swdimxpert" (FileC (Join-Path $redist "SolidWorks.Interop.swdimxpert.dll"))
List (Join-Path $redist "SolidWorks.Interop.swdimxpert.dll")
Write-Output ""

# ── Z-H4: Simulation ----------------------------------------------------------
Write-Output "=== Z-H4  Simulation ==="
Line "cosworks.dll" (Find "cosworks.dll")
Line "interop cosworks" (FileC (Join-Path $redist "SolidWorks.Interop.cosworks.dll"))
$k = "HKCU:\SOFTWARE\SolidWorks\AddInsStartup"
if (Test-Path $k) {
    $p = Get-ItemProperty $k
    foreach ($n in ($p.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' })) {
        if ($n.Name -match 'cw|cos|Simulation') {
            Line ("add-in " + $n.Name) $(if ($n.Value -eq 0) { "0 = NON attivo all'avvio" } else { $n.Value })
        }
    }
} else { Line "AddInsStartup" "la chiave non c'e'" }
Write-Output ""

# ── Z-H5: Toolbox --------------------------------------------------------------
Write-Output "=== Z-H5  Toolbox ==="
Line "cartella Toolbox" $(if (Test-Path "C:\SOLIDWORKS Data") { "SI'  C:\SOLIDWORKS Data" } else { "non in C:\SOLIDWORKS Data" })
Line "interop toolboxconfigure" (FileC (Join-Path $redist "SolidWorks.Interop.sldtoolboxconfigureaddin.dll"))
Write-Output ""

# -- S4: Routing ----------------------------------------------------------------
Write-Output "=== S4  Routing (serve a questa officina?) ==="
Line "interop SWRoutingLib" (FileC (Join-Path $redist "SolidWorks.Interop.SWRoutingLib.dll"))
List (Join-Path $redist "SolidWorks.Interop.SWRoutingLib.dll")
Write-Output ""

# ==============================================================================
# THE ADD-INS, REALLY RESOLVED -- and with two positive controls (entry K20)
# ==============================================================================
#
# HERE THERE WAS «THE REGISTERED ADD-INS, the shortest answer of all»: the
# key HKLM\SOFTWARE\SolidWorks\AddIns, and «a module that does not appear here
# does not start inside SOLIDWORKS». It was FALSE, and it was seen on 24/9/2026:
#
#   - that key lists only the THIRD-PARTY add-ins (OpenPDM, Composer,
#     Marketplace...). The Dassault modules are elsewhere, or in no key;
#   - Simulation appears in NONE of the three keys -- neither AddIns, nor
#     SOLIDWORKS 2021\AddIns, nor AddInsStartup -- and that day it ran
#     (entry S3, 54.3 MPa against 60.0 by hand).
#
# On this line rested Z-H1, Z-H3, Z-H4 and S4: «it cannot be created», «the
# Installation Manager is needed», «nothing can be built». A check that gives
# the same answer for a module that is on and for one that is off measures nothing,
# and nobody had ever seen it tell the truth on a known case.
#
# NOW: every add-in is resolved the way Windows does it -- ProgID -> CLSID ->
# InprocServer32 -> file, looking in HKCU\Software\Classes BEFORE HKLM
# (it is the order in which HKCR is merged, and this house has already paid for it), with the
# two cases that the first draft of the probe got wrong:
#
#   - .NET add-in: InprocServer32 is `mscoree.dll`, and the real file is in
#     `CodeBase`. The first probe said «FILE ASSENTE» for OpenPDM, which is
#     installed and starts by itself;
#   - relative paths: `.\sldUtils\SwLoaderSw.dll` is relative to the root of
#     SOLIDWORKS.
#
# AND TWO POSITIVE CONTROLS, which must turn out reachable or the script
# declares its own criterion blind and exits 1:
#   Simulation  (a Dassault module that is in no add-in key)
#   OpenPDM     (a .NET add-in, registered in HKCU)
$regHKCU = [Microsoft.Win32.Registry]::CurrentUser
$regHKLM = [Microsoft.Win32.Registry]::LocalMachine

function OpenClass($below) {
    foreach ($r in @($regHKCU, $regHKLM)) {
        $k = $r.OpenSubKey("SOFTWARE\Classes\$below")
        if ($k) { return $k }
    }
    return $null
}

function ProgIdClsid($progId) {
    $k = OpenClass "$progId\CLSID"
    if (-not $k) { return $null }
    $g = $k.GetValue(""); $k.Close()
    return $g
}

function ServerFile($guid) {
    foreach ($typ in @("InprocServer32", "LocalServer32")) {
        $k = OpenClass "CLSID\$guid\$typ"
        if (-not $k) { continue }
        $v = $k.GetValue("")
        if ($v -and $v -match 'mscoree') {
            $cb = $k.GetValue("CodeBase")
            if (-not $cb) {
                foreach ($sv in $k.GetSubKeyNames()) {
                    $s = $k.OpenSubKey($sv)
                    if ($s) { $cb = $s.GetValue("CodeBase"); $s.Close() }
                    if ($cb) { break }
                }
            }
            $k.Close()
            if ($cb) { return ($cb -replace '^file:///', '' -replace '/', '\') }
            return $null
        }
        $k.Close()
        if ($v) {
            $v = $v.Trim('"')
            if ($v.StartsWith('.\')) { $v = Join-Path $root $v.Substring(2) }
            return $v
        }
    }
    return $null
}

function Startup($guid) {
    $k = $regHKCU.OpenSubKey("SOFTWARE\SolidWorks\AddInsStartup")
    if (-not $k) { return "" }
    $v = $k.GetValue($guid)
    if ($null -eq $v) { $s = $k.OpenSubKey($guid); if ($s) { $v = $s.GetValue(""); $s.Close() } }
    $k.Close()
    if ($null -eq $v) { return "" }
    if ("$v" -eq "1") { return "parte da solo" }
    return "va acceso a mano"
}

# The modules, by ProgID. They are added here, not guessed.
$modules = @(
    @{ nome = "Simulation";          progId = "SldWorks.Simulation";         positivo = $true },
    @{ nome = "OpenPDM (add-in .NET)"; guid = "{0FED85A2-3F4A-4F2C-8DCF-7C8AE1E15A5C}"; positivo = $true },
    @{ nome = "SOLIDWORKS Utilities (Z-H1)"; progId = "Utilities.UtilitiesApp" },
    @{ nome = "Analysis Tools";      progId = "AnalysisTools.AnalysisAddin" }
)

Write-Output "=== GLI ADD-IN, RISOLTI (ProgID -> CLSID -> file) ==="
$blindOnes = 0
foreach ($m in $modules) {
    $g = $m.guid
    if (-not $g -and $m.progId) { $g = ProgIdClsid $m.progId }
    if (-not $g) {
        Line $m.nome "ProgID NON registrato"
        if ($m.positivo) { $blindOnes++ }
        continue
    }
    $f = ServerFile $g
    $outcome = if (-not $f) { "CLSID senza server" }
             elseif (Test-Path $f) { "RAGGIUNGIBILE" }
             else { "server dichiarato, FILE ASSENTE" }
    $a = Startup $g
    Line $m.nome ($outcome + $(if ($a) { " - $a" } else { "" }))
    if ($f) { Write-Output ("      -> " + $f.Replace($root, "<radice>")) }
    if ($m.positivo -and $outcome -ne "RAGGIUNGIBILE") { $blindOnes++ }
}
Write-Output ""

# The three add-in keys, for completeness: they say who is listed, not who
# gets loaded.
Write-Output "=== LE TRE CHIAVI DI ADD-IN (chi e' elencato, NON chi si carica) ==="
foreach ($c in @(
    @{ n = "HKLM AddIns";                 r = $regHKLM; p = "SOFTWARE\SolidWorks\AddIns" },
    @{ n = "HKLM SOLIDWORKS 2021\AddIns"; r = $regHKLM; p = "SOFTWARE\SolidWorks\SOLIDWORKS 2021\AddIns" })) {
    $k = $c.r.OpenSubKey($c.p)
    if (-not $k) { Line $c.n "la chiave non c'e'"; continue }
    foreach ($g in $k.GetSubKeyNames()) {
        $s = $k.OpenSubKey($g); $t = ""
        if ($s) { $t = "" + $s.GetValue("Title"); $s.Close() }
        $f = ServerFile $g
        $outcome = if ($f -and (Test-Path $f)) { "ok" } elseif ($f) { "file assente" } else { "senza server" }
        Line ("  " + $t) ($c.n + ": " + $outcome)
    }
    $k.Close()
}
Write-Output ""

if ($blindOnes -gt 0) {
    Write-Output "XX  CRITERIO CIECO: $blindOnes controlli positivi non risultano raggiungibili."
    Write-Output "    Simulation e l'add-in .NET DEVONO risolversi: se non lo fanno, e' la"
    Write-Output "    sonda a sbagliare, e ogni 'non raggiungibile' qui sopra non vale niente."
    exit 1
}
Write-Output "Controlli positivi: raggiungibili. Il criterio vede quello che deve vedere."
Write-Output ""
Write-Output "E NEMMENO 'RAGGIUNGIBILE' BASTA: dice che il file c'e' e che Windows sa"
Write-Output "crearlo. Se parte davvero lo dice solo SOLIDWORKS, con GetAddInObject --"
Write-Output "o una spunta in Strumenti -> Componenti aggiuntivi."
