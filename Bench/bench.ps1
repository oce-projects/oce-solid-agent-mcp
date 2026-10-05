# =============================================================================
#  bench.ps1 - un comando solo, e un resoconto su disco   (voce F2)
# =============================================================================
#
#  QUESTO FILE E' IN ASCII PURO, E NON E' UN VEZZO. PowerShell 5.1 legge uno
#  script senza BOM come ANSI: una virgoletta caporale o un trattino lungo
#  dentro un messaggio esce storto sullo schermo di chi lo lancia, e un
#  messaggio storto si legge peggio di uno brutto.
#
#  COSA FA, IN ORDINE, E PERCHE' QUESTO ORDINE:
#
#    1. compila la libreria e il banco, e SI FERMA se una delle due fallisce.
#       E' il difetto silenzioso che la voce F2 nominava: uno script che esce 0
#       perche' la compilazione e' fallita e nessuno ha guardato. Qui msbuild
#       viene controllato sul codice di uscita, non sul testo.
#    2. guarda se SOLIDWORKS e' agganciabile, e si ferma con un messaggio se
#       non lo e'. Senza, il banco parte e muore trenta secondi dopo con una
#       riga che parla di COM.
#    3. svuota i documenti rimasti aperti. Non e' pulizia: sei documenti di
#       troppo sono ~650 caratteri nello stato, e la prova "stato sotto i
#       16 KB" passa da 15.6k a 16.2k e FALLISCE senza che il codice sia
#       cambiato. Costa mezz'ora cercare una regressione che non c'e'.
#    4. lancia il banco e misura quanto ci mette.
#    5. scrive Bench\report\AAAAMMGG_hhmm.md e .json, piu' last.json.
#
#  PERCHE' IL RESOCONTO. "Gira solo quando qualcuno lo lancia a mano" e' meta'
#  del problema; l'altra meta' e' che NON LASCIA TRACCIA. Un numero che non sta
#  su un file non si puo' confrontare con quello di ieri, ne' mettere in un
#  README.
#
#  USCITE:
#    0  tutto verde
#    1  prove fallite
#    2  errore di banco: compilazione fallita, SOLIDWORKS non agganciabile,
#       gruppo inesistente, oppure un caso che NON SI E' POTUTO MISURARE (un
#       campo assente, un selettore che non risolve, una gemella che non c'e').
#       NON e' la stessa cosa di una prova rossa e non va contata insieme: una
#       prova rossa e' una misura, un errore di banco e' una misura MANCATA.
#    3  la scrivania non era pulita: documenti gia' aperti in SOLIDWORKS che
#       non sono del banco (voce K12). Il giro non parte affatto.
#    4  SOLIDWORKS se n'e' andato a meta' giro: tre documenti nuovi non nati
#       DI FILA (voce K17). Le prove rimaste NON sono rosse, sono saltate, e
#       il giro non e' valido. Vince sul 2 e sull'1: chi legge un 1 si mette
#       a cercare cosa ha rotto, e non ha rotto niente.
#
#      .\Bench\bench.ps1
#      .\Bench\bench.ps1 -Group viste          una famiglia sola (voce F13)
#      .\Bench\bench.ps1 -Family reanchor    solo quella cartella di casi (E19)
#      .\Bench\bench.ps1 -Slow                 anche le prove che costano minuti
#      .\Bench\bench.ps1 -Mutate                  perturba i casi (voce F6)
#      .\Bench\bench.ps1 -Regression           i riferimenti sotto modifica (F8)
#      .\Bench\bench.ps1 -SkipCompilation     il binario che c'e' gia'
# =============================================================================

param(
    [string] $Group = "",
    [string] $Family = "",
    [switch] $Slow,
    [switch] $Mutate,
    [switch] $Regression,
    [switch] $SkipCompilation,
    [switch] $NoRestart,
    [string] $Library = ""
)

$ErrorActionPreference = "Stop"

