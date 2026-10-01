# 08 — Box with lid

**Family:** assemblies (and part, to build them)

## The task

> Make me a little box in two parts: a square base 100x100, 20 tall, with a 90x90 pocket 15 deep (5 of solid rim all around); and a flat lid 100x100, 3 thick, that sits on top of it. Then assemble them: fix the base and sit the lid on top of it, mated.

## Why this task

It tests the difference between **inserting** a component and **mating** it: the catalogue itself says so plainly ("Inserting is NOT mating"). A lid that only rests there, unmated, slides off on the first save that moves something: the task explicitly asks for the mate, and the measurement checks that it's there.

## What it deliberately doesn't say

It doesn't say which mate type to use (coincident, distance...): "sits on top" is enough for anyone who knows the catalogue to land on a coincident mate between the right faces. It doesn't say which face to fix the base by either: it's the only fixed component, so there is no wrong choice left.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| components | 2 | — | base and lid, not a single part and not a third component never asked for (a screw, a stiffener) |
| volume_mm3 (base) | 78,500 | ± 50 | 100×100×20 block = 200,000 minus the 90×90×15 pocket = 121,500. 200,000 − 121,500 = 78,500 |
| volume_mm3 (lid) | 30,000 | ± 20 | flat 100×100×3 plate |
| measurements.how_many (of the `interference_check` step) | 0 | — | the lid rests, it doesn't wedge in: zero interferences is the right result when two faces touch without overlapping, not a defect to fix |
| bounding_box_mm (the height, the axis the lid sits along) | 23 | ± 0.1 | the assembly's height is the sum of the two: 20 from the base plus 3 from the lid, whichever mate was chosen to hold them together |

## What this measurement does not see

Whether the base is really fixed and the lid isn't: an assembly with both components fixed would pass the same four checks above (volumes, interferences and height don't depend on which one is fixed), but it would be the wrong assembly — a fixed lid no longer follows the base if someone moves it in the drawing. Seeing that needs reading each component's "fixed" state, not covered by this measurement yet.
