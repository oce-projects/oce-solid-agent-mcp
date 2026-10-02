# Installing this bridge, for an AI assistant

You are reading this because you are installing **OCE Solid Agent** on someone
else's Windows machine, on their behalf. A human reading this instead should
use [QUICKSTART.md](QUICKSTART.md) — same steps, written for hands and eyes.

Everything below was run against the installer's own code. Where a message is
quoted, it is quoted **as the program prints it**, in Italian or in English (the
program is being translated, and new messages are born in English): match the
substring, do not translate it before matching.

---

## 0. What must be true before you start

| | how to check | if it is not true |
|---|---|---|
| Windows 10 or 11 | — | nothing here works elsewhere |
| SOLIDWORKS® installed, with the user's own licence | the installer checks it for you in step 2 | stop and say so — no licence is shipped here, and none can be |
| SOLIDWORKS® **running** | ask the user to open it | you can still install; the final check needs it running, and measures a part only if one is open |
| An MCP client: Claude Desktop or Claude Code | the installer finds them itself | with neither, install with `--senza-client` and wire it by hand |
| `Installa.exe` | see step 1 | — |

You do **not** need administrator rights, Visual Studio, a .NET SDK, Python, or
an internet connection. The installer says so itself: it writes into the user's
own folder and does not touch the system registry.

## 1. Getting `Installa.exe`

**There is no download page yet.** This is a real gap, not something you have
missed: check the repository's Releases; if it is empty, the file has to come
from the maintainer, or be built from a clone with

```
powershell -File build.ps1
```

which writes `dist\Installa.exe` (about 940 kB) and `dist\Installa.exe.sha256`
beside it. Building needs SOLIDWORKS installed on the build machine, because
the project compiles against its interop assemblies.

**Never** fetch an `Installa.exe` from anywhere other than the maintainer or a
repository Release, and say out loud where the one you are using came from.

## 2. The install itself

```
Installa.exe --silenzioso
```

| flag | what it does |
|---|---|
| `--cartella <path>` | where to install. Default: `%LOCALAPPDATA%\Oce.Solid.Agent` |
| `--redist <path>` | the `api\redist` folder of SOLIDWORKS, if it is not where the installer looks |
| `--senza-client` | do not touch any MCP client configuration |
| `--config <path>` | wire **that one file** and do not go looking for clients |
| `--weldment-profiles <folder>` | the weldment profiles (`.sldlfp`) the `profili_saldatura` operation reads, written as `PROFILI_SALDATURA` in the client entry's `env`. Without it the installer keeps a folder already in the client entry, else uses the folder SOLIDWORKS itself is set to (Tools > Options > File Locations > Weldment Profiles), or the one SOLIDWORKS installs (`<SOLIDWORKS>\lang\<language>\weldment profiles`), and says which; it never writes a folder that does not exist. A folder given here that does not exist stops the run with exit 1, before anything is installed |
| `--log <file>` | also write every line of the run to this file (UTF-8) |
| `--disinstalla` | remove the installation (see section 6) |
| `--verify [--json]` | check an existing installation instead of installing (section 4) |

### Read the exit code, not the output

`Installa.exe` is a Windows program, not a console one: **how you start it
decides whether the shell waits for it and sees its exit code.** Measured in a
fresh console (from 0.126.0):

| shell | use this | exit code |
|---|---|---|
| PowerShell | `(Start-Process .\Installa.exe -ArgumentList '--silenzioso' -Wait -PassThru -NoNewWindow).ExitCode` | right |
| cmd | in a batch file: `start "" /wait Installa.exe --silenzioso`, then `%ERRORLEVEL%` | right |
| PowerShell | `.\Installa.exe --silenzioso` (plain call) | **lost**: PowerShell does not wait |
| PowerShell | `cmd /c "start "" /wait Installa.exe --silenzioso"` on one line | **wrong**: always 0 |

The output appears on the console from 0.126.0 (before, nothing at all), and
`--log <file>` keeps a copy whatever the shell does.

| code | what it means | what to do |
|---|---|---|
| **0** | installed, **every client it tried is wired** (or `--senza-client`), and the bridge's self-test confirmed it — attached, or SOLIDWORKS simply closed | done. Go to step 4 |
| **1** | **nothing was installed.** A prerequisite failed (`[NO]` lines, then `Mi fermo: ci sono cose da sistemare.`), `--weldment-profiles` named a folder that does not exist, or the copy itself threw | read the `[NO]` lines back to the user. Do not retry blindly |
| **3** | the files are installed, but **a client was not wired** (its config is malformed or locked) or no client was found. The block to paste by hand is printed after the line `Paste this into the MCP client's configuration file` | fix what the line above the block says, or paste the block; then run `--verify` |
| **2** | installed and wired, but the self-test did not confirm (SOLIDWORKS not installed or not registered; SOLIDWORKS running but not answering, usually because it was started as administrator; or another failure) | not a failure to repeat: go to step 4 and find out why |

