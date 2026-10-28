# Known pitfalls

<!-- Part of skill solidworks-bridge 0.1.0. Each pitfall says the bridge version it was seen on.
When the bridge is newer, check whether it still happens before working around it. -->

| Seen on | What happens | What to do |
|---|---|---|
| 0.152.10 | `outline_mm` of a drawing view includes a margin of about 6 mm per side around the drawn geometry (5.8 and 5.9 mm measured on two views). A detail circle inside the outline but in the margin passes the "fits" check and is then refused with "visible edges 0". | Place detail circles on edges you can see in the exported PDF image, not from the outline alone. |
| 0.152.10 | `inspect_part` on an assembly says "It is NOT assembled" and lists as free the components driven by a component pattern and the suppressed components. | Check the tree with the user. Count only resolved components that have neither mates nor a pattern. |
| 0.152.10 | `inspect_part` does not read sketches (entities, relations, fully defined state), mate references and distance values, or tree folders. | To learn from a person's model, ask them what the sketches and the mates contain. |
| 0.152.10 | No operation creates a component pattern or a tree folder. | Ask the user to do it, or say that it is missing. |
| 0.152.10 | `mate` has no width or symmetric variant: `variant` is coincident, parallel, perpendicular, distance, concentric or tangent. | Ask the user to add the width mate, or say that it is missing. Do not replace it with two distances without saying so. |
| 0.152.x | `interference_check` leaves the assembly marked as changed. `document` `close` then refuses it. | Save it, or let the person close it. |
| 0.152.x | `balloons` with the default order can swap two numbers between two equal runs (`numbers_decided_by_a_tie`). | Use `order` `bill_of_materials` when the numbers must not move. |
| 0.152.9 - 0.152.10 | After `set_property` on a model, an open drawing keeps the old text in the title block until it is closed and reopened. One test exported the PDF with the old value, one with the new. | Close and reopen the drawing before `export_pdf`. |
| 0.152.x | A refused `detail_view` still uses up a view number: the names of the next views shift. | Read the view name from each answer. |
| 0.152.10 | After `document` `open` brings a document to the front, a plan that cites an identifier (V1, F3, C1) is refused: «I have not read ... since «document» brought it to the front». | Call `inspect_part` after `document` `open`, then copy the identifiers from it. |
| 0.152.x | A plan with an operation that does not exist is refused whole, before any step runs. | Check the name with `list_operations` or `describe_operation` (argument `name`). |
| all | SOLIDWORKS is one program shared by every session and by the person. A test bench refuses to start while documents are open. | Agree on turns; leave SOLIDWORKS with no documents open when you finish. |
