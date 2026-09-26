# Il banco

Le prove che si fanno **dentro SOLIDWORKS**, contro la libreria vera.

Sono due banchi diversi, per due domande diverse:

| | domanda | serve SOLIDWORKS? |
|---|---|---|
| `ProvaSaldatura.exe` | le operazioni della libreria fanno il pezzo giusto? | si' |
| `corri_piani.js` | il ponte MCP esegue i piani come deve? | si' |
| `prova_annulla.js` | `annulla` ritira il piano — e si **rifiuta** quando deve? | si' |
| `raccogli_viste.js` | costruisce le cinque forme di V2/V3 e ne salva immagini e stato | si' |
| `prova_forma.js` | dalla sola **frase** un modello sa ridisegnare il pezzo? (V3) | si' |
| `prova_sezione.js` | lo **spaccato** mostra la parete, e si toglie da solo? (V4) | si' |
| `..\Controlli\Prova*.exe` | la matematica torna? | **no** |

I primi tre misurano pezzi, l'ultimo misura formule. L'ultimo si lancia in un
secondo e va lanciato per primo: se non torna la formula, non vale la pena
aprire il CAD.

---

## `ProvaSaldatura.exe` — le operazioni

Carica `Riscontro.Disegno.dll` e chiama i suoi tipi per riflessione, con gli
stessi argomenti che arriverebbero da un piano. Ogni prova lavora su una parte
usa e getta e la chiude.

> **Prima di tutto questo c'e' `Prove\Prove.Contratti.exe`** — 54 controlli in
> pochi secondi, **senza SOLIDWORKS**: il valutatore delle attese, il catalogo,
> `StessoDocumento`, il protocollo MCP. Vedi [Prove/LEGGIMI.md](../Prove/LEGGIMI.md).
> Se sono rossi quelli, aprire il CAD e' tempo perso.

### Un comando solo

```
.\Banco\banco.ps1                        tutto, e scrive il resoconto
.\Banco\banco.ps1 -Gruppo viste          una famiglia sola
.\Banco\banco.ps1 -Famiglia ritrovare    una cartella sola di casi dichiarati
.\Banco\banco.ps1 -Lento                 anche quelle che costano minuti
.\Banco\banco.ps1 -SaltaCompilazione     il binario che c'e' gia'
```

Compila libreria e banco (e **si ferma** se una delle due fallisce), guarda se
SOLIDWORKS e' agganciabile, **svuota i documenti rimasti aperti**, lancia, e
scrive `Banco/resoconto/AAAAMMGG_hhmm.md` e `.json` piu' `ultimo.json`.

| uscita | |
|---|---|
| **0** | tutto verde |
| **1** | prove fallite — il `.md` le elenca con atteso e misurato |
| **2** | errore di banco: compilazione fallita, SOLIDWORKS non agganciabile, gruppo che non esiste, oppure un caso che **non si e' potuto misurare** |

Il 2 **non e' una prova rossa** e non va contato insieme: una prova rossa e' una
misura, un errore di banco e' una misura **mancata**. E il 2 vince sull'1: se il
banco non ha potuto misurare, quante prove siano fallite e' una domanda a cui
non si e' risposto.

