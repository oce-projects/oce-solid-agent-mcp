using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHICH FACES THE STATE LISTS, when the reader does not want «the sixty
    /// largest» (entry W3.1-bis.12, faces beyond the 60 largest).
    ///
    /// ── WHY IT EXISTS ────────────────────────────────────────────────────
    ///
    /// The state lists the 60 largest faces and says `truncated`. On the tube
    /// stand of task 07 (17 solids, 177 faces) the wall of the third and the
    /// fourth foot is not among them: 32 walls tie at 4,800 mm2 behind 40
    /// larger faces of the frame, and the ceiling cuts them in the order of the
    /// bodies. A face that is not listed has no identifier, so no plan and no
    /// model can name it, and the weldment cannot be finished.
    ///
    /// Raising the ceiling was measured and refused: the faces are 8,718 of the
    /// 16,000 characters the state may cost (<see cref="DocumentState.MaxEdges"/>).
    /// What the reader needs is not more faces, it is THE faces it is looking
    /// for. So the ceiling stays where it is for the default reading, and
    /// `inspect_part` takes a filter: the state then lists the faces that match
    /// it instead of the largest ones.
    ///
    /// ── THE IDENTIFIER OF A FACE IS ITS RANK ─────────────────────────────
    ///
    /// Every face of the part has a number: its place in the list of ALL the
    /// faces, largest first (ties in the order of the bodies, then of the body's
    /// own list). F1 is the largest, F177 the smallest of 177. The default
    /// reading lists F1..F60; a filtered reading lists the matching ones with
    /// the SAME numbers, so «F70» means the same face in both. The numbers are
    /// positional, as they always were: they hold for the reading that gave
    /// them, and the guards that protect F1..F60 (the signature that travels
    /// with the plan, the persist ID) protect the others in the same way.
    ///
    /// ── THE CONDITIONS ───────────────────────────────────────────────────
    ///
    ///   normal        up, down, front, back, left, right: the same words as
    ///                 the `facing` of a listed face. Only the six axis-aligned
    ///                 directions; a face that looks elsewhere has none and
    ///                 matches no `normal`.
    ///   area_min_mm2, area_max_mm2   the area, in mm2, inclusive.
    ///   near_mm       [x, y, z]: a point of the model, in mm. The face matches
    ///                 when its closest point to it is within `within_mm`
    ///                 (default 1) — the distance to the FACE, not to the
    ///                 point the state writes as `point_mm`, which is the
    ///                 point of the face closest to the ORIGIN.
    ///   body          a body identifier (C3) or the exact name of a body.
    ///   limit         how many to list at most, default 60, at most 500. When
    ///                 more faces match the largest are listed and the state
    ///                 says how many were left out.
    ///
    /// All the conditions given must hold (they are an AND). No conditions and
    /// no limit is the default reading.
    ///
    /// Parsing and the decisions that need no SOLIDWORKS live here, so that
    /// Oce.Solid.Agent.ContractTests checks them without it.
    /// </summary>
    public sealed class FaceFilter
    {
        /// <summary>The default number of faces listed: the ceiling of the plain state.</summary>
        public const int DefaultLimit = DocumentState.MaxFaces;

        /// <summary>The most faces one filtered reading lists. 500 faces are ~72 KB of state.</summary>
        public const int MaxLimit = 500;

        /// <summary>The default distance of <c>near_mm</c>, in mm.</summary>
        public const double DefaultWithinMm = 1.0;

        /// <summary>The words of `normal`: the ones `facing` is written with.</summary>
        public static readonly string[] Facings = { "up", "down", "front", "back", "left", "right" };

        /// <summary>The keys the filter understands.</summary>
        public static readonly string[] Keys =
            { "normal", "area_min_mm2", "area_max_mm2", "near_mm", "within_mm", "body", "limit" };

        public string Normal;
        public double? AreaMinMm2;
        public double? AreaMaxMm2;
        public double[] NearMm;
        public double WithinMm = DefaultWithinMm;
        public string Body;
        public int Limit = DefaultLimit;

        /// <summary>Whether the reader asked to see something other than the largest faces.</summary>
        public bool Narrows
        {
            get { return Normal != null || AreaMinMm2.HasValue || AreaMaxMm2.HasValue || NearMm != null || Body != null; }
        }

        /// <summary>
        /// The filter written in an argument, or null with the reason. Nothing
        /// is guessed: an unknown key, a word that is not a direction, a number
        /// that is not one, an empty range, a `within_mm` without a point are
        /// each refused with what was expected — a filter that silently ignored
        /// one of its conditions would list faces the reader did not ask for.
        /// </summary>
        public static FaceFilter Parse(object raw, out string problem)
        {
            problem = null;
            var map = raw as IDictionary<string, object>;
            if (map == null)
            {
                problem = "«faces» must be an object with some of: " + string.Join(", ", Keys) + ".";
                return null;
            }
            var filter = new FaceFilter();
            foreach (var key in map.Keys)
            {
                if (Array.IndexOf(Keys, key) < 0)
                {
                    problem = "«faces» has no «" + key + "». It takes: " + string.Join(", ", Keys) + ".";
                    return null;
                }
            }

            object v;
            if (map.TryGetValue("normal", out v) && v != null)
            {
                var word = (v as string ?? "").Trim().ToLowerInvariant();
                if (Array.IndexOf(Facings, word) < 0)
                {
                    problem = "«faces.normal» must be one of " + string.Join(", ", Facings) +
                              " (the words of «facing»), not «" + Convert.ToString(v, CultureInfo.InvariantCulture) + "».";
                    return null;
                }
                filter.Normal = word;
            }
            if (!Number(map, "area_min_mm2", 0, double.MaxValue, out filter.AreaMinMm2, out problem)) return null;
            if (!Number(map, "area_max_mm2", 0, double.MaxValue, out filter.AreaMaxMm2, out problem)) return null;
            if (filter.AreaMinMm2.HasValue && filter.AreaMaxMm2.HasValue && filter.AreaMinMm2.GetValueOrDefault() > filter.AreaMaxMm2.GetValueOrDefault())
            {
                problem = "«faces.area_min_mm2» (" + Text(filter.AreaMinMm2.GetValueOrDefault()) + ") is larger than «faces.area_max_mm2» (" +
                          Text(filter.AreaMaxMm2.GetValueOrDefault()) + "): no face can match.";
                return null;
            }
            if (map.TryGetValue("near_mm", out v) && v != null)
            {
                var point = Triplet(v);
                if (point == null)
                {
                    problem = "«faces.near_mm» must be three numbers [x, y, z], in mm.";
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
                    problem = "«faces.within_mm» is the distance from «faces.near_mm»: give the point too.";
                    return null;
                }
                if (within.GetValueOrDefault() <= 0)
                {
                    problem = "«faces.within_mm» must be above 0 (the default is " + Text(DefaultWithinMm) + ").";
                    return null;
                }
                filter.WithinMm = within.GetValueOrDefault();
            }
            if (map.TryGetValue("body", out v) && v != null)
            {
                var name = (v as string ?? "").Trim();
                if (name.Length == 0)
                {
                    problem = "«faces.body» must be a body identifier (C3) or the name of a body.";
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
                    problem = "«faces.limit» must be a whole number from 1 to " + MaxLimit + ".";
                    return null;
                }
                filter.Limit = (int)Math.Round(limit.GetValueOrDefault());
            }
            return filter;
        }

        /// <summary>A number from the map within [low, high], or the reason. Absent or null is fine.</summary>
        private static bool Number(IDictionary<string, object> map, string key, double low, double high,
                                   out double? value, out string problem)
        {
            value = null;
            problem = null;
            object v;
            if (!map.TryGetValue(key, out v) || v == null) return true;
            double d;
            if (!ToDouble(v, out d) || double.IsNaN(d) || double.IsInfinity(d))
            {
                problem = "«faces." + key + "» must be a number.";
                return false;
            }
            if (d < low || d > high)
            {
                problem = "«faces." + key + "» must be from " + Text(low) +
                          (high < double.MaxValue ? " to " + Text(high) : " up") + ", not " + Text(d) + ".";
                return false;
            }
            value = d;
            return true;
        }

        private static bool ToDouble(object v, out double d)
        {
            d = 0;
            if (v is bool || v is string || v == null) return false;
            try { d = Convert.ToDouble(v, CultureInfo.InvariantCulture); return true; }
            catch (Exception) { return false; }
        }

        private static double[] Triplet(object v)
        {
            var list = v as IEnumerable;
            if (list == null || v is string) return null;
            var numbers = new List<double>();
            foreach (var item in list)
            {
                double d;
                if (!ToDouble(item, out d) || double.IsNaN(d) || double.IsInfinity(d)) return null;
                numbers.Add(d);
            }
            return numbers.Count == 3 ? numbers.ToArray() : null;
        }

        private static string Text(double d)
        {
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Whether an area (mm2) is inside the range given. Faces whose area could not be read never match a range.</summary>
        public bool AreaAllows(double? areaMm2)
        {
            if (!AreaMinMm2.HasValue && !AreaMaxMm2.HasValue) return true;
            if (!areaMm2.HasValue) return false;
            if (AreaMinMm2.HasValue && areaMm2.GetValueOrDefault() < AreaMinMm2.GetValueOrDefault() - 1e-9) return false;
            if (AreaMaxMm2.HasValue && areaMm2.GetValueOrDefault() > AreaMaxMm2.GetValueOrDefault() + 1e-9) return false;
            return true;
        }

        /// <summary>Whether a facing word (up, down, ..., or null for none) is the one asked for.</summary>
        public bool FacingAllows(string facing)
        {
            return Normal == null || string.Equals(Normal, facing, StringComparison.Ordinal);
        }

        /// <summary>Whether a distance (mm) from `near_mm` is within `within_mm`. Unread distances never match.</summary>
        public bool NearAllows(double? distanceMm)
        {
            if (NearMm == null) return true;
            return distanceMm.HasValue && distanceMm.GetValueOrDefault() <= WithinMm + 1e-9;
        }

        /// <summary>
        /// The index of the body a `body` condition names, from the body
        /// identifiers (C1..) and names in the order of the state; -1 when it
        /// names none. An identifier is tried first, then the exact name.
        /// </summary>
        public int BodyIndex(IList<string> bodyNames)
        {
            if (Body == null) return -2;
            if (bodyNames == null) return -1;
            int rank;
            if (Body.Length > 1 && (Body[0] == 'C' || Body[0] == 'c') &&
                int.TryParse(Body.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out rank) &&
                rank >= 1 && rank <= bodyNames.Count &&
                string.Equals(Body.Substring(1), rank.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                return rank - 1;
            for (int i = 0; i < bodyNames.Count; i++)
                if (string.Equals(bodyNames[i], Body, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The filter as the state echoes it: only what was asked.</summary>
        public Dictionary<string, object> Describe()
        {
            var d = new Dictionary<string, object>();
            if (Normal != null) d["normal"] = Normal;
            if (AreaMinMm2.HasValue) d["area_min_mm2"] = AreaMinMm2.GetValueOrDefault();
            if (AreaMaxMm2.HasValue) d["area_max_mm2"] = AreaMaxMm2.GetValueOrDefault();
            if (NearMm != null) { d["near_mm"] = NearMm; d["within_mm"] = WithinMm; }
            if (Body != null) d["body"] = Body;
            d["limit"] = Limit;
            return d;
        }

        /// <summary>
        /// The 1-based rank a face identifier names («F70» is 70), or 0 when it
        /// is not an identifier of a face in its canonical spelling (F + the
        /// digits of a positive number, no sign and no leading zero).
        /// </summary>
        public static int RankOf(string uid)
        {
            if (string.IsNullOrEmpty(uid) || uid.Length < 2 || (uid[0] != 'F' && uid[0] != 'f')) return 0;
            int rank;
            if (!int.TryParse(uid.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out rank) || rank < 1) return 0;
            return string.Equals(uid.Substring(1), rank.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ? rank : 0;
        }

        /// <summary>
        /// Which of `count` faces, taken in the order given (largest first),
        /// match the filter, as 0-based positions. The facing, the area, the
        /// distance to `near_mm` and the body index of a face are asked for
        /// ONLY when a condition needs them and in that order — cheapest first,
        /// a face already refused by its body or area is not asked for its
        /// normal or its closest point, which are COM calls. Pure, so that the
        /// contract tests check the rule without SOLIDWORKS. `matched` is how
        /// many match; the list is cut at the limit and keeps the first ones.
        /// </summary>
        public List<int> Choose(int count, Func<int, int> bodyOf, Func<int, double?> areaMm2Of,
                                Func<int, string> facingOf, Func<int, double?> distanceMmOf,
                                int wantedBody, out int matched)
        {
            var list = new List<int>();
            matched = 0;
            for (int i = 0; i < count; i++)
            {
                if (wantedBody >= 0 && bodyOf(i) != wantedBody) continue;
                if ((AreaMinMm2.HasValue || AreaMaxMm2.HasValue) && !AreaAllows(areaMm2Of(i))) continue;
                if (Normal != null && !FacingAllows(facingOf(i))) continue;
                if (NearMm != null && !NearAllows(distanceMmOf(i))) continue;
                matched++;
                if (list.Count < Limit) list.Add(i);
            }
            return list;
        }
    }
}
