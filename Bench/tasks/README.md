# `tasks/` — what is asked of the MODEL, not of the library

> **This folder is not a bench.** It is the INPUT of the bench of entry Z-G9,
> which does not exist yet. Here are the tasks and **how they are measured**;
> the autonomous run below executes them, but the Z-G9 bench itself is still to
> be written.

## Why it exists, and why before the code

The bench of `Bench/cases/` executes plans **already written by a person**: it
measures the library, and it does that well. But it does not answer the question
that really matters — *can the model, on its own, make the part?*

As long as that run does not exist, «the model draws better» stays an opinion
with a table around it.

And the hard part of that run **is not executing it**: it is deciding what
«right» means. A bench where the part is looked at and someone says «yes, it
seems good» is not a bench: it is a demo. That is why the tasks are born here,
with their measure beside them, **before** whoever executes them.

## What a task is made of

A Markdown file `NN_name.md`. Two parts do not touch each other, and they are the
two the run reads:

```
the task        (the first blockquote, under "The task") what is asked, in
                a workshop's words, as somebody at the bench would say it.
                NO operation names, no arguments, no numbers that a human
                would not say. The Italian originals of the words are kept
                outside the repository (the run on the Italian version takes
                their folder with -TasksDir).

how it is       (the table under "How it is measured") the expectations, in
measured        the same form as the cases of `Bench/cases/`: what is read,
                the expected value, the tolerance, and WHY. They are evaluated
                on the FINISHED part, not on the steps: the model is not told
                how to get there.
```

The rest of the file (why this task, what it deliberately does not say, what the
measure does not see) is for the person who reads the result.

**The task does not name the operations, and it is not an oversight.** If the
task said «use `extrude_rectangle` then `cylindrical_cut`», obedience would be
measured, not the ability to choose. Choosing the operations **is what is
measured**.

## How it is measured, and what «right» means

Three outcomes, and they must be kept apart:

```
RIGHT       every expectation holds
WRONG       the part comes out, and at least one expectation does not hold
NOT DONE    the plan was refused, or failed halfway
```

**«Wrong» and «not done» are not added up.** A refused plan is the bridge having
worked: it said no instead of producing a crooked part. A wrong part is the worst
case — nobody notices until it reaches the shop. Adding them would give a number
that hides the difference between the defence that holds and the defence that is
missing.

The number of entry Z-G9 is **how many come out RIGHT at the first try**, and the
first try is the one that counts: it is measured without running again, without
adjusting the task, without telling the model what it got wrong. A number
obtained at the third attempt describes a model that does not exist.

## Tolerances, and why there are any

A volume calculated by hand and one measured by SOLIDWORKS diverge because of the
fillets nobody asked for and because of rounding. Where an expectation has a
tolerance, **the tolerance carries its why**, as in the cases: a tolerance with
no reason is an expectation that widens until it passes.

## What is NOT in this folder

- **The Z-G9 bench is not here.** It is still to be written. The autonomous run of
  D9 (below) executes the tasks and scores them, once, for the comparison of the
  rename; it is not that bench.
- **There is no score kept here.** The number exists only after the run has run
  (its reports go to `Bench/report/autonomous/`); writing it now would be
  inventing it.
- **There are no tasks that nobody would do.** Every task here is something that
  in this shop has really been made or would be: a bracket, a frame, a drilled
  plate, a lid. *A bench built on invented parts measures how well the model
  makes invented parts.*
- **The first form of tasks 01 and 06** (`l_bracket.json` and `one_meter_frame.json`,
  JSON with the keys «task» and «how_it_is_measured») is gone: it was the Italian
  original of those two tasks, nothing read it (the run reads `NN_*.md`), and
  their words and numbers are in `01_l_bracket.md` and `06_welded_frame.md`. It is
  in the history of the repository (`git log --follow Bench/tasks/l_bracket.json`).

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

**The scorer reads the English protocol only** (W3.1-bis.11). Until then it also read
the answers of the bridge before the rename (9384b2c) through the name map, so that D9
could compare that bridge with this one; the map is archived outside the repository
and so is that reading. `--read-state` reads the state with `inspect_part`,
`run.json` records the bridge's version and protocol (`en`), a run in which no
session gave a single outcome is refused instead of scored, and `--compare` refuses a
`score.json` of an older scorer. The `score.json` of the run before, scored while the
reading existed, is kept and still compares (same format).

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
the run before the rename against the run after it, at most one task of fifteen
worse), ×1 on the English version (a signal, not a threshold). The Italian version
is not in the repository; `<it>` below is its folder. The run before was made with the
bridge of 9384b2c and scored while the scorer could read it: its folder and its
`score.json` are what `--compare` takes. This runner speaks to a bridge that answers
in English, so a new run before cannot be made with it.

