# The snapshot of W3.1-bis

`Controlli/lingua/snapshot.js` takes a picture of the repository once, before
the renames and the translation of W3.1-bis, and compares every later tree
with it (entry W3.1-bis.2, item 11). This file documents the tool, the format
of a snapshot directory, and what the comparison treats as a difference.

The snapshot itself is not versioned: by default it goes to
`note-interne/lingua/snapshots/<date>`, a folder git ignores. The tool, its
registry of scripts (`snapshot-scripts.json`) and this format are.

## What it is for

Every later step of W3.1-bis must show that it moved only what it meant to
move. The conditions it is checked against (section 1 of W3.1-bis, conditions
5 and 6, and the "done when" of steps .4, .5, .6 and .8):

- the bench gives the same outcome test by test, the identity of a test being
  `File.cs:Method:ordinal` and never the text of its label, with the same number
  of expectations checked per case, apart from the cases known to be unstable
  from the two reference runs;
- every script of `Controlli/` and `.github/passi/` gives the same exit code
  and the same counts, and no count drops to 0 in silence;
- every textual assertion of a case is still satisfied by a literal of the
  sources (none points to nothing);
- the catalogue and the MCP tools are the same, or differ exactly as the step
  says.

## Usage

```
node Controlli/lingua/snapshot.js take --ledger <tests.json> --ledger <tests.json> [options]
node Controlli/lingua/snapshot.js compare <snapshot dir> [--ledger <tests.json>] [options]
node Controlli/lingua/snapshot.js --self-test
```

The two ledgers of `take` are `Banco/report/tests.json` of two full bench
runs on the same tree (the format of `Banco/TestLedger.cs`). The ledger of
`compare` is the one of the run being judged; without it the tests are not
compared, and the comparison says so.

| option | meaning |
|---|---|
| `--root <dir>` | the tree (default: the repository the tool is in) |
| `--out <dir>` | `take`: the snapshot folder; it must not exist, or be empty |
| `--todo <TODO.md>` | the TODO the TODO probes read; default `$TODO_MD`, then `<root>/note-interne/TODO.md`. A `--todo` or a `TODO_MD` that names a missing file stops the tool |
| `--no-todo` | record the scripts that read the TODO as skipped |
| `--bridge <exe>` | the bridge; default `<root>/bin/oce-solid-agent-mcp.exe`. A `.js` file is a test double spoken to over stdio |
| `--tools-from auto\|stdio\|reflection` | how the tools are read (below) |
| `--tools-timeout <s>` | how long the bridge may take to answer over stdio (default 90; the reflection helper gets twice that). A bridge that does not answer in time is stopped, and the tool exits 2 |
| `--skip <section>` | do not take, or do not compare, a section; always said |
| `--skip-script <id>` | record one registry entry as skipped by the caller |
| `--with-probes` | also run the registry entries marked `probe` |
| `--show <n>` | differences printed per section (all of them are counted) |
| `--window <n>` | `compare`: the snapshot was taken before rename window `<n>` of W3.1-bis; its case folders, case names, case keys and the case part of the test identities (`File.cs:Method:ordinal@<case>`) are read through that window's renames, by the script of the window itself. Window 1 (`Controlli/lingua/window-1-cases.js`, W3.1-bis.4); window 2 (`Controlli/lingua/window-2-surface.js`, W3.1-bis.5) reads through window 1 first and then renames the operation, the path (`what`) and the value of every assertion, the snapshot's copy of the catalogue (rewritten as the window rewrites `Strumenti/catalogo.json`) and the list of tools (names, titles, schemas, the names the descriptions quote) |

Exit codes: 0 the snapshot is written, or no difference anywhere; 1 a
difference outside the unstable list (or a self-test check not as expected);
2 the tool cannot run (a missing or unreadable input, a script the registry does
not name, a source that does not parse, a bad argument); 3 no difference in
what was compared, but some section was not compared.

The tool never reads anything in silence. A missing catalogue, case folder,
case-keys file, project, ledger, bridge, TODO or input of a script stops it
with exit 2. A key it expects that has been renamed (in the catalogue, in
`Banco/case-keys.json`) stops it too: the tables at the top of `snapshot.js`
(`LAYOUT`, `CATALOGUE_KEYS`, `ASSERTION_KEYS`, `CASE_KEYS`, `CALLEES`) are
renamed in the same commit as what they name.

