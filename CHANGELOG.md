# OCE Solid Agent — changelog

> **LANGUAGE OF THIS FILE — all of it is in English since W3.1-bis (decision D2).**
> Entries written from 16 September 2026 on (W3.1 / W3.2) were born in English.
> The historical entries before them were written in Italian and were translated
> in full in W3.1-bis.10, before the first public release (decision D2, taken on
> 28 September 2026 and confirmed on 3 October 2026). The translation changes the
> language, not the content: every sentence is kept, nothing is summarized, and
> the rule of entry I1 still holds — *a rewritten changelog is a changelog that
> lies*. Names of operations, arguments, fields, classes, files and folders are
> given in their English form from the rename map
> (`Checks/language/name-map.tsv`, archived outside the repository in W3.1-bis.11), so that they match the repository as it is
> published; SOLIDWORKS names, literal values and names with no row in the map
> stay as they were written. The entries of the rename windows (W3.1-bis.4 to
> .6) are the one place where the names from before the rename are quoted on
> purpose, each with its English name beside it, and so are the sentences between
> «» that quote what a program printed in Italian.

**RULE, not habit: every code change bumps the version in
`VERSION.txt` and adds an entry here.**

Why it is a rule: this bridge exists to MEASURE, and a measurement without the
version number of what produced it is not a measurement — it is an
anecdote. If a month from now a test says something different from today, the
first question will be "with which version?", and it must have an answer.

Criterion (reduced semver MAJOR.MINOR.PATCH):

- **MINOR +1** when the bridge gains visible tools or capabilities;
- **PATCH +1** for fixes;
- **MAJOR** stays 0 until the tool protocol is stable.

---

## 0.152.6 — 27 October 2026 · library 0.174.5 · SOLIDWORKS no longer falls after some drawings made through the bridge (branch `night-2026-10-26-asm`)

- **Found while checking 0.152.5**: SOLIDWORKS fell inside `new_sheet` in the bench
  (case `two_sheets_and_the_same_projection_angle`, after about 50 drawings) and in a
  scenario on a copy (after about 9), with `RPC_E_SERVERFAULT` and the same stack in
  the journal: the window of a drawing being painted, `uiBaseView_c::OnPaint` and
  `uiModelView_c::OnPrepareDC`. The code of `new_sheet` had not changed since 0.152.3,
  and 12 sheets made one after another by a probe on a drawing opened from disk did not
  fall (0.44 to 0.59 s each).
- **Measured, the same cycle 15 times on a fresh SOLIDWORKS** (a drawing from a part,
  a second sheet, save, close):

  | driven by | cycles | fall | working set of SLDWORKS |
  |---|---|---|---|
  | bridge 0.152.5 | 7 | at the 8th | not logged |
  | bridge 0.152.5 | 10 | at the 11th | 532 to 1094 MB |
  | bridge with a collection after every call | 15 | none | 537 to 812 MB |
  | a probe, without the bridge | 20 | none | 660 to 746 MB |

  GDI objects (at most 2091) and USER objects (1722) stayed far from their limit of
  10000 per process. The bridge never released a SOLIDWORKS object: each one it reads comes as a
  runtime callable wrapper, and SOLIDWORKS keeps the object alive until the wrapper is
  finalized, which the garbage collector of this small process seldom does. A drawing
  closed through the bridge stayed in SOLIDWORKS.
- Now the bridge collects and finalizes after every `tools/call`, after the answer is
  sent (`ReleaseSolidWorksObjects`): the client does not wait for it; measured inside
  the call it cost about 0.9 s.
- The falls of rounds 13 and 14 (`create_drawing` at the 15th and 23rd drawing,
  a drawing opened at the 16th) had another stack, also while a drawing window was
  painted; whether they had the same cause is not tested.
- Contract Zc2197: the release is wired after the answer.

## 0.152.5 — 27 October 2026 · library 0.174.5 · fourteenth round of session 8cd843: N52 to N56 (branch `night-2026-10-26-asm`)

- **Fourteenth round on SOLIDWORKS (session 8cd843, bridge 0.152.4)**: N47 to N51
  held. The details are full, the section has its hatching and its letter, the
  numbers of the balloons follow the table. A new drawing of two sheets from the
  start, a detail on empty space (refused, as it must) and the assembly checked.
  Five new defects, and two observations.
- **N52 (P3)**: the circle of a detail landed 1.09 mm lower than asked on the front
  view of the report, and not on the view below. The report supposed the origin of
  the view sketch was wrong. A probe on a copy of the drawing measured it otherwise:
  `GetXform`, `ModelToViewTransform` and the `ModelToSketchTransform` of the view
  sketch agree, the circle reads back at (0, 0) in the sketch, and the PDF (circles
  fitted to the segments SOLIDWORKS draws) put it at (94.479, 228.386) mm with
  radius 5.117 mm, asked (94.5, 229.49) mm with radius 5 mm. The sketch had snapped
  it onto the geometry of the view around it. The same circle drawn with
  `SketchManager.AddToDB` on landed at (94.480, 229.472) mm with radius 5.000 mm.
  Now `detail_view` draws its circle and `section_view` its cutting line with
  `AddToDB` on (put back as it was after), read them back from the sketch, and
  refuse and take them away if they moved by more than 0.001 mm
  (`LandedElsewhere`): the message says asked and read, in millimetres of the view
  sketch. The reading was seen to work before it was trusted: the centre of a circle
  is the `ISketchPoint` of `ISketchArc.GetCenterPoint2` (read as an array it is
  always null, and the check would never refuse). Read that way, the probe gave the
  snapped circle at (0, -2.175) mm with radius 10.234 mm in the sketch, that is
  (94.5, 228.402) mm and 5.117 mm on the sheet, as the PDF; with `AddToDB` on,
  exactly the numbers asked.
- **N53 (P2)**: the section was born on top of the side view (outlines 214.997 to
  256.701 mm and 220.648 to 262.352 mm in x on the same band in y). The probe read
  that a section is aligned to its view (`GetAlignment` 2): a move across its line
  does nothing (asked +30 mm in y, moved 0 mm), a move along it is exact (asked
  +30 mm and −150 mm in x, moved +30 mm and −150 mm), and the outline does not
  change after birth (three round trips, the same to 0.001 mm). Now `section_view`
  slides the section along its line to the nearest place where it overlaps no view,
  table, note or title block, inside the frame (`SectionInAFreePlace`,
  `NearestFreeAlong`), says the place in `placed`, and is refused and taken away
  when there is none.
- **N54 (P3)**: there was no operation to move or take away a view. Two new
  operations, so the catalog has 107. `move_view` moves a view by `dx_mm`/`dy_mm`
  or to `x_mm`/`y_mm`, reads the move back from the outline, and puts back and
  refuses a move done only in part (an aligned view moves only along its line); it
  checks that each view moved (the ones aligned to it move with it) does not fall on
  anything. `delete_view` takes a view away and reads the views back. Measured on the
  probe: deleting a view takes its sections and its details with it, leaves its
  projected views without a parent, and a detail takes its circle off the parent
  view; so a view with sections or details is refused unless `with_derived` is true,
  and a view that disappears without being asked is said in the answer.
- **N55 (P3)**: `save_document` with `path` replaced another existing file without
  saying it. Now the answer says `replaced_another_file`, the bytes and the date of
  the file replaced; `overwrite` false refuses, and nothing is saved.
- **N56 (P2)**: three balloons of the report ended within 0.725 mm and 1.549 mm of
  each other, on the mouth of a coupler where three parts meet. A probe read the
  arrow tip as the last point of the leader, in sheet coordinates.
  `SetLeaderAttachmentPointAtIndex` read back the new point but the PDF still drew
  the old one, so the arrow is not moved: the balloon is put again. `balloons` now
  finds the balloons whose arrow ends within 5 mm of another arrow
  (`CrowdedIndices`), takes the visible lines of the view, decides which part owns
  each piece by its geometry (the visible edges of the part, tessellated and carried
  onto the sheet by the transform of the component and of the view), picks the point
  of the part farthest from the other arrows, inserts a new balloon on the edge there
  without changing item numbers, checks its number, takes the old one away and puts
  the new one where the old one stood. On the copy: the three tips went to 28.092 mm,
  8.93 mm and 8.883 mm from the nearest arrow, the numbers did not change, and the
  PDF made a picture shows each arrow on its own part. The answer says
  `arrows_moved` and the numbers read again; finding the owners took 7.26 s for three
  parts.
- **Observation 2**: the `position_mm` of a detail is not where the detail is seen
  (150 mm away on the report). `inspect_part` with the drawing now gives each view
  `outline_mm` and `centre_on_the_sheet_mm`, read from its outline.
- `ReadSheet` can leave out a list of views (the ones moved together). Catalog of
  `section_view`, `detail_view`, `balloons`, `save_document`, `move_view`,
  `delete_view`; six bench cases (`view_is_moved`, `view_is_deleted`,
  `save_says_what_it_replaced` and their refused twins); contract Zc2197.

## 0.152.4 — 27 October 2026 · library 0.174.4 · thirteenth round of session 8cd843: N47 to N51 (branch `night-2026-10-26-asm`)

- **Thirteenth round on SOLIDWORKS (session 8cd843, bridge 0.152.3)**: N44, N45 and
  N46 held, N43 held for the positions (three details with three different centres,
  their labels inside the frame). Real work on copies: the assembly checked and a
  new drawing of two sheets from the start. Looked at as a picture, the PDF said
  more than its texts: five new defects.
- **N47 (P1)**: the three details were empty in the PDF. The circle went in the
  sketch of the view since 0.174.2 (N41), but with sheet coordinates, while that
  sketch has its origin at the centre of the view and measures the model. A probe on
  a copy of the drawing of the report read `IView.GetXform` of the view below as
  [94.5 mm, 110.69 mm, 0.5]: the circle wanted on the sheet at (134.5, 110.69) mm,
  radius 10 mm, drawn at the sheet coordinates gave a detail with 0 visible edges;
  drawn at (80, 0) mm, radius 20 mm, one with 11. Now `detail_view` converts the
  centre and the radius, (sheet - centre) / scale (`SheetToViewSketch`), and says
  them in `circle_in_the_view_sketch_mm`. A view that cannot be activated is refused
  (no more the sheet sketch, which hands the circle to another view), and so is a
  turned view (only views at 0 degrees were measured). One more detail circle on the
  view was not a proof: the detail must be made from the view asked (`base_view`,
  read with `GetBaseView`) and show at least one edge (`visible_edges`), otherwise
  it is refused and removed with its circle. The first bench run caught the bench
  case itself: `detail_enlarges` asked for a detail at the centre of the front view
  of a plate (120 x 20 mm at scale 1), where a circle of radius 8 mm stays inside
  the face; measured (scenario t40), 0 visible edges there, 1 with `x_mm` 55 at the
  right end of the plate, 2 at a corner of the top view, and the PDF made a picture
  shows the edges in the two details. The case now passes `x_mm` 55 and expects 1
  visible edge (its twin `limit_detail_outside_the_view` too, still one argument
  apart), and the new limit case `limit_detail_on_empty_space` keeps the centre and
  expects the refusal.
- **N48 (P1)**: the cutting line of A-A, wanted across the view above, was at the
  left end of the view below, and «the hatching is there» held for that view. The
  line was drawn on the sheet sketch, which gave it to the other view (measured on a
  copy: base the view below, 1 face hatch). Now `section_view` activates the view
  and draws the line in its sketch, converted as the circle (measured: base the view
  asked, 2 face hatches), with `cut_line_in_the_view_sketch_mm`; `cut_line_mm` stays
  in sheet millimetres. A section made from another view is refused (`base_view`),
  and the line of every refusal is removed from the sketch of the view.
- **N49 (P2)**: `new_sheet` made a sheet with no format, so no frame and no title
  block (swDwgTemplateNone). Now the format file of the current sheet
  (`ISheet.GetTemplateName`) is passed with swDwgTemplateCustom, and read back from
  the new sheet (`format`); a format that did not come is refused, a format file
  that cannot be found is refused before anything is created, and a `paper_size`
  given keeps the sheet without a format (the format is drawn for its own size).
  Measured on a copy: the second sheet of round 13 read "" (none), the new sheet
  read the .slddrt of the first, with the 67 fields of the title block.
- **N50 (P2)**: the printed bill of materials was not `rows_read`. Measured on a
  copy: the table read the same right after it was born, after a rebuild, after a
  save and a reopening; it was `AutoBalloon5` that renumbered it, with `ItemOrder`
  coming as 4 (order sequentially): 26P000007 to 26P000013 as items 1 to 7 before
  the balloons, 26P000010, 11, 9, 8, 7, 13, 12 after, the order printed in round 13;
  with «do not change item numbers» the table kept its order. The order is now set
  explicitly and kept as every drawing had it (which order a drawing wants is not
  decided here), and `balloons` reads the table again: `bom_renumbered`,
  `bom_rows_after` and a note that the `rows_read` of `drawing_bom` no longer hold.
- **N51 (P3)**: a note in the same plan after `section_view` went into the section
  view; the view activated for the line stayed active. Now the sheet is in edit
  again right after the section is created.
- **Observation**: «in the first free place (147/144 mm)» now says that the numbers
  are the bottom-left corner in millimetres of sheet.
- **A detail view just born is not yet the view** (found by scenario t39, the
  drawing of round 13 made again with this version): detail B, now full of
  geometry, was sent «above the title block» and refused there, «falls on the title
  block». The refusal now carries the rectangles asked, before and after, and they
  said it: the outline read 56.1 x 31.2 mm at birth and 100.3 x 50.5 mm after the
  move. A probe on a copy read the same 56.1 x 31.2 mm at birth, after Position set
  to its own value and after a move of 1 mm, and 100.3 x 50.5 mm after the move back
  (on a second run already after the move of 1 mm), the size the PDF shows with the
  end part of the assembly whole in it; Position then read 78.4 mm away from the
  value read at birth, and on the first run the view had jumped by as much;
  EditRebuild3 and ForceRebuild3 changed nothing after. Now `detail_view` settles the
  view right after it is born (`SettleDetailView`: a move of 1 mm and back, until a
  round trip leaves its outline where and as big as it was, within 0.1 mm, at most
  three times) and only then judges and moves it. Scenario t39 again: B «above the
  title block», C «bottom left», D beside its view, all inside the frame with their
  labels, nothing on top of anything (the PDF made a picture), the 8 texts found.
- **SOLIDWORKS fell twice on `create_drawing`** during this work (the first bench run
  and a scenario), with the same stack in the journal both times: the window of the
  new drawing being painted, `moSketchBitmap_c::getProfileFeature` and
  `uoBillboardi_c::make`, after 23 and 15 new drawings in the session. The operation
  did not change; the cause is not tested (a hypothesis: the picture of the format
  painted on a new drawing). SOLIDWORKS was restarted and the step done again.
- `RemoveSheetSketchSegment` and `SheetSketchSegmentCount` are gone: nothing draws on
  the sheet sketch any more. Catalog of `detail_view`, `section_view`, `new_sheet`,
  `balloons`; contracts Zc2189 (M41) and Zc2194 (N41) follow the new road; contract
  Zc2196.

## 0.152.3 — 27 October 2026 · library 0.174.3 · twelfth round of session 8cd843: N43 to N46 (branch `night-2026-10-26-asm`)

- **Twelfth round on SOLIDWORKS (session 8cd843, bridge 0.152.2, 47 calls)**: N39,
  N40, N41 and N42 held. With M15 suppressed, C8 read `mates` 2, `mates_suppressed`
  1 and the note, `place_component` moved it 6.5 mm, and with M15 back it went back
  by itself; a section and a refused detail in one plan stopped with notes that
  agree, and the undo refused with the reason; three details out of three on views
  of an assembly, the circle in the sketch of the view; views counted 4 to 5 to 8,
  as `inspect_part`. Real work on copies: the assembly checked (15 active mates, 0
  degrees of freedom left on the mated components, only the two parts that decision
  6 leaves out are loose, 6 interferences of 19.033 mm³), a drawing of two sheets
  with a section, three details, the table, the balloons, a general note and the
  PDF of 2 pages with the 9 texts found. The time of `drawing_dimension` goes to the
  edges on the sheet (10.2 to 10.7 s a view, 95.5 % of the refusal of V3). Four
  new defects.
- **N43 (P2)**: `detail_view` put every detail of a view at the same place, beside
  it: two details of V1 had the same centre, the detail of V2 the centre of the
  section A-A, and no operation moves a view. Now the detail stays beside the view
  only when that place touches nothing; otherwise it goes to the first free place of
  the sheet, outside the other views, the tables, the notes and the title block, as
  the flat pattern view and the tables do (`placed` says where), and it is judged
  again where it landed. With no free place it is refused and removed with its
  circle. The place counts the label too: it is a note of the detail view, under
  its outline (a probe on a copy read the outline of detail B at 209.6 to 249.5 mm
  and its label at 191 to 207.2 mm), and the first try, looked at on the PDF, put a
  detail at the bottom left with its label below the frame. For the same reason
  the free place no longer counts the notes of the view being moved as obstacles.
  Where the detail landed is its rectangle with the label moved as far as the
  outline moved: right after `Position` the extent of the label still said the old
  place, and after a redraw it had moved by the same (-150, -150) mm as the outline,
  to 0.1 mm (probe on a copy). Read back, the label was judged where it had been
  (second try refused); read after a redraw, an obsolete call, 0.1 mm of rounding put
  a detail placed at the frame out of it (third try refused).
- **N44 (P2)**: the report blamed `title_block` for a drawing left «to be saved».
  Measured on a copy of the drawing (scenario t35s, the flag read after every step):
  `title_block` left it saved twice; `export_pdf` of the two sheets turned it from
  saved to «to be saved». The plan of the report ended with `save_document` and then
  `export_pdf`. Now `export_pdf` reads the save flag before and after the PDF and,
  when the PDF marked a saved drawing, says so (`marked_unsaved`, with the remedy:
  `save_document` after `export_pdf`), as `interference_check` does (N16).
- **N45 (P3)**: `title_block` read the general note put on the sheet as the first
  field of the title block (67 then 68). The annotations of the sheet hold both.
  `IAnnotation.OwnerType` tells them apart (IAnnotation~OwnerType.html); a probe on a
  copy of the drawing (outside the repository) read the general note 1
  (drawing sheet) and the 67 fields 2 (drawing template), and the save flag stayed
  false through the reading. Now only the notes of the format are the title block,
  and `notes_on_the_sheet_left_out` counts the others; a note whose owner cannot be
  read is kept, as before. The note counts of `note` stay as they are: they check
  that the note was born (one more), not what the title block holds.
- **N46 (P3)**: `place_component` said `mates` 3 for a component with one of its 3
  mates suppressed, beside the 2 of the state. Now it says the mates that hold and
  `mates_suppressed`, as the state; `fix_component` too.
- Observations of the report: the sentence on the degrees of freedom of an assembly
  just opened said «after the first plan», and after `interference_check` it was
  still 8 of 8 «unavailable»: it now says «until a plan has changed a mate»; the
  refusal before a document was looked at said «any part» with a drawing in front:
  it now says «any document», that the one looked at may have been closed, and that
  it can be named in `document` (the refusal itself is decision N17); a sheet that
  is not active says `not_read`, instead of reading as a sheet with no views (the
  decision not to change sheet to read stays).
- Catalog: `detail_view`, `export_pdf` and `title_block` say the new behaviour.
- Contract Zc2195 (pure rules and wiring, every changed copy red); the floor of the
  refusals that say what to do goes to 281 of 1012 (the detail with no free place).
- Contracts: 944 checks, 2 failed (the bench run is partial, Z-C6). Language gate
  held. Bench, the 10 cases of the operations changed (`detail_view`,
  `title_block`, `export_pdf`, `place_component`, `fix_component`, drawings of two
  sheets): 168 tests, 0 failed, expectations 130 of 130 (report
  `20261027_1931`). A partial run: the full one is still owed before a push.
- On SOLIDWORKS, scenario t36 on copies of the drawing and the assembly of round
  12, bridge 0.152.2 against 0.152.3: `title_block` 68 notes with the general note
  first, now 67 and `notes_on_the_sheet_left_out` 1; `export_pdf` after a save left
  the drawing «to be saved» in both, and now says `marked_unsaved`; the second
  sheet says `not_read`; `place_component` with M15 suppressed `mates` 3, now 2 and
  `mates_suppressed` 1; two new details went on the side view and on the section,
  as B, C and D, and now to the bottom left and to a free place at 77/144 mm, the
  labels inside the frame, looked at on the PDF made a picture.

## 0.152.2 — 27 October 2026 · library 0.174.2 · eleventh round of session 8cd843: N39 to N42 (branch `night-2026-10-26-asm`)

- **Eleventh round on SOLIDWORKS (session 8cd843, bridge 0.152.1, 52 calls)**: N37
  and N38 held (the refusal of N38 kept: «the argument of the standard holds»;
  its cost, 11 s of edges read to say «already dimensioned», is left to the owner).
  Real work on copies: mate suppressed and the undo that put it back, read again;
  STEP AP214 with 7 solids of 7; a drawing of two sheets with the overall sizes,
  a section A-A, the table and 7 balloons on the second sheet, two properties of
  the title block shown, the PDF of 2 pages with the 8 texts searched; closed by
  the bridge. Four new defects.
- **N39 (P1)**: with one of its mates suppressed, a coupler read «constrained», 0
  degrees of freedom and «mates» 3, and `place_component` moved it 6.5 mm.
  `GetMates` lists a suppressed mate with the others. Now `mates` in the state
  counts the mates that hold the component and `mates_suppressed` the others
  (`ActiveMates`), the kinds skip the suppressed ones, and a component held only
  by suppressed mates is loose for the overall size of a drawing too. The number
  SOLIDWORKS gives for a component with a suppressed mate is left out with the
  reason (`degrees_of_freedom_note`): its reading does not follow the
  suppression. The sentence on what is not known says that on an assembly just
  opened SOLIDWORKS may give no number until a plan ran (8 of 8 «unavailable»,
  then 5 of 8 with a number). Case `a_wrong_mate_is_taken_out` reads both counts.
- **N40 (P2)**: a plan on a drawing that put a section and stopped at a refused
  detail said «refused and changed nothing I compare» beside «the 1 step(s) before
  it are done and stay», and the undo after it said «as it was» while the section
  stayed. SOLIDWORKS had kept an undo entry, which made the plan one to withdraw;
  on a drawing the comparison sees nothing. Now a plan that did a step on a
  drawing leaves no memory whatever SOLIDWORKS recorded, and says so; the remedy
  says that a view has no operation that removes it yet.
- **N41 (P2)**: `detail_view` on a view of an assembly was refused three times out
  of three. Cause measured with a probe on copies of the drawing: drawn on the
  sketch of the sheet, the circle went to another view (the one below), so the
  view asked kept 0 detail circles and the bridge removed the detail; drawn after
  `ActivateView`, the circle went in the sketch of the view asked, and the detail
  had it as its base, at the centre and 40 mm to the right. Now the circle is
  drawn in the sketch of the view (the sheet only when the view cannot be
  activated), removed from that sketch by a refusal, and the sheet is put back
  in edit after the step (`circle_drawn_in`).
- **N42 (P3)**: `section_view` and `detail_view` counted the sheet among the
  views, 5 where `inspect_part` said 4. Now they count the views of the model, as
  `inspect_part` and `model_view`; the cases `section_cuts_the_part` and
  `detail_enlarges` go from 4 and 5 to 3 and 4. Not done: `hatches` 1 and
  `hatches_solids` 0 on a cut through two bodies, not tested.
- Also from the report: a bill of materials with no room says it can go on
  another sheet (`activate_sheet`), and `drawing_dimension` times its phases
  (visible edges, edges on the sheet, dimensions already there, texts) for the
  log.
- Contract Zc2194 (pure rules and wiring, every changed copy red); Zc2189 follows
  the circle removed from the sketch it is in, Zc2192 the timed call.
- Contracts: 939 checks, 2 failed (the bench run is partial, Z-C6). Language gate
  held. Bench, the families `assemblies` and `drawing`: 731 tests passed, 0
  failed (report `20261027_1815`), then a call that did not come back
  (`drawing_dimension` on V2 of `turned_bushing_is_not_a_hole`, idle SOLIDWORKS,
  the share answering, no window open), 2 h 19 min into the SOLIDWORKS session;
  the same case was green at 16:17. The second time in `drawing_dimension`: the
  cause is not tested, a hypothesis. After a restart of SOLIDWORKS the cases not
  yet tried: 83 tests, 0 failed, expectations 59 of 59 (report
  `20261027_1827`). A partial run: the full one is still owed before a push.
- On SOLIDWORKS, scenario t34 on copies of the drawing of round 10 and of the
  assembly of round 11, bridge 0.152.1 against 0.152.2: with M15 suppressed, C8
  read «mates» 3 and now reads 2, `mates_suppressed` 1 and the note, then 3 and 0
  after the mate is back; `detail_view` on V1 of the assembly (x 40 mm, radius 10
  mm) was refused («the detail circles are still 0») and is now done, circle in
  the sketch of the view, circles 0 to 1; `section_view` said views 5 to 6 and
  now 4 to 5, as `inspect_part`. N40: in both versions the undo after a section
  refuses with the reason; the branch of a drawing plan with an undo entry
  recorded was not reached (the detail that recorded one is now done), so it is
  held by the contract only.

## 0.152.1 — 27 October 2026 · library 0.174.1 · tenth round of session 8cd843: N37 and N38 (branch `night-2026-10-26-asm`)

- **Tenth round on SOLIDWORKS (session 8cd843, bridge 0.152.0, 20 calls)**: N33,
  N34 and N35 held, N36 in part. N33: a name the assembly does not have refused
  in 30 ms (round 9: 6965 ms) with the 8 names; V2 with the list of V1 measured,
  `components_not_in_view` «26P000007-1». N34: after a plan that saved and
  exported the drawing, `document list` false, a second save rebuilt nothing
  (0.7 ms), and `document close` closed the drawing. N35: «193,7», «17», «16,3»
  printed and found in the PDF. Real work on copies, in the test position chosen
  by the owner: the assembly mated (free only the two Solid couplers that decision 6
  leaves out), 6 interferences for 19.033 mm³ as in round 9, and the drawing made
  from the start (overall 193.7 × 17 × 16.258, a table of 7 rows matching the
  assembly, 7 balloons, the general note read back the same, the PDF), closed by
  the bridge with the assembly and its parts. Two new defects.
- **N37 (P3)**: the note of the compact answer counted `warnings` 0 of
  `save_document` (how many bits of the Warnings mask of the save are set) as a
  warning: «the 1 warning(s) of the measures stay» over measures with none, «3»
  where there were 2. Now a `warning…` key counts only when it holds a text or a
  list with something in it (`SaysAWarning`).
- **N38 (P3)**: a view of an assembly whose overall sizes were all dimensioned in
  other views answered «No dimension was created», as if something had gone
  wrong, lost the components measured and the ones the view hides, and wrote
  «16.26» beside 16.258. It stays a refusal, as on a view of a part (the catalog
  says it, and the case `the_second_sheet_does_not_repeat_the_dimensions` proves
  it): zero dimensions are not an answer of «done». Now the refusal says that
  nothing is left to dimension that is not already dimensioned (UNI ISO 129-1),
  to leave the step out of the plan, the components measured and the ones not
  in the view; the other refusals of a view of an assembly name the components
  too; and an «already dimensioned» length has three decimals, as
  `dimensions_mm`, on a view of an assembly and of a part.
- Contract Zc2193 (pure rules and wiring, every changed copy red). Contract suite:
  935 checks, 2 failed, both because the last bench run is partial; language gate
  held.
- Bench, the 17 cases that call `drawing_dimension`: 240 tests, 0 failed,
  expectations 208 of 208, twins 6 of 6 (report `20261027_1617`). The first run
  stopped at a call that did not come back (`drawing_dimension` on the side view
  of `chamfered_shaft_does_not_dimension_the_legs`, idle SOLIDWORKS, no window
  open), 5 h 48 min into the same SOLIDWORKS session; after a restart of
  SOLIDWORKS the same cases ran green. A long session as the cause is not
  tested, a hypothesis. A partial run: the full one is still owed before a push.
- On SOLIDWORKS, scenario t32 on two copies of the drawing of round 10, bridge
  0.152.0 against 0.152.1: on V3 «No dimension was created … 16.26» became «I
  touched nothing: … nothing left to dimension that is not already dimensioned:
  overall length 16.258 mm …», with the remedy, the 5 components measured and
  «Not in this view: «26P000009-2»»; a plan of `export_pdf` and `save_document`
  said «The 1 warning(s) of the measures stay» over `warnings` 0 alone, and now
  says no warning.

## 0.152.0 — 27 October 2026 · library 0.174.0 · ninth round of session 8cd843: N33 to N36 (branch `night-2026-10-26-asm`)

- **Ninth round on SOLIDWORKS (session 8cd843, bridge 0.151.0, 53 calls)**: N29 to
  N32 all held. Real work on a copy of the clip assembly: the two Split couplers
  mated 6/6 in a test position chosen by the owner (not the final one), 6
  interferences (the 4 clamps and the wanted press fit of the lower coupler), and
  the workshop drawing measured on the channel, the clamps and the couplers with no
  warning of loose components. Four new defects.
- **N33 (P2)**: `components` refused a component of the assembly because one view
  did not show it (one half of the channel lies under the other in the top view),
  and only after reading the edges of the view, 7 to 10 s. Now the names are
  checked on the components of the assembly before the view is read; a name of
  the assembly that this view does not show goes in `components_not_in_view` and
  the overall size is measured on the others; the step is refused only when the
  view shows none of them. A refusal lists the names of the assembly, up to 20.
- **N34 (P2)**: the drawing saved by `save_document` read «unsaved» right after,
  and `document close` refused to close it. Cause measured with a probe on two
  copies: on a drawing EditRebuild3 raises the save flag every time, with no view
  out of date, and the plan rebuilt after every step, the save included. Now the
  plan and the bench run one after-step, `SwOperations.AfterStep`: a drawing with
  nothing unsaved is not rebuilt (`RebuildsAfterStep`), and after a save the flag
  is read once the rebuild is over (`unsaved_changes`, and
  `warning_unsaved_after_save` if it stays raised). Found on the way: the bench
  did not run the plan executor and rebuilt on its own, so it could not see the
  defect; now it calls the same method, and on an assembly it rebuilds the way
  the plan does (`Extension.Rebuild`, before `EditRebuild3`). The case
  `saved_drawing_stays_saved` was red first, without the rule: 26 tests, 1
  failed, step 5 (the drawing saved) `unsaved_changes` True where False was
  expected; the part saved at step 2 read False (report `20261027_1355`).
- **N35 (P3)**: `drawing_dimension` answered 16.258 where the sheet printed
  «16,3». Now every dimension carries `text_shown`, the numbers its texts print
  (the reading `export_pdf` already made to look for them in the PDF), and the
  answer `dimensions_shown` beside `dimensions_mm`.
- **N36 (P3)**: «none is a warning» read as no warning beside two warnings in the
  measures. Now the note of the compact answer counts the warnings the measures
  keep: «none of them is a warning; the 2 warning(s) of the measures stay, above».
- Contract Zc2192 (pure rules and wiring, every changed copy red); Zc2191 follows
  the check of `components` onto the assembly. Found on the way: two Italian
  strings in the test data of Zc2191 since 0.151.0 (the heading of a general note,
  reported by the session of 0.151.1), now English; the floor of the refusals that say what
  to do goes to 280 of 1011 (the early refusal of N33 says it). Contract suite:
  931 checks, 2 failed, both because the last bench run is partial (not every
  operation has a green case in it); language gate held.
- Bench, the families assemblies, drawing, dimensions and export (the after-step
  runs after every step; a part is rebuilt as before): 966 tests, 0 failed,
  expectations 846 of 846, twins 50 of 50, 82 min (report `20261027_1526`). A
  partial run: the full one is still owed before a push.
- On SOLIDWORKS, scenario t31 on two fresh copies of the drawing of the report,
  bridge 0.151.0 against 0.152.0: a name the assembly does not have refused in
  7 ms instead of 8338 ms, with the names of the assembly; `text_shown` «32,3»
  beside 32.25 mm; the note «the 2 warning(s) of the measures stay, above»; after
  the save `unsaved_changes` false, `document list` false, and `document close`
  closed the drawing (0.151.0: true, and the close refused). With the component
  that V2 hides, the step was no longer refused for it, but because both overall
  sizes were already dimensioned in another view of that drawing: the measure on
  the components V2 shows is not yet seen on SOLIDWORKS.

## 0.151.1 — 27 October 2026 · library 0.173.0 (unchanged) · a dead argument taken out of 21 bench steps (branch `night-2026-10-26-asm`)

- **What was wrong.** 21 steps of 11 bench cases sent «plane» to
  `structural_member` together with «segments». The schema of
  `structural_member` (`Tools/catalog.json`, built from `Catalog/Operations.cs`)
  declares `configuration`, `corner_treatment`, `gap_mm`, `profile_path`,
  `rotation_deg` and `segments`, and no «plane».
- **Read from the code, not assumed.** In `WeldmentOperations.cs` «plane» is read
  in two places only, the frame with centre distances and the frame with overall
  dimensions, and both run only when `RequestedFrame` returns a frame. It returns
  one only for a nested «frame» or for «length_mm» together with «width_mm» (the
  form of `rectangular_frame`); the dispatcher in `SwOperations.cs` sends
  `structural_member` and `rectangular_frame` to the same executor, and nothing
  else on that path reads «plane». A script read the 11 files before the change:
  21 steps with «plane», all 21 with «segments» and none with «frame»,
  «length_mm» or «width_mm». There the argument was read by nobody.
- **Why it is worth taking out.** Since 0.151.0 a real `build` from the bridge
  refuses an argument its operation does not declare (`UnknownArgumentsOfStep`).
  The bench calls the library and does not go through that check, so these steps
  stayed green; but a case is also an example of how a step is written, and the
  same step sent through the bridge is refused.
- **The change.** The pair `"plane": "top",` taken out of the 21 steps, nothing
  else: no operation, no file of the library, no expectation. The steps are in
  `drawing/weld_symbol_on_the_joint` (2), `frames/profiles_are_read_first` (1),
  and in `weldments/`: `cap_closes_the_pipe` (1),
  `cut_list_counts_the_gussets_and_the_caps` (2), `cut_shortens_the_bar` (3),
  `cut_without_gap_does_not_inherit` (3), `gusset_polygon_is_not_degenerate` (2),
  `gusset_sits_in_the_corner` (2), `the_end_cap_skips_a_drilled_wall` (1),
  `the_trim_refuses_an_unknown_end_condition` (2),
  `weld_bead_goes_between_touching_faces` (2). Checked by script after the
  change: every file is valid JSON with CRLF line ends, as before, and no
  `structural_member` step of the 11 carries «plane».
- **Checks.** Contract suite: 925 checks, 4 red, all four about the freshness of
  the last bench run (partial, older than the last commit and than the cases
  changed here, so not every operation has a green case in it); only a full run
  closes them. Bench, the 11 cases alone (`-Case`, `-SkipCompilation`): 140 tests,
  0 failed, expectations 100 of 100, 2011 s (report `20261027_1340`). A partial
  run: the full one is still owed before a push.

## 0.151.0 — 27 October 2026 · library 0.173.0 · eighth round of session 8cd843: N29 to N32 (branch `night-2026-10-26-asm`)

- **Eighth round on SOLIDWORKS (session 8cd843, bridge 0.150.1, 30 calls)**: N23 to
  N28 all held, and the note after a dimension stays on the sheet. The workshop
  drawing of a copy of the clip assembly was finished: bill of materials, 7
  balloons, a general note, the overall length 175 and the clip section 15.6 x
  12.8 named by edge after `view_edges`, all found in the PDF. Four new defects.
- **N29 (P2)**: a note of four lines sent with «\n» came back with «\r\n»:
  `text_matches` false, the step succeeded, and the compact answer said «none is
  a warning» and left the text read back out. Now the comparison makes the line
  ends one (`SameNoteText`), and a text that still differs is
  `warning_text_differs` with both texts. The case
  `note_on_two_lines_reads_back_the_same` was red on 0.150.1 first
  (`text_matches False (expected True)`).
- **N30 (P2)**: the overall height of the clip assembly (31.55 on the front view,
  59.646 on the top view) ended on couplers with no mate, resting where they were
  inserted, and the answer gave it as good. Now an overall dimension that ends on
  a top-level component that is not fixed and has no mate (the rule of
  `components_still_free`) carries `free_components`, and the answer
  `warning_free_components` with the remedy; the new argument `components` limits
  the overall size to the components listed (a name the view does not show is
  refused with the names it shows).
- **N31 (P3)**: `count` on a view of an assembly was set aside under
  `count_ignored`, and the compact answer drops a long text whose key names no
  warning: «count» 3 gave two dimensions in silence. Now `warning_count_not_used`.
- **N32 (P3)**: `delete_annotation` added the notes of the sheet and of the views
  (67 + 7 + 1 = 75), the sum `note` refuses. Now `notes_sheet_*` and
  `notes_views_*` apart, `attached_to`, and `notes_down_by`. The case
  `note_is_taken_away_by_its_text` was red on 0.150.1 first (the fields were
  missing).
- **Found while writing the replay of round 8**: `delete_annotation` looked for a
  note by its exact text, so a note of several lines (read back with «\r\n»)
  answered to nobody. Now the same comparison as `note`; the case
  `note_on_two_lines_reads_back_the_same` takes it away by its text (removed,
  `notes_down_by` 1).
- **Found in the replay of round 8 (scenario t28, old bridge against new)**: an
  argument the operation does not declare was refused only by the dry run; a real
  `build` ran without it. `components` sent to 0.150.1, which did not know it, put
  the overall height 31.55 on every component, the very thing it asked to avoid.
  Now a real `build` refuses it too, before anything is asked or touched, with the
  same lines as the dry run (`UnknownArgumentsOfStep`, near names included).
  Measured before turning it on: 265 plan steps of session 8cd843 (args to args8),
  none with an argument its schema does not declare; the bench calls the library,
  not the bridge, and is not touched by it.
- **Measured on SOLIDWORKS, scenario t28 on a copy of the clip drawing, bridge
  0.150.1 then 0.151.0**: «count» 3 on V1 gave 175 and 31.55 both times, now with
  `warning_free_components` naming 26P000010-1 and `warning_count_not_used` in the
  compact answer; `components` of the four mounted parts gave the vertical 15.6
  (15.598 from the probe's boxes), and an unknown name is refused with the eight
  names the view shows; a note of two lines `text_matches` false then true, and
  taken away by its text (refused before), the sheet 68 -> 67 and the views 7 -> 7.
- **Checks**: contract Zc2191 (pure rules `SameNoteText`, `NoteTextWarning`,
  `NotesDownBy`, `OverallComponents`, `OverallFreeWarning`, `CountNotUsedWarning`,
  `UnknownArgumentsOfStep`; `Warns` of the compact answer keeps the three new keys
  and drops the old one; 14 lines of wiring, 2 old lines kept out, the unknown
  arguments refused before the confirmation, each with its changed copy). Refusal
  floors 279 of 1010. Bench README: drawing cases 46 -> 48.

## 0.150.1 — 27 October 2026 · library 0.172.1 · what the replay of the round 7 fixes found (branch `night-2026-10-26-asm`)

- **The replay**: the 16 calls of round 7 that showed N23 to N28, run on SOLIDWORKS
  twice on fresh copies, with the shared bridge 0.149.0 and with 0.150.0
  (scenarios t26o and t26n). Before and after: N23 two chamfer dimensions of 0.4
  and -0.4 mm, then the overall size 175 x 31.55 mm (the same as `GetBox` of the
  eight components in the probe: 87.5 x 15.775 mm of sheet at 1:2); N24 undo with
  no reason and `delete_annotation` unknown, then the reason with its remedy and a
  dimension (2 -> 1) and a note (75 -> 74) taken away; N25 the « Kg» field marked
  `only_fixed_text`; N26 the part still not marked modified after `set_property`,
  then `unsaved_changes_after_write` false, marked, and `document list` saying
  unsaved; N28 no `property`, then `property` (empty) on the assembly.
- **N27 sat in the wrong place.** In report 8cd843 (call 009) `create_drawing`
  answered a default name and it was `standard_views` that renamed the drawing
  after the assembly, taking the title of the saved drawing already open; the
  replay gave a default name to `create_drawing` alone too. A warning in
  `create_drawing` only never fires on the case it was written for. Now
  `TellTitle` writes `document_title` and `warning_same_title` in `create_drawing`
  and in the three operations that put a model view on the sheet:
  `standard_views`, `model_view`, `flat_pattern_view`.
- **A note asked on the sheet went into a view.** After `drawing_dimension` the view
  it dimensioned stayed active, and `note` at 20, 250 mm landed inside it: notes of
  the views 7 -> 8, of the sheet 67 -> 67, and the answer and the log said «on the
  sheet». The new case `note_after_a_dimension_stays_on_the_sheet` was run first
  on 0.150.0 and was red (`notes_views_after 1 (expected 0)`). Now `note` calls
  `IDrawingDoc.EditSheet` before `InsertNote` (the guide, Remarks: «Subsequently
  created geometry resides on this drawing sheet.»), says `attached_to` (sheet,
  view or both, read from the two counters), and `warning_in_view` with the name,
  the counts and `delete_annotation` when it is not the sheet.
- **Checks**: contract Zc2190 gets the pure rules `NoteAttachedTo` and
  `NoteInViewWarning`, six more lines of wiring, `TellTitle` counted in 2 places
  of `DrawingOperations.cs`, and `EditSheet` before `InsertNote`, each with its
  changed copy that must turn red. Bench README: drawing cases 45 -> 46.

## 0.150.0 — 27 October 2026 · library 0.172.0 · seventh round of session 8cd843: N23 to N28 (branch `night-2026-10-26-asm`)

- **Seventh round on SOLIDWORKS (session 8cd843, bridge 0.149.0, 36 calls)**: a
  drawing of the assembly on a copy. Six new defects: one P1, two P2, three P3.
  The empty «Descrizione» column of the bill of materials is not one of them: the
  property to read is «Denominazione», and the template is changed by its owner.
- **N23 (P1)**: `drawing_dimension {view: V1, count: 3}` on the front view of the
  assembly placed 1.725, 15.23° and 159.25° and called them placed. Cause,
  measured with a read-only probe on a copy of the drawing: on a view of an
  ASSEMBLY `GetVisibleEntities2` hands over the edges of the components, and
  `GetCurveParams3` gives their ends in the coordinates of the PART. Projected
  with the view transform alone, every edge of a component placed away from the
  origin lands elsewhere on the sheet (26P000010-1, moved by [-60, 0, 50]: box
  60.25..68.75 mm, raw projection 90.25..98.75 mm). Composed with
  `IComponent2.Transform2` first, the extent of each of the 8 components equals
  its `GetBox`. Fixed in the three projections that read edges of a view
  (`ReadEdge`, `CircleOnSheet`, the segments of the text overlap) through
  `ToSheetOf`. A view of an assembly now takes its own path
  (`DimensionAssemblyView`): the overall size along the axes of the sheet,
  between the outermost straight edges, checked against what the view shows
  (`OverallEdgesProblem`), not repeated between views (`OverallKey`), and the
  value read back compared with the expected one; or the edges named by their
  index. The new argument `overall` (`both`, `horizontal`, `vertical`, `none`)
  says which. A chamfer dimension read back with an angle outside (0°, 90°) or a
  leg of zero or less is taken away and listed in `failed`
  (`ChamferValuesProblem`), and so is any dimension read back at zero or less.
  `inspect_part` takes `view_edges` (`{view, component, limit}`): the edges of a
  drawing view with their kind, length, component and position on the sheet, so
  a plan can name them.
- **The same cause elsewhere**: `center_marks` judged and placed the holes of an
  assembly view with `ModelToViewTransform` alone, and the datums, surface
  finishes and tolerance frames picked the side of the view among edges
  projected the same way. Both now go through `ToSheetOf`. The hole table and
  the flat pattern view read `ModelToViewTransform` alone too, but they refuse a
  view that does not show a part, so an assembly never reaches them.
- **N24 (P2)**: `undo` after a dimension on the drawing answered that no plan
  had built anything and proposed `delete_feature`, which does not take a
  dimension away. After a plan that worked on a drawing, the undo now says why
  it keeps no plan (`DrawingPlanMemoryReason`: SOLIDWORKS records no undo entry
  for it) and proposes the new operation `delete_annotation` (family drawing):
  it takes away one dimension by its name (`RD1@Drawing View1@...`, or what
  comes before an «@») or one note by its name or exact text, on the current
  sheet, and says «removed» only when nothing answers to the name any more.
  `note` now gives the `name` of the note it placed. 105 operations.
- **N25 (P3)**: the title block counted as shown the weight field, two sheet
  property links followed by «Kg», showing « Kg». A field that shows only
  the fixed text around its links (`ShowsOnlyFixedText`) is counted as empty and
  marked `only_fixed_text`.
- **N26 (P3)**: after `set_property` the document kept `unsaved_changes` false.
  The flag is read after the write; when SOLIDWORKS did not raise it, the bridge
  calls `SetSaveFlag` and reads it again (`unsaved_changes_after_write`,
  `unsaved_changes`, `warning_unsaved`).
- **N27 (P3)**: `create_drawing` made a drawing with the title of one already
  open, without a word. It now answers `document_title` and, when another open
  document has it, `warning_same_title` with the remedy (`save_document` in the
  same plan).
- **N28 (P3)**: `inspect_part` of an assembly gave no `property` key. It now
  always does (empty when the assembly has none), with
  `configuration_properties` when there are some.
- Contract `Zc2190DrawingRound7Contract`: the pure rules above by reflection,
  and the wiring on the sources, each line first turned red on a changed copy.

## 0.149.0 — 27 October 2026 · library 0.171.0 · sixth round of session 8cd843: N17 to N22 (branch `night-2026-10-26-asm`)

- **Sixth round on SOLIDWORKS (session 8cd843, bridge 0.148.1, 25 calls)**: held
  N16 (`marked_unsaved` on the first check of a saved copy, nothing on the
  second). Real work on copies: STEP of the assembly, a drawing with three views,
  an isometric view, the bill of materials, balloons, the saved drawing and its
  PDF, 7 steps in one plan. Six new defects, all P2.
- **N17**: a plan sent before any `inspect_part` was refused after 10.9 s, because
  the bridge first read the whole assembly in front (`session.state_read` 10728
  ms). The refusal now comes before the reading, when the plan names no
  document; the text is the same (`NotLookedAtYet`).
- **N18**: `export_step` with `reopen_and_compare` on an assembly was refused only
  when its step came, after `save_document` in the step before it had run. The
  pre-check of the plan now gives the refusal of the operation before any step
  (`SwOperations.AssemblyExchangeRefusal`, also for the IGES of an assembly),
  while the plan is still on the assembly it started on (a `create_drawing`
  before the step moves it elsewhere, and the check is left to the operation).
  `ExportExchange` keeps the two texts in its own `Failed`: moved into the
  helper, the count of refusals with a way forward stopped reading them (264
  of 995 against the floor of 266 of 997), so the two copies stay and a check
  turns red if they part.
- **N19**: the STEP of an assembly answered `solids_expected` 7 next to 8
  components, and nothing said why. The count is right (one part placed twice is
  written once); `solids_expected_basis` now says what it counts.
- **N20**: the bill of materials came with the column «Descrizione» empty on all
  7 rows and nothing said it. `warning_empty_columns` names the columns empty on
  every row (`EmptyColumnsWarning`).
- **N21**: the headers of the bill of materials read «<FONT size=3.5>Num. parte».
  The FONT codes of a note are taken out of every cell read from a table
  (`WithoutFormatCodes`, shared by the bill of materials and the cut list);
  nothing else is touched.
- **N22**: `export_pdf` answered `set_sheets_answer` false next to a right PDF. A
  false now comes with `set_sheets_note` (under 100 characters, so the summary
  keeps it): false is seen on complete PDFs too, the check is the page count.
- **Checks**: `Zc2114UndoHonestyContract` (N17 and N18 in the plan tool, 3 changed
  copies), `Zc2189WeldAssemblyContract` (N18 to N22 by reflection, the two
  copies of the N18 refusals compared, and on the sources with 5 changed
  copies), each turning red.

## 0.148.1 — 27 October 2026 · library 0.170.1 · fifth round of session 8cd843, N16 (branch `night-2026-10-26-asm`)

- **Fifth round on SOLIDWORKS (session 8cd843, bridge 0.148.0, 9 calls)**: held
  N13 («Parte356» without «title:»), N14 (pairs and boxes) and N15 (`contacts`
  1, total 14.763 mm³ with and without `touching_counts`); no new defect. From
  the boxes the session measured the squeeze of the clips on the channel: 0.05 mm
  per side, the same as its own script.
- **N16**: the session saw the saved copy of the assembly marked unsaved after a
  scenario of this side. Isolated on SOLIDWORKS, on copies saved just before:
  `activate`, `inspect_part` and `measure` of `mass_properties` leave the save
  flag false; `measure` of `interference_check` sets it. A read-only probe of the
  interference manager read `GetSaveFlag` after every phase: false after the
  three settings, the count, the list and the bodies, true right after
  `IInterferenceDetectionMgr.Done`. Done is still called (it stops the
  detection) and no call clears the flag, so the answer now says it:
  `marked_unsaved` true and `warning_marked_unsaved`, when the flag went from
  false to true across the check. The descriptions of `interference_check` and
  of the tool `measure` say it too.
- **Check** (`Zc2189WeldAssemblyContract`): the save flag read before and after
  Done, and the mark said, with a changed copy that turns red.

## 0.148.0 — 27 October 2026 · library 0.170.0 · fourth round of session 8cd843: N13, N14, N15 (branch `night-2026-10-26-asm`)

- **Fourth round on SOLIDWORKS (session 8cd843, bridge 0.147.0, 20 calls, the
  whole assembly mounted on copies and saved)**: held N7 (the family
  `documents`), N11 (`undo_possible` false and `note_undo`, then the undo
  refuses) and N12 (`steps_done` 2 of 6, `note_stopped`, then activate and undo
  2603.7 to 2610 mm³).
- **N15 (P1)**: with `touching_counts` a fifth interference read 368475 mm³ on
  parts of a few thousand. Measured with a probe on a copy of the assembly: the
  faces that touch come as a SHEET body (`IBody2.GetType` 1, one face, box
  175 × 0 × 3.825 mm) with `IInterference.Volume` 0, and `GetMassProperties` on
  a sheet answered 3.68475e-4 at index 3. A sheet is now a CONTACT: `contact`
  true, volume 0, counted in `how_many` and in a new `contacts`, never in
  `volume_total_mm3` nor in the alarm; the verdict says them apart («No part
  goes into another» when they are all there is). On the four solid ones
  `IInterference.Volume` and `GetMassProperties` index 3 agreed to the last
  digit.
- **N14**: four equal interferences of 3.691 mm³ did not say which clip touched
  which half. Each item of `interferences` now names its `components`
  (`IInterference.Components`) and its box in the space of the assembly
  (`box_from_mm`, `box_to_mm`, from `IBody2.GetBodyBox`).
- **N13**: the error of a stopped plan wrote «title:Parte354», the key the
  executor compares by. Its messages now print the name (`ReadableIdentity`).
- **Checks**: `Zc2189WeldAssemblyContract` (the contact sentence by reflection,
  the source order sheet before volume, pair and box, with changed copies that
  turn red); the bench case `touching_is_not_interpenetrating` expects
  `contacts` 1 and `volume_total_mm3` 0.

## 0.147.0 — 27 October 2026 · library 0.169.0 · third round of session 8cd843: N7, N11, N12 (branch `night-2026-10-26-asm`)

- **Third round on SOLIDWORKS (session 8cd843, bridge 0.146.3, 29 calls)**:
  held N10 (the first undo after a refused plan and its `note_refused_plan` say
  the same thing, the second withdraws the good plan) and N4 (after a plan with
  `parameters` the undo no longer says «not yet run any that built something»).
  `on_window_change=stop` stopped at step 3 after two cuts (2610 to 2603.7 mm³).
- **N7**: `save_document` saved an assembly well but sat in the family
  `drawing`. It is now in a family of its own, `documents` («the open document as
  a file»); the line of `drawing` says that a part is saved first with it.
  Checked in `CatalogTests`.
- **N11**: a plan with an extrusion and `parameters` answered
  `undo_entry_recorded` true, and the undo after it refused (rightly: no equation
  is in an undo entry). A plan whose memory is dropped although it changed
  something now says so in its own answer: `undo_possible` false and
  `note_undo` with the reason.
- **N12**: the stop for a window that came to the front said «I touched
  nothing» at step 3, after two cuts. The step now says «I did not run this step;
  the 2 step(s) before it are done», and a plan that stops after some steps
  answers `steps_done`, `steps_in_plan` and `note_stopped` (what stays, and
  whether «undo» withdraws it).
- **Contract** (`Zc2114UndoHonestyContract`): the after-plan check asks for
  `note_undo` and `note_stopped`, the executor check for the guard on the steps
  already run, each with a changed copy that must turn red.

## 0.146.3 — 27 October 2026 · library 0.168.2 · second round of session 8cd843, N10 corrected (branch `night-2026-10-26-asm`)

- **Second round on SOLIDWORKS (session 8cd843, bridge 0.146.2, 24 calls, the
  assembly mounted again on copies)**: held N1 (`confirmation` on delete_mate and
  on delete_feature), N2 (the readings before the steps of six blind cuts
  1716.7 ms against 5633 ms of the first round, the plan 7954 ms against
  13216 ms), N3 (the plan of six distance mates: TY, RX, RZ blocked on the
  fourth component, every mate «removes»), N5, N6, N8 (`expect_verdict` when
  held, `measurements` when missed), N9 (three progress notes per assembly undo)
  and R1 (a window brought in front during six cuts: `reactivations` with
  `brought_back` true, six cuts of six). Not tried: N4, N7,
  `on_window_change=stop`.
- **N10**: the undo of a refused plan answered `note` «The plan was withdrawn
  whole and the assembly went back to how it was» next to `note_refused_plan`
  «this undo took away only its undo entry», and nothing had changed (mates 9 and
  9). The note now depends on whether the plan was refused: «The plan was refused
  and had changed nothing: this undo took away its undo entry».
- **Contract** (`Zc2114UndoHonestyContract`): the note of a successful undo must
  depend on `memory.Refused`, with a changed copy that drops the condition.

## 0.146.2 — 27 October 2026 · library 0.168.2 · N2 measured, N3 reproduced and corrected (branch `night-2026-10-26-asm`)

- **N2 measured on SOLIDWORKS (scenario t17, 0.146.1 against 0.145.0, two runs
  each)**: the readings before the steps of a plan of six blind cuts went from
  2794.3 / 2782.0 ms to 1209.4 / 1204.4 ms (mean -56.7 %), the whole plan from
  9834 / 10011 ms to 8333 / 7835 ms (mean -18.5 %). A missed «expect» now keeps
  the numbers of the step (`measurements` with `removed_mm3` 3.14 next to
  «expected 99»).
- **N3 reproduced**: scenario t18b replays the calls of session 8cd843 (insertions,
  mates, undo, suppression, deletion, then the plan of six distance mates) and gave
  the same verdicts: on the fourth mate «after the move RZ and the rebuild it
  neither came back nor stayed where it was moved (0.218 degrees of 5)», on the
  fifth «RX ... 0.219 degrees», mate verdict «unknown» on both.
- **The cause**: the test judged a turn by the whole angle between the two
  rotations. A distance mate between two «up» faces puts the normal of the face
  back, and whatever turn is left is about that normal, a free direction (RY):
  0.218 degrees is θ²/2 of 5 degrees (0.08727² / 2 = 0.003808 rad). The rule now
  judges a turn by the turn about its own axis (`MateOperations.TurnAboutAxisDeg`:
  the rotation vector of Q = R'^T R, its component on the axis), as it judges a
  translation by the displacement along its own axis.
- **Contract** (`Zc14MateDirectionsContract`): a row «N3: RZ put back, turned
  0.218 degrees about Y» that must read blocked; the source check asks for
  TurnAboutAxisDeg and refuses AngleBetweenDeg in MovementOf, with a changed copy
  that puts the whole angle back. The rows of the probe (S3 slides, RY that stays
  turned, RZ half way) read as before.

## 0.146.1 — 27 October 2026 · library 0.168.1 · N2 measured: the reading without edges never happened (branch `night-2026-10-26-asm`)

- **Measured on SOLIDWORKS (scenario t17, 27/10/2026 07:03-07:06)**: six blind
  cuts on one face, then an undo, six more, a missed «expect», an undo, with the
  shared bridge 0.145.0 and with 0.146.0, twice each, alternated. Same times
  (six cuts 9834 / 10011 ms on 0.145.0, 9804 / 10541 ms on 0.146.0), and
  `state.edges` read 8 times per plan in both (2164.8 ms against 2143.1 ms).
- **The cause**: the bridge attaches `_signatures` to every step, the
  signatures of everything the last reading listed, edges included, so that a
  uid the step cites can be found again. `MentionsEdges` looked into it and found
  an «E» identifier in every step. It no longer looks into `_signatures`: an
  edge the step cites is in its own arguments.
- **Contract**: one more row of `MentionsEdges` (a face with the signatures the
  bridge attaches: no mention), the row that would have caught it.
- **Seen in the same measure**: `expect_verdict` in the answer («held:
  removed_mm3 3.14 (expected 3.1416 ±0.01)», N8 works); the gear of 17 teeth
  (scenario t16) still takes 15.3 s in `spur_gear` on library 0.168.0, so reading
  the sketch points once (0.167.1) was not where its time goes.

## 0.146.0 — 27 October 2026 · library 0.168.0 · the report of session 8cd843 on the reassembly of 26M000002 (branch `night-2026-10-26-asm`)

The session that wrote the brief of 25/10 reassembled its assembly on copies with
bridge 0.145.0 (70 calls; the report is in the private notes of the project): R3, R4, R5,
R8, R10/R11, R12/R21, R13, R15, R16, R17, R18, R19, R23, R24, §5.1, §5.2 and the
blind cut verified; mate 30.3 s → 2.2 s, save 134 s → 2.4 s. What it found:

- **N1 — `delete_mate` ran without a confirmation and the answer did not say so.**
  With a client that does not declare «elicitation» the plan runs as before (W2.5);
  the answer now carries `confirmation` («Nobody was asked to confirm delete_mate:
  ... tell the person what this plan deleted»). The descriptions of `delete_mate`
  and `delete_feature` say both cases.
- **N2 — the reading before every step read the edges too.** Six cuts on a part of
  43 edges: `plan.state_read_step` 5632.7 ms of 13215.5 ms, the edges about 563 ms
  of every reading. A step that names no edge (`PlanExecutor.MentionsEdges`:
  «edge» in any key or text, or an «E» identifier, anywhere in the arguments) is
  now read with `DocumentState.ReadWithoutEdges`: the faces as before, no edge
  listed. The time saved is NOT measured yet.
- **N4 — the opening of the refusal of `undo` after `parameters`**: already
  corrected in 0.145.1.
- **N5 — a refused plan that left an undo entry became the memory and hid the plan
  before it.** A `place_component` refused because the mates moved the component
  (set back, nothing visible changed) closed its entry with something in it: the
  next `undo` took away that empty entry, said «withdrawn», and the plan before
  could no longer be withdrawn. The refused plan still becomes the memory (its
  entry is on top), but it keeps the plan before it: the answer of the refused plan
  says so in `note_undo_entry`, the `undo` that takes its entry away says so in
  `note_refused_plan`, and a second `undo` withdraws the plan before, with all the
  usual checks.
- **N6 — the log said «going ahead» for a plan then refused as a resend**, and the
  refusal of a resend spoke of «a SECOND hole» on a mate. The log line is written
  right before the plan runs; the refusal says «the same work a SECOND time (a
  second hole, a second mate)».
- **N7 — `save_document` said «part or drawing»**: it saves assemblies too, and the
  description now says why it sits among the drawing operations.
- **N8 — a kept `expect` was invisible, and a missed one lost its numbers.** A step
  whose expectations hold carries `expect_verdict` («held: removed_mm3 7.07
  (expected 7.07 ±0.01)», `StepExpectations.Held`); a step that missed keeps its
  `measurements` in the outcome (they were null next to «measured true»).
- **N9 — `undo` sent no progress** (8.6 s in silence): three notifications now,
  reading, pressing, reading back.
- **Contract checks**: `R16StepExpectationsContract` (Held, seven rows of
  MentionsEdges, the executor uses both), `ConfirmationTests` (the field, and the
  log line after the refusal of a resend and before the plan runs).
- **Not in this version: N3** (the test of the directions of a component read
  «unknown» after a mate equal to one read blocked): it needs the assembly on
  SOLIDWORKS to be reproduced.

## 0.145.3 — 27 October 2026 · library 0.167.1 · the bench says whether a call that does not come back is slow or hung (branch `night-2026-10-26-asm`)

- **Why.** The full bench of 27/10 stopped at 94 s on «spur_gear» after almost
  four hours; alone, right after, the same call took 19.8 s. The run said which
  call it was and nothing else: not whether SOLIDWORKS was still working on it,
  not whether a window had opened, not how long the other calls had lasted.
- **When the leash stops a run** (`Bench/Program.cs`, guardian), it now reads the
  processor time of SLDWORKS twice five seconds apart (busy: the call is slow;
  idle: it waits for a window, a lock, the network), says whether SOLIDWORKS
  responds and lists its visible windows by class and title (a modal shows up
  there), and writes the ten slowest calls of the run with the time of day.
- **Every operation and every new part is timed**; a whole run writes the ten
  slowest calls before the summary.
- **`--leash-ms <n>`** (at least 1000) sets the leash of one run, to see the
  guardian fire on the real bench with one small `--group`; the default stays
  90 s. `Bench/README.md` no longer says that entry K18 is not done.
- **Contract checks** (`Tests/LeashTests.cs`): the guardian reads the state of
  SOLIDWORKS and the slowest calls, every operation and NewDocument are timed,
  and `--leash-ms` exists.

## 0.145.2 — 27 October 2026 · library 0.167.1 · the number of operations in the refusal of an unknown tool (branch `night-2026-10-26-asm`)

- **The refusal of a tool that does not exist said «The sixty drawing
  OPERATIONS are not tools»** with 104 operations in the catalog. Seen while
  driving the bridge from a script (a call with a wrong tool name, 27/10/2026).
  The number now comes from the catalog (`Catalog.HowMany`), and is left out when
  the catalog did not load.
- **Contract check** (`Tests/ProtocolTests.cs`): the refusal names the number of
  operations that the «total» of `list_operations` gives.

## 0.145.1 — 27 October 2026 · library 0.167.1 · what the first full bench after the brief found (branch `night-2026-10-26-asm`)

- **The first full bench since 9/10/2026** (commit bdc70c6, library 0.167.0,
  13827 s, SOLIDWORKS restarted before it): 1881 checks passed, 2 red, and the run
  stopped as INVALID at `the_seventeen_tooth_gear_can_be_made`: «spur_gear» had
  not answered after 94 s (threshold 90). Alone on the same SOLIDWORKS, right
  after, the same wheel took 19.8 s, was right (8808.6 mm3, 170 segments) and one
  undo withdrew it: the stop came from a SOLIDWORKS session that had made 340 parts
  in four hours, not from the bridge.
- **Red 1, a check older than brief R18.** `document: after a document change
  «build» insists on looking first` sent an extrusion right after `document`
  «create» and asked the refusal «not looked at any part yet». Since 0.139.0 a plan
  that cites no identifier runs without a reading, so the extrusion ran. The check
  now sends a plan that cites «F1» and asks the refusal that names it and
  «inspect_part», with the part still empty.
- **Red 2, a case that numbered the components by insertion.**
  `a_face_is_named_by_a_point` called them «shaft» and «pin» and expected C1 to be
  the shaft; the state numbers components by NAME, so C1 was the pin and [10, 0, 0]
  fell 26.926 mm from it (its origin at [40, 10, 0]: radially 30 - 5 = 25, and 10
  below its bottom). The shaft is called «axle» now.
- **The opening of the refusal of `undo`** said «here I have not yet run any that
  built something» also when the last plan had built and the memory was dropped
  with a reason (t14: the equations); it now says that no memory of the last plan
  is kept, for the reason that follows.
- **The consumed string of the document check is bound again**
  (`Checks/language/consumed-strings.json`, `bench-client-not-looked-yet`): it
  pointed at the old literal of `Bench/ClientPort.cs` and the contracts said so; it
  now points at «inspect_part» on line 261, produced by `Tools/McpTools.cs`. 39
  entries, 0 broken.
- **Library 0.167.1: the points of a sketch are read once.** `VerticesNotDrawn`
  compared every vertex of a profile with every point of the sketch by reading
  `ISketchPoint.X` and `.Y` again each time: two calls out of process per
  comparison, tens of thousands for a wheel of 17 teeth. It now reads the points
  into a list once and compares numbers. Whether this was the slowness of
  «spur_gear» was NOT measured: it is the next measure on SOLIDWORKS (scenario t16).

## 0.145.0 — 27 October 2026 · library 0.167.0 · undo after «parameters» and «place_component» (scenarios t9b, t14; probe `Checks\UndoGaps`; branch `night-2026-10-26-asm`)

- **Seen on SOLIDWORKS (t14, t9b, 27/10/2026).** After `parameters` (two
  variables) the plan answered `"undo_entry_recorded": false`, and `undo`
  withdrew the extrusion of the plan BEFORE it (96000 -> 0 mm3) while the
  equations stayed, and answered that «the plan was withdrawn whole». After
  `place_component` the plan answered false too, and `undo` refused.
- **Measured (`Checks\UndoGaps`, new probe, two runs on 27/10/2026).**
  - Equations written by `IEquationMgr.Add3` and `EvaluateAll` inside the
    recording: `FinishRecordingUndoObject` false with `EditRebuild3`,
    `ForceRebuild3(false)` or no rebuild; one undo took away the feature before
    them (features 19 -> 18) and the two equations stayed.
  - A component moved by setting `IComponent2.Transform2`, or by
    `SetTransformAndSolve2`: false, and one undo took away the component inserted
    before it.
  - The same move by `IDragOperator` (absolute transform, `AddComponent`,
    `BeginDrag`, `Drag`, `EndDrag`): true, and one undo put the component back
    (x 200 -> 90 mm); with `TransformType` 2 and a turn of 180 degrees about X as
    well: true, and the undo put back the place and the axes.
- **`place_component` drags the component** (`DragTo`) instead of setting
  `Transform2`. The direction test of `mate` still sets `Transform2` and puts it
  back: the mate itself is recorded, and its undo held on t9b.
- **The memory of `undo` sees the equations.** The snapshot carries the equations
  of the document; `WhatDiffers` names a change of them; `MemoryAfterPlan` has a
  rule 3b: a plan that changed the equations is not remembered, whatever else it
  did, and the reason says that SOLIDWORKS records no equation in an undo entry and
  that a variable goes back with `parameters` and the value it had.
  `parameters` answers `equation_before` for a variable it rewrote.
- **R10 measured, nothing to fix (t13).** `cylindrical_cut` and `rectangular_cut`
  keep `AddToDB` true, and one undo withdrew a block with two holes (116732.7 -> 0
  mm3) and a block with two pockets (114000 -> 0 mm3); two revolves (81681.4 -> 0
  mm3) with the recipe of 0.143.0.
- **A depth given alone makes the cut blind (scenario t15).** A `cylindrical_cut`
  with `depth_mm` 5 and no `through_all` drilled through the 20 mm block (1570.8
  mm3 removed): `through_all` defaulted to true and the depth was dropped without a
  word. `cylindrical_cut`, `slot` and `hole_wizard` now default `through_all` to
  false when `depth_mm` is given (`SwOperations.ThroughAllAsked`); `through_all`
  true written beside a depth still wins, as the catalog said and as the pair case
  `limit_pocket_that_covers_everything` sends. The catalog says it. Bench case
  `a_depth_alone_makes_the_hole_blind` (features: 38). Not yet run on the bench.
- **Identifiers of an older reading (brief R18, second half), measured (t15).**
  Block 100 x 60 x 20, read once: F1 the top face (6000 mm2, y = 20), F2 the
  bottom one, of the same area. A blind hole on F1, then a second plan citing F1 of
  that same reading: after the first hole the top face is the smaller (5921.46
  mm2), so by position F1 would be the bottom face; the second hole went into the
  top face all the same (both bottoms at y = 15 mm, 120000 -> 119607.3 -> 119214.6
  mm3). With the answer «summary» the signatures of the last reading stay, and the
  face is found again by its persistent reference. No change to the code.
- **Contracts.** Zc2114: three situations of the memory with equations, and
  `WhatDiffers` sees an equation added and one rewritten (9 pairs).
- **The reason of the equations** says that a variable or a link the plan
  created stays, since no operation removes an equation yet.

## 0.144.0 — 27 October 2026 · library 0.166.0 · «extrude_regions»: several heights of one plane in one step (brief §5.2; branch `night-2026-10-26-asm`)

- **Seen on 24/10/2026.** Each half of a channel took six `extrude_profile`, one
  per height, and every step paid its own reading, confirmation and rebuild
  (brief R2).
- **`extrude_regions`, new operation.** It takes `plane` and `regions`, each
  region with its `contours` (the form of `extrude_profile`) and its `height_mm`.
  Every region is checked before the first is drawn (`SwOperations.Regions`,
  static, without SOLIDWORKS); then each one is drawn by the executor of
  `extrude_profile`, one sketch and one extrusion, merged with the bodies it
  touches. The answer lists, for each region, the feature, the height and
  `depth_dimension`, the name of the dimension of its height read from the
  feature (the dimension whose value is the height asked, named as the driven
  vertices name theirs), ready for `link_dimension` or `parameters`. A region that
  fails after others were drawn stops the step and names the ones in the part.
- **Bench case `three_heights_in_one_step`.** Two plates side by side at 10 and
  20 mm and a square at 30 mm on the first: 68000 mm3, one body; the second
  height moved to 25 mm by the name the answer gave: 78000 mm3; a height of zero
  refused with nothing drawn. Not yet run on the bench.
- **Checks.** Examples 35 (floor 34); refusals with a way forward 266 of 997
  (the failure of a region after others names them and says to undo the step);
  the written decision for the required field `extrude_regions.regions`; README
  104 operations; the bench README counts 12 extrusion cases.

## 0.143.0 — 27 October 2026 · library 0.165.0 · «parameters» in one step, an empty assembly reads empty, revolve keeps its undo (briefs §5.1, R11, R23; branch `night-2026-10-26-asm`)

- **`parameters`, new operation (brief §5.1).** On 24/10/2026 a part took five
  `global_variable` and six `link_dimension`, eleven steps, each with its own
  evaluation and rebuild. `parameters` takes `variables` (name: mm or an
  expression) and `links` (dimension: expression) and writes them in one step:
  every name, value and dimension is checked before anything is written; the
  variables are written first, so that a link can use a variable of the same step;
  one `EvaluateAll` and one rebuild; every value is read back. If a value does not
  read back as asked, every equation written is put back as it was and the refusal
  says so. Bench case `variables_and_links_in_one_step` (96000 -> 115200 -> 144000
  mm3, and a refusal that touches nothing). Not yet run on the bench.
- **An empty assembly read «did not answer» (scenario t9b, 27/10/2026).**
  `GetComponents(false)` answers null on an assembly with no component; the state
  of the assembly wrote «the assembly did not answer the list of components», and
  the undo that had just emptied the assembly said it could not read it.
  `AssemblyState` now asks `GetComponentCount(false)` when the list is null: 0 is
  an empty list (`components_top_level` 0), anything else is still a reading that
  failed.
- **The resent-plan guard was blind on an assembly (scenario t9b).** Its summary
  held volume, bodies, features and title, which read the same on every assembly:
  a plan inserted, withdrawn with `undo` and sent again was refused as «already
  run». On an assembly the summary now carries the components and mates as the undo
  snapshot reads them.
- **`revolve` closed an empty undo entry (scenario t13, 27/10/2026).** Two
  revolves answered `"undo_entry_recorded": false` and the undo pressed after them
  took nothing away. `revolve` now draws its profile with the recipe of
  `extrude_profile` (brief R11): no `AddToDB`, automatic relations and inference off
  while drawing and put back after, `ISketch.MergePoints`, a vertex drawn elsewhere
  refused (`VerticesNotDrawn`), and the sketch locked while it is still open
  (`LockOpenSketch`). `LockSketch`, whose last caller was `revolve`, is gone. Not
  yet measured on SOLIDWORKS with the new code.
- **A plan that built while its undo entry closed empty is not remembered.** The
  memory of `undo` (rule 4) remembered a plan as soon as it built; when its own
  entry closed empty (`FinishRecordingUndoObject` false) one undo does not withdraw
  it and could take away something older. Now: no memory, and the reason says why.
- **Bench.** `Expectations.Resolve` resolves keys that start with «@» or carry
  «{», so that `links` can name a dimension with a bench selector.
- **Checks.** Refusals with a way forward 265 of 996 (floor and share raised from
  259 of 978); examples 34 (floor 33); README 103 operations; the frozen undecided
  call `SwOperations.cs|AddRelation` is gone with `LockSketch`; the mutation «the
  sketch to lock found with no names» is gone with it; `Checks/AssemblyUndo/` is in
  the snapshot register of the language gate.

## 0.142.0 — 27 October 2026 · library 0.164.0 · undo withdraws a plan on an assembly (brief R23; branch `night-2026-10-26-asm`)

- **Seen on SOLIDWORKS (t9, 26/10/2026).** On an assembly every plan answered
  `"undo_entry_recorded": false`, and `undo` answered that it had no plan to
  withdraw. Two causes, both measured.
- **The rebuild emptied the undo.** `Checks\AssemblyUndo` (new probe) inserts two
  components and makes a coincident mate inside one recording, a rebuild after each
  step, then presses undo; on SOLIDWORKS, 26/10/2026:
  - `EditRebuild3`, `ForceRebuild3(false)` or `ForceRebuild3(true)`:
    `FinishRecordingUndoObject` answered false and two undos took nothing away, not
    even the native entries;
  - `IModelDocExtension.Rebuild` (`swRebuildAll`, `swUpdateMates`,
    `swUpdateDirtyOnly`) or no rebuild: it answered true and one undo emptied the
    assembly;
  - in every way the mate moved the component (B from x = 90 to x = −20 mm).

  `SwOperations.RebuildKeepingUndo` rebuilds an assembly with
  `IModelDocExtension.Rebuild(swRebuildAll)` and a part or a drawing with
  `EditRebuild3` as before. The plan executor and the assembly operations
  (insert, mate, mate state, mirror, fix, replace) go through it, and so do the
  direction test of `mate`, the read-back of `place_component`, the rebuild after
  an activation that asks for one (`DocumentByPath.RebuildIfActivationAsked`, which
  the plan executor uses when it brings the plan's document back to the front) and
  the same rebuild of `document` «activate». `RebuildKeepingUndo` is public, so
  that the bridge can call it.
- **The memory of `undo` was blind on an assembly.** The state of an assembly
  writes neither a volume nor features, so both read 0 before and after every plan
  (ZERO-E-ZERO): a plan was never «built», and with the first cause fixed `undo`
  would have pressed without seeing a component moved by hand after the plan. The
  snapshot of `undo` now reads, on an assembly, the components (which ones, file,
  configuration, state, fixed, visible, transparent, origin within 0.0015 mm, axes
  within 0.00015) and the mates (which ones, kind, suppressed, the components they
  hold, by name, so that renumbered uids do not read as a change). It is used in
  the three places that compare: whether the plan built, whether the assembly
  changed since the plan (refusal before pressing), and whether the press brought
  it back as before the plan (and what a press took away). Without the two lists
  `undo` stops, as it does without a volume, and never redoes blindly.
- **The answers on an assembly** speak of components and mates
  (`AnswerAfterWrongPressOnAssembly`, `RefusalChangedSinceThePlanOnAssemblyText`),
  and the remedy names `delete_mate`, `suppress_mate` and `place_component`: no
  operation of the catalog takes a component away, so a component a plan inserted
  goes only by hand. A successful undo answers `components_before/after` and
  `mates_before/after` instead of the volume.
- **Contract `Zc2114UndoHonestyContract`:** 13 pairs of assembly readings
  (moved, turned, added with uids renumbered, taken away, mate added or suppressed,
  an unreadable origin or list), the answers and the refusal on an assembly, and
  the two guards of `undo` and the check of `hasBuilt` seen red on changed copies.
- **Brief R5, the rest.** `extrude_profile` with `dimensions` «driven» gives a vertex
  on an axis of the sketch no dimension (SOLIDWORKS makes none from the origin: a
  relation holds it, probe `Checks\DrivenSketch`), so that coordinate cannot be driven.
  `vertex_dimensions` said «on_axis»; now `warning_dimensions` also says how many
  coordinates sit on an axis, that no variable can move them off it, and to draw the
  vertex off the axis to drive it. The description of the operation says the same.
- **Not measured yet:** the undo of an assembly plan through the bridge (scenario
  t9b); a component whose origin the state cannot read makes `undo` refuse on
  that assembly (a refusal, the lesser error); the rebuild of a drawing stays
  `EditRebuild3`, and the undo of a plan on a drawing was never measured.

## 0.141.2 — 26 October 2026 · library 0.163.2 · save_document names the open document that holds the file (branch `night-2026-10-26-asm`)

- **Seen on SOLIDWORKS.** t10 and t11 run twice in the same session: the second run
  saved a new part onto `M26U10C.SLDPRT` and `M26S11.SLDPRT`, which the first run had
  left open, and the answer was only «The document was not saved (code 1:
  swGenericSaveError).».
- **`save_document`**, after a refused save onto a file that is not the document's own,
  asks SOLIDWORKS whether another open document holds that path
  (`ISldWorks.GetOpenDocumentByName`) and, if so, names it and says what to do: close
  it with «document» «close» and save again, or give another «path». It is looked at
  only after the refusal, so it never stops a save that would go through.
## 0.141.1 — 26 October 2026 · library 0.163.1 · a profile is drawn with snapping off (brief R11; branch `night-2026-10-26-asm`)

- **What 0.141.0 broke, on SOLIDWORKS.** The t11 case of R16 and R18 with the right
  `expect` (a star of 20 vertices on the top plane) was refused: «6 vertex(es) of the profile are not where they were asked, the first at
  (11.76; 16.18) mm». With `AddToDB` off, sketch inference (snapping) puts a vertex on
  a point nearby, and its distance is in pixels, so it follows the zoom: the probe's
  L of 6 vertices never showed it. The check of 0.141.0 (`VerticesNotDrawn`) caught it
  instead of giving another shape.
- **Measured.** `Checks\UndoSplit` takes two more flags: `star` (the star of t11 as the
  first profile) and `merge` (`ISketch.MergePoints(1e-7)` after drawing), and prints
  the vertices that are not where they were asked. On SOLIDWORKS, 26/10/2026:
  - `open+nodb+norel+star` (the way of 0.141.0): 20 points, 6 vertices moved, volume
    26243.214 mm³ instead of 26720.964;
  - `open+nodb+noinf+star`: 40 points for 20 vertices, every vertex where asked;
  - `open+nodb+noinf+merge+star`: `MergePoints` 40 → 20 points, every vertex where
    asked, volume 26720.964 mm³, one undo took the whole plan away;
  - `open+nodb+noinf+merge` and `none+nodb+noinf+merge` on the L: 12 → 6 points,
    volume 10000 mm³, the whole plan undone.
- **`extrude_profile`** turns sketch inference off too while the lines are drawn (and
  sets it back), then joins the ends with `ISketch.MergePoints(1e-7)`. Every vertex is
  still read back; the refusal now says «it was drawn on a point nearby».
- **Not changed:** `revolve`, `cylindrical_cut` and `rectangular_cut` still set
  `AddToDB` (brief R10); whether their undo holds is not measured yet.
## 0.141.0 — 26 October 2026 · library 0.163.0 · one undo withdraws a profile again (brief R11; branch `night-2026-10-26-asm`)

- **The cause, measured.** `Checks\UndoSplit` now takes a lock (`none`, `after`,
  `open`) and flags joined by `+` (`nodb` leaves `AddToDB` alone, `noinf` turns sketch
  inference off, `norel` turns the automatic relations off), and prints the points and
  relations of each sketch. On SOLIDWORKS, 26/10/2026, two profiles in one recorded
  entry, then one undo:
  - `AddToDB` true, any lock: the first extrusion stayed. The API help says why: with
    it «It is not possible to undo the creation of some types of sketch entities»
    (`ISketchManager~AddToDB.html`);
  - lock `after` (EditSketch and a rebuild after the extrusion):
    `FinishRecordingUndoObject` answered false and the undo took nothing away;
  - `nodb`: the whole plan went, but one automatic relation per line;
  - `nodb+noinf`: the whole plan went, but 12 points for 6 vertices (the ends of the
    lines not joined);
  - `nodb+norel`, lock `none` or `open`: 6 points for 6 vertices, 0 relations, the
    whole plan went.
- **`extrude_profile` follows the last way.** No `AddToDB`; the automatic relations
  are off while the lines are drawn and set back after; the FIXED relations go on
  while the sketch is still open (`LockOpenSketch`, a typed `SketchSegment[]`), not
  after the extrusion. Snapping stays on, so every vertex of the straight strokes is
  read back (`VerticesNotDrawn`, a ten-thousandth of a mm): one that is not where it
  was asked fails the step, because a vertex drawn wrong gives no error, it gives
  another shape.
- **Not changed:** `cylindrical_cut` and `rectangular_cut` still set `AddToDB` (brief
  R10); whether their undo holds is not measured yet.

## 0.140.2 — 26 October 2026 · library 0.162.1 · the volume an «expect» asks for is read from the part (brief R16; branch `night-2026-10-26-asm`)

- **First run of this branch on SOLIDWORKS** (26/10/2026, bridge 0.140.1, scenarios
  t9 to t12 and the probes `DrivenSketch` and `UndoSplit`). What held: placing and
  mating components, the side of a distance mate, a face named by a point (R12, R21);
  the `edges` filter (R17); the `scope` of properties (R8); a wrong `expect` stops the
  plan (R16); a profile driven by its vertices, linked to a global variable, gives the
  volumes computed beforehand, 16,500 and 17,000 mm³ (R5). What did not: the three
  points below, and two more still open, R11 (one undo of a profile plus a chamfer
  takes away only the chamfer, in the three lock modes of the probe: locking the
  sketch is not the cause) and R23 (on an assembly `FinishRecordingUndoObject`
  answers false after every plan, and the memory of a plan is never made there).
- **R16: `expect` `volume_mm3` on a step that measures no volume.** `extrude_profile`
  answered «this step does not measure it», and only after the step had run, so the
  plan stopped with the body made. The volume of the part is the same for every
  operation: `PlanExecutor.WithVolumeIfAsked` reads it once, from the part, when the
  expectation asks for `volume_mm3` or `volume_after_mm3` and the step did not give
  it. When it cannot be read, the miss says so as before. `SwOperations.VolumeMm3`
  is now internal.
- **R5, seen on the probe.** A vertex with a zero coordinate gets no dimension from
  the origin: it gets a vertical or horizontal relation with it, so that coordinate
  cannot be driven later. 10 dimensions out of 12 on the L profile of the probe.
  Not changed yet.

## 0.140.1 — 26 October 2026 · library 0.162.0 · ten calls at once, and the probe of the split undo (briefs R14 and R11; branch `night-2026-10-26-asm`)

- **R14, ten calls at once.** On 25/10/2026 six `api_search` sent together got one
  «Connection closed», and the same call alone answered. `R14ParallelCallsContract`
  writes ten `tools/call` on the stdin of the bridge without waiting (six
  `api_search`, two `list_operations`, two `describe_operation`) and wants ten
  answers, one per id, every line on stdout whole JSON, and the bridge alive after.
  Green on this branch. The bridge is started with `OCE_NO_ATTACH=1`: the check must
  not touch a SOLIDWORKS session that is open. The crash of 25/10 itself (an
  SEHException in `ApiCatalog.Search`, the executable started from a network drive)
  is not reproduced by it.
- **R11, a probe: `Checks\UndoSplit`, not run yet.** On 25/10/2026 one undo of a plan
  of 11 steps took away seven features of eight. The probe makes the same two
  extrusions inside one undo recording three ways — no lock, the lock of the library
  (editing the sketch again after the extrusion), the FIXED relations added while
  the sketch is still open with the typed array — and presses ONE undo after each: it
  says whether the lock splits the entry (hypothesis H1) and whether the lock can move
  into the open sketch. Each way works on a new part closed without saving.

Contract suite: 886 checks, 4 failed — the 4 of the bench, not run on this branch yet.

## 0.140.0 — 26 October 2026 · library 0.162.0 · a profile driven by its vertices: «dimensions» of extrude_profile (brief R5; branch `night-2026-10-26-asm`)

On 24/10/2026 every `extrude_profile` came back with `sketch_locked` true and
twelve FIXED relations: the only dimension a variable could drive was the depth.
The width of a rib, the position of a slot, the inside of a clamp could not be tied
to a name.

- **`extrude_profile` takes `dimensions`**: `fixed` (the default, unchanged: the
  sketch locked where it is) or `driven`. Driven, every vertex gets a horizontal and
  a vertical dimension from the origin, put while the sketch is still open (as
  `cylindrical_cut` puts its own), and the sketch is not locked: its dimensions hold
  it. A circle gets the two dimensions of its centre and the one of its size
  (`diameter` or `radius`, whichever SOLIDWORKS made).
- **`vertex_dimensions`** lists, for each vertex, `x` and `y`: the name of the
  dimension for an equation (`D3@Sketch1`, ready for `link_dimension`), or `on_axis`
  when the vertex sits on the axis and a relation keeps it there (a dimension of zero
  has no side). A dimension is a distance: a vertex at x = -30 has a dimension of 30
  and `x_side` `negative`; driving it keeps the vertex on its side.
- **Every dimension is read back** against the coordinate it was asked for; one that
  measures from another point (a sketch whose origin is not the origin of the
  contours) is said under `x_problem` / `y_problem` instead of named, and
  `warning_dimensions` counts what could not be dimensioned. `sketch_fully_defined`
  is read on the sketch before it is closed.
- Straight contours and circles only (a curve has no vertex to hold it), at most 100
  vertices (provisional: the probe gives the time). Refused before anything is drawn,
  with what to pass.
- `swInputDimValOnCreate` is switched off around the dimensions and put back.

**Not measured yet**, and written as such in the code: how the sketch origin is
selected from outside the process (first the point of the sketch of the origin
feature, then the sketch point at 0,0,0 by position) and whether the horizontal and
vertical dimension calls give a sketch point against it. The probe
`Checks\DrivenSketch` (0.139.0) measures both; the bench case
`extrusions/a_profile_is_driven_by_its_vertices` (new) is the judge: an L with three
vertices on the negative side and two on the Y axis, ten dimensions and two
relations, three vertices moved by `set_dimension` against the volumes of the
shoelace formula (15000, 15750, 16500, 18500 mm³, computed by script).

**Tests.** `R5DrivenProfileContract`: the rule (fixed and driven taken, another word,
a curve and one vertex too many refused naming «fixed»), the limit said in the
catalogue equal to the one in the code, and the five places of the source (the
dimensions before the sketch is closed, a driven sketch not locked, the value box
switched off, the argument in the catalogue). Contract suite: 885 checks, 4 failed —
the 4 of the bench, not run on this branch yet. **Not tried on SOLIDWORKS yet.**

## 0.139.0 — 26 October 2026 · library 0.161.0 · a plan that cites nothing of the part needs no reading (brief R18, first half; branch `night-2026-10-26-asm`)

On 24/10/2026 a session called `document` and `inspect_part` 19 times for 16
plans. After every `document` the bridge forgot which document was in front,
although it had just put it there and written its title in the answer; and after
a change made by hand it stopped every plan, also one that cites no face.

- **`document` keeps the document it put in front.** After `create`, `open` and
  `activate`, when the card says the document is in front, `build` takes it as the
  document looked at. A plan that cites **no identifier** (a sketch on a plane, an
  extrusion, a property) runs on it without `inspect_part`; `next_step` says so.
- **A plan that cites an identifier is still sent to `inspect_part`**, with the
  identifier named in the refusal: identifiers are born from a reading and hold for
  it. Cited means any text under a key ending in `_uid` or `_uids`, or a text with
  the shape of an identifier the state hands out (one of C, E, F, M, S, V and a
  number: `F3`, `C2`, `M1`) under a key that names what identifiers name
  (component, mate, face, edge, body, sketch, feature, axis, plane, view, vertex,
  entity, reference, target, and their plurals, compared word by word: `material`
  is not `mate`); under any other key it is a value (`S235` in `value_text` is a
  steel). `*_ref` cites a step of the plan and is not an
  identifier; the keys of a size or a thread are skipped (`M6` there is a screw).
- **A change by hand stops only a plan that cites an identifier** (entry X2). A plan
  that cites none runs on the part as it is now, and the answer carries
  `warning_part_changed` with what changed.
- A plan refused further down does not turn a document that was not read into one
  that was.

Not done: stable identifiers from the persistent reference (the second half of R18).

**A probe for brief R5, not run yet.** `Checks\DrivenSketch` (built by
`Checks\build-checks.ps1`) measures what the driven dimensions of `extrude_profile`
need before the library writes them: how the sketch origin is selected from outside
the process in any language (by position, or through the sketch of the origin
feature), whether `AddHorizontalDimension2` / `AddVerticalDimension2` give the
coordinates of a sketch point against it, and what a driven value does afterwards
(a vertex on the negative side, two vertices moved together, a dimension of zero),
against the volume the shoelace formula gives. Each run works on a new part and
closes it without saving.

**Tests.** `R18DocumentWithoutReadingContract`: twelve plans read for the identifier
they cite (a rectangle on top, a property, a fillet by step reference, an M6 hole by
size, an edge, a feature, a mate, a face in the second step, an empty uid, a steel
named C45 — «material» is not «mate» —, faces in a list, a face given as plane),
and the six places of the source that use the rule. Contract suite: 882 checks, 4
failed — the 4 of the bench, not run on this branch yet. **Not tried on SOLIDWORKS
yet.**

## 0.138.0 — 26 October 2026 · library 0.161.0 · which edges the state lists: «edges» of inspect_part (brief R17; branch `night-2026-10-26-asm`)

On 25/10, on 26P000007, the edges to fillet were picked by hand among the thirty
longest the state lists (E3, E5, E13, E16, E17, E20, E29, E30), reading lengths
and points one by one; an edge shorter than the thirtieth had no identifier at
all. The faces had a filter since W3.1-bis.12; the edges had none.

- **`inspect_part` takes `edges`**, the twin of `faces`: `along` (x, y or z: a
  straight edge along that axis), `curved` (true or false), `length_min_mm` /
  `length_max_mm`, `near_mm` + `within_mm` (the distance to the EDGE, from
  `IEdge.GetClosestPointOn`), `face` (a face identifier: the edges that bound it),
  `body`, `limit` (default 30, at most 500). Every condition must hold. The two
  filters can be given together. The state then says `edges_total`,
  `edges_matched`, `edges_not_listed` and echoes `edges_filter`.
- **The identifier of an edge is its rank among ALL the edges** of the part,
  longest first, whatever is listed: the default reading lists E1..E30 as
  before, a filtered one lists the matching edges with the same numbers.
- **A plan can cite E45.** The state keeps the whole ranking, and
  `TryGetSignature` builds the signature of an edge it did not list the first
  time a plan cites it; the two places of the library that resolve an edge ask it
  (they read only the dictionary of the listed ones before). The check of the
  identifiers before a plan accepts an edge up to `edges_total`, as it accepts a
  face up to `faces_total`. Whether it is the edge the plan meant is still the
  business of the signature it carries (length and point).
- Each property of an edge is asked only when a condition needs it, cheapest
  first: a curve, the adjacent faces or the closest point are COM calls, not paid
  for an edge already out by its body or length. A filter that cannot be applied
  is refused (13 forms: an unknown key, an axis that is not x, y or z, `along` with
  `curved` true, an empty range, `within_mm` without a point, a face that is not
  an identifier...); a face beyond the part or a body that does not exist is
  refused with what the part has.

The rules are in `Oce.Solid.Agent.Core/EdgeFilter.cs`, pure.

**Tests.** `R17EdgesFilterContract`: a full filter read, `x` read as X, an empty
filter as the ordinary reading, 13 refusals, `RankOf` in its canonical spelling;
eight filters on eight edges known by construction, with the count of the curve,
face and distance reads (asked only of the edges still in); the wiring in five
places. Contract suite: 880 checks, 4 failed — the 4 of the bench. **Not tried on
SOLIDWORKS yet.**

## 0.137.0 — 26 October 2026 · library 0.160.0 · `mesh`: an STL file measured and cut into contours (brief R15; branch `night-2026-10-26-asm`)

On 24-25/10 the parts of 26M000002 were drawn from the customer's STL files, and
every number taken from them came from three Python programs written for the
occasion. **A tenth tool, `mesh`**, reads an STL file (binary or ASCII) and never
touches SOLIDWORKS:

- **`measure`**: bounding box, size, area, triangles and vertices, whether the mesh
  is **closed** (every edge shared by exactly two triangles) and, only then, the
  **volume** (signed tetrahedra; an open mesh gets `null` and the count of its
  open edges, not a number with no meaning). With `axis` and `levels` (1 to 50),
  the area of N sections evenly spaced along the axis.
- **`section`**: the plane square to `axis` at `at_mm` cuts the mesh into closed
  contours, simplified by `simplify_mm` (Douglas-Peucker, default 0.05 mm), sorted
  by area, each with its depth (outer edge, void, island) and orientation (outer
  counter-clockwise, voids clockwise). **`profiles` are ready for
  `extrude_profile`**: `plane` and `contours`, outer edge first, in the sketch
  coordinates of that plane as R7 measured them (front x = X, y = Y; right
  x = −Z, y = Y; top x = X, y = −Z). More than 3,000 vertices is refused with a
  larger `simplify_mm` to pass.
- An STL has no units: its numbers are taken as mm, times `scale` (25.4 for
  inches). A binary file is told by its size (84 + 50 × the count it declares),
  so one whose header begins with «solid» is still read as binary.

**Measured on the files of 24/10** (through the bridge, `OCE_NO_ATTACH=1`):
`9mmAmmoBox_100ct_V3.stl`, 35,176 triangles, closed, 113.751 × 113.750 × 34.627 mm,
**68,905.399 mm³** in 1,117 ms with five sections; at z = 10 one outer edge
(12,014.136 mm²) and 100 voids of 102.515 mm², section 1,762.597 mm², 1,517
vertices simplified to 646, one profile, in 1,030 ms. The lid: 11,956 triangles,
**23,728.340 mm³**, at z = 2 23 contours in 9 profiles (islands inside voids).
An independent script (`struct` on the same bytes) gives the same two volumes to
the thousandth.

The rules are in `Oce.Solid.Agent.Core/StlMesh.cs`, pure (no interop type).
`tools/list` gives ten tools now; the README lists `mesh`.

**Tests.** `R15MeshContract`: a box 10 × 20 × 30 (6,000 mm³, 2,200 mm², closed,
outward), a square tube 20/10 × 30 (9,000 mm³), a 64-sided cylinder (the polygon
area × 20), a box without one triangle (3 open edges, no volume); sections of the
box on front, right and top inside the measured axes, exactly at its top, outside
it; the tube as an outer edge and its void in one profile (300 mm²); the cylinder
from 128 crossings to fewer than 64 vertices at 0.5 mm; the same box as binary,
as binary with a «solid» header, as ASCII and in inches; junk refused; and the
tool through the real bridge on a file written by the test. Contract suite: 877
checks, 4 failed — the 4 of the bench, which has not run on this branch yet. The
frozen undecided calls get `StlMesh.cs|Add 4`: List<T>.Add on the mesh's own
lists, which the scanner cannot type (it reads a declared type only without
angle brackets).

## 0.136.0 — 26 October 2026 · library 0.159.0 · where a property goes, said; the near name of an unknown argument (brief R8; branch `night-2026-10-26-asm`)

On 25/10 «set_property» wrote the drawing number on the document and answered
that from the configuration «Default<A macchina>» it was NOT visible: two
drawers, and one step could fill only one of them.

- **`scope`**: `file` (the document), `configuration` (the one named in
  `configuration`, or else **the active one**) or `both`. Left out it is
  `configuration` when a configuration is named and `file` otherwise, as before.
  **`both` writes the property in the document and in the configuration and
  reads it back from each**; one that does not come back as written fails the
  step and says whether the document already had it. `file` together with a
  configuration named is refused («choose one, or pass «scope» «both»»), and so
  is a word that is not one of the three. The answer carries `scope`.
- **`value` is not taken for `value_text`.** The repository keeps one name per
  argument (no alias). Instead, **an unknown argument now names the near ones**
  in the refusal: ««value» is not an argument of this operation, and would be
  IGNORED. Did you mean «value_text»?». Near is: one that begins with it and an
  underscore (`radius` → `radius_mm`), one it begins with and an underscore, or
  one at most two letters away when both have four letters or more (a typo).
  It holds for every operation, not only this one.

Every new refusal says what to pass instead: the floor of the refusals with a
way forward goes from 251 of 970 to **257 of 976** (26.3%).

**Tests.** `R8PropertyScopeContract`: 7 combinations of `scope` taken and 4
refused, the refusal of `file` with a configuration naming `both`; 6 near names
(`value`, `radius`, a typo, a longer name, a far one, `x`). Bench: new case
`document_properties/a_property_is_written_in_both_drawers` (both drawers from
one step, the active configuration when none is named, the contradiction
refused). Contract suite: 873 checks, 4 failed — the 4 of the bench, which has
not run on this branch yet. **Not tried on SOLIDWORKS yet.**

## 0.135.0 — 26 October 2026 · library 0.158.0 · the assembly state says what is true, and a long step beats (briefs R6, R13; branch `night-2026-10-26-asm`)

**The assembly state (R6).** On 25/10 the state of 26M000002 said that inserting
and mating components were not operations yet and that the positions of the
components were not in the state — both false since «insert_component», «mate»
and «origin_mm» — and that SOLIDWORKS answers «unavailable» on every component,
while C2 had `degrees_of_freedom_remaining: 2` beside it.

- `what_cannot_be_done_yet` is gone. **`what_can_be_done`** lists the operations
  that run on an assembly, **taken from the catalog** (`Operations.NamesFor
  ("assembly")`: the 9 of the assemblies, the 6 for a part or an assembly, the 3
  for any document), and says where each component is and how it is turned
  («origin_mm», «axes»). The next operation cannot leave it behind.
- `what_is_not_known_about_constraints` **names the components** whose degrees
  of freedom SOLIDWORKS did not give, each with its state («C1 (root
  component)»), and says «any of the N components» only when none was read.
  What «constrained» means, and that one coincident mate leaves three of the six
  directions free, is unchanged (the bench reads those words).

**A long step beats (R13).** One notification per step said «running step 3 of
6» and then nothing until the step was over: on 24/10 a step of 1,069 s was
silent for eighteen minutes. Now, while a step runs, **every 10 s** a
notification says how long it has been on it and the last line the executor
wrote: «running step 3 of 6: apply_material, still running after 40s (last:
…)». Its `progress` grows inside the step (k.5, k.67, k.75 …) and never
reaches the next step, as the specification demands. It is sent only when the
client asked for progress (a `progressToken`), and **stopped before the answer**:
at the next step, at the outcome, and by the session when the executor returns
or throws — a beat after the answer is the defect R13 found in the dry run. The
beat holds a lock while it sends, so nothing goes out once the stop returns.
`Protocol.Send` now writes under a lock too: the beat comes from a timer thread
while the executor writes from its own, and two messages at once would make a
line that is not JSON.

**Tests.** `R6AssemblyStateTextContract`: the old sentences are absent from
`AssemblyState.cs` and the new one is built from the catalog; `NamesFor
("assembly")` gives the 18 names the exported catalog says, and `NamesFor
("part")` holds `extrude_profile` and not `mate`; 5 sentences of the two
builders. `ProgressTests.TheHeartbeat` drives the beat by hand: 1.5, 1.67, 1.75,
then 2 for the next step; nothing after the stop or from the timer of an old
step; no timer without a token; the message carries the time and the last
line. Contract suite: 871 checks, 4 failed — the 4 of the bench, which has not
run on this branch yet. **The timer itself (10 s) is not driven by the suite,
and none of this is tried on SOLIDWORKS yet.**

## 0.134.0 — 26 October 2026 · library 0.157.0 · what a step must measure, written in the plan (brief R16; branch `night-2026-10-26-asm`)

On 24-25/10 every check of a plan was made by hand afterwards: the window of
the clamp had to remove 36 mm³ (3 × 6 × 2), and the only way to know was to
read the outcome and do the sum. The plan said what to do, never what the step
had to come out as.

**A step of `build` can carry `expect`, next to `arguments`.** Its keys are the
step's own measurements, by the name the outcome answers them
(`removed_mm3`, `diameter_mm`, `walls_drilled`, `bounding_box_mm`, `kind`...);
`volume_mm3` is the volume after the step (`volume_after_mm3`). A value is a
number, a text, true/false or a list of numbers, or
`{"equal_to": value, "tolerance": number}`.

- A plain number holds within **0.01**, the rounding of the measures that come
  rounded to 0.01; a list is compared element by element; texts and true/false
  must be equal.
- After the step the executor compares each key. **One that does not hold makes
  the step fail and the plan stop there**, with the numbers side by side:
  «The step ran, but it did not measure what «expect» asked: removed_mm3:
  expected 36 ±0.01, measured 35.2». The step is not withdrawn: `undo` is
  still the way back, as for any failed step.
- **A key the step does not measure is a miss, not a pass**: an expectation
  nobody checked would read as kept. The sentence lists what the step does
  measure.
- A form that cannot be checked (not an object, empty, an object without
  `equal_to`, a negative tolerance, a key other than `equal_to`/`tolerance`, a
  list of texts) is **refused before anything runs**, and `dry_run` lists it with
  the other troubles of the plan.

The rules are in `Oce.Solid.Agent.Core/StepExpectations.cs`, pure (no interop
type); `PlanStep.Expect` carries them from the tool to the executor.

**Tests.** `R16StepExpectationsContract`: 12 rows of `Misses` against one set of
measurements (plain number, explicit tolerance, lists, texts, true,
`volume_mm3`, a key not measured, two keys with one off) and the two sentences
(the numbers side by side, the names the step measures); 6 good forms taken and
9 bad ones refused. Contract suite: 867 checks, 4 failed — the 4 of the bench,
which has not run on this branch yet. **Not tried on SOLIDWORKS yet.**

## 0.133.0 — 26 October 2026 · library 0.156.0 · placing and turning components, the side of a distance mate, a face named by a point (briefs R12, R21; branch `night-2026-10-26-asm`)

The assembly of 25/10 could not be put together: «insert_component» places the
centre of the box with no rotation, nothing moved or turned a component
afterwards, a mate took its faces by direction only (the largest one that looks
that way), and a distance mate put the upper half underneath with no way to ask
for the other side. 102 operations (101 before).

**`place_component` (new).** Sets where the origin of a component goes
(`origin_mm`, the same field the state answers) and how it is turned
(`rotation`: one turn `{axis, angle_deg}` or a list, about the axes of the
assembly, from the part's own axes). It writes `IComponent2.Transform2` with the
layout the direction test of Z-C14 already measured (rotation by rows, row i =
axis i of the component; translation in [9..11]), rebuilds and reads it back. A
component its mates hold comes back elsewhere: the step is refused, the component
set back, and the answer says how far the mates took it. A fixed component is
refused with the road (`fix_component`), and so is one inside a sub-assembly.

**`insert_component`** takes `origin_mm` and `rotation` too (refused together
with `x_mm`/`y_mm`/`z_mm`, which place the box centre); the placement is applied
after the insertion and read back, or the component is taken out again. Its
answer and the state list `axes` for each component (`+X -Y -Z` when every axis
lies on an axis of the assembly, three unit vectors otherwise).

**`mate`.**
- `distance_side`: `along_normal_a` (the default, what SOLIDWORKS does with
  `Flip` false) or `against_normal_a` (`Flip` true). The side is MEASURED after
  the mate, on the two faces (signed distance of face B from face A along the
  normal of face A, both turned into the assembly): a mate on the other side is
  taken out and refused. The answer gives `face_b_along_normal_a_mm`.
- `placement_after`: `origin_mm` and `axes` of both components, also in the
  verdict.
- A face can be named by a point, `{"point_mm": [x, y, z]}` in mm of the
  assembly: the face of the component within 0.05 mm of it, flat or curved. A
  point no face reaches, or one on an edge, is refused with the distance read.
  So concentric and tangent are back when at least one face is named by a point.

**Tests.** The pure rules (turns, axes in words, signed gap, a point carried into
the part and back) are checked without CAD in the Z-C14 group. Two bench cases:
`a_part_is_turned_over_and_placed`, `a_face_is_named_by_a_point`. Refusals that
say what to do: 251 of 970.

---

## 0.132.0 — 26 October 2026 · library 0.155.0 · the brief of 25 October (branch `night-2026-10-26`)

On 24 and 25 October a client drew a part and an assembly through the bridge for
hours, and wrote down 23 problems with their evidence (brief of 25/10/2026). This
version takes the first of them, in the order of the brief, **after measuring
them**: every number below comes from a run on SOLIDWORKS 2021 SP5.1, with the
script and the output kept beside it, not from an estimate.

**The measure that drove it (scenario T1).** The plan of 21 steps of 24/10 (six
extrusions, a chamfer, five variables, six links, material, a property, a save),
replayed on a new part each time:

| | 0.130.2 | readings cut | + API sequence | 0.132.0 |
|---|---|---|---|---|
| time of the plan | 1,243.8 s | 672.7 s | 63.6 s | **55.9 s** |
| characters of the answer | 21,663 | 9,831 | 9,782 | **5,103** |

A plan refused in validation answers **377 characters** (10,804 on 24/10; the
brief asked under 600). The brief asked the answer of that plan under 3,000
characters: **not reached**, 5,103. What is left is the measures of each step
and the summary of the state; going lower means leaving out measures, which the
brief itself wants kept. To be decided.

**Speed (briefs R2 and R24).**

- `ISldWorks.CommandInProgress` is set for the length of a call when it was
  off, and put back after (`Session.BeginApiSequence`): on the plan above,
  **x10.6** (672.7 s to 63.6 s). On by default; `OCE_API_SEQUENCE=0` turns it off.
- A plan held in memory no longer reads the state to know its source
  (`IPlanSourceWithoutState`), and 42 operations that do not need the state of
  the document no longer read it before their step
  (`SwOperations.OperationsWithoutState`). A contract keeps that list equal to
  the switch of `Run`.
- `PhaseClock` times every phase of a call. In `full` each step has its
  `timing_ms`; the answer always has `timing_ms` for the whole plan, with the
  slowest step.
- **Log of the bridge** (`Bridge/CallLog.cs`): one JSON line per tool call, with
  the times of each phase and the notes that went to stderr, in
  `%LOCALAPPDATA%\Oce.Solid.Agent\logs` (`OCE_LOG` moves it). Unhandled
  exceptions go there too (brief R14), and the bridge warns when its exe runs
  from a network disk. The log holds local paths: it is not for publishing.

**Another window comes to the front during a plan (brief R1).**

- The plan's document is brought back to the front, before the next step and at
  the end (`on_window_change`: `reactivate`, the default, or `stop`). The answer
  lists the `reactivations`, and says when SOLIDWORKS asked for a rebuild on
  activation.
- **The cause of a part that could not be saved any more.** Measured with probes:
  `IEquationMgr.Add3` of a NEW equation on a document that is not in front leaves
  it unsavable — every `SaveAs` after it answers `swGenericSaveError` and writes
  a file of 0 bytes, even after a rebuild and a reactivation. `set_Equation`,
  `Delete`, properties and rebuilds behind are harmless. The remedy that worked:
  rewrite each equation in place (`set_Equation` with its own text) with the
  document in front. `global_variable` and `link_dimension` now bring the part
  to the front before adding (in `stop` mode they refuse instead), and if
  another window came in front during the call they apply the remedy and say so
  in `warning_window`.

**Saving with forward slashes (the part of 13 October).** Measured on copies:
a `SaveAs` with `/` in the path onto a new file leaves the document unable to
save on its own file afterwards, and on its own file `SaveAs` compares the path
as text. Every path argument (`path`, `profile_path`, `template`, `model`) now
goes through `Path.GetFullPath` before any operation; `save_document` onto the
document's own file uses the very string SOLIDWORKS holds; `insert_component`
normalizes its path. A document already damaged that way still refuses its own
name: `save_document` now names the two measured causes and says to save under a
new name, which worked in both (the old name stays refused).

**Holes and pockets (brief R10).** `cylindrical_cut` and `rectangular_cut` draw
their sketch without snapping (`AddToDB`, `DisplayWhenAdded` off) and read the
circle or the rectangle back: a centre SOLIDWORKS moved (tolerance 1e-7 m) is a
refusal with a remedy, not a «succeeded». `diameter_mm` is the measured one,
beside `diameter_requested_mm` and `centre_offset_mm`. A through hole that
drilled no wall fails.

**Sketch axes (brief R7).** An extrusion says where the axes of its sketch go in
the model (`sketch_axes`: `x`, `y`, `extrusion`), read from the inverse of
`ModelToSketchTransform`; measured on front, right and top.

**Mates (brief R22).** The state of an assembly lists its mates (`mates_list`:
`uid` M1, M2..., name, kind, suppressed, components; at most 60). Two new
operations: **`suppress_mate`** (and `reactivate`) on every configuration, read
back; **`delete_mate`**, which asks for confirmation like `delete_feature` and
reads back that the mate is gone. Bench case `a_wrong_mate_is_taken_out`. A
refused confirmation quotes what the client answered. 99 operations become 101.

**Short answers (brief R3).** `response`: `summary` (default) or `full`.
`summary` gives the outcomes with their measures and warnings, `state_summary`
(volume, box, mass, features added and removed) and `timing_ms`; the whole
`state_after` only in `full`, and a plan refused in validation does not read the
state at all. Of a successful step, `summary` leaves out: `error` when null, the
times of the step, volumes before and after when equal, texts over 100
characters that are not warnings, a `value_before_*` equal to its
`value_after_*`, and a measure with the same name and value as an argument the
caller sent. `note_outcomes` counts what was left out. The sentence
`link_dimension` writes after every link is now `meaning`, not `verdict`.

**Smaller ones.** Materials (R4): a name under four letters matches only as a
whole word, the refusal of a name that matches several materials no longer says
«steel», and the name is resolved in validation and in `dry_run`, before anything
runs. On 24/10 «PLA» was found wrong only at its step, after 1,069 s, and the
`dry_run` before had said yes; now the `dry_run` refuses it in 74 ms. A `dry_run` sends no progress notifications
(R13: they arrived after the answer, and the client counted eight dropped
connections). `faces` on a part without a body is an empty list with a note
(R9). The precondition of `extrude_profile` says an empty part is fine (R20).

**Tests.** 864 checks, 4 failed: the four that compare the bench with the code
(the last run is of 9/10 on library 0.153.1, and the new case has no green run
yet). Refusals that say what to do: 241 of 952. Language gate held. **The bench
has not run on this version yet.**

**Still open from the brief.** P0: R12 (transforms of components, placing and
rotating, a mate on an explicit face), R21 (the side of a distance mate, and the
origin in the verdict), R19. P1: R11, R23, R5, R16. P2: R6, R8, R15, R17, R18,
and a heartbeat for long plans (R13).

---

## 0.130.2 — 12 October 2026 · library 0.153.1 (unchanged)

Documents, tests and installer. **No operation and no file of the library or of the
bench changes**, so the bench of 0.130.1 (2,248 tests, 0 failed, in Italian and in
English) still describes this version. Two texts the bridge sends do change, both in
wording only: one message that still carried an Italian preposition (« a » before an
offset now reads « at »), and a few notes `api_search` returns (an Italian dialog name
and a source label, now in English).

**For whoever installs it.**

- The README is rewritten for a visitor of the public page: what it is, what it needs,
  how to install it and connect a client, what is measured and how, what is not proven.
- QUICKSTART and INSTALL-FOR-AI point to the latest Release for `Installer.exe` and
  its `.sha256`; they no longer say the page may be empty.
- `Installer.exe` is now built deterministically: two clean builds of the same source
  give the same file, so the published one can be checked against the source.

**For whoever builds it from a clone.**

- The contract suite no longer ends red on a fresh clone without the TODO: it runs 862
  checks, 0 failed, 2 reported as «not runnable here», exit 0.
- Italian words that the language gate does not see (names of locals, strings of the
  tests, comments, a few English slips) are swept from the tests, the checks, the
  installer and the MCP layer. The library and the bench are left for the next version.

**For the maintainers.** `Checks/prepare-release.ps1` took into the Release notes
everything up to the next `## <number>` heading, so the seventeen «Part of 0.130.1»
entries went in (223,562 characters) and GitHub refused the body (limit 125,000). The
notes now stop at the next `## ` heading of any kind, and the script stops with a clear
message if the notes are longer than 125,000 characters. `Checks/check-publishability.ps1`
now also catches a folder of the maintainer's `S:` share (the working folders and the
settings and libraries folders), in the tree and in the history; it was seen red on the
five places that held one, and clean after they were rewritten.

---

## 0.130.1 — 10 October 2026 · library 0.153.1 · first public release

The first version published. It is the bridge of 0.124.1 brought to English
in every file (W3.1-bis), able to work with an Italian or an English SOLIDWORKS,
with an installer that a person or an AI can run alone. The entries below marked
«Part of 0.130.1» are the steps that led here, one per branch.

**What it does.** An MCP server that lets a model drive SOLIDWORKS: 99 operations
in 12 families (parts, sheet metal, weldments, assemblies, drawings, dimensions,
measurements, exports...), each with its limits and its refusals written in the
catalogue the model reads. Every operation re-reads what SOLIDWORKS really did
and says so: a step that could not be verified is reported, never assumed.

**What changed since 0.124.1, for whoever uses it.**
- Everything is in English: tool names, arguments, values, messages, documents.
  The Italian names are gone (no aliases): an Italian name is an unknown tool or
  argument.
- It works with SOLIDWORKS in Italian or in English: features, planes, standard
  views and cut-list columns are found by type or by what they do, not by name;
  the few words SOLIDWORKS writes as text live in one table
  (`Oce.Solid.Agent.Core/Localization/solidworks-names.json`).
- `inspect_part` can return faces filtered by normal, area or position, and plan
  selectors address every face, not only the 60 largest.
- Fixed: `cut_list` counts gussets and end caps; `gusset` and `weld_bead` read
  an edge running either way; `insert_component` accepts the first component of
  an empty assembly; the centre marks SOLIDWORKS sometimes leaves out of a new
  view are put back.
- Installer (`Installer.exe`, one file): `--silent`, `--dir`, `--no-client`,
  `--uninstall`, `--verify [--json]`, with exit codes a script can trust; see
  INSTALL-FOR-AI.md and QUICKSTART.md.
- Environment variables are `OCE_*` (`OCE_WELDMENT_PROFILES` and the others in the
  README); whoever set the old Italian ones sets them again.

**How it was proven.** The full bench, run with SOLIDWORKS in Italian and with
SOLIDWORKS in English (menus and feature names): 2,248 tests, 0 failed, 1,824 of
1,824 expectations, in both. The autonomous run (decision D9: a model with only
the bridge, 15 tasks, Italian x3 and English x1) is not worse than the bridge
before the rename beyond its threshold (one task of fifteen). The installer
was run on two clean Windows machines of GitHub (windows-2022 and windows-latest).

**Requirements.** Windows x64, SOLIDWORKS 2021 SP5.1 (tested; other versions not
yet), .NET Framework 4.8, an MCP client (Claude Desktop, Claude Code, others).

**Known limits.** After about four hours in one SOLIDWORKS session a call can
hang (the bench restarts SOLIDWORKS; a long working session may meet it). The
drawing title block is read through the custom properties of the template in use.

---

## Part of 0.130.1 — GitHub tests the installer on a clean machine before publication (branch `ci-installer`) · bridge and library unchanged

Tooling only: no change to the bridge, the library, the installer or the catalog, so no version bump
(the rule above is about code that ships). New: `.github/workflows/installer-smoke.yml` (by hand, on
`windows-latest` and `windows-2022`), its two steps `.github/steps/smoke-fetch-release.ps1` and
`installer-smoke.ps1`, and `Checks/release-candidate.ps1` (builds, then creates or updates a DRAFT
prerelease; `-DryRun` prints the plan). `Checks/check-workflow.ps1` now counts a step as called when ANY
workflow calls it, and fails if `release-candidate.ps1` has a line that could publish. A section in
RELEASE.md. A machine without SOLIDWORKS cannot install (exit 1, nothing written), so the workflow asserts
that, and proves the install mechanics against a placeholder file with `OCE_NO_ATTACH=1`; see RELEASE.md for
what that does and does not prove. The workflow has not run yet: it needs a draft release to run on.

`Checks/check-publishability.ps1` no longer reads the owner's signature as a leak: the field `signed_by` with the owner's exact name, which every entry of `Checks/language/exceptions.json` carries, is exempt by its shape (same style as the two support URLs), the same name in any other field or case stays red, and a new rule refuses that signature in any file but `exceptions.json`. Seen red without the pattern (25 lines in the tree, 450 in the history) and clean with it.

---

## Part of 0.130.1 — final bench fix: the centre marks SOLIDWORKS leaves out are put by the bridge (branches `final-bench-fix` and `release-1` merged) · bridge 0.130.1 · library 0.153.1

**The full bench of `w31bis-11` (7/10/2026, 16:09, library 0.152.0) went red on three
lines of the drawing:** `center_marks_on_the_holes` step 8 («3 holes are without a
center mark»), `drawing_is_a_second_document` step 7 (`state.center_marks_total` 0
instead of 3) and `dimensions_come_from_the_model` step 7 (3 dimensions of 4). What was
found, in order:

1. **It is not a regression of window .11.** The families `drawing` and `dimensions`
   alone are green on library 0.151.0 and on 0.152.0, twice each, on a restarted
   SOLIDWORKS. The full bench of `integration-1` (library 0.151.0) ran the same sequence,
   with the same drawing created at the same point («Disegno7» for the centre marks), and
   was green; a run of all the case families on 0.152.0 (no C# group first) passed the
   centre marks too. The red is SOLIDWORKS leaving the hole views of a fresh drawing
   **without its automatic centre marks**, now and then, with the document option on.
2. **What it is not.** Not the age of the session: the red came 49 minutes into a
   session restarted by the bench, and the same point was green in another run. Not the
   access violation SOLIDWORKS logs in `slduiu.dll` (offset 0x94d513): the Application
   event log has one per SOLIDWORKS process since 26/9, in green runs as in red ones, and
   the process goes on working after it. The documented causes (a draft quality view,
   large assembly mode) were read on the views of an earlier long run, but that run did not
   reproduce the red, so they are neither confirmed nor excluded. The cause inside
   SOLIDWORKS stays unidentified; what the bridge does about it is not.
3. **The remedy never worked.** Since 0.144.0 `standard_views` walks the views born
   with zero marks and calls `IDrawingDoc.AddCenterMark` on each bare hole. A probe on
   8/10/2026 made the bare views on purpose (the document toggle
   `swDetailingAutoInsertCenterMarksForHoles` turned off on the drawing before the
   views): `AddCenterMark` on the selected circle returns an object and adds nothing,
   three holes out of three, with the view activated or not; `IView.AutoInsertCenterMarks2`
   with the hole type puts one mark per hole (0 → 3, at −40/0, 0/0 and 40/0), and on a view
   that already has them it **doubles** them (3 → 6, then 6 → 9).
4. **The fix.** `MarkHolesOfView`, shared by `standard_views` and `center_marks`, now
   inserts the marks with `AutoInsertCenterMarks2` (hole type, no connection lines, the
   document's settings unless the call asked for its own size or extended lines), after
   `IDrawingDoc.ActivateView` as the Remarks of `IView~AutoInsertCenterMarks2.html` ask,
   and looks for every bare hole again at its centre: only a mark found there counts as
   added. It is called only on a view where **no** hole has its mark; on a view where some
   are marked and some are not, nothing is inserted (it would double the existing ones) and
   the bare holes are refused with the way that works. `AddCenterMark` is gone from the
   library (its entry leaves `ObsoleteCallsContract` and the frozen list). `standard_views`
   writes one journal line when it fires.
5. **The bench group `bare-views`** (new, `Bench/BareViews.cs`): the plate of
   `center_marks_on_the_holes`, the toggle turned off by COM on each new drawing (no catalog
   operation can turn it off, so no declared case could build a bare view), then four
   checks: `standard_views` marks the three holes, the state counts three, `center_marks`
   afterwards finds the three and adds none, `center_marks` on a single bare top view adds
   three. It refuses to measure if the views come out with marks anyway. On library
   0.152.0 the four are red; on 0.152.1 the four are green (also on the last build, where
   the activation reads the view name with `GetName2` instead of the obsolete `IView.Name`
   that the contract on obsolete calls refused, at 08:55 in the long session).
6. **The dimensions are another matter.** On the views born bare, `model_dimensions`
   inserts the same dimensions with and without the marks (6 and 6 on the plate with three
   holes, library 0.152.0 against 0.152.1): the 3 of 4 is not explained by the bare views,
   and this fix does not claim it. It did not come back in the long run below.
7. **The long run.** One SOLIDWORKS session, restarted once at the start (04:25 on
   8/10/2026) and never again, driven in four bench runs of at most 44 minutes each (the
   machine is shared and a hold of SOLIDWORKS lasts 45 minutes at most; between the runs
   the lock passed to another agent, and the session stayed the same process): first
   every C# group the full bench runs before the cases, `bare-views` included, then the
   case families `text`, `bodies`, `slot`;
   then `features`, `patterns`, `measurement`, `export`; then `weldments`, `sheet_metal`,
   `removal`, `extrusions`, `pairs`, `assemblies`, `chamfer`; last `check`, `reanchor`,
   `plane`, `mirror`, `axis`, `revolve`, `sentence`, `document_properties`, `drawing`,
   `dimensions`. **1,364 checks, 0 red**, handles 1,924 → 7,686, the access violation of
   `slduiu.dll` logged at 04:38 and the session working for four more hours after it.
   `center_marks_on_the_holes` step 8, `drawing_is_a_second_document` step 7 (3 marks) and
   `dimensions_come_from_the_model` step 7 (4 dimensions) green nearly four hours into
   the session. SOLIDWORKS did not leave a view bare by itself this time: the journal line of
   the remedy fired only in `bare-views`, where the bare view is made on purpose. Each run
   was stopped at its 44th minute, so the last cases of each set of families did not run
   there; the full bench before the merge is still the measure.
8. **The crash at 01:35 of the previous attempt was not one.** The run was a background
   task of the agent with a timeout of one hour: started at 00:35, killed at 01:35, in the
   middle of `cut_without_gap_does_not_inherit`. SOLIDWORKS had logged its access violation
   in `slduiu.dll` at 01:04 and gone on (the centre marks were green at 01:12, after it).

`Tests/Zc2113SplineCentreMarksContract.cs` now pins one `AutoInsertCenterMarks2` call in the
marks source, inside `MarkHolesOfView`, after the return on a view with holes already
marked, and no `AddCenterMark` anywhere; each of these is seen red on a changed copy.

---

## Part of 0.130.1 — Release 1: the SOLIDWORKS-language converter and the faces beyond the 60 largest, together (branch `release-1`) · bridge 0.130.0 · library 0.153.0

Built on `w31bis-11` (f538914) by merging `sw-language` and `faces-beyond-60`. Both took the next
MINOR (bridge 0.130.0, library 0.153.0) from the same base; together they are ONE minor above
f538914, so the version stays 0.153.0 / 0.130.0 and the two entries below are both kept, each as
it was written on its branch. The merge conflicts were the version comment in `AssemblyInfo.cs`,
one list in `Checks/build-checks.ps1` (both probes are kept), the check count in `Tests/README.md`
and this file.

Two things the merge itself found, and fixed: the registry of the snapshot (`Checks/language/snapshot-scripts.json`) did
not name the two new probes, `Checks/SolidWorksLanguage/` and `Checks/FacesBeyondSixty/`, and a script it does not name
stops `snapshot.js take`, a thing no check saw (each branch had passed without a snapshot). Both are now rows (skipped:
SOLIDWORKS probes), and `snapshot.js --check-registry` runs in the contracts on the real tree (seen red by taking a row out).

Proof of the merged tree, 8/10/2026 (SOLIDWORKS 29.5 Italian, the lock `SW-LOCK` held in two windows of 39 and 42 minutes, single
families only, never the full bench): `build.ps1` ok; contracts 861, the same 3 red as f538914 (the freshness of the full bench,
which the owner's run closes); `catalog.json` differs from f538914 in five descriptions (the language converter) and nothing else;
`test-installer.js` exit 0; `prerequisite.js --check` holds on the new bridge and on the bridge of f538914 (0.129.0); the six
prerequisites of D9 (01, 02, 03, 07, 08, 09) build with the NEW bridge and meet all their expectations (07: 40 minutes);
the family `measurement` (13 cases, 109 tests, 77 of 77 expectations) is green with the native Italian names and again with the
toggle «Use English feature and file names» on (put back to off at the end); the language gate lists only the three unsigned entries
of `solidworks-names.json` (24 occurrences). Not proved: the English user interface of SOLIDWORKS itself (it needs a restart of
SOLIDWORKS); the `en` column of the views, the callout, the cut-list properties and the reserved name is not measured live.

---

## Part of 0.130.1 — the SOLIDWORKS-language converter: the code writes English and reads any language (branch `sw-language`) · bridge 0.130.0 · library 0.153.0

Built on `w31bis-11` (f538914). The owner's words of 7/10/2026 are applied, «prima di v0.1.0» for
the time and, for the reason:
«così noi scriviamo tutto il programma in inglese e poi lui può usare quello italiano».
Between the English program and the names SOLIDWORKS writes there is a converter, two levels deep.
It was measured first (probe `Checks/SolidWorksLanguage`, on the Italian SOLIDWORKS 29.5.1 and with
the toggle «Use English feature and file names» on), then written.

**1. First level: language-neutral wherever the API allows.**
- *The standard views* are no longer found by name. `IModelDocExtension.GetNamedViewRotation`
  answers nine zeros for every named view (measured), so it cannot tell them apart; what can is
  showing the view: `IModelDoc2.ShowNamedView2` with an id of `swStandardViews_e` and with each
  name the document declares leave `IModelView.Orientation3` in the same place when the name IS
  that view, in any language (front, back, left, right, top, bottom, isometric, trimetric,
  dimetric; «*Normal To» looks like the front and is dropped by taking the later name). The
  orientation the window had is put back from a copy of its data (measured: identical). With no
  window the order of the ids is used (only when all ten system views are there), and then the
  table. `model_view` says in `found_by` which one answered.
- *The columns of the cut-list table* are told by TYPE (`swWeldTableColumnType_ItemNumber` and
  `_Quantity`, as before) and by role: `ColumnRole` gives item number, quantity, length, angle1,
  angle2, description. The measurement `workshop_columns_visible` lists the roles, and the case
  `cut_list_in_the_drawing` asserts it instead of the headers the template prints. The column
  types are read once (`GetColumnType2` is a frozen obsolete call with a count: no call added).
- *Base planes* (by their normal), *materials* (the databases `GetMaterialDatabases` lists),
  *feature kinds* (`GetTypeName2`), *cut-list folders* (their type) and the *bill of materials*
  columns (their type) were already language-neutral: nothing to change in the code, and the
  Italian names in their comments are English now.

**2. Second level: one table.** `Oce.Solid.Agent.Core/Localization/solidworks-names.json` (embedded
in the library, compiled as a link into the bench and the probes) has a row per name SOLIDWORKS
writes only as text and a column per language (`it`, `en`; a language is added with a
`solidworks_language` and the words). `SolidWorksNameTable` (no interop) holds it;
`SolidWorksLanguage` reads `ISldWorks.GetCurrentLanguage` and the toggles `swUseEnglishLanguage` and
`swUseEnglishLanguageFeatureNames` (API pages ISldWorks~GetCurrentLanguage, swUserPreferenceToggle_e,
SO_General.htm) and chooses the column. Measured: the names of FEATURES, sketches and new documents
follow the toggle at once, with no restart (`Boss-Extrude1`, `Sketch1`, `Cut-Extrude1`, `Shell1`,
`Part368`, `Draw53`, `Cut-List-Item1`, the weldment feature `Weldment`); the texts of the INTERFACE (view
names, the word of a hole callout, cut-list property names, reserved variable names) follow the
language of the interface, so the table has two domains. A language with no column (German,
French...) falls back to English and the state says so: `solidworks_language` in the state of
every document (`language`, `names`, `feature_names` when it differs, `warning`). Used by: the
view fallback; the word «through» of a hole callout (`callout.through`, every column
recognised, with the geometry read next to it); the property names of the cut list
(`cutlist.property.*`, every column recognised); the reserved word of the equations
(`equation.reserved.thickness`: measured, the English word is accepted on an Italian SOLIDWORKS
and the Italian one is refused).

**3. What a template stores does NOT follow the language, and is not in the table.** Measured
with the toggle on: the planes of the company part template, its configurations and display
state, the sheet of a new drawing from the company drawing template, the cut-list property
«MATERIALE» and the view names stay as the file or the interface has them. They are data of the
template, not language: the bench quotes the configuration pair and the sheet as the template
has them (two narrowed exceptions, below). An English part and drawing template (the plan of
the previous window) is what makes them English; the cases are not written to find them by
type yet.

**4. The bench runs the same cases in both languages.** The cases say `{SW:feature.cut_extrude}1`,
`{SW:equation.reserved.thickness}`...: `CaseNames` replaces the placeholder, in the text of the
case before it is parsed, with the word of the table for the language of the run; `run_plans.js`
does the same for the plans (it asks `Checks/SolidWorksLanguage.exe --columns`). 72 case and plan
files changed, 103 placeholders. New switches: `bench.ps1 -SolidWorksLanguage en|it|native`
(`--sw-language`: the toggle «Use English feature and file names» ON or OFF for the run, put back
at the end, also on Ctrl+C, and by the next run if the run was killed: the value to restore sits in
`%TEMP%\oce-bench-language-restore.txt`; `SolidWorksLanguage.exe --english-names off` does it by
hand) and `-Case a,b` (`--case`: single declared cases by file name, the twins they name come
along; a selection with no refusal does not fail the «there is a twin» check). The ceiling test
looked for a feature name that starts with the Italian word; it asks the table. The report names
the language of the run. `Tests/SolidWorksNamesContract.cs`: 21 new checks without SOLIDWORKS
(the table parses, the column of a language and the fallback with its warning, the placeholder,
every key in use exists, the nine views and their ids, the cut-list roles, a malformed table
refused, the gate entries equal to the Italian column).

**5. The exceptions: 40 entries to 28, THREE NEW ONES WAIT FOR THE OWNER'S SIGNATURE.**
`solidworks-localized` goes from 21 entries to 9.
- *New, unsigned (3):* the Italian column of the table, `Oce.Solid.Agent.Core/Localization/solidworks-names.json`:
  the five names of features, sketches, axes and shells; the nine names of the standard views; the six
  words of the interface (the callout word, the four cut-list properties, the reserved variable name).
  The detector does flag them (a first try with no entry passed only because the new files were not
  tracked yet). Three and not one because the gate refuses an entry that takes away ten hits or more
  of a file (`exception_limits`) and the column has 24: split by what the words are, each is under the
  limit. The gate says «3 exception entries wait for the owner's signature» and that the 24 occurrences
  are covered only by them: nothing else is left. A contract (`ItalianColumnAndTheGate`) keeps the
  three patterns equal to the Italian column, no word more and none less.
- *Narrowed (2):* the sheet of the company drawing template (one case, «Foglio1») and the
  configuration pair of the company part template («A macchina», «Saldato», three files).
- *Untouched (4), company data and not language:* the square-tube and BHP profile files of the
  company weldment library, the title-block properties and the drawing template name (the last two
  wait for the English template of the previous window).
- *Removed (15):* the names of features, sketches, axes, shells wherever they were quoted (cases,
  library, probes, README), the hole callout word, the center-mark option, the section and drawing
  view names, the reserved word, the standard views, the cut-list columns, the mirror, weldment
  and solid-body names, the hole wizard thread type, the material classes, the three base planes.
  The Italian name of a SOLIDWORKS feature in a comment is its English name now.

**6. Proof.**

The owner's rule for a shared SOLIDWORKS held: probes, single cases and families only, under the
shared lock, never the full bench (the run of the owner's session is the one the contract suite
waits for). SOLIDWORKS 29.5.1, Italian interface.
- *Probe* `Checks/SolidWorksLanguage`, native and with the toggle on: the names of the new
  features, document titles and the folders of a weldment in each language, the ten named views
  and what each does when shown, the reserved variable names, the material classes, the
  configurations. The toggle was put back to «off» every time (read back).
- *Bench, Italian* (`--case`, 20 cases, 252 tests): green. They cover every key of the table
  (boss, cut, sketch, axis, shell, the reserved word) and every operation touched here
  (`model_view`, `drawing_cut_list` twice, the hole callout three times, `global_variable`, a
  weldment cut list).
- *Bench, English* (`-SolidWorksLanguage en`, the toggle on, the interface still Italian): the 40
  declared cases that hold a placeholder or exercise a touched operation, in two runs of 20, green
  (227 and 202 tests; the first of the two runs was repeated on the final library: 227, green),
  plus the four refusals of the group with their twins (`--case` first did not find a twin that
  sits inside a step: 62 tests, twins 4/4 green once it did). The families are bodies, check,
  dimensions, drawing, extrusions, features, measurement, patterns, removal, slot, weldments.
- *Regression references* (`-Regression`, 19 cases): Italian 17 hold, 2 come loose and say so,
  none in silence, 45 of 45 expectations. English: the same, 17 hold, 2 come loose and say so, none in silence, 45 of 45 (the first English run had one bench error: r12 asked for its dimension by the sketch name that the profile file of the library keeps in Italian; it asks for the dimension by its name alone now).
- *Plans* of `cases/bridge` (15, by `run_plans.js`) in both languages: the same outcome in both,
  and the only difference in the answers is the name SOLIDWORKS wrote (they are plans of the
  hand runs that need an earlier state: they fail alike in both languages).
- *Contracts* 835 checks, 3 red: the same three as on f538914 (the bench of 6/10/2026 is older
  than the code; they turn green with the owner's full bench). `see-it-fail.js` is red the same
  way on f538914 in a worktree. The gate waits only for the three signatures of 5 (its own words: 24 occurrences covered only by entries that wait; 0 outside every entry).
- *Not done*: the interface itself in English (the toggle «Use English language menus» needs a
  restart of a SOLIDWORKS other people use), so the English column of the interface texts (view
  names, callout word, cut-list properties, reserved word) is read from the guide and from the
  old code, not measured live; a language without a column (German, French) only has the
  fallback; and the planes, configurations and sheet that a template stores are not found by
  type (see 3).

**7. Adversarial review (same version: the library and the bridge change only in comments).**
- *A probe the translation had broken.* `Checks/FaceFillet` selected its sketches with
  `SelectByID2` and the Italian name; the mechanical translation turned it into «Sketch1», which
  selects nothing on the Italian installation, and `FeatureExtrusion3` was left without a profile.
  It now takes the last feature of type `ProfileFeature` (IFeature~GetTypeName2.html) and selects
  it with IFeature~Select2.html: no name, any language.
- *The toggle is put back on the main thread.* The bench put «Use English feature and file
  names» back only from `ProcessExit`, which runs on another thread with a time limit of about
  two seconds, and a COM call to SOLIDWORKS from there needs the main thread to pump. `Main` now
  calls `CaseNames.RestoreLanguage` in a `finally` (renamed from `End`, a name the obsolete-call
  scanner cannot decide); `ProcessExit` and the restore file stay as the second and third net.
- *The first level is now asserted.* Only the isometric was ever asked of `model_view`, and no
  case said WHICH level had found the name: a first level finding nothing would have been rescued
  by the table and stayed green. `drawing_exports_to_pdf` asserts `found_by: model`, and the new
  case `drawing/model_view_finds_the_view_by_what_it_shows` asks for front, back, right and top,
  each found by the model AND equal to the name the table has for it (`{SW:view.front}`...): the
  swStandardViews_e mapping (front 1 / back 2, right 4 / left 3, top 5 / bottom 6) and the
  table checked against each other.
- *Sentences the translation made false.* ««Alloy Steel» in Italian, «Alloy Steel» in English»
  and four more comparisons that said the same word twice (materials, `SwOperations` on localized
  names, the drawing views, the PDF case, the callout case), the weldment case that called
  «Weldment» the Italian name, the reserved-word case that called «thickness» the taken word,
  and `Tools/api_knowledge.json` (a rename «to Cut-Extrude1» compared with «Cut-Extrude1»; and the
  Italian reserved word with an Italian example name, now `wall`).
- *Italian SOLIDWORKS names the gate does not see* (its lexicon has no feature names), left in
  comments outside the table: fillet, rib, draft, revolve, chamfer and cut names in the library
  (`DocumentState`, `DraftOperations`, `FaceFilletOperations`, `RibOperations`, `SwOperations`),
  the probes (`Rib`, `RibWithdraw`, `BrokenBody`, `FaceFillet`, `CloseLeftovers`), `Bridge/Session.cs`,
  `Tools/McpTools.cs` and `Bench/DeclaredCases.cs`: in English, as the rest of the library. Outside
  the table and the signed template entries no Italian SOLIDWORKS name is left (searched by
  pattern, not by the gate).
- Counts: `Bench/README.md` drawing 42 -> 43, `Tests/README.md` 835 -> 837 checks.
- *Proof of the review.* Bench `--case` on 7 cases (the two view cases, the cut list in the
  drawing and in the weldment, the reserved word, the blind hole, suppress twice): Italian 120
  tests, 0 failed; `-SolidWorksLanguage en` the same 120, 0 failed, the features born
  `Boss-Extrude1`, `Cut-Extrude1`, `Cut-List-Item1`, and every view found by the model under
  the name of the `it` column of the table (the interface stays Italian). The toggle came back to off after the English run AND after a run that failed
  before any case (`--case no_such_case_exists`, bench error, exit 2), read back by
  `SolidWorksLanguage.exe --columns`. `Checks/FaceFillet` on the Italian installation: the cubes
  are extruded (64000 mm3) and the four recipes give their fillet as before. Contracts 837
  checks, the same 3 red as f538914 (the freshness of the full bench); the gate: 0 outside the
  entries, the same three entries waiting for the signature.

---

## Part of 0.130.1 — W3.1-bis.12, faces beyond the 60 largest (branch `faces-beyond-60`) · bridge 0.130.0 · library 0.153.0

Built on `w31bis-11` (f538914). Task: the state lists the 60 largest faces and says
`truncated`; a small face of a part with more of them has no identifier, so no plan and
no model can name it. On the tube stand of task 07 (17 solids, 177 faces) the walls of
the third and fourth foot tie at 4,800 mm2 behind 40 larger faces of the frame, the
prerequisite plan stopped at its first gusset («not found (the state lists only the 60
largest faces of 177)»), and the autonomous run could not measure tasks 11 and 15.

**1. Every face has an identifier: the rank of its area among ALL the faces** (F1 the
largest). The default reading lists F1..F60 as before; the other faces are numbered, not
described. `DocumentState.TryGetSignature` builds the signature of any face the first
time a plan cites it (a part of 8,000 faces pays for the faces a plan cites, not for all
of them at every reading), and the five places that find a face by its identifier — a
face (`ResolveFace`: holes, pockets, fillets, drafts, and the rest), a shell, a gusset, a
weld bead, a measurement — ask the state by rank. The check that stops a plan citing an
identifier that was not in the part (`UidsThatDoNotExist`) accepts a rank up to
`faces_total`. A face the reading did not list is protected as the listed ones are by the
guard that stops a plan on a part that changed since it was read; the search by signature
after a mismatch looks at the listed faces only, so an unlisted face that moved is refused,
not searched for again (declared in the description of `inspect_part`).

**2. `inspect_part` takes `faces`, a filter** (`FaceFilter`, `DocumentState.ReadFaces`):
`normal` (up, down, front, back, left, right: the words of `facing`), `area_min_mm2` and
`area_max_mm2`, `near_mm` with `within_mm` (the distance to the FACE, from
`IFace2.GetClosestPointOn`, not to `point_mm`), `body` (C3 or the exact name) and `limit`
(60 by default, 500 at most). The state then lists the matching faces with their ranks
and says `faces_total`, `faces_matched`, `faces_filter` and `faces_not_listed`. The
expensive reads are asked last (body, area, normal, point), the `shape` sentence is not
written on a filtered reading (it would describe only the faces listed), a crooked filter
is refused before the document is touched and a body the part does not have says how many
it has. A reading with a filter keeps the signatures of the reading before when it is the
same part, so it does not unprotect F1..F60. Only the faces are filtered.

**3. The runner of the prerequisites reads every face, and builds the starting part with
one bridge for both sides of D9.** A group of a plan that names a face by selector reads
the state with `faces: {limit: 500}`; a bridge that does not answer the filter fails the
build instead of resolving on the cut list. `run-autonomous.ps1` takes
`-PrerequisiteBridge` (default: the bridge of this repository, the one AFTER) and builds
every prerequisite with it, whatever bridge the session talks to: the starting part is a
fixture, not a measure, and it is the same part on both sides: the same plan built by the
same bridge in the folder of every session (README of the tasks; `run.json` records
`prerequisite_bridge` and its version).

**4. The bench** reads every face when a step names a face by selector and the state is
cut (`ReadStateEveryFace`), finds an unlisted face by its rank, has the case
`a_face_beyond_the_largest_sixty_is_named_by_selector` (`red_before` 0.153.0) and the group
`faces-beyond-sixty` through the bridge; `Checks/FacesBeyondSixty` is the probe. The
contract `FacesBeyondSixtyContract` checks the filter's parsing, refusals and choice
without SOLIDWORKS.

**Measured live on 7/10/2026** (SOLIDWORKS 2021 SP5.1, the bridge of this branch):

- **The six prerequisites of D9 build, with the new bridge and the new runner, every
  expectation of their tasks checked**: `01_l_bracket` (4 expectations), `02_bolt_circle_flange`
  (3), `03_slotted_plate` (3), `07_tube_stand` (10), `08_box_and_lid` (5), `09_bracket_pair` (6).
  The stand: 17 bodies, 6 rows of cut list (17 bodies in it), `end_capped` 4, the four gussets
  2,400 mm3 each; the part has 206 faces and three of the four gussets rest on foot walls the
  ordinary state does not list (`F70`, `F80` and `F64` in the successive readings).
- **Red before, green after.** The plan of 07 as it was (feet one by one, `end_cap` on the
  bodies), with the new runner and the new bridge, built its four gussets and then held
  neither number of the task: `end_cap` capped both ends of every foot (`end_capped` 8), 21
  bodies and 21 in the cut list instead of 17 — the defect the old `_status` predicted, and
  the reason for the rewrite of the plan (point 3). On the bridge before, the record of 6/10 is a
  stop at the first gusset of its group (the state lists 60 faces of 177); a run of it on 7/10
  was cut after 15 minutes, still building on a slow SOLIDWORKS, and proves nothing either way. The case
  `a_face_beyond_the_largest_sixty_is_named_by_selector`: **green on 0.153.0** (the plate has
  71 faces, the Ø3 wall is named by selector and measured at 8.5 mm, deviation 0) and **red
  on 0.152.0** («@face(diameter_mm~=3): I did not find it», reported by the bench as «red before
  0.153.0, as it should be»). The family `measurement` is 13 green of 13 on the new library
  (77 of 77 expectations verified).
- The probe `Checks/FacesBeyondSixty` on the part of 230 faces (the first stand): the ranking
  by area is stable between two readings of the same geometry (0 ranks differ); the 60th face
  has 4,800 mm2 and 32 faces share that area, 12 inside the first 60 and 20 left out, so the
  order of the ties is what hid them and a higher ceiling would only have moved the problem;
  `GetClosestPointOn` finds the wall of a foot at distance 0 from a point on it.

**Adversarial review (8/10/2026), fixed:**

- **A reading of every face can still be cut** (a part with more than 500 faces): a selector
  that found ONE face on that list was taken, although its twin could be beyond the cut — the
  silent defect the selectors exist to refuse. Now refused, in the bench (`Expectations.OnlyOne`,
  `FacesCutByFilter`) and in the runner (`resolveSelector`), each with a self-test or contract
  seen red without the fix; `n=` still works (the listing keeps the largest first).
- **A filter the reading did not apply** (an assembly, a drawing, a part without solid
  bodies) came back as the ordinary state, which reads as «nothing matched»: `inspect_part`
  now refuses it and says why. The description of `faces` also says that every listed face
  costs time and text, that ties are ranked in the order of the bodies, and that it is for
  parts only.
- **The group `faces-beyond-sixty` sent the same plan twice** on an unchanged part and the
  bridge refused the repeat («I have already run this plan»): its step 4 swaps the two ends.
  12 of 12 green live on a fresh SOLIDWORKS.
- **«The same file on both sides» was not true**: every session builds its own copy from the
  same plan with the same bridge. The READMEs and the runner say so, and
  `score-autonomous.js --compare` now refuses two runs whose prerequisites were built by
  different bridge versions (a run older than the option is a note).
- `Bench/report/last.json` had been overwritten by a partial run: restored to the last full run.
- The trace of the runner (`PREREQ_TRACE`) records the seconds of every answer.

Measured live in the review: the six prerequisites build and hold every expectation of their
tasks (01: 119 s, 02: 81 s, 03: 149 s, 08: 189 s, 09: 202 s, **07: 2,432 s**: 17 bodies, 6 rows,
17 in the cut list, four gussets of 2,400 mm3, volume 1,814,624.9 mm3, the same as the build of
7/10); the bridge BEFORE (0.129.0) opens that stand and reads it (17 bodies, the same volume,
60 faces of 206 listed); the family `measurement` 13 of 13 green, 77 of 77 expectations.
**What a reading costs on the stand** (206 faces, 17 bodies, `inspect_part` timed through the
bridge): the ordinary reading 50 s (97 s the first time), every face (`limit` 500) 86 s, a
narrow filter (8 faces) 47 s. The filter is not where the 40 minutes of the stand go: the
reading itself costs 50 s on 17 bodies and every build step of the plan costs minutes; that
cost was there before this window and is still open.

## Part of 0.130.1 — W3.1-bis.11, the translation closed: no alias, a gate of zero (branch `w31bis-11`) · bridge 0.129.0 · library 0.152.0

Last window of W3.1-bis. It closes what the first ten left open on purpose: the
dictionary that still accepted the Italian names on input, and a language gate that
only promised not to grow. Decision D4 (no alias) and the order of 1/10/2026
(«tutta l'intera repo con niente escluso deve essere in inglese») are applied to the letter.
Built on `integration-1` (c4707ef). Nothing here touched SOLIDWORKS: the contracts ran
with `OCE_NO_ATTACH=1` and the bench running on `integ1` was not disturbed.

**1. The input dictionary is gone.** `Alias.cs` (the table that translated tools,
operations, arguments, values, selectors and resource URIs on the way in), the name
map embedded in the library, `SwOperations.AttachDeprecated` and the lines that told the
caller «deprecated» are removed, with every reader: `Mcp/Tools`, `Program.cs`, the
bench counters `alias_uses` and the family `aliases` of declared cases (four cases),
`AliasLayerContract`. `SwOperations.Run()` now holds the switch itself. An old name is
an unknown name and is treated as every other unknown name: an unknown tool is `isError`
and lists the tools there are, an unknown operation is refused as unknown, an unknown
argument is reported as ignored and never moved to another one. The new contract
`NoAliasContract` proves it twice, without SOLIDWORKS: by reflection (no type that
translates, no `.tsv` resource) and over stdio on the built bridge.
*A hidden Italian protocol came out with it:* `Bench/DeclaredCases.cs` recognised an
ignored argument by the Italian word that began the old message; it now reads the
English `not `. The case `c4_frame_axis` still asked for `measured_as: «asse»`, an alias;
it asks for `center_distance`. The cases that made up a name («Non-Esiste» and the like)
now make up an English one.

**2. The Italian branches of the tests.** `CatalogTests.Number()` reads the operation
counts of the README in English and digits only (16 fakes, seen red); the remedy table
of `RefusalsTests` and `RefusalKeywordsContract` has English forms only (floor 228 of 938,
unchanged). `SafetyNetSelfTests` no longer runs the name-map checks.

**3. Archived, not deleted.** The rename map (`name-map.tsv`) and the tools that only
served it — `name-map-extract.js`, `update-names-in-notes.js`, the `window-*.js` and
`window-*.json` of the rename windows, `Alias.cs` with its contract and the four alias
cases — were moved out of the repository into an archive folder of the owner's
notes, with a note that says from which commit they come. Every reader was fixed: `snapshot.js` and its registry (the script table stops
the tool on a script it does not name, and names the new probe set), `same-il.ps1`
(generic, it takes the map as a parameter), the pre-push hook (the name-map self-test
is gone), `check-publishability.ps1` (the old script names are no longer a list: the
exceptions by name read them from `git log --follow`, which already knows every path a
file had), `Checks/todo-paths.js`. `Checks/language/baseline.json` is gone.

**4. The gate is zero, and blocking.** `Checks/language/detect-italian.js` used to count
the Italian of the tree and fail if it went up against a baseline. It now counts the
Italian outside the exceptions the owner has SIGNED, and fails if there is any: an
exception counts only when `signed_by` and `signed_on` are filled; until then the gate
lists the waiting entries (reason, pattern, files, how much each would cover) and fails.
The transition categories (the ratchet, the baseline, the frozen totals) are removed;
the eight categories stay. The closed list of classes is six: `changelog-history`,
`changelog-quote`, `solidworks-localized`, `data-becomes-geometry`, `todo-reader`,
`detector-lexicon`. Every entry names a narrow pattern, never a whole file, and the
reach limits of `lexicon.json` judge it (40 hits in a file, half of the Italian of a
file from ten hits, 300 in the tree; not the changelog classes and the lexicon). The CI
step `language-gate.ps1` is strict (marker `language-gate-strict`): it judges the head with
the detector of the base when the base is already strict, and a change to
`exceptions.json`, `lexicon.json` or the detector needs the trailer
`Language-Gate-Config-Change` in the commit. `see-it-fail.js` (20 mutations, each seen
red), `ci-proof.ps1` and `LanguageGateTests` were rewritten for the new rule.

**5. Everything the gate found, translated.** Every hit the gate found became English: the
probes of `Checks/` (names of classes, files and messages), the scripts of `Checks/` and
`Bench/` (`count-tested.js`, `error-enum.ps1`, `solidworks-modules.ps1`, ...), the
comments and strings of the library, the stale identifiers that the cases and the notes
still quoted («Provata» is `Tested`, the readers by their current names), the `rag:` and
`memory:` references of `Tools/api_knowledge.json` (the note id is kept, `rag:82d`; the
slug is dropped), the report of the last bench run. `Bench/tasks/prerequisites/07_tube_stand.json`
writes the square-tube profile as `{SQUARE_TUBE}` and `prerequisite.js` holds the name once.
`Tests/Zc2111ComponentsContract.cs` keeps its «old reader must stay gone» check under an
English name (`PartsOfTheAssembly`).

**6. The exceptions to sign: 39 entries, none signed** (40 before the review below
removed the one of the sheet name). The gate lists them and nothing
else. By class: `detector-lexicon` 2 (the word lists of `lexicon.json`; the patterns of
`exceptions.json` itself), `changelog-quote` 1 (the text between «» in this file),
`changelog-history` 1 (this file from the entry of W3.1-bis.10b down), `solidworks-localized`
21, `data-becomes-geometry` 2, `todo-reader` 12. The 21 of SOLIDWORKS are the names it
writes in the language of the installation: the default names of features, sketches, axes,
sheets and parts; the square-tube profile of the weldment library; the hole callout word;
the center-mark option; the section and drawing view names; the reserved equation word;
the standard views; the cut-list columns; the mirror, weldment and solid-body names; the
hole wizard thread type; the material classes; the three base planes; the configurations
of the company template; the title-block properties; the drawing template name; the
default configuration and front plane quoted in the README. The two of
`data-becomes-geometry` are the engraved test word (whose 56 outlines are counted) and the
note of the drawing standard of the catalogue example. The 12 `todo-reader` entries are one
per family of words of `count-todo.js` (seven, so that none covers the file), the readers
`todo-paths.js`, `todo-references.js` and `todo-lines.js`, and the folder of the owner's
notes (the TODO lives there).

**The plan for the English drawing template.** The title-block properties are a
temporary `solidworks-localized` entry. To remove it: make, with SOLIDWORKS, a drawing
template and a part template with English property names and the same links, replace
the names in `TitleBlockOperations`, the catalogue examples, the case
`title_block_says_what_it_wants` and the case
`limit_property_on_a_configuration_that_does_not_exist`, run the bench on the new
template, and delete the entry. The same template removes the cut-list column entry and the
drawing template name entry.

**Things for the owner to decide or know.**
- `OPENPDM_ADDIN` is still read (`Bridge/Session.cs`) next to `OCE_DRAWING_LIBRARY`; it is
  an English name and was left; decide whether it stays.
- The autonomous run lost its «before» mode (D9): it compared two runs through a
  name list that no longer exists; the stored `score.json` of the earlier run still compares.
- `Checks/todo-paths.js` now reports one more missing path (eight against seven): the
  TODO cites the archived `window-3-renamer.js`; the TODO is the owner's.

**Proofs.** `build.ps1` green (bridge, catalogue, probes, payload, installer); `Tools/catalog.json`
differs in one line (a field name in the text of `drawing_dimension`); contracts 813 checks, 3
red, all of them the freshness of the bench report (`the bench ran on the current library` and
the two dates), which only a bench run can turn green; `test-installer.js` 65 checks passed, none failed;
`snapshot.js take` writes the script table (35 scripts run, 48 skipped with their reason) and
`--self-test` 28 of 28; `token-stream.js --self-test` 34 of 34; `consumed-strings.js` 39 entries,
0 broken; `see-it-fail.js --check` 20 mutations; `check-workflow.ps1` all good;
`check-publishability.ps1` clean. The gate, in strict mode, fails and lists exactly the 40
unsigned entries — «0 outside every entry» — and with the 40 signed (tried on a copy) it holds.

**Review (adversarial, same window).**
- *The detector had a hole:* the prose check blanked everything between angle brackets as
  if it were an XML tag, so a placeholder such as `<folder with the files>` written in
  Italian was never read (three were in the tree: `Bench/bench.ps1`, `Checks/Gusset`,
  `check-publishability.ps1`). Now only a name with attributes is a tag, its name goes to
  the identifier check, and a placeholder with words in it is prose. The fix found 21 more
  occurrences: the placeholders above, `<author>` in a comment, and the company-template
  configuration names («Default<…>») in four more files, which the configuration entry of
  `solidworks-localized` now names.
- *The bridge named sheets in Italian by itself:* `new_sheet` without a name produced the
  Italian word for «sheet» plus a number on every installation, an English one included,
  and an exception called it a SOLIDWORKS name. It now continues the series of the first
  existing sheet whose name is a word and a number (unchanged on the Italian installation)
  and falls back to `Sheet`; the catalogue says so, and the exception is gone (39 left).
- *Italian the gate cannot see, found by reading:* the contract details «tutti e …» and
  «tutti e due …» (`Tests/Program.cs`, `ProtocolTests.cs`), a step label and a detail of
  `ProgressTests.cs`, a message of the `Orphan` probe, a quoted argument name in
  `api_knowledge.json`, and the variable `quote` (the Italian word for dimensions, an
  English homograph) in `Bench/DimensionNumbers.cs`, `Bench/Expectations.cs` and
  `AnchorOperations.cs`. All translated or renamed; no behaviour changes but the sheet name.
- Checked and left as they are: every remaining entry is a literal or a closed list of
  names on named files (the changelog history is a scope, by design); nothing reads the
  alias layer, the map or the window tools (`same-il.ps1` takes any map as a parameter, the
  `alias_uses` line in `Bench/report/last.json` is the record of a past run); the detector
  reads every tracked file but the two `.png`, and the contract injects Italian in each of
  the eight categories; both CI workflows run the gate without `continue-on-error`.

**Condition 8, the sample.** `node Checks/language/sample-lines.js --rev ff83e28 --seed 20261006
--count 100` (new: every non-blank line of the tracked text files of the commit has the same
chance; same commit and seed, same lines). 778 files, 168,772 lines. Judged by a model other
than the translator: 97 English (code, JSON, English prose, language-neutral data), 3 Italian
covered by entries that wait for the signature (`CHANGELOG.md:1523`, old option names in the
history table, `changelog-history`; `Checks/language/lexicon.json:884` and `:1678`,
`detector-lexicon`), 0 Italian outside the exceptions. No line is doubtful. The reading that
went with the review (outside the sample) is what found the Italian translated above.

**The entry `changelog-history` is narrow now.** It was `[^\n]+` from the heading of
W3.1-bis.10b down: every line of the history, which would have hidden any Italian prose
left there by mistake. Measured with the entry removed: 3,419 occurrences in `CHANGELOG.md`
(86 lines of prose, 3,333 names). Every one of the 86 lines was read: a verbatim output that
ran over two lines, or sat between straight quotes, is now on one line between guillemets;
a name of before that stood bare is now between backticks; the block of
«nota_assunto» and the message of the changed document («Mi fermo…», the output of the
bench) are translated and say so; «L's notch» (read as an elided article) and
«Università del Salento» are English now. The pattern covers a span between backticks on one line, or a name written as one
token (snake_case, PascalCase, camelCase, `Namespace.Type`), and nothing else: 0 outside every
entry with it, and the 188 occurrences that were left with backtick spans alone are all gone.
The same measure on the other entries (every entry, every alternative of its pattern and every
file of its file list taken out one at a time, 160 runs): the alternatives
«Membro strutturale\d+» and «Elenco elementi\d+» in the entry for the library (the entry
of the weldment files has them), `Tools/catalog.json` in the entry of the fill probes (no
match there), the alternative «Default<…>» of the entry of the configurations (the two
words after it are already covered), «tenut[ao] per» and «licenza commerciale» of the TODO
readers (covered by a longer alternative, or matching nothing) are removed. The entry on
the folder of the internal notes of the owner covers the folder name only; the two subfolders the snapshot tool names (and only in its two files)
are a second entry. 40 entries wait for the signature, and the measure finds no alternative or file of them
left that it does not need; the
gate with every entry signed (tried on a copy) holds, unsigned it fails only for them. The
list for the owner is outside the repository.

**7. The top of the README: two support buttons and a «Project information» section.** At the
owner's request (his own words, typed in chat on 7/10/2026: the indications go in the
README of every repository, to keep track of everything) the README opens with a
«Buy me a coffee» button and a «Donate via PayPal» button, then a section of true badges
only: bridge version, library version, platform (Windows x64), SOLIDWORKS 2021 SP5.1 (the
version the README already says is the tested one), .NET Framework 4.8, license Apache-2.0,
MCP over stdio, and the status badge of the real workflow `.github/workflows/tests.yml`
(it renders only once the repository is public; an HTML comment in the README says so).
There is no badge of code quality, lint or coverage: none of those services is configured.
The new contract `ReadmeBadgesContract` keeps them true: the bridge badge must equal
`VERSION.txt`, the library badge the `AssemblyVersion` of the library (both read from the
source), every badge must be a static shields.io one or the status of a workflow that exists,
and a service that is not configured is red. 16 fakes run in every run, each the good text
with one change. Seen red on a mutated copy of the README, run by the executable from a
folder of its own: library badge one minor behind (red, naming both numbers), bridge badge
one patch ahead (red), original README (green). `Checks/check-publishability.ps1` forbids the
author's name everywhere and both support addresses contain it: the rule now lets through
exactly those two addresses (a lookahead on the shape, in exact case, wherever they stand), never
a file; one letter of difference, another host, http, a longer name, or the name next to
the address stays red (13 sample lines, all as expected, with the .NET regex of the working
tree and with `git grep -P` of the history). Without the exemption the check was red on the README; with
it, clean. `Tests/README.md` said 813 checks while 811 ran; it says 814 now, the 811 plus the three
of this contract. Nothing was built outside the contracts project (its own output folder), the
library, the bridge and `Bench/` were not touched, and the bench running from this tree was
not disturbed. Versions unchanged (bridge 0.129.0, library 0.152.0): no library source changed.

---

## Part of 0.130.1 — Integration 1: d9-defects and d9-fix on top of .10-bis, `Bench/tasks` in English (branch `integration-1`) · bridge 0.128.0 · library 0.151.0

**One branch, from the tip of `w31bis-10b` (33451b7: windows .7 to .10-bis on main
11d7fc2), that carries the three lines of work that were built apart, so that one
bench run measures all of them.** No version moves: the highest of each wins
(bridge 0.128.0 and library 0.151.0 of `d9-defects`, over 0.127.3 and 0.150.2), and the
entries of both stay in this file.

1. **`d9-defects` merged** (f181391, from 3e1fc1d). Conflicts, all read on both sides:
   the two version files and the comments of `AssemblyInfo.cs` (both histories stay,
   0.150.2 and then 0.151.0), the two CHANGELOG entries (both kept, this one above), the
   line of the bench README that counts the cases of `assemblies` (23, with the new one)
   and names `{PROFILES}`, and `Checks/CloseLeftovers/Program.cs`: the closing by path
   of `d9-defects` (a saved document is closed by its path, a never-saved one by its
   title) with the English options of .10-bis (`--close`, `--spare`) and the English
   text of the comments. What the merge cannot see, and was fixed by reading the new
   files against .10-bis: the three cases and the probe `Checks/D9Defects` still named
   the Italian placeholders, environment variable and options; they are
   `{WORK}`, `{PROFILES}`, `OCE_WELDMENT_PROFILES`, `--close` and `--spare` now.
   `Tools/catalog.json` merged by itself and was regenerated from the built library:
   identical, and its diff against .10-bis is the `cut_list` description only.
2. **`d9-fix` taken, not merged** (8d4e511, from main 11d7fc2): the autonomous run closes
   every document between two sessions (`Bench/tasks/session-documents.js`, the new
   modes `--list-json` and `--close-titles-file` of `CloseLeftovers`, next to the
   path-closing of item 1) and builds the part tasks 09 to 15 start from
   (`Bench/tasks/prerequisite.js`, `Bench/tasks/prerequisites/*.json`), with the reader
   of the protocol of before in `prerequisite.js` and in `score-autonomous.js`. It
   comes as ONE commit with the four commits of the branch named in its message
   (6bc6f24, 0604b79, 2a6a7f2, 8d4e511) and not as a merge, because the language gate
   judges every commit of the range against its own parent and 0604b79 introduces
   Italian names that 2a6a7f2 removes two commits later: no merge can make that commit
   pass, and the gate must hold on this branch. The branch `d9-fix` itself is not
   touched. The only textual conflicts were `CloseLeftovers/Program.cs` (the new modes
   and the English options) and the count of `Tests/README.md` (828 checks: 825 plus
   the three self-tests of the branch).
3. **The plans and the runner move to the protocol of .10-bis.** Plans 08 and 09:
   `variant` is `coincident` or `distance`, `alignment` is `aligned` or `opposite`
   (the plans carried the Italian words of before the rename, and the bridge of today
   refuses them: since .10-bis an alignment it does not know is refused, it is no longer
   taken for «aligned»). Plan 07 and `prerequisite.js`: `{PROFILES}` and
   `OCE_WELDMENT_PROFILES`. The comments of the runner: `--close` and `--spare`.
   **The bridge of before the rename** (9384b2c) takes the Italian words, and the name
   map has no row for them (the catalogue describes the values in prose, and the map
   renames keys and enum values only): a plan translated by the map alone reached that
   bridge with «distance» where it wants its own word. `prerequisite.js` now holds an
   explicit table, `MATE_VALUES_BEFORE` (coincident, parallel, perpendicular, distance;
   aligned, opposite, each with the word of before), applied when a plan is translated
   for that bridge; a value that is not in the table stops the translation. `--check`
   reads what neither the enum check nor the dry run of the bridge could see: a mate
   value of before on the bridge of today, and a placeholder nobody fills (the Italian
   profiles placeholder would have reached the bridge as a path). The self-test reads `MateOperations.cs` to
   prove that the table of today is the one the bridge takes, and each new check was
   seen red first. `--check` resolves a relative `--bridge` path (the bridge runs in its
   own folder, and a relative path did not find it there).
4. **`Bench/tasks` in English.** `README.md` was Italian for its first half (about 440
   words) and described the tasks as JSON files that no longer exist: it is translated
   and says what a task is now (a Markdown file whose first blockquote the runner hands
   to the model and whose table `How it is measured` the scorer reads). The Italian
   originals of the task texts stay in the owner's internal notes, outside the
   repository, and `run-autonomous.ps1 -Language it -TasksDir` takes them from there.
   **`l_bracket.json` and `one_meter_frame.json` are removed, not translated:** they
   were the first form, in Italian, of tasks 01 and 06; nothing reads them (the runner
   and the scorer read `NN_*.md` only, no check and no contract names them), and the
   words and the numbers of both are in `01_l_bracket.md` and `06_welded_frame.md`,
   which carry the same text, the same table and the same «what this measurement does
   not see». They stay in the history. The stale statements of the README are corrected
   where the work of this branch made them false (the library defects of the stand and
   the first insert are fixed in 0.151.0; `CloseLeftovers` closes by path).
5. **The case `cut_list_in_the_drawing`, red since window .8 (a7108a5).** It asked
   `columns_visible` for «quantity column», and the drawing template of the workshop,
   which is Italian, prints its own header: the window translated the literal in the
   assertion, but the text is written by SOLIDWORKS, not by the repository. The literal
   the template really prints is back («QUANTITA», next to «LUNGHEZZA»), and it is listed
   among the candidates for the exceptions of .11 (class `solidworks-localized`, in the
   «Kept on purpose» paragraph of the entry of .10-bis above).
6. **The script table of the snapshot** (`Checks/language/snapshot-scripts.json`) did not
   name the probe `Checks/D9Defects/` nor `build-stand.js` that `d9-defects` brought:
   the snapshot tool stops on a script the table does not name. Both are registered as
   skipped, with their reason (they drive SOLIDWORKS).
7. **Review: the closing by path reaches the mode of the run too.** The merge kept the
   path rule of `d9-defects` in the closing of the bench, but `--close-titles-file`
   (the mode `session-documents.js` uses between two sessions, from `d9-fix`) still
   handed `CloseDoc` the bare title first, and only then the path: the fallback
   `d9-defects` measured (a title that matches nothing whole closes the first document
   of that base name) could take a document the run spares. The mode now reads the
   path of every listed title from the open documents and closes a saved document by
   its path, a never-saved one by its title matched whole; a listed title that is not
   open is said and skipped.

**Proofs.** `build.ps1` green (bridge, probes, installer); `Tools/catalog.json`
regenerated from the built library: byte for byte what the merge produced, and its diff
against .10-bis is the description of `cut_list`, nothing else. Contract suite: 828
checks, 3 red, the three being the freshness of `Bench/report/last.json` (the last full
run is of library 0.150.1; this branch runs families, not the full bench, and does not
rewrite that record: the owner's full run does). `Checks/test-installer.js`: 65 pass,
0 fail. `prerequisite.js --self-test`, `session-documents.js --self-test`,
`score-autonomous.js --self-test`, `snapshot.js --self-test`: all held.
`prerequisite.js --check` on the six plans: against the bridge of today with its dry run
(`bin/oce-solid-agent-mcp.exe`), and against the bridge of before
(`d9-before/bin/oce-solid-agent-mcp.exe` and its catalogue, the plans translated with
the table), every plan holds. On SOLIDWORKS 29.5, one family at a time, no full bench:
**`cut_list_in_the_drawing` green** (12 of 12 checks); `weldments` 164 tests, 0
failed (118 of 118 expectations, with the two new cases of `d9-defects`); `assemblies`
229, 0 failed; `drawing` 492, 0 failed (436 of 436). The first pass of `drawing` gave
488 and two reds, both centre marks (`center_marks_on_the_holes` step 8,
`drawing_is_a_second_document` step 7: 0 marks), on a SOLIDWORKS that had been open for
sixteen hours and 22,000 handles, the degradation entry Z-C24 describes; after a restart
(the only documents open were the residue of a killed run) the same family is green, so
they are the session and not this branch. Plans `08_box_and_lid` and
`09_bracket_pair` built on SOLIDWORKS on both bridges (the bridge of before through the
table of mate words), every expectation of their tasks held. **`07_tube_stand` still
does not build**: with `cut_list`, `gusset` and the first insert fixed it now goes
through step 9 and stops at step 10, the first gusset, on defect (3) of the list that
`d9-defects` left on purpose (the state lists the 60 largest faces of 177); the plan
and the README say so, and the runner keeps refusing tasks 11 and 15 on both sides.
Nothing in SOLIDWORKS was left open (the desk was read empty at the end). This machine
has neither the old Italian-named variable nor `OCE_WELDMENT_PROFILES` set: the runs above set the
second for their own process only.

**The language gate on this branch.** Three commits carry a trailer.
`Language-Gate-Config-Change`: one exception of class `transition` for
`Bench/tasks/prerequisite.js` (the six mate words of before and the two refused values
its self-test names, as the one `score-autonomous.js` already has; it goes with the
bridge of before in .11). `Language-Baseline-Raise`: the `transition` of that file, 0 to
11 words, and the case of item 5 (the one literal, in two places of the case). Gate
against 33451b7 held, every commit judged against its own parent.

---

## Part of 0.130.1 — D9 defects: the cut list counts every body, the gusset at the end of a bar, the empty assembly (branch `d9-defects`) · bridge 0.128.0 · library 0.151.0

**Four defects found on 6/10/2026 by the live proof of the D9 prerequisites
(the tube stand of task 07 and the plans that start from it), each measured
before it was fixed with the probe `Checks/D9Defects` ("guide first").** MINOR is
raised for the bridge and the library although two of the four are fixes: the
answers of `cut_list` change (more rows, two new fields) and two refusals that were
wrong are gone, and a reader who compares a number across versions must see it.

1. **`cut_list` counted only the structural members.** The tube stand has 17
   bodies and 6 distinct entries; the bridge read 9 bodies and 4 rows, with no
   error: a plausible wrong number (the only signal was the warning «the cut list
   folders hold 9 bodies and the part has 15», one line among the answer's
   fields, in the live proof). Measured (`D9Defects tree`): the item
   folders of the gussets and of the end caps are `Type` 5 like the others but
   answer `GetCutListType` 1 (`swSolidBodyCutList`; the structural members 3), and
   `WeldmentOperations.Recognize` kept only 3. It now keeps 1, 2 and 3 (page
   `swCutListType_e.html`). Those items have a quantity and a material but no
   length and no description, so every row now carries `item_type`
   (`weldment_member`, `solid_body`, `sheet_metal`) and `first_body`, or a gusset
   would be a row of nulls. Bench case `cut_list_counts_the_gussets_and_the_caps`
   (5 bodies, 4 rows, quantity 5, and the `item_type` and quantity of every row:
   red on 0.150.1 with `bodies_in_cut_list` 2). A frame of structural members
   only has no item of type 1 or 2, so its rows are the same as before plus the
   two new fields; the warning on the sum now says «bodies in all (bars, gussets,
   caps)» instead of «bars in all»;
2. **`gusset` refused the faces of a foot at the END of its bar segment.** It
   was the corner, not the segment: the contact test evaluates the curve of each
   edge at `UMinValue`, the midpoint and `UMaxValue` of `GetCurveParams3`, and
   when the curve of the edge runs the other way those are in negative parameter
   space (page `ICurveParamData~UMinValue.html`), so the curve was evaluated 32 mm
   off the edge (measured: distances 32, 16, 0 mm for an edge that lies on the
   face). New helper `ApiHelpers.EdgeRangeOnCurve` tries `(UMin, UMax)` and the
   normalized `(-UMax, -UMin)` and takes the one whose ends evaluate CLOSER to
   `StartPoint` and `EndPoint`, the first on a tie, so an edge whose range was
   right reads exactly as before; an edge whose closer candidate still misses its
   ends by more than 0.01 mm counts as not read, it is not guessed (review: the
   first version demanded 1 µm on both ends, which could turn a spline or an
   intersection edge a few microns off its vertices from «evaluated as before»
   into «not read»). `weld_bead` and `face_fillet` (its hand calculation of the
   volume) share the contact test `LengthThatLiesOn` and get the same repair.
   Which way the curve of an edge runs depends on how the bar and the
   foot were drawn, not on where the foot is: on the stand the corners at the
   start of the segment were taken and the ones at the end refused, and on
   straight feet (the bench case) the old reading refuses the first gusset
   too. Bench case `gusset_at_the_end_of_the_bar`. The same pattern is in
   `DrawingWeldOperations.VisibleJoint` and was NOT touched: it is not one of the
   four defects, and it needs its own case;
3. **The first `insert_component` into an empty assembly was refused** («the
   assembly had -1 components»), although the component went in, on both bridges.
   Measured (`D9Defects count`): on an empty assembly `GetComponents(true)` is
   null, which the helper turned into -1, while `GetComponentCount(true)` is 0;
   after the first and the second insertion both agree. The count is now asked of
   `GetComponentCount` (page `IAssemblyDoc~GetComponentCount.html`). Bench case
   `the_first_component_goes_into_an_empty_assembly`;
4. **`CloseLeftovers --close` closed by title.** It now closes a saved
   document by its path and a never-saved one by its title. Measured
   (`D9Defects names`, `closing`): the path is exact; a title matches whole first,
   and a name that matches no title whole falls back to the base name and closes
   the first open document of that base name (with `twin.SLDPRT` and
   `twin.SLDASM` open, `CloseDoc("twin")` closed the part); two documents with
   the same title cannot be open together (`OpenDoc6` answers 65536). **The harm
   was NOT reproduced with the titles this machine gives** (extension and drawing
   sheet included): both desks the probe builds (a never-saved document sharing a
   base name with a saved one; a saved drawing and its part) were cleaned
   correctly by the old tool too, so there is no case that is red before. The fix
   is the defence, and `D9Defects closing` stays as the regression check.

Not changed, on purpose: the state still lists only the 60 largest faces (a
plan of the stand with the long bars already drawn needs the walls of the feet
by their area and a selector, which the 60-face list can hide: propose, do not
change the truncation); the other places that close a document by title
(`Bench/`, `Checks/*`) close a document they just made and whose title they just
read.

Adversarial review (same day): the probe subcommands `tree` and `gusset` read the
part as `ActiveDoc`, against the rule that a shared SOLIDWORKS is never read
through the document in front; they now take the document by name
(`D9Defects tree DOC`, `gusset DOC X Z`, `ISldWorks~GetOpenDocumentByName.html`),
and `build-stand.js` prints the title to pass. `EdgeRangeOnCurve` takes the closer
candidate instead of demanding 1 µm (above). Reproved: the three new cases red on
0.150.1 for the defect itself (`bodies_in_cut_list` 2; «the two faces do not
touch»; «the assembly had -1 components»), and green on 0.151.0 with the families
weldments and assemblies, the three face-fillet cases and the two drawing cut-list
cases (421 tests, 349 of 349 expectations), but for one check that is red on
0.150.1 too and is not this branch's: `cut_list_in_the_drawing` expects the column
«quantity column» where the Italian template prints its own header, a literal that
window 5 (a7108a5) translated in the assertion though SOLIDWORKS prints it.

---

## Part of 0.130.1 — W3.1-bis.10b, the names the code still held in Italian (branch `w31bis-10b`) · bridge 0.127.3 · library 0.150.2

**Why this window exists.** Window .11 makes the language detector blocking at 0
everywhere outside closed classes of exceptions. After .10 what still kept it
above 0 were names the code itself holds, and that a reader somewhere else looks
for: the words the programs print for the bench and for the contract suite, the
options of the bench and of the probes, environment variables, the values the
model passes and reads, a few file names, the id of the CI job and some texts of
the installer. This window renames them, **every producer and every reader in
the same commit, no alias** (decision L0 of H19; the aliases of the input are
removed before the tag anyway). Each name was found with a script on every
producer and every consumer (C#, JavaScript, PowerShell, YAML, JSON cases,
documents), and the old name was searched for again in the whole tree afterwards:
none is left outside this file, the name map, the baseline, the lexicon, the window
tools of W3.1-bis and the record `Bench/report/last.json`. The old names are
quoted between «» below, as the entries of the rename windows do.

1. **The summary lines and their readers.**
   - The bench prints `N tests, M failed` (it printed «N prove, M fallite») and
     `N tests in K groups` (it printed «N prove in K gruppi»); the readers are
     `Bench/bench.ps1`, `Bench/README.md`, the contract `BenchTests` and the
     consumed-strings inventory.
   - The contract suite prints `N checks, M failed` (it printed «N controlli, M falliti»)
     and `root:` (it printed «radice:»); the reader is `Checks/prepare-release.ps1`,
     which now also stops with a message when the suite printed no summary line
     (it would have thrown on a null).
   - The JUnit file is `results.xml` (it was «prove.xml»): writer `Tests/Program.cs`;
     readers `.github/steps/how-much-did-not-run.ps1`, the upload step of
     `tests.yml`, `Checks/language/snapshot-scripts.json`, `Tests/README.md`.
   - The files the bench writes: `last-run.txt`, `in-progress.txt`, `history.txt`,
     `regression.md`, `mutations.md`, `without-waiting.txt` (they were «ultimo-giro.txt»,
     «in-corso.txt», «storico.txt», «regressione.md», «mutazioni.md», «senza-attesa.txt»,
     and the ignore rules of the same files); the folders `Bench\work` and `Bench\views`
     («lavoro», «viste»); the lines `report:`, `exit:`, `building`, `SKIPPED:`,
     `twins N/M green`, `N mutations, M red`; the labels of the regression table `HOLDS`,
     `SAYS SO`, `SILENCE`; the session files `step_N.json` and `state_N.json`; the
     placeholders of the cases `{WORK}` (142 uses in 78 files), `{PROFILES}` (42 in 22),
     `{DATA}` (11 in 6), `{BENCH}` of the plans (23 in 21), and the copy of the frame
     profile `FRAME_COPY.SLDLFP`.
   - **Found while proving that nothing dangles.** `Bench/bench.ps1` (translated in
     .10) read the line `library X.Y.Z`, but the bench still printed «libreria X.Y.Z»:
     the `library` field of the report would have said «(not declared)» on the next
     run, and the freshness contract would have judged the wrong library. And the
     bench's own check of the view photograph looked for the temporary files «oce-vista-*»
     while the library names them `oce-view-*` now. Both are fixed. Three entries of
     `consumed-strings.json` (`bench-summary-line`, `release-contract-summary`,
     `installer-probe-backup-name`) went red when their consumer changed, as designed,
     and were bound again to the new lines.
   - `Bench/report/last.json` is the record of a run made before this window: its
     `summary` keeps the words and the library number of that run (0.150.1). It is
     not edited by hand; the next full run rewrites it. The freshness contract is
     red until then, exactly as it already was.
2. **The options, no alias.** Bench and `bench.ps1`: `--library`, `--slow`,
   `--mutate`, `--regression`, `--group` and `--groups`, `--family`,
   `--pretend-no-solidworks`, `--close-leftovers`, `--session`; `CloseLeftovers`:
   `--close`, `--spare`; `run_plans.js` and `measure_accumulation.js`: `--clean`,
   `--close`, `--spare`; the probes: `--keep` (nine of them), `--load`, `--explore`,
   `--head`, `--high`, `--append`, `--with-inferences`; the catalogue exporter:
   `--check`. They were «--libreria», «--lento», «--muta», «--regressione», «--gruppo»,
   «--gruppi», «--famiglia», «--fingi-senza-solidworks», «--chiudi-gli-scarti»,
   «--sessione», «--chiudi», «--risparmia», «--pulisci», «--tieni», «--carica»,
   «--esplora», «--testa», «--alta», «--aggiungi», «--con-inferenze», «--controlla»
   (about 160 uses in 29 files). The 21 names of the bench groups are English too
   (`cases`, `views`, `frames`, `holes`, `state`, `table`, ...), and the texts that
   name them.
3. **Environment variables.** `OCE_WELDMENT_PROFILES` (it was «PROFILI_SALDATURA»: 63
   uses in 18 files, installer included), `OCE_WRITE_ROOT`, `OCE_READ_ONLY_FOLDERS`,
   `OCE_DRAWING_LIBRARY`, `OCE_CATALOG`, `OCE_QUESTION_REGISTRY` (the five variables
   whose names began with «RISCONTRO»: 47 uses in 15 files), `OCE_ADDIN_SOURCES`, `OCE_PRETEND_NO_INTEROP`,
   `OCE_DOCUMENT_TEMPLATES`, `OCE_BRIDGE` and `OCE_INSTALLER` (the two variables of
   the bench scripts and of `test-installer.js`). The documents and the contract that
   demands every variable of the bridge in the README follow. **The alias
   «OPENPDM_CATALOGO» is gone** (an Italian name read as the old one of `OCE_CATALOG`):
   `Tools/Catalog.cs` reads one name. The legacy `OPENPDM_ADDIN` is an English name and
   stays: it is an alias, and .11 decides it.
4. **The values the model passes and reads.** All English, with no alias:
   - mates: `variant` is `coincident`, `parallel`, `perpendicular`, `distance` (and
     the refused `concentric`, `tangent`), `alignment` is `aligned` or `opposite`;
     the state names a mate `coincident`, `concentric`, `perpendicular`, `parallel`,
     `tangent`, `distance`, `angle`, `symmetric`, `width`, `lock`, `hinge`, `slot`,
     `gear`, `screw`, a component `suppressed`, `lightweight` or `resolved`, its
     freedom `constrained` or `free`;
   - the `kind` of a selected entity is `face`, `edge`, `body`, `vertex`, `feature`; a
     body is `solid`, `sheet`, `wire`, `empty`; a drawing view is `sheet`, `projected`,
     `section`, `detail`, `auxiliary`, `relative`, `detached`, `view`; the unknown
     codes read `type N`, `condition N`, `code N`;
   - keys: `standard` and `hole_type` (hole wizard), `perimeter_mm`,
     `normal_distance_mm`; `degrees` (the units of an angle); the identity of an
     unsaved document is `title:<title>`; the undo entry is `Assistant`; the registry
     records the kind `tool`;
   - the refused values of `applies_to` of the centre marks (`fillets`, `all`,
     `slots`), the plane names the cases invent (`diagonal`, `oblique`, `upper`), the
     example name `wall` (it was «parete»);
   - the internal tags of the drawing dimensions (`hole|`, `cyl?|`, `chamfer|`,
     `round|`, `outline`, `edge_on`, `plane`, `cone`, `torus`) and the names the cases
     choose (`cover`, `bracket`, `container_c`, `ABSENT987`, `TEST123`,
     `anchored_to_absolute`, `anchored_to_border`, `fuse`).
5. **File names.** `dist\release-notes-X.Y.Z.md` (it was «note-di-rilascio-X.Y.Z.md»);
   `questions-without-answer.jsonl` (it was «domande-senza-risposta.jsonl»);
   `profile-sections.txt` in `%LOCALAPPDATA%\Oce.Solid.Agent` (it was
   «Riscontro\sezioni-profili.txt»; the archive is rebuilt by measuring); the installer's
   `.partial` and `.before-oce-solid-agent-<time>.bak` (they were «.parziale» and
   «.prima-di-oce-solid-agent-»); the MSBuild names `VersionFromFile`, `BridgeVersion`,
   `VersionFile`.
6. **The CI job id** is `contracts` (it was «contratti»). There is no branch
   protection, so no required status check named after it exists.
7. **Texts.** The installer window (`Where to install`, `Browse…`, `Operations library
   (where it comes from)`, `Ready.`, `Uninstall?`), the line the installer prints for a
   copied file, `build.ps1` (`found`, `Cleaning`, `Done`) and its payload table, the
   leftovers of the messages of the bench and of the probes (`failed:`, `(missing)`,
   `refused:`, `error`, `warning`), the labels of `test-installer.js` and of
   `questions-without-answer.js`.

**The language gate.** This commit carries `Language-Baseline-Raise` and
`Language-Gate-Config-Change` (the id of the CI job and the comments of `tests.yml`,
the workflow of the gate itself, change). The baseline of one file rises, `CHANGELOG.md`
(markdown 361 → 404 words), because this entry quotes the old names between «»; every
other file is equal or lower, and no identifier, key, value or path is new. Totals
before → after (comments, strings, markdown, json_values, identifiers, json_keys,
protocol, paths, transition): 1762 → 1723, 417 → 390, 825 → 852, 611 → 583,
1626 → 1603, 14 → 10, 647 → 449, 0 → 0, 2188 → 2188.

**What changed besides names** (the only differences of the compiled code outside
string literals, measured with `same-il.ps1` and `token-stream.js`): `Tools/Catalog.cs`
no longer falls back to the alias variable; `Bridge/Paths.cs` no longer lists three dead
argument names (the real argument is `path`); the refusal that spoke of a step
reference as «{passo, genere}» says `{step, kind}`. `Checks/prepare-release.ps1` gets its
guard. The historical comparison of the bench (F10, decision D11) now starts at library
0.150.2.

**What an installed machine has to do.** The variables have new names: an MCP client
entry written by the installer before this version carries «PROFILI_SALDATURA», which
nothing reads any more; reinstalling writes `OCE_WELDMENT_PROFILES`, and a variable set
by hand (`setx`) has to be set again under its new name. An old installation keeps its
old backups and its old registry file next to the bridge (the uninstaller of this
version removes the new names only).

**Kept on purpose, for the owner to read in .11** (pattern, reason, class):
words Italian SOLIDWORKS itself writes and the code reads — «PASSANTE»,
«Croci di centro sui fori», «Vista in sezione», «Foglio» followed by a number, the reserved equation word
«spessore», the standard-view fragments («anterior», «posterior», «superior»,
«inferior», «destra», «sinistr», «isometri», «trimetri», «Isometrica»), the cut-list
properties «LUNGHEZZA», «ANGOLO1», «ANGOLO2», «DESCRIZIONE», the header «QUANTITA»
that the workshop drawing template prints for the quantity column (case
`cut_list_in_the_drawing`; added by integration-1, see its entry), the feature names
«Specchiato» and «Saldatura» (class `solidworks-localized`); the title-block properties
of the workshop template «Numero disegno» (and its one-word form in the catalogue example), «Cliente», «Descrizione»,
«Materiale», «TRATTAMENTO» (class `solidworks-localized` until a new English drawing
template exists: it has to be made with SOLIDWORKS, with the same links under English
property names, and the cases `title_block_says_what_it_wants` and
`limit_property_on_a_configuration_that_does_not_exist`, the examples of the catalogue
and `TitleBlockOperations` change with it); verbatim transcripts of program output in
this file (class `quote`); «PROVA», engraved text whose width was measured (class
`data-becomes-geometry`), and the example note «TOLLERANZE NON INDICATE ISO 2768-mK» of an
Italian workshop (class `workshop-lexicon`, or an English example); the readers of the
Italian TODO (`Checks/todo-*.js`, `count-todo.js`, class `todo-reader`); the ids of the
private RAG notes in `Tools/api_knowledge.json` (the `rag:` references of the `source`
fields, class `quote`). Not exceptions, still to do or to remove: `Alias.cs` and the
name map, the `Checks/language/window-*.js` tools, `Bench/tasks/` (the autonomous run,
branch `d9-fix`), the local names of the probes and of the `Bench/*.js` scripts.

**Adversarial review of the window (second commit).** Every literal the rename commit
removed was looked for again in the whole tree, quoted, and every reader of a renamed
value was followed to its producer. What it found, and what changed:
- **The mate took any alignment but `opposite` as aligned.** After the rename an old or
  misspelt value (a plan written for the bridge before, such as the D9 prerequisites
  of `d9-fix`) would have built the other mate in silence, and no volume sees an
  alignment. `mate` now refuses an alignment that is neither `aligned` nor `opposite`,
  before touching anything, and says what to write; the catalogue says so. The floor of
  the refusals that say what to do rises to 228 of 938. *Not proven on SOLIDWORKS: the
  refusal comes before the first call, but no bench case asks for it yet.*
- **Readers renamed, producers not**: the probe `BrokenBody` compared its body kind with
  `sheet` while its producer still returned the Italian words (the check could never
  hold); `Bench/test_shape.js` read `cases/` plus the Italian folder while the ignore
  rule and the texts name `cases/shape`. Both producers now say the English names.
- **The archive of the measured profile sections** moved, with its new name, into
  `%LOCALAPPDATA%\Oce.Solid.Agent`, which is also the default install folder: left
  there, it would have kept the folder alive after an uninstall with the message that
  the folder held someone else's files (the defect of H19 A5, back). The installer now
  removes `profile-sections.txt` with the registry; `test-installer.js` A5 writes it
  and was seen red before the fix; `INSTALL-FOR-AI.md` says so.
- **A test made vacuous by an earlier rename**: `Zc215IdentityContract` asked for «a name
  in another case» with an Italian plane name, which is simply unknown; it asks for `Top`
  now, the English name in the wrong case.
- The last names of the same kind: the internal cylinder key `cyl|` (its sibling was
  already `cyl?|`), the invented end condition of the trim case (`made_up`), the labels
  `step N` of the bench and of `LimitsTests`, the counts of `check-catalog.ps1` and
  `CatalogTests`, the outputs of `BrokenBody`, `FramingTest` (and its `frames` folder),
  `test_undo.js`, `Regression.cs`, a field of `ExpectationsTests`, the path of the
  archive in the note of `a_frame_too_small_closes_the_profile_library`.

---

## Part of 0.130.1 — W3.1-bis.10, the documents, the scripts and the options in English (branch `w31bis-10`) · bridge 0.127.2 · library 0.150.1 (unchanged)

**The tenth window of W3.1-bis (TODO entry W3.1-bis.10, installer entry H19 lot
L3, decision D13): the user documents, every other tracked Markdown file, the
leftover texts of the checks and of the CI, and the command-line options are
English.** Five groups, in five commits:

1. **the user documents**: `README.md`, `QUICKSTART.md`, `INSTALL-FOR-AI.md`,
   `RELEASE.md`, `NOTICE`, and light edits to `CONTRIBUTING.md` and
   `SECURITY.md`, rewritten against the code (the catalogue has 99 operations in
   12 families, the bridge 9 tools). `INSTALL-FOR-AI.md` is now a procedure that
   can be verified: every step has the command, the expected exit code and
   output, and what to do on each failure; it matches the options and exit codes
   the installer really has (silent install 0, 1, 2, 3; uninstall 0, 1, 3;
   `--verify`; bridge `--self-test` 0, 3, 4, 5, 6, 1). `AGENTS.md` is new and
   points to it. Corrected on the way: `reactivate` is an argument of
   `suppress_feature`, not an operation; the client configuration entry is
   named `solidworks`; the bench takes hours, not seventy minutes;
2. **the other documents and this changelog's header**: `Bench/README.md`,
   `Bench/cases/E2-the-two-cases.md`, `Bench/cases/views/HOW_IT_COUNTS.md`,
   `Bench/pending-cases/README.md`, `Tests/README.md`, `Tools/update.md`. The
   contract check that reads the number of checks from `Tests/README.md` now
   reads the bold form `**825 checks`. `Bench/tasks/` is untouched on purpose:
   another branch is rewriting the runner there;
3. **the scripts and the CI**: the options of the checks are English
   (`--tutti` is `--all`, `--provami` is `--self-test`, `--righe` is `--lines`,
   `--blocchi` is `--blockers`, `--solo-orfane` is `--orphans-only`), the step
   names and messages of `.github/` are English, and the pre-push hook greps the
   words the check really prints now (it grepped Italian markers that
   the script had stopped printing, so it could not fail). The CI job id stays
   `contratti`: renaming it would rename the status check that branch
   protection may require;
4. **the options of the programs, no aliases** (decision L0 of H19): the
   installer takes `--silent`, `--dir`, `--no-client`, `--uninstall`, `--help`
   (they were `--silenzioso`, `--cartella`, `--senza-client`, `--disinstalla`,
   `--aiuto`) and the bridge takes `--self-test` (it was `--prova`). **Why this
   could not wait:** an option the installer does not know does not fail, it
   opens the window and waits for a click, so a documented `--silent` that the
   code did not know would have stopped the AI-driven installation, which is the
   wall INSTALL-FOR-AI exists to remove. Also English now: the installer's
   console lines and window labels (`Install`, `Check again`, `Uninstall`,
   `STOPPED:`), the self-test prefixes `[self-test]` and `[bridge]`, and the
   messages that named `profili_saldatura` (now `weldment_profiles`). The
   «» quotes in the installer's console lines are plain quotes, because the
   console prints «» as garbage. The "done" line of the installer window no
   longer says "sees the open document" when none is open: it says it is
   attached to SOLIDWORKS.

5. **D13: two throwaway scripts removed** (`Checks/rename-product.js` and
`Checks/cleanup-history.js`, made for the rename of the product and the cleanup of
the history, both finished); `Checks/check-publishability.ps1` still names them
because it scans the history, where they exist.

Still Italian on purpose, and why: what the programs still print for the bench
and the contract suite (the words of their summary lines and the name of the
JUnit file: the producers and the readers, `bench.ps1`, `prepare-release.ps1`, `Checks/test-installer.js`,
change together or not at all), the option names of the bench, the catalogue and
the leftovers closer, the file name `dist\note-di-rilascio-X.Y.Z.md`, the
readers of the Italian TODO, and the quotes. The language gate learns them as
exceptions in W3.1-bis.11.

---

## Part of 0.130.1 — W3.1-bis.6, rename window 3: namespaces, projects and identifiers (branch `w31bis-6`) · bridge 0.127.1 · library 0.150.1

**The third rename window of W3.1-bis (TODO entry W3.1-bis.6, decision D6): the
root folders, the projects and assemblies, the namespaces, the 35 probes and the
183 file names of the name map are English.** Names only: no behaviour changes,
so PATCH is raised for the bridge and the library. The window is generated from
`Checks/language/name-map.tsv` by `Checks/language/window-3-identifiers.js`, in
separate commits so that git records every move as a rename:

1. **the gate learns both folder names** (before the move): `detect-italian.js`
   and `.github/steps/language-gate.ps1` find their files under
   `Controlli/lingua` or `Checks/language` in each commit, a commit whose parent
   has the gate and that has none is now a failure instead of «before the gate»,
   and the file patterns of `exceptions.json` accept both paths while the move
   is in flight. Without this the gate judged nothing after the move;
2. **the moves alone** (`--moves`): 756 paths with `git mv`, no content changed
   (the language baseline is re-keyed to the new paths, same counts);
3. **the references** (`--references`): every text that names a moved path or a
   renamed project. A word is renamed as a folder only where it is one: a path
   token whose run of segments exists in the tree before the window (from the
   root, or as a piece of a real path), the arguments of `Path.Combine`,
   `path.join` and `Join-Path`, a dotted project name anywhere (also written for
   a regular expression), a bare namespace segment only as a qualification in
   C#. A path the history had and the tree before the window no longer has
   (`Banco/casi/...`, renamed in window 1) is left whole; a folder git ignores
   (`Bench/viste/`, written by the scripts) keeps its name. CHANGELOG.md, the map,
   the language baseline and the labelled samples of the gate are not rewritten;
   lines that name the old and the new name on purpose carry `window-3: keep`.
4. **the identifiers** (`--identifiers production`, `harness`, `scripts`,
   `mentions`): the 3,900 `identifier` rows of the map, in the C# and in the
   scripts. The C# goes through Roslyn with a semantic model of every project,
   by symbol: a name is renamed where it binds to a symbol the batch declares,
   never where the same word means something else (a member of the SOLIDWORKS
   API, a string, a comment, a local of another method); production first
   (32,168 edits in 156 files, 2,834 names), then the bench, the contracts and
   the probes (13,759 edits in 97 files, 1,463 names). A string is renamed only
   where it names a renamed type or member for reflection, or where a contract
   quotes the code it reads. The JavaScript (acorn) and the PowerShell (its own
   parser) files are renamed file by file (3,220 edits in 48 files, 458 names):
   variables, functions, parameters and the calls of the functions; a shorthand
   property keeps its key (`{ name }` becomes `{ name: itemName }`). The mentions
   of a renamed name that a comment, a document or a script string writes follow
   it, so that the translation of the comments finds the English names already
   there (695 mentions in 135 files, 317 names: a name of two parts or more
   anywhere, a single capitalised word between backticks, never a single
   lowercase word, which may be a local, a key or a word of the sentence).
   Never renamed, as decided: the keys of serialized types, the members of
   anonymous types, the MCP tool names, the case files and `Tools/catalog.json`
   (it is rewritten byte for byte).

   Where the English name of a symbol differs from its row, the tool says why:
   the nested type of the drawing dimensions is `ProjectedEdge` (`Edge` would
   hide the SOLIDWORKS type in the same partial class), the three states the
   undo tool reads are `stateBefore`, `identityNow` and `stateAfter`, the helper
   that cuts a message is `Truncated`, and in the scripts `Write` and `Type` are
   aliases of Windows PowerShell (so the functions are `WriteLine` and
   `TypeName`) and `arguments` is a reserved word of JavaScript (`args`, and
   `cliArgs` in the one file that already has `args`). Two rows of the map were
   edited by hand for the same reason (`FindOpenDocument`, the nested
   `ProjectedEdge`), each with a note.

### Projects, assemblies and namespaces

| before | after |
|---|---|
| `Riscontro.Disegno` (library, `Riscontro.Disegno.dll`) | `Oce.Solid.Agent.Core` (`Oce.Solid.Agent.Core.dll`) |
| `Riscontro.Disegno.Api` | `Oce.Solid.Agent.Core.Api` |
| `Riscontro.Disegno.Catalogo` | `Oce.Solid.Agent.Core.Catalog` |
| `Riscontro.Catalogo` (catalogue exporter) | `Oce.Solid.Agent.Catalog` |
| `Prove.Contratti` (contract suite) | `Oce.Solid.Agent.ContractTests` |
| `ProvaSaldatura` (bench) | `Oce.Solid.Agent.Bench` |
| `Installa` (installer assembly, `dist\Installa.exe`) | `Installer` (`dist\Installer.exe`) |
| `Oce.Solid.Agent.Ponte` | `Oce.Solid.Agent.Bridge` |
| `Oce.Solid.Agent.Strumenti` | `Oce.Solid.Agent.Tools` |
| `Condiviso` | `Shared` |
| `Controlli.<probe>` (21 probe namespaces) | `Checks.<probe>` |

### Folders

| before | after |
|---|---|
| `Riscontro.Disegno/`, `Riscontro.Disegno/Catalogo/` | `Oce.Solid.Agent.Core/`, `Oce.Solid.Agent.Core/Catalog/` |
| `Riscontro.Catalogo/` | `Oce.Solid.Agent.Catalog/` |
| `Banco/`, `Banco/dati/` | `Bench/`, `Bench/data/` |
| `Prove/` | `Tests/` |
| `Controlli/`, `Controlli/lingua/`, `Controlli/ganci/` | `Checks/`, `Checks/language/`, `Checks/hooks/` |
| `Ponte/` | `Bridge/` |
| `Strumenti/` | `Tools/` |
| `Condiviso/` | `Shared/` |
| `.github/passi/` | `.github/steps/` |

The 35 probes of `Checks/` (each compiles to `<folder>.exe` in its `bin\`):
`AlberoRosso`→`RedTree`, `Annulla`→`Undo`, `ApiQuote`→`DimensionApis`,
`CambiaAMano`→`ChangeByHand`, `ChiudiScarti`→`CloseLeftovers`,
`CoperturaRuoli`→`RoleCoverage`, `CorpoRotto`→`BrokenBody`,
`DettaglioInTavola`→`DetailView`, `Fazzoletto`→`Gusset`,
`FlangiaSpigolo`→`EdgeFlange`, `ForoOppureRaccordo`→`HoleOrFillet`,
`Lamiera`→`SheetMetal`, `Mensola`→`Bracket`, `Modale`→`Modal`, `Nervatura`→`Rib`,
`NervaturaRitira`→`RibWithdraw`, `NuovaParte`→`NewPart`, `Opzione`→`Option`,
`Orfana`→`Orphan`, `ProvaInquadratura`→`FramingTest`,
`ProvaPosizioni`→`PositionsTest`, `ProvaRuota`→`GearTest`,
`ProvaSagoma`→`OutlineTest`, `ProvaVista`→`ViewTest`,
`QuoteInTavola`→`DrawingDimensions`, `RaccordoDiFaccia`→`FaceFillet`,
`Riempimento`→`Fill`, `SchizzoSulFoglio`→`SketchOnSheet`,
`SeguiIlBordo`→`FollowTheEdge`, `Seleziona`→`Select`,
`SezioneInTavola`→`SectionView`, `Spessore`→`Thickness`, `TestoNelPdf`→`PdfText`,
`TipoDellEstrusione`→`ExtrusionType` (and `DimXpert`, unchanged). Each
`Programma.cs` is `Program.cs`. The other file names follow the `file` rows of
the map (`Attrezzi.cs`→`McpTools.cs`, `catalogo.json`→`catalog.json`,
`banco.ps1`→`bench.ps1`, `prove.yml`→`tests.yml`, every `Operazioni*.cs` of the
library, and the rest).

### What changes for whoever uses the repository

- The installer is `dist\Installer.exe` (and `Installer.exe.sha256`); the
  bench is `Bench\bin\Oce.Solid.Agent.Bench.exe`, launched by `.\Bench\bench.ps1`;
  the contracts are `Tests\bin\Oce.Solid.Agent.ContractTests.exe`; the probe that
  closes the leftover documents is `Checks\CloseLeftovers\bin\CloseLeftovers.exe`.
- **The switches of the scripts are English.** `Bench\bench.ps1`: `-Group`,
  `-Family`, `-Slow`, `-Mutate`, `-Regression`, `-SkipCompilation`, `-Library`;
  `build.ps1`: `-Clean`, `-BridgeOnly`; `Checks\library-of-the-time.ps1`:
  `-Clean`; `Checks\error-enum.ps1` and `Checks\todo-signatures.ps1`: `-All`;
  `Checks\check-catalog.ps1`: `-Catalog`, `-Library`, `-Source`;
  `Checks\signatures-api.ps1`: `-TestMe`; the CI step `executables-exist.ps1`:
  `-InteropIsThere`. Every text that wrote one of them (the workflow, the
  documents, the scripts, the contracts) was renamed with it.
- An installed bridge keeps a stale `Riscontro.Disegno.dll` next to the new
  `Oce.Solid.Agent.Core.dll` until it is reinstalled; nothing loads it.
- **Historical comparison (decision D11).** The bench reads a library by its
  English names, so the comparison with an older library starts at 0.150.1: a
  library before it (`Riscontro.Disegno`) is refused with exit 2 and a message
  that says so (`Bench/Program.cs`, `Checks/library-of-the-time.ps1`). A defect
  guarded before 0.150.1 is proven red with the bench of its own commit.
- The TODO and the notes outside the repository name the old paths: the
  code names in them are updated with `window-3-identifiers.js --todo <file>`
  (paths and project names only, decision D10), after the merge.

### Proofs

- `window-3-identifiers.js --reverse-proof a883903`: every line that differs
  between the commit before the moves and the window is explained by pairs of
  the map, apart from the hand edits the script lists with their reason (the
  D11 message, the stale `Agente/` file names in `SwOperations.cs`, the patterns
  of `exceptions.json`).
- `--check`: no tracked path and no reference left; a forward run on the window
  changes nothing.
- `same-il.ps1 -Mode Renames` on the bridge, the library, the catalogue
  exporter, the installer, the bench, the contracts and the 35 probes: every
  differing line of IL is explained by the map.
- `Tools/catalog.json` is rewritten byte for byte by the renamed exporter.
- Contracts: 821 checks, only the bench-freshness checks red (no bench on this
  branch). `name-map-extract.js --check` passes. Language gate: held, judged
  commit by commit with the parent's rules.

### The identifiers commit: proofs, hand edits and what is left

- `window-3-identifiers.js --reverse-proof a883903` on the working tree: 779
  paths, 777 text files, 32,096 lines differ, 0 not explained by pairs of the
  map. The pairs the tools decided apart are read too (the symbols the renamer
  names otherwise, the overrides of the scripts), and so is a shorthand
  property written out (`{ name }` and `{ name: itemName }`); the hand edits
  of the script are listed with their reason (28 lines).
- `same-il.ps1 -Mode Renames` against the assemblies built from a883903, on
  the bridge, the library, the exporter, the installer, the bench, the
  contracts and the 35 probes: every differing line of IL is explained by the
  map except (a) the version fields and the Win32 version block (bridge 0.127.1,
  library 0.150.1: 3 lines per assembly), (b) the embedded resources whose
  bytes legitimately changed (the payload of the installer, `VERSION.txt`,
  `api_knowledge.json`, the map), (c) the one message of the bench that says
  where the historical comparison starts (decision D11), and (d) 630 lines of
  the contract suite, all in the methods this commit edits by hand (below).
  The tool learned the identifiers for this, and the self-test has three new
  cases (a message that keeps a word of the map, an iterator class that moves
  among its brothers because its method is renamed, an accessor), each seen red
  on a mutated copy of the tool (17 of 17): a changed line is judged on the
  original text of the old side, not on the rewritten one; an accessor is its
  prefix and the name of its member; the quotes ildasm puts round a name that
  is an IL keyword mean nothing; Roslyn writes the synthesized nested types in
  the order of their names, so the runs of nested classes are put in order by a
  key that is the same on both sides; and the lines not explained are counted
  by the method they sit in.
- `token-stream.js --mode renames` on the 259 files that changed: names renamed
  and nothing else, except the version strings and the hand edits below; the
  `using static` of a dotted namespace is one token for it, and a shorthand
  property written out adds two tokens, so those files are judged by the reverse
  proof.
- Contracts: 821 checks, only the three bench-freshness checks red (no bench on
  this branch). `Tools/catalog.json` is rewritten byte for byte. `name-map-extract.js
  --check` passes (6,062 rows). `check-catalog.ps1`, `error-enum.ps1`,
  `check-documents-without-file.ps1`, `check-workflow.ps1`, `zero-or-unknown.js`,
  `count-tested.js`, the CI steps and `test-installer.js` (65 passes, 0 failures)
  give the same verdict as before; the scripts that need SOLIDWORKS were parsed
  and not run.
- Language gate: judged commit by commit with the parent's rules. The identifiers
  of the baseline go from 5,124 names to 1,873. The commit carries
  `Language-Baseline-Raise`, and this is its reason: three tools of the window
  (`window-3-renamer.js`, `window-3-scripts.js`, `window-3-mentions.js`) are
  written against the names they rename and quote four of them in strings.
  They leave the tree with the map, before the tag.

By hand, each with its reason (and each listed in the reverse proof):

- `RefusalsTests`: a refusal is judged on what its message says, its string
  literals, not on the names of the code around it. Until this commit a variable
  or a method whose Italian name held a remedy word counted as a remedy, and the
  count moved with the names. The floor is 216 of 937, the same on the tree
  before the window and after it (it was 238 with the names).
- the obsolete-calls contract: the frozen undecided calls are raised, with the
  reason in the header of `obsolete-undecided-frozen.tsv` (the receivers are the
  same, in the same untyped places; only the names now collide with an obsolete
  name of the SOLIDWORKS API), and its injections quote the new names.
- the contracts that quote the code they read: the two `Check(Assembly)` that
  would capture the renamed recorder are `CheckLibrary`; two regular expressions
  and one change of the spline contract; the anchors of the sketch lookup
  contract; the pattern the bench source must hold for its default library; the
  `via` of `consumed-strings.json`.
- the self-test of the name map takes the name it removes from the names the
  tree still has (`name-map-extract.js --found`): after this commit no
  identifier row is left to take, so any row would give a green that proves
  nothing. The glued identifier of the language gate's contract is made of two
  words the baseline still has.
- a script parameter is a flag that other texts pass: the 16 switches are
  renamed everywhere they are written with their hyphen.

What is left, on purpose: 77 names of the map are still written in code tokens
of the scripts and of one contract. None is a declaration: they are keys and
member names of the objects the scripts build, words inside regular expressions
and two members of an anonymous type. A key may be the key of a JSON, and the
rule of the window is that keys are not renamed; the owner judges them.

### The review of the identifiers commit

An adversarial review, by script and not by eye, of what the renamer left
behind outside the lines it changed:

- **three checks that had gone quiet are back.** The README contract looked for
  the environment variables through `Variabile("X")`, the old name of the
  shortcut of `Bridge/Paths.cs`: it found 9 of 11 and stayed green; it looks for
  `Variable(` now (11 of 11 again). The bench's F13 sieve took its families by
  the prefix `Prova`, so after the rename it would have found no method at all;
  the prefix is `Test` (the same 14 methods). The contract that looks for the
  commit that moved the bench report listed the report's new path and its first
  one but not the one in between (W3.1-bis.4 to this window): git found no
  rename and the check passed on nothing; with the three paths it finds
  1e3a623 and judges it, and the history between the two renames carries its
  reports again.
- the strings that follow a renamed symbol: the parameter names of two
  `ArgumentOutOfRangeException` (`expiryMs`, `threshold`), the fallback of
  `check-catalog.ps1` (`public Outcome Run(`), the message calls of the
  self-test of `same-il.ps1` and the refusal callee of `snapshot.js` (`Failed`).
- Italian prose the renamer had turned into names, put back as it was before
  the window: a relative pronoun had become `whichOne` in a message of the
  forbidden-signatures contract, a word of a message of the examples contract
  `nothing`, a word of the header of the bench diary `summary`, and two case
  keys of a sample of the gate's contract `itemName` and `expectations`. The
  baseline of those three files goes back up by the words put back (the
  commit carries `Language-Baseline-Raise`).
- two English names the map had read as Italian: `faUpper` (the false-alarm
  bound of the gate's measure) is `falseAlarmUpper`, not `doesUpper`;
  `Get-IlText` (the IL of ildasm) is `Get-Disassembly`, not `Get-TheText`. The
  English column of the two rows says why.
- 188 mentions of renamed symbols in the labels and messages of the contracts,
  the bench and the probes and in comments (`OperazioniSw is in the library`,
  `Sessione.StessoDocumento`, `Riferimenti.BasePlane`...) and a few more by hand
  (`Fallito(`, `Esegui`, `Riuscito()`, the variables a comment of
  `check-publishability.ps1` names).

Rejected after a check: the raised undecided calls (by script, on both trees:
the 593 calls before the window are all there at the same lines, and each of
the 91 more sits where the old line had `.Nome`, `.Spigolo`, `.Tipo`... that
the map renames into a name the SOLIDWORKS list holds); the refusal floor (216
of 937 on the literals, on both trees); serialized types (every serializer of
the repository is given dictionaries, none a named type). Left as they are:
`Tools/api_knowledge.json` and the pending cases quote three old names in their
prose (data the bridge serves, not code). Proofs after the review: contracts 821,
the three bench-freshness checks red; reverse proof 0 not explained;
`same-il -Mode Renames` as above; `name-map-extract.js --check` passes.

## Part of 0.130.1 — H19 L1+L2: the installer tells the truth and verifies itself (branch `installatore-l1`) · bridge 0.126.0 · library 0.149.0 (unchanged)

**Bench, 3/10/2026, on 4c9e465 (window 2 and H19 L1+L2 together, bridge 0.127.0,
library 0.150.0): 2191 tests, 0 failed, 1 skipped (`--lento`), expectations 1783
of 1783.** Run 35 had found 8 case data the window had missed (fixed in
1764232), run 36 ended green but with one bench error, a save into the bench
work folder on the network drive that left no file: the work folder is now
local (4c9e465).

**Lots L1 and L2 of TODO entry H19** (the installer finished for v0.1.0; the
audit of the installer kept with the internal notes, defects 1-7). New options and
texts are born in English; the existing Italian option names (`--silenzioso`,
`--cartella`, `--senza-client`, `--disinstalla`, `--redist`, `--config`, `--aiuto`,
the bridge's `--prova`) stay as they are in this lot and are renamed, with no
aliases, in L2-bis (decision L0a). MINOR raised: new user-visible options. The
library (`Riscontro.Disegno`) is not touched, so its version stays 0.149.0 and the
bench has not run on this branch: the three bench-freshness checks of
Prove.Contratti are red until it does, and they are the only red ones (790 checks;
`Prove/LEGGIMI.md` said 785).

### Exit codes that do not lie (B1)

`Installa.exe --silenzioso` exited 0 when no client had been wired (the exceptions of
`Collega` were printed and swallowed; with no client found nothing was printed).
Now, checked in this order (`Installer/Programma.cs`, constants `Exit*`):

| code | silent install |
|---|---|
| 1 | nothing installed: a prerequisite failed, `--weldment-profiles` names a folder that does not exist, the copy failed, or an unhandled error |
| 3 | files installed, but a client that should have been wired was not (malformed or locked config, the `claude` command and the file both failed), or no client was found; the block to paste is printed after `Paste this into the MCP client's configuration file` |
| 2 | installed and wired, but the bridge's self-test did not confirm (its code is not 0, 3 or 5) |
| 0 | installed, every client tried is wired (or `--senza-client`), self-test 0, 3 or 5 |

3 is stricter than "no client at all": one client failing while another is wired
also gives 3, so no exception of `Collega` turns into a 0. Uninstall: 0 done, 1 a file
of the installation stayed, 3 files gone but a client entry left. `--verify`: 0 all
green, 2 only warnings, 1 something failed.

### A real console, a log, and the launch forms measured (B2)

`Installer/Output.cs`: silent, uninstall and verify modes attach to the parent's
console (as `--aiuto` already did) unless standard output is already a file or a
pipe; `--log <file>` writes the same lines to a UTF-8 file. Measured on 1-2 October
2026 in a fresh console window, no redirection anywhere: 0.125.0 with a run that
stops at the prerequisites (exit 1), 0.126.0 with a malformed client config (exit 3).

| form | exit code seen | console output |
|---|---|---|
| PowerShell `& .\Installa.exe ...` | none: PowerShell does not wait | 0.126.0 yes (after the prompt); 0.125.0 none |
| PowerShell `Start-Process -Wait -PassThru -NoNewWindow` → `.ExitCode` | right (1; 3) | 0.126.0 yes; 0.125.0 none |
| PowerShell `cmd /c Installa.exe ...` | right | 0.126.0 yes; 0.125.0 none |
| PowerShell `cmd /c "start "" /wait Installa.exe ..."` (one line) | **wrong: 0 both times** | 0.126.0 yes; 0.125.0 none |
| cmd batch file: `Installa.exe ...` then `%ERRORLEVEL%` | right | 0.126.0 yes; 0.125.0 none |
| cmd batch file: `start "" /wait Installa.exe ...` then `%ERRORLEVEL%` | right | 0.126.0 yes; 0.125.0 none |
| PowerShell `& .\Installa.exe ... \| Out-Null` | right (a pipe waits) | into the pipe |

INSTALL-FOR-AI gives the second form for PowerShell and `start "" /wait` in a batch
file for cmd, and warns against the one-line `cmd /c "start /wait"`. The table is
also in the comment of `Output.cs`; `prova-installatore.js` launches those two forms
(B2 cases).

### The self-test answers with a code, and the installer reads it (B3)

`Ponte/Sessione.cs`: `AttachFailure` (None, NotRunning, NotInstalled, Skipped, Other)
and `FailureOf(hresult)`; `Programma.cs`: `--prova` exits **0** attached, **3**
SOLIDWORKS not running (MK_E_UNAVAILABLE), **4** not installed or not registered
(CO_E_CLASSSTRING, REGDB_E_CLASSNOTREG), **5** not tried (`OCE_NO_ATTACH`), **1**
anything else; 2 is left to the installer. With code 3 and a `SLDWORKS` process
running it adds that SOLIDWORKS may run as administrator (the ROT is not shared
across integrity levels). The human sentences stay. The installer (`Passi.Prova`,
`SelfTestAccepted`) and the window read the code; the two entries of
`Controlli/lingua/consumed-strings.json` that bound the installer to the bridge's
sentences (`installer-bridge-standing`, `installer-solidworks-not-open`) are dropped
in this commit, and two new consumers of the installer's English output enter
(`installer-paste-header`, `installer-uninstall-file-left`): 39 entries, the floor.
`Prove/H19InstallerContract.cs` (7 checks): each HRESULT gives the promised code by
reflection, every `AttachFailure` has a code and none is 2, and the numbers in
`Programma.cs` and `Installer/Passi.cs` agree (seen red on purpose with the installer's
`SelfTestSkipped` changed to 6: "SelfTestSkipped bridge 5, installer 6").

### `OCE_NO_ATTACH` (new variable of the bridge)

Any value except empty, `0`, `false`: the bridge starts, answers the protocol, lists
its tools, does not attach, and says so; `--prova` exits 5. Needed because the
installer runs the installed bridge (self-test, and the `--verify` handshake), which
attached to the running SOLIDWORKS and read its active document: with the bench
running, `prova-installatore.js` could not run at all. It sets the variable unless
given `--attach`. In the README table.

### Uninstall is coherent (A5)

`--disinstalla` removes the client entry too, unless `--keep-client` (decision L0b:
the author's default; TODO H15 point 3 stays the owner's to change), and only an
entry whose command is the bridge in the uninstalled folder; an entry pointing at
another installation is left alone and named. With `--config` only that file. The
window does the same through the same code. It removes `domande-senza-risposta.jsonl`
(`Passi.BridgeOwnFiles`), and exits 1 naming the file when one stays.

### Weldment profiles reach the bridge (A6)

`--weldment-profiles <folder>` and a field in the window write `PROFILI_SALDATURA`
into the client entry's `env` (also into the block to paste and into
`claude mcp add -e`). A folder that does not exist stops the run with exit 1 before
anything is installed. Without the option, `Passi.FindWeldmentProfiles` proposes a
folder only if one exists. Checked on this machine (SOLIDWORKS 2021 SP5.1): (1)
`HKCU\Software\SolidWorks\SOLIDWORKS <year>\ExtReferences`, value
`Weldment Profile Folders`, is the folder SOLIDWORKS itself uses (Tools > Options >
File Locations); it exists here and is used first; (2) `<SOLIDWORKS root>\lang\<language>\weldment profiles`
exists for all 15 language folders (ANSI, ansi inch, AS, BSI, CISC, DIN, GB, iso,
JIS), language taken from the same key's `Weldment Cut List Template Folders` /
`Weld Table Template Folder`, else english. The year comes from the interop's major
version. Not checked on another SOLIDWORKS version or a machine where the setting
was never touched.

### Stale texts (A7)

`--aiuto` no longer lists `--addin` and lists the new options and the measured
launch forms; the "SOLIDWORKS installed" check names `--redist` and `SW_INSTALL_PATH`
instead of a Browse button that does not exist; `TrovaRedist` reads `SW_INSTALL_PATH`
(the root) before `SW_REDIST`, like the bridge, and `Passi.Prova` hands the bridge the
redist the installer chose.

### `Installa.exe --verify [--json]` (B4)

`Installer/Verify.cs`: files and versions (bridge from its `VERSION.txt`, library,
installer); each client's `solidworks` entry runs the installed exe or not, and its
`PROFILI_SALDATURA`; `api\redist` and the SOLIDWORKS year; the self-test code; an MCP
handshake on the installed exe (`initialize`, `notifications/initialized`,
`tools/list` over stdio, every wait with a timeout, replies matched by id). One JSON
line with `--json`, `[ok]`/`[~]`/`[NO]` lines without.

### Claude Code through `claude mcp add` (B5)

`Installer/ClaudeCli.cs`: when `claude` (`.exe`, `.cmd`, `.bat`) is on PATH and no
`--config` is given, Claude Code is wired with `claude mcp remove --scope user
solidworks` then `claude mcp add --scope user solidworks -e "K=V" ... -- "<exe>"`
(and unwired with `claude mcp remove`); if it is absent or fails, the file is written
as before and the output says so. **The overwrite risk, measured first**
(`Controlli/claude-json-watch.js`, read only, 180 s while Claude Code ran on this
machine): 5 rewrites of `%USERPROFILE%\.claude.json`, shortest gap about 20 s, no lock
file, and a leftover `.claude.json.tmp.<pid>.<hex>`: Claude Code writes a temporary
file and renames it. So the installer's "file locked?" check can never see it, and a
session that writes back a copy read before the installer wrote would drop the entry
with no error. **Not measured:** whether Claude Code re-reads the file before each
write (no live session was put under test), and the real `claude mcp add` (there is
no `claude` on PATH on this machine: the case uses a fake `claude.cmd`). Both stay for
the owner (H19 L4/B8).

### `Controlli/prova-installatore.js`: 55 cases

Red first on the installer of 47d0f8f (c7ab519 plus `OCE_NO_ATTACH` only): **21 PASS,
28 FAIL**; green on 0.126.0: **55 PASS, 0 FAIL**, without touching SOLIDWORKS. New
cases: B1 (malformed config → 3, untouched, block printed; `--senza-client` → 0; a
prerequisite → 1, nothing created; case 6 → 0; case 4 → 3), A7 (no Browse button;
`--aiuto` without `--addin` and with the new options; `SW_INSTALL_PATH`), B2 (`--log`;
Start-Process and `start "" /wait` both report 3), B3 (`--prova` with `OCE_NO_ATTACH`
→ 5), B4 (one JSON line; files present; bridge version; entry → installed exe; the
tool count equals an independent handshake: 9; self-test 5; exit 2; readable lines;
empty folder → 1), A6 (env written, other servers intact; missing folder → 1, nothing
installed, config untouched), A5 (entry removed, others kept, `.jsonl` and folder
gone; `--keep-client`; an entry elsewhere left alone; a locked file → 1, named), B5
(fake `claude.cmd` on PATH found; remove then add with `-e`; success and failure
reported), case 3 (exit 0, entry gone, with `--config`).

**Expected results that need SOLIDWORKS or the owner** (not run here):
`node Controlli\prova-installatore.js --attach` with the bench stopped (the self-test
attaches; `--verify` should then exit 0 with SOLIDWORKS 2021 open and a client
wired); self-test 3 with SOLIDWORKS closed and 4 on a machine without it; the
elevated case (SOLIDWORKS as administrator: self-test 3 plus the warning); the real
`claude mcp add` and a live Claude Code session during an install; the window by
hand (new weldment profiles field, uninstall result); a clean machine (H6 cases 1, 4,
5).

### Review fixes (adversarial review of L1+L2, inside and outside the diff)

- **Claude Code was "installed" on every machine** (high). Its file sits in the
  profile, and "installed" meant "the folder of the file exists": the profile always
  does. A silent install with no client at all wrote a `.claude.json` nobody reads and
  exited 0; the "no MCP client found → 3" branch could never run. Now Claude Code is
  there when its `.claude.json` exists, or its own folder (`%USERPROFILE%\.claude`, or
  `CLAUDE_CONFIG_DIR`), or `claude` is on PATH (`Configurazione.ClaudeCodePresent`).
  Its file is `.claude.json` in `CLAUDE_CONFIG_DIR`, else in `%USERPROFILE%`, as Claude
  Code 2.1.281 reads it (`Configurazione.ClaudeCodeFile`): with `CLAUDE_CONFIG_DIR`
  set, `claude mcp add` and `--verify` looked at two different files.
- **A reinstall dropped the entry's own variables** (medium). The file route replaced
  the whole `solidworks` entry and `claude mcp remove` + `add` did the same:
  `RISCONTRO_RADICE` / `RISCONTRO_SOLA_LETTURA` (where the bridge may write) or a
  `PROFILI_SALDATURA` set by hand vanished on update, silently.
  `Configurazione.MergedEnv` keeps every variable; the installer sets or removes
  `SW_REDIST` and sets `PROFILI_SALDATURA` only when it has one. Without
  `--weldment-profiles` a folder already in a client entry that exists wins over the
  one the installer would find, and the output says "kept from the existing client
  entry" (the window prefills it the same way).
- **No backup on the `claude mcp` route** (medium): `Configurazione.Backup` (the old
  backup code, now public) runs before `claude mcp remove`/`add` and before the
  uninstall's `claude mcp remove`.
- **SOLIDWORKS as administrator was "installed", exit 0** (medium). Self-test 3 with a
  `SLDWORKS` process present was accepted like a closed SOLIDWORKS, the hint was one
  line among the others. Now `--prova` exits **6** when attach says "not running" and a
  `SLDWORKS` process runs in this Windows session (another user's session does not
  count); the installer does not accept 6 (silent exit 2, window red, `--verify`
  fail). `Programma.SelfTestCodeSeen` is the pure rule, checked by the contracts.
- **Quoting for `claude`** (low). `ClaudeCli.Quote` only escaped quotes: a value
  ending in `\` (`PROFILI_SALDATURA=D:\Profiles\`) became `\"` and swallowed the next
  arguments. Now the `CommandLineToArgvW` rules (backslashes doubled before a quote
  and at the end); an argument with `%` is not sent through `cmd.exe` (it would be
  expanded) and the file route is used instead. `--weldment-profiles` is stored
  without a trailing backslash.
- **`--verify` all green beside a malformed config** (low): a client whose config
  cannot be read was treated like one without an entry. Now it is a warning naming the
  file (`readError` in the JSON row), and the clients line names the clients that have
  no entry.
- **The B5 cases were red on the network drive** (test): they loaded the installer
  with `LoadFrom`, which the drive the repository lives on refuses (0x80131515,
  measured). Now from the bytes.
- Smaller: the new Browse button of the window in English; the registry path in the
  comment of `Passi.FindWeldmentProfiles` had lost its backslashes; the skip reason of
  `prova-installatore.js` in `snapshot-scripts.json` no longer says it attaches;
  `Prove/LEGGIMI.md` counts the checks of a run with the TODO present (798; the 790
  written before came from a run without it, and the run on the main copy would have
  been red).

Seen red first: the new cases of `prova-installatore.js` on the installer of bda8946
(**56 PASS, 6 FAIL**; the install cases of the no-client block refused to run there,
because that installer would have written the real profile), then **65 PASS, 0 FAIL**;
the new contracts on the bridge and installer of bda8946 (quoting:
`[PROFILI_SALDATURA=D:\Weld Profiles" C:\Some]`), then green. The self-test 6 case
with a fake `SLDWORKS.exe` runs only with no real SOLIDWORKS open: NOT run here.

**The real `claude mcp add`, measured** (it was "not measured" above): Claude Code
2.1.281 (`%APPDATA%\Claude\claude-code\2.1.281\claude.exe`, the one Claude Desktop
ships, not on PATH here) put on PATH for one run, with `CLAUDE_CONFIG_DIR`,
`USERPROFILE` and `APPDATA` pointing into a scratch folder. First a probe entry with
another name proved the sandbox (it landed in the scratch `.claude.json`, the real one
did not change). Then `Installa.exe --silenzioso` into a folder with spaces: exit 0,
backup made, "registered with claude mcp add --scope user", entry
`{"type":"stdio","command":"...\inst dir with spaces\oce-solid-agent-mcp.exe","args":[],
"env":{"PROFILI_SALDATURA":"S:\..."}}` (a value with spaces, through `-e`);
`--verify --json`: files, solidworks, clients, weldment profiles, handshake ok,
self-test warn (`OCE_NO_ATTACH`), exit 2; a variable added by hand to the entry
survived a reinstall through `remove` + `add`; `--disinstalla` exit 0, "removed with
claude mcp remove", the probe entry left alone. The real `.claude.json` and Claude
Desktop config were unchanged. Still not measured: a LIVE Claude Code session that
rewrites `.claude.json` while the installer writes it by the file route (L4, B8).

## Part of 0.130.1 — W3.1-bis.5, rename window 2: the MCP surface (branch w31bis-5) · bridge 0.127.0 (with H19 L1+L2) · library 0.150.0

Merged together with H19 L1+L2 (below), on a branch rebased on main e49d4d6.
`Banco/banco.ps1` also counts the documents left open from the last line of
`ChiudiScarti.exe --chiudi`: the late-bound `GetDocumentCount()` of e49d4d6
fails from PowerShell (TYPE_E_ELEMENTNOTFOUND), so the restart before a full
run never fired.

Bridge 0.126.0 · library 0.150.0.

**The second of the three rename windows of W3.1-bis (TODO entry W3.1-bis.5):
the protocol of the bridge is English.** The seven tools and their
sub-actions, the 99 operations and their 12 families, every argument and the
value of every argument whose schema lists one, the selectors, the fields of
every answer and the value of every response field the map covers, the keys
of `Strumenti/catalogo.json`, the MCP resources and the tool titles get the
English names of the name map (`Controlli/lingua/name-map.tsv`, W3.1-bis.3),
and so do the bench cases that speak that protocol, the readers of the
catalogue and every code name quoted in a message, a comment or a document.
The texts themselves (descriptions, messages, comments) stay in the language
they were written in: only the names inside them change. Classes, files and
folders of the code are window 3 (W3.1-bis.6). A few values outside any enum
(`mate`'s `variant` and `alignment`, the `kind` of a mate, a drawing view, a
selection or `check_body_geometry` in the state) are not in the map yet and
stay Italian: W3.1-bis.8.

### The script of the window

- **`Controlli/lingua/window-2-surface.js`**, driven by the map (types tool,
  sub_action, operation, catalogue_family, argument, enum_value,
  catalogue_key, response_field, response_value, selector), in one pass over
  every tracked text file but the excluded ones (this CHANGELOG, the map and its
  extractor, the gate: `detect-italian.js`, its lexicon, its exceptions and
  baseline, `language-gate.ps1`; window 1's script):
  - the catalogue token by token: keys, families, operation names, schema
    properties, enums, `required`, the examples, and the code names in the
    descriptions; `Riscontro.Disegno/Catalogo/Operazioni.cs` gets the same
    renames, so the catalogue the library exports is byte for byte the one the
    script writes;
  - every case and plan: operations, arguments (by the operation of the step),
    values (by the enum of the argument), selectors
    (`@faccia(verso=davanti)` → `@face(normal=front)`), the paths and the
    values of the expectations, the pieces a message must contain, and the
    code names in the free notes;
  - the string literals of the C#, JavaScript and PowerShell sources by their
    code context (an argument read with `Str(a, "…")`, a field written with
    `misure["…"]`, a step of a plan, a key of an answer), with a table of
    sites (`CS_SITES`) for the words that have two senses in the map (`a_mm` is
    `throat_mm` in `drawing_weld_symbol` and `offset_mm` in `section_view`;
    `formato` is `paper_size` for `create_drawing` and `set_sheet`,
    `sheet_format` for `title_block`); every row of the table must be used by
    the run, or it stops;
  - the code names in messages, comments and documents: only between
    backticks, guillemets or quotes, as a whole snake_case token, a selector or
    a resource URI. **A common word is never replaced inside prose**
    («la quota», «il foro» stay as they are), and the names an Italian SOLIDWORKS
    gives (`Taglio-Estrusione1`, «Piano frontale»; class
    `solidworks-localized` of `exceptions.json`) are never touched.
- JSON is rewritten token by token: indentation, line ends, key order and the
  hand-made layout of every case stay byte for byte.
- `--reverse` runs the window backwards with the ledger the forward run writes
  (`Controlli/lingua/window-2-reverse.json`, 122 entries in 34 files): the words
  the map read backwards would not give back, a name that was already English
  before the window (`feature`, `faces`, `orientation`) or a form the map merges with another.
  `--reverse-proof <rev>` and `--forward-proof <rev>` compare the JSON files of
  the window (catalogue, cases, plans, tasks) with another revision; `--check`
  says whether the tree is after the window (it is in the snapshot registry);
  `--todo` and `--notes` rename the code names of notes outside the repository.
  A forward run on a tree that is already after the window stops (it would
  read the English names as the names before it): `--check` is the second run.
- **`Controlli/lingua/update-names-in-notes.js`** drives `--notes` and `--todo`
  on explicit paths (the TODO, the CLAUDE.md files, `INSTALL-FOR-AI.md` copies,
  the memories, the RAG notes), with `--dry-run`, `--list` and `--self-test`.
  It touches code names only (decision D10).
- `snapshot.js compare --window 2` reads a snapshot taken before the window
  through its renames (operations, paths and values of the assertions, the
  catalogue, the tools), so a renamed name is the same one, not one gone and
  one new.

### What changed

606 files rewritten by the script, 431 of them the cases and plans of the
bench: 1374 operations, 695 catalogue keys,
1078 argument blocks and 394 plan keys, 1425 expectation paths, 113
expectation values, 43 message pieces, 281 free notes of cases, 4356 string
literals of the sources, 971 messages and 1011 comments that quote a code name.

- **The readers of the catalogue in the same commit**:
  `Controlli/conta-provate.js` and `Controlli/controlla-catalogo.ps1` read
  `operations`, `families`, `name`, `family`; the bridge, the bench, the
  contracts and `Controlli/expected-keys-test.js` read the English keys too.
  Same counts as the snapshot of 1/10/2026.
- **The resources**: `catalog://operations` and `catalog://operation/{name}`
  (were `catalogo://operazioni`, `catalogo://operazione/{nome}`).
- **The tool titles**: «Inspect the open part», «List the operations»,
  «Describe an operation», «Build in the part», «Measure the part, without
  touching it», «Withdraw the last plan», «Open, create or pick the document».
- **The arguments the bench writes in place**: an initializer reads as an
  answer unless the line says otherwise, and where the map gives the field and
  the argument different names the site table says which one the line is:
  `paper_size` for `create_drawing` (`Banco/NumeriDelleQuote.cs`,
  `Banco/Orfane.cs`), `variant` for `hole_wizard` (`Banco/Program.cs`,
  `Banco/ProvaNuove.cs`), `measured_as` and the segment ends `start`/`end`
  inside `rectangular_frame` (`Banco/Program.cs`). The library echoes the
  paper size as `paper_size` (`create_drawing`, `set_sheet`) and the sheet
  format of `title_block` as `sheet_format` (the notes of the map).
- **The facings**: the face a selector or a mate names by its normal
  (`up`, `down`, `right`, `left`, `front`, `back`), in the selectors, in the
  `facing` field of the state, in `face_a`/`face_b` of `mate` and in the bench
  (`Banco/Attese.cs` maps `normal` to `facing`).
- **D12, the examples of the catalogue with a SOLIDWORKS name**: they were
  copied from cases run on an Italian installation (`Taglio-Estrusione1`,
  `Svuota1`, `Foglio1`, `D1@Schizzo2`); they now carry the name an English
  installation gives (`Cut-Extrude1`, `Shell1`, `Sheet1`, `D1@Sketch2`), and
  the description of the argument says that the name is the installation's, in
  its language, to be copied from the state (8 arguments of 8 operations).
- The README section «Language» is rewritten: what is English, what comes from
  SOLIDWORKS in the language of the installation, and the aliases below.

### The Italian names, accepted on input until the first release (decision D4)

- **`Riscontro.Disegno/Alias.cs`**: the dictionary is the name map itself,
  embedded in the library (`Riscontro.Disegno.csproj`, `name-map.tsv`), so the
  rows that renamed the surface are the rows that read the old names. A key is
  translated only to a name the schema of the operation has at that level (the
  two senses of `a_mm`: `throat_mm` in `drawing_weld_symbol`, `offset_mm` in
  `section_view`), a value only to a value the schema lists, a selector field
  by field (`normal=front`). Input only: the answers, the catalogue,
  `describe_operation` and reflection speak English.
- **Where**: `OperazioniSw.Esegui` translates a step for the callers of the
  library that do not come through the bridge (the bench, the add-in) and
  writes `measurements.deprecated`; the switch moved to
  `ExecuteWithCurrentNames`, and `Controlli/controlla-catalogo.ps1` reads the
  `case` labels there. The bridge translates in `Attrezzi.Chiama`, before any
  of its checks: the tool, its arguments, the action of `document`, the family
  of `list_operations`, the operation of `describe_operation`, the steps of a
  plan with their arguments; the answer carries the lines in
  `structuredContent.deprecated` and as its last text item. The bench reads
  its selectors through the same dictionary (`Attese.SelectorAlias`) and adds
  them to the step's `measurements.deprecated`.
- **How it warns**: one line per name, «old» is deprecated, use «new» (the
  Italian names are accepted until the first release).
- **The bench**: `Banco/cases/aliases/`, one case per kind of name (an
  operation, an argument, a value, a selector), each checking the part and the
  deprecated line. The window and its proofs leave the folder alone, and
  `conta-provate.js` does not count it (it exercises the dictionary, not an
  operation).
- **The contract** `AliasLayerContract` (14 checks): the library by reflection
  (an operation step, its arguments and value, an English step left alone,
  `a_mm` in its two operations, a value the schema does not list, a selector)
  and the bridge over stdio with a dry run (an Italian tool, plan and step give
  the dry run of the English plan and list all ten old names; an Italian
  family and operation are answered in English only). The Italian names come
  from the map, not from the contract. **Seen red** with the map not embedded:
  11 of the 14 fail (the Italian tool is refused as unknown).
- **The bench counts them**: every step whose answer carries `deprecated` adds
  its lines to its case, and the run ends with a line
  `alias uses: N in M cases; outside the alias cases: K` that `banco.ps1` writes into `last.json` as
  `alias_uses`. K must be 0: a case of before the rename would otherwise hide
  behind the aliases until they go. `Alias.cs` and its contract are excluded
  from the window, which would otherwise rename the old names they quote.
- **Removed in W3.1-bis.11**, before the tag: `Alias.cs`, the embedded map, the
  calls in `OperazioniSw.Esegui`, `Attrezzi.Chiama` and `Attese`, the cases of
  `Banco/cases/aliases`, the contract.

### The tasks of the autonomous run (D9)

- **The measured fields are real**: the tables of `Banco/tasks/*.md` named 14
  fields no operation writes (`holes` for every way of making a hole,
  `holes_on_circle`, `slots`, `hems`, `cut_list_rows`, the three
  `envelope_*`, `mirrored`, `bom_quantity_total`, `cut_list_matches`,
  `geometry_check_ok`, `export_succeeded`, `bodies_in_file`, `volume_mismatch`)
  and six more a field under another name (`interferences` is the list, the
  count is `how_many`; `balloons_placed`; `length_mm` and `width_mm` that no
  sheet-metal step writes). 20 rows are rewritten on what the bridge and the
  bench do write (`diameters_mm`, `bounding_box_mm`, `measurements.<field>`
  of a named step).
- **`Banco/tasks/run-autonomous.ps1`** hands each task to `claude -p` with the
  bridge as the only MCP server and keeps the transcript and the state of the
  finished part; **`Banco/tasks/score-autonomous.js`** scores RIGHT, WRONG or
  NOT DONE per session, the median per task, and `--compare` is the threshold
  of D9 (at most one task of fifteen worse). Self-test without SOLIDWORKS; the
  run itself needs SOLIDWORKS and has not been made. The commands are in
  `Banco/tasks/README.md`. The Italian version of the tasks is not in the
  repository.

### The renames (old → new)

The full list is the map (`Controlli/lingua/name-map.tsv`): 205 arguments and
689 answer fields are not repeated here.

**Tools** (7)

| Italian | English | Italian | English |
|---|---|---|---|
| `annulla` | `undo` | `descrivi_operazione` | `describe_operation` |
| `disegna` | `build` | `documento` | `document` |
| `elenco_operazioni` | `list_operations` | `guarda_il_pezzo` | `inspect_part` |
| `misura` | `measure` | | |

**Sub-actions** (5)

| Italian | English | Italian | English |
|---|---|---|---|
| `apri` | `open` | `attiva` | `activate` |
| `chiudi` | `close` | `elenca` | `list` |
| `nuovo` | `create` | | |

**Operations** (99)

| Italian | English | Italian | English | Italian | English |
|---|---|---|---|---|---|
| `accoppia` | `mate` | `applica_materiale` | `apply_material` | `asola` | `slot` |
| `attiva_configurazione` | `activate_configuration` | `attiva_foglio` | `activate_sheet` | `bollatura` | `balloons` |
| `cambia_estrusione` | `edit_extrusion` | `cartiglio` | `title_block` | `combina_corpi` | `combine_bodies` |
| `cordone_saldatura` | `weld_bead` | `crea_asse` | `create_axis` | `crea_configurazione` | `create_configuration` |
| `crea_disegno` | `create_drawing` | `crea_piano` | `create_plane` | `croci_di_centro` | `center_marks` |
| `datum_in_tavola` | `datum` | `dettaglio_in_tavola` | `detail_view` | `dipendenze` | `file_dependencies` |
| `distanza_minima` | `minimum_distance` | `distinta_base_in_tavola` | `drawing_bom` | `distinta_taglio` | `cut_list` |
| `distinta_taglio_in_tavola` | `drawing_cut_list` | `elimina_lavorazione` | `delete_feature` | `esporta_iges` | `export_iges` |
| `esporta_pdf` | `export_pdf` | `esporta_step` | `export_step` | `esporta_stl` | `export_stl` |
| `estrudi_cerchio` | `extrude_circle` | `estrudi_profilo` | `extrude_profile` | `estrudi_rettangolo` | `extrude_rectangle` |
| `estrudi_sezione` | `extrude_section` | `estrudi_su_faccia` | `extrude_on_face` | `fazzoletto` | `gusset` |
| `filettatura_cosmetica` | `cosmetic_thread` | `fissa_componente` | `fix_component` | `flangia_base` | `base_flange` |
| `flangia_spigolo` | `edge_flange` | `foro_guidato` | `hole_wizard` | `guscio` | `shell` |
| `immagine_nello_schizzo` | `sketch_picture` | `imposta_foglio` | `set_sheet` | `incidi_testo` | `engrave_text` |
| `ingombro_corpo` | `body_bounding_box` | `inserisci_componente` | `insert_component` | `lega_quota` | `link_dimension` |
| `loft_fra_due_cerchi` | `loft_two_circles` | `membro_strutturale` | `structural_member` | `misura_fra_entita` | `measure_between` |
| `modifica_quota` | `set_dimension` | `nervatura` | `rib` | `nota_in_tavola` | `note` |
| `nuovo_foglio` | `new_sheet` | `orlo` | `hem` | `piano_per_tre_punti` | `plane_three_points` |
| `piega_da_schizzo` | `sketched_bend` | `profili_saldatura` | `weldment_profiles` | `proprieta_di_massa` | `mass_properties` |
| `quota_in_tavola` | `drawing_dimension` | `quote_dal_modello` | `model_dimensions` | `raccordo_faccia` | `fillet_face` |
| `raccordo_spigoli` | `fillet_edges` | `raccordo_variabile` | `variable_fillet` | `riordina_lavorazione` | `reorder_feature` |
| `ripeti_circolare` | `circular_pattern` | `ripeti_lineare` | `linear_pattern` | `ripeti_riempimento` | `fill_pattern` |
| `rivoluzione` | `revolve` | `rugosita_in_tavola` | `surface_finish` | `ruota_dentata` | `spur_gear` |
| `saldatura_in_tavola` | `drawing_weld_symbol` | `salva_documento` | `save_document` | `schema_di_quote` | `dimension_scheme` |
| `scrivi_proprieta` | `set_property` | `sezione_in_tavola` | `section_view` | `sformo` | `draft` |
| `smusso_spigoli` | `chamfer_edges` | `sopprimi_istanza` | `suppress_instance` | `sopprimi_lavorazione` | `suppress_feature` |
| `sostituisci_componente` | `replace_component` | `specchia` | `mirror` | `specchia_componente` | `mirror_component` |
| `sposta_corpi` | `move_bodies` | `sviluppo_dxf` | `flat_pattern_dxf` | `sweep_circolare` | `circular_sweep` |
| `tabella_fori_in_tavola` | `hole_table` | `taglia_membri` | `trim_members` | `taglio_cilindrico` | `cylindrical_cut` |
| `taglio_rettangolare` | `rectangular_cut` | `tappo_estremita` | `end_cap` | `telaio_rettangolare` | `rectangular_frame` |
| `tolleranza_geometrica_in_tavola` | `geometric_tolerance` | `tolleranza_quota` | `dimension_tolerance` | `variabile_globale` | `global_variable` |
| `verifica_geometria_corpo` | `check_body_geometry` | `verifica_interferenze` | `interference_check` | `verifica_sovrasporgenze` | `check_overhangs` |
| `vista_modello` | `model_view` | `vista_sviluppo` | `flat_pattern_view` | `viste_standard` | `standard_views` |

**Catalogue families** (12)

| Italian | English | Italian | English |
|---|---|---|---|
| `assiemi` | `assemblies` | `esportazioni` | `exports` |
| `estrusioni` | `extrusions` | `identita` | `identity` |
| `lamiera` | `sheet_metal` | `lavorazioni` | `features` |
| `proprieta` | `physical_properties` | `quote` | `dimensions` |
| `riferimenti` | `references` | `saldature` | `weldments` |
| `tavola` | `drawing` | `verifiche` | `checks` |

**Catalogue keys** (9)

| Italian | English | Italian | English | Italian | English |
|---|---|---|---|---|---|
| `descrizione` | `details` | `documento` | `document` | `esempio` | `example` |
| `famiglia` | `family` | `famiglie` | `families` | `nome` | `name` |
| `operazioni` | `operations` | `precondizione` | `precondition` | `sommario` | `summary` |

**Selectors** (20)

| Italian | English | Italian | English |
|---|---|---|---|
| `@corpo` | `@body` | `@faccia` | `@face` |
| `@quota` | `@dimension` | `@spigolo` | `@edge` |
| `angolare` | `angular` | `asse` | `axis` |
| `curvo` | `curved` | `diametro_mm` | `diameter_mm` |
| `lungo` | `length` | `nome` | `name` |
| `piana` | `planar` | `raggio_mm` | `radius_mm` |
| `tipo` | `kind` | `verso` | `normal` |
| `verso=alto` | `normal=up` | `verso=basso` | `normal=down` |
| `verso=davanti` | `normal=front` | `verso=destra` | `normal=right` |
| `verso=dietro` | `normal=back` | `verso=sinistra` | `normal=left` |

**Enum values (argument=value)** (128)

| Italian | English | Italian | English |
|---|---|---|---|
| `caratteristica=cilindricita` | `characteristic=cylindricity` | `caratteristica=circolarita` | `characteristic=circularity` |
| `caratteristica=concentricita` | `characteristic=concentricity` | `caratteristica=inclinazione` | `characteristic=angularity` |
| `caratteristica=oscillazione_circolare` | `characteristic=circular_runout` | `caratteristica=oscillazione_totale` | `characteristic=total_runout` |
| `caratteristica=parallelismo` | `characteristic=parallelism` | `caratteristica=perpendicolarita` | `characteristic=perpendicularity` |
| `caratteristica=planarita` | `characteristic=flatness` | `caratteristica=posizione` | `characteristic=position` |
| `caratteristica=profilo_linea` | `characteristic=profile_of_line` | `caratteristica=profilo_superficie` | `characteristic=profile_of_surface` |
| `caratteristica=rettilineita` | `characteristic=straightness` | `caratteristica=simmetria` | `characteristic=symmetry` |
| `condizione=quartabuono` | `end_condition=miter` | `condizione=taglio` | `end_condition=cut` |
| `condizione=testa1` | `end_condition=butt1` | `condizione=testa2` | `end_condition=butt2` |
| `disposizione=cerchio` | `layout=circle` | `disposizione=quadrato` | `layout=square` |
| `disposizione=sopra` | `layout=above` | `disposizione=sotto` | `layout=below` |
| `documento=assieme` | `document=assembly` | `documento=nessuno` | `document=none` |
| `documento=parte` | `document=part` | `documento=parte/assieme` | `document=part/assembly` |
| `documento=qualunque` | `document=any` | `documento=tavola` | `document=drawing` |
| `dove=dopo` | `placement=after` | `dove=prima` | `placement=before` |
| `genere=asse` | `kind=axis` | `genere=corpi` | `kind=bodies` |
| `genere=estremita_libere` | `kind=free_ends` | `genere=faccia_del_foro` | `kind=hole_face` |
| `genere=faccia_di_fondo` | `kind=pocket_floor` | `genere=faccia_inferiore` | `kind=bottom_face` |
| `genere=faccia_superiore` | `kind=top_face` | `genere=spigoli_inferiori` | `kind=bottom_edges` |
| `genere=spigoli_laterali` | `kind=side_edges` | `genere=spigoli_superiori` | `kind=top_edges` |
| `genere=tutti_gli_spigoli` | `kind=all_edges` | `lato=alto` | `side=top` |
| `lato=basso` | `side=bottom` | `lato=destra` | `side=right` |
| `lato=sinistra` | `side=left` | `lavorazione=asportazione` | `process=material_removal` |
| `lavorazione=qualsiasi` | `process=any` | `lavorazione=senza_asportazione` | `process=no_removal` |
| `misure=ingombro` | `measured_as=overall` | `misure=interasse` | `measured_as=center_distance` |
| `modo=istanza` | `mode=instance` | `modo=opposto_legato` | `mode=opposite_linked` |
| `modo=opposto_slegato` | `mode=opposite_unlinked` | `operazione=interseca` | `operation=intersect` |
| `operazione=sottrai` | `operation=subtract` | `operazione=unisci` | `operation=union` |
| `origine=alto_destra` | `origin=top_right` | `origine=alto_sinistra` | `origin=top_left` |
| `origine=basso_destra` | `origin=bottom_right` | `origine=basso_sinistra` | `origin=bottom_left` |
| `piano=destro` | `plane=right` | `piano=frontale` | `plane=front` |
| `piano=superiore` | `plane=top` | `posizione=centro` | `position=center` |
| `posizione=fine` | `position=end` | `posizione=inizio` | `position=start` |
| `precondizione=corpo_solido` | `precondition=solid_body` | `precondizione=corpo_solido_e_faccia` | `precondition=solid_body_and_face` |
| `precondizione=corpo_solido_e_spigolo` | `precondition=solid_body_and_edge` | `precondizione=documento_senza_corpo` | `precondition=document_without_body` |
| `precondizione=feature_e_direzione` | `precondition=feature_and_direction` | `profilo=poligonale` | `profile=polygonal` |
| `profilo=triangolare` | `profile=triangular` | `qualita=grossolana` | `quality=coarse` |
| `rappresentazione=solidi` | `representation=solids` | `rappresentazione=superfici` | `representation=surfaces` |
| `su=fori` | `applies_to=holes` | `tipo=accoppiamento` | `variant=fit` |
| `tipo=angolare` | `variant=angle` | `tipo=angolo` | `variant=fillet` |
| `tipo=aperto` | `variant=open` | `tipo=asola` | `variant=slot` |
| `tipo=assieme` | `variant=assembly` | `tipo=bilaterale` | `variant=bilateral` |
| `tipo=chiuso` | `variant=closed` | `tipo=esagono` | `variant=hexagon` |
| `tipo=filettato` | `variant=threaded` | `tipo=indentata` | `variant=indented` |
| `tipo=lamato` | `variant=counterbore` | `tipo=mezza_v` | `variant=half_v` |
| `tipo=nessuna` | `variant=none` | `tipo=parte` | `variant=part` |
| `tipo=passaggio` | `variant=clearance` | `tipo=piatto` | `variant=flat` |
| `tipo=primo_livello` | `variant=top_level` | `tipo=simmetrica` | `variant=symmetric` |
| `tipo=solo_parti` | `variant=parts_only` | `tipo=svasato` | `variant=countersink` |
| `tipo=testa` | `variant=butt` | `tipo=tondo` | `variant=round` |
| `tipo=tubo` | `variant=tube` | `tipo=tubolare` | `variant=tubular` |
| `tolleranza=geometrica` | `tolerance=geometric` | `tolleranza=piu_meno` | `tolerance=plus_minus` |
| `trattamento_angolo=nessuno` | `corner_treatment=none` | `trattamento_angolo=quartabuono` | `corner_treatment=miter` |
| `trattamento_angolo=testa1` | `corner_treatment=butt1` | `trattamento_angolo=testa2` | `corner_treatment=butt2` |
| `verso=orizzontale` | `orientation=horizontal` | `verso=verticale` | `orientation=vertical` |
| `vista=destra` | `view=right` | `vista=dietro` | `view=back` |
| `vista=dimetrica` | `view=dimetric` | `vista=fronte` | `view=front` |
| `vista=isometrica` | `view=isometric` | `vista=sinistra` | `view=left` |
| `vista=sopra` | `view=top` | `vista=sotto` | `view=bottom` |
| `vista=trimetrica` | `view=trimetric` | `viste=destra` | `views=right` |
| `viste=dimetrica` | `views=dimetric` | `viste=frontale` | `views=front` |
| `viste=inferiore` | `views=bottom` | `viste=isometrica` | `views=isometric` |
| `viste=posteriore` | `views=back` | `viste=sinistra` | `views=left` |
| `viste=superiore` | `views=top` | `viste=trimetrica` | `views=trimetric` |

**Response values** (48)

| Italian | English | Italian | English | Italian | English |
|---|---|---|---|---|---|
| `Catalogo delle operazioni` | `Operation catalog` | `Destra` | `Right` | `Montaggio` | `Assembly` |
| `Revisione` | `Revision` | `Sinistra` | `Left` | `Una operazione` | `Single operation` |
| `assieme` | `assembly` | `assieme illeggibile` | `unreadable assembly` | `binario` | `binary` |
| `centro` | `center` | `cerchio` | `circle` | `chiesta` | `requested` |
| `chiuso` | `closed` | `cieca` | `blind` | `completamente definito` | `fully defined` |
| `composito` | `composite` | `diametro del tondo` | `round diameter` | `diametro esterno` | `outer diameter` |
| `documento` | `document` | `esterne` | `outer` | `esterno` | `outer` |
| `estrusione` | `extrusion` | `frontale` | `front` | `il documento` | `the document` |
| `ingombro` | `overall` | `interne` | `inner` | `interno` | `inner` |
| `linee` | `lines` | `lunghezza del tondo` | `round length` | `millimetri e chilogrammi` | `millimeter kilogram` |
| `nessuno` | `none` | `non disponibile` | `unavailable` | `orizzontale` | `horizontal` |
| `passante` | `through_all` | `piano medio` | `mid plane` | `poligonale` | `polygonal` |
| `primo` | `first` | `quartabuono` | `miter` | `quota da smusso` | `chamfer dimension` |
| `solidi` | `solids` | `solido` | `solid` | `taglio` | `cut` |
| `tavola` | `drawing` | `tavola illeggibile` | `unreadable drawing` | `terzo` | `third` |
| `triangolare` | `triangular` | `unisci` | `merge` | `verticale` | `vertical` |

## Part of 0.130.1 — W3.1-bis.4, rename window 1: the bench cases (branch w31bis-4)

**Bench, 2/10/2026, on e49d4d6 (W3.1-bis.3, Z-C21.14 and this window together):
2179 tests, 0 failed, 1 skipped (`--lento`), expectations 1771 of 1771.** Three
runs before it did not count, for causes outside the code: run 31 on a
SOLIDWORKS session of ~4,900 documents (window resources exhausted: rebuilds
skipped in silence), run 32 killed by a network-drive hiccup in Tee-Object
(c008f7a writes the live output locally), run 33 on a 7-hour session (centre
marks and dimensions not inserted; the same families green on a restarted
SOLIDWORKS). e49d4d6 restarts SOLIDWORKS before a full run.

**The first of the three rename windows of W3.1-bis (TODO entry W3.1-bis.4):
the cases of the bench get English keys, English names, English folders and
English files, generated from the name map of W3.1-bis.3.** The bench and its
loader change; the bridge and the library do not: their sources change only in
comments that cite a case or a bench path (22 files), and `same-il.ps1 -Mode
Identical` gives 0 differing lines for `Riscontro.Disegno.dll`,
`oce-solid-agent-mcp.exe` and `Riscontro.Catalogo.exe` against 1ccb18a, and
`Strumenti/catalogo.json` is rebuilt byte for byte the same. `VERSION.txt` and
the library version are therefore not raised (the practice of W3.1-bis.2, which
changed the bench loader the same way).

### The script of the window

- **`Controlli/lingua/window-1-cases.js`**, driven by `Controlli/lingua/name-map.tsv`
  (types case_key, free_case_key, case_family, case_name, folder, file,
  summary_key). In order: `git mv` of the folders and files of the bench this
  window owns (`Banco/casi`, `Banco/casi-in-attesa`, `Banco/compiti`,
  `Banco/resoconto`; `Banco/` itself and its root files are window 3, .6);
  the keys of every case, walked through the contexts of `Banco/case-keys.json`
  so that an argument name is never taken for a case key; the free notes to the
  `_` convention; the family, the case name and the twin; every case name and
  bench path inside a value (the component files of an assembly case are named
  after the case, `{LAVORO}\<case>_<component>`); `Banco/case-keys.json`; the
  keys of the bench summary; and the bench paths, case names and
  `--famiglia <folder>` values in every other tracked text file, except the
  CHANGELOG (history), the map and its extractor (Italian on purpose) and the
  language baseline. **Operations, arguments and selectors inside the cases
  stay as they are**: they are the MCP surface, window 2 (.5). A plan (a file
  whose root is a list) is the bridge's protocol: only its free notes and the
  names inside its values change.
- JSON is rewritten token by token: only the strings that change are replaced,
  so indentation, line ends, the trailing newline, the key order and the
  hand-made layout of each case stay byte for byte.
- `--reverse` runs the window backwards. `--reverse-proof <rev>` extracts HEAD
  into a temporary folder, runs `--reverse` there and compares the bench files
  of `<rev>` with what came back. `--check` writes nothing and says whether the
  tree is after the window (it is in the snapshot registry). `--todo <file>`
  rewrites the bench paths, case names and `--famiglia` values of a TODO
  outside its closed entries (the rule of `percorsi-del-todo.js`). `--step
  moves|content` splits the window in two commits, so that git sees every move
  as an exact rename. A second run changes nothing (also on its own sources: a
  line that must name both the old and the new path carries the marker
  `window-1: keep` and is never rewritten, and comments say "its Italian
  folder" instead of quoting the old path).
- `--load-proof <rev>` writes nothing: it reads the cases of `<rev>` and of the
  tree the way `Banco/CasiDichiarati.cs` reads them (root array = plan, root
  object = case, declared name = file name, keys walked through each tree's
  own `case-keys.json`) and compares them position by position, with its own
  rules rather than the rewriting code: same shape, a key changes only as the
  map says and only where the loader walks, the loader's child context of
  every key is the same, a string changes only by renamed names of this
  window. It also prints how the run order changes. Seen failing on a tampered
  case (an argument key renamed, a word added to a `why`).
- `--check` also looks for code (`.js`, `.cs`, `.ps1`) that names an Italian
  free note: step 5 rewrites paths and case names in the code, not free notes,
  and `Banco/raccogli_viste.js` kept reading `_verita` after the plans of
  `Banco/cases/views/` said `_truth` (it wrote "not declared" in silence;
  it reads `_truth` now). Seen failing on the old reader: 2 lines.
- `--todo` treats a `(com'era)` section (the original wording of an entry,
  kept for reference) as history like a closed entry, and so does
  `Controlli/percorsi-del-todo.js`: before, the F3 example of the TODO came out
  half old and half new.
- A case folder followed by a glob of its cases (`ponte/c2_*`) is a bench path
  too: the folder changes, the glob stays (2 cases, found by `--load-proof`).
- Two small tables in the script carry what the reverse run needs and the map
  does not hold, checked against the tree by every forward run:
  `ENGLISH_FROZEN_FREE` (the seven frozen free notes already written in English;
  `the_numbers` becomes `_the_numbers` like the Italian note with the same
  meaning) and `ENGLISH_CASE_KEYS` (`name` of an assembly configuration, which
  was English while the case's own name key becomes `name` too).

### What changed in the cases

- 304 case files and 127 plans (431 JSON files): 11161 case keys renamed by
  context, 327 free notes, 304 families, 262 declared case names, 136 twin
  references (115 of them to a renamed case), 806 case names inside values (the
  262 names and the 115 twins among them) and 7 bench paths, 3 component files
  named after their case.
- **The frozen list of free keys is empty.** The 184 frozen notes of
  30/9/2026 (182 keys) start with `_` and are admitted everywhere without being
  read; `FrozenFreeCeiling` is 0. `CaseKeysTests` keeps every check: the one that
  showed that the context decides now uses a documented key of the case root
  (`defect_caught`) where it used a frozen note.
- `Banco/report/last.json` (was `Banco/resoconto/ultimo.json`): its keys are
  the English summary keys of the map (`timestamp`, `library`, `catalog`,
  `runnable`, `in_catalog`, `batch`, `family`, `slow`, `duration_s`,
  `summary`, `expectations`, `passed`, `failed`, `skipped`, `errors`,
  `exit_code`, `red`, `bench_errors`, `green_cases`), written so by
  `Banco/banco.ps1` and read so by `ProveIlBanco` and `ProveILimiti`.

### The readers, renamed in the same commit

- The loader: `Banco/CasiDichiarati.cs`, `Banco/Attese.cs`, `Banco/AssiemeDiProva.cs`,
  `Banco/Regressione.cs` read the English keys, and their refusals name them;
  the case folders are `Banco/cases/<folder>` (the list `Famiglie`, hence the
  values of `--famiglia` and `-Famiglia`, are the English folder names), the
  regression folder is `regression`, the report folder `Banco/report`. No alias:
  a case with an Italian key is refused like any unknown key. C# identifiers are
  not renamed (window 3).
- The contracts: `CaseKeysTests`, `ProveAttese`, `ProveILimiti`, `ProveIlBanco`,
  `Programma` (the folder table of `Banco/LEGGIMI.md`, the folders the bench
  walks), `ObsoleteCallsContract` (the cases its OBSOLETE-MEASURED markers cite),
  `SelectionMarksContract`, `Zc2189WeldAssemblyContract`.
- The checks: `Controlli/conta-provate.js` (the keys of a case; a plan step keeps
  `operazione`), `Controlli/expected-keys-test.js` (the fake cases, whose wrong
  keys are now the names of before the window), `Controlli/percorsi-del-todo.js`
  (`Banco/cases` among the folders the TODO writes from, and the report of a run,
  `AAAAMMGG_hhmm.md`, a name made at run time and not a file),
  `Controlli/lingua/snapshot.js` (layout and case keys; the assertion kinds keep
  their names) and `Controlli/lingua/name-map-extract.js` (layout and case keys;
  a plan step is read with the protocol keys; the map is not touched and
  `--check` passes). `Controlli/rimandi-del-todo.js` and
  `Controlli/controlla-catalogo.ps1` name no bench path and are unchanged.
- **`snapshot.js compare --window 1`**: a snapshot taken before this window is
  read through its renames (case folders, case names, case keys, the test
  identities `File.cs:Method:ordinal@<case>`), with the script of the window
  doing the reading, so that the comparison of the next full bench with the
  snapshot of 1/10/2026 matches a renamed case with itself.
- The language gate: the `data-becomes-geometry` exception of the engraved word
  names `Banco/cases/text/` (it covered both paths for the commit of the moves).
- **The freshness check of the bench (`ProveIlBanco`) was fooled by the window.**
  It skipped every commit that touched `Banco/report/last.json` as "a delivery
  with its run": the move and the key rewrite of this window touch it and run
  nothing, and the rewrite also changes the loader, every case and 22 comments
  of the library; and the historical deliveries, which wrote the old path, were
  no longer recognised. A commit carries a run now only when the run's
  timestamp (`timestamp`, or `quando` on the old path) differs between the
  commit and its parent, read from whichever path exists. Seven new checks,
  made to fail on purpose with the real shapes of the two commits, and one on
  the real history (the move commit is not a delivery). The check now says
  `ultima modifica 2026-10-01T17:49:41 (commit)` for the operations, the
  rewrite, where it said 1ccb18a before.

### The language gate, and the one thing that grows

Three commits: the exception above widened (signed
`Language-Gate-Config-Change`), the moves alone with the script and this entry
(nothing grows: every move is an exact rename, compared with its old path), and
the rewrite. The rewrite lowers every count (json_keys 443 → 219, protocol
2170 → 1878, comments, strings and markdown down) but adds **two names to the
protocol set**, and carries the trailer `Language-Baseline-Raise` for them:
`inserting_is_not_mating_coperchio` and
`rebuilt_cover_keeps_the_constraints_but_broken_coperchio2`, the files the
bench writes for a component of an assembly case, named
`{LAVORO}\<case>_<component>` by the loader. The case name is English now; the
component name (`coperchio`) is a value of the case, translated with the other
values in W3.1-bis.8. The same commit narrows the exception and rewrites two
comment lines of `.github/workflows/prove.yml` (a path), and carries
`Language-Gate-Config-Change` too.

### Proved, and not proved here

- The inverse identity: `--reverse-proof 1ccb18a` on the new tree gives back
  every path and every byte of the cases, plans, case keys, summary and tasks of
  1ccb18a (the tree of the snapshot of 1/10/2026 for those files). On a copy of
  the whole tree the reverse run gives back every tracked file byte for byte.
- The cases load the same before and after: `window-1-cases.js --load-proof
  1ccb18a` (431 of 431 files paired, 1001 steps and 1836 expectations, 0 keys
  changed inside values, 0 differences), `--check`, and the contracts (785
  checks, the only reds the library version 0.148 against 0.149 and the bench
  freshness, which needs the bench run at the merge).
- 136 twin references in 121 cases, all resolving, before and after.
- **The bench has NOT run on this branch** (a full bench was running on the main
  checkout): the bench and
  `snapshot.js compare --window 1 --ledger Banco/report/tests.json` are to be run at the merge.
- The untracked outputs of the bench stay where they are on a checkout: move
  `Banco/resoconto/*` (but `ultimo.json`, which git moves) into `Banco/report/`
  and `Banco/casi/forma/` into `Banco/cases/forma/` once, or `.gitignore` will
  show them and `prova_forma.js` will not find its plans. The pieces of
  `Banco/viste/` carry the Italian shape names as their prefix: rename it to
  the English shape name (`1_drilled_plate`, `2_open_shell`, `3_l_bracket`,
  `4_drilled_flange`, `5_stepped_shaft`) or run `node Banco/raccogli_viste.js`
  again, or `prova_forma.js` finds no true piece.

### What to watch in the first bench after the merge

- **The run order changes for every case.** The loader sorts the files of all
  families together by file name (`Banco/CasiDichiarati.cs`), so renaming the
  files reshuffles the run: of the 291 cases of the loader's families, 5 keep
  their position and 62 keep the case before them (`--load-proof` prints it).
  `{LAVORO}` is emptied once per run, not per case, some cases share files,
  and SOLIDWORKS carries state from one case to the next. A case that changes
  verdict may do so for the order, not for the rename: rerun its folder alone
  (`.\Banco\banco.ps1 -Famiglia <folder>`) before blaming the map.
- Two cases keep the prefix `lim_` (`Banco/cases/export/lim_step_ap242_needs_the_mbd_licence.json`,
  `lim_step_ap242_wants_a_stp_path.json`): their names were already English and
  are not in the map, which this window does not rewrite. No code reads the
  prefix; two `case_name` rows belong to W3.1-bis.5.
- Open, outside this window: `Banco/tasks/*.json` still write the expectation
  keys of before (`cosa`, `uguale`, `tolleranza`, `perche`). No code reads them
  today; the runner of the tasks (entry Z-G9) will hand them to `Attese`, which
  reads `what`. Point 4 left open by W3.1-bis.3: to decide before Z-G9.

### The renames (old → new)

**Folders** (33):

| old | new |
|---|---|
| `Banco/casi/` | `Banco/cases/` |
| `Banco/casi-in-attesa/` | `Banco/pending-cases/` |
| `Banco/casi/asola/` | `Banco/cases/slot/` |
| `Banco/casi/asse/` | `Banco/cases/axis/` |
| `Banco/casi/assiemi/` | `Banco/cases/assemblies/` |
| `Banco/casi/coppie/` | `Banco/cases/pairs/` |
| `Banco/casi/corpi/` | `Banco/cases/bodies/` |
| `Banco/casi/esporta/` | `Banco/cases/export/` |
| `Banco/casi/estrusioni/` | `Banco/cases/extrusions/` |
| `Banco/casi/frase/` | `Banco/cases/sentence/` |
| `Banco/casi/lamiera/` | `Banco/cases/sheet_metal/` |
| `Banco/casi/lavorazioni/` | `Banco/cases/features/` |
| `Banco/casi/limiti/` | `Banco/cases/limits/` |
| `Banco/casi/misura/` | `Banco/cases/measurement/` |
| `Banco/casi/piano/` | `Banco/cases/plane/` |
| `Banco/casi/ponte/` | `Banco/cases/bridge/` |
| `Banco/casi/proprieta/` | `Banco/cases/document_properties/` |
| `Banco/casi/quote/` | `Banco/cases/dimensions/` |
| `Banco/casi/regressione/` | `Banco/cases/regression/` |
| `Banco/casi/ripeti/` | `Banco/cases/patterns/` |
| `Banco/casi/ritrovare/` | `Banco/cases/reanchor/` |
| `Banco/casi/rivoluzione/` | `Banco/cases/revolve/` |
| `Banco/casi/saldature/` | `Banco/cases/weldments/` |
| `Banco/casi/smusso/` | `Banco/cases/chamfer/` |
| `Banco/casi/specchia/` | `Banco/cases/mirror/` |
| `Banco/casi/tavola/` | `Banco/cases/drawing/` |
| `Banco/casi/telai/` | `Banco/cases/frames/` |
| `Banco/casi/testo/` | `Banco/cases/text/` |
| `Banco/casi/togliere/` | `Banco/cases/removal/` |
| `Banco/casi/verifica/` | `Banco/cases/check/` |
| `Banco/casi/viste/` | `Banco/cases/views/` |
| `Banco/compiti/` | `Banco/tasks/` |
| `Banco/resoconto/` | `Banco/report/` |

**Files whose name changed** (390 of the 453 moved; the others keep their name in the new folder), by folder:

- `Banco/casi-in-attesa/` → `Banco/pending-cases/` (1): `LEGGIMI.md` → `README.md`
- `Banco/casi/` → `Banco/cases/` (1): `E2-i-due-casi.md` → `E2-the-two-cases.md`
- `Banco/casi/asola/` → `Banco/cases/slot/` (18): `asola_cieca.json` → `blind_slot.json`, `asola_cieca_faccia_alto.json` → `blind_slot_top_face.json`, `asola_cieca_faccia_basso.json` → `blind_slot_bottom_face.json`, `asola_cieca_faccia_destra.json` → `blind_slot_right_face.json`, `asola_cieca_faccia_dietro.json` → `blind_slot_back_face.json`, `asola_cieca_faccia_sinistra.json` → `blind_slot_left_face.json`, `asola_fuori_centro.json` → `off_center_slot.json`, `asola_passante.json` → `through_slot.json`, `asola_ruotata_30.json` → `slot_rotated_30.json`, `asola_ruotata_90.json` → `slot_rotated_90.json`, `asola_su_piastra_stretta_0.json` → `slot_on_narrow_plate_0.json`, `asola_su_piastra_stretta_90.json` → `slot_on_narrow_plate_90.json`, `l_asola_ancorata_segue_il_bordo.json` → `anchored_slot_follows_the_border.json`, `l_asola_obliqua_ancorata_tiene_l_angolo.json` → `anchored_oblique_slot_keeps_its_angle.json`, `l_asola_si_corregge_con_la_quota.json` → `slot_is_corrected_with_the_dimension.json`, `lim_asola_cieca_senza_profondita.json` → `limit_blind_slot_without_depth.json`, `lim_asola_fuori_dal_pezzo.json` → `limit_slot_outside_the_part.json`, `lim_asola_tonda.json` → `limit_round_slot.json`
- `Banco/casi/asse/` → `Banco/cases/axis/` (2): `asse_dalla_parete_del_foro.json` → `axis_from_hole_wall.json`, `lim_asse_da_faccia_piana.json` → `limit_axis_from_a_planar_face.json`
- `Banco/casi/assiemi/` → `Banco/cases/assemblies/` (10): `due_basi_uguali_uno_step.json` → `two_equal_bases_one_step.json`, `fisso_non_e_accoppiato.json` → `fixed_is_not_mated.json`, `il_coperchio_dentro_la_base.json` → `cover_inside_base.json`, `il_coperchio_rifatto_tiene_i_vincoli_ma_rotti.json` → `rebuilt_cover_keeps_the_constraints_but_broken.json`, `inserire_non_e_accoppiare.json` → `inserting_is_not_mating.json`, `l_assieme_dice_da_che_file_dipende.json` → `assembly_says_which_file_it_depends_on.json`, `l_assieme_si_pesa_si_misura_si_manda.json` → `assembly_is_weighed_measured_and_sent.json`, `la_staffa_destra_e_la_sinistra.json` → `right_bracket_and_left_bracket.json`, `toccarsi_non_e_compenetrarsi.json` → `touching_is_not_interpenetrating.json`, `un_accoppiamento_non_basta.json` → `one_mate_is_not_enough.json`
- `Banco/casi/coppie/` → `Banco/cases/pairs/` (8): `asola_30x9.json` → `slot_30x9.json`, `foro_270_su_faccia_274.json` → `hole_270_on_face_274.json`, `foro_60_sul_bordo.json` → `hole_60_on_the_border.json`, `lim_asola_che_copre_tutto.json` → `limit_slot_that_covers_everything.json`, `lim_foro_che_copre_tutto.json` → `limit_hole_that_covers_everything.json`, `lim_foro_fuori_dal_materiale.json` → `limit_hole_outside_the_material.json`, `lim_tasca_che_copre_tutto.json` → `limit_pocket_that_covers_everything.json`, `tasca_400_cieca.json` → `blind_pocket_400.json`
- `Banco/casi/corpi/` → `Banco/cases/bodies/` (15): `combina_chiesto.json` → `requested_combine.json`, `combina_predefinito.json` → `default_combine.json`, `filetto_su_un_perno.json` → `thread_on_pin.json`, `interseca_due_cubi.json` → `intersect_two_cubes.json`, `lim_booleana_sconosciuta.json` → `limit_unknown_boolean.json`, `lim_combina_col_corpo_letto_prima_del_taglio.json` → `limit_combine_with_body_read_before_the_cut.json`, `lim_filetto_senza_denominazione.json` → `limit_thread_without_a_designation.json`, `lim_filetto_su_uno_spigolo_dritto.json` → `limit_thread_on_a_straight_edge.json`, `lim_raccordo_variabile_su_un_cerchio.json` → `limit_variable_fillet_on_a_circle.json`, `lim_raggio_troppo_grande_per_lo_spigolo.json` → `limit_radius_too_big_for_the_edge.json`, `lim_un_corpo_solo.json` → `limit_a_single_body.json`, `lim_unione_di_corpi_che_non_si_toccano.json` → `limit_union_of_bodies_that_do_not_touch.json`, `raccordo_che_cambia_raggio.json` → `fillet_that_changes_radius.json`, `sottrai_un_cubo_dall_altro.json` → `subtract_one_cube_from_the_other.json`, `unisci_due_cubi.json` → `merge_two_cubes.json`
- `Banco/casi/esporta/` → `Banco/cases/export/` (12): `due_corpi_esportati_a_forza.json` → `two_bodies_exported_by_force.json`, `iges_solidi.json` → `iges_solids.json`, `lim_due_corpi_non_si_esporta.json` → `limit_two_bodies_do_not_export.json`, `lim_estensione_sbagliata.json` → `limit_wrong_extension.json`, `lim_iges_con_estensione_step.json` → `limit_iges_with_a_step_extension.json`, `lim_iges_di_un_assieme.json` → `limit_iges_of_an_assembly.json`, `lim_protocollo_inventato.json` → `limit_made_up_protocol.json`, `lo_stl_non_esporta_la_faccia_selezionata.json` → `stl_does_not_export_the_selected_face.json`, `scatola_dodici_triangoli.json` → `box_twelve_triangles.json`, `scatola_in_ascii.json` → `box_in_ascii.json`, `step_ap203_e_lo_dice.json` → `step_ap203_says_so.json`, `step_ap214_riletto.json` → `step_ap214_reread.json`
- `Banco/casi/estrusioni/` → `Banco/cases/extrusions/` (8): `il_rialzo_rettangolare_ancorato_segue_la_mezzeria.json` → `anchored_rectangular_boss_follows_the_centerline.json`, `il_rialzo_tondo_ancorato_segue_il_bordo.json` → `anchored_round_boss_follows_the_border.json`, `il_tubo_ha_il_buco.json` → `pipe_has_the_hole.json`, `l_aletta_cresce_dalla_faccia.json` → `tab_grows_from_the_face.json`, `l_asola_estrusa.json` → `extruded_slot.json`, `la_ruota_da_diciassette_denti_si_fa.json` → `the_seventeen_tooth_gear_can_be_made.json`, `lim_rialzo_ancorato_ne_tondo_ne_rettangolare.json` → `limit_anchored_boss_neither_round_nor_rectangular.json`, `lim_ruota_da_sedici_denti.json` → `limit_sixteen_tooth_gear.json`
- `Banco/casi/frase/` → `Banco/cases/sentence/` (2): `albero_forato_non_dichiara_piu_il_profilo.json` → `drilled_shaft_no_longer_declares_profile.json`, `albero_pulito_dichiara_il_profilo.json` → `clean_shaft_declares_profile.json`
- `Banco/casi/lamiera/` → `Banco/cases/sheet_metal/` (12): `l_orlo_aperto_torna_col_conto.json` → `open_hem_checks_out.json`, `l_orlo_chiuso_si_schiaccia.json` → `closed_hem_flattens.json`, `la_L_al_contrario.json` → `the_L_reversed.json`, `la_L_si_sviluppa.json` → `the_L_flattens.json`, `la_flangia_a_spigolo_chiude_il_lato.json` → `edge_flange_closes_the_side.json`, `la_piega_da_schizzo_alza_meta_lamiera.json` → `sketched_bend_raises_half_the_sheet_metal.json`, `lim_flangia_spigolo_non_di_bordo.json` → `limit_edge_flange_not_on_a_border.json`, `lim_flangia_spigolo_senza_dritto.json` → `limit_edge_flange_without_a_straight.json`, `lim_flangia_tratto_corto.json` → `limit_flange_short_segment.json`, `lim_orlo_aperto_senza_gioco.json` → `limit_open_hem_without_gap.json`, `lim_piega_a_centottanta_gradi.json` → `limit_bend_at_180_deg.json`, `lim_sviluppo_senza_file.json` → `limit_flat_pattern_without_a_file.json`
- `Banco/casi/lavorazioni/` → `Banco/cases/features/` (34): `guscio_esterno.json` → `external_shell.json`, `guscio_interno.json` → `internal_shell.json`, `i_fori_guidati_ancorati_restano_a_venti.json` → `anchored_hole_wizard_holes_stay_at_twenty.json`, `il_cieco_diventa_passante.json` → `blind_becomes_through.json`, `il_foro_ancorato_segue_il_bordo.json` → `anchored_hole_follows_the_border.json`, `il_foro_guidato_segue_la_tabella.json` → `hole_wizard_follows_the_table.json`, `il_foro_sul_dietro_segue_l_angolo.json` → `hole_on_the_back_follows_the_corner.json`, `il_foro_sul_fianco_resta_a_meta_spessore.json` → `hole_on_the_side_stays_at_half_thickness.json`, `il_loft_non_e_un_cono.json` → `loft_is_not_a_cone.json`, `il_raccordo_arrotonda_i_quattro_angoli.json` → `fillet_rounds_the_four_corners.json`, `il_rialzo_cresce_e_non_e_un_taglio.json` → `boss_grows_and_is_not_a_cut.json`, `l_ordine_cambia_il_pezzo.json` → `order_changes_the_part.json`, `la_piastra_simmetrica_cresce_dalle_due_parti.json` → `symmetric_plate_grows_from_both_sides.json`, `la_quota_che_lascia_l_asola_dentro.json` → `dimension_that_keeps_the_slot_inside.json`, `la_quota_che_lascia_stare_il_raccordo.json` → `dimension_that_leaves_the_fillet_alone.json`, `la_tasca_centrata_segue_la_mezzeria.json` → `centered_pocket_follows_the_centerline.json`, `lim_ancora_che_non_dice_lo_stesso_punto.json` → `limit_anchor_that_does_not_report_the_same_point.json`, `lim_ancora_sul_bordo_del_raccordo.json` → `limit_anchor_on_the_fillet_border.json`, `lim_ancora_sul_bordo_dello_smusso.json` → `limit_anchor_on_the_chamfer_border.json`, `lim_fori_guidati_ancorati_a_meta.json` → `limit_anchored_hole_wizard_holes_halfway.json`, `lim_foro_guidato_fuori_dal_pezzo.json` → `limit_hole_wizard_outside_the_part.json`, `lim_nervatura_fuori_dal_suo_piano.json` → `limit_rib_outside_its_plane.json`, `lim_nervatura_sulla_piastra_senza_pareti.json` → `limit_rib_on_a_plate_without_walls.json`, `lim_quota_che_fa_uscire_l_asola.json` → `limit_dimension_that_pushes_the_slot_out.json`, `lim_quota_che_non_lascia_stare_il_raccordo.json` → `limit_dimension_that_will_not_leave_the_fillet_alone.json`, `lim_raccordo_faccia_fra_facce_che_si_danno_le_spalle.json` → `limit_fillet_face_between_back_to_back_faces.json`, `lim_raccordo_faccia_sulla_stessa_faccia.json` → `limit_fillet_face_on_the_same_face.json`, `lim_sformo_sulla_faccia_neutra.json` → `limit_draft_on_the_neutral_face.json`, `nervatura_nell_angolo_della_l.json` → `rib_in_the_corner_of_the_l.json`, `nervatura_nella_scatola_svuotata.json` → `rib_in_the_hollowed_box.json`, `raccordo_faccia_fra_due_facce_di_un_cubo.json` → `fillet_face_between_two_faces_of_a_cube.json`, `sformo_capovolto_cala.json` → `flipped_draft_drops.json`, `sformo_quattro_pareti.json` → `draft_four_walls.json`, `tubo_dritto_area_per_lunghezza.json` → `straight_pipe_area_per_length.json`
- `Banco/casi/limiti/` → `Banco/cases/limits/` (8): `lim_1_guscio_troppo_spesso.json` → `limit_1_shell_too_thick.json`, `lim_2_asola_piu_stretta_che_lunga.json` → `limit_2_slot_narrower_than_it_is_long.json`, `lim_3_raccordo_piu_grande_del_pezzo.json` → `limit_3_fillet_bigger_than_the_part.json`, `lim_4_foro_piu_largo_della_faccia.json` → `limit_4_hole_wider_than_the_face.json`, `lim_5_faccia_che_non_esiste.json` → `limit_5_face_that_does_not_exist.json`, `lim_6_quota_inesistente.json` → `limit_6_nonexistent_dimension.json`, `lim_7_taglio_fuori_dal_pezzo.json` → `limit_7_cut_outside_the_part.json`, `lim_8_quote_senza_viste.json` → `limit_8_dimensions_without_views.json`
- `Banco/casi/misura/` → `Banco/cases/measurement/` (10): `area_di_una_faccia_sola.json` → `area_of_a_single_face.json`, `distanza_fra_le_due_facce.json` → `distance_between_two_faces.json`, `due_facce_parallele_e_lo_dice.json` → `two_parallel_faces_and_it_says_so.json`, `ingombro_della_piastra.json` → `plate_bounding_box.json`, `ingombro_di_due_corpi_lontani.json` → `bounding_box_of_two_far_bodies.json`, `lim_distanza_di_una_faccia_da_se_stessa.json` → `limit_distance_of_a_face_from_itself.json`, `lim_ingombro_di_un_corpo_che_non_esiste.json` → `limit_bounding_box_of_a_body_that_does_not_exist.json`, `lo_spessore_che_resta_sotto_la_tasca.json` → `thickness_remaining_below_the_pocket.json`, `massa_di_un_cubo_di_acciaio.json` → `mass_of_a_steel_cube.json`, `massa_senza_materiale_non_si_sa.json` → `mass_without_material_is_unknown.json`
- `Banco/casi/piano/` → `Banco/cases/plane/` (6): `la_foto_sta_in_scala.json` → `the_picture_is_to_scale.json`, `lim_tre_punti_in_fila.json` → `limit_three_points_in_a_row.json`, `piano_frontale_meno_250.json` → `front_plane_minus_250.json`, `piano_obliquo_per_tre_punti.json` → `oblique_plane_through_three_points.json`, `piano_superiore_meno_100.json` → `top_plane_minus_100.json`, `piano_superiore_piu_100.json` → `top_plane_plus_100.json`
- `Banco/casi/ponte/` → `Banco/cases/bridge/` (101): `b8_due_passi.json` → `b8_two_steps.json`, `b8_due_tagli.json` → `b8_two_cuts.json`, `b8_due_tagli_uid.json` → `b8_two_cuts_uid.json`, `b8_secondo_storto.json` → `b8_second_crooked.json`, `c2_cerchio_senza_diametro.json` → `c2_circle_without_diameter.json`, `c2_cerchio_zero.json` → `c2_zero_circle.json`, `c2_tubo_dal_catalogo.json` → `c2_pipe_from_catalog.json`, `c4_ruota.json` → `c4_gear.json`, `c4_telaio_asse.json` → `c4_frame_axis.json`, `c4_telaio_dal_catalogo.json` → `c4_frame_from_catalog.json`, `c4_telaio_interasse.json` → `c4_frame_center_distance.json`, `c4_telaio_solo_quote.json` → `c4_frame_dimensions_only.json`, `c4_tubolare.json` → `c4_tubular.json`, `c4r_127denti.json` → `c4r_127_teeth.json`, `c4r_16denti.json` → `c4r_16_teeth.json`, `c4r_17denti.json` → `c4r_17_teeth.json`, `c4r_42denti.json` → `c4r_42_teeth.json`, `c4r_cava.json` → `c4r_keyway.json`, `c4r_foro.json` → `c4r_hole.json`, `c4r_foro_grande.json` → `c4r_big_hole.json`, `c4r_mezzo_dente.json` → `c4r_half_tooth.json`, `c4r_primario.json` → `c4r_primary.json`, `c4s_angolare.json` → `c4s_angular.json`, `c4s_asola.json` → `c4s_slot.json`, `c4s_asola_tonda.json` → `c4s_round_slot.json`, `c4s_esagono.json` → `c4s_hexagon.json`, `c4s_manca.json` → `c4s_missing.json`, `c4s_parete_meta.json` → `c4s_half_wall.json`, `c4s_spessore_lato.json` → `c4s_side_thickness.json`, `c4s_tondo.json` → `c4s_round.json`, `c4s_tubo.json` → `c4s_pipe.json`, `c5_intero.json` → `c5_whole.json`, `c5_senza_spessore.json` → `c5_without_thickness.json`, `c5_tipo_storto.json` → `c5_crooked_type.json`, `cambia_diametro.json` → `change_diameter.json`, `cilindro.json` → `cylinder.json`, `cp_1_blocco.json` → `cp_1_block.json`, `cp_2_foro.json` → `cp_2_hole.json`, `cp_3_ripeti.json` → `cp_3_repeat.json`, `cp_3b_ripeti.json` → `cp_3b_repeat.json`, `cp_4_specchia.json` → `cp_4_mirror.json`, `d3_apri.json` → `d3_empty.json`, `d3_barre.json` → `d3_bars.json`, `d3_completo.json` → `d3_complete.json`, `d3_frontale.json` → `d3_front.json`, `d3_modifica.json` → `d3_change.json`, `d3_pezzo_e_tavola.json` → `d3_part_and_drawing.json`, `d3_quote_tutte.json` → `d3_all_dimensions.json`, `d3_solo_tavola.json` → `d3_drawing_only.json`, `d3_solo_viste.json` → `d3_views_only.json`, `d3_tagli.json` → `d3_cuts.json`, `d3_tavola.json` → `d3_drawing.json`, `d3_vuoto.json` → `d3_open.json`, `d4_piano_orfano.json` → `d4_orphan_plane.json`, `d4_piano_orfano2.json` → `d4_orphan_plane_2.json`, `d5_cubo_e_raccordo.json` → `d5_cube_and_fillet.json`, `d5_cubo_e_taglio.json` → `d5_cube_and_cut.json`, `d5_cubo_sano.json` → `d5_healthy_cube.json`, `d5_innocente.json` → `d5_innocent.json`, `d5_raccordo_impossibile.json` → `d5_impossible_fillet.json`, `d5_stringi162.json` → `d5_tighten_162.json`, `d6_ripara.json` → `d6_repair.json`, `d6_rompi.json` → `d6_break.json`, `d6_sano.json` → `d6_healthy.json`, `d6_stringi.json` → `d6_tighten.json`, `d7_dappertutto.json` → `d7_everywhere.json`, `d7_in_parte.json` → `d7_in_part.json`, `d7_in_parte2.json` → `d7_in_part_2.json`, `d7_rompi.json` → `d7_break.json`, `e13_foro_sul_bordo.json` → `e13_hole_on_the_border.json`, `e15_guscio_da_riferimento.json` → `e15_shell_from_reference.json`, `e15_tutto_chiuso.json` → `e15_everything_closed.json`, `e1b_uid_inventato.json` → `e1b_made_up_uid.json`, `e1b_uid_vero.json` → `e1b_true_uid.json`, `e2_blocco.json` → `e2_block.json`, `e2_caso_a.json` → `e2_case_a.json`, `e2_caso_b.json` → `e2_case_b.json`, `e2_caso_b_giusto.json` → `e2_case_b_correct.json`, `e2_solo_quota.json` → `e2_dimension_only.json`, `e2_tasca.json` → `e2_pocket.json`, `e4_due_corpi.json` → `e4_two_bodies.json`, `e4_spezza_e_sposta.json` → `e4_split_and_move.json`, `e4_sposta_legittimo.json` → `e4_legitimate_move.json`, `e5_falso_rifiuto.json` → `e5_false_refusal.json`, `e5_intacca_angolo.json` → `e5_nicks_corner.json`, `e5_piastra_angolo.json` → `e5_plate_corner.json`, `e5_tondo_svasato.json` → `e5_round_countersunk.json`, `s_cava_a_filo.json` → `s_cavity_flush.json`, `s_composito_aperto.json` → `s_open_composite.json`, `s_foro_fuori.json` → `s_hole_outside.json`, `s_foro_staccato.json` → `s_detached_hole.json`, `s_papillon.json` → `s_bowtie.json`, `s_papillon2.json` → `s_bowtie_2.json`, `seq_a_tasca60.json` → `seq_a_pocket60.json`, `seq_b_tasca10_su_F2.json` → `seq_b_pocket10_on_F2.json`, `telaio_libreria.json` → `frame_library.json`, `telaio_su_copia.json` → `frame_on_copy.json`, `uid_buono.json` → `good_uid.json`, `uid_stantio.json` → `stale_uid.json`, `uid_stantio_2.json` → `stale_uid_2.json`, `z06_tavola.json` → `z06_drawing.json`
- `Banco/casi/proprieta/` → `Banco/cases/document_properties/` (11): `i_due_cassetti_non_si_vedono.json` → `the_two_lists_do_not_see_each_other.json`, `la_configurazione_nuova_si_attiva_da_sola.json` → `new_configuration_activates_itself.json`, `lim_configurazione_che_non_esiste.json` → `limit_configuration_that_does_not_exist.json`, `lim_configurazione_gia_esistente.json` → `limit_configuration_that_already_exists.json`, `lim_default_non_si_chiama_default.json` → `limit_default_is_not_named_default.json`, `lim_proprieta_senza_nome.json` → `limit_property_without_a_name.json`, `lim_proprieta_su_configurazione_che_non_esiste.json` → `limit_property_on_a_configuration_that_does_not_exist.json`, `materiale_chiesto.json` → `requested_material.json`, `materiale_predefinito.json` → `default_material.json`, `numero_disegno_sul_documento.json` → `drawing_number_on_the_document.json`, `si_torna_indietro_col_nome_intero.json` → `goes_back_with_the_full_name.json`
- `Banco/casi/quote/` → `Banco/cases/dimensions/` (6): `la_parete_ha_un_nome.json` → `wall_has_a_name.json`, `la_piastra_si_quota_da_sola.json` → `plate_dimensions_itself.json`, `la_tolleranza_sulla_quota_del_modello.json` → `tolerance_on_the_model_dimension.json`, `la_variabile_non_scavalca_lo_smusso.json` → `variable_does_not_override_the_chamfer.json`, `le_quote_arrivano_dal_modello.json` → `dimensions_come_from_the_model.json`, `lim_schema_con_due_riferimenti.json` → `limit_scheme_with_two_references.json`
- `Banco/casi/regressione/` → `Banco/cases/regression/` (19): `r01_quattro_fori_dal_bordo.json` → `r01_four_holes_from_the_border.json`, `r01b_quattro_fori_restano_dove_sono.json` → `r01b_four_holes_stay_where_they_are.json`, `r02_passanti_restano_passanti.json` → `r02_through_holes_stay_through.json`, `r03_tasca_centrata.json` → `r03_centered_pocket.json`, `r03b_tasca_resta_dov_era.json` → `r03b_pocket_stays_where_it_was.json`, `r04_quattro_raccordi_restano.json` → `r04_four_fillets_remain.json`, `r05_smusso_piu_grande_del_pezzo.json` → `r05_chamfer_bigger_than_the_part.json`, `r06_foro_assiale_nel_cilindro.json` → `r06_axial_hole_in_the_cylinder.json`, `r07_corona_di_fori_resta.json` → `r07_ring_of_holes_remains.json`, `r08_ripeti_circolare_istanze.json` → `r08_circular_pattern_instances.json`, `r10_fondo_del_guscio_si_sposta.json` → `r10_shell_bottom_moves.json`, `r11_fondo_piu_spesso.json` → `r11_thicker_bottom.json`, `r12_telaio_col_profilo_piu_sottile.json` → `r12_frame_with_a_thinner_profile.json`, `r14_foro_guidato_e_il_preforo.json` → `r14_hole_wizard_and_the_pilot_hole.json`, `r15_raccordo_sullo_spigolo_interno.json` → `r15_fillet_on_the_internal_edge.json`, `r16_asola_che_sborda.json` → `r16_slot_that_overflows.json`, `r17_specchia_resta_simmetrico.json` → `r17_mirror_stays_symmetric.json`, `r18_ripeti_lineare_passo_invariato.json` → `r18_linear_pattern_pitch_unchanged.json`, `r19_foro_a_venti_dal_bordo.json` → `r19_hole_twenty_from_the_border.json`
- `Banco/casi/ripeti/` → `Banco/cases/patterns/` (13): `griglia_3x2.json` → `grid_3x2.json`, `il_riempimento_fa_quattordici_fori.json` → `fill_makes_fourteen_holes.json`, `l_asse_riconosciuto.json` → `recognized_axis.json`, `lim_asse_spostato.json` → `limit_moved_axis.json`, `lim_copia_che_non_e_mai_esistita.json` → `limit_copy_that_never_existed.json`, `lim_direzione_da_spigolo_curvo.json` → `limit_direction_from_a_curved_edge.json`, `lim_griglia_non_si_conta_come_una_fila.json` → `limit_grid_does_not_count_as_a_row.json`, `lim_lo_zero_non_e_una_copia.json` → `limit_zero_is_not_a_copy.json`, `lim_riempimento_contorno_non_piano.json` → `limit_fill_contour_not_planar.json`, `lim_riempimento_direzione_fuori_dalla_faccia.json` → `limit_fill_direction_outside_the_face.json`, `lim_riempimento_senza_direzione.json` → `limit_fill_without_direction.json`, `salta_la_seconda_copia.json` → `skip_the_second_copy.json`, `tre_fori_passo_40.json` → `three_holes_pitch_40.json`
- `Banco/casi/ritrovare/` → `Banco/cases/reanchor/` (3): `il_raccordo_ritrova_lo_spigolo_rinumerato.json` → `fillet_finds_the_renumbered_edge_again.json`, `lim_raccordo_su_uno_spigolo_accorciato.json` → `limit_fillet_on_a_shortened_edge.json`, `lim_raccordo_su_uno_spigolo_doppio.json` → `limit_fillet_on_a_double_edge.json`
- `Banco/casi/rivoluzione/` → `Banco/cases/revolve/` (5): `albero_a_gradini.json` → `stepped_shaft.json`, `boccola_cava.json` → `hollow_bushing.json`, `cilindro_d40_lungo_50.json` → `cylinder_d40_long_50.json`, `lim_profilo_a_cavallo_dell_asse.json` → `limit_profile_straddling_the_axis.json`, `mezzo_cilindro_180.json` → `half_cylinder_180.json`
- `Banco/casi/saldature/` → `Banco/cases/weldments/` (7): `il_cordone_va_fra_facce_che_si_toccano.json` → `weld_bead_goes_between_touching_faces.json`, `il_fazzoletto_sta_nell_angolo.json` → `gusset_sits_in_the_corner.json`, `il_poligono_del_fazzoletto_non_e_degenere.json` → `gusset_polygon_is_not_degenerate.json`, `il_taglio_accorcia_la_barra.json` → `cut_shortens_the_bar.json`, `il_taglio_senza_gioco_non_eredita.json` → `cut_without_gap_does_not_inherit.json`, `il_tappo_chiude_il_tubo.json` → `cap_closes_the_pipe.json`, `la_distinta_conta_le_barre.json` → `cut_list_counts_the_bars.json`
- `Banco/casi/smusso/` → `Banco/cases/chamfer/` (4): `l_angolo_dello_smusso_si_cambia_in_gradi.json` → `chamfer_angle_changes_in_deg.json`, `lim_smusso_piu_grande_dello_spigolo.json` → `limit_chamfer_bigger_than_the_edge.json`, `smusso_3x6_cianfrino.json` → `chamfer_3x6_bevel.json`, `smusso_5_a_45.json` → `chamfer_5_at_45.json`
- `Banco/casi/specchia/` → `Banco/cases/mirror/` (2): `foro_specchiato_sul_destro.json` → `hole_mirrored_on_right.json`, `lim_piano_inventato.json` → `limit_made_up_plane.json`
- `Banco/casi/tavola/` → `Banco/cases/drawing/` (44): `due_fogli_e_lo_stesso_diedro.json` → `two_sheets_and_the_same_projection_angle.json`, `due_fori_uguali_uno_passante_uno_cieco.json` → `two_equal_holes_one_through_one_blind.json`, `i_contatori_ci_sono_anche_a_zero.json` → `counters_exist_even_at_zero.json`, `il_cartiglio_dice_cosa_vuole.json` → `title_block_says_what_it_wants.json`, `il_dettaglio_ingrandisce.json` → `detail_enlarges.json`, `il_foro_cieco_dice_quanto_e_profondo.json` → `blind_hole_says_how_deep_it_is.json`, `il_simbolo_di_saldatura_sul_giunto.json` → `weld_symbol_on_the_joint.json`, `l_albero_a_gradini_quotato_di_fianco.json` → `stepped_shaft_dimensioned_from_the_side.json`, `l_albero_smussato_non_quota_le_gambe.json` → `chamfered_shaft_does_not_dimension_the_legs.json`, `la_boccola_tornita_non_e_un_foro.json` → `turned_bushing_is_not_a_hole.json`, `la_borchia_non_e_un_foro.json` → `stud_is_not_a_hole.json`, `la_distinta_base_conta_i_componenti.json` → `bom_counts_the_components.json`, `la_distinta_di_taglio_dice_se_ha_confrontato.json` → `cut_list_says_whether_it_compared.json`, `la_distinta_di_taglio_in_tavola.json` → `cut_list_in_the_drawing.json`, `la_nota_finisce_sul_foglio.json` → `note_ends_up_on_the_sheet.json`, `la_sezione_taglia_il_pezzo.json` → `section_cuts_the_part.json`, `la_tabella_fori_dice_dove_sono.json` → `hole_table_says_where_they_are.json`, `la_tavola_e_un_secondo_documento.json` → `drawing_is_a_second_document.json`, `la_tavola_esce_in_pdf.json` → `drawing_exports_to_pdf.json`, `la_tavola_quota_fori_e_smusso.json` → `drawing_dimensions_holes_and_chamfer.json`, `la_tavola_si_quota.json` → `drawing_dimensions_itself.json`, `le_annotazioni_stanno_fuori_dal_pezzo.json` → `annotations_stay_outside_the_part.json`, `le_croci_di_centro_sui_fori.json` → `center_marks_on_the_holes.json`, `le_quote_arrivano_nel_pdf.json` → `dimensions_arrive_in_the_pdf.json`, `lim_datum_con_la_stessa_lettera.json` → `limit_datum_with_the_same_letter.json`, `lim_dettaglio_fuori_dalla_vista.json` → `limit_detail_outside_the_view.json`, `lim_due_sezioni_con_la_stessa_lettera.json` → `limit_two_sections_with_the_same_letter.json`, `lim_nota_fuori_dal_foglio.json` → `limit_note_outside_the_sheet.json`, `lim_planarita_con_un_riferimento.json` → `limit_flatness_with_one_reference.json`, `lim_rugosita_fuori_scala.json` → `limit_surface_finish_out_of_scale.json`, `lim_saldatura_su_un_corpo_solo.json` → `limit_weld_on_a_single_body.json`, `lim_sezione_fuori_dalla_vista.json` → `limit_section_outside_the_view.json`, `lim_spigolo_che_non_esiste.json` → `limit_edge_that_does_not_exist.json`, `lim_sviluppo_di_un_pezzo_pieno.json` → `limit_flat_pattern_of_a_solid_part.json`, `lim_tabella_fori_sulla_vista_di_fianco.json` → `limit_hole_table_on_the_side_view.json`, `lim_tolleranza_su_un_datum_che_non_c_e.json` → `limit_tolerance_on_a_datum_that_is_not_there.json`, `lo_sviluppo_in_tavola.json` → `flat_pattern_in_the_drawing.json`, `tav_1_salva_pezzo.json` → `dwg_1_save_part.json`, `tav_2_crea_disegno.json` → `dwg_2_create_drawing.json`, `tav_3_foglio.json` → `dwg_3_sheet.json`, `tav_4_viste.json` → `dwg_4_views.json`, `tav_5_isometrica.json` → `dwg_5_isometric.json`, `tav_6_quote.json` → `dwg_6_dimensions.json`, `tav_7_pdf.json` → `dwg_7_pdf.json`
- `Banco/casi/telai/` → `Banco/cases/frames/` (1): `i_profili_si_leggono_prima.json` → `profiles_are_read_first.json`
- `Banco/casi/testo/` → `Banco/cases/text/` (6): `l_area_va_col_quadrato_dell_altezza.json` → `area_scales_with_the_square_of_the_height.json`, `la_stessa_sigla_in_rilievo.json` → `the_same_code_embossed.json`, `lim_marcatura_senza_profondita.json` → `limit_marking_without_depth.json`, `lim_scritta_che_esce_dalla_faccia.json` → `limit_engraved_text_that_exits_the_face.json`, `lim_scritta_tutta_fuori_dal_pezzo.json` → `limit_engraved_text_entirely_outside_the_part.json`, `sigla_incisa_al_centro.json` → `code_engraved_in_the_center.json`
- `Banco/casi/togliere/` → `Banco/cases/removal/` (5): `elimina_il_primo_foro.json` → `delete_first_hole.json`, `lim_elimina_estrusione_di_base.json` → `limit_delete_base_extrusion.json`, `lim_nome_inventato.json` → `limit_made_up_name.json`, `lim_sopprimere_due_volte.json` → `limit_suppress_twice.json`, `sopprimi_il_terzo_foro.json` → `suppress_the_third_hole.json`
- `Banco/casi/verifica/` → `Banco/cases/check/` (6): `blocco_dritto_si_stampa.json` → `straight_block_prints.json`, `corpo_sano_si_esporta.json` → `healthy_body_exports.json`, `due_corpi_non_si_esportano.json` → `two_bodies_do_not_export.json`, `il_corpo_nascosto_che_pende_si_vede.json` → `hidden_hanging_body_is_visible.json`, `lim_corpo_che_non_esiste.json` → `limit_body_that_does_not_exist.json`, `pareti_a_sessanta_gradi_non_si_stampano.json` → `walls_at_sixty_degrees_do_not_print.json`
- `Banco/casi/viste/` → `Banco/cases/views/` (6): `1_piastra_forata.json` → `1_drilled_plate.json`, `2_guscio_aperto.json` → `2_open_shell.json`, `3_staffa_a_L.json` → `3_l_bracket.json`, `4_flangia_forata.json` → `4_drilled_flange.json`, `5_albero_a_gradini.json` → `5_stepped_shaft.json`, `COME_SI_CONTA.md` → `HOW_IT_COUNTS.md`
- `Banco/compiti/` → `Banco/tasks/` (3): `LEGGIMI.md` → `README.md`, `staffa_a_elle.json` → `l_bracket.json`, `telaio_da_un_metro.json` → `one_meter_frame.json`
- `Banco/resoconto/` → `Banco/report/` (1): `ultimo.json` → `last.json`

## Part of 0.130.1 — Z-C21.14, `annulla` tells the truth (branch `zc21-14`) · bridge 0.125.0 · library 0.149.0

**What the live probe M19 and the probe `redo` (1/10/2026) found on the way of
`annulla`** (TODO entry Z-C21.14; RAG note 82zz_duodecies, the section on the live
probe of M19), with the adversarial review of the lot applied (findings F1-F16 below;
the version was raised once for the lot, as the review of Z-C21.11 did). The bench has
NOT run on this version yet: the three bench-freshness checks of Prove.Contratti are
red until it does. Prove.Contratti: 772 checks (759 on b5f7506, whose
`Prove/LEGGIMI.md` said 762), only those three red.

- **The state after a plan is read on the plan's document.** `Strumenti/Attrezzi.cs`
  read it on the window in front (`_sessione.Stato`), so after a plan that ended with
  another document in front (a `crea_disegno`, another session, the person) the memory
  of `annulla` held that document's numbers (line BRIDGE-R1 (X2) of the probe: a right
  undo refused; phase N: after `crea_disegno` no memory at all). The executor now hands
  on its plan document (`EsecutorePiano.Risultato.PlanDocument`, the reference it read
  once at the start) and the document it ended on (`FinalDocument`), and the bridge
  reads the plan's document there (`Sessione.StateOfDocument`, `Sessione.IsInFront`).
  The memory, `volume_dopo_mm3` and the error counts describe the plan's document;
  `stato_dopo` describes the document in front, and when the two differ the answer says
  so (`document_in_front`, `note_document_in_front`, with the way back).
- **The plan runs on the document the bridge checked (review F7).** The executor took
  the active document by itself (`EseguiAsync(null)`), so a window brought to the front
  between the bridge's reading and the plan got the plan. `Sessione.EseguiPiano` now
  hands it the identity it checked, and the executor stops before writing when the
  active document is another one; the window it found is then not taken for the plan's
  document, nor adopted.
- **The window in front is adopted only when the plan ended on it (review F5).** After
  a plan during which another window came to the front from outside (scenario X2 of the
  probe), its identity and signatures were adopted, so a retry of the refused step
  written for the plan's part passed the «document changed» check and wrote into the
  other document. Now they are cleared, the note says to call `guarda_il_pezzo` or to
  bring the part back, and the next `disegna` asks for `guarda_il_pezzo` first. After
  `crea_disegno` the drawing is the document the plan ended on, and is adopted as
  before.
- **The last plan is the memory, and only a plan that wrote something replaces it
  (review F1, F3, F12).** The executor reads what FinishRecordingUndoObject answered for
  the plan's undo entry (`UndoEntryOnPlanDocument`, answered as `undo_entry_recorded`).
  The rules are one pure function (`Attrezzi.MemoryAfterPlan`): a plan that built, or a
  `disegna` whose own entry closed with something in it (a reorder, a rename, a
  property), becomes the memory, so `annulla` withdraws the last plan as its
  description says (the first version of this lot refused to); a plan that wrote
  nothing (never reached the executor, stopped before its recording, or closed an empty
  one with nothing visibly changed) keeps the memory, so a plan, a measure and an undo,
  or a plan, a drawing with its views, the part brought back and an undo, are no longer
  refused; a
  `misura` that recorded something, or another bridge process holding the turn on the
  memory's document (review F4), marks the memory covered; an executor that threw
  (review F9), an unknown answer or a plan on another document drops it, and the reason
  is rewritten after every plan that wrote. The probe measured only the answer of
  FinishRecordingUndoObject (false for an empty recording, true for a full one); whether
  an empty recording leaves an entry on the stack is undecided (its control H3 failed,
  review F2), and the bench case `undo case c` decides it: if it is red, the rule that
  keeps the memory after an empty recording goes.
- **What `annulla` compares, and when it refuses before pressing (review F1, F11, F14,
  F16).** The memory holds the plan's document by its COM reference (a save renames it;
  a document closed and opened again has the same name and lost the undo stack) and a
  snapshot of it before and after the plan: volume, feature count, feature names with
  their IDs, in tree order. `annulla` refuses BEFORE pressing (`refused_before_pressing`)
  when the memory is covered, or when the part no longer reads as the plan left it,
  names and order included (a reorder by another bridge process was pressed away
  before); and it says «withdrawn» only if the part then reads as before the plan. A
  plan that changed nothing compared is withdrawn with `checked_by_reading` false and a
  note. Beyond the feature cap of the state the answers say that names and order were
  compared only on the listed features.
- **No blind redo, and no claim of a part put back without looking.** SOLIDWORKS never
  redoes an undo entry recorded through the API (probe `redo`: EditRedo2(1) and (2),
  RunCommand(swCommands_Redo), after rebuilds, the executor's own entry; a plain
  unrecorded extrusion is redone at once), and the answer after a wrong press said the
  part was put back whenever EditRedo2 did not throw. Now a press that did not bring the
  part back is compared with the part before pressing, one redo is tried only if the
  press took away something visible, and «restored» is said only from a new reading.
  The refusal of a covered memory says what a press would do according to what covers
  it (a recorded entry is lost to a press; an unrecorded action put back by a redo is
  not, review F10), and every refusal gives a remedy for the model (take the features
  away with a new plan: `elimina_lavorazione`, `sopprimi_lavorazione`) before the one
  for the person at the screen (review F1, F12). The comments that stated the false
  premise now say what was measured.
- **The resent-plan guard reads one document (review F6):** the plan's identity and the
  summary of the plan's document after it, so after [..., `crea_disegno`] a plan resent
  on the part is recognised instead of building a second time.
- **Tool descriptions (review F15):** `annulla` and `disegna` say which plan is the
  last one, how to come back to the part, `document_in_front`, `undo_entry_recorded`
  and the refusal before pressing.
- **Bench, the group that talks to the bridge** (`Banco/PortaDelCliente.cs`,
  `UndoHonesty`), 19 tests in four cases, each read back through COM and red on 0.124.1
  by the code path (written in the method's summary): (a) plan 2 reorders the holes of
  plan 1, and `annulla` withdraws the reorder and leaves plan 1; (b) a plan on a saved
  part ends with `crea_disegno`, the part comes back with `documento`/`attiva`, and
  `annulla` withdraws the plan; (c) `annulla` after a `misura` still withdraws the plan
  (it decides review F3); (d) a second bridge process reorders the holes of the first
  one's plan, and the first one's `annulla` refuses before pressing, the reorder still
  there (review F14). Case b saves with SaveAs3; the one obsolete SaveAs of the file is
  the document cycle's, as before (review F13).
- **Prove.Contratti:** `Zc2114UndoHonestyContract`, 13 checks (the pure decisions by
  reflection, the source rules of the plan, of `annulla`, of the executor and of the
  session each seen red on changed copies); `ProveIlDocumento` follows the new signature
  of `FinishUndoRecording`; the frozen undecided count of `Strumenti/Attrezzi.cs|Value`
  goes down from 16 to 14.
- **Review findings not applied as asked:** the per-document counter shared between
  bridge processes (second half of F4) is not built: the snapshot compared before
  pressing now sees a reorder or a rename by any writer, and what it cannot see (a
  property, a configuration) needs its own design (where the counter lives, who resets
  it, a crash in between).

## Part of 0.130.1 — W3.1-bis.3, the name map (branch w31bis-3)

Tooling, data and documents only: the bridge and the library are unchanged, so
`VERSION.txt` is not raised (the practice of W3.1-bis.2).

### The one map the rename windows are generated from

- **`Controlli/lingua/name-map-extract.js`** reads the tree and writes
  **`Controlli/lingua/name-map.tsv`**: one row per Italian name per type
  (columns `type`, `it`, `en`, `count`, `where`, `note`), in 19 types: tool,
  sub_action, operation, catalogue_family, case_family, argument, enum_value
  (argument=value, per enum block), catalogue_key, response_field,
  response_value, case_key, free_case_key, summary_key, selector, folder, file,
  case_name, project, identifier. The same word in two roles is two rows.
- **Italian is decided by the language gate itself**: its lexicon, its
  identifier rule and its exceptions, plus its prose rule on a name of two
  words or more read as words (a phrase glued with underscores is still a
  phrase). So every name the gate counts has a row, and a name generated by an
  Italian SOLIDWORKS never gets one.
- Sources: the catalogue and its schemas; the tool list and the tool schemas of
  `Strumenti/` (Roslyn; an enum built from a member is resolved through it);
  the cases of `Banco/casi/` and `Banco/casi-in-attesa/`, plans included,
  walked through the contexts of `Banco/case-keys.json`; the summary of the
  bench; the keys and values the library and the bridge write into a
  `Dictionary<string, object>`, found with a Roslyn **semantic model** (schema
  dictionaries left out); C# declarations through Roslyn, JavaScript through
  acorn, PowerShell through its own parser; `git ls-files` for folders and
  files.
- **`--check`**: (a) every Italian name found has a row with an English name;
  (b) no two rows of a type share an English name, unless each says "merged";
  (c) no English name, or part of one, is a C# keyword (contextual ones
  included) or a JSON Schema keyword; (d) ASCII and the shape of the Italian
  name (snake, Pascal or camel case, the extension, a leading underscore), unit
  suffixes kept and degrees as `_deg`, a free note of a case with a leading
  underscore; (e) a namespace extends the English name of its parent; (f) the
  gate does not read the English name as Italian. **It passes on the map as
  committed: 6,062 rows, 0 failures** (743 warnings: an English name the same
  role already uses somewhere in the tree, or a name kept as it is).
- **`--self-test`**: 27 cases, every check seen red on its fake row and a good
  map seen green; a mutation of any check turns it red. Run by the pre-push
  hook, and registered in `snapshot-scripts.json` with `--counts` and
  `--check`.
- **`--coverage`** lists the names of the gate's set categories that no row
  covers: paths 0 of 640, JSON keys 9 of 443 (the task files of
  `Banco/compiti/`, rewritten for D9, and three keys of a fake client
  configuration), identifiers 341 of 4,799 and protocol values 389 of 2,162
  (names inside prose and documents, words of messages, environment variable
  names, SOLIDWORKS API names the lexicon misreads). Not mapped yet, because
  none of the 19 types fits them: the keys of `Banco/compiti/*.json`, three
  keys of `Controlli/config-con-altri-server.json`, the environment
  variable names, the command-line options of the bench, the probes and the
  installer (about thirty literals such as the group, keep, mutate and
  uninstall options, read by `Banco/banco.ps1`, the hooks and the owner's
  habits), and the file names of the file-based plan session of
  `Banco/Sessione.cs` (a step file and a state file per step, written by a
  planner on the other side). A type for each, or a decision to keep them,
  is needed before W3.1-bis.6 and .10 touch those scripts.

### The English column, filled and checked

Four translators filled the English column on separate files, so that no two
wrote the same one; the integrator merged them into
`Controlli/lingua/name-map.tsv` (every row covered exactly once, `count` and
`where` identical to the skeleton), deleted the partial files and ran
`--check` until it passed. Rows per type, with the rows the integration
changed after the merge:

| type | rows | changed by the integration |
|---|---|---|
| tool | 7 | 0 |
| sub_action | 5 | 0 |
| operation | 99 | 0 |
| catalogue_family | 12 | 0 |
| case_family | 26 | 0 |
| argument | 205 | 0 |
| enum_value | 128 | 0 |
| catalogue_key | 9 | 0 |
| response_field | 690 | 22 |
| response_value | 49 | 4 |
| case_key | 38 | 1 |
| free_case_key | 179 | 175 |
| summary_key | 19 | 0 |
| selector | 20 | 0 |
| folder | 80 | 13 |
| file | 183 | 100 |
| case_name | 383 | 111 |
| project | 30 | 5 |
| identifier | 3,900 | 1,325 |
| **total** | **6,062** | **1,756** |

One row was added by hand: the probe folder `Controlli/ApiQuote/`, which the
gate reads as English, renamed with its project.

**The rules applied** (TODO W3.1-bis, section 2, and D3):

- The glossary of section 2 first, and the proposal for the operations: every
  tool, sub-action, operation, catalogue family, catalogue key and selector
  follows it, except where a check forbids it (below).
- snake_case stays snake_case, `_mm` stays, the degree suffix becomes `_deg`; the
  plural follows English; a free note of a case gets the leading underscore
  of W3.1-bis.4 (all 179).
- Different enumerations stay different: the catalogue families and the case
  families are two vocabularies (checks / check, exports / export are not
  merged). The case folders under `Banco/casi/` take the English name of their
  case family, because the loader joins the folder name to the family.
- A word with two senses is decided by context, never by a replace-all:
  `piano` is plane in the CAD code and plan in the plan executor and the bench
  orchestration (the plan executor, the footprint of a plan, the last plan, the
  plan in memory); `passo` is the step of a plan, but the pitch of a pattern and
  of a gear; `albero` is the feature tree (or a parsed JSON tree) in the locals
  and the probes that count features, and a shaft in the cases and the gear
  code (a stepped shaft, a keyway for a shaft); `distinta` is the cut list where the
  code reads a weldment cut list; `stadio` is the stadium shape of a slot, not a
  stage. Where one identifier carries both senses in different files (the
  locals of plane and plan), the row holds one sense and its note names the
  files of the other, for W3.1-bis.6.
- Method names take the English base verb (Widen, Analyze, Compare, not the
  third person); noun phrases take the English order (a body name, not a name
  of body); the helpers ending in "of" become `NameOf`, `KeyOf`, `BodiesOf`;
  the test classes become `<Subject>Tests`, like the contracts already written
  in English; a file of a C# type takes the English name of its type.
- A collision inside a type is decided, never renamed silently: inflections and
  synonyms (gender, number, two words for one thing) share an English name and
  say "merged", but only after a scope analysis showed that no two of them
  meet (C# member bodies, containing types and namespaces through Roslyn;
  JavaScript and PowerShell per file); the 37 pairs that do meet, and words of
  different meaning (open the verb and open the adjective), got distinct names.
- An English name that the same scope already declares was changed too (a
  local `name` beside the old one, the path module of Node, `System.IO.Path`
  and `System.Diagnostics.Process` hidden by a property, two `SameFile` in one
  partial class): 19 rows.
- A keyword is replaced by the nearest plain word, the same in every type where
  the root occurs: new becomes create (sub-action) or new_part (case key),
  type becomes kind (selector, response field) or variant (argument), equals
  becomes equal_to, contains becomes includes (and the message assertion
  message_includes, the same verb), description becomes details (catalogue
  key, argument and response field alike), dependencies becomes
  file_dependencies, fixed becomes grounded, where becomes placement or
  location, value becomes value_text; lowercase identifiers get the same
  treatment (doubled, ext, timestamp, thisOne, theField, testCase, fitClass).

**The doubtful rows, and how they were decided:**

- The test-chosen custom properties of `Banco/casi/proprieta/` are data the
  cases write and read back, so they are renamed in the cases (Hand,
  DrawingNumber, Revision). One property is **kept as it is, an open
  question for the owner**: the description the Italian drawing template of the
  shop shows in its title block (`Banco/casi/tavola/il_cartiglio_dice_cosa_vuole.json`
  writes it and reads it on the sheet). Renaming it would cut the case off the
  template; the way out is either an exception of class `solidworks-localized`
  or a template with English property names. The same case asserts two more
  property names of that title block in a textual contains: they stay with
  it, whatever the owner decides.
- `Segna` becomes `Check`, as the glossary has it, but two English contracts
  (`Prove/StateChamferContract.cs:69`,
  `Prove/WeldmentConfigurationZc212terContract.cs:72`) declare
  `Check(Assembly)`, which would hide the imported one: W3.1-bis.6 renames
  those two methods first. The note of the row says so.
- The edge cases of the bench keep a prefix of their own, limit, where the
  translator had used edge (a word the map already gives to the geometric
  edge); the sense of each name is otherwise kept.
- The same key holding two senses (the end point of a segment and, at one site
  of the catalogue, the ISO 2553 weld throat) gets the sense of most sites; the
  note names the other site for W3.1-bis.5.
- The gear probe and the gear cases say gear, not wheel; the raised boss of
  the anchoring cases says boss, not raise; a hole wizard is never a pilot
  hole.

**The adversarial review** read every row against the code that writes or
reads the name, and changed 146 rows (138 English names, 8 notes only), each
one signed `reviewer:` in its note. `--check` passes again on 6,062 rows.

- **Senses a model would have misread.** The profile argument of a
  structural member is the path of the `.sldlfp` file, so it is
  `profile_path`, as the profile listing already answers, not `path_profile`
  (the glossary note read it as the profile of a sweep path, and no sweep
  takes it). The export answers say `file_format`, not `paper_size`: the five
  cases that check the key read IGES, STEP, ASCII or binary; the paper-size
  and the sheet-format sites of the same key are named in the note for
  W3.1-bis.5. The frame argument that says how its sizes are measured is
  `measured_as`, not `measurements`, the block of every answer.
- **The cut list is not a BOM.** The bodies counted in a weldment cut list are
  `bodies_in_cut_list` and `bodies_in_part_cut_list`, and the file of the
  drawing cut-list table is `DrawingCutListOperations.cs`.
- **One English word per Italian word on the whole surface.** The answer echo
  of an argument takes the argument's name: `gap_mm` (the weld gap and the
  hem gap; the cases follow), `offset_used_mm`, `library`, `end_condition`,
  `tool_bodies`, `trim_boundary_bodies`, `instance_count1`, `copy_count`,
  grounded for every fixed component, and `details` for the description of
  the catalogue, of describe_operation and of a configuration.
- **Words of the trade.** `projection_angle` for first and third angle (the
  key, the case and the free note that named it dihedral), `pitch_diameter_mm`,
  `outside_diameter_mm` and `root_diameter_mm` of a gear, `k_factor`,
  `center_of_mass_mm`, `lower_deviation_mm` and `upper_deviation_mm` of an
  ISO 286 tolerance, `driven_dimensions` of an equation, `read_only`,
  `throat` of a weld, `document_title` instead of heading, `in_front` for the
  document in front, `two_lists` for the two property lists the Italian
  called drawers.
- **Identifiers whose meaning had changed.** The helpers that draw sketch
  entities say Draw, not Build (the very case the TODO warns about, in the
  opposite direction); an upper limit is Cap, not Roof; a reply timeout is
  TimeoutMs, not an expectation; an MD5 or SHA-256 is a fingerprint, not a
  footprint; the pitch of a pattern and of gear teeth is pitch, not a plan
  step; the height of a build plate is a level, not a dimension; the IT
  grades of ISO 286 are not degrees; a component along an axis is along, not
  longer; the kind of a JSON node is NodeKind, not That.
- **Other senses at single sites**, named in the notes for the window that
  renames them: the section offset under the end-point key, the elevation and
  the share under the dimension local, the dimension spacing under the step
  local, the key path under the file-path local, the inner key of the frame
  answer under measurements.

### A contract that runs the check, seen red

- `Prove/SafetyNetSelfTests.cs` runs `name-map-extract.js --self-test`, then
  `--check` on the real map, then `--check` on a temporary copy of the map
  without one row, with a collision and with a C# keyword, which must exit 1
  naming (a), (b) and (c). Seen red on 1/10/2026 with a row taken out of the
  real map and the keyword check of the extractor blinded: all three red, the
  copy red on (a) and (b) only. `Prove/LEGGIMI.md`: a row for it, and 764
  checks (a full run, with the TODO).

### The 15 tasks of the autonomous run (D9)

Written in Italian as the owner would ask them (in the internal notes,
ignored by git), then in English in `Banco/compiti/`, same numbering; each one
gives the request in shop language (no operation names), why it is a
meaningful test, what it deliberately leaves out, a table of checkable facts
with the arithmetic and the reason for each tolerance, and what the
measurement cannot see:

1. an L-bracket with two holes; 2. a round flange with a bolt circle;
3. a slotted plate with four chamfers; 4. a sheet-metal C-channel;
5. a hemmed sheet-metal cover; 6. the one-metre welded frame (the existing
task, ported); 7. a tube stand weldment with gussets and caps; 8. a box and
lid assembly; 9. a mirrored bracket pair; 10. the drawing of the bracket pair
with a BOM and balloons; 11. the drawing of the tube stand with its cut list;
12. the weight and bounding box of the flange; 13. the interference check of
the box and lid; 14. an STL export for 3D printing; 15. a STEP export of the
tube stand, reopened and compared.

### Language gate

- `exceptions.json`: one exception of class `transition` for the second column
  of `Controlli/lingua/name-map.tsv` (a pattern on that column, never the
  file): the gate counts those names in `transition`, which must be 0 at the
  tag, so the map leaves the tree before `v0.1.0`. Committed on its own, with
  `Language-Gate-Config-Change:`. The same exception for a translator's
  partial file was removed with the file, with the same trailer.
- `baseline.json`: `transition` 0 -> 2,165 Italian words on the commit that
  adds the map (`Language-Baseline-Raise:`), all in its second column;
  3,907 while a partial file was tracked, back to 2,165 with the merge.
  `markdown` +1 in `Controlli/lingua/name-map.tsv`, with
  `Language-Baseline-Raise:`: the English column of the one property kept as
  it is (above). Notes are written in English, without Italian words outside
  the names the tree already has.

## Part of 0.130.1 — W3.1-bis.2 item 11, the snapshot tool (branch `w31bis-2-foto`)

Tooling only: the bridge and the library are unchanged, so `VERSION.txt` is
not raised (the practice of W3.1-bis.2 on its branch).

### The snapshot every later step of W3.1-bis is compared with

- **`Controlli/lingua/snapshot.js`** (`take` / `compare` / `--self-test`).
  `take` writes a snapshot directory (default `note-interne/lingua/snapshots/<date>`,
  ignored by git: the tool and its format are versioned, the snapshot is not)
  with six sections:
  - the **catalogue**, copied as it is, with its raw and normalized sha256 and
    one hash per operation and per family;
  - the **tools** of the built bridge (`initialize` + `tools/list`), over stdio
    as a client asks them, or **by reflection when SOLIDWORKS is running**:
    started as a program the bridge attaches to SOLIDWORKS at once, so then its
    assembly is loaded and the protocol dispatcher (found by its shape, never
    the entry point) is called directly. The source used is recorded;
  - every **`Fallito(...)` call** of the `<Compile>` items of the library and
    the bridge, named `file:method:ordinal`, and every string unit
    (`file:member:sN`, `+` chains joined, run-time values as holes), read
    through Roslyn: a comment is never a literal;
  - every **textual assertion** of the cases (`il_messaggio_contiene`,
    `contiene`, `non_contiene`, textual `uguale`, found through the contexts of
    `Banco/case-keys.json`) with the case, path, step and operation, all the
    string units that contain it (ignoring case, as the bench does), the
    message templates that satisfy it across a run-time value, and the list of
    assertions with no candidate;
  - the **tests** of TWO reference ledgers: identity, label, outcome, and per
    case the outcome and the expectations attempted and checked, with the
    **unstable** cases and tests (a different outcome or count between the
    two runs);
  - the exit code and the counts of every **script** of `Controlli/` and
    `.github/passi/` that runs without SOLIDWORKS.
- `compare` reports every difference per category (tests by
  `file:method:ordinal`, never by the label text), prints the unstable cases
  apart, and exits 1 on any difference outside the unstable list, 3 when a
  section was not compared, 2 on a missing input. A snapshot is written as
  `<out>.partial-<pid>` and renamed at the end. Children never inherit
  `GIT_INDEX_FILE`/`GIT_DIR`: `expected-keys-test.js` builds a throw-away
  repository and wrote its index over the one such a variable named.
- **`Controlli/lingua/snapshot-scripts.json`** names all 82 scripts and
  programs of those folders: 36 run, 46 skipped, each with the reason (32
  SOLIDWORKS probes, the installer test, the release and history-rewriting
  tools, the hooks, anything that edits tracked files or pushes). A script it
  does not name stops the tool. Entries carry `needs`, `varies_with` and
  `volatile_lines` with their reasons (`rinomina-prodotto.js` walks the file
  system and counts ignored files: three masked lines).
- **`Controlli/lingua/snapshot.md`**: usage, exit codes, the format of every
  file of a snapshot directory, the template rule, the registry, what counts
  as a difference, and what the tool does not do (renamed identities through
  the name map of W3.1-bis.3 are not followed yet).
- `token-stream.js` can be required as a module (it restarts itself with
  `--expose-internals` only when it is the main module) and exports
  `compileRoslynProgram` and `GateError`; its own helper's cache hash is
  unchanged.

### Seen red

- `--self-test`: 18 of 18 on a fake tree and fake ledgers. A changed refusal
  literal, the literal or the template behind an assertion gone, a changed
  outcome, a count dropped to 0, a catalogue change and a tool change are red;
  the same tree, a translated label and a move of the unstable case alone are
  green; one ledger, a missing ledger, a missing snapshot file and a script the
  registry does not name are refused (exit 2). Eight mutations of a copy of the
  tool were each seen red.
- It runs with the suite: `SafetyNetSelfTests` has a fifth self-test
  (`N of M checks as expected`, at least 28 since the review below). Seen red
  in the suite by expecting the wrong exit code in one check: 17 of 18, NO.
  **Prove.Contratti: 762 checks** (761 + this one; every self-test is one
  check whatever its result).

### The adversarial review: four ways to make `compare` say "same" when it was not

Tried on a copy of the real tree against a snapshot of it, with the saved
ledger. Renaming a catalogue operation, translating a refusal, flipping a test
outcome under the same label, changing the expectations of a case, a missing
or truncated ledger were already caught. These were not, and are fixed (the
snapshot format becomes 2, and a snapshot of format 1 is refused):

- **A literal inside a refusal that the text of the call shows as a hole.**
  `text` renders `cond ? "a" : "b"`, `string.Format("...", x)` or a helper
  call as `{*}`, so translating the branch "the outline is not complete" of
  the slot refusal compared as 0 differences. 185 of the 1071 calls hold such
  a literal, and 235 have no constant text at all. Every call now carries
  `inner`, the text of each string unit inside its arguments, and a change
  there is a difference.
- **The literal behind an assertion gone while another one still contains
  the words.** The mate type a case asserts with `uguale` translated in
  `StatoAssieme.cs`: the same word is in the catalogue descriptions, so the
  assertion stayed "satisfied" and the compare said 0 differences, while the
  bench would go red. Now every member (`file:member`) whose units satisfied
  an assertion in the snapshot must still satisfy it; a unit renumbered or
  reworded in the same member that still holds the words is not a loss.
- **A ledger that is not of a full run, or with unnamed tests.** A `partial`
  ledger that happened to miss nothing compared as 0 differences, and so did
  one with `identities_unresolved` above 0. Both are now a difference.
- **A `varies_with` added to the registry after the snapshot** excused every
  other count of that script: an input absent from the snapshot compared as
  "changed". Only an input recorded then and now, with another value, excuses
  counts now.
- Smaller: `--tools-timeout` (the stdio read was fixed at 90 s, now also
  proved: a bridge that never answers is stopped and the tool exits 2, a
  bridge that ends at once exits 2, a broken pipe no longer ends the tool with
  exit 1, "a difference"); a request of the bridge to the client is not taken
  for an answer; a `TODO_MD` naming a missing file stops the tool instead of
  falling back to the default TODO.
- The self-test grows from 18 to 28 checks, one per hole above plus the same
  ledger twice (no unstable case, "not measured"), the excuse of a recorded
  input that moved, and no folder left by a take that stopped. Each new check
  was seen red on its own mutation of a copy of the tool (six mutations, each
  27 of 28). With two different ledgers the unstable case and test are listed
  apart, with the tests of that case; with one ledger twice the list is empty
  and the snapshot says it was not measured.
- Contracts on this branch were run with `PROVE_SENZA_INTEROP=1` only, because
  a full bench was running on SOLIDWORKS and the groups that start the bridge
  attach to it: 593 checks, 18 not runnable here, 9 red, none of them new: the
  bench-freshness check of `Banco` (the bench report predates the last commit
  on `Banco`), and 8 library checks (N59 x2, N26, N16, the six moves, the rule
  per direction, «elsewhere», the redundant decision) whose type initializer
  needs the SOLIDWORKS interop that the simulated mode hides.

### The first snapshot is a proof run only

Taken on the real tree with the one saved ledger (the full bench of library
0.148.0) given twice, so its unstable list is empty by construction and it says so:
99 operations, 9 tools (by reflection, SOLIDWORKS was running), 1071
`Fallito` calls in 101 files, 11841 string units, 693 textual assertions in
303 cases (9 satisfied only by a template, 77 with no satisfying literal,
mostly SOLIDWORKS data such as materials and component names), 2161 tests in
284 cases, 36 scripts run. A compare of the same tree against it: 0
differences. Taken again after the review, on the committed tree (format 2,
nothing uncommitted, about 4 minutes), with the same numbers; the compare of
all six sections with the ledger: 0 differences, exit 0. Two TODO probes exit
1 in it (`conta-todo.js`: boxes outside the `####` level; `percorsi-del-todo.js`:
bench outputs that exist only in the main checkout), which the real snapshot
records as they are. The real snapshot, with the ledger of the bench now running as
the second one and the tools read over stdio with SOLIDWORKS closed, is taken
after that bench ends. The test «every lookup by name that falls back to null
found its target» will be listed unstable (skipped on 0.124.0, run on
0.124.1): a bench change, not flakiness.

---

## 0.124.1 — 1 October 2026 · library 0.148.0 (unchanged)

**Full bench on bridge 0.124.1 / library 0.148.0, 1/10/2026: 2160 tests, 0 failed**
(1771 expectations checked). The bench's lookup log ran this time: «every lookup by name
that falls back to null found its target», 106 lookups, none null. This run and the one
of 0.124.0 are the two reference runs of W3.1-bis.2 item 11.

### The bench's lookup log runs on a full bench

- **The bench took any `--libreria` for «another library»** (W3.1-bis.2, item 3).
  `banco.ps1` always passes `--libreria`, with the same path as the bench's
  default, so the end-of-run test «every lookup by name that falls back to null
  found its target» was skipped on every full bench (seen on the bench of 0.148.0).
  Now only a different library file counts as another (`SameLibraryPath`, full
  paths without case); an older library named by hand still skips the test. A
  contract check in `ReflectionTests` reads the source and is seen red on the old
  line. Prove.Contratti: 761 checks.
- **`Prove/LEGGIMI.md` repaired:** an edit of the merge commit of 0.124.0 (d2c7dd9) read the
  file with PowerShell 5.1 `Get-Content` without an encoding and wrote it back: 26 lines with
  every accented letter, guillemet and dash turned into mojibake. Converted back (1252 to
  UTF-8); against the last healthy version only the intended edits differ.
- **W3.1-bis.2 item 10 rerun on SOLIDWORKS:** the eight probes that moved from base
  planes by localized name to base planes by position (`CorpoRotto`, `DimXpert`, `Lamiera`,
  `Mensola`, `Orfana`, `RaccordoDiFaccia`, `SeguiIlBordo`, `Spessore`) were run old (built from
  2df71c4) and new, one at a time: the same output line for line (titles, paths and
  times normalized). `Spessore` needs SOLIDWORKS Utilities loaded (`--carica`): its first
  thickness analysis after loading the add-in answers differently from the next ones,
  old and new alike; with the add-in already loaded the two versions give the same
  numbers.

## 0.124.0 — 1 October 2026 · library 0.148.0

**Full bench on library 0.148.0, 1/10/2026: 2159 tests, 0 failed** (1771 expectations
checked), the new mate case green, and the test ledger written (2161 tests, 284 cases, 0
identities unresolved). One net did not run: «every lookup by name that falls back to
null found its target» was skipped, because `banco.ps1` always passes `--libreria` and
the bench took any `--libreria` for «another library». Fixed right after this entry
(see the next version). Prove.Contratti: 760 checks, 0 failed.

Two lots, merged as two squashed commits: W3.1-bis.2 (the safety nets before the English renames) and Z-C14 (the second number of a mate).

### W3.1-bis.2, the safety nets (branch w31bis-2)

**The nets that must turn red when the translation of W3.1-bis renames a
protocol name, a case key, a catalogue name or a namespace and breaks something
in silence (TODO entry W3.1-bis, step .2, items 1-10).** Built on bridge
0.123.0 / library 0.147.0; the version is not raised: no operation, message or
tool of the bridge or the library changes. Every net was seen failing on fake
cases, and where it could the seen-red stays as an automatic check (a contract
group or a self-test). **Item 11 is NOT done**: the snapshot and the two
reference bench runs come next, on SOLIDWORKS. On the full bench of 0.148.0 the
case-key loader refused nothing and the test ledger was written. Prove.Contratti: 736 checks (693 before),
only the three bench-freshness checks red (the last run is 0.146.0).

- **Item 8, `<Deterministic>` (its own commit).** `Oce.Solid.Agent.csproj`,
  `Banco/ProvaSaldatura.csproj` and `Prove/Prove.Contratti.csproj` build with
  `<Deterministic>true</Deterministic>`: two builds of the same source give the
  same bytes, so the IL before and after a translation lot can be compared.
  None of them uses a version wildcard, and no code reads PE timestamps.
  Measured with `Controlli/lingua/same-il.ps1 -OnProjects`: before the change
  the three projects differed in bytes between two builds; after it, 0 lines and
  identical bytes in all five projects. The bench and the contracts also get
  `<PathMap>`, so `[CallerFilePath]` (item 7) embeds `Banco\File.cs`, not the
  absolute path of the checkout.
- **Item 1, the README counts in both languages.** `ProveCatalogo.Numero()`
  reads Italian 0-99 (as before) and English 0-999 and digits, in the README
  forms «N operations», «N drawing operations», «drawing operations are N».
  The silent `if (n < 0) continue` is gone: a word that looks like a number
  and cannot be read is red, and a README with no count read at all is red, so
  the check cannot go blind after the translation. Seen red on every run by 17
  fakes («ninety-six drawing operations» against 94, a misspelt Italian 94 that
  used to be skipped, «fourty»...). Not done: Italian hundreds (the word for
  100 is a new Italian protocol element the gate blocks); a hundred with a tens
  root or a unit in it is red, not silent, and the rest is documented in the code.
- **Item 2, the refusals that name a remedy, in both languages.**
  `ProveIRifiuti` has a table of 29 Italian remedy words with their English
  forms (90 in all). The share is now a floor kept as the exact fraction
  236/935, so ONE new mute refusal is red (the header promised it, the code only
  checked the absolute count). Every Italian word has English partners that
  catch its sample; on the Italian refusals the English forms add nothing (209
  of 817 either way, and 75 of 578 on the tree at b0587d6). Floor 234 -> 236:
  two English refusals that really name a remedy. Seen red on fake sources on
  every run (one more mute refusal: 236 of 936 is under the floor); by hand,
  the English form «indicate » made the Italian-subset check red.
- **Item 3, the names looked up by reflection (`Prove/ReflectionTests.cs`).**
  Static: the literal names handed to `GetType`, `GetMethod`, `GetField`...
  and to 19 helpers that pass them on (discovered, not listed) are extracted
  from `Prove`, `Banco` and `Controlli` (333 names in 27 files) and must resolve
  on the built bridge, library and catalogue exporter. Run time: the contract
  helpers log every lookup by name through `ReflectionLog`, and the last group
  is red if one came back null (399 lookups in a full run, none null). Seen red
  on every run on fakes; by hand, `GetField("Riuscito")` renamed in
  `Banco/Program.cs` gave «1 do not resolve». The bench has its own log,
  `Banco/ReflectionLog.cs` (added by the review): a member name resolves as
  soon as ANY type declares it, and 14 of the 58 direct member names of `Prove`
  and `Banco` are declared by more than one type, so a stale name read with `?.`
  (the schemas of the mutations, the catalogue count of the header, the
  signatures of the state) could pass the static net and go on with null. Those
  lookups go through the log, a full or partial run ends with the test «every
  lookup by name that falls back to null found its target» (skipped with
  `--libreria`, whose older library lacks newer names by design), a mutation or
  regression run makes a miss a bench error, and a contract forbids a `?.`
  read of a lookup anywhere else in `Banco/`. Both seen red on fakes on every
  run; the bench-side test has not run yet (it needs SOLIDWORKS).
- **Item 4, the bench refuses unknown case keys.** `Banco/case-keys.json`
  lists, for 13 places in a case, the keys the bench reads, the documented
  ones, the children and 184 frozen free keys (prose notes; 182 distinct keys
  over 302 cases, none a misspelt schema key). `Banco/CaseKeys.cs` checks every
  declared case, every mutation and every regression case before anything is
  built: an unknown key is a bench error naming its path
  (`passi[2].deve_fallire.volume_unchanged`). Contract `CaseKeysTests` (15
  checks): every case of the tree is clean, the frozen list only shrinks
  (`FrozenFreeCeiling` = 184, to 0 in W3.1-bis.4), no frozen entry is unused,
  every read key is a literal the bench sources read. The review found that
  last check green on a reader renamed alone: a doc comment still quoted the
  key «document», and the self-tests of the bench WRITE every expectation key
  in fake cases. Comments and writes («["key"] = ...», «{ "key", ... }») no
  longer count as a reader; seen red with the reader of «document» and the
  reader of the tolerance renamed alone. Seen red on every run: a
  made-up key in each of the 13 contexts, a read key renamed as a wrong
  translation in 12, 12 malformed lists. The pending case
  `a_dimension_hidden_by_hand_does_not_count` would be refused as it stands.
- **Item 5, JSON readers that fail when a key is missing.**
  `conta-provate.js`, `controlla-catalogo.ps1`, `domande-senza-risposta.js`,
  `prova-x2.js` and `detect-italian.js` exit non-zero on a missing or renamed
  key instead of reading `|| []` or `$null` as nothing. `controlla-catalogo.ps1`
  had a real silent defect: `famiglie` is an object and was read as a list, so
  0 declared families were checked; it also gains an interop resolver
  (`-SwInstallPath`, `SW_INSTALL_PATH`) and now runs to the end on this machine.
  A lexicon without `content_words` used to leave the language gate blind with
  exit 0. The permanent check is `Controlli/expected-keys-test.js` (28 cases
  with a built library): against the old readers, 21 of 24 cases were not as
  expected. The review added one: `conta-provate.js` read the bench's own C#
  inside an empty `catch`, so a renamed folder counted as «touches nothing»;
  now it exits 1 (`--bench <folder>` for the fake).
- **Item 6, the strings another program looks for.**
  `Controlli/lingua/consumed-strings.js` and its inventory
  `consumed-strings.json` (39 entries, 46 runs): installer and bridge, the bench
  report lines `banco.ps1` parses, the release script and this suite's summary,
  the CI proof and the language gate, and the other pairs the TODO lists. Each
  consumed string must be in a string literal (never a comment) of its producer;
  consumers are bound by the SHA-1 of their literal, `--rebind` after a
  translation lot. Contract `ConsumedStringsContract` runs `--self-test` (25
  fakes: the string taken out, kept only in a comment, translated on one side,
  a regex alternative no longer produced...), then the check, then a floor of
  39 entries. By hand, translating the partial-run marker of `Banco/Program.cs` broke
  two entries. Not done: producers are checked on the source literals, not on
  the ldstr heap of the built assemblies.
- **Item 7, the bench report names every test.** `Segna`, `SegnaAttesa`,
  `Saltata` and `ErroreDiBanco` take the caller's file, member and line;
  `Banco/TestLedger.cs` gives each test the identity `File.cs:Method:ordinal`
  (plus `@case` and `#n`), keeps per case the outcome and the expectations
  attempted and checked, and writes `Banco/resoconto/tests.json` in every mode.
  One new console line (`test ledger: ...`) and, in full and partial runs, one
  more test: «every test has its identity, file:method:ordinal». Existing lines
  and what `banco.ps1` parses are unchanged. Contract `BenchReportTests` (10
  checks): the source scanner on a probe of 14 call shapes and against the
  compiler's `CallerMemberName`; a wrong member or a line without a call stays
  unresolved, never guessed. By hand, the scanner agreed with Roslyn's syntax
  trees on 2,421 real recording calls. Not done: the bench-side check seen red
  on a real run (it needs the bench).
- **Item 9, `Controlli/lingua/token-stream.js`.** The token streams of C#
  (Roslyn from MSBuild), JavaScript (acorn, Node's own copy) and PowerShell (its
  own parser), without comments and whitespace, literals compared by value;
  modes identical, strings and renames (with a TSV map). `--self-test`: 31 of
  31, with a failing case in every language for every mode; making the
  identical mode accept everything gives 27 of 31. Not done: shell, YAML and
  csproj (no tokenizer on this machine, nothing installed).
- **Items 8 and 9 in the suite.** The self-tests of `token-stream.js`,
  `expected-keys-test.js`, `prova-x2.js` and `same-il.ps1` ran only from the
  pre-push hook; the new group `SafetyNetSelfTests` (4 checks) runs them with
  the contracts, on every machine and on the CI runner. A self-test that says it
  cannot run here (exit 2) is «not runnable here», never green. Seen red on
  1/10/2026 with the identical mode of `token-stream.js` accepting everything.
  `Controlli/lingua/same-il.ps1` (ildasm without line numbers, MVID and
  banner normalized; modes Identical, Ldstr, Renames; `-SelfTest` 17 of 17)
  stays a re-runnable command for the five projects (`-OnProjects`, about 6
  minutes, needs MSBuild and the interop on disk).
- **Item 10, the base planes by position, not by localized name.** Eight
  probes of `Controlli/` selected a base plane by its Italian name (sometimes
  with the English one as fallback): `CorpoRotto`, `DimXpert`, `Lamiera`,
  `Mensola`, `Orfana`, `RaccordoDiFaccia`, `SeguiIlBordo`, `Spessore` (the TODO
  says seven; `RaccordoDiFaccia` kept the old code in the batch of 28/9 that
  gave its siblings the new rule). Each now takes the first, second or third
  `RefPlane` feature of the tree, the rule `Nervatura`, `Riempimento` and
  `TipoDellEstrusione` already used. The library itself left that rule in
  Z-C21.5 for `Riferimenti.BasePlane` (by geometry): each new helper says so,
  and moving the probes to the geometric rule would need `lib = $true` in
  `costruisci-controlli.ps1`. All eight compile (`build.ps1` builds every
  probe). **Not rerun on SOLIDWORKS**: «the same outcome as before» waits for a
  free SOLIDWORKS.
- **Not done in this step**: item 11 (snapshot and reference bench runs); the
  bench-side null-lookup log seen on a real run; the ldstr cross-check of item 6; Italian hundreds
  in `Numero()`; the probes of item 10 on SOLIDWORKS; `<Deterministic>` in
  `Installer/Installa.csproj`; the silent readers outside `Controlli/`
  (`.github/passi/quanto-non-ha-girato.ps1`, the answer readers of
  `Banco/*.js`). The floors (`Pavimento` 236, 250 literal names, 300 lookups,
  39 consumed strings) go down with the tree only with the reason written next
  to them.

### Z-C14, the second number of a mate (branch z-c14)

**`accoppia` now measures which directions a mate blocks, and refuses a
REDUNDANT mate (TODO entry Z-C14, RAG note 82d).** Built on bridge 0.123.0 /
library 0.147.0. On the full bench of 0.148.0 the new case and the existing cases
that call `accoppia` are green.
Prove.Contratti: 708 checks run in this worktree (710 with `TODO_MD` pointing at
the TODO, which a worktree does not have), only the two bench-freshness checks
red plus the README count: a worktree of main built the same way runs 690 of
the 693 its README states, so the README says 693 + 18 = 711, the 18 being the
new group (17 as written, one added by the review).

- **Why.** The mate count before/after was the whole judgement, and a redundant
  mate raises it too. Nothing in SOLIDWORKS sees one: probe
  `zc14/probe_mate_dof` (30/9/2026, two 40 x 40 x 20 blocks, C1 fixed) read the
  same coincident added twice as AddMate5 NoError, GetWhatsWrongCount 0, mate
  feature error 0 and GetConstrainedStatus 2, as for the first one;
  GetRemainingDOFs answers Unavailable in every state.
- **The perturbation test (`ReadBlockedDirections`).** Each non-fixed
  top-level component of the pair is moved 5 mm along X, Y, Z and turned 5
  degrees about X, Y, Z of the assembly through its origin, one move at a time:
  IComponent2.Transform2 set from IMathUtility.CreateTransform, EditRebuild3,
  Transform2 read again. Only the moved direction is judged
  (`DirectionBlocked`): a translation is blocked when it is back within
  0.01 mm along its axis, a turn when the angle is back within 0.01 degree. The
  probe's first rule judged the whole transform and read the parallel's turns
  free, because the solver put each turn back while sliding the block 1.667 and
  2.435 mm along a free direction. The probe read 0 blocked with no mate, 3
  (TY, RX, RZ) with one coincident, 3 again with the same coincident repeated,
  4 with a parallel added, 6 fully mated. After each move the component goes
  back where the move started; at the end it gets its original transform, is
  rebuilt and read, and the drift left is reported. Cost: 13 rebuilds per
  non-fixed component per reading, read before AddMate5 and after it.
- **Never a guess.** A transform that cannot be set or read, a set that did not
  move by the step asked, a rebuild answering false or an orientation that does
  not come back makes the reading «unknown» with the reason, and the mate is
  kept. A fixed component is not moved (fully blocked by definition); a
  component inside a sub-assembly is not moved either (unknown).
- **The verdict (`MateVerdict`).** «removes» when any direction is newly
  blocked; «redundant» only when nothing is new and one side is anchored (fixed
  or already 6/6 blocked); «not_judged» with two free components (moving one
  can drag the other); «unknown» on an unreadable or self-contradicting reading
  or an anchored mate that reads zero blocked. A redundant mate is taken out by
  the object AddMate5 returned (`mate as IFeature`, Select2, DeleteSelection2,
  both counts read again, `MateRemovalNote`) and the step is refused, saying
  which directions were already blocked and that AddMate5 NoError and
  GetWhatsWrongCount 0 do not see it.
- **The answer** adds `blocked_directions_before`, `blocked_directions_after`,
  `degrees_removed`, `direction_test`, `residual_drift_mm`,
  `residual_drift_deg` (per component, in the order of the counts) and
  `mate_verdict`; `giudizio` is now in English and names what is still free.
  The catalogue entry says what is measured, the refusal, when the test stays
  unknown and the cost.
- **Not measured yet, for the first bench:** `mate as IFeature` on the object
  AddMate5 returns (the guide and probe zd345 confirm it for GetMates objects),
  Select2 + DeleteSelection2 on a mate feature, EditRebuild3 answering true
  during the test, GetWhatsWrongCount 0 after the repeated coincident.
- **Bench:** new case `assiemi/the_same_mate_twice_is_refused_as_redundant`
  (red on 0.147.0 at step 2: the same coincident again is accepted because the
  count goes from 1 to 2; step 4 is the probe's parallel state and checks the
  rule of the moved direction inside SOLIDWORKS); `un_accoppiamento_non_basta`
  now wants «still free in TX, TZ, RY», blocked TY, RX, RZ and 3 degrees
  removed. `Banco/LEGGIMI`: assemblies 21 to 22.
- **Contracts:** `Zc14MateDirectionsContract`, 17 checks as written (18 after the review): the probe's S3 rows
  against both rules, six source rules each seen red on changed copies of the
  real source (26 mutants), and the pure rules by reflection on the built
  library (`MovedForTest`, `DirectionBlocked`, `MateVerdict`,
  `MateRemovalNote`).
- **Review of the branch (30/9/2026), two fixes before the first bench.**
  (1) The probe named three outcomes of a move -- BLOCKED, free, ELSEWHERE --
  and the first code kept two: a move the mates put back only in part read
  «free». A face up to 8 degrees off the axis (the face is chosen with a dot
  above 0.99) puts a translation back only in part, and a direction read free
  before and after a mate that holds it is a mate called redundant and taken
  out. Now a move that after the rebuild is neither back (`DirectionBlocked`)
  nor where it was moved (`MoveTookEffect` on the same reading) makes the
  reading unknown and the mate is kept; none of the probe's rows fell there.
  (2) The test set back only the component it moved; a rebuild can carry a
  component mated to it along. The other top-level non-fixed components are
  now read before the test, any that is away from where it was is set back in
  the same final rebuild (still 13 rebuilds), and what is left is said in the
  answer. One more contract check (18) and six more mutants (32).

## 0.123.0 — 30 September 2026 · library 0.147.0

**The four items the 0.122.0 entry listed as not done in Z-C21.5: the two «last
sketch of its type» lookups (S7), N7, N23 and C4-3; and Z-C21.11, the assembly
components that were not read: lightweight, envelope and hidden (N5, N27, M23).**
Built on bridge 0.122.0 / library 0.146.0. **Full bench on library 0.147.0,
30/9/2026: 2139 tests, 0 failed** (1754 expectations checked), the seven new cases
(`saldature/the_end_cap_skips_a_drilled_wall` and the six of Z-C21.11 below)
green. Prove.Contratti: 693 checks, 0 failed.

- **S7, the sketch just drawn (`SchizzoAppenaCreato`, `SelezionaSchizzoAppenaCreato`,
  `BloccaSchizzo`).** They took the LAST `ProfileFeature` of the tree. Now the
  sketch is the one `ProfileFeature` born since the caller took
  `FeatureNamesNow` just before selecting the plane or face
  (`FeatureBorn`, the same decision 0.146.0 uses for planes and axes). The nine
  callers are the rectangle, profile, circle, cylindrical cut, rectangular cut,
  slot, extrusion on a face, revolve and text operations; the profile extrusion
  and the revolve lock the sketch found the same way. Messages are kept: none
  born says the old sentence, an unreadable tree adds why, two or more born is a
  new refusal instead of taking the later (`SketchNotIdentified`). The cast of
  `ISketchManager.ActiveSketch` to `IFeature` was not used: only a VBA example
  does it, no Remarks says it. Contract `Zc215SketchIdentityContract` (4 checks,
  seen red on 8 mutants of the source); no bench case can be red on 0.146.0 by
  construction (no operation moves the rollback bar or leaves a sketch unborn).
- **S7, the sweep path (`UltimoSchizzoFeature`, deleted).** `sweep_circolare`
  took the last sketch of the tree, 2D or 3D. Now the path is the one
  `3DProfileFeature` born since the names taken before `Insert3DSketch`; the old
  sentence is kept through `SketchNotIdentified`. Covered by the same contract.
- **N7, an end cap on a drilled wall (`tappo_estremita`).** «Planar with more
  than one loop» also held for both faces of a tube wall with a hole drilled in
  it, and they went to `InsertEndCapFeature3` as ends. `Riferimenti.OpenEndsOnly`
  now takes such a face only when every face across each inner loop was made
  (oldest owner, `IFace2.GetFeature`, compared by `IFeature.GetID`) by a feature
  that also made a face across the outer loop or the face itself. The faces left
  out are counted in the new `misure.machined_faces_skipped`; a face that cannot
  be read stops the step, and so does a body whose faces cannot be read (it used
  to be skipped). The "no open end" refusal is rewritten in English and counts
  the faces left out. Catalogue: the description and the `estremita_libere`
  genre say it. New bench case `saldature/the_end_cap_skips_a_drilled_wall`
  (red on 0.146.0 by construction: four faces instead of two went to the cap).
- **N23, the hole point (`AncoraIFori`).** Of the sketch points within 1e-7 m
  of the hole it kept the last one found. Now all are collected and
  `SameHolePoint` proves they are one point by the sketch point's own ID
  (`ISketchPoint.GetID`, unique within a sketch: ISketchPoint~GetID.html) or
  refuses, naming every candidate in millimetres with its ID. Two points at the
  same coordinates with two IDs are two points (two coincident holes) and are
  refused; an ID that cannot be read proves nothing. Contract
  `Zc215HolePointIdentityContract` (8 checks); no bench case can place two
  sketch points that close on purpose.
- **C4-3, a document opened or activated with a pending rebuild (the bridge's
  open and activate tools, through `PortaDavanti`).** 0.144.0 (`DocumentByPath`) had already fixed the name
  half (the path and the extension instead of `GetTitle()`). What was left: the
  `Errors` bitmask of `ActivateDoc3` was thrown away. Now
  `Sessione.NeedsRebuildAfterActivation` tests `swDocNeedsRebuildWarning` and
  `PortaDavanti` calls `EditRebuild3` before answering, with
  `rebuilt_on_activation` and its reason in the answer (ActivateDoc3.html,
  Remarks). Contract `Zc215ActivationRebuildContract` (10 checks); no bench case
  can stage a document left stale by a separate activation.
- **C4-3 in the library's own activations** (left open by the first review, done
  after it). The six `DocumentByPath.ActivateAndReadBack` calls of
  `OperazioniEsporta`, `OperazioniLamiera` and `SezioneProfilo` now read the
  bitmask through `DocumentByPath.NeedsRebuildAfterActivation`. OUR document,
  brought forward to be exported (STEP AP242, flat pattern DWG), is rebuilt with
  `EditRebuild3` first (`RebuildIfActivationAsked`), and the step's log says so;
  a false answer is said, not a refusal, since a part with a feature in error
  answers false on every rebuild and was exported the same way before. The
  user's document, put back in front afterwards, is only reported
  (`PutBackNeedsRebuild`): a rebuild nobody asked for would change it. Contract
  `Zc215ActivationRebuildContract` +2 checks (every library call acts on the bit;
  the library's bit test by reflection), 12 in all.
- **Two more «last new feature of a type» loops** (seen by the first review
  outside the rows). The sketched bend (`OperazioniLamiera`) and the rib
  (`OperazioniNervatura`, its sketch and the `Rib` itself) kept the LAST feature
  of the type whose name was new. Now both go through `FeatureBorn`: two sketches
  born refuse with `SketchNotIdentified`, two ribs from one `InsertRib` refuse and
  go back out with the rest, and a step taken away removes every feature of the
  type it made, not one. The bend's «before» is `FeatureNamesNow` (null when the
  tree cannot be read). Contract `Zc215SketchIdentityContract` +2 checks (no such
  loop in the library; seen red on a copy with the old loop), 6 in all. The rib
  and bend cases of the bench are the judges of unchanged behaviour.

**Z-C21.11, which components of an assembly count.** Two probes on 30/9/2026
(SOLIDWORKS 2021 SP5.1, `probe_alleggeriti` and the indented-BOM probe; RAG note
82zz_undecies) measured:
a lightweight component answers `IsSuppressed()` TRUE and `GetModelDoc2` null,
while `GetSuppression2` says 1 (lightweight), `GetBodies3` gives its bodies with
the right volume, `Transform2` is there and `IsHidden(false)` is false; an
assembly reopened with OpenDoc6 Silent|OverrideDefaultLoadLightweight|LoadLightweight
(193), nothing of it left loaded, has every component lightweight; an envelope
(InsertEnvelope, or `CompConfigProperties6` with IsEnvelope true) is left out of
the assembly's `CreateMassProperty` and written to the STEP; a hidden component
(`Visible` = swComponentHidden) is weighed and NOT written to the STEP; the STEP
export resolved every lightweight component; no material reader answered on a
component, resolved or not; the indented BOM (Detailed) writes the tree, not the
parts: A + 3 x S_show(2 B) + B gives A 1, S_show 3, S_show > B 2, B 1.

- **One reader, `AssemblyComponents` (new file).** The load state from
  `GetSuppression2` (suppressed, lightweight, resolved, unknown), part or
  sub-assembly from the extension of the saved path (so a lightweight one keeps
  its kind), `IsEnvelope`, hidden = `IsHidden(false)` here or on a parent,
  `GetModelDoc2` only for a resolved component. `PezziDellAssieme`, which asked
  `IsSuppressed` and so took every lightweight part for suppressed, is gone; the
  mass and the box go through `ComponentsOrRefusal`, which refuses when a load
  state cannot be read. Review: when the extension is neither .sldprt nor
  .sldasm the kind comes from the document, as up to 0.146.0 (a resolved
  component with another extension was otherwise dropped in silence from the
  mass, the box and the STEP count); a component of unknown kind by both
  refuses the mass and the box and makes the STEP count unknown (null).
- **`proprieta_di_massa` on an assembly.** Envelopes are left out of the sum of
  the parts as SOLIDWORKS leaves them out of the mass (they made a right
  assembly refuse with a gap, N27), named in `envelope_components`; hidden parts
  are weighed and named in `hidden_components_weighed`; lightweight parts refuse
  the step with their names (their material cannot be read), unless the new
  boolean argument `resolve_lightweight` is true: then
  `IAssemblyDoc.ResolveAllLightWeightComponents(false)`, the list is read again,
  a part still lightweight refuses, and `lightweight_resolved_on_request` says
  what was resolved (review: a refusal that comes after the resolution says it
  too, since the parts stay resolved). The old list of components whose document was not read is gone. The step used to
  refuse «nothing to weigh, all suppressed» on an assembly opened lightweight.
- **`ingombro_corpo` on an assembly.** Lightweight parts are measured through
  `GetBodies3` without resolving them (`lightweight_components_measured`);
  envelopes and hidden parts are in the box and named (`envelope_components`,
  `hidden_components`). A throw of `GetBodies3`, or a lightweight part that
  answers no solid body, refuses instead of leaving the part out of the box (a
  partial box answered as a success, N5). The step used to have no box on an
  assembly opened lightweight and a partial one with one part lightweight.
- **`esporta_step` of an assembly.** Hidden components are left out of the
  expected solids and named in `hidden_components_not_exported` (the old count
  asked for them and refused a right file); lightweight parts count (the count
  is made after the export, which resolves them); `GetLightWeightComponentCount`
  is read before and after the export (`SaveAs3`, or `PublishSTEP242File` for
  AP242: review), and `lightweight_resolved_by_the_export` says how many the
  export resolved; envelopes are written and counted.
- **`distinta_base_in_tavola`.** Suppressed by `GetSuppression2`, not
  `IsSuppressed` (a lightweight sub-assembly and all its children were left out);
  a sub-assembly by its extension; a lightweight sub-assembly that is not
  suppressed refuses with a reason (its BOM option is in its document), and so
  does an unreadable load state, the reason passed to the answer. The INDENTED
  table is now compared with the measured tree (M23): siblings grouped by file
  and configuration, the children of ONE instance under a sub-assembly row, a
  Hide sub-assembly one row with nothing under it, a Promote one no row; until
  0.146.0 every indented table of an assembly with a sub-assembly was refused.
  New answer key `counted_as`. Catalogue texts of the four operations say it,
  in English.
- **Bench.** The assembly builder has three new keys, `envelope`, `hidden` (per
  component, read back) and `reopen_lightweight` (saves, closes every document
  of the case, checks nothing is left loaded, reopens with 193 and checks
  `GetLightWeightComponentCount`), and a repeated child `nome` inside a
  sub-assembly is a second instance of one part (`Banco/LEGGIMI.md`). Six new
  cases: `assiemi/the_lightweight_assembly_has_a_box`,
  `the_lightweight_assembly_is_weighed_when_asked`, `the_envelope_does_not_weigh`,
  `the_hidden_component_is_not_in_the_step` (each red on 0.146.0: no box, the
  wrong refusal, the envelope in the sum, two solids expected for one), and
  `the_lightweight_step_says_the_export_resolved_them` (green on 0.146.0 except
  for the new answer field, as the case says); `tavola/the_indented_bom_follows_the_tree`
  (red on 0.146.0: 2 flat items against 4 rows). The two risks named before the
  bench did not happen: the bench's state reading left the parts lightweight
  (two components still counted lightweight after the box), and the station's «resolve
  lightweight: prompt» option opened no dialog.
- **Contracts.** New group `Zc2111ComponentsContract`, 28 checks: ten source
  rules, each seen red on changed copies of the real source (no `IsSuppressed()`
  in the library, the reader, the kind of a component, its four users, mass, box, STEP count and
  answer, BOM), and by reflection `LoadFromSuppression2`, the extension rules and
  `ExpectedBomItems` on the indented probe trees. `AssembliesZc212terContract`:
  the indented T_hide check follows the M23 measurement (A 1, S_hide 1).
  Refusal floor 232 -> 234; the undecided obsolete-call count of
  `OperazioniMisuraAssieme.cs|Add` 2 -> 1 (the new loops type their receivers).

## 0.122.0 — 30 September 2026 · library 0.146.0

**The configuration is always passed (TODO entry Z-C21.4) and identity by the
object, not by position (Z-C21.5), with the ids the audit rounds assigned to
Z-C21.5 (S19, C3-6 extended), the verdicts of the Z-C21.3 probes, S1 in parts only
(Z-C21.13) and the turned-view leftover of the Z-C21.8/9 review.** Built on bridge
0.121.0 / library 0.145.0. **Full bench on library 0.146.0, 30/9/2026: 2085 tests,
0 failed.** A first run was stopped: it had been started without rebuilding the
bench, and this lot changes `Banco/AssiemeDiProva.cs` and `Banco/CasiDichiarati.cs`.
The eight new cases were written red on 0.145.0 by the guide or by the code, not by
a measurement; on 0.146.0 they are green.

- **L11-1, `inserisci_componente`:** `configurazione` is checked against the
  configurations of the open file before inserting (names compared whole) and a
  name the file lacks is refused with nothing inserted. AddComponent5 gets
  UseConfigForPartReferences true and the named CurrentSelectedConfig option; the
  configuration the new component references is read back, and a different one
  takes the component out again (said removed only after counting the components).
  Case `assiemi/the_component_comes_in_with_the_configuration_asked`.
- **L16-2 and L16-3, `distinta_base_in_tavola`:** the view's configuration is read
  first; when the assembly has another one active, the view's is activated for the
  reading and the previous one put back in a finally, each read back; the step
  refuses before inserting the table if either switch fails. New answer keys
  `configurazione` and «assembly_active_configuration». The obsolete
  `IComponent2.ExcludeFromBOM` is replaced by `GetExcludeFromBOM2` in the owner
  configuration; an unreadable exclusion refuses instead of meaning «included»
  (Frozen key removed). Bench builder: `configurations` in `assieme` and the step
  key «activate_before». Case `tavola/the_bom_follows_the_view_configuration`; its
  step 5 doubles as the L16-4 probe (top-level table on a configuration that is not
  active), which stays open.
- **M18, `sostituisci_componente`:** a configuration the new file lacks is refused
  before the swap; after it, the configuration of the component(s) that took the
  place is read back into `configurazione`, with «configuration_asked» and
  «configuration_as_asked»; a mismatch opens the verdict with a WARNING and the swap
  stays. The ManuallySelect switch waits for the N57 probe. Case
  `assiemi/the_replaced_component_says_its_configuration`.
- **M32, `distinta_taglio_in_tavola`:** not changed, documented: the catalogue no
  longer says an empty configuration is the view's, and says what the comparison
  counts (top-level cut-list folders, active configuration).
- **L8-5 closed as NOT REAL** by the Z-C21.3 probe (comment in
  `OperazioniProprieta.cs`); the ForceRebuild3 before the weldment Get6 loop stays
  as a defence. L17-1/M10 and L6-3/S3 verified already fixed.
- **S7, the feature a call made:** `FeatureNamesNow` / `FeatureBorn` take the one
  feature of the type whose name was not there before the call; the old last-feature helper and
  every `IFeatureByPositionReverse(0)` are gone. Used by `crea_piano`, `crea_asse`
  (L8-6: on sheet metal it no longer finds the flat pattern), `piano_per_tre_punti`,
  `guscio`, `loft_fra_due_cerchi` (N13), the rib offset plane and `SenzaLoSchizzo`.
  Case `lamiera/the_axis_and_the_planes_of_a_sheet`, contract
  `Zc215IdentityContract`. The two «last sketch of its type» lookups
  (`SchizzoAppenaCreato`, `UltimoSchizzoFeature`) are left for a follow-up.
- **S19 and C3-6 extended, base planes by geometry:** `Riferimenti.BasePlane` finds
  front, top and right by their normal (same sign) through the origin, as probe
  C3-6 read them, the first in the tree on a tie; used by `TrovaPianoBase`,
  `SelezionaPiano`, the part view section and `specchia_componente` (no more counting
  RefPlanes in tree order). On an assembly the plane transform was not probed: the
  existing mirror cases are the check. Contract `Zc215IdentityContract`.
- **S5, bodies by signature:** `ingombro_corpo`, `proprieta_di_massa` and
  `verifica_geometria_corpo` resolve «corpo_uid» through the state and FirmaCombacia
  and refuse a uid that now names another body; the answer names the body by the uid
  asked (it said C1 for C2). Case `misura/a_body_named_before_the_cut_is_not_measured`.
- **M1, `accoppia`:** face directions are judged in assembly space (Transform2 of
  the component); an unreadable placement refuses. Case
  `assiemi/the_turned_pin_is_mated_by_assembly_directions`.
- **L11-3 and L11-5, nested components:** `fissa_componente` and
  `sostituisci_componente` refuse a component inside a sub-assembly and name the
  sub-assembly; the swap counts as done only when a component newly on the new file
  exists and the target left its old file («took_its_place»). The assembly state
  writes «top_level» (bool, null if unknown) on every component and
  «nested_components»; `fisso` is null on a nested component, and
  the counts of fixed, non-fixed and resting components and the
  constrained verdict count top-level components only. The bench builder makes
  sub-assemblies. Case `assiemi/a_component_inside_a_sub_assembly_is_refused`.
- **M19, undo recording:** started on the document of the plan, finished on the
  document that holds it, closed and reopened when a step makes a new document; the
  state is read from the plan's document; `_sw.IActiveDoc2` reads in
  `EsecutorePiano.cs` go from 5 to 2. Contract `ProveIlDocumento` (+4 checks). A live
  probe through the bridge is still to run.
- **M4 and L13-3 closed as already fixed in 0.144.0** by `DocumentByPath`.
- **S1 in parts:** `ApiHelpers.ReadDimension` reads GetSystemValue3(swThisConfiguration,
  null) through `OneDimensionValue`, which throws on any answer that is not one
  number; the obsolete `IDimension.SystemValue` is gone (Frozen key removed). The
  drawing readers stay on GetSystemValue2, since drawing-owned dimensions answer null
  (probe_dimension_drawing_owned). The part state writes null, not 0, for a
  dimension it cannot read. Contract `Zc2113PartDimensionContract`, before/after case
  `quote/the_dimension_is_read_in_the_active_configuration` (green on 0.145.0 by
  design).
- **N80 in the turned view (`OperazioniTondoTavola.cs`):** `QuotaFraDue` asks
  `NonCresciuta` first; a dimension born without its object is recorded by its three
  callers instead of dropped, and the silhouette-pair loop stops on it. Contract
  check in `Zc2189DrawingContract`; no bench case can make SOLIDWORKS do it.
- **Integration:** catalogue texts of the operations above rewritten in English;
  new undecided obsolete-scanner calls removed by typing their receivers
  (`IConfiguration`, casts of `bool?`) instead of raising the frozen counts; floor of
  refusals with a remedy 230 -> 232; `Prove/LEGGIMI` 639 checks (636 before the bench summary adds its own); `Banco/LEGGIMI` case
  counts; language baseline lowered.
- **Review:** the exclusion from the BOM falls back to the unnamed form when the
  named GetExcludeFromBOM2 throws, not only when it answers something that is not one
  Boolean (a COM error there made every BOM with a child refuse); the summary of the
  bench's `NascondiPrima` is back on its method (the new `ActivateBefore` had been
  inserted between them). Still open from the audit list of Z-C21.5 and not in this
  lot: N7 (a drilled tube's side face taken for an open end), N23 (the last coincident
  sketch point of a hole kept without saying it was ambiguous) and C4-3 (rebuild on
  activation).

## 0.121.0 — 30 September 2026 · library 0.145.0

**Returns and messages (TODO entry Z-C21.8), and the catalogue, comments and RAG
notes that said the wrong thing (Z-C21.9), with the findings the audit rounds
Z-C21.10 and Z-C21.12 assigned to them and the NOT-REAL findings of the Z-C21.3
probes.** Built on bridge 0.120.0 / library 0.144.0. **Full bench on library 0.145.0,
30/9/2026: 1986 tests, 0 failed**, after the first run had shown that
IExportPdfData.SetSheets answers false on PDFs that come out complete (the L13-6
bullet below says what changed). Every behaviour change has a bench case or a
contract test that is red on 0.144.0.

- **Export and save warnings (L13-7).** The note that `ErrorCodeDecoder` had no
  callers was stale (`SaveErrorNames` already decoded the SaveAs3 Errors). What was
  missing is now there: the Warnings mask is decoded by `swFileSaveWarning_e` name
  on STL, STEP/IGES, `esporta_pdf` and `salva_documento`; the answers carry
  `save_warnings` (null for AP242, whose call has no mask) and `save_warnings_note`
  when the model needed a rebuild, had rebuild errors or had views needing an update;
  failure messages give the names next to the raw codes.
- **User preferences put back only as read (N17).** `UserPreferenceSnapshot`
  replaces the fixed 0/false defaults of the STL and exchange exports: only
  preferences actually read are written back, the Set answers are read, toggles are
  read back, and `preferences_not_restored` names any that did not go back
  (swSTLQuality was written as 0, out of its 1..3 range, after a failed read).
- **Assembly returns (L11-7, M39, Select4 in `accoppia`, N18, N38).** The component
  mirror reads its rebuild error and removes a mirror born in error; `bollatura`
  refuses before AutoBalloon5 when the view cannot be selected and reports
  `balloons_returned` / `balloons_count_note`; `accoppia` refuses when a face cannot
  be selected, without calling AddMate5; `verifica_interferenze` reads its three
  settings back (refusing on a difference), leaves suppressed components out of the
  count, and counts POSSIBLE interferences apart (`certain_interferences`,
  `possible_interferences`). Open: when SOLIDWORKS 2021 flags an interference as
  possible was never measured.
- **Weldment returns (N15, N41, N30, N19, N22, N69).** `distinta_taglio` reads
  SetAutomaticCutList, SetAutomaticUpdate and UpdateCutList, checks every cut-list
  item against the bodies of its folder (QUANTITY, equal volumes, total bodies) as
  warnings, and refuses a cut list that is not automatic. A refusal removes only
  the weldment feature this step added (never features picked by new name, which
  could delete the renamed solid-bodies folder) and says whether IsWeldment reads
  false again. `taglia_membri` refuses an end condition that is not one of the
  four, before touching the part (case
  `saldature/the_trim_refuses_an_unknown_end_condition`). The gusset null-path
  message no longer blames geometry; three comments now say library 0.139.0.
- **Drawing returns (L13-6, L16-1, M38, M40, M41, N47, N80).** `esporta_pdf` reads
  the SetSheets answer and reports it (`set_sheets_answer`): the first full bench on 0.145.0 measured false on PDFs that came out complete, so the page count of the written file decides, not the answer; the page counter
  (`CountPdfPages`) answers -1, not 0, when it cannot count, and
  `PdfPageCountRefusal` refuses both an uncounted and a wrong count. The weld symbol
  is refused with fewer than two texts; the gtol frame reads its Ø back
  (`diameter_read`, case `tavola/the_position_frame_reads_its_diameter_back`); every
  removal (view, annotation, sheet sketch segment) is said done only after a
  recount. The section and the detail read the view the call returned
  (`view_read_from`) and remove the line or circle they drew on a refusal. A broken
  walk over the drawing's dimensions answers null, not a partial count; a dimension
  born without its object is kept and listed, and no longer triggers a second
  diameter dimension on a hole. Residual: the same `q == null || NonCresciuta`
  pattern in `OperazioniTondoTavola.cs`.
- **Machining and anchor returns (L5-5, L5-7, L12-9, L17-8, M24, M35, M36, M37,
  M42, M43, N16, N21, N49, N53, N56, C7-3, C9-6).** AccessSelections answers are
  read; the tolerance decimals are set and read back on every displayed dimension
  (`precision_status` reports 1, 2 and 4); ClosestDistance -1 is refused; Get6
  NotPresent is skipped; `scrivi_proprieta` compares the written text with ValOut
  (`resolved_value`); the picture placement is read back to 0.01 mm; the edge
  fillet and chamfer refuse an unchanged volume (`material_change_mm3`); «put back»
  and «nothing touched» are said only after a reread in `cambia_estrusione`,
  `tolleranza_quota`, `riordina_lavorazione` and `sopprimi_istanza`; the mass sums
  every body or fails; a defect check that cannot be read is not «sound»; the flat
  pattern suppression answers are reported (`flat_pattern_problem`); direct
  sketch-point casts are gone; all seven constrained states are named; a removed
  feature is no longer said to be «still in the part».
- **Catalogue text (S15, S16, L1-3, N4, N12, N44, N62, N64, N81 and the texts of
  the items above).** `accoppia` directions are those of the part's own file;
  `sostituisci_componente` and `inserisci_componente` no longer promise a
  configuration they do not honour; the extrusions say they merge with bodies they
  touch; `tappo_estremita` says that its gap is the side gap, not a depth, and that
  its chamfer is ignored today; `loft_fra_due_cerchi` takes a positive distance only; `quota_in_tavola`
  names its overlap fields and gives one hole callout per diameter AND depth;
  `raccordo_faccia` describes its hand calculation as the check it is;
  `ripeti_riempimento` asks for an axis that lies along the face.
- **Comments that said the wrong thing, and NOT-REAL findings closed with the probe
  numbers (L2-1, L4-1, L7-2, L10-7, L14-1, L15-2, L17-5, C3-1, C3-4, C4-4, C6-2,
  L9-6, L11-4, L12-3, C3-6, M16, M33, L7-5, N10, N60, N65, N71, N72, N74).** Each
  comment now cites the measure (for example N10: every Flip=false variant ends at
  s=+30 from +90 and from -110; L9-6: ConnectedSegmentsOption 1 and 2 identical on
  the round tube in every scene). L7-5 and M16 also get a bench case each
  (`ripeti/the_linear_pattern_follows_a_reference_axis`,
  `ripeti/the_circular_pattern_turns_around_a_straight_edge`).
- **Two historical lines of this file corrected in place, marked:** the gusset entry
  of 0.115.0 said 0.138.0 (N69), and the UseGlobal note of 0.53.0 claimed a measure
  that could not tell the branches apart (L8-3).
- **Adversarial review of the branch.**
  - `modifica_quota` (L5-6, left out of the first pass): a value SetSystemValue3
    refuses gets the remedy of its own swSetValueReturnStatus_e
    (`SetValueRefusalCause`), instead of «usually an equation or a relation» for
    every status.
  - The component mirror a refusal removes is said removed only after the tree is
    read again and no longer holds it (`MirrorRemovalNote`); it said «the assembly is
    as it was» on the answer of DeleteSelection2 alone, and L11-7 had added a caller.
  - `vista_sviluppo` refuses before placing the view when the flat pattern of the
    part does not toggle (M24); the overload that answered only NaN is gone.
  - `sezione_in_tavola` and `dettaglio_in_tavola` never delete a view that was on
    the sheet before the call: with no view returned, the fallback «last view of
    the chain» could have been a view of the user (M41).
  - `inserisci_componente`: the catalogue quoted the guide («in its last saved
    configuration») with words that are not on any page of the extracted guide; the
    quote is gone, and the comment next to AddComponent5 no longer says the named
    configuration is taken.
  - Comments tightened to what the probes measured: N10 (aligned from above only),
    L12-3 (measured after ForceRebuild3, and verifica_geometria_corpo does not
    rebuild), the linear pattern marks (L7-5, grids G0-G3), L17-1 in the state.
- **Contracts.** New groups `Zc2189WeldAssemblyContract` (29 checks),
  `Zc2189DrawingContract` (13), `Zc2189MachiningContract` (27): 615 checks in all.
  `ProveIRifiuti` learned the English imperatives of the rewritten refusals: floor
  217 -> 230 (with the same detector HEAD counts 223, so +7 is this branch). The
  frozen obsolete and undecided counts only went down: the tolerance limits are read
  in one helper instead of three call sites, and nullable `.Value` became
  `GetValueOrDefault()` on the new lines.

---

## 0.120.0 — 29 September 2026 · library 0.144.0

**What the online research of 29 September 2026 decided (TODO entry Z-C21.13, RAG
note 82zz-decies), built on library 0.143.0 with the probes of the same afternoon.
** **Full bench on library 0.144.0, 30/9/2026: 1941 tests, 0 failed**, after two
fixes the first runs asked for (a path not on disk is not a name; never CloseDoc(""),
both below). The 17-tooth gear with CreateSpline3 and AddToDB off: 8808.59 as before.

- **C5-7, STEP AP242 through SOLIDWORKS MBD.** The library no longer writes
  `swStepAP = 242`: the probe `step_ap242` measured that SOLIDWORKS 2021 accepts it and
  writes an AP203 file. AP242 now goes through `IModelDocExtension.PublishSTEP242File`
  (help.solidworks.com/2017/english/api/sldworksapi/...imodeldocextension~publishstep242file.html;
  help.solidworks.com/2017/English/WhatsNew/c_step_242_pmi_export.htm) with our document
  brought to the front (COM identity, not the path) and the previous front document put
  back. The answer is read as `swStep242Error_e`: 4 (MBDLicenseNotAvailable, what the
  bench machine answers) is a refusal naming the SOLIDWORKS MBD licence and AP214/AP203;
  1, 3 and unlisted values are refusals naming the code. A `.step` path is refused before
  the call (the probe got InvalidPath), the default path ends in `.stp`, and a file
  already at the path is moved aside and put back on a refusal (on 0.143.0 it was
  replaced by an AP203 file). New measure `written_with`. Cases:
  `esporta/lim_step_ap242_needs_the_mbd_licence` (machine-dependent: on a machine with
  MBD it must become a success case), `esporta/lim_step_ap242_wants_a_stp_path`; contract
  `Zc2113ExportUnitsContract` (the answers the bench cannot produce, the path rule, no
  bare 242 near swStepAP).
- **L17-7, document length units.** `MmPerUnitaDelDocumento` goes through the pure
  `MmPerLengthUnit`, with the factors of the corrected probe `feet_inches`: angstrom
  1E-07, nanometre 1E-06, microinch 2.54E-05 (swLengthUnit_e and the Custom-first order
  from help.solidworks.com/2021/english/api/swconst/dp_units.htm). Feet-and-inches
  answers NaN (its two readings disagree, 25.53 against 25.40). **Micron also answers NaN
  now, instead of 0.001:** the same probe read it with the nanometre factor from both
  readings, which contradicts a micrometre; until a second probe explains it the
  comparison is skipped and said so. Contract only (no bench step sets units).
- **L1-1, splines: `ISketchManager.CreateSpline` -> `CreateSpline3(triples, null, null,
  true)`.** Natural ends true is S5 of the probe `spline3` (8808.586 mm3, the same as the
  bridge; false gives 8806.568); guide:
  help.solidworks.com/2023/English/api/sldworksapi/...ISketchManager~CreateSpline3.html,
  help.solidworks.com/2022/english/api/sldworksapiprogguide/Overview/Splines.htm. AddToDB
  off and DisplayWhenAdded on around the call, restored in a finally; the input stays
  triples (pairs break the call, S4/S6/S8); a spline counts in `segmenti` only when a
  segment comes back. Frozen key removed from `ObsoleteCallsContract`. Case
  `estrusioni/la_ruota_da_diciassette_denti_si_fa` gains `misure.segmenti = 170` and must
  keep 8808.59 +-0.5. Untested: natural ends true together with AddToDB off (the probe ran
  S5 with AddToDB on); the bench must confirm it, also on the c4r gears for time.
  Contract `Zc2113SplineCentreMarksContract`.
- **Centre marks on views left bare by SOLIDWORKS (`viste_standard`).** The hole walk of
  `croci_di_centro` is now a shared method, `MarkHolesOfView`, unchanged in behaviour;
  `viste_standard` counts marks view by view after the rebuild and walks only views with
  zero marks that this call made (review: a view already on the sheet may be bare on
  purpose, and is never marked; if the names of the views before cannot be read, no view
  is marked). New answer keys: `centre_marks_added_by_bridge` (always),
  `holes_in_views_without_centre_marks`, `auto_centre_marks_option` (the document toggle
  swDetailingAutoInsertCenterMarksForHoles, null when unreadable),
  `holes_left_without_centre_mark`, `centre_marks_note`; `centre_marks_after_rebuild` is
  null when a view cannot be counted. Sources: the silent-suppression mechanisms of the
  second research round (help.solidworks.com/2018/English/solidworks/sldworks/t_Setting_Options_for_Center_Marks.htm,
  help.solidworks.com/2020/English/api/sldworksapi/...IView~AutoInsertCenterMarks2.html).
  No bench case can build a bare view (it happened only in long runs); contract
  `Zc2113SplineCentreMarksContract`. Open: AddCenterMark put no mark back on 26/9 once the
  automatic ones were deleted, so the remedy may add nothing, and the answer then says so;
  the 17/9 verdict on AutoInsertCenterMarks2 was counted with GetCenterMarkCount, which
  does not see automatic marks, and deserves a new probe.
- **N11 / M4 / S18 / N8 / L13-3, documents closed and activated by path or reference,
  never by title.** New public `Riscontro.Disegno.DocumentByPath`: a saved document is
  closed by full path, then by file name with extension (the CloseDoc probe, A-G1 and
  A-G3); a never-saved one with `CloseDoc("")` when it is the active document (not
  measured by the probe: if the read back finds it still open, its title follows, only
  when no other open document answers to it) (the
  ActivateDoc3/Close pages:
  help.solidworks.com/2023/English/api/sldworksapi/...ISldWorks~ActivateDoc3.html,
  help.solidworks.com/2019/english/api/sldworksapi/...IModelDoc2~Close.html), otherwise by
  a title no other open document answers to, or refused (probe A-S: the stem collides).
  Every close is read back (closed, still loaded hidden, still open, refused, failed) and
  hidden documents that went with it are listed. `documento chiudi` also refuses when
  hidden documents with unsaved changes would go with it (probe H4) and answers
  `closed_by`, `still_loaded_hidden`, `still_loaded_reason`, `also_closed`,
  `also_closed_unsaved`. Activation is by file name with extension, then path, with
  swDontRebuildActiveDoc: this removes the swUserDecision dialog of
  `SezioneProfilo.TelaioDiProva` (N8) and of the STEP/IGES reopen (L13-3). The reopened
  STEP/IGES is no longer closed by its title, which with the default path is the part's
  own stem (M4). The weldment profile library is closed by path; if it stays loaded,
  hidden and dirty, `profile_library_warning` says so (probe H1d: the save flag lies).
  No bench case (equal titles need Explorer's hide-extensions, a station setting that is
  off here); contract `DocumentByPathContract`, 13 checks. Still open: which document
  CloseDoc/ActivateDoc3 picks when two titles are really equal, CloseDoc by path on a
  drawing, the bench's own teardown by title, and C4-3 (rebuild on activation).
  **A path counts as a name only if the file is on disk** (first full bench on 0.144.0,
  group `iges_solidi`): a part reopened from an IGES with LoadFile4 was never saved,
  yet GetPathName answered `...\lavoro\gp3_solidi.SLDPRT`, a file that does not exist,
  and CloseDoc given that path closed the reopened part AND the original part of the
  export («Parte2115», never saved), discarding its changes. The collateral check saw
  it and said so, after the fact. `DocumentByPath.PathOnDisk` now gives "" for such a
  path, and the document is closed by its title under the collision rule, as before
  0.144.0.
- **Contracts and counts.** Three new groups, 23 checks (6 + 4 + 13); `Prove/LEGGIMI.md`
  now says 546 controls. The 526 it said before was 3 more than main runs today: main
  (25cc619), built and run in a throw-away worktree on 29/9/2026, gives 523 with the same
  count in every group, so the three groups add exactly 23. `Banco/LEGGIMI.md`: `casi/esporta/` has 15 cases. The served
  catalogue `Strumenti/catalogo.json` is regenerated from `Operazioni.cs` (esporta_step,
  esporta_iges, viste_standard, croci_di_centro, membro_strutturale, telaio_rettangolare);
  the `documento` tool text in `Strumenti/Attrezzi.cs` describes the new close and
  activation.

## 0.119.0 — 29 September 2026 · library 0.143.0

**The obsolete API calls that the Z-C21.3 probes of 28 September 2026 decided,
migrated one per commit.** Each migration follows exactly the call the probe
measured, cites its guide page and the probe numbers in the code, and takes its
keys out of the exact ratchet of `Prove/ObsoleteCallsContract.cs`. Every
bullet names the existing cases that must give the same answer (same volume,
same box). **Full bench on library 0.143.0, 29/9/2026: 1934 tests, 0 failed**, after the
bench families `tavola` and `quote` had shown that migration 9 (S1) breaks the
drawing's own dimensions, and it was withdrawn (below). The same run is the green
bench of 0.142.0 (entry 0.118.0).

- **L8-1 / L2-4, the offset plane: `CreatePlaneAtOffset3` -> `InsertRefPlane`.**
  `crea_piano`, the second plane of `loft_fra_due_cerchi` and the plane of
  `nervatura` now select the base plane by name with mark 0 (the guide's «0 =
  First reference entity»; `SelectionMarks.OffsetPlaneReference` goes from 1 to 0)
  and call `IFeatureManager.InsertRefPlane(8 | 256 when d < 0, |d|, 0, 0, 0, 0)`
  through `ApiHelpers.SelectOffsetPlaneBase` / `InsertOffsetPlane`. Probe: G1 top
  +100 at (0, 100, 0), G4 top -100 at (0, -100, 0), G6 front -250 at (0, 0, -250);
  H1 with mark 1 made no plane (the measure of 10 September that ruled the method
  out was the mark); G5 with a SIGNED distance put the plane at (0, 0, 0), hence
  OptionFlip and the absolute value; the loft L2 gave 43980.736 mm3, the same as on
  the old plane. Frozen keys removed: 3 (`OperazioniSw`, `OperazioniPercorso`,
  `OperazioniNervatura`). No answer changes. Cases that must stay identical:
  `piano/piano_superiore_piu_100`, `piano/piano_superiore_meno_100`,
  `piano/piano_frontale_meno_250`, `lavorazioni/il_loft_non_e_un_cono`,
  `lavorazioni/nervatura_nell_angolo_della_l`,
  `lavorazioni/lim_nervatura_fuori_dal_suo_piano`.
- **L10-1, the edge flange: `InsertSheetMetalEdgeFlange2` -> `InsertSketchForEdgeFlange`
  + `CreateDefinition(swFmEdgeFlange)`.** `flangia_spigolo` follows the guide's
  recipe (case E1 of `probe_edge_flange_definition`): the sketch SOLIDWORKS makes for
  the edge, the edge converted into it (`SketchUseEdge3`), three lines L - T up in
  its +Y, then `AddEdges`, OffsetDistance L at the outer virtual sharp, material
  inside, default radius, relief and allowance, `CreateFeature` through
  `ApiHelpers.CreateFeatureFromDefinition`. Probe on the L of the bench (T 2, R 2,
  depth 40, L 25): E1 born without error, OffsetDistance 25, +1736.995 mm3, flat
  80.712 -> 102.425, the same as the obsolete call (E0); OffsetDistance now governs
  the length (E3, 35: +2536.997 mm3), the profile height does not matter (E2), a
  pre-selected edge changes nothing (E4). New: before drawing, the far side of the
  profile is read back in model coordinates and must go along the broad face's
  outward normal, the side the catalogue promises; a flipped flange adds the same
  volume (E5) and no later check would see it, so the other side is refused. New
  refusals are in English. Frozen key removed: 1. No answer changes. Cases that must
  stay identical: `lamiera/la_flangia_a_spigolo_chiude_il_lato` (+1736.99 mm3,
  length 25, flat 102.424), `lamiera/lim_flangia_spigolo_non_di_bordo` and
  `lamiera/lim_flangia_spigolo_senza_dritto` (refused before the recipe).
- **L10-2, the flat-pattern DXF: `ExportFlatPatternView` -> `ExportToDWG2`.**
  `sviluppo_dxf` calls `ExportToDWG2(dxf, part path, swExportToDWG_ExportSheetMetal,
  true, null, false, false, 1, null)`, case X1 of `probe_export_dwg2`: on the saved
  bench L it returned true with 4 LINE entities and the box 40 x 80.712 mm, the same
  as the obsolete call (X0), the part unchanged. ModelName names the ACTIVE document,
  so our document is first brought to the front with `ActivateDoc3` (title, no
  rebuild, as the bridge's `PortaDavanti`), checked by path, and the document that
  was in front goes back there afterwards; a failed activation is a refusal. The
  refusal of a part never saved stays (the unsaved case of the probe was not run).
  Frozen key removed: 1. The only field that can differ is `byte`, the file size
  (22946 -> 23005 bytes in the probe, a different header), which no case checks.
  Cases that must stay identical: `lamiera/la_L_si_sviluppa` (flat 80.712, the DXF
  long side checked against it) and `lamiera/lim_sviluppo_senza_file`.
- **L15-1, the hole table: `IView.InsertHoleTable2` -> `InsertHoleTable3`.**
  `tabella_fori_in_tavola` passes the same six arguments plus TagOrder XY (1),
  TagType alphanumeric (1) and no manual tags, case H1 of `probe_hole_table3`: on the
  bench plate the same six rows as the obsolete call (H0), A1 (15,15), A2 (15,65),
  A3 (105,15), A4 (105,65), B1 (60,40), each on a hole. TagType is honoured (H2,
  numeric: 1 to 5); the marks 1 (origin vertex) and 2 (holes) are still needed (H3,
  swapped: the header row only), so the table-born-empty refusal stays.
  `SelectionMarks.HoleTableDatumOrigin` / `HoleTableHoles` now quote
  IView~InsertHoleTable3.html (same values). Frozen key removed: 1. No answer
  changes. Cases that must stay identical: `tavola/la_tabella_fori_dice_dove_sono`
  (5 holes, 6 rows, positions verified) and
  `tavola/lim_tabella_fori_sulla_vista_di_fianco`.
- **L8-4, the picture in a sketch: `InsertSketchPicture` -> `InsertSketchPicture2(file,
  false)`.** `immagine_nello_schizzo` calls the replacement with HighestResolution
  false, which is what a part gets anyway (the page's Remarks). `probe_sketch_picture2`:
  a drop-in on the 400 x 200 PNG (P1 = P0: pixel map 400 x 200, 120 x 60 mm at
  (10, 20), first row 119.7 mm), and on the 4096 x 2048 PNG the same 2048 x 1024
  pixel map as the obsolete call (true is ignored in a part too, P2 = P1), so the
  file-pixel readback of M11 added in library 0.142.0 stays as it is. The refusal on
  a null return names the new method; its text is otherwise unchanged. Frozen key
  removed: 1. No answer changes. Cases that must stay identical:
  `piano/la_foto_sta_in_scala` (0.3 mm per pixel, 119.7 mm check) and
  `piano/a_picture_over_2048_px_keeps_the_scale_of_its_file` (0.1 mm per file pixel,
  SOLIDWORKS pixels 2048 x 1024).
- **L6-3 / S3 / M44 (features), suppressed or not: `IFeature.IsSuppressed` ->
  `IsSuppressed2(swThisConfiguration, null)[0]`.** One helper,
  `ApiHelpers.IsSuppressedHere`, now answers for `sopprimi_lavorazione` (already
  suppressed or active?), the «soppressa» flag of the document state
  (`StatoDocumento`) and the rib's retreat (`NervaturaRitira`, a sketch that can only
  be suppressed). `probe_is_suppressed2`, four configurations and a cut suppressed in
  one: `(swThisConfiguration, null)` is a Boolean[1] equal to `IsSuppressed()` with
  either configuration active ({False}, then {True}); `(swAllConfiguration, null)`
  gives one Boolean per configuration in `GetConfigurationNames` order (not used). An
  answer of another shape throws, and the call sites keep their catch. Frozen keys
  removed: 2 (`OperazioniSw`, `StatoDocumento`); the undecided entry
  `OperazioniNervatura.cs|IsSuppressed` goes too. The component reads
  (`IComponent2.IsSuppressed`) are not obsolete and stay. No answer changes. Cases
  that must stay identical: `togliere/sopprimi_il_terzo_foro`,
  `togliere/lim_sopprimere_due_volte`,
  `lavorazioni/lim_nervatura_sulla_piastra_senza_pareti`.
- **S2 / L17-3 (part readings), mass properties: `CreateMassProperty` ->
  `CreateMassProperty2`.** New `ApiHelpers.NewPartMassProperty`: the selection
  cleared, `CreateMassProperty2`, system units, `IncludeHiddenBodiesOrComponents`
  true, the bodies as a typed `Body2[]` in `SelectedItems`, `Recalculate` always, the
  bodies read back from `SelectedItems`. Used by `proprieta_di_massa` on a part, by
  the per-part masses that check the assembly mass (`MassaDelPezzo`), and by the
  bench's volume reader (`Banco/PortaDelCliente.cs`). `probe_mass_property2` on a
  three-body AISI 304 part (16000 + 4000 + 1000 mm3, the last hidden): nothing
  selected 21000 (N1 = M0), {A} without Recalculate still 21000 (N4), with it 16000
  (N5 = M1), the hidden {C} 1000 (N7 = M2), {A, B, C} 21000 (N8 = M3), density 8000
  throughout; the old order trap (density 1 after Volume) is absent (O1). Kept on the
  obsolete call, with the reason in `WhyFrozen`: the ASSEMBLY mass (components not
  probed) and the document STATE's mass field, because the new call measures what is
  selected and the state reads the designer's selection after the mass: clearing it
  there would erase the state's selection field, while left in place a pre-selected
  body is measured alone (N2/N3: 16000 mm3 instead of 21000); whether `SelectedItems`
  replaces a pre-selection was not probed. Frozen key removed: 1 (`Banco/PortaDelCliente.cs`). No
  answer changes. Cases that must stay identical: `misura/massa_di_un_cubo_di_acciaio`
  (200000 mm3, 1.6 kg, centre [0, 0, 10]), `misura/massa_senza_materiale_non_si_sa`,
  `proprieta/the_material_reaches_every_configuration` (7800, 1.56 kg),
  `assiemi/l_assieme_si_pesa_si_misura_si_manda` (1.0675 kg, sum of the parts
  1.0675), `assiemi/due_basi_uguali_uno_step`, and the bench's client-door group.
- **L9-3, the cosmetic weld bead: `InsertCosmeticWeldBead` -> `InsertCosmeticWeldBead2`
  with the marks 4 / 8.** `cordone_saldatura` selects the from face with
  `SelectionMarks.WeldBeadFrom` (4) and the to face with `WeldBeadTo` (8) on a
  SelectData, keeps the selection, and calls `InsertCosmeticWeldBead2(weld geometry,
  Face2[]{from}, Face2[]{to}, size)`: case W1 of the Z-C21.3 weld bead probe,
  decision A. On the trimmed T of the bench case W1 (and W2, the guide's example
  literally) made one bead, BeadSize 4 mm, 68 mm long, the same as the obsolete call
  (W0); without the marks (W4), with the selection cleared (W3) or with no selection
  (W5, the call of 25 September) there is no bead: the "null in every combination" of
  25 September was the missing marks. The return is an Object[1], on which `as
  IFeature` is null, so the new bead is found in the tree as the probe counted it:
  the beads by name before and after, sub-features included (it lives in the weld
  folder); two new beads are a refusal. This was an OBSOLETE-MEASURED entry, not a
  frozen one: the Measured entry goes. `SelectionMarks.WeldBeadFrom/To` lose their
  DIVERGES note. No answer changes. Case that must stay identical:
  `saldature/il_cordone_va_fra_facce_che_si_toccano` (68 mm, side read back 4,
  volume 1264161.94 unchanged, the two refusals before it).
- **S1, reading a dimension through `GetSystemValue3`: migrated, benched, WITHDRAWN.**
  The migration moved `ApiHelpers.ReadDimension` to `GetSystemValue3(swThisConfiguration,
  null)[0]` and made every drawing caller read in the configuration of its view
  (`GetSystemValue3(swSpecifyConfiguration, {view configuration})[0]`), as
  `probe_dimension_value3` measured for MODEL dimensions shown in a drawing. The
  dimensions the DRAWING owns (AddDimension, AddChamferDim, between silhouettes) had not
  been probed, and the bench families `tavola` and `quote` on 0.143.0 (29/9/2026) gave 12
  failures and 8 bench errors: "no dimension was born" on the turned parts, `quote_mm`
  without 120 or 10 in every `quota_in_tavola` case, no outside diameters read back.
  The dimensions were made and then discarded by the readback. The commit is reverted:
  the readings are back on `SystemValue` / `GetSystemValue2`, the two keys are frozen
  again, and `WhyFrozen` carries this measure. What stays true from the probe: in a
  drawing `GetSystemValue3(This, null)` reads the model's active configuration, not the
  view's, so a future migration must be probed on drawing-owned dimensions first.

---

## 0.118.0 — 29 September 2026 · library 0.142.0

**Sixteen findings of the Z-C21 audit fixed with the remedy the Z-C21.3 probes
measured on 28 September 2026.** Every finding below was measured REAL by a probe
first; each fix follows the call that the probe showed working, with its numbers,
and cites its page of the API guide in the code. **The bench has not run on this
branch yet**: the red-before and green-after of the new cases come from the probe
numbers and from reasoning, not from a bench run. **Full bench on library 0.142.0,
29/9/2026: 1932 tests, 1 failed**, and not in these fixes: `le_croci_di_centro_sui_fori`
step 7 counted 0 centre marks instead of 3 right after `viste_standard`. It was
green in the three full runs before and green again alone on the same library,
and neither `viste_standard` nor the counter had changed. `viste_standard` now
rebuilds the drawing before answering and reports `centre_marks_after_rebuild`.
The green full bench of this entry is the one on 0.143.0 (entry 0.119.0), which
contains it.

Drawing (`OperazioniTavola.cs`, `StatoTavola.cs`):

- **M2, duplicate dimensions from the model.** Both `InsertModelAnnotations3` calls
  of `quote_dal_modello` passed DuplicateDims false, which the guide defines as
  "allow" duplicates, under a comment that said the opposite. Measured on the
  bench plate (120 x 80 x 10, one hole, three views): false gave 7 dimensions with
  3 of them twice, true gave 4 with none twice. Now true. Bench case
  `quote/le_quote_arrivano_dal_modello.json` updated: 4 dimensions instead of 7.
  The obsolete `InsertModelAnnotations2` fallback stays false on purpose: its own
  page gives the flag the opposite meaning, and it was not measured.
- **N9, unmarked dimensions came in through the fallbacks.** Without
  «tutte_le_quote» the first masks asked only MARKED dimensions, but the v2
  fallback passed AllTypes true, `InsertModelDimensions(0)` took every dimension
  of the view, and the view-by-view fallback asked NOT-MARKED too. Measured on a
  part with nothing marked: 7 unmarked dimensions came in and the operation said
  «done»; the view-by-view fallback with MARKED only gave 0. Now every step stops
  at the MARKED masks unless «tutte_le_quote» is true, and a part that has
  dimensions but none marked is refused with the way out. No bench case can build
  a part with nothing marked: contract group `Zc212DrawingContract` (6 checks).
- **N3, a turned diameter counted as an orphan.** A dimension that only
  silhouette edges hold (attached type 46) cannot be checked with IsSame: the
  probe got 0 hits at every step, and silhouette against silhouette answers Same
  for every pair. Such a dimension is no longer counted in the orphans but in the
  new state fields `dimensions_not_verifiable` (per view) and
  `dimensions_not_verifiable_total`. Bench case
  `tavola/the_turned_diameter_is_not_an_orphan.json` (a plain cylinder: 0 orphans,
  1 not verifiable).

Sketch, measure and picture (`OperazioniSw.cs`, `OperazioniMisura.cs`,
`OperazioniRicalco.cs`):

- **M5, the profile sketch was reported locked when it was not.** `AddRelation`
  FIXED on a closed sketch with an object[] returned null and added 0 relations,
  and the bridge answered locked. Measured remedy: EditSketch, the segments as a
  typed SketchSegment[], leave the sketch, rebuild, count the FIXED relations
  again (0 to 6 on the L profile, volume unchanged). The sketch is reported locked
  only when every segment has its relation; the answer carries `fixed_relations`
  and, otherwise, `sketch_lock_note`. Bench case
  `estrusioni/the_l_profile_sketch_is_locked_for_real.json`. The typed array was
  measured on straight lines only: on splines, circles or the revolve centreline a
  shortfall is now reported, not hidden.
- **M29, the distance between arcs was always centre to centre.** IMeasure.ArcOption
  was never set: between two holes D10 and D20 at 50 mm the probe read 50 (centre),
  35 (minimum) and 65 (maximum). `misura_fra_entita` takes `arc_measure` (centre,
  the default and the old answer; minimum; maximum), sets it and ProjectionOption
  before Calculate, reads it back and publishes it; another value is refused.
  Bench case `misura/the_wall_between_two_holes_is_measured_as_asked.json`.
- **M11, a picture over 2048 px got the wrong scale.** SOLIDWORKS holds a
  4096 x 2048 picture as 2048 x 1024 in a part, with or without
  HighestResolution, so the scale per pixel was twice the true one and the
  bridge's own check passed. The pixel size is now read from the file header; the
  map SOLIDWORKS holds is published beside it (`solidworks_pixels`,
  `compression`), and a map larger than the file, of another shape, or a header
  that cannot be read at 2048 px or more, is refused. Bench case
  `piano/a_picture_over_2048_px_keeps_the_scale_of_its_file.json` with the data
  file `Banco/dati/trace_4096x2048.png`.

Assemblies (`OperazioniSpecchiaComponente.cs`, `OperazioniMisuraAssieme.cs`,
`OperazioniDistintaBase.cs`, `OperazioniDipendenze.cs`):

- **N26, the orientation of a mirrored instance was never set nor checked.** The
  probe measured four different axis sets for the four orientations on the Right
  plane, with the same centre, so the centre check could not tell them apart.
  `specchia_componente` takes `orientation`, sets it with a typed
  swMirrorComponentOrientation2_e[] and checks the axes of the new instance on
  Transform2; a mismatch is refused and the mirror removed. The answer carries
  `orientation` and `new_axes`. Bench case
  `assiemi/the_mirrored_instance_turns_as_asked.json`; its Top and Front steps
  extend the axis rule beyond the Right plane the probe measured, so a red there
  after the fix means the rule is wrong for those planes. Contract checks in
  `AssembliesZc212terContract`.
- **C4-2, the STEP solid count ignored bodies cut in the assembly.** Measured:
  STEP files with 1, 2, 3 and 2 solids where the old count said 1, 1, 2 and 2, so
  a STEP missing the cut instance would have passed. An instance with user bodies
  (GetBodies3 BodiesInfo) now counts all its bodies. No bench operation makes an
  assembly cut: contract checks.
- **N27, the BOM check refused right tables.** The assembly-side count ignored
  ChildComponentDisplayInBOM: a Promote sub-assembly in a top-level table and a
  Hide sub-assembly in a parts-only table were refused. The count now follows it
  (8 probe tables reproduced; the indented table, not measured, keeps the old
  count). No bench operation sets the option: contract checks.
- **L12-8, a missing file could be hidden by a file with the same name.**
  GetDependencies with Searchflag true answered a same-name file found in another
  folder, which exists, and once it had, later calls with false answered it too.
  Now Searchflag false only, and the disk decides what is missing. The bench
  cannot rename a component file: a source check in the contracts.

Weldments and configurations (`OperazioniMateriale.cs`, `OperazioniSaldatura.cs`):

- **M10, the material reached only some configurations.** One
  SetMaterialPropertyName2 with the empty name reached the active configuration and
  its derived ones, not the others (density 1000 instead of 7800 on the plain
  one). `applica_materiale` now sets and reads back every configuration by name,
  refuses naming any that did not take it, and lists them in `configurations`.
  Bench case `proprieta/the_material_reaches_every_configuration.json`.
- **N25, the corner treatment and the empty profile size.** A member group without
  a corner treatment was born with none (the probe read ApplyCornerTreatment
  false): the miter is now the default, applied and declared
  (`corner_treatment_note`), and the trial frame matches the real one. An empty
  profile size built the size the file was saved on (20 x 20 of four): it is now
  accepted only on a one-size file (`configuration_note`) and refused otherwise,
  listing the sizes. Bench cases `saldature/a_member_group_is_mitred_by_default.json`
  and `saldature/an_empty_profile_size_names_the_sizes.json` (tied to the Italian
  install's profile file, the one the probe measured); the one-size acceptance in
  `WeldmentConfigurationZc212terContract` (4 checks). Risk for the first bench
  run: the miter default now also applies to single-bar groups, measured on an L
  only.
- **L9-4, the trim did not extend a short bar.** Options 8 alone gave a phantom
  feature and no change on a bar 20 mm short; 1|2|8 = 11 extends it (+29399.11
  mm3) and cuts a plain crossing exactly as 8 did. Bench case
  `saldature/the_trim_extends_a_short_bar.json`.

Document state (`StatoDocumento.cs`):

- **C7-2 and N14, fillet and chamfer edge counts.** The fillet count came from
  FilletItemsCount, which is -1 on uniform fillets, so it never appeared. The state
  now writes `selected_edges` (GetEdgeCount, the selection) and `faces`
  (IFeature.GetFaceCount), for fillets and chamfers: one selected edge that became
  a whole loop reads 1 and 8. Bench case
  `lavorazioni/the_state_counts_selected_edges_and_faces.json`. The replies of
  `raccordo_spigoli` and `smusso_spigoli` (added in review) keep `spigoli`, the
  edges passed, and carry the same `selected_edges` and `faces` (IFeature.GetFaceCount
  of the new feature); the same bench case asserts them.
- **N29, a vertex chamfer read as nothing.** The chamfer is now read by
  IChamferFeatureData2.Type: a vertex chamfer gives `vertex_distances_mm` (probe
  2/3/4 and 3/3/3, where the state gave {}). The read moved from the obsolete
  IChamferFeatureData, so the obsolete ratchet goes down by two entries. No bench
  operation makes a vertex chamfer: contract group `StateChamferContract` (4 checks).

Not fixed in this lot, as measured: **C3-5** (framing on GetVisibleBox cut 13 of 24
shots against 3 for the current framing; a real fix needs SOLIDWORKS to re-measure
the margin), **M2 on the v2 fallback** (its page says the opposite of v3; needs its
own probe or the v2-to-v3 migration), **N27 for mass and STEP parts** (the probe
found them not real or undecided).

Catalogue: `quote_dal_modello` (the stale «nothing comes in out of process»
paragraphs replaced by the measured behaviour, duplicates, the marked default and
its refusal), `misura_fra_entita` (`arc_measure`), `immagine_nello_schizzo`,
`specchia_componente` (`orientation`), `esporta_step`, `distinta_base_in_tavola`,
`dipendenze`, `applica_materiale`, `membro_strutturale`, `telaio_rettangolare`,
`taglia_membri`, `raccordo_spigoli`, `smusso_spigoli`, and the four sketch-based
operations (the lock flag, `fixed_relations`, `sketch_lock_note`).

Counts: contracts 498 to 523 (four new groups: 4 + 6 + 11 + 4); bench cases +10
(one each in `casi/assiemi`, `casi/estrusioni`, `casi/lavorazioni`, `casi/misura`,
`casi/piano`, `casi/proprieta`, `casi/tavola`, three in `casi/saldature`), one case
changed in `casi/quote`; the floor of refusals with a way out 216
to 217 (a detector match on the helper that removes the mirror, not a better
message); obsolete ratchet two entries lower. Language gate: nothing grew.

## 0.117.0 — 28 September 2026 · library 0.141.0 (unchanged)

**The language gate: the Italian in the repository can only go down (entry
W3.1-bis.1).** Decided on 28 September 2026: the whole repository goes to English
before the first release, names included (D1) and historical entries of this file
included (D2). This entry builds the ratchet that keeps the Italian from growing
while the translation goes on, measures it before trusting it, and was then
reviewed adversarially: the review opened the ratchet without a signature in three
ways and found eight ways past the detector. All of them are closed below, each
with a contract check that was seen red.

- `Controlli/lingua/detect-italian.js` reads every file of `git ls-files` (or of a
  commit, `--rev`, from the object store) and skips binaries **by extension only**,
  from a closed list: never `git grep -I`, never git's own guess. Nine categories,
  never summed. Comments, strings, markdown, JSON values and transition count
  **Italian words per file** and may not grow in any file: Italian appended to a
  line that is already Italian is growth too. Identifiers, JSON keys, protocol
  values and path segments are repository-wide sets of whole names: no new Italian
  name, so an identifier glued from Italian words that exist one by one is blocked,
  and so is a name glued with hyphens (a JSON key with spaces is one whole key). A
  name that was a tracked path segment is not new when it shows up in text after
  the file is renamed. Transition must be 0 at a `v*` tag.
- What the detector reads now, after the review: the holes of interpolated strings
  (a string inside `{...}` or `${...}` is a string, a name is a name); prose after a
  selector or a placeholder (`{name} not found...` is prose, the placeholder a
  name); the words of `echo`/`printf` and the body of a here-document in shell
  scripts; the typographic apostrophe as the accent; workshop words the repository
  does not use yet (a list in `lexicon.json`, and the endings `-atura`, `-trice`,
  `-one`, `-ella`, `-etto` for names). Italian words that English also has (a
  musical term, an abbreviation, a brand: `weak_words` in `lexicon.json`) count
  only in pairs, so an English line with one of them stays English.
- `exceptions.json`: patterns with class, reason, signature and date, from a closed
  list of classes; never a whole file. Refused now also: a `json_path` outside the
  class detector-lexicon or outside one file of `Controlli/lingua/`; a pattern that
  matches an Italian function word on its own, or takes away a third of the Italian
  of one of five sentinel sentences; a pattern without `file` that matches an
  Italian content word; and an exception that, on the tree, suppresses more than 40
  hits in a file, more than the Italian left in it (from 10 up), or more than 300 in
  the repository (`exception_limits` in `lexicon.json`).
- **The CI step judges every commit of the range against its own parent**
  (`.github/passi/language-gate.ps1`): (a) the tree is within its own baseline;
  (b) a change to the gate itself (detector, lexicon, exceptions, the step, the
  workflows, CODEOWNERS) carries the trailer `Language-Gate-Config-Change:`;
  (c) the baseline is not above the parent's; (d) the tree is not above the
  parent's baseline, **measured with the parent's detector, lexicon and
  exceptions**. (c) and (d) pass only with the trailer
  `Language-Baseline-Raise: <reason>` on **that** commit: a signature no longer
  covers the range. Trailers are read by git, so the words in the middle of a body
  do not sign. Renames are followed (`git diff -M`), a merge is judged against the
  union of its parents' baselines, and on a pull request the base is the first
  parent of the merge commit. `.github/workflows/language-gate.yml` runs the step
  again on `pull_request_target`, where the workflow and the script are the target
  branch's: a pull request cannot switch the gate off by editing it.
  `.github/CODEOWNERS` covers `.github/`, `Controlli/lingua/` and
  `Controlli/ganci/`. `prove.yml` runs on `v*` tags too, and a manual run takes a
  `base` input.
- **Pre-push**: the same step on the commits being pushed (read from git, so work
  not yet committed changes nothing), then `--tighten` on each pushed tip. It stops
  when the detector or the step is missing, instead of skipping in silence.
- `baseline.json`, format 2, written on the MERGED tree (main at library 0.141.0
  plus this entry), 674 files read: Italian words comments 147,940, strings
  56,071, markdown 33,983, JSON values 55,845; distinct names identifiers 4,808,
  JSON keys 443, protocol values 2,170, path segments 640; transition 0. **Raise
  reason** (the commit carries `Language-Baseline-Raise:`): the branch baseline
  had been measured on `4d7b84a`, before 0.137.0-0.141.0 landed on main with the
  Italian they were written in; this is the first baseline, not a growth. The
  branch history (eight commits, kept locally as `w31bis-1-cancello-storia`) is
  landed as ONE commit, because the CI step judges every commit against its own
  baseline and the old commits carried the old one.
- **The measure** (`node Controlli/lingua/measure.js --summary`), four draws from
  the tree of `4d7b84a`, labels stored as hashes. The first two (810 lines, and 120
  English-heavy ones) were labelled while the detector was written and helped tune
  it: precision 1.000, recall 0.997, 0 false alarms in 105. **The numbers to quote
  are the two new draws, labelled blind** (from `--next`, before the detector ran on
  them), after the review: 930 lines at random (seed 5150), 820 Italian and 110
  English, **precision 1.000 (balanced 1:1) and recall 0.985**; 200 lines with two
  English function words (seed 4747), **0 false alarms on 166 lines of English
  prose**. Pooled over the blind draws: 0 false alarms in 279 English lines, so
  with 95% confidence the false-alarm rate is below 1.1% and the balanced
  precision above 0.989. Six labels were typing slips (a letter shifted by one
  line), found by reading every label back against the notes, not only the
  disagreements. Of the 12 Italian lines missed, 5 only name an existing file (by
  design: counted once, under path segments) and 7 use words the lexicon lacks, or
  one homograph alone (`--summary` lists them): left as measured.
  808 found lines split as 473 counted (blocked wherever the words are added) and
  335 found through names only (blocked only when the name is new). Out of the
  repository: 41 workshop terms the repository never uses, kept out of every list
  the detector loads, 29 caught in a new name by their ending alone; the Vim user
  manual (9,796 English lines never seen): 3 false alarms.
- Contract `LanguageGateTests`, 50 checks: the nine categories, the glued
  identifier, the Italian folder and the file with a NUL byte as before, plus one
  injection for each hole of the review and English lines with one homograph each
  (must not count); four ways to cover a file with an exception (refused); and the
  CI step on a throw-away repository with commits: a growth signed on a later
  commit, a signature on the wrong commit, a loosened lexicon, an exception for the
  exact sentence, a trailer in the body (all refused), a signed raise, a rename, a
  JSON key with spaces, a merge (all accepted), the step switched off and judged by
  the base's copy, a release tag with transition left (refused). **Every new check
  seen red**: `node Controlli/lingua/see-it-fail.js` switches off each repair in
  turn (20 mutations of the detector, the lexicon and the step) and runs the suite.
- `Controlli/lingua/ci-proof.ps1`, the proof on GitHub, still to run: six
  throw-away branches (an Italian line; the same with the baseline raised and no
  trailer; the same, signed; the Italian line and a raise signed on the next
  commit; a loosened lexicon; a rename), and the step must fail, fail, pass, fail,
  fail, pass.
- `Controlli/domande-senza-risposta.js`: the real NUL byte (offset 3899, line 97, a
  key separator) is now written `'\u0000'`. Same value, and the same output byte
  for byte with the same exit code on the same register, with and without its
  line-by-line option. Git called the file binary and `controlla-pubblicabilita.ps1`
  (`git grep -I`) still skips files like it: a separate entry, not language.
- README: the section that kept the names Italian as protocol is replaced by an
  English note (D1), and so is the matching sentence in
  `Controlli/rinomina-prodotto.js`.

- **`controlla-pubblicabilita.ps1` reads the files git takes for binary**
  (entry Z-C22). The history pass used `git grep -I`, which skips every blob
  git guesses is binary: a text file with one NUL byte is one, and
  `Controlli/domande-senza-risposta.js` (a real NUL at offset 3899) was never
  looked at. It is now `git grep --text`, with the binaries dropped by
  extension (the same `$binari` list as the working-tree pass). Seen red on a
  throw-away clone: a `.js` with a NUL and a private address in a commit
  later deleted — the old script says "ok", the new one names the file.

Bridge version 0.117.0; the library is unchanged (0.141.0): nothing here runs
inside SOLIDWORKS, so the full bench of 0.141.0 still stands.

## 0.116.0 — 28 September 2026 · library 0.141.0

**Silent defects of the API audit, second round (TODO entry `Z-C21.10-esito-2`).**
Every fix below is a case where a part, a file or an answer came out wrong
with `riuscita: true`, decided from the SOLIDWORKS API help (quoted in the
code) or from the library's own logic. The new bench cases are written to go
red on library 0.140.0 and green on this one, except where a bullet says
otherwise. **Full bench on library 0.141.0, 28/9/2026: 1832 tests, 0 failed**
(second run; the first one found the three points listed at the end of this
entry). Whether the new cases were RED on 0.140.0 has not been run yet
(rule Z-C11).

- **`telaio_rettangolare` / `membro_strutturale` close the weldment profile
  library again, and check that it closed** (N31). ISldWorks~CloseDoc.html:
  "void CloseDoc(System.string Name)" returns nothing, and "the name of a
  document that is not open, then this method does nothing"; the library is
  now looked for again by path and, if still open, its IModelDoc2~GetSaveFlag
  ("True if this document needs to be saved") decides. The close runs on every
  exit of `membro_strutturale` (try/finally) and after the trial frame of
  `SezioneProfilo`, which the bench's «sezioni» group calls directly. A library
  left open and modified is reported in `profile_library_warning`. Case:
  `saldature/a_frame_too_small_closes_the_profile_library` (red on 0.140.0
  only on a fresh archive key; whether CloseDoc of the trial part already
  closes the library as a "non-active hidden document" is still to be probed).
- **`telaio_rettangolare` reads «misure» whatever the case, and refuses any
  other word** (N32). «Ingombro» used to fall into the centre-line branch and
  build a 1080 x 880 frame for 1000 x 800. The answer carries `misure`. Case:
  `saldature/frame_reading_ignores_case` (the frame and its refused twin).
- **The overhang archive of `SezioneProfilo` no longer keeps a failure or a
  stale number** (N36). The key now carries the library file's date and size
  (`frame-v2|...`), old-format lines are dropped on load, a failed trial
  measurement is not remembered, and a frame built from a remembered overhang
  that fails the outside-size check drops that entry and says so in the
  rejection. The answer carries `overhang_source` («measured» / «archive»).
  No API call changed. No bench case can build it: contract group «the pure
  rules of the Z-C21.10 fixes the bench cannot reach».
- **The bridge hands every enum value on in the catalogue's spelling, and the
  step goes on under the catalogue's operation name** (S20, `Ponte/Forma.cs`,
  `Strumenti/Attrezzi.cs`). The Forma accepted «Superiore» ignoring case and
  passed it on untouched to a library that compares Ordinal. Nested objects
  are covered; the rewrite walks a copy of the entries. Contract group «the
  Forma rewrites enum values in the catalogue's spelling» (12 checks, 7 red on
  the 0.140.0 Forma).
- **An unknown plane name is refused, never the front plane** (N33,
  `SelezionaPiano`). IFeature~Select2.html: "True if the feature was selected,
  false if not"; the fallback now returns false like `TrovaPianoBase`, and
  every caller turns it into a refusal. Case:
  `estrusioni/unknown_plane_does_not_fall_back_to_front`.
- **`verifica_interferenze` checks the list against the count** (N34).
  IInterferenceDetectionMgr~GetInterferenceCount.html: "Call this method
  before calling IInterferenceDetectionMgr::IGetInterferences to determine the
  size of the array for the interferences". A missing or short array, or an
  item that is not an IInterference, leaves `volume_totale_mm3` out and says
  why in `cosa_non_so`; a count of 0 with interferences in the list is refused.
  New `interferences_read`. Case:
  `assiemi/the_interference_array_is_as_long_as_the_count` (not red on 0.140.0:
  it stops on the missing field; it pins array length = count on SW 2021).
- **`verifica_geometria_corpo` with `corpo_uid` speaks for that body only**
  (N61). IPartDoc~GetBodies2.html: "false gets all of the bodies in the part".
  `bodies_in_part` always counts every body, `verdict_about` says «part» or
  «body Cn», and a sound body of a multibody part no longer gets the sentence
  «si puo' esportare». Case: `verifica/one_body_of_two_is_not_the_part`.
- **`esporta_iges` turns off the three toggles that add curves or turn the
  solid into curves, and counts the solids in the file** (N59).
  FileSaveAsIGESOptions.htm: swIGESExportAsWireframe "Specifies whether to
  convert solid body to a 3D wireframe representation in the IGES file",
  swIGESExportFreeCurves, swIGESExportSketchEntities. They are read, set off
  and restored like the other exchange preferences (only the ones actually
  read are restored). The IGES directory section is counted: `entities`,
  `solid_entities` (type 186), `surface_entities`; a file with neither is
  refused. Case: `esporta/the_iges_carries_the_solid_not_its_wireframe` (not
  red on 0.140.0: it needs a user profile with the toggles on; it pins 1 solid
  and 6 surfaces for the 100 x 100 x 20 block).
- **`foro_guidato` with an anchor checks centred axes and the widest cut**
  (N50). Centred axes were skipped; now half the gap between the two edges
  must hold the radius, and the radius is the counterbore or countersink for
  «lamato» / «svasato» (IWizardHoleFeatureData2~CounterBoreDiameter.html:
  "This property is relevant only for swCounterBored and swCounterBoredDrilled
  holes"). The check runs before the anchor. Cases:
  `lavorazioni/guided_hole_centred_too_narrow`,
  `lavorazioni/guided_hole_counterbore_past_the_edge`.
- **The anchor's movement test no longer passes without testing** (N51): a
  distance axis without its dimension, a dimension that cannot be read, or a
  rebuild state that cannot be read (IFeature~GetErrorCode2.html) is a refusal,
  not a pass.
- **The anchor sketch's relations must be readable** (N52):
  ISketchRelationManager~GetRelationsCount.html ("Number of sketch relations");
  a null manager, a throwing or negative count is a refusal, not "healthy".
- **The slot's four tangencies and equal radii are counted before the centre
  line** (N54): IModelDoc2~SketchAddConstraints.html returns nothing, so the
  count is the only proof, and the coincidences no longer hide two missing
  tangencies. The answer carries `slot_relations`.
- **The oblique slot's angle dimension is checked** (N55).
  IModelDocExtension~AddSpecificDimension.html: "Error Result status as
  defined in swAddSpecificDimension_e"; it must be Success, and the angle read
  back must be the angle between the slot and the reference edge, or its
  supplement. N51, N52, N54, N55 have no bench case (the bench cannot make the
  API fail there); every anchored case is their regression guard, and the pure
  angle rule is in the contract group.
- **`quota_in_tavola` reads «already dimensioned» from every sheet** (N43).
  IDrawingDoc~GetFirstView.html: "Gets the first drawing view on the current
  sheet"; the views now come from IDrawingDoc.GetViews, with the current sheet
  as fallback, said in `already_dimensioned_read_from`. Only views of the same
  model and configuration count (`views_of_other_models_not_counted`). New
  helper file `ApiHelpers.Drawing.cs`. Case:
  `tavola/the_second_sheet_does_not_repeat_the_dimensions`.
- **The «4×» of a hole callout is read back** (N48). IDisplayDimension~SetText.html:
  "NOTE: This method does not support hole callouts."; the prefix is read
  again into `fori[].prefix_read`, and a count that did not stick is a failure.
  Case: `tavola/la_tavola_quota_fori_e_smusso` (new expectation).
- **A dimension hidden by hand does not count as already dimensioned** (C8-2).
  IAnnotation~Visible.html; swAnnotationHidden and swAnnotationHalfHidden are
  skipped and counted in `hidden_dimensions_not_counted`. A hidden layer or a
  suppressed owner feature is not seen ("This property cannot determine..."),
  still open. Case written and parked in
  `Banco/casi-in-attesa/a_dimension_hidden_by_hand_does_not_count.json` until
  the runner field `prima_nascondi_quote` exists.
- **Contracts**: `ProveILimiti` accepts the English stems «refus» / «reject» as
  a declared limit; the undecided-call count of `OperazioniQuoteTavola.cs|Add`
  goes from 26 to 27 (the new `falliti.Add` of N48: `falliti` is a
  `List<string>` parameter, which the scanner does not type); new group
  `Zc2110HelpersContract` (N32, N34, N36, N43, N55, C8-2 by reflection).
- **From the full bench on 0.141.0 (first run):**
  - **`nervatura` says what it could not remove.** When no rib is born in
    either direction (a plate with no walls), the rib sketch cannot be
    deleted afterwards: measured by `Controlli/NervaturaRitira` on SOLIDWORKS
    29.5.1. DeleteSelection2 answers false with every option, EditDelete and
    three EditUndo2 leave it, a control sketch deletes normally, and
    EditSuppress2 works. The sketch is now suppressed and named in the refusal
    («resta nell'albero, SOPPRESSO»), and the volume check still says «com'era».
    Before, the refusal declared a restore that had failed.
  - **Two bench cases corrected, no library change.**
    `guided_hole_counterbore_past_the_edge` step 3 selects the plate face with
    `z~=20`: after the first counterbore there are two front faces.
    `l_albero_a_gradini_quotato_di_fianco` no longer demands that the Ø20 be
    unreachable: with SOLIDWORKS freshly restarted it was exposed and
    dimensioned ([34, 30, 20]), with no change to the shaft code. Both
    outcomes are honest; Ø30 and Ø34 are still required to be dimensioned.

## 0.115.0 — 28 September 2026 · library 0.140.0

**Four operations that never worked, rebuilt from the SOLIDWORKS API help.**
Each one had a history of NULL returns; each time the help page named the
missing piece (an obsolete method, a selection mark, a flag, a degenerate
profile). The recipe of the help was measured first by a probe, then written
into the library together with its bench case, with the expected numbers
computed by hand and compared with the probe. The help page is quoted in the
code. Three operations are new (`raccordo_faccia`, `ripeti_riempimento`,
`nervatura`) and one comes back into the catalogue (`fazzoletto`): the
catalogue goes from 95 to 99 operations, all 99 executable ones. New answer
fields are in English. **Full bench on library 0.140.0, 28/9/2026: 1753 tests,
2 failed**, both in the new rib cases and neither in the recipe: a refusal
without «com'era», and a shell volume written without tolerance, which kept
the rib step behind it from running. Corrected, and seen green on the full
bench on 0.141.0 (entry 0.116.0), together with the locked rib sketch found
there.

- **The face fillet (entry GP7, the half that was missing).** New operation
  `raccordo_faccia` (`faccia_1_uid`, `faccia_2_uid`, `raggio_mm`): a fillet
  that rests on two faces instead of an edge. Four earlier attempts returned
  NULL, and the API help named both causes: `FeatureFillet3` is obsolete for
  face fillets since SOLIDWORKS 2020 (its Remarks), and the selection marks
  are 2 and 4 (`ISimpleFilletFeatureData2` Remarks, "Face fillet/face-face
  chamfer") — not the 1/2 of `swSimpleFilletWhichFaces_e`, which is the
  parameter of `SetFaces`. Measured first by the probe (40 mm cube, two
  adjacent faces, r = 5): the guide's recipe (`CreateDefinition(swFmFillet)`,
  `Initialize(swFaceFillet)`, marks 2/4, circular profile, `DefaultRadius`,
  `CreateFeature`) removes 214.6 mm³, exactly 40·25·(1 − π/4); the old call
  and the new one with marks 1/2 both give NULL. The same face twice, and two
  flat parallel faces (same side, or back to back), are refused before
  calling: no ball touches both. Two walls facing each other are tried only
  with the radius equal to half their distance. Afterwards the new feature
  must be a `Fillet`, the volume must change, bodies and features in error
  must be as before; otherwise the fillet is removed (`DeleteSelection2`) and
  the part is checked to be as it was. Where the hand calculation exists (two
  flat faces at 90 degrees sharing an edge) it is read BEFORE the fillet —
  afterwards the two faces no longer touch — and a volume change that differs
  from it by more than 0.05 mm³ (or 1e-4 of it) removes the fillet and refuses,
  naming `raccordo_spigoli`. New fields: `volume_change_mm3`,
  `material_removed`, `shared_edge_mm`, `expected_change_abs_mm3`,
  `features_in_error`, `bodies`, `feature_type`, and `declared_radius_mm`
  (read back from `ISimpleFilletFeatureData2.DefaultRadius`; the inputs are
  not echoed). Bench: `lavorazioni/raccordo_faccia_fra_due_facce_di_un_cubo`
  (volume and centre of mass, which tells which edge was filleted), and the
  two refusals `lim_raccordo_faccia_sulla_stessa_faccia`,
  `lim_raccordo_faccia_fra_facce_che_si_danno_le_spalle`.
- **`ripeti_riempimento`, the fill pattern (entry W1.1).** The recipe of
  IFillPatternFeatureData (Remarks) and Create_Fill_Pattern_Example_CSharp.htm:
  pre-select the feature (mark 4), a direction edge (mark 1) and the boundary
  face (mark 16384), then `CreateDefinition(swFmFillPattern)` with scalars
  only, then `CreateFeature`. `FeatureFillPattern` is "Obsolete" in the help,
  and so were the fifteen NULLs of GP14. The probe of 28/9/2026 measured 14
  holes on a 100 x 60 x 12 plate (-8444.6 mm³ = 14 x 603.2, exact); without
  the direction edge `CreateFeature` returns NULL although the help calls it
  optional, so the operation refuses before selecting anything. Circular
  layout only (the one measured). It refuses a non-planar boundary and a
  direction that leaves the face, and removes the pattern (`DeleteSelection2`,
  then recounts the tree and re-measures the volume) when it breaks a
  feature, changes the body count or makes openings merge. New answer fields:
  `copies_made` (counted from the faces the pattern owns), `volume_per_copy_mm3`,
  `feature_type` (`APattern`), and `declared_layout`,
  `declared_loop_spacing_mm`, `declared_instances_per_loop`,
  `declared_margin_mm` read back from `IFillPatternFeatureData` (the layout by
  enum name), not echoed from the inputs. The direction edge is re-selected with
  `Select4` and a `SelectData` carrying mark 1, as the probe did; an axis from
  `crea_asse` is accepted but not measured, and the catalogue says so. The
  table pattern stays out: NULL without a coordinate system on mark 16. Bench:
  `ripeti/il_riempimento_fa_quattordici_fori` on the probe's own plate (Top
  plane, direction along the long edge of the boundary face: 14 is an
  algorithmic number and is compared only on the measured geometry) and three
  refusals twinned with it (`lim_riempimento_senza_direzione`,
  `lim_riempimento_contorno_non_piano`,
  `lim_riempimento_direzione_fuori_dalla_faccia`).
- **New operation `nervatura` (rib, entry W1.2)**, family `lavorazioni`.
  IFeatureManager~InsertRib.html and the only example in the help
  (Get_Rib_Feature_Data_Example_CSharp.htm) call `InsertRib` with
  `IsNormToSketch = false`: the rib grows in the sketch plane, from the line to
  the walls. Every historical probe passed `true`, which is why the rib "never
  worked". The material side depends on the shape (L bracket: straight; shelled
  box: flipped — measured by the probe of 28/9/2026), so the operation tries
  straight, then flipped, and keeps the first one in which a new `Rib` feature is
  born and the volume grows; `material_side` and `attempts` say which. Arguments:
  a base plane plus an optional `distanza_mm` (the offset plane is created by the
  operation), the two line ends in model coordinates, `spessore_mm` (two-sided).
  Refused, with nothing left in the part (sketch and plane removed with
  `DeleteSelection2`, volume read back): an end off the plane, a line under 1 mm,
  and a rib that is born in neither direction (a plate without walls — it names
  `estrudi_su_faccia`). Before the second attempt the tree must be as it was
  after the sketch: a failed attempt that leaves a feature of another type is
  refused (and removed) instead of built upon. New fields: `added_material_mm3`,
  `material_side`, `attempts`, `plane_created`, `declared_thickness_mm`,
  `declared_flip_side`, `declared_parallel_to_sketch`; the inputs come back as
  `requested_thickness_mm`, `requested_sketch_plane`,
  `requested_plane_offset_mm`, `requested_line_length_mm`, named as inputs. It carries a catalogue example copied from its green case.
  Bench: `lavorazioni/nervatura_nell_angolo_della_l` (+2700 mm³),
  `lavorazioni/nervatura_nella_scatola_svuotata` (+720 mm³, second attempt on
  the same part: the one step the probe did not measure), and two refusals with
  their twins (`lim_nervatura_sulla_piastra_senza_pareti`,
  `lim_nervatura_fuori_dal_suo_piano`). The plate refusal has the same
  arguments as its twin: the difference is the part, and it is declared (see
  the bench entry below) instead of carried by a dummy `distanza_mm: 0`.
- **`fazzoletto` (weldment gusset) is back in the catalogue, measured** (entry
  GP20). Until library 0.139.0 (this line said 0.138.0 until Z-C21.9, finding
  N69: the fix is in 0.140.0) it never created a gusset: it passed
  `InsertGussetFeature3` a polygon profile with both short sides at zero
  (`ProfileD3 = ProfileD4 = 0`) and no selection mark, and SOLIDWORKS returned
  null without a reason. The API help (InsertGussetFeature3 Remarks: "select two
  faces that are the supporting legs of this gusset with a Mark of 1";
  Insert_Gusset_Feature_Example_CSharp.htm) asks for mark 1 on both faces AND a
  valid profile. Measured on the 80x80x5 tube T: triangle 60/60, 5 thick,
  +9000 mm³, bodies 2 -> 3; polygon 60/60 with short sides 15/15, +12937.5 mm³.
  New arguments `profilo` (`triangolare` | `poligonale`), `lato3_mm`,
  `lato4_mm`; a degenerate polygon, faces that are parallel, apart or not
  planar, and a `posizione` outside the list are refused before SOLIDWORKS is
  called. Only `centro` is exercised on the bench; `inizio` and `fine` are
  accepted but not measured. The answer no longer echoes the inputs (audit
  finding C1-1): the added volume is checked against the hand calculation, and
  thickness, profile, location and supporting faces are read back from
  `IGussetFeatureData`, plus the new body and its box (`volume_added_mm3`,
  `volume_expected_mm3`, `volume_check`, `bodies_before`, `bodies_after`,
  `new_body`, `new_body_box_mm`, `thickness_read_mm`, `thickness_type_read`,
  `profile_read`, `location_read`, `supporting_faces_area_mm2`,
  `contact_mm`); if one does not match, the gusset is removed and the refusal
  says so. The new feature must also be of type `Gusset`, the type every
  variant of the probe returned. The schema no longer says which side goes on
  which face: the help does not say it and no probe told them apart (only equal
  sides were measured). Bench: `saldature/il_fazzoletto_sta_nell_angolo`,
  `saldature/il_poligono_del_fazzoletto_non_e_degenere` (each with its
  refusals and twins in the same file).
- **The four probes are in the repository** (`Controlli\RaccordoDiFaccia`,
  `Controlli\Riempimento`, `Controlli\Nervatura`, `Controlli\Fazzoletto`),
  each with the output of 28/9/2026 in its header, built by
  `costruisci-controlli.ps1` and so by every `build.ps1` (step 1c). The code
  and the cases cite those paths and the help pages by name, no longer a
  session scratchpad.
- **Bench: a twin with the same arguments on another part.** A refusal that
  depends on the part and not on an argument declares it in `deve_fallire` as
  `la_differenza_e_il_pezzo` (a sentence saying what changes). The bench and
  the contracts (`Attese.SiSomiglianoNelPezzo`) require the sentence, arguments
  IDENTICAL to the twin's, and different steps before (same part and same
  arguments is still a copy). Four new contracts test both ways.
- **Contract floors.** Refusals that name a way forward (`ProveIRifiuti`):
  146 -> 216 (+15 face fillet, +18 fill pattern, +10 rib, +24 gusset, +3 from
  the review: the fillet off the hand calculation, the rib attempt that leaves
  another feature, the gusset of the wrong type). Contracts declared in
  `Prove/LEGGIMI.md`: 401 -> 405.
  Operations with a copyable example (`ProveGliEsempi`): 29 -> 30 (the rib).
  Two required fields get a written decision (`ProveICampiObbligatori`):
  `ripeti_riempimento.copie_per_giro`, `nervatura.piano`. Bench cases: 13 new
  (`lavorazioni` 27 -> 34, `ripeti` 9 -> 13, `saldature` 5 -> 7).

## 0.114.0 — 28 September 2026 · library 0.139.0

**Single helpers for the recurring API patterns, and a static check that keeps
obsolete calls from coming back (entry Z-C21.6 of the API audit).** Built on
the Z-C21.2 branch. **Seen on the full bench on library 0.140.0, 28/9/2026**,
together with the four operations of 0.115.0: none of the reds of that run is
in the changes marked *behaviour* below.

- **`ApiHelpers`** (Riscontro.Disegno/ApiHelpers.cs, ApiHelpers.Geometry.cs):
  one place per pattern, each helper quoting the help page it is built on.
  - S1 `ReadDimension` / `ReadDimensionInConfiguration`: the 18 readings of a
    dimension value (7 `SystemValue`, 11 `GetSystemValue2("")`) go through
    them, each keeping the call it made before. GetSystemValue3 is not used
    yet: the shape of its return waits for the Z-C21.3 probe.
  - S2 `NewMassProperty`: the 4 `CreateMassProperty` sites (a new object in
    system units, bodies attached, `bodiesAttached` reported). CreateMassProperty2
    works by selection and waits for the same probe.
  - S11 `ModifyAndVerify` (ModifyDefinition always followed by GetErrorCode2)
    and `RebuildError`: 4 ModifyDefinition sites and 9 hand-written
    GetErrorCode2 readers. Each caller keeps its own verdict. `ErrorCodeKnown`
    (and `RebuildError(feature, out known)`) tells "no error" from "could not
    read the code".
  - S12 `EdgeParameters`: GetCurve always before GetCurveParams3 (15 sites,
    6 of which skipped it). *Behaviour*: GetCurve is now called where it was
    not, as IEdge~GetCurveParams3.html requires. The one GetCurveParams2 site
    (sketch-text outline) goes through `EdgeParameterArray`, still obsolete.
  - S6 `TrueBoundingBox` (six GetExtremePoint) for bodies and, through a
    temporary sheet body (IFace2.CreateSheetBody), for faces. *Behaviour*: the
    flat length measured by the sheet-metal operations (LunghezzaSviluppata), the face boxes of
    `verifica_sovrasporgenze`, `incidi_testo` and the extreme-face choice
    in `Riferimenti` now use the true box instead of GetBox/GetBodyBox, which the
    guide calls approximate and "not for comparison or calculation". A face box
    that cannot be measured is null, not approximate, and each caller says so
    instead of turning it into a verdict (see the review corrections below). The approximate boxes that are
    approximate ON PURPOSE (a ceiling, a permissive pre-check, the pair
    SezioneProfilo / IngombroDeiCorpi, the fallback of the state when GetExtremePoint does not answer) go through `ApproximateBoundingBox`
    with their reason; the unions of boxes go through `UnionOfBoxes`.
  - S10 `CreateFeatureFromDefinition` (selection, CreateDefinition, fill,
    CreateFeature, GetErrorCode2): ready for the Z-C21.7 migrations; today only
    the mirror of components uses it.
  - S17 `NormalIfPlanar`: 10 readings of IFace2.Normal (null on a curved face
    instead of 0,0,0; each site already treated either the same way).
- **`SelectionMarks`** (S9): the 18 non-zero literal marks at selection calls,
  and the 2 passed through the draft's `Seleziona` helper, now name a constant
  that quotes its page (23 constants); the divergences from the guide (L2-1,
  L4-4, L7-5, L8-1, and the missing marks of the gusset M27, of the variable
  fillet and of InsertCosmeticWeldBead2 L9-3) are recorded in the table, not
  changed. `SelezionaAsse` takes `append` explicitly instead of inferring it
  from `mark != 1`.
- **`ErrorCodeDecoder`** (S13): swSetValueReturnStatus_e, swFileSaveError_e and
  swFileSaveWarning_e decoded to their ENUM NAMES, from the interop enums (a
  mask as `swReadOnlySaveError | swFileLockError`). *Behaviour*: four
  messages now add the enum name after the raw code (two SaveAs3 exports, two
  SetSystemValue3 refusals), e.g. «(codice 2: swSetValue_InvalidValue)». No
  sentence in either language: English words inside the Italian messages, or
  new Italian words past the language gate, were both wrong.
- **Refined list of obsolete members**: `Controlli/obsolete-api-list.js`
  builds `Prove/data/obsolete-api-members.tsv` (3066 members, 21 obsolete only
  for some uses) from sldworksapi.chm and obsoleteapi.chm. Compared with the
  raw list of Z-C21.1 it drops 27 false positives ("the now obsolete X"),
  adds about 110 members the raw extraction missed, adds the 2 members known
  only from a "now obsolete" mention (ICurve.Length2, IPartDoc.EditSuppress2),
  and lets the current guide win on 10 old forms of current members.
- **Prove.Contratti**: `ObsoleteCallsContract` (every call to an obsolete
  member is MEASURED, i.e. an `OBSOLETE-MEASURED: <bank case>` comment AND an
  exact entry per file, member and case that says what is known of the
  replacement, or FROZEN for Z-C21.7; the calls whose receiver type the source
  does not say are frozen per file and name in
  `Prove/data/obsolete-undecided-frozen.tsv`; all three are exact ratchets.
  88 calls today, 4 measured, 84 frozen, 451 undecided; seen red five times on
  changed copies of real files) and `SelectionMarksContract`.
  `OBSOLETE-MEASURED` on GraphicsRedraw2 x2 (OperazioniTondoTavola),
  AddCenterMark and InsertCosmeticWeldBead. 392 checks.
- **Corrections after the adversarial review** (same branch):
  - the obsolete-calls contract: a marker citing any existing case no longer
    lets a new call through (the `Measured` entries are exact); a type is
    taken only from a declaration IN SCOPE (the same method's blocks, the
    parameters, the fields of the same partial class), never from another
    method's variable of the same name, and a `var` of unknown type hides
    outer names; the return types of methods in the source, `new T()` and
    array elements give a type, which found an obsolete `IView.Name` in
    OperazioniDistintaTavola and three `ISheetMetalFeatureData.KFactor` in
    OperazioniLamiera (both frozen, with reasons) and removed two false ones
    (`.Select` of LINQ in `Banco/Program.cs`); the holes of interpolated
    strings are scanned; the undecided calls have their ratchet; the ratchet is
    seen red both ways;
  - `OBSOLETE-MEASURED` now means "a bank case proves the effect of this very
    call"; what is known of the replacement (tried and failed, or not
    measured) is written in the entry, so the two markers whose replacement was
    not measured are no longer in contradiction with the definition;
  - *behaviour* — a face whose true box does not answer is not a verdict:
    `verifica_sovrasporgenze` counts it as not analysed (inconclusive, new
    `planar_faces_without_true_box`), `incidi_testo` refuses before touching
    the part instead of skipping the overflow check, and the extreme-face choice
    of `Riferimenti` falls back to a point on the face (IFace2.GetClosestPointOn)
    and says "could not be measured" instead of "there is no such face";
  - *behaviour* — putting a pattern back after a refused change of instances
    says "put back as it was" only when GetErrorCode2 says the pattern is sound;
  - the four sheet-metal operations, the anchor movement check and the
    feature-error count of `riordina_lavorazione` read GetErrorCode2 through
    `RebuildError` (*behaviour*: the sheet-metal and anchor messages give the
    error in words instead of «codice N»); the three hand-written unions of
    approximate boxes and the one-axis extent go through `UnionOfBoxes`. The
    state (StatoDocumento) keeps its own reader: it also records warnings,
    which RebuildError leaves out by design.

## 0.113.0 — 28 September 2026 · library 0.138.0

**Silent defects that need no probe (entry Z-C21.2 of the API audit).** Every
fix below is a case where a part, a file or an answer came out wrong with
`riuscita: true`, and the SOLIDWORKS API help alone was enough to decide. Each
fix quotes its help page in the code. The new bench cases are written to go
red on the previous library and green on this one.

- **`esporta_stl`, `esporta_step`, `esporta_iges` clear the selection before
  `SaveAs3`** (L13-1). IModelDocExtension~SaveAs3.html: "Exports the entire
  model, unless faces or bodies are selected, in which case, it exports only
  those". A face left selected produced an STL of that face alone, and the
  file check (84 + 50 bytes per triangle) compared the file with itself.
  `triangoli_dalla_geometria` is still reported, not enforced: a threshold on
  the order of magnitude needs a measurement on curved parts first.
- **`taglia_membri` always sets the WeldGap bit, also with gap 0** (L9-2).
  InsertWeldmentTrimFeature2.html: without the bit "the weld gap defaults to
  the last value specified in the SOLIDWORKS user-interface". A gap given by
  hand in Trim/Extend was silently inherited while the answer said
  `gioco_mm: 0`. This changes behaviour for anyone relying on the UI gap; the
  catalog says so.
- **`combina_corpi` resolves body uids by signature** (L6-1), through
  `CorpiDaUid` + `FirmaCombacia` like `sposta_corpi` (entry E4), instead of by
  position in a fresh `GetBodies2` (GetBodies2.html: "May vary the order in
  which bodies are returned"). `IndiceDelCorpo` is gone. The bodies found are
  passed to `InsertCombineFeature` as they are (no search by COM identity in a
  second `GetBodies2`, which was never measured); "is this the main body?" is
  asked through `ISldWorks.IsSame`. New `corpo_principale_rif` and
  `corpi_utensile_rif` name the bodies of an earlier step of the same plan —
  without them a "cut, then combine" plan had no valid way to name a body.
  Names in the answer are state uids, or «C<n> (per posizione)» when the body
  was taken by position.
- **`verifica_sovrasporgenze` checks hidden bodies too** (L12-1:
  `GetBodies2(swSolidBody, false)`) and **no longer treats a curved face
  without tessellation as vertical** (L12-2: IFace2~Normal.html, "If the face
  is not a planar face, then this property returns 0,0,0"). Such faces are
  counted in the new `faces_not_analyzed` (with `area_not_analyzed_mm2`), and
  when nothing overhangs elsewhere `si_stampa_senza_supporti` is `null`, not
  `true`. `hidden_bodies` says how many hidden bodies were checked; `corpo_uid`
  now resolves by signature. **Open: the mini-probe on the tessellation of a
  hidden body was written, not run** (`TassellaNascosto`, kept outside the
  repository in the session scratchpad): the help does not say whether
  `GetTessTriangles` answers on a hidden body. If it does not, curved faces of
  hidden bodies end up in `faces_not_analyzed` — a non-conclusive verdict, not
  a silent "yes".
- **`modifica_quota` changes angular dimensions in degrees** (M3).
  IDimension~GetType.html (swDimensionParamType_e): "30" on an angle used to
  become 0.03 rad and the equally wrong read-back passed. The answer carries
  `before_deg`/`after_deg` instead of `prima_mm`/`adesso_mm`; integer
  dimensions are refused. **Read-only and driven dimensions are refused** (M8):
  SetSystemValue3.html, "This method allows you to change the value of a
  read-only dimension".
- **`cambia_estrusione`: "ICE" is not "cut"** (M7). A boss made with
  `estrudi_su_faccia` has type "ICE" like face cuts (GetTypeName2.html);
  cut or boss now comes from `IExtrudeFeatureData2.IsBossFeature`, so a boss
  answers `tipo: estrusione` and "passante" on it is refused, and a real "Cut"
  is accepted. IsBossFeature is asked **only for "ICE"**: "Cut" and "Extrusion"
  decide by themselves. Asked of every extrusion, it answered false on the
  base plate (the first extrusion of the part), and "passante" on the plate
  went through (`il_cieco_diventa_passante`, step 6, red in the full bench).
  Measured by `Controlli/TipoDellEstrusione`: the base is "Extrusion" and
  IsBossFeature false; every later extrusion, boss or cut, even from a sketch
  on a plane, is "ICE" for GetTypeName2 (GetTypeName says Boss or Cut).
  **Two independent directions are refused** (L5-1:
  BothDirections.html): only direction 1 was written and read back.
- **Rebuild errors after `ModifyDefinition` and after creation are read**
  (ModifyDefinition.html: "call IFeature::GetErrorCode2"):
  `flangia_base` reads `GetErrorCode2` like its three siblings and refuses a
  flange that added less than half of the hand-computed volume (L10-4);
  `foro_guidato` reads it after each `ModifyDefinition` but decides only after
  positioning (before that the hole still sits at HoleWizard5's provisional
  point and may be in error while the final hole is sound; earlier codes are
  only quoted in the refusal), always measures the volume (not only with
  anchors), and removes a hole that is in error or removed nothing (L4-3);
  `sopprimi_istanza` reads it, refuses a pattern left with no faces, and
  refuses when the lost faces are not seed faces × copies removed, putting
  the array back (L6-5). The answer carries `faces_per_copy` (null when the
  seed faces could not be read, i.e. that check did not run), and the catalog
  says that copies merged together are refused for the same reason.
- **`sopprimi_istanza` refuses two-direction grids** (M6). SkippedItemArray.html
  gives I = n2·(i−1)+(j−1) for a bi-directional pattern: a single copy number
  does not name a grid position, and "copy 3" on a 3 × 2 grid removed
  instance [2, 2]. Linear (`IsDirection2Specified`), circular (`Direction2`,
  `TotalInstances2`, or `Symmetric`) and curve-driven (`Dir2Specified`,
  `D2InstanceCount`) patterns are all checked.
- **`accoppia` no longer offers concentric and tangent** (M17): faces are
  chosen by direction through `IFace2.Normal`, i.e. only planar faces, and
  between two planar faces those mates cannot exist. They are refused with the
  reason, before touching anything.
- **`distinta_taglio_in_tavola` finds the quantity column by type first**
  (L16-8: GetColumnCustomProperty.html, "returns an empty string if the column
  is not a user-defined column"; `GetColumnType2` = swWeldTableColumnType_Quantity),
  then by the "QUANTITY" property, and says so in `quantity_column_found_by`
  and `comparison_done`. Its bench case now uses SOLIDWORKS' stock template
  (`{SOLIDWORKS}\lang\english\cut list.sldwldtbt`), where QTY is a typed
  column: the shop template of the options has QTY as a property and was
  already found by name on 0.136.0. That QTY is type 402 in the stock
  template is taken from the help, not measured: the case says what to do if
  it comes out "name".
- **The anchor refuses the tangent edge of a fillet** (C2-1 of the audit
  completion): the rule "the other face must be planar and perpendicular"
  never checked "planar", and a fillet's Normal is 0,0,0 — cosine 0, exactly
  like a real perpendicular wall. `IsPlane()` now comes before `Normal`; on a
  curved face the normal is taken at the edge midpoint with
  `ISurface.EvaluateAtPoint`, and the edge is refused when that face is
  tangent (a fillet) or oblique, and accepted when it meets the face at 90°
  (a sharp edge against a cylinder). If
  the check cannot look (no faces, no normal, an exception) it refuses instead
  of accepting (N6); an anchor on a non-planar working face is refused (N24).
  The anchor's movement probe now reads the `SetSystemValue3` status and the
  moved value (M12): a dimension that did not move proved nothing.
- **Bench**: steps accept `prima_seleziona` (entities left selected before the
  step) and `prima_nascondi` (bodies, or the body of a face, hidden before the
  step); `@quota(angolare=true)` picks the only angular dimension; with
  `uid_dal_passo` bodies now carry their signature like the bridge sends it.
  Ten new cases: `lo_stl_non_esporta_la_faccia_selezionata`,
  `il_taglio_senza_gioco_non_eredita`, `lim_combina_col_corpo_letto_prima_del_taglio`,
  `il_corpo_nascosto_che_pende_si_vede`, `l_angolo_dello_smusso_si_cambia_in_gradi`,
  `il_rialzo_cresce_e_non_e_un_taglio`, `lim_foro_guidato_fuori_dal_pezzo`,
  `lim_griglia_non_si_conta_come_una_fila`,
  `la_distinta_di_taglio_dice_se_ha_confrontato`, `lim_ancora_sul_bordo_del_raccordo`.
  `il_taglio_senza_gioco_non_eredita` cannot reproduce a gap typed in the UI:
  that scenario stays a manual probe, and if the case stays green on 0.136.0
  it is a regression case, not the proof of L9-2 (the case says so).
  `salta_la_seconda_copia` also expects `faces_per_copy: 1`. New placeholder
  `{SOLIDWORKS}`: the SOLIDWORKS install folder (`SW_INSTALL_PATH`, as in
  build.ps1, or the default one), for stock files that cannot be versioned.
- Numeric comparisons of `GetErrorCode2` in the new code use
  `swFeatureError_e.swFeatureErrorNone` instead of the digit (Z-C7).
- Contracts: the floor of refusals that name a way out goes from 136 to 146.

## 0.112.0 — 28 September 2026 · library 0.137.0

**The whole SOLIDWORKS API, searched and described by reflection (entry
U1.1).** Two new MCP tools, nine in all: `api_search` and `api_signature`.
They read the interop loaded in the bridge (`typeof(ISldWorks).Assembly`) and
the `swconst` enums; `swmotionstudy` is listed read only when present. They
never touch SOLIDWORKS and work with it closed.

- **The engine is in the library**, `Riscontro.Disegno\Api\ApiCatalog.cs`;
  the bridge only registers the tools.
- **13,945 exposed members**: the 14,928 of the interop minus the four old
  interfaces `IModelDoc`, `IBody`, `IFace`, `IComponent` (983). A search names
  them with the sister to use; a signature request answers «old interface: use
  …» and never prints the old signature.
- **A card says only what is known.** Every overload with real types, property
  or method, `&` out parameters by name, the typed `I…` variant, the numbered
  family with the last one «latest by number, not verified», the Void flag.
  Which enum an `Int32` takes comes ONLY from the hand-written map in
  `Strumenti/api_knowledge.json` (otherwise «enum not tabulated»), and array
  indices only when measured (otherwise «index not tabulated»).
- **`Strumenti/api_knowledge.json`** (new, embedded in the executable): state
  (`forbidden`, `confirm`, `gray`, `never_probed`, `probed_read`,
  `probed_modifies`), enum map, measured indices, measured types of `Object`
  parameters, source. First fill from the memories, the RAG notes and the
  "measured" comments of the sources: 167 members (52 forbidden, 33 confirm,
  16 gray, 21 never_probed, 31 probed_read, 14 probed_modifies), each name
  checked against the interop by the contract.
- **`Controlli/api-inventory.ps1`** writes the inventory from PowerShell; the
  contract `ApiSignatureMatchesInventory` compares it with the C# formatter
  (14,928 lines, zero differences, seen failing on one lost `&`).
- Contracts: the point checks of the entry, knowledge naming only real
  members (seen refusing an invented one), hidden interfaces losing no name
  (seen saying no for `IMassProperty`), both tools through the real bridge.
  `tools/list` is now asserted at nine.
- **Bench:** no case, by the entry's own rule — these calls do not touch the
  part. The bench still has to run on library 0.137.0 before release, because
  the library changed.

## 0.111.0 — 27 September 2026 · library 0.136.0

**The adversarial review of the day's work (entry Z-C20).** Three reviewers and
one adversarial verifier per finding went over everything closed on 27
September; seven findings were confirmed real, and eight more were sensible.
All of them are fixed here, each with a bench case where the bench can build
the situation.

- **The guards of `modifica_quota` in one place** (`OperazioniGuardie.cs`):
  rebuild result, features in error, body count, openings that break out of the
  material, a chamfer or fillet that no longer fits. `variabile_globale`,
  `lega_quota` and `cambia_estrusione` had one or none — a variable driving the
  thickness, set to 1 under a 2 mm chamfer, rebuilt clean, changed the shape
  and answered "done". Now they refuse and put things back (the old equation
  rewritten, a new one deleted, an unbound dimension restored), and say
  whether the volume came back.
- **`cambia_estrusione` keeps a mid-plane extrusion mid-plane.** A symmetric
  plate 100 deep asked for 120 became blind from 0 to 120 — it moved by 60
  instead of growing by 10 per side. "Through" on a mid-plane cut goes through
  both ways; end conditions with a reference (up to a surface, a vertex, a
  body) are refused, because the reference could not be restored.
- **Drawing dimensions: "already dimensioned" is read before the call, and per
  CYLINDER.** Two Ø8 holes, one through and one blind, are two groups — the
  second one used to be skipped as already dimensioned, with no callout and no
  depth. Two Ø30 journals, or a Ø30 step and a Ø30 cross hole, are different
  cylinders and no longer cancel each other. A hole-position dimension no
  longer marks the reference edge's length as dimensioned.
- **Turned parts seen from the side count only planar faces.** On a Ø30 x 40
  shaft chamfered 1 x 45° the chamfer circles made the lengths 1, 39 and 40;
  now only 40, and the chamfer is reported (`tondo_note`). Seen end-on, a
  chamfer edge is not an outer diameter (`bordi_non_quotati`).
- **The STEP of an assembly counts one shape per part, not per instance.**
  Measured: an assembly with the same base twice and a lid once writes 2
  solids — the shape of a part is written once and the instances are
  occurrences. The expected count was per instance (3) and refused a correct
  STEP on every assembly with repeated screws or feet. Now per unique part
  (file plus configuration); a solid with voids (`BREP_WITH_VOIDS`) counts too.
- **A boss is not a hole.** The foot of a boss is also an inner loop of a
  planar face; the wall that starts from it goes outward, so it gets a
  diameter dimension, not a hole callout.
- **Smaller ones:** `lega_quota` compares in document units (angles in degrees)
  and reports a replaced binding; `variabile_globale` reads back in mm from the
  document units; the state reports `equazioni_totali` beyond twenty; IGES of an
  assembly is refused (not verifiable, depends on a global preference); a
  rejected `specchia_componente` removes the mirror; `dipendenze` without an
  answer says "unknown" for missing files too; the assembly mass skips the
  per-part cross-check for a component using another configuration, and asks
  that configuration for the material.
- **The last live \`EditDelete\`.** Removing a hole table born wrong called
  \`modello.EditDelete()\`, which asks for confirmation in a dialog — inside
  SOLIDWORKS the COM call never returns. Found by the workflow that designed
  Section U; replaced with \`DeleteSelection2\`, and the ban is now a rule of
  \`Prove/ProveLeFirmeVietate.cs\` (it was only written in comments).
- **The tested version is declared (entry H17).** The README says "tested only
  on SOLIDWORKS 2021 SP5.1"; the installer accepts any version and warns when
  it is not the tested one (older ones can be tried, newer ones are untested).

New cases: `tavola/due_fori_uguali_uno_passante_uno_cieco`,
`tavola/la_borchia_non_e_un_foro`, `tavola/l_albero_smussato_non_quota_le_gambe`,
`lavorazioni/la_piastra_simmetrica_cresce_dalle_due_parti`,
`quote/la_variabile_non_scavalca_lo_smusso`, `assiemi/due_basi_uguali_uno_step`,
`esporta/lim_iges_di_un_assieme`.
`esporta_iges` now declares `parte` only. Ninety-five operations.

## 0.110.0 — 27 September 2026 · library 0.135.0

**Which files an assembly stands on, and which are missing (entry Z-B3).**
An assembly is a graph of files on disk, and the model could not know that
one of them was gone. New operation `dipendenze` (any document). Measured
first: `GetDependencies` returns flat name/path pairs at all levels, and
**the parameter literally called «list broken references» marks nothing** —
a component file renamed on disk is listed with its path like the others.
Only the disk can tell (`File.Exists`); reopened, the assembly shows that
component suppressed with no document and `OpenDoc6` error 2.

- The answer lists every file with `esiste`, and the missing ones with a
  warning (find or replace them before measuring or exporting).
- The assembly state adds `file_mancante` to a component whose file is not
  on disk.

New case `assiemi/l_assieme_dice_da_che_file_dipende` (the healthy assembly;
the missing file was seen by the probe). Ninety-five operations.

## 0.109.0 — 27 September 2026 · library 0.134.0

**Change a feature that is already there (entry Z-F4).** A blind hole that
must go through, a plate that must grow: until now the only way was to
rebuild the part. New operation `cambia_estrusione` (`feature_uid`, and
either `passante` or `profondita_mm`) for extrusions and extruded cuts.
Measured first (120×80×10 plate, Ø8×5 blind hole = 95748.67 mm³):
`GetDefinition()` as `IExtrudeFeatureData2`, `SetEndCondition` /
`SetDepth`, `ModifyDefinition` — through-all 95497.35, blind 7 again
95648.14; the base extrusion 10 → 12, with the hole on the top face following
it (114848.14).

- The definition is read back (end condition and depth), with volume and
  features in error before and after; if the change breaks a feature
  downstream the old definition is put back and the operation refuses.
- A blind depth past the material gives no error and is already through:
  when the volume does not change, the answer says so.
- `passante` on a boss extrusion is refused (there is nothing to go
  through); other feature types point to `modifica_quota`.

New case `lavorazioni/il_cieco_diventa_passante`. Ninety-four operations.

## 0.108.0 — 27 September 2026 · library 0.133.0

**The right bracket and the left one (entry GA11).** New operation
`specchia_componente` (`componente`, `piano` of the assembly, `modo`,
optional `suffisso`). The danger is in the link: linked and not wanted, you
change the right bracket and the left one changes too; unlinked and not
wanted, you correct the right one and the left stays wrong without saying so.
So `modo` is always asked, never defaulted. Measured first with the modern
route (`FeatureManager.CreateDefinition(swFmMirrorComponent)` →
`IMirrorComponentFeatureData`, typed `Component2[]` arrays):

- `istanza`: one more component of the SAME file, mirrored;
- `opposto_legato`: a NEW file whose tree is a single `MirrorStock` feature —
  it follows the original;
- `opposto_slegato`: a new file with the features COPIED — independent.

Afterwards the answer is VERIFIED: the new component's true-box centre
against the mirror of the original's centre (within 0.01 mm), and for the
opposite-hand files the link read from the new file's tree. New case
`assiemi/la_staffa_destra_e_la_sinistra` (all three, on three planes).
Ninety-three operations.

## 0.107.0 — 27 September 2026 · library 0.132.0

**A photo to trace, to scale, in a sketch (entry Z-F5).** The most frequent
input in a small shop is not a DXF: it is a photo, or a sketch with the
dimensions in pen. The model can read the image; it lacked the place to put
it. New operation `immagine_nello_schizzo` (`percorso`, `piano`,
`larghezza_mm` — the true width of the whole image — and the lower-left
corner `x_mm`, `y_mm`). Measured first with a 400×200 px PNG:
`InsertSketchPicture` puts it in at 1 px = 1 mm; `SetSize` with the aspect
ratio locked and `SetOrigin` put it where asked (120×60 mm at 10,20).

- The scale is VERIFIED, not believed: the first and last pixel of the first
  row, mapped into the sketch with `GetPointOnSketchFromPixel`, must be
  (columns − 1) × mm-per-pixel apart within 1% (119.7 mm in the case).
  **That call takes (column, row), the opposite of its argument names**
  (measured: the first argument moves along X; a row beyond the height gives
  null).
- A PDF does not go in (null): the refusal says to convert the page to PNG.
- The answer gives the rule from a pixel to millimetres in the sketch.
- The bench learns `{DATI}` (`Banco/dati/`, versioned): input files a case
  reads, since `{LAVORO}` is emptied every run.

New case `piano/la_foto_sta_in_scala`. Ninety-two operations.

## 0.106.0 — 27 September 2026 · library 0.131.0

**Reorder the tree (entry Z-F3).** A shell before a hole gives a different
part from a shell after it, and moving the feature is a weekly gesture. New
operation `riordina_lavorazione` (`feature_uid`, `rispetto_a_uid`, `dove`:
`prima`/`dopo`). Measured first on a 100×60×40 box with a Ø10 through hole and a
3 mm shell opening the top: `ReorderFeature` returns true and the volume
changes (hole then shell 56485.7 mm³, shell before the hole 51952.38), and
putting it back gives 56485.7 again; an impossible move (the hole before the
extrusion it sits on) returns false and touches nothing.

- Features are named as everywhere in the bridge, and the name is checked
  against the `feature_id` the state gave (a name can be renamed, the ID
  cannot).
- After the move the tree is rebuilt and the features in error counted: if
  the move broke one downstream, the feature goes back where it was and the
  operation refuses. The order is read back (true from `ReorderFeature` is
  not trusted alone), with volume and bodies before and after; an order that
  changes nothing in the part is said.

New case `lavorazioni/l_ordine_cambia_il_pezzo`. Ninety-one operations.

## 0.105.0 — 27 September 2026 · library 0.130.0

**Give a number a name (entry Z-F1).** Two new operations on SOLIDWORKS'
own equations: `variabile_globale` creates or changes a global variable (a
value in mm, or an expression such as `2 * "parete" + 1mm`), and
`lega_quota` makes a dimension driven by an expression. A dimension driven by
`parete` follows every change of `parete` by construction — the identity
system SOLIDWORKS already has, and the only kind of model someone who is not
the designer can change without breaking it. Measured first on a 120×80×10
plate: `IEquationMgr.Add3` returns the index; with the thickness linked,
`parete` = 12 mm gives 115200 mm³ and 15 mm gives 144000.

- `get_Status` cannot be trusted (−1 after a successful `EvaluateAll`, 1
  after a successful `set_Equation`): every answer is checked on the VALUES
  read back — the equation's and the dimension's.
- «spessore» (and «Spessore») is refused by the Italian SOLIDWORKS even on a
  part with no properties, and an equation that uses it evaluates to 3: the
  refusal says so and proposes another name.
- The name of a dimension in an equation is its `FullName` without the part
  at the end («D1@Estrusione-Estrusione1»).
- The part state lists its equations (`equazioni`), so a driven dimension is
  known before someone tries to change it by hand.

New case `quote/la_parete_ha_un_nome`. Ninety operations.

## 0.104.0 — 27 September 2026 · library 0.129.0

**What holds a component, and a new operation to fix or free it (entries
Z-D3, Z-D4, Z-D5).** The assembly state said HOW MANY mates a component has,
not WHICH; it did not say whether a component is a sub-assembly, hidden or
transparent; and fixing or freeing a component was not possible. Measured
first: `GetMates` returns `IMate2` objects whose `Type` is `swMateType_e`;
transparency lives in `MaterialPropertyValues[7]` (0.75 after
`SetComponentTransparent`), only once the component has its own appearance;
`FixComponent`/`UnfixComponent` on the selected component work both ways,
including on the first component, which SOLIDWORKS fixes by itself.

- Assembly state, per component: `accoppiamenti_tipi` (by name: `coincidente`,
  `concentrico`, `parallelo`, …), `e_sottoassieme` (null when the component is
  suppressed: «I don't know», not «no»), `visibile`, `trasparente`.
- New operation `fissa_componente` (`componente`, `fisso` — both required:
  fixing and freeing are opposites). Both calls return nothing: the result is
  read back with `IsFixed`, and the answer says how many mates still hold a
  freed component (zero means it rests there and moves).

New case `assiemi/fisso_non_e_accoppiato`. Eighty-eight operations.

## 0.103.0 — 27 September 2026 · library 0.128.0

**The whole assembly: how much it weighs, how big it is, and the STEP to send
(entries Z-D1, Z-D6).** `proprieta_di_massa`, `ingombro_corpo`,
`esporta_step` and `esporta_iges` refused an assembly («not a part»).
Measured first (base in AISI 304, lid in S235JR, a third instance
suppressed):

- `CreateMassProperty()` on the assembly, with no bodies added, gives
  exactly the sum of the parts (1.06752 kg); the suppressed component does
  not weigh. The answer checks it against the parts read one by one and stops
  if they differ; a part without material makes the mass null, naming it.
- `IComponent2.GetBodies3` returns bodies in PART space (the lid at y 0..4
  instead of 5..9): the bounding box uses a copy of each body moved with the
  component's `Transform2`.
- The assembly STEP is written like a part's, and the check is the count of
  `MANIFOLD_SOLID_BREP` in the text against the solid bodies of the
  unsuppressed parts: reopening an assembly STEP with `LoadFile4` opens the
  «New document» dialog and the call never returns, so
  `riapri_e_confronta` is refused on an assembly, saying why.

Suppressed components are listed, never silently dropped. The STL of an
assembly is still refused, now pointing to `esporta_step`. New case
`assiemi/l_assieme_si_pesa_si_misura_si_manda`.

## 0.102.0 — 27 September 2026 · library 0.127.0

**A turned part dimensioned from the side (entry GT14).** Until now the
longitudinal view of a shaft or a bushing was refused: its only edges are the
end circles seen edge-on. Measured on the stepped shaft of the `rivoluzione`
example (Ø30×40, Ø20×30, Ø34×20):

- two edge-on circles selected + `AddDimension2` give the distance between
  the faces — the LENGTHS, now placed in parallel from the first face (the
  same single zero as the holes): 40, 70, 90;
- two silhouette edges of the same face give, by themselves, a diameter
  dimension with the Ø prefix — the DIAMETERS: Ø34, Ø30;
- but SOLIDWORKS does not always expose them. `ISilhouetteEdge.GetFace` is
  null until the drawing is redrawn and the face asked a second time; on the
  bushing it stays null, and those silhouettes are tried in pairs — the
  dimension is kept only if SOLIDWORKS puts the Ø on it AND its value is the
  diameter of a coaxial cylinder of the part not yet dimensioned; the Ø20 of
  the shaft has a silhouette that cannot be selected at all, and the answer
  says so in `tondo_diametri_non_raggiunti` instead of keeping quiet.
- Roads measured and closed: `GetStartPoint` from out of process makes
  SOLIDWORKS raise (RPC_E_SERVERFAULT), `GetCurve` is null, picking by
  position takes one silhouette at a time, a centreline plus a silhouette
  measures from the SHEET origin (212.49) and `Diametric` cannot be set.

The probes used `IModelDoc2.AddDimension2`, which the contract forbids (no
direction, entry GP6), and `Extension.AddDimension` with two entities creates
nothing: the code uses the DECLARED horizontal/vertical dimensions
(`AddHorizontalDimension2` for the lengths, `AddVerticalDimension2` for the
diameters of a horizontal shaft) — on two silhouettes of one face SOLIDWORKS
still adds the Ø by itself. Silhouettes are read again before every
dimension: after a new dimension the old ones may no longer select.
Every dimension is read back against the geometry before it stays. The two
views that follow repeat nothing. `esporta_pdf` now counts the dimensions
whose text cannot be read (`quote_non_controllate`) instead of skipping them
silently. New case `tavola/l_albero_a_gradini_quotato_di_fianco`; the side
view of `tavola/la_boccola_tornita_non_e_un_foro` is no longer refused.

## 0.101.0 — 26 September 2026 · library 0.126.0

**Do the dimensions reach the PDF? (entry Z-C9, the «objects» half).** The
drawing counters count what is in the document: a dimension on a layer that
does not print, or in a hidden view, counts — and never reaches the shop.
`esporta_pdf` now reads the PDF text back and requires every number printed
by the drawing's dimensions to appear there at least as many times as the
dimensions print it («at least»: the PDF also has title-block numbers). Seen
firing before being trusted: the «Ø12 THRU» callout moved to a non-printing
layer, and the export reports «12 (on the drawing 1, in the PDF 0)». One case
(the check stays quiet on a good drawing).

**`palloncini` is now `bollatura`** — the shop word for BOM balloons,
and the name the operation, the catalogue and the messages use. Tried on a
real three-part assembly (read-only, drawing saved outside the vault): the
isometric view ballooned items 1 and 2 and answered «done» while item 3 —
covered in that view — had no balloon anywhere. `bollatura` now compares the
item numbers of the BOM with the balloons of the WHOLE drawing and names the
items still without one («balloon another view, where it shows»). And a view
where every item is already ballooned elsewhere is no longer a failure: it
answers «nothing to add in this view». The old warning comparing balloons
with visible components is gone: it fired on every repeated component and
the item-number check says the thing that matters.

**A circle is not always a hole (`quota_in_tavola`, entry GT12).** Looking
for a blind hole the callout would not recognise, a turned bushing (Ø30 × 30,
axial blind bore Ø8 × 10, made by revolution) showed a worse defect: the Ø30
OUTLINE got a hole callout «Ø30 ↧30», drawn on top of the real «Ø8 ↧10», and
in the side views the two circles seen edge-on were «holes whose callout did
not appear». A hole now is a circle that faces the reader AND is an inner loop
of a planar face (the centre-mark rule); the outline of a round part gets a
diameter dimension, read back (edge-on, `AddDiameterDimension2` says 60 for a
Ø30 — measured), reported in `diametri_esterni`; edge-on circles are listed in
`cerchi_di_taglio`, and a view with nothing else is refused saying so. Every
hole also reports `passante`/depth READ FROM THE GEOMETRY (the cylinder and its
other circle: inner loop of a plane = through; outer loop or cone = blind),
holes are grouped by diameter AND depth, and when the callout does not appear
the depth is written into the diameter dimension («Ø8 ↧5», tried and looked
at). Revolved bores do get a proper callout: the fallback has not yet been
seen on a real hole. New case `tavola/la_boccola_tornita_non_e_un_foro`.

## 0.100.0 — 26 September 2026 · library 0.125.0

**`distinta_taglio` was wrong, and its case was green (entry GT2).** It put
EVERY bar into one cut-list item: the 1000 x 800 frame came out as
«QUANTITY 4, LENGTH 1000» — four 1000 mm bars where two are 800. The case
only asked that the bodies be four, and they were. The cause, measured: a
part made of structural members is not a WELDMENT (`IsWeldment` false),
and SOLIDWORKS does not group bars by length without the «Weldment»
feature. Now `distinta_taglio` adds that feature when missing, turns on the
automatic cut list on the solid-bodies folder, reads the items back
(description, length, quantity — recognised by their expression, since the
property NAMES are localised) and checks that the quantities add up to the
bodies. The same fix makes `distinta_taglio_in_tavola` work: the cut-list
table is born in the drawing, one row per length, and its quantity matches
the part. The operation is now `Provata`, and it takes the view by its
uid (V1) like the other drawing operations — the first bench case that
called it with «V1» got «no such view». One new case, and the old case now
asks for the two items.

New operation `vista_sviluppo` (entry GT13): the flat pattern of a sheet
metal part as a drawing view, with its bend lines. The view is MEASURED:
its size comes from the visible edges, not from the view outline — which
said 92.6 mm for a flat length of 80.7, because it includes the bend note
and a margin — and is compared with the flat length measured on the part
before the view is placed. Refused on a part that is not sheet metal, and a
view that is not flat or measures something else is removed. Two cases.

New operation `saldatura_in_tavola` (entry GT7): the ISO 2553 weld symbol
(fillet, V, bevel, square butt) with the throat «a» and optional length,
all-around and field-weld flags. The operation finds the joint itself —
the longest visible edge where one body touches another — puts the arrow
on it, moves the symbol out of the part, and reads everything back. The
symbol code is the one from SOLIDWORKS' symbol library, `<WELD-FILL>`:
measured, with a wrong code (`<IGWS-FILLET>`, «Fillet») the symbol is
born, reads back identical, and draws no triangle — only the PNG showed
it. Refused on a single-body part and on a view with no visible joint; a
symbol born wrong is removed. Two cases.

**Two defects found by LOOKING at the drawings, with the bench green.**
The cut-list table used the shop's table template, 17 columns wide, and at
250/200 mm it ran off the sheet after the fourth column. The flat-pattern
view at 300/90 mm, scale 2:1, ran into the title block. Now the cut list
keeps only the columns a workshop needs (item, quantity, length, the two
angles, description; `colonne` chooses others or «tutte»), and both the
table and the view are measured and placed in a free spot inside the
border, away from the other views and the title block (read from the
sheet format when it declares one, estimated otherwise). If there is no
room, they are removed and the operation says so.

`quota_in_tavola` (entry GT12): the hole callout of a blind hole already
carries its depth — SOLIDWORKS puts `<HOLE-DEPTH>5` in the suffix — and
now the operation reads it back (`fori[].profondita_mm`, `passante`). The
text of every dimension has a real rectangle: `IDisplayData` gives
position, height, angle and — `GetTextInBoxWidthAtIndex` — the width,
even without a box. The operation now reports texts that fall on another
text or on a line of the part (`testi_che_si_accavallano`). One case: a
blind hole with its depth, no hole dimensioned from the side view, no text
overlapping. (Pieces of the same dimension — a hole callout «Ø8 ↧5» is
four texts side by side — are not compared with each other: the first
bench counted four overlaps that on paper are one word.)

Entry Z-C13: two section views «A-A» on the same drawing are two different
cuts that look like the same one, and SOLIDWORKS allows it. `sezione_in_tavola`
and `dettaglio_in_tavola` now refuse a letter already used by a section or a
detail — read from the views (`IDrSection.GetLabel`, `IDetailCircle.GetLabel`),
not from what was asked — and report the letter they actually got. Both also
take the view by its uid (V1), like the other drawing operations. One case.

**Center marks (entry W2.11), blocked since 17/9 — and the block was mostly a
counter that lies.** SOLIDWORKS puts a center mark on every hole when it
creates a view (document option «center marks on holes», on in the house
template), and `IView.GetCenterMarkCount()` does not count those: it said 0
on a view with three marks. Enumerating them (`GetFirstCenterMark`/`GetNext`)
finds them all. The drawing state now counts them that way — an old case that
asserted «0 center marks» on three holes was green for a week on a false
zero. From code, no new mark is born: `AutoInsertCenterMarks2`,
`InsertCenterMark2/3` and `AddCenterMark` (on a circle selected in the view)
were all measured; with the automatic marks deleted, `AddCenterMark` adds
none, and on fillet arcs none either. So `croci_di_centro` is now a CHECK: it
recognizes the holes that have a mark, tries to add the missing ones, and
refuses — naming the document option — when a hole stays without one.
`Provata` again. One case.

New operation `tolleranza_quota` (entry Z-F2): a tolerance on a MODEL
dimension — symmetric (±), bilateral, or an ISO fit (H7 for a hole, g6 for a
shaft) — which then reaches the drawing by itself with `quote_dal_modello`.
SOLIDWORKS computes the ISO 286 deviations of a fit; the operation checks
them against its own copy of the ISO 286-1 table (IT5..IT11 up to 500 mm:
the width must be the IT grade, H starts at zero, h ends at zero) and puts
the dimension back if they disagree. It also sets the tolerance precision:
measured, without it an H7 on a 10 mm hole printed as «10 H7 +0 / 0» —
«exact» — because the limits followed the dimension's zero decimals. One
case (fit, symmetric, and IT14 refused).

**An adversarial review of this release, before the bench** (three lenses,
each finding checked by a refuter) found and this version fixes: the weld
symbol accepted when the symbol count could not be read (it could re-read an
old symbol), and «field weld» / «both sides» were not checked; the free-spot
search ignored tables and notes already on the sheet; `tolleranza_quota` on
an ANGULAR dimension took radians for metres (now refused), and a tolerance
whose precision could not be set answered «done» (now refused and restored);
`vista_sviluppo` answered «done» when the part's flat length could not be
measured; `croci_di_centro` took every full circle for a hole — a disc's rim
too (now: the circle faces the viewer and is an INNER loop of a planar face;
`FaceInSurfaceSense` was measured and does not tell, it depends on how the
surface was made); a section with no hatch stayed on the sheet and its letter
blocked the very remedy the message suggested; «0 overlapping texts» when no
text had been read; hidden cut-list columns declared without re-reading;
«feature created» on a cut list that already existed; the duplicate-letter
check looked at the current sheet only; `reso_saldato` true when
`IsWeldment` could not be read; and a failed `distinta_taglio` left the
«Weldment» feature in the part (now removed).

New operations `distinta_base_in_tavola` and `bollatura` (entry GT9 — «bollatura» is the shop word for BOM balloons): the
bill of materials of an assembly (`IView.InsertBomTable4`, top level by
default, the type chosen by name because `swBomType_e` starts at 1), checked
against the assembly itself — items = distinct components, quantities =
instances, two independent readings — and placed in a free spot; then the
balloons (`AutoBalloon5` with an options object), counted on the view before
and after, with the item numbers read back. One case: a two-part assembly.

## 0.99.0 — 26 September 2026 · library 0.124.0

**Tables are born on this installation.** On 17 September no table would
insert (entry GT2: even a generic 3x3 returned null). One probe on 26
September, with a positive control in the same run (a note, +1 every
time), across a fresh drawing, a saved one and one activated in front:
`InsertGeneralTableAnnotation` (never tried before), `InsertTableAnnotation`
and `InsertTableAnnotation2`, `InsertRevisionTable2` all create their
table; only `InsertWeldmentTable` stays null, in 24 combinations of view,
template, configuration and anchor. What changed since the 17th is not
measured; the likeliest candidate is the K9 fix (`viste_standard` put the
wrong part in the drawing).

`tabella_fori_in_tavola` (entry GT5): the hole table of the drilled face
seen in a view. `InsertHoleTable2` has no argument for origin or face:
they are SELECTED in the view — a corner vertex with mark 1, the face with
mark 2. With the marks swapped the table is born EMPTY (header only) and no
error: so the operation requires rows = holes + 1, removes an empty table,
and reads every row back against the part (each position must match a hole
measured on the view from the origin). On the demo plate: A1..A4 Ø8 at
15/15, 15/65, 105/15, 105/65 and B1 Ø20 at 60/40.

Looking at the drawing found a defect no check saw: the holes were
dimensioned twice, in the table and again with callouts and position
dimensions. `quota_in_tavola` now leaves the holes to the table when there
is one — and the table sits on the SHEET, not on the view, which is why
the first fix did not see it. A table added after the dimensions says the
holes are now dimensioned twice. `RisolviVista` is extracted from
`quota_in_tavola`. Two cases (the drawing, looked at; the table on the
side view, refused).

And the annotations (entries GT6, GT8): `rugosita_in_tavola` (surface
finish, Ra in micrometres), `datum_in_tavola` and
`tolleranza_geometrica_in_tavola` (a geometric tolerance frame: flatness,
perpendicularity, position...). All three were born at the first try — no
modal window — and all three were WRONG on the sheet, which only the PNG
showed: the surface finish ignored its coordinates and landed outside the
border; the datum flag sat on the far side of its edge with the leader
across the part; the frame floated attached to nothing. Then, with the
dimensions in place, the datum flag landed on the 120 dimension line and
the frame on top of the side view. Now each one is attached to the border
edge of the requested side (selected in the view), and moved out of the
part — beyond the dimensions already on that side, and out of the other
views. Ra, letter, symbol code, value and datum references are read back.
Refused: a datum letter already used (two datum A are no error and make
every tolerance citing A ambiguous), a form tolerance with a datum, an
orientation or location tolerance without one, a datum that is not on the
drawing, Ra outside 0–100. Five cases.

Full bench: 1291 tests, 0 failed, 1018 of 1018 expectations verified.

---

## 0.98.0 — 26 September 2026 · library 0.123.0

`piega_da_schizzo`, the fifth sheet-metal operation: a sheet bent along a
line drawn on one of its faces. `InsertSheetMetal3dBend` with the line's
sketch selected was born at the first try; the fixed face is not needed.
Measured on a flat 100 x 40 x 2 sheet (R 2), line across at x = 50, 90°:
volume unchanged, and the bounding box matching the hand calculation — 46
of flat (50 - R - T), the 4.712 bend, 49.288 rising from y = 2. A bend
adds no sheet, it moves it: volume and flat length are checked unchanged,
and the box before and after tells which side SOLIDWORKS kept fixed. The
sheet bends toward the side the face looks at.

The jog was not: `IModelDoc2.InsertSheetMetalJog` (it returns nothing)
created nothing in seven selection variants, and there is no jog among the
feature definitions. It is in the TODO with the variants written down.

Full bench: 1224 tests, 0 failed, 961 of 961 expectations verified.

---

## 0.97.0 — 26 September 2026 · library 0.122.0

`flangia_spigolo`, the third sheet-metal operation of seven (entry GP18):
a 90° flange on a border edge of an existing sheet — how a box gets closed,
with bends on edges in different directions, which `flangia_base` from one
profile cannot do.

Probed before writing, ten attempts (`Controlli/FlangiaSpigolo`).
`InsertSheetMetalEdgeFlange2` returns null, with no exception, with null
sketches, with its seventh parameter (`FlangeOffsetDist` — the length) at
0 or 20, with typed `Edge[]`/`Feature[]`; it creates an EMPTY flange (no
volume added) with a line on a plane perpendicular to the edge. It creates a
real one only with a **rectangle on the flange plane**, the base on the
edge: the end face of the sheet. Measured with profiles 20 and 30 high:
`OffsetDistance` reads 22 and 32 (profile + T, at the outer virtual
sharp), and the flat length grows by (L - R - T) + BA - (R + T) to the
thousandth. SOLIDWORKS replaces the sketch it is given with its own, and
the length follows the profile: `ModifyDefinition` on `OffsetDistance`
answers true and changes nothing.

The operation finds the edge by signature (E19), checks it is straight and
lies between the broad face and an end face as wide as the thickness,
draws the rectangle from the edge along the broad face's outward normal,
and compares the volume added with the hand calculation; a flange born
empty is refused. Three cases: the 25 mm flange that lengthens the flat
by 21.712, and its two refusals (nothing straight left, not a border edge).

And `orlo`, the fourth: a hem, closed or open. `InsertSheetMetalHem2` works
on the selected edge and was born at the first try — no sketch. Measured
on the harness before writing: the open hem is the flange formula with a
180° bend and an inner radius of half the gap, three measurements out of
three to the thousandth; the closed one sits 0.057 above the same formula
with radius 0, like a 0.05 mm inner radius, measured with thickness 2 only
— so for the closed hem the hand calculation is reported, not trusted.
`SpigoloDiBordo` is extracted: flange and hem find their edge the same way.
Three more cases (open, closed, open without gap refused).

The probe also found that a sheet metal part's side is not one face: it is
split per straight segment, each as wide as the thickness — so side edges
are border edges too, and the first «not a border edge» case was wrong.

Full bench: 1216 tests, 0 failed, 955 of 955 expectations verified.

---

## 0.96.0 — 26 September 2026 · library 0.121.0

**The regression has no silent case left.** `r05` — a 2 mm chamfer, then
the plate brought to 1 mm — was the last one, and it was written off as
SOLIDWORKS' own: the chamfer no longer fits, and SOLIDWORKS does not fail,
it rebuilds clean and eats a millimetre of width. The sign was already
there and thrown away: the BASE feature loses a whole face (the one the
chamfer now takes entirely), and `LavorazioniCambiate` wrote it in a note
with the wrong explanation («a blind feature became through»).

Now `modifica_quota` refuses, names the chamfer or fillet, and puts the
old value back when a feature that is not a cut loses faces and a chamfer
or fillet comes after it in the tree. Cuts stay out on purpose: a pocket
that loses its bottom has become a through cut, and that may be the point
(the note says so, now worded for cuts). `RimettiLaQuota` is extracted:
the aperture guard of E18(c) and this one put a value back the same way.

It came out of entry E20. The probe (`prova0089 storia`: two R8 fillets,
then width 60 -> 10 or thickness 20 -> 6) found **no feature in error** in
either case — a per-step error count would have caught nothing. E20
closes as not needed in its written form; this guard is what it found.

Regression (19 cases): **17 hold, 2 break and say so, 0 silent** (it was
12/0/4 of 16 on 15 September). New cases: the thickness below the fillet
radius, refused and restored, and its twin at 12 that fits; and E19's
«two candidates» branch — a plate and its copy touching, the same edge
twice — refused with «non scelgo io».

Full bench: 1194 tests, 0 failed, 939 of 939 expectations verified.

---

## 0.95.0 — 25 September 2026 · library 0.120.0

Edges are found again by signature (entry E19). Faces have had this since
10 September (and the persist ID since E6); edges had only the refusal: a
plan read once — «two holes, then fillet the front right corner» — stopped
at the fillet, because edge uids are ranked by length and the fillet carried
the uid read before the holes. Now, when the uid no longer matches, the
library looks for the ONE edge with the same length at the same point, uses
it, and says so in `riancoraggi` («E11 adesso si chiama E15»). Never the
nearest: an edge shortened by a chamfer has another length and another
midpoint, and the plan is refused. Two candidates are an ambiguity, and it
is said. It applies wherever an edge is taken by uid: fillet, chamfer,
variable fillet, cosmetic thread, the direction of a linear pattern, the
anchor.

**What writing the case found.** With library 0.119.0 the case PASSED
without finding anything: `LunghezzaMm` measured the chord between the
endpoints, and the rim of a hole — a closed edge — was «0 mm long» in the
state. Holes always ranked last and never renumbered anything; a reader of
the state saw a 37.7 mm circle described as an edge of length zero. The
length is now the real one (`ICurve.GetLength3`, already measured in the
weld perimeter), -1/null when unreadable. And `FirmaCombacia` now compares
an edge's length too, not only its midpoint.

The bench learns `-Famiglia <name>` (one folder of declared cases, so a new
case can be seen failing before the code is written) and `uid_dal_passo` in
a step (selectors and signatures from the state before step N, as the
bridge does). New family `ritrovare`, two cases: the fillet that finds E15
and lands on the right corner (the centre of mass says which), and its
twin refused on an edge shortened by a chamfer.

Full bench: 1180 tests, 0 failed, 929 of 929 expectations verified — the
real length changes the order of curved edges, and no existing case moved.

---

## 0.94.0 — 25 September 2026 · library 0.119.0

The anchor on `estrudi_su_faccia` (entry E18). There are no `x_mm`/`y_mm`
here: the point the anchor holds is the CENTRE of the contour, and it
applies to one contour only, round or an axis-aligned rectangle. A circle
gets its diameter dimensioned and the anchor on its centre; a rectangle of
four lines (corners already merged under `AddToDB`, as measured on the
slot) gets horizontal/vertical relations, two dimensions and a centre built
with a construction diagonal, like the pocket. Any other contour is refused
before touching the part, saying which contours the anchor accepts. Measured
on the harness and in three bench cases: a round boss 30 mm from the right
side stays 30 mm from it on a plate that grows, a rectangular boss stays
centred, a triangle is refused.

And `foro_guidato`, the last piece of E18: each position takes its own
`ancora`, all of them or none (the holes share one sketch, and one free
point leaves it under-defined). HoleWizard builds the point sketch; after
the positions are placed the library re-enters it, anchors each point, and
requires the sketch fully defined after the last one. The hole radius is
known only from the table, so a distance smaller than the radius is caught
once the hole exists, and the hole is removed. Measured on the harness:
four M8 holes 20 mm from the edges go to y -40 -40 100 100 and x -80 -80
140 140 when the plate grows. Regression `r01` now builds with the anchor
(same expectations); `r01b` is its absolute twin.

`TogliIlTaglio` now says «l'ho tolto» instead of «il taglio l'ho tolto»,
since it also removes a boss or a guided hole.

Regression (`banco.ps1 -Regressione`, 19 cases): **17 hold, 1 breaks and
says so (r16), 1 breaks silently (r05, SOLIDWORKS' own)** — it was 15/1/2 of
18 with library 0.116.0. The finish line written in E18 («silent down to 1,
r05 left») is reached, and E11 (rollback as oracle) closes as not done by
its own criterion: silent + refused without criterion = 0/18 with r05 left
out. Full bench: 1169 tests, 0 failed, 920 of 920 expectations verified.

---

## 0.93.0 — 25 September 2026 · library 0.118.0

### The number on the drawing is the model's? (entry Z-C2)

«A dimension comes from the model, it cannot lie» holds until someone
writes over the number. Measured the three ways the interop offers:
`SetOverride(true, v)` (read back by `GetOverride`/`GetOverrideValue`),
`SetText(0, "…")` (which writes the PREFIX and switches the value off), and
`ShowDimensionValue = false`. Two reads catch all three, including an
override that happens to equal the model value. The drawing state now counts
`quote_numero_non_del_modello` per view and in total, lists up to ten with
how, and warns. A C# bench group (`numeri`) forges three dimensions and
expects three; on the untouched drawing, zero.

### The slot sketch is defined, and the slot takes the anchor (entry E18)

`asola` drew four entities and nothing else: no relations, no dimensions,
so a finished slot could not be corrected. Now the sketch gets four
tangencies, equal radii, a construction line between the arc centres with
its length dimensioned, the radius dimensioned, and horizontal/vertical when
the slot is at 0 or 90 degrees — measured: change the radius and the centre
distance and the slot stays a slot, volume exact. `asola` takes `ancora`;
an oblique slot dimensions its angle against the anchor's first edge with
`AddSpecificDimension` (angular), because `Extension.AddDimension` returns
null on that pair and `AddDimension2` is forbidden. Without an anchor an
oblique slot is free to rotate, and the response says so. Three bench cases:
the slot corrected through its dimension, the anchored slot, the anchored
oblique slot keeping its 30 degrees on a plate that grows.

---

## 0.92.0 — 25 September 2026 · library 0.117.0

Zero is not «I don't know» (entry Z-C8), and two guards that were looking
elsewhere (Z-C16, Z-C18).

### Counts read from SOLIDWORKS no longer answer 0 when the read fails

A function that reads the CAD and returns `0` from its `catch` makes a
statement: «this part has zero bodies», «zero mm³ were removed». A reader
has no way to tell the read failed. The scanner found 21 such places; a
read-only agent traced every caller and classified them: 12 real defects,
4 true zeros, 5 not reads at all. Then a third syntactic form the scanner
had never seen (`int n = 0; try { … } catch { } return n;`) added ten more.

- `VolumeMm3` is now `double?`, and the compiler listed its 44 call sites;
  `ContaCorpiSolidi`, `FacceDellaFeature`, `ContaFacce`, `ContaFogli`,
  `ContaViste`, `ContaQuote`, `ContaFeature`, `ContaPiani`, the note, centre
  mark, trim-tree, draft-face and triangle counts return -1.
- Callers compare only values that were read. A volume that cannot be read
  **before** an operation is a refusal (nothing has been touched yet); one
  that cannot be read **after** is not: the field is null and the response
  says the material checks were not done.
- The document state writes `null` for a body volume or a face area it
  could not read, instead of leaving the key out (which also meant zero);
  `annulla` no longer redoes a step on an unreadable volume.
- In passing, real false outcomes this closes: a failed read after a note
  or a set of centre marks produced «nothing was created» with the thing
  in place; a failed face count made `sopprimi_istanza` revert a correct
  change; two failed volume reads made a copy look «identical to before».
- `Controlli/zero-o-non-lo-so.js` knows the third form, and with all sites
  fixed or marked `ZERO-E-ZERO:` with a reason it becomes a pre-push guard,
  seen failing on a planted site before being trusted.

### Body numbers are not stable, and three cases were green by symmetry

Measured again: in one run out of three, the copy of a plate was `C1`.
`sottrai_un_cubo_dall_altro`, `ingombro_di_due_corpi_lontani` and
`due_corpi_non_si_esportano` now name the body (`@corpo(nome=…)`) and check
what tells the two apart — the centre of mass of what remains, the position
of the box.

### The union guard compared two different enums

`combina_corpi` refused «the union left two bodies» by comparing
`SWBODYADD` (15903) with `swCombineBodiesOperationAdd` (0): it could never
fire. Measured first: with two bodies that do not touch, SOLIDWORKS does
not create the feature at all, so the earlier refusal answers; the new case
`lim_unione_di_corpi_che_non_si_toccano` pins it, and the guard now looks at
the right value.

---

## 0.91.0 — 25 September 2026 · library 0.116.0

The anchor (entry E18, parts a and b). `x_mm`/`y_mm` are absolute model
coordinates: a hole «20 mm from the edge» on a plate that grows stayed
where it was — clean rebuild, no error, wrong part. `taglio_cilindrico` and
`taglio_rettangolare` now take `ancora`:

```json
"ancora": { "x": {"spigolo_uid": "E7", "distanza_mm": 20},
            "y": {"centrata_fra": ["E3", "E5"]} }
```

For each face axis, either a dimension from the profile centre to a
straight edge, or the centre kept halfway between two opposite edges. Both
axes are required. `x_mm`/`y_mm` stay: they say where the point is, the
anchor says what holds it there, and they must agree.

The rules come from measuring and from an adversarial review written before
the code: the edge carries its signature, is straight, lies in the face and
runs along the other axis, and is not the boundary of a chamfer or fillet;
`AddToDB` is on only around each creation; the sketch must come out fully
defined with no dangling, over-defining or broken relation; after the cut a
movement test (each anchor dimension +0.1 mm, rebuild, restore) and, if it
fails, the cut is removed and the part checked back to what it was.
Centring is per axis (a centre line with its ends on the two edges' lines,
perpendicular, the point at its middle): centring between the edges'
midpoints moved the hole in y at creation.

Measuring found three things the design did not:

- a sketch point or segment is not an `IEntity`: the cast returns null
  silently, and the centre line came out with 2 relations out of 4 — the
  relation count caught it;
- `IModelDocExtension.AddDimension` returns null for a sketch point plus a
  model edge, in all four directions, on three faces. Horizontal/vertical
  dimensions work, chosen from the edge direction in the sketch;
- the expectation of regression case `r01` assumed the plate grows
  symmetrically; it grows on one side (entry E2). Corrected to
  [-40, -40, 100, 100]; with the old numbers it could never hold.

Bench cases: an anchored hole on the front, side and back faces; a pocket
centred between the sides; eight refusals and the chamfer-edge refusal.
Regression: `r03` now builds with the anchor (same expectations), `r03b` is
its absolute twin, `r19` is an anchored hole. The bench attaches signatures
for the edges inside `ancora` too, as the bridge does.

---

## 0.90.0 — 25 September 2026 · library 0.115.0

The dimensioned drawing, looked at (entry GT11). Asked to see a drawing,
the bridge produced one — with the bench all green — that had four defects
nobody's expectations asked about. All four are fixed and have a bench case.

### The numbers sat on the extension lines

`quota_in_tavola` placed the text of each dimension at the middle of the
edge as computed on the sheet, and that computation was wrong twice: it
used the transposed rotation (in a rotated view the Y came out mirrored:
the chamfer strip of the top view at y 70.7 instead of 145.7) and it added
the view position instead of the transform's translation (the same only
when the part is centred on the origin: a 10 mm plate extruded from 0 was
placed 5 mm too high). The vertical texts ended up on or beyond the
extension lines. Horizontal ones survived because the plate was centred in
X. Points now go through `MathPoint.MultiplyTransform`.

### Holes are dimensioned

Every view that shows holes along their axis gets one hole callout per
diameter, with the count in front («4× Ø8 PASSANTE» — prepending to the
existing prefix, because replacing it erased the diameter symbol), and the
hole positions from the two reference edges, left and bottom, in parallel:
15 and 105, 15 and 65 on the demo plate. Each position dimension is
attached to the hole nearest to its side (the first attempt ran an
extension line across the whole part) and re-read: it must equal the sheet
distance divided by the scale, otherwise the answer says it may measure to
the rim instead of the centre. Measured first: circle plus edge with
`AddHorizontalDimension2`/`AddVerticalDimension2` measures from the
centre; with `AddDimension` nothing is created.

### Chamfers get the chamfer dimension

Edges of a chamfer face are no longer dimensioned as lines (the 7.071
hypotenuse), and neither are the edges shortened by the chamfer (the 75
next to the 80, the 5 next to the 10). Where the chamfer is seen in
profile, `IDrawingDoc.AddChamferDim` writes «5 x 45°». Its first dimension
is the angle, not a length: the answer reads both by type
(`swDimensionParamType_e`) and reports side 5, angle 45, face width 7.071.

### No dimension twice in the drawing

Before placing anything, the operation reads the dimensions already on the
sheet and the edges they are attached to, and skips the same model extent
(axis and interval) in another view: the 10 and the 120 now appear once.
The answer lists what was skipped and why. A view with nothing left to
dimension is refused, with the reasons counted.

### Also

- Dimensions stay inside the sheet frame: a band that would leave the
  border moves to the other side.
- `vista` accepts the drawing-state identifier (`V2`): view names depend on
  the SOLIDWORKS language.
- Bench selector: `x`, `y`, `z` read the coordinates of `punto_mm`, so an
  edge can be named by where it is (`@spigolo(asse=Y,x~=-100,z~=20)`).
- Entry Z-C6, second half: the bench writes the names of its green cases
  into `ultimo.json` (`casi_verdi`), and a contract check requires every
  catalogue operation to have a green case in the last run, with
  `quote_dal_modello` as a declared exception (entry Z-E7) that is itself
  checked for staleness. **On its first run it found three operations with
  no case in a normal run**: `foro_guidato`, `estrudi_su_faccia` and
  `raccordo_spigoli` appeared only in regression cases (which run only with
  `-Regressione`) and in hand-written plans. Yesterday's recount had counted
  the regression cases as declared cases. Three new cases, with values
  measured first and equal to the hand calculation (the M8 tapped hole
  removes the 6.8 pilot, the clearance hole the 9).
- A contract check for the bench rule "every step measures something": a
  case step without expectations stops the whole case as a bench error after
  ninety minutes — it happened to the new drawing case on its first run.
- And one for the bench's twin rule as the bench applies it: the twin is the
  FIRST successful step of the same operation, and it must share at least
  one argument value with the refusal (`Attese.SiSomigliano`, the same
  function). The second run stopped on it: the refusal on `V9` shared
  `quante` with the second step and nothing with the first. Seen red on the
  real case before being trusted.
- Rule of the house from now on: every change to the drawing commands ends
  by generating the demo drawing as an image and looking at it against the
  dimensioning rules.

---

## 0.89.0 — 25 September 2026 · library 0.114.0

Axes are checked for identity, and writing the missing bench cases for the
weldment operations (entry Z-C6) found that two of them had never done
anything. 76 runnable operations, 73 in the served catalogue: `fazzoletto`
leaves it.

### `asse_uid` is checked for identity, not just existence (entry E4, the axes)

Until library 0.113.0 an axis in the document state was `{uid, tipo}` and
nothing else. If someone rebuilt it elsewhere under the same name between plan
confirmation and execution, `ripeti_circolare` patterned around the new one,
silently. The state now carries a point on the axis line and its direction
(from `IRefAxis.GetRefAxisParams`), the bridge attaches them to each step as a
signature, and the step refuses when the axis has moved: parallel within one
degree and the line within the point tolerance, otherwise
«L'asse si e' spostato di N mm». It compares the LINE, not the start point, because an axis can be
redrawn longer without changing. Two bench cases: the twin with a point halfway
along the axis (six instances), and the refusal with the axis 30 mm away.

### `taglia_membri` never cut anything

It passed the bodies as `object[]`. SOLIDWORKS answered with a feature named
«Accorcia/Estendi1» that **never entered the tree**; the T stayed identical and
the operation reported `corpi_tagliati: 1`. Measured on a T of two 80x80x5
tubes: `object[]` gives the phantom, `Body2[]` and `DispatchWrapper[]` give the
real trim, −58798.23 mm³ = 40 mm × 1469.956 mm² of tube section, to the
hundredth. `distinta_taglio` already passed `Body2[]`, which is why it worked.

A guard now requires the returned feature to be in the tree and the volume to
change. It is not only for the fixed bug: with typed arrays, bars that do not
meet **still** produce the phantom, and the operation now says so instead of
confirming. The answer carries `variazione_volume_mm3`.

### `cordone_saldatura` never created a bead, and now takes two faces

It passed every face of two sets of bodies to `InsertCosmeticWeldBead2`, which
returned null in every combination tried: faces as `object[]`, `Face2[]` or
`DispatchWrapper[]`, all faces or only the two that touch, geometry mode or
path mode on the edges. The bead is created only by the old selection-based
`InsertCosmeticWeldBead`, and only with **one face per side** (three against
one: null). And SOLIDWORKS does not check contact: between two faces 80 mm
apart it creates a bead anyway, with the size read back as 40 instead of 4.

So the schema changed: `da_faccia_uid` and `a_faccia_uid` instead of the body
sets (the old `tipo` field, never read by the code, is gone too). Before the
call the operation refuses faces of the same body, parallel faces (a bar's end
resting on another is contact, but not a fillet weld) and faces that do not
touch: an edge of one must lie on the other at three points. After the call it
re-reads the bead size from the feature. The length returned is the shared
edge, measured on the geometry: 68 mm on the T, the flat side of the tube.

### `fazzoletto` leaves the catalogue

`Provata = false`: no gusset was created in sixteen variants over the three
gusset calls — selection with and without marks, both orders, inner, outer and
both-sides thickness, triangular and polygonal profile, typed face arrays, on
the T and in a mitred frame corner. Entry GP20 of the TODO says where to start.

### Bench: bodies by name

Three structural members created in the same order came back as C1 = the short
bar in one run and C1 = the long bar in the next. A case that writes `C1` means
a different body depending on the run. The bench has a new selector,
`@corpo(nome=...)`: the body name («... 80X80X5(2)») follows creation order.
The parentheses in the name are handled (the selector opens at the first and
closes at the last). Covered in the bench self-test and in the contract suite.

### Also

- Bench cases for the last orphans of Z-C6: the trim with its refusal, the
  bead with two refusals, the end cap (+42250 mm³ exactly: two 65×65×5 plates
  inside the tube), and the gear at the undercut boundary — 17 teeth is made
  (2/sin²20° = 17.097, rounded down), 16 is refused.
- The `ruota_dentata` description declares that limit, and no longer says the
  server computes the profile (the library does).
- Two probes (`NuovaParte`, `CorpoRotto`) had not compiled since 0.86.0 —
  `Environment` ambiguous between System and the SOLIDWORKS interop — and
  nobody noticed for three versions because the probe build is run by hand.
  `build.ps1` now compiles every probe (13 seconds), and `FlangiaSpigolo` is
  registered.
- The README check read numbers written in words, but «settantatré» (with the
  accent, as it is spelled) came back as "not a number" and was skipped
  silently. Fixed, with a check that the reader recognises it.
- **CI had been red for five pushes** (0.85.0 to 0.88.0), always on the same
  eight checks, none of them a real defect: four looked for the served
  catalogue only in `bin\` (absent in a clean clone; the committed copy in
  `Strumenti\` is the same file), three needed the bridge executable and
  reported red instead of "not runnable here" where SOLIDWORKS is absent,
  and one counted a sentence in the bench README that *tells* the story of
  the old `casi/forma/ 5` row, a folder that is not in the repository by
  design. All eight now read the committed catalogue, use the existing
  "not here" rule, or anchor the row pattern at the start of the line.
- A contract ratchet for entry Z-C16: declared cases that cite a body by
  number in a `_uid` field cannot grow (12 today, seen red at zero first;
  the two refusals on a `C9` that exists in no order are excluded).

Bench: 1016 checks, 0 failed, 795/795 expectations, first run on this
library. Contracts: 370. Installer tests: 17/17.

---

## 0.88.0 — 25 September 2026 · library 0.113.0

The six probes committed with 0.87.0 ran on a free SOLIDWORKS the same night,
and all six turned into code in this version. 76 runnable operations, 74 in
the served catalogue (three new: `flangia_base`, `sviluppo_dxf`,
`schema_di_quote`); two new families, `lamiera` and `quote`.

### `modifica_quota` refuses an opening that breaks out of the material (entry E18, part c)

Regression case r16: a centred 200 mm plate shortened to 150 shrinks **from one
side only**, and a 120 mm slot in the middle comes out through the flank. Clean
rebuild, one body, no error — and a closed slot has become an open notch, which
is a different part. `modifica_quota` now counts the closed openings of planar
faces (inner loops) before and after; if they drop, it **writes the old value
back**, rebuilds, re-reads the volume, and refuses with «esce dal materiale».
Two new bench cases: the refusal, which also requires the volume to be exactly
what it was, and its twin at 180 mm, where the slot stays inside.

### The busy-SOLIDWORKS guard could never fire (entry Z-C3)

The executor refused a plan when `ISldWorks.CommandInProgress` was true. With a
real modal open — a SOLIDWORKS message box, opened from another thread and
closed by the probe — `CommandInProgress` stayed **false** in three rounds out of
three. The main window, instead, is disabled, and `IsWindowEnabled` says so six
times out of six in zero milliseconds, without going through COM. The guard now
checks that; a new check in the bench's `porta-del-cliente` group opens a real
message box, asks the bridge for a plan, requires the refusal and an unchanged
part, closes the box, and requires the same bridge to work again.

### Two catalogue descriptions that claimed to be the only one (entry Z-C15)

A new contract check fails when a description says "the only way to", "the only
operation" and its family has other operations. It went red on two before the
fix: `quote_dal_modello` ("the only way to dimension", while `quota_in_tavola`
dimensions) and `estrudi_su_faccia` ("the only operation that adds", while
`incidi_testo` embosses and `combina_corpi` unites) — the second found by the
check, not looked for. Both now say what is true; `quote_dal_modello` says it
imports nothing out of process on this installation (measured 18/9) and points
to `quota_in_tavola`.

### `documento` can close (entry Z-B1)

`azione: "chiudi"` closes an open document by path or title, and **refuses one
with unsaved changes** — there is no discard option on purpose. `CloseDoc`
returns void, so the bridge reads back that the document is gone. Two bench
checks: the bench's own unsaved part is refused and stays open; a freshly
opened file closes.

### Not installed is not "not open" (entry H16)

One `catch (COMException)` gave the same sentence for both. Now
`MK_E_UNAVAILABLE` says «non risulta aperto» (unchanged, word for word: the
installer looks for it), while `CO_E_CLASSSTRING` and `REGDB_E_CLASSNOTREG` say
SOLIDWORKS is not installed or not registered — and **must not** contain
«non risulta aperto», or an install on a machine without SOLIDWORKS would exit 0.
Six contract checks, by HRESULT, without SOLIDWORKS.

### File properties show the version (entry H14)

Right-click → Properties → Details on the bridge and on `Installa.exe` said
0.0.0.0. An MSBuild target now writes the version from `VERSION.txt` before
compiling, for every way of building the project; a contract check reads it
back from the file.

### Four installer defects (entry H15)

- The uninstaller removed an `Oce.Solid.Agent.pdb` that no version ever
  installed (checked in git history). Install and uninstall now share one list.
- A client config locked by another program was reported as "not valid JSON",
  and a failed write left a `.parziale` next to the real file. The lock is now
  detected before anything is touched, and the temporary file never stays.
- Silent mode now warns when the client is open, like the window did.
- Backups are pruned to the last three per file; reinstalling says
  «aggiorno dalla X alla Y» or «reinstallo la X», reading the version embedded in the old
  executable from its bytes (loading the file would lock it).

`Controlli/prova-installatore.js`: 17 checks, 0 failed, including half of the
"client open" scenario (a child process holding the file exclusively).

### The bench says two numbers (entry F15)

It printed «catalogo: 73» while the served catalogue has 71; the difference
was once explained, plausibly and wrongly, as the two library copies diverging.
It now prints `eseguibili: 73 operazioni (a catalogo: 71)`, and the report
carries both (`eseguibili`, `a_catalogo`; `catalogo` kept for readers).

### Operations that no bench run touched (entry Z-C6)

New declared cases for `estrudi_profilo` (a tube must have its hole: 86 393.8
mm³, not 282 743), `estrudi_sezione` (a slot's length is its overall length),
and `imposta_foglio` + `vista_modello` + `esporta_pdf` in one drawing (scale read
back from the sheet, one view counted two ways, pages counted in the written
PDF). New family folder `casi/estrusioni/`, registered — a contract check had
flagged it as mute before the bench ran.

### Sheet metal, from a measurement (entry GP18)

The probe measured the flat pattern of an L-flange to the hundredth — 80.712
mm, the hand calculation with the sketch on the inner faces — and found three
traps: `SetBendState` returns 0 and `GetBendState` reports flattened while the
body stays folded (flattening means unsuppressing the flat-pattern feature);
and `ExportFlatPatternView` on a never-saved part opens a Save As dialog and
the COM call never returns. Two operations are built on that:

- `flangia_base`: an open profile on a base plane, thickness, bend radius,
  depth. The answer carries T, R and K **read back** from the part, the flat
  length measured by SOLIDWORKS next to the one computed by hand in both
  conventions, and **which convention matched** (`i_punti_sono_le_facce`):
  the side the thickness goes to follows the direction of the points, and the
  two differ by twice the thickness per bend. A straight run shorter than the
  bends that eat it is refused with the number.
- `sviluppo_dxf`: refuses a part without a file before calling, writes the DXF,
  **reads it back**, and refuses if its long side is not the measured flat
  length.

Four bench cases, among them the same L given in reverse order, which must say
"esterne" and 76.712.

Two defects of the first draft, both found before they could ship. The second
bench run went red on all four cases with "«punti» wants at least two points":
arguments arrive as the caller's serializer lists, and an `as object[]` cast
gave null on a correct L — the library already had the rule (`HaElenco` reads
any `IEnumerable`). And a direct harness run before the third bench found that
the DXF header lies: `$EXTMIN/$EXTMAX` said 279.4 mm — eleven inches, the DXF
template's sheet — for an 80.712 mm flat pattern. The extents are now computed
from the entities, and come out [40, 80.712].

### Dimensions on the model from DimXpert (entry Z-H3)

`schema_di_quote`: three planar datum faces by uid (primary, secondary,
tertiary), selected with marks 1/2/4, and DimXpert's auto-dimension scheme
from the core interop — no add-in. The answer lists the values the new
dimensions carry; `AutoDimensionScheme`'s own answer is reported under its
name, because it is false even when it dimensioned. Bench: a plate with a hole
must come out with exactly 10, 20, 30, 50, 60, 100; two datums are refused.

### The PDF is read back (entries GT3-bis, Z-C9)

`esporta_pdf` takes `cerca`, a list of strings the PDF must contain, and says
which are there (`trovati`) and which are not (`non_trovati`). SOLIDWORKS
writes drawing text as glyph indices behind `/ToUnicode` maps; a search in the
bytes finds nothing. `TestoDelPdf` inflates the streams and decodes the maps —
checked on a real export against `pdftotext`: same text, and a string that is
not on the drawing is never found. The bench case writes a note and requires
it found and an absent string not found, in the same run.

### Dimensions left hanging (entry Z-A2)

`IsDangling` stays false on a drawing dimension whose edge was suppressed. The
drawing state now counts, per view and in total, dimensions attached only to
edges or vertices the view no longer shows (`quote_orfane`,
`quote_orfane_totali`, and `attenzione_quote` when there are any); null means
"cannot tell", not zero. A bench group builds the probe's part, dimensions the
block with `quota_in_tavola` (0 hanging), suppresses the block and requires
exactly 1 plus the warning.

### A slot is a polygon, and now the catalogue says so

The first bench run of this version went red on a new case: a 60 × 20 slot from
`estrudi_sezione` measured 11163.281 mm³ against 11141.59 computed by hand.
The part was right: the round ends are 24-sided polygons on the circumscribed
circle, on purpose, so the slot is never narrower than asked (it comes out
20.043 wide). The expected value was wrong, and the description did not say
it; now it does.

That run died at 535 checks for a reason outside the code: the bench's own
`Tee-Object` to `Banco\ultimo-giro.txt` on the network drive failed with a
semaphore timeout. It was also the run that showed a missing
`perche_questa_tolleranza` in a new case as a bench error — after an hour. A
contract check now catches that in a tenth of a second, before the bench.

The third run found one more bench error of the same kind: the refusal
"sviluppo_dxf on a never-saved part" and its twin called the operation with
identical arguments («gemella finta»: the refusal depends on the save state,
not on an argument). The pair now differs by `percorso`, with a note saying
that is not what decides; and a second contract check compares every refusal
with its twin before the bench — seen red on the copy, then refined when it
flagged a case that is legitimately its own twin. 365 contract checks.

---

## 0.87.0 — 24 September 2026 · library unchanged (0.112.0)

### A seventh tool: the document itself (entries Z-B1, Z-B2, Z-B5, Z-B6)

With no document open the bridge answered "there is no open document" and
stopped there: starting a part from scratch needed a person to click File >
New. The acceptance test of the whole project — a model drawing a real part
from a workshop request — could not even begin on its own.

`documento` creates an empty part or assembly from the default template,
opens a SOLIDWORKS file from disk, lists the open documents and which one is in
front, and brings one to the front. It is a **tool of the bridge**, not an
operation of the catalogue, on purpose: catalogue operations are steps of a
plan that works *on* one document, held from start to finish and wrapped in one
undo record. Changing the document is not such a step — and what was looked at
lives in the bridge. Every time `documento` changes the document in front, the
bridge drops what had been looked at and its signatures together (the rule of
entry Z-A1), and `disegna` answers "I have not looked at any part yet".

The traps written in the entries are handled where they bite: a wrong template
does not fail, it creates a document of the wrong **type**, so the type is read
back after creation; `OpenDoc6` succeeding does not mean the requested file was
opened, so the path is read back; `ActivateDoc3` succeeding does not mean it is
in front, so the active document is read back; and a file opened read-only —
usually a colleague has it open on a network share — is opened by SOLIDWORKS
**without asking**, so the bridge always says so.

Seen through the client's door, with a real SOLIDWORKS: ten new checks in the
bench's `porta-del-cliente` group, all green, the bench re-reading from COM
what is actually in front instead of trusting the answer. Two are red by
construction and fire: right after `nuovo`, `disegna` refuses until the part
has been looked at; `apri` on a file that does not exist refuses, and the
document in front does not change. The read-only case uses the file attribute:
the colleague-on-the-share case cannot be built inside one SOLIDWORKS session.

The first draft of that bench case saved the new part through
`GetOpenDocumentByName(title)`, which returns null for a document that was
never saved: the save was never attempted, and it reported `e=0 w=0`. It now
keeps the reference it got when the part was created.

`tools/list` now returns **seven** tools, asserted exactly; `documento`
declares itself neither read-only nor idempotent (`nuovo` twice makes two
documents) and not destructive (it closes nothing). 353 contract checks.

Bench on this version: 909 checks, 0 failed, 715 of 715 expectations verified.

### Six probes written while the bench held SOLIDWORKS — not run yet

Each asks one question and carries its red side in the same run; each is built
by `Controlli/costruisci-controlli.ps1`. They run on a free SOLIDWORKS, after
this commit, and their answers decide what the next library version contains.

- `Controlli/Lamiera` (GP18) — does the flat pattern of an L-shaped base flange
  match the length computed by hand from T, R and K read back from the part,
  within 0.5 mm; is the volume conserved; does the DXF come out?
- `Controlli/DimXpert` (Z-H3) — reached from the core interop, without any
  add-in, does an auto-dimension scheme add annotations and recognise the hole?
- `Controlli/Orfana` (Z-A2) — does `IAnnotation.IsDangling` turn true when the
  edge a drawing dimension sits on is suppressed in the part?
- `Controlli/TestoNelPdf` (GT3-bis) — does drawing text, typed and linked to a
  custom property, come out of the PDF as text: raw bytes, inflated streams,
  and `pdftotext`, with a string that is not there as the red?
- `Controlli/Modale` (Z-C3) — with a real modal open, do `CommandInProgress`
  (the guard in use today) and `IsWindowEnabled` of the main window tell the
  truth, and does the COM read come back at all?
- `Controlli/SeguiIlBordo` (E18 a, b) — does a hole dimensioned to two model
  edges follow the corner, and does a point at the middle of a construction
  line between two edge midpoints stay centred, when the plate grows?

---

## 0.86.1 — 24 September 2026 · library unchanged (0.112.0)

### Minimum wall thickness, measured end to end: 2.00 mm against 2.00 by hand (entry Z-H1)

Same method as the cantilever for Simulation: a part whose answer is known
without a computer. A plate 100 × 60 × 2 mm with, on half its length, a block
10 mm high — two extrusions from the top plane, no face to pick. Minimum wall:
2.0 mm. `Controlli/Spessore/`, run on a free SOLIDWORKS after the bench.

Utilities was not loaded; `LoadAddIn` on `sldUtils\SwLoaderSw.dll` loaded it
for the session (answer 0), with the owner's go-ahead. Then
`IUtilities.GetToolInterface(7)` → `IThicknessAnalysis`:

```
  threshold   answer   minimum   maximum   critical faces
  0.003 m        0     0.0020    0.1000         2
  0.012 m        0     0.0020    0.1000         3
  0.0015 m   12009       -         -            -
```

Five things reflection could not have said. **The API works in metres**, in
and out. **A threshold below the minimum does not give "zero critical faces"**:
the analysis does not run and answers 12009,
`gtWarningTckThinAnalRangeBelowMaxThick` — a warning meaning "nothing below this
threshold". The probe's first draft had written its criterion as "zero faces"
and went red, which was the right thing to do: it stopped instead of closing on
a guess. **Right on the minimum** (2.00, 2.01 mm) it answers 15. **An absurd
threshold poisons the next call**: after a 3-metre threshold, the next call at
3 mm answered 15 instead of 0. **Critical faces grow with the threshold**, which
the probe keeps as a monotonicity check. A thirteen-threshold sweep is behind
`Spessore.exe --esplora`.

What remains is the operation itself, in the library, with its bench case on
this very part.

---

## 0.86.0 — 24 September 2026 · library 0.112.0

### The library number moves, because its bytes did

The fallback removal below changed `OperazioniProfili.cs` — behaviour, not
just comments — while `AssemblyVersion` still said 0.111.0. A green bench on
those bytes would have described a different library under the same number,
which is exactly entry K14. It went to **0.112.0**, and the bench ran again.
Nothing caught it: the critic of the RAG re-read did. The note for 0.111.0 was
missing from `AssemblyInfo.cs` too, and is now there.

### The module probe could not tell a working module from a missing one (entry K20)

`Controlli/moduli-di-solidworks.ps1` decided "installed but cannot be created"
by counting the registered coclasses of a module's interop and by listing
`HKLM\SOFTWARE\SolidWorks\AddIns`. On the day Simulation ran end to end (S3),
the same criterion said about Simulation exactly what it said about Utilities
and DimXpert: 80 coclasses, 0 registered, and absent from every add-in key it
read. Four TODO entries rested on that line. A check that gives the same
answer for a module that works and one that does not measures nothing, and
nobody had ever seen it tell the truth about a known case.

It now resolves each module the way Windows does — ProgID → CLSID →
`InprocServer32` → file — looking in `HKCU\Software\Classes` **before** HKLM,
reading `CodeBase` for .NET add-ins, and resolving paths relative to the
SOLIDWORKS root. It carries **two positive controls**: Simulation (a Dassault
module listed under no add-in key) and the OpenPDM add-in (.NET, registered
under HKCU). If either fails to resolve, the script declares its own criterion
blind and exits 1. Seen firing: a copy with the Simulation ProgID misspelled
exits 1; the real one exits 0.

The first draft of the probe got wrong exactly what the positive control is
for: it reported "file missing" for the OpenPDM add-in, which is installed and
loads at startup, because it read HKLM first and took `mscoree.dll` for the
file. Without the positive control that line would have gone into the TODO as
a measurement.

What it then said changes a decision: **SOLIDWORKS Utilities is registered and
reachable** — `Utilities.UtilitiesApp` resolves to `sldUtils\SwLoaderSw.dll`,
with the thickness checker `ThickChkswu.dll` beside it. The conclusion of 22
September ("no tick box, needs the Installation Manager") is refuted at the
registry level. Whether it actually loads is still for SOLIDWORKS to say.

### A successful install reported as unconfirmed, whenever SOLIDWORKS was empty (entry H6-bis)

The dry run (`--prova`) asked for the state of the open document, and with
SOLIDWORKS running and nothing open the state is null: it exited 1, and the
installer said "installed, but the dry run did not confirm" — on the most
ordinary machine there is, one where SOLIDWORKS has just been started. Found on
14 September, left without its own entry until the 24th, and seen again that
evening by accident: the bench had just closed every document and case 2 of
`Controlli/prova-installatore.js` went red. In the morning it had been green
only because a part happened to be open.

The dry run now has three outcomes, not two: attached with a document (it
measures it), attached with none (it says so and exits 0), not attached (exits
1). Red before, green after, with SOLIDWORKS open and empty: 11 of 11.

### The lock between two bridges, seen firing without SOLIDWORKS (entry W2.9)

The per-document lock (`Ponte/TurnoSulDocumento.cs`) had been "written, to be
seen firing" since 16 September, on the premise that seeing it needed two
bridges on a real document. For the lock itself it did not: it is a named mutex
in `Local\`, and any process in the session can hold it. A new contract group
has a child PowerShell hold the mutex and checks four things: free, it is
taken; held by another process, `Prendi` returns null — the bridge's refusal;
the holder killed without releasing, it is taken again (abandoned means free);
a different document does not wait. Four green, 350 checks in the suite.

What it does not prove is that `Sessione.cs` wraps the plan with the lock on
the *right* document; that half still wants two bridges and SOLIDWORKS.

### A release rite, since no runner can build the bridge

No GitHub-hosted runner has the SOLIDWORKS interop, so none can compile the
bridge; the owner chose a manual rite over a self-hosted runner. `RELEASE.md`
describes it, and `Controlli/prepara-release.ps1` checks every step that can
be checked — version and CHANGELOG entry, clean tree, `HEAD` pushed, tag not
yet taken, build, the SHA-256 **recomputed** rather than trusted, the contract
suite green (which includes bench freshness) — and then prints the three
publishing commands without running them. Publishing leaves this machine, and
a person decides it.

### The counter that was meant to stop inflation was inflating

`Controlli/conta-todo.js` excluded "traces kept for reference" — the original
text of an entry that has since been rewritten — only among open entries, and
found one. There were 54, and 49 of them sat among the half-done ones, where
the filter did not look: the diary claimed 87 entries half done when 36 were.
The rule was right and applied to one case in fifty. The script now **exits 1**
when a trace carries a status box, or when a boxed entry sits outside the
heading level every other tool reads (one 30-50-hour entry, a prerequisite of
publication, was invisible that way). Seen firing on the file as it was, silent
on the file as it is.

### The working diary leaves the repository, and the probes that read it survive

`TODO.md` — 17.117 lines, 1 MB — is now in `note-interne/`, which `.gitignore`
excludes. It names the author nineteen times, the workstations, the decisions
not yet taken, and it judges someone else's project by name. None of that
teaches a reader anything about installing, using or contributing here.

**Five probes exist only to check it, and three of them run in the pre-push
hook.** Removing the file without touching them would have killed them with
`ENOENT` on every machine that is not this one — which is to say it would have
broken `git push` for anyone who cloned, over a file they do not have and
should not have. So `Controlli/dov-e-il-todo.js` looks in `note-interne/`, then
in the root, then at `TODO_MD`, and when it finds nothing it **says it checked
nothing and exits 0**. Same shape in `firme-del-todo.ps1` and in the contract
suite, where the check used to be a red.

*A test that fails for everyone except the person who wrote it does not measure
the code. It measures who is running it.*

### The name pattern was case-sensitive, and the capital letter walked past it

The username check ran `-CaseSensitive` against the lowercase spelling only. The
capitalised form was sitting in a comment in `Banco/ProveSenzaIlPezzo.cs:185`,
and the check said "ok". It is now case-insensitive — no Italian word contains
that string, so the change costs not one false positive — and it fired on 24
points across four files the moment it was made.

*And it fired a third time on this very entry:* the first draft of the
paragraph you are reading spelled the name out twice, to explain the bug. The
check refused the commit. **The rule about writing an incident by its shape and
not by its value applies to the sentence that explains the rule.**

`LICENSE` and `NOTICE` are exempt **from this pattern only**: there the name is
the copyright line and the attribution that Apache-2.0 requires. Two texts that
do not grow, three fixed lines.

### An exemption that named three files was the widest one in the file

`$possonoNominarli` listed `controlla-catalogo.ps1`,
`controlla-pubblicabilita.ps1` and `bonifica-storia.js`, and it was applied
inside `Righe()` — so it exempted those three from **every** pattern at once. It
looked narrow. It was not: it never said *what* it exempted from.

**The bill arrived inside the file itself.** While repairing the TODO exemption
in 0.85.0 I wrote the two real addresses into the comment that explains it —
inside the file that exists to forbid them, which exempted itself. They sat
there for three hours. No probe found them; a census done by hand did.

Every pattern now carries its own list, and every name on a list has its reason
written beside it. The internal-address pattern carries **no list at all**: the
regex `192\.168\.\d` does not match its own text, so nobody needs a pass, and
anyone who did would be writing a real address.

### A new guard: the inside of somebody else's repository

There is not one Python file in this repository (`git ls-files '*.py'`: empty).
So a `.py` with a **folder in front** or a **line number behind** can only be
talking about another project — and three of them were, naming directories,
files and line numbers of a private repository in `Strumenti/aggiorna.md` and in
the CHANGELOG entry that had copied them. A bare filename still passes: that is
where the catalogue came from, and it is context.

Seen firing: it caught the one occurrence I had not yet fixed, then went quiet.

### The census, and three decisions taken on it

A sweep over the 481 tracked files, looking for what identifies the company, the
people, the real parts or the machines. What came out:

- **The real product is no longer named.** The comment that opened
  `OperazioniInterferenze` said what the box is for, by brand. It now says what
  it is — a two-part weatherproof box — which is the part the reasoning actually
  needs. Four bench cases lose the product name from a file path.
- **`Banco/casi/pezzi/` leaves the repository.** Ten plans carrying non-round,
  mutually consistent dimensions — copied from a real drawing, not invented.
  They were plans and not declared cases, so the bench never ran them.
- **The hard-coded network paths are gone.** The weldment library is named by
  `PROFILI_SALDATURA` and the document templates by `MODELLI_SOLIDWORKS`, with
  **no fallback**. The old fallback was one workstation's drive: on every other
  machine it did not exist, so it helped nobody, and meanwhile it published how
  that drive is laid out.

  Removing a fallback without adding the sentence that explains what is missing
  is not a repair, it is moving the damage — so the bench now stops at line one
  with exit 2 and says which variable to set. Verified both ways: exit 2 with
  the message when it is unset, a full run when it is set.

### For the person — or the assistant — who has to install this

- **`INSTALL-FOR-AI.md`**, new. Written for an AI assistant installing on
  someone else's machine: the flags, what each exit code means, what the
  installer changed on disk, how to verify with `--prova` (whose last line is
  `[prova] il ponte sta in piedi.` — quoted in Italian because that is what the
  program prints), and five named traps. Every fact in it was run or read
  against the installer's own code, not summarised from a description.
- **`SECURITY.md`**, new. `CONTRIBUTING.md` promised private vulnerability
  reporting and there was no file where GitHub looks for one. It also says what
  is in scope for something that has no server and no account.
- **`README.md` now links to `QUICKSTART.md`** in its first screen. It did not,
  at all: a stranger landing on the default GitHub page had no way to discover
  that a prebuilt installer exists.
- **`QUICKSTART.md` says where `Installa.exe` comes from.** It used to call it
  "the single file you were given", which is written for someone who already
  knows the maintainer. It now says to check Releases, says plainly that an
  empty Releases page is a gap and not a search failure, and gives the
  `build.ps1` route with the hash check.

---

## 0.85.0 — 24 September 2026 · library 0.111.0

### Simulation, measured end to end: 54.3 MPa against 60.0 by hand (entry S3)

Entry S3 is **closed**. A cantilever 200 × 20 × 10 mm in 1.0037 (S235JR), fixed
at one end, 100 N at the tip: by hand σ = M·c / I = **60.0 MPa** exactly. The
full API chain — study, material, restraint, load, mesh, solve, read — was run
from `Controlli/Mensola/Programma.cs`, a standalone probe that does not touch
the bench.

```
  element    sigma max    off 60.0 MPa
  -------    ---------    ------------
   4.0 mm       29.85       -50.2 %
   2.0 mm       35.86       -40.2 %
   1.0 mm       45.44       -24.3 %
   0.6 mm       54.28        -9.5 %
```

**9.5%, inside the 10% the entry required** — and the criterion is met by the
whole column, not by its last row. The error shrinks monotonically as the
element does, which is what mesh convergence looks like. A single run would
have proved nothing in either direction.

Five facts reflection could not have given, each of which cost time:
`DispArray` is an `object[]` of `IFace2`; `AddForce` demands a `RefGeom` while
`AddRestraint` accepts `null`; the force value is set afterwards between
`ForceBeginEdit` and `ForceEndEdit`; the material must be applied **before the
study is created**, or `RunAnalysis` answers `19 = MaterialNotDefined` even
though a read-back confirms the material is on the part; and `CreateMesh(u,0,0)`
answers `5 = SpecifyPositiveValue`, because zero does not mean "default".

One prediction from the reflection half was **wrong**, and is corrected here:
the material does not go on the body through Simulation's `SetLibraryMaterial`.
That call **returns 0 and does nothing**. It goes on the part with
`IPartDoc.SetMaterialPropertyName2` and reads back with
`GetMaterialPropertyName2` — the getter the census reported missing existed,
on a different interface.

And one reading error worth recording: `GetMinMaxStress` returns
`[minNode, minValue, maxNode, maxValue]`, and the first version took the
maximum of all four. It read node number `11875` and printed it as MPa — off by
19,691%. **It was lucky that it was absurd.** Had the peak node been numbered
58, the probe would have reported "58 against 60, off by 3%" and declared the
chain sound. A wrong number that is plausible is indistinguishable from a
measurement.

### The exemption named a file when it should have described a shape

`controlla-pubblicabilita.ps1` exempted `TODO.md` from the `192\.168\.` pattern,
because entry A1 lists what must not ship and has to write it down to list it.
The comment said the exemption covered only the patterns that entry discusses;
the code exempted **the whole file**, and `Guarda` matches on filename, not on
line. Between one revision and the next, TODO.md grew by sixteen thousand lines,
and three real addresses went in with it — one server, and a network share named
twice. The check ran, reported "ok, no internal address", and was right
according to the rule it had been given.

They were pushed, not published: this repository is still private (checked with
`gh repo view`: visibility PRIVATE), so no stranger has ever been able to read
them. But they sat in a history that would have gone public all at once on the
day the switch is flipped, and that day is the only day this check exists for.

The repair is not a narrower exemption. The pattern is now `192\.168\.\d`: a
real address has a digit after the third dot, a rule being quoted writes the
wildcard. `192.168.*` passes everywhere, a real address is refused everywhere,
TODO.md included, and **no file holds a pass on this pattern any more**. Seen
firing in both directions: three hits before the redaction, silent after.

And then a fourth, on the way here: the first draft of this very entry spelled a
real address out as an example, and the check refused it. *It caught the person
who had just written it, in a file that used to hold a pass.* That is the whole
difference between the two rules, and it cost nothing to see.

*An exemption that names a file widens itself every time the file grows. One
that describes a shape does not.*

### Three files leave the repository, two halves of them stay

`CONFRONTO.md` (a competitive comparison against a named third-party project)
and `PROVE-INSTALLATORE.md` (the results of one test session, stuck at
`Installa.exe` 0.7.1 of 14/9/2026 while VERSION.txt says 0.84.0) are working
notes: they teach a reader nothing about installing, using or contributing.
`Controlli/voce-0.76.0.js` is a single-use scaffold that rewrites a CHANGELOG
entry already written; it was the only one of the 24 scripts in `Controlli/`
with zero references anywhere in the repository.

What was worth keeping did not leave with them. The **(D)/(M) rule** — declared
is not measured, and comparing their (D) with our (M) is not an advantage — is
now a section of `CONTRIBUTING.md`, next to the one that already asks
contributors what they measured. The **six installer scenarios**, and which
three run on any machine, are now the header of
`Controlli/prova-installatore.js`, which is the script that runs them: its
line 11 used to point at the file being removed, and now points at nothing
because it needs to point at nothing.

`.gitignore` gains `note-interne/` (without it the next `git add -A` puts them
straight back, which is how they got in), `Controlli/*.local.js` in place of the
single named file, and `*.bak`.

### A fallback path that only helped the machine it named

`controlla-documenti-senza-file.ps1` fell back to a hard-wired folder of the
maintainer's workstation when `RISCONTRO_ADDIN` was unset — a path into **another**
repository, one that is not publishable. On every other machine the path did not
exist and the check skipped that half while saying so, which means the fallback
helped nobody and published where things are kept. Removed. The guard is now
`-not $addin -or -not (Test-Path $addin)`, in that order: with the fallback gone
the variable can be empty, and `Test-Path $null` in PowerShell 5.1 does not
answer `$false` — it raises a non-terminating error and carries on into the
wrong branch.

---

## 0.84.0 — 24 September 2026 · library 0.111.0

### Two fields stop being asked for, and start saying who chose (entry Z-G3)

**A contract change**, which in this repository is delivered with a green bench
run beside it — and at 02:15 there was one: 880 checks, 0 failures, against
library 0.110.0. That is what unblocked this entry, and nothing else did.

`applica_materiale.materiale` now defaults to **`S235JR`** and
`combina_corpi.operazione` to **`unisci`**. Both leave `required`, both gain a
`default` in the schema and a sentence in the description.

#### The sentinel promised exit 4 and delivered a stack trace (entry K17)

K17 says the bench *"stops **saying so** and exits 4"* when SOLIDWORKS dies
mid-run. On one path it did not: it **died**.

```
«Eccezione non gestita:» Condiviso.PreparazioneFallita: «SOLIDWORKS NON»
  «PRODUCE PIU' DOCUMENTI: 3 tentativi di fila andati a vuoto...»
   in ProvaSaldatura.Program.ProvaFotografia()
   in ProvaSaldatura.Program.Gruppo.Corri()
   in ProvaSaldatura.Program.Main(String[])

exit 0xE0434352
```

The group loop was `try { gruppo.Corri(); } finally { ... }` — **a
`try/finally` with no `catch`**. Most groups catch `PreparazioneFallita`
per check and mark it red; a group that throws while **preparing** —
`VistaDelPezzo`, which builds its part before measuring — went straight
through, and `Main` with it. The block that produces the 4 sits *after* the
group loop, so it was never reached.

The cost is the exact opposite of what the entry promised: instead of *"I am
stopping, here is why, exit 4"* — a stack trace, a dead process, **no report
written to disk** (so no evidence of what happened), and an exit code that
means nothing to anybody. In precisely the situation the guard exists for.

Found by running `--fingi-senza-solidworks`: **exercising the guard** rather
than trusting that it had worked once. The entry already said "seen firing" —
and that was true, on a different path. *A guard never seen firing all the way
through is half proven, and the missing half is always the one that matters.*

Fixed and seen in both directions; the groups that never ran are now named
one by one, because "14 skipped" without saying which is the same defect K2
had already corrected elsewhere.

#### A default is not one field fewer: it is a choice somebody else made

The entry exists because *"nobody thought about it"* and *"somebody thought
about it and decided to ask"* read identically in JSON, and only one of them
can be improved. **The same holds in reverse, and that is the dangerous
direction:** if the bridge applies S235JR and says nothing, the model believes
it chose it, next time it will not even ask, and then a part that needed
stainless ships with nobody able to say where the decision was made.

So every answer now carries `misure.nota_assunto` (the two lines are translated
from the Italian):

```
material: «S235JR» — DEFAULT of this shop, you did not ask for it.
material: «AISI 304» — you asked for it.
```

**Written always**, including when the value was asked for. A field that only
appears in the odd case is a field readers never learn to look at.

#### The guard would have gone red by finishing the work

`ProveICampiObbligatori` demanded `candidati > 0`. It counted **promised**
work, and that was right while there was some. With both candidates done the
counter goes to zero — *a check that demands the existence of work to do turns
red exactly when the work finishes.* It now reports instead of demanding, and
in its place is a check on **delivered** work: the `default` must be in the
**served** catalogue, with the right value, and the field must be out of
`required` — all three together, because a field removed from `required`
*without* a default is worse than before: the model does not know it may omit
it, and if it does it cannot know what it got.

The neighbouring guard — *"a decision about a field that no longer exists is
noise"* — fired by itself on the two now-obsolete entries the moment the
catalogue was regenerated. Nobody had to remember it.

#### Two mistakes in the bench cases, both caught by reading the operation

The four cases (in pairs: default and twin) were written the night before,
against the **schema**. Both errors came from not reading the **description**.

- The two bodies for `combina_corpi` were placed **120 mm apart**, to make the
  three outcomes unmistakable. But the operation declares it *fails* when after
  a union the bodies are still two: *"usually they do not really touch, and I
  fail rather than confirm a part different from the one you think you have"*.
  The union would have failed and **the red case would have accused the default
  instead of the geometry** — sending someone to hunt in the wrong place. They
  overlap by 40 mm now: 80000 / 30000 / 20000, still three distinct numbers.
- `sposta_corpi` was called without `corpi_uid`. Its schema says
  `required: []` — correctly, because it is an either/or between `corpi_uid`
  and `corpi_rif` that a flat `required` cannot express — and its description
  says so in plain words. Caught by the bench going red.

*A schema says what an operation accepts; the description says what it demands
and what it refuses.* Writing a case against only the first is how you produce
a red that blames the wrong thing.

---

## 0.83.0 — 24 September 2026 · library unchanged (0.110.0)

### The elapsed time next to the step, and Section S is unblocked (entry S0)

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

#### Reading the criterion again, after building, found the gap

Entry S0 asks for *"a trace readable from outside **with the time next to
it** — and the caller can tell 'working' from 'stuck' without watching the
CPU"*. Yesterday's progress notifications carried the step number.

**The step number alone does not say it.** "Step 3 of 5" is identical
whether that step started two seconds ago or eleven minutes ago — and the
whole point of the criterion is exactly that difference. *The criterion asked
for two things and I had heard one.*

Every notification now carries the elapsed time written the way a person
reads it (`eseguo il passo 3 di 5: guscio — 2m14s`), not raw seconds: the
`message` field is the only one a human reads, and putting seconds there
would make them do the division.

The clock is **passed** to the `Filo` rather than read inside it — same
choice as `Guinzaglio` and `SentinellaSolidWorks`. Seven checks freeze it and
compare the exact string, including the case that matters: eleven minutes on
one step.

#### S0's "to be decided" is decided, and by neither of its two options

The entry weighed *"start and return an id immediately"* (needs an extra
"where are you up to" tool) against *"block with a declared ceiling"* (which
lies to whoever exceeds it). **The third road was already in the MCP spec.**
The bridge stays synchronous — which SOLIDWORKS imposes, it is not a choice —
and talks while it works. No extra tool, and a generic client reads it
knowing nothing about this bridge.

S0 and S1 are no longer blocked.

#### Two probes that were lying, both found by using them

`conta-todo.js --blocchi` listed **Y2**, **W2.6** and **Z-B7** under "CAN BE
DONE NOW". None of them could: Y2 declares *"depends on Y1"*, Z-B7 *"depends
entirely on B1 and B3"*, and W2.6's criterion is a measured volume. Two new
rules — and the dependency one is not a regex, it is a lookup: the entry
names its own blocker, and the probe checks how that entry is doing.

It went wrong immediately, and instructively. The TODO keeps, beside a closed
entry, its *"original trace kept for reference"* — **same code**, and at `~`
forever because it is a memory, not work. There are dozens. The map took the
last one written, so `B1` read as open although the real B1 closed on 14/9,
and Z-B7 was declared blocked by something finished ten days earlier. *A
warning light that accuses a finished thing sends you looking in the wrong
place, which is worse than staying quiet.* With traces skipped, Y2 and S1
stay blocked and Z-B7 comes back out — the rule exercised in both directions
on real data.

#### The bench cases for Z-G3, written before the thing they measure

Entry Z-G3 closes when two fields have their `default`, the sentence that
declares it, **and a green bench case exercising it**. The default itself
cannot ship — the entry forbids it in writing: *a contract change is delivered
with a green bench run beside it*, and the bench is down. The cases can be
written, and now are: `Banco/casi-in-attesa/`, a folder the bench does not
scan (it reads `Banco/casi/` and nothing else).

A case that must fail, placed in `casi/`, turns the bench red for a known
reason — and *a red for a known reason is a red people learn to skip*, so the
day it goes red for a different reason nobody notices. Same shape as
`Banco/compiti/` (Z-G9): the input is born before the thing that runs it.

They come **in pairs, and that is not symmetry**. Without the twin that names
the value explicitly, the default case proves nothing: a declaration that
appeared *always* would pass it and be noise — and a sentence that always
appears stops being read exactly when it says something.

Writing them turned up three things the plan did not have:

- **The dangerous direction is the other one.** The entry worries the model
  won't know it omitted a field. The real risk is the reverse: if the bridge
  applies S235JR and **says nothing**, the model believes it chose it, next
  time it won't even ask, and then a part that needed stainless goes out with
  nobody able to say where the decision was made.
- **The two bodies must be far apart**, and making them needs `sposta_corpi`
  with `copia` — the catalogue says so itself: *"separate bodies are born from
  `sposta_corpi` and the copy"*. Far apart so the three outcomes are
  unmistakable: `unisci` = 100000, `sottrai` = 50000, `interseca` = 0.
  **`riuscita: true` would give all three.**
- **The first draft invented two arguments** — `corpo_separato`,
  `scostamento_x_mm` — that are not in the catalogue, and the wrong enum value
  (`unione` instead of `unisci`). Caught by checking against the served
  catalogue before saving. *A case with invented names is worse than no case:
  it fails, and sends you hunting for the defect in the wrong place.*

The field the cases watch, `misure.assunto`, **does not exist yet**: it is a
proposal, and the README says so. Whoever implements may name it otherwise —
that is one line in four files. What does not move is the demand: the answer
must distinguish "I chose it" from "the bridge chose it for me".

#### A false positive inside the pre-push hook, found by using it

`righe-del-todo.js` resolved a cited file **by basename** across the known
folders. The repository has two `Sessione.cs` — `Banco/` and `Ponte/` — and
`Banco` comes first in the list. So the citation `Ponte/Sessione.cs:824`
landed on `Banco/Sessione.cs`, which has 392 lines instead of 832, and the
probe declared a correct citation dead. **In the pre-push hook**: it would
have stopped a delivery over its own mistake.

The folder written in a citation is an address, not a hint. It is honoured
now, and where a citation gives no folder and the basename exists in more
than one, the probe **abstains and says so** instead of picking — two
same-named files are exactly the case where guessing produces the wrong red.
It also prints the unresolvable names rather than just counting them: a bare
`7` cannot be checked without redoing the probe's work by hand. The six that
remain are all genuinely external — three files from the add-in repository,
three RAG notes.

Two TODO citations were wrong behind that fog. One said the per-operation
state re-read lives in `Sessione.cs` — it is in
`Riscontro.Disegno/EsecutorePiano.cs:354`, **wrong file before wrong line**;
the entry rests a real warning on it (*"comparing the reading of now with the
reading of now always returns true, and you believe you have a protection you
do not have"*). The other was 23 lines off from `EseguiIlPiano`.

And writing the correction, the dead addresses went back in as citations —
the same mistake made six hours earlier and written down as *"a corpse gets
named, not cited"*. The probe caught it again. A lesson written down is not a
lesson learned; a probe that keeps checking is.

#### Counting the places where zero means "I don't know" (entry Z-C8)

The entry asked that *"every enumeration declares **zero** against **I don't
know**"*. Before declaring anything you have to know how many there are.
`Controlli/zero-o-non-lo-so.js` finds **21**, and exactly **one** already
followed the rule.

The swap runs both ways, and the second way is worse. The one the entry
describes — `GetMates()` returning `null` for zero and the bridge answering
*"couldn't read"* — is the **cautious** direction: it alarms the reader for
nothing. The other is a function that **reads from the CAD and answers `0`
from its `catch`**. Zero is not "I don't know": zero is an **assertion**.
*"This part has zero bodies"*, *"this hole removed zero mm³"*. The reader has
no way to notice the read failed.

And the two opposite rules live in the same file, two thousand lines apart:

```
OperazioniSw.cs:2122  "A volume that could not be read does not become a
                       zero: a zero would say 'it removed nothing', which is
                       an assertion."
OperazioniSw.cs:4837  "The volume of all solid bodies, in mm3. Zero if it
                       cannot be read."
```

The first is the comment of whoever **found** the defect; the second is the
function that **produces** it. The call site defended itself — in twenty lines
— and the root stayed. *A patch on the caller leaves the hole for the next
caller.*

The model to copy already exists, and there is one:
`OperazioniAssieme.ContaGliAccoppiamenti` first checks whether the handle
**still answers** (reads `Name2`), and only then does a `null` from `GetMates`
count as zero; otherwise it returns **`-1`**. It now carries the
`ZERO-E-ZERO:` marker with its reason, and the probe leaves it alone. **The
rule in one line: `-1` (or nullable) for "I don't know", `0` only when zero is
an assertion you can make.**

The probe does not exit 1 and is not in the hook: fixing these changes library
signatures and every caller, and doing it now would stack twenty unverified
changes **on a library the bench has not yet run against** — if the run went
red, nobody could say whose fault it was. It is a work list, not a ban.

It got its own window wrong first, and instructively: it counted fourteen
lines above the site, and the comment carrying the reason is twenty long — the
marker was there, in the right place, and the probe could not see it. **Second
time in two days**: the Z-A1 guard was born with exactly the same defect.
*Counting raw lines punishes whoever explains*, in a repository where
explaining is the rule. It now walks up to the member's `<summary>` instead of
counting, and the self-test puts the marker twenty lines away on purpose.

#### Four "to be ascertained" ascertained, none of which wanted the CAD

Four entries carried an open question — *"unverified"*, *"to be ascertained
which"*. **None of the four needed SOLIDWORKS running**; they needed somebody
to read the interop. Thirteen more expectations in `Controlli/firme-api.ps1`,
which now pins thirty.

- **GT5.** The `TableTemplate` preference index — *"33, per an unverified note
  in 80r"* — **is right**: `swUserPreferenceStringValue_e` has 131 members and
  33 is `swFileLocationsHoleTableTemplates`. The note was true. The rule it
  guards does not change, though: knowing 33 is correct does not say where
  *this* installation keeps its files, and that is the data needed. The
  constant is for **asking** the question, not skipping it.
- **GT2.** Its `Cosa` line still asked *"or `IWeldmentCutListAnnotation`, if it
  exposes a direct insertion — to be verified which"*. The answer had been
  written further down **in the same entry** since 17/9: it is
  `IView.InsertWeldmentTable`, and the annotation interface exposes accessors
  only. *A question left open next to its own answer sends someone to redo the
  work* — and on 24/9 I did redo it. Reflection returned the same answer, which
  is good news about reflection and bad news about that line.
- **GA6.** *"`GetTotalTransform(false)`, not `GetXform`, format unverified"* now
  has a measured reason instead of a suspicion: `GetTotalTransform` returns a
  typed `MathTransform`, `GetXform` returns a bare `Object`. *Between two roads
  to the same place, take the one you can read without guessing.* And
  `GetMates()` returns a bare `Object` too — so reflection gets far enough to
  prove the format **cannot** be known without an open assembly. "To be
  ascertained first" stands, now on a measured limit rather than on inertia.
- **X2.** A reference row said the real helical thread is *"`InsertHelix` +
  `InsertCutSwept5`, a five-step composite with a **void** return and 21
  parameters"*. It said three things and two were wrong. `InsertHelix` **is not
  on `IFeatureManager`** — it is on `IModelDoc2`, 10 arguments, returning
  `Void`; fifth time in a week a member sits on a different interface from the
  one its name suggests. And `InsertCutSwept5` **does not return void**: it
  returns a `Feature`, with 22 arguments. So the blind step is **one**, not the
  whole procedure — the cut can be checked from what it hands back. A road
  nobody had looked at also turned up: `InsertVariablePitchHelix` +
  `AddVariablePitchHelixSegment` + **`EndVariablePitchHelix()`, which returns a
  `Feature`**. Whether a variable-pitch helix with equal segments is the same
  thing as a constant one is **not measured** and needs the CAD — but if it is,
  the last blind step disappears.

#### Simulation: the census S3 asked for (entry S3, first half)

S3 says *"survey first, build second"*. The surveying never needed SOLIDWORKS
**running** — only installed. Seventeen expectations now live in
`Controlli/firme-api.ps1`, which exits 1 if one changes.

The entry's two numbers were right: `cosworks` has **589 types and 160
interfaces** (plus 349 enums). Three findings the plan did not have:

- **`MeshAndRun()` returns `Void`.** It is the most inviting name in the
  interface and the only one of the two that **cannot say whether it worked**.
  `RunAnalysis()` returns an `Int32`. Same rule this project has already paid
  for elsewhere — *the value returned by whoever did the thing is not the
  measure of the thing* — except here there is no value at all.
- **Material sits on the BODY, not the study**, three hops down, and
  `ICWSolidBody` has `SetLibraryMaterial` and **no `GetMaterial`**: a setter
  with no reader. Fourth time a property can be written and not read back.
- **`AddRestraint` and `AddForce` take `Object DispArray` and `Object
  RefGeom`.** The signature does not say what goes inside, and no amount of
  reflection will. *The census did not remove the risk: it put it in one place
  and gave it a name.*

---

## 0.82.0 — 24 September 2026 · library unchanged (0.110.0)

### Progress and cancellation, with the protocol's own mechanisms (entry W2.2)

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

Entry W2.2 was marked **blocking for the whole of Section S**. It is not any
more.

#### Why not a second "where are you up to" tool

Because it already exists in MCP 2025-06-18, and reinventing it would cost
more to be understood by fewer: a generic client can read
`notifications/progress`, and will never read a tool invented here. There
were **zero** occurrences of `progress` or `cancelled` in the repository.

The token is read from `params._meta.progressToken` — next to `name`, not
inside `arguments`: it belongs to the *protocol*, and a generic client puts
it there knowing nothing about this bridge.

Three decisions carry the design:

- **The token is copied, not read.** The spec allows a string or an integer.
  A `42` coming back as `"42"` matches nothing on the client side, *and the
  client has no way to say why*. So it stays an `object` and is never
  inspected. A check demands that an integer comes back an integer.
- **`progress` must increase, and here it cannot fail to.** That rule belongs
  to the spec. Left to callers, it would be discovered broken inside a client
  — which is to say never. It lives in the `Filo`, which keeps the last value
  and **discards** a step that does not grow; a non-growing step is fed to it
  on purpose to watch it discard.
- **The total lives in the `Filo`, not in the callers.** A plan is narrated in
  two halves — validated whole against an untouched document, then executed —
  and the two points sit in **different files**. If each computed the total
  itself, sooner or later they would disagree and the reader's bar would run
  backwards. Execution resumes the count from `Filo.Ultimo` instead of
  recomputing it.

#### The library did not need touching, and that is the instructive part

The progress that matters is the one *during* execution: validating a plan
takes milliseconds, a real step can take minutes. It looked like a callback
would have to be threaded through `EsecutorePiano` — inside the library.

It did not. The executor asks `IFontePiano` for the next step, and the
implementation — `Ponte/PianoInMemoria.cs` — **is in the bridge**. The
notification goes out from `ProssimoAsync`, that is *before* the step starts:
saying "about to do step 3 of 5" is useful, saying it afterwards much less.
*The right seam was already drawn; all that was missing was someone using it.*

#### `notifications/cancelled` no longer falls into silence

The entry was right: it fell into the `case default`, and since it is a
**notification**, the main loop discarded the error response. A silent no-op,
with the client convinced it had cancelled something.

This bridge cannot interrupt a call in flight, and that is not a gap to fix
with more code: it serves one request at a time on one thread because
SOLIDWORKS requires STA, so while it works it does not read stdin — and by
the time the line arrives, the request it names has finished. The spec allows
ignoring a `cancelled`. Here it is not ignored, it is **answered**, with the
request number and the reason. *A stated no can be read; silence cannot.*

#### What is measured, and what is not

Thirty contract checks, six of them end-to-end over stdio against the real
bridge — possible because `prova: true` (entry Z-G6) walks a plan step by
step with SOLIDWORKS closed. Among them the half that is usually forgotten:
**a client that sends no token receives nothing**.

Not measured: the three-minute operation the entry's criterion names. The
wiring is exercised — `PianoInMemoria` is driven directly and what it emits
is inspected — but *driving the class is not the same as watching the
notification arrive during a real extrusion*, and in this repository that
difference is the rule, not a detail. The entry stays at half.

---

## 0.81.0 — 23 September 2026 · library unchanged (0.110.0)

### The bridge starts writing down what it was asked for and did not have

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

Everything below was built and measured **without** the CAD, because none of
it needs one — the run afterwards confirms it did not break what does.

#### What was asked for and wasn't there (entry Z-G7)

The five catalogue operations with no bench case — `ripeti_circolare`,
`cordone_saldatura`, `fazzoletto`, `taglia_membri`, `tappo_estremita` — **we
picked ourselves**. Which ones are actually needed, nobody knows, and asking
people does not find out: whoever uses the bridge has no list of what is
missing.

The bridge had that data in hand four times over and threw it away. The
`default` branch of `Attrezzi.Chiama` *knows* it was asked for a tool that
does not exist, answers well, and writes it down nowhere.

`Strumenti/RegistroDelleDomande.cs` now appends one JSON line per unmet
request, wired into all four sites, and `Controlli/domande-senza-risposta.js`
reads it back. Three decisions matter more than the code:

- **It does not record arguments.** Only the name asked, its kind, and the
  near names the catalogue offered. A ledger that copies call arguments
  becomes, in one afternoon, a file full of network paths and job numbers —
  and this repository already has a personal-data problem that blocks
  publication. *A name a model invented is nobody's data; the path the part
  sits at is.*
- **It can never fail the call it observes.** It does not name `Console`
  anywhere — that is where the JSON-RPC goes, and one extra line there is not
  a dirty log, it is a broken protocol — and it never throws. Installed under
  `Program Files` the folder is not writable, and then it simply goes quiet.
  The reason for the last silence stays in `PercheHaTaciuto`, which nothing
  prints: it exists so a test can tell an empty `catch` from a `catch` that
  never ran.
- **The report is split in two.** A name asked ten times **with a near match**
  is a synonym to add and costs one line. The same name **with nothing near**
  is an operation to build. One combined count would hide the difference.

The guard watches the *fifth* site, not the four. The predictable failure is
not getting one of them wrong — it is adding a fifth and forgetting to
record it. So the test demands that **every** line calling
`_catalogo.Simili(` also writes to the ledger, and is made to fail on
purpose in both directions on fabricated lines.

The entry stays open at half, and deliberately: it did not promise a file,
it promised to know which operation to build next. That number does not
exist yet — the ledger is empty. *What is missing is not work, it is
traffic.*

#### The version the bridge announced had been wrong for 78 minor releases

`Programma.cs` carried `const string Versione = "0.2.0"`. `VERSION.txt` said
`0.80.0`. Every MCP `initialize` reply has been announcing 0.2.0, and anyone
logging the conversation recorded a false number.

Nothing in the code was wrong. There were **two places to write the same
number, and nobody ever re-read one of them.** It is the same rot the TODO
had already cured in its own header on 16/9 — that line said *«Ponte 0.2.0»*
and had been true on the 14th — but in code, where it shows even less.

`VERSION.txt` now travels inside the executable as an embedded resource and
is the single source. If the resource cannot be read the bridge answers
**`sconosciuta`** rather than a fallback number: *a wrong number is worse
than a missing one*, because whoever reads it has no way to know it is wrong.
A contract check pins the announced version to `VERSION.txt`, which is what
stops the two places from becoming two again.

#### And a guard already in place caught the new environment variable

`RISCONTRO_REGISTRO` was in the code and not in the README, and the check
that reads every variable the bridge looks up went red within a minute of
the ledger compiling. Nothing to fix in the guard — it is reported here
because a guard that fires on its author on the day it is needed is the
cheapest evidence that it still works.

---

## 0.80.0 — 23 September 2026 · library unchanged (0.110.0)

### One real defect in the bridge, and two guards that were green for the wrong reason

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

Everything here was found without the CAD — by reflection over the installed
interop assemblies, by reading the TODO against the code, and by asking one
guard what it actually measures.

#### A plan that names another document left the signatures behind (entry Z-A1)

The bridge keeps three things about the part it is looking at: `_guardato`
(*which* document) and `_firmaGuardata` / `_firmeViste` (*what it looked
like*). In `EseguiIlPiano` there was `_guardato = adesso;` **on its own**.

A plan naming a different document — `documento: "..."`, the legitimate,
documented route — was enough: from that moment `_guardato` pointed at the new
part and the signatures at the old one.

**The damage does not land there.** It lands on the *next* call: the document
in front is the new one, so the "same part?" test passes, and the comparison
then runs against another part's signatures. Two outcomes, both wrong — reject
a good plan saying *«the part changed»*, or **let a plan through on uids that
no longer mean anything**. The second is the failure this project has been
chasing for weeks: a plan that succeeds in the wrong place, silently.

The principle was already written two lines above, on the local variable:
*«better no comparison than a comparison between two different parts»*. It was
right, it was there, and it was scoped to the local. **A rule written for half
the code is a rule somebody will apply to half the code.**

How it surfaced is the instructive part: not by reading the code, but by
**reading the TODO**. The entry already quoted the offending line as
background. It had been sitting there for days, described accurately, and
nobody had read it as a defect.

`Prove/ProveLeFirmeDelDocumento.cs` now demands that no `_guardato =` travels
alone. It was born red on a correct piece of code — it counted raw lines, and
the three legitimate assignments sat in one block separated by twenty-five
lines of comment. Counting raw lines there means **punishing whoever explains**,
in a repository where explaining is the rule. It counts code lines now, and
it is exercised in both directions.

#### The guard that asked git when it should have asked the disk

`ProveIlBanco` refuses a delivery where nobody ran the bench. Part of that is:
*is the run newer than the last change to `Riscontro.Disegno`?* It answered by
calling **`git log`** — committed history, and nothing else.

But code does not live in git while you are writing it. It lives in the
working tree, and **that is where `build.ps1` builds the DLL from**. Between
the edit and the commit there was a window where the guard reported *«there is
nothing unproven»* over a tree full of code the bench had never seen.

Measured on 23/9/2026: `Riscontro.Disegno/Catalogo/Operazioni.cs` touched at
19:18, last bench run at 15:23, guard **green** — because the last *commit*
under that folder was from 15/9. Green for the wrong reason, which is how a
guard stops working without anyone noticing. The window was not narrow either:
at that moment the tree held **29 modified files and 31 new ones**.

The verdict now lives in a function that takes two dates and no I/O, so it can
be made to fail on purpose. Four checks drive it: a dirty file newer than the
commit wins; a clean tree changes nothing; a dirty file *older* than the commit
moves nothing; and — the one that matters — **"no unproven commit" no longer
covers a full working tree**. That last case *is* the false green.

It reads the tree with three commands that print bare paths
(`diff --name-only`, the same `--cached`, `ls-files --others`) rather than
`status --porcelain`, because the repository's own `Git()` helper trims every
line and `--porcelain` puts the status in fixed columns that the trim eats.

With the fix in place the guard immediately went red twice. That is correct.

#### A citation that lands in the right method under the wrong name (entry Z-A7)

The entry sent the reader to `` `OperazioniSw.cs:6186` `` for
`GetPersistReference3`. **That method is not in that file at all** — it is in
`StatoDocumento.cs:392`. Line 6186 falls twelve lines short of
`GetObjectByPersistReference3`, which is to say *inside the right method*: a
reader who follows it finds plausible persist-ID code and never notices that
the named API isn't there.

*A citation that sends you out of the file is caught immediately; one that
sends you into the right method under the wrong name is never caught.* The
entry now names both halves of the round trip and where each lives.

#### Internal cross-references rot the same way (entry Z-C14)

Entry Z-C14 pointed at `TODO.md:7497-7530` and `TODO.md:~7753` to talk about
`GetRemainingDOFs`. Both lines exist — the file has sixteen thousand — and both
talk about something else entirely: `IFace2.Normal` and undercuts in the first
case, the origin of a single-direction extrusion in the second. The real
addresses were three hundred and four hundred lines further down.

`Controlli/righe-del-todo.js` could not see this, and says so: its first pass
only checks whether the line **exists**. It has a second pass now, and it does
not guess what an entry is about — it takes **the subject the sentence wrote
for itself**: the backticked identifiers on the citing line. If none of them
appears in the cited range, the reference is broken. Same technique that
resolved `vedi C8` into `Z-C8`.

It fired twice on its author, and both times it was right. First on the
sentence *documenting* the two dead addresses — which had written them in
citation form. But `TODO.md:N` is an imperative, it means *«go here»*, and
using it for an address you are declaring wrong is a contradiction before it
is a false positive. **A corpse gets named, not cited.** Then on the corrected
sentence, which cited two addresses with two different subjects and named
neither — the reader had to infer the topic from context exactly as the probe
did. It names `GetRemainingDOFs` now.

Neither time was the rule narrowed to shut it up.

#### Entry GT2 needed nothing, and that is also a result

Reflection over `IWeldmentCutListAnnotation` re-derived, independently, a fact
the entry had already recorded on 17/9: the insertion is
`IView.InsertWeldmentTable`, the annotation interface is an accessor and not an
inserter, and `swTableAnnotation_WeldmentCutList = 4`. No change. A probe that
confirms what is written is worth as much as one that contradicts it.

---

## 0.79.0 — 23 September 2026 · library 0.110.0

### The other half of GT4, and a bench that kept testing against a dead SOLIDWORKS

**The bench ran green on 24/9/2026 at 02:15**, after the handle on the network
document template was released: **880 checks, 0 failures, 695 of 695
expectations verified**, 67 minutes, against library **0.110.0.0** — the one
this entry ships. The contract suite is **339 checks, 0 red**.

For about eleven hours it had not. SOLIDWORKS was unusable on 23/9/2026: a
handle left on the document template, on a network share, made every new
document hang (entries K17, K19). Everything in this entry was written,
compiled and structurally checked in that window — **and the run afterwards
measured it on a part, which is the only thing that settles it.**

**And the local model copy (K19) was finally seen to *succeed*, not fail:**
`Parte.prtdot` 45955 bytes and `Assieme.asmdot` 25895 bytes copied into
`%TEMP%\banco-modelli`, one network read instead of eight hundred. Until now
that code had only ever been watched failing.

#### `dettaglio_in_tavola`

A section shows what is inside; a detail shows what is too small to read at
the drawing's scale — a groove, a chamfer, a fillet with its dimension.
Without it, on a real shop drawing those get dimensioned by eye.

```
"vista_dettaglio":"«Vista dettagliata B (2 : 1)»", "tipo_vista":3,
"e_un_dettaglio":true, "circonferenze_prima":0, "circonferenze_dopo":1
```

**Three things "by analogy" would have got wrong, and two of them were.**

1. **The crosshatch has nothing to do with it.** On a section the hatch is
   the check that matters. A detail cuts nothing: it shows the same geometry
   enlarged, and `GetFaceHatchCount` on a detail view answers **0**. Copying
   the section's guard would have produced an operation that **always
   refuses** — the exact defect the section had on its first real use, one
   day after it was corrected.
2. **`GetDetailCircleCount2` has two outputs, and the obvious one lies.** The
   count is the **return value**; its `out` goes from **1 to 35** for a
   single circle — it is an array size, not a count. Read as a count it
   would say the view already had one before it had any.
3. **`Showtype` wants `swDetCircleShowType_e`**, not `swDetViewShowType_e`,
   which does not exist.

So the guard is that the circle **became** a detail: the source view gains a
detail circle, 0 → 1. `viste_dopo` goes up either way — one more view on the
sheet does not say it is tied to that circle. Without this check the sheet
can keep **a hand-drawn circle next to an unrelated extra view**, and no
error would say so.

And it refuses a circle larger than the view: that enlarges emptiness, and
**enlarged emptiness is still a valid view** — it counts, it has a label, and
it reaches the shop floor as a frame with nothing in it.

What neither half can tell you is whether that is the feature you needed. A
circle moved ten millimetres enlarges a patch of flat surface, and is just as
valid. It says so, under `cosa_non_guarda`.

#### The bench now notices when SOLIDWORKS is gone (entry K17)

A full run died after 312 green checks: `NewDocument` started answering
`null` and never stopped. The report said

```
NO   il_dettaglio_ingrandisce   «SOLIDWORKS non ha prodotto la parte nuova»
NO   il_loft_non_e_un_cono      «SOLIDWORKS non ha prodotto la parte nuova»
```

on nine cases that were written correctly, and then hung inside the next COM
call for as long as nobody looked.

Two defects, not one. **`NO` was the wrong word** — it means "I measured and
the part came out different", and the fact was "I did not measure"; whoever
reads it goes and inspects the case, which is the wrong place. And **the run
carried on**, forty minutes of testing against a dead program.

Three new-document failures **in a row** now mean the program is gone: the
rest of the run is skipped **saying so**, and the exit code is **4**, which
wins over 2 and 1 — a run that did not happen is not a run with errors. "In a
row" is the word that matters: three scattered failures across eight hundred
checks are normal noise, and a guard that stopped healthy runs would be
switched off by the first person who saw it do that.

`--fingi-senza-solidworks` makes `NewDocument` answer `null` on demand, so the
guard can be **watched firing** on the real bench instead of believed: two
`!!`, then 127 cases skipped, exit 4. Seventeen more checks run it where
SOLIDWORKS does not exist at all.

**And one of those checks was green for the wrong reason.** It looked for the
digit `3` in the report while the report said "**0** attempts in a row" — the
`3` it found was the *threshold*, in the same sentence. The defect was in the
sentinel: it read the live counter instead of the one at the moment it
tripped. *A right number in the wrong place hides a wrong number.*

#### And the cause was not SOLIDWORKS (entry K19)

This station's default document template lives on a **network share**. Every
new part is an SMB read, and a run makes eight hundred of them. When
SOLIDWORKS hangs while opening that file it keeps the handle, and from then
on the file cannot be read **by anyone** — including the next SOLIDWORKS,
which is why three runs in a row died in the same place.

Measured while it was stuck: the profile folder **on the same share**
answered, the server replied to ping in 0 ms, and `Get-Item` on that
`.prtdot` did not return within twenty seconds. Killing SOLIDWORKS: 45,955
bytes, instantly. **It looked like the network, and it was a handle.**

The bench now copies the template locally once per run and says so, with the
source and destination paths; if the copy fails it uses the network one and
**says that too**. It does not cover everything — SOLIDWORKS also reads the
weldment profiles and the material libraries from the share — but it removes
the most frequent read, and the README says which ones remain.

#### Twelve operations now declare the limit they were already refusing (Z-G5)

Forty-five refusal steps across 146 declared cases, on 31 distinct operations.
Twelve of those 31 said **nothing** about their limit in the catalogue
description — and the model does not read bench cases, it reads the
catalogue. For those twelve the only way to discover an already-measured
limit was to hit it. *A declared limit costs zero turns; the same limit
found by a refusal costs one.*

The new sentences are not invented: each one is the message its bench case
already demands, turned to the future tense. Inventing them would have meant
promising a limit different from the one the code enforces — worse than
silence.

The count is a ceiling, not a note, and it was watched moving in both
directions: born at 12 with 12 mute (green), then red with **"there are 0:
lower the ceiling"** once the descriptions were written. It is 0 now, and a
new guard without the sentence that declares it turns the check red **on the
day it is written** — the only day anyone is looking at it.

Alongside it: every `deve_fallire` must name a `gemella`, that twin must
exist, and it must demand a message (entry Z-C11). The bench already checked
twins — after an hour of running, with SOLIDWORKS on. A case written without
a twin is a writing mistake, not a measurement one: it now shows in a tenth
of a second.

#### Every operation now says which document it runs on (entry Z-D0-ter)

All 71 served operations declare `parte`, `assieme`, `tavola` — or more than
one — next to `precondizione`, with a closed vocabulary. The family was an
approximation and `verifiche` proved it alone: `cartiglio` wants a DRAWING,
`verifica_interferenze` an ASSEMBLY, the other six a PART. Three documents
under one label, a fact that lived in the head of whoever wrote the operation.

The values are not guessed: where the precondition says it, it was taken from
there; the twelve without one are in a hand-written table, decided one by one.
The script **stops without writing anything** if it meets an operation it
cannot classify — better unfinished than guessed.

And the check verifies the field was *needed*: if no family were mixed, it
would be a duplicate of the family and should be removed.

#### An example copied from a green case (entry Z-G1)

24 operations out of 71 now carry a real call, copied from a step the bench
**executed and measured**. A schema says which fields are legal; it does not
say which combination builds a part.

24 and not 71 on purpose: the other cases use things that live only in the
bench — the `@faccia(verso=alto)` selectors, the `{LAVORO}` placeholders.
Copying them would have produced 24 good examples and 47 **traps**: a model
that copies a bench selector gets a refusal it cannot understand, and next
time it does not trust the catalogue. That is worse than no example.

#### Every required field is a decision taken (entry Z-G3)

92 required fields, 58 of them dimensions that are genuinely asked for, **34
with a written decision**. "Nobody thought about it" and "somebody decided to
ask for it" read identically in the JSON, and only one of the two can be
improved.

Two have a real shop default and are **not done**: `applica_materiale.materiale`
and `combina_corpi.operazione`. Removing a field from `required` changes the
contract, and a contract change ships with a green bench beside it.

#### Numbers read off an image are not authoritative (entry Z-C10)

The caption next to the views said where the measurements are. It did not say
the ones in the image are **wrong**. It now carries the two measured errors —
a Ø90 bolt circle read as 79, a 12.5 wall read as 6 — says **why** (in
isometric, thin walls are foreshortened) and names the operations that give
the authoritative number.

What cannot be done is stated rather than implied: the bridge cannot know
where a model got a number from. A 79 in an argument is identical to a
measured 79.

#### A leash on calls that never return (entries K18 and Z-C4)

Three bench runs died hung: the process alive, SOLIDWORKS `Responding=False`,
**zero CPU on both**, no modal dialog. The sentinel could not help — it counts
*answers*, and no answer was coming.

It does **not** rescue the call: a wedged COM call cannot be cancelled, and
promising a "timeout that retries" would be another guard that does not do
what it says. It turns an infinite silent wait into a report: **which**
operation, **for how long**.

A heartbeat, not a worker thread — the interop is single-threaded, and moving
calls onto another thread would add a new way to hang instead of removing
one. The working thread announces what it is about to do and says when it is
done; a watchdog outside watches the clock. *It does not touch the work: it
watches it.*

Two callers, two reactions, and the difference is the point: the bench **dies**
with exit code 5 — a run that did not happen is not a run with errors — while
the bridge **says and carries on**, because it is somebody else's service. Its
report goes to **stderr**, never stdout: on stdio, stdout is the JSON-RPC
channel.

#### A dry run: being wrong without touching the part (entry Z-G6)

`disegna` with `prova: true` checks the plan and touches nothing — and works
**with SOLIDWORKS closed**. On a plan wrong in four ways it finds six defects,
including the most valuable one: an argument spelled wrong, which today is
**silently ignored** so the part comes out different from what was asked with
no error at all.

And it always states what it could *not* check — that the uids are still
valid, that the document is of the right type. *A dry run that keeps quiet
about its limits makes you believe the plan is fine*, and then the trouble
arrives one step later with the added confidence of whoever asked.

#### Confirmations reach the USER, not the model (entry W2.5)

This bridge's confirmations were sentences the model re-reads. *A confirmation
that goes through the model is one the model can interpret* — and interpreting
a request for permission means, sooner or later, granting it to yourself.

The bridge now sends `elicitation/create` before an operation that cannot be
undone, and on a `decline` it **stops and says it touched nothing**. Three
decisions worth more than the code: it asks **before anything else**, even
before checking that SOLIDWORKS is attached; **doubt counts as no** (decline,
cancel, an unparseable answer, an error, a timeout); and the list has **one**
entry, because *whoever confirms ten times in a row stops reading what they
confirm*.

The fallback is declared, not silent: a client that does not support
elicitation gets the previous behaviour, and the bridge writes on stderr that
it could not ask.

#### Five tools that check the TODO against reality

A 16,000-line file rots silently. Five probes now check it, and between them
they found, in one day:

- **five wrong API signatures** out of 181 cited — including one the entry
  itself rested a decision on (`MirrorComponents3` has fourteen arguments, the
  entry said thirteen);
- **one broken cross-reference** out of 73 (`vedi C8` meant `Z-C8` — the
  sentence's own subject proved which);
- **two dead `file:line` citations** out of 96 — one of which pointed at C#
  that no longer exists at all, because that test became a set of declared
  cases. *A citation does not move, it breaks, and whoever follows it
  concludes the entry is stale;*
- three distinct categories of unresolvable path — another repository's, a
  closed entry's historical name, and a transient working file.

Every one of those probes **over-reported on its first draft**: 33 paths, 23
signatures, 12 folders belonging to the other repo. None of those were defects
in the TODO — they were the boundary of what the rule could see. *A probe that
shouts is a probe nobody reads*, and narrowing it to shut it up is worse: the
exception gets declared with its reason, and the probe stays wide.

And the three that need nothing but the repository now run **from the pre-push
hook**, because a probe that runs when you remember is a probe that does not
run on the day it matters. They were watched firing both ways first — exit 1
on a deliberately broken copy of the TODO, exit 0 on the real one — which is
why they take the file as an argument: a guard you cannot make fail is a guard
you cannot believe.

The other two stay out of the hook on purpose: they read the SOLIDWORKS
interop, and a hook that fails on a machine without the CAD is a hook the
first person to clone switches off.

---

## 0.75.0 — 23 September 2026 · library 0.104.0

### The first section view, and a guard that was right about the wrong number

Bench 864 checks, 0 red, 681/681 expectations, full run. Contract suite 170,
0 red.

`sezione_in_tavola` (entry GT4). A part with an internal cavity, without a
section, **cannot be read on the shop floor**: hidden lines say there is a void,
not what shape it has.

`CreateSectionViewAt5` takes no entity in its signature. The cut line has to be
**drawn on the sheet and left selected** — the API takes that. Five steps that
all have to work, each able to fail in silence. The 0.6 measurement had mapped
them: `Select4(false, null)`, sketch closed with `InsertSketch(true)` *before*
asking for the view, and a line that **crosses** the view — one that grazes it
gives an empty section without saying anything.

```
"vista_sezione":"«Vista in sezione A-A»", "tipo_vista":2, "e_una_sezione":true,
"viste_prima":4, "viste_dopo":5, "tratteggi":1, "tratteggi_solidi":0
```

**And the check the entry prescribed was on the wrong counter.** GT4 said
*"mandatory check afterwards: `GetSolidHatchCount() > 0` — a section without
crosshatch is a cut line that fell outside the material, and the operation must
fail"*. The criterion is right and it saved the entry; the counter is not.

The operation refused **on its first real use**, inside the first case written
for it, on a vertical cut through the middle of a solid plate. Both could not be
true, so it was measured:

```
hatch BEFORE rebuild: return=0  out=0
hatch AFTER  rebuild: return=0  out=0
face hatch:           1
view type: 2 (swDrawingSectionView)   GetSection(): an object
```

`GetSolidHatchCount` answers **zero on a real section**, before and after the
rebuild — and it has two outputs, a return value and an `out`, both zero. The
counter for a section's crosshatch is **`GetFaceHatchCount`**, which reads 1.

*A right criterion resting on the wrong counter is a guard that always says no,
and it is indistinguishable from one that works until you use it.* Both numbers
come out in the answer: `tratteggi_solidi: 0` is there so nobody walks the
measured road again.

### The bridge could not save and then draw

Preparing the drawing for that measurement, the most ordinary sequence there
is — **extrude, cut, save, create drawing** — stopped at the fourth step, with
this message (translated from the Italian):

```
I stop: the document changed. You had looked at «Parte1597», now
in front there is «...\sonda_sezione.SLDPRT».
```

True, and our fault. `salva_documento` changes the document's identity while the
plan runs — before the save it is "Parte1597" with no path, after it is a path
on disk — and `disegna` restored `_guardato` to the identity read *before*
executing. The next `disegna` was refused with a correct message and a wrong
cause. `annulla` already did the right thing three methods further down.

**The bench could not see it**: it calls the library, not the bridge. The group
that comes in through the client's door (entry F14) saw it, the day after it was
born.

### The check on the two library copies, seen going red at last

Entry K9 left one residue, written down as a promise: *"I have not seen it go
red yet, and I say so instead of letting it be believed. At the next library
change, compile the single project on purpose, watch the red, then run
`build.ps1`."*

Done, by taking the library to 0.104.0 and building only its project:

```
0.104.0.0   Riscontro.Disegno\bin\Release\...   (what the bench measures)
0.103.0.0   bin\...                             (what the bridge loads)

NO   «il ponte e il banco hanno la STESSA versione della libreria»
     «BANCO 0.104.0.0 contro PONTE 0.103.0.0 — chi ha compilato il solo»
     «progetto ha aggiornato il primo e non il secondo: si rilancia»
     «.\build.ps1, che li allinea»
```

That is the defect which on 18 September cost nine hours disguised as "the
operation does not refuse". The cause was not in the code, it was in a file
path.

### `Provata = true` does not mean anything checks it (entry Z-C6)

Measured by crossing the catalogue with every declared case, the bench's C#, and
the hand plans:

| | |
|---|---|
| operations in the catalogue | **70** |
| with at least one declared case | 57 |
| named only by a hand plan — never runs, asserts nothing | 7 |
| **exercised by nothing at all** | **5** |

`cordone_saldatura`, `distinta_taglio`, `fazzoletto`, `taglia_membri`,
`tappo_estremita`: in the catalogue, visible to any client, and no round touches
them. The first of the five now has a declared case; the guard that would make
this permanent is deliberately **not** written yet — *a guard born with twelve
reds is a list somebody silences the next day.*

`Controlli\conta-provate.js` is in the repository so the number can be redone
instead of believed.

### And a folder of cases nobody was reading

Writing that first case created `Banco\casi\saldature\`, and `Famiglie` in
`CasiDichiarati.cs` is an **explicit list**: the folder was not in it, so its
case would have stayed mute. There is now a contract check for exactly that, and
it was watched going red by removing `saldature` on purpose:

```
NO   «ogni cartella con dei casi dentro e' nell'elenco»
     «MUTE, i loro casi non girano mai: saldature»
```

Also measured: the other folders outside the list (`ponte`, `limiti`, `viste`,
`pezzi`, `forma`) hold **only plans** — which is correct, but it is a measured
fact, not a guarantee of the mechanism.

### Three times in two days, an expectation broke on capital letters

```
21/9  it looked for «vero»,        the text said «VERO»
23/9  it looked for «accavallano», the text says «si accavalla»
23/9  it looked for «che serviva», the text says «CHE SERVIVA»
```

The second was a verb ending, repaired by matching the stem. The first and the
third are the same thing, and they were not defects in the cases: `contiene`
compared with `StringComparison.Ordinal`, but what it reads is **prose written
for a person**, and in this repository emphasis is made by raising the voice —
that is, by changing the case.

*Three incidents from one cause are not three cases to repair: they are an
operator to repair.* `contiene` and `non_contiene` no longer look at case.

**And `non_contiene` became stricter**, which is the uncomfortable direction: a
"does not say X" that passed only because the text wrote "x" now goes red. It
should — that green was luck, not a measurement — and it is why the change was
made with a full bench run against it rather than by eye. The run says none was
relying on it.

### A twin that was not one, and the case said so itself

The refusal case for the section passed `a_mm: 9999` while its green twin
passed `etichetta: "A"` — **no argument in common**:

```
!!  lim_sezione_fuori_dalla_vista · «passo 6»
    «ERRORE DI BANCO: gemella finta: non ha nemmeno un argomento in comune»
    «col rifiuto: sono due operazioni che non si parlano»
```

The rule was already written, in a sentence copied into that very file from
the note twin: *"what changes is the VALUE of an argument, not which argument
is passed."* Two calls that share nothing do not prove the check on `a_mm`
works; they prove two different calls do different things. The pair now shares
both arguments and differs only in the value — 0 against 9999. Zero is the
default, so the twin behaves exactly as before; what changed is that it now
**says so**.

### Three runs on one version, and none of them wasted

The first found the wrong hatch counter, the second the case-sensitive
`contiene`, the third the fake twin. An hour each, and each removed something
nothing else would have seen — the first two were defects in code that had
just been written and read twice.

---

## 0.74.1 — 23 September 2026 · library unchanged (0.103.0)

### The four silences have two causes, not four (entry K5)

Reference regression re-run on library 0.103.0: the same four cases, the same
numbers as 15 September. Twenty-eight library versions later,
`Banco/resoconto/regressione.md` is **byte-identical**. No code changed in this
release — only the measurement, and what it is written down as.

**Three of the four are one repair.** The half that settles it is that two
`ancorato_a_bordo` cases **hold**: raise a box by 28 mm and the hole in its
floor stays on the floor, because it is anchored to a **face** and the face
moves with the part. So the cause is narrower, and more useful, than "absolute
anchoring":

*an operation anchored to a **face** follows the geometry; the `x_mm`/`y_mm`
coordinates inside that face's sketch plane do not.*

Four edge-anchored cases, two each way.

**The number that proves it is not an approximation.** In `r03` the plate grows
from 200 to 260 and the expected centre of mass is x = 30:

```
if the pocket follows the centre    x = 30.000   (what the case expects)
if the pocket stays at x = 0        x = 30.588   <- measured 30.588
```

**And `r16` turned out to be the same cause, which took two readings to see.**
The case asserts only the volume, and the 3,141.597 mm³ difference fits two
different geometries — an uncut rounded end, or a slot shortened by 7.854 mm.
*Volume says how much material is left, not where.* Reproduced by hand through
the bridge, looking at the faces: 2 curved faces before, **1 after**. A
shortened slot would still have two.

The reason is the same one: the plate shrinks **from one side only**, so x goes
from [-100,+100] to [-100,+50] while the slot stays centred on 0. The right edge
lands exactly on the arc centre, so the right half-disc is never cut:
360,000 − 43,141.593 = **316,858.407** against a measured 316,858.4. The slot
does break out of the side, exactly as the case feared — silently.

**The fourth is not ours, and that belongs where people read it.** A 2×45°
chamfer on a plate that becomes 1 mm thick does not fail: SOLIDWORKS rebuilds
clean and eats a millimetre of width, `[200, 119, 1]` instead of
`[200, 120, 1]`. It cannot be prevented by anchoring better — only caught by
reading the part back afterwards, which is what this bridge does at every step.

The README now says this under the 12/0/4 table, because "four detach silently"
without the causes is a number nobody can act on.

### And one thing this measurement did not do

The cases still assert only what they asserted. For `r16` the faces were
inspected **by hand**, and next run that diagnosis is gone — the volume remains,
and on its own it does not tell two geometries apart. Putting it in an
expectation would mean writing the **wrong** geometry into the case, which is
fitting the test to the defect. The right path is that the case goes green when
E6/E7 is done; until then the diagnosis lives in the entry.

---

## 0.74.0 — 23 September 2026 · library unchanged (0.103.0)

### A group of checks that comes in through the client's door (entry F14)

Bench 843 checks, 0 red, 662/662 expectations, full run — eight of them new,
from the client's door. Contract suite 168, 0 red.

Everything else in this bench calls **the library**. On 18 September the bridge
was refusing every assembly while its own catalogue promised a whole family of
them, and the bench had 781 green checks — five of them on assemblies — and
**not one could see it**: `EsecutorePiano` appears in no file under `Banco/`.

*Two true measurements of two different things, and whoever reads the big number
believes it covers the other one too.*

The new group does not call the library. It starts
`oce-solid-agent-mcp.exe` as a subprocess and talks to it over stdio, the way
any MCP client would — with SOLIDWORKS running and a document the bench opened
itself. That is exactly the difference from `Prove.Contratti`, which has been
speaking the protocol for days **without** SOLIDWORKS: there every plan dies on
"there is no open document", which is a true refusal and proves nothing about
the rest.

**What it proves that nothing else proves:** everything *between* the client and
the library — preliminary checks, the JSON-to-arguments translation, refusals on
operations the open document does not allow, and `guarda_il_pezzo`, which does
not go through the executor at all.

```
OK   «porta: guarda_il_pezzo nomina il documento del banco»   «Parte1241»
OK   «porta: il piano riesce»
OK   «porta: e il pezzo e' cambiato davvero, misurato dal banco»  0 -> 96000 mm3
OK   «porta: un'operazione di assieme su una PARTE e' rifiutata»
OK   «porta: e il rifiuto dice su cosa sta lavorando»
OK   «porta: il ponte e' ancora vivo dopo una notifica»
```

**The bridge says "riuscita"; the bench does not take its word.** It reads the
volume back from SOLIDWORKS over COM: 0 → 96,000 mm³ for a 120×80×10 plate.
That is the one thing this group can do and the contract suite cannot.

### Red by construction, and watched going red

An `assiemi` operation with a **part** open must be refused, and the refusal
must say *what it is working on* — not a fixed sentence. Proven by temporarily
pointing that case at an operation that does work on a part: both refusal checks
go red, and the detail shows `"riuscita":true` where it should have been false.

Two assertions were too loose on the first pass, and loose in the worse
direction — they passed in cases that should have failed. One accepted any
answer containing the word «assieme», which a *successful* plan could contain;
*a `||` added for safety is a `||` that one day holds on its own.* The other
accepted the word «parte», which sits in any fixed sentence about document
types — exactly the answer the check exists to reject. Both now demand the
document's own title.

### One transport, not two

`Prove.Contratti` already had the plumbing, so it moved to
`Condiviso/ClienteMcp.cs` and both projects compile it. Copying would have been
quicker and wrong: inside it is something that cost an afternoon — the UTF-8 BOM
that `AutoFlush = true` writes in front of the first line, three bytes that made
the bridge answer «Primitiva JSON non valida» while the symptom looked like
«initialize does not answer». *A defect paid for once gets fixed in one place,
or it gets paid again in the copy.*

`Condiviso\` is now in the folder-to-project map of the check that verifies every
source belongs to a project — otherwise it would have been the one source folder
nobody looks at, where a file could sit uncompiled and unmentioned.

---

## 0.73.0 — 23 September 2026 · library 0.103.0

### The 16 KB ceiling was already broken, and the bench could not see it

Bench 835 checks, 0 red, 662/662 expectations, full run. Contract suite 168,
0 red. The check now reads: `13216 caratteri, margine 2784 (17%)`.

The bench check «stato: sotto i 16 KB su un telaio da quattro barre» passed at
**15,922** characters against 16,000 — seventy-eight to spare. But the bench
runs with **exactly one document open**, because it refuses to start otherwise.
The same frame with two documents open — what anyone working actually has —
measures **16,089**.

*The ceiling did not have a thin margin: it was already over, and the check
could not see it because the bench cleans its own desk.* Two correct defences
which, together, hide a third.

**Where the characters are, measured rather than imagined:**

```
    8,718  «facce»    60 entries   (145 each)
    5,572  «spigoli»  60 entries   ( 93 each)
    1,799  everything else
```

Eighty-nine per cent is two lists, and the entries are already lean — an edge is
`{"uid":"E1","lunghezza_mm":1000,"punto_mm":[...],"lungo":"X","fra":["F36","F1"],
"corpo":"C3"}`. There is no fat to trim: **the only lever is how many**. Which
is the question the check's own comment had been asking for days — *"do we
really need sixty edges?"* — and the answer is no.

**Edges, not faces.** Faces are the attachment surface: almost every operation
takes a `faccia_uid`. Edges are used by few — fillets, chamfers, drawing
dimensions — and on a four-bar weldment they are noise: sixty out of three
hundred–odd is a sample you cannot rely on anyway. `MaxSpigoli` goes from 60 to
30, and the ceiling still declares itself: `spigoli_totali` says how many there
were, `troncato` goes true.

Measured after: **13,378 with two documents open**, a 16% margin.

**And the check now demands the margin, not the ceiling.** It is called
«stato: sotto i 16 KB con almeno il 15% di margine», and it prints the margin **always**,
including on green — the only moment you can see the wall coming. *A check that
says "passed" seventy-eight characters from the wall is not warning anyone.*

---

## 0.72.0 — 23 September 2026 · library unchanged (0.102.0)

### The same document, with something else inside it (entry X2)

Contract suite 168, 0 red. The bench was not re-run: the library did not
change — only the bridge did.

The bridge already refused to draw on a document that was not the one you
looked at. It did not notice **the same document with something else inside**:
title and path match perfectly while someone drags a dimension in the
SOLIDWORKS window.

That matters because **face identifiers are positional**. A plan is born from
one reading and its `F1`, `F2` speak of that moment; on changed geometry `F1`
may be a different face and the operation succeeds in the wrong place — the
defect already measured on 14 September with the two pockets, both of them
«riuscita: true».

`guarda_il_pezzo` now keeps a light signature — volume, number of features,
file write time — and `disegna` compares it before executing. All three are
already in hand: no extra reading, nothing new asked of SOLIDWORKS. *A check
that costs as much as the thing it protects does not get run.*

**It stops with the criterion, not with a verdict.**
«il volume e' passato da 96000 a 97125 mm3; le lavorazioni sono passate da 1 a 2» can be checked;
«lo stato e' cambiato» has to be believed. The difference is the risk the entry
itself named: an unverifiable warning is one you learn to ignore.

**What it does not catch, said here rather than discovered:** a change that
leaves the volume identical and the feature count unchanged — moving a hole
without resizing it. Catching that needs a geometric fingerprint, which this
is not.

### The false positive the entry warned about, written and then found

The signature was taken in `guarda_il_pezzo` and never refreshed, so
`guarda -> disegna -> disegna` stopped at the second: the first `disegna`
changes the volume, and the comparison read that as someone editing by hand.
The rule was already there three lines further down, for the uid signatures:
after the bridge itself changes the part, the new reading is the base for the
next plan. The signature now follows it in the same three places — after
`disegna`, after undo, after redo.

**No contract test could have seen that**, and that is the point of the probe.

### `Controlli\prova-x2.js`, and a second actor

Nine contract checks cover `CosaECambiato`, a pure function on two
dictionaries. They do not cover the wiring, and the wiring is where the bug
was. The probe drives the bridge over stdio and proves both halves:

```
=== 1. «guarda -> disegna -> disegna, senza falsi positivi»
  OK   «il secondo disegna passa (nessun falso positivo)»
=== 2. «qualcuno cambia il pezzo da fuori, e il disegna dopo si ferma»
  OK   «qualcuno ha cambiato il pezzo da fuori»  volume: 96000 -> 97125 mm3
  OK   «il disegna dopo si ferma»                «Mi fermo: il pezzo e' cambiato...»
  OK   «e dopo aver riletto si riparte»
```

The change has to come from **outside the bridge** — while it comes through
the bridge, the bridge knows and refreshes its signature — so there is a new
tool, `Controlli\CambiaAMano`, which attaches to SOLIDWORKS and extrudes a
block into the active document. Compiled C#, like every COM probe here:
PowerShell 5.1 cannot resolve methods on the SOLIDWORKS IDispatch.

**Two things the probe got wrong first, both worth keeping written.**

It moved the file's write time instead of changing geometry — and after
`salva_documento` the open document still answers `percorso: ""`, so there was
no date in the signature to compare. *A probe testing the wrong thing is worse
than no probe: it gives a red that sends you looking where the defect is not.*

And it paired replies to requests **by arrival order** instead of by `id`. One
extra line on stdout and everything shifts by one: it was reading the previous
call's answer and judging it. That is not a random error — it is a systematic
off-by-one, and it is why the protocol has an `id` at all.

---

## 0.71.1 — 23 September 2026 · library unchanged (0.102.0)

### The guard checked the library, not the bench

Contract suite 159, 0 red. The bench was not re-run: nothing in the library
changed.

`Prove.Contratti` refuses a delivery where nobody ran the bench — it reads
`Banco/resoconto/ultimo.json` and checks the run is whole, exited 0, on the
current library, and later than the last change to `Riscontro.Disegno`.

**It watched one half.** The bench is two things: the library that measures
and the bench code that decides what to measure. A new check, a new counter, a
stricter expectation — none of it triggered anything, because the library had
not changed.

Not hypothetical: earlier today the clean-desk guard and the CAD-document
count went into the bench *after* a run had started, and the committed report
described a bench that was no longer in the repository. Not false — that run
happened — but answering a different question from the one it appears to
answer: not "this bench is green" but "a bench from a few hours ago was".

**Watched firing, on a real commit, in a throwaway clone.** One line added to
`Banco\Program.cs`, committed without re-running: the library check stays
green, the bench check goes red. In a separate clone because proving it here
would have meant dirtying the history to clean it up again — *a test you run
by undoing a commit is a test you will eventually skip.*

**And a `null` that meant two things was untangled.** "Git did not answer" and
"there is nothing to find" both returned `null`, and the first was printed with
the second's message. A repository with no unproven commits is now a green that
says so; git not answering is a red, because a check that could not look is not
a check that found nothing.

---

## 0.71.0 — 23 September 2026 · library 0.102.0

### A drawing that would not close, because its name had changed

Bench 835 checks, 0 red, 662/662 expectations, full run, 67 minutes. Contract
suite 158, 0 red. **Zero documents left open afterwards**, which is this
entry's own closing criterion.

**Fourteen documents were left open after every full run**, seven parts and
seven drawings, one pair for each drawing case. The mechanism that should have
closed them existed, was correct, and carried a comment explaining exactly why
it mattered — the ~650 characters of extra state and the 16 KB check that turns
red without any code having changed.

**The title was captured at the wrong moment.** When a step produces a new
document the bench records it for later closing. At that instant the drawing is
called `Disegno38 - Foglio1`. Two steps later the case saves it and it becomes
`la_tavola_si_quota - Foglio1`. At teardown the bench asked SOLIDWORKS to close
`Disegno38 - Foglio1` — **and `CloseDoc` on a name that does not exist is not an
error**, so nothing said anything. An open drawing then holds its part open, so
each case leaked two.

**A document's identity is not its title.** The same lesson as the active
document: hold the reference, ask for the name when you need it.
`Preparazione` now carries `DocumentiDaChiudere`, a list of `IModelDoc2`, and
teardown closes those before the names — the drawing holds the part, not the
other way round.

**And teardown counts again.** It cannot go red — it also runs inside a case
that already failed, and a second red would bury the first — but if documents
remain it says so. A line that appears only when something did not work is the
least that stops a silent defect from living another seven days.

**How it was found is worth as much as the fix.** Not by reading the code, but
by reading the run's own diary: the line
`| da qui si lavora su «Disegno38 - Foglio1»` says the name the bench saw, and comparing it with what
`ChiudiScarti` listed after the run closes the diagnosis in a minute. *The bench
had been saying it all along; nobody had put the two lists side by side.*

### And it moved a number that was not supposed to be a measurement

The K2-bis count — how many checks run with a CAD document open — went from
**806 with / 29 without** to **750 with / 85 without**. Fifty-six checks were
counted as *with a document* only because seven leaked drawings were still
sitting there. The entry had declared the figure a ceiling rather than a
measurement precisely because of this leak; the leak is gone, and the ceiling
came down by fifty-six.

*A declared blind spot is worth what it costs to close it* — here it cost one
line of diagnosis and produced a number that is fifty-six units less
flattering.

### Smaller

- **`soprattutto` counted the band that goes below.** The horizontal-dimension
  counter was named "above all" and its next line subtracts. Renamed to `sotto`;
  no behaviour change. It had already produced two wrong *descriptions* in one
  day — the 0.101.0 version note and a spoken summary — which is exactly how a
  misleading name costs.
- **The 0.101.0 note said «orizzontali sopra».** It is wrong, and it is not
  corrected in place: *a version note rewritten backwards is a note that lies.*
  The 0.102.0 note contradicts it where someone reading in order will find it.
- **The comment about `AssemblyInformationalVersion` said no check compared the
  two numbers.** One does now, and it fired the same day. *Between a comment
  that warns and a test that goes red there is no difference of degree: the
  first is read after you have already made the mistake.*

---

## 0.70.0 — 23 September 2026 · library 0.101.0

### Dimensions land where they belong, and a CI that had never once been green

Bench 835 checks, 0 red, 662/662 expectations, full run, 77 minutes. Contract
suite 158, 0 red.

**Dimensions are no longer left in a heap on top of the view.** `quota_in_tavola`
created them and dropped them all at the same spot, so a drawing came out with
four labels stacked over the part. The placement is now computed from the edge
itself: `IEdge.GetCurveParams3()` gives the two endpoints in the model,
`IView.ModelToViewTransform` maps them onto the sheet, and **only then**
`IView.Position` is added — without that last term every dimension landed at
x = 0, which is exactly what the first attempt did.

Horizontal edges get their dimension below the view, vertical ones to the right,
oblique ones off to the left on their own, and the three bands are stepped
independently: 12 mm clear of the outline, 10 mm apart. Shortest nearest the
part. Those are not invented numbers — UNI ISO
129-1, as reported by the Zanichelli sheet (7–12 mm between parallel dimension
lines on A3/A4) and the University of Salento lecture notes (more than 10 mm
from the outline, more than 6 mm between lines). The two values the code
already used satisfy all four constraints: *the defect was never the numbers,
it was the direction.*

**Duplicates are removed by geometry, not by value.** The first version
deduplicated on the measured length, and that is wrong in a way that only
shows up on a real part: two different 10 mm steps are two different features
and both have to be dimensioned. The key is direction on the sheet plus the
interval of the projection onto the measuring axis.

**And the operation says what it did not look at.** Which dimensions a drawing
actually needs, whether the dimensioning is complete, which view shows a
feature most clearly, whether the text overlaps the geometry — none of that is
decidable here, and the answer carries the list instead of implying it did
everything.

### The CI had never compiled, and the README said it did

`.github/workflows/prove.yml` began with `msbuild Oce.Solid.Agent.csproj`. That
project — and `Riscontro.Disegno` under it — resolves `SolidWorks.Interop.*`
through a `HintPath` inside the SOLIDWORKS installation. No GitHub runner has
that folder, and the interop belongs to Dassault: it cannot be committed and it
cannot be downloaded.

Measured rather than assumed: building with `/p:SwInstallPath=Z:\non-esiste`
produces two hundred–odd `CS0246: the type or namespace name 'IModelDoc2' could
not be found`. The first step failed every time, so the two steps that were
worth something — the contract suite and the refusal to ship without a bench
run — were never reached. **A CI that has never run is a badge, not a test.**

The bridge is now compiled **only where the interop is present**: a step looks
in the same three places msbuild looks, and on a self-hosted runner with
SOLIDWORKS the bridge really is built (and a missing executable there is a red,
not an excuse).

**What cannot run does not turn green.** The suite has a third verdict beside
OK and NO: `--`, *not runnable here*. It is counted separately, printed, written
to `prove.xml` as `<skipped>` with `skipped=` on both the suite and the total,
and summarised on the run page. The declared minimum is now measured against
what actually ran — twenty-five rows of "not runnable here" used to satisfy it.

**The distinction that holds it together** is between *cannot here* and *you
forgot to build*. Same symptom — no executable — opposite defects. They are
told apart by looking at the interop, not at the executable.

**And what could run anyway, now does.** The catalogue group stopped at its
first line because `bin\catalogo.json` is produced by `build.ps1`: thirty-odd
checks on the catalogue's *content* were skipped although they read a JSON file
and have nothing to ask SOLIDWORKS. They now fall back to
`Strumenti\catalogo.json`, which is in the repository, and say so.

**The new verdict was watched firing.** `PROVE_SENZA_INTEROP=1` makes the suite
behave like a bare runner on a machine that has SOLIDWORKS — otherwise the code
that writes those rows would have stayed untested until the first CI run, which
is another way of saying *believed*.

### The bench now refuses to start on a dirty SOLIDWORKS

A full run — an hour and a quarter — went red on
`stato: sotto i 16 KB su un telaio da quattro barre: 17435 caratteri, 15 documenti aperti`. Nothing in the
code had changed; fifteen documents had been left open by an earlier session,
and a document's state lists `documenti_aperti`.

It had happened before, on 14 September, and the remedy was already written in
the comment next to that very check. The first version of this entry concluded
that the remedy was documented and never executed. **That was wrong, and worth
saying so:** `banco.ps1` runs it, step 3 of five, before every run. What
bypassed it was a `ProvaSaldatura.exe` launched by hand to pass `--libreria`.

The real finding is less comfortable. **The protection lived in the wrapper**,
and you step outside the wrapper precisely when you are doing something
unusual — a different library, one group, an existing binary — which is when it
matters most.

The bench now looks first **however it is launched**, lists what it found, and
exits 3 — a run that never started is not a red run, and calling it red would
mean writing the report of a run that did not happen. `--chiudi-gli-scarti`
closes them and goes; it is not the default because `CloseDoc` discards unsaved
changes, and on this machine that could be someone's work. It also counts again
afterwards: "I called close fifteen times" is not "they are closed".

`banco.ps1` step 3 and this check now say the same thing twice, and that is
fine: the first quietly closes what it knows is scrap, the second refuses to
begin if anything is left.

Measured both ways on the real dirty desk: without the flag, exit 3 and fifteen
names; with it, `chiusi 15 documenti su 15, ne restano 0`, and then the check
above passes at 15922 characters.

### How much of that 835 actually has SOLIDWORKS in it

The per-group count (entry K2) said *where* the checks are. The other half of
that entry asked *how many of them have a CAD document under them* — and the
answer is now printed beside the total:

```
«prove con un documento CAD aperto: 806 - senza: 29»
```

**It is asked of SOLIDWORKS, not of the bench.** The first draft kept an
internal counter, incremented on open and decremented on close, and it would
have been wrong: documents are opened in at least four places and closed in
three, one of which deliberately tries twice (entry GT0, the teardown order
nobody claims to know). *A counter with seven keepers is a counter that will one
day say -1.* `GetDocumentCount()` asks the one who knows.

**And it says what it really measures**, which is not what the entry's title
promises: "a document was open when this check was recorded". Since the bench
only starts on a clean desk, that document is necessarily one it opened itself —
but while the drawing cases leak documents (entry K12-bis), later checks count
as *with document* even when they never touched CAD. **It is a ceiling, not a
measurement, and the summary line says so** rather than leaving it to be
discovered.

### Smaller things, all of them found by something other than reading

- **The publishability check was looking at 29 commits out of 104, and the
  history rewrite was the reason.** `git filter-branch -- --all` rewrites the
  *local* remote-tracking refs too, so `origin/main..HEAD` collapsed to 29 while
  the real remote still held the old history. It now asks the remote where it
  actually is, and looks at everything when the answer differs — or when the
  remote does not answer at all.
- **Two TODO entries were both called K11.** Caught by the contract check that
  exists for exactly that, not by rereading.
- **A bench expectation was pinned to a verb ending.** It wanted «accavallano»
  and the rewritten text says «si accavalla». Second time in two days that an
  expectation hung on a word form — the first wanted «vero» where the text said
  «VERO». It now matches the stem.
- **`CONFRONTO.md` exists** (entry K6), with the (D)/(M) rule and a column the
  entry did not ask for: the date of each measurement. Rewriting it found one
  figure that was simply wrong — "~100 commits in 9 days" is 104 in 13 — and
  nobody would ever have contested it.
- **The six trademark conditions** of entry I4 are applied: SOLIDWORKS® is an
  adjective everywhere in the README, the English non-affiliation formula and
  the explicit reference to Apache-2.0 section 6 are in `NOTICE`, and the short
  declaration is repeated at the foot of the README, because almost nobody
  opens a NOTICE.
- **`AssemblyInformationalVersion` was two versions behind again**, and this
  time a check said so instead of a comment:
  `assembly 0.101.0.0, proprietà del file 0.100.0`. The comment warning about exactly this has been in
  `AssemblyInfo.cs` since the last occurrence — *a comment is not a check*. The
  fix landed after the bench run; it changes one metadata string and no
  behaviour, and `AssemblyVersion` — the number the report is checked against —
  is the same 0.101.0 the bench measured.
- **The PowerShell of the CI now lives in `.github\passi\*.ps1`**, not inside
  the YAML, and `Controlli\controlla-workflow.ps1` parses every one of them and
  checks the workflow actually invokes them; the `pre-push` hook runs it. Code
  inside a YAML nobody executes is this entry's own defect moved up one level.
  Twenty minutes after being written it caught a real break: converting those
  scripts to pure ASCII (house rule — PowerShell 5.1 reads a BOM-less file as
  ANSI) turned one line into `$r += ""Non eseguibile qui" ..."`, quotes inside
  quotes. `riga 48: Token 'Non' imprevisto`, first try.

---

## 0.69.0 — 23 September 2026 · library 0.100.0

### How much the suite actually measures, and a check that could never pass

Bench 832 checks, 0 red, 659/659 expectations, full run, 77 minutes. Contract
suite 158, 0 red.

**What the number covers (K2).** 760 of 832 checks sit in one group. «832 checks
on real SOLIDWORKS» covers far less ground than the number suggests, and putting
it in front of anyone counts in the same figure checks that never open a CAD
document. Not a lie: *a big number reassures in proportion to how big it is, not
to how relevant it is.* The report now carries the per-group count, and goes red
if the sum stops matching the total.

**What it asserts, and what it only survives (K3).** `659 with a declared
expectation, 173 without`, and the names of the second group go to
`Banco/resoconto/senza-attesa.txt`. The list also shows the entry overstated
it: 43 are twin checks, 42 are refusal steps asserting the message, 26 are the
bench checking itself, 16 are lookup tables. Most assert — in C# instead of in a
case file.

**A check that could never pass.** «the run is later than the last change to the
operations» compared the run with the commit date, but the report is written
before it is committed and lands in the same commit. On b35550d: run 21:12:13,
commit 21:13:52. It missed always. Now it walks the library commits and stops at
the first that does not carry a fresh report — a commit that carries one *is* the
run.

**Two documents described this machine instead of the repository.** The bench
`LEGGIMI` counted `casi/forma/ 5`, which git does not track: the check
«every folder named in the LEGGIMI exists» was green here and red for anyone cloning.
Found by cloning. And TODO.md pointed at `scratchpad/conta_todo.js`, which is
not versioned — the tool meant to keep the counts honest could not be run by
anyone. `Controlli/conta-todo.js` now exists, and the counts are 108 open of
300, not 104.

Plus: the README carries the trademark line it was missing.

---

## 0.68.0 — 22 September 2026 · library 0.100.0

### A note on the drawing, and the counter that would have closed four entries

Bench 831 checks, 0 red, 659/659 expectations, full run, 71 minutes. Contract suite 158, 0 red.

**`nota_in_tavola` (entry E2-bis).** A workshop drawing is not only geometry and
dimensions: it carries the general notes — "break all sharp edges", "galvanise
after welding", "unspecified tolerances ISO 2768-mK". Until today the bridge
could not write one.

Measured on the real drawing, first through the bridge and then by the bench:

```
note_foglio_prima: 67  ->  note_foglio_dopo: 68
note_viste_prima:   0  ->  note_viste_dopo:   0
dove_mm: [20, 250]         testo_combacia: true
```

**The second line is the one that earns the entry.** `InsertNote` writes on the
SHEET, but `GetNoteCount()` lives on `IView` — and a drawing's real views start
*after* the first one, because the first "view" **is** the sheet.

Counting only the real views would have read `0 -> 0` with the note sitting
exactly where it belongs, and the conclusion would have been "notes do not get
created either: this is systemic". That false negative would have wrongly closed
four entries of this TODO — surface finish, datums, welding symbols, GD&T — all
of which are open precisely *because* a note is born.

So the two counters come back separately, and the bench case asserts that **the
views stay at zero**. Without that line, `note_messe: 1` would be green even
with the note inside a view — and a note inside a view travels with the view.

### Two things that went into the code because they were measured

- **`InsertNewNote3` is `Void`** — a button, not an API: it has no way of saying
  it did nothing, and it does not. `InsertNote` answers, so that is the one.
  (The entry named `InsertNewNote2`; this installation exposes the `3`, equally
  mute.) Written in the source so nobody tries the other one again.
- **The position does not read back on the first call.** `GetTextPoint2()`
  returns an object whose `ArrayData` is null. Without a second route this would
  have been written down as "on this version the position cannot be read", as if
  it were a property of SOLIDWORKS instead of a property of the first call
  tried. Three routes; the first that answers wins.

A position outside the sheet is refused **before** anything is written, naming
the number asked for and the sheet size. Outside the sheet a note exists, gets
counted, and is not on the paper — and no error would say so.

### The catalogue can be stale, and nothing was checking

Writing the operation, compiling it, and calling it produced: *"there is no
operation «nota_in_tavola». Did you mean: quota_in_tavola"* — while it existed,
was in the dispatcher, and was inside the DLL the bridge had just loaded. It was
missing only from `catalogo.json`, which is the only thing the bridge looks at to
know what exists.

And `Strumenti/aggiorna.md` said: *"`build.ps1` regenerates the file with every
package, so it cannot fall behind unless somebody deletes that step"*. **Not true
with `-SoloPonte`**, which is how you compile while iterating.

> A guarantee written in a document is not a guarantee: it is a hope with a
> source.

The tool to notice already existed — `--controlla` exits 1 and says *where* — and
nobody was calling it. Two new contract checks now do: the C# against
`Strumenti/catalogo.json`, and `Strumenti/` against `bin/`. They are different
defects: the first means somebody edited the operations and did not re-export,
the second that the bridge is still loading the old one. Both watched failing.

### And a red worth keeping on the record

The first full run came back with three reds, all from one mistake of mine: the
answer says «sia VERO» in capitals and the expectation looked for `vero` in
lower case. `contiene` is case-sensitive.

The interesting part is the second and third red. The bench **refused to count
the twin's refusal as proof** — *"the twin «la_nota_finisce_sul_foglio» is not
green in this run"* — and that is exactly right:

> A call that refuses proves something only if the same call with a valid
> argument works.

The defect was in the expectation, not in the operation. From the outside the
two are written the same way.

---

## 0.67.0 — 22 September 2026 · library 0.99.0

### The dimensions in a drawing are CREATED, not imported

Bench 815 checks, 0 red, 645/645 expectations, full run. Contract suite 156,
0 red.

**`quota_in_tavola` (entry K10).** Until today the bridge could build the part,
save it, make the drawing, put three views on it, read the title block and
export the PDF — and could not put a single dimension on it. A drawing without
dimensions is not a workshop drawing: it is printed geometry.

The other operation, `quote_dal_modello`, **imports** the dimensions the part
already carries, with `InsertModelAnnotations*`. Measured on 18 September: a
part with seven dimensions all marked for drawing, on **eight** drawing
templates (seven stock plus the workshop's own), through five different routes,
brings **zero** of them across — with a positive control green in the same run
(`InsertNote` works on that same drawing). That route is closed.

But the goal was never "the model's dimensions in the drawing". The goal is **a
dimensioned drawing**, and `AddDimension` creates them.

> A name that describes the means instead of the end narrows the solutions
> before anyone looks for them. For two days the other operation's name decided
> the road.

**Every dimension is read back.** `GetSystemValue2` after placing it, so the
operation answers *how much it measures*, not "done". A dimension that appears
and measures something else is worse than one that is missing: on paper it reads
the same, and in the workshop the cut is made on the wrong number.

The bench case asserts the millimetres — 120 the length, 10 the thickness — and
also asserts that **80 is absent**: 80 is the width, visible in the top view and
not in this one. Without that line, "contains 120" would not say *which view*
was dimensioned, because there is a 120 there too.

### Three numbers for the same thing, and a check that was missing

The bench prints **70** operations, the catalogue declares **68**, the README
said **67**.

The first two are both right and measure different things: 70 is what the
dispatcher can **execute**, 68 is what a model can **see**. The two missing ones
— `croci_di_centro` and `distinta_taglio_in_tavola` — are out on purpose, both
`Provata=false`: the API answers "yes" and creates nothing, 0 → 0 on every
combination probed.

**Nobody was comparing the two lists.** The same difference would be produced by
a *forgotten* operation: written, working, never added to the catalogue. That
would give no error anywhere — the bench would test it (it reads
`NomiEseguibili`), the catalogue would not mention it, and no model could ever
call it.

New check: every executable operation is either in the catalogue or declared
unproven, and the reverse — every catalogued operation is really executable.
Watched failing, with `quota_in_tavola` removed from the catalogue by hand:
*"executable and invisible to the model: quota_in_tavola"*.

### The third version number, and a comment that was not a check

`AssemblyInformationalVersion` is the one Windows shows in the file properties.
No code reads it, so it ages on its own: on 18 September it said 0.61.0 with the
assembly at 0.66.0, it was corrected, and today it said **0.94.0 with the
assembly at 0.99.0** — five versions behind, again.

Right above that line there was already a comment explaining the defect at
length. It did not help, and it could not:

> A comment is not a check. Whoever aligns the number by hand keeps aligning it
> for as long as they remember, and then stops.

Now a check compares the two, on the binary and not on the source — the binary
is what ships. Watched failing by putting 0.94.0 back.

### Also

The two library copies had drifted again (bench 0.99.0.0, bridge 0.98.5.0):
`build.ps1` realigns them, and the check written on 18 September caught it
without anyone looking for it.

And a line in the K10 bench case declared "120 the plate, 5 the chamfer" while
the assertion checked **10**. The 5 came from a probe run on a different part;
the assertion had been corrected and the prose had not. Green every time, and
anyone reading without getting to the bottom learned a false number.

---

## 0.66.0 — 18 September 2026 · library 0.98.5

### The drawing of the wrong part

Bench 796 checks, 0 red, 628/628 expectations. Contract suite 151, 0 red.

**`viste_standard` was putting views of a part that had nothing to do with it
into the drawing, and nothing gave an error.** Three views, a PDF that exports,
and a drawing of somebody else's part arriving in the workshop.

`crea_disegno` takes the right part — it is handed to it. But `viste_standard`
runs **on the drawing**, where it no longer holds the part, and fell back to
"the first open part or assembly", in the order SOLIDWORKS happens to list them.

> An order nobody declares is not a choice: it is a draw that looks like a
> choice. And a draw with one ticket looks like an algorithm.

Fixed: one part open, that one — there is nothing to guess. Two or more, it
**refuses and lists them**, asking for the `modello` argument. Watched refusing.

**Why it was invisible for months.** There was always exactly one part open, so
the draw always won. It surfaced the day a second drawing case was added, whose
part sorts first alphabetically and stays open. And the drawing cases could not
have caught it anyway: they counted **three views** and never asked which part
they were of.

> A test that counts the objects and never looks at what they are is green even
> when the part is a different one.

So `la_tavola_e_un_secondo_documento` now asserts `misure.modello`, and all three
drawing cases name the part they want instead of hoping there is only one.

### Four broken instruments, and none of them was the product

Finding this took a day, and the day is worth more than the defect.

- **The witness queued behind the defendant.** A diagnostic expectation that
  tells two opposite diagnoses apart was written *after* the expectation that
  could fail — in a bench that stops a case at the first red. It never spoke:
  the report had the red and not its reason.
- **Empty out of ignorance.** The lookup returns `null` when it cannot reach the
  model; without saying so, the list stays empty **out of ignorance rather than
  measurement**, and an empty list passes any "does not contain". The
  distinction lived only in a comment. *A distinction that lives only in a
  comment is not a distinction: it is an intention.* Now there is a field
  carrying the **path** of the model actually read — and it was that field that
  named the wrong part and ended the hunt.
- **The wrong binary, and it cost the most.** There are two copies of the
  library: the one the bench measures and the one the bridge loads, put there by
  `build.ps1`. Building **only the project** — the most natural thing while
  iterating — updates the first and not the second. They had drifted four
  versions apart for nine hours: the fix was tried three times from the bridge
  without ever being in that binary, and the symptom was perfect ("the operation
  does not refuse"). A new contract check compares the two versions.
- **A wrong verifier.** The script judging one probe printed "refuses but does
  not list the alternatives" — false; it was looking for a part name I had
  renamed. *A wrong verifier delivers a wrong verdict with the same confidence
  as a right one.*

### Also

- `cartiglio` separates "the property is not on the model" from "it **is** on the
  model and the title block shows it empty": two opposite diagnoses that used to
  land in the same list, sending you to repair the wrong thing.
- `salva_documento` looks at **why** it did not save instead of reporting a code:
  a missing folder and a read-only file are visible from here, and `SaveAs`'s
  number says neither.
- Six annotation counters per view in the drawing state, always written even at
  zero — plus six for the **sheet**, because `InsertNote` writes there and
  counting only views would make `note` a field that cannot become true.
- The contract suite goes from 147 to 151 checks.

## 0.65.1 — 18 September 2026 · library 0.98.1

### The drawing that does everything except take a dimension

`Z-C1` asked for `quote_dal_modello` to be re-measured today. It was, and the
answer is **no** — but the value is not "it doesn't work", which was known since
14 September. The value is **where**.

Four measurements, each narrowing the last:

| | question | answer |
|---|---|---|
| 1 | on a part built **by hand**? | 1 dimension in the model, 0 in the drawing |
| 2 | on a part built **by the bridge**? | **7** in the model, **0** in the drawing, 13 routes tried |
| 3 | are those 7 **marked for drawing**? | **all 7**, `MarkedForDrawing = True` |
| 4 | is it the import or the session? | **the import** |

Measurement 1 counted for nothing, and understanding why is half the lesson: a
part built by hand has no sketch dimensions, while the library's own operations
dimension **and mark** them. Two different objects, and the difference was the
thing under examination.

Measurement 3 is what moved the diagnosis. `quote_dal_modello` imports only
marked dimensions, so "the part has 7 and none arrive" left two **opposite**
causes open — none are marked (the fault is upstream, in whoever should mark
them) or they are marked and the import will not take them. Two repairs, two
files. The data existed and the message did not carry it, so a probe was needed
to split what the answer could have split. Fixed: the refusal now writes
`[marcata]` / `[NON marcata]` beside every name and the count of marked ones up
front.

Measurement 4 is a positive control in the same run, on the same drawing. Five
import routes — `AllViews=true` with nothing selected (what the library does),
the view selected by name, the view activated, the view selected with
`AllViews=true`, and plain `InsertModelDimensions(0)` — all zero. And then, on
that same drawing in the same instant, `InsertNote` returns an object and the
annotation count goes 67 → 68. **It is not the session and not the document: it
is the dimension import, and only that.**

Two of the first four attempts had not actually been attempts: they did
`((IEntity)view).Select4(...)` and took `E_NOINTERFACE`, because a drawing view
is not an `IEntity` — it is selected by name with type `"DRAWINGVIEW"`. Without
reading the exception they would have gone into the list as "tried, doesn't
work". A list of routes tried is persuasive in proportion to its length, which
is exactly why each line has to be checked for having arrived where it says.

One hypothesis survives all four: the import works **in process**, inside an
add-in. That is now the only road to a dimensioned drawing.

### A contract check that admits what it is not

While the import is broken, that refusal message is everything a model gets when
it asks for dimensions — and it is the part that works. A new contract check
requires it to still name how many dimensions are marked and the list of routes
tried. It reads the **source**, not the run, and says so: the behavioural test
would be a bench case with `deve_fallire`, and it cannot be written today,
because the bench requires a **green twin** with the same operation and
`quote_dal_modello` never succeeds. The rule is right — a test that only checks
the refusal cannot tell "the guard works" from "nothing works" — and the check
does not pretend to be that test.

The contract suite goes from 145 to 147 checks.

## 0.65.0 — 18 September 2026 · library 0.98.0

### The title block says what it wants, and a door that was locked

Sixty-seven operations. `cartiglio` (GT3) and `verifica_interferenze` (GA10).

**A title block holds no text: it holds links.** `cartiglio` reads which custom
properties this drawing's title block actually references, with their exact
spelling, because they cannot be guessed. Measured on this workshop's own
template: **39 notes, 26 properties referenced, 19 of them empty.**

Three things nobody would have guessed, and each one is a defect that gives no
error:

- They are almost all `$PRPSHEET`, not `$PRP` — they are read **from the part
  shown in a view**, not from the drawing. Write them on the drawing and every
  box stays blank, and neither side complains. The answer marks those names
  "(from the part, not the drawing)" rather than just listing them.
- **The spelling is mixed inside one title block**: `TRATTAMENTO` uppercase
  beside `Descrizione` capitalised, `Peso` and `MASSA` in the same note. There is
  no rule to derive — there is what someone typed. That is exactly why the list
  is **asked for** instead of deduced.
- `ISheet.get_TitleBlock()` returns **null** here and the title block still
  exists, made of sheet-format notes. An operation that tried only the first road
  would answer "no title block" about a drawing that has one — wrong, and
  confident. `cartiglio` tries both and declares which one answered.

The bench case closes the circle across two documents: it writes
`Descrizione = PROVA123` **on the part**, creates the drawing, and requires
`Descrizione` in `chieste_e_mostrate`. That field was added on purpose: with only
the list of empty ones, the case could have asserted at most "it is not among the
broken". **Not being among the broken is not working.** The red lives in the same
run — `Materiale` is asked for and was never written, so it must appear among the
empty ones; if that list were empty, the positive assertion would prove nothing.

**`verifica_interferenze` gives a volume, not a count.** 19200 mm³ predicted
before measuring and found to ±1 — a tolerance kept tight on purpose, because a
tolerance chosen for peace of mind stops measuring. Touching faces count as
interference or not depending on the job, so it is an argument, it defaults to
`false`, **and the answer states which way it was used**. The detection manager
is switched off in a `finally`, always: the exception arrives precisely when the
analysis goes wrong.

### The bridge was refusing the assemblies its own catalogue promised

`ControlliPreliminari` runs before the first step of every plan and admitted only
parts and drawings. Meanwhile the catalogue served to clients listed
`inserisci_componente`, `accoppia`, `sostituisci_componente` — and
`verifica_interferenze` needs an assembly too.

**None of the bench's 781 checks could see it: the bench calls the library, not
the bridge.** Two true measurements of two different things, and the large number
looks like it covers both.

The comment defending the old choice read *"no operation in the library knows how
to work on an assembly"*. True until 16 September; four of them existed by the
17th. **A comment that explains a choice is also what prevents it being
revisited** — whoever reads it finds a reason, and finding a reason feels like
being done looking.

So the defence is no longer a sentence. `OgniFamigliaDelCatalogoHaUnDocumentoAmmesso`
compares the catalogue's families against the document types the executor admits,
runs with no SOLIDWORKS and no licence, and **was born red on this defect**. It
reads the admitted types **from the source**, fails loudly if the line it parses
disappears, and treats an unclassified new family as **red, not green**.

Fixed with a third switch, `AncheSuAssiemi`, on in the bridge and off in the
add-in — same shape as `DocumentoSenzaFile` and `AncheSuTavole`, and watched by
the same script. The refusal message is now built from what is actually admitted
instead of naming yesterday's world.

### Also

- Section Z's 65 entries are renamed `Z-A1`, `Z-C3`…: **37 of their names were
  already in use** by other entries in the same TODO, 33 of them closed. "Do C3"
  led to a green entry from 14 September about something else. A new contract
  check refuses two different entries sharing a name — the exception being an
  entry's own "original trace, kept for reference", where the repeated name says
  something. It was watched going red on a duplicate put there on purpose.
- The refusal for a wrong document type is now **built from what is actually
  admitted** instead of a fixed sentence. The old one named parts and drawings
  even once assemblies were allowed: a message listing yesterday's world sends
  the reader looking for the problem where it is not.
- The contract suite goes from 139 to 145 checks.

## 0.64.0 — 17 September 2026 · library 0.97.0

### Two sheets, and the guard that could never fire

Sixty-five operations. `nuovo_foglio` and `attiva_foglio` — GT1, the first GT
entry that GT0 made testable.

`esporta_pdf` has always counted the pages of the PDF it writes: the guard exists
because a one-page PDF of a three-sheet drawing comes out with no error at all and
arrives in the workshop incomplete. That count was **always 1**, because there was
no way to make a second sheet.

> A guard that can never fire is not a guard: it is a line of code that consoles.

**The difficulty is not the sheet, it is the projection angle.** `NewSheet4` takes
`FirstAngle` as a mandatory sixth parameter — there is nothing to inherit.
Writing `false` because it is the default would give one drawing with **two
sheets mirrored differently**; neither gives an error, and whoever cuts the part
on sheet 2 cuts it reversed.

So the angle is read from the current sheet, passed through, **and then read back
from the new sheet**: passing it is not having it. If the two disagree, the
operation refuses rather than reporting done. Format and scale follow the same
rule — two sheets at different scales can be wanted, but not by accident.

`ActivateSheet` takes **the name**, not an index; an index would be the perfect
opportunity to work on the wrong sheet with no error. And after activating,
`attiva_foglio` asks the drawing which sheet it now has in front, because the
answer to the call is not the state of the document.

**What the bench case had to teach me.** I wanted both sheets' angles asserted in
one instant. That is not possible, and the reason is good: the state writes
`diedro` **only for the active sheet**, because reading a non-active sheet's
properties would mean switching sheets — a read that modifies. So the case reads
one sheet at a time, and the comparison between the two angles closes on the last
step. It is more honest than what I had planned.

---

## 0.63.0 — 17 September 2026 · library 0.96.0

### An exploded view that explodes nothing, and the millimetre next to the boolean

GA8 is blocked, and the block is worth more than the entry was.

What works: `CreateExplodedView()` returns True and creates the view,
`ShowExploded2(show, realName)` shows and hides it, `IsExploded(out name)`
follows, and the steps read back. What does not: the step that appears on its own
has **distance 0 mm**, and the real displacement — `GetTotalTransform(true)`
minus `GetTotalTransform(false)` — is **[0, 0, 0] mm**. Nothing separates.
`AddExplodeStep2` answers `NoComponents` with a component selected, in five
different ways of selecting it, and `AutoExplode()` answers True and adds
nothing.

**I had excluded `CreateExplodedView()` for a reason that sounded solid**: zero
arguments, and an exploded view has distances and components, so it must be the
button. The hole in it: an **empty** exploded view has nothing to configure. The
steps come afterwards.

> The good criterion is not "zero arguments": it is **whether it answers**.
> `EditExplodeParameters` and `ViewExplodeAssembly` return `Void` — those stay
> buttons. `CreateExplodedView` and `AutoExplode` return `Boolean`.

And the API said what was missing, because the `out` was read:
`AddExplodeStep2` without a view answers `Error = 2 =
swCreateExplodeStepError_NoExplodeView`. Not a silent `False` — the exact name of
the thing that was absent.

**Four probes, and the first three were the same probe.** They all worked on two
plates inserted and **never mated** — the very thing the
`inserire_non_e_accoppiare` case exists to shout about. `AutoExplode` explodes
along mates. The fourth mated first, and the result did not change.

> Three attempts that share the same defect are one attempt made three times, and
> the only way to notice is to write down what they have in common, not what they
> have that differs.

**Shipped anyway, and it earns its place on its own.** The assembly state now
carries `esploso`, `vista_esplosa`, `viste_esplose`, `passi_di_esploso` and
`esploso_sposta_al_massimo_mm`. `GetTotalTransform(true)` includes the explode
transform, so on an assembly shown exploded `origine_mm` would be the position
*in the exploded view* — plausible and wrong by ten centimetres, with no error.
The `inserire_non_e_accoppiare` case reads `origine_mm`, and now also requires
`stato.esploso = false`: **it is that false which makes the other reading
trustworthy.**

And `esploso: true` alone would have been a lie, because here it is true while
nothing moves. Hence the millimetre beside it: *a boolean that can be true while
the thing has not happened is not a measurement, it is a promise.*

---

## 0.62.0 — 17 September 2026 · library 0.95.0

### The bench could not turn the page, and that is why ten entries were stuck

Seven drawing operations have been in the catalogue for months, all marked
`Provata = true`. Declared bench cases for those seven: **zero.** Ten GT entries
open, all with the same shape — *write it, then prove it* — and the second half
never arrived. For a year the implicit explanation was "we haven't got there
yet".

The real explanation is one line. The bench ran every step of a case on the
**same** `IModelDoc2`, and `crea_disegno` opens a second document. The next step
got the part back, and refused with *"the active document is not a drawing"* —
the right message for a problem the person writing the case could not solve. So
nobody wrote the case.

> **An operation with no case is not proven: it is declared.**

It is exactly the shape of GA0: no assembly operation could have a case until the
bench learned to build an assembly. That became the entry *before* all the
others. Here the entry did not exist at all — it does now, GT0.

**The fix is not `IActiveDoc2`.** On this machine SOLIDWORKS is shared: "active"
is whatever *anyone* has in front of them, and one window is enough for the bench
to measure somebody else's document. The bench follows what the operation
**declares** — `Esito.DocumentoNuovo`, which `crea_disegno` has been filling for
months and which the bench was throwing away.

> **A declared change can be followed; a deduced one can only be suffered.**

Three details that separate "follows" from "follows well": it switches **only if
the step succeeded** (a half-failed `crea_disegno` can leave a wreck on screen);
it **says so in the diary** (a silent document switch makes a red three steps
later unreadable); and it tolerates the field being absent, because the bench
loads the library by reflection and an older one does not have it.

### `croci_di_centro`: written, measured, and **not shipped**

Still sixty-three operations. A drawing with three holes and no center marks
prints, looks complete, and in the workshop the one thing needed to drill is
missing: where the centre is.

`AutoInsertCenterMarks2` **returns `True` and inserts nothing.** Not an error —
a yes that does nothing, which is exactly the kind of thing this project exists
to catch, and this time the API is the one doing it. Three probes:

- **25 combinations** of `InsertType` × `InsertOption`, plus the version without
  the `2`: `True` every time, center marks 0 → 0, centerlines 0 → 0;
- the plate really has the holes (**3 cylindrical faces**) and the view really
  shows them (**3 circular edges**, counted with `GetVisibleEntityCount2`); every
  view, activated and selected: still 0 → 0;
- detailing preferences read, then switched on by hand
  (`swDetailingAutoInsertCenterMarks` was **off**, `…ForHoles` on, size 2.5 mm),
  `UseDocumentDefaults: false` with an explicit size, and `InsertCenterMark2` /
  `InsertCenterMark3` on each selected circular edge: **0 marks placed.**

The first probe had two defects of mine — it tried one view (the one without
circles) and never checked the holes existed. The second removed both. *A probe
that does not measure the thing is not evidence of the thing*, even when the
result later holds up.

So the operation is marked **`Provata = false`**: it does not reach the
catalogue and is offered to nobody. *An operation that has not been built and
measured is not exposed* — and that matters most here, where it "succeeds":
whoever called it would believe they had the centres. The code stays, because it
reports `croci_aggiunte = 0` rather than declaring done, and because the day the
missing piece is found there is nothing left to write.

**What was not ruled out:** that it depends on the workshop's drawing template.
A virgin template was not tried — ten minutes, and it separates "the API does not
work" from "it does not work with this template". Written next to the entry.

**What is gained anyway:** the drawing state now carries `croci_di_centro` per
view and a total, and the bench case asserts it is **zero**. The hole is now
visible — a drawing that comes out with no centres says so, and before, nobody
said it.

> The prediction in the case allowed 9 or 3. The measurement was **0**, which was
> neither. *When the measurement is none of the answers you predicted, the
> question was usually about something else.*

### Four numbers out of twenty were wrong, and nobody could have noticed

The bench README has a table — folder, how many cases, what they prove.
`lavorazioni` said 2 and had 7. `ripeti` said 3 and had 6. Four of twenty.

A number in a document does not fail; it ages. Same defect the README's operation
count had ("thirty-eight" when there were sixty), fixed the same way: not by
re-counting once by hand — *a count made by hand ages while you write it* — but by
having it re-counted every run. A contract test now sums the declared numbers per
folder and compares them with the files on disk.

Per folder, and summed, because one folder is now listed twice with two different
numbers: `casi/tavola/` holds both the hand-run plans and the declared cases.
7 + 1 = 8. Demanding that each line match alone would turn a correct table red.

### And a census that read fine and could not be called

`SolidWorks.Interop.gtswutilities.dll` ships in the redist. It loads, its
twenty-two interfaces enumerate, every signature of `IThicknessAnalysis` and
`ICompareGeometry` reads cleanly — and **none of it can be called here.** The
three coclasses are not registered in `CLSID` (GUIDs taken from the interop
itself, both registry views), no `gtcocsw*` binary exists on disk, and the add-in
is not among the registered ones. SOLIDWORKS Utilities is simply not installed.

> **That an interface can be READ does not prove it can be CALLED.**

Written down rather than worked around: the question "how thin is this shell at
its worst point" still has no answer in this catalogue, and the stated purpose of
the project is a watertight box for a boat.

---

## 0.61.0 — 17 September 2026 · library 0.94.1

### The catalogue as a Resource, and one name for where SOLIDWORKS lives

Three findings from one afternoon, and they are the same thing seen from three
sides: **two names for one thing become two things.**

**`SW_INSTALL_PATH` and `SW_REDIST` meant two different things** (W2.12 / H10).
The root of the installation and its `api\redist` subfolder. Neither was wrong.
What it did: whoever set the first one to build the library saw the bench fail to
find the interop, and nothing named the second variable until after the failure.

The root wins — `api\redist` follows from it, not the other way round; going up
from a subfolder means guessing a convention, and a guessed convention is where
plausible-and-wrong paths come from. `SW_REDIST` is still read, and still
second: *a variable that stops working without saying so is worse than one that
was never there.* Same treatment `OPENPDM_ADDIN` got. And the last `OPENPDM_*`
left, `OPENPDM_CATALOGO`, is now `RISCONTRO_CATALOGO` — old name still read.

**Three of the environment variables the bridge reads were not in the README**,
found while reading it for another reason. Two of the three were
`RISCONTRO_RADICE` and `RISCONTRO_SOLA_LETTURA` — the only two configurable
defences the bridge has, the ones that say where it must *not* write. The code
was there and worked.

> **A defence that is in the code and not in the README is a defence nobody turns
> on.** Whoever installs reads the table, not the sources.

They were invisible to a grep too: they go through a `Variabile(...)` helper, not
`GetEnvironmentVariable`. *Looking for one form means finding one form.* There is
now a contract test that collects both forms across the bridge and the library
and requires every name to appear in the README — with a line that checks it
found something at all, because a test that finds nothing passes forever.

It found **nine**, not eight. Counting by hand had given eight; the ninth is
`SW_INSTALL_PATH` itself, born half an hour earlier. *A count made by hand ages
while you write it; the one that re-reads does not.*

**The catalogue is now also an MCP Resource** (W2.12), not only a tool. The
content is the same; who decides, and when, is not. A tool is called by the model
when it works out that it needs it; a resource is attached by the person using
the client, before the model says anything.

One resource, `catalogo://operazioni`, plus a URI template,
`catalogo://operazione/{nome}` — not one resource per operation. It is the same
two-level catalogue that governs the tools, said in the vocabulary of resources.
`subscribe` and `listChanged` are deliberately not declared: the catalogue is a
file read at startup. *A declared capability that is never exercised is a promise
someone will wait for.*

Two doors on one thing become two things the day one of them gets written
separately. So: `Risorse` knows nothing about the catalogue — it takes the same
`Catalogo` object the tools use and calls the same two methods, and a contract
test asks the resource and the tool and compares the two texts byte for byte.

The library version moves for comments only. It is the one change that could not
alter behaviour — and *"it's only comments" is exactly what gets said right
before it wasn't.*

---

## 0.60.0 — 17 September 2026 · library 0.94.0

### `sostituisci_componente` — the mates survive, and they are broken

"The lid has to be remade" is the commonest gesture in design, and GA7 named the
danger it carries: *if the API replaces the document but does not keep the
mates, you get the typical defect again — different part, no error.*

**The signature existed. It was on the other interface.** GA7 sat at *"not
estimable"* because the API census had looked for `ReplaceComponent` among the
170 methods of `IComponent2` and concluded *"no dedicated signature is listed"*.
It is on `IAssemblyDoc`: `ReplaceComponents2(FileName, ConfigName,
ReplaceAllInstance, UseConfigChoice, ReAttachMates)`. Third time in this project
that *a name tells you what a method is called, not which object owns it* — and
the expensive step is not failing to find it, it is turning "I did not find it"
into "it does not exist".

**What the bench measured, and it is not what I predicted.** Two components, one
coincident mate, lid replaced with a 6 mm one:

| | before | after |
|---|---|---|
| components | 2 | 2 |
| mates (endpoint count) | 2 | 2 |
| mates lost | — | **0** |
| `GetWhatsWrongCount()` | **0** | **2** |

The first three rows say "all good". The fourth says those mates are **in
error** — they pointed at faces of the old part. SOLIDWORKS keeps them in the
tree and marks them red.

> **A broken mate is worse than a missing one: missing is visible, broken looks
> like it holds.** Surviving is not working.

So the operation reports **two** before/after pairs, not one —
`accoppiamenti_prima/dopo` and `problemi_prima/dopo` — and its loudest branch is
the one where the mates survive and the problem count rises. It names the remedy
too: delete those mates and put them back with `accoppia`. *A warning with no
remedy is one people learn to ignore.*

**`ReAttachMates` is not a knob.** With `true`, and a real mate to reattach,
`ReplaceComponents2` raises `RPC_E_SERVERFAULT` — measured in four conditions:
assembly saved and not, new file open and closed. With `false` it always passes.
So `false` is hard-coded and what it costs is written down: *an argument that
changes nothing is worse than an argument that is not there, because whoever
sets it believes they decided something.*

**And I was wrong twice on the way here, which is why the bench case is named
`il_coperchio_rifatto_tiene_i_vincoli_ma_rotti` and carries its own history.**
The probe that found `RPC_E_SERVERFAULT` never counted mates — it printed
component paths — and from "the call succeeds with `false`" I deduced "the mates
are discarded". They are not. **A probe that does not measure the thing is not
evidence of the thing, not even when it succeeds.** Then, having measured that
the mates survive, I concluded they were fine. They are not.

Two more traps avoided in the same operation: `IComponent2` does not cast to
`IEntity` — it has its own three-parameter `Select4`, and the third parameter is
`ShowPopup`, which must be `false`; and `swReplaceComponentsConfiguration_e` has
a `ManuallySelect` value that opens a modal dialog. **Three calls in one
operation that know how to open a window**, and from here nobody can press OK.

Sixty-three operations. README updated to match — the count is checked by a test,
not by hand.

---

## 0.59.0 — 17 September 2026 · library 0.93.0

### `accoppia` — and the unknown that was blocking it did not exist

GA4 was marked blocking and blocked, on this: *"which selection marks are needed,
reflection does not say"*, with an 8-10 hour estimate hanging off it.

**No marks are needed.** Select the two entities with
`IEntity.Select4(append: true, null)` and call `AddMate5`: it answers
`swAddMateError_NoError`. That was measured inside a probe written for an
entirely different question — *probes are worth reading whole, not only for the
line you went looking for.*

Sixty-two operations now. Coincident, concentric, parallel, perpendicular,
distance, tangent; aligned or anti-aligned; `distanza_mm` required for distance,
because there is no reasonable default for how far apart two faces are.

**How the faces are named, which was the real question.** The state gives
`C1..Cn` for components and **no identifiers for their faces**. Making some would
mean numbering hundreds of faces, and they would be **positional** — to be
re-read after every change. So faces are named by **direction**, reusing the
vocabulary this project already has for parts: *"the top face of C1 on the bottom
face of C2"*. It does not cover everything — an oblique face has no direction —
and it **says so** instead of guessing. The directions are the **assembly's**,
not the part's: a rotated component's "top" face is the one facing up *in the
assembly*.

**`C1` means one thing.** The ordering that assigns it now lives in one place,
`StatoDocumento.ComponentiInOrdine`, used by the state **and** by `accoppia`. Two
copies of that rule and one of them drifting would make `C1` mean two different
things — and mate the wrong component with nothing raising an error.

**The measurement is not the return value.** `AddMate5` returns a `Mate2` *and*
writes an `ErrorStatus`, and neither says the mate exists. The mate counts on
both components are compared before and after, re-reading the handles by name
after the rebuild.

### The entry's own success criterion had to be rewritten

GA4 asked for: *"an insufficient mate must leave `GetRemainingDOFs > 0` on that
axis"*. **That is not reachable** — `GetRemainingDOFs` answers *unavailable* in
every condition tried.

So the case `un_accoppiamento_non_basta` does the nearest thing that is **true**:
it proves the mate exists (the count rises, `appoggiato` turns false) **and** that
the bridge says out loud it does not know how much freedom is left.

A coincident mate removes three degrees of freedom out of six: the lid can no
longer lift off, but it can still **slide in the plane and spin about the
normal**. If that sentence ever disappears from the answer, the bridge would be
letting a reader believe one mate is enough — and an assembly that looks
assembled without being assembled is the exact defect this project chases.

715 bench tests, 0 failed, 550 expectations verified. 128 contract checks.

---

## 0.58.0 — 16 September 2026 · library 0.92.0

### Assemblies: the bench can build one, the state can read one, and one operation can touch one

Zero of sixty operations worked on assemblies. Three things landed together,
because each is useless without the others.

**GA0 — the bench can prepare an assembly.** Until today `NuovaParte()` was the
only way the bench produced a document, and it used the part template. That meant
**no assembly operation could ever have a test case**: all nine assembly entries
could have been written, made to work by hand, and left with nothing to notice
when they broke. *An operation without a case is not an operation, it is a
demonstration.*

A case can now declare `"assieme": { "componenti": [...] }`. Each component is
built with **the same operations the cases use** — no separate little language to
keep in sync — then saved into `Banco\lavoro`, reopened, and inserted.
`AddComponent5` takes a path, so a component has to exist on disk first; that is
seven steps, and each one fails its own way. All of them report through
`ErroreDiBanco`, never `Segna(false)`: a failed *preparation* is not a failed
*test*, and calling it one would suggest something had been measured.

A component marked `"da_inserire": false` is built and opened but **not**
inserted — that is how a case can test the insertion itself instead of measuring
the preparation's work.

Three self-checks guard it, and the third is the one nobody writes: after the
case, `lavoro` must be empty again. *A case that passes because a file from the
previous run was still there is not a case, it is a memory.*

**GA1 — `guarda_il_pezzo` describes assemblies.** It used to answer, for any
assembly, one line: *«documento di assieme: l'assistente non lo modifica ancora»*.
True, and blinding: the state is the only thing the model sees, so it
could not know how many components there were, what they were called, which were
suppressed. It could not even refuse with any understanding of what it was
looking at.

Now: component list (uid, name, path, configuration, suppression state, fixed),
counts of components/suppressed/lightweight/fixed, and the active configuration.

**The component uids are stable, and not by accident.** Face and edge uids are
*positional* — they renumber by area on every read, and that is the most
expensive trap in this project. Components are different: `Name2` is unique
within an assembly, so the list is **sorted by name** and `C1..Cn` stays put
between reads. Trusting `GetComponents`' own ordering, which is documented
nowhere, would have repeated the face trap where it was avoidable.

`GetComponents(false)`, not `true`: with `true` a subassembly shows up as *one*
component with nothing inside, and the model would read "2 components" on an
assembly of thirty — every conclusion after that plausible and wrong. The
top-level count is reported separately, because that is the other real question.

The note did not disappear, it changed kind: `cosa_non_so_ancora_fare` now says
**what** is missing. A model reading "I can see four components but I cannot mate
them" knows what it may ask; one reading "I do not modify this" can only stop.

**GA3 — `inserisci_componente`.** Sixty-one operations now. The thing it does
beyond inserting is the point, and the API census wrote it down long before
anyone measured it: *"AddComponent5 places without mating — four loose bodies
that look like an assembly."* Inserting almost always succeeds. The part appears,
the tree fills, the rebuild is clean, and **nothing is constrained.**

So the operation reports whether the component is `fisso`, how many
`accoppiamenti` it has, and — the conclusion — whether it is `appoggiato`: not
fixed and with zero mates means it is just sitting there. The warning travels in
the answer, not only in the catalogue description, because whoever reads the
answer may never have read the description.

**Fixed components are excluded from the "is it assembled?" count** (GA5), and
that is not an edge case: **SOLIDWORKS® fixes the first component inserted into
an assembly** — measured, `base-1 fixed=True` against `coperchio-1 fixed=False`.
Every assembly that exists has at least one nailed-down part, so counting them as
constrained would have been wrong *always*, not occasionally, and in the
comfortable direction. A fixed part does not move because someone nailed it, not
because anyone mated it: two different things, and the state keeps them apart.

**What it does NOT report, and says so.** How *much* freedom is left. The first
version reported `gradi_di_liberta_residui` from
`IComponent2.GetRemainingDOFs`, believing the return value was a count and that
a freshly inserted component would give six. The bench case caught it on its
first run: it measured **2**.

**That return value is not a count. It is a status** — `swRemainingDofs_e`:
`Restricted=0`, `Unrestricted=1`, **`Unavailable=2`**, `Failed=3`,
`RootComponent=4`. The 2 meant *unavailable*. Six was never a possible return.
Meanwhile the bridge was telling the model *"it still has 2 degrees of freedom
out of 6"* — invented number, invented unit, entirely plausible sentence.

What actually exposed it was not the wrong value but the **comparison**: the same
2 comes back for a **fixed** component and for a **free** one. The number did not
distinguish the two things it was there to distinguish.

Four conditions were tried and all return *unavailable*: freshly built assembly,
component selected first, assembly saved to disk, and **after a real coincident
mate** — `AddMate5` answered `swAddMateError_NoError`, so that last condition
genuinely holds. (A trap in passing: in `swAddMateError_e`, **`NoError` is 1**
and `ErrorUknown` is 0. `if (err != 0)` would have called a successful mate a
failure.)

So the count is not obtainable here, and rather than wait for it the bridge
stopped publishing a false one. `IsFixed()` works and genuinely separates the
nailed-down first component from the rest; `GetMates()` counts. The state also
carries `cosa_non_so_dei_vincoli`, which warns that "constrained" here means
*"has at least one mate"*, not *"cannot move"* — one mate still leaves five
degrees of freedom out of six.

*A measurement you cannot have must be declared absent, not approximated. Saying
"I don't know" costs one line; a plausible number costs whatever someone builds
on top of it.*

It refuses before it acts, twice: not an assembly (says which type it found), and
component not open (says which documents *are* open — `AddComponent5` answers
`null` on a closed file without saying why, and a refusal that does not name the
alternative forces guessing).

And it does not believe the return value: it counts components before and after.

**Where the component actually ends up, and a third trap.** The position is read
from `GetTotalTransform`, and the index layout was not assumed: the case inserts
at z = 30 mm and reads the translation back. It found more than it was looking
for. The **Z matched** — so indices 9, 10, 11 are the translation, question
answered — but Y came back **-2**:

> **`AddComponent5(x, y, z)` positions the component's bounding-box CENTRE, not
> its origin.**

A part extruded in one direction — which is what `estrudi_rettangolo` does, and
what any ordinary extrusion does — has its origin on a face, not at mid
thickness. Ask for the centre at z = 30 and the origin lands at y = -2: half the
4 mm thickness. **Confirmed by prediction, not by fitting**: a second part 10 mm
thick, requested at zero, was predicted to read (0, -5, 0) *before* the probe ran,
and read (0, -5, 0).

The fix is in the **name**, not the code. The field was `posizione_mm`, which
invited comparison with the point that was *asked for*, and the two almost never
coincide. It is now **`origine_mm`**, and `x_mm`/`y_mm`/`z_mm` say "bounding-box
centre" in the catalogue. Neither number was wrong; they were two different
things and nothing said so — the defect that raises no error in its subtlest
form: not a false value, but **a name promising a meaning the value does not
carry**.

**This has now run against SOLIDWORKS®, and the first run was worth it.** The
assembly case found three things in one go: a status read as a count, an empty
collection returned as null, and a field name that promised the wrong meaning.
All three were written by the same person who wrote the case — which is the
argument for writing the case.

### A source file that isn't in its `.csproj` doesn't exist

`OperazioniAssieme.cs` was written, compiled fine in a scratch check, and failed
the real build: it was not listed in `Riscontro.Disegno.csproj`. Two errors,
thirty seconds to fix — because something referenced it.

**The dangerous case is the other one.** A new class nobody references yet — an
operation written but not yet wired to the dispatcher — is simply **not
compiled**, and the build stays green. You write a file, everything passes, and
that file is not in the program. No error, anywhere. It is the defect that raises
no error, applied to the *project* instead of the part.

A contract test now walks `Riscontro.Disegno`, `Ponte`, `Strumenti`, `Mcp`,
`Banco`, `Prove` and `Installer` and checks every `.cs` appears in its project
file. Sixty-eight files, all present. Seen firing: a throwaway file dropped in
`Riscontro.Disegno` turns it red and names the file and the project.

It is a test and not a script on purpose. A script has to be remembered, and that
is exactly how the `pre-push` hook came about: the check existed, running it
didn't.

### The data comes out machine-readable too — and no `outputSchema`, deliberately

Everything came out as **text**: document state, plan measurements, the
catalogue. A language model reads that fine; a program has to re-parse JSON that
was JSON before being put inside a string. `structuredContent` is the field MCP
provides for exactly this, and it costs one line. The text stays — clients that
read only `content` are the majority — and the data comes out **even when the
plan failed**, which is when a program most needs it.

**`outputSchema` is not declared, and that is the decision, not an omission.**
The spec is firm: a tool declaring `outputSchema` **must** return conforming
`structuredContent`. The shape of the state changes with the document — an
assembly has `componenti`, a drawing has `fogli`, a part has `facce` — and every
new field (eight were added to assemblies today) would become a promise to keep.
**A partial schema is worse than none**: a validating client would reject correct
answers. So: the data without a promise about its shape, and a contract test that
fails if any tool ever declares an `outputSchema` before that shape is actually
settled.

### The same plan twice

A client that never receives the answer — network, timeout, restart — resends the
request. That is what every client does, and this bridge's own annotations
already said `disegna` is **not** idempotent: re-running does not restore the
part, it **makes a second hole**. They said it, and nothing stopped it.

The bridge now remembers the fingerprint of the last successful plan, which
document it ran on, and how the part looked **right after**. It refuses only when
all three match.

**Both conditions are needed.** The plan alone is not enough: identical plans on
documents in different states do different things and may both be wanted. The
state alone is not enough either: a document that has not changed is no reason to
refuse. Together they describe exactly one situation — the resend.

**No flag to bypass it.** *An argument that disables a defence is an invitation to
disable it*, and whoever hits it has every incentive to retry with it on — so the
defence disappears precisely when it was needed. The refusal says what to do
instead: if the answer never arrived, call `guarda_il_pezzo` (the current state
**is** the result); if you really want it again, change something or use
`annulla`, so it is a choice.

**The state summary is deliberately not a hash of everything.** The full state
changes even when the part does not — a uid renumbering, a face crossing the
display cap — and comparing all of it would report "different" every time,
**switching the check off without anyone noticing**. Volume, bodies, feature
count and title change when the *part* changes, not when the way it was read
changes. *The most common way to disable a check is to make it too sensitive.*

**And a bug found while writing it:** `misura` runs through the same body. But
measuring twice is legitimate — it is the normal case — and since a measurement
does not change the document, the state after equals the state before, so the
guard would have **refused the second measurement**. Worse than inconvenient:
`misura` declares `idempotentHint: true`, and a client reads that line to decide
whether it may retry on its own. Refusing would have made that declaration **a
lie in the protocol** — the very category of defect `misura` exists to avoid. The
guard now applies to `disegna` only.

### The bridge tells the client what it is doing

`stderr` stays — it is the only place that works **before** a client exists
(attaching to SOLIDWORKS®, a catalogue that will not load, `--prova`), and it is
what you read when nothing starts. What is added is `notifications/message`, with
the `logging` capability declared and `logging/setLevel` served.

What this gains: the agent's own lines — *"I re-anchored face F3, moved 2 mm"* —
are exactly what explains why a part came out the way it did, and they were going
to a terminal nobody watches.

The channel only opens once the message loop is running; before that, a line on
stdout with nobody reading JSON is just noise that breaks whoever connects next.
And sending the notification is wrapped in a `try` that never rethrows: *a report
that makes the reported thing fail is worse than silence.*

**An unknown level is an error, not a shrug.** Accepting and ignoring it would
leave the client believing it had raised the threshold while messages keep
arriving — or, worse, believing it hears everything while hearing nothing.

One existing test was made **robust, not loosened**: "a notification gets no
reply" read whatever line came first; it now skips **only**
`notifications/message`, which is ours and recognisable. Widening it to "ignore
anything without an id" would have switched it off.

### One bridge at a time on a document

Two MCP clients open at once is ordinary, and until now nothing stopped two
bridge processes from executing plans on the same document. The precedent is
already paid for: on 14 September a 314×274×52 block was extruded **into someone
else's part** — no error, no warning, the plan succeeded on the wrong thing. That
was fixed by holding the `IModelDoc2` reference instead of asking for the active
document. Within one process that is enough. Between two processes it protects
nothing: two different COM objects looking at the same file, each believing it is
alone.

A system mutex, keyed by the document, now decides whose turn it is. The refusal
says which document and why.

**A mutex, not a lock file**: a file outlives whoever made it, so a bridge that
dies leaves the document unusable until someone deletes it by hand. Windows
releases a mutex however the process ends. *A lock that stays shut after the one
who shut it has died is not a protection, it is a fault.* `AbandonedMutexException`
is not an error either — it is Windows saying the previous holder did not close
the door, and the turn is ours.

**Zero wait, deliberately.** Waiting would hang the client with no explanation,
and the bridge already has a problem with calls that never return. An immediate
refusal that explains lets the reader decide whether to wait.

**The key is a SHA-1, not the path.** Mutex names reject backslashes and have a
length limit, and a truncated path would make two documents in the same deep
folder collide — two different parts blocking each other, a defect that only
shows up once someone is actually working. Seven contract tests cover the keying,
including that case; that two processes exclude each other is Windows' job, not
something to test here.

**The limit, written down rather than discovered**: the name lives in `Local\`,
the current Windows session. It covers the real case — two MCP clients on one
desktop — and not two different users or a service. `Global\` would, but it needs
privileges a bridge launched by an MCP client does not have, and a lock that
sometimes cannot be taken is worse than one whose reach is known.

---

## 0.57.0 — 16 September 2026 · library 0.91.0

*First entry in English, per the decision recorded at the top of this file.*

### `misura` — a sixth tool, so the client stops asking for confirmation to read

MCP annotations travel on `tools/list`: they sit on the **tool**, and they are
sent before any call exists. With only `disegna`, a client read
`destructiveHint: true` and asked for confirmation even for
`proprieta_di_massa`, which does not move a single cubic millimetre. **Someone
who confirms ten times in a row stops reading what they are confirming**, and a
confirmation that has become a click no longer protects anything.

This could not be fixed inside `disegna`: at annotation time nobody knows which
of the sixty operations will arrive. So `misura` takes the same plan shape and
accepts **only** the `verifiche` family — the six operations that read and
nothing else — and declares `readOnlyHint: true`, `destructiveHint: false`,
`idempotentHint: true`. Idempotent for real, not out of politeness: measuring
twice gives the same answer twice because nothing changed in between.

**The gate runs before the plan starts, not during it.** A plan of three
measurements plus one extrusion, stopped at the fourth step, would already have
measured three times: whoever read the answer would see three good measurements
and an error, with no way to know the part survived only by luck. Every step is
checked first; if one is not a `verifiche` operation, nothing runs at all.

**`identita` is deliberately not read-only.** `scrivi_proprieta`,
`crea_configurazione` and `attiva_configurazione` write to the document.
Declaring them harmless is the direction in which being wrong costs most.

Four contract tests guard this, including the red one: `misura` must **refuse**
`estrudi_rettangolo`, and the refusal must name the tool that does do it. A tool
that declares itself read-only and then accepts an extrusion is not a
convenience — it is a lie in the protocol, and the client has already decided
not to ask for confirmation on the strength of that declaration.

### `title` where new clients look, and an error that says what does exist

`title` sat only inside `annotations`, where older revisions of the spec put it.
It is now **also** on the tool itself, where a newer client looks — written in
both places, because choosing one would mean half the clients showing
`guarda_il_pezzo` to a person instead of «Guarda il pezzo aperto».

The unknown-tool error now lists the tools that exist, and says the thing the
model actually gets wrong: **the sixty operations are not tools** — they go to
`disegna` (or `misura`). Before, it said only "I don't know x", and a model that
had guessed a name could only guess again.

The names in that message are written by hand — rebuilding sixty JSON schemas to
print six words makes no sense — so they can drift. A contract test asks for a
tool that does not exist and checks the message names **every** tool
`tools/list` just declared. *An incomplete list is worse than no list.*

### SOLIDWORKS® going away mid-plan now says so

`Aggancia()` records the PID (`ISldWorks.GetProcessID`, verified by reflection
against this installation's interop: zero parameters, returns `Int32`), and it is
checked before a plan and again when one stops.

**Not `Process.GetProcessesByName("SLDWORKS")`**: more than one session runs on
this machine, and searching by name finds *a* process, not *the* process this
bridge is talking to. A check that watches the wrong process answers "alive"
while its own is dead — the defect that raises no error, inside a check written
to catch it.

**The half that matters is the one after, not before.** Before catches "it was
already closed". After catches **closed mid-plan**, where the error that surfaces
is whatever the first call to find a dead reference said — a null reference, an
HRESULT — sending the reader to look for a fault inside the operation that was
running. The message now says SOLIDWORKS® closed, after how many steps, that the
document was not saved, **and keeps the original message**: if the two ever
disagree — SOLIDWORKS alive and a real error — discarding the original would hide
the thing to fix.

If the PID cannot be read, the check stays quiet and the bridge works as before.
A new defence that becomes a new cause of refusal is a regression, not a defence.

### `pre-push` hook: the publishability check now runs when it matters

`controlla-pubblicabilita.ps1` already existed and was already correct. On
14 September it reported "clean" on the working tree one minute before the first
push, while the **history** still carried a network path in 155 places across 41
commits. The control did not fail — **nobody ran it**.

Now git runs it. `Controlli/ganci/pre-push` blocks the push on a non-zero exit;
`Controlli\installa-ganci.ps1` points `core.hooksPath` at the versioned folder
(once per clone), so a hook fixed tomorrow applies to everyone immediately
instead of rotting as a copy inside each clone. Override with
`git push --no-verify` — done by hand, and known to have been done.

Seen firing: a file carrying a private-network address, staged on purpose, makes
it exit 1; removed, it exits 0.

*(And writing this entry set it off again — the first draft quoted the address
itself. The check caught its own description. Left as a note rather than an
anecdote: **a document about what must not leak is a place where it leaks**, and
that is why `CHANGELOG.md` is exempt for drive letters only, never for
addresses.)*

**Two real false positives, fixed narrowly.** `CHANGELOG.md` and
`Prove/ProveCatalogo.cs` name `O:\` because their job is to explain this very
check. The exception is a **separate list** that applies only to the `O:`/`W:`
patterns and leaves usernames, session paths and private addresses forbidden on
those same files. Putting them in the general exemption list would have been the
hole that the script's own comments warn against.

### The README claimed thirty-eight operations. There are sixty.

Nothing could notice: documents do not compile and nobody rereads them until
they matter to someone who was not there. **This is the defect that raises no
error, applied to documentation** — the README looks right, reads well, and is
false, and its reader has no other source.

Fixed, and a contract test now reads the number back out of the README and
compares it against the catalogue. It only recognises two shapes
(`<number> operazioni`, `operazioni di disegno sono <number>`) so that "five,
not sixty" — which is about *tools* — does not set it off: a control that shouts
at a correct sentence is one people learn to ignore.

### `QUICKSTART.md`, `CONTRIBUTING.md`

A six-step guide for the person who has to *use* this, with **"what it does not
cover" as a list of names** — assemblies, sheet metal, surfaces, sweep along a
free path, loft between arbitrary profiles, DXF/DWG to 3D, fill and table
patterns — so the limit is found by reading rather than by drawing something and
getting a part that is quietly not what was asked for.

Writing it surfaced one defect: step 2 told the reader to verify
`Installa.exe.sha256`, **and nothing produced that file**. An instruction that
cannot be carried out is worse than no instruction, because whoever follows it
concludes they have verified. `build.ps1` now writes it, in `sha256sum` format.

`CONTRIBUTING.md` records the policy *before* the first pull request arrives:
issues open, pull requests not merged until a CLA exists, and no support
commitment. Written afterwards it would not be a policy — it would be a personal
answer to one person, and it would read like one.

---

## 0.56.0 — 16 September 2026 · library 0.91.0

### `weldment_profiles` — the operation the catalogue was asking for and that did not exist

Catalogue at **60 operations**. Found by a TODO audit done with seven agents
in parallel, and verified by hand before trusting it.

`structural_member` and `rectangular_frame` say, in their own description: *"FIRST
CALL \"weldment_profiles\" and copy from it \"profile_path\" and \"configuration\" LITERALLY"*.

**That operation did not exist.** The name appeared four times in
`catalog.json` — all four *inside the text* of whoever was asking for it — and zero
times among the executable names. The MCP tools are five and none of them lists the
profiles. So the model was reading an instruction that led nowhere, and the only
thing it could do was **make up** a `.sldlfp` path and a measurement name.

And making up the measurement raises no error — the comment in
`WeldmentOperations` says so, written well before this audit: *"if the measurement
is not there, SOLIDWORKS does NOT fail: it uses the profile's active configuration.
The part comes out with a different wall than the one confirmed, the rebuild is
clean, and nobody notices until the workshop"*. A check that catches it **already
existed**, and it is good; but it can only fire *after* someone has already guessed.

Now the operation exists: it lists the library's profiles and, for each one, the
measurements written exactly as they must be copied. It reads the `.sldlfp` files
with `ISldWorks.GetConfigurationNames(path)`, **without opening documents**. The
folder is overridden with `PROFILI_SALDATURA`, the same variable the bench already
uses.

The bench case closes the loop: it reads the measurement in the first step and
copies it back into `structural_member` in the second. It proves that the two
pieces of the catalogue *talk to each other*, not just that they cite each other.

---

## 0.55.0 — 16 September 2026 · library 0.90.0

### GP17 — `circular_sweep` and `loft_two_circles`, and the loft that is not a cone

Catalogue at **59 operations**. The first two that make a **non-straight** shape:
a cable-pass grommet, a handle, a reducer between two sections.

**The profile is a number, not a sketch.** `InsertProtrusionSwept4` has
`CircularProfile` + `CircularProfileDiameter`: with those, the profile sketch is
not needed. And the path is **a list of points**, drawn by the operation in a 3D
sketch — not a sketch to be named, because in the document state sketches have no
identifier and no operation leaves a reusable one lying around. If the path had to
be "a sketch", the operation would not be callable by anyone. It is the same
reason `plane_three_points` takes coordinates and not vertices.

**The marks, measured:**

| | 0 | 1 | 2 | 4 |
|---|---|---|---|---|
| sweep, path | ok | null | null | **ok** |
| loft, profiles | ok | **ok** | — | null |

4 is used for the path and 1 for the profiles. Zero works for both because it
means "no mark", but relying on it would make two selections with different roles
indistinguishable the day a third one is added.

**The straight sweep is exact.** Ø20 over 100 mm of path: **31415,927 mm³**
against π·10²·100 = 31415,9265. Not "close": equal to the seventh digit. The
operation does the arithmetic and refuses if it does not match. On a path with
elbows the arithmetic **does not** hold — at the elbows the material overlaps —
and then the theoretical value is declared as an *upper bound*, not as the answer.

**And a loft between two circles is not a cone.** Ø40 and Ø20 at 50 mm:

    frustum-of-cone formula      36.651,91 mm3
    measured loft                 36.650,61 mm3
    difference                         1,30 mm3   (0,0035%)

The surface of a loft is a **spline approximation** of the conic. It is small and
it is systematic, always on the low side: whoever compares a loft with the formula
and does not know this chases a defect that is not there. The bench case builds
**the same cone frustum with `revolve`** next to the loft and expects the sum of
the two bodies to be 73302,53 — that is, 36650,61 + 36651,91. If the formula were
the one that was wrong, the two bodies would weigh the same and the sum would be a
different number. It is the difference between "my numbers do not add up" and "I
know which of the two is approximate".

---

## 0.54.0 — 16 September 2026 · library 0.89.0

### GP9 (in part) — `plane_three_points`, and the third row that is not the third column

Catalogue at **57 operations**. `create_plane` could only make the *offset* plane
from one of the three standards: for a tilted hole or an oblique face there was no
plane to draw on.

**Three coordinates, not three vertices.** `CreatePlaneThru3Points3` wants three
selected vertices; `CreatePlaneFixed2` takes three triplets of numbers. The second
is used, and not by taste: **the document state does not list vertices** — faces
and edges have an identifier, vertices do not — so "three selected vertices" is
something the language model has no way to ask for.

**The normal is the third ROW of `Transform.ArrayData`, not the third column.**
Measured on three planes of known normal:

| points | expected normal | third row |
|---|---|---|
| (0,0,20) (100,0,20) (0,100,20) | (0, 0, 1) | (0, 0, 1) |
| (0,0,0) (100,0,0) (0,0,100) | (0, −1, 0) | (0, −1, 0) |
| (0,0,0) (100,0,0) (0,100,100) | (0, −0,7071, 0,7071) | (0, −0,7071, 0,7071) |

On the first two **the third row and the third column give the same result**: a
probe built only with axis-aligned planes would have picked one at random, with a
one-in-two chance of being wrong forever. On the third, the column gives
`(0, 0,7071, 0,7071)` — rotated by 90°. *A case where two wrong values give the
same result tables nothing*: the oblique plane is in the probe on purpose, and it
is in the bench case for the same reason.

The operation **computes the normal from the three points and compares it with
what the plane claims to be**, refusing if they diverge: "one more feature in the
tree" would say the same of a crooked plane, and on a crooked plane you draw on it
without noticing. And three collinear points are refused **before** calling,
because the reason is known without having to ask — SOLIDWORKS answers `null` and
does not say so.

`UseGlobal` tried as `true` and `false`: the same transform, number for number. It
is not exposed.

> **Rectified in Z-C21.9 (finding L8-3):** that measure could not tell the two
> branches apart. All three point sets had P1->P2 along the global X axis, and
> there the two coincide by construction. IModelDoc2~CreatePlaneFixed2.html,
> Remarks: with `true` the plane is not reoriented, its own X axis is turned to
> the projected global X. So the normal is the same either way and the plane's
> X axis is not; nothing in the catalogue can see that yet (comment in
> `PlaneOperations.cs`).

**The other two parts of GP9 remain open, with the reason written down**: no
operation in the catalogue consumes a coordinate system or a reference point, and
an operation whose effect is not observable by any other is exactly what this
project refuses to add.

---

## 0.53.0 — 16 September 2026 · library 0.88.0

### GP16 — `check_overhangs`, and the normal that was already right

Catalogue at **56 operations**. What does not print without supports: a wall that
overhangs into empty space beyond a certain angle comes out as lost filament, and
SOLIDWORKS says nothing — the part is sound, the volume is right, the geometry is
closed.

**The TODO entry's risk, measured, turned out to be reversed.** The entry warned
that the normal of `ISurface` is that of the underlying surface and must be
flipped when `FaceInSurfaceSense()` is false, and that getting it wrong gives "an
analysis that declares supported exactly the face that overhangs into empty
space". On a block about which everything is known (100×100×20, six faces with
known outward normals):

| | |
|---|---|
| `IFace2.Normal` points out of the material | **6 times out of 6** |
| `Normal` agrees with `PlaneParams` | 1 time out of 6 |
| `FaceInSurfaceSense() = true` | on the **5** where they are *opposite* |
| `FaceInSurfaceSense() = false` | on the **1** where they agree |

Two things. **`IFace2.Normal` is already right** and needs no correction at all —
it is the one the project uses everywhere, and now it is measured instead of
hoped for. And **the rule "if the sense is false, flip it" would have been wrong
the other way round**, not on half the faces but on five-sixths of them.

**Curved faces are tessellated.** The underside of a horizontal cylinder overhangs
into empty space on its lower half and not on its upper half: one normal per face
is not enough. `GetTessTriangles` answers **`float[]`, not `double[]`** — the
wrong cast silently returns null. The triangles' facing is decided by the
winding, which is a convention: **it is checked against `IFace2.Normal` on every
flat face**, where the answer is known, and if it does not match the operation
refuses instead of answering with a reversed analysis.

**The angle is counted from vertical**, as in slicers: a straight wall is 0, a
horizontal ceiling is 90, "threshold 45" means beyond 45. It is not the angle
with the plane, which is the complement — two numbers that look alike and say
the opposite.

**And what rests on the build plate is not an overhang.** The bottom face points
straight down — 90°, the worst case — but it rests on the machine's plate.
Counting it would mean declaring every existing part unprintable.

---

## 0.52.0 — 16 September 2026 · library 0.87.0

### GP15 — `draft`, and two APIs that cannot be told apart by reading

Catalogue at **55 operations**. The draft angle: the walls of a part moulded in a
die must lean by a degree or two, or the part will not come out of the mould.

**Four families for the same command, and two have an identical signature.**

    IFeatureManager.InsertMultiFaceDraft(Angle, FlipDir, EdgeDraft,
                           PropType, IsStepDraft, IsBodyDraft) -> Feature
    IFeatureManager.DraftXpertChange    (Angle, FlipDir, EdgeDraft,
                           PropType, IsStepDraft, IsBodyDraft) -> Feature
    IModelDoc2.InsertMfDraft (Angle, FlipDir, IsEdgeDraft, PropType) -> void
    IModelDoc2.InsertMfDraft2(... , StepDraft)                       -> void

The first two have the same parameter names, the same types and the same return
type: **they cannot be told apart by reading**. Measured, with the exact same
selections and the exact same arguments, `InsertMultiFaceDraft` makes the draft
and **`DraftXpertChange` returns `null`**. The other two return `void` and cannot
even be checked.

**The marks, and the defect inside them.** Neutral face mark 1, faces to draft
mark 2. On the same 100×100×20 block, a 5° draft:

| marks | result | sides | volume |
|---|---|---|---|
| neutral 1, faces 2 | `Sformo1` | **5,00°** | 200000 → 207080,7 |
| neutral 2, faces 1 | `Sformo1` | **0,00°** | 200000 → 243744,3 |

The second row is the defect: swapping the marks, the feature **is still
created, is named the same, gives no error at all** — and it has drafted
different faces, by a different amount.

**And the feature's angle is not a measurement.** In both cases
`IDraftFeatureData.Angle` reads back **5°**: what was asked, not what happened —
even where the faces stayed at zero. So the operation counts the faces **of the
part** that sit at the right angle to the neutral face, before and after, and
refuses if not enough of them have appeared. The angle declared by the feature
comes back in the answer next to the measured one, with a note of what each one
is.

It is the third API in two days that answers with what it was asked instead of
with what it did, after `ShowConfiguration2` and `ModifyDefinition`.

---

## 0.51.0 — 16 September 2026 · library 0.86.0

### GP14 — `suppress_instance`, and the two patterns that stay stuck

Catalogue at **54 operations**. A grid of holes that skips one where a rib runs
through, a row of slots that stops before the bend.

**There is no method: you write an array.** There is no "SkipInstance". You take
the feature's data, write `SkippedItemArray`, and put it back: `GetDefinition()` →
`AccessSelections` → `SkippedItemArray = {…}` → `ModifyDefinition()`. It is a
write *after* creation, not a parameter.

**The indices, measured.** Plate 200×100, hole at x=−50, pattern of 4 with a 25
step toward −X, that is positions −50 (the original), −75, −100, −125. Written as
`{2}`, the one at **−100** disappears: the index is the *second copy*, the
original does not count, counting starts at 1. It is also how whoever is asking
would count, so the number passes through as-is — and **zero is refused**, saying
the original is not a copy.

**The defect: you can remove a copy that is not there.** In the same test the
fourth position (−125) fell off the plate, and SOLIDWORKS had built three copies
out of four while answering "succeeded" — this is note 80y, and it is still true.
Asking to skip a copy that never existed **succeeds**: `ModifyDefinition` answers
`true`, the array gets written, nothing changes in the part. So the pattern's
faces are counted before and after, and if they have not gone down **it refuses,
putting the array back as it was**.

**Putting a copy back is written as `{0}`, not `{}`.** Removing works on the first
try; putting back does not, and the bench caught it. Three ways to empty the
list, after removing copy 2 from a row of four (faces 3 → 2):

| written | `ModifyDefinition` | faces after | read back |
|---|---|---|---|
| `new int[0]` | **true** | 2 (unchanged) | `{2}` |
| `null` | **true** *(but the write raises `RPC_E_SERVERFAULT`)* | 2 (unchanged) | `{2}` |
| `new int[]{ 0 }` | true | **3** | `{}` |

Zero is the way of saying "none", not an index; it is written one way and read
back another; and **two ways out of three answer `true` about something that did
not happen**. This is why here the criterion is the face count and never the
return value — and it retroactively justifies refusing `copies: [0]`, which
passed through there would silently erase every copy removed before.

**And of the nine pattern families, three do not have the array.** Measured:
sketch-driven, chain and dimension-driven do not expose `SkippedItemArray`, even
though all of them have `AccessSelections` with the same signature. There is no
common interface — writing one makes the build fail, which is the best way to
find out.

**The other two items in the TODO entry remain open, and the reason is written
down.** `FeatureFillPattern` answered `null` in **fifteen** measured combinations
(selection marks, face or sketch boundary, by step or by count, zero margin,
stagger 0° and 90°, square and perforation layout, seed as a feature, as a
sketch, as a cylindrical face, seed created by SOLIDWORKS, `GeometryPattern`) —
with no exception, no message, and no feature left in error in the tree. And
`InsertTableDrivenPattern2` with an empty `FileName` answers `null`: it wants a
file on disk, which is another kind of operation altogether. Tables and details
in `81u_quindici_volte_null_senza_un_perche`.

---

## 0.50.0 — 16 September 2026 · library 0.85.0

### GP13 — `engrave_text`, and the text that disappears halfway without saying anything

Catalogue at **53 operations**. It writes *words* onto the part — serial number,
assembly orientation, customer marking — engraved or embossed on a flat face.

`IModelDoc2.InsertSketchText` makes the **sketch**; the material is removed (or
added) by the cut and the extrusion already measured. It opens no window — it is
not like `AddDimension` — and it works even with `AddToDB = true`.

**The defect, and here it is a big one.** "PROVA" 10 mm tall, engraved 2 mm deep,
on a 100×100 square:

    anchored at (0, 0)     ->  removed 230,70 mm3
    anchored at (30, 10)   ->  removed 111,20 mm3

**Half the text falls off the face and disappears silently.** The feature is born
green, is named `Taglio-Estrusione1`, and on the part there is "PRO". On a serial
number there is nothing in the tree to notice it by. Falling entirely off is
already caught (`FeatureCut4` returns `null`): the case that matters is the
**partial** overlap.

**How it is caught.** The width of a piece of text cannot be computed from the
string: it depends on the glyphs' own drawing. But `ISketchText.GetEdges()`
returns the letters' contours — **56 `IEdge` for "PROVA"** — and from those you
read where the text really ends, in model coordinates. It is checked **before**
cutting, and it refuses, saying by how much it overran; the sketch is retracted.
For "PROVA" in Century Gothic 10 mm tall that is **47,05 × 10,51 mm** — a number
that can only be measured.

**And the one thing that does have a number.** The letters' area cannot be
predicted, but it scales with the **square of the height**:

    height  5 mm  ->  area  28,83 mm2  ->  area/h² = 1,1533
    height 10 mm  ->  area 115,35 mm2  ->  area/h² = 1,1535
    height 20 mm  ->  area 265,88 mm2  ->  area/h² = 0,6647   <- broken

The third is broken because at 20 mm "PROVA" is 94 mm wide and did not fit on the
100 face: **the same law that holds for the first two is the one that exposes the
third.** The operation answers `area_over_height_squared`, and the bench demands
it be equal across three markings of different height and depth.

**`Alignment` does nothing.** The TODO entry had it down as "not verified in
practical meaning". All five values 0..4 were tried on the same text: the
bounding box comes back identical to the thousandth across all five. It is not a
parameter that was not understood — it is a parameter that this call ignores. So
it is not exposed.

The operation also declares **what it does not check**: it compares the text with
the face's *bounding box*, and says whether that face fills its own box (then the
check is complete) or whether it has a hole or a re-entrant outline (then a letter
could fall on it without being noticed).

---

## 0.49.0 — 16 September 2026 · library 0.84.0

### GP12 — custom properties and configurations, and the two drawers that cannot see each other

Catalogue at **52 operations**, with a new family — **`identity`**: *what the
part says about itself*. `set_property`, `create_configuration`,
`activate_configuration`.

Up to now the catalogue knew how to make geometry and did not know how to say
**whose it is**. The drawing number and the revision are not geometry, but they
are what reaches the title block and the PDM record: a correct part with the
wrong revision is a wrong part.

**The same name on two interfaces, with different signatures.** The TODO entry
warned of a "twin-but-different signature": it is worse. `AddConfiguration`,
`AddConfiguration2` and `AddConfiguration3` sit on **`IModelDoc2`** (eight
parameters, eight, four; they return `void`, `bool`, `object`), and
`AddConfiguration`/`AddConfiguration2` **also** sit on `IConfigurationManager`
(six and seven parameters, returning `IConfiguration`). Looked up on the wrong
interface, `AddConfiguration3` "does not exist" — which is the mistake we made
before looking. *A name tells you what a method is called, not which interface
it is on.* `IConfigurationManager.AddConfiguration2` is used, being the only one
that returns the configuration object — from which the real name can be read
back — and that takes `Rebuild`.

**The two paths to a configuration's properties are the same path.** The TODO
entry asked to establish whether `IConfiguration.get_CustomPropertyManager()` and
`IModelDocExtension.get_CustomPropertyManager(name)` gave equivalent results —
"two paths never compared". Both were measured on the same part: written with
one, read back identical with the other, in both directions. `Extension(name)`
is used, which does not need the configuration object in hand.

**But the document and the configuration are two separate drawers**, and this was
not known: a property written on the document **cannot be seen from a
configuration**, and the other way round. They are not inherited. Whoever writes
the drawing number on the document and then looks for it among the
configuration's properties will not find it, and nothing tells them why.
`set_property` now also checks the other drawer every time and answers
`visible_from_other_list`, so it is a number measured on this part right now and
not a note in the catalogue that nobody read.

**The default configuration is not called `Default`.** It is called
`Default<A macchina>`, and the angle brackets are **inside the name**.
`ShowConfiguration2("Default")` answers **false** — it raises nothing, it says
nothing — and whoever does not check the return value works on the wrong variant
for the rest of the plan.

And it cannot even be guessed: a part freshly born from this company's template
has **two real configurations**, `Default<A macchina>` and `Default<Saldato>` —
the welded pair — and `GetConfigurationCount()` answers **2**. These are not
display states: those are yet another thing (`IConfiguration.GetDisplayStates()`,
here one per configuration). So `activate_configuration` **does not translate**
the short name: it compares it with the ones the document declares, and if it is
not there it says so, explaining why. The only good source is
`GetConfigurationNames()`: *names are not built, they are asked for.* After
asking, **it double-checks which one is active now**.

**And `ShowConfiguration2` answers `false` when there was nothing to do** — that
is, when the requested configuration is already the active one. The bench found
it in the nicest possible way: two red cases whose refusal message said all by
itself that the refusal was wrong ("*I asked to activate Left and now Left is
active*"). That boolean does not distinguish "I did not do it" from "there was
nothing to do", and so **it cannot be the criterion**: the criterion is which
configuration is active afterwards. The boolean comes back in the answer as
`was_already_active`, because the difference between a move and a non-move still
counts.

**`AddConfiguration2` activates** the new configuration **right away**, without
being asked to: everything the plan draws afterwards, and everything the state
reports afterwards, is about *that one*. It is stated in the answer.

**And `get_CustomPropertyManager("Right")` on a configuration that does not exist
raises nothing**: it returns a manager, and `Add3` on that manager answers
`AddedOrChanged`. The property is written, the code is happy, and it is not in
the file. It is the no-error defect in its purest form — the whole chain says
yes. The only way to catch it is to ask the document which configurations it
declares **before** writing.

The document state now carries `configuration`, `configurations`,
`how_many_configurations`, `property` and `configuration_properties` — two
fields for properties and not one, because they are two different places: a
single field would be a merge that does not exist in the file.

---

## 0.48.0 — 16 September 2026 · library 0.83.0

### GP11 — `cosmetic_thread`, and the callout that is not generated but copied

Catalogue at **49 operations**. It says *"there is a thread here"* on a pin or a
hole already made: it is an annotation, not a cut.

**It attaches to an EDGE, not to a face.** On the cylindrical face
`InsertCosmeticThread3` returns `null` in every combination tried. What gave it
away was the data model: `ICosmeticThreadFeatureData` has an **`Edge`**
property. With the circular edge selected it works on the first try.

**And the TODO entry's question had no answer.** The entry asked to find the
right value of `Standard` using "the generated callout" as the judge. Measured:
with `Standard` 0, 1, 2, 3 and 8 (ISO), with `StandardType` "Tapped hole",
"Machine Threads", «Maschiatura», «Filettature macchina», and `Size` "M6x1.0",
"M6", "M6x1" — the feature is always created, and reading it back:

```
Standard = -2    StandardType = «»    Size = «»    callout = «»
```

Passing the **callout** instead: `callout = «M6x1»`, even with `Standard = -2`
and both names empty. **The standard and the size are ignored, and the callout
is exactly the text it is given.**

So the operation asks for a `designation`, writes it, and **declares in the
answer that this text belongs to whoever asked for it and not to a table**:
nobody checks that "M6x1" is right for a 6 mm pin, and it reaches the workshop
exactly as written. *Generated* and *copied* are not the same thing.

**The volume stays the same as before** — 282,74 mm³ on a Ø6×10 cylinder, before
and after. If it changes, the operation fails: that would mean it actually cut
something.

## 0.47.0 — 16 September 2026 · library 0.82.0

### GP7 — `variable_fillet`, and the number that tells the variable one apart from the constant one

Catalogue at **48 operations**. A fillet whose radius **changes** from one end of
the edge to the other: large where the part takes a load, small where there is no
room.

**The recipe was the TODO entry's risk, and it was settled on the first
measurement.** Cube 40×40×40, vertical edge, radii 3 and 8, a new shape for a new
part:

```
edge + its 2 vertices, Radii = {3, 8}          277,7 mm³
ONLY the two vertices (without the edge)         NULL
with marks 1/1/1 instead of no mark            277,7 mm³
adding PointRadiusArray = {3, 8}                277,7 mm³
```

**The edge must be selected**, so must the vertices, **the marks do not
matter**, and `PointRadiusArray` **is not needed** — that is for the
*intermediate* points, which are not present here. Passing it out of habit would
be an argument whose effect is unknown.

**And the number is what tells the variable one apart from the constant one.**
The volume removed by a fillet of radius r on an edge of length L is
L·r²·(1 − π/4); for a radius going from 3 to 8 the mean of r² is
(9 + 24 + 64)/3 = 32,33 and the arithmetic gives **277,5**, measured **277,7** —
0,07%, because the blend is not exactly a linear sweep of the radius. A
**constant** fillet of 3 would remove **77,3**, one of 8 **549,4**: the three
numbers are far apart, and neither of the wrong ones can pass for the right one.
The operation reports both — the true amount removed and the hand-redone
arithmetic — and declares that the second one is an order of magnitude.

Two refusals: a largest radius that does not fit on the edge (it says so before
trying, using the real length) and the **circle** — the rim of a hole has no two
ends to vary between.

**`fillet_face` is not done**, and the entry stays half open: with the two faces
selected at mark 1 and 2 it returns `NULL` in all four combinations of options
tried. The hypothesis to try first is that it is meant for **non-adjacent**
faces — the ones used here share an edge, which is the case the normal fillet
already covers.

## 0.46.0 — 16 September 2026 · library 0.81.0

### GP8 — `combine_bodies`, and the enum with the right name that is the wrong one

Catalogue at **47 operations**. Booleans between bodies of the same part: union,
subtract, intersect. They are needed for moulds and jigs, and for parts designed
as several bodies and then put together.

**The enum named after the operation is not the one it needs.** In `swconst`
there is `swCombineBodiesOperationType_e`, with values `Add = 0`, `Subtract = 1`,
`Common = 2` — the exact names of the three booleans. Passed to
`InsertCombineFeature` **all three return `null`**, with no exception and no
code. The method wants `swBodyOperationType_e`:

```
SWBODYADD       = 15903     union
SWBODYCUT       = 15902     subtract
SWBODYINTERSECT = 15901     intersect
```

A name tells you what a constant is called, not which function it belongs to.

**And the array must be wrapped, and it also contains the main one.** Measured
on new parts, one variant per part:

| `ToolVar` | ADD | CUT | INTERSECT |
|---|---|---|---|
| `object[]` with only the tool | NULL | NULL | NULL |
| typed `Body2[]` | NULL | ok | NULL |
| `DispatchWrapper[]` only the tool | NULL | ok | NULL |
| `DispatchWrapper[]` **both** | ok | ok | ok |

An `object[]` of COM objects does not turn into a `SAFEARRAY` of `IDispatch*` by
itself — `DispatchWrapper` is needed for that — and without the main body in the
list, `union` and `intersect` do nothing, without saying so.

**The three numbers, on a case that tells them apart.** Two cubes of 10 offset
by 2 in X and 5 in Z, overlapping by 400 mm³: **1600 / 600 / 400**. With two
equal, centred cubes they would have been 1500, 500 and 500, and "subtract" and
"intersect" would not have been distinguished — a case where two wrong values
give the same result tables nothing.

After a `union` the bodies are counted: if there are still two they have not
fused, and the operation fails instead of confirming a part different from the
one whoever reads it believes they have.

**And the bench learned to compare a list of text.** `["C2"]` — the bodies the
boolean worked on — used to end up in "not a list of numbers: System.String[]":
a message that was true and useless, because the case was asking something
sensible and the evaluator did not know how to compare it. Now lists without
numbers are compared one by one, **and order counts**; seven new checks in
`Oce.Solid.Agent.ContractTests`.

## 0.45.0 — 16 September 2026 · library 0.80.0

### GP5 — `minimum_distance` and `measure_between`, and the argument that gets ignored

Catalogue at **46 operations**. `minimum_distance` is the way of answering "how
much material is left between the gasket groove and the outer face?" with two
**named** faces and a number, instead of tuning a vector of ray intersections
that nobody knows how to verify — the path the API census flagged as too risky.

**`ClosestDistance` returns metres**, and its two points are real: on a
40×40×10 plate, between the two large faces, it answers `0,01` and the points
are `[0,0,10]` and `[0,0,0]` mm, **zero** discrepancy. That check is now made on
every call: **if the value says one thing and its own points say another, the
call has failed and answered anyway** — and then the operation fails instead of
giving the number.

**`IMeasure.Calculate(Object Entities)` takes an argument and does not look at
it.** It measures what is **selected**:

```
Calculate(new object[] { faceA, faceB })              ->  FALSE
two faces selected, then Calculate(null)              ->  TRUE, 0,01
```

And worse: called with **one** face in the array, while the selection still
held two, it answered `TRUE` and gave the area of both — 3200 mm² instead
of 1600. A believable number, from the right part, from a different selection. The
operation clears the selection, selects by hand and **checks the boolean**.

**`Area` and `TotalArea` alternate**, and the declared case caught it:

```
one face only      Area = 1600 mm²    TotalArea = -1
two faces          Area = -1          TotalArea = 3200 mm²
```

The one that is valid is always the other one. Reading only one of them, the
field would vanish depending on how many things were asked for. And **`-1` is
not a measurement**: it is SOLIDWORKS's way of saying "this thing here does not
have it", so negative values become `null` instead of numbers.

## 0.44.0 — 15 September 2026 · library 0.79.0

### GP4 — `mass_properties` and `body_bounding_box`, and the measurement that changes its answer

Catalogue at **44 operations**. The two questions you ask before ordering —
"how much does this part weigh in aluminium" and "does it fit the 300×260×40
box" — were already computed inside `DocumentState` but **no operation could ask
for them**: they arrived inside the whole document state, or they did not arrive
at all.

**The defect the declared case caught on the first run.**

```
NO   mass_of_a_steel_cube · measurements.density_kg_m3   1 (expected 8000 ±0.5)
```

`IMassProperty` **changes its answers depending on what it was asked for
first**. On the same object, with `AddBodies` answering `true`:

```
Density read right away                  ->  8000   (correct)
Volume read first,      then Density     ->     1
SurfaceArea read first, then Density     ->     1
CenterOfMass read first, then Density    ->  8000   (unaffected)
```

And the mass follows suit: **0,0002 kg instead of 1,6 — eight thousand times
lighter**, with volume, area and centre of mass all correct right next to it. No
exception, no absurd value: a believable number in the middle of four correct
ones.

The fix is not "read the mass first": an order of lines is a contract that
breaks the day someone reorders them, and nothing would fail. It is **a fresh
`IMassProperty` for every number** — four COM calls that nobody feels, and order
stops existing as a concept.

**Mass without a material is `null`, not a number.** SOLIDWORKS does not fail:
it answers with the model's default density, **1000 kg/m³, that is, water** —
0,2 kg instead of 1,6 on the same cube. Believable and wrong by a factor of
eight. The operation answers `null` and writes why, and what to do.

**And the principal moments are not there, on purpose.**
`IMassProperty.PrincipleMomentsOfInertia` answers **three zeros**, and so does
`IMassProperty2.PrincipalMomentsOfInertia` with the other spelling. The real
moments sit in `Extension.GetMassProperties(1)`, positions 6-8. A field called
"principal moments" that answers zero raises no error — a moment of zero is
still a moment — so it is not reported at all.

`body_bounding_box` takes the **real points** with `GetExtremePoint` in six
directions, not the approximate box from `GetBodyBox`, and states its limit on
every call: it is the axis-aligned box, not the smallest possible one.

## 0.43.0 — 15 September 2026 · library 0.78.0

### GP3 — `export_step` and `export_iges`, and the file that had nothing to repair

Catalogue at **42 operations**. STEP and IGES are the formats used to send a
part to a supplier, to a customer, or to bring it into another CAD.

**They are not refused the way STL is, and that is intentional.** A STEP with
ten bodies is *normal* — a weldment is sent to the supplier just like that — and
so is one with open surfaces, because STEP and IGES carry them. Refusing them
would mean applying one format's rules to another. The check still runs and
**its verdict travels with the answer**: whoever sends a part has the right to
know it had two bodies, not to be stopped.

**The protocol is read back from the file, not taken on faith.** `swStepAP`
wants 203, 214 or 242, and those three values **have no enum** in `swconst`: the
number is a literal. The only proof that the 214 asked for is the 214 obtained
is the `FILE_SCHEMA` written inside the file — `AUTOMOTIVE_DESIGN` for AP214,
`CONFIG_CONTROL_DESIGN` for AP203. If it does not match, the operation fails:
some suppliers accept only 203, and a file with the wrong protocol gets
discovered on the other end.

```
OK   step_ap214_reread · measurements.file_schema      AUTOMOTIVE_DESIGN
OK   step_ap203_says_so · measurements.file_schema    CONFIG_CONTROL_DESIGN
```

Two cases write the same cube with the two protocols and expect different
schemas — with `does_not_include` on the other name, because "the right name is
there" is not enough. If the preference did not reach its destination they
would come out identical, and both green.

**`reopen_and_compare`: the only measurement that says the geometry survived.**
The file is reopened in SOLIDWORKS and its volume compared with the part,
failing beyond a thousandth of discrepancy. Measured: **0,00% in both STEP
AP214 and IGES BREP**, exactly 200.000 mm³ in both. It is not the default
because it costs a document open.

**And the defect that needed a probe.** The first implementation reopened with
`OpenDoc6`, which on a freshly written file answers:

```
OpenDoc6(…, swDocPART, Silent)           NULL   2097152  swFileRequiresRepairError
OpenDoc6(…, swDocIMPORTED_PART, Silent)  NULL      1024  swInvalidFileTypeError
LoadFile4(path, "", null)                open      0   1 body, 200000 mm3
```

"The file requires a repair" on a file that has nothing to repair — we had
written it ourselves two lines earlier. **An error code describes what the API
concluded, not what actually happened**: for neutral formats the door is
`LoadFile4`, and `OpenDoc6` sends you looking for the fault in the wrong place.

## 0.42.0 — 15 September 2026 · library 0.77.0

### GP2 — `export_stl`, and the format that carries nothing with it

The catalogue goes up to **40 operations** and gains the **`exports`** family.
It is the first bridge toward a printer: without it, a part the model has drawn
never reaches a machine.

**An STL file contains almost nothing**: a list of triangles. It does not say
what units they are in, does not say whether the solid is closed, does not say
which part it comes from. Everything that can go wrong here **still opens,
still slices and still prints**, and the defect shows up once the part is in
your hand. So:

**The check runs FIRST, and it can stop everything.** `export_stl` calls
`check_body_geometry` and **refuses to write** if the part is not a single
closed solid body with no defects. This was the reason GP1 had to be done
before this one: the file-level measurement — it exists, it weighs something —
does not catch the body with the gap.

```
NO   export_stl on two bodies
     I will not export: I would not export it like this: the part has 2 bodies: for a
     print it must be a single one.
     An STL carries nothing with it saying the part was like this: it
     opens, it slices and it prints just the same. Fix the part, or pass
     "export_anyway": true if you know what you are doing.
```

**The way out exists and declares itself.** One day someone will actually be
right — two bodies meant to be printed together on purpose — and a refusal with
no escape gets worked around by copying the file by hand, that is, outside
every check. With `export_anyway` the file gets written **and the answer
carries a note** saying the check was overridden, and what it said.

**The millimetres are explicit.** `IExportStlData` **does not exist in this
interop** (searched across all 1737 interfaces): every option is a *global user
preference* of the machine. Leaving `swExportStlUnits` as it is means the
result depends on whoever used SOLIDWORKS before — and the file does not carry
its units with it, so exported in inches the part comes out 25,4 times smaller
and no error says so. The preferences are **read, set, and put back as they
were**: they belong to whoever is drawing, not to us. `swSTLShowInfoOnSave` is
set to `false` because a window would block the COM call and would never
return — the same trap as `AddDimension`.

**And then the file is read back, not the answer.** A binary STL is 80 bytes
of header, 4 with the triangle count, then 50 per triangle:

```
OK   box_twelve_triangles · measurements.triangles   12   (six faces, two triangles each)
OK   box_twelve_triangles · measurements.byte       684   (84 + 50*12)
```

If the arithmetic does not add up, the operation fails, saying that the file
exists and is not the one needed. `SaveAs3` returning `true` only says that
SOLIDWORKS did not object.

Five declared cases in `cases\export\`: the binary box, the same one in ASCII
(twelve triangles counted a different way — `facet` lines instead of the four
bytes), the two rejected bodies, the two bodies exported by force (twenty-four
triangles: the number also says the second body really did end up in the file),
and the wrong extension — `.stp` instead of `.stl` would write an entirely
different format, and the file would exist, would weigh something, and would
open.

Cases can write files: placeholder **`{WORK}`**, which is `Bench\lavoro\` —
ignored by git and emptiable at any time.

## 0.41.0 — 15 September 2026 · library 0.76.0

### GP1 — `check_body_geometry`, and the first operation that does not draw

The catalogue goes up to **39 operations** and gains a family: **`checks`** —
"say how it came out, without touching it". Up to now no operation knew how to
answer "how did it come out?": the measurements existed — `rectangular_cut`
counts the material removed, `hole_wizard` measures where the axis ended up —
but they were private inside the operations, and the model could not call any
of them. `cut_list` was the only precedent of an operation that reads.

`check_body_geometry` answers, for every body: type (solid, sheet, wire), how
many faces, whether it is closed, how many gaps, how many topological defects
**by name** (`swFaultEntityErrorCode_e`, 36 values) and not by number. Then a
verdict in one sentence, where *printable* means **a single solid body, closed,
with no defects**.

**The two things measured before writing it** (`Checks\BrokenBody`, a new tool
that is also the check that keeps them true):

```
                          Check()  Check2()  Check3.Count  gaps
  sound solid                1        0           0           0
  two solids touching        1        0           0           0
  SHEET BODY (open)          1        0           0           1
```

1. **`Check()` and `Check2()` have opposite conventions.** On the same sound
   body the first says 1 and the second says 0. This was the question the API
   census had left open — "I don't know whether zero means sound or zero checks
   performed" — and the answer is worse than the uncertainty: the two
   conventions coexist on the same interface, and whoever swaps one for the
   other gets a result that is coherent, stable, and reversed.
2. **An open body passes three checks out of four.** A surface is not
   "broken": it is a valid body that encloses nothing. `Check`, `Check2` and
   `Check3` all three declare it sound, and **only `Diagnose().GetGapsCount()`
   sees it**. An operation built on `Check3` — the path the GP1 entry named
   first — would say "no problem" on exactly the case it exists for.

So all four are checked, and the raw numbers from `Check()` and `Check2()` stay
in the answer: the day a SOLIDWORKS version changes one of the two conventions,
it will be seen there, not from a verdict flipped backwards.

**The limit is declared on every call**, not once in the description:
`what_it_does_not_check` says that wall thickness and overhangs are not checked
by anyone yet. A shell three tenths thick passes through here without a word.

API traps paid for along the way: **`IBody2.Check3` is a PROPERTY, not a
method** (written as `Check3()` in late binding, it gives no compile error, it
gives a runtime failure inside a `catch`), and **`InsertPlanarRefSurface` sits
on `IModelDoc2` and returns a boolean**, while every other `Insert*RefSurface`
that returns a `Feature` sits on the `FeatureManager`.

## The bench, not the bridge — 15 September 2026 · no new number

### F11 — Test families become files, and three defects that had been there for months

Six test families in C# — `revolve`, `create_plane`, `create_axis`,
`linear_pattern`, `mirror`, `suppress_feature/delete_feature`, plus `the shape in one sentence`
— have become **twenty-five declared cases**. `Check(` in
`NewOperationsTests.cs` goes from **99 to 17**; the cases of the declared
families are **50**, plus the sixteen regression ones.

**The move tightened the tolerances, it did not just relocate the code.** The
revolve test accepted `expected * 0.02`: on the 62.831 mm³ cylinder that is
1.256 mm³ of allowed slack, more than a through M20 hole. In the files it is
±0,5 mm³ with the reason written right next to it. And the cases pinch harder:
`suppress_feature` now measures **which** hole has been turned off
(`centers_x_mm` from `[-60, 0, 60]` to `[-60, 0]`), not just that the volume
came back right.

**Three defects found while writing the files, and none of them raised an
error:**

1. **A green check that could never turn red.** "No curved edge is offered to
   the model" counted edges with `straight: false`. **That field does not
   exist**: the state writes `curved: true` and never writes `straight` on
   anything, so the count always stayed zero. And the premise written above it
   — "the model cannot name a curved edge, so the refusal is unreachable" —
   was false: after a through hole the curved edges are **two**. Now there is
   a real refusal case, `cases/patterns/limit_direction_from_a_curved_edge.json`.
2. **The comparison between a refusal and its twin was blind on lists.**
   `Convert.ToString` on an `object[]` returns `"System.Object[]"`: two
   different profiles would compare as equal.
3. **The list of measurement names had come loose from whoever measures
   them.** Two new measurements came out as "I don't know how to measure …"
   with the old list right next to it, and a bench error on a test the bridge
   was actually passing.

**Expectations can now ask for a sentence.** `includes` and `does_not_include`,
with a string or a list. The half that matters is the second: a description
that declares a profile the part no longer has fails nothing — it is simply
the right description of a different part.

**The twin of an operation with a single argument.** `create_axis` takes
`face_uid` and nothing else: two cases that differ by that one argument cannot
have one in common, and rule F5 would always refuse them. When there is only
one key, "nothing in common" is not a warning sign: it is the arity — and the
exception is declared in the report.

**F13's red is no longer a number.** It used to be `families.Count >= 15` and
it turned red without anything having broken, because the families had gone
down to twelve. A numeric floor on something that is meant to decrease is a red
with an expiry date: now the sieve must find **itself**.

**`bench.ps1` no longer mistakes yesterday's report for a new one.** A
`TypeInitializationException` — a static field with an initializer that names
`IModelDoc2` runs **before `Main`**, that is, before `AssemblyResolve` — used
to come out as a `NativeCommandError` naming the script, with
`ultimo-giro.txt` stuck on the previous run because `Tee-Object` does not open
it if no line passes through. Now the file is removed beforehand, and a run
that writes nothing is exit 2 with instructions for seeing the exception.

**Tests that do not open SOLIDWORKS live in a file of their own**
(`Bench/NoPartTests.cs`): the checks on `Sentences` (E10) and on the
exception's account (F12) are pure functions on strings and a sieve over the
sources, and in the middle of the part tests they looked like drawing tests.

**Stays in C#, with the reason written right next to it:** `move_bodies` (it
needs two distant plates, and `extrude_rectangle` always centres on the
origin), `CaptureView` and `HowMuchTheModelSees` (they measure images), and a
single line of `create_axis` — that the axis reattaches with
`SelectByID2("AXIS")`, which is a COM call and not a catalogue operation.

### F10 — The library of the time is not kept around: it is rebuilt

`.\Checks\library-of-the-time.ps1 0.70.0` finds the commit that **declares**
that `AssemblyVersion` — inside `AssemblyInfo.cs`, not in the commit messages —
makes a disposable worktree out of it, builds it, and prints the DLL's path.
`-Clean` removes it.

**The discipline the TODO entry was asking for already exists**, and it is
measured: `git log` on `AssemblyInfo.cs` shows one line per number, with no
gaps. The defect the entry described — a commit spanning 0.29.1 → 0.44.0 — was
the OpenPDM monorepo's, before the library moved over here.

**`red_before`**: a case declares the version before which it had to be red,
and on an older library it **must** fail. Seen firing: "on 0.75.0.0 it passed,
and it should not have: this test does not guard the defect it claims to
guard".

**And "the library does not load" is no longer a red run.** Pointing
`--libreria` at 0.30.2, the bench used to exit with an unhandled exception and
code **-532462766**, indistinguishable from a hang; now it says so and exits
2. The historical comparison **starts at 0.52.0**, measured: before that the
operations lived in `OpenPDM.SwAddin`, under a different namespace.

A cross-check that came for free: today's cases run against 0.70.0 do not pass
silently — they say "missing field: `state.bounding_box_from_mm`" and name every
field that was there. It is F4's behaviour, seen across time instead of
through a typo.

### F9 — A CI that builds, runs the contract tests and **demands the bench**

`.github/workflows/tests.yml` on `windows-latest`: it builds the bridge and
`Oce.Solid.Agent.ContractTests`, checks that the two executables **really
exist**, runs the tests, publishes `prove.xml`.

**And it does the one thing a machine with no licence can really do**: a
contract test reads `Bench/report/last.json` and **refuses a delivery in which
nobody ran the bench**. It checks that the run measured something ("0 tests, 0
failed" is not a green), that it is not partial, that the library is the one
declared in `AssemblyInfo.cs`, and that it is **later than the last change to
`Oce.Solid.Agent.Core`** — asked of git. *A library touched after the last run
is an untested library, even if the version number matches because someone
forgot to bump it.*

The red, measured by removing the file: "the bench has not been run on this
version".

`Bench/report/` stays gitignored **except for `last.json`**, and this is not a
convenience exception: without that file in the repository, that check does
not exist.

### F8 — The regression on references, and the three numbers

```
.\Bench\bench.ps1 -Regression   ->  16 cases
                                     12  hold
                                      0  break loose and SAY SO
                                      4  break loose IN SILENCE
```

Sixteen parts built, **a single upstream dimension changed**, and the
downstream geometry re-measured. The numbers are in the README.

**The zero in the middle is the most interesting of the three.** None of the
sixteen cases broke visibly: the four times the part came out different from
what the drawing declared, SOLIDWORKS rebuilt clean and said nothing.

**And the discovery worth more than the numbers:** a cut sketched **on a face
follows that face**. The hole on the bottom of a shell holds whether the box
grows by 28 mm or the wall thickens by 7. What does not follow is the position
**inside** the face: `x_mm`/`y_mm` are absolute coordinates on the sketch
plane, and a growing face does not move them.

> It is not "the references do not hold". It is: **the sketch plane holds, the
> coordinates inside the plane do not.**

Two of the eighteen cases **cannot be written today**, and the reason is
written down: a frame's path is a `3DProfileFeature` whose dimensions the state
does not publish, and `gusset` wants two corner faces that no selector knows
how to name.

Two new tools in the case format: **`same_as_before`** (the invariant is "how
it was", and computing it by hand would mean redoing arithmetic SOLIDWORKS has
already done — getting it wrong creates a false silence) and
**`center_of_mass_mm`** (the position invariant for something that leaves no
holes behind). The second one was useful right away: one case **held on
volume** while the hole could have ended up somewhere else.

### F7 — Tests that run where SOLIDWORKS is not there

```
msbuild Tests\Oce.Solid.Agent.ContractTests.csproj -p:Configuration=Release
Tests\bin\Oce.Solid.Agent.ContractTests.exe    ->  54 checks, 0 failed, prove.xml (JUnit)
```

A project **on its own**, not a bench mode: the bench's csproj resolves the
interop with a `HintPath` inside `…\SOLIDWORKS\api\redist`, and on a machine
without SOLIDWORKS **the build fails**. A flag is not enough — the problem
comes before the runtime.

It tests the **expectation evaluator** (19), the **catalogue** and `Similar()`
(11), `Session.SameDocument` (10, and until today that guard did not have
**even one test**), the **MCP protocol** talking to the bridge over stdio
(13), and that this project **does not reference the interop** — looking at
the assembly's references, not the csproj's text.

The evaluator is the **same file** the bench compiles, not a copy: a copy
drifts apart, and the day it drifts the suite with the CAD starts passing for
reasons invisible here. *If this one is broken, the whole SOLIDWORKS suite
turns green for free.*

**Three defects found the moment it was written**, and they are its whole
value:

* **the BOM that broke the first line.** `Process.StandardInput` is born
  with `Console.InputEncoding`'s encoding, and `AutoFlush = true` is set on
  it, which does a Flush, and a UTF-8 Flush writes the preamble: **three
  bytes in front of the first line**. The bridge answered `-32700`, and the
  symptom said "initialize does not respond" because the error line carries
  no id;
* **a check that fired for the wrong reason**: "no path on this machine"
  searched for `O:\` case-insensitively, and matched `tipo:\n`;
* **a `BadImageFormatException` that looked like a missing file**: an
  `AnyCPU` executable starts in 32-bit, and the bridge's x64 image will not
  load from there.

### F6 — `--muta`: every case must be able to turn red

`.\Bench\bench.ps1 -Mutate` perturbs **the plan, not the code**: one argument
at a time multiplied by 1,5 (booleans inverted), the case rerun, and it
**demands that it turn red**. A case that stays green under every perturbation
is **blind**: its expectations do not constrain its arguments.

**67 mutations on 11 cases, zero blind, 854 s** — `Bench/report/mutazioni.md`.

It is **the only mechanical way** of enforcing that a test has already failed
at least once: a hand-written `provata_a_fallire` is a declaration, and a
declaration gets copied from the case next door.

**The false blind spot is avoided by reading the catalogue.** Mutating
`depth_mm` on a through cut leaves the case green and it is not blindness: the
schema says `"only if not through"`, and the bench reads that line — from the
**loaded library**, not from `bin/catalog.json`, which another build writes.
Two safeguards of the same family: only steps **with expectations** are
mutated (a 500 hole mutated to 750 still covers the part), and **zero times
1,5 is zero**, so a coordinate at 0 is shifted by 1 mm instead.

The **surviving** mutations are the useful part, and the report names them:
`x_mm 0->1` on a hole stays green because those cases measure **how much**
material was removed, not **where**; `length_mm 400->600` on
`blind_pocket_400` stays green because the pocket is already bigger than the
face and SOLIDWORKS clips it at the edge — geometry, not a weak expectation.

`--muta` has a summary of its own (`MUTATIONS — 11 cases, 0 blind`): the
reds it produces are **sought after**, and adding them to a count meant for
reading the reds that were not sought after would make both useless.

### F5 — A refusal never stands alone

Every case with `must_fail` **must** name, in `twin`, a case that succeeds,
and that twin must be **green in the same run**. Removing the field from one
of the `lim_*.json` files, the command exits **2** and names the case.

> A test that only checks the refusal does not distinguish "the check works"
> from "the check refuses everything".

The four canonical pairs from note 80v are in the repository, all on the same
rectangular block, with the numbers re-measured today:

```
hole 270 through                 removed 2.977.287,36   (pi*135^2*52)
hole 60 with CENTER ON THE EDGE  removed 73.513,27      (half cylinder: 73.512,62)
pocket 400x400 blind, depth 5    volume 4.043.692, bounding box [314, 274, 47]
slot 30x9 through                area 252,62, removed 13.136,1
twins 4/4 green
```

The second is the most important: the center falls **outside** the material
and the cut is legitimate all the same — a check written as "the center must
sit inside the bounding box" would have rejected it.

**The rule written in the Risks section does not hold up to its own pairs.**
"The twin must differ by exactly ONE argument" rejects **two out of four**:
the hole outside the material differs by two coordinates, the slot by length
and width. The rule that actually runs asks for the same operation,
non-identical arguments and at least one argument in common — and **it says
how many differ**, instead of deciding on behalf of whoever is reading.

### F4 — A field that is not there is not a zero, and it is exit 2

A case that asks `measurements.material_removed_mm3` of `apply_material` —
which does not report it — **does not fail: it could not be measured**.

```
!!  ... BENCH ERROR: missing field: measurements.material_removed_mm3;
        present: library, class_name, material

28 tests, 0 failed, 1 BENCH ERRORS
expectations verified 15 of 16
exit: 2
```

**2 beats 1**: if the bench could not measure, how many tests have failed is a
question that has not been answered. Its own symbol (`!!`), its own count, its
own exit code, and its own section in the report — "these are not red tests,
these are tests that WERE NOT DONE".

**"Expectations verified N of M" is always written**, even when they match: a
number that only shows up when things go wrong is one nobody looks at until it
is too late.

And a declared `null` **is not** a missing field: the state writes
`material: null` on purpose, to say "that place exists and is empty". In
ninety percent of cases a test stops verifying because the field it was
reading was renamed, a dictionary that cannot find the key returns `null`, and
`null` compared with `null` passes.

### F3 — Cases are files, and the expected numbers sit right next to the steps

`cases/features/internal_shell.json` and `external_shell.json` run **with no
dedicated line of C#** and reproduce 82.500 and 106.500, plus the bounding box,
the removed faces, the declared orientation and the material taken off.
Changing 82.500 to 82.501, the case turns **red** and the report names it.

**The C# test was removed, not left alongside.** `ProvaGuscio()` measured the
same two numbers with `expectedVolume * 0.02`: on the plate's 240.000 mm³ that
is **4.800 mm³, more than a through M12 hole**. A test that accepts a hole's
worth of slack does not test the hole.

**Selectors refuse instead of guessing**, and this is the risk the TODO entry
named — a case that runs on a different face than the one the file name
promises, and that might even return the same volume. Ten pure tests verify
this: zero faces and two faces both cause a refusal, `@feature(n=…)` is the
only ambiguity that can be resolved, and `@dimension` comes back with the name
tied to the document's title **right now** — `cases/bridge/change_diameter.json`
used to write `Parte76`, which changes on every run, and that plan could never
be re-run.

**Tolerances are absolute and carry the why**, and without the why the case is
**broken**, not permissive. Same rule for a case with no `why`, a step **with
no expectations** (it passes and measures nothing), and a `must_fail` with no
`message_includes` (any error at all would do, even the wrong one): they come
out red, not skipped.

### F2 — A single command, and a report on disk

`Bench\bench.ps1` builds the library and the bench, checks whether SOLIDWORKS
can be attached to, **closes the documents left open**, runs, and writes
`Bench/report/AAAAMMGG_hhmm.md` and `.json` plus `last.json` — with the
SOLIDWORKS revision, the library version, how many operations the catalogue
has, the commit, the duration, and the reds spelled out in full.

The closing is inside the command because it is not housekeeping: six extra
documents are ~650 characters in the state, and "state under 16 KB" fails
**without the code having changed at all**.

**The three exit codes, all three observed:**

```
0   -Group table                                 -> 0
1   volumeExpected 200000 set to 199000 by hand   -> 1, and the .md names the case
2   a syntax error in the bench                   -> 2, and it does NOT run
2   -Group vist (a group that does not exist)      -> 2
```

The compilation case 2 is the defect the TODO entry named explicitly: the
**exit code** of msbuild is checked, not the text.

What the report **does not** have — blind cases, expectations verified out of
total expectations — is written in the report itself: these are numbers that
exist once cases declare their expectations, that is, from entry F3 onward. A
zero put there today would look like a result.

Two defects caught while testing: the first `.json` weighed **498 kB**
(`Get-Content` returns lines carrying `PSPath`, `PSDrive` and the provider
along with them, and `ConvertTo-Json` follows all of them), and **two runs in
the same minute overwrote each other** — with `-Group` a run lasts seconds,
and a green report was wiped out by a red run started twenty seconds later.

### F12 — The null now has a name, and runs are counted

On 15/9 a test came out red with "Object reference not set to an instance of
an object.", and nothing else. **It was not reproduced**, and it did not need
to be: it was enough to count how many nulls could give that message at that
point. There were two, and of one family only — `NewPart()` used without
looking at what it had produced (**18 spots**) and
`GetFeatures(false) as object[]` with a `.Select` right after it (**3
spots**).

`NewPart()` no longer returns null: it raises an exception that names the
step and says that **it is not the test that has failed** — it is the bench
that failed to *prepare* the part, which is a different thing and reads
differently. The six `if (doc == null)` checks have been **removed**: after
this change they can never fire again, and an unreachable check reads as a
defence that defends against nothing.

**And the count, which used to be impossible.** The TODO entry asked "how
many times out of how many runs": `in-corso.txt` gets overwritten by the next
run and `ultimo-giro.txt` is a redirection made by whoever launches it, so
**nothing was left of past runs**. Every run now adds a line to
`Bench/storico.txt` with the tests that failed **by name**, because the
question put to that file is always "how many times has it happened to THAT
one?". Not version-controlled: it is this machine's own log.

### F13 — One group of tests at a time, while you work

`--gruppo <name>[,<name>]` runs a single family. `--gruppi` lists them. A
wrong name is refused **before attaching to SOLIDWORKS**, with the list of
real names and exit 2.

```
full run                 ~900 s
--gruppo views            6,6 s
```

On 15/9 the bench was launched about a dozen times: **roughly three and a
half hours of SOLIDWORKS**, almost all of it to retest a hundred things that
had not been touched.

**The full run stays the default**, because "I'll only test the one I
changed" does not work, and the same day proved it twice: three numbers added
to hole faces pushed past the 16 KB ceiling **on a frame, which has no holes
at all**, and image cropping changed an assertion on the PNG's weight. *The
bench measures what you broke, not what you did.*

That is why a partial run **cannot pass for a full one** — on screen and in
the last line of `in-corso.txt`, which is the one read with grep:

```
NOT RUN — 23 groups out of 24: sections, library, frames, ...
the full run is launched without "--gruppo", and that is the one that counts before committing.
PARTIAL RUN (views only, 23 groups not run) — 6 tests, 0 failed
```

### And the defect that groups introduce

A new test that nobody puts in the table does not run **and does not say
so**: the same oversight as before — back when the list was
`NuoveOperazioni()` — and the same silence. `TestThatTheTableCoversEverything`
asks .NET which `Test…()` methods really exist and compares them with the
table, using `MethodInfo` and not hand-written names.

**Seen firing**, by adding a method the table does not name:

```
  OK   F13 RED: the sieve sees the test families         17 "Test…()" methods
  NO   F13: no test family left outside the table         ProvaDimenticataApposta_TEMPORANEA
```

For the same reason a group's actions are an `Action[]` and not a lambda that
calls them: a lambda would hide the real methods behind its own, and the
check would end up searching among compiler-generated methods, saying yes to
everything.

**The bridge and the library do not change**, and that is why `VERSION.txt`
stays at 0.40.0: a number that goes up without what it numbers having changed
tells a lie to whoever reads it to find out "with which version".

---

## 0.40.0 — 15 September 2026 · library 0.75.0

### E10 — The bounding-box fraction: as AN EXPLANATION, never as identity

When a face moves because the part has grown, the messages used to say
«moved by 60 mm» — true, and it explains nothing. Now they also say **where
it stood relative to the part**:

```
«F6» now points to a different part of the piece (moved by 60.00 mm). The piece
has changed since you confirmed: I am not working on the wrong face.
For what it's worth: «F6» stood at 100% of side X and still does — the side
went from 100 to 160 mm. It may be the same face grown with the piece, but
that is not mine to decide: identity is measured in millimeters.
```

The same sentence attaches to the message of the other path, the one where the
face **is found again** through the persistent reference: it is case A of entry
E2, and it is the case this entry existed for.

**And the real closing criterion is a grep that must come back empty.** As a
CRITERION OF IDENTITY the fraction is the only one of the proposed paths that turns
an «I don't know» into a wrong «yes»: a hole 20 mm from the edge, on a plate
that goes from 100 to 300, **stays at 20 mm from the edge** — that is what it
means in the shop — while the fraction sends it to 60, where a face finds it,
and **confirms instead of rejecting**. On top of that the bounding box is unstable: a boss in
one corner changes the denominator for every face at once.

For this reason the calculation lives in **one place only** (`Sentences.WhereItWasAndWhereItIs`),
returns `string` and not `double`, and `SignatureMatches` and the re-matching loop
never name it. A bench test **reads the source** and checks it, with its own red
case: the same sieve run over `Sentences.cs`, where those words do appear,
must find them.

### The state also says WHERE the box sits

`bounding_box_from_mm` — three numbers, the minimum corner of the part. `bounding_box_mm` used to say
«140 x 16 x 140» and did not say where that volume sat: 50 on a side 100 long
means half if the box starts at zero and **all of it** if it starts at −50. Without
the corner the explanation above cannot even be written, and whoever wants to
put a hole «20 mm from the edge» had to guess whether the part was
centered on the origin or resting against it.

A few dozen characters on top of a 16.000 ceiling.

---

## 0.39.0 — 15 September 2026 · library 0.74.0

### E14 — `hole_face` used to return a fillet, and did not say so

`References` was looking for **cylindrical** faces left by a step: when there was
exactly one it returned it, more than one tripped a rejection. **But a fillet
leaves cylindrical faces just like a hole does.** A step that left exactly one, and
that one was a fillet, returned it **without saying anything**: whoever had asked
for the wall of a hole ended up with the surface of a fillet, and the next
operation landed on it.

The criterion is geometric, and **measured** — `Checks\HoleOrFillet`, a
new tool that builds a plate with a Ø12 hole and an R5 fillet and
asks the API:

```
hole wall                FaceInSurfaceSense = true    (concave)
fillet surface           FaceInSurfaceSense = false   (convex)
```

A hole's wall has material **outside** the cylinder. When in doubt the answer is
«not a hole»: mistaking a fillet for a hole sends a later operation onto the
wrong face without anything failing; the other way around produces a rejection,
which can be read.

### And the entry proposed the wrong remedy

E14 asked to **choose with a criterion** — «the diameter, or the
position» — instead of rejecting when there is more than one wall. Among four
identical M10 holes **there is no criterion at all**: any choice is guessing, and
the next step would land on a random hole without anything failing. It is
exactly the defect this project exists to make impossible,
offered as a convenience.

The rejection stays, and now it says **how many there are and what diameter**: whoever
reads it already has in hand what they need to name one.

### Three wrong reds before the right one

The test (`Bench/test_face_hole.js`) cost more than the fix, and
every attempt tripped **for the wrong reason** — that is, it looked like a red
that works:

1. **in the bench**: `face_ref` names a step on the **same plane**, and the
   bench runs one operation at a time. It rejected with «I don't have the
   geometry for that step», without ever reaching the code under test;
2. **on `cylindrical_cut`**: that kind does not even accept it. The role
   `hole_face` is reachable from **only one** operation, `shell` —
   checked against the catalog, not remembered;
3. **with `mirror`**: the mirror step leaves **only one** new wall,
   because the original belongs to the earlier step. Taking it is correct.

The right one is `hole_wizard`, which says so itself in its schema: «several
identical holes belong in a single call».

> A test case must not depend on geometry that the case itself is
> creating, and «which operation leaves N faces» is read from the catalog instead
> of being inferred.

---

## 0.38.0 — 15 September 2026 · library 0.73.0

### V6 — the frame fits the part, and costs less

After the automatic fit the part filled **58% of the width and 89% of the
height**. It looks like a crop that needs tightening, and it is not: **the vertical
is the constraint**. `ViewZoomtofit2` scales until the part fits in both
directions, and whichever hits first ends up at ~90%; the other stays behind
by however much the part's shape differs from the frame's.

A zoom cannot fix this, because it acts on both directions **together** —
tried in V5 at 1.35, and the part came out cropped at top and bottom to gain
width.

> You do not fit the part to the frame: you fit **the frame to the part**.

The PNG is cropped around the box of pixels that are not background, plus
a 4% margin and never less than twelve pixels — the test calls «cropped» a
part three pixels from the edge, and a margin under its own tolerance would
make a perfect image fail.

```
L-bracket     isometric   92% x 93%     plate    isometric   91% x 93%
300x20 bar    isometric   93% x 89%     bar      right       93% x 93%

No view cropped.       (12 of 12, the entry's criterion: 75%)
```

**And it costs less, not more:**

| | before | now |
|---|---|---|
| isometric of the flange | 97 kB | **24 kB** |
| isometrics of the five shapes | 71-115 kB | **24-44 kB** |

Fewer pixels **and** more part inside. An image costs more than a thousand tokens, and
those were tokens paid for showing nothing.

**It cannot crop the part, by construction:** the crop is made on the box
of the part's own pixels, so cropping one of them away would mean that pixel
was not part of the part — that is, that the threshold is lying. And it is the same
threshold that `FramingTest` tests on an **empty document**, where the count
must come out to zero.

`LockBits` one row at a time with `Marshal.Copy`, and not an `unsafe` block:
turning that on would change how **the entire library** compiles, and that is a
bigger commitment than a crop is worth.

**With this, sections R and V are both closed.** They were born on the same
day from the same oversight — the goal says *propose, do, look,
fix*, and the file only had the first two.

---

## Unnumbered — 15 September 2026 · in the monorepo, not here

### B6 — the library enters the monorepo as a submodule

The commit lives in the other project's monorepo, not in this repository: here
not a line changes. It is told here because entry B6 lives in this TODO, and
because the test clone found something worth knowing.

`OpenPDMSwAddin.csproj` pointed at the library with an **absolute**
path, into the developer's working folder. Now it is a submodule of that
repository, with a relative path.

**What the test clone found.** Before the commit, a clean clone of the
monorepo in a new folder compiled **just the same** — fetching the
library from that absolute path, that is, from this machine's working
copy, whatever happened to be inside it at that moment.

On this workstation the absolute path never failed. **It only failed
elsewhere, and here it lied**: the defect that gives no error in the
hardest form to see, the one where the developer's own machine is the only
one where it never shows up.

**The criterion, verified:** without `git submodule update --init` exactly **one
error** comes out, naming the submodule and the command — no CS0246. After
the init, the library builds from inside the clone and the add-in exits **0**.

**And the defect the entry predicted** — «submodule stuck on an old sha,
add-in and bridge against two different libraries, with nothing failing» —
cannot be prevented, but now it is visible: every build prints which
library it is building against.

---

## 0.37.0 — 15 September 2026 · library 0.72.0

### V4 — the cutaway: looking inside without touching the part

`inspect_part` accepts `section: {plane: "right", offset_mm: 0}` and answers
with the part **cut open**. A cutaway says more about a shell than ten
outside views: from the outside an emptied box and a full one are the same
rectangular block, and **the floor's thickness cannot be read at all**.

**It does not go through `RunCommand`.** The menu command «Section View» opens a
panel and sits there waiting: a COM call that opens a modal **never
returns** — the `AddDimension` trap, already paid for once.
`IModelViewManager.CreateSectionView` answers with a bool and asks nobody
anything.

**And it does not really touch the part.** `GraphicsOnlySection = true` cuts the
**on-screen drawing**, not the geometry: no feature in the tree, no
body split, nothing to undo. The section is removed with
`RemoveSectionView()` in the `finally`, together with the orientation — leaving
it on would be the same damage as leaving the part turned.

Measured by `Bench/test_section.js`, after the shot: **volume 109056, bodies 1,
features 2** — identical to before.

#### The red case: a section that sections nothing

`test_section.js` compares the bytes of the two images. A section that
cuts nothing would give an image **identical** to the full view, and «the section
comes out» would pass all the same.

#### And you look FROM THE FRONT, not isometric — measured

The first pass photographed the section in the current orientation. Two isolated
models, asked how many times the thickness fits into the box's
height — the true value is **12,5**:

| | wall | floor | stated confidence |
|---|---|---|---|
| full view | 8 · 13 | **not readable** · **not readable** | 2/5 |
| section in **isometric** | 6 | 8 | — |
| section **from the front** | **12** | **12** | **5/5** |

Two things at once. The section **reveals the floor**, which from the outside was
not visible at all — both judges answered «not readable» looking at
the outside, and that is the entry's criterion. But in isometric the numbers were
off by 40-50%, because **a thin wall seen at an angle looks
thicker**.

Now the view is derived from the plane — `right` → you look from the right — and
cannot be chosen: whoever asks for a cutaway on the right plane wants to look from
the right, and letting it be chosen would be one more way to get it wrong. Remeasured:
**12 against 12,5, 4%**, and the stated confidence rises from 2/5 to **5/5** — the
model is more confident exactly where it is right.

> A foreshortened image shows a SHAPE; only an orthogonal view carries a
> NUMBER. It is V2's line — images give the shape, the numbers live in the
> text — with the one exception worth having.

---

## 0.36.0 — 15 September 2026 · library 0.71.0

The turned profile in the sentence (entry V7). The content is described further below,
under 0.35.0, under «V7 — and then they became five out of five»: the two entries
were written a few hours apart and tell the same story, so the
account lives in one place only.

Here is the number, which changed: **`test_shape.js` goes from 4/5 to 5 out of
5**, and the bench from 105 to **107 tests, 0 failed** — the two new ones are the green on the
profile on a clean tree and the **red on a drilled tree**, where the sentence
must stay silent.

---

## 0.35.0 — 15 September 2026 · library 0.70.0

### V3 — the shape told in one sentence, and **4 parts out of 5 can be redrawn from it**

The state carries a new field, `shape`: the same geometry in one line.

```
Bounding box 140 x 16 x 140 mm, volume 191938.7 mm3 (61% of the bounding box),
1 Ø60 hole (center 0,0,0), 6 Ø11 holes on a Ø90 bolt circle at a 60-degree pitch.
```

**68-152 characters** across the five test parts, out of a 16.000 ceiling.

#### The measurement, harder than V2's

The entry asked that from the **sentence alone** a model would know how to redraw
the part with the right dimensions. Five sentences given to an isolated model —
which had read only `catalog.json`, no state, no image — so that it would write
five plans. The plans were run and the volume compared with
the original:

```
part 1 drilled_plate       volume 353214.2 against 353214.2   (0.0%)
part 2 open_shell          volume 109056   against 109056     (0.0%)
part 3 l_bracket           volume 105600   against 105600     (0.0%)
part 4 drilled_flange      volume 191938.7 against 191938.7   (0.0%)
part 5 stepped_shaft       volume 57962.4  against 55857.5    (3.8%)
```

**Four out of five at the exact volume**, not «close»: 0.0%. And the fifth is
the one the model itself **had declared in advance** that it could not do,
saying what was missing: «it does not say how many steps the profile has, where
the step falls, nor the intermediate diameters».

A model that can say in advance which of the five will not succeed is a
better result than five out of five obtained by guessing.

#### V7 — and then they became five out of five

The sentence now also carries the profile of a turned part:

```
part of revolution, profile Ø30 for 40, Ø20 for 30, Ø34 for 20 mm
```

```
part 5 stepped_shaft       55857.5 against 55857.5   (0.0%)    <- was 3.8%
```

**Where a segment's length lives: in the area.** It is written nowhere, but a
cylinder of diameter d and length L has area **π·d·L**. The order
comes from the `point_mm` projected onto the axis — it is a point on the surface and
not on the axis (E16), but for **ordering** that is enough, because the segments of
a turned part do not overlap.

**And the formula is fragile, so the profile is only declared if it
reconstructs.** `area / (π·d)` holds on a **whole** cylinder: cut by a
flat, a keyway or a hole, the area drops and the length comes out short — with the
right shape, so invisible. Two checks, and both must come back right:
the sum of the lengths gives the bounding box along the axis, **and** the
reconstructed volume — Σ π(d/2)²·L — gives the true volume. If they do not match it stays
silent, and «part of revolution» remains: little, but true.

**The red case is in the bench:** the same shaft with an **axial Ø10 hole**. The
internal cylindrical face is one extra segment for the formula, the sum overshoots
the bounding box and the reconstructed volume counts removed material. The test asks that the
sentence say «revolution» and **not** say «profile». Without it, «the profile comes out
right on the clean shaft» would pass even if the check were not there.

#### The generator invented a fact on the first sentence

On the plate it said: «4 Ø12 holes **on a Ø90 bolt circle at a 90-degree
pitch**». The holes sit at the corners of a **160 x 90** rectangle.

The rule was a single one — «all at the same distance from an axis» — and it
needed three:

1. **same plane**: equidistant from an axis means on a CYLINDER;
2. **same radius**;
3. **regular angular pitch**: the corners of a rectangle sit on a circle,
   and their angles are 29.4° and 150.6° — not 90° from one another.

It is the same defect as E16, committed by the code written to fix it. Now
if any one of the three does not match the sentence **says nothing** and leaves the
centers: whoever reads it does their own math on real numbers.

#### The family name is written only when a TYPE authorizes it

`Shell` says «hollowed out», `Revolution` says «of revolution»: they are what the
designer **has done**. It does not say «plate» because one dimension is small
relative to the others — that would be a made-up threshold, and the day it is wrong
whoever reads it believes it all the same, because the sentence sounds as confident as
all the others.

*(V3's premise was that the sentence should serve to make the shape
UNDERSTOOD. V2 had already shown that with the state alone the family comes out
right 5 times out of 5: what was really needed was **to say the derived numbers
once, and well** — centers, pitch, wall thickness — instead of leaving them for
each reader to work out.)*

### E17 — an angle declared in millimeters

Found while looking at **why** the stepped shaft was the only one that could not be
recovered: the only dimension that part offered was

```
"name": "D1@Rivoluzione1", "mm": 6283.185
```

which is **2π × 1000**. `SystemValue` returns everything in system
units, and for an angle those are **radians**; the state was multiplying by a thousand
whatever it was — the metre-to-mm conversion — and labeling it `mm`. A dimension of six
point two eight metres on a part ninety millimetres long, and no one noticed
because the field is called `mm` and the number is a number.

Now an angle comes out as **`"deg": 360`**. The field's name changes with
the unit: keeping `mm` and only fixing the number would have been **worse than
before** — a correct value under a false label cannot even be suspected,
whereas 6283 at least jumped out.

**And the right enum was not the one it looked like.** `IDimension.GetType()` returns an
`int`, but **not** `swDimensionType_e` (where angular is 3): the first
fix used that one and it did not fire. Measured by putting the number into the
state and looking at it — angle `1`, linear `0` — that is,
`swDimensionParamType_e`. Two almost-homonymous enums for two different things, and
the compiler cannot say anything because both are `int`.

---

## 0.34.0 — 15 September 2026 · library 0.69.0

### R2 — a feature in the middle can be removed, not only the last plan

The operations go from **36 to 38**: `suppress_feature` (with `reactivate`
to switch it back on) and `delete_feature`.

`undo` (R1) undoes **one plan**, whole and only the last one.
`set_dimension` changes a number, it does not withdraw a choice. In the middle there
was nothing: a wrong feature at the third of ten steps could only be
endured.

**The entry's criterion, to the letter.** On a part with three holes, suppressing
the third brings the volume back to the value it had with two — measured before
making the third, so an equality and not an estimate:

```
the volume returns to what it was with two holes     393842,478 against 393842,478
the state shows it suppressed                        suppressed: true on «Taglio-Estrusione3»
reactivating it restores the volume as it was         390763,718
deleting a hole with no children succeeds             and it is no longer in the list
```

### The part that matters is the rejection

| case | what it does |
|---|---|
| a feature that others depend on | `delete_feature` **rejects**, and **says which ones** |
| a name that does not exist | rejects, and says which ones do exist |
| suppressing something already off | it says so, instead of answering «done» |

**Children stop `delete_feature` and not `suppress_feature`, and that is intended.** Suppressing a
parent turns off the children and turns them back on together with it: that is
almost always what is wanted («take off the hole and its fillets»). Deleting would make
them **vanish**, and SOLIDWORKS does that without asking.

**And afterward it checks that it is really gone.** `DeleteSelection2` answers
`true`; the question is whether that name is no longer in the tree. It is the
difference between «it said it did it» and «it did it».

**The identifiers change, and the answer always says so.** Removing a
feature makes faces vanish, and the uids are positional: whoever has in hand
a plan written against «F3» no longer has an «F3» after this.

`SucceededWithoutCreating` instead of `Succeeded`: the latter writes into the log
«created feature X», and an operation that says so while deleting one would leave
a false sentence. A new name and not one more parameter — `Succeeded` is the point
every success of the project passes through, the worst place to try to
change a signature.

### A SKIPPED red is not a passed red

The test's first draft built a fillet on the third hole to give it a
child. The fillet did not come out («No edge selected»), and the test
ended up **SKIPPED**: the very check the whole entry exists for never
fired, and the run said «104 tests, 0 failed».

It is exactly the case the bench exists to print the skipped ones apart and count them for.
Redone on the natural parent — **the base extrusion**, which is the case the
entry calls dangerous by name: the three holes are its children.

---

## 0.33.0 — 15 September 2026 · library 0.68.0

### V5 — views no longer come out cropped

**The defect.** `ViewZoomtofit2` frames inside SOLIDWORKS's **graphics
area**; `SaveBMP` then renders into whatever frame it is asked for. If the two
shapes do not match, whatever is left over gets cropped away — and nothing
says so.

```
graphics area  1845 x 924  ->  1,997
requested      1024 x 768  ->  1,333
```

Seven views out of twelve came out past the **right** edge: always on the
right, never top or bottom, never on the left.

**I was not the one who found it.** Two models had noticed it on their own
during the V2 measurement, while answering a different question: «the third
dimension cannot be determined because it **falls outside the frame**», «the view
comes out **cropped on the right side**».

**The fix.** The frame no longer imposes its own shape: it takes
the graphics area's shape, within the maximum size requested. With 1024x768 requested and
a 2:1 area, out comes 1024x513 — smaller, and **whole**. The width and height
passed in stay a ceiling, not a demand.

**And then it is tightened by 1,15.** The automatic fit left the part at
~50% of the width: half the pixels of an image that costs more than a thousand
tokens showed nothing. The factor is not chosen by taste — **the ceiling is set
by the direction that is already full**: in the vertical it was already at
**78%**, and trying 1,35 the L-bracket and the plate came out past the top and
bottom edges. Width was being gained by cropping height.

After: **zero views cropped out of twelve**, fill going from ~50% to **58-89%**.

A uniform zoom cannot do better than this: the two margins differ and the
factor is a single one. Really framing well would mean projecting
the part's bounding box into the view and fitting the two directions on their own —
another entry, not a constant to raise.

### `Checks\FramingTest`, the ruler that tests itself

It takes the shot and counts the **pixels**: does the box of the ones that are not
background touch an edge? how much does it fill? Three shapes chosen to be awkward
in different ways (a long, low bracket, a wide, flat plate, a 15:1 bar); a
cubic part would say nothing because it always fits.

**Two wrong thresholds before the right one**, and both declared twelve
views out of twelve cropped **with the same box on parts of different
shapes** — which is the sign that a measurement is not measuring its own input.
What exposed them was looking at the frame: on a bar that sits between y 358
and 410, the ruler said y 5-443.

Now the tool has **a red case of its own**: it photographs an **empty** document and
the count of part pixels must come out to **zero**. It costs one shot, and the two
wrong thresholds would have died there instead of after three rounds and two
readings by eye.

And when it says «cropped» it **leaves the frame on disk**: a tool that
issues a verdict must let you see what it is based on.

**The two criteria are not of the same kind, and the outcome depends only on the
first.** Cropping is a defect — a cropped part is a different part.
Filling little is a cost: it always gets printed, and it does not fail anything.

---

## 0.32.0 — 15 September 2026 · library 0.67.0

**The V2 measurement was measuring something else, and found this.**

### E16 — a hole's `point_mm` is not where the hole is

Two models, each on its own and without seeing each other's answers, read
the state of a flange with **six holes on a Ø90 bolt circle** and both
answered **Ø79**, both with **confidence 5 out of 5**. On the same plate
the center distance of 160 came out as 149,5.

79 = 90 − 11: they had measured between the **near edges** of the holes instead of
between the axes. Not through carelessness — because that was the only thing the
state gave them.

**What there was, for a cylindrical face:**

| field | what it really is |
|---|---|
| `point_mm` | a point **on the surface**, at a radius from the axis in a direction nobody states. Used to SELECT |
| `axis` | a **direction** (`[0,1,0]`), not a position |
| `normal` | `null`, because the face is curved — so not even a direction to correct toward |

The center could not be derived from any combination of those three.

**And the data had already been read, and thrown away.** `CylinderParams` returns seven
numbers — **point (0-2)**, axis (3-5), radius (6) — and `CylinderOf` read
`par[3..6]` discarding `par[0..2]`, that is, the one thing that says **where**.

It is the third time this project finds the same shape: a measurement taken and
thrown away (E8, the section V, and now this one).

**Now** cylindrical faces carry `center_mm`, a point on the axis. It is called
that and not `axis_mm` because `axis` next to it is already the direction: two
almost-homonymous fields meaning different things are the surest way to get
them swapped.

**Only on faces that come from a feature, and the ceiling decided it.** The
first draft put it on every cylindrical face, and **the bench stopped the
commit**: a four-bar frame went from 15.633 to **16.163** characters,
past the 16.000 ceiling, with a single document open. The cylindrical faces
of a tubular frame are the corner fillets of the profiles — dozens of them, and
nobody measures a center distance between their axes. The ones that matter are
the faces that come from a feature **different** from the one that made the body:
holes, pockets, countersinks, that is, things that get positioned.
Same rule as `from_feature`. On the seven-hole flange: **+230
characters**.

It does not cover a cylindrical face that *is* the body — the inside of a tube. If
it is ever needed, that is a new entry.

**The green case** was asked of a fresh model, not told that `center_mm`
existed: it answered Ø90 and center distance 160x90, and declared on its own
that it had used `center_mm`. That is how you check whether a new field is
*readable* and not merely *present*.

### V2 — how many views are needed, and the answer is not the one we expected

Five shapes, four conditions, four **isolated** models. Criterion written
**before** looking at the answers (`Bench/cases/views/HOW_IT_COUNTS.md`).

| condition | FAMILY | FEATURES | DIMENSIONS | **MADE-UP** |
|---|---|---|---|---|
| isometric only | **5/5** | 3/5 | 0/5 | **0** |
| four views | **5/5** | 4/5 | 0/5 | **0** |
| state only *(the bridge before 0.31.0)* | **5/5** | **5/5** | **5/5** | **3** |
| state + isometric *(now)* | **5/5** | **5/5** | **5/5** | **1** |

**With the text state alone the family comes out right 5 times out of 5.**
«Thin-walled box open on one side», «stepped shaft of revolution», «round drilled
flange»: all guessed without seeing anything. Section V's premise —
«the model does not understand the shape» — was **too strong**.

The image's contribution is something else:

> With the state alone the model produced **three** wrong numbers, all with
> confidence 5 out of 5. With the state plus the isometric, **one**.

And in ten out of ten answers based on images alone, the model wrote
«not estimable in absolute mm, no reference for scale»: **zero invented
numbers**, and correct proportions (side ratio 1,6-1,7:1 against the true 1,67).

One model, one run per condition, five shapes chosen by whoever wrote the
measurement: on FAMILY and DIMENSIONS the gap is clear, on MADE-UP **3 against
1 is a single run**, and a single run is not a trend.

### V5 revealed: the three standard views come out CROPPED

The red case was written by a model, while it was answering something else: «the
third dimension cannot be determined because it **falls outside the frame**», «the
view comes out **cropped on the right side**». Looked at by eye: in the
front view of the bracket the part starts 300 px from the left and runs off the
right edge.

The isometric is whole and centered — and it is the one the bridge recommends. For
this reason the defect went unnoticed, and for this reason the «four views against
one» comparison above is **skewed against the four**.

---

## 0.31.0 — 14-15 September 2026 · library 0.66.0

**The model had never seen the part, and it had no way back.**

Two halves of the same goal — *propose, do, **look**, **fix*** — and neither of
them had ever entered the TODO.

### R1 — `undo`: the fifth tool

This bridge told the model **twenty-one times** «undo it with Ctrl+Z». The
recipient of those messages is a language model, which has no
keyboard: it was a fix offered to someone who could not take it.

And the thirty-six operations are all additive or parametric. The only
possible correction was `set_dimension` — you change a number, you do not
withdraw a choice. **The tree could only grow:** whoever noticed a mistake had two
paths, both wrong — build on top of it, or redo the part from scratch, which
is exactly what the goal excludes.

**The machine already existed.** `PlanExecutor` opens and closes an undo
entry around each plan (entry B8, measured: «a single Ctrl+Z restores the
document to the value of `volume_before_mm3`»), and `Checks\Undo` proves it
works. It could only be triggered with a human keyboard. Same shape as section V:
the capability exists, and it never reaches the one who reads.

**A FIFTH tool and not a catalog operation:** operations run inside
`build`, that is, inside a plan, and undoing a plan from inside
the plan itself is a knot. The reason the tools are not thirty-six —
not exposing the operations one by one — does not apply to a gesture that is not
an operation.

#### The rejection comes first

Between the plan and the undo request the designer may have worked by
hand, and pressing undo then would take away **their own** work. So `build`
leaves a memory — document, volume and number of features before and after — and
`undo` checks it again. Three rejections, each with its own red case in
`Bench/test_undo.js`:

| case | what it does |
|---|---|
| no plan has been run | rejects: it would not even know what it would take away |
| called twice | the second call rejects: the memory is consumed on the first, and the next step would remove what was there **before** the plan |
| a different document in front | rejects, saying which one it had in hand and which one is there now |

**The memory is written even when the plan fails halfway through**, and that is the
case that matters most: a plan stopped at step 3 leaves the first two
built, and those are exactly the ones you want withdrawn. It is **cleared** instead
when the part has not changed — a plan that built nothing never opened any
undo entry, and remembering it would mean offering to undo something
earlier.

#### Two numbers are not a proof, and it is checked again afterward

Volume and count do not see a gesture that changes neither one nor the
other — a renamed name, a configuration. There is no API at all to look at the
undo stack before pressing (`IModelDocExtension` only has
`StartRecordingUndoObject` / `FinishRecordingUndoObject`). So it presses, it
**checks**, and if the part has not gone back to where it was it **redoes it**
with `EditRedo2` and says what happened. Trying, checking, and undoing the
wrong trial is the only honest path there is.

`HowManyFeatures` adds up `feature_not_listed`: without it, the count would
stop at eighty and say «nothing has changed» even after ten new holes — the
defect of E12, repeated from the other side.

### R3 — the sentences no longer name a key

Thirteen messages rewritten; **eight comments left as they were**, because
there whoever reads is a person, and for a person `Ctrl+Z` is the right gesture.

The sentence now lives in `Oce.Solid.Agent.Core/Sentences.cs`, in one place only. It was
written twenty-one times with nearly identical words, and the day it changed it
presented the bill: twenty-one places to fix.

And it says **two** things:

```
«undo» if you are talking to the bridge, Ctrl+Z if you are in front of the screen
```

This library has two readers. The model, which arrives through the bridge and has no
keyboard. And the person, who uses the same operations from OpenPDM's add-in,
where `undo` **does not exist**: naming only the tool would have sent them
looking in the menu for a command that is not there — the same defect as this
entry, with the recipients swapped.

### The bench says WHERE, not only what

`Why(ex)` in place of `ex.Message` in eighteen places: the exception's type,
message, first line of the stack trace, and out with the `TargetInvocationException`
wrapper that reflection puts around everything.

The reason cost a whole run: a test printed «Object reference not set to an
instance of an object.» and nothing else. That sentence says something was
null and does not say **what**, nor whether the null was in the bench or in the
library — and finding it meant relaunching seventeen minutes of tests.

### V1 — `inspect_part` can take a photograph

### V1 — `inspect_part` can take a photograph

Until now, «looking at the part» meant receiving a list: sixty faces
with area, normal and direction. For a drilled plate that is enough. For understanding what SHAPE
a shell with tabs has, no — sixty faces with an area are not a shape,
they are a list.

**The capability existed and was proven.** The bench has saved views with `SaveBMP` for
months. But it lived in the bench, and `SaveBMP` did not appear **even once** in
the library: the thing the reader needed sat in the program that checks whether
the library works. It is the shape of defect of entry E8 — a measurement taken
and thrown away — with the added weight that here it had never been connected to
anything.

Now:

- **`Oce.Solid.Agent.Core/PartView.cs`** (new): `Fire(model, which,
  width, height, out reason)` returns the bytes of a **PNG**. Nine views with
  the name used by whoever is drafting, the isometric first because it is the only
  one that alone shows a shape.
- **`Mcp/Protocol.cs`**: `ContentWithImages` — MCP provides for content of
  type `image` (base64 plus MIME type) and this protocol only knew how to do
  `text`. The text goes first: an image with no caption, in the middle of a
  conversation, is a riddle.
- **`Bridge/Session.cs`**: `Snapshot(...)`, which is where the hard
  half lives — knowing which document is in front.
- **`Tools/McpTools.cs`**: `inspect_part` gains `views`, optional,
  **at most three**.

**ON REQUEST, NEVER BY DEFAULT.** A 1024x768 view costs more than a thousand tokens:
more than ten times the entire state of the document, which stays under 16.000
characters. If every read carried three of them, the bridge's cost would change by an
order of magnitude — and even whoever only wanted to know how many bodies
there are would pay for it.

**PNG, and not the BMP that SOLIDWORKS gives.** `SaveBMP` writes uncompressed:
1024x768 is 2,3 MB, three in base64. The same frame in PNG fits in a few dozen
KB, with identical pixels.

**THE VIEW OF WHOEVER IS WORKING IS NOT TOUCHED.** The bridge attaches to the document a
designer has open. Turning the part to photograph it and leaving it turned
would mean moving the ground under their hands. The orientation is saved before and
put back in the `finally`, **even when the photograph fails halfway through**: a
real piece of damage done by an operation that is supposed to be read-only.

#### And putting it back was the hard part: six paths out of nine do not work

| path | outcome |
|---|---|
| reassigning `IModelView.Transform` | off by **2598** on index 12 (the scale) |
| `Transform` copied with `IInverse()` twice | same |
| `IModelDoc2.ViewOrientationUndo()` | off by 212 on index 9 |
| triple `Orientation3`/`Translation3`/`Scale2`, order O → **T** → S | off by **4,77** on index 9 |
| **triple, order O → S → T** | **comes back right** |

**`Transform` is not the view's state:** it is its description. The state a
view can be rebuilt from is the triple — where you look from, where you are
centered, how close you are.

**And the order is not a matter of style.** With O → T → S the rotation (indices 0-8 of the
matrix) comes back **exact** and only the translation is wrong: `Translation3` is
valid for whatever scale is in effect at that moment, so putting it back before
`Scale2` restores a position that is good for a different framing. The part came out
**straight but shifted**, which is worse than crooked — it looks right.

The number is what the measurement said: it was «off by 4,77 **on index 9**» that
pointed to the translation, after three reasoned explanations had been
proven false one after another.

#### `Checks\ViewTest`, because three seconds is not seventeen minutes

The first three paths were tried **inside the bench**, that is, paying a
seventeen-minute run for every answer. Now they sit in a tool of their
own that tries nine of them in three seconds and prints **by how much** and **on
which number** each one is wrong — and it was that detail, not the green/red, that
found the answer.

> A three-second question is not worth paying seventeen minutes for. If it
> is being paid, a tool is missing.

**THE STATE COMES OUT ANYWAY.** If no photograph succeeds, `inspect_part`
answers all the same with the list of faces and says in the text why the views are
missing. Failing everything because a shot could not be taken would take away from
the reader the one thing they needed.

**Three reasoned explanations, all three false.** Before the measurement found
the order, the red case «the orientation does not come back right» had three
diagnoses, each plausible and each disproved by the part:

1. «`Transform` returns the view's **live** object, so saving it saves
   nothing» — false: a measurement written on purpose showed that its sixteen
   numbers do **not** change on their own. It is a copy.
2. «then it's `ViewOrientationUndo()`, which exists for exactly this» — does not work.
3. «then it's the triple instead of `Transform`» — half right: the order was
   needed too.

> The direction of a fix is not worked out by reasoning about the shop: you ask
> the part. Here the part said no three times before saying where.

**Two compiler errors each worth one line**, because they were written against
an imagined API instead of a read one:

- `ShowNamedView2` returns `void`. The first draft treated it as a `bool` and
  built a check on top of it — a check on a value that does not exist.
- The *setter* of `IModelView.Transform` wants `MathTransform`, not
  `IMathTransform`. The compiler only says so on the line that restores the
  orientation — that is, on the line that cannot be tested without opening
  SOLIDWORKS.

**The bench test (`TestSnapshot`), with its own RED case.** Four claims
plus one: something comes out; **it is a PNG and not a BMP** (signature
`89 50 4E 47`); **the orientation comes back as it was** (sixteen numbers compared);
**a made-up view name is rejected, saying which ones exist** — without this
last one, «something always gets shot» would pass even if the name were never
checked. And at the end: no file left in the temp folder, because
`SaveBMP` wants a path and every shot writes 2 MB to disk.

When the orientation could not be read even BEFORE, the test came out
**failed**, not green: a check with no input has no answer, and one that
gives up saying «ok» is the defect this project exists to make
impossible.

### E12 — the ceiling on features no longer hides the latest ones

`DocumentState` listed at most eighty features and kept **the first
ones**: on an eighty-five-feature part, the five most recent — that is, exactly
the ones being talked about — disappeared. Now it keeps the LAST ones, and it
says how many are left out.

The walk of the tree now goes through a list of `Candidate` (feature, type, name): the
cut is made **before** calling `FeatureDetail`, so the eighty entries built
are the eighty that come out, and not eighty-five of which five are thrown away.

### `--lento`, and the skipped tests are counted

E12's test builds eighty-five cuts. Eighty-five cuts cost at least
**half an hour** — against the thirteen minutes of all the others together — and in
this run, at feature 84, SOLIDWORKS ground a core at 100% for
**eighty-eight minutes** without finishing one. A test that heavy is a test
that sooner or later someone skips.

So it only runs with `--lento`. **But a skipped test has to show:** the
bench prints it as `SALTATA` with the reason and counts it separately. «88 tests, 0
failed» for a run where one never started is a true sentence that makes
you believe something false.

### Two defects of my own, found by the review

**`NoteOnVolume` raised the alarm on a perfect hole.** The guard said
`volumeAfter < 0`, and `VolumeMm3` **never** returns a negative number: it answers 0 when
the document is not a part, when `GetBodies2` returns null, and from the `catch`
that wraps everything — that is, on every error path. The branch could never
fire, and a zero from a failed read passed as a measurement: `removed` became the
entire volume of the part, and on a successful hole it came out «192.000 mm³
instead of 392,7». A rejection of something correct, which this project
declares worse than the error it was meant to close. The comment two lines above
already said the right thing: it was the line that failed to do it.

**`shell` with `faces_ref` could open fewer faces than requested.** The
rule «all or nothing» held at the RESOLUTION level and not at the
SELECTION level: with three references resolved and two faces selected it went
ahead, «succeeded: true», volume reduced, and a container closed on a side that
should have been open. The twin branch, fifteen lines further down, already
rejected at the first face: a fix that covers one branch and leaves its
twin exposed.

---

## 0.30.0 — 14 September 2026 · library 0.65.0

**Two entries, and both turned out to be different from how they were written.**

### E8 — the mechanism had measured the displacement and thrown the measurement away

E8 was **two entries in one**, with opposite fates.

**Proportional tolerance is not done.** E5 had attributed it to this entry on
closing («the anchor is the number that makes a proportional tolerance possible,
and that is entry E8»). But after E6 re-finding works by persist ID, and the anchor
**is read by no one**: `McpTools` copies it into the signatures, `DocumentState`
computes it paying a `GetTessTriangles` per face on every `inspect_part`, and
then `EntitySignature` has no field to hold it, `FaceSignature` does not compute it,
and neither `SignatureMatches` nor `Corroborates` nor `MoveSentence` ever read
that key. **Zero consumers.** Eight hours for a problem already solved.

**E8's real text was something else, and it was broken.** The entry asks that,
when geometry moves, you **show whoever confirmed it what has
changed**. 0.26.0 built exactly that sentence — «it moved by
60 mm, from [50, 0, 0] to [110, 0, 0]» — and then handed it to `_log`:

```
_log  ->  Bridge/Session.cs:418  ->  Mcp.Protocol.Note  ->  Console.Error
```

**In an MCP server over stdio, stderr is not the answer.** Measured: `e2_case_a`
runs the hole on a part **60% longer** than the one confirmed, on the
right face, and in the JSON answer there was `succeeded: true` and nothing else.

> The mechanism written on purpose to have no silent defects had one of its
> own: it had measured the displacement and thrown the measurement away.

The right channel already existed and was empty: `Outcome.Measurements` goes straight
into the JSON, the way `note_volume` does. Now the re-anchorings drain into `Succeeded`,
which is **the single point every success of all thirty-six
operations passes through**, including the four `partial` files: no operation
can forget about it.

Three paths covered, not one: the persist ID, `Corroborates`'s note on the
grown face, and the search by signature — which used to say «it had moved: found
again by signature» **with no number**, and now uses the same sentence as the
persist ID. Two different paths to find it again, one single way to say so.

### E15 — `shell` accepts references, and the closed branch never worked

**`faces_ref`.** «Make a block and then hollow it out» is now a single plan —
the antenna container's sequence. Until yesterday `shell` was the only
operation on a face that did not accept a reference to a step: it wanted
`faces_uid`, which only come from a reading. Measured: volume **82.500**, the
same number the bench has measured forever for «shell 5 mm inward».

A list of **single** references, not a plural kind: `References`
resolves one face at a time and rejects when the role is not unambiguous, and that
rule is correct. And if even one fails to resolve, **everything fails**: a shell
open on two faces instead of three is not «almost right», it is a different part
that would have succeeded silently.

**`fully_closed` had never worked.**

```csharp
var corpoEntita = bodies[0] as IEntity;   // always null
```

A body is **not** an `IEntity` in the interop: that branch answered «I could not
select the solid body» on any part at all. Now it gives 105.000
mm³ on a 240.000 block with a 5 wall.

And the lesson was **already written in this very file**, four thousand lines
further up, in the comment on `move_bodies`: measured inside SOLIDWORKS on
10 September, with five tests saying «the indicated bodies did not let
themselves be selected», and fixed there already with `IBody2.Select2`.

> A lesson written and not applied is a lesson the code has not
> learned: the comment reminds whoever reads THOSE lines, and whoever writes
> elsewhere never passes through there.

### And two minor fixes, found along the way

`faces_removed` said **0** on a shell where a face really had been removed,
because it only counted the uids. A measurement that says zero when the number is
one is the defect of this project in miniature.

`build.ps1` used to stop halfway saying it had failed while it had actually
succeeded: PowerShell 5.1 wraps every stderr line from a native command in a
`NativeCommandError` and stops the script even when the executable exited 0. Now the
preference is lowered only around the native call, and `$LASTEXITCODE` is checked,
which is the only thing that truly says how it went.

---

## 0.29.0 — 14 September 2026 · library 0.64.0

**A cut that half-succeeded now says so.** Entry E13.

`cylindrical_cut` with the profile straddling the edge of the face removed
half the material and answered `succeeded: true`, `walls_drilled: 1`. Measured: a
hole of 10 depth 5, centered on the lower edge of a face, removed
**196,3 mm³ instead of 392,7** — exactly half — with the bottom reduced to a
half disc of 39,27 mm² instead of 78,54.

**The guard was there and was looking the other way.** `BelowProfile` knows
how to count how many points of the profile fall on the face and says so
correctly, but its own comment declares: «REJECTS NOTHING AND MUST NEVER DO SO: it
is only called when SOLIDWORKS has already said no». When SOLIDWORKS says
**yes**, nobody calls it.

### It is not rejected, it is reported

The operation succeeded and the part exists: rejecting after the fact would mean
throwing away a feature over a suspicion. New measures on `cylindrical_cut` and
`rectangular_cut` — `removed_mm3`, `expected_mm3`, and when they do not match a
`note_volume` that says by how much and what that usually means. It is the same
shape as `positions_not_checked` and the anchor that moves.

### Only on through-cuts is the expected value a multiplication

For a through-cut the volume depends on how much material is underneath, which is
not known without measuring it: there, `removed_mm3` alone is written, with no
expected value, which would be made up. **A false alarm on a legitimate
through-cut would be worse than the defect being closed.**

### The threshold comes from two measurements, not from an opinion

A hole entirely on the face removes **392,7** against the 392,699 expected —
less than a thousandth. One centered on the edge removes **half of
that**. Between the two there is a factor of five hundred: one percent lets
rounding through and lets nothing with a shape through.

### The first red case did not reproduce the defect

It was written with `face_ref: top_face`, and there the point (0,0) falls
**in the middle** of the face: the hole came out whole, `removed 392,7 expected
392,7`, all green. It looked like the fix worked, and nothing was
actually being tested.

The defect lives on the **side** faces, where (0,0) falls on the edge — and
naming one needs the uid, because the vocabulary of roles has no word at all
for the flanks of an extrusion. It is **exactly the 58% of faces with no
role** measured in E7 a few hours earlier: the two entries explained each other.

> A test case that has not been seen to fail is not a test: it is a sentence
> that resembles a test.

Checks: bench 88 tests 0 failed, with **zero** `note_volume` on eighty-eight
legitimate operations — which is the half that matters more.

---

## 0.28.0 — 14 September 2026 · library 0.63.0

**E7 measured, and the number demotes it.** New tool
`Checks/RoleCoverage`.

Entry E7 proposed naming a face by MEANING instead of by position — "the
bottom of the pocket" stays the bottom whatever dimension upstream changes —
and asked for a number **before** building, plus what to do with it:

> "If it's under half, this is a complement, not the main path, **and that
> has to be written down.**"

**31.4% of 86 faces, ten parts.** It's a complement.

### The average hides the thing that matters

| | faces | with a role | |
|---|---|---|---|
| cylinder | 3 | **3** | **100%** |
| drilled cylinder | 4 | **4** | **100%** |
| drilled plate | 7 | 3 | 43% |
| plate | 6 | 2 | 33% |
| pocket | 11 | 3 | 27% |
| two pockets | 16 | 4 | 25% |
| filleted | 10 | 2 | 20% |

The split is **geometric**, not statistical. A revolved part has a face on
top, one on the bottom and one cylindrical: three faces, three roles, no
ambiguity. A prismatic part has four or six **sides**, and the vocabulary has
no word for the sides. On "two pockets" that's twelve faces out of sixteen.

### And the roles that fire are the ones needed least

Of the 27 roles assigned: `top_face` **13**, `bottom_face`
**10**, `hole_face` **4**, `pocket_floor` **0**.

Twenty-three out of twenty-seven are "top" and "bottom", i.e. the faces that
already identify themselves perfectly well on their own. `hole_face` —
the only one that solves a real problem — covers **four faces out of
eighty-six**.

### Three different ways of having no role

Counted separately, because they get fixed in three different ways: **50**
for geometry (the sides: the only fix is widening the vocabulary), **8**
ambiguous (more than one cylindrical face on the same feature, where it
refuses rather than choosing: the fix is refusing better), **9** with no
readable direction (the data is in the model but can't be derived: the only
one of the three that's actually our own defect).

### Why this closes here

E6 solved both cases of E2, and on 0.1 the persist ID had found 8 faces out
of 8 across all five successful edits. The main path already exists.
Building a second mechanism that covers 31% — and for 85% of that 31% covers
"the top face", which was never the problem — would mean opening new
surface for a gain that can't be named.

> A twelve-hour entry closed by a two-hour measurement isn't work saved:
> it's work that should never have been done, and now we know why.

Three new entries remain, all sized on numbers already measured: **E14**
(the 8 ambiguous ones), **E15** (`shell` is the only face operation that
does not accept a reference to a step, found while building the ten parts)
and **E13** from the previous version.

`RoleCoverage` always comes out **0**: it's a measurement, not a check. A
low number isn't a failure, it's the answer.

---

## 0.27.0 — 14 September 2026 · library 0.63.0

**The trademark comes out of the product name.** Entry I1.

The name is **OCE Solid Agent**; the repository, the package and the
executable are called `oce-solid-agent-mcp`.

| | before | after |
|---|---|---|
| namespace | `SolidWorksMcp.*` | `Oce.Solid.Agent.*` |
| executable | `SolidWorksMcp.exe` | `oce-solid-agent-mcp.exe` |
| project | `SolidWorksMcp.csproj` | `Oce.Solid.Agent.csproj` |
| product name (installer) | `SolidWorks MCP` | `OCE Solid Agent` |
| name on the MCP protocol | `solidworks-mcp` | `oce-solid-agent` |
| first line of `NOTICE` | `SolidWorks MCP` | `OCE Solid Agent` |

29 files, 71 replacements, with `Checks/rename-product.js`: a script
and not twenty-nine edits by hand, because it redoes itself identically if
it gets something wrong, it prints what it touched, and a by-hand
replacement across twenty-nine files forgets one — the one nobody will ever
look at again.

### Renaming the repository wasn't enough

The product name didn't live only in the URL. It lived in `RootNamespace`,
in `AssemblyName`, in the executable's name that ends up in the
configuration of **every MCP client**, in the installer's payload and in the
manifest. Whoever downloaded the package found `SolidWorksMcp.exe` on disk.

### Two things that only showed up by doing it

**The first pass had only caught the joined spelling.** It had left behind
the six separate-word instances, which were **the most visible of all**: the
name the installer shows, the first line of the legal notice, the README's
title and — the worst one — the name the server declares **on the MCP
protocol**, i.e. the one the client reads.

And the second pass found **zero**, because the script's entry filter only
searched for the first form and skipped files that had only the others. The
count came out zero and looked like "nothing to do."

> A filter narrower than the list of replacements is a way of doing half the
> work without saying so.

**`AssemblyName` is the PACKAGE's name, not the namespace's.** On the first
build, `Oce.Solid.Agent.exe` came out while the build, the installer, the
bench and the README all pointed to `oce-solid-agent-mcp.exe`. Whoever
copies a line from the documentation has to find the file that's actually
there.

### What was not touched, and those are decisions

**`CHANGELOG.md`**: historical entries say what something was called back
then, and a changelog that gets rewritten is a changelog that lies.

**`Riscontro.Disegno`**: it contains no one else's trademark and is a good
name. The rename is there to remove a trademark, not to make things uniform.

**The word `SolidWorks` on its own**, which stays wherever it's needed:
naming the program this project interoperates with is lawful, necessary
use, and is the rule this project had already written for itself — a
trademark is an adjective that modifies a noun, never the product's own
name.

### And the TODO, which the rename had ruined

The script replaced the name even where the TODO **talked about** the name,
and I1's closing criterion had turned into its own opposite: "a grep for
`Oce.Solid.Agent` no longer finds anything." Nine references fixed,
distinguishing the **repository** (`oce-solid-agent-mcp`) from the **C#
project** (`Oce.Solid.Agent.csproj`) — which after today are two different
names.

> A mechanical replacement over a document that describes the past rewrites
> the past too.

Checks: bridge restarted under the new name (attached, library 0.63.0.0, 36
operations); bench 88 cases, 0 failed; GearTest, OutlineTest, PositionsTest
at 0; the bridge runs plans and the negative case is still refused.

---

## 0.26.0 — 14 September 2026 · library 0.63.0

**The persist ID proposes, the measurement judges.** Entry E6.

When an upstream dimension moves a face, the anchor on the point gets lost
and the plan is refused even though it's correct. Now, before searching
blind, SOLIDWORKS is asked: `GetObjectByPersistReference3` is the only
anchor that isn't one of our own conjectures about the geometry.

### The two cases of E2, and the second one solved itself

| case | before | after |
|---|---|---|
| **A** — `F6` is cited, then the part is stretched from 100 to 160 | REFUSAL | **solved**, and it says so: "it moved by 60 mm, from [50, 0, 0] to [110, 0, 0]" |
| **B** — the bottom of the pocket drops exactly where the top face used to be | solved by signature, **and the part was wrong** (E3 had closed it with a refusal) | **solved correctly**: volume **58,885.8** |

Case B is the best of the two possible outcomes. The entry said: "if the
persist ID made the right face come back there too, case B becomes a
non-regression test, not a solved case." It happened — **58,885.8** against
the **59,042.9** of the half hole, i.e. the correct part's number.

### Never alone

What SOLIDWORKS returns is a **candidate**, and it goes through the same
judge as all the others — `Corroborates`, with the signature **re-measured
now** on the face that was found, not the one the plan carries with it,
which is exactly what's being checked. `GetObjectByPersistReference3` can
return a different face with no error after a rebuild that changes the
topology: that would be the worst defect in this entire section, because it
would give no error.

`IsSamePersistentID` **is not used**: on 0.1 it answered true 46 times out
of 46, even where the area had doubled because two faces had merged. It
attests the identifier, not the face. The real signal is `ErrorCode`
different from zero — which is exactly what happens to faces deleted by a
suppression, and there the usual search is left to speak.

### Where it lives, and why not in the state

**375-423 bytes per face**, i.e. ~530 characters in base64: over sixty
faces that would be **~32 KB**, twice the entire state ceiling. It travels
in the **step's signature** — an internal attachment that never reaches any
model — and the bridge strips it from the state right after building the
signatures. It's the same route as the anchor in 0.25.0, multiplied by
twenty. State measured afterward: unchanged.

`DocumentState.Model` is a **property**, not one more parameter on
`Read`: changing a method's set of signatures breaks whoever calls it by
reflection even when the compiler stays silent. A lesson from 0.25.1,
applied before running into it again.

### Two wrong numbers in the TODO, corrected by measuring

Entry E6 said the face moves **to X=80**. Measured: **X=110**. The part
grows from one side only even though the sketch is centred, so the
right-hand face takes the whole 60 mm of growth — the same trip-up already
fixed in E2, where the TODO said 30 mm and the measurement said 60.

And the uid **stays `F6`**: areas aren't reordered, so renumbering has
nothing to do with it. The refusal came purely from the point comparison,
and that's the cleanest proof that this entry was needed: there was no
ambiguity to resolve, only a lost anchor.

### And a defect that gives no error, made visible

Case A, the moment it stopped being refused, produced **half a hole**:
`y_mm: 0` puts the centre on the face's bottom edge, and the operation
removed **196.3 mm³** instead of 392.7 while answering `succeeded: true`.

It isn't a defect of this version — it was there all along, covered by the
fact that this plan used to be refused before getting there. `BelowProfile`
can count how many points fall on the face, but its own comment states: "IT
REFUSES NOTHING AND MUST NEVER DO SO: it's only called once SOLIDWORKS has
already said no." When SOLIDWORKS says yes, nobody calls it.

The case was moved to `y_mm: 10`, where the hole sits entirely on the face
and the numbers add up (volume **191,607.3** = 192,000 − π·5²·5). The
defect is entry **E13**, with the numbers already measured.

> Removing a refusal doesn't just remove the refusal: it also takes the lid
> off whatever was underneath it.

---

## 0.25.2 — 14 September 2026 · library 0.62.0

**Seven checks that weren't checking, found by a review done before the
push.** One of them was watching over the bench.

### 1. The bench was promoting itself

`HoleOperations` called `ForiMancanti` and then wrote, unconditionally:

```csharp
measurements["positions_checked"] = true;
```

`ForiMancanti` returned `null` — "all clear" — even in **four paths where it
had looked at nothing at all**: unknown diameter, `GetFaces()` null, the
`catch`, and above all **no cylindrical face of the expected diameter**,
which is exactly the case where the holes might not be there. One of its
`return null` even had the right comment on it: *"nothing to compare: a
verdict is not invented."*

> **The function refused to invent the verdict, and whoever called it
> invented one in its place.**

And the bench decided the pass with
`measurements.ContainsKey("positions_checked")`: a key that was always
written. `verified` was true by construction, and the branch that
printed "POSITIONS NOT CHECKED" was **dead code**. The five `hole_wizard`
cases were measuring the diameter while believing they were also measuring
the positions.

Now the outcomes are **three**: `Verified`, `NotVerifiable`, `Wrong`.
"I couldn't look" is not a refusal — the feature succeeded, it's the check
that didn't happen — and it travels in a **different** key,
`positions_not_checked`, with the reason inside: a
`positions_checked = false` would have been read as "checked" by anyone
who just looks at whether the key is there.

The verdict lives in `PositionsVerdict`, **without SOLIDWORKS**: collecting
the cylindrical faces needs COM, deciding what to do with them doesn't.
Separated, the case that matters — zero faces found — gets written in one
line, and with SOLIDWORKS running there was no way to force it to happen.
`Checks\PositionsTest` (20 cases).

One test started out red, and it was right to: "deviation of exactly 0.1
mm" is not a testable case. The round trip mm → m → mm gives
0.10000000000000142, and the verdict hinged on the last bit. In its place,
what's measured is the **separation**: 2.9e-15 mm of noise against a
threshold of 0.1 — fourteen orders of magnitude.

### 2. The notch flush with the edge, refused from only one side

`ClosedShape.Inside` uses crossing parity, which **can't answer on the edge
itself** and answers according to which side: with the `x < cut`
comparison a vertex on the right edge comes out outside, the same one on
the left comes out inside.

On a 100×100 plate: a notch flush with the **right** edge —
(30,−10)(50,−10)(50,10)(30,10) — had two vertices "outside" and two
"inside", and was refused as "straddling the outer edge." The same notch
**mirrored to the left** had all four vertices "inside", and passed.

Same geometry, opposite verdict depending on the side. One of the two was
wrong, and SOLIDWORKS said which.

**The first fix went the wrong way, and it's worth writing down.** The
reasoning was: "a notch flush with the edge is an open slot, it's done
every day, so both should be accepted." That's true of the **part** and
false of the **sketch**: two contours that share a stretch of edge don't
bound a region, and an open slot is drawn as **one single** contour with
eight points, not as two contours touching each other.

Measured — three geometries, a single result, "The extrusion was not
created": the notch on the right, the same one mirrored on the left, and a
diamond that touches the edge at **a single vertex**.

So the correct verdict is **no from both sides**, and what was broken was
the **yes** on the left. What `WhereItIs` gains isn't letting more through:
it's answering the **same way** on both sides, and giving a better reason
than the one SOLIDWORKS gives — "TOUCHES the outer edge at 2 points" — plus
the way to get the slot that was actually wanted.

> The direction of a fix isn't derived from reasoning about the shop floor:
> you ask the part. "It's legitimate work" said how it **should** go, not
> how it **does** go.

The edge band is a micrometre wide, with ten million times the margin over
noise and ten thousand over the smallest sensible dimension. In
`OutlineTest` the test on the two mirrored notches checks **symmetry** and
nothing else — which answer is correct is for the part to decide — and it
remains the proof that `Inside` alone **is** asymmetric: if someone fixes it
one day, that line will tell them so.

### 3. The radius that was too large got through on one path and not the other

The comment on `SelectEdges` promised that "every check that matters is
here", including the one that checks whether the requested dimension fits
within the edge's length. The `edges_ref` branch returned with
`return true` **before** getting there: a 30 mm fillet on a 10 mm edge was
refused via `edges_uid` and let through via `edges_ref`, all the way to
`FeatureFillet3`, which fails saying only that it wasn't created.

> There was one method, but it had two paths, and that alone was enough to
> feel safe. **Putting two paths under the same comment is no less
> dangerous than putting two copies under two names.**

### 4. The ceiling on features didn't say it had been hit

Three ceilings out of four declared themselves. `MaxFeature` didn't:
features past the eightieth ended up in `other_feature` together with the
ones of an ignored type — two different things added together — and
`truncated` stayed `false`.

It was the worst of the four to stay silent about: the tree is walked in
**creation order**, so the eighty listed are the **oldest**, and the one
the plan just created is among the discarded ones. The model read
"complete list", didn't find the new feature, and could conclude the
operation had done nothing — and redo it.

Now: `feature_not_listed`, `feature_total`, `truncated: true`. Which ones
get kept is still to be decided, and that's entry **E12**: it needs a light
pass first and the detail afterward, because `FeatureDimensions` costs COM
calls.

> **A ceiling that doesn't say it's been hit isn't a ceiling: it's a wrong
> answer presenting itself as complete.**

### 5. The plan checked for "everything" stopped at the first level

The description of `build` promises the model that *"if any step has
arguments that don't match the schema, nothing is executed and the document
stays as it was."* `Bridge.Shape`, for `type: "object"`, checked that it was
an object and **didn't look inside**: neither the nested `required` nor the
`properties`.

The schema of `linear_pattern` declares `feature_ref` with
`required: ["step", "kind"]`. A four-step plan whose fourth step writes
`feature_ref: {}` passed the whole upfront validation, the first three
steps got built into the document, and the fourth one failed. **The
document was left half-done.** The all-or-nothing guarantee is exactly
what that check exists for: keeping it only at the first level means not
keeping it.

Now it goes down to three levels and the message names the field in full
("feature_ref.step"), because the same name "step" sits under three
different references in the same schema.

### 6. The state showed a name, and the check declared it nonexistent

`UidsThatDoNotExist` stops a plan that cites an identifier that wasn't
there. But it only collected **the `uid` field of the five families**,
while the state publishes feature names in three other places too:
`from_feature` on every face (put there on purpose for "take that hole to
M10"), `created_by` on every body, and the `features` map. And the
`feature` list is filtered by type and cut off at `MaxFeature`.

A part with more than eighty features, or one of an unlisted type, was all
it took for the state to show a name and the check to call it
nonexistent — **a false refusal caused by the output of the very thing that
emits it**, and the most galling kind, because whoever wrote that name read
it in the previous answer.

### 7. The anchor was being paid for even when it was thrown away

`Bridge.Session.ReadState` had the flag hardwired to `true`. The "before"
read of `build` only needs to know which document is being worked on, how
much volume there was, and how many features were in error: no signatures
are built from it, and it isn't returned to the reader. It was paying the
same price anyway — a `GetTessTriangles` call and an area-weighted
centroid **on every face**.

Now there is `StateWithoutAnchor`. A different name, not one more parameter:
that's the lesson of `ReadWithAnchor` from 0.25.1.

### A bench test was measuring even what it shouldn't have

`state: under 16 KB on a four-bar frame` failed at **16,280
characters**, against **15,628** an hour earlier, **with no code change at
all**.

The state lists `documents_open`, so the measurement depends on whatever
is open in SOLIDWORKS besides the frame. Six parts saved by a
`run_plans.js` run had been left open: ~650 characters, and they were the
entire difference.

And `CloseLeftovers --chiudi` does **not** close them: it deliberately spares
documents that have a file. It needs the folder —
`CloseLeftovers.exe --chiudi "...\Bench\lavoro"`.

Now the message **always** prints how many documents were open, green or
red, and says so when there's more than one. A test that depends on the
environment isn't wrong — it's wrong if it doesn't declare it, and half an
hour spent chasing a regression that wasn't there is the price of not
declaring it.

> A message that gives the criterion is useful for reading the green
> results too, not only the red ones.

### And five bench cases could no longer run

`run_plans.js` was creating `Bench\lavoro\profili\` **empty**. The five
frame cases name `{BENCH}/profili/QUADRO_COPIA.SLDLFP`: before the fix with
the placeholder (0.25.0), that path pointed at the session scratchpad,
where the file existed because someone had put it there by hand. The
placeholder made the path portable and left the file **with nobody to
provide it**.

They answered "The profile ... does not exist on this machine", which is
the right message for a problem that wasn't the actual one. Now
`prepareFolder` copies the standard profile from `PROFILI_SALDATURA`,
and if it can't find it **it says so** instead of dying: whoever clones the
repository doesn't have that library and still has to be able to run every
other case.

It's copied rather than using the original because inserting a profile
**opens** its `.SLDLFP` and leaves it dirty: the bench would end up working
on the shop's real standard library.

### And, last, whoever clones the repo

`build-checks.ps1` had the interop path hardcoded. Now it accepts
`-SwRedist` and reads `SW_REDIST`, the same variable `Bench\README.md`
already documents.

---

## 0.25.1 — 14 September 2026 · library 0.61.0

**The guard was watching the tree, and `git push` ships the history.**

One minute before the first push, `check-publishability.ps1` said
"Clean." The working tree really had been fixed. But in the **history** the
scratchpad path with the username was still there, in **155 spots across
41 commits**.

Not two, as `git log -S` claimed: that one flags the commits where the
count **changes**, while the blob itself lives on in every intermediate
tree until it's actually removed. You look at each commit's tree, not its
diff.

The forty-one commits weren't on `origin` yet, so rewriting them cost
nothing and broke nothing: after the push it would have been a force-push
over published history. `Checks/cleanup-history.js` plus
`git filter-branch` over just the `origin/main..HEAD` range, so the two
already-published commits stay intact. **The final tree is bit-for-bit
identical to before** — it was already fine; it was the history behind it
that wasn't.

### The perimeter wrong three times over

1. `check-catalog.ps1` looked inside `catalog.json`, because when it
   was written the catalogue was the only thing that came out.
2. This one looked at the whole tree, but only seven **extensions**:
   `App.config`, `app.manifest`, `LICENSE` and `NOTICE` didn't have a good
   one.
3. And it only looked at the tree.

Now the perimeter **is no longer a list, it's whatever comes out**:
`git ls-files` plus whatever isn't tracked yet and isn't ignored — i.e.
exactly what a `git add -A` would bring in. The extensions have become an
**exclusion** list (the binaries): a new kind of text file enters on its
own. **An inclusion list fails silently, an exclusion list fails loudly.**

### And the check wasn't actually reading the files

Only noticed because the new guard was first pointed at the dirty history,
to see it go red. `Select-String`, given an array of strings over the
**pipeline**, treats them as **text to search**, not as a list of files: it
was searching for the username inside the *path names themselves* and
answered "ok, none" without having opened anything. It was found because
`S:\` matches every absolute path on this drive and blew up a `Substring`
call. Fixed with `-LiteralPath`, and run to red across all five categories
before trusting it.

Two minor traps, measured here: a PowerShell function called `Git` makes
`& git` resolve to itself (infinite recursion, and the error only says
"Call depth overflow"); and the exception list for files that may
legitimately name the forbidden ones must be written **by name, not by
path**, because in history `check-catalog.ps1` lives in `Tools\` —
files move, their job doesn't.

---

## 0.25.0 — 14 September 2026 · library 0.61.0

**The point that selects and the anchor that identifies are two different
things.**

`point_mm` is a point **guaranteed to sit on the face**, taken as the point
nearest the origin: fine for selecting — the centre of a face with a hole
in it would fall into empty space. As a fingerprint it's a bad one, because
it's a point on the **boundary**.

A 100 × 60 × 20 plate, a 15 × 15 notch in one corner — **225 mm² out of
6,000, 3.75%**:

| | before | after | shift |
|---|---|---|---|
| `point_mm` | `[0,20,0]` | `[15,20,0]` | **15 mm** |
| `anchor` (area centroid) | `[50,20,-30]` | `[51.656,20,-30.877]` | **1.87 mm** |

### The entry's criterion was already met, and it had to be measured

"Ten identical readings, and one after an empty rebuild." Tested on the
rotationally symmetric case where the points at minimum distance are
infinite (`extrude_circle` Ø40 with a coaxial countersink): **ten out of
ten identical**, and identical after the rebuild too. The feared
instability isn't there in this version.

### And the anchor alone removes no refusal

The tolerance is 0.05 mm and 1.87 exceeds it anyway. The anchor **is not
the fix**: it's the number that makes a tolerance proportional to the
change possible, and that's entry E8. `SignatureMatches` therefore keeps using
`point_mm`: changing the comparison term without changing the tolerance
policy would move the refusals around without removing them.

### The anchor is not sent to the reader

Two jobs, two recipients. Offering two points to whoever only needs one,
and the wrong choice wouldn't raise an error: it would give a hole outside
the material. And over sixty faces the anchor is 1,400 characters — the
state was going up to **17,171**, past the 16 KB ceiling. It's stripped
after the signatures are built, which carry it along with them.

### The defect that cost half an hour

`IFace2.GetTessTriangles` returns a **`float[]`**, not a `double[]`: an
`as double[]` comes back **silently null**, and the anchor came out empty
on every face with no error at all. Third time for this family — and the
interop said so: `IGetTessTriangles` is declared `Single`.

---

## 0.24.0 — 14 September 2026 · library 0.60.0

**Bodies were checked for existence, not for identity**, and the code
comment said the opposite.

### The red case, measured

Two separate blocks: `C1` the large one at `[-70,10,0]`, `C2` the small one
at `[60,10,0]`. A two-step plan: cut the large one in two, then
**move C2**.

The cut renumbers the bodies. "C2" becomes **half of the large block**, and
that's the one that gets moved:

```
succeeded: true         (no warning)
C2  center [-87.5, 110, 0]   <- half the large block, moved
C3  center [60, 10, 0]       <- the small block, never touched
```

### The comment already promised the check

`BodiesFromUid` said, above: "A body's signature is the centre and volume of
its bounding box... **It's the same check done on faces.**" The code never
actually called `SignatureMatches`.

> A comment describing a check that isn't there is **worse** than no
> comment at all: whoever reads it stops looking for the hole exactly where
> it is.

The hole was double: the bridge wasn't copying `center_mm` into the
signature either, so for "C1" there was nothing to compare even if you
wanted to.

### Now

```
"C2" now points to a different part of the part (moved by 17.5 mm). The
part has changed since you confirmed it: I will not work on the wrong body.
```

And moving a body that hasn't moved still succeeds.

**The right word for the right thing:** the same comparison serves faces,
edges and bodies, and "I will not work on the wrong face" said about a body
sends someone off looking for a face that has nothing to do with it.

### Features: `feature_id`

A feature's uid **is its name**, and a name can be renamed: whoever renames
"Taglio1" and then calls another feature "Taglio1" has transferred the
identifier, and `SelectByID2` obediently selects the new one. The state now
carries `feature_id` from `IFeature.GetID()`, and it's compared before
selecting.

### The axes are still open

The state of the axes is `{uid, kind}`: there's nothing to sign, so
`axis1_uid` and `axis2_uid` are still checked for existence, not identity.
It would take publishing point and direction — work of its own.

### And the state says what's SELECTED

Not a new tool: one more line in what's already being read, written **only
when something is selected**.

```
nothing       the field is absent
one face      [{"kind":"face","uid":"F5"}]
three at once [{"kind":"face","uid":"F5"},{"kind":"face","uid":"F3"},
               {"kind":"edge","uid":"E9"}]
```

The identifier is **the same one** the state assigns, so the designer can
point at the face with a finger instead of describing it in words: the
ambiguity is removed before the operation instead of being discovered
afterward.

The two awkward cases are **decided**, not merely discovered: nothing
selected → the field isn't written; more than one thing → all of them are
returned, because picking one would mean guessing. And a third: a selected
entity that has no name in the state (past the sixty-face ceiling, or a
vertex) states **what it is** without giving it a name.

### The name of features lives in a map, not on every face

Writing it on every face pushed the state past its 16 KB ceiling: on a
welded frame the names are «TUBO STRUTTURALE QUADRO 80X80X5(1)», and sixty
faces coming from four features repeated it sixty times —
**18,629 characters**, against a limit the bench watches.

With the map at the bottom (`features: {"12": "..."}`) and just the
number on each face: **15,752**. The margin is thin, and needs watching.

---

## 0.23.0 — 14 September 2026 · library 0.59.0

**Blind-eyed matching is closed.** It was the only way the system could
produce a wrong part without saying so.

### Before

Matching by signature accepted the candidate by comparing **only point and
normal**. In case B of entry E2, after thinning the part down, the top
face (area 4,400) ended up exactly where the plan was looking for the
bottom of the pocket (area 1,600). The point matched, the area was almost
triple, and it was taken anyway: `succeeded: true`, half a hole in place of
a whole one.

**The point had won over the area.**

### Now

```
Face "F5" has moved, and the only one that matches by position comes from
«Estrusione-Estrusione1» instead of «Taglio-Estrusione1»: it isn't the same
face, it's a different one that ended up in the same spot.
```

The part stays intact: bottom at 1,600 and top face at 4,400, both where
they were.

### Two checks, and neither is "the area must match"

1. **the same feature**, by `IFeature.GetID()` and not by name: the name is
   localized and renameable, and the same face would stop being the same
   face just because someone changed SOLIDWORKS's language;
2. **area can only decrease** — a monotonic constraint, not a comparison: a
   hole removes area and is legitimate, but never adds any.

These sit **outside** `SignatureMatches`, not inside it: that comparison also
decides whether the starting face is still good, and there, growing area
is normal. Corroboration only applies once a replacement is already being
looked for.

The state now carries `feature` and `feature_id` on every face —
useful to the reader too, not only to the check.

### The counter-proof

The cover's four slots, which was the case that must not break:
**four out of four**, three of which were recovered by the signature. The
criterion asked for three.

### And a defect that was MINE, fixed: I was refusing legitimate work

The 0.14.0 contour check refused the plan for the **cover's four tabs**,
which is correct: four **separate** contours on a face are four regions,
not four voids outside the boundary.

The cases are now three:

| | |
|---|---|
| inside | it's a void |
| **straddling** | cuts across the edge: always inconsistent, **refused** |
| **outside** | it's a region of its own: **accepted, and said so** |

The third produces `note_contours`, next to `bodies_total`. The defect of
the plate with the detached circle — 2 bodies, "succeeded" — now goes
through **with the note and the body count in plain sight**, instead of
being blocked.

> Refusing something that's correct is the worse of the two ways to be
> wrong.

---

## 0.22.0 — 14 September 2026 · library 0.58.0

**The two cases of entry E2, built before fixing anything.** In
`Bench/cases/E2-the-two-cases.md`, with the expected numbers next to every
step, and runnable **with no language model at all**.

Case B is the one that matters: after `set_dimension` 20 → 12, the top
face ends up where the plan is looking for the bottom of the pocket, and
the matching logic takes it.

```
right      58,885.8   cylinder 125.66 mm² = π·10·4 at [5,0,0]
wrong      59,042.9   cylinder  62.83 mm² at [-5,0,8]
```

157.1 mm³, 0.27%: half a 2-deep hole instead of a whole 4-deep one. Clean
rebuild, no warning, "succeeded: true".

**Two numbers in the entry corrected by measuring:** case A's shift is 60
mm, not 30 (the part grows from one side only, not symmetrically); and
case B's pocket has to be centred in Y, or it runs off the edge and
becomes 1,200 mm² instead of 1,600.

### `set_dimension` was suggesting a form that didn't work

The message says "for example «D1@Schizzo1»", and that form was being
refused: the comparison wanted either the short name or the full one with
the document appended. **The message was lying.**

It isn't a matter of convenience: the form with the document contains the
**title**, so a bench case written that way can't be rerun on a different
part. Now all three forms are valid, and cases that change a dimension are
repeatable.

---

## 0.21.0 — 14 September 2026 · library 0.58.0

**Cited identifiers must exist.** It's the other half of the anti-drift
defence, and it catches a case signatures can't catch.

### The red case

`[extrude_rectangle, cylindrical_cut on F1]` on an empty document:
**succeeded**, volume 117,738.1. The hole ended up on the largest face —
which in that case also happened to be the right one, but **nobody had
chosen it**. "F1" is positional and gets renumbered by area on every read.

### Why signatures weren't enough

A signature compares an identifier against the geometry it described when
the plan was created. If the face **didn't exist at all**, there's no
signature to compare against, and so no refusal.

> The signature guards against **drift**; this guards against
> **invention**.

### The message points to the right path

```
"face_uid" cites "F1", which wasn't there in the part I looked at.
There was no identifier at all in the part I looked at: it was empty.

Identifiers come from "inspect_part" and are only valid for THAT
reading. If the geometry is created by an earlier step of this same plan
[...] it must be named with a reference to the step — for example
"face_ref": {"step": 1, "kind": "top_face"}.
```

A refusal that doesn't point to the right path only teaches you to try
again at random. It applies to all thirteen `*_uid` fields of the
catalogue, whether string or list.

### What's legitimate goes through

| | |
|---|---|
| the same plan with `face_ref` | passes, 117,738.1 |
| `face_uid: "F1"` on a part that already exists | passes, −π·4²·20 |
| hole / pattern / mirror by uid, axis and feature | pass |

---

## 0.20.0 — 14 September 2026 · library 0.58.0

**The bench enters the repository, and turns out to have been broken for
three days.**

`Bench/` with its four sources, `Bench/cases/` with **112** plans, the
bridge's runner (`run_plans.js`, `inspect.js`) and `Bench/README.md`. All
of it was sitting in a temporary session folder.

### It was broken, and nobody could tell

The bench was loading `OpenPDM.SwAddin.dll` and asking for the types
`OpenPDM.SwAddin.Agente.OperazioniSw`. Entries B4 and B5 had moved the
operations into `Oce.Solid.Agent.Core`: ever since, `GetType(..., true)`
throws.

Three days with it not even starting. Nobody had noticed **because it
lived outside the repository and nothing ran it anymore**.

> A measurement that can only be redone from the one machine and the one
> week it was written in isn't a measurement: it's an anecdote.

### Put back on its feet

* the types now point to `Oce.Solid.Agent.Core.*`;
* the library's path becomes `--libreria <path>`, defaulting to next
  to the executable — it used to be a constant pointing inside a
  temporary folder;
* `Assembly.Load(File.ReadAllBytes(...))` instead of `LoadFrom`, because
  the library sits on a network path and .NET refuses to load from there;
* **`Main` split in two.** The `AssemblyResolve` handler has to be
  installed before the JIT compiles a method that names an interop type,
  and the JIT compiles the whole method before running its first line:
  inside `Main` it arrived too late, and the exception came out as "in
  Program.Main" — i.e. **before any line actually written in there**.

### Dassault's libraries: two defences instead of one

`bin/` in `.gitignore`, **and** `Private=False` in the csproj, so the
interop assemblies never get copied at all. At run time, whatever is
installed on the machine is used (`SW_REDIST` for non-standard paths).

Verified: `Bench\bin\` holds **only the executable**.

### And the bench closes what it opens

Every plan left the part it created open, and the state enumerates all of
them on every read: 244 ms per document just for `GetNext()`. After fifty
cases the bench slows itself down by a minute and a half per reading.

It isn't a matter of comfort: as long as the bench is dirtying itself,
**the times it measures aren't the product's times, they're its own**.

`run_plans.js --pulisci` reads which documents were open **before**,
opens a clean part for each plan, and **closes it after each one** —
`CloseLeftovers --risparmia` keeps out the ones belonging to whoever was
actually working.

The same plan run twelve times, both ways (`Bench\measure_accumulation.js`):

```
NEVER closed: 10.2 11.1 11.4 11.1 11.3 12.1 12.2 13.3 13.8 13.9 15.2 15.4
              first 10.2 s, last 15.4 s (x1.5)   12 documents at the end

CLOSED after every plan:
              10.7 10.7 10.1 10.3 10.1  9.9 10.9 10.0 10.1 10.6 10.4 10.2
              first 10.7 s, last 10.2 s (x1.0)   0 documents at the end
```

**0.43 seconds per accumulated document.**

Two methodological mistakes before getting there, and they're the same
mistake — measuring one thing while believing you're measuring another:

* the first measurement lined up nineteen **different** plans and watched
  the times grow. They grew because the first one was a tube and the rest
  were gears with 336 segments: what was being read was the order of the
  list;
* the first cleanup only closed things **at the end** of the run.
  Documents accumulate during the run, and it's during the run that the
  state enumerates all of them: the times came out identical to running
  with no cleanup at all.

The comparison that isolates the right variable is the same plan repeated.

---

## 0.19.0 — 14 September 2026 · library 0.58.0

**The error count is given as a delta, not as a raw number.**

"The part has 1 feature in error" after a plan sounds like it's saying
"the plan broke something." It can mean the opposite: the part was already
broken when it arrived. The absolute number **lies about who did it**.

```
a plan that breaks something:
  features_in_error: 1   features_in_error_before: 0
  note_errors: "...this plan broke 1 of them."

an innocent plan on an already-broken part:
  features_in_error: 1   features_in_error_before: 1
  note_errors: "The part already had 1 feature in error before this
                plan...: this plan didn't break them."

a healthy part that stays healthy:
  (nothing)
```

The third line matters as much as the first two: repeating "all normal" on
every answer is room taken away from what actually matters.

### The MEASURE channel already existed, and now it holds both readings

`inspect_part` is a tool of its own, `readOnlyHint`, which goes through
no confirmation at all because it doesn't draw anything — exactly route
(c), which entry D5 proposed. There was no need to build another one: what
was needed was putting the tree inside it (done in 0.18.0), next to volume
and bodies, which were already there.

| case | expected | measured |
|---|---|---|
| healthy 10 × 10 × 10 cube | `GetWhatsWrongCount` = 0 | **0**, volume 1000, 1 body |
| minus a through-cut 4 × 4 | 840 mm³ | **840**, 1 body |

### A case that doesn't reproduce, and that's good news

The criterion called for an R=20 fillet on a 10 mm edge, with code 18. The
feature **is not created at all**: the operation says "The fillet was not
created" and the tree stays clean. The library doesn't leave red features
behind when the operation fails.

The real red case is the other one: a **valid** feature that becomes
impossible because of a later edit (0.18.0, code 12).

---

## 0.18.0 — 14 September 2026 · library 0.58.0

**The state says when a feature is broken.** Before, it didn't, and that
was a defect that gave no error **behind the glasses**: the model was
looking at a broken part and seeing a healthy one.

### The red case, built before fixing it

A 100 × 60 × 20 plate, an 8 mm fillet on the top edges, then the width
brought down to **10 mm**. Two 8 mm fillets don't fit in 10 mm, and
SOLIDWORKS knows it:

```
GetWhatsWrongCount: 1
Raccordo1 [Fillet] error=12  swFeatureErrorFilletInvalidRadius
```

`inspect_part` answered
`{"uid":"Raccordo1","kind":"Fillet","radius_mm":8}` — an ordinary fillet.
And the volume was **3,000**, i.e. exactly 100 × 10 × 3: the fillet had
done nothing at all.

### Now

```
features_in_error: 1
"error": "the fillet's radius doesn't work for this geometry: it usually
           means it's bigger than there is room for"
"error_code": 12
```

On the same part once fixed, the field **doesn't appear at all**. The two
tests go together: without the second one, the first would say nothing.

### `NeedsRebuild2` wasn't enough

On the broken part it answered **0**, while `GetWhatsWrongCount` said 1.
"It needs rebuilding" and "it is broken" are two different questions, and
using the first to answer the second would have given a guard that's
**always green**.

### What isn't known stays a number

`FeatureErrors.cs` translates the known codes — fillets (10-19), extrusions
(30-37), the dangling reference (51). For the others, out comes the enum's
name and the number.

> A made-up name on a code is worse than the bare number, because it looks
> like an explanation.

### The window that stalls the bench

`PlanExecutor` turns off `swShowErrorsEveryRebuild` for the duration of
the plan and restores it afterward. A rebuild with an error opens a modal,
and the COM call that triggered it **never returns**: the same lesson as
`swInputDimValOnCreate`. Tested with the window **turned on**: the plan
that breaks the part runs all the way through, and the option goes back to
what it was before.

### New tool

`Checks\RedTree` — `NeedsRebuild2`, `GetWhatsWrong` and the error
code of every feature, with the enum's name next to it.

---

## 0.17.0 — 14 September 2026 · library 0.57.0

**Cuts carry their own dimensions.** `cylindrical_cut` adds the
diameter, `rectangular_cut` length and width. Without them those parts
couldn't be edited with `set_dimension`: they could be made but not
corrected, which is the shop floor's most frequent complaint.

### Measured

A 120 × 80 × 20 plate, a through Ø12 hole, a through 24 × 16 pocket:

```
volume: 182,058.1     expected 192,000 - pi*6^2*20 - 24*16*20 = 182,058.05
```

Then `set_dimension` on the diameter, 12 → 20:

```
before 182,058.1   after 178,036.8   difference 4,021.3
expected pi*(10^2-6^2)*20 = 4,021.24
```

### Two decisions, and the reasons why

**What gets dimensioned is the SIZE, not the position.** The position
would need a reference to measure from, and choosing one on the designer's
behalf means constraining the sketch more than they asked for: the
following `set_dimension` would then fail with an over-constraint message
that explains nothing.

**Dimensions are born marked for the drawing**
(`IDisplayDimension.MarkedForDrawing`). `model_dimensions` imports only
those by default — a drawing with every single model dimension is
unreadable — so an unmarked dimension would never have made it onto the
drawing.

### The missing Z

`DimensionSegments` was passing `AddDimension` the text point as `(x, y, 0)`.
That's fine for a sketch on a reference plane, where Z is zero by
construction; it is **not** fine for a sketch on an arbitrary face, which
is where every cut actually lives. Now the point has three coordinates.

### Still not working: `model_dimensions`

The dimensions reach the part and **do not reach the drawing**. It isn't a
regression: that operation never worked, and only now is there something
to import.

Measured: 16 dimensions in the part, all marked; three views resolved;
**zero** imported. Tried three versions of the API, five source values,
four type masks, the active view and the selected view, the document's
auto-insert-on-creation option turned on, and sketches on both planes. Two
independent counts agree on zero. Detail and what's left to try is in
entry D3 of `TODO.md`.

### Found along the way: an option asked at the wrong level doesn't complain

`swDetailingAutoInsertDimsMarkedForDrawing` (248) is a **document**
preference. Asking for it at the **application** level:
`GetUserPreferenceToggle` answers `False`, `SetUserPreferenceToggle(248, true)`
**does not fail**, and reading it back still gives `False`. No error, no
warning, no effect.

> An option that "won't let itself be changed" is almost always an option
> asked for at the wrong level.

### And a trap in the interop

`ISketchSegment.GetType()` is **not** the one from `Object`: the interop
hides it behind its own method that returns the segment's type as an
integer (0 = line). Writing `.Name` after it doesn't compile — and that's
a good thing, because if it compiled you'd be reading a class name in
place of a line.

### New tools

| | |
|---|---|
| `Checks\DrawingDimensions` | tries every combination of insertion and **counts** after each one |
| `Checks\Option` | reads and writes a preference at both levels, application and document |
| `Checks\DimensionApis` | which APIs and which constants actually exist, word by word |

---

## 0.16.0 — 14 September 2026 · library 0.56.0

**A document is held onto by hand, not by name.** And a plan can finally
make a drawing and fill it in.

### The defect that 0.15.0 had just created

0.15.0 had given an identity to documents with no file (`titolo:<title>`).
It looked right, and it broke on the very first real plan:

```
step 1   extrude_rectangle    ok
step 2   save_document        ok
step 3   create_drawing       REFUSED
         "The active document changed while I was running: the plan was
          for «titolo:Parte131». I have not touched anything."
```

The document **had not changed**: it had been *saved*. And saving changes
both names at once — the path from empty to a file, the title from
"Parte131" to "z06". **No string survives a save.**

Now identity is the **COM object**: it's captured once at the start, and
the `IUnknown` pointers are compared (with the `Release` calls this
requires — a count that doesn't return to zero is a document SOLIDWORKS
never closes again). The name stays, but only for messages.

This applies to the add-in too, which used to compare paths: a
`Save As` halfway through a plan would have stopped it the same way.

### `Outcome.NewDocument`: the only document change that declares itself

With identity fixed, the "part → drawing → views" plan still stopped —
and this time **rightly so**: `create_drawing` really does change the
document, because a drawing *is* a new document.

The defence doesn't switch off: the operation **declares** the change, and
whoever runs it follows along only if it's declared.

```
[agent] step 3: create_drawing
[agent] drawing created from <the shop's drawing template>
[agent] from here on, work continues on «titolo:Disegno3 - Foglio1»
[agent] step 4: standard_views        -> views_total: 3
```

A document that changes **without** anyone saying so remains a reason to
stop: it's the defence that stops you extruding into someone else's part.

### Measurement 0.6 closed: a line is drawn on the sheet, and the section comes out

It was the last of section 0's ten measurements, and it unlocks GT4
(sections and detail views on the drawing).

```
line drawn at x=0.0945, from y=0.2074 to y=0.2516
line selected: True, objects selected: 1, type 10 (swSelSKETCHSEGS)
CreateSectionViewAt5 answered: one object
views on the sheet: 4 -> 5
  -> THE SECTION IS THERE. Last view: "Section View A-A"
```

**Something that was believed and wasn't true:** `ISheet.GetTemplateSketch()`
is **not** the sheet's drawing surface — it's the TITLE BLOCK, 71 segments
unchanged before and after. The line ends up in the **first view**'s
sketch, which *is* the sheet (`Type = 1`), and is read back with
`IView.GetSketch()`.

Two tricks that can't be deduced: the line has to cross the view all the
way through (one that just grazes it gives an empty section **without
saying anything**), and the sketch has to be closed with
`InsertSketch(true)` before asking for the section.

And the proof is **the view count**: `CreateSectionViewAt5` can answer
with an object without having done anything. 4 → 5 is the measurement.

### New tools

| | |
|---|---|
| `Checks\Undo` | a single Ctrl+Z, with volume before and after |
| `Checks\SketchOnSheet` | where a line drawn on the sheet ends up, and how to read it back |
| `Checks\SectionView` | the complete round trip through to the section, with the view count |

---

## 0.15.0 — 14 September 2026 · library 0.55.0

**The bridge runs plans with `PlanExecutor`, the same loop as the add-in.**
It used to have its own loop — a fresh `SwOperations` at every step and `stepsDone`
set to `null` — and that cost three things. The first two are measured below, **before and
after**; the third is that a second implementation of the same cycle means two
behaviors that diverge, and the tests run against only one of the two.

### 1. A step can name what the previous step did

A two-step plan — plate, then a hole on its top face:

| | |
|---|---|
| **before** | step 1 succeeded (volume 120.000), step 2 **rejected**: "cites step 1, but I don't have that step's geometry" |
| **after** | `[agent] face taken from step 1` — both succeeded |

This wasn't a gap in the add-in: `References` and `stepsDone` already existed and
worked. The bridge simply wasn't using them, because it was passing `null`.

### 2. A single Ctrl+Z undoes the whole plan

Same two-extrusion plan, volume 133.500. **One single** undo:

| | volume after |
|---|---|
| **before** | **120.000** — only the second step undone |
| **after** | **no solid body** — everything undone |

Measured by shelving the change and rebuilding, not inferred from the code.

### The ENTIRE plan is checked before anything starts

Operation name, paths and argument shape can be seen just by reading. Before,
they were checked one at a time: a shape error at step 3 stopped the plan
**with the first two already built**, and the document was left with half a part for
something that could have been said without opening anything.

Measured: a plan with a valid step 1 and a step 2 missing `diameter_mm` →
`solid_body: false`, `feature: []`, volume `null`. Nothing was touched.

And every shape rejection **says so**: "The plan was not run at all:
the document is left as it was." Without that sentence the answer only lists the
wrong step, and whoever reads it goes looking in the document for a plate that
isn't there.

### `DocumentWithoutFile`, and the check that watches over it

`PlanExecutor.PreliminaryChecks` rejects never-saved documents — right
for the add-in, which lives in a PDM where an unsaved file isn't checked out;
wrong for the bridge, which deliberately works on a freshly opened `Parte1`.

The check was **made configurable, not deleted**, and the danger was
written into TODO entry B8: that someone might switch it on in the add-in too, "because it was
getting in the way." For this reason:

* the name says what it **allows**, not what it switches off;
* the default is false;
* `Checks\check-documents-without-file.ps1` exits **1** if the property is
  turned on anywhere outside `Bridge\Session.cs`, or if it shows up in the add-in's
  sources. Both paths were **seen to fail** before being trusted.

When the add-in's sources aren't reachable the check **does not say
everything is fine**: it says out loud that it didn't look at that half.

### A document without a file now has a name

`PlanExecutor` compared bare paths to know whether the document was
still the same one as before. Two never-saved documents **both have an empty
path**: the check that exists so as not to write into someone else's part would
have called them the same document, in exactly the case where the bridge
always works. Now identity is the path when there is one, and `title:<title>`
when there isn't.

### `Session.Run` removed

After this change nothing called it anymore: 67 lines of dead code, gone.

### No regression

| | |
|---|---|
| gears and sections | 12.263,3 · 86.393,8 · 1.500.000 — identical |
| the four rejected outlines | still rejected, document intact |
| `GearTest`, `OutlineTest`, `check-catalog` | 0 |
| `test-installer` | 11 PASS, 0 FAIL |

---

## 0.14.0 — 14 September 2026 · library 0.54.0

**Outlines are checked before they are drawn.** This was the last piece of
C4, and it closes the entry: `_pulisci_contorni` and `_controlla_contorni` existed
only in the Python server, so a plan that reached the bridge **by the direct
route** went through no geometric check at all.

The geometry lives in `Oce.Solid.Agent.Core\ClosedShape.cs`, in a file of its own and not
inside the executor, for a practical reason: that way it can be tested without opening
SOLIDWORKS. A geometric check that can only be tested with the CAD open gets
tested rarely.

### The silent defect, measured

A 100 × 100 plate with a Ø30 circle **detached**, 200 mm away:

```
succeeded: true
bodies_total: 2
bounding_box: [265, 10, 100]        volume: 107.068,6   (100.000 + 7.068,6)
```

A void outside the boundary doesn't cut through: SOLIDWORKS turns it into a **second
body**, says "succeeded," and nobody notices by reading "outline with 5 contours."

The other three tested cases — a void that **straddles** the boundary, a bow-tie
contour, a composite contour with detached strokes — SOLIDWORKS already stops those.
But it only says **"The extrusion could not be created,"** which doesn't make clear where
the problem is. There, the gain isn't the rejection: it's the explanation, with the specifics.

### The four checks, and what they say

| | message |
|---|---|
| strokes that don't connect | "stroke 1 ends at (50, 0) and the next one starts at (52, 0)" |
| self-crossing contour | "the side starting at (100, 0) crosses another one" |
| zero area | "its points all lie on the same line" |
| void outside the boundary | "point (215, 0) is outside" |

Every message carries **the coordinates**: "self-crossing" on a two-hundred-
vertex outline doesn't help anyone fix it.

One message was **fixed because it was stating something false**: it said
"SOLIDWORKS would not reject this" about the self-crossing contour, and the measurement
says it does stop it. Now it says what actually happens, including the unhelpful
message you would receive.

### The limits, which are counts, not dimensions

| | |
|---|---|
| contours per outline | 20 |
| vertices per contour, **written by a model** | 200 |
| vertices per contour, **generated by a recipe** | 4000 |

How many times a line can be broken, not how long it can be: a
three-meter plate is legitimate, a five-thousand-vertex outline written by
a model almost never is. Recipes get the wide ceiling because their points are
calculated by a formula — a 127-tooth gear is 1016 segments.

### No regression, and the volumes say so

All recipes now go through the new checks. Volumes identical to before:

| | |
|---|---|
| gears | 12.263,3 · 43.964,1 · 8.808,6 · 54.999,8 |
| sections | 1.500.000 · 392.699,1 · 86.393,8 · 47.500 · 77.942,4 · 60.000 · 68.400 |

On z=42 the segments drop from 420 to 336 — that's the gain from 0.13.0 on the
zero-length root — **at unchanged volume**.

### `Checks\OutlineTest`, and why half the cases must pass

A check that rejects too much is worse than one that rejects too little: it blocks good
work and isn't discovered until someone runs into it. Half the test's cases
are legitimate outlines that **must** pass:

* **the concave L** and **the five-point star** — whoever confuses "concave" with
  "self-crossing" rejects half the brackets that actually exist;
* **the triangle** — with three sides they are all adjacent to each other, and whoever
  doesn't skip adjacent pairs flags every triangle;
* **the square**, which has four vertices and the fourth isn't a duplicate: always
  dropping the last point is the easy mistake to make in `Ring`;
* **the point in the notch of the L**, inside the bounding box and outside the material:
  it exposes an "is inside" check done with the box instead of the polygon.

---

## 0.13.0 — 14 September 2026 · library 0.53.0

**`spur_gear` can now be run.** It was the last of the two operations the
catalog declared without knowing how to perform. Declared-but-not-executable operations
go from **one to zero**, and `Checks\check-catalog.ps1` finally exits
**0**.

The geometry lives in `Oce.Solid.Agent.Core\SpurGear.cs`: a true involute, flanks
as splines over points calculated from the formula, a bore and a keyway.

### Two defects that gave no error, found without opening SOLIDWORKS

`Checks\GearTest` compares the points that come out of the generator against
the ISO formulas. It exits 0/1, doesn't open the CAD, and found this:

**(1) From 42 teeth upward the flank started below the tooth root.** The base
circle sits below the root when `cos α < 1 − 2,5/z`, that is from z=42 at the
standard pressure angle of 20 degrees. At z=127 m=1 the flank dropped **2,58 mm below the
root diameter**, and the contour cut across itself. The flank now starts from
`max(rb, rf)`.

No test caught it: the boundary cases were z=16, 17, 20, 24, and z=40 —
which had in fact been tested — sits on the right side of the jump **by 33 microns**.

**(2) The root was anchored at the wrong angle**, and the first fix wasn't enough.
ψ is the tooth's half-angle **on the base circle**; by the time the flank starts higher
up, the tooth has already narrowed at the root by `deviation(tMin)`. Using ψ for
both, at z=127 the root started at 0,027272 rad and ended at 0,022208:
**it traveled the arc backwards**. SOLIDWORKS only said "The extrusion could not be
created."

As for the contour, the radial line at the root now exists only when there is
really somewhere to descend to — at z=42 it was **0,9 microns** long — and the
segments per tooth drop from 10 to 8 where the base sits below the root:
**1016 instead of 1270** at z=127.

### The check that caught the second one, and it is general

Unrolling the angle along the contour, **a closed contour never goes
backwards**. A negative step is a self-intersection, and it can be seen without opening
anything.

The threshold is **calculated, not chosen**: the points come out rounded to 1e-4 mm, which
on the smallest radius amounts to `1e-4/rf` radians of noise; the threshold is three
times that. At z=20 it comes to 1,7e-5 against 6e-6 measured, at z=127 it comes to 4,8e-6
against 1,7e-3 for the defect. Between noise and defect there are two orders of magnitude.

### New guard: teeth touching at the root

The gap at the root measures `(2π/z − 2·psiRoot)·rf`. It vanishes past
`2,5·tan α = π/2`, that is for **α > 32,15 degrees**, and the teeth would weld together.
The message gives both the criterion and the number:

> With z=60, m=1 and a pressure angle of 35 degrees, between one tooth and the next
> there would be -0.0955 mm of root left: the teeth would touch at the root.

### `GetBodyBox` does not measure a gear's outside diameter

With z=20 the bounding box is `[44, 10, 44]`, exact. With z=17 it is `[37,79, 10, 37,94]`
instead of `[38, 10, 38]`.

It isn't approximation: the sagitta of the tip chord comes to **0,0012 mm**. The
box coincides with the outside diameter **only when a tooth lands on the axis** —
with 20 teeth the pitch is 18 degrees and it lands there, with 17 it's 21,18 degrees and
it doesn't. The criterion written in the TODO works because it fixes z=20.

### The boundary cases

| case | result | numbers |
|---|---|---|
| z=20 m=2 | ok | outside Ø 44, volume 12.263,3 |
| z=17 m=2 | ok | undercut limit, `floor(2/sin²20°) = 17` |
| z=16 m=2 | rejected | "below 17 teeth needs profile correction" |
| z=24 m=3 bore Ø20 | ok | bounding box `[78, 12, 78]` |
| z=24 m=3 bore Ø20 + keyway | ok | volume 43.964,1 |
| z=20 m=2 bore Ø34 | rejected | "tooth root at 35 mm: no material left" |
| z=20,5 | rejected | "half a tooth can't be cut" |
| z=42 m=2 | ok | outside Ø 88 — first case past the jump |
| z=127 m=1 | ok | outside Ø 129, 1016 segments |
| z=5 m=4 α=45° | rejected | "it would close to a point" |
| z=60 m=1 α=35° | rejected | "they would touch at the root" |

The undercut limit is rounded **down**: `2/sin²20° = 17,097`, and a
17-tooth gear is made every day. Rejecting it would be the worst way to
get this wrong.

The case z=5 m=4 at 20 degrees **wasn't testing what it seemed to be**: it was being
rejected by the undercut check, not by the point closing up. Moved to α=45°, where the
undercut limit drops to 4 teeth. In the test the expected reason is now written next to
the case.

### The keyway, verified by volume difference

43.964,1 mm³ with the keyway against 44.177,2 without, over 12 mm: **17,76 mm²**
more cross-section. The calculation for Ø20 (b=6, t2=2,8) gives 17,72 mm² — a 0,2% gap,
in the right direction because the bore is a 180-sided polygon **circumscribed** around it.

This verifies that the geometry matches the `Keyways` table, **not that
the table itself is correct**: it still remains to check it against the actual UNI 6604.

### Two new tools, for the bench

| tool | what it's for |
|---|---|
| `Checks\CloseLeftovers` | closes the test documents. Only those **never saved** or inside the folders passed as an argument, and it never saves |
| `Checks\NewPart` | opens an empty part so the bench can start again |

They're needed because every test plan leaves a part open, and **every state
read walks through all of them** at 244 ms of `GetNext()` each: 47 documents closed in
one go. Even worse with a heavy part: with the 1018-face gear
left open, two trivial gears after it went over ten minutes.

`NewPart` is compiled rather than a script because `ISldWorks.NewDocument` through
late binding from PowerShell 5.1 returns `TYPE_E_ELEMENTNOTFOUND`.

---

## 0.12.0 — 14 September 2026 · library 0.52.0

**`extrude_section` can finally be run.** It was declared in the catalog and
didn't exist in the switch: it only survived because the server expanded it into
`contours` before sending it, and the `default` branch fell back to profile
extrusion, finding them there. Called from the bridge with its real arguments —
`variant`, `wall_mm` — it answered "unknown operation."

Declared-but-not-executable operations go from **two to one**: `spur_gear`
remains.

### Seven sections, and every number checks out against the calculation

| section | measured volume | check |
|---|---|---|
| tubular 80×80×5 × 1000 | 1.500.000 | (80² − 70²) · 1000 |
| round Ø50 × 200 | 392.699,1 | π · 25² · 200 |
| tube Ø60/Ø50 × 100 | 86.393,8 | π/4 · (60² − 50²) · 100 |
| angle 50×50×5 × 100 | 47.500 | (50·5 + 45·5) · 100 |
| hex across-flats 30 × 100 | 77.942,4 | (√3/2) · 30² · 100 |
| U 50×40×5 × 100 | 60.000 | 2000 − 40·35 |
| T 60×60×6 × 100 | 68.400 | 360 + 324 |

The hex is dimensioned **across the flats**, the way it's bought and the way it's
measured: the bounding box comes out 30 × **34,64**, that is 30/cos 30°. Confusing it
with the circumscribed diameter would give a hex 15% larger.

The 60×20 slot comes out at 11.163,3 against the nominal 11.141,6, **+0,19%**: the
semicircles sit on the **circumscribed** circle and not the nominal one. This is
intentional — a slot with inscribed semicircles is narrower than nominal, and a
screw that has to go through it wouldn't. Where it approximates, it approximates on the
high side.

### The checks are physical, and each one has a defect behind it

| asked for | response |
|---|---|
| tubular 80×80 with wall **40** | "no cavity is left… at exactly half the void has zero area, SOLIDWORKS doesn't flag it and extrudes a solid bar — which weighs four times as much and shows green just like the right one" |
| angle 50×50 with thickness **50** | "it's no longer a profile, what comes out is a plate" |
| slot **20×20** | "the length must be greater than the width, otherwise it's a circle" |
| tubular without `wall_mm` | "For a "tubular" section, "wall_mm" is required." |

None of the four touches the document: zero faces after the rejection.

### The TODO entry's criterion was wrong, and the reason for that made it into the catalog

C4 asked for: tubular 80×80×5, 1000 long → **1.443.000 mm³** within 0,5%.
Measured: **1.500.000**, which is 4% more and would be a failure.

The entry's number is that of the **commercial profile**, which has rounded
corners. This recipe — like the Python one it comes from — produces **sharp
edges**: 1.500.000 is the right value for what it draws.

But the difference is real and it matters: anyone using `extrude_section` to
weigh a part takes home 4% more than they think. **The catalog didn't say so**, and now
it does — and this is the first change made through the new route from 0.10.0: you touch
`Operations.cs`, re-export, and the diff of `catalog.json` is **a single line**.

---

## 0.11.0 — 14 September 2026 · library 0.51.0

**The frame is requested the way the catalog documents it, and "center_distance" exists.**

### The divergence

The catalog declares `length_mm`, `width_mm`, `plane`, `measured_as`,
`centered` **at the top level** — they're in its `required`, and the description
promises that the frame "is requested with two dimensions instead of eight
coordinates." The C# side, instead, looked for a nested `frame` object, which is
the shape the server builds inside `valida()` before sending.

So a model that read the catalog and wrote exactly what was written there
**wasn't being served** — and it wasn't visible from the server, because the server
rewrites things in between. It's the same shape of defect as 0.7.1, on a bigger
operation.

Now both forms are accepted. If both are present the nested one wins:
whoever sends it built it on purpose.

### `measured_as: "center_distance"` was documented and not implemented

The catalog has listed it among the allowed values from the start. The C# side only
switched on the frame block for `overall`, and for everything else it fell back to the
segments fallback — which is exactly what this operation exists to avoid making you
write. Anyone asking for a frame by `center_distance` got back "I could not work out the
path for it."

The two modes, measured on the same 1000×800 frame in 80×80×5 tubular, and
the numbers explain each other:

| `measured_as` | bounding box on the part | |
|---|---|---|
| `overall` (default) | **1000 × 800** | the requested dimensions, measured on the finished part |
| `center_distance` | **1080 × 880** | the path is 1000×800, plus 40 mm per side for the profile |

### The September 9 number, double-checked

The criterion of TODO entry C4 was: 1000×800 frame in 80×80×5, outside bounding box
**1000×800 within 0,1 mm** — *"it's the defect measured on September 9, so it's
exactly the number to double-check"*. Measured: `[1000, 800, 80]`, four
bars, with the protrusion found at 80×80 and the path set back to 920×720.

### Found by reading, and it's worth saying

The entry asked to port the server's 145 lines of `_segmenti_telaio` into C#.
**Most of it was already there, and it's better than the Python**: the C# side doesn't
calculate four corners on the nominal, it MEASURES how much the profile protrudes and
sets the path back. Three small things were missing — segments no longer mandatory,
the catalog's shape, and `center_distance` — not a hundred and forty-five lines.

### A change in behavior, and it needs to be said

A plan that passed already-computed `segments` **together** with the two
top-level dimensions used to give 1080×880; now it gives 1000×800. The catalog's
dimensions **are** the outside bounding box, so the new number is the right one — but
it's a different number from yesterday, and anyone who had calibrated something against
the old one will notice it here. To get the old behavior, write
`measured_as: "center_distance"`.

---

## 0.10.0 — 14 September 2026 · library 0.50.0

**The catalog now lives in C#, next to the code that runs it. `catalog.json` has
become a build product.**

```bash
.\bin\Oce.Solid.Agent.Catalog.exe Tools\catalog.json
```

Nothing else. Before, it took three steps, two machines and docker: packaging the
server sources, sending them over by `scp`, running them in a disposable
container, bringing the file back. **Anyone cloning this repository could not
regenerate the catalog**, and a file that can't be regenerated is a file
nobody dares touch — while that file is the interface toward the model.

The more important reason is another one: **the layer that DESCRIBES an operation
is now the same one that RUNS it.** It is the only arrangement in which an
operation that is described but cannot be run cannot get into the catalog
without someone noticing. `Tested` is a C# `bool` next to the `case` that runs
it, not a promise written in another repository.

### The criterion was an empty diff, and it is an empty diff

```
[catalog] identical to Tools/catalog.json (72204 characters)
72.452 bytes, byte for byte identical
```

### Generated, not hand-transliterated

TODO entry C3 of the plan asked to transliterate 2.051 lines of Python, and
its risk note said: *"two thousand lines of transliteration done in the evening
are two thousand chances to change a word in a description without
noticing — and those descriptions are the real interface toward the model"*.

The C# was **generated from the `catalog.json` that already existed**, which is
already that Python's product and is the file the bridge actually reads. The chances of
changing a word drop to zero, and the criterion is still verified. The
generator is used once; from now on the C# is edited by hand, and it's 2.026
lines as readable as the original.

### The file's shape is part of the contract

One-space indentation, accented letters written out in full, line breaks as bare
line feeds, no trailing newline, keys in the order they're written. `Node` is a
dedicated type, and **not a `Dictionary<string, object>`**: a dictionary doesn't promise
key ordering, and if the keys shuffle on every build every commit
shows a whole file changed and nobody can see the line that actually changed anymore.

### `build.ps1` regenerates it, and says whether it changed

A catalog that's regenerated by hand falls behind. Now it's rewritten on every
package build, and the build prints `invariato` or `RIGENERATO: N -> M byte` along
with a prompt to look at the diff.

Outside the file there remain — **and they are reported, one per line on stderr** — the
untested operations and the families without even one operation. A list that shrinks
silently goes unnoticed.

### What was NOT done, and why

`ai_disegno.py` is still the catalog's source **for the server**: until it
reads this JSON, there are two sources.

Checked with a grep, as the entry asked: `CATALOGO_DISEGNO` has **three**
consumers besides the one that defines it, and they use `.name`, `.tested`,
`.precondition`, all fields the C# record has. *(The paths and line numbers
used to be spelled out here, and belong to another, private repository: removed on
24/9/2026 during the cleanup.)* But `ai_disegno.py` also holds
`limits` and `summary`, which aren't among the six exported and are needed by
`valida()`: the inversion is a **split** of the record, not a replacement. It's
work on the production server, which TODO entry C3 had already declared outside its
own estimate.

**A criterion not followed to the letter, and said so.** The entry asked that
`update.md` no longer contain the words "server," "docker," "python." They're
still there, but only to say **that they're no longer needed** and to record what
remains. Removing them would have made the document worse, not cleaner.

---

## 0.9.0 — 14 September 2026 · library 0.49.0

Two things, and the second is a live risk on a shared file.

### 1. The bridge now enforces the schemas it already declares

It offered thirty-six operations with detailed schemas and then never checked whether the
arguments actually matched them: the schema was only there to suggest. A forgotten
`thickness_mm` arrived downstream as zero and turned into a zero-thickness
extrusion.

```
extrude_rectangle without thickness_mm
  -> "For "extrude_rectangle" "thickness_mm" is required, and it is missing.
      (HOW MUCH IS EXTRUDED, in mm. For a plate this is the thickness; for a bar
       or a profile it is the LENGTH…)"

extrude_rectangle with length_mm: "two hundred"
  -> "For "extrude_rectangle", "length_mm" wants a number and instead it is
      text ("two hundred")."
```

**And it doesn't touch the document:** after the two rejections the part has 0 features,
0 sketches, 0 faces — not even an orphan sketch.

**Only the shape, never the merit**, and that's the line to keep in mind reading
`Bridge/Shape.cs`: the required parameter is there, it's a number where a number is
needed, it's one of the allowed values. Whether that number *makes sense* is not judged —
widening a hole has its center **exactly in the void**, and an overzealous check
would reject it.

**An integer is a number.** The easiest defect to write here would be a
`type: number` that rejects the `20` in `"thickness_mm": 20` because the
deserializer turned it into an `int`. Tested: it passes, and the block comes out at
400.000 mm³. Also tested the whole range — block, hole, pattern, mirror, the tube with
the `circle` shape, and the welded frame with its nested structures: **no
false rejection**.

### 2. The profile library: the case right next to the one that was fixed

`CloseLibraryAgain` (add-in 0.45.0) does not touch the file if it was already open
before the insertion, and the reason given is a good one: if the designer was
working on it, closing it out from under them would cause harm.

**But "leaving it as it was" preserved the OPEN state, not the CLEAN one.**

Measured: `QUADRO_COPIA.SLDLFP` was open **and clean**; after a
`rectangular_frame` it came out `GetSaveFlag() = true`. The code left it there
on purpose. It is exactly the risk all of this exists to remove — a
shared file one `Ctrl+S` away from being overwritten — in the case
right next to the one it was written for.

The flag can't be cleared: `IModelDoc2.SetSaveFlag()` takes no arguments,
it only ever dirties. So three states instead of two, each with its own reason:

| how it was before | what is done | why |
|---|---|---|
| **closed** | it is closed again | we're the ones who opened it |
| **open and clean** | **it is closed anyway** | nothing is lost — it was clean — and a shared file is not left armed |
| **open and dirty** | it is left untouched, with a warning | someone is working on it, and it was already at risk before us |

All three tested. In all three, after the operation: **no file on disk
left open and modified**, and the copy on disk intact — 99.918 bytes, the same
timestamp down to the nanosecond.

In the third case SOLIDWORKS reopens the library as a reference for the weldment, but
**clean**, which is the right state.

---

## 0.8.0 — 14 September 2026 · library 0.48.0

**`set_dimension` now also says when a feature changes nature, not just
size.**

### The defect, measured

A 200×100×20 plate with a **blind** Ø60 pocket, 5 deep. The thickness is brought
to 3 — an ordinary dimension change, the kind made ten times a
day. The pocket becomes **through**.

Before this version, all you could see was:

```
succeeded: true
volume_mm3: 51.517,7    volume_changed_mm3: -334.345,13
```

The volume checks out — `60.000 − π·900·3 = 51.517,7`, exact. The bodies are always
one. The rebuild succeeds. And **SOLIDWORKS has nothing to say**:
`GetErrorCode2` returns 0 and `IsWarning` is false on every feature.

A blind seat and a through-hole are two different parts. Whatever rests
inside it, in one case falls through to the other side.

### Now

```
features_with_fewer_faces: ["«Taglio-Estrusione1» 2 -> 1"]
note: "The new dimension changed the tree, not just the measurements:
       these lost faces: «Taglio-Estrusione1» 2 -> 1. A blind feature that
       loses a face has usually become THROUGH: the bottom is no longer there."
```

### It is reported, not rejected

Thinning a plate until a pocket becomes through can be
exactly what's wanted. Rejecting it would mean rejecting something that is merely
unusual — the worse of the two defects. But whoever asked for the change must
be able to **read it here**, instead of discovering it on the shop floor.

And the check stays quiet when it should: bringing the same part to 12 mm — the pocket
stays blind — `note` is `null`. The two tests go together, and without the second
the first wouldn't prove anything.

### The calculation has a ceiling, and it isn't generic caution

Reading the faces of every feature in the tree costs about **200 ms per
feature** (measured). On a part with hundreds of features this read alone
would take longer than the operation: past 200 it stops and reports what
it has seen so far.

---

## 0.7.1 — 14 September 2026 · library 0.47.1

**The tube, written the way the catalog writes it, now actually draws.**

In the description of `extrude_profile` the catalog carries this example in
full:

```
Example, TUBE outside diameter 60 wall 5:
[{"circle": [0, 0], "diameter_mm": 60}, {"circle": [0, 0], "diameter_mm": 50}]
```

The parser understood `{"variant":"circle","center":[x,y],"diameter_mm":d}` and
**not** this one. A model that read the catalog and copied the example written in
there fell into the polygon branch, looked for `vertices`, didn't find it, and
failed.

**A divergence between what is declared and what can actually be done, inside the file
that exists so there would be none.** And on the worst form of it: it's the one the
catalog **recommends** for holes and round stock, with the warning that a circle
approximated as segments has its material inside the circumference, and a shaft
won't fit through it.

Measured, the catalog's tube:

```
volume 86.393,8 mm³   =   π/4 · (60² − 50²) · 100
```

### Missing and non-numeric are two different things

Both used to end up as "the diameter is not a number," because the missing
key raised inside the same `try`. Whoever wrote `{"circle":[0,0]}` and
nothing else should be sent to add the diameter, not to go checking a number they never
wrote. Both tested:

| written | response |
|---|---|
| `{"circle":[0,0]}` | "A circle without "diameter_mm": a center alone doesn't say how big it is." |
| `{"circle":[0,0],"diameter_mm":0}` | "The circle's diameter is 0: a circle with zero or negative diameter can't be drawn." |

### Something the plan said that wasn't true

`TODO.md` also assumed the `{"vertices":[[x,y],...]}` form was unsupported.
**It is supported:** it's the parser's default branch. There was only one real
divergence.

And the expected volume the plan reported — "863.938 mm³" — is the **section's
area in mm²** with the wrong label: to get the volume it must be multiplied by the
100 mm of wall thickness.

---

## 0.7.0 — 14 September 2026

**Now it's actually true: a single file, and whoever installs it needs nothing but
SOLIDWORKS.**

0.5.0 had to carry an honest correction: "installable by anyone,
usable by whoever already has the add-in." The payload couldn't contain the operations
because they lived in `OpenPDM.SwAddin.dll`, private code that a public
package cannot bundle without publishing it.

Now that they live in `Oce.Solid.Agent.Core.dll`, inside this repository, that
reason no longer applies. **`dist\Installer.exe` — 324 kB — carries them inside.**

### Tested by actually installing, not just by building

Silent install into an empty folder, with OpenPDM nowhere on the machine:

```
[ok] Bridge to install: included in this installer
[ok] Drawing operations: Oce.Solid.Agent.Core.dll is inside the installer
  wrote SolidWorksMcp.exe (32 kB)
  wrote catalog.json (70 kB)
  wrote Oce.Solid.Agent.Core.dll (172 kB)
  ...
[test] attached: True
[test] volume_mm3 = 396858,4
[test] the bridge is up and running.
```

And then a real plan **run by the installed bridge**, not the development
one: block 200×100×20, Ø10 through hole, a pattern of 4 at 30 pitch.
`volume_changed -4.712,39`, `faces 7 → 10`, `copies_done 3`. The same numbers
as the bridge built in-house.

### Three things changed in the installer, and the reason for each

1. **The library is no longer searched for on the machine, it is extracted from
   the payload.** Searching for it made sense when the package couldn't contain it.
   Continuing to do so would mean installing, on a machine that happens to have an old
   copy lying around somewhere, **that** copy — with the operations from two weeks ago
   and nobody telling you so.
2. **If it is missing, it is now blocking.** Before it was a warning, and rightly so:
   not having the add-in was normal, the bridge would start and say it wasn't
   attached. Now the operations are part of the product: if they're missing the
   package is broken, and a broken package that gets installed is discovered by
   whoever uses it.
3. **The "where is the add-in" field has disappeared from the window.** Asking
   whoever installs it for a file the package already contains isn't a kindness: it's a
   question with no right answer, and whoever doesn't know what to answer thinks they've
   gotten something wrong. In its place, a read-only line that says where
   the operations come from.

### Reflection removed (B7)

The bridge calls `Oce.Solid.Agent.Core` directly: `new SwOperations(...)`,
`DocumentState.Read(...)`, `outcome.Measurements`. Type names are no longer
strings, and a typo is now caught by the compiler instead of the client.

Late binding toward SOLIDWORKS remains — `ActiveDocument`, `Identity`,
`Invoke` — and it isn't an oversight: it's the measured route (`ActiveDoc` and not
`IActiveDoc2`, see the comment in the code), and redoing it here would mean
paying again for a measurement already paid for.

**Trap found along the way:** `SolidWorks.Interop.sldworks` has a type
`Environment`, which makes `Environment.GetEnvironmentVariable` ambiguous. The three
calls are qualified as `System.Environment`, with the line saying why.

### The setting that no longer decides anything, and says so

`RISCONTRO_DISEGNO` and `OPENPDM_ADDIN` no longer choose which DLL to load —
.NET loads it on its own, next to the executable. They are still read in order to
**warn**: if they point elsewhere, the bridge says so instead of letting you believe they
still matter. An environment variable that does nothing is worse than one that doesn't
exist.

For the same reason a non-existent path is no longer a rejection but a
warning: stopping over a wrong path while the right library is already in
hand would mean rejecting something that works.

---

## 0.6.0 — 14 September 2026

**The bridge no longer loads the PDM just to draw a hole.**

The 36 operations have moved out of `OpenPDM.SwAddin.dll` and now live in
`Oce.Solid.Agent.Core.dll`, twelve files and 10.714 lines, inside this repository.
The bridge loads that one.

### What changes for whoever uses the bridge

The file to place next to the executable is **`Oce.Solid.Agent.Core.dll`**, no
longer `OpenPDM.SwAddin.dll`. The environment variable is `RISCONTRO_DISEGNO`;
`OPENPDM_ADDIN` is still accepted, because whoever has it set shouldn't have to
find out from a rejection.

And if it still points at the old add-in, the bridge says so by name instead of
complaining about a missing type:

> `"…\OpenPDM.SwAddin.dll" is the old OpenPDM add-in. Since 0.47.0 the
> operations live in "Oce.Solid.Agent.Core.dll": point RISCONTRO_DISEGNO there,
> or remove OPENPDM_ADDIN and place it next to the executable.`

Both paths tested: the right one attaches, the wrong one explains itself.
**A message that was never seen to come out isn't a message.**

### The invented boundary, and the defect it invited

`PlanExecutor` was the only one of the twelve files to talk to OpenPDM's
server. Now it talks to `IPlanSource`, and the add-in supplies
the implementation.

The obvious shape would be to return `null` to mean "plan finished." **That
wasn't done**, and the reason is written in the code: an adapter that swallows
a network exception and returns `null` would report "plan completed" for a
plan that broke off halfway through. There's a `NextStep` type with an explicit
`Done`, and faced with a `null` the executor stops, saying it doesn't know which of the
two cases it is.

### Dassault's libraries still stay out

`Oce.Solid.Agent.Core` references the interop assemblies with `Private=False` and the
HintPath pointing at the installation: no Dassault binary enters this repository, which
was the condition for it to be publishable.

### Loading now reads the bytes

`Assembly.LoadFrom` on a network drive fails with `0x80131515`, an error
that doesn't name the network and sends you looking for a missing dependency that
doesn't exist. The bytes are read and it is loaded from those.

### What did NOT change, and that's the point

The reference test redone on the real part, after the extraction:

```
extrude_rectangle   200x100x20                      -> succeeded
cylindrical_cut     Ø10 through_all                  -> succeeded
linear_pattern      4 instances, pitch 30, flip      -> succeeded
                    volume_changed -4.712,39 mm³
                    faces 7 -> 10   copies_done 3
```

The same numbers as before the extraction, to the digit.

### Still open

**Reflection is still there, and now it no longer has a reason to be.** The
bridge could reference the library like any other project: that's entry
B7 of `TODO.md`. As long as it's there, type names are strings and a typo
is only discovered at runtime.

The library's path in the add-in's csproj is **absolute**, overridable
with `/p:RiscontroDisegnoProj=...`, until the submodule arrives (B6).

---

## 0.5.0 — 14 September 2026

**The bridge no longer silently works on the wrong face.**

### The defect, measured

A 200×100×20 block already open. The model calls `inspect_part` — `F1` is the
top face, 20.000 mm² — and sends a two-step plan:

1. a Ø60 pocket, 5 deep, **on F1**;
2. a Ø10 pocket, 5 deep, **on F1**.

The first step brings the top face to 17.172,57 mm². The identifiers
are **positional** — the add-in reassigns them by sorting faces by area — and
the top one thus drops behind the bottom one, which stayed at 20.000. At the
second step `F1` is the **bottom** face.

Measured: the small pocket ended up on the bottom of the part.

| face | area | says |
|---|---|---|
| F1 (bottom) | 19.921,46 = 20.000 − π·25 | the Ø10 is **here** |
| F2 (top) | 17.172,57 | untouched by the Ø10 |
| F9, wall | π·10·5, point y = 0 | rises **from the bottom** |
| F10, floor | point y = **5** | 5 mm **from the bottom** |

Both steps answered `succeeded: true`, the depths were the ones
requested, and the volume checked out: **385.470,1 mm³, exact**.

And it's exactly the same volume as the right part. The two features remove
the same material: **no volume check could ever have told them apart.**

### The defense already existed, and nobody was feeding it

The add-in knows how to recognize that "F1" is no longer the same face as before: it
reads the `_signatures` key from the step's arguments and compares point and normal. But that
key was written **only** by OpenPDM's Python server (`ai_disegno.py`). In the bridge it
appeared in no source file at all, so `SignatureMatches` had nothing to
compare and always returned `true`.

Now `inspect_part` remembers the signatures of what it described — point,
normal, area, length, for faces, edges, features, axes and **bodies** — and
`build` attaches them to every step. The same four fields as the server, so the two
routes tell the add-in the same thing.

On the same plan, now:

> `"F1" now points to a different part of the part (moved by 20 mm). The part has
> changed since you confirmed it: I will not work on the wrong face.`

and the bottom face stays at 20.000 mm²: the wrong cut **does not happen**.

### `state_after` is the next reading, and must be signed as such

`build` returns the document's state after the steps, with the new
identifiers: whoever reads it writes the next plan against **those**. If the
signatures stayed the ones from `inspect_part`, the next plan — written
correctly — would be rejected because it was compared against a reading its
author never saw. So the signatures are refreshed from `state_after`.

Measured: two `build` calls in a row in the same session, the second one citing `F2`
taken from `state_after`, passes and cuts the right face — F2 drops from 17.172,57
to 17.094,03, the bottom face stays at 20.000.

### Signatures are only valid for the document that produced them

If the plan names a different document with `document`, the identifiers don't
refer to this one: attaching the old signatures would produce rejections that
have nothing to do with anything. Better no comparison than a comparison between two
different parts.

### A defect of my own, caught by measurement

The first draft read the state's families with `as object[]`. The state
arrives from the add-in with its real types, and there they are
`List<Dictionary<string, object>>`: the cast silently returned `null` and the
check stayed **off**. The test noticed it, not a reading of the
code — the broken plan was still passing. Now it's iterated as `IEnumerable`, which
works for both shapes.

### Tested on the bench, four measurements

| plan | expected | got |
|---|---|---|
| `stale_uid_2` — `F1` cited after `F1` has changed | **rejection** | rejected, "moved by 20 mm," part intact |
| `good_uid` — `F3` that has only been repositioned to `F4` | **passes** | passes, Ø6 on the front face (4.000 → 3.971,73) |
| `seq_a` + `seq_b` — two `build` calls, the second on `state_after` | **passes** | passes, Ø10 on the top face |
| `cp_1`+`cp_2`+`cp_3b` — a plan that creates the part from scratch | **passes** | passes, no signature to compare against |

### A correction to what 0.4.0 said about the installer

The entry below says "you download it, double-click, and Claude talks to
SOLIDWORKS," and `build.ps1` used to print "give it to anyone who has SOLIDWORKS."
**It isn't true yet, and the line has been changed.**

The payload embeds the bridge, the catalog, the configuration and the legal
notes, but **not** `OpenPDM.SwAddin.dll`, which the bridge loads by reflection from next
to the executable or from `OPENPDM_ADDIN`. Whoever doesn't already have that
add-in installs a bridge that stops right away — with a clear message, but still a
rejection.

Until the operations are extracted into a library of their own (section B of
`TODO.md`), the exact formula is: **installable by anyone, usable by whoever
already has the add-in.** The 0.4.0 entry stays exactly as it was written, because a log
is not rewritten; this is the correction, dated.

---

## 0.4.0 — 14 September 2026

**A single file: you download it, double-click, and Claude talks to SOLIDWORKS.**

`dist\Installer.exe`, 151 kB. It carries the bridge, the catalog, the
configuration and the legal notes inside as embedded resources, writes them to disk, and
— this is the real work — **writes the entry into the MCP client's
configuration by itself**.

### Why an installer, and not three lines in a README

Whoever is in a hurry skips the three lines of README, and then the bridge doesn't
start for a reason neither of them guesses. The point where people get lost isn't
copying two files: it's opening a JSON file hidden in `%APPDATA%`, figuring out where the
block goes, not breaking what was already there, and knowing that Claude has to be
restarted for it to read it.

Three rules in writing that configuration, all three paid for by
someone else before us:

- **the file is never overwritten.** Whoever uses Claude often has other MCP servers
  configured: it is read, our own entry is added, and the rest is rewritten back
  as it was;
- **a copy is made first**, always, with the date in the name;
- **it tells you to restart.** Claude reads that file at startup, and "nothing
  happens" is the fastest way to make someone conclude the install
  failed.

The JSON is rewritten **indented**: leaving the user a two-thousand-
character line where something readable used to be is a form of damage even if the
content is correct.

### What it does, in order

It checks before touching anything: .NET Framework 4.8, SOLIDWORKS installed (and the
`api\redist` folder, needed because Dassault's libraries are **not**
included), the payload to install, the add-in, and whether SOLIDWORKS is open right now.
Every check that fails says **what to do**, not just that it failed.

Then it installs into `%LOCALAPPDATA%`, wires up the chosen clients, and closes with the
**dry run**: `--prova` attaches, says what it sees, and exits. It's the only thing
that lets you write "installed" knowing it's true.

No elevation: it doesn't write to Program Files, doesn't touch the registry, doesn't
register COM components. In many shops the designer doesn't have
administrator rights, and an installer that asks for them can't be used.

There's also `--silenzioso` for installing across multiple workstations, and
`Uninstall`, which removes **only** the files placed here — never a recursive delete
on a folder chosen by whoever is installing.

### Measured

| test | result |
|---|---|
| clean install | 4 files written, dry run: "the bridge is up and running" |
| reinstall over a previous one | rewritten, no error |
| **the exe ALONE in an empty folder** | **extracts everything and installs itself: the payload is really inside** |
| `--aiuto` from a prompt | attaches to the console of whoever invoked it |

The third row is the test that matters: if the payload weren't embedded,
the installer would pick it up from the adjacent folder and the test would pass
just the same without the file being self-contained. `build.ps1` also has a
check that refuses to produce a package smaller than the bridge it's
supposed to contain.

### One small thing, and the reason

The console prints ASCII only. It attaches to the console of whoever launched the
command, with that console's own code page: an em dash comes out as a diamond with a
question mark, and an installer that prints broken characters looks broken
itself.

---

## 0.3.0 — 14 September 2026

**Three defenses, and one was a defect I had added myself.**

### The bridge declares what it does

The protocol provides four hints the client uses to decide whether to
stop and ask the person working for confirmation: `readOnlyHint`,
`destructiveHint`, `idempotentHint`, `openWorldHint`. There was **none** of them,
so every client treated `inspect_part` — which touches nothing — and
`build` — which changes the part in a way nobody undoes on its behalf —
exactly the same way.

For a project whose whole thesis is "no geometry exists without someone
having confirmed it," this was the most glaring omission.

`build` is declared **not idempotent**, and that's the line that matters:
running the same plan again doesn't put the part back the way it was, it makes a
**second hole**. Declaring it idempotent would invite a client to retry on its own after a
network error, and the part would come out with double the features without
anyone having asked for it.

**Measured:** `tools/list` reports all four annotations on all four
tools; `build` destructive, not idempotent, not read-only.

### The document is NAMED, the check is not switched off

0.2.x had `qualunque_documento: true` to override the rejection when the
active document had changed. It was a defect, and of the worst kind: **an
argument that switches off a defense is an invitation to switch it off.** Whoever hits
the rejection has every incentive to retry with it turned on, and the defense
disappears in exactly the case where it was needed.

Now `document` is passed with its title or path. It asks for the same thing
in a form that can't be given at random: **to write its name you have to have
looked at it.** Anyone can set a boolean; a name that doesn't match gets
rejected like everything else.

### Paths are checked before anything runs

`save_document` and `export_pdf` take a path **from the model** and write to
it. It's the only thing the bridge touches outside the open document, and it
came from generated text with no check at all.

Three rules, in `Bridge/Paths.cs`:

- a mandatory root, if `RISCONTRO_RADICE` is set;
- **system and program** folders never, under any circumstance — not because
  anyone would deliberately ask for them, but because a wrong relative path
  ends up there on its own;
- the folders declared in `RISCONTRO_SOLA_LETTURA`, which come from a fact:
  inserting a structural member makes SOLIDWORKS open the profile library
  file on its own and leave it marked as modified. From there any
  save at all rewrites everyone's shared library.

**Measured**, and the third row is the one that matters:

| request | result |
|---|---|
| a path inside the system folder | rejected, nothing run |
| a path with `..\..\` that lands there | rejected: normalized **before** the comparison |
| a normal working folder | **saved, 45.144 bytes** |

The last row is the lesson already paid for elsewhere: a check that rejects even
what is merely unusual is worse than no check at all, because it blocks
legitimate work and nobody revisits it.

---

## 0.2.0 — 11 September 2026

**The bridge stands up and draws.** Four tools, complete protocol,
tested against a real SolidWorks with a part open.

```
inspect_part   list_operations   describe_operation   build
```

### Four tools, not thirty-six

There are thirty-six drawing operations, and the temptation is to expose one
per tool. That would be wrong for two reasons already measured in the project:

1. **the cost.** The full schemas are tens of thousands of characters in
   every request. The server's two-tier catalog exists for exactly this, and
   it holds just as true here;
2. **the right order.** The costliest defect isn't a wrong operation:
   it's a right operation done **blind**. Four tools in sequence —
   look, search, dig in, do — make it hard to skip the first one.

`inspect_part` isn't an accessory: without it, the identifiers the other
tools want (`F1`, `E4`, `Corpo1`) don't exist.

### The catalog isn't rewritten by hand: it's exported

`Tools/catalog.json`, regenerated by the server with a command
(`Tools/update.md`). It contains **only the operations with `tested=True`** —
36 as of today. A bridge exists to measure, and measuring with an
untested tool isn't measuring.

### What cost more than expected

- **`Assembly.LoadFrom` from a network drive** fails with `0x80131515`,
  "Operation is not supported": a message that names neither the network nor
  trust, and sends you off looking for missing dependencies that aren't missing. It
  needs `<loadFromRemoteSources enabled="true"/>` in `App.config`.
- **`IActiveDoc2` doesn't exist for COM.** Calling through reflection you
  go through IDispatch, and there the name is `ActiveDoc`: `IActiveDoc2` is how
  C#'s typed interop exposes it. Asking for the wrong one
  doesn't give a useful error — the bridge would answer "there is no document
  open" while there were twenty.
- **Dassault's interop libraries are not copied** into the
  repository: they are looked up in the installation's `redist` folder, through an
  `AssemblyResolve`. Copying them would mean redistributing a third-party
  binary and tying it to one version.

### An error that helps instead of just stating facts

Whoever gets an operation's name wrong almost always has a nearby one in mind,
and misses it by a letter. `asole` now gets back *"Did you mean:
`asola`"*. The first attempt searched for the whole word inside the name, and on
`asole` it didn't find `asola` — it failed in exactly the case it had been
written for.

### Measured

Against SolidWorks 29.5.1 with add-in 0.40.0.0 and the watertight enclosure part
open:

| | |
|---|---|
| `initialize` / `tools/list` | 4 tools |
| `inspect_part` | full part state, with `documents_open` |
| `list_operations` (family `drawing`) | 7 operations |
| `describe_operation` (`asola`) | full schema |
| misspelled name (`asole`) | "Did you mean: `asola`" |
| plan with a non-existent operation | rejected **before** touching the part |
| `build` → `apply_material` | "generic ABS," read from the state afterward |

The ambiguity was declared rather than resolved: "ABS" matches two
materials and the bridge stopped to ask which one. That's the add-in's
behavior, and it passes through the bridge unsoftened — which is the whole point of
loading the real add-in instead of rewriting it.
