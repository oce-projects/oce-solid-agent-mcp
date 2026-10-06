# `tasks/` — quello che si chiede al MODELLO, non alla libreria

> **Questa cartella non e' un banco.** E' l'INGRESSO del banco della voce
> Z-G9, che oggi non esiste. Qui ci sono i compiti e **come si misurano**; il
> giro che li esegue va ancora scritto.

## Perche' esiste, e perche' prima del codice

Il banco di `Bench/cases/` esegue piani **gia' scritti da una persona**: misura
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

come_si_misura  le attese, nella stessa forma dei casi di `Bench/cases/`:
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

**The run before speaks the protocol of before.** The bridge of 9384b2c answers
`esiti`, `riuscita`, `misure` and describes the part with `corpi_totali` and <!-- window-2: keep -->
`facce`; read with the English keys only, every session of the run before would <!-- window-2: keep -->
be NOT DONE and «at most one task worse» could not fail. So the scorer reads
every answer and every state through the name map (the rows `response_field`,
`operation`, `enum_value`, `response_value`, with the sites of
`Checks/language/window-2-surface.js`), `--read-state` reads the state with the
tool the bridge has (`inspect_part`, or `guarda_il_pezzo` before the rename), <!-- window-2: keep -->
`run.json` records the bridge's version and protocol, a run in which no session
gave a single outcome is refused instead of scored, and `--compare` refuses a
`score.json` of a scorer that did not read the protocol of before. The
self-test has a session of the bridge before that must score RIGHT (seen red
with the reading through the map switched off: 3 checks fail).

**Each session sees the bridge and nothing else**: an empty temporary folder of
its own (so no `Bench/tasks/NN_*.md` with the expected answers, no cases, no
project memory), no built-in tool (`--tools ""`), no settings file
(`--setting-sources ""`), no skill, the bridge as the only MCP server. The user
`~/.claude/CLAUDE.md` is skipped only with `--bare`, which needs
`ANTHROPIC_API_KEY`; without it the runner says so and `run.json` records it
(`isolation.user_claude_md`). Set the key for the run that counts.

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
.\Bench\tasks\run-autonomous.ps1 -Language it -TasksDir <it> -Repeats 3 -Label before `
    -Bridge <worktree of 9384b2c>\bin\oce-solid-agent-mcp.exe
# after: this bridge
.\Bench\tasks\run-autonomous.ps1 -Language it -TasksDir <it> -Repeats 3 -Label after
.\Bench\tasks\run-autonomous.ps1 -Language en -Repeats 1 -Label after
node Bench\tasks\score-autonomous.js Bench\report\autonomous\<stamp>-it-before
node Bench\tasks\score-autonomous.js Bench\report\autonomous\<stamp>-it-after
node Bench\tasks\score-autonomous.js Bench\report\autonomous\<stamp>-en-after
node Bench\tasks\score-autonomous.js --compare Bench\report\autonomous\<stamp>-it-before Bench\report\autonomous\<stamp>-it-after
```

If the run after is worse, the name is changed, not the threshold.

### Every session starts with no document open, and with the part its task talks about

Two defects of the first run (the night of 6/10, entry W3.1-bis.5) made tasks
09-15 measure nothing about the model, and the runner now repairs both.

