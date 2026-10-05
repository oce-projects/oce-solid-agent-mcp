# Costruisce gli attrezzi del banco che stanno in Checks\.
#
# Sono eseguibili piccoli e a se' stanti, fuori dalla soluzione: non finiscono
# nel pacchetto e non servono a chi usa il ponte, servono a chi lo PROVA. Per
# questo hanno uno script loro invece di un .csproj: un progetto in piu' nella
# soluzione li farebbe compilare a ogni build senza motivo.
#
# Ma uno script ci vuole lo stesso: compilarli a mano con csc significa che
# chi clona il repo trova tre sorgenti che non sa come costruire.
#
#   .\Checks\build-checks.ps1
#
# ATTENZIONE al compilatore. Il csc in Framework64\v4.0.30319 e' quello VECCHIO
# e non conosce nemmeno l'operatore ?. : qui si usa Roslyn dei Build Tools.
#
# DOVE STA L'INTEROP: si puo' scavalcare. SOLIDWORKS non e' installato nello
# stesso posto su tutte le macchine, e un percorso costante qui vuol dire che
# chi clona deve modificare lo script a mano per compilare.
#
#   .\Checks\build-checks.ps1 -SwInstallPath 'D:\...\SOLIDWORKS'
#   oppure   $env:SW_INSTALL_PATH = 'D:\...\SOLIDWORKS'
#
# VOCE H10: la variabile e' UNA SOLA e vuol dire LA RADICE dell'installazione,
# quella che finisce con \SOLIDWORKS. Da li' si ricava api\redist; al contrario
# no. Prima qui si chiedeva gia' la sottocartella, e chi impostava la radice per
# compilare la libreria si trovava questo script che non la vedeva.
#
# -SwRedist e SW_REDIST restano letti, dopo: chi ce li ha gia' nel proprio
# profilo non deve accorgersi di niente.

param([string]$SwInstallPath, [string]$SwRedist)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$csc = @(
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "Roslyn (csc.exe dei Build Tools) non trovato." }

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

# nome ; cosa gli serve accanto
$tools = @(
    @{ nome = 'GearTest';     sw = $false; lib = $true  },
    @{ nome = 'OutlineTest';    sw = $false; lib = $true  },
    @{ nome = 'PositionsTest'; sw = $false; lib = $true  },
    @{ nome = 'CloseLeftovers'; sw = $true;  lib = $false },
    @{ nome = 'NewPart';   sw = $true;  lib = $false },
    @{ nome = 'Undo';      sw = $true;  lib = $false },
    @{ nome = 'SketchOnSheet'; sw = $true; lib = $false },
    @{ nome = 'SectionView'; sw = $true; lib = $false },
    @{ nome = 'DrawingDimensions'; sw = $true; lib = $false },
    @{ nome = 'Option'; sw = $true; lib = $false },
    @{ nome = 'DimensionApis'; sw = $true; lib = $false },
    @{ nome = 'RedTree'; sw = $true; lib = $false },
    @{ nome = 'Select'; sw = $true; lib = $false },
    @{ nome = 'RoleCoverage'; sw = $true; lib = $false },
    @{ nome = 'ViewTest'; sw = $true; lib = $false },
    @{ nome = 'FramingTest'; sw = $true; lib = $true; disegno = $true },
    @{ nome = 'HoleOrFillet'; sw = $true; lib = $false },
    @{ nome = 'BrokenBody'; sw = $true; lib = $false },
    @{ nome = 'ChangeByHand'; sw = $true; lib = $false },
    @{ nome = 'DetailView'; sw = $true; lib = $false },
    # La mensola di S3 vuole ANCHE l'interop di Simulation: e' l'unica
    # sonda che esce da sldworks+swconst.
    @{ nome = 'Bracket'; sw = $true; lib = $false; sim = $true },
    # Lo spessore minimo di parete (voce Z-H1) vuole l'interop di Utilities,
    # che si chiama `gtswutilities` (il prefisso e' di Geometric, che scrisse
    # il modulo). Stessa regola di Simulation: se manca, si salta dicendolo.
    @{ nome = 'Thickness'; sw = $true; lib = $false; util = $true },
    # Lo sviluppo della lamiera (voce GP18): solo il nucleo.
    @{ nome = 'SheetMetal'; sw = $true; lib = $false },
    # DimXpert dal nucleo (voce Z-H3): l'interop `swdimxpert` sta nello stesso
    # api\redist del nucleo e c'e' su ogni installazione, ma si referenzia solo
    # per chi la chiede.
    @{ nome = 'DimXpert'; sw = $true; lib = $false; dimx = $true },
    # Una quota in tavola che perde il suo spigolo (voce Z-A2).
    @{ nome = 'Orphan'; sw = $true; lib = $false },
    # Il testo di una tavola esce dal PDF come testo? (voce GT3-bis)
    @{ nome = 'PdfText'; sw = $true; lib = $false },
    # La guardia contro il modale scatta? (voce Z-C3)
    @{ nome = 'Modal'; sw = $true; lib = $false },
    # Un foro quotato contro lo spigolo lo segue? (voce E18, pezzi a e b)
    @{ nome = 'FollowTheEdge'; sw = $true; lib = $false },
    # La flangia a spigolo nasce senza schizzo di profilo? (voce GP18: no)
    @{ nome = 'EdgeFlange'; sw = $true; lib = $false },
    # Le quattro lavorazioni della guida (28/9/2026): le misure dietro
    # `fillet_face` (GP7), `fill_pattern` (W1.1), `rib` (W1.2)
    # e `gusset` (GP20). Ognuna porta in testa l'esito di quel giorno.
    @{ nome = 'FaceFillet'; sw = $true; lib = $false },
    @{ nome = 'Fill'; sw = $true; lib = $false },
    @{ nome = 'Rib'; sw = $true; lib = $false },
    @{ nome = 'Gusset'; sw = $true; lib = $false },
    @{ nome = 'ExtrusionType'; sw = $true; lib = $false },
    @{ nome = 'RibWithdraw'; sw = $true; lib = $false }
)

