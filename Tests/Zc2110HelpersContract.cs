using System;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE PURE RULES BEHIND THE Z-C21.10 FIXES THE BENCH CANNOT REACH.
    ///
    /// Several findings of Z-C21.10 (esito 2) were fixed in branches no bench
    /// case can build: a probe that fails once and then succeeds (N36), a
    /// stale archive line (N36), an interference array shorter than its count
    /// (N34), a view of another model on another sheet (N43), a dimension
    /// hidden by hand (C8-2, the runner has no field for it yet), a slot angle
    /// read back as its supplement (N55), a frame reading written in another
    /// case (N32, which the bench also covers). Each fix keeps its decision in
    /// a small static method with no interop in the signature, and this group
    /// calls those methods on the BUILT library by reflection: no SOLIDWORKS.
    ///
    /// The overhang archive (N36) is exercised in a temporary folder of
    /// %LOCALAPPDATA%, never the real one, and the folder is deleted at the end.
    /// </summary>
    internal static class Zc2110HelpersContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Gruppo("the pure rules of the Z-C21.10 fixes the bench cannot reach (N32, N34, N36, N43, N55, C8-2)");

            var library = Path.Combine(Radice(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NonQui("the pure rules of the Z-C21.10 fixes",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Assembly asm;
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
                asm = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            Type sw = asm.GetType("Oce.Solid.Agent.Core.OperazioniSw", false);
            Type helpers = asm.GetType("Oce.Solid.Agent.Core.ApiHelpers", false);
            Type section = asm.GetType("Oce.Solid.Agent.Core.SezioneProfilo", false);
            if (sw == null || helpers == null || section == null)
            {
                Segna("the three library types are there", false,
                      "OperazioniSw " + (sw != null) + ", ApiHelpers " + (helpers != null) +
                      ", SezioneProfilo " + (section != null));
                return;
            }

            FrameReading(sw);
            InterferenceList(sw);
            SlotAngle(sw);
            SameModel(helpers);
            HiddenByHand(helpers);
            OverhangArchive(section);
            IgesCensus(sw);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // ── N32: the frame's «measured_as», whatever the case ─────────────────────
        private static void FrameReading(Type sw)
        {
            try
            {
                string a = (string)Call(sw, "FrameReading", "Overall");
                string b = (string)Call(sw, "FrameReading", " CENTER_DISTANCE ");
                string c = (string)Call(sw, "FrameReading", (object)null);
                string d = (string)Call(sw, "FrameReading", "outer");
                Segna("N32: «Overall» and « CENTER_DISTANCE » read as the catalogue spells them",
                      a == "overall" && b == "center_distance", "«" + a + "», «" + b + "»");
                Segna("N32: a missing «measured_as» is «overall», any other word is no reading",
                      c == "overall" && d == null, "null -> «" + c + "», «outer» -> " + (d ?? "null"));
            }
            catch (Exception ex) { Segna("N32: FrameReading can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N34: the array against the count ─────────────────────────────────
        private static void InterferenceList(Type sw)
        {
            try
            {
                var whole = (string)Call(sw, "InterferenceListProblem", 2, (int?)2, 0, null);
                var shorter = (string)Call(sw, "InterferenceListProblem", 3, (int?)2, 0, null);
                var none = (string)Call(sw, "InterferenceListProblem", 1, null, 0, "COMException: boom");
                var noneAtZero = (string)Call(sw, "InterferenceListProblem", 0, null, 0, null);
                var foreign = (string)Call(sw, "InterferenceListProblem", 2, (int?)2, 1, null);
                Segna("N34: an array as long as the count is whole", whole == null, whole ?? "null");
                Segna("N34: a short array, a missing one and a foreign item are each a problem",
                      shorter != null && shorter.Contains("counted 3") && shorter.Contains("array of 2")
                      && none != null && none.Contains("boom")
                      && foreign != null && foreign.Contains("not an IInterference"),
                      (shorter ?? "null") + " | " + (none ?? "null") + " | " + (foreign ?? "null"));
                Segna("N34: no array after a count of zero is not a problem", noneAtZero == null, noneAtZero ?? "null");
            }
            catch (Exception ex) { Segna("N34: InterferenceListProblem can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N55: the slot angle against the reference edge ───────────────────
        private static void SlotAngle(Type sw)
        {
            try
            {
                double a = (double)Call(sw, "AngleBetweenLinesDeg", 30.0, 90.0);
                double b = (double)Call(sw, "AngleBetweenLinesDeg", 30.0, -90.0);
                double c = (double)Call(sw, "AngleBetweenLinesDeg", 30.0, 0.0);
                double d = (double)Call(sw, "AngleBetweenLinesDeg", 30.0, 180.0);
                Segna("N55: a 30-degree slot reads 60 against an edge along y, 30 against one along x",
                      Math.Abs(a - 60) < 1e-9 && Math.Abs(b - 60) < 1e-9 &&
                      Math.Abs(c - 30) < 1e-9 && Math.Abs(d - 30) < 1e-9,
                      a + ", " + b + ", " + c + ", " + d + " (expected 60, 60, 30, 30)");
            }
            catch (Exception ex) { Segna("N55: AngleBetweenLinesDeg can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N43: views of another model do not count ─────────────────────────
        private static void SameModel(Type helpers)
        {
            try
            {
                bool unknown = (bool)Call(helpers, "MayShowTheSameModel", null, "a|c");
                bool same = (bool)Call(helpers, "MayShowTheSameModel", "a|c", "a|c");
                bool otherModel = (bool)Call(helpers, "MayShowTheSameModel", "a|c", "b|c");
                bool otherConfig = (bool)Call(helpers, "MayShowTheSameModel", "a|c1", "a|c2");
                Segna("N43: an unknown view and the same model/configuration count, another model or configuration does not",
                      unknown && same && !otherModel && !otherConfig,
                      "unknown " + unknown + ", same " + same + ", other model " + otherModel +
                      ", other configuration " + otherConfig);
            }
            catch (Exception ex) { Segna("N43: MayShowTheSameModel can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── C8-2: a dimension hidden by hand ─────────────────────────────────
        private static void HiddenByHand(Type helpers)
        {
            try
            {
                bool hidden = (bool)Call(helpers, "IsHiddenVisibility", 3);
                bool half = (bool)Call(helpers, "IsHiddenVisibility", 2);
                bool visible = (bool)Call(helpers, "IsHiddenVisibility", 1);
                bool unknown = (bool)Call(helpers, "IsHiddenVisibility", 0);
                Segna("C8-2: hidden (3) and half-hidden (2) are not printed, visible (1) and unknown (0) are",
                      hidden && half && !visible && !unknown,
                      "3 " + hidden + ", 2 " + half + ", 1 " + visible + ", 0 " + unknown);
            }
            catch (Exception ex) { Segna("C8-2: IsHiddenVisibility can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N59: what an IGES file carries, from its directory section ───────
        //
        // Added in review: the bench case of N59 cannot build a wireframe file
        // (it would need a user preference the bench does not write), so the
        // census that decides the refusal «curves only» (no solid, no surface)
        // had never been seen to say zero. Here it reads three synthetic files:
        // a solid with its surfaces, curves only, and a directory section cut
        // in the middle of an entry.
        private static void IgesCensus(Type sw)
        {
            string folder = Path.Combine(Path.GetTempPath(), "zc2110-iges-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                var census = sw.GetMethod("IgesEntityCensus", Any);
                if (census == null) { Segna("N59: IgesEntityCensus is there", false, "missing on this library"); return; }

                Func<int[], string> write = types =>
                {
                    var lines = new System.Collections.Generic.List<string>
                    {
                        "SYNTHETIC IGES FOR PROVE.CONTRATTI".PadRight(72) + "S      1",
                        "1H,,1H;".PadRight(72) + "G      1",
                    };
                    int seq = 0;
                    foreach (var t in types)
                    {
                        // Two directory lines per entity; the type in columns 1-8 of both.
                        lines.Add(t.ToString().PadLeft(8).PadRight(72) + "D" + (++seq).ToString().PadLeft(7));
                        lines.Add(t.ToString().PadLeft(8).PadRight(72) + "D" + (++seq).ToString().PadLeft(7));
                    }
                    lines.Add("S      1G      1D" + seq.ToString().PadLeft(7) + "P      0".PadRight(48) + "T      1");
                    string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".igs");
                    File.WriteAllLines(file, lines);
                    return file;
                };
                Func<string, object[]> count = file =>
                {
                    var args = new object[] { file, 0, 0, 0, null };
                    bool ok = (bool)census.Invoke(null, args);
                    return new object[] { ok, args[1], args[2], args[3], args[4] };
                };

                // A solid (186) over two plane surfaces (190) and one line (110).
                var solid = count(write(new[] { 186, 190, 190, 110 }));
                // The same part as a wireframe: lines and points only.
                var wire = count(write(new[] { 110, 110, 110, 116 }));
                Segna("N59: the census counts the solid and its surfaces, and a wireframe has neither",
                      (bool)solid[0] && (int)solid[1] == 4 && (int)solid[2] == 1 && (int)solid[3] == 2 &&
                      (bool)wire[0] && (int)wire[1] == 4 && (int)wire[2] == 0 && (int)wire[3] == 0,
                      "solid file: ok " + solid[0] + ", entities " + solid[1] + ", solids " + solid[2] +
                      ", surfaces " + solid[3] + " (expected 4, 1, 2); wireframe: ok " + wire[0] +
                      ", entities " + wire[1] + ", solids " + wire[2] + ", surfaces " + wire[3] +
                      " (expected 4, 0, 0)");

                // A directory section with an odd number of lines is not read as a count.
                string broken = write(new[] { 186, 190 });
                var kept = File.ReadAllLines(broken).Where((l, i) => i != 3).ToArray();   // drop one D line
                File.WriteAllLines(broken, kept);
                var odd = count(broken);
                Segna("N59: a directory section cut in the middle of an entry is not a census",
                      !(bool)odd[0] && Convert.ToString(odd[4]).Contains("an entry takes two"),
                      "ok " + odd[0] + ", " + Convert.ToString(odd[4]));
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Segna("N59: IgesEntityCensus can be called", false, inner.GetType().Name + ": " + inner.Message);
            }
            finally
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            }
        }

        // ── N36: the overhang archive ────────────────────────────────────────
        private static void OverhangArchive(Type section)
        {
            string folderName = "Riscontro-prove-" + Guid.NewGuid().ToString("N");
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folderName);
            try
            {
                var folderProp = section.GetProperty("CartellaArchivio", Any);
                if (folderProp == null) { Segna("N36: SezioneProfilo.CartellaArchivio is there", false, ""); return; }
                folderProp.SetValue(null, folderName);

                // (a) THE KEY CHANGES WITH THE FILE.
                Func<long, long, string> key = (ticks, length) => (string)Call(section, "FrameKey",
                    "C:\\x\\TUBO.sldlfp", ticks, length, "80X80X5", 0, 1, 0.0, 1, 0.0);
                string k = key(1000, 2000);
                Segna("N36: the frame key changes with the library file's date and size",
                      k == key(1000, 2000) && k != key(1001, 2000) && k != key(1000, 2001), k);
                bool current = (bool)Call(section, "IsCurrentKey", k);
                bool old = (bool)Call(section, "IsCurrentKey", "telaio|X|80X80X5|0|1|0|1|0");
                Segna("N36: a key of the old format is not current, so it is dropped on load",
                      current && !old, "new " + current + ", old " + old);

                // (b) A FAILURE IS NOT REMEMBERED. On 0.140.0 the stored null
                // came back and the second measurement was never asked.
                var remember = section.GetMethod("RememberOrMeasure", Any);
                if (remember == null) { Segna("N36: RememberOrMeasure is there", false, ""); return; }
                int calls = 0;
                Func<object[], object[]> run = args =>
                {
                    try { remember.Invoke(null, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
                    return args;
                };
                run(new object[] { k, (Func<double[]>)(() => { calls++; return null; }), null, null });
                var second = run(new object[] { k, (Func<double[]>)(() => { calls++; return new[] { 80.0, 80.0 }; }), null, null });
                int askedTwice = calls;
                Segna("N36: a failed measurement is not remembered: the next call measures again",
                      askedTwice == 2, "measurement asked " + askedTwice + " times (0.140.0: 1)");
                Segna("N36: and the successful one says «measured»",
                      Convert.ToString(second[3]) == "measured", "source = " + Convert.ToString(second[3]));

                var third = new object[] { k, (Func<double[]>)(() => { throw new InvalidOperationException("must not be asked"); }), null, null };
                var got = (double[])remember.Invoke(null, third);
                Segna("N36: the third call is the archive: 80/80, source «archive», no measurement",
                      got != null && got.Length == 2 && got[0] == 80 && got[1] == 80 &&
                      Convert.ToString(third[3]) == "archive",
                      (got == null ? "null" : string.Join("/", got)) + ", source = " + Convert.ToString(third[3]));

                // (c) FORGETTING.
                bool dropped = (bool)Call(section, "ForgetKey", k, null);
                bool again = (bool)Call(section, "ForgetKey", k, null);
                int before = calls;
                var fourth = new object[] { k, (Func<double[]>)(() => { calls++; return new[] { 81.0, 81.0 }; }), null, null };
                remember.Invoke(null, fourth);
                Segna("N36: a dropped entry is gone once, and the next call measures again",
                      dropped && !again && calls == before + 1 && Convert.ToString(fourth[3]) == "measured",
                      "dropped " + dropped + ", dropped again " + again + ", measured " + (calls - before) +
                      " time(s), source = " + Convert.ToString(fourth[3]));
            }
            catch (Exception ex) { Segna("N36: the overhang archive can be driven", false, ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            }
        }
    }
}
