using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
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
        /// reflection on the interop 29.5.1.1, see StatoDocumento.Estremi).
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
        /// The obsolete array form of the edge parameters, for the one call site
        /// that still reads it by position (OperazioniTesto, sketch-text
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
        /// (StatoDocumento, OperazioniMisura); it now lives here once.
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
        /// The outward normal of a PLANAR face, as SOLIDWORKS gives it (not
        /// normalised), or null if the face is not planar.
        ///
        /// Guide: IFace2~Normal.html, Remarks: «If the face is not a planar
        /// face, then this property returns 0,0,0.» A zero vector gives a zero
        /// cosine, exactly like a truly perpendicular wall: the audit found it
        /// letting the tangent edge of a fillet pass as a sharp edge (C2-1).
        /// Guide: ISurface~IsPlane.html for the test.
        ///
        /// IFace2.Normal is already the OUTWARD normal of the face (it follows
        /// the sense in which the face uses its surface); the surface's own
        /// PlaneParams would point inwards for half the faces of a solid
        /// (Riferimenti.Normale, where this helper comes from).
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