$Bench  = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $Bench

if ([string]::IsNullOrWhiteSpace($Library)) {
    $Library = Join-Path $Root "Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll"
}

function Die($code, $message) {
    Write-Output ""
    Write-Output ("BANCO NON LANCIATO: " + $message)
    exit $code
}

# --- 1. compilazione -------------------------------------------------------

if (-not $SkipCompilation) {
    $msbuild = $null
    $places = @(
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($p in $places) { if (Test-Path $p) { $msbuild = $p; break } }
    if ($msbuild -eq $null) {
        Die 2 "non trovo MSBuild. Lancia con -SkipCompilation se hai gia' i binari."
    }

    foreach ($project in @("Oce.Solid.Agent.Core\Oce.Solid.Agent.Core.csproj",
                            "Bench\Oce.Solid.Agent.Bench.csproj")) {
        $full = Join-Path $Root $project
        Write-Output ("compilo " + $project)
        & $msbuild $full /p:Configuration=Release /v:m /nologo | Out-Null
        # IL CODICE DI USCITA, NON IL TESTO. E' il controllo che la voce F2
        # chiedeva per nome.
        if ($LASTEXITCODE -ne 0) { Die 2 ("compilazione fallita: " + $project) }
    }
}

if (-not (Test-Path $Library)) {
    Die 2 ("non trovo la libreria: " + $Library)
}

# --- 2. SOLIDWORKS agganciabile? -------------------------------------------

try {
    $sw = [Runtime.InteropServices.Marshal]::GetActiveObject("SldWorks.Application")
    if ($sw -eq $null) { throw "nessuna istanza" }
} catch {
    Die 2 "SOLIDWORKS non e' agganciabile. Aprilo (una sessione sola) e rilancia."
}

# --- 3. via i documenti rimasti aperti --------------------------------------

$close = Join-Path $Root "Checks\CloseLeftovers\bin\CloseLeftovers.exe"
$openAfterCleanup = -1
if (Test-Path $close) {
    # Its last line says how many documents are still open: the count decides
    # the restart below (late-bound ISldWorks calls fail from PowerShell with
    # TYPE_E_ELEMENTNOTFOUND, measured 2/10/2026).
    # The bench's work folder is local (see Bench/Expectations.cs); the old one
    # next to the repository is still closed too, for documents of older runs.
    $benchWork = if ($env:OCE_BENCH_WORK) { $env:OCE_BENCH_WORK } else { Join-Path $env:TEMP "oce-bench-work" }
    $cleanup = & $close --chiudi $benchWork (Join-Path $Bench "lavoro") 2>&1
    $countLine = @($cleanup | Where-Object { "$_" -match ':\s*(\d+)\s*$' }) | Select-Object -Last 1
    if ($countLine -and ("$countLine" -match ':\s*(\d+)\s*$')) { $openAfterCleanup = [int]$Matches[1] }
} else {
    Write-Output "ChiudiScarti non c'e': i documenti aperti restano, e lo stato li elenca."
}

# --- 3b. a fresh SOLIDWORKS for a full run (entry Z-C24) ---------------------
# A long SOLIDWORKS session degrades without raising errors: on 1-2/10/2026 two
# full runs went red on rebuilds that never happened and on centre marks and
# dimensions that were not inserted, after ~4,900 documents (window resources
# exhausted) and after ~7 hours (process handles 2,332 -> 16,281). The same
# families on a restarted SOLIDWORKS: 314/314, 492/492, 90/90. So a full run
# starts on a fresh session, unless documents the bench did not open are still
# there (those are someone's work: never killed) or -NoRestart is given.
$fullRun = [string]::IsNullOrWhiteSpace($Group) -and [string]::IsNullOrWhiteSpace($Family)
if ($fullRun -and -not $NoRestart) {
    $openDocuments = $openAfterCleanup
    $process = Get-Process SLDWORKS -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($openDocuments -ne 0 -or $process -eq $null) {
        Write-Output ("SOLIDWORKS not restarted: " + $openDocuments + " document(s) still open (or the process was not found).")
    } else {
        $exe = $process.Path
        Write-Output ("restarting SOLIDWORKS for a fresh session (it had " + $process.HandleCount + " handles)")
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($sw)
        $sw = $null
        Stop-Process -Id $process.Id -Force
        Start-Sleep -Seconds 5
        Start-Process $exe | Out-Null
        $ready = $false
        for ($i = 0; $i -lt 48 -and -not $ready; $i++) {
            Start-Sleep -Seconds 5
            try { $sw = [Runtime.InteropServices.Marshal]::GetActiveObject("SldWorks.Application"); $ready = ($sw -ne $null) } catch { }
        }
        if (-not $ready) { Die 2 "SOLIDWORKS did not come back within 4 minutes after the restart." }
        Start-Sleep -Seconds 20   # the add-ins finish loading after the COM object appears
    }
}

# --- 4. il giro -------------------------------------------------------------

$arguments = @("--libreria", $Library)
if ($Slow) { $arguments += "--lento" }
# -Mutate non e' un giro con qualcosa in piu': e' un altro giro. Perturba i casi
# un argomento alla volta e PRETENDE che diventino rossi (voce F6). Costa un
# documento di SOLIDWORKS per mutazione: si lancia prima di una consegna.
if ($Mutate) { $arguments += "--muta" }
# -Regression: i diciannove casi dei riferimenti (voce F8). Produce TRE NUMERI
# che sommano ai casi -- reggono, si staccano e lo dicono, si staccano in
# silenzio -- e il terzo e' l'unico che conta.
if ($Regression) { $arguments += "--regressione" }
if (-not [string]::IsNullOrWhiteSpace($Group)) { $arguments += @("--gruppo", $Group) }
# -Family: dentro i casi dichiarati, solo quelle cartelle; implica il gruppo
# «casi». Parziale come -Group, e scritto nel riepilogo.
if (-not [string]::IsNullOrWhiteSpace($Family)) { $arguments += @("--famiglia", $Family) }

$executable = Join-Path $Bench "bin\Oce.Solid.Agent.Bench.exe"
if (-not (Test-Path $executable)) { Die 2 ("non trovo " + $executable) }

$start = Get-Date
# The live output goes to a LOCAL file and is copied next to the bench at the
# end. Written straight onto a network drive, one SMB hiccup («semaphore
# timeout») made Tee-Object throw and killed the whole run: on 24/9/2026 at 535
# checks and on 2/10/2026 after four hours.
$outputNextToBench = Join-Path $Bench "ultimo-giro.txt"
$output = Join-Path $env:TEMP "oce-bench-last-run.txt"
if (Test-Path $outputNextToBench) { Remove-Item $outputNextToBench -Force -ErrorAction SilentlyContinue }
# "Continue" SOLO QUI, E CON UN MOTIVO MISURATO. Con $ErrorActionPreference a
# "Stop", la PRIMA riga che il banco scrive su stderr diventa un errore
# terminante di PowerShell: lo script muore a questa riga con un
# NativeCommandError che nomina bench.ps1 e non dice niente del banco.
#
# E' successo. Il banco non partiva per un TypeInitializationException, e
# quello che si vedeva era:
#
#     Oce.Solid.Agent.Bench.exe :
#     In <la cartella del clone>\Bench\bench.ps1:138 car:1
#     + & $eseguibile $argomenti 2>&1 | Tee-Object -FilePath $uscita
#
# senza una parola sull'eccezione, e con `ultimo-giro.txt` fermo al giro
# PRECEDENTE -- che e' la cosa peggiore: si legge un resoconto vecchio
# credendolo nuovo. Adesso stderr passa, finisce nel file come tutto il
# resto, e il codice di uscita e' l'unica cosa che decide.
# VIA IL FILE DI IERI PRIMA DI LANCIARE. Tee-Object lo apre alla prima riga
# che passa: se il banco muore prima di scriverne una, il file NON viene
# toccato e resta li' con dentro il giro precedente, ora compresa. Toglierlo
# adesso e' quello che rende vero il controllo qui sotto.
if (Test-Path $output) { Remove-Item $output -Force }

$wasBefore = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& $executable $arguments 2>&1 | Tee-Object -FilePath $output
$code = $LASTEXITCODE
$ErrorActionPreference = $wasBefore
$duration = (Get-Date) - $start

# The copy next to the bench, retried: the network drive may be busy for a few
# seconds; the local file stays the one this script reads.
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try { Copy-Item $output $outputNextToBench -Force -ErrorAction Stop; break }
    catch { if ($attempt -eq 5) { Write-Warning ("could not copy the run output to " + $outputNextToBench + ": " + $_.Exception.Message + " (it stays in " + $output + ")") } else { Start-Sleep -Seconds 10 } }
}

