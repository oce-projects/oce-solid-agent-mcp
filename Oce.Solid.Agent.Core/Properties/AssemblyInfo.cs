using System.Reflection;
using System.Runtime.InteropServices;

// Oce.Solid.Agent.Core — the drawing operations for SOLIDWORKS, without the PDM.
//
// THE VERSION GOES UP WITH EVERY CHANGE, as for the add-in and for the bridge, and for
// the same reason: this library serves to MEASURE, and a measurement without the
// version number of what produced it is not a measurement, it is an
// anecdote.
//
// It starts from 0.52.0 and not from 0.1.0: it is the same code as the OpenPDM add-in at
// 0.52.0, moved. Restarting the numbering from scratch would make what
// already has a year of measurements behind it look new, and would make it impossible to say
// «this check runs on the same version as back then».
[assembly: AssemblyTitle("Oce.Solid.Agent.Core")]
[assembly: AssemblyDescription(
    "CAD operations for SOLIDWORKS that report what actually came out, not what was asked for.")]
[assembly: AssemblyProduct("Oce.Solid.Agent")]
[assembly: AssemblyCulture("")]

// Not visible to COM: the bridge uses it, not SOLIDWORKS.
[assembly: ComVisible(false)]

// 0.94.1: **comments only.** The rule says that every change to the library
// raises the version, and makes no exception for comments — because «it is only
// comments» is exactly what is said a moment before it stops being
// true. Here it is true (two documentation blocks in
// `ReplaceOperations.cs`, rewritten because they said something that the bench
// then disproved), and the number moves all the same.
// 0.95.0: `center_marks` (operation 64) and the crosses in the drawing state.
// 0.96.0: the exploded view in the assembly state, with the millimetre next to the boolean.
// 0.97.0: `new_sheet` and `activate_sheet` (entry GT1), with the dihedral read back.
// 0.98.0: `title_block` (entry GT3) and `interference_check` (GA10); and
//         `AlsoOnAssemblies`, because the executor refused every assembly while
//         the catalogue promised four operations on them.
// 0.98.1: the refusal of `model_dimensions` says how many dimensions are MARKED,
//         not only how many there are: without that figure the diagnosis stays split
//         in two, and on 18/9 a dedicated probe was needed to split it.
// 0.98.2: the six annotation counters per view (entry Z-E1), plus the six of the
//         SHEET separately — because `InsertNote` writes there, and counting only the
//         `note` views would be a field that cannot become true.
// 0.98.3: `title_block` tells «the property is not there» from «it is on the model and
//         the title block shows it empty» — two opposite diagnoses that used to
//         end up in the same list, and sent people to repair the wrong
//         thing.
// 0.98.4: `standard_views` no longer draws the part by lot. It put on the drawing the
//         views of the FIRST open document — an order that nobody declares — and
//         with more than one part open it made the drawing of another part without
//         giving any error. Now: exactly one, that one; two or more, it refuses.
// 0.98.5: `save_document` looks at WHY it did not save instead of reporting
//         a code: the folder that does not exist and the read-only file can
//         be seen from here, and the `SaveAs` code does not say them.
// 0.99.0: `drawing_dimension` (entry K10) — THE DIMENSIONS ON THE DRAWING CAN BE CREATED.
//         `InsertModelAnnotations*` brings none out of process, but
//         `AddDimension` on the view edges does, and with the right values.
//         Every dimension is READ BACK: it answers how much it measures, not «done».
// 0.100.0: `note` (entry E2-bis) — a text note on the sheet.
//          `InsertNote` answers, `InsertNewNote3` is `Void` and does nothing.
//          Notes are counted ON THE SHEET and not in the views: counting them in the
//          wrong place would have read 0 with the note in its place, and that false
//          negative would have closed four TODO entries by mistake.
// 0.101.0: dimensions are no longer left piled up on the view. The point where a
//          dimension ends is computed from the edge: `GetCurveParams3`
//          gives the two ends in the model, `ModelToViewTransform` brings them onto the
//          sheet, and ONLY THEN is `IView.Position` added -- without that
//          addition the dimensions all fell at x=0. Horizontal above, vertical
//          at the side (UNI ISO 129-1: dimension lines outside the figures), in
//          order of increasing length moving away from the part, offset
//          12 mm and pitch 10 mm. Duplicates removed by GEOMETRY and not by
//          value: two 10 mm steps are two elements, and both must be
//          dimensioned.
// 0.102.0: the counter of the horizontal band was called `above_all`
//          and put the dimensions BELOW (entry K15). Nothing else changes in the
//          behaviour: it is a name that said the opposite of what it
//          did, and a trap for whoever reads it in six months.
//          AND THE LINE OF 0.101.0 JUST ABOVE SAYS «horizontal above»: it is
//          wrong, and the name of the counter is the reason it was written
//          that way. It is not corrected in silence — a version note rewritten
//          backwards is a note that lies — it
//          is disproved here: **below**, as the code says and as it has always
//          done.
// 0.103.0: `MaxEdges` from 60 to 30 (entry K13). The 16 KB ceiling on the state
//          was already exceeded in the normal configuration of whoever works —
//          16,089 characters with two documents open — and the check did not
//          see it because the bench runs on a clean desk. Faces and
//          edges are 89% of the load and the entries are already lean:
//          the only lever is HOW MANY. Now ~13,300, 17% of margin.
// 0.104.0: `section_view` (entry GT4) — the first section view. The
//          cutting line is not passed in the signature: it is drawn on the sheet and
//          left selected, and `CreateSectionViewAt5` takes that.
//          THE HATCH COUNTER IS NOT THE ONE THE ENTRY
//          PRESCRIBED: `GetSolidHatchCount` answers ZERO on a real
//          section, before and after the rebuild. `GetFaceHatchCount` is read, and
//          the other is reported all the same in the answer so that nobody
//          retraces the road already measured.
// 0.105.0: `detail_view` (entry GT4, the other half). And here 'by
//          analogy' would have been wrong two times out of three: 'Showtype'
//          wants swDetCircleShowType_e, and the count of detail circles is the
//          RETURN VALUE of GetDetailCircleCount2, not
//          its 'out' -- which goes from 1 to 35 for ONE circle.
//          And the check is not the hatching: a detail cuts
//          nothing and the hatching there is zero. The proof that the circle
//          became a detail is the extra circle on the starting
//          view: 0 -> 1.
// 0.106.0: the preliminary checks RECEIVE the document instead of
//          fishing out `_sw.IActiveDoc2` again (entry Z-C5). They were two reads
//          of the same global a few lines apart, with a window
//          in between: they could approve document A -- it is a part, it is saved,
//          it is not read-only -- and the plan take B. No error
//          would have said so, because both reads were legitimate:
//          what was wrong was that there were TWO. Five reads remain
//          in PlanExecutor, and now
//          a contract check counts them on every run.
// 0.107.0: twelve operations declare the limit they already refused
//          (entry Z-G5). Each had a bench case that demands a
//          refusal message, and a description that said nothing
//          about it: the model could discover that limit only
//          by running into it, and a refusal costs a run while a declared
//          limit costs zero. The sentences are not made up:
//          they are the case's message, turned into the future. The count of the
//          mute descriptions is a ceiling in Tests/LimitsTests.cs, and it
//          went from 12 to ZERO.
// 0.108.0: `RequestedDocument` on every operation of the catalogue (entry
//          Z-D0-ter): «part», «assembly», «drawing», or more than one. The
//          family was an approximation, and `checks` showed it
//          by itself -- `title_block` wants a drawing, `interference_check`
//          an assembly, the other six a part. That fact lived in the
//          head of whoever had written the operation and in a sentence of prose
//          that no check reads. Now it is in the JSON, and a model
//          that reads it does NOT try an operation on the wrong document.
// 0.109.0: `Example` on 24 operations (entry Z-G1): a real call,
//          copied from a step that the bench EXECUTED and measured. A
//          schema says which fields are legal, not which combination
//          builds a part.
//          They are 24 and not 71 on purpose: the other cases use selectors that
//          live only in the bench (@face, @edge) or the placeholders
//          {WORK}/{PROFILES}. Copying them would have given 24 good examples and 47
//          TRAPS -- a model that copies @face(normal=up) receives a
//          refusal it does not understand, and then no longer trusts the catalogue.
// 0.110.0: the leash in the library too (entry Z-C4), the SAME
//          class the bench uses -- two expiry mechanisms that
//          behave differently are worse than a single one.
//          Two deliberate differences from the bench: the report goes on
//          **stderr**, because on stdio stdout is the JSON-RPC channel and a
//          non-JSON line breaks it; and it **does not kill the process**, because
//          the bridge is someone else's service while the bench is a
//          run that either finishes or is not needed.
// 0.111.0: `apply_material.material` is S235JR if not stated and
//          `combine_bodies.operation` is union (entry Z-G3), and the
//          answer SAYS that the bridge chose it. This note was missing:
//          the version had been raised and told only in the bridge's
//          CHANGELOG (0.84.0). Written on 24/9/2026 with 0.112.0.
// 0.112.0: the weldment profiles folder no longer has a default
//          path. `ProfilesFolder()` returns null without
//          OCE_WELDMENT_PROFILES, and `weldment_profiles` tells «I do not know where
//          it is» from «it is not there»: before, the two cases gave the same
//          message, with an empty path between quotation marks. The fallback was
//          the drive of a single workstation: it helped nobody else and
//          published how that drive is laid out.
//          The rest is comments rewritten without names of real parts.
//          RAISED ON PURPOSE EVEN IF THE CHANGE IS SMALL: the sources had
//          changed under 0.111.0, and a green bench on those bytes would have
//          described a different library with the same number on it --
//          the defect of entry K14. It was noticed by the critic of the
//          reread, not by a check.
// 0.113.0: three things measured by the probes of 24/9/2026, all three without
//          an error anywhere before today.
//          - `set_dimension` REFUSES if the new dimension makes an
//            opening leave the material (entry E18, part c): the closed
//            openings of the planar faces are counted before and after, if they drop
//            it puts the previous value back and re-reads the volume. It was case
//            r16 of the regression, a slot that overflowed in silence.
//          - The guard against the modal looks at whether the main window
//            is ENABLED (entry Z-C3): `CommandInProgress` stays false with
//            a modal open, measured with `Checks/Modal`.
//          - Two catalogue descriptions that proclaimed themselves alone
//            (`model_dimensions`, `extrude_on_face`) now tell the truth
//            (entry Z-C15): the new contract check found them.
//          And before the first green bench, on the same version never
//          committed (the run of the evening of 24/9 died of a hiccup of
//          S:, and a new run was needed anyway):
//          - the SHEET METAL family: `base_flange`, `flat_pattern_dxf` (entry GP18),
//            from the probe that measured the flat pattern to the hundredth;
//          - `dimension_scheme`, DimXpert from the core (entry Z-H3);
//          - `export_pdf` with «search»: the PDF is re-read, decoding the
//            /ToUnicode (entries GT3-bis, Z-C9);
//          - the drawing state counts the dimensions without an edge (entry Z-A2);
//          - the description of `extrude_section` says that the slot is a
//            circumscribed polygon: the first run discovered it with the new case.
// 0.114.0: the AXES are checked by identity and two welding operations
//          that had never done anything.
//          - The state of an axis carries a point on the line and the direction, and
//            `axis_uid` is compared with the signature of the plane (entry E4, the
//            axes part): an axis rebuilt elsewhere with the previous name is
//            refused, instead of repeating around the new one.
//          - `trim_members` passed the bodies as `object[]`, and SOLIDWORKS
//            answered with a feature that did not enter the tree: the T
//            stayed identical and the operation said `bodies_trimmed: 1`. With
//            `Body2[]` the cut is born, and a guard demands that the feature
//            be in the tree and that the volume change (entry Z-C6).
//          - `weld_bead` had never created a bead: it takes TWO
//            faces instead of two sets of bodies, uses the call by
//            selection (the only one that creates it), checks first that the faces
//            touch at an angle and re-reads the leg from the feature.
//          - `gusset` leaves the catalogue (`Tested = false`): it is not born in
//            any of the sixteen variants tried.
// 0.115.0: THE DIMENSIONED DRAWING, LOOKED AT (entry GT11). Asked for a drawing
//          to look at, the drawing — with the bench green — had four defects:
//          - the numbers of the vertical dimensions OVER the extension lines: the
//            model-to-sheet transform of `drawing_dimension` used the transposed
//            matrix (Y mirrored in the rotated views) and added the position
//            of the view instead of the translation (5 mm of error). Now
//            `MathPoint.MultiplyTransform`;
//          - the holes not dimensioned: hole callout («4× Ø8 THRU») and
//            positions in parallel from the reference edges;
//          - the chamfer dimensioned 7.071 obliquely with the 5/75 chain: now the
//            chamfer dimension «5 x 45°», and legs and hypotenuse are skipped;
//          - the 10 and the 120 in two views: what is already dimensioned in
//            another view is skipped, reading it from the edges of the existing dimensions.
//          And the dimensions stay inside the frame of the sheet, and the view can be
//          named with the state identifier («V2»).
// 0.116.0: THE ANCHOR (entry E18, parts a and b). `cylindrical_cut` and
//          `rectangular_cut` take `anchor`: for each axis of the face
//          a dimension from the centre to an edge, or the centre halfway between two
//          opposite edges. Without it, x_mm/y_mm stay absolute and a hole 20
//          from the edge stays where it was when the plate gets longer. The rules
//          come from the measure and from an adversarial critic: both
//          axes, edge with the signature, straight, on the face and not on the edge of
//          a chamfer; consistent with x_mm/y_mm; `AddToDB` only around the
//          creations; fully defined sketch and no sick
//          relation; a motion check after the cut, and if it does not hold the
//          cut is removed. The centring is PER AXIS: between the midpoints of the
//          flanks it made the hole jump in y already at creation.
// 0.117.0: ZERO IS NOT «I DON'T KNOW» (entry Z-C8), and two guards that were looking
//          elsewhere. Every count read from the CAD that answered 0 from a catch
//          now answers -1 or null: VolumeMm3 is double?, CountSolidBodies,
//          FeatureFaces, CountSheets, CountViews, CountDimensions, CountFeature,
//          CountPlanes and the others return -1, and the callers compare only
//          numbers that were read. Before the change an unreadable volume is a
//          refusal (nothing was touched); after, no refusal and no
//          number: the field is null and there is the note. The state writes null for a
//          volume or an area that cannot be read. The guard of the union
//          compared two different enums and never fired (entry Z-C18).
// 0.118.0: IS THE PRINTED NUMBER THE MODEL'S? (entry Z-C2) The state of the
//          drawing counts the dimensions that print a number that is not the model's: number
//          overwritten (`GetOverride`) or value turned off with text in its
//          place (`ShowDimensionValue`), measured on 25/9/2026 on the three roads
//          of the interop. It also catches the overwritten number that matches the model by
//          chance, because it looks at the override and not at the values.
//          AND THE SLOT (entry E18): the sketch is born defined — tangencies, equal
//          radii, midline between the centres dimensioned, radius dimensioned, horizontal or
//          vertical — and it is corrected with set_dimension; it takes `anchor`, and
//          oblique dimensions the angle against the anchor edge with
//          AddSpecificDimension (Extension.AddDimension returns null there).
// 0.119.0: THE ANCHORED BOSS (entry E18). `extrude_on_face` takes `anchor`:
//          the point is the centre of the contour, and it holds for one contour only,
//          round (diameter dimensioned, anchor on the centre) or aligned rectangular
//          (horizontals and verticals, two dimensions, centre built with
//          the diagonal). For any other contour a refusal that says why.
//          And `hole_wizard`, the last piece: one anchor per position (all or
//          none), put in the HoleWizard points sketch after the
//          positioning; defined is checked after the last point, the
//          override with the true radius once the hole is born.
// 0.120.0: EDGES ARE FOUND AGAIN BY SIGNATURE (entry E19). Fillet, chamfer,
//          variable fillet, cosmetic thread, direction of a pattern
//          and anchor: if the uid changed between the reading and now, the
//          edge with the same length in the same place is looked for — only one, never
//          the nearest — and it is said in `reanchors`. `LengthMm` gives the
//          TRUE length (GetLength3), not the chord: the edge of a hole was
//          «0 long» in the state. SignatureMatches also compares the length.
// 0.121.0: THE CHAMFER THAT DOES NOT FIT (case r05, the last silence of the
//          regression). set_dimension refuses, and puts the value back, if a
//          feature that is not a cut loses faces and after it there is a
//          chamfer or a fillet: it is the chamfer that takes a whole face, and
//          SOLIDWORKS does not report it. PutBackDimension extracted (two guards).
// 0.122.0: `edge_flange` (entry GP18, third of seven). The edge flange
//          is born only with a RECTANGLE of profile on the flange plane (the
//          end face): ten attempts of the probe, the first eight null, the
//          ninth an empty flange. Length at the outer virtual edge =
//          profile + T; extra volume compared with the hand calculation. And `hem`
//          (fourth of seven): on the selected edge, no sketch;
//          the open one is the 180 degree bend with radius = half the gap.
//          `BorderEdge` extracted, for flange and hem.
// 0.123.0: `sketched_bend` (entry GP18, fifth of seven): the sheet metal
//          bends along a line drawn on a face (InsertSheetMetal3dBend
//          with the sketch selected, at the first try). Volume and flat pattern
//          stay; the bounding box before and after says which side stayed still.
//          The jog no: seven variants of selection, nothing.
// 0.124.0: `hole_table` (entry GT5). The tables are born (the single
//          probe of 26/9: general, revisions, holes; the cut list not).
//          The hole table wants the origin vertex with mark 1 and the face
//          with mark 2, SELECTED in the view: with the marks swapped it is born
//          empty without an error, so rows = holes + 1 is demanded and every
//          position is re-read against the part. `drawing_dimension` leaves the holes
//          to the table when there is one (the table is on the SHEET, not on the view).
//          `ResolveView` extracted from drawing_dimension. And `surface_finish`,
//          `datum`, `geometric_tolerance` (entries GT6, GT8):
//          attached to the border edge of the requested side (selected in the
//          view), then MOVED out of the part, beyond the dimensions already placed and out of
//          the other views — all found by looking at the PNG. Ra,
//          letter, symbol, value and references re-read.
// 0.125.0: `cut_list` right (entry GT2). It put ALL the bars in a single
//          entry («4 x 1000» for a 1000 x 800 frame) and the case was green.
//          Cause: the part was not a weldment. Now it puts the feature
//          «Weldment» if it is missing, turns on the automatic cut list on the folder
//          of the bodies, reads the entries (description, length, quantity) and
//          checks that the quantities add up to the bodies. With the same care
//          `drawing_cut_list` is born: tested (and takes the view by uid,
//          V1, like the other drawing operations). And `flat_pattern_view` (entry
//          GT13): the flattened sheet metal on the drawing; the bounding box from the visible
//          edges (the contour of the view said 92.6 for 80.7) against the
//          flat pattern measured on the part before placing the view. And
//          `drawing_weld_symbol` (entry GT7): the ISO symbol on the joint between two
//          bodies, which the operation finds by itself; the code is the one from
//          gtol.sym (`<WELD-FILL>`) — with a wrong code the symbol was born
//          and read back the same, without a triangle. LOOKING AT THE PNGs, with the bench
//          green: the cut list (17 columns of the table template) came out of the
//          sheet and the flat pattern fell into the title block — now the
//          columns that are not needed in the workshop are hidden and table and view go into a
//          free place (SheetPlacementOperations). And GT12: `drawing_dimension`
//          re-reads the depth of the blind hole from the callout and looks at the
//          real rectangle of every text (IDisplayData) against the other texts and
//          the lines of the part (the pieces of one same dimension — the callout «Ø8 ↧5»
//          is made of four texts — are not compared with each other: the first
//          bench counted four overlaps). And Z-C13: a section or a
//          detail with a letter already used is refused; the effective letter
//          is re-read (IDrSection / IDetailCircle); the two operations take
//          the view by uid too (V1). W2.11: the centre crosses on the holes WERE THERE
//          (SOLIDWORKS puts them creating the view) and GetCenterMarkCount did not
//          count them — they are counted by enumerating them, in the state and in the operation, which
//          becomes a check (from code a new cross is not born, measured).
//          Z-F2: `dimension_tolerance` (±, bilateral, ISO fit checked
//          on the ISO 286-1 table) with the decimals of the tolerance set — without,
//          on the drawing it came out «10 H7 +0 / 0». GT9: `drawing_bom`
//          (InsertBomTable4 on the view, compared with the components of the assembly,
//          placed in a free place) and `balloons` (the balloons: AutoBalloon5, counted after).
// 0.126.0: Z-C9, the «objects» half: `export_pdf` re-reads the text of the PDF and
//          demands that every number printed by the dimensions be there (a dimension on a
//          layer that is not printed counts in the document and in the workshop does not
//          arrive). Seen to fire: the Ø12 callout on a layer that is not
//          printable, «12 (on drawing 1, in the PDF 0)».
//          the workshop word for balloons becomes `balloons` and says which
//          entries of the bill of materials remain without a balloon in the WHOLE drawing
//          (seen on the Starlink: the disc covered in the isometric). A view
//          where every entry is already ballooned elsewhere is no longer an error.
//          `drawing_dimension`: a circle is a hole only if it looks toward the reader AND is the
//          inner ring of a planar face (the turned bushing: the contour
//          Ø30 took «Ø30 ↧30»); the contour has the diameter dimension, the cut
//          circles are said. Through/depth also from the geometry, holes
//          grouped by diameter and depth, and the fallback without a callout
//          writes the depth in the text.
// 0.127.0: GT14, the round part seen from the side. `drawing_dimension` on the
//          longitudinal view of a shaft or a bushing: lengths between the faces
//          (two cut circles, in parallel from the first face) and diameters
//          from the two generatrices of every cylinder (the Ø is put by SOLIDWORKS),
//          re-read; the generatrices without a face tried in pairs and kept only
//          if the value is a true diameter of the part; the diameters that the view
//          does not expose, said. `export_pdf` counts the dimensions whose text
//          cannot be read (not checked) instead of skipping them in silence.
// 0.128.0: Z-D1 and Z-D6, the whole assembly. `mass_properties` on the assembly
//          (CreateMassProperty without bodies) checked against the sum of the
//          parts; `body_bounding_box` with the bodies of the components brought
//          into the assembly (they arrive in the space of the part); `export_step` and
//          `export_iges` of the assembly, with the solids of the STEP counted against
//          those of the parts (re-reading it opens a modal). Suppressed ones excluded and said.
// 0.129.0: Z-D3, Z-D4, Z-D5. The assembly state says the TYPES of the
//          mates by name (GetMates gives IMate2), whether a component is a
//          sub-assembly, whether it is visible and whether it is transparent (MaterialPropertyValues
//          [7]); new operation `fix_component` (FixComponent/UnfixComponent
//          on the selected component, re-read with IsFixed; even the first
//          component, which SOLIDWORKS fixes by itself, can be freed).
// 0.130.0: Z-F1, global variables and equations. `global_variable` (a
//          value in mm or an expression) and `link_dimension` (the dimension driven by
//          an expression), re-read from the VALUES (get_Status cannot be read);
//          the word an Italian SOLIDWORKS reserves for the thickness is a name already taken. The state
//          lists the equations of the part.
// 0.131.0: Z-F3, `reorder_feature`: ReorderFeature before or after another
//          feature, name checked with the feature_id, order and volume re-read;
//          a move that breaks a downstream feature is put back where it was.
// 0.132.0: Z-F5, `sketch_picture`: a photo or a hand sketch in a
//          sketch on a plane, to scale (SetSize with the aspect locked,
//          SetOrigin), verified with GetPointOnSketchFromPixel — which wants
//          (column, row), contrary to the name of its arguments. A PDF does not
//          go in. The bench reads {DATA} (Bench\data\, versioned).
// 0.133.0: GA11, `mirror_component`: IMirrorComponentFeatureData with typed
//          arrays; instance, opposite linked (MirrorStock) or unlinked, always
//          asked; verified the centre against the mirror and, for the opposites, the
//          link read from the tree of the new file.
// 0.134.0: Z-F4, `edit_extrusion`: GetDefinition as IExtrudeFeatureData2,
//          SetEndCondition/SetDepth, ModifyDefinition — the blind turned through, another
//          depth, the plate that grows; definition, volume and errors
//          re-read, and put back as it was if it breaks something downstream.
// 0.135.0: Z-B3, `file_dependencies`: the files the document depends on
//          (GetDependencies) and which are missing, said from the DISK — the parameter
//          «ListBrokenRefs» marks nothing (measured). The assembly state
//          says `file_missing` for the component whose file is not there.
// 0.136.0: the adversarial review of 27/9/2026 (entry Z-C20). The guards of
//          set_dimension in a single place (GuardOperations.cs) and used by
//          global_variable, link_dimension and edit_extrusion, with the restore;
//          edit_extrusion keeps the mid plane; on the drawing the «already dimensioned»
//          is read before the call and per CYLINDER, the round counts only the
//          planar faces (the chamfers are said), the boss is not a hole;
//          IGES of the assembly refused; mass per configuration of the component;
//          mirror refused removed; dependencies without an answer = «I do not know».
// 0.137.0: U1.1, Api\ApiCatalog: api_search and api_signature by reflection on
//          the loaded interop, with what is known of each member from
//          api_knowledge.json.
// 0.138.0: Z-C21.2, the silent defects of the API audit that need no probe:
//          SaveAs3 after ClearSelection2, WeldGap always on, combine_bodies by
//          signature, hidden bodies checked, angular dimensions in degrees,
//          read-only and driven dimensions refused, BothDirections refused,
//          GetErrorCode2 after ModifyDefinition, grids refused, the anchor on a
//          fillet edge refused (IFace2.Normal is 0,0,0 on a non-planar face).
// 0.139.0: Z-C21.6, one helper per recurring API pattern (rebuild errors,
//          selection by mark, unions of boxes, body lists) and the static
//          checks that keep obsolete calls and literal selection marks out
//          (ObsoleteCallsContract, SelectionMarksContract, SelectionMarks.cs).
// 0.140.0: the four operations rebuilt from the API help: fillet_face
//          (CreateDefinition(swFmFillet), marks 2/4), fill_pattern
//          (IFillPatternFeatureData, marks 1/4/16384), rib (InsertRib
//          parallel to the sketch), gusset back in the catalogue (mark 1
//          and a profile that is not degenerate).
// 0.141.0: Z-C21.2-bis, the silent defects of the second audit round that
//          need no probe: the profile library closed and checked (N31), the
//          frame reading and the plane names whatever their case (S20, N32,
//          N33), interference lists read whole (N34), the overhang cache
//          without nulls (N36), the whole drawing for "already dimensioned"
//          (N43), the hole count read back (N48), the anchored hole checks
//          (N50-N55), the IGES options read back (N59), the body verdict
//          (N61), hidden dimensions skipped (C8-2).
// 0.142.0: Z-C21.2-ter, sixteen audit findings fixed with the remedy the
//          Z-C21.3 probes measured: model dimensions without duplicates and
//          only the marked ones (M2, N9), silhouette dimensions not verifiable
//          instead of orphans (N3), the sketch lock that really locks (M5), arc
//          measures with their option (M29), the real picture scale (M11), the
//          mirrored instance orientation (N26), STEP solids with assembly cuts
//          (C4-2), BOM of promoted and hidden children (N27), dependencies
//          without same-name guesses (L12-8), material per configuration
//          (M10), miter by default and trim bits (N25, L9-4), fillet and
//          chamfer edges and vertex chamfers in the state (C7-2, N14, N29).
// 0.143.0: Z-C21.7, the obsolete calls the Z-C21.3 probes decided, one per
//          commit: offset planes through InsertRefPlane with mark 0, the edge
//          flange through CreateDefinition, ExportToDWG2, InsertHoleTable3,
//          InsertSketchPicture2, IsSuppressed2, CreateMassProperty2 for parts,
//          InsertCosmeticWeldBead2 with marks 4/8. The dimension readings
//          through GetSystemValue3 were migrated and withdrawn (the drawing's
//          own dimensions broke on the bench).
// 0.144.0: Z-C21.13, what the online research and its probes decided: STEP
//          AP242 through PublishSTEP242File (never swStepAP 242, which writes
//          AP203), the measured length-unit factors (feet-inches and micron
//          unknown), the gear spline through CreateSpline3 with natural ends,
//          centre marks added when a new view has none, documents closed and
//          brought to the front by path, not by title.
// 0.145.0: Z-C21.8 and Z-C21.9, returns read and acted on, every "put back as
//          it was" after a reread, and the catalogue texts and comments that
//          said the wrong thing corrected (the probe NOT-REAL findings now carry
//          their measure).
// 0.146.0: Z-C21.4 and Z-C21.5, the configuration always passed (components,
//          BOM, replacement, materials) and identity by the object (standard
//          planes recognised by their geometry, bodies by signature, features by
//          what the call returned), plus dimension readings in parts through
//          GetSystemValue3 (drawings stay on GetSystemValue2).
// 0.147.0: the Z-C21.5 leftovers (the sketch just drawn by identity, the end
//          cap on a drilled wall, one hole point by its sketch ID, a pending
//          rebuild after an activation) and Z-C21.11, the components of an
//          assembly read once: lightweight parts measured and never called
//          suppressed, envelopes out of the mass, hidden components out of the
//          STEP count, and the indented BOM compared with the tree SOLIDWORKS writes.
// 0.148.0: Z-C14, `mate` measures the directions a mate blocks (the
//          perturbation test measured on 30/9/2026) and refuses a redundant
//          mate; and the safety nets of W3.1-bis.2 around the library.
// 0.149.0: Z-C21.14, the plan executor hands on its plan document, the
//          document it ended on and what FinishRecordingUndoObject answered for
//          the plan's undo entry, so that `undo` reads the plan's document
//          and knows when a later entry sits on top.
// 0.150.0: W3.1-bis.5, rename window 2: the operations, arguments, values,
//          selectors and answer fields of the library are English names,
//          generated from the name map (archived outside the repository in 0.152.0).
// 0.150.1: W3.1-bis.6, rename window 3: the library is Oce.Solid.Agent.Core
//          (assembly, namespaces, folder) and its file names are English,
//          from the name map (archived outside the repository in 0.152.0).
// 0.150.2: W3.1-bis.10b, the names the library still held in Italian: the
//          values of the mates (`coincident`, `parallel`, `perpendicular`,
//          `distance`; `aligned`, `opposite`) and of the state (the kinds of
//          mates, selections, bodies and drawing views, the degrees of freedom
//          and the suppression states), the keys `standard` and `hole_type`
//          of a hole wizard, `perimeter_mm` and `normal_distance_mm`, the
//          document identity `title:`, the undo entry `Assistant`, and the
//          internal tags of the drawing dimensions. No alias.
// 0.151.0: D9 defects of the live proof of 6/10/2026. cut_list counts the gussets
//          and the end caps (their item folders are swSolidBodyCutList, not
//          swWeldmentCutlist) and says the item_type of every row; gusset finds
//          the contact of the faces of a foot at the END of a bar (the range of
//          an edge whose curve runs the other way is normalized); the first
//          insert_component into an empty assembly is no longer refused (the
//          count is asked of GetComponentCount, not read off a null array).
// 0.152.0: W3.1-bis.11, closure of the translation. The input dictionary is gone
//          (Alias.cs and every reader of it): a tool, operation, argument or
//          value that is not the English name is an unknown name, like any
//          other. The Italian branches of the number reader of the tests and
//          of the refusals table are gone with it. The surface has one name
//          for everything.
// 0.153.0: the SOLIDWORKS-language converter. The names SOLIDWORKS writes only as
//          localized text are one table (Localization/solidworks-names.json, a
//          column per language, chosen by ISldWorks.GetCurrentLanguage and by
//          the toggle «Use English feature and file names»; an unknown language
//          falls back to English and the document state says so in
//          solidworks_language). A standard view is found by what the model
//          view does when shown (IModelDoc2.ShowNamedView2 and
//          IModelView.Orientation3, put back afterwards), not by its name; a
//          cut-list column by its type and the property names of the table in
//          every column; the «through» word of a hole callout and the
//          reserved variable names through the table. The bench runs the same
//          cases in both languages (--sw-language).
// 0.153.0 (same version, second change of the release): W3.1-bis.12, faces beyond the 60 largest. Every face of a part has an
//          identifier, the rank of its area among ALL the faces (F1 the largest),
//          listed or not: the state lists the 60 largest as before, and
//          DocumentState.TryGetSignature builds the signature of any other face when
//          a plan cites it. FaceFilter and DocumentState.ReadFaces list the faces a
//          filter asks for (normal, area range, near a point, one body, a limit) with
//          those identifiers, so a small face of a part with more than 60 can be
//          named by a plan and by the model. The places that find a face by its
//          identifier (a face, a shell, a gusset, a weld bead, a measurement) ask the
//          state by rank. The state of a filtered reading says faces_total,
//          faces_matched, faces_filter and faces_not_listed.
// 0.153.1: final bench fix. A view SOLIDWORKS left without centre marks gets them
//          from IView.AutoInsertCenterMarks2 (hole type, after IDrawingDoc.ActivateView
//          as the page asks), in standard_views and in
//          center_marks, and each one is found again at its hole; AddCenterMark,
//          which adds nothing on a bare view (probe of 8/10/2026), is gone. The call
//          is never made on a view where a hole already has its mark: it doubles them.
//          standard_views writes one journal line when it fires.
// 0.155.0: brief of 25/10/2026, measured on 26/10/2026 (0.154.0 is the number of the
//          branch release-0.131.0, not merged yet: this one is cut from main).
//          PhaseClock times each phase of a call. PlanExecutor writes «timing_ms»
//          for each step, skips the state reading for a source that does not use
//          it (IPlanSourceWithoutState) and for the 40 operations that never cite
//          an identifier (SwOperations.OperationsWithoutState), lets the bridge own
//          CommandInProgress, and brings the plan's document back when another
//          window comes to the front (ReactivateWindow, MaxReactivations). A
//          plan of 21 steps went from 1,243.8 s to 63.6 s (same plan, a new part each time).
//          cylindrical_cut and rectangular_cut always add the sketch entities to
//          the database (SOLIDWORKS no longer snaps a centre), read the circle or
//          the rectangle back before the cut and refuse one that moved; a through
//          hole that drilled no wall fails. apply_material: a name shorter than
//          four letters counts only as a whole word (PLA was matching
//          «Plastiche»). extrude_profile tells the model axes of the sketch
//          («sketch_axes») and accepts a part that has bodies already.
// 0.156.0: briefs R12 and R21 (26/10/2026). place_component sets where a component
//          goes and how it is turned (Transform2, read back after a rebuild; the
//          mates win and the step is refused). The state lists «axes» for each
//          component. mate takes «distance_side» (Flip of AddMate5), measures the
//          side on the two faces and answers «placement_after».
// 0.157.0: brief R16 (26/10/2026). A plan step can carry «expect», next to its
//          arguments: the measurements it must come out with. One that does not
//          hold, or that the step does not measure, makes the step fail
//          (StepExpectations, PlanStep.Expect).
// 0.158.0: brief R6 (26/10/2026). The assembly state builds «what_can_be_done»
//          from the catalog (Operations.NamesFor) and names the components whose
//          degrees of freedom SOLIDWORKS did not give, instead of «every one».
// 0.159.0: brief R8 (26/10/2026). set_property takes «scope»: file,
//          configuration (named or active) or both, each drawer read back.
// 0.160.0: brief R15 (26/10/2026). StlMesh reads an STL file, measures it and
//          cuts it into contours in the sketch coordinates of extrude_profile.
// 0.161.0: brief R17 (26/10/2026). EdgeFilter and DocumentState.ReadFiltered:
//          the edges a filter asks for, by rank among all the edges; an edge
//          beyond the thirty listed is found by TryGetSignature.
// 0.162.0: brief R5 (26/10/2026). extrude_profile «dimensions»: «driven» puts a
//          dimension from the origin on every vertex (DriveProfile), «fixed» locks
//          the sketch as before. Origin selection and the two dimension calls not
//          measured yet: probe Checks\DrivenSketch, bench case
//          a_profile_is_driven_by_its_vertices.
// 0.162.1: brief R16 seen on SOLIDWORKS, an «expect» of volume on a step that
//          measures none (extrude_profile) failed after the step had run; the
//          plan executor now reads the volume of the part when it is asked for
//          and the step did not give it (PlanExecutor.WithVolumeIfAsked).
// 0.163.0: brief R11, extrude_profile draws without AddToDB (its entities could
//          not be undone), with the automatic relations off while drawing, locks
//          the sketch while it is open and reads every vertex back
//          (LockOpenSketch, VerticesNotDrawn; probe Checks\UndoSplit).
// 0.163.1: brief R11, extrude_profile draws with sketch inference off too and
//          joins the ends with ISketch.MergePoints: with snapping on, 6 vertices
//          of a star of 20 went onto points nearby (probe Checks\UndoSplit star).
// 0.163.2: save_document names the open document that holds the file it was
//          asked to write onto (GetOpenDocumentByName, after the refusal only).
// 0.164.0: brief R23, the undo of a plan on an assembly holds again: the plan
//          executor and the assembly operations rebuild an assembly with
//          IModelDocExtension.Rebuild (SwOperations.RebuildKeepingUndo), because
//          EditRebuild3 and ForceRebuild3 made FinishRecordingUndoObject answer
//          false and no undo took anything away (probe Checks\AssemblyUndo).
//          Brief R5: a driven profile says in «warning_dimensions» how many
//          coordinates sit on an axis (held by a relation, not drivable).
// 0.165.0: brief §5.1, «parameters» writes variables and links in one step
//          (EquationOperations.Parameters). An empty assembly reads empty
//          (GetComponentCount when GetComponents answers null). Revolve draws
//          with the R11 recipe and locks the open sketch; LockSketch is gone.
// 0.166.0: brief §5.2, «extrude_regions» extrudes several regions of one plane
//          to their heights in one step and names the depth dimension of each.
// 0.167.0: place_component moves the component with IDragOperator: setting
//          Transform2 left the undo entry of the plan empty (Checks\UndoGaps).
//          parameters answers equation_before for a variable it rewrote.
// 0.167.1: VerticesNotDrawn reads the points of the sketch once instead of
//          two COM calls per vertex and per point.
// 0.168.0: report 8cd843: the reading before a step leaves the edges out when
//          the step names none (DocumentState.ReadWithoutEdges,
//          PlanExecutor.MentionsEdges); a kept «expect» is said
//          (StepExpectations.Held, expect_verdict) and a missed one keeps the
//          numbers of the step.
// 0.168.1: MentionsEdges does not look into «_signatures», which carries the
//          signatures of everything read (measured: scenario t17).
// 0.168.2: the test of the directions judges a turn by the turn about its own
//          axis (TurnAboutAxisDeg), not the whole angle (report 8cd843 N3).
// 0.169.0: save_document in the family «documents» (report 8cd843 N7); the
//          stop for a window that changed says the steps already done (N12).
// 0.170.0: interference_check takes a sheet body as a contact, not a volume
//          (report 8cd843 N15), and names the pair and the box of each (N14);
//          the executor prints a document never saved by its name (N13).
// 0.170.1: interference_check says when Done marked a saved assembly
//          unsaved (N16, measured).
// 0.171.0: the shared refusal of an assembly exchange (N18), the basis of
//          the solid count (N19), the empty columns of a bill of materials
//          (N20), cells without FONT codes (N21), the note of a false
//          SetSheets (N22).
// 0.172.0: views of an assembly dimensioned in assembly coordinates, with
//          the overall size and the edge list (report 8cd843 N23),
//          delete_annotation (N24), fixed text of a title block (N25),
//          unsaved after set_property (N26), twin titles (N27), property of
//          an assembly (N28).
// 0.172.1: the twin title told by the views that rename a drawing (N27 was
//          only in create_drawing), and a note made on the sheet even when a
//          view was left active (found in the replay of round 7).
// 0.173.0: round 8 of report 8cd843: a note read back without its line ends
//          (N29), the loose components of an overall size named and
//          «components» to leave them out (N30), «count» on an assembly under a
//          warning key (N31), the note counts of delete_annotation apart (N32),
//          and a note of several lines found by its text.
// 0.174.0: round 9 of report 8cd843: «components» checked on the assembly
//          before the view is read, and a component the view hides said, not
//          refused (N33); the plan and the bench run one after-step, which does
//          not rebuild a drawing with nothing unsaved and reads the save flag
//          after a save (N34); every dimension says what the sheet prints (N35).
// 0.174.1: round 10 of report 8cd843 (N38): a view of an assembly whose overall
//          sizes are all dimensioned elsewhere is refused as on a view of a part,
//          naming the components measured and the ones the view does not show;
//          «already dimensioned» lengths with three decimals, as dimensions_mm.
// 0.174.2: round 11 of report 8cd843: a suppressed mate does not count among
//          the mates that hold a component, and its degrees of freedom are left
//          out with the reason (N39); the circle of a detail goes in the sketch
//          of the view, so a view of an assembly gets its detail (N41); section
//          and detail count the views of the model only (N42); the phases of
//          drawing_dimension are timed.
// 0.174.3: round 12 of report 8cd843: a detail goes to a free place of the
//          sheet, not on top of other views (N43); export_pdf says when it
//          marks a saved drawing as changed (N44); title_block leaves the notes
//          of the sheet out of the title block (N45); place_component and
//          fix_component count the mates that hold (N46); a sheet that is not
//          active says it was not read.
// 0.174.4: round 13 of report 8cd843: the circle of a detail and the cutting
//          line of a section go in the sketch of the view in its own
//          coordinates, and a detail with no edge or a section of another view
//          is refused (N47, N48); a new sheet takes the format of the current
//          one (N49); the balloons say the bill of materials they renumbered
//          (N50); after a section the sheet is in edit again (N51); a detail
//          view is settled to its true outline before it is placed.
// 0.174.5: round 14 of report 8cd843: the circle of a detail and the cutting
//          line of a section are drawn with no snapping and read back (N52);
//          a section slides along its line to a free place (N53);
//          move_view and delete_view (N54); save_document says the file it
//          replaced, and refuses with «overwrite» false (N55); balloons whose
//          arrows end within 5 mm of another are put again on a visible line
//          of their own part (N56); inspect_part gives the outline of a view.
// 0.174.6: round 15 of report 8cd843: every view of the state says its sheet,
//          and the state says that V1, V2 ... count the views of the active
//          sheet; delete_view refuses a view asked by number on a drawing of
//          more sheets (N57); move_view and delete_view say the sheet; the
//          section no longer reports hatches_solids (observation 2).
// 0.174.7: round 16 of report 8cd843: detail_view refuses a scale not
//          larger than the one of its view (N58); title_block reads the
//          models of the other sheets and says the names asked with
//          $PRPSHEET that they lack, in the verdict too (N59); the sheets
//          of the state say «active» false (observation).
// 0.174.8: round 17 of report 8cd843: balloons takes «order», around the
//          view or in the order of the bill of materials, and says the
//          numbers a tie between crowded arrows decided (N60); title_block
//          compares the formats of the sheets by their file (observation).
// 0.174.9: round 19 of report 8cd843: export_pdf finds a searched text
//          wrapped over two lines, and says it in found_on_several_lines
//          with a note, not in not_found (observation); title_block says the
//          boxes that show a value the model no longer has (N61).
// 0.175.0: round 20 of report 8cd843: detail_view refuses a circle in the
//          margin of the outline, saying where the view draws, and the state
//          says drawn_mm (N62); the verdict on the mates leaves out the
//          suppressed components and those a pattern places (N64); the state
//          says the folders of a component (N65); new operation
//          sketch_details reads what the sketches are made of (N63).
// 0.176.0: round 21 of report 8cd843: «scale» on standard_views and
//          model_view, new operation set_view_scale, the note when a view has
//          another scale than the sheet (point 1); every dimension placed
//          prints its value, «decimals» on drawing_dimension (point 4).
// 0.177.0: round 21 of report 8cd843, point 7: «mate» makes a width mate
//          («variant» width, «face_a2» and «face_b2», centred).
// 0.178.0: round 21 of report 8cd843, point 5: the texts of the
//          dimensions compared with the notes of the sheet and of its views
//          and with the title block; «overlaps_checked_against».
// 0.178.1: round 21 of report 8cd843: a chamfer dimension compared with
//          the definition of its feature (26P000011, «0,4 x 15,23°»).
// 0.179.0: round 22 of report 8cd843 (the drawings of 26M000002 set against the owner's):
//          «between», «row», the sides, «prefix»/«suffix», «parentheses», «holes» and
//          «chamfers» on drawing_dimension, the arrows where the text is, a detail
//          dimensioned above; «scale» on section_view; «circle_style» and
//          «lock_position» on detail_view; «again» on balloons; «over_title_block»
//          on drawing_bom; new operations move_annotation and read_drawing.
// 0.179.1: OCE_LOCAL_COPIES (LocalCopies.cs): the templates the operations open (drawing,
//          sheet format, table templates, the part of the section probe) are opened
//          from a local copy when the variable asks for it, and reported with the path
//          of the installation. Unset, nothing changes. Asked by the owner to make the bench
//          faster (29/10/2026).
// 0.179.2: the state asks SOLIDWORKS less for the same values (DocumentState.cs): the
//          listed edges reuse the curve, parameters and length read for the ranking
//          (EdgeReading), the listed faces reuse the area of the ranking and read their
//          surface and feature once. Same calls on the same entities: same values,
//          same ranking, same identifiers. Asked by the owner to make drawing faster
//          (29/10/2026): the edges were 32.2 % of the time of the plans in the bridge log.
// 0.179.3: the tree of the state asks less (DocumentState.cs): GetDefinition only for the five
//          types FeatureParameters reads (it was asked of every feature that counts and
//          thrown away), the name of a feature only where it is used (axes and features
//          that count), ThreadDiameter and DefaultRadius read once. NOT the same state on
//          weldments: GetDefinition on a structural member loaded the profile .SLDLFP from the
//          share as an open document and the order of the bodies changed between openings.
// 0.179.4: PhaseClock phases inside two slow operations, to see where their time goes:
//          «mate.direction_test» around the two readings of the directions before and
//          after AddMate5 (MateOperations.cs), «drawing.new_document» around the
//          NewDocument of create_drawing (DrawingOperations.cs); PhaseClock.Spent. Nothing else changes.
// 0.180.0: UiFreeze (UiFreeze.cs): the FeatureManager tree, its window and the graphics of the
//          view turned off while a plan runs (PlanExecutor, phases «plan.ui_freeze» and
//          «plan.ui_thaw»), put back as they were, the tree updated and the view redrawn (IModelView.GraphicsRedraw).
//          Only what was on is turned off. Off unless OCE_UI_FREEZE is 1, true or on: asked
//          by the owner to measure it, speed and accuracy, before deciding (29/10/2026).
// 0.181.0: the edges of the state read with IEdge.GetCurveParams2 (DocumentState.cs, ReadEdge):
//          GetCurve, the array (ends, parameters, packed curve type) and GetLength3 between the
//          same parameters, instead of GetCurve, GetCurveParams3, UMinValue, UMaxValue and
//          GetLength3; the listed edges take point and direction from the array, a circle asks
//          only CircleParams. Same lengths, same ranking (probe on 317 edges of four parts);
//          the ranking of 192 edges from 925.4 ms to 79.1 ms. Obsolete call, measured.
// 0.182.0: the bodies of the state in the natural order of their names (DocumentState.cs),
//          not in the order of GetBodies2, which «May vary»: between two identical bench runs
//          4 to 9 of the 9 states with more than one body had them in another order, same
//          names. C1, C2 and the F/E numbers of equal faces and edges of different bodies are
//          now the same in every run. The name is read once and used again.
// 0.183.0: the listed faces of the state ask IFace2.Normal (one call) instead of IGetSurface,
//          IsPlane, PlaneParams and FaceInSurfaceSense, and the surface only when the face is
//          not planar; the feature number from IFace2.GetFeatureId, its name asked once per
//          number (checked against GetID on the first face of each). Probe on 136 faces of four
//          parts: same planar faces, same normals, same feature numbers.
// 0.184.0: the rankings of faces and edges sort on the measure rounded to the sixth decimal
//          (mm², mm): measures equal by design keep the order of SOLIDWORKS instead of the
//          order of the last bits of a double, which changed when the length was computed
//          through another call (eight equal arcs, A/B of 0.180.0 and 0.181.0).
// 0.185.0: the length of a straight edge is the distance between its ends from the array of
//          GetCurveParams2, without GetLength3; safe since the rounded key of 0.184.0. Probe on
//          317 edges of four parts: equal to GetLength3 to the sixth decimal, ranking unchanged.
// 0.186.0: the same state in every run. `created_by` is the oldest feature of the body's faces;
//          ties ordered by body, then feature number (faces) or middle of the ends (edges);
//          `between` lower number first; `documents_open` by title. Bench of 30/10/2026: 3
//          state files of 291 differed between two equal runs, all by an order.
// 0.186.1: zero is not «I do not know» (the pre-push hook stopped the push of 0.186.0 on 12
//          points): `edges_total` of `view_edges`, `notes_on_the_sheet_left_out` of
//          `title_block` and the tables of the sheet are null when they cannot be read; the
//          pieces of a view without the array of its lines are null; the true zeros carry
//          the marker with the reason.
// 0.187.0: the reading holds the curves of its edges (one EdgeReading per ranked edge), so that
//          whoever keeps the reading alive keeps them too; the bridge does it only with
//          OCE_KEEP_LAST_READING=1 (bridge 0.160.0). Nothing else changes in the state.
// 0.188.0: OCE_UI_FREEZE can name one part: `graphics` (only the graphics of the view) or `tree`
//          (only the tree and its window); `1`, `true`, `on` still turn off all three. The cycle of
//          15 drawings fell 11 times in 11 without the freeze and 0 in 3 with it (30/10/2026).
[assembly: AssemblyVersion("0.188.0.0")]
[assembly: AssemblyFileVersion("0.188.0.0")]
// It had stayed at 0.61.0 while the assembly went to 0.66.0: a number that
// nobody reads and that tells a falsehood to whoever opens the file properties. It
// served no purpose and it was wrong, which is the worst combination.
//
// AND IT HAPPENED AGAIN: on 22/9/2026 it said 0.94.0 with the assembly at 0.99.0, that is
// five versions behind. The line above tells the defect and did not
// prevent it — because a comment is not a check.
//
// NOW THE CHECK EXISTS, and on 23/9/2026 it spoke: «assembly 0.101.0.0,
// file property 0.100.0». The defect had recurred for the third time,
// and this time nobody found it by reading — the contract suite said so,
// before the commit. *Between a comment that warns and a check that turns
// red there is no difference of degree: the first is read when one has
// already gone wrong, the second prevents going wrong.*
[assembly: AssemblyInformationalVersion("0.188.0")]
