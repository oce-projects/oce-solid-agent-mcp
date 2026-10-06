using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  TextInThePdf — does the text of a drawing come out of the PDF as TEXT? (GT3-bis)
// =============================================================================
//
//  GT3 proved that the title block SHOWS a property (`INote.GetText()`
//  returns the resolved text). The other question remains: does the PDF that goes to
//  the shop CONTAIN it as text, or has the exporter turned it into
//  outlines? A PDF with outlines opens, looks the same, and is neither searchable nor
//  copyable: no error anywhere.
//
//  THE DRAWING: empty, from the default template, with two notes on the sheet —
//    one written by hand:       TEST123
//    one tied to a property:    $PRP:"Description"  with Description = TEST456
//  The second is the case of the title block: the text that SOLIDWORKS resolves.
//
//  THREE READINGS OF THE PDF, from the poorest to the richest:
//    1. the raw bytes of the file;
//    2. the decompressed /FlateDecode streams (what a bench case can
//       do without depending on anything);
//    3. `pdftotext`, if present (what whoever searches the PDF sees).
//
//  THE RED, in the same run: ABSENT987 is not in the drawing and MUST NOT
//  be found by any of the three readings. A search that finds everything proves
//  nothing.
//
//  USAGE
//      .\Checks\PdfText\bin\PdfText.exe            [--keep]
// =============================================================================

internal static class Program
{
    private const string Inscription = "TEST123";
    private const string Property = "TEST456";
    private const string Absent = "ABSENT987";