# UN GIRO CHE NON HA SCRITTO NIENTE NON E' UN GIRO. Se il banco muore prima
# della prima riga -- una libreria che non si carica, un interop che non c'e'
# -- il file resta quello di ieri e ogni conto che segue e' una bugia.
$written = 0
if (Test-Path $output) { $written = (Get-Item $output).Length }
if ($written -lt 100) {
    Die 2 ("il banco non ha scritto niente (uscita " + $code + "). " +
             "Lancia l'eseguibile a mano per vedere l'eccezione: " + $executable)
}

# --- 5. il resoconto --------------------------------------------------------

$lines = Get-Content $output

# IL RIEPILOGO SI PRENDE DALLA RIGA CHE IL BANCO SCRIVE, non si ricalcola:
# due conti dello stesso numero, fatti in due posti, prima o poi divergono e
# allora non si sa piu' quale sia quello vero.
$fine = ($lines | Where-Object { $_ -match "^(GIRO PARZIALE|[0-9]+ prove,)" } | Select-Object -Last 1)
if ($fine -eq $null) { $fine = "(il banco non ha scritto il riepilogo)" }
# [string] E NON BASTA CHE SIA UNA STRINGA. Get-Content torna righe con
# addosso le note di PowerShell (PSPath, PSDrive, il provider...), e
# ConvertTo-Json le segue tutte: il primo .json uscito da qui pesava 498 kB
# di albero dell'unita' S: attorno a una riga di riepilogo.
$fine = [string]$fine

