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
//  TestoNelPdf — il testo di una tavola esce dal PDF come TESTO? (GT3-bis)
// =============================================================================
//
//  GT3 ha provato che il cartiglio MOSTRA una proprieta' (`INote.GetText()`
//  restituisce il testo risolto). Resta l'altra domanda: il PDF che va in
//  officina la CONTIENE come testo, o l'esportatore l'ha fatta diventare
//  contorni? Un PDF coi contorni si apre, si vede uguale, e non e' cercabile ne'
//  copiabile: nessun errore da nessuna parte.
//
//  LA TAVOLA: vuota, dal modello di default, con due note sul foglio —
//    una scritta a mano:        PROVA123
//    una legata a proprieta':   $PRP:"Descrizione"  con Descrizione = PROVA456
//  La seconda e' il caso del cartiglio: il testo che SOLIDWORKS risolve.
//
//  TRE LETTURE DEL PDF, dalla piu' povera alla piu' ricca:
//    1. i byte grezzi del file;
//    2. i flussi /FlateDecode decompressi (quello che un caso di banco puo'
//       fare senza dipendere da niente);
//    3. `pdftotext`, se c'e' (quello che vede chi cerca nel PDF).
//
//  IL ROSSO, nella stessa corsa: ASSENTE987 non e' nella tavola e NON deve
//  trovarsi con nessuna delle tre letture. Una ricerca che trova tutto non
//  prova niente.
//
//  USO
//      .\Controlli\TestoNelPdf\bin\TestoNelPdf.exe            [--tieni]
// =============================================================================

internal static class Programma
{
    private const string Scritta = "PROVA123";
    private const string Proprieta = "PROVA456";
    private const string Assente = "ASSENTE987";

    private static int _passo;
    private static void Dice(string cosa)
    {
        _passo++;
        Console.WriteLine("  " + _passo.ToString("00") + ". " + cosa);
    }

    private static int Main(string[] argomenti)
    {
        bool tieni = Array.IndexOf(argomenti, "--tieni") >= 0;
        Console.WriteLine();
        Console.WriteLine("TESTO NEL PDF — il testo della tavola esce come testo? (voce GT3-bis)");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }
        Dice("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        string pdf = Path.Combine(Path.GetTempPath(), "testo-nel-pdf-sonda.pdf");
        IModelDoc2 doc = null;
        try
        {
            string modTavola = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            doc = (IModelDoc2)sw.NewDocument(modTavola, (int)swDwgPaperSizes_e.swDwgPaperA3size, 0.42, 0.297);
            if (doc == null) { Console.WriteLine("  NewDocument(tavola) -> null"); return 4; }

            var cpm = doc.Extension.get_CustomPropertyManager("");
            int esitoProp = cpm.Add3("Descrizione", (int)swCustomInfoType_e.swCustomInfoText, Proprieta,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            string letta, risolta; bool dalCache;
            cpm.Get5("Descrizione", false, out letta, out risolta, out dalCache);
            Dice("proprieta' Descrizione: Add3 -> " + esitoProp + ", riletta «" + risolta + "»");

            var n1 = doc.InsertNote(Scritta) as INote;
            if (n1 != null) (n1.GetAnnotation() as IAnnotation)?.SetPosition2(0.05, 0.25, 0);
            var n2 = doc.InsertNote("$PRP:\"Descrizione\"") as INote;
            if (n2 != null) (n2.GetAnnotation() as IAnnotation)?.SetPosition2(0.05, 0.22, 0);
            doc.ClearSelection2(true);
            doc.EditRebuild3();
            Dice("note: «" + (n1 == null ? "(non nata)" : n1.GetText()) + "», «" +
                 (n2 == null ? "(non nata)" : n2.GetText()) + "»");
            if (n1 == null || n2 == null) { Console.WriteLine("  una nota non e' nata"); return 4; }

            // ── IL PDF, come lo scrive esporta_pdf ──────────────────────
            var dati = sw.GetExportFileData((int)swExportDataFileType_e.swExportPdfData) as IExportPdfData;
            if (dati == null) { Console.WriteLine("  niente opzioni PDF"); return 4; }
            dati.ViewPdfAfterSaving = false;
            dati.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportAllSheets, null);
            try { if (File.Exists(pdf)) File.Delete(pdf); } catch { }
            int err = 0, avv = 0;
            bool ok = doc.Extension.SaveAs(pdf, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, dati, ref err, ref avv);
            if (!ok || !File.Exists(pdf)) { Console.WriteLine("  PDF non scritto, codice " + err); return 4; }
            byte[] grezzi = File.ReadAllBytes(pdf);
            Dice("PDF scritto: " + grezzi.Length + " byte");

            // ── LE TRE LETTURE ──────────────────────────────────────────
            string crudo = Encoding.GetEncoding(28591).GetString(grezzi);
            int flussi, decompressi;
            string dentro = Decomprimi(grezzi, out flussi, out decompressi);
            int blocchiDiTesto = Conta(dentro, "BT\n") + Conta(dentro, "BT\r") + Conta(dentro, "BT ");
            int toUnicode = Conta(crudo, "/ToUnicode") + Conta(dentro, "/ToUnicode");
            string estratto = PdfToText(pdf);

            Console.WriteLine();
            Console.WriteLine("      flussi compressi " + flussi + ", decompressi " + decompressi +
                              ", blocchi di testo (BT) " + blocchiDiTesto + ", /ToUnicode " + toUnicode);
            Console.WriteLine("      pdftotext: " + (estratto == null ? "non disponibile" : estratto.Length + " caratteri estratti"));
            Console.WriteLine();
            Console.WriteLine("      cerca          grezzo  flussi  pdftotext");
            var esiti = new Dictionary<string, bool[]>();
            foreach (var s in new[] { Scritta, Proprieta, Assente })
            {
                var r = new[]
                {
                    crudo.Contains(s),
                    dentro.Contains(s) || dentro.Contains(Esadecimale(s)),
                    estratto != null && estratto.Contains(s),
                };
                esiti[s] = r;
                Console.WriteLine("      " + s.PadRight(14) + " " + Si(r[0]).PadRight(7) + " " + Si(r[1]).PadRight(7) + " " +
                                  (estratto == null ? "-" : Si(r[2])));
            }
            Console.WriteLine();

            if (esiti[Assente][0] || esiti[Assente][1] || esiti[Assente][2])
            {
                Console.WriteLine("IL ROSSO NON REGGE: " + Assente + " si trova, e nella tavola non c'e'. La ricerca e' cieca.");
                return 7;
            }
            bool testoVero = estratto != null && esiti[Scritta][2] && esiti[Proprieta][2];
            bool senzaAttrezzi = esiti[Scritta][1] && esiti[Proprieta][1];
            if (testoVero && senzaAttrezzi)
            {
                Console.WriteLine("REGGE, tutte e due le meta': il PDF porta il testo come testo (pdftotext lo estrae),");
                Console.WriteLine("e lo si trova anche solo decomprimendo i flussi — un caso di banco non ha bisogno di attrezzi.");
                return 0;
            }
            if (testoVero)
            {
                Console.WriteLine("IL TESTO C'E' (pdftotext lo estrae), ma NON in chiaro nei flussi: i glifi sono codificati");
                Console.WriteLine("(" + toUnicode + " tabelle /ToUnicode). Il caso di banco deve decodificarle, o dipendere da pdftotext.");
                return 3;
            }
            if (estratto == null)
            {
                Console.WriteLine(senzaAttrezzi
                    ? "Nei flussi il testo c'e'; pdftotext manca per la seconda meta'."
                    : "Nei flussi non si trova, e pdftotext manca: la sonda non puo' decidere.");
                return senzaAttrezzi ? 0 : 8;
            }
            Console.WriteLine("IL TESTO NON ESCE COME TESTO: nemmeno pdftotext lo trova. " +
                              (blocchiDiTesto == 0 ? "Zero blocchi BT: l'esportatore disegna contorni." : "Ci sono " + blocchiDiTesto + " blocchi BT, ma non questo testo."));
            return 6;
        }
        catch (Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine();
            Console.WriteLine("ECCEZIONE: " + ex.GetType().Name + ": " + ex.Message);
            return 9;
        }
        finally
        {
            if (doc != null && !tieni)
            {
                try { sw.CloseDoc(doc.GetTitle()); } catch { }
                try { if (File.Exists(pdf)) File.Delete(pdf); } catch { }
                Dice("tavola chiusa senza salvare, PDF tolto");
            }
        }
    }