    private static int _step;
    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("  " + _step.ToString("00") + ". " + what);
    }

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--keep") >= 0;
        Console.WriteLine();
        Console.WriteLine("TEXT IN THE PDF — does the text of the drawing come out as text? (entry GT3-bis)");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }
        Says("attached to SOLIDWORKS " + sw.RevisionNumber());

        string pdf = Path.Combine(Path.GetTempPath(), "text-in-the-pdf-probe.pdf");
        IModelDoc2 doc = null;
        try
        {
            string drawingMod = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            doc = (IModelDoc2)sw.NewDocument(drawingMod, (int)swDwgPaperSizes_e.swDwgPaperA3size, 0.42, 0.297);
            if (doc == null) { Console.WriteLine("  NewDocument(drawing) -> null"); return 4; }

            var cpm = doc.Extension.get_CustomPropertyManager("");
            int propOutcome = cpm.Add3("Description", (int)swCustomInfoType_e.swCustomInfoText, Property,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            string read, resolved; bool fromCache;
            cpm.Get5("Description", false, out read, out resolved, out fromCache);
            Says("property Description: Add3 -> " + propOutcome + ", read back «" + resolved + "»");

            var n1 = doc.InsertNote(Inscription) as INote;
            if (n1 != null) (n1.GetAnnotation() as IAnnotation)?.SetPosition2(0.05, 0.25, 0);
            var n2 = doc.InsertNote("$PRP:\"Description\"") as INote;
            if (n2 != null) (n2.GetAnnotation() as IAnnotation)?.SetPosition2(0.05, 0.22, 0);
            doc.ClearSelection2(true);
            doc.EditRebuild3();
            Says("note: «" + (n1 == null ? "(not created)" : n1.GetText()) + "», «" +
                 (n2 == null ? "(not created)" : n2.GetText()) + "»");
            if (n1 == null || n2 == null) { Console.WriteLine("  a note was not created"); return 4; }

            // ── THE PDF, as export_pdf writes it ───────────────────────
            var data = sw.GetExportFileData((int)swExportDataFileType_e.swExportPdfData) as IExportPdfData;
            if (data == null) { Console.WriteLine("  no PDF options"); return 4; }
            data.ViewPdfAfterSaving = false;
            data.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportAllSheets, null);
            try { if (File.Exists(pdf)) File.Delete(pdf); } catch { }
            int err = 0, avv = 0;
            bool ok = doc.Extension.SaveAs(pdf, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, data, ref err, ref avv);
            if (!ok || !File.Exists(pdf)) { Console.WriteLine("  PDF not written, code " + err); return 4; }
            byte[] rawItems = File.ReadAllBytes(pdf);
            Says("PDF written: " + rawItems.Length + " bytes");

            // ── LE TRE LETTURE ──────────────────────────────────────────
            string rawText = Encoding.GetEncoding(28591).GetString(rawItems);
            int flows, decompressed;
            string inside = Decompress(rawItems, out flows, out decompressed);
            int textBlocks = Count(inside, "BT\n") + Count(inside, "BT\r") + Count(inside, "BT ");
            int toUnicode = Count(rawText, "/ToUnicode") + Count(inside, "/ToUnicode");
            string extracted = PdfToText(pdf);
            string decoded = DecodeWithCMap(inside);

            Console.WriteLine();
            Console.WriteLine("      compressed streams " + flows + ", decompressed " + decompressed +
                              ", text blocks (BT) " + textBlocks + ", /ToUnicode " + toUnicode);
            Console.WriteLine("      pdftotext: " + (extracted == null ? "unavailable" : extracted.Length + " characters extracted"));
            Console.WriteLine("      decoded with the CMaps: " + decoded.Length + " characters");
            Console.WriteLine();
            Console.WriteLine("      search         raw     streams cmap    pdftotext");
            var outcomes = new Dictionary<string, bool[]>();
            foreach (var s in new[] { Inscription, Property, Absent })
            {
                var r = new[]
                {
                    rawText.Contains(s),
                    inside.Contains(s) || inside.Contains(Hexadecimal(s)),
                    extracted != null && extracted.Contains(s),
                    decoded.Contains(s),
                };
                outcomes[s] = r;
                Console.WriteLine("      " + s.PadRight(14) + " " + Si(r[0]).PadRight(7) + " " + Si(r[1]).PadRight(7) + " " +
                                  Si(r[3]).PadRight(7) + " " + (extracted == null ? "-" : Si(r[2])));
            }
            Console.WriteLine();

            if (outcomes[Absent][3])
            {
                Console.WriteLine("THE DECODING RED DOES NOT HOLD: " + Absent + " comes out of the CMaps. The decoder makes things up.");
                return 7;
            }
            if (outcomes[Inscription][3] && outcomes[Property][3] && extracted != null && outcomes[Inscription][2])
            {
                Console.WriteLine("HOLDS WITHOUT TOOLS: the /ToUnicode CMaps, read here, give the same text as pdftotext.");
                Console.WriteLine("The bench case of GT3-bis is written with this decoder, without depending on Git.");
                return 0;
            }

            if (outcomes[Absent][0] || outcomes[Absent][1] || outcomes[Absent][2])
            {
                Console.WriteLine("THE RED DOES NOT HOLD: " + Absent + " is found, and it is not in the drawing. The search is blind.");
                return 7;
            }
            bool trueText = extracted != null && outcomes[Inscription][2] && outcomes[Property][2];
            bool withoutTools = outcomes[Inscription][1] && outcomes[Property][1];
            if (trueText && withoutTools)
            {
                Console.WriteLine("HOLDS, both halves: the PDF carries the text as text (pdftotext extracts it),");
                Console.WriteLine("and it is found even just by decompressing the streams — a bench case needs no tools.");
                return 0;
            }
            if (trueText)
            {
                Console.WriteLine("THE TEXT IS THERE (pdftotext extracts it), but NOT in the clear in the streams: the glyphs are encoded");
                Console.WriteLine("(" + toUnicode + " /ToUnicode tables). The bench case must decode them, or depend on pdftotext.");
                return 3;
            }
            if (extracted == null)
            {
                Console.WriteLine(withoutTools
                    ? "The text is in the streams; pdftotext is missing for the second half."
                    : "It is not found in the streams, and pdftotext is missing: the probe cannot decide.");
                return withoutTools ? 0 : 8;
            }
            Console.WriteLine("THE TEXT DOES NOT COME OUT AS TEXT: not even pdftotext finds it. " +
                              (textBlocks == 0 ? "Zero BT blocks: the exporter draws outlines." : "There are " + textBlocks + " BT blocks, but not this text."));
            return 6;
        }
        catch (Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine();
            Console.WriteLine("EXCEPTION: " + ex.GetType().Name + ": " + ex.Message);
            return 9;
        }
        finally
        {
            if (doc != null && !keep)
            {
                try { sw.CloseDoc(doc.GetTitle()); } catch { }
                try { if (File.Exists(pdf)) File.Delete(pdf); } catch { }
                Says("drawing closed without saving, PDF removed");
            }
        }
    }

    private static string Si(bool b) { return b ? "SI" : "no"; }

    private static int Count(string text, string what)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }

    private static string Hexadecimal(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(((int)c).ToString("X2"));
        return sb.ToString();
    }

    /// <summary>
    /// All the streams of the file, decompressed and put in a row. Those that
    /// can be decompressed are decompressed (zlib: two header bytes and
    /// then deflate); the others are added as they are.
    /// </summary>
    private static string Decompress(byte[] b, out int flows, out int decompressed)
    {
        var lat = Encoding.GetEncoding(28591);
        string s = lat.GetString(b);
        var outside = new StringBuilder();
        flows = 0; decompressed = 0;
        int i = 0;
        while ((i = s.IndexOf("stream", i, StringComparison.Ordinal)) >= 0)
        {
            if (i >= 3 && s.Substring(i - 3, 3) == "end") { i += 6; continue; }
            int start = i + 6;
            if (start < s.Length && s[start] == '\r') start++;
            if (start < s.Length && s[start] == '\n') start++;
            int fine = s.IndexOf("endstream", start, StringComparison.Ordinal);
            if (fine < 0) break;
            flows++;
            int len = fine - start;
            string text = null;
            if (len > 2)
            {
                try
                {
                    using (var ms = new MemoryStream(b, start + 2, len - 2))
                    using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var o = new MemoryStream())
                    {
                        z.CopyTo(o);
                        text = lat.GetString(o.ToArray());
                        decompressed++;
                    }
                }
                catch { text = null; }
            }
            outside.Append(text ?? s.Substring(start, len)).Append('\n');
            i = fine + 9;
        }
        return outside.ToString();
    }

    /// <summary>
    /// The text of the content streams, DECODED with the /ToUnicode
    /// tables (entry GT3-bis). It is the fourth reading, and the one that a bench
    /// case can do without `pdftotext`.
    ///
    /// Measured late on 24/9: SOLIDWORKS writes the text as glyph indices
    /// (hexadecimal strings in Tj/TJ), and the correspondence with the letters sits
    /// in the /ToUnicode CMaps (`bfchar`, `bfrange`). We do not reconstruct which
    /// character uses which CMap: every string is decoded with each one, and
    /// everything is kept. A string decoded with the wrong table gives
    /// garbage, not TEST123 — and the red ABSENT987 is there to say so if
    /// it were not so.
    /// </summary>
    internal static string DecodeWithCMap(string flows)
    {
        // 1. the tables: from every begincmap..endcmap block
        var tables = new List<Dictionary<int, string>>();
        int i = 0;
        while ((i = flows.IndexOf("begincmap", i, StringComparison.Ordinal)) >= 0)
        {
            int fine = flows.IndexOf("endcmap", i, StringComparison.Ordinal);
            if (fine < 0) break;
            var block = flows.Substring(i, fine - i);
            var t = new Dictionary<int, string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         block, @"beginbfchar(.*?)endbfchar", System.Text.RegularExpressions.RegexOptions.Singleline))
                foreach (System.Text.RegularExpressions.Match c in System.Text.RegularExpressions.Regex.Matches(
                             m.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                    t[Convert.ToInt32(c.Groups[1].Value, 16)] = Utf16(c.Groups[2].Value);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         block, @"beginbfrange(.*?)endbfrange", System.Text.RegularExpressions.RegexOptions.Singleline))
                foreach (System.Text.RegularExpressions.Match c in System.Text.RegularExpressions.Regex.Matches(
                             m.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                {
                    int da = Convert.ToInt32(c.Groups[1].Value, 16), a = Convert.ToInt32(c.Groups[2].Value, 16);
                    int base0 = Convert.ToInt32(c.Groups[3].Value, 16);
                    for (int k = da; k <= a && k - da < 65536; k++) t[k] = char.ConvertFromUtf32(base0 + (k - da));
                }
            if (t.Count > 0) tables.Add(t);
            i = fine + 7;
        }

        // 2. the hexadecimal strings of the text blocks, decoded with
        //    every table, at two bytes per code (Identity-H) and at one.
        var outside = new StringBuilder();
        foreach (System.Text.RegularExpressions.Match s in System.Text.RegularExpressions.Regex.Matches(
                     flows, @"<([0-9A-Fa-f\s]{2,})>"))
        {
            var esa = System.Text.RegularExpressions.Regex.Replace(s.Groups[1].Value, @"\s", "");
            foreach (var t in tables)
                foreach (int planStep in new[] { 4, 2 })
                {
                    if (esa.Length % planStep != 0) continue;
                    var sb = new StringBuilder();
                    bool everything = true;
                    for (int k = 0; k < esa.Length; k += planStep)
                    {
                        string c;
                        if (t.TryGetValue(Convert.ToInt32(esa.Substring(k, planStep), 16), out c)) sb.Append(c);
                        else { everything = false; break; }
                    }
                    if (everything && sb.Length > 0) outside.Append(sb).Append('\n');
                }
        }
        return outside.ToString();
    }

    private static string Utf16(string esa)
    {
        var sb = new StringBuilder();
        for (int k = 0; k + 4 <= esa.Length; k += 4) sb.Append((char)Convert.ToInt32(esa.Substring(k, 4), 16));
        return sb.ToString();
    }

    private static string PdfToText(string pdf)
    {
        foreach (var exe in new[]
                 {
                     @"C:\Program Files\Git\mingw64\bin\pdftotext.exe",
                     "pdftotext.exe",
                 })
        {
            try
            {
                var psi = new ProcessStartInfo(exe, "-q \"" + pdf + "\" -")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30000);
                    if (p.ExitCode == 0) return text;
                }
            }
            catch { }
        }
        return null;
    }
}
