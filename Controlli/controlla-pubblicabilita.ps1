# Quello che non deve uscire da questa macchina.
#
# Questo repository e' destinato a diventare pubblico. Un percorso con dentro il
# nome di chi lavora, o l'indirizzo di un server aziendale, non e' un difetto di
# stile: e' roba di qualcun altro che esce senza che nessuno l'abbia deciso.
#
#   .\Controlli\controlla-pubblicabilita.ps1
#
# Esce 0 se e' pulito, 1 se no.
#
# ------------------------------------------------------------------------------
# IL PERIMETRO E' SBAGLIATO TRE VOLTE, E LA TERZA E' COSTATA QUASI UN PUSH.
# ------------------------------------------------------------------------------
#
#   1. `controlla-catalogo.ps1` guardava dentro `catalogo.json` e basta, perche'
#      quando fu scritto il catalogo era l'unica cosa che usciva. Poi e' uscito
#      il banco: DICIANNOVE file col percorso della scratchpad di sessione, nome
#      utente compreso. Il controllo c'era e guardava altrove.
#
#   2. Allora e' nato questo, che guardava tutto l'albero di lavoro - ma solo i
#      file con certe estensioni. `App.config`, `app.manifest`, `LICENSE` e
#      `NOTICE` non ne avevano una buona e non venivano letti da nessuno.
#
#   3. E guardava solo l'ALBERO. Il 14/9/2026, un minuto prima del primo push,
#      diceva «Pulito»: l'albero era stato riparato davvero. Ma `git push` manda
#      la STORIA, e nella storia il percorso c'era ancora in 155 punti su 41
#      commit - perche' i blob vivono in ogni albero intermedio, non solo nei due
#      commit dove `git log -S` segnala un cambiamento.
#
# LE DUE LEZIONI, scritte qui perche' la quarta volta non costi di nuovo:
#
#   IL PERIMETRO NON E' UNA LISTA, E' CIO' CHE ESCE. Non piu' un elenco di
#   estensioni da tenere aggiornato a mano, ma `git ls-files`: quello che git
#   traccia e' esattamente quello che git spinge. Le estensioni adesso sono una
#   lista di ESCLUSIONE (i binari): se domani nasce un tipo di file di testo
#   nuovo, entra da solo nel controllo. Una lista di inclusione sbaglia in
#   silenzio, una di esclusione sbaglia rumorosamente.
#
#   SI GUARDA ANCHE LA STORIA, e si guarda l'ALBERO di ogni commit, non il suo
#   diff. Un `git log -p` avrebbe mostrato due commit; gli alberi ne mostrano
#   quarantuno.
#
# ------------------------------------------------------------------------------
# DUE ACCORGIMENTI CHE NON SI DEDUCONO, e sono costati un falso positivo a testa:
#
#   1. `Select-String` NON distingue maiuscole e minuscole di suo. Cercando
#      "O:\" trovava `esempio:\n` dentro una descrizione JSON - cioe' "o" +
#      ":" + l'escape di un a capo. Le lettere di unita' si cercano con
#      -CaseSensitive e con la regola che prima non ci sia una lettera.
#   2. I file che NOMINANO i vietati perche' il loro mestiere e' cercarli si
#      elencano uno per uno. Una regola larga qui sarebbe un buco: l'eccezione
#      del TODO vale solo per i pattern che la voce A1 discute, e infatti il
#      nome utente nel TODO c'era ed era una fuga come le altre.
#
# NOTA: in ASCII puro, come tutti gli .ps1 del progetto.

$ErrorActionPreference = 'Stop'
$radice = Split-Path -Parent $PSScriptRoot
$guai = 0

# Estensioni ESCLUSE: i binari, che Select-String non sa leggere e che non
# portano testo. Tutto il resto entra, anche cio' che non ha estensione.
$binari = @(
    '.dll', '.exe', '.pdb', '.res', '.cache', '.suo', '.zip', '.7z', '.gz',
    '.png', '.jpg', '.jpeg', '.gif', '.ico', '.bmp', '.pdf',
    '.sldprt', '.slddrw', '.sldasm', '.sldlfp', '.sldmat', '.step', '.stp', '.igs'
)

# I file che possono NOMINARE i vietati, perche' il loro mestiere e' cercarli.
#
# Si elencano per NOME e non per percorso, e il motivo e' misurato: nella storia
# `controlla-catalogo.ps1` sta in `Strumenti\`, perche' e' stato spostato in
# `Controlli\` solo dopo. Un'eccezione legata al percorso avrebbe segnalato come
# fuga il controllo stesso, a ogni commit precedente allo spostamento.
# I file si spostano, il loro mestiere no.
#
# Resta comunque un elenco di tre nomi precisi: una regola larga qui sarebbe un
# buco, e infatti nel TODO il nome utente c'era davvero.
$possonoNominarli = @(
    'controlla-catalogo.ps1',
    'controlla-pubblicabilita.ps1',
    'bonifica-storia.js'
)

