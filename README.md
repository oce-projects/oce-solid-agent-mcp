# OCE Solid Agent

Un ponte MCP per **disegnare in SOLIDWORKS**: espone come strumenti sessantatre
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

Sei, non sessantatre. Le operazioni di disegno sono sessantatre, ma esporne
una per strumento costerebbe decine di migliaia di caratteri di schemi in
ogni richiesta — e, soprattutto, renderebbe facile saltare il primo passo.
(Erano cinque fino alla 0.56.0: il sesto e' nato per una ragione precisa,
scritta sotto la tabella, non per coprire di piu'.)

| strumento | a cosa serve |
|---|---|
| `guarda_il_pezzo` | lo stato del documento aperto: ingombro, volume, corpi, lavorazioni con le loro quote, facce e spigoli con il loro identificativo. Con `viste` torna anche una **fotografia** del pezzo |
| `elenco_operazioni` | l'elenco corto per famiglia: un nome e una riga |
| `descrivi_operazione` | lo schema completo di una |
| `disegna` | esegue una sequenza e **riporta cos'e' venuto**: le misure di ogni passo e lo stato dopo |
| `misura` | le stesse operazioni, ma **solo quelle che leggono** (famiglia `verifiche`). Si dichiara di sola lettura, cosi' il client non chiede conferma per una misura |
| `annulla` | ritira l'ultimo piano, tutto intero — ma solo se il pezzo e' ancora come `disegna` l'aveva lasciato |

**`misura` esiste perche' le annotazioni stanno sullo strumento.** Il client MCP
legge `readOnlyHint` e `destructiveHint` in `tools/list`, cioe' **prima** che
esista una chiamata: con il solo `disegna` chiedeva conferma anche per
`proprieta_di_massa`, che non muove un millimetro cubo. Chi conferma dieci volte
di fila smette di leggere cosa conferma. Un'operazione che tocca il pezzo,
dentro `misura`, viene rifiutata **prima** che il piano parta — mai meta' piano.

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

**E la forma si puo' anche DIRE.** Lo stato porta un campo `forma` che riassume
il pezzo in una riga — 68-152 caratteri contro i 40-115 kB di un'immagine:

```
Ingombro 140 x 16 x 140 mm, volume 191938.7 mm3 (il 61% dell'ingombro),
1 foro Ø60 (centro 0,0,0), 6 fori Ø11 su una circonferenza Ø90 a passo 60 gradi.
```

Misurato con un criterio piu' duro di quello sopra: **dalla sola frase** un
modello isolato ha riscritto il piano di costruzione, e il pezzo rifatto e'
stato confrontato con l'originale.

| pezzo | volume rifatto contro originale |
|---|---|
| piastra forata | 353214,2 contro 353214,2 — **0,0%** |
| scatola svuotata | 109056 contro 109056 — **0,0%** |
| staffa a L | 105600 contro 105600 — **0,0%** |
| flangia con corona | 191938,7 contro 191938,7 — **0,0%** |
| albero a gradini | 55857,5 contro 55857,5 — **0,0%** |

**Cinque su cinque al volume esatto**, non «vicino».

Al primo giro erano quattro: l'albero usciva al 3,8%, perche' la frase diceva
«pezzo di rivoluzione» e nient'altro — il nome della famiglia senza una quota. E
il modello **l'aveva detto prima di provare**, indicando cosa mancava: i
diametri intermedi. Adesso la frase porta anche il profilo — *«Ø30 per 40, Ø20
per 30, Ø34 per 20»* — ricavato dall'area delle facce cilindriche.

**E lo dichiara solo se lo sa ricostruire.** La lunghezza di un tratto viene da
`area / (π·d)`, che vale su un cilindro intero: tagliato da una cava o da un
foro darebbe un numero corto, plausibile e falso. Il profilo si scrive solo
quando la somma delle lunghezze fa l'ingombro **e** il volume ricostruito fa il
volume vero. Altrimenti la frase tace, e resta «pezzo di rivoluzione»: poco, ma
vero.

**Al massimo tre, e solo se le si chiede.** Una vista costa piu' di dieci volte
l'intero stato scritto, che sta sotto i 16.000 caratteri: portarne tre a ogni
lettura cambierebbe di un ordine di grandezza il costo del ponte, e lo
pagherebbe anche chi voleva solo sapere quanti corpi ci sono.

Fotografare **non tocca la vista di chi lavora**: l'orientamento si salva prima
e si rimette dopo, anche quando la fotografia fallisce a meta'.

**E si puo' guardare dentro.** Con `sezione: {piano: "destro"}` il pezzo arriva
in **spaccato**: uno spaccato dice di un guscio piu' di dieci viste esterne —
dall'esterno una scatola svuotata e una piena sono lo stesso parallelepipedo, e
lo spessore del fondo non si legge.

Taglia **solo il disegno**, non la geometria: nessuna feature nell'albero,
nessun corpo diviso. E si toglie da sola appena scattata, come l'orientamento.

**E si puo' tornare indietro.** Fino alla 0.31.0 le operazioni erano tutte
additive o parametriche: l'albero poteva solo crescere, e chi si accorgeva di
aver sbagliato doveva costruirci sopra o rifare il pezzo da capo. `annulla`
ritira l'ultimo piano tutto intero — `disegna` lo esegue gia' dentro **una sola**
voce di annullamento.

**E si puo' togliere una lavorazione in mezzo**, non solo l'ultimo piano:
`sopprimi_lavorazione` la spegne (e `riattiva` la riaccende),
`elimina_lavorazione` la cancella. La seconda **si rifiuta quando qualcosa
dipende da quella lavorazione**, e dice cosa: cancellarla la porterebbe via
insieme ai suoi figli, e SOLIDWORKS lo fa senza chiedere.

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

Subito dopo il clone, una volta sola:

```
.\Controlli\installa-ganci.ps1
```

Accende il gancio `pre-push`, che lancia `controlla-pubblicabilita.ps1` e
**ferma il push** se dall'albero o dalla storia uscirebbe un percorso di questa
macchina, un nome utente o un indirizzo di rete privato. Git non versiona
`.git\hooks`: finche' non si lancia quella riga, il gancio sta nel repository e
non e' acceso. Per saltarlo una volta, sapendo di farlo: `git push --no-verify`.

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
| `RISCONTRO_CATALOGO` | dove sta `catalogo.json`; se manca, accanto all'eseguibile |
| `OPENPDM_CATALOGO` | il vecchio nome di `RISCONTRO_CATALOGO`, ancora letto |
| `SW_INSTALL_PATH` | dove e' installato SolidWorks: la **radice**, quella che finisce con `\SOLIDWORKS`. Vale sia per compilare che per far girare — da qui si ricava `api\redist` |
| `SW_REDIST` | il vecchio nome, ancora letto. Voleva la sottocartella `api\redist` e non la radice: se sono impostate tutte e due vince `SW_INSTALL_PATH` |
| `RISCONTRO_RADICE` | l'unica cartella dentro cui il ponte puo' scrivere. Se e' impostata, tutto il resto del disco e' fuori |
| `RISCONTRO_SOLA_LETTURA` | cartelle che il ponte non deve scrivere, separate da `;` — esempio `S:\CAD;S:\_LIBRERIE`. Si aggiungono a quelle di sistema, non le sostituiscono |
| `PROFILI_SALDATURA` | la libreria dei profili saldati (`.SLDLFP`), se non e' dove la mette questa installazione |

`RISCONTRO_DISEGNO` non decide piu' quale libreria caricare, e resta letta
apposta: se punta altrove, il ponte lo **dice**. Una variabile d'ambiente che
non fa niente e' peggio di una che non esiste — chi l'ha impostata continua a
credere che conti.

Le librerie di interoperabilita' di Dassault **non stanno in questo
repository**: si prendono dall'installazione, cosi' la versione e' sempre
quella che sta girando.

## La lingua, che non e' mezza per caso

Il repository sta passando all'inglese: commenti, messaggi, descrizioni e
documenti. **I NOMI no, e non e' lavoro lasciato a meta'.**

| cosa | lingua | perche' |
|---|---|---|
| nomi delle operazioni (`estrudi_rettangolo`, `profili_saldatura`) | **italiano, e restano** | sono **il protocollo**: cambiarli rompe i piani salvati, i 270 casi di prova e ogni client che li abbia gia' visti |
| nomi degli argomenti (`diametro_mm`, `facce_uid`) | **italiano, e restano** | idem |
| namespace, classi, file, metodi | **italiano, e restano** | il banco e le prove chiamano **per riflessione, per nome** (`Carica.Classe(asm, "Catalogo")`). Rinominare una classe rompe chi la chiama **senza che il compilatore dica niente** |
| commenti, messaggi di rifiuto, descrizioni del catalogo | → inglese | li legge chi lavora, e il modello |
| README, guide, `CONTRIBUTING` | → inglese | li legge chiunque |
| voci storiche del `CHANGELOG` | **italiano, e restano** | raccontano come si chiamavano le cose **allora**. *Un changelog riscritto e' un changelog che mente* |

Quindi un lettore inglese che apre `OperazioniSformo.cs` e trova un file dal
nome italiano **non ha trovato un residuo**: ha trovato un confine, e il confine
passa fra **cio' che qualcuno cerca per nome a runtime** e tutto il resto.

## Il catalogo

`Strumenti/catalogo.json` e' una copia ESPORTATA dal server, non una
seconda scrittura: vedi [Strumenti/aggiorna.md](Strumenti/aggiorna.md).
Contiene solo le operazioni **misurate dentro SolidWorks**.

## Stato

Vedi [CHANGELOG.md](CHANGELOG.md). La versione sta in `VERSION.txt` e sale a
ogni modifica: e' una regola, non una consuetudine.

### Quanto reggono i riferimenti quando il pezzo cambia

Sedici pezzi costruiti, **una sola quota a monte cambiata**, e la geometria a
valle rimisurata. Il risultato e' una di tre cose, non due:

| | casi su 16 | |
|---|---|---|
| **reggono** | **12** | ricostruzione pulita, e la geometria sta dove l'intento dichiara |
| **si staccano e lo dicono** | **0** | la ricostruzione fallisce o una lavorazione porta un errore: il progettista lo vede |
| **si staccano in silenzio** | **4** | ricostruzione a posto, nessun errore, e la geometria e' finita da un'altra parte |

*Misurato il 15/9/2026 su SOLIDWORKS 29.5.1, libreria 0.75.0. Si rifa' con
`.\Banco\banco.ps1 -Regressione`; il dettaglio caso per caso finisce in
`Banco/resoconto/regressione.md`.*

**Lo zero in mezzo e' il numero piu' interessante dei tre.** Nessuno dei
sedici casi si e' rotto in modo visibile: le quattro volte che il pezzo e'
uscito diverso da quello che il disegno dichiarava, SOLIDWORKS ha ricostruito
pulito e non ha detto niente. E' la ragione per cui questa suite misura **la
posizione della geometria** e non il fatto che la ricostruzione sia andata.

La terza riga si scrive **anche quando vale zero**: un conteggio che sparisce
quando e' zero e' un conteggio che nessuno controlla.

## Vincoli che non si negoziano

- **Serve SolidWorks aperto.** Il ponte si aggancia a una sessione che c'e',
  non ne avvia una.
- **Niente scritture fuori dal documento aperto.** Il ponte non salva nel
  vault, non tocca librerie, non installa niente.
- **Nessuna operazione non misurata.** Se un'operazione non e' stata costruita
  dentro SolidWorks e verificata con un numero, non si espone. E' la regola
  che regge tutto OpenPDM e vale anche qui.
