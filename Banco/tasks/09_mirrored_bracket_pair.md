# 09 — Left and right bracket pair

**Family:** assemblies

## The task

> Take the L-bracket (the 8 mm steel one, long leg 120x50, short leg 60 total, two holes of 9 — see task 01) and make me the left and right pair out of it: mirrored, not rebuilt from scratch. Mount one on each side of a 300x120 base plate, 10 thick, at the two short edges, with the long leg resting on the plate.

## Why this task

The catalogue names this exact situation as the example for its mirror-component operation: "the right bracket and the left one". This task checks whether the model actually uses it instead of redrawing the mirrored bracket from zero — which would cost twice the work and, on every future change to the original bracket, would leave the other one behind.

## What it deliberately doesn't say

It doesn't say which plane to mirror on, nor whether the mirrored bracket must be a new file or an instance of the same one: those are the model's choices, and the catalogue itself leaves three different ways to do it.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| components | 3 | — | plate, right bracket, left bracket |
| volume_mm3 (each bracket) | 67,782 | ± 200 | the same number, for the same reason, as task 01: mirroring doesn't change the volume |
| volume_mm3 (plate) | 360,000 | ± 50 | 300×120×10 |
| measurements.new_component (of the `mirror_component` step) | present | — | at least one component must have been born from a mirror operation, not a second independent modelling pass: that's the point of the task. The mirror answers with the name of the component it made |
| measurements.how_many (of the `interference_check` step) | 0 | — | the brackets rest on the plate, they don't wedge into it |

## What this measurement does not see

Whether the mirrored bracket is an **instance** (the same file, following every change to the first) or an **independent** file: the catalogue itself flags this as an open choice, and volume and component count are identical either way. Seeing it needs opening the file and checking whether it's really a second document or a reference to the same one.
