# =============================================================================
#  Compila tutto e produce UN SOLO file: dist\Installer.exe
# =============================================================================
#
#  Tre passi, in quest'ordine perche' il secondo dipende dal primo:
#
#    1. compila il ponte             -> bin\oce-solid-agent-mcp.exe
#    2. lo mette in Installer\Payload\ come risorsa da incorporare
#    3. compila l'installatore       -> dist\Installer.exe, che se lo porta dentro
#
#  Il risultato e' un eseguibile unico da dare a chiunque: si scarica, doppio
#  clic, e Claude parla con SOLIDWORKS. Nessun altro file da mettere accanto.
#
#  COSA FINISCE DENTRO: il ponte, il catalogo, la configurazione, le note
#  legali E LE OPERAZIONI (Oce.Solid.Agent.Core.dll). Fino alla 0.5.0 le
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
#      .\build.ps1 -Clean     cancella prima bin, obj e dist
#      .\build.ps1 -BridgeOnly   solo il ponte, senza installatore
#
#      .\build.ps1 -SwInstallPath 'D:\...\SOLIDWORKS'
#      $env:SW_INSTALL_PATH = 'D:\...\SOLIDWORKS'
#                               se SOLIDWORKS non e' nel posto di serie
#
# =============================================================================

[CmdletBinding()]
param(
    [switch] $Clean,
    [switch] $BridgeOnly,

    # DOVE STA SOLIDWORKS. I .csproj lo leggono gia' da /p:SwInstallPath, ma
    # questo script non lo passava: chi ha SOLIDWORKS in un posto non standard
    # non poteva compilare con `.\build.ps1` e doveva chiamare msbuild a mano,
    # cosa che il README non diceva. La cartella e' la RADICE
    # dell'installazione, non `api\redist`: i .csproj ci aggiungono loro il
    # resto.
    [string] $SwInstallPath
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Anche da variabile d'ambiente, cosi' non va ripetuto a ogni chiamata.
if (-not $SwInstallPath -and $env:SW_INSTALL_PATH) { $SwInstallPath = $env:SW_INSTALL_PATH }
$location = @()
if ($SwInstallPath) {
    if (-not (Test-Path $SwInstallPath)) {
        Write-Host "  -SwInstallPath punta a una cartella che non c'e': $SwInstallPath" -ForegroundColor Red
        exit 1
    }
    $location = @("-p:SwInstallPath=$SwInstallPath")
}

function Step($t) { Write-Host ""; Write-Host "=== $t" -ForegroundColor Cyan }
function Ok($t)    { Write-Host "  $t" -ForegroundColor Green }
function Note($t)  { Write-Host "  $t" -ForegroundColor DarkGray }
function Die($t) { Write-Host "  $t" -ForegroundColor Red; exit 1 }

# -----------------------------------------------------------------------------
Step "MSBuild"
# -----------------------------------------------------------------------------
# NON si usa quello dentro C:\Windows\Microsoft.NET: e' la versione del
# framework, non sa leggere questi csproj e fallisce con «OutputPath non
# impostato», che manda a cercare la cosa sbagliata. Misurato.
$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    try {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
        if ($found) { $msbuild = @($found)[0] }
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
    Die ("MSBuild non trovato. Installa 'Build Tools for Visual Studio 2022' " +
           "(gratis) da https://visualstudio.microsoft.com/downloads/ scegliendo " +
           "'Sviluppo di applicazioni desktop .NET'.")
}
Ok "trovato"
Note $msbuild

# -----------------------------------------------------------------------------
if ($Clean) {
    Step "Pulizia"
    foreach ($d in @("bin", "obj", "dist", "Installer\obj", "Installer\Payload")) {
        $p = Join-Path $root $d
        if (Test-Path $p) { Remove-Item $p -Recurse -Force; Note "tolto $d" }
    }
    Ok "pulito"
}

# -----------------------------------------------------------------------------
Step "1. Il ponte"
# -----------------------------------------------------------------------------
& $msbuild (Join-Path $root "Oce.Solid.Agent.csproj") -p:Configuration=Release -v:m -nologo @location
if ($LASTEXITCODE -ne 0) { Die "compilazione del ponte fallita" }

$bridge = Join-Path $root "bin\oce-solid-agent-mcp.exe"
if (-not (Test-Path $bridge)) { Die "compilato senza errori ma oce-solid-agent-mcp.exe non c'e'" }
Ok ("oce-solid-agent-mcp.exe  " + [math]::Round((Get-Item $bridge).Length / 1KB) + " kB")

if ($BridgeOnly) { Step "Fatto"; Ok $bridge; exit 0 }

# -----------------------------------------------------------------------------
Step "1b. Il catalogo"
# -----------------------------------------------------------------------------
# SI RIGENERA, NON SI SPERA.
#
# `catalog.json` e' un PRODOTTO del catalogo C#. Finche' lo si rigenerava a
# mano — e prima serviva il server, docker e la rete — poteva restare indietro
# rispetto al codice senza che nulla lo dicesse: il ponte avrebbe dichiarato
# operazioni con descrizioni vecchie, o taciuto operazioni nuove.
#
# Qui si riscrive a ogni pacchetto, e se cambia lo si dice: un file che cambia
# durante una build e' una cosa che chi guarda deve vedere, non una sorpresa
# nel prossimo `git status`.
& $msbuild (Join-Path $root "Oce.Solid.Agent.Catalog\Oce.Solid.Agent.Catalog.csproj") -p:Configuration=Release -v:m -nologo @location
if ($LASTEXITCODE -ne 0) { Die "compilazione dell'esportatore del catalogo fallita" }

$exporter = Join-Path $root "bin\Oce.Solid.Agent.Catalog.exe"
$catalog = Join-Path $root "Tools\catalog.json"
$beforeBytes = $null
if (Test-Path $catalog) { $beforeBytes = [IO.File]::ReadAllBytes($catalog) }

# UN ESEGUIBILE CHE SCRIVE SU stderr NON E' UN ESEGUIBILE CHE FALLISCE.
#
# `Oce.Solid.Agent.Catalog.exe` stampa su stderr la riga «[catalogo] scritto ...»,
# che e' un resoconto e non un errore. Ma con `$ErrorActionPreference = 'Stop'`
# PowerShell 5.1 avvolge ogni riga di stderr di un comando nativo in un
# ErrorRecord (`NativeCommandError`) e FERMA lo script, anche con l'eseguibile
# uscito 0. Il build si interrompeva a meta' dicendo che era andato male
# mentre era andato bene, e il catalogo risultava scritto.
#
# Si abbassa la preferenza SOLO intorno alla chiamata nativa, e si continua a
# guardare $LASTEXITCODE, che e' l'unica cosa che dice davvero com'e' andata.
$oldPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $exporter $catalog
$catalogOutcome = $LASTEXITCODE
$ErrorActionPreference = $oldPreference
if ($catalogOutcome -ne 0) { Die "il catalogo non si e' scritto" }

$afterBytes = [IO.File]::ReadAllBytes($catalog)
if ($null -eq $beforeBytes) {
    Ok "catalog.json scritto ($($afterBytes.Length) byte)"
} elseif ([Convert]::ToBase64String($beforeBytes) -eq [Convert]::ToBase64String($afterBytes)) {
    Ok "catalog.json invariato ($($afterBytes.Length) byte)"
} else {
    Ok "catalog.json RIGENERATO: $($beforeBytes.Length) -> $($afterBytes.Length) byte"
    Note "era indietro rispetto al C#. Guarda il diff prima di committare."
}

# Il ponte legge quello che sta accanto a lui, non quello in Tools\.
Copy-Item $catalog (Join-Path $root "bin\catalog.json") -Force

# -----------------------------------------------------------------------------
Step "1c. Le sonde"
# -----------------------------------------------------------------------------
# LE SONDE SI COMPILANO A OGNI PACCHETTO, e non per scrupolo.
#
# Misurato il 25/9/2026: `Checks\NewPart` e `Checks\BrokenBody` non
# compilavano dalla 0.86.0 — `Environment` ambiguo fra System e l'interop di
# SOLIDWORKS — e nessuno se n'era accorto per tre versioni, perche'
# `build-checks.ps1` si lancia a mano e nessuno lo lanciava. Una sonda
# che non compila e' una misura che non si puo' rifare il giorno che serve.
# Costa tredici secondi.
$oldPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "Checks\build-checks.ps1") *> $null
$probesOutcome = $LASTEXITCODE
$ErrorActionPreference = $oldPreference
if ($probesOutcome -ne 0) { Die "una sonda non compila: lancia .\Checks\build-checks.ps1 per vedere quale" }
Ok "tutte le sonde compilano"

