# SolidWorks MCP — versioni

> **LINGUA DI QUESTO FILE — deciso il 16 settembre 2026 (voci W3.1 / W3.2).**
> Il repository passa all'inglese. **Questo file no, non all'indietro.** Le voci
> storiche restano in italiano e **non si traducono**: raccontano cosa si
> chiamavano le cose *allora*, e *un changelog riscritto e' un changelog che
> mente* (regola della voce I1, che resta in piedi). Le voci **nuove** si
> scrivono in inglese, a partire dalla prima successiva a questa riga. Il
> confine e' qui, ed e' dichiarato invece che implicito.

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

## 0.75.0 — 23 September 2026 · library 0.104.0

### The first section view, and a guard that was right about the wrong number

Bench 864 checks, 0 red, 681/681 expectations, full run. Contract suite 170,
0 red.

`sezione_in_tavola` (entry GT4). A part with an internal cavity, without a
section, **cannot be read on the shop floor**: hidden lines say there is a void,
not what shape it has.

`CreateSectionViewAt5` takes no entity in its signature. The cut line has to be
**drawn on the sheet and left selected** — the API takes that. Five steps that
all have to work, each able to fail in silence. The 0.6 measurement had mapped
them: `Select4(false, null)`, sketch closed with `InsertSketch(true)` *before*
asking for the view, and a line that **crosses** the view — one that grazes it
gives an empty section without saying anything.

```
"vista_sezione":"Vista in sezione A-A", "tipo_vista":2, "e_una_sezione":true,
"viste_prima":4, "viste_dopo":5, "tratteggi":1, "tratteggi_solidi":0
```

**And the check the entry prescribed was on the wrong counter.** GT4 said
*"mandatory check afterwards: `GetSolidHatchCount() > 0` — a section without
crosshatch is a cut line that fell outside the material, and the operation must
fail"*. The criterion is right and it saved the entry; the counter is not.

The operation refused **on its first real use**, inside the first case written
for it, on a vertical cut through the middle of a solid plate. Both could not be
true, so it was measured:

```
hatch BEFORE rebuild: return=0  out=0
hatch AFTER  rebuild: return=0  out=0
face hatch:           1
view type: 2 (swDrawingSectionView)   GetSection(): an object
```

`GetSolidHatchCount` answers **zero on a real section**, before and after the
rebuild — and it has two outputs, a return value and an `out`, both zero. The
counter for a section's crosshatch is **`GetFaceHatchCount`**, which reads 1.

*A right criterion resting on the wrong counter is a guard that always says no,
and it is indistinguishable from one that works until you use it.* Both numbers
come out in the answer: `tratteggi_solidi: 0` is there so nobody walks the
measured road again.

### The bridge could not save and then draw

Preparing the drawing for that measurement, the most ordinary sequence there
is — **extrude, cut, save, create drawing** — stopped at the fourth step:

```
Mi fermo: il documento e' cambiato. Avevi guardato «Parte1597», adesso
davanti c'e' «...\sonda_sezione.SLDPRT».
```

True, and our fault. `salva_documento` changes the document's identity while the
plan runs — before the save it is "Parte1597" with no path, after it is a path
on disk — and `disegna` restored `_guardato` to the identity read *before*
executing. The next `disegna` was refused with a correct message and a wrong
cause. `annulla` already did the right thing three methods further down.

**The bench could not see it**: it calls the library, not the bridge. The group
that comes in through the client's door (entry F14) saw it, the day after it was
born.

### The check on the two library copies, seen going red at last

Entry K9 left one residue, written down as a promise: *"I have not seen it go
red yet, and I say so instead of letting it be believed. At the next library
change, compile the single project on purpose, watch the red, then run
`build.ps1`."*

Done, by taking the library to 0.104.0 and building only its project:

```
0.104.0.0   Riscontro.Disegno\bin\Release\...   (what the bench measures)
0.103.0.0   bin\...                             (what the bridge loads)

NO   il ponte e il banco hanno la STESSA versione della libreria
     BANCO 0.104.0.0 contro PONTE 0.103.0.0 — chi ha compilato il solo
     progetto ha aggiornato il primo e non il secondo: si rilancia
     .\build.ps1, che li allinea
```

That is the defect which on 18 September cost nine hours disguised as "the
operation does not refuse". The cause was not in the code, it was in a file
path.

### `Provata = true` does not mean anything checks it (entry Z-C6)

Measured by crossing the catalogue with every declared case, the bench's C#, and
the hand plans:

| | |
|---|---|
| operations in the catalogue | **70** |
| with at least one declared case | 57 |
| named only by a hand plan — never runs, asserts nothing | 7 |
| **exercised by nothing at all** | **5** |

`cordone_saldatura`, `distinta_taglio`, `fazzoletto`, `taglia_membri`,
`tappo_estremita`: in the catalogue, visible to any client, and no round touches
them. The first of the five now has a declared case; the guard that would make
this permanent is deliberately **not** written yet — *a guard born with twelve
reds is a list somebody silences the next day.*

`Controlli\conta-provate.js` is in the repository so the number can be redone
instead of believed.

### And a folder of cases nobody was reading

Writing that first case created `Banco\casi\saldature\`, and `Famiglie` in
`CasiDichiarati.cs` is an **explicit list**: the folder was not in it, so its
case would have stayed mute. There is now a contract check for exactly that, and
it was watched going red by removing `saldature` on purpose:

```
NO   ogni cartella con dei casi dentro e' nell'elenco
     MUTE, i loro casi non girano mai: saldature
```

Also measured: the other folders outside the list (`ponte`, `limiti`, `viste`,
`pezzi`, `forma`) hold **only plans** — which is correct, but it is a measured
fact, not a guarantee of the mechanism.

### Three times in two days, an expectation broke on capital letters

```
21/9  it looked for «vero»,        the text said «VERO»
23/9  it looked for «accavallano», the text says «si accavalla»
23/9  it looked for «che serviva», the text says «CHE SERVIVA»
```

The second was a verb ending, repaired by matching the stem. The first and the
third are the same thing, and they were not defects in the cases: `contiene`
compared with `StringComparison.Ordinal`, but what it reads is **prose written
for a person**, and in this repository emphasis is made by raising the voice —
that is, by changing the case.

*Three incidents from one cause are not three cases to repair: they are an
operator to repair.* `contiene` and `non_contiene` no longer look at case.

**And `non_contiene` became stricter**, which is the uncomfortable direction: a
"does not say X" that passed only because the text wrote "x" now goes red. It
should — that green was luck, not a measurement — and it is why the change was
made with a full bench run against it rather than by eye. The run says none was
relying on it.

### A twin that was not one, and the case said so itself

The refusal case for the section passed `a_mm: 9999` while its green twin
passed `etichetta: "A"` — **no argument in common**:

```
!!  lim_sezione_fuori_dalla_vista · passo 6
    ERRORE DI BANCO: gemella finta: non ha nemmeno un argomento in comune
    col rifiuto: sono due operazioni che non si parlano
```

The rule was already written, in a sentence copied into that very file from
the note twin: *"what changes is the VALUE of an argument, not which argument
is passed."* Two calls that share nothing do not prove the check on `a_mm`
works; they prove two different calls do different things. The pair now shares
both arguments and differs only in the value — 0 against 9999. Zero is the
default, so the twin behaves exactly as before; what changed is that it now
**says so**.

### Three runs on one version, and none of them wasted

The first found the wrong hatch counter, the second the case-sensitive
`contiene`, the third the fake twin. An hour each, and each removed something
nothing else would have seen — the first two were defects in code that had
just been written and read twice.

---

## 0.74.1 — 23 September 2026 · library unchanged (0.103.0)

### The four silences have two causes, not four (entry K5)

Reference regression re-run on library 0.103.0: the same four cases, the same
numbers as 15 September. Twenty-eight library versions later,
`Banco/resoconto/regressione.md` is **byte-identical**. No code changed in this
release — only the measurement, and what it is written down as.

**Three of the four are one repair.** The half that settles it is that two
`ancorato_a_bordo` cases **hold**: raise a box by 28 mm and the hole in its
floor stays on the floor, because it is anchored to a **face** and the face
moves with the part. So the cause is narrower, and more useful, than "absolute
anchoring":

*an operation anchored to a **face** follows the geometry; the `x_mm`/`y_mm`
coordinates inside that face's sketch plane do not.*

Four edge-anchored cases, two each way.

**The number that proves it is not an approximation.** In `r03` the plate grows
from 200 to 260 and the expected centre of mass is x = 30:

```
if the pocket follows the centre    x = 30.000   (what the case expects)
if the pocket stays at x = 0        x = 30.588   <- measured 30.588
```

**And `r16` turned out to be the same cause, which took two readings to see.**
The case asserts only the volume, and the 3,141.597 mm³ difference fits two
different geometries — an uncut rounded end, or a slot shortened by 7.854 mm.
*Volume says how much material is left, not where.* Reproduced by hand through
the bridge, looking at the faces: 2 curved faces before, **1 after**. A
shortened slot would still have two.

The reason is the same one: the plate shrinks **from one side only**, so x goes
from [-100,+100] to [-100,+50] while the slot stays centred on 0. The right edge
lands exactly on the arc centre, so the right half-disc is never cut:
360,000 − 43,141.593 = **316,858.407** against a measured 316,858.4. The slot
does break out of the side, exactly as the case feared — silently.

**The fourth is not ours, and that belongs where people read it.** A 2×45°
chamfer on a plate that becomes 1 mm thick does not fail: SOLIDWORKS rebuilds
clean and eats a millimetre of width, `[200, 119, 1]` instead of
`[200, 120, 1]`. It cannot be prevented by anchoring better — only caught by
reading the part back afterwards, which is what this bridge does at every step.

The README now says this under the 12/0/4 table, because "four detach silently"
without the causes is a number nobody can act on.

### And one thing this measurement did not do

The cases still assert only what they asserted. For `r16` the faces were
inspected **by hand**, and next run that diagnosis is gone — the volume remains,
and on its own it does not tell two geometries apart. Putting it in an
expectation would mean writing the **wrong** geometry into the case, which is
fitting the test to the defect. The right path is that the case goes green when
E6/E7 is done; until then the diagnosis lives in the entry.

---

## 0.74.0 — 23 September 2026 · library unchanged (0.103.0)

### A group of checks that comes in through the client's door (entry F14)

Bench 843 checks, 0 red, 662/662 expectations, full run — eight of them new,
from the client's door. Contract suite 168, 0 red.

Everything else in this bench calls **the library**. On 18 September the bridge
was refusing every assembly while its own catalogue promised a whole family of
them, and the bench had 781 green checks — five of them on assemblies — and
**not one could see it**: `EsecutorePiano` appears in no file under `Banco/`.

*Two true measurements of two different things, and whoever reads the big number
believes it covers the other one too.*

The new group does not call the library. It starts
`oce-solid-agent-mcp.exe` as a subprocess and talks to it over stdio, the way
any MCP client would — with SOLIDWORKS running and a document the bench opened
itself. That is exactly the difference from `Prove.Contratti`, which has been
speaking the protocol for days **without** SOLIDWORKS: there every plan dies on
"there is no open document", which is a true refusal and proves nothing about
the rest.

**What it proves that nothing else proves:** everything *between* the client and
the library — preliminary checks, the JSON-to-arguments translation, refusals on
operations the open document does not allow, and `guarda_il_pezzo`, which does
not go through the executor at all.

```
OK   porta: guarda_il_pezzo nomina il documento del banco   Parte1241
OK   porta: il piano riesce
OK   porta: e il pezzo e' cambiato davvero, misurato dal banco  0 -> 96000 mm3
OK   porta: un'operazione di assieme su una PARTE e' rifiutata
OK   porta: e il rifiuto dice su cosa sta lavorando
OK   porta: il ponte e' ancora vivo dopo una notifica
```

**The bridge says "riuscita"; the bench does not take its word.** It reads the
volume back from SOLIDWORKS over COM: 0 → 96,000 mm³ for a 120×80×10 plate.
That is the one thing this group can do and the contract suite cannot.

### Red by construction, and watched going red

An `assiemi` operation with a **part** open must be refused, and the refusal
must say *what it is working on* — not a fixed sentence. Proven by temporarily
pointing that case at an operation that does work on a part: both refusal checks
go red, and the detail shows `"riuscita":true` where it should have been false.

Two assertions were too loose on the first pass, and loose in the worse
direction — they passed in cases that should have failed. One accepted any
answer containing the word «assieme», which a *successful* plan could contain;
*a `||` added for safety is a `||` that one day holds on its own.* The other
accepted the word «parte», which sits in any fixed sentence about document
types — exactly the answer the check exists to reject. Both now demand the
document's own title.

### One transport, not two

`Prove.Contratti` already had the plumbing, so it moved to
`Condiviso/ClienteMcp.cs` and both projects compile it. Copying would have been
quicker and wrong: inside it is something that cost an afternoon — the UTF-8 BOM
that `AutoFlush = true` writes in front of the first line, three bytes that made
the bridge answer «Primitiva JSON non valida» while the symptom looked like
«initialize does not answer». *A defect paid for once gets fixed in one place,
or it gets paid again in the copy.*

`Condiviso\` is now in the folder-to-project map of the check that verifies every
source belongs to a project — otherwise it would have been the one source folder
nobody looks at, where a file could sit uncompiled and unmentioned.

---

## 0.73.0 — 23 September 2026 · library 0.103.0

### The 16 KB ceiling was already broken, and the bench could not see it

Bench 835 checks, 0 red, 662/662 expectations, full run. Contract suite 168,
0 red. The check now reads: `13216 caratteri, margine 2784 (17%)`.

The bench check «stato: sotto i 16 KB su un telaio da quattro barre» passed at
**15,922** characters against 16,000 — seventy-eight to spare. But the bench
runs with **exactly one document open**, because it refuses to start otherwise.
The same frame with two documents open — what anyone working actually has —
measures **16,089**.

*The ceiling did not have a thin margin: it was already over, and the check
could not see it because the bench cleans its own desk.* Two correct defences
which, together, hide a third.

**Where the characters are, measured rather than imagined:**

```
    8,718  facce      60 entries   (145 each)
    5,572  spigoli    60 entries   ( 93 each)
    1,799  everything else
```

Eighty-nine per cent is two lists, and the entries are already lean — an edge is
`{"uid":"E1","lunghezza_mm":1000,"punto_mm":[...],"lungo":"X","fra":["F36","F1"],
"corpo":"C3"}`. There is no fat to trim: **the only lever is how many**. Which
is the question the check's own comment had been asking for days — *"do we
really need sixty edges?"* — and the answer is no.

**Edges, not faces.** Faces are the attachment surface: almost every operation
takes a `faccia_uid`. Edges are used by few — fillets, chamfers, drawing
dimensions — and on a four-bar weldment they are noise: sixty out of three
hundred–odd is a sample you cannot rely on anyway. `MaxSpigoli` goes from 60 to
30, and the ceiling still declares itself: `spigoli_totali` says how many there
were, `troncato` goes true.

Measured after: **13,378 with two documents open**, a 16% margin.

**And the check now demands the margin, not the ceiling.** It is called «stato:
sotto i 16 KB con almeno il 15% di margine», and it prints the margin **always**,
including on green — the only moment you can see the wall coming. *A check that
says "passed" seventy-eight characters from the wall is not warning anyone.*

---

## 0.72.0 — 23 September 2026 · library unchanged (0.102.0)

### The same document, with something else inside it (entry X2)

Contract suite 168, 0 red. The bench was not re-run: the library did not
change — only the bridge did.

The bridge already refused to draw on a document that was not the one you
looked at. It did not notice **the same document with something else inside**:
title and path match perfectly while someone drags a dimension in the
SOLIDWORKS window.

That matters because **face identifiers are positional**. A plan is born from
one reading and its `F1`, `F2` speak of that moment; on changed geometry `F1`
may be a different face and the operation succeeds in the wrong place — the
defect already measured on 14 September with the two pockets, both of them
"riuscita: true".

`guarda_il_pezzo` now keeps a light signature — volume, number of features,
file write time — and `disegna` compares it before executing. All three are
already in hand: no extra reading, nothing new asked of SOLIDWORKS. *A check
that costs as much as the thing it protects does not get run.*

**It stops with the criterion, not with a verdict.** «il volume e' passato da
96000 a 97125 mm3; le lavorazioni sono passate da 1 a 2» can be checked; «lo
stato e' cambiato» has to be believed. The difference is the risk the entry
itself named: an unverifiable warning is one you learn to ignore.

**What it does not catch, said here rather than discovered:** a change that
leaves the volume identical and the feature count unchanged — moving a hole
without resizing it. Catching that needs a geometric fingerprint, which this
is not.

### The false positive the entry warned about, written and then found

The signature was taken in `guarda_il_pezzo` and never refreshed, so
`guarda -> disegna -> disegna` stopped at the second: the first `disegna`
changes the volume, and the comparison read that as someone editing by hand.
The rule was already there three lines further down, for the uid signatures:
after the bridge itself changes the part, the new reading is the base for the
next plan. The signature now follows it in the same three places — after
`disegna`, after undo, after redo.

**No contract test could have seen that**, and that is the point of the probe.

### `Controlli\prova-x2.js`, and a second actor

Nine contract checks cover `CosaECambiato`, a pure function on two
dictionaries. They do not cover the wiring, and the wiring is where the bug
was. The probe drives the bridge over stdio and proves both halves:

```
=== 1. guarda -> disegna -> disegna, senza falsi positivi
  OK   il secondo disegna passa (nessun falso positivo)
=== 2. qualcuno cambia il pezzo da fuori, e il disegna dopo si ferma
  OK   qualcuno ha cambiato il pezzo da fuori    volume: 96000 -> 97125 mm3
  OK   il disegna dopo si ferma                  Mi fermo: il pezzo e' cambiato...
  OK   e dopo aver riletto si riparte
