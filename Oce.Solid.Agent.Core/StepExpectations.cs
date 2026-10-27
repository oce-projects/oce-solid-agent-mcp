using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHAT A STEP MUST MEASURE, SAID BY WHOEVER WROTE THE PLAN (brief R16).
    ///
    /// On 24-25/10/2026 every check was made by hand after the plan: the window
    /// of the clamp had to remove 36 mm3 (3 x 6 x 2), and the only way to know was
    /// to read the outcome and do the sum. A step can now carry «expect», next to
    /// «arguments»: {"removed_mm3": 36} or {"removed_mm3": {"equal_to": 36,
    /// "tolerance": 0.5}}. After the step the executor compares each key with the
    /// step's own measurements; one that does not hold makes the step fail and
    /// the plan stop there, with the numbers side by side.
    ///
    /// THE KEYS ARE THE STEP'S MEASUREMENTS, by name, as the outcome answers them
    /// («removed_mm3», «diameter_mm», «walls_drilled»...). «volume_mm3» is the
    /// volume after the step («volume_after_mm3»). A key the step does not measure
    /// is a miss, not a pass: an expectation nobody checked would read as kept.
    ///
    /// Pure: no interop type, so the contract suite calls it by reflection.
    /// </summary>
    public static class StepExpectations
    {
        /// <summary>The tolerance of a plain number: the rounding of the measures that come rounded to 0.01.</summary>
        public const double DefaultTolerance = 0.01;

        /// <summary>
        /// Why «expect» is not in a form this checks, or null when it is: an object
        /// whose values are a number, a text, true/false, a list of numbers, or
        /// {"equal_to": one of those, "tolerance": a number ≥ 0}.
        /// </summary>
        public static string FormProblem(object raw)
        {
            if (raw == null) return null;
            var expect = raw as Dictionary<string, object>;
            if (expect == null)
                return "«expect» is an object of the step's measurements, for example {\"removed_mm3\": 36} or " +
                       "{\"diameter_mm\": {\"equal_to\": 0.992, \"tolerance\": 0.001}}.";
            if (expect.Count == 0) return "«expect» is empty: name at least one measurement, or leave it out.";
            foreach (string key in expect.Keys)
            {
                object wanted = expect[key];
                var detailed = wanted as Dictionary<string, object>;
                if (detailed != null)
                {
                    object tolerance;
                    if (!detailed.TryGetValue("equal_to", out wanted))
                        return "«expect»." + key + " is an object without «equal_to»: write {\"equal_to\": value, " +
                               "\"tolerance\": number} or the value alone.";
                    if (detailed.TryGetValue("tolerance", out tolerance))
                    {
                        double t;
                        if (!Number(tolerance, out t) || t < 0)
                            return "«expect»." + key + ".tolerance must be a number of zero or more.";
                    }
                    foreach (string part in detailed.Keys)
                        if (part != "equal_to" && part != "tolerance")
                            return "«expect»." + key + " has «" + part + "»: only «equal_to» and «tolerance» are read.";
                }
                if (!Comparable(wanted))
                    return "«expect»." + key + " must be a number, a text, true/false or a list of numbers.";
            }
            return null;
        }

        /// <summary>
        /// The keys of <paramref name="expect"/> that the measurements do not keep,
        /// one sentence each («removed_mm3: expected 36 ±0.01, measured 35.2»), or an
        /// empty list when every one holds. A key the step does not measure is in
        /// the list, with the names it does measure.
        /// </summary>
        public static List<string> Misses(Dictionary<string, object> expect, Dictionary<string, object> measurements)
        {
            var misses = new List<string>();
            if (expect == null) return misses;
            foreach (string key in expect.Keys)
            {
                object wanted = expect[key];
                double tolerance = DefaultTolerance;
                var detailed = wanted as Dictionary<string, object>;
                if (detailed != null)
                {
                    detailed.TryGetValue("equal_to", out wanted);
                    object t;
                    double given;
                    if (detailed.TryGetValue("tolerance", out t) && Number(t, out given)) tolerance = given;
                }
                object measured;
                if (!Measured(measurements, key, out measured))
                {
                    misses.Add(key + ": this step does not measure it (it measures " +
                               (measurements == null || measurements.Count == 0 ? "nothing" : string.Join(", ", measurements.Keys)) + ")");
                    continue;
                }
                if (!Holds(wanted, measured, tolerance))
                    misses.Add(key + ": expected " + Text(wanted) + (IsNumeric(wanted) ? " ±" +
                               tolerance.ToString("0.######", CultureInfo.InvariantCulture) : "") + ", measured " + Text(measured));
            }
            return misses;
        }

        /// <summary>
        /// What «expect» checked and found kept, one entry per key, for the
        /// outcome of a step whose expectations all held: «held: removed_mm3 7.07
        /// (expected 7.07 ±0.01)». Report 8cd843 N8, 27/10/2026: a plan with six
        /// expectations kept said the word «expect» nowhere, and a check that
        /// passes in silence reads as a check not made. Null without «expect».
        /// </summary>
        public static string Held(Dictionary<string, object> expect, Dictionary<string, object> measurements)
        {
            if (expect == null || expect.Count == 0) return null;
            var parts = new List<string>();
            foreach (string key in expect.Keys)
            {
                object wanted = expect[key];
                double tolerance = DefaultTolerance;
                var detailed = wanted as Dictionary<string, object>;
                if (detailed != null)
                {
                    detailed.TryGetValue("equal_to", out wanted);
                    object t;
                    double given;
                    if (detailed.TryGetValue("tolerance", out t) && Number(t, out given)) tolerance = given;
                }
                object measured;
                Measured(measurements, key, out measured);
                parts.Add(key + " " + Text(measured) + " (expected " + Text(wanted) + (IsNumeric(wanted) ? " ±" +
                          tolerance.ToString("0.######", CultureInfo.InvariantCulture) : "") + ")");
            }
            return "held: " + string.Join("; ", parts);
        }

        /// <summary>The measurement of that name; «volume_mm3» falls back on «volume_after_mm3».</summary>
        private static bool Measured(Dictionary<string, object> measurements, string key, out object value)
        {
            value = null;
            if (measurements == null) return false;
            if (measurements.TryGetValue(key, out value) && value != null) return true;
            if (key == "volume_mm3" && measurements.TryGetValue("volume_after_mm3", out value) && value != null) return true;
            return false;
        }

        /// <summary>Whether a measured value keeps the one wanted: numbers within the tolerance, lists element by element, the rest equal.</summary>
        public static bool Holds(object wanted, object measured, double tolerance)
        {
            double w, m;
            if (Number(wanted, out w)) return Number(measured, out m) && Math.Abs(w - m) <= tolerance + 1e-12;
            if (wanted is string) return measured is string && string.Equals((string)wanted, (string)measured, StringComparison.Ordinal);
            if (wanted is bool) return measured is bool && (bool)wanted == (bool)measured;
            var wantedList = wanted as IEnumerable;
            var measuredList = measured as IEnumerable;
            if (wantedList == null || measuredList == null || measured is string) return false;
            var a = new List<object>(); foreach (var x in wantedList) a.Add(x);
            var b = new List<object>(); foreach (var x in measuredList) b.Add(x);
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!Holds(a[i], b[i], tolerance)) return false;
            return true;
        }

        private static bool Comparable(object v)
        {
            double d;
            if (Number(v, out d) || v is string || v is bool) return true;
            var list = v as IEnumerable;
            if (list == null) return false;
            int n = 0;
            foreach (var x in list) { if (!Number(x, out d)) return false; n++; }
            return n > 0;
        }

        private static bool IsNumeric(object v)
        {
            double d;
            if (Number(v, out d)) return true;
            var list = v as IEnumerable;
            return list != null && !(v is string);
        }

        private static bool Number(object v, out double d)
        {
            d = 0;
            if (v == null || v is string || v is bool) return false;
            if (!(v is IConvertible)) return false;
            try { d = Convert.ToDouble(v, CultureInfo.InvariantCulture); return !double.IsNaN(d) && !double.IsInfinity(d); }
            catch (FormatException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        private static string Text(object v)
        {
            double d;
            if (Number(v, out d)) return d.ToString("0.######", CultureInfo.InvariantCulture);
            if (v is string) return "«" + v + "»";
            if (v is bool) return (bool)v ? "true" : "false";
            var list = v as IEnumerable;
            if (list == null) return v == null ? "null" : v.ToString();
            var parts = new List<string>();
            foreach (var x in list) parts.Add(Text(x));
            return "[" + string.Join(", ", parts) + "]";
        }
    }
}
