# Advice from an experienced designer

<!-- Part of skill solidworks-bridge 0.1.0. Rules 1-7 of "Mates" and "Organizing the tree"
come from an assembly corrected by hand by an experienced designer (28/10/2026);
the rest is common good practice. -->

The aim is **design intent**: a model that stays right when a size changes, and that another
designer understands by reading the tree. A model that only looks right today is not enough.

## Mates

1. **Contact first.** Faces that really touch get a `coincident` mate. Do not use a distance
   of 0, and do not copy a value you measured.
2. **Center with `width`.** To put a part in the middle of another (a clip on a channel, a sleeve
   on a bar), use a width mate in each cross direction, not two distances: with a width mate the
   symmetry survives when a size changes. Bridge 0.152.10 cannot make a width mate (`mate`
   `variant` is coincident, parallel, perpendicular, distance, concentric or tangent): ask the
   user to add it, or explain the limit. Do not replace it with two distances without saying so.
3. **A number only where there is a real choice.** A `distance` mate is right for a position
   that is a design decision (where a clip sits along the channel). One distance for every
   degree of freedom gives a fragile assembly with no intent.
4. **Butt end parts against what receives them.** A coupler or cap at the end of a channel is
   placed by a coincident mate against the end face, not by a coordinate.
5. **Equal parts repeated: use a pattern** of the component (a local pattern in SOLIDWORKS), not
   a second copy with its own mates. Note: bridge 0.152.10 has no component-pattern operation and
   reports patterned instances as "free". Ask the user to make the pattern, or explain the limit.
6. **The first component is fixed** and aligned with the assembly origin and planes. Aim for a
   fully defined assembly ("Fully defined" in the SOLIDWORKS status bar).
7. **Check what you get.** After the mates, `interference_check`. An intended press fit (for
   example a clip squeezing a channel by 0.05 mm) is fine only if the user confirms it.

## Organizing the tree

- **Folders with names that explain.** Group components (and features) in folders of the
  FeatureManager tree whose name says why they are together. It helps to find, isolate or
  hide a group. Bridge 0.152.10 cannot create folders yet: ask the user, or note it.
- **Alternative accessories stay in the assembly, suppressed, in their own folder** (for
  example "EXTRA ACCESSORIES"). They are parts equal to the ones in use with small changes:
  switching one on is faster than inserting and mating it again. They are not scrap.
- **Variants are separate part files with their own codes**, not configurations inside one
  part. Exception: sheet metal (the flat pattern configuration is normal there).
- Name features and sketches after what they do when the tree gets long.

## Parts

- **Fully defined sketches.** Every sketch black (fully defined) before the feature. An
  under-defined sketch moves on the next change.
- **Build on the origin and the main planes.** Put the first sketch on a main plane, with the
  part symmetric about the planes where the part is symmetric: mirrors and mates get easier.
- **Key sizes in global variables**, linked to the feature dimensions. Name them after what
  they mean (`wall_height`, `rib_height`), not after the feature.
- **Order of features:** main body, then cuts and holes, then patterns and mirrors, then
  chamfers and fillets last.
- **Reference stable geometry.** Prefer planes, the origin and sketch geometry over faces
  and edges of later features: those change when the model changes.
- **One part, one job.** If a part needs many variants, it is probably more than one part.

## Designing for 3D printing (FDM)

- Ask the user for the printer, nozzle, layer height and material; do not assume them.
  Put them in the drawing note.
- **Clearances and press fits depend on the printer.** Do not invent a number: ask the user,
  or make a test piece and measure it.
- Chamfer the edges that touch the bed or that go into another part: a small chamfer
  helps assembly and hides elephant foot.
- Check overhangs (`check_overhangs`) and choose the print orientation before finishing the part.

## Drawings

- Dimension what production and inspection need, not every edge.
- A note on the first sheet says the material, how it is made, and what is still provisional.
- Section views and details only where they show something the main views do not.