```

The change has to come from **outside the bridge** — while it comes through
the bridge, the bridge knows and refreshes its signature — so there is a new
tool, `Controlli\CambiaAMano`, which attaches to SOLIDWORKS and extrudes a
block into the active document. Compiled C#, like every COM probe here:
PowerShell 5.1 cannot resolve methods on the SOLIDWORKS IDispatch.

**Two things the probe got wrong first, both worth keeping written.**

It moved the file's write time instead of changing geometry — and after
`salva_documento` the open document still answers `percorso: ""`, so there was
no date in the signature to compare. *A probe testing the wrong thing is worse
than no probe: it gives a red that sends you looking where the defect is not.*

And it paired replies to requests **by arrival order** instead of by `id`. One
extra line on stdout and everything shifts by one: it was reading the previous
call's answer and judging it. That is not a random error — it is a systematic
off-by-one, and it is why the protocol has an `id` at all.

---

## 0.71.1 — 23 September 2026 · library unchanged (0.102.0)

### The guard checked the library, not the bench

Contract suite 159, 0 red. The bench was not re-run: nothing in the library
changed.

`Prove.Contratti` refuses a delivery where nobody ran the bench — it reads
`Banco/resoconto/ultimo.json` and checks the run is whole, exited 0, on the
current library, and later than the last change to `Riscontro.Disegno`.

**It watched one half.** The bench is two things: the library that measures
and the bench code that decides what to measure. A new check, a new counter, a
stricter expectation — none of it triggered anything, because the library had
not changed.

Not hypothetical: earlier today the clean-desk guard and the CAD-document
count went into the bench *after* a run had started, and the committed report
described a bench that was no longer in the repository. Not false — that run
happened — but answering a different question from the one it appears to
answer: not "this bench is green" but "a bench from a few hours ago was".

**Watched firing, on a real commit, in a throwaway clone.** One line added to
`Banco\Program.cs`, committed without re-running: the library check stays
green, the bench check goes red. In a separate clone because proving it here
would have meant dirtying the history to clean it up again — *a test you run
by undoing a commit is a test you will eventually skip.*

**And a `null` that meant two things was untangled.** "Git did not answer" and
"there is nothing to find" both returned `null`, and the first was printed with
the second's message. A repository with no unproven commits is now a green that
says so; git not answering is a red, because a check that could not look is not
a check that found nothing.

---

## 0.71.0 — 23 September 2026 · library 0.102.0

### A drawing that would not close, because its name had changed

Bench 835 checks, 0 red, 662/662 expectations, full run, 67 minutes. Contract
suite 158, 0 red. **Zero documents left open afterwards**, which is this
entry's own closing criterion.

**Fourteen documents were left open after every full run**, seven parts and
seven drawings, one pair for each drawing case. The mechanism that should have
closed them existed, was correct, and carried a comment explaining exactly why
it mattered — the ~650 characters of extra state and the 16 KB check that turns
red without any code having changed.

**The title was captured at the wrong moment.** When a step produces a new
document the bench records it for later closing. At that instant the drawing is
called `Disegno38 - Foglio1`. Two steps later the case saves it and it becomes
`la_tavola_si_quota - Foglio1`. At teardown the bench asked SOLIDWORKS to close
`Disegno38 - Foglio1` — **and `CloseDoc` on a name that does not exist is not an
error**, so nothing said anything. An open drawing then holds its part open, so
each case leaked two.

**A document's identity is not its title.** The same lesson as the active
document: hold the reference, ask for the name when you need it.
`Preparazione` now carries `DocumentiDaChiudere`, a list of `IModelDoc2`, and
teardown closes those before the names — the drawing holds the part, not the
other way round.

**And teardown counts again.** It cannot go red — it also runs inside a case
that already failed, and a second red would bury the first — but if documents
remain it says so. A line that appears only when something did not work is the
least that stops a silent defect from living another seven days.

**How it was found is worth as much as the fix.** Not by reading the code, but
by reading the run's own diary: the line `| da qui si lavora su «Disegno38 -
Foglio1»` says the name the bench saw, and comparing it with what
`ChiudiScarti` listed after the run closes the diagnosis in a minute. *The bench
had been saying it all along; nobody had put the two lists side by side.*

### And it moved a number that was not supposed to be a measurement

The K2-bis count — how many checks run with a CAD document open — went from
**806 with / 29 without** to **750 with / 85 without**. Fifty-six checks were
counted as *with a document* only because seven leaked drawings were still
sitting there. The entry had declared the figure a ceiling rather than a
measurement precisely because of this leak; the leak is gone, and the ceiling
came down by fifty-six.

*A declared blind spot is worth what it costs to close it* — here it cost one
line of diagnosis and produced a number that is fifty-six units less
flattering.

### Smaller

- **`soprattutto` counted the band that goes below.** The horizontal-dimension
  counter was named "above all" and its next line subtracts. Renamed to `sotto`;
  no behaviour change. It had already produced two wrong *descriptions* in one
  day — the 0.101.0 version note and a spoken summary — which is exactly how a
  misleading name costs.
- **The 0.101.0 note said «orizzontali sopra».** It is wrong, and it is not
  corrected in place: *a version note rewritten backwards is a note that lies.*
  The 0.102.0 note contradicts it where someone reading in order will find it.
- **The comment about `AssemblyInformationalVersion` said no check compared the
  two numbers.** One does now, and it fired the same day. *Between a comment
  that warns and a test that goes red there is no difference of degree: the
  first is read after you have already made the mistake.*

---

## 0.70.0 — 23 September 2026 · library 0.101.0

### Dimensions land where they belong, and a CI that had never once been green

Bench 835 checks, 0 red, 662/662 expectations, full run, 77 minutes. Contract
suite 158, 0 red.

**Dimensions are no longer left in a heap on top of the view.** `quota_in_tavola`
created them and dropped them all at the same spot, so a drawing came out with
four labels stacked over the part. The placement is now computed from the edge
itself: `IEdge.GetCurveParams3()` gives the two endpoints in the model,
`IView.ModelToViewTransform` maps them onto the sheet, and **only then**
`IView.Position` is added — without that last term every dimension landed at
x = 0, which is exactly what the first attempt did.

Horizontal edges get their dimension below the view, vertical ones to the right,
oblique ones off to the left on their own, and the three bands are stepped
independently: 12 mm clear of the outline, 10 mm apart. Shortest nearest the
part. Those are not invented numbers — UNI ISO
129-1, as reported by the Zanichelli sheet (7–12 mm between parallel dimension
lines on A3/A4) and the Università del Salento lecture notes (more than 10 mm
from the outline, more than 6 mm between lines). The two values the code
already used satisfy all four constraints: *the defect was never the numbers,
it was the direction.*

**Duplicates are removed by geometry, not by value.** The first version
deduplicated on the measured length, and that is wrong in a way that only
shows up on a real part: two different 10 mm steps are two different features
and both have to be dimensioned. The key is direction on the sheet plus the
interval of the projection onto the measuring axis.

**And the operation says what it did not look at.** Which dimensions a drawing
actually needs, whether the dimensioning is complete, which view shows a
feature most clearly, whether the text overlaps the geometry — none of that is
decidable here, and the answer carries the list instead of implying it did
everything.

### The CI had never compiled, and the README said it did

`.github/workflows/prove.yml` began with `msbuild Oce.Solid.Agent.csproj`. That
project — and `Riscontro.Disegno` under it — resolves `SolidWorks.Interop.*`
through a `HintPath` inside the SOLIDWORKS installation. No GitHub runner has
that folder, and the interop belongs to Dassault: it cannot be committed and it
cannot be downloaded.

Measured rather than assumed: building with `/p:SwInstallPath=Z:\non-esiste`
produces two hundred–odd `CS0246: the type or namespace name 'IModelDoc2' could
not be found`. The first step failed every time, so the two steps that were
worth something — the contract suite and the refusal to ship without a bench
run — were never reached. **A CI that has never run is a badge, not a test.**

The bridge is now compiled **only where the interop is present**: a step looks
in the same three places msbuild looks, and on a self-hosted runner with
SOLIDWORKS the bridge really is built (and a missing executable there is a red,
not an excuse).

**What cannot run does not turn green.** The suite has a third verdict beside
OK and NO: `--`, *not runnable here*. It is counted separately, printed, written
to `prove.xml` as `<skipped>` with `skipped=` on both the suite and the total,
and summarised on the run page. The declared minimum is now measured against
what actually ran — twenty-five rows of "not runnable here" used to satisfy it.

**The distinction that holds it together** is between *cannot here* and *you
forgot to build*. Same symptom — no executable — opposite defects. They are
told apart by looking at the interop, not at the executable.

**And what could run anyway, now does.** The catalogue group stopped at its
first line because `bin\catalogo.json` is produced by `build.ps1`: thirty-odd
checks on the catalogue's *content* were skipped although they read a JSON file
and have nothing to ask SOLIDWORKS. They now fall back to
`Strumenti\catalogo.json`, which is in the repository, and say so.

**The new verdict was watched firing.** `PROVE_SENZA_INTEROP=1` makes the suite
behave like a bare runner on a machine that has SOLIDWORKS — otherwise the code
that writes those rows would have stayed untested until the first CI run, which
is another way of saying *believed*.

### The bench now refuses to start on a dirty SOLIDWORKS

A full run — an hour and a quarter — went red on `stato: sotto i 16 KB su un
telaio da quattro barre: 17435 caratteri, 15 documenti aperti`. Nothing in the
code had changed; fifteen documents had been left open by an earlier session,
and a document's state lists `documenti_aperti`.

It had happened before, on 14 September, and the remedy was already written in
the comment next to that very check. The first version of this entry concluded
that the remedy was documented and never executed. **That was wrong, and worth
saying so:** `banco.ps1` runs it, step 3 of five, before every run. What
bypassed it was a `ProvaSaldatura.exe` launched by hand to pass `--libreria`.

The real finding is less comfortable. **The protection lived in the wrapper**,
and you step outside the wrapper precisely when you are doing something
unusual — a different library, one group, an existing binary — which is when it
matters most.

The bench now looks first **however it is launched**, lists what it found, and
exits 3 — a run that never started is not a red run, and calling it red would
mean writing the report of a run that did not happen. `--chiudi-gli-scarti`
closes them and goes; it is not the default because `CloseDoc` discards unsaved
changes, and on this machine that could be someone's work. It also counts again
afterwards: "I called close fifteen times" is not "they are closed".

`banco.ps1` step 3 and this check now say the same thing twice, and that is
fine: the first quietly closes what it knows is scrap, the second refuses to
begin if anything is left.

Measured both ways on the real dirty desk: without the flag, exit 3 and fifteen
names; with it, `chiusi 15 documenti su 15, ne restano 0`, and then the check
above passes at 15922 characters.

### How much of that 835 actually has SOLIDWORKS in it

The per-group count (entry K2) said *where* the checks are. The other half of
that entry asked *how many of them have a CAD document under them* — and the
answer is now printed beside the total:

```
prove con un documento CAD aperto: 806 - senza: 29
```

**It is asked of SOLIDWORKS, not of the bench.** The first draft kept an
internal counter, incremented on open and decremented on close, and it would
have been wrong: documents are opened in at least four places and closed in
three, one of which deliberately tries twice (entry GT0, the teardown order
nobody claims to know). *A counter with seven keepers is a counter that will one
day say -1.* `GetDocumentCount()` asks the one who knows.

**And it says what it really measures**, which is not what the entry's title
promises: "a document was open when this check was recorded". Since the bench
only starts on a clean desk, that document is necessarily one it opened itself —
but while the drawing cases leak documents (entry K12-bis), later checks count
as *with document* even when they never touched CAD. **It is a ceiling, not a
measurement, and the summary line says so** rather than leaving it to be
discovered.

### Smaller things, all of them found by something other than reading

- **The publishability check was looking at 29 commits out of 104, and the
  history rewrite was the reason.** `git filter-branch -- --all` rewrites the
  *local* remote-tracking refs too, so `origin/main..HEAD` collapsed to 29 while
  the real remote still held the old history. It now asks the remote where it
  actually is, and looks at everything when the answer differs — or when the
  remote does not answer at all.
- **Two TODO entries were both called K11.** Caught by the contract check that
  exists for exactly that, not by rereading.
- **A bench expectation was pinned to a verb ending.** It wanted «accavallano»
  and the rewritten text says «si accavalla». Second time in two days that an
  expectation hung on a word form — the first wanted «vero» where the text said
  «VERO». It now matches the stem.
- **`CONFRONTO.md` exists** (entry K6), with the (D)/(M) rule and a column the
  entry did not ask for: the date of each measurement. Rewriting it found one
  figure that was simply wrong — "~100 commits in 9 days" is 104 in 13 — and
  nobody would ever have contested it.
- **The six trademark conditions** of entry I4 are applied: SOLIDWORKS® is an
  adjective everywhere in the README, the English non-affiliation formula and
  the explicit reference to Apache-2.0 section 6 are in `NOTICE`, and the short
  declaration is repeated at the foot of the README, because almost nobody
  opens a NOTICE.
- **`AssemblyInformationalVersion` was two versions behind again**, and this
  time a check said so instead of a comment: `assembly 0.101.0.0, proprietà del
  file 0.100.0`. The comment warning about exactly this has been in
  `AssemblyInfo.cs` since the last occurrence — *a comment is not a check*. The
  fix landed after the bench run; it changes one metadata string and no
  behaviour, and `AssemblyVersion` — the number the report is checked against —
  is the same 0.101.0 the bench measured.
- **The PowerShell of the CI now lives in `.github\passi\*.ps1`**, not inside
  the YAML, and `Controlli\controlla-workflow.ps1` parses every one of them and
  checks the workflow actually invokes them; the `pre-push` hook runs it. Code
  inside a YAML nobody executes is this entry's own defect moved up one level.
  Twenty minutes after being written it caught a real break: converting those
  scripts to pure ASCII (house rule — PowerShell 5.1 reads a BOM-less file as
  ANSI) turned one line into `$r += ""Non eseguibile qui" ..."`, quotes inside
  quotes. `riga 48: Token 'Non' imprevisto`, first try.

---

## 0.69.0 — 23 September 2026 · library 0.100.0

### How much the suite actually measures, and a check that could never pass

Bench 832 checks, 0 red, 659/659 expectations, full run, 77 minutes. Contract
suite 158, 0 red.

**What the number covers (K2).** 760 of 832 checks sit in one group. «832 checks
on real SOLIDWORKS» covers far less ground than the number suggests, and putting
it in front of anyone counts in the same figure checks that never open a CAD
document. Not a lie: *a big number reassures in proportion to how big it is, not
to how relevant it is.* The report now carries the per-group count, and goes red
if the sum stops matching the total.

**What it asserts, and what it only survives (K3).** `659 with a declared
expectation, 173 without`, and the names of the second group go to
`Banco/resoconto/senza-attesa.txt`. The list also shows the entry overstated
it: 43 are twin checks, 42 are refusal steps asserting the message, 26 are the
bench checking itself, 16 are lookup tables. Most assert — in C# instead of in a
case file.

**A check that could never pass.** «the run is later than the last change to the
operations» compared the run with the commit date, but the report is written
before it is committed and lands in the same commit. On b35550d: run 21:12:13,
commit 21:13:52. It missed always. Now it walks the library commits and stops at
the first that does not carry a fresh report — a commit that carries one *is* the
run.

**Two documents described this machine instead of the repository.** The bench
LEGGIMI counted `casi/forma/ 5`, which git does not track: the check «every
folder named in the LEGGIMI exists» was green here and red for anyone cloning.
Found by cloning. And TODO.md pointed at `scratchpad/conta_todo.js`, which is
not versioned — the tool meant to keep the counts honest could not be run by
anyone. `Controlli/conta-todo.js` now exists, and the counts are 108 open of
300, not 104.

Plus: the README carries the trademark line it was missing.

---

## 0.68.0 — 22 September 2026 · library 0.100.0

### A note on the drawing, and the counter that would have closed four entries

Bench 831 checks, 0 red, 659/659 expectations, full run, 71 minutes. Contract suite 158, 0 red.

**`nota_in_tavola` (entry E2-bis).** A workshop drawing is not only geometry and
dimensions: it carries the general notes — "break all sharp edges", "galvanise
after welding", "unspecified tolerances ISO 2768-mK". Until today the bridge
could not write one.

Measured on the real drawing, first through the bridge and then by the bench:

```
note_foglio_prima: 67  ->  note_foglio_dopo: 68
note_viste_prima:   0  ->  note_viste_dopo:   0
dove_mm: [20, 250]         testo_combacia: true
```

**The second line is the one that earns the entry.** `InsertNote` writes on the
SHEET, but `GetNoteCount()` lives on `IView` — and a drawing's real views start
*after* the first one, because the first "view" **is** the sheet.

Counting only the real views would have read `0 -> 0` with the note sitting
exactly where it belongs, and the conclusion would have been "notes do not get
created either: this is systemic". That false negative would have wrongly closed
four entries of this TODO — surface finish, datums, welding symbols, GD&T — all
of which are open precisely *because* a note is born.

So the two counters come back separately, and the bench case asserts that **the
views stay at zero**. Without that line, `note_messe: 1` would be green even
with the note inside a view — and a note inside a view travels with the view.

### Two things that went into the code because they were measured

- **`InsertNewNote3` is `Void`** — a button, not an API: it has no way of saying
  it did nothing, and it does not. `InsertNote` answers, so that is the one.
  (The entry named `InsertNewNote2`; this installation exposes the `3`, equally
  mute.) Written in the source so nobody tries the other one again.
- **The position does not read back on the first call.** `GetTextPoint2()`
  returns an object whose `ArrayData` is null. Without a second route this would
  have been written down as "on this version the position cannot be read", as if
  it were a property of SOLIDWORKS instead of a property of the first call
  tried. Three routes; the first that answers wins.

A position outside the sheet is refused **before** anything is written, naming
the number asked for and the sheet size. Outside the sheet a note exists, gets
counted, and is not on the paper — and no error would say so.

### The catalogue can be stale, and nothing was checking

Writing the operation, compiling it, and calling it produced: *"there is no
operation «nota_in_tavola». Did you mean: quota_in_tavola"* — while it existed,
was in the dispatcher, and was inside the DLL the bridge had just loaded. It was
missing only from `catalogo.json`, which is the only thing the bridge looks at to
know what exists.

And `Strumenti/aggiorna.md` said: *"`build.ps1` regenerates the file with every
package, so it cannot fall behind unless somebody deletes that step"*. **Not true
with `-SoloPonte`**, which is how you compile while iterating.

> A guarantee written in a document is not a guarantee: it is a hope with a
> source.

The tool to notice already existed — `--controlla` exits 1 and says *where* — and
nobody was calling it. Two new contract checks now do: the C# against
`Strumenti/catalogo.json`, and `Strumenti/` against `bin/`. They are different
defects: the first means somebody edited the operations and did not re-export,
the second that the bridge is still loading the old one. Both watched failing.

### And a red worth keeping on the record

The first full run came back with three reds, all from one mistake of mine: the
answer says "sia VERO" in capitals and the expectation looked for `vero` in
lower case. `contiene` is case-sensitive.

The interesting part is the second and third red. The bench **refused to count
the twin's refusal as proof** — *"the twin «la_nota_finisce_sul_foglio» is not
green in this run"* — and that is exactly right:

> A call that refuses proves something only if the same call with a valid
> argument works.

The defect was in the expectation, not in the operation. From the outside the
two are written the same way.

---

## 0.67.0 — 22 September 2026 · library 0.99.0

### The dimensions in a drawing are CREATED, not imported

Bench 815 checks, 0 red, 645/645 expectations, full run. Contract suite 156,
0 red.

**`quota_in_tavola` (entry K10).** Until today the bridge could build the part,
save it, make the drawing, put three views on it, read the title block and
export the PDF — and could not put a single dimension on it. A drawing without
dimensions is not a workshop drawing: it is printed geometry.

The other operation, `quote_dal_modello`, **imports** the dimensions the part
already carries, with `InsertModelAnnotations*`. Measured on 18 September: a
part with seven dimensions all marked for drawing, on **eight** drawing
templates (seven stock plus the workshop's own), through five different routes,
brings **zero** of them across — with a positive control green in the same run
(`InsertNote` works on that same drawing). That route is closed.

But the goal was never "the model's dimensions in the drawing". The goal is **a
dimensioned drawing**, and `AddDimension` creates them.

> A name that describes the means instead of the end narrows the solutions
> before anyone looks for them. For two days the other operation's name decided
> the road.

**Every dimension is read back.** `GetSystemValue2` after placing it, so the
operation answers *how much it measures*, not "done". A dimension that appears
and measures something else is worse than one that is missing: on paper it reads
the same, and in the workshop the cut is made on the wrong number.

The bench case asserts the millimetres — 120 the length, 10 the thickness — and
also asserts that **80 is absent**: 80 is the width, visible in the top view and
not in this one. Without that line, "contains 120" would not say *which view*
was dimensioned, because there is a 120 there too.

### Three numbers for the same thing, and a check that was missing

The bench prints **70** operations, the catalogue declares **68**, the README
said **67**.

The first two are both right and measure different things: 70 is what the
dispatcher can **execute**, 68 is what a model can **see**. The two missing ones
— `croci_di_centro` and `distinta_taglio_in_tavola` — are out on purpose, both
`Provata=false`: the API answers "yes" and creates nothing, 0 → 0 on every
combination probed.

**Nobody was comparing the two lists.** The same difference would be produced by
a *forgotten* operation: written, working, never added to the catalogue. That
would give no error anywhere — the bench would test it (it reads
`NomiEseguibili`), the catalogue would not mention it, and no model could ever
call it.

New check: every executable operation is either in the catalogue or declared
unproven, and the reverse — every catalogued operation is really executable.
Watched failing, with `quota_in_tavola` removed from the catalogue by hand:
*"executable and invisible to the model: quota_in_tavola"*.

### The third version number, and a comment that was not a check

`AssemblyInformationalVersion` is the one Windows shows in the file properties.
No code reads it, so it ages on its own: on 18 September it said 0.61.0 with the
assembly at 0.66.0, it was corrected, and today it said **0.94.0 with the
assembly at 0.99.0** — five versions behind, again.

Right above that line there was already a comment explaining the defect at
length. It did not help, and it could not:

> A comment is not a check. Whoever aligns the number by hand keeps aligning it
> for as long as they remember, and then stops.

Now a check compares the two, on the binary and not on the source — the binary
is what ships. Watched failing by putting 0.94.0 back.

### Also

The two library copies had drifted again (bench 0.99.0.0, bridge 0.98.5.0):
`build.ps1` realigns them, and the check written on 18 September caught it
without anyone looking for it.

And a line in the K10 bench case declared "120 the plate, 5 the chamfer" while
the assertion checked **10**. The 5 came from a probe run on a different part;
the assertion had been corrected and the prose had not. Green every time, and
anyone reading without getting to the bottom learned a false number.

---

## 0.66.0 — 18 September 2026 · library 0.98.5

### The drawing of the wrong part

Bench 796 checks, 0 red, 628/628 expectations. Contract suite 151, 0 red.

**`viste_standard` was putting views of a part that had nothing to do with it
into the drawing, and nothing gave an error.** Three views, a PDF that exports,
and a drawing of somebody else's part arriving in the workshop.

`crea_disegno` takes the right part — it is handed to it. But `viste_standard`
runs **on the drawing**, where it no longer holds the part, and fell back to
"the first open part or assembly", in the order SOLIDWORKS happens to list them.

> An order nobody declares is not a choice: it is a draw that looks like a
> choice. And a draw with one ticket looks like an algorithm.

Fixed: one part open, that one — there is nothing to guess. Two or more, it
**refuses and lists them**, asking for the `modello` argument. Watched refusing.

**Why it was invisible for months.** There was always exactly one part open, so
the draw always won. It surfaced the day a second drawing case was added, whose
part sorts first alphabetically and stays open. And the drawing cases could not
have caught it anyway: they counted **three views** and never asked which part
they were of.

> A test that counts the objects and never looks at what they are is green even
> when the part is a different one.

So `la_tavola_e_un_secondo_documento` now asserts `misure.modello`, and all three
drawing cases name the part they want instead of hoping there is only one.

### Four broken instruments, and none of them was the product

Finding this took a day, and the day is worth more than the defect.

- **The witness queued behind the defendant.** A diagnostic expectation that
  tells two opposite diagnoses apart was written *after* the expectation that
  could fail — in a bench that stops a case at the first red. It never spoke:
  the report had the red and not its reason.
- **Empty out of ignorance.** The lookup returns `null` when it cannot reach the
  model; without saying so, the list stays empty **out of ignorance rather than
  measurement**, and an empty list passes any "does not contain". The
  distinction lived only in a comment. *A distinction that lives only in a
  comment is not a distinction: it is an intention.* Now there is a field
  carrying the **path** of the model actually read — and it was that field that
  named the wrong part and ended the hunt.
- **The wrong binary, and it cost the most.** There are two copies of the
  library: the one the bench measures and the one the bridge loads, put there by
  `build.ps1`. Building **only the project** — the most natural thing while
  iterating — updates the first and not the second. They had drifted four
  versions apart for nine hours: the fix was tried three times from the bridge
  without ever being in that binary, and the symptom was perfect ("the operation
  does not refuse"). A new contract check compares the two versions.
- **A wrong verifier.** The script judging one probe printed "refuses but does
  not list the alternatives" — false; it was looking for a part name I had
  renamed. *A wrong verifier delivers a wrong verdict with the same confidence
  as a right one.*

### Also

- `cartiglio` separates "the property is not on the model" from "it **is** on the
  model and the title block shows it empty": two opposite diagnoses that used to
  land in the same list, sending you to repair the wrong thing.
- `salva_documento` looks at **why** it did not save instead of reporting a code:
  a missing folder and a read-only file are visible from here, and `SaveAs`'s
  number says neither.
- Six annotation counters per view in the drawing state, always written even at
  zero — plus six for the **sheet**, because `InsertNote` writes there and
  counting only views would make `note` a field that cannot become true.
- The contract suite goes from 147 to 151 checks.

## 0.65.1 — 18 September 2026 · library 0.98.1

### The drawing that does everything except take a dimension

`Z-C1` asked for `quote_dal_modello` to be re-measured today. It was, and the
answer is **no** — but the value is not "it doesn't work", which was known since
14 September. The value is **where**.

Four measurements, each narrowing the last:

| | question | answer |
|---|---|---|
| 1 | on a part built **by hand**? | 1 dimension in the model, 0 in the drawing |
| 2 | on a part built **by the bridge**? | **7** in the model, **0** in the drawing, 13 routes tried |
| 3 | are those 7 **marked for drawing**? | **all 7**, `MarkedForDrawing = True` |
| 4 | is it the import or the session? | **the import** |

Measurement 1 counted for nothing, and understanding why is half the lesson: a
part built by hand has no sketch dimensions, while the library's own operations
dimension **and mark** them. Two different objects, and the difference was the
thing under examination.

Measurement 3 is what moved the diagnosis. `quote_dal_modello` imports only
marked dimensions, so "the part has 7 and none arrive" left two **opposite**
causes open — none are marked (the fault is upstream, in whoever should mark
them) or they are marked and the import will not take them. Two repairs, two
files. The data existed and the message did not carry it, so a probe was needed
to split what the answer could have split. Fixed: the refusal now writes
`[marcata]` / `[NON marcata]` beside every name and the count of marked ones up
front.

Measurement 4 is a positive control in the same run, on the same drawing. Five
import routes — `AllViews=true` with nothing selected (what the library does),
the view selected by name, the view activated, the view selected with
`AllViews=true`, and plain `InsertModelDimensions(0)` — all zero. And then, on
that same drawing in the same instant, `InsertNote` returns an object and the
annotation count goes 67 → 68. **It is not the session and not the document: it
is the dimension import, and only that.**

Two of the first four attempts had not actually been attempts: they did
`((IEntity)view).Select4(...)` and took `E_NOINTERFACE`, because a drawing view
is not an `IEntity` — it is selected by name with type `"DRAWINGVIEW"`. Without
reading the exception they would have gone into the list as "tried, doesn't
work". A list of routes tried is persuasive in proportion to its length, which
is exactly why each line has to be checked for having arrived where it says.

One hypothesis survives all four: the import works **in process**, inside an
add-in. That is now the only road to a dimensioned drawing.

### A contract check that admits what it is not

While the import is broken, that refusal message is everything a model gets when
it asks for dimensions — and it is the part that works. A new contract check
requires it to still name how many dimensions are marked and the list of routes
tried. It reads the **source**, not the run, and says so: the behavioural test
would be a bench case with `deve_fallire`, and it cannot be written today,
because the bench requires a **green twin** with the same operation and
`quote_dal_modello` never succeeds. The rule is right — a test that only checks
the refusal cannot tell "the guard works" from "nothing works" — and the check
does not pretend to be that test.

The contract suite goes from 145 to 147 checks.

## 0.65.0 — 18 September 2026 · library 0.98.0

### The title block says what it wants, and a door that was locked

Sixty-seven operations. `cartiglio` (GT3) and `verifica_interferenze` (GA10).

**A title block holds no text: it holds links.** `cartiglio` reads which custom
properties this drawing's title block actually references, with their exact
spelling, because they cannot be guessed. Measured on this workshop's own
template: **39 notes, 26 properties referenced, 19 of them empty.**

Three things nobody would have guessed, and each one is a defect that gives no
error:

- They are almost all `$PRPSHEET`, not `$PRP` — they are read **from the part
  shown in a view**, not from the drawing. Write them on the drawing and every
  box stays blank, and neither side complains. The answer marks those names
  "(from the part, not the drawing)" rather than just listing them.
- **The spelling is mixed inside one title block**: `TRATTAMENTO` uppercase
  beside `Descrizione` capitalised, `Peso` and `MASSA` in the same note. There is
  no rule to derive — there is what someone typed. That is exactly why the list
  is **asked for** instead of deduced.
- `ISheet.get_TitleBlock()` returns **null** here and the title block still
  exists, made of sheet-format notes. An operation that tried only the first road
  would answer "no title block" about a drawing that has one — wrong, and
  confident. `cartiglio` tries both and declares which one answered.

The bench case closes the circle across two documents: it writes
`Descrizione = PROVA123` **on the part**, creates the drawing, and requires
`Descrizione` in `chieste_e_mostrate`. That field was added on purpose: with only
the list of empty ones, the case could have asserted at most "it is not among the
broken". **Not being among the broken is not working.** The red lives in the same
run — `Materiale` is asked for and was never written, so it must appear among the
empty ones; if that list were empty, the positive assertion would prove nothing.

**`verifica_interferenze` gives a volume, not a count.** 19200 mm³ predicted
before measuring and found to ±1 — a tolerance kept tight on purpose, because a
tolerance chosen for peace of mind stops measuring. Touching faces count as
interference or not depending on the job, so it is an argument, it defaults to
`false`, **and the answer states which way it was used**. The detection manager
is switched off in a `finally`, always: the exception arrives precisely when the
analysis goes wrong.

### The bridge was refusing the assemblies its own catalogue promised

`ControlliPreliminari` runs before the first step of every plan and admitted only
parts and drawings. Meanwhile the catalogue served to clients listed
`inserisci_componente`, `accoppia`, `sostituisci_componente` — and
`verifica_interferenze` needs an assembly too.

**None of the bench's 781 checks could see it: the bench calls the library, not
the bridge.** Two true measurements of two different things, and the large number
looks like it covers both.

The comment defending the old choice read *"no operation in the library knows how
to work on an assembly"*. True until 16 September; four of them existed by the
17th. **A comment that explains a choice is also what prevents it being
revisited** — whoever reads it finds a reason, and finding a reason feels like
being done looking.

So the defence is no longer a sentence. `OgniFamigliaDelCatalogoHaUnDocumentoAmmesso`
compares the catalogue's families against the document types the executor admits,
runs with no SOLIDWORKS and no licence, and **was born red on this defect**. It
reads the admitted types **from the source**, fails loudly if the line it parses
disappears, and treats an unclassified new family as **red, not green**.

Fixed with a third switch, `AncheSuAssiemi`, on in the bridge and off in the
add-in — same shape as `DocumentoSenzaFile` and `AncheSuTavole`, and watched by
the same script. The refusal message is now built from what is actually admitted
instead of naming yesterday's world.

### Also

- Section Z's 65 entries are renamed `Z-A1`, `Z-C3`…: **37 of their names were
  already in use** by other entries in the same TODO, 33 of them closed. "Do C3"
  led to a green entry from 14 September about something else. A new contract
  check refuses two different entries sharing a name — the exception being an
  entry's own "original trace, kept for reference", where the repeated name says
  something. It was watched going red on a duplicate put there on purpose.
- The refusal for a wrong document type is now **built from what is actually
  admitted** instead of a fixed sentence. The old one named parts and drawings
  even once assemblies were allowed: a message listing yesterday's world sends
  the reader looking for the problem where it is not.
- The contract suite goes from 139 to 145 checks.

## 0.64.0 — 17 September 2026 · library 0.97.0

### Two sheets, and the guard that could never fire

Sixty-five operations. `nuovo_foglio` and `attiva_foglio` — GT1, the first GT
entry that GT0 made testable.

`esporta_pdf` has always counted the pages of the PDF it writes: the guard exists
because a one-page PDF of a three-sheet drawing comes out with no error at all and
arrives in the workshop incomplete. That count was **always 1**, because there was
no way to make a second sheet.

> A guard that can never fire is not a guard: it is a line of code that consoles.

**The difficulty is not the sheet, it is the projection angle.** `NewSheet4` takes
`FirstAngle` as a mandatory sixth parameter — there is nothing to inherit.
Writing `false` because it is the default would give one drawing with **two
sheets mirrored differently**; neither gives an error, and whoever cuts the part
on sheet 2 cuts it reversed.

So the angle is read from the current sheet, passed through, **and then read back
from the new sheet**: passing it is not having it. If the two disagree, the
operation refuses rather than reporting done. Format and scale follow the same
rule — two sheets at different scales can be wanted, but not by accident.

`ActivateSheet` takes **the name**, not an index; an index would be the perfect
opportunity to work on the wrong sheet with no error. And after activating,
`attiva_foglio` asks the drawing which sheet it now has in front, because the
answer to the call is not the state of the document.

**What the bench case had to teach me.** I wanted both sheets' angles asserted in
one instant. That is not possible, and the reason is good: the state writes
`diedro` **only for the active sheet**, because reading a non-active sheet's
properties would mean switching sheets — a read that modifies. So the case reads
one sheet at a time, and the comparison between the two angles closes on the last
step. It is more honest than what I had planned.

---

## 0.63.0 — 17 September 2026 · library 0.96.0

### An exploded view that explodes nothing, and the millimetre next to the boolean

GA8 is blocked, and the block is worth more than the entry was.

What works: `CreateExplodedView()` returns True and creates the view,
`ShowExploded2(show, realName)` shows and hides it, `IsExploded(out name)`
follows, and the steps read back. What does not: the step that appears on its own
has **distance 0 mm**, and the real displacement — `GetTotalTransform(true)`
minus `GetTotalTransform(false)` — is **[0, 0, 0] mm**. Nothing separates.
`AddExplodeStep2` answers `NoComponents` with a component selected, in five
different ways of selecting it, and `AutoExplode()` answers True and adds
nothing.

**I had excluded `CreateExplodedView()` for a reason that sounded solid**: zero
arguments, and an exploded view has distances and components, so it must be the
button. The hole in it: an **empty** exploded view has nothing to configure. The
steps come afterwards.

> The good criterion is not "zero arguments": it is **whether it answers**.
> `EditExplodeParameters` and `ViewExplodeAssembly` return `Void` — those stay
> buttons. `CreateExplodedView` and `AutoExplode` return `Boolean`.

And the API said what was missing, because the `out` was read:
`AddExplodeStep2` without a view answers `Error = 2 =
swCreateExplodeStepError_NoExplodeView`. Not a silent `False` — the exact name of
the thing that was absent.

**Four probes, and the first three were the same probe.** They all worked on two
plates inserted and **never mated** — the very thing the
`inserire_non_e_accoppiare` case exists to shout about. `AutoExplode` explodes
along mates. The fourth mated first, and the result did not change.

> Three attempts that share the same defect are one attempt made three times, and
> the only way to notice is to write down what they have in common, not what they
> have that differs.

**Shipped anyway, and it earns its place on its own.** The assembly state now
carries `esploso`, `vista_esplosa`, `viste_esplose`, `passi_di_esploso` and
`esploso_sposta_al_massimo_mm`. `GetTotalTransform(true)` includes the explode
transform, so on an assembly shown exploded `origine_mm` would be the position
*in the exploded view* — plausible and wrong by ten centimetres, with no error.
The `inserire_non_e_accoppiare` case reads `origine_mm`, and now also requires
`stato.esploso = false`: **it is that false which makes the other reading
trustworthy.**

And `esploso: true` alone would have been a lie, because here it is true while
nothing moves. Hence the millimetre beside it: *a boolean that can be true while
the thing has not happened is not a measurement, it is a promise.*

---

## 0.62.0 — 17 September 2026 · library 0.95.0

### The bench could not turn the page, and that is why ten entries were stuck

Seven drawing operations have been in the catalogue for months, all marked
`Provata = true`. Declared bench cases for those seven: **zero.** Ten GT entries
open, all with the same shape — *write it, then prove it* — and the second half
never arrived. For a year the implicit explanation was "we haven't got there
yet".

The real explanation is one line. The bench ran every step of a case on the
**same** `IModelDoc2`, and `crea_disegno` opens a second document. The next step
got the part back, and refused with *"the active document is not a drawing"* —
the right message for a problem the person writing the case could not solve. So
nobody wrote the case.

> **An operation with no case is not proven: it is declared.**

It is exactly the shape of GA0: no assembly operation could have a case until the
bench learned to build an assembly. That became the entry *before* all the
others. Here the entry did not exist at all — it does now, GT0.

**The fix is not `IActiveDoc2`.** On this machine SOLIDWORKS is shared: "active"
is whatever *anyone* has in front of them, and one window is enough for the bench
to measure somebody else's document. The bench follows what the operation
**declares** — `Esito.DocumentoNuovo`, which `crea_disegno` has been filling for
months and which the bench was throwing away.

> **A declared change can be followed; a deduced one can only be suffered.**

Three details that separate "follows" from "follows well": it switches **only if
the step succeeded** (a half-failed `crea_disegno` can leave a wreck on screen);
it **says so in the diary** (a silent document switch makes a red three steps
later unreadable); and it tolerates the field being absent, because the bench
loads the library by reflection and an older one does not have it.

### `croci_di_centro`: written, measured, and **not shipped**

Still sixty-three operations. A drawing with three holes and no center marks
prints, looks complete, and in the workshop the one thing needed to drill is
missing: where the centre is.

`AutoInsertCenterMarks2` **returns `True` and inserts nothing.** Not an error —
a yes that does nothing, which is exactly the kind of thing this project exists
to catch, and this time the API is the one doing it. Three probes:

- **25 combinations** of `InsertType` × `InsertOption`, plus the version without
  the `2`: `True` every time, center marks 0 → 0, centerlines 0 → 0;
- the plate really has the holes (**3 cylindrical faces**) and the view really
  shows them (**3 circular edges**, counted with `GetVisibleEntityCount2`); every
  view, activated and selected: still 0 → 0;
- detailing preferences read, then switched on by hand
  (`swDetailingAutoInsertCenterMarks` was **off**, `…ForHoles` on, size 2.5 mm),
  `UseDocumentDefaults: false` with an explicit size, and `InsertCenterMark2` /
  `InsertCenterMark3` on each selected circular edge: **0 marks placed.**

The first probe had two defects of mine — it tried one view (the one without
circles) and never checked the holes existed. The second removed both. *A probe
that does not measure the thing is not evidence of the thing*, even when the
result later holds up.

So the operation is marked **`Provata = false`**: it does not reach the
catalogue and is offered to nobody. *An operation that has not been built and
measured is not exposed* — and that matters most here, where it "succeeds":
whoever called it would believe they had the centres. The code stays, because it
reports `croci_aggiunte = 0` rather than declaring done, and because the day the
missing piece is found there is nothing left to write.

**What was not ruled out:** that it depends on the workshop's drawing template.
A virgin template was not tried — ten minutes, and it separates "the API does not
work" from "it does not work with this template". Written next to the entry.

**What is gained anyway:** the drawing state now carries `croci_di_centro` per
view and a total, and the bench case asserts it is **zero**. The hole is now
visible — a drawing that comes out with no centres says so, and before, nobody
said it.

> The prediction in the case allowed 9 or 3. The measurement was **0**, which was
> neither. *When the measurement is none of the answers you predicted, the
> question was usually about something else.*

### Four numbers out of twenty were wrong, and nobody could have noticed

The bench README has a table — folder, how many cases, what they prove.
`lavorazioni` said 2 and had 7. `ripeti` said 3 and had 6. Four of twenty.

A number in a document does not fail; it ages. Same defect the README's operation
count had ("thirty-eight" when there were sixty), fixed the same way: not by
re-counting once by hand — *a count made by hand ages while you write it* — but by
having it re-counted every run. A contract test now sums the declared numbers per
folder and compares them with the files on disk.

Per folder, and summed, because one folder is now listed twice with two different
numbers: `casi/tavola/` holds both the hand-run plans and the declared cases.
7 + 1 = 8. Demanding that each line match alone would turn a correct table red.

### And a census that read fine and could not be called

`SolidWorks.Interop.gtswutilities.dll` ships in the redist. It loads, its
twenty-two interfaces enumerate, every signature of `IThicknessAnalysis` and
`ICompareGeometry` reads cleanly — and **none of it can be called here.** The
three coclasses are not registered in `CLSID` (GUIDs taken from the interop
itself, both registry views), no `gtcocsw*` binary exists on disk, and the add-in
is not among the registered ones. SOLIDWORKS Utilities is simply not installed.

> **That an interface can be READ does not prove it can be CALLED.**

Written down rather than worked around: the question "how thin is this shell at
its worst point" still has no answer in this catalogue, and the stated purpose of
the project is a watertight box for a boat.

---

## 0.61.0 — 17 September 2026 · library 0.94.1

### The catalogue as a Resource, and one name for where SOLIDWORKS lives

Three findings from one afternoon, and they are the same thing seen from three
sides: **two names for one thing become two things.**

**`SW_INSTALL_PATH` and `SW_REDIST` meant two different things** (W2.12 / H10).
The root of the installation and its `api\redist` subfolder. Neither was wrong.
What it did: whoever set the first one to build the library saw the bench fail to
find the interop, and nothing named the second variable until after the failure.

The root wins — `api\redist` follows from it, not the other way round; going up
from a subfolder means guessing a convention, and a guessed convention is where
plausible-and-wrong paths come from. `SW_REDIST` is still read, and still
second: *a variable that stops working without saying so is worse than one that
was never there.* Same treatment `OPENPDM_ADDIN` got. And the last `OPENPDM_*`
left, `OPENPDM_CATALOGO`, is now `RISCONTRO_CATALOGO` — old name still read.

**Three of the environment variables the bridge reads were not in the README**,
found while reading it for another reason. Two of the three were
`RISCONTRO_RADICE` and `RISCONTRO_SOLA_LETTURA` — the only two configurable
defences the bridge has, the ones that say where it must *not* write. The code
was there and worked.

> **A defence that is in the code and not in the README is a defence nobody turns
> on.** Whoever installs reads the table, not the sources.

They were invisible to a grep too: they go through a `Variabile(...)` helper, not
`GetEnvironmentVariable`. *Looking for one form means finding one form.* There is
now a contract test that collects both forms across the bridge and the library
and requires every name to appear in the README — with a line that checks it
found something at all, because a test that finds nothing passes forever.

It found **nine**, not eight. Counting by hand had given eight; the ninth is
`SW_INSTALL_PATH` itself, born half an hour earlier. *A count made by hand ages
while you write it; the one that re-reads does not.*

**The catalogue is now also an MCP Resource** (W2.12), not only a tool. The
content is the same; who decides, and when, is not. A tool is called by the model
when it works out that it needs it; a resource is attached by the person using
the client, before the model says anything.

One resource, `catalogo://operazioni`, plus a URI template,
`catalogo://operazione/{nome}` — not one resource per operation. It is the same
two-level catalogue that governs the tools, said in the vocabulary of resources.
`subscribe` and `listChanged` are deliberately not declared: the catalogue is a
file read at startup. *A declared capability that is never exercised is a promise
someone will wait for.*

