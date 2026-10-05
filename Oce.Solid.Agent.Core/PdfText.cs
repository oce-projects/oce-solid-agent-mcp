using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE TEXT OF AN EXPORTED PDF, read without tools (entries GT3-bis, Z-C9).
    ///
    /// Measured on 24/9/2026 with `Checks/PdfText`: the PDF that SOLIDWORKS
    /// exports carries the text of the drawing AS TEXT — searchable, copyable,
    /// even the one resolved from a property — but in the streams it is written as
    /// GLYPH INDICES inside hexadecimal strings, and the letters sit in the
    /// /ToUnicode tables. Searching for «PROVA123» in the bytes does not find it, nor
    /// in the decompressed streams; `pdftotext` finds it, but it is only there where Git is.
    ///
    /// Here: /FlateDecode streams decompressed, `bfchar`/`bfrange` tables read,
    /// and every hexadecimal string decoded with every table. It is not
    /// reconstructed which font uses which table: a string decoded with
    /// the wrong table gives garbage, not the word searched for — and whoever uses
    /// this class must always have alongside a word that is NOT there, to
    /// see it.
    /// </summary>
    public static class PdfText
    {
        public static string Read(string filePath)
        {
            var b = File.ReadAllBytes(filePath);
            var flows = Decompress(b);
            return Decode(flows) + "\n" + Literals(flows);
        }

        private static string Decompress(byte[] b)
        {
            var lat = Encoding.GetEncoding(28591);
            string s = lat.GetString(b);
            var outside = new StringBuilder();
            int i = 0;
            while ((i = s.IndexOf("stream", i, StringComparison.Ordinal)) >= 0)
            {
                if (i >= 3 && s.Substring(i - 3, 3) == "end") { i += 6; continue; }
                int start = i + 6;
                if (start < s.Length && s[start] == '\r') start++;
                if (start < s.Length && s[start] == '\n') start++;
                int fine = s.IndexOf("endstream", start, StringComparison.Ordinal);
                if (fine < 0) break;
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
                        }
                    }
                    catch { text = null; }
                }
                outside.Append(text ?? s.Substring(start, len)).Append('\n');
                i = fine + 9;
            }
            return outside.ToString();
        }

        private static string Decode(string flows)
        {
            var tables = new List<Dictionary<int, string>>();
            int i = 0;
            while ((i = flows.IndexOf("begincmap", i, StringComparison.Ordinal)) >= 0)
            {
                int fine = flows.IndexOf("endcmap", i, StringComparison.Ordinal);
                if (fine < 0) break;
                var block = flows.Substring(i, fine - i);
                var t = new Dictionary<int, string>();
                foreach (Match m in Regex.Matches(block, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                    foreach (Match c in Regex.Matches(m.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                        t[Convert.ToInt32(c.Groups[1].Value, 16)] = Utf16(c.Groups[2].Value);
                foreach (Match m in Regex.Matches(block, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                    foreach (Match c in Regex.Matches(m.Groups[1].Value,
                                 @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                    {
                        int da = Convert.ToInt32(c.Groups[1].Value, 16), a = Convert.ToInt32(c.Groups[2].Value, 16);
                        int base0 = Convert.ToInt32(c.Groups[3].Value, 16);
                        for (int k = da; k <= a && k - da < 65536; k++) t[k] = char.ConvertFromUtf32(base0 + (k - da));
                    }
                if (t.Count > 0) tables.Add(t);
                i = fine + 7;
            }

            var outside = new StringBuilder();
            foreach (Match s in Regex.Matches(flows, @"<([0-9A-Fa-f\s]{2,})>"))
            {
                var esa = Regex.Replace(s.Groups[1].Value, @"\s", "");
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

        /// <summary>The literal strings (...) of the streams, for the PDFs that use them.</summary>
        private static string Literals(string flows)
        {
            var outside = new StringBuilder();
            foreach (Match m in Regex.Matches(flows, @"\(((?:\\.|[^\\)])*)\)\s*T[Jj]"))
                outside.Append(m.Groups[1].Value).Append('\n');
            return outside.ToString();
        }

        private static string Utf16(string esa)
        {
            var sb = new StringBuilder();
            for (int k = 0; k + 4 <= esa.Length; k += 4) sb.Append((char)Convert.ToInt32(esa.Substring(k, 4), 16));
            return sb.ToString();
        }
    }
}