# LE RIGHE DI ESITO ESCONO DUE VOLTE: una mentre il giro va, una nell'elenco
# finale dopo la riga di ====. Si contano quelle dopo, altrimenti tutto
# raddoppia.
$cut = 0
for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^={40,}") { $cut = $i + 1 } }
$list = if ($cut -gt 0) { $lines[$cut..($lines.Count - 1)] } else { $lines }

$passed = @($list | Where-Object { $_ -match "^  OK   " })
$failed = @($list | Where-Object { $_ -match "^  NO   " })
$skipped = @($list | Where-Object { $_ -match "^  --   " })
# GLI ERRORI DI BANCO SI CONTANO A PARTE (voce F4): "non ho potuto misurare"
# non e' "ho misurato e non torna", e sommarli farebbe leggere una suite che
# si e' svuotata come una suite che ha trovato qualcosa.
$errors  = @($list | Where-Object { $_ -match "^  !!   " })

function Take($regex) {
    $r = ($lines | Where-Object { $_ -match $regex } | Select-Object -First 1)
    if ($r -eq $null) { return "(non dichiarato)" }
    return ([regex]::Match($r, $regex)).Groups[1].Value
}

$sw_ver  = Take "^SolidWorks (.+)$"
$lib_ver = Take "^libreria ([0-9.]+)"
# DUE NUMERI, col loro nome (voce F15): le eseguibili dalla libreria e quelle
# servite a catalogo. Il campo «catalog» del JSON resta, con lo stesso valore
# di prima (le eseguibili), per chi lo leggeva.
$executables = Take "^eseguibili: ([0-9]+) operazioni"
$inCatalog  = Take "^eseguibili: [0-9]+ operazioni \(a catalogo: ([0-9]+)\)"
# ATTESE VERIFICATE N SU M: se i due numeri divergono, qualcosa sta misurando
# il nulla. Lo scrive il banco, non si ricalcola qui.
$expectations = ($lines | Where-Object { $_ -match "^attese verificate " } | Select-Object -Last 1)
$expectations = [string]$expectations
if ([string]::IsNullOrWhiteSpace($expectations)) { $expectations = "attese verificate: (non dichiarate)" }

