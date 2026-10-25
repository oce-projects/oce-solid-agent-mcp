using System;
using System.Collections.Generic;
using System.Linq;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Pure matching of material names, with no SOLIDWORKS reference, so that the
    /// contract suite can compile and test it on machines without SOLIDWORKS.
    ///
    /// WHY STAGE 4 IS BY WHOLE WORDS
    /// In the usage report of 24/10/2026 «PLA» matched three plastics and a PVC
    /// («Custom Plastic», «PVC ... plasticized») by plain substring. A word that
    /// is contained in another word is not the same material: the match must be
    /// on whole words, and the language of the name must not matter.
    /// </summary>
    internal static class MaterialNameMatch
    {
        /// <summary>
        /// The indexes into <paramref name="names"/> found by the FIRST stage that
        /// finds at least one. Empty if nothing matches.
        /// </summary>
        internal static List<int> Candidates(IList<string> names, string searched)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(searched) || names == null) return result;

            string compactSearched = Compact(searched);

            // 1. the exact name
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], searched, StringComparison.Ordinal)) result.Add(i);
            if (result.Count > 0) return result;

            // 2. the exact name ignoring capitals and spaces
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(Compact(names[i]), compactSearched, StringComparison.OrdinalIgnoreCase)) result.Add(i);
            if (result.Count > 0) return result;

            // 3. the name inside the parentheses: «1.0037 (S235JR)» asked for as «S235JR»
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(Compact(InParentheses(names[i])), compactSearched, StringComparison.OrdinalIgnoreCase)) result.Add(i);
            if (result.Count > 0) return result;

            // 4. every word asked for appears as a whole word in the name
            for (int i = 0; i < names.Count; i++)
                if (ContainsWholeWords(names[i], searched)) result.Add(i);
            return result;
        }

        /// <summary>The string without whitespace; empty for null.</summary>
        internal static string Compact(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// The text between the first '(' and the last ')' when both exist and are
        /// in order; otherwise the string itself. Empty for null.
        /// </summary>
        internal static string InParentheses(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int a = s.IndexOf('(');
            int b = s.LastIndexOf(')');
            return (a >= 0 && b > a) ? s.Substring(a + 1, b - a - 1) : s;
        }

        /// <summary>
        /// True only if EVERY whitespace-separated token of <paramref name="searched"/>
        /// occurs in <paramref name="name"/> (ignoring case) at least once, bounded on
        /// each side by a character that is not a letter or digit. No tokens: false.
        /// </summary>
        internal static bool ContainsWholeWords(string name, string searched)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(searched)) return false;
            var tokens = searched.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return false;

            foreach (var token in tokens)
                if (!OccursAsWord(name, token)) return false;
            return true;
        }

        private static bool OccursAsWord(string name, string token)
        {
            int from = 0;
            while (from <= name.Length - token.Length)
            {
                int at = name.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return false;

                bool leftOk = at == 0 || !char.IsLetterOrDigit(name[at - 1]);
                int end = at + token.Length;
                bool rightOk = end == name.Length || !char.IsLetterOrDigit(name[end]);
                if (leftOk && rightOk) return true;

                from = at + 1;
            }
            return false;
        }
    }
}