Two doors on one thing become two things the day one of them gets written
separately. So: `Risorse` knows nothing about the catalogue — it takes the same
`Catalogo` object the tools use and calls the same two methods, and a contract
test asks the resource and the tool and compares the two texts byte for byte.

The library version moves for comments only. It is the one change that could not
alter behaviour — and *"it's only comments" is exactly what gets said right
before it wasn't.*

---

## 0.60.0 — 17 September 2026 · library 0.94.0

### `sostituisci_componente` — the mates survive, and they are broken

"The lid has to be remade" is the commonest gesture in design, and GA7 named the
danger it carries: *if the API replaces the document but does not keep the
mates, you get the typical defect again — different part, no error.*

**The signature existed. It was on the other interface.** GA7 sat at *"not
estimable"* because the API census had looked for `ReplaceComponent` among the
170 methods of `IComponent2` and concluded *"no dedicated signature is listed"*.
It is on `IAssemblyDoc`: `ReplaceComponents2(FileName, ConfigName,
ReplaceAllInstance, UseConfigChoice, ReAttachMates)`. Third time in this project
that *a name tells you what a method is called, not which object owns it* — and
the expensive step is not failing to find it, it is turning "I did not find it"
into "it does not exist".

**What the bench measured, and it is not what I predicted.** Two components, one
coincident mate, lid replaced with a 6 mm one:

| | before | after |
|---|---|---|
| components | 2 | 2 |
| mates (endpoint count) | 2 | 2 |
| mates lost | — | **0** |
| `GetWhatsWrongCount()` | **0** | **2** |

The first three rows say "all good". The fourth says those mates are **in
error** — they pointed at faces of the old part. SOLIDWORKS keeps them in the
tree and marks them red.

> **A broken mate is worse than a missing one: missing is visible, broken looks
> like it holds.** Surviving is not working.

So the operation reports **two** before/after pairs, not one —
`accoppiamenti_prima/dopo` and `problemi_prima/dopo` — and its loudest branch is
the one where the mates survive and the problem count rises. It names the remedy
too: delete those mates and put them back with `accoppia`. *A warning with no
remedy is one people learn to ignore.*

**`ReAttachMates` is not a knob.** With `true`, and a real mate to reattach,
`ReplaceComponents2` raises `RPC_E_SERVERFAULT` — measured in four conditions:
assembly saved and not, new file open and closed. With `false` it always passes.
So `false` is hard-coded and what it costs is written down: *an argument that
changes nothing is worse than an argument that is not there, because whoever
sets it believes they decided something.*

**And I was wrong twice on the way here, which is why the bench case is named
`il_coperchio_rifatto_tiene_i_vincoli_ma_rotti` and carries its own history.**
The probe that found `RPC_E_SERVERFAULT` never counted mates — it printed
component paths — and from "the call succeeds with `false`" I deduced "the mates
are discarded". They are not. **A probe that does not measure the thing is not
evidence of the thing, not even when it succeeds.** Then, having measured that
the mates survive, I concluded they were fine. They are not.

Two more traps avoided in the same operation: `IComponent2` does not cast to
`IEntity` — it has its own three-parameter `Select4`, and the third parameter is
`ShowPopup`, which must be `false`; and `swReplaceComponentsConfiguration_e` has
a `ManuallySelect` value that opens a modal dialog. **Three calls in one
operation that know how to open a window**, and from here nobody can press OK.

Sixty-three operations. README updated to match — the count is checked by a test,
not by hand.

---

## 0.59.0 — 17 September 2026 · library 0.93.0

### `accoppia` — and the unknown that was blocking it did not exist

GA4 was marked blocking and blocked, on this: *"which selection marks are needed,
reflection does not say"*, with an 8-10 hour estimate hanging off it.

**No marks are needed.** Select the two entities with
`IEntity.Select4(append: true, null)` and call `AddMate5`: it answers
`swAddMateError_NoError`. That was measured inside a probe written for an
entirely different question — *probes are worth reading whole, not only for the
line you went looking for.*

Sixty-two operations now. Coincident, concentric, parallel, perpendicular,
distance, tangent; aligned or anti-aligned; `distanza_mm` required for distance,
because there is no reasonable default for how far apart two faces are.

**How the faces are named, which was the real question.** The state gives
`C1..Cn` for components and **no identifiers for their faces**. Making some would
mean numbering hundreds of faces, and they would be **positional** — to be
re-read after every change. So faces are named by **direction**, reusing the
vocabulary this project already has for parts: *"the top face of C1 on the bottom
face of C2"*. It does not cover everything — an oblique face has no direction —
and it **says so** instead of guessing. The directions are the **assembly's**,
not the part's: a rotated component's "top" face is the one facing up *in the
assembly*.

**`C1` means one thing.** The ordering that assigns it now lives in one place,
`StatoDocumento.ComponentiInOrdine`, used by the state **and** by `accoppia`. Two
copies of that rule and one of them drifting would make `C1` mean two different
things — and mate the wrong component with nothing raising an error.

**The measurement is not the return value.** `AddMate5` returns a `Mate2` *and*
writes an `ErrorStatus`, and neither says the mate exists. The mate counts on
both components are compared before and after, re-reading the handles by name
after the rebuild.

### The entry's own success criterion had to be rewritten

GA4 asked for: *"an insufficient mate must leave `GetRemainingDOFs > 0` on that
axis"*. **That is not reachable** — `GetRemainingDOFs` answers *unavailable* in
every condition tried.

So the case `un_accoppiamento_non_basta` does the nearest thing that is **true**:
it proves the mate exists (the count rises, `appoggiato` turns false) **and** that
the bridge says out loud it does not know how much freedom is left.

A coincident mate removes three degrees of freedom out of six: the lid can no
longer lift off, but it can still **slide in the plane and spin about the
normal**. If that sentence ever disappears from the answer, the bridge would be
letting a reader believe one mate is enough — and an assembly that looks
assembled without being assembled is the exact defect this project chases.

715 bench tests, 0 failed, 550 expectations verified. 128 contract checks.

---

## 0.58.0 — 16 September 2026 · library 0.92.0

### Assemblies: the bench can build one, the state can read one, and one operation can touch one

Zero of sixty operations worked on assemblies. Three things landed together,
because each is useless without the others.

**GA0 — the bench can prepare an assembly.** Until today `NuovaParte()` was the
only way the bench produced a document, and it used the part template. That meant
**no assembly operation could ever have a test case**: all nine assembly entries
could have been written, made to work by hand, and left with nothing to notice
when they broke. *An operation without a case is not an operation, it is a
demonstration.*

A case can now declare `"assieme": { "componenti": [...] }`. Each component is
built with **the same operations the cases use** — no separate little language to
keep in sync — then saved into `Banco\lavoro`, reopened, and inserted.
`AddComponent5` takes a path, so a component has to exist on disk first; that is
seven steps, and each one fails its own way. All of them report through
`ErroreDiBanco`, never `Segna(false)`: a failed *preparation* is not a failed
*test*, and calling it one would suggest something had been measured.

A component marked `"da_inserire": false` is built and opened but **not**
inserted — that is how a case can test the insertion itself instead of measuring
the preparation's work.

Three self-checks guard it, and the third is the one nobody writes: after the
case, `lavoro` must be empty again. *A case that passes because a file from the
previous run was still there is not a case, it is a memory.*

**GA1 — `guarda_il_pezzo` describes assemblies.** It used to answer, for any
assembly, one line: *"documento di assieme: l'assistente non lo modifica
ancora"*. True, and blinding: the state is the only thing the model sees, so it
could not know how many components there were, what they were called, which were
suppressed. It could not even refuse with any understanding of what it was
looking at.

Now: component list (uid, name, path, configuration, suppression state, fixed),
counts of components/suppressed/lightweight/fixed, and the active configuration.

**The component uids are stable, and not by accident.** Face and edge uids are
*positional* — they renumber by area on every read, and that is the most
expensive trap in this project. Components are different: `Name2` is unique
within an assembly, so the list is **sorted by name** and `C1..Cn` stays put
between reads. Trusting `GetComponents`' own ordering, which is documented
nowhere, would have repeated the face trap where it was avoidable.

`GetComponents(false)`, not `true`: with `true` a subassembly shows up as *one*
component with nothing inside, and the model would read "2 components" on an
assembly of thirty — every conclusion after that plausible and wrong. The
top-level count is reported separately, because that is the other real question.

The note did not disappear, it changed kind: `cosa_non_so_ancora_fare` now says
**what** is missing. A model reading "I can see four components but I cannot mate
them" knows what it may ask; one reading "I do not modify this" can only stop.

**GA3 — `inserisci_componente`.** Sixty-one operations now. The thing it does
beyond inserting is the point, and the API census wrote it down long before
anyone measured it: *"AddComponent5 places without mating — four loose bodies
that look like an assembly."* Inserting almost always succeeds. The part appears,
the tree fills, the rebuild is clean, and **nothing is constrained.**

