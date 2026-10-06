# Installing this bridge, for an AI assistant

You are reading this because you are installing **OCE Solid Agent** on someone
else's Windows machine, on their behalf. A human reading this instead should use
[QUICKSTART.md](QUICKSTART.md) — same job, written for hands and eyes.

This page is a procedure you can run and check. Every step has the same four parts:
**Run** (the command), **Expect** (the exit code and the lines you will see),
**If not** (what to do), and, where it matters, **Never** (what not to do). The
commands, options and exit codes below were read from the installer's source
(`Installer/`) and the bridge's (`Program.cs`), not guessed.

**Read the exit code, not the sentence.** Where a message is quoted, it is quoted as
the program prints it in this version; the program's text is for people and can be
reworded, the exit codes are the contract. Match a short piece of a line, never the
whole line.

**You are done when all of these are true:** `Installer.exe --verify` exits 0 (or 2
with only warnings you can explain to the user — see step 4), the user has restarted
their MCP client, and a first call of the `inspect_part` tool in that client answers
(step 5). Nothing less is "installed".

---

## 0. What must be true before you start

| | how to check | if it is not true |
|---|---|---|
| Windows 10 or 11 | — | nothing here works elsewhere |
| SOLIDWORKS® installed, with the user's own license | the installer checks it for you in step 2 | stop and say so — no license is shipped here, and none can be |
| SOLIDWORKS® **running** | ask the user to open it | you can still install; the final check needs it running, and measures a part only if one is open |
| An MCP client: Claude Desktop or Claude Code | the installer finds them itself | with neither, install with `--no-client` and wire it by hand (step 2, exit 3) |
| `Installer.exe` | step 1 | — |

You do **not** need administrator rights, Visual Studio, a .NET SDK, Python, or an
internet connection. The installer writes into the user's own folder and does not
touch the system registry. **Do not run it elevated:** if SOLIDWORKS® is running as
administrator and the client is not (or the other way round), the bridge cannot attach
(step 6, case 7); starting the installer as administrator does not fix that.

## 1. Get `Installer.exe` and check it

**There is no download page yet.** This is a real gap, not something you have missed:
check the repository's Releases; if it is empty, the file has to come from the
maintainer, or be built from a clone with

```
powershell -File build.ps1
```

which writes `dist\Installer.exe` and `dist\Installer.exe.sha256` beside it. Building
needs SOLIDWORKS® installed on the build machine, because the project compiles against
its interop assemblies, and MSBuild (Build Tools for Visual Studio 2022).

**Never** fetch an `Installer.exe` from anywhere other than the maintainer or a
repository Release, and say out loud where the one you are using came from.

**Run** (in PowerShell, in the folder that holds both files):

```powershell
$expected = (Get-Content .\Installer.exe.sha256 -Raw).Trim().Split(' ')[0]
$actual   = (Get-FileHash .\Installer.exe -Algorithm SHA256).Hash
$actual -ieq $expected
```

**Expect:** `True`. The `.sha256` file is one line, `<64 hexadecimal characters>` then two
spaces then `Installer.exe`. The same check with the Windows tool is
`certutil -hashfile Installer.exe SHA256`, which prints the hash on its second line.

**If not (`False`, or no `.sha256` file):** stop. Do not run the file. Tell the user the
hash does not match and ask where the file came from; get it again.

## 2. The install itself

**Run** — launch it the way your shell needs, because **how you start `Installer.exe`
decides whether the shell waits for it and sees its exit code.** `Installer.exe` is a
Windows program, not a console one. Measured in a fresh console:

| shell | use this | exit code |
|---|---|---|
| PowerShell | `$p = Start-Process .\Installer.exe -ArgumentList '--silent' -Wait -PassThru -NoNewWindow; $p.ExitCode` | right |
| cmd | in a batch file: `start "" /wait Installer.exe --silent`, then `%ERRORLEVEL%` | right |
| PowerShell | `.\Installer.exe --silent` (plain call) | **lost**: PowerShell does not wait |
| PowerShell | `cmd /c "start "" /wait Installer.exe --silent"` on one line | **wrong**: always 0 |

Add a log file whatever the shell does with the console; it keeps a UTF-8 copy of every
line of the run:

```powershell
$log = Join-Path $env:TEMP 'oce-install.log'
$p = Start-Process .\Installer.exe -ArgumentList '--silent', '--log', "`"$log`"" -Wait -PassThru -NoNewWindow
$p.ExitCode
```

If you cannot launch it in one of the forms above, launch it any way you can, then read
the log file and run step 4: `--verify` tells the truth whatever the shell did with the
exit code.

The options:

| option | what it does |
|---|---|
| `--silent` | install without a window: the mode this page uses |
| `--dir <path>` | where to install. Default: `%LOCALAPPDATA%\Oce.Solid.Agent`. `--verify` and `--uninstall` use it too |
| `--redist <path>` | the `api\redist` folder of SOLIDWORKS®, if it is not where the installer looks. `--verify` uses it too |
| `--no-client` | do not touch any MCP client configuration |
| `--config <path>` | wire **that one file** and do not go looking for clients. `--verify` and `--uninstall` use it too |
| `--weldment-profiles <folder>` | the weldment profiles (`.sldlfp`) the `weldment_profiles` operation reads, written as `PROFILI_SALDATURA` in the client entry's `env`. Without it the installer keeps a folder already in the client entry, else uses the folder SOLIDWORKS® itself is set to (Tools > Options > File Locations > Weldment Profiles), or the one SOLIDWORKS® installs (`<SOLIDWORKS>\lang\<language>\weldment profiles`), and says which; it never writes a folder that does not exist. A folder given here that does not exist stops the run with exit 1, before anything is installed |
| `--log <file>` | also write every line of the run to this file (UTF-8). Works with every mode that has no window. If the file cannot be created, the run stops with exit 1 |
| `--uninstall` | remove the installation (step 7) |
| `--keep-client` | with `--uninstall`: leave the MCP client entries in place |
| `--verify [--json]` | check an existing installation instead of installing (step 4) |
| `--help` | print the list of options |

An option and its value are two words (`--dir C:\Tools\oce`). Option names are matched
without regard to case. Quote a value that contains spaces.

**Expect.** The run prints, in this order: a first line `OCE Solid Agent - silent install`;
the five prerequisite checks, one line each (plus an indented line saying what to do, when there is something to do), prefixed `[ok]`, `[~] ` or `[NO]`
(**only `[NO]` stops the install**; `[~]` is a warning, and "SOLIDWORKS is not running" is
one of them); a line about the weldment profiles; the install lines (which version it is
putting where); one block per MCP client it wires; the output of the bridge's own
self-test followed by `self-test: exit <n>, <what it means>`; and a last line that says how it
ended. The five checks are titled `.NET Framework 4.8`, `SOLIDWORKS installed`,
`Bridge to install`, `Drawing operations`, `SOLIDWORKS open right now`.

Then read the **exit code**:

| code | what it means | what to do |
|---|---|---|
| **0** | installed, **every client it tried is wired** (or `--no-client`), and the bridge's self-test confirmed it — attached, or SOLIDWORKS® simply closed, or not tried because `OCE_NO_ATTACH` is set. Last line `Installed in <folder>` | done with this step. Go to step 4 |
| **1** | **nothing was installed.** A prerequisite failed (`[NO]` lines, then the line `I stop here: there are things to fix.`), or `--weldment-profiles` named a folder that does not exist (`[NO] weldment profiles: … Nothing installed.`), or the copy itself threw (`Install failed: …`), or the `--log` file could not be created, or an unhandled error (`Stopped by an error that was not handled: …`) | read the `[NO]` lines back to the user and fix what they say (step 6 has the usual causes). Do not retry blindly |
| **3** | the files are installed, but **a client was not wired** (its configuration is malformed or locked) or **no client was found** (`no MCP client found (Claude Desktop, Claude Code)`). The block to paste by hand is printed after the line `Paste this into the MCP client's configuration file (inside "mcpServers" if it has one already):`. Last line `Installed in <folder>, but not every MCP client is wired: paste the block above, then run Installer.exe --verify. (exit 3)` | fix what the line above the block says (step 6, case 4), or paste the block into the client's configuration yourself, preserving what is already in the file; then run step 4 |
| **2** | installed and wired, but the self-test did not confirm (SOLIDWORKS® not installed or not registered; SOLIDWORKS® running but not answering, usually because it was started as administrator; or another failure). Last line `Installed, but the dry run did not confirm: read above.` | not a failure to repeat: go to step 4 and find out why |

