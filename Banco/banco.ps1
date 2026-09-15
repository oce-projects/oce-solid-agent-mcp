# =============================================================================
#  banco.ps1 - un comando solo, e un resoconto su disco   (voce F2)
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
#    5. scrive Banco\resoconto\AAAAMMGG_hhmm.md e .json, piu' ultimo.json.
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
#
#      .\Banco\banco.ps1
#      .\Banco\banco.ps1 -Gruppo viste          una famiglia sola (voce F13)
#      .\Banco\banco.ps1 -Lento                 anche le prove che costano minuti
#      .\Banco\banco.ps1 -Muta                  perturba i casi (voce F6)
#      .\Banco\banco.ps1 -Regressione           i riferimenti sotto modifica (F8)
#      .\Banco\banco.ps1 -SaltaCompilazione     il binario che c'e' gia'
# =============================================================================

param(
    [string] $Gruppo = "",
    [switch] $Lento,
    [switch] $Muta,
    [switch] $Regressione,
    [switch] $SaltaCompilazione,
    [string] $Libreria = ""
)

$ErrorActionPreference = "Stop"

$Banco  = Split-Path -Parent $MyInvocation.MyCommand.Path
$Radice = Split-Path -Parent $Banco

if ([string]::IsNullOrWhiteSpace($Libreria)) {
    $Libreria = Join-Path $Radice "Riscontro.Disegno\bin\Release\Riscontro.Disegno.dll"
}

function Muori($codice, $messaggio) {
    Write-Output ""
    Write-Output ("BANCO NON LANCIATO: " + $messaggio)
    exit $codice
}

# --- 1. compilazione -------------------------------------------------------

