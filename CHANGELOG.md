# SolidWorks MCP — versioni

**REGOLA, non consuetudine: ogni modifica al codice alza la versione in
`VERSION.txt` e aggiunge una voce qui.**

Perche' e' una regola: questo ponte serve a MISURARE, e una misura senza il
numero di versione di cio' che l'ha prodotta non e' una misura — e' un
aneddoto. Se fra un mese una prova dice una cosa diversa da oggi, la prima
domanda sara' «con quale versione?», e deve avere una risposta.

Criterio (semver ridotto MAJOR.MINOR.PATCH):

- **MINOR +1** quando il ponte guadagna strumenti o capacita' visibili;
- **PATCH +1** per correzioni;
- **MAJOR** resta 0 finche' il protocollo degli strumenti non e' stabile.

---

## 0.31.0 — 14-15 settembre 2026 · libreria 0.66.0

**Il modello non aveva mai visto il pezzo, e non poteva tornare indietro.**

Due meta' dello stesso goal — *proponi, fai, **guarda**, **correggi*** — e nessuna
delle due era mai entrata nel TODO.

### R1 — `annulla`: il quinto strumento

Questo ponte diceva **ventuno volte** al modello «annullalo con Ctrl+Z». Il
destinatario di quei messaggi e' un modello linguistico, che non ha una
tastiera: era una riparazione offerta a qualcuno che non poteva prenderla.

E le trentasei operazioni sono tutte additive o parametriche. L'unica correzione
possibile era `modifica_quota` — si cambia un numero, non si ritira una scelta.
**L'albero poteva solo crescere:** chi si accorgeva di aver sbagliato aveva due
strade, tutte e due sbagliate — costruirci sopra, o rifare il pezzo da capo, che
e' proprio cio' che il goal esclude.

**La macchina c'era gia'.** `EsecutorePiano` apre e chiude una voce di
annullamento intorno a ogni piano (voce B8, misurata: «un solo Ctrl+Z riporta il
documento al valore di `volume_prima_mm3`»), e `Controlli\Annulla` prova che
funziona. Si azionava solo con una tastiera umana. Stessa forma della sezione V:
la capacita' c'e', e non arriva a chi legge.

**Un QUINTO strumento e non un'operazione del catalogo:** le operazioni si
eseguono dentro `disegna`, cioe' dentro un piano, e annullare un piano da dentro
il piano stesso e' un nodo. Il motivo per cui gli strumenti non sono trentasei —
non esporre le operazioni una per una — non riguarda un gesto che operazione non
e'.

#### Il rifiuto viene prima

Fra il piano e la richiesta di annullamento il progettista puo' aver lavorato a
mano, e premere annulla allora gli toglie il **suo** lavoro. Quindi `disegna`
lascia un ricordo — documento, volume e numero di lavorazioni prima e dopo — e
`annulla` lo riconfronta. Tre rifiuti, ognuno col suo caso rosso in
`Banco/prova_annulla.js`:

| caso | cosa fa |
|---|---|
| nessun piano eseguito | rifiuta: non saprebbe nemmeno dire cosa toglierebbe |
| chiamato due volte | il secondo rifiuta: il ricordo si consuma al primo, e il passo successivo toglierebbe quello che c'era **prima** del piano |
| un altro documento davanti | rifiuta dicendo quale aveva in mano e quale c'e' adesso |

**Il ricordo si scrive anche quando il piano e' fallito a meta'**, ed e' il caso
che conta di piu': un piano fermato al passo 3 lascia i primi due costruiti, e
sono proprio quelli che si vuole ritirare. Si **cancella** invece quando il pezzo
non e' cambiato — un piano che non ha costruito niente non ha aperto nessuna
voce di annullamento, e ricordarlo vorrebbe dire offrire di disfare qualcosa di
precedente.

#### Due numeri non sono una prova, e si controlla anche dopo

Volume e conteggio non vedono un gesto che non cambia ne' l'uno ne' l'altro — un
nome cambiato, una configurazione. Non esiste nessuna API per guardare la pila
degli annullamenti prima di premere (`IModelDocExtension` ha solo
`StartRecordingUndoObject` / `FinishRecordingUndoObject`). Quindi si preme, si
**controlla**, e se il pezzo non e' tornato dov'era si **rimette** con
`EditRedo2` e si dice cos'e' successo. Provare, controllare, e disfare la prova
sbagliata e' l'unica strada onesta che c'e'.

`QuanteFeature` somma `feature_non_elencate`: senza, il conteggio si fermerebbe
a ottanta e direbbe «non e' cambiato niente» anche dopo dieci fori nuovi — il
difetto di E12 ripetuto dall'altra parte.

### R3 — le frasi non nominano piu' un tasto

Tredici messaggi riscritti; **otto commenti lasciati com'erano**, perche' li'
chi legge e' una persona e per una persona `Ctrl+Z` e' il gesto giusto.

La frase adesso sta in `Riscontro.Disegno/Frasi.cs`, una volta sola. Era scritta
ventuno volte con parole quasi uguali, e il giorno in cui e' cambiata ha
presentato il conto: ventuno posti da correggere.

E dice **due** cose:

```
«annulla» se stai parlando col ponte, Ctrl+Z se sei davanti allo schermo
```

Questa libreria ha due lettori. Il modello, che arriva dal ponte e non ha una
tastiera. E la persona, che usa le stesse operazioni dall'add-in di OpenPDM,
dove `annulla` **non esiste**: nominare solo lo strumento l'avrebbe mandata a
cercare nel menu un comando che non c'e' — lo stesso difetto di questa voce, coi
destinatari scambiati.

### Il banco dice DOVE, non solo cosa

`Perche(ex)` al posto di `ex.Message` in diciotto punti: tipo dell'eccezione,
messaggio, prima riga di stack, e via l'involucro `TargetInvocationException`
che la riflessione mette intorno a tutto.

Il motivo e' costato un giro: una prova ha stampato «Riferimento a un oggetto
non impostato su un'istanza di oggetto.» e nient'altro. Quella frase dice che
qualcosa era null e non dice **cosa**, ne' se il null stesse nel banco o nella
libreria — e localizzarla voleva dire rilanciare diciassette minuti di prove.

### V1 — `guarda_il_pezzo` sa scattare una fotografia

### V1 — `guarda_il_pezzo` sa scattare una fotografia

Fino a qui, «guardare il pezzo» voleva dire ricevere un elenco: sessanta facce
con area, normale e verso. Per una piastra forata basta. Per capire che FORMA ha
un guscio con le alette, no — sessanta facce con un'area non sono una forma,
sono un elenco.

**La capacita' c'era ed era provata.** Il banco salva viste con `SaveBMP` da
mesi. Ma viveva nel banco, e `SaveBMP` non compariva **nemmeno una volta** nella
libreria: la cosa che serviva a chi legge stava nel programma che controlla se
la libreria funziona. E' la forma di difetto della voce E8 — una misura presa e
buttata via — con l'aggravante che qui non era mai stata collegata a niente.

Adesso:

- **`Riscontro.Disegno/VistaDelPezzo.cs`** (nuovo): `Scatta(modello, quale,
  larghezza, altezza, out motivo)` torna i byte di un **PNG**. Nove viste col
  nome che usa chi disegna, l'isometrica per prima perche' e' la sola che da
  sola mostri una forma.
- **`Mcp/Protocollo.cs`**: `ContenutoConImmagini` — MCP prevede contenuti di
  tipo `image` (base64 piu' tipo MIME) e questo protocollo sapeva fare solo
  `text`. Il testo va per primo: un'immagine senza didascalia, in mezzo a una
  conversazione, e' un indovinello.
- **`Ponte/Sessione.cs`**: `Fotografia(...)`, che e' dove sta la meta'
  difficile — sapere qual e' il documento davanti.
- **`Strumenti/Attrezzi.cs`**: `guarda_il_pezzo` guadagna `viste`, facoltativo,
  **al massimo tre**.

**SU RICHIESTA, MAI DI SERIE.** Una vista 1024x768 costa piu' di mille token:
piu' di dieci volte l'intero stato del documento, che sta sotto i 16.000
caratteri. Se ogni lettura ne portasse tre, il costo del ponte cambierebbe di un
ordine di grandezza — e lo pagherebbe anche chi voleva solo sapere quanti corpi
ci sono.

**PNG e non il BMP che SOLIDWORKS da'.** `SaveBMP` scrive non compresso: 1024x768
sono 2,3 MB, in base64 tre. Lo stesso fotogramma in PNG sta in poche decine di
KB, coi pixel identici.

**NON SI TOCCA LA VISTA DI CHI LAVORA.** Il ponte si aggancia al documento che
un progettista ha davanti. Girare il pezzo per fotografarlo e lasciarlo girato
vuol dire spostargli la roba sotto le mani. L'orientamento si salva prima e si
rimette nel `finally`, **anche quando la fotografia fallisce a meta'**: un danno
vero fatto da un'operazione di sola lettura.

#### E rimetterlo a posto e' stata la parte difficile: sei strade su nove non funzionano

| strada | esito |
|---|---|
| riassegnare `IModelView.Transform` | scarto **2598** sull'indice 12 (la scala) |
| `Transform` copiato con `IInverse()` due volte | idem |
| `IModelDoc2.ViewOrientationUndo()` | scarto 212 sull'indice 9 |
| terna `Orientation3`/`Translation3`/`Scale2`, ordine O → **T** → S | scarto **4,77** sull'indice 9 |
| **terna, ordine O → S → T** | **torna** |

**`Transform` non e' lo stato della vista:** e' la sua descrizione. Lo stato da
cui una vista si ricostruisce e' la terna — da dove guardi, dove sei centrato,
quanto sei vicino.

**E l'ordine non e' stile.** Con O → T → S la rotazione (indici 0-8 della
matrice) torna **esatta** e sbaglia solo la traslazione: `Translation3` vale per
la scala che c'e' in quel momento, quindi rimetterla prima di `Scale2` rimette
una posizione buona per un'altra inquadratura. Il pezzo usciva **dritto e
spostato**, che e' peggio di storto — sembra giusto.

Il numero l'ha detto la misura: e' stato «scarto 4,77 **sull'indice 9**» a
indicare la traslazione, dopo che tre spiegazioni ragionate erano risultate
false una dopo l'altra.

#### `Controlli\ProvaVista`, perche' tre secondi non sono diciassette minuti

Le prime tre strade sono state provate **dentro il banco**, cioe' pagando un
giro da diciassette minuti per ogni risposta. Adesso stanno in un attrezzo suo
che ne prova nove in tre secondi e stampa **di quanto** e **su quale numero**
ciascuna sbaglia — ed e' quel dettaglio, non il verde/rosso, che ha trovato la
risposta.

> Una domanda da tre secondi non si paga diciassette minuti. Se la si sta
> pagando, manca un attrezzo.

**LO STATO ESCE COMUNQUE.** Se nessuna fotografia riesce, `guarda_il_pezzo`
risponde lo stesso con l'elenco delle facce e dice nel testo perche' le viste
mancano. Fallire tutto perche' non si e' potuto scattare toglierebbe a chi legge
l'unica cosa che gli serviva.

**Tre spiegazioni ragionate, tutte e tre false.** Prima che la misura trovasse
l'ordine, il rosso «l'orientamento non torna» ha avuto tre diagnosi, ognuna
plausibile e ognuna smentita dal pezzo:

1. «`Transform` torna l'oggetto **vivo** della vista, quindi salvarlo non salva
   niente» — falsa: una misura scritta apposta ha mostrato che i suoi sedici
   numeri **non** cambiano da soli. E' una copia.
2. «allora e' `ViewOrientationUndo()`, che esiste apposta» — non torna.
3. «allora e' la terna invece di `Transform`» — giusta a meta': serviva anche
   l'ordine.

> Il verso di una riparazione non si ricava ragionando sull'officina: si chiede
> al pezzo. Qui il pezzo ha detto no tre volte prima di dire dove.

**Due errori del compilatore che valgono una riga ciascuno**, perche' erano
scritti contro un'API immaginata invece che letta:

- `ShowNamedView2` torna `void`. La prima scrittura la trattava come un `bool` e
  ci costruiva sopra un controllo — un controllo su un valore che non esiste.
- Il *settore* di `IModelView.Transform` vuole `MathTransform`, non
  `IMathTransform`. Il compilatore lo dice solo sulla riga che rimette a posto
  l'orientamento: cioe' sulla riga che non si puo' provare senza aprire
  SOLIDWORKS.

**La prova nel banco (`ProvaFotografia`), col suo caso ROSSO.** Quattro
affermazioni piu' una: esce qualcosa; **e' un PNG e non un BMP** (firma
`89 50 4E 47`); **l'orientamento torna com'era** (sedici numeri confrontati);
**una vista inventata viene rifiutata dicendo quali esistono** — senza
quest'ultima, «scatta sempre qualcosa» passerebbe anche se il nome non venisse
mai guardato. E in coda: nessun file lasciato nella temporanea, perche'
`SaveBMP` vuole un percorso e ogni scatto scrive 2 MB su disco.

Quando l'orientamento non si e' potuto leggere nemmeno PRIMA, la prova e'
**fallita**, non verde: un controllo senza ingresso non ha una risposta, e uno
che si arrende dicendo «ok» e' il difetto che questo progetto esiste per rendere
impossibile.

### E12 — il tetto sulle lavorazioni non nasconde piu' le ultime

`StatoDocumento` elencava al massimo ottanta lavorazioni e teneva **le prime**:
su un pezzo da ottantacinque, le cinque piu' recenti — cioe' quelle appena
fatte, le sole di cui si stia parlando — sparivano. Adesso si tengono le
ULTIME, e quante ne restano fuori si dice.

Il giro dell'albero passa da una lista di `Candidata` (feature, tipo, nome): il
taglio si fa **prima** di chiamare `DettaglioFeature`, cosi' le ottanta voci
costruite sono le ottanta che escono, e non ottantacinque di cui cinque buttate.

### `--lento`, e le prove saltate si contano

La prova di E12 costruisce ottantacinque tagli. Ottantacinque tagli costano
almeno **mezz'ora** — contro i tredici minuti di tutte le altre insieme — e in
questo giro, alla lavorazione 84, SOLIDWORKS ha macinato un core al 100% per
**ottantotto minuti** senza finirne una. Una prova che pesa cosi' e' una prova
che prima o poi qualcuno salta.

Quindi si lancia solo con `--lento`. **Ma una prova saltata si deve vedere:** il
banco la stampa come `SALTATA` col motivo e la conta a parte. «88 prove, 0
fallite» su un giro in cui una non e' partita e' una frase vera che fa credere
una cosa falsa.

### Due difetti miei, trovati dalla revisione

**`NotaSulVolume` dava l'allarme su un foro perfetto.** La guardia diceva
`volumeDopo < 0`, e `VolumeMm3` non torna **mai** un negativo: risponde 0 quando
il documento non e' una parte, quando `GetBodies2` da' null, e dal `catch` che
avvolge tutto — cioe' su ogni strada di errore. Il ramo non poteva scattare, e
uno zero di lettura fallita passava come misura: `tolti` diventava l'intero
volume del pezzo, e su un foro riuscito usciva «192.000 mm³ invece di 392,7».
Un rifiuto di cio' che e' corretto, che questo progetto dichiara peggiore
dell'errore che stava chiudendo. Il commento due righe sopra diceva gia' la cosa
giusta: e' stata la riga a non farla.

**`guscio` con `facce_rif` poteva aprire meno facce di quelle chieste.** La
regola «tutte o nessuna» valeva sulla RISOLUZIONE e non sulla SELEZIONE: con tre
riferimenti risolti e due facce selezionate si andava avanti, «riuscita: true»,
volume calato, e un contenitore chiuso su un lato che doveva essere aperto. Il
ramo gemello, quindici righe piu' giu', rifiutava gia' alla prima faccia:
una riparazione che copre un ramo e lascia scoperto il suo gemello.

---

## 0.30.0 — 14 settembre 2026 · libreria 0.65.0

**Due voci, e tutte e due si sono rivelate diverse da come erano scritte.**

### E8 — il meccanismo aveva misurato lo spostamento e buttato via la misura

E8 erano **due voci in una**, con destini opposti.

**La tolleranza proporzionale non si fa.** Gliel'aveva attribuita E5 chiudendosi
(«l'ancora e' il numero che rende possibile una tolleranza proporzionale, che e'
la voce E8»). Ma dopo E6 il ritrovamento funziona per persist ID, e l'ancora
**non la legge nessuno**: `Attrezzi` la copia nelle firme, `StatoDocumento` la
calcola pagando un `GetTessTriangles` per faccia a ogni `guarda_il_pezzo`, e poi
`FirmaEntita` non ha nessun campo per tenerla, `FirmaDiUnaFaccia` non la calcola,
e ne' `FirmaCombacia` ne' `Corrobora` ne' `FraseDelloSpostamento` leggono mai
quella chiave. **Zero consumatori.** Otto ore per un problema gia' risolto.

**Il testo vero di E8 era un'altra cosa, ed era rotta.** La voce chiede che,
quando la geometria si sposta, si **mostri a chi ha confermato cosa e'
cambiato**. La 0.26.0 ha costruito esattamente quella frase — «si e' spostata di
60 mm, da [50, 0, 0] a [110, 0, 0]» — e poi l'ha consegnata a `_log`:

```
_log  ->  Ponte/Sessione.cs:418  ->  Mcp.Protocollo.Nota  ->  Console.Error
```

**In un server MCP su stdio stderr non e' la risposta.** Misurato: `e2_caso_a`
esegue il foro su un pezzo **lungo il 60% in piu'** di quello confermato, sulla
faccia giusta, e nella risposta JSON c'era `riuscita: true` e nient'altro.

> Il meccanismo scritto apposta per non avere difetti silenziosi ne aveva uno
> addosso: aveva misurato lo spostamento e buttato via la misura.

Il canale giusto esisteva gia' ed era vuoto: `Esito.Misure` arriva dritto nel
JSON, come fa `nota_volume`. Adesso i riancoraggi si drenano in `Riuscito`, che
e' **l'unico punto da cui passa ogni successo di tutte e trentasei le
operazioni**, compresi i quattro file `partial`: nessuna operazione puo'
dimenticarsene.

Tre strade coperte, non una: il persist ID, la nota di `Corrobora` sulla faccia
cresciuta, e la ricerca per firma — che prima diceva «si era spostata: ritrovata
dalla firma» **senza un numero**, e adesso usa la stessa frase del persist ID.
Due strade diverse per ritrovare, una sola per dirlo.

### E15 — `guscio` accetta i riferimenti, e il ramo chiuso non ha mai funzionato

**`facce_rif`.** «Fai un blocco e poi svuotalo» adesso e' un piano solo — la
sequenza del contenitore per l'antenna. Fino a ieri `guscio` era l'unica
operazione su faccia che non accettava un riferimento a un passo: voleva
`facce_uid`, che nascono solo da una lettura. Misurato: volume **82.500**, lo
stesso numero che il banco misura da sempre per «guscio 5 mm verso l'interno».

Un elenco di riferimenti **singoli**, non un genere plurale: `Riferimenti`
risolve una faccia per volta e rifiuta quando il ruolo non e' univoco, e quella
regola e' giusta. E se uno solo non si risolve **fallisce tutto**: un guscio
aperto su due facce invece che su tre non e' «quasi giusto», e' un pezzo diverso
che sarebbe riuscito in silenzio.

**`tutto_chiuso` non aveva mai funzionato.**

```csharp
var corpoEntita = corpi[0] as IEntity;   // sempre null
```

Un corpo **non e'** una `IEntity` nell'interop: quel ramo rispondeva «Non sono
riuscito a selezionare il corpo solido» su qualunque pezzo. Adesso da' 105.000
mm³ su un blocco da 240.000 con parete 5.

E la lezione era **gia' scritta in questo stesso file**, quattromila righe piu'
su, nel commento di `sposta_corpi`: misurata dentro SOLIDWORKS il 10 settembre,
con cinque prove che dicevano «i corpi indicati non si sono lasciati
selezionare», e li' gia' riparata con `IBody2.Select2`.

> Una lezione scritta e non applicata e' una lezione che il codice non ha
> imparato: il commento la ricorda a chi legge QUELLE righe, e chi scrive
> altrove non passa di li'.

### E due riparazioni minori, trovate strada facendo

`facce_tolte` diceva **0** su un guscio che una faccia l'aveva tolta davvero,
perche' contava solo gli uid. Una misura che dice zero quando il numero e' uno e'
il difetto di questo progetto in miniatura.

`build.ps1` si fermava a meta' dicendo che era andato male mentre era andato
bene: PowerShell 5.1 avvolge ogni riga di stderr di un comando nativo in un
`NativeCommandError` e ferma lo script anche con l'eseguibile uscito 0. Adesso la
preferenza si abbassa solo intorno alla chiamata nativa, e si guarda
`$LASTEXITCODE`, che e' l'unica cosa che dice davvero com'e' andata.

---

## 0.29.0 — 14 settembre 2026 · libreria 0.64.0

**Un taglio riuscito a meta' adesso lo dice.** Voce E13.

`taglio_cilindrico` con il profilo a cavallo del bordo della faccia toglieva
meta' materiale e rispondeva `riuscita: true`, `pareti_forate: 1`. Misurato: un
foro da 10 profondo 5, centrato sul bordo inferiore di una faccia, toglieva
**196,3 mm³ invece di 392,7** — esattamente meta' — con il fondo ridotto a un
mezzo disco da 39,27 mm² invece di 78,54.

**Il guardiano c'era e guardava dall'altra parte.** `SottoIlProfilo` sa contare
quanti punti del profilo cadono sulla faccia e lo dice bene, ma il suo commento
dichiara: «NON RIFIUTA NIENTE E NON DEVE MAI FARLO: si chiama solo quando
SOLIDWORKS ha gia' detto di no». Quando SOLIDWORKS dice di **si'**, non lo
chiama nessuno.

### Non si rifiuta, si dice

L'operazione e' riuscita e il pezzo esiste: rifiutare a cose fatte vorrebbe dire
buttare una lavorazione per un sospetto. Misure nuove su `taglio_cilindrico` e
`taglio_rettangolare` — `tolti_mm3`, `attesi_mm3`, e quando non tornano una
`nota_volume` che dice di quanto e cosa di solito significa. E' la stessa forma
di `posizioni_non_verificate` e dell'ancora che si sposta.

### Solo sui ciechi l'atteso e' una moltiplicazione

Per un passante il volume dipende da quanto materiale c'e' sotto, che non si sa
senza misurarlo: li' si scrive `tolti_mm3` e basta, senza un atteso che sarebbe
inventato. **Un allarme sbagliato su un passante legittimo sarebbe peggio del
difetto che si sta chiudendo.**

### La soglia viene da due misure, non da un'opinione

Un foro tutto sulla faccia toglie **392,7** contro i 392,699 attesi — meno di un
millesimo. Uno centrato sul bordo ne toglie **la meta'**. Fra i due c'e' un
fattore cinquecento: l'uno per cento lascia passare l'arrotondamento e non
lascia passare niente che abbia una forma.

### Il primo caso rosso non riproduceva il difetto

Era scritto con `faccia_rif: faccia_superiore`, e li' il punto (0,0) cade in
**mezzo** alla faccia: il foro veniva intero, `tolti 392,7 attesi 392,7`, tutto
verde. Sembrava che la riparazione funzionasse, e non si stava provando niente.

Il difetto vive sulle facce **laterali**, dove (0,0) cade sul bordo — e per
nominarne una serve l'uid, perche' il vocabolario dei ruoli non ha nessuna
parola per i fianchi di un'estrusione. E' **esattamente il 58% di facce senza
ruolo misurato in E7** poche ore prima: le due voci si sono spiegate a vicenda.

> Un caso di prova che non si e' visto fallire non e' una prova: e' una frase
> che somiglia a una prova.

Controlli: banco 88 prove 0 fallite, con **zero** `nota_volume` su ottantotto
operazioni legittime — che e' la meta' che conta di piu'.

---

## 0.28.0 — 14 settembre 2026 · libreria 0.63.0

**E7 misurata, e il numero la declassa.** Nuovo attrezzo
`Controlli/CoperturaRuoli`.

La voce E7 proponeva di nominare una faccia per SIGNIFICATO invece che per
posizione — «il fondo della tasca» resta il fondo qualunque quota si cambi a
monte — e chiedeva un numero **prima** di costruire, piu' cosa farne:

> «Se e' sotto meta', questa e' un complemento, non la strada principale, **e va
> scritto.»

**31,4% su 86 facce, dieci pezzi.** E' un complemento.

### La media nasconde la cosa che conta

| | facce | con ruolo | |
|---|---|---|---|
| cilindro | 3 | **3** | **100%** |
| cilindro forato | 4 | **4** | **100%** |
| piastra forata | 7 | 3 | 43% |
| piastra | 6 | 2 | 33% |
| tasca | 11 | 3 | 27% |
| due tasche | 16 | 4 | 25% |
| raccordato | 10 | 2 | 20% |

La spaccatura e' **geometrica**, non statistica. Un pezzo di rivoluzione ha una
faccia in cima, una sotto e una cilindrica: tre facce, tre ruoli, nessuna
ambiguita'. Un pezzo prismatico ha quattro o sei **fianchi**, e per i fianchi il
vocabolario non ha nessuna parola. Su «due tasche» sono dodici facce su sedici.

### E i ruoli che scattano sono quelli che servono meno

Dei 27 ruoli assegnati: `faccia_superiore` **13**, `faccia_inferiore` **10**,
`faccia_del_foro` **4**, `faccia_di_fondo` **0**.

Ventitre su ventisette sono «sopra» e «sotto», cioe' le facce che si
identificano gia' benissimo da sole. `faccia_del_foro` — l'unico che risolve un
problema vero — copre **quattro facce su ottantasei**.

### Tre modi diversi di non avere un ruolo

Contati separati, perche' si riparano in tre modi diversi: **50** per geometria
(i fianchi: si risolve solo allargando il vocabolario), **8** ambigue (piu' di una
faccia cilindrica sulla stessa lavorazione, dove si rifiuta invece di scegliere:
si risolve rifiutando meglio), **9** senza direzione leggibile (il dato c'e' nel
modello ma non si ricava: e' l'unico dei tre che sia un difetto nostro).

### Perche' si chiude qui

E6 ha risolto entrambi i casi di E2, e in 0.1 il persist ID aveva ritrovato 8
facce su 8 in tutte e cinque le modifiche riuscite. La strada principale c'e'
gia'. Costruire un secondo meccanismo che copre il 31% — e per l'85% di quel 31%
copre «la faccia in cima», che non e' mai stato il problema — vorrebbe dire
aprire superficie nuova per un guadagno che non si riesce a nominare.

> Una voce da dodici ore chiusa da una misura di due non e' lavoro risparmiato:
> e' lavoro che non andava fatto, e adesso si sa perche'.

Restano tre voci nuove, tutte dimensionate su numeri gia' misurati: **E14** (le 8
ambigue), **E15** (`guscio` e' l'unica operazione su faccia che non accetta un
riferimento a un passo, trovata costruendo i dieci pezzi) e **E13** dalla
versione precedente.