When both 3 and 2 apply, the code is 3. Until 0.125.0 the code was 0 even when no client
had been wired: an older installer's 0 does not mean the client is wired.

**The lines worth reading back to the user**, because they change something on their
machine:

- the version line: `installing <X>` on a fresh install, `updating from <X> to <Y>` over an
  older one, `reinstalling <X> (it was already this one)` over the same;
- `backup copy: <name>` — a backup of each client configuration it touches (see step 3);
- `added the entry "solidworks" in <client>` or `updated the entry "solidworks" in <client>`,
  and for Claude Code `Claude Code: registered with "claude mcp add --scope user"`;
- `adding SW_REDIST: …` and `adding PROFILI_SALDATURA: …` — the variables it wrote into the
  entry's `env`; `keeping from the existing entry's env: …` — variables already there that
  it left alone;
- `WARNING: <client> is open right now: close it and reopen it, or it will not read the new
  configuration.` — tell the user, it matters for step 5.

**Never:** answer a `[NO]` by running the installer again hoping it changes, or by editing
the installer's checks.

## 3. What it changed on the machine

- **Files** in `%LOCALAPPDATA%\Oce.Solid.Agent` (or `--dir`): `oce-solid-agent-mcp.exe`,
  `oce-solid-agent-mcp.exe.config`, `catalog.json`, `Oce.Solid.Agent.Core.dll`, `LICENSE`,
  `NOTICE` — six files.
- **The MCP client config**, unless `--no-client`. The entry is named `solidworks`:
  - Claude Desktop → `%APPDATA%\Claude\claude_desktop_config.json`
  - Claude Code → only when it is on the machine: its `.claude.json` exists, or its own
    folder does (`%USERPROFILE%\.claude`, or `CLAUDE_CONFIG_DIR`), or `claude` is on PATH.
    Its file is `.claude.json` in `CLAUDE_CONFIG_DIR` when that is set, else in
    `%USERPROFILE%`: where Claude Code reads it. When `claude` is on PATH it is written
    through Claude Code's own `claude mcp remove --scope user solidworks` and `claude mcp
    add --scope user solidworks [-e K=V] -- "<exe>"`; otherwise (or if that fails, and the
    output says so) by writing that file itself. A running Claude Code rewrites that file on
    its own every few tens of seconds, so its own command is the safer writer; whether a
    running session can still lose an entry written by the file route is not measured.
  - the entry's `env` holds `SW_REDIST` when SOLIDWORKS® is not in its usual folder
    (`C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS`), and `PROFILI_SALDATURA` when a weldment
    profiles folder is known. On a reinstall every other variable already in the
    `solidworks` entry stays (for example `RISCONTRO_RADICE`), and so does a
    `PROFILI_SALDATURA` that names a folder that exists, unless `--weldment-profiles` names
    another; the output says what it kept.
- **A backup of each config it touched**, the `claude mcp` route included, beside the
  original, named `<original>.prima-di-oce-solid-agent-<yyyyMMdd-HHmmss>.bak` (the prefix is
  still Italian). Only the **last three** per file are kept; older ones with that exact
  prefix are removed, and the output says how many. Copies anyone made by hand are never
  touched.
- **If the client config is locked by another program** (usually the client itself, open),
  nothing is touched and the message says `is locked by another program`: ask the user to
  close the client and run the installer again.
- **Nothing in the registry, and nothing outside the user's profile.**

## 4. Verify — the step that is actually worth something

**Run** (launch it as in step 2; the plain call loses the exit code):

```powershell
$p = Start-Process .\Installer.exe -ArgumentList '--verify', '--json' -Wait -PassThru -NoNewWindow
$p.ExitCode
```

Without `--json` the same checks come as readable lines, one per check, each prefixed `[ok]`,
`[~] ` or `[NO]`, then a last line: `verify: all green (exit 0)`, `verify: nothing failed,
warnings above (exit 2)` or `verify: something failed, see [NO] above (exit 1)`.

**Expect — exit code:**

| code | meaning | what to do |
|---|---|---|
| **0** | everything is `ok` | go to step 5 |
| **2** | nothing failed, but something is a warning: SOLIDWORKS® not running (or `OCE_NO_ATTACH` set), a SOLIDWORKS® version nobody measured, no weldment profiles folder, a client entry that points to another installation, or the installed bridge is a different version from this installer | read the warnings back to the user; each one is fine to accept or has an action in the table below |
| **1** | something failed | the `fail` check says what; use the table below |

**Expect — the JSON.** One line. Its keys: `installer` (version of this installer) and
`folder`; `files` (one object per installed file: `name`, `present`, `version`),
`bridgeVersion`, `libraryVersion`; `solidworks` (`redist`, `interop`, `year`); `clients` (one
object per client: `name`, `file`, `command`, `pointsToInstalled`, `weldmentProfiles`,
`readError`); `selfTest` (`code`, `meaning`); `handshake` (`ok`, `tools`, `serverVersion`,
`detail`); `checks` (each one `name`, `status` — `ok`, `warn` or `fail` — and `detail`); and
`ok` and `exit`. The handshake is an MCP conversation with the installed exe over stdio,
`initialize` then `tools/list`, as a client has it; it passes when the bridge answers with at
least one tool.

**The checks, and what a `warn` or `fail` asks of you** (`weldment profiles` appears only when at least one client runs the
installed bridge; on a machine where nothing is installed you get `files`, `solidworks`, `clients`, `self-test` and
`handshake`, all failing except possibly `solidworks`, and exit 1):

| check | `fail` when | `warn` when | what to do |
|---|---|---|---|
| `files` | a file of the installation is missing | the installed bridge is another version than this installer | run the installer again (to repair or update) |
| `solidworks` | `api\redist` is not found | SOLIDWORKS® is not the 2021 SP5.1 the bridge was measured on | `--redist <path>` or `SW_INSTALL_PATH`; for the warning, tell the user: it works, nobody has measured it |
| `clients` | no client runs the installed bridge, or none was found | one client does and another points elsewhere or has a config that cannot be read (a client with no entry at all is only mentioned in the detail) | run step 2 again, or paste the block, or name a file with `--config` |
| `weldment profiles` | — | `PROFILI_SALDATURA` is not set, or names a folder that does not exist | only matters if the user uses weldments: run step 2 again with `--weldment-profiles <folder>` |
| `self-test` | the bridge's self-test exits 1, 4 or 6, does not answer, or the bridge is not installed | it exits 3 (SOLIDWORKS® not running) or 5 (`OCE_NO_ATTACH` set) | see the table of the self-test below |
| `handshake` | the bridge did not answer `initialize` within 90 s or `tools/list` within 60 s, or answered no tools | — | run the self-test by hand (below) and read its lines |

The bridge's own self-test, which `--verify` runs too:

```
%LOCALAPPDATA%\Oce.Solid.Agent\oce-solid-agent-mcp.exe --self-test
```

In PowerShell, `& "$env:LOCALAPPDATA\Oce.Solid.Agent\oce-solid-agent-mcp.exe" --self-test`. It
writes to **stderr** (stdout belongs to the protocol). **Read its exit code** (it is a console
program, so the plain call waits):

| code | meaning |
|---|---|
| 0 | attached to SOLIDWORKS® (with or without a document open) |
| 3 | SOLIDWORKS® installed but not running |
| 4 | SOLIDWORKS® not installed, or not registered: opening it will not help |
| 5 | not tried: `OCE_NO_ATTACH` is set |
| 6 | SOLIDWORKS® runs in this Windows session but did not answer: it runs as administrator while this process does not (or the other way round), or it is still starting. The installer does **not** count it as installed |
| 1 | anything else: read the lines |

What it writes: `[self-test] attached: <True or False>`, `[self-test] drawing library: <path>`,
`[self-test] catalog: <path>`, `[self-test] first operations: <five names>`, and, when attached,
either `[self-test] no document open: attached, nothing to measure.` or — with a part open — the
lines `document_title`, `kind`, `volume_mm3` and `bodies_total`, then `[self-test] the bridge
works.` Before them the bridge's start-up line names the SOLIDWORKS® version
(`[bridge] SolidWorks <version>, drawing library <version>`). **The version line means the
bridge has talked to SOLIDWORKS®; the volume means it has also read a part.** That is the
difference between "the process started" and "it works", and it is the whole reason this flag
exists. Attached with no document is a working bridge and exits 0: the model can start a part
itself with the `document` tool (`action: "create"`) instead of asking the user to click File >
New, and it can close one (`action: "close"`), refusing to close a document with unsaved changes.

## 5. Restart the client and make the first real call

Tell the user to **restart their MCP client completely** (quit the application, start it
again; for Claude Code, start a new session). A client started before the configuration was
written does not know the new server, and nothing in the output will tell you that.

Then, with SOLIDWORKS® open and a part in it, call the `inspect_part` tool (or ask: "look at
the part"). **Expect** the state of the open document: bounding box, volume, bodies, features,
faces and edges with their identifiers. **If not**, in this order: the client was not
restarted; step 4 had a `fail`; SOLIDWORKS® was started as administrator and the client was not.

## 6. The ways this goes wrong, named

**1. SOLIDWORKS® is closed, and it looks like a failure.** It is not. The installer treats the
self-test's exit code 3 (not running) as success, because a bridge that cannot attach to a
session it does not have is not a broken install. Ask the user to open SOLIDWORKS® with a part,
then run `--verify` again. Exit code 4 means Windows does not know `SldWorks.Application` at
all: opening it will not help, it has to be installed or repaired from the Installation
Manager — and the installer does **not** treat that one as success.

**2. Claude Code's file is `.claude.json`, not `.mcp.json`.** In the user's profile root (or in
`CLAUDE_CONFIG_DIR`), with a leading dot. Writing the wrong one produces no error and no
server.

**3. `--silent` only wires clients it finds.** Claude Desktop is found by its folder
(`%APPDATA%\Claude`), Claude Code by its `.claude.json`, its `.claude` folder or `claude` on
PATH. A client the user has installed but never opened may have none of these, and it is
skipped. If no client at all is found the run exits 3 and prints the block to paste; if another
one is found it exits 0. If the user expects a specific client, check `--verify`, or name its
file with `--config`.

**4. A malformed client config is never overwritten.** The installer refuses and the message
says `is not valid JSON and I do not feel like rewriting it`. This is deliberate: that file can
hold ten other servers someone configured by hand. **Do not "fix" it by replacing it.** Open it,
show the user the broken part, let them decide.

**5. A non-standard `api\redist` stops everything with exit 1.** If SOLIDWORKS® is installed
somewhere unusual, pass `--redist <path>` (or set `SW_INSTALL_PATH` to the SOLIDWORKS® root, the
folder that ends in `\SOLIDWORKS`) rather than concluding that SOLIDWORKS® is missing.

**6. PowerShell execution policy.** If you drive `build.ps1` and get `cannot be loaded because
running scripts is disabled`, use `powershell -ExecutionPolicy Bypass -File build.ps1` for that
one call. Do not change the machine's policy on the user's behalf.

**7. SOLIDWORKS® and the client started at different privilege levels.** SOLIDWORKS® visibly
open and the self-test exits 6 (`--verify`: `self-test` fails; the installer: exit 2). The
running-object table is not shared between an administrator process and a normal one: start
SOLIDWORKS® and the MCP client the same way, then verify again. Do not start the installer as
administrator to "fix" it.

**8. Windows blocks the installer.** The file is not code-signed, so Windows SmartScreen shows
"Windows protected your PC" (More info, then Run anyway) — that is the user's decision, not yours: do not
remove the mark yourself (for example with `Unblock-File`) without asking them. On Windows 11 with Smart App Control on, an unsigned
program may be blocked with no way past: say so to the user.

## 7. Removing it

**Run:**

```powershell
$p = Start-Process .\Installer.exe -ArgumentList '--uninstall' -Wait -PassThru -NoNewWindow
$p.ExitCode
```

It removes the six installed files, the `.jsonl` log of unanswered questions the bridge writes
beside itself, **and the `solidworks` entry of each MCP client** — but only an entry that runs
the bridge in that folder; an entry pointing elsewhere is left alone, and the output says so.
`--keep-client` leaves the entries in place (someone about to reinstall). With `--config <file>`
only that file is touched. The folder is removed only if nothing else is in it.

**Expect:** exit **0** when done (last line `Uninstalled. (exit 0)`); **1** when a file of the
installation could not be removed (`<n> file(s) could not be removed: …` — usually because a
client still runs the bridge: close it and run again); **3** when the files are gone but an
entry could not be removed (`The files are removed, but a client entry is still there: see above.
(exit 3)`).

The `.bak` files from step 3 stay (the last three). They are the user's.

---

*The installer's own help is `Installer.exe --help`. The bridge lives in
`%LOCALAPPDATA%\Oce.Solid.Agent` unless `--dir` said otherwise; its configuration entry is
named `solidworks`.*