# -----------------------------------------------------------------------------
Step "2. Il carico da incorporare"
# -----------------------------------------------------------------------------
$payload = Join-Path $root "Installer\Payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

# Quello che serve al ponte per girare, e le note legali che devono viaggiare
# con il programma.
#
# `Oce.Solid.Agent.Core.dll` E' OBBLIGATORIO, non facoltativo: senza, il ponte
# si installa e poi non disegna. Un pacchetto che si monta e non funziona e'
# peggio di uno che rifiuta di montarsi.
$toBring = @(
    @{ da = "bin\oce-solid-agent-mcp.exe";        a = "oce-solid-agent-mcp.exe";        obbligatorio = $true  },
    @{ da = "bin\Oce.Solid.Agent.Core.dll";    a = "Oce.Solid.Agent.Core.dll";    obbligatorio = $true  },
    @{ da = "bin\catalog.json";            a = "catalog.json";            obbligatorio = $true  },
    @{ da = "bin\oce-solid-agent-mcp.exe.config"; a = "oce-solid-agent-mcp.exe.config"; obbligatorio = $true  },
    @{ da = "LICENSE";                      a = "LICENSE";                  obbligatorio = $false },
    @{ da = "NOTICE";                       a = "NOTICE";                   obbligatorio = $false }
)

