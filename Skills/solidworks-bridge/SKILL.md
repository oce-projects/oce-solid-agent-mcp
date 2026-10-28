---
name: solidworks-bridge
description: How to design parts, assemblies and drawings in SOLIDWORKS through the oce-solid-agent-mcp bridge (MCP server "solidworks"), with the working method, the known pitfalls and the advice of an experienced designer. Use it whenever you model, mate, draw or export with the bridge, or when you read a part or an assembly made by a person to learn from it.
---

# Designing with the SOLIDWORKS bridge

<!--
Skill version: 0.1.2 (draft)
Tested with: oce-solid-agent-mcp bridge 0.152.10, SOLIDWORKS Premium 2021 SP5.1 (Italian UI)
Changelog:
0.1.0 - 2026-10-28 - first draft, written from 20 test rounds on a real assembly
                     (two-half channel, clips, couplers, 3-sheet drawing) and from
                     an assembly corrected by hand by an experienced designer.
0.1.1 - 2026-10-28 - names checked against the bridge sources: the bridge has no
                     width mate, the version comes from the server, `offset_mm`.
0.1.2 - 2026-10-28 - `inspect_part` `view_edges` to choose the edges of
                     `drawing_dimension`; a plan that cites an identifier after
                     `document` open wants `inspect_part` first.
-->

This skill is for the AI that drives the bridge. It does not replace the bridge's own
descriptions (`list_operations`, `describe_operation`): it says **how to work** with them,
**what goes wrong**, and **how an experienced designer would do it**.

## 0. Check the version first

1. Read the bridge version: the server sends it with its name, `oce-solid-agent`, when the
   client connects (MCP `serverInfo`).
2. This skill was tested with **bridge 0.152.10**.
3. If the bridge is newer, the advice still holds, but a pitfall in
   `references/known-pitfalls.md` may already be fixed: trust what the bridge answers today.
4. If the bridge is older, some operations named here may not exist: check `list_operations`.

## 1. The working method

1. **Look before you touch.** Call `inspect_part` before any plan. Face, edge and component
   identifiers (F1, E4, C1) are born there and hold **for that reading only**. After `document`
   `open` too: a plan that cites an identifier (V1, F3, C1, M1) is refused until `inspect_part`
   has read the document; a plan that cites none runs.
2. **Read again after every change.** After a plan the part is different: old identifiers are void.
3. **Ask for a picture when the shape is in doubt.** `inspect_part` with `views` (start with
   `isometric`). Never take a measurement from a picture: measure with the `checks` operations.
4. **Short plans.** One intent per `build` call (a feature, a group of mates, one drawing sheet).
   A plan that fails stops where it failed: the steps before stay. Read the answer, then go on.
5. **Use `undo` to withdraw a wrong plan** instead of rebuilding the part from scratch.
6. **Work on copies** when you test, and never save over a file the user has not looked at.
7. **One driver at a time.** SOLIDWORKS is one program: if another session or a person is
   using it, agree on turns before you start, and leave it with no documents open when you finish.
8. **Read the refusals.** The bridge refuses a step that would give a wrong result and says
   "I touched nothing". The message usually names the right value or the right view: use it.
9. **Numbers come from the bridge, not from you.** Volumes, distances, interferences: take them
   from `measure`, `measure_between`, `minimum_distance`, `interference_check`, `mass_properties`.

## 2. Parts

- Start with `inspect_part` on the empty part, then the first body from the `extrusions` family.
- Put the key sizes in **global variables** (`global_variable`, `parameters`) and link the
  feature dimensions to them (`link_dimension`): a change is then one number, not ten.
- Add chamfers and fillets **last** (`chamfer_edges`, `fillet_edges`), after the shape is final.
- Set the identity (`set_property`) and the material (`apply_material`) before the drawing:
  the title block reads them from the model.
- Check printability before exporting for 3D printing: `check_overhangs`, `check_body_geometry`,
  then `export_stl`.

See `references/designer-advice.md` for how to build a part with design intent.

## 3. Assemblies

- `insert_component` places a part; it does **not** mate it. A component with no mate only
  rests there: look at `components_still_free` and `components_resting` in `inspect_part`.
- The first component is **fixed** (`fix_component`), aligned with the assembly origin and planes.
- Mate with **contact and symmetry** first (`mate` with `variant` `coincident`, a width mate for
  symmetry), and use a distance only where the position is a real design choice. Bridge 0.152.10
  cannot make a width mate: ask the user to add it, or say that it is missing. Details in
  `references/designer-advice.md`.
- After mating, always run `interference_check`. A small volume (hundredths of mm3 to a few mm3)
  can be an intended press fit; one part inside another is thousands. Ask the user which it is.
- `interference_check` marks the assembly as changed even if nothing changed: save it
  (`save_document`) or `document` `close` will refuse it.
- To learn from an assembly made by a person, compare readings: `inspect_part` of each part
  (features, dimensions, equations, volume, faces) and of the assembly (mates, components, states).

## 4. Drawings

Full recipe in `references/drawings.md`. The short version:

1. `create_drawing` (paper size), `standard_views`, `model_view` isometric, `save_document`.
2. `drawing_dimension` per view (it takes time: 15-30 s each is normal). To choose its `edges`,
   read them first: `inspect_part` with `view_edges` (`{"view": "V1"}`) lists each edge with the
   number `edges` takes, its kind, its mm, how it faces and where it sits on the sheet.
3. `section_view`, `note`, `detail_view` (with `x_mm`, `y_mm`, `radius_mm` on real edges).
4. `new_sheet` + `model_view` + `drawing_bom` + `balloons` for the assembly sheet.
5. `export_pdf` with `search` for the texts that must be there, then **look at the PDF as an image**.

## 5. Before you say "done"

- The part or assembly was read again after the last change.
- `interference_check` was run on an assembly, and every interference is explained.
- The drawing PDF was exported **and looked at**: every view on the sheet, every balloon on its part.
- Nothing was saved over the user's files without their consent.
- SOLIDWORKS is left with no documents open by you.

## References

- `references/designer-advice.md` - how an experienced designer models, mates and organizes.
- `references/drawings.md` - the drawing recipe and its traps.
- `references/known-pitfalls.md` - bridge behaviours that surprise, with the version they were seen on.
