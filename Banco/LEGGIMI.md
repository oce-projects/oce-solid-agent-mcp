# Il banco

Le prove che si fanno **dentro SOLIDWORKS**, contro la libreria vera.

Sono due banchi diversi, per due domande diverse:

| | domanda | serve SOLIDWORKS? |
|---|---|---|
| `ProvaSaldatura.exe` | le operazioni della libreria fanno il pezzo giusto? | si' |
| `corri_piani.js` | il ponte MCP esegue i piani come deve? | si' |
| `..\Controlli\Prova*.exe` | la matematica torna? | **no** |

I primi due misurano pezzi, il terzo misura formule. Il terzo si lancia in un
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
```

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
