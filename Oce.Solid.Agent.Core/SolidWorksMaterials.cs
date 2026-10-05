using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// I materiali installati su questa postazione, letti dagli archivi veri.
    ///
    /// PERCHE' NON BASTA SCRIVERE IL NOME
    /// `IPartDoc.SetMaterialPropertyName2(config, archivio, nome)` restituisce
    /// **void**. Se l'archivio o il nome non esistono non solleva niente e non
    /// cambia niente: si rilegge il materiale di prima. Misurato il 9 settembre
    /// 2026 — chiedendo «AISI 304» nell'archivio «materiali personalizzati»
    /// (che contiene una sola plastica) la rilettura tornava vuota, senza un
    /// solo segnale. Un'operazione costruita su quella firma senza rilettura e'
    /// un fallimento silenzioso per costruzione.
    ///
    /// E I NOMI NON SI COMPONGONO
    /// Il nome vero di un S235JR e' `1.0037 (S235JR)`, sigla numerica e
    /// parentesi comprese, e nell'archivio DIN convivono 1.0036 (S235JRG1),
    /// 1.0038 (S235JRG2), 1.0114 (S235J0), 1.0116 (S235J2G3), 1.0117
    /// (S235J2G4): non sono lo stesso acciaio. I nomi che contengono parole
    /// sono per di piu' TRADOTTI («Acciaio legato» in italiano, «Alloy Steel»
    /// in inglese), quindi dipendono dalla lingua dell'installazione.
    ///
    /// COSA SI FA QUI
    /// Si leggono gli archivi che SolidWorks dichiara
    /// (`ISldWorks.GetMaterialDatabases()`, quattro su questa macchina: quello
    /// personalizzato, SOLIDWORKS Materials con 260 voci, SOLIDWORKS DIN
    /// Materials con 207, e gli extra di sostenibilita') e si risolve il nome
    /// che arriva dal piano CONTRO QUELLO CHE C'E'. Se corrisponde a una voce
    /// sola si applica; se a nessuna o a piu' d'una si fallisce elencando le
    /// candidate, perche' scegliere fra 1.0037 e 1.0116 e' una decisione di
    /// progetto, non un ripiego di un assistente.
    ///
    /// Questo evita di riversare centinaia di nomi nel messaggio di sistema:
    /// il modello scrive «S235JR» come lo direbbe un progettista, e la grafia
    /// esatta la trova la postazione che deve eseguire.
    /// </summary>
    public static class SolidWorksMaterials
    {
        public class Material
        {
            /// <summary>Il nome dell'archivio come lo vuole SetMaterialPropertyName2.</summary>
            public string Archive;
            /// <summary>La classe dentro l'archivio: «Acciaio», «Leghe di alluminio»...</summary>
            public string Class = "";
            /// <summary>Il nome ESATTO, da copiare senza toccarlo.</summary>
            public string Name;
        }

        private static readonly object _lock = new object();
        private static List<Material> _list;

        /// <summary>Tutti i materiali degli archivi dichiarati da SolidWorks.</summary>
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
                    // L'argomento «Database» di SetMaterialPropertyName2 e' il
                    // nome del file SENZA estensione: verificato il 9 settembre
                    // 2026, funziona anche con il percorso intero e con le
                    // maiuscole diverse, ma il nome del file e' la forma che
                    // SolidWorks poi rilegge.
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
        /// Trova il materiale che corrisponde a cio' che ha scritto il modello.
        ///
        /// L'ordine di ricerca va dal certo all'incerto, e si ferma appena una
        /// tappa da' UNA sola risposta:
        ///   1. il nome esatto;
        ///   2. il nome esatto ignorando maiuscole e spazi;
        ///   3. il nome dentro le parentesi — «1.0037 (S235JR)» chiesto come
        ///      «S235JR», che e' come lo chiama un progettista;
        ///   4. il nome contenuto da qualche parte.
        ///
        /// Se una tappa ne trova piu' d'una ci si ferma li' e si elencano: fra
        /// due acciai diversi non si tira a indovinare.
        /// </summary>
        /// <returns>null se non c'e' una risposta sola; «perche'» dice cosa dire all'utente.</returns>
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
        /// Somiglianza grossolana per il messaggio d'errore: basta che
        /// condividano tre caratteri di seguito. Serve a suggerire, non a
        /// decidere — decidere su una somiglianza e' esattamente cio' che qui
        /// non si fa.
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
