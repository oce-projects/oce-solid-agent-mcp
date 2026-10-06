using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.RoleCoverage
{
    /// <summary>
    /// HOW MANY FACES HAVE A UNIQUE ROLE? (measurement that sizes entry E7)
    ///
    /// E7 proposes naming a face by MEANING instead of by
    /// position: «the floor of the pocket made at step 2» stays the floor of the
    /// pocket whatever dimension is changed upstream.
    ///
    /// Half of the mechanism already exists and works, in `References.cs`, but
    /// it only holds INSIDE a plan: it resolves «the top face of what
    /// you made at step 1» because that step is still in hand, with its
    /// extrusion direction. For a face that ALREADY exists in the tree
    /// that direction is not there.
    ///
    /// Entry E7 expressly asks for a number before building:
    ///
    ///   «Coverage to be declared as a number: out of twenty faces cited at random in
    ///   ten test parts, how many have a unique role. If it is under half,
    ///   this is a complement, not the main road, AND IT MUST BE WRITTEN DOWN.»
    ///
    /// This tool measures that number on the open document, without changing
    /// anything. It is run on one part at a time.
    ///
    ///     Checks\RoleCoverage\bin\RoleCoverage.exe
    ///
    /// THE FOUR ROLES, and what each one needs to be decided:
    ///
    ///   hole_face     the geometry alone: is there ONE cylindrical face?
    ///   top_face    the DIRECTION of the feature, plus the geometry
    ///   bottom_face    the DIRECTION
    ///   pocket_floor     the DIRECTION
    ///
    /// Three out of four depend on the direction. This tool tries to
    /// derive it from the definition of the feature and SAYS when it does not
    /// manage to, instead of counting that face as «without role» and making
    /// the problem look geometric when it is one of missing information.
    ///
    /// It always exits 0: it is a measurement, not a check. A low number is not
    /// a failure, it is the answer.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// How much a normal must be aligned with the direction to count
        /// as «perpendicular to the cut». Same value as `References`.
        /// </summary>
        private const double Cosine = 0.99;

        private static int Main()
        {
            ISldWorks sw;
            try { sw = Marshal.GetActiveObject("SldWorks.Application") as ISldWorks; }
            catch { Console.Error.WriteLine("SolidWorks is not open."); return 0; }
            if (sw == null) { Console.Error.WriteLine("SolidWorks does not respond."); return 0; }

            var doc = sw.ActiveDoc as IModelDoc2;
            if (doc == null) { Console.Error.WriteLine("No open document."); return 0; }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                Console.Error.WriteLine("The active document is not a part.");
                return 0;
            }

            Console.WriteLine("Role coverage on: " + doc.GetTitle());
            Console.WriteLine();

            var faces = AllFaces(doc);
            Console.WriteLine("  total faces: " + faces.Count);

            // ── which feature each face belongs to ──────────────────────────
            var perFeature = new Dictionary<int, List<IFace2>>();
            var featureName = new Dictionary<int, string>();
            var direction = new Dictionary<int, double[]>();
            int withoutFeature = 0;

            foreach (var f in faces)
            {
                IFeature feat = null;
                try { feat = f.GetFeature() as IFeature; } catch { }
                if (feat == null) { withoutFeature++; continue; }

                int id;
                try { id = feat.GetID(); } catch { withoutFeature++; continue; }

                if (!perFeature.ContainsKey(id))
                {
                    perFeature[id] = new List<IFace2>();
                    try { featureName[id] = feat.Name; } catch { featureName[id] = "?"; }
                    direction[id] = DirectionOf(feat);
                }
                perFeature[id].Add(f);
            }

            Console.WriteLine("  without a readable feature: " + withoutFeature);
            Console.WriteLine("  features with faces: " + perFeature.Count);
            Console.WriteLine();

            // ── the role of each face, feature by feature ───────────────────
            int unique = 0, ambiguous = 0, withoutRole = 0, withoutDirection = 0;
            var roleCount = new Dictionary<string, int>();

            foreach (var entry in perFeature.OrderBy(v => v.Key))
            {
                var its = entry.Value;
                var dir = direction[entry.Key];
                var roleOf = new Dictionary<IFace2, string>();

                // hole_face: the only one that does NOT need the direction.
                var cylindrical = its.Where(Cylindrical).ToList();
                if (cylindrical.Count == 1) roleOf[cylindrical[0]] = "hole_face";

                if (dir != null)
                {
                    var above = End(its, dir, +1);
                    var below = End(its, dir, -1);
                    if (above != null && !roleOf.ContainsKey(above))
                        roleOf[above] = "top_face";
                    if (below != null && !roleOf.ContainsKey(below) && !ReferenceEquals(below, above))
                        roleOf[below] = "bottom_face";
                }

                int howManyHere = 0;
                foreach (var f in its)
                {
                    string r;
                    if (roleOf.TryGetValue(f, out r))
                    {
                        unique++;
                        howManyHere++;
                        roleCount[r] = (roleCount.ContainsKey(r) ? roleCount[r] : 0) + 1;
                    }
                    else if (dir == null) withoutDirection++;
                    else withoutRole++;
                }

                // MORE THAN ONE CYLINDRICAL FACE IS NOT «NO ROLE»: it is
                // an ambiguity, and in `References` it triggers a refusal
                // instead of a choice. Counting it together with the faces that have no
                // role at all would confuse two different problems:
                // one is solved by adding vocabulary, the other is not.
                if (cylindrical.Count > 1) ambiguous += cylindrical.Count;

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-34} {1,2} faces, {2} with role{3}",
                    Short(featureName[entry.Key], 34), its.Count, howManyHere,
                    dir == null ? "   [direction not readable]" : ""));
            }

            // ── the number that entry E7 asks for ──────────────────────────
            Console.WriteLine();
            Console.WriteLine("=== The number");
            int total = faces.Count;
            Console.WriteLine("  with unique role:             " + Line(unique, total));
            Console.WriteLine("  ambiguous (several cylindrical):" + Line(ambiguous, total));
            Console.WriteLine("  without role (geometry):      " + Line(withoutRole, total));
            Console.WriteLine("  without role (no direction):  " + Line(withoutDirection, total));
            Console.WriteLine();
            foreach (var r in roleCount.OrderByDescending(x => x.Value))
                Console.WriteLine("      " + r.Key.PadRight(20) + r.Value);

            Console.WriteLine();
            double dimension = total > 0 ? unique * 100.0 / total : 0;
            Console.WriteLine(dimension >= 50
                ? "  Above half: for this part the role path holds."
                : "  BELOW HALF: for this part the role is a complement, not the path.");

            return 0;
        }

        private static string Line(int howMany, int total)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0,3}  ({1,5:0.0}%)",
                howMany, total > 0 ? howMany * 100.0 / total : 0);
        }

        private static string Short(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Length <= n ? s : s.Substring(0, n - 3) + "...";
        }

        private static List<IFace2> AllFaces(IModelDoc2 doc)
        {
            var outside = new List<IFace2>();
            try
            {
                var partDoc = doc as IPartDoc;
                if (partDoc == null) return outside;
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null) return outside;
                foreach (var c in bodies)
                {
                    var body = c as IBody2;
                    if (body == null) continue;
                    var f = body.GetFirstFace() as IFace2;
                    while (f != null)
                    {
                        outside.Add(f);
                        f = f.GetNextFace() as IFace2;
                    }
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// The direction of a feature, if it can be read.
        ///
        /// IT IS THE DATUM THAT DECIDES THREE ROLES OUT OF FOUR, and inside a plan it arrives
        /// for free because the step carries it along. Here it must be rebuilt from the
        /// definition, and it is not always there: a revolve has no extrusion
        /// direction, a fillet neither.
        ///
        /// It is read from the normal of the SKETCH PLANE, which is the most
        /// stable datum: the extrusion direction is perpendicular to the profile.
        /// </summary>
        private static double[] DirectionOf(IFeature featureItem)
        {
            try
            {
                var sketches = featureItem.GetFirstSubFeature() as IFeature;
                while (sketches != null)
                {
                    var sketch = sketches.GetSpecificFeature2() as ISketch;
                    if (sketch != null)
                    {
                        var m = sketch.ModelToSketchTransform as IMathTransform;
                        // `Inverse()` answers an `object`, not an
                        // `IMathTransform`: without the conversion it does not compile, and
                        // with a wrong `as` it would return null silently.
                        var reverse = m == null ? null : m.Inverse() as IMathTransform;
                        if (reverse != null)
                        {
                            var data = reverse.ArrayData as double[];
                            // The third column of the rotation is the normal
                            // of the sketch plane.
                            if (data != null && data.Length >= 9)
                                return Normalize(new[] { data[6], data[7], data[8] });
                        }
                    }
                    sketches = sketches.GetNextSubFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        private static IFace2 End(List<IFace2> faces, double[] direction, int facing)
        {
            IFace2 choice = null;
            double best = 0;
            foreach (var f in faces)
            {
                var n = Normal(f);
                if (n == null) continue;
                if (Math.Abs(Product(n, direction)) <= Cosine) continue;
                double[] box;
                try { box = f.GetBox() as double[]; } catch { continue; }
                if (box == null || box.Length < 6) continue;
                var center = new[]
                {
                    (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2,
                };
                double projection = Product(center, direction) * facing;
                if (choice == null || projection > best) { choice = f; best = projection; }
            }
            return choice;
        }

        private static bool Cylindrical(IFace2 f)
        {
            try
            {
                var s = f.IGetSurface();
                return s != null && s.IsCylinder();
            }
            catch { return false; }
        }

        private static double[] Normal(IFace2 f)
        {
            try
            {
                var s = f.IGetSurface();
                if (s == null || !s.IsPlane()) return null;
                var p = s.PlaneParams as double[];
                return p != null && p.Length >= 3 ? Normalize(new[] { p[0], p[1], p[2] }) : null;
            }
            catch { return null; }
        }

        private static double[] Normalize(double[] v)
        {
            double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (n < 1e-12) return null;
            return new[] { v[0] / n, v[1] / n, v[2] / n };
        }

        private static double Product(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }
    }
}
