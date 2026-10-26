using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A TRIANGLE MESH READ FROM AN STL FILE, measured and cut (brief R15).
    ///
    /// On 24-25/10/2026 the parts of 26M000002 were drawn from STL files of the
    /// customer, and every number taken from them came from three Python
    /// programs written for the occasion: the bounding box, the volume, and the
    /// sections to redraw as contours. The bridge had no tool for it. This class
    /// is that tool: it reads the file (binary or ASCII), measures it, and cuts it
    /// with a plane square to an axis into closed contours, already in the sketch
    /// coordinates of the plane «extrude_profile» draws on.
    ///
    /// THE SKETCH AXES ARE THE MEASURED ONES (brief R7, 26/10/2026): front has x
    /// along +X and y along +Y; right has x along -Z and y along +Y; top has x
    /// along +X and y along -Z. A section square to Z is drawn on front, square to
    /// X on right, square to Y on top.
    ///
    /// UNITS. An STL file has none. Its numbers are taken as millimetres, times
    /// «scale» (25.4 for inches, 1000 for metres).
    ///
    /// Pure: no interop type, so the contract suite calls it by reflection, and
    /// SOLIDWORKS is not needed to use it.
    /// </summary>
    public sealed class StlMesh
    {
        /// <summary>The distinct vertices, in mm.</summary>
        public readonly List<double[]> Vertices = new List<double[]>();

        /// <summary>The triangles, three vertex indices each, in the order of the file.</summary>
        public readonly List<int[]> Triangles = new List<int[]>();

        /// <summary>«binary» or «ascii».</summary>
        public string Format = "";

        /// <summary>Triangles of the file with two equal vertices, left out.</summary>
        public int Degenerate;

        /// <summary>The largest file read: a larger one is refused with its size.</summary>
        public const long MaxBytes = 400L * 1024 * 1024;

        private readonly Dictionary<VertexKey, int> _index = new Dictionary<VertexKey, int>();

        private struct VertexKey : IEquatable<VertexKey>
        {
            private readonly double _x, _y, _z;
            public VertexKey(double x, double y, double z) { _x = x; _y = y; _z = z; }
            public bool Equals(VertexKey o) { return _x == o._x && _y == o._y && _z == o._z; }
            public override bool Equals(object o) { return o is VertexKey && Equals((VertexKey)o); }
            public override int GetHashCode()
            {
                unchecked { return (_x.GetHashCode() * 397 ^ _y.GetHashCode()) * 397 ^ _z.GetHashCode(); }
            }
        }

        private int Vertex(double x, double y, double z)
        {
            var key = new VertexKey(x, y, z);
            int i;
            if (_index.TryGetValue(key, out i)) return i;
            List<double[]> vertices = Vertices;
            i = vertices.Count;
            vertices.Add(new[] { x, y, z });
            _index[key] = i;
            return i;
        }

        private void Add(double[] t, int offset, double scale)
        {
            int a = Vertex(t[offset] * scale, t[offset + 1] * scale, t[offset + 2] * scale);
            int b = Vertex(t[offset + 3] * scale, t[offset + 4] * scale, t[offset + 5] * scale);
            int c = Vertex(t[offset + 6] * scale, t[offset + 7] * scale, t[offset + 8] * scale);
            if (a == b || b == c || a == c) { Degenerate++; return; }
            List<int[]> triangles = Triangles;
            triangles.Add(new[] { a, b, c });
        }

        /// <summary>A mesh from triangles of nine numbers each (three vertices), for the checks.</summary>
        public static StlMesh FromTriangles(IList<double[]> triangles, double scale)
        {
            var m = new StlMesh { Format = "triangles" };
            foreach (var t in triangles) m.Add(t, 0, scale);
            return m;
        }

        /// <summary>The mesh of an STL file, or null with the reason.</summary>
        public static StlMesh Load(string path, double scale, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(path)) { problem = "«path» is missing: give the full path of the STL file."; return null; }
            if (!File.Exists(path)) { problem = "There is no file «" + path + "»: give the full path of an STL file that exists."; return null; }
            long size = new FileInfo(path).Length;
            if (size > MaxBytes)
            {
                problem = "«" + path + "» is " + (size / 1048576) + " MB, and I read at most " + (MaxBytes / 1048576) +
                          " MB: decimate it first (fewer triangles keep the shape).";
                return null;
            }
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) { problem = "I cannot read «" + path + "»: " + ex.Message; return null; }
            return FromBytes(bytes, scale, out problem);
        }

        /// <summary>
        /// The mesh of the bytes of an STL file. BINARY when the size is exactly
        /// 84 + 50 × the count written at byte 80 (a binary file may begin with
        /// «solid» too, so the word alone does not decide); ASCII otherwise, when
        /// it begins with «solid».
        /// </summary>
        public static StlMesh FromBytes(byte[] bytes, double scale, out string problem)
        {
            problem = null;
            if (!(scale > 0) || double.IsInfinity(scale)) { problem = "«scale» must be a number above zero (25.4 for a file in inches)."; return null; }
            if (bytes == null || bytes.Length < 15) { problem = "The file is too short to be an STL."; return null; }
            if (bytes.Length >= 84)
            {
                long count = BitConverter.ToUInt32(bytes, 80);
                if (84 + 50 * count == bytes.Length)
                {
                    var m = new StlMesh { Format = "binary" };
                    var t = new double[9];
                    for (long i = 0; i < count; i++)
                    {
                        int at = (int)(84 + 50 * i + 12);
                        for (int k = 0; k < 9; k++) t[k] = BitConverter.ToSingle(bytes, at + 4 * k);
                        m.Add(t, 0, scale);
                    }
                    return Checked(m, out problem);
                }
            }
            // Read as ASCII by hand, one byte one character: an STL in text is ASCII.
            var characters = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) characters[i] = (char)bytes[i];
            string text = new string(characters);
            if (!text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase))
            {
                problem = "The file is neither a binary STL (its size does not match the count of triangles it declares) " +
                          "nor an ASCII one (it does not begin with «solid»).";
                return null;
            }
            var mesh = new StlMesh { Format = "ascii" };
            var numbers = new double[9];
            int n = 0;
            var tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!string.Equals(tokens[i], "vertex", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 3 >= tokens.Length) { problem = "The ASCII file ends in the middle of a vertex."; return null; }
                for (int k = 1; k <= 3; k++)
                    if (!double.TryParse(tokens[i + k], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[n * 3 + k - 1]))
                    { problem = "«" + tokens[i + k] + "» after «vertex» is not a number."; return null; }
                i += 3;
                if (++n == 3) { mesh.Add(numbers, 0, scale); n = 0; }
            }
            if (n != 0) { problem = "The ASCII file has a facet with " + n + " vertices instead of three."; return null; }
            return Checked(mesh, out problem);
        }

        private static StlMesh Checked(StlMesh m, out string problem)
        {
            problem = m.Triangles.Count == 0 ? "The file holds no triangle with three distinct vertices." : null;
            return problem == null ? m : null;
        }

        // ── MEASURE ─────────────────────────────────────────────────────

        /// <summary>
        /// Bounding box, volume, area and whether the mesh is closed. The volume is
        /// the sum of the signed tetrahedra from the origin, which is the volume
        /// only for a CLOSED mesh: «closed» says it, from the edges (each must be
        /// shared by exactly two triangles).
        /// </summary>
        public Dictionary<string, object> Measure()
        {
            double[] min = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] max = { double.MinValue, double.MinValue, double.MinValue };
            foreach (var v in Vertices)
                for (int k = 0; k < 3; k++) { min[k] = Math.Min(min[k], v[k]); max[k] = Math.Max(max[k], v[k]); }
            double volume = 0, area = 0;
            var edges = new Dictionary<long, int>();
            foreach (var t in Triangles)
            {
                double[] a = Vertices[t[0]], b = Vertices[t[1]], c = Vertices[t[2]];
                volume += (a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0])) / 6.0;
                double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                double vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                area += Math.Sqrt(cx * cx + cy * cy + cz * cz) / 2.0;
                for (int k = 0; k < 3; k++)
                {
                    long key = EdgeKey(t[k], t[(k + 1) % 3]);
                    int seen;
                    edges.TryGetValue(key, out seen);
                    edges[key] = seen + 1;
                }
            }
            int open = 0, crowded = 0;
            foreach (int count in edges.Values) { if (count == 1) open++; else if (count > 2) crowded++; }
            bool closed = open == 0 && crowded == 0;
            var result = new Dictionary<string, object>
            {
                ["format"] = Format,
                ["triangles"] = Triangles.Count,
                ["vertices"] = Vertices.Count,
                ["bounding_box_min_mm"] = Round(min, 3),
                ["bounding_box_max_mm"] = Round(max, 3),
                ["size_mm"] = Round(new[] { max[0] - min[0], max[1] - min[1], max[2] - min[2] }, 3),
                ["area_mm2"] = Math.Round(area, 3),
                ["closed"] = closed,
                ["open_edges"] = open,
                ["edges_of_more_than_two_triangles"] = crowded,
            };
            if (Degenerate > 0) result["degenerate_triangles_left_out"] = Degenerate;
            if (closed)
            {
                result["volume_mm3"] = Math.Round(Math.Abs(volume), 3);
                result["normals"] = volume >= 0 ? "outward" : "inward (the volume is given as a positive number)";
            }
            else
            {
                result["volume_mm3"] = null;
                result["volume_note"] = "The mesh is not closed (" + open + " open edges, " + crowded +
                                        " edges of more than two triangles): a volume computed on it would be a number with no meaning, so I give none.";
            }
            return result;
        }

        private static long EdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        // ── SECTION ─────────────────────────────────────────────────────

        /// <summary>The plane «extrude_profile» draws a section square to this axis on.</summary>
        public static string PlaneFor(int axis)
        {
            return axis == 0 ? "right" : axis == 1 ? "top" : "front";
        }

        /// <summary>
        /// A point of the model in the sketch coordinates of the plane square to
        /// <paramref name="axis"/> (measured, brief R7): front (X, Y), right (-Z, Y),
        /// top (X, -Z).
        /// </summary>
        public static double[] ToSketch(int axis, double[] p)
        {
            if (axis == 0) return new[] { -p[2], p[1] };
            if (axis == 1) return new[] { p[0], -p[2] };
            return new[] { p[0], p[1] };
        }

        /// <summary>One closed contour of a section.</summary>
        public sealed class Loop
        {
            /// <summary>The vertices in the sketch coordinates of the plane, not repeated at the end.</summary>
            public List<double[]> Points;
            /// <summary>Area with sign: positive counter-clockwise.</summary>
            public double SignedArea;
            /// <summary>How many other contours hold it: 0 is an outer edge, 1 a void in it, 2 an island in the void.</summary>
            public int Depth;
            public double Perimeter;
        }

        /// <summary>What a section found.</summary>
        public sealed class Cut
        {
            public int Axis;
            public double AtMm;
            public List<Loop> Loops = new List<Loop>();
            /// <summary>Chains that did not close: the mesh has a hole or a crowded edge at this height.</summary>
            public int OpenChains;
            /// <summary>Vertices before the simplification, all contours together.</summary>
            public int PointsBefore;
        }

        /// <summary>
        /// The section of the mesh by the plane square to <paramref name="axis"/>
        /// (0 = X, 1 = Y, 2 = Z) at <paramref name="atMm"/>, as closed contours in
        /// the sketch coordinates of its plane, each simplified so that no vertex
        /// left out is farther than <paramref name="simplifyMm"/> from the
        /// contour kept (Douglas-Peucker). Sorted by area, largest first, each
        /// with its depth; outer edges counter-clockwise, voids clockwise.
        ///
        /// A vertex exactly on the plane counts as above it, so every crossing
        /// edge is cut once, and the two triangles of an edge cut it at the same
        /// point: the contour closes on a closed mesh.
        /// </summary>
        public Cut Section(int axis, double atMm, double simplifyMm)
        {
            var cut = new Cut { Axis = axis, AtMm = atMm };
            var points = new Dictionary<long, double[]>();
            var ends = new List<long[]>();
            foreach (var t in Triangles)
            {
                var crossing = new List<long>(2);
                for (int k = 0; k < 3; k++)
                {
                    int p = t[k], q = t[(k + 1) % 3];
                    double dp = Vertices[p][axis] - atMm, dq = Vertices[q][axis] - atMm;
                    if ((dp >= 0) == (dq >= 0)) continue;
                    long key = EdgeKey(p, q);
                    if (!points.ContainsKey(key))
                    {
                        double s = dp / (dp - dq);
                        double[] a = Vertices[p], b = Vertices[q];
                        points[key] = new[] { a[0] + s * (b[0] - a[0]), a[1] + s * (b[1] - a[1]), a[2] + s * (b[2] - a[2]) };
                    }
                    crossing.Add(key);
                }
                if (crossing.Count == 2) ends.Add(new[] { crossing[0], crossing[1] });
            }

            var byPoint = new Dictionary<long, List<int>>();
            for (int i = 0; i < ends.Count; i++)
                foreach (long key in ends[i])
                {
                    List<int> list;
                    if (!byPoint.TryGetValue(key, out list))
                    {
                        list = new List<int>(2);
                        byPoint[key] = list;
                    }
                    list.Add(i);
                }

            var used = new bool[ends.Count];
            for (int start = 0; start < ends.Count; start++)
            {
                if (used[start]) continue;
                used[start] = true;
                long first = ends[start][0], current = ends[start][1];
                var chain = new List<double[]> { points[first] };
                bool closed = false;
                while (true)
                {
                    if (current == first) { closed = true; break; }
                    chain.Add(points[current]);
                    int next = -1;
                    foreach (int s in byPoint[current]) if (!used[s]) { next = s; break; }
                    if (next < 0) break;
                    used[next] = true;
                    current = ends[next][0] == current ? ends[next][1] : ends[next][0];
                }
                if (!closed) { cut.OpenChains++; continue; }
                var flat = new List<double[]>(chain.Count);
                foreach (var p in chain) flat.Add(ToSketch(axis, p));
                flat = WithoutRepeats(flat);
                cut.PointsBefore += flat.Count;
                var kept = SimplifyClosed(flat, Math.Max(0, simplifyMm));
                if (kept.Count < 3) continue;
                double area = SignedArea(kept);
                if (Math.Abs(area) < 1e-9) continue;
                List<Loop> found = cut.Loops;
                found.Add(new Loop { Points = kept, SignedArea = area, Perimeter = Perimeter(kept) });
            }

            foreach (var loop in cut.Loops)
            {
                int depth = 0;
                foreach (var other in cut.Loops)
                    if (!ReferenceEquals(other, loop) && Math.Abs(other.SignedArea) > Math.Abs(loop.SignedArea) &&
                        Inside(other.Points, loop.Points[0]))
                        depth++;
                loop.Depth = depth;
                bool wantCounterClockwise = depth % 2 == 0;
                if ((loop.SignedArea > 0) != wantCounterClockwise)
                {
                    loop.Points.Reverse();
                    loop.SignedArea = -loop.SignedArea;
                }
            }
            cut.Loops.Sort((a, b) => Math.Abs(b.SignedArea).CompareTo(Math.Abs(a.SignedArea)));
            return cut;
        }

        /// <summary>
        /// The contours grouped as «extrude_profile» takes them: each outer edge
        /// first, then the voids directly inside it. An island inside a void is an
        /// outer edge of its own, in another group.
        /// </summary>
        public static List<List<Loop>> Profiles(Cut cut)
        {
            var groups = new List<List<Loop>>();
            foreach (var outer in cut.Loops)
            {
                if (outer.Depth % 2 != 0) continue;
                var group = new List<Loop> { outer };
                foreach (var hole in cut.Loops)
                    if (hole.Depth == outer.Depth + 1 && Inside(outer.Points, hole.Points[0])) group.Add(hole);
                groups.Add(group);
            }
            return groups;
        }

        /// <summary>The area of the section: outer edges minus voids, plus islands.</summary>
        public static double SectionArea(Cut cut)
        {
            double sum = 0;
            foreach (var loop in cut.Loops) sum += loop.Depth % 2 == 0 ? Math.Abs(loop.SignedArea) : -Math.Abs(loop.SignedArea);
            return sum;
        }

        private static List<double[]> WithoutRepeats(List<double[]> p)
        {
            var r = new List<double[]>(p.Count);
            foreach (var q in p)
                if (r.Count == 0 || Distance(r[r.Count - 1], q) > 1e-9) r.Add(q);
            while (r.Count > 1 && Distance(r[0], r[r.Count - 1]) <= 1e-9) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>
        /// Douglas-Peucker on a closed contour: split at the vertex farthest from
        /// the first, simplify the two halves.
        /// </summary>
        internal static List<double[]> SimplifyClosed(List<double[]> p, double tolerance)
        {
            if (p.Count < 4) return new List<double[]>(p);
            int far = 0;
            double best = -1;
            for (int i = 1; i < p.Count; i++) { double d = Distance(p[0], p[i]); if (d > best) { best = d; far = i; } }
            var keep = new bool[p.Count + 1];
            keep[0] = keep[far] = keep[p.Count] = true;
            Func<int, double[]> at = i => p[i % p.Count];
            var stack = new Stack<int[]>();
            stack.Push(new[] { 0, far });
            stack.Push(new[] { far, p.Count });
            while (stack.Count > 0)
            {
                var span = stack.Pop();
                int a = span[0], b = span[1];
                if (b - a < 2) continue;
                int worst = -1;
                double worstDistance = -1;
                for (int i = a + 1; i < b; i++)
                {
                    double d = DistanceToSegment(at(i), at(a), at(b));
                    if (d > worstDistance) { worstDistance = d; worst = i; }
                }
                if (worstDistance > tolerance)
                {
                    keep[worst] = true;
                    stack.Push(new[] { a, worst });
                    stack.Push(new[] { worst, b });
                }
            }
            var r = new List<double[]>();
            for (int i = 0; i < p.Count; i++) if (keep[i]) r.Add(p[i]);
            return r;
        }

        private static double Distance(double[] a, double[] b)
        {
            double dx = a[0] - b[0], dy = a[1] - b[1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double DistanceToSegment(double[] p, double[] a, double[] b)
        {
            double dx = b[0] - a[0], dy = b[1] - a[1];
            double length2 = dx * dx + dy * dy;
            if (length2 <= 1e-18) return Distance(p, a);
            double s = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / length2;
            s = Math.Max(0, Math.Min(1, s));
            return Distance(p, new[] { a[0] + s * dx, a[1] + s * dy });
        }

        internal static double SignedArea(List<double[]> p)
        {
            double sum = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Count];
                sum += a[0] * b[1] - b[0] * a[1];
            }
            return sum / 2.0;
        }

        private static double Perimeter(List<double[]> p)
        {
            double sum = 0;
            for (int i = 0; i < p.Count; i++) sum += Distance(p[i], p[(i + 1) % p.Count]);
            return sum;
        }

        private static bool Inside(List<double[]> polygon, double[] q)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                double[] a = polygon[i], b = polygon[j];
                if ((a[1] > q[1]) != (b[1] > q[1]) &&
                    q[0] < (b[0] - a[0]) * (q[1] - a[1]) / (b[1] - a[1]) + a[0])
                    inside = !inside;
            }
            return inside;
        }

        public static double[] Round(double[] v, int digits)
        {
            var r = new double[v.Length];
            for (int i = 0; i < v.Length; i++) r[i] = Math.Round(v[i], digits);
            return r;
        }
    }
}
