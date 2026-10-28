# Advice from an experienced designer

<!-- Part of skill solidworks-bridge 0.2.0. Rules 1-7 of "Mates" and "Organizing the tree", and
the "Drawings" section, come from an assembly and drawings corrected by hand by an experienced designer (28/10/2026);
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

These rules come from drawings corrected by hand by an experienced designer (28/10/2026).

### Who reads the drawing

- The drawing is read in the **workshop**, often by people without a strong technical
  background. It must let them work **on their own**, without asking.
- **Better one dimension too many than one too few.** A dimension that is missing stops the
  work; one that is repeated costs nothing.
- Every feature needs its **size and its position**. A slot with its length but not its distance
  from the end cannot be made.
- Add a word to a dimension when it says what it is for: "2,5 CANALE" (channel), not only "2,5".
- On an assembly, also put the dimension that states **the purpose** of the design (for example
  where the primers sit when they slide). This is judgment, not a rule: put it when the part
  exists for that reason.

### Scale

- Every orthographic view (front, top, side...) is at the **scale written in the title block**.
- The isometric view may have another scale: it only shows the shape. It must fit on the sheet.
- Section views and details **enlarge** (2:1, 5:1), never shrink.

### Dimensions

- Chains: put the **small dimensions nearest to the part**, then the chains of longer ones,
  and the **overall size last**, farthest out.
- **Arrows follow the text.** Text between the extension lines: arrows inside. Text outside
  (the space is too small): arrows outside. Never text inside with the arrows outside.
- **Short dimensions** (about 1-2 mm on the sheet, where the number does not fit): the number
  goes **outside** the extension lines and the arrows point in from outside.
- Dimensions of a **detail go above it**, never between the detail and its label
  ("DETTAGLIO A / SCALA 5 : 1").
- Vertical dimensions step outwards: the shortest nearest to the part.
- Chamfer: a callout with a leader from outside the part, "0,4 x 45°". The text never sits on
  the lines of the part.
- On an assembly, positions decided by the mates are **reference dimensions in parentheses**:
  (35), (135).

### Details

- The circle takes in the whole feature (for a profile: the whole profile).
- Style **"with leader"** (SOLIDWORKS: "Con linea di associazione"): the letter sits at the end
  of a short line from the circle. "Fix position" on; no profile, full outline or jagged
  outline.
- The label says the letter and the scale: "DETTAGLIO A / SCALA 5 : 1".

### Layout

- Notes at the top left: material and how the part is made, then "QUOTE NON INDICATE: DA
  MODELLO 3D" (sizes not dimensioned come from the 3D model).
- Views do not touch each other or the isometric view.
- An assembly drawing fits on **one sheet** when it can: views, section, detail, BOM and balloons.
- The BOM sits **on top of the title block**, as wide as it.
- Balloons go on every view where they help: isometric, section and detail, repeated.
- Each drawing is saved **in the same folder and with the same name as its 3D file**
  (26P000007.SLDPRT and 26P000007.SLDDRW side by side): with another path SOLIDWORKS loses
  the references.
- Section views and details only where they show something the main views do not.
