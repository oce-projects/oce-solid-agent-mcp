# =============================================================================
#  Compila tutto e produce UN SOLO file: dist\Installa.exe
# =============================================================================
#
#  Tre passi, in quest'ordine perche' il secondo dipende dal primo:
#
#    1. compila il ponte             -> bin\oce-solid-agent-mcp.exe
#    2. lo mette in Installer\Payload\ come risorsa da incorporare
#    3. compila l'installatore       -> dist\Installa.exe, che se lo porta dentro
#
#  Il risultato e' un eseguibile unico da dare a chiunque: si scarica, doppio
#  clic, e Claude parla con SOLIDWORKS. Nessun altro file da mettere accanto.
#
#  COSA FINISCE DENTRO: il ponte, il catalogo, la configurazione, le note
#  legali E LE OPERAZIONI (Riscontro.Disegno.dll). Fino alla 0.5.0 le
#  operazioni stavano in OpenPDM.SwAddin.dll, codice privato che un pacchetto
#  pubblico non poteva incorporare senza pubblicarlo: l'installatore doveva
#  cercarlo sulla macchina di chi installa, e chi non ce l'aveva installava un
#  ponte che si fermava subito. Da quando la libreria e' in questo repository
#  quella ragione non c'e' piu'.
#
#  COSA CONTINUA A NON FINIRE DENTRO: le interop di Dassault. Quelle si
#  prendono dall'installazione di SOLIDWORKS di chi installa — sono sue, e
#  sono per definizione della versione giusta.
#
#      .\build.ps1              compila e impacchetta
#      .\build.ps1 -Pulisci     cancella prima bin, obj e dist
#      .\build.ps1 -SoloPonte   solo il ponte, senza installatore
#
#      .\build.ps1 -SwInstallPath 'D:\...\SOLIDWORKS'
#      $env:SW_INSTALL_PATH = 'D:\...\SOLIDWORKS'
#                               se SOLIDWORKS non e' nel posto di serie
#
# =============================================================================

[CmdletBinding()]
param(
    [switch] $Pulisci,
    [switch] $SoloPonte,

    # DOVE STA SOLIDWORKS. I .csproj lo leggono gia' da /p:SwInstallPath, ma
    # questo script non lo passava: chi ha SOLIDWORKS in un posto non standard
    # non poteva compilare con `.\build.ps1` e doveva chiamare msbuild a mano,
    # cosa che il README non diceva. La cartella e' la RADICE
    # dell'installazione, non `api\redist`: i .csproj ci aggiungono loro il
    # resto.
    [string] $SwInstallPath
)

$ErrorActionPreference = "Stop"
$radice = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Anche da variabile d'ambiente, cosi' non va ripetuto a ogni chiamata.
if (-not $SwInstallPath -and $env:SW_INSTALL_PATH) { $SwInstallPath = $env:SW_INSTALL_PATH }
$dove = @()
if ($SwInstallPath) {
    if (-not (Test-Path $SwInstallPath)) {
        Write-Host "  -SwInstallPath punta a una cartella che non c'e': $SwInstallPath" -ForegroundColor Red
        exit 1
    }
    $dove = @("-p:SwInstallPath=$SwInstallPath")
}

function Passo($t) { Write-Host ""; Write-Host "=== $t" -ForegroundColor Cyan }
function Ok($t)    { Write-Host "  $t" -ForegroundColor Green }
function Nota($t)  { Write-Host "  $t" -ForegroundColor DarkGray }
function Muori($t) { Write-Host "  $t" -ForegroundColor Red; exit 1 }

