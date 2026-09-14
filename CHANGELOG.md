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
