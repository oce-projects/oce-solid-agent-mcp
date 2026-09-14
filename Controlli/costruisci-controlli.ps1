# Costruisce gli attrezzi del banco che stanno in Controlli\.
#
# Sono eseguibili piccoli e a se' stanti, fuori dalla soluzione: non finiscono
# nel pacchetto e non servono a chi usa il ponte, servono a chi lo PROVA. Per
# questo hanno uno script loro invece di un .csproj: un progetto in piu' nella
# soluzione li farebbe compilare a ogni build senza motivo.
#
# Ma uno script ci vuole lo stesso: compilarli a mano con csc significa che
# chi clona il repo trova tre sorgenti che non sa come costruire.
#
#   .\Controlli\costruisci-controlli.ps1
#
# ATTENZIONE al compilatore. Il csc in Framework64\v4.0.30319 e' quello VECCHIO
# e non conosce nemmeno l'operatore ?. : qui si usa Roslyn dei Build Tools.
#
# DOVE STA L'INTEROP: si puo' scavalcare. SOLIDWORKS non e' installato nello
# stesso posto su tutte le macchine, e un percorso costante qui vuol dire che
# chi clona deve modificare lo script a mano per compilare.
#
#   .\Controlli\costruisci-controlli.ps1 -SwRedist 'D:\...\api\redist'
#   oppure   $env:SW_REDIST = 'D:\...\api\redist'
#
# E' la stessa variabile che Banco\LEGGIMI.md gia' documenta per il banco.

param([string]$SwRedist)

$ErrorActionPreference = 'Stop'
$radice = Split-Path -Parent $PSScriptRoot

$csc = @(
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "Roslyn (csc.exe dei Build Tools) non trovato." }

$interop = @(
    $SwRedist,
    $env:SW_REDIST,
    'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

$libreria = Join-Path $radice 'Riscontro.Disegno\bin\Release\Riscontro.Disegno.dll'

# nome ; cosa gli serve accanto
$attrezzi = @(
    @{ nome = 'ProvaRuota';     sw = $false; lib = $true  },
    @{ nome = 'ProvaSagoma';    sw = $false; lib = $true  },
    @{ nome = 'ProvaPosizioni'; sw = $false; lib = $true  },
    @{ nome = 'ChiudiScarti'; sw = $true;  lib = $false },
    @{ nome = 'NuovaParte';   sw = $true;  lib = $false },
    @{ nome = 'Annulla';      sw = $true;  lib = $false },
    @{ nome = 'SchizzoSulFoglio'; sw = $true; lib = $false },
    @{ nome = 'SezioneInTavola'; sw = $true; lib = $false },
    @{ nome = 'QuoteInTavola'; sw = $true; lib = $false },
    @{ nome = 'Opzione'; sw = $true; lib = $false },
    @{ nome = 'ApiQuote'; sw = $true; lib = $false },
    @{ nome = 'AlberoRosso'; sw = $true; lib = $false },
    @{ nome = 'Seleziona'; sw = $true; lib = $false },
    @{ nome = 'CoperturaRuoli'; sw = $true; lib = $false },
    @{ nome = 'ProvaVista'; sw = $true; lib = $false }
)

foreach ($a in $attrezzi) {
    $cartella = Join-Path $PSScriptRoot $a.nome
    $bin = Join-Path $cartella 'bin'
    $sorgente = Join-Path $cartella 'Programma.cs'
    if (-not (Test-Path $sorgente)) { throw "manca $sorgente" }
    New-Item -ItemType Directory -Force -Path $bin | Out-Null

    $rif = @()
    if ($a.lib) {
        if (-not (Test-Path $libreria)) {
            throw "manca $libreria : costruisci prima la libreria (.\build.ps1)"
        }
        # La DLL si COPIA accanto all'eseguibile, non si referenzia e basta:
        # senza la copia l'exe parte e muore con FileNotFoundException al primo
        # uso, che e' il modo peggiore di accorgersene.
        Copy-Item $libreria $bin -Force
        $rif += '-reference:' + (Join-Path $bin 'Riscontro.Disegno.dll')
    }
    if ($a.sw) {
        foreach ($d in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
            $p = Join-Path $interop $d
            if (-not (Test-Path $p)) { throw "manca $p" }
            Copy-Item $p $bin -Force
            $rif += '-reference:' + (Join-Path $bin $d)
        }
    }

    $uscita = Join-Path $bin ($a.nome + '.exe')
    & $csc -nologo -langversion:7.3 -target:exe "-out:$uscita" @rif $sorgente
    if ($LASTEXITCODE -ne 0) { throw ($a.nome + ' non compila') }
    Write-Host ('  ' + $a.nome + '.exe')
}

Write-Host ''
Write-Host 'Fatti. Le tre Prova* non aprono SOLIDWORKS; ChiudiScarti e NuovaParte si.'
