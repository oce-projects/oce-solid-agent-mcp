# 05 — Hemmed cover

**Family:** sheet metal

## The task

> Make me a rectangular cover in 1.5 mm sheet metal, 180 x 100. On the long side (180), fold me a closed hem of 10, so it doesn't cut your hands when you pick it up.

## Why this task

A hem (the edge folded back onto itself) is not just any bend: in the catalogue it is a separate operation from the one that makes a straight flange. This task checks that the model doesn't confuse the two — a closed hem doubles the thickness along a strip, a normal bend does not — and that it doesn't count one as the other in the final report.

## What it deliberately doesn't say

It doesn't give the hem's bend radius: a "closed" hem is crushed, close to zero radius by definition, and stating one would add no information. It says "closed" because the alternative (open, with a gap) would change the count below, and here the solid, gap-free edge is what's wanted.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| volume_mm3 | 32,400 | ± 400 | flat panel 180×100×1.5 = 27,000; the closed hem folds a 10 mm strip back on itself along the whole 180 side: at near-zero radius the added material is roughly a 10 mm round trip, i.e. 2×10×180×1.5 = 5,400. Total 32,400. The tolerance is deliberately wide: a real "closed" hem has a small but non-zero radius, and that bend consumes a sliver of material not counted here by hand |
| bodies | 1 | — | the cover stays one part |
| measurements.kind (of the `hem` step) | closed | — | one hem, and the closed one the task asks for: the hem answers with its own kind (closed, open, teardrop) |
| measurements.bends (of the `base_flange` step) | 0 | — | a dedicated entry: the hem is not a "bend" in the sense of the straight flanges from task 04, and the cover starts flat. A 1 here would mean a flange was built where a hem was asked |
| bounding_box_mm (the side the hem does not touch) | 100 | ± 0.01 | the 100 side is untouched by the hem (which runs along the 180 side): it stays the flat-pattern value, no flat-pattern math needed |

## What this measurement does not see

Whether the hem is truly "closed" (crushed) or SOLIDWORKS made it "open" with a small gap: for the same folded length the volume difference between the two is small, inside the tolerance above, and not enough to tell them apart. Seeing that needs reading the operation's type, not the volume.
