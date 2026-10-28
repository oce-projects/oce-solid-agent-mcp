# The bench

The tests that run **inside SOLIDWORKS**, against the real library.

There are several benches, for different questions:

| | question | needs SOLIDWORKS? |
|---|---|---|
| `Oce.Solid.Agent.Bench.exe` | do the library's operations make the right part? | yes |
| `run_plans.js` | does the MCP bridge run plans the way it should? | yes |
| `test_undo.js` | does `undo` withdraw the plan, and does it **refuse** when it should? | yes |
| `collect_views.js` | builds the five shapes of V2/V3 and saves their images and state | yes |
| `test_shape.js` | from the **sentence** alone, can a model redraw the part? (V3) | yes |
| `test_section.js` | does the **cutaway** show the wall, and does it switch itself off? (V4) | yes |
| `Checks\GearTest`, `OutlineTest`, `PositionsTest` | does the maths come out right? | **no** |

The first ones measure parts, the last ones measure formulas. The last ones run
in a second and should be run first: if the formula is wrong, it is not worth
opening the CAD.

---

## `Oce.Solid.Agent.Bench.exe` — the operations

It loads `Oce.Solid.Agent.Core.dll` and calls its types by reflection, with the
same arguments that would arrive from a plan. Every test works on a throw-away
part and closes it.

> **Before all this there is `Tests\Oce.Solid.Agent.ContractTests.exe`** — hundreds of checks (the exact count is in Tests/README.md) in
> a few seconds, **without SOLIDWORKS**: the expectation evaluator, the catalogue,
> `SameDocument`, the MCP protocol. See [Tests/README.md](../Tests/README.md).
> If those are red, opening the CAD is wasted time.

### One command

```
.\Bench\bench.ps1                        everything, and writes the report
.\Bench\bench.ps1 -Group views          a single family
.\Bench\bench.ps1 -Family reanchor    a single folder of declared cases
.\Bench\bench.ps1 -Case a,b           single declared cases, by file name (twins come along)
.\Bench\bench.ps1 -SolidWorksLanguage en   the same cases, SOLIDWORKS writing English names
.\Bench\bench.ps1 -Slow                 also the ones that cost minutes
.\Bench\bench.ps1 -SkipCompilation     the binary that is already there
```

It compiles the library and the bench (and **stops** if either fails), checks
whether SOLIDWORKS can be attached to, **closes the documents left open**, runs,
and writes `Bench/report/YYYYMMDD_hhmm.md` and `.json` plus `last.json`.

| exit code | |
|---|---|
| **0** | all green |
| **1** | failed tests — the `.md` lists them with expected and measured values |
| **2** | bench error: the build failed, SOLIDWORKS cannot be attached to, a group that does not exist, or a case that **could not be measured** |
| **3** | the desk was not clean: documents that are not the bench's were already open in SOLIDWORKS (entry K12). The run does not start at all |
| **4** | SOLIDWORKS went away mid-run (entry K17, see below). The remaining checks are skipped, not red, and the run is not valid |

The 2 **is not a red test** and must not be counted with one: a red test is a
measurement, a bench error is a **missed** measurement. And the 2 wins over the
1: if the bench could not measure, how many tests failed is a question that has
not been answered. The 4 wins over both.

**The test ledger, `Bench/report/tests.json`** (W3.1-bis.2, item 7), is
written by the bench itself at the end of every run (full, partial, mutations,
regression) and is not tracked. One line per test, named by where it is
recorded: `File.cs:Method:ordinal`, the ordinal counting the recording calls of
that method in source order, then `@case` while a case runs and `#2`, `#3` when
the same call records again for the same case. One line per case, with its
outcome and the expectations attempted and checked. The label is kept for the
reader, but the identity does not depend on it, so a translated label is still
the same test. The run checks that every identity resolved against the sources
it was built from (a test of its own, before the summary line); a source edited
after the build leaves the test unresolved and that check red.

**The lookups by name that fall back to null** (W3.1-bis.2, item 3) go through
`Bench/ReflectionLog.cs`: the schemas of the mutations, the catalogue count of
the header, the signatures of the state. A full or partial run ends with one
more test, «every lookup by name that falls back to null found its target»
(skipped with `--library`: an older library lacks newer names by design); in a
mutation or regression run a miss is a bench error. `Oce.Solid.Agent.ContractTests` forbids a
`?.` read of a lookup anywhere else in `Bench/`.

