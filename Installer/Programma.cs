using System;
using System.Linq;
using System.Windows.Forms;

namespace SolidWorksMcp.Installer
{
    internal static class Programma
    {
        /// <summary>
        /// STAThread non e' una formalita': senza, le finestre di dialogo del
        /// sistema — «Sfoglia», e i controlli che usano il COM — si comportano
        /// in modo imprevedibile o si piantano. E' l'errore che si scopre solo
        /// quando qualcuno clicca il bottone sbagliato su una macchina
        /// diversa dalla tua.
        /// </summary>
        [STAThread]
        private static int Main(string[] argomenti)
        {
            argomenti = argomenti ?? new string[0];

            if (argomenti.Any(a => Uguale(a, "--aiuto") || Uguale(a, "-h") || Uguale(a, "/?")))
            {
                Mostra(Aiuto());
                return 0;
            }

            // MODO SILENZIOSO: per chi installa su piu' postazioni, o da uno
            // script di distribuzione. Fa le stesse cose della finestra e
            // scrive su console invece che in un riquadro.
            if (argomenti.Any(a => Uguale(a, "--silenzioso") || Uguale(a, "-s")))
                return Silenzioso(argomenti);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new Finestra());
                return 0;
            }
            catch (Exception ex)
            {
                // Un installatore che sparisce senza dire niente e' il modo
                // piu' rapido di far concludere che il programma e' rotto.
                MessageBox.Show(
                    "L'installatore si e' fermato per un errore che non ha saputo " +
                    "gestire:\n\n" + ex.Message + "\n\n" + ex.GetType().Name,
                    Passi.NomeProdotto, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static bool Uguale(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Valore(string[] argomenti, string nome)
        {
            for (int i = 0; i < argomenti.Length - 1; i++)
                if (Uguale(argomenti[i], nome)) return argomenti[i + 1];
            return null;
        }

        // ─────────────────────────────────────────────────────────────────

        private static int Silenzioso(string[] argomenti)
        {
            var cartella = Valore(argomenti, "--cartella") ?? Passi.CartellaPredefinita;
            var addin = Valore(argomenti, "--addin") ?? Passi.TrovaAddin();
            var redist = Valore(argomenti, "--redist") ?? Passi.TrovaRedist();
            bool niente = argomenti.Any(a => Uguale(a, "--senza-client"));

            Action<string> racconta = riga => Console.WriteLine("  " + riga);

            // SOLO ASCII SU CONSOLE. Ci si attacca alla console di chi ha
            // lanciato il comando, con la sua tabella codici: un trattino lungo
            // o un accento escono come un rombo con un punto interrogativo, e
            // un installatore che stampa caratteri rotti sembra rotto lui.
            Console.WriteLine(Passi.NomeProdotto + " - installazione silenziosa");

            var controlli = Passi.Controlla(addin, redist);
            foreach (var c in controlli)
            {
                var segno = c.Esito == Esito.Bene ? "[ok]" : c.Esito == Esito.Avviso ? "[~] " : "[NO]";
                Console.WriteLine(segno + " " + c.Titolo + ": " + c.Dettaglio);
                if (!string.IsNullOrWhiteSpace(c.CosaFare)) Console.WriteLine("     " + c.CosaFare);
            }
            if (controlli.Any(c => c.Esito == Esito.Male))
            {
                Console.WriteLine("Mi fermo: ci sono cose da sistemare.");
                return 1;
            }

            string eseguibile;
            try { eseguibile = Passi.Installa(cartella, addin, racconta); }
            catch (Exception ex)
            {
                Console.WriteLine("Installazione fallita: " + ex.Message);
                return 1;
            }

            if (!niente)
            {
                foreach (var client in Configurazione.ClientTrovati().Where(c => c.Installato))
                {
                    try { Configurazione.Collega(client, eseguibile, redist, racconta); }
                    catch (Exception ex) { Console.WriteLine("  " + client.Nome + ": " + ex.Message); }
                }
            }

            bool inPiedi;
            var detto = Passi.Prova(eseguibile, redist, out inPiedi);
            Console.WriteLine(detto);

            // «Installato» si dice solo se il giro secco lo conferma, oppure se
            // l'unico motivo per cui non conferma e' che SOLIDWORKS e' chiuso —
            // che non e' un problema di installazione.
            if (inPiedi || detto.IndexOf("non risulta aperto", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine("Installato in " + cartella);
                return 0;
            }
            Console.WriteLine("Installato, ma il giro secco non ha confermato: leggi sopra.");
            return 2;
        }

        private static void Mostra(string testo)
        {
            // Un eseguibile WinExe non ha una console propria: ci si attacca a
            // quella di chi l'ha lanciato, se c'e'. Senza questo, `--aiuto` da
            // un prompt non stamperebbe niente.
            if (!AttachConsole(-1)) MessageBox.Show(testo, Passi.NomeProdotto);
            else Console.WriteLine(testo);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int idProcesso);

        private static string Aiuto() =>
            Passi.NomeProdotto + " - installatore\n\n" +
            "  Installa.exe                      apre la finestra\n" +
            "  Installa.exe --silenzioso         installa senza finestra\n" +
            "\n" +
            "  --cartella <percorso>   dove installare (predefinito: " +
            Passi.CartellaPredefinita + ")\n" +
            "  --addin <percorso>      dove sta OpenPDM.SwAddin.dll\n" +
            "  --redist <percorso>     la cartella api\\redist di SOLIDWORKS\n" +
            "  --senza-client          non toccare la configurazione dei client MCP\n" +
            "\n" +
            "Non serve l'amministratore: installa nella cartella dell'utente e non\n" +
            "scrive nel registro di sistema.";
    }
}
