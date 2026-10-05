# Quello che non deve uscire da questa macchina.
#
# Questo repository e' destinato a diventare pubblico. Un percorso con dentro il
# nome di chi lavora, o l'indirizzo di un server aziendale, non e' un difetto di
# stile: e' roba di qualcun altro che esce senza che nessuno l'abbia deciso.
#
#   .\Checks\check-publishability.ps1
#
# Esce 0 se e' pulito, 1 se no.
#
# ------------------------------------------------------------------------------
# IL PERIMETRO E' SBAGLIATO TRE VOLTE, E LA TERZA E' COSTATA QUASI UN PUSH.
# ------------------------------------------------------------------------------
#
#   1. `check-catalog.ps1` guardava dentro `catalog.json` e basta, perche'
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
$root = Split-Path -Parent $PSScriptRoot
$trouble = 0

# Estensioni ESCLUSE: i binari, che Select-String non sa leggere e che non
# portano testo. Tutto il resto entra, anche cio' che non ha estensione.
$binary = @(
    '.dll', '.exe', '.pdb', '.res', '.cache', '.suo', '.zip', '.7z', '.gz',
    '.png', '.jpg', '.jpeg', '.gif', '.ico', '.bmp', '.pdf',
    '.sldprt', '.slddrw', '.sldasm', '.sldlfp', '.sldmat', '.step', '.stp', '.igs'
)

# ==============================================================================
# LE ECCEZIONI, UNA PER PATTERN E MAI UNA PER FILE
# ==============================================================================
#
# QUI STAVA `$possonoNominarli`, tre nomi di file applicati dentro `Lines()` e
# quindi validi per OGNI pattern: chi era in quell'elenco non veniva controllato
# per niente. Sembrava stretto - tre nomi soli - e invece era la piu' larga
# delle eccezioni, perche' non diceva DA COSA esentava.
#
# **Il conto e' arrivato dentro questo stesso file.** Riparando l'eccezione del
# TODO ho scritto i due indirizzi veri nel commento che la racconta, e sono
# rimasti li' senza che niente li segnalasse: il controllo non guardava il
# controllo. Non l'ha trovato una sonda, l'ha trovato un censimento fatto a
# mano tre ore dopo.
#
# Adesso ogni pattern porta il PROPRIO elenco, e ogni nome in un elenco deve
# avere un motivo che si legge accanto:
#
#   $nameOnly        chi deve scrivere «oscar» perche' lo cerca (i due
#                      controlli) o perche' lo attribuisce (LICENSE, NOTICE).
#   $sessionOnly    chi deve scrivere il percorso di scratchpad perche' lo
#                      sostituisce.
#   $talkAboutDisks  chi deve scrivere `O:` e `W:` perche' li elenca.
#   l'indirizzo        NESSUNO. Il pattern non nomina se stesso, quindi non
#                      serve a nessuno un lasciapassare - e chi ne avesse
#                      bisogno starebbe scrivendo un indirizzo vero.
#
# I nomi restano NOMI e non percorsi, e il motivo e' misurato: nella storia
# `check-catalog.ps1` sta in `Tools\`, perche' e' stato spostato in
# `Checks\` solo dopo. Un'eccezione legata al percorso avrebbe segnalato come
# fuga il controllo stesso a ogni commit precedente allo spostamento. I file si
# spostano, il loro mestiere no.
$twoChecks = @('check-catalog.ps1', 'check-publishability.ps1')

