using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHICH LANGUAGE SOLIDWORKS IS WRITING IN, AND THE COLUMN OF THE TABLE THAT
    /// GOES WITH IT.
    ///
    /// The three things SOLIDWORKS reports (pages of the API guide, extracted
    /// 7/10/2026):
    ///
    ///  * <c>ISldWorks.GetCurrentLanguage</c> (ISldWorks~GetCurrentLanguage.html):
    ///    «Gets the current language used by SOLIDWORKS»; the possible values are
    ///    chinese, chinese-simplified, czech, english, french, german, italian,
    ///    japanese, korean, polish, portuguese-brazilian (the list of the page);
    ///  * <c>swUserPreferenceToggle_e.swUseEnglishLanguage</c>, «Use English
    ///    language menus» (System Options, General page, SO_General.htm): «SOLIDWORKS
    ///    software must be restarted for this change to take effect»; it is read
    ///    here only to say that a restart is pending;
    ///  * <c>swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames</c>, «Use
    ///    English language feature and file names» (same page): «Specifies whether
    ///    to use English feature and file names in languages other than English».
    ///    It decides the column of the names of FEATURES, documents and sheets
    ///    (the «feature» domain of the table), not that of the interface texts.
    ///
    /// Read afresh at every call, never cached: the toggle of the feature names
    /// can change while SOLIDWORKS runs (the bench switches it to measure the same
    /// cases in English), and two reads cost nothing.
    /// </summary>
    public sealed class SolidWorksLanguage
    {
        /// <summary>What <c>GetCurrentLanguage</c> answered («italian»), or null if it did not.</summary>
        public string Language;

        /// <summary>The column for the texts of the interface.</summary>
        public string InterfaceColumn;

        /// <summary>The column for the names of features, documents and sheets.</summary>
        public string FeatureColumn;

        /// <summary>The toggle «Use English feature and file names»; null if it could not be read.</summary>
        public bool? EnglishFeatureNames;

        /// <summary>The toggle «Use English language menus»; null if it could not be read.</summary>
        public bool? EnglishInterface;

        /// <summary>What the person has to know about the choice of the column, or null.</summary>
        public string Warning;

        private SolidWorksNameTable _table;

        /// <summary>The table the columns belong to.</summary>
        public SolidWorksNameTable Table { get { return _table; } }

        /// <summary>Reads the language of this SOLIDWORKS and picks the columns.</summary>
        public static SolidWorksLanguage Read(ISldWorks sw)
        {
            return Read(sw, SolidWorksNameTable.Embedded());
        }

        /// <summary>The same, with a table that is not the embedded one (the contracts use it).</summary>
        public static SolidWorksLanguage Read(ISldWorks sw, SolidWorksNameTable table)
        {
            var language = new SolidWorksLanguage { _table = table };
            try { language.Language = sw == null ? null : sw.GetCurrentLanguage(); } catch { }
            try
            {
                if (sw != null)
                    language.EnglishFeatureNames = sw.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames);
            }
            catch { }
            try
            {
                if (sw != null)
                    language.EnglishInterface = sw.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swUseEnglishLanguage);
            }
            catch { }

            string warning;
            language.InterfaceColumn = table.ColumnForLanguage(language.Language, out warning);
            language.Warning = warning;
            language.FeatureColumn = language.EnglishFeatureNames == true
                ? table.FallbackColumn
                : language.InterfaceColumn;

            // «Use English language menus» is on, but SOLIDWORKS still says another
            // language: the toggle needs a restart, and the names the document gets
            // are those of the language it says.
            if (language.EnglishInterface == true && language.Language != null &&
                !string.Equals(language.Language, "english", StringComparison.OrdinalIgnoreCase))
                language.Warning = (language.Warning == null ? "" : language.Warning + " ") +
                    "The toggle «Use English language menus» is on but SOLIDWORKS still runs in «" + language.Language +
                    "»: it takes effect after a restart, and until then the names are those of «" + language.Language + "».";
            return language;
        }

        /// <summary>The column of a domain («feature» or «ui»).</summary>
        public string ColumnOf(string domain)
        {
            return domain == SolidWorksNameTable.FeatureDomain ? FeatureColumn : InterfaceColumn;
        }

        /// <summary>The word SOLIDWORKS writes for the key in its present language.</summary>
        public string Name(string key)
        {
            return _table.Get(key, ColumnOf(_table.Domain(key)));
        }

        /// <summary>The spellings of the key, the present language first and the others after.</summary>
        public IList<string> Spellings(string key)
        {
            return _table.All(key, ColumnOf(_table.Domain(key)));
        }

        /// <summary>
        /// What goes into the state of a document, short: the language SOLIDWORKS
        /// says, the column the names come from, and the warning if there is one.
        /// </summary>
        public Dictionary<string, object> ToState()
        {
            var state = new Dictionary<string, object>
            {
                ["language"] = Language ?? "unknown",
                ["names"] = InterfaceColumn,
            };
            if (FeatureColumn != InterfaceColumn) state["feature_names"] = FeatureColumn;
            if (Warning != null) state["warning"] = Warning;
            return state;
        }
    }
}