function PuoNominarli($percorso) {
    $possonoNominarli -contains (Split-Path -Leaf ($percorso -replace '/', '\'))
}

# IL TODO NOMINA I VIETATI PERCHE' E' LI' CHE SONO SCRITTI.
# La voce A1 elenca cosa non deve uscire, e per elencarlo lo deve scrivere.
# L'eccezione vale SOLO per i pattern che quella voce discute: il nome utente
# non e' fra questi, e se compare nel TODO e' una fuga come le altre.
$loDiceIlTodo = @('TODO.md')

# NON si puo' chiamare questa funzione `Git`: PowerShell non distingue maiuscole
# e minuscole nei nomi di comando, quindi `& git` qui dentro risolverebbe alla
# funzione stessa invece che all'eseguibile - ricorsione infinita, e l'errore che
# si legge e' «Overflow della profondita' delle chiamate», che non nomina la
# causa. Il nome diverso e' la riparazione, come per `LeggiConAncora`.
function EseguiGit($argomenti) {
    $vecchio = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git.exe -C $radice @argomenti 2>$null } finally { $ErrorActionPreference = $vecchio }
}

# ==============================================================================
# 1. L'albero di lavoro: cio' che si sta per committare
# ==============================================================================

function FileCheEscono {
    # Tracciati + non ancora tracciati ma non ignorati. Insieme sono tutto cio'
    # che un `git add -A` porterebbe dentro. I file ignorati (bin, obj) no: non
    # escono, e leggerli raddoppia il tempo per niente.
    $tracciati = @(EseguiGit @('ls-files'))
    $nuovi = @(EseguiGit @('ls-files', '--others', '--exclude-standard'))
    @($tracciati + $nuovi) |
        Where-Object { $_ } |
        Sort-Object -Unique |
        Where-Object { $binari -notcontains [IO.Path]::GetExtension($_).ToLower() } |
        ForEach-Object { Join-Path $radice ($_ -replace '/', '\') } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
}

$script:elenco = $null
function Righe($pattern, $caseSensitive) {
    if ($null -eq $script:elenco) { $script:elenco = @(FileCheEscono) }
    if ($script:elenco.Count -eq 0) { return @() }
    # -LiteralPath, NON la pipeline. Un array di stringhe mandato a
    # `Select-String` per pipeline viene preso come TESTO DA CERCARE, non come
    # elenco di file: il controllo cercava il nome utente dentro i NOMI dei
    # percorsi e rispondeva «ok nessuno» senza aver aperto un solo file.
    # Si e' visto solo perche' il pattern `S:\` combacia con ogni percorso
    # assoluto di questo disco e ha fatto scoppiare la riga qui sotto.
    if ($caseSensitive) { $trovate = Select-String -LiteralPath $script:elenco -Pattern $pattern -CaseSensitive }
    else { $trovate = Select-String -LiteralPath $script:elenco -Pattern $pattern }
    @($trovate | Where-Object {
        -not (PuoNominarli $_.Path)
    })
}

function Guarda($nome, $pattern, $perche, $caseSensitive, $eccetto = @()) {
    $trovati = @(Righe $pattern $caseSensitive | Where-Object {
        -not ($eccetto -contains (Split-Path -Leaf $_.Path))
    })
    if ($trovati.Count -eq 0) { Write-Host "  ok  nessun $nome"; return }
    Write-Host "  XX  $nome ($perche) in $($trovati.Count) punti:"
    $script:guai++
    $visti = @{}
    foreach ($t in $trovati) {
        $relativo = $t.Path.Substring($radice.Length + 1)
        if ($visti.ContainsKey($relativo)) { continue }
        $visti[$relativo] = $true
        Write-Host "        ${relativo}:$($t.LineNumber)"
    }
    if ($visti.Count -lt $trovati.Count) { Write-Host "        (e altre righe negli stessi file)" }
}

Write-Host "=== Nell'albero di lavoro"
Guarda 'il nome utente' 'oscar' 'il nome di chi lavora' $true
Guarda 'un percorso di sessione' 'AppData.Local.Temp' 'esiste solo su questa macchina' $false
Guarda 'un indirizzo interno' '192\.168\.' 'una rete privata' $false $loDiceIlTodo
Guarda 'il disco O:' '(?<![A-Za-z])O:\\' 'un disco di rete aziendale' $true $loDiceIlTodo
Guarda 'il disco W:' '(?<![A-Za-z])W:\\' 'un disco di rete aziendale' $true $loDiceIlTodo

# ==============================================================================
# 2. La storia che si sta per spingere
# ==============================================================================
#
# Si guarda l'ALBERO di ogni commit da spingere, non il suo diff: un blob
# introdotto una volta vive in tutti gli alberi successivi finche' non viene
# tolto, e il diff lo mostra solo due volte.
#
# `git grep` cerca dentro un albero senza estrarlo, quindi quarantuno commit
# costano secondi e non minuti. -I salta i binari da solo.

Write-Host ""
Write-Host "=== Nella storia che si sta per spingere"

$aMonte = (EseguiGit @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{upstream}'))
if (-not $aMonte) { $aMonte = 'origin/main' }
$esiste = (EseguiGit @('rev-parse', '--verify', '--quiet', $aMonte))

if (-not $esiste) {
    Write-Host "  .. nessun ramo a monte ($aMonte): si guarda TUTTA la storia"
    $daSpingere = @(EseguiGit @('rev-list', 'HEAD'))
} else {
    $daSpingere = @(EseguiGit @('rev-list', "$aMonte..HEAD"))
}

if ($daSpingere.Count -eq 0) {
    Write-Host "  ok  niente da spingere"
} else {
    Write-Host "  .. $($daSpingere.Count) commit da guardare"

    function GuardaStoria($nome, $pattern, $perche, $caseSensitive, $eccetto = @()) {
        $trovati = @(EseguiGit (@('grep', '-I', '-n', '--no-color') +
            $(if ($caseSensitive) { @() } else { @('-i') }) +
            @('-P', '-e', $pattern) + $daSpingere))

        # Forma di una riga: <sha>:<percorso>:<numero>:<testo>
        $fughe = @($trovati | Where-Object {
            if ($_ -notmatch '^([0-9a-f]{7,40}):([^:]+):(\d+):') { return $false }
            $percorso = $Matches[2] -replace '/', '\'
            if (PuoNominarli $percorso) { return $false }
            if ($eccetto -contains (Split-Path -Leaf $percorso)) { return $false }
            $true
        })

        if ($fughe.Count -eq 0) { Write-Host "  ok  nessun $nome"; return }
        $script:guai++
        $file = @($fughe | ForEach-Object {
            $null = $_ -match '^([0-9a-f]{7,40}):([^:]+):'
            $Matches[2]
        } | Sort-Object -Unique)
        Write-Host "  XX  $nome ($perche) in $($fughe.Count) punti, $($file.Count) file:"
        foreach ($f in $file) { Write-Host "        $f" }
        Write-Host "        La storia non si ripara con una modifica: serve"
        Write-Host "        Controlli\bonifica-storia.js e git filter-branch, e va"
        Write-Host "        fatto PRIMA del push - dopo diventa un force-push."
    }

    GuardaStoria 'il nome utente' 'oscar' 'il nome di chi lavora' $true
    GuardaStoria 'un percorso di sessione' 'AppData.Local.Temp' 'esiste solo su questa macchina' $false
    GuardaStoria 'un indirizzo interno' '192\.168\.' 'una rete privata' $false $loDiceIlTodo
    GuardaStoria 'il disco O:' '(?<![A-Za-z])O:\\' 'un disco di rete aziendale' $true $loDiceIlTodo
    GuardaStoria 'il disco W:' '(?<![A-Za-z])W:\\' 'un disco di rete aziendale' $true $loDiceIlTodo
}

# ==============================================================================
# 3. Il disco S:, che si dice invece di nascondere
# ==============================================================================
#
# `<impostazioni SOLIDWORKS>` e' dove stanno i profili normati di chi lavora,
# e il banco ci punta davvero per provare le saldature. Non e' un segreto ed e'
# una precondizione vera: ma chi clona il repository non ce l'ha, e deve saperlo
# prima di lanciare il banco, non dopo.

Write-Host ""
Write-Host "=== Percorsi di questa installazione (non vietati, ma da sapere)"
$suDiscoS = Righe '(?<![A-Za-z])S:\\' $true
if ($suDiscoS.Count -eq 0) {
    Write-Host "  nessuno"
} else {
    $file = $suDiscoS | ForEach-Object { $_.Path.Substring($radice.Length + 1) } | Sort-Object -Unique
    Write-Host "  $($suDiscoS.Count) riferimenti a S: in $($file.Count) file:"
    foreach ($f in $file) { Write-Host "     $f" }
    Write-Host "  Vanno bene solo se chi clona puo' scavalcarli con una variabile o un"
    Write-Host "  argomento. Se sono costanti, non e' un repository che si clona."
}

Write-Host ""
if ($guai -eq 0) { Write-Host 'Pulito.'; exit 0 }
Write-Host "$guai categorie da ripulire."
exit 1