When both 3 and 2 apply, the code is 3. Until 0.125.0 the code was 0 even when
no client had been wired: an older installer's 0 does not mean the client is
wired.

The prerequisite lines are prefixed `[ok]`, `[~] ` and `[NO]`. **Only `[NO]`
stops the install** — `[~]` is a warning, and "SOLIDWORKS is not running" is one
of them.

## 3. What it changed on the machine

- **Files** in `%LOCALAPPDATA%\Oce.Solid.Agent` (or `--cartella`).
- **The MCP client config**, unless `--senza-client`:
  - Claude Desktop → `%APPDATA%\Claude\claude_desktop_config.json`
  - Claude Code → only when it is on the machine: its `.claude.json` exists,
    or its own folder does (`%USERPROFILE%\.claude`, or `CLAUDE_CONFIG_DIR`),
    or `claude` is on PATH. Its file is `.claude.json` in `CLAUDE_CONFIG_DIR`
    when that is set, else in `%USERPROFILE%`: where Claude Code reads it.
    From 0.126.0, when `claude` is on PATH, through Claude Code's own `claude
    mcp remove --scope user solidworks` and `claude mcp add --scope user
    solidworks [-e K=V] -- "<exe>"`; otherwise (or if that fails, and it says
    so) by writing that file itself. A running Claude Code rewrites that file
    on its own every few tens of seconds, so its own command is the safer
    writer; whether a running session can still lose an entry written by the
    file route is not yet measured.
  - the entry's `env` holds `SW_REDIST` when SOLIDWORKS is not in its usual
    folder, and `PROFILI_SALDATURA` when a weldment profiles folder is known.
    On a reinstall every other variable already in the `solidworks` entry
    stays (for example `RISCONTRO_RADICE`), and so does a `PROFILI_SALDATURA`
    that names a folder that exists, unless `--weldment-profiles` names
    another; the output says what it kept.
- **A backup of each config it touched**, the `claude mcp` route included, beside the original, named
  `<original>.prima-di-oce-solid-agent-<yyyyMMdd-HHmmss>.bak`. From 0.88.0
  only the **last three** per file are kept; older ones with that exact prefix
  are removed, and the output says how many. Copies anyone made by hand are
  never touched.
- **A line saying which version replaced which**: `installo la X` on a fresh
  install, `aggiorno dalla X alla Y` over an older one, `reinstallo la X` over
  the same. Read it back to the user.
- **If the client config is locked by another program** (usually the client
  itself, open), nothing is touched and the message says `bloccato da un altro
  programma`: ask the user to close the client and run the installer again.
- **Nothing in the registry, and nothing outside the user's profile.**

## 4. Verifying — and this is the step that is actually worth something

From 0.126.0 the installer checks an installation for you:

```
Installa.exe --verify --json
```

One line of JSON: the installed files and their versions (`files`,
`bridgeVersion`, `libraryVersion`), for each client whether its `solidworks`
entry runs the installed exe (`clients[].pointsToInstalled`) and its
`weldmentProfiles`, where SOLIDWORKS is (`solidworks`), the bridge's self-test
code (`selfTest.code`), and an MCP handshake with the installed exe —
`initialize` and `tools/list` over stdio, as a client does
(`handshake.ok`, `handshake.tools`). `checks[]` gives each check as `ok`,
`warn` or `fail`. Exit **0** only when everything is `ok`; **2** when nothing
failed but something is a warning (SOLIDWORKS not running, a SOLIDWORKS version
nobody measured, no weldment profiles); **1** when something failed. Without
`--json` the same checks come as readable lines. Launch it the way the table
in section 2 says.

The bridge's own self-test, which `--verify` runs too:

```
%LOCALAPPDATA%\Oce.Solid.Agent\oce-solid-agent-mcp.exe --prova
```

It writes to **stderr** (stdout belongs to the protocol). **Read its exit code**
(from 0.126.0; it is a console program, so the plain call waits):

| code | meaning |
|---|---|
| 0 | attached to SOLIDWORKS (with or without a document open) |
| 3 | SOLIDWORKS installed but not running |
| 4 | SOLIDWORKS not installed, or not registered: opening it will not help |
| 5 | not tried: `OCE_NO_ATTACH` is set |
| 6 | SOLIDWORKS runs in this Windows session but did not answer: it runs as administrator while this process does not (or the other way round), or it is still starting. The installer does **not** count it as installed |
| 1 | anything else: read the lines |

Its last line when attached is still `[prova] il ponte sta in piedi.`, for a
person to read. Before it you get the SOLIDWORKS
version, the library version, where the catalogue was loaded from, and — if a
part is open — a measurement of it: `volume_mm3`, `bodies_total`. **The
SOLIDWORKS version line means the bridge has talked to SOLIDWORKS; the volume
means it has also read a part.** That is the difference between "the process
started" and "it works", and it is the whole reason this flag exists.

With SOLIDWORKS open and **no document**, it says
`[prova] nessun documento aperto: agganciato, niente da misurare.` and still
exits 0 — attached and nothing to measure is a working bridge. From 0.87.0 the
model can start a part itself, with the `document` tool (`action: "create"`),
instead of asking the user to click File > New; from 0.88.0 it can also close
one (`action: "close"`), and it refuses to close a document with unsaved
changes. (Before 0.86.0
this case exited 1, and the installer reported a successful install as
unconfirmed.)

Then tell the user to **restart their MCP client**. A client started before the
config was written does not know about the new server, and nothing in the
output will tell you that.

## 5. The five ways this goes wrong, named

**1. SOLIDWORKS is closed, and it looks like a failure.** It is not. The
installer treats the self-test's exit code 3 (not running) as success, because
a bridge that cannot attach to a session it does not have is not a broken
install. Ask the user to open SOLIDWORKS with a part, then run `--verify`
again. Exit code 4 means Windows does not know `SldWorks.Application` at all:
opening it will not help, it has to be installed or repaired from the
Installation Manager — and the installer does **not** treat that one as
success. Exit code 6 means SOLIDWORKS is visibly open and did not answer: it
is probably running as administrator and the client is not (or the other way
round), and they must be started the same way; the installer exits 2 for it.
(Until 0.125.0 the installer decided this from the self-test's wording, and the
administrator case looked like a closed SOLIDWORKS; from 0.126.0 it reads the
code.)

**2. Claude Code's file is `.claude.json`, not `.mcp.json`.** In the user's
profile root, with a leading dot. Writing the wrong one produces no error and
no server.

**3. `--silenzioso` only wires clients it finds.** Claude Desktop is found by
its folder (`%APPDATA%\Claude`), Claude Code by its `.claude.json`, its
`.claude` folder or `claude` on PATH. A client the user has installed but never
opened may have none of these, and it is skipped.
If no client at all is found the run exits 3 and prints the block to paste;
if another one is found it exits 0. If the user expects a specific client,
check `--verify`, or name its file with `--config`.

**4. A malformed client config is never overwritten.** The installer refuses
and says `non e' JSON valido e non me la sento di riscriverlo`. This is
deliberate: that file can hold ten other servers someone configured by hand.
**Do not "fix" it by replacing it.** Open it, show the user the broken part, let
them decide.