# I CASI VERDI, PER NOME (voce Z-C6). Il banco li scrive su una riga sola; qui
# diventano un elenco (`green_cases`) in `last.json`, che la suite di contratto incrocia col
# catalogo: ogni operazione servita deve avere un caso verde nell'ultimo giro.
# [string] per la stessa ragione di $fine: niente note di PowerShell nel JSON.
$greenLine = [string]($lines | Where-Object { $_ -match "^casi verdi: " } | Select-Object -Last 1)
$greenCases = @()
if (-not [string]::IsNullOrWhiteSpace($greenLine)) {
    $greenCases = @($greenLine.Substring("casi verdi: ".Length).Split(
        [char[]]@(' '), [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [string]$_ })
}

# THE ITALIAN NAMES THE RUN ACCEPTED (W3.1-bis.5, decision D4): the bench writes
# them on a line of their own; outside Bench/cases/aliases they must be 0.
$aliasLine = [string]($lines | Where-Object { $_ -match "^alias uses: " } | Select-Object -Last 1)
$aliasUses = $(if ([string]::IsNullOrWhiteSpace($aliasLine)) { $null } else { $aliasLine.Substring("alias uses: ".Length) })

$commit = "(non in un repository)"
try {
    Push-Location $Root
    $c = & git rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0) { $commit = $c }
    Pop-Location
} catch { }

$timestamp = Get-Date
$itemName = $timestamp.ToString("yyyyMMdd_HHmm")
$folder = Join-Path $Bench "report"
if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }

# DUE GIRI NELLO STESSO MINUTO NON SI SOVRASCRIVONO.
# Un giro con `-Group` dura secondi, quindi succede subito: il 15/9/2026 un
# resoconto verde e' stato cancellato da un giro rosso partito venti secondi
# dopo, e l'unico segno era il conteggio che cambiava. Un resoconto che sparisce
# e' peggio di un resoconto che non c'e': fa credere che quel giro sia andato
# come dice quello che l'ha rimpiazzato.
$nth = 2
while (Test-Path (Join-Path $folder ($itemName + ".md"))) {
    $itemName = $timestamp.ToString("yyyyMMdd_HHmm") + "_" + $nth
    $nth++
}

