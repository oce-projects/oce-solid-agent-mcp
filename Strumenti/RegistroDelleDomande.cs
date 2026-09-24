using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Oce.Solid.Agent.Strumenti
{
    /// <summary>
    /// IL REGISTRO DELLE DOMANDE SENZA RISPOSTA (voce Z-G7)
    ///
    /// ── PERCHE' ESISTE ──────────────────────────────────────────────────
    ///
    /// Le cinque operazioni del catalogo senza nessun caso di banco —
    /// `ripeti_circolare`, `cordone_saldatura`, `fazzoletto`, `taglia_membri`,
    /// `tappo_estremita` — **le abbiamo scelte noi**. Quali servano davvero
    /// non lo sa nessuno, e chiederlo alle persone non lo fa sapere: chi usa
    /// il ponte non ha l'elenco di cosa non c'e'.
    ///
    /// In OpenPDM il registro delle domande e' cio' che ha fatto trovare il
    /// difetto «libreria cartella / libreria stato»: non si e' scoperto
    /// chiedendo, si e' letto li'. Questo ponte aveva lo stesso dato in mano e
    /// lo buttava via — il caso `default` di `Attrezzi.Chiama` **sa** che gli
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
    /// I `simili` si scrivono perche' sono **la meta' utile della misura**:
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
    /// Il motivo dell'ultimo silenzio resta in `PercheHaTaciuto`, che nessuno
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
    /// Contare e' un problema di chi legge: `Controlli/domande-senza-risposta.js`.
    /// </summary>
    internal sealed class RegistroDelleDomande
    {
        public const string NomeDelFile = "domande-senza-risposta.jsonl";

        /// <summary>Il file su cui si scrive, o null se non si e' trovato dove.</summary>
        public string Percorso { get; }

        /// <summary>
        /// Perche' l'ultima scrittura non e' avvenuta, o null se e' andata.
        /// Non lo stampa nessuno: esiste perche' una prova possa verificare
        /// che il silenzio era voluto e non un ramo morto.
        /// </summary>
        public string PercheHaTaciuto { get; private set; }

        /// <summary>Quante righe questa istanza ha scritto davvero.</summary>
        public int Scritte { get; private set; }

        public RegistroDelleDomande() : this(PercorsoPredefinito()) { }

        public RegistroDelleDomande(string percorso)
        {
            Percorso = percorso;
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
        public static string PercorsoPredefinito()
        {
            var dalla = Environment.GetEnvironmentVariable("RISCONTRO_REGISTRO");
            if (dalla != null) return dalla.Length == 0 ? null : dalla;

            try
            {
                var cartella = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(cartella)) return null;
                return Path.Combine(cartella, NomeDelFile);
            }
            catch { return null; }
        }

        public void StrumentoCheNonCe(string nome)
        {
            Annota("strumento", nome, null);
        }

        public void OperazioneCheNonCe(string nome, IEnumerable<string> simili)
        {
            Annota("operazione", nome, simili);
        }

        /// <summary>
        /// LA RIGA, SEPARATA DA CHI LA SCRIVE.
        ///
        /// Prende l'orologio invece di leggerlo, cosi' una prova puo' fissare
        /// la data e confrontare la riga intera. E serializza con lo stesso
        /// `Protocollo.InJson` del resto del ponte, invece di incollare
        /// virgolette a mano: un modello puo' benissimo inventarsi
        /// un'operazione che si chiama `taglia"a"meta`, e una riga JSONL rotta
        /// rende illeggibile il file da li' in poi.
        /// </summary>
        public static string Riga(
            DateTime quando, string tipo, string nome, IEnumerable<string> simili)
        {
            var voce = new Dictionary<string, object>
            {
                ["quando"] = quando.ToString("s"),
                ["tipo"] = tipo,
                ["nome"] = nome ?? "",
            };
            if (simili != null)
            {
                var vicini = simili.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                if (vicini.Length > 0) voce["simili"] = vicini;
            }
            return Mcp.Protocollo.InJson(voce);
        }

        private void Annota(string tipo, string nome, IEnumerable<string> simili)
        {
            PercheHaTaciuto = null;

            if (Percorso == null)
            {
                PercheHaTaciuto = "non c'e' nessun file su cui scrivere";
                return;
            }
            if (string.IsNullOrWhiteSpace(nome))
            {
                // Un nome vuoto non e' una domanda: `Attrezzi` risponde gia'
                // «manca «operazione»», ed e' un guaio di forma del piano, non
                // una cosa che qualcuno voleva e non c'era. Registrarlo
                // riempirebbe il file di righe che non insegnano niente.
                PercheHaTaciuto = "nome vuoto: non e' una domanda";
                return;
            }

            try
            {
                // `Append` apre e chiude subito: il file resta occupato per il
                // tempo di una riga, e due ponti aperti non si bloccano a
                // vicenda per tutta la sessione.
                File.AppendAllText(
                    Percorso,
                    Riga(DateTime.Now, tipo, nome, simili) + Environment.NewLine,
                    new UTF8Encoding(false));
                Scritte++;
            }
            catch (Exception ex)
            {
                // REGOLA 2. Qui si tace, e il motivo resta in mano a chi prova.
                PercheHaTaciuto = ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
