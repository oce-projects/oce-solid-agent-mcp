using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHERE A COMPONENT GOES, AND HOW IT IS TURNED (brief R12).
    ///
    /// On 25/10/2026 an assembly of three parts could not be put together:
    /// «insert_component» places the centre of the box with no rotation, and
    /// no operation moved or turned a component afterwards. The half that had
    /// to sit upside down on the other could only be mated by direction, and a
    /// mate by direction cannot turn a part over.
    ///
    /// «place_component» sets the placement of one component: where its origin
    /// goes («origin_mm», the same field the state answers) and how its axes are
    /// turned («rotation», turns about the axes of the ASSEMBLY, applied in order
    /// to the part's own axes). What is not sent stays as it is.
    ///
    /// THE LAYOUT OF THE NUMBERS IS THE MEASURED ONE, not a new guess: the
    /// direction test of Z-C14 (MateOperations.MovedForTest, probe of 30/9/2026)
    /// already turns components through IComponent2.Transform2, whose
    /// ArrayData holds the rotation BY ROWS — row i is the axis i of the
    /// component in the assembly — and the translation in [9..11], in metres;
    /// a turn Q of the assembly makes each row r into Q r. The same reading
    /// gives ComponentAxes and InAssemblySpace (M1, N26).
    ///
    /// IT IS READ BACK: Transform2 set, a rebuild (RebuildForTest, which keeps
    /// the undo of the plan, brief R23), Transform2 read again.
    /// IComponent2~Transform2.html, Remarks: a placement that breaks the mates is
    /// taken, and the rebuild «moves your components to the closest valid
    /// location». So a component that its mates hold comes back somewhere else:
    /// that is a refusal, the component is set back where it was, and the answer
    /// says how far it was moved and how many mates it has.
    ///
    /// A FIXED COMPONENT IS REFUSED, with the road: «fix_component» frees it.
    /// A component inside a sub-assembly is refused too: setting its Transform2
    /// moves the sub-assembly (same Remarks).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>A placement read back within this distance of the one asked is the one asked (the direction test's own threshold).</summary>
        private const double PlacedWithinMm = 0.01;

        /// <summary>A placement read back within this angle of the one asked is the one asked.</summary>
        private const double PlacedWithinDeg = 0.01;

        private Outcome PlaceComponent(IModelDoc2 model, Dictionary<string, object> a)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed("This is not an assembly: a component is placed inside an assembly. Open the assembly for me.");

            object[] rawItems = null;
            try { rawItems = assemblyDoc.GetComponents(false) as object[]; } catch { }
            var components = DocumentState.ComponentsInOrder(rawItems);
            string uid = (Str(a, "component", "") ?? "").Trim();
            var component = PerUid(components, uid);
            if (component == null) return Failed(NotFound(uid, "component", components));
            string name = NameOf(component);

            // ── WHAT IS ASKED, refused before anything is touched ──
            double[] originMm = null;
            if (a.ContainsKey("origin_mm"))
            {
                originMm = Triplet(a, "origin_mm");
                if (originMm == null)
                    return Failed("«origin_mm» wants three numbers, [x, y, z] in mm of the assembly: where the origin " +
                                   "of " + uid + " goes. Send it again in that form; I touched nothing.");
            }
            string rotationProblem;
            var rotations = Rotations(a, out rotationProblem);
            if (rotationProblem != null) return Failed(rotationProblem);
            if (originMm == null && rotations == null)
                return Failed("Tell me where " + uid + " goes: «origin_mm» [x, y, z], «rotation» {\"axis\": \"x\", " +
                               "\"angle_deg\": 180}, or both. I touched nothing.");

            var topLevel = TopLevelNamesOf(assemblyDoc);
            if (topLevel == null)
                return Failed("I could not read the top-level components (GetComponents(true)), so I cannot tell whether " +
                               uid + " sits inside a sub-assembly. Try again after reading the state; I touched nothing.");
            if (!topLevel.Contains(name))
                return Failed(uid + " is inside a sub-assembly: setting its placement would move the whole sub-assembly. " +
                               "Place the sub-assembly itself, or open it and place the part there. I touched nothing.");
            if (IsFixed(component))
                return Failed(uid + " is fixed, and a fixed component does not move. Free it first with «fix_component» " +
                               "{\"component\": \"" + uid + "\", \"grounded\": false}, then place it (and fix it again if " +
                               "you want). I touched nothing.");

            IMathUtility math = null;
            try { math = _sw.GetMathUtility() as IMathUtility; } catch { }
            var before = ReadTransform(component);
            if (math == null || before == null)
                return Failed("I cannot read where " + uid + " is (" + (math == null ? "IMathUtility did not answer" :
                               "Transform2 did not answer") + "), so I cannot place it. Read the state again and try " +
                               "again; I touched nothing.");

            var asked = PlacedTransform(before, originMm, rotations);
            if (asked == null || !SetTransform(assemblyDoc, name, math, asked))
                return Failed("SOLIDWORKS did not take the new placement of " + uid + " (Transform2 could not be set). " +
                               "Read the state again and try again; I touched nothing.");
            bool rebuilt = RebuildForTest(model);
            var after = ReadTransform(ComponentByName(assemblyDoc, name));
            double offMm = DistanceMm(asked, after), offDeg = AngleBetweenDeg(asked, after);
            int mates = MatesOf(ComponentByName(assemblyDoc, name));

            if (!rebuilt || double.IsNaN(offMm) || double.IsNaN(offDeg) || offMm > PlacedWithinMm || offDeg > PlacedWithinDeg)
            {
                string why = !rebuilt
                    ? "the assembly did not rebuild after the new placement" + RebuildFalseHint + "."
                    : double.IsNaN(offMm) || double.IsNaN(offDeg)
                        ? "its placement could not be read back after the rebuild."
                        : "its mates moved it: asked origin " + MmText(OriginMm(asked)) + ", it went to " +
                          MmText(OriginMm(after)) + " (" + offMm.ToString("0.###", CultureInfo.InvariantCulture) +
                          " mm and " + offDeg.ToString("0.###", CultureInfo.InvariantCulture) + " degrees from the " +
                          "placement asked). " + uid + " has " + mates + (mates == 1 ? " mate" : " mates") +
                          ": suppress the one that holds it («suppress_mate») or place it before mating.";
                return Failed("I did not place " + uid + ": " + why + SetBackSentence(model, assemblyDoc, name, math, before));
            }

            var measurements = new Dictionary<string, object>
            {
                ["component"] = uid,
                ["origin_mm"] = OriginMm(after),
                ["axes"] = AxesValue(after),
                ["moved_mm"] = Math.Round(DistanceMm(before, after), 3),
                ["turned_deg"] = Math.Round(AngleBetweenDeg(before, after), 3),
            };
            if (mates >= 0) measurements["mates"] = mates;
            measurements["verdict"] =
                uid + " placed: origin " + MmText(OriginMm(after)) + ", axes " + AxesText(after) +
                ", read back after the rebuild.";
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>The component set back to where it was, rebuilt and read again: the sentence that says how it went.</summary>
        private static string SetBackSentence(IModelDoc2 model, IAssemblyDoc assemblyDoc, string name, IMathUtility math, double[] before)
        {
            if (!SetTransform(assemblyDoc, name, math, before) || !RebuildForTest(model))
                return " I could not set it back where it was: read the state again before the next step.";
            var back = ReadTransform(ComponentByName(assemblyDoc, name));
            double mm = DistanceMm(before, back), deg = AngleBetweenDeg(before, back);
            if (!double.IsNaN(mm) && !double.IsNaN(deg) && mm <= PlacedWithinMm && deg <= PlacedWithinDeg)
                return " I set it back where it was.";
            return " I set it back, and it sits " + (double.IsNaN(mm) ? "at an unreadable distance" :
                   mm.ToString("0.###", CultureInfo.InvariantCulture) + " mm and " +
                   deg.ToString("0.###", CultureInfo.InvariantCulture) + " degrees") +
                   " from where it was: read the state again before the next step.";
        }

        /// <summary>
        /// The turns of «rotation»: one object {axis, angle_deg} or a list of them,
        /// as {axis index 0..2, degrees}. Null with no problem when the argument is
        /// absent; null with the sentence of the refusal when it is malformed.
        /// </summary>
        private static List<double[]> Rotations(Dictionary<string, object> a, out string problem)
        {
            problem = null;
            object raw;
            if (a == null || !a.TryGetValue("rotation", out raw) || raw == null) return null;
            const string form =
                "Each «rotation» is {\"axis\": \"x\", \"y\" or \"z\" (an axis of the assembly), \"angle_deg\": a " +
                "number}, or a list of them applied in order. Send it again in that form; I touched nothing.";
            var items = new List<object>();
            if (raw is Dictionary<string, object>) items.Add(raw);
            else if (!(raw is string) && raw is System.Collections.IEnumerable list)
                foreach (var x in list) items.Add(x);
            else { problem = form; return null; }
            if (items.Count == 0) { problem = "«rotation» is an empty list. " + form; return null; }

            var turns = new List<double[]>();
            foreach (var item in items)
            {
                var r = item as Dictionary<string, object>;
                string axis = r == null ? "" : (Str(r, "axis", "") ?? "").Trim().ToLowerInvariant();
                int k = axis == "x" ? 0 : axis == "y" ? 1 : axis == "z" ? 2 : -1;
                double angle = r == null || !r.ContainsKey("angle_deg") ? double.NaN : Num(r, "angle_deg", double.NaN);
                if (k < 0 || double.IsNaN(angle) || double.IsInfinity(angle)) { problem = form; return null; }
                turns.Add(new[] { k, angle });
            }
            return turns;
        }

        // ── the pure rules of the placement: no interop type in a signature, so
        //    that Oce.Solid.Agent.ContractTests calls them by reflection ──

        /// <summary>
        /// The transform asked: <paramref name="current"/> with its translation
        /// replaced by <paramref name="originMm"/> (mm, when given) and its
        /// rotation replaced by the part's own axes turned by
        /// <paramref name="rotations"/> in order (when given). The scale and the
        /// four last numbers stay. Null when the input is not a readable transform.
        /// </summary>
        internal static double[] PlacedTransform(double[] current, double[] originMm, IList<double[]> rotations)
        {
            if (!ReadableTransform(current)) return null;
            var placed = (double[])current.Clone();
            if (rotations != null)
            {
                var rows = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
                foreach (double[] turn in rotations)
                {
                    rows = turn == null || turn.Length < 2 ? null : TurnedRows(rows, (int)turn[0], turn[1]);
                    if (rows == null) return null;
                }
                for (int i = 0; i < 9; i++) placed[i] = rows[i];
            }
            if (originMm != null)
            {
                if (originMm.Length < 3) return null;
                for (int k = 0; k < 3; k++) placed[9 + k] = originMm[k] / 1000.0;
            }
            return placed;
        }

        /// <summary>
        /// The nine rotation numbers (by rows, row i = axis i of the component)
        /// turned by <paramref name="angleDeg"/> about the assembly's X, Y or Z
        /// (0..2), right hand: each row r becomes Q r, the rule of MovedForTest.
        /// Values within 1e-12 of 0, 1 or -1 are snapped, so that a turn of 90 or
        /// 180 degrees gives the axes exactly. Null on a bad input.
        /// </summary>
        internal static double[] TurnedRows(double[] rows, int axis, double angleDeg)
        {
            if (rows == null || rows.Length < 9 || axis < 0 || axis > 2 || double.IsNaN(angleDeg)) return null;
            double angle = angleDeg * Math.PI / 180.0, c = Snapped(Math.Cos(angle)), s = Snapped(Math.Sin(angle));
            double[,] q = axis == 0 ? new double[,] { { 1, 0, 0 }, { 0, c, -s }, { 0, s, c } }
                        : axis == 1 ? new double[,] { { c, 0, s }, { 0, 1, 0 }, { -s, 0, c } }
                        : new double[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } };
            var turned = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double v = 0;
                    for (int k = 0; k < 3; k++) v += rows[i * 3 + k] * q[j, k];
                    turned[i * 3 + j] = Snapped(v);
                }
            return turned;
        }

        private static double Snapped(double v)
        {
            if (Math.Abs(v) < 1e-12) return 0;
            if (Math.Abs(v - 1) < 1e-12) return 1;
            if (Math.Abs(v + 1) < 1e-12) return -1;
            return v;
        }

        /// <summary>The translation of a transform in mm, rounded to 0.001; null when unreadable.</summary>
        internal static object[] OriginMm(double[] t)
        {
            if (!ReadableTransform(t)) return null;
            return new object[] { Math.Round(t[9] * 1000.0, 3), Math.Round(t[10] * 1000.0, 3), Math.Round(t[11] * 1000.0, 3) };
        }

        /// <summary>
        /// Where the component's axes point in the assembly. When each axis lies
        /// on an axis of the assembly (within 1e-6), the short form «+X -Y -Z»:
        /// the component's x points to +X of the assembly, its y to -Y, its z to
        /// -Z. Otherwise the three rows as unit vectors rounded to 0.0001. Null
        /// when unreadable.
        /// </summary>
        internal static object AxesValue(double[] t)
        {
            if (!ReadableTransform(t)) return null;
            string text = AlignedAxes(t);
            if (text != null) return text;
            var rows = new object[3];
            for (int i = 0; i < 3; i++)
                rows[i] = new object[] { Math.Round(t[i * 3], 4), Math.Round(t[i * 3 + 1], 4), Math.Round(t[i * 3 + 2], 4) };
            return rows;
        }

        /// <summary>The axes in words for a sentence: the short form, or the three rows.</summary>
        internal static string AxesText(double[] t)
        {
            if (!ReadableTransform(t)) return "unreadable";
            string text = AlignedAxes(t);
            if (text != null) return text;
            var words = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var numbers = new List<string>();
                for (int k = 0; k < 3; k++) numbers.Add(Math.Round(t[i * 3 + k], 4).ToString(CultureInfo.InvariantCulture));
                words.Add("xyz"[i] + "=(" + string.Join(", ", numbers) + ")");
            }
            return string.Join(" ", words);
        }

        private static string AlignedAxes(double[] t)
        {
            var names = new string[3];
            for (int i = 0; i < 3; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    double v = t[i * 3 + k];
                    if (Math.Abs(Math.Abs(v) - 1) < 1e-6) names[i] = (v > 0 ? "+" : "-") + "XYZ"[k];
                }
                if (names[i] == null) return null;
            }
            return string.Join(" ", names);
        }

        /// <summary>
        /// A point or a direction of the part file (metres) in the assembly, with
        /// the component's transform: v0 X' + v1 Y' + v2 Z' (+ the translation for a
        /// point), X' Y' Z' being the rows. Null on a bad input.
        /// </summary>
        internal static double[] PartToAssembly(double[] t, double[] v, bool isPoint)
        {
            if (!ReadableTransform(t) || v == null || v.Length < 3) return null;
            var r = new double[3];
            for (int j = 0; j < 3; j++)
                r[j] = (isPoint ? t[9 + j] : 0) + v[0] * t[j] + v[1] * t[3 + j] + v[2] * t[6 + j];
            return r;
        }

        /// <summary>
        /// THE SIDE A DISTANCE MATE PUT THE SECOND FACE ON (brief R21): the signed
        /// distance, in mm, from the plane of face A to a point of face B, along the
        /// outward normal of face A, everything turned into the assembly with each
        /// component's transform. Positive: B is on the side face A looks at.
        /// NaN on a bad input or a normal with no length.
        /// </summary>
        internal static double SignedGapMm(double[] transformA, double[] pointA, double[] normalA,
                                           double[] transformB, double[] pointB)
        {
            var pa = PartToAssembly(transformA, pointA, true);
            var na = PartToAssembly(transformA, normalA, false);
            var pb = PartToAssembly(transformB, pointB, true);
            if (pa == null || na == null || pb == null) return double.NaN;
            double length = Math.Sqrt(na[0] * na[0] + na[1] * na[1] + na[2] * na[2]);
            if (length < 1e-12) return double.NaN;
            return ((pb[0] - pa[0]) * na[0] + (pb[1] - pa[1]) * na[1] + (pb[2] - pa[2]) * na[2]) / length * 1000.0;
        }

        private static string MmText(object[] origin)
        {
            if (origin == null) return "(unreadable)";
            var numbers = new List<string>();
            foreach (object o in origin)
                numbers.Add(Convert.ToDouble(o, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture));
            return "[" + string.Join(", ", numbers) + "] mm";
        }
    }
}
