using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// THE DRAWING FIXES OF Z-C21.8 (RETURNS AND MESSAGES) THAT THE BENCH
    /// CANNOT REACH.
    ///
    ///   L13-6  esporta_pdf. The answer of IExportPdfData.SetSheets is read, and
    ///          a page count that cannot be taken is -1 and a refusal
    ///          (PdfPageCountRefusal), not a 0 that skipped the check.
    ///   L16-1  saldatura_in_tavola. The text count measured on 26/9/2026 (1
    ///          without the drawn symbol, 2 with it) is now one of the checks
    ///          (WeldSymbolTextsWrong).
    ///   M38    tolleranza_geometrica_in_tavola. The Ø is read back with
    ///          IGtol.GetFrameDiameterSymbols (GtolDiameterWrong), and a wrong
    ///          frame is removed and recounted (RemoveNewAnnotation).
    ///   M40    vista_sviluppo. No refusal drops the answer of RemoveNewView.
    ///   M41    sezione_in_tavola, dettaglio_in_tavola. The new view is the one
    ///          the call returns, and the line or circle a refusal leaves on the
    ///          sheet is removed and recounted.
    ///   N47    quota_in_tavola. A walk over the dimension texts that broke off
    ///          gives null, not the texts read before the break.
    ///   N80    quota_in_tavola. «Not born» is the count of dimensions, asked
    ///          first; a dimension born without its object is described, not
    ///          answered with a second annotation. The site the lot left out,
    ///          QuotaFraDue of the turned view (OperazioniTondoTavola.cs), was
    ///          done after library 0.145.0 and has its own check, red on 0.145.0.
    ///
    /// None of these paths can be driven from a bench case: SOLIDWORKS would
    /// have to answer false, null or throw on demand. So the pure rules are
    /// called on the BUILT library by reflection (no SOLIDWORKS), and the call
    /// sites are read in the source, because rules that nobody calls would keep
    /// this group green after a revert. On library 0.144.0 every check is red:
    /// the rules are missing, the page counter (then under an Italian name)
    /// answered 0 for a PDF it could not count, and each call site is in its
    /// old form.
    /// </summary>
    internal static class Zc2189DrawingContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Gruppo("drawing returns and messages the bench cannot reach (Z-C21.8: L13-6, L16-1, M38, M40, M41, N47, N80)");

            CallSites();

            var library = Path.Combine(Radice(), "Riscontro.Disegno", "bin", "Release", "Riscontro.Disegno.dll");
            if (!File.Exists(library))
            {
                NonQui("the pure rules of the Z-C21.8 drawing fixes",
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

            PdfPages(sw);
            WeldSymbolTexts(sw);
            GtolDiameter(sw);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method + " is missing on this library");
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // ── L13-6: the pages of the PDF ──────────────────────────────────────
        private static void PdfPages(Type sw)
        {
            const string rule = "L13-6: an uncounted PDF (-1) is refused, a count that differs is refused, a match passes";
            try
            {
                var unknown = (string)Call(sw, "PdfPageCountRefusal", -1, 3, "x.pdf");
                var differs = (string)Call(sw, "PdfPageCountRefusal", 1, 3, "x.pdf");
                var match = (string)Call(sw, "PdfPageCountRefusal", 3, 3, "x.pdf");
                Segna(rule, unknown != null && differs != null && match == null,
                      "(-1, 3) " + (unknown == null ? "passes" : "refused") +
                      ", (1, 3) " + (differs == null ? "passes" : "refused") +
                      ", (3, 3) " + (match == null ? "passes" : "refused: " + match));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }

            const string counter = "L13-6: CountPdfPages counts 3 pages, and answers -1 (not 0) for no page objects and for no file";
            string folder = Path.Combine(Path.GetTempPath(), "zc2189_pdf_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                string three = Path.Combine(folder, "three.pdf");
                string none = Path.Combine(folder, "none.pdf");
                // One page-tree node and three pages, written the three ways the
                // counter must read: a space, no space, and more keys after.
                File.WriteAllText(three,
                    "%PDF-1.4\n1 0 obj << /Type /Pages /Kids [2 0 R 3 0 R 4 0 R] /Count 3 >> endobj\n" +
                    "2 0 obj << /Type /Page >> endobj\n3 0 obj << /Type/Page >> endobj\n" +
                    "4 0 obj << /Type /Page /Parent 1 0 R >> endobj\n%%EOF\n");
                // Page objects hidden in a compressed stream: nothing to count.
                File.WriteAllText(none, "%PDF-1.5\n1 0 obj << /Type /ObjStm /N 4 >> stream\nxxxx\nendstream endobj\n%%EOF\n");
                int n3 = (int)Call(sw, "CountPdfPages", three);
                int n0 = (int)Call(sw, "CountPdfPages", none);
                int nMissing = (int)Call(sw, "CountPdfPages", Path.Combine(folder, "missing.pdf"));
                Segna(counter, n3 == 3 && n0 == -1 && nMissing == -1,
                      "three pages " + n3 + ", no page objects " + n0 + ", no file " + nMissing);
            }
            catch (Exception ex) { Segna(counter, false, ex.GetType().Name + ": " + ex.Message); }
            finally { try { Directory.Delete(folder, true); } catch { } }
        }

        // ── L16-1: the texts of a weld symbol ────────────────────────────────
        private static void WeldSymbolTexts(Type sw)
        {
            const string rule = "L16-1: 1 text (no symbol drawn) and an unreadable count are wrong, 2 and 3 are not";
            try
            {
                var one = (string)Call(sw, "WeldSymbolTextsWrong", 1);
                var unreadable = (string)Call(sw, "WeldSymbolTextsWrong", -1);
                var two = (string)Call(sw, "WeldSymbolTextsWrong", 2);
                var three = (string)Call(sw, "WeldSymbolTextsWrong", 3);
                Segna(rule, one != null && unreadable != null && two == null && three == null,
                      "1 " + (one == null ? "passes" : "wrong") + ", -1 " + (unreadable == null ? "passes" : "wrong") +
                      ", 2 " + (two == null ? "passes" : "wrong: " + two) + ", 3 " + (three == null ? "passes" : "wrong: " + three));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── M38: the Ø of a tolerance frame ──────────────────────────────────
        private static void GtolDiameter(Type sw)
        {
            const string rule = "M38: a Ø read back that differs from the one asked is wrong; unreadable is wrong only when asked";
            try
            {
                var cases = new[]
                {
                    Tuple.Create(true, 1, false), Tuple.Create(false, 0, false),
                    Tuple.Create(true, 0, true), Tuple.Create(false, 1, true),
                    Tuple.Create(true, -1, true), Tuple.Create(false, -1, false),
                };
                var bad = cases.Where(c => ((string)Call(sw, "GtolDiameterWrong", c.Item1, c.Item2) != null) != c.Item3)
                               .Select(c => "asked " + c.Item1 + ", read " + c.Item2 + (c.Item3 ? " should be wrong" : " should pass"))
                               .ToList();
                Segna(rule, bad.Count == 0, bad.Count == 0 ? "six cases as expected" : string.Join("; ", bad));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── the call sites, in the source ────────────────────────────────────
        //
        // Whole files, not method bodies: each name looked for below lives in
        // one file only, and the counts include the definition.
        private static void CallSites()
        {
            string dir = Path.Combine(Radice(), "Riscontro.Disegno");

            string pdf = Read(dir, "OperazioniTavola.cs");
            if (pdf != null)
            {
                bool kept = Regex.IsMatch(pdf, @"\w+\s*=\s*pdf\.SetSheets\(");
                bool bare = Regex.IsMatch(pdf, @"(?m)^\s*pdf\.SetSheets\(");
                int refusals = Count(pdf, "PdfPageCountRefusal(");
                Segna("L13-6: EsportaPdf keeps the answer of SetSheets and asks PdfPageCountRefusal",
                      kept && !bare && refusals >= 2,
                      "answer kept " + kept + ", bare call " + bare + ", PdfPageCountRefusal " + refusals + " (with the definition)");
            }

            string weld = Read(dir, "OperazioniSaldaturaTavola.cs");
            if (weld != null)
            {
                bool called = Regex.IsMatch(weld, @"= WeldSymbolTextsWrong\(\w+\);");
                Segna("L16-1: SaldaturaInTavola checks the text count with WeldSymbolTextsWrong",
                      called, called ? "called" : "not called");
            }

            string gtol = Read(dir, "OperazioniAnnotazioniTavola.cs");
            if (gtol != null)
            {
                bool reread = gtol.Contains(".GetFrameDiameterSymbols(");
                bool judged = Regex.IsMatch(gtol, @"= GtolDiameterWrong\(");
                int removals = Count(gtol, "RemoveNewAnnotation(");
                Segna("M38: the frame's Ø is read back and judged, and a wrong or unfilled frame is removed",
                      reread && judged && removals >= 3,
                      "GetFrameDiameterSymbols " + reread + ", GtolDiameterWrong " + judged +
                      ", RemoveNewAnnotation " + removals + " (definition and two calls expected)");
            }

            string flat = Read(dir, "OperazioniSviluppoTavola.cs");
            if (flat != null)
            {
                int bare = Regex.Matches(flat, @"(?m)^\s*RemoveNewView\(").Count;
                int all = Count(flat, "RemoveNewView(");
                Segna("M40: no refusal of vista_sviluppo drops the answer of RemoveNewView",
                      bare == 0 && all >= 6, bare + " bare calls, " + all + " with the definition (six expected)");
            }

            string section = Read(dir, "OperazioniSezioneTavola.cs");
            if (section != null)
            {
                bool returned = Regex.IsMatch(section, @"IView sectionView = \w+ as IView;");
                bool used = section.Contains("sectionView.GetFaceHatchCount(") && section.Contains("(sectionView)");
                int cleanups = Count(section, "RemoveSheetSketchSegment(");
                Segna("M41: the section is the returned view, and the cutting line of a refusal is removed",
                      returned && used && cleanups >= 3,
                      "returned view " + returned + ", read from it " + used + ", RemoveSheetSketchSegment " + cleanups +
                      " (definition and two calls expected)");
            }

            string detail = Read(dir, "OperazioniDettaglioTavola.cs");
            if (detail != null)
            {
                bool returned = Regex.IsMatch(detail, @"IView detailView = \w+ as IView;");
                bool used = detail.Contains("detailView.GetName2(") && detail.Contains("(detailView)");
                int cleanups = Count(detail, "RemoveSheetSketchSegment(");
                bool viewRemoved = detail.Contains("RemoveNewView(");
                Segna("M41: the detail is the returned view, and the circle (and unlinked view) of a refusal are removed",
                      returned && used && cleanups >= 3 && viewRemoved,
                      "returned view " + returned + ", read from it " + used + ", circle removals " + cleanups +
                      ", view removal " + viewRemoved);
            }

            // M41, the other edge of the fallback: with no view returned, the
            // view read is the last of the chain, and a name that was on the
            // sheet before the call is a view of the user. Neither refusal
            // deletes it.
            if (section != null && detail != null)
            {
                bool sectionGuarded = section.Contains("!nomiViste.Contains(nomeSezione) && RemoveNewView(");
                bool detailGuarded = detail.Contains("bool viewIsNew = !nomiViste.Contains(nomeDettaglio);") &&
                                     detail.Contains("viewIsNew && RemoveNewView(");
                Segna("M41: a refusal never deletes a view that was on the sheet before the call",
                      sectionGuarded && detailGuarded,
                      "section guarded " + sectionGuarded + ", detail guarded " + detailGuarded);
            }

            string texts = Read(dir, "OperazioniTestiQuote.cs");
            string dims = Read(dir, "OperazioniQuoteTavola.cs");
            if (texts != null && dims != null)
            {
                // The outer catch of the walk is the first catch after the step
                // to the next dimension.
                int step = texts.IndexOf("dd = dd.GetNext5() as IDisplayDimension;", StringComparison.Ordinal);
                int outer = step < 0 ? -1 : texts.IndexOf("catch", step, StringComparison.Ordinal);
                bool nullOnBreak = outer >= 0 &&
                                   texts.Substring(outer).StartsWith("catch { return null; }", StringComparison.Ordinal);
                bool mapped = dims.Contains("testiDelleQuote == null ? null");
                Segna("N47: a broken walk over the dimension texts gives null, and quota_in_tavola says «not known»",
                      nullOnBreak && mapped,
                      (step < 0 ? "the step to the next dimension is not found" : "null on break " + nullOnBreak) +
                      ", mapped to null " + mapped);
            }
            if (dims != null)
            {
                int objectFirst = Count(dims, "== null || NonCresciuta(");
                int described = Count(dims, "RecordBornWithoutObject(");
                Segna("N80: «not born» asks the count first, and a dimension born without its object is described",
                      objectFirst == 0 && described >= 5,
                      objectFirst + " sites asking the object first, RecordBornWithoutObject " + described +
                      " (definition and four calls expected)");
            }

            // N80 AT THE SITE THE LOT LEFT OUT: QuotaFraDue of the turned view
            // (OperazioniTondoTavola.cs), whose three callers (lengths, diameters,
            // pairs of silhouette edges without a face) took a dimension born
            // without its object for «not born». Red on library 0.145.0: the
            // guard was still «q == null || NonCresciuta(» and the callers had no
            // second outcome to tell.
            string turned = Read(dir, "OperazioniTondoTavola.cs");
            if (turned != null)
            {
                int objectFirst = Count(turned, "== null || NonCresciuta(");
                // The line right before the outcome is told is the count's own
                // «not born».
                int told = turned.IndexOf("bornWithoutObject = q == null;", StringComparison.Ordinal);
                int lineStart = told < 0 ? -1 : turned.LastIndexOf('\n', told);
                int previousStart = lineStart <= 0 ? -1 : turned.LastIndexOf('\n', lineStart - 1);
                string previous = previousStart < 0 ? "" : turned.Substring(previousStart + 1, lineStart - previousStart - 1).Trim();
                bool countFirst = told >= 0 && previous.StartsWith("if (NonCresciuta(", StringComparison.Ordinal) &&
                                  previous.EndsWith(")) return null;", StringComparison.Ordinal);
                int asked = Count(turned, "out bornWithoutObject);");
                int notBornOnly = Count(turned, "if (q == null && !bornWithoutObject)");
                int described = Count(turned, "RecordBornWithoutObject(");
                Segna("N80 in the turned view: QuotaFraDue asks the count first, and its three callers describe a dimension born without its object",
                      objectFirst == 0 && countFirst && asked == 3 && notBornOnly == 2 && described == 3,
                      objectFirst + " sites asking the object first, count first and outcome told " + countFirst +
                      ", callers reading the outcome " + asked + " (3 expected), «not born» only when not grown " +
                      notBornOnly + " (2 expected), RecordBornWithoutObject " + described + " (3 expected)");
            }
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