> ### ⚠ NON LANCIARLO DENTRO UNA PIPELINE POWERSHELL
>
> `.\Banco\banco.ps1 ... | Select-Object -First 25` **uccide il giro a meta'**.
> `Select-Object -First N` lancia `StopUpstreamCommandsException` appena ha N
> oggetti e **termina il comando a monte**: il banco muore, esce **255** — che
> non e' nessuno dei tre codici qui sopra — e **non scrive il resoconto**.
> Stesso esito con `Tee-Object` su un giro lungo, che sbatte contro
> «Periodo di timeout del semaforo scaduto».
>
> Costato due volte: un giro pieno morto a **8469 s** (644 prove passate, zero
> fallite) e un `-Gruppo casi` morto a **1132 s**. In tutti e due i casi **zero
> rossi**: e' tempo buttato, non informazione.
>
> **Il segnale che li distingue e' semplice: un giro vero scrive SEMPRE un file
> in `Banco\resoconto\`.** Se il resoconto non c'e', il giro non c'e' stato —
> qualunque cosa dica l'ultima riga a schermo.
>
> Si lancia **nudo**, e si legge dopo: `Banco\resoconto\ultimo.json`, oppure
> `Banco\ultimo-giro.txt` — che e' **UTF-16LE**, quindi un `grep` fatto a mano
> non ci trova niente e sembra che il caso non sia mai girato. *Un log che non
> si lascia leggere e' un log che fa concludere il falso.*

L'ultima riga del giro dice **«attese verificate N su M»**, sempre, anche quando
i due numeri combaciano. Se divergono, qualcosa sta misurando il nulla.

Lo svuotamento sta dentro il comando perche' non e' pulizia — vedi «Prima e dopo
ogni giro», piu' sotto: ogni lettura di stato scorre **tutti** i documenti
aperti, e sei di troppo fanno fallire «stato sotto i 16 KB» senza che il codice
sia cambiato.

### A mano, se serve

```
msbuild Banco\ProvaSaldatura.csproj -p:Configuration=Release
Banco\bin\ProvaSaldatura.exe --libreria Riscontro.Disegno\bin\Release\Riscontro.Disegno.dll
```

Senza `--libreria` cerca `Riscontro.Disegno.dll` accanto all'eseguibile. Esce 0
se non fallisce niente.

### Un'attesa che SPIEGA un rosso va messa PRIMA di quel rosso

Il banco ferma il caso alla **prima** attesa rossa (`CasiDichiarati.cs`:
`if (!ok) return false`). Non e' un difetto: un passo che ha gia' sbagliato non
ha piu' uno stato su cui misurare, e continuare produrrebbe rossi derivati che
sembrano difetti nuovi.

**La conseguenza sorprende, e il 18/9/2026 e' costata un giro.** Un caso aveva
un'attesa diagnostica — «di quale dei due guasti si tratta?» — scritta **dopo**
l'attesa che poteva fallire. Quella e' fallita, il caso si e' fermato, e la
diagnosi **non ha parlato**: nel resoconto c'era il rosso e non la sua ragione.

> *Un testimone messo in fila dietro l'imputato non testimonia.*

Quindi, quando un'attesa serve a spiegare perche' un'altra potrebbe fallire, va
messa **sopra**. E la prima di tutte e' sempre la stessa domanda: **ho potuto
guardare?** Un elenco vuoto perche' non si e' potuto leggere passa qualunque
`non_contiene`, e sembra una misura.

### Un gruppo solo, mentre si lavora

Un giro intero dura **da un'ora e tre quarti a due ore** — misurato il
18/9/2026: **6774 s** per 781 prove, e 7017 s per il solo gruppo `casi`.
Mentre si sta scrivendo qualcosa non si possono aspettare due ore per sapere di
aver sbagliato una riga:

> **Questa riga diceva «13-17 minuti» fino al 18/9/2026, ed era vera quando fu
> scritta: 88 prove.** Adesso sono 781, e nessuno aveva ricontrollato il numero
> mentre la suite cresceva — lo ha trovato un agente di ricognizione, notando
> che il resoconto e il LEGGIMI **non erano d'accordo**.
>
> E' lo stesso difetto che il LEGGIMI delle prove si e' gia' preso una volta
> («diceva 54 mentre ne giravano 113»), e la' e' stato chiuso con un controllo
> che rilegge il numero. **Qui il controllo non c'e'**: la durata non e'
> facilmente sorvegliabile, perche' cambia con la macchina.
> Quello che si puo' fare, ed e' scritto qui perche' valga: **il numero che conta
> sta in `resoconto\ultimo.json`, campo `durata_s`, e quello non invecchia** —
> questa riga e' un'indicazione, quel campo e' una misura.

```
Banco\bin\ProvaSaldatura.exe --gruppi                  quali gruppi ci sono
Banco\bin\ProvaSaldatura.exe --gruppo viste            solo quelle: 6,6 secondi
Banco\bin\ProvaSaldatura.exe --gruppo tabella,forma    piu' d'uno, separati da virgola
```

Un nome sbagliato viene **rifiutato prima di agganciare SOLIDWORKS**, e il
rifiuto elenca i nomi veri (uscita 2).

**Il giro intero resta il predefinito, ed e' quello che vale prima di
committare.** «Provo solo quella che ho cambiato» non funziona, e il 15/9/2026
l'ha dimostrato due volte: tre numeri aggiunti alle facce dei fori hanno fatto
sforare il tetto dei 16 KB **su un telaio, che fori non ne ha**, e il ritaglio
delle immagini ha cambiato un'asserzione sul peso del PNG.

> Il banco misura **cosa hai rotto**, non cosa hai fatto. Quello lo sai gia'.

Per questo un giro parziale **non puo' passare per un giro intero**: il
riepilogo — a schermo e nell'ultima riga di `in-corso.txt` — lo scrive a chiare
lettere, e dice quanti gruppi non ha lanciato.

```
GIRO PARZIALE (solo viste, 23 gruppi non lanciati) — 6 prove, 0 fallite
```

### I casi che sono file — `casi/lavorazioni/`

Un caso e' un JSON con **i numeri attesi accanto a ogni passo**, e si lancia con
`--gruppo casi`. Non serve una riga di C#:

```json
{ "nome": "guscio_interno", "famiglia": "lavorazioni",
  "perche": "«Outward» toglie verso l'esterno o verso l'interno?",
  "parte": { "nuova": true },
  "passi": [
    { "operazione": "guscio",
      "argomenti": { "facce_uid": ["@faccia(verso=davanti)"],
                     "spessore_mm": 5, "verso_esterno": false },
      "attese": [ { "cosa": "volume_mm3", "uguale": 82500 },
                  { "cosa": "ingombro_mm", "uguale": [100, 60, 40] },
                  { "cosa": "misure.verso", "uguale": "interno" } ] } ] }
