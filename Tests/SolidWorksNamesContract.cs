using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE NAMES SOLIDWORKS WRITES ONLY AS LOCALIZED TEXT: ONE TABLE, A COLUMN PER LANGUAGE.
    ///
    /// The library, the bench and the probes ask for a KEY of
    /// <c>Oce.Solid.Agent.Core/Localization/solidworks-names.json</c>, never for a
    /// word. What this group holds true without SOLIDWORKS:
    ///
    ///  * the embedded table parses, has the columns «it» and «en», English as the
    ///    fallback, and an English word for every key;
    ///  * the column is chosen from what <c>GetCurrentLanguage</c> says: an
    ///    unknown language (or none) falls back to English and SAYS so;
    ///  * a <c>{SW:key}</c> of a case is replaced with the word of the column of
    ///    the domain of the key, and a key that is not in the table raises: a typo
    ///    must not become a name SOLIDWORKS does not have;
    ///  * every <c>{SW:key}</c> of every case and every literal key of the library
    ///    exists in the table, and the nine standard views have a row;
    ///  * a malformed table is refused with the reason;
    ///  * the decisions that need no language: a column of the cut-list table is
    ///    told by its TYPE first and by the property names of every column of the
    ///    table second, and the standard views map to the ids of swStandardViews_e.
    ///
    /// The library is called by reflection on the BUILT dll (no SOLIDWORKS), as
    /// Zc215IdentityContract does.
    /// </summary>
    internal static class SolidWorksNamesContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        internal static void RunAll()
        {
            Group("the names SOLIDWORKS writes as localized text: one table, a column per language");

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the table of the SOLIDWORKS names", library + " is missing: build the library first (build.ps1)");
                return;
            }
            Assembly asm;
            try
            {
                var redist = WhereTheInteropIs();
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
                asm = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            Type tableType = asm.GetType("Oce.Solid.Agent.Core.SolidWorksNameTable", false);
            Type operations = asm.GetType("Oce.Solid.Agent.Core.SwOperations", false);
            if (tableType == null || operations == null)
            {
                Check("the table type and SwOperations are in the library", false,
                      "SolidWorksNameTable " + (tableType != null) + ", SwOperations " + (operations != null));
                return;
            }

            object table;
            try { table = tableType.GetMethod("Embedded", Any).Invoke(null, null); }
            catch (TargetInvocationException ex) { Check("the embedded table parses", false, (ex.InnerException ?? ex).Message); return; }
            Check("the embedded table parses", table != null);

            var columns = ((IEnumerable<string>)tableType.GetProperty("Columns").GetValue(table, null)).ToList();
            var keys = ((IEnumerable<string>)tableType.GetProperty("Keys").GetValue(table, null)).ToList();
            string fallback = (string)tableType.GetProperty("FallbackColumn").GetValue(table, null);
            Check("the table has the columns it and en, and English is the fallback",
                  columns.Contains("it") && columns.Contains("en") && fallback == "en",
                  "columns " + string.Join(", ", columns) + ", fallback " + fallback);

            Func<string, string, string> get = (key, which) => (string)tableType.GetMethod("Get", Any).Invoke(table, new object[] { key, which });
            var withoutEnglish = keys.Where(k => string.IsNullOrEmpty(get(k, "en"))).ToList();
            Check("every key has an English word", withoutEnglish.Count == 0 && keys.Count > 0,
                  keys.Count + " keys, without English: " + string.Join(", ", withoutEnglish));
            var withoutItalian = keys.Where(k => get(k, "it") == get(k, "en")).ToList();
            Check("no row repeats its English word in the Italian column (a row that does not translate is not a row)",
                  withoutItalian.Count == 0, string.Join(", ", withoutItalian));

            // ── the column from the language SOLIDWORKS says ───────────────────
            Func<string, object[]> column = language =>
            {
                var args = new object[] { language, null };
                var answer = tableType.GetMethod("ColumnForLanguage", Any).Invoke(table, args);
                return new[] { answer, args[1] };
            };
            var italian = column("italian");
            var italianUpper = column("ITALIAN");
            var english = column("english");
            Check("«italian» is column it, «english» is column en, with no warning, whatever the capitals",
                  (string)italian[0] == "it" && italian[1] == null && (string)italianUpper[0] == "it" &&
                  (string)english[0] == "en" && english[1] == null,
                  italian[0] + "/" + italian[1] + ", " + italianUpper[0] + ", " + english[0] + "/" + english[1]);
            var german = column("german");
            var nothing = column(null);
            Check("a language with no column («german»), or none at all, falls back to English and says so",
                  (string)german[0] == "en" && ((string)german[1] ?? "").Contains("german") &&
                  (string)nothing[0] == "en" && nothing[1] != null,
                  german[0] + "/" + german[1] + "; " + nothing[0] + "/" + nothing[1]);

            // ── {SW:key} in a case ─────────────────────────────────────────────
            Func<string, Func<string, string>, string> expand = (text, which) =>
                (string)tableType.GetMethod("Expand", Any).Invoke(table, new object[] { text, which });
            string inItalian = expand("{SW:feature.cut_extrude}1 / {SW:view.front}", d => "it");
            string inEnglish = expand("{SW:feature.cut_extrude}1 / {SW:view.front}", d => "en");
            string mixed = expand("{SW:feature.cut_extrude}1 / {SW:view.front}", d => d == "feature" ? "en" : "it");
            Check("{SW:key} is replaced with the word of the column of the domain of the key",
                  inItalian == get("feature.cut_extrude", "it") + "1 / " + get("view.front", "it") &&
                  inEnglish == "Cut-Extrude1 / *Front" &&
                  mixed == "Cut-Extrude1 / " + get("view.front", "it"),
                  inItalian + " | " + inEnglish + " | " + mixed);
            Check("a text with no {SW:...} comes back as it was", expand("Boss-Extrude1", d => "it") == "Boss-Extrude1");
            string typo = null;
            try { expand("{SW:feature.cut_extrud}1", d => "en"); }
            catch (TargetInvocationException ex) { typo = ex.InnerException == null ? ex.Message : ex.InnerException.Message; }
            Check("a key that is not in the table raises, and lists the keys there are",
                  typo != null && typo.Contains("feature.cut_extrude"), typo ?? "it did not raise");

            // ── the keys in use exist ──────────────────────────────────────────
            var used = new SortedSet<string>(StringComparer.Ordinal);
            var cases = Path.Combine(Root(), "Bench", "cases");
            if (Directory.Exists(cases))
                foreach (var f in Directory.GetFiles(cases, "*.json", SearchOption.AllDirectories))
                    foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{SW:([A-Za-z0-9_.]+)\}")) used.Add(m.Groups[1].Value);
            var missingForCases = used.Where(k => !keys.Contains(k)).ToList();
            Check("every {SW:key} of every case is a key of the table",
                  used.Count > 0 && missingForCases.Count == 0,
                  used.Count + " keys used, missing: " + string.Join(", ", missingForCases));

            var inCode = new SortedSet<string>(StringComparer.Ordinal);
            var core = Path.Combine(Root(), "Oce.Solid.Agent.Core");
            foreach (var f in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(f),
                         "\\.(?:All|Get|Name|Spellings)\\(\"((?:feature|document|view|callout|cutlist|equation)\\.[a-z0-9_.]*[a-z0-9_])\""))
                    inCode.Add(m.Groups[1].Value);
            }
            var missingForCode = inCode.Where(k => !keys.Contains(k)).ToList();
            Check("every literal key the library asks the table for is in the table",
                  inCode.Count > 0 && missingForCode.Count == 0,
                  inCode.Count + " keys, missing: " + string.Join(", ", missingForCode));

            // ── the standard views: ids, and a row each ────────────────────────
            var ids = new Dictionary<string, int>
            {
                ["front"] = 1, ["back"] = 2, ["left"] = 3, ["right"] = 4, ["top"] = 5,
                ["bottom"] = 6, ["isometric"] = 7, ["trimetric"] = 8, ["dimetric"] = 9,
            };
            var wrongIds = new List<string>();
            foreach (var kv in ids)
            {
                int id = (int)operations.GetMethod("StandardViewId", Any).Invoke(null, new object[] { kv.Key });
                if (id != kv.Value) wrongIds.Add(kv.Key + "=" + id);
            }
            int unknownView = (int)operations.GetMethod("StandardViewId", Any).Invoke(null, new object[] { "oblique" });
            Check("the nine standard views map to the ids of swStandardViews_e, an unknown name to -1",
                  wrongIds.Count == 0 && unknownView == -1, string.Join(", ", wrongIds) + " / oblique " + unknownView);
            Check("the nine standard views have a row view.* in the table",
                  ids.Keys.All(k => keys.Contains("view." + k)),
                  string.Join(", ", ids.Keys.Where(k => !keys.Contains("view." + k))));

            Func<double[], double[], bool> same = (a, b) =>
                (bool)operations.GetMethod("SameRotation", Any).Invoke(null, new object[] { a, b });
            var front = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
            var frontAgain = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0.5, 0.2, 0.1, 2, 0, 0, 0 };   // moved and scaled: the same rotation
            var back = new double[] { -1, 0, 0, 0, 1, 0, 0, 0, -1, 0, 0, 0, 1, 0, 0, 0 };
            var zeros = new double[16];
            Check("two orientations are the same when their 3x3 rotations agree (translation and scale do not count)",
                  same(front, frontAgain) && !same(front, back) && !same(front, zeros) && !same(front, null),
                  same(front, frontAgain) + ", " + same(front, back) + ", " + same(front, zeros) + ", " + same(front, null));

            // ── a column of the cut-list table: the type first, the names of every language second ──
            Func<int, string, string> role = (type, property) =>
                (string)operations.GetMethod("ColumnRole", Any).Invoke(null, new object[] { type, property });
            Check("the cut-list column of the item number and of the quantity are told by their TYPE, with no name",
                  role(401, null) == "item_number" && role(402, "") == "quantity",
                  role(401, null) + ", " + role(402, ""));
            // The words come from the table itself (every column), so that no word of a language is typed here.
            string lengthIt = get("cutlist.property.length", "it"), angle1It = get("cutlist.property.angle1", "it");
            string descriptionIt = get("cutlist.property.description", "it");
            Check("a cut-list property is the length, the angles or the description in ANY language of the table",
                  role(404, get("cutlist.property.length", "en")) == "length" && role(404, lengthIt.ToLowerInvariant()) == "length" &&
                  role(404, angle1It) == "angle1" && role(404, get("cutlist.property.angle2", "en")) == "angle2" &&
                  role(404, "Description") == "description" && role(404, descriptionIt) == "description",
                  role(404, lengthIt) + ", " + role(404, angle1It) + ", " + role(404, descriptionIt));
            Check("any other property is not a workshop column (author, treatment, material...)",
                  role(404, "AUTHOR") == null && role(404, "Material") == null && role(0, "") == null,
                  role(404, "AUTHOR") + ", " + role(404, "Material") + ", " + role(0, ""));

            // ── the exceptions of the gate on this file are its Italian column, no more and no less ──
            ItalianColumnAndTheGate(keys, get);

            // ── a malformed table is refused with the reason ───────────────────
            Func<string, string> refusal = json =>
            {
                try { tableType.GetMethod("Parse", Any).Invoke(null, new object[] { json }); return null; }
                catch (TargetInvocationException ex) { return (ex.InnerException ?? ex).Message; }
            };
            string good = "{\"columns\":{\"it\":{\"solidworks_language\":\"italian\"},\"en\":{\"solidworks_language\":\"english\"}}," +
                          "\"names\":{\"a.b\":{\"domain\":\"ui\",\"it\":\"x\",\"en\":\"y\"}}}";
            Check("a minimal good table parses", refusal(good) == null, refusal(good));
            string noColumn = refusal(good.Replace("\"it\":\"x\"", "\"de\":\"x\""));
            string badDomain = refusal(good.Replace("\"ui\"", "\"menu\""));
            string noLanguage = refusal(good.Replace("{\"solidworks_language\":\"italian\"}", "{}"));
            string noFallback = refusal(good.Replace("\"columns\":{", "\"fallback_column\":\"fr\",\"columns\":{"));
            Check("a cell in a column that is not declared, a domain that is not feature or ui, a column with no language, a fallback that is no column: refused",
                  noColumn != null && noColumn.Contains("de") && badDomain != null && badDomain.Contains("menu") &&
                  noLanguage != null && noLanguage.Contains("solidworks_language") && noFallback != null && noFallback.Contains("fr"),
                  noColumn + " | " + badDomain + " | " + noLanguage + " | " + noFallback);
        }

        /// <summary>
        /// The Italian column of the table is Italian text the language gate has to be told about, and
        /// the entries of Checks/language/exceptions.json that do it are narrow by the gate's own rule
        /// (no entry may take away ten hits or more of a file). This keeps them honest: every word of
        /// the Italian column is covered by an entry on this file, and every alternative of those
        /// entries is a word of the Italian column (an entry cannot cover a word the table does not have).
        /// </summary>
        private static void ItalianColumnAndTheGate(List<string> keys, Func<string, string, string> get)
        {
            var file = Path.Combine(Root(), "Checks", "language", "exceptions.json");
            if (!File.Exists(file)) { NotHere("the exceptions of the gate on the table", file + " is missing"); return; }
            var entries = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .DeserializeObject(File.ReadAllText(file)) as object[];
            var patterns = new List<string>();
            foreach (var one in entries ?? new object[0])
            {
                var entry = one as Dictionary<string, object>;
                if (entry == null || !entry.ContainsKey("file") || !entry.ContainsKey("pattern")) continue;
                // The entries written for this file (a wide entry that happens to match its path is not one of them).
                if (((string)entry["file"]).Contains("solidworks-names") &&
                    Regex.IsMatch("Oce.Solid.Agent.Core/Localization/solidworks-names.json", (string)entry["file"]))
                    patterns.Add((string)entry["pattern"]);
            }
            var italian = keys.Select(k => get(k, "it")).Where(w => !string.IsNullOrEmpty(w)).Distinct().ToList();
            var uncovered = italian.Where(w => !patterns.Any(p => Regex.IsMatch(w, "^(?:" + p + ")$"))).ToList();
            Check("every word of the Italian column is covered by an entry of the gate on the table",
                  patterns.Count > 0 && uncovered.Count == 0,
                  patterns.Count + " entries, uncovered: " + string.Join(", ", uncovered));
            var alternatives = new List<string>();
            foreach (var p in patterns)
                foreach (var alternative in Regex.Split(p, @"(?<!\\)\|"))
                    alternatives.Add(Regex.Unescape(alternative));
            var strangers = alternatives.Where(a => !italian.Contains(a)).ToList();
            Check("no entry of the gate on the table covers a word the Italian column does not have",
                  strangers.Count == 0, string.Join(", ", strangers));
        }
    }
}
