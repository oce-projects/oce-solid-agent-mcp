using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// THE Z-C21.13 FIXES OF THE STEP EXPORT AND OF THE DOCUMENT UNITS, WHERE
    /// THE BENCH CANNOT REACH (TODO entry Z-C21.13, export and units group).
    ///
    ///   L17-7  MmPerLengthUnit. The bench has no step that sets the units of a
    ///          document, so the factors that the probe probe_feet_inches
    ///          measured on 29/9/2026 (IEquationMgr.get_Value of 12 mm and
    ///          1000 mm, read in every swLengthUnit_e) are checked here, and so
    ///          are the two units that answer NaN on purpose: feet-and-inches
    ///          (its two readings disagree) and micron (the reading is the
    ///          nanometre's).
    ///   C5-7   Step242Refusal and Step242PathProblem. The bench machine has no
    ///          SOLIDWORKS MBD licence, so the bench sees one answer of
    ///          PublishSTEP242File (4, case lim_step_ap242_needs_the_mbd_licence)
    ///          and the .step refusal (case lim_step_ap242_wants_a_stp_path).
    ///          The other answers (0, 1, 3, a value the enum does not list) are
    ///          checked here. And on the source: a library file that sets
    ///          swStepAP has no bare 242, the value that wrote an AP203 file.
    ///
    /// The methods are static, with no interop in the signature, and are called
    /// on the BUILT library by reflection: no SOLIDWORKS. On library 0.143.0
    /// every check is red: the three methods are missing, and
    /// OperazioniEsporta.cs sets the protocol to 242.
    /// </summary>
    internal static class Zc2113ExportUnitsContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Gruppo("the Z-C21.13 STEP export and unit fixes the bench cannot reach (C5-7, L17-7)");

            NoBare242WhereSwStepApIsSet();

            var library = Path.Combine(Radice(), "Riscontro.Disegno", "bin", "Release", "Riscontro.Disegno.dll");
            if (!File.Exists(library))
            {
                NonQui("the pure rules of the Z-C21.13 export and unit fixes",
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
                sw = asm.GetType("Riscontro.Disegno.OperazioniSw", false);
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Segna("OperazioniSw is in the library", false, "type not found"); return; }

            LengthUnits(sw);
            Step242Answers(sw);
            Step242Paths(sw);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method + " is missing on this library");
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // ── L17-7: millimetres per unit of the document ──────────────────────
        //
        // swLengthUnit_e from the interop, as the probe printed it: swMM 0,
        // swCM 1, swMETER 2, swINCHES 3, swFEET 4, swFEETINCHES 5, swANGSTROM 6,
        // swNANOMETER 7, swMICRON 8, swMIL 9, swUIN 10.
        private static void LengthUnits(Type sw)
        {
            const string known = "L17-7: every unit the probe settled converts with its measured factor " +
                                 "(mm, cm, m, in, ft, mil, angstrom 1E-07, nanometre 1E-06, microinch 2.54E-05)";
            const string unknown = "L17-7: feet-and-inches (5), micron (8) and values outside swLengthUnit_e " +
                                   "(-1, 11) answer NaN, not a guess";
            var factors = new List<Tuple<int, string, double>>
            {
                Tuple.Create(0, "swMM", 1.0),
                Tuple.Create(1, "swCM", 10.0),
                Tuple.Create(2, "swMETER", 1000.0),
                Tuple.Create(3, "swINCHES", 25.4),
                Tuple.Create(4, "swFEET", 304.8),
                Tuple.Create(9, "swMIL", 0.0254),
                Tuple.Create(6, "swANGSTROM", 1E-07),
                Tuple.Create(7, "swNANOMETER", 1E-06),
                Tuple.Create(10, "swUIN", 2.54E-05),
            };
            var nan = new List<Tuple<int, string>>
            {
                Tuple.Create(5, "swFEETINCHES"),
                Tuple.Create(8, "swMICRON"),
                Tuple.Create(-1, "unreadable (-1)"),
                Tuple.Create(11, "outside the enum (11)"),
            };
            var wrong = new List<string>();
            var guessed = new List<string>();
            try
            {
                foreach (var f in factors)
                {
                    double got = (double)Call(sw, "MmPerLengthUnit", f.Item1);
                    if (double.IsNaN(got) || Math.Abs(got - f.Item3) > 1e-12 * f.Item3)
                        wrong.Add(f.Item2 + " " + got.ToString("R") + " (expected " + f.Item3.ToString("R") + ")");
                }
                foreach (var n in nan)
                {
                    double got = (double)Call(sw, "MmPerLengthUnit", n.Item1);
                    if (!double.IsNaN(got)) guessed.Add(n.Item2 + " " + got.ToString("R"));
                }
            }
            catch (Exception ex)
            {
                string why = ex.GetType().Name + ": " + ex.Message;
                Segna(known, false, why);
                Segna(unknown, false, why);
                return;
            }
            Segna(known, wrong.Count == 0,
                  wrong.Count == 0 ? factors.Count + " units, each with its factor" : string.Join(" | ", wrong));
            Segna(unknown, guessed.Count == 0,
                  guessed.Count == 0
                      ? "NaN for all four: the caller skips the comparison and says so"
                      : "a number where the probe could not settle one: " + string.Join(" | ", guessed));
        }

        // ── C5-7: the answers of PublishSTEP242File ──────────────────────────
        //
        // swStep242Error_e: swPublishStep242_Success 0, swPublishStep242_InvalidPath 1,
        // swPublishStep242_UnknownError 3, swPublishStep242_MBDLicenseNotAvailable 4.
        private static void Step242Answers(Type sw)
        {
            const string licence = "C5-7: success (0) is no refusal, and the licence answer (4) names " +
                                   "SOLIDWORKS MBD and the two protocols that remain";
            const string others = "C5-7: invalid path (1), unknown error (3) and a value the enum does not " +
                                  "list (2) are refusals that name what came back";
            const string path = "C:\\out\\part.stp";
            string r0, r1, r2, r3, r4;
            try
            {
                r0 = (string)Call(sw, "Step242Refusal", 0, path);
                r1 = (string)Call(sw, "Step242Refusal", 1, path);
                r2 = (string)Call(sw, "Step242Refusal", 2, path);
                r3 = (string)Call(sw, "Step242Refusal", 3, path);
                r4 = (string)Call(sw, "Step242Refusal", 4, path);
            }
            catch (Exception ex)
            {
                string why = ex.GetType().Name + ": " + ex.Message;
                Segna(licence, false, why);
                Segna(others, false, why);
                return;
            }
            Func<string, string[], bool> says = (text, parts) =>
                text != null && parts.All(p => text.IndexOf(p, StringComparison.Ordinal) >= 0);
            Segna(licence,
                  r0 == null && says(r4, new[] { "SOLIDWORKS MBD", "swPublishStep242_MBDLicenseNotAvailable", "AP214", "AP203" }),
                  "0 -> " + (r0 ?? "null") + " | 4 -> " + (r4 ?? "null"));
            Segna(others,
                  says(r1, new[] { "swPublishStep242_InvalidPath", path, ".stp" }) &&
                  says(r3, new[] { "swPublishStep242_UnknownError" }) &&
                  says(r2, new[] { "answered 2", "does not list" }),
                  "1 -> " + (r1 ?? "null") + " | 3 -> " + (r3 ?? "null") + " | 2 -> " + (r2 ?? "null"));
        }

        // ── C5-7: the path AP242 takes ───────────────────────────────────────
        private static void Step242Paths(Type sw)
        {
            const string name = "C5-7: AP242 takes a .stp path (either case) and refuses .step and any other " +
                                "extension before the call";
            string stp, upper, step, other;
            try
            {
                stp = (string)Call(sw, "Step242PathProblem", "C:\\out\\part.stp");
                upper = (string)Call(sw, "Step242PathProblem", "C:\\out\\PART.STP");
                step = (string)Call(sw, "Step242PathProblem", "C:\\out\\part.step");
                other = (string)Call(sw, "Step242PathProblem", "C:\\out\\part.igs");
            }
            catch (Exception ex) { Segna(name, false, ex.GetType().Name + ": " + ex.Message); return; }
            bool ok = stp == null && upper == null &&
                      step != null && step.Contains(".stp path") && step.Contains("swPublishStep242_InvalidPath") &&
                      other != null && other.Contains(".igs") && other.Contains(".stp");
            Segna(name, ok,
                  ".stp -> " + (stp ?? "null") + " | .STP -> " + (upper ?? "null") +
                  " | .step -> " + (step ?? "null") + " | .igs -> " + (other ?? "null"));
        }

        // ── C5-7: no bare 242 where swStepAP is set ──────────────────────────
        //
        // Until 0.143.0 OperazioniEsporta.cs mapped AP242 to the protocol value 242
        // and wrote it into swStepAP: accepted, read back, and an AP203 file.
        // The rule is narrowed to the files whose CODE (comments and strings
        // blanked by ObsoleteCallScanner.Strip) names swStepAP, so that a 242
        // elsewhere in the library, a size or a count, is not taken for this;
        // «PublishSTEP242File» and «swStep242Error_e» are identifiers, not
        // numbers, and the pattern does not see them.
        private static void NoBare242WhereSwStepApIsSet()
        {
            const string name = "C5-7: a library file that sets swStepAP has no bare 242 in its code " +
                                "(swStepAP = 242 wrote an AP203 file)";
            var folder = Path.Combine(Radice(), "Riscontro.Disegno");
            if (!Directory.Exists(folder)) { Segna(name, false, "missing " + folder); return; }
            var bare242 = new Regex(@"(?<![\w.])242(?![\w.])");
            var found = new List<string>();
            int files = 0;
            try
            {
                foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        file.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    var state = new StripState();
                    var code = new List<Tuple<int, string>>();
                    int number = 0;
                    foreach (var raw in File.ReadAllLines(file))
                    {
                        number++;
                        string comment;
                        code.Add(Tuple.Create(number, ObsoleteCallScanner.Strip(raw, state, out comment)));
                    }
                    if (!code.Any(c => c.Item2.Contains("swStepAP"))) continue;
                    files++;
                    foreach (var c in code)
                        if (bare242.IsMatch(c.Item2))
                            found.Add(Path.GetFileName(file) + ":" + c.Item1 + " " + c.Item2.Trim());
                }
            }
            catch (Exception ex) { Segna(name, false, ex.GetType().Name + ": " + ex.Message); return; }
            Segna(name, files > 0 && found.Count == 0,
                  files == 0
                      ? "no library file names swStepAP in its code: the rule has nothing to look at, and that is not a pass"
                      : found.Count == 0
                          ? files + " file(s) set swStepAP, none with a bare 242"
                          : string.Join(" | ", found));
        }
    }
}
