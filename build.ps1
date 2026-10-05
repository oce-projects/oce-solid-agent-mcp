# =============================================================================
#  Builds everything and produces A SINGLE file: dist\Installer.exe
# =============================================================================
#
#  Three steps, in this order because the second depends on the first:
#
#    1. builds the bridge             -> bin\oce-solid-agent-mcp.exe
#    2. puts it in Installer\Payload\ as a resource to embed
#    3. builds the installer          -> dist\Installer.exe, which carries it inside
#
#  The result is a single executable to give to anyone: download, double
#  click, and Claude talks to SOLIDWORKS. No other file to put next to it.
#
#  WHAT ENDS UP INSIDE: the bridge, the catalog, the configuration, the legal
#  notices AND THE OPERATIONS (Oce.Solid.Agent.Core.dll). Up to 0.5.0 the
#  operations lived in OpenPDM.SwAddin.dll, private code that a public
#  package could not embed without publishing it: the installer had to
#  look for it on the machine of whoever installs, and whoever did not have it installed a
#  bridge that stopped immediately. Since the library is in this repository
#  that reason is gone.
#
#  WHAT STILL DOES NOT END UP INSIDE: the Dassault interops. Those are
#  taken from the SOLIDWORKS installation of whoever installs — they are theirs, and
#  they are by definition of the right version.
#
#      .\build.ps1              builds and packages
#      .\build.ps1 -Clean     deletes bin, obj and dist first
#      .\build.ps1 -BridgeOnly   only the bridge, without installer
#
#      .\build.ps1 -SwInstallPath 'D:\...\SOLIDWORKS'
#      $env:SW_INSTALL_PATH = 'D:\...\SOLIDWORKS'
#                               if SOLIDWORKS is not in the default place
#
# =============================================================================

[CmdletBinding()]
param(
    [switch] $Clean,
    [switch] $BridgeOnly,

    # WHERE SOLIDWORKS IS. The .csproj files already read it from /p:SwInstallPath, but
    # this script did not pass it: whoever has SOLIDWORKS in a non-standard place
    # could not build with `.\build.ps1` and had to call msbuild by hand,
    # which the README did not say. The folder is the ROOT
    # of the installation, not `api\redist`: the .csproj files add the
    # rest themselves.
    [string] $SwInstallPath
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Also from an environment variable, so it does not have to be repeated at every call.
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
# The one inside C:\Windows\Microsoft.NET is NOT used: it is the version of the
# framework, it does not know how to read these csproj and fails with «OutputPath not
# set», which sends one looking for the wrong thing. Measured.
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
# IT IS REGENERATED, NOT HOPED FOR.
#
# `catalog.json` is a PRODUCT of the C# catalog. As long as it was regenerated by
# hand — and before that the server, docker and the network were needed — it could fall behind
# the code without anything saying so: the bridge would have declared
# operations with old descriptions, or kept silent about new operations.
#
# Here it is rewritten at every package, and if it changes it says so: a file that changes
# during a build is something the person looking must see, not a surprise
# in the next `git status`.
& $msbuild (Join-Path $root "Oce.Solid.Agent.Catalog\Oce.Solid.Agent.Catalog.csproj") -p:Configuration=Release -v:m -nologo @location
if ($LASTEXITCODE -ne 0) { Die "compilazione dell'esportatore del catalogo fallita" }

$exporter = Join-Path $root "bin\Oce.Solid.Agent.Catalog.exe"
$catalog = Join-Path $root "Tools\catalog.json"
$beforeBytes = $null
if (Test-Path $catalog) { $beforeBytes = [IO.File]::ReadAllBytes($catalog) }

# AN EXECUTABLE THAT WRITES TO stderr IS NOT AN EXECUTABLE THAT FAILS.
#
# `Oce.Solid.Agent.Catalog.exe` prints to stderr the line «[catalogo] scritto ...»,
# which is a report and not an error. But with `$ErrorActionPreference = 'Stop'`
# PowerShell 5.1 wraps every stderr line of a native command in an
# ErrorRecord (`NativeCommandError`) and STOPS the script, even with the executable
# having exited 0. The build was interrupted halfway saying it had gone badly
# when it had gone well, and the catalog turned out to be written.
#
# The preference is lowered ONLY around the native call, and one keeps
# looking at $LASTEXITCODE, which is the only thing that really says how it went.
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

# The bridge reads what is next to it, not what is in Tools\.
Copy-Item $catalog (Join-Path $root "bin\catalog.json") -Force

# -----------------------------------------------------------------------------
Step "1c. Le sonde"
# -----------------------------------------------------------------------------
# THE PROBES ARE COMPILED AT EVERY PACKAGE, and not out of scruple.
#
# Measured on 25/9/2026: `Checks\NewPart` and `Checks\BrokenBody` had not
# compiled since 0.86.0 — `Environment` ambiguous between System and the SOLIDWORKS
# interop — and nobody had noticed for three versions, because
# `build-checks.ps1` is run by hand and nobody ran it. A probe
# that does not compile is a measurement that cannot be redone the day it is needed.
# It costs thirteen seconds.
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

# What the bridge needs in order to run, and the legal notices that must travel
# with the program.
#
# `Oce.Solid.Agent.Core.dll` IS MANDATORY, not optional: without it, the bridge
# installs and then does not draw. A package that assembles and does not work is
# worse than one that refuses to assemble.
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
# A package smaller than the bridge it should contain is a package
# that does not contain it. It is a coarse check and it catches the case that matters:
# the Payload folder forgotten or empty.
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

# THE CHECKSUM NEXT TO THE FILE, ALWAYS.
#
# The decided distribution (entry H8) is "the file plus its checksum, delivered
# by hand". As long as the checksum was computed by hand it almost never existed, and
# QUICKSTART.md said to compare it with a file that nobody produced: an
# instruction that cannot be executed is worse than no instruction, because
# whoever follows it concludes they have verified.
#
# The format is that of `sha256sum`: "<lowercase hash>  <file name>". Two
# spaces, and the NAME alone and not the path, so it holds on any machine.
$fingerprint = (Get-FileHash $final -Algorithm SHA256).Hash.ToLowerInvariant()
$fingerprintFile = $final + ".sha256"
Set-Content -Path $fingerprintFile -Encoding ascii -NoNewline `
    -Value ($fingerprint + "  Installer.exe`n")
Ok ("Installer.exe.sha256  " + $fingerprint.Substring(0, 16) + "...")
Note $fingerprintFile

Write-Host ""
# NOW IT CAN BE SAID, BECAUSE IT IS TRUE.
# This line used to say for a version «installable by anyone, usable by
# whoever already has the add-in»: the payload could not contain the operations, which
# were in a private DLL. Since they are in Oce.Solid.Agent.Core.dll,
# inside this repository, the payload carries them. The only thing needed
# on the machine of whoever installs is SOLIDWORKS, for its interops.
Write-Host "  Un file solo. Dallo a chiunque abbia SOLIDWORKS." -ForegroundColor White
Write-Host "  Prova:  .\dist\Installer.exe --aiuto" -ForegroundColor DarkGray