```

| | |
|---|---|
| `cosa` | senza prefisso = lo misura il banco sul documento; `stato.…` = lo stato dell'add-in; `misure.…` = quello che l'operazione riporta |
| misure del banco | `volume_mm3`, `corpi`, `ingombro_mm`, `centri_x_mm`/`_y_`/`_z_`, `diametri_mm`, `centro_massa_mm`, `ultimo_piano_mm`, `spigoli_curvi`, `assi_creati`, `quante_lavorazioni`. **L'elenco non e' scritto da nessuna parte a mano**: e' l'elenco delle chiavi di chi le misura, e il messaggio «non so misurare …» lo legge da li' |
| `uguale` | un numero, un testo, un elenco di numeri (con tolleranza) o un elenco di TESTO (uno a uno, e l'ordine conta) |
| `contiene` / `non_contiene` | per le misure che sono **frasi** (`stato.forma`): una stringa o un elenco. Non si mettono insieme a `uguale` |
| `tolleranza` | **assoluta**, in mm o mm³. Diversa da zero → serve `perche_questa_tolleranza`, altrimenti il caso e' **rotto** |
| selettori | `@faccia(verso=…)`, `@faccia(piana=false)`, `@faccia(area~=907.92)`, `@spigolo(asse=Z,lungo~=100)`, `@spigolo(curvo=true,n=1)`, `@feature(tipo=…,n=2)`, `@quota(feature=Schizzo1,nome=D1)` |
| segnaposto | `{PROFILI}` la libreria dei profili saldati di questa installazione; `{LAVORO}` la cartella in cui un caso puo' SCRIVERE (`Banco\lavoro\`, ignorata da git). Un caso che nomina un percorso di questa macchina non e' una prova, e' un ricordo |
| `deve_fallire` | `il_messaggio_contiene` (obbligatorio), `volume_invariato`, `nessuna_feature_in_piu`, `gemella` |

### Un caso che gira su un ASSIEME — `casi/assiemi/` (voce GA0)

Al posto di `"parte": { "nuova": true }` un caso puo' chiedere un assieme, e il
banco se lo costruisce: ogni componente e' una parte fatta **con le stesse
operazioni dei casi**, salvata in `Banco\lavoro`, riaperta e inserita.

```json
"assieme": {
  "componenti": [
    { "nome": "base",
      "passi": [ { "operazione": "estrudi_rettangolo", "argomenti": { … } } ] },
    { "nome": "coperchio", "da_inserire": false,
      "posizione_mm": [0, 0, 30],
      "passi": [ … ] }
  ]
}
```

| | |
|---|---|
| `nome` | entra nel nome del file: `<caso>_<nome>.sldprt` in `Banco\lavoro`. Il caso lo nomina con `{LAVORO}\<caso>_<nome>.sldprt` |
| `posizione_mm` | dove va il **centro dell'ingombro**, non l'origine. Assente = nell'origine dell'assieme. **Non e' un accoppiamento**. `AddComponent5` posiziona il centro: un pezzo con l'origine su una faccia — qualunque cosa estrusa a una direzione sola — si ritrova l'origine spostata di meta' ingombro, e nello stato lo si rilegge come `origine_mm`. Chiedi z=30 su un pezzo spesso 4 e l'origine esce a `[0, -2, 30]` |
| `da_inserire` | assente o `true` = lo inserisce la preparazione. **`false` = lo costruisce e lo apre, ma NON lo inserisce**: serve per provare `inserisci_componente`, altrimenti si misurerebbe il lavoro della preparazione credendo di misurare l'operazione |

**`AddComponent5` vuole un documento gia' aperto**: la preparazione riapre
**tutti** i componenti, anche quelli che non inserisce. Su un file chiuso
l'inserimento risponde `null` senza dire perche'.

Alla fine i file vengono **cancellati**, e c'e' un controllo che lo verifica
(`--gruppo assieme-di-prova`): *un caso che riesce perche' un file del giro prima
era rimasto non e' un caso, e' un ricordo.*

Quello che non riesce durante la preparazione e' un **errore di banco** (uscita
2), non una prova rossa: il caso voleva misurare un assieme e l'assieme non e'
mai esistito — dirlo «fallito» manderebbe a cercare il difetto nell'operazione di
assieme, che non e' nemmeno stata chiamata.

**`non_contiene` non e' la comodita' simmetrica di `contiene`: e' il motivo per
cui esiste.** Una frase che dichiara «il profilo Ø30 per 40» su un pezzo che
quel profilo non ce l'ha piu' non fallisce niente — e' la descrizione giusta di
un ALTRO pezzo, e il modello ci costruisce sopra. Si prende solo pretendendo
che una parola **sparisca**.

**Un selettore che trova zero o piu' di una cosa rifiuta**, e non sceglie la
prima: un caso che gira su una faccia diversa da quella che il nome del file
promette puo' tornare lo stesso volume, e allora e' verde e non prova niente.
L'unica ambiguita' che si scioglie e' `n=` di `@feature`, dove l'ordine lo
decide chi scrive il caso.

Tutto quello che impedisce di **misurare** e' un errore di banco (uscita 2), non
una prova rossa: un campo che nello stato o nelle misure non c'e' (col messaggio
che dice quali c'erano), un caso senza `perche`, un passo **senza attese**, un
`deve_fallire` senza `il_messaggio_contiene`, una tolleranza senza il suo
perche', una `gemella` che non esiste, un selettore che non risolve.

Un `null` **dichiarato** invece si misura: `materiale: null` vuol dire «quel
posto esiste ed e' vuoto», ed e' una cosa diversa da un campo che manca.

### E quando SOLIDWORKS se ne va a meta' giro (uscita 4)

Tre documenti nuovi che non nascono **di fila** non sono tre prove rosse: sono
SOLIDWORKS che non c'e' piu'. Il banco se ne accorge, **salta** tutto il resto
dicendo per ognuno che non e' stato misurato, ed esce **4** — che vince sul 2
e sull'1, perche' un giro non fatto non e' un giro con errori.

«Di fila» e' la parola che conta: tre fallimenti sparsi in ottocento controlli
sono rumore normale, e una guardia che fermasse i giri sani verrebbe
disattivata dal primo che la vede fermarne uno.

Per **vederla scattare** — che e' l'unico modo di crederle — c'e'
`--fingi-senza-solidworks`: fa rispondere `null` a `NewDocument`, e in pochi
secondi si legge il giro che si interrompe da solo. La condizione vera capita
dopo ore e non si sa riprodurre a comando (voce K17, il 23/9/2026 e' costata
un'ora e mezza di giro e nove casi giusti dipinti di rosso).

Quello che **non** copre: un SOLIDWORKS che si pianta *dentro* una chiamata
invece di rispondere `null`. Li' non torna nessuna risposta da contare, e il
giro resta appeso — e' la voce K18, non fatta.

### Cosa il giro legge dalla RETE, e cosa no (voce K19)

Su questa postazione `S:` e' una condivisione di rete, e il modello di
documento predefinito sta li' sopra. **Ogni parte nuova sarebbe una lettura
SMB, e un giro ne fa ottocento.** Il 23/9/2026 tre giri di fila sono morti
proprio li': SOLIDWORKS si e' piantato mentre apriva il `.prtdot` e ha tenuto
la presa, e da quel momento **quel file non si leggeva piu' nemmeno da fuori**
— ne' per il giro dopo, ne' per il SOLIDWORKS dopo. Sembrava la rete: il server
rispondeva al ping in 0 ms e la cartella dei profili, sulla stessa
condivisione, si elencava senza esitare.

Dal 23/9/2026 il banco **copia il modello in locale una volta per giro** e lo
dice, col percorso di partenza e quello d'arrivo. Se la copia non riesce usa
quello di rete e **dice anche quello**: un banco che in silenzio usa un modello
diverso da quello della postazione sta misurando un'altra cosa.

**Restano letture di rete**, e vanno sapute perche' la prossima volta non si
cerchi di nuovo nel posto sbagliato:

```
i profili di carpenteria   la cartella che dice PROFILI_SALDATURA. Senza
                           la variabile il banco si ferma subito e lo dice:
                           non c'e' un percorso predefinito, perche' ogni
                           officina tiene la propria libreria dove vuole.
