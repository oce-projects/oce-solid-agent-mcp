# =============================================================================
#  The library as it was at a certain version        (entry F10)
# =============================================================================
#
#  THIS FILE IS PURE ASCII: PowerShell 5.1 reads a script without a BOM as
#  ANSI, and a crooked message reads worse than an ugly one.
#
#  WHAT IT IS FOR. A check really guards its defect only if, run
#  against the code from BEFORE the repair, it turns red. For that we need
#  that library, and rebuilding it by hand means finding the commit, making a
#  worktree, compiling, remembering to delete it.
#
#      .\Checks\library-of-the-time.ps1 0.70.0
#      -> ...\library-of-the-time\0.70.0\Oce.Solid.Agent.Core.dll
#
#      .\Bench\bench.ps1 -Library <that path> -Group cases
#
#  WHY IT WORKS, AND SINCE WHEN. In this repository **a commit is a
#  version**: `git log` on `AssemblyInfo.cs` shows one line per number, without
#  gaps. So the DLL does not need to be kept anywhere -- it is rebuilt.
#
#  THE HISTORICAL COMPARISON STARTS AT LIBRARY 0.150.2 (W3.1-bis.10b,
#  decision D11). Before it the library, its namespaces, its types, the
#  case names and some of the values the cases name had Italian names, and the bench of today reads none of them;
#  before 0.52.0 the operations lived in `OpenPDM.SwAddin`. The bench says
#  so and exits 2 instead of crashing (measured on 0.30.2). A defect guarded
#  before 0.150.2 is proven red on the library of that time with the bench
#  of that time (its own commit), not with this one.
# =============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string] $Version,
    [switch] $Clean
)

# NOT "Stop", AND IT IS NOT LAZINESS. git writes on standard error even when
# everything goes well -- «Preparing worktree (detached HEAD ...)» -- and PowerShell 5.1
# turns every stderr line of an executable into an error record. With
# "Stop" the script stops on a success message.
# The outcomes are looked at where they count: on $LASTEXITCODE, one by one.
$ErrorActionPreference = "Continue"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Where = Join-Path $env:TEMP ("library-of-the-time\" + $Version)

function Die($message) {
    Write-Output ""
    Write-Output ("COULD NOT DO IT: " + $message)
    exit 1
}

if ($Clean) {
    if (Test-Path $Where) { Remove-Item $Where -Recurse -Force }
    git -C $Root worktree prune
    Write-Output ("tolto " + $Where)
    exit 0
}

# --- the commit that DECLARES that version ----------------------------------
#
# We search inside AssemblyInfo.cs, not in the commit messages: the message is
# written by a person, the `AssemblyVersion` line is read by the compiler.
$commit = $null
$history = git -C $Root log --format=%H -- Oce.Solid.Agent.Core/Properties/AssemblyInfo.cs
foreach ($c in $history) {
    $text = git -C $Root show ($c + ":Oce.Solid.Agent.Core/Properties/AssemblyInfo.cs") 2>$null
    if ($text -match ('AssemblyVersion\("' + [regex]::Escape($Version) + '\.0"\)')) {
        $commit = $c
        break
    }
}
if ($commit -eq $null) {
    Die ("no commit declares AssemblyVersion " + $Version + ".0. " +
           "Versions can be read with:  git log --format='%h %s' -- Oce.Solid.Agent.Core/Properties/AssemblyInfo.cs")
}
Write-Output ("versione " + $Version + " -> commit " + $commit.Substring(0, 7))

# --- a throwaway worktree ---------------------------------------------------
if (Test-Path $Where) { Remove-Item $Where -Recurse -Force }
git -C $Root worktree prune
# `2>&1` because «Preparing worktree» git writes to stderr even when it
# succeeds, and letting it through fills a command that went well with red.
git -C $Root worktree add --detach $Where $commit 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Die "git worktree failed" }

# --- we compile -------------------------------------------------------------
$msbuild = $null
foreach ($p in @(
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe")) {
    if (Test-Path $p) { $msbuild = $p; break }
}
if ($msbuild -eq $null) { Die "cannot find MSBuild" }

& $msbuild (Join-Path $Where "Oce.Solid.Agent.Core\Oce.Solid.Agent.Core.csproj") `
    /p:Configuration=Release /v:m /nologo | Out-Null
if ($LASTEXITCODE -ne 0) {
    Die ("the library of that time does not compile. It is not a red run: it is a " +
           "version that cannot be rebuilt today, and that has to be said rather than worked around.")
}

$dll = Join-Path $Where "Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll"
if (-not (Test-Path $dll)) { Die ("compiled, but cannot find " + $dll) }

Write-Output ""
Write-Output $dll
Write-Output ""
Write-Output "use it like this:"
Write-Output ("  .\Bench\bench.ps1 -Library """ + $dll + """ -Group cases -SkipCompilation")
Write-Output ""
Write-Output ("and remove it like this:  .\Checks\library-of-the-time.ps1 " + $Version + " -Clean")
