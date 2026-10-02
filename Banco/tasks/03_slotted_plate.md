# 03 — Slotted plate

**Family:** part (extrusions/features)

## The task

> Make me a rectangular aluminium plate, 150 x 80, 10 thick. One slot in the middle, 60 centre-to-centre of the two round ends and 12 wide, through. Chamfer the four vertical edges at 3x45.

## Why this task

It tests two features the catalogue **deliberately** keeps apart from a round hole: a slot (an elongated hole) and a chamfer applied to a group of vertical edges, not a single one. It is also the task where the part's glossary matters for real: a slot is not a hole, even though it is a through cut that removes material the same way a hole does.

## What it deliberately doesn't say

It says "centre-to-centre of the two round ends", not just "60 long": the same lesson as task 01 (measurements "in total" or "centre-to-centre" must be stated, not guessed) applies here too, and here the difference between the two readings is about 720 mm² of area.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| volume_mm3 | 111,489.03 | ± 0.1 | plate 150×80×10 = 120,000; minus the slot (a 60×12 rectangle plus the two radius-6 half-circles, i.e. 60×12 + π×6² = 833.0973 of area, ×10 thick = 8,330.97); minus four 3×3 chamfers (each 0.5×3×3×10 = 45, 180 for four). 120,000 − 8,330.97 − 180 = 111,489.03 |
| bodies | 1 | — | a machined plate stays one part |
| diameters_mm | [12, 12] | ± 0.02 | the only curved faces are the two round ends of the slot, half-cylinders of radius 6: a round hole (none was asked for) would add a diameter, and a slot built from straight cuts would give none. The slot is counted by its own shape, not as holes |

## What this measurement does not see

Whether the chamfer is truly a 45-degree cut and not, say, a rounded fillet put in its place: a chamfer and a fillet remove slightly different volumes (here the difference would be small, under the 0.1 mm³ tolerance), and a fillet mistaken for a chamfer would pass this measurement unnoticed.