    private static string Si(bool b) { return b ? "SI" : "no"; }

    private static int Conta(string testo, string cosa)
    {
        int n = 0, i = 0;
        while ((i = testo.IndexOf(cosa, i, StringComparison.Ordinal)) >= 0) { n++; i += cosa.Length; }
        return n;
    }

    private static string Esadecimale(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(((int)c).ToString("X2"));
        return sb.ToString();
    }

    /// <summary>
    /// Tutti i flussi del file, decompressi e messi in fila. Si decomprimono
    /// quelli che si lasciano decomprimere (zlib: due byte di intestazione e
    /// poi deflate); gli altri si aggiungono cosi' come sono.
    /// </summary>
    private static string Decomprimi(byte[] b, out int flussi, out int decompressi)
    {
        var lat = Encoding.GetEncoding(28591);
        string s = lat.GetString(b);
        var fuori = new StringBuilder();
        flussi = 0; decompressi = 0;
        int i = 0;
        while ((i = s.IndexOf("stream", i, StringComparison.Ordinal)) >= 0)
        {
            if (i >= 3 && s.Substring(i - 3, 3) == "end") { i += 6; continue; }
            int inizio = i + 6;
            if (inizio < s.Length && s[inizio] == '\r') inizio++;
            if (inizio < s.Length && s[inizio] == '\n') inizio++;
            int fine = s.IndexOf("endstream", inizio, StringComparison.Ordinal);
            if (fine < 0) break;
            flussi++;
            int lung = fine - inizio;
            string testo = null;
            if (lung > 2)
            {
                try
                {
                    using (var ms = new MemoryStream(b, inizio + 2, lung - 2))
                    using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var o = new MemoryStream())
                    {
                        z.CopyTo(o);
                        testo = lat.GetString(o.ToArray());
                        decompressi++;
                    }
                }
                catch { testo = null; }
            }
            fuori.Append(testo ?? s.Substring(inizio, lung)).Append('\n');
            i = fine + 9;
        }
        return fuori.ToString();
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
                    string testo = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30000);
                    if (p.ExitCode == 0) return testo;
                }
            }
            catch { }
        }
        return null;
    }
}