# -----------------------------------------------------------------------------
Passo "MSBuild"
# -----------------------------------------------------------------------------
# NON si usa quello dentro C:\Windows\Microsoft.NET: e' la versione del
# framework, non sa leggere questi csproj e fallisce con «OutputPath non
# impostato», che manda a cercare la cosa sbagliata. Misurato.
$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    try {
        $trovato = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
        if ($trovato) { $msbuild = @($trovato)[0] }
    } catch { }
}
if (-not $msbuild) {
    foreach ($c in @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )) { if (Test-Path $c) { $msbuild = $c; break } }
}
if (-not $msbuild) {
    Muori ("MSBuild non trovato. Installa 'Build Tools for Visual Studio 2022' " +
           "(gratis) da https://visualstudio.microsoft.com/downloads/ scegliendo " +
           "'Sviluppo di applicazioni desktop .NET'.")
}
Ok "trovato"
Nota $msbuild

# -----------------------------------------------------------------------------
if ($Pulisci) {
    Passo "Pulizia"
    foreach ($d in @("bin", "obj", "dist", "Installer\obj", "Installer\Payload")) {
        $p = Join-Path $radice $d
        if (Test-Path $p) { Remove-Item $p -Recurse -Force; Nota "tolto $d" }
    }
    Ok "pulito"
}

# -----------------------------------------------------------------------------
Passo "1. Il ponte"
# -----------------------------------------------------------------------------
& $msbuild (Join-Path $radice "Oce.Solid.Agent.csproj") -p:Configuration=Release -v:m -nologo @dove
if ($LASTEXITCODE -ne 0) { Muori "compilazione del ponte fallita" }

$ponte = Join-Path $radice "bin\oce-solid-agent-mcp.exe"
if (-not (Test-Path $ponte)) { Muori "compilato senza errori ma oce-solid-agent-mcp.exe non c'e'" }
Ok ("oce-solid-agent-mcp.exe  " + [math]::Round((Get-Item $ponte).Length / 1KB) + " kB")

if ($SoloPonte) { Passo "Fatto"; Ok $ponte; exit 0 }

# -----------------------------------------------------------------------------
Passo "1b. Il catalogo"
# -----------------------------------------------------------------------------
# SI RIGENERA, NON SI SPERA.
#
# `catalogo.json` e' un PRODOTTO del catalogo C#. Finche' lo si rigenerava a
# mano — e prima serviva il server, docker e la rete — poteva restare indietro
# rispetto al codice senza che nulla lo dicesse: il ponte avrebbe dichiarato
# operazioni con descrizioni vecchie, o taciuto operazioni nuove.
#
# Qui si riscrive a ogni pacchetto, e se cambia lo si dice: un file che cambia
# durante una build e' una cosa che chi guarda deve vedere, non una sorpresa
# nel prossimo `git status`.
& $msbuild (Join-Path $radice "Riscontro.Catalogo\Riscontro.Catalogo.csproj") -p:Configuration=Release -v:m -nologo @dove
if ($LASTEXITCODE -ne 0) { Muori "compilazione dell'esportatore del catalogo fallita" }

$esportatore = Join-Path $radice "bin\Riscontro.Catalogo.exe"
$catalogo = Join-Path $radice "Strumenti\catalogo.json"
$primaDi = $null
if (Test-Path $catalogo) { $primaDi = [IO.File]::ReadAllBytes($catalogo) }

& $esportatore $catalogo
if ($LASTEXITCODE -ne 0) { Muori "il catalogo non si e' scritto" }

$dopoDi = [IO.File]::ReadAllBytes($catalogo)
if ($null -eq $primaDi) {
    Ok "catalogo.json scritto ($($dopoDi.Length) byte)"
} elseif ([Convert]::ToBase64String($primaDi) -eq [Convert]::ToBase64String($dopoDi)) {
    Ok "catalogo.json invariato ($($dopoDi.Length) byte)"
} else {
    Ok "catalogo.json RIGENERATO: $($primaDi.Length) -> $($dopoDi.Length) byte"
    Nota "era indietro rispetto al C#. Guarda il diff prima di committare."
}

# Il ponte legge quello che sta accanto a lui, non quello in Strumenti\.
Copy-Item $catalogo (Join-Path $radice "bin\catalogo.json") -Force

# -----------------------------------------------------------------------------
Passo "2. Il carico da incorporare"
# -----------------------------------------------------------------------------
$payload = Join-Path $radice "Installer\Payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

