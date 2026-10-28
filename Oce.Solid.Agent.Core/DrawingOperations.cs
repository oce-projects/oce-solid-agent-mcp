using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE DRAWING.
    ///
    /// Until 0.39.0 the catalogue could only make parts: twenty-nine
    /// operations, all on a `.SLDPRT`. The strings «Drawing», «IView» and
    /// «SaveAs» appeared in no file of the add-in. That is, the assistant
    /// could draw a watertight container and could not deliver it: what reaches
    /// the workshop is not a model, it is a dimensioned drawing.
    ///
    /// The census (RAG 80r) counted 278 methods on `IDrawingDoc` and 481 on
    /// `IView`. Here there are seven. The choice is deliberate: seven measured
    /// operations are worth more than seventy declared ones, and it is the rule of the
    /// two-level catalogue (RAG 80j).
    ///
    /// THE THREE TRAPS OF THIS FAMILY, all of the kind this
    /// project exists to catch — no error, wrong result:
    ///
    /// 1. THE PROJECTION ANGLE. `Create3rdAngleViews2` and `Create1stAngleViews2` are
    ///    twins, neither of the two fails, and they produce drawings with the right
    ///    and left views SWAPPED. Italy draws in first angle; a
    ///    drawing in third angle is readable, plausible, and the part that comes out of it is
    ///    mirrored. No geometric measurement reveals it. Here the third is not
    ///    exposed at all and the first angle is not a parameter: it is a
    ///    constant.
    ///
    /// 2. THE DIMENSION THAT LIES. `CreateLinearDim4` takes a parameter `Val`, and
    ///    `IDisplayDimension.SetText` overwrites the displayed number: the API
    ///    allows writing on the drawing a number that is NOT the geometry.
    ///    Here there is no operation that dimensions by hand: the dimensions are
    ///    taken from the model (`InsertModelAnnotations3`), where they cannot
    ///    lie.
    ///
    /// 3. THE PDF OF ONE SHEET ONLY. The default export takes the current
    ///    sheet: a three-sheet drawing goes out to the workshop as one page,
    ///    without errors. `IExportPdfData` has seven methods in all and two
    ///    serve only to avoid this. Here the sheets are counted first and the
    ///    PAGES of the PDF after.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The preference number of the default drawing template.
        /// A path is never written by hand: it is the lesson of the weld
        /// profiles. The workshop has its own template, with its own title block and its
        /// own projection angle, and a path written by hand works on the machine of
        /// whoever wrote it and on no other.
        /// </summary>
        private const int DefaultDrawingTemplate =
            (int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing;

        // ─────────────────────────────────────────────────────────────────
        // create_drawing
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the drawing of the open model, and leaves it active.
        ///
        /// TWO DOCUMENTS OPEN AT THE SAME TIME: it is the architectural novelty, not
        /// the API. `CreateDrawViewFromModelView3(ModelName, ...)` wants the
        /// path of an ALREADY OPEN document — so the model must
        /// stay open while the drawing becomes active. Until here
        /// the agent worked on a single document and the state took it
        /// for granted.
        /// </summary>
        /// <summary>
        /// The path (or, without one, the title) of another open document with
        /// the same title as <paramref name="document"/>, or null. Counted, not
        /// compared by object: a title that appears twice among the open
        /// documents is the case, whichever of the two the walk meets first.
        /// </summary>
        private string OtherDocumentWithTitle(IModelDoc2 document, string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            int same = 0;
            string other = null;
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                for (int guard = 0; d != null && guard < 200; guard++)
                {
                    string t = "", p = "";
                    try { t = d.GetTitle() ?? ""; } catch { }
                    if (string.Equals(t, title, StringComparison.Ordinal))
                    {
                        same++;
                        try { p = d.GetPathName() ?? ""; } catch { }
                        if (!ReferenceEquals(d, document) && (other == null || p.Length > 0)) other = p.Length > 0 ? p : t;
                    }
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { return null; }
            return same >= 2 ? other ?? title : null;
        }

        /// <summary>
        /// What a drawing with the title of another open document must be told
        /// (report 8cd843 N27). Pure, so the contract suite reads it.
        /// </summary>
        internal static string SameTitleWarning(string title, string other)
        {
            return "The new drawing is called «" + title + "», like «" + other + "» already open: two windows with one " +
                   "title, and «document activate» cannot bring the new one in front until it has a name of its own. " +
                   "Save it in this plan with «save_document» and a path, before another window comes in front.";
        }

        /// <summary>
        /// Writes the title of the drawing into <paramref name="measurements"/>, and the
        /// warning of N27 when another open document has it too. Called by
        /// «create_drawing» AND by every operation that puts a model view on the sheet:
        /// a drawing without a file takes the name of the first model that lands on it.
        /// In report 8cd843 N27 (call 009) «create_drawing» gave a default name and it
        /// was «standard_views» that renamed the drawing after the assembly, the title
        /// of the saved drawing already open; the replay t26n of 27/10/2026 gave a
        /// default name to «create_drawing» alone. A warning only in «create_drawing»
        /// never fires on the case it was written for.
        /// </summary>
        private void TellTitle(IModelDoc2 drawing, Dictionary<string, object> measurements)
        {
            string title = "";
            try { title = drawing.GetTitle() ?? ""; } catch { }
            measurements["document_title"] = title;
            string twin = OtherDocumentWithTitle(drawing, title);
            if (twin != null) measurements["warning_same_title"] = SameTitleWarning(title, twin);
        }

        private Outcome CreateDrawing(IModelDoc2 model, Dictionary<string, object> a)
        {
            if (model == null)
                return Failed("There is no open document to put in a drawing.");

            string modelPath = "";
            try { modelPath = model.GetPathName() ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(modelPath))
                return Failed(
                    "The part has never been saved, so it has no path, and a " +
                    "drawing view attaches to the PATH of the model. Save " +
                    "it once and then ask me for the drawing again.");

            int modelType = 0;
            try { modelType = model.GetType(); } catch { }
            if (modelType != (int)swDocumentTypes_e.swDocPART &&
                modelType != (int)swDocumentTypes_e.swDocASSEMBLY)
                return Failed(
                    "The active document is neither a part nor an assembly: I do not " +
                    "know what to make the drawing of.");

            string template = Str(a, "template", null);
            if (string.IsNullOrWhiteSpace(template))
            {
                try { template = _sw.GetUserPreferenceStringValue(DefaultDrawingTemplate); }
                catch { template = null; }
            }
            if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                return Failed(
                    "I cannot find the drawing template of this installation " +
                    (string.IsNullOrWhiteSpace(template)
                        ? "(the preference is empty)."
                        : "(«" + template + "» does not exist)") +
                    " Without it, the drawing would be created without a title block and " +
                    "with the wrong projection angle: I prefer to stop.");

            // Format and scale are passed already here: `NewDocument` accepts them, and
            // doing it in two steps would mean a wasted sheet.
            int sheetFormat = SheetFormatCode(Str(a, "paper_size", "A3"));
            object newItem;
            try { newItem = _sw.NewDocument(template, sheetFormat, 0, 0); }
            catch (Exception ex) { return Failed("I cannot create the drawing: " + ex.Message); }

            var drawing = newItem as IModelDoc2;
            if (drawing == null)
                return Failed(
                    "SolidWorks did not create any drawing and did not say why.");

            int newType = 0;
            try { newType = drawing.GetType(); } catch { }
            if (newType != (int)swDocumentTypes_e.swDocDRAWING)
                return Failed(
                    "The document created is not a drawing: the template «" + template +
                    "» is not a drawing template.");

            // THE MODEL MUST STILL BE OPEN.
            //
            // It is the precondition of everything that comes after, and it is not
            // a given: creating a document changes the active document, and
            // one preference («close unused documents») is enough for the
            // part to vanish from under our feet. If it vanishes, the views would be born
            // empty without any error.
            if (!OpenedDocument(modelPath))
                return Failed(
                    "The drawing was created but the part no longer appears to be open: the " +
                    "views would be empty. Open the part again and retry.");

            var measurements = new Dictionary<string, object>
            {
                ["template"] = template,
                ["paper_size"] = Str(a, "paper_size", "A3"),
                ["model_in_drawing"] = modelPath,
                ["sheets"] = NumberOrNull(CountSheets(drawing)),
                ["documents_open"] = OpenDocuments().Count,
            };

            // TWO WINDOWS, ONE TITLE (report 8cd843 N27, round 7): a second drawing
            // of a copy of the clip assembly, not saved, had the title of the saved
            // drawing of that copy, already open, and «document activate» refused
            // it, rightly: by title it cannot tell the two apart. The refusal came
            // three steps later; the drawing is told here, and again by the first
            // model view, which renames it (see TellTitle).
            TellTitle(drawing, measurements);
            _log("[agent] drawing created from " + template);
            var outcome = WithoutFeature(measurements);
            // FROM HERE ON WE WORK ON THE DRAWING, and it is stated.
            // Whoever executes the plan watches that the document does not change under
            // its feet; this is the only legitimate change, so it must be declared
            // instead of suffered.
            outcome.NewDocument = drawing;
            return outcome;
        }

        // ─────────────────────────────────────────────────────────────────
        // set_sheet
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Format and scale of the active sheet. The projection angle is NOT a parameter.
        /// </summary>
        private Outcome SetSheet(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("The active document is not a drawing.");

            ISheet sheet;
            try { sheet = drawing.GetCurrentSheet() as ISheet; }
            catch (Exception ex) { return Failed("I cannot read the sheet: " + ex.Message); }
            if (sheet == null) return Failed("The drawing has no active sheet.");

            string formatName = Str(a, "paper_size", "A3");
            int sheetFormat = SheetFormatCode(formatName);
            double scaleNum = Num(a, "scale_numerator", 1);
            double scaleDen = Num(a, "scale_denominator", 1);
            if (scaleNum <= 0 || scaleDen <= 0)
                return Failed("The scale must have two positive numbers, for example 1:2.");

            // FORMAT AND SCALE ARE WRITTEN TOGETHER, AND THE PROJECTION ANGLE IS READ BACK.
            //
            // The first attempt did `SetSize` and then `SetScale`, and the
            // second returned `false` — measured on 11 September 2026 on the
            // first real drawing. The reason is the same one already paid for with the
            // faces: changing the sheet invalidates the pointer to the sheet, and
            // the second call worked on an old object.
            //
            // `SetProperties2` does everything in one go, so the problem
            // does not arise. It does carry a parameter that must NOT be decided
            // here: `FirstAngle`. It is the projection angle, that is the most expensive trap
            // of this family (a drawing in third angle has right and
            // left swapped, gives no error, and the part comes out
            // mirrored). What the workshop's drawing template
            // has already decided is READ and written back unchanged.
            var before = SheetProperty(sheet);
            bool firstAngle = before != null && before.Length > 4 && before[4] != 0;

            try
            {
                sheet.SetProperties2(
                    sheetFormat,
                    (int)swDwgTemplates_e.swDwgTemplateNone,
                    scaleNum, scaleDen,
                    firstAngle,
                    0, 0,
                    true);
            }
            catch (Exception ex) { return Failed("Sheet not set: " + ex.Message); }

            // The pointer to the sheet is old from this line on.
            try { sheet = drawing.GetCurrentSheet() as ISheet; }
            catch { sheet = null; }
            if (sheet == null) return Failed("I lost the sheet after setting it.");

            // IT IS READ BACK. Both calls return `true` even when the
            // sheet stayed as it was — it happened with `SetMaterialPropertyName2`,
            // and the defense is always the same: ask the document, do not
            // trust the call.
            double[] sheetMeasurements = SheetMeasurements(sheet);
            var scaleAfter = ReadScale(sheet);
            if (scaleAfter != null && scaleDen != 0 &&
                Math.Abs(scaleAfter[0] / scaleAfter[1] - scaleNum / scaleDen) > 1e-9)
                return Failed(
                    "I asked for the scale " + scaleNum + ":" + scaleDen +
                    " and the sheet says " + scaleAfter[0] + ":" + scaleAfter[1] +
                    ". I do not say it is done when it is not.");

            var measurements = new Dictionary<string, object>
            {
                ["paper_size"] = formatName,
                ["width_mm"] = sheetMeasurements != null ? (object)Math.Round(sheetMeasurements[0], 1) : null,
                ["height_mm"] = sheetMeasurements != null ? (object)Math.Round(sheetMeasurements[1], 1) : null,
                ["scale"] = scaleAfter != null ? scaleAfter[0] + ":" + scaleAfter[1] : null,
                ["projection_angle"] = firstAngle ? "first" : "third",
            };
            // THE PROJECTION ANGLE IS ALWAYS STATED, even when it is right. It is not a
            // warning: it is the only way for someone to notice before
            // the part is cut.
            if (!firstAngle)
                _log("[agent] WARNING: this drawing is in THIRD angle projection");
            return WithoutFeature(measurements);
        }

        // ─────────────────────────────────────────────────────────────────
        // standard_views
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The three standard views, in FIRST angle.
        ///
        /// `Create1stAngleViews2` returns `bool`, not the views: to know what it
        /// did one has to count them before and after. It is the same shape already
        /// seen with `InsertFeatureShell` — a call that says «yes» and does not
        /// say what.
        /// </summary>
        private Outcome StandardViews(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null) return Failed("The active document is not a drawing.");

            string modelPath = Str(a, "model", null);
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                string why;
                modelPath = ModelToPutInView(out why);
                if (modelPath == null) return Failed(why);
            }
            var swPath = CanonicalPath(modelPath);
            if (swPath == null)
                return Failed(
                    "«" + modelPath + "» does not appear to be open. The " +
                    "views would be created empty without any error.");

            int before = CountViews(drawing);
            if (before < 0)
                return Failed("I cannot count the views of the drawing before starting. I touched nothing.");
            // The views that were already on the sheet: the centre marks the bridge
            // adds below go only on the views this call makes (Z-C21.13 review).
            // Null when one name cannot be read: then no view is known to be new,
            // and the bridge marks none.
            var viewsBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var sheetBefore = drawing.GetFirstView() as IView;   // the sheet itself
                var old = sheetBefore == null ? null : sheetBefore.GetNextView() as IView;
                while (old != null)
                {
                    string oldName = old.GetName2();
                    if (oldName == null) { viewsBefore = null; break; }
                    viewsBefore.Add(oldName);
                    old = old.GetNextView() as IView;
                }
            }
            catch { viewsBefore = null; }
            bool ok;
            try { ok = drawing.Create1stAngleViews2(swPath); }
            catch (Exception ex) { return Failed("Views not created: " + ex.Message); }

            int after = CountViews(drawing);
            if (after < 0)
                return Failed(
                    "I asked for the views, but then I cannot count them again: I cannot tell you whether " +
                    "they were created. Look at the drawing before going on.");
            if (!ok && after == before)
                return Failed(
                    "SolidWorks did not create any view and did not say why. " +
                    "Usually it means that the model is not rebuilt.");
            if (after <= before)
                return Failed(
                    "The call succeeded but the views are still " + before +
                    ": the drawing is empty. I do not call it successful.");

            // THE DRAWING IS REBUILT BEFORE ANSWERING (29/9/2026). The centre marks
            // SOLIDWORKS puts on holes when it creates a view (document option
            // swDetailingAutoInsertCenterMarksForHoles) were counted 3 in three full
            // bench runs and alone, and 0 once in the full run on 0.142.0
            // (center_marks_on_the_holes, step 7), with no change to this code or
            // to the counter. The state read right after the views were made may
            // come before the views are complete; a rebuild settles them, and the
            // answer now carries the count, so a second zero comes with its number.
            try { ((IModelDoc2)drawing).EditRebuild3(); } catch { }
            // Counted view by view. A view whose marks cannot be counted makes the
            // total unknown (null), not smaller: up to library 0.143.0 such a view
            // was skipped, and the total still looked like a count.
            var views = new List<IView>();
            try
            {
                var sheet = drawing.GetFirstView() as IView;          // the sheet itself
                var next = sheet == null ? null : sheet.GetNextView() as IView;
                while (next != null)
                {
                    views.Add(next);
                    next = next.GetNextView() as IView;
                }
            }
            catch { views = null; }
            // ZERO-E-ZERO: the sum starts at zero and every counted view adds its
            // own count; views that cannot be walked, or one view that cannot be
            // counted, turn it to -1, which the answer gives as null.
            int centreMarks = views == null ? -1 : 0;
            var viewsWithoutMarks = new List<IView>();
            if (views != null)
            {
                foreach (IView view in views)
                {
                    int n = CountCenterMarks.OfView(view);
                    if (n < 0) { centreMarks = -1; continue; }
                    if (centreMarks >= 0) centreMarks += n;
                    if (n == 0) viewsWithoutMarks.Add(view);
                }
            }

            // THE BRIDGE MARKS THE HOLES SOLIDWORKS LEFT BARE (Z-C21.13). A view
            // that shows holes end-on and has no centre mark at all after the
            // rebuild gets them from the walk the centre-mark operation uses
            // (MarkHolesOfView, CenterMarkOperations.cs), and the answer says how
            // many the bridge added. The online search of 29/9/2026 (RAG note
            // 82zz-decies) found no documented fix, only conditions that suppress
            // the automatic marks in silence: draft quality views ("Some edges may
            // be missing, since not all model information is loaded in Draft
            // quality"), large assembly mode. Only views with ZERO marks: where
            // SOLIDWORKS put some, the view is left as it is. Up to library 0.152.0
            // the walk called AddCenterMark, which adds nothing on a bare view
            // (measured on 8/10/2026, three holes out of three), so the remedy
            // never worked: the full bench of 0.152.0 went red on it. The walk now
            // inserts them with AutoInsertCenterMarks2 and finds each one again at
            // its hole (see MarkHolesOfView); a hole that still stays bare is said
            // in the answer. A view with no hole seen end-on (the front and side
            // views of a plate) is walked and left untouched.
            // ZERO-E-ZERO: the three counters below count what the walk found and
            // did; with no view without marks, or no hole in them, zero is real.
            int addedByBridge = 0;
            int holesInViewsWithoutMarks = 0;
            int holesLeftWithoutMark = 0;
            object autoMarksOption = null;
            if (viewsWithoutMarks.Count > 0)
            {
                IMathUtility mu = null;
                try { mu = _sw.GetMathUtility() as IMathUtility; } catch { }
                double size = DocumentCentreMarkSize(model, Units.M(DefaultCentreMarkSizeMm));
                foreach (IView view in viewsWithoutMarks)
                {
                    // ONLY THE VIEWS THIS CALL MADE (Z-C21.13 review): a view that was
                    // on the sheet before may be bare on purpose, and it is not ours
                    // to mark. A view whose name cannot be read is not taken for new.
                    string viewName = null;
                    try { viewName = view.GetName2(); } catch { }
                    if (viewName == null || viewsBefore == null || viewsBefore.Contains(viewName)) continue;
                    var marks = MarkHolesOfView(model, view, size, false, true, mu);
                    if (marks.Before < 0) continue;   // not counted this time: nothing was touched
                    holesInViewsWithoutMarks += marks.Holes;
                    addedByBridge += marks.Added;
                    holesLeftWithoutMark += marks.LeftWithoutMark.Count;
                }
                if (addedByBridge > 0) { try { model.EditRebuild3(); } catch { } }
                // The document option that makes SOLIDWORKS put the marks, read only
                // when it did not (DP_Detailing.htm of the swconst guide: the toggle
                // with swDetailingNoOptionSpecified, one for parts and one for
                // assemblies): a template with the option off explains a zero that
                // no suppression does.
                if (holesInViewsWithoutMarks > 0)
                {
                    bool assembly = swPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        autoMarksOption = model.Extension.GetUserPreferenceToggle(
                            (int)(assembly ? swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHolesAsm
                                           : swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHoles),
                            (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);
                    }
                    catch { }
                    // THE JOURNAL SAYS WHEN THE REMEDY FIRES (RAG note 82zz-duodecies, 1/10/2026:
                    // «the bench journal cannot say whether the remedy fires, because
                    // standard_views does not write it»). One line, only when SOLIDWORKS
                    // left holes bare, with what the bridge did about them.
                    _log("[agent] standard_views: SOLIDWORKS put no centre mark on " + holesInViewsWithoutMarks +
                         (holesInViewsWithoutMarks == 1 ? " hole" : " holes") + " seen end-on (document option " +
                         (autoMarksOption is bool o ? (o ? "on" : "off") : "unreadable") + "); the bridge added " +
                         addedByBridge + (holesLeftWithoutMark > 0 ? ", " + holesLeftWithoutMark + " left bare" : ""));
                }
            }

            var measurements = new Dictionary<string, object>
            {
                ["projection_angle"] = "first",
                ["model"] = swPath,
                ["views_before"] = before,
                ["views_after"] = after,
                ["views_created"] = after - before,
                ["names"] = ViewNames(drawing),
                // The marks SOLIDWORKS put, after the rebuild and before the bridge.
                ["centre_marks_after_rebuild"] = centreMarks >= 0 ? (object)centreMarks : null,
                ["centre_marks_added_by_bridge"] = addedByBridge,
            };
            if (holesInViewsWithoutMarks > 0)
            {
                measurements["holes_in_views_without_centre_marks"] = holesInViewsWithoutMarks;
                measurements["auto_centre_marks_option"] = autoMarksOption;
            }
            if (holesLeftWithoutMark > 0)
            {
                measurements["holes_left_without_centre_mark"] = holesLeftWithoutMark;
                string option = "the document option that inserts centre marks on holes when a view is created " +
                                "(Document Properties, Detailing)";
                measurements["centre_marks_note"] =
                    "SOLIDWORKS put no centre mark on the " + holesInViewsWithoutMarks +
                    (holesInViewsWithoutMarks == 1 ? " hole" : " holes") + " seen end-on in views without marks, and the bridge " +
                    "added " + addedByBridge + ": on the other " + holesLeftWithoutMark + " AutoInsertCenterMarks2 put no mark where the " +
                    "hole is. " +
                    (autoMarksOption is bool optionOn
                        ? (optionOn ? "The option that inserts them is on, so something suppressed them in silence (the documented " +
                                      "cases: a draft quality view, large assembly mode): make the views again."
                              : "Turn on " + option + " and make the views again.")
                        : "Check " + option + ", which could not be read, and make the views again.");
            }
            // The first model view renames a drawing without a file (N27).
            TellTitle(model, measurements);
            return WithoutFeature(measurements);
        }

        // ─────────────────────────────────────────────────────────────────
        // model_view (isometric and the other named views)
        // ─────────────────────────────────────────────────────────────────

        private Outcome ModelView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null) return Failed("The active document is not a drawing.");

            string modelPath = Str(a, "model", null);
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                string why;
                modelPath = ModelToPutInView(out why);
                if (modelPath == null) return Failed(why);
            }
            var swPath = CanonicalPath(modelPath);
            if (swPath == null)
                return Failed("«" + modelPath + "» does not appear to be open.");

            // THE NAMES OF THE VIEWS ARE SOLIDWORKS'S, NOT THE DESIGNER'S, AND
            // THE DESIGNER SAYS «isometric», NOT «*Isometric» (nor the name an
            // installation in another language gives it).
            string requested = (Str(a, "view", "isometric") ?? "").Trim().ToLowerInvariant();
            int standardView = StandardViewId(requested);
            if (standardView < 0)
                return Failed(
                    "I do not know the view «" + requested + "». I know nine: " +
                    "isometric, dimetric, trimetric, front, back, top, " +
                    "bottom, right, left.");

            double x = Units.M(Num(a, "x_mm", 0));
            double y = Units.M(Num(a, "y_mm", 0));

            // THE NAME OF THE VIEW IS NOT GUESSED, NOR TAKEN FROM A LIST OF
            // LANGUAGES: IT IS READ FROM THE MODEL, BY WHAT THE VIEW IS.
            //
            // A named view is asked for by NAME, and the name depends on the
            // language of the installation: «*Isometric» on an English one, another
            // word on an Italian one. The call does not fail with a message: it
            // returns `null` and nothing else, and from outside it looks as if
            // the drawing does not want that view.
            //
            // THE FIRST LEVEL, which needs no language: WHAT THE VIEW DOES.
            // Measured on 7/10/2026 (Checks/Language, Italian SOLIDWORKS, and the
            // same with the toggle «Use English feature and file names» on):
            // IModelDocExtension~GetNamedViewRotation answers nine ZEROS for
            // every named view, standard ones included, so it cannot tell them
            // apart. What can: showing the view. IModelDoc2~ShowNamedView2 with a
            // swStandardViews_e id (page ShowNamedView2: «ID of the view to display
            // as defined by swStandardViews_e») and the same call with the NAME of
            // a view of the document leave the model view in the same
            // IModelView~Orientation3 («Gets or sets the model view orientation
            // matrix») when the name is that standard view, in any language:
            // the ten names the part declares came out as front (twice: «*Normal
            // To» is the front when nothing is selected), back, left, right, top,
            // bottom, isometric, trimetric and dimetric. The orientation the
            // window had is put back from a copy of its data (measured: identical).
            //
            // WHEN THE DOCUMENT HAS NO WINDOW (no ActiveView) the system views come
            // in the order of the ids: «*Normal To», front, back, left, right, top,
            // bottom, isometric, trimetric, dimetric (as measured), and that order
            // is used, only if all ten are there.
            //
            // THE SECOND LEVEL, the table of the names (solidworks-names.json,
            // keys view.*): the names SOLIDWORKS writes for that view, tried when
            // the first level says nothing.
            var itsItems = ModelViewNames(swPath);
            var toTest = new List<string>();
            var byRotation = StandardViewNames(swPath, standardView);
            foreach (var n in byRotation)
                if (!toTest.Contains(n)) toTest.Add(n);
            var spelled = SolidWorksLanguage.Read(_sw).Spellings("view." + requested);
            foreach (var n in spelled)
            {
                // Among the names the model declares, the one that matches the
                // spelling of the table (ignoring capitals): its real spelling.
                var declared = itsItems.FirstOrDefault(i => string.Equals(i, n, StringComparison.OrdinalIgnoreCase));
                if (declared != null && !toTest.Contains(declared)) toTest.Add(declared);
            }
            // If the model declares nothing the known spellings remain.
            foreach (var known in spelled)
                if (!toTest.Contains(known)) toTest.Add(known);

            int before = CountViews(drawing);
            if (before < 0)
                return Failed("I cannot count the views of the drawing before starting. I touched nothing.");
            IView view = null;
            string swName = null;
            var refused = new List<string>();
            foreach (var candidate in toTest)
            {
                try { view = drawing.CreateDrawViewFromModelView3(swPath, candidate, x, y, 0); }
                catch { view = null; }
                if (view != null) { swName = candidate; break; }
                refused.Add(candidate);
            }

            int after = CountViews(drawing);
            // With an unreadable count the returned view stays (entry Z-C8).
            if (view == null || (after >= 0 && after <= before))
                return Failed(
                    "SolidWorks did not create the view «" + requested + "». I tried " +
                    "calling it: " + string.Join(", ", refused) + ". " +
                    (itsItems.Count > 0
                        ? "The part says it has these views: " + string.Join(", ", itsItems) + "."
                        : "The part does not declare any named view."));

            string itemName = "";
            double scale = 0;
            try { itemName = view.Name; } catch { }
            try { scale = view.ScaleDecimal; } catch { }

            var measurements = new Dictionary<string, object>
            {
                ["view"] = requested,
                ["sw_name"] = swName,
                // How the name was found: «model» (the first level, no language) or «table».
                ["found_by"] = byRotation.Contains(swName) ? "model" : "table",
                ["names_rejected"] = refused.Count > 0 ? (object)string.Join(",", refused) : null,
                ["name"] = itemName,
                ["scale"] = scale > 0 ? (object)Math.Round(scale, 4) : null,
                ["views_after"] = after,
            };
            // The first model view renames a drawing without a file (N27).
            TellTitle(model, measurements);
            return WithoutFeature(measurements);
        }

        // ─────────────────────────────────────────────────────────────────
        // model_dimensions
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Brings onto the drawing the dimensions the model already has.
        ///
        /// It is the only dimensioning route exposed, and the reason is safety,
        /// not convenience: a dimension inserted by hand can carry a number
        /// different from the geometry (`Val`, and `SetText` which overwrites the displayed
        /// text). A dimension that comes from the model cannot.
        ///
        /// WHY HERE THERE IS A TABLE OF ATTEMPTS AND NOT A SINGLE CALL.
        /// Measured on 11 September 2026 on the first real drawing: a part with
        /// five dimensions, three standard views, and `InsertModelAnnotations3` which
        /// brings nothing — without error, returning an empty vector, that is
        /// exactly what it returns when it worked and there was
        /// nothing to bring.
        ///
        /// The things that can make the difference are three, and the
        /// documentation treats them all as a detail:
        ///
        ///   - WHICH BITS. `swInsertDimensions` (8) is the generic bit; the dimensions
        ///     are however divided into MARKED for the drawing (32768) and NOT
        ///     marked (524288), and a feature made via the API is not
        ///     marked. Asking for them all together is not the same thing as
        ///     asking for them one at a time.
        ///   - WHERE TO PLACE THEM. `UsePlacementInSketch` takes the position
        ///     from the sketch of the model; if the sketch is not dimensioned there is
        ///     no position to take.
        ///   - WHICH VERSION. The 2 has a parameter that the 3 lost —
        ///     `AllTypes`, «bring them all, do not look at the mask» — and the
        ///     rule «the highest is the best» in this interop is already
        ///     proven false (`InsertEndCapFeature3` had REMOVED the faces
        ///     compared with the 2).
        ///
        /// So it is tried, in order from the most precise to the coarsest, and
        /// which route worked is written into the measurements. It is not elegance:
        /// it is the only way to turn «it did not work» into a fact.
        /// </summary>
        private Outcome ModelDimensions(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null) return Failed("The active document is not a drawing.");

            int views = CountViews(drawing);
            if (views < 0)
                return Failed("I cannot count the views of the drawing. I touched nothing.");
            if (views == 0)
                return Failed(
                    "The drawing has no views: the dimensions would have nowhere to attach. " +
                    "First put the views.");

            bool all = Bool(a, "all_dimensions", false);
            int extra = Bool(a, "hole_callouts", true)
                ? (int)swInsertAnnotation_e.swInsertholeCallout
                : 0;

            // By default only the dimensions MARKED for the drawing are taken: they are
            // the ones a designer chose to show, and a drawing with
            // all the dimensions of the model is unreadable.
            // The masks, and every fallback below, come from the ModelImport*
            // members after this method (Z-C21.3, findings M2 and N9): with
            // all_dimensions false no step may carry the NOT-MARKED bit or
            // ignore the mask. Oce.Solid.Agent.ContractTests (Zc212DrawingContract) reads the
            // method from the first line of this comment to its closing brace.
            var masks = ModelImportMasks(all);

            int before = CountDimensions(drawing);
            if (before < 0)
                return Failed(
                    "I cannot count the dimensions of the drawing before starting: without that " +
                    "number I could not tell you which ones arrived. I touched nothing.");
            int after = before;
            string howItIsDone = null;
            int returnedItems = 0;
            var tested = new List<string>();

            foreach (var mask in masks)
            {
                int types = mask | extra;

                foreach (var fromSketch in new[] { false, true })
                {
                    try
                    {
                        var returned = drawing.InsertModelAnnotations3(
                            (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                            types,
                            true,    // all the views
                            ModelImportV3DuplicateDims, // TRUE eliminates duplicates (M2, see the constant)
                            false,   // HiddenFeatureDims
                            fromSketch) as object[];
                        // ZERO-E-ZERO: it is only how many the API returned; the true measurement
                        // is `CountDimensions` before and after, and the exception has its own branch.
                        returnedItems = returned == null ? 0 : returned.Length;
                    }
                    catch (Exception ex) { return Failed("Dimensions not inserted: " + ex.Message); }

                    tested.Add("v3/" + mask + (fromSketch ? "/sketch" : ""));
                    after = CountDimensions(drawing);
                    if (after < 0) return Failed("I asked for the dimensions of the model, but then I cannot count the dimensions of the drawing again: I cannot tell you whether they arrived. Look at the drawing before going on.");
                    if (after > before)
                    {
                        howItIsDone = "InsertModelAnnotations3, bit " + mask +
                                    (fromSketch ? ", position from the sketch" : "");
                        break;
                    }
                }
                if (after > before) break;

                // Version 2, with `AllTypes`: the parameter the 3 does not have.
                // AllTypes TRUE ignores the mask, so it is TRUE only when every
                // dimension was asked for (N9, see ModelImportAllTypes).
                // duplicateDims stays FALSE here on purpose: the page of THIS
                // version says the opposite of the page of the 3
                // (extracted_obsoleteapi\DrawingDoc\DrawingDoc__InsertModelAnnotations2.htm:
                // «duplicateDims TRUE to insert duplicate dimensions, FALSE to
                // eliminate duplicate dimensions»), and the probe of M2 measured
                // the 3 only.
                bool allTypes = ModelImportAllTypes(all);
                try
                {
                    if (drawing.InsertModelAnnotations2(
                            (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                            allTypes, types, true, false, false))
                    {
                        tested.Add("v2/" + mask);
                        after = CountDimensions(drawing);
                        if (after < 0) return Failed("I asked for the dimensions of the model, but then I cannot count the dimensions of the drawing again: I cannot tell you whether they arrived. Look at the drawing before going on.");
                        if (after > before)
                        {
                            howItIsDone = "InsertModelAnnotations2" +
                                        (allTypes ? " with AllTypes" : " with the types mask") +
                                        ", bit " + mask;
                            break;
                        }
                    }
                }
                catch { }
            }

            // InsertModelDimensions has no mask at all
            // (IDrawingDoc~InsertModelDimensions.html: «0 - All dimensions in
            // the view»): it runs only when every dimension was asked for (N9).
            if (after <= before && ModelImportMayTakeEveryViewDimension(all))
            {
                // The oldest of all, a single parameter. It returns `void`:
                // the only way to know whether it did anything is to count again.
                try
                {
                    drawing.InsertModelDimensions(
                        (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel);
                    tested.Add("InsertModelDimensions");
                    after = CountDimensions(drawing);
                    if (after < 0) return Failed("I asked for the dimensions of the model, but then I cannot count the dimensions of the drawing again: I cannot tell you whether they arrived. Look at the drawing before going on.");
                    if (after > before) howItIsDone = "InsertModelDimensions";
                }
                catch { }
            }

            if (after <= before)
            {
                // One view at a time, selecting it first: some
                // installations ignore «all the views».
                int howMany = 0;
                try
                {
                    var doc = (IModelDoc2)drawing;
                    var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                    while (v != null)
                    {
                        try
                        {
                            doc.ClearSelection2(true);
                            if (doc.Extension.SelectByID2(
                                    v.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                            {
                                // The types follow all_dimensions (N9: the
                                // NOT-MARKED bit here brought 7 unmarked
                                // dimensions with nothing marked), and
                                // DuplicateDims is the same TRUE as above (M2).
                                drawing.InsertModelAnnotations3(
                                    (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                                    ModelImportViewByViewTypes(all, extra),
                                    false, ModelImportV3DuplicateDims, false, false);
                                howMany++;
                            }
                        }
                        catch { }
                        v = v.GetNextView() as IView;
                    }
                    doc.ClearSelection2(true);
                }
                catch { }
                tested.Add("view by view");
                after = CountDimensions(drawing);
                if (after < 0) return Failed("I asked for the dimensions of the model, but then I cannot count the dimensions of the drawing again: I cannot tell you whether they arrived. Look at the drawing before going on.");
                if (after > before) howItIsDone = "one view at a time (" + howMany + ")";
            }

            if (after <= before)
            {
                // HOW MANY THE PART HAS, AND WHICH. Without this answer a
                // «no dimension arrived» does not distinguish between «the
                // call does not work» and «there was nothing to bring»:
                // they are two different defects and they are repaired in two different places.
                // HERE THE PART IS ASKED OF THE VIEW, not of the list of
                // open documents. It is an error message: it must say how many
                // dimensions **the part of THIS drawing** has, and taking the first one
                // open would mean counting the dimensions of another part and
                // printing them inside an explanation. An error message that
                // cites the wrong document sends one searching farther than where one
                // started.
                string _ignore;
                var dimensionNames = ModelDimensionNames(
                    ModelPathInView(drawing) ?? ModelToPutInView(out _ignore));
                int marked = dimensionNames.Count(n => n.EndsWith("[marked]"));

                // NOTHING MARKED, AND ONLY THE MARKED ONES WERE ASKED FOR (N9).
                // Until library 0.141.0 the fallbacks brought the unmarked
                // dimensions anyway: the N9 probe of Z-C21.3, 28/9/2026,
                // on a part with nothing marked the chain imported all 7 of
                // them, view by view, and answered «done». Now every step stops
                // at the marked masks, and the refusal says how to get the
                // others instead of bringing them unasked.
                if (!all && dimensionNames.Count > 0 && marked == 0)
                    return Failed(
                        "No dimension came in (there were " + before + ", there are " + after +
                        "): the part has " + dimensionNames.Count + " dimensions and none is marked " +
                        "for drawing (" + string.Join(", ", dimensionNames) + "). By default only the " +
                        "MARKED dimensions are imported, and no fallback brings the others " +
                        "unasked. Pass all_dimensions=true to import them all, or mark on the " +
                        "model the ones the drawing must show. I tried: " +
                        string.Join(", ", tested) + ".");

                return Failed(
                    "No dimension arrived (there were " + before + ", there are " + after +
                    "; the last attempt returned " + returnedItems + "). " +
                    (dimensionNames.Count == 0
                        ? "The part has no dimension to bring into the drawing: it was " +
                          "built without dimensioning the sketches. Dimensions go on the " +
                          "MODEL, not on the drawing."
                        : "The part has " + dimensionNames.Count + " dimensions, of which " +
                          marked +
                          " marked for the drawing (" +
                          string.Join(", ", dimensionNames) + "), and none arrives in the drawing. " +
                          "I tried: " + string.Join(", ", tested) + ". " +
                          // Up to library 0.141.0 the tail said the import never works out
                          // of process (18/9/2026). It does since 26/9/2026 (the bench case
                          // dimensions_come_from_the_model), and with the fallbacks now held
                          // to the MARKED masks (N9) this refusal is what a part gets when
                          // its marks do not reach the import: the N9 probe of Z-C21.3,
                          // 28/9/2026, marked two sketch dimensions and the MARKED mask
                          // brought 0 of them.
                          "The import works out of process (measured on 26/9/2026 and 28/9/2026). " +
                          "A dimension marked on the sketch may still not be one the MARKED " +
                          "mask brings (measured on 28/9/2026: two sketch dimensions marked, 0 imported). " +
                          "Pass all_dimensions=true to import every dimension of the model, or " +
                          "create the ones you need with " +
                          "drawing_dimension."));
            }

            var measurements = new Dictionary<string, object>
            {
                ["how"] = howItIsDone,
                ["dimensions_before"] = before,
                ["dimensions_after"] = after,
                ["dimensions_inserted"] = after - before,
                ["views"] = views,
                ["only_marked_for_drawing"] = !all,
            };
            return WithoutFeature(measurements);
        }

        // ── THE IMPORT CHAIN OF model_dimensions, AS VALUES (Z-C21.3: M2, N9) ──
        //
        // Small static members with no interop in the signature, so that
        // Oce.Solid.Agent.ContractTests (Zc212DrawingContract) reads them by reflection. The
        // situation of N9, a part with NOTHING marked for drawing, cannot be
        // built by a bench case: every extrusion depth is born marked, and the
        // bridge marks the sketch dimensions it places (SwOperations.cs,
        // DimensionSegments).
        //
        // The bits (swInsertAnnotation_e): swInsertDimensions = 8,
        // swInsertDimensionsMarkedForDrawing = 32768,
        // swInsertDimensionsNotMarkedForDrawing = 524288.

        private const int ImportMarked = (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing;
        private const int ImportNotMarked = (int)swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing;
        private const int ImportGeneric = (int)swInsertAnnotation_e.swInsertDimensions;

        /// <summary>
        /// DuplicateDims of InsertModelAnnotations3: TRUE (finding M2).
        ///
        /// IDrawingDoc~InsertModelAnnotations3.html: «DuplicateDims True to
        /// eliminate duplicate dimensions, false to allow duplicate
        /// dimensions». Up to library 0.141.0 the bridge passed false with the
        /// comment «never duplicates», that is the opposite. Measured by
        /// Z-C21.3 probe_duplicate_dims (28/9/2026) on the plate of the bench
        /// case dimensions_come_from_the_model (120 x 80 x 10, hole Ø10 through,
        /// three standard views): false gave 7 dimensions with 3 of them twice
        /// (the 120 in the front and top views, the depth 10 in the front and
        /// side views, the 80 in the top and side views); true gave 4, none
        /// twice (front: the 10; top: 120, 80 and Ø10; side: none). A second
        /// call added 0 either way.
        /// </summary>
        internal const bool ModelImportV3DuplicateDims = true;

        /// <summary>
        /// The masks tried with InsertModelAnnotations3 (and 2), in order.
        ///
        /// With <paramref name="allDimensions"/> false every mask carries the
        /// MARKED bit and none the NOT-MARKED bit (finding N9). Measured by
        /// the N9 probe of Z-C21.3 (28/9/2026) on a part with NOTHING
        /// marked: MARKED and MARKED|GENERIC, both placements, brought 0 of
        /// its 7 unmarked dimensions; NOT_MARKED alone brought all 7.
        /// </summary>
        internal static int[] ModelImportMasks(bool allDimensions)
        {
            return allDimensions
                ? new[] { ImportMarked | ImportNotMarked | ImportGeneric, ImportNotMarked, ImportGeneric, ImportMarked }
                : new[] { ImportMarked, ImportMarked | ImportGeneric };
        }

        /// <summary>
        /// AllTypes of InsertModelAnnotations2: TRUE only when every dimension
        /// was asked for (finding N9). The current page of the 2 says only
        /// «Obsolete»; the old one,
        /// extracted_obsoleteapi\DrawingDoc\DrawingDoc__InsertModelAnnotations2.htm,
        /// says «allTypes TRUE to insert all types of annotations, FALSE uses
        /// the types argument»: TRUE ignores the mask. Measured by
        /// the N9 probe: FALSE with the MARKED mask brought no
        /// unmarked dimension.
        /// </summary>
        internal static bool ModelImportAllTypes(bool allDimensions)
        {
            return allDimensions;
        }

        /// <summary>
        /// Whether InsertModelDimensions(0) may run: only when every dimension
        /// was asked for (finding N9). IDrawingDoc~InsertModelDimensions.html:
        /// «Inserts model dimensions into the selected drawing view», «0 - All
        /// dimensions in the view»: there is no mask. Measured by
        /// the N9 probe: with nothing selected it brought 0, with the
        /// first view selected it brought 2 unmarked dimensions — and nothing
        /// in model_dimensions decides what is selected when it gets there.
        /// </summary>
        internal static bool ModelImportMayTakeEveryViewDimension(bool allDimensions)
        {
            return allDimensions;
        }

        /// <summary>
        /// The types of the last fallback, one view at a time (finding N9).
        /// Measured by the N9 probe on the part with nothing marked:
        /// MARKED|NOT_MARKED|GENERIC brought all 7 unmarked dimensions (and
        /// the bridge's whole chain reached them through this step), MARKED
        /// alone brought 0.
        /// </summary>
        internal static int ModelImportViewByViewTypes(bool allDimensions, int extra)
        {
            return (allDimensions ? ImportMarked | ImportNotMarked | ImportGeneric : ImportMarked) | extra;
        }

        // ─────────────────────────────────────────────────────────────────
        // export_pdf
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The PDF of ALL the sheets, with the page count.
        ///
        /// The defect to catch is known: the default export takes the
        /// current sheet, and a three-sheet drawing comes out as a single
        /// page without errors. So all the sheets are asked for, and then the
        /// pages of the written file are COUNTED. Counting the pages of a PDF is
        /// crude, and it is the only check that can really fail.
        /// </summary>
        private Outcome ExportPdf(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null) return Failed("The active document is not a drawing.");

            string location = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(location))
            {
                string its = "";
                try { its = model.GetPathName() ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(its))
                    return Failed(
                        "The drawing has never been saved and you did not tell me where " +
                        "to put the PDF.");
                location = Path.ChangeExtension(its, ".pdf");
            }

            int sheets = CountSheets(model);
            if (sheets < 0)
                return Failed(
                    "I cannot count the sheets of the drawing: without that number " +
                    "I could not check that the PDF has all of them, which is the reason " +
                    "why this operation exists. I wrote nothing.");
            if (sheets == 0) return Failed("The drawing has no sheets.");

            object dataPdf;
            try { dataPdf = _sw.GetExportFileData((int)swExportDataFileType_e.swExportPdfData); }
            catch (Exception ex) { return Failed("I cannot prepare the PDF: " + ex.Message); }

            var pdf = dataPdf as IExportPdfData;
            if (pdf == null)
                return Failed("This installation does not expose the PDF export options.");

            bool everySheetAsked;
            try
            {
                pdf.ViewPdfAfterSaving = false;
                // ALL the sheets, explicitly. It is the defect this
                // operation exists not to commit.
                everySheetAsked = pdf.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportAllSheets, null);
            }
            catch (Exception ex) { return Failed("I cannot ask for all the sheets: " + ex.Message); }
            // THE ANSWER OF SetSheets IS READ (Z-C21.8, finding L13-6).
            // IExportPdfData~SetSheets.html, Return Value: «True if the drawings
            // sheets are set to export to PDF, false if not». Until library
            // 0.144.0 the answer was dropped, and a false went on to the export
            // with whatever sheets SOLIDWORKS kept: the one-page PDF of a
            // three-sheet drawing that this operation exists to prevent.
            //
            // BUT THE ANSWER LIES (full bench on 0.145.0, 30/9/2026): SetSheets
            // answered false in drawing_exports_to_pdf and dimensions_arrive_in_the_pdf,
            // whose PDFs came out right in every run up to 0.144.0, when the answer
            // was dropped. So a false is not a refusal: it is said in the answer
            // («set_sheets_answer»), and the page count read from the written file
            // against the sheets of the drawing (PdfPageCountRefusal below) decides.
            if (!everySheetAsked)
                _log("[agent] IExportPdfData.SetSheets answered false; the page count of the written PDF decides");

            if (File.Exists(location))
            {
                try { File.Delete(location); }
                catch (Exception ex)
                {
                    return Failed(
                        "There is already a PDF there and I cannot replace it (" + ex.Message +
                        "): it is probably open.");
                }
            }

            // The save flag before the export (report 8cd843 N44, round 12): on a copy
            // of the drawing of two sheets it was false before export_pdf and true
            // after, and «document close» then refused the drawing (t35s, 27/10/2026).
            bool unsavedBeforePdf = true;
            try { unsavedBeforePdf = model.GetSaveFlag(); } catch { }

            int errors = 0, warnings = 0;
            bool ok;
            try
            {
                ok = model.Extension.SaveAs(
                    location, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    pdf, ref errors, ref warnings);
            }
            catch (Exception ex) { return Failed("PDF not written: " + ex.Message); }

            if (!ok || !File.Exists(location))
                // L13-7: the codes by their swFileSaveError_e / swFileSaveWarning_e
                // names (ErrorCodeDecoder), the raw number kept for a search.
                return Failed("The PDF was not written (code " + errors + SaveErrorNames(errors) + ")." +
                               SaveWarningsTail(warnings));

            int pages = CountPdfPages(location);
            string pagesRefusal = PdfPageCountRefusal(pages, sheets, location);
            if (pagesRefusal != null) return Failed(pagesRefusal);

            var measurements = new Dictionary<string, object>
            {
                ["path"] = location,
                ["sheets"] = sheets,
                ["pages"] = pages > 0 ? (object)pages : null,
                ["byte"] = new FileInfo(location).Length,
                // L13-6: what SetSheets answered; false has been measured on PDFs
                // that came out complete, so the page count above is the check.
                ["set_sheets_answer"] = everySheetAsked,
                // L13-7: the Warnings of the save, decoded, also on a success.
                ["save_warnings"] = SaveWarningsForAnswer(warnings),
            };
            // WHAT A FALSE MEANS, in the answer (report 8cd843 N22, 27/10/2026:
            // false next to a right PDF, with nothing to read). Under 100
            // characters, so the summary keeps it.
            if (!everySheetAsked) measurements["set_sheets_note"] = SetSheetsFalseNote(pages, sheets);
            bool unsavedAfterPdf = false;
            try { unsavedAfterPdf = model.GetSaveFlag(); } catch { }
            if (MarkedUnsavedByThePdf(unsavedBeforePdf, unsavedAfterPdf))
            {
                measurements["marked_unsaved"] = true;
                measurements["warning_marked_unsaved"] = PdfMarkedUnsavedWarning(sheets);
            }
            string pdfWarningAdvice = SaveWarningsAdvice(warnings);
            if (pdfWarningAdvice != null) measurements["save_warnings_note"] = pdfWarningAdvice;

            // THE PDF IS READ BACK, IF ONE ASKS WHAT IT MUST CONTAIN (entries GT3-bis,
            // Z-C9). Measured on 24/9/2026: the text of the drawing comes out as
            // text, but written as glyph indices behind the /ToUnicode
            // tables — a search in the bytes does not find it. `PdfText` decodes
            // it. It says what is there and what is not, without refusing: a
            // drawing number missing from the PDF is something to know, not a
            // PDF to throw away.
            var searched = StringList(a, "search");
            if (searched.Count > 0)
            {
                string text = "";
                try { text = PdfText.Read(location); } catch { }
                var foundItems = new List<object>();
                var wrappedItems = new List<string>();
                var missingItems = new List<object>();
                foreach (var c in searched)
                {
                    string where = PdfSearchVerdict(text, c);
                    if (where == "found") foundItems.Add(c);
                    else if (where == "on_several_lines") wrappedItems.Add(c);
                    else missingItems.Add(c);
                }
                measurements["found"] = foundItems.ToArray();
                // A value wrapped in its box is printed, over two lines: it is not
                // «not_found» (report 8cd843, round 19), but it is worth a look.
                measurements["found_on_several_lines"] = wrappedItems.ToArray();
                string wrappedNote = OnSeveralLinesNote(wrappedItems);
                if (wrappedNote != null) measurements["on_several_lines_note"] = wrappedNote;
                measurements["not_found"] = missingItems.ToArray();
                if (missingItems.Count > 0)
                    measurements["warning"] = "I do not find in the written PDF: «" + string.Join("», «", missingItems) +
                        "». If they were supposed to be there, check the drawing before sending it.";
            }
            // ── DO THE DIMENSIONS REACH THE PDF? (entry Z-C9, the «objects» half) ──
            //
            // The counters count in the document: a dimension on a layer that is not
            // printed, or in a hidden view, counts and does not reach the workshop.
            // Every number printed by the dimensions must appear in the text of the PDF
            // at least as many times as they print it (PdfDimensionOperations).
            int drawingDimensions, dimensionsWithoutText;
            var expectedNumbers = DimensionNumbers(drawing, out drawingDimensions, out dimensionsWithoutText);
            measurements["dimensions_in_drawing"] = drawingDimensions;
            measurements["dimensions_not_checked"] = dimensionsWithoutText;   // their text cannot be read
            if (dimensionsWithoutText > 0)
                measurements["warning"] = (measurements.ContainsKey("warning") ? measurements["warning"] + " " : "") +
                    dimensionsWithoutText + (dimensionsWithoutText == 1 ? " dimension has no" : " dimensions have no") +
                    " text that I can read: I did not check whether they reach the PDF.";
            if (drawingDimensions > 0)
            {
                string pdfText = null;
                try { pdfText = PdfText.Read(location); } catch { }
                if (string.IsNullOrWhiteSpace(pdfText))
                    measurements["dimensions_missing_in_pdf"] = null;   // the text cannot be read: I do not know
                else
                {
                    var areMissing = DimensionsMissingInPdf(expectedNumbers, pdfText);
                    measurements["dimensions_missing_in_pdf"] = areMissing.ToArray();
                    measurements["how_many_dimension_numbers_missing"] = areMissing.Count;
                    if (areMissing.Count > 0)
                        measurements["warning"] = (measurements.ContainsKey("warning") ? measurements["warning"] + " " : "") +
                            "The PDF is missing numbers that the dimensions of the drawing print: " + string.Join(", ", areMissing) +
                            ". Usually it is a dimension on a layer that is not printed or in a hidden view: look " +
                            "at the drawing before sending it to the workshop.";
                }
            }
            _log("[agent] PDF: " + sheets + " sheets, " + pages + " pages, " + location);
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// Whether the export raised the save flag of a drawing that had none (N44).
        /// Pure, so the contract suite reads it.
        /// </summary>
        internal static bool MarkedUnsavedByThePdf(bool unsavedBefore, bool unsavedAfter)
        {
            return !unsavedBefore && unsavedAfter;
        }

        /// <summary>
        /// What export_pdf says when SOLIDWORKS marked the drawing as changed while
        /// writing the PDF (N44), as interference_check says it (N16). Pure, so the
        /// contract suite reads it.
        /// </summary>
        internal static string PdfMarkedUnsavedWarning(int sheets)
        {
            return "SOLIDWORKS marked the drawing as changed while it wrote the PDF of its " + sheets +
                   (sheets == 1 ? " sheet" : " sheets") + " (measured: saved before, «to be saved» after): nothing in it " +
                   "changed, and «document» «close» refuses it as unsaved. Put «save_document» after «export_pdf» in " +
                   "the plan, or let the person close it.";
        }

        // ─────────────────────────────────────────────────────────────────
        // save_document
        // ─────────────────────────────────────────────────────────────────

        private Outcome SaveDocument(IModelDoc2 model, Dictionary<string, object> a)
        {
            if (model == null) return Failed("There is no open document.");

            string location = Str(a, "path", null);
            int errors = 0, warnings = 0;
            bool ok;

            string its = "";
            try { its = model.GetPathName() ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(location))
            {
                if (string.IsNullOrWhiteSpace(its))
                    return Failed(
                        "The document has never been saved: tell me where to put it.");
                location = its;
            }
            else if (SameFile(its, location))
            {
                // ITS OWN FILE IS SAVED WITH THE VERY STRING SOLIDWORKS HOLDS
                // (26/10/2026). SaveAs compares the path as text: a document
                // whose path SOLIDWORKS keeps with forward slashes (it was saved
                // that way once) answered swGenericSaveError to the same path
                // with backslashes, and the other way round. Four calls on a copy
                // of the part of 13/10/2026, each with its control, gave the same
                // rule; with the string of GetPathName the save went through.
                location = its;
            }

            // ANOTHER FILE ALREADY THERE (report 8cd843 N55, round 14): a save onto an existing
            // file that is not this document's own replaced it, and the answer said only the
            // bytes. Now it says what it replaced; with «overwrite» false it refuses instead.
            // Not a refusal by default: the bench writes in a folder that keeps the files of
            // the run before, and so does whoever saves the same copy twice.
            long replacedBytes = -1;
            DateTime replacedWritten = DateTime.MinValue;
            if (!SameFile(its, location))
            {
                try
                {
                    if (File.Exists(location))
                    {
                        var old = new FileInfo(location);
                        replacedBytes = old.Length;
                        replacedWritten = old.LastWriteTime;
                    }
                }
                catch { }
            }
            string kept = ReplaceRefused(location, replacedBytes, replacedWritten, Bool(a, "overwrite", true));
            if (kept != null) return Failed(kept);

            try
            {
                ok = model.Extension.SaveAs(
                    location, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, ref errors, ref warnings);
            }
            catch (Exception ex) { return Failed("Not saved: " + ex.Message); }

            if (!ok || !File.Exists(location))
            {
                // THE CODE ALONE CANNOT BE USED. `SaveAs` answers a
                // number, and the number does not say what to do: the most common cause
                // — measured on 18/9/2026 while trying something else — is
                // that **the folder does not exist**, and that is checked from here in
                // a millisecond instead of making the reader guess it.
                //
                // *An error message that reports a code and stops
                // moves the work onto whoever receives it.*
                string folder = null;
                try { folder = Path.GetDirectoryName(location); } catch { }
                if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                    return Failed(
                        "The document was not saved: **the folder does not exist** — «" +
                        folder + "». SOLIDWORKS does not create it, and its error code " +
                        "(" + errors + SaveErrorNames(errors) + ") does not say so. Create it, or give me a path inside " +
                        "a folder that exists.");

                bool isReadOnly = false;
                try
                {
                    isReadOnly = File.Exists(location) &&
                                  new FileInfo(location).IsReadOnly;
                }
                catch { }
                if (isReadOnly)
                    return Failed(
                        "The document was not saved: the file already exists and is " +
                        "READ-ONLY — «" + location + "». Clear the read-only flag, or check it out " +
                        "if it is in a PDM.");

                // A FILE THAT ANOTHER OPEN DOCUMENT HOLDS (26/10/2026 night, t10
                // and t11 run twice): the second run saved a new part onto the
                // file the first run had left open, and SOLIDWORKS answered only
                // swGenericSaveError. Looked at after the refusal, so it never
                // stops a save that would go through.
                string holder = null;
                if (!SameFile(its, location))
                {
                    try
                    {
                        var open = _sw.GetOpenDocumentByName(location) as IModelDoc2;
                        if (open != null) holder = open.GetTitle();
                    }
                    catch { }
                }
                if (holder != null)
                    return Failed(
                        "The document was not saved: **another open document holds that file** — «" + holder +
                        "» is «" + location + "», and SOLIDWORKS does not write a document onto it (code " +
                        errors + SaveErrorNames(errors) + "). Close it first («document» «close» with that " +
                        "«path») and save again, or give me another «path».");

                // L13-7: the codes by their enum names (ErrorCodeDecoder).
                // ITS OWN FILE REFUSED WITH THE GENERIC CODE: the two causes
                // measured on 26/10/2026, and the road that worked. A document
                // saved once with forward slashes in its path, and a part that
                // got a new equation while another window was in front, both
                // answer swGenericSaveError to every save on their own file; a
                // save under a NEW name went through in both, and the document
                // was savable again afterwards (the old name stayed refused).
                bool ownFile = SameFile(its, location);
                bool generic = (errors & (int)swFileSaveError_e.swGenericSaveError) != 0;
                return Failed("The document was not saved (code " + errors + SaveErrorNames(errors) + ")." +
                               (ownFile && generic
                                   ? " SOLIDWORKS refuses to write this document onto its own file. Measured causes: " +
                                     "the document was saved once with forward slashes in its path, or a new equation " +
                                     "was added while another window was in front. Save it under a new name (pass a " +
                                     "new «path»): that worked in both cases, and the document saves again afterwards."
                                   : "") +
                               SaveWarningsTail(warnings));
            }

            var measurements = new Dictionary<string, object>
            {
                ["path"] = location,
                ["byte"] = new FileInfo(location).Length,
                // The raw mask stays: bench cases read it (measures.warnings = 0).
                ["warnings"] = warnings,
                // L13-7: and its swFileSaveWarning_e names.
                ["save_warnings"] = SaveWarningsForAnswer(warnings),
            };
            string saveWarningAdvice = SaveWarningsAdvice(warnings);
            if (saveWarningAdvice != null) measurements["save_warnings_note"] = saveWarningAdvice;
            // What was there before (N55), always: false is the answer to «did I lose a file».
            measurements["replaced_another_file"] = replacedBytes >= 0;
            if (replacedBytes >= 0)
            {
                measurements["replaced_file"] = new Dictionary<string, object>
                {
                    ["byte"] = replacedBytes,
                    ["written"] = replacedWritten.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                };
                measurements["replaced_note"] = "«" + location + "» already existed and was not this document's file: it was " +
                    "replaced. Pass «overwrite»: false to be refused instead.";
            }
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// The refusal of a save onto another existing file when «overwrite» is false (report 8cd843
        /// N55), null when the save may go on: no file there (<paramref name="bytes"/> below zero) or
        /// overwriting allowed. Pure, so the contract suite reads it.
        /// </summary>
        internal static string ReplaceRefused(string location, long bytes, DateTime written, bool overwrite)
        {
            if (bytes < 0 || overwrite) return null;
            return "«" + location + "» already exists (" + bytes + " bytes, written " +
                   written.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ") and is not this document's file, " +
                   "and «overwrite» is false: I saved nothing. Give another «path», or «overwrite»: true to replace it.";
        }

        // ─────────────────────────────────────────────────────────────────
        // Tools
        // ─────────────────────────────────────────────────────────────────

        /// <summary>A successful outcome for an operation that creates no feature.</summary>
        private Outcome WithoutFeature(Dictionary<string, object> measurements)
        {
            return new Outcome { Succeeded = true, Measurements = measurements };
        }

        private static int SheetFormatCode(string itemName)
        {
            switch ((itemName ?? "").Trim().ToUpperInvariant())
            {
                case "A0": return (int)swDwgPaperSizes_e.swDwgPaperA0size;
                case "A1": return (int)swDwgPaperSizes_e.swDwgPaperA1size;
                case "A2": return (int)swDwgPaperSizes_e.swDwgPaperA2size;
                case "A4": return (int)swDwgPaperSizes_e.swDwgPaperA4size;
                case "A4V":
                case "A4 PORTRAIT": return (int)swDwgPaperSizes_e.swDwgPaperA4sizeVertical;
                default: return (int)swDwgPaperSizes_e.swDwgPaperA3size;
            }
        }

        /// <summary>
        /// The id of swStandardViews_e for the name the designer uses, or -1.
        /// swStandardViews_e (SolidWorks.Interop.swconst~swStandardViews_e.html):
        /// swFrontView 1, swBackView 2, swLeftView 3, swRightView 4, swTopView 5,
        /// swBottomView 6, swIsometricView 7, swTrimetricView 8, swDimetricView 9.
        /// </summary>
        internal static int StandardViewId(string requested)
        {
            switch (requested)
            {
                case "isometric": return (int)swStandardViews_e.swIsometricView;
                case "dimetric": return (int)swStandardViews_e.swDimetricView;
                case "trimetric": return (int)swStandardViews_e.swTrimetricView;
                case "front": return (int)swStandardViews_e.swFrontView;
                case "back": return (int)swStandardViews_e.swBackView;
                case "top": return (int)swStandardViews_e.swTopView;
                case "bottom": return (int)swStandardViews_e.swBottomView;
                case "right": return (int)swStandardViews_e.swRightView;
                case "left": return (int)swStandardViews_e.swLeftView;
                default: return -1;
            }
        }

        /// <summary>
        /// THE NAMES OF THE NAMED VIEWS OF A DOCUMENT THAT ARE A STANDARD VIEW, told
        /// by what the view does and not by what it is called (see ModelView). The
        /// system views (the ones that start with an asterisk) come first, the later
        /// ones of the list before the earlier ones: «*Normal To» is the first of the
        /// list and looks like the front view, and the front view must not be
        /// confused with it. Empty when the document is not open or nothing can be
        /// told.
        /// </summary>
        private List<string> StandardViewNames(string filePath, int standardView)
        {
            var outside = new List<string>();
            try
            {
                var d = FindOpenDocument(filePath);
                if (d == null) return outside;
                var names = (d.GetModelViewNames() as string[] ?? new string[0])
                    .Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                var shown = StandardViewNamesByShowing(d, names, standardView);
                if (shown != null)
                {
                    var system = shown.Where(n => n.StartsWith("*", StringComparison.Ordinal))
                                      .OrderByDescending(n => names.IndexOf(n)).ToList();
                    outside.AddRange(system);
                    outside.AddRange(shown.Where(n => !system.Contains(n)));
                    return outside;
                }
                // The order of the ids, with no window: only when it is all there.
                var stars = names.Where(n => n.StartsWith("*", StringComparison.Ordinal)).ToList();
                if (stars.Count == 10 && standardView >= 1 && standardView <= 9)
                    outside.Add(stars[standardView]);
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// The names whose view leaves the model view where the standard view leaves it,
        /// or null when the document has no model view to look at. The orientation the
        /// window had is put back, from a copy of its data, whatever happens.
        /// </summary>
        private List<string> StandardViewNamesByShowing(IModelDoc2 d, List<string> names, int standardView)
        {
            var view = d.ActiveView as IModelView;
            var before = OrientationData(view);
            if (before == null) return null;
            var found = new List<string>();
            try
            {
                d.ClearSelection2(true);   // with a face selected «*Normal To» turns to it
                d.ShowNamedView2("", standardView);
                var wanted = OrientationData(view);
                if (wanted == null) return null;
                // Another standard view is shown before every name: a name that SOLIDWORKS
                // does not show would otherwise leave the view of the name before it.
                int other = standardView == (int)swStandardViews_e.swBackView
                    ? (int)swStandardViews_e.swFrontView : (int)swStandardViews_e.swBackView;
                foreach (var n in names)
                {
                    d.ShowNamedView2("", other);
                    d.ShowNamedView2(n, -1);
                    if (SameRotation(wanted, OrientationData(view))) found.Add(n);
                }
            }
            finally
            {
                try
                {
                    var math = (IMathUtility)_sw.GetMathUtility();
                    view.Orientation3 = (MathTransform)math.CreateTransform(before);
                }
                catch { }
            }
            return found;
        }

        /// <summary>The data of the orientation of a model view (16 doubles), or null.</summary>
        private static double[] OrientationData(IModelView view)
        {
            try
            {
                var transform = view == null ? null : view.Orientation3 as IMathTransform;
                var data = transform == null ? null : transform.ArrayData as double[];
                return data != null && data.Length >= 9 ? (double[])data.Clone() : null;
            }
            catch { return null; }
        }

        /// <summary>Two orientations are the same one when their 3x3 rotations agree to a millionth.</summary>
        internal static bool SameRotation(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 9 || b.Length < 9) return false;
            for (int i = 0; i < 9; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-6) return false;
            return true;
        }

        /// <summary>
        /// How many sheets the drawing has, or **-1 if it could not be read**
        /// (entry Z-C8). A drawing always has at least one sheet: a list that
        /// does not arrive is not a zero, it is a read that went badly — and with
        /// zero `export_pdf` answered «the drawing has no sheets».
        /// </summary>
        private int CountSheets(IModelDoc2 drawing)
        {
            try
            {
                var d = drawing as IDrawingDoc;
                if (d == null) return -1;
                var names = d.GetSheetNames() as string[];
                return names == null ? -1 : names.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// The real views of the drawing (sheet excluded), or **-1 if it could not
        /// be read** (entry Z-C8). It had the shape the scanner did not
        /// see: `int n = 0; try { ... } catch { }`, and an error halfway
        /// through gave a number lower than the true one, stated as true.
        /// </summary>
        private int CountViews(IDrawingDoc drawing)
        {
            int n = 0;
            try
            {
                var sheet = drawing.GetFirstView() as IView;   // the first one is the SHEET
                if (sheet == null) return -1;
                var v = sheet.GetNextView() as IView;
                while (v != null) { n++; v = v.GetNextView() as IView; }
            }
            catch { return -1; }
            return n;
        }

        private List<string> ViewNames(IDrawingDoc drawing)
        {
            var outside = new List<string>();
            try
            {
                var sheet = drawing.GetFirstView() as IView;
                if (sheet == null) return outside;
                var v = sheet.GetNextView() as IView;
                while (v != null)
                {
                    try { outside.Add(v.Name); } catch { }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// The dimensions of all the views, or **-1 if it could not be read**
        /// (entry Z-C8): a view that does not answer does not count zero, because the
        /// sum of the others would be a number lower than the true one. Whoever
        /// compares before and after uses `NotGrown`.
        /// </summary>
        private int CountDimensions(IDrawingDoc drawing)
        {
            int n = 0;
            try
            {
                var sheet = drawing.GetFirstView() as IView;
                if (sheet == null) return -1;
                var v = sheet.GetNextView() as IView;
                while (v != null)
                {
                    n += v.GetDimensionCount();
                    v = v.GetNextView() as IView;
                }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// Did the count of dimensions NOT go up from `before`? It is said only of
        /// two counts read. If one of the two cannot be read, the only
        /// measurement that exists remains: the object the call returned (null =
        /// not born). It is less than the house rule would want — «the
        /// value that comes back from whoever did the thing is not the measurement of the
        /// thing» — but it is truer than an invented zero.
        /// </summary>
        private bool NotGrown(IDrawingDoc drawing, int before, object returned)
        {
            int now = CountDimensions(drawing);
            if (before >= 0 && now >= 0) return now <= before;
            return returned == null;
        }

        private static double[] SheetProperty(ISheet sheet)
        {
            try { return sheet.GetProperties2() as double[]; }
            catch { return null; }
        }

        private static double[] SheetMeasurements(ISheet sheet)
        {
            var p = SheetProperty(sheet);
            if (p == null || p.Length < 7) return null;
            // 5 = width, 6 = height, in metres.
            return new[] { Units.Mm(p[5]), Units.Mm(p[6]) };
        }

        private static double[] ReadScale(ISheet sheet)
        {
            var p = SheetProperty(sheet);
            if (p == null || p.Length < 4) return null;
            // 2 = numerator, 3 = denominator.
            if (p[3] == 0) return null;
            return new[] { p[2], p[3] };
        }

        /// <summary>
        /// How many dimensions the MODEL has, that is how many there would be to
        /// bring onto the drawing. They are counted by walking the tree and asking
        /// every feature for its displayed dimensions — the same route
        /// the state of the document uses to list them.
        /// </summary>
        private List<string> ModelDimensionNames(string filePath)
        {
            var outside = new List<string>();
            try
            {
                var d = FindOpenDocument(filePath);
                var partDoc = d as IPartDoc;
                if (partDoc == null) return outside;
                var f = partDoc.IFirstFeature();
                int guard = 0;
                while (f != null && guard++ < 2000)
                {
                    try
                    {
                        var q = f.GetFirstDisplayDimension() as IDisplayDimension;
                        while (q != null)
                        {
                            try
                            {
                                var real = q.GetDimension2(0) as IDimension;
                                string itemName = real != null ? real.FullName : "?";

                                // MARKED OR NOT, NEXT TO THE NAME, and it is the
                                // difference that splits the diagnosis in two.
                                // `model_dimensions` by default imports ONLY the
                                // marked ones: without this datum, «the part has 7
                                // dimensions and none arrives» leaves open two
                                // opposite explanations — that none is
                                // marked (a fault upstream, in whoever should have
                                // marked them) or that they are marked and the import does not
                                // take them (a fault in the import).
                                // On 18/9/2026 a probe made on purpose was needed
                                // to know which of the two: the datum was there,
                                // and the answer did not say it.
                                try
                                {
                                    itemName += q.MarkedForDrawing ? " [marked]" : " [NOT marked]";
                                }
                                catch { itemName += " [mark not readable]"; }

                                outside.Add(itemName);
                            }
                            catch { outside.Add("?"); }
                            q = f.GetNextDisplayDimension(q) as IDisplayDimension;
                        }
                    }
                    catch { }
                    f = f.IGetNextFeature();
                }
            }
            catch { }
            return outside;
        }

        /// <summary>The document open at that path, or null.</summary>
        private IModelDoc2 FindOpenDocument(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 200)
                {
                    string its = "";
                    try { its = d.GetPathName() ?? ""; } catch { }
                    if (IsSameFile(its, filePath)) return d;
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return null;
        }

        /// <summary>What this document calls its named views.</summary>
        private List<string> ModelViewNames(string filePath)
        {
            var outside = new List<string>();
            try
            {
                var d = FindOpenDocument(filePath);
                if (d == null) return outside;
                var names = d.GetModelViewNames() as string[];
                if (names == null) return outside;
                foreach (var n in names)
                    if (!string.IsNullOrWhiteSpace(n)) outside.Add(n);
            }
            catch { }
            return outside;
        }

        /// <summary>The documents open now, with path and type.</summary>
        private List<Dictionary<string, object>> OpenDocuments()
        {
            var outside = new List<Dictionary<string, object>>();
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                while (d != null)
                {
                    string filePath = "", docTitle = "";
                    int typ = 0;
                    try { filePath = d.GetPathName() ?? ""; } catch { }
                    try { docTitle = d.GetTitle() ?? ""; } catch { }
                    try { typ = d.GetType(); } catch { }
                    outside.Add(new Dictionary<string, object>
                    {
                        ["path"] = filePath,
                        ["document_title"] = docTitle,
                        ["kind"] = DocumentTypeName(typ),
                    });
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return outside;
        }

        internal static string DocumentTypeName(int typ)
        {
            switch (typ)
            {
                case (int)swDocumentTypes_e.swDocPART: return "part";
                case (int)swDocumentTypes_e.swDocASSEMBLY: return "assembly";
                case (int)swDocumentTypes_e.swDocDRAWING: return "drawing";
                default: return "other";
            }
        }

        /// <summary>
        /// Are two paths the same file?
        ///
        /// IT LOOKS LIKE A TRIVIAL QUESTION AND IT IS NOT. Measured at the first try
        /// inside SolidWorks, on 11 September 2026: the plan said
        /// `C:/Users/.../part.SLDPRT` and SolidWorks answered
        /// `C:\Users\...\part.SLDPRT`. Same file, two spellings, and a
        /// comparison between strings that said no. The result was the
        /// worst possible for a user: «the part is not open»
        /// while the part was there, open, in front of everybody.
        ///
        /// `Path.GetFullPath` is not used because it normalises relative
        /// paths too and can throw on a crooked string: here
        /// all that is needed is to make the slashes and the capitals agree.
        /// </summary>
        private static bool IsSameFile(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(
                a.Trim().Replace('/', '\\'),
                b.Trim().Replace('/', '\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The path of this document AS SOLIDWORKS WRITES IT,
        /// or null if it is not open.
        ///
        /// It is not enough to know that the file is open: the path must then be
        /// passed to `Create1stAngleViews2` and to
        /// `CreateDrawViewFromModelView3`, and there a forward slash in place
        /// of a backward one makes it return `false` without explanations
        /// and without creating anything.
        /// </summary>
        private string CanonicalPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            foreach (var d in OpenDocuments())
            {
                object p;
                if (!d.TryGetValue("path", out p)) continue;
                var its = Convert.ToString(p);
                if (IsSameFile(its, filePath)) return its;
            }
            return null;
        }

        private bool OpenedDocument(string filePath)
        {
            return CanonicalPath(filePath) != null;
        }

        /// <summary>The first part or assembly open: the one that goes onto the drawing.</summary>
        /// <summary>
        /// The model to put in view when the plan does not say.
        ///
        /// ── WHY IT IS NO LONGER CALLED «THE FIRST» ───────────────────────
        ///
        /// Until 18/9/2026 it returned **the first** open document of type
        /// part or assembly, in the order in which SOLIDWORKS lists them — an
        /// order that **nobody declares** and that changes with what was
        /// left open from before.
        ///
        /// The defect that came out of it, measured by the bench:
        /// `create_drawing` made the drawing of the RIGHT part — the one it is
        /// passed — and right after `standard_views`, which runs **on the
        /// drawing** and no longer has the part in hand, put into it the
        /// views of **another part**, left open from the previous case.
        ///
        /// **Three views, no error, and the drawing of a part that has
        /// nothing to do with it.** The title block read the `$PRPSHEET` from that part and
        /// showed empty boxes; the PDF would have come out and would have gone to the
        /// workshop. It is exactly the defect this project exists to
        /// not have, applied to the delivery route.
        ///
        /// ── WHAT IT DOES NOW ─────────────────────────────────────────────
        ///
        /// If **only one** is open, that is it — there is nothing to
        /// guess. If there are **two or more**, it REFUSES and says
        /// which they are, because choosing among them would mean deciding in
        /// place of whoever draws on something that then gives no error.
        ///
        /// *An order that nobody declares is not a choice: it is a draw
        /// that looks like a choice.*
        /// </summary>
        /// <summary>
        /// The path of the model this drawing really shows, asked
        /// of the first real view. It is the only answer that does not depend on what
        /// is open around it: the view knows which document it is attached to.
        /// Returns `null` if the drawing has no views yet.
        /// </summary>
        private static string ModelPathInView(IDrawingDoc drawing)
        {
            try
            {
                // The first «view» is the SHEET: the real views start after it.
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    try
                    {
                        var refObj = v.ReferencedDocument as IModelDoc2;
                        var p = refObj?.GetPathName();
                        if (!string.IsNullOrWhiteSpace(p)) return p;
                    }
                    catch { }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
        }

        private string ModelToPutInView(out string why)
        {
            why = null;
            var candidates = new List<string>();
            foreach (var d in OpenDocuments())
            {
                var typ = Convert.ToString(d["kind"]);
                if (typ != "part" && typ != "assembly") continue;
                var p = Convert.ToString(d["path"]);
                if (!string.IsNullOrWhiteSpace(p) &&
                    !candidates.Contains(p, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(p);
            }

            if (candidates.Count == 1) return candidates[0];

            if (candidates.Count == 0)
            {
                why =
                    "There is no open part to put in a view: a drawing view " +
                    "attaches to an OPEN document, not to a file on disk.";
                return null;
            }

            why =
                "There are " + candidates.Count + " open documents that I could put in a " +
                "view, and choosing one would be a lottery: " +
                string.Join(", ", candidates) +
                ". Tell me which with the «model» argument, writing the path. " +
                "It is NOT pedantry: until 18/9/2026 the first of the list was " +
                "taken here, and a drawing came out with the views of the wrong part " +
                "without anything giving an error.";
            return null;
        }

        /// <summary>
        /// How many pages the PDF just written really has, or -1 when they
        /// cannot be counted.
        ///
        /// The file is read and the «/Type /Page» objects that are not
        /// «/Type /Pages» (the node of the page tree) are counted. It is crude:
        /// a PDF with compressed object streams can hide them. A PDF has at
        /// least one page, so finding none means «I could not count», not
        /// «zero pages»: ZERO-E-ZERO, the unknown is -1 (Z-C21.8, finding
        /// L13-6; until library 0.144.0 it was 0, and the caller skipped the
        /// check on it without a word).
        /// </summary>
        private static int CountPdfPages(string filePath)
        {
            try
            {
                var text = File.ReadAllText(filePath, System.Text.Encoding.ASCII);
                int n = 0, i = 0;
                while (true)
                {
                    i = text.IndexOf("/Type", i, StringComparison.Ordinal);
                    if (i < 0) break;
                    int j = i + 5;
                    while (j < text.Length && (text[j] == ' ' || text[j] == '\r' ||
                                                text[j] == '\n' || text[j] == '\t')) j++;
                    if (j + 5 <= text.Length && text.Substring(j, 5) == "/Page")
                    {
                        char after = j + 5 < text.Length ? text[j + 5] : ' ';
                        if (after != 's') n++;   // «/Pages» is the node, not a page
                    }
                    i = j;
                }
                return n > 0 ? n : -1;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Why the PDF just written cannot be handed over, or null when its
        /// pages match the sheets (Z-C21.8, finding L13-6). Counting the pages
        /// is the one check of export_pdf that can fail, so a count that could
        /// not be taken is a refusal, not a pass: the file stays where it was
        /// written, and the message says so.
        /// </summary>
        /// <summary>
        /// The note next to «set_sheets_answer» false (report 8cd843 N22): the
        /// answer of SetSheets has been false on complete PDFs, the page count
        /// is the check. Under 100 characters, so CompactOutcomes keeps it. No
        /// interop type: Oce.Solid.Agent.ContractTests calls it by reflection.
        /// </summary>
        internal static string SetSheetsFalseNote(int pages, int sheets)
        {
            return "false is seen on complete PDFs too; the check is the page count: " +
                   (pages > 0 ? pages.ToString() : "?") + " of " + sheets;
        }

        internal static string PdfPageCountRefusal(int pages, int sheets, string path)
        {
            if (pages < 0)
                return "The PDF is written (" + path + "), but I cannot count its pages, so I cannot say " +
                       "that all " + sheets + (sheets == 1 ? " sheet is" : " sheets are") + " in it: that " +
                       "check is the reason this operation exists. Open the file and look before sending it.";
            if (pages != sheets)
                return "The PDF came out with " + pages + (pages == 1 ? " page" : " pages") + " and the drawing has " +
                       sheets + (sheets == 1 ? " sheet" : " sheets") + ". The file is there and it is not the one " +
                       "you need: look at it before sending it to the shop. (" + path + ")";
            return null;
        }

        /// <summary>
        /// WHERE A SEARCHED TEXT IS IN THE TEXT OF THE PDF (report 8cd843, round 19,
        /// observation, 28/10/2026). A value too long for its box is wrapped by
        /// SOLIDWORKS, and PdfText gives each printed line as a line of its own:
        /// measured on the PDF of round 19, a value of four words in a box of the
        /// title block came out as two lines, the first ending in a space. The plain
        /// search said «not_found» for a text that is printed. Only the line breaks
        /// are ignored, joined by a space or by nothing (a word cut in two): every
        /// other character must match as asked. «found», «on_several_lines» or
        /// «not_found». No interop type: Oce.Solid.Agent.ContractTests calls it by
        /// reflection.
        /// </summary>
        internal static string PdfSearchVerdict(string text, string wanted)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(wanted)) return "not_found";
            if (text.IndexOf(wanted, StringComparison.Ordinal) >= 0) return "found";
            // The spaces and tabs at a line break go with it: «A \nB» is «A B» or «AB».
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i < lines.Length - 1) lines[i] = lines[i].TrimEnd(' ', '\t');
                if (i > 0) lines[i] = lines[i].TrimStart(' ', '\t');
            }
            string joinedBySpace = string.Join(" ", lines);
            string joinedByNothing = string.Join("", lines);
            if (joinedBySpace.IndexOf(wanted, StringComparison.Ordinal) >= 0 ||
                joinedByNothing.IndexOf(wanted, StringComparison.Ordinal) >= 0)
                return "on_several_lines";
            return "not_found";
        }

        /// <summary>
        /// What «found_on_several_lines» means, for the answer; null when it is empty.
        /// </summary>
        internal static string OnSeveralLinesNote(List<string> texts)
        {
            if (texts == null || texts.Count == 0) return null;
            return "«" + string.Join("», «", texts) + "» " + (texts.Count == 1 ? "is" : "are") +
                   " in the PDF only across a line break: printed over more than one line, or where one text " +
                   "ends and the next begins. A value wrapped in a box of the title block can run out of it: " +
                   "look at the box.";
        }
    }
}
