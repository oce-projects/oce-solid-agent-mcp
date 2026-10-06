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
//          - the holes not dimensioned: hole callout («4× Ø8 PASSANTE») and
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
//          «Saldatura» if it is missing, turns on the automatic cut list on the folder
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
//          «spessore» is a name already taken in SOLIDWORKS in Italian. The state
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
[assembly: AssemblyVersion("0.152.0.0")]
[assembly: AssemblyFileVersion("0.152.0.0")]
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
[assembly: AssemblyInformationalVersion("0.152.0")]