So the operation reports whether the component is `fisso`, how many
`accoppiamenti` it has, and — the conclusion — whether it is `appoggiato`: not
fixed and with zero mates means it is just sitting there. The warning travels in
the answer, not only in the catalogue description, because whoever reads the
answer may never have read the description.

**Fixed components are excluded from the "is it assembled?" count** (GA5), and
that is not an edge case: **SOLIDWORKS® fixes the first component inserted into
an assembly** — measured, `base-1 fixed=True` against `coperchio-1 fixed=False`.
Every assembly that exists has at least one nailed-down part, so counting them as
constrained would have been wrong *always*, not occasionally, and in the
comfortable direction. A fixed part does not move because someone nailed it, not
because anyone mated it: two different things, and the state keeps them apart.

**What it does NOT report, and says so.** How *much* freedom is left. The first
version reported `gradi_di_liberta_residui` from
`IComponent2.GetRemainingDOFs`, believing the return value was a count and that
a freshly inserted component would give six. The bench case caught it on its
first run: it measured **2**.

**That return value is not a count. It is a status** — `swRemainingDofs_e`:
`Restricted=0`, `Unrestricted=1`, **`Unavailable=2`**, `Failed=3`,
`RootComponent=4`. The 2 meant *unavailable*. Six was never a possible return.
Meanwhile the bridge was telling the model *"it still has 2 degrees of freedom
out of 6"* — invented number, invented unit, entirely plausible sentence.

What actually exposed it was not the wrong value but the **comparison**: the same
2 comes back for a **fixed** component and for a **free** one. The number did not
distinguish the two things it was there to distinguish.

Four conditions were tried and all return *unavailable*: freshly built assembly,
component selected first, assembly saved to disk, and **after a real coincident
mate** — `AddMate5` answered `swAddMateError_NoError`, so that last condition
genuinely holds. (A trap in passing: in `swAddMateError_e`, **`NoError` is 1**
and `ErrorUknown` is 0. `if (err != 0)` would have called a successful mate a
failure.)

So the count is not obtainable here, and rather than wait for it the bridge
stopped publishing a false one. `IsFixed()` works and genuinely separates the
nailed-down first component from the rest; `GetMates()` counts. The state also
carries `cosa_non_so_dei_vincoli`, which warns that "constrained" here means
*"has at least one mate"*, not *"cannot move"* — one mate still leaves five
degrees of freedom out of six.

*A measurement you cannot have must be declared absent, not approximated. Saying
"I don't know" costs one line; a plausible number costs whatever someone builds
on top of it.*

It refuses before it acts, twice: not an assembly (says which type it found), and
component not open (says which documents *are* open — `AddComponent5` answers
`null` on a closed file without saying why, and a refusal that does not name the
alternative forces guessing).

And it does not believe the return value: it counts components before and after.

**Where the component actually ends up, and a third trap.** The position is read
from `GetTotalTransform`, and the index layout was not assumed: the case inserts
at z = 30 mm and reads the translation back. It found more than it was looking
for. The **Z matched** — so indices 9, 10, 11 are the translation, question
answered — but Y came back **-2**:

> **`AddComponent5(x, y, z)` positions the component's bounding-box CENTRE, not
> its origin.**

A part extruded in one direction — which is what `estrudi_rettangolo` does, and
what any ordinary extrusion does — has its origin on a face, not at mid
thickness. Ask for the centre at z = 30 and the origin lands at y = -2: half the
4 mm thickness. **Confirmed by prediction, not by fitting**: a second part 10 mm
thick, requested at zero, was predicted to read (0, -5, 0) *before* the probe ran,
and read (0, -5, 0).

The fix is in the **name**, not the code. The field was `posizione_mm`, which
invited comparison with the point that was *asked for*, and the two almost never
coincide. It is now **`origine_mm`**, and `x_mm`/`y_mm`/`z_mm` say "bounding-box
centre" in the catalogue. Neither number was wrong; they were two different
things and nothing said so — the defect that raises no error in its subtlest
form: not a false value, but **a name promising a meaning the value does not
carry**.

**This has now run against SOLIDWORKS®, and the first run was worth it.** The
assembly case found three things in one go: a status read as a count, an empty
collection returned as null, and a field name that promised the wrong meaning.
All three were written by the same person who wrote the case — which is the
argument for writing the case.

### A source file that isn't in its `.csproj` doesn't exist

`OperazioniAssieme.cs` was written, compiled fine in a scratch check, and failed
the real build: it was not listed in `Riscontro.Disegno.csproj`. Two errors,
thirty seconds to fix — because something referenced it.

**The dangerous case is the other one.** A new class nobody references yet — an
operation written but not yet wired to the dispatcher — is simply **not
compiled**, and the build stays green. You write a file, everything passes, and
that file is not in the program. No error, anywhere. It is the defect that raises
no error, applied to the *project* instead of the part.

A contract test now walks `Riscontro.Disegno`, `Ponte`, `Strumenti`, `Mcp`,
`Banco`, `Prove` and `Installer` and checks every `.cs` appears in its project
file. Sixty-eight files, all present. Seen firing: a throwaway file dropped in
`Riscontro.Disegno` turns it red and names the file and the project.

It is a test and not a script on purpose. A script has to be remembered, and that
is exactly how the `pre-push` hook came about: the check existed, running it
didn't.

### The data comes out machine-readable too — and no `outputSchema`, deliberately

Everything came out as **text**: document state, plan measurements, the
catalogue. A language model reads that fine; a program has to re-parse JSON that
was JSON before being put inside a string. `structuredContent` is the field MCP
provides for exactly this, and it costs one line. The text stays — clients that
read only `content` are the majority — and the data comes out **even when the
plan failed**, which is when a program most needs it.

**`outputSchema` is not declared, and that is the decision, not an omission.**
The spec is firm: a tool declaring `outputSchema` **must** return conforming
`structuredContent`. The shape of the state changes with the document — an
assembly has `componenti`, a drawing has `fogli`, a part has `facce` — and every
new field (eight were added to assemblies today) would become a promise to keep.
**A partial schema is worse than none**: a validating client would reject correct
answers. So: the data without a promise about its shape, and a contract test that
fails if any tool ever declares an `outputSchema` before that shape is actually
settled.

### The same plan twice

A client that never receives the answer — network, timeout, restart — resends the
request. That is what every client does, and this bridge's own annotations
already said `disegna` is **not** idempotent: re-running does not restore the
part, it **makes a second hole**. They said it, and nothing stopped it.

The bridge now remembers the fingerprint of the last successful plan, which
document it ran on, and how the part looked **right after**. It refuses only when
all three match.

**Both conditions are needed.** The plan alone is not enough: identical plans on
documents in different states do different things and may both be wanted. The
state alone is not enough either: a document that has not changed is no reason to
refuse. Together they describe exactly one situation — the resend.

**No flag to bypass it.** *An argument that disables a defence is an invitation to
disable it*, and whoever hits it has every incentive to retry with it on — so the
defence disappears precisely when it was needed. The refusal says what to do
instead: if the answer never arrived, call `guarda_il_pezzo` (the current state
**is** the result); if you really want it again, change something or use
`annulla`, so it is a choice.

**The state summary is deliberately not a hash of everything.** The full state
changes even when the part does not — a uid renumbering, a face crossing the
display cap — and comparing all of it would report "different" every time,
**switching the check off without anyone noticing**. Volume, bodies, feature
count and title change when the *part* changes, not when the way it was read
changes. *The most common way to disable a check is to make it too sensitive.*

**And a bug found while writing it:** `misura` runs through the same body. But
measuring twice is legitimate — it is the normal case — and since a measurement
does not change the document, the state after equals the state before, so the
guard would have **refused the second measurement**. Worse than inconvenient:
`misura` declares `idempotentHint: true`, and a client reads that line to decide
whether it may retry on its own. Refusing would have made that declaration **a
lie in the protocol** — the very category of defect `misura` exists to avoid. The
guard now applies to `disegna` only.

### The bridge tells the client what it is doing

`stderr` stays — it is the only place that works **before** a client exists
(attaching to SOLIDWORKS®, a catalogue that will not load, `--prova`), and it is
what you read when nothing starts. What is added is `notifications/message`, with
the `logging` capability declared and `logging/setLevel` served.

What this gains: the agent's own lines — *"I re-anchored face F3, moved 2 mm"* —
are exactly what explains why a part came out the way it did, and they were going
to a terminal nobody watches.

The channel only opens once the message loop is running; before that, a line on
stdout with nobody reading JSON is just noise that breaks whoever connects next.
And sending the notification is wrapped in a `try` that never rethrows: *a report
that makes the reported thing fail is worse than silence.*

**An unknown level is an error, not a shrug.** Accepting and ignoring it would
leave the client believing it had raised the threshold while messages keep
arriving — or, worse, believing it hears everything while hearing nothing.

One existing test was made **robust, not loosened**: "a notification gets no
reply" read whatever line came first; it now skips **only**
`notifications/message`, which is ours and recognisable. Widening it to "ignore
anything without an id" would have switched it off.

### One bridge at a time on a document

Two MCP clients open at once is ordinary, and until now nothing stopped two
bridge processes from executing plans on the same document. The precedent is
already paid for: on 14 September a 314×274×52 block was extruded **into someone
else's part** — no error, no warning, the plan succeeded on the wrong thing. That
was fixed by holding the `IModelDoc2` reference instead of asking for the active
document. Within one process that is enough. Between two processes it protects
nothing: two different COM objects looking at the same file, each believing it is
alone.

A system mutex, keyed by the document, now decides whose turn it is. The refusal
says which document and why.

**A mutex, not a lock file**: a file outlives whoever made it, so a bridge that
dies leaves the document unusable until someone deletes it by hand. Windows
releases a mutex however the process ends. *A lock that stays shut after the one
who shut it has died is not a protection, it is a fault.* `AbandonedMutexException`
is not an error either — it is Windows saying the previous holder did not close
the door, and the turn is ours.

**Zero wait, deliberately.** Waiting would hang the client with no explanation,
and the bridge already has a problem with calls that never return. An immediate
refusal that explains lets the reader decide whether to wait.

**The key is a SHA-1, not the path.** Mutex names reject backslashes and have a
length limit, and a truncated path would make two documents in the same deep
folder collide — two different parts blocking each other, a defect that only
shows up once someone is actually working. Seven contract tests cover the keying,
including that case; that two processes exclude each other is Windows' job, not
something to test here.

