# OCE Solid Agent

*An independent project, not affiliated with Dassault Systèmes.*

An MCP bridge for **drawing in the SOLIDWORKS® software**: it exposes ninety-nine
drawing operations, each one already measured, as tools, so that any model — local
or remote — can build a part and, above all, **look at what came out**.

It installs with a single file and asks for nothing more than a SOLIDWORKS®
installation with a license of your own.

> **Do you only want to use it, not build it?** → **[QUICKSTART.md](QUICKSTART.md)**,
> six steps, in plain English. This page is for people who want to build it.
> If you are an AI assistant installing it on someone's behalf, the page written
> for you is **[INSTALL-FOR-AI.md](INSTALL-FOR-AI.md)**.

> SOLIDWORKS® is a registered trademark of Dassault Systèmes SolidWorks
> Corporation. This project is independent and is not affiliated with, sponsored
> by or endorsed by Dassault Systèmes. It contains and redistributes no code or
> material of Dassault Systèmes: the interoperability libraries are loaded at
> start-up from **your** SOLIDWORKS® installation, which requires a license of
> your own.

## Why it exists

OpenPDM has an assistant that draws: a local model proposes a plan, the server
validates it, the add-in runs it inside the SOLIDWORKS software, and a designer
confirms before the part exists. That chain is the product and stays the product.

The problem has always been the same: **measuring**. Every real defect of that
assistant turned up by actually drawing, and every test round cost a dance of
files between two machines. With this bridge a round is one call.

There is a second reason, less obvious and more important. The model that runs
in the workshop is small. As long as the only way to test the system is through
that model, you cannot tell **"the tools are missing"** from **"the model is not
up to it"**. With this bridge the same part can be drawn by a large model and by a
small one, by the same route and with the same checks: what remains different is
the answer.

## What it is NOT

It is not a way to let a model on the network draw the company's parts. The product
stays the local, offline assistant with its confirmation panel. Here things are
measured and developed.

## How it works

A command-line application that speaks **MCP over stdio** and calls
`Oce.Solid.Agent.Core`, the library that holds the operations. It does not rewrite
them: they are the same ones the OpenPDM add-in runs when a designer presses
"confirm", so what you test here is exactly what the designer will get.

```
model  <--MCP/stdio-->  oce-solid-agent-mcp.exe  ---->  Oce.Solid.Agent.Core.dll
                                                            |
                                                        SOLIDWORKS
```

Up to 0.5.0 the library was `OpenPDM.SwAddin.dll` — the whole add-in, loaded by
reflection — and the bridge dragged all of the PDM along to drill one hole. From
0.6.0 the operations stand on their own; from 0.7.0 the installer carries them
inside.

## The tools

Nine, not ninety-nine. The drawing operations are ninety-nine, but exposing one
per tool would cost tens of thousands of characters of schemas in every request
— and, above all, it would make it easy to skip the first step. (There were five
up to 0.56.0: the sixth, `measure`, came with 0.57.0 for a precise reason, written below
the table, not to cover more. The seventh, `document`, came with 0.87.0: without it, starting
a part from scratch needed a person to click File > New.)

*Since 0.112.0, the eighth and ninth, `api_search` and `api_signature`, read the
whole SOLIDWORKS API — the `sldworks` interop and the `swconst` enums, 13,945
members once the four old interfaces are left out — by reflection, with
SOLIDWORKS closed. They are the first half of the generic layer; `api_objects` and
`api_call` are still to come.*

