# =============================================================================
#  Il catalogo dichiara operazioni che l'esecutore sa fare?
# =============================================================================
#
#  Esce 0 se tutto torna, 1 se no. E' pensato per essere lanciato prima di una
#  consegna e da una build automatica.
#
#  PERCHE' ESISTE. Il catalogo (`catalogo.json`) e' l'elenco di cio' che il
#  modello puo' chiedere; lo switch di `OperazioniSw.Esegui` e' l'elenco di cio'
#  che si puo' davvero fare. Sono due posti, e due posti divergono sempre. Oggi
#  divergono di tre nomi, e quei tre nomi il modello puo' chiederli: si prende
#  «operazione sconosciuta» dopo aver scritto un piano intero.
#
#  UN CONTROLLO NON DEVE POTER USCIRE 0 PER NON AVER GUARDATO.
#  E' il difetto che questo controllo rischiava piu' di tutti: leggere i `case`
#  con una regex su 4.700 righe, non trovarne nessuno, e dichiarare «nessun
#  disallineamento». Qui ci si difende in tre modi:
#    1. se il catalogo ha meno di 20 operazioni si esce 1 dicendo che non e'
#       stato letto, invece di confrontare due insiemi vuoti;
#    2. se dal sorgente si leggono meno di 20 `case`, idem;
#    3. l'elenco dichiarato dalla libreria (`OperazioniSw.NomiEseguibili`) e
#       quello letto dal sorgente devono COMBACIARE fra loro: sono due strade
#       indipendenti verso lo stesso fatto, e se divergono e' uno dei due a
#       essere marcio.
#
# =============================================================================

[CmdletBinding()]
param(
    [string]$Catalogo,
    [string]$Libreria,
    [string]$Sorgente
)

$ErrorActionPreference = "Stop"
$radice = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)

if (-not $Catalogo) { $Catalogo = Join-Path $radice "bin\catalogo.json" }
if (-not $Libreria) { $Libreria = Join-Path $radice "Riscontro.Disegno\bin\Release\Riscontro.Disegno.dll" }
if (-not $Sorgente) { $Sorgente = Join-Path $radice "Riscontro.Disegno\OperazioniSw.cs" }

$problemi = New-Object System.Collections.Generic.List[string]
function Male($m) { $problemi.Add($m) | Out-Null; Write-Host "  [X] $m" -ForegroundColor Red }
function Bene($m) { Write-Host "  [ok] $m" -ForegroundColor Green }
function Nota($m) { Write-Host "       $m" -ForegroundColor DarkGray }

Write-Host ""
Write-Host "=== Il catalogo contro l'esecutore" -ForegroundColor Cyan

# --- 1. il catalogo ----------------------------------------------------------
if (-not (Test-Path -LiteralPath $Catalogo)) {
    Write-Host "  [X] non trovo il catalogo: $Catalogo" -ForegroundColor Red
    Write-Host ""
    Write-Host "1 problema: il catalogo non c'e'. Compila con build.ps1." -ForegroundColor Red
    exit 1
}
$cat = Get-Content -LiteralPath $Catalogo -Raw -Encoding UTF8 | ConvertFrom-Json
$operazioni = @($cat.operazioni)
$nomiCatalogo = @($operazioni | ForEach-Object { $_.nome } | Sort-Object -Unique)

if ($nomiCatalogo.Count -lt 20) {
    Write-Host ("  [X] il catalogo ha solo " + $nomiCatalogo.Count + " operazioni: " +
                "non l'ho letto bene, e confrontare due insiemi vuoti direbbe " +
                "che va tutto bene.") -ForegroundColor Red
    exit 1
}
Bene ("catalogo: " + $nomiCatalogo.Count + " operazioni")

# --- 2. cio' che l'esecutore DICHIARA di saper fare --------------------------
if (-not (Test-Path -LiteralPath $Libreria)) {
    Write-Host "  [X] non trovo la libreria: $Libreria" -ForegroundColor Red
    exit 1
}
# Load sui byte e non LoadFrom: la libreria puo' stare su un'unita' di rete,
# dove LoadFrom fallisce con 0x80131515 nominando una dipendenza che non esiste.
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Libreria))
$tipo = $asm.GetType("Riscontro.Disegno.OperazioniSw")
if (-not $tipo) {
    Write-Host "  [X] nella libreria non c'e' Riscontro.Disegno.OperazioniSw" -ForegroundColor Red
    exit 1
}
$campo = $tipo.GetField("NomiEseguibili")
if (-not $campo) {
    Write-Host ("  [X] OperazioniSw non espone NomiEseguibili: senza, questo " +
                "controllo dovrebbe indovinare.") -ForegroundColor Red
    exit 1
}
$dichiarati = @($campo.GetValue($null) | Sort-Object -Unique)
Bene ("l'esecutore dichiara: " + $dichiarati.Count + " operazioni")