**The limit, written down rather than discovered**: the name lives in `Local\`,
the current Windows session. It covers the real case — two MCP clients on one
desktop — and not two different users or a service. `Global\` would, but it needs
privileges a bridge launched by an MCP client does not have, and a lock that
sometimes cannot be taken is worse than one whose reach is known.

---

## 0.57.0 — 16 September 2026 · library 0.91.0

*First entry in English, per the decision recorded at the top of this file.*

### `misura` — a sixth tool, so the client stops asking for confirmation to read

MCP annotations travel on `tools/list`: they sit on the **tool**, and they are
sent before any call exists. With only `disegna`, a client read
`destructiveHint: true` and asked for confirmation even for
`proprieta_di_massa`, which does not move a single cubic millimetre. **Someone
who confirms ten times in a row stops reading what they are confirming**, and a
confirmation that has become a click no longer protects anything.

This could not be fixed inside `disegna`: at annotation time nobody knows which
of the sixty operations will arrive. So `misura` takes the same plan shape and
accepts **only** the `verifiche` family — the six operations that read and
nothing else — and declares `readOnlyHint: true`, `destructiveHint: false`,
`idempotentHint: true`. Idempotent for real, not out of politeness: measuring
twice gives the same answer twice because nothing changed in between.

**The gate runs before the plan starts, not during it.** A plan of three
measurements plus one extrusion, stopped at the fourth step, would already have
measured three times: whoever read the answer would see three good measurements
and an error, with no way to know the part survived only by luck. Every step is
checked first; if one is not a `verifiche` operation, nothing runs at all.

**`identita` is deliberately not read-only.** `scrivi_proprieta`,
`crea_configurazione` and `attiva_configurazione` write to the document.
Declaring them harmless is the direction in which being wrong costs most.

Four contract tests guard this, including the red one: `misura` must **refuse**
`estrudi_rettangolo`, and the refusal must name the tool that does do it. A tool
that declares itself read-only and then accepts an extrusion is not a
convenience — it is a lie in the protocol, and the client has already decided
not to ask for confirmation on the strength of that declaration.

### `title` where new clients look, and an error that says what does exist

`title` sat only inside `annotations`, where older revisions of the spec put it.
It is now **also** on the tool itself, where a newer client looks — written in
both places, because choosing one would mean half the clients showing
`guarda_il_pezzo` to a person instead of "Guarda il pezzo aperto".

The unknown-tool error now lists the tools that exist, and says the thing the
model actually gets wrong: **the sixty operations are not tools** — they go to
`disegna` (or `misura`). Before, it said only "I don't know x", and a model that
had guessed a name could only guess again.

The names in that message are written by hand — rebuilding sixty JSON schemas to
print six words makes no sense — so they can drift. A contract test asks for a
tool that does not exist and checks the message names **every** tool
`tools/list` just declared. *An incomplete list is worse than no list.*

### SOLIDWORKS® going away mid-plan now says so

`Aggancia()` records the PID (`ISldWorks.GetProcessID`, verified by reflection
against this installation's interop: zero parameters, returns `Int32`), and it is
checked before a plan and again when one stops.

**Not `Process.GetProcessesByName("SLDWORKS")`**: more than one session runs on
this machine, and searching by name finds *a* process, not *the* process this
bridge is talking to. A check that watches the wrong process answers "alive"
while its own is dead — the defect that raises no error, inside a check written
to catch it.

**The half that matters is the one after, not before.** Before catches "it was
already closed". After catches **closed mid-plan**, where the error that surfaces
is whatever the first call to find a dead reference said — a null reference, an
HRESULT — sending the reader to look for a fault inside the operation that was
running. The message now says SOLIDWORKS® closed, after how many steps, that the
document was not saved, **and keeps the original message**: if the two ever
disagree — SOLIDWORKS alive and a real error — discarding the original would hide
the thing to fix.

If the PID cannot be read, the check stays quiet and the bridge works as before.
A new defence that becomes a new cause of refusal is a regression, not a defence.

### `pre-push` hook: the publishability check now runs when it matters

`controlla-pubblicabilita.ps1` already existed and was already correct. On
14 September it reported "clean" on the working tree one minute before the first
push, while the **history** still carried a network path in 155 places across 41
commits. The control did not fail — **nobody ran it**.

Now git runs it. `Controlli/ganci/pre-push` blocks the push on a non-zero exit;
`Controlli\installa-ganci.ps1` points `core.hooksPath` at the versioned folder
(once per clone), so a hook fixed tomorrow applies to everyone immediately
instead of rotting as a copy inside each clone. Override with
`git push --no-verify` — done by hand, and known to have been done.

Seen firing: a file carrying a private-network address, staged on purpose, makes
it exit 1; removed, it exits 0.

*(And writing this entry set it off again — the first draft quoted the address
itself. The check caught its own description. Left as a note rather than an
anecdote: **a document about what must not leak is a place where it leaks**, and
that is why `CHANGELOG.md` is exempt for drive letters only, never for
addresses.)*

**Two real false positives, fixed narrowly.** `CHANGELOG.md` and
`Prove/ProveCatalogo.cs` name `O:\` because their job is to explain this very
check. The exception is a **separate list** that applies only to the `O:`/`W:`
patterns and leaves usernames, session paths and private addresses forbidden on
those same files. Putting them in the general exemption list would have been the
hole that the script's own comments warn against.

### The README claimed thirty-eight operations. There are sixty.

Nothing could notice: documents do not compile and nobody rereads them until
they matter to someone who was not there. **This is the defect that raises no
error, applied to documentation** — the README looks right, reads well, and is
false, and its reader has no other source.

Fixed, and a contract test now reads the number back out of the README and
compares it against the catalogue. It only recognises two shapes
(`<number> operazioni`, `operazioni di disegno sono <number>`) so that "five,
not sixty" — which is about *tools* — does not set it off: a control that shouts
at a correct sentence is one people learn to ignore.

### `QUICKSTART.md`, `CONTRIBUTING.md`

A six-step guide for the person who has to *use* this, with **"what it does not
cover" as a list of names** — assemblies, sheet metal, surfaces, sweep along a
free path, loft between arbitrary profiles, DXF/DWG to 3D, fill and table
patterns — so the limit is found by reading rather than by drawing something and
getting a part that is quietly not what was asked for.

Writing it surfaced one defect: step 2 told the reader to verify
`Installa.exe.sha256`, **and nothing produced that file**. An instruction that
cannot be carried out is worse than no instruction, because whoever follows it
concludes they have verified. `build.ps1` now writes it, in `sha256sum` format.

`CONTRIBUTING.md` records the policy *before* the first pull request arrives:
issues open, pull requests not merged until a CLA exists, and no support
commitment. Written afterwards it would not be a policy — it would be a personal
answer to one person, and it would read like one.

---

## 0.56.0 — 16 settembre 2026 · libreria 0.91.0

### `profili_saldatura` — l'operazione che il catalogo chiedeva e che non c'era

Catalogo a **60 operazioni**. Trovata da un audit del TODO fatto con sette agenti
in parallelo, e verificata a mano prima di crederci.

`membro_strutturale` e `telaio_rettangolare` dicono, nella loro stessa
descrizione: *«PRIMA CHIAMA "profili_saldatura" e copia da lì "profilo_percorso"
e "configurazione" ALLA LETTERA»*.

**Quell'operazione non esisteva.** Il nome compariva quattro volte in
`catalogo.json` — tutte e quattro *dentro il testo* di chi la chiedeva — e zero
volte fra i nomi eseguibili. Gli strumenti MCP sono cinque e nessuno elenca i
profili. Quindi il modello leggeva un'istruzione che non portava da nessuna
parte, e l'unica cosa che poteva fare era **inventarsi** un percorso `.sldlfp` e
un nome di misura.

E inventarsi la misura non dà errore — lo dice il commento di
`OperazioniSaldatura`, scritto molto prima di questo audit: *«se la misura non
c'è, SOLIDWORKS NON fallisce: usa la configurazione attiva del profilo. Il pezzo
esce con una parete diversa da quella confermata, il rebuild è pulito, e nessuno
se ne accorge fino all'officina»*. Un controllo che lo prende **c'era già**, ed è
buono; ma può scattare solo *dopo* che qualcuno ha indovinato.

Ora l'operazione c'è: elenca i profili della libreria e, per ognuno, le misure
scritte esattamente come vanno copiate. Legge i `.sldlfp` con
`ISldWorks.GetConfigurationNames(percorso)`, **senza aprire documenti**. La
cartella si scavalca con `PROFILI_SALDATURA`, la stessa variabile che usa già il
banco.

Il caso di banco chiude il cerchio: legge la misura al primo passo e la ricopia
dentro `membro_strutturale` al secondo. Prova che i due pezzi del catalogo si
*parlano*, non che si citano.

---

## 0.55.0 — 16 settembre 2026 · libreria 0.90.0

### GP17 — `sweep_circolare` e `loft_fra_due_cerchi`, e il loft che non è un cono

Catalogo a **59 operazioni**. Le prime due che fanno una forma **non dritta**: un
bocchettone passacavo, un manico, un riduttore fra due sezioni.

**Il profilo è un numero, non uno schizzo.** `InsertProtrusionSwept4` ha
`CircularProfile` + `CircularProfileDiameter`: con quelli lo schizzo del profilo
non serve. E il percorso è **un elenco di punti**, disegnato dall'operazione in
uno schizzo 3D — non uno schizzo da nominare, perché nello stato del documento
gli schizzi non hanno un identificativo e nessuna operazione ne lascia in giro
uno riutilizzabile. Se il percorso dovesse essere «uno schizzo», l'operazione non
sarebbe chiamabile da nessuno. È la stessa ragione per cui
`piano_per_tre_punti` prende coordinate e non vertici.

**Le marche, misurate:**

| | 0 | 1 | 2 | 4 |
|---|---|---|---|---|
| sweep, percorso | ok | null | null | **ok** |
| loft, profili | ok | **ok** | — | null |

Si usa 4 per il percorso e 1 per i profili. Lo zero funziona in tutti e due
perché vuol dire «nessuna marca», ma appoggiarcisi renderebbe indistinguibili
due selezioni con ruoli diversi il giorno che se ne aggiunge una terza.

**Lo sweep dritto è esatto.** Ø20 su 100 mm di percorso: **31415,927 mm³** contro
π·10²·100 = 31415,9265. Non «vicino»: uguale alla settima cifra. L'operazione fa
il conto e si rifiuta se non torna. Su un percorso con gomiti il conto **non**
vale — nei gomiti il materiale si sovrappone — e allora il teorico si dichiara
come *limite superiore*, non come risposta.

**E un loft fra due cerchi non è un cono.** Ø40 e Ø20 a 50 mm:

    formula del tronco di cono   36.651,91 mm3
    loft misurato                36.650,61 mm3
    differenza                        1,30 mm3   (0,0035%)

La superficie di un loft è un'**approssimazione a spline** della conica. È poco
ed è sistematico, sempre in difetto: chi confronta un loft con la formula e non
lo sa insegue un difetto che non c'è. Il caso di banco costruisce **lo stesso
tronco di cono con `rivoluzione`** accanto al loft e pretende che la somma dei
due corpi sia 73302,53 — cioè 36650,61 + 36651,91. Se fosse la formula a
sbagliare, i due corpi peserebbero uguale e la somma sarebbe un altro numero. È
la differenza fra «i miei numeri non tornano» e «so quale dei due è approssimato».

---

## 0.54.0 — 16 settembre 2026 · libreria 0.89.0

### GP9 (in parte) — `piano_per_tre_punti`, e la terza riga che non è la terza colonna

Catalogo a **57 operazioni**. `crea_piano` sapeva fare solo il piano *sfalsato*
da uno dei tre standard: per un foro inclinato o una faccia obliqua non c'era
nessun piano su cui disegnare.

**Tre coordinate, non tre vertici.** `CreatePlaneThru3Points3` vuole tre vertici
selezionati; `CreatePlaneFixed2` prende tre terne di numeri. Si usa la seconda, e
non per gusto: **lo stato del documento non elenca i vertici** — facce e spigoli
hanno un identificativo, i vertici no — quindi «tre vertici selezionati» è una
cosa che il modello linguistico non ha modo di chiedere.

**La normale è la terza RIGA di `Transform.ArrayData`, non la terza colonna.**
Misurato su tre piani di normale nota:

| punti | normale attesa | terza riga |
|---|---|---|
| (0,0,20) (100,0,20) (0,100,20) | (0, 0, 1) | (0, 0, 1) |
| (0,0,0) (100,0,0) (0,0,100) | (0, −1, 0) | (0, −1, 0) |
| (0,0,0) (100,0,0) (0,100,100) | (0, −0,7071, 0,7071) | (0, −0,7071, 0,7071) |

Sui primi due **la terza riga e la terza colonna danno lo stesso risultato**: una
sonda fatta solo con piani allineati agli assi avrebbe scelto a caso, con una
probabilità su due di sbagliare per sempre. Sul terzo la colonna dà
`(0, 0,7071, 0,7071)` — girata di 90°. *Un caso in cui due valori sbagliati danno
lo stesso risultato non tabella niente*: il piano obliquo sta nella sonda apposta,
e sta nel caso di banco per lo stesso motivo.

L'operazione **calcola la normale dai tre punti e la confronta con quella che il
piano dice di essere**, rifiutandosi se divergono: «una feature in più
nell'albero» lo direbbe anche di un piano storto, e su un piano storto ci si
disegna sopra senza accorgersene. E tre punti in fila si rifiutano **prima** di
chiamare, perché il perché si sa senza chiederlo — SOLIDWORKS risponde `null` e
non lo dice.

`UseGlobal` provato `true` e `false`: stessa trasformazione, numero per numero.
Non si espone.

**Le altre due parti di GP9 restano aperte, con la ragione scritta**: nessuna
operazione del catalogo consuma un sistema di coordinate o un punto di
riferimento, e un'operazione il cui effetto non è osservabile da nessuna altra è
esattamente quello che questo progetto rifiuta di aggiungere.

---

## 0.53.0 — 16 settembre 2026 · libreria 0.88.0

### GP16 — `verifica_sovrasporgenze`, e la normale che era già giusta

Catalogo a **56 operazioni**. Cosa non si stampa senza supporti: una parete che
pende nel vuoto oltre un certo angolo esce a filamenti persi, e SOLIDWORKS non
dice niente — il pezzo è sano, il volume è giusto, la geometria è chiusa.

**Il rischio della voce, misurato, si è rovesciato.** La voce avvertiva che la
normale di `ISurface` è quella della superficie sottostante e va girata quando
`FaceInSurfaceSense()` è falso, e che sbagliarlo dà *«un'analisi che dichiara
supportata proprio la faccia che pende nel vuoto»*. Su un blocco di cui si sa
tutto (100×100×20, sei facce con normali uscenti note):

| | |
|---|---|
| `IFace2.Normal` esce dal materiale | **6 volte su 6** |
| `Normal` concorde con `PlaneParams` | 1 volta su 6 |
| `FaceInSurfaceSense() = true` | sulle **5** in cui sono *opposte* |
| `FaceInSurfaceSense() = false` | sull'**1** in cui sono concordi |

Due cose. **`IFace2.Normal` è già giusta** e non va corretta con niente — è
quella che il progetto usa dappertutto, e adesso è misurato invece che sperato.
E **la regola «se il senso è falso, gira» sarebbe stata sbagliata al
contrario**, non su metà delle facce ma su cinque sesti.

**Le facce curve si tassellano.** Il sotto di un cilindro orizzontale pende nel
vuoto nella parte bassa e non in quella alta: non basta una normale per faccia.
`GetTessTriangles` risponde **`float[]`, non `double[]`** — il cast sbagliato
torna null in silenzio. Il verso dei triangoli lo decide l'avvolgimento, che è
una convenzione: **si controlla contro `IFace2.Normal` su tutte le facce piane**,
dove la risposta si conosce, e se non combacia l'operazione si rifiuta invece di
rispondere con un'analisi rovesciata.

**L'angolo si conta dalla verticale**, come negli slicer: parete dritta 0,
soffitto orizzontale 90, «soglia 45» vuol dire oltre 45. Non è l'angolo col
piano, che è il complementare — due numeri che si somigliano e dicono il
contrario.

**E quello che appoggia sul piano non è una sporgenza.** La faccia di sotto
guarda dritta in giù — 90°, il caso peggiore — ma appoggia sul piano della
macchina. Contarla vorrebbe dire dichiarare non stampabile ogni pezzo esistente.

---

## 0.52.0 — 16 settembre 2026 · libreria 0.87.0

### GP15 — `sformo`, e due API che non si distinguono leggendo

Catalogo a **55 operazioni**. L'angolo di spoglia: le pareti di un pezzo
stampato in stampo devono pendere di un grado o due, o il pezzo non esce dalla
forma.

**Quattro famiglie per lo stesso comando, e due hanno la firma identica.**

    IFeatureManager.InsertMultiFaceDraft(Angle, FlipDir, EdgeDraft,
                           PropType, IsStepDraft, IsBodyDraft) -> Feature
    IFeatureManager.DraftXpertChange    (Angle, FlipDir, EdgeDraft,
                           PropType, IsStepDraft, IsBodyDraft) -> Feature
    IModelDoc2.InsertMfDraft (Angle, FlipDir, IsEdgeDraft, PropType) -> void
    IModelDoc2.InsertMfDraft2(... , StepDraft)                       -> void

Le prime due hanno gli stessi nomi di parametro, gli stessi tipi e lo stesso
tipo di ritorno: **non si distinguono leggendo**. Misurate, con le stesse
identiche selezioni e gli stessi identici argomenti, `InsertMultiFaceDraft` fa
lo sformo e **`DraftXpertChange` torna `null`**. Le altre due tornano `void` e
non si possono nemmeno controllare.

**I marchi, e il difetto che ci sta dentro.** Faccia neutra marchio 1, facce da
sformare marchio 2. Sullo stesso blocco 100×100×20, sformo di 5°:

| marchi | esito | laterali | volume |
|---|---|---|---|
| neutra 1, facce 2 | `Sformo1` | **5,00°** | 200000 → 207080,7 |
| neutra 2, facce 1 | `Sformo1` | **0,00°** | 200000 → 243744,3 |

La seconda riga è il difetto: scambiando i marchi la lavorazione **nasce lo
stesso, si chiama lo stesso, non dà nessun errore** — e ha sformato altre facce,
di un'altra quantità.

**E l'angolo della feature non è una misura.** In tutti e due i casi
`IDraftFeatureData.Angle` rilegge **5°**: quello che si è chiesto, non quello
che è successo — anche dove le facce sono rimaste a zero. Perciò l'operazione
conta le facce **del pezzo** che stanno all'angolo giusto rispetto alla neutra,
prima e dopo, e si rifiuta se non ne sono comparse abbastanza. L'angolo
dichiarato dalla feature torna nella risposta accanto a quello misurato, con
scritto che cos'è.

È la terza API in due giorni che risponde con ciò che le è stato chiesto invece
che con ciò che ha fatto, dopo `ShowConfiguration2` e `ModifyDefinition`.

---

## 0.51.0 — 16 settembre 2026 · libreria 0.86.0

### GP14 — `sopprimi_istanza`, e le due ripetizioni che restano bloccate

Catalogo a **54 operazioni**. Una griglia di fori che ne salta uno dove passa
una nervatura, una fila di asole che si ferma prima della piega.

**Non c'è un metodo: si scrive un array.** Non esiste una «SkipInstance». Si
prende il dato della feature, si scrive `SkippedItemArray`, e si rimette:
`GetDefinition()` → `AccessSelections` → `SkippedItemArray = {…}` →
`ModifyDefinition()`. È una scrittura *dopo* la creazione, non un parametro.

**Gli indici, misurati.** Piastra 200×100, foro a x=−50, ripetizione di 4 con
passo 25 verso −X, cioè posizioni −50 (l'originale), −75, −100, −125. Scritto
`{2}`, sparisce quello a **−100**: l'indice è la *seconda copia*, l'originale non
si conta, il conto parte da 1. È anche il modo in cui conterebbe chi chiede, per
cui il numero passa così com'è — e lo **zero si rifiuta** dicendo che
l'originale non è una copia.

**Il difetto: si può togliere una copia che non c'è.** Nella stessa prova la
quarta posizione (−125) cadeva fuori dalla piastra, e SOLIDWORKS aveva
costruito tre copie su quattro rispondendo «riuscito» — è la nota 80y, ed è
ancora vera. Chiedere di saltare una copia mai esistita **riesce**:
`ModifyDefinition` risponde `true`, l'array si scrive, nel pezzo non cambia
niente. Perciò si contano le facce della ripetizione prima e dopo, e se non sono
calate **ci si rifiuta rimettendo l'array com'era**.

**Rimettere una copia si scrive `{0}`, non `{}`.** Togliere funziona al primo
colpo; rimettere no, e l'ha preso il banco. Tre modi di svuotare la lista, dopo
aver tolto la copia 2 da una fila di quattro (facce 3 → 2):

| scritto | `ModifyDefinition` | facce dopo | rilettura |
|---|---|---|---|
| `new int[0]` | **true** | 2 (invariate) | `{2}` |
| `null` | **true** *(ma la scrittura alza `RPC_E_SERVERFAULT`)* | 2 (invariate) | `{2}` |
| `new int[]{ 0 }` | true | **3** | `{}` |

Lo zero è il modo di dire «nessuna», non un indice; si scrive in un modo e si
rilegge in un altro; e **due modi su tre rispondono `true` su una cosa che non è
successa**. È la ragione per cui qui il criterio è il conto delle facce e mai il
valore di ritorno — e retro-giustifica il rifiuto di `copie: [0]`, che passato
di là cancellerebbe in silenzio tutte le copie tolte prima.

**E delle nove famiglie di ripetizione, tre non hanno l'array.** Misurato:
sketch-driven, chain e dimension-driven non espongono `SkippedItemArray`, pur
avendo tutte `AccessSelections` con la stessa firma. Non esiste nessuna
interfaccia comune — scriverne una fa fallire la compilazione, che è il modo
migliore di scoprirlo.

**Le altre due della voce restano aperte, e la ragione è scritta.**
`FeatureFillPattern` ha risposto `null` in **quindici** combinazioni misurate
(marchi di selezione, contorno faccia o schizzo, a passo o a numero, margine
zero, stagger 0° e 90°, layout quadro e a perforazione, seme come feature, come
schizzo, come faccia cilindrica, seme creato da SOLIDWORKS, `GeometryPattern`)
— senza eccezione, senza messaggio e senza lasciare nell'albero nessuna feature
in errore. E `InsertTableDrivenPattern2` con `FileName` vuoto risponde `null`:
vuole un file su disco, che è un'altra forma di operazione. Tabelle e dettagli
in `81u_quindici_volte_null_senza_un_perche`.

---

## 0.50.0 — 16 settembre 2026 · libreria 0.85.0

### GP13 — `incidi_testo`, e la scritta che sparisce a metà senza dire niente

Catalogo a **53 operazioni**. Scrive delle *parole* nel pezzo — numero di serie,
verso di montaggio, marcatura del cliente — incise o in rilievo su una faccia
piana.

`IModelDoc2.InsertSketchText` fa lo **schizzo**; il materiale lo tolgono (o lo
aggiungono) il taglio e l'estrusione già misurati. Non apre nessuna finestra —
non è come `AddDimension` — e funziona anche con `AddToDB = true`.

**Il difetto, e qui è grosso.** «PROVA» alta 10 mm, incisa 2 mm, su un quadrato
di 100×100:

    ancorata a (0, 0)     ->  tolti 230,70 mm3
    ancorata a (30, 10)   ->  tolti 111,20 mm3

**Metà della scritta cade fuori dalla faccia e sparisce in silenzio.** La
lavorazione nasce verde, si chiama `Taglio-Estrusione1`, e nel pezzo c'è «PRO».
Su un numero di serie non c'è niente da cui accorgersene guardando l'albero.
Tutta fuori invece si prende già (`FeatureCut4` torna `null`): il caso che conta
è la sovrapposizione **parziale**.

**Come si prende.** La larghezza di una scritta non si calcola dalla stringa:
dipende dal disegno dei glifi. Ma `ISketchText.GetEdges()` torna i contorni
delle lettere — **56 `IEdge` per «PROVA»** — e da quelli si legge dove la
scritta finisce davvero, in coordinate del modello. Si controlla **prima** di
tagliare e ci si rifiuta, dicendo di quanto sbordava; lo schizzo viene ritirato.
Per «PROVA» in Century Gothic alta 10 mm sono **47,05 × 10,51 mm** — un numero
che si può solo misurare.

**E quello che un numero ce l'ha.** L'area delle lettere non si prevede, ma va
col **quadrato dell'altezza**:

    altezza  5 mm  ->  area  28,83 mm2  ->  area/h² = 1,1533
    altezza 10 mm  ->  area 115,35 mm2  ->  area/h² = 1,1535
    altezza 20 mm  ->  area 265,88 mm2  ->  area/h² = 0,6647   <- rotto

Il terzo è rotto perché a 20 mm «PROVA» è larga 94 mm e sulla faccia da 100 non
ci stava: **la stessa legge che tiene i primi due è quella che denuncia il
terzo.** L'operazione risponde `area_su_altezza_quadra`, e il banco lo pretende
uguale fra tre marcature di altezza e profondità diverse.

**`Alignment` non fa niente.** La voce lo dava per «non verificato nel
significato pratico». Provati tutti e cinque i valori 0..4 sulla stessa scritta:
la scatola torna identica al millesimo in tutti e cinque. Non è un parametro che
non si è capito — è un parametro che questa chiamata ignora. Perciò non si
espone.

L'operazione dichiara anche **cosa non guarda**: confronta la scritta con la
*scatola* della faccia, e dice se quella faccia riempie la propria scatola
(allora il controllo è completo) o se ha un foro o un contorno rientrante
(allora una lettera potrebbe caderci sopra senza che se ne accorga).

---

## 0.49.0 — 16 settembre 2026 · libreria 0.84.0

### GP12 — proprietà personalizzate e configurazioni, e i due cassetti che non si vedono

Catalogo a **52 operazioni**, con una famiglia nuova — **`identita`**: *quello
che il pezzo dice di sé*. `scrivi_proprieta`, `crea_configurazione`,
`attiva_configurazione`.

Fino a qui il catalogo sapeva fare geometria e non sapeva dire **di chi è**. Il
numero di disegno e la revisione non sono geometria, ma sono ciò che arriva al
cartiglio e alla scheda del PDM: un pezzo giusto con la revisione sbagliata è un
pezzo sbagliato.

**Lo stesso nome su due interfacce, con firme diverse.** La voce avvisava di una
*«firma gemella-ma-diversa»*: è peggio. `AddConfiguration`, `AddConfiguration2`
e `AddConfiguration3` stanno su **`IModelDoc2`** (otto parametri, otto, quattro;
tornano `void`, `bool`, `object`), e `AddConfiguration`/`AddConfiguration2`
stanno **anche** su `IConfigurationManager` (sei e sette parametri, tornano
`IConfiguration`). Cercata sull'interfaccia sbagliata, `AddConfiguration3` *«non
esiste»* — ed è l'errore che abbiamo fatto prima di guardare. *Un nome dice come
si chiama un metodo, non su quale interfaccia sta.* Si usa
`IConfigurationManager.AddConfiguration2`, che è l'unica a tornare l'oggetto
configurazione — da cui si rilegge il nome vero — e a prendere `Rebuild`.

**Le due strade per le proprietà di una configurazione sono la stessa strada.**
La voce chiedeva di accertare se `IConfiguration.get_CustomPropertyManager()` e
`IModelDocExtension.get_CustomPropertyManager(nome)` dessero risultati
equivalenti — *«due strade mai confrontate»*. Misurate entrambe sullo stesso
pezzo: scritta con l'una si rilegge identica con l'altra, in tutti e due i
versi. Si usa `Extension(nome)`, che non ha bisogno dell'oggetto configurazione
in mano.

**Ma il documento e la configurazione sono due cassetti separati**, e questo non
si sapeva: una proprietà scritta sul documento **da una configurazione non si
vede**, e viceversa. Non si ereditano. Chi scrive il numero di disegno sul
documento e poi lo cerca fra le proprietà della configurazione non lo trova, e
nessuno gli dice perché. `scrivi_proprieta` guarda ogni volta anche nell'altro
cassetto e risponde `si_vede_dall_altro_cassetto`, così è un numero misurato su
questo pezzo adesso e non una nota nel catalogo che nessuno ha letto.

**La configurazione predefinita non si chiama `Default`.** Si chiama
`Default<A macchina>`, e le parentesi angolari sono **dentro il nome**.
`ShowConfiguration2("Default")` risponde **false** — non solleva niente, non
dice niente — e chi non guarda il valore di ritorno lavora sulla variante
sbagliata per tutto il resto del piano.

E non si può nemmeno indovinare: un pezzo appena nato dal modello di questa
azienda ha **due configurazioni vere**, `Default<A macchina>` e
`Default<Saldato>` — la coppia dei saldati — e `GetConfigurationCount()`
risponde **2**. Non sono stati di visualizzazione: quelli sono un'altra cosa
ancora (`IConfiguration.GetDisplayStates()`, qui uno per configurazione). Perciò
`attiva_configurazione` **non traduce** il nome corto: lo confronta con quelli
che il documento dichiara, e se non c'è lo dice spiegando perché. L'unica fonte
buona è `GetConfigurationNames()`: *i nomi non si costruiscono, si chiedono.*
Dopo aver chiesto, **ricontrolla quale è attiva adesso**.

**E `ShowConfiguration2` risponde `false` quando non c'era niente da fare** —
cioè quando la configurazione chiesta è già quella attiva. L'ha trovata il
banco, nel modo più bello possibile: due casi rossi il cui messaggio di rifiuto
diceva da solo che il rifiuto era sbagliato («*ho chiesto di attivare Sinistra e
adesso è attiva Sinistra*»). Quel booleano non distingue «non l'ho fatto» da
«non c'era niente da fare», e quindi **non può essere il criterio**: il criterio
è quale configurazione è attiva dopo. Il booleano torna nella risposta come
`era_gia_attiva`, perché la differenza fra uno spostamento e un non-spostamento
conta lo stesso.

**`AddConfiguration2` attiva subito** la configurazione nuova senza che glielo
si chieda: tutto quello che il piano disegna dopo, e tutto quello che lo stato
racconta dopo, riguarda *quella*. Si dice nella risposta.

**E `get_CustomPropertyManager("Destra")` su una configurazione che non esiste
non solleva niente**: torna un gestore, e `Add3` su quel gestore risponde
`AddedOrChanged`. La proprietà è scritta, il codice è contento, nel file non
c'è. È il difetto che non dà errore nella sua forma più pura — tutta la catena
dice di sì. L'unico modo di prenderlo è chiedere al documento quali
configurazioni dichiara **prima** di scrivere.

Lo stato del documento porta adesso `configurazione`, `configurazioni`,
`quante_configurazioni`, `proprieta` e `proprieta_configurazione` — due campi
per le proprietà e non uno, perché sono due posti diversi: un campo solo sarebbe
una fusione che nel file non esiste.

---

## 0.48.0 — 16 settembre 2026 · libreria 0.83.0

### GP11 — `filettatura_cosmetica`, e la nota che non è generata ma ricopiata

Catalogo a **49 operazioni**. Dice *«qui c'è un filetto»* su un perno o un foro
già fatto: è un'annotazione, non un taglio.

**Si attacca a uno SPIGOLO, non a una faccia.** Sulla faccia cilindrica
`InsertCosmeticThread3` torna `null` in tutte le combinazioni provate. A dirlo è
stato il modello dei dati: `ICosmeticThreadFeatureData` ha una proprietà
**`Edge`**. Col bordo circolare selezionato funziona al primo colpo.

**E la domanda della voce non aveva risposta.** La voce chiedeva di trovare il
valore giusto di `Standard` usando «la nota generata» come giudice. Misurato:
con `Standard` 0, 1, 2, 3 e 8 (ISO), con `StandardType` «Tapped hole», «Machine
Threads», «Maschiatura», «Filettature macchina», e `Size` «M6x1.0», «M6»,
«M6x1» — la feature nasce sempre, e rileggendola:

```
Standard = -2    StandardType = «»    Size = «»    callout = «»
```

Passando invece la **nota**: `callout = «M6x1»`, anche con `Standard = -2` e i
due nomi vuoti. **Lo standard e la taglia vengono ignorati, e il callout è
esattamente il testo che gli si dà.**

Quindi l'operazione chiede una `denominazione`, la scrive, e **dichiara nella
risposta che quel testo è di chi l'ha chiesto e non di una tabella**: nessuno
controlla che «M6x1» sia giusto per un perno da 6, e in officina arriva com'è
scritto. *Generata* e *ricopiata* non sono la stessa cosa.

**Il volume resta quello di prima** — 282,74 mm³ su un cilindro Ø6×10, prima e
dopo. Se cambia, l'operazione fallisce: vorrebbe dire che ha tagliato davvero.

## 0.47.0 — 16 settembre 2026 · libreria 0.82.0

### GP7 — `raccordo_variabile`, e il numero che distingue il variabile dal costante

Catalogo a **48 operazioni**. Un raccordo il cui raggio **cambia** da un capo
all'altro dello spigolo: grande dove il pezzo incassa un carico, piccolo dove
non c'è posto.

**La ricetta era il rischio della voce, e si è chiusa alla prima misura.** Cubo
40×40×40, spigolo verticale, raggi 3 e 8, una forma per pezzo nuovo:

```
spigolo + i suoi 2 vertici, Radii = {3, 8}      277,7 mm³
SOLO i due vertici (senza lo spigolo)             NULL
con le marche 1/1/1 invece che senza marca      277,7 mm³
aggiungendo PointRadiusArray = {3, 8}           277,7 mm³
```

**Lo spigolo va selezionato**, i vertici pure, **le marche non contano**, e
`PointRadiusArray` **non serve** — quello è per i punti *intermedi*, che qui non
ci sono. Passarlo per abitudine sarebbe un argomento che non si sa cosa fa.

**E il numero è quello che distingue il variabile dal costante.** Il volume
tolto da un raccordo di raggio r su uno spigolo lungo L è L·r²·(1 − π/4); per un
raggio che va da 3 a 8 la media di r² è (9 + 24 + 64)/3 = 32,33 e il conto dà
**277,5**, misurati **277,7** — lo 0,07%, perché il blend non è esattamente una
spazzata lineare del raggio. Un raccordo **costante** da 3 ne toglierebbe
**77,3**, uno da 8 **549,4**: i tre numeri sono lontani, e nessuno dei due
sbagliati può passare per quello giusto. L'operazione riporta tutti e due — il
tolto vero e il conto rifatto a mano — e dichiara che il secondo è un ordine di
grandezza.

Due rifiuti: il raggio più grande che non ci sta sullo spigolo (lo dice prima di
provare, con la lunghezza vera) e il **cerchio** — il bordo di un foro non ha due
estremi fra cui variare.

**`raccordo_faccia` non è fatto**, e la voce resta aperta a metà: con le due
facce selezionate a marca 1 e 2 torna `NULL` in tutte e quattro le combinazioni
di opzioni provate. L'ipotesi da provare per prima è che serva a facce **non
adiacenti** — quelle che ho usato condividono uno spigolo, cioè sono il caso del
raccordo normale.

## 0.46.0 — 16 settembre 2026 · libreria 0.81.0

### GP8 — `combina_corpi`, e l'enum col nome giusto che è quello sbagliato

Catalogo a **47 operazioni**. Le booleane fra corpi dello stesso pezzo: unisci,
sottrai, interseca. Servono agli stampi e alle dime, e ai pezzi disegnati come
più corpi e poi messi insieme.

**L'enum che si chiama come l'operazione non è il suo.** In `swconst` c'è
`swCombineBodiesOperationType_e`, con i valori `Add = 0`, `Subtract = 1`,
`Common = 2` — i nomi esatti delle tre booleane. Passati a
`InsertCombineFeature` **tornano `null` tutti e tre**, senza un'eccezione e
senza un codice. Il metodo vuole `swBodyOperationType_e`:

```
SWBODYADD       = 15903     unisci
SWBODYCUT       = 15902     sottrai
SWBODYINTERSECT = 15901     interseca
```

Un nome dice come si chiama una costante, non a quale funzione appartiene.

**E l'array va avvolto, e contiene anche il principale.** Misurato su pezzi
nuovi, una variante per pezzo:

| `ToolVar` | ADD | CUT | INTERSECT |
|---|---|---|---|
| `object[]` col solo utensile | NULL | NULL | NULL |
| `Body2[]` tipizzato | NULL | ok | NULL |
| `DispatchWrapper[]` solo utensile | NULL | ok | NULL |
| `DispatchWrapper[]` **tutti e due** | ok | ok | ok |

Un `object[]` di oggetti COM non diventa un `SAFEARRAY` di `IDispatch*` da solo
— ci vuole `DispatchWrapper` — e senza il corpo principale nell'elenco `unisci`
e `interseca` non fanno niente, senza dirlo.

**I tre numeri, su un caso che li distingue.** Due cubi da 10 sfalsati di 2 in X
e 5 in Z, sovrapposti per 400 mm³: **1600 / 600 / 400**. Con due cubi uguali e
centrati sarebbero stati 1500, 500 e 500, e «sottrai» e «interseca» non si
sarebbero distinti — un caso in cui due valori sbagliati danno lo stesso
risultato non tabella niente.

Dopo un `unisci` i corpi si contano: se sono ancora due non si sono fusi, e
l'operazione fallisce invece di confermare un pezzo diverso da quello che chi
legge crede di avere.

**E il banco ha imparato a confrontare un elenco di testo.** `["C2"]` — i corpi
su cui la booleana ha lavorato — finiva in *«non è un elenco di numeri:
System.String[]»*: un messaggio vero e inutile, perché il caso chiedeva una cosa
sensata e il valutatore non sapeva confrontarla. Adesso gli elenchi senza numeri
si confrontano uno a uno, **e l'ordine conta**; sette controlli nuovi in
`Prove.Contratti`.

## 0.45.0 — 16 settembre 2026 · libreria 0.80.0

### GP5 — `distanza_minima` e `misura_fra_entita`, e l'argomento che viene ignorato

Catalogo a **46 operazioni**. `distanza_minima` è il modo di rispondere a
*«quanto materiale resta fra la cava della guarnizione e la faccia esterna?»*
con due facce **nominate** e un numero, invece di tarare un vettore di
intersezioni a raggi che nessuno sa verificare — la via che il censimento delle
API segnalava come troppo rischiosa.

**`ClosestDistance` torna metri**, e i suoi due punti sono veri: su una piastra
40×40×10, fra le due facce grandi, risponde `0,01` e i punti sono `[0,0,10]` e
`[0,0,0]` mm, scarto **zero**. Quel confronto adesso si fa a ogni chiamata: **se
il valore dice una cosa e i suoi stessi punti ne dicono un'altra, la chiamata è
fallita e ha risposto lo stesso** — e allora l'operazione fallisce invece di
dare il numero.

**`IMeasure.Calculate(Object Entities)` prende un argomento e non lo guarda.**
Misura quello che è **selezionato**:

```
Calculate(new object[] { facciaA, facciaB })          ->  FALSE
due facce selezionate, poi Calculate(null)            ->  TRUE, 0,01
```

E peggio: chiamata con **una** faccia nell'array, mentre la selezione ne
conteneva ancora due, ha risposto `TRUE` e ha dato l'area di tutte e due —
3200 mm² invece di 1600. Un numero credibile, del pezzo giusto, di un'altra
selezione. L'operazione svuota la selezione, seleziona a mano e **guarda il
booleano**.

**`Area` e `TotalArea` si alternano**, e il caso dichiarato l'ha preso:

```
una faccia sola    Area = 1600 mm²    TotalArea = -1
due facce          Area = -1          TotalArea = 3200 mm²
```

Quella che vale è sempre l'altra. Leggendone una sola, il campo spariva a
seconda di quante cose si chiedevano. E **`-1` non è una misura**: è il modo di
SOLIDWORKS di dire «questa cosa qui non ce l'ha», quindi i valori negativi
diventano `null` invece di numeri.

## 0.44.0 — 15 settembre 2026 · libreria 0.79.0

### GP4 — `proprieta_di_massa` e `ingombro_corpo`, e la misura che cambia risposta

Catalogo a **44 operazioni**. Le due domande che si fanno prima di ordinare —
«quanto pesa questo pezzo in alluminio» e «entra nella scatola 300×260×40» — si
calcolavano già dentro `StatoDocumento` ma **nessuna operazione poteva
chiederle**: arrivavano dentro l'intero stato del documento, o non arrivavano.

**Il difetto che il caso dichiarato ha preso alla prima esecuzione.**

```
NO   massa_di_un_cubo_di_acciaio · misure.densita_kg_m3   1 (atteso 8000 ±0.5)
```

`IMassProperty` **cambia le sue risposte a seconda di che cosa gli si è chiesto
prima**. Sullo stesso oggetto, con `AddBodies` che risponde `true`:

```
letto subito Density                     ->  8000   (giusto)
letto prima Volume,      poi Density     ->     1
letto prima SurfaceArea, poi Density     ->     1
letto prima CenterOfMass, poi Density    ->  8000   (indenne)
```

E la massa segue: **0,0002 kg invece di 1,6 — ottomila volte più leggera**, con
volume, area e baricentro tutti giusti accanto. Nessuna eccezione, nessun valore
assurdo: un numero credibile in mezzo a quattro numeri giusti.

Il rimedio non è «leggi prima la massa»: un ordine di righe è un contratto che
si rompe il giorno in cui qualcuno le riordina, e non fallirebbe niente. È **un
`IMassProperty` nuovo per ogni numero** — quattro chiamate COM che non le sente
nessuno, e l'ordine smette di esistere come concetto.

**La massa senza materiale è `null`, non un numero.** SOLIDWORKS non fallisce:
risponde con la densità predefinita del modello, **1000 kg/m³, cioè acqua** —
0,2 kg invece di 1,6 sullo stesso cubo. Credibile e sbagliato di otto volte.
L'operazione risponde `null` e scrive perché e cosa fare.

**E i momenti principali non ci sono, di proposito.**
`IMassProperty.PrincipleMomentsOfInertia` risponde **tre zeri**, e così
`IMassProperty2.PrincipalMomentsOfInertia` con l'altra grafia. I momenti veri
stanno in `Extension.GetMassProperties(1)`, posizioni 6-8. Un campo che si
chiama «momenti principali» e risponde zero non dà errore — un momento nullo è
un momento — quindi non si riporta affatto.

`ingombro_corpo` prende i **punti veri** con `GetExtremePoint` in sei direzioni,
non la scatola approssimata di `GetBodyBox`, e dichiara il suo limite a ogni
chiamata: è la scatola allineata agli assi, non la più piccola possibile.

## 0.43.0 — 15 settembre 2026 · libreria 0.78.0

### GP3 — `esporta_step` e `esporta_iges`, e il file che non aveva niente da riparare

Catalogo a **42 operazioni**. STEP e IGES sono i formati con cui si manda un
pezzo a un fornitore, a un cliente, o lo si porta in un altro CAD.

**Non si rifiutano come lo STL, ed è voluto.** Un STEP con dieci corpi è
*normale* — un saldato si manda al fornitore così — e uno con delle superfici
aperte pure, perché STEP e IGES le portano. Rifiutarli sarebbe applicare a un
formato le regole di un altro. La verifica gira lo stesso e **il suo giudizio
viaggia con la risposta**: chi manda un pezzo ha diritto di sapere che aveva due
corpi, non di essere fermato.

**Il protocollo si rilegge dal file, non si crede.** `swStepAP` vuole 203, 214 o
242, e quei tre valori **non hanno un enum** in `swconst`: il numero è un
letterale. L'unica prova che il 214 chiesto è il 214 ottenuto è il `FILE_SCHEMA`
scritto dentro il file — `AUTOMOTIVE_DESIGN` per l'AP214,
`CONFIG_CONTROL_DESIGN` per l'AP203. Se non combacia, l'operazione fallisce:
certi fornitori accettano solo il 203, e un file col protocollo sbagliato si
scopre dall'altra parte.

```
OK   step_ap214_riletto · misure.file_schema      AUTOMOTIVE_DESIGN
OK   step_ap203_e_lo_dice · misure.file_schema    CONFIG_CONTROL_DESIGN
```

Due casi scrivono lo stesso cubo con i due protocolli e pretendono schemi
diversi — con `non_contiene` sull'altro nome, perché «c'è il nome giusto» non
basta. Se la preferenza non arrivasse a destinazione uscirebbero identici, e
verdi tutti e due.

**`riapri_e_confronta`: la sola misura che dice che la geometria è
sopravvissuta.** Il file viene riaperto in SOLIDWORKS e il suo volume
confrontato col pezzo, fallendo oltre un millesimo di scarto. Misurato: **0,00%
sia in STEP AP214 sia in IGES BREP**, 200.000 mm³ esatti in tutti e due. Non è
il predefinito perché costa un'apertura di documento.

**E il difetto che ha richiesto una sonda.** La prima scrittura riapriva con
`OpenDoc6`, che su un file appena scritto risponde:

```
OpenDoc6(…, swDocPART, Silent)           NULL   2097152  swFileRequiresRepairError
OpenDoc6(…, swDocIMPORTED_PART, Silent)  NULL      1024  swInvalidFileTypeError
LoadFile4(percorso, "", null)            aperto      0   1 corpo, 200000 mm3
```

«Il file richiede una riparazione» su un file che non ha niente da riparare —
lo avevamo scritto noi due righe prima. **Un codice d'errore descrive quello che
l'API ha concluso, non quello che è successo**: per i formati neutri la porta è
`LoadFile4`, e `OpenDoc6` manda a cercare il guasto dalla parte sbagliata.

## 0.42.0 — 15 settembre 2026 · libreria 0.77.0

### GP2 — `esporta_stl`, e il formato che non porta con sé niente

Il catalogo passa a **40 operazioni** e guadagna la famiglia **`esportazioni`**.
È il primo ponte verso una stampante: senza, un pezzo disegnato dal modello non
arriva mai a una macchina.

**Un file STL non contiene quasi niente**: un elenco di triangoli. Non dice in
che unità sono, non dice se il solido è chiuso, non dice da quale pezzo viene.
Tutto quello che si può sbagliare qui **si apre lo stesso, si affetta lo stesso
e si stampa lo stesso**, e il difetto si vede quando il pezzo è in mano. Quindi:

**La verifica gira PRIMA, e può fermare tutto.** `esporta_stl` chiama
`verifica_geometria_corpo` e **si rifiuta di scrivere** se il pezzo non è un
solo corpo solido chiuso senza difetti. Era la riga per cui GP1 andava fatta
prima di questa: la misura del file — esiste, pesa qualcosa — non cattura il
corpo con la fessura.

```
NO   esporta_stl su due corpi
     Non esporto: Non lo esporterei cosi': il pezzo ha 2 corpi: per una
     stampa dev'essere uno solo.
     Un STL non porta con se' niente che dica che il pezzo era cosi': si
     apre, si affetta e si stampa lo stesso. Sistema il pezzo, oppure passa
     «esporta_comunque»: true se sai quello che fai.
