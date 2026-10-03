using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// IL TESTO DI UN PDF ESPORTATO, letto senza attrezzi (voci GT3-bis, Z-C9).
    ///
    /// Misurato il 24/9/2026 con `Checks/PdfText`: il PDF che SOLIDWORKS
    /// esporta porta il testo della tavola COME TESTO — cercabile, copiabile,
    /// anche quello risolto da una proprieta' — ma nei flussi e' scritto come
    /// INDICI DI GLIFO dentro stringhe esadecimali, e le lettere stanno nelle
    /// tabelle /ToUnicode. Cercare «PROVA123» nei byte non lo trova, e nemmeno
    /// nei flussi decompressi; `pdftotext` lo trova, ma c'e' solo dove c'e' Git.
    ///
    /// Qui: flussi /FlateDecode decompressi, tabelle `bfchar`/`bfrange` lette,
    /// e ogni stringa esadecimale decodificata con ogni tabella. Non si
    /// ricostruisce quale font usa quale tabella: una stringa decodificata con
    /// la tabella sbagliata da' spazzatura, non la parola cercata — e chi usa
    /// questa classe deve sempre avere accanto una parola che NON c'e', per
    /// vederlo.
    /// </summary>
    public static class TestoDelPdf
    {
        public static string Leggi(string percorso)
        {
            var b = File.ReadAllBytes(percorso);
            var flussi = Decomprimi(b);
            return Decodifica(flussi) + "\n" + Letterali(flussi);
        }

        private static string Decomprimi(byte[] b)
        {
            var lat = Encoding.GetEncoding(28591);
            string s = lat.GetString(b);
            var fuori = new StringBuilder();
            int i = 0;
            while ((i = s.IndexOf("stream", i, StringComparison.Ordinal)) >= 0)
            {
                if (i >= 3 && s.Substring(i - 3, 3) == "end") { i += 6; continue; }
                int inizio = i + 6;
                if (inizio < s.Length && s[inizio] == '\r') inizio++;
                if (inizio < s.Length && s[inizio] == '\n') inizio++;
                int fine = s.IndexOf("endstream", inizio, StringComparison.Ordinal);
                if (fine < 0) break;
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
                        }
                    }
                    catch { testo = null; }
                }
                fuori.Append(testo ?? s.Substring(inizio, lung)).Append('\n');
                i = fine + 9;
            }
            return fuori.ToString();
        }

        private static string Decodifica(string flussi)
        {
            var tabelle = new List<Dictionary<int, string>>();
            int i = 0;
            while ((i = flussi.IndexOf("begincmap", i, StringComparison.Ordinal)) >= 0)
            {
                int fine = flussi.IndexOf("endcmap", i, StringComparison.Ordinal);
                if (fine < 0) break;
                var blocco = flussi.Substring(i, fine - i);
                var t = new Dictionary<int, string>();
                foreach (Match m in Regex.Matches(blocco, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                    foreach (Match c in Regex.Matches(m.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                        t[Convert.ToInt32(c.Groups[1].Value, 16)] = Utf16(c.Groups[2].Value);
                foreach (Match m in Regex.Matches(blocco, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                    foreach (Match c in Regex.Matches(m.Groups[1].Value,
                                 @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                    {
                        int da = Convert.ToInt32(c.Groups[1].Value, 16), a = Convert.ToInt32(c.Groups[2].Value, 16);
                        int base0 = Convert.ToInt32(c.Groups[3].Value, 16);
                        for (int k = da; k <= a && k - da < 65536; k++) t[k] = char.ConvertFromUtf32(base0 + (k - da));
                    }
                if (t.Count > 0) tabelle.Add(t);
                i = fine + 7;
            }

            var fuori = new StringBuilder();
            foreach (Match s in Regex.Matches(flussi, @"<([0-9A-Fa-f\s]{2,})>"))
            {
                var esa = Regex.Replace(s.Groups[1].Value, @"\s", "");
                foreach (var t in tabelle)
                    foreach (int passo in new[] { 4, 2 })
                    {
                        if (esa.Length % passo != 0) continue;
                        var sb = new StringBuilder();
                        bool tutto = true;
                        for (int k = 0; k < esa.Length; k += passo)
                        {
                            string c;
                            if (t.TryGetValue(Convert.ToInt32(esa.Substring(k, passo), 16), out c)) sb.Append(c);
                            else { tutto = false; break; }
                        }
                        if (tutto && sb.Length > 0) fuori.Append(sb).Append('\n');
                    }
            }
            return fuori.ToString();
        }

        /// <summary>Le stringhe letterali (...) dei flussi, per i PDF che le usano.</summary>
        private static string Letterali(string flussi)
        {
            var fuori = new StringBuilder();
            foreach (Match m in Regex.Matches(flussi, @"\(((?:\\.|[^\\)])*)\)\s*T[Jj]"))
                fuori.Append(m.Groups[1].Value).Append('\n');
            return fuori.ToString();
        }

        private static string Utf16(string esa)
        {
            var sb = new StringBuilder();
            for (int k = 0; k + 4 <= esa.Length; k += 4) sb.Append((char)Convert.ToInt32(esa.Substring(k, 4), 16));
            return sb.ToString();
        }
    }
}
