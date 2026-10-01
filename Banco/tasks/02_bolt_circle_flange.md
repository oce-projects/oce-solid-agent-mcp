# 02 — Bolt-circle flange

**Family:** part (extrusions/features)

## The task

> Make me a round flange, 160 diameter, 12 thick, with a 40 through bore in the middle for the shaft. Put six through holes of 9 for M8 screws on it, on a 130-diameter circle, evenly spaced.

## Why this task

It tests a choice that has no single name in the catalogue: **repeating a feature in a circle** (one hole, then six around an axis) instead of drawing each one by hand. It also tests the difference between a through hole **for a shaft** and holes **for screws**: the same part holds two different families of holes with different roles, and the measurement keeps them apart on purpose.

## What it deliberately doesn't say

It doesn't say how to get the even spacing (a purpose-built axis, or around the central hole's own circular edge): that choice is what is measured, not obedience to one method.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| volume_mm3 | 221,614.23 | ± 2 | disc π×80²×12 = 241,274.32; minus the centre bore π×20²×12 = 15,079.64; minus six holes of π×4.5²×12 = 763.41 each (4,580.47 total). 241,274.32 − 15,079.64 − 4,580.47 = 221,614.21 (the one-cent difference is cascading π rounding). The tolerance covers only that, not a seventh hole forgotten or double-counted |
| bodies | 1 | — | a bored disc is ONE part |
| measurements.holes | 7 | — | the centre bore plus the six bolt-circle holes: all seven are literally "holes", and the total count takes all of them |
| measurements.holes_on_circle | 6 | — | a separate entry on purpose: the centre bore (for the shaft) is not one of the six bolt-circle holes (for the screws). Two different vocabularies in the same part, and the count must not blur them |

## What this measurement does not see

Whether the six holes are truly evenly spaced (60° apart) and on the right circle (65 radius). Volume and count are identical even with a wrong layout, as long as the diameters are right: seeing the position needs reading the centre coordinates, not in the harness yet.