# --- 3. cio' che c'e' DAVVERO nello switch, letto dal sorgente ---------------
# Seconda strada verso lo stesso fatto: se le due divergono, e' una delle due a
# essere marcia, e va detto invece di scegliere quella comoda.
$dalSorgente = @()
if (Test-Path -LiteralPath $Sorgente) {
    $testo = Get-Content -LiteralPath $Sorgente -Raw -Encoding UTF8

    # SI LEGGONO I «case», NON I «case ... return».
    #
    # La prima stesura cercava `case "x": return`, e perdeva il primo di una
    # coppia che ricade sull'altro:
    #
    #     case "membro_strutturale":
    #     case "telaio_rettangolare": return MembroStrutturale(...);
    #
    # Il controllo denunciava `membro_strutturale` come «dichiarato ma non
    # nello switch», che era falso. Un controllo che accusa il codice giusto
    # si smette di guardare dopo due volte.
    #
    # Si guarda solo dentro il corpo di `Esegui`, se lo si trova: altrove nel
    # file ci sono altri switch (nomi di piano, di vista) che non c'entrano.
    $corpo = $testo
    $inizio = $testo.IndexOf("public Esito Esegui(")
    if ($inizio -ge 0) {
        $fine = $testo.IndexOf("`n        }", $inizio)
        if ($fine -gt $inizio) { $corpo = $testo.Substring($inizio, $fine - $inizio) }
    }
    $dalSorgente = @([regex]::Matches($corpo, 'case\s+"([a-z_0-9]+)"\s*:') |
                     ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}
if ($dalSorgente.Count -lt 20) {
    Male ("dal sorgente ho letto solo " + $dalSorgente.Count + " 'case': la " +
          "lettura non ha funzionato, e un controllo che non ha guardato non " +
          "puo' dire che va tutto bene.")
} else {
    $soloDichiarati = @($dichiarati | Where-Object { $_ -notin $dalSorgente })
    $soloSorgente = @($dalSorgente | Where-Object { $_ -notin $dichiarati })
    if ($soloDichiarati.Count -or $soloSorgente.Count) {
        Male "NomiEseguibili e i 'case' del sorgente non combaciano"
        $soloDichiarati | ForEach-Object { Nota "dichiarato ma non nello switch: $_" }
        $soloSorgente   | ForEach-Object { Nota "nello switch ma non dichiarato: $_" }
    } else {
        Bene ("l'elenco dichiarato combacia con i " + $dalSorgente.Count + " 'case' del sorgente")
    }
}

# --- 4. il confronto che conta ----------------------------------------------
$dichiaratiNonEseguibili = @($nomiCatalogo | Where-Object { $_ -notin $dichiarati })
$eseguibiliNonDichiarati = @($dichiarati | Where-Object { $_ -notin $nomiCatalogo })

if ($dichiaratiNonEseguibili.Count) {
    Male ("il catalogo dichiara " + $dichiaratiNonEseguibili.Count +
          " operazioni che l'esecutore NON sa eseguire")
    foreach ($n in $dichiaratiNonEseguibili) {
        $op = $operazioni | Where-Object { $_.nome -eq $n } | Select-Object -First 1
        Nota ("$n   (famiglia: " + $op.famiglia + ")")
    }
    Nota "Il modello puo' chiederle, e si prende 'operazione sconosciuta'."
} else {
    Bene "ogni operazione del catalogo e' eseguibile"
}

if ($eseguibiliNonDichiarati.Count) {
    # NON e' un errore: un'operazione che c'e' ma non e' dichiarata e'
    # semplicemente invisibile al modello, che e' il comportamento voluto per
    # cio' che non e' ancora stato provato.
    Bene ("eseguibili ma non dichiarate: " + $eseguibiliNonDichiarati.Count +
          " (invisibili al modello, ed e' voluto)")
    $eseguibiliNonDichiarati | ForEach-Object { Nota $_ }
}

# --- 5. la forma di ogni voce ------------------------------------------------
Write-Host ""
Write-Host "=== La forma del catalogo" -ForegroundColor Cyan

$campiObbligatori = @("nome", "famiglia", "sommario", "descrizione", "schema")
$senzaCampo = 0
foreach ($op in $operazioni) {
    foreach ($c in $campiObbligatori) {
        $v = $op.$c
        if ($null -eq $v -or ($v -is [string] -and [string]::IsNullOrWhiteSpace($v))) {
            Male ("'" + $op.nome + "' non ha '" + $c + "'")
            $senzaCampo++
        }
    }
}
if ($senzaCampo -eq 0) { Bene "ogni operazione ha nome, famiglia, sommario, descrizione, schema" }

# --- 6. le famiglie ----------------------------------------------------------
# E' il difetto gia' riparato una volta in esporta_catalogo.py: una famiglia
# dichiarata e vuota arriva al modello come una porta che non apre su niente.
$famiglieUsate = @($operazioni | ForEach-Object { $_.famiglia } | Sort-Object -Unique)
$famiglieDichiarate = @()
if ($cat.famiglie) {
    $famiglieDichiarate = @($cat.famiglie | ForEach-Object {
        if ($_ -is [string]) { $_ } else { $_.nome }
    } | Sort-Object -Unique)
}
$vuote = @($famiglieDichiarate | Where-Object { $_ -notin $famiglieUsate })
$nonDichiarate = @($famiglieUsate | Where-Object { $_ -notin $famiglieDichiarate })
if ($vuote.Count) {
    Male ("famiglie dichiarate e VUOTE: " + ($vuote -join ", "))
    Nota "Al modello arrivano come una porta che non apre su niente."
}
if ($nonDichiarate.Count -and $famiglieDichiarate.Count) {
    Male ("famiglie usate ma non dichiarate: " + ($nonDichiarate -join ", "))
}
if (-not $vuote.Count -and -not ($nonDichiarate.Count -and $famiglieDichiarate.Count)) {
    Bene ("le " + $famiglieUsate.Count + " famiglie tornano")
}

# --- 7. cosa NON deve comparire nei testi ------------------------------------
# C2 dell'audit smette di essere un grep da fare a mano e diventa una prova.
Write-Host ""
Write-Host "=== Cosa non deve uscire dal catalogo" -ForegroundColor Cyan
# SI CERCA NEL TESTO DECODIFICATO, NON NEL FILE GREZZO.
#
# La prima stesura leggeva il JSON come testo e cercava «O:\» senza distinguere
# maiuscole. Nel catalogo c'e' la frase «...con il tipo:\n tubolare...», dove
# `\n` e' l'escape JSON di un a capo: la ricerca trovava `o:` + `\` e
# denunciava un percorso interno che non esiste. Decodificando, quell'escape
# diventa un a capo vero e il falso positivo sparisce.
$vietati = @("S:\", "O:\", "W:\", "OpenPDM", "192.168.", "oscar")
$decodificato = New-Object System.Text.StringBuilder
function Raccogli($o) {
    if ($null -eq $o) { return }
    if ($o -is [string]) { [void]$decodificato.AppendLine($o); return }
    if ($o -is [System.Collections.IEnumerable]) {
        foreach ($x in $o) { Raccogli $x }
        return
    }
    if ($o -is [psobject] -and $o.PSObject.Properties) {
        foreach ($p in $o.PSObject.Properties) {
            [void]$decodificato.AppendLine($p.Name)
            Raccogli $p.Value
        }
    }
}
Raccogli $cat
$testo = $decodificato.ToString()
$trovati = @()
foreach ($v in $vietati) {
    # I percorsi si cercano rispettando le maiuscole: «O:\» e' un'unita', «o:\»
    # in mezzo a una frase italiana non lo e' quasi mai. I nomi propri no,
    # quelli si cercano comunque.
    $modo = if ($v.EndsWith("\")) { [StringComparison]::Ordinal }
            else { [StringComparison]::OrdinalIgnoreCase }
    if ($testo.IndexOf($v, $modo) -ge 0) { $trovati += $v }
}
if ($trovati.Count) {
    Male ("nel catalogo compaiono: " + ($trovati -join ", "))
    Nota "Il catalogo lo legge chi usa il ponte: qui dentro non ci va casa nostra."
} else {
    Bene "nessun percorso interno, nome di prodotto o indirizzo"
}

# --- esito -------------------------------------------------------------------
Write-Host ""
if ($problemi.Count -eq 0) {
    Write-Host "Tutto torna." -ForegroundColor Green
    exit 0
}
Write-Host ($problemi.Count.ToString() + " problemi.") -ForegroundColor Red
exit 1