| tool | what it is for |
|---|---|
| `inspect_part` | the state of the open document: bounding box, volume, bodies, features with their dimensions, faces and edges with their identifier. With `views` it also returns a **photograph** of the part |
| `list_operations` | the short list by family: a name and one line each |
| `describe_operation` | the complete schema of one |
| `build` | runs a sequence and **reports what came out**: the measurements of every step and the state afterwards |
| `measure` | the same operations, but **only the ones that read** (family `checks`). It declares itself read-only, so the client does not ask for confirmation for a measurement |
| `undo` | withdraws the last plan, all of it — but only if the part is still as `build` left it |
| `document` | creates an empty part or assembly, opens a file from disk, lists the open documents, brings one to the front, closes one — and **refuses to close** a document with unsaved changes. It always says whether a file is open **read-only**; and after every change of document `build` insists on a new `inspect_part` |
| `api_search` | searches the whole API by a piece of a name and answers with the **real signatures** read from the interop on this machine; the four old interfaces (`IModelDoc`, `IBody`, `IFace`, `IComponent`) are named with the sister to use, never shown. Read only |
| `api_signature` | the card of one member: every overload, property or method, the `&` out parameters, the typed `I…` variant, the numbered family with the last one marked **"latest by number, not verified"**, the **Void** flag, and what is known of it from `Tools/api_knowledge.json` — state, enum map (**"enum not tabulated"** where nobody wrote one), measured indices (**"index not tabulated"** otherwise), measured types, source. Read only |

**`measure` exists because the annotations sit on the tool.** The MCP client reads
`readOnlyHint` and `destructiveHint` in `tools/list`, that is **before** any call
exists: with `build` alone it asked for confirmation even for `mass_properties`,
which does not move a cubic millimetre. Someone who confirms ten times in a row
stops reading what they confirm. An operation that touches the part, inside
`measure`, is refused **before** the plan starts — never half a plan.

**`inspect_part` is not an accessory.** The identifiers the other tools want —
`F1`, `E4`, `C1` — are born there and hold for that reading: they are POSITIONAL,
they are renumbered by area at every reading, and after every change they must be
read again. Anyone who skips the first tool works blind, which is the most
expensive defect of the whole project.

**Measuring and seeing are not the same thing.** Since 0.31.0 `inspect_part` accepts
`views` — for example `["isometric"]` — and answers with one or more PNG images next
to the state. The views are `isometric`, `trimetric`, `dimetric`, `front`, `back`,
`left`, `right`, `top` and `bottom`.

**How much is needed, measured.** Five parts of different shapes (a plate with
holes, a hollowed box, an L bracket, a flange with a ring of holes, a stepped
shaft), four isolated models, criterion written before looking at the answers:

| the model receives | says what object it is | says the machining | says the dimensions | **wrong numbers** |
|---|---|---|---|---|
| the isometric only | **5/5** | 3/5 | 0/5 | **0** |
| four views | **5/5** | 4/5 | 0/5 | **0** |
| the state only | **5/5** | **5/5** | **5/5** | **3** |
| state + isometric | **5/5** | **5/5** | **5/5** | **1** |

Two things we did not expect. **The written state alone is enough to understand
what the object is, 5 times out of 5** — "thin-walled box open on one side",
"stepped shaft of revolution" — so the bridge was not as blind as it seemed. And
**an image does not make a model invent numbers**: ten answers out of ten wrote
"cannot be estimated, no scale reference" instead of guessing, while still giving
right proportions (side ratio 1.6-1.7:1 against the true 1.67).

One model, one round per condition. On "wrong numbers", **3 against 1 is a single
round**, and a single round is not a trend.

**And the shape can also be SAID.** The state carries a `shape` field that sums the
part up in one line — 68-152 characters against the 40-115 kB of an image:

```
Bounding box 140 x 16 x 140 mm, volume 191938.7 mm3 (that is 61% of the bounding box),
1 hole Ø60 (center 0,0,0), 6 holes Ø11 on a circle Ø90 at a pitch of 60 degrees.
```

Measured with a harder criterion than the one above: **from the sentence alone**
an isolated model rewrote the construction plan, and the rebuilt part was compared
with the original.

| part | rebuilt volume against original |
|---|---|
| plate with holes | 353214.2 against 353214.2 — **0.0%** |
| hollowed box | 109056 against 109056 — **0.0%** |
| L bracket | 105600 against 105600 — **0.0%** |
| flange with a ring | 191938.7 against 191938.7 — **0.0%** |
| stepped shaft | 55857.5 against 55857.5 — **0.0%** |

**Five out of five at the exact volume**, not "close".

