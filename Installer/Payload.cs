using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SolidWorksMcp.Installer
{
    /// <summary>
    /// IL PONTE DENTRO L'INSTALLATORE.
    ///
    /// L'obiettivo e' **un file solo**: chi installa scarica `Installa.exe`,
    /// doppio clic, finito. Niente zip da scompattare, niente «metti questo
    /// accanto a quello», niente cartella che si perde a meta' strada.
    ///
    /// Quindi `SolidWorksMcp.exe` e `catalogo.json` viaggiano come RISORSE
    /// INCORPORATE dentro questo eseguibile e vengono scritte su disco al
    /// momento dell'installazione.
    ///
    /// DUE MODI, E IL SECONDO SERVE A CHI SVILUPPA.
    /// Se le risorse non ci sono — perche' si sta eseguendo l'installatore
    /// appena compilato dentro `bin\`, senza essere passati da `build.ps1` —
    /// si prendono i file accanto all'eseguibile. Cosi' lo stesso installatore
    /// funziona sia come pacchetto da distribuire sia come strumento di chi ci
    /// lavora sopra, senza due strade da tenere allineate.
    ///
    /// L'ADD-IN NON STA QUI. `OpenPDM.SwAddin.dll` non e' incorporato ne' mai
    /// lo sara' in un pacchetto pubblico: e' codice privato, e un installatore
    /// pubblico che se lo porta dietro lo pubblica. Si cerca sulla macchina di
    /// chi installa, e se non c'e' si dice cosa manca.
    /// </summary>
    internal static class Payload
    {
        private const string Prefisso = "SolidWorksMcp.Installer.Payload.";

        private static string[] Risorse =>
            Assembly.GetExecutingAssembly().GetManifestResourceNames();

        /// <summary>C'e' almeno il ponte, incorporato?</summary>
        public static bool Incorporato =>
            Risorse.Any(r => r.EndsWith(
                Passi.NomeEseguibile, StringComparison.OrdinalIgnoreCase));

        private static string NomeRisorsa(string file)
        {
            // Si cerca per suffisso invece che per nome esatto: MSBuild
            // costruisce il nome della risorsa dal percorso, e una cartella in
            // piu' o in meno cambierebbe la stringa senza che nessuno se ne
            // accorga fino all'installazione.
            return Risorse.FirstOrDefault(r =>
                r.EndsWith("." + file, StringComparison.OrdinalIgnoreCase) ||
                r.Equals(Prefisso + file, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Il file c'e'? Guarda dove guarderebbe <see cref="EstraiSePresente"/>:
        /// prima dentro il carico, poi accanto all'installatore.
        ///
        /// Serve ai controlli, che devono poter dire «ce l'ho» **prima** di
        /// scrivere qualcosa su disco: un controllo che per rispondere deve
        /// installare non e' un controllo.
        /// </summary>
        public static bool Contiene(string file)
        {
            if (NomeRisorsa(file) != null) return true;
            try { return File.Exists(Path.Combine(Passi.CartellaDiQuestoExe, file)); }
            catch { return false; }
        }

        /// <summary>Scrive il file, o solleva dicendo perche' non ci riesce.</summary>
        public static void Estrai(string file, string destinazione, Action<string> racconta)
        {
            if (!EstraiSePresente(file, destinazione, racconta))
                throw new Exception(
                    "Non trovo «" + file + "»: non e' dentro l'installatore ne' " +
                    "accanto a lui. L'installatore e' incompleto.");
        }

        /// <summary>Scrive il file se c'e'. Ritorna false se non c'era.</summary>
        public static bool EstraiSePresente(
            string file, string destinazione, Action<string> racconta)
        {
            // 1. incorporato
            var nome = NomeRisorsa(file);
            if (nome != null)
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(nome))
                {
                    if (s != null)
                    {
                        ScriviConCura(s, destinazione);
                        racconta("scritto " + file + " (" + Byte(destinazione) + ")");
                        return true;
                    }
                }
            }

            // 2. accanto all'installatore
            var accanto = Path.Combine(Passi.CartellaDiQuestoExe, file);
            if (File.Exists(accanto))
            {
                if (Passi.PercorsiUguali(accanto, destinazione))
                {
                    racconta(file + " e' gia' al suo posto");
                    return true;
                }
                using (var s = File.OpenRead(accanto)) ScriviConCura(s, destinazione);
                racconta("copiato " + file + " (" + Byte(destinazione) + ")");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Scrive prima un file temporaneo e poi rinomina.
        ///
        /// Se l'installazione muore a meta' — disco pieno, antivirus, corrente —
        /// quello che resta sul disco e' la versione vecchia intera, non una
        /// nuova troncata. Un eseguibile troncato non da' un errore leggibile:
        /// da' «impossibile caricare il file o l'assembly», che manda a cercare
        /// una dipendenza mancante che non manca.
        /// </summary>
        private static void ScriviConCura(Stream sorgente, string destinazione)
        {
            var cartella = Path.GetDirectoryName(destinazione);
            if (!string.IsNullOrEmpty(cartella)) Directory.CreateDirectory(cartella);

            var temporaneo = destinazione + ".parziale";
            try
            {
                using (var f = File.Create(temporaneo)) sorgente.CopyTo(f);
                if (File.Exists(destinazione)) File.Delete(destinazione);
                File.Move(temporaneo, destinazione);
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception(
                    "Non ho il permesso di scrivere in «" + cartella + "». " +
                    "Scegli un'altra cartella, per esempio dentro Documenti.");
            }
            catch (IOException ex) when (File.Exists(destinazione))
            {
                throw new Exception(
                    "«" + Path.GetFileName(destinazione) + "» e' in uso: probabilmente " +
                    "il ponte sta girando dentro un client MCP aperto. Chiudi il " +
                    "client e riprova. (" + ex.Message + ")");
            }
            finally
            {
                try { if (File.Exists(temporaneo)) File.Delete(temporaneo); }
                catch { }
            }
        }

        private static string Byte(string file)
        {
            try
            {
                long n = new FileInfo(file).Length;
                if (n < 1024) return n + " B";
                if (n < 1024 * 1024) return (n / 1024) + " kB";
                return (n / (1024 * 1024)) + " MB";
            }
            catch { return "?"; }
        }
    }
}
