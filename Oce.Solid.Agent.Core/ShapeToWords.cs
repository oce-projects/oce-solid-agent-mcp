using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE SHAPE OF THE PART, TOLD IN A SENTENCE (entry V3).
    ///
    /// The state lists up to sixty faces with area, point and normal. They are the
    /// right numbers, and it is a list: to know that the part is «a box
    /// 120x80x50 svuotata 4, con quattro fori Ø12» it has to be worked out. This
    /// class works it out once only, here, and says it.
    ///
    /// ── WHY IT CAN BE WORTH MORE THAN AN IMAGE ─────────────────────────────
    ///
    ///   * **it costs characters, not image tokens.** A view is over
    ///     a thousand tokens; this sentence is under three hundred CHARACTERS.
    ///   * **it survives a model that does not see images.**
    ///   * **it can be verified.** A sentence is either true or false, and the number
    ///     it carries is compared with the part. Of a PNG nothing is verified.
    ///   * **it stays readable by a person** inside a log, six months later.
    ///
    /// ── THE RULE THAT HOLDS EVERYTHING TOGETHER: NOTHING IS MADE UP ───────
    ///
    /// «It looks like a bracket» is not a measure. Every word of this sentence must
    /// come from a number that is in the state, and **where the number is not there the
    /// sentence is silent** — like `positions_not_checked`, which says «I cannot
    /// say» instead of guessing.
    ///
    /// Concretely: **the name of the family is written only when a TYPE of
    /// feature authorizes it.** `Shell` says «svuotato», `Revolution` says
    /// «di rivoluzione»: they are what the designer did, not what
    /// it looks like. For everything else the measures are given and that is all.
    ///
    /// «Plate» is not said because one dimension is small compared with the others:
    /// it would be a threshold invented by me, and on the day it is wrong — on a
    /// part that is not a plate — whoever reads believes it all the same, because the
    /// sentence sounds as sure as all the others. **A wrong sentence is worse
    /// than no sentence.**
    ///
    /// ── AND WHAT THE V2 MEASURE HAS ALREADY SAID ──────────────────────────
    ///
    /// Entry V3 took for granted that it served to make the shape UNDERSTOOD. The
    /// V2 measure showed that with the textual state alone a model gets
    /// the family **5 times out of 5**: understanding was not the problem. What the
    /// state alone did badly were the DERIVED numbers — three wrong centre distances
    /// with full confidence.
    ///
    /// So this sentence serves above all to **say the derived numbers
    /// once only and well**, instead of leaving each reader to work them out on their own:
    /// the hole centres, the pitch, the wall thickness. It is compression with
    /// a calculation already done inside.
    /// </summary>
    public static class ShapeToWords
    {
        private static readonly CultureInfo Point = CultureInfo.InvariantCulture;

        /// <summary>
        /// The sentence. Null when there is not enough to say one: **better
        /// nothing than an empty sentence that looks like a description**.
        /// </summary>
        public static string Describe(Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            if (!True(docState, "solid_body")) return null;

            var parts = new List<string>();

            // ── 1. HOW BIG IT IS, and how full it is ────────────────────
            //
            // The ratio between volume and bounding box says something true and useful
            // without classifying anything: 98% is solid, 23% is something
            // hollow or thin. It is a number, not an adjective.
            double[] ing = Vector(docState, "bounding_box_mm");
            double volume = Number(docState, "volume_mm3");
            if (ing != null && ing.Length == 3)
            {
                string measurements = Mm(ing[0]) + " x " + Mm(ing[1]) + " x " + Mm(ing[2]) + " mm";
                double box = ing[0] * ing[1] * ing[2];
                if (volume > 0 && box > 0)
                {
                    int full = (int)Math.Round(volume / box * 100.0);
                    parts.Add("Bounding box " + measurements + ", volume " + Mm(volume) +
                              " mm3 (that is " + full + "% of the bounding box)");
                }
                else parts.Add("Bounding box " + measurements);
            }
            else if (volume > 0) parts.Add("Volume " + Mm(volume) + " mm3");

            int bodies = (int)Number(docState, "bodies_total");
            if (bodies > 1) parts.Add(bodies + " separate bodies");

            // ── 2. WHAT HAS BEEN DONE ON IT ─────────────────────────────
            //
            // Only types that the designer chose: `Shell` and `Revolution`
            // say what the part is because what it is was decided by them.
            var feature = Entries(docState, "feature").ToList();
            if (feature.Any(f => Text(f, "kind") == "Revolution"))
            {
                parts.Add("part of revolution");
                string profile = TurnedProfile(docState, ing, volume);
                if (profile != null) parts.Add(profile);
            }

            var shell = feature.FirstOrDefault(f => Text(f, "kind") == "Shell");
            if (shell != null)
            {
                double wall = FirstDimension(shell);
                parts.Add(wall > 0
                    ? "hollowed, wall " + Mm(wall) + " mm"
                    : "hollowed");
            }

            // ── 3. THE HOLES, with the centre already calculated ─────────
            parts.AddRange(Holes(docState));

            // ── 4. WHAT COULD NOT BE SAID ───────────────────────────────
            //
            // A truncated list or features in error change the
            // sense of the whole sentence, and keeping quiet about them would make it a lie by
            // omission: whoever reads would believe they have the whole part.
            int outside = (int)Number(docState, "feature_not_listed");
            if (outside > 0)
                parts.Add("plus " + outside + " more features that are not in the list");
            int inError = (int)Number(docState, "features_in_error");
            if (inError > 0)
                parts.Add(inError + " feature" + (inError == 1 ? "" : "s") +
                          " IN ERROR: the part is not what the tree says");

            if (parts.Count == 0) return null;
            return string.Join(", ", parts) + ".";
        }

        /// <summary>
        /// THE PROFILE OF A TURNED PART: «Ø30 per 40, Ø20 per 30, Ø34 per 20».
        /// Null when it cannot be said — and it is the most important case.
        ///
        /// ── WHY IT EXISTS (entry V7) ──────────────────────────────────────
        ///
        /// In the V3 measure, four parts out of five were redrawn
        /// from the sentence alone with the **exact** volume. The fifth, a stepped
        /// shaft, came out at 3.8% — and the model had said in advance
        /// why: «it does not say how many steps the profile has, where the
        /// step falls, nor the intermediate diameters». The sentence said «pezzo di
        /// rivoluzione» and nothing else, that is the name of the family without a
        /// dimension.
        ///
        /// ── HOW IT IS DERIVED, AND FROM WHAT ─────────────────────────────
        ///
        /// The cylindrical faces of a turned part carry `diameter_mm`, `axis` and
        /// the area. The **length** of a stretch is not anywhere, but
        /// a cylinder of diameter d and length L has area **π·d·L**: the
        /// length is `area / (π·d)`.
        ///
        /// THE ORDER comes from `point_mm`, projected on the axis. It is a point
        /// on the surface and not on the axis — see E16 — but to ORDER
        /// it is enough: the stretches of a turned part do not overlap along the axis,
        /// so any point inside a stretch places it correctly. For a
        /// position it would not be enough, and indeed none are given.
        ///
        /// ── AND THE FORMULA IS FRAGILE, SO IT IS CHECKED ─────────────────
        ///
        /// `area / (π·d)` holds on a **whole** cylinder. If the face is
        /// cut by a flat, a keyway or a cross hole, the area
        /// is smaller and the length comes out **short** — and nobody would
        /// notice, because the number has the right shape. A part with a
        /// through hole would get a plausible and false profile: it is
        /// exactly the invented bolt circle, with another formula.
        ///
        /// So the profile is declared **only if it is reconstructed**. Two
        /// checks, and both must come back:
        ///
        ///   1. the sum of the lengths makes the bounding box along the axis;
        ///   2. the reconstructed volume — Σ π(d/2)²·L — makes the true volume.
        ///
        /// The second is the strong one: it catches the cross holes, the keyways, and a
        /// hollow part (where a cylindrical face is internal and removes material
        /// instead of adding it). If it does not come back, **it stays silent**: «pezzo di
        /// rivoluzione» remains, which is little but is true.
        /// </summary>
        private static string TurnedProfile(
            Dictionary<string, object> docState, double[] boundingBox, double volume)
        {
            if (boundingBox == null || boundingBox.Length != 3 || volume <= 0) return null;

            // ── the axis, and it must be only one ────────────────────────
            int alongWhich = -1;
            var strokes = new List<double[]>();   // {position, diameter, length}

            foreach (var f in Entries(docState, "faces"))
            {
                if (True(f, "planar")) continue;
                double d = Number(f, "diameter_mm");
                double area = Number(f, "area_mm2");
                var axis = Vector(f, "axis");
                var point = Vector(f, "point_mm");
                if (d <= 0 || area <= 0 || axis == null || point == null) return null;
                if (axis.Length != 3 || point.Length != 3) return null;

                // The axis must be aligned with X, Y or Z: a turned part slanted
                // with respect to the model axes cannot be told, and
                // trying would mean guessing.
                int which = -1;
                for (int i = 0; i < 3; i++)
                    if (Math.Abs(Math.Abs(axis[i]) - 1) < 1e-6) which = i;
                if (which < 0) return null;
                if (alongWhich < 0) alongWhich = which;
                else if (alongWhich != which) return null;   // two axes: it is not a simple turned part

                strokes.Add(new[] { point[which], d, area / (Math.PI * d) });
            }

            if (alongWhich < 0 || strokes.Count == 0) return null;

            strokes.Sort((a, b) => a[0].CompareTo(b[0]));

            // ── 1. do the lengths make the bounding box? ─────────────────
            double sum = strokes.Sum(t => t[2]);
            if (Math.Abs(sum - boundingBox[alongWhich]) > 0.05) return null;

            // ── 2. and does the volume come back? ────────────────────────
            double rebuilt = strokes.Sum(t => Math.PI * (t[1] / 2) * (t[1] / 2) * t[2]);
            if (Math.Abs(rebuilt - volume) > Math.Max(0.5, volume * 0.005)) return null;

            return "profile " + string.Join(", ",
                strokes.Select(t => Diam(Mm(t[1])) + " for " + Mm(t[2]))) + " mm";
        }

        /// <summary>
        /// The holes, grouped by diameter, with the CENTRE — not with the point on the
        /// surface.
        ///
        /// **It is the calculation that two models got wrong** (entry E16): from the
        /// `point_mm` of a cylindrical face, which lies on the surface, a
        /// centre distance comes out short by one radius per hole. A Ø90 bolt circle read
        /// Ø79. Here the calculation is done once, with `center_mm`, and whoever reads does not
        /// redo it.
        ///
        /// Only the cylindrical faces that come from a feature
        /// (`from_feature`) are counted: they are the holes and the round pockets. The corner
        /// fillets of a profile are cylindrical too and are not holes.
        /// </summary>
        private static IEnumerable<string> Holes(Dictionary<string, object> docState)
        {
            var perDiameter = new Dictionary<string, List<double[]>>();
            foreach (var f in Entries(docState, "faces"))
            {
                if (True(f, "planar")) continue;
                double d = Number(f, "diameter_mm");
                if (d <= 0) continue;
                if (string.IsNullOrEmpty(Text(f, "from_feature"))) continue;
                var center = Vector(f, "center_mm");
                if (center == null) continue;

                string key = Mm(d);
                if (!perDiameter.ContainsKey(key)) perDiameter[key] = new List<double[]>();
                perDiameter[key].Add(center);
            }

            foreach (var g in perDiameter.OrderByDescending(x => Convert.ToDouble(x.Key, Point)))
            {
                int howMany = g.Value.Count;
                string entry = howMany + (howMany == 1 ? " hole" : " holes") + " " + Diam(g.Key);

                // IF THEY LIE ON A CIRCLE, IT IS SAID.
                // It is the datum needed by whoever has to bolt, and it is precisely
                // the one that the list of faces alone led people to get wrong.
                double rimDiameter;
                if (howMany >= 3 && OnCircumference(g.Value, out rimDiameter))
                    entry += " on a circle " + Diam(Mm(rimDiameter)) +
                            " at a pitch of " + Mm(360.0 / howMany) + " degrees";
                else if (howMany <= 6)
                    entry += (howMany == 1 ? " (center " : " (centers ") + string.Join("; ",
                        g.Value.Select(c => Mm(c[0]) + "," + Mm(c[1]) + "," + Mm(c[2]))) + ")";

                yield return entry;
            }
        }

        /// <summary>
        /// Do the holes lie on a bolt circle, at a regular pitch?
        ///
        /// ── THREE CONDITIONS, AND THE FIRST TWO ALONE LIE ─────────────────
        ///
        /// The first writing asked for only one — «all at the same
        /// distance from an axis» — and on the **very first sentence ever generated**
        /// it declared that the four holes of a rectangular plate lie
        /// «su una circonferenza Ø90 a passo 90 gradi». They lie at the vertices of a
        /// **160 x 90** rectangle.
        ///
        /// Equidistant from an axis means on a CYLINDER, not on a
        /// circle: what was missing was that they all lie in the same plane. And
        /// lying on a circle does not mean lying on it **at regular
        /// pitch**: the vertices of a 160x90 rectangle lie on a circle,
        /// and their angles are 29.4° and 150.6° — not 90° from each other.
        ///
        /// So:
        ///
        ///   1. **same plane** — the coordinate along the axis is the same;
        ///   2. **same radius** from the axis;
        ///   3. **regular angular pitch**, which is the condition that the
        ///      rectangle does not meet and that neither of the other two captures.
        ///
        /// If any one does not come back, the sentence **says nothing** and leaves the
        /// centres: whoever reads does their own calculation on true numbers, instead of believing
        /// a wrong calculation made by me. A bolt circle declared where there is none
        /// is exactly the defect that this class exists not to have —
        /// and it is the same as E16, committed by the code written to repair it.
        /// </summary>
        private static bool OnCircumference(List<double[]> centers, out double diameter)
        {
            diameter = 0;
            if (centers.Count < 3) return false;

            for (int grounded = 0; grounded < 3; grounded++)
            {
                int a = (grounded + 1) % 3, b = (grounded + 2) % 3;

                // 1. same plane
                double dimension = centers[0][grounded];
                if (!centers.All(c => Math.Abs(c[grounded] - dimension) < 0.05)) continue;

                // 2. same radius
                double radius = Math.Sqrt(centers[0][a] * centers[0][a] + centers[0][b] * centers[0][b]);
                if (radius < 0.05) continue;
                if (!centers.All(c =>
                        Math.Abs(Math.Sqrt(c[a] * c[a] + c[b] * c[b]) - radius) < 0.05))
                    continue;

                // 3. regular angular pitch
                if (!RegularPitch(centers, a, b)) continue;

                diameter = radius * 2;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Are the angles equidistant from each other?
        ///
        /// They are sorted and the jumps are looked at: they must all equal
        /// 360/n. Half a degree of tolerance — a hole placed by hand on a
        /// real drawing is never exact to the hundredth, and half a degree on a
        /// Ø90 bolt circle is 0.4 mm.
        /// </summary>
        private static bool RegularPitch(List<double[]> centers, int a, int b)
        {
            var angles = centers
                .Select(c => Math.Atan2(c[b], c[a]) * 180.0 / Math.PI)
                .Select(g => g < 0 ? g + 360 : g)
                .OrderBy(g => g)
                .ToList();

            double expected = 360.0 / angles.Count;
            for (int i = 0; i < angles.Count; i++)
            {
                double skip = (i + 1 < angles.Count)
                    ? angles[i + 1] - angles[i]
                    : angles[0] + 360 - angles[i];
                if (Math.Abs(skip - expected) > 0.5) return false;
            }
            return true;
        }

        // ── readings from the state, all tolerant ────────────────────────

        private static IEnumerable<Dictionary<string, object>> Entries(
            Dictionary<string, object> docState, string key)
        {
            object g;
            if (docState == null || !docState.TryGetValue(key, out g) || g == null) yield break;
            var list = g as System.Collections.IEnumerable;
            if (list == null) yield break;
            foreach (var v in list)
            {
                var entry = v as Dictionary<string, object>;
                if (entry != null) yield return entry;
            }
        }

        private static double FirstDimension(Dictionary<string, object> feature)
        {
            foreach (var q in Entries(feature, "dimensions"))
            {
                double mm = Number(q, "mm");
                if (mm > 0) return mm;
            }
            return 0;
        }

        private static double Number(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, Point); } catch { return 0; }
        }

        private static string Text(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static bool True(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        private static double[] Vector(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            var ready = v as double[];
            if (ready != null) return ready;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try { outside.Add(Convert.ToDouble(x, Point)); } catch { return null; }
            }
            return outside.Count == 0 ? null : outside.ToArray();
        }

        /// <summary>A number as a draughtsman would write it: without useless zeros.</summary>
        private static string Mm(double v)
        {
            return v.ToString("0.##", Point);
        }

        private static string Diam(string measure)
        {
            return "Ø" + measure;
        }
    }
}
