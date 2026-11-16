using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// AN STL FILE, MEASURED AND CUT (brief R15). On 24-25/10/2026 the numbers of
    /// the customer's STL files came from three Python programs written for the
    /// occasion. The «mesh» tool reads the file, measures it and cuts it into
    /// contours for «extrude_profile». These checks build meshes whose numbers
    /// are known by construction — a box, a square tube, a 64-sided cylinder —
    /// and compare: volume, area, closed or not, the contours of a section with
    /// their depth and their plane, the binary and the ASCII form of the same
    /// file, and the tool through the real bridge on a file written here.
    /// </summary>
    internal static class R15MeshContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        internal static void RunAll()
        {
            Group("an STL file, measured and cut: the «mesh» tool (brief R15)");
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library)) { NotHere("the mesh rules", library + " is missing: build the library first (build.ps1)"); return; }
            Type mesh;
            try { mesh = Assembly.Load(File.ReadAllBytes(library)).GetType("Oce.Solid.Agent.Core.StlMesh", false); }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (mesh == null) { Check("StlMesh is in the library", false, "not found (0.159.0 or older)"); return; }
            Measures(mesh);
            Sections(mesh);
            Files(mesh);
            ThroughTheBridge();
        }

        // ── meshes known by construction ─────────────────────────────────

        private static double[] T(double[] a, double[] b, double[] c)
        {
            return new[] { a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2] };
        }

        private static double[] P(double[] xy, double z) { return new[] { xy[0], xy[1], z }; }

        /// <summary>
        /// A prism from z0 to z1: side walls of each loop (outer counter-clockwise,
        /// void clockwise, so every normal points out of the material), caps by a fan
        /// when there is no void, by quads between matching vertices when there is one.
        /// </summary>
        private static List<double[]> Prism(List<double[]> outer, List<double[]> hole, double z0, double z1)
        {
            var t = new List<double[]>();
            Action<List<double[]>> walls = loop =>
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    var a = loop[i]; var b = loop[(i + 1) % loop.Count];
                    t.Add(T(P(a, z0), P(b, z0), P(b, z1)));
                    t.Add(T(P(a, z0), P(b, z1), P(a, z1)));
                }
            };
            walls(outer);
            if (hole == null)
            {
                for (int i = 1; i + 1 < outer.Count; i++)
                {
                    t.Add(T(P(outer[0], z1), P(outer[i], z1), P(outer[i + 1], z1)));
                    t.Add(T(P(outer[0], z0), P(outer[i + 1], z0), P(outer[i], z0)));
                }
                return t;
            }
            var reversed = new List<double[]>(hole);
            reversed.Reverse();
            walls(reversed);
            for (int i = 0; i < outer.Count; i++)
            {
                int j = (i + 1) % outer.Count;
                t.Add(T(P(outer[i], z1), P(outer[j], z1), P(hole[j], z1)));
                t.Add(T(P(outer[i], z1), P(hole[j], z1), P(hole[i], z1)));
                t.Add(T(P(outer[i], z0), P(hole[j], z0), P(outer[j], z0)));
                t.Add(T(P(outer[i], z0), P(hole[i], z0), P(hole[j], z0)));
            }
            return t;
        }

        private static List<double[]> Square(double x0, double y0, double x1, double y1)
        {
            return new List<double[]> { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x1, y1 }, new[] { x0, y1 } };
        }

        private static List<double[]> Polygon(int sides, double radius)
        {
            var p = new List<double[]>();
            for (int i = 0; i < sides; i++)
            {
                double a = 2 * Math.PI * i / sides;
                p.Add(new[] { radius * Math.Cos(a), radius * Math.Sin(a) });
            }
            return p;
        }

        private static List<double[]> Box() { return Prism(Square(0, 0, 10, 20), null, 0, 30); }
        private static List<double[]> Tube() { return Prism(Square(-10, -10, 10, 10), Square(-5, -5, 5, 5), 0, 30); }
        private static List<double[]> Cylinder() { return Prism(Polygon(64, 10), null, 0, 20); }

        // ── reflection ───────────────────────────────────────────────────

        private static object Mesh(Type mesh, List<double[]> triangles)
        {
            return mesh.GetMethod("FromTriangles", Any).Invoke(null, new object[] { triangles, 1.0 });
        }

        private static Dictionary<string, object> Measure(object m)
        {
            return (Dictionary<string, object>)m.GetType().GetMethod("Measure", Any).Invoke(m, null);
        }

        private static object Cut(object m, int axis, double at, double simplify)
        {
            return m.GetType().GetMethod("Section", Any).Invoke(m, new object[] { axis, at, simplify });
        }

        private static IList Loops(object cut) { return (IList)cut.GetType().GetField("Loops", Any).GetValue(cut); }
        private static object F(object o, string name) { return o.GetType().GetField(name, Any).GetValue(o); }

        private static double Num(object v) { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }

        private static string Near(double got, double want, double tolerance, string what)
        {
            return Math.Abs(got - want) <= tolerance ? null : what + " " + got.ToString("0.######", CultureInfo.InvariantCulture) +
                   " instead of " + want.ToString("0.######", CultureInfo.InvariantCulture);
        }

        // ── the checks ───────────────────────────────────────────────────

        private static void Measures(Type mesh)
        {
            const string label = "measure: box 10x20x30, square tube 20/10 x 30, 64-sided cylinder r10 x 20 — volume, area, size, closed; one triangle missing is open and has no volume";
            try
            {
                var wrong = new List<string>();
                var box = Measure(Mesh(mesh, Box()));
                wrong.Add(Near(Num(box["volume_mm3"]), 6000, 1e-6, "box volume"));
                wrong.Add(Near(Num(box["area_mm2"]), 2200, 1e-6, "box area"));
                var size = (double[])box["size_mm"];
                if (size[0] != 10 || size[1] != 20 || size[2] != 30) wrong.Add("box size " + string.Join("x", size));
                if (!(bool)box["closed"]) wrong.Add("the box is not closed");
                if (Convert.ToString(box["normals"]) != "outward") wrong.Add("box normals " + box["normals"]);
                var tube = Measure(Mesh(mesh, Tube()));
                wrong.Add(Near(Num(tube["volume_mm3"]), 9000, 1e-6, "tube volume"));
                if (!(bool)tube["closed"]) wrong.Add("the tube is not closed");
                double polygonArea = 0.5 * 64 * 100 * Math.Sin(2 * Math.PI / 64);
                var cylinder = Measure(Mesh(mesh, Cylinder()));
                wrong.Add(Near(Num(cylinder["volume_mm3"]), polygonArea * 20, 1e-3, "cylinder volume"));
                var open = Box();
                open.RemoveAt(open.Count - 1);
                var holed = Measure(Mesh(mesh, open));
                if ((bool)holed["closed"] || Convert.ToInt32(holed["open_edges"]) != 3 || holed["volume_mm3"] != null)
                    wrong.Add("a box without one triangle: closed " + holed["closed"] + ", open edges " + holed["open_edges"] + ", volume " + (holed["volume_mm3"] ?? "null"));
                wrong.RemoveAll(s => s == null);
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6000, 2200, 9000, " + (polygonArea * 20).ToString("0.###", CultureInfo.InvariantCulture) + " mm3; 3 open edges and no volume" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        private static void Sections(Type mesh)
        {
            const string label = "section: the box on front, right and top in the measured sketch axes; the tube as an outer edge and its void in one profile; the cylinder simplified; outside the mesh, nothing";
            try
            {
                var wrong = new List<string>();
                var box = Mesh(mesh, Box());
                var z = Cut(box, 2, 15, 0.01);
                var zl = Loops(z);
                if (zl.Count != 1) wrong.Add("box at z 15: " + zl.Count + " contours");
                else
                {
                    var pts = (List<double[]>)F(zl[0], "Points");
                    if (pts.Count != 4) wrong.Add("box at z 15: " + pts.Count + " vertices instead of 4");
                    wrong.Add(Near(Num(F(zl[0], "SignedArea")), 200, 1e-9, "box at z 15, area"));
                }
                if ((string)mesh.GetMethod("PlaneFor", Any).Invoke(null, new object[] { 2 }) != "front") wrong.Add("z is not front");
                if ((string)mesh.GetMethod("PlaneFor", Any).Invoke(null, new object[] { 0 }) != "right") wrong.Add("x is not right");
                if ((string)mesh.GetMethod("PlaneFor", Any).Invoke(null, new object[] { 1 }) != "top") wrong.Add("y is not top");
                // Square to X the sketch of «right» has x along -Z and y along +Y.
                var xl = Loops(Cut(box, 0, 5, 0.01));
                if (xl.Count != 1) wrong.Add("box at x 5: " + xl.Count + " contours");
                else
                    foreach (var p in (List<double[]>)F(xl[0], "Points"))
                        if (p[0] < -30 - 1e-9 || p[0] > 1e-9 || p[1] < -1e-9 || p[1] > 20 + 1e-9) { wrong.Add("box at x 5: vertex (" + p[0] + ", " + p[1] + ") is not in u -30..0, v 0..20"); break; }
                // Square to Y the sketch of «top» has x along +X and y along -Z.
                var yl = Loops(Cut(box, 1, 10, 0.01));
                if (yl.Count != 1) wrong.Add("box at y 10: " + yl.Count + " contours");
                else
                    foreach (var p in (List<double[]>)F(yl[0], "Points"))
                        if (p[0] < -1e-9 || p[0] > 10 + 1e-9 || p[1] < -30 - 1e-9 || p[1] > 1e-9) { wrong.Add("box at y 10: vertex (" + p[0] + ", " + p[1] + ") is not in u 0..10, v -30..0"); break; }
                var top = Loops(Cut(box, 2, 30, 0.01));
                if (top.Count != 1) wrong.Add("box exactly at its top: " + top.Count + " contours");

                var tube = Cut(Mesh(mesh, Tube()), 2, 12.5, 0.01);
                var tl = Loops(tube);
                if (tl.Count != 2) wrong.Add("tube: " + tl.Count + " contours");
                else
                {
                    if (Convert.ToInt32(F(tl[0], "Depth")) != 0 || Convert.ToInt32(F(tl[1], "Depth")) != 1) wrong.Add("tube depths " + F(tl[0], "Depth") + ", " + F(tl[1], "Depth"));
                    if (Num(F(tl[0], "SignedArea")) <= 0 || Num(F(tl[1], "SignedArea")) >= 0) wrong.Add("tube: the outer edge is not counter-clockwise or the void not clockwise");
                    wrong.Add(Near(Num(mesh.GetMethod("SectionArea", Any).Invoke(null, new[] { tube })), 300, 1e-9, "tube section area"));
                    var profiles = (IList)mesh.GetMethod("Profiles", Any).Invoke(null, new[] { tube });
                    if (profiles.Count != 1 || ((IList)profiles[0]).Count != 2) wrong.Add("tube: " + profiles.Count + " profiles instead of one with two contours");
                }

                var cylinder = Cut(Mesh(mesh, Cylinder()), 2, 10, 0.5);
                var cl = Loops(cylinder);
                int before = Convert.ToInt32(F(cylinder, "PointsBefore"));
                int after = cl.Count == 1 ? ((List<double[]>)F(cl[0], "Points")).Count : -1;
                // 128 crossings: each of the 64 walls is two triangles, cut on its
                // vertical edge and on its diagonal, which lies on the chord.
                if (before != 128 || after < 8 || after >= 64) wrong.Add("cylinder simplified by 0.5 mm: " + before + " vertices before, " + after + " after");

                var outside = Loops(Cut(box, 2, 50, 0.01));
                if (outside.Count != 0) wrong.Add("box at z 50: " + outside.Count + " contours");
                wrong.RemoveAll(s => s == null);
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "box 4 vertices 200 mm2 on three planes, tube 300 mm2 in one profile, cylinder 64 -> " + after + " vertices" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        internal static byte[] Binary(List<double[]> triangles)
        {
            var bytes = new List<byte>(new byte[80]);
            bytes.AddRange(BitConverter.GetBytes((uint)triangles.Count));
            foreach (var t in triangles)
            {
                for (int k = 0; k < 3; k++) bytes.AddRange(BitConverter.GetBytes(0f));
                for (int k = 0; k < 9; k++) bytes.AddRange(BitConverter.GetBytes((float)t[k]));
                bytes.Add(0); bytes.Add(0);
            }
            return bytes.ToArray();
        }

        private static string Ascii(List<double[]> triangles)
        {
            var s = new StringBuilder("solid made_by_the_tests\n");
            foreach (var t in triangles)
            {
                s.Append(" facet normal 0 0 0\n  outer loop\n");
                for (int k = 0; k < 3; k++)
                    s.Append("   vertex ").Append(t[3 * k].ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                     .Append(t[3 * k + 1].ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                     .Append(t[3 * k + 2].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                s.Append("  endloop\n endfacet\n");
            }
            return s.Append("endsolid made_by_the_tests\n").ToString();
        }

        private static void Files(Type mesh)
        {
            const string label = "the same box as a binary and as an ASCII file gives the same numbers; a binary that begins with «solid» stays binary; inches by «scale»; a file that is neither is refused";
            try
            {
                var wrong = new List<string>();
                var fromBytes = mesh.GetMethod("FromBytes", Any);
                Func<byte[], double, object[]> read = (b, scale) =>
                {
                    var args = new object[] { b, scale, null };
                    var m = fromBytes.Invoke(null, args);
                    return new[] { m, args[2] };
                };
                var binary = Binary(Box());
                var b1 = read(binary, 1);
                if (b1[0] == null) wrong.Add("binary refused: " + b1[1]);
                else if (Convert.ToString(F(b1[0], "Format")) != "binary" || Num(Measure(b1[0])["volume_mm3"]) != 6000) wrong.Add("binary: " + F(b1[0], "Format") + ", " + Measure(b1[0])["volume_mm3"]);
                var solidHeader = (byte[])binary.Clone();
                Encoding.ASCII.GetBytes("solid header of a binary file").CopyTo(solidHeader, 0);
                var b2 = read(solidHeader, 1);
                if (b2[0] == null || Convert.ToString(F(b2[0], "Format")) != "binary") wrong.Add("a binary beginning with «solid» was not read as binary: " + (b2[1] ?? F(b2[0], "Format")));
                var a1 = read(Encoding.ASCII.GetBytes(Ascii(Box())), 1);
                if (a1[0] == null) wrong.Add("ascii refused: " + a1[1]);
                else if (Convert.ToString(F(a1[0], "Format")) != "ascii" || Num(Measure(a1[0])["volume_mm3"]) != 6000) wrong.Add("ascii: " + F(a1[0], "Format") + ", " + Measure(a1[0])["volume_mm3"]);
                var inches = read(binary, 25.4);
                if (inches[0] != null) wrong.Add(Near(Num(Measure(inches[0])["volume_mm3"]), 6000 * Math.Pow(25.4, 3), 1e-3, "scale 25.4, volume"));
                var junk = read(Encoding.ASCII.GetBytes("this is not a mesh at all, not even close"), 1);
                if (junk[0] != null || !Convert.ToString(junk[1]).Contains("neither")) wrong.Add("junk: " + (junk[1] ?? "taken"));
                wrong.RemoveAll(s => s == null);
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "binary 6000, «solid» binary 6000, ascii 6000, inches x 25.4^3, junk refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        private static void ThroughTheBridge()
        {
            const string label = "«mesh» through the real bridge: measure and section of a file written here, and the profile ready for extrude_profile";
            var exe = BridgeOrNotHere(label);
            if (exe == null) return;
            var file = Path.Combine(Path.GetTempPath(), "oce-mesh-check-" + Guid.NewGuid().ToString("N") + ".stl");
            File.WriteAllBytes(file, Binary(Tube()));
            var p = Open(exe);
            if (p == null) { Check(label, false, "the bridge did not start"); File.Delete(file); return; }
            try
            {
                Ask(p, 1, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                          "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");
                var path = file.Replace("\\", "\\\\");
                var measured = Inside(Obj(Ask(p, 50, "{\"jsonrpc\":\"2.0\",\"id\":50,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"mesh\",\"arguments\":{\"path\":\"" + path + "\",\"axis\":\"z\",\"levels\":3}}}")), "result") as Dictionary<string, object>;
                var mt = TextOf(measured);
                var cut = Inside(Obj(Ask(p, 51, "{\"jsonrpc\":\"2.0\",\"id\":51,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"mesh\",\"arguments\":{\"path\":\"" + path + "\",\"action\":\"section\",\"axis\":\"z\",\"at_mm\":15}}}")), "result") as Dictionary<string, object>;
                var ct = TextOf(cut);
                var wrong = new List<string>();
                if (!mt.Contains("\"volume_mm3\":9000") || !mt.Contains("\"closed\":true") || !mt.Contains("\"area_mm2\":300")) wrong.Add("measure: " + Short(mt));
                if (!ct.Contains("\"plane\":\"front\"") || !ct.Contains("\"section_area_mm2\":300") || !ct.Contains("\"contours\":[[[")) wrong.Add("section: " + Short(ct));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "9000 mm3, closed, three sections of 300 mm2; one profile on front of 300 mm2" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, ex.GetType().Name + ": " + ex.Message); }
            finally { Close(p); try { File.Delete(file); } catch { } }
        }
    }
}
