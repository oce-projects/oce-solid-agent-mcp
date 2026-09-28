# OCE Solid Agent

*Un progetto indipendente, non affiliato a Dassault Systèmes.*

Un ponte MCP per **disegnare nel software SOLIDWORKS®**: espone come strumenti novantacinque
operazioni di disegno gia' misurate, cosi' che un modello qualunque — locale o
remoto — possa costruire un pezzo e, soprattutto, **guardare cos'e' venuto**.

Si installa con un file solo e non chiede altro che un'installazione
SOLIDWORKS® con licenza tua.

> **Vuoi solo usarlo, non compilarlo?** → **[QUICKSTART.md](QUICKSTART.md)**,
> sei passi, in inglese. Questa pagina e' per chi vuole costruirlo.
> Se invece sei un assistente AI che installa per conto di qualcuno, la pagina
> scritta per te e' **[INSTALL-FOR-AI.md](INSTALL-FOR-AI.md)**.

> SOLIDWORKS® e' un marchio registrato di Dassault Systèmes SolidWorks
> Corporation. Questo progetto e' indipendente e non e' affiliato, sponsorizzato
> ne' approvato da Dassault Systèmes. Non contiene e non ridistribuisce codice o
> materiale di Dassault Systèmes: le librerie di interoperabilita' si caricano
> all'avvio dalla **tua** installazione di SOLIDWORKS®, che richiede una licenza
> tua.

## Perche' esiste

OpenPDM ha un assistente che disegna: un modello locale propone un piano, il
server lo valida, l'add-in lo esegue dentro il software SOLIDWORKS, e un progettista
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
                                                         SOLIDWORKS
