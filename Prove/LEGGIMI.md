# `Prove.Contratti` — le prove che girano dove SOLIDWORKS non c'e'

```
msbuild Prove\Prove.Contratti.csproj -p:Configuration=Release
Prove\bin\Prove.Contratti.exe
```

Esce **0** se non fallisce niente, **1** altrimenti, e scrive
`Prove\bin\prove.xml` in formato JUnit — quello che ogni CI sa leggere senza
che nessuno le insegni niente.

**353 controlli, qualche secondo, nessuna licenza.**

Quel numero non e' decorativo: **c'e' un controllo che lo rilegge da questo file
e lo confronta con quanti ne girano davvero**, ed e' l'ultimo del giro (si conta
anche da solo). Diceva `54` mentre ne giravano `113` — giusto quando fu scritto,
invecchiato da solo mentre la suite cresceva. Chi aggiunge un controllo cambia
anche questa riga, oppure il giro diventa rosso e gli dice di che numero si
tratta.

---

## Perche' un progetto a se', e non un modo del banco

Il csproj del banco risolve l'interop di Dassault con un `HintPath` dentro
`…\SOLIDWORKS\api\redist`: su una macchina senza SOLIDWORKS quel percorso non
esiste e **la compilazione fallisce**. Un flag non basta — il problema e' prima
del runtime.

Qui dentro **non c'e' nessun riferimento a `SolidWorks.Interop.*`**, e il primo
controllo del giro e' proprio quello: si guardano i riferimenti dell'assembly,
non il testo del csproj. Se qualcuno ne aggiunge uno «perche' serviva un tipo»,
su questa macchina non lo noterebbe nessuno — qui SOLIDWORKS c'e', e compila lo
stesso.

Quel che serve dalla libreria e dal ponte si prende per **riflessione** o
parlandoci **su stdio**, come farebbe un cliente qualunque.

## Cosa prova