On the first round it was four: the shaft came out 3.8% off, because the sentence
said "part of revolution" and nothing else — the name of the family without a
single dimension. And the model **had said so before trying**, pointing at what was
missing: the intermediate diameters. Now the sentence also carries the profile —
*"Ø30 for 40, Ø20 for 30, Ø34 for 20"* — worked out from the area of the
cylindrical faces.

**And it only states it if it can rebuild it.** The length of a stretch comes from
`area / (π·d)`, which holds on a whole cylinder: cut by a groove or a hole it would
give a number that is short, plausible and false. The profile is written only when
the sum of the lengths makes the bounding box **and** the rebuilt volume makes the
true volume. Otherwise the sentence is silent and stays "part of revolution": little,
but true.

**At most three, and only if you ask for them.** A view costs more than ten times the
whole written state, which stays under 16,000 characters: carrying three at every
reading would change the cost of the bridge by an order of magnitude, and it would
also be paid by whoever only wanted to know how many bodies there are.

Taking a photograph **does not touch the view of whoever is working**: the
orientation is saved before and put back after, even when the photograph fails
halfway.

**And you can look inside.** With `section: {plane: "right"}` the part arrives as a
**cutaway** (planes `front`, `top` or `right`; `offset_mm` moves the cut): a cutaway
says more about a shell than ten external views — from the outside a hollowed box
and a full one are the same block, and the thickness of the bottom cannot be read.

It cuts **only the picture**, not the geometry: no feature in the tree, no body
split. And it removes itself as soon as it has been taken, like the orientation.

**And you can go back.** Up to 0.31.0 the operations were all additive or
parametric: the tree could only grow, and whoever noticed a mistake had to build on
top of it or redo the part from scratch. `undo` withdraws the last plan, all of it —
`build` already runs it inside **a single** undo entry.

**And you can remove a feature in the middle**, not only the last plan:
`suppress_feature` turns it off (with `reactivate: true` it turns it back on),
`delete_feature` deletes it. The second **refuses when anything depends on that
feature**, and says what: deleting it would take its children away with it, and
SOLIDWORKS does that without asking.

**It refuses if the part changed afterwards.** Volume and number of features must
still be what `build` left: if the designer worked by hand in the meantime, undoing
would remove their work, and the bridge stops and says what changed.

## How to put it to work

There are two routes. To **use** the bridge you do not build anything: take
`Installer.exe` and follow [QUICKSTART.md](QUICKSTART.md). The rest of this section
is for building it from a clone.

### First, build it

