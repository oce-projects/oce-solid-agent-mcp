# Security policy

## Reporting a vulnerability

**Do not open a public issue.** Use GitHub's **private vulnerability reporting**
on this repository: the *Security* tab → *Report a vulnerability*.

If that button is not there, the feature has not been enabled and the report has
nowhere private to go — say so in a public issue **without the details**, and
wait for a channel.

## What is in scope

This bridge runs on the user's own machine, alongside their own SOLIDWORKS®
installation, and speaks MCP over stdio to a client on that same machine. There
is no server, no account, no network listener. What is in scope is therefore
narrower than usual, and worth naming:

- **Writing outside where it says it writes.** The bridge refuses paths under
  the system and program folders (Windows, System32, Program Files, Program Files
  (x86), ProgramData), under the folders listed in `RISCONTRO_SOLA_LETTURA`, and —
  when `RISCONTRO_RADICE` is set — anywhere outside that one folder. A way to get
  it to write where it refuses, or to escape the declared working folder, is a
  vulnerability.
- **The installer touching something it did not say it would.** It installs
  into the user's own profile, edits the MCP client configuration it names, and
  backs up what it edits. Anything else is a bug worth reporting privately.
- **A tool argument that reaches a shell, a file path, or SOLIDWORKS without
  being checked.** The operations take arguments from a language model, which
  is to say from text of uncertain origin.
- **An answer that says an operation succeeded when it did not.** This is the
  defect this whole project exists to chase. It is not a classical security
  issue, but a part that is silently different from the confirmed one is the
  most expensive thing here — report it the same way if it looks exploitable,
  and as a normal issue otherwise.

## What is out of scope

- Anything requiring the attacker to already have the user's Windows session.
  With that, they have SOLIDWORKS too.
- SmartScreen warnings on `Installer.exe`. It is not code-signed, this is
  documented in [QUICKSTART.md](QUICKSTART.md), and it is a known gap rather
  than a finding.
- SOLIDWORKS® itself, and Dassault Systèmes software generally. Report those to
  Dassault Systèmes; nothing here redistributes their code.

## What you can expect back

An acknowledgement, and an honest answer about whether and when it will be
fixed. Read the "No support commitment" section of
[CONTRIBUTING.md](CONTRIBUTING.md) before assuming more than that: this is
published under the Apache License 2.0, "as is", and there is one person behind
it.

## Supported versions

Only the latest. There is no backporting, there are no maintenance branches, and
`VERSION.txt` says which one is current. If you depend on this, pin a version
and read the [CHANGELOG](CHANGELOG.md) before moving off it.