# L'ECCEZIONE ERA SUL FILE, E DOVEVA ESSERE SULLA FORMA. TRE INDIRIZZI VERI
# SONO USCITI DA QUI.
#
# Qui stava `$loDiceIlTodo = @('TODO.md')`, passato al pattern `192\.168\.`
# perche' la voce A1 elenca cosa non deve uscire e per elencarlo lo deve
# scrivere. Il commento diceva «vale SOLO per i pattern che quella voce
# discute»; il codice esentava il FILE INTERO, e `Look` confronta il nome del
# file, non la riga. Fra una stesura e l'altra il TODO e' cresciuto di
# sedicimila righe, e dentro ci sono finiti tre indirizzi VERI:
#
#   TODO.md:12107   l'indirizzo del server di un altro progetto
#   TODO.md:16569   il percorso UNC della condivisione dietro S:
#   TODO.md:16750   la stessa, un'altra volta
#
# E NON SI SCRIVONO QUI. La prima stesura di questo commento li riportava per
# esteso, tutti e due, «per chiarezza» - dentro il file che esiste per vietarli,
# e che si esentava da solo. E' il cortocircuito che `cleanup-history.js` aveva
# gia' pagato e scritto in cima a se stesso: *uno strumento che toglie una
# stringa deve nominarla, e cosi' la cura porta la malattia.* Un incidente si
# racconta con la sua forma, non con il suo valore.
#
# Erano gia' spinti su GitHub. NON erano pubblici: il repository e' ancora
# privato (verificato con `gh repo view`: visibility PRIVATE), quindi nessun
# estraneo li ha mai potuti leggere. Ma erano dentro la storia che il giorno
# della pubblicazione sarebbe uscita tutta insieme, e questo controllo esiste
# esattamente per quel giorno: girava, diceva «ok nessun indirizzo interno», e
# aveva ragione secondo la regola che gli era stata data.
#
# LA RIPARAZIONE NON E' UN'ECCEZIONE PIU' STRETTA, e' una regola che guarda la
# cosa invece di chi l'ha scritta: un indirizzo vero ha una CIFRA dopo il terzo
# punto, un elenco di regole scrive il jolly. `192.168.*` passa ovunque, un
# indirizzo con le cifre e' vietato ovunque, TODO compreso. NESSUN FILE HA PIU'
# UN LASCIAPASSARE SU QUESTO PATTERN - nemmeno questo, e il motivo sta qui
# sotto, in `$nameOnly`.
#
# Nota che il pattern non nomina se stesso: `192\.168\.\d` come TESTO non
# combacia con `192\.168\.\d` come REGOLA, perche' dopo il «192» la regola
# pretende un punto e il testo ha una barra rovescia. E' per questo che questo
# file puo' scrivere la regola e restare sotto la regola.
#
# *Un'eccezione che nomina un file si allarga da sola ogni volta che il file
# cresce. Una che descrive la forma no.*
$TrueAddress = '192\.168\.\d'

# I DUE FILE IL CUI MESTIERE E' NOMINARE L'AUTORE.
#
# La regola del nome utente e' passata da `-CaseSensitive` a insensibile,
# perche' cercava il nome tutto minuscolo e quello con la maiuscola le passava
# davanti:
# `Bench\NoPartTests.cs:185` lo portava in un commento, e il controllo
# diceva «ok». Nessuna parola italiana contiene «oscar», quindi togliere la
# sensibilita' non costa un solo falso positivo... tranne questi due, dove il
# nome NON e' una fuga: e' cio' che il file deve dire.
#
#   LICENSE:189   «Copyright 2026 <autore>» - la riga che la Apache-2.0
#                 richiede, e senza la quale la licenza non e' concessa a
#                 nessuno.
#   NOTICE:2,4    l'attribuzione, che la Apache-2.0 impone di ridistribuire.
#
# PERCHE' QUESTA ECCEZIONE NON E' QUELLA CHE HA FATTO USCIRE TRE INDIRIZZI.
# Quella esentava un diario di diciassettemila righe che cresce ogni giorno: si
# allargava da sola, e nessuno rileggeva cosa ci era finito dentro. Questi sono
# due testi di licenza che non crescono, in cui il nome sta in tre righe fisse
# e messe li' apposta. L'eccezione vale SOLO per il pattern del nome: percorsi,
# indirizzi e lettere di unita' su questi due file restano vietati come altrove.
$nameOnly = @('LICENSE', 'NOTICE') + $twoChecks

# Chi sostituisce il percorso di scratchpad lo deve scrivere per trovarlo:
# `cleanup-history.js` ha lo schema, questo controllo ha il pattern.
$sessionOnly = @('cleanup-history.js', 'check-publishability.ps1')

# LA STRUTTURA DI UN ALTRO REPOSITORY, CHE E' PRIVATO.
#
# Questo ponte nasce da un progetto piu' grande e ci dialoga: nominarlo e
# raccontare da dove viene il catalogo e' giusto, e il nome nudo di un file
# (`ai_disegno.py`) e' contesto, non una fuga.
#
# Ma il 24/9/2026 un censimento a mano ha trovato scritto per esteso
# `api/<file>.py:283`, `services/<file>.py:104` e `:252` - cioe' CARTELLE,
# FILE e NUMERI DI RIGA di un repository privato, che permettono di
# ricostruire com'e' fatto dentro senza averlo mai visto. Erano in due posti
# (`Tools\update.md` e la voce di CHANGELOG che la ricopiava), e nessun
# controllo li guardava: cercavamo percorsi di QUESTA macchina e indirizzi di
# QUESTA rete, e non ci era venuto in mente che si potesse pubblicare l'interno
# di un progetto vicino.
#
# LA FORMA, e non il nome del progetto: in questo repository NON C'E' UN SOLO
# FILE PYTHON (verificato con `git ls-files '*.py'`: vuoto). Quindi un `.py`
# con una CARTELLA davanti, o con un NUMERO DI RIGA dietro, parla per forza di
# casa d'altri. Il nome nudo continua a passare.
$InsideAnotherRepo = '([A-Za-z0-9_]+/[A-Za-z0-9_/]*\.py|\.py:\d)'