## The tools, and why the bridge is not always started

The tools are what the built bridge answers to `initialize` and `tools/list`.
`--tools-from stdio` starts the bridge and asks, as a client does. But the
bridge, started as a program, attaches at once to a running SOLIDWORKS
(`GetActiveObject`) and calls it. So when `tasklist` shows `SLDWORKS.exe`, or
cannot be read, the bridge is never started: `--tools-from reflection` (the
`auto` choice in that case) loads the bridge assembly in a small helper and
calls the method that answers the protocol, with the same objects the entry
point builds, without running the entry point. The helper finds that method by
its shape, not by its name, and stops if the shape is missing or ambiguous.
The source used is printed and recorded (`tools.json`: `source`, `why`).
`--tools-from stdio` is refused while SOLIDWORKS is running.

## The snapshot directory

Every file is UTF-8 JSON unless said otherwise.

### `snapshot.json`, the manifest

| key | content |
|---|---|
| `format` | 2 (since `inner`, 1/10/2026); a snapshot of another format is refused, never read in part |
| `taken` | ISO time |
| `root` | the tree it was taken from |
| `git` | `head`, `branch`, `dirty` (the lines of `git status --porcelain`: a snapshot of an uncommitted tree says so) |
| `versions` | `bridge` (`VERSION.txt`) and `library` (`AssemblyVersion`) |
| `inputs` | `ledgers` (file, sha256, mode, library, written, counts of each), `same_ledger_twice`, `todo` (file, sha256) |
| `sections` | for each section taken, its files |
| `skipped` | for each section not taken, the reason |

A snapshot is written into `<out>.partial-<pid>` and renamed at the end: a run
that stopped half way leaves no folder that looks like a snapshot.

### `catalogo.json` and `catalogue.json`

`catalogo.json` is `Strumenti/catalogo.json` byte for byte. `catalogue.json`
holds its `sha256_raw`, the `sha256_normalized` of its JSON with the keys of
every object sorted (spacing and key order do not count), its `top_keys`, and
one sha256 per operation (by name) and per family, so that the comparison says
which operation changed and in which fields.

### `tools.json`

`source` and `why` (above), `bridge` and `bridge_sha256`, `server` (name and
version: the version is recorded and printed, never a difference), the answer
to `initialize` without `serverInfo` and its normalized sha256, every tool of
`tools/list` as the bridge gives it (name, title, description, input schema,
annotations) and their normalized sha256, the standard error of the bridge,
and, for a reflection read, the dispatcher used and where the catalogue was
read from.

### `literals.json`

The calls of the refusal methods (`CALLEES`, today one method) in the sources
of the library and the bridge, which are the `<Compile>` items of the two
projects of `LAYOUT.projects`. Read through Roslyn (the copy inside MSBuild,
compiled once by `token-stream.js`): a comment is never a call. For each call:

| key | content |
|---|---|
| `id` | `file:method:ordinal`, the file from the root, the method named as the compiler names it for `[CallerMemberName]` (a lambda or a local function belongs to the member around it, a constructor is `.ctor`), the ordinal counting the calls of that member name in that file, in source order |
| `line`, `callee` | where, and which method |
| `args` | for each argument, its parts: constant text, and `null` for a value computed at run time |
| `text` | the arguments written out, a hole as `{*}`, arguments separated by ` \| ` |
| `units` | the string units inside the call |
| `inner` | the text of each of those units, a hole as `{*}`, in source order. `text` shows `cond ? "a" : "b"`, `string.Format("...", x)` or a helper called inside the call as one hole; their literals are only here (185 of the 1071 calls of 1/10/2026 hold such a literal, 235 have no constant text at all) |

`sources` lists every source file with its project, sha256 and counts.

### `strings.json`

Every string unit of the same sources. A unit is one piece of constant text as
the program builds it: a literal alone, or a chain of `+` whose literals are
joined and whose other operands are holes (`null`), or an interpolated string
with its holes. `id` is `file:member:sN`, numbered like the calls; `call` is the
id of the refusal call it is inside, or `null`.

### `assertions.json`

Every textual assertion of the bench cases. The cases are the JSON objects of
`Banco/cases/<folder>/` (the folder `regression` holds regression cases; an
array is a plan, not a case), walked through the contexts of
`Banco/case-keys.json`. The assertion keys are `ASSERTION_KEYS`: the message of
a must-fail block, `contains` and `not contains` of an expectation, and an
`equals` whose value is text (a number is not textual: those are only counted,
in `values_not_text`). A list of texts is one assertion per element.

