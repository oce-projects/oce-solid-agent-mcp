using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>
    /// IL PONTE DENTRO L'INSTALLATORE.
    ///
    /// L'obiettivo e' **un file solo**: chi installa scarica `Installer.exe`,
    /// doppio clic, finito. Niente zip da scompattare, niente «metti questo
    /// accanto a quello», niente cartella che si perde a meta' strada.
    ///
    /// Quindi `oce-solid-agent-mcp.exe` e `catalog.json` viaggiano come RISORSE
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
        private const string Prefix = "Oce.Solid.Agent.Installer.Payload.";

        private static string[] Risorse =>
            Assembly.GetExecutingAssembly().GetManifestResourceNames();

        /// <summary>C'e' almeno il ponte, incorporato?</summary>
        public static bool Embedded =>
            Risorse.Any(r => r.EndsWith(
                Steps.ExecutableName, StringComparison.OrdinalIgnoreCase));

        private static string ResourceName(string file)
        {
            // Si cerca per suffisso invece che per nome esatto: MSBuild
            // costruisce il nome della risorsa dal percorso, e una cartella in
            // piu' o in meno cambierebbe la stringa senza che nessuno se ne
            // accorga fino all'installazione.
            return Risorse.FirstOrDefault(r =>
                r.EndsWith("." + file, StringComparison.OrdinalIgnoreCase) ||
                r.Equals(Prefix + file, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Il file c'e'? Guarda dove guarderebbe <see cref="ExtractIfPresent"/>:
        /// prima dentro il carico, poi accanto all'installatore.
        ///
        /// Serve ai controlli, che devono poter dire «ce l'ho» **prima** di
        /// scrivere qualcosa su disco: un controllo che per rispondere deve
        /// installare non e' un controllo.
        /// </summary>
        public static bool Contains(string file)
        {
            if (ResourceName(file) != null) return true;
            try { return File.Exists(Path.Combine(Steps.ThisExeFolder, file)); }
            catch { return false; }
        }

        /// <summary>Scrive il file, o solleva dicendo perche' non ci riesce.</summary>
        public static void Extract(string file, string destination, Action<string> tell)
        {
            if (!ExtractIfPresent(file, destination, tell))
                throw new Exception(
                    "Non trovo «" + file + "»: non e' dentro l'installatore ne' " +
                    "accanto a lui. L'installatore e' incompleto.");
        }

        /// <summary>Scrive il file se c'e'. Ritorna false se non c'era.</summary>
        public static bool ExtractIfPresent(
            string file, string destination, Action<string> tell)
        {
            // 1. incorporato
            var itemName = ResourceName(file);
            if (itemName != null)
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(itemName))
                {
                    if (s != null)
                    {
                        WriteWithCare(s, destination);
                        tell("scritto " + file + " (" + Byte(destination) + ")");
                        return true;
                    }
                }
            }

            // 2. accanto all'installatore
            var nextTo = Path.Combine(Steps.ThisExeFolder, file);
            if (File.Exists(nextTo))
            {
                if (Steps.EqualPaths(nextTo, destination))
                {
                    tell(file + " e' gia' al suo posto");
                    return true;
                }
                using (var s = File.OpenRead(nextTo)) WriteWithCare(s, destination);
                tell("copiato " + file + " (" + Byte(destination) + ")");
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
        private static void WriteWithCare(Stream source, string destination)
        {
            var folder = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var temporary = destination + ".parziale";
            try
            {
                using (var f = File.Create(temporary)) source.CopyTo(f);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination);
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception(
                    "Non ho il permesso di scrivere in «" + folder + "». " +
                    "Scegli un'altra cartella, per esempio dentro Documenti.");
            }
            catch (IOException ex) when (File.Exists(destination))
            {
                throw new Exception(
                    "«" + Path.GetFileName(destination) + "» e' in uso: probabilmente " +
                    "il ponte sta girando dentro un client MCP aperto. Chiudi il " +
                    "client e riprova. (" + ex.Message + ")");
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
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