> ### ⚠ DO NOT RUN IT INSIDE A POWERSHELL PIPELINE
>
> `.\Bench\bench.ps1 ... | Select-Object -First 25` **kills the run half way**.
> `Select-Object -First N` throws `StopUpstreamCommandsException` as soon as it
> has N objects and **terminates the upstream command**: the bench dies, exits
> **255** — which is none of the codes above — and **does not write the
> report**. The same happens with `Tee-Object` on a long run, which crashes into
> the Windows error about the semaphore timeout period having expired.
>
> This has cost us twice: a full run dead at **8469 s** (644 tests passed, zero
> failed) and a `-Group cases` run dead at **1132 s**. In both cases **zero
> reds**: it is wasted time, not information.
>
> **The signal that tells them apart is simple: a real run ALWAYS writes a file
> in `Bench\report\`.** If the report is not there, the run did not happen —
> whatever the last line on screen says.
>
> Run it **bare**, and read afterwards: `Bench\report\last.json`, or
> `Bench\last-run.txt` — which is **UTF-16LE**, so a `grep` done by hand
> finds nothing in it and it looks as if the case never ran. *A log that cannot
> be read is a log that leads to the wrong conclusion.*

The last line of the run says **«expectations verified N of M»**, always, even
when the two numbers match. If they diverge, something is measuring nothing.

The clean-up is inside the command because it is not tidiness — see «Before and
after every run», further down: every state read walks through **all** the open
documents, and six too many make «state: under 16 KB» fail without the code
having changed.

### By hand, if needed

```
msbuild Bench\Oce.Solid.Agent.Bench.csproj -p:Configuration=Release
Bench\bin\Oce.Solid.Agent.Bench.exe --library Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll
```

Without `--library` it looks for `Oce.Solid.Agent.Core.dll` next to the
executable. It exits 0 if nothing fails.

### An expectation that EXPLAINS a red must be put BEFORE that red

The bench stops the case at the **first** red expectation (`DeclaredCases.cs`:
`if (!ok) return false`). It is not a defect: a step that has already gone wrong
no longer has a state to measure on, and carrying on would produce derived reds
that look like new defects.

**The consequence is surprising, and on 18/9/2026 it cost a run.** A case had a
diagnostic expectation — «which of the two faults is it?» — written **after**
the expectation that could fail. That one failed, the case stopped, and the
diagnosis **did not speak**: the report had the red and not its reason.

> *A witness queued up behind the defendant does not testify.*

So, when an expectation exists to explain why another one might fail, it goes
**above** it. And the first of all is always the same question: **could I
look?** An empty list because nothing could be read passes any
`does_not_include`, and looks like a measurement.

### One group only, while working

A whole run lasts **from an hour and three quarters to two hours** — measured on
18/9/2026: **6774 s** for 781 tests, and 7017 s for the `cases` group alone.
While you are writing something you cannot wait two hours to learn that you got
one line wrong:

> **This line said «13-17 minutes» until 18/9/2026, and it was true when it was
> written: 88 tests.** Now there are 781, and nobody had rechecked the number
> while the suite grew — a reconnaissance agent found it, noticing that the
> report and the README **did not agree**.
>
> It is the same defect that the README of the tests has already suffered once
> («it said 54 while 113 were running»), and there it was closed with a check
> that rereads the number. **Here there is no check**: the duration is not easy
> to watch over, because it changes with the machine.
> What can be done, and it is written here so that it counts: **the number that
> matters is in `report\last.json`, field `duration_s`, and that one does not
> age** — this line is an indication, that field is a measurement.

```
Bench\bin\Oce.Solid.Agent.Bench.exe --groups                  which groups there are
Bench\bin\Oce.Solid.Agent.Bench.exe --group views            only that one: 6.6 seconds
Bench\bin\Oce.Solid.Agent.Bench.exe --group table,cases    more than one, separated by commas
```

A wrong name is **refused before SOLIDWORKS is attached to**, and the refusal
lists the real names (exit 2).

**The whole run stays the default, and it is the one that counts before
committing.** «I'll only try the one I changed» does not work, and on 15/9/2026
it proved so twice: three numbers added to the hole faces pushed the 16 KB
ceiling over the limit **on a frame, which has no holes**, and the cropping of
the images changed an assertion about the weight of the PNG.

> The bench measures **what you have broken**, not what you have done. You
> already know that.

That is why a partial run **cannot pass for a whole run**: the summary — on
screen and in the last line of `in-progress.txt` — says so in plain words, and says
how many groups it did not run.

```
PARTIAL RUN (only views, 22 groups not launched) — 6 tests, 0 failed
```

### The cases that are files — `cases/features/`

A case is a JSON file with **the expected numbers next to every step**, and it
is run with `--group cases`. No line of C# is needed:

```json
{ "name": "internal_shell", "family": "features",
  "why": "Does «Outward» remove towards the outside or towards the inside? ...",
  "start": { "new_part": true },
  "steps": [
    { "operation": "shell",
      "arguments": { "faces_uid": ["@face(normal=front)"],
                     "thickness_mm": 5, "outward": false },
      "expect": [ { "what": "volume_mm3", "equal_to": 82500 },
                  { "what": "bounding_box_mm", "equal_to": [100, 60, 40] },
                  { "what": "measurements.facing", "equal_to": "inner" } ] } ] }
