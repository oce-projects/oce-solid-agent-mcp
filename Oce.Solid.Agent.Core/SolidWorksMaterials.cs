using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// The materials installed on this workstation, read from the real archives.
    ///
    /// WHY WRITING THE NAME IS NOT ENOUGH
    /// `IPartDoc.SetMaterialPropertyName2(config, archivio, nome)` returns
    /// **void**. If the database or the name do not exist it raises nothing and does not
    /// change anything: the previous material is read back. Measured on 9 September
    /// 2026 — asking for «AISI 304» in the «materiali personalizzati» database
    /// (which contains a single plastic) the re-read came back empty, without a
    /// single signal. An operation built on that signature without a re-read is
    /// a silent failure by construction.
    ///
    /// AND NAMES ARE NOT COMPOSED
    /// The real name of an S235JR is `1.0037 (S235JR)`, numeric code and
    /// parentheses included, and in the DIN archive 1.0036 (S235JRG1),
    /// 1.0038 (S235JRG2), 1.0114 (S235J0), 1.0116 (S235J2G3), 1.0117
    /// (S235J2G4) coexist: they are not the same steel. Names that contain
    /// words are moreover TRANSLATED («Acciaio legato» in Italian, «Alloy Steel»
    /// in English), so they depend on the language of the installation.
    ///
    /// WHAT IS DONE HERE
    /// The databases that SolidWorks declares are read
    /// (`ISldWorks.GetMaterialDatabases()`, four on this machine: the
    /// custom one, SOLIDWORKS Materials with 260 entries, SOLIDWORKS DIN
    /// Materials with 207, and the sustainability extras) and the name
    /// that arrives from the plan is resolved AGAINST WHAT IS THERE. If it matches a single
    /// entry it is applied; if none or more than one it fails listing the
    /// candidates, because choosing between 1.0037 and 1.0116 is a design
    /// decision, not a fallback of an assistant.
    ///
    /// This avoids pouring hundreds of names into the system message:
    /// the model writes «S235JR» as a designer would say it, and the exact spelling
    /// is found by the workstation that has to execute.
    /// </summary>
    public static class SolidWorksMaterials
    {
        public class Material
        {
            /// <summary>The name of the database as SetMaterialPropertyName2 wants it.</summary>
            public string Archive;
            /// <summary>The class inside the database: «Acciaio», «Leghe di alluminio»...</summary>
            public string Class = "";
            /// <summary>The EXACT name, to be copied without touching it.</summary>
            public string Name;
        }

        private static readonly object _lock = new object();
        private static List<Material> _list;

        /// <summary>All the materials of the databases declared by SolidWorks.</summary>
        public static List<Material> All(ISldWorks sw, Action<string> log)
        {
            log = log ?? (m => { });
            lock (_lock)
            {
                if (_list != null) return _list;
                _list = new List<Material>();
                string[] archives = null;
                try { archives = sw.GetMaterialDatabases() as string[]; }
                catch (Exception ex) { log("[materiali] archivi non leggibili: " + ex.Message); }
                if (archives == null) return _list;

                foreach (var filePath in archives)
                {
                    // The «Database» argument of SetMaterialPropertyName2 is the
                    // name of the file WITHOUT extension: verified on 9 September
                    // 2026, it also works with the whole path and with
                    // different capitals, but the file name is the form that
                    // SolidWorks then re-reads.
                    string archive = Path.GetFileNameWithoutExtension(filePath);
                    try
                    {
                        var xml = XDocument.Load(filePath);
                        foreach (var fitClass in xml.Descendants()
                                     .Where(e => e.Name.LocalName == "classification"))
                        {
                            string className = (string)fitClass.Attribute("name") ?? "";
                            foreach (var m in fitClass.Descendants()
                                         .Where(e => e.Name.LocalName == "material"))
                            {
                                string itemName = (string)m.Attribute("name");
                                if (string.IsNullOrWhiteSpace(itemName)) continue;
                                _list.Add(new Material
                                {
                                    Archive = archive,
                                    Class = className,
                                    Name = itemName,
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log("[materiali] archivio illeggibile «" + filePath + "»: " + ex.Message);
                    }
                }
                log("[materiali] letti " + _list.Count + " materiali da " +
                    archives.Length + " archivi");
                return _list;
            }
        }

        /// <summary>
        /// Finds the material that corresponds to what the model wrote.
        ///
        /// The search order goes from the certain to the uncertain, and stops as soon as a
        /// stage gives ONE answer only:
        ///   1. the exact name;
        ///   2. the exact name ignoring capitals and spaces;
        ///   3. the name inside the parentheses — «1.0037 (S235JR)» asked for as
        ///      «S235JR», which is how a designer calls it;
        ///   4. the name contained somewhere.
        ///
        /// If a stage finds more than one it stops there and lists them: between
        /// two different steels one does not guess.
        /// </summary>
        /// <returns>null if there is not a single answer; «why» says what to tell the user.</returns>
        public static Material Resolve(
            ISldWorks sw, string requested, string requestedArchive,
            Action<string> log, out string why)
        {
            why = null;
            string searched = (requested ?? "").Trim();
            if (searched.Length == 0) { why = "Manca il nome del materiale."; return null; }

            var all = All(sw, log);
            if (all.Count == 0)
            {
                why =
                    "Su questa postazione non riesco a leggere nessun archivio di " +
                    "materiali di SolidWorks, quindi non posso applicarne uno.";
                return null;
            }

            var theField = all;
            if (!string.IsNullOrWhiteSpace(requestedArchive))
            {
                var restricted = all.Where(m => m.Archive.IndexOf(
                    requestedArchive.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (restricted.Count > 0) theField = restricted;
            }

            var stages = new Func<List<Material>>[]
            {
                () => theField.Where(m => string.Equals(m.Name, searched, StringComparison.Ordinal)).ToList(),
                () => theField.Where(m => string.Equals(
                    Compact(m.Name), Compact(searched), StringComparison.OrdinalIgnoreCase)).ToList(),
                () => theField.Where(m => string.Equals(
                    Compact(InParentheses(m.Name)), Compact(searched),
                    StringComparison.OrdinalIgnoreCase)).ToList(),
                () => theField.Where(m => Compact(m.Name).IndexOf(
                    Compact(searched), StringComparison.OrdinalIgnoreCase) >= 0).ToList(),
            };

            foreach (var stage in stages)
            {
                var foundItems = stage();
                if (foundItems.Count == 1) return foundItems[0];
                if (foundItems.Count > 1)
                {
                    why =
                        "«" + searched + "» corrisponde a " + foundItems.Count +
                        " materiali diversi, e non sono lo stesso acciaio: " +
                        string.Join(", ", foundItems.Take(8).Select(m => "«" + m.Name + "»")) +
                        (foundItems.Count > 8 ? ", e altri" : "") +
                        ". Dimmi quale.";
                    return null;
                }
            }

            var similar = theField
                .Where(m => Resemble(m.Name, searched))
                .Select(m => "«" + m.Name + "»")
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToList();
            why =
                "Negli archivi di questa postazione non c'e' nessun materiale " +
                "«" + searched + "»." +
                (similar.Count > 0
                    ? " Ci sono: " + string.Join(", ", similar) + "."
                    : " Chiedi all'utente quale materiale usare fra quelli che ha in libreria.");
            return null;
        }

        private static string Compact(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
        }

        private static string InParentheses(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int a = s.IndexOf('(');
            int b = s.LastIndexOf(')');
            return (a >= 0 && b > a) ? s.Substring(a + 1, b - a - 1) : s;
        }

        /// <summary>
        /// Coarse similarity for the error message: it is enough that
        /// they share three characters in a row. It serves to suggest, not to
        /// decide — deciding on a similarity is exactly what is not
        /// done here.
        /// </summary>
        private static bool Resemble(string itemName, string searched)
        {
            string a = Compact(itemName).ToUpperInvariant();
            string b = Compact(searched).ToUpperInvariant();
            if (b.Length < 3) return false;
            for (int i = 0; i + 3 <= b.Length; i++)
                if (a.IndexOf(b.Substring(i, 3), StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