`bin\` is in `.gitignore`: after a clone that folder **does not exist**, and neither
does the executable named below.

```
.\build.ps1
```

You need MSBuild (the **Build Tools for Visual Studio 2022**, free, are enough) and
SOLIDWORKS installed on the machine: the interoperability libraries are not in this
repository and are taken from there. The script builds the bridge into `bin\`,
regenerates `Tools\catalog.json`, builds the probes of `Checks\`, and ends with
`dist\Installer.exe` and the checksum `dist\Installer.exe.sha256` next to it. Its
other switches are `-Clean` (delete `bin`, `obj` and `dist` first) and `-BridgeOnly`
(only the bridge, no installer).

Right after the clone, once only:

```
.\Checks\install-hooks.ps1
```

It turns on the `pre-push` hook. Before a push the hook runs the repository's own
checks — the workflow steps of the CI, the language gate (new text is written in
English), and `check-publishability.ps1` — and **stops the push** if one of them
fails; the last one fails if the tree or the history would let out a path of this
machine, a user name or a private network address. Git does not version
`.git\hooks`: until you run that line, the hook sits in the repository and is not
on. To skip it once, knowing you do it: `git push --no-verify`.

If SOLIDWORKS is not in the usual place, name the folder — it is the **root** of the
installation, not `api\redist`:

```
.\build.ps1 -SwInstallPath 'D:\Programs\SOLIDWORKS Corp\SOLIDWORKS'
```

The probes in `Checks\` have a script of their own, and they sit outside the solution
on purpose — one more project would make them rebuild at every build for no reason.
`build.ps1` already builds them (it stops if one does not compile); to build only
them:

```
.\Checks\build-checks.ps1
```

It takes the same `-SwInstallPath` (and reads `SW_INSTALL_PATH`), so the two scripts
agree on what "the SOLIDWORKS folder" means.

### Then connect it

```jsonc
// in the configuration of the MCP client
{
  "mcpServers": {
    "solidworks": {
      "command": "C:\\path\\to\\the\\clone\\bin\\oce-solid-agent-mcp.exe"
    }
  }
}
```

`solidworks` is the name the installer gives the entry; any name works. With
Claude Code, the supported way to write the same thing is
`claude mcp add --scope user solidworks -- "C:\path\to\the\clone\bin\oce-solid-agent-mcp.exe"`.

Before connecting a client, the dry run is worth it:

```
bin\oce-solid-agent-mcp.exe --self-test
```

It prints on stderr whether it attached, which library it loaded, how many
operations it knows and what it sees in the open document. It touches nothing.
Its exit code says the same thing in a number (since 0.126.0): 0 attached, 3
SOLIDWORKS not running, 4 not installed or not registered, 5 not tried
(`OCE_NO_ATTACH`), 6 SOLIDWORKS running in this session but not answering
(usually started as administrator while the client is not), 1 anything else.
The installer reads the number, not the text.

### Environment variables

| | |
|---|---|
| `OCE_DRAWING_LIBRARY` | which `Oce.Solid.Agent.Core.dll` you expect to be used. It does **not choose it**: .NET loads the one next to the executable. It is there to be told if the two differ |
| `OPENPDM_ADDIN` | the old name of `OCE_DRAWING_LIBRARY`, still read |
| `OCE_CATALOG` | where `catalog.json` is; if missing, next to the executable |
| `OCE_QUESTION_REGISTRY` | where the bridge writes **what it was asked for and was not there**: one JSON line per question, only the name and the nearby names, never the arguments. It is a `.jsonl` file; if the variable is missing, it is written next to the executable (the installer's uninstall removes it); **set to an empty string it means "record nothing"**. If it cannot be written, the bridge stays quiet and goes on: the log never makes the call it observes fail |
| `SW_INSTALL_PATH` | where the SOLIDWORKS software is installed: the **root**, the one that ends with `\SOLIDWORKS`. It holds both for building and for running — `api\redist` is derived from it |
| `SW_REDIST` | the old name, still read. It wanted the `api\redist` subfolder and not the root: if both are set, `SW_INSTALL_PATH` wins |
| `OCE_WRITE_ROOT` | the only folder the bridge may write into. If it is set, everything else on the disk is out |
| `OCE_READ_ONLY_FOLDERS` | folders the bridge must not write, separated by `;` — for example `S:\CAD;S:\LIBRARIES`. They add to the system ones, they do not replace them. The bridge never writes under Windows, System32, Program Files, Program Files (x86) or ProgramData either, with or without this variable |
| `OCE_WELDMENT_PROFILES` | the folder of the weldment profiles (`.sldlfp`) that `weldment_profiles` reads. It has no default: the installer writes it into the client entry's `env`, from `--weldment-profiles <folder>` or from the folder SOLIDWORKS is set to (Tools > Options > File Locations > Weldment Profiles), else the one SOLIDWORKS installs (`<SOLIDWORKS>\lang\<language>\weldment profiles`) |
| `OCE_NO_ATTACH` | any value except empty, `0` and `false`: the bridge starts, answers the protocol and lists its tools, but **does not attach to SOLIDWORKS** and says so; `--self-test` exits 5. For a session busy with a batch job or the bench, and for the installer's own tests (`Checks/test-installer.js` sets it unless given `--attach`) |

`OCE_DRAWING_LIBRARY` no longer decides which library to load, and is still read on
purpose: if it points elsewhere, the bridge **says so**. An environment variable that
does nothing is worse than one that does not exist — whoever set it keeps believing
it counts.

The Dassault interoperability libraries are **not in this repository**: they are
taken from the installation, so the version is always the one that is running.

## Language

**The project is in English**: the nine tools and their sub-actions, the 99
operations and their families, every argument and the value of every argument whose
schema lists one, the selectors (`@face(normal=front)`), the fields of every answer,
the keys of `Tools/catalog.json`, the MCP resources (`catalog://operations`,
`catalog://operation/{name}`), the tool titles, the texts of the answers, the
descriptions in the catalog, the code, its comments and these documents. New text is
written in English, and a language gate (`Checks/language/detect-italian.js`) keeps
the Italian from growing, on every push and on the CI: every commit is judged
against its parent, with its parent's rules, and a raise needs a signed trailer on
the commit that raises.

