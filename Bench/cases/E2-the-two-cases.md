# E2 — The two cases that fail today

Two hand-written plans, runnable **with no language model at all**, with the
expected numbers next to every step. They exist to pin down today's behaviour
**before** repairing it: a red that has not been seen cannot be known to turn
green.

They are run like this, on an empty document:

```
Checks\CloseLeftovers\bin\CloseLeftovers.exe --close
Checks\NewPart\bin\NewPart.exe
node Bench\run_plans.js Bench\cases\bridge\e2_block.json
node Bench\run_plans.js Bench\cases\bridge\e2_case_a.json
```

---

## The starting block — `e2_block.json`

`extrude_rectangle` {front, 100 x 60 x 20, centered}

| | |
|---|---|
| bounding box | `[100, 60, 20]` |
| volume | **120,000** mm³ |

The six faces, with the signature that the state gives for them:

```
F1  6000  [0,0,20]   [0,0,1]
F2  6000  [0,0,0]    [0,0,-1]
F3  2000  [0,-30,0]  [0,-1,0]
F4  2000  [0,30,0]   [0,1,0]
F5  1200  [-50,0,0]  [-1,0,0]
F6  1200  [50,0,0]   [1,0,0]
```

---

## CASE A — the refusal · `e2_case_a.json`

**F6**, the right-hand face, is cited, and then the part is lengthened.

| step | expected |
|---|---|
| `set_dimension D1@Sketch1 100 → 160` | succeeds, volume **192,000** = 120,000 · 160/100 |
| `cylindrical_cut` Ø10 on **F6** | **succeeds, and SAYS how far the face has moved** |

**Up to 0.25.2 it was a REFUSAL**, and entry E2 had written it so (the bridge of
that day worded it in Italian; this is the same text in English):

```
«F6» now points to another part of the piece (moved by 60 mm). The piece has
changed since you confirmed: I do not work on the wrong face.
```

**From 0.26.0 (entry E6) the persistent reference finds the true face again**,
and the step succeeds, explaining what happened instead of stopping. Measured on
15/9/2026, bridge 0.40.0 — volume **191,607.3**, 392.7 mm³ removed:

```
the face «F6» was found again by SOLIDWORKS from its persistent reference: it
moved by 60 mm, from [50, 0, 0] to [110, 0, 0]. The part has changed since you
looked at it, but the face is the right one — if the plan counted on the earlier
measurements, check the result again. For what it is worth: «F6» was at 100% of
side X and is still there — the side went from 100 to 160 mm. It may be the same
face grown with the part, but I do not decide that: identity is measured in
millimeters.
```

The last sentence belongs to entry E10, and it is **only an explanation**: the
face had already been chosen by the persistent reference, and the judgment had
already been given by `Corroborates`. The fraction of the bounding box enters no
decision — and there is a test that reads the source to make sure of it.

> **60 mm, not 30.** The TODO entry expected 30, supposing that the part grew
> **symmetrically** around its centre. It does not: it grows on one side only,
> so the right-hand face moves by the whole 60 mm of the growth. It is a
> difference that shows only when measuring, and it changes the number written
> in the test case.

**This case is the RIGHT behaviour**, and it is here for two reasons: so that the
defence stays visible when E3 changes the finding again, and so that the number
in the message does not change by mistake.

---

## CASE B — the silent defect · `e2_pocket.json` then `e2_case_b.json`

**This is the case that counts.**

### The preparation — `e2_pocket.json`

`rectangular_cut` on the top face (F1), 40 x 40 pocket centred in Y,
depth 8.

| | expected |
|---|---|
| volume | **107,200** = 120,000 − 40·40·8 |
| remaining top face | **4400** mm² at `[0,0,20]` |
| bottom of the pocket | **1600** mm² at `[0,0,12]` |

> The pocket has to be centred in Y. With the width moved to `y_mm: 20` it comes
> out of the edge — the face goes from −30 to +30 — and it is cut at 40 x 30 =
> 1200 mm². Measured: volume 110,400 instead of 107,200, and the numbers of the
> case no longer agree with the ones written here.

### The plan — `e2_case_b.json`

| step | expected |
|---|---|
| `set_dimension D1@Boss-Extrude1 20 → 12` | succeeds, volume **59,200** |
| `cylindrical_cut` Ø10 depth 4 on **F5** (the bottom of the pocket) | «succeeded», **and the part is wrong** |

### What happens, and why nobody notices

After the change the part is 12 thick: the bottom of the pocket drops to **Z=4**,
and the top face drops to **Z=12** — that is, **exactly where the plan expects the
bottom**.

Reading the state again after the change:

```
F2  4400  [0,0,12]     <- the TOP face, where the plan looks for the bottom
F3  1600  [0,0,4]      <- the bottom of the pocket, where the plan does not look
```

The signature that the plan carries with it says: point `[0,0,12]`, area **1600**.
The finding step finds a single face that matches on the **point** — and that one
has area **4400**, that is, almost three times as much.

```
[agent] the face «F5» had moved: found again from its signature
[agent] created feature Cut-Extrude2
succeeded: true
```

**The point won over the area.** It is the shortest sentence that describes the
defect.

### The two parts, compared

| | volume | the hole |
|---|---|---|
| **right** (hole on the bottom of the pocket) | **58,885.8** | cylinder **125.66** mm² = π·10·4, at `[5,0,0]` |
| **wrong** (what comes out today) | **59,042.9** | cylinder **62.83** mm² = π·10·2, at `[-5,0,8]` |

**157.1 mm³ of difference, 0.27%.** The wrong hole straddles the edge of the
pocket: half falls in the void and half in the material, and what comes out is
half a hole 2 deep instead of a whole hole 4 deep.

The right part is obtained by reading the state again after the change and
citing the bottom with its NEW identifier (`e2_case_b_correct.json`, which cites
F3).

> Clean rebuild, no error, no warning, «succeeded: true». The defect is not «a
> legitimate job is refused»: it is **«it works on the wrong face saying it went
> well»**.

---

## What must change (entry E3)

Finding by a single signature is not enough, and case B says why: **a
corroboration**. The point matches and the area does not — and a face whose area
has changed by 175% is not the same face that was looked at.

The way is not to refuse more: it is to **ask for a second confirmation before
saying «found again»**, and when the two do not agree, to say so instead of
choosing.
