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
  JSON with the keys «compito» and «come_si_misura») is gone: it was the Italian
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

**After the move to the protocol of .10-bis** (integration-1, 6/10 evening) the plans
`08_box_and_lid` and `09_bracket_pair` were built again on SOLIDWORKS on both
bridges, every expectation of their tasks checked: on the bridge of today with the
English mate values, on the bridge of before through the table
`MATE_VALUES_BEFORE` of `prerequisite.js`.

**The stand (`07_tube_stand`, tasks 11 and 15) still does not build**, on either
bridge, and the runner refuses those sessions (on both sides, so the comparison is
not tilted; the two tasks are not measured). **State after the integration (bridge
0.128.0, library 0.151.0):** of the three defects below, `cut_list` and `gusset`
are fixed, and so is the first insert into an empty assembly; the third (the list
of the 60 largest faces) is not, and the live build of 6/10 evening goes through
steps 1 to 9 and stops at step 10, the first gusset: «not found (the state lists
only the 60 largest faces of 177)». The bridge of before keeps all of them. The
text below is the state of the morning. Three defects of the library, each enough
alone, are written in the plan's `_status` with what the probes measured: `cut_list` counts only the structural members (never the
17 bodies and 6 rows of task 07, which no model can reach either); `gusset`
refuses the faces of a foot at the END of the segment of the bar it rests on and
accepts them at its START; the 60 largest faces leave out the walls of the third
and fourth foot. The plan keeps the numbers of task 07; what the probes proved
for the plan to come (one cap per foot through `trim_members` and `end_cap`
`ends_ref`) is written there too.

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
