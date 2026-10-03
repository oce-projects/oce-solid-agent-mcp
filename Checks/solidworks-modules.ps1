# =============================================================================
#  solidworks-modules.ps1 - quali moduli ci sono DAVVERO su questa
#                             postazione, e quali sono registrati  (voci Z-H*)
# =============================================================================
#
#  QUESTO FILE E' IN ASCII PURO, come bench.ps1 e per la stessa ragione.
#
#  PERCHE' ESISTE
#
#  Le voci Z-H1..Z-H5 chiedono tutte la stessa cosa: "il modulo c'e' o va
#  acceso?". E' una domanda che si risponde con una MISURA, non leggendo il
#  listino: un binario sul disco non vuol dire che sia registrato, e un CLSID
#  registrato non vuol dire che la licenza lo faccia partire.
#
#  Tre livelli, e vanno distinti:
#
#    1. IL FILE C'E'         -> il modulo e' stato installato almeno una volta
#    2. L'INTEROP LO DESCRIVE -> si puo' programmare contro di lui
#    3. IL CLSID E' REGISTRATO -> si puo' CREARE l'oggetto
#
#  Il terzo e' l'unico che conta per sapere se una chiamata partira'. E nemmeno
#  lui basta: la licenza si vede solo provando.
#
#  ── DUE TRAPPOLE, MISURATE ──────────────────────────────────────────────────
#
#  - **MAI `Win32_Product`.** Interrogarlo fa RICONFIGURARE ogni pacchetto MSI
#    della macchina: su questa postazione ha rotto l'avvio di SOLIDWORKS. Qui
#    si guardano file e registro, e basta.
#  - **CI SONO DUE ALBERI INSTALLATI** (`...\SOLIDWORKS` e `...\SOLIDWORKS (2)`)
#    e alcuni CLSID puntano al secondo. Lo script stampa il percorso che il
#    registro dichiara, non quello che ci si aspetta: se puntano altrove, si
#    vede.
#
#  USO
#      .\Checks\solidworks-modules.ps1
# =============================================================================

$ErrorActionPreference = "Continue"

$radice = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
$redist = Join-Path $radice "api\redist"

function Riga($etichetta, $valore) {
    Write-Output ("  {0,-34} {1}" -f $etichetta, $valore)
}

function FileC($percorso) {
    if (Test-Path $percorso) {
        $b = (Get-Item $percorso).Length
        return ("SI'  " + "{0:N0}" -f $b + " byte")
    }
    return "NO"
}

# CERCA, NON PRESUMERE.
#
# La prima stesura cercava `cosworks.dll` nella radice di SOLIDWORKS e
# rispondeva **NO**. Il file c'era, in `SOLIDWORKS\Simulation\`. Una sonda che
# guarda nel posto sbagliato dice «non c'e'» con la stessa sicurezza di
# un'assenza vera, e quella risposta finisce in un TODO come se fosse una
# misura.
#
# Qui si cerca sotto la radice e si stampa DOVE si e' trovato: il percorso e'
# parte della misura, non un dettaglio.
function Trova($nome) {
    try {
        $f = Get-ChildItem -Path $radice -Filter $nome -Recurse -File -ErrorAction SilentlyContinue |
             Select-Object -First 1
        if ($f) {
            return ("SI'  " + "{0:N0}" -f $f.Length + " byte  ->  " +
                    $f.DirectoryName.Replace($radice, "<radice>"))
        }
    } catch { }
    return "NON TROVATO sotto la radice"
}

# I coclass di un interop: sono le classi che si possono creare con `new`.
# Prenderli dall'interop stesso e non da una nota e' il punto -- una nota
# invecchia, l'assembly no.
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