# Quello che serve al ponte per girare, e le note legali che devono viaggiare
# con il programma.
#
# `Riscontro.Disegno.dll` E' OBBLIGATORIO, non facoltativo: senza, il ponte
# si installa e poi non disegna. Un pacchetto che si monta e non funziona e'
# peggio di uno che rifiuta di montarsi.
$daPortare = @(
    @{ da = "bin\oce-solid-agent-mcp.exe";        a = "oce-solid-agent-mcp.exe";        obbligatorio = $true  },
    @{ da = "bin\Riscontro.Disegno.dll";    a = "Riscontro.Disegno.dll";    obbligatorio = $true  },
    @{ da = "bin\catalogo.json";            a = "catalogo.json";            obbligatorio = $true  },
    @{ da = "bin\oce-solid-agent-mcp.exe.config"; a = "oce-solid-agent-mcp.exe.config"; obbligatorio = $true  },
    @{ da = "LICENSE";                      a = "LICENSE";                  obbligatorio = $false },
    @{ da = "NOTICE";                       a = "NOTICE";                   obbligatorio = $false }
)

foreach ($f in $daPortare) {
    $sorgente = Join-Path $radice $f.da
    if (Test-Path $sorgente) {
        Copy-Item $sorgente (Join-Path $payload $f.a) -Force
        Nota ("+ " + $f.a)
    } elseif ($f.obbligatorio) {
        Muori ("manca " + $f.da + ", che deve finire dentro l'installatore")
    } else {
        Nota ("- " + $f.a + " (non c'e', si prosegue)")
    }
}
Ok ((Get-ChildItem $payload).Count.ToString() + " file da incorporare")

# -----------------------------------------------------------------------------
Passo "3. L'installatore"
# -----------------------------------------------------------------------------
& $msbuild (Join-Path $radice "Installer\Installa.csproj") -p:Configuration=Release -v:m -nologo @dove
if ($LASTEXITCODE -ne 0) { Muori "compilazione dell'installatore fallita" }

$exe = Join-Path $radice "bin\Installa.exe"
if (-not (Test-Path $exe)) { Muori "compilato senza errori ma Installa.exe non c'e'" }

$dist = Join-Path $radice "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$finale = Join-Path $dist "Installa.exe"
Copy-Item $exe $finale -Force

# -----------------------------------------------------------------------------
Passo "Verifica"
# -----------------------------------------------------------------------------
# Un pacchetto piu' piccolo del ponte che dovrebbe contenere e' un pacchetto
# che non lo contiene. E' un controllo grossolano e prende il caso che conta:
# la cartella Payload dimenticata o vuota.
$grandePonte = (Get-Item $ponte).Length
$grandeExe = (Get-Item $finale).Length
if ($grandeExe -le $grandePonte) {
    Muori ("Installa.exe e' " + [math]::Round($grandeExe / 1KB) + " kB ma il ponte da " +
           "solo ne occupa " + [math]::Round($grandePonte / 1KB) + ": il carico non e' " +
           "stato incorporato. Controlla Installer\Payload\ e l'elemento " +
           "EmbeddedResource nel csproj.")
}
Ok ("Installa.exe  " + [math]::Round($grandeExe / 1KB) + " kB  (ponte incorporato)")
Nota $finale

Write-Host ""
# ADESSO SI PUO' DIRE, PERCHE' E' VERO.
# Questa riga ha detto per una versione «installabile da chiunque, usabile da
# chi ha gia' l'add-in»: il carico non poteva contenere le operazioni, che
# stavano in una DLL privata. Da quando stanno in Riscontro.Disegno.dll,
# dentro questo repository, il carico se le porta. L'unica cosa che serve
# sulla macchina di chi installa e' SOLIDWORKS, per le sue interop.
Write-Host "  Un file solo. Dallo a chiunque abbia SOLIDWORKS." -ForegroundColor White
Write-Host "  Prova:  .\dist\Installa.exe --aiuto" -ForegroundColor DarkGray