gli archivi dei materiali  dove li tiene SOLIDWORKS di suo
i casi e i binari          dove sta il clone, che puo' essere di rete
```

Quella tolta e' la **piu' frequente**, non l'unica.

### Le coppie — `casi/coppie/`

Ogni caso con `deve_fallire` **deve** nominare in `gemella` un caso che riesce,
e quella gemella dev'essere **verde nello stesso giro**:

> Una prova che verifica solo il rifiuto non distingue «il controllo funziona»
> da «il controllo rifiuta tutto».

Quattro coppie, tutte sullo stesso blocco 314 x 274 x 52:

| rifiuta | riesce | differiscono per |
|---|---|---|
| foro 500 passante | foro 270 su faccia 274 | `diametro_mm` |
| tasca 400x400 passante | la stessa, **cieca** da 5 | `passante` |
| foro a (5000, 5000) | foro 60 **col centro sul bordo** | `x_mm`, `y_mm` |
| asola 500x300 | asola 30x9 | `lunghezza_mm`, `larghezza_mm` |

Il banco dice **quanti argomenti differiscono** e rifiuta solo i due casi che
contano: una gemella identica al rifiuto (e' una copia) e una che non ha nemmeno
un argomento in comune (sono due operazioni che non si parlano).

### `-Regressione` — quanto reggono i riferimenti

```
.\Banco\banco.ps1 -Regressione
```

Diciannove pezzi in `casi/regressione/`, ognuno con `costruisci`, **una sola
`modifica`** e un `intento`. Il risultato e' **una di tre cose, non due**:

| | |
|---|---|
| **regge** | ricostruzione pulita, e la geometria sta dove l'intento dichiara |
| **si stacca e lo dice** | la ricostruzione fallisce o una lavorazione porta un errore: il progettista lo vede |
| **si stacca in silenzio** | ricostruzione a posto, nessun errore, **e la geometria e' altrove** |

La terza riga e' l'unico numero che conta, e si scrive **anche quando vale
zero**. Il dettaglio caso per caso finisce in `resoconto/regressione.md`, i tre
numeri nel README.

**L'`intento` e' obbligatorio**, e un caso che non lo dichiara e' un errore di
banco: un foro a 20 mm dal bordo, su una piastra che si allarga, deve restare a
20 dal bordo (`ancorato_a_bordo`) o dov'era (`ancorato_a_assoluto`)? Senza
saperlo non esiste risposta giusta.

Due campi in piu' rispetto ai casi normali:

* **`come_prima: true`** su un'attesa — l'invariante e' «com'era», e si misura
  prima della modifica. Calcolarlo a mano vorrebbe dire rifare un conto che
  SOLIDWORKS ha gia' fatto, e sbagliarlo creerebbe un falso silenzio;
* **`centro_massa_mm`** fra i `cosa` — l'invariante di posizione per cio' che
  fori non ne lascia. Il volume da solo non distingue una tasca ferma da una
  tasca che ha seguito il bordo.

### `-Muta` — ogni caso deve poter diventare rosso

```
.\Banco\banco.ps1 -Muta
```

Perturba **il piano, non il codice**: un argomento alla volta moltiplicato per
1,5 (booleani invertiti), il caso rieseguito, e si **pretende che diventi
rosso**. Un caso che resta verde sotto ogni perturbazione e' **cieco**.

E' l'unico modo meccanico di imporre che una prova sia gia' fallita almeno una
volta: un campo scritto a mano e' una dichiarazione, e una dichiarazione si
copia dal caso accanto.

Costa **un documento di SOLIDWORKS per mutazione** — 67 mutazioni, ~14 minuti —
quindi si lancia prima di una consegna, non ogni sera. Scrive
`Banco/resoconto/mutazioni.md`, una riga per caso, e ha un riepilogo suo
(`MUTAZIONI — 11 casi, 0 ciechi`): i rossi che produce sono **cercati**.

Le mutazioni su argomenti che l'operazione **ignora** (`profondita_mm` su un
taglio passante) sono saltate leggendo il catalogo, non indovinate; e si mutano
solo i passi **con attese**.

### La libreria di allora — `Controlli\libreria-di-allora.ps1`

Una prova custodisce davvero il suo difetto solo se, lanciata contro il codice
di **prima** della riparazione, diventa rossa.

```
.\Controlli\libreria-di-allora.ps1 0.70.0
.\Banco\banco.ps1 -Libreria <il percorso stampato> -Gruppo casi -SaltaCompilazione
.\Controlli\libreria-di-allora.ps1 0.70.0 -Pulisci
```

La DLL non si conserva da nessuna parte: **si ricostruisce**, perche' in questo
repository un commit e' una versione (`git log` su `AssemblyInfo.cs`, una riga
per numero).

Un caso puo' dichiarare **`rossa_prima_di: "0.67.0"`**: su una libreria piu'
vecchia di cosi', quel caso **deve** fallire. Se passa, non stava custodendo
niente.

**Il confronto storico comincia dalla 0.52.0.** Prima di allora le operazioni
stavano in `OpenPDM.SwAddin` con un altro spazio dei nomi: il banco non le
trova, lo dice ed esce 2 — «non e' un giro rosso, e' una libreria che il banco
non sa leggere».

### I giri si contano — `storico.txt`

`in-corso.txt` lo riscrive il giro dopo, e `ultimo-giro.txt` e' una redirezione
di chi lancia: **dei giri passati non restava niente.** Dalla voce F12 ogni giro
aggiunge una riga in coda a `Banco/storico.txt`, con le prove fallite **per
nome**:

```
2026-09-15 08:53  libreria 0.75.0.0  117 prove, 0 fallite, 1 SALTATE
2026-09-15 09:11  libreria 0.75.0.0  117 prove, 1 fallite  ->  spostamento nullo (deve fallire)
```

Serve a rispondere a una domanda sola, che senza questo file non ha risposta:
**«quante volte e' successo a quella?»** — perche' una prova che fallisce a caso
e che nessuno contabilizza smette di essere una prova: la volta che fallira' per
un motivo vero, verra' archiviata come «quella che ogni tanto salta».

Non e' versionato: e' il registro di **questa** macchina.

**Le librerie di Dassault non stanno qui dentro.** Il csproj le referenzia con
`Private=False`, quindi non finiscono nemmeno in `bin\`; a tempo di esecuzione
si prende quella INSTALLATA sulla macchina, che e' per definizione la stessa che
sta girando. Se SOLIDWORKS e' in un posto non standard:
`set SW_INSTALL_PATH=D:\...\SOLIDWORKS` — **la radice, non `api\redist`**. Vale
per compilare e per far girare, e la legge anche `build.ps1`. Il vecchio
`SW_REDIST` (che voleva la sottocartella) resta letto dopo, per chi ce l'ha
gia'.

**Il nome dice meno di quello che fa.** Si chiamava cosi' quando provava solo le
saldature; adesso prova sezioni, materiali, telai, fori, stato e le operazioni
nuove. Il nome e' rimasto perche' cambiarlo costa piu' di quello che vale.

---

## `corri_piani.js` — il ponte

Manda una sequenza di piani al ponte MCP come farebbe un client, e stampa gli
esiti.

```
node Banco\corri_piani.js Banco\casi\ponte\c4r_primario.json
node Banco\guarda.js                  leggi lo stato senza mandare un piano
node Banco\prova_annulla.js           «annulla» (voce R1), coi suoi tre rossi
```

`prova_annulla.js` sta qui e non in `ProvaSaldatura` per una ragione precisa:
`annulla` **non e' nella libreria**. E' uno strumento del ponte, e la parte che
conta — il ricordo dell'ultimo piano, e i tre casi in cui si RIFIUTA — vive in
`Attrezzi`. Provarlo dalla libreria vorrebbe dire provare un'altra cosa.

### `prova_forma.js` — una MISURA, non un controllo

Non fa fallire nessun giro, e il codice di uscita e' sempre zero. Risponde a una
domanda che non ha un verde e un rosso ma **un numero**: quanti dei cinque pezzi
si ridisegnano partendo dalla sola frase che li descrive (voce V3).

Va rifatta in tre passi, e il secondo non lo fa una macchina:

```
node raccogli_viste.js        costruisce i cinque pezzi veri e salva lo stato,
                              che dentro ha il campo `forma`
                              -- poi si danno le cinque frasi, E NIENTE ALTRO,
                                 a un modello isolato, che scrive i piani in
                                 casi/forma/1..5.json
