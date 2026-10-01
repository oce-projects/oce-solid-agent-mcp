# Installing this bridge, for an AI assistant

You are reading this because you are installing **OCE Solid Agent** on someone
else's Windows machine, on their behalf. A human reading this instead should
use [QUICKSTART.md](QUICKSTART.md) — same steps, written for hands and eyes.

Everything below was run against the installer's own code. Where a message is
quoted, it is quoted **in Italian**, because that is what the program prints:
match the substring, do not translate it before matching.

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
| `--disinstalla` | remove the installation |

### Read the exit code, not the output

| code | what it means | what to do |
|---|---|---|
| **0** | installed, and the self-check confirmed it — *or* the only thing missing was that SOLIDWORKS is closed | done. Go to step 4 |
| **1** | **nothing was installed.** Either a prerequisite failed (`[NO]` lines, then `Mi fermo: ci sono cose da sistemare.`) or the copy itself threw | read the `[NO]` lines back to the user. Do not retry blindly |
| **2** | the files are installed, but the self-check did not confirm | not a failure to repeat: go to step 4 and find out why |

The prerequisite lines are prefixed `[ok]`, `[~] ` and `[NO]`. **Only `[NO]`
stops the install** — `[~]` is a warning, and "SOLIDWORKS is not running" is one
of them.

## 3. What it changed on the machine

- **Files** in `%LOCALAPPDATA%\Oce.Solid.Agent` (or `--cartella`).
- **The MCP client config**, unless `--senza-client`:
  - Claude Desktop → `%APPDATA%\Claude\claude_desktop_config.json`
  - Claude Code → `%USERPROFILE%\.claude.json`
- **A backup of each config it touched**, beside the original, named
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

The installer runs this itself, but run it again after the user has restarted
their client:

```
%LOCALAPPDATA%\Oce.Solid.Agent\oce-solid-agent-mcp.exe --prova
```

It writes to **stderr** (stdout belongs to the protocol), exits **0** when the
bridge is up, and its last line is exactly:

```
[prova] il ponte sta in piedi.
```

Match that substring, case-insensitively. Before it you get the SOLIDWORKS
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
installer treats the phrase `non risulta aperto` in the self-check as success,
because a bridge that cannot attach to a session it does not have is not a
broken install. Ask the user to open SOLIDWORKS with a part, then run `--prova`
again.

From 0.88.0 the bridge tells the two absences apart. `non risulta aperto`
means installed and not running. `non e' installato su questa macchina, o non
e' registrato` means Windows does not know `SldWorks.Application` at all:
opening it will not help, it has to be installed or repaired from the
Installation Manager — and the installer does **not** treat that one as
success.

**2. Claude Code's file is `.claude.json`, not `.mcp.json`.** In the user's
profile root, with a leading dot. Writing the wrong one produces no error and
no server.

**3. `--silenzioso` only wires clients whose folder already exists.** A client
the user has installed but never opened may have no folder, and it will be
skipped **silently** — the run still exits 0. If the user expects a specific
client, check that file afterwards, or name it with `--config`.

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

It removes the files and **deliberately leaves the MCP client entry alone** —
someone uninstalling may be about to reinstall, and quietly editing their config
would be doing more than was asked. If the user wants the entry gone too, tell
them it is there and let them say so.

The `.bak` files from step 3 also stay (the last three). They are theirs.

---

*This page is in English because it sits next to QUICKSTART.md, which is also
in English. The program's own messages are in Italian, and are quoted here
verbatim for that reason: a substring you match must be the one the program
actually prints.*
