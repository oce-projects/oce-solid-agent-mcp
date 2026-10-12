# Releasing

For the maintainer. If you want to **use** the bridge, this is not your page:
see [QUICKSTART.md](QUICKSTART.md).

## Why this is done by hand

No GitHub-hosted runner can build the bridge. `Oce.Solid.Agent.csproj` compiles
against the SOLIDWORKS® interop assemblies, which exist only on a machine with
SOLIDWORKS installed — on `windows-latest` the build stops at
`CS0246: IModelDoc2`. The CI compiles and runs the contract suite, which needs
no SOLIDWORKS, and says so; it does not produce an installer.

A self-hosted runner with a SOLIDWORKS license would automate this. It is a
cost and a machine to maintain, and for now the choice is a manual rite on a
workstation that has SOLIDWORKS. The risk of a manual rite is always the same —
a step skipped on the day there is a hurry — so the steps that can be checked
are checked by a script.

## The rite

**1. The version is bumped and the bench is green.** `VERSION.txt` says the new
version, `CHANGELOG.md` has an entry for it (a heading that starts `## X.Y.Z`), and
if the library changed its `AssemblyVersion` moved too. The bench ran on those bytes:
`Bench\bench.ps1`, exit 0. A whole run takes hours, not minutes: the last committed
report, `Bench/report/last.json`, says 17,381 seconds in its `duration_s` field, and
that field is the measure — this sentence is only an indication. Everything is
committed and pushed. The contract suite is built too
(`Tests\Oce.Solid.Agent.ContractTests.csproj`, which produces
`Tests\bin\Oce.Solid.Agent.ContractTests.exe`): the script of step 2 stops if the
executable is not there.

**2. Prepare.**

```
.\Checks\prepare-release.ps1
```

It stops at the first thing that is not right, and publishes nothing; it exits 0 when
the release is ready and 1 at the first check that fails, with a line that starts
`STOPPED:`. It checks that:

- `VERSION.txt` is `X.Y.Z` and `CHANGELOG.md` has an entry for it;
- the working tree is clean and `HEAD` is `origin/main` — a binary must come
  from a commit that others can resolve;
- the tag does not exist yet;
- `build.ps1` succeeds and `dist\Installer.exe` exists;
- the SHA-256 in `dist\Installer.exe.sha256` is **recomputed** and matches — it
  is the value people will check against, so a stale one would tell everyone
  their download had been tampered with;
- the contract suite is green, which includes "the version the bridge announces
  is `VERSION.txt`" and "the last bench run is newer than the last change to the
  library and to the bench".