**What comes from SOLIDWORKS stays in the language of the installation.** The names
SOLIDWORKS gives to features, sketches, planes and configurations (`Boss-Extrude1`,
`Sketch1`, `Front Plane`, `Default` on an English installation; `Estrusione1`,
`Schizzo1`, «Piano frontale», `Predefinito` on an Italian one), the custom properties
of the templates (`Descrizione`, `Materiale` in the templates this bridge was
measured with) and the messages SOLIDWORKS itself writes are read and passed on as
they are: the bridge never translates them, and an operation that takes such a name
takes it as the installation spells it. Where the catalog gives an example with a
feature name, it gives the English one and says that the name is the one of the
installation. The Italian feature, sketch, sheet and plane names that the bench meets
are listed in `Checks/language/exceptions.json` (class `solidworks-localized`): the
language gate does not count them.

**Values outside any enum are English words too**: the `variant` of `mate`
(`coincident`, `parallel`, `perpendicular`, `distance`) and its `alignment` (`aligned`,
`opposite`) are free text, checked by the operation and not by an enum of the catalog,
and so are the `kind` the state reports for a selected entity (`face`, `edge`, `body`,
`vertex`, `feature`), for a mate, for a drawing view and for `check_body_geometry`.
`describe_operation` on `mate` shows the exact words it accepts.

**One name for everything.** A tool, a sub-action, an operation, a family, an argument
or a value is known by its English name only. A name the bridge does not know (an old
name, a foreign one, a typo) is an unknown name and is treated like any other: an
unknown tool is an `isError` answer that lists the tools there are, an unknown
operation is refused with the operations it resembles, an argument the schema does not
have is reported as ignored, and a value the schema does not list is refused. Nothing is
translated on the way in.

## The catalog

`Tools/catalog.json` is an EXPORTED copy, written by the bridge itself, not a second
writing: see [Tools/update.md](Tools/update.md). It holds only the operations
**measured inside the SOLIDWORKS software**.

An operation that exists in the library but creates nothing **stays out of the file**
(`Tested = false`): a client must not be able to call it. Today there is none. The
last one was `gusset`, null in sixteen variants (measured on 25 September 2026); it
came back when the API guide said what was missing (mark 1 on the two faces and a
profile that is not degenerate). The bench prints in its header **both numbers with
their name** — "runnable: 99 operations (in catalog: 99)": until 24 September 2026
it printed "catalog: 73" while the catalog had 71, and the difference was explained
once, plausibly and wrongly, with two copies of the library that diverged.

### What runs on which document

| family | document |
|---|---|
| `extrusions`, `features`, `weldments`, `references`, `physical_properties`, `sheet_metal`, `dimensions` | a **part** (`weldment_profiles` only reads a folder and needs no document) |
| `drawing` | a **drawing** (`create_drawing` starts from a part or an assembly; `save_document` takes any document) |
| `assemblies` — `insert_component`, `mirror_component`, `fix_component`, `mate`, `replace_component` | an **assembly** |
| `exports`, `identity` | a part or an assembly (`export_stl` and `export_iges` want a part; `set_property` takes any document) |
| `checks` | mostly a part; `interference_check` wants an **assembly**, `title_block` a **drawing**, `mass_properties` and `body_bounding_box` a part or an assembly, `file_dependencies` any document |