`CoperturaRuoli` esce **sempre 0**: e' una misura, non un controllo. Un numero
basso non e' un fallimento, e' la risposta.

---

## 0.27.0 — 14 settembre 2026 · libreria 0.63.0

**Il marchio esce dal nome del prodotto.** Voce I1.

Il nome e' **OCE Solid Agent**; il repository, il pacchetto e l'eseguibile si
chiamano `oce-solid-agent-mcp`.

| | prima | adesso |
|---|---|---|
| namespace | `SolidWorksMcp.*` | `Oce.Solid.Agent.*` |
| eseguibile | `SolidWorksMcp.exe` | `oce-solid-agent-mcp.exe` |
| progetto | `SolidWorksMcp.csproj` | `Oce.Solid.Agent.csproj` |
| nome prodotto (installatore) | `SolidWorks MCP` | `OCE Solid Agent` |
| nome sul protocollo MCP | `solidworks-mcp` | `oce-solid-agent` |
| prima riga del `NOTICE` | `SolidWorks MCP` | `OCE Solid Agent` |

29 file, 71 sostituzioni, con `Controlli/rinomina-prodotto.js`: uno script e non
ventinove modifiche a mano, perche' si rifa' identico se si sbaglia, si legge
cosa ha toccato, e una sostituzione a mano su ventinove file ne dimentica uno —
quello che nessuno riguardera' piu'.

### Non bastava rinominare il repository

Il nome del prodotto non stava solo nell'URL. Stava nel `RootNamespace`,
nell'`AssemblyName`, nel nome dell'eseguibile che finisce nella configurazione
di **ogni client MCP**, nel payload dell'installatore e nel manifest. Chi
scaricava il pacchetto si trovava `SolidWorksMcp.exe` sul disco.

### Due cose che si sono viste solo facendo

**La prima passata aveva preso solo la grafia attaccata.** Si era lasciata
dietro le sei separate, che erano **le piu' visibili di tutte**: il nome che
l'installatore mostra, la prima riga delle note legali, il titolo del README e —
la peggiore — il nome che il server dichiara **sul protocollo MCP**, cioe' quello
che legge il client.

E la seconda passata dava **zero**, perche' il filtro d'ingresso dello script
cercava solo la prima forma e saltava i file che avevano solo le altre. Il
conteggio usciva zero e sembrava «niente da fare».

> Un filtro piu' stretto dell'elenco delle sostituzioni e' un modo di non fare
> meta' del lavoro senza dirlo.

**L'`AssemblyName` e' il nome del PACCHETTO, non del namespace.** Alla prima
compilazione era uscito `Oce.Solid.Agent.exe` mentre build, installatore, banco e
README puntavano tutti a `oce-solid-agent-mcp.exe`. Chi copia una riga dalla
documentazione deve trovare il file che c'e'.

### Cosa non si e' toccato, e sono decisioni

**`CHANGELOG.md`**: le voci storiche dicono cosa si chiamava allora, e un
changelog riscritto e' un changelog che mente.

**`Riscontro.Disegno`**: non contiene nessun marchio altrui ed e' un buon nome.
La rinomina serve a togliere un marchio, non a uniformare.

**La parola `SolidWorks` da sola**, che resta ovunque serva: nominare il
programma con cui si e' interoperabili e' uso lecito e necessario, ed e' la
regola che questo progetto si era gia' scritto — il marchio e' un aggettivo che
modifica un sostantivo, mai il nome del prodotto.

### E il TODO, che la rinomina aveva rovinato

Lo script ha sostituito il nome anche dove il TODO **parlava** del nome, e il
criterio di chiusura di I1 era diventato il suo contrario: «un grep di
`Oce.Solid.Agent` non trova piu' niente». Riparati nove riferimenti, distinguendo
il **repository** (`oce-solid-agent-mcp`) dal **progetto C#**
(`Oce.Solid.Agent.csproj`) — che dopo oggi sono due nomi diversi.

> Una sostituzione meccanica su un documento che descrive il passato riscrive
> anche il passato.

Controlli: ponte riavviato col nome nuovo (agganciato, libreria 0.63.0.0, 36
operazioni); banco 88 prove 0 fallite; ProvaRuota, ProvaSagoma, ProvaPosizioni a
0; il ponte esegue i piani e il caso negativo continua a essere rifiutato.

---

## 0.26.0 — 14 settembre 2026 · libreria 0.63.0

**Il persist ID propone, la misura giudica.** Voce E6.

Quando una quota a monte sposta una faccia, l'ancora sul punto si perde e il
piano viene rifiutato pur essendo giusto. Adesso, prima di cercare alla cieca,
si chiede a SOLIDWORKS: `GetObjectByPersistReference3` e' l'unica ancora che non
sia una nostra congettura sulla geometria.

### I due casi di E2, e il secondo si e' risolto da solo

| caso | prima | adesso |
|---|---|---|
| **A** — si cita `F6`, poi il pezzo si allunga da 100 a 160 | RIFIUTO | **risolto**, e lo dice: «si e' spostata di 60 mm, da [50, 0, 0] a [110, 0, 0]» |
| **B** — il fondo della tasca scende esattamente dove stava la faccia in alto | risolto per firma, **e il pezzo era sbagliato** (E3 lo aveva chiuso col rifiuto) | **risolto giusto**: volume **58.885,8** |

Il caso B e' il migliore dei due esiti possibili. La voce diceva: «se il persist
ID facesse tornare la faccia giusta anche li', il caso B diventa un test di
non-regressione, non un caso risolto». E' successo — **58.885,8** contro i
59.042,9 del mezzo foro, cioe' il numero del pezzo giusto.

### Mai da solo

Quello che SOLIDWORKS restituisce e' un **candidato**, e passa dallo stesso
giudice di tutti gli altri — `Corrobora`, con la firma **rimisurata adesso**
sulla faccia ritrovata e non quella che il piano porta con se', che e' proprio
cio' che si sta verificando. `GetObjectByPersistReference3` puo' restituire
senza errore una faccia diversa dopo un rebuild che cambia la topologia:
sarebbe il difetto peggiore di tutta la sezione, perche' non darebbe errore.

`IsSamePersistentID` **non si usa**: in 0.1 ha risposto vero 46 volte su 46,
anche dove l'area era raddoppiata perche' due facce si erano fuse. Attesta
l'identificativo, non la faccia. Il segnale vero e' `ErrorCode` diverso da zero
— che e' esattamente cio' che succede alle facce cancellate da una soppressione,
e li' si lascia parlare la ricerca di sempre.

### Dove vive, e perche' non nello stato

**375-423 byte per faccia**, cioe' ~530 caratteri in base64: su sessanta facce
sarebbero **~32 KB**, il doppio dell'intero tetto dello stato. Viaggia nella
**firma del passo** — un allegato interno che non arriva a nessun modello — e il
ponte lo toglie dallo stato subito dopo aver costruito le firme. E' la stessa
strada dell'ancora della 0.25.0, moltiplicata per venti. Stato misurato dopo:
invariato.

`StatoDocumento.Modello` e' una **proprieta'** e non un parametro in piu' su
`Leggi`: cambiare l'insieme delle firme di un metodo rompe chi lo chiama per
riflessione anche quando il compilatore tace. Lezione della 0.25.1, applicata
prima di sbatterci di nuovo.

### Due numeri sbagliati nel TODO, corretti misurando