foreach ($a in $tools) {
    $folder = Join-Path $PSScriptRoot $a.nome
    $bin = Join-Path $folder 'bin'
    $source = Join-Path $folder 'Program.cs'
    if (-not (Test-Path $source)) { throw "manca $source" }
    New-Item -ItemType Directory -Force -Path $bin | Out-Null

    $refObj = @()
    if ($a.lib) {
        if (-not (Test-Path $library)) {
            throw "manca $library : costruisci prima la libreria (.\build.ps1)"
        }
        # La DLL si COPIA accanto all'eseguibile, non si referenzia e basta:
        # senza la copia l'exe parte e muore con FileNotFoundException al primo
        # uso, che e' il modo peggiore di accorgersene.
        Copy-Item $library $bin -Force
        $refObj += '-reference:' + (Join-Path $bin 'Oce.Solid.Agent.Core.dll')
    }
    if ($a.sw) {
        foreach ($d in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
            $p = Join-Path $interop $d
            if (-not (Test-Path $p)) { throw "manca $p" }
            Copy-Item $p $bin -Force
            $refObj += '-reference:' + (Join-Path $bin $d)
        }
    }

    # `sim = $true`: l'interop di SIMULATION, che sta a parte.
    #
    # Una sola sonda lo usa (Mensola, voce S3) e non si referenzia per tutte:
    # `cosworks.dll` pesa 467 kB e su una macchina senza Simulation non c'e'
    # affatto. Chi non lo chiede continua a compilare anche li'.
    #
    # Se manca, la sonda NON si compila e si dice perche': una sonda che
    # compila senza l'interop e poi muore al primo uso e' peggio.
    if ($a.sim) {
        $p = Join-Path $interop 'SolidWorks.Interop.cosworks.dll'
        if (-not (Test-Path $p)) {
            Write-Warning "$($a.nome): manca $p -- Simulation non e' installato qui, salto questa sonda"
            continue
        }
        Copy-Item $p $bin -Force
        $refObj += '-reference:' + (Join-Path $bin 'SolidWorks.Interop.cosworks.dll')
    }

    # `util = $true`: l'interop di SOLIDWORKS Utilities, per la stessa ragione
    # e con la stessa regola di `sim`.
    if ($a.util) {
        $p = Join-Path $interop 'SolidWorks.Interop.gtswutilities.dll'
        if (-not (Test-Path $p)) {
            Write-Warning "$($a.nome): manca $p -- Utilities non e' installato qui, salto questa sonda"
            continue
        }
        Copy-Item $p $bin -Force
        $refObj += '-reference:' + (Join-Path $bin 'SolidWorks.Interop.gtswutilities.dll')
    }

    if ($a.dimx) {
        $p = Join-Path $interop 'SolidWorks.Interop.swdimxpert.dll'
        if (-not (Test-Path $p)) { throw "manca $p" }
        Copy-Item $p $bin -Force
        $refObj += '-reference:' + (Join-Path $bin 'SolidWorks.Interop.swdimxpert.dll')
    }

    # `disegno = $true` aggiunge System.Drawing: serve a chi guarda i PIXEL di
    # un'immagine invece della geometria. Roslyn non lo referenzia da solo, e
    # senza si prende un CS0246 su `Bitmap` che non dice da dove viene.
    if ($a.disegno) { $refObj += '-reference:System.Drawing.dll' }

    $output = Join-Path $bin ($a.nome + '.exe')
    & $csc -nologo -langversion:7.3 -target:exe "-out:$output" @refObj $source
    if ($LASTEXITCODE -ne 0) { throw ($a.nome + ' non compila') }
    Write-Host ('  ' + $a.nome + '.exe')
}

Write-Host ''
Write-Host 'Fatti. Le tre Prova* non aprono SOLIDWORKS; ChiudiScarti e NewPart si.'