It is not a decorative table: **a check enforces it.** `Oce.Solid.Agent.ContractTests`
compares the families of the served catalog with the document types the executor
admits, and turns red if the bridge promises a family that it would refuse before the
first step. It was born red: until 18 September 2026 the catalog listed the family
`assemblies` in full while the executor refused every assembly. **The bench could not
notice — it calls the library, not the bridge.**

> This sentence, in its first draft, set off another one: it named the number of
> assembly operations in words, and the check that guards **the count of operations
> in the README** read it as if the README were declaring that number — and went red.
>
> The check was right to be stupid. A number in words next to the word "operations"
> cannot be told apart, by someone reading in a hurry, from the one that says how
> many there are in all; and the defect the check exists to catch — a README that
> promises a catalog different from the real one — has exactly that shape. **I
> rewrote the sentence, not the check.** *A check to which you explain the exceptions
> stops being a check.*

## Status

See [CHANGELOG.md](CHANGELOG.md). The version is in `VERSION.txt` and goes up with
every change: it is a rule, not a habit.

### What the CI proves, and what it does NOT prove

> **This section was rewritten on 23 September 2026, and before that it said what was
> false.** It said "the CI builds, runs the contract checks — which talk to the bridge
> over stdio, without SOLIDWORKS — and refuses a delivery in which nobody ran the
> bench". **Neither of the first two things could happen on a GitHub runner**, and the
> CI never turned green. The why is below, because it is the kind of thing whoever reads
> a README has the right to know before trusting a badge.

**The bridge is not built in the CI.** `Oce.Solid.Agent.csproj` and
`Oce.Solid.Agent.Core.csproj` reference `SolidWorks.Interop.sldworks` and
`SolidWorks.Interop.swconst` with a `HintPath` inside the SOLIDWORKS installation.
Those DLLs are Dassault's: they are not in the repository, they cannot be downloaded,
and on a GitHub runner they are not there. Measured, not deduced — building with
`/p:SwInstallPath=Z:\does-not-exist` gives two hundred errors and goes through
`CS0246: The type or namespace name 'IModelDoc2' could not be found` (the compiler's
message, here in English; on a Windows set to Italian it comes out in Italian).

As a consequence **not even the checks that talk to the bridge** can run: without an
executable there is no stdio to write to.

What the CI really does:

| | runs on a GitHub runner |
|---|---|
| builds `Oce.Solid.Agent.ContractTests` (it has no interop, and a check verifies it) | **yes** |
| the evaluator of the expectations, the sources inside their `.csproj`, the documents that count the right files | **yes** |
| the content of the catalog, read from `Tools\catalog.json`, which is in the repository | **yes** |
| **refuses a delivery in which nobody ran the bench** | **yes** |
| builds the bridge | only where the interop is (a self-hosted runner with SOLIDWORKS) |
| the MCP protocol, `Session.SameDocument`, the catalog exporter | only there |

**And what does not run does not turn green.** The groups that need the bridge write
an entry **"not runnable here"**: it is counted apart, printed, and ends up in the
JUnit report as `<skipped>` and in the summary of the run. The summary line gives the
number of checks, how many failed, how many were "not runnable here" and, in
brackets, how many were really done; the declared minimum is measured on the **real
ones** — a suite that is all skipped does not pass the threshold. *Skipping
in silence would have been the easiest way to make this file turn green, and it is the
defect the file itself exists not to have.*

> **The point about the bench is not an independent verification, and it is worth
> saying here.** No runner has a SOLIDWORKS license, so **the bench does not run in the
> CI**. What the CI reads is `Bench/report/last.json`: a file that **we produce and
> commit ourselves**. It checks that it declares a whole run, consistent with the
> library of now and later than the last change to the operations.
>
> It is a guard against **our own** distraction — the commit made without having rerun
> the bench — not against a report that said something false. If the report lied, the
> CI would believe it.
>
> *A defense told as stronger than it is weakens the true ones too, because it teaches
> people not to trust the label.*

### How well references hold when the part changes

Nineteen parts built, **a single upstream dimension changed**, and the downstream
geometry measured again. The result is one of three things, not two:

| | cases out of 19 | |
|---|---|---|
| **hold** | **17** | clean rebuild, and the geometry is where the intent says |
| **come off and say so** | **2** | the rebuild fails, a feature carries an error, or the change is refused: the designer sees it |
| **come off in silence** | **0** | rebuild fine, no error, and the geometry ended up somewhere else |

*Measured on 26 September 2026 on SOLIDWORKS 29.5.1, library 0.121.0. It is redone with
`.\Bench\bench.ps1 -Regression`; the case-by-case detail goes into the `Bench/report/`
folder.*

**On 15 September they were 12, 0 and 4 out of sixteen**, and the zero in the middle
was the most interesting number: no case had broken in a visible way, the four times
the part had come out different SOLIDWORKS had rebuilt clean and kept quiet. It is the
reason this suite measures **the position of the geometry** and not whether the rebuild
went through.

**The four silences had two causes, not four** (measured on 23 September 2026). Three
were the same: *an operation attached to a **face** follows the geometry; the `x_mm`/
`y_mm` coordinates inside the sketch plane of that face do not.* Since 25 September
`cylindrical_cut` and `rectangular_cut` take `anchor` — a dimension from an edge, or the
center between two edges, per axis — and the profile follows the edge:

- the centered pocket (`r03`) holds, and its twin without an anchor (`r03b`) stays where
  it was, as it should;
- a hole 20 from the edge (`r19`) holds, and so do the four holes guided 20 from the
  corners (`r01`, from library 0.119.0) — with the absolute twin `r01b`;
- the slot that leaves the material by shortening the plate (`r16`) is now **refused
  with a reason**, and the dimension goes back as it was: that is the middle row;
- slots and bosses take the anchor too (library 0.118.0 and 0.119.0).

The fourth (`r05`) cannot be repaired by anchoring, and for a while we wrote it down as
irreparable: a chamfer bigger than the material **does not fail**. SOLIDWORKS rebuilds
clean and eats a millimetre of width. It is caught by reading the part again afterwards
— and the sign was already there: the base feature loses a whole face, the one the
chamfer now takes entirely. Since library 0.121.0 `set_dimension` looks at exactly that:
if a feature that is not a cut loses faces and further on there is a chamfer or a
fillet, it **refuses, says which, and puts the previous value back**. `r05` moved to the
middle row, and the third is at zero.

The third row is written **even when it is zero**: a count that disappears when it is
zero is a count nobody checks.

## Constraints that are not negotiable

- **A SOLIDWORKS session must be open.** The bridge attaches to a session that is
  there; it does not start one.
- **Tested only on SOLIDWORKS 2021 SP5.1** (interop 29.5.1). Older versions can be
  tried when needed; newer ones are not tested. The installer accepts them all, and for
  a version different from the tested one it prints a warning.
- **No writing outside the open document.** The bridge does not save into the vault, does
  not touch libraries, does not install anything. The paths it is asked to write to are
  checked against the rules in `OCE_WRITE_ROOT` and `OCE_READ_ONLY_FOLDERS` above.
- **No operation that has not been measured.** If an operation has not been built inside
  the SOLIDWORKS software and checked with a number, it is not exposed. It is the rule
  that holds up all of OpenPDM, and it holds here too.

## Trademarks

SOLIDWORKS and 3DEXPERIENCE are registered trademarks of Dassault Systèmes SolidWorks
Corporation. This project is independent and **is not affiliated with, authorized,
sponsored or endorsed by Dassault Systèmes SolidWorks Corporation**: *not affiliated
with, authorized by, sponsored by or endorsed by Dassault Systemes SolidWorks
Corporation.*

The Apache-2.0 license of this project **grants no rights over trademarks**: its
section 6 says so, and it is worth saying here because a permissive license is easily
read as if it allowed everything.

The name SOLIDWORKS appears only as a description of the software this bridge talks to.
See [NOTICE](NOTICE), which says the same thing at more length — and it is repeated
here because **hardly anyone opens the NOTICE**.