# SI USA L'API .NET, NON IL PROVIDER `HKLM:`.
#
# Misurato il 23/9/2026: con `Test-Path HKLM:\...` questa sonda non finiva. Il
# Document Manager dichiara **piu' di sessanta coclass**, e ognuna costava
# qualche secondo di provider -- due giri uccisi dalla scadenza, nessuna misura.
# `OpenSubKey` fa la stessa domanda in un millesimo del tempo.
#
# Un attrezzo che non finisce non e' lento: e' un attrezzo che non risponde.
function Registrato($guid) {
    # HKCU PRIMA DI HKLM, come Windows fonde HKCR. Questa funzione guardava
    # solo HKLM: e' l'errore che questa casa ha gia' pagato con l'add-in
    # (HKCU\Software\Classes nasconde HKLM). Corretto il 24/9/2026 (voce K20).
    try {
        foreach ($radice in @([Microsoft.Win32.Registry]::CurrentUser,
                              [Microsoft.Win32.Registry]::LocalMachine)) {
            foreach ($tipo in @("InprocServer32", "LocalServer32")) {
                $k = $radice.OpenSubKey("SOFTWARE\Classes\CLSID\{$guid}\$tipo")
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

# SI STAMPANO SOLO QUELLE REGISTRATE, piu' il conto delle altre.
#
# Il primo giro elencava 26 `SwDMDocumentNClass` una per una, tutte «non
# registrato», e la riga che contava -- la fabbrica, l'unica che si crea con
# `new` -- spariva in mezzo. Un elenco che si legge male e' un elenco che non
# si legge, e allora la misura non e' stata fatta.
function Elenca($dll) {
    $cc = Coclass $dll
    $reg = 0
    foreach ($c in $cc) {
        $dove = Registrato $c.Guid
        if ($dove) { Riga ("  " + $c.Nome) "REGISTRATO -> $dove"; $reg++ }
    }
    Riga "coclass nell'interop" ("" + $cc.Count + ", di cui registrate: " + $reg)
    if ($reg -eq 0 -and $cc.Count -gt 0) {
        # QUESTA RIGA DICEVA «il modulo e' sul disco ma non si puo' creare», ed
        # era una conclusione cieca: il 24/9/2026 Simulation ha girato (voce S3)
        # e le coclass del SUO interop risultano 0 registrate su 80, come qui.
        # Le coclass dell'interop non sono l'add-in. L'add-in si risolve piu'
        # sotto, dal suo ProgID, con un controllo positivo (voce K20).
        Write-Output "    nessuna coclass dell'interop e' registrata. NON vuol dire che il"
        Write-Output "    modulo non si carichi: vedi la risoluzione dell'add-in in fondo."
    }
}

Write-Output ""
Write-Output "radice: $radice"
Write-Output ""

# ── Z-H1: SOLIDWORKS Utilities -----------------------------------------------
Write-Output "=== Z-H1  SOLIDWORKS Utilities (IThicknessAnalysis) ==="
Riga "cartella sldutils" $(if (Test-Path (Join-Path $radice "sldutils")) {
    "SI'  " + (Get-ChildItem (Join-Path $radice "sldutils") -File | Measure-Object).Count + " file" } else { "NO" })
Riga "ThickChkswu.dll" (Trova "ThickChkswu.dll")
Riga "interop gtswutilities" (FileC (Join-Path $redist "SolidWorks.Interop.gtswutilities.dll"))
Elenca (Join-Path $redist "SolidWorks.Interop.gtswutilities.dll")
Write-Output ""

# ── Z-H2: Document Manager ---------------------------------------------------
Write-Output "=== Z-H2  Document Manager (serve una CHIAVE, non una spunta) ==="
Riga "swdocumentmgr.dll" (Trova "swdocumentmgr.dll")
Riga "interop swdocumentmgr" (FileC (Join-Path $redist "SolidWorks.Interop.swdocumentmgr.dll"))
Elenca (Join-Path $redist "SolidWorks.Interop.swdocumentmgr.dll")
Write-Output "  NOTA: GetApplication vuole una chiave rilasciata da Dassault."
Write-Output "        Finche' non e' in mano, non si scrive una riga attorno (W2.14)."
Write-Output ""

# ── Z-H3: DimXpert ------------------------------------------------------------
Write-Output "=== Z-H3  DimXpert ==="
Riga "slddimxpertclru.dll" (Trova "slddimxpertclru.dll")
Riga "interop swdimxpert" (FileC (Join-Path $redist "SolidWorks.Interop.swdimxpert.dll"))
Elenca (Join-Path $redist "SolidWorks.Interop.swdimxpert.dll")
Write-Output ""

# ── Z-H4: Simulation ----------------------------------------------------------
Write-Output "=== Z-H4  Simulation ==="
Riga "cosworks.dll" (Trova "cosworks.dll")
Riga "interop cosworks" (FileC (Join-Path $redist "SolidWorks.Interop.cosworks.dll"))
$k = "HKCU:\SOFTWARE\SolidWorks\AddInsStartup"
if (Test-Path $k) {
    $p = Get-ItemProperty $k
    foreach ($n in ($p.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' })) {
        if ($n.Name -match 'cw|cos|Simulation') {
            Riga ("add-in " + $n.Name) $(if ($n.Value -eq 0) { "0 = NON attivo all'avvio" } else { $n.Value })
        }
    }
} else { Riga "AddInsStartup" "la chiave non c'e'" }
Write-Output ""

# ── Z-H5: Toolbox --------------------------------------------------------------
Write-Output "=== Z-H5  Toolbox ==="
Riga "cartella Toolbox" $(if (Test-Path "C:\SOLIDWORKS Data") { "SI'  C:\SOLIDWORKS Data" } else { "non in C:\SOLIDWORKS Data" })
Riga "interop toolboxconfigure" (FileC (Join-Path $redist "SolidWorks.Interop.sldtoolboxconfigureaddin.dll"))
Write-Output ""

# -- S4: Routing ----------------------------------------------------------------
Write-Output "=== S4  Routing (serve a questa officina?) ==="
Riga "interop SWRoutingLib" (FileC (Join-Path $redist "SolidWorks.Interop.SWRoutingLib.dll"))
Elenca (Join-Path $redist "SolidWorks.Interop.SWRoutingLib.dll")
Write-Output ""

# ==============================================================================
# GLI ADD-IN, RISOLTI DAVVERO -- e con due controlli positivi (voce K20)
# ==============================================================================
#
# QUI C'ERA «GLI ADD-IN REGISTRATI, la risposta piu' corta di tutte»: la
# chiave HKLM\SOFTWARE\SolidWorks\AddIns, e «un modulo che non compare qui
# non parte dentro SOLIDWORKS». Era FALSO, e lo si e' visto il 24/9/2026:
#
#   - quella chiave elenca solo gli add-in di TERZE PARTI (OpenPDM, Composer,
#     Marketplace...). I moduli di Dassault stanno altrove, o in nessuna chiave;
#   - Simulation non compare in NESSUNA delle tre chiavi -- ne' AddIns, ne'
#     SOLIDWORKS 2021\AddIns, ne' AddInsStartup -- e quel giorno ha girato
#     (voce S3, 54,3 MPa contro 60,0 a mano).
#
# Su questa riga poggiavano Z-H1, Z-H3, Z-H4 e S4: «non si puo' creare», «serve
# l'Installation Manager», «non si costruisce niente». Un controllo che da'
# la stessa risposta per un modulo acceso e per uno spento non misura niente,
# e nessuno l'aveva mai visto dire la verita' su un caso noto.
#
# ADESSO: ogni add-in si risolve come fa Windows -- ProgID -> CLSID ->
# InprocServer32 -> file, cercando in HKCU\Software\Classes PRIMA che in HKLM
# (e' l'ordine con cui si fonde HKCR, e questa casa lo ha gia' pagato), con i
# due casi che la prima stesura della sonda ha sbagliato:
#
#   - add-in .NET: InprocServer32 vale `mscoree.dll`, e il file vero sta in
#     `CodeBase`. La prima sonda diceva «FILE ASSENTE» per OpenPDM, che e'
#     installato e parte da solo;
#   - percorsi relativi: `.\sldUtils\SwLoaderSw.dll` e' relativo alla radice di
#     SOLIDWORKS.
#
# E DUE CONTROLLI POSITIVI, che devono risultare raggiungibili o lo script
# dichiara cieco il proprio criterio ed esce 1:
#   Simulation  (un modulo Dassault che non sta in nessuna chiave di add-in)
#   OpenPDM     (un add-in .NET, registrato in HKCU)
$regHKCU = [Microsoft.Win32.Registry]::CurrentUser
$regHKLM = [Microsoft.Win32.Registry]::LocalMachine

function ApriClasse($sotto) {
    foreach ($r in @($regHKCU, $regHKLM)) {
        $k = $r.OpenSubKey("SOFTWARE\Classes\$sotto")
        if ($k) { return $k }
    }
    return $null
}

function ClsidDelProgId($progId) {
    $k = ApriClasse "$progId\CLSID"
    if (-not $k) { return $null }
    $g = $k.GetValue(""); $k.Close()
    return $g
}

function FileDelServer($guid) {
    foreach ($tipo in @("InprocServer32", "LocalServer32")) {
        $k = ApriClasse "CLSID\$guid\$tipo"
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
            if ($v.StartsWith('.\')) { $v = Join-Path $radice $v.Substring(2) }
            return $v
        }
    }
    return $null
}

function Avvio($guid) {
    $k = $regHKCU.OpenSubKey("SOFTWARE\SolidWorks\AddInsStartup")
    if (-not $k) { return "" }
    $v = $k.GetValue($guid)
    if ($null -eq $v) { $s = $k.OpenSubKey($guid); if ($s) { $v = $s.GetValue(""); $s.Close() } }
    $k.Close()
    if ($null -eq $v) { return "" }
    if ("$v" -eq "1") { return "parte da solo" }
    return "va acceso a mano"
}

# I moduli, per ProgID. Si aggiungono qui, non si indovinano.
$moduli = @(
    @{ nome = "Simulation";          progId = "SldWorks.Simulation";         positivo = $true },
    @{ nome = "OpenPDM (add-in .NET)"; guid = "{0FED85A2-3F4A-4F2C-8DCF-7C8AE1E15A5C}"; positivo = $true },
    @{ nome = "SOLIDWORKS Utilities (Z-H1)"; progId = "Utilities.UtilitiesApp" },
    @{ nome = "Analysis Tools";      progId = "AnalysisTools.AnalysisAddin" }
)

Write-Output "=== GLI ADD-IN, RISOLTI (ProgID -> CLSID -> file) ==="
$ciechi = 0
foreach ($m in $moduli) {
    $g = $m.guid
    if (-not $g -and $m.progId) { $g = ClsidDelProgId $m.progId }
    if (-not $g) {
        Riga $m.nome "ProgID NON registrato"
        if ($m.positivo) { $ciechi++ }
        continue
    }
    $f = FileDelServer $g
    $esito = if (-not $f) { "CLSID senza server" }
             elseif (Test-Path $f) { "RAGGIUNGIBILE" }
             else { "server dichiarato, FILE ASSENTE" }
    $a = Avvio $g
    Riga $m.nome ($esito + $(if ($a) { " - $a" } else { "" }))
    if ($f) { Write-Output ("      -> " + $f.Replace($radice, "<radice>")) }
    if ($m.positivo -and $esito -ne "RAGGIUNGIBILE") { $ciechi++ }
}
Write-Output ""

# Le tre chiavi di add-in, per completezza: dicono chi e' elencato, non chi
# si carica.
Write-Output "=== LE TRE CHIAVI DI ADD-IN (chi e' elencato, NON chi si carica) ==="
foreach ($c in @(
    @{ n = "HKLM AddIns";                 r = $regHKLM; p = "SOFTWARE\SolidWorks\AddIns" },
    @{ n = "HKLM SOLIDWORKS 2021\AddIns"; r = $regHKLM; p = "SOFTWARE\SolidWorks\SOLIDWORKS 2021\AddIns" })) {
    $k = $c.r.OpenSubKey($c.p)
    if (-not $k) { Riga $c.n "la chiave non c'e'"; continue }
    foreach ($g in $k.GetSubKeyNames()) {
        $s = $k.OpenSubKey($g); $t = ""
        if ($s) { $t = "" + $s.GetValue("Title"); $s.Close() }
        $f = FileDelServer $g
        $esito = if ($f -and (Test-Path $f)) { "ok" } elseif ($f) { "file assente" } else { "senza server" }
        Riga ("  " + $t) ($c.n + ": " + $esito)
    }
    $k.Close()
}
Write-Output ""

if ($ciechi -gt 0) {
    Write-Output "XX  CRITERIO CIECO: $ciechi controlli positivi non risultano raggiungibili."
    Write-Output "    Simulation e l'add-in .NET DEVONO risolversi: se non lo fanno, e' la"
    Write-Output "    sonda a sbagliare, e ogni 'non raggiungibile' qui sopra non vale niente."
    exit 1
}
Write-Output "Controlli positivi: raggiungibili. Il criterio vede quello che deve vedere."
Write-Output ""
Write-Output "E NEMMENO 'RAGGIUNGIBILE' BASTA: dice che il file c'e' e che Windows sa"
Write-Output "crearlo. Se parte davvero lo dice solo SOLIDWORKS, con GetAddInObject --"
Write-Output "o una spunta in Strumenti -> Componenti aggiuntivi."