node prova_forma.js           esegue i piani e confronta volume e ingombro
```

**Il modello che scrive i piani non deve vedere `Banco/viste/`**: li' dentro ci
sono gli stati completi, cioe' le risposte. Se le vede, la misura non misura
piu' niente.

### `prova_sezione.js` — meta' automatica, meta' da guardare

Costruisce un guscio, ne scatta la vista intera e lo **spaccato**, e controlla
da sola le cose che si contano:

- la sezione esce ed e' un PNG;
- **e' diversa dalla vista intera** — il rosso vero, perche' una sezione che non
  seziona darebbe un'immagine identica e «la sezione esce» passerebbe lo stesso;
- dopo, volume, corpi e lavorazioni sono quelli di prima: **nessuna sezione
  rimasta accesa, nessun corpo tagliato**. E' il rischio che la voce V4 nomina.

L'altra meta' — «dalla sezione si LEGGE lo spessore» — la puo' dire solo
qualcuno che guarda. Le due immagini restano in `Banco/viste/`
(`v4-guscio-intero.png` e `v4-guscio-sezione.png`) proprio per quello.

Con `PONTE=...` si punta a un altro eseguibile: serve per provare quello
**installato** invece di quello appena compilato, che e' l'unico modo di sapere
se il pacchetto funziona davvero e non solo se compila.

### Prima e dopo ogni giro

```
Controlli\ChiudiScarti\bin\ChiudiScarti.exe --chiudi
Controlli\NuovaParte\bin\NuovaParte.exe
```

Non e' pulizia estetica: **ogni lettura di stato scorre tutti i documenti
aperti**, a 244 ms di `GetNext()` ciascuno. Con cinquanta parti di scarto
addosso, un piano da mezzo secondo di lavoro ne impiega ottantacinque.

---

## I casi

**I casi dichiarati** — quelli che `--gruppo casi` esegue e misura. L'elenco
delle famiglie sta in `Famiglie` in `CasiDichiarati.cs`: una cartella che c'e'
ma non e' nell'elenco non viene letta, e una nell'elenco che non c'e' e' un
errore di banco.

```
casi/lavorazioni/ 21   il guscio, e da che parte toglie;
                       la quota che fa uscire l'asola dal materiale, rifiutata
                       e rimessa com'era (voce E18 c), e la sua gemella;
                       il raccordo sui quattro angoli e il foro guidato che
                       segue la tabella ISO (Z-C6: prima solo in regressione);
                       l'ANCORA (voce E18): il foro a 20 dal bordo che segue
                       il bordo, la tasca centrata che segue la mezzeria, sul
                       davanti, sul fianco e sul dietro; otto rifiuti e lo
                       spigolo sul bordo di uno smusso; i fori guidati
                       ancorati a 20 dai bordi, e «tutte o nessuna»;
                       lo spessore sotto il raggio dei raccordi, rifiutato e
                       rimesso (il meccanismo di r05), e la gemella che ci sta
