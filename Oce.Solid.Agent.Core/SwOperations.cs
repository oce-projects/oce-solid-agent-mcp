using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// The six operations the agent can really build.
    ///
    /// THE RULE AT THE TOP OF EVERYTHING
    /// This file writes inside a person's document. Every method in
    /// here does one thing, does it with parameters already validated by the
    /// server, and if something does not add up it does NOT try to fix it: it
    /// fails and says so. An automatic attempt at a remedy inside someone
    /// else's model is the best way to produce a part that looks right.
    ///
    /// NAMED PARAMETERS, ALWAYS
    /// FeatureExtrusion3 has 23 parameters, FeatureCut4 has 27. Written
    /// positionally, one swapped gives no compile error and no
    /// exception: it gives a different feature. The names below are the real ones,
    /// read by reflection from interop 29.5.1.1 (SOLIDWORKS 2021), not
    /// remembered.
    ///
    /// UNITS
    /// Every input value is in MILLIMETERS and DEGREES, as on the
    /// protocol. The conversion is done by Units, and only by Units.
    ///
    /// THE DECLARED LIMIT OF THIS VERSION
    /// The operations on a face (holes and pockets) work only on PLANAR faces
    /// perpendicular to one of the main axes. On a tilted face the sketch
    /// coordinate system does not coincide with the model's, and the x/y
    /// coordinates arriving from the plan would end up elsewhere — with no
    /// error at all. Better to refuse: whoever reads a refusal understands, whoever
    /// looks at a hole shifted by 30 mm often does not notice.
    /// </summary>
    public partial class SwOperations
    {
        private readonly ISldWorks _sw;
        private readonly Action<string> _log;

        /// <summary>What the previous steps of this plan have left behind.</summary>
        private IReadOnlyDictionary<int, References.Step> _stepsDone =
            new Dictionary<int, References.Step>();

        /// <summary>
        /// How many features the tree had when this step began.
        ///
        /// Until Z-C21.5 it was what let <see cref="WithoutSketch"/> tell a
        /// sketch made by this step from one already there; that is now
        /// <see cref="_featureNamesAtStepStart"/>, which says WHICH features
        /// were born and not only that the tree grew. The count is still read
        /// after a withdrawal (AnchorOperations, FillPatternOperations):
        /// a tree longer than at the start means the step left something in
        /// the part.
        ///
        /// The start value is int.MaxValue and NOT zero: if nobody wrote it,
        /// the tree cannot look «grown». A defect of ours must leave one sketch
        /// too many, never delete someone else's feature.
        /// </summary>
        private int _featuresAtStepStart = int.MaxValue;

        /// <summary>
        /// The names of the features of the tree when this step began, or null
        /// (not a part, tree unreadable, or not written yet).
        ///
        /// SINCE Z-C21.5 (scheme S7) this, and no longer the count above, is
        /// what lets <see cref="WithoutSketch"/> tell a sketch made by this
        /// step from one that was already there: the count only said that the
        /// tree had grown, and the sketch was then taken as the last feature of
        /// the tree, which on a sheet-metal part is the flat pattern. Null is
        /// the safe default for the same reason int.MaxValue is above: with no
        /// «before», nothing looks born and nothing is deleted.
        /// </summary>
        private HashSet<string> _featureNamesAtStepStart;

        public SwOperations(ISldWorks sw, Action<string> log)
        {
            _sw = sw;
            _log = log ?? (m => { });
        }

        /// <summary>
        /// Whether an operation that must work on the document in front may bring
        /// it back when another window came there (brief R1). True by default;
        /// PlanExecutor passes its own ReactivateWindow, so a plan that stops on a
        /// window change is refused instead.
        /// </summary>
        public bool MayBringDocumentBack { get; set; } = true;

        /// <summary>
        /// WHEN A CHECK REJECTS A SHAPE, THE SHAPE IS ALREADY IN THE PART.
        ///
        /// The operations that check themselves — the slot counts the
        /// material removed, the shell the volume — decide AFTER calling
        /// the feature, because before there is nothing to measure. If the measure
        /// does not add up, the cut stays: undoing a feature that has REMOVED
        /// material is not the same as removing an empty sketch
        /// (see <see cref="WithoutSketch"/>), and it has not been tried.
        ///
        /// So it is SAID, always and in the same words. A refusal that
        /// stays silent lets people believe the part is intact.
        ///
        /// Only where the cut is really still in the part (Z-C21.9, finding
        /// C9-6): once DeleteSelection2 has removed it, RemoveCut answers
        /// with RemovedButTreeLonger or RemovedButVolumeChanged instead.
        /// </summary>
        private const string RemainsInPart =
            " The cut has already been made, though: withdraw it — " + Sentences.HowToWithdrawIt + ".";

        /// <summary>
        /// Tail of the error messages that leave a sketch in the tree.
        ///
        /// IT IS NO LONGER THE NORMAL ROAD: since 0.43.0 we remove the sketch
        /// ourselves, and this sentence stays for the case where we do NOT manage to. The
        /// reason for the change is measured: on 11 September 2026, eight requests
        /// beyond the limits produced seven right refusals and seven orphan
        /// sketches. The message was honest, but «you can remove it with Ctrl+Z»
        /// seven times is not a repair: it is a task handed to the reader.
        ///
        /// Whoever receives it now knows two true things together: that the operation
        /// did not succeed, and that something of ours has been left in the part.
        /// </summary>
        private const string LeftSketchBehind =
            " The sketch I had drawn stayed in the part: you can remove it — " +
            Sentences.HowToWithdrawIt + ".";

        /// <summary>
        /// The third outcome of <see cref="WithoutSketch"/>, and the most important
        /// of the three: the sketch was removed, but by removing it the part
        /// changed.
        ///
        /// It should not happen — only a feature that is a sketch is deleted,
        /// one that sits at the bottom of the tree and that nothing depends on —
        /// and precisely for this reason it must be said if it happens. Answering
        /// <see cref="LeftSketchBehind"/> here would be a lie («the sketch
        /// was left behind» when it was not) that would cover up real damage.
        ///
        /// IT HAS NEVER BEEN SEEN TO HAPPEN, and on 14/9/2026 we tried to make it
        /// happen. Entry D7 of the TODO.
        ///
        /// Why it is hard to reach, reasoning on the conditions that
        /// <see cref="WithoutSketch"/> imposes before getting here: the sketch
        /// is the last feature, it was born in this step, and **it has no children**. A
        /// sketch with no children carries no material, and `swDelete_Absorbed` removes
        /// it and its dimensions and nothing else. So volume and body count
        /// cannot change, and the feature count drops by exactly one.
        ///
        /// We also tried the nastiest case we knew how to build: the
        /// same run on a part that is **already unstable** — with a feature in
        /// error in the tree — because the deletion triggers a rebuild, and a
        /// rebuild on a broken part can give a different volume. It did not
        /// happen: the sketch was removed cleanly and the message did not come out.
        ///
        /// WHAT WOULD MAKE IT REACHABLE. One of these:
        ///
        ///   * `IGetChildCount()` answering 0 for a sketch that does have a
        ///     child (an external reference not counted as a child);
        ///   * an operation that leaves a BODY behind besides the
        ///     sketch, and then fails: there the body count would change;
        ///   * `swDelete_Absorbed` that in a future version deletes more than
        ///     it deletes today.
        ///
        /// **The message stays**, and it is not dead code: it is the guard that
        /// tells «I put the part back as it was» from «I believe I put it back
        /// as it was». The day one of the three things above becomes true,
        /// this line is the difference between noticing and not noticing.
        /// And it is written on purpose as «check it» and not as «I broke the
        /// part»: what is known is that the numbers do not add up, not who
        /// did it.
        /// </summary>
        private const string SketchRemovedButNotOnlyIt =
            " I removed the sketch I had drawn, but removing it also changed the" +
            " part: check it, and if it is not as you want it withdraw it — " +
            Sentences.HowToWithdrawIt + ".";

        /// <summary>
        /// The type of the part's origin, the only thing that <see cref="WithoutSketch"/>
        /// refuses by TYPE name.
        ///
        /// Measured on 14 September 2026: the origin answers ISketch to
        /// `GetSpecificFeature2` exactly like a real sketch, and on an empty
        /// document it is the last feature of the tree. It is not a translated name —
        /// that one is `Name`, which in an Italian installation is the localized name of the origin — it is
        /// the API's TYPE name, which does not change with the language.
        /// </summary>
        private const string PartOrigin = "OriginProfileFeature";

        public class Outcome
        {
            public bool Succeeded;
            public string Error;
            /// <summary>Name of the feature created: needed to go back.</summary>
            public string CreatedFeature;
            /// <summary>
            /// The real feature, not its name. The plan executor keeps it
            /// so it can resolve the references of the later steps: the name
            /// is localized and can be renamed, the object cannot.
            /// </summary>
            public IFeature Feature;
            /// <summary>
            /// The direction along which this step worked. It is used to say
            /// which face is «on top»; null for the operations that have
            /// no direction.
            /// </summary>
            public double[] Direction;
            /// <summary>
            /// THE DOCUMENT WORKED ON FROM NOW ON, when this
            /// operation has changed it ON PURPOSE.
            ///
            /// Only one does that — `create_drawing` — and it is not a
            /// side effect: a drawing IS a new document, and the later steps
            /// (the views, the dimensions) must be done there.
            ///
            /// It is needed because whoever runs the plan watches that the document does not
            /// change under its feet: it is the defense that prevents extruding into
            /// someone else's part. Without a way to DECLARE the change,
            /// that defense would stop a correct plan («make me the drawing and
            /// put the views on it») — and refusing what is right is the
            /// worse of the two ways of being wrong.
            ///
            /// Only what is declared is followed. A document that changes
            /// without anyone saying so remains a reason to stop.
            /// </summary>
            public IModelDoc2 NewDocument;
            public Dictionary<string, object> Measurements = new Dictionary<string, object>();
        }

        /// <summary>
        /// Runs one operation of the plan. Always returns an outcome, never throws.
        /// </summary>
        /// <param name="stepsDone">
        /// What the steps already run of this same plan have left behind,
        /// by sequence number. It is what lets step 2 say «the
        /// face on top of what you did in step 1» instead of citing
        /// an identifier that, when the plan was written, did
        /// not exist yet.
        /// </param>
        /// <summary>
        /// The names that the switch below knows how to run.
        ///
        /// IT SERVES TO COMPARE THE CATALOG WITH WHAT REALLY EXISTS. The
        /// catalog declares 38 operations; here there are 36, and the two that
        /// are missing — `extrude_section` and `spur_gear` — exist only because
        /// the server expands them into `contours` before they arrive here, and the `default`
        /// branch falls back to the profile extrusion when it finds
        /// contours. Called with their real arguments they answer
        /// «unknown operation». The check that compares the two sets says so; without
        /// a list, it would be a regex over 4,700 lines that breaks at the first
        /// reformatting.
        ///
        /// **THIS LIST CAN DERIVE FROM THE SWITCH, and that is its flaw.**
        /// We defend against it in two ways, both in the check and none in
        /// good will: we verify that every name in here is really
        /// dispatchable (calling `Run` and looking that it does NOT answer
        /// «unknown operation»), and we compare with the `case` labels read from the
        /// source — where reading zero is a failure, not a success.
        /// </summary>
        public static readonly string[] ExecutableNames =
        {
            "apply_material", "slot", "activate_configuration",
            "mate", "combine_bodies",
            "weld_bead", "create_axis", "create_configuration",
            "create_drawing", "create_plane", "cut_list",
            "plane_three_points", "weldment_profiles", "sketch_picture", "file_dependencies",
            "minimum_distance", "delete_feature",
            "export_iges", "export_pdf", "export_step", "export_stl",
            "extrude_circle", "extrude_profile", "extrude_rectangle",
            "extrude_section", "spur_gear",
            "extrude_on_face", "gusset", "cosmetic_thread",
            "hole_wizard", "shell", "insert_component",
            "set_sheet", "engrave_text", "body_bounding_box", "structural_member",
            "measure_between", "set_dimension", "global_variable", "link_dimension", "reorder_feature", "edit_extrusion", "dimension_tolerance", "mass_properties",
            "loft_two_circles",
            "model_dimensions", "fillet_edges", "variable_fillet", "fillet_face",
            "circular_pattern", "fill_pattern", "set_property",
            "linear_pattern", "revolve", "save_document",
            "draft", "rib", "chamfer_edges", "suppress_instance", "suppress_feature",
            "replace_component", "fix_component", "mirror_component", "suppress_mate", "delete_mate", "place_component",
            "center_marks", "drawing_dimension", "note", "hole_table", "flat_pattern_view", "drawing_weld_symbol", "drawing_bom", "balloons",
            "surface_finish", "datum", "geometric_tolerance",
            "section_view", "detail_view",
            "new_sheet", "activate_sheet", "drawing_cut_list",
            "interference_check", "title_block",
            "circular_sweep",
            "mirror",
            "move_bodies", "trim_members",
            "cylindrical_cut", "rectangular_cut", "end_cap",
            "rectangular_frame", "check_body_geometry",
            "check_overhangs",
            "model_view", "standard_views",
            "base_flange", "edge_flange", "hem", "sketched_bend", "flat_pattern_dxf", "dimension_scheme",
        };

        /// <summary>
        /// The exact sentence with which the switch refuses a name it does not know.
        /// It lives here and is not written twice because the check compares it:
        /// if it changes, the check must notice instead of believing
        /// that all is well.
        /// </summary>
        public const string UnknownOperation = "Unknown operation: ";

        /// <summary>
        /// The operations whose case in <see cref="Run"/> does not pass the
        /// reading of the document on (brief R2). For them the executor does not
        /// read the document before the step: on 24/10/2026 a full reading of a
        /// part with a body was one of the two that every step paid.
        ///
        /// A list written by hand next to a switch goes stale; the contract
        /// suite reads the switch and fails when an operation here passes
        /// `docState` on, or one that does not pass it is missing.
        /// </summary>
        internal static readonly HashSet<string> OperationsWithoutState =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "extrude_rectangle", "extrude_circle", "extrude_section", "spur_gear",
                "extrude_profile", "apply_material", "dimension_tolerance", "create_plane",
                "base_flange", "flat_pattern_dxf", "mirror", "revolve", "create_drawing",
                "set_sheet", "standard_views", "model_view", "model_dimensions",
                "center_marks", "drawing_dimension", "note", "hole_table",
                "flat_pattern_view", "drawing_weld_symbol", "drawing_bom", "balloons",
                "surface_finish", "datum", "geometric_tolerance", "section_view",
                "detail_view", "new_sheet", "activate_sheet", "drawing_cut_list",
                "title_block", "export_pdf", "save_document", "structural_member",
                "rectangular_frame", "cut_list", "rib", "suppress_mate", "delete_mate", "place_component",
            };

        /// <summary>True when the operation looks at the reading of the document.</summary>
        public static bool UsesState(string operation)
        {
            return operation == null || !OperationsWithoutState.Contains(operation);
        }

        /// <summary>
        /// THE PATHS IN WINDOWS FORM, for every operation (26/10/2026). A path
        /// written with forward slashes, «C:/folder/part.SLDPRT», is a path
        /// Windows accepts and SOLIDWORKS does not always: SaveAs with it on the
        /// file the document already is answered swGenericSaveError (the
        /// unexplained failure of 13/10/2026, reproduced: the same call with
        /// backslashes saved 140,100 bytes), and AddComponent5 answered null.
        /// A model writes forward slashes because they need no escaping in JSON.
        /// The arguments that name a file are turned with Path.GetFullPath when
        /// they are rooted and hold a forward slash; anything else is left as it
        /// came. A copy: the caller's dictionary is not touched.
        /// </summary>
        internal static Dictionary<string, object> WithWindowsPaths(Dictionary<string, object> arguments)
        {
            if (arguments == null) return null;
            Dictionary<string, object> copy = null;
            foreach (var key in PathArguments)
            {
                object raw;
                if (!arguments.TryGetValue(key, out raw)) continue;
                var text = raw as string;
                if (string.IsNullOrWhiteSpace(text) || text.IndexOf('/') < 0) continue;
                string full;
                try
                {
                    if (!System.IO.Path.IsPathRooted(text.Trim())) continue;
                    full = System.IO.Path.GetFullPath(text.Trim());
                }
                catch (Exception) { continue; }
                if (copy == null) copy = new Dictionary<string, object>(arguments, StringComparer.Ordinal);
                copy[key] = full;
            }
            return copy ?? arguments;
        }

        /// <summary>The arguments of the catalogue that name a file on disk.</summary>
        internal static readonly string[] PathArguments = { "path", "profile_path", "template", "model" };

        public Outcome Run(
            IModelDoc2 model,
            string operation,
            Dictionary<string, object> arguments,
            DocumentState docState,
            IReadOnlyDictionary<int, References.Step> stepsDone = null)
        {
            _stepsDone = stepsDone ?? new Dictionary<int, References.Step>();
            // It is emptied at the START: emptying it at the end would leave it full
            // when the operation fails, and the next one would inherit the
            // offsets of the one before.
            _reanchors.Clear();
            // The picture of the tree BEFORE anything is touched: the count,
            // read after a withdrawal, and the names, which say WHICH features
            // this step made (Z-C21.5, scheme S7) and are what WithoutSketch
            // reads, so as never to delete what was already there. Here and not
            // in each operation, because every operation comes through here.
            _featuresAtStepStart = CountFeature(model);
            _featureNamesAtStepStart = FeatureNamesNow(model);
            arguments = WithWindowsPaths(arguments);
            try
            {
                switch (operation)
                {
                    case "extrude_rectangle": return ExtrudeRectangle(model, arguments);
                    case "extrude_circle": return ExtrudeCircle(model, arguments);
                    case "extrude_section": return ExtrudeSection(model, arguments);
                    case "spur_gear": return SpurGearOp(model, arguments);
                    // The free shape and the RECIPES end up in the same
                    // executor: a gear is not another operation,
                    // it is a way of writing contours. The server expands it
                    // and sends the same `contours`. Two executors for the same
                    // thing would mean two behaviors that diverge at the
                    // first change.
                    case "extrude_profile":
                        return ExtrudeProfile(model, arguments);
                    case "cylindrical_cut": return CylindricalCut(model, arguments, docState);
                    // The hole made the way a hole is made: guided creation,
                    // ISO standard, thread, counterbore, countersink. It lives in
                    // HoleOperations.cs because the recipe is long and
                    // every line of that recipe was measured.
                    case "hole_wizard": return HoleWizard(model, arguments, docState);
                    // The material: MaterialOperations.cs.
                    case "apply_material": return ApplyMaterial(model, arguments);
                    case "rectangular_cut": return RectangularCut(model, arguments, docState);
                    case "slot": return Slot(model, arguments, docState);
                    case "extrude_on_face": return ExtrudeOnFace(model, arguments, docState);
                    case "set_dimension": return SetDimension(model, arguments, docState);
                    // Global variables and equations (entry Z-F1): giving a name to
                    // a number instead of repeating it. EquationOperations.cs.
                    case "global_variable": return GlobalVariable(model, arguments, docState);
                    case "link_dimension": return LinkDimension(model, arguments, docState);
                    // Reordering the tree (entry Z-F3). ReorderOperations.cs.
                    case "reorder_feature": return ReorderFeature(model, arguments, docState);
                    // Changing an extrusion or a cut already made (entry Z-F4).
                    // EditExtrusionOperations.cs.
                    case "edit_extrusion": return EditExtrusion(model, arguments, docState);
                    case "dimension_tolerance": return DimensionTolerance(model, arguments);
                    case "suppress_feature": return SuppressFeature(model, arguments, docState);
                    case "delete_feature": return DeleteFeature(model, arguments, docState);
                    case "move_bodies": return MoveBodies(model, arguments, docState);
                    // The booleans between bodies and the variable-radius fillet
                    // live in BodyOperations.cs.
                    case "combine_bodies": return CombineBodies(model, arguments, docState);
                    case "variable_fillet":
                        return VariableFillet(model, arguments, docState);
                    // The FACE fillet (entry GP7, the half that was missing):
                    // CreateDefinition(swFmFillet) and marks 2/4, not FeatureFillet3.
                    // FaceFilletOperations.cs.
                    case "fillet_face":
                        return FaceFillet(model, arguments, docState);
                    case "cosmetic_thread":
                        return CosmeticThread(model, arguments, docState);
                    case "fillet_edges": return FilletEdges(model, arguments, docState);
                    case "chamfer_edges": return ChamferEdges(model, arguments, docState);
                    case "shell": return Shell(model, arguments, docState);
                    case "create_plane": return CreatePlane(model, arguments);
                    // The oblique plane, in PlaneOperations.cs. `create_plane` can only
                    // make the offset from the three standards: for a tilted
                    // face there was nothing to draw on.
                    // The operation the catalog asked for and that was not there:
                    // it lives in ProfileOperations.cs. Without it, «structural_member»
                    // told the model to copy from something nonexistent.
                    case "weldment_profiles":
                        return WeldmentProfiles(model, arguments, docState);

                    // ── assemblies ───────────────────────────────────────
                    // The first. It lives in AssemblyOperations.cs, and what it does
                    // besides inserting is counting the residual degrees of freedom:
                    // «AddComponent5 places without mating», and four free bodies
                    // look like an assembly.
                    case "insert_component":
                        return InsertComponent(model, arguments, docState);
                    // The second, and the one that makes the difference: inserting is not
                    // mating. It lives in MateOperations.cs.
                    case "mate":
                        return Mate(model, arguments, docState);
                    // «The cover has to be redone»: it lives in ReplaceOperations.cs,
                    // and what it measures is whether the mates survive.
                    case "replace_component":
                        return ReplaceComponent(model, arguments, docState);
                    // Fixing or freeing (entry Z-D5): the only state of a component
                    // that changes how the assembly rebuilds. FixComponentOperations.cs.
                    case "fix_component":
                        return FixComponent(model, arguments, docState);
                    // A mate taken out by its uid or its name (brief R22).
                    // MateStateOperations.cs.
                    case "suppress_mate":
                        return SuppressMate(model, arguments);
                    case "delete_mate":
                        return DeleteMate(model, arguments);
                    // Where a component goes and how it is turned (brief R12).
                    // PlacementOperations.cs.
                    case "place_component":
                        return PlaceComponent(model, arguments);
                    // The right bracket and the left one (entry GA11). MirrorComponentOperations.cs.
                    case "mirror_component":
                        return MirrorComponent(model, arguments, docState);
                    // An image to trace, to scale, in a sketch (entry Z-F5).
                    // RetraceOperations.cs.
                    case "sketch_picture":
                        return SketchPicture(model, arguments, docState);
                    case "plane_three_points":
                        return PlaneThroughThreePoints(model, arguments, docState);

                    // ── pipes and transitions ──────────────
                    // They live in PathOperations.cs. The only two operations
                    // that make a non-straight shape, and the only ones where the
                    // profile is a NUMBER and not a sketch to be named.
                    case "circular_sweep":
                        return CircularSweep(model, arguments, docState);
                    case "loft_two_circles":
                        return LoftTwoCircles(model, arguments, docState);
                    // ── sheet metal (entry GP18) and dimensions in the model (entry Z-H3) ──
                    // In SheetMetalOperations.cs and DimXpertOperations.cs, each
                    // with the measure that made it buildable written at the top.
                    case "base_flange": return BaseFlange(model, arguments);
                    case "edge_flange": return EdgeFlange(model, arguments, docState);
                    case "hem": return Hem(model, arguments, docState);
                    case "sketched_bend": return SketchedBend(model, arguments, docState);
                    case "flat_pattern_dxf": return FlatPatternDxf(model, arguments);
                    case "dimension_scheme": return DimensionScheme(model, arguments, docState);
                    case "create_axis": return CreateAxis(model, arguments, docState);
                    case "linear_pattern": return LinearPattern(model, arguments, docState);
                    case "mirror": return Mirror(model, arguments);
                    case "revolve": return Revolve(model, arguments);
                    case "circular_pattern": return CircularPattern(model, arguments, docState);
                    // FillPatternOperations.cs (entry W1.1).
                    case "fill_pattern": return RepeatFill(model, arguments, docState);

                    // ── drawing ──────────────────────────
                    // They live in DrawingOperations.cs. They are the first
                    // operations of the catalog that do NOT work on a part:
                    // they create a second document and keep it open
                    // together with the model. The three traps of the family
                    // (the projection angle, the dimension that lies, the PDF of a single
                    // sheet) are explained at the top of that file.
                    case "create_drawing": return CreateDrawing(model, arguments);
                    case "set_sheet": return SetSheet(model, arguments);
                    case "standard_views": return StandardViews(model, arguments);
                    case "model_view": return ModelView(model, arguments);
                    case "model_dimensions": return ModelDimensions(model, arguments);
                    // The center marks, in CenterMarkOperations.cs. A drawing
                    // with twelve holes and no center gets printed and looks
                    // complete: in the shop it is missing where to drill.
                    case "center_marks": return CenterMarks(model, arguments);
                    case "drawing_dimension": return DrawingDimension(model, arguments);
                    // The note goes on the SHEET, not inside a view: see
                    // `DrawingNoteOperations.cs`, where it also says why
                    // counting it in the wrong place would have closed four entries.
                    case "note": return DrawingNote(model, arguments);
                    case "hole_table": return HoleTable(model, arguments);
                    case "flat_pattern_view": return FlatPatternView(model, arguments);
                    case "drawing_weld_symbol": return DrawingWeldSymbol(model, arguments);
                    case "drawing_bom": return DrawingBom(model, arguments);
                    case "balloons": return Balloons(model, arguments);
                    case "surface_finish": return SurfaceFinish(model, arguments);
                    case "datum": return Datum(model, arguments);
                    case "geometric_tolerance": return GeometricTolerance(model, arguments);
                    case "section_view": return SectionView(model, arguments);
                    case "detail_view": return DetailView(model, arguments);
                    // The sheets, in SheetOperations.cs. `export_pdf` already counts
                    // the pages of the PDF, and that count was always 1
                    // because there was no way to make a second sheet.
                    case "new_sheet": return NewSheet(model, arguments);
                    case "activate_sheet": return ActivateSheet(model, arguments);
                    // The cut list brought onto the drawing, and COMPARED with
                    // the part's: the second reading already existed.
                    case "drawing_cut_list":
                        return DrawingCutList(model, arguments);
                    // «Does the cover touch the base?» — in InterferenceOperations.cs.
                    // Two interpenetrating parts give no error: they open,
                    // they export, they print, and in the shop they do not fit.
                    case "interference_check":
                        return InterferenceCheck(model, arguments, docState);
                    // The title block, in TitleBlockOperations.cs. It does not fill it:
                    // it says which properties it ASKS FOR and which it SHOWS, so the
                    // name to write is copied instead of guessed.
                    case "title_block": return TitleBlock(model, arguments);
                    case "export_pdf": return ExportPdf(model, arguments);
                    case "save_document": return SaveDocument(model, arguments);

                    // ── weldments ───────────────────────
                    // They live in WeldmentOperations.cs: they are a
                    // subsystem with its own APIs and its own traps, and
                    // keeping them here would have made a two-thousand-line file.
                    //
                    // «rectangular_frame» ends up in the same executor:
                    // the server expands it into segments and sends the same
                    // parameters as «structural_member», as already happens
                    // for the standard sections. Two executors for the
                    // same thing would mean two behaviors that
                    // diverge at the first change.
                    case "structural_member":
                    case "rectangular_frame": return StructuralMember(model, arguments);
                    case "trim_members": return TrimMembers(model, arguments, docState);
                    case "cut_list": return CutList(model, arguments);
                    case "end_cap": return EndCap(model, arguments, docState);
                    case "gusset": return Gusset(model, arguments, docState);
                    case "weld_bead": return WeldBead(model, arguments, docState);

                    // ── checks ───────────────────────
                    // They live in CheckOperations.cs. They DO NOT DRAW: they say
                    // how it came out. `cut_list` is the precedent —
                    // a catalog operation that reads instead of building.
                    case "check_body_geometry":
                        return CheckBodyGeometry(model, arguments, docState);
                    // What cannot be printed without supports. It lives in
                    // OverhangOperations.cs, and like the other checks it does not
                    // touch the part: it looks at the angle of every surface.
                    case "check_overhangs":
                        return CheckOverhangs(model, arguments, docState);
                    // The two questions asked before ordering: how much it
                    // weighs, and what box it fits in. They live in
                    // MeasureOperations.cs.
                    case "mass_properties":
                        return MassProperties(model, arguments, docState);
                    // Which files the document depends on, and which are missing (entry Z-B3).
                    // DependencyOperations.cs.
                    case "file_dependencies":
                        return Dependencies(model, arguments, docState);
                    case "body_bounding_box":
                        return BodyBoundingBox(model, arguments, docState);
                    case "minimum_distance":
                        return MinimumDistance(model, arguments, docState);
                    case "measure_between":
                        return MeasureBetween(model, arguments, docState);

                    // ── what the part says about itself ─
                    // They live in PropertyOperations.cs. They draw nothing:
                    // they fill the title block and the PDM card, and make the
                    // variants. The properties of the DOCUMENT and those of a
                    // CONFIGURATION are two separate lists that do not
                    // see each other — measured, not deduced.
                    case "set_property":
                        return SetProperty(model, arguments, docState);
                    case "create_configuration":
                        return CreateConfiguration(model, arguments, docState);
                    case "activate_configuration":
                        return ActivateConfiguration(model, arguments, docState);

                    // ── the marking ────────────────────
                    // It lives in TextOperations.cs. It is the only operation that
                    // writes WORDS into the part, and the only one where the
                    // size of what is drawn cannot be computed
                    // beforehand: it depends on the font. It is measured and checked.
                    case "engrave_text":
                        return EngraveText(model, arguments, docState);

                    // ── removing a copy ──────────────
                    // It lives in InstanceOperations.cs. It is not a new feature:
                    // it is a write INSIDE a pattern already made, and it is
                    // the only catalog operation that goes through
                    // GetDefinition() + ModifyDefinition().
                    case "suppress_instance":
                        return SuppressInstance(model, arguments, docState);

                    // ── the draft angle ─────────────
                    // It lives in DraftOperations.cs. Four APIs for the same
                    // command, and two with the IDENTICAL signature: the right one was
                    // found by measuring, not by reading.
                    case "draft":
                        return Draft(model, arguments, docState);

                    // ── the rib ────────────────────
                    // It lives in RibOperations.cs (entry W1.2). InsertRib
                    // PARALLEL to the sketch (IsNormToSketch = false, like
                    // the guide's example); the direction of the material is tried
                    // straight and then flipped.
                    case "rib":
                        return Rib(model, arguments);

                    // ── exports ────────────────────
                    // They live in ExportOperations.cs. `export_stl` calls
                    // `check_body_geometry` BEFORE writing, and refuses
                    // if the part is not a single closed solid: an STL carries
                    // nothing with it that says the part was fine.
                    case "export_stl":
                        return ExportStl(model, arguments, docState);
                    // STEP and IGES end up in the same executor: what changes is
                    // the preferences and the way of rereading the file, not the
                    // road. Two executors for the same thing would mean
                    // two behaviors that diverge at the first change.
                    case "export_step":
                    case "export_iges":
                        return ExportExchange(model, arguments, operation, docState);
                    default:
                        // ANY operation that carries contours is drawn
                        // with the same executor. That way a new RECIPE
                        // (a standard section, a gear, a profile of
                        // tomorrow) is added to the server without touching the add-in
                        // and without anyone having to reinstall anything.
                        if (arguments != null && arguments.ContainsKey("contours"))
                            return ExtrudeProfile(model, arguments);
                        return Failed(UnknownOperation + operation);
                }
            }
            catch (Exception ex)
            {
                return Failed($"{operation}: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // The six operations
        // ─────────────────────────────────────────────────────────────────

        private Outcome ExtrudeRectangle(IModelDoc2 model, Dictionary<string, object> a)
        {
            double length = Num(a, "length_mm");
            double width = Num(a, "width_mm");
            double thickness = Num(a, "thickness_mm");
            bool centered = Bool(a, "centered", true);
            string plane = Str(a, "plane", "front");

            // The names of the tree before the sketch: the sketch selected below
            // is the one born since (scheme S7), not the last of the tree.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectPlane(model, plane))
                return Failed($"I did not find the plane «{plane}» in the tree.");

            var sketches = model.SketchManager;
            sketches.InsertSketch(true);

            double x1 = centered ? Units.M(-length / 2) : 0;
            double y1 = centered ? Units.M(-width / 2) : 0;
            double x2 = centered ? Units.M(length / 2) : Units.M(length);
            double y2 = centered ? Units.M(width / 2) : Units.M(width);
            var sides = sketches.CreateCornerRectangle(x1, y1, 0, x2, y2, 0) as object[];

            // THE TWO DIMENSIONS THAT MAKE THE PART EDITABLE.
            //
            // They are placed here, inside the sketch, on the horizontal side and on
            // the vertical one. From this moment «bring the tub to 300»
            // is a dimension edit instead of a part to redo, and the
            // dimensions exist for the drawing too.
            int placedDimensions = DimensionTheRectangle(model, sides, x1, y1, x2, y2);

            sketches.InsertSketch(false);

            // The sketch must be SELECTED: leaving the sketch does not select it,
            // and the feature works on what is selected.
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Extrude(model, thickness, merge: true);
            if (feature == null) return Failed("The extrusion was not created." + WithoutSketch(model));
            return Succeeded(feature, new Dictionary<string, object>
            {
                ["length_mm"] = length,
                ["width_mm"] = width,
                ["thickness_mm"] = thickness,
                ["dimensions_on_sketch"] = placedDimensions,
            }, References.PlaneNormal(plane));
        }

        /// <summary>
        /// Puts a dimension on the horizontal side and one on the vertical side of
        /// a rectangle just drawn. Returns how many it placed.
        ///
        /// HOW TO DIMENSION VIA API. `IModelDocExtension.AddDimension(X, Y, Z,
        /// Direction)` takes no entity: it works on what is
        /// SELECTED, and it is yet another call of this interop made
        /// that way. The point passed is not the entity to dimension — it is WHERE
        /// the number is to be written. So: the side is selected, we say where
        /// to put the text, and the dimension is born.
        ///
        /// IT NEVER THROWS. A part without dimensions is worse than a
        /// dimensioned one, but it is still a right part: if the dimensioning
        /// does not succeed we go on and write how many were placed, which
        /// is a number the reader can check.
        /// </summary>
        private int DimensionTheRectangle(
            IModelDoc2 model, object[] sides,
            double x1, double y1, double x2, double y2)
        {
            if (sides == null || sides.Length == 0) return 0;

            // The text must be placed OUTSIDE the rectangle, otherwise it ends up on top of the
            // drawing. A sixth of the side is enough on a 10 mm part
            // as on a 3 meter one.
            double outsideX = Math.Max(Math.Abs(x2 - x1) * 0.15, 0.005);
            double outsideY = Math.Max(Math.Abs(y2 - y1) * 0.15, 0.005);

            var toDimension = new List<Dimension>();
            bool horizontalDone = false, verticalDone = false;

            foreach (var o in sides)
            {
                var line = o as ISketchLine;
                if (line == null) continue;

                double[] p1, p2;
                if (!EndsOf(line, out p1, out p2)) continue;

                bool horizontal = Math.Abs(p1[1] - p2[1]) < 1e-9;
                bool vertical = Math.Abs(p1[0] - p2[0]) < 1e-9;
                if (horizontal && horizontalDone) continue;
                if (vertical && verticalDone) continue;
                if (!horizontal && !vertical) continue;

                double tx = (p1[0] + p2[0]) / 2;
                double ty = (p1[1] + p2[1]) / 2;
                if (horizontal) ty = Math.Min(y1, y2) - outsideY;
                else tx = Math.Min(x1, x2) - outsideX;

                toDimension.Add(Dimension.Su(o, new[] { tx, ty, 0.0 }));
                if (horizontal) horizontalDone = true; else verticalDone = true;
                if (horizontalDone && verticalDone) break;
            }

            return DimensionSegments(model, toDimension);
        }

        /// <summary>
        /// Puts a dimension on each of the given segments, with the text at the point
        /// indicated (meters, sketch coordinates). Returns how many it placed.
        ///
        /// HOW TO DIMENSION VIA API. `IModelDocExtension.AddDimension(X, Y, Z,
        /// Direction)` takes no entity: it works on what is
        /// SELECTED, like half of this interop. The point passed is not
        /// the entity to dimension — it is WHERE the number is to be written.
        ///
        /// THE VALUE POP-UP IS TURNED OFF, and this is the line that matters.
        /// `AddDimension` places the dimension and then opens «Modify» to ask
        /// what it is worth, and stays there waiting for an answer that will never
        /// come. It does not fail, does not throw, does not time out: it gets stuck. In
        /// production this code runs INSIDE SolidWorks — it would mean
        /// blocking the program of whoever is working in it. Measured on 11
        /// September 2026, ten minutes of SolidWorks stopped.
        ///
        /// The preference is put back as it was in EVERY case: it is a setting
        /// of the person using the program, not ours. Same rule as `AddToDB`.
        ///
        /// IT NEVER THROWS. A part without dimensions is worse than a dimensioned one, but
        /// it is still a right part: if the dimensioning does not succeed we go on
        /// and write how many were placed, which is a number the reader
        /// can check.
        /// </summary>
        /// <summary>
        /// A segment to dimension, and WHERE the number is to be written (meters,
        /// model coordinates).
        ///
        /// The Z is there and it is not an extra. Before, the text point was
        /// `(x, y, 0)`: fine for a sketch on a reference plane,
        /// where Z is zero by construction, and **not** for a sketch on any
        /// face — which is where all the cuts are. With Z
        /// nailed to zero the dimension of a hole on a side is born outside the
        /// sketch plane, and SOLIDWORKS refuses it without explaining.
        /// </summary>
        private class Dimension
        {
            public object Segment;
            public double X, Y, Z;

            public static Dimension Su(object segment, double[] point)
            {
                return new Dimension
                {
                    Segment = segment,
                    X = point[0],
                    Y = point[1],
                    Z = point.Length > 2 ? point[2] : 0,
                };
            }
        }

        private int DimensionSegments(IModelDoc2 model, List<Dimension> toDimension)
        {
            if (model == null || toDimension == null || toDimension.Count == 0) return 0;

            bool askedForTheValue = true;
            try
            {
                askedForTheValue = _sw.GetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }

            // ZERO-E-ZERO: `placed` counts the dimensions BORN, one by one; one that is not
            // born is not a dimension. The catch blocks below close the selection and
            // put the preference back: they do not touch the count.
            int put = 0;
            foreach (var entry in toDimension)
            {
                try
                {
                    model.ClearSelection2(true);
                    var segment = entry.Segment as ISketchSegment;
                    if (segment == null || !segment.Select4(false, null)) continue;
                    var born = model.Extension.AddDimension(entry.X, entry.Y, entry.Z, 0)
                        as IDisplayDimension;
                    if (born == null) continue;

                    // MARKED FOR THE DRAWING, and this is the line that makes the
                    // difference between a dimensioned drawing and a mute one.
                    //
                    // `model_dimensions` imports by default only the dimensions
                    // MARKED: those are the ones a designer chose to
                    // show, and a drawing with all the model's dimensions is
                    // unreadable. A dimension born from `AddDimension` is not
                    // marked, so ours never arrived — measured
                    // on 14/9/2026: part with 11 dimensions, drawing with 0, and
                    // not even asking for «all_dimensions».
                    //
                    // By marking them here, the drawing takes EXACTLY the dimensions that
                    // the operations placed on purpose, and not the
                    // service ones.
                    try { born.MarkedForDrawing = true; } catch { }
                }
                catch { continue; }
                put++;
            }

            try { model.ClearSelection2(true); } catch { }
            try
            {
                _sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate, askedForTheValue);
            }
            catch { }
            return put;
        }

        /// <summary>The endpoints of a sketch line, in meters.</summary>
        private static bool EndsOf(ISketchLine line, out double[] p1, out double[] p2)
        {
            p1 = null; p2 = null;
            try
            {
                var a = line.GetStartPoint2() as ISketchPoint;
                var b = line.GetEndPoint2() as ISketchPoint;
                if (a == null || b == null) return false;
                p1 = new[] { a.X, a.Y, a.Z };
                p2 = new[] { b.X, b.Y, b.Z };
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// <summary>
        /// A cylindrical SPUR GEAR with straight teeth.
        ///
        /// Like the section, it is not another extrusion: it is a way of
        /// writing contours, and it ends up in the same executor.
        ///
        /// The geometry lives in <see cref="SpurGear"/>, which is the most
        /// delicate file of the library: a wrong transliteration of the
        /// involute gives no error, it gives a gear that meshes
        /// badly.
        /// </summary>
        private Outcome SpurGearOp(IModelDoc2 model, Dictionary<string, object> a)
        {
            double rawTeeth, module, thickness;
            string why;
            if (!RequiredNum(a, "teeth", out rawTeeth, out why))
                return Failed("A gear needs «teeth»: " + why);
            if (!RequiredNum(a, "module_mm", out module, out why))
                return Failed("A gear needs «module_mm»: " + why);
            if (!RequiredNum(a, "thickness_mm", out thickness, out why))
                return Failed("A gear needs «thickness_mm»: " + why);

            int teeth = (int)Math.Round(rawTeeth);
            if (Math.Abs(rawTeeth - teeth) > 1e-9)
                return Failed(
                    "The teeth are " + rawTeeth + ": a number of teeth is a " +
                    "whole number, and half a tooth cannot be cut.");
            if (teeth < 3)
                return Failed("A wheel of " + teeth + " teeth is not a wheel.");
            if (module <= 0 || thickness <= 0)
                return Failed("Module and thickness must be greater than zero.");

            string reason;
            var contours = SpurGear.Contours(
                teeth, module,
                Num(a, "pressure_angle_deg", 20.0),
                Num(a, "bore_mm", 0),
                Bool(a, "keyway", false),
                8,
                out reason);
            if (contours == null) return Failed(reason);

            var itsItems = new Dictionary<string, object>
            {
                ["contours"] = contours.ToArray(),
                ["thickness_mm"] = thickness,
            };
            if (a.ContainsKey("plane")) itsItems["plane"] = a["plane"];

            // The wide ceiling: these points were calculated by a formula, not
            // written by a model. A gear with 127 teeth is a thousand and twenty
            // segments, and the two-hundred ceiling would refuse it.
            var outcome = ExtrudeProfile(model, itsItems, MaxGeneratedVertices);

            // THE GEAR'S NUMBERS, NEXT TO THOSE OF THE EXTRUSION.
            // Whoever asked for z=20 m=2 wants to be able to check that the
            // outside diameter is 44: it is the quickest way to notice
            // that something in the math does not add up, and it cannot be read from
            // the volume.
            if (outcome != null && outcome.Succeeded && outcome.Measurements != null)
            {
                outcome.Measurements["teeth"] = teeth;
                outcome.Measurements["module_mm"] = module;
                outcome.Measurements["pitch_diameter_mm"] = Math.Round(module * teeth, 4);
                outcome.Measurements["outside_diameter_mm"] = Math.Round(module * (teeth + 2), 4);
                outcome.Measurements["root_diameter_mm"] =
                    Math.Round(module * teeth - 2.5 * module, 4);
                outcome.Measurements["pitch_mm"] = Math.Round(Math.PI * module, 4);
            }
            return outcome;
        }

        /// <summary>
        /// Extrudes a STANDARD SECTION: two or three dimensions instead of eight
        /// coordinates.
        ///
        /// It is not another extrusion: it is a way of writing contours.
        /// The recipe builds the contours and passes them to the same executor
        /// as `extrude_profile` — two executors for the same thing
        /// would diverge at the first change.
        ///
        /// Before, this recipe lived only in the Python server, so the
        /// operation was **declared in the catalog and not executable**: from the
        /// bridge it answered «unknown operation» to whoever asked for it with its
        /// real arguments.
        /// </summary>
        private Outcome ExtrudeSection(IModelDoc2 model, Dictionary<string, object> a)
        {
            string typ = Str(a, "variant", "");

            string[] wanted;
            if (string.IsNullOrWhiteSpace(typ) ||
                !Sections.Wanted.TryGetValue(typ.Trim(), out wanted))
                return Failed(
                    "Section «" + typ + "» unknown. There are: " +
                    string.Join(", ", Sections.Wanted.Keys) + ".");

            // THE DIMENSIONS THIS SECTION NEEDS, and only those.
            // The catalog schema declares them all together because it is
            // a single operation, but a tube has no diameter and a
            // round bar has no wall: if one is missing say WHICH, not
            // «invalid parameters».
            var m = new Dictionary<string, double>();
            var missingItems = new List<string>();
            foreach (var q in wanted)
            {
                double v;
                string why;
                if (!RequiredNum(a, q, out v, out why)) { missingItems.Add(q); continue; }
                if (v <= 0) return Failed(
                    "«" + q + "» is " + v + ": a dimension of a section cannot " +
                    "be zero or negative.");
                m[q] = v;
            }
            if (missingItems.Count > 0)
                return Failed(
                    "A section «" + typ + "» " +
                    (missingItems.Count == 1 ? "needs " : "need ") +
                    string.Join(", ", missingItems.ConvertAll(x => "«" + x + "»")) + ".");

            string reason;
            var contours = Sections.Contours(typ, m, out reason);
            if (contours == null) return Failed(reason);

            // The arguments are rewritten for the profile executor,
            // leaving out the section's dimensions: from here on the
            // shape is made, and the dimensions are of no use to anyone.
            var itsItems = new Dictionary<string, object>
            {
                ["contours"] = contours.ToArray(),
                ["thickness_mm"] = Num(a, "length_mm", 0),
            };
            if (a.ContainsKey("plane")) itsItems["plane"] = a["plane"];
            if (a.ContainsKey("symmetric")) itsItems["symmetric"] = a["symmetric"];

            return ExtrudeProfile(model, itsItems, MaxGeneratedVertices);
        }

        /// <summary>
        /// Extrudes any shape, given as contours of vertices.
        ///
        /// It is the operation that removes the ceiling from all the rest: with a
        /// closed polyline one makes shaped brackets, angles, gussets, and
        /// — with TWO contours, one outer and one inner — hollow profiles.
        /// The recipes (the gear, tomorrow the tube) are not other
        /// operations: the server expands them into these same contours.
        ///
        /// THE FIRST CONTOUR IS THE EDGE, THE OTHERS ARE THE VOIDS.
        /// SolidWorks extrudes the enclosed region: nested contours give a
        /// drilled part with no need for a second cut. It is also the reason
        /// a tube is made this way instead of with the thin extrusion:
        /// the outer bounding box is written in the coordinates, it does not depend on a
        /// wall direction that can be got wrong silently.
        ///
        /// AddToDB during drawing: without it, SolidWorks tries to infer a
        /// relation for every segment, and on five hundred points of a gear
        /// that is minutes instead of seconds. It is put back as it was in every
        /// case, even if something goes wrong: it is a setting of the environment
        /// of whoever is working, not ours.
        /// </summary>
        /// <summary>
        /// THE MODEL AXIS EACH SKETCH AXIS RUNS ALONG (brief R7): «x» and «y» of
        /// the contours, and «extrusion», the normal of the sketch along which
        /// the extrusion goes. On 24/10/2026 a contour drawn on «top» came out
        /// with its y along -Z of the model, and only a measure afterwards said
        /// so: the description of the plane did not.
        ///
        /// The unit vectors of the sketch are carried into the model by the
        /// inverse of ModelToSketchTransform, through IMathVector, so nothing
        /// depends on the order of the nine numbers of a rotation. Each answer is
        /// the dominant model axis with its sign.
        /// </summary>
        private Dictionary<string, object> SketchAxes(ISketch sketch)
        {
            if (sketch == null) return null;
            var math = _sw.GetMathUtility() as IMathUtility;
            var toSketch = sketch.ModelToSketchTransform;
            if (math == null || toSketch == null) return null;
            var toModel = toSketch.Inverse() as IMathTransform;
            if (toModel == null) return null;
            Func<double[], string> along = v =>
            {
                var vector = math.CreateVector(v) as IMathVector;
                var moved = vector?.MultiplyTransform(toModel) as IMathVector;
                var r = moved?.ArrayData as double[];
                if (r == null || r.Length < 3) return null;
                int k = 0;
                for (int i = 1; i < 3; i++) if (Math.Abs(r[i]) > Math.Abs(r[k])) k = i;
                return (r[k] >= 0 ? "+" : "-") + "XYZ"[k];
            };
            var x = along(new[] { 1.0, 0, 0 });
            var y = along(new[] { 0, 1.0, 0 });
            var normal = along(new[] { 0, 0, 1.0 });
            if (x == null || y == null || normal == null) return null;
            return new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["extrusion"] = normal };
        }

        private Outcome ExtrudeProfile(
            IModelDoc2 model, Dictionary<string, object> a,
            int maxVertices = MaxModelVertices)
        {
            var contours = Contours(a, out string reason, out string noteContours, maxVertices);
            if (contours == null) return Failed(reason);

            double thickness = Num(a, "thickness_mm");
            bool symmetric = Bool(a, "symmetric", false);
            string plane = Str(a, "plane", "front");

            // Before the sketch: it is selected, and locked after the
            // extrusion, as the one sketch born since (scheme S7).
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectPlane(model, plane))
                return Failed($"I did not find the plane «{plane}» in the tree.");

            var sketches = model.SketchManager;
            bool addToDbBefore = false;
            bool displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }

            int segments = 0;
            Dictionary<string, object> axesOfSketch = null;
            try
            {
                sketches.InsertSketch(true);
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }

                foreach (var contour in contours)
                {
                    segments += DrawContour(sketches, contour);
                }

                // WHERE THE CONTOUR'S AXES WENT (brief R7), read from the open
                // sketch and not from a table.
                try { axesOfSketch = SketchAxes(sketches.ActiveSketch as ISketch); } catch { }
            }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
            }
            sketches.InsertSketch(false);

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Extrude(model, thickness, merge: true, symmetric: symmetric);
            if (feature == null) return Failed("The extrusion was not created." + WithoutSketch(model));

            // The sketch is locked AFTER the extrusion: a FIXED relation added
            // while the sketch was still being drawn raised RPC_E_SERVERFAULT
            // in the probe of 28/9/2026 (finding M5, case R4, which passed an
            // object[]: the typed array was measured only after the feature).
            string noteBlock;
            int fixedRelations;
            bool locked = LockSketch(model, sketchNamesBefore, out noteBlock, out fixedRelations);

            var measurements = new Dictionary<string, object>
            {
                ["sketch_locked"] = locked,
                ["fixed_relations"] = fixedRelations,
                ["contours"] = contours.Count,
                ["segments"] = segments,
                ["thickness_mm"] = thickness,
            };
            if (axesOfSketch != null) measurements["sketch_axes"] = axesOfSketch;
            // Up to library 0.141.0 the note was computed and dropped: whoever
            // reads a sketch that is not locked must be able to read why.
            if (noteBlock != null) measurements["sketch_lock_note"] = noteBlock;
            // Contours detached from the first are not refused, they are SAID: a
            // contour outside the outer edge is a region of its own, and is
            // legitimate (four tabs on a face). But it becomes an extra
            // body, and whoever thought they were making a hole must be able to read it.
            if (noteContours != null) measurements["note_contours"] = noteContours;
            return Succeeded(feature, measurements, References.PlaneNormal(plane));
        }

        /// <summary>
        /// Draws a contour, whatever shape it has. Returns how many entities
        /// it created.
        ///
        /// THREE SHAPES, AND EACH EXISTS FOR A REASON
        ///  * circle    — a TRUE circle. Approximating it with 48 inscribed segments
        ///                puts the material inside the circumference: on a hole
        ///                of 20 mm it intrudes by 0,021 mm and the shaft does not fit.
        ///  * composite — different stretches in a row: lines where it is straight,
        ///                SPLINES where it is a curve. The involute flank of a
        ///                tooth is a curve, and with segments it stayed faceted
        ///                with 528 entities in the sketch.
        ///  * polygonal — the closed polyline, which remains the natural shape for
        ///                a bracket or a tube.
        /// </summary>
        private int DrawContour(ISketchManager sketches, Contour contour)
        {
            if (contour.Circle)
            {
                sketches.CreateCircleByRadius(
                    Units.M(contour.Center[0]), Units.M(contour.Center[1]), 0,
                    Units.M(contour.Diameter / 2.0));
                return 1;
            }

            int done = 0;
            foreach (var stroke in contour.Strokes)
            {
                if (stroke.Spline)
                {
                    // TRIPLES x,y,z per point, in metres, in one flat array
                    // (ISketchManager~CreateSpline3.html, Remarks: "PointData
                    // with at least six coordinates ... [start_pt_x, start_pt_y,
                    // start_pt_z, end_pt_x, end_pt_y, end_pt_z]"). PAIRS break it
                    // with no error: probe_spline3 (Z-C21.3) cases S4, S6 and S8,
                    // 27 or 28 splines of 34 and no extrusion.
                    var data = new double[stroke.Points.Count * 3];
                    for (int i = 0; i < stroke.Points.Count; i++)
                    {
                        data[i * 3] = Units.M(stroke.Points[i][0]);
                        data[i * 3 + 1] = Units.M(stroke.Points[i][1]);
                        data[i * 3 + 2] = 0;
                    }
                    if (CreateSplineThroughPoints(sketches, data) != null) done++;
                }
                else
                {
                    for (int i = 0; i < stroke.Points.Count - 1; i++)
                    {
                        var da = stroke.Points[i];
                        var a2 = stroke.Points[i + 1];
                        sketches.CreateLine(
                            Units.M(da[0]), Units.M(da[1]), 0,
                            Units.M(a2[0]), Units.M(a2[1]), 0);
                        done++;
                    }
                }
            }
            return done;
        }

        /// <summary>
        /// SimulateNaturalEnds of the spline call: TRUE, and it is a measure,
        /// not a taste. probe_spline3 (Z-C21.3), case S5,
        /// CreateSpline3(triples, null, null, true), gave the bench gear of the
        /// obsolete CreateSpline exactly: 8808.586 mm3, box 37.787 x 10 x
        /// 37.938 mm, all 34 splines read back within 1e-4 mm. FALSE (S3, and
        /// S7 with AddToDB off) gives 8806.568 mm3, which
        /// the_seventeen_tooth_gear_can_be_made (8808.59 +-0.5) turns red. The
        /// guide: "SimulateNaturalEnds with true for zero curvature end
        /// conditions or false to maintain curvature at the ends".
        /// </summary>
        internal const bool SplineSimulateNaturalEnds = true;

        /// <summary>
        /// ONE 2D SPLINE THROUGH THE POINTS, with CreateSpline3 (L1-1, Z-C21.13).
        ///
        /// Up to library 0.143.0 this was the obsolete CreateSpline
        /// (ISketchManager~CreateSpline.html: "Obsolete. Superseded by
        /// ISketchManager::CreateSpline2", which CreateSpline3 supersedes in
        /// turn). The probe's own verdict line printed "keep CreateSpline
        /// frozen": its decision rule only counted SimulateNaturalEnds false,
        /// while its S5 line (true) reproduced the gear exactly
        /// (<see cref="SplineSimulateNaturalEnds"/>).
        ///
        /// ADDTODB OFF AND DISPLAYWHENADDED ON AROUND THE CALL.
        /// ISketchManager~CreateSpline3.html, Remarks: "Does not work with
        /// ISketchManager::AddToDB or ISketchManager::DisplayWhenAdded", and
        /// both callers draw with AddToDB on and DisplayWhenAdded off
        /// (ExtrudeProfile and the revolved profile). The probe measured the
        /// switch with false (S7 = S3: the same volume, every spline exact), so
        /// on this gear the state does not change the geometry; the pair true +
        /// AddToDB off was not probed on its own, and the gear case is its
        /// judge. The state is read first and put back right after the call,
        /// whatever happens; if it cannot be read it is not touched.
        ///
        /// THE RETURN IS READ: "ISketchSegment or null if an error" (the guide).
        /// The obsolete call's return was never read, and a spline that was not
        /// born was counted all the same (L1-1, second half). The probe: 34
        /// calls, 0 null, every return an ISketchSegment. Status is "empty for
        /// 2D splines" (the guide; the probe read null) and is not used.
        /// </summary>
        private static ISketchSegment CreateSplineThroughPoints(ISketchManager sketches, double[] triples)
        {
            bool addToDbBefore = false, displayBefore = false, stateRead = false;
            try
            {
                addToDbBefore = sketches.AddToDB;
                displayBefore = sketches.DisplayWhenAdded;
                stateRead = true;
            }
            catch { }
            object created = null;
            try
            {
                if (stateRead)
                {
                    try { sketches.AddToDB = false; sketches.DisplayWhenAdded = true; } catch { }
                }
                created = sketches.CreateSpline3(triples, null, null, SplineSimulateNaturalEnds, out _);
            }
            finally
            {
                if (stateRead)
                {
                    try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
                }
            }
            return created as ISketchSegment;
        }

        /// <summary>
        /// LOCKS THE SKETCH WHERE IT IS.
        ///
        /// A generated sketch without relations stays UNDER-DEFINED: a careless
        /// drag moves a vertex and the part changes without anyone knowing. The
        /// customer noticed it looking at the sketch of the first gear.
        ///
        /// A FIXED relation (swConstraintType_FIXED = 17), NOT FullyDefineSketch:
        /// that one does not measure, it adds relations and MOVES the geometry to
        /// satisfy them (two nearly parallel sides become parallel, and the sketch
        /// turns fully defined and a few tenths away from the confirmed one).
        ///
        /// THE SKETCH MUST BE OPEN AND THE ARRAY TYPED (finding M5,
        /// probe_fixed_relation of 28/9/2026: an L profile of six lines extruded
        /// 10 mm, 11000 mm3, the sketch found by JustCreatedSketch after the
        /// extrusion):
        ///   * sketch closed, object[] (this method up to library 0.141.0):
        ///     AddRelation returns null, 0 FIXED relations before and after the
        ///     rebuild, and the sketch was declared locked all the same;
        ///   * EditSketch, object[]: RPC_E_SERVERFAULT, 0 relations;
        ///   * EditSketch, SketchSegment[] (DispatchWrapper[] the same): an
        ///     ISketchRelation back, 6 FIXED relations read back after
        ///     InsertSketch(true) and EditRebuild3, status 2 (under constrained)
        ///     -> 3 (fully constrained), volume 11000 -> 11000, no sketch left
        ///     active.
        /// ISketchRelationManager~AddRelation.html (Remarks): "A sketch must be
        /// active for this method to have any effect." IModelDoc2~EditSketch.html:
        /// "If the selection is a feature, its underlying sketch is placed in edit
        /// mode." SketchSegment[] and not object[] is the rule of the structural
        /// member group too (WeldmentOperations.cs, measured 9/9/2026).
        ///
        /// THE ANSWER IS WHAT IS READ BACK, not what the call returned: the FIXED
        /// relations are counted before the call and again after leaving the
        /// sketch and rebuilding, on the sketch taken again from its feature. The
        /// sketch is locked only when every segment got its relation;
        /// <paramref name="fixedRelations"/> is the count that was added.
        ///
        /// A failure does not fail the operation: the part is right all the same,
        /// it is only editable by mistake. It goes in the result (the note says
        /// why) and the plan goes on.
        ///
        /// WHICH SKETCH (scheme S7, Z-C21.5): the one ProfileFeature born since
        /// <paramref name="namesBeforeSketch"/>, the names the caller took before
        /// opening the sketch, found again in the tree after the feature as the
        /// probe found it. A sketch used by a feature stays in the top-level
        /// walk of IFirstFeature (measured 14/9/2026, see WithoutSketch), and
        /// the feature itself is no ProfileFeature, so it is still the one born.
        /// Until library 0.146.0 it was the last sketch of the tree.
        /// </summary>
        private bool LockSketch(IModelDoc2 model, HashSet<string> namesBeforeSketch, out string note, out int fixedRelations)
        {
            note = null;
            fixedRelations = 0;
            int sketchesBorn;
            IFeature sketchFeature = JustCreatedSketch(model, namesBeforeSketch, out sketchesBorn);
            var sketch = sketchFeature?.GetSpecificFeature2() as ISketch;
            if (sketch == null)
            {
                note = sketchesBorn > 1
                    ? sketchesBorn + " sketches born since the sketch was opened: which one to lock is not known"
                    : "sketch not found after the feature";
                return false;
            }
            int before = FixedRelationCount(sketch);
            if (before < 0) { note = "the relations of the sketch cannot be read"; return false; }

            int segmentCount = 0;
            bool inEdit = false;
            try
            {
                model.ClearSelection2(true);
                if (!sketchFeature.Select2(false, 0)) note = "the sketch could not be selected";
                else
                {
                    model.EditSketch();
                    inEdit = true;
                    if (model.SketchManager.ActiveSketch == null) note = "EditSketch did not open the sketch";
                    else
                    {
                        // Taken again from the feature once in edit mode, as the probe did.
                        sketch = sketchFeature.GetSpecificFeature2() as ISketch;
                        var segments = sketch?.GetSketchSegments() as object[];
                        if (segments == null || segments.Length == 0) note = "the sketch has no segments";
                        else
                        {
                            segmentCount = segments.Length;
                            var typedSegments = new SketchSegment[segments.Length];
                            for (int i = 0; i < segments.Length; i++) typedSegments[i] = (SketchSegment)segments[i];
                            var relations = sketch.RelationManager;
                            if (relations == null) note = "the sketch has no relation manager";
                            else relations.AddRelation(typedSegments, (int)swConstraintType_e.swConstraintType_FIXED);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                note = "AddRelation FIXED failed: " + ex.Message;
            }
            finally
            {
                // Leave the sketch only if one is open: InsertSketch(true) with
                // no active sketch and a feature selected would OPEN one.
                bool stillOpen = false;
                try { stillOpen = model.SketchManager.ActiveSketch != null; } catch { }
                if (stillOpen) { try { model.SketchManager.InsertSketch(true); } catch { } }
                if (inEdit || stillOpen) { try { model.EditRebuild3(); } catch { } }
                try { model.ClearSelection2(true); } catch { }
            }

            int after = FixedRelationCount(sketchFeature.GetSpecificFeature2() as ISketch);
            if (after < 0)
            {
                note = (note == null ? "" : note + "; ") + "the relations cannot be read back after the rebuild";
                return false;
            }
            fixedRelations = after - before;
            if (note != null) return false;
            if (segmentCount == 0 || fixedRelations < segmentCount)
            {
                note = fixedRelations + " FIXED relation(s) read back after the rebuild for " + segmentCount +
                       " segment(s): the sketch is not locked";
                return false;
            }
            return true;
        }

        /// <summary>
        /// The FIXED relations (swConstraintType_FIXED = 17) of a sketch, read
        /// with GetRelations(swAll); -1 when they cannot be read.
        /// ISketchRelationManager~GetRelations.html (Remarks): "Because some of
        /// the objects may be NULL, you should check for this before accessing
        /// them."
        /// </summary>
        private static int FixedRelationCount(ISketch sketch)
        {
            if (sketch == null) return -1;
            try
            {
                var manager = sketch.RelationManager;
                if (manager == null) return -1;
                var relations = manager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[];
                if (relations == null) return 0;
                int count = 0;
                foreach (var item in relations)
                {
                    var relation = item as ISketchRelation;
                    if (relation != null && relation.GetRelationType() == (int)swConstraintType_e.swConstraintType_FIXED) count++;
                }
                return count;
            }
            catch { return -1; }
        }

        /// <summary>A contour read from the plan: circle, composite or polyline.</summary>
        /// <summary>
        /// How many contours at most in a single shape.
        ///
        /// The limits here are on COUNT, not on dimension: how many times a line
        /// can be broken, not how long it can be. A three-meter plate
        /// is legitimate; a five-thousand-vertex shape written by a
        /// model almost never.
        /// </summary>
        private const int MaxContours = 20;

        /// <summary>Maximum vertices per contour, when a model writes them.</summary>
        private const int MaxModelVertices = 200;

        /// <summary>
        /// Maximum vertices per contour, when a recipe generates them.
        ///
        /// The contours coming out of <see cref="SpurGear"/> or <see cref="Sections"/>
        /// do not pass through anyone's head and can be
        /// much denser: a gear with 127 teeth is a thousand and twenty
        /// segments, and that is right.
        /// </summary>
        private const int MaxGeneratedVertices = 4000;

        private class Contour
        {
            public bool Circle;
            public double[] Center = new double[2];
            public double Diameter;
            public List<Stroke> Strokes = new List<Stroke>();
        }

        private class Stroke
        {
            public bool Spline;
            public List<double[]> Points = new List<double[]>();
        }

        /// <summary>
        /// The contours from the plan. Null with the reason if the data is not what it
        /// must be: here we fail instead of guessing, because
        /// a vertex read wrong gives no error — it gives a different shape.
        /// </summary>
        private static List<Contour> Contours(
            Dictionary<string, object> a, out string reason, out string note,
            int maxVertices = MaxModelVertices)
        {
            reason = null;
            note = null;
            object raw;
            if (a == null || !a.TryGetValue("contours", out raw) || raw == null)
            {
                reason = "No outline to draw arrived.";
                return null;
            }
            var list = raw as System.Collections.IEnumerable;
            if (list == null) { reason = "The outline is not a list of contours."; return null; }

            var outside = new List<Contour>();
            foreach (var c in list)
            {
                var entry = c as Dictionary<string, object>;
                if (entry == null)
                {
                    // Old form: a contour was a bare list of points.
                    // It is still accepted, so a plan proposed before
                    // this version does not become unreadable halfway.
                    var points = Points(c, out reason);
                    if (points == null) return null;
                    var old = new Contour();
                    old.Strokes.Add(new Stroke { Points = Close(points) });
                    outside.Add(old);
                    continue;
                }

                // TWO WAYS OF WRITING A CIRCLE, AND THE CATALOG
                // RECOMMENDS ONE THAT THE PARSER DID NOT UNDERSTAND.
                //
                // The description of `extrude_profile` carries this example
                // in full — «TUBE outside diameter 60 thickness 5»:
                //
                //   [{"circle": [0,0], "diameter_mm": 60},
                //    {"circle": [0,0], "diameter_mm": 50}]
                //
                // The parser understood only the explicit form with `variant` and
                // `center`, so a model that copied the catalog's example
                // ended up in the polygonal branch, looked for `vertices`,
                // did not find it and failed. A divergence between what is
                // declared and what can be done, inside the file that exists
                // to have none — and precisely on the form the catalog
                // recommends for holes and round bars, where a mistake costs a part
                // the shaft does not fit in.
                bool circleKey = entry.ContainsKey("circle");
                var typ = Convert.ToString(
                    entry.TryGetValue("kind", out var t) ? t
                    : circleKey ? "circle" : "polygonal");
                if (string.Equals(typ, "circle", StringComparison.OrdinalIgnoreCase))
                {
                    object rawCenter = null;
                    if (entry.TryGetValue("center", out var there) && there != null) rawCenter = there;
                    else if (circleKey) rawCenter = entry["circle"];
                    var center = Points(
                        rawCenter != null ? new[] { rawCenter } : null, out reason);
                    if (center == null || center.Count != 1)
                    {
                        reason = "The center of the circle is not [x, y]. Write \"circle\": [x, y] or \"variant\": \"circle\" with \"center\": [x, y].";
                        return null;
                    }

                    // MISSING AND NON-NUMERIC ARE TWO DIFFERENT THINGS.
                    // Before, both ended up in «is not a number»,
                    // because the absent key threw inside the same
                    // try. Whoever wrote just {"circle":[0,0]} must
                    // be sent to put in the diameter, not to check a
                    // number they did not write.
                    object rawDiameter;
                    if (!entry.TryGetValue("diameter_mm", out rawDiameter) || rawDiameter == null)
                    {
                        reason = "A circle without \"diameter_mm\": a center alone does not say how big it is.";
                        return null;
                    }
                    double diameter;
                    try
                    {
                        diameter = Convert.ToDouble(
                            rawDiameter, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch { reason = "The diameter of the circle is not a number."; return null; }
                    if (diameter <= 0)
                    {
                        reason = "The diameter of the circle is " + diameter + ": a circle of zero or negative diameter cannot be drawn.";
                        return null;
                    }

                    outside.Add(new Contour { Circle = true, Center = center[0], Diameter = diameter });
                    continue;
                }

                if (string.Equals(typ, "composite", StringComparison.OrdinalIgnoreCase))
                {
                    var contour = new Contour();
                    var strokes = entry.TryGetValue("strokes", out var tr) ? tr as System.Collections.IEnumerable : null;
                    if (strokes == null) { reason = "A composite contour has no segments."; return null; }
                    foreach (var x in strokes)
                    {
                        var tt = x as Dictionary<string, object>;
                        if (tt == null) { reason = "A segment is not readable."; return null; }
                        var points = Points(tt.TryGetValue("points", out var pp) ? pp : null, out reason);
                        if (points == null) return null;
                        if (points.Count < 2) { reason = "A segment has fewer than two points."; return null; }
                        var itemName = Convert.ToString(tt.TryGetValue("stroke", out var nt) ? nt : "lines");
                        contour.Strokes.Add(new Stroke
                        {
                            Spline = string.Equals(itemName, "spline", StringComparison.OrdinalIgnoreCase),
                            Points = points,
                        });
                    }
                    if (contour.Strokes.Count == 0) { reason = "A composite contour is empty."; return null; }
                    outside.Add(contour);
                    continue;
                }

                var vertices = Points(entry.TryGetValue("vertices", out var vv) ? vv : null, out reason);
                if (vertices == null) return null;
                if (vertices.Count < 3) { reason = "A contour has fewer than three points: it does not close."; return null; }
                var polygonal = new Contour();
                polygonal.Strokes.Add(new Stroke { Points = Close(vertices) });
                outside.Add(polygonal);
            }
            if (outside.Count == 0) { reason = "The outline has no contour."; return null; }
            if (outside.Count > MaxContours)
            {
                reason = outside.Count + " contours are too many (at most " + MaxContours + ").";
                return null;
            }
            if (!SensibleOutline(outside, maxVertices, out reason, out note)) return null;
            return outside;
        }

        /// <summary>
        /// THE POINTS OF A CONTOUR, for the geometric checks only.
        ///
        /// A circle is sampled at 64 sides: it is only needed to know whether it is inside
        /// the outer edge, not to draw it. Drawn, it stays a true circle.
        /// </summary>
        private static List<double[]> PointsOf(Contour c)
        {
            if (c.Circle)
            {
                const int sides = 64;
                double r = c.Diameter / 2.0;
                var outside = new List<double[]>(sides);
                for (int i = 0; i < sides; i++)
                {
                    double t = 2 * Math.PI * i / sides;
                    outside.Add(new[] { c.Center[0] + r * Math.Cos(t), c.Center[1] + r * Math.Sin(t) });
                }
                return outside;
            }
            var all = new List<double[]>();
            foreach (var stroke in c.Strokes) all.AddRange(stroke.Points);
            return ClosedShape.Ring(all);
        }

        /// <summary>
        /// THE WAYS IN WHICH A WRONG SHAPE GIVES NO ERROR.
        ///
        /// None of these checks protects against a crash: they protect against a
        /// part that builds perfectly well and is not the one asked for. It is the
        /// reason they live here and not in the schema — the schema looks at the
        /// SHAPE of the data, this looks at the geometry that comes out of it.
        ///
        ///   1. a composite contour whose stretches do not join: it gives no
        ///      error, it simply does not extrude;
        ///   2. a contour that crosses itself: SOLIDWORKS builds the
        ///      regions it can and gives a plausible and wrong part;
        ///   3. a contour of zero area;
        ///   4. **a void that leaves the outer edge**, which instead of drilling
        ///      the part CUTS it. The solid that comes out is valid, and nobody
        ///      says anything.
        ///
        /// The fourth is the most insidious of the four, because the wrong part
        /// looks as if it came out fine.
        /// </summary>
        private static bool SensibleOutline(
            List<Contour> contours, int maxVertices, out string reason, out string note)
        {
            reason = null;
            note = null;
            for (int i = 0; i < contours.Count; i++)
            {
                var entry = contours[i];
                string label = i == 0 ? "The outer contour" : "Void no. " + i;

                // A circle cannot cross itself and does not have zero
                // area: sampling it to find out would be wasted work.
                if (entry.Circle) continue;

                // 1. DO THE STRETCHES JOIN?
                // The tolerance is a thousandth of a millimeter and not a
                // micron: the points of the generated contours come out rounded to the
                // ten-thousandth, and demanding more would refuse correct
                // geometry.
                for (int j = 0; j < entry.Strokes.Count; j++)
                {
                    var fine = entry.Strokes[j].Points[entry.Strokes[j].Points.Count - 1];
                    var after = entry.Strokes[(j + 1) % entry.Strokes.Count].Points[0];
                    if (!ClosedShape.Neighbors(fine, after, 1e-3))
                    {
                        reason = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}: segment {1} ends at ({2:0.###}, {3:0.###}) and the " +
                            "next one starts at ({4:0.###}, {5:0.###}): the contour does " +
                            "not close, and an open contour raises no error — it just " +
                            "does not extrude.",
                            label, j + 1, fine[0], fine[1], after[0], after[1]);
                        return false;
                    }
                }

                var ring = PointsOf(entry);
                if (ring.Count > maxVertices)
                {
                    reason = label + " has " + ring.Count + " vertices (at most " +
                             maxVertices + ").";
                    return false;
                }
                if (ring.Count < 3)
                {
                    reason = label + " has " + ring.Count + " distinct vertices: with " +
                             "fewer than three an outline cannot be closed.";
                    return false;
                }

                // 2. DOES IT CROSS ITSELF?
                int location = ClosedShape.WhereItCrosses(ring);
                if (location >= 0)
                {
                    var p = ring[location];
                    // MEASURED, and it is worth saying as it is: on a bow-tie
                    // SOLIDWORKS stops, but the only thing it says is
                    // «The extrusion was not created» — which does not help to
                    // understand where the trouble is. This message exists to
                    // give the point, not to add a refusal.
                    reason = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0} intersects itself: the side that starts from " +
                        "({1:0.###}, {2:0.###}) crosses another one. SOLIDWORKS would " +
                        "stop anyway, but it would only say «The extrusion was not created». " +
                        "Usually it is the order of the vertices.",
                        label, p[0], p[1]);
                    return false;
                }

                // 3. ZERO AREA?
                if (ClosedShape.Area(ring) <= 0)
                {
                    reason = label + " has zero area: its points are all on the " +
                             "same straight line.";
                    return false;
                }
            }

            // 4. THE CONTOURS AFTER THE FIRST: INSIDE, STRADDLING, OR OUTSIDE?
            //
            // THREE CASES, AND ONLY ONE IS AN ERROR. The first draft refused
            // two of them, and so refused legitimate work:
            //
            //   inside     is a void. Normal.
            //   straddling cuts the outer edge. Always incoherent: it is
            //              neither a void nor a region, and SOLIDWORKS stops
            //              saying only «The extrusion was not created».
            //   outside    is a REGION OF ITS OWN. It is legitimate: four tabs
            //              on a face are four detached contours.
            //
            // Measured on 14/9/2026: the plan of a cover with four
            // tabs was refused by this check, and it was a
            // right plan. **Refusing what is
            // correct is the worse of the two ways of being wrong** — it is the
            // lesson of the shell, and this check had forgotten it.
            //
            // The third case is SAID, loudly: a detached contour becomes an extra
            // body, and whoever thought they were making a hole finds a disc
            // (measured: 100x100 plate with a Ø30 circle at 200 mm, 2 bodies,
            // «succeeded»). But it is said instead of prevented, and the reader has
            // `bodies_total` at hand to check.
            if (contours.Count > 1)
            {
                var outer = PointsOf(contours[0]);
                var detached = new List<int>();
                for (int i = 1; i < contours.Count; i++)
                {
                    // THREE BOXES, NOT TWO. A vertex EXACTLY on the outer
                    // edge is neither inside nor outside, and forcing it to
                    // be one of the two made the verdict depend on the SIDE:
                    // measured on a 100x100 plate, a slot flush with the
                    // right edge was refused as «straddling» and the
                    // same slot mirrored to the left passed.
                    //
                    // THE TWO SIDES MUST GIVE THE SAME ANSWER, AND THE ANSWER
                    // IS NO. Measured: slot on the right, slot on the left and a diamond
                    // touching at a single vertex all three fail with
                    // «The extrusion was not created». Two contours that
                    // touch do not delimit a region; an open slot is
                    // drawn as ONE contour only, with eight points.
                    // The gain is not letting more through: it is answering
                    // the same from both sides, and saying it better than
                    // SOLIDWORKS says it.
                    int inside = 0, outside = 0, onBorder = 0;
                    double[] firstOutside = null;
                    foreach (var point in PointsOf(contours[i]))
                    {
                        switch (ClosedShape.WhereItIs(point, outer))
                        {
                            case WhereItIsFound.Inside: inside++; break;
                            case WhereItIsFound.OnBorder: onBorder++; break;
                            default: outside++; if (firstOutside == null) firstOutside = point; break;
                        }
                    }

                    if (inside > 0 && outside > 0)
                    {
                        reason = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "Contour no. {0} STRADDLES the outer border: the point " +
                            "({1:0.###}, {2:0.###}) is outside and others are inside. " +
                            "It is neither a void nor a region on its own, and SOLIDWORKS " +
                            "stops saying only «The extrusion was not created». " +
                            "Move it all inside if it is a void, all outside if it is " +
                            "a separate piece.",
                            i, firstOutside[0], firstOutside[1]);
                        return false;
                    }
                    // TOUCHES THE EDGE: it is not extruded, and we say why.
                    // It is not «straddling» — there is nothing outside — and it is a
                    // case of its own that deserves its message: whoever wrote it
                    // wanted an open slot, and the way to get one exists.
                    if (onBorder > 0)
                    {
                        reason = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "Contour no. {0} TOUCHES the outer border in {1} " +
                            (onBorder == 1 ? "point" : "points") + ". Two contours that " +
                            "touch do not delimit a region, and SOLIDWORKS stops saying " +
                            "only «The extrusion was not created». If you wanted " +
                            "a slot open on the border, draw it as ONE contour only: " +
                            "the outer border that goes in and comes back out. If you " +
                            "wanted a hole, move it inside by at least a tenth.",
                            i, onBorder);
                        return false;
                    }

                    // Detached means it does not even touch.
                    if (inside == 0) detached.Add(i);
                }

                if (detached.Count > 0)
                    note =
                        (detached.Count == 1
                            ? "Contour no. " + detached[0] + " is not inside the first one"
                            : "Contours no. " + string.Join(", ", detached) +
                              " are not inside the first one") +
                        ": not voids but regions of their own. They are extruded all the same, " +
                        "and the part may have more than one body — look at «bodies_total». " +
                        "If you wanted holes, they must be moved inside the outer contour.";
            }
            return true;
        }

        /// <summary>The closed polyline: the first point is repeated at the end.</summary>
        private static List<double[]> Close(List<double[]> points)
        {
            var outside = new List<double[]>(points);
            outside.Add(points[0]);
            return outside;
        }

        /// <summary>
        /// A list of [x, y] points from JSON data. Null with the reason if
        /// something is not a number: a vertex read wrong gives no error, it gives
        /// a different shape.
        /// </summary>
        private static List<double[]> Points(object raw, out string reason)
        {
            reason = null;
            if (raw == null) { reason = "The points are missing."; return null; }
            var list = raw as System.Collections.IEnumerable;
            if (list == null) { reason = "The points are not a list."; return null; }
            var points = new List<double[]>();
            foreach (var p in list)
            {
                var pair = p as System.Collections.IEnumerable;
                if (pair == null) { reason = "A point is not a pair [x, y]."; return null; }
                var values = new List<double>();
                foreach (var v in pair)
                {
                    try
                    {
                        values.Add(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture));
                    }
                    catch { reason = "A point contains a value that is not a number: " + v; return null; }
                }
                if (values.Count != 2) { reason = "A point does not have exactly two coordinates."; return null; }
                if (double.IsNaN(values[0]) || double.IsNaN(values[1])
                    || double.IsInfinity(values[0]) || double.IsInfinity(values[1]))
                { reason = "A point has coordinates that cannot be used."; return null; }
                points.Add(new[] { values[0], values[1] });
            }
            if (points.Count == 0) { reason = "A list of points is empty."; return null; }
            return points;
        }

        private Outcome ExtrudeCircle(IModelDoc2 model, Dictionary<string, object> a)
        {
            double diameter = Num(a, "diameter_mm");
            double height = Num(a, "height_mm");
            string plane = Str(a, "plane", "front");

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectPlane(model, plane))
                return Failed($"I did not find the plane «{plane}» in the tree.");

            var sketches = model.SketchManager;
            sketches.InsertSketch(true);
            var circle = sketches.CreateCircleByRadius(0, 0, 0, Units.M(diameter / 2));

            // THE DIAMETER, on the sketch. A round part without the diameter dimension is as
            // editable as a tub without its sides: that is,
            // not at all. The text is placed outside the circle, diagonally,
            // otherwise it ends up on top of the drawing.
            double far = Units.M(diameter / 2) * 1.4 + 0.005;
            int placedDimensions = DimensionSegments(model,
                new List<Dimension> { Dimension.Su(circle, new[] { far, far, 0.0 }) });

            sketches.InsertSketch(false);

            // The sketch must be SELECTED: leaving the sketch does not select it,
            // and the feature works on what is selected.
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Extrude(model, height, merge: true);
            if (feature == null) return Failed("The extrusion was not created." + WithoutSketch(model));
            return Succeeded(feature, new Dictionary<string, object>
            {
                ["diameter_mm"] = diameter,
                ["height_mm"] = height,
                ["dimensions_on_sketch"] = placedDimensions,
            }, References.PlaneNormal(plane));
        }

        // ─────────────────────────────────────────────────────────────────
        // TWO DIFFERENT DEFECTS CANNOT GIVE THE SAME SENTENCE
        //
        // Measured on 11 September 2026 (RAG 80u, cases 4 and 7): a 500
        // mm hole on a 314x274 face, and a hole asked for at x = 5000 on a part
        // that reaches 157, both ended up with
        //
        //     «The cut was not created. The sketch is left in the part»
        //
        // which is not a diagnosis: it is the observation that SolidWorks said
        // no. The reader does not know whether to shrink the hole, move it, or
        // change face — and the two things to do are opposite.
        //
        // The two causes can however be told apart BEFORE drawing, with numbers already
        // in hand: the part's bounding box and the profile asked for. The model to
        // imitate is the fillet, which in the same round did not say «it did not
        // succeed» but «200 mm is too much for the edge «E1», 314 mm long: it
        // does not even fit half» — that is the CRITERION, not the verdict: the reader
        // knows how to choose R150 without having to guess it.
        //
        // WHY THE CHECKS ARE THESE TWO AND NOT TWO TIGHTER ONES.
        // In the same round the 30 shell on a box 52 high looked like
        // the most obvious impossible case and was only ugly: a check written
        // on appearance would have refused a legitimate part, that is, would have
        // turned an oversight into a rule of the product. So here
        // we refuse ONLY what cannot produce any part:
        //
        //   1. the profile falls entirely outside the bounding box — below there is no
        //      material, nothing is removed;
        //   2. the profile COVERS the whole bounding box and the cut goes from side to
        //      side — nothing is left.
        //
        // A hole that overhangs the face, a hole centered on the edge, a
        // pocket that eats half the part pass through here and are judged by SolidWorks
        // as before: they are unusual, not impossible.
        //
        // And the check sits BEFORE the sketch on purpose. The operations that
        // check themselves must draw in order to measure, and when they
        // reject, the sketch is already there: it is up to `WithoutSketch` to go and
        // remove it, and if it cannot, to say so (`LeftSketchBehind`). These two
        // do not: there is nothing to measure that is not already known, so the
        // part stays as it was and there is nothing to remove — the cheapest refusal
        // is the one that has drawn nothing.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>The name of the model's axis, so it can be told to the reader.</summary>
        private static string AxisName(int index)
        {
            return index == 0 ? "X" : index == 1 ? "Y" : "Z";
        }

        /// <summary>
        /// The bounding box of the WHOLE part in mm, in model coordinates:
        /// {minX, minY, minZ, maxX, maxY, maxZ}. Null if there is no body.
        ///
        /// GetBodyBox, not the six GetExtremePoint calls that the state uses. The
        /// approximate box can be LARGER than the real part, never
        /// smaller — and here that is exactly what is needed: every error of it makes the two
        /// checks more PERMISSIVE. A profile that passes here and should not
        /// is rejected by SolidWorks an instant later; a profile rejected by
        /// mistake is instead never seen again by anyone.
        /// </summary>
        private static double[] PartBoxMm(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            var bodies = partDoc?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) return null;
            // Approximate ON PURPOSE, see the summary: a box that can only
            // be larger than the part makes both checks more permissive.
            var outside = ApiHelpers.UnionOfBoxes(bodies, c => ApiHelpers.ApproximateBoundingBox(c as IBody2));
            if (outside == null) return null;
            for (int i = 0; i < 6; i++) outside[i] = Units.Mm(outside[i]);
            return outside;
        }

        /// <summary>
        /// Does the cut come out the other side of the part?
        ///
        /// It serves the second check and only that one: a cut that does NOT go through
        /// can cover the whole face with no problem — it shortens the
        /// part, and shortening a part is something that is done. A blind cut comes out
        /// anyway when the depth exceeds the thickness measured along the
        /// normal of the face.
        /// </summary>
        private static bool ThroughAll(
            IModelDoc2 model, double[] axes, bool throughAll, double depthMm)
        {
            if (throughAll) return true;
            if (depthMm <= 0 || axes == null) return false;
            double thickness = ExtentAlongAxis(model, (int)axes[2]);
            return thickness > 0 && depthMm >= thickness;
        }

        /// <summary>
        /// The two checks, for all three operations that cut a
        /// shape on a face. Returns null when the cut can be tried,
        /// otherwise the reason, written so that the reader knows which
        /// number to change.
        /// </summary>
        /// <param name="whatItIs">
        /// The shape with its dimensions, already written: «The 500 mm hole».
        /// </param>
        /// <param name="semiU">Half the bounding-box size of the profile on the first axis.</param>
        /// <param name="semiV">Half the bounding-box size of the profile on the second axis.</param>
        /// <param name="insideTheProfile">Is this point of the face inside the shape?</param>
        /// <param name="advice">
        /// Given the two distances from the center to the nearest edge of the part, the
        /// sentence that says what would fit. Each operation writes it, because
        /// «it is 274 mm» and «it is 314 x 274 mm» are not the same sentence.
        /// </param>
        private static string CutWithoutHope(
            IModelDoc2 model,
            double[] axes,
            string whatItIs,
            double xMm,
            double yMm,
            double semiU,
            double semiV,
            Func<double, double, bool> insideTheProfile,
            bool throughAll,
            Func<double, double, string> advice)
        {
            if (axes == null) return null;
            var box = PartBoxMm(model);
            if (box == null) return null;   // no body: there is nothing to compare

            int iu = (int)axes[0], iv = (int)axes[1];
            double uMin = box[iu], uMax = box[iu + 3];
            double vMin = box[iv], vMax = box[iv + 3];
            string nameU = AxisName(iu), nameV = AxisName(iv);

            // ── 1. THE PROFILE FALLS ENTIRELY OUTSIDE THE PART ──────────────────
            // The two BOXES are compared: if not even those touch,
            // the two shapes surely do not touch. The opposite does not hold — two
            // boxes that touch say nothing about the shapes — and that is fine:
            // SolidWorks resolves the doubt, not this check.
            if (xMm + semiU < uMin || xMm - semiU > uMax ||
                yMm + semiV < vMin || yMm - semiV > vMax)
            {
                return string.Format(
                    "{0} is centered at {1} = {2:0.##}, {3} = {4:0.##} mm, but the part goes on " +
                    "{1} from {5:0.##} to {6:0.##} and on {3} from {7:0.##} to {8:0.##}: the profile " +
                    "falls entirely outside the material, so there is nothing to remove.",
                    whatItIs, nameU, xMm, nameV, yMm, uMin, uMax, vMin, vMax);
            }

            // ── 2. THE PROFILE COVERS THE WHOLE PART, AND THE CUT GOES THROUGH ─────
            // The four corners are enough: circle, rectangle and stadium are all
            // three CONVEX, and a convex shape that contains the four
            // corners contains the whole box.
            if (throughAll &&
                insideTheProfile(uMin, vMin) && insideTheProfile(uMin, vMax) &&
                insideTheProfile(uMax, vMin) && insideTheProfile(uMax, vMax))
            {
                double du = Math.Min(xMm - uMin, uMax - xMm);
                double dv = Math.Min(yMm - vMin, vMax - yMm);
                // The advice makes sense only if the center is INSIDE the part:
                // from a center outside, «the largest that fits» would be a
                // negative number passed off as a measurement.
                string straight = du > 0 && dv > 0 ? " " + advice(du, dv) : "";
                return string.Format(
                    "{0} centered at {1} = {2:0.##}, {3} = {4:0.##} mm covers the whole part, " +
                    "which here occupies {5:0.##} x {6:0.##} mm: a cut right through like that " +
                    "would leave nothing.{7}",
                    whatItIs, nameU, xMm, nameV, yMm, uMax - uMin, vMax - vMin, straight);
            }

            return null;
        }

        /// <summary>
        /// Is the point (u, v) inside the stadium of the slot? It is the distance from the
        /// SEGMENT between the two end centers: less than the radius means
        /// inside, and it holds both in the straight part and in the two
        /// semicircles. The same stroke/radius/cos/sin that draw the shape.
        /// </summary>
        private static bool InsideStadium(
            double u, double v,
            double centerU, double centerV,
            double cos, double sin, double travel, double radius)
        {
            double du = u - centerU, dv = v - centerV;
            double t = du * cos + dv * sin;
            if (t > travel) t = travel;
            else if (t < -travel) t = -travel;
            double pu = centerU + t * cos, pv = centerV + t * sin;
            return (u - pu) * (u - pu) + (v - pv) * (v - pv) <= radius * radius;
        }

        // ─────────────────────────────────────────────────────────────────
        // WHEN IT IS NEITHER THE MEASURE NOR THE POSITION: WE MEASURE ANYWAY,
        // BUT TO EXPLAIN
        //
        // Measured on 14 September 2026, right after 0.43.0. A block
        // 314x274x52, a 270 through hole, then a 20 hole centered INSIDE
        // that gap. The two checks of 0.42.0 let it pass — and they are
        // right to, the profile is inside the bounding box and does not cover it —
        // SolidWorks does not create the feature, and 0.43.0 takes the sketch away
        // as it should. What is left in the reader's hands is:
        //
        //     «The cut was not created.»
        //
        // Four words. It is again the disease of 80u — an observation
        // instead of a diagnosis — in fact worse than before, because before it at least
        // mentioned the sketch left behind.
        //
        // WHY A REFUSAL CANNOT BE ADDED HERE. The temptation is
        // obvious: «if there is no material under the center, refuse». But widening
        // a hole from 20 to 30 has its center EXACTLY in the void and is
        // an operation done every day. It would be the trap of the
        // 80u shell taken for the third time.
        //
        // So we measure to EXPLAIN. The difference is not a nuance:
        // a wrong refusal blocks legitimate work and nobody sees it again,
        // a wrong explanation is corrected by the reader in three seconds
        // because the part is in front of them. The cost of the error is not the same,
        // nor is the right to be wrong.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Does this point, given in the face's coordinates (the same x/y that
        /// arrive from the plan), REALLY fall on the face?
        ///
        /// SIGNATURE VERIFIED by reflection on interop 29.5.1.1:
        ///   IFace2.GetClosestPointOn(Double X, Double Y, Double Z) -> Object
        ///
        /// AND THE DIFFERENCE THAT MATTERS IS WITH `ISurface.GetClosestPointOn`, which
        /// exists with the very same signature one level below. The
        /// SURFACE knows nothing about the contours: of a drilled plane it knows the
        /// plane, not the hole. The FACE has the contours. Asked of a drilled
        /// face what its closest point is to a point lying in the
        /// hole, it answers a point on the hole's EDGE — that is, different from the one
        /// we gave it; if the point lies on the solid, it answers the point
        /// itself. It is an «inside or outside» asked of SolidWorks instead of
        /// deduced by us, and it is the same posture as `IGetChildCount` in
        /// <see cref="WithoutSketch"/>.
        ///
        /// No open sketch is needed: here we stay in the MODEL's
        /// system and do not go through `PointInSketch`.
        /// </summary>
        private static bool PointOnFace(
            IFace2 face, double[] axes, double offsetM, double xMm, double yMm)
        {
            if (face == null || axes == null) return false;
            try
            {
                var p = new double[3];
                p[(int)axes[0]] = Units.M(xMm);
                p[(int)axes[1]] = Units.M(yMm);
                p[(int)axes[2]] = offsetM;
                var near = face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                if (near == null || near.Length < 3) return false;
                double dx = near[0] - p[0], dy = near[1] - p[1], dz = near[2] - p[2];
                // One micron, squared. It is not a machining tolerance:
                // it is the margin for floating-point arithmetic, because for
                // a point lying on the face the answer is the point itself,
                // not an approximation.
                return dx * dx + dy * dy + dz * dz <= 1e-12;
            }
            catch { return false; }
        }

        /// <summary>
        /// What is under the profile, told by counting. Returns the sentence to
        /// add to the reason for the failure, empty if nothing could be
        /// measured.
        ///
        /// DOES NOT REFUSE ANYTHING AND MUST NEVER DO SO: it is called only when
        /// SolidWorks has already said no, and it serves to tell the reader which
        /// of the roads is still open.
        /// </summary>
        private static string BelowProfile(
            IFace2 face, string uidFace, double[] axes, double offsetM,
            List<double[]> samples)
        {
            if (face == null || axes == null || samples == null || samples.Count == 0)
                return "";

            int onCount = 0;
            foreach (var c in samples)
                if (PointOnFace(face, axes, offsetM, c[0], c[1])) onCount++;

            // «on the face» with one preposition for all three sentences: the preposition is chosen
            // in here, because all three sentences use it.
            string location = string.IsNullOrEmpty(uidFace)
                ? "on the chosen face"
                : "on the face «" + uidFace + "»";
            int howMany = samples.Count;

            if (onCount == 0)
                return " I tried " + howMany + " points under the profile and none falls " +
                       location + ": there the part is already open, and where there is no material " +
                       "there is nothing to remove. Move the profile onto solid material, or choose " +
                       "the face on which the material is.";

            if (onCount == howMany)
                return " Under the profile there is material: all " + howMany + " points " +
                       "I tried fall " + location + ". So it is not the position — " +
                       "the depth and the direction of the cut remain.";

            return " Under the profile there is material only in part: " + onCount + " points out of " +
                   howMany + " fall " + location + ", the others do not. The profile straddles " +
                   "a border or an opening.";
        }

        /// <summary>
        /// The points to try inside a circle: the center and two rings. Two and
        /// not one because a single ring, taken near the edge, tells about the
        /// edge and not about the profile; taken near the center, it tells about the center.
        /// </summary>
        private static List<double[]> CircleSamples(double x, double y, double radius)
        {
            var outside = new List<double[]> { new[] { x, y } };
            foreach (double fraction in new[] { 0.55, 0.93 })
                for (int i = 0; i < 8; i++)
                {
                    double t = i * Math.PI / 4;
                    outside.Add(new[]
                    {
                        x + radius * fraction * Math.Cos(t),
                        y + radius * fraction * Math.Sin(t),
                    });
                }
            return outside;
        }

        /// <summary>The center, the four half-sides and the four corners, pulled inward.</summary>
        private static List<double[]> RectangleSamples(
            double x, double y, double length, double width)
        {
            double a = length / 2 * 0.93, b = width / 2 * 0.93;
            return new List<double[]>
            {
                new[] { x, y },
                new[] { x - a, y }, new[] { x + a, y },
                new[] { x, y - b }, new[] { x, y + b },
                new[] { x - a, y - b }, new[] { x + a, y - b },
                new[] { x - a, y + b }, new[] { x + a, y + b },
            };
        }

        /// <summary>
        /// The points to try inside the stadium: three stations along the long
        /// axis, each with its middle point and the two beside it, plus the
        /// two crests. The same stroke/radius/cos/sin that draw it.
        /// </summary>
        private static List<double[]> StadiumSamples(
            double x, double y, double cos, double sin, double travel, double radius)
        {
            var outside = new List<double[]>();
            Func<double, double, double[]> inPlane = (u, v) => new[]
            {
                x + u * cos - v * sin,
                y + u * sin + v * cos,
            };
            foreach (double u in new[] { -travel, 0.0, travel })
                foreach (double v in new[] { 0.0, -radius * 0.9, radius * 0.9 })
                    outside.Add(inPlane(u, v));
            outside.Add(inPlane(-(travel + radius) * 0.93, 0));
            outside.Add(inPlane(+(travel + radius) * 0.93, 0));
            return outside;
        }

        /// <summary>
        /// How far a drawn sketch entity may be from where it was asked, in
        /// metres (brief R10): 0.0001 mm.
        /// </summary>
        internal const double SketchToleranceM = 1e-7;

        /// <summary>
        /// Why the circle is not the one asked for, or null (brief R10): its
        /// centre and its radius read back from the sketch.
        /// </summary>
        private static string MisplacedCircle(
            ISketchSegment circle, double[] centre, double radiusM,
            out double measuredRadiusM, out double centreOffsetM)
        {
            measuredRadiusM = radiusM;
            centreOffsetM = 0;
            var arc = circle as ISketchArc;
            if (arc == null) return "SOLIDWORKS gave back no circle.";
            ISketchPoint point = null;
            try { point = arc.GetCenterPoint2() as ISketchPoint; measuredRadiusM = arc.GetRadius(); }
            catch (Exception ex) { return "its centre could not be read back (" + ex.Message + ")."; }
            if (point == null) return "its centre could not be read back.";
            double du = point.X - centre[0], dv = point.Y - centre[1];
            centreOffsetM = Math.Sqrt(du * du + dv * dv);
            var problems = new List<string>();
            if (centreOffsetM > SketchToleranceM)
                problems.Add("its centre is " + (centreOffsetM * 1000).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                             " mm away from the point asked");
            if (Math.Abs(measuredRadiusM - radiusM) > SketchToleranceM)
                problems.Add("its diameter is " + (2 * measuredRadiusM * 1000).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                             " mm instead of " + (2 * radiusM * 1000).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            return problems.Count == 0 ? null : string.Join(", and ", problems) + ".";
        }

        /// <summary>
        /// Why the rectangle is not the one asked for, or null (brief R10): the
        /// box of the end points of its sides against the two corners asked.
        /// </summary>
        private static string MisplacedRectangle(object[] sides, double[] p1, double[] p2, out double cornerOffsetM)
        {
            cornerOffsetM = 0;
            if (sides == null || sides.Length < 4) return "SOLIDWORKS gave back no rectangle.";
            double minU = double.MaxValue, minV = double.MaxValue, maxU = double.MinValue, maxV = double.MinValue;
            try
            {
                foreach (var s in sides)
                {
                    var line = s as ISketchLine;
                    if (line == null) continue;
                    foreach (var end in new[] { line.GetStartPoint2() as ISketchPoint, line.GetEndPoint2() as ISketchPoint })
                    {
                        if (end == null) return "a corner could not be read back.";
                        minU = Math.Min(minU, end.X); maxU = Math.Max(maxU, end.X);
                        minV = Math.Min(minV, end.Y); maxV = Math.Max(maxV, end.Y);
                    }
                }
            }
            catch (Exception ex) { return "its corners could not be read back (" + ex.Message + ")."; }
            if (minU == double.MaxValue) return "its sides could not be read back.";
            cornerOffsetM = new[]
            {
                Math.Abs(minU - Math.Min(p1[0], p2[0])), Math.Abs(maxU - Math.Max(p1[0], p2[0])),
                Math.Abs(minV - Math.Min(p1[1], p2[1])), Math.Abs(maxV - Math.Max(p1[1], p2[1])),
            }.Max();
            return cornerOffsetM > SketchToleranceM
                ? "a side is " + (cornerOffsetM * 1000).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                  " mm away from where it was asked."
                : null;
        }

        private Outcome CylindricalCut(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uidFace = Str(a, "face_uid", "");
            var face = ResolveFace(docState, a, uidFace, out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double diameter = Num(a, "diameter_mm");
            bool throughAll = Bool(a, "through_all", true);
            double depth = Num(a, "depth_mm", 0);
            double x, y;
            if (!RequiredNum(a, "x_mm", out x, out error)) return Failed(error);
            if (!RequiredNum(a, "y_mm", out y, out error)) return Failed(error);

            // WE MEASURE FIRST, because afterwards it is no longer possible.
            // It serves the volume check at the bottom: see `NoteOnVolume`.
            double? volumeBefore = VolumeMm3(model);

            double holeRadius = diameter / 2;

            // THE ANCHOR (entry E18): what holds the hole where it is when the part
            // changes. It is all checked BEFORE drawing: a wrong anchor
            // must not leave anything in the part. Empty = the old way.
            var anchor = ReadAnchor(a, docState, face, axes, offsetM, x, y, holeRadius, holeRadius, out reason);
            if (anchor == null) return Failed(reason);

            string withoutHope = CutWithoutHope(
                model, axes,
                "The hole of " + diameter.ToString("0.##") + " mm",
                x, y, holeRadius, holeRadius,
                (u, v) => (u - x) * (u - x) + (v - y) * (v - y) <= holeRadius * holeRadius,
                ThroughAll(model, axes, throughAll, depth),
                (du, dv) => "The largest that fits, centered there, is " +
                            (2 * Math.Min(du, dv)).ToString("0.##") + " mm.");
            if (withoutHope != null) return Failed(withoutHope);

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("I could not select the face.");
            var sketches = model.SketchManager;
            sketches.InsertSketch(true);
            var center = PointInSketch(model, axes, offsetM, x, y, out error);
            if (center == null)
            {
                // The open sketch must be closed anyway: leaving it open
                // would block the document in sketch mode in front of a
                // person who asked for none of this.
                try { sketches.InsertSketch(false); } catch { }
                return Failed(error);
            }
            // `AddToDB` TRUE AROUND THE CREATION, ALWAYS (brief R10). With it
            // false SOLIDWORKS infers: on 24/10/2026, on 26P000012, four holes
            // of 0.992 asked at four points came out with their centres moved
            // up to 0.09 mm onto nearby edges and diameters of 1.036 and 1.144
            // (journal: CreateCircle(-0.005802, ...) for a centre asked at
            // -0.00578), and every step answered «succeeded» with the diameter
            // asked for. Up to library 0.153.1 it was set only with an anchor,
            // where the guessed relations would also overconstrain the sketch.
            ISketchSegment cutCircle;
            bool addBefore = sketches.AddToDB;
            bool displayBefore = sketches.DisplayWhenAdded;
            try
            {
                sketches.AddToDB = true;
                sketches.DisplayWhenAdded = false;
                cutCircle = sketches.CreateCircleByRadius(
                    center[0], center[1], center[2], Units.M(diameter / 2));
            }
            finally
            {
                sketches.AddToDB = addBefore;
                sketches.DisplayWhenAdded = displayBefore;
            }

            // AND WHAT WAS DRAWN IS READ BACK before anything is built on it.
            double measuredRadiusM, centreOffsetM;
            string misplaced = MisplacedCircle(
                cutCircle, center, Units.M(diameter / 2), out measuredRadiusM, out centreOffsetM);
            if (misplaced != null)
            {
                try { sketches.InsertSketch(false); } catch { }
                return Failed("The hole of " + diameter.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                              " mm was not drawn where it was asked: " + misplaced +
                              " Try a centre a little away from the edges already on the face, or check «x_mm» " +
                              "and «y_mm» against the face." + WithoutSketch(model));
            }

            // THE DIAMETER, ON THE HOLE'S SKETCH.
            //
            // Without it, the hole cannot be edited with `set_dimension` and its
            // drawing stays mute: `model_dimensions` has nothing to
            // report. It is the shop's most frequent complaint —
            // a part that can be made but cannot be corrected.
            //
            // WE DIMENSION THE SIZE, NOT THE POSITION. The position would want
            // a reference to measure from, and choosing one on behalf of whoever
            // draws means constraining the sketch more than they
            // asked: the later `set_dimension` would fail with an
            // over-constraint message that talks about nothing.
            string textError;
            var dimensionText = PointInSketch(
                model, axes, offsetM,
                x + holeRadius * 1.4 + 5, y + holeRadius * 1.4 + 5, out textError);
            int placedDimensions = cutCircle != null && dimensionText != null
                ? DimensionSegments(model,
                    new List<Dimension> { Dimension.Su(cutCircle, dimensionText) })
                : 0;

            // THE ANCHOR ATTACHES TO THE CIRCLE'S CENTER, after the diameter: the
            // sketch must come out of it fully defined.
            var anchorMeasurements = new Dictionary<string, object>();
            if (!anchor.Empty)
            {
                ISketchPoint centerPoint = null;
                try { centerPoint = ((ISketchArc)cutCircle).GetCenterPoint2() as ISketchPoint; } catch { }
                if (!ApplyAnchor(model, anchor, centerPoint, axes, offsetM, x, y, anchorMeasurements, out string anchorError))
                {
                    try { sketches.InsertSketch(false); } catch { }
                    return Failed(anchorError + WithoutSketch(model));
                }
            }

            sketches.InsertSketch(false);

            // The sketch must be SELECTED: leaving the sketch does not select it,
            // and the feature works on what is selected.
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Cut(model, throughAll, depth);
            if (feature == null)
            {
                // We measure BEFORE removing the sketch: the face stays
                // valid anyway, but the right order is to read the part
                // as it was when SolidWorks said no.
                string below = BelowProfile(
                    face, uidFace, axes, offsetM,
                    CircleSamples(x, y, holeRadius));
                return Failed("The cut was not created." + below + WithoutSketch(model));
            }

            // A THROUGH HOLE ON AN EMPTY BOX MAKES TWO.
            //
            // Measured on 10 September 2026 drawing the enclosure for
            // the antenna: the cable passage asked on a side, with
            // «through_all». Removed 3,922.2 mm3 against the 1,961.1 of a single
            // wall — exactly double, because the cut came out
            // of the back too. The operation said «succeeded», the part
            // had one hole too many, and no check noticed.
            //
            // Whoever draws a box and writes «through_all» almost always means
            // «go through THIS wall». The number of walls drilled
            // cannot be guessed, but it can be COUNTED: every wall crossed
            // leaves a cylindrical face of the diameter asked for.
            int walls = DrilledWalls(feature, diameter);
            if (throughAll && walls > 1)
                return Failed(
                    "The through hole went through " + walls + " walls, " +
                    "not one: the part is hollow and the cut came out on the " +
                    "other side too. If you wanted to drill only this wall, " +
                    "ask for it blind with the depth of the thickness." +
                    RemainsInPart);

            // THE MOVEMENT TEST: an anchor that does not hold up under an edit is of no
            // use, and the cut that carries it does not stay in the part.
            string doesNotHold = MovementTest(model, anchor, feature);
            if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));

            // A THROUGH HOLE THAT DRILLED NO WALL IS NOT A HOLE (brief R10): on
            // 26P000012 two of them answered «succeeded» with walls_drilled 0.
            if (throughAll && walls == 0)
                return Failed(
                    "The through hole drilled no wall: no face of " +
                    diameter.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm came out of the cut, so it did not go through the material where it was asked. " +
                    "Check that the centre lies on material of the face, in the face's coordinates, and try again." +
                    RemoveCut(model, feature, volumeBefore));

            var measurements = new Dictionary<string, object>
            {
                // MEASURED, not repeated from the request (brief R10).
                ["diameter_mm"] = Math.Round(2 * measuredRadiusM * 1000, 4),
                ["diameter_requested_mm"] = diameter,
                ["centre_offset_mm"] = Math.Round(centreOffsetM * 1000, 4),
                ["through_all"] = throughAll,
                ["walls_drilled"] = walls >= 0 ? (object)walls : null,
                ["dimensions_on_sketch"] = placedDimensions,
            };
            foreach (var entry in anchorMeasurements) measurements[entry.Key] = entry.Value;

            // HOW MUCH REALLY WENT AWAY. See `NoteOnVolume`.
            NoteOnVolume(
                measurements, volumeBefore, VolumeMm3(model),
                throughAll ? 0 : Math.PI * holeRadius * holeRadius * depth,
                "The hole of " + diameter.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm");

            return Succeeded(feature, measurements, FaceNormal(face));
        }

        /// <summary>
        /// How far a feature may deviate from the expected volume before
        /// it is worth saying so. One hundredth.
        ///
        /// Measured on 14/9/2026 on the two cases that matter: a hole entirely on the
        /// face removes the exact volume (192,000 - 191,607.3 = 392.7 against
        /// the 392,699 of pi*5^2*5, that is less than a thousandth of deviation), and
        /// one centered on the edge removes **half**. Between the two there is a
        /// factor of five hundred: any threshold in between works, and one
        /// percent lets the rounding pass without letting through anything
        /// that has a shape.
        /// </summary>
        private const double VolumeOffset = 0.01;

        /// <summary>
        /// WE SAY HOW MUCH WENT AWAY, AND HOW MUCH WAS SUPPOSED TO GO AWAY.
        ///
        /// THE DEFECT THIS CLOSES (entry E13). A `cylindrical_cut` with
        /// the profile straddling the edge of the face removes half the material and
        /// answers `succeeded: true`, `walls_drilled: 1`. Measured: 10 hole
        /// 5 deep centered on the lower edge of a face, **196.3 mm³
        /// instead of 392.7** — exactly half — with the bottom of the hole reduced
        /// to a half disc of 39.27 mm² instead of 78.54.
        ///
        /// THE GUARDIAN WAS THERE AND WAS LOOKING THE OTHER WAY. `BelowProfile` knows how to
        /// count how many points of the profile fall on the face and says so well,
        /// but its comment declares: «DOES NOT REFUSE ANYTHING AND MUST NEVER DO
        /// SO: it is called only when SOLIDWORKS has already said no». When
        /// SOLIDWORKS says YES, nobody calls it.
        ///
        /// WE DO NOT REFUSE, WE SAY. The operation succeeded and the part exists:
        /// refusing now would mean throwing away a feature already made over
        /// a suspicion. We write how much went away, how much was expected, and
        /// how far it deviates — then the reader decides. It is the same form as
        /// `positions_not_checked` and of the anchor that moves.
        ///
        /// ONLY ON BLIND CUTS IS THE EXPECTED A MULTIPLICATION. For a through cut the
        /// volume depends on how much material is underneath, which is not known without
        /// measuring it: there we write `removed_mm3` and nothing else, without an expected value that
        /// would be invented. A wrong alarm on a legitimate through cut
        /// would be worse than the defect being closed.
        ///
        /// <param name="expectedMm3">Zero when it cannot be known.</param>
        /// </summary>
        private static void NoteOnVolume(
            Dictionary<string, object> measurements,
            double? volumeBefore, double? volumeAfter, double expectedMm3, string what)
        {
            if (measurements == null) return;

            // A volume that could not be read does not become a zero: a
            // zero would mean «it removed nothing», which is a statement.
            //
            // `<= 0` ON BOTH, and the first version had `volumeAfter < 0`.
            // `VolumeMm3` NEVER returns a negative: it answers 0 when the
            // document is not a part, when `GetBodies2` gives null and from the
            // `catch` that wraps everything — that is, on every error road.
            // So that branch could not fire, and a zero from a failed
            // reading passed as if it were a measure: `removed` became
            // the WHOLE volume of the part, and on a perfectly successful hole
            // a `note_volume` came out saying «192,000 mm³ instead of 392.7».
            // The false alarm on the correct one, that is exactly what this
            // entry declares worse than the defect it is closing.
            //
            // The comment above already said the right thing, and it was the
            // line that did not do it: it is the most frequent form of defect found
            // in this project, committed while documenting it.
            //
            // `Shell` writes it right fifteen lines further down
            // (`volumeBefore > 0 && volumeAfter > 0`): the same thing, done well,
            // in the same file.
            //
            // SINCE 25/9/2026 (entry Z-C8) THE TYPE SAYS IT: `VolumeMm3` returns
            // `double?`, null when it cannot be read, and this check no longer
            // has to guess which zero is a measure.
            if (volumeBefore == null || volumeAfter == null || volumeBefore <= 0) return;
            double removed = volumeBefore.Value - volumeAfter.Value;

            measurements["removed_mm3"] = Math.Round(removed, 2);
            if (expectedMm3 <= 0) return;

            measurements["expected_mm3"] = Math.Round(expectedMm3, 2);
            double offset = Math.Abs(removed - expectedMm3) / expectedMm3;
            if (offset <= VolumeOffset) return;

            measurements["note_volume"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} removed {1:0.##} mm³ instead of the {2:0.##} that was expected " +
                "({3:0.#}% {4}). The operation succeeded, but the part is not what " +
                "the plan described: usually it means that the profile falls partly " +
                "outside the face, or on material already removed. Look at the part " +
                "before going on.",
                what, removed, expectedMm3, offset * 100,
                removed < expectedMm3 ? "less" : "more");
        }

        private Outcome RectangularCut(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uidFace = Str(a, "face_uid", "");
            var face = ResolveFace(docState, a, uidFace, out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double length = Num(a, "length_mm");
            double width = Num(a, "width_mm");
            bool throughAll = Bool(a, "through_all", false);
            double depth = Num(a, "depth_mm", 0);
            if (!throughAll && depth <= 0)
                return Failed("A pocket that is not through needs a depth.");

            // WE MEASURE FIRST, because afterwards it is no longer possible.
            double? volumeBefore = VolumeMm3(model);
            double x, y;
            if (!RequiredNum(a, "x_mm", out x, out error)) return Failed(error);
            if (!RequiredNum(a, "y_mm", out y, out error)) return Failed(error);

            // THE ANCHOR (entry E18), all checked before drawing.
            var anchor = ReadAnchor(a, docState, face, axes, offsetM, x, y, length / 2, width / 2, out reason);
            if (anchor == null) return Failed(reason);

            string withoutHope = CutWithoutHope(
                model, axes,
                "The pocket " + length.ToString("0.##") + " x " + width.ToString("0.##") + " mm",
                x, y, length / 2, width / 2,
                (u, v) => Math.Abs(u - x) <= length / 2 && Math.Abs(v - y) <= width / 2,
                ThroughAll(model, axes, throughAll, depth),
                (du, dv) => "The largest that fits, centered there, is " +
                            (2 * du).ToString("0.##") + " x " + (2 * dv).ToString("0.##") + " mm.");
            if (withoutHope != null) return Failed(withoutHope);

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("I could not select the face.");
            var sketches = model.SketchManager;
            sketches.InsertSketch(true);
            var p1 = PointInSketch(model, axes, offsetM, x - length / 2, y - width / 2, out error);
            var p2 = p1 == null
                ? null
                : PointInSketch(model, axes, offsetM, x + length / 2, y + width / 2, out error);
            if (p1 == null || p2 == null)
            {
                try { sketches.InsertSketch(false); } catch { }
                return Failed(error);
            }
            // `AddToDB` TRUE AROUND THE CREATION, ALWAYS, and read back: the same
            // inference that moved the holes moves the corners (brief R10).
            object[] cutSides;
            bool addBefore = sketches.AddToDB;
            bool displayBefore = sketches.DisplayWhenAdded;
            try
            {
                sketches.AddToDB = true;
                sketches.DisplayWhenAdded = false;
                cutSides = sketches.CreateCornerRectangle(
                    p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]) as object[];
            }
            finally
            {
                sketches.AddToDB = addBefore;
                sketches.DisplayWhenAdded = displayBefore;
            }
            double cornerOffsetM;
            string misplacedRectangle = MisplacedRectangle(cutSides, p1, p2, out cornerOffsetM);
            if (misplacedRectangle != null)
            {
                try { sketches.InsertSketch(false); } catch { }
                return Failed("The pocket was not drawn where it was asked: " + misplacedRectangle +
                              " Try a position a little away from the edges already on the face, or check " +
                              "«x_mm» and «y_mm» against the face." + WithoutSketch(model));
            }

            // THE POCKET'S TWO DIMENSIONS, for the same reason as the round hole.
            //
            // Only the first two sides are taken: they are adjacent, so
            // perpendicular, so one gives the length and the other the
            // width. WHICH of the two is which is not
            // established a priori — on any face «horizontal» does not
            // mean anything — and it is not needed: the two dimensions are born
            // right anyway, and the entry's criterion is verified by MEASURING
            // the values, not by deducing them from the order of the sides.
            int placedDimensions = 0;
            if (cutSides != null && cutSides.Length >= 2)
            {
                string textError;
                var below = PointInSketch(
                    model, axes, offsetM,
                    x, y - width / 2 - 5, out textError);
                var beside = PointInSketch(
                    model, axes, offsetM,
                    x + length / 2 + 5, y, out textError);
                var list = new List<Dimension>();
                if (below != null) list.Add(Dimension.Su(cutSides[0], below));
                if (beside != null) list.Add(Dimension.Su(cutSides[1], beside));
                placedDimensions = DimensionSegments(model, list);
            }

            // THE ANCHOR ATTACHES TO THE CENTER OF THE POCKET, which is built: the
            // rectangle by corners has no point at the center.
            var anchorMeasurements = new Dictionary<string, object>();
            if (!anchor.Empty)
            {
                var centerPoint = RectangleCenter(model, cutSides, p1, p2);
                string anchorError = centerPoint == null
                    ? "I could not build the center of the pocket to attach the anchor to."
                    : null;
                if (anchorError != null ||
                    !ApplyAnchor(model, anchor, centerPoint, axes, offsetM, x, y, anchorMeasurements, out anchorError))
                {
                    try { sketches.InsertSketch(false); } catch { }
                    return Failed(anchorError + WithoutSketch(model));
                }
            }

            sketches.InsertSketch(false);

            // The sketch must be SELECTED: leaving the sketch does not select it,
            // and the feature works on what is selected.
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Cut(model, throughAll, depth);
            if (feature == null)
            {
                string below = BelowProfile(
                    face, uidFace, axes, offsetM,
                    RectangleSamples(x, y, length, width));
                return Failed("The cut was not created." + below + WithoutSketch(model));
            }

            string doesNotHold = MovementTest(model, anchor, feature);
            if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));

            var pocketMeasurements = new Dictionary<string, object>
            {
                ["length_mm"] = length,
                ["width_mm"] = width,
                ["through_all"] = throughAll,
                ["dimensions_on_sketch"] = placedDimensions,
            };
            foreach (var entry in anchorMeasurements) pocketMeasurements[entry.Key] = entry.Value;

            // THE SAME DEFECT AS `cylindrical_cut`, and the same repair
            // (entry E13). A pocket whose profile falls partly outside the
            // face removes less material than the plan described, and
            // answers «succeeded» all the same.
            NoteOnVolume(
                pocketMeasurements, volumeBefore, VolumeMm3(model),
                throughAll ? 0 : length * width * depth,
                "The pocket " + length.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                "x" + width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

            return Succeeded(feature, pocketMeasurements, FaceNormal(face));
        }

        /// <summary>
        /// THE SLOT: an elongated hole, that is, the fastening that gets adjusted.
        ///
        /// WHY IT NEEDS AN OPERATION OF ITS OWN. In metalwork the slot is not a
        /// whim: it is what makes mounting possible. A bracket with two slots
        /// aligns on site, a bracket with two round holes either fits or gets
        /// redone. With the previous catalog one could only ask for a round
        /// hole (`cylindrical_cut`) or a rectangular pocket
        /// (`rectangular_cut`): the second resembles it, but has sharp
        /// corners and the screw rattles inside it instead of sliding.
        ///
        /// THE SHAPE. A "stadium": two straight segments of length L−W and two
        /// semicircles of radius W/2 at the ends. L is the TOTAL length
        /// from outside to outside — as it is measured on the part and as it is written
        /// on the drawing — and W is the width, that is, the diameter of the
        /// ends.
        ///
        /// SIGNATURES VERIFIED by reflection on interop 29.5.1.1:
        ///   ISketchManager.CreateLine(Double X1, Double Y1, Double Z1,
        ///       Double X2, Double Y2, Double Z2) -> SketchSegment
        ///   ISketchManager.CreateArc(Double XC, Double YC, Double Zc,
        ///       Double X1, Double Y1, Double Z1, Double X2, Double Y2,
        ///       Double Z2, Int16 Direction) -> SketchSegment
        ///
        /// THE ARC'S DIRECTION IS THE DOUBT, AND WE DO NOT HOPE: WE MEASURE.
        /// `Direction` says which way to turn from the first point to the second, and
        /// lives in the SKETCH's plane — while the six points below are
        /// built in the MODEL's system and get there through
        /// `PointInSketch`. That transformation does not preserve handedness: its
        /// own comment says that «for two opposite faces the Y axis
        /// comes out inverted». So the same `-1` means one thing on one
        /// face and the opposite on the one facing it.
        ///
        /// WHAT HAPPENS IF IT IS WRONG — and here the comment said the wrong thing
        /// until 10 September 2026, when a review redid it with the
        /// numbers in hand. A 270 degree arc does not come out and nothing self-intersects:
        /// the two ends of each arc are diametrically opposite
        /// with respect to the center, so BOTH candidate arcs are 180
        /// degrees. The direction decides only on which side the semicircle passes.
        /// With the right direction it bulges outward and the slot is L long; with the wrong
        /// one it goes inward, and the slot comes out L−W long with the ends BITTEN
        /// into half moons. For an M10 51x11: 40 mm instead of 51, area 344.97
        /// instead of 535.03. The contour stays closed and simple, the cut
        /// succeeds, the feature is green, and the screw slides 29 mm instead of 40.
        ///
        /// THEREFORE THE MATERIAL REMOVED IS COUNTED. The blind case takes it in
        /// full, because there the comparison is an equality. The through case
        /// is more awkward — how much material is underneath is not known in
        /// general — and there two things are done: a ceiling against much
        /// larger shapes, and a check that looks for exactly THIS error, which is
        /// smaller and would pass under the ceiling. It is the same lesson as
        /// `HoleWizard5`, which made a two-inch hole without giving any
        /// error: the only defense against a part that looks right is a
        /// measure, but the measure must be chosen so that it can fail.
        /// </summary>
        private Outcome Slot(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uidFace = Str(a, "face_uid", "");
            var face = ResolveFace(docState, a, uidFace, out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double length = Num(a, "length_mm");
            double width = Num(a, "width_mm");
            double angle = Num(a, "angle_deg", 0);
            bool throughAll = Bool(a, "through_all", true);
            double depth = Num(a, "depth_mm", 0);
            double x, y;
            if (!RequiredNum(a, "x_mm", out x, out error)) return Failed(error);
            if (!RequiredNum(a, "y_mm", out y, out error)) return Failed(error);

            // THE LENGTH IS FROM OUTSIDE TO OUTSIDE. A slot as long as it is wide
            // is a round hole, and asking for it that way almost always means that
            // one of the two numbers is wrong.
            if (length <= width)
                return Failed(
                    "The slot is " + length.ToString("0.##") + " long and " +
                    width.ToString("0.##") + " wide (mm): the length is measured from " +
                    "outside to outside and must be greater than the width. If you " +
                    "wanted a round hole of " + width.ToString("0.##") +
                    " mm use the hole, not the slot.");
            if (!throughAll && depth <= 0)
                return Failed("A slot that is not through needs a depth.");

            // The stadium, in the face's system and before rotating it: half the
            // stroke between the two centers, and the radius of the ends.
            double travel = (length - width) / 2.0;
            double radius = width / 2.0;
            double rad = angle * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            Func<double, double, double[]> inPlane = (u, v) => new[]
            {
                x + u * cos - v * sin,
                y + u * sin + v * cos,
            };

            // THE CRESTS: the outermost point of each semicircle, on the slot's
            // long axis. They are what determines the arc without having to
            // declare its direction.
            var crestA = inPlane(-travel - radius, 0);
            var crestB = inPlane(+travel + radius, 0);
            var topA = inPlane(-travel, +radius);
            var topB = inPlane(+travel, +radius);
            var bottomA = inPlane(-travel, -radius);
            var bottomB = inPlane(+travel, -radius);

            // THE ANCHOR (entry E18), all checked before drawing. The
            // half bounding-box size per axis is the one below: the projected stroke
            // plus the radius.
            var anchor = ReadAnchor(a, docState, face, axes, offsetM, x, y,
                Math.Abs(travel * cos) + radius, Math.Abs(travel * sin) + radius, out reason);
            if (anchor == null) return Failed(reason);

            // Half bounding-box size of the stadium on the face's two axes: the projected
            // stroke plus the radius of the ends. With angle 0 it gives
            // length/2 and width/2, as it should.
            string withoutHope = CutWithoutHope(
                model, axes,
                "The slot " + length.ToString("0.##") + " x " + width.ToString("0.##") + " mm",
                x, y,
                Math.Abs(travel * cos) + radius, Math.Abs(travel * sin) + radius,
                (u, v) => InsideStadium(u, v, x, y, cos, sin, travel, radius),
                ThroughAll(model, axes, throughAll, depth),
                (du, dv) => "Centered there, a shape up to " +
                            (2 * du).ToString("0.##") + " x " + (2 * dv).ToString("0.##") + " mm fits inside.");
            if (withoutHope != null) return Failed(withoutHope);

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("I could not select the face.");

            double? volumeBefore = VolumeMm3(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("cutting the slot"));
            var sketches = model.SketchManager;
            sketches.InsertSketch(true);

            double[] pTopA = null, pTopB = null, pBottomA = null, pBottomB = null;
            double[] pCrestA = null, pCrestB = null;
            bool drawn = false;
            int segments = 0;
            int stadiumDimensions = 0;
            var anchorMeasurements = new Dictionary<string, object>();
            try
            {
                pTopA = PointInSketch(model, axes, offsetM, topA[0], topA[1], out error);
                pTopB = pTopA == null ? null
                    : PointInSketch(model, axes, offsetM, topB[0], topB[1], out error);
                pBottomA = pTopB == null ? null
                    : PointInSketch(model, axes, offsetM, bottomA[0], bottomA[1], out error);
                pBottomB = pBottomA == null ? null
                    : PointInSketch(model, axes, offsetM, bottomB[0], bottomB[1], out error);
                pCrestA = pBottomB == null ? null
                    : PointInSketch(model, axes, offsetM, crestA[0], crestA[1], out error);
                pCrestB = pCrestA == null ? null
                    : PointInSketch(model, axes, offsetM, crestB[0], crestB[1], out error);

                if (pCrestB != null)
                {
                    // ADD-TO-DB, AS FOR «extrude_profile», AND HERE IT IS NOT TO
                    // GO FASTER. Without it, SolidWorks infers a
                    // relation for every segment and MOVES the points to make it
                    // hold. The four pieces of the stadium touch by
                    // construction — the end of the line is the SAME vector
                    // that serves as the start of the arc — but if each one gets attached
                    // on its own, the contour opens by fractions of a
                    // millimeter. An open contour cannot be cut, and indeed
                    // on 10 September 2026 twelve tests out of twelve ended with
                    // «the slot was not cut».
                    ISketchSegment[] created;
                    drawn = DrawStadium(
                        sketches, pTopA, pTopB, pBottomA, pBottomB, pCrestA, pCrestB,
                        out segments, out error, out created);

                    // THE STADIUM GETS DEFINED (entry E18): tangencies, equal radii,
                    // the midline between the centers and its dimensions. Without an anchor it is
                    // a gain all the same — the slot is corrected with
                    // `set_dimension` — and an error here does not stop the cut;
                    // with the anchor the sketch must come out fully
                    // defined, or it is not cut.
                    if (drawn)
                    {
                        string stadiumError;
                        stadiumDimensions = DefineStadium(
                            model, created, axes, offsetM, x, y, angle, travel, radius,
                            anchor, anchorMeasurements, out stadiumError);
                        if (!anchor.Empty && stadiumError != null)
                        {
                            drawn = false;
                            error = stadiumError;
                        }
                    }
                }
            }
            finally
            {
                // The empty `catch` is not laziness: if leaving the sketch
                // failed, the exception would replace the TRUE reason for the
                // failure — and the reader would find the wrong error.
                try { sketches.InsertSketch(false); } catch { }
            }
            if (!drawn) return Failed(error + WithoutSketch(model));

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Cut(model, throughAll, depth);
            if (feature == null)
            {
                // With the complete contour, «the problem is elsewhere» was a
                // polite way of saying that we did not know. Now we look.
                string below = segments < 4
                    ? ""
                    : BelowProfile(
                        face, uidFace, axes, offsetM,
                        StadiumSamples(x, y, cos, sin, travel, radius));
                return Failed(
                    "The slot was not cut. The sketch has " + segments +
                    " segments out of the four that are needed (two lines and two arcs): " +
                    (segments < 4
                        ? "the contour is not complete, and an open contour cannot be cut."
                        : "the contour is complete, so the problem is not the drawing.") +
                    below + WithoutSketch(model));
            }

            // THE MOVEMENT TEST of the anchor, as for hole and pocket.
            string doesNotHold = MovementTest(model, anchor, feature);
            if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));

            // ── AND NOW WE COUNT ────────────────────────────────────────────
            // The stadium's area: the central rectangle plus the two
            // semicircles, which together make a circle.
            double area = (length - width) * width + Math.PI * radius * radius;
            double? volumeAfter = VolumeMm3(model);
            if (volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["length_mm"] = length,
                    ["width_mm"] = width,
                    ["angle_deg"] = angle,
                    ["through_all"] = throughAll,
                    ["area_mm2"] = Math.Round(area, 2),
                    ["material_removed_mm3"] = null,
                    ["dimensions_on_sketch"] = stadiumDimensions,
                    ["volume_not_reread"] = NoteVolumeNotReread,
                }, FaceNormal(face));
            double removed = volumeBefore.Value - volumeAfter.Value;
            if (removed <= 0)
                return Failed(
                    "The slot removed nothing: the cut succeeded but all the " +
                    "material is still there. Usually it means that the " +
                    "profile was outside the part." + RemainsInPart);

            if (!throughAll)
            {
                double expected = area * depth;
                if (Math.Abs(removed - expected) > Math.Max(1.0, expected * 0.03))
                    return Failed(
                        "The slot was cut but it removed " + removed.ToString("0.#") +
                        " mm3 instead of " + expected.ToString("0.#") + ": the shape " +
                        "that came out is not the one asked for. I do not confirm a part " +
                        "that does not add up." + RemainsInPart);
            }
            else
            {
                // ── Through: two checks, and they serve different things ────────
                //
                // How much material is underneath is not known in general: a
                // through cut goes through whatever it finds. That is why here
                // there is no equality as in the blind case.
                double maximumThickness = ExtentAlongAxis(model, (int)axes[2]);

                // 1. THE CEILING. The part cannot have lost more material than
                //    fits in the bounding box crossed. It catches shapes
                //    much larger than they should be.
                double limit = area * maximumThickness * 1.02;
                if (maximumThickness > 0 && removed > limit)
                    return Failed(
                        "The slot removed " + removed.ToString("0.#") + " mm3, more " +
                        "than a slot of those measures can remove (" +
                        limit.ToString("0.#") + " at most): the shape that came " +
                        "out is not the one asked for." + RemainsInPart);

                // 2. THE CHECK THAT LOOKS FOR THE ERROR WE KNOW.
                //
                //    A CEILING ALONE IS NOT ENOUGH, AND THE REASON IS PRECISE: if the
                //    direction of the arcs is wrong the semicircles GO INWARD
                //    instead of bulging out, and the slot removes LESS material than
                //    it should — so it passes under the ceiling and is never
                //    reported. The ceiling is blind exactly on the only error for
                //    which the operation is not yet declared.
                //
                //    The wrong area, however, is known in advance: it is the rectangle
                //    MINUS the circle instead of plus. If the material removed
                //    resembles that one, it is not a coincidence.
                double wrongArea = (length - width) * width
                                       - Math.PI * radius * radius;
                if (maximumThickness > 0 && wrongArea > 0)
                {
                    double suspicious = wrongArea * maximumThickness;
                    if (Math.Abs(removed - suspicious) <= Math.Max(1.0, suspicious * 0.03))
                        return Failed(
                            "The slot removed " + removed.ToString("0.#") + " mm3, which " +
                            "is exactly what a slot removes with the semicircles " +
                            "turned INWARD instead of outward: it would be " +
                            (length - width).ToString("0.##") + " mm long instead of " +
                            length.ToString("0.##") + ", with the ends bitten into a " +
                            "crescent. I do not confirm a part like that." + RemainsInPart);
                }
            }

            var slotMeasurements = new Dictionary<string, object>
            {
                ["length_mm"] = length,
                ["width_mm"] = width,
                ["angle_deg"] = angle,
                ["through_all"] = throughAll,
                ["area_mm2"] = Math.Round(area, 2),
                ["material_removed_mm3"] = Math.Round(removed, 2),
                ["dimensions_on_sketch"] = stadiumDimensions,
            };
            foreach (var entry in anchorMeasurements) slotMeasurements[entry.Key] = entry.Value;
            return Succeeded(feature, slotMeasurements, FaceNormal(face));
        }

        /// <summary>
        /// How thick the part is along a model axis, in mm. It serves
        /// as an upper limit for a through cut: it is not a fine measure,
        /// it is a ceiling.
        /// </summary>
        private static double ExtentAlongAxis(IModelDoc2 model, int axis)
        {
            var partDoc = model as IPartDoc;
            var bodies = partDoc?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) return 0;
            // Approximate ON PURPOSE: this is a ceiling for a through cut,
            // and the approximate box encloses the body (IBody2~GetBodyBox.html),
            // so it is never lower than the true one.
            var u = ApiHelpers.UnionOfBoxes(bodies, c => ApiHelpers.ApproximateBoundingBox(c as IBody2));
            if (u == null || u[axis + 3] <= u[axis]) return 0;
            return Units.Mm(u[axis + 3] - u[axis]);
        }

        /// <summary>
        /// MOVES (or COPIES) BODIES. The wall the bench ran into.
        ///
        /// WHY IT EXISTS. On 9 September 2026, at the request «make me a
        /// castle: two 1000x800 frames 600 mm apart, joined by four
        /// uprights», the model wrote the right diagnosis by itself:
        ///
        ///   «I cannot overlap the same frame twice. I have to move
        ///    the second frame by 600 mm along the Z axis»
        ///
        /// and then proposed the same `rectangular_frame` twice, which the
        /// validator refused. It was not the model that was wrong: there was
        /// no operation able to move what it had already made.
        /// With `copy = true` the castle is made in one step — the second frame
        /// is a COPY of the first, moved by 600.
        ///
        /// SIGNATURES VERIFIED by reflection on interop 29.5.1.1:
        ///   IFeatureManager.InsertMoveCopyBody2(
        ///       Double TransX, Double TransY, Double TransZ, Double TransDist,
        ///       Double RotPointX, Double RotPointY, Double RotPointZ,
        ///       Double RotAngleX, Double RotAngleY, Double RotAngleZ,
        ///       Boolean BCopy, Int32 NumCopies) -> Feature
        ///   ISelectionMgr.CreateSelectData() -> SelectData   (with .Mark)
        ///   IMoveCopyBodyFeatureData has TransformX/Y/Z AND TransformValue,
        ///       that is DIRECTION and MAGNITUDE kept separate.
        ///
        /// THE DOUBT, AND IT IS A BIG ONE. Since direction and magnitude are two
        /// distinct fields, it is not certain which of the two commands: if SolidWorks
        /// took TransX/Y/Z as an absolute displacement ignoring TransDist,
        /// or multiplied the two, the part would move by the WRONG
        /// distance — and nobody would give an error. Here we pass a UNIT
        /// direction and the distance in TransDist, which is the most
        /// likely reading; and then we measure.
        ///
        /// HOW A DISPLACEMENT IS MEASURED. With the center of gravity. Moving a
        /// subset of bodies moves the center of gravity of the WHOLE part by a
        /// known fraction — that of the moved volume over the total volume:
        ///
        ///     center_after = center_before + (volume_moved / volume_total) * d
        ///
        /// It is an exact equality, not an estimate, and it falls apart at once if the
        /// distance is wrong even slightly. `IBody2.GetMassProperties`
        /// gives center of gravity and volume together: the first three numbers are the center
        /// of mass in meters, the fourth is the volume.
        /// </summary>
        private Outcome MoveBodies(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            int howMany;
            string reason;
            var bodies = BodiesFromReference(a, "bodies_ref", docState, out howMany, out reason);
            if (bodies == null) return Failed(reason);

            double dx = Num(a, "dx_mm", 0);
            double dy = Num(a, "dy_mm", 0);
            double dz = Num(a, "dz_mm", 0);
            bool copy = Bool(a, "copy", false);
            int copies = (int)Num(a, "copy_count", 1);
            if (!copy) copies = 0;
            else if (copies < 1) copies = 1;

            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < 1e-6)
                return Failed(
                    "Zero move: dx, dy and dz are all zero. If you wanted " +
                    "to leave the bodies where they are, this operation is not " +
                    "needed; if you wanted to copy them in place, two overlapping " +
                    "bodies are a wrong part, not a copy.");

            // The starting state: the whole part, and only the bodies to move.
            double totalVolume, movedVolume;
            double[] totalCenter, movedCenter;
            if (!MassOf(AllSolidBodies(model), out totalVolume, out totalCenter))
                return Failed("I cannot measure the part before moving it.");
            if (!MassOf(bodies, out movedVolume, out movedCenter))
                return Failed("I cannot measure the bodies to move.");
            if (totalVolume <= 0 || movedVolume <= 0)
                return Failed("The bodies indicated have no volume: there is nothing to move.");
            int bodiesBefore = CountSolidBodies(model);
            if (bodiesBefore < 0) return Failed("I cannot count the bodies of the part before moving them.");

            // The bodies to move must be selected with MARK 1: it is the mark that
            // the feature reads. With the mark at zero the selection is there but the
            // feature does not see it, and does not say so.
            model.ClearSelection2(true);
            var handler = model.SelectionManager as ISelectionMgr;
            var mark = handler?.CreateSelectData();
            if (mark == null) return Failed("SolidWorks does not give me a way to mark the selection.");
            mark.Mark = SelectionMarks.MoveCopyBodyBody;
            // A BODY IS NOT AN IEntity, AND THE DIFFERENCE DOES NOT SHOW UNTIL
            // IT IS TRIED. Measured on 10 September 2026 inside SolidWorks:
            // `body as IEntity` returns null in the interop, the counter stayed at
            // zero and all five displacement tests said «the bodies
            // indicated could not be selected». Faces and edges
            // are IEntity; bodies have their own `IBody2.Select2`.
            int selected = 0;
            foreach (var c in bodies)
            {
                var body = c as IBody2;
                if (body != null && body.Select2(true, mark)) { selected++; continue; }
                var ent = c as IEntity;
                if (ent != null && ent.Select4(true, mark)) selected++;
            }
            if (selected == 0)
                return Failed("The bodies indicated could not be selected.");

            IFeature feature;
            try
            {
                // MEASURED INSIDE SOLIDWORKS ON 10 SEPTEMBER 2026, AND THE DOUBT
                // IS RESOLVED: TransX/Y/Z are in command, which are the ABSOLUTE
                // displacement in meters; TransDist is IGNORED.
                //
                // Before, here I was passing a unit vector plus the distance in
                // TransDist — the reading that seemed most likely. The
                // result: the vector (1,0,0 or similar) was taken as
                // meters, and ANY displacement asked for became 1000 mm.
                // Asking for 50 mm the part moved a thousand, with no
                // error. The center of gravity said so: a deviation of 475 mm on an
                // expected 25. It was exactly the measure written in order to be able to
                // fail, and it failed at the right point.
                //
                // TransDist stays populated with the true distance: if a
                // future version decided to read it, it would read the right number
                // instead of a zero.
                feature = model.FeatureManager.InsertMoveCopyBody2(
                    TransX: Units.M(dx),
                    TransY: Units.M(dy),
                    TransZ: Units.M(dz),
                    TransDist: Units.M(distance),
                    RotPointX: 0, RotPointY: 0, RotPointZ: 0,
                    RotAngleX: 0, RotAngleY: 0, RotAngleZ: 0,
                    BCopy: copy,
                    NumCopies: copies);
            }
            catch (Exception ex)
            {
                return Failed("The move did not succeed: " + ex.Message);
            }
            if (feature == null)
                return Failed("The move was not created.");

            // ── AND NOW WE MEASURE ───────────────────────────────────────────
            int bodiesAfter = CountSolidBodies(model);
            int expectedBodies = bodiesBefore + copies * selected;
            if (bodiesAfter >= 0 && bodiesAfter != expectedBodies)
                return Failed(
                    "After the move the part has " + bodiesAfter + " bodies instead of " +
                    expectedBodies + ". I do not confirm a part different from the " +
                    "one I showed you: withdraw it — " + Sentences.HowToWithdrawIt + ".");

            double volumeAfter;
            double[] centerAfter;
            if (!MassOf(AllSolidBodies(model), out volumeAfter, out centerAfter))
                return Failed("I cannot measure the part again after the move.");

            double expectedVolume = totalVolume + copies * movedVolume;
            if (Math.Abs(volumeAfter - expectedVolume) > Math.Max(1.0, expectedVolume * 0.005))
                return Failed(
                    "After the move the part has a volume of " +
                    volumeAfter.ToString("0.#") + " mm3 instead of " +
                    expectedVolume.ToString("0.#") + ": something merged or was " +
                    "cut. Withdraw it — " + Sentences.HowToWithdrawIt + ".");

            // The expected center of gravity. Without a copy the fraction of moved volume
            // is shifted; with copies, the k-th copy sits at k*d, and the sum of the k
            // from 1 to n is n(n+1)/2.
            var d = new[] { dx, dy, dz };
            var expected = new double[3];
            if (copies == 0)
            {
                for (int i = 0; i < 3; i++)
                    expected[i] = totalCenter[i] + (movedVolume / totalVolume) * d[i];
            }
            else
            {
                double sumK = copies * (copies + 1) / 2.0;
                for (int i = 0; i < 3; i++)
                    expected[i] =
                        (totalVolume * totalCenter[i]
                         + movedVolume * (copies * movedCenter[i] + sumK * d[i]))
                        / expectedVolume;
            }

            double deviation = 0;
            for (int i = 0; i < 3; i++) deviation = Math.Max(deviation, Math.Abs(centerAfter[i] - expected[i]));
            double tolerance = Math.Max(0.05, distance * 0.002);
            if (deviation > tolerance)
                return Failed(
                    "The bodies did not end up where they should: the center of " +
                    "mass of the part is " + deviation.ToString("0.###") + " mm away " +
                    "from where a move of " + distance.ToString("0.##") +
                    " mm puts it. Almost always it means that the distance applied is not " +
                    "the one asked for. Withdraw it — " + Sentences.HowToWithdrawIt + ".");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["bodies_moved"] = selected,
                ["dx_mm"] = dx,
                ["dy_mm"] = dy,
                ["dz_mm"] = dz,
                ["distance_mm"] = Math.Round(distance, 3),
                ["copy"] = copy,
                ["copy_count"] = copies,
                ["bodies_after"] = bodiesAfter,
                ["centroid_deviation_mm"] = Math.Round(deviation, 4),
            }, null);
        }

        /// <summary>
        /// The four segments of the stadium, with automatic inference turned off.
        ///
        /// WHY TURN IT OFF. The four pieces touch by construction: the
        /// start of each line is the SAME vector that serves as the start of the arc, not
        /// a recalculated number. But if SolidWorks attaches each segment on
        /// its own — which is what it does when `AddToDB` is false — it
        /// moves them to make the relations it infers hold, and the contour
        /// opens by fractions of a millimeter. An open contour cannot be cut.
        ///
        /// It returns how many segments were really born: it is the number the error
        /// message uses to say WHETHER the contour was complete, instead of
        /// leaving it to be guessed.
        /// </summary>
        private static bool DrawStadium(
            ISketchManager sketches,
            double[] topA, double[] topB, double[] bottomA, double[] bottomB,
            double[] crestA, double[] crestB,
            out int segments, out string error, out ISketchSegment[] created)
        {
            segments = 0;
            error = null;
            created = new ISketchSegment[4];
            bool addToDbBefore = false, displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }
            try
            {
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }

                created[0] = sketches.CreateLine(topA[0], topA[1], topA[2],
                                               topB[0], topB[1], topB[2]);
                if (created[0] != null) segments++;
                created[1] = sketches.CreateLine(bottomB[0], bottomB[1], bottomB[2],
                                               bottomA[0], bottomA[1], bottomA[2]);
                if (created[1] != null) segments++;

                // THE ARCS ARE GIVEN BY THREE POINTS, NOT BY DIRECTION.
                //
                // Before they were `CreateArc(center, end1, end2, Direction)` with
                // `Direction = -1` written by hand. A direction, however, lives in the
                // SKETCH's plane, while the points are built in the
                // MODEL's system, and the transformation between the two does not
                // preserve handedness. Measured on 10 September 2026 on a
                // plate: it worked on «front», «down» and «right» and it
                // flipped on «back», «up» and «left» — one face for
                // each pair, that is half of the face set.
                //
                // Flipped, the semicircle GOES INWARD instead of bulging out:
                // the slot comes out L−W long with the ends bitten. On the blind
                // test the count was 1724.8 mm3 instead of 2675.2, that is
                // exactly (L−W)·W − pi·r^2 in place of + pi·r^2.
                //
                // `Create3PointArc(end1, end2, pointOnArc)` removes the
                // question: the third point is the crest of the semicircle,
                // goes through `PointInSketch` like all the others, and
                // the arc stays determined by the GEOMETRY instead of by a
                // convention. There is nothing left to guess.
                created[2] = sketches.Create3PointArc(topB[0], topB[1], topB[2],
                                                    bottomB[0], bottomB[1], bottomB[2],
                                                    crestB[0], crestB[1], crestB[2]);
                if (created[2] != null) segments++;
                created[3] = sketches.Create3PointArc(bottomA[0], bottomA[1], bottomA[2],
                                                    topA[0], topA[1], topA[2],
                                                    crestA[0], crestA[1], crestA[2]);
                if (created[3] != null) segments++;
            }
            catch (Exception ex)
            {
                error = "The sketch of the slot did not succeed: " + ex.Message;
                return false;
            }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
            }

            if (segments < 4)
            {
                error =
                    "The sketch of the slot has only " + segments + " segments out " +
                    "of the four that are needed: SolidWorks did not build " +
                    (segments < 2 ? "the lines" : "the arcs") + ".";
                return false;
            }
            return true;
        }

        /// <summary>
        /// How many walls a cylindrical cut went through: we count the
        /// cylindrical faces of the requested diameter that the feature left behind.
        ///
        /// A drilled wall leaves one cylindrical face. Two walls, two
        /// faces. It is a count, not an estimate, and it does not depend on how
        /// thick the part is — which in general is not known.
        ///
        /// The recipe to recognize a cylindrical face of a certain
        /// diameter is the one already in production in `HoleOperations`:
        /// `IFace2.IGetSurface().IsCylinder()` and `CylinderParams[6] * 2000`.
        ///
        /// **-1 when it could not be read** (entry Z-C8): `walls_drilled: 0`
        /// on a successful hole is a false statement. ZERO-E-ZERO: a
        /// live feature without faces (`GetFaces` null) drilled nothing.
        /// </summary>
        private static int DrilledWalls(IFeature feature, double diameterMm)
        {
            try
            {
                var faces = feature.GetFaces() as object[];
                if (faces == null) return 0;
                int howMany = 0;
                foreach (var o in faces)
                {
                    var surface = (o as IFace2)?.IGetSurface();
                    if (surface == null || !surface.IsCylinder()) continue;
                    var par = surface.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    if (Math.Abs(par[6] * 2000.0 - diameterMm) > 0.05) continue;
                    howMany++;
                }
                return howMany;
            }
            catch { return -1; }
        }

        /// <summary>All the part's solid bodies, as MassOf wants them.</summary>
        private static object[] AllSolidBodies(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return new object[0];
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            return bodies ?? new object[0];
        }

        /// <summary>
        /// Volume in mm3 and center of gravity in mm of a group of bodies.
        ///
        /// `IBody2.GetMassProperties(density)` returns a vector in which the first
        /// three numbers are the center of mass in METERS and the fourth is the volume
        /// in cubic meters. The center of gravity of several bodies is the average of their
        /// centers of gravity WEIGHTED BY VOLUME: taking the plain average would give a
        /// plausible and wrong number, and nobody would notice.
        /// </summary>
        private static bool MassOf(object[] bodies, out double volumeMm3, out double[] centerMm)
        {
            volumeMm3 = 0;
            centerMm = new double[3];
            if (bodies == null || bodies.Length == 0) return false;
            var readings = new List<double[]>();
            try
            {
                foreach (var c in bodies)
                    readings.Add((c as IBody2)?.GetMassProperties(1000.0) as double[]);
            }
            catch { return false; }
            return MassOfBodies(readings, out volumeMm3, out centerMm);
        }

        /// <summary>
        /// The volume (mm3) and the volume-weighted centre (mm) of the bodies
        /// whose IBody2.GetMassProperties readings are given, one per body; false
        /// when ANY of them cannot be used (Z-C21.8, finding M43).
        ///
        /// A BODY THAT CANNOT BE MEASURED IS NOT SKIPPED. Until library 0.144.0
        /// a missing, short or non-positive reading was skipped with a
        /// `continue`, and the sum of the others was given as the whole: a
        /// partial volume and a centre of part of the bodies, from which
        /// move_bodies then drew its diagnosis of the move. VolumeMm3 already
        /// had the rule (a body that gives no mass properties makes the whole
        /// null); this is the same rule. Kept apart from the document so that
        /// Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// </summary>
        internal static bool MassOfBodies(IList<double[]> readings, out double volumeMm3, out double[] centreMm)
        {
            volumeMm3 = 0;
            centreMm = new double[3];
            if (readings == null || readings.Count == 0) return false;
            var weighted = new double[3];
            double total = 0;
            foreach (var mp in readings)
            {
                if (mp == null || mp.Length < 4) return false;
                double v = mp[3] * 1e9;
                if (!(v > 0)) return false;
                total += v;
                for (int i = 0; i < 3; i++) weighted[i] += Units.Mm(mp[i]) * v;
            }
            volumeMm3 = total;
            for (int i = 0; i < 3; i++) centreMm[i] = weighted[i] / total;
            return true;
        }

        /// <summary>
        /// EXTRUDES A PROFILE ON A FACE THAT IS ALREADY THERE.
        ///
        /// WHY IT WAS MISSING, AND WHY IT IS THE BIGGEST HOLE IN THE CATALOG.
        /// Measured on 10 September 2026, listing the preconditions of all
        /// twenty-seven operations: on a normal part — not a
        /// metalwork frame — one could only REMOVE. Cylindrical cut,
        /// rectangular cut, slot, guided hole: they remove. Shell: hollows out.
        /// Mirror and move: they move. None added anything.
        ///
        /// That is: make a body, and from there on you can only dig into it.
        ///
        /// It is the best explanation we have found of the shop's
        /// complaint — «once it draws something it cannot
        /// modify it afterwards». Without this operation these cannot
        /// be made: a sealing lip, a spacer to rest
        /// something on, a fixing tab, a turret for a screw, a
        /// raised pad. That is half of what is inside an enclosure.
        ///
        /// THE ROAD IS ALREADY TRIED, AND THAT IS WHY IT COSTS LITTLE.
        /// Drawing a sketch on a chosen face is exactly what
        /// `rectangular_cut` and `slot` do, measured. Drawing any
        /// contour is what `extrude_profile` does, measured.
        /// Here the two pieces are put together and `FeatureExtrusion3` is called
        /// instead of `FeatureCut4`.
        ///
        /// THE DOUBT: `Merge`. With `merge` true the new material becomes
        /// part of the body that was there; with false a SEPARATE BODY is born, which
        /// is a different part and nobody says so — the tree grows
        /// the same, the shape looks the same, and the difference shows only by
        /// counting the bodies. Therefore here they are counted.
        /// </summary>
        private Outcome ExtrudeOnFace(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var contours = Contours(a, out string reason, out string _);
            if (contours == null) return Failed(reason);

            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double height = Num(a, "height_mm");
            if (height <= 0)
                return Failed("A boss of zero or negative height is not a boss.");
            bool merge = Bool(a, "merge", true);

            // THE ANCHOR (entry E18). Here there are no x_mm/y_mm: the point is the
            // CENTER of the contour, and the anchor holds for a single contour, round
            // or rectangular aligned with the face.
            var anchor = new Anchoring();
            double centerX = 0, centerY = 0;
            object rawAnchor;
            if (a.TryGetValue("anchor", out rawAnchor) && rawAnchor != null)
            {
                double halfU, halfV;
                if (!ContourCenterOnly(contours, out centerX, out centerY, out halfU, out halfV, out reason))
                    return Failed(reason);
                anchor = ReadAnchor(a, docState, face, axes, offsetM, centerX, centerY, halfU, halfV, out reason);
                if (anchor == null) return Failed(reason);
            }
            var anchorMeasurements = new Dictionary<string, object>();

            int bodiesBefore = CountSolidBodies(model);
            double? volumeBefore = VolumeMm3(model);
            if (bodiesBefore < 0 || volumeBefore == null)
                return Failed(VolumeNotReadBefore("making the boss"));

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("I could not select the face.");

            var sketches = model.SketchManager;
            bool addToDbBefore = false, displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }

            int segments = 0;
            bool drawn = false;
            try
            {
                sketches.InsertSketch(true);
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }
                // The contours arrive in coordinates of the FACE, not of the
                // plane: it is the same convention as `rectangular_cut`, and
                // the model must not know where the face sits in space.
                var created = new List<ISketchSegment>();
                segments = DrawContoursOnFace(
                    model, sketches, contours, axes, offsetM, out error, created);
                drawn = segments > 0;
                if (drawn && !anchor.Empty &&
                    !AnchorContour(model, contours[0], created, axes, offsetM, centerX, centerY,
                                      anchor, anchorMeasurements, out error))
                    drawn = false;
            }
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
                try { sketches.InsertSketch(false); } catch { }
            }
            if (!drawn) return Failed((error ?? "sketch not successful") + WithoutSketch(model));

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Extrude(model, height, merge: merge);
            if (feature == null)
                return Failed(
                    "The boss was not created. The sketch has " + segments +
                    " segments: if the contour is not closed, the extrusion does not start." +
                    WithoutSketch(model));

            // ── and now we count ────────────────────────────────────────
            string doesNotHold = MovementTest(model, anchor, feature);
            if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));

            int bodiesAfter = CountSolidBodies(model);
            double? volumeAfter = VolumeMm3(model);
            if (bodiesAfter < 0 || volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["height_mm"] = height,
                    ["merge"] = merge,
                    ["contours"] = contours.Count,
                    ["segments"] = segments,
                    ["bodies"] = NumberOrNull(bodiesAfter),
                    ["material_added_mm3"] = null,
                    ["volume_not_reread"] = NoteVolumeNotReread,
                }, FaceNormal(face));
            double added = volumeAfter.Value - volumeBefore.Value;

            if (merge && bodiesAfter != bodiesBefore)
                return Failed(
                    "The boss had to merge with the part and instead the bodies " +
                    "went from " + bodiesBefore + " to " + bodiesAfter + ": almost " +
                    "always it means that the profile does not touch the body. " +
                    "Withdraw it — " + Sentences.HowToWithdrawIt + ".");

            if (added <= 0)
                return Failed(
                    "The boss did not add material: the volume " +
                    (added < 0 ? "went down" : "stayed the same") + ". Usually " +
                    "it means that the extrusion went INTO the part " +
                    "instead of outward. Withdraw it — " + Sentences.HowToWithdrawIt + ".");

            var bossMeasurements = new Dictionary<string, object>
            {
                ["height_mm"] = height,
                ["merge"] = merge,
                ["contours"] = contours.Count,
                ["segments"] = segments,
                ["bodies"] = bodiesAfter,
                ["material_added_mm3"] = Math.Round(added, 2),
            };
            foreach (var entry in anchorMeasurements) bossMeasurements[entry.Key] = entry.Value;
            return Succeeded(feature, bossMeasurements, FaceNormal(face));
        }

        /// <summary>
        /// Draws contours on the plane of a FACE, not on a plane
        /// of the tree: every point goes through `PointInSketch`, which is the
        /// same road as the cuts on a face already measured.
        /// </summary>
        private int DrawContoursOnFace(
            IModelDoc2 model, ISketchManager sketches, List<Contour> contours,
            double[] axes, double offsetM, out string error, List<ISketchSegment> created = null)
        {
            error = null;
            int done = 0;
            foreach (var contour in contours)
            {
                if (contour.Circle)
                {
                    var c = PointInSketch(model, axes, offsetM,
                                              contour.Center[0], contour.Center[1], out error);
                    var border = PointInSketch(model, axes, offsetM,
                                                  contour.Center[0] + contour.Diameter / 2.0,
                                                  contour.Center[1], out error);
                    if (c == null || border == null) return 0;
                    // CreateCircle wants a point on the edge, not a radius:
                    // that way the circle stays round even if the sketch's plane
                    // is rotated with respect to the model.
                    var circle = sketches.CreateCircle(c[0], c[1], c[2], border[0], border[1], border[2]);
                    if (circle != null) { done++; if (created != null) created.Add(circle); }
                    continue;
                }
                foreach (var stroke in contour.Strokes)
                {
                    for (int i = 0; i < stroke.Points.Count - 1; i++)
                    {
                        var p1 = PointInSketch(model, axes, offsetM,
                                                   stroke.Points[i][0], stroke.Points[i][1], out error);
                        var p2 = PointInSketch(model, axes, offsetM,
                                                   stroke.Points[i + 1][0], stroke.Points[i + 1][1], out error);
                        if (p1 == null || p2 == null) return 0;
                        var line = sketches.CreateLine(p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]);
                        if (line != null) { done++; if (created != null) created.Add(line); }
                    }
                }
            }
            return done;
        }

        /// <summary>
        /// CHANGES A DIMENSION OF A FEATURE THAT IS ALREADY THERE.
        ///
        /// It is the most frequent request in the shop — «take that hole from
        /// M8 to M10», «make the thickness 3» — and until 10 September 2026 it was
        /// impossible: all twenty-eight operations added or
        /// removed, none MODIFIED. The only way was to delete and
        /// redo, that is, to lose everything that came after: holes,
        /// fillets, ribs built on that geometry.
        ///
        /// Signatures verified by reflection on interop 29.5.1.1:
        ///   IFeature.GetFirstDisplayDimension() / GetNextDisplayDimension(Object)
        ///   IDisplayDimension.GetDimension2(Int32 Index) -> Dimension
        ///   IDimension.SystemValue (in METERS) and
        ///   IDimension.SetSystemValue3(Double, Int32 WhichConfigurations, Object Config_names) -> Int32
        ///   IModelDoc2.EditRebuild3() -> Boolean
        ///
        /// THE CONFIGURATIONS ARE AN OPEN TRAP.
        /// `SetSystemValue3` wants to know ON WHICH configurations to write.
        /// Choosing «this one» on a part that has three means changing
        /// one and leaving the others as they were — and the part looks fine
        /// until someone opens the other configuration. Here we write on
        /// ALL of them, which is the only choice that does not leave the part half done.
        ///
        /// AND WE REBUILD, BECAUSE WITHOUT IT NOTHING CHANGES.
        /// Changing the value does not move the geometry: the
        /// rebuild does. Without `EditRebuild3` the number is new and the part
        /// is old — which is the perfect way to get someone to confirm
        /// a change that is not there.
        /// </summary>
        private Outcome SetDimension(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string dimensionName = Str(a, "dimension", "");
            if (string.IsNullOrWhiteSpace(dimensionName))
                return Failed(
                    "You did not say which dimension to change. The names of the " +
                    "dimensions are in the state of the document, inside each feature " +
                    "(for example «D1@Sketch1»).");

            double val;
            string error;
            if (!RequiredNum(a, "value_mm", out val, out error)) return Failed(error);

            var dimension = FindDimension(model, dimensionName, out string location);
            if (dimension == null)
                return Failed(
                    "I cannot find the dimension «" + dimensionName + "» in this document." +
                    (location == null ? "" : " There are: " + location + "."));

            // ── A READ-ONLY DIMENSION IS NOT OVERRIDDEN (entry Z-C21.2, M8) ──
            //
            // IDimension~SetSystemValue3.html, Remarks: «This method allows you
            // to change the value of a read-only dimension. You can use
            // IDimension::ReadOnly to determine if a dimension is read-only.»
            // Whoever made the dimension read-only did it on purpose, and
            // SetSystemValue3 changed it all the same without saying so. The same for
            // a DRIVEN dimension (IDimension.DrivenState, swDimensionDriven = 1,
            // swconst inventory): it is a reference measure, it commands
            // nothing, and writing over it would not change the part.
            bool isReadOnly = false;
            try { isReadOnly = dimension.ReadOnly; } catch { }
            if (isReadOnly)
                return Failed(
                    "The dimension «" + dimensionName + "» is read-only: someone locked it on purpose, " +
                    "and I do not override it even if SOLIDWORKS would allow it. If it has to " +
                    "be changed, first remove the read-only flag from the dimension in SOLIDWORKS. " +
                    "I touched nothing.");
            const int Guided = 1;   // swDimensionDrivenState_e.swDimensionDriven
            int comeGuida = 0;
            try { comeGuida = dimension.DrivenState; } catch { }
            if (comeGuida == Guided)
                return Failed(
                    "The dimension «" + dimensionName + "» is driven (reference): it measures the part, " +
                    "it does not command it. Change the dimension that drives it instead. I " +
                    "touched nothing.");

            // ── DEGREES OR MILLIMETERS: THE DIMENSION SAYS SO (entry Z-C21.2, M3) ─────
            //
            // IDimension~GetType.html: «Type of dimension as defined by
            // swDimensionParamType_e» — DoubleLinear = 0, DoubleAngular = 1,
            // Integer = 2 (swconst inventory). Up to library 0.136.0 every
            // dimension was converted from mm: «30» on an angle became 0.03 rad
            // (1.7 degrees), and the reread, wrong in the same way, said it
            // had taken. The document state writes the angular dimensions in
            // `deg` (DocumentState.cs, same GetType): here the value is read
            // in the same unit in which the model read it.
            const int AngularDimension = 1, WholeDimension = 2;
            int dimensionType = 0;
            try { dimensionType = dimension.GetType(); } catch { }
            if (dimensionType == WholeDimension)
                return Failed(
                    "The dimension «" + dimensionName + "» is a whole number (for example the copies of a pattern), " +
                    "not a measurement: I do not change it with «value_mm». I touched " +
                    "nothing.");
            bool angular = dimensionType == AngularDimension;
            Func<double, double> fromSystem = angular ? (Func<double, double>)Units.Degrees : Units.Mm;
            Func<double, double> toSystem = angular ? (Func<double, double>)Units.Rad : Units.M;
            string units = angular ? "degrees" : "mm";

            double before = fromSystem(ApiHelpers.ReadDimension(dimension));
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            if (bodiesBefore < 0)
                return Failed(
                    "I cannot count the bodies of the part before changing the dimension: without " +
                    "that, I could not tell you whether the change breaks it. I touched nothing.");

            // swSetValueInConfiguration_e.swSetValue_InAllConfigurations = 2
            const int inAllConfigurations = 2;
            int outcome;
            var featuresBefore = FacesPerFeature(model);
            int openBefore = ClosedOpenings(model);

            try
            {
                outcome = dimension.SetSystemValue3(toSystem(val), inAllConfigurations, null);
            }
            catch (Exception ex)
            {
                return Failed("The dimension could not be changed: " + ex.Message);
            }
            if (outcome != 0)
                return Failed(
                    "SolidWorks refused the value " + val + " " + units + " for «" +
                    dimensionName + "» (code " + outcome + ": " + ErrorCodeDecoder.SetValueStatus(outcome) + "). " +
                    SetValueRefusalCause(outcome));

            bool rebuilt;
            try { rebuilt = model.EditRebuild3(); }
            catch (Exception ex) { return Failed("The rebuild failed: " + ex.Message); }

            // ── and now we look whether it really happened ────────────────
            double after = fromSystem(ApiHelpers.ReadDimension(dimension));
            if (Math.Abs(after - val) > 0.001)
                return Failed(
                    "The dimension «" + dimensionName + "» still says " +
                    after.ToString("0.###") + " " + units + " instead of " + val + ": the " +
                    "value did not take. The part did not change.");

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);

            // AN EDIT THAT MOVES NOTHING IS A SUSPICION, NOT A
            // SUCCESS. If the dimension changed and the volume did not, almost always
            // it means that dimension does not command the geometry the person
            // asking had in mind — and confirming it as «done» would be a
            // convenient lie.
            //
            // Unchanged can be said only of two READ volumes (entry Z-C8): with two
            // failed readings the zero from before and the zero from after were equal,
            // and the note said «the volume does not».
            bool unchangedVolume = volumeBefore.HasValue && volumeAfter.HasValue &&
                               Math.Abs(volumeAfter.Value - volumeBefore.Value) < 0.01;
            if (!rebuilt)
                return Failed(
                    "The dimension changed but the rebuild did not succeed: " +
                    "the part is in error. Look at it before going on." +
                    RemainsInPart);

            if (bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                return Failed(
                    "After the change the part has " + bodiesAfter + " bodies instead " +
                    "of " + bodiesBefore + ": the new dimension split or merged " +
                    "something." + RemainsInPart);

            // AN OPENING THAT LEAVES THE MATERIAL (entry E18, part c).
            //
            // Measured in the regression, case r16: a centered plate of
            // 200 brought to 150 shrinks ON ONE SIDE ONLY, and the slot 120 long
            // at the center comes out of the side. Clean rebuild, one body,
            // no error, the volume changes slightly — and a closed slot
            // became an open notch on the edge: another part.
            //
            // We look at the TOPOLOGY, not the geometry: the closed openings
            // of the planar faces (the inner loops) were N and now there are
            // fewer. It means that a hole or a slot that was inside a
            // face now touches its edge — or that two openings have
            // merged into one. Both things change the part, and neither
            // was wanted by whoever asked to change a measure.
            //
            // THE REFUSAL PUTS THE PREVIOUS VALUE BACK. The dimension at this point
            // is already written: saying «no» and leaving it would be telling the truth and
            // keeping the damage. `before` is rewritten, the model is rebuilt, and the
            // volume is REREAD: if it does not add up, we say so.
            int openAfter = ClosedOpenings(model);
            if (openBefore >= 0 && openAfter >= 0 && openAfter < openBefore)
            {
                string restored = PutBackDimension(model, dimension, before, volumeBefore, toSystem, units);
                return Failed(
                    "With «" + dimensionName + "» at " + val.ToString("0.###") + " " + units + " an opening " +
                    "goes out of the material: the closed openings on the planar faces go from " +
                    openBefore + " to " + openAfter + ". A hole or a slot that was inside " +
                    "the part now opens its border, or two openings merged into one." +
                    restored + " If that is really what you want, first move the opening, then change " +
                    "the dimension.");
            }

            // A CHAMFER OR A FILLET THAT NO LONGER FITS (26/9/2026, case r05
            // of the regression — the last of the silences).
            //
            // Measured: plate 20 thick with a 2 chamfer on a long edge,
            // thickness brought to 1. The chamfer does not fit, and SOLIDWORKS DOES NOT
            // FAIL: it rebuilds clean, no feature in error, and it
            // eats a millimeter of width. The same with the little bench
            // (the historical probe of 0.89): two 8 fillets on top of a plate, then
            // width to 10 or thickness to 6 — succeeded, zero errors, a part that
            // is not what the fillets describe.
            //
            // The sign was already there, and it was being thrown away: the BASE feature loses
            // a whole face — the one that chamfer or fillet now take
            // entirely — and `ChangedFeatures` wrote it in a note, with the
            // wrong explanation («a blind feature that became through»).
            // Here we look at the same thing with the right question: a feature
            // that is NOT a cut has lost faces, and further along in the tree is there
            // a chamfer or a fillet? Then it is that one that does not fit, and it is
            // refused as for the openings: previous value, and volume reread.
            //
            // THE CUTS STAY OUT on purpose: a pocket that loses its bottom has
            // become through, and that may be just what was wanted (the
            // note below says it and does not refuse).
            string whoDoesNotFit = ChamferThatDoesNotFit(model, featuresBefore, FacesPerFeature(model));
            if (whoDoesNotFit != null)
            {
                string restored = PutBackDimension(model, dimension, before, volumeBefore, toSystem, units);
                return Failed(
                    "With «" + dimensionName + "» at " + val.ToString("0.###") + " " + units + " " + whoDoesNotFit +
                    ": it no longer fits. SOLIDWORKS does not report it as an error — it rebuilds " +
                    "and changes the shape by itself — but the part that comes out is not what the feature " +
                    "describes." + restored + " If you want that measure, first change or remove the chamfer or " +
                    "the fillet, then the dimension.");
            }

            var measurements = new Dictionary<string, object>
            {
                ["dimension"] = dimensionName,
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["volume_changed_mm3"] = NumberOrNull(volumeAfter - volumeBefore, 2),
            };
            // AN ANGULAR DIMENSION IS NOT REPORTED IN «_mm» (M3): a field that
            // is called mm and carries degrees is the same defect said in another
            // place. The new names are in English, like the next ones of the bridge.
            if (angular)
            {
                measurements["before_deg"] = Math.Round(before, 3);
                measurements["after_deg"] = Math.Round(after, 3);
            }
            else
            {
                measurements["before_mm"] = Math.Round(before, 3);
                measurements["now_mm"] = Math.Round(after, 3);
            }
            if (volumeAfter == null || bodiesAfter < 0) measurements["volume_not_reread"] = NoteVolumeNotReread;

            // THE TREE CAN BE CHANGED, NOT JUST THE MEASURES.
            //
            // Measured: 200x100x20 plate with a blind pocket 5 deep,
            // thickness brought to 3. The pocket becomes THROUGH — the
            // cut feature goes from 2 faces to 1, because the bottom is
            // no longer there — and nobody says so: GetErrorCode2 answers 0,
            // IsWarning is false, the volume matches, the bodies are still one.
            //
            // A blind seat and a passage are two different parts. Whoever
            // asked to thin the plate maybe wanted it, so it is not
            // refused; but they must be able to read it here instead of discovering it in the
            // shop.
            var changed = ChangedFeatures(
                featuresBefore, FacesPerFeature(model), measurements);

            measurements["note"] = unchangedVolume
                ? "the dimension changed but the volume did not: check that it is the right one"
                : changed;

            return Succeeded(null, measurements);
        }

        /// <summary>
        /// How many CLOSED openings the part has: the inner loops of the
        /// PLANAR faces, counted over all the solid bodies. -1 if it cannot be
        /// counted, and then the E18 guard stays quiet instead of inventing.
        ///
        /// ONLY THE PLANAR FACES. A cylindrical face of a through hole has
        /// two loops, one per mouth, and which of the two SOLIDWORKS calls
        /// «outer» on a periodic surface is not something to
        /// rest a guard on. On a planar face an inner loop is a
        /// hole in that face, and nothing else.
        /// </summary>
        private static int ClosedOpenings(IModelDoc2 model)
        {
            int n = 0;
            try
            {
                var bodies = (model as IPartDoc)?.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null) return -1;
                foreach (var c in bodies)
                {
                    var face = (c as IBody2)?.GetFirstFace() as IFace2;
                    while (face != null)
                    {
                        bool planar = false;
                        try { planar = (face.GetSurface() as ISurface)?.IsPlane() == true; } catch { }
                        if (planar)
                        {
                            var rings = face.GetLoops() as object[];
                            if (rings != null)
                                foreach (var o in rings)
                                {
                                    var ring = o as ILoop2;
                                    if (ring != null && !ring.IsOuter()) n++;
                                }
                        }
                        face = face.GetNextFace() as IFace2;
                    }
                }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// Looks for a dimension by name in the whole tree.
        ///
        /// The name may be the full one («D1@Sketch1») or the short one
        /// («D1»): the model reads the full one in the state, but a person
        /// writing by hand uses the short one, and refusing for that would be pedantic.
        /// If it does not find it, it lists the names that exist: a «not found» without
        /// the list forces guessing.
        /// </summary>
        private static IDimension FindDimension(IModelDoc2 model, string itemName, out string available)
        {
            available = null;
            var partDoc = model as IPartDoc;
            if (partDoc == null) return null;
            // ONLY ONCE PER DIMENSION. An absorbed sketch appears twice
            // in the tree — as a sketch and inside the feature that
            // took it — and its dimension is met twice.
            var names = new List<string>();
            var already = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IFeature f = partDoc.IFirstFeature();
            while (f != null)
            {
                object display = null;
                try { display = f.GetFirstDisplayDimension(); } catch { }
                while (display != null)
                {
                    IDimension d = null;
                    try { d = (display as IDisplayDimension)?.GetDimension2(0); } catch { }
                    if (d != null)
                    {
                        string full = null, shorter = null;
                        try { full = d.FullName; shorter = d.Name; } catch { }
                        if (!string.IsNullOrEmpty(full))
                        {
                            if (names.Count < 12 && already.Add(full)) names.Add(full);

                            // THREE FORMS, AND ALL THREE MUST WORK.
                            //
                            //   D1                          the short name
                            //   D1@Sketch1                 name and feature
                            //   D1@Sketch1@Part286.Part   as the state writes it
                            //
                            // The second was missing, and the error message
                            // took it for granted («for example «D1@Sketch1»»):
                            // the message said the wrong thing.
                            //
                            // And it is not a convenience detail: the form with
                            // the document at the end contains the document's TITLE,
                            // so a bench case written with
                            // that one cannot be rerun on another part.
                            // Without the middle form, the tests that change
                            // a dimension are not repeatable.
                            string withoutDocument = full;
                            int lastAtSign = full.LastIndexOf('@');
                            if (lastAtSign > 0)
                                withoutDocument = full.Substring(0, lastAtSign);

                            if (string.Equals(full, itemName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(shorter, itemName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(withoutDocument, itemName, StringComparison.OrdinalIgnoreCase))
                            {
                                return d;
                            }
                        }
                    }
                    object next = null;
                    try { next = f.GetNextDisplayDimension(display); } catch { }
                    display = next;
                }
                try { f = (IFeature)f.GetNextFeature(); } catch { break; }
            }
            if (names.Count > 0) available = string.Join(", ", names);
            return null;
        }

        private Outcome FilletEdges(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double radius = Num(a, "radius_mm");
            int howMany;
            string why;
            // The volume before, for the check of M37 in RunFillet; read
            // before the edges are selected (GetMassProperties on the bodies,
            // which does not touch the selection).
            double? volumeBefore = VolumeMm3(model);
            if (!SelectEdges(model, a, docState, radius, out howMany, out why))
                return Failed(why);
            return RunFillet(model, radius, howMany, volumeBefore);
        }

        /// <summary>
        /// What to do about a value that IDimension.SetSystemValue3 refused, by
        /// its status (Z-C21.8, finding L5-6). IDimension~SetSystemValue3.html:
        /// «Success indicator value as defined in swSetValueReturnStatus_e».
        /// Until the review of this lot every refusal said «usually a dimension
        /// governed by an equation or a relation», also for a value out of range,
        /// a frozen feature or a model not loaded. The status name is already in
        /// the message (ErrorCodeDecoder.SetValueStatus); this is the remedy that
        /// goes with it. Only what the enum name says is said: none of these
        /// statuses was provoked on purpose by a probe. No interop type in the
        /// signature: Oce.Solid.Agent.ContractTests calls it by reflection.
        /// </summary>
        internal static string SetValueRefusalCause(int status)
        {
            switch (status)
            {
                case (int)swSetValueReturnStatus_e.swSetValue_InvalidValue:
                    return "SOLIDWORKS does not take this value for this dimension: ask for a value the " +
                           "geometry allows (not zero or negative for a length).";
                case (int)swSetValueReturnStatus_e.swSetValue_DrivenDimension:
                    return "It is a DRIVEN dimension: its value follows the geometry, it is not set. Change the " +
                           "dimension that drives it.";
                case (int)swSetValueReturnStatus_e.swSetValue_ModelNotLoaded:
                    return "The model that owns the dimension is not loaded: open it, then ask again.";
                case (int)swSetValueReturnStatus_e.swSetValue_FrozenFeatureOwner:
                    return "The feature that owns the dimension is FROZEN: move the freeze bar below it, then " +
                           "ask again.";
                case (int)swSetValueReturnStatus_e.swSetValue_Failure:
                    return "SOLIDWORKS gives no reason: check whether an equation or a relation drives the " +
                           "dimension, then ask again.";
                default:
                    return "Read the status above, then ask again.";
            }
        }

        /// <summary>
        /// A FILLET OR A CHAMFER MUST MOVE THE VOLUME OF THE PART (Z-C21.8,
        /// finding M37): the fault in words, or null. fillet_edges and
        /// chamfer_edges had no check on the volume at all, while
        /// variable_fillet had one: a feature that leaves the volume where it
        /// was has worked nothing, and the tree says otherwise. The SIGN is not
        /// judged: on a concave edge a fillet or a chamfer ADDS material. A
        /// volume that could not be read, before or after, is not a fault here:
        /// the answer says «not read» instead. Kept apart from the document so
        /// that Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// </summary>
        internal static string EdgeFeatureVolumeFault(double? volumeBefore, double? volumeAfter)
        {
            if (volumeBefore == null || volumeAfter == null) return null;
            if (Math.Abs(volumeAfter.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) >= 0.01) return null;
            return "the volume of the part did not change (" +
                   volumeAfter.GetValueOrDefault().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm3 before and after)";
        }

        /// <summary>
        /// The volume check of <see cref="EdgeFeatureVolumeFault"/> applied to a
        /// fillet or a chamfer just born: null when it passes (the change, or
        /// the note that the volume was not read, goes into
        /// <paramref name="measures"/>), the refusal otherwise.
        /// </summary>
        private string EdgeFeatureVolumeCheck(
            IModelDoc2 model, string what, double? volumeBefore, Dictionary<string, object> measures)
        {
            try { model.EditRebuild3(); } catch { }
            double? volumeAfter = VolumeMm3(model);
            string unchanged = EdgeFeatureVolumeFault(volumeBefore, volumeAfter);
            if (unchanged != null)
                return "The " + what + " was created, but " + unchanged + ": it worked nothing, and the tree says " +
                       "otherwise. It is still in the part: undo it (" + Sentences.HowToWithdrawIt + ").";
            if (volumeBefore == null || volumeAfter == null)
                measures["volume_not_read"] = "The volume of the part could not be read " +
                                              (volumeBefore == null ? "before" : "after") +
                                              " the " + what + ", so the " + what + " was not checked against it.";
            else
                measures["material_change_mm3"] = Math.Round(volumeAfter.GetValueOrDefault() - volumeBefore.GetValueOrDefault(), 2);
            return null;
        }

        /// <summary>
        /// The chamfer: the twin of the fillet, and the reason why the
        /// selection of the edges was extracted into a single method.
        ///
        /// In metalwork the chamfer is a specification item: it is chamfered to
        /// remove the burr, to make a part fit, to prepare a
        /// weld bevel. Up to 0.30.2 the assistant knew how to
        /// round and did not know how to chamfer, and whoever asked for it heard
        /// a no.
        ///
        /// SIGNATURE VERIFIED by reflection on interop 29.5.1.1:
        ///   IFeatureManager.InsertFeatureChamfer(Int32 Options, Int32
        ///       ChamferType, Double Width, Double Angle, Double OtherDist,
        ///       Double VertexChamDist1, Double VertexChamDist2,
        ///       Double VertexChamDist3) -> Feature
        ///   swChamferType_e: AngleDistance=1, DistanceDistance=2, Vertex=3,
        ///       EqualDistance=16
        ///   swFeatureChamferOption_e: FlipDirection=1, KeepFeature=2,
        ///       TangentPropagation=4, PropagateFeatToParts=8
        ///
        /// WARNING, and it is written in the RAG among «the seven APIs that seemed
        /// right»: the chamfer enums are TWO and have the same numbers
        /// with different meanings — `swSketchChamferType_e` is for the
        /// sketch, `swChamferType_e` for the feature. Here the second is needed.
        ///
        /// THE TWO SIDES GO IN Width AND OtherDist, NOT WHERE THE GUIDE PUTS
        /// THEM. IFeatureManager~InsertFeatureChamfer.html gives Width only for
        /// swChamferAngleDistance and OtherDist only for swChamferEqualDistance,
        /// and says «VertexChamDist1 — If ChamferType is swChamferDistanceDistance
        /// or swChamferVertex, then specify a value for the distance on first
        /// side». Measured otherwise: Bench/cases/chamfer/chamfer_3x6_bevel
        /// asks 3 and 6 on a 100 mm edge and gets 119100 mm3 exactly (120000
        /// minus 3 x 6 / 2 x 100); an ignored second side would leave 119550,
        /// and a SOLIDWORKS reading VertexChamDist1/2 = 0 would make no chamfer
        /// at all (audit finding L7-2, not real). Moving to VertexChamDist1/2
        /// means running that case before and after.
        /// </summary>
        private Outcome ChamferEdges(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double side = Num(a, "leg_mm");
            double angle = Num(a, "angle_deg", 45);
            double secondSide = Num(a, "second_leg_mm", 0);
            bool twoSides = secondSide > 0;

            int howMany;
            string why;
            // The volume before, for the check of M37 below (EdgeFeatureVolumeCheck).
            double? volumeBefore = VolumeMm3(model);
            // The limit «half of the edge» holds for the chamfer too: the longer
            // of the two sides is the one that cannot fit.
            if (!SelectEdges(model, a, docState,
                                  Math.Max(side, secondSide), out howMany, out why))
                return Failed(why);

            int typ = twoSides
                ? (int)swChamferType_e.swChamferDistanceDistance
                : (int)swChamferType_e.swChamferAngleDistance;

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertFeatureChamfer(
                    Options: (int)swFeatureChamferOption_e.swFeatureChamferTangentPropagation,
                    ChamferType: typ,
                    Width: Units.M(side),
                    Angle: Units.Rad(angle),
                    OtherDist: twoSides ? Units.M(secondSide) : 0,
                    VertexChamDist1: 0,
                    VertexChamDist2: 0,
                    VertexChamDist3: 0) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("The chamfer did not succeed: " + ex.Message);
            }
            if (feature == null)
                return Failed(
                    "The chamfer was not created. Usually it is the size: " +
                    "a side bigger than the material around there cannot " +
                    "be removed.");

            var measurements = new Dictionary<string, object>
            {
                ["leg_mm"] = side,
                ["edges"] = howMany,
            };
            SelectedEdgesAndFaces(feature, howMany, measurements);
            if (twoSides) measurements["second_leg_mm"] = secondSide;
            else measurements["angle_deg"] = angle;
            // After the counts, which SelectedEdgesAndFaces reads right after
            // creation as the probe did: the check rebuilds before measuring.
            string chamferUnchanged = EdgeFeatureVolumeCheck(model, "chamfer", volumeBefore, measurements);
            if (chamferUnchanged != null) return Failed(chamferUnchanged);
            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// A new reference plane, parallel to one of the three base planes.
        ///
        /// BASE PLANES ONLY, AND THE REASON LIES IN THE SERVER'S VALIDATOR.
        /// the validation function ALWAYS imposes one of `face_uid` and `face_ref` as soon as
        /// that property appears in an operation's schema. If a face were
        /// also declared here as an alternative reference, «top
        /// plane, 600 mm» alone would no longer be accepted: a face
        /// identifier would be needed anyway. Version 1 handles the case
        /// the client asked for by name.
        ///
        /// THE CALL (Z-C21.7, L8-1): IFeatureManager.InsertRefPlane with the
        /// base plane selected with mark 0 (the first reference entity), the
        /// Distance constraint, OptionFlip when the distance is negative, and
        /// the ABSOLUTE distance: ApiHelpers.SelectOffsetPlaneBase and
        /// ApiHelpers.InsertOffsetPlane, where the guide pages and the probe
        /// numbers are. Until Z-C21.7 this was the obsolete
        /// IModelDoc2.CreatePlaneAtOffset3, because InsertRefPlane had been
        /// measured to make no plane on 10/9/2026; the InsertRefPlane probe of
        /// Z-C21.3 (28/9/2026) showed that measure was
        /// made with mark 1 (H1: no plane), while mark 0 makes the plane where
        /// it belongs (G1 top +100, G4 top -100, G6 front -250). The sign of
        /// the distance is measured too: a signed distance puts the plane on
        /// the base plane (G5: (0, 0, 0) for -100), hence OptionFlip.
        /// </summary>
        private Outcome CreatePlane(IModelDoc2 model, Dictionary<string, object> a)
        {
            string plane = Str(a, "plane", "front");
            double distance;
            string error;
            if (!RequiredNum(a, "distance_mm", out distance, out error))
                return Failed(error);

            model.ClearSelection2(true);
            IFeature reference = FindBasePlane(model, plane);
            if (reference == null)
                return Failed($"I did not find the plane «{plane}» in the tree.");
            // The PLANE is selected by name, with mark 0; the tree entry through
            // IFeature.Select2 is only the fallback (the probe's G3 measured it
            // working with InsertRefPlane too).
            if (!ApiHelpers.SelectOffsetPlaneBase(model, reference))
                return Failed($"I could not select the plane «{plane}».");

            // WHAT SOLIDWORKS REALLY HOLDS WHEN WE CALL. InsertRefPlane works on
            // what is selected WITH THE RIGHT MARK, and with the wrong mark it
            // returns null and says nothing (H1). These lines cost nothing and
            // turn a "not created" into a diagnosis.
            string comEra = "";
            try
            {
                var sel = model.SelectionManager as ISelectionMgr;
                int howMany = sel == null ? -1 : sel.GetSelectedObjectCount2(-1);
                int mark = (sel != null && howMany > 0) ? sel.GetSelectedObjectMark(1) : -1;
                string itemName = "";
                try { itemName = reference.Name; } catch { }
                int typ = (sel != null && howMany > 0) ? sel.GetSelectedObjectType3(1, -1) : -1;
                comEra = " [selected " + howMany + ", mark " + mark +
                         ", type " + typ + ", plane «" + itemName + "»]";
            }
            catch { }

            // THE SIDE IS A BIT, NOT A SIGN (probe G4/G5): the absolute
            // distance, with OptionFlip when the distance asked is negative.
            int planesBefore = CountPlanes(model);
            var namesBefore = FeatureNamesNow(model);
            IFeature feature = null;
            int planesBorn = -1;
            try
            {
                object raw = ApiHelpers.InsertOffsetPlane(model, distance);
                feature = raw as IFeature;
                // The return value is a RefPlane; the probe found it to be an
                // IFeature as well. When the cast fails the plane is the ONE
                // RefPlane born in the tree since the call (FeatureBorn, scheme
                // S7, Z-C21.5), never the last feature of the tree: on a
                // sheet-metal part that is the flat pattern (finding M9).
                if (feature == null)
                    feature = FeatureBorn(model, namesBefore, "RefPlane", out planesBorn);
            }
            catch (Exception ex)
            {
                return Failed("The plane was not created: " + ex.Message + comEra);
            }
            if (feature == null && planesBorn > 1)
                return Failed(
                    "InsertRefPlane returned no plane and " + planesBorn + " reference planes were born in the " +
                    "tree: I do not pick one of them for you. Look at the part before going on." + comEra);
            if (feature == null && namesBefore == null)
                return Failed(
                    "InsertRefPlane returned no plane, and the feature tree could not be read to find one it " +
                    "may have made. Look at the part before going on." + comEra);
            if (feature == null)
                return Failed(
                    "The plane was not created, and none appeared " +
                    "in the tree." + comEra);

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["starting_plane"] = plane,
                ["planes_before"] = planesBefore,
                ["distance_mm"] = distance,
            });
        }

        /// <summary>
        /// A new axis, along the center of a cylindrical or conical face.
        ///
        /// It is the operation that was missing so that «circular_pattern» would stop
        /// being a dead end: it demands an axis that already exists, and up
        /// to 0.30.2 no operation of the catalog created one.
        ///
        /// SIGNATURES CHECKED on the interop 29.5.1.1:
        ///   IModelDoc2.InsertAxis2(Boolean AutoSize) -> Boolean
        ///   ISurface.IsCylinder(), ISurface.IsCone()
        ///
        /// IT RETURNS A BOOLEAN, NOT A FEATURE, like the shell. Until Z-C21.5
        /// the axis was taken as the last feature of the tree
        /// (IFeatureByPositionReverse(0)) and refused when that was not a
        /// «RefAxis»: on a sheet-metal part the last feature is the flat
        /// pattern, and the operation refused an axis it had made and left it
        /// in the tree (finding L8-6). The axis is now the ONE RefAxis born
        /// since the call (FeatureBorn, scheme S7), wherever SOLIDWORKS put it.
        /// «RefAxis» is the string DocumentState recognises axes by, so an axis
        /// that passes here shows in the state at the next step.
        /// </summary>
        private Outcome CreateAxis(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out string reason);
            if (face == null) return Failed(reason);

            bool round;
            try
            {
                var surface = face.IGetSurface();
                round = surface != null && (surface.IsCylinder() || surface.IsCone());
            }
            catch (Exception ex)
            {
                return Failed("I cannot read the face: " + ex.Message);
            }
            if (!round)
                return Failed(
                    "This face is neither cylindrical nor conical: the axis is " +
                    "made from the center of a hole or of a shaft. For " +
                    "the axis at the intersection of two planes this version " +
                    "has no operation.");

            model.ClearSelection2(true);
            var entity = face as IEntity;
            if (entity == null || !entity.SelectByMark(false, SelectionMarks.AxisFace))
                return Failed("I could not select the face.");

            var namesBefore = FeatureNamesNow(model);
            if (namesBefore == null)
                return Failed(
                    "I cannot read the feature tree before making the axis, and without it I could not tell " +
                    "the new axis from the ones already there. Nothing was changed.");

            bool created;
            try { created = model.InsertAxis2(true); }
            catch (Exception ex) { return Failed("The axis was not created: " + ex.Message); }
            if (!created)
                return Failed(
                    "The axis was not created. Usually it is the face: it is not " +
                    "cylindrical enough, or it is the wrong one.");

            // The ONE axis born since the call, not the last feature of the tree
            // (finding L8-6, scheme S7).
            int axesBorn;
            IFeature feature = FeatureBorn(model, namesBefore, "RefAxis", out axesBorn);
            if (feature == null)
                return Failed(
                    (axesBorn < 0
                        ? "SOLIDWORKS says it made the axis, but I cannot read the feature tree to find it"
                        : axesBorn == 0
                            ? "SOLIDWORKS says it made the axis, but no new RefAxis is in the feature tree"
                            : "SOLIDWORKS says it made the axis, and " + axesBorn + " new RefAxis features are in the tree") +
                    ": I do not confirm an axis I cannot point to. Look at the part before trusting the rest of the plan.");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["axis"] = feature.Name,
            });
        }

        /// <summary>
        /// Repeats a feature in a row, or in a grid in two directions.
        ///
        /// WHY IT MATTERS. Eight holes in a row, without this, take eight
        /// calls with eight coordinates computed by hand: slow, and every
        /// coordinate is a chance to make a mistake. Worse: the finished part
        /// does not DECLARE that those holes are a series, so changing their
        /// pitch means redoing them all.
        ///
        /// SIGNATURE VERIFIED on interop 29.5.1.1 — it is already the highest:
        ///   IFeatureManager.FeatureLinearPattern5(Num1, Spacing1, Num2,
        ///       Spacing2, FlipDir1, FlipDir2, DName1, DName2,
        ///       GeometryPattern, VaryInstance, HasOffset1, HasOffset2,
        ///       CtrlByNum1, CtrlByNum2, FromCentroid1, FromCentroid2,
        ///       RevOffset1, RevOffset2, Offset1, Offset2, D2PatternSeedOnly,
        ///       SyncSubAssemblies) -> Feature
        ///
        /// THE MARKS ARE MEASURED, NOT GUESSED (Z-C21.9, finding L7-5, not
        /// real). Mark 1 for the first direction, mark 2 for the second and
        /// mark 4 for the feature (SelectionMarks.LinearPatternDirection1/2,
        /// LinearPatternFeatures; IFeatureManager~FeatureLinearPattern5.html,
        /// «1 to mark Direction 1», «2 to mark Direction 2»). Reflection cannot
        /// check them, they are behaviour; the Z-C21.3 probe of L7-5 and M16
        /// (28/9/2026) did: a row along an edge and along an axis of create_axis
        /// (L0-L5, six ways of selecting), and a grid on two edges and on two
        /// axes (G0-G3), each with the expected removed volume and the new hole
        /// centres where they should be. Until Z-C21.9 this note called mark 2
        /// an analogy still to be measured on a grid.
        ///
        /// GeometryPattern stays FALSE. Copying only the shape is faster
        /// and more fragile: a threaded hole repeated that way loses the thread
        /// on the copies, and no error says so.
        /// </summary>
        private Outcome LinearPattern(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double? volumeBefore; int bodiesBefore, facesBefore;
            Snapshot(model, out volumeBefore, out bodiesBefore, out facesBefore);
            if (volumeBefore == null || bodiesBefore < 0 || facesBefore < 0)
                return Failed(VolumeNotReadBefore("making the copy"));

            double pitch1, instances1Raw;
            string error;
            if (!RequiredNum(a, "pitch1_mm", out pitch1, out error)) return Failed(error);
            if (!RequiredNum(a, "instance_count1", out instances1Raw, out error))
                return Failed(error);
            int instances1 = (int)instances1Raw;


            string axis2 = Str(a, "axis2_uid", "");
            bool grid = !string.IsNullOrEmpty(axis2);
            int instances2 = 1;
            double pitch2 = 0;
            if (grid)
            {
                double n2;
                if (!RequiredNum(a, "instance_count2", out n2, out error))
                    return Failed("You gave «axis2_uid» but not «instance_count2»: " + error);
                instances2 = (int)n2;
                if (instances2 < 2)
                    return Failed(
                        "«instance_count2» must be at least 2. For a single row, " +
                        "leave «axis2_uid» out instead of setting 1.");
                if (!RequiredNum(a, "pitch2_mm", out pitch2, out error))
                    return Failed("You gave «axis2_uid» but not «pitch2_mm»: " + error);
            }

            model.ClearSelection2(true);
            if (!SelectAxis(model, a, "axis1", docState, SelectionMarks.LinearPatternDirection1, append: false, out error)) return Failed(error);
            // The second direction goes through the SAME helper, with Mark 2:
            // before it had a line of its own that accepted only an axis, and since
            // the direction can be an edge the two roads diverged —
            // a grid with direction 1 on an edge and direction 2 not.
            if (grid && !SelectAxis(model, a, "axis2", docState, SelectionMarks.LinearPatternDirection2, append: true, out error))
                return Failed("Direction 2: " + error);

            if (!SelectFeature(model, a, out error)) return Failed(error);
            // Before building: creating the pattern clears the selection.
            var seed = SelectedSeed(model, SelectionMarks.LinearPatternFeatures);

            IFeature feature;
            try
            {
                feature = model.FeatureManager.FeatureLinearPattern5(
                    Num1: instances1,
                    Spacing1: Units.M(pitch1),
                    Num2: instances2,
                    Spacing2: Units.M(pitch2),
                    FlipDir1: Bool(a, "flip1", false),
                    FlipDir2: Bool(a, "flip2", false),
                    DName1: "",
                    DName2: "",
                    GeometryPattern: false,
                    VaryInstance: false,
                    HasOffset1: false,
                    HasOffset2: false,
                    CtrlByNum1: true,
                    CtrlByNum2: true,
                    FromCentroid1: false,
                    FromCentroid2: false,
                    RevOffset1: false,
                    RevOffset2: false,
                    Offset1: 0,
                    Offset2: 0,
                    // false = FULL grid: every instance of direction 1 is
                    // repeated on direction 2 too. true would make a cross, which repeats
                    // only the original. For a plate drilled in a matrix pattern you need
                    // the grid.
                    D2PatternSeedOnly: false,
                    SyncSubAssemblies: false) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("The linear pattern did not succeed: " + ex.Message);
            }
            if (feature == null)
                return Failed(
                    "The linear pattern was not created. Usually it is the pitch " +
                    "or the number: a series that goes out of the material, " +
                    "or that overlaps itself, cannot be built.");

            var measurements = new Dictionary<string, object>
            {
                ["instance_count1"] = instances1,
                ["pitch1_mm"] = pitch1,
            };
            if (grid)
            {
                measurements["instance_count2"] = instances2;
                measurements["pitch2_mm"] = pitch2;
            }

            var stopped = CompareCopy(
                model, volumeBefore, bodiesBefore, facesBefore, measurements,
                "The linear pattern");
            if (stopped != null) return Failed(stopped);

            var missingItems = MissingCopies(
                measurements, feature, seed, instances1 * instances2 - 1,
                "The linear pattern",
                "If the row leaves the part on one side only, reverse the direction with " +
                "«flip1» (or «flip2» for the second direction); " +
                "otherwise reduce the pitch or the number of instances.");
            if (missingItems != null) return Failed(missingItems);

            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// Mirrors a feature with respect to one of the three base planes.
        ///
        /// SIGNATURE VERIFIED on interop 29.5.1.1 — already the highest:
        ///   IFeatureManager.InsertMirrorFeature2(Boolean BMirrorBody, Boolean
        ///       BGeometryPattern, Boolean BMerge, Boolean BKnit, Int32
        ///       ScopeOptions) -> Feature
        ///   swFeatureScope_e: AllBodies = 0
        ///
        /// THE MARKS ARE THE GUIDE'S, AND MEASURED: the plane with mark 2 and
        /// the features with mark 1 (SelectionMarks.MirrorFeaturePlane and
        /// MirrorFeatureFeatures). IFeatureManager~InsertMirrorFeature2.html,
        /// Remarks table: «Features to be mirrored 1», «Plane or planar face 2».
        /// Until 0.35.0 this used the pattern's 1 and 4, and no mirror was
        /// created. The off-centre hole the old note asked for is the bench
        /// case mirror/hole_mirrored_on_right: a hole at (60, 25) mirrored
        /// on the right plane gives holes at x -60 and 60, y 25 and 25,
        /// 475476.11 mm3, one body.
        ///
        /// The FEATURE is mirrored, not the body: `BMirrorBody` stays
        /// false. Mirroring a whole body is the most common case in the
        /// shop for a symmetric part, but it requires first understanding how
        /// «the body» is selected — no helper of this file does it — and
        /// reverifying the same Marks on that branch.
        /// </summary>
        private Outcome Mirror(IModelDoc2 model, Dictionary<string, object> a)
        {
            string plane = Str(a, "plane", "");
            if (string.IsNullOrEmpty(plane)) return Failed("The plane of symmetry is missing.");

            double? volumeBefore; int bodiesBefore, facesBefore;
            Snapshot(model, out volumeBefore, out bodiesBefore, out facesBefore);
            if (volumeBefore == null || bodiesBefore < 0 || facesBefore < 0)
                return Failed(VolumeNotReadBefore("making the copy"));

            model.ClearSelection2(true);
            var planeFeature = FindBasePlane(model, plane);
            // `InsertMirrorFeature2` wants the PLANE with mark 2 and the
            // features with 1 — not the same marks as the pattern.
            // Up to 0.35.0 there were 1 and 4 here, that is those of the pattern, and
            // the mirror was not created.
            if (planeFeature == null || !planeFeature.Select2(false, SelectionMarks.MirrorFeaturePlane))
                return Failed($"I did not find the plane «{plane}» in the tree.");

            string error;
            if (!SelectFeature(model, a, out error, mark: SelectionMarks.MirrorFeatureFeatures)) return Failed(error);
            var seed = SelectedSeed(model, SelectionMarks.MirrorFeatureFeatures);

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertMirrorFeature2(
                    BMirrorBody: false,
                    BGeometryPattern: false,
                    BMerge: true,
                    BKnit: false,
                    ScopeOptions: (int)swFeatureScope_e.swFeatureScope_AllBodies) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("The mirror did not succeed: " + ex.Message);
            }
            if (feature == null)
                return Failed(
                    "The mirror was not created. If the chosen plane cuts the " +
                    "feature instead of leaving it all on one side, or if the " +
                    "copy would come out of the material, SolidWorks refuses.");

            var measurements = new Dictionary<string, object> { ["plane"] = plane };
            var stopped = CompareCopy(
                model, volumeBefore, bodiesBefore, facesBefore, measurements, "The mirror");
            if (stopped != null) return Failed(stopped);

            // HERE THE REFUSAL CANNOT FIRE, AND THAT IS RIGHT.
            // With a single copy we would need «zero copies made», and zero never
            // comes out: MissingCopies stops earlier if the feature has no
            // faces. So on the mirror this call only REPORTS
            // «copies_done», and the mirror that mirrored nothing remains
            // the responsibility of CompareCopy, above, which catches it from the part
            // staying identical.
            //
            // It is written down because a guard that seems to protect and cannot
            // fire is worse than an absent guard: the first time it is
            // read as a defense, the second one is not counted on.
            MissingCopies(measurements, feature, seed, 1, "The mirror", "");

            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// The axis around which — or along which — we repeat, with Mark 1.
        ///
        /// BY REFERENCE OR BY IDENTIFIER, AND IT IS A CORRECTION.
        /// Up to 0.30.2 `circular_pattern` accepted ONLY `axis_uid`, that is
        /// an axis that already existed in the document when the plan was born. And
        /// no operation of the catalog created axes. The result: the circular
        /// pattern was a dead end, offered and never
        /// usable.
        ///
        /// Now «create_axis» makes one, and this helper lets the step
        /// after cite it — «make the hole's axis, then repeat the feature
        /// six times around it» in a single plan. The object is selected
        /// directly: the name SolidWorks gave it we would know only by
        /// rereading the tree, and it would be an extra trip to get to the
        /// same feature that we already have in hand.
        /// </summary>
        /// <param name="prefix">«axis», «axis1» or «axis2»: the fields are
        /// named differently but they resolve identically.</param>
        /// <param name="mark">
        /// The selection mark, from <see cref="SelectionMarks"/>: the main
        /// direction (which all three patterns read), or the second direction
        /// of a grid.
        /// </param>
        /// <param name="append">
        /// True to ADD to the selection already made (the second direction of a
        /// grid), otherwise the second direction would clear the first. Explicit
        /// since Z-C21.6: it used to be inferred from `mark != 1`, which tied
        /// the selection behaviour to the numeric value of a mark.
        /// </param>
        private bool SelectAxis(
            IModelDoc2 model, Dictionary<string, object> a, string prefix,
            DocumentState docState, int mark, bool append, out string reason)
        {
            reason = null;
            string refField = prefix + "_ref";
            if (HasReference(a, refField))
            {
                int number;
                var planStep = CitedStep(a, refField, out number, out reason);
                if (planStep == null)
                {
                    reason = reason ?? "Reference to the axis not resolved.";
                    return false;
                }
                if (planStep.Feature == null)
                {
                    reason = "The step " + number + " left no axis.";
                    return false;
                }
                if (!planStep.Feature.Select2(append, mark))
                {
                    reason = "I could not select the axis of the step " + number + ".";
                    return false;
                }
                _log("[agent] axis taken from the step " + number);
                return true;
            }

            string uid = Str(a, prefix + "_uid", "");
            if (string.IsNullOrEmpty(uid))
            {
                reason =
                    "You did not say along or around which direction. Give in «" +
                    prefix + "_uid» an axis OR a straight edge of " +
                    "the state of the document, or give «" + refField + "» if " +
                    "the axis is created by a previous step of this plan with " +
                    "«create_axis».";
                return false;
            }

            // ── A STRAIGHT EDGE COUNTS AS A DIRECTION, AND IT IS A CORRECTION ──
            //
            // Up to 0.30.2 there was only `SelectByID2(uid, "AXIS", ...)` here,
            // that is a reference axis. But the only operation of the catalog
            // that creates axes is «create_axis», which takes them from the center of a
            // CYLINDRICAL face: the axis that comes out is always that of a
            // hole or a shaft, that is PERPENDICULAR to the face the
            // feature sits on. Repeating along that means repeating inside
            // the thickness.
            //
            // Result: «eight holes in a row on the plate» — the case for which
            // the linear pattern exists — had NO valid form.
            // It was the same dead end that 0.30.2 closed for the
            // circular pattern, one floor further on.
            //
            // An edge of the part has the direction, and the document state
            // already lists the edges (E1, E2, ...). SolidWorks
            // accepts a linear edge as the direction of a linear pattern
            // and as the axis of a circular one.
            //
            // The circular half is measured, not only accepted (Z-C21.9,
            // finding M16, not real): the Z-C21.3 probe of L7-5 and M16 (28/9/2026)
            // turned four copies exactly around a straight edge selected with
            // SelectByMark and mark 1, the call below, although the marks table
            // of FeatureCircularPattern5.html names only features and axes
            // (SelectionMarks.CircularPatternAxis); bench case
            // the_circular_pattern_turns_around_a_straight_edge.
            //
            // IT MUST BE STRAIGHT, AND IT IS CHECKED. A curved edge gives no
            // error: it gives the direction of the tangent at a point nobody
            // chose, that is a part that looks right.
            // AN EDGE EVEN IF THAT UID NO LONGER IS ONE (entry E19): if
            // the plan carries a signature with the length, when it read it
            // it was an edge, and it is searched as an edge — it does not fall back on axes.
            DocumentState.EntitySignature signature;
            var signatureInPlane = ExpectedSignature(a, uid);
            bool wasAnEdge = signatureInPlane != null && signatureInPlane.ContainsKey("length_mm");
            // By rank too (brief R17): an edge beyond the thirty listed is found
            // by TryGetSignature, not by the dictionary of the listed ones.
            if ((docState != null && docState.TryGetSignature(uid, out signature) && signature.Type == "EDGE") ||
                wasAnEdge)
            {
                signature = EdgeSignature(
                    a, docState, uid, "The edge «" + uid + "» is no longer in this document.",
                    out reason);
                if (signature == null) return false;

                var edge = signature.Entity as IEdge;
                bool straight = false;
                try
                {
                    var curva = edge == null ? null : edge.IGetCurve();
                    straight = curva != null && curva.IsLine();
                }
                catch { }
                if (!straight)
                {
                    reason =
                        "The edge «" + uid + "» is not straight: it does not " +
                        "give a direction. For the pattern you need a " +
                        "rectilinear edge, or an axis made with «create_axis».";
                    return false;
                }

                var entity = edge as IEntity;
                if (entity == null || !entity.SelectByMark(append, mark))
                {
                    reason = "The edge «" + uid + "» could not be selected.";
                    return false;
                }
                _log("[agent] direction taken from the edge " + uid);
                return true;
            }

            // THE AXIS BY IDENTITY, NOT JUST BY NAME (entry E4). If the state
            // knows it, the signature the plan carries must match the current
            // line, and it is selected by object instead of by name.
            DocumentState.EntitySignature axisSignature;
            if (docState != null && docState.Signatures.TryGetValue(uid, out axisSignature) && axisSignature.Type == "AXIS")
            {
                string offsetAxis;
                if (!SignatureMatches(a, uid, axisSignature, out offsetAxis)) { reason = offsetAxis; return false; }
                var featAxis = axisSignature.Entity as IFeature;
                if (featAxis != null && featAxis.Select2(append, mark)) return true;
            }

            if (!model.Extension.SelectByID2(uid, "AXIS", 0, 0, 0, append, mark, null, 0))
            {
                reason =
                    "I did not find the axis «" + uid + "». Axes are made with " +
                    "«create_axis»; or point to a straight edge of " +
                    "the part, which in the state has an identifier like «E1».";
                return false;
            }
            return true;
        }

        /// <summary>
        /// The feature to pattern or mirror, selected with the mark the caller
        /// passes (4 for the patterns, 1 for the mirror: see the parameter),
        /// by reference to a previous step or by identifier.
        ///
        /// In a single method because linear pattern, circular
        /// pattern and mirror name it in the very same way, and three
        /// copies of the same logic diverge at the first correction.
        /// </summary>
        /// <param name="mark">
        /// THE MARK IS NOT THE SAME FOR ALL FEATURES, and it is the kind of
        /// detail that gives no error: with the wrong mark the selection
        /// is there, the feature does not see it, and null comes back without saying anything.
        /// The patterns want the features with mark 4 and the axis with
        /// 1; the mirror wants them with 1 and the plane with 2.
        /// </param>
        private bool SelectFeature(
            IModelDoc2 model, Dictionary<string, object> a, out string reason,
            int mark = SelectionMarks.LinearPatternFeatures)
        {
            reason = null;
            if (HasReference(a, "feature_ref"))
            {
                int number;
                var planStep = CitedStep(a, "feature_ref", out number, out reason);
                if (planStep == null)
                {
                    reason = reason ?? "Reference not resolved.";
                    return false;
                }
                if (planStep.Feature == null)
                {
                    reason = "The step " + number + " did not leave a feature.";
                    return false;
                }
                if (!planStep.Feature.Select2(true, mark))
                {
                    reason = "I could not select the feature of the step " +
                             number + ".";
                    return false;
                }
                _log("[agent] feature taken from the step " + number);
                return true;
            }

            string uid = Str(a, "feature_uid", "");
            if (string.IsNullOrEmpty(uid))
            {
                reason = "You did not say which feature to repeat or mirror.";
                return false;
            }
            // IS IT STILL THAT ONE? See FeatureIsThatOne.
            if (!FeatureIsThatOne(model, uid, a, out reason)) return false;

            if (!model.Extension.SelectByID2(uid, "BODYFEATURE", 0, 0, 0, true, mark, null, 0))
            {
                reason = "I did not find the feature «" + uid + "».";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Is the feature carrying that name still the one the plan
        /// had looked at?
        ///
        /// THE UID OF A FEATURE IS ITS NAME — there is nothing else
        /// in the tree — and a name can be renamed. Whoever renames «Cut-Extrude1» and then
        /// calls «Cut-Extrude1» another feature has transferred the
        /// identifier from one thing to another, and `SelectByID2` will
        /// obediently select the new one.
        ///
        /// `IFeature.GetID()` cannot be renamed, and the plan carries it along
        /// in the signature. Here they are compared.
        ///
        /// IT IS CHECKED ONLY IF IT CAN BE. No signature, or a signature without
        /// `feature_id`, means nothing to compare: a refusal is not invented. It is the same
        /// rule as `SignatureMatches`.
        /// </summary>
        private static bool FeatureIsThatOne(
            IModelDoc2 model, string uid,
            Dictionary<string, object> arguments, out string reason)
        {
            reason = null;
            var expectation = ExpectedSignature(arguments, uid);
            if (expectation == null) return true;
            object raw;
            if (!expectation.TryGetValue("feature_id", out raw) || raw == null) return true;
            int expected;
            try { expected = Convert.ToInt32(raw); } catch { return true; }

            var partDoc = model as IPartDoc;
            if (partDoc == null) return true;
            IFeature f = partDoc.IFirstFeature();
            while (f != null)
            {
                string itemName = null;
                try { itemName = f.Name; } catch { }
                if (string.Equals(itemName, uid, StringComparison.OrdinalIgnoreCase))
                {
                    int now;
                    try { now = f.GetID(); } catch { return true; }
                    if (now == expected) return true;
                    reason =
                        "The feature «" + uid + "» is no longer the one you " +
                        "looked at: someone renamed the tree, and now that name is " +
                        "on another feature. Read the state of the document again " +
                        "and name it again.";
                    return false;
                }
                f = f.GetNextFeature() as IFeature;
            }
            // I did not find it by walking the tree: SelectByID2 says so with its
            // message, which is more precise than one invented here.
            return true;
        }

        /// <summary>
        /// The base plane indicated, WITHOUT selecting it.
        ///
        /// It is not a lazy duplicate of `SelectPlane`: that one always selects
        /// with Mark 0 — which suits the extrusions, which do not read
        /// Marks — and returns a bool. Here the Feature is needed, in order to be able to
        /// select it with the Mark the operation wants.
        /// </summary>
        private static IFeature FindBasePlane(IModelDoc2 model, string which)
        {
            if (!(model is IPartDoc)) return null;
            // THREE NAMES AND NOTHING ELSE: an unknown name is null, never the
            // front plane (a final `: 1` did that until the measure of 10
            // September 2026, «mirror on a plane that does not exist»).
            // BY GEOMETRY, NOT BY POSITION (Z-C21.5, scheme S19): the plane with
            // the canonical normal through the origin, References.BasePlane.
            // The callers keep their own refusal, which names the plane.
            return References.BasePlane(model, which, out _);
        }

        /// <summary>
        /// Revolve: rotates a profile around an axis. It is the general way
        /// of making a body with rotational symmetry — bushings, spacers,
        /// stepped shafts, flanges, pulleys, handwheels — when the shape is
        /// not a constant cylinder (that is «extrude_circle», simpler).
        ///
        /// THE AXIS IS THE HARD POINT, AND IT IS WORTH EXPLAINING.
        /// `FeatureRevolve2` wants, at the moment of the call, both the profile
        /// and the axis. In an empty document — the normal case, because this
        /// operation makes the FIRST body — there is no reference axis
        /// to cite and no edge to use as one.
        ///
        /// The road chosen: the axis is drawn as a CENTERLINE inside the same
        /// sketch as the profile (`ISketchManager.CreateCenterLine`, verified
        /// by reflection: same signature as CreateLine), and then the whole
        /// sketch is selected, as is already done for extrusions and cuts.
        ///
        /// THE AXIS IS THE ONLY CENTERLINE OF THE SKETCH, AND THAT IS A MEASURE,
        /// NOT UseAutoSelect. The guide asks for two selections
        /// (IFeatureManager~FeatureRevolve2.html, Remarks: «the sketch to
        /// revolve, using Mark = 0» and «the axis of revolution, using Mark = 4
        /// or 16»); only the sketch is selected here, and SOLIDWORKS takes its
        /// single centerline as the axis. Measured by the revolve cases of the
        /// bench (Bench/cases/revolve): four with their volume to half a
        /// cubic millimetre and one body (cylinder_d40_long_50 62831.853 mm3,
        /// half_cylinder_180, hollow_bushing, stepped_shaft) and the refusal
        /// limit_profile_straddling_the_axis; a wrong axis would not give those
        /// volumes (audit finding L2-1, not real). UseAutoSelect is about
        /// BODIES, not the axis: «True to automatically select all bodies and
        /// have the feature affect those bodies» (same page). Adding the
        /// centerline with mark 4 would follow the guide to the letter, and
        /// needs those cases run before and after.
        ///
        /// SIGNATURE VERIFIED on interop 29.5.1.1, used AS IS and not
        /// «updated» to a higher number:
        ///   IFeatureManager.FeatureRevolve2(SingleDir, IsSolid, IsThin,
        ///       IsCut, ReverseDir, BothDirectionUpToSameEntity, Dir1Type,
        ///       Dir2Type, Dir1Angle, Dir2Angle, OffsetReverse1,
        ///       OffsetReverse2, OffsetDistance1, OffsetDistance2, ThinType,
        ///       ThinThickness1, ThinThickness2, Merge, UseFeatScope,
        ///       UseAutoSelect) -> Feature
        ///
        /// ONLY THE SOLID BODY: IsThin stays false. For a thin wall the
        /// void is drawn as a second contour of the profile.
        ///
        /// AND THE RESULT IS COUNTED. A profile that CROSSES the axis gives no
        /// error: it gives a self-intersecting solid. It is the same family
        /// as the 50.8 mm hole that looked right. Starting from a document
        /// without bodies — which is the precondition — a successful revolve leaves
        /// exactly ONE: zero or more than one means the profile
        /// and the axis were not what they seemed.
        /// </summary>
        private Outcome Revolve(IModelDoc2 model, Dictionary<string, object> a)
        {
            var contours = Contours(a, out string reason, out string _);
            if (contours == null) return Failed(reason);

            object rawAxis;
            if (!a.TryGetValue("axis", out rawAxis) || rawAxis == null)
                return Failed("The axis of revolution is missing.");
            var axisPoints = Points(rawAxis, out reason);
            if (axisPoints == null) return Failed("Axis of revolution: " + reason);
            if (axisPoints.Count != 2)
                return Failed(
                    "The axis of revolution must be given as two points [x_mm, y_mm], not " +
                    axisPoints.Count + ".");
            double dx = axisPoints[1][0] - axisPoints[0][0];
            double dy = axisPoints[1][1] - axisPoints[0][1];
            if (Math.Sqrt(dx * dx + dy * dy) < 1e-6)
                return Failed("The two points of the axis coincide: they do not define a line.");

            double angle = Num(a, "angle_deg", 360);
            string plane = Str(a, "plane", "front");
            // Before the sketch: it is selected, and locked after the
            // revolve, as the one sketch born since (scheme S7).
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectPlane(model, plane))
                return Failed($"I did not find the plane «{plane}» in the tree.");

            var sketches = model.SketchManager;
            bool addToDbBefore = false, displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }

            int segments = 0;
            bool createdAxis = false;
            try
            {
                sketches.InsertSketch(true);
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }

                foreach (var contour in contours) segments += DrawContour(sketches, contour);

                var axisSegment = sketches.CreateCenterLine(
                    Units.M(axisPoints[0][0]), Units.M(axisPoints[0][1]), 0,
                    Units.M(axisPoints[1][0]), Units.M(axisPoints[1][1]), 0) as ISketchSegment;
                // Redundant if, as expected, CreateCenterLine is already born as a
                // construction line: it is written anyway because it costs nothing and
                // it is the difference between a recognized axis and any
                // line at all, if that expectation were wrong.
                try { if (axisSegment != null) axisSegment.ConstructionGeometry = true; } catch { }
                createdAxis = axisSegment != null;
            }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
            }
            sketches.InsertSketch(false);

            if (!createdAxis)
                return Failed("I could not draw the axis of revolution." + WithoutSketch(model));
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            IFeature feature;
            try
            {
                feature = model.FeatureManager.FeatureRevolve2(
                    SingleDir: true,
                    IsSolid: true,
                    IsThin: false,
                    IsCut: false,
                    ReverseDir: false,
                    BothDirectionUpToSameEntity: false,
                    Dir1Type: (int)swEndConditions_e.swEndCondBlind,
                    Dir2Type: (int)swEndConditions_e.swEndCondBlind,
                    Dir1Angle: Units.Rad(angle),
                    Dir2Angle: 0,
                    OffsetReverse1: false,
                    OffsetReverse2: false,
                    OffsetDistance1: 0,
                    OffsetDistance2: 0,
                    ThinType: 0,
                    ThinThickness1: 0,
                    ThinThickness2: 0,
                    Merge: true,
                    UseFeatScope: true,
                    UseAutoSelect: true) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("The revolve did not succeed: " + ex.Message + WithoutSketch(model));
            }
            if (feature == null)
                return Failed(
                    "The revolve was not created. Usually it is the axis: it " +
                    "must lie on the plane of the profile, and " +
                    "the profile must not cross it." + WithoutSketch(model));

            int nBodies = CountSolidBodies(model);
            if (nBodies >= 0 && nBodies != 1)
                return Failed(
                    "The revolve created the feature but the document has " +
                    nBodies + " solid bodies instead of one: most probably the " +
                    "profile crosses the axis. I do not confirm a part " +
                    "different from the one I showed you: withdraw it — " +
                    Sentences.HowToWithdrawIt + ".");

            string noteBlock;
            int fixedRelations;
            bool locked = LockSketch(model, sketchNamesBefore, out noteBlock, out fixedRelations);
            var revolveMeasures = new Dictionary<string, object>
            {
                ["sketch_locked"] = locked,
                ["fixed_relations"] = fixedRelations,
                ["contours"] = contours.Count,
                ["segments"] = segments,
                ["angle_deg"] = angle,
            };
            if (noteBlock != null) revolveMeasures["sketch_lock_note"] = noteBlock;
            return Succeeded(feature, revolveMeasures, References.PlaneNormal(plane));
        }

        /// <summary>
        /// The shell: hollows out a solid body leaving a wall of constant
        /// thickness. The faces indicated are REMOVED, that is they stay open.
        ///
        /// TWO THINGS TO KNOW BEFORE TOUCHING THIS METHOD, both
        /// verified by reflection on interop 29.5.1.1:
        ///
        ///   IModelDoc2.InsertFeatureShell(Double Thickness, Boolean Outward)
        ///
        ///   1. it lives on IModelDoc2, NOT on IFeatureManager where everything
        ///      else in this file lives. It is one of the seven APIs that in the RAG
        ///      «seemed right and were not»;
        ///   2. it returns VOID. There is no InsertFeatureShell2 that
        ///      returns a Feature.
        ///
        /// It does not take the faces as a parameter: it works on what is ALREADY
        /// SELECTED, as the fillet and the chamfer do with edges.
        ///
        /// SINCE IT DOES NOT RETURN A FEATURE, it is looked for at the end of
        /// the tree — and since «the tree grew» is not enough to say that
        /// it grew WELL, the volume is measured: a shell REMOVES material,
        /// and it removes less than the whole body. On 9 September 2026 we
        /// measured that in this same agent a call can succeed and
        /// produce wrong geometry without giving an error (HoleWizard5, two-inch
        /// hole). Here the check costs two readings.
        /// </summary>
        private Outcome Shell(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double thickness = Num(a, "thickness_mm");
            bool outwardFacing = Bool(a, "outward", false);
            bool allClosed = Bool(a, "fully_closed", false);
            var uid = StringList(a, "faces_uid");

            // THE FACES CAN COME FROM A PREVIOUS STEP (entry E15).
            //
            // Up to 0.30.0 `shell` was the ONLY face operation that
            // did not accept a reference to a step: it wanted `faces_uid`, which
            // are born only from a reading. So «make a block and then hollow it»
            // could not be written in a single plan, while «make a block and
            // then drill it» could — and that is precisely the sequence of the antenna
            // enclosure: block, shell, tabs.
            //
            // A LIST OF SINGLE REFERENCES, not a plural kind. The
            // vocabulary of `References` resolves one face at a time, and
            // no operation of the catalog today uses a kind that gives
            // more than one: inventing one here would mean opening a new
            // surface for a problem that is solved by repeating the element.
            List<IFace2> fromSteps = null;
            if (uid.Count == 0 && HasList(a, "faces_ref"))
            {
                if (!FacesFromReferences(a, "faces_ref", out fromSteps, out string why))
                    return Failed(why);
            }
            else if (uid.Count > 0 && HasList(a, "faces_ref"))
            {
                return Failed(
                    "The shell has both «faces_uid» and «faces_ref»: one names faces " +
                    "that are already there, the other faces that a step of this plan " +
                    "creates. Choose one.");
            }

            if (uid.Count == 0 && fromSteps == null && !allClosed)
                return Failed(
                    "No face indicated for the shell. If the face is created by " +
                    "a previous step of this plan, use «faces_ref»: " +
                    "[{\"step\": 1, \"kind\": \"top_face\"}]. If you really " +
                    "need a completely closed hollow body, with no opening at all, " +
                    "put «fully_closed: true» — but usually it is a face forgotten " +
                    "in the request.");

            model.ClearSelection2(true);

            if (fromSteps != null)
            {
                // The faces arrive FRESH from the step just run, not from
                // a previous reading: there is no signature to
                // compare, and it is the same reason why the `face_ref`
                // branch of `ResolveFace` skips that check.
                // ALL, NOT AT LEAST ONE. The first version counted how many had
                // let themselves be selected and stopped only at zero: with three
                // references resolved and two faces selected it went on, and
                // the shell came out open on TWO faces instead of three —
                // «succeeded: true», volume dropped, all the downstream checks
                // satisfied, and an enclosure closed on a side that was supposed to
                // be open.
                //
                // The rule of this entry was already written and held on
                // RESOLUTION: «if even one does not resolve, everything fails».
                // It held on half of the road. The uid branch, fifteen
                // lines further down, refuses at the FIRST face that does not
                // get selected: a repair that covers one branch and leaves its
                // twin uncovered.
                int takenOnes = 0;
                foreach (var f in fromSteps)
                {
                    if ((f as IEntity)?.Select4(true, null) == true) { takenOnes++; continue; }
                    return Failed(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Face no. {0} of «faces_ref» could not be selected: {1} out of " +
                        "{2} were ready. A shell open on fewer faces than the ones asked " +
                        "for is not almost right, it is a different part.{3}",
                        takenOnes + 1, takenOnes, fromSteps.Count, RemainsInPart));
                }
                _log("[agent] " + takenOnes + " faces taken from the previous steps");
            }
            else if (uid.Count == 0)
            {
                // Closed shell: the body is selected. With several bodies we do not
                // choose: we ask. A shell applied to the wrong body
                // is exactly the part that looks right.
                var partDoc = model as IPartDoc;
                var bodies = partDoc?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null || bodies.Length == 0)
                    return Failed("I did not find any solid body to hollow out.");
                if (bodies.Length > 1)
                    return Failed(
                        "In this document there are " + bodies.Length + " solid " +
                        "bodies: a closed shell without faces indicated would work on " +
                        "one chosen at random. Point to the faces to remove.");
                // A BODY IS NOT AN IEntity, and this branch never
                // knew it. `bodies[0] as IEntity` **always** returns null
                // in the interop, so «fully_closed: true» answered «I could not
                // select the solid body» on ANY
                // part — it was not an edge case, it was always broken.
                //
                // What hurts: the lesson was already written, in this
                // same file, four thousand lines further up. The comment of
                // `MoveBodies` tells it at length — measured inside
                // SOLIDWORKS on 10 September 2026, with five tests that
                // said «the bodies indicated could not be selected»
                // — and there it is already repaired with `IBody2.Select2`. Not here.
                //
                // A lesson written and not applied is a lesson the
                // code has not learned: the comment reminds whoever reads
                // THOSE lines, and whoever writes elsewhere does not pass through there.
                var body = bodies[0] as IBody2;
                bool taken = body != null && body.Select2(false, null);
                if (!taken)
                {
                    // The old road stays as a fallback, not as the first
                    // choice: if tomorrow the interop changed its mind, this
                    // branch keeps working instead of breaking.
                    var asEntity = bodies[0] as IEntity;
                    taken = asEntity != null && asEntity.Select4(false, null);
                }
                if (!taken)
                    return Failed("I could not select the solid body.");
            }
            else
            {
                // The faces are selected by ACCUMULATING, as the gusset does:
                // `SelectFace` selects with append=false and is meant
                // for a single face before a sketch. Here all are needed
                // together, and using that helper would leave only
                // the last one selected — a shell open on one side instead of three,
                // with no error.
                foreach (var u in uid)
                {
                    DocumentState.EntitySignature signature;
                    if (docState == null || !docState.TryGetSignature(u, out signature) ||
                        signature.Type != "FACE")
                        return Failed("The face «" + u + "» is no longer in this document.");
                    string offset;
                    if (!SignatureMatches(a, u, signature, out offset)) return Failed(offset);
                    var ent = signature.Entity as IEntity;
                    if (ent == null || !ent.Select4(true, null))
                        return Failed("The face «" + u + "» could not be selected.");
                }
            }

            int featureBefore = CountFeature(model);
            int bodiesBefore = CountSolidBodies(model);
            double? volumeBefore = VolumeMm3(model);
            var namesBefore = FeatureNamesNow(model);
            if (bodiesBefore < 0 || featureBefore < 0 || namesBefore == null) return Failed(VolumeNotReadBefore("making the shell").Replace("the volume of the part", "the bodies of the part"));

            try
            {
                model.InsertFeatureShell(Units.M(thickness), outwardFacing);
            }
            catch (Exception ex)
            {
                return Failed("The shell did not succeed: " + ex.Message);
            }

            int featureAfter = CountFeature(model);
            if (featureAfter < 0)
                return Failed(
                    "I asked for the shell, but then I cannot count the tree again: I cannot tell you " +
                    "whether it was created. Look at the part before going on.");
            if (featureAfter <= featureBefore)
                return Failed(
                    "The shell was not created. Usually it is the thickness: " +
                    "bigger than half of the body does not fit, and near an " +
                    "edge it would leave a wall of negative thickness.");

            int bodiesAfter = CountSolidBodies(model);
            if (bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                return Failed(
                    "The shell changed the number of solid bodies (from " +
                    bodiesBefore + " to " + bodiesAfter + "): it is not the hollowed " +
                    "body that was expected. I do not let it pass as good.");

            // THE SHELL REMOVES MATERIAL. If the volume did not drop, that
            // feature is not a shell, whatever the tree says.
            double? volumeAfter = VolumeMm3(model);
            if (volumeBefore > 0 && volumeAfter > 0 && volumeAfter >= volumeBefore - 0.001)
                return Failed(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "The shell removed nothing: the volume was {0:0.#} mm3 and it " +
                    "is {1:0.#} mm3. It is not the part I promised you.",
                    volumeBefore, volumeAfter));

            // THE SHELL IS THE ONE FEATURE BORN SINCE THE CALL (FeatureBorn,
            // scheme S7, Z-C21.5), not the last feature of the tree: on a
            // sheet-metal part that is the flat pattern (finding M9).
            int featuresBorn;
            var feature = FeatureBorn(model, namesBefore, null, out featuresBorn);
            if (feature == null && featuresBorn > 1)
                return Failed(
                    "The shell seems made, but " + featuresBorn + " features were born in the tree and I do not " +
                    "pick one of them for you. Look at the part before going on.");
            if (feature == null)
                return Failed("The shell seems created but I cannot find the feature in the tree.");

            var measurements = new Dictionary<string, object>
            {
                ["thickness_mm"] = thickness,
                ["facing"] = outwardFacing ? "outer" : "inner",
                // NOT `uid.Count`: the faces can also come from
                // `faces_ref`, and counting only the uids made a shell that had removed
                // a face for real say «faces_removed:
                // 0». A measure that says zero when the number is one is the defect
                // this project chases, in miniature.
                ["faces_removed"] = fromSteps != null ? fromSteps.Count : uid.Count,
            };
            if (volumeBefore > 0 && volumeAfter > 0)
                measurements["material_removed_mm3"] = Math.Round(volumeBefore.Value - volumeAfter.Value, 1);
            else
                measurements["volume_not_reread"] = NoteVolumeNotReread;
            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// How many features there are in the tree.
        ///
        /// It serves the operations whose API returns void — the shell, for
        /// now — to know whether they created anything: the only sign of life
        /// is that the tree grew by one position.
        ///
        /// **-1 when it could not be read, or if it is not a part** (entry
        /// Z-C8): with the form `int n = 0; try { ... } catch { }` an error halfway
        /// through the tree gave a lower number, and «the tree did not
        /// grow» became a false refusal.
        /// </summary>
        private static int CountFeature(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return -1;
            int n = 0;
            try
            {
                var f = partDoc.IFirstFeature();
                while (f != null) { n++; f = f.IGetNextFeature(); }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// How many reference planes the part has, now, or **-1 if it could not
        /// be read** (entry Z-C8): a feature that does not say its type,
        /// or a tree that breaks off, left a lower number.
        /// </summary>
        private static int CountPlanes(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return -1;
            int n = 0;
            try
            {
                IFeature f = partDoc.IFirstFeature();
                while (f != null)
                {
                    if (f.GetTypeName2() == "RefPlane") n++;
                    f = (IFeature)f.GetNextFeature();
                }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>How many planes the step added, or null if a count cannot be read.</summary>
        private static object AddedPlanes(IModelDoc2 model, int before)
        {
            int now = CountPlanes(model);
            return before >= 0 && now >= 0 ? (object)(now - before) : null;
        }

        // ─────────────────────────────────────────────────────────────────
        // The feature a call has just added (scheme S7, Z-C21.5)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The names of the top-level features of a part, now: the «before» of
        /// <see cref="FeatureBorn"/>, taken right before the call that adds a
        /// feature. Null when the document is not a part or the tree cannot be
        /// read to the end (a partial set would make an old feature look born).
        /// </summary>
        private static HashSet<string> FeatureNamesNow(IModelDoc2 model)
        {
            List<IFeature> features;
            List<string> names, types;
            if (!ReadFeatureTree(model, out features, out names, out types)) return null;
            return new HashSet<string>(names, StringComparer.Ordinal);
        }

        /// <summary>
        /// THE FEATURE A CALL HAS JUST ADDED, FOUND BY THE DIFFERENCE OF THE
        /// TREE (scheme S7: findings L8-6, M9 and N13; Z-C21.5). The one feature
        /// of type <paramref name="type"/> (any type when null) whose name was
        /// not in <paramref name="before"/>. Null when none was born, when more
        /// than one was, or when the tree cannot be read; <paramref name="born"/>
        /// says how many of that type were born (-1: not read).
        ///
        /// WHY NOT THE LAST FEATURE OF THE TREE. On a sheet-metal part the tail
        /// of the tree is the flat pattern (the FlatPattern feature at the bottom
        /// of the tree, SheetMetalOperations), and SOLIDWORKS inserts a new feature
        /// before it. The last feature is then the flat pattern: create_axis
        /// refused an axis it had made and left it in the tree,
        /// plane_three_points reported the flat pattern as the plane it had
        /// made, and the orphan sketch of a failed step was never taken away.
        /// IModelDoc2~IFeatureByPositionReverse.html: «SOLIDWORKS does not
        /// guarantee the name or positioning of default features». A feature
        /// name is unique in a part, so the names that were not there before are
        /// what the call added, wherever in the tree it went.
        /// </summary>
        private static IFeature FeatureBorn(IModelDoc2 model, HashSet<string> before, string type, out int born)
        {
            born = -1;
            List<IFeature> features;
            List<string> names, types;
            if (before == null || !ReadFeatureTree(model, out features, out names, out types)) return null;
            int index = BornFeatureIndex(names, types, before, type, out born);
            return index >= 0 ? features[index] : null;
        }

        /// <summary>
        /// The top-level features of a part in tree order, with their names and
        /// types (an unreadable type is ""). False when the document is not a
        /// part or the walk breaks: then nothing is returned rather than half a
        /// tree.
        /// </summary>
        private static bool ReadFeatureTree(
            IModelDoc2 model, out List<IFeature> features, out List<string> names, out List<string> types)
        {
            var inTree = new List<IFeature>();
            var theirNames = new List<string>();
            var theirTypes = new List<string>();
            features = inTree;
            names = theirNames;
            types = theirTypes;
            var partDoc = model as IPartDoc;
            if (partDoc == null) return false;
            try
            {
                IFeature f = partDoc.IFirstFeature();
                while (f != null)
                {
                    string kind = "";
                    try { kind = f.GetTypeName2() ?? ""; } catch { }
                    inTree.Add(f);
                    theirNames.Add(f.Name);
                    theirTypes.Add(kind);
                    f = f.IGetNextFeature();
                }
            }
            catch { return false; }
            return true;
        }

        /// <summary>
        /// The positions, in tree order, of the features whose name is not in
        /// <paramref name="before"/>: what was born since. Pure, like
        /// <see cref="BornFeatureIndex"/>.
        /// </summary>
        internal static List<int> BornFeatureIndices(IList<string> names, ICollection<string> before)
        {
            var born = new List<int>();
            if (names == null || before == null) return born;
            for (int i = 0; i < names.Count; i++)
                if (names[i] != null && !before.Contains(names[i])) born.Add(i);
            return born;
        }

        /// <summary>
        /// THE DECISION OF <see cref="FeatureBorn"/>, WITH NO INTEROP: the
        /// position of the ONE born feature of that type (any type when null),
        /// or -1 when there is none or more than one. Two born features of the
        /// same type are two candidates, and taking the later would be «the last
        /// in the tree» again. Pure, so that Oce.Solid.Agent.ContractTests checks it without
        /// SOLIDWORKS (Zc215IdentityContract).
        /// </summary>
        internal static int BornFeatureIndex(
            IList<string> names, IList<string> types, ICollection<string> before, string type, out int born)
        {
            born = -1;
            if (names == null || before == null) return -1;
            int found = -1;
            born = 0;
            foreach (int i in BornFeatureIndices(names, before))
            {
                if (type != null && (types == null || i >= types.Count || types[i] != type)) continue;
                born++;
                found = i;
            }
            return born == 1 ? found : -1;
        }

        /// <summary>
        /// How many faces the solid bodies have in all.
        ///
        /// It serves the operations that COPY — mirror and patterns —
        /// because the volume alone is not enough to say that something
        /// happened: mirroring a cut symmetric with respect to the chosen plane
        /// the volume changes, but there are cases where it changes little and
        /// cases where the copy merges with what was there. The faces
        /// instead are counted, and an extra copy always brings some.
        ///
        /// **-1 when it could not be read** (entry Z-C8): it had the form
        /// the scanner did not see, `int n = 0; try { ... } catch { }`, and two
        /// failed readings gave «same 0 faces» — that is the identical part.
        /// </summary>
        private static int CountFaces(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return -1;
            int n = 0;
            try
            {
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null) return 0;
                foreach (var c in bodies)
                {
                    var faces = (c as IBody2)?.GetFaces() as object[];
                    if (faces != null) n += faces.Length;
                }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// How the part is now: volume, bodies, faces. It is the photograph
        /// to compare before and after an operation that copies.
        /// </summary>
        private static void Snapshot(
            IModelDoc2 model, out double? volume, out int bodies, out int faces)
        {
            volume = VolumeMm3(model);
            bodies = CountSolidBodies(model);
            faces = CountFaces(model);
        }

        /// <summary>
        /// Compares the photograph before and after, and fills in the measures.
        /// Returns the reason for refusal if NOTHING HAPPENED.
        ///
        /// WHY THE CRITERION IS «NOTHING CHANGED» AND NOT SOMETHING
        /// MORE PRECISE. Of a copy it is not known in advance how much volume
        /// it adds or removes: it depends on how much the copy overlaps what
        /// was there. Demanding an exact number would mean refusing legitimate
        /// cases — and refusing what is merely unusual is a
        /// worse defect than not checking.
        ///
        /// «Nothing changed», instead, is unequivocal: SolidWorks said
        /// it created the feature and the part is identical. That is
        /// the false success, and it is the only thing this check
        /// refuses.
        /// </summary>
        private static string CompareCopy(
            IModelDoc2 model,
            double? volumeBefore, int bodiesBefore, int facesBefore,
            Dictionary<string, object> measurements,
            string whatItWas)
        {
            double? volumeAfter; int bodiesAfter, facesAfter;
            Snapshot(model, out volumeAfter, out bodiesAfter, out facesAfter);

            measurements["volume_before_mm3"] = NumberOrNull(volumeBefore, 2);
            measurements["volume_after_mm3"] = NumberOrNull(volumeAfter, 2);
            measurements["volume_changed_mm3"] = NumberOrNull(volumeAfter - volumeBefore, 2);
            measurements["faces_before"] = NumberOrNull(facesBefore);
            measurements["faces_after"] = NumberOrNull(facesAfter);
            measurements["bodies"] = NumberOrNull(bodiesAfter);

            // «IDENTICAL» IS SAID ONLY OF THINGS READ (entry Z-C8): with two failed
            // readings, zero before and zero after were «same volume, same 0
            // faces», and a successful copy was refused as nonexistent.
            bool everythingRead = volumeBefore.HasValue && volumeAfter.HasValue &&
                              facesBefore >= 0 && facesAfter >= 0 && bodiesBefore >= 0 && bodiesAfter >= 0;
            if (!everythingRead)
            {
                measurements["volume_not_reread"] = NoteVolumeNotReread;
                return null;
            }
            bool unchangedVolume = Math.Abs(volumeAfter.Value - volumeBefore.Value) < 0.01;
            if (unchangedVolume && facesAfter == facesBefore && bodiesAfter == bodiesBefore)
                return
                    whatItWas + " says it succeeded, but the part is identical to before: " +
                    "same volume (" + Math.Round(volumeAfter.Value, 2) + " mm3), same " +
                    facesAfter + " faces, same " + bodiesAfter + " bodies. Nothing was copied. Usually " +
                    "it means that the indicated feature is not the one you think, or that " +
                    "the copy would have fallen exactly where the original already is.";

            return null;
        }

        /// <summary>
        /// How many faces a feature OWNS.
        ///
        /// It is not the count of the part's faces: GetFaces() on a feature
        /// gives only those that feature created. It is the difference
        /// that makes it possible to count the copies without the material already
        /// present having anything to do with it.
        ///
        /// **-1 when it could not be read** (entry Z-C8). With zero, a
        /// failed reading became «the feature no longer has any
        /// face», and `SuppressInstance` undid a correct edit.
        /// ZERO-E-ZERO: `GetFaces` null on a live feature means it owns
        /// none (a plane, a sketch): that zero is true.
        /// </summary>
        private static int FeatureFaces(IFeature f)
        {
            if (f == null) return -1;
            try
            {
                var faces = f.GetFaces() as object[];
                return faces == null ? 0 : faces.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// The feature just selected with that mark.
        ///
        /// It is needed BEFORE creating the pattern, because building it
        /// clears the selection. Whoever calls it must accept a null: if
        /// the seed cannot be found again nothing is counted, and nothing is refused.
        /// </summary>
        private static IFeature SelectedSeed(IModelDoc2 model, int mark)
        {
            try
            {
                var sel = model.SelectionManager as ISelectionMgr;
                if (sel == null) return null;
                int howMany = sel.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= howMany; i++)
                    if (sel.GetSelectedObjectMark(i) == mark)
                        return sel.GetSelectedObject6(i, -1) as IFeature;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// THE COPIES THAT ARE NOT THERE.
        ///
        /// MEASURED on 14 September 2026 on two twin parts: block
        /// 200x100x20, hole Ø10 at x=-60, pattern of 4 along edge E1.
        ///
        ///   direction taken from the edge -> the pattern owns 1 face
        ///   with «flip1»                  -> the pattern owns 3 faces
        ///
        /// The seed owns 1. So ONE copy against THREE, for the same
        /// request. AND IN BOTH CASES SOLIDWORKS STAYED SILENT:
        /// GetErrorCode2 gave 0, IsWarning false, SkippedItemArray null.
        /// Two instances fell outside the material, it built what it
        /// could and answered «succeeded». Whoever asked for four holes got
        /// two, and nobody told them: it is exactly the defect
        /// that gives no error.
        ///
        /// WHEN IT IS NOT KNOWN, WE DO NOT REFUSE. If the seed cannot be found again, if
        /// one of the two has no faces, or if the count is not divisible, the
        /// number is not written and the operation passes. Copies that merge
        /// among themselves leave a remainder, and it is a legitimate case: refusing
        /// what is merely unusual is a worse defect than not
        /// checking at all.
        ///
        /// Nor do we refuse if the copies are MORE than those asked for:
        /// it would mean that this way of counting is wrong, and on a
        /// count that has just proved wrong nothing is denied.
        /// </summary>
        private static string MissingCopies(
            Dictionary<string, object> measurements, IFeature repetition, IFeature seed,
            int requestedCopies, string whatItWas, string howToRepair)
        {
            if (requestedCopies < 1) return null;

            int patternFaces = FeatureFaces(repetition);
            int seedFaceList = FeatureFaces(seed);
            if (patternFaces <= 0 || seedFaceList <= 0) return null;
            if (patternFaces % seedFaceList != 0) return null;

            int done = patternFaces / seedFaceList;
            measurements["copies_requested"] = requestedCopies;
            measurements["copies_done"] = done;
            if (done >= requestedCopies) return null;

            return
                whatItWas + " says it succeeded, but the copies are not all there: " +
                "I counted " + done + " of the " + requestedCopies + " requested. " +
                "I count them from the faces that the pattern owns (" + patternFaces +
                ") divided by those of the repeated feature (" + seedFaceList + "). " +
                "SOLIDWORKS reports nothing here: it builds what it can and says " +
                "it succeeded. Usually the missing copies fall outside the " +
                "material" + (done > 1 ? ", or they merged with each other" : "") + ". " +
                howToRepair +
                " The pattern stayed in the part: withdraw it — " +
                Sentences.HowToWithdrawIt + ".";
        }

        /// <summary>
        /// How many faces each feature of the tree owns, by name.
        ///
        /// It serves to see whether a dimension change altered the NATURE of
        /// a feature, not just its measures. The count of the faces of the
        /// whole part is not enough: a pocket that becomes through takes one
        /// face away from the part, but so do ten other things.
        ///
        /// There is a ceiling, and it is not generic prudence: the tree costs about
        /// 200 ms per feature (measured), so on a part with hundreds of
        /// features this reading alone would last longer than the operation.
        /// Beyond the ceiling we stop and report what has been seen.
        /// </summary>
        private static Dictionary<string, int> FacesPerFeature(IModelDoc2 model)
        {
            var outside = new Dictionary<string, int>();
            try
            {
                var f = model.FirstFeature() as IFeature;
                int views = 0;
                while (f != null && views < 200)
                {
                    // -1 IS KEPT (entry Z-C8): a feature that could not be
                    // counted is not a feature «without faces», and
                    // removing it from the list would make it look vanished.
                    int howMany = FeatureFaces(f);
                    if (howMany != 0)
                    {
                        views++;
                        try { outside[f.Name] = howMany; } catch { }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// What changed in the tree, in one sentence for the reader.
        /// Null if nothing topological changed.
        ///
        /// IT IS REPORTED, NOT REFUSED. Thinning a plate until a
        /// pocket becomes through may be exactly what is wanted:
        /// refusing it would be refusing what is merely unusual. But a
        /// blind seat and a passage are two different parts, and whoever asked for
        /// the change must be able to read it instead of discovering it in the shop.
        /// </summary>
        /// <summary>
        /// Puts a dimension back to its previous value after a refusal, rebuilds and
        /// REREADS the volume: the tail of the refusal sentence. It lives in a single method
        /// because two guards of `SetDimension` use it (openings that
        /// leave the material, chamfers that no longer fit), and two copies of the
        /// same «I put it back» diverge at the first correction.
        /// </summary>
        private static string PutBackDimension(
            IModelDoc2 model, IDimension dimension, double before, double? volumeBefore,
            Func<double, double> toSystem, string units)
        {
            // swSetValueInConfiguration_e.swSetValue_InAllConfigurations = 2
            const int inAllConfigurations = 2;
            // In the same unit in which `before` was read: degrees for an angular
            // dimension, mm for the others (entry Z-C21.2, M3).
            try
            {
                dimension.SetSystemValue3(toSystem(before), inAllConfigurations, null);
                model.EditRebuild3();
                double? returned = VolumeMm3(model);
                return returned == null || volumeBefore == null
                    ? " I restored " + before.ToString("0.###") + " " + units + ", but I could not " +
                      "measure the volume again to check that the part is as it was: look at it."
                    : Math.Abs(returned.Value - volumeBefore.Value) < 0.01
                    ? " I did not leave it like this: I restored " + before.ToString("0.###") +
                      " " + units + " and the part is as it was."
                    : " I tried to restore " + before.ToString("0.###") + " " + units + ", but " +
                      "the volume is now " + returned.Value.ToString("0.##") + " mm3 instead of " +
                      volumeBefore.Value.ToString("0.##") + ": look at the part before going on.";
            }
            catch (Exception ex)
            {
                return " And I could not restore " + before.ToString("0.###") +
                       " " + units + " (" + ex.Message + "): the part kept the new value.";
            }
        }

        /// <summary>
        /// The chamfer or fillet that a new dimension has pushed out of measure,
        /// said in words («the chamfer «Chamfer1» takes a whole face of
        /// «Boss-Extrude1» (6 -> 5 faces)»); null if there is none.
        ///
        /// Two conditions, both read: a feature that is not a
        /// cut (`Cut`, `ICE`, `HoleWzd`, and the revolve cuts) has FEWER
        /// faces than before, and after it in the tree there is a chamfer or a
        /// fillet. A count of -1 does not enter: «I do not know» is not «lost».
        /// </summary>
        private static string ChamferThatDoesNotFit(
            IModelDoc2 model, Dictionary<string, int> before, Dictionary<string, int> after)
        {
            if (model == null || before == null || after == null) return null;
            string loser = null, howMuch = null;
            try
            {
                var f = model.FirstFeature() as IFeature;
                int views = 0;
                while (f != null && views < 400)
                {
                    views++;
                    string itemName = null, typ = null;
                    try { itemName = f.Name; typ = f.GetTypeName2(); } catch { }
                    if (itemName != null && typ != null)
                    {
                        bool isCut = typ == "Cut" || typ == "ICE" || typ == "HoleWzd" ||
                                         typ.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isChamferOrFillet = typ == "Chamfer" ||
                                                typ.IndexOf("Fillet", StringComparison.OrdinalIgnoreCase) >= 0;
                        int p, d;
                        if (loser == null && !isCut && !isChamferOrFillet &&
                            before.TryGetValue(itemName, out p) && after.TryGetValue(itemName, out d) &&
                            p >= 0 && d >= 0 && d < p)
                        {
                            loser = itemName;
                            howMuch = p + " -> " + d;
                        }
                        else if (loser != null && isChamferOrFillet)
                        {
                            return (typ == "Chamfer" ? "the chamfer «" : "the fillet «") + itemName +
                                   "» takes a whole face of «" + loser + "» (" + howMuch +
                                   " faces)";
                        }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        private static string ChangedFeatures(
            Dictionary<string, int> before, Dictionary<string, int> after,
            Dictionary<string, object> measurements)
        {
            var lost = new List<string>();
            var taken = new List<string>();
            var disappeared = new List<string>();

            foreach (var entry in before)
            {
                if (entry.Value < 0) continue;
                int now;
                if (!after.TryGetValue(entry.Key, out now)) { disappeared.Add(entry.Key); continue; }
                if (now < 0) continue;
                if (now < entry.Value) lost.Add(entry.Key + " " + entry.Value + " -> " + now);
                else if (now > entry.Value) taken.Add(entry.Key + " " + entry.Value + " -> " + now);
            }

            if (lost.Count == 0 && taken.Count == 0 && disappeared.Count == 0) return null;

            if (lost.Count > 0) measurements["features_with_fewer_faces"] = lost.ToArray();
            if (taken.Count > 0) measurements["features_with_more_faces"] = taken.ToArray();
            if (disappeared.Count > 0) measurements["features_gone"] = disappeared.ToArray();

            var lines = new List<string>();
            if (disappeared.Count > 0)
                lines.Add("have no face left: " + string.Join(", ", disappeared));
            if (lost.Count > 0)
                lines.Add("lost faces: " + string.Join(", ", lost) +
                          ". A blind cut that loses a face has usually " +
                          "become THROUGH: the bottom is gone");
            if (taken.Count > 0)
                lines.Add("took faces: " + string.Join(", ", taken));

            return "The new dimension changed the tree, not just the measures: " +
                   string.Join("; ", lines) + ".";
        }

        /// <summary>
        /// How many solid bodies the part has, or **-1 if it could not be read**
        /// (entry Z-C8): also for a document that is not a part, because «zero
        /// bodies» said of an assembly is a false sentence.
        ///
        /// ZERO-E-ZERO: `GetBodies2` null on a live part means it has no
        /// solid bodies (a part just opened, or made only of surfaces);
        /// a dead handle throws, and ends up in the `catch`.
        ///
        /// Whoever compares two counts must first check that both are
        /// >= 0: a -1 inside a difference is an invented number.
        /// </summary>
        private static int CountSolidBodies(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return -1;
            try
            {
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                return bodies == null ? 0 : bodies.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// A count for an answer: the number, or null when it is -1 («I do
        /// not know»). A null field is read as «not measured»; a -1 would be
        /// read as a number.
        /// </summary>
        private static object NumberOrNull(int n)
        {
            return n >= 0 ? (object)n : null;
        }

        /// <summary>A volume for the journal: the number, or «?» if it could not be read.</summary>
        private static string ReadValue(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.#") : "?";
        }

        /// <summary>A volume for an answer: rounded, or null if it could not be read.</summary>
        private static object NumberOrNull(double? v, int digits)
        {
            return v.HasValue ? (object)Math.Round(v.Value, digits) : null;
        }

        /// <summary>
        /// The refusal when the volume cannot be read BEFORE doing something
        /// (entry Z-C8). It costs nothing: the part has not been touched yet, and
        /// without that number every later check would be a comparison with
        /// air. It is the same road as `MoveBodies` with `MassOf`.
        /// </summary>
        private static string VolumeNotReadBefore(string what)
        {
            return "I cannot measure the volume of the part before " + what + ": without that " +
                   "number I could not tell you what changed. I touched nothing.";
        }

        /// <summary>
        /// The note when the volume cannot be reread AFTER (entry Z-C8). Here we do not
        /// refuse: the feature is there, and throwing it away over a failed reading
        /// would be worse. Not even a number is written: it says that the
        /// material checks were not done. It is the rule of
        /// `NoteOnVolume` and of the shell, «the guard stays quiet instead of inventing»,
        /// said out loud.
        /// </summary>
        private const string NoteVolumeNotReread =
            "After the feature I could not measure the volume of the part again: " +
            "the feature is there, but I could not do the checks on the material removed " +
            "or added. Look at the part before going on.";

        /// <summary>
        /// The volume of all the solid bodies, in mm3, or **null if it could not
        /// be read** (entry Z-C8).
        ///
        /// It used to say «zero if it cannot be read», and two thousand lines further up
        /// `NoteOnVolume` explained why that zero was a false
        /// statement — defending itself alone, and leaving the hole to all the other
        /// callers. Now the type says it: `double?`, and the compiler
        /// prevents summing or rounding it without looking.
        ///
        /// ZERO-E-ZERO: `GetBodies2` null on a live part means no
        /// solid body, and the volume really is zero. A body that does not give its
        /// mass properties makes the WHOLE thing null: skipping it would give a
        /// partial sum presented as whole.
        ///
        /// INDEX 3 IS MEASURED, NOT REMEMBERED. `IBody2.GetVolume` does not
        /// exist (it is among the seven APIs that seemed right); the real
        /// alternative is `GetMassProperties(density)`, which returns a vector.
        /// On 9 September 2026, measuring the guided holes, element 3 turned
        /// out to be the volume in cubic meters: an M8 hole on a 20 plate
        /// removed 726.3 mm3, that is pi*3.4^2*20 to a tenth of a cubic millimeter,
        /// and so for all eleven sizes from M3 to M24. On a vector
        /// read wrong those numbers would not add up.
        /// </summary>
        private static double? VolumeMm3(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return null;
            double v = 0;
            try
            {
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null) return 0;
                foreach (var c in bodies)
                {
                    var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                    if (mp == null || mp.Length <= 4) return null;
                    v += mp[3] * 1e9;
                }
            }
            catch { return null; }
            return v;
        }

        /// <summary>
        /// Selects the edges indicated by the plan, by reference to a step
        /// or by state identifier.
        ///
        /// IT LIVES IN A SINGLE METHOD BECAUSE TWO COPIES DIVERGE.
        /// Fillet and chamfer name the edges in the very same way, and
        /// all the checks that matter are here: that the cited step exists,
        /// that the identifier is still the same one (uids are positional and get
        /// reassigned at every reading), and that the measure asked for fits within
        /// the edge's length. Written twice, the day one is
        /// corrected the other is left behind.
        ///
        /// AND THE TWO ROADS INSIDE THIS METHOD WERE ALREADY DIVERGING.
        /// The method was one, but it had two branches — by uid and by reference to
        /// a step — and the second exited with `return true` before reaching
        /// the check on the measure. A single method was enough to believe oneself
        /// safe: the comment above promised a check that half of the
        /// method did not do. Measured on 14/9/2026, repaired in 0.25.2.
        /// Putting two roads under the same comment is no less dangerous
        /// than putting two copies under two names.
        /// </summary>
        /// <param name="maximumMeasure">
        /// Radius or side: it cannot exceed half the edge. Zero = do not
        /// check.
        /// </param>
        private bool SelectEdges(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState,
            double maximumMeasure, out int howMany, out string reason)
        {
            howMany = 0;
            reason = null;
            var uid = StringList(a, "edges_uid");

            // ── Edges named by reference to a previous step ──
            //
            // «Round the four corners of the plate you make at step 1»:
            // when the plan was written those edges did not exist.
            // Now they exist, and the selector finds them.
            if (HasReference(a, "edges_ref"))
            {
                int number;
                var planStep = CitedStep(a, "edges_ref", out number, out reason);
                if (planStep == null)
                {
                    reason = reason ?? "Reference not resolved.";
                    return false;
                }

                List<IEntity> foundItems;
                if (!References.Edges(planStep, KindOf(a, "edges_ref"), out foundItems, out reason))
                    return false;

                // THE MEASURE IS CHECKED HERE TOO, and up to 0.25.2 it was not.
                //
                // The comment of this method promised that «all the
                // checks that matter are here», and among those it listed that
                // the measure asked for fits within the edge's length. But
                // this branch exited with `return true` BEFORE getting there: the
                // same 30 mm fillet on a 10 mm edge was
                // refused with `edges_uid` and let through with
                // `edges_ref`, as far as FeatureFillet3 which fails saying
                // only that it was not created.
                //
                // Here there is no signature to read the measure from — the edges
                // arrive from the step, not from the state — so they are measured
                // now, which is also the right moment: between the proposal and
                // the execution the part may have changed.
                if (maximumMeasure > 0)
                {
                    double shorter = 0;
                    foreach (var e in foundItems)
                    {
                        double len = DocumentState.LengthMm(e as IEdge);
                        if (len <= 0) continue;          // not measurable: a verdict is not invented
                        if (maximumMeasure <= len / 2) continue;
                        if (shorter == 0 || len < shorter) shorter = len;
                    }
                    if (shorter > 0)
                    {
                        reason = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0:0.##} mm is too much for the edges of step {1}: the shortest " +
                            "is {2:0.##} mm long, and not even half of it fits. " +
                            "The most that fits is {3:0.##} mm.",
                            maximumMeasure, number, shorter, shorter / 2);
                        return false;
                    }
                }

                model.ClearSelection2(true);
                foreach (var e in foundItems) e.Select4(true, null);

                // THE TRUE NUMBER IS GIVEN BY SOLIDWORKS, NOT OUR COUNT.
                // The same entity met on two faces is selected only
                // once: `found.Count` would be higher than the real one, and
                // would end up in the message the user reads afterwards.
                try
                {
                    var selection = model.SelectionManager as ISelectionMgr;
                    if (selection != null) howMany = selection.GetSelectedObjectCount2(-1);
                }
                catch { }
                if (howMany == 0)
                {
                    reason = "The edges of step " + number +
                             " could not be selected.";
                    return false;
                }
                _log("[agent] " + howMany + " edges taken from step " + number);
                return true;
            }

            if (uid.Count == 0)
            {
                reason = "No edge indicated.";
                return false;
            }

            model.ClearSelection2(true);
            int selected = 0;
            foreach (var u in uid)
            {
                // Edges too are numbered by position, ordered by
                // length: after a step that changes the body, «E7» is a
                // different edge. Same check as the faces, and since entry E19
                // the same search by signature.
                var signature = EdgeSignature(
                    a, docState, u, "The edge «" + u + "» is no longer in this document.",
                    out reason);
                if (signature == null) return false;
                var edge = signature.Entity as IEntity;
                if (edge == null)
                {
                    reason = "The edge «" + u + "» can no longer be selected.";
                    return false;
                }

                // The measure cannot exceed half the edge: the check
                // is redone HERE, on the length measured now, because between the
                // proposal and the execution the part may have changed.
                if (maximumMeasure > 0 && signature.Measure > 0 && maximumMeasure > signature.Measure / 2)
                {
                    reason = string.Format(
                        "{0:0.##} mm is too much for the edge «{1}», which is {2:0.##} mm long: " +
                        "not even half of it fits.",
                        maximumMeasure, u, signature.Measure);
                    return false;
                }

                if (!edge.Select4(true, null))
                {
                    reason = "I could not select the edge «" + u + "».";
                    return false;
                }
                selected++;
            }

            howMany = selected;
            return true;
        }

        /// <summary>
        /// The fillet itself, on what is already selected. <paramref name="volumeBefore"/>
        /// is the volume read before the selection, for EdgeFeatureVolumeCheck
        /// (Z-C21.8, finding M37).
        /// </summary>
        private Outcome RunFillet(IModelDoc2 model, double radius, int howManyEdges, double? volumeBefore)
        {
            var options = (int)(swFeatureFilletOptions_e.swFeatureFilletUniformRadius |
                                swFeatureFilletOptions_e.swFeatureFilletPropagate);
            var feature = model.FeatureManager.FeatureFillet3(
                Options: options,
                R1: Units.M(radius),
                R2: 0,
                Rho: 0,
                Ftyp: (int)swFeatureFilletType_e.swFeatureFilletType_Simple,
                OverflowType: (int)swFilletOverFlowType_e.swFilletOverFlowType_Default,
                ConicRhoType: (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                Radii: null,
                Dist2Arr: null,
                RhoArr: null,
                SetBackDistances: null,
                PointRadiusArray: null,
                PointDist2Array: null,
                PointRhoArray: null) as IFeature;

            if (feature == null) return Failed("The fillet was not created.");
            var measures = new Dictionary<string, object>
            {
                ["radius_mm"] = radius,
                ["edges"] = howManyEdges,
            };
            SelectedEdgesAndFaces(feature, howManyEdges, measures);
            // After the counts, which SelectedEdgesAndFaces reads right after
            // creation as the probe did: the check rebuilds before measuring.
            string filletUnchanged = EdgeFeatureVolumeCheck(model, "fillet", volumeBefore, measures);
            if (filletUnchanged != null) return Failed(filletUnchanged);
            return Succeeded(feature, measures);
        }

        /// <summary>
        /// THE REPLY OF A FILLET OR A CHAMFER SAYS WHAT WAS SELECTED AND WHAT WAS
        /// MADE (finding N14, Z-C21.3). «edges» is the number of edges the
        /// caller passed, kept under its old name for the callers that read it;
        /// but with tangent propagation one selected edge can work a whole loop.
        /// Measured on 28/9/2026 (Z-C21.3 edge-count probe, a block with its four
        /// vertical edges rounded r 5): a fillet r 2 on ONE top edge, with these
        /// options, removed 110.399 mm3 and owned 8 faces; a chamfer 2 x 45 on one
        /// bottom edge, 254.454 mm3 and 8 faces; the reply said «edges: 1» both
        /// times. So the reply also carries «selected_edges» (the same count,
        /// under the name of what it is) and «faces», IFeature.GetFaceCount of the
        /// new feature (IFeature~GetFaceCount.html: «Gets the number of faces in
        /// this feature»), read right after creation as the probe read it. The
        /// document state carries the same two fields for every fillet and
        /// chamfer (DocumentState.EdgeAndFaceCounts).
        /// </summary>
        private static void SelectedEdgesAndFaces(IFeature feature, int selected, Dictionary<string, object> measures)
        {
            measures["selected_edges"] = selected;
            try
            {
                int faces = feature.GetFaceCount();
                if (faces > 0) measures["faces"] = faces;
            }
            catch { }
        }

        private Outcome CircularPattern(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double? volumeBefore; int bodiesBefore, facesBefore;
            Snapshot(model, out volumeBefore, out bodiesBefore, out facesBefore);
            if (volumeBefore == null || bodiesBefore < 0 || facesBefore < 0)
                return Failed(VolumeNotReadBefore("making the copy"));

            int instances = (int)Num(a, "instance_count");
            double angle = Num(a, "total_angle_deg", 360);
            string featureUid = Str(a, "feature_uid", "");

            model.ClearSelection2(true);
            // The axis must be selected FIRST with Mark = 1: the circular
            // pattern takes the direction from the selection, not from the parameters.
            string why;
            if (!SelectAxis(model, a, "axis", docState, SelectionMarks.CircularPatternAxis, append: false, out why)) return Failed(why);

            // The feature to repeat can be the one made by a previous
            // step of this same plan — «make a hole and repeat it 6
            // times». In that case the OBJECT is selected, not the name: the
            // name SolidWorks gave it we would know only by rereading it, and
            // it would be an extra trip to get to the same feature that
            // we already have in hand.
            if (HasReference(a, "feature_ref"))
            {
                int number;
                var previous = CitedStep(a, "feature_ref", out number, out why);
                if (previous == null) return Failed(why ?? "Reference not resolved.");
                if (previous.Feature == null)
                    return Failed($"The step {number} did not leave a feature to repeat.");
                if (!previous.Feature.Select2(true, SelectionMarks.CircularPatternFeatures))
                    return Failed($"I could not select the feature of step {number}.");
                _log($"[agent] repeating the feature of step {number}");
            }
            else if (!model.Extension.SelectByID2(featureUid, "BODYFEATURE", 0, 0, 0, true, SelectionMarks.CircularPatternFeatures, null, 0))
            {
                return Failed($"I did not find the feature «{featureUid}» to repeat.");
            }

            var seed = SelectedSeed(model, SelectionMarks.CircularPatternFeatures);

            var feature = model.FeatureManager.FeatureCircularPattern5(
                Number: instances,
                Spacing: Units.Rad(angle),
                FlipDirection: false,
                DName: "",
                GeometryPattern: false,
                EqualSpacing: true,
                VaryInstance: false,
                SyncSubAssemblies: false,
                BDir2: false,
                BSymmetric: false,
                Number2: 1,
                Spacing2: 0,
                DName2: "",
                EqualSpacing2: false) as IFeature;

            if (feature == null) return Failed("The pattern was not created.");
            var measurements = new Dictionary<string, object>
            {
                ["instances"] = instances,
                ["angle_deg"] = angle,
            };
            var stopped = CompareCopy(
                model, volumeBefore, bodiesBefore, facesBefore, measurements,
                "The circular pattern");
            if (stopped != null) return Failed(stopped);

            var missingItems = MissingCopies(
                measurements, feature, seed, instances - 1, "The circular pattern",
                "With an angle smaller than the full turn the copies stop " +
                "where the arc ends: check «total_angle_deg», or bring " +
                "the instances down to how many fit.");
            if (missingItems != null) return Failed(missingItems);

            return Succeeded(feature, measurements);
        }

        // ─────────────────────────────────────────────────────────────────
        // Mattoni comuni
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// THE SKETCH A CALLER HAS JUST DRAWN AND CLOSED, BY THE DIFFERENCE OF
        /// THE TREE (scheme S7, Z-C21.5): the one ProfileFeature whose name was
        /// not in <paramref name="namesBeforeSketch"/>, the names the caller
        /// took with <see cref="FeatureNamesNow"/> BEFORE it selected the plane
        /// or face and opened the sketch. Null when none was born, when more
        /// than one was, when the names were not taken or the tree cannot be
        /// read, and on anything but a part; <paramref name="born"/> says how
        /// many sketches were born (-1: not read). The decision is
        /// <see cref="BornFeatureIndex"/>, the same as for the planes and axes.
        ///
        /// UNTIL LIBRARY 0.146.0 it was the LAST ProfileFeature of the tree,
        /// which is the new sketch only when SOLIDWORKS puts it at the bottom
        /// and when it is born at all: a sketch that was not born left the
        /// previous one, a sketch an earlier step had already used, to be
        /// selected in its place. IPartDoc~IFirstFeature.html
        /// (Remarks): «this order changes when the part is edited» and «you
        /// should not assume that Sketch1 always appears before Sketch2».
        ///
        /// THE NAMES ARE READ, NEVER WRITTEN. Feature names are localized (an
        /// English installation says «Sketch1», an Italian one another word
        /// for it) and a SelectByID2 on a name we made up answers false in
        /// silence. Here a name is only compared with the names read from the
        /// same tree a moment before, and the feature is then selected as an
        /// object. IFeature~Name.html (Remarks): a new name «must be unique in
        /// the FeatureManager design tree», so a name that was not there before
        /// is a feature that was not there before. The type only narrows:
        /// IFeature~GetTypeName2.html lists ProfileFeature under Sketch, with
        /// ISketch as its specific feature (3DProfileFeature is the 3D sketch).
        ///
        /// Not ISketchManager.ActiveSketch taken as a feature while the sketch
        /// is still open: only a guide example does that cast
        /// (Rename_Active_Sketch_Example_VB.htm, «Set swFeat = swSketch» after
        /// GetActiveSketch2), no Remarks says it, and nothing here can measure
        /// it today; the names before and after are what FeatureBorn already
        /// uses for every other feature a call makes.
        /// </summary>
        private static IFeature JustCreatedSketch(IModelDoc2 model, HashSet<string> namesBeforeSketch, out int born)
        {
            return FeatureBorn(model, namesBeforeSketch, "ProfileFeature", out born);
        }

        /// <summary>
        /// Why the sketch just drawn could not be identified. With none born the
        /// caller's own sentence, the one it has always said; with an unreadable
        /// tree the same sentence and why; with two or more born, how many:
        /// two candidates, and taking the later would be «the last sketch of the
        /// tree» again. Pure, so that Oce.Solid.Agent.ContractTests checks it without
        /// SOLIDWORKS (Zc215SketchIdentityContract).
        /// </summary>
        internal static string SketchNotIdentified(int born, string notFound)
        {
            if (born > 1)
                return born + " sketches were born since this sketch was opened, and nothing tells which " +
                       "one was just drawn: none is taken.";
            if (born < 0)
                return notFound + " The feature tree could not be read before or after the sketch, so the " +
                       "new sketch cannot be told from the others.";
            return notFound;
        }

        /// <summary>
        /// Selects the sketch just closed, ready to be extruded or cut. False
        /// when it cannot be identified or selected.
        ///
        /// WHY IT IS NEEDED. `InsertSketch(false)` leaves the sketch but does
        /// NOT leave it selected, and FeatureExtrusion3 works on what is
        /// selected: without this step the feature is simply not born. It
        /// happened on 7 September 2026 at the first real extrusion: the sketch
        /// stayed in the tree, unused, and the plan stopped saying that the
        /// extrusion was not created, with no other explanation. The
        /// parameters were right: the selection was missing.
        ///
        /// WHICH SKETCH: the one born since <paramref name="namesBeforeSketch"/>
        /// (<see cref="JustCreatedSketch"/>), never the last of the tree.
        /// </summary>
        private bool SelectJustCreatedSketch(IModelDoc2 model, HashSet<string> namesBeforeSketch, out string error)
        {
            error = null;
            int born;
            var sketch = JustCreatedSketch(model, namesBeforeSketch, out born);
            if (sketch == null)
            {
                error = SketchNotIdentified(born, "I cannot find the sketch I just created.");
                return false;
            }
            try
            {
                model.ClearSelection2(true);
                if (!sketch.Select2(false, 0))
                {
                    error = "I could not select the sketch just created.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "Selecting the sketch did not succeed: " + ex.Message;
                return false;
            }
            return true;
        }

        /// <summary>
        /// REMOVES THE SKETCH WHEN THE FEATURE WAS NOT BORN.
        ///
        /// THE DEFECT IT COMES FROM. On 11 September 2026, eight requests beyond the
        /// limits on a rectangular box: seven right refusals, zero
        /// silent failures — and seven orphan sketches in the part. Every refusal said
        /// «you can remove it with Ctrl+Z», which is honest and is not a repair:
        /// it is a task handed to the reader, and it multiplies by the number of
        /// attempts.
        ///
        /// WHY IT CAN BE DONE NOW, AND COULD NOT BEFORE. The rule at the top of this
        /// file — take no initiatives inside someone's document —
        /// remains valid for the features that have REMOVED or ADDED material:
        /// those are undone with Ctrl+Z and nothing else (see
        /// <see cref="RemainsInPart"/>). A sketch that produced nothing
        /// is a different case: it is not a change to the part, it is a refusal
        /// of ours left lying on the floor. Removing it is not initiative, it is cleaning.
        ///
        /// FOUR CHECKS BEFORE DELETING, BECAUSE DELETING THE WRONG FEATURE
        /// IS MUCH WORSE THAN LEAVING A SKETCH.
        ///
        ///   1. IS IT A SKETCH? We do not look at the type name — names are
        ///      translated, and it is the mistake this file has always avoided. We
        ///      ask for the specific interface: `IFeature.GetSpecificFeature2`
        ///      returns ISketch for a 2D sketch and for a 3D one, and returns NULL for
        ///      extrusions, cuts, fillets, chamfers and lofts. It is written in the
        ///      2021 API guide, entry GetSpecificFeature2: «For some feature
        ///      types, this method returns Nothing or null because there is no
        ///      specific object for that type of feature (e.g., features such
        ///      as extrusions, lofts, fillets, chamfers, etc.)». So: if the
        ///      feature at the bottom of the tree is the one we wanted to create, here
        ///      we exit.
        ///
        ///      AND THEN THE ORIGIN IS EXCLUDED, which is the trap measured on 14
        ///      September 2026 walking the tree of a tub: the part's origin
        ///      is of type `OriginProfileFeature` and to
        ///      GetSpecificFeature2 it answers **ISketch too**. On an
        ///      empty document it is the last feature of the tree. The type is
        ///      looked at to REFUSE, never to accept: if the type name
        ///      were different from what we expect, all the other checks
        ///      remain standing.
        ///
        ///   2. IS IT AT THE BOTTOM OF THE TREE? We look at the LAST feature, not
        ///      «the last sketch». And here is the second fact measured on 14
        ///      September 2026, which is the opposite of what it seems: an ABSORBED
        ///      sketch stays anyway in the top-level walk of
        ///      `IFirstFeature`/`GetNextFeature`. On a tub made with a
        ///      single extrusion the tree is: ... `#18 ProfileFeature Sketch1`,
        ///      `#19 Extrusion Extrude1`. That is, a successful extrusion
        ///      adds TWO features to the count, not one — and the sketch is
        ///      second to last, not last. It is exactly the reason why the
        ///      check is «the LAST one is a sketch» and not «there is a sketch
        ///      at the tail»: when the feature is born, after the sketch comes
        ///      the feature.
        ///
        ///   3. DID THIS STEP MAKE IT? It must be born in this step: its name
        ///      was not in the tree when the step began
        ///      (<see cref="_featureNamesAtStepStart"/>). A sketch that was
        ///      already there belongs to someone else and is not touched.
        ///
        ///   2 AND 3 SINCE Z-C21.5 (scheme S7): «the last feature» of 2 is the
        ///   newest of the features BORN IN THIS STEP, not the last feature of
        ///   the tree. On a sheet-metal part the tail of the tree is the flat
        ///   pattern and a new feature goes before it: the old check found the
        ///   flat pattern, which is no sketch, and left every orphan sketch of a
        ///   failed step in the part. Among the features born in this step the
        ///   newest is the last in tree order, because each was inserted after
        ///   the one before it; a feature born after the sketch (the one the
        ///   step wanted) still makes the check give up.
        ///
        ///   4. DOES ANYTHING DEPEND ON IT? `IGetChildCount` must be zero, and
        ///      it is the check that weighs most of the four. Again on the 14
        ///      September: the sketch absorbed by a successful extrusion has
        ///      **1 child**, which is the extrusion. An orphan from a failure
        ///      has nobody using it. So this number is not a generic
        ///      caution: it is the difference between «consumed» and
        ///      «abandoned», asked of SolidWorks instead of deduced.
        ///
        /// DeleteSelection2 AND NOT EditDelete, AND THE REASON IS A KNOWN TRAP.
        /// The 2021 API guide, in the Remarks of IModelDoc2::EditDelete, says:
        /// «Use IModelDocExtension::DeleteSelection2 to turn off prompting the
        /// user to confirm the deletion». EditDelete opens a confirmation
        /// window: inside the add-in, which runs IN the SolidWorks process, the
        /// COM call would never return and SolidWorks would be stuck —
        /// it is exactly what AddDimension did with
        /// swInputDimValOnCreate on, ten minutes stopped, measured on 11
        /// September 2026. DeleteSelection2 returns a bool and asks
        /// nobody anything. The option is swDelete_Absorbed: the sketch's dimensions
        /// are absorbed by it and must go away with it.
        /// swDelete_Children is NOT passed: if a child were there, check
        /// 4 has already given up, and passing the flag would mean deleting
        /// more precisely in the case we have not understood.
        ///
        /// AND AFTERWARDS WE MEASURE. Volume, number of bodies and number of features: the
        /// first and the second must be identical, the third must have
        /// dropped by ONE. If it does not add up, we say <see cref="SketchRemovedButNotOnlyIt"/>,
        /// which is the only honest answer: <see cref="LeftSketchBehind"/> at that
        /// point would be a lie covering up damage.
        /// </summary>
        /// <returns>
        /// Empty string if the sketch was removed and the part is what it was
        /// before — there is nothing to add to the reason for the failure;
        /// <see cref="LeftSketchBehind"/> if it could not be removed;
        /// <see cref="SketchRemovedButNotOnlyIt"/> if removing it changed the part.
        /// </returns>
        // ══════════════════════════════════════════════════════════════════
        // R2 — REMOVING a feature from the tree
        //
        // Up to 0.33.0 the thirty-six operations were all additive or
        // parametric: the tree could only GROW. The only correction was
        // `set_dimension` — one changes a number, one does not withdraw a choice — and
        // `undo` (R1), which however undoes the whole LAST plan and not a
        // feature in the middle.
        //
        // These two remove any feature whatsoever: `suppress_feature`
        // turns it off (it can be turned back on), `delete_feature` deletes it.
        //
        // ── THE HARD PART IS THE REFUSAL, as for `undo` ───────────────
        //
        // **Features have children.** Removing the base extrusion makes
        // everything above it collapse, and SOLIDWORKS does it **without
        // asking**. The `IGetChildCount()` guard already existed for the
        // orphan sketch (`WithoutSketch`, point 4); here it is extended, and when
        // there are children **we refuse saying which** — we do not choose on behalf
        // of whoever is drawing.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>The feature carrying that name, or null.</summary>
        private static IFeature FindFeature(IModelDoc2 model, string uid)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return null;
            try
            {
                var f = partDoc.IFirstFeature();
                while (f != null)
                {
                    string itemName = null;
                    try { itemName = f.Name; } catch { }
                    if (string.Equals(itemName, uid, StringComparison.OrdinalIgnoreCase)) return f;
                    f = f.IGetNextFeature();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The names of what depends on this feature.
        ///
        /// They serve to WRITE them in the refusal. «I cannot, it has children» sends the
        /// reader to look for them by hand; «I cannot, Cut-Extrude1 and
        /// Fillet2 sit on it» tells them what to remove first, or lets them realize they
        /// picked the wrong feature.
        /// </summary>
        private static List<string> ChildrenOf(IFeature f)
        {
            var names = new List<string>();
            try
            {
                var children = f.GetChildren() as object[];
                if (children == null) return names;
                foreach (var g in children)
                {
                    var child = g as IFeature;
                    if (child == null) continue;
                    try { names.Add(child.Name); } catch { }
                }
            }
            catch { }
            return names;
        }

        /// <summary>
        /// The names of the features that exist, for a refusal that helps
        /// instead of merely stating. Short: twelve are enough to convey the shape
        /// of the names.
        /// </summary>
        private static string FeaturesThatExist(IModelDoc2 model)
        {
            var names = new List<string>();
            var partDoc = model as IPartDoc;
            if (partDoc == null) return "";
            try
            {
                var f = partDoc.IFirstFeature();
                while (f != null && names.Count < 12)
                {
                    string itemName = null;
                    try { itemName = f.Name; } catch { }
                    if (!string.IsNullOrEmpty(itemName)) names.Add(itemName);
                    f = f.IGetNextFeature();
                }
            }
            catch { }
            return string.Join(", ", names);
        }

        /// <summary>
        /// The named feature, with all the checks that apply to
        /// both operations. Null with the reason if it cannot be done.
        /// </summary>
        private IFeature FeatureToTouch(
            IModelDoc2 model, Dictionary<string, object> a, out string reason)
        {
            reason = null;
            string uid = Str(a, "feature_uid", "");
            if (string.IsNullOrWhiteSpace(uid))
            {
                reason = "You did not say which feature. The names are in the " +
                         "state of the document, inside «feature» (for example " +
                         "«Cut-Extrude2»).";
                return null;
            }

            // IS IT STILL THAT ONE? A name can be renamed, `GetID()` cannot. Same
            // defense as `linear_pattern` and `mirror`, and here it weighs more:
            // there a wrong feature is copied, here it is deleted.
            if (!FeatureIsThatOne(model, uid, a, out reason)) return null;

            var f = FindFeature(model, uid);
            if (f == null)
            {
                string ci = FeaturesThatExist(model);
                reason = "I cannot find the feature «" + uid + "» in this document." +
                         (ci.Length > 0 ? " There are: " + ci + "." : "");
                return null;
            }

            // THE ORIGIN IS NOT TOUCHED. It is a special sketch on which everything may
            // depend, and `GetChildren` does not always tell.
            string typ = "";
            try { typ = f.GetTypeName2(); } catch { }
            if (typ == PartOrigin)
            {
                reason = "«" + uid + "» is the origin of the part: it is not a " +
                         "feature and with it removed nothing would be left.";
                return null;
            }
            return f;
        }

        /// <summary>
        /// TURNS OFF a feature, or turns it back on.
        ///
        /// ── WHY SUPPRESS AND NOT DELETE ───────────────────────────────────
        ///
        /// Suppression **can be undone**: it is the road to take when one
        /// wants to see the part without that feature without committing.
        /// The state already shows it — `suppressed: true` — since 0.25.2, and that
        /// field existed before there was a way to set it: it was a
        /// display without a switch.
        ///
        /// ── THE IDENTIFIERS CHANGE, AND IT MUST BE SAID ───────────────────
        ///
        /// Suppressing a feature makes its faces disappear, and uids are
        /// **positional**: they are renumbered by area at every reading. Measured
        /// in measurement 0.1: suppressing the generating feature
        /// two faces out of eight disappeared. Whoever holds a plan written on «F3»
        /// after this no longer has «F3»: the answer says so, and does not leave them
        /// to discover it at the next step.
        ///
        /// **Children do not stop the suppression, but they are reported.** Suppressing a
        /// parent turns off the children too, and it is what one usually wants («remove
        /// the hole and its fillets»). Different from `delete_feature`, where
        /// children stop everything: there is no going back.
        /// </summary>
        private Outcome SuppressFeature(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string reason;
            var f = FeatureToTouch(model, a, out reason);
            if (f == null) return Failed(reason);

            string uid = Str(a, "feature_uid", "");
            bool reactivate = Bool(a, "reactivate", false);

            // Z-C21.7 (S3): IsSuppressed2 in the active configuration, not the
            // obsolete IsSuppressed; guide page and probe numbers in
            // ApiHelpers.IsSuppressedHere.
            bool wasSuppressed = false;
            try { wasSuppressed = ApiHelpers.IsSuppressedHere(f); } catch { }
            if (wasSuppressed == !reactivate)
            {
                // ALREADY AS ASKED: it is not an error, and it is important not to
                // treat it as one — a plan that asks for it twice has broken
                // nothing. But we do not just say «done», because then
                // «done» would no longer tell a change from nothing.
                return Failed(
                    "«" + uid + "» is already " +
                    (wasSuppressed ? "suppressed" : "active") +
                    ": I changed nothing. If you wanted the opposite, " +
                    (wasSuppressed
                        ? "pass «reactivate»: true."
                        : "remove «reactivate»."));
            }

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            var children = ChildrenOf(f);

            // IN ALL THE CONFIGURATIONS, for the same reason as
            // `set_dimension`: suppressing in only one would leave the part
            // different depending on how it is looked at.
            const int inAllConfigurations = 2;
            int action = reactivate
                ? (int)swFeatureSuppressionAction_e.swUnSuppressFeature
                : (int)swFeatureSuppressionAction_e.swSuppressFeature;

            bool done;
            try { done = f.SetSuppression2(action, inAllConfigurations, null); }
            catch (Exception ex)
            {
                return Failed("SOLIDWORKS did not " +
                               (reactivate ? "reactivate" : "suppress") +
                               " «" + uid + "»: " + ex.Message);
            }
            if (!done)
                return Failed("SOLIDWORKS did not " +
                               (reactivate ? "reactivate" : "suppress") +
                               " «" + uid + "», and did not say why.");

            try { model.EditRebuild3(); } catch { }

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);

            var measurements = new Dictionary<string, object>
            {
                ["feature"] = uid,
                ["suppressed"] = !reactivate,
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["bodies"] = NumberOrNull(bodiesAfter),
                // THE IDENTIFIERS ARE NO LONGER VALID, and it is always said: it is the
                // costliest defect of the project — a right operation done
                // blind — and here the blindness is guaranteed.
                ["note_identifiers"] =
                    "The faces changed: the identifiers (F1, E4) from before " +
                    "are no longer valid. Read the part again before the next " +
                    "step.",
            };
            if (bodiesBefore >= 0 && bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                measurements["note_bodies"] =
                    "The bodies went from " + bodiesBefore + " to " + bodiesAfter + ".";
            if (children.Count > 0)
                measurements["note_children"] =
                    "What depends on «" + uid + "»: " + string.Join(", ", children) +
                    ": " + (reactivate ? "they came back on with it." : "they went off with it.");

            _log("[agent] " + (reactivate ? "reactivated" : "suppressed") + " " + uid +
                 ", volume " + ReadValue(volumeBefore) + " -> " + ReadValue(volumeAfter));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// DELETES a feature. There is no going back with another
        /// operation: only with `undo`.
        ///
        /// ── CHILDREN STOP EVERYTHING, AND HERE THEY DO ────────────────────
        ///
        /// In `suppress_feature` children are turned off together with the parent and
        /// turned back on together. Here they would disappear, and **SOLIDWORKS does it without
        /// asking**. So we refuse, saying which they are: the reader decides
        /// whether to remove them first or whether they had another feature in mind.
        ///
        /// It is the same guard that `WithoutSketch` uses at point 4, extended from
        /// «the sketch I just made myself» to «any feature whatsoever».
        ///
        /// ── `DeleteSelection2` AND NOT `EditDelete` ───────────────────────
        ///
        /// The documentation says so, and the reason is a known trap:
        /// `EditDelete` opens the confirmation window, and a COM call that
        /// opens a modal **does not return** — it is the same trap as
        /// `AddDimension`. `DeleteSelection2` answers a bool and asks
        /// nobody anything.
        ///
        /// `swDelete_Absorbed` also removes the absorbed sketch: without it, an orphan sketch
        /// that does nothing stays in the tree and confuses whoever
        /// reads the state.
        ///
        /// A SHARED SKETCH IS SPARED, MEASURED (Z-C21.9, finding M33, not real).
        /// DeleteSelection2.html says nothing about a sketch that two features
        /// use, and the audit called the effect undocumented. The Z-C21.3 probe
        /// probe_shared_sketch_delete (SOLIDWORKS 29.5.1) measured it: with two
        /// extrusions on the same sketch (A 16000 mm3, B on top, 24000 in all),
        /// deleting B with swDelete_Absorbed left the sketch alive, with A as its
        /// only child and no error, and the volume went from 24000 to 16000, A
        /// intact (option 0, the guide's, gave the same). The control, one
        /// extrusion on its own sketch, took the sketch with it. Deleting A
        /// first is refused here anyway: the probe read IGetChildCount 1 on A.
        /// </summary>
        private Outcome DeleteFeature(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string reason;
            var f = FeatureToTouch(model, a, out reason);
            if (f == null) return Failed(reason);

            string uid = Str(a, "feature_uid", "");

            var children = ChildrenOf(f);
            int howManyChildren = 0;
            try { howManyChildren = f.IGetChildCount(); } catch { howManyChildren = children.Count; }
            if (howManyChildren > 0)
                return Failed(
                    "I do not delete «" + uid + "»: there " +
                    (howManyChildren == 1 ? "is" : "are") + " " +
                    (children.Count > 0
                        ? string.Join(", ", children)
                        : howManyChildren + " feature" + (howManyChildren == 1 ? "" : "s")) +
                    " depending on it. If I deleted it, " + (howManyChildren == 1 ? "that feature" : "those features") +
                    " would disappear too" + (howManyChildren == 1 ? "" : ", all of them") +
                    ", and SOLIDWORKS would do it without asking.\n\n" +
                    "If it really is that one you want to remove, " +
                    "first remove what sits on top of it. If you only need to see the part without it, " +
                    "use «suppress_feature»: it also turns off the children and it can be undone.");

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            int featureBefore = CountFeature(model);

            bool removed;
            try
            {
                model.ClearSelection2(true);
                if (!f.Select2(false, 0))
                    return Failed("I could not select «" + uid + "».");
                removed = model.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                return Failed("SOLIDWORKS did not delete «" + uid + "»: " + ex.Message);
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }

            if (!removed)
                return Failed("SOLIDWORKS did not delete «" + uid + "», and did " +
                               "not say why. The part stayed as it was.");

            try { model.EditRebuild3(); } catch { }

            // ── AND NOW WE CHECK THAT IT REALLY IS GONE ───────────────────
            //
            // `DeleteSelection2` answered true. It is not enough: the question is whether
            // that name is no longer in the tree. It is the difference between «it
            // said it had done it» and «it did it» — the same distance
            // this project has been chasing for fifty entries.
            if (FindFeature(model, uid) != null)
                return Failed(
                    "I asked to delete «" + uid + "», SOLIDWORKS " +
                    "answered yes, and the feature is still in the tree. I cannot " +
                    "tell you what happened: look at the part before doing " +
                    "anything else.");

            int featureAfter = CountFeature(model);
            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);

            var measurements = new Dictionary<string, object>
            {
                ["feature_removed"] = uid,
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["features_before"] = NumberOrNull(featureBefore),
                ["features_after"] = NumberOrNull(featureAfter),
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["note_identifiers"] =
                    "The faces changed: the identifiers (F1, E4) from before " +
                    "are no longer valid. Read the part again before the next " +
                    "step.",
            };
            if (bodiesBefore >= 0 && bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                measurements["note_bodies"] =
                    "The bodies went from " + bodiesBefore + " to " + bodiesAfter +
                    ": removing this feature split or merged the part.";

            _log("[agent] deleted " + uid + ", features " + featureBefore +
                 " -> " + featureAfter + ", volume " + ReadValue(volumeBefore) +
                 " -> " + ReadValue(volumeAfter));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// A success that created NOTHING.
        ///
        /// `Succeeded` writes in the log «created feature X» and carries the new
        /// feature in the outcome, because the thirty-six operations there were before
        /// all created something. Removing does not — and an operation that says
        /// «created feature» while it deletes one leaves a false sentence
        /// in the register.
        ///
        /// **A new name instead of an extra parameter on `Succeeded`:**
        /// changing the set of signatures breaks whoever calls by reflection
        /// even when the compiler is silent. It is the lesson of 0.25.1, and
        /// `Succeeded` is the point EVERY success of the project goes through —
        /// the worst place to try it.
        ///
        /// The re-anchorings are drained all the same: if in finding the feature
        /// a face was found elsewhere, the reader must know it here
        /// too.
        /// </summary>
        private Outcome SucceededWithoutCreating(Dictionary<string, object> measurements)
        {
            if (_reanchors.Count > 0)
            {
                if (measurements == null) measurements = new Dictionary<string, object>();
                measurements["reanchors"] = _reanchors.ToArray();
            }
            return new Outcome
            {
                Succeeded = true,
                CreatedFeature = null,
                Feature = null,
                Measurements = measurements,
            };
        }

        private string WithoutSketch(IModelDoc2 model)
        {
            if (model == null) return LeftSketchBehind;

            // ── 2 and 3. the newest feature BORN IN THIS STEP ───────────
            // (Z-C21.5, scheme S7): not the last feature of the tree, which on
            // a sheet-metal part is the flat pattern. Without the names of the
            // step start, or with a tree that cannot be read, nobody knows what
            // this step made, and nothing is deleted (Z-C8).
            List<IFeature> features;
            List<string> names, types;
            if (_featureNamesAtStepStart == null ||
                !ReadFeatureTree(model, out features, out names, out types)) return LeftSketchBehind;
            var born = BornFeatureIndices(names, _featureNamesAtStepStart);
            if (born.Count == 0) return LeftSketchBehind;
            IFeature last = features[born[born.Count - 1]];
            int featureNow = features.Count;

            // ── 1. and it must be a sketch, and NOT the origin ──────────
            ISketch sketch = null;
            try { sketch = last.GetSpecificFeature2() as ISketch; } catch { }
            if (sketch == null) return LeftSketchBehind;
            string typ = "";
            try { typ = last.GetTypeName2(); } catch { }
            if (typ == PartOrigin) return LeftSketchBehind;

            // ── 4. and it must have no children ─────────────────────────
            try { if (last.IGetChildCount() > 0) return LeftSketchBehind; }
            catch { return LeftSketchBehind; }

            // What must NOT change, read before touching anything. If it cannot
            // be read (entry Z-C8), nothing is deleted: without the before we could not
            // say that removing the sketch did not change the part.
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            if (volumeBefore == null || bodiesBefore < 0) return LeftSketchBehind;

            string itemName = "";
            try { itemName = last.Name; } catch { }

            bool removed;
            try
            {
                model.ClearSelection2(true);
                if (!last.Select2(false, 0)) return LeftSketchBehind;
                removed = model.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                _log("[agent] I could not remove the sketch: " + ex.Message);
                return LeftSketchBehind;
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }

            if (!removed) return LeftSketchBehind;

            // ── and now we count ────────────────────────────────────────
            int featureAfter = CountFeature(model);
            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            if (volumeAfter == null || bodiesAfter < 0 || featureAfter < 0)
                return " The sketch I had drawn I removed, but I could not measure the part " +
                       "again to check that it is as it was: look at it.";
            if (featureAfter != featureNow - 1
                || bodiesAfter != bodiesBefore
                || Math.Abs(volumeAfter.Value - volumeBefore.Value) > 1e-6)
            {
                _log(string.Format(
                    "[agent] removed the sketch «{0}» but the part changed: " +
                    "features {1}->{2}, bodies {3}->{4}, volume {5:0.###}->{6:0.###} mm3",
                    itemName, featureNow, featureAfter, bodiesBefore, bodiesAfter,
                    volumeBefore, volumeAfter));
                return SketchRemovedButNotOnlyIt;
            }

            _log("[agent] removed the sketch «" + itemName + "», which the feature did not use");
            return "";
        }

        private IFeature Extrude(IModelDoc2 model, double thicknessMm, bool merge, bool symmetric = false)
        {
            // Symmetric = half on each side of the plane. It must be said in the summary
            // because it changes where the part ends up: «100 symmetric» is 50 on
            // one side and 50 on the other, and whoever reads «100» may understand
            // something quite different. swEndCondMidPlane = 6, verified in the enum.
            return model.FeatureManager.FeatureExtrusion3(
                Sd: true,                       // a single direction
                Flip: false,
                Dir: false,
                T1: symmetric
                    ? (int)swEndConditions_e.swEndCondMidPlane
                    : (int)swEndConditions_e.swEndCondBlind,
                T2: (int)swEndConditions_e.swEndCondBlind,
                D1: Units.M(thicknessMm),
                D2: 0,
                Dchk1: false, Dchk2: false,
                Ddir1: false, Ddir2: false,
                Dang1: 0, Dang2: 0,
                OffsetReverse1: false, OffsetReverse2: false,
                TranslateSurface1: false, TranslateSurface2: false,
                Merge: merge,
                UseFeatScope: true,
                UseAutoSelect: true,
                T0: (int)swStartConditions_e.swStartSketchPlane,
                StartOffset: 0,
                FlipStartOffset: false) as IFeature;
        }

        private IFeature Cut(IModelDoc2 model, bool throughAll, double depthMm)
        {
            return model.FeatureManager.FeatureCut4(
                Sd: true,
                Flip: false,
                Dir: false,
                T1: throughAll
                    ? (int)swEndConditions_e.swEndCondThroughAll
                    : (int)swEndConditions_e.swEndCondBlind,
                T2: (int)swEndConditions_e.swEndCondBlind,
                D1: throughAll ? 0 : Units.M(depthMm),
                D2: 0,
                Dchk1: false, Dchk2: false,
                Ddir1: false, Ddir2: false,
                Dang1: 0, Dang2: 0,
                OffsetReverse1: false, OffsetReverse2: false,
                TranslateSurface1: false, TranslateSurface2: false,
                NormalCut: false,
                UseFeatScope: true,
                UseAutoSelect: true,
                AssemblyFeatureScope: false,
                AutoSelectComponents: false,
                PropagateFeatureToParts: false,
                T0: (int)swStartConditions_e.swStartSketchPlane,
                StartOffset: 0,
                FlipStartOffset: false,
                OptimizeGeometry: true) as IFeature;
        }

        /// <summary>
        /// Selects one of the three base planes WITHOUT using its name, with
        /// mark 0.
        ///
        /// The names are localized (Front/Top/Right in an English installation):
        /// a SelectByID2 on the wrong name answers false in silence and the
        /// feature downstream is built on an empty selection. Until Z-C21.5 the
        /// base planes were taken as the first three RefPlane features of the
        /// tree; since then they are recognised by their geometry, the canonical
        /// normal through the origin (References.BasePlane, scheme S19), which
        /// no plane added before them can take.
        /// </summary>
        private bool SelectPlane(IModelDoc2 model, string which)
        {
            if (!(model is IPartDoc)) return false;
            // THREE NAMES AND NOTHING ELSE (Z-C21.10, finding N33). Here too a
            // final `: 1` sent ANY unknown name to the front plane: the same
            // fallback FindBasePlane dropped after the measure of 10
            // September 2026. «Upper» (accepted by the Shape ignoring
            // case, and until 0.140.0 passed on untouched) built the box on
            // the front plane with succeeded:true. An unknown name is now a
            // false, which every caller already turns into a refusal naming
            // the plane, exactly as for a Select2 that fails
            // (IFeature~Select2.html: «True if the feature was selected,
            // false if not»). The comparison stays Ordinal on purpose: the
            // Forma now rewrites every enum value in the catalogue's spelling
            // (Bridge/Shape.cs, pattern S20); References.BasePlane compares
            // Ordinal too.
            IFeature f = References.BasePlane(model, which, out _);
            if (f == null) return false;
            model.ClearSelection2(true);
            return f.Select2(false, 0);
        }

        private bool SelectFace(IFace2 face)
        {
            var entity = face as IEntity;
            if (entity == null) return false;
            return entity.Select4(false, null);
        }

        /// <summary>
        /// How far an entity's reference point may have moved
        /// between the proposal and the execution. It must match
        /// the point tolerance of the server: they are the same check made twice,
        /// and two different tolerances would mean that one of the two
        /// sides accepts what the other refuses.
        /// </summary>
        private const double PointToleranceMm = 0.05;

        /// <summary>
        /// The lengths of the edges, in the state and in the signatures, are
        /// rounded to the hundredth: five hundredths cover two roundings
        /// and stay a thousand times below the length of any real edge.
        /// </summary>
        private const double LengthToleranceMm = 0.05;
        private const double NormalTolerance = 0.01;

        /// <summary>
        /// Finds the face indicated by the step, AND VERIFIES THAT IT IS STILL THE SAME ONE.
        ///
        /// WHY LOOKING UP THE UID IS NOT ENOUGH
        /// The uids are POSITIONAL: DocumentState reassigns them at every reading
        /// enumerating the faces by decreasing area, and the executor rereads the
        /// document before every step. The plan instead is frozen at the
        /// moment of confirmation. If step 1 adds material, at step 2
        /// «F1» is another face: the feature succeeds, the rebuild is
        /// clean, the outcome says «succeeded» and the part is wrong.
        ///
        /// That is why the plan carries with it the SIGNATURE the face had
        /// when the model chose it, and here it is compared again. The comment at the
        /// top of DocumentState had always promised this check; up to
        /// 0.24.0 it did not exist anywhere in the code.
        ///
        /// The point and the normal are compared, not the area: the area changes
        /// legitimately when a hole is made on the face.
        /// </summary>
        private IFace2 ResolveFace(
            DocumentState docState,
            Dictionary<string, object> arguments,
            string uid,
            out string reason)
        {
            reason = null;

            // ── The face can come from a previous step of the plan ──
            //
            // It lives here, and not in the two cutting operations, because here is
            // the only place both of them go through: a hole and a pocket
            // must be able to name the same face in the same way, and two
            // copies of this logic would diverge at the first change.
            //
            // The reference takes precedence over the uid: the server guarantees
            // that both do not arrive, but if they arrived anyway it is
            // better to fail on a uid that is not there than to work silently
            // on the wrong face.
            if (HasReference(arguments, "face_ref"))
            {
                int number;
                var planStep = CitedStep(arguments, "face_ref", out number, out reason);
                if (planStep == null) return null;
                IFace2 fromStep;
                if (!References.Face(
                        planStep, KindOf(arguments, "face_ref"), out fromStep, out reason))
                    return null;
                _log($"[agent] face taken from step {number}");
                return fromStep;
            }

            if (docState == null || string.IsNullOrEmpty(uid))
            {
                reason =
                    "No face indicated. If the face is created by a previous " +
                    "step of this plan, it must be named with face_ref.";
                return null;
            }
            if (!docState.TryGetSignature(uid, out var signature) || signature.Type != "FACE")
            {
                reason = $"The face «{uid}» is no longer in this document.";
                return null;
            }
            var face = signature.Entity as IFace2;
            if (face == null)
            {
                reason = $"The face «{uid}» is no longer usable.";
                return null;
            }
            if (SignatureMatches(arguments, uid, signature, out reason)) return face;

            // ── THE PERSIST ID PROPOSES, THE MEASURE JUDGES (entry E6) ──────────
            //
            // The signature does not match. Before searching blind we ask
            // SOLIDWORKS: the persistent reference is the only anchor that
            // is not a conjecture of ours about the geometry.
            //
            // NEVER ALONE. `GetObjectByPersistReference3` can return WITHOUT
            // ERROR a different face after a rebuild that changes the topology:
            // it would be the worst defect of the whole section, because it would
            // give no error. What comes back is a CANDIDATE, and it goes through the
            // same judge as all the others — `Corroborates`.
            //
            // MEASURED (0.1, 14/9/2026): 8 faces out of 8 found again in all
            // five successful edits, including the three in which the anchor on the
            // point is lost. Where it gave way — the suppression of the
            // generating feature — it gave way in a DISTINGUISHABLE manner: the two
            // faces that had really ceased to exist do not come back
            // (`ErrorCode = 1`) instead of coming back wrong.
            // And `IsSamePersistentID` is not used: it answered true 46 times out of
            // 46, even where the area had doubled because two faces had
            // merged. It attests the identifier, not the face.
            var expectedPersist = ExpectedSignature(arguments, uid);
            if (expectedPersist != null)
            {
                string move;
                var fromPersist = FaceFromPersistId(docState, expectedPersist, uid, out move);
                if (fromPersist != null)
                {
                    // WE EXPLAIN BY HOW MUCH, INSTEAD OF REFUSING. It is the reason
                    // this entry exists: the face is the right one, it has
                    // only moved, and the reader must know that it moved.
                    ReAnchored(move);
                    reason = null;
                    return fromPersist;
                }
            }

            // THE SIGNATURE DOES NOT MATCH: BEFORE REFUSING, WE SEARCH.
            //
            // The uids are POSITIONAL — faces are renumbered by area at
            // every reading — so a single feature is enough for «F13» to
            // no longer be the face it was. Up to here the system
            // noticed (the signature) and stopped.
            //
            // Stopping was right and was not enough. Measured on 10 September
            // 2026 drawing the enclosure for the antenna: «cut four
            // identical slots on the four tabs» — a perfectly
            // normal request — could not be done in a single plan. The first
            // slot changed the areas, the faces were renumbered, and from the
            // second on the plan named wrong faces.
            //
            // The signature however does not serve only to notice: it serves to
            // FIND AGAIN. If in the document there is ONE single face that matches
            // the one the plan describes, it is that one — and using it is
            // more right than refusing. If there are two, we do not choose:
            // there the ambiguity is real and must be said.
            var expectation = ExpectedSignature(arguments, uid);
            if (expectation == null) return null;   // no signature: the refusal from before stays

            IFace2 foundAgain = null;
            DocumentState.EntitySignature refoundSignature = null;
            int howMany = 0;
            string discardedBecause = null;
            foreach (var entry in docState.Signatures)
            {
                if (entry.Value.Type != "FACE") continue;
                var candidate = entry.Value.Entity as IFace2;
                if (candidate == null) continue;
                string why;
                if (!SignatureMatches(arguments, uid, entry.Value, out why)) continue;

                // MATCHING BY POSITION IS NOT ENOUGH. See Corroborates.
                string grown;
                string doesNotHold = Corroborates(expectation, uid, entry.Value, out grown);
                ReAnchored(grown);
                if (doesNotHold != null)
                {
                    // We keep the FIRST reason for rejection for the message: if
                    // in the end nothing is found, the reader must know
                    // that a candidate existed and why it was not taken.
                    if (discardedBecause == null) discardedBecause = doesNotHold;
                    continue;
                }

                howMany++;
                foundAgain = candidate;
                refoundSignature = entry.Value;
            }

            if (howMany == 1)
            {
                // WE SAY BY HOW MUCH, not only THAT. Up to 0.29.0 this
                // road said «it had moved: found again by the signature» and
                // nothing more — a fact without a measure, and moreover only on
                // stderr. The measured signature is already in hand (`refoundSignature`),
                // and the sentence is written by the same function that uses the persist
                // ID: two different roads to find again, only one to say it.
                ReAnchored(
                    MoveSentence(expectedPersist, uid, refoundSignature, null)
                    ?? "the face «" + uid + "» had moved and was found again from the " +
                       "measured signature.");
                reason = null;
                return foundAgain;
            }
            if (howMany > 1)
            {
                reason =
                    "The face «" + uid + "» has moved and there are " + howMany +
                    " that look exactly like it: I do not choose. Read the state " +
                    "of the document again and name it again.";
            }
            else if (discardedBecause != null)
            {
                // The case that matters: ONE candidate existed, it matched by
                // position, and the corroboration disproved it. Before, it was
                // taken and the part came out wrong without anyone saying
                // anything.
                reason = discardedBecause;
            }
            return null;
        }

        /// <summary>
        /// THE SECOND PROOF, before saying «found again».
        ///
        /// The finding by signature already existed and was useful: the uids are
        /// positional, and without it «cut four identical slots» could not be
        /// written in a single plan. But it accepted the candidate
        /// comparing **only point and normal**, and that is not enough.
        ///
        /// MEASURED (entry E2, 14/9/2026). Plate 100x60x20 with a 40x40 pocket
        /// 8 deep: the bottom is at Z=12 with area 1600, the top face at
        /// Z=20 with area 4400. The thickness is brought to 12; the bottom drops to Z=4
        /// and **the top face drops to Z=12**, that is exactly where the
        /// plan looks for the bottom.
        ///
        /// The point matched. The area was 4400 against 1600 expected — almost
        /// triple — and it was taken all the same: `succeeded: true`, clean
        /// rebuild, and a half hole 2 deep in place of a whole hole
        /// 4 deep. **The point had won over the area.**
        ///
        /// TWO PROOFS, AND NEITHER OF THE TWO IS «THE AREA MUST MATCH».
        ///
        /// 1. **The feature must be the same**, by identifier and not
        ///    by name (`IFeature.GetID()`). A face that comes from another
        ///    feature is not the same face, however it is placed.
        ///
        /// 2. **The area can only DROP**, but ONLY when the first proof has not
        ///    already answered. It is a monotonic constraint, not a comparison: you make
        ///    a hole in it and the face loses area, and that is legitimate — it is precisely the
        ///    reason the area had been excluded from the comparison. But it does not
        ///    GAIN: a face that has grown is another piece of the part.
        ///
        /// THE AREA CHECK WAS CHASING ITS OWN TAIL, and was repaired on 14/9/2026.
        ///
        /// The uids are assigned by decreasing area. A face that widens
        /// enough to overtake another in the ranking MUST CHANGE UID:
        /// `SignatureMatches` on the old uid fails, we go to the
        /// search, the true face is found again — and it was discarded precisely
        /// because it had grown. **The growth of the area was at once the cause
        /// of this search and the reason for its failure.**
        ///
        /// Measured: L-shaped bracket, horizontal tab 100x20 = 2000 mm² (F2) and
        /// vertical 80x20 = 1600 mm² (F3). Plan of two steps: `set_dimension`
        /// takes the vertical from 80 to 120, then `cylindrical_cut` on «F3». After
        /// the first step the vertical is worth 2400 and becomes F2; the search
        /// finds the right face again — same plane, same point, same
        /// normal, **same feature** — and the area check
        /// threw it away. A refusal of legitimate work.
        ///
        /// THE REPAIR is not removing the check: it is ordering it. When the
        /// first proof has ESTABLISHED that the feature is the same — not
        /// «I don't know», really established — the identity is already settled by a
        /// stronger signal, and the growth of the area becomes something to
        /// SAY, not to refuse. The monotonic constraint stays whole where it is
        /// really needed: when the feature is not known, which is case E2.
        ///
        /// In E2 the two faces came from different features (the extrusion and
        /// the cut), so the first proof separates them and the defect stays
        /// closed: it is checked in `Bench/cases/E2-the-two-cases.md`.
        ///
        /// WHY HERE AND NOT INSIDE `SignatureMatches`. Because that comparison
        /// also serves to decide whether the starting face is still fine, and
        /// there a growing area is normal — the part widens and the top face
        /// becomes larger, without the point moving. The
        /// corroboration holds **only when a substitute is already being
        /// searched for**, that is, when something has already moved.
        ///
        /// Returns null if the candidate holds up; the reason for refusal if not —
        /// and the reason gives **the criterion**, not the verdict.
        /// </summary>
        /// <summary>
        /// The face that SOLIDWORKS finds again from the persistent reference, if it
        /// finds it AND if it stands up to judgment. Null in every other case — and null
        /// here means «try another road», not «refuse».
        ///
        /// THREE DOORS, AND ALL THREE MUST OPEN.
        ///
        ///  1. **The plan must carry a persist ID.** The signatures from before
        ///     0.26.0 do not have one, and a plan written yesterday must keep
        ///     working: without it, we go back to the usual search.
        ///  2. **SOLIDWORKS must find something.** An `ErrorCode` other than
        ///     zero, or an object that is not a face, means that
        ///     that face no longer exists: it is the right answer to the case
        ///     of the suppressed feature, and it must be let through to the search
        ///     that will be able to say it better.
        ///  3. **The judgment must hold up.** The candidate goes through `Corroborates`
        ///     like any other: same feature, and the area growing
        ///     only if the feature is established. A persist ID that returns a
        ///     face of another feature is exactly the silent defect
        ///     this entry feared, and here it stops.
        ///
        /// <param name="move">
        /// By how much it moved, in words, when it moved. It goes to the reader:
        /// the face is the right one and the part is NO LONGER what it was,
        /// and keeping quiet would be working well on a changed part without saying so.
        /// </param>
        /// </summary>
        private IFace2 FaceFromPersistId(
            DocumentState docState, Dictionary<string, object> expectation, string uid,
            out string move)
        {
            move = null;
            if (docState == null || docState.Model == null || expectation == null) return null;

            object raw;
            if (!expectation.TryGetValue("persist_id", out raw) || raw == null) return null;
            var base64 = Convert.ToString(raw);
            if (string.IsNullOrEmpty(base64)) return null;

            byte[] blob;
            try { blob = Convert.FromBase64String(base64); }
            catch { return null; }
            if (blob.Length == 0) return null;

            IFace2 candidate;
            try
            {
                var ext = docState.Model.Extension;
                if (ext == null) return null;
                int error;
                var obj = ext.GetObjectByPersistReference3(blob, out error);
                if (error != 0 || obj == null) return null;
                candidate = obj as IFace2;
                if (candidate == null) return null;
            }
            catch { return null; }

            // THE JUDGMENT. The signature is remeasured NOW on the face found again:
            // the one the plan carries is not used, since that is what is being
            // verified.
            var signatureNow = DocumentState.FaceSignature(candidate);
            if (signatureNow == null) return null;
            // `FaceSignature` measures ONE face and does not reread the document:
            // the part's box is not there, and without it the sentence cannot
            // be written. Whoever has read the document passes it in.
            signatureNow.BoxPartMm = docState?.BoxPartMm;

            string note;
            if (Corroborates(expectation, uid, signatureNow, out note) != null) return null;

            move = MoveSentence(expectation, uid, signatureNow, note);
            return candidate;
        }

        /// <summary>
        /// What changed between the signature the plan carried and the one
        /// measured now, in one sentence. Null if nothing changed that
        /// is worth saying.
        ///
        /// THE CRITERION IS SAID, NOT THE VERDICT: how far it moved and by
        /// how much the area changed, so the reader can decide whether the plan
        /// relied on those numbers.
        /// </summary>
        private static string MoveSentence(
            Dictionary<string, object> expectation, string uid,
            DocumentState.EntitySignature now, string corroboratingNote)
        {
            var parts = new List<string>();

            var pointBefore = Triplet(expectation, "point_mm");
            if (pointBefore != null && now.PointMm != null && pointBefore.Length >= 3 &&
                now.PointMm.Length >= 3)
            {
                double dx = now.PointMm[0] - pointBefore[0];
                double dy = now.PointMm[1] - pointBefore[1];
                double dz = now.PointMm[2] - pointBefore[2];
                double howMuch = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (howMuch > 0.05)
                    parts.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "moved by {0:0.##} mm, from [{1:0.##}, {2:0.##}, {3:0.##}] to " +
                        "[{4:0.##}, {5:0.##}, {6:0.##}]",
                        howMuch, pointBefore[0], pointBefore[1], pointBefore[2],
                        now.PointMm[0], now.PointMm[1], now.PointMm[2]));
            }

            object rawArea;
            if (expectation.TryGetValue("area_mm2", out rawArea) && rawArea != null)
            {
                double beforeArea;
                try
                {
                    beforeArea = Convert.ToDouble(
                        rawArea, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { beforeArea = 0; }
                if (beforeArea > 0 && Math.Abs(now.Measure - beforeArea) > beforeArea * 0.001)
                    parts.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "the area went from {0:0.##} to {1:0.##} mm²", beforeArea, now.Measure));
            }

            if (parts.Count == 0) return null;

            // THE EXPLANATION, which here is the case for which entry E10 existed:
            // the face moved by sixty millimeters AND sits where it sat
            // relative to the part. Saying so helps to understand; deciding it does not — and indeed
            // here nothing is decided: the face was already chosen by the
            // persistent reference, and the judgment was already given by Corroborates.
            return "SOLIDWORKS found the face «" + uid + "» again from its persistent " +
                   "reference: " + string.Join(", ", parts) +
                   ". The part has changed since you looked at it, but the face is " +
                   "the right one — if the plan relied on the earlier measures, " +
                   "check the result again." +
                   Sentences.WhereItWasAndWhereItIs(
                       uid, pointBefore, now.PointMm,
                       Sextuple(expectation, "box_part_mm"), now.BoxPartMm) +
                   (corroboratingNote == null ? "" : " " + corroboratingNote);
        }

        private static string Corroborates(
            Dictionary<string, object> expectation, string uid,
            DocumentState.EntitySignature candidate, out string note)
        {
            note = null;
            if (expectation == null) return null;

            // 1. THE SAME FEATURE.
            // Only if we know both: two «-1» are not a
            // match, they are two times «I don't know».
            bool sameFeatureConfirmed = false;
            object rawId;
            if (expectation.TryGetValue("feature_id", out rawId) && rawId != null &&
                candidate.FeatureId >= 0)
            {
                int expected;
                try { expected = Convert.ToInt32(rawId); } catch { expected = -1; }
                if (expected >= 0 && expected == candidate.FeatureId)
                    sameFeatureConfirmed = true;
                if (expected >= 0 && expected != candidate.FeatureId)
                {
                    object expectedName;
                    string fromWhom = expectation.TryGetValue("feature", out expectedName) &&
                                   expectedName != null
                        ? "«" + Convert.ToString(expectedName) + "»"
                        : "another feature";
                    return
                        "The face «" + uid + "» has moved, and the only one that matches " +
                        "by position comes from «" + (candidate.Feature ?? "?") +
                        "» instead of from " + fromWhom + ": it is not the same face, it is " +
                        "another one that ended up in the same place. Read the state of the " +
                        "document again and tell me which one.";
                }
            }

            // 2. THE AREA CAN ONLY DROP.
            object rawArea;
            if (expectation.TryGetValue("area_mm2", out rawArea) && rawArea != null)
            {
                double expectedArea;
                try { expectedArea = Convert.ToDouble(rawArea, System.Globalization.CultureInfo.InvariantCulture); }
                catch { return null; }
                // One per mille of margin: areas are rounded to the
                // hundredth and a strict comparison would accuse the rounding.
                if (expectedArea > 0 && candidate.Measure > expectedArea * 1.001)
                {
                    // THE ESTABLISHED FEATURE WINS OVER THE AREA.
                    // If it is known for certain that it comes from the same feature,
                    // the identity is already settled: a face may have
                    // grown because a dimension changed, and that is legitimate.
                    // We SAY by how much, and go on.
                    if (sameFeatureConfirmed)
                    {
                        note = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "The face «{0}» had moved and it also GREW: {1:0.##} " +
                            "mm² against the {2:0.##} that the plan expected. I took " +
                            "it all the same because it comes from the same feature " +
                            "«{3}», but if the plan relied on the earlier measure, " +
                            "check the result again.",
                            uid, candidate.Measure, expectedArea, candidate.Feature ?? "?");
                        return null;
                    }

                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "The face «{0}» has moved, and the only one that matches by " +
                        "position is BIGGER than the one I was looking for: {1:0.##} " +
                        "mm² against {2:0.##}. A feature removes area from a face, it does " +
                        "not add any: that is not the same face. Read the state of the " +
                        "document again and tell me which one.",
                        uid, candidate.Measure, expectedArea);
                }
            }

            return null;
        }

        /// <summary>
        /// The edge the plan calls «uid»: the state's if the signature
        /// matches, otherwise FOUND AGAIN BY SIGNATURE — and only if it is one (entry E19).
        ///
        /// The uids of the edges are positional, in order of length: a
        /// hole adds two circumferences and «E9» becomes another edge. For
        /// faces the search has existed since 10/9/2026 (above, with the persist ID
        /// of entry E6); for edges up to library 0.119.0 it
        /// just refused, and a plan of two steps — drill, then fillet the
        /// edge you saw — stopped at the second.
        ///
        /// THE SAME LENGTH AT THE SAME POINT, AND NOTHING ELSE. The point is the
        /// midpoint of the ends: a shortened edge (a chamfer on its
        /// top) has another point and another length, and is NOT the same
        /// edge even if it sits in the same corner. The nearest one is never
        /// taken: it would be success on the wrong part, case B of E2.
        /// Two equal candidates — two bodies touching along an edge —
        /// are a real ambiguity, and it is said.
        ///
        /// Without a signature in the plan everything stays as it was: the uid is used, or it
        /// is refused with `vanished` if the state does not have it.
        /// </summary>
        private DocumentState.EntitySignature EdgeSignature(
            Dictionary<string, object> a, DocumentState docState, string uid,
            string disappeared, out string reason)
        {
            reason = null;
            DocumentState.EntitySignature signature = null;
            bool present = docState != null && docState.TryGetSignature(uid, out signature) &&
                            signature.Type == "EDGE";
            if (present && SignatureMatches(a, uid, signature, out reason)) return signature;
            string refusal = present ? reason : disappeared;
            reason = refusal;

            var expectation = ExpectedSignature(a, uid);
            var point = Triplet(expectation, "point_mm");
            double length = Num(expectation, "length_mm");
            if (docState == null || point == null || length <= 0) return null;

            string found = null;
            DocumentState.EntitySignature foundAgain = null;
            int howMany = 0;
            foreach (var entry in docState.Signatures)
            {
                var candidate = entry.Value;
                if (candidate.Type != "EDGE" || candidate.Entity == null) continue;
                if (Math.Abs(candidate.Measure - length) > LengthToleranceMm) continue;
                string why;
                if (!SignatureMatches(a, uid, candidate, out why)) continue;
                howMany++;
                found = entry.Key;
                foundAgain = candidate;
            }

            if (howMany == 1)
            {
                // IT IS SAID, as for faces: the plan named «E9» and the
                // fillet ended up on «E11». Whoever reads the answer must be able to
                // check that it is the same edge, and these are the numbers
                // to do it.
                ReAnchored(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "the edge «{0}» is now called «{1}»: same length ({2:0.##} mm), " +
                    "same point [{3:0.##}, {4:0.##}, {5:0.##}]. Edges are renumbered by " +
                    "length when the part changes: I worked on the one you had seen.",
                    uid, found, length, point[0], point[1], point[2]));
                reason = null;
                return foundAgain;
            }
            if (howMany > 1)
            {
                reason = "The edge «" + uid + "» changed number, and there are " + howMany +
                         " with its length at its point: I do not choose. Read the state of the " +
                         "document again and name it again.";
                return null;
            }
            reason = refusal + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                " And among the edges as they are now none is {0:0.##} mm long at [{1:0.##}, " +
                "{2:0.##}, {3:0.##}] like the one you had seen: I do not choose the closest one.",
                length, point[0], point[1], point[2]);
            return null;
        }

        /// <summary>
        /// Does the signature carried by the plan match the one read now?
        /// True also when the plan carries no signature: in that case there is
        /// nothing to compare and a refusal cannot be invented.
        /// </summary>
        private static bool SignatureMatches(
            Dictionary<string, object> arguments,
            string uid,
            DocumentState.EntitySignature now,
            out string reason)
        {
            reason = null;
            var expectation = ExpectedSignature(arguments, uid);
            if (expectation == null) return true;

            // THE AXIS IS A LINE, not a point (entry E4). The point the state
            // publishes is the start of the axis, and the start moves if the hole it
            // is born from gets longer: the same line with another start is the
            // same axis. So the direction must be parallel, and the expected
            // point must lie on the current line.
            if (now.Type == "AXIS")
            {
                var q = Triplet(expectation, "point_mm");
                var e = Triplet(expectation, "direction");
                var p = now.PointMm; var d = now.Normal;
                if (q == null || p == null || d == null || p.Length != 3 || d.Length != 3) return true;
                if (e != null)
                {
                    double dot = Math.Abs(d[0] * e[0] + d[1] * e[1] + d[2] * e[2]);
                    if (dot < Math.Cos(Math.PI / 180.0))
                    {
                        reason = "The axis «" + uid + "» is now oriented in another way. The part " +
                                 "has changed since you confirmed: " + IDoNotWorkOn(now);
                        return false;
                    }
                }
                double vx = q[0] - p[0], vy = q[1] - p[1], vz = q[2] - p[2];
                double cx = vy * d[2] - vz * d[1], cy = vz * d[0] - vx * d[2], cz = vx * d[1] - vy * d[0];
                double dist = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                if (dist > PointToleranceMm)
                {
                    reason = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "The axis «{0}» has moved by {1:0.##} mm. The part has changed since you confirmed: " +
                        "{2}", uid, dist, IDoNotWorkOn(now));
                    return false;
                }
                return true;
            }

            // TWO NAMES FOR THE SAME THING, and it is not laziness.
            // A face publishes `point_mm` — a point ON it; a body
            // publishes `center_mm` — the center of its box. They are two different
            // measures with the same job: saying WHERE the entity is.
            // Calling them the same in the state would have been worse (the
            // center of a body is not on the body, if the body is horseshoe-shaped),
            // so here both are accepted.
            // AN EDGE MUST ALSO BE AS LONG AS IT WAS (entry E19). The
            // midpoint alone is not enough: an edge shortened at BOTH ends
            // — two equal chamfers — has the same midpoint and another
            // length. It is compared only with two lengths read.
            if (now.Type == "EDGE")
            {
                double expectedLength = Num(expectation, "length_mm");
                if (expectedLength > 0 && now.Measure > 0 &&
                    Math.Abs(now.Measure - expectedLength) > LengthToleranceMm)
                {
                    reason = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "«{0}» is now {1:0.##} mm long instead of {2:0.##}. The part " +
                        "has changed since you confirmed: {3}",
                        uid, now.Measure, expectedLength, IDoNotWorkOn(now));
                    return false;
                }
            }

            var expectedPoint = Triplet(expectation, "point_mm") ?? Triplet(expectation, "center_mm");
            if (expectedPoint != null && now.PointMm != null && now.PointMm.Length == 3)
            {
                double deviation = 0;
                for (int i = 0; i < 3; i++)
                    deviation = Math.Max(deviation, Math.Abs(expectedPoint[i] - now.PointMm[i]));
                if (deviation > PointToleranceMm)
                {
                    // THE RIGHT WORD FOR THE RIGHT THING. The same comparison
                    // serves faces, edges and bodies, and saying «I am not working on the
                    // wrong face» of a body sends the reader looking for a face
                    // that has nothing to do with it.
                    // THE EXPLANATION IS ATTACHED AFTER THE VERDICT, and the verdict
                    // is already taken: above, on the millimeters. What follows is
                    // a sentence — not a number, not a `bool` — and it cannot
                    // change the outcome even if someone wanted it to. See
                    // Sentences.WhereItWasAndWhereItIs (entry E10).
                    reason =
                        $"«{uid}» now points at another part of the piece (moved by " +
                        $"{deviation:0.##} mm). The part " +
                        $"has changed since you confirmed: {IDoNotWorkOn(now)}" +
                        Sentences.WhereItWasAndWhereItIs(
                            uid, expectedPoint, now.PointMm,
                            Sextuple(expectation, "box_part_mm"), now.BoxPartMm);
                    return false;
                }
            }

            var expectedNormal = Triplet(expectation, "normal");
            if (expectedNormal != null && now.Normal != null && now.Normal.Length == 3)
            {
                double deviation = 0;
                for (int i = 0; i < 3; i++)
                    deviation = Math.Max(deviation, Math.Abs(expectedNormal[i] - now.Normal[i]));
                if (deviation > NormalTolerance)
                {
                    reason =
                        $"«{uid}» is now oriented in another way. The part " +
                        "has changed since you confirmed.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The closing sentence of the refusal, with the right word for the type
        /// of entity. Three words instead of one, and it is the difference between a
        /// message that makes sense and one that sends people looking for the wrong
        /// thing.
        /// </summary>
        private static string IDoNotWorkOn(DocumentState.EntitySignature entity)
        {
            switch (entity?.Type)
            {
                case "BODY": return "I do not work on the wrong body.";
                case "AXIS": return "I do not work on the wrong axis.";
                case "EDGE": return "I do not work on the wrong edge.";
                default: return "I do not work on the wrong face.";
            }
        }

        private static Dictionary<string, object> ExpectedSignature(
            Dictionary<string, object> arguments, string uid)
        {
            if (arguments == null) return null;
            if (!arguments.TryGetValue("_signatures", out var all)) return null;
            var map = all as Dictionary<string, object>;
            if (map == null) return null;
            foreach (var entry in map)
            {
                if (string.Equals(entry.Key, uid, StringComparison.OrdinalIgnoreCase))
                    return entry.Value as Dictionary<string, object>;
            }
            return null;
        }

        /// <summary>Three numbers from a JSON field, or null if they are not there.</summary>
        private static double[] Triplet(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.TryGetValue(key, out var v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try { outside.Add(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            return outside.Count == 3 ? outside.ToArray() : null;
        }

        /// <summary>
        /// The six numbers of the part's box from a JSON field
        /// (`{minX,minY,minZ,maxX,maxY,maxZ}`), or null if they are not there.
        ///
        /// It serves only to write a sentence: see
        /// <see cref="Sentences.WhereItWasAndWhereItIs"/>, where it also says why
        /// these numbers do not enter into any decision.
        /// </summary>
        private static double[] Sextuple(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.TryGetValue(key, out var v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try { outside.Add(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            return outside.Count == 6 ? outside.ToArray() : null;
        }

        /// <summary>
        /// Which model axes correspond to X and Y of the sketch on this
        /// face. Returns null if the face is not planar and perpendicular to a
        /// main axis — a case in which we refuse instead of guessing.
        /// </summary>
        private double[] FaceAxes(IFace2 face, out double offsetM, out string error)
        {
            error = null;
            offsetM = 0;
            try
            {
                var surface = face.IGetSurface();
                if (surface == null || !surface.IsPlane())
                {
                    error = "The face is not planar: this version does not work on it.";
                    return null;
                }
                var plane = surface.PlaneParams as double[];
                if (plane == null || plane.Length < 6)
                {
                    // Six numbers are needed: the normal and a point on the plane. With
                    // only the three of the normal we do not know WHERE the plane is, and
                    // the third coordinate of the point would end up at zero — that is
                    // on the plane through the origin, not on this face.
                    error = "I cannot read the position of the face.";
                    return null;
                }
                double ax = Math.Abs(plane[0]), ay = Math.Abs(plane[1]), az = Math.Abs(plane[2]);
                const double almostOne = 0.999;
                // The last index is the axis of the NORMAL: from there we take
                // the plane's offset, that is the coordinate the model does not
                // have to say because the face itself fixes it.
                if (az > almostOne) { offsetM = plane[5]; return new double[] { 0, 1, 2 }; } // normal Z
                if (ay > almostOne) { offsetM = plane[4]; return new double[] { 0, 2, 1 }; } // normal Y
                if (ax > almostOne) { offsetM = plane[3]; return new double[] { 1, 2, 0 }; } // normal X
                error =
                    "The face is tilted with respect to the main axes: in this version " +
                    "I do not make features on it, because the coordinates would " +
                    "end up in the wrong place without any error.";
                return null;
            }
            catch (Exception ex)
            {
                error = "I cannot read the face: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// From (x, y) of the MODEL to sketch coordinates, in meters.
        ///
        /// WHY A REAL CONVERSION IS NEEDED
        /// Up to 0.24.0 this method received the axis mapping and
        /// IGNORED it: it returned (x, y, 0) as if the model's coordinates
        /// were already sketch coordinates. They are not. When SolidWorks
        /// opens a sketch on a face it chooses by itself the origin and direction of the
        /// axes based on the normal: for two opposite faces the Y axis comes out
        /// inverted, and a hole asked for at (30, 20) came out at (30, -20) — 40 mm
        /// further away, with the feature green and no warning.
        ///
        /// We do not guess the conversion: we ask SolidWorks for it.
        /// ISketch.ModelToSketchTransform IS the transformation it itself
        /// used to build the sketch. (It exists in 2021: verified
        /// by reflection on interop 29.5.1.1, together with
        /// IMathUtility.CreatePoint and IMathPoint.MultiplyTransform.)
        ///
        /// It must be called with the sketch ALREADY OPEN: before InsertSketch(true)
        /// there is no active sketch to read the transformation from.
        /// </summary>
        private double[] PointInSketch(
            IModelDoc2 model,
            double[] axes,
            double offsetM,
            double xMm,
            double yMm,
            out string error)
        {
            error = null;

            // 1. The point in the MODEL's system, in meters. The two coordinates
            //    that the language model gave go on the two free axes;
            //    the third is the face's plane, which it must not guess.
            var inModel = new double[3];
            inModel[(int)axes[0]] = Units.M(xMm);
            inModel[(int)axes[1]] = Units.M(yMm);
            inModel[(int)axes[2]] = offsetM;

            try
            {
                var sketch = model.SketchManager?.ActiveSketch;
                if (sketch == null)
                {
                    error = "No sketch open: I cannot position the feature.";
                    return null;
                }
                var transformation = sketch.ModelToSketchTransform;
                if (transformation == null)
                {
                    error = "SolidWorks does not give me the orientation of the sketch.";
                    return null;
                }
                var math = _sw.GetMathUtility() as IMathUtility;
                var point = math?.CreatePoint(inModel) as IMathPoint;
                var inSketch = point?.MultiplyTransform(transformation) as IMathPoint;
                var outside = inSketch?.ArrayData as double[];
                if (outside == null || outside.Length < 3)
                {
                    error = "I could not convert the coordinates into the sketch.";
                    return null;
                }
                return new[] { outside[0], outside[1], outside[2] };
            }
            catch (Exception ex)
            {
                error = "The conversion of the coordinates did not succeed: " + ex.Message;
                return null;
            }
        }

        private Outcome Succeeded(
            IFeature feature,
            Dictionary<string, object> measurements,
            double[] direction = null)
        {
            string itemName = "";
            try { itemName = feature.Name; } catch { }
            _log($"[agent] created feature {itemName}");

            // WHAT MOVED REACHES THE READER (entry E8).
            //
            // Here is the only point that EVERY success of all
            // thirty-six operations goes through, including those in the `partial` files
            // (Holes, Weldment, Drawing, Material). Draining here means that
            // no operation can forget it.
            //
            // THE DEFECT THIS CLOSES, and it is the most mocking of all:
            // 0.26.0 built the finding again by persist ID and with it
            // the sentence that says BY HOW MUCH the face moved — «it has
            // moved by 60 mm, from [50, 0, 0] to [110, 0, 0]». Then it
            // handed it to `_log`, which in this bridge ends up on
            // `Console.Error`. And in an MCP server on stdio **stderr is not the
            // answer**: the model that wrote the plan and the person who
            // confirmed it never see it.
            //
            // Measured result: `e2_case_a` runs the hole on a part 60% longer
            // than the one confirmed, on the right face, and in the
            // answer there is `succeeded: true` and nothing else. The mechanism
            // written on purpose to have no silent defects had one
            // on itself: **it had measured the displacement and thrown the
            // measure away**.
            //
            // The right channel already existed and was empty: `Outcome.Measurements`
            // goes straight into the answer's JSON, as `note_volume` does.
            if (_reanchors.Count > 0)
            {
                if (measurements == null) measurements = new Dictionary<string, object>();
                measurements["reanchors"] = _reanchors.ToArray();
            }

            return new Outcome
            {
                Succeeded = true,
                CreatedFeature = itemName,
                Feature = feature,
                Direction = direction,
                Measurements = measurements,
            };
        }

        /// <summary>
        /// The faces that were found elsewhere than where the plan
        /// expected them, with how far they moved written down.
        ///
        /// It is a FIELD and not a parameter: no method signature changes,
        /// so whoever calls by reflection does not notice. It is the rule
        /// that 0.25.1 learned at a high price.
        ///
        /// It is emptied at the start of every operation, not at the end: a list
        /// that is emptied at the end stays full if the operation fails, and the
        /// next one would inherit the displacements of the one before.
        /// </summary>
        private readonly List<string> _reanchors = new List<string>();

        /// <summary>
        /// Records that a face was found elsewhere, and says so also
        /// on stderr for whoever watches the bridge work.
        /// </summary>
        private void ReAnchored(string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return;
            _log("[agent] " + sentence);
            _reanchors.Add(sentence);
        }

        // ─────────────────────────────────────────────────────────────────
        // References to previous steps
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The step cited by a reference, or null with the reason.
        ///
        /// The step number was already checked by the server while it
        /// wrote the plan; here it is checked again all the same, because between that
        /// check and this line there is the whole network round trip, the user's
        /// confirmation, and the previous steps that may have failed.
        /// </summary>
        /// <summary>
        /// Is there really a reference in that field?
        ///
        /// IT LOOKS AT THE VALUE, NOT THE KEY, and the difference cost a
        /// defect. A model that sees two alternative fields often emits
        /// both, filling the one it does not use with `null`. With
        /// `ContainsKey` a `face_ref: null` next to a perfectly valid
        /// `face_uid` made it take the reference branch,
        /// threw away the good identifier, and the step died. The server
        /// now removes those keys from the plan; this check is the
        /// second lock, for plans already saved and for the day
        /// someone changes the server.
        /// </summary>
        private static bool HasReference(Dictionary<string, object> a, string theField)
        {
            object raw;
            return a != null && a.TryGetValue(theField, out raw) && raw != null;
        }

        private References.Step CitedStep(
            Dictionary<string, object> a, string theField, out int number, out string reason)
        {
            number = 0;
            // Never null: a null reason becomes `Failed(null)`, and the reader
            // finds «I stopped after 1 operations: » with nothing after the
            // colon. A failure without a written reason is a failure
            // that cannot be corrected.
            reason = $"The reference «{theField}» could not be resolved.";
            object raw;
            if (a == null || !a.TryGetValue(theField, out raw) || raw == null)
            {
                reason =
                    $"The plan says to use «{theField}», but that reference is " +
                    "empty. The step cannot be executed.";
                return null;
            }

            var map = raw as Dictionary<string, object>;
            if (map == null)
            {
                reason = $"The reference «{theField}» is not readable.";
                return null;
            }
            object val;
            if (!map.TryGetValue("step", out val))
            {
                reason = $"The reference «{theField}» does not say which step.";
                return null;
            }
            try { number = Convert.ToInt32(val); }
            catch
            {
                reason = $"The reference «{theField}» has an unreadable step number.";
                return null;
            }

            References.Step planStep;
            if (!_stepsDone.TryGetValue(number, out planStep) || planStep == null)
            {
                reason =
                    $"The reference «{theField}» cites step {number}, but of that " +
                    "step I have no geometry: either it has not been run yet, " +
                    "or it created nothing.";
                return null;
            }
            return planStep;
        }

        /// <summary>
        /// The normal of the face that was cut: it is the direction of the
        /// cut, and a later step needs it to know which face of the
        /// pocket is the bottom. Null if the face is not planar — in that case the
        /// next step will not be able to name its bottom, and will say so.
        /// </summary>
        private static double[] FaceNormal(IFace2 face)
        {
            try
            {
                return ApiHelpers.NormalIfPlanar(face);
            }
            catch { return null; }
        }

        private static string KindOf(Dictionary<string, object> a, string theField)
        {
            object raw;
            if (a == null || !a.TryGetValue(theField, out raw)) return "";
            var map = raw as Dictionary<string, object>;
            if (map == null) return "";
            object g;
            return map.TryGetValue("kind", out g) ? Convert.ToString(g) ?? "" : "";
        }

        private Outcome Failed(string reason)
        {
            // A failure without a written reason cannot be corrected.
            // It really happened: a path returned the reason as null, and whoever
            // read found «I stopped after 1 operations: » with
            // nothing after the colon. Better a generic sentence than nothing.
            if (string.IsNullOrWhiteSpace(reason))
                reason =
                    "The operation did not succeed, and I do not have a reason to give " +
                    "you: it is a defect of mine. Report it together with the log.";
            _log("[agent] " + reason);
            return new Outcome { Succeeded = false, Error = reason };
        }

        // ─── reading the parameters, without surprises ───────────────────
        private static double Num(Dictionary<string, object> a, string key, double se = 0)
        {
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return se;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return se; }
        }

        /// <summary>
        /// A number that MUST be there. Fails instead of returning zero.
        ///
        /// Num() above swallows every exception and returns the default
        /// value: handy for «centered» or «fillet», ruinous for a
        /// coordinate. A zero in place of an unreadable value gives no
        /// error: it moves the hole to the origin and that is all.
        /// </summary>
        private static bool RequiredNum(
            Dictionary<string, object> a, string key, out double val, out string error)
        {
            val = 0;
            error = null;
            object v = null;
            if (a == null || !a.TryGetValue(key, out v) || v == null)
            {
                error = $"The parameter «{key}» is missing.";
                return false;
            }
            try
            {
                val = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                error = $"«{key}» is not a number: {v}.";
                return false;
            }
            if (double.IsNaN(val) || double.IsInfinity(val))
            {
                error = $"«{key}» is not a usable number: {v}.";
                return false;
            }
            return true;
        }

        private static bool Bool(Dictionary<string, object> a, string key, bool se)
        {
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return se;
            try { return Convert.ToBoolean(v); }
            catch { return se; }
        }

        private static string Str(Dictionary<string, object> a, string key, string se)
        {
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return se;
            return Convert.ToString(v) ?? se;
        }

        /// <summary>Is there a non-empty list under this key?</summary>
        private static bool HasList(Dictionary<string, object> a, string key)
        {
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return false;
            if (v is string) return false;
            var e = v as System.Collections.IEnumerable;
            if (e == null) return false;
            foreach (var x in e) if (x != null) return true;
            return false;
        }

        /// <summary>
        /// The faces that a list of references to previous steps resolves.
        ///
        /// A LIST OF SINGLE REFERENCES, and not a plural kind. The
        /// vocabulary of <see cref="References"/> resolves one face at a
        /// time and refuses when the role is not unambiguous — which is the right
        /// rule and must not be touched. To get more than one the element is repeated:
        /// it is what whoever writes `faces_uid` with three uids inside already does.
        ///
        /// IF EVEN ONE DOES NOT RESOLVE, EVERYTHING FAILS. A shell open on two
        /// faces instead of three is not «almost right»: it is a different part,
        /// and it would have succeeded silently.
        /// </summary>
        private bool FacesFromReferences(
            Dictionary<string, object> a, string theField,
            out List<IFace2> faces, out string reason)
        {
            faces = new List<IFace2>();
            reason = null;

            object raw;
            if (a == null || !a.TryGetValue(theField, out raw) || raw == null)
            {
                reason = "«" + theField + "» is empty.";
                return false;
            }
            var list = raw as System.Collections.IEnumerable;
            if (list == null || raw is string)
            {
                reason = "«" + theField + "» must be a list of references to steps.";
                return false;
            }

            int which = 0;
            foreach (var v in list)
            {
                which++;
                var entry = v as Dictionary<string, object>;
                if (entry == null)
                {
                    reason = "Reference no. " + which + " of «" + theField +
                             "» is not a {step, kind} object.";
                    return false;
                }

                // `CitedStep` is reused by building a dictionary of a single
                // field: that way the checks on the step — that it exists, that it is
                // earlier, that it created something — stay the same, and
                // a second copy that would diverge is not written.
                var one = new Dictionary<string, object> { [theField] = entry };
                int number;
                var planStep = CitedStep(one, theField, out number, out reason);
                if (planStep == null)
                {
                    reason = "Reference no. " + which + ": " +
                             (reason ?? "not resolved.");
                    return false;
                }

                IFace2 face;
                if (!References.Face(planStep, KindOf(one, theField), out face, out reason))
                {
                    reason = "Reference no. " + which + ": " +
                             (reason ?? "face not found.");
                    return false;
                }
                faces.Add(face);
            }

            if (faces.Count == 0)
            {
                reason = "«" + theField + "» has no reference.";
                return false;
            }
            return true;
        }

        private static List<string> StringList(Dictionary<string, object> a, string key)
        {
            var outside = new List<string>();
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return outside;
            if (v is System.Collections.IEnumerable e && !(v is string))
            {
                foreach (var x in e) if (x != null) outside.Add(Convert.ToString(x));
            }
            return outside;
        }
    }
}
