using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace Checks.FacesBeyondSixty
{
    /// <summary>
    /// FACES BEYOND THE 60 LARGEST, MEASURED BEFORE THE CODE CHANGES
    /// (entry W3.1-bis.12, the house method: the probe replicates the ORDER and the
    /// POINT of the reads the bridge does, and the bench case is red before and green
    /// after).
    ///
    /// The identifier of a face becomes its RANK by area among ALL the faces of the
    /// part, listed or not. That is only sound if three things are true, and each
    /// subcommand measures one on a part that is open in SOLIDWORKS (the tube stand of
    /// task 07 is the case; any part with more than 60 faces will do). The part is read
    /// by TITLE or path (GetOpenDocumentByName), never as the document in front.
    ///
    ///   FacesBeyondSixty.exe rank DOC       how many faces, how the ranking ties at the
    ///                                       cut (the 60th area and how many faces share
    ///                                       it), the faces at ranks 55..70, and whether a
    ///                                       SECOND enumeration gives the same order: the
    ///                                       uid of a face is its place in the ranking, so
    ///                                       two readings of the same geometry must agree
    ///                                       on it, ties included
    ///   FacesBeyondSixty.exe near DOC X Y Z R   the faces whose closest point to (X, Y, Z)
    ///                                       (mm) is within R mm, with their rank: what
    ///                                       `faces: { near_mm, within_mm }` reads
    ///   FacesBeyondSixty.exe cost DOC       the time of the three reads the filter does on
    ///                                       every face (area, normal, closest point), to
    ///                                       know what «every face» costs on the stand
    ///
    /// ── GUIDE PAGES READ (sldworksapi.chm, extracted 28/9/2026) ──────────────────
    ///   IBody2~GetFaces.html           «Gets all of the faces on the body»: no order promised
    ///   IFace2~GetArea.html            «calculates and returns an approximate value», m2
    ///   IFace2~GetClosestPointOn.html  «Array of five doubles representing the X,Y,Z point on
    ///                                  the face followed by the U, V parameter»; «returns only
    ///                                  one point, regardless of how many points achieve the
    ///                                  minimum distance»
    ///   ISldWorks~GetOpenDocumentByName.html
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed class Row
        {
            public IFace2 Face;
            public int Body;
            public double Area;
        }

        private static int Main(string[] args)
        {
            try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            if (args.Length < 2) return Usage();
            var model = Open(args[1]);
            if (model == null) { Console.Error.WriteLine("«" + args[1] + "» is not open."); return 2; }
            switch (args[0])
            {
                case "rank": return Rank(model);
                case "near":
                    if (args.Length < 6) return Usage();
                    return Near(model, double.Parse(args[2], Inv), double.Parse(args[3], Inv), double.Parse(args[4], Inv), double.Parse(args[5], Inv));
                case "cost": return Cost(model);
                default: return Usage();
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine("rank DOC | near DOC X Y Z R | cost DOC   (DOC: title or path of an open part)");
            return 2;
        }

        private static IModelDoc2 Open(string name)
        {
            try
            {
                var d = _sw.GetOpenDocumentByName(name) as IModelDoc2;
                if (d != null) return d;
            }
            catch (Exception) { }
            for (var d = _sw.GetFirstDocument() as IModelDoc2; d != null; d = d.GetNext() as IModelDoc2)
            {
                string title = null, path = null;
                try { title = d.GetTitle(); path = d.GetPathName(); } catch (Exception) { }
                if (string.Equals(title, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, name, StringComparison.OrdinalIgnoreCase)) return d;
            }
            return null;
        }

        /// <summary>Every face of every solid body, largest first: the ranking of DocumentState.</summary>
        private static List<Row> Ranked(IModelDoc2 model)
        {
            var part = model as IPartDoc;
            var rows = new List<Row>();
            var bodies = (part.GetBodies2((int)SolidWorks.Interop.swconst.swBodyType_e.swSolidBody, false) as object[]) ?? new object[0];
            for (int i = 0; i < bodies.Length; i++)
            {
                var faces = ((IBody2)bodies[i]).GetFaces() as object[];
                if (faces == null) continue;
                foreach (var o in faces)
                {
                    var f = (IFace2)o;
                    double a = 0;
                    try { a = f.GetArea(); } catch (Exception) { a = 0; }
                    rows.Add(new Row { Face = f, Body = i, Area = a });
                }
            }
            return rows.OrderByDescending(r => r.Area).ToList();
        }

        private static string Point(IFace2 f)
        {
            var p = f.GetClosestPointOn(0, 0, 0) as double[];
            return p == null ? "?" : string.Format(Inv, "[{0:0.#}, {1:0.#}, {2:0.#}]", p[0] * 1000, p[1] * 1000, p[2] * 1000);
        }

        private static int Rank(IModelDoc2 model)
        {
            var first = Ranked(model);
            Console.WriteLine("faces: " + first.Count + ", solid bodies in the ranking: " + (first.Count == 0 ? 0 : first.Max(r => r.Body) + 1));
            if (first.Count > 60)
            {
                double cut = first[59].Area;
                int tied = first.Count(r => Math.Abs(r.Area - cut) < 1e-12);
                int tiedBefore = first.Take(60).Count(r => Math.Abs(r.Area - cut) < 1e-12);
                Console.WriteLine(string.Format(Inv, "the 60th face has {0:0.##} mm2; {1} faces share that area, {2} of them inside the first 60, {3} left out",
                                                cut * 1e6, tied, tiedBefore, tied - tiedBefore));
            }
            for (int i = 54; i < Math.Min(70, first.Count); i++)
                Console.WriteLine(string.Format(Inv, "  F{0,-4} {1,10:0.##} mm2  body {2,-3} closest to origin {3}", i + 1, first[i].Area * 1e6, first[i].Body + 1, Point(first[i].Face)));
            var last = first.Count - 1;
            if (last >= 0)
                Console.WriteLine(string.Format(Inv, "  F{0,-4} {1,10:0.##} mm2  body {2,-3} closest to origin {3}  (the smallest)", last + 1, first[last].Area * 1e6, first[last].Body + 1, Point(first[last].Face)));

            // A SECOND ENUMERATION, as a second reading of the state would do: same order?
            var second = Ranked(model);
            int differ = 0;
            for (int i = 0; i < first.Count && i < second.Count; i++)
                if (Math.Abs(first[i].Area - second[i].Area) > 1e-15 || first[i].Body != second[i].Body || Point(first[i].Face) != Point(second[i].Face)) differ++;
            Console.WriteLine("second enumeration: " + second.Count + " faces, " + differ + " ranks differ from the first -> " +
                              (differ == 0 && second.Count == first.Count ? "THE RANKING IS STABLE between two readings" : "THE RANKING MOVES between two readings"));
            return differ == 0 && second.Count == first.Count ? 0 : 1;
        }

        private static int Near(IModelDoc2 model, double x, double y, double z, double r)
        {
            var ranked = Ranked(model);
            int found = 0;
            for (int i = 0; i < ranked.Count; i++)
            {
                var p = ranked[i].Face.GetClosestPointOn(x / 1000, y / 1000, z / 1000) as double[];
                if (p == null || p.Length < 3) continue;
                double dx = p[0] * 1000 - x, dy = p[1] * 1000 - y, dz = p[2] * 1000 - z;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > r) continue;
                found++;
                Console.WriteLine(string.Format(Inv, "  F{0,-4} {1,10:0.##} mm2  body {2,-3} distance {3:0.###} mm  (closest point [{4:0.##}, {5:0.##}, {6:0.##}])",
                                                i + 1, ranked[i].Area * 1e6, ranked[i].Body + 1, d, p[0] * 1000, p[1] * 1000, p[2] * 1000));
            }
            Console.WriteLine(found + " of " + ranked.Count + " faces within " + r.ToString(Inv) + " mm of [" + x.ToString(Inv) + ", " + y.ToString(Inv) + ", " + z.ToString(Inv) + "]");
            return 0;
        }

        private static int Cost(IModelDoc2 model)
        {
            var sw = Stopwatch.StartNew();
            var ranked = Ranked(model);
            long enumerate = sw.ElapsedMilliseconds;
            sw.Restart();
            foreach (var r in ranked) { try { var n = r.Face.Normal; } catch (Exception) { } }
            long normals = sw.ElapsedMilliseconds;
            sw.Restart();
            foreach (var r in ranked) { r.Face.GetClosestPointOn(0, 0, 0); }
            long closest = sw.ElapsedMilliseconds;
            Console.WriteLine(ranked.Count + " faces: enumerate and area " + enumerate + " ms, Normal " + normals + " ms, GetClosestPointOn " + closest + " ms");
            return 0;
        }
    }
}