casi/coppie/       8   i rifiuti con la loro gemella
casi/asola/       18   ogni faccia, non una sola; lo schizzo quotato che si
                       corregge con modifica_quota, e l'ancora dritta e obliqua
                       (voce E18)
casi/smusso/       3   dove un raccordo toglierebbe un altro volume
casi/rivoluzione/  5   i solidi di rotazione, e il profilo a cavallo dell'asse
casi/piano/        5   DOVE finisce il piano, non la distanza chiesta
casi/specchia/     2   il foro deve comparire dall'altra parte
casi/ripeti/       8   la direzione, che e' il dubbio vero; l'asse riconosciuto
                       per identita', e rifiutato se si e' spostato (voce E4)
casi/asse/         2   l'asse dalla parete di un foro
casi/togliere/     5   sopprimere, riaccendere, cancellare
casi/frase/        2   la forma detta a parole, e quando tace
casi/verifica/     5   com'e' venuto: solido, chiuso, sano
casi/misura/      10   quanto pesa, in che scatola ci sta, quanto dista
casi/corpi/       14   le booleane, il raccordo variabile, il filetto cosmetico;
                       l'unione di corpi che non si toccano rifiutata (Z-C18)
casi/esporta/     10   STL, STEP e IGES: il file riletto byte per byte,
                       e il volume riaperto in SOLIDWORKS
