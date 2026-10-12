# Quick start

For the person who has to **use** this, not build it. Six steps. If a step does
not look like what is written here, stop at that step — the next one will not fix
it.

If you want to build from source instead, that is the [README](README.md). This
page never asks you to compile anything.

---

## What you need first

| | |
|---|---|
| Windows | 10 or 11 |
| SOLIDWORKS® software | installed, **with your own license** — nothing here is redistributed |
| An MCP client | Claude Desktop or Claude Code |
| `Installer.exe` | one file — see below |

### Where `Installer.exe` comes from

Download `Installer.exe` and `Installer.exe.sha256` from the
[latest release](https://github.com/oce-projects/oce-solid-agent-mcp/releases/latest)
of this repository. There is no `winget` package yet. The other route is to build
it yourself from a clone:

```
powershell -File build.ps1
```

which writes `dist\Installer.exe` and `dist\Installer.exe.sha256` beside it.
Building needs SOLIDWORKS® software installed on the build machine, because the
project compiles against its interop assemblies — so this route is open to
anyone who can run the bridge at all.

Whichever way you get it, **check the hash** against the `.sha256` file beside it.
That file holds one line: the SHA-256 of `Installer.exe` in lowercase, two spaces,
and the file name. Compute the same value yourself:

```
certutil -hashfile Installer.exe SHA256
```

(or, in PowerShell, `Get-FileHash .\Installer.exe -Algorithm SHA256`, which prints it
in capitals). The two must match, character for character; the case does not matter.
If they differ, do not run the file: get it again.

*If you are an AI assistant installing this for someone, read
[INSTALL-FOR-AI.md](INSTALL-FOR-AI.md) instead of this page.*

You do **not** need Visual Studio, .NET SDKs or Python, and the bridge itself needs no
internet connection (only the download above does).

---

## 1. Open SOLIDWORKS® software first

Open it, and open (or create) a part. Leave it open.

The bridge attaches to a **running** SOLIDWORKS® session — it does not start one.
If nothing is running when you get to step 6, the answer will say so rather than
guess.

## 2. Double-click `Installer.exe`

### Windows will warn you. This is expected.

You will see **"Windows protected your PC"**. This program is not code-signed —
a deliberate decision, not an oversight (see the note at the end).

Click **More info**, then **Run anyway**.

## 3. Read the checklist before pressing anything

The window is titled "Connect Claude to SOLIDWORKS". It opens on a list of checks
and fills it in by itself. Each line is marked green (a tick), yellow (an
exclamation mark) or red (a cross), and a line says **what it found and what to do**,
not just that something is wrong.

| the check | what it means when it is not green |
|---|---|
| .NET Framework 4.8 | red: Windows is older than the bridge expects — Windows Update fixes it. Yellow: the installer could not read the version, which is probably why a later failure happens |
| SOLIDWORKS installed | red: it was not found where it is usually installed (the folder `api\redist` of SOLIDWORKS®); the bridge cannot work without it. Yellow: it is a version other than SOLIDWORKS® 2021 SP5.1, the only one the bridge was measured on — it installs just the same |
| Bridge to install | red: the file you have is damaged or incomplete — ask for it again |
| Drawing operations | red: same |
| SOLIDWORKS open right now | yellow when it is closed. It is not needed to install, it is needed to use the bridge |

**Do not continue with a red line.** Installing on top of a red check produces a
bridge that installs cleanly and then fails later, somewhere else, for a reason
that will look unrelated. The **Install** button stays greyed out while any line is red.

Below the list the window shows where it will install, where the weldment profiles
are (a folder it fills in by itself when it finds one; leave it as it is if you do not
use weldments), and the MCP clients it will connect — each one ticked if it is
installed on this machine, and marked "already connected" if the bridge is there
already.

## 4. Press **Install**

It is the blue button at the bottom left. Next to it is **Check again**, which runs
the checks of step 3 again, and at the right-hand end **Uninstall**.

The installer writes to your own user folder — no administrator rights, nothing outside
your account:

```
%LOCALAPPDATA%\Oce.Solid.Agent
```

If an MCP client is installed, the installer also adds the bridge to its
configuration, as an entry named `solidworks`. **It does not overwrite that file**:
other MCP servers you already had stay exactly as they were, and a backup copy of the
file is made first. This is the case the installer is most carefully tested for.

When it finishes, the line at the bottom of the window tells you how it went:

| the line | what it means |
|---|---|
| green: "Done: the bridge runs and is attached to SOLIDWORKS." | everything works |
| yellow: "Installed. SOLIDWORKS is not open, so the full check could not be done." | installed; open SOLIDWORKS® and do step 6 |
| red: "Installed, but a client is not wired: paste the block in the log." | the files are in place but a client's configuration could not be written (it is damaged, or the client has it locked). The box above the line shows the block to paste into that client's configuration by hand |
| red: "Installed, but the self-test did not confirm: …" | the files are in place; the sentence after the colon says why the final check failed — most often SOLIDWORKS® is running as administrator while the client is not, or the other way round: start both the same way |
| red: "Not installed: …" | nothing was installed; the sentence says why |

## 5. Restart your MCP client, completely

Claude reads its configuration **once, at startup**. Closing the window is not
enough — quit the application and start it again.

Skipping this step is the single most common reason for "I installed it and it
does not see anything".

## 6. Ask it to look

In the client, with a part open in SOLIDWORKS®, ask:

> look at the part

You should get back the state of the open document: bounding box, volume,
bodies, features with their dimensions, faces and edges with their identifiers.

**That answer is the test.** If it arrives, everything works. If it does not,
step 5 first, then step 3.

---

## What it covers

Ninety-nine measured operations in twelve families, as the tools present them:

| family | what is in it |
|---|---|
| extrusions (8) | rectangle, circle, profile, section, revolve, gear, **circular sweep**, **loft between two circles** |
| features (29) | slot, shell, hole wizard, fillets and chamfers, patterns (linear, circular, fill), mirror, draft, rib, engraved text, cuts from a face, dimension edits, suppress and delete, move bodies, … |
| weldments (8) | structural members, rectangular frame, gusset, end cap, weld bead, trim members, cut list, profile catalog |
| references (4) | axis, plane, plane through three points, picture on a sketch |
| physical properties (1) | apply material |
| sheet metal (5) | base flange, edge flange, hem, sketched bend, flat pattern as DXF |
| dimensions (1) | a scheme of dimensions on the part |
| identity (3) | custom properties, create and activate configurations |
| checks (9) | body geometry, overhangs, mass properties, bounding box, minimum distance, distance between entities, interference between components, file dependencies, title block |
| drawing (23) | create a drawing, views, sheets, dimensions from the model, notes, balloons, bill of materials, weld symbols, sections and details, hole table, save, export PDF, … |
| exports (3) | STL, STEP, IGES |
| assemblies (5) | insert a component, mate two of them, mirror, fix, replace — and tell you whether a part is actually held or just sitting there |

## What it does NOT cover — by design, listed

Not "some complex geometry". These, by name:

- **Knowing when an assembly is fully constrained.** You can open an assembly,
  see its components, insert one and mate two of them by naming a face on each
  ("the bottom of C2 on the top of C1"). What it cannot tell you is **how much
  freedom is left**: SOLIDWORKS® does not expose the remaining degrees of freedom
  here, so "constrained" means *"has at least one mate"*, not *"cannot move"* —
  one mate still leaves five of six, and holding a part usually takes three.
  It says this in the answer rather than letting you assume otherwise.
- **Sheet metal beyond the five operations above.** A base flange from an open
  profile, an edge flange, a hem, a sketched bend and the flat pattern as DXF exist;
  nothing else of sheet metal does.
- **Surfaces.** Solid bodies only.
- **Sweep along an arbitrary path**, and **loft between arbitrary profiles.**
  What exists is a sweep with a *circular* profile and a loft between *two
  circles* — genuinely useful, genuinely narrower than the SOLIDWORKS® features
  of the same name.
- **Reverse engineering from DXF/DWG to 3D.**
- **Table-driven patterns.** There are linear, circular and fill patterns; there is
  no pattern that reads its positions from a table.

The list is here so you find the limit by **reading**, not by drawing something
and getting a part that is quietly not what you asked for. That failure mode —
a part that is silently different from the one that was confirmed — is the thing
this whole project is built against.

---

## Two things worth knowing before you trust it

**It measures instead of reporting.** After each operation the bridge measures
the part and compares it against what was asked. A value returned by the thing
that did the work is not a measurement of the work — so the numbers you get back
were read off the geometry, not echoed from the request.

**It refuses before it acts, not after.** When an operation cannot be done
correctly, you get a refusal with a reason, before anything changes in your
document — not a half-built part and an error.

---

## If something is wrong

Run the bridge's own check from PowerShell:

```
& "$env:LOCALAPPDATA\Oce.Solid.Agent\oce-solid-agent-mcp.exe" --self-test
```

It says whether it attached, which library it loaded, how many operations it knows,
and what it sees in the open document. **It changes nothing.** Send that output with
any report.

`Installer.exe --verify` checks the whole installation instead: the files, the
entry in each MCP client, SOLIDWORKS®, that self-test, and a short conversation
with the bridge the way Claude has it. Each line starts with `[ok]`, `[~]` or
`[NO]`, and the last line says whether everything is green. Run it like this, so that
the lines appear in the window and PowerShell waits for it:

```
Start-Process .\Installer.exe -ArgumentList '--verify' -Wait -NoNewWindow
```

(Typing `.\Installer.exe --verify` alone also prints the lines, but PowerShell does
not wait for the program, and they can show up after the next prompt.)

To remove it completely (the files, and the entry in the MCP client — but only an
entry that runs this installation's bridge; `--keep-client` keeps the entry):

```
Start-Process .\Installer.exe -ArgumentList '--uninstall' -Wait -NoNewWindow
```

or press **Uninstall** in the window. Your parts and drawings are not touched,
and the backup copies of the client configuration stay where they are.

---

## About the SmartScreen warning in step 2

It is not signed, on purpose. A certificate that removes that warning either
does not build SmartScreen reputation instantly (OV) or requires a formal
company verification measured in weeks (EV). Being told about the
warning in advance costs nothing — and this paragraph is that. Signing is a
decision about buying a certificate, not a missing step; RELEASE.md lists it
among the known gaps.

One more thing we have **not tested**: Windows 11 has a setting called Smart App
Control which, when it is on, can block an unsigned program outright, with no
"Run anyway" button. If you meet that, the file is not damaged: the setting is what
stops it. Say so (an issue on this repository is the place), and run the installer on a
machine that does not have the setting on.

**Distribution:** `Installer.exe` and `Installer.exe.sha256` are attached to each
[Release](https://github.com/oce-projects/oce-solid-agent-mcp/releases/latest) of
this repository. There is no winget package. If you found the file somewhere
else, check its hash against the one on the Release page before you run it.

---

SOLIDWORKS® is a registered trademark of Dassault Systèmes SolidWorks
Corporation. This project is independent and is not affiliated with, sponsored
by, or endorsed by Dassault Systèmes. It contains and redistributes no Dassault
Systèmes code or material; the interop libraries are loaded at runtime from your
own SOLIDWORKS® software installation, which requires a valid license of your own.