if (-not $SaltaCompilazione) {
    $msbuild = $null
    $posti = @(
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($p in $posti) { if (Test-Path $p) { $msbuild = $p; break } }
    if ($msbuild -eq $null) {
        Muori 2 "non trovo MSBuild. Lancia con -SaltaCompilazione se hai gia' i binari."
    }

    foreach ($progetto in @("Riscontro.Disegno\Riscontro.Disegno.csproj",
                            "Banco\ProvaSaldatura.csproj")) {
        $pieno = Join-Path $Radice $progetto
        Write-Output ("compilo " + $progetto)
        & $msbuild $pieno /p:Configuration=Release /v:m /nologo | Out-Null
        # IL CODICE DI USCITA, NON IL TESTO. E' il controllo che la voce F2
        # chiedeva per nome.
        if ($LASTEXITCODE -ne 0) { Muori 2 ("compilazione fallita: " + $progetto) }
    }
}

if (-not (Test-Path $Libreria)) {
    Muori 2 ("non trovo la libreria: " + $Libreria)
}

# --- 2. SOLIDWORKS agganciabile? -------------------------------------------

try {
    $sw = [Runtime.InteropServices.Marshal]::GetActiveObject("SldWorks.Application")
    if ($sw -eq $null) { throw "nessuna istanza" }
} catch {
    Muori 2 "SOLIDWORKS non e' agganciabile. Aprilo (una sessione sola) e rilancia."
}

# --- 3. via i documenti rimasti aperti --------------------------------------

$chiudi = Join-Path $Radice "Controlli\ChiudiScarti\bin\ChiudiScarti.exe"
if (Test-Path $chiudi) {
    & $chiudi --chiudi (Join-Path $Banco "lavoro") | Out-Null
} else {
    Write-Output "ChiudiScarti non c'e': i documenti aperti restano, e lo stato li elenca."
}

# --- 4. il giro -------------------------------------------------------------

$argomenti = @("--libreria", $Libreria)
if ($Lento) { $argomenti += "--lento" }
# -Muta non e' un giro con qualcosa in piu': e' un altro giro. Perturba i casi
# un argomento alla volta e PRETENDE che diventino rossi (voce F6). Costa un
# documento di SOLIDWORKS per mutazione: si lancia prima di una consegna.
if ($Muta) { $argomenti += "--muta" }
# -Regressione: i diciotto casi dei riferimenti (voce F8). Produce TRE NUMERI
# che sommano ai casi -- reggono, si staccano e lo dicono, si staccano in
# silenzio -- e il terzo e' l'unico che conta.
if ($Regressione) { $argomenti += "--regressione" }
if (-not [string]::IsNullOrWhiteSpace($Gruppo)) { $argomenti += @("--gruppo", $Gruppo) }

$eseguibile = Join-Path $Banco "bin\ProvaSaldatura.exe"
if (-not (Test-Path $eseguibile)) { Muori 2 ("non trovo " + $eseguibile) }

$inizio = Get-Date
$uscita = Join-Path $Banco "ultimo-giro.txt"
& $eseguibile $argomenti 2>&1 | Tee-Object -FilePath $uscita
$codice = $LASTEXITCODE
$durata = (Get-Date) - $inizio

# --- 5. il resoconto --------------------------------------------------------

$righe = Get-Content $uscita

# IL RIEPILOGO SI PRENDE DALLA RIGA CHE IL BANCO SCRIVE, non si ricalcola:
# due conti dello stesso numero, fatti in due posti, prima o poi divergono e
# allora non si sa piu' quale sia quello vero.
$fine = ($righe | Where-Object { $_ -match "^(GIRO PARZIALE|[0-9]+ prove,)" } | Select-Object -Last 1)
if ($fine -eq $null) { $fine = "(il banco non ha scritto il riepilogo)" }
# [string] E NON BASTA CHE SIA UNA STRINGA. Get-Content torna righe con
# addosso le note di PowerShell (PSPath, PSDrive, il provider...), e
# ConvertTo-Json le segue tutte: il primo .json uscito da qui pesava 498 kB
# di albero dell'unita' S: attorno a una riga di riepilogo.
$fine = [string]$fine

# LE RIGHE DI ESITO ESCONO DUE VOLTE: una mentre il giro va, una nell'elenco
# finale dopo la riga di ====. Si contano quelle dopo, altrimenti tutto
# raddoppia.
$taglio = 0
for ($i = 0; $i -lt $righe.Count; $i++) { if ($righe[$i] -match "^={40,}") { $taglio = $i + 1 } }
$elenco = if ($taglio -gt 0) { $righe[$taglio..($righe.Count - 1)] } else { $righe }

$passate = @($elenco | Where-Object { $_ -match "^  OK   " })
$fallite = @($elenco | Where-Object { $_ -match "^  NO   " })
$saltate = @($elenco | Where-Object { $_ -match "^  --   " })
# GLI ERRORI DI BANCO SI CONTANO A PARTE (voce F4): "non ho potuto misurare"
# non e' "ho misurato e non torna", e sommarli farebbe leggere una suite che
# si e' svuotata come una suite che ha trovato qualcosa.
$errori  = @($elenco | Where-Object { $_ -match "^  !!   " })

function Prendi($regex) {
    $r = ($righe | Where-Object { $_ -match $regex } | Select-Object -First 1)
    if ($r -eq $null) { return "(non dichiarato)" }
    return ([regex]::Match($r, $regex)).Groups[1].Value
}

$sw_ver  = Prendi "^SolidWorks (.+)$"
$lib_ver = Prendi "^libreria ([0-9.]+)"
$catalogo = Prendi "^catalogo: ([0-9]+) operazioni"
# ATTESE VERIFICATE N SU M: se i due numeri divergono, qualcosa sta misurando
# il nulla. Lo scrive il banco, non si ricalcola qui.
$attese = ($righe | Where-Object { $_ -match "^attese verificate " } | Select-Object -Last 1)
$attese = [string]$attese
if ([string]::IsNullOrWhiteSpace($attese)) { $attese = "attese verificate: (non dichiarate)" }

$commit = "(non in un repository)"
try {
    Push-Location $Radice
    $c = & git rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0) { $commit = $c }
    Pop-Location
} catch { }

$quando = Get-Date
$nome = $quando.ToString("yyyyMMdd_HHmm")
$cartella = Join-Path $Banco "resoconto"
if (-not (Test-Path $cartella)) { New-Item -ItemType Directory -Path $cartella | Out-Null }

# DUE GIRI NELLO STESSO MINUTO NON SI SOVRASCRIVONO.
# Un giro con `-Gruppo` dura secondi, quindi succede subito: il 15/9/2026 un
# resoconto verde e' stato cancellato da un giro rosso partito venti secondi
# dopo, e l'unico segno era il conteggio che cambiava. Un resoconto che sparisce
# e' peggio di un resoconto che non c'e': fa credere che quel giro sia andato
# come dice quello che l'ha rimpiazzato.
$ennesimo = 2
while (Test-Path (Join-Path $cartella ($nome + ".md"))) {
    $nome = $quando.ToString("yyyyMMdd_HHmm") + "_" + $ennesimo
    $ennesimo++
}