casi/proprieta/   11   i due cassetti, e il codice che va in distinta
casi/testo/        6   incidere, e la scritta che esce dalla faccia
casi/telai/        1   i profili saldati, letti dalla libreria normata ({PROFILI})
casi/assiemi/      5   inserire non e' accoppiare; un vincolo solo non basta;
                       il coperchio rifatto tiene i vincoli e li rompe;
                       il coperchio DENTRO la base (GA10);
                       toccarsi non e' compenetrarsi (GA10, la gemella)
casi/tavola/      32   il primo caso che arriva in TAVOLA (voce GT0);
                       la tavola della piastra con fori e smusso, GUARDATA:
                       fori col richiamo e le posizioni, smusso 5 x 45, niente
                       quote ripetute fra viste (voce GT11);
                       foglio, isometrica e PDF di un foglio in fila (voce Z-C6);
                       due fogli e lo stesso diedro (voce GT1);
                       il cartiglio dice cosa vuole (voce GT3);
                       i contatori ci sono anche a zero (voce Z-E1);
                       la tavola si quota, e le quote dicono 120 e 10 (voce K10);
                       lo spigolo che non esiste (la gemella del rifiuto);
                       la nota finisce sul FOGLIO e non in una vista (voce E2-bis);
                       la nota fuori dal foglio (la gemella del rifiuto);
                       la sezione taglia il pezzo, e ha il TRATTEGGIO (voce GT4);
                       il taglio fuori dalla vista (la gemella del rifiuto);
                       il dettaglio INGRANDISCE, e il cerchio e' DIVENTATO un
                       dettaglio -- non il tratteggio, che su un dettaglio
                       vale zero (voce GT4, l'altra meta');
                       il cerchio piu' grande della vista (la gemella);
                       la tabella fori dice dove sono i fori, riletta contro il
                       pezzo, e quota_in_tavola lascia i fori alla tabella
                       (voce GT5); la tabella sulla vista di fianco, rifiutata;
                       datum, rugosita' e tolleranze geometriche attaccate agli
                       spigoli e fuori dal pezzo (voci GT6, GT8), coi quattro
                       rifiuti: lettera doppia, planarita' con un riferimento,
                       datum che non c'e', Ra fuori scala; la distinta di
                       taglio in tavola, una riga per lunghezza (voce GT2);
                       lo sviluppo della L in tavola misurato contro il pezzo,
                       e rifiutato su un pezzo pieno (voce GT13); il simbolo
                       di saldatura sul giunto della T, e rifiutato su un corpo
                       solo (voce GT7); il foro cieco con la sua profondita',
                       niente fori dalla vista di fianco, nessun testo di quota
                       accavallato (voce GT12); due sezioni con la stessa
                       lettera, rifiutata la seconda (voce Z-C13); le croci di
                       centro sui fori contate davvero, e i raccordi rifiutati
                       (voce W2.11); la distinta base di un assieme di due
                       pezzi con la bollatura (voce GT9); le quote della tavola
                       ritrovate nel testo del PDF (voce Z-C9); la boccola
                       tornita, dove il contorno Ø30 non e' un foro e la
                       profondita' del cieco si legge anche dalla geometria (GT12)
casi/saldature/    4   la distinta conta le barre, e i due numeri vengono da
                       due posti diversi; il taglio accorcia la barra davvero,
                       e fra barre che non si incontrano rifiuta; il cordone
                       va fra facce che si toccano ad angolo; il tappo chiude
                       il tubo dentro (voce Z-C6). I corpi per NOME, con
                       @corpo(nome=...): i numeri C1, C2 cambiano fra un giro
                       e l'altro
casi/estrusioni/   8   il tubo ha il buco; l'asola estrusa e' lunga quanto
                       l'ingombro, estremita' comprese, ed e' un poligono; la
                       ruota da 17 denti si fa, quella da 16 rifiuta; l'aletta
                       cresce dalla faccia verso fuori (voce Z-C6); il rialzo
                       tondo e rettangolare ancorati, e il triangolo rifiutato
                       (voce E18)
casi/lamiera/     12   la L si sviluppa lunga quanto il conto a mano; data al
                       contrario, i punti diventano le facce esterne; lo
                       sviluppo senza file rifiutato; il tratto piu' corto
                       delle pieghe rifiutato; la flangia a spigolo da 25
                       che allunga lo sviluppo di 21,712, e i suoi due
                       rifiuti — niente di dritto, spigolo non di bordo;
                       l'orlo aperto che torna col conto, il chiuso misurato,
                       e l'aperto senza gioco rifiutato; la piega da schizzo
                       che alza meta' lamiera (ingombro dal conto, sviluppo
                       invariato) e la piega a 180 rifiutata (voce GP18)
