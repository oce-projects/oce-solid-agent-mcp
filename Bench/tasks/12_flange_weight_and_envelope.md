# 12 — Flange weight and envelope

**Family:** measures (and properties, for the material)

## The task

> Put S235JR steel on the bolt-circle flange (task 02), and tell me how much it weighs and how big it is.

## Why this task

It is the most common question at the bench for anyone who has to ship a part or work out a freight quote, and in the bridge it is two different operations (one for weight, one for envelope) plus a third, upstream, without which the first says nothing: with no material assigned, a part has no weight, only volume.

## What it deliberately doesn't say

It doesn't give the material's density: that's a property of SOLIDWORKS's own material table, not a number the shop picks. Asking for it would measure the ability to read a table, not the ability to assign the right material.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| measurements.mass_kg | 1.73 | ± 0.02 | the flange's volume (221,614.23 mm³, from task 02) times S235JR's density, taken as 7,800 kg/m³ (a common value for structural steel): 221,614.23 × 7.8×10⁻⁶ = 1.729 kg. The tolerance covers this installation's real material table, which might use a slightly different value (e.g. 7,850 kg/m³): it is not an error if the read-back number falls in that range |
| bounding_box_mm | [160, 160, 12] in some order | ± 0.01 | the flange's diameter twice (a circle's bounding box is a square with side equal to the diameter, which the material doesn't change) and the disc's thickness; the order of the three depends on the plane the disc was drawn on |

## What this measurement does not see

Whether the assigned material is really "S235JR" by exact name, or a different material whose density happens to give the same weight: a generic steel at 7,800 kg/m³ would pass this measurement identically. Telling them apart needs reading the material name written into the part's properties, not the weight.