| gruppo | |
|---|---|
| **i sorgenti stanno nei progetti** | ogni `.cs` di `Riscontro.Disegno`, `Ponte`, `Strumenti`, `Mcp`, `Banco`, `Prove` e `Installer` compare fra i `<Compile Include>` del suo `.csproj`. **Un file che non c'e' non viene compilato**, e se nessuno lo referenzia ancora la build resta VERDE e il codice non esiste: il difetto che non da' errore, applicato al progetto invece che al pezzo |
| **il valutatore delle attese** | numero giusto/sbagliato, tolleranze assolute, campo assente, elenchi, indici, selettori. E' lo **stesso file** che compila il banco (`..\Banco\Attese.cs`), non una copia |
| **il catalogo** | ogni operazione ha nome, famiglia, sommario, descrizione, schema; ogni famiglia usata e' dichiarata; **nessun percorso di questa macchina**; `Simili()` che su «asole» trova «asola» |
| **`Sessione.StessoDocumento`** | la guardia contro il blocco estruso nel pezzo di qualcun altro, che fino al 15/9/2026 **non aveva nemmeno una prova** |
| **il protocollo MCP** | `initialize` rimanda la versione ricevuta, una notifica non riceve risposta, `tools/list` da' **sette** strumenti e ognuno porta un `title` di primo livello, le annotazioni dicono la verita' (`misura` di sola lettura, `disegna` distruttivo e non idempotente, `documento` ne' di sola lettura ne' idempotente), `misura` **rifiuta** `estrudi_rettangolo` nominando lo strumento con cui si fa, uno strumento sconosciuto da' `isError` **nel contenuto** e non un errore JSON-RPC **ed elenca tutti quelli che esistono**, JSON malformato da' `-32700` con id nullo, e **su stdout non c'e' niente che non sia JSON** |
| **il banco e' stato lanciato** | legge `Banco/resoconto/ultimo.json` e **rifiuta una consegna in cui nessuno ha lanciato il banco**: piu' di zero prove, nessuna rossa, non un giro parziale, la libreria di adesso, e **posteriore all'ultima modifica a `Riscontro.Disegno`** — dove «ultima modifica» vuol dire **anche i file sporchi**, non solo i commit |
| **e «ultima modifica» include l'albero di lavoro** | fino al 23/9/2026 questa riga chiedeva a `git log`, cioe' alla sola storia **committata**. Fra la modifica e il commit c'era una finestra in cui rispondeva *«non c'e' niente di non provato»* con 29 file modificati e 31 nuovi sul disco — e `build.ps1` costruisce da li', non da git. Misurato: `Operazioni.cs` toccato alle 19:18, ultimo giro alle 15:23, controllo **verde**. Il verdetto sta adesso in una funzione che prende due date, e si prova in quattro versi: sporco piu' nuovo vince, albero pulito non cambia niente, sporco piu' vecchio non sposta, e **«nessun commit non provato» non copre piu' un albero pieno** |
| **il ponte e il banco caricano la stessa libreria** | ce ne sono **due copie**: `Riscontro.Disegno\bin\Release\` (la misura il banco) e `bin\` (la carica il ponte, ce la mette `build.ps1`). Chi compila **il solo progetto** — la cosa piu' naturale mentre si itera — aggiorna la prima e non la seconda. Il 18/9/2026 sono divergute di **quattro versioni per nove ore**: il banco misurava 0.98.4, il ponte girava 0.98.1, e una riparazione e' stata provata tre volte dal ponte senza mai esserci dentro. Il sintomo era perfetto — «l'operazione non rifiuta» — e la causa non era nel codice |
| **il rifiuto delle quote dice ancora quello che serve** | finche' l'import delle quote e' rotto (misurato il 18/9: sette quote **tutte marcate**, cinque strade di import, zero in tavola, e `InsertNote` che nello stesso istante funziona) **quel messaggio e' tutto cio' che un modello riceve quando chiede le quote**. Si pretende che nomini ancora quante sono marcate — il dato che divide in due le cause — e l'elenco delle strade provate. **Guarda il sorgente, non l'esecuzione, e lo dice**: la prova di comportamento sarebbe un caso con `deve_fallire`, e oggi non si puo' scrivere perche' il banco pretende una gemella VERDE e per `quote_dal_modello` non esiste. Visto scattare il 18/9 togliendo una frase e rimettendola |
| **nel TODO due voci non si chiamano allo stesso modo** | il 17/9/2026 sono state innestate 65 voci chiamate `A1`, `C3`…: **37 di quei nomi erano gia' usati** nello stesso file da voci diverse, 33 delle quali chiuse. «Fai C3» portava a una voce verde del 14/9 che non c'entrava niente. Il doppione si accetta **solo** se una delle due e' una «traccia originale tenuta per riferimento» — li' il nome ripetuto dice qualcosa. Visto scattare il 18/9 su un doppione messo apposta, e tolto subito dopo |
| **il catalogo promette solo documenti che l'esecutore ammette** | i tipi ammessi si leggono **dal sorgente** di `ControlliPreliminari` (i token `swDoc...` dentro `bool ammesso`), le famiglie da `bin\catalogo.json`, e ogni famiglia servita deve avere un tipo su cui possa girare. Nato il 18/9/2026 **rosso**: il catalogo elencava tre operazioni di famiglia `assiemi` e l'esecutore rifiutava ogni assieme prima del primo passo. **Il banco non poteva vederlo — chiama la libreria, non il ponte.** Una famiglia nuova che nessuno ha classificato e' **rossa, non verde** |
| **l'avanzamento e l'annullamento (W2.2)** | il gettone sta in `params._meta.progressToken` e **si rimanda com'è arrivato**: un `42` che tornasse `"42"` non combacerebbe più con niente dalla parte del client. Si pretende: chi non manda un gettone **non riceve niente** (una notifica non richiesta è rumore sul canale del protocollo); `progress` cresce sempre, e gliene si passa uno che non cresce per vedere che lo scarti; `total` e `message` si omettono invece di scriverli vuoti. La catena intera si guarda **su stdio col ponte vero**, sfruttando `prova: true` che gira a SOLIDWORKS chiuso. E `notifications/cancelled` **non cade più nel silenzio**: cadeva nel `default`, e siccome è una notifica l'errore veniva scartato |
| **il registro delle domande senza risposta** | il ponte sa, quattro volte in quattro punti, che gli e' stato chiesto un nome che non esiste — e finora lo diceva a chi chiedeva e a nessun altro. Ora lo scrive. Si pretende: la riga sta su una riga sola anche se il modello inventa un nome con le virgolette dentro (una riga JSONL rotta rende illeggibile il file **da li' in poi**, non solo se stessa); scrive in aggiunta, perche' due ponti aperti che riscrivono lo stesso file si mangiano le righe; **non solleva mai** e il motivo del silenzio resta leggibile, perche' un `catch` vuoto e un `catch` mai eseguito si assomigliano troppo; **non nomina `Console`**, dove passa il JSON-RPC; e **ogni** punto che cerca un nome vicino registra, cosi' il quinto punto nasce gia' sorvegliato invece di essere dimenticato |
| **le firme non sopravvivono al cambio di documento** | il ponte tiene `_guardato` (quale documento) e le firme (com'era). Una riga le faceva **divergere**: un piano che nominava un altro documento spostava il primo e lasciava indietro le seconde, e il guaio arrivava alla chiamata DOPO — o un rifiuto sbagliato, o un piano che passa su uid che non valgono piu'. La regola si prova prima su righe finte: prende il difetto vero, e **non punisce chi spiega** (venticinque righe di commento fra le firme e `_guardato`) — ci era gia' caduta una volta (voce Z-A1) |
| **le chiamate vietate per contratto** | tre firme `AddDimension` quasi identiche coesistono, e due la voce GP6 le **vieta**: quella su `IModelDoc2` ritorna `Boolean` e la quota, se nasce, non e' raggiungibile. Fino al 23/9/2026 il divieto viveva **solo in una riga di TODO**. Adesso si guarda il sorgente — 30.048 righe in cinque cartelle — e si prova che la regola sappia prendere le vietate **e lasciar stare quella giusta** (voce GP6) |
| **la conferma all'utente** | il client finto **dichiara `elicitation`**, il ponte gli manda `elicitation/create` prima di eliminare una lavorazione, il client risponde `decline` — e il ponte **si ferma dicendo di non aver toccato niente**. Piu' il ripiego: con un client che non sa chiedere non resta appeso (voce W2.5) |
| **la prova a vuoto** | `disegna` con `prova: true` **parla col ponte vero, su stdio, con SOLIDWORKS chiuso**: su un piano sbagliato in quattro modi trova sei difetti — l'operazione che non esiste, l'obbligatorio che manca, l'argomento **sconosciuto che verrebbe ignorato in silenzio**, il rimando a un passo che non c'e'. E su un piano buono non trova niente. In tutti e due i casi **dichiara cosa non ha potuto guardare** (voce Z-G6) |
| **la sentinella: SOLIDWORKS se n'e' andato** | tre documenti nuovi non nati **di fila** fermano il giro, che salta il resto **dicendolo** ed esce 4. Due fallimenti non bastano, tre sparsi nemmeno; una volta scattata non si spegne. Il 23/9/2026 il banco aveva provato per quaranta minuti contro un programma morto, chiamando **rosse** nove prove che non aveva fatto (voce K17) |
| **gli errori non si confrontano con una cifra** | su 38 enum di errore di SOLIDWORKS, **13 hanno «niente errore» = 1**: `if (errore != 0)` li' e' rovesciato. La guardia dimostra di avere i denti su quattro righe che deve prendere e quattro che deve lasciar stare, **prima** di dire che i sorgenti sono puliti (voce Z-C7) |
| **i limiti misurati e le descrizioni che li dichiarano** | ogni rifiuto ha la sua gemella e la gemella **esiste**; ogni `deve_fallire` pretende un messaggio; e **ogni operazione con un rifiuto provato lo dichiara nella descrizione** — erano 12 mute su 31, adesso zero (voci Z-G5, Z-C11) |
| **i rifiuti che dicono anche cosa fare** | 576 punti di rifiuto, 537 con un messaggio proprio, **83 offrono una strada**. E' un pavimento: un rifiuto nuovo scritto muto lo fa scendere (voce Z-G4) |
| **su che documento gira ogni operazione** | tutte e 71 lo dichiarano, con un vocabolario chiuso, e **cio' che dichiarano dev'essere eseguibile** — i tipi ammessi si leggono dal sorgente dell'esecutore. Il controllo verifica anche che il campo **servisse**: se nessuna famiglia fosse mista, sarebbe un doppione (voce Z-D0-ter) |
| **ogni campo obbligatorio e' una decisione presa** | 92 campi, 58 quote che si chiedono davvero, **34 con una decisione scritta**. «Nessuno ci ha pensato» e «qualcuno ha deciso di chiederlo» si leggono uguali nel JSON, e solo una delle due si puo' migliorare (voce Z-G3) |
| **gli esempi si copiano da un caso verde** | 24 operazioni su 71 portano una chiamata vera. **Nessun esempio contiene `@faccia(...)` o `{LAVORO}`**: un esempio che il client non puo' copiare e' una trappola, non un aiuto. E ogni campo dell'esempio esiste nello schema — l'esempio non lo esegue nessuno, quindi invecchia in silenzio (voce Z-G1) |
| **cosa accompagna una fotografia** | la didascalia dice che **i numeri letti da un'immagine non sono autorevoli**, con i due errori misurati (Ø90 letto 79; parete 12,5 letta 6) e la strada per la misura vera (voce Z-C10) |
| **il documento si tiene per mano** | le letture di `_sw.IActiveDoc2` in `EsecutorePiano.cs` sono **cinque e non di piu'**, e i controlli preliminari lo **ricevono** invece di ripescarlo: erano due letture dello stesso globale con una finestra in mezzo (voce Z-C5) |

L'ultima riga e' la ragione per cui esiste la CI (voce F9): nessun runner ha una
licenza SOLIDWORKS e non l'avra', ma **rifiutare una consegna non provata** si
puo' fare da qualunque macchina. Per questo `ultimo.json` e' l'unico file di
`Banco/resoconto/` versionato.

> **Il valutatore delle attese DEVE stare qui.** Se e' rotto lui, tutta la suite
> con SOLIDWORKS diventa verde gratis.

## Due trappole, e come si evitano

**Il timeout non e' una comodita'.** Leggere da un sottoprocesso senza bloccarsi
vuole un timeout su ogni riga attesa: senza, una prova che fallisce **appende il
comando** invece di diventare rossa, e in una CI vuol dire un lavoro che non
finisce mai invece di un rosso che si legge.

**E il difetto silenzioso e' l'altro**: una prova di protocollo che «passa»
perche' ha letto **zero righe in attesa di zero righe**. Per questo «una notifica
non riceve risposta» non si misura con un'attesa a vuoto — si manda la notifica,
subito dopo una domanda vera, e si guarda che la **prima** riga che torna sia la
risposta a quella.

## Il BOM che ha rotto la prima riga

Appena scritto, questo gruppo ha trovato un difetto **suo**: `initialize` non
riceveva risposta, e il ponte rispondeva `-32700: Primitiva JSON non valida`.

`Process.StandardInput`, su .NET Framework, nasce con la codifica di
`Console.InputEncoding` e gli si mette subito `AutoFlush = true` — che **fa un
Flush**, e un Flush di uno `StreamWriter` UTF-8 scrive il preambolo: tre byte,
`EF BB BF`, davanti alla prima riga.

> Quello che arrivava al ponte non era JSON: erano tre byte e poi del JSON.

Il sintomo pero' diceva un'altra cosa — «initialize non risponde» — perche' la
riga d'errore non porta l'id della richiesta. Si e' visto solo **stampando le
righe arrivate** dentro il messaggio di fallimento, ed e' il motivo per cui il
dettaglio di una prova rossa qui non viene mai troncato.

E' la trappola gemella di quella che questo gruppo prova sul ponte: **su stdio,
il primo byte conta.**
