# OCE Solid Agent

Un ponte MCP per **disegnare in SOLIDWORKS**: espone come strumenti trentasei
operazioni di disegno gia' misurate, cosi' che un modello qualunque — locale o
remoto — possa costruire un pezzo e, soprattutto, **guardare cos'e' venuto**.

Si installa con un file solo e non chiede altro che SOLIDWORKS.

## Perche' esiste

OpenPDM ha un assistente che disegna: un modello locale propone un piano, il
server lo valida, l'add-in lo esegue dentro SolidWorks, e un progettista
conferma prima che il pezzo esista. Quella catena e' il prodotto e resta il
prodotto.

Il problema e' stato sempre lo stesso: **misurare**. Ogni difetto vero di
quell'assistente e' venuto fuori disegnando davvero, e ogni giro di prova
costava un balletto di file fra due macchine. Con questo ponte un giro e' una
chiamata.

E c'e' una seconda ragione, meno ovvia e piu' importante. Il modello che gira
in officina e' piccolo. Finche' l'unico modo di provare il sistema e' passare
da lui, non si distingue **«mancano gli strumenti»** da **«il modello non ce la
fa»**. Con questo ponte si puo' far disegnare lo stesso pezzo a un modello
grande e a uno piccolo, sulla stessa strada e con gli stessi controlli: la
differenza che resta e' la risposta.

## Cosa NON e'

Non e' un modo per far disegnare a un modello in rete i pezzi dell'azienda. Il
prodotto resta l'assistente locale, offline, con il pannello di conferma. Qui
si misura e si sviluppa.

## Come funziona

Un'applicazione da riga di comando che parla **MCP su stdio** e chiama
`Riscontro.Disegno`, la libreria che contiene le operazioni. Non le riscrive:
sono le stesse che l'add-in di OpenPDM esegue quando un progettista preme
«conferma», cosi' cio' che si prova di qui e' esattamente cio' che avra' in
mano.

```
modello  <--MCP/stdio-->  oce-solid-agent-mcp.exe  ---->  Riscontro.Disegno.dll
                                                             |
                                                         SolidWorks
```

Fino alla 0.5.0 la libreria era `OpenPDM.SwAddin.dll` — l'add-in intero,
caricato per riflessione — e il ponte si portava dietro tutto il PDM per fare
un foro. Dalla 0.6.0 le operazioni stanno per conto loro; dalla 0.7.0
l'installatore se le porta dentro.

## Gli strumenti

Cinque, non trentasei. Le operazioni di disegno sono trentasei, ma esporne
una per strumento costerebbe decine di migliaia di caratteri di schemi in
ogni richiesta — e, soprattutto, renderebbe facile saltare il primo passo.

| strumento | a cosa serve |
|---|---|
| `guarda_il_pezzo` | lo stato del documento aperto: ingombro, volume, corpi, lavorazioni con le loro quote, facce e spigoli con il loro identificativo. Con `viste` torna anche una **fotografia** del pezzo |
| `elenco_operazioni` | l'elenco corto per famiglia: un nome e una riga |
| `descrivi_operazione` | lo schema completo di una |
| `disegna` | esegue una sequenza e **riporta cos'e' venuto**: le misure di ogni passo e lo stato dopo |
| `annulla` | ritira l'ultimo piano, tutto intero — ma solo se il pezzo e' ancora come `disegna` l'aveva lasciato |

**`guarda_il_pezzo` non e' un accessorio.** Gli identificativi che gli altri
strumenti vogliono — `F1`, `E4`, `Corpo1` — nascono li' e valgono per quella
lettura: sono POSIZIONALI, si rinumerano per area a ogni lettura, e dopo
ogni modifica vanno riletti. Chi salta il primo strumento lavora alla cieca,
ed e' il difetto piu' caro di tutto il progetto.

**Misurare e vedere non sono la stessa cosa.** Dalla 0.31.0 `guarda_il_pezzo`
accetta `viste` — per esempio `["isometrica"]` — e risponde con una o piu'
immagini PNG accanto allo stato.

**Quanto serve, misurato.** Cinque pezzi di forma diversa (piastra forata,
scatola svuotata, staffa a L, flangia con corona di fori, albero a gradini),
quattro modelli isolati, criterio scritto prima di guardare le risposte:

| il modello riceve | dice che oggetto e' | dice le lavorazioni | dice le quote | **numeri sbagliati** |
|---|---|---|---|---|
| solo l'isometrica | **5/5** | 3/5 | 0/5 | **0** |
| quattro viste | **5/5** | 4/5 | 0/5 | **0** |
| solo lo stato | **5/5** | **5/5** | **5/5** | **3** |
| stato + isometrica | **5/5** | **5/5** | **5/5** | **1** |

Due cose che non ci aspettavamo. **Il solo stato testuale basta a capire che
oggetto e', 5 volte su 5** — «scatola a pareti sottili aperta su un lato»,
«albero di rivoluzione a gradini» — quindi il ponte non era cieco come
sembrava. E **un'immagine non fa inventare numeri**: dieci risposte su dieci
hanno scritto «non stimabile, nessun riferimento di scala» invece di tirare a
indovinare, dando pero' proporzioni giuste (rapporto lati 1,6-1,7:1 contro
1,67 vero).