casi/quote/        4   la piastra si quota da sola con DimXpert, coi valori
                       giusti; con due riferimenti rifiuta (voce Z-H3); la
                       tolleranza sulla quota del modello, H7 riscontrato sulla
                       ISO 286 e ±0,05, IT14 rifiutato (voce Z-F2); le quote del
                       modello arrivano in tavola, e senza viste rifiuta (Z-C1-bis)
casi/regressione/ 19   una quota a monte cambia: chi regge e chi si stacca (--regressione)
casi/ritrovare/    3   lo spigolo nominato con l'uid letto PRIMA di due fori si
                       ritrova per firma (stessa lunghezza, stesso punto) e il
                       baricentro dice quale angolo; accorciato da uno smusso
                       si rifiuta, mai il piu' vicino; doppio (due corpi che si
                       toccano) si rifiuta, «non scelgo io» (voce E19,
                       `uid_dal_passo`)
```

`-Famiglia` (voce E19) fa girare solo quelle cartelle dei casi dichiarati: il
gruppo `casi` da solo sono tre quarti d'ora, e un caso nuovo si vede fallire
PRIMA di scrivere il codice. Il giro e' parziale come con `-Gruppo`, e riscrive
`ultimo.json` come lui: si rimette con `git checkout` prima del giro intero.

`"uid_dal_passo": N` in un passo prende selettori e firme dallo stato di PRIMA
del passo N, come fa il ponte con un piano scritto guardando lo stato una volta.

**Gli altri** — piani per il ponte MCP e materiale di lavoro, non li esegue
`--gruppo casi`:

```
casi/limiti/    8   dove le operazioni si rompono
casi/tavola/    7   viste, quote, PDF  (+ i casi dichiarati, vedi sotto)
casi/viste/     5   pezzi di cui si sa tutto, per le prove sulle immagini
casi/ponte/   107   i piani del ponte MCP, per voce del TODO
```

**`casi/forma/` NON E' NEL REPOSITORY, ed e' voluto.** La cartella compare su
questa macchina e contiene i cinque piani che un modello isolato scrive durante
la misura V3 — li riscrive a ogni giro, e versionarli vorrebbe dire congelare
la risposta di **un** modello di **un** giorno come se fosse la misura. La
misura e' il numero, e sta nel README (`.gitignore`, riga 59).

*Questa riga prima la contava insieme alle altre, «casi/forma/ 5», e il conto
era vero solo qui:* su un clone pulito quella cartella non esiste, e il
controllo di contratto «ogni cartella nominata nel LEGGIMI esiste» diventava
rosso per chiunque tranne che per chi l'aveva scritta. **Un documento che
descrive la macchina di chi lo scrive invece del repository e' il difetto che
si scopre solo dal di fuori** — e dal di fuori lo scopre uno sconosciuto, al
primo tentativo.

**`casi/tavola/` contiene tutt'e due le cose, dal 17/9/2026** (voce GT0), ed e'
l'unica cartella cosi'. I sette `tav_*.json` sono piani; i casi dichiarati della
famiglia `tavola` stanno li' accanto. **Non si distinguono dal nome, si
distinguono dalla FORMA**: un piano e' un array di passi, un caso e' un oggetto
con `nome`, `perche` e `passi`. Il caricatore guarda la radice del JSON, salta
gli array, e **dice a ogni giro quanti ne ha saltati e quali**:

```
OK   i piani a mano si riconoscono e si saltano   7 saltati (sono array, non casi):
     tav_1_salva_pezzo.json, tav_2_crea_disegno.json, tav_3_foglio.json, ...
```

Un file **illeggibile** non conta come piano: resta un errore di banco col motivo
giusto. *Un riconoscimento che inghiotte anche i guasti smette di riconoscere.*

E attenzione al segnaposto, che **non e' lo stesso nelle due forme**: i casi
dichiarati usano `{LAVORO}` e `{PROFILI}` (li risolve `Banco\Attese.cs`), i piani
usano `{BANCO}` (lo risolve `corri_piani.js`). Scambiarli non da' nessun errore:
il percorso resta letterale, e il caso si lamenta che un file non esiste.

I nomi in `casi/ponte/` cominciano con la voce che li ha fatti nascere —
`c4r_*` la ruota dentata, `d3_*` le quote sui tagli, `b8_*` i riferimenti fra
passi, `s_*` le sagome rifiutate. Chi riapre una voce trova i suoi casi
cercando per prefisso.

---

## Perche' stanno nel repository

Fino al 14/9/2026 stavano in una cartella temporanea di sessione, e il percorso
della libreria era una **costante** che puntava dentro quella cartella. Quando
le voci B4 e B5 hanno spostato le operazioni da `OpenPDM.SwAddin.Agente` a
`Riscontro.Disegno`, il banco ha smesso di partire — e **non se n'e' accorto
nessuno per tre giorni**, perche' viveva fuori dal repository e non lo lanciava
piu' niente.

> Una misura che si puo' rifare solo dalla macchina e dalla settimana in cui e'
> stata scritta non e' una misura: e' un aneddoto.