```

| | |
|---|---|
| `what` | no prefix = the bench measures it on the document; `state.…` = the add-in's state; `measurements.…` = what the operation reports |
| bench measures | `volume_mm3`, `bodies`, `bounding_box_mm`, `centers_x_mm`/`_y_`/`_z_`, `diameters_mm`, `center_of_mass_mm`, `last_plane_mm`, `edges_curved`, `axes_created`, `how_many_features`. **The list is not written by hand anywhere**: it is the list of the keys of whoever measures them, and the message «cannot measure …» reads it from there |
| `equal_to` | a number, a text, a list of numbers (with tolerance) or a list of TEXT (one by one, and the order counts) |
| `includes` / `does_not_include` | for the measures that are **sentences** (`state.shape`): a string or a list. They are not combined with `equal_to` |
| `tolerance` | **absolute**, in mm or mm³. Non-zero → `tolerance_why` is needed, otherwise the case is **broken** |
| selectors | `@face(normal=…)`, `@face(planar=false)`, `@face(area~=907.92)`, `@face(normal=front,x~=150)` (x, y, z are the point that the state publishes), `@edge(axis=Z,length~=100)`, `@edge(curved=true,n=1)`, `@feature(kind=…,n=2)`, `@dimension(feature=Sketch1,name=D1)`, `@dimension(angular=true)` (the only dimension in degrees, entry Z-C21.2) |
| before the step | `select_first`: uids or selectors left SELECTED when the step starts, as someone sitting in front of SOLIDWORKS would leave them (the STL that exported only the selected face). `hide_first`: bodies, or the body of a face, HIDDEN before the step (the hidden body that came out of the overhang check). Both are counted after they are done: a selection that did not succeed is a bench error, not a green (entry Z-C21.2) |
| `activate_before` | `{ "document": path, "configuration": name }`: a configuration of ANOTHER open document made active before the step, read back (ShowConfiguration2 answers false on the one already active, so its answer decides nothing). It is how a drawing step finds its model on a configuration other than the one the views show (Z-C21.4, L16-2); a document that is not open, or a configuration that does not become the active one, is a bench error |
| placeholders | `{PROFILES}` the library of welded profiles of this installation; `{WORK}` the folder a case may WRITE to (`Bench\work\`, ignored by git); `{DATA}` the versioned input files (`Bench\data\`); `{SOLIDWORKS}` the SOLIDWORKS installation folder, for the files that ship WITH it and are not versioned (the table templates; `SW_INSTALL_PATH` if it is not the default one, entry Z-C21.2). A case that names a path of this machine is not a test, it is a memory |
| `must_fail` | `message_includes` (mandatory), `volume_unchanged`, `no_extra_feature`, `twin`, `difference_is_the_part` (see «The pairs») |

**A key the bench does not read is refused** (W3.1-bis.2, item 4). Every place
of a case (the case, a step, an expectation, a must-fail block, an assembly, a
component, a regression case...) admits only the keys listed for it in
`Bench/case-keys.json`: the keys the loader reads and the prose keys it
documents (the free keys frozen on 30/9/2026 took the prefix `_` in window 1
of W3.1-bis.4, and the frozen list is empty). Any other key is a bench error before
anything is built, because the loader would skip it in silence and whatever it
asked for would never be checked. A new free note starts with `_` (for example
`_seen_red_once`): admitted everywhere, never read. The frozen list stays
empty. `Oce.Solid.Agent.ContractTests` (CaseKeysTests) applies the same rule to every case
in a second, so a key written wrong is found before the bench runs.

### A case that runs on an ASSEMBLY — `cases/assemblies/` (entry GA0)

Instead of `"start": { "new_part": true }` a case can ask for an assembly, and the
bench builds it: every component is a part made **with the same operations as the
cases**, saved in `Bench\work`, reopened and inserted.

```json
"assembly": {
  "components": [
    { "name": "base",
      "steps": [ { "operation": "extrude_rectangle", "arguments": { … } } ] },
    { "name": "cover", "to_insert": false,
      "position_mm": [0, 0, 30],
      "steps": [ … ] }
  ]
}
```

| | |
|---|---|
| `name` | goes into the file name: `<case>_<name>.sldprt` in `Bench\work`. The case names it with `{WORK}\<case>_<name>.sldprt` |
| `position_mm` | where the **centre of the bounding box** goes, not the origin. Absent = at the origin of the assembly. **It is not a mate**. `AddComponent5` positions the centre: a part with its origin on a face — anything extruded in one direction only — finds its origin shifted by half its size, and in the state it is read back as `origin_mm`. Ask for z=30 on a part 4 thick and the origin comes out at `[0, -2, 30]` |
| `to_insert` | absent or `true` = the preparation inserts it. **`false` = it builds it and opens it, but does NOT insert it**: it is for testing `insert_component`, otherwise one would measure the work of the preparation believing to measure the operation |
| `configurations` | next to `components`: `[{ "name": …, "suppressed": [component names], "excluded_from_bom": [component names] }]`, made in this order after the components are in (Z-C21.4, L16-2 and L16-3). Each new configuration becomes the active one, is set while active (suppression, exclusion from the BOM of that configuration only) and every setting is read back; the last one made stays active. No operation of the catalogue suppresses a component or excludes it from the BOM in one configuration |
| `components` inside a component | a SUB-ASSEMBLY instead of a part (Z-C21.5, L11-3 and L11-5): each child is built like a component (its own `name`, `steps`, `position_mm` inside the sub-assembly) as `<case>_<name>_<child>.sldprt`, inserted into a new assembly, saved as `<case>_<name>.sldasm`, and that is inserted like a part. One level only. In the state a nested component is named with its whole path (`<case>_<name>-1/<case>_<name>_<child>-1`), so it sorts right after its sub-assembly |
| a repeated child `name` | inside a sub-assembly, two children with the same `name` are two instances of ONE part, built and registered from the first; the later one brings only its `position_mm`, and `steps` of its own are a bench error (Z-C21.11, M23) |
| `envelope` | on a component: `true` makes it an envelope once inserted (Select4, then IAssemblyDoc.CompConfigProperties6 with IsEnvelope true, as the Z-C21.3 probe of 28/9/2026), read back with IsEnvelope. Top-level parts only, not with `to_insert` false, not on a child of a sub-assembly (Z-C21.11, N27) |
| `hidden` | on a component: `true` hides it (IComponent2.Visible = swComponentHidden), read back with IsHidden(false). Top level only, not with `to_insert` false, not on a child of a sub-assembly (Z-C21.11) |
| `reopen_lightweight` | next to `components`: `true` saves the assembly as `{WORK}\<case>.sldasm`, closes it and every document of its components (checked by path: nothing of the case stays loaded), reopens it with OpenDoc6 Silent, OverrideDefaultLoadLightweight and LoadLightweight (193), and checks GetLightWeightComponentCount > 0 and the top-level count. The case runs on the reopened document. It is the last step of the preparation, after `configurations`; not with any component at `to_insert` false (Z-C21.11, N5) |

The three new keys take only a JSON Boolean: any other value is a bench error,
never read as false, and so is every read-back that does not say what was asked.

**`AddComponent5` wants an already open document**: the preparation reopens
**all** the components, even the ones it does not insert. On a closed file the
insertion answers `null` without saying why.

At the end the files are **deleted**, and a check verifies it
(`--group test-assembly`): *a case that succeeds because a file from the
previous run was left behind is not a case, it is a memory.*

What fails during the preparation is a **bench error** (exit 2), not a red test:
the case wanted to measure an assembly and the assembly never existed — calling
it «failed» would send you looking for the defect in the assembly operation,
which was not even called.

**`does_not_include` is not the symmetric convenience of `includes`: it is the
reason `includes` is not enough.** A sentence that declares «the Ø30 by 40 profile» on a part
that no longer has that profile fails nothing — it is the right description of
ANOTHER part, and the model builds on it. It is caught only by demanding that a
word **disappears**.

**A selector that finds zero or more than one thing refuses**, and does not pick
the first: a case that runs on a face other than the one the file name promises
may return the same volume, and then it is green and proves nothing.
The only ambiguity that is resolved is `n=` of `@feature`, where the order is
decided by whoever writes the case.

**A selector on a face reads EVERY face of the part**, not the 60 the ordinary state
lists (W3.1-bis.12). The state lists the 60 largest faces and says `truncated`; a
selector such as `@face(diameter_mm~=3)` was resolved against that list, and a small
face of a part with more of them was «not found» although it was there. When the
arguments of a step name a face by selector and the state says `faces_total`, the bench
reads the state again with every face listed (`ReadFaces`, up to 500) and resolves
against that one; the step still runs on the ordinary state, because every face has
the identifier of its rank by area and the library finds an unlisted one by it. On a
library older than 0.153.0 there is no `ReadFaces`, the selector meets the list of 60,
and the case cannot be built: that is its red (`a_face_beyond_the_largest_sixty_is_named_by_selector`,
`red_before` 0.153.0). The group `--group faces-beyond-sixty` walks the same road through
the bridge on the mitred frame of the stand (72 faces): the ordinary reading lists 60,
`faces: {near_mm}` finds the two mitre faces at a corner (ranks above 60), a plan cites
one of them and measures 960 mm to the opposite side, the same identifier still works
after an ordinary reading, the identifier after the last face and a crooked filter are
refused. A part with more faces than one reading lists (500) is cut there too: a
selector that finds ONE face on a cut list is refused (the same face may be beyond the
cut), not taken; `n=` still works, the listing keeps the largest first.

Everything that prevents **measuring** is a bench error (exit 2), not a red
test: a field that is not in the state or in the measures (with a message that
says which ones there were), a case without `why`, a step **without
expectations**, a `must_fail` without `message_includes`, a tolerance without
its reason, a `twin` that does not exist, a selector that does not resolve.

A **declared** `null`, on the other hand, is measured: `material: null` means
«that place exists and is empty», which is a different thing from a field that
is missing.

### And when SOLIDWORKS goes away half way (exit 4)

Three new documents that are not born **in a row** are not three red tests:
they are SOLIDWORKS no longer being there. The bench notices, **skips** all the
rest saying for each one that it was not measured, and exits **4** — which wins
over the 2 and over the 1, because a run that was not done is not a run with
errors.

«In a row» is the word that counts: three failures scattered among eight hundred
checks are normal noise, and a guard that stopped healthy runs would be switched
off by the first person who saw it stop one.

To **see it fire** — which is the only way to believe it — there is
`--pretend-no-solidworks` («pretend there is no SOLIDWORKS»): it makes
`NewDocument` answer `null`, and in a few seconds you can read the run that
interrupts itself. The real condition happens after hours and cannot be
reproduced on demand (entry K17; on 23/9/2026 it cost an hour and a half of run
and nine right cases painted red).

What it does **not** cover: a SOLIDWORKS that hangs *inside* a call instead of
answering `null`. There no answer comes back to be counted: that is the leash
(entry K18), which stops the run with exit 5 when a call has not come back after
90 s. Since 27/10/2026, before stopping, it reads the processor time of SLDWORKS
twice five seconds apart (busy: the call is slow; idle: it waits for a window, a
lock, the network), says whether SOLIDWORKS responds and which windows it has
open, and writes the ten slowest calls of the run with the time of day; a whole
run writes them before the summary too. To see it fire: `--leash-ms 1500` with
one small `--group` (never on a full run: it stops it).

### What the run reads from the NETWORK, and what it does not (entry K19)

On this workstation `S:` is a network share, and the default document template
is on it. **Every new part would be an SMB read, and a run makes eight hundred.**
On 23/9/2026 three runs in a row died exactly there: SOLIDWORKS hung while
opening the `.prtdot` and kept hold of it, and from that moment **that file could
not be read any more even from outside** — neither by the next run, nor by the
next SOLIDWORKS. It looked like the network: the server answered ping in 0 ms
and the profiles folder, on the same share, could be listed without hesitation.

Since 23/9/2026 the bench **copies the template locally once per run** and says
so, with the source path and the destination path. If the copy fails it uses the
network one and **says that too**: a bench that silently uses a template
different from the workstation's is measuring something else.

**Network reads remain**, and they are worth knowing so that next time nobody
looks again in the wrong place:

```
the framing profiles       the folder named by OCE_WELDMENT_PROFILES. Without
                           the variable the bench stops at once and says so:
                           there is no default path, because every
                           workshop keeps its own library where it likes.
