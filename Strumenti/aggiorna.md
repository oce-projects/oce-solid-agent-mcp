# Rigenerare `catalogo.json`

Il catalogo vive in `Riscontro.Disegno/Catalogo/Operazioni.cs`, **accanto al
codice che esegue le operazioni**. `catalogo.json` e' un prodotto: si rigenera,
non si modifica a mano.

## Il comando

```bash
.\bin\Riscontro.Catalogo.exe Strumenti\catalogo.json
```

Nient'altro. Non serve il server, non serve docker, non serve la rete.

Per controllare senza scrivere — utile prima di un commit, e in una build
automatica:

```bash
.\bin\Riscontro.Catalogo.exe --controlla Strumenti\catalogo.json
```

Esce 0 se combacia, 1 se no. E quando non combacia dice **dove**: la posizione
del primo carattere diverso e le trenta lettere intorno, da tutte e due le
parti. Un «sono diversi» su un file da settantaduemila caratteri manderebbe a
cercare a occhio.

## Perche' sta in C# e non nel server

Prima il catalogo viveva in `<un file del server>`, e questo file lo
copiava. Rigenerarlo voleva dire: impacchettare i sorgenti del server, mandarli
via `scp`, farli girare dentro un container usa-e-getta, e riportarsi indietro
il risultato. Tre passi, due macchine, e docker.

**Chi clonava questo repository non poteva rigenerare il catalogo.** Un file che
non si puo' rigenerare e' un file che nessuno osa toccare — e quel file e'
l'interfaccia verso il modello.

C'e' una seconda ragione, meno pratica e piu' importante: **lo strato che
DESCRIVE un'operazione deve essere lo stesso che la ESEGUE.** E' l'unica
disposizione in cui un'operazione descritta e non eseguibile non puo' entrare in
catalogo senza che qualcuno se ne accorga — il controllo diventa un'asserzione
dentro un progetto, invece di un confronto fra due repository che nessuno rifa'.

## Cosa finisce nel file

**Solo le operazioni con `Provata = true`.** Un'operazione che non e' stata
costruita dentro SOLIDWORKS e misurata non si offre a nessuno — nemmeno da qui,
e specialmente da qui, perche' un ponte serve a misurare e misurare con uno
strumento non misurato non e' misurare.

Quelle che restano fuori **si dicono**, su stderr, una per riga: un elenco che
si accorcia in silenzio non si nota.

Per ogni operazione: `nome`, `famiglia`, `sommario`, `descrizione`, `schema`,
`precondizione`. `Provata` no — e' una cosa nostra, non del modello.

Fuori restano anche le famiglie che non hanno nemmeno un'operazione provata, e
anche quelle si dicono. Una famiglia dichiarata e vuota arriva al modello come
una porta che non apre su niente.

## La forma del file

Rientro di **uno** spazio, accenti per esteso e non in `\u`, a capo a solo
avanzamento riga, nessun a capo finale, e le chiavi nell'ordine in cui sono
scritte in C#.

Sembra pignoleria e non lo e'. Quando il catalogo e' passato dal Python al C#,
il criterio era che il file uscito fosse **identico byte per byte** a quello che
c'era: 72.452 byte, e lo era. Un criterio piu' morbido — «equivalente una volta
riletto» — avrebbe lasciato passare una descrizione accorciata, e le descrizioni
sono l'interfaccia vera verso il modello, molto piu' degli schemi.

Da qui in avanti la stessa forma serve a un'altra cosa: tenere il `diff` di
`catalogo.json` leggibile quando cambia una riga sola.

## Come accorgersi che e' vecchio

Il ponte scrive all'avvio, su stderr:

```
[ponte] catalogo: 60 operazioni provate
```

Il numero qui sopra e' quello del 16 settembre 2026 ed e' solo un **esempio di
come si legge la riga**: quello che conta e' confrontarlo con quante ne dichiara
il `catalogo.json` che si sta usando, non con questo. *Un numero scritto dentro
un documento invecchia da solo* — era `36` fino a oggi, mentre le operazioni
erano gia' sessanta, e nessuno poteva accorgersene leggendo.

E `build.ps1` rigenera il file a ogni pacchetto, quindi non puo' restare
indietro senza che qualcuno cancelli quel passo.

## Il lato server, che resta da fare

`ai_disegno.py` e' ancora la fonte del catalogo **per il server**: finche' non
legge questo JSON, le fonti sono due e divergeranno.

Accertato con un grep su `server/app/`: `CATALOGO_DISEGNO` ha **tre**
consumatori fuori da `ai_disegno.py` — `<un file del server>`,
`<un file del server>` e `:252` — e usano `.nome`, `.provata` e
`.precondizione`, tutti campi che il record C# ha.

Ma `ai_disegno.py` tiene anche `limiti` e `riassunto`, che **non** sono fra i sei
campi esportati e servono a `valida()` e a costruire il piano da confermare.
Quindi l'inversione lascia in Python una parte del record: non e' una
sostituzione, e' uno spacco. E' il lavoro che la voce C3 del `TODO.md` aveva
gia' dichiarato fuori dalla propria stima.
