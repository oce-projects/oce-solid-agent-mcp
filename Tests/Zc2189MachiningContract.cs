using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE MACHINING AND ANCHOR FIXES OF Z-C21.8 AND Z-C21.9 THAT THE BENCH
    /// CANNOT REACH.
    ///
    ///   L5-5   edit_extrusion. The answer of AccessSelections is read, and a
    ///          refused definition is read back before anything is said
    ///          (ExtrusionReadBackTail, M42). suppress_instance reads the same
    ///          answer for its three pattern families.
    ///   L5-6   set_dimension. A refused value gets the remedy of its own
    ///          swSetValueReturnStatus_e (SetValueRefusalCause).
    ///   L5-7   dimension_tolerance. Every displayed dimension answers for itself
    ///          (ToleranceDecimalsFault), not the last one of the loop; the
    ///          tolerance put back is read back (M42).
    ///   L12-9  minimum_distance. ClosestDistance's -1 («no solution») is said.
    ///   L17-8  the state. The result code of Get6 is read.
    ///   M24    the flat pattern. Both answers of SetSuppression2 are read.
    ///   M35    set_property. The text written is compared with ValOut, not
    ///          with the evaluated value (PropertyWrittenAsAsked).
    ///   M36    sketch_picture. SetSize and SetOrigin are read, and the
    ///          placement is judged to a hundredth (PicturePlacementFault).
    ///   M37    fillet_edges, chamfer_edges. The volume must move
    ///          (EdgeFeatureVolumeFault); R1 = 0 for the variable fillet.
    ///   M42    reorder_feature. «Nothing touched» and «put back» after a
    ///          reread of the tree (TreeReadBackFault).
    ///   M43    move_bodies. A body that cannot be measured is not skipped
    ///          (MassOfBodies).
    ///   N16    check_body_geometry. An unreadable Check3 is not «no
    ///          defects» (BodyFaultNames answers null).
    ///   N49    the anchors. No direct cast of a sketch point that may be null.
    ///   N53    the anchor sketch. All seven swConstrainedStatus_e values
    ///          (ConstrainedStatusRefusal).
    ///   N56    the anchor of the guided holes. The open sketch is read after
    ///          EditSketch, and the selection is cleared before InsertSketch.
    ///   C7-3   suppress_instance. Fill and table patterns are named as such
    ///          (NotANumberedPattern), the derived pattern is not.
    ///   C9-6   the three removal helpers. A removed feature is not said to be
    ///          «still in the part» (RemovedButVolumeChanged,
    ///          RemovedButTreeLonger).
    ///
    /// None of these paths can be driven from a bench case: SOLIDWORKS would
    /// have to answer false, -1 or null on demand. So the pure rules are called
    /// on the BUILT library by reflection (no SOLIDWORKS), and the call sites
    /// are read in the source, because rules that nobody calls would keep this
    /// group green after a revert. On library 0.144.0 every check is red: the
    /// rules are missing and each call site is in its old form.
    /// </summary>
    internal static class Zc2189MachiningContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Gruppo("machining and anchor returns and messages the bench cannot reach (Z-C21.8, Z-C21.9: " +
                   "L5-5, L5-6, L5-7, L12-9, L17-8, M24, M35, M36, M37, M42, M43, N16, N49, N53, N56, C7-3, C9-6)");

            CallSites();

            var library = Path.Combine(Radice(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NonQui("the pure rules of the Z-C21.8 and Z-C21.9 machining fixes",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Type sw;
            try
            {
                var redist = DoveStaLInterop();
                if (redist != null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var file = Path.Combine(redist, name + ".dll");
                        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                    };
                }
                // From the bytes, as ApiCatalogContracts: the repository may sit
                // on a network drive, where LoadFrom is refused.
                var asm = Assembly.Load(File.ReadAllBytes(library));
                sw = asm.GetType("Oce.Solid.Agent.Core.OperazioniSw", false);
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Segna("OperazioniSw is in the library", false, "type not found"); return; }

            ExtrusionReadBack(sw);
            ToleranceDecimals(sw);
            TreeReadBack(sw);
            PropertyText(sw);
            PicturePlacement(sw);
            EdgeFeatureVolume(sw);
            MassOfBodies(sw);
            UnreadableCheck3(sw);
            ConstrainedStatus(sw);
            NumberedPattern(sw);
            RemovalTails(sw);
            SetValueRefusal(sw);
        }

        // ── L5-6: a refused dimension value, by its status ───────────────────
        // set_dimension said «usually a dimension governed by an equation or a
        // relation» for every status of SetSystemValue3. Each status gets its own
        // remedy now; the codes are swSetValueReturnStatus_e (1 Failure, 2
        // InvalidValue, 3 DrivenDimension, 4 ModelNotLoaded, 5 FrozenFeatureOwner).
        private static void SetValueRefusal(Type sw)
        {
            const string rule = "L5-6: a value SetSystemValue3 refuses gets the remedy of its own status";
            try
            {
                var texts = new[] { 1, 2, 3, 4, 5, 99 }.Select(s => (string)Call(sw, "SetValueRefusalCause", s)).ToList();
                bool distinct = texts.Take(5).Distinct().Count() == 5;
                bool named = texts[2].Contains("DRIVEN") && texts[4].Contains("FROZEN") && texts[3].Contains("not loaded") &&
                             texts[1].Contains("does not take this value");
                bool noBlanket = texts.Take(5).Count(t => t.Contains("equation")) == 1;   // only the reasonless Failure
                bool unknown = !string.IsNullOrEmpty(texts[5]);
                Segna(rule, distinct && named && noBlanket && unknown, string.Join(" | ", texts));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method + " is missing on this library");
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static string Shown(string s) => s == null ? "null" : "«" + s + "»";

        // ── L5-5 / M42: the definition read back ─────────────────────────────
        private static void ExtrusionReadBack(Type sw)
        {
            const string rule = "L5-5/M42: «nothing changed» only when end condition, depth and volume read back are " +
                                "the ones before; a changed volume and an unreadable end condition say so";
            try
            {
                // 0 is swEndCondBlind in swEndConditions_e.
                var same = (string)Call(sw, "ExtrusionReadBackTail", 0, 5.0, (double?)1000.0, 0, 5.0, (double?)1000.0);
                var moved = (string)Call(sw, "ExtrusionReadBackTail", 0, 5.0, (double?)1000.0, 0, 5.0, (double?)900.0);
                var deeper = (string)Call(sw, "ExtrusionReadBackTail", 0, 5.0, (double?)1000.0, 0, 7.0, (double?)1000.0);
                var unread = (string)Call(sw, "ExtrusionReadBackTail", 0, 5.0, (double?)1000.0, -1, double.NaN, (double?)1000.0);
                var noVolume = (string)Call(sw, "ExtrusionReadBackTail", 0, 5.0, (double?)1000.0, 0, 5.0, null);
                bool ok = same != null && same.Contains("nothing changed") &&
                          moved != null && !moved.Contains("nothing changed") && moved.Contains("not as it was") &&
                          deeper != null && !deeper.Contains("nothing changed") &&
                          unread != null && unread.Contains("could not read") &&
                          noVolume != null && noVolume.Contains("could not read");
                Segna(rule, ok, "same " + Shown(same) + " | volume moved " + Shown(moved) + " | deeper " + Shown(deeper) +
                                " | end not read " + Shown(unread) + " | volume not read " + Shown(noVolume));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── L5-7: every displayed dimension ──────────────────────────────────
        private static void ToleranceDecimals(Type sw)
        {
            const string rule = "L5-7: a first dimension left without the decimals is a fault even when the last one " +
                                "took them; status -1 or 3, a throw and no dimension at all are faults; status 1, 2 " +
                                "or 4 with the decimals read back is reported, not refused";
            try
            {
                var firstMissed = (string)Call(sw, "ToleranceDecimalsFault", 3, new[] { 0, 0 }, new[] { 2, 3 });
                var allTook = (string)Call(sw, "ToleranceDecimalsFault", 3, new[] { 0, 0 }, new[] { 3, 3 });
                var problem = (string)Call(sw, "ToleranceDecimalsFault", 3, new[] { 3 }, new[] { 3 });
                var failed = (string)Call(sw, "ToleranceDecimalsFault", 3, new[] { -1 }, new[] { 3 });
                var threw = (string)Call(sw, "ToleranceDecimalsFault", 3, new[] { int.MinValue }, new[] { int.MinValue });
                var none = (string)Call(sw, "ToleranceDecimalsFault", 3, new int[0], new int[0]);
                var other = (string)Call(sw, "ToleranceDecimalsFault", 3, new[] { 1 }, new[] { 3 });
                var note = (string)Call(sw, "ToleranceDecimalsStatusNote", new object[] { new[] { 0, 1 } });
                var noNote = (string)Call(sw, "ToleranceDecimalsStatusNote", new object[] { new[] { 0, 0 } });
                bool ok = firstMissed != null && firstMissed.Contains("displayed dimension 1") &&
                          allTook == null && problem != null && failed != null && threw != null && none != null &&
                          other == null && note != null && noNote == null;
                Segna(rule, ok, "first missed " + Shown(firstMissed) + " | all took " + Shown(allTook) +
                                " | status 3 " + Shown(problem) + " | status -1 " + Shown(failed) + " | threw " + Shown(threw) +
                                " | none " + Shown(none) + " | status 1 " + Shown(other) + " | note " + Shown(note) +
                                " | no note " + Shown(noNote));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── M42: the tree read back ──────────────────────────────────────────
        private static void TreeReadBack(Type sw)
        {
            const string rule = "M42: the tree is «the same» only with the same order and a volume read both times";
            try
            {
                var ab = new[] { "A", "B" };
                var same = (string)Call(sw, "TreeReadBackFault", ab, new[] { "A", "B" }, (double?)100.0, (double?)100.0);
                var swapped = (string)Call(sw, "TreeReadBackFault", ab, new[] { "B", "A" }, (double?)100.0, (double?)100.0);
                var volume = (string)Call(sw, "TreeReadBackFault", ab, new[] { "A", "B" }, (double?)100.0, (double?)90.0);
                var unread = (string)Call(sw, "TreeReadBackFault", ab, new[] { "A", "B" }, (double?)100.0, null);
                Segna(rule, same == null && swapped != null && volume != null && unread != null,
                      "same " + Shown(same) + " | swapped " + Shown(swapped) + " | volume " + Shown(volume) +
                      " | not read " + Shown(unread));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── M35: the text written, not the evaluated value ───────────────────
        private static void PropertyText(Type sw)
        {
            const string rule = "M35: a formula read back as its own text passes, a missing property and another text fail";
            try
            {
                const string formula = "\"SW-Mass@part.SLDPRT\"";
                bool asWritten = (bool)Call(sw, "PropertyWrittenAsAsked", formula, true, formula);
                bool missing = (bool)Call(sw, "PropertyWrittenAsAsked", formula, false, formula);
                bool other = (bool)Call(sw, "PropertyWrittenAsAsked", "A", true, "B");
                bool empty = (bool)Call(sw, "PropertyWrittenAsAsked", "", true, null);
                Segna(rule, asWritten && !missing && !other && empty,
                      "formula " + asWritten + ", missing " + missing + ", other text " + other + ", empty " + empty);
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── M36: the picture where it was asked ──────────────────────────────
        private static void PicturePlacement(Type sw)
        {
            const string rule = "M36: a picture placed as asked passes; an origin 0.02 mm off, a false SetSize, a false " +
                                "SetOrigin and an unread placement are faults";
            try
            {
                Func<bool, bool, bool, double, double, string> at = (size, origin, read, x, height) =>
                    (string)Call(sw, "PicturePlacementFault", size, origin, read, 120.0, 60.0, 10.0, 20.0, 120.0, height, x, 20.0);
                var good = at(true, true, true, 10.0, 60.0);
                var off = at(true, true, true, 10.02, 60.0);
                var tall = at(true, true, true, 10.0, 60.5);
                var sizeFalse = at(false, true, true, 10.0, 60.0);
                var originFalse = at(true, false, true, 10.0, 60.0);
                var unread = at(true, true, false, 10.0, 60.0);
                Segna(rule, good == null && off != null && tall != null && sizeFalse != null && originFalse != null && unread != null,
                      "good " + Shown(good) + " | origin off " + Shown(off) + " | height off " + Shown(tall) +
                      " | SetSize false " + Shown(sizeFalse) + " | SetOrigin false " + Shown(originFalse) +
                      " | not read " + Shown(unread));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── M37: the volume must move ────────────────────────────────────────
        private static void EdgeFeatureVolume(Type sw)
        {
            const string rule = "M37: a fillet or chamfer that leaves the volume where it was is a fault; less or MORE " +
                                "material (a concave edge) passes; an unread volume is not judged";
            try
            {
                var still = (string)Call(sw, "EdgeFeatureVolumeFault", (double?)1000.0, (double?)1000.0);
                var less = (string)Call(sw, "EdgeFeatureVolumeFault", (double?)1000.0, (double?)990.0);
                var more = (string)Call(sw, "EdgeFeatureVolumeFault", (double?)1000.0, (double?)1010.0);
                var unread = (string)Call(sw, "EdgeFeatureVolumeFault", null, (double?)1000.0);
                Segna(rule, still != null && less == null && more == null && unread == null,
                      "unchanged " + Shown(still) + " | less " + Shown(less) + " | more " + Shown(more) +
                      " | not read " + Shown(unread));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── M43: no body skipped ─────────────────────────────────────────────
        private static void MassOfBodies(Type sw)
        {
            const string rule = "M43: two bodies of 1000 mm3 at x = 0 and x = 10 mm give 2000 mm3 centred at x = 5; " +
                                "a body without a reading makes the whole unknown";
            try
            {
                var m = sw.GetMethod("MassOfBodies", Any);
                if (m == null) throw new MissingMethodException("OperazioniSw.MassOfBodies is missing on this library");
                // GetMassProperties: centre in metres (0-2), volume in cubic metres (3).
                var two = new List<double[]> { new[] { 0.0, 0.0, 0.0, 1e-6 }, new[] { 0.01, 0.0, 0.0, 1e-6 } };
                var argsTwo = new object[] { two, 0.0, null };
                bool okTwo = (bool)m.Invoke(null, argsTwo);
                double volume = (double)argsTwo[1];
                var centre = argsTwo[2] as double[];
                var withGap = new List<double[]> { new[] { 0.0, 0.0, 0.0, 1e-6 }, null };
                bool okGap = (bool)m.Invoke(null, new object[] { withGap, 0.0, null });
                bool numbers = okTwo && Math.Abs(volume - 2000) < 1e-6 && centre != null && Math.Abs(centre[0] - 5) < 1e-9;
                Segna(rule, numbers && !okGap,
                      "two bodies " + okTwo + " (" + volume + " mm3, x " + (centre == null ? "?" : centre[0].ToString()) +
                      "), with an unreadable body " + okGap);
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N16: an unreadable Check3 ────────────────────────────────────────
        private static void UnreadableCheck3(Type sw)
        {
            const string rule = "N16: a body whose Check3 cannot be read gives null, not the empty list of a sound body";
            try
            {
                // A null body makes the Check3 access throw inside the method:
                // the unreadable case, without SOLIDWORKS.
                object faults = Call(sw, "BodyFaultNames", new object[] { null });
                Segna(rule, faults == null,
                      faults == null ? "null" : "a list of " + ((ICollection)faults).Count + " (the answer of a sound body)");
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N53: the seven statuses ──────────────────────────────────────────
        private static void ConstrainedStatus(Type sw)
        {
            const string rule = "N53: automatic solving off (7), unknown (1) and an unread status (-1) are named, and " +
                                "all seven values of swConstrainedStatus_e have a name";
            try
            {
                var off = (string)Call(sw, "ConstrainedStatusRefusal", 7);
                var unknown = (string)Call(sw, "ConstrainedStatusRefusal", 1);
                var unread = (string)Call(sw, "ConstrainedStatusRefusal", -1);
                var under = (string)Call(sw, "ConstrainedStatusRefusal", 2);
                var unnamed = Enumerable.Range(1, 7)
                    .Where(v => (string)Call(sw, "ConstrainedStatusName", v) == (string)Call(sw, "ConstrainedStatusName", 99))
                    .ToList();
                bool ok = off != null && off.Contains("automatic solving") && !off.Contains("where the solver puts it") &&
                          unknown != null && unknown.Contains("does not know") &&
                          unread != null && unread.Contains("could not read") &&
                          under != null && under.Contains("under defined") && unnamed.Count == 0;
                Segna(rule, ok, "7 " + Shown(off) + " | 1 " + Shown(unknown) + " | -1 " + Shown(unread) + " | 2 " + Shown(under) +
                                (unnamed.Count == 0 ? "" : " | without a name: " + string.Join(", ", unnamed)));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── C7-3: fill and table patterns named ─────────────────────────────
        private static void NumberedPattern(Type sw)
        {
            const string rule = "C7-3: a fill and a table pattern are named, not called «not a pattern with numbered " +
                                "copies», and the derived pattern is not mentioned";
            try
            {
                var fill = (string)Call(sw, "NotANumberedPattern", true, false);
                var table = (string)Call(sw, "NotANumberedPattern", false, true);
                var other = (string)Call(sw, "NotANumberedPattern", false, false);
                const string old = "not a pattern with numbered copies";
                bool ok = fill != null && fill.Contains("FILL") && !fill.Contains(old) &&
                          table != null && table.Contains("TABLE") && !table.Contains(old) &&
                          other != null && other.Contains(old) &&
                          new[] { fill, table, other }.All(s => s.IndexOf("derived", StringComparison.OrdinalIgnoreCase) < 0);
                Segna(rule, ok, "fill " + Shown(fill) + " | table " + Shown(table) + " | other " + Shown(other));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── C9-6: a removed feature is not «still in the part» ───────────────
        private static void RemovalTails(Type sw)
        {
            const string rule = "C9-6: after a removal the tail says it was removed and gives the new volume, or counts " +
                                "the features in excess; never «still in the part»";
            try
            {
                var volume = (string)Call(sw, "RemovedButVolumeChanged", 990.0, 1000.0);
                var longer = (string)Call(sw, "RemovedButTreeLonger", 1);
                bool ok = volume != null && volume.Contains("I removed it") && volume.Contains("990") &&
                          volume.Contains("1000") && !volume.Contains("still in the part") &&
                          longer != null && longer.Contains("1 feature more") && !longer.Contains("still in the part");
                Segna(rule, ok, "volume " + Shown(volume) + " | tree " + Shown(longer));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── the call sites, in the source ────────────────────────────────────
        //
        // Whole files: each name looked for lives in the file named, and the
        // counts include the definition where there is one.
        private static void CallSites()
        {
            string dir = Path.Combine(Radice(), "Oce.Solid.Agent.Core");

            string extrusion = Read(dir, "EditExtrusionOperations.cs");
            if (extrusion != null)
            {
                bool kept = Regex.IsMatch(extrusion, @"\w+\s*=\s*dati\.AccessSelections\(");
                bool bare = Regex.IsMatch(extrusion, @"(?m)^\s*dati\.AccessSelections\(");
                int readBacks = Count(extrusion, "ExtrusionReadBack(");
                Segna("L5-5/M42: edit_extrusion keeps the answer of AccessSelections and reads a refused definition back",
                      kept && !bare && readBacks >= 2,
                      "answer kept " + kept + ", bare call " + bare + ", ExtrusionReadBack " + readBacks + " (with the definition)");
            }

            string patterns = Read(dir, "InstanceOperations.cs");
            if (patterns != null)
            {
                // The opener of each family has the signature (object d, IModelDoc2 m):
                // void until library 0.144.0, bool now.
                int voidOpeners = Regex.Matches(patterns, @"void \w+\(object d, IModelDoc2 m\)").Count;
                int returned = Regex.Matches(patterns, @"return \(\(I\w+PatternFeatureData\)d\)\.AccessSelections\(").Count;
                int refusals = Count(patterns, "AccessSelections answered false");
                Segna("L5-5: suppress_instance reads AccessSelections for its three families, where it opens and where it puts back",
                      voidOpeners == 0 && returned == 3 && refusals == 2,
                      voidOpeners + " void openers, " + returned + " returned answers, " + refusals + " refusals on a false answer");
            }

            string tolerance = Read(dir, "DimensionToleranceOperations.cs");
            if (tolerance != null)
            {
                bool bare = Regex.IsMatch(tolerance, @"(?m)^\s*dd\.SetPrecision3\(");
                bool kept = Regex.IsMatch(tolerance, @"status\s*=\s*dd\.SetPrecision3\(");
                int verdicts = Count(tolerance, "ToleranceDecimalsFault(");
                int restores = Count(tolerance, "RestoreTolerance(");
                Segna("L5-7/M42: dimension_tolerance keeps every SetPrecision3 answer, judges them all and reads the restore back",
                      !bare && kept && verdicts >= 2 && restores >= 3,
                      "bare call " + bare + ", answer kept " + kept + ", ToleranceDecimalsFault " + verdicts +
                      ", RestoreTolerance " + restores + " (definition and two calls expected)");
            }

            string measure = Read(dir, "MeasureOperations.cs");
            if (measure != null)
            {
                bool guarded = Regex.IsMatch(measure, @"if \(\w+ < 0\)\s*return Fallito\(\s*""SOLIDWORKS found no closest distance");
                Segna("L12-9: minimum_distance says ClosestDistance's -1 as «no solution»", guarded,
                      guarded ? "guarded" : "no guard on the -1");
            }

            string state = Read(dir, "DocumentState.cs");
            if (state != null)
            {
                bool bare = Regex.IsMatch(state, @"(?m)^\s*\w+\.Get6\(");
                bool kept = Regex.IsMatch(state, @"int \w+ = \w+\.Get6\(");
                bool notPresent = state.Contains("swCustomInfoGetResult_NotPresent) continue;");
                Segna("L17-8: the state reads the result code of Get6 and leaves out a name that is not present",
                      !bare && kept && notPresent, "bare call " + bare + ", code kept " + kept + ", NotPresent skipped " + notPresent);
            }

            string sheet = Read(dir, "SheetMetalOperations.cs");
            if (sheet != null)
            {
                bool bare = Regex.IsMatch(sheet, @"(?m)^\s*\w+\.SetSuppression2\(");
                bool unsuppress = Regex.IsMatch(sheet, @"bool \w+ = \w+\.SetSuppression2\(");
                bool suppress = Regex.IsMatch(sheet, @"\w+ = \w+\.SetSuppression2\(\(int\)swFeatureSuppressionAction_e\.swSuppressFeature");
                int reported = Count(sheet, "[\"flat_pattern_problem\"] = ");
                Segna("M24: both SetSuppression2 answers around the flat pattern are read, and the answers say a refusal",
                      !bare && unsuppress && suppress && reported >= 5,
                      "bare call " + bare + ", unsuppress kept " + unsuppress + ", suppress kept " + suppress +
                      ", answers that report it " + reported);

                // And no caller is left without the reason: the overload that
                // answered only NaN is gone, and the flat-pattern view of a drawing
                // refuses before placing the view when the flat pattern did not
                // toggle (flat_pattern_view called the NaN-only overload until the
                // review of this lot).
                bool nanOnly = Regex.IsMatch(sheet, @"double LunghezzaSviluppata\(IModelDoc2 \w+, out double \w+\)");
                string flatView = Read(dir, "DrawingFlatPatternOperations.cs");
                bool viewReports = flatView != null &&
                                   Regex.IsMatch(flatView, @"LunghezzaSviluppata\(\w+, out double _, out string flatPatternProblem\);") &&
                                   flatView.Contains("if (flatPatternProblem != null)");
                Segna("M24: every caller of the flat length gets the reason, flat_pattern_view included",
                      !nanOnly && viewReports, "NaN-only overload " + nanOnly + ", flat_pattern_view reports it " + viewReports);
            }

            string properties = Read(dir, "PropertyOperations.cs");
            if (properties != null)
            {
                bool compared = Regex.IsMatch(properties, @"if \(!PropertyWrittenAsAsked\(\w+, \w+, \w+\)\)");
                Segna("M35: set_property compares the text written with ValOut", compared,
                      compared ? "compared" : "PropertyWrittenAsAsked is not called");
            }

            string picture = Read(dir, "RetraceOperations.cs");
            if (picture != null)
            {
                bool size = Regex.IsMatch(picture, @"\w+ = foto\.SetSize\(");
                bool origin = Regex.IsMatch(picture, @"\w+ = foto\.SetOrigin\(");
                int judged = Count(picture, "PicturePlacementFault(");
                Segna("M36: sketch_picture keeps both answers and judges the placement",
                      size && origin && judged >= 2, "SetSize kept " + size + ", SetOrigin kept " + origin +
                      ", PicturePlacementFault " + judged + " (with the definition)");
            }

            string main = Read(dir, "SwOperations.cs");
            if (main != null)
            {
                bool fillet = Regex.IsMatch(main, @"EdgeFeatureVolumeCheck\(\w+, ""fillet""");
                bool chamfer = Regex.IsMatch(main, @"EdgeFeatureVolumeCheck\(\w+, ""chamfer""");
                bool mass = Regex.IsMatch(main, @"return MassOfBodies\(readings, out \w+, out \w+\);");
                Segna("M37/M43: fillet and chamfer check the volume, and MassaDi goes through MassOfBodies",
                      fillet && chamfer && mass, "fillet " + fillet + ", chamfer " + chamfer + ", MassOfBodies " + mass);
                bool byStatus = Regex.IsMatch(main, @"SetValueRefusalCause\(\w+\)\);");
                Segna("L5-6: set_dimension words its refusal by the status of SetSystemValue3", byStatus,
                      byStatus ? "SetValueRefusalCause is called" : "SetValueRefusalCause is not called");
            }

            string bodies = Read(dir, "BodyOperations.cs");
            if (bodies != null)
            {
                bool r1 = Regex.IsMatch(bodies, @"R1:\s*0,");
                Segna("M37: the variable fillet passes R1 = 0, as FeatureFillet3.html asks", r1,
                      r1 ? "R1: 0" : "R1 is not 0");
            }

            string order = Read(dir, "ReorderOperations.cs");
            if (order != null)
            {
                int reread = Count(order, "TreeReadBackFault(");
                Segna("M42: reorder_feature reads the tree back before «nothing touched» and «put back»",
                      reread >= 3, "TreeReadBackFault " + reread + " (definition and two calls expected)");
            }

            string anchor = Read(dir, "AnchorOperations.cs");
            if (anchor != null)
            {
                int casts = Count(anchor, "(ISketchPoint)");
                bool statusNamed = Regex.IsMatch(anchor, @"\w+ = ConstrainedStatusRefusal\(\w+\);");
                bool opened = Regex.IsMatch(anchor, @"var openSketch = \w+\.ActiveSketch;");
                int clearBeforeClose = Regex.Matches(anchor,
                    @"try \{ \w+\.ClearSelection2\(true\); \} catch \{ \}\s*if \(\w+\) \{ try \{ \w+\.InsertSketch\(true\); \} catch \{ \} \}").Count;
                Segna("N49/N53/N56: no direct sketch-point cast, the status named, the open sketch read, the selection cleared first",
                      casts == 0 && statusNamed && opened && clearBeforeClose >= 1,
                      casts + " direct casts, status named " + statusNamed + ", open sketch read " + opened +
                      ", clear before close " + clearBeforeClose);
            }

            int removals = 0;
            foreach (var file in new[] { "AnchorOperations.cs", "FillPatternOperations.cs", "WeldmentOperations.cs" })
            {
                string text = Read(dir, file);
                if (text != null && text.Contains("return RemovedButVolumeChanged(volumeDopo.GetValueOrDefault(), volumePrima.GetValueOrDefault());")) removals++;
            }
            Segna("C9-6: the three removal helpers give the new volume instead of «still in the part»",
                  removals == 3, removals + " of 3");
        }

        private static string Read(string dir, string file)
        {
            try { return File.ReadAllText(Path.Combine(dir, file)); }
            catch (Exception ex) { Segna(file + " can be read", false, ex.Message); return null; }
        }

        private static int Count(string text, string what)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
            return n;
        }
    }
}