the material databases     where SOLIDWORKS keeps them by itself
the cases and the binaries wherever the clone is, which may be on the network
```

The one that was removed is the **most frequent**, not the only one.

### The pairs — `cases/pairs/`

Every case with `must_fail` **must** name in `twin` a case that succeeds,
and that twin must be **green in the same run**:

> A test that verifies only the refusal does not tell «the check works»
> from «the check refuses everything».

Four pairs, all on the same 314 x 274 x 52 block:

| refuses | succeeds | they differ by |
|---|---|---|
| 500 through hole | 270 hole on the 274 face | `diameter_mm` |
| 400x400 through pocket | the same, **blind** by 5 | `through_all` |
| hole at (5000, 5000) | 60 hole **with its centre on the edge** | `x_mm`, `y_mm` |
| 500x300 slot | 30x9 slot | `length_mm`, `width_mm` |

The bench says **how many arguments differ** and refuses only the two cases that
matter: a twin identical to the refusal (it is a copy) and one that does not even
have one argument in common (they are two operations that do not talk to each other).

**When the difference is the part and not an argument** (28/9/2026): the same
rib is born in the hollowed box and not on the plate without walls. No fake
argument is written to make the rule pass: it is declared in `must_fail`

```json
"difference_is_the_part": "the 40 x 40 x 2 plate has no walls; the twin is the 40 x 40 x 20 box hollowed from the top, ..."
```

and the bench and the contracts (`Expectations.ResembleInPart`) demand three
things: the sentence is there, the arguments are **identical** to those of the
twin (if they differ the normal rule is enough, and the declaration is
superfluous), and the steps **before** are different (the same part and the same
arguments remains a copy). Today only one case uses it:
`features/limit_rib_on_a_plate_without_walls`.

### `-Regression` — how well the references hold

```
.\Bench\bench.ps1 -Regression
```

Nineteen parts in `cases/regression/`, each with `build`, **a single
`modify`** and an `intent`. The result is **one of three things, not two**:

| | |
|---|---|
| **holds** | clean rebuild, and the geometry is where the intent declares |
| **detaches and says so** | the rebuild fails or a feature carries an error: the designer sees it |
| **detaches in silence** | rebuild fine, no error, **and the geometry is elsewhere** |

The third row is the only number that counts, and it is written **even when it
is zero**. The detail case by case goes into `report/regression.md`, the three
numbers into the README.

**The `intent` is mandatory**, and a case that does not declare it is a bench
error: a hole 20 mm from the edge, on a plate that gets wider, must stay 20
from the edge (`anchored_to_border`) or where it was (`anchored_to_absolute`)?
Without knowing that there is no right answer.

Two more fields than the normal cases:

* **`same_as_before: true`** on an expectation — the invariant is «as it was»,
  and it is measured before the modification. Working it out by hand would mean
  redoing a calculation that SOLIDWORKS has already done, and getting it wrong
  would create a false silence;
* **`center_of_mass_mm`** among the `what` — the position invariant for what
  leaves no holes. The volume alone does not tell a pocket that stayed put from
  a pocket that followed the edge.

### `-Mutate` — every case must be able to turn red

```
.\Bench\bench.ps1 -Mutate
```

It perturbs **the plan, not the code**: one argument at a time multiplied by
1.5 (booleans inverted), the case rerun, and it **demands that it turn red**. A
case that stays green under every perturbation is **blind**.

It is the only mechanical way to enforce that a test has already failed at
least once: a hand-written field is a declaration, and a declaration is copied
from the case next to it.

It costs **one SOLIDWORKS document per mutation** — 67 mutations, ~14 minutes —
so it is run before a delivery, not every evening. It writes
`Bench/report/mutations.md`, one line per case, and has a summary of its own
(`MUTATIONS — 11 cases, 0 blind`): the reds it produces are **sought**.

The mutations on arguments that the operation **ignores** (`depth_mm` on a
through cut) are skipped by reading the catalogue, not guessed; and only the
steps **with expectations** are mutated.

### The library of the time — `Checks\library-of-the-time.ps1`

A test really guards its defect only if, run against the code from **before**
the repair, it turns red.

```
.\Checks\library-of-the-time.ps1 0.70.0
.\Bench\bench.ps1 -Library <the path it printed> -Group cases -SkipCompilation
.\Checks\library-of-the-time.ps1 0.70.0 -Clean
```

The DLL is not kept anywhere: it is **rebuilt**, because in this repository a
commit is a version (`git log` on `AssemblyInfo.cs`, one line per number).

A case can declare **`red_before: "0.67.0"`**: on a library older than that,
that case **must** fail. If it passes, it was not guarding anything.

**The historical comparison starts at 0.52.0.** Before that the operations
lived in `OpenPDM.SwAddin` with another namespace: the bench does not find
them, says so and exits 2 — «it is not a red run, it is a library the bench
cannot read».

### The runs are counted — `history.txt`

`in-progress.txt` is rewritten by the next run, and `last-run.txt` is a
redirection by whoever launches: **nothing was left of past runs.** Since entry
F12 every run appends a line at the end of `Bench/history.txt`, with the failed
tests **by name**:

```
2026-09-15 08:53  library 0.75.0.0  117 tests, 0 failed, 1 SKIPPED (--slow runs them)
2026-09-15 09:11  library 0.75.0.0  117 tests, 1 failed  ->  null displacement (must fail)
```

It serves to answer one question, which without this file has no answer:
**«how many times has that one happened?»** — because a test that fails at
random and that nobody keeps count of stops being a test: the time it fails for
a real reason, it will be filed away as «the one that jumps now and then».

It is not versioned: it is the record of **this** machine.

**Dassault's libraries are not kept in here.** The csproj references them with
`Private=False`, so they do not even end up in `bin\`; at run time the one
INSTALLED on the machine is taken, which by definition is the same one that is
running. If SOLIDWORKS is in a non-standard place:
`set SW_INSTALL_PATH=D:\...\SOLIDWORKS` — **the root, not `api\redist`**. It
applies to building and to running, and `build.ps1` reads it too. The old
`SW_REDIST` (which wanted the subfolder) is still read afterwards, for whoever
already has it.

**The name says less than what it does.** It was called that when it tested only
welds; now it tests sections, materials, frames, holes, state and the new
operations. The name stayed because changing it costs more than it is worth.

---

## `run_plans.js` — the bridge

It sends a sequence of plans to the MCP bridge as a client would, and prints the
outcomes.

```
node Bench\run_plans.js Bench\cases\bridge\c4r_primary.json
node Bench\inspect.js                  read the state without sending a plan
node Bench\test_undo.js           «undo» (entry R1), with its three reds
```

`test_undo.js` is here and not in `Oce.Solid.Agent.Bench` for a precise reason:
`undo` **is not in the library**. It is a bridge tool, and the part that
counts — the memory of the last plan, and the three cases in which it REFUSES —
lives in `McpTools`. Testing it from the library would mean testing something
else.

Since Z-C21.14 four more `undo` cases run in the full bench, in the group that
talks to the bridge (`ClientPort.cs`, `UndoHonesty`): (a) a later plan that
only reorders two holes is the last plan, and `undo` must withdraw it and leave
the first plan; (b) a plan that ends with `create_drawing` leaves the drawing in
front, the part comes back with `document`/`activate`, and `undo` must withdraw
the plan from the part; (c) a `measure` after a plan does not stop `undo` from
withdrawing it (this case decides whether an empty undo recording stands in the
way); (d) a second bridge process reorders the holes of the first one's plan, and
the first one's `undo` must refuse before pressing.

### `test_shape.js` — a MEASUREMENT, not a check

It does not make any run fail, and the exit code is always zero. It answers a
question that does not have a green and a red but **a number**: how many of the
five parts are redrawn starting from the sole sentence that describes them
(entry V3).

It has to be redone in three steps, and the second one is not done by a machine:

```
node collect_views.js        builds the five real parts and saves the state,
                              which has the `shape` field inside
                              -- then the five sentences are given, AND NOTHING
                                 ELSE, to an isolated model, which writes the
                                 plans in cases/shape/1..5.json