$md = New-Object System.Collections.Generic.List[string]
$md.Add("# Giro di banco - " + $quando.ToString("yyyy-MM-dd HH:mm"))
$md.Add("")
$md.Add("| | |")
$md.Add("|---|---|")
$md.Add("| SOLIDWORKS | " + $sw_ver + " |")
$md.Add("| libreria | " + $lib_ver + " |")
$md.Add("| catalogo | " + $catalogo + " operazioni |")
$md.Add("| commit | " + $commit + " |")
$md.Add("| durata | " + [int]$durata.TotalSeconds + " s |")
if (-not [string]::IsNullOrWhiteSpace($Gruppo)) { $md.Add("| gruppo | " + $Gruppo + " |") }
$md.Add("")
$md.Add("**" + $fine + "**")
$md.Add("")
$md.Add("| | |")
$md.Add("|---|---|")
$md.Add("| passate | " + $passate.Count + " |")
$md.Add("| fallite | " + $fallite.Count + " |")
$md.Add("| saltate | " + $saltate.Count + " |")
$md.Add("| errori di banco | " + $errori.Count + " |")
$md.Add("| uscita | " + $codice + " |")
$md.Add("")
$md.Add("**" + $attese + "**")
$md.Add("")
$md.Add("> Se i due numeri divergono, qualcosa sta misurando il nulla: un caso")
$md.Add("> che legge un campo che non esiste piu'. Un errore di banco non e'")
$md.Add("> una prova rossa - vuol dire che la prova NON E' STATA FATTA - e")
$md.Add("> porta il giro a uscita 2.")
$md.Add("")

if ($fallite.Count -gt 0) {
    $md.Add("## Le rosse")
    $md.Add("")
    $md.Add("Il dettaglio a destra e' quello che la prova ha scritto: dove c'e'")
    $md.Add("un numero, l'atteso e il misurato stanno li'.")
    $md.Add("")
    # APICI SINGOLI: il backtick e' il carattere di escape di PowerShell, e
    # tre backtick dentro una stringa a doppi apici sono un errore di sintassi.
    $md.Add('```')
    foreach ($r in $fallite) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

if ($errori.Count -gt 0) {
    $md.Add("## Gli errori di banco")
    $md.Add("")
    $md.Add("Non sono prove rosse: sono prove che NON SONO STATE FATTE.")
    $md.Add("")
    $md.Add('```')
    foreach ($r in $errori) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

if ($saltate.Count -gt 0) {
    $md.Add("## Le saltate")
    $md.Add("")
    $md.Add('```')
    foreach ($r in $saltate) { $md.Add($r.TrimEnd()) }
    $md.Add('```')
    $md.Add("")
}

# UTF-8 SENZA BOM. `Out-File -Encoding utf8` su PowerShell 5.1 mette il BOM,
# e i tre byte davanti al `#` fanno sembrare il titolo del markdown un
# paragrafo qualunque a chi lo rende.
$senzaBom = New-Object System.Text.UTF8Encoding($false)

$fileMd = Join-Path $cartella ($nome + ".md")
[System.IO.File]::WriteAllText($fileMd, ($md -join "`r`n") + "`r`n", $senzaBom)

$oggetto = [ordered]@{
    quando     = $quando.ToString("s")
    solidworks = $sw_ver
    libreria   = $lib_ver
    catalogo   = $(if ($catalogo -match "^[0-9]+$") { [int]$catalogo } else { $catalogo })
    commit     = $commit
    gruppo     = $Gruppo
    lento      = [bool]$Lento
    durata_s   = [int]$durata.TotalSeconds
    riepilogo  = $fine
    attese     = $attese
    passate    = $passate.Count
    fallite    = $fallite.Count
    saltate    = $saltate.Count
    errori     = $errori.Count
    uscita     = $codice
    rosse      = @($fallite | ForEach-Object { $_.TrimEnd() })
    di_banco   = @($errori | ForEach-Object { $_.TrimEnd() })
}
$json = $oggetto | ConvertTo-Json -Depth 4
$fileJson = Join-Path $cartella ($nome + ".json")
[System.IO.File]::WriteAllText($fileJson, $json, $senzaBom)
[System.IO.File]::WriteAllText((Join-Path $cartella "ultimo.json"), $json, $senzaBom)

Write-Output ""
Write-Output ("resoconto: " + $fileMd)
Write-Output ("           " + $fileJson)
Write-Output ("uscita: " + $codice)
exit $codice