| key | content |
|---|---|
| `id` | `<folder>/<file>#<path>`, the path in the case (`steps[2].must_fail.message_includes` since window 1 of W3.1-bis.4; a snapshot taken before it has `passi[2].deve_fallire.il_messaggio_contiene`) |
| `case`, `case_name`, `root` | the case file, its name, declared or regression |
| `step`, `operation`, `what` | the step it belongs to, its operation, the measure of the expectation |
| `kind`, `key`, `value` | message_contains, contains, not_contains or equals; the key; the text |
| `candidates` | the ids of every string unit whose constant text contains the value, ignoring case as the bench does (`strings.json` says which refusal call a unit is in) |
| `equal` | those of them where a whole constant part is the value |
| `template` | when no constant text contains the value: the units whose TEMPLATE does |

A message that carries a value computed at run time, `"the hole is " + w + "
mm wide"`, holds the assertion `the hole is 9 mm wide` in none of its constant
parts. Its template does: the assertion starts inside one constant run, ends
inside a later one, the runs between are whole, each hole (or format
placeholder such as `{0:0.##}`) stands for any text, and the constant text
covers more than half of the assertion, so that `(C2)` is not satisfied by
`"(" + x + ")"`.

`satisfied_by_a_template` and `without_candidate` list the assertions that only
a template satisfies, and the ones nothing satisfies. Most of the latter assert
on what SOLIDWORKS answers (a material, a feature name, a number read from the
part), not on a message of the sources. They are listed, not failed.

The big lists (`calls`, `units`, `assertions`, `tests`, `results`) are written
one element per line.

### `tests.json`

From the two reference ledgers: `ledgers` (their metadata),
`same_ledger_twice` and `reference`; `tests`, by identity: `label`, `group`,
`subject` and `outcome` as a pair (first ledger, second ledger, `null` when a
run lacks it); `cases`, by `<folder>/<name>`: `outcome`, `attempted` and
`checked` as pairs; `unstable_cases` (a different outcome, a different number
of expectations attempted or checked, or present in one run only, with the
reason) and `unstable_tests` (a different outcome, or present in one run only).

When the same ledger is given twice the unstable lists are empty by
construction, not measured, and the snapshot says so. That is good only to
prove that the tool runs.

### `scripts.json` and `scripts/`

`registry` and `folders` (from `snapshot-scripts.json`), and one result per
registry entry, in registry order:

| key | content |
|---|---|
| `id` | the script, then its arguments |
| `skipped` | the reason, for an entry that did not run |
| `exit` | the exit code, or `timeout`, or `not started: ...` |
| `counts` | every line of the output that holds a number, as `{ n: [numbers], text }`; the numbers are kept as written |
| `volatile_lines` | how many lines a `volatile_lines` pattern of the registry took out |
| `varies` | the sha256 (or the commit) of what `varies_with` names |
| `seconds`, `lines` | for the reader, never compared |
| `output_file` | the whole output under `scripts/`, standard error after a `--- stderr ---` line |

Every script runs from the root, with `TODO_MD` set to the TODO when there is
one, and without the variables that point git elsewhere (`GIT_INDEX_FILE`,
`GIT_DIR`, `GIT_WORK_TREE`...): a script that builds a throw-away repository
to test itself would otherwise write its index over the one they name.

Before the numbers are read, the output is normalized: line ends, ANSI
colours, Windows and POSIX paths, GUIDs, dates, clock times, durations,
sizes, three-part versions, hexadecimal hashes and the line numbers of file
references (`File.cs:123`) are masked. What is left is a count.

## The registry of scripts

`snapshot-scripts.json` names every script of its `folders`: a file with a
script extension, a file without extension that starts with `#!` (a git hook),
and a folder that holds C# sources (a compiled probe), written with a trailing
slash. In a git work tree only tracked files count. A script it does not name,
or an entry whose script is gone, stops the tool. Each entry runs (`run: true`)
or is skipped with its reason (`skip`): a SOLIDWORKS probe, a script that needs
SOLIDWORKS open, one that writes into the tree or acts outside this machine.
The keys of an entry are described in its `about`.

