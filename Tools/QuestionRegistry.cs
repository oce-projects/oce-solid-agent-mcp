using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Oce.Solid.Agent.Tools
{
    /// <summary>
    /// IL REGISTRO DELLE DOMANDE SENZA RISPOSTA (voce Z-G7)
    ///
    /// ── PERCHE' ESISTE ──────────────────────────────────────────────────
    ///
    /// Le cinque operazioni del catalogo che al 23/9/2026 non avevano nessun
    /// caso di banco — `circular_pattern`, `weld_bead`, `gusset`,
    /// `trim_members`, `end_cap` — **le abbiamo scelte noi**. (Il
    /// 25/9, scrivendo quei casi, due si sono scoperte mai funzionanti e una
    /// e' uscita dal catalogo: scelte a tavolino, e nemmeno misurate.) Quali
    /// servano davvero non lo sa nessuno, e chiederlo alle persone non lo fa
    /// sapere: chi usa il ponte non ha l'elenco di cosa non c'e'.
    ///
    /// In OpenPDM il registro delle domande e' cio' che ha fatto trovare il
    /// difetto «libreria cartella / libreria stato»: non si e' scoperto
    /// chiedendo, si e' letto li'. Questo ponte aveva lo stesso dato in mano e
    /// lo buttava via — il caso `default` di `McpTools.Invoke` **sa** che gli
    /// e' stato chiesto uno strumento che non esiste, risponde bene, e non lo
    /// scrive da nessuna parte.
    ///
    /// **Tre modelli diversi che inventano lo stesso nome sono la prossima
    /// operazione da costruire, e non e' opinione.**
    ///
    /// ── COSA NON SCRIVE, E NON E' UNA DIMENTICANZA ──────────────────────
    ///
    /// Solo il nome chiesto, di che tipo era la domanda, e i nomi vicini che
    /// il catalogo aveva proposto. **Niente argomenti, niente percorsi,
    /// niente nomi di documento.**
    ///
    /// Un registro che copia gli argomenti di una chiamata diventa in un
    /// pomeriggio un file pieno di percorsi di rete e di nomi di commessa, e
    /// questo repository ha gia' un problema di dati personali che ne
    /// impedisce la pubblicazione. Un nome inventato da un modello non e' il
    /// dato di nessuno; il percorso in cui sta il pezzo lo e'.
    ///
    /// I `similar` si scrivono perche' sono **la meta' utile della misura**:
    /// distinguono «il modello ha sbagliato a scrivere un nome che c'e'» da
    /// «il modello chiede una cosa che non esiste». Il primo caso si aggiusta
    /// con un sinonimo, il secondo e' lavoro da fare.
    ///
    /// ── E NON PUO' FAR FALLIRE LA CHIAMATA CHE OSSERVA ──────────────────
    ///
    /// Due regole, tutte e due assolute:
    ///
    ///  1. **Non scrive mai su stdout.** Li' passa il JSON-RPC: una riga di
    ///     troppo non e' un log sporco, e' il protocollo rotto. Questa classe
    ///     non nomina `Console` da nessuna parte, e c'e' un controllo di
    ///     contratto che lo pretende leggendo il sorgente.
    ///  2. **Non solleva mai.** Installato in `Program Files` la cartella non
    ///     e' scrivibile; su una macchina con due ponti aperti il file puo'
    ///     essere occupato; il disco puo' essere pieno. In tutti questi casi
    ///     si tace e si va avanti. *Un registro che fa fallire la chiamata che
    ///     doveva osservare e' peggio di nessun registro.*
    ///
    /// Il motivo dell'ultimo silenzio resta in `WhyItWasSilent`, che nessuno
    /// stampa: serve a una prova, per non dover credere sulla parola che il
    /// silenzio fosse voluto.
    ///
    /// ── LA FORMA: UNA RIGA PER DOMANDA, NON UN CONTEGGIO ────────────────
    ///
    /// JSONL in aggiunta, non un JSON riscritto. Riscrivere vorrebbe dire
    /// leggere-modificare-scrivere, e due ponti aperti sulla stessa macchina
    /// — che qui succede — si mangerebbero le righe a vicenda senza che
    /// nessuno se ne accorga. In aggiunta, il peggio che capita e' una riga
    /// persa.
    ///
    /// Contare e' un problema di chi legge: `Checks/questions-without-answer.js`.
    /// </summary>
    internal sealed class QuestionRegistry
    {
        public const string FileName = "domande-senza-risposta.jsonl";

        /// <summary>Il file su cui si scrive, o null se non si e' trovato dove.</summary>
        public string FilePath { get; }

        /// <summary>
        /// Perche' l'ultima scrittura non e' avvenuta, o null se e' andata.
        /// Non lo stampa nessuno: esiste perche' una prova possa verificare
        /// che il silenzio era voluto e non un ramo morto.
        /// </summary>
        public string WhyItWasSilent { get; private set; }

        /// <summary>Quante righe questa istanza ha scritto davvero.</summary>
        public int Written { get; private set; }

        public QuestionRegistry() : this(DefaultPath()) { }

        public QuestionRegistry(string filePath)
        {
            FilePath = filePath;
        }

        /// <summary>
        /// DUE NOMI DI VARIABILE E UN RIPIEGO, come per il catalogo.
        ///
        /// `RISCONTRO_REGISTRO` dice il file per intero. Senza, si scrive
        /// accanto all'eseguibile — che su una macchina di sviluppo e'
        /// scrivibile e su una installata quasi mai, e va bene cosi': la
        /// regola 2 dice che allora si tace.
        ///
        /// Impostarla a stringa vuota **non** e' come non impostarla: e' il
        /// modo di dire «non registrare niente», e va rispettato senza
        /// ripieghi, altrimenti chi la svuota per zittire il registro si
        /// ritrova a scrivere accanto all'eseguibile.
        /// </summary>
        public static string DefaultPath()
        {
            var fromEnv = Environment.GetEnvironmentVariable("RISCONTRO_REGISTRO");
            if (fromEnv != null) return fromEnv.Length == 0 ? null : fromEnv;

            try
            {
                var folder = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(folder)) return null;
                return Path.Combine(folder, FileName);
            }
            catch { return null; }
        }

        public void ToolThatDoesNotExist(string itemName)
        {
            Annotate("strumento", itemName, null);
        }

        public void OperationThatDoesNotExist(string itemName, IEnumerable<string> similar)
        {
            Annotate("operation", itemName, similar);
        }

        /// <summary>
        /// LA RIGA, SEPARATA DA CHI LA SCRIVE.
        ///
        /// Prende l'orologio invece di leggerlo, cosi' una prova puo' fissare
        /// la data e confrontare la riga intera. E serializza con lo stesso
        /// `Protocol.InJson` del resto del ponte, invece di incollare
        /// virgolette a mano: un modello puo' benissimo inventarsi
        /// un'operazione che si chiama `taglia"a"meta`, e una riga JSONL rotta
        /// rende illeggibile il file da li' in poi.
        /// </summary>
        public static string Line(
            DateTime timestamp, string typ, string itemName, IEnumerable<string> similar)
        {
            var entry = new Dictionary<string, object>
            {
                ["timestamp"] = timestamp.ToString("s"),
                ["kind"] = typ,
                ["name"] = itemName ?? "",
            };
            if (similar != null)
            {
                var neighbors = similar.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                if (neighbors.Length > 0) entry["similar"] = neighbors;
            }
            return Mcp.Protocol.InJson(entry);
        }

        private void Annotate(string typ, string itemName, IEnumerable<string> similar)
        {
            WhyItWasSilent = null;

            if (FilePath == null)
            {
                WhyItWasSilent = "non c'e' nessun file su cui scrivere";
                return;
            }
            if (string.IsNullOrWhiteSpace(itemName))
            {
                // Un nome vuoto non e' una domanda: `McpTools` risponde gia'
                // «manca «operation»», ed e' un guaio di forma del piano, non
                // una cosa che qualcuno voleva e non c'era. Registrarlo
                // riempirebbe il file di righe che non insegnano niente.
                WhyItWasSilent = "nome vuoto: non e' una domanda";
                return;
            }

            try
            {
                // `Append` apre e chiude subito: il file resta occupato per il
                // tempo di una riga, e due ponti aperti non si bloccano a
                // vicenda per tutta la sessione.
                File.AppendAllText(
                    FilePath,
                    Line(DateTime.Now, typ, itemName, similar) + Environment.NewLine,
                    new UTF8Encoding(false));
                Written++;
            }
            catch (Exception ex)
            {
                // REGOLA 2. Qui si tace, e il motivo resta in mano a chi prova.
                WhyItWasSilent = ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
