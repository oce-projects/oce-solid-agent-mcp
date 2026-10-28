# Drawings with the bridge

<!-- Part of skill solidworks-bridge 0.2.0. Recipe used in test rounds 14-24 on bridge
0.152.x: a 3-sheet A3 drawing of an assembly (views, section, details, BOM, balloons,
one part alone on sheet 3). -->

## Recipe (one `build` call per line)

1. `create_drawing` (`paper_size`), `standard_views` (`model`), `model_view` isometric,
   `save_document` with the drawing path.
2. `drawing_dimension` on the front view, then on the top view (`overall`, `components`).
   Each one takes 15-30 s: it is computing, not stuck. Choose the `edges` from `inspect_part`
   with `view_edges`: each edge with its number, kind, mm, facing and position on the sheet.
   Chosen blind, they give dimensions nobody needs (5 mm, 133 mm instead of the overall size).
3. `section_view` (`view`, `orientation`, `offset_mm`, `label`) and `note` (general notes).
   The bridge slides the section along its view to where nothing else is.
4. `detail_view` for each detail (see "Details" below).
5. `new_sheet`, `model_view` isometric of the assembly.
6. `drawing_bom` on that view, `save_document`.
7. `balloons` on the same view, `save_document`.
8. `new_sheet`, `model_view` of a single part (front and isometric), `detail_view` if needed.
9. `activate_sheet` back to the first sheet, `save_document`.
10. `export_pdf` with `search` listing the texts that must be on the drawing.
11. Turn the PDF pages into images and **look at them**: views inside the frame, no overlaps,
    every balloon arrow on its part, the BOM equal to the balloons.

## A part drawing the way the designer wants it (bridge 0.152.10)

Read the rules first: `designer-advice.md`, section "Drawings". On bridge 0.152.10 this is how
to get closest to them (tested on 26P000007, round 24):

1. **Views at the sheet scale.** `standard_views` always made views at 0.5 (sheet 1:1 and 2:1
   tried). Instead: `set_sheet` with the scale, then `model_view` `front`, `top` and `left` at
   sheet coordinates. First-angle projection: `top` **below** `front`, `left` **to the right**
   of `front`, same x or same y. These views are not linked: moving one does not move the others.
2. `model_view` `isometric` in a free corner.
3. `detail_view` on the side view, circle on the whole profile (`radius_mm` about 6.5 for a
   9 x 7 mm profile), `scale` 5, `label` A; `move_view` it with `x_mm`, `y_mm`.
4. Read every view with `inspect_part` `view_edges` and choose the `edges`.
5. **Chamfer first**, on the view where you want it: the first `drawing_dimension` call puts the
   chamfer callout by itself on the view it works on, and refuses it later on another view.
6. **One step per dimension** with the same `offset_mm` makes one row (a chain). In one step
   with many edges the dimensions go down in a staircase. Rows: small ones at 12, chains at 22
   and 32, the overall size at 42.
7. **Detail dimensions above the detail:** a negative `offset_mm` moves a horizontal row up
   (-50 put it above a 54 mm tall detail). Vertical dimensions: `offset_mm` 12, 24, 36 step
   them out on the side.
8. Export the PDF, turn it into an image, **look at it**, fix, then `save_document`.

What 0.152.10 cannot do yet (asked of the bridge, see `known-pitfalls.md`): a distance between
two edges, text added to a dimension, the side of a dimension, arrow placement, the detail circle
style, moving a dimension, a scale on `section_view`.

## View names

- `model_view` and `detail_view` answer with the name of the view they created
  ("Drawing View9", in the language of the SOLIDWORKS installation). **Use that name** in the next plan.
- A refused or removed view still uses up a number: after a refusal the next views shift.
  Do not copy view names from an older plan.
- `V1`, `V2` ... in `inspect_part` number the views **of the active sheet only**. On a drawing
  with more sheets, use the view name, not `V1`.

## Details

- Give `x_mm`, `y_mm` (from the view center, on the sheet) and `radius_mm` on **real edges**:
  a corner, a chamfer, a step. A circle on a flat face or on empty space is refused
  ("visible edges 0") and removed.
- The circle must lie inside the view outline, or it is refused ("does not fit").
- The detail scale must be **larger** than the scale of the view it comes from.
- The bridge places the label under the detail and the horizontal dimensions under it too:
  they collide. Put the dimensions above (negative `offset_mm`).
- To choose the spot, look at the exported PDF as an image and place the circle on an edge
  you can see. See `known-pitfalls.md`: the outline of a view has a margin around the drawing.

## Bill of materials and balloons

- `balloons` with `order` `around_the_view` (default) numbers the balloons around the view and
  renumbers the BOM. When two numbers depend on how SOLIDWORKS returns nearby parts, the answer
  lists them in `numbers_decided_by_a_tie`: the numbers may change between two equal runs.
- `order` `bill_of_materials` keeps the BOM order and gives the same numbers every time.
- Ask the user which numbering they want.

## Title block

- The title block reads the properties (`$PRPSHEET`) of the model shown in that sheet's views.
  A sheet that shows another part shows **that** part's properties.
- `title_block` lists every sheet, its format file and its model.
- After you change a property on the model, the open drawing may still show the old value.
  `title_block` says so (bridge 0.152.10). Close and reopen the drawing before exporting the PDF.

## PDF search

- `export_pdf` `search` finds the texts in the PDF. A text wrapped over two lines is reported in
  `found_on_several_lines` (bridge 0.152.10): look at that box, the text may run out of it.
