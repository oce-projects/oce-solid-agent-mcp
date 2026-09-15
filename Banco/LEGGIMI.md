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

### Un gruppo solo, mentre si lavora

Un giro intero dura **13-17 minuti**. Mentre si sta scrivendo qualcosa non si
possono aspettare quindici minuti per sapere di aver sbagliato una riga:

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
| `uguale` | un numero, un elenco di numeri, o un testo |
| `contiene` / `non_contiene` | per le misure che sono **frasi** (`stato.forma`): una stringa o un elenco. Non si mettono insieme a `uguale` |
| `tolleranza` | **assoluta**, in mm o mm³. Diversa da zero → serve `perche_questa_tolleranza`, altrimenti il caso e' **rotto** |
| selettori | `@faccia(verso=…)`, `@faccia(piana=false)`, `@faccia(area~=907.92)`, `@spigolo(asse=Z,lungo~=100)`, `@spigolo(curvo=true,n=1)`, `@feature(tipo=…,n=2)`, `@quota(feature=Schizzo1,nome=D1)` |
| `deve_fallire` | `il_messaggio_contiene` (obbligatorio), `volume_invariato`, `nessuna_feature_in_piu`, `gemella` |

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

Sedici pezzi in `casi/regressione/`, ognuno con `costruisci`, **una sola
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
`set SW_REDIST=D:\...\api\redist`.

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
casi/lavorazioni/  2   il guscio, e da che parte toglie
casi/coppie/       8   i rifiuti con la loro gemella
casi/asola/       15   ogni faccia, non una sola
casi/smusso/       3   dove un raccordo toglierebbe un altro volume
casi/rivoluzione/  5   i solidi di rotazione, e il profilo a cavallo dell'asse
casi/piano/        3   DOVE finisce il piano, non la distanza chiesta
casi/specchia/     2   il foro deve comparire dall'altra parte
casi/ripeti/       3   la direzione, che e' il dubbio vero
casi/asse/         2   l'asse dalla parete di un foro
casi/togliere/     5   sopprimere, riaccendere, cancellare
casi/frase/        2   la forma detta a parole, e quando tace
casi/verifica/     3   com'e' venuto: solido, chiuso, sano
casi/regressione/ 16   una quota a monte cambia: chi regge e chi si stacca (--regressione)
```

**Gli altri** — piani per il ponte MCP e materiale di lavoro, non li esegue
`--gruppo casi`:

```
casi/limiti/    8   dove le operazioni si rompono
casi/tavola/    7   viste, quote, PDF
casi/pezzi/    10   pezzi interi: coperchio, vasca, cupola
casi/viste/     5   pezzi di cui si sa tutto, per le prove sulle immagini
casi/forma/     5   materiale per la forma a parole
casi/ponte/   107   i piani del ponte MCP, per voce del TODO
```

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
