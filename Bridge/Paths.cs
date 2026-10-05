using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// DOVE SI PUO' SCRIVERE.
    ///
    /// Alcune operazioni prendono un percorso e ci scrivono sopra: salvare il
    /// pezzo, salvare la tavola, esportare il PDF. Quel percorso arriva dal
    /// MODELLO, cioe' da un testo generato: non e' un ingresso fidato, ed e'
    /// l'unico punto del ponte che tocca il disco fuori dal documento aperto.
    ///
    /// Senza un controllo, «salva in ..\..\..\Windows\System32\qualcosa» e'
    /// una richiesta come un'altra. Non serve malizia perche' faccia danno:
    /// basta un percorso plausibile e sbagliato.
    ///
    /// TRE REGOLE, IN ORDINE DI SEVERITA'.
    ///
    /// 1. UNA RADICE, SE C'E'. Con `RISCONTRO_RADICE` impostata, si scrive solo
    ///    sotto quella cartella e basta. E' la configurazione da usare quando
    ///    il ponte gira senza qualcuno che guarda.
    ///
    /// 2. CARTELLE VIETATE, SEMPRE. Le cartelle di sistema e l'installazione di
    ///    SOLIDWORKS non si toccano mai, nemmeno senza radice impostata. Non
    ///    perche' qualcuno lo chiederebbe apposta, ma perche' un percorso
    ///    relativo sbagliato ci finisce da solo.
    ///
    /// 3. LE LIBRERIE DELL'AZIENDA, se dichiarate in `RISCONTRO_SOLA_LETTURA`.
    ///    E' una regola nata da un fatto: inserendo un membro strutturale,
    ///    SOLIDWORKS apre da solo il file di libreria dei profili e lo lascia
    ///    marcato come modificato. Da li' un salvataggio qualunque riscrive la
    ///    libreria di tutti. Dichiarare quelle cartelle di sola lettura e'
    ///    piu' economico che accorgersene dopo.
    ///
    /// Il messaggio dice sempre QUALE regola ha fermato la scrittura e cosa
    /// fare: un rifiuto che non si sa aggirare in modo legittimo e' un difetto
    /// quanto un permesso di troppo.
    /// </summary>
    internal static class Paths
    {
        /// <summary>I nomi di argomento che contengono un percorso su cui si scrive.</summary>
        private static readonly string[] WritingFields =
        {
            "path", "file", "percorso_pdf", "percorso_file", "destinazione",
        };

        /// <summary>
        /// Controlla tutti i percorsi dentro gli argomenti di una operazione.
        /// Ritorna null se va bene, altrimenti il motivo del rifiuto.
        /// </summary>
        public static string Check(Dictionary<string, object> arguments)
        {
            if (arguments == null) return null;
            foreach (var theField in WritingFields)
            {
                object v;
                if (!arguments.TryGetValue(theField, out v) || v == null) continue;
                var filePath = Convert.ToString(v);
                if (string.IsNullOrWhiteSpace(filePath)) continue;

                var reason = CheckOne(filePath, theField);
                if (reason != null) return reason;
            }
            return null;
        }

        public static string CheckOne(string filePath, string theField = "path")
        {
            string full;
            try
            {
                // Path.GetFullPath risolve «..» e i separatori misti: e' il
                // passo che trasforma un percorso in cio' che toccherebbe
                // davvero. Va fatto PRIMA di qualunque confronto, se no si
                // confronta una stringa e si scrive altrove.
                full = Path.GetFullPath(filePath);
            }
            catch (Exception ex)
            {
                return "Il percorso «" + filePath + "» non e' un percorso valido (" +
                       ex.Message + ").";
            }

            // Niente flussi, niente dispositivi, niente percorsi di rete non
            // dichiarati: sono tutte cose che un percorso di salvataggio non
            // ha mai bisogno di essere.
            if (full.IndexOf(':') != full.LastIndexOf(':'))
                return "Il percorso «" + filePath + "» contiene due volte i due punti: " +
                       "non lo tocco.";

            var root = Variable("RISCONTRO_RADICE");
            if (!string.IsNullOrWhiteSpace(root))
            {
                if (!Below(full, root))
                    return "Posso scrivere solo dentro «" + root + "» (lo dice la " +
                           "variabile RISCONTRO_RADICE di questa installazione), e «" +
                           full + "» e' fuori. Scegli un percorso li' dentro.";
            }

            foreach (var forbidden in Forbidden())
            {
                if (Below(full, forbidden))
                    return "Non scrivo dentro «" + forbidden + "»: e' una cartella di " +
                           "sistema o di programma. «" + full + "» finisce li'. " +
                           "Scegli una cartella di lavoro, per esempio dentro Documenti.";
            }

            foreach (var single in ReadOnly())
            {
                if (Below(full, single))
                    return "«" + single + "» e' dichiarata di sola lettura su questa " +
                           "installazione (variabile RISCONTRO_SOLA_LETTURA): di " +
                           "solito sono le librerie condivise, e riscriverle tocca il " +
                           "lavoro di tutti. «" + full + "» finisce li'.";
            }

            return null;
        }

        // ─────────────────────────────────────────────────────────────────

        private static string Variable(string itemName)
        {
            try { return Environment.GetEnvironmentVariable(itemName); }
            catch { return null; }
        }

        private static IEnumerable<string> Forbidden()
        {
            var outside = new List<string>();
            foreach (var folder in new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData,
            })
            {
                try
                {
                    var p = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrWhiteSpace(p)) outside.Add(p);
                }
                catch { }
            }
            return outside;
        }

        /// <summary>
        /// Cartelle dichiarate di sola lettura, separate da punto e virgola.
        /// Esempio: `RISCONTRO_SOLA_LETTURA=S:\CAD;S:\_LIBRERIE`
        /// </summary>
        private static IEnumerable<string> ReadOnly()
        {
            var v = Variable("RISCONTRO_SOLA_LETTURA");
            if (string.IsNullOrWhiteSpace(v)) return Enumerable.Empty<string>();
            return v.Split(';')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        /// <summary>
        /// `pieno` sta dentro `folder`?
        ///
        /// Il separatore in coda non e' pignoleria: senza, «C:\Dati2» risulta
        /// dentro «C:\Dati», e un controllo che dice di si' quando e' no e' un
        /// controllo che non c'e'.
        /// </summary>
        private static bool Below(string full, string folder)
        {
            try
            {
                var root = Path.GetFullPath(folder)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var p = Path.GetFullPath(full);
                return p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(
                           p.TrimEnd(Path.DirectorySeparatorChar),
                           root.TrimEnd(Path.DirectorySeparatorChar),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