node test_shape.js           runs the plans and compares volume and bounding box
```

**The model that writes the plans must not see `Bench/views/`**: in there are
the complete states, that is, the answers. If it sees them, the measurement no
longer measures anything.

### `test_section.js` — half automatic, half to be looked at

It builds a shell, takes the full view and the **cutaway** of it, and checks by
itself the things that can be counted:

- the section comes out and it is a PNG;
- **it is different from the full view** — the real red, because a section that
  does not section would give an identical image and «the section comes out»
  would pass all the same;
- afterwards, volume, bodies and features are those of before: **no section
  left on, no body cut**. It is the risk that entry V4 names.

The other half — «the thickness can be READ from the section» — can be said only
by someone who looks. The two images stay in `Bench/views/`
(`v4-shell-whole.png` and `v4-shell-section.png`) precisely for that.

With `OCE_BRIDGE=...` you point at another executable: it is for testing the
**installed** one instead of the one just built, which is the only way to know
whether the package really works and not only whether it compiles.

### Before and after every run

```
Checks\CloseLeftovers\bin\CloseLeftovers.exe --close
Checks\NewPart\bin\NewPart.exe
```

It is not cosmetic cleaning: **every state read walks through all the open
documents**, at 244 ms of `GetNext()` each. With fifty leftover parts on it, a
plan that is half a second of work takes eighty-five.

---

## The cases

**The declared cases** — the ones that `--group cases` runs and measures. The
list of the families is in `Families` in `DeclaredCases.cs`: a folder that is
there but is not in the list is not read, and one in the list that is not there
is a bench error.

```
cases/features/ 38   the shell, and which side it removes from;
                       the symmetric plate that grows on both sides
                       with edit_extrusion (revision of 27/9, Z-C20);
                       the dimension that pushes the slot out of the material,
                       refused and put back as it was (entry E18 c), and its twin;
                       the fillet on the four corners and the guided hole that
                       follows the ISO table (Z-C6: before, only in regression);
                       the ANCHOR (entry E18): the hole 20 from the edge that follows
                       the edge, the centred pocket that follows the centre line, on
                       the front, on the side and on the back; eight refusals and the
                       edge on the border of a chamfer; the guided holes
                       anchored 20 from the edges, and «all or none»;
                       the thickness below the fillet radius, refused and
                       put back (the mechanism of r05), and the twin that fits;
                       the order changes the part: the shell before or after the hole,
                       and the impossible move refused (Z-F3); the blind hole
                       that becomes through and the plate that grows (Z-F4);
                       the edge on the border of a FILLET refused like
                       that of the chamfer, the boss on a face that grows
                       and is not a cut, the guided hole outside the part
                       refused (Z-C21.2); the FACE fillet between two faces
                       of a cube, with the centre of mass saying which edge, and the
                       two refusals: the same face twice and two faces that
                       turn their backs on each other (GP7); the RIB (W1.2): the corner
                       of the L with straight material and the hollowed box with the
                       turned one (the second attempt on the same part), the
                       plate without walls and the line outside the plane
                       refused; the guided hole centred between edges too
                       close for it, and the counterbore past the edge, both
                       refused with their twins (Z-C21.10, N50); the state
                       counts selected edges and faces of fillets and
                       chamfers (Z-C21, C7-2, N14); a depth given alone makes
                       the hole blind, and «through_all» beside it still wins (t15)
