# SolidWorks MCP — versioni

> **LINGUA DI QUESTO FILE — deciso il 16 settembre 2026 (voci W3.1 / W3.2).**
> Il repository passa all'inglese. **Questo file no, non all'indietro.** Le voci
> storiche restano in italiano e **non si traducono**: raccontano cosa si
> chiamavano le cose *allora*, e *un changelog riscritto e' un changelog che
> mente* (regola della voce I1, che resta in piedi). Le voci **nuove** si
> scrivono in inglese, a partire dalla prima successiva a questa riga. Il
> confine e' qui, ed e' dichiarato invece che implicito.
>
> **Superseded on 28 September 2026 (entry W3.1-bis, decision D2):** the historical
> entries below are translated too, in W3.1-bis.10, and the paragraph above is
> rewritten then. New entries were already in English.

**REGOLA, non consuetudine: ogni modifica al codice alza la versione in
`VERSION.txt` e aggiunge una voce qui.**

Perche' e' una regola: questo ponte serve a MISURARE, e una misura senza il
numero di versione di cio' che l'ha prodotta non e' una misura — e' un
aneddoto. Se fra un mese una prova dice una cosa diversa da oggi, la prima
domanda sara' «con quale versione?», e deve avere una risposta.

Criterion (reduced semver MAJOR.MINOR.PATCH):

- **MINOR +1** when the bridge gains visible tools or capabilities;
- **PATCH +1** for fixes;
- **MAJOR** stays 0 until the tool protocol is stable.

---

## Unreleased — H19 L1+L2: the installer tells the truth and verifies itself (branch `installatore-l1`) · bridge 0.126.0 · library 0.149.0 (unchanged)

**Lots L1 and L2 of TODO entry H19** (the installer finished for v0.1.0; the
audit of the installer kept with the internal notes, defects 1-7). New options and
texts are born in English; the existing Italian option names (`--silenzioso`,
`--cartella`, `--senza-client`, `--disinstalla`, `--redist`, `--config`, `--aiuto`,
the bridge's `--prova`) stay as they are in this lot and are renamed, with no
aliases, in L2-bis (decision L0a). MINOR raised: new user-visible options. The
library (`Riscontro.Disegno`) is not touched, so its version stays 0.149.0 and the
bench has not run on this branch: the three bench-freshness checks of
Prove.Contratti are red until it does, and they are the only red ones (790 checks;
`Prove/LEGGIMI.md` said 785).

### Exit codes that do not lie (B1)

`Installa.exe --silenzioso` exited 0 when no client had been wired (the exceptions of
`Collega` were printed and swallowed; with no client found nothing was printed).
Now, checked in this order (`Installer/Programma.cs`, constants `Exit*`):

| code | silent install |
|---|---|
| 1 | nothing installed: a prerequisite failed, `--weldment-profiles` names a folder that does not exist, the copy failed, or an unhandled error |
| 3 | files installed, but a client that should have been wired was not (malformed or locked config, the `claude` command and the file both failed), or no client was found; the block to paste is printed after `Paste this into the MCP client's configuration file` |
| 2 | installed and wired, but the bridge's self-test did not confirm (its code is not 0, 3 or 5) |
| 0 | installed, every client tried is wired (or `--senza-client`), self-test 0, 3 or 5 |

3 is stricter than "no client at all": one client failing while another is wired
also gives 3, so no exception of `Collega` turns into a 0. Uninstall: 0 done, 1 a file
of the installation stayed, 3 files gone but a client entry left. `--verify`: 0 all
green, 2 only warnings, 1 something failed.

### A real console, a log, and the launch forms measured (B2)

`Installer/Output.cs`: silent, uninstall and verify modes attach to the parent's
console (as `--aiuto` already did) unless standard output is already a file or a
pipe; `--log <file>` writes the same lines to a UTF-8 file. Measured on 1-2 October
2026 in a fresh console window, no redirection anywhere: 0.125.0 with a run that
stops at the prerequisites (exit 1), 0.126.0 with a malformed client config (exit 3).

| form | exit code seen | console output |
|---|---|---|
| PowerShell `& .\Installa.exe ...` | none: PowerShell does not wait | 0.126.0 yes (after the prompt); 0.125.0 none |
| PowerShell `Start-Process -Wait -PassThru -NoNewWindow` → `.ExitCode` | right (1; 3) | 0.126.0 yes; 0.125.0 none |
| PowerShell `cmd /c Installa.exe ...` | right | 0.126.0 yes; 0.125.0 none |
| PowerShell `cmd /c "start "" /wait Installa.exe ..."` (one line) | **wrong: 0 both times** | 0.126.0 yes; 0.125.0 none |
| cmd batch file: `Installa.exe ...` then `%ERRORLEVEL%` | right | 0.126.0 yes; 0.125.0 none |
| cmd batch file: `start "" /wait Installa.exe ...` then `%ERRORLEVEL%` | right | 0.126.0 yes; 0.125.0 none |
| PowerShell `& .\Installa.exe ... \| Out-Null` | right (a pipe waits) | into the pipe |

INSTALL-FOR-AI gives the second form for PowerShell and `start "" /wait` in a batch
file for cmd, and warns against the one-line `cmd /c "start /wait"`. The table is
also in the comment of `Output.cs`; `prova-installatore.js` launches those two forms
(B2 cases).

### The self-test answers with a code, and the installer reads it (B3)

`Ponte/Sessione.cs`: `AttachFailure` (None, NotRunning, NotInstalled, Skipped, Other)
and `FailureOf(hresult)`; `Programma.cs`: `--prova` exits **0** attached, **3**
SOLIDWORKS not running (MK_E_UNAVAILABLE), **4** not installed or not registered
(CO_E_CLASSSTRING, REGDB_E_CLASSNOTREG), **5** not tried (`OCE_NO_ATTACH`), **1**
anything else; 2 is left to the installer. With code 3 and a `SLDWORKS` process
running it adds that SOLIDWORKS may run as administrator (the ROT is not shared
across integrity levels). The human sentences stay. The installer (`Passi.Prova`,
`SelfTestAccepted`) and the window read the code; the two entries of
`Controlli/lingua/consumed-strings.json` that bound the installer to the bridge's
sentences (`installer-bridge-standing`, `installer-solidworks-not-open`) are dropped
in this commit, and two new consumers of the installer's English output enter
(`installer-paste-header`, `installer-uninstall-file-left`): 39 entries, the floor.
`Prove/H19InstallerContract.cs` (7 checks): each HRESULT gives the promised code by
reflection, every `AttachFailure` has a code and none is 2, and the numbers in
`Programma.cs` and `Installer/Passi.cs` agree (seen red on purpose with the installer's
`SelfTestSkipped` changed to 6: "SelfTestSkipped bridge 5, installer 6").

### `OCE_NO_ATTACH` (new variable of the bridge)

Any value except empty, `0`, `false`: the bridge starts, answers the protocol, lists
its tools, does not attach, and says so; `--prova` exits 5. Needed because the
installer runs the installed bridge (self-test, and the `--verify` handshake), which
attached to the running SOLIDWORKS and read its active document: with the bench
running, `prova-installatore.js` could not run at all. It sets the variable unless
given `--attach`. In the README table.

### Uninstall is coherent (A5)

`--disinstalla` removes the client entry too, unless `--keep-client` (decision L0b:
the author's default; TODO H15 point 3 stays the owner's to change), and only an
entry whose command is the bridge in the uninstalled folder; an entry pointing at
another installation is left alone and named. With `--config` only that file. The
window does the same through the same code. It removes `domande-senza-risposta.jsonl`
(`Passi.BridgeOwnFiles`), and exits 1 naming the file when one stays.

### Weldment profiles reach the bridge (A6)

`--weldment-profiles <folder>` and a field in the window write `PROFILI_SALDATURA`
into the client entry's `env` (also into the block to paste and into
`claude mcp add -e`). A folder that does not exist stops the run with exit 1 before
anything is installed. Without the option, `Passi.FindWeldmentProfiles` proposes a
folder only if one exists. Checked on this machine (SOLIDWORKS 2021 SP5.1): (1)
`HKCU\Software\SolidWorks\SOLIDWORKS <year>\ExtReferences`, value
`Weldment Profile Folders`, is the folder SOLIDWORKS itself uses (Tools > Options >
File Locations); it exists here and is used first; (2) `<SOLIDWORKS root>\lang\<language>\weldment profiles`
exists for all 15 language folders (ANSI, ansi inch, AS, BSI, CISC, DIN, GB, iso,
JIS), language taken from the same key's `Weldment Cut List Template Folders` /
`Weld Table Template Folder`, else english. The year comes from the interop's major
version. Not checked on another SOLIDWORKS version or a machine where the setting
was never touched.

### Stale texts (A7)

`--aiuto` no longer lists `--addin` and lists the new options and the measured
launch forms; the "SOLIDWORKS installed" check names `--redist` and `SW_INSTALL_PATH`
instead of a Browse button that does not exist; `TrovaRedist` reads `SW_INSTALL_PATH`
(the root) before `SW_REDIST`, like the bridge, and `Passi.Prova` hands the bridge the
redist the installer chose.

### `Installa.exe --verify [--json]` (B4)

`Installer/Verify.cs`: files and versions (bridge from its `VERSION.txt`, library,
installer); each client's `solidworks` entry runs the installed exe or not, and its
`PROFILI_SALDATURA`; `api\redist` and the SOLIDWORKS year; the self-test code; an MCP
handshake on the installed exe (`initialize`, `notifications/initialized`,
`tools/list` over stdio, every wait with a timeout, replies matched by id). One JSON
line with `--json`, `[ok]`/`[~]`/`[NO]` lines without.

### Claude Code through `claude mcp add` (B5)

`Installer/ClaudeCli.cs`: when `claude` (`.exe`, `.cmd`, `.bat`) is on PATH and no
`--config` is given, Claude Code is wired with `claude mcp remove --scope user
solidworks` then `claude mcp add --scope user solidworks -e "K=V" ... -- "<exe>"`
(and unwired with `claude mcp remove`); if it is absent or fails, the file is written
as before and the output says so. **The overwrite risk, measured first**
(`Controlli/claude-json-watch.js`, read only, 180 s while Claude Code ran on this
machine): 5 rewrites of `%USERPROFILE%\.claude.json`, shortest gap about 20 s, no lock
file, and a leftover `.claude.json.tmp.<pid>.<hex>`: Claude Code writes a temporary
file and renames it. So the installer's "file locked?" check can never see it, and a
session that writes back a copy read before the installer wrote would drop the entry
with no error. **Not measured:** whether Claude Code re-reads the file before each
write (no live session was put under test), and the real `claude mcp add` (there is
no `claude` on PATH on this machine: the case uses a fake `claude.cmd`). Both stay for
the owner (H19 L4/B8).

### `Controlli/prova-installatore.js`: 55 cases

Red first on the installer of 47d0f8f (c7ab519 plus `OCE_NO_ATTACH` only): **21 PASS,
28 FAIL**; green on 0.126.0: **55 PASS, 0 FAIL**, without touching SOLIDWORKS. New
cases: B1 (malformed config → 3, untouched, block printed; `--senza-client` → 0; a
prerequisite → 1, nothing created; case 6 → 0; case 4 → 3), A7 (no Browse button;
`--aiuto` without `--addin` and with the new options; `SW_INSTALL_PATH`), B2 (`--log`;
Start-Process and `start "" /wait` both report 3), B3 (`--prova` with `OCE_NO_ATTACH`
→ 5), B4 (one JSON line; files present; bridge version; entry → installed exe; the
tool count equals an independent handshake: 9; self-test 5; exit 2; readable lines;
empty folder → 1), A6 (env written, other servers intact; missing folder → 1, nothing
installed, config untouched), A5 (entry removed, others kept, `.jsonl` and folder
gone; `--keep-client`; an entry elsewhere left alone; a locked file → 1, named), B5
(fake `claude.cmd` on PATH found; remove then add with `-e`; success and failure
reported), case 3 (exit 0, entry gone, with `--config`).

**Expected results that need SOLIDWORKS or the owner** (not run here):
`node Controlli\prova-installatore.js --attach` with the bench stopped (the self-test
attaches; `--verify` should then exit 0 with SOLIDWORKS 2021 open and a client
wired); self-test 3 with SOLIDWORKS closed and 4 on a machine without it; the
elevated case (SOLIDWORKS as administrator: self-test 3 plus the warning); the real
`claude mcp add` and a live Claude Code session during an install; the window by
hand (new weldment profiles field, uninstall result); a clean machine (H6 cases 1, 4,
5).

### Review fixes (adversarial review of L1+L2, inside and outside the diff)

- **Claude Code was "installed" on every machine** (high). Its file sits in the
  profile, and "installed" meant "the folder of the file exists": the profile always
  does. A silent install with no client at all wrote a `.claude.json` nobody reads and
  exited 0; the "no MCP client found → 3" branch could never run. Now Claude Code is
  there when its `.claude.json` exists, or its own folder (`%USERPROFILE%\.claude`, or
  `CLAUDE_CONFIG_DIR`), or `claude` is on PATH (`Configurazione.ClaudeCodePresent`).
  Its file is `.claude.json` in `CLAUDE_CONFIG_DIR`, else in `%USERPROFILE%`, as Claude
  Code 2.1.281 reads it (`Configurazione.ClaudeCodeFile`): with `CLAUDE_CONFIG_DIR`
  set, `claude mcp add` and `--verify` looked at two different files.
- **A reinstall dropped the entry's own variables** (medium). The file route replaced
  the whole `solidworks` entry and `claude mcp remove` + `add` did the same:
  `RISCONTRO_RADICE` / `RISCONTRO_SOLA_LETTURA` (where the bridge may write) or a
  `PROFILI_SALDATURA` set by hand vanished on update, silently.
  `Configurazione.MergedEnv` keeps every variable; the installer sets or removes
  `SW_REDIST` and sets `PROFILI_SALDATURA` only when it has one. Without
  `--weldment-profiles` a folder already in a client entry that exists wins over the
  one the installer would find, and the output says "kept from the existing client
  entry" (the window prefills it the same way).
- **No backup on the `claude mcp` route** (medium): `Configurazione.Backup` (the old
  backup code, now public) runs before `claude mcp remove`/`add` and before the
  uninstall's `claude mcp remove`.
- **SOLIDWORKS as administrator was "installed", exit 0** (medium). Self-test 3 with a
  `SLDWORKS` process present was accepted like a closed SOLIDWORKS, the hint was one
  line among the others. Now `--prova` exits **6** when attach says "not running" and a
  `SLDWORKS` process runs in this Windows session (another user's session does not
  count); the installer does not accept 6 (silent exit 2, window red, `--verify`
  fail). `Programma.SelfTestCodeSeen` is the pure rule, checked by the contracts.
- **Quoting for `claude`** (low). `ClaudeCli.Quote` only escaped quotes: a value
  ending in `\` (`PROFILI_SALDATURA=D:\Profiles\`) became `\"` and swallowed the next
  arguments. Now the `CommandLineToArgvW` rules (backslashes doubled before a quote
  and at the end); an argument with `%` is not sent through `cmd.exe` (it would be
  expanded) and the file route is used instead. `--weldment-profiles` is stored
  without a trailing backslash.
- **`--verify` all green beside a malformed config** (low): a client whose config
  cannot be read was treated like one without an entry. Now it is a warning naming the
  file (`readError` in the JSON row), and the clients line names the clients that have
  no entry.
- **The B5 cases were red on the network drive** (test): they loaded the installer
  with `LoadFrom`, which the drive the repository lives on refuses (0x80131515,
  measured). Now from the bytes.
- Smaller: the new Browse button of the window in English; the registry path in the
  comment of `Passi.FindWeldmentProfiles` had lost its backslashes; the skip reason of
  `prova-installatore.js` in `snapshot-scripts.json` no longer says it attaches;
  `Prove/LEGGIMI.md` counts the checks of a run with the TODO present (798; the 790
  written before came from a run without it, and the run on the main copy would have
  been red).

Seen red first: the new cases of `prova-installatore.js` on the installer of bda8946
(**56 PASS, 6 FAIL**; the install cases of the no-client block refused to run there,
because that installer would have written the real profile), then **65 PASS, 0 FAIL**;
the new contracts on the bridge and installer of bda8946 (quoting:
`[PROFILI_SALDATURA=D:\Weld Profiles" C:\Some]`), then green. The self-test 6 case
with a fake `SLDWORKS.exe` runs only with no real SOLIDWORKS open: NOT run here.

**The real `claude mcp add`, measured** (it was "not measured" above): Claude Code
2.1.281 (`%APPDATA%\Claude\claude-code\2.1.281\claude.exe`, the one Claude Desktop
ships, not on PATH here) put on PATH for one run, with `CLAUDE_CONFIG_DIR`,
`USERPROFILE` and `APPDATA` pointing into a scratch folder. First a probe entry with
another name proved the sandbox (it landed in the scratch `.claude.json`, the real one
did not change). Then `Installa.exe --silenzioso` into a folder with spaces: exit 0,
backup made, "registered with claude mcp add --scope user", entry
`{"type":"stdio","command":"...\inst dir with spaces\oce-solid-agent-mcp.exe","args":[],
"env":{"PROFILI_SALDATURA":"S:\..."}}` (a value with spaces, through `-e`);
`--verify --json`: files, solidworks, clients, weldment profiles, handshake ok,
self-test warn (`OCE_NO_ATTACH`), exit 2; a variable added by hand to the entry
survived a reinstall through `remove` + `add`; `--disinstalla` exit 0, "removed with
claude mcp remove", the probe entry left alone. The real `.claude.json` and Claude
Desktop config were unchanged. Still not measured: a LIVE Claude Code session that
rewrites `.claude.json` while the installer writes it by the file route (L4, B8).

## Unreleased — W3.1-bis.5, rename window 2: the MCP surface (branch w31bis-5) · bridge 0.127.0 (with H19 L1+L2) · library 0.150.0

Merged together with H19 L1+L2 (below), on a branch rebased on main e49d4d6.
`Banco/banco.ps1` also counts the documents left open from the last line of
`ChiudiScarti.exe --chiudi`: the late-bound `GetDocumentCount()` of e49d4d6
fails from PowerShell (TYPE_E_ELEMENTNOTFOUND), so the restart before a full
run never fired.

Bridge 0.126.0 · library 0.150.0.

**The second of the three rename windows of W3.1-bis (TODO entry W3.1-bis.5):
the protocol of the bridge is English.** The seven tools and their
sub-actions, the 99 operations and their 12 families, every argument and the
value of every argument whose schema lists one, the selectors, the fields of
every answer and the value of every response field the map covers, the keys
of `Strumenti/catalogo.json`, the MCP resources and the tool titles get the
English names of the name map (`Controlli/lingua/name-map.tsv`, W3.1-bis.3),
and so do the bench cases that speak that protocol, the readers of the
catalogue and every code name quoted in a message, a comment or a document.
The texts themselves (descriptions, messages, comments) stay in the language
they were written in: only the names inside them change. Classes, files and
folders of the code are window 3 (W3.1-bis.6). A few values outside any enum
(`mate`'s `variant` and `alignment`, the `kind` of a mate, a drawing view, a
selection or `check_body_geometry` in the state) are not in the map yet and
stay Italian: W3.1-bis.8.

### The script of the window

- **`Controlli/lingua/window-2-surface.js`**, driven by the map (types tool,
  sub_action, operation, catalogue_family, argument, enum_value,
  catalogue_key, response_field, response_value, selector), in one pass over
  every tracked text file but the excluded ones (this CHANGELOG, the map and its
  extractor, the gate: `detect-italian.js`, its lexicon, its exceptions and
  baseline, `language-gate.ps1`; window 1's script):
  - the catalogue token by token: keys, families, operation names, schema
    properties, enums, `required`, the examples, and the code names in the
    descriptions; `Riscontro.Disegno/Catalogo/Operazioni.cs` gets the same
    renames, so the catalogue the library exports is byte for byte the one the
    script writes;
  - every case and plan: operations, arguments (by the operation of the step),
    values (by the enum of the argument), selectors
    (`@faccia(verso=davanti)` → `@face(normal=front)`), the paths and the
    values of the expectations, the pieces a message must contain, and the
    code names in the free notes;
  - the string literals of the C#, JavaScript and PowerShell sources by their
    code context (an argument read with `Str(a, "…")`, a field written with
    `misure["…"]`, a step of a plan, a key of an answer), with a table of
    sites (`CS_SITES`) for the words that have two senses in the map (`a_mm` is
    `throat_mm` in `drawing_weld_symbol` and `offset_mm` in `section_view`;
    `formato` is `paper_size` for `create_drawing` and `set_sheet`,
    `sheet_format` for `title_block`); every row of the table must be used by
    the run, or it stops;
  - the code names in messages, comments and documents: only between
    backticks, guillemets or quotes, as a whole snake_case token, a selector or
    a resource URI. **A common word is never replaced inside prose** («la
    quota», «il foro» stay as they are), and the names an Italian SOLIDWORKS
    gives (`Taglio-Estrusione1`, «Piano frontale»; class
    `solidworks-localized` of `exceptions.json`) are never touched.
- JSON is rewritten token by token: indentation, line ends, key order and the
  hand-made layout of every case stay byte for byte.
- `--reverse` runs the window backwards with the ledger the forward run writes
  (`Controlli/lingua/window-2-reverse.json`, 122 entries in 34 files): the words
  the map read backwards would not give back, a name that was already English
  before the window (`feature`, `faces`, `orientation`) or a form the map merges with another.
  `--reverse-proof <rev>` and `--forward-proof <rev>` compare the JSON files of
  the window (catalogue, cases, plans, tasks) with another revision; `--check`
  says whether the tree is after the window (it is in the snapshot registry);
  `--todo` and `--notes` rename the code names of notes outside the repository.
  A forward run on a tree that is already after the window stops (it would
  read the English names as the names before it): `--check` is the second run.
- **`Controlli/lingua/update-names-in-notes.js`** drives `--notes` and `--todo`
  on explicit paths (the TODO, the CLAUDE.md files, `INSTALL-FOR-AI.md` copies,
  the memories, the RAG notes), with `--dry-run`, `--list` and `--self-test`.
  It touches code names only (decision D10).
- `snapshot.js compare --window 2` reads a snapshot taken before the window
  through its renames (operations, paths and values of the assertions, the
  catalogue, the tools), so a renamed name is the same one, not one gone and
  one new.

### What changed

606 files rewritten by the script, 431 of them the cases and plans of the
bench: 1374 operations, 695 catalogue keys,
1078 argument blocks and 394 plan keys, 1425 expectation paths, 113
expectation values, 43 message pieces, 281 free notes of cases, 4356 string
literals of the sources, 971 messages and 1011 comments that quote a code name.

- **The readers of the catalogue in the same commit**:
  `Controlli/conta-provate.js` and `Controlli/controlla-catalogo.ps1` read
  `operations`, `families`, `name`, `family`; the bridge, the bench, the
  contracts and `Controlli/expected-keys-test.js` read the English keys too.
  Same counts as the snapshot of 1/10/2026.
- **The resources**: `catalog://operations` and `catalog://operation/{name}`
  (were `catalogo://operazioni`, `catalogo://operazione/{nome}`).
- **The tool titles**: «Inspect the open part», «List the operations»,
  «Describe an operation», «Build in the part», «Measure the part, without
  touching it», «Withdraw the last plan», «Open, create or pick the document».
- **The arguments the bench writes in place**: an initializer reads as an
  answer unless the line says otherwise, and where the map gives the field and
  the argument different names the site table says which one the line is:
  `paper_size` for `create_drawing` (`Banco/NumeriDelleQuote.cs`,
  `Banco/Orfane.cs`), `variant` for `hole_wizard` (`Banco/Program.cs`,
  `Banco/ProvaNuove.cs`), `measured_as` and the segment ends `start`/`end`
  inside `rectangular_frame` (`Banco/Program.cs`). The library echoes the
  paper size as `paper_size` (`create_drawing`, `set_sheet`) and the sheet
  format of `title_block` as `sheet_format` (the notes of the map).
- **The facings**: the face a selector or a mate names by its normal
  (`up`, `down`, `right`, `left`, `front`, `back`), in the selectors, in the
  `facing` field of the state, in `face_a`/`face_b` of `mate` and in the bench
  (`Banco/Attese.cs` maps `normal` to `facing`).
- **D12, the examples of the catalogue with a SOLIDWORKS name**: they were
  copied from cases run on an Italian installation (`Taglio-Estrusione1`,
  `Svuota1`, `Foglio1`, `D1@Schizzo2`); they now carry the name an English
  installation gives (`Cut-Extrude1`, `Shell1`, `Sheet1`, `D1@Sketch2`), and
  the description of the argument says that the name is the installation's, in
  its language, to be copied from the state (8 arguments of 8 operations).
- The README section «Language» is rewritten: what is English, what comes from
  SOLIDWORKS in the language of the installation, and the aliases below.

### The Italian names, accepted on input until the first release (decision D4)

- **`Riscontro.Disegno/Alias.cs`**: the dictionary is the name map itself,
  embedded in the library (`Riscontro.Disegno.csproj`, `name-map.tsv`), so the
  rows that renamed the surface are the rows that read the old names. A key is
  translated only to a name the schema of the operation has at that level (the
  two senses of `a_mm`: `throat_mm` in `drawing_weld_symbol`, `offset_mm` in
  `section_view`), a value only to a value the schema lists, a selector field
  by field (`normal=front`). Input only: the answers, the catalogue,
  `describe_operation` and reflection speak English.
- **Where**: `OperazioniSw.Esegui` translates a step for the callers of the
  library that do not come through the bridge (the bench, the add-in) and
  writes `measurements.deprecated`; the switch moved to
  `ExecuteWithCurrentNames`, and `Controlli/controlla-catalogo.ps1` reads the
  `case` labels there. The bridge translates in `Attrezzi.Chiama`, before any
  of its checks: the tool, its arguments, the action of `document`, the family
  of `list_operations`, the operation of `describe_operation`, the steps of a
  plan with their arguments; the answer carries the lines in
  `structuredContent.deprecated` and as its last text item. The bench reads
  its selectors through the same dictionary (`Attese.SelectorAlias`) and adds
  them to the step's `measurements.deprecated`.
- **How it warns**: one line per name, «old» is deprecated, use «new» (the
  Italian names are accepted until the first release).
- **The bench**: `Banco/cases/aliases/`, one case per kind of name (an
  operation, an argument, a value, a selector), each checking the part and the
  deprecated line. The window and its proofs leave the folder alone, and
  `conta-provate.js` does not count it (it exercises the dictionary, not an
  operation).
- **The contract** `AliasLayerContract` (14 checks): the library by reflection
  (an operation step, its arguments and value, an English step left alone,
  `a_mm` in its two operations, a value the schema does not list, a selector)
  and the bridge over stdio with a dry run (an Italian tool, plan and step give
  the dry run of the English plan and list all ten old names; an Italian
  family and operation are answered in English only). The Italian names come
  from the map, not from the contract. **Seen red** with the map not embedded:
  11 of the 14 fail (the Italian tool is refused as unknown).
- **The bench counts them**: every step whose answer carries `deprecated` adds
  its lines to its case, and the run ends with a line `alias uses: N in M cases;
  outside the alias cases: K` that `banco.ps1` writes into `last.json` as
  `alias_uses`. K must be 0: a case of before the rename would otherwise hide
  behind the aliases until they go. `Alias.cs` and its contract are excluded
  from the window, which would otherwise rename the old names they quote.
- **Removed in W3.1-bis.11**, before the tag: `Alias.cs`, the embedded map, the
  calls in `OperazioniSw.Esegui`, `Attrezzi.Chiama` and `Attese`, the cases of
  `Banco/cases/aliases`, the contract.

### The tasks of the autonomous run (D9)

- **The measured fields are real**: the tables of `Banco/tasks/*.md` named 14
  fields no operation writes (`holes` for every way of making a hole,
  `holes_on_circle`, `slots`, `hems`, `cut_list_rows`, the three
  `envelope_*`, `mirrored`, `bom_quantity_total`, `cut_list_matches`,
  `geometry_check_ok`, `export_succeeded`, `bodies_in_file`, `volume_mismatch`)
  and six more a field under another name (`interferences` is the list, the
  count is `how_many`; `balloons_placed`; `length_mm` and `width_mm` that no
  sheet-metal step writes). 20 rows are rewritten on what the bridge and the
  bench do write (`diameters_mm`, `bounding_box_mm`, `measurements.<field>`
  of a named step).
- **`Banco/tasks/run-autonomous.ps1`** hands each task to `claude -p` with the
  bridge as the only MCP server and keeps the transcript and the state of the
  finished part; **`Banco/tasks/score-autonomous.js`** scores RIGHT, WRONG or
  NOT DONE per session, the median per task, and `--compare` is the threshold
  of D9 (at most one task of fifteen worse). Self-test without SOLIDWORKS; the
  run itself needs SOLIDWORKS and has not been made. The commands are in
  `Banco/tasks/README.md`. The Italian version of the tasks is not in the
  repository.

### The renames (old → new)

The full list is the map (`Controlli/lingua/name-map.tsv`): 205 arguments and
689 answer fields are not repeated here.

**Tools** (7)

| Italian | English | Italian | English |
|---|---|---|---|
| `annulla` | `undo` | `descrivi_operazione` | `describe_operation` |
| `disegna` | `build` | `documento` | `document` |
| `elenco_operazioni` | `list_operations` | `guarda_il_pezzo` | `inspect_part` |
| `misura` | `measure` | | |

**Sub-actions** (5)

| Italian | English | Italian | English |
|---|---|---|---|
| `apri` | `open` | `attiva` | `activate` |
| `chiudi` | `close` | `elenca` | `list` |
| `nuovo` | `create` | | |

**Operations** (99)

| Italian | English | Italian | English | Italian | English |
|---|---|---|---|---|---|
| `accoppia` | `mate` | `applica_materiale` | `apply_material` | `asola` | `slot` |
| `attiva_configurazione` | `activate_configuration` | `attiva_foglio` | `activate_sheet` | `bollatura` | `balloons` |
| `cambia_estrusione` | `edit_extrusion` | `cartiglio` | `title_block` | `combina_corpi` | `combine_bodies` |
| `cordone_saldatura` | `weld_bead` | `crea_asse` | `create_axis` | `crea_configurazione` | `create_configuration` |
| `crea_disegno` | `create_drawing` | `crea_piano` | `create_plane` | `croci_di_centro` | `center_marks` |
| `datum_in_tavola` | `datum` | `dettaglio_in_tavola` | `detail_view` | `dipendenze` | `file_dependencies` |
| `distanza_minima` | `minimum_distance` | `distinta_base_in_tavola` | `drawing_bom` | `distinta_taglio` | `cut_list` |
| `distinta_taglio_in_tavola` | `drawing_cut_list` | `elimina_lavorazione` | `delete_feature` | `esporta_iges` | `export_iges` |
| `esporta_pdf` | `export_pdf` | `esporta_step` | `export_step` | `esporta_stl` | `export_stl` |
| `estrudi_cerchio` | `extrude_circle` | `estrudi_profilo` | `extrude_profile` | `estrudi_rettangolo` | `extrude_rectangle` |
| `estrudi_sezione` | `extrude_section` | `estrudi_su_faccia` | `extrude_on_face` | `fazzoletto` | `gusset` |
| `filettatura_cosmetica` | `cosmetic_thread` | `fissa_componente` | `fix_component` | `flangia_base` | `base_flange` |
| `flangia_spigolo` | `edge_flange` | `foro_guidato` | `hole_wizard` | `guscio` | `shell` |
| `immagine_nello_schizzo` | `sketch_picture` | `imposta_foglio` | `set_sheet` | `incidi_testo` | `engrave_text` |
| `ingombro_corpo` | `body_bounding_box` | `inserisci_componente` | `insert_component` | `lega_quota` | `link_dimension` |
| `loft_fra_due_cerchi` | `loft_two_circles` | `membro_strutturale` | `structural_member` | `misura_fra_entita` | `measure_between` |
| `modifica_quota` | `set_dimension` | `nervatura` | `rib` | `nota_in_tavola` | `note` |
| `nuovo_foglio` | `new_sheet` | `orlo` | `hem` | `piano_per_tre_punti` | `plane_three_points` |
| `piega_da_schizzo` | `sketched_bend` | `profili_saldatura` | `weldment_profiles` | `proprieta_di_massa` | `mass_properties` |
| `quota_in_tavola` | `drawing_dimension` | `quote_dal_modello` | `model_dimensions` | `raccordo_faccia` | `fillet_face` |
| `raccordo_spigoli` | `fillet_edges` | `raccordo_variabile` | `variable_fillet` | `riordina_lavorazione` | `reorder_feature` |
| `ripeti_circolare` | `circular_pattern` | `ripeti_lineare` | `linear_pattern` | `ripeti_riempimento` | `fill_pattern` |
| `rivoluzione` | `revolve` | `rugosita_in_tavola` | `surface_finish` | `ruota_dentata` | `spur_gear` |
| `saldatura_in_tavola` | `drawing_weld_symbol` | `salva_documento` | `save_document` | `schema_di_quote` | `dimension_scheme` |
| `scrivi_proprieta` | `set_property` | `sezione_in_tavola` | `section_view` | `sformo` | `draft` |
| `smusso_spigoli` | `chamfer_edges` | `sopprimi_istanza` | `suppress_instance` | `sopprimi_lavorazione` | `suppress_feature` |
| `sostituisci_componente` | `replace_component` | `specchia` | `mirror` | `specchia_componente` | `mirror_component` |
| `sposta_corpi` | `move_bodies` | `sviluppo_dxf` | `flat_pattern_dxf` | `sweep_circolare` | `circular_sweep` |
| `tabella_fori_in_tavola` | `hole_table` | `taglia_membri` | `trim_members` | `taglio_cilindrico` | `cylindrical_cut` |
| `taglio_rettangolare` | `rectangular_cut` | `tappo_estremita` | `end_cap` | `telaio_rettangolare` | `rectangular_frame` |
| `tolleranza_geometrica_in_tavola` | `geometric_tolerance` | `tolleranza_quota` | `dimension_tolerance` | `variabile_globale` | `global_variable` |
| `verifica_geometria_corpo` | `check_body_geometry` | `verifica_interferenze` | `interference_check` | `verifica_sovrasporgenze` | `check_overhangs` |
| `vista_modello` | `model_view` | `vista_sviluppo` | `flat_pattern_view` | `viste_standard` | `standard_views` |

**Catalogue families** (12)

| Italian | English | Italian | English |
|---|---|---|---|
| `assiemi` | `assemblies` | `esportazioni` | `exports` |
| `estrusioni` | `extrusions` | `identita` | `identity` |
| `lamiera` | `sheet_metal` | `lavorazioni` | `features` |
| `proprieta` | `physical_properties` | `quote` | `dimensions` |
| `riferimenti` | `references` | `saldature` | `weldments` |
| `tavola` | `drawing` | `verifiche` | `checks` |

**Catalogue keys** (9)

| Italian | English | Italian | English | Italian | English |
|---|---|---|---|---|---|
| `descrizione` | `details` | `documento` | `document` | `esempio` | `example` |
| `famiglia` | `family` | `famiglie` | `families` | `nome` | `name` |
| `operazioni` | `operations` | `precondizione` | `precondition` | `sommario` | `summary` |

**Selectors** (20)

| Italian | English | Italian | English |
|---|---|---|---|
| `@corpo` | `@body` | `@faccia` | `@face` |
| `@quota` | `@dimension` | `@spigolo` | `@edge` |
| `angolare` | `angular` | `asse` | `axis` |
| `curvo` | `curved` | `diametro_mm` | `diameter_mm` |
| `lungo` | `length` | `nome` | `name` |
| `piana` | `planar` | `raggio_mm` | `radius_mm` |
| `tipo` | `kind` | `verso` | `normal` |
| `verso=alto` | `normal=up` | `verso=basso` | `normal=down` |
| `verso=davanti` | `normal=front` | `verso=destra` | `normal=right` |
| `verso=dietro` | `normal=back` | `verso=sinistra` | `normal=left` |

**Enum values (argument=value)** (128)

| Italian | English | Italian | English |
|---|---|---|---|
| `caratteristica=cilindricita` | `characteristic=cylindricity` | `caratteristica=circolarita` | `characteristic=circularity` |
| `caratteristica=concentricita` | `characteristic=concentricity` | `caratteristica=inclinazione` | `characteristic=angularity` |
| `caratteristica=oscillazione_circolare` | `characteristic=circular_runout` | `caratteristica=oscillazione_totale` | `characteristic=total_runout` |
| `caratteristica=parallelismo` | `characteristic=parallelism` | `caratteristica=perpendicolarita` | `characteristic=perpendicularity` |
| `caratteristica=planarita` | `characteristic=flatness` | `caratteristica=posizione` | `characteristic=position` |
| `caratteristica=profilo_linea` | `characteristic=profile_of_line` | `caratteristica=profilo_superficie` | `characteristic=profile_of_surface` |
| `caratteristica=rettilineita` | `characteristic=straightness` | `caratteristica=simmetria` | `characteristic=symmetry` |
| `condizione=quartabuono` | `end_condition=miter` | `condizione=taglio` | `end_condition=cut` |
| `condizione=testa1` | `end_condition=butt1` | `condizione=testa2` | `end_condition=butt2` |
| `disposizione=cerchio` | `layout=circle` | `disposizione=quadrato` | `layout=square` |
| `disposizione=sopra` | `layout=above` | `disposizione=sotto` | `layout=below` |
| `documento=assieme` | `document=assembly` | `documento=nessuno` | `document=none` |
| `documento=parte` | `document=part` | `documento=parte/assieme` | `document=part/assembly` |
| `documento=qualunque` | `document=any` | `documento=tavola` | `document=drawing` |
| `dove=dopo` | `placement=after` | `dove=prima` | `placement=before` |
| `genere=asse` | `kind=axis` | `genere=corpi` | `kind=bodies` |
| `genere=estremita_libere` | `kind=free_ends` | `genere=faccia_del_foro` | `kind=hole_face` |
| `genere=faccia_di_fondo` | `kind=pocket_floor` | `genere=faccia_inferiore` | `kind=bottom_face` |
| `genere=faccia_superiore` | `kind=top_face` | `genere=spigoli_inferiori` | `kind=bottom_edges` |
| `genere=spigoli_laterali` | `kind=side_edges` | `genere=spigoli_superiori` | `kind=top_edges` |
| `genere=tutti_gli_spigoli` | `kind=all_edges` | `lato=alto` | `side=top` |
| `lato=basso` | `side=bottom` | `lato=destra` | `side=right` |
| `lato=sinistra` | `side=left` | `lavorazione=asportazione` | `process=material_removal` |
| `lavorazione=qualsiasi` | `process=any` | `lavorazione=senza_asportazione` | `process=no_removal` |
| `misure=ingombro` | `measured_as=overall` | `misure=interasse` | `measured_as=center_distance` |
| `modo=istanza` | `mode=instance` | `modo=opposto_legato` | `mode=opposite_linked` |
| `modo=opposto_slegato` | `mode=opposite_unlinked` | `operazione=interseca` | `operation=intersect` |
| `operazione=sottrai` | `operation=subtract` | `operazione=unisci` | `operation=union` |
| `origine=alto_destra` | `origin=top_right` | `origine=alto_sinistra` | `origin=top_left` |
| `origine=basso_destra` | `origin=bottom_right` | `origine=basso_sinistra` | `origin=bottom_left` |
| `piano=destro` | `plane=right` | `piano=frontale` | `plane=front` |
| `piano=superiore` | `plane=top` | `posizione=centro` | `position=center` |
| `posizione=fine` | `position=end` | `posizione=inizio` | `position=start` |
| `precondizione=corpo_solido` | `precondition=solid_body` | `precondizione=corpo_solido_e_faccia` | `precondition=solid_body_and_face` |
| `precondizione=corpo_solido_e_spigolo` | `precondition=solid_body_and_edge` | `precondizione=documento_senza_corpo` | `precondition=document_without_body` |
| `precondizione=feature_e_direzione` | `precondition=feature_and_direction` | `profilo=poligonale` | `profile=polygonal` |
| `profilo=triangolare` | `profile=triangular` | `qualita=grossolana` | `quality=coarse` |
| `rappresentazione=solidi` | `representation=solids` | `rappresentazione=superfici` | `representation=surfaces` |
| `su=fori` | `applies_to=holes` | `tipo=accoppiamento` | `variant=fit` |
| `tipo=angolare` | `variant=angle` | `tipo=angolo` | `variant=fillet` |
| `tipo=aperto` | `variant=open` | `tipo=asola` | `variant=slot` |
| `tipo=assieme` | `variant=assembly` | `tipo=bilaterale` | `variant=bilateral` |
| `tipo=chiuso` | `variant=closed` | `tipo=esagono` | `variant=hexagon` |
| `tipo=filettato` | `variant=threaded` | `tipo=indentata` | `variant=indented` |
| `tipo=lamato` | `variant=counterbore` | `tipo=mezza_v` | `variant=half_v` |
| `tipo=nessuna` | `variant=none` | `tipo=parte` | `variant=part` |
| `tipo=passaggio` | `variant=clearance` | `tipo=piatto` | `variant=flat` |
| `tipo=primo_livello` | `variant=top_level` | `tipo=simmetrica` | `variant=symmetric` |
| `tipo=solo_parti` | `variant=parts_only` | `tipo=svasato` | `variant=countersink` |
| `tipo=testa` | `variant=butt` | `tipo=tondo` | `variant=round` |
| `tipo=tubo` | `variant=tube` | `tipo=tubolare` | `variant=tubular` |
| `tolleranza=geometrica` | `tolerance=geometric` | `tolleranza=piu_meno` | `tolerance=plus_minus` |
| `trattamento_angolo=nessuno` | `corner_treatment=none` | `trattamento_angolo=quartabuono` | `corner_treatment=miter` |
| `trattamento_angolo=testa1` | `corner_treatment=butt1` | `trattamento_angolo=testa2` | `corner_treatment=butt2` |
| `verso=orizzontale` | `orientation=horizontal` | `verso=verticale` | `orientation=vertical` |
| `vista=destra` | `view=right` | `vista=dietro` | `view=back` |
| `vista=dimetrica` | `view=dimetric` | `vista=fronte` | `view=front` |
| `vista=isometrica` | `view=isometric` | `vista=sinistra` | `view=left` |
| `vista=sopra` | `view=top` | `vista=sotto` | `view=bottom` |
| `vista=trimetrica` | `view=trimetric` | `viste=destra` | `views=right` |
| `viste=dimetrica` | `views=dimetric` | `viste=frontale` | `views=front` |
| `viste=inferiore` | `views=bottom` | `viste=isometrica` | `views=isometric` |
| `viste=posteriore` | `views=back` | `viste=sinistra` | `views=left` |
| `viste=superiore` | `views=top` | `viste=trimetrica` | `views=trimetric` |

**Response values** (48)

| Italian | English | Italian | English | Italian | English |
|---|---|---|---|---|---|
| `Catalogo delle operazioni` | `Operation catalog` | `Destra` | `Right` | `Montaggio` | `Assembly` |
| `Revisione` | `Revision` | `Sinistra` | `Left` | `Una operazione` | `Single operation` |
| `assieme` | `assembly` | `assieme illeggibile` | `unreadable assembly` | `binario` | `binary` |
| `centro` | `center` | `cerchio` | `circle` | `chiesta` | `requested` |
| `chiuso` | `closed` | `cieca` | `blind` | `completamente definito` | `fully defined` |
| `composito` | `composite` | `diametro del tondo` | `round diameter` | `diametro esterno` | `outer diameter` |
| `documento` | `document` | `esterne` | `outer` | `esterno` | `outer` |
| `estrusione` | `extrusion` | `frontale` | `front` | `il documento` | `the document` |
| `ingombro` | `overall` | `interne` | `inner` | `interno` | `inner` |
| `linee` | `lines` | `lunghezza del tondo` | `round length` | `millimetri e chilogrammi` | `millimeter kilogram` |
| `nessuno` | `none` | `non disponibile` | `unavailable` | `orizzontale` | `horizontal` |
| `passante` | `through_all` | `piano medio` | `mid plane` | `poligonale` | `polygonal` |
| `primo` | `first` | `quartabuono` | `miter` | `quota da smusso` | `chamfer dimension` |
| `solidi` | `solids` | `solido` | `solid` | `taglio` | `cut` |
| `tavola` | `drawing` | `tavola illeggibile` | `unreadable drawing` | `terzo` | `third` |
| `triangolare` | `triangular` | `unisci` | `merge` | `verticale` | `vertical` |

## Unreleased — W3.1-bis.4, rename window 1: the bench cases (branch w31bis-4)

**Bench, 2/10/2026, on e49d4d6 (W3.1-bis.3, Z-C21.14 and this window together):
2179 tests, 0 failed, 1 skipped (`--lento`), expectations 1771 of 1771.** Three
runs before it did not count, for causes outside the code: run 31 on a
SOLIDWORKS session of ~4,900 documents (window resources exhausted: rebuilds
skipped in silence), run 32 killed by a network-drive hiccup in Tee-Object
(c008f7a writes the live output locally), run 33 on a 7-hour session (centre
marks and dimensions not inserted; the same families green on a restarted
SOLIDWORKS). e49d4d6 restarts SOLIDWORKS before a full run.

**The first of the three rename windows of W3.1-bis (TODO entry W3.1-bis.4):
the cases of the bench get English keys, English names, English folders and
English files, generated from the name map of W3.1-bis.3.** The bench and its
loader change; the bridge and the library do not: their sources change only in
comments that cite a case or a bench path (22 files), and `same-il.ps1 -Mode
Identical` gives 0 differing lines for `Riscontro.Disegno.dll`,
`oce-solid-agent-mcp.exe` and `Riscontro.Catalogo.exe` against 1ccb18a, and
`Strumenti/catalogo.json` is rebuilt byte for byte the same. `VERSION.txt` and
the library version are therefore not raised (the practice of W3.1-bis.2, which
changed the bench loader the same way).

### The script of the window

- **`Controlli/lingua/window-1-cases.js`**, driven by `Controlli/lingua/name-map.tsv`
  (types case_key, free_case_key, case_family, case_name, folder, file,
  summary_key). In order: `git mv` of the folders and files of the bench this
  window owns (`Banco/casi`, `Banco/casi-in-attesa`, `Banco/compiti`,
  `Banco/resoconto`; `Banco/` itself and its root files are window 3, .6);
  the keys of every case, walked through the contexts of `Banco/case-keys.json`
  so that an argument name is never taken for a case key; the free notes to the
  `_` convention; the family, the case name and the twin; every case name and
  bench path inside a value (the component files of an assembly case are named
  after the case, `{LAVORO}\<case>_<component>`); `Banco/case-keys.json`; the
  keys of the bench summary; and the bench paths, case names and
  `--famiglia <folder>` values in every other tracked text file, except the
  CHANGELOG (history), the map and its extractor (Italian on purpose) and the
  language baseline. **Operations, arguments and selectors inside the cases
  stay as they are**: they are the MCP surface, window 2 (.5). A plan (a file
  whose root is a list) is the bridge's protocol: only its free notes and the
  names inside its values change.
- JSON is rewritten token by token: only the strings that change are replaced,
  so indentation, line ends, the trailing newline, the key order and the
  hand-made layout of each case stay byte for byte.
- `--reverse` runs the window backwards. `--reverse-proof <rev>` extracts HEAD
  into a temporary folder, runs `--reverse` there and compares the bench files
  of `<rev>` with what came back. `--check` writes nothing and says whether the
  tree is after the window (it is in the snapshot registry). `--todo <file>`
  rewrites the bench paths, case names and `--famiglia` values of a TODO
  outside its closed entries (the rule of `percorsi-del-todo.js`). `--step
  moves|content` splits the window in two commits, so that git sees every move
  as an exact rename. A second run changes nothing (also on its own sources: a
  line that must name both the old and the new path carries the marker
  `window-1: keep` and is never rewritten, and comments say "its Italian
  folder" instead of quoting the old path).
- `--load-proof <rev>` writes nothing: it reads the cases of `<rev>` and of the
  tree the way `Banco/CasiDichiarati.cs` reads them (root array = plan, root
  object = case, declared name = file name, keys walked through each tree's
  own `case-keys.json`) and compares them position by position, with its own
  rules rather than the rewriting code: same shape, a key changes only as the
  map says and only where the loader walks, the loader's child context of
  every key is the same, a string changes only by renamed names of this
  window. It also prints how the run order changes. Seen failing on a tampered
  case (an argument key renamed, a word added to a `why`).
- `--check` also looks for code (`.js`, `.cs`, `.ps1`) that names an Italian
  free note: step 5 rewrites paths and case names in the code, not free notes,
  and `Banco/raccogli_viste.js` kept reading `_verita` after the plans of
  `Banco/cases/views/` said `_truth` (it wrote "not declared" in silence;
  it reads `_truth` now). Seen failing on the old reader: 2 lines.
- `--todo` treats a `(com'era)` section (the original wording of an entry,
  kept for reference) as history like a closed entry, and so does
  `Controlli/percorsi-del-todo.js`: before, the F3 example of the TODO came out
  half old and half new.
- A case folder followed by a glob of its cases (`ponte/c2_*`) is a bench path
  too: the folder changes, the glob stays (2 cases, found by `--load-proof`).
- Two small tables in the script carry what the reverse run needs and the map
  does not hold, checked against the tree by every forward run:
  `ENGLISH_FROZEN_FREE` (the seven frozen free notes already written in English;
  `the_numbers` becomes `_the_numbers` like the Italian note with the same
  meaning) and `ENGLISH_CASE_KEYS` (`name` of an assembly configuration, which
  was English while the case's own name key becomes `name` too).

### What changed in the cases

- 304 case files and 127 plans (431 JSON files): 11161 case keys renamed by
  context, 327 free notes, 304 families, 262 declared case names, 136 twin
  references (115 of them to a renamed case), 806 case names inside values (the
  262 names and the 115 twins among them) and 7 bench paths, 3 component files
  named after their case.
- **The frozen list of free keys is empty.** The 184 frozen notes of
  30/9/2026 (182 keys) start with `_` and are admitted everywhere without being
  read; `FrozenFreeCeiling` is 0. `CaseKeysTests` keeps every check: the one that
  showed that the context decides now uses a documented key of the case root
  (`defect_caught`) where it used a frozen note.
- `Banco/report/last.json` (was `Banco/resoconto/ultimo.json`): its keys are
  the English summary keys of the map (`timestamp`, `library`, `catalog`,
  `runnable`, `in_catalog`, `batch`, `family`, `slow`, `duration_s`,
  `summary`, `expectations`, `passed`, `failed`, `skipped`, `errors`,
  `exit_code`, `red`, `bench_errors`, `green_cases`), written so by
  `Banco/banco.ps1` and read so by `ProveIlBanco` and `ProveILimiti`.

### The readers, renamed in the same commit

- The loader: `Banco/CasiDichiarati.cs`, `Banco/Attese.cs`, `Banco/AssiemeDiProva.cs`,
  `Banco/Regressione.cs` read the English keys, and their refusals name them;
  the case folders are `Banco/cases/<folder>` (the list `Famiglie`, hence the
  values of `--famiglia` and `-Famiglia`, are the English folder names), the
  regression folder is `regression`, the report folder `Banco/report`. No alias:
  a case with an Italian key is refused like any unknown key. C# identifiers are
  not renamed (window 3).
- The contracts: `CaseKeysTests`, `ProveAttese`, `ProveILimiti`, `ProveIlBanco`,
  `Programma` (the folder table of `Banco/LEGGIMI.md`, the folders the bench
  walks), `ObsoleteCallsContract` (the cases its OBSOLETE-MEASURED markers cite),
  `SelectionMarksContract`, `Zc2189WeldAssemblyContract`.
- The checks: `Controlli/conta-provate.js` (the keys of a case; a plan step keeps
  `operazione`), `Controlli/expected-keys-test.js` (the fake cases, whose wrong
  keys are now the names of before the window), `Controlli/percorsi-del-todo.js`
  (`Banco/cases` among the folders the TODO writes from, and the report of a run,
  `AAAAMMGG_hhmm.md`, a name made at run time and not a file),
  `Controlli/lingua/snapshot.js` (layout and case keys; the assertion kinds keep
  their names) and `Controlli/lingua/name-map-extract.js` (layout and case keys;
  a plan step is read with the protocol keys; the map is not touched and
  `--check` passes). `Controlli/rimandi-del-todo.js` and
  `Controlli/controlla-catalogo.ps1` name no bench path and are unchanged.
- **`snapshot.js compare --window 1`**: a snapshot taken before this window is
  read through its renames (case folders, case names, case keys, the test
  identities `File.cs:Method:ordinal@<case>`), with the script of the window
  doing the reading, so that the comparison of the next full bench with the
  snapshot of 1/10/2026 matches a renamed case with itself.
- The language gate: the `data-becomes-geometry` exception of the engraved word
  names `Banco/cases/text/` (it covered both paths for the commit of the moves).
- **The freshness check of the bench (`ProveIlBanco`) was fooled by the window.**
  It skipped every commit that touched `Banco/report/last.json` as "a delivery
  with its run": the move and the key rewrite of this window touch it and run
  nothing, and the rewrite also changes the loader, every case and 22 comments
  of the library; and the historical deliveries, which wrote the old path, were
  no longer recognised. A commit carries a run now only when the run's
  timestamp (`timestamp`, or `quando` on the old path) differs between the
  commit and its parent, read from whichever path exists. Seven new checks,
  made to fail on purpose with the real shapes of the two commits, and one on
  the real history (the move commit is not a delivery). The check now says
  `ultima modifica 2026-10-01T17:49:41 (commit)` for the operations, the
  rewrite, where it said 1ccb18a before.

### The language gate, and the one thing that grows

Three commits: the exception above widened (signed
`Language-Gate-Config-Change`), the moves alone with the script and this entry
(nothing grows: every move is an exact rename, compared with its old path), and
the rewrite. The rewrite lowers every count (json_keys 443 → 219, protocol
2170 → 1878, comments, strings and markdown down) but adds **two names to the
protocol set**, and carries the trailer `Language-Baseline-Raise` for them:
`inserting_is_not_mating_coperchio` and
`rebuilt_cover_keeps_the_constraints_but_broken_coperchio2`, the files the
bench writes for a component of an assembly case, named
`{LAVORO}\<case>_<component>` by the loader. The case name is English now; the
component name (`coperchio`) is a value of the case, translated with the other
values in W3.1-bis.8. The same commit narrows the exception and rewrites two
comment lines of `.github/workflows/prove.yml` (a path), and carries
`Language-Gate-Config-Change` too.

### Proved, and not proved here

- The inverse identity: `--reverse-proof 1ccb18a` on the new tree gives back
  every path and every byte of the cases, plans, case keys, summary and tasks of
  1ccb18a (the tree of the snapshot of 1/10/2026 for those files). On a copy of
  the whole tree the reverse run gives back every tracked file byte for byte.
- The cases load the same before and after: `window-1-cases.js --load-proof
  1ccb18a` (431 of 431 files paired, 1001 steps and 1836 expectations, 0 keys
  changed inside values, 0 differences), `--check`, and the contracts (785
  checks, the only reds the library version 0.148 against 0.149 and the bench
  freshness, which needs the bench run at the merge).
- 136 twin references in 121 cases, all resolving, before and after.
- **The bench has NOT run on this branch** (a full bench was running on the main
  checkout): the bench and `snapshot.js compare --window 1 --ledger
  Banco/report/tests.json` are to be run at the merge.
- The untracked outputs of the bench stay where they are on a checkout: move
  `Banco/resoconto/*` (but `ultimo.json`, which git moves) into `Banco/report/`
  and `Banco/casi/forma/` into `Banco/cases/forma/` once, or `.gitignore` will
  show them and `prova_forma.js` will not find its plans. The pieces of
  `Banco/viste/` carry the Italian shape names as their prefix: rename it to
  the English shape name (`1_drilled_plate`, `2_open_shell`, `3_l_bracket`,
  `4_drilled_flange`, `5_stepped_shaft`) or run `node Banco/raccogli_viste.js`
  again, or `prova_forma.js` finds no true piece.

### What to watch in the first bench after the merge

- **The run order changes for every case.** The loader sorts the files of all
  families together by file name (`Banco/CasiDichiarati.cs`), so renaming the
  files reshuffles the run: of the 291 cases of the loader's families, 5 keep
  their position and 62 keep the case before them (`--load-proof` prints it).
  `{LAVORO}` is emptied once per run, not per case, some cases share files,
  and SOLIDWORKS carries state from one case to the next. A case that changes
  verdict may do so for the order, not for the rename: rerun its folder alone
  (`.\Banco\banco.ps1 -Famiglia <folder>`) before blaming the map.
- Two cases keep the prefix `lim_` (`Banco/cases/export/lim_step_ap242_needs_the_mbd_licence.json`,
  `lim_step_ap242_wants_a_stp_path.json`): their names were already English and
  are not in the map, which this window does not rewrite. No code reads the
  prefix; two `case_name` rows belong to W3.1-bis.5.
- Open, outside this window: `Banco/tasks/*.json` still write the expectation
  keys of before (`cosa`, `uguale`, `tolleranza`, `perche`). No code reads them
  today; the runner of the tasks (entry Z-G9) will hand them to `Attese`, which
  reads `what`. Point 4 left open by W3.1-bis.3: to decide before Z-G9.

### The renames (old → new)

**Folders** (33):

| old | new |
|---|---|
| `Banco/casi/` | `Banco/cases/` |
| `Banco/casi-in-attesa/` | `Banco/pending-cases/` |
| `Banco/casi/asola/` | `Banco/cases/slot/` |
| `Banco/casi/asse/` | `Banco/cases/axis/` |
| `Banco/casi/assiemi/` | `Banco/cases/assemblies/` |
| `Banco/casi/coppie/` | `Banco/cases/pairs/` |
| `Banco/casi/corpi/` | `Banco/cases/bodies/` |
| `Banco/casi/esporta/` | `Banco/cases/export/` |
| `Banco/casi/estrusioni/` | `Banco/cases/extrusions/` |
| `Banco/casi/frase/` | `Banco/cases/sentence/` |
| `Banco/casi/lamiera/` | `Banco/cases/sheet_metal/` |
| `Banco/casi/lavorazioni/` | `Banco/cases/features/` |
| `Banco/casi/limiti/` | `Banco/cases/limits/` |
| `Banco/casi/misura/` | `Banco/cases/measurement/` |
| `Banco/casi/piano/` | `Banco/cases/plane/` |
| `Banco/casi/ponte/` | `Banco/cases/bridge/` |
| `Banco/casi/proprieta/` | `Banco/cases/document_properties/` |
| `Banco/casi/quote/` | `Banco/cases/dimensions/` |
| `Banco/casi/regressione/` | `Banco/cases/regression/` |
| `Banco/casi/ripeti/` | `Banco/cases/patterns/` |
| `Banco/casi/ritrovare/` | `Banco/cases/reanchor/` |
| `Banco/casi/rivoluzione/` | `Banco/cases/revolve/` |
| `Banco/casi/saldature/` | `Banco/cases/weldments/` |
| `Banco/casi/smusso/` | `Banco/cases/chamfer/` |
| `Banco/casi/specchia/` | `Banco/cases/mirror/` |
| `Banco/casi/tavola/` | `Banco/cases/drawing/` |
| `Banco/casi/telai/` | `Banco/cases/frames/` |
| `Banco/casi/testo/` | `Banco/cases/text/` |
| `Banco/casi/togliere/` | `Banco/cases/removal/` |
| `Banco/casi/verifica/` | `Banco/cases/check/` |
| `Banco/casi/viste/` | `Banco/cases/views/` |
| `Banco/compiti/` | `Banco/tasks/` |
| `Banco/resoconto/` | `Banco/report/` |

**Files whose name changed** (390 of the 453 moved; the others keep their name in the new folder), by folder:

- `Banco/casi-in-attesa/` → `Banco/pending-cases/` (1): `LEGGIMI.md` → `README.md`
- `Banco/casi/` → `Banco/cases/` (1): `E2-i-due-casi.md` → `E2-the-two-cases.md`
- `Banco/casi/asola/` → `Banco/cases/slot/` (18): `asola_cieca.json` → `blind_slot.json`, `asola_cieca_faccia_alto.json` → `blind_slot_top_face.json`, `asola_cieca_faccia_basso.json` → `blind_slot_bottom_face.json`, `asola_cieca_faccia_destra.json` → `blind_slot_right_face.json`, `asola_cieca_faccia_dietro.json` → `blind_slot_back_face.json`, `asola_cieca_faccia_sinistra.json` → `blind_slot_left_face.json`, `asola_fuori_centro.json` → `off_center_slot.json`, `asola_passante.json` → `through_slot.json`, `asola_ruotata_30.json` → `slot_rotated_30.json`, `asola_ruotata_90.json` → `slot_rotated_90.json`, `asola_su_piastra_stretta_0.json` → `slot_on_narrow_plate_0.json`, `asola_su_piastra_stretta_90.json` → `slot_on_narrow_plate_90.json`, `l_asola_ancorata_segue_il_bordo.json` → `anchored_slot_follows_the_border.json`, `l_asola_obliqua_ancorata_tiene_l_angolo.json` → `anchored_oblique_slot_keeps_its_angle.json`, `l_asola_si_corregge_con_la_quota.json` → `slot_is_corrected_with_the_dimension.json`, `lim_asola_cieca_senza_profondita.json` → `limit_blind_slot_without_depth.json`, `lim_asola_fuori_dal_pezzo.json` → `limit_slot_outside_the_part.json`, `lim_asola_tonda.json` → `limit_round_slot.json`
- `Banco/casi/asse/` → `Banco/cases/axis/` (2): `asse_dalla_parete_del_foro.json` → `axis_from_hole_wall.json`, `lim_asse_da_faccia_piana.json` → `limit_axis_from_a_planar_face.json`
- `Banco/casi/assiemi/` → `Banco/cases/assemblies/` (10): `due_basi_uguali_uno_step.json` → `two_equal_bases_one_step.json`, `fisso_non_e_accoppiato.json` → `fixed_is_not_mated.json`, `il_coperchio_dentro_la_base.json` → `cover_inside_base.json`, `il_coperchio_rifatto_tiene_i_vincoli_ma_rotti.json` → `rebuilt_cover_keeps_the_constraints_but_broken.json`, `inserire_non_e_accoppiare.json` → `inserting_is_not_mating.json`, `l_assieme_dice_da_che_file_dipende.json` → `assembly_says_which_file_it_depends_on.json`, `l_assieme_si_pesa_si_misura_si_manda.json` → `assembly_is_weighed_measured_and_sent.json`, `la_staffa_destra_e_la_sinistra.json` → `right_bracket_and_left_bracket.json`, `toccarsi_non_e_compenetrarsi.json` → `touching_is_not_interpenetrating.json`, `un_accoppiamento_non_basta.json` → `one_mate_is_not_enough.json`
- `Banco/casi/coppie/` → `Banco/cases/pairs/` (8): `asola_30x9.json` → `slot_30x9.json`, `foro_270_su_faccia_274.json` → `hole_270_on_face_274.json`, `foro_60_sul_bordo.json` → `hole_60_on_the_border.json`, `lim_asola_che_copre_tutto.json` → `limit_slot_that_covers_everything.json`, `lim_foro_che_copre_tutto.json` → `limit_hole_that_covers_everything.json`, `lim_foro_fuori_dal_materiale.json` → `limit_hole_outside_the_material.json`, `lim_tasca_che_copre_tutto.json` → `limit_pocket_that_covers_everything.json`, `tasca_400_cieca.json` → `blind_pocket_400.json`
- `Banco/casi/corpi/` → `Banco/cases/bodies/` (15): `combina_chiesto.json` → `requested_combine.json`, `combina_predefinito.json` → `default_combine.json`, `filetto_su_un_perno.json` → `thread_on_pin.json`, `interseca_due_cubi.json` → `intersect_two_cubes.json`, `lim_booleana_sconosciuta.json` → `limit_unknown_boolean.json`, `lim_combina_col_corpo_letto_prima_del_taglio.json` → `limit_combine_with_body_read_before_the_cut.json`, `lim_filetto_senza_denominazione.json` → `limit_thread_without_a_designation.json`, `lim_filetto_su_uno_spigolo_dritto.json` → `limit_thread_on_a_straight_edge.json`, `lim_raccordo_variabile_su_un_cerchio.json` → `limit_variable_fillet_on_a_circle.json`, `lim_raggio_troppo_grande_per_lo_spigolo.json` → `limit_radius_too_big_for_the_edge.json`, `lim_un_corpo_solo.json` → `limit_a_single_body.json`, `lim_unione_di_corpi_che_non_si_toccano.json` → `limit_union_of_bodies_that_do_not_touch.json`, `raccordo_che_cambia_raggio.json` → `fillet_that_changes_radius.json`, `sottrai_un_cubo_dall_altro.json` → `subtract_one_cube_from_the_other.json`, `unisci_due_cubi.json` → `merge_two_cubes.json`
- `Banco/casi/esporta/` → `Banco/cases/export/` (12): `due_corpi_esportati_a_forza.json` → `two_bodies_exported_by_force.json`, `iges_solidi.json` → `iges_solids.json`, `lim_due_corpi_non_si_esporta.json` → `limit_two_bodies_do_not_export.json`, `lim_estensione_sbagliata.json` → `limit_wrong_extension.json`, `lim_iges_con_estensione_step.json` → `limit_iges_with_a_step_extension.json`, `lim_iges_di_un_assieme.json` → `limit_iges_of_an_assembly.json`, `lim_protocollo_inventato.json` → `limit_made_up_protocol.json`, `lo_stl_non_esporta_la_faccia_selezionata.json` → `stl_does_not_export_the_selected_face.json`, `scatola_dodici_triangoli.json` → `box_twelve_triangles.json`, `scatola_in_ascii.json` → `box_in_ascii.json`, `step_ap203_e_lo_dice.json` → `step_ap203_says_so.json`, `step_ap214_riletto.json` → `step_ap214_reread.json`
- `Banco/casi/estrusioni/` → `Banco/cases/extrusions/` (8): `il_rialzo_rettangolare_ancorato_segue_la_mezzeria.json` → `anchored_rectangular_boss_follows_the_centerline.json`, `il_rialzo_tondo_ancorato_segue_il_bordo.json` → `anchored_round_boss_follows_the_border.json`, `il_tubo_ha_il_buco.json` → `pipe_has_the_hole.json`, `l_aletta_cresce_dalla_faccia.json` → `tab_grows_from_the_face.json`, `l_asola_estrusa.json` → `extruded_slot.json`, `la_ruota_da_diciassette_denti_si_fa.json` → `the_seventeen_tooth_gear_can_be_made.json`, `lim_rialzo_ancorato_ne_tondo_ne_rettangolare.json` → `limit_anchored_boss_neither_round_nor_rectangular.json`, `lim_ruota_da_sedici_denti.json` → `limit_sixteen_tooth_gear.json`
- `Banco/casi/frase/` → `Banco/cases/sentence/` (2): `albero_forato_non_dichiara_piu_il_profilo.json` → `drilled_shaft_no_longer_declares_profile.json`, `albero_pulito_dichiara_il_profilo.json` → `clean_shaft_declares_profile.json`
- `Banco/casi/lamiera/` → `Banco/cases/sheet_metal/` (12): `l_orlo_aperto_torna_col_conto.json` → `open_hem_checks_out.json`, `l_orlo_chiuso_si_schiaccia.json` → `closed_hem_flattens.json`, `la_L_al_contrario.json` → `the_L_reversed.json`, `la_L_si_sviluppa.json` → `the_L_flattens.json`, `la_flangia_a_spigolo_chiude_il_lato.json` → `edge_flange_closes_the_side.json`, `la_piega_da_schizzo_alza_meta_lamiera.json` → `sketched_bend_raises_half_the_sheet_metal.json`, `lim_flangia_spigolo_non_di_bordo.json` → `limit_edge_flange_not_on_a_border.json`, `lim_flangia_spigolo_senza_dritto.json` → `limit_edge_flange_without_a_straight.json`, `lim_flangia_tratto_corto.json` → `limit_flange_short_segment.json`, `lim_orlo_aperto_senza_gioco.json` → `limit_open_hem_without_gap.json`, `lim_piega_a_centottanta_gradi.json` → `limit_bend_at_180_deg.json`, `lim_sviluppo_senza_file.json` → `limit_flat_pattern_without_a_file.json`
- `Banco/casi/lavorazioni/` → `Banco/cases/features/` (34): `guscio_esterno.json` → `external_shell.json`, `guscio_interno.json` → `internal_shell.json`, `i_fori_guidati_ancorati_restano_a_venti.json` → `anchored_hole_wizard_holes_stay_at_twenty.json`, `il_cieco_diventa_passante.json` → `blind_becomes_through.json`, `il_foro_ancorato_segue_il_bordo.json` → `anchored_hole_follows_the_border.json`, `il_foro_guidato_segue_la_tabella.json` → `hole_wizard_follows_the_table.json`, `il_foro_sul_dietro_segue_l_angolo.json` → `hole_on_the_back_follows_the_corner.json`, `il_foro_sul_fianco_resta_a_meta_spessore.json` → `hole_on_the_side_stays_at_half_thickness.json`, `il_loft_non_e_un_cono.json` → `loft_is_not_a_cone.json`, `il_raccordo_arrotonda_i_quattro_angoli.json` → `fillet_rounds_the_four_corners.json`, `il_rialzo_cresce_e_non_e_un_taglio.json` → `boss_grows_and_is_not_a_cut.json`, `l_ordine_cambia_il_pezzo.json` → `order_changes_the_part.json`, `la_piastra_simmetrica_cresce_dalle_due_parti.json` → `symmetric_plate_grows_from_both_sides.json`, `la_quota_che_lascia_l_asola_dentro.json` → `dimension_that_keeps_the_slot_inside.json`, `la_quota_che_lascia_stare_il_raccordo.json` → `dimension_that_leaves_the_fillet_alone.json`, `la_tasca_centrata_segue_la_mezzeria.json` → `centered_pocket_follows_the_centerline.json`, `lim_ancora_che_non_dice_lo_stesso_punto.json` → `limit_anchor_that_does_not_report_the_same_point.json`, `lim_ancora_sul_bordo_del_raccordo.json` → `limit_anchor_on_the_fillet_border.json`, `lim_ancora_sul_bordo_dello_smusso.json` → `limit_anchor_on_the_chamfer_border.json`, `lim_fori_guidati_ancorati_a_meta.json` → `limit_anchored_hole_wizard_holes_halfway.json`, `lim_foro_guidato_fuori_dal_pezzo.json` → `limit_hole_wizard_outside_the_part.json`, `lim_nervatura_fuori_dal_suo_piano.json` → `limit_rib_outside_its_plane.json`, `lim_nervatura_sulla_piastra_senza_pareti.json` → `limit_rib_on_a_plate_without_walls.json`, `lim_quota_che_fa_uscire_l_asola.json` → `limit_dimension_that_pushes_the_slot_out.json`, `lim_quota_che_non_lascia_stare_il_raccordo.json` → `limit_dimension_that_will_not_leave_the_fillet_alone.json`, `lim_raccordo_faccia_fra_facce_che_si_danno_le_spalle.json` → `limit_fillet_face_between_back_to_back_faces.json`, `lim_raccordo_faccia_sulla_stessa_faccia.json` → `limit_fillet_face_on_the_same_face.json`, `lim_sformo_sulla_faccia_neutra.json` → `limit_draft_on_the_neutral_face.json`, `nervatura_nell_angolo_della_l.json` → `rib_in_the_corner_of_the_l.json`, `nervatura_nella_scatola_svuotata.json` → `rib_in_the_hollowed_box.json`, `raccordo_faccia_fra_due_facce_di_un_cubo.json` → `fillet_face_between_two_faces_of_a_cube.json`, `sformo_capovolto_cala.json` → `flipped_draft_drops.json`, `sformo_quattro_pareti.json` → `draft_four_walls.json`, `tubo_dritto_area_per_lunghezza.json` → `straight_pipe_area_per_length.json`
- `Banco/casi/limiti/` → `Banco/cases/limits/` (8): `lim_1_guscio_troppo_spesso.json` → `limit_1_shell_too_thick.json`, `lim_2_asola_piu_stretta_che_lunga.json` → `limit_2_slot_narrower_than_it_is_long.json`, `lim_3_raccordo_piu_grande_del_pezzo.json` → `limit_3_fillet_bigger_than_the_part.json`, `lim_4_foro_piu_largo_della_faccia.json` → `limit_4_hole_wider_than_the_face.json`, `lim_5_faccia_che_non_esiste.json` → `limit_5_face_that_does_not_exist.json`, `lim_6_quota_inesistente.json` → `limit_6_nonexistent_dimension.json`, `lim_7_taglio_fuori_dal_pezzo.json` → `limit_7_cut_outside_the_part.json`, `lim_8_quote_senza_viste.json` → `limit_8_dimensions_without_views.json`
- `Banco/casi/misura/` → `Banco/cases/measurement/` (10): `area_di_una_faccia_sola.json` → `area_of_a_single_face.json`, `distanza_fra_le_due_facce.json` → `distance_between_two_faces.json`, `due_facce_parallele_e_lo_dice.json` → `two_parallel_faces_and_it_says_so.json`, `ingombro_della_piastra.json` → `plate_bounding_box.json`, `ingombro_di_due_corpi_lontani.json` → `bounding_box_of_two_far_bodies.json`, `lim_distanza_di_una_faccia_da_se_stessa.json` → `limit_distance_of_a_face_from_itself.json`, `lim_ingombro_di_un_corpo_che_non_esiste.json` → `limit_bounding_box_of_a_body_that_does_not_exist.json`, `lo_spessore_che_resta_sotto_la_tasca.json` → `thickness_remaining_below_the_pocket.json`, `massa_di_un_cubo_di_acciaio.json` → `mass_of_a_steel_cube.json`, `massa_senza_materiale_non_si_sa.json` → `mass_without_material_is_unknown.json`
- `Banco/casi/piano/` → `Banco/cases/plane/` (6): `la_foto_sta_in_scala.json` → `the_picture_is_to_scale.json`, `lim_tre_punti_in_fila.json` → `limit_three_points_in_a_row.json`, `piano_frontale_meno_250.json` → `front_plane_minus_250.json`, `piano_obliquo_per_tre_punti.json` → `oblique_plane_through_three_points.json`, `piano_superiore_meno_100.json` → `top_plane_minus_100.json`, `piano_superiore_piu_100.json` → `top_plane_plus_100.json`
- `Banco/casi/ponte/` → `Banco/cases/bridge/` (101): `b8_due_passi.json` → `b8_two_steps.json`, `b8_due_tagli.json` → `b8_two_cuts.json`, `b8_due_tagli_uid.json` → `b8_two_cuts_uid.json`, `b8_secondo_storto.json` → `b8_second_crooked.json`, `c2_cerchio_senza_diametro.json` → `c2_circle_without_diameter.json`, `c2_cerchio_zero.json` → `c2_zero_circle.json`, `c2_tubo_dal_catalogo.json` → `c2_pipe_from_catalog.json`, `c4_ruota.json` → `c4_gear.json`, `c4_telaio_asse.json` → `c4_frame_axis.json`, `c4_telaio_dal_catalogo.json` → `c4_frame_from_catalog.json`, `c4_telaio_interasse.json` → `c4_frame_center_distance.json`, `c4_telaio_solo_quote.json` → `c4_frame_dimensions_only.json`, `c4_tubolare.json` → `c4_tubular.json`, `c4r_127denti.json` → `c4r_127_teeth.json`, `c4r_16denti.json` → `c4r_16_teeth.json`, `c4r_17denti.json` → `c4r_17_teeth.json`, `c4r_42denti.json` → `c4r_42_teeth.json`, `c4r_cava.json` → `c4r_keyway.json`, `c4r_foro.json` → `c4r_hole.json`, `c4r_foro_grande.json` → `c4r_big_hole.json`, `c4r_mezzo_dente.json` → `c4r_half_tooth.json`, `c4r_primario.json` → `c4r_primary.json`, `c4s_angolare.json` → `c4s_angular.json`, `c4s_asola.json` → `c4s_slot.json`, `c4s_asola_tonda.json` → `c4s_round_slot.json`, `c4s_esagono.json` → `c4s_hexagon.json`, `c4s_manca.json` → `c4s_missing.json`, `c4s_parete_meta.json` → `c4s_half_wall.json`, `c4s_spessore_lato.json` → `c4s_side_thickness.json`, `c4s_tondo.json` → `c4s_round.json`, `c4s_tubo.json` → `c4s_pipe.json`, `c5_intero.json` → `c5_whole.json`, `c5_senza_spessore.json` → `c5_without_thickness.json`, `c5_tipo_storto.json` → `c5_crooked_type.json`, `cambia_diametro.json` → `change_diameter.json`, `cilindro.json` → `cylinder.json`, `cp_1_blocco.json` → `cp_1_block.json`, `cp_2_foro.json` → `cp_2_hole.json`, `cp_3_ripeti.json` → `cp_3_repeat.json`, `cp_3b_ripeti.json` → `cp_3b_repeat.json`, `cp_4_specchia.json` → `cp_4_mirror.json`, `d3_apri.json` → `d3_empty.json`, `d3_barre.json` → `d3_bars.json`, `d3_completo.json` → `d3_complete.json`, `d3_frontale.json` → `d3_front.json`, `d3_modifica.json` → `d3_change.json`, `d3_pezzo_e_tavola.json` → `d3_part_and_drawing.json`, `d3_quote_tutte.json` → `d3_all_dimensions.json`, `d3_solo_tavola.json` → `d3_drawing_only.json`, `d3_solo_viste.json` → `d3_views_only.json`, `d3_tagli.json` → `d3_cuts.json`, `d3_tavola.json` → `d3_drawing.json`, `d3_vuoto.json` → `d3_open.json`, `d4_piano_orfano.json` → `d4_orphan_plane.json`, `d4_piano_orfano2.json` → `d4_orphan_plane_2.json`, `d5_cubo_e_raccordo.json` → `d5_cube_and_fillet.json`, `d5_cubo_e_taglio.json` → `d5_cube_and_cut.json`, `d5_cubo_sano.json` → `d5_healthy_cube.json`, `d5_innocente.json` → `d5_innocent.json`, `d5_raccordo_impossibile.json` → `d5_impossible_fillet.json`, `d5_stringi162.json` → `d5_tighten_162.json`, `d6_ripara.json` → `d6_repair.json`, `d6_rompi.json` → `d6_break.json`, `d6_sano.json` → `d6_healthy.json`, `d6_stringi.json` → `d6_tighten.json`, `d7_dappertutto.json` → `d7_everywhere.json`, `d7_in_parte.json` → `d7_in_part.json`, `d7_in_parte2.json` → `d7_in_part_2.json`, `d7_rompi.json` → `d7_break.json`, `e13_foro_sul_bordo.json` → `e13_hole_on_the_border.json`, `e15_guscio_da_riferimento.json` → `e15_shell_from_reference.json`, `e15_tutto_chiuso.json` → `e15_everything_closed.json`, `e1b_uid_inventato.json` → `e1b_made_up_uid.json`, `e1b_uid_vero.json` → `e1b_true_uid.json`, `e2_blocco.json` → `e2_block.json`, `e2_caso_a.json` → `e2_case_a.json`, `e2_caso_b.json` → `e2_case_b.json`, `e2_caso_b_giusto.json` → `e2_case_b_correct.json`, `e2_solo_quota.json` → `e2_dimension_only.json`, `e2_tasca.json` → `e2_pocket.json`, `e4_due_corpi.json` → `e4_two_bodies.json`, `e4_spezza_e_sposta.json` → `e4_split_and_move.json`, `e4_sposta_legittimo.json` → `e4_legitimate_move.json`, `e5_falso_rifiuto.json` → `e5_false_refusal.json`, `e5_intacca_angolo.json` → `e5_nicks_corner.json`, `e5_piastra_angolo.json` → `e5_plate_corner.json`, `e5_tondo_svasato.json` → `e5_round_countersunk.json`, `s_cava_a_filo.json` → `s_cavity_flush.json`, `s_composito_aperto.json` → `s_open_composite.json`, `s_foro_fuori.json` → `s_hole_outside.json`, `s_foro_staccato.json` → `s_detached_hole.json`, `s_papillon.json` → `s_bowtie.json`, `s_papillon2.json` → `s_bowtie_2.json`, `seq_a_tasca60.json` → `seq_a_pocket60.json`, `seq_b_tasca10_su_F2.json` → `seq_b_pocket10_on_F2.json`, `telaio_libreria.json` → `frame_library.json`, `telaio_su_copia.json` → `frame_on_copy.json`, `uid_buono.json` → `good_uid.json`, `uid_stantio.json` → `stale_uid.json`, `uid_stantio_2.json` → `stale_uid_2.json`, `z06_tavola.json` → `z06_drawing.json`
- `Banco/casi/proprieta/` → `Banco/cases/document_properties/` (11): `i_due_cassetti_non_si_vedono.json` → `the_two_lists_do_not_see_each_other.json`, `la_configurazione_nuova_si_attiva_da_sola.json` → `new_configuration_activates_itself.json`, `lim_configurazione_che_non_esiste.json` → `limit_configuration_that_does_not_exist.json`, `lim_configurazione_gia_esistente.json` → `limit_configuration_that_already_exists.json`, `lim_default_non_si_chiama_default.json` → `limit_default_is_not_named_default.json`, `lim_proprieta_senza_nome.json` → `limit_property_without_a_name.json`, `lim_proprieta_su_configurazione_che_non_esiste.json` → `limit_property_on_a_configuration_that_does_not_exist.json`, `materiale_chiesto.json` → `requested_material.json`, `materiale_predefinito.json` → `default_material.json`, `numero_disegno_sul_documento.json` → `drawing_number_on_the_document.json`, `si_torna_indietro_col_nome_intero.json` → `goes_back_with_the_full_name.json`
- `Banco/casi/quote/` → `Banco/cases/dimensions/` (6): `la_parete_ha_un_nome.json` → `wall_has_a_name.json`, `la_piastra_si_quota_da_sola.json` → `plate_dimensions_itself.json`, `la_tolleranza_sulla_quota_del_modello.json` → `tolerance_on_the_model_dimension.json`, `la_variabile_non_scavalca_lo_smusso.json` → `variable_does_not_override_the_chamfer.json`, `le_quote_arrivano_dal_modello.json` → `dimensions_come_from_the_model.json`, `lim_schema_con_due_riferimenti.json` → `limit_scheme_with_two_references.json`
- `Banco/casi/regressione/` → `Banco/cases/regression/` (19): `r01_quattro_fori_dal_bordo.json` → `r01_four_holes_from_the_border.json`, `r01b_quattro_fori_restano_dove_sono.json` → `r01b_four_holes_stay_where_they_are.json`, `r02_passanti_restano_passanti.json` → `r02_through_holes_stay_through.json`, `r03_tasca_centrata.json` → `r03_centered_pocket.json`, `r03b_tasca_resta_dov_era.json` → `r03b_pocket_stays_where_it_was.json`, `r04_quattro_raccordi_restano.json` → `r04_four_fillets_remain.json`, `r05_smusso_piu_grande_del_pezzo.json` → `r05_chamfer_bigger_than_the_part.json`, `r06_foro_assiale_nel_cilindro.json` → `r06_axial_hole_in_the_cylinder.json`, `r07_corona_di_fori_resta.json` → `r07_ring_of_holes_remains.json`, `r08_ripeti_circolare_istanze.json` → `r08_circular_pattern_instances.json`, `r10_fondo_del_guscio_si_sposta.json` → `r10_shell_bottom_moves.json`, `r11_fondo_piu_spesso.json` → `r11_thicker_bottom.json`, `r12_telaio_col_profilo_piu_sottile.json` → `r12_frame_with_a_thinner_profile.json`, `r14_foro_guidato_e_il_preforo.json` → `r14_hole_wizard_and_the_pilot_hole.json`, `r15_raccordo_sullo_spigolo_interno.json` → `r15_fillet_on_the_internal_edge.json`, `r16_asola_che_sborda.json` → `r16_slot_that_overflows.json`, `r17_specchia_resta_simmetrico.json` → `r17_mirror_stays_symmetric.json`, `r18_ripeti_lineare_passo_invariato.json` → `r18_linear_pattern_pitch_unchanged.json`, `r19_foro_a_venti_dal_bordo.json` → `r19_hole_twenty_from_the_border.json`
- `Banco/casi/ripeti/` → `Banco/cases/patterns/` (13): `griglia_3x2.json` → `grid_3x2.json`, `il_riempimento_fa_quattordici_fori.json` → `fill_makes_fourteen_holes.json`, `l_asse_riconosciuto.json` → `recognized_axis.json`, `lim_asse_spostato.json` → `limit_moved_axis.json`, `lim_copia_che_non_e_mai_esistita.json` → `limit_copy_that_never_existed.json`, `lim_direzione_da_spigolo_curvo.json` → `limit_direction_from_a_curved_edge.json`, `lim_griglia_non_si_conta_come_una_fila.json` → `limit_grid_does_not_count_as_a_row.json`, `lim_lo_zero_non_e_una_copia.json` → `limit_zero_is_not_a_copy.json`, `lim_riempimento_contorno_non_piano.json` → `limit_fill_contour_not_planar.json`, `lim_riempimento_direzione_fuori_dalla_faccia.json` → `limit_fill_direction_outside_the_face.json`, `lim_riempimento_senza_direzione.json` → `limit_fill_without_direction.json`, `salta_la_seconda_copia.json` → `skip_the_second_copy.json`, `tre_fori_passo_40.json` → `three_holes_pitch_40.json`
- `Banco/casi/ritrovare/` → `Banco/cases/reanchor/` (3): `il_raccordo_ritrova_lo_spigolo_rinumerato.json` → `fillet_finds_the_renumbered_edge_again.json`, `lim_raccordo_su_uno_spigolo_accorciato.json` → `limit_fillet_on_a_shortened_edge.json`, `lim_raccordo_su_uno_spigolo_doppio.json` → `limit_fillet_on_a_double_edge.json`
- `Banco/casi/rivoluzione/` → `Banco/cases/revolve/` (5): `albero_a_gradini.json` → `stepped_shaft.json`, `boccola_cava.json` → `hollow_bushing.json`, `cilindro_d40_lungo_50.json` → `cylinder_d40_long_50.json`, `lim_profilo_a_cavallo_dell_asse.json` → `limit_profile_straddling_the_axis.json`, `mezzo_cilindro_180.json` → `half_cylinder_180.json`
- `Banco/casi/saldature/` → `Banco/cases/weldments/` (7): `il_cordone_va_fra_facce_che_si_toccano.json` → `weld_bead_goes_between_touching_faces.json`, `il_fazzoletto_sta_nell_angolo.json` → `gusset_sits_in_the_corner.json`, `il_poligono_del_fazzoletto_non_e_degenere.json` → `gusset_polygon_is_not_degenerate.json`, `il_taglio_accorcia_la_barra.json` → `cut_shortens_the_bar.json`, `il_taglio_senza_gioco_non_eredita.json` → `cut_without_gap_does_not_inherit.json`, `il_tappo_chiude_il_tubo.json` → `cap_closes_the_pipe.json`, `la_distinta_conta_le_barre.json` → `cut_list_counts_the_bars.json`
- `Banco/casi/smusso/` → `Banco/cases/chamfer/` (4): `l_angolo_dello_smusso_si_cambia_in_gradi.json` → `chamfer_angle_changes_in_deg.json`, `lim_smusso_piu_grande_dello_spigolo.json` → `limit_chamfer_bigger_than_the_edge.json`, `smusso_3x6_cianfrino.json` → `chamfer_3x6_bevel.json`, `smusso_5_a_45.json` → `chamfer_5_at_45.json`
- `Banco/casi/specchia/` → `Banco/cases/mirror/` (2): `foro_specchiato_sul_destro.json` → `hole_mirrored_on_right.json`, `lim_piano_inventato.json` → `limit_made_up_plane.json`
- `Banco/casi/tavola/` → `Banco/cases/drawing/` (44): `due_fogli_e_lo_stesso_diedro.json` → `two_sheets_and_the_same_projection_angle.json`, `due_fori_uguali_uno_passante_uno_cieco.json` → `two_equal_holes_one_through_one_blind.json`, `i_contatori_ci_sono_anche_a_zero.json` → `counters_exist_even_at_zero.json`, `il_cartiglio_dice_cosa_vuole.json` → `title_block_says_what_it_wants.json`, `il_dettaglio_ingrandisce.json` → `detail_enlarges.json`, `il_foro_cieco_dice_quanto_e_profondo.json` → `blind_hole_says_how_deep_it_is.json`, `il_simbolo_di_saldatura_sul_giunto.json` → `weld_symbol_on_the_joint.json`, `l_albero_a_gradini_quotato_di_fianco.json` → `stepped_shaft_dimensioned_from_the_side.json`, `l_albero_smussato_non_quota_le_gambe.json` → `chamfered_shaft_does_not_dimension_the_legs.json`, `la_boccola_tornita_non_e_un_foro.json` → `turned_bushing_is_not_a_hole.json`, `la_borchia_non_e_un_foro.json` → `stud_is_not_a_hole.json`, `la_distinta_base_conta_i_componenti.json` → `bom_counts_the_components.json`, `la_distinta_di_taglio_dice_se_ha_confrontato.json` → `cut_list_says_whether_it_compared.json`, `la_distinta_di_taglio_in_tavola.json` → `cut_list_in_the_drawing.json`, `la_nota_finisce_sul_foglio.json` → `note_ends_up_on_the_sheet.json`, `la_sezione_taglia_il_pezzo.json` → `section_cuts_the_part.json`, `la_tabella_fori_dice_dove_sono.json` → `hole_table_says_where_they_are.json`, `la_tavola_e_un_secondo_documento.json` → `drawing_is_a_second_document.json`, `la_tavola_esce_in_pdf.json` → `drawing_exports_to_pdf.json`, `la_tavola_quota_fori_e_smusso.json` → `drawing_dimensions_holes_and_chamfer.json`, `la_tavola_si_quota.json` → `drawing_dimensions_itself.json`, `le_annotazioni_stanno_fuori_dal_pezzo.json` → `annotations_stay_outside_the_part.json`, `le_croci_di_centro_sui_fori.json` → `center_marks_on_the_holes.json`, `le_quote_arrivano_nel_pdf.json` → `dimensions_arrive_in_the_pdf.json`, `lim_datum_con_la_stessa_lettera.json` → `limit_datum_with_the_same_letter.json`, `lim_dettaglio_fuori_dalla_vista.json` → `limit_detail_outside_the_view.json`, `lim_due_sezioni_con_la_stessa_lettera.json` → `limit_two_sections_with_the_same_letter.json`, `lim_nota_fuori_dal_foglio.json` → `limit_note_outside_the_sheet.json`, `lim_planarita_con_un_riferimento.json` → `limit_flatness_with_one_reference.json`, `lim_rugosita_fuori_scala.json` → `limit_surface_finish_out_of_scale.json`, `lim_saldatura_su_un_corpo_solo.json` → `limit_weld_on_a_single_body.json`, `lim_sezione_fuori_dalla_vista.json` → `limit_section_outside_the_view.json`, `lim_spigolo_che_non_esiste.json` → `limit_edge_that_does_not_exist.json`, `lim_sviluppo_di_un_pezzo_pieno.json` → `limit_flat_pattern_of_a_solid_part.json`, `lim_tabella_fori_sulla_vista_di_fianco.json` → `limit_hole_table_on_the_side_view.json`, `lim_tolleranza_su_un_datum_che_non_c_e.json` → `limit_tolerance_on_a_datum_that_is_not_there.json`, `lo_sviluppo_in_tavola.json` → `flat_pattern_in_the_drawing.json`, `tav_1_salva_pezzo.json` → `dwg_1_save_part.json`, `tav_2_crea_disegno.json` → `dwg_2_create_drawing.json`, `tav_3_foglio.json` → `dwg_3_sheet.json`, `tav_4_viste.json` → `dwg_4_views.json`, `tav_5_isometrica.json` → `dwg_5_isometric.json`, `tav_6_quote.json` → `dwg_6_dimensions.json`, `tav_7_pdf.json` → `dwg_7_pdf.json`
- `Banco/casi/telai/` → `Banco/cases/frames/` (1): `i_profili_si_leggono_prima.json` → `profiles_are_read_first.json`
- `Banco/casi/testo/` → `Banco/cases/text/` (6): `l_area_va_col_quadrato_dell_altezza.json` → `area_scales_with_the_square_of_the_height.json`, `la_stessa_sigla_in_rilievo.json` → `the_same_code_embossed.json`, `lim_marcatura_senza_profondita.json` → `limit_marking_without_depth.json`, `lim_scritta_che_esce_dalla_faccia.json` → `limit_engraved_text_that_exits_the_face.json`, `lim_scritta_tutta_fuori_dal_pezzo.json` → `limit_engraved_text_entirely_outside_the_part.json`, `sigla_incisa_al_centro.json` → `code_engraved_in_the_center.json`
- `Banco/casi/togliere/` → `Banco/cases/removal/` (5): `elimina_il_primo_foro.json` → `delete_first_hole.json`, `lim_elimina_estrusione_di_base.json` → `limit_delete_base_extrusion.json`, `lim_nome_inventato.json` → `limit_made_up_name.json`, `lim_sopprimere_due_volte.json` → `limit_suppress_twice.json`, `sopprimi_il_terzo_foro.json` → `suppress_the_third_hole.json`
- `Banco/casi/verifica/` → `Banco/cases/check/` (6): `blocco_dritto_si_stampa.json` → `straight_block_prints.json`, `corpo_sano_si_esporta.json` → `healthy_body_exports.json`, `due_corpi_non_si_esportano.json` → `two_bodies_do_not_export.json`, `il_corpo_nascosto_che_pende_si_vede.json` → `hidden_hanging_body_is_visible.json`, `lim_corpo_che_non_esiste.json` → `limit_body_that_does_not_exist.json`, `pareti_a_sessanta_gradi_non_si_stampano.json` → `walls_at_sixty_degrees_do_not_print.json`
- `Banco/casi/viste/` → `Banco/cases/views/` (6): `1_piastra_forata.json` → `1_drilled_plate.json`, `2_guscio_aperto.json` → `2_open_shell.json`, `3_staffa_a_L.json` → `3_l_bracket.json`, `4_flangia_forata.json` → `4_drilled_flange.json`, `5_albero_a_gradini.json` → `5_stepped_shaft.json`, `COME_SI_CONTA.md` → `HOW_IT_COUNTS.md`
- `Banco/compiti/` → `Banco/tasks/` (3): `LEGGIMI.md` → `README.md`, `staffa_a_elle.json` → `l_bracket.json`, `telaio_da_un_metro.json` → `one_meter_frame.json`
- `Banco/resoconto/` → `Banco/report/` (1): `ultimo.json` → `last.json`

## Unreleased — Z-C21.14, `annulla` tells the truth (branch `zc21-14`) · bridge 0.125.0 · library 0.149.0

**What the live probe M19 and the probe `redo` (1/10/2026) found on the way of
`annulla`** (TODO entry Z-C21.14; RAG note 82zz_duodecies, the section on the live
probe of M19), with the adversarial review of the lot applied (findings F1-F16 below;
the version was raised once for the lot, as the review of Z-C21.11 did). The bench has
NOT run on this version yet: the three bench-freshness checks of Prove.Contratti are
red until it does. Prove.Contratti: 772 checks (759 on b5f7506, whose
`Prove/LEGGIMI.md` said 762), only those three red.

- **The state after a plan is read on the plan's document.** `Strumenti/Attrezzi.cs`
  read it on the window in front (`_sessione.Stato`), so after a plan that ended with
  another document in front (a `crea_disegno`, another session, the person) the memory
  of `annulla` held that document's numbers (line BRIDGE-R1 (X2) of the probe: a right
  undo refused; phase N: after `crea_disegno` no memory at all). The executor now hands
  on its plan document (`EsecutorePiano.Risultato.PlanDocument`, the reference it read
  once at the start) and the document it ended on (`FinalDocument`), and the bridge
  reads the plan's document there (`Sessione.StateOfDocument`, `Sessione.IsInFront`).
  The memory, `volume_dopo_mm3` and the error counts describe the plan's document;
  `stato_dopo` describes the document in front, and when the two differ the answer says
  so (`document_in_front`, `note_document_in_front`, with the way back).
- **The plan runs on the document the bridge checked (review F7).** The executor took
  the active document by itself (`EseguiAsync(null)`), so a window brought to the front
  between the bridge's reading and the plan got the plan. `Sessione.EseguiPiano` now
  hands it the identity it checked, and the executor stops before writing when the
  active document is another one; the window it found is then not taken for the plan's
  document, nor adopted.
- **The window in front is adopted only when the plan ended on it (review F5).** After
  a plan during which another window came to the front from outside (scenario X2 of the
  probe), its identity and signatures were adopted, so a retry of the refused step
  written for the plan's part passed the «document changed» check and wrote into the
  other document. Now they are cleared, the note says to call `guarda_il_pezzo` or to
  bring the part back, and the next `disegna` asks for `guarda_il_pezzo` first. After
  `crea_disegno` the drawing is the document the plan ended on, and is adopted as
  before.
- **The last plan is the memory, and only a plan that wrote something replaces it
  (review F1, F3, F12).** The executor reads what FinishRecordingUndoObject answered for
  the plan's undo entry (`UndoEntryOnPlanDocument`, answered as `undo_entry_recorded`).
  The rules are one pure function (`Attrezzi.MemoryAfterPlan`): a plan that built, or a
  `disegna` whose own entry closed with something in it (a reorder, a rename, a
  property), becomes the memory, so `annulla` withdraws the last plan as its
  description says (the first version of this lot refused to); a plan that wrote
  nothing (never reached the executor, stopped before its recording, or closed an empty
  one with nothing visibly changed) keeps the memory, so a plan, a measure and an undo,
  or a plan, a drawing with its views, the part brought back and an undo, are no longer
  refused; a
  `misura` that recorded something, or another bridge process holding the turn on the
  memory's document (review F4), marks the memory covered; an executor that threw
  (review F9), an unknown answer or a plan on another document drops it, and the reason
  is rewritten after every plan that wrote. The probe measured only the answer of
  FinishRecordingUndoObject (false for an empty recording, true for a full one); whether
  an empty recording leaves an entry on the stack is undecided (its control H3 failed,
  review F2), and the bench case `undo case c` decides it: if it is red, the rule that
  keeps the memory after an empty recording goes.
- **What `annulla` compares, and when it refuses before pressing (review F1, F11, F14,
  F16).** The memory holds the plan's document by its COM reference (a save renames it;
  a document closed and opened again has the same name and lost the undo stack) and a
  snapshot of it before and after the plan: volume, feature count, feature names with
  their IDs, in tree order. `annulla` refuses BEFORE pressing (`refused_before_pressing`)
  when the memory is covered, or when the part no longer reads as the plan left it,
  names and order included (a reorder by another bridge process was pressed away
  before); and it says «withdrawn» only if the part then reads as before the plan. A
  plan that changed nothing compared is withdrawn with `checked_by_reading` false and a
  note. Beyond the feature cap of the state the answers say that names and order were
  compared only on the listed features.
- **No blind redo, and no claim of a part put back without looking.** SOLIDWORKS never
  redoes an undo entry recorded through the API (probe `redo`: EditRedo2(1) and (2),
  RunCommand(swCommands_Redo), after rebuilds, the executor's own entry; a plain
  unrecorded extrusion is redone at once), and the answer after a wrong press said the
  part was put back whenever EditRedo2 did not throw. Now a press that did not bring the
  part back is compared with the part before pressing, one redo is tried only if the
  press took away something visible, and «restored» is said only from a new reading.
  The refusal of a covered memory says what a press would do according to what covers
  it (a recorded entry is lost to a press; an unrecorded action put back by a redo is
  not, review F10), and every refusal gives a remedy for the model (take the features
  away with a new plan: `elimina_lavorazione`, `sopprimi_lavorazione`) before the one
  for the person at the screen (review F1, F12). The comments that stated the false
  premise now say what was measured.
- **The resent-plan guard reads one document (review F6):** the plan's identity and the
  summary of the plan's document after it, so after [..., `crea_disegno`] a plan resent
  on the part is recognised instead of building a second time.
- **Tool descriptions (review F15):** `annulla` and `disegna` say which plan is the
  last one, how to come back to the part, `document_in_front`, `undo_entry_recorded`
  and the refusal before pressing.
- **Bench, the group that talks to the bridge** (`Banco/PortaDelCliente.cs`,
  `UndoHonesty`), 19 tests in four cases, each read back through COM and red on 0.124.1
  by the code path (written in the method's summary): (a) plan 2 reorders the holes of
  plan 1, and `annulla` withdraws the reorder and leaves plan 1; (b) a plan on a saved
  part ends with `crea_disegno`, the part comes back with `documento`/`attiva`, and
  `annulla` withdraws the plan; (c) `annulla` after a `misura` still withdraws the plan
  (it decides review F3); (d) a second bridge process reorders the holes of the first
  one's plan, and the first one's `annulla` refuses before pressing, the reorder still
  there (review F14). Case b saves with SaveAs3; the one obsolete SaveAs of the file is
  the document cycle's, as before (review F13).
- **Prove.Contratti:** `Zc2114UndoHonestyContract`, 13 checks (the pure decisions by
  reflection, the source rules of the plan, of `annulla`, of the executor and of the
  session each seen red on changed copies); `ProveIlDocumento` follows the new signature
  of `FinishUndoRecording`; the frozen undecided count of `Strumenti/Attrezzi.cs|Value`
  goes down from 16 to 14.
- **Review findings not applied as asked:** the per-document counter shared between
  bridge processes (second half of F4) is not built: the snapshot compared before
  pressing now sees a reorder or a rename by any writer, and what it cannot see (a
  property, a configuration) needs its own design (where the counter lives, who resets
  it, a crash in between).

## Unreleased — W3.1-bis.3, the name map (branch w31bis-3)

Tooling, data and documents only: the bridge and the library are unchanged, so
`VERSION.txt` is not raised (the practice of W3.1-bis.2).

### The one map the rename windows are generated from

- **`Controlli/lingua/name-map-extract.js`** reads the tree and writes
  **`Controlli/lingua/name-map.tsv`**: one row per Italian name per type
  (columns `type`, `it`, `en`, `count`, `where`, `note`), in 19 types: tool,
  sub_action, operation, catalogue_family, case_family, argument, enum_value
  (argument=value, per enum block), catalogue_key, response_field,
  response_value, case_key, free_case_key, summary_key, selector, folder, file,
  case_name, project, identifier. The same word in two roles is two rows.
- **Italian is decided by the language gate itself**: its lexicon, its
  identifier rule and its exceptions, plus its prose rule on a name of two
  words or more read as words (a phrase glued with underscores is still a
  phrase). So every name the gate counts has a row, and a name generated by an
  Italian SOLIDWORKS never gets one.
- Sources: the catalogue and its schemas; the tool list and the tool schemas of
  `Strumenti/` (Roslyn; an enum built from a member is resolved through it);
  the cases of `Banco/casi/` and `Banco/casi-in-attesa/`, plans included,
  walked through the contexts of `Banco/case-keys.json`; the summary of the
  bench; the keys and values the library and the bridge write into a
  `Dictionary<string, object>`, found with a Roslyn **semantic model** (schema
  dictionaries left out); C# declarations through Roslyn, JavaScript through
  acorn, PowerShell through its own parser; `git ls-files` for folders and
  files.
- **`--check`**: (a) every Italian name found has a row with an English name;
  (b) no two rows of a type share an English name, unless each says "merged";
  (c) no English name, or part of one, is a C# keyword (contextual ones
  included) or a JSON Schema keyword; (d) ASCII and the shape of the Italian
  name (snake, Pascal or camel case, the extension, a leading underscore), unit
  suffixes kept and degrees as `_deg`, a free note of a case with a leading
  underscore; (e) a namespace extends the English name of its parent; (f) the
  gate does not read the English name as Italian. **It passes on the map as
  committed: 6,062 rows, 0 failures** (743 warnings: an English name the same
  role already uses somewhere in the tree, or a name kept as it is).
- **`--self-test`**: 27 cases, every check seen red on its fake row and a good
  map seen green; a mutation of any check turns it red. Run by the pre-push
  hook, and registered in `snapshot-scripts.json` with `--counts` and
  `--check`.
- **`--coverage`** lists the names of the gate's set categories that no row
  covers: paths 0 of 640, JSON keys 9 of 443 (the task files of
  `Banco/compiti/`, rewritten for D9, and three keys of a fake client
  configuration), identifiers 341 of 4,799 and protocol values 389 of 2,162
  (names inside prose and documents, words of messages, environment variable
  names, SOLIDWORKS API names the lexicon misreads). Not mapped yet, because
  none of the 19 types fits them: the keys of `Banco/compiti/*.json`, three
  keys of `Controlli/config-con-altri-server.json`, the environment
  variable names, the command-line options of the bench, the probes and the
  installer (about thirty literals such as the group, keep, mutate and
  uninstall options, read by `Banco/banco.ps1`, the hooks and the owner's
  habits), and the file names of the file-based plan session of
  `Banco/Sessione.cs` (a step file and a state file per step, written by a
  planner on the other side). A type for each, or a decision to keep them,
  is needed before W3.1-bis.6 and .10 touch those scripts.

### The English column, filled and checked

Four translators filled the English column on separate files, so that no two
wrote the same one; the integrator merged them into
`Controlli/lingua/name-map.tsv` (every row covered exactly once, `count` and
`where` identical to the skeleton), deleted the partial files and ran
`--check` until it passed. Rows per type, with the rows the integration
changed after the merge:

| type | rows | changed by the integration |
|---|---|---|
| tool | 7 | 0 |
| sub_action | 5 | 0 |
| operation | 99 | 0 |
| catalogue_family | 12 | 0 |
| case_family | 26 | 0 |
| argument | 205 | 0 |
| enum_value | 128 | 0 |
| catalogue_key | 9 | 0 |
| response_field | 690 | 22 |
| response_value | 49 | 4 |
| case_key | 38 | 1 |
| free_case_key | 179 | 175 |
| summary_key | 19 | 0 |
| selector | 20 | 0 |
| folder | 80 | 13 |
| file | 183 | 100 |
| case_name | 383 | 111 |
| project | 30 | 5 |
| identifier | 3,900 | 1,325 |
| **total** | **6,062** | **1,756** |

One row was added by hand: the probe folder `Controlli/ApiQuote/`, which the
gate reads as English, renamed with its project.

**The rules applied** (TODO W3.1-bis, section 2, and D3):

- The glossary of section 2 first, and the proposal for the operations: every
  tool, sub-action, operation, catalogue family, catalogue key and selector
  follows it, except where a check forbids it (below).
- snake_case stays snake_case, `_mm` stays, the degree suffix becomes `_deg`; the
  plural follows English; a free note of a case gets the leading underscore
  of W3.1-bis.4 (all 179).
- Different enumerations stay different: the catalogue families and the case
  families are two vocabularies (checks / check, exports / export are not
  merged). The case folders under `Banco/casi/` take the English name of their
  case family, because the loader joins the folder name to the family.
- A word with two senses is decided by context, never by a replace-all:
  `piano` is plane in the CAD code and plan in the plan executor and the bench
  orchestration (the plan executor, the footprint of a plan, the last plan, the
  plan in memory); `passo` is the step of a plan, but the pitch of a pattern and
  of a gear; `albero` is the feature tree (or a parsed JSON tree) in the locals
  and the probes that count features, and a shaft in the cases and the gear
  code (a stepped shaft, a keyway for a shaft); `distinta` is the cut list where the
  code reads a weldment cut list; `stadio` is the stadium shape of a slot, not a
  stage. Where one identifier carries both senses in different files (the
  locals of plane and plan), the row holds one sense and its note names the
  files of the other, for W3.1-bis.6.
- Method names take the English base verb (Widen, Analyze, Compare, not the
  third person); noun phrases take the English order (a body name, not a name
  of body); the helpers ending in "of" become `NameOf`, `KeyOf`, `BodiesOf`;
  the test classes become `<Subject>Tests`, like the contracts already written
  in English; a file of a C# type takes the English name of its type.
- A collision inside a type is decided, never renamed silently: inflections and
  synonyms (gender, number, two words for one thing) share an English name and
  say "merged", but only after a scope analysis showed that no two of them
  meet (C# member bodies, containing types and namespaces through Roslyn;
  JavaScript and PowerShell per file); the 37 pairs that do meet, and words of
  different meaning (open the verb and open the adjective), got distinct names.
- An English name that the same scope already declares was changed too (a
  local `name` beside the old one, the path module of Node, `System.IO.Path`
  and `System.Diagnostics.Process` hidden by a property, two `SameFile` in one
  partial class): 19 rows.
- A keyword is replaced by the nearest plain word, the same in every type where
  the root occurs: new becomes create (sub-action) or new_part (case key),
  type becomes kind (selector, response field) or variant (argument), equals
  becomes equal_to, contains becomes includes (and the message assertion
  message_includes, the same verb), description becomes details (catalogue
  key, argument and response field alike), dependencies becomes
  file_dependencies, fixed becomes grounded, where becomes placement or
  location, value becomes value_text; lowercase identifiers get the same
  treatment (doubled, ext, timestamp, thisOne, theField, testCase, fitClass).

**The doubtful rows, and how they were decided:**

- The test-chosen custom properties of `Banco/casi/proprieta/` are data the
  cases write and read back, so they are renamed in the cases (Hand,
  DrawingNumber, Revision). One property is **kept as it is, an open
  question for the owner**: the description the Italian drawing template of the
  shop shows in its title block (`Banco/casi/tavola/il_cartiglio_dice_cosa_vuole.json`
  writes it and reads it on the sheet). Renaming it would cut the case off the
  template; the way out is either an exception of class `solidworks-localized`
  or a template with English property names. The same case asserts two more
  property names of that title block in a textual contains: they stay with
  it, whatever the owner decides.
- `Segna` becomes `Check`, as the glossary has it, but two English contracts
  (`Prove/StateChamferContract.cs:69`,
  `Prove/WeldmentConfigurationZc212terContract.cs:72`) declare
  `Check(Assembly)`, which would hide the imported one: W3.1-bis.6 renames
  those two methods first. The note of the row says so.
- The edge cases of the bench keep a prefix of their own, limit, where the
  translator had used edge (a word the map already gives to the geometric
  edge); the sense of each name is otherwise kept.
- The same key holding two senses (the end point of a segment and, at one site
  of the catalogue, the ISO 2553 weld throat) gets the sense of most sites; the
  note names the other site for W3.1-bis.5.
- The gear probe and the gear cases say gear, not wheel; the raised boss of
  the anchoring cases says boss, not raise; a hole wizard is never a pilot
  hole.

**The adversarial review** read every row against the code that writes or
reads the name, and changed 146 rows (138 English names, 8 notes only), each
one signed `reviewer:` in its note. `--check` passes again on 6,062 rows.

- **Senses a model would have misread.** The profile argument of a
  structural member is the path of the `.sldlfp` file, so it is
  `profile_path`, as the profile listing already answers, not `path_profile`
  (the glossary note read it as the profile of a sweep path, and no sweep
  takes it). The export answers say `file_format`, not `paper_size`: the five
  cases that check the key read IGES, STEP, ASCII or binary; the paper-size
  and the sheet-format sites of the same key are named in the note for
  W3.1-bis.5. The frame argument that says how its sizes are measured is
  `measured_as`, not `measurements`, the block of every answer.
- **The cut list is not a BOM.** The bodies counted in a weldment cut list are
  `bodies_in_cut_list` and `bodies_in_part_cut_list`, and the file of the
  drawing cut-list table is `DrawingCutListOperations.cs`.
- **One English word per Italian word on the whole surface.** The answer echo
  of an argument takes the argument's name: `gap_mm` (the weld gap and the
  hem gap; the cases follow), `offset_used_mm`, `library`, `end_condition`,
  `tool_bodies`, `trim_boundary_bodies`, `instance_count1`, `copy_count`,
  grounded for every fixed component, and `details` for the description of
  the catalogue, of describe_operation and of a configuration.
- **Words of the trade.** `projection_angle` for first and third angle (the
  key, the case and the free note that named it dihedral), `pitch_diameter_mm`,
  `outside_diameter_mm` and `root_diameter_mm` of a gear, `k_factor`,
  `center_of_mass_mm`, `lower_deviation_mm` and `upper_deviation_mm` of an
  ISO 286 tolerance, `driven_dimensions` of an equation, `read_only`,
  `throat` of a weld, `document_title` instead of heading, `in_front` for the
  document in front, `two_lists` for the two property lists the Italian
  called drawers.
- **Identifiers whose meaning had changed.** The helpers that draw sketch
  entities say Draw, not Build (the very case the TODO warns about, in the
  opposite direction); an upper limit is Cap, not Roof; a reply timeout is
  TimeoutMs, not an expectation; an MD5 or SHA-256 is a fingerprint, not a
  footprint; the pitch of a pattern and of gear teeth is pitch, not a plan
  step; the height of a build plate is a level, not a dimension; the IT
  grades of ISO 286 are not degrees; a component along an axis is along, not
  longer; the kind of a JSON node is NodeKind, not That.
- **Other senses at single sites**, named in the notes for the window that
  renames them: the section offset under the end-point key, the elevation and
  the share under the dimension local, the dimension spacing under the step
  local, the key path under the file-path local, the inner key of the frame
  answer under measurements.

### A contract that runs the check, seen red

- `Prove/SafetyNetSelfTests.cs` runs `name-map-extract.js --self-test`, then
  `--check` on the real map, then `--check` on a temporary copy of the map
  without one row, with a collision and with a C# keyword, which must exit 1
  naming (a), (b) and (c). Seen red on 1/10/2026 with a row taken out of the
  real map and the keyword check of the extractor blinded: all three red, the
  copy red on (a) and (b) only. `Prove/LEGGIMI.md`: a row for it, and 764
  checks (a full run, with the TODO).

### The 15 tasks of the autonomous run (D9)

Written in Italian as the owner would ask them (in the internal notes,
ignored by git), then in English in `Banco/compiti/`, same numbering; each one
gives the request in shop language (no operation names), why it is a
meaningful test, what it deliberately leaves out, a table of checkable facts
with the arithmetic and the reason for each tolerance, and what the
measurement cannot see:

1. an L-bracket with two holes; 2. a round flange with a bolt circle;
3. a slotted plate with four chamfers; 4. a sheet-metal C-channel;
5. a hemmed sheet-metal cover; 6. the one-metre welded frame (the existing
task, ported); 7. a tube stand weldment with gussets and caps; 8. a box and
lid assembly; 9. a mirrored bracket pair; 10. the drawing of the bracket pair
with a BOM and balloons; 11. the drawing of the tube stand with its cut list;
12. the weight and bounding box of the flange; 13. the interference check of
the box and lid; 14. an STL export for 3D printing; 15. a STEP export of the
tube stand, reopened and compared.

### Language gate

- `exceptions.json`: one exception of class `transition` for the second column
  of `Controlli/lingua/name-map.tsv` (a pattern on that column, never the
  file): the gate counts those names in `transition`, which must be 0 at the
  tag, so the map leaves the tree before `v0.1.0`. Committed on its own, with
  `Language-Gate-Config-Change:`. The same exception for a translator's
  partial file was removed with the file, with the same trailer.
- `baseline.json`: `transition` 0 -> 2,165 Italian words on the commit that
  adds the map (`Language-Baseline-Raise:`), all in its second column;
  3,907 while a partial file was tracked, back to 2,165 with the merge.
  `markdown` +1 in `Controlli/lingua/name-map.tsv`, with
  `Language-Baseline-Raise:`: the English column of the one property kept as
  it is (above). Notes are written in English, without Italian words outside
  the names the tree already has.

## Unreleased — W3.1-bis.2 item 11, the snapshot tool (branch `w31bis-2-foto`)

Tooling only: the bridge and the library are unchanged, so `VERSION.txt` is
not raised (the practice of W3.1-bis.2 on its branch).

### The snapshot every later step of W3.1-bis is compared with

- **`Controlli/lingua/snapshot.js`** (`take` / `compare` / `--self-test`).
  `take` writes a snapshot directory (default `note-interne/lingua/snapshots/<date>`,
  ignored by git: the tool and its format are versioned, the snapshot is not)
  with six sections:
  - the **catalogue**, copied as it is, with its raw and normalized sha256 and
    one hash per operation and per family;
  - the **tools** of the built bridge (`initialize` + `tools/list`), over stdio
    as a client asks them, or **by reflection when SOLIDWORKS is running**:
    started as a program the bridge attaches to SOLIDWORKS at once, so then its
    assembly is loaded and the protocol dispatcher (found by its shape, never
    the entry point) is called directly. The source used is recorded;
  - every **`Fallito(...)` call** of the `<Compile>` items of the library and
    the bridge, named `file:method:ordinal`, and every string unit
    (`file:member:sN`, `+` chains joined, run-time values as holes), read
    through Roslyn: a comment is never a literal;
  - every **textual assertion** of the cases (`il_messaggio_contiene`,
    `contiene`, `non_contiene`, textual `uguale`, found through the contexts of
    `Banco/case-keys.json`) with the case, path, step and operation, all the
    string units that contain it (ignoring case, as the bench does), the
    message templates that satisfy it across a run-time value, and the list of
    assertions with no candidate;
  - the **tests** of TWO reference ledgers: identity, label, outcome, and per
    case the outcome and the expectations attempted and checked, with the
    **unstable** cases and tests (a different outcome or count between the
    two runs);
  - the exit code and the counts of every **script** of `Controlli/` and
    `.github/passi/` that runs without SOLIDWORKS.
- `compare` reports every difference per category (tests by
  `file:method:ordinal`, never by the label text), prints the unstable cases
  apart, and exits 1 on any difference outside the unstable list, 3 when a
  section was not compared, 2 on a missing input. A snapshot is written as
  `<out>.partial-<pid>` and renamed at the end. Children never inherit
  `GIT_INDEX_FILE`/`GIT_DIR`: `expected-keys-test.js` builds a throw-away
  repository and wrote its index over the one such a variable named.
- **`Controlli/lingua/snapshot-scripts.json`** names all 82 scripts and
  programs of those folders: 36 run, 46 skipped, each with the reason (32
  SOLIDWORKS probes, the installer test, the release and history-rewriting
  tools, the hooks, anything that edits tracked files or pushes). A script it
  does not name stops the tool. Entries carry `needs`, `varies_with` and
  `volatile_lines` with their reasons (`rinomina-prodotto.js` walks the file
  system and counts ignored files: three masked lines).
- **`Controlli/lingua/snapshot.md`**: usage, exit codes, the format of every
  file of a snapshot directory, the template rule, the registry, what counts
  as a difference, and what the tool does not do (renamed identities through
  the name map of W3.1-bis.3 are not followed yet).
- `token-stream.js` can be required as a module (it restarts itself with
  `--expose-internals` only when it is the main module) and exports
  `compileRoslynProgram` and `GateError`; its own helper's cache hash is
  unchanged.

### Seen red

- `--self-test`: 18 of 18 on a fake tree and fake ledgers. A changed refusal
  literal, the literal or the template behind an assertion gone, a changed
  outcome, a count dropped to 0, a catalogue change and a tool change are red;
  the same tree, a translated label and a move of the unstable case alone are
  green; one ledger, a missing ledger, a missing snapshot file and a script the
  registry does not name are refused (exit 2). Eight mutations of a copy of the
  tool were each seen red.
- It runs with the suite: `SafetyNetSelfTests` has a fifth self-test
  (`N of M checks as expected`, at least 28 since the review below). Seen red
  in the suite by expecting the wrong exit code in one check: 17 of 18, NO.
  **Prove.Contratti: 762 checks** (761 + this one; every self-test is one
  check whatever its result).

### The adversarial review: four ways to make `compare` say "same" when it was not

Tried on a copy of the real tree against a snapshot of it, with the saved
ledger. Renaming a catalogue operation, translating a refusal, flipping a test
outcome under the same label, changing the expectations of a case, a missing
or truncated ledger were already caught. These were not, and are fixed (the
snapshot format becomes 2, and a snapshot of format 1 is refused):

- **A literal inside a refusal that the text of the call shows as a hole.**
  `text` renders `cond ? "a" : "b"`, `string.Format("...", x)` or a helper
  call as `{*}`, so translating the branch "the outline is not complete" of
  the slot refusal compared as 0 differences. 185 of the 1071 calls hold such
  a literal, and 235 have no constant text at all. Every call now carries
  `inner`, the text of each string unit inside its arguments, and a change
  there is a difference.
- **The literal behind an assertion gone while another one still contains
  the words.** The mate type a case asserts with `uguale` translated in
  `StatoAssieme.cs`: the same word is in the catalogue descriptions, so the
  assertion stayed "satisfied" and the compare said 0 differences, while the
  bench would go red. Now every member (`file:member`) whose units satisfied
  an assertion in the snapshot must still satisfy it; a unit renumbered or
  reworded in the same member that still holds the words is not a loss.
- **A ledger that is not of a full run, or with unnamed tests.** A `partial`
  ledger that happened to miss nothing compared as 0 differences, and so did
  one with `identities_unresolved` above 0. Both are now a difference.
- **A `varies_with` added to the registry after the snapshot** excused every
  other count of that script: an input absent from the snapshot compared as
  "changed". Only an input recorded then and now, with another value, excuses
  counts now.
- Smaller: `--tools-timeout` (the stdio read was fixed at 90 s, now also
  proved: a bridge that never answers is stopped and the tool exits 2, a
  bridge that ends at once exits 2, a broken pipe no longer ends the tool with
  exit 1, "a difference"); a request of the bridge to the client is not taken
  for an answer; a `TODO_MD` naming a missing file stops the tool instead of
  falling back to the default TODO.
- The self-test grows from 18 to 28 checks, one per hole above plus the same
  ledger twice (no unstable case, "not measured"), the excuse of a recorded
  input that moved, and no folder left by a take that stopped. Each new check
  was seen red on its own mutation of a copy of the tool (six mutations, each
  27 of 28). With two different ledgers the unstable case and test are listed
  apart, with the tests of that case; with one ledger twice the list is empty
  and the snapshot says it was not measured.
- Contracts on this branch were run with `PROVE_SENZA_INTEROP=1` only, because
  a full bench was running on SOLIDWORKS and the groups that start the bridge
  attach to it: 593 checks, 18 not runnable here, 9 red, none of them new: the
  bench-freshness check of `Banco` (the bench report predates the last commit
  on `Banco`), and 8 library checks (N59 x2, N26, N16, the six moves, the rule
  per direction, «elsewhere», the redundant decision) whose type initializer
  needs the SOLIDWORKS interop that the simulated mode hides.

### The first snapshot is a proof run only

Taken on the real tree with the one saved ledger (the full bench of library
0.148.0) given twice, so its unstable list is empty by construction and it says so:
99 operations, 9 tools (by reflection, SOLIDWORKS was running), 1071
`Fallito` calls in 101 files, 11841 string units, 693 textual assertions in
303 cases (9 satisfied only by a template, 77 with no satisfying literal,
mostly SOLIDWORKS data such as materials and component names), 2161 tests in
284 cases, 36 scripts run. A compare of the same tree against it: 0
differences. Taken again after the review, on the committed tree (format 2,
nothing uncommitted, about 4 minutes), with the same numbers; the compare of
all six sections with the ledger: 0 differences, exit 0. Two TODO probes exit
1 in it (`conta-todo.js`: boxes outside the `####` level; `percorsi-del-todo.js`:
bench outputs that exist only in the main checkout), which the real snapshot
records as they are. The real snapshot, with the ledger of the bench now running as
the second one and the tools read over stdio with SOLIDWORKS closed, is taken
after that bench ends. The test «every lookup by name that falls back to null
found its target» will be listed unstable (skipped on 0.124.0, run on
0.124.1): a bench change, not flakiness.

---

## 0.124.1 — 1 October 2026 · library 0.148.0 (unchanged)

**Full bench on bridge 0.124.1 / library 0.148.0, 1/10/2026: 2160 tests, 0 failed**
(1771 expectations checked). The bench's lookup log ran this time: «every lookup by name
that falls back to null found its target», 106 lookups, none null. This run and the one
of 0.124.0 are the two reference runs of W3.1-bis.2 item 11.

### The bench's lookup log runs on a full bench

- **The bench took any `--libreria` for «another library»** (W3.1-bis.2, item 3).
  `banco.ps1` always passes `--libreria`, with the same path as the bench's
  default, so the end-of-run test «every lookup by name that falls back to null
  found its target» was skipped on every full bench (seen on the bench of 0.148.0).
  Now only a different library file counts as another (`SameLibraryPath`, full
  paths without case); an older library named by hand still skips the test. A
  contract check in `ReflectionTests` reads the source and is seen red on the old
  line. Prove.Contratti: 761 checks.
- **`Prove/LEGGIMI.md` repaired:** an edit of the merge commit of 0.124.0 (d2c7dd9) read the
  file with PowerShell 5.1 `Get-Content` without an encoding and wrote it back: 26 lines with
  every accented letter, guillemet and dash turned into mojibake. Converted back (1252 to
  UTF-8); against the last healthy version only the intended edits differ.
- **W3.1-bis.2 item 10 rerun on SOLIDWORKS:** the eight probes that moved from base
  planes by localized name to base planes by position (`CorpoRotto`, `DimXpert`, `Lamiera`,
  `Mensola`, `Orfana`, `RaccordoDiFaccia`, `SeguiIlBordo`, `Spessore`) were run old (built from
  2df71c4) and new, one at a time: the same output line for line (titles, paths and
  times normalized). `Spessore` needs SOLIDWORKS Utilities loaded (`--carica`): its first
  thickness analysis after loading the add-in answers differently from the next ones,
  old and new alike; with the add-in already loaded the two versions give the same
  numbers.

## 0.124.0 — 1 October 2026 · library 0.148.0

**Full bench on library 0.148.0, 1/10/2026: 2159 tests, 0 failed** (1771 expectations
checked), the new mate case green, and the test ledger written (2161 tests, 284 cases, 0
identities unresolved). One net did not run: «every lookup by name that falls back to
null found its target» was skipped, because `banco.ps1` always passes `--libreria` and
the bench took any `--libreria` for «another library». Fixed right after this entry
(see the next version). Prove.Contratti: 760 checks, 0 failed.

Two lots, merged as two squashed commits: W3.1-bis.2 (the safety nets before the English renames) and Z-C14 (the second number of a mate).

### W3.1-bis.2, the safety nets (branch w31bis-2)

**The nets that must turn red when the translation of W3.1-bis renames a
protocol name, a case key, a catalogue name or a namespace and breaks something
in silence (TODO entry W3.1-bis, step .2, items 1-10).** Built on bridge
0.123.0 / library 0.147.0; the version is not raised: no operation, message or
tool of the bridge or the library changes. Every net was seen failing on fake
cases, and where it could the seen-red stays as an automatic check (a contract
group or a self-test). **Item 11 is NOT done**: the snapshot and the two
reference bench runs come next, on SOLIDWORKS. On the full bench of 0.148.0 the
case-key loader refused nothing and the test ledger was written. Prove.Contratti: 736 checks (693 before),
only the three bench-freshness checks red (the last run is 0.146.0).

- **Item 8, `<Deterministic>` (its own commit).** `Oce.Solid.Agent.csproj`,
  `Banco/ProvaSaldatura.csproj` and `Prove/Prove.Contratti.csproj` build with
  `<Deterministic>true</Deterministic>`: two builds of the same source give the
  same bytes, so the IL before and after a translation lot can be compared.
  None of them uses a version wildcard, and no code reads PE timestamps.
  Measured with `Controlli/lingua/same-il.ps1 -OnProjects`: before the change
  the three projects differed in bytes between two builds; after it, 0 lines and
  identical bytes in all five projects. The bench and the contracts also get
  `<PathMap>`, so `[CallerFilePath]` (item 7) embeds `Banco\File.cs`, not the
  absolute path of the checkout.
- **Item 1, the README counts in both languages.** `ProveCatalogo.Numero()`
  reads Italian 0-99 (as before) and English 0-999 and digits, in the README
  forms «N operations», «N drawing operations», «drawing operations are N».
  The silent `if (n < 0) continue` is gone: a word that looks like a number
  and cannot be read is red, and a README with no count read at all is red, so
  the check cannot go blind after the translation. Seen red on every run by 17
  fakes («ninety-six drawing operations» against 94, a misspelt Italian 94 that
  used to be skipped, «fourty»...). Not done: Italian hundreds (the word for
  100 is a new Italian protocol element the gate blocks); a hundred with a tens
  root or a unit in it is red, not silent, and the rest is documented in the code.
- **Item 2, the refusals that name a remedy, in both languages.**
  `ProveIRifiuti` has a table of 29 Italian remedy words with their English
  forms (90 in all). The share is now a floor kept as the exact fraction
  236/935, so ONE new mute refusal is red (the header promised it, the code only
  checked the absolute count). Every Italian word has English partners that
  catch its sample; on the Italian refusals the English forms add nothing (209
  of 817 either way, and 75 of 578 on the tree at b0587d6). Floor 234 -> 236:
  two English refusals that really name a remedy. Seen red on fake sources on
  every run (one more mute refusal: 236 of 936 is under the floor); by hand,
  the English form «indicate » made the Italian-subset check red.
- **Item 3, the names looked up by reflection (`Prove/ReflectionTests.cs`).**
  Static: the literal names handed to `GetType`, `GetMethod`, `GetField`...
  and to 19 helpers that pass them on (discovered, not listed) are extracted
  from `Prove`, `Banco` and `Controlli` (333 names in 27 files) and must resolve
  on the built bridge, library and catalogue exporter. Run time: the contract
  helpers log every lookup by name through `ReflectionLog`, and the last group
  is red if one came back null (399 lookups in a full run, none null). Seen red
  on every run on fakes; by hand, `GetField("Riuscito")` renamed in
  `Banco/Program.cs` gave «1 do not resolve». The bench has its own log,
  `Banco/ReflectionLog.cs` (added by the review): a member name resolves as
  soon as ANY type declares it, and 14 of the 58 direct member names of `Prove`
  and `Banco` are declared by more than one type, so a stale name read with `?.`
  (the schemas of the mutations, the catalogue count of the header, the
  signatures of the state) could pass the static net and go on with null. Those
  lookups go through the log, a full or partial run ends with the test «every
  lookup by name that falls back to null found its target» (skipped with
  `--libreria`, whose older library lacks newer names by design), a mutation or
  regression run makes a miss a bench error, and a contract forbids a `?.`
  read of a lookup anywhere else in `Banco/`. Both seen red on fakes on every
  run; the bench-side test has not run yet (it needs SOLIDWORKS).
- **Item 4, the bench refuses unknown case keys.** `Banco/case-keys.json`
  lists, for 13 places in a case, the keys the bench reads, the documented
  ones, the children and 184 frozen free keys (prose notes; 182 distinct keys
  over 302 cases, none a misspelt schema key). `Banco/CaseKeys.cs` checks every
  declared case, every mutation and every regression case before anything is
  built: an unknown key is a bench error naming its path
  (`passi[2].deve_fallire.volume_unchanged`). Contract `CaseKeysTests` (15
  checks): every case of the tree is clean, the frozen list only shrinks
  (`FrozenFreeCeiling` = 184, to 0 in W3.1-bis.4), no frozen entry is unused,
  every read key is a literal the bench sources read. The review found that
  last check green on a reader renamed alone: a doc comment still quoted the
  key «document», and the self-tests of the bench WRITE every expectation key
  in fake cases. Comments and writes («["key"] = ...», «{ "key", ... }») no
  longer count as a reader; seen red with the reader of «document» and the
  reader of the tolerance renamed alone. Seen red on every run: a
  made-up key in each of the 13 contexts, a read key renamed as a wrong
  translation in 12, 12 malformed lists. The pending case
  `a_dimension_hidden_by_hand_does_not_count` would be refused as it stands.
- **Item 5, JSON readers that fail when a key is missing.**
  `conta-provate.js`, `controlla-catalogo.ps1`, `domande-senza-risposta.js`,
  `prova-x2.js` and `detect-italian.js` exit non-zero on a missing or renamed
  key instead of reading `|| []` or `$null` as nothing. `controlla-catalogo.ps1`
  had a real silent defect: `famiglie` is an object and was read as a list, so
  0 declared families were checked; it also gains an interop resolver
  (`-SwInstallPath`, `SW_INSTALL_PATH`) and now runs to the end on this machine.
  A lexicon without `content_words` used to leave the language gate blind with
  exit 0. The permanent check is `Controlli/expected-keys-test.js` (28 cases
  with a built library): against the old readers, 21 of 24 cases were not as
  expected. The review added one: `conta-provate.js` read the bench's own C#
  inside an empty `catch`, so a renamed folder counted as «touches nothing»;
  now it exits 1 (`--bench <folder>` for the fake).
- **Item 6, the strings another program looks for.**
  `Controlli/lingua/consumed-strings.js` and its inventory
  `consumed-strings.json` (39 entries, 46 runs): installer and bridge, the bench
  report lines `banco.ps1` parses, the release script and this suite's summary,
  the CI proof and the language gate, and the other pairs the TODO lists. Each
  consumed string must be in a string literal (never a comment) of its producer;
  consumers are bound by the SHA-1 of their literal, `--rebind` after a
  translation lot. Contract `ConsumedStringsContract` runs `--self-test` (25
  fakes: the string taken out, kept only in a comment, translated on one side,
  a regex alternative no longer produced...), then the check, then a floor of
  39 entries. By hand, translating the partial-run marker of `Banco/Program.cs` broke
  two entries. Not done: producers are checked on the source literals, not on
  the ldstr heap of the built assemblies.
- **Item 7, the bench report names every test.** `Segna`, `SegnaAttesa`,
  `Saltata` and `ErroreDiBanco` take the caller's file, member and line;
  `Banco/TestLedger.cs` gives each test the identity `File.cs:Method:ordinal`
  (plus `@case` and `#n`), keeps per case the outcome and the expectations
  attempted and checked, and writes `Banco/resoconto/tests.json` in every mode.
  One new console line (`test ledger: ...`) and, in full and partial runs, one
  more test: «every test has its identity, file:method:ordinal». Existing lines
  and what `banco.ps1` parses are unchanged. Contract `BenchReportTests` (10
  checks): the source scanner on a probe of 14 call shapes and against the
  compiler's `CallerMemberName`; a wrong member or a line without a call stays
  unresolved, never guessed. By hand, the scanner agreed with Roslyn's syntax
  trees on 2,421 real recording calls. Not done: the bench-side check seen red
  on a real run (it needs the bench).
- **Item 9, `Controlli/lingua/token-stream.js`.** The token streams of C#
  (Roslyn from MSBuild), JavaScript (acorn, Node's own copy) and PowerShell (its
  own parser), without comments and whitespace, literals compared by value;
  modes identical, strings and renames (with a TSV map). `--self-test`: 31 of
  31, with a failing case in every language for every mode; making the
  identical mode accept everything gives 27 of 31. Not done: shell, YAML and
  csproj (no tokenizer on this machine, nothing installed).
- **Items 8 and 9 in the suite.** The self-tests of `token-stream.js`,
  `expected-keys-test.js`, `prova-x2.js` and `same-il.ps1` ran only from the
  pre-push hook; the new group `SafetyNetSelfTests` (4 checks) runs them with
  the contracts, on every machine and on the CI runner. A self-test that says it
  cannot run here (exit 2) is «not runnable here», never green. Seen red on
  1/10/2026 with the identical mode of `token-stream.js` accepting everything.
  `Controlli/lingua/same-il.ps1` (ildasm without line numbers, MVID and
  banner normalized; modes Identical, Ldstr, Renames; `-SelfTest` 17 of 17)
  stays a re-runnable command for the five projects (`-OnProjects`, about 6
  minutes, needs MSBuild and the interop on disk).
- **Item 10, the base planes by position, not by localized name.** Eight
  probes of `Controlli/` selected a base plane by its Italian name (sometimes
  with the English one as fallback): `CorpoRotto`, `DimXpert`, `Lamiera`,
  `Mensola`, `Orfana`, `RaccordoDiFaccia`, `SeguiIlBordo`, `Spessore` (the TODO
  says seven; `RaccordoDiFaccia` kept the old code in the batch of 28/9 that
  gave its siblings the new rule). Each now takes the first, second or third
  `RefPlane` feature of the tree, the rule `Nervatura`, `Riempimento` and
  `TipoDellEstrusione` already used. The library itself left that rule in
  Z-C21.5 for `Riferimenti.BasePlane` (by geometry): each new helper says so,
  and moving the probes to the geometric rule would need `lib = $true` in
  `costruisci-controlli.ps1`. All eight compile (`build.ps1` builds every
  probe). **Not rerun on SOLIDWORKS**: «the same outcome as before» waits for a
  free SOLIDWORKS.
- **Not done in this step**: item 11 (snapshot and reference bench runs); the
  bench-side null-lookup log seen on a real run; the ldstr cross-check of item 6; Italian hundreds
  in `Numero()`; the probes of item 10 on SOLIDWORKS; `<Deterministic>` in
  `Installer/Installa.csproj`; the silent readers outside `Controlli/`
  (`.github/passi/quanto-non-ha-girato.ps1`, the answer readers of
  `Banco/*.js`). The floors (`Pavimento` 236, 250 literal names, 300 lookups,
  39 consumed strings) go down with the tree only with the reason written next
  to them.

### Z-C14, the second number of a mate (branch z-c14)

**`accoppia` now measures which directions a mate blocks, and refuses a
REDUNDANT mate (TODO entry Z-C14, RAG note 82d).** Built on bridge 0.123.0 /
library 0.147.0. On the full bench of 0.148.0 the new case and the existing cases
that call `accoppia` are green.
Prove.Contratti: 708 checks run in this worktree (710 with `TODO_MD` pointing at
the TODO, which a worktree does not have), only the two bench-freshness checks
red plus the README count: a worktree of main built the same way runs 690 of
the 693 its README states, so the README says 693 + 18 = 711, the 18 being the
new group (17 as written, one added by the review).

- **Why.** The mate count before/after was the whole judgement, and a redundant
  mate raises it too. Nothing in SOLIDWORKS sees one: probe
  `zc14/probe_mate_dof` (30/9/2026, two 40 x 40 x 20 blocks, C1 fixed) read the
  same coincident added twice as AddMate5 NoError, GetWhatsWrongCount 0, mate
  feature error 0 and GetConstrainedStatus 2, as for the first one;
  GetRemainingDOFs answers Unavailable in every state.
- **The perturbation test (`ReadBlockedDirections`).** Each non-fixed
  top-level component of the pair is moved 5 mm along X, Y, Z and turned 5
  degrees about X, Y, Z of the assembly through its origin, one move at a time:
  IComponent2.Transform2 set from IMathUtility.CreateTransform, EditRebuild3,
  Transform2 read again. Only the moved direction is judged
  (`DirectionBlocked`): a translation is blocked when it is back within
  0.01 mm along its axis, a turn when the angle is back within 0.01 degree. The
  probe's first rule judged the whole transform and read the parallel's turns
  free, because the solver put each turn back while sliding the block 1.667 and
  2.435 mm along a free direction. The probe read 0 blocked with no mate, 3
  (TY, RX, RZ) with one coincident, 3 again with the same coincident repeated,
  4 with a parallel added, 6 fully mated. After each move the component goes
  back where the move started; at the end it gets its original transform, is
  rebuilt and read, and the drift left is reported. Cost: 13 rebuilds per
  non-fixed component per reading, read before AddMate5 and after it.
- **Never a guess.** A transform that cannot be set or read, a set that did not
  move by the step asked, a rebuild answering false or an orientation that does
  not come back makes the reading «unknown» with the reason, and the mate is
  kept. A fixed component is not moved (fully blocked by definition); a
  component inside a sub-assembly is not moved either (unknown).
- **The verdict (`MateVerdict`).** «removes» when any direction is newly
  blocked; «redundant» only when nothing is new and one side is anchored (fixed
  or already 6/6 blocked); «not_judged» with two free components (moving one
  can drag the other); «unknown» on an unreadable or self-contradicting reading
  or an anchored mate that reads zero blocked. A redundant mate is taken out by
  the object AddMate5 returned (`mate as IFeature`, Select2, DeleteSelection2,
  both counts read again, `MateRemovalNote`) and the step is refused, saying
  which directions were already blocked and that AddMate5 NoError and
  GetWhatsWrongCount 0 do not see it.
- **The answer** adds `blocked_directions_before`, `blocked_directions_after`,
  `degrees_removed`, `direction_test`, `residual_drift_mm`,
  `residual_drift_deg` (per component, in the order of the counts) and
  `mate_verdict`; `giudizio` is now in English and names what is still free.
  The catalogue entry says what is measured, the refusal, when the test stays
  unknown and the cost.
- **Not measured yet, for the first bench:** `mate as IFeature` on the object
  AddMate5 returns (the guide and probe zd345 confirm it for GetMates objects),
  Select2 + DeleteSelection2 on a mate feature, EditRebuild3 answering true
  during the test, GetWhatsWrongCount 0 after the repeated coincident.
- **Bench:** new case `assiemi/the_same_mate_twice_is_refused_as_redundant`
  (red on 0.147.0 at step 2: the same coincident again is accepted because the
  count goes from 1 to 2; step 4 is the probe's parallel state and checks the
  rule of the moved direction inside SOLIDWORKS); `un_accoppiamento_non_basta`
  now wants «still free in TX, TZ, RY», blocked TY, RX, RZ and 3 degrees
  removed. Banco/LEGGIMI: assemblies 21 to 22.
- **Contracts:** `Zc14MateDirectionsContract`, 17 checks as written (18 after the review): the probe's S3 rows
  against both rules, six source rules each seen red on changed copies of the
  real source (26 mutants), and the pure rules by reflection on the built
  library (`MovedForTest`, `DirectionBlocked`, `MateVerdict`,
  `MateRemovalNote`).
- **Review of the branch (30/9/2026), two fixes before the first bench.**
  (1) The probe named three outcomes of a move -- BLOCKED, free, ELSEWHERE --
  and the first code kept two: a move the mates put back only in part read
  «free». A face up to 8 degrees off the axis (the face is chosen with a dot
  above 0.99) puts a translation back only in part, and a direction read free
  before and after a mate that holds it is a mate called redundant and taken
  out. Now a move that after the rebuild is neither back (`DirectionBlocked`)
  nor where it was moved (`MoveTookEffect` on the same reading) makes the
  reading unknown and the mate is kept; none of the probe's rows fell there.
  (2) The test set back only the component it moved; a rebuild can carry a
  component mated to it along. The other top-level non-fixed components are
  now read before the test, any that is away from where it was is set back in
  the same final rebuild (still 13 rebuilds), and what is left is said in the
  answer. One more contract check (18) and six more mutants (32).

## 0.123.0 — 30 September 2026 · library 0.147.0

**The four items the 0.122.0 entry listed as not done in Z-C21.5: the two «last
sketch of its type» lookups (S7), N7, N23 and C4-3; and Z-C21.11, the assembly
components that were not read: lightweight, envelope and hidden (N5, N27, M23).**
Built on bridge 0.122.0 / library 0.146.0. **Full bench on library 0.147.0,
30/9/2026: 2139 tests, 0 failed** (1754 expectations checked), the seven new cases
(`saldature/the_end_cap_skips_a_drilled_wall` and the six of Z-C21.11 below)
green. Prove.Contratti: 693 checks, 0 failed.

- **S7, the sketch just drawn (`SchizzoAppenaCreato`, `SelezionaSchizzoAppenaCreato`,
  `BloccaSchizzo`).** They took the LAST `ProfileFeature` of the tree. Now the
  sketch is the one `ProfileFeature` born since the caller took
  `FeatureNamesNow` just before selecting the plane or face
  (`FeatureBorn`, the same decision 0.146.0 uses for planes and axes). The nine
  callers are the rectangle, profile, circle, cylindrical cut, rectangular cut,
  slot, extrusion on a face, revolve and text operations; the profile extrusion
  and the revolve lock the sketch found the same way. Messages are kept: none
  born says the old sentence, an unreadable tree adds why, two or more born is a
  new refusal instead of taking the later (`SketchNotIdentified`). The cast of
  `ISketchManager.ActiveSketch` to `IFeature` was not used: only a VBA example
  does it, no Remarks says it. Contract `Zc215SketchIdentityContract` (4 checks,
  seen red on 8 mutants of the source); no bench case can be red on 0.146.0 by
  construction (no operation moves the rollback bar or leaves a sketch unborn).
- **S7, the sweep path (`UltimoSchizzoFeature`, deleted).** `sweep_circolare`
  took the last sketch of the tree, 2D or 3D. Now the path is the one
  `3DProfileFeature` born since the names taken before `Insert3DSketch`; the old
  sentence is kept through `SketchNotIdentified`. Covered by the same contract.
- **N7, an end cap on a drilled wall (`tappo_estremita`).** «Planar with more
  than one loop» also held for both faces of a tube wall with a hole drilled in
  it, and they went to `InsertEndCapFeature3` as ends. `Riferimenti.OpenEndsOnly`
  now takes such a face only when every face across each inner loop was made
  (oldest owner, `IFace2.GetFeature`, compared by `IFeature.GetID`) by a feature
  that also made a face across the outer loop or the face itself. The faces left
  out are counted in the new `misure.machined_faces_skipped`; a face that cannot
  be read stops the step, and so does a body whose faces cannot be read (it used
  to be skipped). The "no open end" refusal is rewritten in English and counts
  the faces left out. Catalogue: the description and the `estremita_libere`
  genre say it. New bench case `saldature/the_end_cap_skips_a_drilled_wall`
  (red on 0.146.0 by construction: four faces instead of two went to the cap).
- **N23, the hole point (`AncoraIFori`).** Of the sketch points within 1e-7 m
  of the hole it kept the last one found. Now all are collected and
  `SameHolePoint` proves they are one point by the sketch point's own ID
  (`ISketchPoint.GetID`, unique within a sketch: ISketchPoint~GetID.html) or
  refuses, naming every candidate in millimetres with its ID. Two points at the
  same coordinates with two IDs are two points (two coincident holes) and are
  refused; an ID that cannot be read proves nothing. Contract
  `Zc215HolePointIdentityContract` (8 checks); no bench case can place two
  sketch points that close on purpose.
- **C4-3, a document opened or activated with a pending rebuild (the bridge's
  open and activate tools, through `PortaDavanti`).** 0.144.0 (`DocumentByPath`) had already fixed the name
  half (the path and the extension instead of `GetTitle()`). What was left: the
  `Errors` bitmask of `ActivateDoc3` was thrown away. Now
  `Sessione.NeedsRebuildAfterActivation` tests `swDocNeedsRebuildWarning` and
  `PortaDavanti` calls `EditRebuild3` before answering, with
  `rebuilt_on_activation` and its reason in the answer (ActivateDoc3.html,
  Remarks). Contract `Zc215ActivationRebuildContract` (10 checks); no bench case
  can stage a document left stale by a separate activation.
- **C4-3 in the library's own activations** (left open by the first review, done
  after it). The six `DocumentByPath.ActivateAndReadBack` calls of
  `OperazioniEsporta`, `OperazioniLamiera` and `SezioneProfilo` now read the
  bitmask through `DocumentByPath.NeedsRebuildAfterActivation`. OUR document,
  brought forward to be exported (STEP AP242, flat pattern DWG), is rebuilt with
  `EditRebuild3` first (`RebuildIfActivationAsked`), and the step's log says so;
  a false answer is said, not a refusal, since a part with a feature in error
  answers false on every rebuild and was exported the same way before. The
  user's document, put back in front afterwards, is only reported
  (`PutBackNeedsRebuild`): a rebuild nobody asked for would change it. Contract
  `Zc215ActivationRebuildContract` +2 checks (every library call acts on the bit;
  the library's bit test by reflection), 12 in all.
- **Two more «last new feature of a type» loops** (seen by the first review
  outside the rows). The sketched bend (`OperazioniLamiera`) and the rib
  (`OperazioniNervatura`, its sketch and the `Rib` itself) kept the LAST feature
  of the type whose name was new. Now both go through `FeatureBorn`: two sketches
  born refuse with `SketchNotIdentified`, two ribs from one `InsertRib` refuse and
  go back out with the rest, and a step taken away removes every feature of the
  type it made, not one. The bend's «before» is `FeatureNamesNow` (null when the
  tree cannot be read). Contract `Zc215SketchIdentityContract` +2 checks (no such
  loop in the library; seen red on a copy with the old loop), 6 in all. The rib
  and bend cases of the bench are the judges of unchanged behaviour.

**Z-C21.11, which components of an assembly count.** Two probes on 30/9/2026
(SOLIDWORKS 2021 SP5.1, `probe_alleggeriti` and the indented-BOM probe; RAG note
82zz_undecies) measured:
a lightweight component answers `IsSuppressed()` TRUE and `GetModelDoc2` null,
while `GetSuppression2` says 1 (lightweight), `GetBodies3` gives its bodies with
the right volume, `Transform2` is there and `IsHidden(false)` is false; an
assembly reopened with OpenDoc6 Silent|OverrideDefaultLoadLightweight|LoadLightweight
(193), nothing of it left loaded, has every component lightweight; an envelope
(InsertEnvelope, or `CompConfigProperties6` with IsEnvelope true) is left out of
the assembly's `CreateMassProperty` and written to the STEP; a hidden component
(`Visible` = swComponentHidden) is weighed and NOT written to the STEP; the STEP
export resolved every lightweight component; no material reader answered on a
component, resolved or not; the indented BOM (Detailed) writes the tree, not the
parts: A + 3 x S_show(2 B) + B gives A 1, S_show 3, S_show > B 2, B 1.

- **One reader, `AssemblyComponents` (new file).** The load state from
  `GetSuppression2` (suppressed, lightweight, resolved, unknown), part or
  sub-assembly from the extension of the saved path (so a lightweight one keeps
  its kind), `IsEnvelope`, hidden = `IsHidden(false)` here or on a parent,
  `GetModelDoc2` only for a resolved component. `PezziDellAssieme`, which asked
  `IsSuppressed` and so took every lightweight part for suppressed, is gone; the
  mass and the box go through `ComponentsOrRefusal`, which refuses when a load
  state cannot be read. Review: when the extension is neither .sldprt nor
  .sldasm the kind comes from the document, as up to 0.146.0 (a resolved
  component with another extension was otherwise dropped in silence from the
  mass, the box and the STEP count); a component of unknown kind by both
  refuses the mass and the box and makes the STEP count unknown (null).
- **`proprieta_di_massa` on an assembly.** Envelopes are left out of the sum of
  the parts as SOLIDWORKS leaves them out of the mass (they made a right
  assembly refuse with a gap, N27), named in `envelope_components`; hidden parts
  are weighed and named in `hidden_components_weighed`; lightweight parts refuse
  the step with their names (their material cannot be read), unless the new
  boolean argument `resolve_lightweight` is true: then
  `IAssemblyDoc.ResolveAllLightWeightComponents(false)`, the list is read again,
  a part still lightweight refuses, and `lightweight_resolved_on_request` says
  what was resolved (review: a refusal that comes after the resolution says it
  too, since the parts stay resolved). The old list of components whose document was not read is gone. The step used to
  refuse «nothing to weigh, all suppressed» on an assembly opened lightweight.
- **`ingombro_corpo` on an assembly.** Lightweight parts are measured through
  `GetBodies3` without resolving them (`lightweight_components_measured`);
  envelopes and hidden parts are in the box and named (`envelope_components`,
  `hidden_components`). A throw of `GetBodies3`, or a lightweight part that
  answers no solid body, refuses instead of leaving the part out of the box (a
  partial box answered as a success, N5). The step used to have no box on an
  assembly opened lightweight and a partial one with one part lightweight.
- **`esporta_step` of an assembly.** Hidden components are left out of the
  expected solids and named in `hidden_components_not_exported` (the old count
  asked for them and refused a right file); lightweight parts count (the count
  is made after the export, which resolves them); `GetLightWeightComponentCount`
  is read before and after the export (`SaveAs3`, or `PublishSTEP242File` for
  AP242: review), and `lightweight_resolved_by_the_export` says how many the
  export resolved; envelopes are written and counted.
- **`distinta_base_in_tavola`.** Suppressed by `GetSuppression2`, not
  `IsSuppressed` (a lightweight sub-assembly and all its children were left out);
  a sub-assembly by its extension; a lightweight sub-assembly that is not
  suppressed refuses with a reason (its BOM option is in its document), and so
  does an unreadable load state, the reason passed to the answer. The INDENTED
  table is now compared with the measured tree (M23): siblings grouped by file
  and configuration, the children of ONE instance under a sub-assembly row, a
  Hide sub-assembly one row with nothing under it, a Promote one no row; until
  0.146.0 every indented table of an assembly with a sub-assembly was refused.
  New answer key `counted_as`. Catalogue texts of the four operations say it,
  in English.
- **Bench.** The assembly builder has three new keys, `envelope`, `hidden` (per
  component, read back) and `reopen_lightweight` (saves, closes every document
  of the case, checks nothing is left loaded, reopens with 193 and checks
  `GetLightWeightComponentCount`), and a repeated child `nome` inside a
  sub-assembly is a second instance of one part (Banco/LEGGIMI.md). Six new
  cases: `assiemi/the_lightweight_assembly_has_a_box`,
  `the_lightweight_assembly_is_weighed_when_asked`, `the_envelope_does_not_weigh`,
  `the_hidden_component_is_not_in_the_step` (each red on 0.146.0: no box, the
  wrong refusal, the envelope in the sum, two solids expected for one), and
  `the_lightweight_step_says_the_export_resolved_them` (green on 0.146.0 except
  for the new answer field, as the case says); `tavola/the_indented_bom_follows_the_tree`
  (red on 0.146.0: 2 flat items against 4 rows). The two risks named before the
  bench did not happen: the bench's state reading left the parts lightweight
  (two components still counted lightweight after the box), and the station's «resolve
  lightweight: prompt» option opened no dialog.
- **Contracts.** New group `Zc2111ComponentsContract`, 28 checks: ten source
  rules, each seen red on changed copies of the real source (no `IsSuppressed()`
  in the library, the reader, the kind of a component, its four users, mass, box, STEP count and
  answer, BOM), and by reflection `LoadFromSuppression2`, the extension rules and
  `ExpectedBomItems` on the indented probe trees. `AssembliesZc212terContract`:
  the indented T_hide check follows the M23 measurement (A 1, S_hide 1).
  Refusal floor 232 -> 234; the undecided obsolete-call count of
  `OperazioniMisuraAssieme.cs|Add` 2 -> 1 (the new loops type their receivers).

## 0.122.0 — 30 September 2026 · library 0.146.0

**The configuration is always passed (TODO entry Z-C21.4) and identity by the
object, not by position (Z-C21.5), with the ids the audit rounds assigned to
Z-C21.5 (S19, C3-6 extended), the verdicts of the Z-C21.3 probes, S1 in parts only
(Z-C21.13) and the turned-view leftover of the Z-C21.8/9 review.** Built on bridge
0.121.0 / library 0.145.0. **Full bench on library 0.146.0, 30/9/2026: 2085 tests,
0 failed.** A first run was stopped: it had been started without rebuilding the
bench, and this lot changes `Banco/AssiemeDiProva.cs` and `Banco/CasiDichiarati.cs`.
The eight new cases were written red on 0.145.0 by the guide or by the code, not by
a measurement; on 0.146.0 they are green.

- **L11-1, `inserisci_componente`:** `configurazione` is checked against the
  configurations of the open file before inserting (names compared whole) and a
  name the file lacks is refused with nothing inserted. AddComponent5 gets
  UseConfigForPartReferences true and the named CurrentSelectedConfig option; the
  configuration the new component references is read back, and a different one
  takes the component out again (said removed only after counting the components).
  Case `assiemi/the_component_comes_in_with_the_configuration_asked`.
- **L16-2 and L16-3, `distinta_base_in_tavola`:** the view's configuration is read
  first; when the assembly has another one active, the view's is activated for the
  reading and the previous one put back in a finally, each read back; the step
  refuses before inserting the table if either switch fails. New answer keys
  `configurazione` and «assembly_active_configuration». The obsolete
  `IComponent2.ExcludeFromBOM` is replaced by `GetExcludeFromBOM2` in the owner
  configuration; an unreadable exclusion refuses instead of meaning «included»
  (Frozen key removed). Bench builder: `configurations` in `assieme` and the step
  key «activate_before». Case `tavola/the_bom_follows_the_view_configuration`; its
  step 5 doubles as the L16-4 probe (top-level table on a configuration that is not
  active), which stays open.
- **M18, `sostituisci_componente`:** a configuration the new file lacks is refused
  before the swap; after it, the configuration of the component(s) that took the
  place is read back into `configurazione`, with «configuration_asked» and
  «configuration_as_asked»; a mismatch opens the verdict with a WARNING and the swap
  stays. The ManuallySelect switch waits for the N57 probe. Case
  `assiemi/the_replaced_component_says_its_configuration`.
- **M32, `distinta_taglio_in_tavola`:** not changed, documented: the catalogue no
  longer says an empty configuration is the view's, and says what the comparison
  counts (top-level cut-list folders, active configuration).
- **L8-5 closed as NOT REAL** by the Z-C21.3 probe (comment in
  `OperazioniProprieta.cs`); the ForceRebuild3 before the weldment Get6 loop stays
  as a defence. L17-1/M10 and L6-3/S3 verified already fixed.
- **S7, the feature a call made:** `FeatureNamesNow` / `FeatureBorn` take the one
  feature of the type whose name was not there before the call; the old last-feature helper and
  every `IFeatureByPositionReverse(0)` are gone. Used by `crea_piano`, `crea_asse`
  (L8-6: on sheet metal it no longer finds the flat pattern), `piano_per_tre_punti`,
  `guscio`, `loft_fra_due_cerchi` (N13), the rib offset plane and `SenzaLoSchizzo`.
  Case `lamiera/the_axis_and_the_planes_of_a_sheet`, contract
  `Zc215IdentityContract`. The two «last sketch of its type» lookups
  (`SchizzoAppenaCreato`, `UltimoSchizzoFeature`) are left for a follow-up.
- **S19 and C3-6 extended, base planes by geometry:** `Riferimenti.BasePlane` finds
  front, top and right by their normal (same sign) through the origin, as probe
  C3-6 read them, the first in the tree on a tie; used by `TrovaPianoBase`,
  `SelezionaPiano`, the part view section and `specchia_componente` (no more counting
  RefPlanes in tree order). On an assembly the plane transform was not probed: the
  existing mirror cases are the check. Contract `Zc215IdentityContract`.
- **S5, bodies by signature:** `ingombro_corpo`, `proprieta_di_massa` and
  `verifica_geometria_corpo` resolve «corpo_uid» through the state and FirmaCombacia
  and refuse a uid that now names another body; the answer names the body by the uid
  asked (it said C1 for C2). Case `misura/a_body_named_before_the_cut_is_not_measured`.
- **M1, `accoppia`:** face directions are judged in assembly space (Transform2 of
  the component); an unreadable placement refuses. Case
  `assiemi/the_turned_pin_is_mated_by_assembly_directions`.
- **L11-3 and L11-5, nested components:** `fissa_componente` and
  `sostituisci_componente` refuse a component inside a sub-assembly and name the
  sub-assembly; the swap counts as done only when a component newly on the new file
  exists and the target left its old file («took_its_place»). The assembly state
  writes «top_level» (bool, null if unknown) on every component and
  «nested_components»; `fisso` is null on a nested component, and
  the counts of fixed, non-fixed and resting components and the
  constrained verdict count top-level components only. The bench builder makes
  sub-assemblies. Case `assiemi/a_component_inside_a_sub_assembly_is_refused`.
- **M19, undo recording:** started on the document of the plan, finished on the
  document that holds it, closed and reopened when a step makes a new document; the
  state is read from the plan's document; `_sw.IActiveDoc2` reads in
  `EsecutorePiano.cs` go from 5 to 2. Contract `ProveIlDocumento` (+4 checks). A live
  probe through the bridge is still to run.
- **M4 and L13-3 closed as already fixed in 0.144.0** by `DocumentByPath`.
- **S1 in parts:** `ApiHelpers.ReadDimension` reads GetSystemValue3(swThisConfiguration,
  null) through `OneDimensionValue`, which throws on any answer that is not one
  number; the obsolete `IDimension.SystemValue` is gone (Frozen key removed). The
  drawing readers stay on GetSystemValue2, since drawing-owned dimensions answer null
  (probe_dimension_drawing_owned). The part state writes null, not 0, for a
  dimension it cannot read. Contract `Zc2113PartDimensionContract`, before/after case
  `quote/the_dimension_is_read_in_the_active_configuration` (green on 0.145.0 by
  design).
- **N80 in the turned view (`OperazioniTondoTavola.cs`):** `QuotaFraDue` asks
  `NonCresciuta` first; a dimension born without its object is recorded by its three
  callers instead of dropped, and the silhouette-pair loop stops on it. Contract
  check in `Zc2189DrawingContract`; no bench case can make SOLIDWORKS do it.
- **Integration:** catalogue texts of the operations above rewritten in English;
  new undecided obsolete-scanner calls removed by typing their receivers
  (`IConfiguration`, casts of `bool?`) instead of raising the frozen counts; floor of
  refusals with a remedy 230 -> 232; Prove/LEGGIMI 639 checks (636 before the bench summary adds its own); Banco/LEGGIMI case
  counts; language baseline lowered.
- **Review:** the exclusion from the BOM falls back to the unnamed form when the
  named GetExcludeFromBOM2 throws, not only when it answers something that is not one
  Boolean (a COM error there made every BOM with a child refuse); the summary of the
  bench's `NascondiPrima` is back on its method (the new `ActivateBefore` had been
  inserted between them). Still open from the audit list of Z-C21.5 and not in this
  lot: N7 (a drilled tube's side face taken for an open end), N23 (the last coincident
  sketch point of a hole kept without saying it was ambiguous) and C4-3 (rebuild on
  activation).

## 0.121.0 — 30 September 2026 · library 0.145.0

**Returns and messages (TODO entry Z-C21.8), and the catalogue, comments and RAG
notes that said the wrong thing (Z-C21.9), with the findings the audit rounds
Z-C21.10 and Z-C21.12 assigned to them and the NOT-REAL findings of the Z-C21.3
probes.** Built on bridge 0.120.0 / library 0.144.0. **Full bench on library 0.145.0,
30/9/2026: 1986 tests, 0 failed**, after the first run had shown that
IExportPdfData.SetSheets answers false on PDFs that come out complete (the L13-6
bullet below says what changed). Every behaviour change has a bench case or a
contract test that is red on 0.144.0.

- **Export and save warnings (L13-7).** The note that `ErrorCodeDecoder` had no
  callers was stale (`SaveErrorNames` already decoded the SaveAs3 Errors). What was
  missing is now there: the Warnings mask is decoded by `swFileSaveWarning_e` name
  on STL, STEP/IGES, `esporta_pdf` and `salva_documento`; the answers carry
  `save_warnings` (null for AP242, whose call has no mask) and `save_warnings_note`
  when the model needed a rebuild, had rebuild errors or had views needing an update;
  failure messages give the names next to the raw codes.
- **User preferences put back only as read (N17).** `UserPreferenceSnapshot`
  replaces the fixed 0/false defaults of the STL and exchange exports: only
  preferences actually read are written back, the Set answers are read, toggles are
  read back, and `preferences_not_restored` names any that did not go back
  (swSTLQuality was written as 0, out of its 1..3 range, after a failed read).
- **Assembly returns (L11-7, M39, Select4 in `accoppia`, N18, N38).** The component
  mirror reads its rebuild error and removes a mirror born in error; `bollatura`
  refuses before AutoBalloon5 when the view cannot be selected and reports
  `balloons_returned` / `balloons_count_note`; `accoppia` refuses when a face cannot
  be selected, without calling AddMate5; `verifica_interferenze` reads its three
  settings back (refusing on a difference), leaves suppressed components out of the
  count, and counts POSSIBLE interferences apart (`certain_interferences`,
  `possible_interferences`). Open: when SOLIDWORKS 2021 flags an interference as
  possible was never measured.
- **Weldment returns (N15, N41, N30, N19, N22, N69).** `distinta_taglio` reads
  SetAutomaticCutList, SetAutomaticUpdate and UpdateCutList, checks every cut-list
  item against the bodies of its folder (QUANTITY, equal volumes, total bodies) as
  warnings, and refuses a cut list that is not automatic. A refusal removes only
  the weldment feature this step added (never features picked by new name, which
  could delete the renamed solid-bodies folder) and says whether IsWeldment reads
  false again. `taglia_membri` refuses an end condition that is not one of the
  four, before touching the part (case
  `saldature/the_trim_refuses_an_unknown_end_condition`). The gusset null-path
  message no longer blames geometry; three comments now say library 0.139.0.
- **Drawing returns (L13-6, L16-1, M38, M40, M41, N47, N80).** `esporta_pdf` reads
  the SetSheets answer and reports it (`set_sheets_answer`): the first full bench on 0.145.0 measured false on PDFs that came out complete, so the page count of the written file decides, not the answer; the page counter
  (`CountPdfPages`) answers -1, not 0, when it cannot count, and
  `PdfPageCountRefusal` refuses both an uncounted and a wrong count. The weld symbol
  is refused with fewer than two texts; the gtol frame reads its Ø back
  (`diameter_read`, case `tavola/the_position_frame_reads_its_diameter_back`); every
  removal (view, annotation, sheet sketch segment) is said done only after a
  recount. The section and the detail read the view the call returned
  (`view_read_from`) and remove the line or circle they drew on a refusal. A broken
  walk over the drawing's dimensions answers null, not a partial count; a dimension
  born without its object is kept and listed, and no longer triggers a second
  diameter dimension on a hole. Residual: the same `q == null || NonCresciuta`
  pattern in `OperazioniTondoTavola.cs`.
- **Machining and anchor returns (L5-5, L5-7, L12-9, L17-8, M24, M35, M36, M37,
  M42, M43, N16, N21, N49, N53, N56, C7-3, C9-6).** AccessSelections answers are
  read; the tolerance decimals are set and read back on every displayed dimension
  (`precision_status` reports 1, 2 and 4); ClosestDistance -1 is refused; Get6
  NotPresent is skipped; `scrivi_proprieta` compares the written text with ValOut
  (`resolved_value`); the picture placement is read back to 0.01 mm; the edge
  fillet and chamfer refuse an unchanged volume (`material_change_mm3`); «put back»
  and «nothing touched» are said only after a reread in `cambia_estrusione`,
  `tolleranza_quota`, `riordina_lavorazione` and `sopprimi_istanza`; the mass sums
  every body or fails; a defect check that cannot be read is not «sound»; the flat
  pattern suppression answers are reported (`flat_pattern_problem`); direct
  sketch-point casts are gone; all seven constrained states are named; a removed
  feature is no longer said to be «still in the part».
- **Catalogue text (S15, S16, L1-3, N4, N12, N44, N62, N64, N81 and the texts of
  the items above).** `accoppia` directions are those of the part's own file;
  `sostituisci_componente` and `inserisci_componente` no longer promise a
  configuration they do not honour; the extrusions say they merge with bodies they
  touch; `tappo_estremita` says that its gap is the side gap, not a depth, and that
  its chamfer is ignored today; `loft_fra_due_cerchi` takes a positive distance only; `quota_in_tavola`
  names its overlap fields and gives one hole callout per diameter AND depth;
  `raccordo_faccia` describes its hand calculation as the check it is;
  `ripeti_riempimento` asks for an axis that lies along the face.
- **Comments that said the wrong thing, and NOT-REAL findings closed with the probe
  numbers (L2-1, L4-1, L7-2, L10-7, L14-1, L15-2, L17-5, C3-1, C3-4, C4-4, C6-2,
  L9-6, L11-4, L12-3, C3-6, M16, M33, L7-5, N10, N60, N65, N71, N72, N74).** Each
  comment now cites the measure (for example N10: every Flip=false variant ends at
  s=+30 from +90 and from -110; L9-6: ConnectedSegmentsOption 1 and 2 identical on
  the round tube in every scene). L7-5 and M16 also get a bench case each
  (`ripeti/the_linear_pattern_follows_a_reference_axis`,
  `ripeti/the_circular_pattern_turns_around_a_straight_edge`).
- **Two historical lines of this file corrected in place, marked:** the gusset entry
  of 0.115.0 said 0.138.0 (N69), and the UseGlobal note of 0.53.0 claimed a measure
  that could not tell the branches apart (L8-3).
- **Adversarial review of the branch.**
  - `modifica_quota` (L5-6, left out of the first pass): a value SetSystemValue3
    refuses gets the remedy of its own swSetValueReturnStatus_e
    (`SetValueRefusalCause`), instead of «usually an equation or a relation» for
    every status.
  - The component mirror a refusal removes is said removed only after the tree is
    read again and no longer holds it (`MirrorRemovalNote`); it said «the assembly is
    as it was» on the answer of DeleteSelection2 alone, and L11-7 had added a caller.
  - `vista_sviluppo` refuses before placing the view when the flat pattern of the
    part does not toggle (M24); the overload that answered only NaN is gone.
  - `sezione_in_tavola` and `dettaglio_in_tavola` never delete a view that was on
    the sheet before the call: with no view returned, the fallback «last view of
    the chain» could have been a view of the user (M41).
  - `inserisci_componente`: the catalogue quoted the guide («in its last saved
    configuration») with words that are not on any page of the extracted guide; the
    quote is gone, and the comment next to AddComponent5 no longer says the named
    configuration is taken.
  - Comments tightened to what the probes measured: N10 (aligned from above only),
    L12-3 (measured after ForceRebuild3, and verifica_geometria_corpo does not
    rebuild), the linear pattern marks (L7-5, grids G0-G3), L17-1 in the state.
- **Contracts.** New groups `Zc2189WeldAssemblyContract` (29 checks),
  `Zc2189DrawingContract` (13), `Zc2189MachiningContract` (27): 615 checks in all.
  `ProveIRifiuti` learned the English imperatives of the rewritten refusals: floor
  217 -> 230 (with the same detector HEAD counts 223, so +7 is this branch). The
  frozen obsolete and undecided counts only went down: the tolerance limits are read
  in one helper instead of three call sites, and nullable `.Value` became
  `GetValueOrDefault()` on the new lines.

---

## 0.120.0 — 29 September 2026 · library 0.144.0

**What the online research of 29 September 2026 decided (TODO entry Z-C21.13, RAG
note 82zz-decies), built on library 0.143.0 with the probes of the same afternoon.
** **Full bench on library 0.144.0, 30/9/2026: 1941 tests, 0 failed**, after two
fixes the first runs asked for (a path not on disk is not a name; never CloseDoc(""),
both below). The 17-tooth gear with CreateSpline3 and AddToDB off: 8808.59 as before.

- **C5-7, STEP AP242 through SOLIDWORKS MBD.** The library no longer writes
  `swStepAP = 242`: the probe `step_ap242` measured that SOLIDWORKS 2021 accepts it and
  writes an AP203 file. AP242 now goes through `IModelDocExtension.PublishSTEP242File`
  (help.solidworks.com/2017/english/api/sldworksapi/...imodeldocextension~publishstep242file.html;
  help.solidworks.com/2017/English/WhatsNew/c_step_242_pmi_export.htm) with our document
  brought to the front (COM identity, not the path) and the previous front document put
  back. The answer is read as `swStep242Error_e`: 4 (MBDLicenseNotAvailable, what the
  bench machine answers) is a refusal naming the SOLIDWORKS MBD licence and AP214/AP203;
  1, 3 and unlisted values are refusals naming the code. A `.step` path is refused before
  the call (the probe got InvalidPath), the default path ends in `.stp`, and a file
  already at the path is moved aside and put back on a refusal (on 0.143.0 it was
  replaced by an AP203 file). New measure `written_with`. Cases:
  `esporta/lim_step_ap242_needs_the_mbd_licence` (machine-dependent: on a machine with
  MBD it must become a success case), `esporta/lim_step_ap242_wants_a_stp_path`; contract
  `Zc2113ExportUnitsContract` (the answers the bench cannot produce, the path rule, no
  bare 242 near swStepAP).
- **L17-7, document length units.** `MmPerUnitaDelDocumento` goes through the pure
  `MmPerLengthUnit`, with the factors of the corrected probe `feet_inches`: angstrom
  1E-07, nanometre 1E-06, microinch 2.54E-05 (swLengthUnit_e and the Custom-first order
  from help.solidworks.com/2021/english/api/swconst/dp_units.htm). Feet-and-inches
  answers NaN (its two readings disagree, 25.53 against 25.40). **Micron also answers NaN
  now, instead of 0.001:** the same probe read it with the nanometre factor from both
  readings, which contradicts a micrometre; until a second probe explains it the
  comparison is skipped and said so. Contract only (no bench step sets units).
- **L1-1, splines: `ISketchManager.CreateSpline` -> `CreateSpline3(triples, null, null,
  true)`.** Natural ends true is S5 of the probe `spline3` (8808.586 mm3, the same as the
  bridge; false gives 8806.568); guide:
  help.solidworks.com/2023/English/api/sldworksapi/...ISketchManager~CreateSpline3.html,
  help.solidworks.com/2022/english/api/sldworksapiprogguide/Overview/Splines.htm. AddToDB
  off and DisplayWhenAdded on around the call, restored in a finally; the input stays
  triples (pairs break the call, S4/S6/S8); a spline counts in `segmenti` only when a
  segment comes back. Frozen key removed from `ObsoleteCallsContract`. Case
  `estrusioni/la_ruota_da_diciassette_denti_si_fa` gains `misure.segmenti = 170` and must
  keep 8808.59 +-0.5. Untested: natural ends true together with AddToDB off (the probe ran
  S5 with AddToDB on); the bench must confirm it, also on the c4r gears for time.
  Contract `Zc2113SplineCentreMarksContract`.
- **Centre marks on views left bare by SOLIDWORKS (`viste_standard`).** The hole walk of
  `croci_di_centro` is now a shared method, `MarkHolesOfView`, unchanged in behaviour;
  `viste_standard` counts marks view by view after the rebuild and walks only views with
  zero marks that this call made (review: a view already on the sheet may be bare on
  purpose, and is never marked; if the names of the views before cannot be read, no view
  is marked). New answer keys: `centre_marks_added_by_bridge` (always),
  `holes_in_views_without_centre_marks`, `auto_centre_marks_option` (the document toggle
  swDetailingAutoInsertCenterMarksForHoles, null when unreadable),
  `holes_left_without_centre_mark`, `centre_marks_note`; `centre_marks_after_rebuild` is
  null when a view cannot be counted. Sources: the silent-suppression mechanisms of the
  second research round (help.solidworks.com/2018/English/solidworks/sldworks/t_Setting_Options_for_Center_Marks.htm,
  help.solidworks.com/2020/English/api/sldworksapi/...IView~AutoInsertCenterMarks2.html).
  No bench case can build a bare view (it happened only in long runs); contract
  `Zc2113SplineCentreMarksContract`. Open: AddCenterMark put no mark back on 26/9 once the
  automatic ones were deleted, so the remedy may add nothing, and the answer then says so;
  the 17/9 verdict on AutoInsertCenterMarks2 was counted with GetCenterMarkCount, which
  does not see automatic marks, and deserves a new probe.
- **N11 / M4 / S18 / N8 / L13-3, documents closed and activated by path or reference,
  never by title.** New public `Riscontro.Disegno.DocumentByPath`: a saved document is
  closed by full path, then by file name with extension (the CloseDoc probe, A-G1 and
  A-G3); a never-saved one with `CloseDoc("")` when it is the active document (not
  measured by the probe: if the read back finds it still open, its title follows, only
  when no other open document answers to it) (the
  ActivateDoc3/Close pages:
  help.solidworks.com/2023/English/api/sldworksapi/...ISldWorks~ActivateDoc3.html,
  help.solidworks.com/2019/english/api/sldworksapi/...IModelDoc2~Close.html), otherwise by
  a title no other open document answers to, or refused (probe A-S: the stem collides).
  Every close is read back (closed, still loaded hidden, still open, refused, failed) and
  hidden documents that went with it are listed. `documento chiudi` also refuses when
  hidden documents with unsaved changes would go with it (probe H4) and answers
  `closed_by`, `still_loaded_hidden`, `still_loaded_reason`, `also_closed`,
  `also_closed_unsaved`. Activation is by file name with extension, then path, with
  swDontRebuildActiveDoc: this removes the swUserDecision dialog of
  `SezioneProfilo.TelaioDiProva` (N8) and of the STEP/IGES reopen (L13-3). The reopened
  STEP/IGES is no longer closed by its title, which with the default path is the part's
  own stem (M4). The weldment profile library is closed by path; if it stays loaded,
  hidden and dirty, `profile_library_warning` says so (probe H1d: the save flag lies).
  No bench case (equal titles need Explorer's hide-extensions, a station setting that is
  off here); contract `DocumentByPathContract`, 13 checks. Still open: which document
  CloseDoc/ActivateDoc3 picks when two titles are really equal, CloseDoc by path on a
  drawing, the bench's own teardown by title, and C4-3 (rebuild on activation).
  **A path counts as a name only if the file is on disk** (first full bench on 0.144.0,
  group `iges_solidi`): a part reopened from an IGES with LoadFile4 was never saved,
  yet GetPathName answered `...\lavoro\gp3_solidi.SLDPRT`, a file that does not exist,
  and CloseDoc given that path closed the reopened part AND the original part of the
  export («Parte2115», never saved), discarding its changes. The collateral check saw
  it and said so, after the fact. `DocumentByPath.PathOnDisk` now gives "" for such a
  path, and the document is closed by its title under the collision rule, as before
  0.144.0.
- **Contracts and counts.** Three new groups, 23 checks (6 + 4 + 13); `Prove/LEGGIMI.md`
  now says 546 controls. The 526 it said before was 3 more than main runs today: main
  (25cc619), built and run in a throw-away worktree on 29/9/2026, gives 523 with the same
  count in every group, so the three groups add exactly 23. `Banco/LEGGIMI.md`: `casi/esporta/` has 15 cases. The served
  catalogue `Strumenti/catalogo.json` is regenerated from `Operazioni.cs` (esporta_step,
  esporta_iges, viste_standard, croci_di_centro, membro_strutturale, telaio_rettangolare);
  the `documento` tool text in `Strumenti/Attrezzi.cs` describes the new close and
  activation.

## 0.119.0 — 29 September 2026 · library 0.143.0

**The obsolete API calls that the Z-C21.3 probes of 28 September 2026 decided,
migrated one per commit.** Each migration follows exactly the call the probe
measured, cites its guide page and the probe numbers in the code, and takes its
keys out of the exact ratchet of `Prove/ObsoleteCallsContract.cs`. Every
bullet names the existing cases that must give the same answer (same volume,
same box). **Full bench on library 0.143.0, 29/9/2026: 1934 tests, 0 failed**, after the
bench families tavola and quote had shown that migration 9 (S1) breaks the
drawing's own dimensions, and it was withdrawn (below). The same run is the green
bench of 0.142.0 (entry 0.118.0).

- **L8-1 / L2-4, the offset plane: `CreatePlaneAtOffset3` -> `InsertRefPlane`.**
  `crea_piano`, the second plane of `loft_fra_due_cerchi` and the plane of
  `nervatura` now select the base plane by name with mark 0 (the guide's «0 =
  First reference entity»; `SelectionMarks.OffsetPlaneReference` goes from 1 to 0)
  and call `IFeatureManager.InsertRefPlane(8 | 256 when d < 0, |d|, 0, 0, 0, 0)`
  through `ApiHelpers.SelectOffsetPlaneBase` / `InsertOffsetPlane`. Probe: G1 top
  +100 at (0, 100, 0), G4 top -100 at (0, -100, 0), G6 front -250 at (0, 0, -250);
  H1 with mark 1 made no plane (the measure of 10 September that ruled the method
  out was the mark); G5 with a SIGNED distance put the plane at (0, 0, 0), hence
  OptionFlip and the absolute value; the loft L2 gave 43980.736 mm3, the same as on
  the old plane. Frozen keys removed: 3 (`OperazioniSw`, `OperazioniPercorso`,
  `OperazioniNervatura`). No answer changes. Cases that must stay identical:
  `piano/piano_superiore_piu_100`, `piano/piano_superiore_meno_100`,
  `piano/piano_frontale_meno_250`, `lavorazioni/il_loft_non_e_un_cono`,
  `lavorazioni/nervatura_nell_angolo_della_l`,
  `lavorazioni/lim_nervatura_fuori_dal_suo_piano`.
- **L10-1, the edge flange: `InsertSheetMetalEdgeFlange2` -> `InsertSketchForEdgeFlange`
  + `CreateDefinition(swFmEdgeFlange)`.** `flangia_spigolo` follows the guide's
  recipe (case E1 of `probe_edge_flange_definition`): the sketch SOLIDWORKS makes for
  the edge, the edge converted into it (`SketchUseEdge3`), three lines L - T up in
  its +Y, then `AddEdges`, OffsetDistance L at the outer virtual sharp, material
  inside, default radius, relief and allowance, `CreateFeature` through
  `ApiHelpers.CreateFeatureFromDefinition`. Probe on the L of the bench (T 2, R 2,
  depth 40, L 25): E1 born without error, OffsetDistance 25, +1736.995 mm3, flat
  80.712 -> 102.425, the same as the obsolete call (E0); OffsetDistance now governs
  the length (E3, 35: +2536.997 mm3), the profile height does not matter (E2), a
  pre-selected edge changes nothing (E4). New: before drawing, the far side of the
  profile is read back in model coordinates and must go along the broad face's
  outward normal, the side the catalogue promises; a flipped flange adds the same
  volume (E5) and no later check would see it, so the other side is refused. New
  refusals are in English. Frozen key removed: 1. No answer changes. Cases that must
  stay identical: `lamiera/la_flangia_a_spigolo_chiude_il_lato` (+1736.99 mm3,
  length 25, flat 102.424), `lamiera/lim_flangia_spigolo_non_di_bordo` and
  `lamiera/lim_flangia_spigolo_senza_dritto` (refused before the recipe).
- **L10-2, the flat-pattern DXF: `ExportFlatPatternView` -> `ExportToDWG2`.**
  `sviluppo_dxf` calls `ExportToDWG2(dxf, part path, swExportToDWG_ExportSheetMetal,
  true, null, false, false, 1, null)`, case X1 of `probe_export_dwg2`: on the saved
  bench L it returned true with 4 LINE entities and the box 40 x 80.712 mm, the same
  as the obsolete call (X0), the part unchanged. ModelName names the ACTIVE document,
  so our document is first brought to the front with `ActivateDoc3` (title, no
  rebuild, as the bridge's `PortaDavanti`), checked by path, and the document that
  was in front goes back there afterwards; a failed activation is a refusal. The
  refusal of a part never saved stays (the unsaved case of the probe was not run).
  Frozen key removed: 1. The only field that can differ is `byte`, the file size
  (22946 -> 23005 bytes in the probe, a different header), which no case checks.
  Cases that must stay identical: `lamiera/la_L_si_sviluppa` (flat 80.712, the DXF
  long side checked against it) and `lamiera/lim_sviluppo_senza_file`.
- **L15-1, the hole table: `IView.InsertHoleTable2` -> `InsertHoleTable3`.**
  `tabella_fori_in_tavola` passes the same six arguments plus TagOrder XY (1),
  TagType alphanumeric (1) and no manual tags, case H1 of `probe_hole_table3`: on the
  bench plate the same six rows as the obsolete call (H0), A1 (15,15), A2 (15,65),
  A3 (105,15), A4 (105,65), B1 (60,40), each on a hole. TagType is honoured (H2,
  numeric: 1 to 5); the marks 1 (origin vertex) and 2 (holes) are still needed (H3,
  swapped: the header row only), so the table-born-empty refusal stays.
  `SelectionMarks.HoleTableDatumOrigin` / `HoleTableHoles` now quote
  IView~InsertHoleTable3.html (same values). Frozen key removed: 1. No answer
  changes. Cases that must stay identical: `tavola/la_tabella_fori_dice_dove_sono`
  (5 holes, 6 rows, positions verified) and
  `tavola/lim_tabella_fori_sulla_vista_di_fianco`.
- **L8-4, the picture in a sketch: `InsertSketchPicture` -> `InsertSketchPicture2(file,
  false)`.** `immagine_nello_schizzo` calls the replacement with HighestResolution
  false, which is what a part gets anyway (the page's Remarks). `probe_sketch_picture2`:
  a drop-in on the 400 x 200 PNG (P1 = P0: pixel map 400 x 200, 120 x 60 mm at
  (10, 20), first row 119.7 mm), and on the 4096 x 2048 PNG the same 2048 x 1024
  pixel map as the obsolete call (true is ignored in a part too, P2 = P1), so the
  file-pixel readback of M11 added in library 0.142.0 stays as it is. The refusal on
  a null return names the new method; its text is otherwise unchanged. Frozen key
  removed: 1. No answer changes. Cases that must stay identical:
  `piano/la_foto_sta_in_scala` (0.3 mm per pixel, 119.7 mm check) and
  `piano/a_picture_over_2048_px_keeps_the_scale_of_its_file` (0.1 mm per file pixel,
  SOLIDWORKS pixels 2048 x 1024).
- **L6-3 / S3 / M44 (features), suppressed or not: `IFeature.IsSuppressed` ->
  `IsSuppressed2(swThisConfiguration, null)[0]`.** One helper,
  `ApiHelpers.IsSuppressedHere`, now answers for `sopprimi_lavorazione` (already
  suppressed or active?), the «soppressa» flag of the document state
  (`StatoDocumento`) and the rib's retreat (`NervaturaRitira`, a sketch that can only
  be suppressed). `probe_is_suppressed2`, four configurations and a cut suppressed in
  one: `(swThisConfiguration, null)` is a Boolean[1] equal to `IsSuppressed()` with
  either configuration active ({False}, then {True}); `(swAllConfiguration, null)`
  gives one Boolean per configuration in `GetConfigurationNames` order (not used). An
  answer of another shape throws, and the call sites keep their catch. Frozen keys
  removed: 2 (`OperazioniSw`, `StatoDocumento`); the undecided entry
  `OperazioniNervatura.cs|IsSuppressed` goes too. The component reads
  (`IComponent2.IsSuppressed`) are not obsolete and stay. No answer changes. Cases
  that must stay identical: `togliere/sopprimi_il_terzo_foro`,
  `togliere/lim_sopprimere_due_volte`,
  `lavorazioni/lim_nervatura_sulla_piastra_senza_pareti`.
- **S2 / L17-3 (part readings), mass properties: `CreateMassProperty` ->
  `CreateMassProperty2`.** New `ApiHelpers.NewPartMassProperty`: the selection
  cleared, `CreateMassProperty2`, system units, `IncludeHiddenBodiesOrComponents`
  true, the bodies as a typed `Body2[]` in `SelectedItems`, `Recalculate` always, the
  bodies read back from `SelectedItems`. Used by `proprieta_di_massa` on a part, by
  the per-part masses that check the assembly mass (`MassaDelPezzo`), and by the
  bench's volume reader (`Banco/PortaDelCliente.cs`). `probe_mass_property2` on a
  three-body AISI 304 part (16000 + 4000 + 1000 mm3, the last hidden): nothing
  selected 21000 (N1 = M0), {A} without Recalculate still 21000 (N4), with it 16000
  (N5 = M1), the hidden {C} 1000 (N7 = M2), {A, B, C} 21000 (N8 = M3), density 8000
  throughout; the old order trap (density 1 after Volume) is absent (O1). Kept on the
  obsolete call, with the reason in `WhyFrozen`: the ASSEMBLY mass (components not
  probed) and the document STATE's mass field, because the new call measures what is
  selected and the state reads the designer's selection after the mass: clearing it
  there would erase the state's selection field, while left in place a pre-selected
  body is measured alone (N2/N3: 16000 mm3 instead of 21000); whether `SelectedItems`
  replaces a pre-selection was not probed. Frozen key removed: 1 (`Banco/PortaDelCliente.cs`). No
  answer changes. Cases that must stay identical: `misura/massa_di_un_cubo_di_acciaio`
  (200000 mm3, 1.6 kg, centre [0, 0, 10]), `misura/massa_senza_materiale_non_si_sa`,
  `proprieta/the_material_reaches_every_configuration` (7800, 1.56 kg),
  `assiemi/l_assieme_si_pesa_si_misura_si_manda` (1.0675 kg, sum of the parts
  1.0675), `assiemi/due_basi_uguali_uno_step`, and the bench's client-door group.
- **L9-3, the cosmetic weld bead: `InsertCosmeticWeldBead` -> `InsertCosmeticWeldBead2`
  with the marks 4 / 8.** `cordone_saldatura` selects the from face with
  `SelectionMarks.WeldBeadFrom` (4) and the to face with `WeldBeadTo` (8) on a
  SelectData, keeps the selection, and calls `InsertCosmeticWeldBead2(weld geometry,
  Face2[]{from}, Face2[]{to}, size)`: case W1 of the Z-C21.3 weld bead probe,
  decision A. On the trimmed T of the bench case W1 (and W2, the guide's example
  literally) made one bead, BeadSize 4 mm, 68 mm long, the same as the obsolete call
  (W0); without the marks (W4), with the selection cleared (W3) or with no selection
  (W5, the call of 25 September) there is no bead: the "null in every combination" of
  25 September was the missing marks. The return is an Object[1], on which `as
  IFeature` is null, so the new bead is found in the tree as the probe counted it:
  the beads by name before and after, sub-features included (it lives in the weld
  folder); two new beads are a refusal. This was an OBSOLETE-MEASURED entry, not a
  frozen one: the Measured entry goes. `SelectionMarks.WeldBeadFrom/To` lose their
  DIVERGES note. No answer changes. Case that must stay identical:
  `saldature/il_cordone_va_fra_facce_che_si_toccano` (68 mm, side read back 4,
  volume 1264161.94 unchanged, the two refusals before it).
- **S1, reading a dimension through `GetSystemValue3`: migrated, benched, WITHDRAWN.**
  The migration moved `ApiHelpers.ReadDimension` to `GetSystemValue3(swThisConfiguration,
  null)[0]` and made every drawing caller read in the configuration of its view
  (`GetSystemValue3(swSpecifyConfiguration, {view configuration})[0]`), as
  `probe_dimension_value3` measured for MODEL dimensions shown in a drawing. The
  dimensions the DRAWING owns (AddDimension, AddChamferDim, between silhouettes) had not
  been probed, and the bench families `tavola` and `quote` on 0.143.0 (29/9/2026) gave 12
  failures and 8 bench errors: "no dimension was born" on the turned parts, `quote_mm`
  without 120 or 10 in every `quota_in_tavola` case, no outside diameters read back.
  The dimensions were made and then discarded by the readback. The commit is reverted:
  the readings are back on `SystemValue` / `GetSystemValue2`, the two keys are frozen
  again, and `WhyFrozen` carries this measure. What stays true from the probe: in a
  drawing `GetSystemValue3(This, null)` reads the model's active configuration, not the
  view's, so a future migration must be probed on drawing-owned dimensions first.

---

## 0.118.0 — 29 September 2026 · library 0.142.0

**Sixteen findings of the Z-C21 audit fixed with the remedy the Z-C21.3 probes
measured on 28 September 2026.** Every finding below was measured REAL by a probe
first; each fix follows the call that the probe showed working, with its numbers,
and cites its page of the API guide in the code. **The bench has not run on this
branch yet**: the red-before and green-after of the new cases come from the probe
numbers and from reasoning, not from a bench run. **Full bench on library 0.142.0,
29/9/2026: 1932 tests, 1 failed**, and not in these fixes: `le_croci_di_centro_sui_fori`
step 7 counted 0 centre marks instead of 3 right after `viste_standard`. It was
green in the three full runs before and green again alone on the same library,
and neither `viste_standard` nor the counter had changed. `viste_standard` now
rebuilds the drawing before answering and reports `centre_marks_after_rebuild`.
The green full bench of this entry is the one on 0.143.0 (entry 0.119.0), which
contains it.

Drawing (`OperazioniTavola.cs`, `StatoTavola.cs`):

- **M2, duplicate dimensions from the model.** Both `InsertModelAnnotations3` calls
  of `quote_dal_modello` passed DuplicateDims false, which the guide defines as
  "allow" duplicates, under a comment that said the opposite. Measured on the
  bench plate (120 x 80 x 10, one hole, three views): false gave 7 dimensions with
  3 of them twice, true gave 4 with none twice. Now true. Bench case
  `quote/le_quote_arrivano_dal_modello.json` updated: 4 dimensions instead of 7.
  The obsolete `InsertModelAnnotations2` fallback stays false on purpose: its own
  page gives the flag the opposite meaning, and it was not measured.
- **N9, unmarked dimensions came in through the fallbacks.** Without
  «tutte_le_quote» the first masks asked only MARKED dimensions, but the v2
  fallback passed AllTypes true, `InsertModelDimensions(0)` took every dimension
  of the view, and the view-by-view fallback asked NOT-MARKED too. Measured on a
  part with nothing marked: 7 unmarked dimensions came in and the operation said
  «done»; the view-by-view fallback with MARKED only gave 0. Now every step stops
  at the MARKED masks unless «tutte_le_quote» is true, and a part that has
  dimensions but none marked is refused with the way out. No bench case can build
  a part with nothing marked: contract group `Zc212DrawingContract` (6 checks).
- **N3, a turned diameter counted as an orphan.** A dimension that only
  silhouette edges hold (attached type 46) cannot be checked with IsSame: the
  probe got 0 hits at every step, and silhouette against silhouette answers Same
  for every pair. Such a dimension is no longer counted in the orphans but in the
  new state fields `dimensions_not_verifiable` (per view) and
  `dimensions_not_verifiable_total`. Bench case
  `tavola/the_turned_diameter_is_not_an_orphan.json` (a plain cylinder: 0 orphans,
  1 not verifiable).

Sketch, measure and picture (`OperazioniSw.cs`, `OperazioniMisura.cs`,
`OperazioniRicalco.cs`):

- **M5, the profile sketch was reported locked when it was not.** `AddRelation`
  FIXED on a closed sketch with an object[] returned null and added 0 relations,
  and the bridge answered locked. Measured remedy: EditSketch, the segments as a
  typed SketchSegment[], leave the sketch, rebuild, count the FIXED relations
  again (0 to 6 on the L profile, volume unchanged). The sketch is reported locked
  only when every segment has its relation; the answer carries `fixed_relations`
  and, otherwise, `sketch_lock_note`. Bench case
  `estrusioni/the_l_profile_sketch_is_locked_for_real.json`. The typed array was
  measured on straight lines only: on splines, circles or the revolve centreline a
  shortfall is now reported, not hidden.
- **M29, the distance between arcs was always centre to centre.** IMeasure.ArcOption
  was never set: between two holes D10 and D20 at 50 mm the probe read 50 (centre),
  35 (minimum) and 65 (maximum). `misura_fra_entita` takes `arc_measure` (centre,
  the default and the old answer; minimum; maximum), sets it and ProjectionOption
  before Calculate, reads it back and publishes it; another value is refused.
  Bench case `misura/the_wall_between_two_holes_is_measured_as_asked.json`.
- **M11, a picture over 2048 px got the wrong scale.** SOLIDWORKS holds a
  4096 x 2048 picture as 2048 x 1024 in a part, with or without
  HighestResolution, so the scale per pixel was twice the true one and the
  bridge's own check passed. The pixel size is now read from the file header; the
  map SOLIDWORKS holds is published beside it (`solidworks_pixels`,
  `compression`), and a map larger than the file, of another shape, or a header
  that cannot be read at 2048 px or more, is refused. Bench case
  `piano/a_picture_over_2048_px_keeps_the_scale_of_its_file.json` with the data
  file `Banco/dati/trace_4096x2048.png`.

Assemblies (`OperazioniSpecchiaComponente.cs`, `OperazioniMisuraAssieme.cs`,
`OperazioniDistintaBase.cs`, `OperazioniDipendenze.cs`):

- **N26, the orientation of a mirrored instance was never set nor checked.** The
  probe measured four different axis sets for the four orientations on the Right
  plane, with the same centre, so the centre check could not tell them apart.
  `specchia_componente` takes `orientation`, sets it with a typed
  swMirrorComponentOrientation2_e[] and checks the axes of the new instance on
  Transform2; a mismatch is refused and the mirror removed. The answer carries
  `orientation` and `new_axes`. Bench case
  `assiemi/the_mirrored_instance_turns_as_asked.json`; its Top and Front steps
  extend the axis rule beyond the Right plane the probe measured, so a red there
  after the fix means the rule is wrong for those planes. Contract checks in
  `AssembliesZc212terContract`.
- **C4-2, the STEP solid count ignored bodies cut in the assembly.** Measured:
  STEP files with 1, 2, 3 and 2 solids where the old count said 1, 1, 2 and 2, so
  a STEP missing the cut instance would have passed. An instance with user bodies
  (GetBodies3 BodiesInfo) now counts all its bodies. No bench operation makes an
  assembly cut: contract checks.
- **N27, the BOM check refused right tables.** The assembly-side count ignored
  ChildComponentDisplayInBOM: a Promote sub-assembly in a top-level table and a
  Hide sub-assembly in a parts-only table were refused. The count now follows it
  (8 probe tables reproduced; the indented table, not measured, keeps the old
  count). No bench operation sets the option: contract checks.
- **L12-8, a missing file could be hidden by a file with the same name.**
  GetDependencies with Searchflag true answered a same-name file found in another
  folder, which exists, and once it had, later calls with false answered it too.
  Now Searchflag false only, and the disk decides what is missing. The bench
  cannot rename a component file: a source check in the contracts.

Weldments and configurations (`OperazioniMateriale.cs`, `OperazioniSaldatura.cs`):

- **M10, the material reached only some configurations.** One
  SetMaterialPropertyName2 with the empty name reached the active configuration and
  its derived ones, not the others (density 1000 instead of 7800 on the plain
  one). `applica_materiale` now sets and reads back every configuration by name,
  refuses naming any that did not take it, and lists them in `configurations`.
  Bench case `proprieta/the_material_reaches_every_configuration.json`.
- **N25, the corner treatment and the empty profile size.** A member group without
  a corner treatment was born with none (the probe read ApplyCornerTreatment
  false): the miter is now the default, applied and declared
  (`corner_treatment_note`), and the trial frame matches the real one. An empty
  profile size built the size the file was saved on (20 x 20 of four): it is now
  accepted only on a one-size file (`configuration_note`) and refused otherwise,
  listing the sizes. Bench cases `saldature/a_member_group_is_mitred_by_default.json`
  and `saldature/an_empty_profile_size_names_the_sizes.json` (tied to the Italian
  install's profile file, the one the probe measured); the one-size acceptance in
  `WeldmentConfigurationZc212terContract` (4 checks). Risk for the first bench
  run: the miter default now also applies to single-bar groups, measured on an L
  only.
- **L9-4, the trim did not extend a short bar.** Options 8 alone gave a phantom
  feature and no change on a bar 20 mm short; 1|2|8 = 11 extends it (+29399.11
  mm3) and cuts a plain crossing exactly as 8 did. Bench case
  `saldature/the_trim_extends_a_short_bar.json`.

Document state (`StatoDocumento.cs`):

- **C7-2 and N14, fillet and chamfer edge counts.** The fillet count came from
  FilletItemsCount, which is -1 on uniform fillets, so it never appeared. The state
  now writes `selected_edges` (GetEdgeCount, the selection) and `faces`
  (IFeature.GetFaceCount), for fillets and chamfers: one selected edge that became
  a whole loop reads 1 and 8. Bench case
  `lavorazioni/the_state_counts_selected_edges_and_faces.json`. The replies of
  `raccordo_spigoli` and `smusso_spigoli` (added in review) keep `spigoli`, the
  edges passed, and carry the same `selected_edges` and `faces` (IFeature.GetFaceCount
  of the new feature); the same bench case asserts them.
- **N29, a vertex chamfer read as nothing.** The chamfer is now read by
  IChamferFeatureData2.Type: a vertex chamfer gives `vertex_distances_mm` (probe
  2/3/4 and 3/3/3, where the state gave {}). The read moved from the obsolete
  IChamferFeatureData, so the obsolete ratchet goes down by two entries. No bench
  operation makes a vertex chamfer: contract group `StateChamferContract` (4 checks).

Not fixed in this lot, as measured: **C3-5** (framing on GetVisibleBox cut 13 of 24
shots against 3 for the current framing; a real fix needs SOLIDWORKS to re-measure
the margin), **M2 on the v2 fallback** (its page says the opposite of v3; needs its
own probe or the v2-to-v3 migration), **N27 for mass and STEP parts** (the probe
found them not real or undecided).

Catalogue: `quote_dal_modello` (the stale «nothing comes in out of process»
paragraphs replaced by the measured behaviour, duplicates, the marked default and
its refusal), `misura_fra_entita` (`arc_measure`), `immagine_nello_schizzo`,
`specchia_componente` (`orientation`), `esporta_step`, `distinta_base_in_tavola`,
`dipendenze`, `applica_materiale`, `membro_strutturale`, `telaio_rettangolare`,
`taglia_membri`, `raccordo_spigoli`, `smusso_spigoli`, and the four sketch-based
operations (the lock flag, `fixed_relations`, `sketch_lock_note`).

Counts: contracts 498 to 523 (four new groups: 4 + 6 + 11 + 4); bench cases +10
(one each in `casi/assiemi`, `casi/estrusioni`, `casi/lavorazioni`, `casi/misura`,
`casi/piano`, `casi/proprieta`, `casi/tavola`, three in `casi/saldature`), one case
changed in `casi/quote`; the floor of refusals with a way out 216
to 217 (a detector match on the helper that removes the mirror, not a better
message); obsolete ratchet two entries lower. Language gate: nothing grew.

## 0.117.0 — 28 September 2026 · library 0.141.0 (unchanged)

**The language gate: the Italian in the repository can only go down (entry
W3.1-bis.1).** Decided on 28 September 2026: the whole repository goes to English
before the first release, names included (D1) and historical entries of this file
included (D2). This entry builds the ratchet that keeps the Italian from growing
while the translation goes on, measures it before trusting it, and was then
reviewed adversarially: the review opened the ratchet without a signature in three
ways and found eight ways past the detector. All of them are closed below, each
with a contract check that was seen red.

- `Controlli/lingua/detect-italian.js` reads every file of `git ls-files` (or of a
  commit, `--rev`, from the object store) and skips binaries **by extension only**,
  from a closed list: never `git grep -I`, never git's own guess. Nine categories,
  never summed. Comments, strings, markdown, JSON values and transition count
  **Italian words per file** and may not grow in any file: Italian appended to a
  line that is already Italian is growth too. Identifiers, JSON keys, protocol
  values and path segments are repository-wide sets of whole names: no new Italian
  name, so an identifier glued from Italian words that exist one by one is blocked,
  and so is a name glued with hyphens (a JSON key with spaces is one whole key). A
  name that was a tracked path segment is not new when it shows up in text after
  the file is renamed. Transition must be 0 at a `v*` tag.
- What the detector reads now, after the review: the holes of interpolated strings
  (a string inside `{...}` or `${...}` is a string, a name is a name); prose after a
  selector or a placeholder (`{name} not found...` is prose, the placeholder a
  name); the words of `echo`/`printf` and the body of a here-document in shell
  scripts; the typographic apostrophe as the accent; workshop words the repository
  does not use yet (a list in `lexicon.json`, and the endings `-atura`, `-trice`,
  `-one`, `-ella`, `-etto` for names). Italian words that English also has (a
  musical term, an abbreviation, a brand: `weak_words` in `lexicon.json`) count
  only in pairs, so an English line with one of them stays English.
- `exceptions.json`: patterns with class, reason, signature and date, from a closed
  list of classes; never a whole file. Refused now also: a `json_path` outside the
  class detector-lexicon or outside one file of `Controlli/lingua/`; a pattern that
  matches an Italian function word on its own, or takes away a third of the Italian
  of one of five sentinel sentences; a pattern without `file` that matches an
  Italian content word; and an exception that, on the tree, suppresses more than 40
  hits in a file, more than the Italian left in it (from 10 up), or more than 300 in
  the repository (`exception_limits` in `lexicon.json`).
- **The CI step judges every commit of the range against its own parent**
  (`.github/passi/language-gate.ps1`): (a) the tree is within its own baseline;
  (b) a change to the gate itself (detector, lexicon, exceptions, the step, the
  workflows, CODEOWNERS) carries the trailer `Language-Gate-Config-Change:`;
  (c) the baseline is not above the parent's; (d) the tree is not above the
  parent's baseline, **measured with the parent's detector, lexicon and
  exceptions**. (c) and (d) pass only with the trailer
  `Language-Baseline-Raise: <reason>` on **that** commit: a signature no longer
  covers the range. Trailers are read by git, so the words in the middle of a body
  do not sign. Renames are followed (`git diff -M`), a merge is judged against the
  union of its parents' baselines, and on a pull request the base is the first
  parent of the merge commit. `.github/workflows/language-gate.yml` runs the step
  again on `pull_request_target`, where the workflow and the script are the target
  branch's: a pull request cannot switch the gate off by editing it.
  `.github/CODEOWNERS` covers `.github/`, `Controlli/lingua/` and
  `Controlli/ganci/`. `prove.yml` runs on `v*` tags too, and a manual run takes a
  `base` input.
- **Pre-push**: the same step on the commits being pushed (read from git, so work
  not yet committed changes nothing), then `--tighten` on each pushed tip. It stops
  when the detector or the step is missing, instead of skipping in silence.
- `baseline.json`, format 2, written on the MERGED tree (main at library 0.141.0
  plus this entry), 674 files read: Italian words comments 147,940, strings
  56,071, markdown 33,983, JSON values 55,845; distinct names identifiers 4,808,
  JSON keys 443, protocol values 2,170, path segments 640; transition 0. **Raise
  reason** (the commit carries `Language-Baseline-Raise:`): the branch baseline
  had been measured on `4d7b84a`, before 0.137.0-0.141.0 landed on main with the
  Italian they were written in; this is the first baseline, not a growth. The
  branch history (eight commits, kept locally as `w31bis-1-cancello-storia`) is
  landed as ONE commit, because the CI step judges every commit against its own
  baseline and the old commits carried the old one.
- **The measure** (`node Controlli/lingua/measure.js --summary`), four draws from
  the tree of `4d7b84a`, labels stored as hashes. The first two (810 lines, and 120
  English-heavy ones) were labelled while the detector was written and helped tune
  it: precision 1.000, recall 0.997, 0 false alarms in 105. **The numbers to quote
  are the two new draws, labelled blind** (from `--next`, before the detector ran on
  them), after the review: 930 lines at random (seed 5150), 820 Italian and 110
  English, **precision 1.000 (balanced 1:1) and recall 0.985**; 200 lines with two
  English function words (seed 4747), **0 false alarms on 166 lines of English
  prose**. Pooled over the blind draws: 0 false alarms in 279 English lines, so
  with 95% confidence the false-alarm rate is below 1.1% and the balanced
  precision above 0.989. Six labels were typing slips (a letter shifted by one
  line), found by reading every label back against the notes, not only the
  disagreements. Of the 12 Italian lines missed, 5 only name an existing file (by
  design: counted once, under path segments) and 7 use words the lexicon lacks, or
  one homograph alone (`--summary` lists them): left as measured.
  808 found lines split as 473 counted (blocked wherever the words are added) and
  335 found through names only (blocked only when the name is new). Out of the
  repository: 41 workshop terms the repository never uses, kept out of every list
  the detector loads, 29 caught in a new name by their ending alone; the Vim user
  manual (9,796 English lines never seen): 3 false alarms.
- Contract `LanguageGateTests`, 50 checks: the nine categories, the glued
  identifier, the Italian folder and the file with a NUL byte as before, plus one
  injection for each hole of the review and English lines with one homograph each
  (must not count); four ways to cover a file with an exception (refused); and the
  CI step on a throw-away repository with commits: a growth signed on a later
  commit, a signature on the wrong commit, a loosened lexicon, an exception for the
  exact sentence, a trailer in the body (all refused), a signed raise, a rename, a
  JSON key with spaces, a merge (all accepted), the step switched off and judged by
  the base's copy, a release tag with transition left (refused). **Every new check
  seen red**: `node Controlli/lingua/see-it-fail.js` switches off each repair in
  turn (20 mutations of the detector, the lexicon and the step) and runs the suite.
- `Controlli/lingua/ci-proof.ps1`, the proof on GitHub, still to run: six
  throw-away branches (an Italian line; the same with the baseline raised and no
  trailer; the same, signed; the Italian line and a raise signed on the next
  commit; a loosened lexicon; a rename), and the step must fail, fail, pass, fail,
  fail, pass.
- `Controlli/domande-senza-risposta.js`: the real NUL byte (offset 3899, line 97, a
  key separator) is now written `'\u0000'`. Same value, and the same output byte
  for byte with the same exit code on the same register, with and without its
  line-by-line option. Git called the file binary and `controlla-pubblicabilita.ps1`
  (`git grep -I`) still skips files like it: a separate entry, not language.
- README: the section that kept the names Italian as protocol is replaced by an
  English note (D1), and so is the matching sentence in
  `Controlli/rinomina-prodotto.js`.

- **`controlla-pubblicabilita.ps1` reads the files git takes for binary**
  (entry Z-C22). The history pass used `git grep -I`, which skips every blob
  git guesses is binary: a text file with one NUL byte is one, and
  `Controlli/domande-senza-risposta.js` (a real NUL at offset 3899) was never
  looked at. It is now `git grep --text`, with the binaries dropped by
  extension (the same `$binari` list as the working-tree pass). Seen red on a
  throw-away clone: a `.js` with a NUL and a private address in a commit
  later deleted — the old script says "ok", the new one names the file.

Bridge version 0.117.0; the library is unchanged (0.141.0): nothing here runs
inside SOLIDWORKS, so the full bench of 0.141.0 still stands.

## 0.116.0 — 28 September 2026 · library 0.141.0

**Silent defects of the API audit, second round (TODO entry Z-C21.10-esito-2).**
Every fix below is a case where a part, a file or an answer came out wrong
with `riuscita: true`, decided from the SOLIDWORKS API help (quoted in the
code) or from the library's own logic. The new bench cases are written to go
red on library 0.140.0 and green on this one, except where a bullet says
otherwise. **Full bench on library 0.141.0, 28/9/2026: 1832 tests, 0 failed**
(second run; the first one found the three points listed at the end of this
entry). Whether the new cases were RED on 0.140.0 has not been run yet
(rule Z-C11).

- **`telaio_rettangolare` / `membro_strutturale` close the weldment profile
  library again, and check that it closed** (N31). ISldWorks~CloseDoc.html:
  "void CloseDoc(System.string Name)" returns nothing, and "the name of a
  document that is not open, then this method does nothing"; the library is
  now looked for again by path and, if still open, its IModelDoc2~GetSaveFlag
  ("True if this document needs to be saved") decides. The close runs on every
  exit of `membro_strutturale` (try/finally) and after the trial frame of
  `SezioneProfilo`, which the bench's «sezioni» group calls directly. A library
  left open and modified is reported in `profile_library_warning`. Case:
  `saldature/a_frame_too_small_closes_the_profile_library` (red on 0.140.0
  only on a fresh archive key; whether CloseDoc of the trial part already
  closes the library as a "non-active hidden document" is still to be probed).
- **`telaio_rettangolare` reads «misure» whatever the case, and refuses any
  other word** (N32). «Ingombro» used to fall into the centre-line branch and
  build a 1080 x 880 frame for 1000 x 800. The answer carries `misure`. Case:
  `saldature/frame_reading_ignores_case` (the frame and its refused twin).
- **The overhang archive of `SezioneProfilo` no longer keeps a failure or a
  stale number** (N36). The key now carries the library file's date and size
  (`frame-v2|...`), old-format lines are dropped on load, a failed trial
  measurement is not remembered, and a frame built from a remembered overhang
  that fails the outside-size check drops that entry and says so in the
  rejection. The answer carries `overhang_source` («measured» / «archive»).
  No API call changed. No bench case can build it: contract group «the pure
  rules of the Z-C21.10 fixes the bench cannot reach».
- **The bridge hands every enum value on in the catalogue's spelling, and the
  step goes on under the catalogue's operation name** (S20, `Ponte/Forma.cs`,
  `Strumenti/Attrezzi.cs`). The Forma accepted «Superiore» ignoring case and
  passed it on untouched to a library that compares Ordinal. Nested objects
  are covered; the rewrite walks a copy of the entries. Contract group «the
  Forma rewrites enum values in the catalogue's spelling» (12 checks, 7 red on
  the 0.140.0 Forma).
- **An unknown plane name is refused, never the front plane** (N33,
  `SelezionaPiano`). IFeature~Select2.html: "True if the feature was selected,
  false if not"; the fallback now returns false like `TrovaPianoBase`, and
  every caller turns it into a refusal. Case:
  `estrusioni/unknown_plane_does_not_fall_back_to_front`.
- **`verifica_interferenze` checks the list against the count** (N34).
  IInterferenceDetectionMgr~GetInterferenceCount.html: "Call this method
  before calling IInterferenceDetectionMgr::IGetInterferences to determine the
  size of the array for the interferences". A missing or short array, or an
  item that is not an IInterference, leaves `volume_totale_mm3` out and says
  why in `cosa_non_so`; a count of 0 with interferences in the list is refused.
  New `interferences_read`. Case:
  `assiemi/the_interference_array_is_as_long_as_the_count` (not red on 0.140.0:
  it stops on the missing field; it pins array length = count on SW 2021).
- **`verifica_geometria_corpo` with `corpo_uid` speaks for that body only**
  (N61). IPartDoc~GetBodies2.html: "false gets all of the bodies in the part".
  `bodies_in_part` always counts every body, `verdict_about` says «part» or
  «body Cn», and a sound body of a multibody part no longer gets the sentence
  "si puo' esportare". Case: `verifica/one_body_of_two_is_not_the_part`.
- **`esporta_iges` turns off the three toggles that add curves or turn the
  solid into curves, and counts the solids in the file** (N59).
  FileSaveAsIGESOptions.htm: swIGESExportAsWireframe "Specifies whether to
  convert solid body to a 3D wireframe representation in the IGES file",
  swIGESExportFreeCurves, swIGESExportSketchEntities. They are read, set off
  and restored like the other exchange preferences (only the ones actually
  read are restored). The IGES directory section is counted: `entities`,
  `solid_entities` (type 186), `surface_entities`; a file with neither is
  refused. Case: `esporta/the_iges_carries_the_solid_not_its_wireframe` (not
  red on 0.140.0: it needs a user profile with the toggles on; it pins 1 solid
  and 6 surfaces for the 100 x 100 x 20 block).
- **`foro_guidato` with an anchor checks centred axes and the widest cut**
  (N50). Centred axes were skipped; now half the gap between the two edges
  must hold the radius, and the radius is the counterbore or countersink for
  «lamato» / «svasato» (IWizardHoleFeatureData2~CounterBoreDiameter.html:
  "This property is relevant only for swCounterBored and swCounterBoredDrilled
  holes"). The check runs before the anchor. Cases:
  `lavorazioni/guided_hole_centred_too_narrow`,
  `lavorazioni/guided_hole_counterbore_past_the_edge`.
- **The anchor's movement test no longer passes without testing** (N51): a
  distance axis without its dimension, a dimension that cannot be read, or a
  rebuild state that cannot be read (IFeature~GetErrorCode2.html) is a refusal,
  not a pass.
- **The anchor sketch's relations must be readable** (N52):
  ISketchRelationManager~GetRelationsCount.html ("Number of sketch relations");
  a null manager, a throwing or negative count is a refusal, not "healthy".
- **The slot's four tangencies and equal radii are counted before the centre
  line** (N54): IModelDoc2~SketchAddConstraints.html returns nothing, so the
  count is the only proof, and the coincidences no longer hide two missing
  tangencies. The answer carries `slot_relations`.
- **The oblique slot's angle dimension is checked** (N55).
  IModelDocExtension~AddSpecificDimension.html: "Error Result status as
  defined in swAddSpecificDimension_e"; it must be Success, and the angle read
  back must be the angle between the slot and the reference edge, or its
  supplement. N51, N52, N54, N55 have no bench case (the bench cannot make the
  API fail there); every anchored case is their regression guard, and the pure
  angle rule is in the contract group.
- **`quota_in_tavola` reads «already dimensioned» from every sheet** (N43).
  IDrawingDoc~GetFirstView.html: "Gets the first drawing view on the current
  sheet"; the views now come from IDrawingDoc.GetViews, with the current sheet
  as fallback, said in `already_dimensioned_read_from`. Only views of the same
  model and configuration count (`views_of_other_models_not_counted`). New
  helper file `ApiHelpers.Drawing.cs`. Case:
  `tavola/the_second_sheet_does_not_repeat_the_dimensions`.
- **The «4×» of a hole callout is read back** (N48). IDisplayDimension~SetText.html:
  "NOTE: This method does not support hole callouts."; the prefix is read
  again into `fori[].prefix_read`, and a count that did not stick is a failure.
  Case: `tavola/la_tavola_quota_fori_e_smusso` (new expectation).
- **A dimension hidden by hand does not count as already dimensioned** (C8-2).
  IAnnotation~Visible.html; swAnnotationHidden and swAnnotationHalfHidden are
  skipped and counted in `hidden_dimensions_not_counted`. A hidden layer or a
  suppressed owner feature is not seen ("This property cannot determine..."),
  still open. Case written and parked in
  `Banco/casi-in-attesa/a_dimension_hidden_by_hand_does_not_count.json` until
  the runner field `prima_nascondi_quote` exists.
- **Contracts**: `ProveILimiti` accepts the English stems «refus» / «reject» as
  a declared limit; the undecided-call count of `OperazioniQuoteTavola.cs|Add`
  goes from 26 to 27 (the new `falliti.Add` of N48: `falliti` is a
  `List<string>` parameter, which the scanner does not type); new group
  `Zc2110HelpersContract` (N32, N34, N36, N43, N55, C8-2 by reflection).
- **From the full bench on 0.141.0 (first run):**
  - **`nervatura` says what it could not remove.** When no rib is born in
    either direction (a plate with no walls), the rib sketch cannot be
    deleted afterwards: measured by `Controlli/NervaturaRitira` on SOLIDWORKS
    29.5.1. DeleteSelection2 answers false with every option, EditDelete and
    three EditUndo2 leave it, a control sketch deletes normally, and
    EditSuppress2 works. The sketch is now suppressed and named in the refusal
    ("resta nell'albero, SOPPRESSO"), and the volume check still says com'era.
    Before, the refusal declared a restore that had failed.
  - **Two bench cases corrected, no library change.**
    `guided_hole_counterbore_past_the_edge` step 3 selects the plate face with
    `z~=20`: after the first counterbore there are two front faces.
    `l_albero_a_gradini_quotato_di_fianco` no longer demands that the Ø20 be
    unreachable: with SOLIDWORKS freshly restarted it was exposed and
    dimensioned ([34, 30, 20]), with no change to the shaft code. Both
    outcomes are honest; Ø30 and Ø34 are still required to be dimensioned.

## 0.115.0 — 28 September 2026 · library 0.140.0

**Four operations that never worked, rebuilt from the SOLIDWORKS API help.**
Each one had a history of NULL returns; each time the help page named the
missing piece (an obsolete method, a selection mark, a flag, a degenerate
profile). The recipe of the help was measured first by a probe, then written
into the library together with its bench case, with the expected numbers
computed by hand and compared with the probe. The help page is quoted in the
code. Three operations are new (`raccordo_faccia`, `ripeti_riempimento`,
`nervatura`) and one comes back into the catalogue (`fazzoletto`): the
catalogue goes from 95 to 99 operations, all 99 executable ones. New answer
fields are in English. **Full bench on library 0.140.0, 28/9/2026: 1753 tests,
2 failed**, both in the new rib cases and neither in the recipe: a refusal
without «com'era», and a shell volume written without tolerance, which kept
the rib step behind it from running. Corrected, and seen green on the full
bench on 0.141.0 (entry 0.116.0), together with the locked rib sketch found
there.

- **The face fillet (entry GP7, the half that was missing).** New operation
  `raccordo_faccia` (`faccia_1_uid`, `faccia_2_uid`, `raggio_mm`): a fillet
  that rests on two faces instead of an edge. Four earlier attempts returned
  NULL, and the API help named both causes: `FeatureFillet3` is obsolete for
  face fillets since SOLIDWORKS 2020 (its Remarks), and the selection marks
  are 2 and 4 (`ISimpleFilletFeatureData2` Remarks, "Face fillet/face-face
  chamfer") — not the 1/2 of `swSimpleFilletWhichFaces_e`, which is the
  parameter of `SetFaces`. Measured first by the probe (40 mm cube, two
  adjacent faces, r = 5): the guide's recipe (`CreateDefinition(swFmFillet)`,
  `Initialize(swFaceFillet)`, marks 2/4, circular profile, `DefaultRadius`,
  `CreateFeature`) removes 214.6 mm³, exactly 40·25·(1 − π/4); the old call
  and the new one with marks 1/2 both give NULL. The same face twice, and two
  flat parallel faces (same side, or back to back), are refused before
  calling: no ball touches both. Two walls facing each other are tried only
  with the radius equal to half their distance. Afterwards the new feature
  must be a `Fillet`, the volume must change, bodies and features in error
  must be as before; otherwise the fillet is removed (`DeleteSelection2`) and
  the part is checked to be as it was. Where the hand calculation exists (two
  flat faces at 90 degrees sharing an edge) it is read BEFORE the fillet —
  afterwards the two faces no longer touch — and a volume change that differs
  from it by more than 0.05 mm³ (or 1e-4 of it) removes the fillet and refuses,
  naming `raccordo_spigoli`. New fields: `volume_change_mm3`,
  `material_removed`, `shared_edge_mm`, `expected_change_abs_mm3`,
  `features_in_error`, `bodies`, `feature_type`, and `declared_radius_mm`
  (read back from `ISimpleFilletFeatureData2.DefaultRadius`; the inputs are
  not echoed). Bench: `lavorazioni/raccordo_faccia_fra_due_facce_di_un_cubo`
  (volume and centre of mass, which tells which edge was filleted), and the
  two refusals `lim_raccordo_faccia_sulla_stessa_faccia`,
  `lim_raccordo_faccia_fra_facce_che_si_danno_le_spalle`.
- **`ripeti_riempimento`, the fill pattern (entry W1.1).** The recipe of
  IFillPatternFeatureData (Remarks) and Create_Fill_Pattern_Example_CSharp.htm:
  pre-select the feature (mark 4), a direction edge (mark 1) and the boundary
  face (mark 16384), then `CreateDefinition(swFmFillPattern)` with scalars
  only, then `CreateFeature`. `FeatureFillPattern` is "Obsolete" in the help,
  and so were the fifteen NULLs of GP14. The probe of 28/9/2026 measured 14
  holes on a 100 x 60 x 12 plate (-8444.6 mm³ = 14 x 603.2, exact); without
  the direction edge `CreateFeature` returns NULL although the help calls it
  optional, so the operation refuses before selecting anything. Circular
  layout only (the one measured). It refuses a non-planar boundary and a
  direction that leaves the face, and removes the pattern (`DeleteSelection2`,
  then recounts the tree and re-measures the volume) when it breaks a
  feature, changes the body count or makes openings merge. New answer fields:
  `copies_made` (counted from the faces the pattern owns), `volume_per_copy_mm3`,
  `feature_type` (`APattern`), and `declared_layout`,
  `declared_loop_spacing_mm`, `declared_instances_per_loop`,
  `declared_margin_mm` read back from `IFillPatternFeatureData` (the layout by
  enum name), not echoed from the inputs. The direction edge is re-selected with
  `Select4` and a `SelectData` carrying mark 1, as the probe did; an axis from
  `crea_asse` is accepted but not measured, and the catalogue says so. The
  table pattern stays out: NULL without a coordinate system on mark 16. Bench:
  `ripeti/il_riempimento_fa_quattordici_fori` on the probe's own plate (Top
  plane, direction along the long edge of the boundary face: 14 is an
  algorithmic number and is compared only on the measured geometry) and three
  refusals twinned with it (`lim_riempimento_senza_direzione`,
  `lim_riempimento_contorno_non_piano`,
  `lim_riempimento_direzione_fuori_dalla_faccia`).
- **New operation `nervatura` (rib, entry W1.2)**, family `lavorazioni`.
  IFeatureManager~InsertRib.html and the only example in the help
  (Get_Rib_Feature_Data_Example_CSharp.htm) call `InsertRib` with
  `IsNormToSketch = false`: the rib grows in the sketch plane, from the line to
  the walls. Every historical probe passed `true`, which is why the rib "never
  worked". The material side depends on the shape (L bracket: straight; shelled
  box: flipped — measured by the probe of 28/9/2026), so the operation tries
  straight, then flipped, and keeps the first one in which a new `Rib` feature is
  born and the volume grows; `material_side` and `attempts` say which. Arguments:
  a base plane plus an optional `distanza_mm` (the offset plane is created by the
  operation), the two line ends in model coordinates, `spessore_mm` (two-sided).
  Refused, with nothing left in the part (sketch and plane removed with
  `DeleteSelection2`, volume read back): an end off the plane, a line under 1 mm,
  and a rib that is born in neither direction (a plate without walls — it names
  `estrudi_su_faccia`). Before the second attempt the tree must be as it was
  after the sketch: a failed attempt that leaves a feature of another type is
  refused (and removed) instead of built upon. New fields: `added_material_mm3`,
  `material_side`, `attempts`, `plane_created`, `declared_thickness_mm`,
  `declared_flip_side`, `declared_parallel_to_sketch`; the inputs come back as
  `requested_thickness_mm`, `requested_sketch_plane`,
  `requested_plane_offset_mm`, `requested_line_length_mm`, named as inputs. It carries a catalogue example copied from its green case.
  Bench: `lavorazioni/nervatura_nell_angolo_della_l` (+2700 mm³),
  `lavorazioni/nervatura_nella_scatola_svuotata` (+720 mm³, second attempt on
  the same part: the one step the probe did not measure), and two refusals with
  their twins (`lim_nervatura_sulla_piastra_senza_pareti`,
  `lim_nervatura_fuori_dal_suo_piano`). The plate refusal has the same
  arguments as its twin: the difference is the part, and it is declared (see
  the bench entry below) instead of carried by a dummy `distanza_mm: 0`.
- **`fazzoletto` (weldment gusset) is back in the catalogue, measured** (entry
  GP20). Until library 0.139.0 (this line said 0.138.0 until Z-C21.9, finding
  N69: the fix is in 0.140.0) it never created a gusset: it passed
  `InsertGussetFeature3` a polygon profile with both short sides at zero
  (`ProfileD3 = ProfileD4 = 0`) and no selection mark, and SOLIDWORKS returned
  null without a reason. The API help (InsertGussetFeature3 Remarks: "select two
  faces that are the supporting legs of this gusset with a Mark of 1";
  Insert_Gusset_Feature_Example_CSharp.htm) asks for mark 1 on both faces AND a
  valid profile. Measured on the 80x80x5 tube T: triangle 60/60, 5 thick,
  +9000 mm³, bodies 2 -> 3; polygon 60/60 with short sides 15/15, +12937.5 mm³.
  New arguments `profilo` (`triangolare` | `poligonale`), `lato3_mm`,
  `lato4_mm`; a degenerate polygon, faces that are parallel, apart or not
  planar, and a `posizione` outside the list are refused before SOLIDWORKS is
  called. Only `centro` is exercised on the bench; `inizio` and `fine` are
  accepted but not measured. The answer no longer echoes the inputs (audit
  finding C1-1): the added volume is checked against the hand calculation, and
  thickness, profile, location and supporting faces are read back from
  `IGussetFeatureData`, plus the new body and its box (`volume_added_mm3`,
  `volume_expected_mm3`, `volume_check`, `bodies_before`, `bodies_after`,
  `new_body`, `new_body_box_mm`, `thickness_read_mm`, `thickness_type_read`,
  `profile_read`, `location_read`, `supporting_faces_area_mm2`,
  `contact_mm`); if one does not match, the gusset is removed and the refusal
  says so. The new feature must also be of type `Gusset`, the type every
  variant of the probe returned. The schema no longer says which side goes on
  which face: the help does not say it and no probe told them apart (only equal
  sides were measured). Bench: `saldature/il_fazzoletto_sta_nell_angolo`,
  `saldature/il_poligono_del_fazzoletto_non_e_degenere` (each with its
  refusals and twins in the same file).
- **The four probes are in the repository** (`Controlli\RaccordoDiFaccia`,
  `Controlli\Riempimento`, `Controlli\Nervatura`, `Controlli\Fazzoletto`),
  each with the output of 28/9/2026 in its header, built by
  `costruisci-controlli.ps1` and so by every `build.ps1` (step 1c). The code
  and the cases cite those paths and the help pages by name, no longer a
  session scratchpad.
- **Bench: a twin with the same arguments on another part.** A refusal that
  depends on the part and not on an argument declares it in `deve_fallire` as
  `la_differenza_e_il_pezzo` (a sentence saying what changes). The bench and
  the contracts (`Attese.SiSomiglianoNelPezzo`) require the sentence, arguments
  IDENTICAL to the twin's, and different steps before (same part and same
  arguments is still a copy). Four new contracts test both ways.
- **Contract floors.** Refusals that name a way forward (`ProveIRifiuti`):
  146 -> 216 (+15 face fillet, +18 fill pattern, +10 rib, +24 gusset, +3 from
  the review: the fillet off the hand calculation, the rib attempt that leaves
  another feature, the gusset of the wrong type). Contracts declared in
  `Prove/LEGGIMI.md`: 401 -> 405.
  Operations with a copyable example (`ProveGliEsempi`): 29 -> 30 (the rib).
  Two required fields get a written decision (`ProveICampiObbligatori`):
  `ripeti_riempimento.copie_per_giro`, `nervatura.piano`. Bench cases: 13 new
  (lavorazioni 27 -> 34, ripeti 9 -> 13, saldature 5 -> 7).

## 0.114.0 — 28 September 2026 · library 0.139.0

**Single helpers for the recurring API patterns, and a static check that keeps
obsolete calls from coming back (entry Z-C21.6 of the API audit).** Built on
the Z-C21.2 branch. **Seen on the full bench on library 0.140.0, 28/9/2026**,
together with the four operations of 0.115.0: none of the reds of that run is
in the changes marked *behaviour* below.

- **`ApiHelpers`** (Riscontro.Disegno/ApiHelpers.cs, ApiHelpers.Geometry.cs):
  one place per pattern, each helper quoting the help page it is built on.
  - S1 `ReadDimension` / `ReadDimensionInConfiguration`: the 18 readings of a
    dimension value (7 `SystemValue`, 11 `GetSystemValue2("")`) go through
    them, each keeping the call it made before. GetSystemValue3 is not used
    yet: the shape of its return waits for the Z-C21.3 probe.
  - S2 `NewMassProperty`: the 4 `CreateMassProperty` sites (a new object in
    system units, bodies attached, `bodiesAttached` reported). CreateMassProperty2
    works by selection and waits for the same probe.
  - S11 `ModifyAndVerify` (ModifyDefinition always followed by GetErrorCode2)
    and `RebuildError`: 4 ModifyDefinition sites and 9 hand-written
    GetErrorCode2 readers. Each caller keeps its own verdict. `ErrorCodeKnown`
    (and `RebuildError(feature, out known)`) tells "no error" from "could not
    read the code".
  - S12 `EdgeParameters`: GetCurve always before GetCurveParams3 (15 sites,
    6 of which skipped it). *Behaviour*: GetCurve is now called where it was
    not, as IEdge~GetCurveParams3.html requires. The one GetCurveParams2 site
    (sketch-text outline) goes through `EdgeParameterArray`, still obsolete.
  - S6 `TrueBoundingBox` (six GetExtremePoint) for bodies and, through a
    temporary sheet body (IFace2.CreateSheetBody), for faces. *Behaviour*: the
    flat length measured by the sheet-metal operations (LunghezzaSviluppata), the face boxes of
    `verifica_sovrasporgenze`, `incidi_testo` and the extreme-face choice
    in Riferimenti now use the true box instead of GetBox/GetBodyBox, which the
    guide calls approximate and "not for comparison or calculation". A face box
    that cannot be measured is null, not approximate, and each caller says so
    instead of turning it into a verdict (see the review corrections below). The approximate boxes that are
    approximate ON PURPOSE (a ceiling, a permissive pre-check, the pair
    SezioneProfilo / IngombroDeiCorpi, the fallback of the state when GetExtremePoint does not answer) go through `ApproximateBoundingBox`
    with their reason; the unions of boxes go through `UnionOfBoxes`.
  - S10 `CreateFeatureFromDefinition` (selection, CreateDefinition, fill,
    CreateFeature, GetErrorCode2): ready for the Z-C21.7 migrations; today only
    the mirror of components uses it.
  - S17 `NormalIfPlanar`: 10 readings of IFace2.Normal (null on a curved face
    instead of 0,0,0; each site already treated either the same way).
- **`SelectionMarks`** (S9): the 18 non-zero literal marks at selection calls,
  and the 2 passed through the draft's `Seleziona` helper, now name a constant
  that quotes its page (23 constants); the divergences from the guide (L2-1,
  L4-4, L7-5, L8-1, and the missing marks of the gusset M27, of the variable
  fillet and of InsertCosmeticWeldBead2 L9-3) are recorded in the table, not
  changed. `SelezionaAsse` takes `append` explicitly instead of inferring it
  from `mark != 1`.
- **`ErrorCodeDecoder`** (S13): swSetValueReturnStatus_e, swFileSaveError_e and
  swFileSaveWarning_e decoded to their ENUM NAMES, from the interop enums (a
  mask as `swReadOnlySaveError | swFileLockError`). *Behaviour*: four
  messages now add the enum name after the raw code (two SaveAs3 exports, two
  SetSystemValue3 refusals), e.g. "(codice 2: swSetValue_InvalidValue)". No
  sentence in either language: English words inside the Italian messages, or
  new Italian words past the language gate, were both wrong.
- **Refined list of obsolete members**: `Controlli/obsolete-api-list.js`
  builds `Prove/data/obsolete-api-members.tsv` (3066 members, 21 obsolete only
  for some uses) from sldworksapi.chm and obsoleteapi.chm. Compared with the
  raw list of Z-C21.1 it drops 27 false positives ("the now obsolete X"),
  adds about 110 members the raw extraction missed, adds the 2 members known
  only from a "now obsolete" mention (ICurve.Length2, IPartDoc.EditSuppress2),
  and lets the current guide win on 10 old forms of current members.
- **Prove.Contratti**: `ObsoleteCallsContract` (every call to an obsolete
  member is MEASURED, i.e. an `OBSOLETE-MEASURED: <bank case>` comment AND an
  exact entry per file, member and case that says what is known of the
  replacement, or FROZEN for Z-C21.7; the calls whose receiver type the source
  does not say are frozen per file and name in
  `Prove/data/obsolete-undecided-frozen.tsv`; all three are exact ratchets.
  88 calls today, 4 measured, 84 frozen, 451 undecided; seen red five times on
  changed copies of real files) and `SelectionMarksContract`.
  `OBSOLETE-MEASURED` on GraphicsRedraw2 x2 (OperazioniTondoTavola),
  AddCenterMark and InsertCosmeticWeldBead. 392 checks.
- **Corrections after the adversarial review** (same branch):
  - the obsolete-calls contract: a marker citing any existing case no longer
    lets a new call through (the `Measured` entries are exact); a type is
    taken only from a declaration IN SCOPE (the same method's blocks, the
    parameters, the fields of the same partial class), never from another
    method's variable of the same name, and a `var` of unknown type hides
    outer names; the return types of methods in the source, `new T()` and
    array elements give a type, which found an obsolete `IView.Name` in
    OperazioniDistintaTavola and three `ISheetMetalFeatureData.KFactor` in
    OperazioniLamiera (both frozen, with reasons) and removed two false ones
    (`.Select` of LINQ in Banco/Program.cs); the holes of interpolated
    strings are scanned; the undecided calls have their ratchet; the ratchet is
    seen red both ways;
  - `OBSOLETE-MEASURED` now means "a bank case proves the effect of this very
    call"; what is known of the replacement (tried and failed, or not
    measured) is written in the entry, so the two markers whose replacement was
    not measured are no longer in contradiction with the definition;
  - *behaviour* — a face whose true box does not answer is not a verdict:
    `verifica_sovrasporgenze` counts it as not analysed (inconclusive, new
    `planar_faces_without_true_box`), `incidi_testo` refuses before touching
    the part instead of skipping the overflow check, and the extreme-face choice
    of Riferimenti falls back to a point on the face (IFace2.GetClosestPointOn)
    and says "could not be measured" instead of "there is no such face";
  - *behaviour* — putting a pattern back after a refused change of instances
    says "put back as it was" only when GetErrorCode2 says the pattern is sound;
  - the four sheet-metal operations, the anchor movement check and the
    feature-error count of `riordina_lavorazione` read GetErrorCode2 through
    `RebuildError` (*behaviour*: the sheet-metal and anchor messages give the
    error in words instead of "codice N"); the three hand-written unions of
    approximate boxes and the one-axis extent go through `UnionOfBoxes`. The
    state (StatoDocumento) keeps its own reader: it also records warnings,
    which RebuildError leaves out by design.

## 0.113.0 — 28 September 2026 · library 0.138.0

**Silent defects that need no probe (entry Z-C21.2 of the API audit).** Every
fix below is a case where a part, a file or an answer came out wrong with
`riuscita: true`, and the SOLIDWORKS API help alone was enough to decide. Each
fix quotes its help page in the code. The new bench cases are written to go
red on the previous library and green on this one.

- **`esporta_stl`, `esporta_step`, `esporta_iges` clear the selection before
  `SaveAs3`** (L13-1). IModelDocExtension~SaveAs3.html: "Exports the entire
  model, unless faces or bodies are selected, in which case, it exports only
  those". A face left selected produced an STL of that face alone, and the
  file check (84 + 50 bytes per triangle) compared the file with itself.
  `triangoli_dalla_geometria` is still reported, not enforced: a threshold on
  the order of magnitude needs a measurement on curved parts first.
- **`taglia_membri` always sets the WeldGap bit, also with gap 0** (L9-2).
  InsertWeldmentTrimFeature2.html: without the bit "the weld gap defaults to
  the last value specified in the SOLIDWORKS user-interface". A gap given by
  hand in Trim/Extend was silently inherited while the answer said
  `gioco_mm: 0`. This changes behaviour for anyone relying on the UI gap; the
  catalog says so.
- **`combina_corpi` resolves body uids by signature** (L6-1), through
  `CorpiDaUid` + `FirmaCombacia` like `sposta_corpi` (entry E4), instead of by
  position in a fresh `GetBodies2` (GetBodies2.html: "May vary the order in
  which bodies are returned"). `IndiceDelCorpo` is gone. The bodies found are
  passed to `InsertCombineFeature` as they are (no search by COM identity in a
  second `GetBodies2`, which was never measured); "is this the main body?" is
  asked through `ISldWorks.IsSame`. New `corpo_principale_rif` and
  `corpi_utensile_rif` name the bodies of an earlier step of the same plan —
  without them a "cut, then combine" plan had no valid way to name a body.
  Names in the answer are state uids, or "C<n> (per posizione)" when the body
  was taken by position.
- **`verifica_sovrasporgenze` checks hidden bodies too** (L12-1:
  `GetBodies2(swSolidBody, false)`) and **no longer treats a curved face
  without tessellation as vertical** (L12-2: IFace2~Normal.html, "If the face
  is not a planar face, then this property returns 0,0,0"). Such faces are
  counted in the new `faces_not_analyzed` (with `area_not_analyzed_mm2`), and
  when nothing overhangs elsewhere `si_stampa_senza_supporti` is `null`, not
  `true`. `hidden_bodies` says how many hidden bodies were checked; `corpo_uid`
  now resolves by signature. **Open: the mini-probe on the tessellation of a
  hidden body was written, not run** (`TassellaNascosto`, kept outside the
  repository in the session scratchpad): the help does not say whether
  `GetTessTriangles` answers on a hidden body. If it does not, curved faces of
  hidden bodies end up in `faces_not_analyzed` — a non-conclusive verdict, not
  a silent "yes".
- **`modifica_quota` changes angular dimensions in degrees** (M3).
  IDimension~GetType.html (swDimensionParamType_e): "30" on an angle used to
  become 0.03 rad and the equally wrong read-back passed. The answer carries
  `before_deg`/`after_deg` instead of `prima_mm`/`adesso_mm`; integer
  dimensions are refused. **Read-only and driven dimensions are refused** (M8):
  SetSystemValue3.html, "This method allows you to change the value of a
  read-only dimension".
- **`cambia_estrusione`: "ICE" is not "cut"** (M7). A boss made with
  `estrudi_su_faccia` has type "ICE" like face cuts (GetTypeName2.html);
  cut or boss now comes from `IExtrudeFeatureData2.IsBossFeature`, so a boss
  answers `tipo: estrusione` and "passante" on it is refused, and a real "Cut"
  is accepted. IsBossFeature is asked **only for "ICE"**: "Cut" and "Extrusion"
  decide by themselves. Asked of every extrusion, it answered false on the
  base plate (the first extrusion of the part), and "passante" on the plate
  went through (`il_cieco_diventa_passante`, step 6, red in the full bench).
  Measured by `Controlli/TipoDellEstrusione`: the base is "Extrusion" and
  IsBossFeature false; every later extrusion, boss or cut, even from a sketch
  on a plane, is "ICE" for GetTypeName2 (GetTypeName says Boss or Cut).
  **Two independent directions are refused** (L5-1:
  BothDirections.html): only direction 1 was written and read back.
- **Rebuild errors after `ModifyDefinition` and after creation are read**
  (ModifyDefinition.html: "call IFeature::GetErrorCode2"):
  `flangia_base` reads `GetErrorCode2` like its three siblings and refuses a
  flange that added less than half of the hand-computed volume (L10-4);
  `foro_guidato` reads it after each `ModifyDefinition` but decides only after
  positioning (before that the hole still sits at HoleWizard5's provisional
  point and may be in error while the final hole is sound; earlier codes are
  only quoted in the refusal), always measures the volume (not only with
  anchors), and removes a hole that is in error or removed nothing (L4-3);
  `sopprimi_istanza` reads it, refuses a pattern left with no faces, and
  refuses when the lost faces are not seed faces × copies removed, putting
  the array back (L6-5). The answer carries `faces_per_copy` (null when the
  seed faces could not be read, i.e. that check did not run), and the catalog
  says that copies merged together are refused for the same reason.
- **`sopprimi_istanza` refuses two-direction grids** (M6). SkippedItemArray.html
  gives I = n2·(i−1)+(j−1) for a bi-directional pattern: a single copy number
  does not name a grid position, and "copy 3" on a 3 × 2 grid removed
  instance [2, 2]. Linear (`IsDirection2Specified`), circular (`Direction2`,
  `TotalInstances2`, or `Symmetric`) and curve-driven (`Dir2Specified`,
  `D2InstanceCount`) patterns are all checked.
- **`accoppia` no longer offers concentric and tangent** (M17): faces are
  chosen by direction through `IFace2.Normal`, i.e. only planar faces, and
  between two planar faces those mates cannot exist. They are refused with the
  reason, before touching anything.
- **`distinta_taglio_in_tavola` finds the quantity column by type first**
  (L16-8: GetColumnCustomProperty.html, "returns an empty string if the column
  is not a user-defined column"; `GetColumnType2` = swWeldTableColumnType_Quantity),
  then by the "QUANTITY" property, and says so in `quantity_column_found_by`
  and `comparison_done`. Its bench case now uses SOLIDWORKS' stock template
  (`{SOLIDWORKS}\lang\english\cut list.sldwldtbt`), where QTY is a typed
  column: the shop template of the options has QTY as a property and was
  already found by name on 0.136.0. That QTY is type 402 in the stock
  template is taken from the help, not measured: the case says what to do if
  it comes out "name".
- **The anchor refuses the tangent edge of a fillet** (C2-1 of the audit
  completion): the rule "the other face must be planar and perpendicular"
  never checked "planar", and a fillet's Normal is 0,0,0 — cosine 0, exactly
  like a real perpendicular wall. `IsPlane()` now comes before `Normal`; on a
  curved face the normal is taken at the edge midpoint with
  `ISurface.EvaluateAtPoint`, and the edge is refused when that face is
  tangent (a fillet) or oblique, and accepted when it meets the face at 90°
  (a sharp edge against a cylinder). If
  the check cannot look (no faces, no normal, an exception) it refuses instead
  of accepting (N6); an anchor on a non-planar working face is refused (N24).
  The anchor's movement probe now reads the `SetSystemValue3` status and the
  moved value (M12): a dimension that did not move proved nothing.
- **Bench**: steps accept `prima_seleziona` (entities left selected before the
  step) and `prima_nascondi` (bodies, or the body of a face, hidden before the
  step); `@quota(angolare=true)` picks the only angular dimension; with
  `uid_dal_passo` bodies now carry their signature like the bridge sends it.
  Ten new cases: `lo_stl_non_esporta_la_faccia_selezionata`,
  `il_taglio_senza_gioco_non_eredita`, `lim_combina_col_corpo_letto_prima_del_taglio`,
  `il_corpo_nascosto_che_pende_si_vede`, `l_angolo_dello_smusso_si_cambia_in_gradi`,
  `il_rialzo_cresce_e_non_e_un_taglio`, `lim_foro_guidato_fuori_dal_pezzo`,
  `lim_griglia_non_si_conta_come_una_fila`,
  `la_distinta_di_taglio_dice_se_ha_confrontato`, `lim_ancora_sul_bordo_del_raccordo`.
  `il_taglio_senza_gioco_non_eredita` cannot reproduce a gap typed in the UI:
  that scenario stays a manual probe, and if the case stays green on 0.136.0
  it is a regression case, not the proof of L9-2 (the case says so).
  `salta_la_seconda_copia` also expects `faces_per_copy: 1`. New placeholder
  `{SOLIDWORKS}`: the SOLIDWORKS install folder (`SW_INSTALL_PATH`, as in
  build.ps1, or the default one), for stock files that cannot be versioned.
- Numeric comparisons of `GetErrorCode2` in the new code use
  `swFeatureError_e.swFeatureErrorNone` instead of the digit (Z-C7).
- Contracts: the floor of refusals that name a way out goes from 136 to 146.

## 0.112.0 — 28 September 2026 · library 0.137.0

**The whole SOLIDWORKS API, searched and described by reflection (entry
U1.1).** Two new MCP tools, nine in all: `api_search` and `api_signature`.
They read the interop loaded in the bridge (`typeof(ISldWorks).Assembly`) and
the `swconst` enums; `swmotionstudy` is listed read only when present. They
never touch SOLIDWORKS and work with it closed.

- **The engine is in the library**, `Riscontro.Disegno\Api\ApiCatalog.cs`;
  the bridge only registers the tools.
- **13,945 exposed members**: the 14,928 of the interop minus the four old
  interfaces `IModelDoc`, `IBody`, `IFace`, `IComponent` (983). A search names
  them with the sister to use; a signature request answers «old interface: use
  …» and never prints the old signature.
- **A card says only what is known.** Every overload with real types, property
  or method, `&` out parameters by name, the typed `I…` variant, the numbered
  family with the last one «latest by number, not verified», the Void flag.
  Which enum an `Int32` takes comes ONLY from the hand-written map in
  `Strumenti/api_knowledge.json` (otherwise «enum not tabulated»), and array
  indices only when measured (otherwise «index not tabulated»).
- **`Strumenti/api_knowledge.json`** (new, embedded in the executable): state
  (`forbidden`, `confirm`, `gray`, `never_probed`, `probed_read`,
  `probed_modifies`), enum map, measured indices, measured types of `Object`
  parameters, source. First fill from the memories, the RAG notes and the
  "measured" comments of the sources: 167 members (52 forbidden, 33 confirm,
  16 gray, 21 never_probed, 31 probed_read, 14 probed_modifies), each name
  checked against the interop by the contract.
- **`Controlli/api-inventory.ps1`** writes the inventory from PowerShell; the
  contract `ApiSignatureMatchesInventory` compares it with the C# formatter
  (14,928 lines, zero differences, seen failing on one lost `&`).
- Contracts: the point checks of the entry, knowledge naming only real
  members (seen refusing an invented one), hidden interfaces losing no name
  (seen saying no for `IMassProperty`), both tools through the real bridge.
  `tools/list` is now asserted at nine.
- **Bench:** no case, by the entry's own rule — these calls do not touch the
  part. The bench still has to run on library 0.137.0 before release, because
  the library changed.

## 0.111.0 — 27 September 2026 · library 0.136.0

**The adversarial review of the day's work (entry Z-C20).** Three reviewers and
one adversarial verifier per finding went over everything closed on 27
September; seven findings were confirmed real, and eight more were sensible.
All of them are fixed here, each with a bench case where the bench can build
the situation.

- **The guards of `modifica_quota` in one place** (`OperazioniGuardie.cs`):
  rebuild result, features in error, body count, openings that break out of the
  material, a chamfer or fillet that no longer fits. `variabile_globale`,
  `lega_quota` and `cambia_estrusione` had one or none — a variable driving the
  thickness, set to 1 under a 2 mm chamfer, rebuilt clean, changed the shape
  and answered "done". Now they refuse and put things back (the old equation
  rewritten, a new one deleted, an unbound dimension restored), and say
  whether the volume came back.
- **`cambia_estrusione` keeps a mid-plane extrusion mid-plane.** A symmetric
  plate 100 deep asked for 120 became blind from 0 to 120 — it moved by 60
  instead of growing by 10 per side. "Through" on a mid-plane cut goes through
  both ways; end conditions with a reference (up to a surface, a vertex, a
  body) are refused, because the reference could not be restored.
- **Drawing dimensions: "already dimensioned" is read before the call, and per
  CYLINDER.** Two Ø8 holes, one through and one blind, are two groups — the
  second one used to be skipped as already dimensioned, with no callout and no
  depth. Two Ø30 journals, or a Ø30 step and a Ø30 cross hole, are different
  cylinders and no longer cancel each other. A hole-position dimension no
  longer marks the reference edge's length as dimensioned.
- **Turned parts seen from the side count only planar faces.** On a Ø30 x 40
  shaft chamfered 1 x 45° the chamfer circles made the lengths 1, 39 and 40;
  now only 40, and the chamfer is reported (`tondo_note`). Seen end-on, a
  chamfer edge is not an outer diameter (`bordi_non_quotati`).
- **The STEP of an assembly counts one shape per part, not per instance.**
  Measured: an assembly with the same base twice and a lid once writes 2
  solids — the shape of a part is written once and the instances are
  occurrences. The expected count was per instance (3) and refused a correct
  STEP on every assembly with repeated screws or feet. Now per unique part
  (file plus configuration); a solid with voids (`BREP_WITH_VOIDS`) counts too.
- **A boss is not a hole.** The foot of a boss is also an inner loop of a
  planar face; the wall that starts from it goes outward, so it gets a
  diameter dimension, not a hole callout.
- **Smaller ones:** `lega_quota` compares in document units (angles in degrees)
  and reports a replaced binding; `variabile_globale` reads back in mm from the
  document units; the state reports `equazioni_totali` beyond twenty; IGES of an
  assembly is refused (not verifiable, depends on a global preference); a
  rejected `specchia_componente` removes the mirror; `dipendenze` without an
  answer says "unknown" for missing files too; the assembly mass skips the
  per-part cross-check for a component using another configuration, and asks
  that configuration for the material.
- **The last live \`EditDelete\`.** Removing a hole table born wrong called
  \`modello.EditDelete()\`, which asks for confirmation in a dialog — inside
  SOLIDWORKS the COM call never returns. Found by the workflow that designed
  Section U; replaced with \`DeleteSelection2\`, and the ban is now a rule of
  \`Prove/ProveLeFirmeVietate.cs\` (it was only written in comments).
- **The tested version is declared (entry H17).** The README says "tested only
  on SOLIDWORKS 2021 SP5.1"; the installer accepts any version and warns when
  it is not the tested one (older ones can be tried, newer ones are untested).

New cases: `tavola/due_fori_uguali_uno_passante_uno_cieco`,
`tavola/la_borchia_non_e_un_foro`, `tavola/l_albero_smussato_non_quota_le_gambe`,
`lavorazioni/la_piastra_simmetrica_cresce_dalle_due_parti`,
`quote/la_variabile_non_scavalca_lo_smusso`, `assiemi/due_basi_uguali_uno_step`,
`esporta/lim_iges_di_un_assieme`.
`esporta_iges` now declares `parte` only. Ninety-five operations.

## 0.110.0 — 27 September 2026 · library 0.135.0

**Which files an assembly stands on, and which are missing (entry Z-B3).**
An assembly is a graph of files on disk, and the model could not know that
one of them was gone. New operation `dipendenze` (any document). Measured
first: `GetDependencies` returns flat name/path pairs at all levels, and
**the parameter literally called «list broken references» marks nothing** —
a component file renamed on disk is listed with its path like the others.
Only the disk can tell (`File.Exists`); reopened, the assembly shows that
component suppressed with no document and `OpenDoc6` error 2.

- The answer lists every file with `esiste`, and the missing ones with a
  warning (find or replace them before measuring or exporting).
- The assembly state adds `file_mancante` to a component whose file is not
  on disk.

New case `assiemi/l_assieme_dice_da_che_file_dipende` (the healthy assembly;
the missing file was seen by the probe). Ninety-five operations.

## 0.109.0 — 27 September 2026 · library 0.134.0

**Change a feature that is already there (entry Z-F4).** A blind hole that
must go through, a plate that must grow: until now the only way was to
rebuild the part. New operation `cambia_estrusione` (`feature_uid`, and
either `passante` or `profondita_mm`) for extrusions and extruded cuts.
Measured first (120×80×10 plate, Ø8×5 blind hole = 95748.67 mm³):
`GetDefinition()` as `IExtrudeFeatureData2`, `SetEndCondition` /
`SetDepth`, `ModifyDefinition` — through-all 95497.35, blind 7 again
95648.14; the base extrusion 10 → 12, with the hole on the top face following
it (114848.14).

- The definition is read back (end condition and depth), with volume and
  features in error before and after; if the change breaks a feature
  downstream the old definition is put back and the operation refuses.
- A blind depth past the material gives no error and is already through:
  when the volume does not change, the answer says so.
- `passante` on a boss extrusion is refused (there is nothing to go
  through); other feature types point to `modifica_quota`.

New case `lavorazioni/il_cieco_diventa_passante`. Ninety-four operations.

## 0.108.0 — 27 September 2026 · library 0.133.0

**The right bracket and the left one (entry GA11).** New operation
`specchia_componente` (`componente`, `piano` of the assembly, `modo`,
optional `suffisso`). The danger is in the link: linked and not wanted, you
change the right bracket and the left one changes too; unlinked and not
wanted, you correct the right one and the left stays wrong without saying so.
So `modo` is always asked, never defaulted. Measured first with the modern
route (`FeatureManager.CreateDefinition(swFmMirrorComponent)` →
`IMirrorComponentFeatureData`, typed `Component2[]` arrays):

- `istanza`: one more component of the SAME file, mirrored;
- `opposto_legato`: a NEW file whose tree is a single `MirrorStock` feature —
  it follows the original;
- `opposto_slegato`: a new file with the features COPIED — independent.

Afterwards the answer is VERIFIED: the new component's true-box centre
against the mirror of the original's centre (within 0.01 mm), and for the
opposite-hand files the link read from the new file's tree. New case
`assiemi/la_staffa_destra_e_la_sinistra` (all three, on three planes).
Ninety-three operations.

## 0.107.0 — 27 September 2026 · library 0.132.0

**A photo to trace, to scale, in a sketch (entry Z-F5).** The most frequent
input in a small shop is not a DXF: it is a photo, or a sketch with the
dimensions in pen. The model can read the image; it lacked the place to put
it. New operation `immagine_nello_schizzo` (`percorso`, `piano`,
`larghezza_mm` — the true width of the whole image — and the lower-left
corner `x_mm`, `y_mm`). Measured first with a 400×200 px PNG:
`InsertSketchPicture` puts it in at 1 px = 1 mm; `SetSize` with the aspect
ratio locked and `SetOrigin` put it where asked (120×60 mm at 10,20).

- The scale is VERIFIED, not believed: the first and last pixel of the first
  row, mapped into the sketch with `GetPointOnSketchFromPixel`, must be
  (columns − 1) × mm-per-pixel apart within 1% (119.7 mm in the case).
  **That call takes (column, row), the opposite of its argument names**
  (measured: the first argument moves along X; a row beyond the height gives
  null).
- A PDF does not go in (null): the refusal says to convert the page to PNG.
- The answer gives the rule from a pixel to millimetres in the sketch.
- The bench learns `{DATI}` (`Banco/dati/`, versioned): input files a case
  reads, since `{LAVORO}` is emptied every run.

New case `piano/la_foto_sta_in_scala`. Ninety-two operations.

## 0.106.0 — 27 September 2026 · library 0.131.0

**Reorder the tree (entry Z-F3).** A shell before a hole gives a different
part from a shell after it, and moving the feature is a weekly gesture. New
operation `riordina_lavorazione` (`feature_uid`, `rispetto_a_uid`, `dove`:
prima/dopo). Measured first on a 100×60×40 box with a Ø10 through hole and a
3 mm shell opening the top: `ReorderFeature` returns true and the volume
changes (hole then shell 56485.7 mm³, shell before the hole 51952.38), and
putting it back gives 56485.7 again; an impossible move (the hole before the
extrusion it sits on) returns false and touches nothing.

- Features are named as everywhere in the bridge, and the name is checked
  against the `feature_id` the state gave (a name can be renamed, the ID
  cannot).
- After the move the tree is rebuilt and the features in error counted: if
  the move broke one downstream, the feature goes back where it was and the
  operation refuses. The order is read back (true from `ReorderFeature` is
  not trusted alone), with volume and bodies before and after; an order that
  changes nothing in the part is said.

New case `lavorazioni/l_ordine_cambia_il_pezzo`. Ninety-one operations.

## 0.105.0 — 27 September 2026 · library 0.130.0

**Give a number a name (entry Z-F1).** Two new operations on SOLIDWORKS'
own equations: `variabile_globale` creates or changes a global variable (a
value in mm, or an expression such as `2 * "parete" + 1mm`), and
`lega_quota` makes a dimension driven by an expression. A dimension driven by
`parete` follows every change of `parete` by construction — the identity
system SOLIDWORKS already has, and the only kind of model someone who is not
the designer can change without breaking it. Measured first on a 120×80×10
plate: `IEquationMgr.Add3` returns the index; with the thickness linked,
`parete` = 12 mm gives 115200 mm³ and 15 mm gives 144000.

- `get_Status` cannot be trusted (−1 after a successful `EvaluateAll`, 1
  after a successful `set_Equation`): every answer is checked on the VALUES
  read back — the equation's and the dimension's.
- «spessore» (and «Spessore») is refused by the Italian SOLIDWORKS even on a
  part with no properties, and an equation that uses it evaluates to 3: the
  refusal says so and proposes another name.
- The name of a dimension in an equation is its `FullName` without the part
  at the end («D1@Estrusione-Estrusione1»).
- The part state lists its equations (`equazioni`), so a driven dimension is
  known before someone tries to change it by hand.

New case `quote/la_parete_ha_un_nome`. Ninety operations.

## 0.104.0 — 27 September 2026 · library 0.129.0

**What holds a component, and a new operation to fix or free it (entries
Z-D3, Z-D4, Z-D5).** The assembly state said HOW MANY mates a component has,
not WHICH; it did not say whether a component is a sub-assembly, hidden or
transparent; and fixing or freeing a component was not possible. Measured
first: `GetMates` returns `IMate2` objects whose `Type` is `swMateType_e`;
transparency lives in `MaterialPropertyValues[7]` (0.75 after
`SetComponentTransparent`), only once the component has its own appearance;
`FixComponent`/`UnfixComponent` on the selected component work both ways,
including on the first component, which SOLIDWORKS fixes by itself.

- Assembly state, per component: `accoppiamenti_tipi` (by name: coincidente,
  concentrico, parallelo, …), `e_sottoassieme` (null when the component is
  suppressed: «I don't know», not «no»), `visibile`, `trasparente`.
- New operation `fissa_componente` (`componente`, `fisso` — both required:
  fixing and freeing are opposites). Both calls return nothing: the result is
  read back with `IsFixed`, and the answer says how many mates still hold a
  freed component (zero means it rests there and moves).

New case `assiemi/fisso_non_e_accoppiato`. Eighty-eight operations.

## 0.103.0 — 27 September 2026 · library 0.128.0

**The whole assembly: how much it weighs, how big it is, and the STEP to send
(entries Z-D1, Z-D6).** `proprieta_di_massa`, `ingombro_corpo`,
`esporta_step` and `esporta_iges` refused an assembly («not a part»).
Measured first (base in AISI 304, lid in S235JR, a third instance
suppressed):

- `CreateMassProperty()` on the assembly, with no bodies added, gives
  exactly the sum of the parts (1.06752 kg); the suppressed component does
  not weigh. The answer checks it against the parts read one by one and stops
  if they differ; a part without material makes the mass null, naming it.
- `IComponent2.GetBodies3` returns bodies in PART space (the lid at y 0..4
  instead of 5..9): the bounding box uses a copy of each body moved with the
  component's `Transform2`.
- The assembly STEP is written like a part's, and the check is the count of
  `MANIFOLD_SOLID_BREP` in the text against the solid bodies of the
  unsuppressed parts: reopening an assembly STEP with `LoadFile4` opens the
  «New document» dialog and the call never returns, so
  `riapri_e_confronta` is refused on an assembly, saying why.

Suppressed components are listed, never silently dropped. The STL of an
assembly is still refused, now pointing to `esporta_step`. New case
`assiemi/l_assieme_si_pesa_si_misura_si_manda`.

## 0.102.0 — 27 September 2026 · library 0.127.0

**A turned part dimensioned from the side (entry GT14).** Until now the
longitudinal view of a shaft or a bushing was refused: its only edges are the
end circles seen edge-on. Measured on the stepped shaft of the `rivoluzione`
example (Ø30×40, Ø20×30, Ø34×20):

- two edge-on circles selected + `AddDimension2` give the distance between
  the faces — the LENGTHS, now placed in parallel from the first face (the
  same single zero as the holes): 40, 70, 90;
- two silhouette edges of the same face give, by themselves, a diameter
  dimension with the Ø prefix — the DIAMETERS: Ø34, Ø30;
- but SOLIDWORKS does not always expose them. `ISilhouetteEdge.GetFace` is
  null until the drawing is redrawn and the face asked a second time; on the
  bushing it stays null, and those silhouettes are tried in pairs — the
  dimension is kept only if SOLIDWORKS puts the Ø on it AND its value is the
  diameter of a coaxial cylinder of the part not yet dimensioned; the Ø20 of
  the shaft has a silhouette that cannot be selected at all, and the answer
  says so in `tondo_diametri_non_raggiunti` instead of keeping quiet.
- Roads measured and closed: `GetStartPoint` from out of process makes
  SOLIDWORKS raise (RPC_E_SERVERFAULT), `GetCurve` is null, picking by
  position takes one silhouette at a time, a centreline plus a silhouette
  measures from the SHEET origin (212.49) and `Diametric` cannot be set.

The probes used `IModelDoc2.AddDimension2`, which the contract forbids (no
direction, entry GP6), and `Extension.AddDimension` with two entities creates
nothing: the code uses the DECLARED horizontal/vertical dimensions
(`AddHorizontalDimension2` for the lengths, `AddVerticalDimension2` for the
diameters of a horizontal shaft) — on two silhouettes of one face SOLIDWORKS
still adds the Ø by itself. Silhouettes are read again before every
dimension: after a new dimension the old ones may no longer select.
Every dimension is read back against the geometry before it stays. The two
views that follow repeat nothing. `esporta_pdf` now counts the dimensions
whose text cannot be read (`quote_non_controllate`) instead of skipping them
silently. New case `tavola/l_albero_a_gradini_quotato_di_fianco`; the side
view of `tavola/la_boccola_tornita_non_e_un_foro` is no longer refused.

## 0.101.0 — 26 September 2026 · library 0.126.0

**Do the dimensions reach the PDF? (entry Z-C9, the «objects» half).** The
drawing counters count what is in the document: a dimension on a layer that
does not print, or in a hidden view, counts — and never reaches the shop.
`esporta_pdf` now reads the PDF text back and requires every number printed
by the drawing's dimensions to appear there at least as many times as the
dimensions print it («at least»: the PDF also has title-block numbers). Seen
firing before being trusted: the «Ø12 THRU» callout moved to a non-printing
layer, and the export reports «12 (on the drawing 1, in the PDF 0)». One case
(the check stays quiet on a good drawing).

**`palloncini` is now `bollatura`** — the shop word for BOM balloons,
and the name the operation, the catalogue and the messages use. Tried on a
real three-part assembly (read-only, drawing saved outside the vault): the
isometric view ballooned items 1 and 2 and answered «done» while item 3 —
covered in that view — had no balloon anywhere. `bollatura` now compares the
item numbers of the BOM with the balloons of the WHOLE drawing and names the
items still without one («balloon another view, where it shows»). And a view
where every item is already ballooned elsewhere is no longer a failure: it
answers «nothing to add in this view». The old warning comparing balloons
with visible components is gone: it fired on every repeated component and
the item-number check says the thing that matters.

**A circle is not always a hole (`quota_in_tavola`, entry GT12).** Looking
for a blind hole the callout would not recognise, a turned bushing (Ø30 × 30,
axial blind bore Ø8 × 10, made by revolution) showed a worse defect: the Ø30
OUTLINE got a hole callout «Ø30 ↧30», drawn on top of the real «Ø8 ↧10», and
in the side views the two circles seen edge-on were «holes whose callout did
not appear». A hole now is a circle that faces the reader AND is an inner loop
of a planar face (the centre-mark rule); the outline of a round part gets a
diameter dimension, read back (edge-on, `AddDiameterDimension2` says 60 for a
Ø30 — measured), reported in `diametri_esterni`; edge-on circles are listed in
`cerchi_di_taglio`, and a view with nothing else is refused saying so. Every
hole also reports passante/depth READ FROM THE GEOMETRY (the cylinder and its
other circle: inner loop of a plane = through; outer loop or cone = blind),
holes are grouped by diameter AND depth, and when the callout does not appear
the depth is written into the diameter dimension («Ø8 ↧5», tried and looked
at). Revolved bores do get a proper callout: the fallback has not yet been
seen on a real hole. New case `tavola/la_boccola_tornita_non_e_un_foro`.

## 0.100.0 — 26 September 2026 · library 0.125.0

**`distinta_taglio` was wrong, and its case was green (entry GT2).** It put
EVERY bar into one cut-list item: the 1000 x 800 frame came out as
«QUANTITY 4, LENGTH 1000» — four 1000 mm bars where two are 800. The case
only asked that the bodies be four, and they were. The cause, measured: a
part made of structural members is not a WELDMENT (`IsWeldment` false),
and SOLIDWORKS does not group bars by length without the «Weldment»
feature. Now `distinta_taglio` adds that feature when missing, turns on the
automatic cut list on the solid-bodies folder, reads the items back
(description, length, quantity — recognised by their expression, since the
property NAMES are localised) and checks that the quantities add up to the
bodies. The same fix makes `distinta_taglio_in_tavola` work: the cut-list
table is born in the drawing, one row per length, and its quantity matches
the part. The operation is now `Provata`, and it takes the view by its
uid (V1) like the other drawing operations — the first bench case that
called it with «V1» got «no such view». One new case, and the old case now
asks for the two items.

New operation `vista_sviluppo` (entry GT13): the flat pattern of a sheet
metal part as a drawing view, with its bend lines. The view is MEASURED:
its size comes from the visible edges, not from the view outline — which
said 92.6 mm for a flat length of 80.7, because it includes the bend note
and a margin — and is compared with the flat length measured on the part
before the view is placed. Refused on a part that is not sheet metal, and a
view that is not flat or measures something else is removed. Two cases.

New operation `saldatura_in_tavola` (entry GT7): the ISO 2553 weld symbol
(fillet, V, bevel, square butt) with the throat «a» and optional length,
all-around and field-weld flags. The operation finds the joint itself —
the longest visible edge where one body touches another — puts the arrow
on it, moves the symbol out of the part, and reads everything back. The
symbol code is the one from SOLIDWORKS' symbol library, `<WELD-FILL>`:
measured, with a wrong code (`<IGWS-FILLET>`, «Fillet») the symbol is
born, reads back identical, and draws no triangle — only the PNG showed
it. Refused on a single-body part and on a view with no visible joint; a
symbol born wrong is removed. Two cases.

**Two defects found by LOOKING at the drawings, with the bench green.**
The cut-list table used the shop's table template, 17 columns wide, and at
250/200 mm it ran off the sheet after the fourth column. The flat-pattern
view at 300/90 mm, scale 2:1, ran into the title block. Now the cut list
keeps only the columns a workshop needs (item, quantity, length, the two
angles, description; `colonne` chooses others or «tutte»), and both the
table and the view are measured and placed in a free spot inside the
border, away from the other views and the title block (read from the
sheet format when it declares one, estimated otherwise). If there is no
room, they are removed and the operation says so.

`quota_in_tavola` (entry GT12): the hole callout of a blind hole already
carries its depth — SOLIDWORKS puts `<HOLE-DEPTH>5` in the suffix — and
now the operation reads it back (`fori[].profondita_mm`, `passante`). The
text of every dimension has a real rectangle: `IDisplayData` gives
position, height, angle and — `GetTextInBoxWidthAtIndex` — the width,
even without a box. The operation now reports texts that fall on another
text or on a line of the part (`testi_che_si_accavallano`). One case: a
blind hole with its depth, no hole dimensioned from the side view, no text
overlapping. (Pieces of the same dimension — a hole callout «Ø8 ↧5» is
four texts side by side — are not compared with each other: the first
bench counted four overlaps that on paper are one word.)

Entry Z-C13: two section views «A-A» on the same drawing are two different
cuts that look like the same one, and SOLIDWORKS allows it. `sezione_in_tavola`
and `dettaglio_in_tavola` now refuse a letter already used by a section or a
detail — read from the views (`IDrSection.GetLabel`, `IDetailCircle.GetLabel`),
not from what was asked — and report the letter they actually got. Both also
take the view by its uid (V1), like the other drawing operations. One case.

**Center marks (entry W2.11), blocked since 17/9 — and the block was mostly a
counter that lies.** SOLIDWORKS puts a center mark on every hole when it
creates a view (document option «center marks on holes», on in the house
template), and `IView.GetCenterMarkCount()` does not count those: it said 0
on a view with three marks. Enumerating them (`GetFirstCenterMark`/`GetNext`)
finds them all. The drawing state now counts them that way — an old case that
asserted «0 center marks» on three holes was green for a week on a false
zero. From code, no new mark is born: `AutoInsertCenterMarks2`,
`InsertCenterMark2/3` and `AddCenterMark` (on a circle selected in the view)
were all measured; with the automatic marks deleted, `AddCenterMark` adds
none, and on fillet arcs none either. So `croci_di_centro` is now a CHECK: it
recognizes the holes that have a mark, tries to add the missing ones, and
refuses — naming the document option — when a hole stays without one.
`Provata` again. One case.

New operation `tolleranza_quota` (entry Z-F2): a tolerance on a MODEL
dimension — symmetric (±), bilateral, or an ISO fit (H7 for a hole, g6 for a
shaft) — which then reaches the drawing by itself with `quote_dal_modello`.
SOLIDWORKS computes the ISO 286 deviations of a fit; the operation checks
them against its own copy of the ISO 286-1 table (IT5..IT11 up to 500 mm:
the width must be the IT grade, H starts at zero, h ends at zero) and puts
the dimension back if they disagree. It also sets the tolerance precision:
measured, without it an H7 on a 10 mm hole printed as «10 H7 +0 / 0» —
«exact» — because the limits followed the dimension's zero decimals. One
case (fit, symmetric, and IT14 refused).

**An adversarial review of this release, before the bench** (three lenses,
each finding checked by a refuter) found and this version fixes: the weld
symbol accepted when the symbol count could not be read (it could re-read an
old symbol), and «field weld» / «both sides» were not checked; the free-spot
search ignored tables and notes already on the sheet; `tolleranza_quota` on
an ANGULAR dimension took radians for metres (now refused), and a tolerance
whose precision could not be set answered «done» (now refused and restored);
`vista_sviluppo` answered «done» when the part's flat length could not be
measured; `croci_di_centro` took every full circle for a hole — a disc's rim
too (now: the circle faces the viewer and is an INNER loop of a planar face;
`FaceInSurfaceSense` was measured and does not tell, it depends on how the
surface was made); a section with no hatch stayed on the sheet and its letter
blocked the very remedy the message suggested; «0 overlapping texts» when no
text had been read; hidden cut-list columns declared without re-reading;
«feature created» on a cut list that already existed; the duplicate-letter
check looked at the current sheet only; `reso_saldato` true when
`IsWeldment` could not be read; and a failed `distinta_taglio` left the
«Weldment» feature in the part (now removed).

New operations `distinta_base_in_tavola` and `bollatura` (entry GT9 — «bollatura» is the shop word for BOM balloons): the
bill of materials of an assembly (`IView.InsertBomTable4`, top level by
default, the type chosen by name because `swBomType_e` starts at 1), checked
against the assembly itself — items = distinct components, quantities =
instances, two independent readings — and placed in a free spot; then the
balloons (`AutoBalloon5` with an options object), counted on the view before
and after, with the item numbers read back. One case: a two-part assembly.

## 0.99.0 — 26 September 2026 · library 0.124.0

**Tables are born on this installation.** On 17 September no table would
insert (entry GT2: even a generic 3x3 returned null). One probe on 26
September, with a positive control in the same run (a note, +1 every
time), across a fresh drawing, a saved one and one activated in front:
`InsertGeneralTableAnnotation` (never tried before), `InsertTableAnnotation`
and `InsertTableAnnotation2`, `InsertRevisionTable2` all create their
table; only `InsertWeldmentTable` stays null, in 24 combinations of view,
template, configuration and anchor. What changed since the 17th is not
measured; the likeliest candidate is the K9 fix (`viste_standard` put the
wrong part in the drawing).

`tabella_fori_in_tavola` (entry GT5): the hole table of the drilled face
seen in a view. `InsertHoleTable2` has no argument for origin or face:
they are SELECTED in the view — a corner vertex with mark 1, the face with
mark 2. With the marks swapped the table is born EMPTY (header only) and no
error: so the operation requires rows = holes + 1, removes an empty table,
and reads every row back against the part (each position must match a hole
measured on the view from the origin). On the demo plate: A1..A4 Ø8 at
15/15, 15/65, 105/15, 105/65 and B1 Ø20 at 60/40.

Looking at the drawing found a defect no check saw: the holes were
dimensioned twice, in the table and again with callouts and position
dimensions. `quota_in_tavola` now leaves the holes to the table when there
is one — and the table sits on the SHEET, not on the view, which is why
the first fix did not see it. A table added after the dimensions says the
holes are now dimensioned twice. `RisolviVista` is extracted from
`quota_in_tavola`. Two cases (the drawing, looked at; the table on the
side view, refused).

And the annotations (entries GT6, GT8): `rugosita_in_tavola` (surface
finish, Ra in micrometres), `datum_in_tavola` and
`tolleranza_geometrica_in_tavola` (a geometric tolerance frame: flatness,
perpendicularity, position...). All three were born at the first try — no
modal window — and all three were WRONG on the sheet, which only the PNG
showed: the surface finish ignored its coordinates and landed outside the
border; the datum flag sat on the far side of its edge with the leader
across the part; the frame floated attached to nothing. Then, with the
dimensions in place, the datum flag landed on the 120 dimension line and
the frame on top of the side view. Now each one is attached to the border
edge of the requested side (selected in the view), and moved out of the
part — beyond the dimensions already on that side, and out of the other
views. Ra, letter, symbol code, value and datum references are read back.
Refused: a datum letter already used (two datum A are no error and make
every tolerance citing A ambiguous), a form tolerance with a datum, an
orientation or location tolerance without one, a datum that is not on the
drawing, Ra outside 0–100. Five cases.

Full bench: 1291 tests, 0 failed, 1018 of 1018 expectations verified.

---

## 0.98.0 — 26 September 2026 · library 0.123.0

`piega_da_schizzo`, the fifth sheet-metal operation: a sheet bent along a
line drawn on one of its faces. `InsertSheetMetal3dBend` with the line's
sketch selected was born at the first try; the fixed face is not needed.
Measured on a flat 100 x 40 x 2 sheet (R 2), line across at x = 50, 90°:
volume unchanged, and the bounding box matching the hand calculation — 46
of flat (50 - R - T), the 4.712 bend, 49.288 rising from y = 2. A bend
adds no sheet, it moves it: volume and flat length are checked unchanged,
and the box before and after tells which side SOLIDWORKS kept fixed. The
sheet bends toward the side the face looks at.

The jog was not: `IModelDoc2.InsertSheetMetalJog` (it returns nothing)
created nothing in seven selection variants, and there is no jog among the
feature definitions. It is in the TODO with the variants written down.

Full bench: 1224 tests, 0 failed, 961 of 961 expectations verified.

---

## 0.97.0 — 26 September 2026 · library 0.122.0

`flangia_spigolo`, the third sheet-metal operation of seven (entry GP18):
a 90° flange on a border edge of an existing sheet — how a box gets closed,
with bends on edges in different directions, which `flangia_base` from one
profile cannot do.

Probed before writing, ten attempts (`Controlli/FlangiaSpigolo`).
`InsertSheetMetalEdgeFlange2` returns null, with no exception, with null
sketches, with its seventh parameter (`FlangeOffsetDist` — the length) at
0 or 20, with typed `Edge[]`/`Feature[]`; it creates an EMPTY flange (no
volume added) with a line on a plane perpendicular to the edge. It creates a
real one only with a **rectangle on the flange plane**, the base on the
edge: the end face of the sheet. Measured with profiles 20 and 30 high:
`OffsetDistance` reads 22 and 32 (profile + T, at the outer virtual
sharp), and the flat length grows by (L - R - T) + BA - (R + T) to the
thousandth. SOLIDWORKS replaces the sketch it is given with its own, and
the length follows the profile: `ModifyDefinition` on `OffsetDistance`
answers true and changes nothing.

The operation finds the edge by signature (E19), checks it is straight and
lies between the broad face and an end face as wide as the thickness,
draws the rectangle from the edge along the broad face's outward normal,
and compares the volume added with the hand calculation; a flange born
empty is refused. Three cases: the 25 mm flange that lengthens the flat
by 21.712, and its two refusals (nothing straight left, not a border edge).

And `orlo`, the fourth: a hem, closed or open. `InsertSheetMetalHem2` works
on the selected edge and was born at the first try — no sketch. Measured
on the harness before writing: the open hem is the flange formula with a
180° bend and an inner radius of half the gap, three measurements out of
three to the thousandth; the closed one sits 0.057 above the same formula
with radius 0, like a 0.05 mm inner radius, measured with thickness 2 only
— so for the closed hem the hand calculation is reported, not trusted.
`SpigoloDiBordo` is extracted: flange and hem find their edge the same way.
Three more cases (open, closed, open without gap refused).

The probe also found that a sheet metal part's side is not one face: it is
split per straight segment, each as wide as the thickness — so side edges
are border edges too, and the first «not a border edge» case was wrong.

Full bench: 1216 tests, 0 failed, 955 of 955 expectations verified.

---

## 0.96.0 — 26 September 2026 · library 0.121.0

**The regression has no silent case left.** `r05` — a 2 mm chamfer, then
the plate brought to 1 mm — was the last one, and it was written off as
SOLIDWORKS' own: the chamfer no longer fits, and SOLIDWORKS does not fail,
it rebuilds clean and eats a millimetre of width. The sign was already
there and thrown away: the BASE feature loses a whole face (the one the
chamfer now takes entirely), and `LavorazioniCambiate` wrote it in a note
with the wrong explanation («a blind feature became through»).

Now `modifica_quota` refuses, names the chamfer or fillet, and puts the
old value back when a feature that is not a cut loses faces and a chamfer
or fillet comes after it in the tree. Cuts stay out on purpose: a pocket
that loses its bottom has become a through cut, and that may be the point
(the note says so, now worded for cuts). `RimettiLaQuota` is extracted:
the aperture guard of E18(c) and this one put a value back the same way.

It came out of entry E20. The probe (`prova0089 storia`: two R8 fillets,
then width 60 -> 10 or thickness 20 -> 6) found **no feature in error** in
either case — a per-step error count would have caught nothing. E20
closes as not needed in its written form; this guard is what it found.

Regression (19 cases): **17 hold, 2 break and say so, 0 silent** (it was
12/0/4 of 16 on 15 September). New cases: the thickness below the fillet
radius, refused and restored, and its twin at 12 that fits; and E19's
«two candidates» branch — a plate and its copy touching, the same edge
twice — refused with «non scelgo io».

Full bench: 1194 tests, 0 failed, 939 of 939 expectations verified.

---

## 0.95.0 — 25 September 2026 · library 0.120.0

Edges are found again by signature (entry E19). Faces have had this since
10 September (and the persist ID since E6); edges had only the refusal: a
plan read once — «two holes, then fillet the front right corner» — stopped
at the fillet, because edge uids are ranked by length and the fillet carried
the uid read before the holes. Now, when the uid no longer matches, the
library looks for the ONE edge with the same length at the same point, uses
it, and says so in `riancoraggi` («E11 adesso si chiama E15»). Never the
nearest: an edge shortened by a chamfer has another length and another
midpoint, and the plan is refused. Two candidates are an ambiguity, and it
is said. It applies wherever an edge is taken by uid: fillet, chamfer,
variable fillet, cosmetic thread, the direction of a linear pattern, the
anchor.

**What writing the case found.** With library 0.119.0 the case PASSED
without finding anything: `LunghezzaMm` measured the chord between the
endpoints, and the rim of a hole — a closed edge — was «0 mm long» in the
state. Holes always ranked last and never renumbered anything; a reader of
the state saw a 37.7 mm circle described as an edge of length zero. The
length is now the real one (`ICurve.GetLength3`, already measured in the
weld perimeter), -1/null when unreadable. And `FirmaCombacia` now compares
an edge's length too, not only its midpoint.

The bench learns `-Famiglia <name>` (one folder of declared cases, so a new
case can be seen failing before the code is written) and `uid_dal_passo` in
a step (selectors and signatures from the state before step N, as the
bridge does). New family `ritrovare`, two cases: the fillet that finds E15
and lands on the right corner (the centre of mass says which), and its
twin refused on an edge shortened by a chamfer.

Full bench: 1180 tests, 0 failed, 929 of 929 expectations verified — the
real length changes the order of curved edges, and no existing case moved.

---

## 0.94.0 — 25 September 2026 · library 0.119.0

The anchor on `estrudi_su_faccia` (entry E18). There are no `x_mm`/`y_mm`
here: the point the anchor holds is the CENTRE of the contour, and it
applies to one contour only, round or an axis-aligned rectangle. A circle
gets its diameter dimensioned and the anchor on its centre; a rectangle of
four lines (corners already merged under `AddToDB`, as measured on the
slot) gets horizontal/vertical relations, two dimensions and a centre built
with a construction diagonal, like the pocket. Any other contour is refused
before touching the part, saying which contours the anchor accepts. Measured
on the harness and in three bench cases: a round boss 30 mm from the right
side stays 30 mm from it on a plate that grows, a rectangular boss stays
centred, a triangle is refused.

And `foro_guidato`, the last piece of E18: each position takes its own
`ancora`, all of them or none (the holes share one sketch, and one free
point leaves it under-defined). HoleWizard builds the point sketch; after
the positions are placed the library re-enters it, anchors each point, and
requires the sketch fully defined after the last one. The hole radius is
known only from the table, so a distance smaller than the radius is caught
once the hole exists, and the hole is removed. Measured on the harness:
four M8 holes 20 mm from the edges go to y -40 -40 100 100 and x -80 -80
140 140 when the plate grows. Regression `r01` now builds with the anchor
(same expectations); `r01b` is its absolute twin.

`TogliIlTaglio` now says «l'ho tolto» instead of «il taglio l'ho tolto»,
since it also removes a boss or a guided hole.

Regression (`banco.ps1 -Regressione`, 19 cases): **17 hold, 1 breaks and
says so (r16), 1 breaks silently (r05, SOLIDWORKS' own)** — it was 15/1/2 of
18 with library 0.116.0. The finish line written in E18 («silent down to 1,
r05 left») is reached, and E11 (rollback as oracle) closes as not done by
its own criterion: silent + refused without criterion = 0/18 with r05 left
out. Full bench: 1169 tests, 0 failed, 920 of 920 expectations verified.

---

## 0.93.0 — 25 September 2026 · library 0.118.0

### The number on the drawing is the model's? (entry Z-C2)

«A dimension comes from the model, it cannot lie» holds until someone
writes over the number. Measured the three ways the interop offers:
`SetOverride(true, v)` (read back by `GetOverride`/`GetOverrideValue`),
`SetText(0, "…")` (which writes the PREFIX and switches the value off), and
`ShowDimensionValue = false`. Two reads catch all three, including an
override that happens to equal the model value. The drawing state now counts
`quote_numero_non_del_modello` per view and in total, lists up to ten with
how, and warns. A C# bench group (`numeri`) forges three dimensions and
expects three; on the untouched drawing, zero.

### The slot sketch is defined, and the slot takes the anchor (entry E18)

`asola` drew four entities and nothing else: no relations, no dimensions,
so a finished slot could not be corrected. Now the sketch gets four
tangencies, equal radii, a construction line between the arc centres with
its length dimensioned, the radius dimensioned, and horizontal/vertical when
the slot is at 0 or 90 degrees — measured: change the radius and the centre
distance and the slot stays a slot, volume exact. `asola` takes `ancora`;
an oblique slot dimensions its angle against the anchor's first edge with
`AddSpecificDimension` (angular), because `Extension.AddDimension` returns
null on that pair and `AddDimension2` is forbidden. Without an anchor an
oblique slot is free to rotate, and the response says so. Three bench cases:
the slot corrected through its dimension, the anchored slot, the anchored
oblique slot keeping its 30 degrees on a plate that grows.

---

## 0.92.0 — 25 September 2026 · library 0.117.0

Zero is not «I don't know» (entry Z-C8), and two guards that were looking
elsewhere (Z-C16, Z-C18).

### Counts read from SOLIDWORKS no longer answer 0 when the read fails

A function that reads the CAD and returns `0` from its `catch` makes a
statement: «this part has zero bodies», «zero mm³ were removed». A reader
has no way to tell the read failed. The scanner found 21 such places; a
read-only agent traced every caller and classified them: 12 real defects,
4 true zeros, 5 not reads at all. Then a third syntactic form the scanner
had never seen (`int n = 0; try { … } catch { } return n;`) added ten more.

- `VolumeMm3` is now `double?`, and the compiler listed its 44 call sites;
  `ContaCorpiSolidi`, `FacceDellaFeature`, `ContaFacce`, `ContaFogli`,
  `ContaViste`, `ContaQuote`, `ContaFeature`, `ContaPiani`, the note, centre
  mark, trim-tree, draft-face and triangle counts return -1.
- Callers compare only values that were read. A volume that cannot be read
  **before** an operation is a refusal (nothing has been touched yet); one
  that cannot be read **after** is not: the field is null and the response
  says the material checks were not done.
- The document state writes `null` for a body volume or a face area it
  could not read, instead of leaving the key out (which also meant zero);
  `annulla` no longer redoes a step on an unreadable volume.
- In passing, real false outcomes this closes: a failed read after a note
  or a set of centre marks produced «nothing was created» with the thing
  in place; a failed face count made `sopprimi_istanza` revert a correct
  change; two failed volume reads made a copy look «identical to before».
- `Controlli/zero-o-non-lo-so.js` knows the third form, and with all sites
  fixed or marked `ZERO-E-ZERO:` with a reason it becomes a pre-push guard,
  seen failing on a planted site before being trusted.

### Body numbers are not stable, and three cases were green by symmetry

Measured again: in one run out of three, the copy of a plate was `C1`.
`sottrai_un_cubo_dall_altro`, `ingombro_di_due_corpi_lontani` and
`due_corpi_non_si_esportano` now name the body (`@corpo(nome=…)`) and check
what tells the two apart — the centre of mass of what remains, the position
of the box.

### The union guard compared two different enums

`combina_corpi` refused «the union left two bodies» by comparing
`SWBODYADD` (15903) with `swCombineBodiesOperationAdd` (0): it could never
fire. Measured first: with two bodies that do not touch, SOLIDWORKS does
not create the feature at all, so the earlier refusal answers; the new case
`lim_unione_di_corpi_che_non_si_toccano` pins it, and the guard now looks at
the right value.

---

## 0.91.0 — 25 September 2026 · library 0.116.0

The anchor (entry E18, parts a and b). `x_mm`/`y_mm` are absolute model
coordinates: a hole «20 mm from the edge» on a plate that grows stayed
where it was — clean rebuild, no error, wrong part. `taglio_cilindrico` and
`taglio_rettangolare` now take `ancora`:

```json
"ancora": { "x": {"spigolo_uid": "E7", "distanza_mm": 20},
            "y": {"centrata_fra": ["E3", "E5"]} }
```

For each face axis, either a dimension from the profile centre to a
straight edge, or the centre kept halfway between two opposite edges. Both
axes are required. `x_mm`/`y_mm` stay: they say where the point is, the
anchor says what holds it there, and they must agree.

The rules come from measuring and from an adversarial review written before
the code: the edge carries its signature, is straight, lies in the face and
runs along the other axis, and is not the boundary of a chamfer or fillet;
`AddToDB` is on only around each creation; the sketch must come out fully
defined with no dangling, over-defining or broken relation; after the cut a
movement test (each anchor dimension +0.1 mm, rebuild, restore) and, if it
fails, the cut is removed and the part checked back to what it was.
Centring is per axis (a centre line with its ends on the two edges' lines,
perpendicular, the point at its middle): centring between the edges'
midpoints moved the hole in y at creation.

Measuring found three things the design did not:

- a sketch point or segment is not an `IEntity`: the cast returns null
  silently, and the centre line came out with 2 relations out of 4 — the
  relation count caught it;
- `IModelDocExtension.AddDimension` returns null for a sketch point plus a
  model edge, in all four directions, on three faces. Horizontal/vertical
  dimensions work, chosen from the edge direction in the sketch;
- the expectation of regression case `r01` assumed the plate grows
  symmetrically; it grows on one side (entry E2). Corrected to
  [-40, -40, 100, 100]; with the old numbers it could never hold.

Bench cases: an anchored hole on the front, side and back faces; a pocket
centred between the sides; eight refusals and the chamfer-edge refusal.
Regression: `r03` now builds with the anchor (same expectations), `r03b` is
its absolute twin, `r19` is an anchored hole. The bench attaches signatures
for the edges inside `ancora` too, as the bridge does.

---

## 0.90.0 — 25 September 2026 · library 0.115.0

The dimensioned drawing, looked at (entry GT11). Asked to see a drawing,
the bridge produced one — with the bench all green — that had four defects
nobody's expectations asked about. All four are fixed and have a bench case.

### The numbers sat on the extension lines

`quota_in_tavola` placed the text of each dimension at the middle of the
edge as computed on the sheet, and that computation was wrong twice: it
used the transposed rotation (in a rotated view the Y came out mirrored:
the chamfer strip of the top view at y 70.7 instead of 145.7) and it added
the view position instead of the transform's translation (the same only
when the part is centred on the origin: a 10 mm plate extruded from 0 was
placed 5 mm too high). The vertical texts ended up on or beyond the
extension lines. Horizontal ones survived because the plate was centred in
X. Points now go through `MathPoint.MultiplyTransform`.

### Holes are dimensioned

Every view that shows holes along their axis gets one hole callout per
diameter, with the count in front («4× Ø8 PASSANTE» — prepending to the
existing prefix, because replacing it erased the diameter symbol), and the
hole positions from the two reference edges, left and bottom, in parallel:
15 and 105, 15 and 65 on the demo plate. Each position dimension is
attached to the hole nearest to its side (the first attempt ran an
extension line across the whole part) and re-read: it must equal the sheet
distance divided by the scale, otherwise the answer says it may measure to
the rim instead of the centre. Measured first: circle plus edge with
`AddHorizontalDimension2`/`AddVerticalDimension2` measures from the
centre; with `AddDimension` nothing is created.

### Chamfers get the chamfer dimension

Edges of a chamfer face are no longer dimensioned as lines (the 7.071
hypotenuse), and neither are the edges shortened by the chamfer (the 75
next to the 80, the 5 next to the 10). Where the chamfer is seen in
profile, `IDrawingDoc.AddChamferDim` writes «5 x 45°». Its first dimension
is the angle, not a length: the answer reads both by type
(`swDimensionParamType_e`) and reports side 5, angle 45, face width 7.071.

### No dimension twice in the drawing

Before placing anything, the operation reads the dimensions already on the
sheet and the edges they are attached to, and skips the same model extent
(axis and interval) in another view: the 10 and the 120 now appear once.
The answer lists what was skipped and why. A view with nothing left to
dimension is refused, with the reasons counted.

### Also

- Dimensions stay inside the sheet frame: a band that would leave the
  border moves to the other side.
- `vista` accepts the drawing-state identifier (`V2`): view names depend on
  the SOLIDWORKS language.
- Bench selector: `x`, `y`, `z` read the coordinates of `punto_mm`, so an
  edge can be named by where it is (`@spigolo(asse=Y,x~=-100,z~=20)`).
- Entry Z-C6, second half: the bench writes the names of its green cases
  into `ultimo.json` (`casi_verdi`), and a contract check requires every
  catalogue operation to have a green case in the last run, with
  `quote_dal_modello` as a declared exception (entry Z-E7) that is itself
  checked for staleness. **On its first run it found three operations with
  no case in a normal run**: `foro_guidato`, `estrudi_su_faccia` and
  `raccordo_spigoli` appeared only in regression cases (which run only with
  `-Regressione`) and in hand-written plans. Yesterday's recount had counted
  the regression cases as declared cases. Three new cases, with values
  measured first and equal to the hand calculation (the M8 tapped hole
  removes the 6.8 pilot, the clearance hole the 9).
- A contract check for the bench rule "every step measures something": a
  case step without expectations stops the whole case as a bench error after
  ninety minutes — it happened to the new drawing case on its first run.
- And one for the bench's twin rule as the bench applies it: the twin is the
  FIRST successful step of the same operation, and it must share at least
  one argument value with the refusal (`Attese.SiSomigliano`, the same
  function). The second run stopped on it: the refusal on `V9` shared
  `quante` with the second step and nothing with the first. Seen red on the
  real case before being trusted.
- Rule of the house from now on: every change to the drawing commands ends
  by generating the demo drawing as an image and looking at it against the
  dimensioning rules.

---

## 0.89.0 — 25 September 2026 · library 0.114.0

Axes are checked for identity, and writing the missing bench cases for the
weldment operations (entry Z-C6) found that two of them had never done
anything. 76 runnable operations, 73 in the served catalogue: `fazzoletto`
leaves it.

### `asse_uid` is checked for identity, not just existence (entry E4, the axes)

Until library 0.113.0 an axis in the document state was `{uid, tipo}` and
nothing else. If someone rebuilt it elsewhere under the same name between plan
confirmation and execution, `ripeti_circolare` patterned around the new one,
silently. The state now carries a point on the axis line and its direction
(from `IRefAxis.GetRefAxisParams`), the bridge attaches them to each step as a
signature, and the step refuses when the axis has moved: parallel within one
degree and the line within the point tolerance, otherwise «L'asse si e' spostato
di N mm». It compares the LINE, not the start point, because an axis can be
redrawn longer without changing. Two bench cases: the twin with a point halfway
along the axis (six instances), and the refusal with the axis 30 mm away.

### `taglia_membri` never cut anything

It passed the bodies as `object[]`. SOLIDWORKS answered with a feature named
«Accorcia/Estendi1» that **never entered the tree**; the T stayed identical and
the operation reported `corpi_tagliati: 1`. Measured on a T of two 80x80x5
tubes: `object[]` gives the phantom, `Body2[]` and `DispatchWrapper[]` give the
real trim, −58798.23 mm³ = 40 mm × 1469.956 mm² of tube section, to the
hundredth. `distinta_taglio` already passed `Body2[]`, which is why it worked.

A guard now requires the returned feature to be in the tree and the volume to
change. It is not only for the fixed bug: with typed arrays, bars that do not
meet **still** produce the phantom, and the operation now says so instead of
confirming. The answer carries `variazione_volume_mm3`.

### `cordone_saldatura` never created a bead, and now takes two faces

It passed every face of two sets of bodies to `InsertCosmeticWeldBead2`, which
returned null in every combination tried: faces as `object[]`, `Face2[]` or
`DispatchWrapper[]`, all faces or only the two that touch, geometry mode or
path mode on the edges. The bead is created only by the old selection-based
`InsertCosmeticWeldBead`, and only with **one face per side** (three against
one: null). And SOLIDWORKS does not check contact: between two faces 80 mm
apart it creates a bead anyway, with the size read back as 40 instead of 4.

So the schema changed: `da_faccia_uid` and `a_faccia_uid` instead of the body
sets (the old `tipo` field, never read by the code, is gone too). Before the
call the operation refuses faces of the same body, parallel faces (a bar's end
resting on another is contact, but not a fillet weld) and faces that do not
touch: an edge of one must lie on the other at three points. After the call it
re-reads the bead size from the feature. The length returned is the shared
edge, measured on the geometry: 68 mm on the T, the flat side of the tube.

### `fazzoletto` leaves the catalogue

`Provata = false`: no gusset was created in sixteen variants over the three
gusset calls — selection with and without marks, both orders, inner, outer and
both-sides thickness, triangular and polygonal profile, typed face arrays, on
the T and in a mitred frame corner. Entry GP20 of the TODO says where to start.

### Bench: bodies by name

Three structural members created in the same order came back as C1 = the short
bar in one run and C1 = the long bar in the next. A case that writes `C1` means
a different body depending on the run. The bench has a new selector,
`@corpo(nome=...)`: the body name («... 80X80X5(2)») follows creation order.
The parentheses in the name are handled (the selector opens at the first and
closes at the last). Covered in the bench self-test and in the contract suite.

### Also

- Bench cases for the last orphans of Z-C6: the trim with its refusal, the
  bead with two refusals, the end cap (+42250 mm³ exactly: two 65×65×5 plates
  inside the tube), and the gear at the undercut boundary — 17 teeth is made
  (2/sin²20° = 17.097, rounded down), 16 is refused.
- The `ruota_dentata` description declares that limit, and no longer says the
  server computes the profile (the library does).
- Two probes (`NuovaParte`, `CorpoRotto`) had not compiled since 0.86.0 —
  `Environment` ambiguous between System and the SOLIDWORKS interop — and
  nobody noticed for three versions because the probe build is run by hand.
  `build.ps1` now compiles every probe (13 seconds), and `FlangiaSpigolo` is
  registered.
- The README check read numbers written in words, but «settantatré» (with the
  accent, as it is spelled) came back as "not a number" and was skipped
  silently. Fixed, with a check that the reader recognises it.
- **CI had been red for five pushes** (0.85.0 to 0.88.0), always on the same
  eight checks, none of them a real defect: four looked for the served
  catalogue only in `bin\` (absent in a clean clone; the committed copy in
  `Strumenti\` is the same file), three needed the bridge executable and
  reported red instead of "not runnable here" where SOLIDWORKS is absent,
  and one counted a sentence in the bench README that *tells* the story of
  the old `casi/forma/ 5` row, a folder that is not in the repository by
  design. All eight now read the committed catalogue, use the existing
  "not here" rule, or anchor the row pattern at the start of the line.
- A contract ratchet for entry Z-C16: declared cases that cite a body by
  number in a `_uid` field cannot grow (12 today, seen red at zero first;
  the two refusals on a `C9` that exists in no order are excluded).

Bench: 1016 checks, 0 failed, 795/795 expectations, first run on this
library. Contracts: 370. Installer tests: 17/17.

---

## 0.88.0 — 25 September 2026 · library 0.113.0

The six probes committed with 0.87.0 ran on a free SOLIDWORKS the same night,
and all six turned into code in this version. 76 runnable operations, 74 in
the served catalogue (three new: `flangia_base`, `sviluppo_dxf`,
`schema_di_quote`); two new families, `lamiera` and `quote`.

### `modifica_quota` refuses an opening that breaks out of the material (entry E18, part c)

Regression case r16: a centred 200 mm plate shortened to 150 shrinks **from one
side only**, and a 120 mm slot in the middle comes out through the flank. Clean
rebuild, one body, no error — and a closed slot has become an open notch, which
is a different part. `modifica_quota` now counts the closed openings of planar
faces (inner loops) before and after; if they drop, it **writes the old value
back**, rebuilds, re-reads the volume, and refuses with "esce dal materiale".
Two new bench cases: the refusal, which also requires the volume to be exactly
what it was, and its twin at 180 mm, where the slot stays inside.

### The busy-SOLIDWORKS guard could never fire (entry Z-C3)

The executor refused a plan when `ISldWorks.CommandInProgress` was true. With a
real modal open — a SOLIDWORKS message box, opened from another thread and
closed by the probe — `CommandInProgress` stayed **false** in three rounds out of
three. The main window, instead, is disabled, and `IsWindowEnabled` says so six
times out of six in zero milliseconds, without going through COM. The guard now
checks that; a new check in the bench's `porta-del-cliente` group opens a real
message box, asks the bridge for a plan, requires the refusal and an unchanged
part, closes the box, and requires the same bridge to work again.

### Two catalogue descriptions that claimed to be the only one (entry Z-C15)

A new contract check fails when a description says "the only way to", "the only
operation" and its family has other operations. It went red on two before the
fix: `quote_dal_modello` ("the only way to dimension", while `quota_in_tavola`
dimensions) and `estrudi_su_faccia` ("the only operation that adds", while
`incidi_testo` embosses and `combina_corpi` unites) — the second found by the
check, not looked for. Both now say what is true; `quote_dal_modello` says it
imports nothing out of process on this installation (measured 18/9) and points
to `quota_in_tavola`.

### `documento` can close (entry Z-B1)

`azione: "chiudi"` closes an open document by path or title, and **refuses one
with unsaved changes** — there is no discard option on purpose. `CloseDoc`
returns void, so the bridge reads back that the document is gone. Two bench
checks: the bench's own unsaved part is refused and stays open; a freshly
opened file closes.

### Not installed is not "not open" (entry H16)

One `catch (COMException)` gave the same sentence for both. Now
`MK_E_UNAVAILABLE` says "non risulta aperto" (unchanged, word for word: the
installer looks for it), while `CO_E_CLASSSTRING` and `REGDB_E_CLASSNOTREG` say
SOLIDWORKS is not installed or not registered — and **must not** contain "non
risulta aperto", or an install on a machine without SOLIDWORKS would exit 0.
Six contract checks, by HRESULT, without SOLIDWORKS.

### File properties show the version (entry H14)

Right-click → Properties → Details on the bridge and on `Installa.exe` said
0.0.0.0. An MSBuild target now writes the version from `VERSION.txt` before
compiling, for every way of building the project; a contract check reads it
back from the file.

### Four installer defects (entry H15)

- The uninstaller removed an `Oce.Solid.Agent.pdb` that no version ever
  installed (checked in git history). Install and uninstall now share one list.
- A client config locked by another program was reported as "not valid JSON",
  and a failed write left a `.parziale` next to the real file. The lock is now
  detected before anything is touched, and the temporary file never stays.
- Silent mode now warns when the client is open, like the window did.
- Backups are pruned to the last three per file; reinstalling says "aggiorno
  dalla X alla Y" or "reinstallo la X", reading the version embedded in the old
  executable from its bytes (loading the file would lock it).

`Controlli/prova-installatore.js`: 17 checks, 0 failed, including half of the
"client open" scenario (a child process holding the file exclusively).

### The bench says two numbers (entry F15)

It printed "catalogo: 73" while the served catalogue has 71; the difference
was once explained, plausibly and wrongly, as the two library copies diverging.
It now prints `eseguibili: 73 operazioni (a catalogo: 71)`, and the report
carries both (`eseguibili`, `a_catalogo`; `catalogo` kept for readers).

### Operations that no bench run touched (entry Z-C6)

New declared cases for `estrudi_profilo` (a tube must have its hole: 86 393.8
mm³, not 282 743), `estrudi_sezione` (a slot's length is its overall length),
and `imposta_foglio` + `vista_modello` + `esporta_pdf` in one drawing (scale read
back from the sheet, one view counted two ways, pages counted in the written
PDF). New family folder `casi/estrusioni/`, registered — a contract check had
flagged it as mute before the bench ran.

### Sheet metal, from a measurement (entry GP18)

The probe measured the flat pattern of an L-flange to the hundredth — 80.712
mm, the hand calculation with the sketch on the inner faces — and found three
traps: `SetBendState` returns 0 and `GetBendState` reports flattened while the
body stays folded (flattening means unsuppressing the flat-pattern feature);
and `ExportFlatPatternView` on a never-saved part opens a Save As dialog and
the COM call never returns. Two operations are built on that:

- `flangia_base`: an open profile on a base plane, thickness, bend radius,
  depth. The answer carries T, R and K **read back** from the part, the flat
  length measured by SOLIDWORKS next to the one computed by hand in both
  conventions, and **which convention matched** (`i_punti_sono_le_facce`):
  the side the thickness goes to follows the direction of the points, and the
  two differ by twice the thickness per bend. A straight run shorter than the
  bends that eat it is refused with the number.
- `sviluppo_dxf`: refuses a part without a file before calling, writes the DXF,
  **reads it back**, and refuses if its long side is not the measured flat
  length.

Four bench cases, among them the same L given in reverse order, which must say
"esterne" and 76.712.

Two defects of the first draft, both found before they could ship. The second
bench run went red on all four cases with "«punti» wants at least two points":
arguments arrive as the caller's serializer lists, and an `as object[]` cast
gave null on a correct L — the library already had the rule (`HaElenco` reads
any `IEnumerable`). And a direct harness run before the third bench found that
the DXF header lies: `$EXTMIN/$EXTMAX` said 279.4 mm — eleven inches, the DXF
template's sheet — for an 80.712 mm flat pattern. The extents are now computed
from the entities, and come out [40, 80.712].

### Dimensions on the model from DimXpert (entry Z-H3)

`schema_di_quote`: three planar datum faces by uid (primary, secondary,
tertiary), selected with marks 1/2/4, and DimXpert's auto-dimension scheme
from the core interop — no add-in. The answer lists the values the new
dimensions carry; `AutoDimensionScheme`'s own answer is reported under its
name, because it is false even when it dimensioned. Bench: a plate with a hole
must come out with exactly 10, 20, 30, 50, 60, 100; two datums are refused.

### The PDF is read back (entries GT3-bis, Z-C9)

`esporta_pdf` takes `cerca`, a list of strings the PDF must contain, and says
which are there (`trovati`) and which are not (`non_trovati`). SOLIDWORKS
writes drawing text as glyph indices behind `/ToUnicode` maps; a search in the
bytes finds nothing. `TestoDelPdf` inflates the streams and decodes the maps —
checked on a real export against `pdftotext`: same text, and a string that is
not on the drawing is never found. The bench case writes a note and requires
it found and an absent string not found, in the same run.

### Dimensions left hanging (entry Z-A2)

`IsDangling` stays false on a drawing dimension whose edge was suppressed. The
drawing state now counts, per view and in total, dimensions attached only to
edges or vertices the view no longer shows (`quote_orfane`,
`quote_orfane_totali`, and `attenzione_quote` when there are any); null means
"cannot tell", not zero. A bench group builds the probe's part, dimensions the
block with `quota_in_tavola` (0 hanging), suppresses the block and requires
exactly 1 plus the warning.

### A slot is a polygon, and now the catalogue says so

The first bench run of this version went red on a new case: a 60 × 20 slot from
`estrudi_sezione` measured 11163.281 mm³ against 11141.59 computed by hand.
The part was right: the round ends are 24-sided polygons on the circumscribed
circle, on purpose, so the slot is never narrower than asked (it comes out
20.043 wide). The expected value was wrong, and the description did not say
it; now it does.

That run died at 535 checks for a reason outside the code: the bench's own
`Tee-Object` to `Banco\ultimo-giro.txt` on the network drive failed with a
semaphore timeout. It was also the run that showed a missing
`perche_questa_tolleranza` in a new case as a bench error — after an hour. A
contract check now catches that in a tenth of a second, before the bench.

The third run found one more bench error of the same kind: the refusal
"sviluppo_dxf on a never-saved part" and its twin called the operation with
identical arguments ("gemella finta": the refusal depends on the save state,
not on an argument). The pair now differs by `percorso`, with a note saying
that is not what decides; and a second contract check compares every refusal
with its twin before the bench — seen red on the copy, then refined when it
flagged a case that is legitimately its own twin. 365 contract checks.

---

## 0.87.0 — 24 September 2026 · library unchanged (0.112.0)

### A seventh tool: the document itself (entries Z-B1, Z-B2, Z-B5, Z-B6)

With no document open the bridge answered "there is no open document" and
stopped there: starting a part from scratch needed a person to click File >
New. The acceptance test of the whole project — a model drawing a real part
from a workshop request — could not even begin on its own.

`documento` creates an empty part or assembly from the default template,
opens a SOLIDWORKS file from disk, lists the open documents and which one is in
front, and brings one to the front. It is a **tool of the bridge**, not an
operation of the catalogue, on purpose: catalogue operations are steps of a
plan that works *on* one document, held from start to finish and wrapped in one
undo record. Changing the document is not such a step — and what was looked at
lives in the bridge. Every time `documento` changes the document in front, the
bridge drops what had been looked at and its signatures together (the rule of
entry Z-A1), and `disegna` answers "I have not looked at any part yet".

The traps written in the entries are handled where they bite: a wrong template
does not fail, it creates a document of the wrong **type**, so the type is read
back after creation; `OpenDoc6` succeeding does not mean the requested file was
opened, so the path is read back; `ActivateDoc3` succeeding does not mean it is
in front, so the active document is read back; and a file opened read-only —
usually a colleague has it open on a network share — is opened by SOLIDWORKS
**without asking**, so the bridge always says so.

Seen through the client's door, with a real SOLIDWORKS: ten new checks in the
bench's `porta-del-cliente` group, all green, the bench re-reading from COM
what is actually in front instead of trusting the answer. Two are red by
construction and fire: right after `nuovo`, `disegna` refuses until the part
has been looked at; `apri` on a file that does not exist refuses, and the
document in front does not change. The read-only case uses the file attribute:
the colleague-on-the-share case cannot be built inside one SOLIDWORKS session.

The first draft of that bench case saved the new part through
`GetOpenDocumentByName(title)`, which returns null for a document that was
never saved: the save was never attempted, and it reported `e=0 w=0`. It now
keeps the reference it got when the part was created.

`tools/list` now returns **seven** tools, asserted exactly; `documento`
declares itself neither read-only nor idempotent (`nuovo` twice makes two
documents) and not destructive (it closes nothing). 353 contract checks.

Bench on this version: 909 checks, 0 failed, 715 of 715 expectations verified.

### Six probes written while the bench held SOLIDWORKS — not run yet

Each asks one question and carries its red side in the same run; each is built
by `Controlli/costruisci-controlli.ps1`. They run on a free SOLIDWORKS, after
this commit, and their answers decide what the next library version contains.

- `Controlli/Lamiera` (GP18) — does the flat pattern of an L-shaped base flange
  match the length computed by hand from T, R and K read back from the part,
  within 0.5 mm; is the volume conserved; does the DXF come out?
- `Controlli/DimXpert` (Z-H3) — reached from the core interop, without any
  add-in, does an auto-dimension scheme add annotations and recognise the hole?
- `Controlli/Orfana` (Z-A2) — does `IAnnotation.IsDangling` turn true when the
  edge a drawing dimension sits on is suppressed in the part?
- `Controlli/TestoNelPdf` (GT3-bis) — does drawing text, typed and linked to a
  custom property, come out of the PDF as text: raw bytes, inflated streams,
  and `pdftotext`, with a string that is not there as the red?
- `Controlli/Modale` (Z-C3) — with a real modal open, do `CommandInProgress`
  (the guard in use today) and `IsWindowEnabled` of the main window tell the
  truth, and does the COM read come back at all?
- `Controlli/SeguiIlBordo` (E18 a, b) — does a hole dimensioned to two model
  edges follow the corner, and does a point at the middle of a construction
  line between two edge midpoints stay centred, when the plate grows?

---

## 0.86.1 — 24 September 2026 · library unchanged (0.112.0)

### Minimum wall thickness, measured end to end: 2.00 mm against 2.00 by hand (entry Z-H1)

Same method as the cantilever for Simulation: a part whose answer is known
without a computer. A plate 100 × 60 × 2 mm with, on half its length, a block
10 mm high — two extrusions from the top plane, no face to pick. Minimum wall:
2.0 mm. `Controlli/Spessore/`, run on a free SOLIDWORKS after the bench.

Utilities was not loaded; `LoadAddIn` on `sldUtils\SwLoaderSw.dll` loaded it
for the session (answer 0), with the owner's go-ahead. Then
`IUtilities.GetToolInterface(7)` → `IThicknessAnalysis`:

```
  threshold   answer   minimum   maximum   critical faces
  0.003 m        0     0.0020    0.1000         2
  0.012 m        0     0.0020    0.1000         3
  0.0015 m   12009       -         -            -
```

Five things reflection could not have said. **The API works in metres**, in
and out. **A threshold below the minimum does not give "zero critical faces"**:
the analysis does not run and answers 12009,
`gtWarningTckThinAnalRangeBelowMaxThick` — a warning meaning "nothing below this
threshold". The probe's first draft had written its criterion as "zero faces"
and went red, which was the right thing to do: it stopped instead of closing on
a guess. **Right on the minimum** (2.00, 2.01 mm) it answers 15. **An absurd
threshold poisons the next call**: after a 3-metre threshold, the next call at
3 mm answered 15 instead of 0. **Critical faces grow with the threshold**, which
the probe keeps as a monotonicity check. A thirteen-threshold sweep is behind
`Spessore.exe --esplora`.

What remains is the operation itself, in the library, with its bench case on
this very part.

---

## 0.86.0 — 24 September 2026 · library 0.112.0

### The library number moves, because its bytes did

The fallback removal below changed `OperazioniProfili.cs` — behaviour, not
just comments — while `AssemblyVersion` still said 0.111.0. A green bench on
those bytes would have described a different library under the same number,
which is exactly entry K14. It went to **0.112.0**, and the bench ran again.
Nothing caught it: the critic of the RAG re-read did. The note for 0.111.0 was
missing from `AssemblyInfo.cs` too, and is now there.

### The module probe could not tell a working module from a missing one (entry K20)

`Controlli/moduli-di-solidworks.ps1` decided "installed but cannot be created"
by counting the registered coclasses of a module's interop and by listing
`HKLM\SOFTWARE\SolidWorks\AddIns`. On the day Simulation ran end to end (S3),
the same criterion said about Simulation exactly what it said about Utilities
and DimXpert: 80 coclasses, 0 registered, and absent from every add-in key it
read. Four TODO entries rested on that line. A check that gives the same
answer for a module that works and one that does not measures nothing, and
nobody had ever seen it tell the truth about a known case.

It now resolves each module the way Windows does — ProgID → CLSID →
`InprocServer32` → file — looking in `HKCU\Software\Classes` **before** HKLM,
reading `CodeBase` for .NET add-ins, and resolving paths relative to the
SOLIDWORKS root. It carries **two positive controls**: Simulation (a Dassault
module listed under no add-in key) and the OpenPDM add-in (.NET, registered
under HKCU). If either fails to resolve, the script declares its own criterion
blind and exits 1. Seen firing: a copy with the Simulation ProgID misspelled
exits 1; the real one exits 0.

The first draft of the probe got wrong exactly what the positive control is
for: it reported "file missing" for the OpenPDM add-in, which is installed and
loads at startup, because it read HKLM first and took `mscoree.dll` for the
file. Without the positive control that line would have gone into the TODO as
a measurement.

What it then said changes a decision: **SOLIDWORKS Utilities is registered and
reachable** — `Utilities.UtilitiesApp` resolves to `sldUtils\SwLoaderSw.dll`,
with the thickness checker `ThickChkswu.dll` beside it. The conclusion of 22
September ("no tick box, needs the Installation Manager") is refuted at the
registry level. Whether it actually loads is still for SOLIDWORKS to say.

### A successful install reported as unconfirmed, whenever SOLIDWORKS was empty (entry H6-bis)

The dry run (`--prova`) asked for the state of the open document, and with
SOLIDWORKS running and nothing open the state is null: it exited 1, and the
installer said "installed, but the dry run did not confirm" — on the most
ordinary machine there is, one where SOLIDWORKS has just been started. Found on
14 September, left without its own entry until the 24th, and seen again that
evening by accident: the bench had just closed every document and case 2 of
`Controlli/prova-installatore.js` went red. In the morning it had been green
only because a part happened to be open.

The dry run now has three outcomes, not two: attached with a document (it
measures it), attached with none (it says so and exits 0), not attached (exits
1). Red before, green after, with SOLIDWORKS open and empty: 11 of 11.

### The lock between two bridges, seen firing without SOLIDWORKS (entry W2.9)

The per-document lock (`Ponte/TurnoSulDocumento.cs`) had been "written, to be
seen firing" since 16 September, on the premise that seeing it needed two
bridges on a real document. For the lock itself it did not: it is a named mutex
in `Local\`, and any process in the session can hold it. A new contract group
has a child PowerShell hold the mutex and checks four things: free, it is
taken; held by another process, `Prendi` returns null — the bridge's refusal;
the holder killed without releasing, it is taken again (abandoned means free);
a different document does not wait. Four green, 350 checks in the suite.

What it does not prove is that `Sessione.cs` wraps the plan with the lock on
the *right* document; that half still wants two bridges and SOLIDWORKS.

### A release rite, since no runner can build the bridge

No GitHub-hosted runner has the SOLIDWORKS interop, so none can compile the
bridge; the owner chose a manual rite over a self-hosted runner. `RELEASE.md`
describes it, and `Controlli/prepara-release.ps1` checks every step that can
be checked — version and CHANGELOG entry, clean tree, `HEAD` pushed, tag not
yet taken, build, the SHA-256 **recomputed** rather than trusted, the contract
suite green (which includes bench freshness) — and then prints the three
publishing commands without running them. Publishing leaves this machine, and
a person decides it.

### The counter that was meant to stop inflation was inflating

`Controlli/conta-todo.js` excluded "traces kept for reference" — the original
text of an entry that has since been rewritten — only among open entries, and
found one. There were 54, and 49 of them sat among the half-done ones, where
the filter did not look: the diary claimed 87 entries half done when 36 were.
The rule was right and applied to one case in fifty. The script now **exits 1**
when a trace carries a status box, or when a boxed entry sits outside the
heading level every other tool reads (one 30-50-hour entry, a prerequisite of
publication, was invisible that way). Seen firing on the file as it was, silent
on the file as it is.

### The working diary leaves the repository, and the probes that read it survive

`TODO.md` — 17.117 lines, 1 MB — is now in `note-interne/`, which `.gitignore`
excludes. It names the author nineteen times, the workstations, the decisions
not yet taken, and it judges someone else's project by name. None of that
teaches a reader anything about installing, using or contributing here.

**Five probes exist only to check it, and three of them run in the pre-push
hook.** Removing the file without touching them would have killed them with
`ENOENT` on every machine that is not this one — which is to say it would have
broken `git push` for anyone who cloned, over a file they do not have and
should not have. So `Controlli/dov-e-il-todo.js` looks in `note-interne/`, then
in the root, then at `TODO_MD`, and when it finds nothing it **says it checked
nothing and exits 0**. Same shape in `firme-del-todo.ps1` and in the contract
suite, where the check used to be a red.

*A test that fails for everyone except the person who wrote it does not measure
the code. It measures who is running it.*

### The name pattern was case-sensitive, and the capital letter walked past it

The username check ran `-CaseSensitive` against the lowercase spelling only. The
capitalised form was sitting in a comment in `Banco/ProveSenzaIlPezzo.cs:185`,
and the check said "ok". It is now case-insensitive — no Italian word contains
that string, so the change costs not one false positive — and it fired on 24
points across four files the moment it was made.

*And it fired a third time on this very entry:* the first draft of the
paragraph you are reading spelled the name out twice, to explain the bug. The
check refused the commit. **The rule about writing an incident by its shape and
not by its value applies to the sentence that explains the rule.**

`LICENSE` and `NOTICE` are exempt **from this pattern only**: there the name is
the copyright line and the attribution that Apache-2.0 requires. Two texts that
do not grow, three fixed lines.

### An exemption that named three files was the widest one in the file

`$possonoNominarli` listed `controlla-catalogo.ps1`,
`controlla-pubblicabilita.ps1` and `bonifica-storia.js`, and it was applied
inside `Righe()` — so it exempted those three from **every** pattern at once. It
looked narrow. It was not: it never said *what* it exempted from.

**The bill arrived inside the file itself.** While repairing the TODO exemption
in 0.85.0 I wrote the two real addresses into the comment that explains it —
inside the file that exists to forbid them, which exempted itself. They sat
there for three hours. No probe found them; a census done by hand did.

Every pattern now carries its own list, and every name on a list has its reason
written beside it. The internal-address pattern carries **no list at all**: the
regex `192\.168\.\d` does not match its own text, so nobody needs a pass, and
anyone who did would be writing a real address.

### A new guard: the inside of somebody else's repository

There is not one Python file in this repository (`git ls-files '*.py'`: empty).
So a `.py` with a **folder in front** or a **line number behind** can only be
talking about another project — and three of them were, naming directories,
files and line numbers of a private repository in `Strumenti/aggiorna.md` and in
the CHANGELOG entry that had copied them. A bare filename still passes: that is
where the catalogue came from, and it is context.

Seen firing: it caught the one occurrence I had not yet fixed, then went quiet.

### The census, and three decisions taken on it

A sweep over the 481 tracked files, looking for what identifies the company, the
people, the real parts or the machines. What came out:

- **The real product is no longer named.** The comment that opened
  `OperazioniInterferenze` said what the box is for, by brand. It now says what
  it is — a two-part weatherproof box — which is the part the reasoning actually
  needs. Four bench cases lose the product name from a file path.
- **`Banco/casi/pezzi/` leaves the repository.** Ten plans carrying non-round,
  mutually consistent dimensions — copied from a real drawing, not invented.
  They were plans and not declared cases, so the bench never ran them.
- **The hard-coded network paths are gone.** The weldment library is named by
  `PROFILI_SALDATURA` and the document templates by `MODELLI_SOLIDWORKS`, with
  **no fallback**. The old fallback was one workstation's drive: on every other
  machine it did not exist, so it helped nobody, and meanwhile it published how
  that drive is laid out.

  Removing a fallback without adding the sentence that explains what is missing
  is not a repair, it is moving the damage — so the bench now stops at line one
  with exit 2 and says which variable to set. Verified both ways: exit 2 with
  the message when it is unset, a full run when it is set.

### For the person — or the assistant — who has to install this

- **`INSTALL-FOR-AI.md`**, new. Written for an AI assistant installing on
  someone else's machine: the flags, what each exit code means, what the
  installer changed on disk, how to verify with `--prova` (whose last line is
  `[prova] il ponte sta in piedi.` — quoted in Italian because that is what the
  program prints), and five named traps. Every fact in it was run or read
  against the installer's own code, not summarised from a description.
- **`SECURITY.md`**, new. `CONTRIBUTING.md` promised private vulnerability
  reporting and there was no file where GitHub looks for one. It also says what
  is in scope for something that has no server and no account.
- **`README.md` now links to `QUICKSTART.md`** in its first screen. It did not,
  at all: a stranger landing on the default GitHub page had no way to discover
  that a prebuilt installer exists.
- **`QUICKSTART.md` says where `Installa.exe` comes from.** It used to call it
  "the single file you were given", which is written for someone who already
  knows the maintainer. It now says to check Releases, says plainly that an
  empty Releases page is a gap and not a search failure, and gives the
  `build.ps1` route with the hash check.

---

## 0.85.0 — 24 September 2026 · library 0.111.0

### Simulation, measured end to end: 54.3 MPa against 60.0 by hand (entry S3)

Entry S3 is **closed**. A cantilever 200 × 20 × 10 mm in 1.0037 (S235JR), fixed
at one end, 100 N at the tip: by hand σ = M·c / I = **60.0 MPa** exactly. The
full API chain — study, material, restraint, load, mesh, solve, read — was run
from `Controlli/Mensola/Programma.cs`, a standalone probe that does not touch
the bench.

```
  element    sigma max    off 60.0 MPa
  -------    ---------    ------------
   4.0 mm       29.85       -50.2 %
   2.0 mm       35.86       -40.2 %
   1.0 mm       45.44       -24.3 %
   0.6 mm       54.28        -9.5 %
```

**9.5%, inside the 10% the entry required** — and the criterion is met by the
whole column, not by its last row. The error shrinks monotonically as the
element does, which is what mesh convergence looks like. A single run would
have proved nothing in either direction.

Five facts reflection could not have given, each of which cost time:
`DispArray` is an `object[]` of `IFace2`; `AddForce` demands a `RefGeom` while
`AddRestraint` accepts `null`; the force value is set afterwards between
`ForceBeginEdit` and `ForceEndEdit`; the material must be applied **before the
study is created**, or `RunAnalysis` answers `19 = MaterialNotDefined` even
though a read-back confirms the material is on the part; and `CreateMesh(u,0,0)`
answers `5 = SpecifyPositiveValue`, because zero does not mean "default".

One prediction from the reflection half was **wrong**, and is corrected here:
the material does not go on the body through Simulation's `SetLibraryMaterial`.
That call **returns 0 and does nothing**. It goes on the part with
`IPartDoc.SetMaterialPropertyName2` and reads back with
`GetMaterialPropertyName2` — the getter the census reported missing existed,
on a different interface.

And one reading error worth recording: `GetMinMaxStress` returns
`[minNode, minValue, maxNode, maxValue]`, and the first version took the
maximum of all four. It read node number `11875` and printed it as MPa — off by
19,691%. **It was lucky that it was absurd.** Had the peak node been numbered
58, the probe would have reported "58 against 60, off by 3%" and declared the
chain sound. A wrong number that is plausible is indistinguishable from a
measurement.

### The exemption named a file when it should have described a shape

`controlla-pubblicabilita.ps1` exempted `TODO.md` from the `192\.168\.` pattern,
because entry A1 lists what must not ship and has to write it down to list it.
The comment said the exemption covered only the patterns that entry discusses;
the code exempted **the whole file**, and `Guarda` matches on filename, not on
line. Between one revision and the next, TODO.md grew by sixteen thousand lines,
and three real addresses went in with it — one server, and a network share named
twice. The check ran, reported "ok, no internal address", and was right
according to the rule it had been given.

They were pushed, not published: this repository is still private (checked with
`gh repo view`: visibility PRIVATE), so no stranger has ever been able to read
them. But they sat in a history that would have gone public all at once on the
day the switch is flipped, and that day is the only day this check exists for.

The repair is not a narrower exemption. The pattern is now `192\.168\.\d`: a
real address has a digit after the third dot, a rule being quoted writes the
wildcard. `192.168.*` passes everywhere, a real address is refused everywhere,
TODO.md included, and **no file holds a pass on this pattern any more**. Seen
firing in both directions: three hits before the redaction, silent after.

And then a fourth, on the way here: the first draft of this very entry spelled a
real address out as an example, and the check refused it. *It caught the person
who had just written it, in a file that used to hold a pass.* That is the whole
difference between the two rules, and it cost nothing to see.

*An exemption that names a file widens itself every time the file grows. One
that describes a shape does not.*

### Three files leave the repository, two halves of them stay

`CONFRONTO.md` (a competitive comparison against a named third-party project)
and `PROVE-INSTALLATORE.md` (the results of one test session, stuck at
Installa.exe 0.7.1 of 14/9/2026 while VERSION.txt says 0.84.0) are working
notes: they teach a reader nothing about installing, using or contributing.
`Controlli/voce-0.76.0.js` is a single-use scaffold that rewrites a CHANGELOG
entry already written; it was the only one of the 24 scripts in `Controlli/`
with zero references anywhere in the repository.

What was worth keeping did not leave with them. The **(D)/(M) rule** — declared
is not measured, and comparing their (D) with our (M) is not an advantage — is
now a section of `CONTRIBUTING.md`, next to the one that already asks
contributors what they measured. The **six installer scenarios**, and which
three run on any machine, are now the header of
`Controlli/prova-installatore.js`, which is the script that runs them: its
line 11 used to point at the file being removed, and now points at nothing
because it needs to point at nothing.

`.gitignore` gains `note-interne/` (without it the next `git add -A` puts them
straight back, which is how they got in), `Controlli/*.local.js` in place of the
single named file, and `*.bak`.

### A fallback path that only helped the machine it named

`controlla-documenti-senza-file.ps1` fell back to `S:\PDM\OpenPDM\client-windows`
when `RISCONTRO_ADDIN` was unset — this machine's path into **another**
repository, one that is not publishable. On every other machine the path did not
exist and the check skipped that half while saying so, which means the fallback
helped nobody and published where things are kept. Removed. The guard is now
`-not $addin -or -not (Test-Path $addin)`, in that order: with the fallback gone
the variable can be empty, and `Test-Path $null` in PowerShell 5.1 does not
answer `$false` — it raises a non-terminating error and carries on into the
wrong branch.

---

## 0.84.0 — 24 September 2026 · library 0.111.0

### Two fields stop being asked for, and start saying who chose (entry Z-G3)

**A contract change**, which in this repository is delivered with a green bench
run beside it — and at 02:15 there was one: 880 checks, 0 failures, against
library 0.110.0. That is what unblocked this entry, and nothing else did.

`applica_materiale.materiale` now defaults to **`S235JR`** and
`combina_corpi.operazione` to **`unisci`**. Both leave `required`, both gain a
`default` in the schema and a sentence in the description.

#### The sentinel promised exit 4 and delivered a stack trace (entry K17)

K17 says the bench *"stops **saying so** and exits 4"* when SOLIDWORKS dies
mid-run. On one path it did not: it **died**.

```
Eccezione non gestita: Condiviso.PreparazioneFallita: SOLIDWORKS NON
  PRODUCE PIU' DOCUMENTI: 3 tentativi di fila andati a vuoto...
   in ProvaSaldatura.Program.ProvaFotografia()
   in ProvaSaldatura.Program.Gruppo.Corri()
   in ProvaSaldatura.Program.Main(String[])

exit 0xE0434352
```

The group loop was `try { gruppo.Corri(); } finally { ... }` — **a
`try/finally` with no `catch`**. Most groups catch `PreparazioneFallita`
per check and mark it red; a group that throws while **preparing** —
`VistaDelPezzo`, which builds its part before measuring — went straight
through, and `Main` with it. The block that produces the 4 sits *after* the
group loop, so it was never reached.

The cost is the exact opposite of what the entry promised: instead of *"I am
stopping, here is why, exit 4"* — a stack trace, a dead process, **no report
written to disk** (so no evidence of what happened), and an exit code that
means nothing to anybody. In precisely the situation the guard exists for.

Found by running `--fingi-senza-solidworks`: **exercising the guard** rather
than trusting that it had worked once. The entry already said "seen firing" —
and that was true, on a different path. *A guard never seen firing all the way
through is half proven, and the missing half is always the one that matters.*

Fixed and seen in both directions; the groups that never ran are now named
one by one, because "14 skipped" without saying which is the same defect K2
had already corrected elsewhere.

#### A default is not one field fewer: it is a choice somebody else made

The entry exists because *"nobody thought about it"* and *"somebody thought
about it and decided to ask"* read identically in JSON, and only one of them
can be improved. **The same holds in reverse, and that is the dangerous
direction:** if the bridge applies S235JR and says nothing, the model believes
it chose it, next time it will not even ask, and then a part that needed
stainless ships with nobody able to say where the decision was made.

So every answer now carries `misure.nota_assunto`:

```
materiale: «S235JR» — PREDEFINITO di questa officina, non l'hai chiesto tu.
materiale: «AISI 304» — l'hai chiesto tu.
```

**Written always**, including when the value was asked for. A field that only
appears in the odd case is a field readers never learn to look at.

#### The guard would have gone red by finishing the work

`ProveICampiObbligatori` demanded `candidati > 0`. It counted **promised**
work, and that was right while there was some. With both candidates done the
counter goes to zero — *a check that demands the existence of work to do turns
red exactly when the work finishes.* It now reports instead of demanding, and
in its place is a check on **delivered** work: the `default` must be in the
**served** catalogue, with the right value, and the field must be out of
`required` — all three together, because a field removed from `required`
*without* a default is worse than before: the model does not know it may omit
it, and if it does it cannot know what it got.

The neighbouring guard — *"a decision about a field that no longer exists is
noise"* — fired by itself on the two now-obsolete entries the moment the
catalogue was regenerated. Nobody had to remember it.

#### Two mistakes in the bench cases, both caught by reading the operation

The four cases (in pairs: default and twin) were written the night before,
against the **schema**. Both errors came from not reading the **description**.

- The two bodies for `combina_corpi` were placed **120 mm apart**, to make the
  three outcomes unmistakable. But the operation declares it *fails* when after
  a union the bodies are still two: *"usually they do not really touch, and I
  fail rather than confirm a part different from the one you think you have"*.
  The union would have failed and **the red case would have accused the default
  instead of the geometry** — sending someone to hunt in the wrong place. They
  overlap by 40 mm now: 80000 / 30000 / 20000, still three distinct numbers.
- `sposta_corpi` was called without `corpi_uid`. Its schema says
  `required: []` — correctly, because it is an either/or between `corpi_uid`
  and `corpi_rif` that a flat `required` cannot express — and its description
  says so in plain words. Caught by the bench going red.

*A schema says what an operation accepts; the description says what it demands
and what it refuses.* Writing a case against only the first is how you produce
a red that blames the wrong thing.

---

## 0.83.0 — 24 September 2026 · library unchanged (0.110.0)

### The elapsed time next to the step, and Section S is unblocked (entry S0)

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

#### Reading the criterion again, after building, found the gap

Entry S0 asks for *"a trace readable from outside **with the time next to
it** — and the caller can tell 'working' from 'stuck' without watching the
CPU"*. Yesterday's progress notifications carried the step number.

**The step number alone does not say it.** "Step 3 of 5" is identical
whether that step started two seconds ago or eleven minutes ago — and the
whole point of the criterion is exactly that difference. *The criterion asked
for two things and I had heard one.*

Every notification now carries the elapsed time written the way a person
reads it (`eseguo il passo 3 di 5: guscio — 2m14s`), not raw seconds: the
`message` field is the only one a human reads, and putting seconds there
would make them do the division.

The clock is **passed** to the `Filo` rather than read inside it — same
choice as `Guinzaglio` and `SentinellaSolidWorks`. Seven checks freeze it and
compare the exact string, including the case that matters: eleven minutes on
one step.

#### S0's "to be decided" is decided, and by neither of its two options

The entry weighed *"start and return an id immediately"* (needs an extra
"where are you up to" tool) against *"block with a declared ceiling"* (which
lies to whoever exceeds it). **The third road was already in the MCP spec.**
The bridge stays synchronous — which SOLIDWORKS imposes, it is not a choice —
and talks while it works. No extra tool, and a generic client reads it
knowing nothing about this bridge.

S0 and S1 are no longer blocked.

#### Two probes that were lying, both found by using them

`conta-todo.js --blocchi` listed **Y2**, **W2.6** and **Z-B7** under "CAN BE
DONE NOW". None of them could: Y2 declares *"depends on Y1"*, Z-B7 *"depends
entirely on B1 and B3"*, and W2.6's criterion is a measured volume. Two new
rules — and the dependency one is not a regex, it is a lookup: the entry
names its own blocker, and the probe checks how that entry is doing.

It went wrong immediately, and instructively. The TODO keeps, beside a closed
entry, its *"original trace kept for reference"* — **same code**, and at `~`
forever because it is a memory, not work. There are dozens. The map took the
last one written, so `B1` read as open although the real B1 closed on 14/9,
and Z-B7 was declared blocked by something finished ten days earlier. *A
warning light that accuses a finished thing sends you looking in the wrong
place, which is worse than staying quiet.* With traces skipped, Y2 and S1
stay blocked and Z-B7 comes back out — the rule exercised in both directions
on real data.

#### The bench cases for Z-G3, written before the thing they measure

Entry Z-G3 closes when two fields have their `default`, the sentence that
declares it, **and a green bench case exercising it**. The default itself
cannot ship — the entry forbids it in writing: *a contract change is delivered
with a green bench run beside it*, and the bench is down. The cases can be
written, and now are: `Banco/casi-in-attesa/`, a folder the bench does not
scan (it reads `Banco/casi/` and nothing else).

A case that must fail, placed in `casi/`, turns the bench red for a known
reason — and *a red for a known reason is a red people learn to skip*, so the
day it goes red for a different reason nobody notices. Same shape as
`Banco/compiti/` (Z-G9): the input is born before the thing that runs it.

They come **in pairs, and that is not symmetry**. Without the twin that names
the value explicitly, the default case proves nothing: a declaration that
appeared *always* would pass it and be noise — and a sentence that always
appears stops being read exactly when it says something.

Writing them turned up three things the plan did not have:

- **The dangerous direction is the other one.** The entry worries the model
  won't know it omitted a field. The real risk is the reverse: if the bridge
  applies S235JR and **says nothing**, the model believes it chose it, next
  time it won't even ask, and then a part that needed stainless goes out with
  nobody able to say where the decision was made.
- **The two bodies must be far apart**, and making them needs `sposta_corpi`
  with `copia` — the catalogue says so itself: *"separate bodies are born from
  `sposta_corpi` and the copy"*. Far apart so the three outcomes are
  unmistakable: `unisci` = 100000, `sottrai` = 50000, `interseca` = 0.
  **`riuscita: true` would give all three.**
- **The first draft invented two arguments** — `corpo_separato`,
  `scostamento_x_mm` — that are not in the catalogue, and the wrong enum value
  (`unione` instead of `unisci`). Caught by checking against the served
  catalogue before saving. *A case with invented names is worse than no case:
  it fails, and sends you hunting for the defect in the wrong place.*

The field the cases watch, `misure.assunto`, **does not exist yet**: it is a
proposal, and the README says so. Whoever implements may name it otherwise —
that is one line in four files. What does not move is the demand: the answer
must distinguish "I chose it" from "the bridge chose it for me".

#### A false positive inside the pre-push hook, found by using it

`righe-del-todo.js` resolved a cited file **by basename** across the known
folders. The repository has two `Sessione.cs` — `Banco/` and `Ponte/` — and
`Banco` comes first in the list. So the citation `Ponte/Sessione.cs:824`
landed on `Banco/Sessione.cs`, which has 392 lines instead of 832, and the
probe declared a correct citation dead. **In the pre-push hook**: it would
have stopped a delivery over its own mistake.

The folder written in a citation is an address, not a hint. It is honoured
now, and where a citation gives no folder and the basename exists in more
than one, the probe **abstains and says so** instead of picking — two
same-named files are exactly the case where guessing produces the wrong red.
It also prints the unresolvable names rather than just counting them: a bare
`7` cannot be checked without redoing the probe's work by hand. The six that
remain are all genuinely external — three files from the add-in repository,
three RAG notes.

Two TODO citations were wrong behind that fog. One said the per-operation
state re-read lives in `Sessione.cs` — it is in
`Riscontro.Disegno/EsecutorePiano.cs:354`, **wrong file before wrong line**;
the entry rests a real warning on it (*"comparing the reading of now with the
reading of now always returns true, and you believe you have a protection you
do not have"*). The other was 23 lines off from `EseguiIlPiano`.

And writing the correction, the dead addresses went back in as citations —
the same mistake made six hours earlier and written down as *"a corpse gets
named, not cited"*. The probe caught it again. A lesson written down is not a
lesson learned; a probe that keeps checking is.

#### Counting the places where zero means "I don't know" (entry Z-C8)

The entry asked that *"every enumeration declares **zero** against **I don't
know**"*. Before declaring anything you have to know how many there are.
`Controlli/zero-o-non-lo-so.js` finds **21**, and exactly **one** already
followed the rule.

The swap runs both ways, and the second way is worse. The one the entry
describes — `GetMates()` returning `null` for zero and the bridge answering
*"couldn't read"* — is the **cautious** direction: it alarms the reader for
nothing. The other is a function that **reads from the CAD and answers `0`
from its `catch`**. Zero is not "I don't know": zero is an **assertion**.
*"This part has zero bodies"*, *"this hole removed zero mm³"*. The reader has
no way to notice the read failed.

And the two opposite rules live in the same file, two thousand lines apart:

```
OperazioniSw.cs:2122  "A volume that could not be read does not become a
                       zero: a zero would say 'it removed nothing', which is
                       an assertion."
OperazioniSw.cs:4837  "The volume of all solid bodies, in mm3. Zero if it
                       cannot be read."
```

The first is the comment of whoever **found** the defect; the second is the
function that **produces** it. The call site defended itself — in twenty lines
— and the root stayed. *A patch on the caller leaves the hole for the next
caller.*

The model to copy already exists, and there is one:
`OperazioniAssieme.ContaGliAccoppiamenti` first checks whether the handle
**still answers** (reads `Name2`), and only then does a `null` from `GetMates`
count as zero; otherwise it returns **`-1`**. It now carries the
`ZERO-E-ZERO:` marker with its reason, and the probe leaves it alone. **The
rule in one line: `-1` (or nullable) for "I don't know", `0` only when zero is
an assertion you can make.**

The probe does not exit 1 and is not in the hook: fixing these changes library
signatures and every caller, and doing it now would stack twenty unverified
changes **on a library the bench has not yet run against** — if the run went
red, nobody could say whose fault it was. It is a work list, not a ban.

It got its own window wrong first, and instructively: it counted fourteen
lines above the site, and the comment carrying the reason is twenty long — the
marker was there, in the right place, and the probe could not see it. **Second
time in two days**: the Z-A1 guard was born with exactly the same defect.
*Counting raw lines punishes whoever explains*, in a repository where
explaining is the rule. It now walks up to the member's `<summary>` instead of
counting, and the self-test puts the marker twenty lines away on purpose.

#### Four "to be ascertained" ascertained, none of which wanted the CAD

Four entries carried an open question — *"unverified"*, *"to be ascertained
which"*. **None of the four needed SOLIDWORKS running**; they needed somebody
to read the interop. Thirteen more expectations in `Controlli/firme-api.ps1`,
which now pins thirty.

- **GT5.** The `TableTemplate` preference index — *"33, per an unverified note
  in 80r"* — **is right**: `swUserPreferenceStringValue_e` has 131 members and
  33 is `swFileLocationsHoleTableTemplates`. The note was true. The rule it
  guards does not change, though: knowing 33 is correct does not say where
  *this* installation keeps its files, and that is the data needed. The
  constant is for **asking** the question, not skipping it.
- **GT2.** Its `Cosa` line still asked *"or `IWeldmentCutListAnnotation`, if it
  exposes a direct insertion — to be verified which"*. The answer had been
  written further down **in the same entry** since 17/9: it is
  `IView.InsertWeldmentTable`, and the annotation interface exposes accessors
  only. *A question left open next to its own answer sends someone to redo the
  work* — and on 24/9 I did redo it. Reflection returned the same answer, which
  is good news about reflection and bad news about that line.
- **GA6.** *"`GetTotalTransform(false)`, not `GetXform`, format unverified"* now
  has a measured reason instead of a suspicion: `GetTotalTransform` returns a
  typed `MathTransform`, `GetXform` returns a bare `Object`. *Between two roads
  to the same place, take the one you can read without guessing.* And
  `GetMates()` returns a bare `Object` too — so reflection gets far enough to
  prove the format **cannot** be known without an open assembly. "To be
  ascertained first" stands, now on a measured limit rather than on inertia.
- **X2.** A reference row said the real helical thread is *"`InsertHelix` +
  `InsertCutSwept5`, a five-step composite with a **void** return and 21
  parameters"*. It said three things and two were wrong. `InsertHelix` **is not
  on `IFeatureManager`** — it is on `IModelDoc2`, 10 arguments, returning
  `Void`; fifth time in a week a member sits on a different interface from the
  one its name suggests. And `InsertCutSwept5` **does not return void**: it
  returns a `Feature`, with 22 arguments. So the blind step is **one**, not the
  whole procedure — the cut can be checked from what it hands back. A road
  nobody had looked at also turned up: `InsertVariablePitchHelix` +
  `AddVariablePitchHelixSegment` + **`EndVariablePitchHelix()`, which returns a
  `Feature`**. Whether a variable-pitch helix with equal segments is the same
  thing as a constant one is **not measured** and needs the CAD — but if it is,
  the last blind step disappears.

#### Simulation: the census S3 asked for (entry S3, first half)

S3 says *"survey first, build second"*. The surveying never needed SOLIDWORKS
**running** — only installed. Seventeen expectations now live in
`Controlli/firme-api.ps1`, which exits 1 if one changes.

The entry's two numbers were right: `cosworks` has **589 types and 160
interfaces** (plus 349 enums). Three findings the plan did not have:

- **`MeshAndRun()` returns `Void`.** It is the most inviting name in the
  interface and the only one of the two that **cannot say whether it worked**.
  `RunAnalysis()` returns an `Int32`. Same rule this project has already paid
  for elsewhere — *the value returned by whoever did the thing is not the
  measure of the thing* — except here there is no value at all.
- **Material sits on the BODY, not the study**, three hops down, and
  `ICWSolidBody` has `SetLibraryMaterial` and **no `GetMaterial`**: a setter
  with no reader. Fourth time a property can be written and not read back.
- **`AddRestraint` and `AddForce` take `Object DispArray` and `Object
  RefGeom`.** The signature does not say what goes inside, and no amount of
  reflection will. *The census did not remove the risk: it put it in one place
  and gave it a name.*

---

## 0.82.0 — 24 September 2026 · library unchanged (0.110.0)

### Progress and cancellation, with the protocol's own mechanisms (entry W2.2)

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

Entry W2.2 was marked **blocking for the whole of Section S**. It is not any
more.

#### Why not a second "where are you up to" tool

Because it already exists in MCP 2025-06-18, and reinventing it would cost
more to be understood by fewer: a generic client can read
`notifications/progress`, and will never read a tool invented here. There
were **zero** occurrences of `progress` or `cancelled` in the repository.

The token is read from `params._meta.progressToken` — next to `name`, not
inside `arguments`: it belongs to the *protocol*, and a generic client puts
it there knowing nothing about this bridge.

Three decisions carry the design:

- **The token is copied, not read.** The spec allows a string or an integer.
  A `42` coming back as `"42"` matches nothing on the client side, *and the
  client has no way to say why*. So it stays an `object` and is never
  inspected. A check demands that an integer comes back an integer.
- **`progress` must increase, and here it cannot fail to.** That rule belongs
  to the spec. Left to callers, it would be discovered broken inside a client
  — which is to say never. It lives in the `Filo`, which keeps the last value
  and **discards** a step that does not grow; a non-growing step is fed to it
  on purpose to watch it discard.
- **The total lives in the `Filo`, not in the callers.** A plan is narrated in
  two halves — validated whole against an untouched document, then executed —
  and the two points sit in **different files**. If each computed the total
  itself, sooner or later they would disagree and the reader's bar would run
  backwards. Execution resumes the count from `Filo.Ultimo` instead of
  recomputing it.

#### The library did not need touching, and that is the instructive part

The progress that matters is the one *during* execution: validating a plan
takes milliseconds, a real step can take minutes. It looked like a callback
would have to be threaded through `EsecutorePiano` — inside the library.

It did not. The executor asks `IFontePiano` for the next step, and the
implementation — `Ponte/PianoInMemoria.cs` — **is in the bridge**. The
notification goes out from `ProssimoAsync`, that is *before* the step starts:
saying "about to do step 3 of 5" is useful, saying it afterwards much less.
*The right seam was already drawn; all that was missing was someone using it.*

#### `notifications/cancelled` no longer falls into silence

The entry was right: it fell into the `case default`, and since it is a
**notification**, the main loop discarded the error response. A silent no-op,
with the client convinced it had cancelled something.

This bridge cannot interrupt a call in flight, and that is not a gap to fix
with more code: it serves one request at a time on one thread because
SOLIDWORKS requires STA, so while it works it does not read stdin — and by
the time the line arrives, the request it names has finished. The spec allows
ignoring a `cancelled`. Here it is not ignored, it is **answered**, with the
request number and the reason. *A stated no can be read; silence cannot.*

#### What is measured, and what is not

Thirty contract checks, six of them end-to-end over stdio against the real
bridge — possible because `prova: true` (entry Z-G6) walks a plan step by
step with SOLIDWORKS closed. Among them the half that is usually forgotten:
**a client that sends no token receives nothing**.

Not measured: the three-minute operation the entry's criterion names. The
wiring is exercised — `PianoInMemoria` is driven directly and what it emits
is inspected — but *driving the class is not the same as watching the
notification arrive during a real extrusion*, and in this repository that
difference is the rule, not a detail. The entry stays at half.

---

## 0.81.0 — 23 September 2026 · library unchanged (0.110.0)

### The bridge starts writing down what it was asked for and did not have

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

Everything below was built and measured **without** the CAD, because none of
it needs one — the run afterwards confirms it did not break what does.

#### What was asked for and wasn't there (entry Z-G7)

The five catalogue operations with no bench case — `ripeti_circolare`,
`cordone_saldatura`, `fazzoletto`, `taglia_membri`, `tappo_estremita` — **we
picked ourselves**. Which ones are actually needed, nobody knows, and asking
people does not find out: whoever uses the bridge has no list of what is
missing.

The bridge had that data in hand four times over and threw it away. The
`default` branch of `Attrezzi.Chiama` *knows* it was asked for a tool that
does not exist, answers well, and writes it down nowhere.

`Strumenti/RegistroDelleDomande.cs` now appends one JSON line per unmet
request, wired into all four sites, and `Controlli/domande-senza-risposta.js`
reads it back. Three decisions matter more than the code:

- **It does not record arguments.** Only the name asked, its kind, and the
  near names the catalogue offered. A ledger that copies call arguments
  becomes, in one afternoon, a file full of network paths and job numbers —
  and this repository already has a personal-data problem that blocks
  publication. *A name a model invented is nobody's data; the path the part
  sits at is.*
- **It can never fail the call it observes.** It does not name `Console`
  anywhere — that is where the JSON-RPC goes, and one extra line there is not
  a dirty log, it is a broken protocol — and it never throws. Installed under
  `Program Files` the folder is not writable, and then it simply goes quiet.
  The reason for the last silence stays in `PercheHaTaciuto`, which nothing
  prints: it exists so a test can tell an empty `catch` from a `catch` that
  never ran.
- **The report is split in two.** A name asked ten times **with a near match**
  is a synonym to add and costs one line. The same name **with nothing near**
  is an operation to build. One combined count would hide the difference.

The guard watches the *fifth* site, not the four. The predictable failure is
not getting one of them wrong — it is adding a fifth and forgetting to
record it. So the test demands that **every** line calling
`_catalogo.Simili(` also writes to the ledger, and is made to fail on
purpose in both directions on fabricated lines.

The entry stays open at half, and deliberately: it did not promise a file,
it promised to know which operation to build next. That number does not
exist yet — the ledger is empty. *What is missing is not work, it is
traffic.*

#### The version the bridge announced had been wrong for 78 minor releases

`Programma.cs` carried `const string Versione = "0.2.0"`. `VERSION.txt` said
`0.80.0`. Every MCP `initialize` reply has been announcing 0.2.0, and anyone
logging the conversation recorded a false number.

Nothing in the code was wrong. There were **two places to write the same
number, and nobody ever re-read one of them.** It is the same rot the TODO
had already cured in its own header on 16/9 — that line said *«Ponte 0.2.0»*
and had been true on the 14th — but in code, where it shows even less.

`VERSION.txt` now travels inside the executable as an embedded resource and
is the single source. If the resource cannot be read the bridge answers
**`sconosciuta`** rather than a fallback number: *a wrong number is worse
than a missing one*, because whoever reads it has no way to know it is wrong.
A contract check pins the announced version to `VERSION.txt`, which is what
stops the two places from becoming two again.

#### And a guard already in place caught the new environment variable

`RISCONTRO_REGISTRO` was in the code and not in the README, and the check
that reads every variable the bridge looks up went red within a minute of
the ledger compiling. Nothing to fix in the guard — it is reported here
because a guard that fires on its author on the day it is needed is the
cheapest evidence that it still works.

---

## 0.80.0 — 23 September 2026 · library unchanged (0.110.0)

### One real defect in the bridge, and two guards that were green for the wrong reason

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

Everything here was found without the CAD — by reflection over the installed
interop assemblies, by reading the TODO against the code, and by asking one
guard what it actually measures.

#### A plan that names another document left the signatures behind (entry Z-A1)

The bridge keeps three things about the part it is looking at: `_guardato`
(*which* document) and `_firmaGuardata` / `_firmeViste` (*what it looked
like*). In `EseguiIlPiano` there was `_guardato = adesso;` **on its own**.

A plan naming a different document — `documento: "..."`, the legitimate,
documented route — was enough: from that moment `_guardato` pointed at the new
part and the signatures at the old one.

**The damage does not land there.** It lands on the *next* call: the document
in front is the new one, so the "same part?" test passes, and the comparison
then runs against another part's signatures. Two outcomes, both wrong — reject
a good plan saying *«the part changed»*, or **let a plan through on uids that
no longer mean anything**. The second is the failure this project has been
chasing for weeks: a plan that succeeds in the wrong place, silently.

The principle was already written two lines above, on the local variable:
*«better no comparison than a comparison between two different parts»*. It was
right, it was there, and it was scoped to the local. **A rule written for half
the code is a rule somebody will apply to half the code.**

How it surfaced is the instructive part: not by reading the code, but by
**reading the TODO**. The entry already quoted the offending line as
background. It had been sitting there for days, described accurately, and
nobody had read it as a defect.

`Prove/ProveLeFirmeDelDocumento.cs` now demands that no `_guardato =` travels
alone. It was born red on a correct piece of code — it counted raw lines, and
the three legitimate assignments sat in one block separated by twenty-five
lines of comment. Counting raw lines there means **punishing whoever explains**,
in a repository where explaining is the rule. It counts code lines now, and
it is exercised in both directions.

#### The guard that asked git when it should have asked the disk

`ProveIlBanco` refuses a delivery where nobody ran the bench. Part of that is:
*is the run newer than the last change to `Riscontro.Disegno`?* It answered by
calling **`git log`** — committed history, and nothing else.

But code does not live in git while you are writing it. It lives in the
working tree, and **that is where `build.ps1` builds the DLL from**. Between
the edit and the commit there was a window where the guard reported *«there is
nothing unproven»* over a tree full of code the bench had never seen.

Measured on 23/9/2026: `Riscontro.Disegno/Catalogo/Operazioni.cs` touched at
19:18, last bench run at 15:23, guard **green** — because the last *commit*
under that folder was from 15/9. Green for the wrong reason, which is how a
guard stops working without anyone noticing. The window was not narrow either:
at that moment the tree held **29 modified files and 31 new ones**.

The verdict now lives in a function that takes two dates and no I/O, so it can
be made to fail on purpose. Four checks drive it: a dirty file newer than the
commit wins; a clean tree changes nothing; a dirty file *older* than the commit
moves nothing; and — the one that matters — **"no unproven commit" no longer
covers a full working tree**. That last case *is* the false green.

It reads the tree with three commands that print bare paths
(`diff --name-only`, the same `--cached`, `ls-files --others`) rather than
`status --porcelain`, because the repository's own `Git()` helper trims every
line and `--porcelain` puts the status in fixed columns that the trim eats.

With the fix in place the guard immediately went red twice. That is correct.

#### A citation that lands in the right method under the wrong name (entry Z-A7)

The entry sent the reader to `` `OperazioniSw.cs:6186` `` for
`GetPersistReference3`. **That method is not in that file at all** — it is in
`StatoDocumento.cs:392`. Line 6186 falls twelve lines short of
`GetObjectByPersistReference3`, which is to say *inside the right method*: a
reader who follows it finds plausible persist-ID code and never notices that
the named API isn't there.

*A citation that sends you out of the file is caught immediately; one that
sends you into the right method under the wrong name is never caught.* The
entry now names both halves of the round trip and where each lives.

#### Internal cross-references rot the same way (entry Z-C14)

Entry Z-C14 pointed at `TODO.md:7497-7530` and `TODO.md:~7753` to talk about
`GetRemainingDOFs`. Both lines exist — the file has sixteen thousand — and both
talk about something else entirely: `IFace2.Normal` and undercuts in the first
case, the origin of a single-direction extrusion in the second. The real
addresses were three hundred and four hundred lines further down.

`Controlli/righe-del-todo.js` could not see this, and says so: its first pass
only checks whether the line **exists**. It has a second pass now, and it does
not guess what an entry is about — it takes **the subject the sentence wrote
for itself**: the backticked identifiers on the citing line. If none of them
appears in the cited range, the reference is broken. Same technique that
resolved `vedi C8` into `Z-C8`.

It fired twice on its author, and both times it was right. First on the
sentence *documenting* the two dead addresses — which had written them in
citation form. But `TODO.md:N` is an imperative, it means *«go here»*, and
using it for an address you are declaring wrong is a contradiction before it
is a false positive. **A corpse gets named, not cited.** Then on the corrected
sentence, which cited two addresses with two different subjects and named
neither — the reader had to infer the topic from context exactly as the probe
did. It names `GetRemainingDOFs` now.

Neither time was the rule narrowed to shut it up.

#### Entry GT2 needed nothing, and that is also a result

Reflection over `IWeldmentCutListAnnotation` re-derived, independently, a fact
the entry had already recorded on 17/9: the insertion is
`IView.InsertWeldmentTable`, the annotation interface is an accessor and not an
inserter, and `swTableAnnotation_WeldmentCutList = 4`. No change. A probe that
confirms what is written is worth as much as one that contradicts it.

---

## 0.79.0 — 23 September 2026 · library 0.110.0

### The other half of GT4, and a bench that kept testing against a dead SOLIDWORKS

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

For about eleven hours it had not. SOLIDWORKS was unusable on 23/9/2026: a
handle left on the document template, on a network share, made every new
document hang (entries K17, K19). Everything in this entry was written,
compiled and structurally checked in that window — **and the run afterwards
measured it on a part, which is the only thing that settles it.**

**And the local model copy (K19) was finally seen to *succeed*, not fail:**
`Parte.prtdot` 45955 bytes and `Assieme.asmdot` 25895 bytes copied into
`%TEMP%\banco-modelli`, one network read instead of eight hundred. Until now
that code had only ever been watched failing.

#### `dettaglio_in_tavola`

A section shows what is inside; a detail shows what is too small to read at
the drawing's scale — a groove, a chamfer, a fillet with its dimension.
Without it, on a real shop drawing those get dimensioned by eye.

```
"vista_dettaglio":"Vista dettagliata B (2 : 1)", "tipo_vista":3,
"e_un_dettaglio":true, "circonferenze_prima":0, "circonferenze_dopo":1
```

**Three things "by analogy" would have got wrong, and two of them were.**

1. **The crosshatch has nothing to do with it.** On a section the hatch is
   the check that matters. A detail cuts nothing: it shows the same geometry
   enlarged, and `GetFaceHatchCount` on a detail view answers **0**. Copying
   the section's guard would have produced an operation that **always
   refuses** — the exact defect the section had on its first real use, one
   day after it was corrected.
2. **`GetDetailCircleCount2` has two outputs, and the obvious one lies.** The
   count is the **return value**; its `out` goes from **1 to 35** for a
   single circle — it is an array size, not a count. Read as a count it
   would say the view already had one before it had any.
3. **`Showtype` wants `swDetCircleShowType_e`**, not `swDetViewShowType_e`,
   which does not exist.

So the guard is that the circle **became** a detail: the source view gains a
detail circle, 0 → 1. `viste_dopo` goes up either way — one more view on the
sheet does not say it is tied to that circle. Without this check the sheet
can keep **a hand-drawn circle next to an unrelated extra view**, and no
error would say so.

And it refuses a circle larger than the view: that enlarges emptiness, and
**enlarged emptiness is still a valid view** — it counts, it has a label, and
it reaches the shop floor as a frame with nothing in it.

What neither half can tell you is whether that is the feature you needed. A
circle moved ten millimetres enlarges a patch of flat surface, and is just as
valid. It says so, under `cosa_non_guarda`.

#### The bench now notices when SOLIDWORKS is gone (entry K17)

A full run died after 312 green checks: `NewDocument` started answering
`null` and never stopped. The report said

```
NO   il_dettaglio_ingrandisce   SOLIDWORKS non ha prodotto la parte nuova
NO   il_loft_non_e_un_cono      SOLIDWORKS non ha prodotto la parte nuova
```

on nine cases that were written correctly, and then hung inside the next COM
call for as long as nobody looked.

Two defects, not one. **`NO` was the wrong word** — it means "I measured and
the part came out different", and the fact was "I did not measure"; whoever
reads it goes and inspects the case, which is the wrong place. And **the run
carried on**, forty minutes of testing against a dead program.

Three new-document failures **in a row** now mean the program is gone: the
rest of the run is skipped **saying so**, and the exit code is **4**, which
wins over 2 and 1 — a run that did not happen is not a run with errors. "In a
row" is the word that matters: three scattered failures across eight hundred
checks are normal noise, and a guard that stopped healthy runs would be
switched off by the first person who saw it do that.

`--fingi-senza-solidworks` makes `NewDocument` answer `null` on demand, so the
guard can be **watched firing** on the real bench instead of believed: two
`!!`, then 127 cases skipped, exit 4. Seventeen more checks run it where
SOLIDWORKS does not exist at all.

**And one of those checks was green for the wrong reason.** It looked for the
digit `3` in the report while the report said "**0** attempts in a row" — the
`3` it found was the *threshold*, in the same sentence. The defect was in the
sentinel: it read the live counter instead of the one at the moment it
tripped. *A right number in the wrong place hides a wrong number.*

#### And the cause was not SOLIDWORKS (entry K19)

This station's default document template lives on a **network share**. Every
new part is an SMB read, and a run makes eight hundred of them. When
SOLIDWORKS hangs while opening that file it keeps the handle, and from then
on the file cannot be read **by anyone** — including the next SOLIDWORKS,
which is why three runs in a row died in the same place.

Measured while it was stuck: the profile folder **on the same share**
answered, the server replied to ping in 0 ms, and `Get-Item` on that
`.prtdot` did not return within twenty seconds. Killing SOLIDWORKS: 45,955
bytes, instantly. **It looked like the network, and it was a handle.**

The bench now copies the template locally once per run and says so, with the
source and destination paths; if the copy fails it uses the network one and
**says that too**. It does not cover everything — SOLIDWORKS also reads the
weldment profiles and the material libraries from the share — but it removes
the most frequent read, and the README says which ones remain.

#### Twelve operations now declare the limit they were already refusing (Z-G5)

Forty-five refusal steps across 146 declared cases, on 31 distinct operations.
Twelve of those 31 said **nothing** about their limit in the catalogue
description — and the model does not read bench cases, it reads the
catalogue. For those twelve the only way to discover an already-measured
limit was to hit it. *A declared limit costs zero turns; the same limit
found by a refusal costs one.*

The new sentences are not invented: each one is the message its bench case
already demands, turned to the future tense. Inventing them would have meant
promising a limit different from the one the code enforces — worse than
silence.

The count is a ceiling, not a note, and it was watched moving in both
directions: born at 12 with 12 mute (green), then red with **"there are 0:
lower the ceiling"** once the descriptions were written. It is 0 now, and a
new guard without the sentence that declares it turns the check red **on the
day it is written** — the only day anyone is looking at it.

Alongside it: every `deve_fallire` must name a `gemella`, that twin must
exist, and it must demand a message (entry Z-C11). The bench already checked
twins — after an hour of running, with SOLIDWORKS on. A case written without
a twin is a writing mistake, not a measurement one: it now shows in a tenth
of a second.

#### Every operation now says which document it runs on (entry Z-D0-ter)

All 71 served operations declare `parte`, `assieme`, `tavola` — or more than
one — next to `precondizione`, with a closed vocabulary. The family was an
approximation and `verifiche` proved it alone: `cartiglio` wants a DRAWING,
`verifica_interferenze` an ASSEMBLY, the other six a PART. Three documents
under one label, a fact that lived in the head of whoever wrote the operation.

The values are not guessed: where the precondition says it, it was taken from
there; the twelve without one are in a hand-written table, decided one by one.
The script **stops without writing anything** if it meets an operation it
cannot classify — better unfinished than guessed.

And the check verifies the field was *needed*: if no family were mixed, it
would be a duplicate of the family and should be removed.

#### An example copied from a green case (entry Z-G1)

24 operations out of 71 now carry a real call, copied from a step the bench
**executed and measured**. A schema says which fields are legal; it does not
say which combination builds a part.

24 and not 71 on purpose: the other cases use things that live only in the
bench — the `@faccia(verso=alto)` selectors, the `{LAVORO}` placeholders.
Copying them would have produced 24 good examples and 47 **traps**: a model
that copies a bench selector gets a refusal it cannot understand, and next
time it does not trust the catalogue. That is worse than no example.

#### Every required field is a decision taken (entry Z-G3)

92 required fields, 58 of them dimensions that are genuinely asked for, **34
with a written decision**. "Nobody thought about it" and "somebody decided to
ask for it" read identically in the JSON, and only one of the two can be
improved.

Two have a real shop default and are **not done**: `applica_materiale.materiale`
and `combina_corpi.operazione`. Removing a field from `required` changes the
contract, and a contract change ships with a green bench beside it.

#### Numbers read off an image are not authoritative (entry Z-C10)

The caption next to the views said where the measurements are. It did not say
the ones in the image are **wrong**. It now carries the two measured errors —
a Ø90 bolt circle read as 79, a 12.5 wall read as 6 — says **why** (in
isometric, thin walls are foreshortened) and names the operations that give
the authoritative number.

What cannot be done is stated rather than implied: the bridge cannot know
where a model got a number from. A 79 in an argument is identical to a
measured 79.

#### A leash on calls that never return (entries K18 and Z-C4)

Three bench runs died hung: the process alive, SOLIDWORKS `Responding=False`,
**zero CPU on both**, no modal dialog. The sentinel could not help — it counts
*answers*, and no answer was coming.

It does **not** rescue the call: a wedged COM call cannot be cancelled, and
promising a "timeout that retries" would be another guard that does not do
what it says. It turns an infinite silent wait into a report: **which**
operation, **for how long**.

A heartbeat, not a worker thread — the interop is single-threaded, and moving
calls onto another thread would add a new way to hang instead of removing
one. The working thread announces what it is about to do and says when it is
done; a watchdog outside watches the clock. *It does not touch the work: it
watches it.*

Two callers, two reactions, and the difference is the point: the bench **dies**
with exit code 5 — a run that did not happen is not a run with errors — while
the bridge **says and carries on**, because it is somebody else's service. Its
report goes to **stderr**, never stdout: on stdio, stdout is the JSON-RPC
channel.

#### A dry run: being wrong without touching the part (entry Z-G6)

`disegna` with `prova: true` checks the plan and touches nothing — and works
**with SOLIDWORKS closed**. On a plan wrong in four ways it finds six defects,
including the most valuable one: an argument spelled wrong, which today is
**silently ignored** so the part comes out different from what was asked with
no error at all.

And it always states what it could *not* check — that the uids are still
valid, that the document is of the right type. *A dry run that keeps quiet
about its limits makes you believe the plan is fine*, and then the trouble
arrives one step later with the added confidence of whoever asked.

#### Confirmations reach the USER, not the model (entry W2.5)

This bridge's confirmations were sentences the model re-reads. *A confirmation
that goes through the model is one the model can interpret* — and interpreting
a request for permission means, sooner or later, granting it to yourself.

The bridge now sends `elicitation/create` before an operation that cannot be
undone, and on a `decline` it **stops and says it touched nothing**. Three
decisions worth more than the code: it asks **before anything else**, even
before checking that SOLIDWORKS is attached; **doubt counts as no** (decline,
cancel, an unparseable answer, an error, a timeout); and the list has **one**
entry, because *whoever confirms ten times in a row stops reading what they
confirm*.

The fallback is declared, not silent: a client that does not support
elicitation gets the previous behaviour, and the bridge writes on stderr that
it could not ask.

#### Five tools that check the TODO against reality

A 16,000-line file rots silently. Five probes now check it, and between them
they found, in one day:

- **five wrong API signatures** out of 181 cited — including one the entry
  itself rested a decision on (`MirrorComponents3` has fourteen arguments, the
  entry said thirteen);
- **one broken cross-reference** out of 73 (`vedi C8` meant `Z-C8` — the
  sentence's own subject proved which);
- **two dead `file:line` citations** out of 96 — one of which pointed at C#
  that no longer exists at all, because that test became a set of declared
  cases. *A citation does not move, it breaks, and whoever follows it
  concludes the entry is stale;*
- three distinct categories of unresolvable path — another repository's, a
  closed entry's historical name, and a transient working file.

Every one of those probes **over-reported on its first draft**: 33 paths, 23
signatures, 12 folders belonging to the other repo. None of those were defects
in the TODO — they were the boundary of what the rule could see. *A probe that
shouts is a probe nobody reads*, and narrowing it to shut it up is worse: the
exception gets declared with its reason, and the probe stays wide.

And the three that need nothing but the repository now run **from the pre-push
hook**, because a probe that runs when you remember is a probe that does not
run on the day it matters. They were watched firing both ways first — exit 1
on a deliberately broken copy of the TODO, exit 0 on the real one — which is
why they take the file as an argument: a guard you cannot make fail is a guard
you cannot believe.

The other two stay out of the hook on purpose: they read the SOLIDWORKS
interop, and a hook that fails on a machine without the CAD is a hook the
first person to clone switches off.

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

> **Rectified in Z-C21.9 (finding L8-3):** that measure could not tell the two
> branches apart. All three point sets had P1->P2 along the global X axis, and
> there the two coincide by construction. IModelDoc2~CreatePlaneFixed2.html,
> Remarks: with `true` the plane is not reoriented, its own X axis is turned to
> the projected global X. So the normal is the same either way and the plane's
> X axis is not; nothing in the catalogue can see that yet (comment in
> `OperazioniPiani.cs`).

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
blocco rettangolare, con i numeri rimisurati oggi:

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

Il commit sta nel monorepo dell'altro progetto, non in questo repository: qui
non cambia una riga. Lo si racconta qui perche' la voce B6 vive in questo TODO, e
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
[agente] tavola creata da <il modello di tavola dell'officina>
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
consumatori oltre a quello che lo definisce, e usano `.nome`, `.provata`,
`.precondizione`, tutti campi che il record C# ha. *(I percorsi e le righe
stavano qui per esteso, e sono di un altro repository, privato: tolti il
24/9/2026 con il censimento.)* Ma `ai_disegno.py` tiene
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
