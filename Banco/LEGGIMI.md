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

```
casi/limiti/    8   dove le operazioni si rompono
casi/tavola/    7   viste, quote, PDF
casi/pezzi/    10   pezzi interi: coperchio, vasca, cupola
casi/ponte/    87   i piani del ponte MCP, per voce del TODO
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