cases/pairs/       8   the refusals with their twin
cases/slot/       18   every face, not just one; the dimensioned sketch that is
                       corrected with set_dimension, and the straight and oblique anchor
                       (entry E18)
cases/chamfer/       4   where a fillet would remove another volume; the angle
                       of the chamfer is changed in degrees (Z-C21.2)
cases/revolve/  5   the solids of revolution, and the profile straddling the axis
cases/plane/        7   WHERE the plane ends up, not the distance asked; the photo
                       to be traced, scaled on a plane (Z-F5, reads
                       {DATA}: Bench/data/, versioned); a picture over 2048 px
                       keeps the scale of its file (Z-C21, M11)
cases/mirror/     2   the hole must appear on the other side
cases/patterns/      15   the direction, which is the real doubt; the axis recognised
                       by identity, and refused if it has moved (entry E4);
                       the copy taken out of a grid, refused (Z-C21.2);
                       the fill that makes fourteen holes, refused
                       without a direction, on a curved face and with the direction
                       outside the face (W1.1); the linear pattern along an
                       axis from create_axis, and the circular pattern around a
                       straight edge (Z-C21.9, L7-5 and M16)
cases/axis/         2   the axis from the wall of a hole
cases/removal/     5   suppress, switch back on, delete
cases/sentence/        2   the shape told in words, and when it is silent
cases/check/     7   how it came out: solid, closed, sound; the dangling hidden
                       body is seen (Z-C21.2); one sound body of two is
                       not the part (Z-C21.10, N61)
cases/measurement/      13   how much it weighs, which box it fits in, how far it is; the
                       wall between two holes, measured as asked (Z-C21, M29);
                       a body named before the cut is not measured in place of
                       another (Z-C21.5, S5); a face beyond the 60 largest is
                       named by selector and measured (W3.1-bis.12)
cases/bodies/       15   the booleans, the variable fillet, the cosmetic thread;
                       the union of bodies that do not touch, refused (Z-C18);
                       the boolean with the body read before a cut,
                       refused (Z-C21.2)
cases/export/     15   STL, STEP and IGES: the file read back byte by byte,
                       and the volume reopened in SOLIDWORKS; the IGES of an
                       assembly refused (Z-C20); the STL with a face
                       left selected is the whole part (Z-C21.2);
                       the IGES carries the solid, not its wireframe: its
                       directory counted (Z-C21.10, N59);
                       AP242 through SOLIDWORKS MBD, refused
                       without its licence or with a .step path (Z-C21.13)
cases/document_properties/   13   the two drawers, and the code that goes into the BOM; the
                       material reaches every configuration (Z-C21, M10);
                       one property written in both drawers by «scope» (R8)
cases/text/        6   engraving, and the lettering that comes out of the face
cases/frames/        1   the welded profiles, read from the standard library ({PROFILES})
cases/assemblies/     26   inserting is not mating; one constraint is not enough;
                       two equal bases and a STEP: one shape per part,
                       the mass per instance (Z-C20);
                       the rebuilt cover keeps the constraints and breaks them;
                       the cover INSIDE the base (GA10);
                       touching is not interpenetrating (GA10, the twin);
                       the assembly is weighed, measured and sent to STEP (Z-D1, Z-D6);
                       fixed is not mated: types of mates, visible,
                       transparent, fix and float (Z-D3, Z-D4, Z-D5);
                       the right bracket and the left one: instance, linked
                       and unlinked opposite, on three planes (GA11); which files
                       the assembly depends on, and that they exist (Z-B3); the interference array
                       is as long as the count (Z-C21.10, N34); the mirrored
                       instance turns as asked (Z-C21, N26); the turned pin is
                       mated by the directions of the assembly (Z-C21.5, M1);
                       a component inside a sub-assembly is refused by
                       fix_component and replace_component, and the
                       state says it is nested (Z-C21.5, L11-3, L11-5); the
                       component comes in with the configuration asked, and the
                       replaced one says its configuration (Z-C21.4, L11-1, M18);
                       a lightweight assembly has a box and is weighed when
                       asked, the envelope does not weigh, the hidden component
                       is not in the STEP, the lightweight STEP says the export
                       resolved them (Z-C21.11, N5, N27); the same mate twice
                       is refused as redundant and taken out, and a parallel
                       after it blocks RY only (Z-C14); the first component goes
                       into an empty assembly, where the count was read off a
                       null array (D9 defect 3); a wrong mate is taken out by its
                       uid: listed in the state, suppressed, turned back on and
                       deleted, each read back (brief R22); a part is turned
                       over and placed, the state says its axes, the mates win
                       over a placement, and a distance mate goes on the side
                       asked, measured on the faces (briefs R12, R21); a face
                       is named by a point, curved ones too, and a pin goes
                       concentric on a shaft (brief R12)