$md = New-Object System.Collections.Generic.List[string]
$md.Add("# Giro di banco - " + $timestamp.ToString("yyyy-MM-dd HH:mm"))
$md.Add("")
$md.Add("| | |")
$md.Add("|---|---|")
$md.Add("| SOLIDWORKS | " + $sw_ver + " |")
$md.Add("| libreria | " + $lib_ver + " |")
$md.Add("| operazioni | " + $executables + " eseguibili, " + $inCatalog + " a catalogo |")
$md.Add("| commit | " + $commit + " |")
$md.Add("| durata | " + [int]$duration.TotalSeconds + " s |")
if (-not [string]::IsNullOrWhiteSpace($Group)) { $md.Add("| gruppo | " + $Group + " |") }
if (-not [string]::IsNullOrWhiteSpace($Family)) { $md.Add("| famiglia | " + $Family + " |") }
$md.Add("")
$md.Add("**" + $fine + "**")
$md.Add("")
$md.Add("| | |")
$md.Add("|---|---|")
$md.Add("| passate | " + $passed.Count + " |")
$md.Add("| fallite | " + $failed.Count + " |")
$md.Add("| saltate | " + $skipped.Count + " |")
$md.Add("| errori di banco | " + $errors.Count + " |")
$md.Add("| uscita | " + $code + " |")
$md.Add("")
$md.Add("**" + $expectations + "**")
$md.Add("")
$md.Add("> Se i due numeri divergono, qualcosa sta misurando il nulla: un caso")
$md.Add("> che legge un campo che non esiste piu'. Un errore di banco non e'")
$md.Add("> una prova rossa - vuol dire che la prova NON E' STATA FATTA - e")
$md.Add("> porta il giro a uscita 2.")
$md.Add("")

if ($failed.Count -gt 0) {
    $md.Add("## Le rosse")
    $md.Add("")
    $md.Add("Il dettaglio a destra e' quello che la prova ha scritto: dove c'e'")
    $md.Add("un numero, l'atteso e il misurato stanno li'.")
    $md.Add("")
    # APICI SINGOLI: il backtick e' il carattere di escape di PowerShell, e
    # tre backtick dentro una stringa a doppi apici sono un errore di sintassi.
    $md.Add('```')
    foreach ($r in $failed) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

if ($errors.Count -gt 0) {
    $md.Add("## Gli errori di banco")
    $md.Add("")
    $md.Add("Non sono prove rosse: sono prove che NON SONO STATE FATTE.")
    $md.Add("")
    $md.Add('```')
    foreach ($r in $errors) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

if ($skipped.Count -gt 0) {
    $md.Add("## Le saltate")
    $md.Add("")
    $md.Add('```')
    foreach ($r in $skipped) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

# UTF-8 SENZA BOM. `Out-File -Encoding utf8` su PowerShell 5.1 mette il BOM,
# e i tre byte davanti al `#` fanno sembrare il titolo del markdown un
# paragrafo qualunque a chi lo rende.
$withoutBom = New-Object System.Text.UTF8Encoding($false)

$fileMd = Join-Path $folder ($itemName + ".md")
[System.IO.File]::WriteAllText($fileMd, ($md -join "`r`n") + "`r`n", $withoutBom)

$obj = [ordered]@{
    timestamp    = $timestamp.ToString("s")
    solidworks   = $sw_ver
    library      = $lib_ver
    catalog      = $(if ($executables -match "^[0-9]+$") { [int]$executables } else { $executables })
    runnable     = $(if ($executables -match "^[0-9]+$") { [int]$executables } else { $executables })
    in_catalog   = $(if ($inCatalog -match "^[0-9]+$") { [int]$inCatalog } else { $inCatalog })
    commit       = $commit
    batch        = $Group
    family       = $Family
    slow         = [bool]$Slow
    duration_s   = [int]$duration.TotalSeconds
    summary      = $fine
    expectations = $expectations
    passed       = $passed.Count
    failed       = $failed.Count
    skipped      = $skipped.Count
    errors       = $errors.Count
    exit_code    = $code
    red          = @($failed | ForEach-Object { $_.TrimEnd() })
    bench_errors = @($errors | ForEach-Object { $_.TrimEnd() })
    green_cases  = $greenCases
    alias_uses   = $aliasUses
}
$json = $obj | ConvertTo-Json -Depth 4
$fileJson = Join-Path $folder ($itemName + ".json")
[System.IO.File]::WriteAllText($fileJson, $json, $withoutBom)
[System.IO.File]::WriteAllText((Join-Path $folder "last.json"), $json, $withoutBom)

Write-Output ""
Write-Output ("resoconto: " + $fileMd)
Write-Output ("           " + $fileJson)
Write-Output ("uscita: " + $code)
exit $code
