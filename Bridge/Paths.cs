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
    internal static class Percorsi
    {
        /// <summary>I nomi di argomento che contengono un percorso su cui si scrive.</summary>
        private static readonly string[] CampiDiScrittura =
        {
            "path", "file", "percorso_pdf", "percorso_file", "destinazione",
        };

        /// <summary>
        /// Controlla tutti i percorsi dentro gli argomenti di una operazione.
        /// Ritorna null se va bene, altrimenti il motivo del rifiuto.
        /// </summary>
        public static string Controlla(Dictionary<string, object> argomenti)
        {
            if (argomenti == null) return null;
            foreach (var campo in CampiDiScrittura)
            {
                object v;
                if (!argomenti.TryGetValue(campo, out v) || v == null) continue;
                var percorso = Convert.ToString(v);
                if (string.IsNullOrWhiteSpace(percorso)) continue;

                var motivo = ControllaUno(percorso, campo);
                if (motivo != null) return motivo;
            }
            return null;
        }

        public static string ControllaUno(string percorso, string campo = "path")
        {
            string pieno;
            try
            {
                // Path.GetFullPath risolve «..» e i separatori misti: e' il
                // passo che trasforma un percorso in cio' che toccherebbe
                // davvero. Va fatto PRIMA di qualunque confronto, se no si
                // confronta una stringa e si scrive altrove.
                pieno = Path.GetFullPath(percorso);
            }
            catch (Exception ex)
            {
                return "Il percorso «" + percorso + "» non e' un percorso valido (" +
                       ex.Message + ").";
            }

            // Niente flussi, niente dispositivi, niente percorsi di rete non
            // dichiarati: sono tutte cose che un percorso di salvataggio non
            // ha mai bisogno di essere.
            if (pieno.IndexOf(':') != pieno.LastIndexOf(':'))
                return "Il percorso «" + percorso + "» contiene due volte i due punti: " +
                       "non lo tocco.";

            var radice = Variabile("RISCONTRO_RADICE");
            if (!string.IsNullOrWhiteSpace(radice))
            {
                if (!Sotto(pieno, radice))
                    return "Posso scrivere solo dentro «" + radice + "» (lo dice la " +
                           "variabile RISCONTRO_RADICE di questa installazione), e «" +
                           pieno + "» e' fuori. Scegli un percorso li' dentro.";
            }

            foreach (var vietata in Vietate())
            {
                if (Sotto(pieno, vietata))
                    return "Non scrivo dentro «" + vietata + "»: e' una cartella di " +
                           "sistema o di programma. «" + pieno + "» finisce li'. " +
                           "Scegli una cartella di lavoro, per esempio dentro Documenti.";
            }

            foreach (var sola in SolaLettura())
            {
                if (Sotto(pieno, sola))
                    return "«" + sola + "» e' dichiarata di sola lettura su questa " +
                           "installazione (variabile RISCONTRO_SOLA_LETTURA): di " +
                           "solito sono le librerie condivise, e riscriverle tocca il " +
                           "lavoro di tutti. «" + pieno + "» finisce li'.";
            }

            return null;
        }

        // ─────────────────────────────────────────────────────────────────

        private static string Variabile(string nome)
        {
            try { return Environment.GetEnvironmentVariable(nome); }
            catch { return null; }
        }

        private static IEnumerable<string> Vietate()
        {
            var fuori = new List<string>();
            foreach (var cartella in new[]
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
                    var p = Environment.GetFolderPath(cartella);
                    if (!string.IsNullOrWhiteSpace(p)) fuori.Add(p);
                }
                catch { }
            }
            return fuori;
        }

        /// <summary>
        /// Cartelle dichiarate di sola lettura, separate da punto e virgola.
        /// Esempio: `RISCONTRO_SOLA_LETTURA=S:\CAD;S:\_LIBRERIE`
        /// </summary>
        private static IEnumerable<string> SolaLettura()
        {
            var v = Variabile("RISCONTRO_SOLA_LETTURA");
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
        private static bool Sotto(string pieno, string cartella)
        {
            try
            {
                var radice = Path.GetFullPath(cartella)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var p = Path.GetFullPath(pieno);
                return p.StartsWith(radice, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(
                           p.TrimEnd(Path.DirectorySeparatorChar),
                           radice.TrimEnd(Path.DirectorySeparatorChar),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