```

**La via d'uscita esiste e si dichiara.** Un giorno qualcuno avrà ragione lui —
due corpi da stampare insieme apposta — e un rifiuto senza scampo si aggira
copiando il file a mano, cioè fuori da ogni controllo. Con `esporta_comunque`
il file si scrive **e la risposta porta scritto** che la verifica è stata
scavalcata e cosa diceva.

**I millimetri sono espliciti.** `IExportStlData` **non esiste in questo
interop** (cercata fra tutte le 1737 interfacce): ogni opzione è una
*preferenza utente globale* della macchina. `swExportStlUnits` lasciata com'è
vuol dire che il risultato dipende da chi ha usato SOLIDWORKS prima — e il file
non porta le unità con sé, quindi esportato in pollici il pezzo esce 25,4 volte
più piccolo e nessun errore lo dice. Le preferenze si **leggono, si impostano e
si rimettono com'erano**: sono le impostazioni di chi disegna, non nostre.
`swSTLShowInfoOnSave` va a `false` perché una finestra bloccherebbe la chiamata
COM e non tornerebbe mai — la stessa trappola di `AddDimension`.

**E poi si rilegge il file, non la risposta.** Un STL binario è 80 byte di
intestazione, 4 col numero di triangoli, poi 50 per triangolo:

```
OK   scatola_dodici_triangoli · misure.triangoli   12   (sei facce, due triangoli ciascuna)
OK   scatola_dodici_triangoli · misure.byte       684   (84 + 50*12)
```

Se il conto non torna, l'operazione fallisce dicendo che il file c'è e non è
quello che serve. `SaveAs3` che torna `true` dice solo che SOLIDWORKS non ha
protestato.

Cinque casi dichiarati in `casi\esporta\`: la scatola binaria, la stessa in
ASCII (dodici triangoli contati in un altro modo — righe `facet` invece dei
quattro byte), i due corpi rifiutati, i due corpi esportati a forza (ventiquattro
triangoli: il numero dice anche che il secondo corpo è finito nel file davvero),
e l'estensione sbagliata — `.stp` al posto di `.stl` farebbe scrivere tutt'altro
formato, e il file esisterebbe, peserebbe e si aprirebbe.

I casi possono scrivere file: segnaposto **`{LAVORO}`**, che è `Banco\lavoro\` —
ignorata da git e svuotabile in qualunque momento.

## 0.41.0 — 15 settembre 2026 · libreria 0.76.0

### GP1 — `verifica_geometria_corpo`, e la prima operazione che non disegna

Il catalogo passa a **39 operazioni** e guadagna una famiglia: **`verifiche`**
— *«dire com'è venuto, senza toccarlo»*. Fino a qui nessuna operazione sapeva
rispondere a «com'è venuto?»: le misure c'erano — `taglio_rettangolare` conta
il materiale tolto, `foro_guidato` misura dove è finito l'asse — ma erano
private dentro le operazioni, e il modello non poteva chiamarne nessuna.
`distinta_taglio` era il solo precedente di un'operazione che legge.

`verifica_geometria_corpo` risponde, per ogni corpo: tipo (solido, foglio,
filo), quante facce, se è chiuso, quante fessure, quanti difetti topologici
**col nome** (`swFaultEntityErrorCode_e`, 36 valori) e non col numero. Poi un
giudizio in una frase, dove *stampabile* vuol dire **un solo corpo solido,
chiuso, senza difetti**.

**Le due cose misurate prima di scriverla** (`Controlli\CorpoRotto`, un
attrezzo nuovo che è anche il controllo che le tiene vere):

```
                          Check()  Check2()  Check3.Count  fessure
  solido sano                1        0           0           0
  due solidi che si toccano  1        0           0           0
  CORPO DI FOGLIO (aperto)   1        0           0           1