# CHI PARLA DELLE LETTERE DI UNITA' PERCHE' STA SPIEGANDO QUESTO CONTROLLO.
#
# Vale SOLO per i pattern `O:` e `W:`, e non tocca il nome utente, i percorsi
# di sessione e gli indirizzi privati, che su questi stessi file restano
# vietati come su tutti.
#
#   CHANGELOG.md       la voce che racconta il falso positivo del 14/9 deve
#                      citare il pattern che lo causava.
#   CatalogTests.cs   e' il controllo che cerca i percorsi locali dentro le
#                      descrizioni del catalogo: il suo mestiere e' nominarli.
#
# Perche' il buco e' accettabile qui e non altrove: nessuno dei tre e' un file
# GENERATO. Un percorso di rete vero ci arriva solo se qualcuno lo scrive a
# mano, e chi lo scrive a mano in un documento che spiega perche' non si scrive
# lo sta facendo apposta. I file generati - il catalogo, i casi del banco -
# restano coperti.
$talkAboutDisks = @('CHANGELOG.md', 'CatalogTests.cs') + $twoChecks

# NON si puo' chiamare questa funzione `Git`: PowerShell non distingue maiuscole
# e minuscole nei nomi di comando, quindi `& git` qui dentro risolverebbe alla
# funzione stessa invece che all'eseguibile - ricorsione infinita, e l'errore che
# si legge e' «Overflow della profondita' delle chiamate», che non nomina la
# causa. Il nome diverso e' la riparazione, come per `ReadWithAnchor`.
function RunGit($arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git.exe -C $root @arguments 2>$null } finally { $ErrorActionPreference = $old }
}

# ==============================================================================
# 1. L'albero di lavoro: cio' che si sta per committare
# ==============================================================================

