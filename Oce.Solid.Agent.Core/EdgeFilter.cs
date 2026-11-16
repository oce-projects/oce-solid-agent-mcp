using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHICH EDGES THE STATE LISTS, when the reader does not want «the thirty
    /// longest» (brief R17).
    ///
    /// ── WHY IT EXISTS ────────────────────────────────────────────────────
    ///
    /// The state lists the 30 longest edges (<see cref="DocumentState.MaxEdges"/>)
    /// and nothing else. On 25/10/2026, on 26P000007, the edges to fillet were
    /// picked by hand among them (E3, E5, E13, E16, E17, E20, E29, E30), reading
    /// lengths and points one by one; an edge shorter than the thirtieth had no
    /// identifier at all. The faces had a filter since W3.1-bis.12 (<see
    /// cref="FaceFilter"/>); the edges get the same, with the conditions a
    /// designer uses to name them: the direction, straight or curved, the length,
    /// a point near them, the face they bound.
    ///
    /// ── THE IDENTIFIER OF AN EDGE IS ITS RANK ────────────────────────────
    ///
    /// Every edge of the part has a number: its place among ALL the edges,
    /// longest first (ties in the order of the bodies). E1 is the longest. The
    /// default reading lists E1..E30; a filtered reading lists the matching
    /// ones with the SAME numbers, and a plan that cites E45 finds the 45th
    /// longest edge of the reading it runs on (<see
    /// cref="DocumentState.TryGetSignature"/>), checked against the signature
    /// it was born with like any other.
    ///
    /// ── THE CONDITIONS ───────────────────────────────────────────────────
    ///
    ///   along          x, y or z: a straight edge along that axis of the model
    ///                  (the «along» the state writes). A diagonal matches none.
    ///   curved         true: only curved edges; false: only straight ones.
    ///   length_min_mm, length_max_mm   the length, in mm, inclusive.
    ///   near_mm        [x, y, z]: the edges whose closest point to it is within
    ///                  `within_mm` (default 1).
    ///   face           a face identifier (F3): the edges that bound that face.
    ///   body           a body identifier (C3) or the exact name of a body.
    ///   limit          how many to list at most, default 30, at most 500.
    ///
    /// All the conditions given must hold (an AND). Parsing and the choice live
    /// here, without SOLIDWORKS, so the contract suite checks them.
    /// </summary>
    public sealed class EdgeFilter
    {
        /// <summary>The default number of edges listed: the ceiling of the plain state.</summary>
        public const int DefaultLimit = DocumentState.MaxEdges;

        /// <summary>The most edges one filtered reading lists.</summary>
        public const int MaxLimit = 500;

        /// <summary>The default distance of <c>near_mm</c>, in mm.</summary>
        public const double DefaultWithinMm = 1.0;

        /// <summary>The keys the filter understands.</summary>
        public static readonly string[] Keys =
            { "along", "curved", "length_min_mm", "length_max_mm", "near_mm", "within_mm", "face", "body", "limit" };

        /// <summary>«X», «Y» or «Z», as the state writes «along»; null for no condition.</summary>
        public string Along;
        public bool? Curved;
        public double? LengthMinMm;
        public double? LengthMaxMm;
        public double[] NearMm;
        public double WithinMm = DefaultWithinMm;
        /// <summary>The face identifier (F3) whose edges are wanted.</summary>
        public string Face;
        public string Body;
        public int Limit = DefaultLimit;

        /// <summary>Whether the reader asked to see something other than the longest edges.</summary>
        public bool Narrows
        {
            get
            {
                return Along != null || Curved.HasValue || LengthMinMm.HasValue || LengthMaxMm.HasValue ||
                       NearMm != null || Face != null || Body != null;
            }
        }

        /// <summary>
        /// The filter written in an argument, or null with the reason. Nothing is
        /// guessed: an unknown key, a direction that is not x, y or z, a «curved»
        /// that is not true or false, a number that is not one, an empty range, a
        /// «within_mm» without a point, «along» with «curved» true (a curved edge
        /// has no direction), a face that is not an identifier: each refused
        /// with what was expected.
        /// </summary>
        public static EdgeFilter Parse(object raw, out string problem)
        {
            problem = null;
            var map = raw as IDictionary<string, object>;
            if (map == null)
            {
                problem = "«edges» must be an object with some of: " + string.Join(", ", Keys) + ".";
                return null;
            }
            foreach (string key in map.Keys)
                if (Array.IndexOf(Keys, key) < 0)
                {
                    problem = "«edges» has no «" + key + "». It takes: " + string.Join(", ", Keys) + ".";
                    return null;
                }

            var filter = new EdgeFilter();
            object v;
            if (map.TryGetValue("along", out v) && v != null)
            {
                var word = (v as string ?? "").Trim().ToUpperInvariant();
                if (word != "X" && word != "Y" && word != "Z")
                {
                    problem = "«edges.along» is «x», «y» or «z» (the axis a straight edge runs along), not «" +
                              Convert.ToString(v, CultureInfo.InvariantCulture) + "».";
                    return null;
                }
                filter.Along = word;
            }
            if (map.TryGetValue("curved", out v) && v != null)
            {
                if (!(v is bool))
                {
                    problem = "«edges.curved» is true (only curved edges) or false (only straight ones).";
                    return null;
                }
                filter.Curved = (bool)v;
            }
            if (filter.Along != null && filter.Curved.GetValueOrDefault())
            {
                problem = "«edges.along» names the direction of a STRAIGHT edge, and «edges.curved» true asks for curved ones: " +
                          "no edge can match both. Pass one of the two.";
                return null;
            }
            if (!Number(map, "length_min_mm", 0, double.MaxValue, out filter.LengthMinMm, out problem)) return null;
            if (!Number(map, "length_max_mm", 0, double.MaxValue, out filter.LengthMaxMm, out problem)) return null;
            if (filter.LengthMinMm.HasValue && filter.LengthMaxMm.HasValue &&
                filter.LengthMinMm.GetValueOrDefault() > filter.LengthMaxMm.GetValueOrDefault())
            {
                problem = "«edges.length_min_mm» (" + Text(filter.LengthMinMm.GetValueOrDefault()) + ") is larger than «edges.length_max_mm» (" +
                          Text(filter.LengthMaxMm.GetValueOrDefault()) + "): no edge can match. Write the smaller one first.";
                return null;
            }
            if (map.TryGetValue("near_mm", out v) && v != null)
            {
                var point = Triplet(v);
                if (point == null)
                {
                    problem = "«edges.near_mm» is written as three numbers [x, y, z], in mm.";
                    return null;
                }
                filter.NearMm = point;
            }
            double? within;
            if (!Number(map, "within_mm", 0, 1e6, out within, out problem)) return null;
            if (within.HasValue)
            {
                if (filter.NearMm == null)
                {
                    problem = "«edges.within_mm» is the distance from «edges.near_mm»: give the point too.";
                    return null;
                }
                if (within.GetValueOrDefault() <= 0)
                {
                    problem = "«edges.within_mm» must be above 0 (the default is " + Text(DefaultWithinMm) + "): write a positive distance.";
                    return null;
                }
                filter.WithinMm = within.GetValueOrDefault();
            }
            if (map.TryGetValue("face", out v) && v != null)
            {
                var uid = (v as string ?? "").Trim();
                if (FaceFilter.RankOf(uid) < 1)
                {
                    problem = "«edges.face» is a face identifier as the state writes it (F3), not «" +
                              Convert.ToString(v, CultureInfo.InvariantCulture) + "».";
                    return null;
                }
                filter.Face = "F" + FaceFilter.RankOf(uid).ToString(CultureInfo.InvariantCulture);
            }
            if (map.TryGetValue("body", out v) && v != null)
            {
                var name = (v as string ?? "").Trim();
                if (name.Length == 0)
                {
                    problem = "«edges.body» is written as a body identifier (C3) or the name of a body.";
                    return null;
                }
                filter.Body = name;
            }
            double? limit;
            if (!Number(map, "limit", 1, MaxLimit, out limit, out problem)) return null;
            if (limit.HasValue)
            {
                if (Math.Abs(limit.GetValueOrDefault() - Math.Round(limit.GetValueOrDefault())) > 1e-9)
                {
                    problem = "«edges.limit» is written as a whole number from 1 to " + MaxLimit + ".";
                    return null;
                }
                filter.Limit = (int)Math.Round(limit.GetValueOrDefault());
            }
            return filter;
        }

        private static bool Number(IDictionary<string, object> map, string key, double low, double high,
                                   out double? value, out string problem)
        {
            value = null;
            problem = null;
            object v;
            if (!map.TryGetValue(key, out v) || v == null) return true;
            double d;
            if (v is bool || v is string || !(v is IConvertible))
            {
                problem = "«edges." + key + "» is written as a number.";
                return false;
            }
            try { d = Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { problem = "«edges." + key + "» is written as a number."; return false; }
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                problem = "«edges." + key + "» is written as a number.";
                return false;
            }
            if (d < low || d > high)
            {
                problem = "«edges." + key + "» is written from " + Text(low) +
                          (high < double.MaxValue ? " to " + Text(high) : " up") + ", not " + Text(d) + ".";
                return false;
            }
            value = d;
            return true;
        }

        private static double[] Triplet(object v)
        {
            var list = v as IEnumerable;
            if (list == null || v is string) return null;
            var numbers = new List<double>();
            foreach (var item in list)
            {
                if (item == null || item is bool || item is string || !(item is IConvertible)) return null;
                double d;
                try { d = Convert.ToDouble(item, CultureInfo.InvariantCulture); }
                catch (Exception) { return null; }
                if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                numbers.Add(d);
            }
            return numbers.Count == 3 ? numbers.ToArray() : null;
        }

        private static string Text(double d)
        {
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The index of the body «body» names, as <see cref="FaceFilter.BodyIndex"/>
        /// reads it (identifier first, then the exact name): -2 without a
        /// condition, -1 when it names none.
        /// </summary>
        public int BodyIndex(IList<string> bodyNames)
        {
            if (Body == null) return -2;
            return new FaceFilter { Body = Body }.BodyIndex(bodyNames);
        }

        /// <summary>The filter as the state echoes it: only what was asked.</summary>
        public Dictionary<string, object> Describe()
        {
            var d = new Dictionary<string, object>();
            if (Along != null) d["along"] = Along.ToLowerInvariant();
            if (Curved.HasValue) d["curved"] = Curved.GetValueOrDefault();
            if (LengthMinMm.HasValue) d["length_min_mm"] = LengthMinMm.GetValueOrDefault();
            if (LengthMaxMm.HasValue) d["length_max_mm"] = LengthMaxMm.GetValueOrDefault();
            if (NearMm != null) { d["near_mm"] = NearMm; d["within_mm"] = WithinMm; }
            if (Face != null) d["face"] = Face;
            if (Body != null) d["body"] = Body;
            d["limit"] = Limit;
            return d;
        }

        /// <summary>
        /// The 1-based rank an edge identifier names («E45» is 45), or 0 when it
        /// is not one in its canonical spelling (E + the digits of a positive
        /// number, no sign and no leading zero).
        /// </summary>
        public static int RankOf(string uid)
        {
            if (string.IsNullOrEmpty(uid) || uid.Length < 2 || (uid[0] != 'E' && uid[0] != 'e')) return 0;
            int rank;
            if (!int.TryParse(uid.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out rank) || rank < 1) return 0;
            return string.Equals(uid.Substring(1), rank.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ? rank : 0;
        }

        /// <summary>
        /// Which of `count` edges, taken in the order given (longest first), match
        /// the filter, as 0-based positions. Each property of an edge is asked
        /// for ONLY when a condition needs it, cheapest first: the body, the
        /// length, the curve (straight, along which axis), the faces it bounds,
        /// the closest point — the last three are COM calls, not paid for an edge
        /// already refused. `along` is «X», «Y», «Z» or null (a diagonal) for a
        /// straight edge; `curvedOf` true for a curved one. `matched` is how many
        /// match; the list is cut at the limit and keeps the first (longest).
        /// </summary>
        public List<int> Choose(int count, Func<int, int> bodyOf, Func<int, double?> lengthMmOf,
                                Func<int, bool> curvedOf, Func<int, string> alongOf,
                                Func<int, bool> boundsFace, Func<int, double?> distanceMmOf,
                                int wantedBody, out int matched)
        {
            var list = new List<int>();
            matched = 0;
            for (int i = 0; i < count; i++)
            {
                if (wantedBody >= 0 && bodyOf(i) != wantedBody) continue;
                if (LengthMinMm.HasValue || LengthMaxMm.HasValue)
                {
                    var length = lengthMmOf(i);
                    if (!length.HasValue) continue;
                    if (LengthMinMm.HasValue && length.GetValueOrDefault() < LengthMinMm.GetValueOrDefault() - 1e-9) continue;
                    if (LengthMaxMm.HasValue && length.GetValueOrDefault() > LengthMaxMm.GetValueOrDefault() + 1e-9) continue;
                }
                if (Curved.HasValue || Along != null)
                {
                    bool curved = curvedOf(i);
                    if (Curved.HasValue && curved != Curved.GetValueOrDefault()) continue;
                    if (Along != null && (curved || !string.Equals(alongOf(i), Along, StringComparison.Ordinal))) continue;
                }
                if (Face != null && !boundsFace(i)) continue;
                if (NearMm != null)
                {
                    var distance = distanceMmOf(i);
                    if (!distance.HasValue || distance.GetValueOrDefault() > WithinMm + 1e-9) continue;
                }
                matched++;
                if (list.Count < Limit) list.Add(i);
            }
            return list;
        }
    }
}
