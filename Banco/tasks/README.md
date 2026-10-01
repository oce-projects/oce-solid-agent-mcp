# `tasks/` — quello che si chiede al MODELLO, non alla libreria

> **Questa cartella non e' un banco.** E' l'INGRESSO del banco della voce
> Z-G9, che oggi non esiste. Qui ci sono i compiti e **come si misurano**; il
> giro che li esegue va ancora scritto.

## Perche' esiste, e perche' prima del codice

Il banco di `Banco/cases/` esegue piani **gia' scritti da una persona**: misura
la libreria, e lo fa bene. Ma non risponde alla domanda che conta davvero —
*il modello, da solo, riesce a fare il pezzo?*

Finche' quel giro non esiste, «il modello disegna meglio» resta un'opinione con
una tabella intorno.

E la parte difficile di quel giro **non e' eseguirlo**: e' decidere cosa
significa «giusto». Un banco in cui il pezzo si guarda e si dice «si', mi
sembra buono» non e' un banco: e' una demo. Per questo i compiti nascono qui,
con la loro misura accanto, **prima** di chi li esegue.

## Com'e' fatto un compito

Un file JSON con due parti che non si toccano:

```
compito         cosa si chiede, in italiano d'officina, come lo direbbe
                qualcuno al banco. NIENTE nomi di operazioni, niente
                argomenti, niente numeri che non direbbe un umano.

come_si_misura  le attese, nella stessa forma dei casi di `Banco/cases/`:
                `cosa`, `uguale`/`contiene`, `perche`. Si valutano sul pezzo
                FINITO, non sui passi: al modello non si dice come arrivarci.
```

**Il compito non nomina le operazioni, e non e' una dimenticanza.** Se il
compito dicesse «usa `extrude_rectangle` poi `cylindrical_cut`», si starebbe
misurando l'obbedienza, non la capacita' di scegliere. La scelta delle
operazioni **e' cio' che si misura**.

## Come si misura, e cosa vuol dire «giusto»

Tre esiti, e vanno tenuti separati:

```
GIUSTO      tutte le attese reggono
SBAGLIATO   il pezzo esce, e almeno un'attesa non regge
NON FATTO   il piano e' stato rifiutato, o e' fallito a meta'
```

**«Sbagliato» e «non fatto» non si sommano.** Un piano rifiutato e' il ponte
che ha funzionato: ha detto di no invece di produrre un pezzo storto. Un pezzo
sbagliato e' il caso peggiore — nessuno se ne accorge finche' non arriva in
officina. Sommarli darebbe un numero che nasconde la differenza fra la difesa
che regge e la difesa che manca.

Il numero della voce Z-G9 e' **quanti escono GIUSTI al primo colpo**, e il
primo colpo conta: si misura senza rilanciare, senza aggiustare il compito,
senza dire al modello cosa ha sbagliato. Un numero ottenuto al terzo tentativo
descrive un modello che non esiste.

## Le tolleranze, e perche' ce ne sono

Un volume calcolato a mano e uno misurato da SOLIDWORKS divergono per i
raccordi che nessuno ha chiesto e per l'arrotondamento. Dove un'attesa ha una
tolleranza, **la tolleranza porta il suo perche'**, come nei casi: una
tolleranza senza motivo e' un'attesa che si allarga finche' non passa.

## Cosa NON e' in questa cartella

- **Non c'e' il giro che li esegue.** Va scritto, ed e' la voce Z-G9.
- **Non c'e' un punteggio.** Il numero esiste solo dopo che il giro e' girato;
  scriverlo adesso sarebbe inventarlo.
- **Non ci sono compiti che nessuno farebbe.** Ogni compito qui e' una cosa che
  in questa officina si e' fatta davvero o si farebbe: una staffa, un telaio,
  una piastra forata, un coperchio. *Un banco costruito su pezzi inventati
  misura quanto il modello sa fare pezzi inventati.*

## The autonomous run of D9 (W3.1-bis.5)

The runner exists now, and the scorer beside it; the Z-G9 bench is still to be
written on top of them. `run-autonomous.ps1` hands each task (its first
blockquote) to `claude -p` with the bridge as the only MCP server, keeps the
transcript and, at the end of each session, the state of the document in front
read by a fresh bridge; `score-autonomous.js` reads the tables above and says
RIGHT, WRONG or NOT DONE per session and the median per task. A row the scorer
cannot read (one component of an assembly) is listed BY HAND with its session.
`score-autonomous.js --self-test` checks the reading of the tables and of the
outcomes without SOLIDWORKS.

**The measured fields are the ones the bridge writes.** Fourteen rows named a
field no operation writes (`holes_on_circle`, `cut_list_rows`,
`export_succeeded`...) and six more a field written under another name or not
at all (`interferences` is the list, the count is `how_many`; `balloons` is
the operation, the count is `balloons_placed`): W3.1-bis.5 rewrote them on the
real fields, naming the step whose answer holds them.

The threshold of D9: every task ×3 on the Italian version (median per task of
the bridge before the rename against the bridge after it, at most one task of
fifteen worse), ×1 on the English version (a signal, not a threshold). The
Italian version is not in the repository; `<it>` below is its folder.

```powershell
# before: the bridge of the snapshot (the catalogue before the rename), built in a worktree of 9384b2c
.\Banco\tasks\run-autonomous.ps1 -Language it -TasksDir <it> -Repeats 3 -Label before `
    -Bridge <worktree of 9384b2c>\bin\oce-solid-agent-mcp.exe -Between '<close the documents>'
# after: this bridge
.\Banco\tasks\run-autonomous.ps1 -Language it -TasksDir <it> -Repeats 3 -Label after -Between '<close the documents>'
.\Banco\tasks\run-autonomous.ps1 -Language en -Repeats 1 -Label after -Between '<close the documents>'
node Banco\tasks\score-autonomous.js Banco\report\autonomous\<stamp>-it-before
node Banco\tasks\score-autonomous.js Banco\report\autonomous\<stamp>-it-after
node Banco\tasks\score-autonomous.js Banco\report\autonomous\<stamp>-en-after
node Banco\tasks\score-autonomous.js --compare Banco\report\autonomous\<stamp>-it-before Banco\report\autonomous\<stamp>-it-after
```

If the run after is worse, the name is changed, not the threshold.
