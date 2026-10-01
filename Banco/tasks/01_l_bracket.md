# 01 — L-bracket

**Family:** part (extrusions/features)

## The task

> Make me an L-bracket out of 8 mm steel: long leg 120 long, 50 wide; the short leg comes up 60 in total, measured from the bottom. On the long leg, two through holes of 9 for M8 screws, 20 from the edge and 80 apart from each other.

## Why this task

It is the most common part to come out of this shop, and it tests two choices the model must make on its own: **how to build an L** (two extrusions, a bent profile, an extruded profile — all valid roads) and **which face** the holes go on, which means picking one out of several that exist.

## What it deliberately doesn't say

It names no operation and doesn't say which plane to start from. If it did, obedience would be measured instead: picking the operations **is** what this measures.

**Why it gives the total height.** A first draft said only "short leg 60", and could be read two ways: 60 of the leg's own height (total height 68) or 60 total (own height 52) — a 3,200 mm³ difference, and both readings are correct. The ambiguity is removed from the task, not hidden inside a wider tolerance.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| volume_mm3 | 67,782 | ± 200 | 120×50×8 = 48,000 (long leg) + 52×50×8 = 20,800 (short leg, 60 total height minus the 8 of thickness it sits on) − 2 through holes (2 × π × 4.5² × 8 = 1,018) = 67,782. The tolerance covers rounding and a minimal fillet, not a different reading of the task |
| bodies | 1 | — | a bracket is ONE part: two bodies means the legs never joined, a defect that doesn't show on screen |
| measurements.holes | 2 | — | two, not one and not four |

## What this measurement does not see

Which leg the holes are on. Volume and count are identical whether they end up on the short leg, and the bracket would arrive in the shop unusable. Seeing that needs comparing the hole centres against the bounding box: added once the harness exists, not promised now.