It writes the release notes for this version, taken from the entry of `CHANGELOG.md`,
into a file in `dist\`, and prints the three commands for step 3 with that file's
name already in the last one.

**3. Publish — by a person.** The script prints these three commands; run them
as printed (`gh` is the GitHub command-line tool):

```
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin vX.Y.Z
gh release create vX.Y.Z dist\Installer.exe dist\Installer.exe.sha256 --title "vX.Y.Z" --notes-file <the notes file the script printed>
```

The script does not run these on purpose: publishing is an action outside this
machine, and it is decided, not scripted. The repository is public, so the
Release is public the moment it is created.

**4. Check what was published.** Download `Installer.exe` and `Installer.exe.sha256`
from the Release (not from `dist\`), compute the hash as QUICKSTART teaches and
compare, then run `Installer.exe --verify` once on a machine other than the one that
built it: it must exit 0, or 2 with warnings you can name
([INSTALL-FOR-AI.md](INSTALL-FOR-AI.md), step 4).

## Before publishing: GitHub tests the installer on a clean machine

The workstation builds the installer; a Windows machine that has never seen this
project is the only honest place to try it. GitHub provides one, and this step
uses it **before** anything is public. It is optional for the script of step 2
and expected by the owner for the first public release.

**1. Make a draft.** From the workstation, with the commit pushed and the tree clean:

```
.\Checks\release-candidate.ps1 -DryRun     # prints the plan, changes nothing
.\Checks\release-candidate.ps1             # tag v<VERSION.txt>-rc1
```

It runs `build.ps1`, recomputes the SHA-256, and uploads `dist\Installer.exe` and
`dist\Installer.exe.sha256` to a **draft prerelease** whose tag is
`v<version>-rc1` (or the `-Tag` you give: it must be `v<version>-<suffix>`). It reads
the release back and fails if GitHub does not say it is a draft. It never publishes,
never tags, never pushes, and stops if a published release already has that tag. The
last lines it prints are the command of the next step.

**2. Run the smoke test.**

```
gh workflow run installer-smoke.yml --ref <the branch with the workflow> -f tag=v<version>-rc1
gh run watch
```

`.github/workflows/installer-smoke.yml` runs by hand only, on `windows-latest` and
`windows-2022`. It downloads the two files from the draft, checks the hash as
[INSTALL-FOR-AI.md](INSTALL-FOR-AI.md) step 1 teaches, then runs `Installer.exe` as
that page's step 2 says (`Start-Process -Wait -PassThru -NoNewWindow`, `--silent`,
`--log`) in four scenarios, each with its assertions in
`.github/steps/installer-smoke.ps1`. The logs, and a copy of each client file after
each stage, are uploaded as an artifact.

**What it proves, and what it cannot.** A runner has no SOLIDWORKS, and the installer
refuses to install without it: its prerequisite "SOLIDWORKS installed" is blocking.
So the right result on a clean machine is **exit 1 with nothing installed**, not a
green install, and scenario 1 asserts exactly that: `[NO] SOLIDWORKS installed` is the
only `[NO]` line, no file is written, no client configuration changes, `--verify`
fails its five checks, an uninstall of nothing exits 0. Scenarios 2 to 4 then put an
**empty placeholder file** with the name of the interop where the prerequisite looks
(the check only asks whether the file exists; the placeholder is written by the
script, it is not Dassault's and cannot be loaded) and set `OCE_NO_ATTACH=1`, so the
bridge never tries to reach SOLIDWORKS. That proves the mechanics: the six files
land; Claude Desktop and Claude Code are found by the installer itself and their
entries are written without disturbing the other servers in the file; `--verify`
exits 2 with exactly one warning (the self-test, exit 5) and the MCP handshake
answers; a reinstall keeps a variable the user added; the uninstall removes files,
entries and the archive of measured profile sections in the default folder. It does
**not** prove that the bridge attaches to SOLIDWORKS, or that it works with the
real interop: only the bench and a machine with SOLIDWORKS can.

**Measured on 10 October 2026.** The workflow ran once, by hand, on the draft
`v0.130.1-rc1` (run 38033725360 on `main`, both runners green). The two things that
were written from documentation turned out like this: the workflow's token sees a
draft release (the run prints `draft: True`), and the bridge's self-test, when it is
allowed to try on a machine without SOLIDWORKS, exits **4** (not installed or not
registered) on `windows-latest` and on `windows-2022` alike. The workflow still
prints that exit code without asserting it.

**3. Then the final release.** When the run is green, the owner decides. The final
release is steps 2 and 3 of "The rite" (`prepare-release.ps1`, then the three commands), made
from the same commit. Delete the draft candidate from the Releases page: it was never
public and it is not the release.

## Known gaps

- `Installer.exe` is **not code-signed**. Windows SmartScreen will warn, and Windows 11
  with Smart App Control on may block it outright; the steps to get past SmartScreen
  are in QUICKSTART. Signing is a decision about buying a certificate, not a missing
  step.
- The Win32 file version of `Installer.exe` and of the bridge reads `0.0.0.0` in
  *Properties → Details*: the real version is an embedded resource read at run
  time. Someone checking "which version did I download" that way will not find
  it. `Installer.exe --verify --json` reports it (`bridgeVersion`).
- The installer has been exercised by `Checks/test-installer.js` and on the
  developer's machine. A clean machine **with SOLIDWORKS**, SOLIDWORKS in another
  language, a SOLIDWORKS version other than 2021 SP5.1 and Windows 11 with Smart App
  Control on have not been tried. A clean machine **without** SOLIDWORKS is what the
  smoke test above covers (the install refusing, and the mechanics against a placeholder).
