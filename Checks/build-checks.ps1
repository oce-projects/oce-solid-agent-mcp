# Builds the bench tools that live in Checks\.
#
# They are small, stand-alone executables, outside the solution: they do not end up
# in the package and are of no use to whoever uses the bridge, they are for whoever TESTS it. That
# is why they have a script of their own instead of a .csproj: one more project in the
# solution would make them compile on every build for no reason.
#
# But a script is needed all the same: compiling them by hand with csc means that
# whoever clones the repo finds three sources they do not know how to build.
#
#   .\Checks\build-checks.ps1
#
# WARNING about the compiler. The csc in Framework64\v4.0.30319 is the OLD one
# and does not even know the ?. operator: here we use the Roslyn of the Build Tools.
#
# WHERE THE INTEROP IS: it can be overridden. SOLIDWORKS is not installed in the same
# place on every machine, and a constant path here means that
# whoever clones has to edit the script by hand to compile.
#
#   .\Checks\build-checks.ps1 -SwInstallPath 'D:\...\SOLIDWORKS'
#   or   $env:SW_INSTALL_PATH = 'D:\...\SOLIDWORKS'
#
# ENTRY H10: the variable is ONE ONLY and means THE ROOT of the installation,
# the one that ends with \SOLIDWORKS. From there api\redist is derived; the other way
# round it is not. Before, this asked for the subfolder, and whoever set the root to
# compile the library found this script that did not see it.
#
# -SwRedist and SW_REDIST are still read, afterwards: whoever already has them in their
# profile must not notice anything.

param([string]$SwInstallPath, [string]$SwRedist)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$csc = @(
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "Roslyn (csc.exe of the Build Tools) not found." }

$swRoots = @($SwInstallPath, $env:SW_INSTALL_PATH) |
    Where-Object { $_ } |
    ForEach-Object { Join-Path $_ 'api\redist' }

