using System;
using System.IO;
using Oce.Solid.Agent.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE NAMES OF SOLIDWORKS IN THE CASES, AND THE LANGUAGE OF THE RUN.
    ///
    /// A case that has to name a feature SOLIDWORKS wrote says
    /// <c>{SW:feature.cut_extrude}1</c>, not the word an installation happens to
    /// use: the placeholder is replaced, in the text of the case before it is
    /// parsed, with the word of the table (<c>Localization/solidworks-names.json</c>)
    /// for the language the run is in. So the same case runs on an Italian
    /// SOLIDWORKS and, with <c>--sw-language en</c>, on the same one writing
    /// English names.
    ///
    /// <c>--sw-language en</c> switches the toggle «Use English language feature
    /// and file names» (<c>swUseEnglishLanguageFeatureNames</c>, page SO_General.htm of
    /// the API guide) ON for the run and puts it back as it was when the run ends
    /// (normally, on Ctrl+C, or at the start of the next run if this one was
    /// killed: the value to restore is kept in a file of the temp folder until it
    /// is restored). It does NOT touch «Use English language menus»: that one
    /// needs a restart of SOLIDWORKS to take effect, and the bench does not
    /// restart a SOLIDWORKS other people use. So the texts of the INTERFACE keep
    /// the language of the installation; what changes is the language of the
    /// names of features, sheets and new documents.
    /// </summary>
    internal static class CaseNames
    {
        private static ISldWorks _sw;
        private static bool _changed;             // the run changed the toggle
        private static bool _before;              // the value of the toggle before the run
        private static SolidWorksNameTable _table;

        /// <summary>The words the cases ask for, with the language of this run.</summary>
        internal static SolidWorksLanguage Language;

        private static string RestoreFile
        {
            get { return Path.Combine(Path.GetTempPath(), "oce-bench-language-restore.txt"); }
        }

        private static SolidWorksNameTable Table
        {
            get { return _table ?? (_table = SolidWorksNameTable.Embedded()); }
        }

        /// <summary>The text of a case file, with the {SW:...} placeholders replaced.</summary>
        internal static string ReadAllText(string path)
        {
            return Expand(File.ReadAllText(path));
        }

        /// <summary>The text with the {SW:...} placeholders replaced (the language of the run).</summary>
        internal static string Expand(string text)
        {
            if (text == null || text.IndexOf("{SW:", StringComparison.Ordinal) < 0) return text;
            if (Language == null)
                throw new InvalidOperationException(
                    "A case names {SW:...} before the language of SOLIDWORKS was read: the run must read it first.");
            return Table.Expand(text, domain => Language.ColumnOf(domain));
        }

        /// <summary>A single word of the table in the language of the run.</summary>
        internal static string Word(string key)
        {
            if (Language == null)
                throw new InvalidOperationException("The language of SOLIDWORKS was not read yet.");
            return Language.Name(key);
        }

        /// <summary>
        /// Starts the run in a language. «native» leaves SOLIDWORKS as it is; «en»
        /// turns the English feature names on. Returns an error text, or null.
        /// </summary>
        internal static string Begin(ISldWorks sw, string choice)
        {
            _sw = sw;
            // A previous run killed in English mode left the value to restore in a file:
            // it is restored before anything else, otherwise the person finds SOLIDWORKS
            // writing English names the next morning.
            RestoreWhatAKilledRunLeft(sw);

            choice = (choice ?? "native").Trim().ToLowerInvariant();
            if (choice != "native" && choice != "en" && choice != "it")
                return "«--sw-language» is «native», «en» or «it», not «" + choice + "».";

            if (choice != "native")
            {
                var now = SolidWorksLanguage.Read(sw);
                bool want = choice == "en";
                if (!want && !string.Equals(now.Language, "italian", StringComparison.OrdinalIgnoreCase))
                    return "«--sw-language it» wants the names in Italian, and this SOLIDWORKS runs in «" + now.Language + "».";
                bool before = sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames);
                if (before != want)
                {
                    // The value to put back is written BEFORE the change.
                    File.WriteAllText(RestoreFile, before ? "1" : "0");
                    _before = before;
                    _changed = true;
                    sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames, want);
                    bool read = sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames);
                    if (read != want)
                    {
                        RestoreLanguage();
                        return "SOLIDWORKS did not take the toggle «Use English feature and file names» (set " + want + ", read back " + read + ").";
                    }
                }
                Console.CancelKeyPress += (s, e) => RestoreLanguage();
                AppDomain.CurrentDomain.ProcessExit += (s, e) => RestoreLanguage();
            }
            Language = SolidWorksLanguage.Read(sw);
            return null;
        }

        /// <summary>Puts the toggle back as it was before the run. Safe to call more than once.</summary>
        internal static void RestoreLanguage()
        {
            try
            {
                if (_changed && _sw != null)
                {
                    _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames, _before);
                    bool read = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames);
                    Console.WriteLine("solidworks language: toggle «Use English feature and file names» put back to " + _before +
                                      (read == _before ? "" : " (READ BACK " + read + ": check it by hand)"));
                    // An empty file says «nothing to put back».
                    if (read == _before) File.WriteAllText(RestoreFile, "");
                    _changed = false;
                }
            }
            catch (Exception ex) { Console.WriteLine("solidworks language: could not put the toggle back: " + ex.Message); }
        }

        private static void RestoreWhatAKilledRunLeft(ISldWorks sw)
        {
            try
            {
                if (!File.Exists(RestoreFile)) return;
                string left = File.ReadAllText(RestoreFile).Trim();
                if (left.Length == 0) return;
                bool value = left == "1";
                sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames, value);
                File.WriteAllText(RestoreFile, "");
                Console.WriteLine("solidworks language: a previous run was killed in the middle: the toggle «Use English feature and file names» was put back to " + value);
            }
            catch { }
        }
    }
}
