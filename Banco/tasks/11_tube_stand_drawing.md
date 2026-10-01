# 11 — Tube stand drawing with cut list

**Family:** drawing

## The task

> Draw up the tube stand with feet (task 07): the standard views, the cut list on the sheet, and balloons on the parts.

## Why this task

The cut-list table on a drawing doesn't just copy the part's own list: the catalogue says it **compares** it against the welded assembly's. This task checks that the comparison comes back green — that the six rows already counted in the part (task 07) arrive on the drawing without being lost or doubled — not the count starting from zero.

## What it deliberately doesn't say

It doesn't say how many columns the table should have, nor the sheet format: the catalogue has a default for both, and a task that spelled them out would only measure the ability to copy a default value.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| measurements.views_created | 3 | — | the three standard views |
| measurements.sheets | 1 | — | a single sheet |
| measurements.cut_list_rows | 6 | — | the same six rows already counted as correct in task 07 (two 1000 sides, two 600 sides, crossbar, feet, gussets, caps): the drawing must have neither more nor fewer |
| measurements.cut_list_matches | true | — | the comparison between the drawing's table and the part's own cut list, which the operation itself says it performs, must come back positive: if the drawing shows six rows different from the part's, the comparison would flag red even with the right row count by sheer coincidence |

## What this measurement does not see

Whether the balloon numbers on the drawing really correspond, part by part, to the cut list's own item numbers (and not, say, to a different order): a correct balloon count doesn't say whether the balloon on a foot actually carries the feet row's number. Seeing that needs reading the number-by-number match, not covered by this measurement yet.
