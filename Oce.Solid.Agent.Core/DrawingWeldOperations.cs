using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE WELD SYMBOL ON THE DRAWING (entry GT7).
    ///
    /// ── MEASURED BEFORE WRITING IT, 26/9/2026 (`probe0089 saldtav`) ────────
    ///
    /// `IDrawingDoc.InsertWeldSymbol(Dim1, Symbol, Dim2, Symmetric, FieldWeld,
    /// ShowOtherSide, DashOnTop, Peripheral, HasProcess, ProcessValue)` returns
    /// `Void` and works on the edge SELECTED in the view: the symbol is born
    /// with the arrow on the edge and the ISO dashed line below.
    ///
    /// **The symbol code is the one of the `gtol.sym` library**,
    /// `<SECTION-ITEM>`: the ISO fillet weld is `<WELD-FILL>` (section
    /// `#WELD`, entry `*FILL`). The first probe had tried `<IGWS-FILLET>`,
    /// `<GWS-FILLET>` and «Fillet»: the symbol WAS BORN all the same, with that text
    /// read back equal — and without a triangle. Only the PNG said so. With
    /// `<WELD-FILL>` the triangle is there, and `GetTextCount` goes from 1 to 2: the
    /// recognised code is one more text (the drawn symbol).
    ///
    /// The edge is not chosen by the caller: it is the one, among the visible edges
    /// of the view, where one body touches the other — an edge of a body whose
    /// ends and midpoint lie on a face of ANOTHER body. The longest.
    /// A view without visible joints is refused: a symbol attached to
    /// any edge would send the welder to the wrong place.
    /// </summary>
    public partial class SwOperations
    {
        private static readonly Dictionary<string, string> WeldSymbols = new Dictionary<string, string>
        {
            ["fillet"] = "<WELD-FILL>",
            ["v"] = "<WELD-BUSV>",
            ["half_v"] = "<WELD-BUSB>",
            ["butt"] = "<WELD-BUSQ>",
        };

        private Outcome DrawingWeldSymbol(IModelDoc2 model, Dictionary<string, object> a)
        {
            string reason;
            IDrawingDoc drawing;
            var view = ViewToAnnotate(model, a, out drawing, out reason);
            if (view == null) return Failed(reason);

            string typ = Str(a, "variant", "fillet");
            string symbol;
            if (!WeldSymbols.TryGetValue(typ, out symbol))
                return Failed("«variant» is one of " + string.Join(", ", WeldSymbols.Keys) + ", not «" + typ + "».");
            double throat;
            if (!RequiredNum(a, "throat_mm", out throat, out reason)) return Failed(reason);
            if (throat <= 0 || throat > 50)
                return Failed("«throat_mm» (the throat of the bead) is between 0 and 50 mm, not " +
                               throat.ToString("0.###", CultureInfo.InvariantCulture) + ".");
            double length = Num(a, "length_mm", 0);
            if (length < 0) return Failed("«length_mm» cannot be negative.");
            bool everythingAround = Bool(a, "all_around", false);
            bool inOpera = Bool(a, "field_weld", false);
            bool twoSides = Bool(a, "both_sides", false);
            string annotationSide = Str(a, "side", "top");

            // ── THE VISIBLE JOINT ───────────────────────────────────────────
            var part = view.ReferencedDocument as IModelDoc2;
            var partDoc = part as IPartDoc;
            if (partDoc == null)
                return Failed("Behind the view there is no part: the weld symbol goes on a joint between the " +
                               "bodies of a weldment.");
            object[] bodies = null;
            try { bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[]; } catch { }
            if (bodies == null)
                return Failed("I cannot read the bodies of the part behind the view: without them, I cannot tell where the " +
                               "joint is. Look at the part and retry.");
            if (bodies.Length < 2)
                return Failed("The part behind the view has " + bodies.Length + " bodies: a " +
                               "welded joint is between TWO bodies, and a symbol on a single body does not indicate any weld.");
            IEdge joint; double alongJoint;
            if (!VisibleJoint(view, bodies, out joint, out alongJoint))
                return Failed("In the view «" + (view.GetName2() ?? "") + "» an edge where one body touches the other is " +
                               "NOT visible: the symbol goes on the line of the joint. Try the view in which the joint is seen " +
                               "from the side.");

            var side = SideEdge(view, annotationSide, out reason);
            if (side == null) return Failed(reason);
            double occupied = OccupiedBand(view, side);

            int before = DrawingWeldSymbols(drawing);
            if (!SelectInView(model, view, joint))
                return Failed("I cannot select the edge of the joint in the view.");
            string dim1 = "a" + throat.ToString("0.##", CultureInfo.InvariantCulture);
            string dim2 = length > 0 ? length.ToString("0.##", CultureInfo.InvariantCulture) : "";
            try { drawing.InsertWeldSymbol(dim1, symbol, dim2, twoSides, inOpera, false, false, everythingAround, false, ""); }
            catch (Exception ex) { model.ClearSelection2(true); return Failed("The weld symbol was not created: " + ex.Message); }
            model.ClearSelection2(true);
            int after = DrawingWeldSymbols(drawing);
            IWeldSymbol ws = LastWeldSymbol(drawing, view);
            // THE NEW SYMBOL IS COUNTED: without the two counts read, the last
            // symbol of the view could be one that was already there (and the «wrong»
            // branch would remove it). Review of 26/9/2026.
            if (before < 0 || after < 0)
                return Failed("I cannot count the weld symbols of the drawing (before " + before + ", after " +
                               after + "): without that, I do not know whether the requested one was created. Look at the drawing.");
            if (ws == null || after != before + 1)
                return Failed("SOLIDWORKS did not create the weld symbol and did not say why (symbols in the " +
                               "drawing: " + before + " before, " + after + " after).");

            // ── READ BACK ───────────────────────────────────────────────────
            string readSymbol = null, readLeft = null, readRight = null;
            int texts = -1, arrows = -1, attached = -1;
            bool readPerimeter = false; int readField = -1, readSymmetric = -1;
            try { readSymbol = ws.GetText((int)swWeldSymbolTextTypes_e.swWeldSymbolTextAbove); } catch { }
            try { readLeft = ws.GetText((int)swWeldSymbolTextTypes_e.swWeldLeftTextAbove); } catch { }
            try { readRight = ws.GetText((int)swWeldSymbolTextTypes_e.swWeldRightTextAbove); } catch { }
            try { texts = ws.GetTextCount(); } catch { }
            try { arrows = ws.GetLeaderCount(); } catch { }
            try { readPerimeter = ws.GetPeripheral(); } catch { }
            try { readField = ws.GetFieldWeld(); } catch { }
            try { readSymmetric = ws.GetSymmetric(); } catch { }
            var an = ws.GetAnnotation() as IAnnotation;
            try { attached = an.GetAttachedEntityCount3(); } catch { }
            var pos = Move(an, drawing, view, side, Math.Max(15, occupied + 12), 20, 25, 10);

            var wrong = new List<string>();
            if (!string.Equals((readSymbol ?? "").Trim(), symbol, StringComparison.OrdinalIgnoreCase))
                wrong.Add("the symbol says «" + readSymbol + "» instead of «" + symbol + "»");
            if (!string.Equals((readLeft ?? "").Trim(), dim1, StringComparison.OrdinalIgnoreCase))
                wrong.Add("the throat says «" + readLeft + "» instead of «" + dim1 + "»");
            if (dim2.Length > 0 && !string.Equals((readRight ?? "").Trim(), dim2, StringComparison.OrdinalIgnoreCase))
                wrong.Add("the length says «" + readRight + "» instead of «" + dim2 + "»");
            if (everythingAround != readPerimeter) wrong.Add("«all around» is not as requested");
            bool fieldOk = readField == (int)swWeldSymbolField_e.swFieldWeldUp || readField == (int)swWeldSymbolField_e.swFieldWeldDown;
            if (readField < 0) wrong.Add("«field weld» cannot be read back");
            else if (inOpera != fieldOk) wrong.Add("«field weld» is not as requested");
            if (readSymmetric < 0 && twoSides) wrong.Add("«both sides» cannot be read back");
            else if (twoSides != (readSymmetric == (int)swWeldSymbolSymmetric_e.swWeldSymmetric)) wrong.Add("«both sides» is not as requested");
            if (attached == 0) wrong.Add("it is not attached to any edge");
            if (attached < 0) wrong.Add("what it is attached to cannot be read back");
            string textsWrong = WeldSymbolTextsWrong(texts);
            if (textsWrong != null) wrong.Add(textsWrong);
            if (wrong.Count > 0)
            {
                // A WRONG SYMBOL IS NOT LEFT ON THE DRAWING: it gets printed and sends the
                // welder to make another weld. Removed through
                // RemoveNewAnnotation, next to the tolerance frame: «removed» is
                // said only when the weld symbols of the drawing, counted again,
                // are back to the count before the insertion (Z-C21.8, M38).
                bool removed = RemoveNewAnnotation(model, an, () => DrawingWeldSymbols(drawing), before);
                return Failed("The weld symbol was created but " + string.Join("; ", wrong) +
                               (removed ? ": I removed it." : ": remove it — " + Sentences.HowToWithdrawIt + "."));
            }

            return WithoutFeature(new Dictionary<string, object>
            {
                ["kind"] = typ,
                ["symbol"] = symbol,
                ["symbol_reread"] = readSymbol,
                ["throat_reread"] = readLeft,
                ["length_reread"] = string.IsNullOrEmpty(readRight) ? null : readRight,
                ["all_around"] = readPerimeter,
                ["field_weld"] = readField == (int)swWeldSymbolField_e.swFieldWeldUp || readField == (int)swWeldSymbolField_e.swFieldWeldDown,
                ["both_sides"] = readSymmetric == (int)swWeldSymbolSymmetric_e.swWeldSymmetric,
                ["texts"] = NumberOrNull(texts),
                ["arrows"] = NumberOrNull(arrows),
                ["attached_to_edge"] = attached > 0,
                ["joint_mm"] = Math.Round(alongJoint, 2),
                ["symbols_before"] = NumberOrNull(before),
                ["symbols_after"] = NumberOrNull(after),
                ["position_mm"] = pos,
            });
        }

        /// <summary>
        /// What the text count of a new weld symbol says is wrong with it, or
        /// null when the symbol was drawn (Z-C21.8, finding L16-1).
        ///
        /// The discriminant is the one measured when the operation was written
        /// (26/9/2026, see the header): with a code the gtol.sym library does
        /// not know, the symbol is born all the same, its code reads back equal,
        /// and nothing is drawn — IWeldSymbol.GetTextCount gives 1, the throat
        /// alone. With a known code the drawn symbol is one text more, 2. The
        /// bench case of this operation asserts that count, but until library
        /// 0.144.0 the operation itself never looked at it, so a
        /// symbol with no triangle was answered as done. A count that cannot be
        /// read is wrong too, as an attached count that cannot be read is: the
        /// one check that sees the missing symbol did not run.
        /// </summary>
        internal static string WeldSymbolTextsWrong(int texts)
        {
            if (texts < 0)
                return "its texts cannot be read back (IWeldSymbol.GetTextCount), so I cannot tell whether the " +
                       "symbol was drawn";
            if (texts < 2)
                return "it has " + texts + (texts == 1 ? " text" : " texts") + " where a drawn symbol makes 2: " +
                       "SOLIDWORKS kept the code as text and drew no symbol";
            return null;
        }

        /// <summary>
        /// The longest visible edge where one body touches the other: start,
        /// midpoint and end on a face of ANOTHER body, within a thousandth of a mm.
        /// </summary>
        private static bool VisibleJoint(IView view, object[] bodies, out IEdge joint, out double lengthMm)
        {
            joint = null; lengthMm = 0;
            object[] edges = null;
            try { edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            foreach (var o in edges ?? new object[0])
            {
                try
                {
                    var e = o as IEdge;
                    var its = e?.GetBody() as IBody2;
                    var p = ApiHelpers.EdgeParameters(e, out ICurve curva);
                    if (its == null || curva == null || p == null) continue;
                    double u0 = p.UMinValue, u1 = p.UMaxValue;
                    var points = new List<double[]>();
                    foreach (double t in new[] { u0, (u0 + u1) / 2, u1 })
                    {
                        var v = curva.Evaluate2(t, 0) as double[];
                        if (v != null) points.Add(new[] { v[0], v[1], v[2] });
                    }
                    if (points.Count < 3) continue;
                    double l = curva.GetLength3(u0, u1) * 1000.0;
                    if (l <= lengthMm) continue;
                    foreach (var oc in bodies)
                    {
                        var other = oc as IBody2;
                        if (other == null || other.Name == its.Name) continue;
                        bool sta = false;
                        foreach (var of in (other.GetFaces() as object[]) ?? new object[0])
                        {
                            var f = of as IFace2;
                            if (f == null) continue;
                            bool all = true;
                            foreach (var q in points)
                            {
                                var c = f.GetClosestPointOn(q[0], q[1], q[2]) as double[];
                                if (c == null || Math.Sqrt(Math.Pow(c[0] - q[0], 2) + Math.Pow(c[1] - q[1], 2) + Math.Pow(c[2] - q[2], 2)) > 1e-6)
                                { all = false; break; }
                            }
                            if (all) { sta = true; break; }
                        }
                        if (sta) { joint = e; lengthMm = l; break; }
                    }
                }
                catch { }
            }
            return joint != null;
        }

        /// <summary>How many weld symbols the drawing has, view by view; -1 if it cannot be read.</summary>
        private static int DrawingWeldSymbols(IDrawingDoc drawing)
        {
            try
            {
                int n = 0;
                var v = drawing.GetFirstView() as IView;
                while (v != null) { n += v.GetWeldSymbolCount(); v = v.GetNextView() as IView; }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// The last weld symbol of the view (the one just put); if the
        /// view has none, the last of the whole drawing, sheet included — the
        /// probe found them by searching all the views, and where SOLIDWORKS
        /// registers it is not measured case by case. Null if there is none.
        /// </summary>
        private static IWeldSymbol LastWeldSymbol(IDrawingDoc drawing, IView view)
        {
            try
            {
                var itsItems = view.GetWeldSymbols() as object[];
                if (itsItems != null && itsItems.Length > 0) return itsItems[itsItems.Length - 1] as IWeldSymbol;
                IWeldSymbol last = null;
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    var o = v.GetWeldSymbols() as object[];
                    if (o != null && o.Length > 0) last = o[o.Length - 1] as IWeldSymbol;
                    v = v.GetNextView() as IView;
                }
                return last;
            }
            catch { return null; }
        }
    }
}
