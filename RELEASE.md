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
machine, and it is decided, not scripted. While the repository is private, the
Release is private too.

**4. Check what was published.** Download `Installer.exe` and `Installer.exe.sha256`
from the Release (not from `dist\`), compute the hash as QUICKSTART teaches and
compare, then run `Installer.exe --verify` once on a machine other than the one that
built it: it must exit 0, or 2 with warnings you can name
([INSTALL-FOR-AI.md](INSTALL-FOR-AI.md), step 4).

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
  developer's machine. A clean machine without development tools, SOLIDWORKS in
  another language, a SOLIDWORKS version other than 2021 SP5.1 and Windows 11 with
  Smart App Control on have not been tried.
