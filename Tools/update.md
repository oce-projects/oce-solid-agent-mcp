# Regenerating `catalog.json`

The catalogue lives in `Oce.Solid.Agent.Core/Catalog/Operations.cs`, **next to
the code that runs the operations**. `catalog.json` is a product: it is
regenerated, never edited by hand.

## The command

```bash
.\bin\Oce.Solid.Agent.Catalog.exe Tools\catalog.json
```

Nothing else. No server is needed, no docker, no network.

To check without writing — useful before a commit, and in an automatic
build:

```bash
.\bin\Oce.Solid.Agent.Catalog.exe --controlla Tools\catalog.json
```

(`--controlla` is Italian for «check»; the option has kept that name.)

It exits 0 if the file matches, 1 if not. And when it does not match it says
**where**: the position of the first different character and the thirty letters
around it, on both sides. An «they are different» on a file of seventy-two
thousand characters would send you looking by eye.

## Why it is in C# and not in the server

The catalogue used to live in the server, in `ai_disegno.py`, and this file was a
copy of it. Regenerating it meant: packing the server's sources, sending them
over with `scp`, running them inside a throw-away container, and bringing the
result back. Three steps, two machines, and docker.

**Whoever cloned this repository could not regenerate the catalogue.** A file
that cannot be regenerated is a file that nobody dares to touch — and that file
is the interface to the model.

There is a second reason, less practical and more important: **the layer that
DESCRIBES an operation must be the same one that RUNS it.** It is the only
arrangement in which an operation that is described and cannot be run cannot
enter the catalogue without somebody noticing — the check becomes an assertion
inside a project, instead of a comparison between two repositories that nobody
redoes.

## What ends up in the file

**Only the operations with `Tested = true`.** An operation that has not been
built inside SOLIDWORKS and measured is offered to nobody — not even from here,
and especially from here, because a bridge exists to measure, and measuring with
an unmeasured instrument is not measuring.

The ones that stay out are **named**, on stderr, one per line: a list that
shrinks in silence is not noticed.

For every operation: `name`, `family`, `summary`, `details`, `schema`,
`precondition`. `Tested` is not exported — it is ours, not the model's.

The families that do not have even one tested operation stay out too, and those
are named as well. A declared and empty family reaches the model as a door that
opens onto nothing.

## The shape of the file

Indentation of **one** space, accents written out and not as `\u` escapes, line
feed only as line end, no final line end, and the keys in the order in which they
are written in C#.

It looks pedantic and it is not. When the catalogue moved from Python to C#, the
criterion was that the file that came out had to be **identical byte for byte**
to the one that was there: 72,452 bytes, and it was. A softer criterion —
«equivalent once read back» — would have let a shortened description through,
and the descriptions are the real interface to the model, much more than the
schemas.

From here on the same shape serves another purpose: keeping the `diff` of
`catalog.json` readable when a single line changes.

## How to notice that it is old

The bridge writes at start-up, on stderr:

```
[bridge] catalog: 60 operations tested
```

The number above is the one of 16 September 2026 and is only an **example of how
to read the line**: what counts is comparing it with how many the `catalog.json`
in use declares, not with this one. *A number written inside a document ages by
itself* — it was `36` until today, when the operations were already sixty, and
nobody could notice by reading.

And `build.ps1` regenerates the file with every package, so it cannot fall behind
unless somebody deletes that step.

## The server side, which is still to be done

`ai_disegno.py` is still the source of the catalogue **for the server**: until it
reads this JSON, there are two sources and they will diverge.

Established with a grep from the server's side: `CATALOGO_DISEGNO` has **three**
consumers besides the one that defines it, and they use `.nome`, `.provata` and
`.precondizione`, all of them fields that the C# record has. *(Paths and lines
used to be written here in full: they belong to another repository, which is
private, and its structure is not something this one should recount.)*

But `ai_disegno.py` also keeps `limiti` and `riassunto`, which are **not** among
the six exported fields and are used by `valida()` and to build the plan to
confirm. So the inversion leaves part of the record in Python: it is not a
replacement, it is a split. It is the work that entry C3 had already declared
outside its own estimate.
