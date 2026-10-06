using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// The geometric half of <see cref="ApiHelpers"/>: edges (S12), boxes (S6)
    /// and normals (S17). See ApiHelpers.cs for the rules every helper follows.
    /// </summary>
    internal static partial class ApiHelpers
    {
        // =====================================================================
        //  S12 — EDGE PARAMETERS
        // =====================================================================

        /// <summary>
        /// The curve parameters of an edge (start and end points, parameter
        /// range), or null for a null edge.
        ///
        /// Guide: IEdge~GetCurveParams3.html, Remarks: «Before calling this
        /// method, you must call IEdge::GetCurve or IEdge::IGetCurve to generate
        /// the underlying curve information.» Six call sites skipped GetCurve
        /// and read whatever SOLIDWORKS had cached. Here GetCurve always comes
        /// first, so nobody has to remember it.
        ///
        /// GetCurveParams3 returns ICurveParamData, not a double[] (checked by
        /// reflection on the interop 29.5.1.1, see the extreme-point reader of DocumentState).
        /// </summary>
        internal static ICurveParamData EdgeParameters(IEdge edge)
        {
            return EdgeParameters(edge, out _);
        }

        /// <summary>
        /// As <see cref="EdgeParameters(IEdge)"/>, handing back the curve that
        /// had to be generated anyway (IEdge~GetCurve.html: «Gets the underlying
        /// curve for this edge»).
        /// </summary>
        internal static ICurveParamData EdgeParameters(IEdge edge, out ICurve curve)
        {
            curve = null;
            if (edge == null) return null;
            curve = edge.GetCurve() as ICurve;
            return edge.GetCurveParams3() as ICurveParamData;
        }

        /// <summary>
        /// The parameter range of an edge ON ITS CURVE, the one whose evaluation lands on
        /// the edge: false when neither candidate does.
        ///
        /// WHY IT IS NOT JUST UMinValue..UMaxValue (found 6/10/2026, D9 defect 2, probe
        /// Checks\D9Defects, subcommand gusset). Guide: ICurveParamData~UMinValue.html,
        /// Remarks: when the curve and the edge run in opposite directions the
        /// parameters are in NEGATIVE parameter space («if Sense is false, minimum U
        /// parameter is 10, and maximum U parameter is 5, then UMinValue = -10 and
        /// UMaxValue = -5. To normalize the values in positive parameter space, swap the
        /// two U parameter values and then negate them»). Measured on the 150 mm foot of a
        /// tube stand: the top edge of the wall of one foot answered UMin 0, UMax 0.032 and
        /// its curve evaluated on the edge; the same edge of the foot at the other end of
        /// the bar answered UMin -0.032, UMax 0 (with Sense reading True, against the
        /// page) and ICurve~Evaluate2 at those values landed 32 mm OUTSIDE the edge.
        /// Three points evaluated there, tested against another face, said «not on it»
        /// for an edge that lies on it.
        ///
        /// So the two candidates are tried, (UMin, UMax) and the normalized (-UMax, -UMin),
        /// and the one whose two ends evaluate CLOSER to ICurveParamData.StartPoint and
        /// EndPoint (in either order: the page says which is which only when Sense is right,
        /// and here it was not) is the range. A circle whose ends are one point matches both
        /// candidates, and the first is as good as the second.
        ///
        /// THE CLOSER ONE, NOT THE ONE WITHIN A MICRON (review of 6/10/2026). The first
        /// version took a candidate only when both ends landed within 1e-6 m and refused
        /// the edge otherwise. That changed edges the defect never touched: a spline or an
        /// intersection curve whose ends evaluate a few microns from its vertices (the
        /// tolerance of the body, not a wrong range) went from «evaluated as before» to
        /// «could not be read», and LengthThatLiesOn (gusset, weld_bead, face_fillet) from
        /// a length to -1. Now the first candidate wins whenever it is at least as close
        /// as the second, so every edge whose range was right reads exactly as before; the
        /// second wins only when it is strictly closer; and an edge is refused only when
        /// even the closer candidate misses its ends by more than 0.01 mm.
        /// </summary>
        internal static bool EdgeRangeOnCurve(ICurveParamData parameters, ICurve curve, out double u0, out double u1)
        {
            u0 = 0; u1 = 0;
            if (parameters == null || curve == null) return false;
            var start = parameters.StartPoint as double[];
            var end = parameters.EndPoint as double[];
            if (start == null || end == null || start.Length < 3 || end.Length < 3) return false;
            var candidates = new[]
            {
                new[] { parameters.UMinValue, parameters.UMaxValue },
                new[] { -parameters.UMaxValue, -parameters.UMinValue },
            };
            double best = double.MaxValue;
            foreach (var c in candidates)
            {
                var a = curve.Evaluate2(c[0], 0) as double[];
                var b = curve.Evaluate2(c[1], 0) as double[];
                if (a == null || b == null || a.Length < 3 || b.Length < 3) continue;
                // The worse of the two ends, in the better of the two orders.
                double miss = Math.Min(Math.Max(Distance(a, start), Distance(b, end)),
                                       Math.Max(Distance(a, end), Distance(b, start)));
                if (miss < best) { best = miss; u0 = c[0]; u1 = c[1]; }
            }
            return best <= 1e-5;
        }

        private static double Distance(double[] p, double[] q)
        {
            double dx = p[0] - q[0], dy = p[1] - q[1], dz = p[2] - q[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// The obsolete array form of the edge parameters, for the one call site
        /// that still reads it by position (TextOperations, sketch-text
        /// outline).
        ///
        /// Guide: IEdge~GetCurveParams2.html, «Obsolete. Superseded by
        /// IEdge::GetCurveParams3»; Remarks: «Calls to this method must be
        /// preceded by a call to IEdge::GetCurve». Moving that site to
        /// <see cref="EdgeParameters(IEdge)"/> is a migration (the positional
        /// array also carries the sense flag, see the same Remarks), so it waits
        /// for Z-C21.7; here it at least gets its GetCurve.
        /// </summary>
        internal static double[] EdgeParameterArray(IEdge edge)
        {
            if (edge == null) return null;
            edge.GetCurve();
            return edge.GetCurveParams2() as double[];
        }

        // =====================================================================
        //  S6 — BOUNDING BOXES
        // =====================================================================

        private static readonly double[][] SixDirections =
        {
            new double[] { -1, 0, 0 }, new double[] { 0, -1, 0 }, new double[] { 0, 0, -1 },
            new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 },
        };

        /// <summary>
        /// The TRUE box of a body, in metres: {minX, minY, minZ, maxX, maxY,
        /// maxZ}. Null if GetExtremePoint does not answer in one of the six
        /// directions.
        ///
        /// Guide: IBody2~GetExtremePoint.html, Remarks: «This method returns the
        /// furthest possible point of intersection between a plane normal to the
        /// direction vector specified and the model as the plane moves along the
        /// direction vector.» Six directions give the exact axis-aligned box.
        ///
        /// NOT GetBodyBox: IBody2~GetBodyBox.html, «IMPORTANT: The values
        /// returned are approximate and should not be used for comparison or
        /// calculation purposes. Furthermore, the bounding box may vary after
        /// rebuilding the model.» The same choice was already written twice
        /// (DocumentState, MeasureOperations); it now lives here once.
        /// </summary>
        internal static double[] TrueBoundingBox(IBody2 body)
        {
            if (body == null) return null;
            var box = new double[6];
            for (int i = 0; i < 6; i++)
            {
                var d = SixDirections[i];
                if (!body.GetExtremePoint(d[0], d[1], d[2], out double x, out double y, out double z))
                    return null;
                double[] p = { x, y, z };
                box[i] = p[i % 3];
            }
            return box;
        }

        /// <summary>
        /// The TRUE box of a face, in metres, or null if it cannot be measured.
        ///
        /// IFace2 has no GetExtremePoint, so the face is turned into a temporary
        /// sheet body and measured as a body. Guide: IFace2~CreateSheetBody.html,
        /// «Creates a sheet body from this face ... A sheet body is a surface
        /// body»; then <see cref="TrueBoundingBox(IBody2)"/>. The temporary body
        /// is not added to the document.
        ///
        /// NOT IFace2.GetBox: IFace2~GetBox.html, «IMPORTANT: The values returned
        /// are approximate and should not be used for comparison or calculation
        /// purposes ... it might not be the tightest box.»
        ///
        /// No silent fallback to GetBox: a caller that gets null knows it has no
        /// measure, while an approximate box handed over as the true one is the
        /// defect this helper exists to remove. First used in Z-C21.6: the bank
        /// is what proves CreateSheetBody answers from outside the process.
        /// </summary>
        internal static double[] TrueBoundingBox(IFace2 face)
        {
            if (face == null) return null;
            var sheet = face.CreateSheetBody() as IBody2;
            return TrueBoundingBox(sheet);
        }

        /// <summary>
        /// The APPROXIMATE box of a body (GetBodyBox), in metres. Only where a
        /// comment at the call site says why the approximation is what is wanted
        /// (a ceiling, a permissive pre-check, two measures that must share the
        /// same approximation). Guide: IBody2~GetBodyBox.html, «The box
        /// dimensions returned by SOLIDWORKS enclose the body and are typically
        /// close to the minimum possible size (this is typical, but not always
        /// true)» — it encloses, so it is never smaller than the body.
        /// </summary>
        internal static double[] ApproximateBoundingBox(IBody2 body)
        {
            var box = body?.GetBodyBox() as double[];
            return box == null || box.Length < 6 ? null : box;
        }

        /// <summary>
        /// The union of boxes computed by <paramref name="measure"/>, in metres;
        /// null if no box could be read. Bodies whose box is null are skipped,
        /// as the call sites did before.
        /// </summary>
        internal static double[] UnionOfBoxes<T>(IEnumerable<T> items, Func<T, double[]> measure)
        {
            double[] union = null;
            if (items == null) return null;
            foreach (var item in items)
            {
                var b = measure(item);
                if (b == null || b.Length < 6) continue;
                if (union == null) { union = (double[])b.Clone(); continue; }
                for (int i = 0; i < 3; i++) union[i] = Math.Min(union[i], b[i]);
                for (int i = 3; i < 6; i++) union[i] = Math.Max(union[i], b[i]);
            }
            return union;
        }

        // =====================================================================
        //  S17 — NORMAL OF A FACE
        // =====================================================================

        /// <summary>
        /// The outward UNIT normal of a PLANAR face, or null if the face is not
        /// planar. This helper does not re-normalise it: SOLIDWORKS gives it
        /// already of length 1, IFace2~Normal.html, «Gets the unit normal vector
        /// for any planar face» (Z-C21.9, finding N71: this line said «not
        /// normalised» until library 0.144.0). References.Normal normalises it
        /// again, a harmless no-op kept as a defence.
        ///
        /// Guide: IFace2~Normal.html, Remarks: «If the face is not a planar
        /// face, then this property returns 0,0,0.» A zero vector gives a zero
        /// cosine, exactly like a truly perpendicular wall: the audit found it
        /// letting the tangent edge of a fillet pass as a sharp edge (C2-1).
        /// Guide: ISurface~IsPlane.html for the test.
        ///
        /// OUTWARD BY THE GUIDE, NOT ONLY BY MEASURE (Z-C21.9, finding L10-7):
        /// IFace2~FaceInSurfaceSense.html, Remarks, «the underlying surface
        /// geometry can have an orientation where its normal vector points
        /// toward or away from the body material. The normal vector of faces,
        /// however, are directed away from the body material». So IFace2.Normal
        /// is the OUTWARD normal of the face, while the surface's own PlaneParams
        /// would point inwards for half the faces of a solid (References.Normal,
        /// where this helper comes from; OverhangOperations measured IFace2.Normal
        /// leaving the material on six faces out of six, 16/9/2026).
        /// </summary>
        internal static double[] NormalIfPlanar(IFace2 face)
        {
            if (face == null) return null;
            var surface = face.GetSurface() as ISurface;
            if (surface == null || !surface.IsPlane()) return null;
            return face.Normal as double[];
        }
    }
}
