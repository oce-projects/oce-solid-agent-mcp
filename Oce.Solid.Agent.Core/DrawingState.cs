using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE EYES ON THE DRAWING.
    ///
    /// The state of the document is the only thing the model sees: if a
    /// drawing reaches it as «document not of type part», the model can
    /// only guess. Until 0.39.0 it was exactly like that — a drawing
    /// was a line of excuses.
    ///
    /// Here what is really worked on is described: the SHEETS (format,
    /// scale, which one is active), the VIEWS (name, position, scale, how many dimensions
    /// they carry, of which model they are) and the OPEN DOCUMENTS, which in this
    /// family are not a detail but a precondition.
    ///
    /// THE RULE ALREADY PAID FOR HOLDS HERE TOO: what is written is what ADDS.
    /// There is no list of the annotations one by one — there are hundreds on
    /// a normal drawing and nobody names them; there is their COUNT, which is
    /// what is needed to notice that a dimension did not arrive.
    /// </summary>
    public partial class DocumentState
    {
        /// <summary>How many views are described in full, at most.</summary>
        private const int MaxViews = 24;

        /// <summary>
        /// The documents open now.
        ///
        /// If `sw` is null the document being read is returned at least:
        /// better a true line than an absent list, because absence is
        /// read as «there is nothing else» and it is a lie.
        /// </summary>
        private static List<Dictionary<string, object>> OpenDocuments(
            ISldWorks sw, IModelDoc2 active)
        {
            var outside = new List<Dictionary<string, object>>();
            string activePath = "", activeTitle = "";
            try { activePath = active?.GetPathName() ?? ""; } catch { }
            // THE TITLE, NOT ONLY THE PATH. A never-saved document
            // has no path, and with the comparison on the path alone in a
            // list of new documents NONE appeared active.
            try { activeTitle = active?.GetTitle() ?? ""; } catch { }

            if (sw == null) return outside;
            try
            {
                var d = sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 200)
                {
                    string filePath = "", docTitle = "";
                    int typ = 0;
                    try { filePath = d.GetPathName() ?? ""; } catch { }
                    try { docTitle = d.GetTitle() ?? ""; } catch { }
                    try { typ = d.GetType(); } catch { }

                    var entry = new Dictionary<string, object>
                    {
                        ["document_title"] = docTitle,
                        ["kind"] = SwOperations.DocumentTypeName(typ),
                    };
                    // The path only if there is one: a never-saved document has
                    // none, and an empty string in a list of paths is an
                    // invitation to use it.
                    if (!string.IsNullOrWhiteSpace(filePath)) entry["path"] = filePath;
                    bool isIt =
                        !string.IsNullOrWhiteSpace(activePath)
                            ? string.Equals(filePath, activePath,
                                  StringComparison.OrdinalIgnoreCase)
                            : (!string.IsNullOrWhiteSpace(activeTitle) &&
                               string.Equals(docTitle, activeTitle, StringComparison.Ordinal));
                    if (isIt) entry["active"] = true;

                    outside.Add(entry);
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>Sheets, views and counts of a drawing.</summary>
        private static void DescribeDrawing(IDrawingDoc drawing, Dictionary<string, object> data, ISldWorks sw)
        {
            if (drawing == null)
            {
                data["note"] = "unreadable drawing";
                return;
            }

            // ── the sheets ────────────────────────────────────────────────
            string active = "";
            try { active = (drawing.GetCurrentSheet() as ISheet)?.GetName() ?? ""; } catch { }

            var sheets = new List<Dictionary<string, object>>();
            try
            {
                var names = drawing.GetSheetNames() as string[];
                if (names != null)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        var entry = new Dictionary<string, object>
                        {
                            ["uid"] = "S" + (i + 1),
                            ["name"] = names[i],
                        };
                        if (string.Equals(names[i], active, StringComparison.Ordinal))
                            entry["active"] = true;

                        // The properties of the sheet can be read only from the
                        // ACTIVE one without changing sheet, and changing sheet to
                        // read would be a read that modifies: what can be read is read
                        // and the rest is left unsaid.
                        if (entry.ContainsKey("active"))
                        {
                            var p = SheetPropertyOf(drawing);
                            if (p != null)
                            {
                                entry["paper_size_mm"] = new object[]
                                {
                                    Math.Round(Units.Mm(p[5]), 1),
                                    Math.Round(Units.Mm(p[6]), 1),
                                };
                                if (p[3] != 0)
                                    entry["scale"] = FormatScale(p[2], p[3]);
                                // THE PROJECTION ANGLE IS SHOWN. It is the only thing of a
                                // drawing that can be wrong without
                                // any measurement noticing: right and
                                // left swapped, plausible drawing,
                                // mirrored part. If it is not written here, the
                                // model has no way to look at it.
                                if (p.Length > 4)
                                    entry["projection_angle"] = p[4] != 0 ? "first" : "third";
                            }
                        }
                        else
                            // SAID, NOT LEFT BLANK (report 8cd843 round 12, observation 3):
                            // a sheet of two with only its name read as a sheet with no
                            // views; the views below are those of the active sheet.
                            entry["not_read"] = NotActiveSheetNote(names[i]);
                        sheets.Add(entry);
                    }
                }
            }
            catch { }
            if (sheets.Count > 0) data["sheets"] = sheets;

            // ── the views ─────────────────────────────────────────────────
            //
            // THE FIRST VIEW IS NOT A VIEW: it is the SHEET. `GetFirstView()`
            // returns the sheet, and the real views start from `GetNextView()`.
            // Whoever forgets it describes a sheet as if it were a view, and
            // then dimensions an object that has no geometry.
            var views = new List<Dictionary<string, object>>();
            // ARE THE TOTALS int? (entry Z-C8): a view that does not answer makes the
            // total null, not a lower total stated as true. Only the
            // views are always counted — they are being walked.
            int totalViews = 0;
            int? quoteTotali = 0, totalCenterMarks = 0;
            int? gtolTot = 0, datumTot = 0, totalWelds = 0, rugTot = 0, noteTot = 0, annTot = 0;
            int orfaneTot = 0;
            // Dimensions attached to a silhouette: not counted as orphans,
            // because IsSame cannot check them (N3, see DimensionsWithoutEdge).
            int notVerifiableTotal = 0;
            int? totalBogusNumbers = 0;
            var bogusNumbers = new List<Dictionary<string, object>>();
            bool unknownOrphans = false;
            // ── THE SHEET IS COUNTED SEPARATELY, AND IT IS NOT A REFINEMENT ─
            //
            // Measured on 18/9/2026: the title block notes — thirty-nine on
            // this drawing template, sixty-seven in all — sit on the
            // SHEET, not on a view. And `InsertNote`, which today is **the only**
            // annotation API that works out of process (entry E2), puts
            // the note on the SHEET: 67 -> 68, while all three views stay
            // at zero.
            //
            // Counting only the views, `note` would be a field that CANNOT
            // become true with today's tools. **A field that cannot
            // change is not a measurement: it is a promise.** So the sheet
            // has its six numbers, with the same name and in a drawer of its own.
            try
            {
                var viewSheet = drawing.GetFirstView() as IView;
                if (viewSheet != null)
                    data["sheet_annotations"] = Annotations(viewSheet);
            }
            catch { }

            try
            {
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    totalViews++;
                    int? quote = ReadValue(() => v.GetDimensionCount());
                    quoteTotali += quote;

                    // THE CENTRE MARKS ARE COUNTED HERE, and not only inside
                    // the operation that puts them (entry W2.11). It is the rule of
                    // GA5: **the state and the operation must tell the same
                    // thing, reading it on their own.** If one day they
                    // diverged, one of the two would lie and it would not be known
                    // which.
                    // ENUMERATE, not GetCenterMarkCount: that one does not see the marks
                    // put by SOLIDWORKS when the view is created (26/9/2026).
                    int? centerMarks = ReadValue(() => { int n = CountCenterMarks.OfView(v); if (n < 0) throw new InvalidOperationException(); return n; });
                    totalCenterMarks += centerMarks;

                    // IS THE NUMBER THAT IS PRINTED THE ONE OF THE MODEL? (entry Z-C2)
                    int? bogus = DimensionsByNumberNotFromTheModel(v, bogusNumbers);
                    totalBogusNumbers += bogus;

                    // ── THE ANNOTATIONS, ONE FAMILY AT A TIME (entry Z-E1) ──
                    //
                    // WHY THEY ARE NEEDED BEFORE the entries that put them, and not
                    // after. Ten drawing entries are waiting to write
                    // surface finish, datum, weld symbols, GD&T. Each one
                    // would call an API that **answers yes without guaranteeing
                    // anything** — and four of those APIs return `Void`, which is
                    // a button and not an API. Without an INDEPENDENT counter
                    // read from the state, every entry would be green on the word
                    // of the operation itself.
                    //
                    // It is the rule of GA5, applied before the need: **the
                    // state and the operation must tell the same thing,
                    // reading it on their own.**
                    int? gtol = ReadValue(() => v.GetGTolCount());
                    int? datum = ReadValue(() => v.GetDatumTagCount());
                    int? weldments = ReadValue(() => v.GetWeldSymbolCount());
                    // `GetSFSymbolCount`, NOT `GetSurfaceFinishSymbolCount`:
                    // the second is the name by symmetry with
                    // `InsertSurfaceFinishSymbol`, and it does not exist.
                    int? roughness = ReadValue(() => v.GetSFSymbolCount());
                    int? note = ReadValue(() => v.GetNoteCount());
                    int? annotations = ReadValue(() => v.GetAnnotationCount());
                    gtolTot += gtol; datumTot += datum; totalWelds += weldments;
                    rugTot += roughness; noteTot += note; annTot += annotations;

                    // THE DIMENSIONS LEFT WITHOUT AN EDGE (entry Z-A2).
                    //
                    // `IAnnotation.IsDangling` is DEAD for dimensions on a drawing
                    // out of process: measured on 24/9/2026 with
                    // `Checks/Orphan`, it stays false on a dimension whose
                    // edge was suppressed, while the dimension keeps
                    // saying 20 mm. What sees it is asking whether the edge
                    // it is attached to is still among those the view
                    // SHOWS (1 before, 0 after). null means «I do not know»
                    // (entry Z-C8), not zero.
                    int notVerifiable;
                    int? orfane = DimensionsWithoutEdge(sw, v, out notVerifiable);
                    if (orfane.HasValue) orfaneTot += orfane.Value; else unknownOrphans = true;
                    notVerifiableTotal += notVerifiable;

                    if (views.Count < MaxViews)
                    {
                        var entry = new Dictionary<string, object>
                        {
                            ["uid"] = "V" + totalViews,
                        };
                        try { entry["name"] = v.Name; } catch { }
                        try { entry["kind"] = ViewTypeName(v.Type); } catch { }

                        var pos = ViewPosition(v);
                        if (pos != null) entry["position_mm"] = pos;
                        // WHERE THE VIEW IS SEEN (report 8cd843 round 14, observation 2): the Position
                        // of a detail is the centre of the whole model at the detail's scale, not of
                        // what it shows (detail B: Position 201, 127 mm; on the PDF x 321..395 mm,
                        // its outline 307.7..408 mm). The outline is the rectangle on the sheet.
                        try
                        {
                            var o = v.GetOutline() as double[];
                            if (o != null && o.Length >= 4)
                            {
                                entry["outline_mm"] = new object[] { Math.Round(o[0] * 1000, 1), Math.Round(o[1] * 1000, 1), Math.Round(o[2] * 1000, 1), Math.Round(o[3] * 1000, 1) };
                                entry["centre_on_the_sheet_mm"] = new object[] { Math.Round((o[0] + o[2]) / 2 * 1000, 1), Math.Round((o[1] + o[3]) / 2 * 1000, 1) };
                            }
                        }
                        catch { }

                        try
                        {
                            double s = v.ScaleDecimal;
                            if (s > 0) entry["scale"] = Math.Round(s, 4);
                        }
                        catch { }

                        // Zero dimensions is not a detail: it is the difference between
                        // a drawing and a sketch. It is always written.
                        entry["dimensions"] = quote;
                        // And zero marks is not a detail either: it is the
                        // difference between a drawing that can be drilled from and one
                        // that can only be looked at. It is always written, for the
                        // same reason — **a field that appears only when it has
                        // an interesting value teaches not to look for it.**
                        entry["center_marks"] = centerMarks;
                        entry["dimensions_with_number_not_from_model"] = bogus;
                        entry["dimensions_orphan"] = orfane;
                        // Unknown when the orphans are unknown: the same reading.
                        entry["dimensions_not_verifiable"] = orfane.HasValue ? (object)notVerifiable : null;

                        // ALWAYS, EVEN ALL AT ZERO, and for the same reason
                        // as the two lines above: a field that appears only
                        // when it has an interesting value teaches not to
                        // look for it, and whoever does not find it concludes that the API
                        // cannot answer rather than that the number is zero.
                        entry["annotations"] = Six(gtol, datum, weldments, roughness, note, annotations);

                        try
                        {
                            var refObj = v.ReferencedDocument as IModelDoc2;
                            var p = refObj?.GetPathName();
                            if (!string.IsNullOrWhiteSpace(p)) entry["model"] = p;
                        }
                        catch { }

                        views.Add(entry);
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch
            {
                // The pass was interrupted halfway: the totals are of a part
                // of the views, and nobody would know. They become «I do not know».
                quoteTotali = totalCenterMarks = null;
                gtolTot = datumTot = totalWelds = rugTot = noteTot = annTot = null;
                data["views_half_read"] = true;
            }

            if (views.Count > 0) data["views"] = views;
            data["views_total"] = totalViews;
            data["dimensions_total"] = quoteTotali;
            data["center_marks_total"] = totalCenterMarks;
            data["dimensions_with_number_not_from_model_total"] = totalBogusNumbers;
            if (bogusNumbers.Count > 0)
            {
                data["dimensions_with_number_not_from_model"] = bogusNumbers;
                data["warning_numbers"] =
                    bogusNumbers.Count + (bogusNumbers.Count == 1 ? " dimension prints" : " dimensions print") +
                    " a number that is not the model's (overwritten, or the value turned off " +
                    "with text in its place). The geometry is right, the paper is not: in the shop " +
                    "you work from the printed number.";
            }
            data["annotations_total"] = Six(gtolTot, datumTot, totalWelds, rugTot, noteTot, annTot);
            data["dimensions_orphan_total"] = unknownOrphans && orfaneTot == 0 ? (object)null : orfaneTot;
            data["dimensions_not_verifiable_total"] =
                unknownOrphans && notVerifiableTotal == 0 ? (object)null : notVerifiableTotal;
            if (orfaneTot > 0)
                data["warning_dimensions"] = orfaneTot + " dimensions are attached to edges that the views " +
                    "no longer show: on the sheet they get printed with the old number, hanging from " +
                    "nothing. Usually the part changed after the drawing was dimensioned.";

            // TWO DECLARED BLINDNESSES, and both would lead to wrong
            // conclusions if they stayed implicit.
            data["what_is_not_known_about_annotations"] =
                // 1. THE TITLE BLOCK IS NOT IN HERE. These counts run
                //    on the REAL VIEWS; the notes of the sheet format sit
                //    on the first «view», which is the sheet, and there are dozens — on
                //    this drawing template, thirty-nine. Whoever adds up the views and
                //    compares with the total of the document finds a big difference
                //    and concludes that something got lost.
                "«annotations_total» adds up the VIEWS; the notes of the title " +
                "block are on the SHEET and are counted apart, " +
                "in «sheet_annotations» — on this drawing template there " +
                "are sixty-seven of them, and adding them to the views would make " +
                "an empty drawing look dimensioned. And an annotation on a " +
                "non-printing layer exists, is counted, and is not in the PDF: " +
                "the count says what is in the document, not what comes out " +
                "of the printer. «dimensions_orphan» checks whether the edges and vertices each " +
                "dimension is attached to are among those the view shows. A dimension " +
                "attached to a SILHOUETTE (the apparent outline of a cylinder: the diameters of a turned part seen from the side) " +
                "cannot be checked, because SOLIDWORKS does not match it with any silhouette " +
                "the view shows: it is not counted as an orphan but in «dimensions_not_verifiable». " +
                "A dimension attached to a hidden edge still reads as an orphan without being " +
                "one.";
            if (totalViews > views.Count) data["views_truncated"] = true;

            // THE EMPTY DRAWING MUST BE STATED, not left to be deduced from a list that is
            // missing. It is the state in which every drawing is born, and the one in which it
            // stays if `Create1stAngleViews2` said «yes» without doing anything.
            if (totalViews == 0)
                data["note"] = "the drawing has no view yet";
            else if (quoteTotali.HasValue && quoteTotali.Value == 0)
                data["note"] = "the views are there but there is no dimension";
        }

        /// <summary>
        /// How many dimensions of the view are attached ONLY to edges or vertices that
        /// the view no longer shows (entry Z-A2). null if it cannot be known:
        /// without SOLIDWORKS in hand for `IsSame`, or on a view too loaded
        /// to compare everything with everything in a reasonable time.
        ///
        /// A dimension with no attachment at all (a free dimension) is not an orphan: it is
        /// free, and it is not counted.
        ///
        /// SILHOUETTES ARE NOT VERIFIABLE (Z-C21.3, finding N3). The diameters of
        /// a turned part seen from the side hang on two silhouette edges
        /// (IAnnotation~GetAttachedEntities3.html: «swSelSilhouettes |
        /// ISilhouetteEdge»), and ISldWorks.IsSame never recognises them: probe
        /// probe_silhouette_issame (28/9/2026), stepped shaft Ø20/Ø30, a Ø20
        /// born on two silhouettes (attached types [46, 46], swSelSILHOUETTES):
        /// 0 hits against the silhouettes the view shows, right after the
        /// dimension, after a rebuild and after a model change, while
        /// silhouette against silhouette answered Same for all four (too loose
        /// to be used). With Edge and Vertex only, that Ø20 was 1 orphan out of
        /// 2 dimensions, and the length beside it 0. So a dimension that no
        /// visible edge or vertex holds and that is attached to a silhouette
        /// (IAnnotation~GetAttachedEntityTypes.html: «Array of longs ... that
        /// indicate the attached entity types as defined in swSelectType_e») is
        /// not an orphan: it goes in <paramref name="notVerifiable"/>.
        /// </summary>
        private static int? DimensionsWithoutEdge(ISldWorks sw, IView v, out int notVerifiable)
        {
            notVerifiable = 0;
            if (sw == null) return null;
            try
            {
                var dims = v.GetDisplayDimensions() as object[];
                if (dims == null || dims.Length == 0) return 0;
                if (dims.Length > 60) return null;
                var visible = new List<object>();
                foreach (int typ in new[] { (int)swViewEntityType_e.swViewEntityType_Edge,
                                             (int)swViewEntityType_e.swViewEntityType_Vertex })
                {
                    var e = v.GetVisibleEntities2(null, typ) as object[];
                    if (e != null) visible.AddRange(e);
                }
                if (visible.Count > 800) return null;
                int orfane = 0;
                int onSilhouettes = 0;
                foreach (var o in dims)
                {
                    var ann = (o as IDisplayDimension)?.GetAnnotation() as IAnnotation;
                    var attached = ann?.GetAttachedEntities3() as object[];
                    if (attached == null || attached.Length == 0) continue;
                    bool seen = false;
                    foreach (var a in attached)
                    {
                        // A NULL ENTITY IS SKIPPED, AND THE COUNT DOES NOT CHANGE
                        // (Z-C21.3, finding C3-4: null seen, harmless; the filter is
                        // Z-C21.8). IAnnotation~GetAttachedEntities3.html: «swSelNOTHING
                        // | NULL (annotation is dangling or unsupported)». The Z-C21.3
                        // probe of C3-4 (28/9/2026), a dimension on the top
                        // edge of a block: with the block suppressed, and again with
                        // it deleted (IsDangling true), GetAttachedEntities3 gave one
                        // null of type 1, not swSelNOTHING. IsSame(null, edge)
                        // answered swObjectNotSame, and the orphan count was 1 with
                        // and without this filter, 0 with the block back. So the
                        // filter only saves one COM call per visible entity: the
                        // plain null passed here answered NotSame, while in the same
                        // probe a null wrapped in a DispatchWrapper threw
                        // RPC_E_SERVERFAULT, which through the catch below would have
                        // turned the whole view into «not known».
                        if (a == null) continue;
                        foreach (var vv in visible)
                            if (sw.IsSame(a, vv) == (int)swObjectEquality.swObjectSame) { seen = true; break; }
                        if (seen) break;
                    }
                    if (seen) continue;
                    if (IsAttachedToSilhouette(ann)) onSilhouettes++;
                    else orfane++;
                }
                notVerifiable = onSilhouettes;
                return orfane;
            }
            catch { notVerifiable = 0; return null; }
        }

        /// <summary>
        /// True when at least one attached entity of the annotation is a
        /// silhouette edge (swSelSILHOUETTES, 46 in the probe of N3). A types
        /// array that cannot be read is not a silhouette: the dimension keeps
        /// the old reading, orphan when nothing visible holds it.
        /// </summary>
        private static bool IsAttachedToSilhouette(IAnnotation ann)
        {
            try
            {
                var types = ann.GetAttachedEntityTypes() as Array;
                if (types == null) return false;
                foreach (var t in types)
                    if (Convert.ToInt32(t, System.Globalization.CultureInfo.InvariantCulture) ==
                        (int)swSelectType_e.swSelSILHOUETTES)
                        return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// The six numbers of a view (or of the sheet), always all six.
        ///
        /// `all` is called that and not «total»: `GetAnnotationCount` **is not
        /// the sum** of the other five — it also counts the dimensions and what
        /// none of the five families names. Calling it «total» would make one
        /// expect a sum that does not add up, and whoever sees it not add up looks for
        /// a defect where there is none.
        /// </summary>
        /// <summary>
        /// How many dimensions of the view print a number that is NOT the one of the
        /// model (entry Z-C2), or null if it cannot be read. It finds them, and
        /// puts at most ten in `details` with the way.
        ///
        /// «The dimension comes from the model, it cannot lie» holds as long as nobody
        /// writes over the number. Measured on 25/9/2026 (`probe0089
        /// testoquote`) on the three routes the interop offers:
        ///
        ///   just born                   GetOverride false, ShowDimensionValue true,
        ///                               GetText(0..4) all empty
        ///   SetOverride(true, 0.099)    GetOverride TRUE, GetOverrideValue 99 mm
        ///   SetText(0, "99")            writes the PREFIX and SWITCHES OFF the value:
        ///                               ShowDimensionValue false, GetText(1) «99»
        ///   ShowDimensionValue = false  the value is not printed
        ///
        /// So two reads are enough, and they also catch the case the entry
        /// feared most: the overwritten number that happens to match the
        /// model stays flagged, because the overwrite is looked at, not the
        /// comparison of the values.
        /// </summary>
        private static int? DimensionsByNumberNotFromTheModel(IView v, List<Dictionary<string, object>> details)
        {
            int n = 0;
            try
            {
                var q = v.GetFirstDisplayDimension5() as IDisplayDimension;
                while (q != null)
                {
                    bool overwritten = q.GetOverride();
                    bool off = !q.ShowDimensionValue;
                    if (overwritten || off)
                    {
                        n++;
                        if (details.Count < 10)
                        {
                            var dim = q.GetDimension2(0);
                            var entry = new Dictionary<string, object>();
                            try { entry["dimension"] = dim.FullName; } catch { }
                            try { entry["view"] = v.Name; } catch { }
                            try { entry["of_model_mm"] = Math.Round(ApiHelpers.ReadDimensionInConfiguration(dim, "") * 1000.0, 3); } catch { }
                            if (overwritten)
                                entry["how"] = "overwritten number: prints " +
                                    Math.Round(q.GetOverrideValue() * 1000.0, 3).ToString(System.Globalization.CultureInfo.InvariantCulture) + " mm";
                            else
                            {
                                string text = "";
                                try { text = (q.GetText(1) ?? "") + (q.GetText(2) ?? ""); } catch { }
                                entry["how"] = "value turned off" + (text.Length > 0 ? ": in its place «" + text + "»" : "");
                            }
                            details.Add(entry);
                        }
                    }
                    q = q.GetNext5() as IDisplayDimension;
                }
            }
            catch { return null; }
            return n;
        }

        /// <summary>
        /// A count read from a view, or null if the call does not answer
        /// (entry Z-C8). The null is carried along into the sums: `int?` added to
        /// null gives null, and that is exactly what is wanted.
        /// </summary>
        private static int? ReadValue(Func<int> read)
        {
            try { return read(); }
            catch { return null; }
        }

        private static Dictionary<string, object> Six(
            int? gtol, int? datum, int? weldments, int? roughness, int? note, int? all)
        {
            return new Dictionary<string, object>
            {
                ["gtol"] = gtol,
                ["datum"] = datum,
                ["welds"] = weldments,
                ["surface_finish"] = roughness,
                ["note"] = note,
                ["all"] = all,
            };
        }

        /// <summary>The six numbers read from a view, each on its own.</summary>
        private static Dictionary<string, object> Annotations(IView v)
        {
            return Six(
                ReadValue(() => v.GetGTolCount()), ReadValue(() => v.GetDatumTagCount()),
                ReadValue(() => v.GetWeldSymbolCount()), ReadValue(() => v.GetSFSymbolCount()),
                ReadValue(() => v.GetNoteCount()), ReadValue(() => v.GetAnnotationCount()));
        }

        private static double[] SheetPropertyOf(IDrawingDoc drawing)
        {
            try
            {
                var sheet = drawing.GetCurrentSheet() as ISheet;
                var p = sheet?.GetProperties2() as double[];
                return (p != null && p.Length >= 7) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// What a sheet that is not active says instead of its views and its paper
        /// (round 12 of 8cd843, observation 3). Pure, so the contract suite reads it.
        /// </summary>
        internal static string NotActiveSheetNote(string sheetName)
        {
            return "Not read: only the active sheet is read, because changing sheet to read it would change the " +
                   "drawing. «views» and «views_total» are those of the active sheet; «activate_sheet» with «" +
                   (sheetName ?? "") + "», then «inspect_part», to read this one.";
        }

        /// <summary>«1:2» instead of «0.5»: it is how it is written on a drawing.</summary>
        private static string FormatScale(double numerator, double denominator)
        {
            try
            {
                return RoundOff(numerator) + ":" + RoundOff(denominator);
            }
            catch { return null; }
        }

        private static string RoundOff(double x)
        {
            double r = Math.Round(x, 4);
            return r == Math.Floor(r)
                ? ((long)r).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : r.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static object[] ViewPosition(IView v)
        {
            try
            {
                var p = v.Position as double[];
                if (p == null || p.Length < 2) return null;
                return new object[]
                {
                    Math.Round(Units.Mm(p[0]), 2),
                    Math.Round(Units.Mm(p[1]), 2),
                };
            }
            catch { return null; }
        }

        private static string ViewTypeName(int typ)
        {
            switch (typ)
            {
                case (int)swDrawingViewTypes_e.swDrawingSheet: return "sheet";
                case (int)swDrawingViewTypes_e.swDrawingProjectedView: return "projected";
                case (int)swDrawingViewTypes_e.swDrawingSectionView: return "section";
                case (int)swDrawingViewTypes_e.swDrawingDetailView: return "detail";
                case (int)swDrawingViewTypes_e.swDrawingNamedView: return "named view";
                case (int)swDrawingViewTypes_e.swDrawingAuxiliaryView: return "auxiliary";
                case (int)swDrawingViewTypes_e.swDrawingAlternatePositionView: return "alternate position";
                case (int)swDrawingViewTypes_e.swDrawingRelativeView: return "relative";
                case (int)swDrawingViewTypes_e.swDrawingStandardView: return "standard";
                case (int)swDrawingViewTypes_e.swDrawingDetachedView: return "detached";
                default: return "view";
            }
        }
    }
}