**5. A non-standard `api\redist` stops everything with exit 1.** If SOLIDWORKS
is installed somewhere unusual, pass `--redist <path>` rather than concluding
that SOLIDWORKS is missing.

And one that is not the installer's fault: **PowerShell execution policy**. If
you drive `build.ps1` and get `cannot be loaded because running scripts is
disabled`, use `powershell -ExecutionPolicy Bypass -File build.ps1` for that one
call. Do not change the machine's policy on the user's behalf.

## 6. Removing it

```
Installa.exe --disinstalla
```

From 0.126.0 it removes the files, the `domande-senza-risposta.jsonl` the bridge
writes beside itself, **and the `solidworks` entry of each MCP client** — but
only an entry that runs the bridge in that folder; an entry pointing elsewhere
is left alone, and the output says so. `--keep-client` leaves the entries in
place (someone about to reinstall). With `--config <file>` only that file is
touched. Exit **0** when done, **1** when a file of the installation could not
be removed (`could not be removed`, usually because a client still runs the
bridge: close it and run again), **3** when the files are gone but an entry
could not be removed. (Until 0.125.0 the command line left the entries and
exited 0 even with a file left behind.)

The `.bak` files from step 3 stay (the last three). They are theirs.

---

*This page is in English because it sits next to QUICKSTART.md, which is also
in English. The program's own messages are in Italian, and are quoted here
verbatim for that reason: a substring you match must be the one the program
actually prints.*
