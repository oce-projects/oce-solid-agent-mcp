using System;
using System.Collections.Generic;
using System.Globalization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE STANDARD SECTIONS: from two or three dimensions to the contours to extrude.
    ///
    /// It is the recipe of `extrude_section`. It lived only in the Python server
    /// (`section_profile`), so from the bridge the operation was **declared in the
    /// catalogue and not executable**: the model could ask for it and got
    /// «unknown operation».
    ///
    /// EVERY SECTION DECLARES WHERE THE ORIGIN FALLS, and it is not a detail: it is the
    /// datum from which all the later features will start. An angle with
    /// the origin at the centre instead of at the inner corner shifts every hole.
    ///
    /// THE CHECKS OF THIS FAMILY ARE PHYSICAL, not of form, and each one
    /// exists for a defect that SOLIDWORKS does not report.
    /// </summary>
    public static class Sections
    {
        /// <summary>
        /// Segments for each semicircle of the slot.
        ///
        /// **It is not a parameter**, and it is intended: if it were, it could be
        /// lowered and an octagonal slot obtained without anybody
        /// noticing.
        /// </summary>
        public const int SemicircleSegments = 24;

        /// <summary>The sections that can be made, and which dimensions they want.</summary>
        public static readonly Dictionary<string, string[]> Wanted =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["tubular"] = new[] { "width_mm", "height_mm", "wall_mm" },
                ["round"] = new[] { "diameter_mm" },
                ["tube"] = new[] { "diameter_mm", "wall_mm" },
                ["angle"] = new[] { "width_mm", "height_mm", "thickness_mm" },
                ["u"] = new[] { "width_mm", "height_mm", "thickness_mm" },
                ["t"] = new[] { "width_mm", "height_mm", "thickness_mm" },
                ["flat"] = new[] { "width_mm", "height_mm" },
                ["hexagon"] = new[] { "across_flats_mm" },
                ["slot"] = new[] { "width_mm", "height_mm" },
            };

        /// <summary>
        /// The contours of a section, or null with the reason.
        ///
        /// The contours come out in the same form that the parser already understands, and
        /// end up in the same executor as `extrude_profile`: a section
        /// is not another operation, it is a way of writing contours.
        /// </summary>
        public static List<object> Contours(
            string typ, Dictionary<string, double> m, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(typ))
            {
                reason = "You did not say which section: " + Items() + ".";
                return null;
            }

            switch (typ.Trim().ToLowerInvariant())
            {
                case "tubular":
                {
                    double b = m["width_mm"], h = m["height_mm"], t = m["wall_mm"];
                    reason = WallIsInside(t, Math.Min(b, h), "tubular");
                    if (reason != null) return null;
                    // Origin at the centre. Two concentric rectangles: the outer
                    // bounding box is written in the coordinates, and does not depend on a
                    // wall direction.
                    return new List<object>
                    {
                        Polygonal(Rectangle(b, h)),
                        Polygonal(Rectangle(b - 2 * t, h - 2 * t)),
                    };
                }

                case "round":
                    return new List<object> { Circle(0, 0, m["diameter_mm"]) };

                case "tube":
                {
                    double d = m["diameter_mm"], t = m["wall_mm"];
                    reason = WallIsInside(t, d, "tube");
                    if (reason != null) return null;
                    return new List<object>
                    {
                        Circle(0, 0, d),
                        Circle(0, 0, d - 2 * t),
                    };
                }

                case "flat":
                    return new List<object>
                    {
                        Polygonal(Rectangle(m["width_mm"], m["height_mm"])),
                    };

                case "hexagon":
                {
                    // DIMENSIONED ACROSS FLATS, which is how it is bought and
                    // how it is measured with an open-ended spanner. The circumscribed
                    // diameter is another thing, and confusing them gives a
                    // hexagon 15% bigger.
                    double c = m["across_flats_mm"];
                    double r = c / Math.Sqrt(3.0);
                    var points = new List<double[]>();
                    for (int i = 0; i < 6; i++)
                    {
                        double angle = Math.PI / 6 + i * Math.PI / 3;
                        points.Add(new[] { Q(r * Math.Cos(angle)), Q(r * Math.Sin(angle)) });
                    }
                    return new List<object> { Polygonal(points) };
                }

                case "angle":
                {
                    double b = m["width_mm"], h = m["height_mm"], t = m["thickness_mm"];
                    reason = ThicknessIsInside(t, Math.Min(b, h), "a flange", "a flat bar");
                    if (reason != null) return null;
                    // Origin on the INNER CORNER: it is a point that exists on the
                    // part and that in the workshop can be measured. The flanges grow toward
                    // +X and +Y.
                    return new List<object>
                    {
                        Polygonal(new List<double[]>
                        {
                            new[] { Q(0), Q(0) },
                            new[] { Q(b), Q(0) },
                            new[] { Q(b), Q(t) },
                            new[] { Q(t), Q(t) },
                            new[] { Q(t), Q(h) },
                            new[] { Q(0), Q(h) },
                        }),
                    };
                }

                case "u":
                {
                    double b = m["width_mm"], h = m["height_mm"], t = m["thickness_mm"];
                    reason = ThicknessIsInside(2 * t, b, "the web", "a flat bar")
                             ?? ThicknessIsInside(t, h, "a flange", "a flat bar");
                    if (reason != null) return null;
                    // Origin on the outer bottom-left corner: the two
                    // flanges go up.
                    return new List<object>
                    {
                        Polygonal(new List<double[]>
                        {
                            new[] { Q(0), Q(0) },
                            new[] { Q(b), Q(0) },
                            new[] { Q(b), Q(h) },
                            new[] { Q(b - t), Q(h) },
                            new[] { Q(b - t), Q(t) },
                            new[] { Q(t), Q(t) },
                            new[] { Q(t), Q(h) },
                            new[] { Q(0), Q(h) },
                        }),
                    };
                }

                case "t":
                {
                    double b = m["width_mm"], h = m["height_mm"], t = m["thickness_mm"];
                    reason = ThicknessIsInside(t, b, "the flange", "a flat bar")
                             ?? ThicknessIsInside(t, h, "the web", "a flat bar");
                    if (reason != null) return null;
                    // Origin at the centre of the top flange: the web goes down
                    // toward -Y.
                    double mb = b / 2.0, mt = t / 2.0;
                    return new List<object>
                    {
                        Polygonal(new List<double[]>
                        {
                            new[] { Q(-mb), Q(0) },
                            new[] { Q(mb), Q(0) },
                            new[] { Q(mb), Q(-t) },
                            new[] { Q(mt), Q(-t) },
                            new[] { Q(mt), Q(-h) },
                            new[] { Q(-mt), Q(-h) },
                            new[] { Q(-mt), Q(-t) },
                            new[] { Q(-mb), Q(-t) },
                        }),
                    };
                }

                case "slot":
                {
                    double b = m["width_mm"], h = m["height_mm"];
                    if (b <= h)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "A slot {0:0.###}x{1:0.###}: the length must be " +
                            "greater than the width, otherwise it is a circle.", b, h);
                        return null;
                    }
                    double r = h / 2.0;
                    double center = b / 2.0 - r;

                    // THE POINTS LIE ON THE CIRCUMSCRIBED CIRCLE, not on the
                    // nominal one. A slot with inscribed semicircles is NARROWER
                    // than the nominal, and a screw that has to pass through does not
                    // pass. Where one approximates, one approximates in excess.
                    double rp = r / Math.Cos(Math.PI / (2 * SemicircleSegments));
                    var points = new List<double[]>();
                    for (int i = 0; i <= SemicircleSegments; i++)
                    {
                        double angle = -Math.PI / 2 + Math.PI * i / SemicircleSegments;
                        points.Add(new[] { Q(center + rp * Math.Cos(angle)), Q(rp * Math.Sin(angle)) });
                    }
                    for (int i = 0; i <= SemicircleSegments; i++)
                    {
                        double angle = Math.PI / 2 + Math.PI * i / SemicircleSegments;
                        points.Add(new[] { Q(-center + rp * Math.Cos(angle)), Q(rp * Math.Sin(angle)) });
                    }
                    return new List<object> { Polygonal(points) };
                }
            }

            reason = "Section «" + typ + "» unknown. There are: " + Items() + ".";
            return null;
        }

        private static string Items()
        {
            return string.Join(", ", Wanted.Keys);
        }

        // ─────────────────────────────────────────────────────────────────
        //  The physical checks
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// THE WALL MUST BE STRICTLY UNDER HALF THE SHORTER SIDE.
        ///
        /// The reason is not that «half is too much»: **at exactly half the cavity
        /// is not there**. On an 80x80 tube with wall 40 the inner contour
        /// becomes four coincident points, zero area. SOLIDWORKS does not report
        /// it — a void of null area is not an error, for it — and
        /// extrudes a SOLID BAR: an 80x80x5 tube one metre long weighs
        /// 11.8 kg, the solid bar weighs 50. **Four times the weight, green
        /// feature, clean rebuild.**
        ///
        /// Above half the inner contour turns inside out: the contour check
        /// would catch it too, but with a message that talks about a «void
        /// that overflows» to whoever wrote a wall. A refusal that does not name
        /// the wrong dimension is a refusal that repeats itself identical.
        /// </summary>
        private static string WallIsInside(double wall, double shorterSide, string what)
        {
            if (shorterSide - 2.0 * wall > 0.001) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "Wall {0:0.###} mm on a {1} of {2:0.###} mm: no cavity is left. " +
                "The wall must be STRICTLY under half, that is under {3:0.###} " +
                "mm. At exactly half the void has area zero, SOLIDWORKS does not " +
                "report it and extrudes a solid bar — which weighs four times " +
                "as much and is green like the right one.",
                wall, what, shorterSide, shorterSide / 2.0);
        }

        /// <summary>
        /// A side cannot be as thick as itself.
        ///
        /// At exact equality two vertices coincide, the segment disappears, and
        /// another section remains: not a stockier angle, a **flat bar**.
        /// Other weight, other inertia, no error.
        /// </summary>
        private static string ThicknessIsInside(
            double thickness, double side, string which, string becomes)
        {
            if (thickness < side) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "Thickness {0:0.###} mm on {1} of {2:0.###} mm: it is no longer a profile, " +
                "what comes out is {3}. The thickness must be under the side.",
                thickness, which, side, becomes);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Supporting geometry
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A coordinate, rounded to a tenth of a micron.
        ///
        /// It is not cosmetics: without it, two vertices that should coincide
        /// differ in the last bit and SOLIDWORKS builds a segment
        /// 1e-16 long from them — which gives no error and leaves the contour open.
        /// </summary>
        private static double Q(double v)
        {
            return Math.Round(v, 4, MidpointRounding.AwayFromZero);
        }

        private static List<double[]> Rectangle(double width, double height)
        {
            double mx = width / 2.0, my = height / 2.0;
            return new List<double[]>
            {
                new[] { Q(-mx), Q(-my) },
                new[] { Q(mx), Q(-my) },
                new[] { Q(mx), Q(my) },
                new[] { Q(-mx), Q(my) },
            };
        }

        private static Dictionary<string, object> Polygonal(List<double[]> vertices)
        {
            var v = new List<object>(vertices.Count);
            foreach (var p in vertices) v.Add(new object[] { p[0], p[1] });
            return new Dictionary<string, object>
            {
                ["kind"] = "polygonal",
                ["vertices"] = v.ToArray(),
            };
        }

        private static Dictionary<string, object> Circle(double cx, double cy, double diameter)
        {
            return new Dictionary<string, object>
            {
                ["kind"] = "circle",
                ["center"] = new object[] { Q(cx), Q(cy) },
                ["diameter_mm"] = diameter,
            };
        }
    }
}
