# 04 — Sheet-metal C-bracket

**Family:** sheet metal

## The task

> Make me a C-bracket in 2 mm sheet metal: the web 150 long and 60 wide, with two flanges rising 28 from the web's two long edges, 2 bend radius.

## Why this task

It is the first sheet-metal task in the folder, and it tests the choice between **two operations that look like the same thing**: a base already bent into a C in one go (an open U profile) or a flat web with two flanges added afterwards on its two edges. The finished part is the same; the plan that gets there is not.

## What it deliberately doesn't say

It does not leave the bend radius to chance: it states 2 mm, because without the radius the flat-pattern length cannot be computed and the measurement below would have no number to check against. It doesn't say which plane to start from, nor whether the flanges come up "in one piece" or "one at a time": those are the model's choices.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| volume_mm3 | 35,227 | ± 150 | sheet-metal volume is conserved from flat to bent: it is the flat-pattern area times thickness. Flat width = web width (60) + two times [(28 − 2 − 2) + π/2×(2 + 0.5×2)] = 60 + 2×28.7124 = 117.4248; times length (150) and thickness (2) gives 35,227.43. The bracketed formula is the same one used and probe-measured on the real case `Bench/cases/sheet_metal/edge_flange_closes_the_side.json`, with the same radius and thickness: reused here, not invented. The tolerance is wide because it assumes a K-factor of 0.5 (neutral fibre at mid-thickness), the material table's default: if this installation's 2 mm sheet-metal table uses a different one, the real number shifts by a few tens of mm³ |
| bodies | 1 | — | the bracket is one part |
| measurements.bends | 2 | — | two bends, one per flange. Not three (an extra flange) and not one (both flanges made as a single bend, which would give a different shape) |
| bounding_box_mm (the length of the bracket) | 150 | ± 0.01 | no bend touches the bracket's length: the bends run parallel to this dimension, so it stays the flat value, no flat-pattern math needed |

## What this measurement does not see

Which way the flanges rise (up, forming a C that holds things, or down, forming a hat that covers them). Volume, bend count and length are identical in both cases, and the wrong bracket doesn't know it is wrong until it reaches the shop floor.