```

Fino alla 0.5.0 la libreria era `OpenPDM.SwAddin.dll` — l'add-in intero,
caricato per riflessione — e il ponte si portava dietro tutto il PDM per fare
un foro. Dalla 0.6.0 le operazioni stanno per conto loro; dalla 0.7.0
l'installatore se le porta dentro.

## Gli strumenti

Nove, non novantacinque. Le operazioni di disegno sono novantacinque, ma esporne
una per strumento costerebbe decine di migliaia di caratteri di schemi in
ogni richiesta — e, soprattutto, renderebbe facile saltare il primo passo.
(Erano cinque fino alla 0.56.0: il sesto e' nato per una ragione precisa,
scritta sotto la tabella, non per coprire di piu'. Il settimo, `documento`,
dalla 0.87.0: senza, per cominciare un pezzo da zero serviva una persona che
cliccasse File > Nuovo.)

*Since 0.112.0 (entry U1.1): the eighth and ninth, `api_search` and
`api_signature`, read the whole SOLIDWORKS API — the `sldworks` interop and the
`swconst` enums, 13,945 members once the four old interfaces are left out — by
reflection, with SOLIDWORKS closed. They are the first half of the generic
layer; `api_objects` and `api_call` come with U1.2-U1.3.*

| strumento | a cosa serve |
|---|---|
| `guarda_il_pezzo` | lo stato del documento aperto: ingombro, volume, corpi, lavorazioni con le loro quote, facce e spigoli con il loro identificativo. Con `viste` torna anche una **fotografia** del pezzo |
| `elenco_operazioni` | l'elenco corto per famiglia: un nome e una riga |
| `descrivi_operazione` | lo schema completo di una |
| `disegna` | esegue una sequenza e **riporta cos'e' venuto**: le misure di ogni passo e lo stato dopo |
| `misura` | le stesse operazioni, ma **solo quelle che leggono** (famiglia `verifiche`). Si dichiara di sola lettura, cosi' il client non chiede conferma per una misura |
| `annulla` | ritira l'ultimo piano, tutto intero — ma solo se il pezzo e' ancora come `disegna` l'aveva lasciato |
| `documento` | crea una parte o un assieme vuoti, apre un file dal disco, elenca i documenti aperti, ne porta uno davanti, ne chiude uno — e **rifiuta di chiudere** un documento con modifiche non salvate. Dice sempre se un file e' aperto in **sola lettura**; e dopo ogni cambio di documento `disegna` pretende un nuovo `guarda_il_pezzo` |
| `api_search` | searches the whole API by a piece of a name and answers with the **real signatures** read from the interop on this machine; the four old interfaces (`IModelDoc`, `IBody`, `IFace`, `IComponent`) are named with the sister to use, never shown. Read only |
| `api_signature` | the card of one member: every overload, property or method, the `&` out parameters, the typed `I…` variant, the numbered family with the last one marked **«latest by number, not verified»**, the **Void** flag, and what is known of it from `Strumenti/api_knowledge.json` — state, enum map (**«enum not tabulated»** where nobody wrote one), measured indices (**«index not tabulated»** otherwise), measured types, source. Read only |

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
due script diversi nati in due momenti diversi, e uniformarli e' una cosa
gia' segnata e non una da indovinare.

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
| `RISCONTRO_REGISTRO` | dove il ponte scrive **cosa gli e' stato chiesto e non c'era** (voce Z-G7): una riga JSON per domanda, solo il nome e i nomi vicini, mai gli argomenti. Se manca, accanto all'eseguibile; **impostata a stringa vuota vuol dire «non registrare niente»**. Se non si puo' scrivere, il ponte tace e va avanti: il registro non fa mai fallire la chiamata che osserva |
| `SW_INSTALL_PATH` | dove e' installato il software SOLIDWORKS: la **radice**, quella che finisce con `\SOLIDWORKS`. Vale sia per compilare che per far girare — da qui si ricava `api\redist` |
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
Contiene solo le operazioni **misurate dentro il software SOLIDWORKS**.

Settantatré provate, e tre che esistono nella libreria ma **restano fuori
dal file**: `croci_di_centro`, `distinta_taglio_in_tavola` e `fazzoletto` —
tutte e tre `Provata=false` perche' non creano niente (le prime due «0 → 0»,
il fazzoletto null in sedici varianti, misurato il 25/9/2026), e un client non
deve poterle chiamare. Il banco stampa in intestazione **tutti e due i numeri col
loro nome** — «eseguibili: 76 operazioni (a catalogo: 73)» — dal 24/9/2026
(voce F15): prima stampava «catalogo: 73» e il catalogo ne aveva 71, e la
differenza e' stata spiegata una volta, in modo plausibile e sbagliato, con due
copie della libreria che divergevano.

### Su che documento gira cosa

| famiglia | documento |
|---|---|
| `estrusioni`, `lavorazioni`, `saldature`, `riferimenti`, `proprieta`, `esportazioni`, `identita`, `lamiera`, `quote` | una **parte** |
| `tavola` | una **tavola** |
| `assiemi` — `inserisci_componente`, `accoppia`, `sostituisci_componente` | un **assieme** |
| `verifiche` | quasi tutte su una parte; `verifica_interferenze` vuole un **assieme** |

Non e' una tabella decorativa: **c'e' un controllo che la fa rispettare.**
`Prove.Contratti` confronta le famiglie del catalogo servito coi tipi di
documento che l'esecutore ammette, e diventa rosso se il ponte promette una
famiglia che rifiuterebbe prima del primo passo. E' nato rosso: fino al
18/9/2026 il catalogo elencava la famiglia `assiemi` per intero mentre
l'esecutore rifiutava ogni assieme. **Il banco non poteva accorgersene — chiama
la libreria, non il ponte.**

> Questa riga, nella prima stesura, ne ha fatto scattare un'altra: nominava il
> numero delle operazioni di assieme a parole, e il controllo che sorveglia **il
> conto delle operazioni nel README** l'ha letto come se il README dichiarasse
> quel numero — e si e' fatto rosso.
>
> Aveva ragione lui a essere stupido. Un numero a parole accanto alla parola
> «operazioni» e' indistinguibile, per chi legge in fretta, da quello che dice
> quante ce ne sono in tutto; e il difetto che il controllo esiste per prendere
> — un README che promette un catalogo diverso da quello vero — ha esattamente
> quella forma. **Ho riscritto la frase, non il controllo.** *Un controllo a cui
> si spiegano le eccezioni smette di essere un controllo.*

## Stato

Vedi [CHANGELOG.md](CHANGELOG.md). La versione sta in `VERSION.txt` e sale a
ogni modifica: e' una regola, non una consuetudine.

### Cosa la CI prova, e cosa NON prova

> **Questa sezione e' stata riscritta il 23/9/2026, e prima diceva il falso.**
> Diceva «la CI compila, lancia i controlli di contratto — che parlano col
> ponte sullo stdio, senza SOLIDWORKS — e rifiuta una consegna in cui nessuno
> ha lanciato il banco». **Nessuna delle prime due cose poteva succedere su un
> runner di GitHub**, e la CI non e' mai diventata verde. Il perche' sta sotto,
> perche' e' il genere di cosa che chi legge un README ha il diritto di sapere
> prima di fidarsi di un distintivo.

**Il ponte, in CI, non si compila.** `Oce.Solid.Agent.csproj` e
`Riscontro.Disegno.csproj` referenziano `SolidWorks.Interop.sldworks` e
`SolidWorks.Interop.swconst` con un `HintPath` dentro l'installazione di
SOLIDWORKS. Quelle DLL sono di Dassault: non stanno in repository, non si
scaricano, e su un runner di GitHub non ci sono. Misurato, non dedotto —
compilando con `/p:SwInstallPath=Z:\non-esiste` escono duecento e passa
`CS0246: il nome di tipo o di spazio dei nomi 'IModelDoc2' non e' stato
trovato`.

Di conseguenza **nemmeno i controlli che parlano col ponte** possono girare:
senza eseguibile non c'e' nessuno stdio a cui scrivere.

Quello che la CI fa davvero:

| | gira su un runner di GitHub |
|---|---|
| compila `Prove.Contratti` (di interop non ne ha, e c'e' un controllo che lo verifica) | **si** |
| il valutatore delle attese, i sorgenti nei loro `.csproj`, i documenti che contano i file giusti | **si** |
| il contenuto del catalogo, letto da `Strumenti\catalogo.json` che sta in repository | **si** |
| **rifiuta una consegna in cui nessuno ha lanciato il banco** | **si** |
| compila il ponte | solo dove l'interop c'e' (un runner self-hosted con SOLIDWORKS) |
| il protocollo MCP, `Sessione.StessoDocumento`, l'esportatore del catalogo | solo li' |

**E cio' che non gira non diventa verde.** I gruppi che hanno bisogno del
ponte scrivono una voce **«non eseguibile qui»**: si conta a parte, si stampa,
finisce in `prove.xml` come `<skipped>` e nel riepilogo della corsa. Il
riepilogo dice `N controlli, 0 falliti, M non eseguibili qui (K fatti
davvero)`, e il minimo dichiarato si misura sui **fatti** — una suite tutta
saltata non supera la soglia. *Saltare in silenzio sarebbe stato il modo piu'
comodo di far diventare verde questo file, ed e' il difetto che il file
stesso esiste per non avere.*

> **Il punto sul banco non e' una verifica indipendente, e vale dirlo qui.**
> Nessun runner ha una licenza SOLIDWORKS, quindi **il banco in CI non gira**.
> Cio' che la CI legge e' `Banco/resoconto/ultimo.json`: un file che
> **produciamo e committiamo noi**. Controlla che dichiari un giro intero,
> coerente con la libreria di adesso e posteriore all'ultima modifica alle
> operazioni.
>
> E' una guardia contro la **nostra** distrazione — il commit fatto senza aver
> rilanciato il banco — non contro un resoconto che dicesse il falso. Se il
> resoconto mentisse, la CI lo crederebbe.
>
> *Una difesa raccontata piu' forte di com'e' indebolisce anche quelle vere,
> perche' insegna a non fidarsi dell'etichetta.*

### Quanto reggono i riferimenti quando il pezzo cambia

Diciannove pezzi costruiti, **una sola quota a monte cambiata**, e la geometria
a valle rimisurata. Il risultato e' una di tre cose, non due:

| | casi su 19 | |
|---|---|---|
| **reggono** | **17** | ricostruzione pulita, e la geometria sta dove l'intento dichiara |
| **si staccano e lo dicono** | **2** | la ricostruzione fallisce, una lavorazione porta un errore, o la modifica viene rifiutata: il progettista lo vede |
| **si staccano in silenzio** | **0** | ricostruzione a posto, nessun errore, e la geometria e' finita da un'altra parte |

*Misurato il 26/9/2026 su SOLIDWORKS 29.5.1, libreria 0.121.0. Si rifa' con
`.\Banco\banco.ps1 -Regressione`; il dettaglio caso per caso finisce in
`Banco/resoconto/regressione.md`.*

**Il 15/9 erano 12, 0 e 4 su sedici**, e lo zero in mezzo era il numero piu'
interessante: nessun caso si era rotto in modo visibile, le quattro volte che
il pezzo era uscito diverso SOLIDWORKS aveva ricostruito pulito e taciuto. E'
la ragione per cui questa suite misura **la posizione della geometria** e non
il fatto che la ricostruzione sia andata.

**I quattro silenzi avevano due cause, non quattro** (misurato il 23/9/2026).
Tre erano la stessa: *un'operazione agganciata a una **faccia** segue la
geometria; le coordinate `x_mm`/`y_mm` dentro il piano di schizzo di quella
faccia no.* Dal 25/9 (voce E18) `taglio_cilindrico` e `taglio_rettangolare`
prendono `ancora` — una quota da uno spigolo, o il centro fra due spigoli, per
asse — e il profilo segue il bordo:

- la tasca centrata (`r03`) regge, e il suo gemello senza ancora (`r03b`)
  resta dov'era, come deve;
- un foro a 20 dal bordo (`r19`) regge, e i quattro fori guidati a 20 dagli
  angoli (`r01`, dalla libreria 0.119.0) pure — col gemello assoluto `r01b`;
- l'asola che esce dal materiale accorciando la piastra (`r16`) adesso si
  **rifiuta con criterio**, e la quota torna com'era: e' la riga di mezzo;
- anche asole e rialzi prendono l'ancora (0.118.0, 0.119.0).

Il quarto (`r05`) non si ripara ancorando, e per un po' l'abbiamo scritto
come irreparabile: uno smusso piu' grande del materiale **non fallisce**.
SOLIDWORKS ricostruisce pulito e si mangia un millimetro di larghezza. Si
prende rileggendo il pezzo dopo — e il segno c'era gia': la lavorazione di
base perde una faccia intera, quella che lo smusso adesso si prende tutta.
Dalla libreria 0.121.0 `modifica_quota` guarda proprio quello: se una
lavorazione che non e' un taglio perde facce e piu' avanti c'e' uno smusso o
un raccordo, **rifiuta, dice quale, e rimette il valore di prima**. `r05` e'
passato alla riga di mezzo, e la terza e' a zero.

La terza riga si scrive **anche quando vale zero**: un conteggio che sparisce
quando e' zero e' un conteggio che nessuno controlla.

## Vincoli che non si negoziano

- **Serve una sessione SOLIDWORKS aperta.** Il ponte si aggancia a una sessione che c'e',
  non ne avvia una.
- **Provato solo su SOLIDWORKS 2021 SP5.1** (interop 29.5.1). Le versioni piu'
  vecchie si possono provare quando serve; le piu' nuove non sono provate.
  L'installatore le accetta tutte, e per una versione diversa da quella provata
  mette un avviso.
- **Niente scritture fuori dal documento aperto.** Il ponte non salva nel
  vault, non tocca librerie, non installa niente.
- **Nessuna operazione non misurata.** Se un'operazione non e' stata costruita
  dentro il software SOLIDWORKS e verificata con un numero, non si espone. E' la regola
  che regge tutto OpenPDM e vale anche qui.

## Marchi

SOLIDWORKS e 3DEXPERIENCE sono marchi registrati di Dassault Systèmes
SolidWorks Corporation. Questo progetto e' indipendente e **non e' affiliato,
autorizzato, sponsorizzato ne' approvato da Dassault Systèmes SolidWorks
Corporation** — in inglese, che e' la lingua in cui la formula conta: *not
affiliated with, authorized by, sponsored by or endorsed by Dassault Systemes
SolidWorks Corporation.*

La licenza Apache-2.0 di questo progetto **non concede alcun diritto sui
marchi**: lo dice la sua sezione 6, e vale la pena dirlo qui perche' una
licenza permissiva si legge in fretta come se permettesse tutto.

Il nome SOLIDWORKS compare solo come qualifica del software con cui questo
ponte parla. Vedi [NOTICE](NOTICE), che dice la stessa cosa piu' per esteso —
ed e' ripetuta qui perche' **il NOTICE quasi nessuno lo apre**.
