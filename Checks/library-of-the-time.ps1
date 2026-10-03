# =============================================================================
#  La libreria com'era a una certa versione        (voce F10)
# =============================================================================
#
#  QUESTO FILE E' IN ASCII PURO: PowerShell 5.1 legge uno script senza BOM come
#  ANSI, e un messaggio storto si legge peggio di uno brutto.
#
#  A COSA SERVE. Una prova custodisce davvero il suo difetto solo se, lanciata
#  contro il codice di PRIMA della riparazione, diventa rossa. Per farlo serve
#  quella libreria, e ricompilarla a mano vuol dire cercare il commit, fare un
#  worktree, compilare, ricordarsi di cancellarlo.
#
#      .\Checks\library-of-the-time.ps1 0.70.0
#      -> ...\libreria-di-allora\0.70.0\Oce.Solid.Agent.Core.dll
#
#      .\Bench\bench.ps1 -Libreria <quel percorso> -Gruppo casi
#
#  PERCHE' FUNZIONA, E DA QUANDO. In questo repository **un commit e' una
#  versione**: `git log` su `AssemblyInfo.cs` mostra una riga per numero, senza
#  salti. La DLL non va quindi conservata da nessuna parte -- si ricostruisce.
#
#  THE HISTORICAL COMPARISON STARTS AT LIBRARY 0.150.1 (rename window 3,
#  decision D11). Before it the library, its namespaces, its types and the
#  case names had Italian names, and the bench of today reads none of them;
#  before 0.52.0 the operations lived in `OpenPDM.SwAddin`. The bench says
#  so and exits 2 instead of crashing (measured on 0.30.2). A defect guarded
#  before 0.150.1 is proven red on the library of that time with the bench
#  of that time (its own commit), not with this one.
# =============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string] $Versione,
    [switch] $Pulisci
)

# NON "Stop", E NON E' PIGRIZIA. git scrive sullo standard error anche quando
# tutto va bene -- «Preparing worktree (detached HEAD ...)» -- e PowerShell 5.1
# trasforma ogni riga di stderr di un eseguibile in un record d'errore. Con
# "Stop" lo script si ferma su un messaggio di successo.
# Gli esiti si guardano dove contano: su $LASTEXITCODE, uno per uno.
$ErrorActionPreference = "Continue"

$Radice = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Dove = Join-Path $env:TEMP ("libreria-di-allora\" + $Versione)

function Muori($messaggio) {
    Write-Output ""
    Write-Output ("NON CI SONO RIUSCITO: " + $messaggio)
    exit 1
}

if ($Pulisci) {
    if (Test-Path $Dove) { Remove-Item $Dove -Recurse -Force }
    git -C $Radice worktree prune
    Write-Output ("tolto " + $Dove)
    exit 0
}

# --- il commit che DICHIARA quella versione ---------------------------------
#
# Si cerca dentro AssemblyInfo.cs, non nei messaggi di commit: il messaggio lo
# scrive una persona, la riga `AssemblyVersion` la legge il compilatore.
$commit = $null
$storia = git -C $Radice log --format=%H -- Oce.Solid.Agent.Core/Properties/AssemblyInfo.cs
foreach ($c in $storia) {
    $testo = git -C $Radice show ($c + ":Oce.Solid.Agent.Core/Properties/AssemblyInfo.cs") 2>$null
    if ($testo -match ('AssemblyVersion\("' + [regex]::Escape($Versione) + '\.0"\)')) {
        $commit = $c
        break
    }
}
if ($commit -eq $null) {
    Muori ("nessun commit dichiara AssemblyVersion " + $Versione + ".0. " +
           "Le versioni si leggono con:  git log --format='%h %s' -- Oce.Solid.Agent.Core/Properties/AssemblyInfo.cs")
}
Write-Output ("versione " + $Versione + " -> commit " + $commit.Substring(0, 7))

# --- un worktree usa e getta ------------------------------------------------
if (Test-Path $Dove) { Remove-Item $Dove -Recurse -Force }
git -C $Radice worktree prune
# `2>&1` perche' «Preparing worktree» git lo scrive su stderr anche quando
# riesce, e lasciarlo passare riempie di rosso un comando andato bene.
git -C $Radice worktree add --detach $Dove $commit 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Muori "git worktree non e' riuscito" }

# --- si compila -------------------------------------------------------------
$msbuild = $null
foreach ($p in @(
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe")) {
    if (Test-Path $p) { $msbuild = $p; break }
}
if ($msbuild -eq $null) { Muori "non trovo MSBuild" }

& $msbuild (Join-Path $Dove "Oce.Solid.Agent.Core\Oce.Solid.Agent.Core.csproj") `
    /p:Configuration=Release /v:m /nologo | Out-Null
if ($LASTEXITCODE -ne 0) {
    Muori ("la libreria di allora non compila. Non e' un giro rosso: e' una " +
           "versione che oggi non si ricostruisce, e va detto invece di girarci intorno.")
}

$dll = Join-Path $Dove "Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll"
if (-not (Test-Path $dll)) { Muori ("compilata, ma non trovo " + $dll) }

Write-Output ""
Write-Output $dll
Write-Output ""
Write-Output "si usa cosi':"
Write-Output ("  .\Bench\bench.ps1 -Libreria """ + $dll + """ -Gruppo casi -SaltaCompilazione")
Write-Output ""
Write-Output ("e si toglie cosi':  .\Checks\library-of-the-time.ps1 " + $Versione + " -Pulisci")
