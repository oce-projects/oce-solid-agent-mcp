# 07 — Tube stand with feet

**Family:** weldments

## The task

> Make me a rectangular frame out of 40x40x3 square tube: 1000 by 600, with a crossbar at mid-length, all corners mitred. Support it on four feet of the same tube, 150 tall, one under each corner, capped at the bottom. Reinforce each foot with a triangular gusset, 40x40, 3 thick, between the foot and the frame. Put the cut list on it.

## Why this task

It brings together, in one plan, almost the whole weldments family: mitred bars in a closed frame, an internal bar butt-cut against the others (not mitred: it's a T-joint, not a corner), legs added afterwards, caps on the only ends that are **truly** free (the bottom of the feet — not the uprights' ends, closed by the mitre, nor the crossbar's, closed by the cut against the long sides), and a gusset at every joint. It is the longest task in the folder on purpose: if one step is skipped, the final count says so.

## What it deliberately doesn't say

It doesn't say how to group the calls (the frame as one piece or bar by bar, the four feet together or one at a time): only the result counts. It also doesn't say that the uprights' ends must **not** be capped: whoever works that out alone (they're closed by the mitre, not free) has understood the difference between an open end and a welded one, which is exactly the point of the task.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| bodies | 17 | — | 4 frame bars (two of 1000, two of 600) + 1 crossbar + 4 feet + 4 gussets + 4 caps = 17. A different number says which group was skipped: 13 means the caps are missing, 9 means both gussets and caps are missing |
| measurements.cut_list_rows | 6 | — | bars group by identical shape, not by name: two 1000 mitred sides (1 row, qty 2); two 600 mitred sides (1 row, qty 2); the crossbar, 600 but with square-cut ends instead of a mitre — a different shape, so its own row (qty 1); four identical feet (1 row, qty 4); four identical gussets (1 row, qty 4); four identical caps (1 row, qty 4). Six rows in total |
| measurements.bodies_in_cut_list | 17 | — | the sum of every row's quantity (2+2+1+4+4+4) must equal the body count: if it doesn't, some row has grouped bodies that aren't really identical, or lost some |
| measurements.already_existed | false | — | the cut list is born in this plan, as in task 06 |

## What this measurement does not see

The part's volume: with four mitres, four T-joints and four triangular gussets, hand arithmetic is no longer as reliable as for a simple part, and a small error in a gusset's angle would not change the body count or the cut-list rows. It also doesn't see whether the four feet are really vertical and not leaning: same length, same count, a crooked stand.
