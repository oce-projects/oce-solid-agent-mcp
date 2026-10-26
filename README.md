# OCE Solid Agent

[![Buy me a coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-d9342b?logo=buymeacoffee&logoColor=white)](https://buymeacoffee.com/oscarcontatoengineering)
[![Donate via PayPal](https://img.shields.io/badge/Donate%20via%20PayPal-0070ba?logo=paypal&logoColor=white)](https://www.paypal.biz/OSCARCONTATO94)

## Project information

![Bridge version](https://img.shields.io/badge/bridge-0.137.0-blue)
![Library version](https://img.shields.io/badge/library-0.160.0-blue)
![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)
![SOLIDWORKS 2021 SP5.1](https://img.shields.io/badge/SOLIDWORKS-2021%20SP5.1-red)
![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512bd4)
![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-green)
![MCP over stdio](https://img.shields.io/badge/MCP-stdio-black)
[![Tests](https://github.com/oce-projects/oce-solid-agent-mcp/actions/workflows/tests.yml/badge.svg)](https://github.com/oce-projects/oce-solid-agent-mcp/actions/workflows/tests.yml)

<!-- Every badge above is a fact or a real workflow, none is decoration. The two
     version badges are read against VERSION.txt and the AssemblyVersion of
     Oce.Solid.Agent.Core by Tests/ReadmeBadgesContract.cs: a release that moves
     one and not the other turns the contracts red. The last badge is the status of
     the workflow .github/workflows/tests.yml. There is deliberately no badge for
     code quality, lint or coverage: none of those services is configured here, and
     a badge of a service that is not configured is a lie. -->

*An independent project, not affiliated with Dassault Systèmes.*

An MCP server for the **SOLIDWORKS® software**: it lets any model — local or
remote — build a part, an assembly or a drawing through 102 operations, each one
measured inside the software, and above all **look at what came out**. Every
operation reads the document again after it runs and reports what SOLIDWORKS
software really did; a step that could not be verified is reported as such, never
assumed.

It installs with a single file and needs nothing more than an MCP client and a
SOLIDWORKS software installation with a license of your own.

> **Do you only want to use it?** → **[QUICKSTART.md](QUICKSTART.md)**, six steps,
> in plain English. If you are an AI assistant installing it on someone's behalf,
> the page written for you is **[INSTALL-FOR-AI.md](INSTALL-FOR-AI.md)**. This page
> is the overview: what it is, what it can do, and how far each claim has been
> checked.

> SOLIDWORKS® is a registered trademark of Dassault Systèmes SolidWorks
> Corporation. This project is independent and is not affiliated with, sponsored
> by or endorsed by Dassault Systèmes. It contains and redistributes no code or
> material of Dassault Systèmes: the interoperability libraries are loaded at
> start-up from **your** SOLIDWORKS software installation, which requires a license
> of your own.

## Contents

[Why it exists](#why-it-exists) · [Requirements](#requirements) · [Install](#install) ·
[Connect a client](#connect-a-client) · [The tools](#the-tools) ·
[The operations](#the-operations) · [Environment variables](#environment-variables) ·
[What is measured, and how](#what-is-measured-and-how) ·
[What is not proven](#what-is-not-proven) · [Known limits](#known-limits) ·
[Languages of SOLIDWORKS software](#languages-of-solidworks-software) ·
[Building from source](#building-from-source) · [Contributing](#contributing) ·
[License and trademarks](#license-and-trademarks)

## Why it exists

The hard part of letting a model draw is not asking it to draw: it is **measuring**
what came out. A part can be silently different from the one that was asked for —
the defect that raises no error — and the only way to find it is to read the part
again. This bridge puts that loop in the model's hands: every answer carries the
measurements of the step and the state of the document afterwards, and a view of
the part when you ask for one.

It was built as the measuring instrument of an assistant that draws parts in a
workshop (a local model proposes, a person confirms). With the bridge the same
part can be drawn by a large model and by a small one, by the same route and with
the same checks, so a missing tool can be told apart from a model that is not up
to it.

**What it is not.** It is not a way to let a model on the network change a
company's parts unattended. It works on the document that is open in a session you
control, and the refusals below (unsaved changes, write folders, undo that checks
the part) exist because of that.

## Requirements

| | |
|---|---|
| Windows | 10 or 11, 64-bit |
| SOLIDWORKS software | installed, **with your own license**. Measured on **2021 SP5.1** (interop 29.5.1); other versions are not tested (see [What is not proven](#what-is-not-proven)) |
| .NET Framework | 4.8 (included in current Windows 10 and 11) |
| An MCP client | Claude Desktop or Claude Code, which the installer connects by itself; any other client that starts a stdio server works with a hand-written entry |
| A running session | the bridge attaches to a SOLIDWORKS session that is **already open**; it does not start one |

You do not need Visual Studio, a .NET SDK, Python or an internet connection to
**use** it. Building from source is a different job, described [below](#building-from-source).

## Install

1. Download `Installer.exe` and `Installer.exe.sha256` from the
   [latest release](https://github.com/oce-projects/oce-solid-agent-mcp/releases/latest)
   and check the hash (QUICKSTART shows how).
2. Follow **[QUICKSTART.md](QUICKSTART.md)**. The installer is not code-signed, so
   Windows will warn you; QUICKSTART says why and what to click.
3. If an assistant does the installing for you, give it
   **[INSTALL-FOR-AI.md](INSTALL-FOR-AI.md)**: a procedure with the command, the
   expected exit code and the recovery for every step.

The installer writes to your own user folder, needs no administrator rights, adds an
entry named `solidworks` to the MCP clients it finds **without overwriting** the
servers already there (it keeps a backup first). Uninstalling removes its files and
its client entry again; the backup copies of the client configuration stay.

## Connect a client

The installer does this for you. By hand, the entry is:

```jsonc
// in the configuration of the MCP client
{
  "mcpServers": {
    "solidworks": {
      "command": "C:\\path\\to\\oce-solid-agent-mcp.exe"
    }
  }
}
```

With Claude Code the supported way to write the same thing is
`claude mcp add --scope user solidworks -- "C:\path\to\oce-solid-agent-mcp.exe"`.

Before connecting a client, a dry run is worth it:

```
oce-solid-agent-mcp.exe --self-test
```

It prints on stderr whether it attached, which library it loaded, how many
operations it knows and what it sees in the open document. It touches nothing.
Its exit code says the same in a number: 0 attached, 3 SOLIDWORKS session not
running, 4 not installed or not registered, 5 not tried (`OCE_NO_ATTACH`), 6 session
running but not answering (usually started as administrator while the client is
not), 1 anything else. The installer reads the number, not the text.

**A first session.** Open SOLIDWORKS software and a part, then ask the client to
read it (`inspect_part`). Only then ask for a change: the identifiers a plan needs
(`F1`, `E4`, `C1`) are born in that first reading. After every change, read again.

## The tools

Nine tools, not one per operation: exposing a tool for each of the 102 operations
would cost tens of thousands of characters of schemas in every request
and would make it easy to skip the first step. The operations sit behind `build`
and `measure`, and are discovered on demand.

| tool | what it is for |
|---|---|
| `inspect_part` | the state of the open document: bounding box, volume, bodies, features with their dimensions, faces and edges with their identifier, and a one-line `shape` that sums the part up. With `views` it also returns a **picture** of the part; with `section` a **cutaway**; with `faces` it lists the faces you ask for instead of the 60 largest |
| `list_operations` | the short list by family: a name and one line each |
| `describe_operation` | the complete schema of one operation |
| `build` | runs a sequence of operations and **reports what came out**: the measurements of every step and the state afterwards. With `dry_run: true` it validates the plan with SOLIDWORKS software closed and says what it could not check. A step can carry `expect`, the measurements it must come out with (`{"removed_mm3": 36}`): one that does not hold stops the plan there |
| `measure` | the same operations, but **only the ones that read** (family `checks`). It declares itself read-only, so the client does not ask for confirmation for a measurement |
| `undo` | withdraws the last plan, all of it — but only if the part is still as `build` left it |
| `document` | creates an empty part or assembly, opens a file from disk, lists the open documents, brings one to the front, closes one — and **refuses to close** a document with unsaved changes. It always says whether a file is open **read-only** |
| `api_search` | searches the whole SOLIDWORKS API by a piece of a name and answers with the **real signatures** read from the interop on this machine, with SOLIDWORKS software closed. Read only |
| `api_signature` | the card of one API member: every overload, the `out` parameters, the numbered family, and what the project's own measurements (`Tools/api_knowledge.json`) say about it. Read only |
| `mesh` | reads an STL file (binary or ASCII): `measure` gives the bounding box, the area, whether it is closed and then the volume; `section` cuts it square to an axis into closed contours ready for `extrude_profile`. Never touches SOLIDWORKS. Read only |

Why some of this is built the way it is:

- **`measure` is separate from `build` because the annotations sit on the tool.** A
  client reads `readOnlyHint` and `destructiveHint` before any call exists. With
  `build` alone it would ask for confirmation even for `mass_properties`, which does
  not move a cubic millimetre, and a person who confirms ten times in a row stops
  reading what they confirm. An operation that touches the part, sent to `measure`,
  is refused **before** the plan starts — never half a plan.
- **`inspect_part` comes first.** Identifiers are positional: they are renumbered by
  area at every reading and hold only for the reading that gave them. A plan built on
  an old reading works blind.
- **Seeing is not measuring, and both are offered.** `views` accepts `isometric`,
  `trimetric`, `dimetric`, `front`, `back`, `left`, `right`, `top` and `bottom` (at
  most three per call, because a view costs far more than the whole written state).
  Taking a picture restores the orientation of the view afterwards, even when the
  picture fails halfway. `section: {plane: "right"}` cuts **only the picture**, never
  the geometry, and removes itself after it has been taken.
- **A small face is not lost behind the 60 largest.** Every face has an identifier —
  the rank of its area among all the faces of the part — and `faces` filters by
  `normal`, `area_min_mm2` and `area_max_mm2`, `near_mm` with `within_mm`, `body` and
  `limit`; the state says `faces_total`, `faces_matched` and `faces_not_listed`.
- **The shape can be said.** The `shape` field is a sentence such as
  *"Bounding box 140 x 16 x 140 mm, volume 191938.7 mm3 (that is 61% of the bounding
  box), 1 hole Ø60 (center 0,0,0), 6 holes Ø11 on a circle Ø90 at a pitch of 60
  degrees"*. The profile
  of a part of revolution is written only when the lengths add up to the bounding
  box and the rebuilt volume to the real one; otherwise the sentence stays silent
  rather than plausible and wrong.
- **You can go back, and remove from the middle.** `undo` withdraws the last plan in
  one step and refuses if the volume or the number of features changed since (someone
  worked by hand in the meantime). `suppress_feature` turns a feature off (or back
  on); `delete_feature` deletes it and **refuses when anything depends on it**,
  saying what, because SOLIDWORKS software would take the children away without
  asking.

## The operations

`Tools/catalog.json` lists 102 operations in 12 families. It is an exported copy,
written by the bridge itself (see [Tools/update.md](Tools/update.md)), and holds only
the operations **measured inside the SOLIDWORKS software**: an operation that exists
in the library but could not be shown to create what it promises stays out of the
file, so a client cannot call it. The bench prints both numbers in its header
("runnable: 102 operations (in catalog: 102)").

Each operation carries its limits and its refusals in the description the model
reads. The same operations are the ones the OpenPDM add-in runs, so what is tested
here is what the add-in does.

| family | what it does | on which document |
|---|---|---|
| `extrusions` | the first body of a part: extrusions, revolve, gear, sweep, loft | a part |
| `features` | add or remove material on an existing body: cuts, holes, fillets, chamfers, patterns, mirror, shell, draft, rib, dimension edits | a part |
| `sheet_metal` | flanges, hems, bends and the flat pattern as DXF | a part |
| `weldments` | bars, frames, trims, gussets, end caps, weld beads, cut list | a part (`weldment_profiles` only reads a folder) |
| `references` | planes, axes, sketch pictures | a part |
| `dimensions` | dimensions and tolerances on the part itself | a part |
| `physical_properties` | material | a part |
| `assemblies` | insert, mate, mirror, fix and replace components | an assembly |
| `drawing` | sheets, views, dimensions, notes, balloons, tables, symbols, PDF | a drawing (`create_drawing` starts from a part or an assembly) |
| `exports` | STL, STEP, IGES | a part or an assembly (`export_stl` and `export_iges` want a part) |
| `identity` | custom properties and configurations | a part or an assembly (`set_property` takes any document) |
| `checks` | measurements and checks without touching the part: mass, bounding box, distances, interference, title block, dependencies | mostly a part; `interference_check` wants an assembly, `title_block` a drawing |

A check enforces this table: the contract suite compares the families of the served
catalog with the document types the executor admits, and turns red if the bridge
promises a family that it would refuse before the first step.

## Environment variables

None is required. They exist to move files and to restrict what the bridge may do.

| | |
|---|---|
| `OCE_DRAWING_LIBRARY` | which `Oce.Solid.Agent.Core.dll` you expect to be used. It does **not choose it**: .NET loads the one next to the executable. It is there to tell you if the two differ |
| `OPENPDM_ADDIN` | the old name of `OCE_DRAWING_LIBRARY`, still read |
| `OCE_CATALOG` | where `catalog.json` is; if missing, next to the executable |
| `OCE_QUESTION_REGISTRY` | where the bridge writes **what it was asked for and was not there**: one JSON line per question, only the name and the nearby names, never the arguments. A `.jsonl` file; if the variable is missing it is written next to the executable (the uninstall removes it); **set to an empty string it means "record nothing"**. If it cannot be written, the bridge stays quiet and goes on |
| `SW_INSTALL_PATH` | where the SOLIDWORKS software is installed: the **root**, the one that ends with `\SOLIDWORKS`. It holds both for building and for running — `api\redist` is derived from it |
| `SW_REDIST` | the old name, still read. It wanted the `api\redist` subfolder and not the root; if both are set, `SW_INSTALL_PATH` wins |
| `OCE_LOG` | where the bridge writes **its own log**: one JSON line per tool call (start and end, milliseconds, the time of each phase, whether the answer is an error, how many characters it has; for `build` the names of the operations, never the values of the arguments) and every line it writes on stderr. A folder; if the variable is missing, `%LOCALAPPDATA%\Oce.Solid.Agent\logs`, one file per day (`bridge-yyyy-MM-dd.jsonl`). Set to `off`, `0`, `false` or an empty string it writes nothing. The log carries local paths: it stays on the machine. If it cannot be written, the bridge stays quiet and goes on |
| `OCE_API_SEQUENCE` | set to `0`, `false` or `off`, the bridge **does not** tell SOLIDWORKS software that a sequence of API calls is coming (`ISldWorks.CommandInProgress`). By default it sets it for the length of every call, and only when it was off: if another program left it on, the bridge leaves it alone and a plan is refused as before. The switch exists to measure the two ways one against the other |
| `OCE_WRITE_ROOT` | the only folder the bridge may write into. If it is set, everything else on the disk is out |
| `OCE_READ_ONLY_FOLDERS` | folders the bridge must not write, separated by `;` — for example `D:\CAD;D:\LIBRARIES`. They add to the system ones, they do not replace them. The bridge never writes under Windows, System32, Program Files, Program Files (x86) or ProgramData either, with or without this variable |
| `OCE_WELDMENT_PROFILES` | the folder of the weldment profiles (`.sldlfp`) that `weldment_profiles` reads. No default: the installer writes it into the client entry's `env`, from `--weldment-profiles <folder>`, or from the folder SOLIDWORKS software is set to (Tools > Options > File Locations > Weldment Profiles), or from the one it installs (`<SOLIDWORKS>\lang\<language>\weldment profiles`) |
| `OCE_NO_ATTACH` | any value except empty, `0` and `false`: the bridge starts, answers the protocol and lists its tools, but **does not attach to SOLIDWORKS software** and says so; `--self-test` exits 5. For a session busy with a batch job or the bench, and for the installer's own tests |

`OCE_DRAWING_LIBRARY` no longer decides which library to load, and is still read on
purpose: if it points elsewhere, the bridge **says so**. An environment variable that
does nothing is worse than one that does not exist — whoever set it keeps believing
it counts.

The Dassault interoperability libraries are **not in this repository**: they are
taken from the installation, so the version is always the one that is running.

## What is measured, and how

This project's premise is that a claim without a number is an opinion. The figures
below are the ones of the current release (library 0.153.1, run on SOLIDWORKS software
29.5.1, i.e. 2021 SP5.1, with bridge 0.130.1; 0.130.2 changes nothing the bench
exercises). Each says what was run and what it
does **not** say.

**1. The bench: the operations against the real software.** `Bench/` builds parts
inside SOLIDWORKS software through the library, reads the result back and compares
it with expectations written beforehand (a number, a tolerance, and why).

> **Full run: 2,248 tests, 0 failed, 1,824 of 1,824 expectations verified.** One test
> is skipped on purpose (it belongs to the `--slow` mode). The same tree and the same
> library were run twice: with SOLIDWORKS software in **Italian**, and with its
> interface and feature names switched to **English**. Each run takes about five
> hours. The record of the Italian run is committed as `Bench/report/last.json`; the
> contract suite refuses a delivery where the library is newer than that record.

Commands and the structure of a case are in [Bench/README.md](Bench/README.md). To
repeat the English run: turn on «Use English language menus» in SOLIDWORKS software,
restart it, then run `.\Bench\bench.ps1 -SolidWorksLanguage en` (the bench switches
«Use English language feature and file names» by itself, and puts it back at the end).

**2. The contract suite: the bridge without the software.**
`Tests\bin\Oce.Solid.Agent.ContractTests.exe` runs hundreds of checks (the exact count is
in [Tests/README.md](Tests/README.md)) in about two minutes, with no SOLIDWORKS software:
the MCP protocol over stdio, the catalog, the badges and numbers of this page, the
consistency of the sources. A check that finds nothing always passes, so the suite
also checks that its own checks can fail.

**3. An autonomous run: can a model, alone, make the part?** Fifteen tasks written in
a workshop's words, with no operation names, each measured on the **finished part**
by expectations fixed before the run (`Bench/tasks/`). Sessions of a Sonnet-class
Claude model, driven by Claude Code, worked through the bridge. The question asked of this
run was narrow: does moving the bridge to English, with SOLIDWORKS software in
Italian or in English, make the model worse?

| | right sessions | of which with rows to read by hand |
|---|---|---|
| bridge before the move to English, Italian software (3 sessions per task) | 26 of 45 | 9 |
| bridge now, Italian software (3 per task) | 24 of 45 | 9 |
| bridge now, English software (1 per task) | 9 of 15 | 3 |

Tasks 03, 08 and 09 have rows the scorer cannot read by itself (such as one component
of an assembly); every other row of those sessions held, and the scorer marks them
"right, N rows by hand". They are counted as right in the table above, the
same way on both sides. The verdict below does not lean on them: the comparison of
the scorer counts those three tasks as not right, before and after.

The rule was to accept at most one task of fifteen getting worse: it held, at exactly
that limit. The one is task 12 (weight and envelope of a flange): the weight is right
in all three Italian sessions of the current bridge, but the model reads it from
`inspect_part` and never calls
`mass_properties`, which sits in family `checks` while the family they browse,
`physical_properties`, lists only the material. Six of the fifteen tasks are wrong in
every Italian session, before and after, so those are defects of the model, the task
or the measure, not of the language. **This run says that translating did not make things worse. It does not say
how good the model is.**

**4. The installer on machines that are not ours.** A GitHub workflow
(`installer-smoke.yml`) takes the release candidate of this version and runs it on two
clean Windows machines (`windows-2022` and `windows-latest`), four scenarios each: no
SOLIDWORKS software (exits 1, writes nothing, touches no client file); install,
verify, reinstall, uninstall; install with `--config`; the default folder. All green.
A GitHub runner has no SOLIDWORKS software, so a stand-in folder is used and the
bridge starts with `OCE_NO_ATTACH`: this proves the **mechanics** of the installer
(files, client entries, handshake, reinstall that keeps what you added, clean
uninstall), not that the bridge attaches.

**5. The CI.** On every push to `main`, every tag and every pull request, GitHub
builds and runs the contract suite and checks that
a bench record exists and matches the library. It does **not** build the bridge and
does not run the bench: both need Dassault's interop, which is not in the repository
and not on a GitHub runner. What cannot run is reported as "not runnable here",
counted separately and never green. The check on the bench record is a guard against our
own oversight — a commit made without rerunning the bench — not an independent
verification: the record is produced and committed by the maintainer, and if it lied
the CI would believe it.

## What is not proven

- **Other SOLIDWORKS software versions.** Everything above is 2021 SP5.1. Older and
  newer versions may work; the installer accepts them and prints a warning, but no
  figure here applies to them.
- **A genuinely English (or any other non-Italian) installation with company
  templates.** The English runs used an Italian installation with the interface and
  feature-name switches turned to English. The part template, drawing template and
  weldment-profile library were the company's own, in Italian, in both runs. A
  machine whose templates and profile libraries are in English, or in another
  language, has not been run.
- **Long sessions.** After about four hours of continuous driving, SOLIDWORKS
  software stopped answering twice (once in a bench run, once in the autonomous run).
  The bench run was discarded and run again in full on a fresh session; in the
  autonomous run the three tasks not yet done were run again on a fresh session. The
  figures above come from the runs that completed. A session of many
  hours without a restart has not been shown to be stable.
- **The bridge attaching, in the installer test.** Only a machine with a license can
  show that; it is covered by the bench and by the installer's own local tests, not by
  the clean-machine run.
- **Reference stability.** The suite that changes one upstream dimension on 19 parts
  and checks whether the downstream geometry follows (`.\Bench\bench.ps1 -Regression`)
  is not part of the 2,248, and no figure for it is given here.
- **The quality of any model.** See the third measurement above.

## Known limits

- **A SOLIDWORKS session must already be open**: the bridge attaches to it and never
  starts one.
- **Only measured operations are exposed.** There is no generic "call any API method"
  tool yet: `api_search` and `api_signature` read the API, but the changes go through
  the 102 operations.
- **Identifiers are positional** and valid for one reading; plans must name faces from
  a fresh `inspect_part`.
- **Writes are fenced** by `OCE_WRITE_ROOT` and `OCE_READ_ONLY_FOLDERS`, and a
  document with unsaved changes is never closed for you.
- **A language the table does not know falls back to English** and the state says so
  in `solidworks_language`.
- **The restart warning for «Use English language menus» never fires**: SOLIDWORKS
  software reports English as soon as that switch is turned on, before the restart that
  makes it true, so until the restart the bridge may pick the English column while the
  software still writes in its own language. After changing that switch, restart the
  software yourself.
- **The installer is not code-signed**, so Windows SmartScreen shows a warning.
- **Pull requests are not merged yet and there is no support commitment**; see
  [Contributing](#contributing).

## Languages of SOLIDWORKS software

**The project is in English**: the ten tools and their sub-actions, the 102
operations and their families, every argument and the value of every argument whose
schema lists one, the selectors (`@face(normal=front)`), the fields of every answer,
the keys of `Tools/catalog.json`, the MCP resources (`catalog://operations`,
`catalog://operation/{name}`), the texts of the answers, the code, its comments and
these documents. A name the bridge does not know (an old one, a foreign one, a typo)
is an unknown name: an unknown tool is an `isError` answer that lists the tools there
are, an unknown operation is refused with the operations it resembles, an argument the
schema does not have is reported as ignored, and a value the schema does not list is
refused. Nothing is translated on the way in. New text is written in English, and a
language gate (`Checks/language/detect-italian.js`) holds the Italian at zero, in the
pre-push hook and in the CI: the tree is judged by itself, outside the signed exceptions
of `Checks/language/exceptions.json`, and a change to the gate itself needs a
`Language-Gate-Config-Change` trailer on its commit.

**What comes from SOLIDWORKS software stays in the language of the installation, and
the bridge reads it in Italian and in English.** The names it gives to features,
sketches, planes and configurations (`Boss-Extrude1`, `Sketch1`, `Front Plane` in an
English installation, their translations in an Italian one), the custom properties of
your templates and the messages it writes are read and passed on as they are; an
operation that takes such a name takes it as the installation spells it. What the
bridge has to recognise by itself it reads from the API wherever the API allows (a
feature by its type, a base plane by its normal, a standard view by its rotation, a
table column by its type); the rest is in one table,
`Oce.Solid.Agent.Core/Localization/solidworks-names.json`, with a column per
language. The column is chosen by `ISldWorks.GetCurrentLanguage` and, for the names of
features, documents and sheets, by the switch «Use English language feature and file names».

| language of the software | status |
|---|---|
| Italian | measured: full bench green, autonomous run done |
| English | measured: full bench green, autonomous run done, **on an Italian installation with the English switches** (see [What is not proven](#what-is-not-proven)) |
| any other | not measured: falls back to the English column and says so in `solidworks_language`; things found by type or by API still work, things found by a word may not |

Adding a language is a new column in that table. That Italian column, and the other
Italian text the repository keeps on purpose (names stored in the company templates
and in the Italian weldment-profile library the bench uses, the history in the
changelog, the words the tools that read the maintainer's notes look for), are listed
in `Checks/language/exceptions.json`, 28 entries, each with its reason and a
signature; the language gate does not count them.

## Building from source

Skip this if you only want to use the bridge. `bin\` is in `.gitignore`: after a
clone it does not exist.

```
.\build.ps1
```

You need MSBuild (the **Build Tools for Visual Studio 2022** are enough) and
SOLIDWORKS software installed on the machine: the interoperability libraries are not
in this repository and are taken from there. The script builds the bridge into `bin\`,
regenerates `Tools\catalog.json`, builds the probes in `Checks\`, and ends with
`dist\Installer.exe` and its checksum `dist\Installer.exe.sha256`. Other switches:
`-Clean` (delete `bin`, `obj` and `dist` first) and `-BridgeOnly`. If SOLIDWORKS
software is not in the usual place, name its root folder (not `api\redist`):

```
.\build.ps1 -SwInstallPath 'D:\Programs\SOLIDWORKS Corp\SOLIDWORKS'
```

Right after the clone, once only:

```
.\Checks\install-hooks.ps1
```

It turns on the `pre-push` hook: before a push it runs the repository's own checks,
the language gate and `check-publishability.ps1`, and **stops the push** if one fails
(the last one fails if the tree or the history would let out a path of the machine, a
user name or a private network address). Git does not version `.git\hooks`, so until
you run that line the hook is in the repository and is not on.

To run the tests: the contract suite needs no license, only MSBuild —

```
msbuild Tests\Oce.Solid.Agent.ContractTests.csproj -p:Configuration=Release
Tests\bin\Oce.Solid.Agent.ContractTests.exe
```

— and `.\Bench\bench.ps1` needs SOLIDWORKS software and a clean desk. See
[Tests/README.md](Tests/README.md) and [Bench/README.md](Bench/README.md). Every code
change bumps the version in `VERSION.txt` and adds an entry to
[CHANGELOG.md](CHANGELOG.md), where the history of this project lives: what was
measured, what was found wrong, and when.

## Contributing

**Issues are open and welcome** — bug reports, measurements that contradict what is
written here, API behavior seen on a SOLIDWORKS software version not tested. A report
is most useful in this form: *I asked for X, the bridge said "done", the part is
actually Y, and here is how I measured Y.* **Pull requests will not be merged for
now**, because there is no Contributor License Agreement yet; describe your fix in an
issue instead. There is **no commitment to answer issues or to keep any interface
stable between versions**: pin a version and read the changelog before moving.
Everything is in [CONTRIBUTING.md](CONTRIBUTING.md); security reports go through
[SECURITY.md](SECURITY.md).

## License and trademarks

Apache-2.0, see [LICENSE](LICENSE) and [NOTICE](NOTICE).

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