foreach ($f in $toBring) {
    $source = Join-Path $root $f.da
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $payload $f.a) -Force
        Note ("+ " + $f.a)
    } elseif ($f.obbligatorio) {
        Die ("manca " + $f.da + ", che deve finire dentro l'installatore")
    } else {
        Note ("- " + $f.a + " (non c'e', si prosegue)")
    }
}
Ok ((Get-ChildItem $payload).Count.ToString() + " file da incorporare")

# -----------------------------------------------------------------------------
Step "3. L'installatore"
# -----------------------------------------------------------------------------
& $msbuild (Join-Path $root "Installer\Installer.csproj") -p:Configuration=Release -v:m -nologo @location
if ($LASTEXITCODE -ne 0) { Die "compilazione dell'installatore fallita" }

$exe = Join-Path $root "bin\Installer.exe"
if (-not (Test-Path $exe)) { Die "compilato senza errori ma Installer.exe non c'e'" }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$final = Join-Path $dist "Installer.exe"
Copy-Item $exe $final -Force

# -----------------------------------------------------------------------------
Step "Verifica"
# -----------------------------------------------------------------------------
# Un pacchetto piu' piccolo del ponte che dovrebbe contenere e' un pacchetto
# che non lo contiene. E' un controllo grossolano e prende il caso che conta:
# la cartella Payload dimenticata o vuota.
$bigBridge = (Get-Item $bridge).Length
$bigExe = (Get-Item $final).Length
if ($bigExe -le $bigBridge) {
    Die ("Installer.exe e' " + [math]::Round($bigExe / 1KB) + " kB ma il ponte da " +
           "solo ne occupa " + [math]::Round($bigBridge / 1KB) + ": il carico non e' " +
           "stato incorporato. Controlla Installer\Payload\ e l'elemento " +
           "EmbeddedResource nel csproj.")
}
Ok ("Installer.exe  " + [math]::Round($bigExe / 1KB) + " kB  (ponte incorporato)")
Note $final

# IL CHECKSUM ACCANTO AL FILE, SEMPRE.
#
# La distribuzione decisa (voce H8) e' "il file piu' il suo checksum, consegnato
# a mano". Finche' il checksum si calcolava a mano non esisteva quasi mai, e
# QUICKSTART.md diceva di confrontarlo con un file che nessuno produceva: una
# istruzione che non si puo' eseguire e' peggio di nessuna istruzione, perche'
# chi la segue conclude di aver verificato.
#
# Il formato e' quello di `sha256sum`: "<hash minuscolo>  <nome file>". Due
# spazi, e il solo NOME e non il percorso, cosi' vale su qualunque macchina.
$fingerprint = (Get-FileHash $final -Algorithm SHA256).Hash.ToLowerInvariant()
$fingerprintFile = $final + ".sha256"
Set-Content -Path $fingerprintFile -Encoding ascii -NoNewline `
    -Value ($fingerprint + "  Installer.exe`n")
Ok ("Installer.exe.sha256  " + $fingerprint.Substring(0, 16) + "...")
Note $fingerprintFile

Write-Host ""
# ADESSO SI PUO' DIRE, PERCHE' E' VERO.
# Questa riga ha detto per una versione «installabile da chiunque, usabile da
# chi ha gia' l'add-in»: il carico non poteva contenere le operazioni, che
# stavano in una DLL privata. Da quando stanno in Oce.Solid.Agent.Core.dll,
# dentro questo repository, il carico se le porta. L'unica cosa che serve
# sulla macchina di chi installa e' SOLIDWORKS, per le sue interop.
Write-Host "  Un file solo. Dallo a chiunque abbia SOLIDWORKS." -ForegroundColor White
Write-Host "  Prova:  .\dist\Installer.exe --aiuto" -ForegroundColor DarkGray