Un modello, un giro per condizione. Su «numeri sbagliati» **3 contro 1 e' un
solo giro**, e un solo giro non e' una tendenza.

**Al massimo tre, e solo se le si chiede.** Una vista costa piu' di dieci volte
l'intero stato scritto, che sta sotto i 16.000 caratteri: portarne tre a ogni
lettura cambierebbe di un ordine di grandezza il costo del ponte, e lo
pagherebbe anche chi voleva solo sapere quanti corpi ci sono.

Fotografare **non tocca la vista di chi lavora**: l'orientamento si salva prima
e si rimette dopo, anche quando la fotografia fallisce a meta'.

**E si puo' tornare indietro.** Fino alla 0.31.0 le trentasei operazioni erano
tutte additive o parametriche: l'albero poteva solo crescere, e chi si accorgeva
di aver sbagliato doveva costruirci sopra o rifare il pezzo da capo. `annulla`
ritira l'ultimo piano tutto intero — `disegna` lo esegue gia' dentro **una sola**
voce di annullamento.

**Si rifiuta se il pezzo e' cambiato dopo.** Volume e numero di lavorazioni
devono essere ancora quelli che `disegna` aveva lasciato: se il progettista ha
lavorato a mano nel frattempo, annullare toglierebbe il suo lavoro, e il ponte
si ferma dicendo cosa e' cambiato.

## Come si mette in funzione

### Prima si compila

`bin\` sta nel `.gitignore`: dopo un clone quella cartella **non esiste**, e
l'eseguibile nominato qui sotto nemmeno.

```
.\build.ps1
```

Serve MSBuild (i **Build Tools for Visual Studio 2022**, gratis, bastano) e
SOLIDWORKS installato sulla macchina: le librerie di interoperabilita' non
stanno in questo repository e si prendono da li'.

Se SOLIDWORKS non e' nel posto di serie, la cartella si dice — ed e' la
**radice** dell'installazione, non `api\redist`:

```
.\build.ps1 -SwInstallPath 'D:\Programmi\SOLIDWORKS Corp\SOLIDWORKS'
```

Gli attrezzi di prova in `Controlli\` hanno uno script loro, e stanno fuori
dalla soluzione apposta — un progetto in piu' li farebbe ricompilare a ogni
build senza motivo:

```
.\Controlli\costruisci-controlli.ps1
```

Qui la cartella si chiama `-SwRedist` ed e' `api\redist`, non la radice: sono
due script diversi nati in due momenti diversi, e uniformarli e' una voce del
`TODO.md` e non una cosa da indovinare.

### Poi si collega

```jsonc
// nella configurazione del client MCP
{
  "mcpServers": {
    "oce-solid-agent": {
      "command": "C:\\percorso\\del\\clone\\bin\\oce-solid-agent-mcp.exe"
    }
  }
}
```

Prima di collegare un client conviene il giro secco:

```
bin\oce-solid-agent-mcp.exe --prova
```

Stampa su stderr se si e' agganciato, quale add-in ha caricato, quante
operazioni conosce e cosa vede nel documento aperto. Non tocca niente.

### Variabili d'ambiente

| | |
|---|---|
| `RISCONTRO_DISEGNO` | quale `Riscontro.Disegno.dll` ci si aspetta di usare. **Non la sceglie**: .NET carica quella accanto all'eseguibile. Serve a farsi dire se le due non coincidono |
| `OPENPDM_ADDIN` | il vecchio nome di `RISCONTRO_DISEGNO`, ancora letto |
| `OPENPDM_CATALOGO` | dove sta `catalogo.json`; se manca, accanto all'eseguibile |
| `SW_REDIST` | la cartella `api\redist` di SolidWorks, se non e' in `C:\Program Files\SOLIDWORKS Corp` |

`RISCONTRO_DISEGNO` non decide piu' quale libreria caricare, e resta letta
apposta: se punta altrove, il ponte lo **dice**. Una variabile d'ambiente che
non fa niente e' peggio di una che non esiste — chi l'ha impostata continua a
credere che conti.

Le librerie di interoperabilita' di Dassault **non stanno in questo
repository**: si prendono dall'installazione, cosi' la versione e' sempre
quella che sta girando.

## Il catalogo

`Strumenti/catalogo.json` e' una copia ESPORTATA dal server, non una
seconda scrittura: vedi [Strumenti/aggiorna.md](Strumenti/aggiorna.md).
Contiene solo le operazioni **misurate dentro SolidWorks**.

## Stato

Vedi [CHANGELOG.md](CHANGELOG.md). La versione sta in `VERSION.txt` e sale a
ogni modifica: e' una regola, non una consuetudine.

## Vincoli che non si negoziano

- **Serve SolidWorks aperto.** Il ponte si aggancia a una sessione che c'e',
  non ne avvia una.
- **Niente scritture fuori dal documento aperto.** Il ponte non salva nel
  vault, non tocca librerie, non installa niente.
- **Nessuna operazione non misurata.** Se un'operazione non e' stata costruita
  dentro SolidWorks e verificata con un numero, non si espone. E' la regola
  che regge tutto OpenPDM e vale anche qui.