function FilesThatGoOut {
    # Tracciati + non ancora tracciati ma non ignorati. Insieme sono tutto cio'
    # che un `git add -A` porterebbe dentro. I file ignorati (bin, obj) no: non
    # escono, e leggerli raddoppia il tempo per niente.
    $tracked = @(RunGit @('ls-files'))
    $newItems = @(RunGit @('ls-files', '--others', '--exclude-standard'))
    @($tracked + $newItems) |
        Where-Object { $_ } |
        Sort-Object -Unique |
        Where-Object { $binary -notcontains [IO.Path]::GetExtension($_).ToLower() } |
        ForEach-Object { Join-Path $root ($_ -replace '/', '\') } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
}

$script:list = $null
function Lines($pattern, $caseSensitive) {
    if ($null -eq $script:list) { $script:list = @(FilesThatGoOut) }
    if ($script:list.Count -eq 0) { return @() }
    # -LiteralPath, NON la pipeline. Un array di stringhe mandato a
    # `Select-String` per pipeline viene preso come TESTO DA CERCARE, non come
    # elenco di file: il controllo cercava il nome utente dentro i NOMI dei
    # percorsi e rispondeva «ok nessuno» senza aver aperto un solo file.
    # Si e' visto solo perche' il pattern `S:\` combacia con ogni percorso
    # assoluto di questo disco e ha fatto scoppiare la riga qui sotto.
    if ($caseSensitive) { $foundOnes = Select-String -LiteralPath $script:list -Pattern $pattern -CaseSensitive }
    else { $foundOnes = Select-String -LiteralPath $script:list -Pattern $pattern }
    @($foundOnes)
}

function Look($itemName, $pattern, $why, $caseSensitive, $except = @()) {
    $foundItems = @(Lines $pattern $caseSensitive | Where-Object {
        -not ($except -contains (Split-Path -Leaf $_.Path))
    })
    if ($foundItems.Count -eq 0) { Write-Host "  ok  nessun $itemName"; return }
    Write-Host "  XX  $itemName ($why) in $($foundItems.Count) punti:"
    $script:trouble++
    $seen = @{}
    foreach ($t in $foundItems) {
        $relative = $t.Path.Substring($root.Length + 1)
        if ($seen.ContainsKey($relative)) { continue }
        $seen[$relative] = $true
        Write-Host "        ${relative}:$($t.LineNumber)"
    }
    if ($seen.Count -lt $foundItems.Count) { Write-Host "        (e altre righe negli stessi file)" }
}

Write-Host "=== Nell'albero di lavoro"
Look 'il nome utente' 'oscar' 'il nome di chi lavora' $false $nameOnly
Look 'un percorso di sessione' 'AppData.Local.Temp' 'esiste solo su questa macchina' $false $sessionOnly
Look 'un indirizzo interno' $TrueAddress 'una rete privata' $false
Look 'l''interno di un altro repository' $InsideAnotherRepo 'cartelle e righe di casa d''altri' $false $twoChecks
Look 'il disco O:' '(?<![A-Za-z])O:\\' 'un disco di rete aziendale' $true $talkAboutDisks
Look 'il disco W:' '(?<![A-Za-z])W:\\' 'un disco di rete aziendale' $true $talkAboutDisks

# ==============================================================================
# 2. La storia che si sta per spingere
# ==============================================================================
#
# Si guarda l'ALBERO di ogni commit da spingere, non il suo diff: un blob
# introdotto una volta vive in tutti gli alberi successivi finche' non viene
# tolto, e il diff lo mostra solo due volte.
#
# `git grep` cerca dentro un albero senza estrarlo, quindi quarantuno commit
# costano secondi e non minuti.
#
# BINARY FILES ARE SKIPPED BY EXTENSION, NOT BY GIT'S GUESS (entry Z-C22). Here
# stood `git grep -I`, which skips every blob git takes for binary: a text file
# with one NUL byte inside is one. Checks/questions-without-answer.js had a real
# NUL (offset 3899) and was never looked at, in the tree or in the history. Now
# `--text` reads every blob, and the paths whose extension is in `$binary`
# (the same list the working-tree pass uses) are dropped afterwards — the rule
# of the language detector: `git ls-files` minus the binaries by extension.

Write-Host ""
Write-Host "=== Nella storia che si sta per spingere"

$upstream = (RunGit @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{upstream}'))
if (-not $upstream) { $upstream = 'origin/main' }
$exists = (RunGit @('rev-parse', '--verify', '--quiet', $upstream))

# ── IL RIFERIMENTO LOCALE PUO' ESSERE UNA BUGIA, E LO E' STATO ────────────
#
# `git filter-branch -- --all` riscrive anche i riferimenti remoti LOCALI.
# Dopo la bonifica del 23/9/2026 `origin/main` qui puntava a un commit
# riscritto: `origin/main..HEAD` dava 29 commit su 104, e questo controllo
# ha detto «Pulito» dopo averne guardato meno di un terzo.
#
# Il remoto vero, intanto, aveva ancora la storia di prima. Al force-push
# partiranno tutti e 104 — cioe' il controllo misurava una cosa diversa da
# quella che sta per succedere, ed e' il modo in cui una difesa vera si
# trasforma in un timbro.
#
# Si chiede al remoto dove sta DAVVERO. Se non combacia, il riferimento
# locale non vale niente e si guarda tutta la storia. Se il remoto non
# risponde, si guarda tutta la storia lo stesso: fra sbagliare per eccesso e
# spingere un nome utente, la scelta non e' difficile.
$allHistory = $false
$why = ""

if (-not $exists) {
    $allHistory = $true
    $why = "nessun ramo a monte ($upstream)"
} else {
    $remote = ($upstream -split '/')[0]
    $branch   = ($upstream -split '/', 2)[1]
    $la = (RunGit @('ls-remote', '--heads', $remote, $branch))
    if (-not $la) {
        $allHistory = $true
        $why = "il remoto non risponde, e un riferimento locale non basta"
    } else {
        $remoteSha = ($la -split "\s+")[0]
        $shaLocale = (RunGit @('rev-parse', $upstream))
        if ($remoteSha -ne $shaLocale) {
            $allHistory = $true
            $why = "$upstream qui dice $($shaLocale.Substring(0,7)) ma il remoto " +
                      "dice $($remoteSha.Substring(0,7)): la storia e' stata riscritta"
        }
    }
}

if ($allHistory) {
    Write-Host "  .. $why"
    Write-Host "  .. si guarda TUTTA la storia"
    $toPush = @(RunGit @('rev-list', 'HEAD'))
} else {
    $toPush = @(RunGit @('rev-list', "$upstream..HEAD"))
}

if ($toPush.Count -eq 0) {
    Write-Host "  ok  niente da spingere"
} else {
    Write-Host "  .. $($toPush.Count) commit da guardare"

    function LookAtHistory($itemName, $pattern, $why, $caseSensitive, $except = @()) {
        $foundItems = @(RunGit (@('grep', '--text', '-n', '--no-color') +
            $(if ($caseSensitive) { @() } else { @('-i') }) +
            @('-P', '-e', $pattern) + $toPush))

        # Forma di una riga: <sha>:<percorso>:<numero>:<testo>
        $gaps = @($foundItems | Where-Object {
            if ($_ -notmatch '^([0-9a-f]{7,40}):([^:]+):(\d+):') { return $false }
            $filePath = $Matches[2] -replace '/', '\'
            # Solo `$except`, cioe' l'eccezione DEL PATTERN. Qui stava anche
            # `PuoNominarli`, la lista valida per tutti i pattern insieme: e'
            # sparita, e con lei il lasciapassare che i due controlli avevano
            # sull'indirizzo interno.
            if ($except -contains (Split-Path -Leaf $filePath)) { return $false }
            if ($binary -contains [IO.Path]::GetExtension($filePath).ToLower()) { return $false }
            $true
        })

        if ($gaps.Count -eq 0) { Write-Host "  ok  nessun $itemName"; return }
        $script:trouble++
        $file = @($gaps | ForEach-Object {
            $null = $_ -match '^([0-9a-f]{7,40}):([^:]+):'
            $Matches[2]
        } | Sort-Object -Unique)
        Write-Host "  XX  $itemName ($why) in $($gaps.Count) punti, $($file.Count) file:"
        foreach ($f in $file) { Write-Host "        $f" }
        Write-Host "        La storia non si ripara con una modifica: serve"
        Write-Host "        Checks\cleanup-history.js e git filter-branch, e va"
        Write-Host "        fatto PRIMA del push - dopo diventa un force-push."
    }

    LookAtHistory 'il nome utente' 'oscar' 'il nome di chi lavora' $false $nameOnly
    LookAtHistory 'un percorso di sessione' 'AppData.Local.Temp' 'esiste solo su questa macchina' $false $sessionOnly
    LookAtHistory 'un indirizzo interno' $TrueAddress 'una rete privata' $false
    LookAtHistory 'l''interno di un altro repository' $InsideAnotherRepo 'cartelle e righe di casa d''altri' $false $twoChecks
    LookAtHistory 'il disco O:' '(?<![A-Za-z])O:\\' 'un disco di rete aziendale' $true $talkAboutDisks
    LookAtHistory 'il disco W:' '(?<![A-Za-z])W:\\' 'un disco di rete aziendale' $true $talkAboutDisks
}

# ==============================================================================
# 3. Il disco S:, che si dice invece di nascondere
# ==============================================================================
#
# Il 24/9/2026 i RIPIEGHI CABLATI sono spariti: la libreria dei profili si dice
# con `PROFILI_SALDATURA` e i modelli di documento con `MODELLI_SOLIDWORKS`, e
# senza quelle variabili il banco si ferma subito dicendo cosa manca invece di
# cercare un disco che su quella macchina non esiste. Un ripiego che funziona su
# una postazione sola non aiuta nessuno, e intanto pubblica com'e' fatto quel
# disco.
#
# QUESTO ELENCO PERO' RESTA, e non e' diventato inutile: `S:` continua a
# comparire negli ESEMPI (README, descrizioni del catalogo) e nelle prove che
# usano percorsi finti. Quelli vanno bene. Cio' che non deve tornare e' una
# COSTANTE da cui dipende un comportamento - ed e' esattamente quello che
# questo elenco fa vedere, file per file, a chi legge prima di pubblicare.

Write-Host ""
Write-Host "=== Percorsi di questa installazione (non vietati, ma da sapere)"
$onDiskS = Lines '(?<![A-Za-z])S:\\' $true
if ($onDiskS.Count -eq 0) {
    Write-Host "  nessuno"
} else {
    $file = $onDiskS | ForEach-Object { $_.Path.Substring($root.Length + 1) } | Sort-Object -Unique
    Write-Host "  $($onDiskS.Count) riferimenti a S: in $($file.Count) file:"
    foreach ($f in $file) { Write-Host "     $f" }
    Write-Host "  Vanno bene solo se chi clona puo' scavalcarli con una variabile o un"
    Write-Host "  argomento. Se sono costanti, non e' un repository che si clona."
}

Write-Host ""
if ($trouble -eq 0) { Write-Host 'Pulito.'; exit 0 }
Write-Host "$trouble categorie da ripulire."
exit 1