$interop = @(
    $swRoots +
    @(
        $SwRedist,
        $env:SW_REDIST,
        'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'
    )
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

$library = Join-Path $root 'Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll'

# name ; what it needs next to it
$tools = @(
    @{ name = 'GearTest';     sw = $false; lib = $true  },
    @{ name = 'OutlineTest';    sw = $false; lib = $true  },
    @{ name = 'PositionsTest'; sw = $false; lib = $true  },
    @{ name = 'CloseLeftovers'; sw = $true;  lib = $false },
    @{ name = 'NewPart';   sw = $true;  lib = $false },
    @{ name = 'Undo';      sw = $true;  lib = $false },
    @{ name = 'SketchOnSheet'; sw = $true; lib = $false },
    @{ name = 'SectionView'; sw = $true; lib = $false },
    @{ name = 'DrawingDimensions'; sw = $true; lib = $false },
    @{ name = 'Option'; sw = $true; lib = $false },
    @{ name = 'DimensionApis'; sw = $true; lib = $false },
    @{ name = 'RedTree'; sw = $true; lib = $false },
    @{ name = 'Select'; sw = $true; lib = $false },
    @{ name = 'RoleCoverage'; sw = $true; lib = $false },
    @{ name = 'ViewTest'; sw = $true; lib = $false },
    @{ name = 'FramingTest'; sw = $true; lib = $true; drawing = $true },
    @{ name = 'HoleOrFillet'; sw = $true; lib = $false },
    @{ name = 'BrokenBody'; sw = $true; lib = $false },
    @{ name = 'ChangeByHand'; sw = $true; lib = $false },
    @{ name = 'DetailView'; sw = $true; lib = $false },
    # The S3 bracket ALSO wants the Simulation interop: it is the only
    # probe that goes beyond sldworks+swconst.
    @{ name = 'Bracket'; sw = $true; lib = $false; sim = $true },
    # The minimum wall thickness (entry Z-H1) wants the Utilities interop,
    # which is called `gtswutilities` (the prefix is Geometric's, who wrote the
    # module). Same rule as Simulation: if it is missing, it is skipped, saying so.
    @{ name = 'Thickness'; sw = $true; lib = $false; util = $true },
    # The flat pattern of sheet metal (entry GP18): the core only.
    @{ name = 'SheetMetal'; sw = $true; lib = $false },
    # DimXpert from the core (entry Z-H3): the `swdimxpert` interop lives in the same
    # api\redist as the core and is there on every installation, but it is only referenced
    # for whoever asks for it.
    @{ name = 'DimXpert'; sw = $true; lib = $false; dimx = $true },
    # A dimension in the drawing that loses its edge (entry Z-A2).
    @{ name = 'Orphan'; sw = $true; lib = $true },
    # Does the text of a drawing come out of the PDF as text? (entry GT3-bis)
    @{ name = 'PdfText'; sw = $true; lib = $false },
    # Does the guard against the modal trigger? (entry Z-C3)
    @{ name = 'Modal'; sw = $true; lib = $false },
    # Does a hole dimensioned against the edge follow it? (entry E18, parts a and b)
    @{ name = 'FollowTheEdge'; sw = $true; lib = $false },
    # Is the edge flange born without a profile sketch? (entry GP18: no)
    @{ name = 'EdgeFlange'; sw = $true; lib = $false },
    # The four features of the guide (28/9/2026): the measurements behind
    # `fillet_face` (GP7), `fill_pattern` (W1.1), `rib` (W1.2)
    # and `gusset` (GP20). Each carries at the top the outcome of that day.
    @{ name = 'FaceFillet'; sw = $true; lib = $false },
    @{ name = 'Fill'; sw = $true; lib = $false },
    @{ name = 'Rib'; sw = $true; lib = $false },
    @{ name = 'Gusset'; sw = $true; lib = $false },
    @{ name = 'ExtrusionType'; sw = $true; lib = $false },
    @{ name = 'RibWithdraw'; sw = $true; lib = $false },
    # The four defects of the live proof of 6/10/2026 (D9 prerequisites), measured
    # before they are fixed: the cut list folders, the contact test of the gusset,
    # the empty assembly, CloseDoc by title or by path.
    @{ name = 'D9Defects'; sw = $true; lib = $false },
    # The language SOLIDWORKS writes its names in, measured next to what the API says without a
    # name: the probe under the table of the names (Localization/solidworks-names.json). It uses
    # the library for the table and for the reading of the language, and reads the material XML.
    @{ name = 'SolidWorksLanguage'; sw = $true; lib = $true; extra = @('System.Xml.Linq.dll', 'System.Xml.dll') }
    # Faces beyond the 60 largest (W3.1-bis.12): is the ranking by area stable between
    # two readings, how does it tie at the cut, what does a closest-point read cost.
    @{ name = 'FacesBeyondSixty'; sw = $true; lib = $false },
    # Dimensions from the sketch origin to every vertex of a profile (brief R5): how
    # the origin is selected, what the two dimension calls give, what a driven value does.
    @{ name = 'DrivenSketch'; sw = $true; lib = $false },
    # Does the lock of the sketch split the undo entry of a plan (brief R11, H1)? And
    # can the FIXED relations go in while the sketch is still open?
    @{ name = 'UndoSplit'; sw = $true; lib = $false },
    # What does one undo take away in an assembly (brief R23): insert, insert, mate,
    # inside the recording of the bridge and without it.
    @{ name = 'AssemblyUndo'; sw = $true; lib = $false }
)

foreach ($a in $tools) {
    $folder = Join-Path $PSScriptRoot $a.name
    $bin = Join-Path $folder 'bin'
    $source = Join-Path $folder 'Program.cs'
    if (-not (Test-Path $source)) { throw "missing $source" }
    New-Item -ItemType Directory -Force -Path $bin | Out-Null

    $refObj = @()
    if ($a.lib) {
        if (-not (Test-Path $library)) {
            throw "missing $library : build the library first (.\build.ps1)"
        }
        # The DLL is COPIED next to the executable, not just referenced:
        # without the copy the exe starts and dies with FileNotFoundException at first
        # use, which is the worst way to find out.
        Copy-Item $library $bin -Force
        $refObj += '-reference:' + (Join-Path $bin 'Oce.Solid.Agent.Core.dll')
    }
    if ($a.sw) {
        foreach ($d in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
            $p = Join-Path $interop $d
            if (-not (Test-Path $p)) { throw "missing $p" }
            Copy-Item $p $bin -Force
            $refObj += '-reference:' + (Join-Path $bin $d)
        }
    }

    # `sim = $true`: the SIMULATION interop, which lives apart.
    #
    # Only one probe uses it (Bracket, entry S3) and it is not referenced for all:
    # `cosworks.dll` weighs 467 kB and on a machine without Simulation it is not there
    # at all. Whoever does not ask for it keeps compiling there too.
    #
    # If it is missing, the probe is NOT compiled and the reason is said: a probe that
    # compiles without the interop and then dies at first use is worse.
    if ($a.sim) {
        $p = Join-Path $interop 'SolidWorks.Interop.cosworks.dll'
        if (-not (Test-Path $p)) {
            Write-Warning "$($a.name): missing $p -- Simulation is not installed here, skipping this probe"
            continue
        }
        Copy-Item $p $bin -Force
        $refObj += '-reference:' + (Join-Path $bin 'SolidWorks.Interop.cosworks.dll')
    }

    # `util = $true`: the SOLIDWORKS Utilities interop, for the same reason
    # and with the same rule as `sim`.
    if ($a.util) {
        $p = Join-Path $interop 'SolidWorks.Interop.gtswutilities.dll'
        if (-not (Test-Path $p)) {
            Write-Warning "$($a.name): missing $p -- Utilities is not installed here, skipping this probe"
            continue
        }
        Copy-Item $p $bin -Force
        $refObj += '-reference:' + (Join-Path $bin 'SolidWorks.Interop.gtswutilities.dll')
    }

    if ($a.dimx) {
        $p = Join-Path $interop 'SolidWorks.Interop.swdimxpert.dll'
        if (-not (Test-Path $p)) { throw "missing $p" }
        Copy-Item $p $bin -Force
        $refObj += '-reference:' + (Join-Path $bin 'SolidWorks.Interop.swdimxpert.dll')
    }

    # `drawing = $true` adds System.Drawing: it is for whoever looks at the PIXELS of
    # an image instead of the geometry. Roslyn does not reference it by itself, and
    # without it you get a CS0246 on `Bitmap` that does not say where it comes from.
    if ($a.drawing) { $refObj += '-reference:System.Drawing.dll' }
    # `extra`: other framework assemblies the probe names.
    if ($a.extra) { foreach ($e in $a.extra) { $refObj += ('-reference:' + $e) } }

    $output = Join-Path $bin ($a.name + '.exe')
    & $csc -nologo -langversion:7.3 -target:exe "-out:$output" @refObj $source
    if ($LASTEXITCODE -ne 0) { throw ($a.name + ' does not compile') }
    Write-Host ('  ' + $a.name + '.exe')
}

Write-Host ''
Write-Host 'Done. GearTest, OutlineTest and PositionsTest do not open SOLIDWORKS; the others (CloseLeftovers, NewPart...) do.'
