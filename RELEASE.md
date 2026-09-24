# Releasing

For the maintainer. If you want to **use** the bridge, this is not your page:
see [QUICKSTART.md](QUICKSTART.md).

## Why this is done by hand

No GitHub-hosted runner can build the bridge. `Oce.Solid.Agent.csproj` compiles
against the SOLIDWORKS® interop assemblies, which exist only on a machine with
SOLIDWORKS installed — on `windows-latest` the build stops at
`CS0246: IModelDoc2`. The CI compiles and runs the contract suite, which needs
no SOLIDWORKS, and says so; it does not produce an installer.

A self-hosted runner with a SOLIDWORKS licence would automate this. It is a
cost and a machine to maintain, and for now the choice is a manual rite on a
workstation that has SOLIDWORKS. The risk of a manual rite is always the same —
a step skipped on the day there is a hurry — so the steps that can be checked
are checked by a script.

## The rite

**1. The version is bumped and the bench is green.** `VERSION.txt` says the new
version, `CHANGELOG.md` has an entry for it, and if the library changed its
`AssemblyVersion` moved too. The bench ran on those bytes: `Banco\banco.ps1`,
about seventy minutes, exit 0. Everything is committed and pushed.

**2. Prepare.**

```
.\Controlli\prepara-release.ps1
```

It stops at the first thing that is not right, and publishes nothing. It
checks that:

- `VERSION.txt` is `X.Y.Z` and `CHANGELOG.md` has an entry for it;
- the working tree is clean and `HEAD` is `origin/main` — a binary must come
  from a commit that others can resolve;
- the tag does not exist yet;
- `build.ps1` succeeds and `dist\Installa.exe` exists;
- the SHA-256 in `dist\Installa.exe.sha256` is **recomputed** and matches — it
  is the value people will check against, so a stale one would tell everyone
  their download had been tampered with;
- the contract suite is green, which includes "the version the bridge announces
  is `VERSION.txt`" and "the last bench run is newer than the last change to the
  library and to the bench".

It writes the release notes for this version, taken from `CHANGELOG.md`, to
`dist\note-di-rilascio-X.Y.Z.md`, and prints the three commands for step 3.

**3. Publish — by a person.**

```
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin vX.Y.Z
gh release create vX.Y.Z dist\Installa.exe dist\Installa.exe.sha256 --title "vX.Y.Z" --notes-file dist\note-di-rilascio-X.Y.Z.md
```

The script does not run these on purpose: publishing is an action outside this
machine, and it is decided, not scripted.

## Known gaps

- `Installa.exe` is **not code-signed**. Windows SmartScreen will warn; the
  steps to get past it are in QUICKSTART. Signing is a decision about buying a
  certificate, not a missing step.
- The Win32 file version of `Installa.exe` and of the bridge reads `0.0.0.0` in
  *Properties → Details*: the real version is an embedded resource read at run
  time. Someone checking "which version did I download" that way will not find
  it.