## What the comparison counts

| section | a difference | not a difference |
|---|---|---|
| catalogue | another normalized JSON: operations and families added, removed or changed (with the fields) | spacing or key order |
| tools | the answer to `initialize` or a tool added, removed or changed (with the JSON paths) | the server version |
| literals | a call `file:method:ordinal` vanished, new, with other `text`, or with other `inner` (a literal inside the call that its text shows as a hole: a branch of `?:`, the format of `string.Format`) | a call that only moved to another line |
| assertions | an assertion gone, changed, newly without a satisfying unit, one of the members (`file:member`) whose units satisfied it no longer satisfying it even though others still do, or new and unsatisfied | an assertion without a candidate in the snapshot and now (listed); a unit of the same member renumbered or reworded that still contains the text |
| tests | a new run that is not `full` or has tests without an identity; a stable test or case with another outcome, another number of expectations attempted or checked, missing from the run, or new | another label for the same identity (counted and printed) |
| scripts | another exit code, a count that drops to 0, other counts, an entry run then and skipped now (or the reverse), an entry added or gone | other counts when an input that `varies_with` names was recorded in the snapshot AND has another value now (printed apart), unless a count dropped to 0; a `varies_with` the snapshot did not record excuses nothing |

Why a lost member counts even when another literal still contains the text:
the words a case asserts on a refusal are often in a description of the
catalogue too. With the refusal translated, the description would keep the
assertion "satisfied" while the bench goes red. Seen on the real tree on
1/10/2026: the mate type a case asserts with `uguale` translated in
`StatoAssieme.cs`, and the same word still in the catalogue, compared as
"no difference" before this rule.

The unstable cases and tests of the snapshot, and the tests whose subject is an
unstable case, are printed in their own block with the reference values and
the new ones. They are never counted as differences and never dropped.

## The self-test

`node Controlli/lingua/snapshot.js --self-test` builds a fake tree (catalogue,
two projects with a refusal quoted in a comment, case keys, cases, two
scripts, a registry, a bridge double over stdio) and two fake ledgers that
differ on one case, takes a snapshot, and compares it with variants of the tree
and of the ledger, 28 checks: a changed literal, a changed literal inside a
refusal that its text shows as a hole, the literal an assertion relied on
gone, one of two members that satisfied an assertion gone, the template an
assertion relied on gone, a changed outcome, a run that is not full, a run
with unnamed tests, a count dropped to 0, other counts with a `varies_with`
the snapshot never recorded, the catalogue and a tool changed are red; the
same tree, a translated label, other counts with a recorded input that moved,
and a move of the unstable case alone are green; the same ledger twice gives
no unstable case and says it was not measured; one ledger, a missing ledger, a
missing snapshot file, an unnamed script, a bridge that never answers (stopped
after `--tools-timeout`, and no folder left behind) and a bridge that ends at
once are refused. Each of eight mutations of the tool (a comparison switched
off, no candidates, no templates, unstable cases compared as stable...) was
seen to turn at least one of its checks red, and so was each of six more on
1/10/2026 (inner literals ignored, the lost-member rule off, the run mode and
the unnamed tests not checked, the old `varies_with` excuse, the partial
folder kept). It runs with
`Prove.Contratti` (`SafetyNetSelfTests`); exit 2 (no Roslyn on the machine) is
"not runnable here", never green.

## What it does not do

- Renamed identities: the comparison matches tests, cases, literals and
  assertions by identity, exactly. `--window 1` reads the case folders, case
  names and case keys of a snapshot taken before window 1 through its renames
  (W3.1-bis.4); it is not in the self-test, and was seen on the real tree on
  1/10/2026: the assertions of the snapshot of that day matched one for one.
  `--window 2` adds the renames of window 2 (W3.1-bis.5): the operations,
  paths and values of the assertions, the catalogue and the tools, read by
  `window-2-surface.js` itself (`snapshotRenames`). The renames of window 3
  (files and methods of the sources) are still to be read: the literals
  section compares the string units of the sources, and a renamed name is a
  changed literal there.
- The reflection read of the tools and the stdio read are two ways to the same
  answers; their equality on one bridge has to be seen once with SOLIDWORKS
  closed (`take` twice, once with each `--tools-from`, and compare).
- Case is compared as JavaScript upper-cases text, which differs from the
  ordinal comparison of .NET only on characters that change length.
