# =============================================================================
#  moduli-di-solidworks.ps1 - quali moduli ci sono DAVVERO su questa
#                             postazione, e quali sono registrati  (voci Z-H*)
# =============================================================================
#
#  QUESTO FILE E' IN ASCII PURO, come banco.ps1 e per la stessa ragione.
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
#      .\Controlli\moduli-di-solidworks.ps1
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
    try {
        $hklm = [Microsoft.Win32.Registry]::LocalMachine
        foreach ($tipo in @("InprocServer32", "LocalServer32")) {
            $k = $hklm.OpenSubKey("SOFTWARE\Classes\CLSID\{$guid}\$tipo")
            if ($k) {
                $v = $k.GetValue("")
                $k.Close()
                if ($v) { return $v }
                return "(chiave senza valore)"
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
        Write-Output "    NESSUNA registrata: il modulo e' sul disco ma non si puo' creare."
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

# -- GLI ADD-IN REGISTRATI, che e' la risposta piu' corta di tutte -----------
#
# Un modulo che non compare qui non parte dentro SOLIDWORKS, qualunque cosa
# dica il listino delle licenze. Il valore in HKCU dice se parte DA SOLO
# all'avvio; 0 vuol dire che c'e' ma va acceso a mano.
Write-Output "=== GLI ADD-IN REGISTRATI SU QUESTA POSTAZIONE ==="
$hklm = "HKLM:\SOFTWARE\SolidWorks\AddIns"
$hkcu = "HKCU:\SOFTWARE\SolidWorks\AddInsStartup"
if (Test-Path $hklm) {
    foreach ($g in (Get-ChildItem $hklm -ErrorAction SilentlyContinue).PSChildName) {
        $titolo = ""
        try { $titolo = (Get-ItemProperty (Join-Path $hklm $g)).Title } catch {}
        $attivo = "non elencato"
        $kk = Join-Path $hkcu $g
        if (Test-Path $kk) { try { $attivo = (Get-ItemProperty $kk)."(default)" } catch { $attivo = "?" } }
        Riga $titolo ("parte da solo: " + $attivo)
    }
} else { Riga "HKLM\SOFTWARE\SolidWorks\AddIns" "la chiave non c'e'" }
Write-Output ""

Write-Output "Tre livelli, e solo il terzo dice che una chiamata partira':"
Write-Output "  il file c'e'  <  l'interop lo descrive  <  il CLSID e' registrato"
Write-Output "E nemmeno quello basta: la licenza si vede solo provando."