cases/drawing/      61   the first case that gets to the DRAWING (entry GT0);
                       a through Ø8 and a blind Ø8, two callouts; the boss
                       is not a hole; the chamfered shaft without the legs of the
                       chamfer (revision of 27/9, Z-C20);
                       the drawing of the plate with holes and chamfer, LOOKED AT:
                       holes with the callout and the positions, 5 x 45 chamfer, no
                       dimensions repeated between views (entry GT11);
                       sheet, isometric and PDF of one sheet in a row (entry Z-C6);
                       two sheets and the same projection angle (entry GT1);
                       the title block says what it wants (entry GT3);
                       the counters are there even at zero (entry Z-E1);
                       the drawing is dimensioned, and the dimensions say 120 and 10 (entry K10);
                       the edge that does not exist (the twin of the refusal);
                       a dimension taken away by its name, two then one
                       (report 8cd843 N24), and the name nobody answers to (the twin);
                       the note ends up on the SHEET and not in a view (entry E2-bis);
                       and it stays there after a view was dimensioned and left active
                       (found in the replay of round 7);
                       a note of two lines reads back the same, line ends aside
                       (report 8cd843 N29); a note taken away by its text, counted
                       on the sheet and in the views apart (N32);
                       a drawing saved by the plan is not «to be saved» again,
                       and neither is its part (N34);
                       the note outside the sheet (the twin of the refusal);
                       the section cuts the part, and has the HATCHING (entry GT4);
                       the cut outside the view (the twin of the refusal);
                       the detail MAGNIFIES, and the circle BECAME a
                       detail -- not the hatching, which on a detail
                       is zero (entry GT4, the other half);
                       the circle bigger than the view (the twin);
                       the detail on empty space, with no edge, refused
                       (report 8cd843 N47, the twin);
                       the hole table says where the holes are, read back against the
                       part, and drawing_dimension leaves the holes to the table
                       (entry GT5); the table on the side view, refused;
                       datum, roughness and geometric tolerances attached to the
                       edges and outside the part (entries GT6, GT8), with the four
                       refusals: double letter, flatness with a reference,
                       a datum that is not there, Ra off scale; the cutting
                       list on the drawing, one row per length (entry GT2);
                       the flat pattern of the L on the drawing measured against the part,
                       and refused on a solid part (entry GT13); the weld symbol
                       on the joint of the T, and refused on a single
                       body (entry GT7); the blind hole with its depth,
                       no holes from the side view, no dimension text
                       overlapping (entry GT12); two sections with the same
                       letter, the second refused (entry Z-C13); the centre
                       marks on the holes really counted, and the fillets refused
                       (entry W2.11); the BOM of an assembly of two
                       parts with the balloons (entry GT9); the dimensions of the drawing
                       found again in the text of the PDF (entry Z-C9); the turned
                       bushing, where the Ø30 outline is not a hole and the
                       depth of the blind hole is also read from the geometry (GT12);
                       the stepped shaft dimensioned from the side: lengths in
                       parallel and diameters from the generatrices (GT14); the
                       cutting list says whether it compared (Z-C21.2);
                       the second sheet does not repeat the dimensions of the
                       first (Z-C21.10, N43); the turned diameter held by
                       silhouettes is not an orphan (Z-C21, N3); the position
                       frame reads its Ø back, and the flatness frame reads
                       none (Z-C21.8, M38); the BOM follows the configuration
                       of the view, not the active one (Z-C21.4, L16-2, L16-3);
                       the indented BOM follows the tree (Z-C21.11, M23); four
                       standard views found by what they show, and named as
                       the table of the SOLIDWORKS names names them (0.153.0);
                       a view moved along its line and read back from its
                       outline, and moved off its line, refused; a view deleted,
                       and one that has a section, refused unless asked
                       (report 8cd843 N54); the save says the file it replaced,
                       and with «overwrite» false it refuses (N55); a view asked
                       by number on a drawing of two sheets is not taken away (N57);
                       a detail not larger than its view is refused (N58); the
                       title block says the other sheet whose model lacks a
                       property it asks for (N59); the balloons in the order of
                       the bill of materials do not renumber it (N60)
cases/weldments/   16   the BOM counts the bars, and the two numbers come from
                       two different places; the trim really shortens the bar,
                       and refuses between bars that do not meet; the bead
                       goes between faces that touch at an angle; the cap closes
                       the tube inside (entry Z-C6). The bodies by NAME, with
                       @body(name=...): the numbers C1, C2 change between one run
                       and the next; the trim without a gap does not inherit the one
                       left over (Z-C21.2); the gusset in the corner of the T,
                       triangular and polygonal, with the volumes measured and the
                       box of the new body; refused: the position outside the
                       list, the faces that do not touch or are parallel, the
                       degenerate polygon and the short sides that are too long (GP20);
                       the frame too small for its profile closes the weldment
                       profile library again, and the frame's «measured_as» is read
                       whatever the case, any other word refused (Z-C21.10,
                       N31, N32); a member group is mitred by default, an
                       empty profile size names the sizes, the trim extends
                       a short bar (Z-C21, N25, L9-4); the trim refuses an
                       end condition it does not know (Z-C21.8, N22); the end
                       cap leaves out a drilled wall (Z-C21.5, N7); the cut list
                       counts the gussets and the end caps, not only the bars,
                       and the gusset at the END of a bar is found like the one
                       at its start (D9 defects 1 and 2)
cases/extrusions/  12   the tube has the hole; the extruded slot is as long as
                       the bounding box, ends included, and is a polygon; the
                       17-tooth wheel can be made, the 16 one refuses; the fin
                       grows from the face outwards (entry Z-C6); the round and
                       rectangular anchored boss, and the triangle refused
                       (entry E18); a plane name that is not one of the three is
                       refused, never the front plane (Z-C21.10, N33); the
                       sketch of the L locked for real (Z-C21, M5); the L
                       driven by its vertices, a vertex moved by its dimension
                       and the one on the negative side kept there (brief R5);
                       three heights of one plane in one step, the second moved
                       by the name of its dimension, a height of zero refused
                       with nothing drawn (brief §5.2)
