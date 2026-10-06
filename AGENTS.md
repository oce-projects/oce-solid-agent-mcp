# For AI agents

This file is for an AI agent that has been pointed at this repository. Find the
sentence that matches your job.

## You were asked to install the bridge for someone

Read **[INSTALL-FOR-AI.md](INSTALL-FOR-AI.md)** and follow it step by step. It is a
procedure you can check: every step gives the command, the exit code and output to
expect, and what to do if you do not get them. Do not improvise around it, and in
particular:

- read the **exit code**, not the sentence the installer prints;
- never replace a client configuration file that the installer says is not valid JSON;
- never fetch `Installer.exe` from anywhere other than the maintainer or a Release of
  this repository, and say where the one you used came from;
- "installed" means `Installer.exe --verify` exits 0 (or 2 with warnings you can
  explain), the user has restarted their MCP client, and a call of the `inspect_part`
  tool answers. Anything less is not done.

A human who wants to do the same thing by hand should be sent to
[QUICKSTART.md](QUICKSTART.md).

## You were asked to change this repository

Read [README.md](README.md) (what the bridge is, how to build it) and
[CONTRIBUTING.md](CONTRIBUTING.md) (what the project accepts and what it does not:
outside pull requests are not merged for now). Then:

- **Write everything in English**: code, identifiers, comments, messages, documents.
  `node Checks/language/detect-italian.js` is the language gate; the `pre-push` hook
  and the CI run it, and a commit may not make the Italian in the repository grow.
- **Every code change bumps `VERSION.txt` and adds an entry to `CHANGELOG.md`.** It is a
  rule of the project, written at the top of the changelog.
- **The check that needs no SOLIDWORKS** is the contract suite:
  `msbuild Tests\Oce.Solid.Agent.ContractTests.csproj -p:Configuration=Release -v:m`, then
  `Tests\bin\Oce.Solid.Agent.ContractTests.exe`. It exits 0 when it is green. It is what
  the CI runs.
- **Building the bridge, the probes in `Checks\` and the bench needs SOLIDWORKS** on the
  machine, and the bench and the probes drive the SOLIDWORKS session that is open. Do not
  run them unless the person asked you to. When you start the bridge itself and it must
  not touch SOLIDWORKS, set `OCE_NO_ATTACH=1`: it then answers the protocol and lists its
  tools without attaching.
- **A number in a document is checked.** The README must say how many operations there are
  (it is read by a contract check) and must name every environment variable the bridge
  reads. Change the count where it is declared, not the check.
- **Do not push with `--no-verify`** to silence a failing check. The hook exists so that
  nothing leaves the machine that must not (a local path, a user name, a private address):
  fix the cause, or widen the exception in the check and write down why.

## Where things are

| | |
|---|---|
| `Program.cs`, `Bridge/`, `Mcp/` | the bridge: the executable, the SOLIDWORKS session, the MCP protocol |
| `Tools/` | the tool definitions (`McpTools.cs`), the catalog reader, `catalog.json` (exported, not written by hand) |
| `Oce.Solid.Agent.Core/` | the library of operations, and the catalog they are exported from |
| `Oce.Solid.Agent.Catalog/` | the program that exports `Tools/catalog.json` |
| `Installer/` | `Installer.exe`: window, silent mode, `--verify`, uninstall |
| `Tests/` | the contract suite |
| `Bench/` | the bench: cases in `Bench/cases/`, the last report in `Bench/report/last.json` |
| `Checks/` | probes, scripts of the release and of the language gate, the git hooks |
| `build.ps1` | builds everything into `dist\Installer.exe` |