```

1. **`Check()` e `Check2()` hanno convenzioni opposte.** Sullo stesso corpo
   sano il primo dice 1 e il secondo dice 0. Era la domanda lasciata aperta dal
   censimento delle API — *«non so se zero voglia dire sano o zero controlli
   fatti»* — e la risposta è peggio dell'incertezza: le due convenzioni
   convivono nella stessa interfaccia, e chi ne scambia una ottiene un
   risultato coerente, stabile e rovesciato.
2. **Un corpo aperto passa tre controlli su quattro.** Una superficie non è
   «rotta»: è un corpo valido che non racchiude niente. `Check`, `Check2` e
   `Check3` la dichiarano sana tutti e tre, e **solo
   `Diagnose().GetGapsCount()` la vede**. Un'operazione costruita su `Check3` —
   la via che la voce GP1 nominava per prima — direbbe «nessun problema»
   esattamente sul caso per cui esiste.

Quindi si guardano tutte e quattro, e i numeri grezzi di `Check()` e `Check2()`
restano nella risposta: il giorno in cui una versione di SOLIDWORKS cambia una
delle due convenzioni lo si vede da lì, non da un giudizio girato al contrario.

**Il limite si dichiara a ogni chiamata**, non una volta nella descrizione:
`cosa_non_guarda` dice che lo spessore delle pareti e le sovrasporgenze non li
guarda nessuno ancora. Un guscio da tre decimi passa di qui senza una parola.

Trappole di API pagate per strada: **`IBody2.Check3` è una PROPRIETÀ, non un
metodo** (scritto `Check3()` in late binding non dà errore di compilazione, dà
un fallimento a runtime dentro un `catch`), e **`InsertPlanarRefSurface` sta su
`IModelDoc2` e torna un booleano**, mentre ogni altra `Insert*RefSurface` che
torna una `Feature` sta sul `FeatureManager`.

## Il banco, non il ponte — 15 settembre 2026 · nessun numero nuovo

### F11 — Le famiglie di prove diventano file, e tre difetti che erano lì da mesi

Sei famiglie di prove in C# — `rivoluzione`, `crea_piano`, `crea_asse`,
`ripeti_lineare`, `specchia`, `sopprimi/elimina`, più `la forma in una frase` —
sono diventate **venticinque casi dichiarati**. `Segna(` in `ProvaNuove.cs`
passa da **99 a 17**; i casi delle famiglie dichiarate sono **50**, più i
sedici della regressione.

**Il trasloco ha stretto le tolleranze, non solo spostato il codice.** La
rivoluzione accettava `atteso * 0.02`: sul cilindro da 62.831 mm³ sono 1.256
mm³ di scarto ammesso, più di un foro M20 passante. Nei file è ±0,5 mm³ col
motivo scritto accanto. E i casi pinzano di più: `sopprimi_lavorazione` adesso
misura **quale** foro si è spento (`centri_x_mm` da `[-60, 0, 60]` a
`[-60, 0]`), non solo che il volume è tornato.

**Tre difetti trovati scrivendo i file, e nessuno dava errore:**

1. **Un controllo verde che non poteva diventare rosso.** «Nessuno spigolo
   curvo è offerto al modello» contava gli spigoli con `dritto: false`. **Quel
   campo non esiste**: lo stato scrive `curvo: true` e non scrive `dritto` su
   nessuno, quindi il conto restava sempre zero. E la premessa scritta sopra —
   «il modello non può nominare un bordo curvo, quindi il rifiuto non è
   raggiungibile» — era falsa: dopo un foro passante gli spigoli curvi sono
   **due**. Adesso c'è il rifiuto vero,
   `casi/ripeti/lim_direzione_da_spigolo_curvo.json`.
2. **Il confronto fra un rifiuto e la sua gemella era cieco sulle liste.**
   `Convert.ToString` su un `object[]` torna `"System.Object[]"`: due profili
   diversi si scrivevano uguali.
3. **L'elenco dei nomi delle misure si era scollato da chi le misura.** Due
   misure nuove uscivano come «non so misurare …» con accanto l'elenco vecchio,
   e un errore di banco su una prova che il ponte superava.

**Le attese sanno chiedere una frase.** `contiene` e `non_contiene`, con una
stringa o un elenco. La metà che conta è la seconda: una descrizione che
dichiara un profilo che il pezzo non ha più non fallisce niente — è la
descrizione giusta di un altro pezzo.

**La gemella di un'operazione con un argomento solo.** `crea_asse` prende
`faccia_uid` e basta: due casi che differiscono per quell'unico argomento non
possono averne uno in comune, e la regola F5 li rifiutava sempre. Quando le
chiavi sono una sola, «niente in comune» non è un indizio: è l'arità — e
l'eccezione si dichiara nel resoconto.

**Il rosso di F13 non è più un numero.** Era `famiglie.Count >= 15` ed è
diventato rosso senza che niente si fosse rotto, perché le famiglie sono scese
a dodici. Un pavimento numerico su una cosa che deve calare è un rosso a
scadenza: adesso il setaccio deve trovare **sé stesso**.

**`banco.ps1` non legge più il resoconto di ieri per nuovo.** Un
`TypeInitializationException` — un campo statico con inizializzatore che nomina
`IModelDoc2` gira **prima di `Main`**, cioè prima dell'`AssemblyResolve` — usciva
come un `NativeCommandError` che nominava lo script, con `ultimo-giro.txt` fermo
al giro precedente perché `Tee-Object` non lo apre se non passa nessuna riga.
Adesso il file si toglie prima, e un giro che non scrive niente è uscita 2 con
l'istruzione per vedere l'eccezione.

**Le prove che non aprono SOLIDWORKS stanno in un file loro**
(`Banco/ProveSenzaIlPezzo.cs`): i controlli su `Frasi` (E10) e sul racconto
dell'eccezione (F12) sono funzioni pure su stringhe e un setaccio sui sorgenti,
e in mezzo alle prove sul pezzo sembravano prove di disegno.

**Resta in C#, col perché scritto accanto:** `sposta_corpi` (servono due
piastre distanti, e `estrudi_rettangolo` centra sempre sull'origine),
`ScattaLaVista` e `QuantoVedeIlModello` (misurano immagini), e una riga sola di
`crea_asse` — che l'asse si riagganci con `SelectByID2("AXIS")`, che è una
chiamata COM e non un'operazione del catalogo.

### F10 — La libreria di allora non si conserva: si ricostruisce

`.\Controlli\libreria-di-allora.ps1 0.70.0` trova il commit che **dichiara**
quella `AssemblyVersion` — dentro `AssemblyInfo.cs`, non nei messaggi di commit
— ne fa un worktree usa e getta, lo compila e stampa il percorso della DLL.
`-Pulisci` lo toglie.

**La disciplina che la voce chiedeva c'e' gia'**, e si misura: `git log` su
`AssemblyInfo.cs` mostra una riga per numero, senza salti. Il difetto che la
voce descriveva — un commit che copriva 0.29.1 → 0.44.0 — era del monorepo
OpenPDM, prima che la libreria si trasferisse qui.

**`rossa_prima_di`**: un caso dichiara la versione prima della quale doveva
essere rosso, e su una libreria piu' vecchia **deve** fallire. Visto scattare:
«su 0.75.0.0 e' passata, e non doveva: questa prova non custodisce il difetto
che dice di custodire».

**E «la libreria non si carica» non e' piu' un giro rosso.** Puntando
`--libreria` alla 0.30.2 il banco usciva con un'eccezione non gestita e il
codice **-532462766**, indistinguibile da un piantamento; adesso lo dice ed esce
2. Il confronto storico **comincia dalla 0.52.0**, misurato: prima di allora le
operazioni stavano in `OpenPDM.SwAddin`, con un altro spazio dei nomi.

Una prova incrociata che e' venuta gratis: i casi di oggi lanciati sulla 0.70.0
non passano in silenzio — dicono «campo assente: `stato.ingombro_da_mm`» e
nominano tutti i campi che c'erano. E' il comportamento della voce F4, visto
attraverso il tempo invece che attraverso un refuso.

### F9 — Una CI che compila, prova i contratti e **pretende il banco**

`.github/workflows/prove.yml` su `windows-latest`: compila il ponte e
`Prove.Contratti`, guarda che i due eseguibili **esistano davvero**, lancia le
prove, pubblica `prove.xml`.

**E fa la cosa che una macchina senza licenza puo' davvero fare**: una prova di
contratto legge `Banco/resoconto/ultimo.json` e **rifiuta una consegna in cui
nessuno ha lanciato il banco**. Controlla che il giro abbia misurato qualcosa
(«0 prove, 0 fallite» non e' un verde), che non sia parziale, che la libreria
sia quella dichiarata in `AssemblyInfo.cs`, e che sia **posteriore all'ultima
modifica a `Riscontro.Disegno`** — chiesto a git. *Una libreria toccata dopo
l'ultimo giro e' una libreria non provata, anche se il numero di versione
combacia perche' qualcuno si e' dimenticato di alzarlo.*

Il rosso, misurato togliendo il file: «il banco non e' stato eseguito su questa
versione».

`Banco/resoconto/` resta ignorato **tranne `ultimo.json`**, e non e'
un'eccezione di comodo: senza quel file in repository quel controllo non
esiste.

### F8 — La regressione sui riferimenti, e i tre numeri

```
.\Banco\banco.ps1 -Regressione   ->  16 casi
                                     12  reggono
                                      0  si staccano e LO DICONO
                                      4  si staccano IN SILENZIO
```

Sedici pezzi costruiti, **una sola quota a monte cambiata**, e la geometria a
valle rimisurata. I numeri sono nel README.

**Lo zero in mezzo e' il piu' interessante dei tre.** Nessuno dei sedici casi si
e' rotto in modo visibile: le quattro volte che il pezzo e' uscito diverso da
quello che il disegno dichiarava, SOLIDWORKS ha ricostruito pulito e non ha
detto niente.

**E la scoperta che vale piu' dei numeri:** un taglio schizzato **su una faccia
segue quella faccia**. Il foro sul fondo di un guscio regge sia quando la
scatola si alza di 28 mm sia quando la parete ingrossa di 7. Quello che non
segue e' la posizione **dentro** la faccia: `x_mm`/`y_mm` sono coordinate
assolute sul piano di schizzo, e una faccia che cresce non le sposta.

> Non e' «i riferimenti non reggono». E': **il piano di schizzo regge, le
> coordinate dentro il piano no.**

Due casi dei diciotto **non sono scrivibili oggi**, e sta scritto perche': il
percorso di un telaio e' un `3DProfileFeature` di cui lo stato non pubblica le
quote, e `fazzoletto` vuole due facce d'angolo che nessun selettore sa nominare.

Due arnesi nuovi nel formato dei casi: **`come_prima`** (l'invariante e'
«com'era», e calcolarlo a mano sarebbe rifare un conto che SOLIDWORKS ha gia'
fatto — sbagliandolo si crea un falso silenzio) e **`centro_massa_mm`**
(l'invariante di posizione per cio' che fori non ne lascia). Il secondo e'
servito subito: un caso **reggeva col volume** mentre il foro poteva essere
finito altrove.

### F7 — Le prove che girano dove SOLIDWORKS non c'e'

```
msbuild Prove\Prove.Contratti.csproj -p:Configuration=Release
Prove\bin\Prove.Contratti.exe    ->  54 controlli, 0 falliti, prove.xml (JUnit)
```

Un progetto **a se'**, non un modo del banco: il csproj del banco risolve
l'interop con un `HintPath` dentro `…\SOLIDWORKS\api\redist`, e su una macchina
senza SOLIDWORKS **la compilazione fallisce**. Un flag non basta — il problema e'
prima del runtime.

Prova il **valutatore delle attese** (19), il **catalogo** e `Simili()` (11),
`Sessione.StessoDocumento` (10, e fino a oggi quella guardia non aveva **nemmeno
una prova**), il **protocollo MCP** parlando col ponte su stdio (13), e che
questo progetto **non referenzi l'interop** — guardando i riferimenti
dell'assembly, non il testo del csproj.

Il valutatore e' lo **stesso file** che compila il banco, non una copia: una
copia diverge, e il giorno che diverge la suite col CAD comincia a passare per
ragioni che qui non si vedono. *Se e' rotto lui, tutta la suite con SOLIDWORKS
diventa verde gratis.*

**Tre difetti trovati appena scritta**, e sono il suo valore:

* **il BOM che rompeva la prima riga.** `Process.StandardInput` nasce con la
  codifica di `Console.InputEncoding` e gli si mette `AutoFlush = true`, che fa
  un Flush, e un Flush UTF-8 scrive il preambolo: **tre byte davanti alla prima
  riga**. Il ponte rispondeva `-32700`, e il sintomo diceva «initialize non
  risponde» perche' la riga d'errore non porta l'id;
* **un controllo scattato per il motivo sbagliato**: «nessun percorso di questa
  macchina» cercava `O:\` senza distinguere maiuscole, e trovava `tipo:\n`;
* **`BadImageFormatException` che sembrava un file mancante**: un `AnyCPU`
  eseguibile parte a 32 bit, e l'immagine x64 del ponte da li' non si carica.

### F6 — `--muta`: ogni caso deve poter diventare rosso

`.\Banco\banco.ps1 -Muta` perturba **il piano, non il codice**: un argomento
alla volta moltiplicato per 1,5 (booleani invertiti), il caso rieseguito, e si
**pretende che diventi rosso**. Un caso che resta verde sotto ogni
perturbazione e' **cieco**: le sue attese non vincolano i suoi argomenti.

**67 mutazioni su 11 casi, zero ciechi, 854 s** — `Banco/resoconto/mutazioni.md`.

E' **l'unico modo meccanico** di imporre che una prova sia gia' fallita almeno
una volta: un `provata_a_fallire` scritto a mano e' una dichiarazione, e una
dichiarazione si copia dal caso accanto.

**Il falso cieco si evita leggendo il catalogo.** Mutare `profondita_mm` su un
taglio passante lascia il caso verde e non e' cecita': lo schema dice `"solo se
non passante"`, e il banco legge quella riga — dalla **libreria caricata**, non
da `bin/catalogo.json`, che lo scrive un'altra compilazione. Due accorgimenti
della stessa famiglia: si mutano solo i passi **con attese** (un foro da 500
mutato a 750 copre il pezzo lo stesso), e **zero per 1,5 fa zero**, quindi una
coordinata a 0 si sposta di 1 mm.

Le mutazioni **sopravvissute** sono la parte utile, e il resoconto le nomina:
`x_mm 0->1` su un foro resta verde perche' quei casi misurano **quanto**
materiale e' stato tolto, non **dove**; `lunghezza_mm 400->600` su
`tasca_400_cieca` resta verde perche' la tasca e' gia' piu' grande della faccia
e SOLIDWORKS la taglia sul bordo — geometria, non attesa debole.

`--muta` ha un riepilogo suo (`MUTAZIONI — 11 casi, 0 ciechi`): i rossi che
produce sono **cercati**, e sommarli a un conteggio che serve a leggere i rossi
non cercati renderebbe inutili tutti e due.

### F5 — Un rifiuto non sta mai da solo

Ogni caso con `deve_fallire` **deve** nominare in `gemella` un caso che riesce,
e quella gemella dev'essere **verde nello stesso giro**. Togliendo il campo da
uno dei `lim_*.json` il comando esce **2** e nomina il caso.

> Una prova che verifica solo il rifiuto non distingue «il controllo funziona»
> da «il controllo rifiuta tutto».

Le quattro coppie canoniche di 80v sono nel repository, tutte sullo stesso
blocco 314 x 274 x 52 (**4.473.872 mm³**), con i numeri rimisurati oggi:

```
foro 270 passante                tolti 2.977.287,36   (pi*135^2*52)
foro 60 col CENTRO SUL BORDO     tolti 73.513,27      (mezzo cilindro: 73.512,62)
tasca 400x400 cieca da 5         volume 4.043.692, ingombro [314, 274, 47]
asola 30x9 passante              area 252,62, tolti 13.136,1
gemelle 4/4 verdi
```

Il secondo e' il piu' importante: il centro cade **fuori** dal materiale e il
taglio e' legittimo lo stesso — un controllo scritto come «il centro deve stare
dentro l'ingombro» lo avrebbe rifiutato.

**La regola scritta nei Rischi non regge alle sue stesse coppie.** «La gemella
deve differire per UN solo argomento» ne rifiuta **due su quattro**: il foro
fuori dal materiale differisce per due coordinate, l'asola per lunghezza e
larghezza. Quella che gira chiede stessa operazione, argomenti non identici e
almeno un argomento in comune — e **dice quanti ne differiscono**, invece di
decidere al posto di chi legge.

### F4 — Un campo che non c'e' non e' uno zero, ed e' uscita 2

Un caso che chiede `misure.materiale_tolto_mm3` a `applica_materiale` — che non
lo riporta — **non fallisce: non si e' potuto misurare**.

```
!!  ... ERRORE DI BANCO: campo assente: misure.materiale_tolto_mm3;
        ci sono: archivio, classe, materiale

28 prove, 0 fallite, 1 ERRORI DI BANCO
attese verificate 15 su 16
uscita: 2
```

**Il 2 vince sull'1**: se il banco non ha potuto misurare, quante prove siano
fallite e' una domanda a cui non si e' risposto. Simbolo suo (`!!`), conteggio
suo, uscita sua, e nel resoconto una sezione sua — «non sono prove rosse, sono
prove che NON SONO STATE FATTE».

**«Attese verificate N su M» si scrive sempre**, anche quando combaciano: un
numero che compare solo quando le cose vanno male non lo guarda nessuno finche'
non e' troppo tardi.

E un `null` dichiarato **non** e' un campo assente: lo stato scrive
`materiale: null` apposta, per dire «quel posto esiste ed e' vuoto». Nel novanta
per cento dei casi una prova smette di verificare perche' il campo che leggeva
e' stato rinominato, un dizionario che non trova la chiave torna `null`, e
`null` confrontato con `null` passa.

### F3 — I casi sono file, e i numeri attesi stanno accanto ai passi

`casi/lavorazioni/guscio_interno.json` e `guscio_esterno.json` girano **senza
una riga di C# dedicata** e riproducono 82.500 e 106.500, piu' l'ingombro, le
facce tolte, il verso dichiarato e il materiale asportato. Cambiando 82.500 in
82.501 il caso diventa **rosso** e il resoconto lo nomina.

**La prova in C# e' stata tolta, non lasciata accanto.** `ProvaGuscio()`
misurava gli stessi due numeri con `volumeAtteso * 0.02`: sui 240.000 mm³ della
piastra sono **4.800 mm³, piu' di un foro M12 passante**. Una prova che accetta
un foro di scarto non prova il foro.

**I selettori rifiutano invece di indovinare**, ed e' il rischio che la voce
nominava — un caso che gira su una faccia diversa da quella che il nome del file
promette, e che magari torna lo stesso volume. Dieci prove pure lo verificano:
zero facce e due facce fanno rifiutare, `@feature(n=…)` e' l'unica ambiguita'
che si puo' sciogliere, e `@quota` torna il nome col titolo del documento **di
adesso** — `piani/cambia_diametro.json` scriveva `Parte76`, che cambia a ogni
giro, e quel piano non si poteva rieseguire.

**Le tolleranze sono assolute e portano il perche'**, e senza il perche' il caso
e' **rotto**, non permissivo. Stessa regola per un caso senza `perche`, un passo
**senza attese** (passa e non misura niente) e un `deve_fallire` senza
`il_messaggio_contiene` (qualunque errore andrebbe bene, anche quello
sbagliato): escono rossi, non saltati.

### F2 — Un comando solo, e un resoconto su disco

`Banco\banco.ps1` compila libreria e banco, guarda se SOLIDWORKS e'
agganciabile, **svuota i documenti rimasti aperti**, lancia e scrive
`Banco/resoconto/AAAAMMGG_hhmm.md` e `.json` piu' `ultimo.json` — con revisione
di SOLIDWORKS, versione della libreria, quante operazioni ha il catalogo,
commit, durata, e le rosse per esteso.

Lo svuotamento sta dentro il comando perche' non e' pulizia: sei documenti di
troppo sono ~650 caratteri nello stato, e «stato sotto i 16 KB» fallisce **senza
che il codice sia cambiato**.

**Le tre uscite, viste tutte e tre:**

```
0   -Gruppo tabella                              -> 0
1   volumeAttesa 200000 messo a 199000 a mano    -> 1, e il .md nomina il caso
2   un errore di sintassi nel banco              -> 2, e NON lancia
2   -Gruppo vist (un gruppo che non c'e')        -> 2
```

Il caso 2 da compilazione e' il difetto che la voce nominava per nome: si
controlla il **codice di uscita** di msbuild, non il testo.

Quello che il resoconto **non** ha — casi ciechi, attese verificate su attese
totali — e' scritto nel resoconto stesso: sono numeri che esistono quando i casi
dichiarano le attese, cioe' dalla voce F3. Uno zero messo li' oggi sembrerebbe
un risultato.

Due difetti presi mentre si provava: il primo `.json` pesava **498 kB**
(`Get-Content` torna righe con addosso `PSPath`, `PSDrive` e il provider, e
`ConvertTo-Json` li segue tutti), e **due giri nello stesso minuto si
sovrascrivevano** — con `-Gruppo` un giro dura secondi, e un resoconto verde e'
stato cancellato da un giro rosso partito venti secondi dopo.

### F12 — Il null adesso ha un nome, e i giri si contano

Il 15/9 una prova e' uscita rossa con «Riferimento a un oggetto non impostato su
un'istanza di oggetto.», e basta. **Non si e' riprodotta**, e non serviva:
bastava contare quanti null potessero dare quel messaggio in quel punto. Erano
due, e di una famiglia sola — `NuovaParte()` usata senza guardare cosa avesse
prodotto (**18 punti**) e `GetFeatures(false) as object[]` col `.Select` subito
dopo (**3 punti**).

`NuovaParte()` non torna piu' null: alza un'eccezione che nomina il passo e dice
che **non e' la prova a essere fallita** — e' il banco che non e' riuscito a
*preparare* il pezzo, che e' un'altra cosa e si legge in un altro modo. I sei
`if (doc == null)` sono stati **tolti**: dopo questa modifica non scattano mai
piu', e un controllo irraggiungibile si legge come una difesa senza difendere da
niente.

**E il conteggio, che prima era impossibile.** La voce chiedeva «quante volte su
quanti giri»: `in-corso.txt` lo riscrive il giro dopo e `ultimo-giro.txt` e' una
redirezione di chi lancia, quindi **dei giri passati non restava niente**. Ogni
giro aggiunge ora una riga a `Banco/storico.txt` con le prove fallite **per
nome**, perche' la domanda a quel file e' sempre «quante volte e' successo a
QUELLA?». Non versionato: e' il registro di questa macchina.

### F13 — Un gruppo di prove alla volta, mentre si lavora

`--gruppo <nome>[,<nome>]` lancia una famiglia sola. `--gruppi` li elenca. Un
nome sbagliato viene rifiutato **prima di agganciare SOLIDWORKS**, con l'elenco
dei nomi veri e uscita 2.

```
giro intero              ~900 s
--gruppo viste            6,6 s
```

Il 15/9 il banco e' stato lanciato una dozzina di volte: **circa tre ore e mezza
di SOLIDWORKS**, quasi tutte per riprovare cento cose non toccate.

**Il giro intero resta il predefinito**, perche' «provo solo quella che ho
cambiato» non funziona e lo stesso giorno l'ha dimostrato due volte: tre numeri
aggiunti alle facce dei fori hanno fatto sforare il tetto dei 16 KB **su un
telaio, che fori non ne ha**, e il ritaglio delle immagini ha cambiato
un'asserzione sul peso del PNG. *Il banco misura cosa hai rotto, non cosa hai
fatto.*

Per questo un giro parziale **non puo' passare per uno intero** — a schermo e
nell'ultima riga di `in-corso.txt`, che e' quella che si legge col grep:

```
NON LANCIATI — 23 gruppi su 24: sezioni, archivio, telai, ...
il giro intero si lancia senza «--gruppo», ed e' quello che vale prima di committare.
GIRO PARZIALE (solo viste, 23 gruppi non lanciati) — 6 prove, 0 fallite
```

### E il difetto che i gruppi introducono

Una prova nuova che nessuno mette nella tabella non gira **e non lo dice**:
stessa dimenticanza di prima — quando l'elenco era `NuoveOperazioni()` — e
stesso silenzio. `ProvaCheLaTabellaCopreTutto` chiede a .NET quali metodi
`Prova…()` esistono davvero e li confronta con la tabella, sui `MethodInfo` e
non sui nomi scritti a mano.

**Visto scattare**, aggiungendo un metodo che la tabella non nomina:

```
  OK   F13 ROSSO: il setaccio vede le famiglie di prove    17 metodi «Prova…()»
  NO   F13: nessuna famiglia di prove fuori dalla tabella  ProvaDimenticataApposta_TEMPORANEA
```

Per lo stesso motivo le azioni di un gruppo sono un `Action[]` e non una lambda
che le chiama: una lambda nasconderebbe i metodi veri dietro il suo, e il
controllo cercherebbe fra i metodi generati dal compilatore dicendo di si' a
tutto.

**Il ponte e la libreria non cambiano**, e per questo `VERSION.txt` resta a
0.40.0: un numero che si alza senza che sia cambiato cio' che numera dice il
falso a chi lo legge per sapere «con quale versione».

---

## 0.40.0 — 15 settembre 2026 · libreria 0.75.0

### E10 — La frazione dell'ingombro: come SPIEGAZIONE, mai come identita'

Quando una faccia si sposta perche' il pezzo e' cresciuto, i messaggi dicevano
«spostata di 60 mm» — vero, e non spiega niente. Adesso dicono anche **dove
stava rispetto al pezzo**:

```
«F6» adesso indica un'altra parte del pezzo (spostato di 60.00 mm). Il pezzo
e' cambiato da quando hai confermato: non lavoro sulla faccia sbagliata.
Per quel che vale: «F6» stava al 100% del lato X e ci sta ancora — il lato e'
passato da 100 a 160 mm. Puo' essere la stessa faccia cresciuta col pezzo, ma
questo non lo decido io: l'identita' si misura in millimetri.
```

La stessa frase si attacca al messaggio dell'altra strada, quella in cui la
faccia **viene ritrovata** dal riferimento persistente: e' il caso A della voce
E2, ed e' il caso per cui questa voce esisteva.

**E il criterio di chiusura vero e' un grep che deve tornare vuoto.** Come
CRITERIO DI IDENTITA' la frazione e' l'unica delle strade proposte che converte
un «non lo so» in un «si'» sbagliato: un foro a 20 mm dal bordo, su una piastra
che passa da 100 a 300, **resta a 20 mm dal bordo** — e' cio' che significa in
officina — mentre la frazione lo manda a 60, dove una faccia la trova, e
**conferma invece di rifiutare**. In piu' l'ingombro e' instabile: un rialzo in
un angolo cambia il denominatore di tutte le facce insieme.

Per questo il conto sta in **un posto solo** (`Frasi.DoveStavaEDoveSta`), torna
`string` e non `double`, e `FirmaCombacia` e il ciclo di ritrovamento non lo
nominano. Una prova del banco **legge il sorgente** e lo verifica, con il suo
rosso: lo stesso setaccio passato su `Frasi.cs`, dove quelle parole ci sono,
deve trovarle.

### Lo stato dice anche DOVE sta la scatola

`ingombro_da_mm` — tre numeri, l'angolo minimo del pezzo. `ingombro_mm` diceva
«140 x 16 x 140» e non diceva dove fosse quel volume: 50 su un lato lungo 100
vuol dire meta' se la scatola parte da zero e **tutto** se parte da −50. Senza
l'angolo la spiegazione di sopra non si puo' nemmeno scrivere, e chi vuole
mettere un foro «a 20 mm dal bordo» doveva indovinare se il pezzo fosse
centrato sull'origine o appoggiato a essa.

Una trentina di caratteri sul tetto di 16.000.

---

## 0.39.0 — 15 settembre 2026 · libreria 0.74.0

### E14 — `faccia_del_foro` restituiva un raccordo, e non lo diceva

`Riferimenti` cercava le facce **cilindriche** lasciate da un passo: una sola la
restituiva, piu' d'una faceva scattare un rifiuto. **Ma un raccordo lascia facce
cilindriche come un foro.** Un passo che ne lasciava esattamente una, e quella
era un raccordo, la restituiva **senza dire niente**: chi aveva chiesto la
parete di un foro si ritrovava la superficie di un raccordo, e la lavorazione
successiva ci finiva sopra.

Il criterio e' geometrico, e **misurato** — `Controlli\ForoOppureRaccordo`, un
attrezzo nuovo che costruisce una piastra con un foro Ø12 e un raccordo R5 e
chiede all'API:

```
parete del foro          FaceInSurfaceSense = true    (concava)
superficie del raccordo  FaceInSurfaceSense = false   (convessa)
```

La parete di un foro ha il materiale **fuori** dal cilindro. Nel dubbio si
risponde «non e' un foro»: scambiare un raccordo per un foro fa finire una
lavorazione sulla faccia sbagliata senza che niente fallisca, il contrario fa
uscire un rifiuto, che si legge.

### E la voce proponeva il rimedio sbagliato

E14 chiedeva di **scegliere con un criterio** — «il diametro, oppure la
posizione» — invece di rifiutare quando le pareti sono piu' d'una. Fra quattro
M10 identici **non esiste nessun criterio**: qualunque scelta e' indovinare, e
il passo seguente finirebbe su un foro a caso senza che niente fallisca. E'
esattamente il difetto che questo progetto esiste per rendere impossibile,
offerto come comodita'.

Il rifiuto resta, e adesso dice **quante sono e di che diametro**: chi legge ha
gia' in mano cio' che gli serve per nominarne uno.

### Tre rossi sbagliati prima di quello buono

La prova (`Banco/prova_faccia_foro.js`) e' costata piu' della riparazione, e
ogni tentativo scattava **per il motivo sbagliato** — cioe' sembrava un rosso
che funziona:

1. **nel banco**: `faccia_rif` nomina un passo dello **stesso piano**, e il
   banco esegue un'operazione per volta. Rifiutava con «di quel passo non ho la
   geometria», senza mai arrivare al codice da provare;
2. **su `taglio_cilindrico`**: quel genere non lo accetta nemmeno. Il ruolo
   `faccia_del_foro` e' raggiungibile da **una sola** operazione, `guscio` —
   verificato sul catalogo, non ricordato;
3. **con `specchia`**: il passo dello specchio lascia **una sola** parete nuova,
   perche' l'originale appartiene al passo di prima. Prenderla e' giusto.

Quello buono e' `foro_guidato`, che lo dice da solo nello schema: «piu' fori
uguali stanno in una chiamata sola».

> Un caso di prova non deve dipendere da geometria che il caso stesso sta
> creando, e «quale operazione lascia N facce» si legge nel catalogo invece di
> dedurlo.

---

## 0.38.0 — 15 settembre 2026 · libreria 0.73.0

### V6 — il fotogramma si adatta al pezzo, e costa meno

Dopo l'adattamento automatico il pezzo riempiva il **58% in larghezza e l'89% in
altezza**. Sembra un'inquadratura da stringere, e non lo e': **il verticale e'
il vincolo**. `ViewZoomtofit2` scala finche' il pezzo ci sta in tutte e due le
direzioni, e quella che tocca per prima finisce a ~90%; l'altra resta indietro
di quanto la forma del pezzo differisce da quella del fotogramma.

Uno zoom non lo puo' riparare, perche' agisce sulle due direzioni **insieme** —
provato in V5 con 1,35, e il pezzo usciva di sopra e di sotto per guadagnare
larghezza.

> Non si adatta il pezzo al fotogramma: si adatta **il fotogramma al pezzo**.

Il PNG si ritaglia attorno alla scatola dei pixel che non sono sfondo, piu' un
margine del 4% e mai meno di dodici pixel — la prova considera «tagliato» un
pezzo a tre pixel dal bordo, e un margine sotto la sua franchigia farebbe
fallire un'immagine perfetta.

```
staffa a L    isometrica  92% x 93%     piastra  isometrica  91% x 93%
barra 300x20  isometrica  93% x 89%     barra    destra      93% x 93%

Nessuna vista tagliata.       (12 su 12, criterio della voce: 75%)
```

**E costa meno, non di piu':**

| | prima | adesso |
|---|---|---|
| isometrica della flangia | 97 kB | **24 kB** |
| isometriche delle cinque forme | 71-115 kB | **24-44 kB** |

Meno pixel **e** piu' pezzo dentro. Un'immagine costa piu' di mille token, e
quelli erano token pagati per non mostrare niente.

**Non puo' tagliare il pezzo, per costruzione:** il ritaglio si fa sulla scatola
dei pixel del pezzo, quindi tagliarne via uno vorrebbe dire che quel pixel non
era pezzo — cioe' che la soglia mente. Ed e' la stessa soglia che
`ProvaInquadratura` prova su un **documento vuoto**, dove il conto deve fare
zero.

`LockBits` una riga per volta con `Marshal.Copy`, e non un blocco `unsafe`:
accenderlo cambierebbe il modo in cui si compila **tutta la libreria**, ed e' un
impegno piu' grosso di quanto valga un ritaglio.

**Con questa, le sezioni R e V sono chiuse tutte e due.** Erano nate lo stesso
giorno dalla stessa dimenticanza — il goal dice *proponi, fai, guarda,
correggi*, e il file aveva solo le prime due.

---

## Fuori numero — 15 settembre 2026 · nel monorepo, non qui

### B6 — la libreria entra nel monorepo come sottomodulo

Il commit sta in `OpenPDM` (`<commit del monorepo>`), non in questo repository: qui non
cambia una riga. Lo si racconta qui perche' la voce B6 vive in questo TODO, e
perche' il clone di prova ha scoperto una cosa che valeva la pena sapere.

`OpenPDMSwAddin.csproj` puntava alla libreria con un percorso **assoluto**,
`S:\PDM\SolidWorksMcp\...`. Adesso e' un sottomodulo in
`client-windows/esterni/riscontro-disegno` con percorso relativo.

**Cosa ha scoperto il clone di prova.** Prima del commit, un clone pulito del
monorepo in una cartella nuova compilava **lo stesso** — andando a prendere la
libreria da `S:\PDM\SolidWorksMcp`, cioe' dalla copia di lavoro di questa
macchina, qualunque cosa ci fosse dentro in quel momento.

Su questa postazione il percorso assoluto non falliva mai. **Falliva solo
altrove, e qui mentiva**: il difetto che non da' errore nella forma piu'
difficile da vedere, quella in cui la macchina di chi sviluppa e' l'unica in cui
non si manifesta.

**Il criterio, verificato:** senza `git submodule update --init` esce **un
errore solo**, che nomina il sottomodulo e il comando — nessun CS0246. Dopo
l'init, la libreria si compila da dentro il clone e l'add-in esce **0**.

**E il difetto che la voce prevedeva** — «sottomodulo fermo a uno sha vecchio,
add-in e ponte contro due librerie diverse, senza che nulla fallisca» — non si
puo' impedire, ma adesso si vede: ogni compilazione stampa contro quale
libreria sta compilando.

---

## 0.37.0 — 15 settembre 2026 · libreria 0.72.0

### V4 — lo spaccato: guardare dentro senza toccare il pezzo

`guarda_il_pezzo` accetta `sezione: {piano: "destro", offset_mm: 0}` e risponde
con il pezzo **tagliato**. Uno spaccato dice di un guscio piu' di dieci viste
esterne: dall'esterno una scatola svuotata e una piena sono lo stesso
parallelepipedo, e **lo spessore del fondo non si legge affatto**.

**Non passa da `RunCommand`.** Il comando di menu «Vista in sezione» apre un
pannello e resta li' ad aspettare: una chiamata COM che apre un modale **non
torna** — la trappola di `AddDimension`, gia' pagata una volta.
`IModelViewManager.CreateSectionView` risponde un bool e non chiede niente a
nessuno.

**E non tocca il pezzo davvero.** `GraphicsOnlySection = true` taglia il
**disegno a schermo**, non la geometria: nessuna feature nell'albero, nessun
corpo diviso, niente da annullare. La sezione si toglie con
`RemoveSectionView()` nel `finally`, insieme all'orientamento — lasciarla accesa
sarebbe lo stesso danno del pezzo lasciato girato.

Misurato da `Banco/prova_sezione.js`, dopo lo scatto: **volume 109056, corpi 1,
lavorazioni 2** — identici a prima.

#### Il rosso: una sezione che non seziona

`prova_sezione.js` confronta i byte delle due immagini. Una sezione che non
taglia niente darebbe un'immagine **identica** alla vista intera, e «la sezione
esce» passerebbe lo stesso.

#### E si guarda DI FRONTE, non in isometrica — misurato

Il primo giro fotografava la sezione nell'orientamento corrente. Due modelli
isolati, a cui e' stato chiesto quante volte lo spessore sta nell'altezza della
scatola — il vero e' **12,5**:

| | parete | fondo | fiducia dichiarata |
|---|---|---|---|
| vista intera | 8 · 13 | **non leggibile** · **non leggibile** | 2/5 |
| sezione in **isometrica** | 6 | 8 | — |
| sezione **di fronte** | **12** | **12** | **5/5** |

Due cose insieme. La sezione **rivela il fondo**, che dall'esterno non si vedeva
proprio — tutti e due i giudici hanno risposto «non leggibile» guardando
l'esterno, ed e' il criterio della voce. Ma in isometrica i numeri erano
sbagliati del 40-50%, perche' **uno spessore sottile in scorcio sembra piu'
grosso**.

Adesso la vista si ricava dal piano — `destro` → si guarda da destra — e non si
puo' scegliere: chi chiede uno spaccato sul piano destro vuole guardarlo da
destra, e poterlo scegliere sarebbe un modo in piu' di sbagliare. Rimisurato:
**12 contro 12,5, il 4%**, e la fiducia dichiarata sale da 2/5 a **5/5** — il
modello e' piu' sicuro proprio dove ha ragione.

> Un'immagine in scorcio mostra una FORMA; solo una vista ortogonale porta un
> NUMERO. E' la riga di V2 — le immagini danno la forma, i numeri stanno nel
> testo — con l'unica eccezione che valga la pena avere.

---

## 0.36.0 — 15 settembre 2026 · libreria 0.71.0

Il profilo tornito nella frase (voce V7). Il contenuto e' descritto qui sotto,
nella 0.35.0, sotto «V7 — e poi sono diventati cinque su cinque»: le due voci
sono state scritte a poche ore di distanza e raccontano la stessa cosa, quindi
il racconto sta in un posto solo.

Qui il numero, che e' cambiato: **`prova_forma.js` passa da 4/5 a 5 su 5**, e il
banco da 105 a **107 prove, 0 fallite** — le due nuove sono il verde del profilo
sull'albero pulito e il **rosso sull'albero forato**, dove la frase deve tacere.

---

## 0.35.0 — 15 settembre 2026 · libreria 0.70.0

### V3 — la forma detta in una frase, e **4 pezzi su 5 si ridisegnano da quella**

Lo stato porta un campo nuovo, `forma`: la stessa geometria in una riga.

```
Ingombro 140 x 16 x 140 mm, volume 191938.7 mm3 (il 61% dell'ingombro),
1 foro Ø60 (centro 0,0,0), 6 fori Ø11 su una circonferenza Ø90 a passo 60 gradi.
```

**68-152 caratteri** sui cinque pezzi di prova, su un tetto di 16.000.

#### La misura, che e' piu' dura di quella di V2

La voce chiedeva che dalla **sola frase** un modello sapesse ridisegnare il
pezzo con le quote giuste. Cinque frasi date a un modello isolato — che ha
letto solo `catalogo.json`, nessuno stato, nessuna immagine — perche' scrivesse
cinque piani. I piani sono stati eseguiti e il volume confrontato con
l'originale:

```
pezzo 1 piastra_forata      volume 353214.2 contro 353214.2   (0.0%)
pezzo 2 guscio_aperto       volume 109056   contro 109056     (0.0%)
pezzo 3 staffa_a_L          volume 105600   contro 105600     (0.0%)
pezzo 4 flangia_forata      volume 191938.7 contro 191938.7   (0.0%)
pezzo 5 albero_a_gradini    volume 57962.4  contro 55857.5    (3.8%)
```

**Quattro su cinque al volume esatto**, non «vicino»: 0,0%. E il quinto e'
quello che il modello stesso **aveva dichiarato in anticipo** di non poter fare,
dicendo cosa mancava: «non dice quanti gradini ha il profilo, dove cade lo
scalino, ne' i diametri intermedi».

Un modello che sa dire in anticipo quale dei cinque non gli riuscira' e' un
risultato migliore di cinque su cinque ottenuti indovinando.

#### V7 — e poi sono diventati cinque su cinque

La frase adesso porta anche il profilo di un tornito:

```
pezzo di rivoluzione, profilo Ø30 per 40, Ø20 per 30, Ø34 per 20 mm
```

```
pezzo 5 albero_a_gradini    55857.5 contro 55857.5   (0.0%)    <- era 3.8%
```

**Dove sta la lunghezza di un tratto: nell'area.** Non e' scritta da nessuna
parte, ma un cilindro di diametro d e lunghezza L ha area **π·d·L**. L'ordine
viene dal `punto_mm` proiettato sull'asse — e' un punto sulla superficie e non
sull'asse (E16), ma per **ordinare** basta, perche' i tratti di un tornito non
si sovrappongono.

**E la formula e' fragile, quindi il profilo si dichiara solo se si
ricostruisce.** `area / (π·d)` vale su un cilindro **intero**: tagliato da un
piatto, da una cava o da un foro, l'area cala e la lunghezza esce corta — con la
forma giusta, quindi invisibile. Due controlli, e devono tornare tutti e due:
la somma delle lunghezze fa l'ingombro lungo l'asse, **e** il volume
ricostruito — Σ π(d/2)²·L — fa il volume vero. Se non tornano si tace, e resta
«pezzo di rivoluzione»: poco, ma vero.

**Il rosso e' nel banco:** lo stesso albero con un **foro assiale Ø10**. La
faccia cilindrica interna e' un tratto in piu' per la formula, la somma sfora
l'ingombro e il volume ricostruito conta materiale tolto. La prova chiede che la
frase dica «rivoluzione» e **non** dica «profilo». Senza, «il profilo esce
giusto sull'albero pulito» passerebbe anche se il controllo non ci fosse.

#### Il generatore ha inventato un fatto alla prima frase

Sulla piastra diceva: «4 fori Ø12 **su una circonferenza Ø90 a passo 90
gradi**». I fori stanno ai vertici di un rettangolo **160 x 90**.

La regola era una sola — «tutti alla stessa distanza da un asse» — e ne
servivano tre:

1. **stesso piano**: equidistanti da un asse vuol dire su un CILINDRO;
2. **stesso raggio**;
3. **passo angolare regolare**: i vertici di un rettangolo stanno su un cerchio,
   e i loro angoli sono 29,4° e 150,6° — non 90° l'uno dall'altro.

E' lo stesso difetto di E16, commesso dal codice scritto per ripararlo. Adesso
se una qualunque delle tre non torna la frase **non dice niente** e lascia i
centri: chi legge fa il conto suo su numeri veri.

#### Il nome della famiglia si scrive solo quando un TIPO lo autorizza

`Shell` dice «svuotato», `Revolution` dice «di rivoluzione»: sono cio' che il
progettista **ha fatto**. Non si dice «piastra» perche' una quota e' piccola
rispetto alle altre — sarebbe una soglia inventata, e il giorno che sbaglia chi
legge ci crede lo stesso, perche' la frase suona sicura come tutte le altre.

*(La premessa di V3 era che la frase servisse a far CAPIRE la forma. V2 aveva
gia' mostrato che col solo stato la famiglia esce giusta 5 volte su 5: quello
che serviva davvero era **dire i numeri derivati una volta sola e bene** —
centri, passo, spessore di parete — invece di lasciarli ricavare a ognuno.)*

### E17 — un angolo dichiarato in millimetri

Trovato guardando **perche'** l'albero a gradini fosse l'unico irrecuperabile:
l'unica quota che quel pezzo offriva era

```
"nome": "D1@Rivoluzione1", "mm": 6283.185
```

che e' **2π × 1000**. `SystemValue` torna tutto in unita' di sistema, e per un
angolo quelle sono i **radianti**; lo stato moltiplicava per mille qualunque
cosa — la conversione metri→mm — e la etichettava `mm`. Una quota di sei metri e
ventotto su un pezzo lungo novanta millimetri, e nessuno se ne accorgeva perche'
il campo si chiama `mm` e il numero e' un numero.

Adesso un angolo esce come **`"gradi": 360`**. Il nome del campo cambia con
l'unita': tenere `mm` e correggere solo il numero sarebbe stato **peggio di
prima** — un valore giusto sotto un'etichetta falsa non si puo' nemmeno
sospettare, mentre 6283 almeno saltava all'occhio.

**E l'enum giusto non era quello che sembrava.** `IDimension.GetType()` torna un
`int`, ma **non** `swDimensionType_e` (dove angolare vale 3): la prima
riparazione usava quello e non e' scattata. Misurato mettendo il numero nello
stato e guardandolo — angolo `1`, lineari `0` — cioe'
`swDimensionParamType_e`. Due enum quasi omonimi per due cose diverse, e il
compilatore non puo' dire niente perche' tutti e due sono `int`.

---

## 0.34.0 — 15 settembre 2026 · libreria 0.69.0

### R2 — si puo' togliere una lavorazione in mezzo, non solo l'ultimo piano

Le operazioni passano da **36 a 38**: `sopprimi_lavorazione` (con `riattiva`
per riaccenderla) ed `elimina_lavorazione`.

`annulla` (R1) disfa **un piano**, tutto intero e solo l'ultimo.
`modifica_quota` cambia un numero, non ritira una scelta. In mezzo non c'era
niente: una lavorazione sbagliata al terzo passo di dieci si poteva solo
subire.

**Il criterio della voce, alla lettera.** Su un pezzo con tre fori, sopprimere
il terzo riporta il volume al valore che aveva con due — misurato prima di fare
il terzo, quindi un'uguaglianza e non una stima:

```
il volume torna a quello con due fori     393842,478 contro 393842,478
lo stato la mostra soppressa              soppressa: true su Taglio-Estrusione3
riattivarla rimette il volume com'era     390763,718
eliminare un foro senza figli riesce      e non e' piu' nell'elenco
```

### La parte che conta e' il rifiuto

| caso | cosa fa |
|---|---|
| una lavorazione da cui ne dipendono altre | `elimina` **rifiuta**, e **dice quali sono** |
| un nome che non c'e' | rifiuta, e dice quali ci sono |
| sopprimere cio' che e' gia' spento | lo dice, invece di rispondere «fatto» |

**I figli fermano `elimina` e non `sopprimi`, ed e' voluto.** Sopprimere un
padre spegne i figli e li riaccende insieme a lui: e' quasi sempre cio' che si
vuole («togli il foro e i suoi raccordi»). Cancellare li farebbe **sparire**, e
SOLIDWORKS lo fa senza chiedere.

**E dopo si controlla che sia sparita davvero.** `DeleteSelection2` risponde
`true`; la domanda e' se quel nome nell'albero non c'e' piu'. E' la differenza
fra «ha detto di averlo fatto» e «l'ha fatto».

**Gli identificativi cambiano, e la risposta lo dice sempre.** Togliere una
lavorazione fa sparire delle facce, e gli uid sono posizionali: chi ha in mano
un piano scritto su «F3» dopo questo non ha piu' «F3».

`RiuscitoSenzaCreare` invece di `Riuscito`: quest'ultimo scrive nel registro
«creata feature X», e un'operazione che lo dice mentre ne cancella una lascia
una frase falsa. Nome nuovo e non un parametro in piu' — `Riuscito` e' il punto
da cui passa **ogni** successo del progetto, il posto peggiore in cui provare a
cambiare una firma.

### Un rosso SALTATO non e' un rosso passato

La prima scrittura della prova costruiva un raccordo sul terzo foro per dargli
un figlio. Il raccordo non e' venuto («Nessuno spigolo indicato»), e la prova e'
finita **SALTATA**: il controllo per cui esiste tutta la voce non e' mai
scattato, e il giro diceva «104 prove, 0 fallite».

E' esattamente il caso per cui il banco stampa le saltate a parte e le conta.
Rifatto sul genitore naturale — **l'estrusione di base**, che e' il caso che la
voce chiama pericoloso per nome: i tre fori sono suoi figli.

---

## 0.33.0 — 15 settembre 2026 · libreria 0.68.0

### V5 — le viste non escono piu' tagliate

**Il difetto.** `ViewZoomtofit2` inquadra dentro l'**area grafica** di
SOLIDWORKS; `SaveBMP` rende poi nel fotogramma che gli si chiede. Se le due
forme non coincidono, quello che avanza viene tagliato via — e non lo dice
nessuno.

```
area grafica  1845 x 924  ->  1,997
chiesto       1024 x 768  ->  1,333
```

Sette viste su dodici uscivano dal bordo **destro**: sempre a destra, mai in
verticale, mai a sinistra.

**A trovarlo non sono stato io.** Due modelli se ne erano accorti da soli
durante la misura di V2, mentre rispondevano a un'altra domanda: «la terza
dimensione non e' determinabile perche' **esce dall'inquadratura**», «la vista
risulta **tagliata sul lato destro**».

**La riparazione.** Il fotogramma non impone piu' la sua forma: prende quella
dell'area grafica, dentro le misure massime chieste. Con 1024x768 chiesti e
un'area 2:1 escono 1024x513 — piu' piccolo, e **intero**. Larghezza e altezza
passate restano un tetto, non una pretesa.

**E poi si stringe di 1,15.** L'adattamento automatico lasciava il pezzo al
~50% della larghezza: meta' dei pixel di un'immagine che costa piu' di mille
token non mostrava niente. Il fattore non e' scelto a gusto — **il tetto lo
detta la direzione gia' piena**: in verticale si stava gia' all'**78%**, e
provando 1,35 la staffa a L e la piastra uscivano dal bordo di sopra e di
sotto. Si guadagnava larghezza tagliando altezza.

Dopo: **zero viste tagliate su dodici**, riempimento da ~50% a **58-89%**.

Uno zoom uniforme non puo' fare di meglio: i due margini sono diversi e il
fattore e' uno solo. Inquadrare bene per davvero vuol dire proiettare
l'ingombro del pezzo nella vista e adattare le due direzioni per conto loro —
un'altra voce, non una costante da alzare.

### `Controlli\ProvaInquadratura`, e il righello che si prova da solo

Scatta e conta i **pixel**: la scatola di quelli che non sono sfondo tocca un
bordo? quanto riempie? Tre forme scelte per essere scomode in modi diversi
(una staffa lunga e bassa, una piastra larga e piatta, una barra 15:1); un
pezzo cubico non direbbe niente perche' ci sta sempre.

**Due soglie sbagliate prima di quella buona**, e tutte e due dichiaravano
tagliate dodici viste su dodici **con la stessa scatola su pezzi di forma
diversa** — che e' il segno che una misura non sta misurando il suo ingresso.
A smascherarle e' stato guardare il fotogramma: su una barra che sta fra y 358
e 410, il righello diceva y 5-443.

Adesso lo strumento ha **un caso rosso suo**: fotografa un documento **vuoto** e
il conto dei pixel di pezzo deve fare **zero**. Costa uno scatto, e le due
soglie sbagliate sarebbero morte li' invece che dopo tre giri e due letture a
occhio.

E quando dice «tagliata» **lascia il fotogramma su disco**: uno strumento che
emette un verdetto deve far vedere su cosa.

**I due criteri non sono dello stesso genere, e l'esito dipende solo dal
primo.** Tagliare e' un difetto — un pezzo tagliato e' un pezzo diverso.
Riempire poco e' un costo: si stampa sempre, e non fa fallire niente.

---

## 0.32.0 — 15 settembre 2026 · libreria 0.67.0

**La misura di V2 stava misurando un'altra cosa, e ha trovato questa.**

### E16 — `punto_mm` di un foro non e' dove sta il foro

Due modelli, ognuno per conto suo e senza vedere le risposte dell'altro, hanno
letto lo stato di una flangia con **sei fori su una circonferenza Ø90** e hanno
risposto **Ø79**, tutti e due con **confidenza 5 su 5**. Sulla stessa piastra
l'interasse 160 e' uscito 149,5.

79 = 90 − 11: avevano misurato fra i **bordi vicini** dei fori invece che fra
gli assi. Non per distrazione — perche' era l'unica cosa che lo stato dava.

**Cosa c'era, di una faccia cilindrica:**

| campo | cos'e' davvero |
|---|---|
| `punto_mm` | un punto **sulla superficie**, a un raggio dall'asse in una direzione che nessuno dichiara. Serve a SELEZIONARE |
| `asse` | una **direzione** (`[0,1,0]`), non una posizione |
| `normale` | `null`, perche' la faccia e' curva — quindi nemmeno la direzione in cui correggere |

Il centro non era ricavabile da nessuna combinazione di quei tre.

**E il dato era gia' letto, e buttato via.** `CylinderParams` torna sette
numeri — **punto (0-2)**, asse (3-5), raggio (6) — e `CilindroDi` leggeva
`par[3..6]` scartando `par[0..2]`, cioe' l'unica cosa che dice **dove**.

E' la terza volta che questo progetto trova la stessa forma: una misura presa e
gettata (E8, la sezione V, e adesso questa).

**Adesso** le facce cilindriche portano `centro_mm`, un punto sull'asse. Si
chiama cosi' e non `asse_mm` perche' `asse` accanto e' gia' la direzione: due
campi quasi omonimi che vogliono dire cose diverse sono il modo piu' sicuro di
farli scambiare.

**Solo sulle facce che vengono da una lavorazione, e l'ha deciso il tetto.** La
prima scrittura lo metteva su tutte le facce cilindriche, e **il banco ha
fermato il commit**: un telaio da quattro barre e' passato da 15.633 a
**16.163** caratteri, oltre il tetto di 16.000, con un solo documento aperto.
Le facce cilindriche di un telaio tubolare sono i raccordi d'angolo dei
profili — decine, e fra i loro assi non misura un interasse nessuno. Quelle che
contano sono le facce che vengono da una lavorazione **diversa** da quella che
ha fatto il corpo: fori, tasche, svasature, cioe' le cose che si posizionano.
Stessa regola di `da_lavorazione`. Sulla flangia a sette fori: **+230
caratteri**.

Non copre una faccia cilindrica che *e'* il corpo — l'interno di un tubo. Se
servira', e' una voce nuova.

**Il verde** e' stato chiesto a un modello nuovo, a cui non e' stato detto che
`centro_mm` esistesse: ha risposto Ø90 e interasse 160x90, e ha dichiarato da
solo di aver usato `centro_mm`. E' il modo in cui si verifica se un campo nuovo
e' *leggibile* invece che soltanto *presente*.

### V2 — quante viste servono, e la risposta non e' quella che ci aspettavamo

Cinque forme, quattro condizioni, quattro modelli **isolati**. Criterio scritto
**prima** di guardare le risposte (`Banco/casi/viste/COME_SI_CONTA.md`).

| condizione | FAMIGLIA | LAVORAZIONI | QUOTE | **INVENZIONI** |
|---|---|---|---|---|
| solo isometrica | **5/5** | 3/5 | 0/5 | **0** |
| quattro viste | **5/5** | 4/5 | 0/5 | **0** |
| solo stato *(il ponte prima della 0.31.0)* | **5/5** | **5/5** | **5/5** | **3** |
| stato + isometrica *(adesso)* | **5/5** | **5/5** | **5/5** | **1** |

**Col solo stato testuale la famiglia esce giusta 5 volte su 5.** «Scatola a
pareti sottili aperta su un lato», «albero di rivoluzione a gradini», «flangia
circolare forata»: tutte prese senza vedere niente. La premessa della sezione V
— «il modello non capisce la forma» — era **troppo forte**.

Il contributo dell'immagine e' un altro:

> Con il solo stato il modello ha prodotto **tre** numeri sbagliati, tutti con
> confidenza 5 su 5. Con lo stato piu' l'isometrica, **uno**.

E in dieci risposte su dieci basate solo su immagini, il modello ha scritto
«non stimabile in mm assoluti, nessun riferimento di scala»: **zero numeri
inventati**, e proporzioni giuste (rapporto lati 1,6-1,7:1 contro 1,67 vero).

Un modello, un giro per condizione, cinque forme scelte da chi ha scritto la
misura: su FAMIGLIA e QUOTE il divario e' netto, su INVENZIONI **3 contro 1 e'
un solo giro**, e un solo giro non e' una tendenza.

### V5 aperta: le tre viste standard escono TAGLIATE

Il rosso l'ha scritto un modello, mentre rispondeva ad altro: «la terza
dimensione non e' determinabile perche' **esce dall'inquadratura**», «la vista
risulta **tagliata sul lato destro**». Guardata a occhio: nella frontale della
staffa il pezzo comincia a 300 px da sinistra ed esce dal bordo destro.

L'isometrica e' intera e centrata — ed e' quella che il ponte consiglia. Per
questo il difetto e' passato, e per questo il confronto «quattro viste contro
una» qui sopra e' **viziato a sfavore delle quattro**.

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
