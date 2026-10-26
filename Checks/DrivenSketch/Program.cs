using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.DrivenSketch
{
    /// <summary>
    /// DIMENSIONS ON THE VERTICES OF A PROFILE (brief R5), measured before the
    /// library writes them.
    ///
    /// On 24/10/2026 every `extrude_profile` came back with «sketch_locked» true
    /// and twelve FIXED relations: the only dimension a variable could drive was
    /// the depth. A width or a position could not be tied to a name. The fix
    /// proposed is a horizontal and a vertical dimension from the sketch origin
    /// to every vertex, put while the sketch is still open. Three things are not
    /// known and this probe measures them:
    ///
    ///  1. HOW THE SKETCH ORIGIN IS SELECTED from outside the process, in any
    ///     language of SOLIDWORKS. Two ways are tried: by position
    ///     (`SelectByID2` with no name, type EXTSKETCHPOINT, at 0,0,0) and through
    ///     the sketch of the origin feature (type `OriginProfileFeature`, its
    ///     sketch points). IModelDocExtension~SelectByID2.html: "Name of object to
    ///     select or an empty string"; with an empty name the point at X, Y, Z is
    ///     taken.
    ///  2. WHETHER `AddHorizontalDimension2` / `AddVerticalDimension2` give the
    ///     coordinate of a sketch point against the origin. IModelDoc2~
    ///     AddHorizontalDimension2.html: "Creates a horizontal dimension for the
    ///     currently selected entities at the specified location." Measured on
    ///     25/9/2026 for a sketch point and a MODEL edge (AnchorOperations,
    ///     DimensionPointEdge); never for two sketch points.
    ///  3. WHAT THE DIMENSIONS DO AFTERWARDS: a vertex on an axis (a dimension of
    ///     zero), a vertex on the negative side (the dimension reads the distance,
    ///     the sign is the side), and a value changed after the extrusion: the
    ///     volume must be the one the shoelace formula gives for the moved vertex.
    ///
    /// Every run opens a NEW part and closes it without saving: nothing on disk
    /// is touched. `swInputDimValOnCreate` is switched off around the
    /// dimensions and put back (a dimension born with it on opens a modal and the
    /// COM call does not return).
    ///
    ///     DrivenSketch.exe              both ways of selecting the origin
    ///     DrivenSketch.exe position     only by position
    ///     DrivenSketch.exe feature      only through the origin feature
    ///     DrivenSketch.exe star         timing: a 20-point star, 40 dimensions
    /// </summary>
    internal static class Program
    {
        // An L with two vertices on the Y axis (x = 0) and three on the negative
        // side. Millimetres, sketch coordinates of the Front plane.
        private static readonly double[][] Ell =
        {
            new[] { -30.0, 10 }, new[] { 30.0, 10 }, new[] { 30.0, 20 },
            new[] { 0.0, 20 }, new[] { 0.0, 50 }, new[] { -30.0, 50 },
        };

        private const double ThicknessMm = 10;

        private static int Main(string[] arguments)
        {
            string mode = arguments.Length > 0 ? arguments[0] : "all";
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            var sw = (ISldWorks)raw;
            string template = Template(sw);
            if (template == null) { Console.Error.WriteLine("No part template (.prtdot) found."); return 2; }

            var runs = new List<Tuple<string, double[][]>>();
            if (mode == "all" || mode == "position") runs.Add(Tuple.Create("position", Ell));
            if (mode == "all" || mode == "feature") runs.Add(Tuple.Create("feature", Ell));
            if (mode == "star") runs.Add(Tuple.Create("feature", Star(20, 40, 20)));
            if (runs.Count == 0) { Console.Error.WriteLine("Unknown mode: " + mode); return 2; }

            int worst = 0;
            foreach (var run in runs)
            {
                var doc = (IModelDoc2)sw.NewDocument(template, 0, 0, 0);
                if (doc == null) { Console.Error.WriteLine("NewDocument returned null."); return 2; }
                string title = doc.GetTitle();
                Console.WriteLine("=== origin selected by " + run.Item1 + ", " + run.Item2.Length + " vertices, part " + title);
                try { worst = Math.Max(worst, Run(sw, doc, run.Item1, run.Item2)); }
                catch (Exception ex) { Console.WriteLine("EXCEPTION " + ex.GetType().Name + ": " + ex.Message); worst = 1; }
                finally { try { sw.CloseDoc(title); } catch { } }
            }
            return worst;
        }

        private static int Run(ISldWorks sw, IModelDoc2 doc, string originWay, double[][] vertices)
        {
            if (!SelectFrontPlane(doc)) { Console.WriteLine("the first plane of the tree could not be selected"); return 1; }
            var sm = doc.SketchManager;
            sm.InsertSketch(true);
            bool addToDb = sm.AddToDB, display = sm.DisplayWhenAdded;
            sm.AddToDB = true;
            sm.DisplayWhenAdded = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Length];
                sm.CreateLine(a[0] / 1000, a[1] / 1000, 0, b[0] / 1000, b[1] / 1000, 0);
            }
            sm.AddToDB = addToDb;
            sm.DisplayWhenAdded = display;

            var sketch = sm.ActiveSketch as ISketch;
            var points = sketch?.GetSketchPoints2() as object[];
            Console.WriteLine("sketch points after drawing: " + (points == null ? "null" : points.Length.ToString()) +
                              " (vertices " + vertices.Length + ": equal means the ends of the lines were merged)");
            if (points == null) return 1;

            bool asked = true;
            try
            {
                asked = sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }

            var watch = Stopwatch.StartNew();
            var dims = new Dictionary<string, IDimension>();
            int born = 0, refusedOrigin = 0;
            try
            {
                for (int i = 0; i < points.Length; i++)
                {
                    var p = points[i] as ISketchPoint;
                    if (p == null) continue;
                    double xMm = p.X * 1000, yMm = p.Y * 1000;
                    foreach (bool horizontal in new[] { true, false })
                    {
                        doc.ClearSelection2(true);
                        bool pointTaken = p.Select4(false, null);
                        bool originTaken = SelectOrigin(doc, originWay);
                        if (!originTaken) refusedOrigin++;
                        object made = null;
                        if (pointTaken && originTaken)
                        {
                            made = horizontal
                                ? doc.AddHorizontalDimension2(p.X / 2, p.Y + 0.006, 0)
                                : doc.AddVerticalDimension2(p.X + 0.006, p.Y / 2, 0);
                        }
                        var display2 = made as IDisplayDimension;
                        var dim = display2?.GetDimension2(0);
                        string key = Fmt(xMm) + "," + Fmt(yMm) + (horizontal ? " x" : " y");
                        if (dim == null)
                        {
                            Console.WriteLine("  vertex " + key + ": no dimension (point " + pointTaken + ", origin " + originTaken + ")");
                            continue;
                        }
                        born++;
                        dims[key] = dim;
                        Console.WriteLine("  vertex " + key + ": " + dim.FullName + " = " + Fmt(Read(dim) * 1000) + " mm");
                    }
                }
            }
            finally
            {
                try { doc.ClearSelection2(true); } catch { }
                try { sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, asked); } catch { }
            }
            watch.Stop();
            int status = -1;
            try { status = sketch.GetConstrainedStatus(); } catch { }
            Console.WriteLine("dimensions born: " + born + " of " + (2 * points.Length) + ", origin not selected " + refusedOrigin +
                              " times, " + watch.ElapsedMilliseconds + " ms; sketch status " + status +
                              " (swConstrainedStatus_e: 2 under, 3 fully, 4 over)");

            // Out of the sketch, select it, extrude.
            sm.InsertSketch(true);
            var sketchFeature = doc.FeatureByPositionReverse(0) as IFeature;
            doc.ClearSelection2(true);
            if (sketchFeature == null || !sketchFeature.Select2(false, 0)) { Console.WriteLine("the sketch could not be selected"); return 1; }
            var feature = doc.FeatureManager.FeatureExtrusion3(true, false, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                ThicknessMm / 1000, 0, false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, (int)swStartConditions_e.swStartSketchPlane, 0, false) as IFeature;
            if (feature == null) { Console.WriteLine("the extrusion was not created"); return 1; }
            Report(doc, "after the extrusion", vertices);

            if (vertices != Ell) return born == 2 * points.Length ? 0 : 1;

            // MOVE ONE VERTEX on the negative side: x of (-30,10) from 30 to 40.
            var moved = Copy(vertices);
            moved[0][0] = -40;
            Change(doc, dims, "-30,10 x", 0.040);
            Report(doc, "x of (-30,10) driven to 40", moved);

            // MOVE TWO VERTICES on the positive side: x of (30,10) and (30,20) to 45.
            moved[1][0] = 45; moved[2][0] = 45;
            Change(doc, dims, "30,10 x", 0.045);
            Change(doc, dims, "30,20 x", 0.045);
            Report(doc, "x of (30,10) and (30,20) driven to 45", moved);

            // A ZERO DIMENSION driven away from zero: which side does the vertex go?
            Change(doc, dims, "0,50 x", 0.005);
            Report(doc, "x of (0,50) driven from 0 to 5 (side not known: read the volume)", null);
            return born == 2 * points.Length ? 0 : 1;
        }

        private static void Change(IModelDoc2 doc, Dictionary<string, IDimension> dims, string key, double metres)
        {
            IDimension dim;
            if (!dims.TryGetValue(key, out dim)) { Console.WriteLine("  no dimension for " + key); return; }
            int answer = -1;
            try { answer = dim.SetSystemValue3(metres, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null); }
            catch (Exception ex) { Console.WriteLine("  SetSystemValue3 on " + key + ": " + ex.Message); }
            bool rebuilt = false;
            try { rebuilt = doc.EditRebuild3(); } catch { }
            Console.WriteLine("  " + key + " -> " + Fmt(metres * 1000) + " mm: SetSystemValue3 " + answer + " (0 = success), rebuild " + rebuilt +
                              ", read back " + Fmt(Read(dim) * 1000) + " mm");
        }

        private static void Report(IModelDoc2 doc, string when, double[][] expectedVertices)
        {
            double? volume = Volume(doc);
            string expected = expectedVertices == null ? "" :
                ", expected " + Fmt(Math.Abs(Shoelace(expectedVertices)) * ThicknessMm) + " mm3 (shoelace x " + Fmt(ThicknessMm) + ")";
            Console.WriteLine(when + ": volume " + (volume == null ? "no body" : Fmt(volume.Value) + " mm3") + expected);
        }

        private static bool SelectOrigin(IModelDoc2 doc, string way)
        {
            if (way == "position")
                return doc.Extension.SelectByID2("", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, 0);
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                string type = "";
                try { type = f.GetTypeName2(); } catch { }
                if (type == "OriginProfileFeature")
                {
                    var originSketch = f.GetSpecificFeature2() as ISketch;
                    var originPoints = originSketch?.GetSketchPoints2() as object[];
                    if (originPoints == null || originPoints.Length == 0) return false;
                    var o = originPoints[0] as ISketchPoint;
                    return o != null && o.Select4(true, null);
                }
                f = f.GetNextFeature() as IFeature;
            }
            return false;
        }

        private static bool SelectFrontPlane(IModelDoc2 doc)
        {
            doc.ClearSelection2(true);
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                string type = "";
                try { type = f.GetTypeName2(); } catch { }
                if (type == "RefPlane") return f.Select2(false, 0);
                f = f.GetNextFeature() as IFeature;
            }
            return false;
        }

        private static double Read(IDimension dim)
        {
            try
            {
                var values = dim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                return values != null && values.Length > 0 ? values[0] : double.NaN;
            }
            catch { return double.NaN; }
        }

        private static double Shoelace(double[][] p)
        {
            double s = 0;
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Length];
                s += a[0] * b[1] - b[0] * a[1];
            }
            return s / 2;
        }

        private static double[][] Star(int points, double outer, double inner)
        {
            var r = new double[points][];
            for (int i = 0; i < points; i++)
            {
                double radius = i % 2 == 0 ? outer : inner;
                double angle = 2 * Math.PI * i / points;
                r[i] = new[] { Math.Round(radius * Math.Cos(angle), 3), Math.Round(radius * Math.Sin(angle), 3) };
            }
            return r;
        }

        private static double[][] Copy(double[][] p)
        {
            var r = new double[p.Length][];
            for (int i = 0; i < p.Length; i++) r[i] = new[] { p[i][0], p[i][1] };
            return r;
        }

        private static double? Volume(IModelDoc2 doc)
        {
            try
            {
                var partDoc = doc as IPartDoc;
                var bodies = partDoc?.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null || bodies.Length == 0) return null;
                double total = 0;
                foreach (var c in bodies)
                {
                    var body = c as IBody2;
                    var p = body?.GetMassProperties(1) as double[];
                    if (p != null && p.Length > 3) total += p[3];
                }
                return total * 1e9;
            }
            catch { return null; }
        }

        private static string Fmt(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Template(ISldWorks sw)
        {
            try
            {
                var p = sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            }
            catch { }
            var its = System.Environment.GetEnvironmentVariable("OCE_DOCUMENT_TEMPLATES");
            foreach (var folder in new[] { @"C:\ProgramData\SolidWorks", its })
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                foreach (var f in Directory.GetFiles(folder, "*.prtdot", SearchOption.AllDirectories))
                {
                    string b = f.ToLowerInvariant();
                    if (b.Contains(@"\mbd\") || b.Contains("0001mm") || b.Contains("inch")) continue;
                    return f;
                }
            }
            return null;
        }
    }
}