**1. The documents.** Between two tasks the run closed only the documents never
saved, so what task 08 saved in its temporary folder (the assembly and its two
parts) stayed open for every later session, and a weld profile of the library
stayed open after tasks 06-07. Now the runner writes down the documents open
before the run (`documents-before-run.json`: the person's, never closed) and,
before and after every session, closes every other one whatever its state:
unsaved, saved in the folder of a session, a library file, a drawing
(`session-documents.js`: the rule, with its self-test; `Checks/CloseLeftovers`
does the closing, with its two modes `--list-json` and `--close-titles-file`,
so rebuild it with `Checks\build-checks.ps1`). A document that stays open after
three passes stops that session (counted as not started): it would not start
from an empty SOLIDWORKS. `-Between` is still there for a command of your own.

**2. The prerequisites.** Seven tasks start from the result of an earlier one,
but each session runs in an empty folder, so the model found a box and answered
«the bracket is not there». The fair fix does not touch the text of a task or
what it measures: the runner BUILDS the part the task talks about before the
session, through the same bridge, from a reference plan written in the format of
the bench cases (`prerequisites/`), saves it in the folder of the session and
leaves it open in front. The numbers of each plan are the ones of its own task,
and the runner checks them (the `expect` entries) before the session starts: if
the prerequisite is not the part the task describes, the session is NOT run
(`<task>-<n>.prerequisite.json` says why, `run.json` lists it, the scorer
counts it NOT DONE) instead of measuring another part.

| task | starts from | reference plan (`prerequisites/`) | what is in front, saved in the folder of the session |
|---|---|---|---|
| 09 mirrored pair | 01 L-bracket | `01_l_bracket` | `01_l_bracket.sldprt`: the bracket, 67,782 mm3, holes of 9 |
| 10 pair drawing | 09 pair | `09_bracket_pair` | `base_plate`, `bracket` and `09_bracket_pair.sldasm`: plate fixed, bracket mated, its mirrored instance driven by the mirror, 300 x 70 x 120, no interference |
| 11 stand drawing | 07 stand | `07_tube_stand` | `07_tube_stand.sldprt`: frame, crossbar, feet, gussets, caps, cut list (does NOT build on the bridge of today: see below) |
| 12 flange weight | 02 flange | `02_bolt_circle_flange` | `02_bolt_circle_flange.sldprt`: 221,614 mm3, NO material (the task asks for it) |
| 13 lid interference | 08 box | `08_box_and_lid` | `base`, `lid` and `08_box_and_lid.sldasm`: base fixed, lid mated on the rim, 23 tall, no interference |
| 14 plate 3D print | 03 plate | `03_slotted_plate` | `03_slotted_plate.sldprt`: 111,489 mm3, one body, no material (see the plan: the name of the aluminium is Italian) |
| 15 stand STEP | 07 stand | `07_tube_stand` | the same stand as task 11 |

Task 15 names task 07 (and counts 17 solids, the stand of task 07), not the
welded frame of task 06: the index follows the text of the tasks, and
`prerequisite.js --check` refuses an index that misses a task which names another
one, names one the text does not, or holds a plan no task uses.

What the run does NOT measure any more in tasks 09-15 is whether the model can
build the earlier part: tasks 01-08 measure that.

**The plans are a protocol of their own, and run on both bridges.** They are
written in English; for the bridge of before the rename each step is translated
by the reverse run of window 2 (the map read from English to Italian) and each
answer is read back through the map, as the scorer does. The same plans pass
`prerequisite.js --check` against the catalogue of the bridge of 9384b2c.

**What was proven without SOLIDWORKS** (`run-autonomous.ps1 -DryRun` runs it):
every plan against the catalogue (operations, arguments, enum values, the
references between steps, the paths of the expectations), the dry run of the
bridge with `OCE_NO_ATTACH` set, the translation to the protocol of before and
back, the build and the three ways to fail against a script that plays the
bridge, the closing rule against a script that plays SOLIDWORKS (with the
defect of 6/10 reproduced), and the index against the texts of the tasks.

**What was proven on SOLIDWORKS** (6/10, both bridges: after = 0.127.1, before =
9384b2c through the map; `PREREQ_TRACE=<file>` keeps every request and answer of
a build). Ten of the twelve builds hold: `01_l_bracket`, `02_bolt_circle_flange`,
`03_slotted_plate`, `08_box_and_lid` and `09_bracket_pair` on both bridges, every
expectation of their tasks checked. What the first proof taught, and is now in
the plans and the runner:

- a selector never names a face by `normal=`: the bridge of before writes the
  facing in Italian and the map translates that field only in the cases, so the
  face is named by numbers (area, and x, y, z of `point_mm`);
- `diameters_mm` is refused only when the FACES are cut (`faces_total` written),
  not on any `truncated` state: 36 edges set it on the slotted plate;
- an assembly has no bounding box in its state: `body_bounding_box` measures it;
- the components are numbered by NAME (Name2), not by the order they came in:
  the plate of the pair is `base_plate` so that it is C1, and every step that
  names a component comes before the mirror;
- a mirrored instance is driven by its mirror and fully blocked: a mate on it is
  refused as REDUNDANT, so only the first bracket is mated;
- the base of the box and the plate of the pair are fixed by `fix_component`,
  and the plan checks it, instead of resting on the order of the inserts;
- the state of an assembly of before has neither bodies nor faces, and the
  scorer did not see it as Italian: its keys (documents open, count of the
  components) are now marks of that protocol, read from the map.

**The stand (`07_tube_stand`, tasks 11 and 15) does not build on the bridge of
today**, on either bridge, and the runner refuses those sessions (on both sides,
so the comparison is not tilted; the two tasks are not measured). Three defects
of the library, each enough alone, are written in the plan's `_status` with what
the probes measured: `cut_list` counts only the structural members (never the
17 bodies and 6 rows of task 07, which no model can reach either); `gusset`
refuses the faces of a foot at the END of the segment of the bar it rests on and
accepts them at its START; the 60 largest faces leave out the walls of the third
and fourth foot. The plan keeps the numbers of task 07; what the probes proved
for the plan to come (one cap per foot through `trim_members` and `end_cap`
`ends_ref`) is written there too.

**Still to know about the tools around the run:**

- the first `insert_component` into an empty assembly is refused by BOTH bridges
  although the component comes in (it counts -1 before): the preparer accepts it
  on the state of the assembly and says so in its record;
- `CloseLeftovers --chiudi <folder>` closes by TITLE: with a `base.SLDPRT` open in
  another folder, the `base.SLDPRT` of the folder stays open (seen on 6/10, five
  of them), and `CloseDoc` by title could close the other one. The runner's
  `--close-titles-file` asks by title first too; a path closes the right one.