cases/sheet_metal/     13   the L unfolds as long as the hand calculation; dated
                       the other way round, the points become the outer faces; the
                       flat pattern without a file refused; the stretch shorter
                       than the bends refused; the 25 edge flange
                       that lengthens the flat pattern by 21.712, and its two
                       refusals — nothing straight, edge not on the border;
                       the open hem that matches the calculation, the closed one measured,
                       and the open one without a gap refused; the sketched bend
                       that lifts half the sheet (bounding box from the calculation, flat pattern
                       unchanged) and the 180 bend refused (entry GP18); the
                       axis and the planes of a sheet are the ones the calls
                       made, not the flat pattern (Z-C21.5, S7)
cases/dimensions/        8   the variable that does not override the chamfer, refused and
                       put back as it was (Z-C20); the plate dimensions itself
                       with DimXpert, with the right
                       values; with two references it refuses (entry Z-H3); the
                       tolerance on the model dimension, H7 checked against
                       ISO 286 and ±0.05, IT14 refused (entry Z-F2); the dimensions of the
                       model arrive on the drawing, and without views it refuses (Z-C1-bis);
                       the wall has a name: global variable and linked dimension,
                       which follow the change (Z-F1); every part dimension
                       reader (set_dimension, dimension_tolerance, link_dimension)
                       with the fourth configuration active, the before and
                       after of S1 in parts (Z-C21.13); two variables and a
                       link in one step, and a link to a missing dimension
                       refused with nothing written (brief §5.1)
cases/regression/ 19   a dimension upstream changes: who holds and who detaches (--regression)
cases/reanchor/    3   the edge named with the uid read BEFORE two holes is
                       found again by signature (same length, same point) and the
                       centre of mass says which corner; shortened by a chamfer
                       it is refused, never the nearest one; double (two bodies that
                       touch) it is refused, «I do not choose» (entry E19,
                       `uid_from_step`)
```

`-Family` (entry E19) runs only those folders of the declared cases: the `cases`
group alone is three quarters of an hour, and a new case can be seen to fail
BEFORE writing the code. The run is partial as with `-Group`, and rewrites
`last.json` like it does: put it back with `git checkout` before the whole run.

**The language of SOLIDWORKS.** A case that names a feature, a sketch, an axis or a
shell SOLIDWORKS wrote says `{SW:feature.cut_extrude}1`, not the word one installation
uses: the bench replaces the placeholder, in the text of the case before it is parsed,
with the word of `Oce.Solid.Agent.Core/Localization/solidworks-names.json` for the
language of the run, so the same cases run on an Italian and on an English SOLIDWORKS.
`-SolidWorksLanguage en` (`--sw-language en`) turns the toggle «Use English feature
and file names» ON for the run (`swUseEnglishLanguageFeatureNames`, applied at once, no
restart) and puts it back as it was when the run ends, also on Ctrl+C; if the run was
killed the value to restore sits in `%TEMP%\oce-bench-language-restore.txt` and the next
run puts it back before anything else. `it` turns it off, `native` (the default)
leaves it alone. The toggle «Use English language menus» is NOT touched: it needs a
restart of SOLIDWORKS. Two things stay as they are, on purpose: the texts of the
interface (view names, hole callouts, cut-list property names) follow the language of
the installation, and the names a TEMPLATE stores (its planes, its configurations, the
sheet of a new drawing) stay in the language of the template. The first line of the
bench output (`solidworks language: ...`) says which columns of the table the run used.

`"uid_from_step": N` in a step takes selectors and signatures from the state
BEFORE step N, as the bridge does with a plan written by looking at the state
once. The signatures are those of faces, edges and — since entry Z-C21.2 —
bodies (`center_mm`, `bounding_box_mm`, as the bridge sends them): before, a body
named that way arrived without a signature, and the body renumbered by a cut
(entry E4) could not be seen by the bench.

**The others** — plans for the MCP bridge and working material, which
`--group cases` does not run:

```
cases/limits/    8   where the operations break
cases/drawing/    7   views, dimensions, PDF  (+ the declared cases, see below)
cases/views/     5   parts of which everything is known, for the tests on images
cases/bridge/   107   the plans of the MCP bridge, per TODO entry
```

**`cases/shape/` IS NOT IN THE REPOSITORY, and that is intended.** The folder
appears on this machine and holds the five plans that an isolated model writes
during the V3 measurement — it rewrites them at every run, and versioning them
would mean freezing the answer of **one** model on **one** day as if it were the
measurement. The measurement is the number, and it is in the README
(`.gitignore`, line 59).

*This line used to count it together with the others, «cases/shape/ 5», and the
count was true only here:* on a clean clone that folder does not exist, and the
contract check «every folder named in the README exists» turned red for anyone
except whoever wrote it. **A document that describes the machine of whoever
writes it instead of the repository is the defect that is only discovered from
the outside** — and from the outside it is discovered by a stranger, at the
first attempt.

**`cases/drawing/` holds both things, since 17/9/2026** (entry GT0), and it is
the only folder like that. The seven `dwg_*.json` are plans; the declared cases
of the `drawing` family are there next to them. **They are not told apart by
name, they are told apart by SHAPE**: a plan is an array of steps, a case is an
object with `name`, `why` and `steps`. The loader looks at the root of the JSON,
skips the arrays, and **says at every run how many it skipped and which**:

```
OK   hand-written plans are recognised and skipped   7 skipped (arrays, not cases):
     dwg_1_save_part.json, dwg_2_create_drawing.json, dwg_3_sheet.json, ...
```

An **unreadable** file does not count as a plan: it stays a bench error with the
right reason. *A recognition that swallows the failures too stops recognising.*

And watch out for the placeholder, which **is not the same in the two shapes**:
the declared cases use `{WORK}` and `{PROFILES}` (resolved by `Bench\Expectations.cs`), the plans
use `{BENCH}` (resolved by `run_plans.js`). Swapping them gives no error:
the path stays literal, and the case complains that a file does not exist.

The names in `cases/bridge/` begin with the entry that gave them birth —
`c4r_*` the gear wheel, `d3_*` the dimensions on the cuts, `b8_*` the references
between steps, `s_*` the refused shapes. Whoever reopens an entry finds its cases
by searching for the prefix.

---

## Why they are in the repository

Until 14/9/2026 they were in a temporary session folder, and the path of the
library was a **constant** pointing inside that folder. When entries B4 and B5
moved the operations from `OpenPDM.SwAddin` to
`Oce.Solid.Agent.Core`, the bench stopped starting — and **nobody noticed for
three days**, because it lived outside the repository and nothing launched it
any more.

> A measurement that can be redone only from the machine and the week in which
> it was written is not a measurement: it is an anecdote.