La voce E6 diceva che la faccia si sposta **a X=80**. Misurato: **X=110**. Il
pezzo cresce da una parte sola anche se lo schizzo e' centrato, quindi la faccia
destra fa tutti i 60 mm della crescita — lo stesso inciampo gia' corretto in E2,
dove il TODO diceva 30 mm e la misura diceva 60.

E l'uid **resta `F6`**: le aree non si riordinano, quindi la rinumerazione non
c'entra niente. Il rifiuto veniva solo dal confronto sul punto, ed e' la prova
piu' pulita che questa voce serviva: non c'era nessuna ambiguita' da sciogliere,
c'era solo un'ancora persa.

### E un difetto che non da' errore, reso visibile

Il caso A, appena ha smesso di essere rifiutato, ha prodotto **mezzo foro**:
`y_mm: 0` mette il centro sul bordo inferiore della faccia, e l'operazione ha
tolto **196,3 mm³** invece di 392,7 rispondendo `riuscita: true`.

Non e' un difetto di questa versione — era li' da sempre, coperto dal fatto che
quel piano veniva rifiutato prima di arrivarci. `SottoIlProfilo` sa contare
quanti punti cadono sulla faccia, ma il suo commento dichiara: «NON RIFIUTA
NIENTE E NON DEVE MAI FARLO: si chiama solo quando SOLIDWORKS ha gia' detto di
no». Quando SOLIDWORKS dice di si', non lo chiama nessuno.

Il caso e' stato spostato a `y_mm: 10`, dove il foro sta tutto sulla faccia e i
numeri tornano (volume **191.607,3** = 192.000 − π·5²·5). Il difetto e' la voce
**E13**, con i numeri gia' misurati.

> Togliere un rifiuto non toglie solo il rifiuto: toglie anche il coperchio a
> quello che c'era sotto.

---

## 0.25.2 — 14 settembre 2026 · libreria 0.62.0

**Sette controlli che non controllavano, trovati da una revisione fatta prima
del push.** Uno di loro sorvegliava il banco.

### 1. Il banco si dava la promozione da solo

`OperazioniFori` chiamava `ForiMancanti` e poi scriveva, senza condizione:

```csharp
misure["posizioni_verificate"] = true;
```

`ForiMancanti` tornava `null` — «tutto a posto» — anche in **quattro strade in
cui non aveva guardato niente**: diametro ignoto, `GetFaces()` nullo, il `catch`,
e soprattutto **nessuna faccia cilindrica del diametro atteso**, cioe' proprio il
caso in cui i fori potrebbero non esserci. Uno dei suoi `return null` aveva
perfino il commento giusto: *«niente da confrontare: non si inventa un
verdetto»*.

> **La funzione si rifiutava di inventare il verdetto, e chi la chiamava lo
> inventava al posto suo.**

E il banco decideva il pass con `misure.ContainsKey("posizioni_verificate")`:
una chiave sempre scritta. `verificate` era vero per costruzione, e il ramo che
stampava «POSIZIONI NON VERIFICATE» era **codice morto**. Le cinque prove
`foro_guidato` misuravano il diametro credendo di misurare anche le posizioni.

Adesso gli esiti sono **tre**: `Verificate`, `NonVerificabile`, `Sbagliate`.
«Non ho potuto guardare» non e' un rifiuto — la lavorazione e' riuscita, e' il
controllo che non c'e' stato — e viaggia in una chiave **diversa**,
`posizioni_non_verificate`, col motivo dentro: un `posizioni_verificate = false`
sarebbe stato letto come «verificate» da chiunque guardi se la chiave c'e'.

Il giudizio sta in `GiudizioPosizioni`, **senza SOLIDWORKS**: raccogliere le
facce cilindriche richiede il COM, decidere cosa farne no. Separati, il caso che
conta — zero facce trovate — si scrive in una riga, e con SOLIDWORKS acceso non
si sapeva come costringerlo a capitare. `Controlli\ProvaPosizioni` (20 prove).

Una prova e' partita rossa e aveva ragione lei: «scostamento esattamente 0,1 mm»
non e' un caso provabile. Il giro mm → m → mm da' 0,10000000000000142, e il
verdetto dipendeva dall'ultimo bit. Al suo posto si misura la **separazione**:
rumore 2,9e-15 mm contro una soglia di 0,1 — quattordici ordini di grandezza.

### 2. La cava a filo del bordo, rifiutata solo da un lato

`FormaChiusa.Dentro` usa la parita' degli attraversamenti, che **sul bordo non
sa rispondere** e risponde secondo il lato: col confronto `x < taglio` un
vertice sul bordo destro risulta fuori, lo stesso a sinistra risulta dentro.

Su una piastra 100×100: una cava a filo del bordo **destro** —
(30,−10)(50,−10)(50,10)(30,10) — aveva due vertici «fuori» e due «dentro», e
veniva rifiutata come «sta a cavallo del bordo esterno». La stessa cava
**specchiata a sinistra** aveva tutti e quattro i vertici «dentro», e passava.

Stessa geometria, verdetto opposto secondo il lato. Uno dei due era sbagliato, e
SOLIDWORKS ha detto quale.

**La prima riparazione andava nel verso sbagliato, e vale la pena scriverlo.**
Il ragionamento era: «una cava a filo del bordo e' un'asola aperta, si fa tutti
i giorni, quindi vanno accettate tutt'e due». E' vero del **pezzo** e falso
dello **schizzo**: due contorni che condividono un tratto di bordo non
delimitano una regione, e l'asola aperta si disegna come **un** contorno solo
con otto punti, non come due che si toccano.

Misurato — tre geometrie, un solo risultato, «L'estrusione non e' stata creata»:
la cava a destra, la stessa specchiata a sinistra, e un rombo che tocca il bordo
in **un solo vertice**.

Quindi il verdetto giusto e' **no da tutt'e due i lati**, e cio' che era rotto
era il **si'** a sinistra. Il guadagno di `DoveSta` non e' far passare di piu':
e' far rispondere **uguale** ai due lati, e dare un motivo migliore di quello
che da' SOLIDWORKS — «TOCCA il bordo esterno in 2 punti», piu' la strada per
avere l'asola che si voleva.

> Il verso di una riparazione non si ricava dal ragionamento sull'officina: si
> chiede al pezzo. «E' lavoro legittimo» diceva come **deve** andare, non come
> **va**.

La banda del bordo e' un micrometro, con dieci milioni di volte di margine sul
rumore e diecimila sulla piu' piccola quota sensata. In `ProvaSagoma` la prova
sulle due cave specchiate controlla la **simmetria** e nient'altro — quale sia
la risposta lo decide il pezzo — e resta la prova che `Dentro` da solo **e'**
asimmetrico: se un giorno qualcuno lo ripara, quella riga glielo dice.

### 3. Il raggio troppo grande passava da una strada e non dall'altra

Il commento di `SelezionaSpigoli` prometteva che «tutti i controlli che contano
stanno qui», e fra quelli che la misura chiesta ci stia sulla lunghezza dello
spigolo. Il ramo `spigoli_rif` usciva con `return true` **prima** di arrivarci:
un raccordo da 30 mm su uno spigolo da 10 era rifiutato con `spigoli_uid` e
lasciato passare con `spigoli_rif`, fino a `FeatureFillet3` che fallisce dicendo
soltanto che non e' stato creato.

> Il metodo era uno, ma aveva due strade, e bastava questo per credersi al
> sicuro. **Mettere due strade sotto lo stesso commento non e' meno pericoloso
> che mettere due copie sotto due nomi.**

### 4. Il tetto sulle lavorazioni non diceva di essere scattato

Tre tetti su quattro si dichiaravano. `MaxFeature` no: le lavorazioni oltre
l'ottantesima finivano in `altre_feature` insieme a quelle di tipo ignorato —
due cose diverse sommate — e `troncato` restava `false`.

Era il peggiore dei quattro da tacere: l'albero si scorre in **ordine di
creazione**, quindi le ottanta elencate sono le piu' **vecchie**, e quella che
il piano ha appena creato e' fra le scartate. Il modello leggeva «elenco
completo», non trovava la lavorazione nuova, e poteva concluderne che
l'operazione non aveva fatto niente — e rifarla.

Adesso: `feature_non_elencate`, `feature_totali`, `troncato: true`. Quali si
tengono resta da cambiare, ed e' la voce **E12**: serve un giro leggero prima e
il dettaglio dopo, perche' `QuoteDellaFeature` costa chiamate COM.

> **Un tetto che non dice di essere scattato non e' un tetto: e' una risposta
> sbagliata che si presenta come completa.**

### 5. Il piano controllato «tutto» si fermava al primo livello

La descrizione di `disegna` promette al modello che *«se un passo qualunque ha
argomenti che non rispettano lo schema, non viene eseguito niente e il documento
resta com'era»*. `Ponte.Forma` per `type: "object"` verificava che fosse un
oggetto e **non guardava dentro**: ne' il `required` annidato ne' le
`properties`.

Lo schema di `ripeti_lineare` dichiara `feature_rif` con
`required: ["passo", "genere"]`. Un piano di quattro passi il cui quarto scrive
`feature_rif: {}` passava tutta la validazione preventiva, i primi tre passi
venivano costruiti nel documento, e il quarto falliva. **Il documento restava a
meta'.** La garanzia di tutto-o-niente e' esattamente cio' per cui quel
controllo esiste: mantenerla solo al primo livello vuol dire non mantenerla.

Adesso scende fino a tre livelli e il messaggio nomina il campo per esteso
(«feature_rif.passo»), perche' lo stesso nome «passo» sta sotto tre riferimenti
diversi nello stesso schema.

### 6. Lo stato mostrava un nome, e il controllo lo dichiarava inesistente