```powershell
# after: this bridge
.\Bench\tasks\run-autonomous.ps1 -Language it -TasksDir <it> -Repeats 3 -Label after
.\Bench\tasks\run-autonomous.ps1 -Language en -Repeats 1 -Label after
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

**The starting part is a fixture, not a measure, and it is the SAME FILE on both sides
of D9** (W3.1-bis.12). The runner builds the prerequisites with ONE bridge, given
by `-PrerequisiteBridge` and by default the bridge of this repository (the one
AFTER); `-Bridge` is only the bridge the session talks to, and `run.json` records
both (`prerequisite_bridge`, `prerequisite_bridge_version`). Built by the bridge
under test, the two sides could start from different parts (the stand of task 07
could not even be built by the bridge before, which lists the 60 largest faces
only), and the comparison would measure the fixtures instead of the model. What a
session does with the part is still measured; how the part came to be is not.

A group of a plan that names a face by selector reads the part with EVERY face
listed (`inspect_part` with `faces: {limit: 500}`): the ordinary state lists the
60 largest and a small face of a part with more of them was «not found» although
it was there. A bridge that does not answer the filter fails the build instead of
resolving on the cut list.

| task | starts from | reference plan (`prerequisites/`) | what is in front, saved in the folder of the session |
|---|---|---|---|
| 09 mirrored pair | 01 L-bracket | `01_l_bracket` | `01_l_bracket.sldprt`: the bracket, 67,782 mm3, holes of 9 |
| 10 pair drawing | 09 pair | `09_bracket_pair` | `base_plate`, `bracket` and `09_bracket_pair.sldasm`: plate fixed, bracket mated, its mirrored instance driven by the mirror, 300 x 70 x 120, no interference |
| 11 stand drawing | 07 stand | `07_tube_stand` | `07_tube_stand.sldprt`: frame, crossbar, feet, gussets, caps, cut list (builds since W3.1-bis.12: see below) |
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

**The plans are a protocol of their own.** They are written in English and pass
`prerequisite.js --check` against the catalogue of this bridge (until W3.1-bis.11 they
were also translated for the bridge before the rename and checked against its
catalogue; that bridge is no longer spoken to).

**What was proven without SOLIDWORKS** (`run-autonomous.ps1 -DryRun` runs it):
every plan against the catalogue (operations, arguments, enum values, the
references between steps, the paths of the expectations), the dry run of the
bridge with `OCE_NO_ATTACH` set, the build and the three ways to fail against a script that plays the
bridge, the closing rule against a script that plays SOLIDWORKS (with the
defect of 6/10 reproduced), and the index against the texts of the tasks.

**What was proven on SOLIDWORKS** (6/10, both bridges: after = 0.127.1, before =
9384b2c through the map that is archived now; `PREREQ_TRACE=<file>` keeps every
request and answer of a build). Ten of the twelve builds hold: `01_l_bracket`,
`02_bolt_circle_flange`, `03_slotted_plate`, `08_box_and_lid` and `09_bracket_pair`
on both bridges, every expectation of their tasks checked. What the first proof taught, and is now in
the plans and the runner:

- a selector never names a face by `normal=`: the bridge of before wrote the
  facing in Italian and the translation covered that field only in the cases, so the
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
- the state of an assembly of before had neither bodies nor faces, and the
  scorer did not see it as Italian: its keys (documents open, count of the
  components) became marks of that protocol (the reading is gone with the map).

**After the move to the protocol of .10-bis** (integration-1, 6/10 evening) the plans
`08_box_and_lid` and `09_bracket_pair` were built again on SOLIDWORKS on both
bridges, every expectation of their tasks checked: on the bridge of today with the
English mate values, on the bridge of before through a table of its mate words
(removed from `prerequisite.js` in W3.1-bis.11 with that bridge).

**The stand (`07_tube_stand`, tasks 11 and 15) builds since W3.1-bis.12**, on the bridge
of that window (0.130.0, library 0.153.0) and by the runner that reads every face. Three
defects of the library had each kept it from building, alone: `cut_list` counted only the
structural members (fixed in 0.151.0: the gussets and the caps are in it, with their
`item_type`); `gusset` refused the faces of a foot at the END of the segment of the bar it
rests on (fixed in 0.151.0); and the state lists the 60 largest faces of the 177 of the stand,
so the walls of the third and fourth foot, which tie at 4,800 mm2 behind 40 larger faces of
the frame, had no identifier (fixed in 0.153.0: every face has the identifier of its rank by
area, `inspect_part` takes a filter `faces`, and a group of a plan that names a face by
selector reads the part with every face listed). The plan was also rewritten for ONE cap per
foot: the four feet in a single step 20 mm short of the frame, extended to it by
`trim_members` and capped by `end_cap` with `ends_ref` on that step (`end_cap` on the
bodies capped both ends of every foot: 8 caps, 21 bodies, measured live on 7/10/2026 with
every gusset built). The live numbers are in the plan's `_status` and in the CHANGELOG.
The bridge before keeps the three defects: it cannot build the stand, which is why the
prerequisites are built by ONE bridge for both sides of D9 (see above).

**Still to know about the tools around the run:**

- the first `insert_component` into an empty assembly was refused by BOTH bridges
  although the component came in (it counted -1 before). The bridge of today
  (0.128.0, library 0.151.0) no longer refuses it; the bridge of before still does,
  so the preparer still accepts it on the state of the assembly and says so in its
  record;
- `CloseLeftovers --close <folder>` closed by TITLE, and a name that matches no title
  whole falls back to the base name and closes the first document of that name.
  Since bridge 0.128.0 it closes a saved document by its PATH and a never-saved one
  by its title (the harm of the old way was not reproduced with the titles this
  machine gives: the fix is the defence). The runner's `--close-titles-file` asks
  by title first, and by path for what stays open.
