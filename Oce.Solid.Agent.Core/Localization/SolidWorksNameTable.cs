using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE TABLE OF THE NAMES SOLIDWORKS WRITES ONLY AS LOCALIZED TEXT
    /// (<c>Localization/solidworks-names.json</c>), WITH NO INTEROP.
    ///
    /// ── WHY A TABLE, AND WHY IT IS THE SECOND LEVEL ──────────────────────
    ///
    /// The bridge is written in English and reads the names of SOLIDWORKS in
    /// English. SOLIDWORKS writes some of them in the language of the
    /// installation (<c>Boss-Extrude1</c> and the Italian name of the same
    /// feature, <c>*Front</c> and the Italian name of the same view, the word of
    /// a hole callout, a cut-list property). Between the two there is this table:
    /// the code asks for a KEY (<c>feature.cut_extrude</c>) and the column that
    /// matches the language SOLIDWORKS runs in gives the word. The FIRST level is
    /// not in the table at all: whatever the API answers without a name (a feature
    /// type from <c>GetTypeName2</c>, a base plane by its normal, a standard view
    /// by what it does when shown, a table column by its type) is read from the
    /// API. A name goes here only when SOLIDWORKS offers nothing else to read.
    ///
    /// ── ONE FILE FOR THE LIBRARY, THE BENCH AND THE PROBES ────────────────
    ///
    /// This class has no <c>SolidWorks.Interop</c> in it, so the bench and the
    /// probes of <c>Checks\</c> compile the same file (as a link) and the
    /// contract suite calls it on the built library by reflection. The selection
    /// of the column from what SOLIDWORKS reports is <c>SolidWorksLanguage</c>.
    ///
    /// Written in C# 7.3 on purpose: the bench and the contract suite compile at
    /// 7.3.
    /// </summary>
    public sealed class SolidWorksNameTable
    {
        /// <summary>The name of the embedded resource, and of the file next to it.</summary>
        public const string ResourceName = "solidworks-names.json";

        /// <summary>The domain of the names of features, documents and sheets.</summary>
        public const string FeatureDomain = "feature";

        /// <summary>The domain of the texts of the interface.</summary>
        public const string UiDomain = "ui";

        private readonly Dictionary<string, string> _domain = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> _names =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _languageToColumn =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _columns = new List<string>();
        private string _fallback = "en";

        private static SolidWorksNameTable _embedded;
        private static readonly object Lock = new object();

        /// <summary>The columns, in the order of the file.</summary>
        public IList<string> Columns { get { return _columns; } }

        /// <summary>The column used when the language has none (English).</summary>
        public string FallbackColumn { get { return _fallback; } }

        /// <summary>Every key of the table.</summary>
        public IEnumerable<string> Keys { get { return _names.Keys; } }

        /// <summary>The table of the assembly this class is compiled into (the embedded resource).</summary>
        public static SolidWorksNameTable Embedded()
        {
            lock (Lock)
            {
                if (_embedded != null) return _embedded;
                var assembly = typeof(SolidWorksNameTable).Assembly;
                string resource = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(ResourceName, StringComparison.OrdinalIgnoreCase));
                if (resource == null)
                    throw new InvalidOperationException(
                        "The table of the SOLIDWORKS names (" + ResourceName + ") is not embedded in " +
                        assembly.FullName + ": the project must list it as an EmbeddedResource.");
                // A StreamReader reads UTF-8 (with or without the BOM) unless told otherwise.
                using (var stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream))
                    _embedded = Parse(reader.ReadToEnd());
                return _embedded;
            }
        }

        /// <summary>The table from the text of the JSON file. Raises with the reason when the file is malformed.</summary>
        public static SolidWorksNameTable Parse(string json)
        {
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json)
                       as Dictionary<string, object>;
            if (root == null) throw new FormatException("The table of the SOLIDWORKS names is not a JSON object.");

            var table = new SolidWorksNameTable();
            var fallback = root.ContainsKey("fallback_column") ? root["fallback_column"] as string : null;
            if (!string.IsNullOrEmpty(fallback)) table._fallback = fallback;

            var columns = root.ContainsKey("columns") ? root["columns"] as Dictionary<string, object> : null;
            if (columns == null || columns.Count == 0)
                throw new FormatException("The table of the SOLIDWORKS names has no «columns».");
            foreach (var columnName in columns.Keys)
            {
                table.DeclareColumn(columnName, columns[columnName] as Dictionary<string, object>);
            }
            if (!table._columns.Contains(table._fallback))
                throw new FormatException("The fallback column «" + table._fallback + "» is not one of the columns.");

            var names = root.ContainsKey("names") ? root["names"] as Dictionary<string, object> : null;
            if (names == null || names.Count == 0)
                throw new FormatException("The table of the SOLIDWORKS names has no «names».");
            foreach (var key in names.Keys)
            {
                table.DeclareRow(key, names[key] as Dictionary<string, object>);
            }
            return table;
        }

        private void DeclareColumn(string columnName, Dictionary<string, object> info)
        {
            _columns = _columns.Concat(new[] { columnName }).ToList();
            var language = info != null && info.ContainsKey("solidworks_language") ? info["solidworks_language"] as string : null;
            if (string.IsNullOrEmpty(language))
                throw new FormatException("The column «" + columnName + "» does not say its «solidworks_language».");
            foreach (var one in language.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                _languageToColumn[one.Trim()] = columnName;
        }

        private void DeclareRow(string key, Dictionary<string, object> cells)
        {
            if (cells == null) throw new FormatException("The row «" + key + "» is not an object.");
            string domain = cells.ContainsKey("domain") ? cells["domain"] as string : null;
            if (domain != FeatureDomain && domain != UiDomain)
                throw new FormatException("The row «" + key + "» has the domain «" + domain +
                                          "»: it must be «" + FeatureDomain + "» or «" + UiDomain + "».");
            _domain[key] = domain;
            var words = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var cellName in cells.Keys)
            {
                if (cellName == "domain") continue;
                if (!_columns.Contains(cellName))
                    throw new FormatException("The row «" + key + "» has a cell «" + cellName +
                                              "» that is not a column of the table.");
                var word = cells[cellName] as string;
                if (!string.IsNullOrEmpty(word)) words[cellName] = word;
            }
            _names[key] = words;
        }

        /// <summary>True when the table has the key.</summary>
        public bool Has(string key)
        {
            return key != null && _names.ContainsKey(key);
        }

        /// <summary>«feature» or «ui».</summary>
        public string Domain(string key)
        {
            Check(key);
            return _domain[key];
        }

        /// <summary>
        /// The word of the column for the key. A column with no word for that key
        /// answers with the English one; null only when neither has it.
        /// </summary>
        public string Get(string key, string column)
        {
            Check(key);
            var words = _names[key];
            string found;
            if (column != null && words.TryGetValue(column, out found)) return found;
            return words.TryGetValue(_fallback, out found) ? found : null;
        }

        /// <summary>
        /// EVERY SPELLING of the key in every column, the one of
        /// <paramref name="firstColumn"/> first, with no repeats. It is what a
        /// check that RECOGNISES a name uses: a template made in another language
        /// than the interface (a company part template, a table template) still
        /// carries its own words, and recognising all of them is the robust choice.
        /// </summary>
        public IList<string> All(string key, string firstColumn)
        {
            Check(key);
            var words = _names[key];
            var spellings = new List<string>();
            string found;
            if (firstColumn != null && words.TryGetValue(firstColumn, out found)) spellings.Add(found);
            foreach (var column in _columns)
                if (words.TryGetValue(column, out found) && !spellings.Contains(found, StringComparer.Ordinal))
                    spellings.Add(found);
            return spellings;
        }

        /// <summary>The spellings of the key in every column, in the order of the columns.</summary>
        public IList<string> All(string key)
        {
            return All(key, null);
        }

        /// <summary>
        /// The column that matches what <c>ISldWorks.GetCurrentLanguage</c> says
        /// («italian», «english», «german»...). A language the table does not know
        /// answers with the fallback column and says so in <paramref name="warning"/>.
        /// </summary>
        public string ColumnForLanguage(string solidWorksLanguage, out string warning)
        {
            warning = null;
            string column;
            if (!string.IsNullOrWhiteSpace(solidWorksLanguage) &&
                _languageToColumn.TryGetValue(solidWorksLanguage.Trim(), out column))
                return column;
            warning = string.IsNullOrWhiteSpace(solidWorksLanguage)
                ? "SOLIDWORKS did not say which language it runs in: the English names of " + ResourceName + " are used."
                : "SOLIDWORKS runs in «" + solidWorksLanguage.Trim() + "» and " + ResourceName +
                  " has no column for it: the English names are used (columns: " + string.Join(", ", _columns) + ").";
            return _fallback;
        }

        private const string TokenStart = "{SW:";

        /// <summary>
        /// Replaces every <c>{SW:key}</c> of a text with the word of the key for
        /// the column that <paramref name="columnOfDomain"/> gives to the domain of
        /// that key. It is how the cases of the bench name a feature without
        /// knowing the language: <c>{SW:feature.cut_extrude}1</c>. An unknown key
        /// raises, with the keys the table has: a typo must not become a name
        /// SOLIDWORKS does not have.
        /// </summary>
        public string Expand(string text, Func<string, string> columnOfDomain)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(TokenStart, StringComparison.Ordinal) < 0) return text;
            var pieces = new List<string>();
            int at = 0;
            while (true)
            {
                int open = text.IndexOf(TokenStart, at, StringComparison.Ordinal);
                int close = open < 0 ? -1 : text.IndexOf('}', open);
                if (open < 0 || close < 0) { pieces.Add(text.Substring(at)); break; }
                pieces.Add(text.Substring(at, open - at));
                string key = text.Substring(open + TokenStart.Length, close - open - TokenStart.Length);
                pieces.Add(WordOfToken(key, columnOfDomain));
                at = close + 1;
            }
            return string.Concat(pieces);
        }

        private string WordOfToken(string key, Func<string, string> columnOfDomain)
        {
            if (!Has(key))
                throw new KeyNotFoundException(
                    "The case names {SW:" + key + "} and " + ResourceName + " has no such key. It has: " +
                    string.Join(", ", _names.Keys.OrderBy(k => k, StringComparer.Ordinal)));
            string word = Get(key, columnOfDomain(_domain[key]));
            if (word == null)
                throw new KeyNotFoundException("{SW:" + key + "} has no word in the column asked for nor in English.");
            return word;
        }

        private void Check(string key)
        {
            if (key == null || !_names.ContainsKey(key))
                throw new KeyNotFoundException(
                    "The table of the SOLIDWORKS names has no key «" + key + "». It has: " +
                    string.Join(", ", _names.Keys.OrderBy(k => k, StringComparer.Ordinal)));
        }
    }
}
