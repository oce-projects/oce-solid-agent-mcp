# 10 — Bracket assembly drawing

**Family:** drawing

## The task

> Draw up the bracket-on-plate assembly (task 09): the three standard views, the bill of materials, and item-number balloons on the parts, on one A3 sheet.

## Why this task

It is the first drawing task in the folder, and it deliberately uses an assembly with a mirrored component: if the right and left brackets are the same part (an instance), the BOM puts them in ONE row with quantity two; if they are two independent files, the BOM puts them in TWO rows. The catalogue itself compares the drawing's BOM against the assembly's own components: this checks that the total comes out right either way, without demanding which of the two the model chose back in task 09.

## What it deliberately doesn't say

It doesn't say how many rows the BOM should have: that depends on a choice made in an earlier task, which is not forced or guessed here. It does say what stays the same regardless of that choice: the total part count and the number of balloons.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| measurements.views_created | 3 | — | the three standard views asked for, not a single view and not an exploded view never requested |
| measurements.sheets | 1 | — | a single A3 sheet |
| measurements.quantity_in_table (of the `drawing_bom` step) | 3 | — | two brackets plus one plate, whatever the row count: one row of quantity 2 plus one of quantity 1, or three rows of quantity 1 each, both sum to 3 |
| measurements.balloons_placed (of the `balloons` step) | 3 | — | one balloon per part physically shown in the view, not one per BOM row: even if the two brackets share a row, on the view they are two distinct parts and each gets its own number |

## What this measurement does not see

How many rows the BOM really has (two or three): that depends on the choice made back in task 09 between an instance and an independent file, and it is the one correct reading this deliberately does not pin down. It also doesn't see whether the model's dimensions were pulled into the drawing: the task never asked for them, and the measurement doesn't expect them.
