# Contributing

Thanks for looking. Please read this page before opening anything — it is short,
and two of the rules will surprise you.

## Issues: yes. Pull requests: not yet.

**Issues are open and welcome.** Bug reports, measurements that contradict this
project's own claims, API behavior you have observed on a SOLIDWORKS® version
we have not tested — all of that is valuable, and an issue costs you nothing to
file.

**Pull requests will not be merged for now.** There is no Contributor License
Agreement in place yet, and until there is, merging outside code would give this
project a licensing situation it cannot undo later. This is not a judgment on
your patch.

If you have written a fix, the most useful thing you can do is open an issue
that **describes** it: what was wrong, how you measured that it was wrong, and
what you changed. That is portable, it survives a rewrite, and it does not
create a licensing problem for either of us.

This rule is written here **before** the first pull request arrives on purpose.
A policy written afterwards is not a policy — it is a personal answer to one
person, and it reads like one.

## No support commitment

This software is published under the Apache License 2.0, which says it is
provided "as is", without warranties or conditions of any kind. That is the
legal statement. The practical one:

**There is no commitment to answer issues, to fix reported defects, or to keep
any interface stable between versions.** Issues may sit unread. A tool name may
change. If you depend on this in production, pin a version and read the
CHANGELOG before moving off it.

## What a good issue looks like here

This project's whole premise is that **a part can be silently different from the
one that was confirmed** — a defect that raises no error. So the single most
useful report is not "it crashed"; it is:

> I asked for X. The bridge answered "done". The part is actually Y.
> Here is how I measured Y.

Include, where you can:

- the version (`VERSION.txt`, or the `version` the bridge reports in `serverInfo` when a client connects, or `bridgeVersion` in `Installer.exe --verify --json`);
- your SOLIDWORKS® software version and service pack, and its interface language
  — several defects found here were localization-dependent;
- the operation and its arguments;
- **what you measured on the part**, not what the API returned. A value returned
  by the thing that did the work is not a measurement of the work.

A report without the last point is still welcome. It is just harder to act on.

## Declared is not measured

The same rule applies when this project is compared with another one — in an
issue, in a benchmark, in a README table. Mark every claim:

| mark | meaning |
|---|---|
| **(D)** | **declared**: their README, their documentation or their code says so. Nobody here ran it. |
| **(M)** | **measured**: a number came out of a run, on a named machine, with a date. |

**Comparing their (D) against our (M) and calling it an advantage is not
allowed.** "They claim it, we measured it" does not mean ours works better; it
means we know more about ours. The reverse holds just as hard, and it happens
more often than it sounds: **a bad (M) loses to a good (D) every time** — which
is exactly why the temptation is to not measure.

## Security

Do not open a public issue for a security problem. Use GitHub's private
vulnerability reporting on this repository instead — [SECURITY.md](SECURITY.md)
says where the button is, what is in scope, and what to expect back.

## Trademark

SOLIDWORKS® is a registered trademark of Dassault Systèmes SolidWorks
Corporation. This project is independent and is not affiliated with, sponsored
by, or endorsed by Dassault Systèmes. It contains and redistributes no Dassault
Systèmes code or material; the interop libraries are loaded at runtime from your
own SOLIDWORKS® software installation, which requires a valid license of your
own.

Please keep that true in anything you contribute: no Dassault Systèmes source,
binaries, redistributable components, logos, fonts or official colors in
issues, screenshots or attachments.

## Third-party code

Do not paste code from other projects into an issue or a patch, even a small
amount, and even when the license looks permissive. If another project solved
something, **name it and describe the idea** — that is what we can use.
