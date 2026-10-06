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
        Write-Host "  -SwInstallPath points to a folder that does not exist: $SwInstallPath" -ForegroundColor Red
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
    Die ("MSBuild not found. Install 'Build Tools for Visual Studio 2022' " +
           "(free) from https://visualstudio.microsoft.com/downloads/ and choose " +
           "'.NET desktop development'.")
}
Ok "found"
Note $msbuild

# -----------------------------------------------------------------------------
if ($Clean) {
    Step "Cleaning"
    foreach ($d in @("bin", "obj", "dist", "Installer\obj", "Installer\Payload")) {
        $p = Join-Path $root $d
        if (Test-Path $p) { Remove-Item $p -Recurse -Force; Note "removed $d" }
    }
    Ok "clean"
}

# -----------------------------------------------------------------------------
Step "1. The bridge"
# -----------------------------------------------------------------------------
& $msbuild (Join-Path $root "Oce.Solid.Agent.csproj") -p:Configuration=Release -v:m -nologo @location
if ($LASTEXITCODE -ne 0) { Die "building the bridge failed" }

$bridge = Join-Path $root "bin\oce-solid-agent-mcp.exe"
if (-not (Test-Path $bridge)) { Die "built without errors but oce-solid-agent-mcp.exe is not there" }
Ok ("oce-solid-agent-mcp.exe  " + [math]::Round((Get-Item $bridge).Length / 1KB) + " kB")

if ($BridgeOnly) { Step "Done"; Ok $bridge; exit 0 }

# -----------------------------------------------------------------------------
Step "1b. The catalog"
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
if ($LASTEXITCODE -ne 0) { Die "building the catalog exporter failed" }

$exporter = Join-Path $root "bin\Oce.Solid.Agent.Catalog.exe"
$catalog = Join-Path $root "Tools\catalog.json"
$beforeBytes = $null
if (Test-Path $catalog) { $beforeBytes = [IO.File]::ReadAllBytes($catalog) }

# AN EXECUTABLE THAT WRITES TO stderr IS NOT AN EXECUTABLE THAT FAILS.
#
# `Oce.Solid.Agent.Catalog.exe` prints to stderr the line «[catalog] written ...»,
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
if ($catalogOutcome -ne 0) { Die "the catalog was not written" }

$afterBytes = [IO.File]::ReadAllBytes($catalog)
if ($null -eq $beforeBytes) {
    Ok "catalog.json written ($($afterBytes.Length) bytes)"
} elseif ([Convert]::ToBase64String($beforeBytes) -eq [Convert]::ToBase64String($afterBytes)) {
    Ok "catalog.json unchanged ($($afterBytes.Length) bytes)"
} else {
    Ok "catalog.json REGENERATED: $($beforeBytes.Length) -> $($afterBytes.Length) bytes"
    Note "was behind the C#. Look at the diff before committing."
}

# The bridge reads what is next to it, not what is in Tools\.
Copy-Item $catalog (Join-Path $root "bin\catalog.json") -Force

# -----------------------------------------------------------------------------
Step "1c. The probes"
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
if ($probesOutcome -ne 0) { Die "a probe does not build: run .\Checks\build-checks.ps1 to see which" }
Ok "all the probes build"

# -----------------------------------------------------------------------------
Step "2. The payload to embed"
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
    @{ source = "bin\oce-solid-agent-mcp.exe";        target = "oce-solid-agent-mcp.exe";        required = $true  },
    @{ source = "bin\Oce.Solid.Agent.Core.dll";    target = "Oce.Solid.Agent.Core.dll";    required = $true  },
    @{ source = "bin\catalog.json";            target = "catalog.json";            required = $true  },
    @{ source = "bin\oce-solid-agent-mcp.exe.config"; target = "oce-solid-agent-mcp.exe.config"; required = $true  },
    @{ source = "LICENSE";                      target = "LICENSE";                  required = $false },
    @{ source = "NOTICE";                       target = "NOTICE";                   required = $false }
)

foreach ($f in $toBring) {
    $source = Join-Path $root $f.source
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $payload $f.target) -Force
        Note ("+ " + $f.target)
    } elseif ($f.required) {
        Die ("missing " + $f.source + ", which has to end up inside the installer")
    } else {
        Note ("- " + $f.target + " (not there, going on)")
    }
}
Ok ((Get-ChildItem $payload).Count.ToString() + " files to embed")

# -----------------------------------------------------------------------------
Step "3. The installer"
# -----------------------------------------------------------------------------
& $msbuild (Join-Path $root "Installer\Installer.csproj") -p:Configuration=Release -v:m -nologo @location
if ($LASTEXITCODE -ne 0) { Die "building the installer failed" }

$exe = Join-Path $root "bin\Installer.exe"
if (-not (Test-Path $exe)) { Die "built without errors but Installer.exe is not there" }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$final = Join-Path $dist "Installer.exe"
Copy-Item $exe $final -Force

# -----------------------------------------------------------------------------
Step "Check"
# -----------------------------------------------------------------------------
# A package smaller than the bridge it should contain is a package
# that does not contain it. It is a coarse check and it catches the case that matters:
# the Payload folder forgotten or empty.
$bigBridge = (Get-Item $bridge).Length
$bigExe = (Get-Item $final).Length
if ($bigExe -le $bigBridge) {
    Die ("Installer.exe is " + [math]::Round($bigExe / 1KB) + " kB but the bridge " +
           "alone takes " + [math]::Round($bigBridge / 1KB) + ": the payload was not " +
           "embedded. Check Installer\Payload\ and the " +
           "EmbeddedResource element in the csproj.")
}
Ok ("Installer.exe  " + [math]::Round($bigExe / 1KB) + " kB  (bridge embedded)")
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
Write-Host "  A single file. Give it to anyone who has SOLIDWORKS." -ForegroundColor White
Write-Host "  Try:  .\dist\Installer.exe --help" -ForegroundColor DarkGray