`UidCheNonEsistono` ferma un piano che cita un identificativo che non c'era. Ma
raccoglieva **solo il campo `uid` delle cinque famiglie**, mentre lo stato
pubblica nomi di lavorazione anche in tre altri posti: `da_lavorazione` su ogni
faccia (messo li' apposta per «quel foro portalo a M10»), `fatto_da` su ogni
corpo, e la mappa `lavorazioni`. E l'elenco `feature` e' filtrato per tipo e
tagliato a `MaxFeature`.

Bastava un pezzo con piu' di ottanta lavorazioni, o una di tipo non elencato,
perche' lo stato mostrasse un nome e il controllo lo chiamasse inesistente —
**un rifiuto falso causato dall'output di chi lo emette**, e il piu' odioso,
perche' chi l'ha scritto quel nome l'ha letto nella risposta precedente.

### 7. L'ancora si pagava anche per buttarla

`Ponte.Sessione.LeggiStato` aveva il flag cablato a `true`. La lettura «prima»
di `disegna` serve solo a sapere su quale documento si lavora, quanto volume
c'era e quante lavorazioni erano in errore: non se ne costruiscono firme e non
torna a chi legge. Pagava lo stesso un `GetTessTriangles` e un baricentro pesato
per area **su ogni faccia**.

Adesso c'e' `StatoSenzaAncora`. Nome diverso e non un parametro in piu': e' la
lezione di `LeggiConAncora` della 0.25.1.

### Una prova del banco misurava anche cio' che non doveva

`stato: sotto i 16 KB su un telaio da quattro barre` e' fallita a **16.280
caratteri**, contro i **15.628** di un'ora prima, **senza che il codice fosse
cambiato**.

Lo stato elenca `documenti_aperti`, quindi la misura dipende da cosa c'e' aperto
in SOLIDWORKS oltre al telaio. Sei parti salvate da un giro di `corri_piani.js`
erano rimaste aperte: ~650 caratteri, ed erano tutta la differenza.

E `ChiudiScarti --chiudi` **non** le chiude: risparmia apposta i documenti che
hanno un file. Ci vuole la cartella — `ChiudiScarti.exe --chiudi "...\Banco\lavoro"`.

Adesso il messaggio stampa **sempre** quanti documenti erano aperti, verde o
rosso che sia, e quando sono piu' di uno lo dice. Una prova che dipende
dall'ambiente non e' sbagliata — e' sbagliata se non lo dichiara, e mezz'ora
spesa a cercare una regressione che non c'era e' il prezzo di non dichiararlo.

> Un messaggio che da' il criterio serve a leggere anche i verdi, non solo i
> rossi.

### E cinque casi del banco non potevano piu' girare

`corri_piani.js` creava `Banco\lavoro\profili\` **vuota**. I cinque casi del
telaio nominano `{BANCO}/profili/QUADRO_COPIA.SLDLFP`: prima della riparazione
col segnaposto (0.25.0) quel percorso puntava alla scratchpad di sessione, dove
il file c'era perche' qualcuno ce l'aveva messo a mano. Il segnaposto ha reso il
percorso portabile e ha lasciato il file **senza nessuno che lo provveda**.

Rispondevano «Il profilo ... non esiste su questa postazione», che e' un
messaggio giusto per un problema che non c'era. Adesso `preparaLaCartella` copia
il profilo normato da `PROFILI_SALDATURA`, e se non lo trova **lo dice** invece
di morire: chi clona il repository non ha quella libreria e deve poter far
girare tutti gli altri casi.

Si copia e non si usa l'originale perche' inserire un profilo **apre** il suo
`.SLDLFP` e lo lascia sporco: il banco lavorerebbe sulla libreria normata vera
dell'officina.

### E, in coda, chi clona

`costruisci-controlli.ps1` aveva il percorso dell'interop costante. Adesso
accetta `-SwRedist` e legge `SW_REDIST`, che e' la stessa variabile che
`Banco\LEGGIMI.md` gia' documenta.

---

## 0.25.1 — 14 settembre 2026 · libreria 0.61.0

**La guardia guardava l'albero, e `git push` manda la storia.**

Un minuto prima del primo push, `controlla-pubblicabilita.ps1` diceva «Pulito».
L'albero di lavoro era stato riparato davvero. Ma nella **storia** il percorso di
scratchpad col nome utente c'era ancora in **155 punti su 41 commit**.

Non due, come diceva `git log -S`: quello segnala i commit in cui il conteggio
**cambia**, e il blob invece vive in ogni albero intermedio finche' non viene
tolto. Si guarda l'albero di ogni commit, non il suo diff.

I quarantuno commit non erano ancora su `origin`, quindi riscriverli e' costato
zero e non ha rotto niente: dopo il push sarebbe stato un force-push su storia
pubblicata. `Controlli/bonifica-storia.js` piu' `git filter-branch` sul solo
intervallo `origin/main..HEAD`, cosi' i due commit gia' pubblicati restano
intatti. **L'albero finale e' identico bit per bit a prima** — era gia' a posto,
era la storia dietro che non lo era.

### Il perimetro sbagliato tre volte

1. `controlla-catalogo.ps1` guardava dentro `catalogo.json`, perche' quando fu
   scritto il catalogo era l'unica cosa che usciva.
2. Questo guardava tutto l'albero, ma solo sette **estensioni**: `App.config`,
   `app.manifest`, `LICENSE` e `NOTICE` non ne avevano una buona.
3. E guardava solo l'albero.

Adesso il perimetro **non e' piu' una lista, e' cio' che esce**: `git ls-files`
piu' i non ancora tracciati e non ignorati — cioe' esattamente cio' che un
`git add -A` porterebbe dentro. Le estensioni sono diventate una lista di
**esclusione** (i binari): un tipo di file di testo nuovo entra da solo.
**Una lista di inclusione sbaglia in silenzio, una di esclusione sbaglia
rumorosamente.**

### E il controllo non stava leggendo i file

Visto solo perche' la guardia nuova e' stata puntata prima sulla storia sporca,
per vederla rossa. `Select-String` ricevuto un array di stringhe per **pipeline**
le tratta come **testo da cercare**, non come elenco di file: cercava il nome
utente dentro i *nomi dei percorsi* e rispondeva «ok nessuno» senza aver aperto
niente. Si e' scoperto perche' `S:\` combacia con ogni percorso assoluto di
questo disco e ha fatto scoppiare un `Substring`. Riparato con `-LiteralPath`, e
provato a rosso su tutte e cinque le categorie prima di crederci.

Due trappole minori, misurate qui: una funzione PowerShell chiamata `Git` fa
risolvere `& git` a se stessa (ricorsione infinita, e l'errore dice solo
«Overflow della profondita' delle chiamate»); e l'eccezione per i file che
possono nominare i vietati va scritta **per nome e non per percorso**, perche'
nella storia `controlla-catalogo.ps1` sta in `Strumenti\` — i file si spostano,
il loro mestiere no.

---

## 0.25.0 — 14 settembre 2026 · libreria 0.61.0

**Il punto che seleziona e l'ancora che identifica sono due cose diverse.**

`punto_mm` e' un punto **garantito sulla faccia**, preso col punto piu' vicino
all'origine: giusto per selezionare — il centro di una faccia bucata cadrebbe nel
vuoto. Come impronta digitale e' cattivo, perche' e' un punto di **frontiera**.

Piastra 100 × 60 × 20, una tacca di 15 × 15 in un angolo — **225 mm² su 6000, il
3,75%**:

| | prima | dopo | spostamento |
|---|---|---|---|
| `punto_mm` | `[0,20,0]` | `[15,20,0]` | **15 mm** |
| `ancora` (baricentro d'area) | `[50,20,-30]` | `[51.656,20,-30.877]` | **1,87 mm** |

### Il criterio della voce era gia' soddisfatto, e andava misurato

«Dieci letture identiche, e una dopo un rebuild a vuoto.» Provato sul caso a
simmetria di rotazione dove i punti a distanza minima sono infiniti
(`estrudi_cerchio` Ø40 con svasato coassiale): **dieci su dieci identiche**,
e identiche dopo il rebuild. L'instabilita' temuta non c'e' su questa versione.

### E l'ancora da sola non toglie nessun rifiuto

La tolleranza e' 0,05 mm e 1,87 la supera lo stesso. L'ancora **non e' la
riparazione**: e' il numero che rende possibile una tolleranza proporzionale al
cambiamento, che e' la voce E8. `FirmaCombacia` continua percio' a usare
`punto_mm`: cambiare il termine di confronto senza cambiare la politica di
tolleranza sposterebbe i rifiuti senza toglierli.

### L'ancora non si manda a chi legge

Due mestieri, due destinatari. Offrire due punti a chi ne deve usare uno, e la
scelta sbagliata non darebbe errore: darebbe un foro fuori dal materiale. E su
sessanta facce l'ancora e' 1.400 caratteri — lo stato passava a **17.171**,
oltre il tetto dei 16 KB. Si toglie dopo aver costruito le firme, che se la
portano dietro.

### Il difetto che e' costato mezz'ora

`IFace2.GetTessTriangles` risponde un **`float[]`**, non un `double[]`: un
`as double[]` torna **null in silenzio**, e l'ancora usciva vuota su ogni faccia
senza nessun errore. Terza volta per questa famiglia — e l'interop lo diceva,
`IGetTessTriangles` e' dichiarata `Single`.

---

## 0.24.0 — 14 settembre 2026 · libreria 0.60.0

**I corpi erano controllati per esistenza e non per identita'**, e il commento
del codice diceva il contrario.

### Il rosso, misurato

Due blocchi separati: `C1` il grande a `[-70,10,0]`, `C2` il piccolo a
`[60,10,0]`. Un piano di due passi: taglia in due il grande, poi **sposta C2**.

Il taglio rinumera i corpi. «C2» diventa **meta' del blocco grande**, e viene
spostato quello:

```
riuscita: true          (nessun avviso)
C2  centro [-87.5, 110, 0]   <- mezzo blocco grande, spostato
C3  centro [60, 10, 0]       <- il blocco piccolo, mai toccato
```

### Il commento prometteva gia' il controllo

`CorpiDaUid` diceva sopra: «La firma di un corpo e' centro e volume della
scatola... **E' lo stesso controllo che si fa sulle facce.**» Il codice non
chiamava `FirmaCombacia` affatto.

> Un commento che descrive un controllo che non c'e' e' **peggio** di nessun
> commento: chi legge smette di cercare il buco proprio dove sta.

Il buco era doppio: anche il ponte non copiava `centro_mm` nella firma, quindi
per «C1» non c'era niente da confrontare nemmeno volendo.

### Adesso

```
«C2» adesso indica un'altra parte del pezzo (spostato di 17,5 mm). Il pezzo e'
cambiato da quando hai confermato: non lavoro sul corpo sbagliato.
```

E spostare un corpo che non si e' mosso continua a riuscire.

**La parola giusta per la cosa giusta:** lo stesso confronto serve a facce,
spigoli e corpi, e «non lavoro sulla faccia sbagliata» detto di un corpo manda a
cercare una faccia che non c'entra.

### Le lavorazioni: `feature_id`

L'uid di una lavorazione **e' il suo nome**, e un nome si rinomina: chi rinomina
«Taglio1» e poi chiama «Taglio1» un'altra lavorazione ha trasferito
l'identificativo, e `SelectByID2` seleziona obbediente quella nuova. Lo stato
porta ora `feature_id` da `IFeature.GetID()`, e prima di selezionare si
confronta.

### Restano gli assi

Lo stato degli assi e' `{uid, tipo}`: non c'e' niente da firmare, quindi
`asse1_uid` e `asse2_uid` sono ancora controllati per esistenza e non per
identita'. Servirebbe pubblicare punto e direzione — lavoro a se'.

### E lo stato dice cosa c'e' SELEZIONATO

Non uno strumento nuovo: una riga in piu' in quello che si legge gia', scritta
**solo quando c'e' qualcosa di selezionato**.

```
niente        il campo non c'e'
una faccia    [{"tipo":"faccia","uid":"F5"}]
tre insieme   [{"tipo":"faccia","uid":"F5"},{"tipo":"faccia","uid":"F3"},
               {"tipo":"spigolo","uid":"E9"}]
```

L'identificativo e' **lo stesso** che lo stato assegna, quindi il progettista
puo' indicare la faccia col dito invece di descriverla a parole: l'ambiguita' si
toglie prima dell'operazione invece di scoprirla dopo.

I due casi scomodi sono **decisi**, non scoperti: niente selezionato → il campo
non si scrive; piu' cose insieme → si tornano tutte, perche' sceglierne una
sarebbe indovinare. E un terzo: un'entita' selezionata che nello stato non ha un
nome (oltre il tetto delle sessanta facce, o un vertice) si dice **che cosa e'**
senza dargli un nome.

### Il nome delle lavorazioni sta in una mappa, non su ogni faccia

Scriverlo su ogni faccia ha fatto sforare il tetto dei 16 KB dello stato: su un
telaio saldato i nomi sono «TUBO STRUTTURALE QUADRO 80X80X5(1)», e sessanta
facce che vengono da quattro lavorazioni li ripetevano sessanta volte —
**18.629 caratteri**, contro un limite che il banco sorveglia.

Con la mappa in fondo (`lavorazioni: {"12": "..."}`) e il solo numero su ogni
faccia: **15.752**. Il margine e' sottile, e va tenuto d'occhio.

---

## 0.23.0 — 14 settembre 2026 · libreria 0.59.0

**Il ritrovamento a occhio chiuso e' chiuso.** Era l'unico modo in cui il
sistema produceva un pezzo sbagliato senza dirlo.

### Prima

Il ritrovamento per firma accettava la candidata confrontando **solo punto e
normale**. Nel caso B della voce E2, dopo aver assottigliato il pezzo, la faccia
in alto (area 4400) finiva esattamente dove il piano cercava il fondo della
tasca (area 1600). Il punto combaciava, l'area era quasi il triplo, e veniva
presa lo stesso: `riuscita: true`, un mezzo foro al posto di un foro.

**Il punto aveva vinto sull'area.**

### Adesso

```
La faccia «F5» si e' spostata, e l'unica che combacia per posizione viene da
«Estrusione-Estrusione1» invece che da «Taglio-Estrusione1»: non e' la stessa
faccia, e' un'altra finita nello stesso posto.
```

Il pezzo resta intatto: fondo 1600 e faccia in alto 4400, tutte e due dove
erano.

### Due prove, e nessuna e' «l'area deve combaciare»

1. **la stessa lavorazione**, per `IFeature.GetID()` e non per nome: il nome e'
   localizzato e rinominabile, e la stessa faccia smetterebbe di essere la
   stessa perche' qualcuno ha cambiato la lingua di SOLIDWORKS;
2. **l'area puo' solo calare** — vincolo monotono, non confronto: un foro toglie
   area ed e' legittimo, ma non ne aggiunge.

Stanno **fuori** da `FirmaCombacia` e non dentro: quel confronto decide anche se
la faccia di partenza va ancora bene, e li' l'area che cresce e' normale. La
corroborazione vale solo quando si sta gia' cercando un sostituto.

Lo stato porta ora `lavorazione` e `lavorazione_id` su ogni faccia — utili anche
a chi legge, non solo al controllo.

### La controprova

Le quattro asole del coperchio, che era il caso da non rompere: **quattro su
quattro**, tre delle quali ritrovate dalla firma. Il criterio ne chiedeva tre.

### E un difetto MIO, riparato: rifiutavo lavoro legittimo

Il controllo sui contorni della 0.14.0 rifiutava il piano delle **quattro alette
del coperchio**, che e' giusto: quattro contorni **staccati** su una faccia sono
quattro regioni, non quattro vuoti fuori dal bordo.

I casi adesso sono tre:

| | |
|---|---|
| dentro | e' un vuoto |
| **a cavallo** | taglia il bordo: incoerente sempre, **si rifiuta** |
| **fuori** | e' una regione a se': **si accetta e si dice** |

Del terzo esce `nota_contorni`, accanto a `corpi_totali`. Il difetto della
piastra col cerchio staccato — 2 corpi, «riuscita» — passa ora **con la nota e
il conteggio dei corpi sotto gli occhi**, invece di essere impedito.

> Un rifiuto di cio' che e' corretto e' il peggiore dei due modi di sbagliare.

---

## 0.22.0 — 14 settembre 2026 · libreria 0.58.0

**I due casi della voce E2, costruiti prima di riparare.** In
`Banco/casi/E2-i-due-casi.md`, con i numeri attesi accanto a ogni passo, e
lanciabili **senza nessun modello linguistico**.

Il caso B e' quello che conta: dopo `modifica_quota` 20 → 12 la faccia in alto
finisce dove il piano cerca il fondo della tasca, e il ritrovamento la prende.

```
giusto     58.885,8   cilindro 125,66 mm² = π·10·4 a [5,0,0]
sbagliato  59.042,9   cilindro  62,83 mm² a [-5,0,8]
```

157,1 mm³, lo 0,27%: mezzo foro profondo 2 invece di un foro intero profondo 4.
Ricostruzione pulita, nessun avviso, «riuscita: true».

**Due numeri della voce corretti misurando:** lo spostamento del caso A e' 60 mm
e non 30 (il pezzo cresce da una parte sola, non simmetrico); e la tasca del
caso B va centrata in Y, se no esce dal bordo e diventa 1200 mm² invece di 1600.

### `modifica_quota` suggeriva una forma che non funzionava

Il messaggio dice «per esempio «D1@Schizzo1»», e quella forma veniva rifiutata:
il confronto voleva il nome corto o quello intero col documento in coda. **Il
messaggio diceva il falso.**

Non e' comodita': la forma con il documento contiene il **titolo**, quindi un
caso di banco scritto cosi' non si puo' rilanciare su un altro pezzo. Adesso le
tre forme valgono tutte, e i casi che cambiano una quota sono ripetibili.

---

## 0.21.0 — 14 settembre 2026 · libreria 0.58.0

**Gli identificativi citati devono esistere.** E' l'altra meta' della difesa
anti-deriva, e prende un caso che le firme non possono prendere.

### Il rosso

`[estrudi_rettangolo, taglio_cilindrico su F1]` su un documento vuoto:
**riusciva**, volume 117.738,1. Il foro e' finito sulla faccia piu' grande — che
in quel caso era anche quella giusta, ma **non l'aveva scelta nessuno**. «F1» e'
posizionale e si rinumera per area a ogni lettura.

### Perche' le firme non bastavano

La firma confronta un identificativo con la geometria che descriveva quando il
piano e' nato. Se la faccia **non esisteva affatto**, non c'e' nessuna firma da
confrontare, quindi nessun rifiuto.

> La firma protegge dallo **spostamento**; questo protegge dall'**invenzione**.

### Il messaggio indica la strada buona

```
«faccia_uid» cita «F1», che nel pezzo che ho guardato non c'era.
Nel pezzo che ho guardato non c'era nessun identificativo: era vuoto.

Gli identificativi nascono da «guarda_il_pezzo» e valgono per QUELLA lettura.
Se la geometria la crea un passo precedente di questo stesso piano [...] va
nominata con il riferimento al passo — per esempio "faccia_rif": {"passo": 1,
"genere": "faccia_superiore"}.
```

Un rifiuto che non indica la strada buona insegna soltanto a riprovare a caso.
Vale per tutti i tredici campi `*_uid` del catalogo, stringa o elenco.

### Il legittimo passa

| | |
|---|---|
| lo stesso piano con `faccia_rif` | passa, 117.738,1 |
| `faccia_uid: "F1"` su un pezzo che c'e' gia' | passa, −π·4²·20 |
| foro / ripetizione / specchio per uid, asse e feature | passano |

---

## 0.20.0 — 14 settembre 2026 · libreria 0.58.0

**Il banco entra nel repository, e si scopre che era rotto da tre giorni.**

`Banco/` con i quattro sorgenti, `Banco/casi/` con **112** piani, il runner del
ponte (`corri_piani.js`, `guarda.js`) e `Banco/LEGGIMI.md`. Stavano tutti in una
cartella temporanea di sessione.

### Era rotto, e nessuno poteva accorgersene

Il banco caricava `OpenPDM.SwAddin.dll` e chiedeva i tipi
`OpenPDM.SwAddin.Agente.OperazioniSw`. Le voci B4 e B5 hanno spostato le
operazioni in `Riscontro.Disegno`: da allora `GetType(..., true)` solleva.

Tre giorni che non partiva. Non se n'era accorto nessuno **perche' viveva fuori
dal repository e non lo lanciava piu' niente**.

> Una misura che si puo' rifare solo dalla macchina e dalla settimana in cui e'
> stata scritta non e' una misura: e' un aneddoto.

### Rimesso in piedi

* i tipi puntano a `Riscontro.Disegno.*`;
* il percorso della libreria diventa `--libreria <percorso>`, con predefinito
  accanto all'eseguibile — era una costante dentro una cartella temporanea;
* `Assembly.Load(File.ReadAllBytes(...))` invece di `LoadFrom`, perche' la
  libreria sta su un percorso di rete e .NET rifiuta di caricare da li';
* **`Main` spezzato in due.** Il gestore `AssemblyResolve` va installato prima
  che il JIT compili un metodo che nomina un tipo interop, e il JIT compila
  tutto il metodo prima di eseguirne la prima riga: dentro `Main` arrivava
  troppo tardi, e l'eccezione usciva «in Program.Main» — cioe' **prima di
  qualunque riga scritta li' dentro**.

### Le librerie di Dassault: due difese invece di una

`bin/` nel `.gitignore`, **e** `Private=False` nel csproj, cosi' le interop non
vengono copiate affatto. A tempo di esecuzione si prende quella installata sulla
macchina (`SW_REDIST` per i percorsi non standard).

Verificato: in `Banco\bin\` c'e' **il solo eseguibile**.

### E il banco chiude quello che apre

Ogni piano lasciava aperto il pezzo creato, e lo stato li enumera tutti a ogni
lettura: 244 ms per documento solo per `GetNext()`. Dopo cinquanta casi il banco
rallenta se stesso di un minuto e mezzo per lettura.

Non e' comodita': finche' il banco si sporca da solo, **i tempi che misura non
sono i tempi del prodotto, sono i suoi**.

`corri_piani.js --pulisci` legge quali documenti c'erano **prima**, apre una
parte pulita per ogni piano, e **chiude dopo ognuno** —
`ChiudiScarti --risparmia` tiene fuori quelli di chi stava lavorando.

Lo stesso piano dodici volte, nei due modi (`Banco\misura_accumulo.js`):

```
MAI chiusa:  10.2 11.1 11.4 11.1 11.3 12.1 12.2 13.3 13.8 13.9 15.2 15.4
             primo 10.2 s, ultimo 15.4 s (x1.5)   alla fine 12 documenti

CHIUSA dopo ogni piano:
             10.7 10.7 10.1 10.3 10.1  9.9 10.9 10.0 10.1 10.6 10.4 10.2
             primo 10.7 s, ultimo 10.2 s (x1.0)   alla fine 0 documenti
```

**0,43 secondi per documento accumulato.**

Due errori di metodo prima di arrivarci, e sono lo stesso errore — misurare una
cosa credendo di misurarne un'altra:

* la prima misura metteva in fila diciannove piani **diversi** e leggeva i tempi
  crescere. Crescevano perche' il primo era un tubolare e gli altri ruote
  dentate da 336 segmenti: si stava leggendo l'ordine dell'elenco;
* la prima pulizia chiudeva solo **alla fine** del giro. I documenti si
  accumulano durante, ed e' durante che lo stato li enumera tutti: i tempi erano
  identici a quelli senza pulizia.

Il confronto che isola la variabile giusta e' lo stesso piano ripetuto.

---

## 0.19.0 — 14 settembre 2026 · libreria 0.58.0

**Il conteggio degli errori si da' come delta, non come numero.**

«Il pezzo ha 1 lavorazione in errore» dopo un piano sembra dire «il piano ha
rotto qualcosa». Puo' voler dire l'opposto: il pezzo era gia' rotto quando e'
arrivato. Il numero assoluto **mente su chi sia stato**.

```
piano che rompe:
  lavorazioni_in_errore: 1   lavorazioni_in_errore_prima: 0
  nota_errori: "...questo piano ne ha rotte 1."

piano innocente su un pezzo gia' rotto:
  lavorazioni_in_errore: 1   lavorazioni_in_errore_prima: 1
  nota_errori: "Il pezzo aveva gia' 1 lavorazioni in errore prima di questo
                piano...: non le ha rotte questo piano."

pezzo sano che resta sano:
  (niente)
```

La terza riga conta quanto le prime due: ripetere «tutto normale» a ogni
risposta e' posto tolto a cio' che conta.

### Il canale MISURARE c'era gia', e adesso ha dentro le due letture

`guarda_il_pezzo` e' uno strumento a se', `readOnlyHint`, che non passa da
nessuna conferma perche' non disegna — cioe' esattamente la via (c) che la voce
D5 proponeva. Non serviva costruirne un altro: serviva metterci dentro l'albero
(fatto nella 0.18.0) accanto a volume e corpi, che c'erano gia'.

| caso | atteso | misurato |
|---|---|---|
| cubo 10 × 10 × 10 sano | `GetWhatsWrongCount` = 0 | **0**, volume 1000, 1 corpo |
| meno un taglio 4 × 4 passante | 840 mm³ | **840**, 1 corpo |

### Un caso che non si riproduce, ed e' una buona notizia

Il criterio chiedeva un raccordo R=20 su uno spigolo da 10, con codice 18. La
feature **non viene creata affatto**: l'operazione dice «Il raccordo non e'
stato creato» e l'albero resta pulito. La libreria non lascia dietro feature
rosse quando l'operazione fallisce.

Il caso rosso vero e' l'altro: una lavorazione **valida** che diventa impossibile
per una modifica successiva (0.18.0, codice 12).

---

## 0.18.0 — 14 settembre 2026 · libreria 0.58.0

**Lo stato dice quando una lavorazione e' rotta.** Prima non lo diceva, ed era
un difetto che non dava errore **dentro gli occhiali**: il modello guardava un
pezzo rotto e vedeva un pezzo sano.

### Il rosso, costruito prima di ripararlo

Piastra 100 × 60 × 20, raccordo da 8 sugli spigoli in cima, poi la larghezza
portata a **10**. Due raccordi da 8 su 10 mm non ci stanno, e SOLIDWORKS lo sa:

```
GetWhatsWrongCount: 1
Raccordo1 [Fillet] errore=12  swFeatureErrorFilletInvalidRadius
```

`guarda_il_pezzo` rispondeva `{"uid":"Raccordo1","tipo":"Fillet","raggio_mm":8}`
— un raccordo normale. E il volume era **3.000**, cioe' esattamente 100 × 10 × 3:
il raccordo non aveva fatto niente.

### Adesso

```
lavorazioni_in_errore: 1
"errore": "il raggio del raccordo non va bene per questa geometria: di solito
           vuol dire che e' piu' grande di quanto ci stia"
"errore_codice": 12
```

Sullo stesso pezzo riparato il campo **non compare affatto**. Le due prove vanno
in coppia: senza la seconda, la prima non direbbe niente.

### `NeedsRebuild2` non bastava

Sul pezzo rotto rispondeva **0**, mentre `GetWhatsWrongCount` diceva 1. «Va
ricostruito» e «e' rotto» sono due domande diverse, e usare la prima per
rispondere alla seconda avrebbe dato una guardia **sempre verde**.

### Quello che non si sa resta un numero

`ErroriFeature.cs` traduce i codici conosciuti — raccordi (10-19), estrusioni
(30-37), il riferimento penzolante (51). Per gli altri esce il nome dell'enum e
il numero.

> Un nome inventato su un codice e' peggio del numero nudo, perche' sembra una
> spiegazione.

### La finestra che pianta il banco

`EsecutorePiano` spegne `swShowErrorsEveryRebuild` per la durata del piano e la
rimette com'era. Una ricostruzione con un errore apre un modale, e la chiamata
COM che l'ha scatenato **non torna piu'**: stessa lezione di
`swInputDimValOnCreate`. Provato con la finestra **accesa**: il piano che rompe
il pezzo va fino in fondo e l'opzione torna al valore di prima.

### Attrezzo nuovo

`Controlli\AlberoRosso` — `NeedsRebuild2`, `GetWhatsWrong` e il codice di errore
di ogni feature, con il nome dell'enum accanto.

---

## 0.17.0 — 14 settembre 2026 · libreria 0.57.0

**I tagli portano le loro quote.** `taglio_cilindrico` mette il diametro,
`taglio_rettangolare` lunghezza e larghezza. Senza, quei pezzi non erano
modificabili con `modifica_quota`: si potevano fare e non si potevano
correggere, che e' la lamentela piu' frequente dell'officina.

### Misurato

Piastra 120 × 80 × 20, foro Ø12 passante, tasca 24 × 16 passante:

```
volume: 182.058,1     atteso 192.000 - pi*6^2*20 - 24*16*20 = 182.058,05
```

Poi `modifica_quota` sul diametro, 12 → 20:

```
prima 182.058,1   dopo 178.036,8   differenza 4.021,3
atteso pi*(10^2-6^2)*20 = 4.021,24
```

### Due decisioni, e il perche'

**Si quota la DIMENSIONE, non la posizione.** La posizione vorrebbe un
riferimento da cui misurare, e sceglierlo per conto di chi disegna vuol dire
vincolare lo schizzo piu' di quanto abbia chiesto: il `modifica_quota`
successivo fallirebbe con un messaggio di sovra-vincolo che non parla di niente.

**Le quote nascono marcate per il disegno** (`IDisplayDimension.MarkedForDrawing`).
`quote_dal_modello` di serie importa solo quelle — una tavola con tutte le quote
del modello e' illeggibile — quindi una quota non marcata non sarebbe mai
arrivata in tavola.

### La Z che mancava

`QuotaSegmenti` passava ad `AddDimension` il punto del testo come `(x, y, 0)`.
Va bene per uno schizzo su un piano di riferimento, dove Z e' zero per
costruzione; **non** per uno schizzo su una faccia qualunque, che e' dove stanno
tutti i tagli. Adesso il punto ha tre coordinate.

### Non funziona ancora: `quote_dal_modello`

Le quote arrivano nel pezzo e **non arrivano in tavola**. Non e' una regressione:
quell'operazione non ha mai funzionato, e solo adesso c'e' qualcosa da importare.

Misurato: 16 quote nel pezzo, tutte marcate; tre viste risolte; **zero**
importate. Provati tre versioni dell'API, cinque valori di fonte, quattro
maschere di tipo, la vista attivata e la vista selezionata, l'opzione di
documento di inserimento automatico accesa, e gli schizzi sui due piani. Due
conti indipendenti d'accordo su zero. Dettaglio e cosa resta da provare nella
voce D3 del `TODO.md`.

### Trovato per strada: un'opzione chiesta al livello sbagliato non si lamenta

`swDetailingAutoInsertDimsMarkedForDrawing` (248) e' una preferenza di
**documento**. Chiedendola all'**applicazione**: `GetUserPreferenceToggle`
risponde `False`, `SetUserPreferenceToggle(248, true)` **non fallisce**, e la
rilettura da' ancora `False`. Nessun errore, nessun avviso, nessun effetto.

> Un'opzione che «non si lascia cambiare» quasi sempre e' un'opzione chiesta al
> livello sbagliato.

### E un'insidia dell'interop

`ISketchSegment.GetType()` **non** e' quello di `Object`: l'interop lo nasconde
con un metodo suo che torna il tipo di segmento come intero (0 = linea). Scrivere
`.Name` dietro non compila — ed e' un bene, perche' se compilasse si leggerebbe
un nome di classe al posto di una linea.

### Attrezzi nuovi

| | |
|---|---|
| `Controlli\QuoteInTavola` | prova tutte le combinazioni di inserimento e **conta** dopo ognuna |
| `Controlli\Opzione` | legge e scrive una preferenza ai due livelli, applicazione e documento |
| `Controlli\ApiQuote` | quali API e quali costanti esistono davvero, per parola |

---

## 0.16.0 — 14 settembre 2026 · libreria 0.56.0

**Il documento si tiene per mano, non per nome.** E un piano puo' finalmente
fare una tavola e riempirla.

### Il difetto che la 0.15.0 aveva appena creato

La 0.15.0 aveva dato un'identita' ai documenti senza file (`titolo:<titolo>`).
Sembrava giusto, e si e' rotto al primo piano vero:

```
passo 1  estrudi_rettangolo   ok
passo 2  salva_documento      ok
passo 3  crea_disegno         RIFIUTATO
         «Il documento attivo e' cambiato mentre eseguivo: il piano era
          per «titolo:Parte131». Non ho toccato niente.»
```

Il documento **non era cambiato**: si era *salvato*. E salvando cambiano tutti e
due i nomi — il percorso da vuoto a un file, il titolo da «Parte131» a «z06».
**Nessuna stringa sopravvive a un salvataggio.**

Adesso l'identita' e' l'**oggetto COM**: si prende una volta all'inizio e si
confrontano i puntatori a `IUnknown` (con le `Release` che servono — un
conteggio che non torna a zero e' un documento che SOLIDWORKS non chiude piu').
Il nome resta, ma solo per i messaggi.

Vale anche per l'add-in, che prima confrontava i percorsi: un `salva con nome` a
meta' piano lo avrebbe fermato allo stesso modo.

### `Esito.DocumentoNuovo`: l'unico cambio di documento che si dichiara

Con l'identita' riparata, il piano «pezzo → tavola → viste» si fermava lo stesso
— e stavolta **a ragione**: `crea_disegno` cambia documento davvero, perche' una
tavola *e'* un documento nuovo.

La difesa non si spegne: l'operazione **dichiara** il cambio, e chi esegue lo
segue soltanto se e' dichiarato.

```
[agente] passo 3: crea_disegno
[agente] tavola creata da <impostazioni SOLIDWORKS>\...
[agente] da qui in poi si lavora su «titolo:Disegno3 - Foglio1»
[agente] passo 4: viste_standard        -> viste_totali: 3
```

Un documento che cambia **senza** che nessuno lo dica resta un motivo per
fermarsi: e' la difesa che impedisce di estrudere nel pezzo di qualcun altro.

### Misura 0.6 chiusa: si disegna sul foglio, e la sezione esce

Era l'ultima delle dieci misure della sezione 0, e sblocca GT4 (sezioni e
dettagli in tavola).

```
linea tracciata in x=0.0945, da y=0.2074 a y=0.2516
linea selezionata: True, oggetti selezionati: 1, tipo 10 (swSelSKETCHSEGS)
CreateSectionViewAt5 ha risposto: un oggetto
viste sul foglio: 4 -> 5
  -> LA SEZIONE C'E'. Ultima vista: «Vista in sezione A-A»
```

**Una cosa che si credeva e non era vera:** `ISheet.GetTemplateSketch()` **non**
e' la superficie di disegno del foglio — e' il CARTIGLIO, 71 segmenti invariati
prima e dopo. La linea finisce nello schizzo della **prima vista**, che *e'* il
foglio (`Type = 1`), e si rilegge con `IView.GetSketch()`.

Due accorgimenti che non si deducono: la linea deve attraversare la vista da
parte a parte (una che la sfiora da' una sezione vuota **senza dire niente**), e
lo schizzo va chiuso con `InsertSketch(true)` prima di chiedere la sezione.

E il riscontro e' **il conto delle viste**: `CreateSectionViewAt5` puo'
rispondere un oggetto senza aver fatto niente. 4 → 5 e' la misura.

### Attrezzi nuovi

| | |
|---|---|
| `Controlli\Annulla` | un solo Ctrl+Z, col volume prima e dopo |
| `Controlli\SchizzoSulFoglio` | dove finisce una linea tracciata sul foglio, e come si riprende |
| `Controlli\SezioneInTavola` | il giro completo fino alla sezione, col conto delle viste |

---

## 0.15.0 — 14 settembre 2026 · libreria 0.55.0

**Il ponte esegue i piani con `EsecutorePiano`, lo stesso giro dell'add-in.**
Aveva un giro suo — un `OperazioniSw` nuovo a ogni passo e `passiFatti` a
`null` — e costava tre cose. Le prime due sono misurate qui sotto, **prima e
dopo**; la terza e' che una seconda implementazione dello stesso ciclo sono due
comportamenti che divergono, e le prove si fanno su una sola delle due.

### 1. Un passo puo' nominare quello che ha fatto il passo prima

Un piano di due passi — piastra, poi foro sulla sua faccia in cima:

| | |
|---|---|
| **prima** | passo 1 riuscito (volume 120.000), passo 2 **rifiutato**: «cita il passo 1, ma di quel passo non ho la geometria» |
| **dopo** | `[agente] faccia presa dal passo 1` — tutti e due riusciti |

Non era una mancanza dell'add-in: `Riferimenti` e `passiFatti` esistevano gia' e
funzionavano. Il ponte semplicemente non li usava, perche' passava `null`.

### 2. Un solo Ctrl+Z disfa il piano intero

Stesso piano di due estrusioni, volume 133.500. **Un solo** annullamento:

| | volume dopo |
|---|---|
| **prima** | **120.000** — disfatto solo il secondo passo |
| **dopo** | **nessun corpo solido** — disfatto tutto |

Misurato mettendo da parte la modifica e ricostruendo, non dedotto dal codice.

### Il piano si controlla TUTTO prima di cominciare

Nome dell'operazione, percorsi e forma degli argomenti si vedono leggendo. Prima
si controllavano a mano a mano: un errore di forma al passo 3 fermava il piano
**con i primi due gia' costruiti**, e nel documento restava mezzo pezzo per una
cosa che si poteva dire senza aprire niente.

Misurato: piano con passo 1 valido e passo 2 senza `diametro_mm` →
`corpo_solido: false`, `feature: []`, volume `null`. Non e' stato toccato niente.

E ogni rifiuto di forma lo **dice**: «Il piano non e' stato eseguito per niente:
il documento e' rimasto com'era.» Senza quella frase la risposta elenca solo il
passo sbagliato, e chi legge si mette a cercare nel documento una piastra che
non c'e'.

### `DocumentoSenzaFile`, e il controllo che lo sorveglia

`EsecutorePiano.ControlliPreliminari` rifiuta i documenti mai salvati — giusto
per l'add-in, che vive in un PDM dove un file non salvato non e' in check-out;
sbagliato per il ponte, che lavora apposta su `Parte1` appena aperta.

Il controllo e' stato **parametrizzato, non cancellato**, e il pericolo era
scritto nella voce B8: che qualcuno lo accenda anche nell'add-in «perche' dava
fastidio». Per questo:

* il nome dice cosa **permette**, non cosa disattiva;
* il predefinito e' falso;
* `Controlli\controlla-documenti-senza-file.ps1` esce **1** se la proprieta' si
  accende fuori da `Ponte\Sessione.cs`, o se compare nelle sorgenti dell'add-in.
  Tutte e due le strade sono state **viste fallire** prima di crederci.

Quando le sorgenti dell'add-in non sono raggiungibili il controllo **non dice
che va bene**: dice a voce alta che quella meta' non l'ha guardata.

### Un documento senza file adesso ha un nome

`EsecutorePiano` confrontava i percorsi nudi per sapere se il documento era
ancora quello di prima. Due documenti mai salvati hanno **tutti e due percorso
vuoto**: il controllo che esiste per non scrivere nel pezzo di qualcun altro li
avrebbe detti lo stesso documento, proprio nel caso in cui il ponte lavora
sempre. Adesso l'identita' e' il percorso quando c'e', e `titolo:<titolo>`
quando non c'e'.

### Tolto `Sessione.Esegui`

Dopo questo cambio non lo chiamava piu' nessuno: 67 righe di codice morto, via.

### Nessuna regressione

| | |
|---|---|
| ruote e sezioni | 12.263,3 · 86.393,8 · 1.500.000 — identici |
| le quattro sagome rifiutate | ancora rifiutate, documento intatto |
| `ProvaRuota`, `ProvaSagoma`, `controlla-catalogo` | 0 |
| `prova-installatore` | 11 PASS, 0 FAIL |

---

## 0.14.0 — 14 settembre 2026 · libreria 0.54.0

**I contorni vengono controllati prima di disegnarli.** Era l'ultimo pezzo di
C4, e chiude la voce: `_pulisci_contorni` e `_controlla_contorni` esistevano
solo nel server Python, quindi un piano che arrivava al ponte **per la via
diretta** non passava da nessun controllo geometrico.

La geometria sta in `Riscontro.Disegno\FormaChiusa.cs`, in un file suo e non
dentro l'esecutore, per un motivo pratico: cosi' si prova senza aprire
SOLIDWORKS. Un controllo geometrico che si puo' provare solo a CAD aperto viene
provato poco.

### Il difetto silenzioso, misurato

Una piastra 100 × 100 con un cerchio Ø30 **staccato**, a 200 mm di distanza:

```
riuscita: true
corpi_totali: 2
ingombro: [265, 10, 100]        volume: 107.068,6   (100.000 + 7.068,6)
```

Un vuoto fuori dal bordo non fora: SOLIDWORKS ne fa un **secondo corpo**, dice
«riuscita», e nessuno se ne accorge leggendo «sagoma con 5 contorni».

Gli altri tre casi provati — vuoto che **scavalca** il bordo, contorno a
papillon, contorno composito con i tratti staccati — SOLIDWORKS li ferma gia'.
Ma dice soltanto **«L'estrusione non e' stata creata»**, che non fa capire dove
sia il guaio. Li' il guadagno non e' il rifiuto: e' la spiegazione, con il punto.

### I quattro controlli, e cosa dicono

| | messaggio |
|---|---|
| tratti che non si attaccano | «il tratto 1 finisce in (50, 0) e il seguente comincia in (52, 0)» |
| contorno che si incrocia | «il lato che parte da (100, 0) ne attraversa un altro» |
| area nulla | «i suoi punti stanno tutti sulla stessa retta» |
| vuoto fuori dal bordo | «il punto (215, 0) e' fuori» |

Ogni messaggio porta **le coordinate**: «si incrocia» su una sagoma da duecento
vertici non aiuta nessuno a ripararla.

Un messaggio e' stato **corretto perche' affermava il falso**: diceva
«SOLIDWORKS non lo rifiuterebbe» del contorno che si incrocia, e la misura dice
che invece si ferma. Adesso dice quello che succede davvero, compreso il
messaggio inutile che si riceverebbe.

### I limiti, che sono di conteggio e non di quota

| | |
|---|---|
| contorni per sagoma | 20 |
| vertici per contorno, **scritti da un modello** | 200 |
| vertici per contorno, **generati da una ricetta** | 4000 |

Quante volte si puo' spezzare una linea, non quanto puo' essere lunga: una
piastra da tre metri e' legittima, una sagoma da cinquemila vertici scritta da
un modello quasi mai. Le ricette passano dal tetto largo perche' i loro punti li
calcola una formula — una ruota da 127 denti sono 1016 segmenti.

### Nessuna regressione, e i volumi lo dicono

Tutte le ricette passano ora dai controlli nuovi. Volumi identici a prima:

| | |
|---|---|
| ruote | 12.263,3 · 43.964,1 · 8.808,6 · 54.999,8 |
| sezioni | 1.500.000 · 392.699,1 · 86.393,8 · 47.500 · 77.942,4 · 60.000 · 68.400 |

Su z=42 i segmenti scendono da 420 a 336 — e' il guadagno della 0.13.0 sul
piede a lunghezza zero — **a volume invariato**.

### `Controlli\ProvaSagoma`, e perche' meta' dei casi devono passare

Un controllo che rifiuta troppo e' peggio di uno che rifiuta poco: blocca lavoro
buono e non si scopre finche' qualcuno non ci sbatte. Meta' dei casi della prova
sono sagome legittime che **devono** passare:

* **l'elle concava** e **la stella a cinque punte** — chi confonde «concavo» con
  «si incrocia» rifiuta meta' delle staffe che esistono;
* **il triangolo** — con tre lati sono tutti adiacenti fra loro, e chi non salta
  le coppie adiacenti accusa ogni triangolo;
* **il quadrato**, che ha quattro vertici e il quarto non e' un doppione: togliere
  sempre l'ultimo punto e' l'errore facile da fare in `Anello`;
* **il punto nell'incavo dell'elle**, dentro l'ingombro e fuori dal materiale:
  smaschera un «sta dentro» fatto con la scatola invece che col poligono.

---

## 0.13.0 — 14 settembre 2026 · libreria 0.53.0

**`ruota_dentata` si puo' eseguire.** Era l'ultima delle due operazioni che il
catalogo dichiarava senza saperle fare. Le operazioni dichiarate e non
eseguibili passano da **una a zero**, e `Controlli\controlla-catalogo.ps1` esce
finalmente **0**.

La geometria sta in `Riscontro.Disegno\RuotaDentata.cs`: evolvente vera, fianchi
a spline sui punti calcolati dalla formula, foro e cava per linguetta.

### Due difetti che non davano errore, trovati senza aprire SOLIDWORKS

`Controlli\ProvaRuota` confronta i punti che escono dal generatore con le
formule ISO. Esce 0/1, non apre il CAD, e ha trovato questo:

**(1) Da 42 denti in su il fianco partiva sotto il fondo del dente.** Il cerchio
di base sta sotto il fondo quando `cos α < 1 − 2,5/z`, cioe' da z=42 con l'angolo
standard di 20 gradi. Su z=127 m=1 il fianco scendeva **2,58 mm sotto il
diametro di fondo** e il contorno si tagliava da solo. Il fianco ora parte da
`max(rb, rf)`.

Nessuna prova lo prendeva: i casi al limite erano z=16, 17, 20, 24, e z=40 —
che pure era stata provata — sta dalla parte giusta del salto **per 33 micron**.

**(2) Il fondo era agganciato all'angolo sbagliato**, e il primo fix non bastava.
ψ e' il semiangolo del dente **sul cerchio di base**; quando il fianco parte piu'
in alto, al fondo il dente si e' gia' stretto di `scarto(tMin)`. Usando ψ per
entrambi, su z=127 il fondo partiva a 0,027272 rad e finiva a 0,022208:
**percorreva il giro all'indietro**. SOLIDWORKS diceva soltanto «L'estrusione non
e' stata creata».

Di contorno, la retta radiale del piede esiste ora solo quando c'e' da scendere
davvero — su z=42 era lunga **0,9 micron** — e i segmenti per dente scendono da
10 a 8 dove la base sta sotto il fondo: **1016 invece di 1270** su z=127.

### Il controllo che ha preso il secondo, ed e' generale

Srotolando l'angolo lungo il contorno, **un contorno chiuso non torna mai
indietro**. Un passo negativo e' un'autointersezione, e si vede senza aprire
niente.

La soglia e' **calcolata, non scelta**: i punti escono arrotondati a 1e-4 mm, che
sul raggio piu' piccolo valgono `1e-4/rf` radianti di rumore; la soglia e' tre
volte tanto. Su z=20 fa 1,7e-5 contro 6e-6 misurati, su z=127 fa 4,8e-6 contro
1,7e-3 del difetto. Fra rumore e difetto ci sono due ordini di grandezza.

### Guardia nuova: i denti che si toccano alla base

Il vano al fondo misura `(2π/z − 2·psiRadice)·rf`. Si annulla oltre
`2,5·tan α = π/2`, cioe' per **α > 32,15 gradi**, e i denti si salderebbero fra
loro. Il messaggio da' il criterio e il numero:

> Con z=60, m=1 e un angolo di pressione di 35 gradi fra un dente e l'altro
> resterebbero -0.0955 mm di fondo: i denti si toccherebbero alla base.

### `GetBodyBox` non misura il diametro esterno di una ruota

Con z=20 l'ingombro e' `[44, 10, 44]`, esatto. Con z=17 e' `[37,79, 10, 37,94]`
invece di `[38, 10, 38]`.

Non e' approssimazione: la freccia della corda di cresta vale **0,0012 mm**. La
scatola coincide col diametro esterno **solo quando un dente cade sull'asse** —
con 20 denti il passo e' 18 gradi e ci cade, con 17 e' 21,18 gradi e no. Il
criterio scritto nel TODO funziona perche' fissa z=20.

### I casi al limite

| caso | esito | numeri |
|---|---|---|
| z=20 m=2 | ok | Ø esterno 44, volume 12.263,3 |
| z=17 m=2 | ok | limite di intaglio, `floor(2/sin²20°) = 17` |
| z=16 m=2 | rifiutata | «sotto 17 denti serve la correzione di profilo» |
| z=24 m=3 foro Ø20 | ok | ingombro `[78, 12, 78]` |
| z=24 m=3 foro Ø20 + cava | ok | volume 43.964,1 |
| z=20 m=2 foro Ø34 | rifiutata | «fondo dente a 35 mm: non lascia materiale» |
| z=20,5 | rifiutata | «mezzo dente non si taglia» |
| z=42 m=2 | ok | Ø esterno 88 — primo caso oltre il salto |
| z=127 m=1 | ok | Ø esterno 129, 1016 segmenti |
| z=5 m=4 α=45° | rifiutata | «si chiuderebbe a punta» |
| z=60 m=1 α=35° | rifiutata | «si toccherebbero alla base» |

Il limite di intaglio e' arrotondato **per difetto**: `2/sin²20° = 17,097`, e una
ruota da 17 denti si fa tutti i giorni. Rifiutarla sarebbe il modo peggiore di
sbagliare.

Il caso z=5 m=4 a 20 gradi **non provava quello che sembrava**: veniva rifiutato
dall'intaglio, non dalla punta. Spostato a α=45°, dove il limite di intaglio
scende a 4 denti. Nella prova il motivo atteso e' ora scritto accanto al caso.

### La cava per linguetta, verificata per differenza di volume

43.964,1 mm³ con la cava contro 44.177,2 senza, su 12 mm: **17,76 mm²** di
sezione in piu'. Il conto per Ø20 (b=6, t2=2,8) da' 17,72 mm² — scarto 0,2%, nel
verso giusto perche' il foro e' un poligono a 180 lati **circoscritto**.

Questo verifica che la geometria rispetti la tabella `CaveLinguetta`, **non che
la tabella sia giusta**: resta da confrontare con la UNI 6604 vera.

### Due attrezzi nuovi, per il banco

| attrezzo | a cosa serve |
|---|---|
| `Controlli\ChiudiScarti` | chiude i documenti di prova. Solo quelli **mai salvati** o dentro le cartelle passate come argomento, e non salva mai |
| `Controlli\NuovaParte` | apre una parte vuota per far ripartire il banco |

Servono perche' ogni piano di prova lascia aperta una parte, e **ogni lettura di
stato le scorre tutte** a 244 ms di `GetNext()` ciascuna: 47 documenti chiusi in
una volta sola. Peggio ancora con un pezzo pesante: con la ruota da 1018 facce
lasciata aperta, due ruote banali dopo di lei hanno sforato i dieci minuti.

`NuovaParte` e' compilato e non uno script perche' `ISldWorks.NewDocument` per
late binding da PowerShell 5.1 risponde `TYPE_E_ELEMENTNOTFOUND`.

---

## 0.12.0 — 14 settembre 2026 · libreria 0.52.0

**`estrudi_sezione` si puo' finalmente eseguire.** Era dichiarata nel catalogo e
non esisteva nello switch: viveva solo perche' il server la espandeva in
`contorni` prima di mandarla, e il ramo `default` ricadeva sull'estrusione di
profilo trovandoceli. Chiamata dal ponte con i suoi argomenti veri — `tipo`,
`parete_mm` — rispondeva «operazione sconosciuta».

Le operazioni dichiarate e non eseguibili passano da **due a una**: resta
`ruota_dentata`.

### Sette sezioni, e ogni numero torna al calcolo

| sezione | volume misurato | verifica |
|---|---|---|
| tubolare 80×80×5 × 1000 | 1.500.000 | (80² − 70²) · 1000 |
| tondo Ø50 × 200 | 392.699,1 | π · 25² · 200 |
| tubo Ø60/Ø50 × 100 | 86.393,8 | π/4 · (60² − 50²) · 100 |
| angolare 50×50×5 × 100 | 47.500 | (50·5 + 45·5) · 100 |
| esagono chiave 30 × 100 | 77.942,4 | (√3/2) · 30² · 100 |
| U 50×40×5 × 100 | 60.000 | 2000 − 40·35 |
| T 60×60×6 × 100 | 68.400 | 360 + 324 |

L'esagono e' quotato **sull'apertura in chiave**, come si compra e come si
misura: l'ingombro esce 30 × **34,64**, cioe' 30/cos 30°. Confonderla con il
diametro circoscritto darebbe un esagono piu' grande del 15%.

L'asola 60×20 esce 11.163,3 contro gli 11.141,6 nominali, **+0,19%**: i
semicerchi stanno sul cerchio **circoscritto** e non su quello nominale. E'
voluto — un'asola con i semicerchi inscritti e' piu' stretta del nominale, e una
vite che ci deve passare non passa. Dove si approssima, si approssima in eccesso.

### I controlli sono fisici, e ognuno ha il suo difetto alle spalle

| chiesto | risposta |
|---|---|
| tubolare 80×80 con parete **40** | «non resta cavita'… a meta' esatta il vuoto ha area zero, SOLIDWORKS non lo segnala ed estrude una barra piena — che pesa quattro volte tanto ed e' verde come quella giusta» |
| angolare 50×50 con spessore **50** | «non e' piu' un profilo, viene fuori un piatto» |
| asola **20×20** | «la lunghezza deve essere maggiore della larghezza, altrimenti e' un cerchio» |
| tubolare senza `parete_mm` | «Per una sezione «tubolare» serve «parete_mm»» |

Nessuno dei quattro tocca il documento: zero facce dopo il rifiuto.

### Il criterio della voce era sbagliato, e il perche' e' finito nel catalogo

C4 chiedeva: tubolare 80×80×5 lungo 1000 → **1.443.000 mm³** entro lo 0,5%.
Misurato: **1.500.000**, che e' il 4% in piu' e sarebbe una bocciatura.

Il numero della voce e' quello del **profilo commerciale**, che ha gli angoli
raggiati. Questa ricetta — come quella Python da cui viene — fa **spigoli vivi**:
1.500.000 e' il valore giusto per quello che disegna.

Ma la differenza e' vera e conta: chi usa `estrudi_sezione` per pesare un pezzo
si porta a casa il 4% in piu'. **Il catalogo non lo diceva**, e adesso lo dice —
ed e' la prima modifica fatta con la strada nuova della 0.10.0: si tocca
`Operazioni.cs`, si riesporta, e il diff di `catalogo.json` e' **una riga sola**.

---

## 0.11.0 — 14 settembre 2026 · libreria 0.51.0

**Il telaio si chiede come lo documenta il catalogo, e «interasse» esiste.**

### La divergenza

Il catalogo dichiara `lunghezza_mm`, `larghezza_mm`, `piano`, `misure`,
`centrato` **al primo livello** — sono nel suo `required`, e la descrizione
promette che il telaio «si chiede con due quote invece che con otto
coordinate». Il C# invece cercava un oggetto annidato `telaio`, che e' la forma
che il server costruisce dentro `valida()` prima di mandare.

Quindi un modello che legge il catalogo e scrive quello che c'e' scritto **non
veniva servito** — e dal server non si vedeva, perche' il server riscrive in
mezzo. E' la stessa forma del difetto della 0.7.1, su un'operazione piu' grossa.

Adesso si accettano tutte e due le forme. Se ci sono tutte e due vince quella
annidata: chi la manda l'ha costruita apposta.

### `misure: "interasse"` era documentato e non implementato

Il catalogo lo elenca fra i valori ammessi da sempre. Il C# accendeva il blocco
del telaio **solo** su `ingombro`, e per tutto il resto cadeva nel ripiego dei
segmenti — che pero' questa operazione esiste apposta per non far scrivere. Chi
chiedeva un telaio a interasse si prendeva «non ho potuto ricavarne il
percorso».

Le due modalita', misurate sullo stesso telaio 1000×800 in tubolare 80×80×5, e
i numeri si spiegano a vicenda:

| `misure` | ingombro sul pezzo | |
|---|---|---|
| `ingombro` (predefinito) | **1000 × 800** | le quote chieste, misurate sul pezzo finito |
| `interasse` | **1080 × 880** | il percorso e' 1000×800, piu' 40 mm per lato di profilo |

### Il numero del 9 settembre, ricontrollato

Il criterio della voce C4 era: telaio 1000×800 in 80×80×5, ingombro esterno
**1000×800 entro 0,1 mm** — *«e' il difetto misurato il 9 settembre, quindi e'
proprio il numero da ricontrollare»*. Misurato: `[1000, 800, 80]`, quattro
barre, con la sporgenza rilevata a 80×80 e il percorso arretrato a 920×720.

### Trovato leggendo, e vale la pena dirlo

La voce chiedeva di portare in C# le 145 righe di `_segmenti_telaio` del server.
**Il grosso c'era gia', ed e' migliore del Python**: il C# non calcola quattro
angoli sul nominale, MISURA quanto sporge il profilo e arretra il percorso.
Mancavano tre cose piccole — i segmenti non piu' obbligatori, la forma del
catalogo, e `interasse` — non centoquarantacinque righe.

### Un cambio di comportamento, e va detto

Un piano che passava i `segmenti` gia' calcolati **insieme** alle due quote al
primo livello prima dava 1080×880; adesso da' 1000×800. Le quote del catalogo
**sono** l'ingombro esterno, quindi il nuovo numero e' quello giusto — ma e' un
numero diverso da ieri, e chi avesse tarato qualcosa su quello se ne accorge
qui. Per avere il vecchio comportamento si scrive `misure: "interasse"`.

---

## 0.10.0 — 14 settembre 2026 · libreria 0.50.0

**Il catalogo vive in C#, accanto al codice che esegue. `catalogo.json` e'
diventato un prodotto.**

```bash
.\bin\Riscontro.Catalogo.exe Strumenti\catalogo.json
```

Nient'altro. Prima ci volevano tre passi, due macchine e docker: impacchettare i
sorgenti del server, mandarli via `scp`, farli girare in un container
usa-e-getta, riportarsi indietro il file. **Chi clonava questo repository non
poteva rigenerare il catalogo**, e un file che non si puo' rigenerare e' un file
che nessuno osa toccare — mentre quel file e' l'interfaccia verso il modello.

La ragione piu' importante e' un'altra: **lo strato che DESCRIVE un'operazione
adesso e' lo stesso che la ESEGUE.** E' l'unica disposizione in cui
un'operazione descritta e non eseguibile non puo' entrare in catalogo senza che
qualcuno se ne accorga. `Provata` e' un `bool` C# accanto al `case` che esegue,
non una promessa scritta in un altro repository.

### Il criterio era diff vuoto, ed e' diff vuoto

```
[catalogo] identico a Strumenti/catalogo.json (72204 caratteri)
72.452 byte, identici byte per byte
```

### Generato, non traslitterato a mano

La voce C3 del piano chiedeva di traslitterare 2.051 righe di Python, e nella
sua nota di rischio diceva: *«duemila righe di traslitterazione fatte di sera
sono duemila occasioni di cambiare una parola in una descrizione senza
accorgersene — e quelle descrizioni sono l'interfaccia vera verso il modello»*.

Il C# e' stato **generato dal `catalogo.json` che c'era**, che di quel Python e'
gia' il prodotto ed e' il file che il ponte legge davvero. Le occasioni di
cambiare una parola diventano zero, e il criterio si verifica lo stesso. Il
generatore si usa una volta; da adesso il C# si modifica a mano, e sono 2.026
righe leggibili come l'originale.

### La forma del file e' parte del contratto

Rientro di uno spazio, accenti per esteso, a capo a solo avanzamento riga,
nessun a capo finale, chiavi nell'ordine in cui sono scritte. `Nodo` e' un tipo
apposta, e **non un `Dictionary<string, object>`**: un dizionario non promette
l'ordine delle chiavi, e se le chiavi ballano a ogni compilazione ogni commit
mostra un file intero cambiato e nessuno vede piu' la riga cambiata davvero.

### `build.ps1` lo rigenera, e dice se e' cambiato

Un catalogo che si rigenera a mano resta indietro. Adesso si riscrive a ogni
pacchetto, e la build stampa `invariato` oppure `RIGENERATO: N -> M byte` con
l'invito a guardare il diff.

Fuori dal file restano — **e si dicono, una per riga su stderr** — le operazioni
non provate e le famiglie senza nemmeno un'operazione. Un elenco che si accorcia
in silenzio non si nota.

### Cosa NON e' stato fatto, e perche'

`ai_disegno.py` e' ancora la fonte del catalogo **per il server**: finche' non
legge questo JSON, le fonti sono due.

Accertato con un grep, che la voce chiedeva: `CATALOGO_DISEGNO` ha **tre**
consumatori fuori da `ai_disegno.py` — `<un file del server>`,
`<un file del server>` e `:252` — e usano `.nome`, `.provata`,
`.precondizione`, tutti campi che il record C# ha. Ma `ai_disegno.py` tiene
anche `limiti` e `riassunto`, che non sono fra i sei esportati e servono a
`valida()`: l'inversione e' uno **spacco** del record, non una sostituzione. E'
lavoro sul server in produzione, che la voce C3 aveva gia' dichiarato fuori
dalla propria stima.

**Un criterio non rispettato alla lettera, e detto.** La voce chiedeva che
`aggiorna.md` non contenesse piu' le parole «server», «docker», «python». Ci
sono ancora, ma solo per dire **che non servono** e per registrare cosa resta.
Toglierle avrebbe reso il documento peggiore, non piu' pulito.

---

## 0.9.0 — 14 settembre 2026 · libreria 0.49.0

Due cose, e la seconda e' un rischio vivo su un file condiviso.

### 1. Il ponte fa rispettare gli schemi che gia' dichiara

Offriva trentasei operazioni con schemi dettagliati e poi non guardava se gli
argomenti li rispettassero: lo schema serviva solo a suggerire. Uno
`spessore_mm` dimenticato arrivava a valle come zero e diventava un'estrusione
di spessore nullo.

```
estrudi_rettangolo senza spessore_mm
  -> "Per «estrudi_rettangolo» serve «spessore_mm», e non c'e'.
      (QUANTO SI ESTRUDE, in mm. Per una piastra e' lo spessore; per una barra
       o un profilo e' la LUNGHEZZA…)"

estrudi_rettangolo con lunghezza_mm: "duecento"
  -> "Per «estrudi_rettangolo», «lunghezza_mm» vuole un numero e invece e'
      un testo («duecento»)."
```

**E non tocca il documento:** dopo i due rifiuti il pezzo ha 0 feature, 0
schizzi, 0 facce — nemmeno uno schizzo orfano.

**Solo la forma, mai il merito**, ed e' la riga da tenere in mente leggendo
`Ponte/Forma.cs`: c'e' il parametro obbligatorio, e' un numero dove serve un
numero, e' uno dei valori ammessi. Se quel numero *abbia senso* non si giudica —
allargare un foro ha il centro **esattamente nel vuoto**, e un controllo zelante
lo rifiuterebbe.

**Un intero e' un numero.** Il difetto piu' facile da scrivere qui sarebbe un
`type: number` che rifiuta il `20` di `"spessore_mm": 20` perche' il
deserializzatore l'ha fatto `int`. Provato: passa, e il blocco esce 400.000 mm³.
Provato anche il ventaglio — blocco, foro, ripetizione, specchio, il tubo con la
forma `cerchio`, e il telaio saldato con le sue strutture annidate: **nessun
rifiuto falso**.

### 2. La libreria dei profili: il caso accanto a quello riparato

`RichiudiLaLibreria` (add-in 0.45.0) non tocca il file se era gia' aperto prima
dell'inserimento, e la ragione scritta e' buona: se il progettista ci stava
lavorando, chiuderglielo sotto e' un danno.

**Ma «lasciarla com'era» conservava lo stato APERTO, non quello PULITO.**

Misurato: `QUADRO_COPIA.SLDLFP` era aperta **e pulita**; dopo un
`telaio_rettangolare` risultava `GetSaveFlag() = true`. Il codice la lasciava li'
apposta. E' esattamente il rischio che tutto questo esiste per togliere — un
file condiviso a un `Ctrl+S` di distanza dall'essere riscritto — nel caso
accanto a quello per cui era stato scritto.

Il flag non si puo' ripulire: `IModelDoc2.SetSaveFlag()` non prende argomenti,
sporca e basta. Quindi tre stati invece di due, e ognuno con la sua ragione:

| com'era prima | cosa si fa | perche' |
|---|---|---|
| **chiusa** | si richiude | l'abbiamo aperta noi |
| **aperta e pulita** | **si richiude lo stesso** | non si perde niente — era pulita — e non si lascia un file condiviso armato |
| **aperta e sporca** | non si tocca, e si avvisa | qualcuno ci sta lavorando, ed era gia' a rischio prima di noi |

Provati tutti e tre. In tutti e tre, dopo l'operazione: **nessun file su disco
aperto e modificato**, e la copia sul disco intatta — 99.918 byte, stesso
orario al nanosecondo.

Nel terzo caso SOLIDWORKS riapre la libreria come riferimento del saldato, ma
**pulita**, che e' lo stato giusto.

---

## 0.8.0 — 14 settembre 2026 · libreria 0.48.0

**`modifica_quota` dice anche quando una lavorazione cambia natura, non solo
misura.**

### Il difetto, misurato

Piastra 200×100×20 con una tasca **cieca** Ø60 profonda 5. Si porta lo spessore
a 3 — una modifica di quota qualunque, di quelle che si fanno dieci volte al
giorno. La tasca diventa **passante**.

Prima di questa versione, tutto quello che si vedeva era:

```
riuscita: true
volume_mm3: 51.517,7    volume_cambiato_mm3: -334.345,13
```

Il volume torna — `60.000 − π·900·3 = 51.517,7`, esatto. I corpi sono sempre
uno. La ricostruzione e' riuscita. E **SOLIDWORKS non ha niente da dire**:
`GetErrorCode2` risponde 0 e `IsWarning` e' falso su tutte le feature.

Una sede cieca e un passaggio sono due pezzi diversi. Quello che ci si appoggia
dentro, in uno cade dall'altra parte.

### Adesso

```
lavorazioni_con_meno_facce: ["Taglio-Estrusione1 2 -> 1"]
nota: "La quota nuova ha cambiato l'albero, non solo le misure:
       hanno perso facce: Taglio-Estrusione1 2 -> 1. Una lavorazione cieca che
       perde una faccia di solito e' diventata PASSANTE: il fondo non c'e' piu'."
```

### Si riporta, non si rifiuta

Assottigliare una piastra finche' una tasca diventa passante puo' essere
esattamente cio' che si vuole. Rifiutarlo sarebbe rifiutare cio' che e' soltanto
insolito — il difetto peggiore dei due. Ma chi ha chiesto la modifica deve
poterlo **leggere qui**, invece di scoprirlo in officina.

E il controllo tace quando deve: portando lo stesso pezzo a 12 mm — la tasca
resta cieca — `nota` e' `null`. Le due prove vanno in coppia, e senza la seconda
la prima non direbbe niente.

### Il conto ha un tetto, e non e' prudenza generica

Leggere le facce di ogni lavorazione dell'albero costa circa **200 ms a
feature** (misurato). Su un pezzo con centinaia di lavorazioni questa lettura da
sola durerebbe piu' dell'operazione: oltre 200 si smette e si riporta quello che
si e' visto.

---

## 0.7.1 — 14 settembre 2026 · libreria 0.47.1

**Il tubo scritto come lo scrive il catalogo adesso si disegna.**

Nella descrizione di `estrudi_profilo` il catalogo porta questo esempio per
esteso:

```
Esempio, TUBO diametro esterno 60 spessore 5:
[{"cerchio": [0, 0], "diametro_mm": 60}, {"cerchio": [0, 0], "diametro_mm": 50}]
```

Il parser capiva `{"tipo":"cerchio","centro":[x,y],"diametro_mm":d}` e **non**
questa. Un modello che leggeva il catalogo e copiava l'esempio scritto li' dentro
finiva nel ramo poligonale, cercava `vertici`, non lo trovava e falliva.

**Una divergenza fra cio' che si dichiara e cio' che si sa fare, dentro il file
che esiste per non averne.** E sulla forma peggiore: e' quella che il catalogo
**raccomanda** per fori e tondi, con l'avvertenza che un cerchio approssimato a
segmenti ha il materiale dentro la circonferenza e un albero non ci entra.

Misurato, il tubo del catalogo:

```
volume 86.393,8 mm³   =   π/4 · (60² − 50²) · 100
```

### Mancante e non numerico sono due cose diverse

Prima finivano tutte e due in «il diametro non e' un numero», perche' la chiave
assente sollevava dentro lo stesso `try`. Chi ha scritto `{"cerchio":[0,0]}` e
basta va mandato a mettere il diametro, non a controllare un numero che non ha
scritto. Provati tutti e due:

| scritto | risposta |
|---|---|
| `{"cerchio":[0,0]}` | «Un cerchio senza "diametro_mm": un centro da solo non dice quanto e' grande.» |
| `{"cerchio":[0,0],"diametro_mm":0}` | «Il diametro del cerchio e' 0: un cerchio di diametro zero o negativo non si disegna.» |

### Una cosa che il piano diceva e non era vera

Il `TODO.md` dava per non supportata anche la forma `{"vertici":[[x,y],...]}`.
**Lo e':** e' il ramo predefinito del parser. La divergenza vera era una sola.

E il volume atteso che il piano riportava — «863.938 mm³» — e' l'**area della
sezione in mm²** con l'etichetta sbagliata: per il volume va moltiplicata per i
100 mm di spessore.

---

## 0.7.0 — 14 settembre 2026

**Adesso e' vero: un file solo, e chi lo installa deve avere soltanto
SOLIDWORKS.**

La 0.5.0 aveva dovuto scrivere una correzione onesta: «installabile da chiunque,
usabile da chi ha gia' l'add-in». Il carico non poteva contenere le operazioni
perche' stavano in `OpenPDM.SwAddin.dll`, codice privato che un pacchetto
pubblico non puo' portarsi dietro senza pubblicarlo.

Da quando stanno in `Riscontro.Disegno.dll`, dentro questo repository, quella
ragione non c'e' piu'. **`dist\Installa.exe` — 324 kB — se le porta dentro.**

### Provato installando davvero, non solo compilando

Installazione silenziosa in una cartella vuota, senza OpenPDM da nessuna parte:

```
[ok] Ponte da installare: incluso in questo installatore
[ok] Operazioni di disegno: Riscontro.Disegno.dll e' dentro l'installatore
  scritto SolidWorksMcp.exe (32 kB)
  scritto catalogo.json (70 kB)
  scritto Riscontro.Disegno.dll (172 kB)
  ...
[prova] agganciata: True
[prova] volume_mm3 = 396858,4
[prova] il ponte sta in piedi.
```

E poi un piano vero **eseguito dal ponte installato**, non da quello di
sviluppo: blocco 200×100×20, foro Ø10 passante, ripetizione di 4 a passo 30.
`volume_cambiato -4.712,39`, `facce 7 → 10`, `copie_fatte 3`. Gli stessi numeri
del ponte compilato in casa.

### Tre cose cambiate nell'installatore, e il perche' di ognuna

1. **La libreria non si cerca piu' sulla macchina, si estrae dal carico.**
   Cercarla aveva senso quando il pacchetto non poteva contenerla. Continuare
   vorrebbe dire installare, su una macchina che ha per caso una copia vecchia
   da qualche parte, **quella** copia — con le operazioni di due settimane fa e
   nessuno che lo dica.
2. **Se manca, adesso e' bloccante.** Prima era un avviso, ed era giusto: non
   avere l'add-in era normale, il ponte partiva e diceva di non essere
   agganciato. Adesso le operazioni sono parte del prodotto: se mancano il
   pacchetto e' rotto, e un pacchetto rotto che si installa lo scopre chi lo
   usa.
3. **Il campo «dove sta l'add-in» e' sparito dalla finestra.** Chiedere a chi
   installa un file che il pacchetto gia' contiene non e' una gentilezza: e' una
   domanda senza risposta giusta, e chi non sa cosa rispondere pensa di aver
   sbagliato qualcosa. Al suo posto una riga in sola lettura che dice da dove
   vengono le operazioni.

### Via la riflessione (B7)

Il ponte chiama `Riscontro.Disegno` direttamente: `new OperazioniSw(...)`,
`StatoDocumento.Leggi(...)`, `esito.Misure`. I nomi dei tipi non sono piu'
stringhe, e un errore di battitura lo prende il compilatore invece del cliente.

Resta il late binding verso SOLIDWORKS — `DocumentoAttivo`, `Identita`,
`Chiama` — e non e' una svista: e' la strada misurata (`ActiveDoc` e non
`IActiveDoc2`, vedi il commento nel codice), e rifarla qui vorrebbe dire
ripagare una misura gia' pagata.

**Trappola trovata per strada:** `SolidWorks.Interop.sldworks` ha un tipo
`Environment`, che rende ambiguo `Environment.GetEnvironmentVariable`. Le tre
chiamate sono qualificate `System.Environment`, con la riga che dice perche'.

### La proprieta' che non decide piu' niente, e lo dice

`RISCONTRO_DISEGNO` e `OPENPDM_ADDIN` non scelgono piu' quale DLL caricare —
.NET la carica da solo accanto all'eseguibile. Restano lette per **avvisare**:
se puntano altrove, il ponte lo dice invece di lasciar credere che contino
ancora. Una variabile d'ambiente che non fa niente e' peggio di una che non
esiste.

Per la stessa ragione un percorso inesistente non e' piu' un rifiuto ma un
avviso: fermarsi per un percorso sbagliato mentre la libreria giusta e' gia' in
mano sarebbe rifiutare cio' che funziona.

---

## 0.6.0 — 14 settembre 2026

**Il ponte non carica piu' il PDM per disegnare un foro.**

Le 36 operazioni sono uscite da `OpenPDM.SwAddin.dll` e vivono in
`Riscontro.Disegno.dll`, dodici file e 10.714 righe, dentro questo repository.
Il ponte carica quella.

### Cosa cambia per chi usa il ponte

Il file da mettere accanto all'eseguibile e' **`Riscontro.Disegno.dll`**, non
piu' `OpenPDM.SwAddin.dll`. La variabile d'ambiente e' `RISCONTRO_DISEGNO`;
`OPENPDM_ADDIN` resta accettata, perche' chi ce l'ha impostata non deve
scoprirlo da un rifiuto.

E se punta ancora al vecchio add-in, il ponte lo dice per nome invece di
lamentare un tipo mancante:

> `«…\OpenPDM.SwAddin.dll» e' il vecchio add-in OpenPDM. Dalla 0.47.0 le
> operazioni stanno in «Riscontro.Disegno.dll»: punta li' RISCONTRO_DISEGNO,
> oppure togli OPENPDM_ADDIN e mettila accanto all'eseguibile.`

Provate tutte e due le strade: quella giusta si aggancia, quella sbagliata
spiega. **Un messaggio che non si e' visto uscire non e' un messaggio.**

### Il confine inventato, e il difetto che invitava a fare

`EsecutorePiano` era l'unico dei dodici file a parlare con il server di
OpenPDM. Adesso parla con `IFontePiano`, e l'add-in ne fornisce
l'implementazione.

La forma ovvia sarebbe far tornare `null` per dire «piano finito». **Non si e'
fatta**, e la ragione e' scritta nel codice: un adattatore che inghiotte
un'eccezione di rete e ritorna `null` racconterebbe «piano completato» per un
piano interrotto a meta'. C'e' un tipo `ProssimoPasso` con `Finito` esplicito,
e davanti a un `null` l'esecutore si ferma dicendo che non sa quale dei due
casi sia.

### Le librerie di Dassault continuano a restare fuori

`Riscontro.Disegno` referenzia le interop con `Private=False` e l'HintPath
sull'installazione: nessun binario di Dassault entra in questo repository, che
era la condizione perche' si potesse pubblicare.

### Il caricamento adesso legge i byte

`Assembly.LoadFrom` su un'unita' di rete fallisce con `0x80131515`, un errore
che non nomina la rete e manda a cercare una dipendenza mancante che non
esiste. Si leggono i byte e si carica da quelli.

### Cosa NON e' cambiato, ed e' il punto

Rifatta la prova di riferimento sul pezzo vero, dopo l'estrazione:

```
estrudi_rettangolo  200x100x20                      -> riuscita
taglio_cilindrico   Ø10 passante                    -> riuscita
ripeti_lineare      4 istanze, passo 30, capovolgi  -> riuscita
                    volume_cambiato -4.712,39 mm³
                    facce 7 -> 10   copie_fatte 3
```

Gli stessi numeri di prima dell'estrazione, alla cifra.

### Resta aperto

**La riflessione c'e' ancora, e adesso non ha piu' ragione di esserci.** Il
ponte potrebbe referenziare la libreria come un progetto qualunque: e' la voce
B7 del `TODO.md`. Finche' c'e', i nomi dei tipi sono stringhe e un errore di
battitura si scopre solo a runtime.

Il percorso della libreria nel csproj dell'add-in e' **assoluto**, scavalcabile
con `/p:RiscontroDisegnoProj=...`, finche' non arriva il sottomodulo (B6).

---

## 0.5.0 — 14 settembre 2026

**Il ponte non lavora piu' sulla faccia sbagliata in silenzio.**

### Il difetto, misurato

Blocco 200×100×20 gia' aperto. Il modello chiama `guarda_il_pezzo` — `F1` e' la
faccia di sopra, 20.000 mm² — e manda un piano di due passi:

1. tasca Ø60 profonda 5 **su F1**;
2. tasca Ø10 profonda 5 **su F1**.

Il primo passo porta la faccia di sopra a 17.172,57 mm². Gli identificativi
sono **posizionali** — l'add-in li riassegna ordinando le facce per area — e
quella di sopra passa cosi' dietro a quella di sotto, rimasta a 20.000. Al
secondo passo `F1` e' la faccia **di sotto**.

Misurato: la tasca piccola e' finita sul fondo del pezzo.

| faccia | area | dice |
|---|---|---|
| F1 (sotto) | 19.921,46 = 20.000 − π·25 | il Ø10 e' **qui** |
| F2 (sopra) | 17.172,57 | intatta dal Ø10 |
| F9, parete | π·10·5, punto y = 0 | sale **dal basso** |
| F10, fondo | punto y = **5** | 5 mm **dal basso** |

Tutti e due i passi hanno risposto `riuscita: true`, le profondita' erano quelle
chieste, e il volume tornava: **385.470,1 mm³, esatto**.

Ed e' esattamente lo stesso volume del pezzo giusto. Le due lavorazioni tolgono
lo stesso materiale: **nessun controllo sul volume avrebbe potuto distinguerle.**

### La difesa c'era gia', e nessuno l'alimentava

L'add-in sa riconoscere che «F1» non e' piu' la faccia di prima: legge la chiave
`_firme` dagli argomenti del passo e confronta punto e normale. Ma quella chiave
la scriveva **solo** il server Python di OpenPDM (`ai_disegno.py`). Nel ponte non
compariva in nessun sorgente, quindi `FirmaCombacia` non aveva termini da
confrontare e tornava `true` sempre.

Adesso `guarda_il_pezzo` ricorda le firme di cio' che ha descritto — punto,
normale, area, lunghezza, per facce, spigoli, feature, assi e **corpi** — e
`disegna` le allega a ogni passo. Stessi quattro campi del server, cosi' le due
strade dicono all'add-in la stessa cosa.

Sullo stesso piano, adesso:

> `«F1» adesso indica un'altra parte del pezzo (spostata di 20 mm). Il pezzo e'
> cambiato da quando hai confermato: non lavoro sulla faccia sbagliata.`

e la faccia di sotto resta a 20.000 mm²: il taglio sbagliato **non avviene**.

### `stato_dopo` e' la lettura successiva, e va firmata come tale

`disegna` restituisce lo stato del documento dopo i passi, con gli
identificativi nuovi: chi legge scrive il piano seguente su **quelli**. Se le
firme restassero quelle di `guarda_il_pezzo`, il piano seguente — scritto
giusto — verrebbe rifiutato perche' confrontato con una lettura che il suo
autore non ha mai visto. Quindi le firme si aggiornano da `stato_dopo`.

Misurato: due `disegna` di fila nella stessa sessione, il secondo che cita `F2`
preso da `stato_dopo`, passa e taglia la faccia giusta — F2 scende da 17.172,57
a 17.094,03, la faccia di sotto resta a 20.000.

### Le firme valgono solo sul documento che le ha prodotte

Se il piano nomina un altro documento con `documento`, gli identificativi non
parlano di questo: allegare le firme vecchie produrrebbe rifiuti che non
c'entrano niente. Meglio nessun confronto che un confronto fra due pezzi
diversi.

### Un difetto mio, preso dalla misura

La prima stesura leggeva le famiglie dello stato con `as object[]`. Lo stato
arriva dall'add-in con i suoi tipi veri, e la' sono
`List<Dictionary<string, object>>`: il cast tornava `null` in silenzio e il
controllo restava **spento**. Se ne e' accorta la prova, non la lettura del
codice — il piano guasto passava ancora. Ora si scorre come `IEnumerable`, che
va bene per tutte e due le forme.

### Provato sul banco, quattro misure

| piano | atteso | ottenuto |
|---|---|---|
| `uid_stantio_2` — `F1` citata dopo che `F1` e' cambiata | **rifiuto** | rifiutato, «spostata di 20 mm», pezzo intatto |
| `uid_buono` — `F3` che si e' solo risposizionata in `F4` | **passa** | passa, Ø6 sulla faccia frontale (4.000 → 3.971,73) |
| `seq_a` + `seq_b` — due `disegna`, il secondo su `stato_dopo` | **passa** | passa, Ø10 sulla faccia di sopra |
| `cp_1`+`cp_2`+`cp_3b` — piano che crea il pezzo da zero | **passa** | passa, nessuna firma da confrontare |

### Correzione a quello che la 0.4.0 diceva dell'installatore

La voce qui sotto dice «si scarica, doppio clic, e Claude parla con
SOLIDWORKS», e `build.ps1` stampava «dallo a chiunque abbia SOLIDWORKS».
**Non e' ancora vero, e la riga e' stata cambiata.**

Il carico incorpora il ponte, il catalogo, la configurazione e le note legali,
ma **non** `OpenPDM.SwAddin.dll`, che il ponte carica per riflessione da accanto
all'eseguibile o da `OPENPDM_ADDIN`. Chi non ha gia' quell'add-in installa un
ponte che si ferma subito — con un messaggio chiaro, ma pur sempre un rifiuto.

Finche' le operazioni non sono estratte in una libreria propria (sezione B del
`TODO.md`), la formula esatta e': **installabile da chiunque, usabile da chi ha
gia' l'add-in.** La voce del 0.4.0 resta com'era scritta, perche' un registro
non si riscrive; questa e' la correzione, datata.

---

## 0.4.0 — 14 settembre 2026

**Un file solo: si scarica, doppio clic, e Claude parla con SOLIDWORKS.**

`dist\Installa.exe`, 151 kB. Si porta dentro il ponte, il catalogo, la
configurazione e le note legali come risorse incorporate, li scrive su disco, e
— questo e' il lavoro vero — **scrive da se' la voce nella configurazione del
client MCP**.

### Perche' l'installatore, e non tre righe di README

Le tre righe di README le salta chi ha fretta, e poi il ponte non parte per una
ragione che nessuno dei due indovina. Il punto dove ci si perde non e' copiare
due file: e' aprire un JSON nascosto in `%APPDATA%`, capire dove va il blocco,
non rompere quello che c'era, e sapere che Claude va riavviato perche' legga.

Tre regole nella scrittura di quella configurazione, tutte e tre pagate da
qualcun altro prima di noi:

- **non si sovrascrive il file.** Chi usa Claude ha spesso altri server MCP
  configurati: si legge, si aggiunge la propria voce, si riscrive il resto
  com'era;
- **si fa una copia prima**, sempre, con la data nel nome;
- **si dice di riavviare.** Claude legge quel file all'avvio, e «non succede
  niente» e' il modo piu' rapido di far concludere che l'installazione e'
  fallita.

Il JSON viene riscritto **indentato**: lasciare all'utente una riga da duemila
caratteri dove prima c'era qualcosa di leggibile e' un danno anche se il
contenuto e' giusto.

### Quello che fa, in ordine

Controlla prima di toccare: .NET Framework 4.8, SOLIDWORKS installato (e la
cartella `api\redist`, che serve perche' le librerie di Dassault **non** sono
incluse), il carico da installare, l'add-in, e se SOLIDWORKS e' aperto adesso.
Ogni controllo che fallisce dice **cosa fare**, non solo che e' fallito.

Poi installa in `%LOCALAPPDATA%`, collega i client scelti, e chiude con il
**giro secco**: `--prova` si aggancia, dice cosa vede, ed esce. E' l'unica cosa
che permette di scrivere «installato» sapendo che e' vero.

Niente elevazione: non scrive in Program Files, non tocca il registro, non
registra componenti COM. In molte officine il progettista l'amministratore non
ce l'ha, e un installatore che lo chiede non si puo' usare.

C'e' anche `--silenzioso` per installare su piu' postazioni, e `Disinstalla`,
che toglie **solo** i file messi da qui — mai una cancellazione ricorsiva su
una cartella scelta da chi installa.

### Misurato

| prova | esito |
|---|---|
| installazione pulita | 4 file scritti, giro secco: «il ponte sta in piedi» |
| reinstallazione sopra una precedente | riscritti, nessun errore |
| **l'exe DA SOLO in una cartella vuota** | **estrae tutto e si installa: il carico e' davvero dentro** |
| `--aiuto` da un prompt | si attacca alla console di chi ha chiamato |

La terza riga e' la prova che conta: se il carico non fosse incorporato,
l'installatore lo prenderebbe dalla cartella accanto e la prova passerebbe
lo stesso senza che il file sia autosufficiente. `build.ps1` ha anche un
controllo che si rifiuta di produrre un pacchetto piu' piccolo del ponte che
dovrebbe contenere.

### Una cosa piccola, e il perche'

La console stampa solo ASCII. Ci si attacca alla console di chi ha lanciato il
comando, con la sua tabella codici: un trattino lungo esce come un rombo con un
punto interrogativo, e un installatore che stampa caratteri rotti sembra rotto
lui.

---

## 0.3.0 — 14 settembre 2026

**Tre difese, e una era un difetto che avevo aggiunto io.**

### Il ponte dichiara cosa fa

Il protocollo prevede quattro suggerimenti che il client usa per decidere se
fermarsi a chiedere conferma a chi sta lavorando: `readOnlyHint`,
`destructiveHint`, `idempotentHint`, `openWorldHint`. Non ce n'era **nessuno**,
quindi ogni client trattava `guarda_il_pezzo` — che non tocca niente — e
`disegna` — che modifica il pezzo in modo che nessuno annulla per lui —
esattamente allo stesso modo.

Per un progetto la cui tesi e' «nessuna geometria esiste senza che qualcuno
l'abbia confermata», era l'omissione piu' vistosa.

`disegna` e' dichiarato **non idempotente**, ed e' la riga che conta:
rieseguire lo stesso piano non rimette il pezzo com'era, fa un **secondo foro**.
Dichiararlo idempotente inviterebbe un client a riprovare da solo dopo un
errore di rete, e il pezzo uscirebbe con il doppio delle lavorazioni senza che
nessuno l'abbia chiesto.

**Misurato:** `tools/list` riporta le quattro annotazioni su tutti e quattro gli
strumenti; `disegna` distruttivo, non idempotente, non di sola lettura.

### Si NOMINA il documento, non si spegne il controllo

La 0.2.x aveva `qualunque_documento: true` per scavalcare il rifiuto quando il
documento attivo era cambiato. Era un difetto, e della specie peggiore: **un
argomento che disattiva una difesa e' un invito a disattivarla.** Chi incontra
il rifiuto ha tutti gli incentivi per riprovare con quello acceso, e la difesa
sparisce proprio nel caso in cui serviva.

Adesso si passa `documento` con il titolo o il percorso. Chiede la stessa cosa
in una forma che non si puo' dare a caso: **per scriverne il nome bisogna averlo
guardato.** Un booleano lo mette chiunque; un nome che non corrisponde viene
rifiutato come tutto il resto.

### I percorsi si controllano prima di eseguire

`salva_documento` ed `esporta_pdf` prendono un percorso **dal modello** e ci
scrivono sopra. E' l'unica cosa che il ponte tocca fuori dal documento aperto, e
arrivava da un testo generato senza nessun controllo.

Tre regole, in `Ponte/Percorsi.cs`:

- una radice obbligatoria, se `RISCONTRO_RADICE` e' impostata;
- le cartelle di **sistema e di programma** mai, in nessun caso — non perche'
  qualcuno le chiederebbe apposta, ma perche' un percorso relativo sbagliato ci
  finisce da solo;
- le cartelle dichiarate in `RISCONTRO_SOLA_LETTURA`, che nascono da un fatto:
  inserendo un membro strutturale SOLIDWORKS apre da solo il file di libreria
  dei profili e lo lascia marcato come modificato. Da li' un salvataggio
  qualunque riscrive la libreria di tutti.

**Misurato**, e la terza riga e' quella che conta:

| richiesta | esito |
|---|---|
| un percorso dentro la cartella di sistema | rifiutato, niente eseguito |
| un percorso con `..\..\` che ci finisce dentro | rifiutato: normalizzato **prima** del confronto |
| una cartella di lavoro normale | **salvato, 45.144 byte** |

L'ultima riga e' la lezione gia' pagata altrove: un controllo che rifiuta anche
cio' che e' soltanto insolito e' peggio di nessun controllo, perche' blocca
lavoro legittimo e nessuno lo rivede.

---

## 0.2.0 — 11 settembre 2026

**Il ponte sta in piedi e disegna.** Quattro strumenti, protocollo completo,
provato contro un SolidWorks vero con un pezzo aperto.

```
guarda_il_pezzo   elenco_operazioni   descrivi_operazione   disegna
```

### Quattro strumenti e non trentasei

Le operazioni di disegno sono trentasei, e la tentazione e' di esporne una
per strumento. Sarebbe sbagliato per due ragioni gia' misurate nel progetto:

1. **il costo.** Gli schemi interi sono decine di migliaia di caratteri in
   ogni richiesta. Il catalogo a due livelli del server esiste per questo, e
   qui vale uguale;
2. **l'ordine giusto.** Il difetto piu' caro non e' un'operazione sbagliata:
   e' un'operazione giusta fatta **alla cieca**. Quattro strumenti in fila —
   guarda, cerca, approfondisci, fai — rendono difficile saltare il primo.

`guarda_il_pezzo` non e' un accessorio: senza, gli identificativi che gli
altri vogliono (`F1`, `E4`, `Corpo1`) non esistono.

### Il catalogo non si riscrive: si esporta

`Strumenti/catalogo.json`, rigenerato dal server con un comando
(`Strumenti/aggiorna.md`). Contiene **solo le operazioni `provata=True`** —
36 alla data di oggi. Un ponte serve a misurare, e misurare con uno
strumento non misurato non e' misurare.

### Cosa e' costato piu' del previsto

- **`Assembly.LoadFrom` da un disco di rete** fallisce con `0x80131515`,
  «Operazione non supportata»: un messaggio che non nomina ne' la rete ne'
  la fiducia, e manda a cercare dipendenze mancanti che non mancano. Serve
  `<loadFromRemoteSources enabled="true"/>` in `App.config`.
- **`IActiveDoc2` non esiste per il COM.** Chiamando per riflessione si
  passa dall'IDispatch, e li' il nome e' `ActiveDoc`: `IActiveDoc2` e' come
  lo espone l'interoperabilita' tipizzata in C#. Chiedere quello sbagliato
  non da' un errore utile — il ponte rispondeva «non c'e' nessun documento
  aperto» mentre ce n'erano venti.
- **le librerie di interoperabilita' di Dassault non si copiano** nel
  repository: si cercano nella cartella `redist` dell'installazione, con un
  `AssemblyResolve`. Copiarle vorrebbe dire ridistribuire un binario di
  terzi e legarsi a una versione.

### Un errore che aiuta invece di constatare

Chi sbaglia il nome di un'operazione quasi sempre ne ha in mente uno vicino,
e lo sbaglia di una lettera. «asole» adesso risponde *«Forse cercavi:
asola»*. Il primo tentativo cercava la parola intera dentro il nome e su
«asole» non trovava «asola» — falliva proprio nel caso per cui era stato
scritto.

### Misurato

Contro SolidWorks 29.5.1 con add-in 0.40.0.0 e il contenitore stagno
aperto:

| | |
|---|---|
| `initialize` / `tools/list` | 4 strumenti |
| `guarda_il_pezzo` | stato completo del pezzo, con `documenti_aperti` |
| `elenco_operazioni` (famiglia `tavola`) | 7 operazioni |
| `descrivi_operazione` (`asola`) | schema intero |
| nome storto (`asole`) | «Forse cercavi: asola» |
| piano con operazione inesistente | rifiutato **prima** di toccare il pezzo |
| `disegna` → `applica_materiale` | «ABS generico», letto dallo stato dopo |

L'ambiguita' e' stata dichiarata invece di risolta: «ABS» corrisponde a due
materiali e il ponte si e' fermato chiedendo quale. E' il comportamento
dell'add-in, e passa dal ponte senza essere addolcito — che e' il punto di
caricare l'add-in vero invece di riscriverlo.
