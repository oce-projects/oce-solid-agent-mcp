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
| SOLIDWORKS® software | installed, **with your own licence** — nothing here is redistributed |
| An MCP client | Claude Desktop or Claude Code |
| `Installa.exe` | one file — see below |

### Where `Installa.exe` comes from

Check this repository's **Releases** first. **If it is empty, it is empty** —
there is no download page and no `winget` package yet, and that is a gap, not
something you have failed to find. In that case the file comes from the
maintainer directly, or you build it yourself from a clone:

```
powershell -File build.ps1
```

which writes `dist\Installa.exe` and `dist\Installa.exe.sha256` beside it.
Building needs SOLIDWORKS® software installed on the build machine, because the
project compiles against its interop assemblies — so this route is open to
anyone who can run the bridge at all.

Whichever way you get it, **check the hash** against the `.sha256` beside it:

```
certutil -hashfile Installa.exe SHA256
```

*If you are an AI assistant installing this for someone, read
[INSTALL-FOR-AI.md](INSTALL-FOR-AI.md) instead of this page.*

You do **not** need Visual Studio, .NET SDKs, Python, or an internet connection.

---

## 1. Open SOLIDWORKS® software first

Open it, and open (or create) a part. Leave it open.

The bridge attaches to a **running** SOLIDWORKS® session — it does not start one.
If nothing is running when you get to step 6, the answer will say so rather than
guess.

## 2. Double-click `Installa.exe`

### Windows will warn you. This is expected.

You will see **"Windows protected your PC"**. This program is not code-signed —
a deliberate decision, not an oversight (see the note at the end).

Click **More info**, then **Run anyway**.

If you would rather check the file first, you were given an `Installa.exe.sha256`
next to it:

```powershell
Get-FileHash .\Installa.exe -Algorithm SHA256
```

The value it prints must match the one in that file, character for character.

## 3. Read the checklist before pressing anything

The window opens on a list of checks and fills it in by itself. Each line is
green or red, and a red line says **what is missing**, not just that something
is.

| the check | what a red line means |
|---|---|
| .NET Framework 4.8 | Windows is older than the bridge expects — Windows Update fixes it |
| SOLIDWORKS® installed | it was not found where it is usually installed; the bridge cannot work without it |
| Bridge to install | the file you have is damaged or incomplete — ask for it again |
| Drawing operations | same |

**Do not continue with a red line.** Installing on top of a red check produces a
bridge that installs cleanly and then fails later, somewhere else, for a reason
that will look unrelated.

## 4. Press Install

It writes to your own user folder — no administrator rights, nothing outside
your account:

```
%LOCALAPPDATA%\Oce.Solid.Agent
```

If an MCP client is installed, the installer also adds the bridge to its
configuration. **It does not overwrite that file**: other MCP servers you
already had stay exactly as they were. This is the case the installer is most
carefully tested for.

## 5. Restart your MCP client, completely

Claude reads its configuration **once, at startup**. Closing the window is not
enough — quit the application and start it again.

Skipping this step is the single most common reason for "I installed it and it
does not see anything".

## 6. Ask it to look

In the client, with a part open in SOLIDWORKS®, ask:

> guarda il pezzo

You should get back the state of the open document: bounding box, volume,
bodies, features with their dimensions, faces and edges with their identifiers.

**That answer is the test.** If it arrives, everything works. If it does not,
step 5 first, then step 3.

---

## What it covers

Sixty-two measured operations, grouped as the tools present them:

| family | what is in it |
|---|---|
| extrusions (8) | rectangle, circle, profile, section, revolve, gear, **circular sweep**, **loft between two circles** |
| machining (21) | slot, shell, guided hole, fillets and chamfers, patterns, mirror, draft, engraved text, suppress, move bodies, … |
| welding (8) | structural members, rectangular frame, gusset, end cap, weld bead, cut list, profile catalogue |
| references (3) | axis, plane, plane through three points |
| identity (3) | custom properties, create and activate configurations |
| material (1) | apply material |
| checks (6) | body geometry, overhangs, mass properties, bounding box, minimum distance, distance between entities |
| drawing (7) | create drawing, views, standard views, sheet setup, dimensions from the model, save, export PDF |
| export (3) | STL, STEP, IGES |
| assemblies (2) | insert a component, mate two of them — and tell you whether a part is actually held or just sitting there |

## What it does NOT cover — by design, listed

Not "some complex geometry". These, by name:

- **Knowing when an assembly is fully constrained.** You can open an assembly,
  see its components, insert one and mate two of them by naming a face on each
  ("the bottom of C2 on the top of C1"). What it cannot tell you is **how much
  freedom is left**: SOLIDWORKS® does not expose the remaining degrees of freedom
  here, so "constrained" means *"has at least one mate"*, not *"cannot move"* —
  one mate still leaves five of six, and holding a part usually takes three.
  It says this in the answer rather than letting you assume otherwise.
- **Sheet metal.** No flanges, no bends, no flat patterns.
- **Surfaces.** Solid bodies only.
- **Sweep along an arbitrary path**, and **loft between arbitrary profiles.**
  What exists is a sweep with a *circular* profile and a loft between *two
  circles* — genuinely useful, genuinely narrower than the SOLIDWORKS® features
  of the same name.
- **Reverse engineering from DXF/DWG to 3D.**
- **Fill patterns and table-driven patterns.** Measured, documented, still not
  working.

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

```
%LOCALAPPDATA%\Oce.Solid.Agent\oce-solid-agent-mcp.exe --prova
```

Run it from PowerShell. It says whether it attached, which add-in it loaded, how
many operations it knows, and what it sees in the open document. **It changes
nothing.** Send that output with any report.

To remove it completely:

```
Installa.exe --disinstalla
```

---

## About the SmartScreen warning in step 2

It is not signed, on purpose. A certificate that removes that warning either
does not build SmartScreen reputation instantly (OV) or requires a formal
company verification measured in weeks (EV). While this is handed to people
directly, being told about the warning in advance costs nothing — and this
paragraph is that. When it is published to strangers, that decision gets
revisited.

**Distribution today:** the file, plus its checksum, handed over directly. There
is no public download page, and no winget package. If you found this file
somewhere else, you did not get it from us.

---

SOLIDWORKS® is a registered trademark of Dassault Systèmes SolidWorks
Corporation. This project is independent and is not affiliated with, sponsored
by, or endorsed by Dassault Systèmes. It contains and redistributes no Dassault
Systèmes code or material; the interop libraries are loaded at runtime from your
own SOLIDWORKS® software installation, which requires a valid licence of your own.
