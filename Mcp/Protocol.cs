using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// MCP su stdio: JSON-RPC 2.0, un messaggio per riga.
    ///
    /// PERCHE' SCRITTO A MANO E NON CON UNA LIBRERIA.
    /// Questo programma gira su .NET Framework 4.8, perche' deve caricare
    /// l'add-in vero di SolidWorks che e' compilato per quello. Le librerie MCP
    /// per .NET vogliono runtime moderni. La parte di protocollo che serve qui
    /// e' piccola — tre metodi e un formato di errore — e riscriverla costa
    /// meno che portarsi dietro un runtime in piu' sulla postazione di un
    /// progettista.
    ///
    /// UNA RIGA PER MESSAGGIO, E STDOUT E' SOLO DEL PROTOCOLLO.
    /// Qualunque cosa scritta su stdout che non sia un messaggio JSON rompe la
    /// conversazione senza dire perche': i messaggi di servizio vanno su
    /// stderr, sempre. E' la trappola numero uno di ogni server MCP su stdio.
    /// </summary>
    internal static class Protocollo
    {
        private static readonly JavaScriptSerializer Json = Creatore();

        private static JavaScriptSerializer Creatore()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        public static string InJson(object o) => Json.Serialize(o);

        public static Dictionary<string, object> DaJson(string testo)
        {
            return Json.DeserializeObject(testo) as Dictionary<string, object>;
        }

        /// <summary>Scrive un messaggio su stdout. Una riga, niente altro.</summary>
        public static void Manda(TextWriter uscita, object messaggio)
        {
            uscita.Write(InJson(messaggio));
            uscita.Write("\n");
            uscita.Flush();
        }

        /// <summary>
        /// DOVE VA A FINIRE QUELLO CHE IL PONTE RACCONTA (voce W2.8).
        ///
        /// `stderr` resta e non si tocca: e' l'unico posto che funziona **prima**
        /// che un client esista — l'aggancio a SOLIDWORKS, il catalogo che non
        /// si carica, `--prova` — ed e' quello che si guarda quando qualcosa non
        /// parte. Toglierlo per «fare le cose per bene nel protocollo» vorrebbe
        /// dire perdere le uniche righe che spiegano un avvio fallito.
        ///
        /// Quello che si AGGIUNGE e' `notifications/message`, che MCP prevede
        /// apposta: cosi' le stesse righe arrivano anche a chi sta dall'altra
        /// parte del tubo, che oggi non le vede **affatto**. Quando l'operazione
        /// dice «ho ritrovato la faccia F3 spostata di 2 mm», quella riga
        /// spiega perche' il pezzo e' venuto cosi', e finiva in un terminale che
        /// nessuno guarda.
        ///
        /// Il canale si accende solo quando il ciclo dei messaggi e' partito:
        /// prima di allora scrivere su stdout sarebbe scrivere a nessuno, e
        /// **una riga non-JSON su stdout rompe il client** (e' la trappola
        /// numero uno dei server MCP su stdio, gia' sorvegliata da una prova).
        /// </summary>
        private static TextWriter _canale;
        private static string _livello = "info";

        /// <summary>
        /// I livelli in ordine, come li elenca lo spec (syslog). Un messaggio
        /// esce se il suo livello e' **almeno** quello chiesto dal client.
        /// </summary>
        private static readonly string[] Livelli =
        {
            "debug", "info", "notice", "warning", "error", "critical", "alert", "emergency",
        };

        public static void AccendiIlCanale(TextWriter uscita) { _canale = uscita; }

        /// <summary>
        /// Una notifica sul canale registrato, e **non solleva mai**.
        ///
        /// Stessa ragione di `MandaComeNotifica`, scritta li' e valida
        /// qui: se scrivere la notifica fallisse e l'errore risalisse, si
        /// perderebbe la RISPOSTA a cui la notifica era accessoria. *Un
        /// racconto che fa fallire la cosa raccontata e' peggio del
        /// silenzio.*
        ///
        /// Senza canale non fa niente: e' il caso del ponte lanciato con
        /// `--prova`, e di ogni prova che costruisce il messaggio senza
        /// avere nessuno a cui mandarlo.
        /// </summary>
        internal static void NotificaSulCanale(object messaggio)
        {
            var canale = _canale;
            if (canale == null) return;
            try { Manda(canale, messaggio); }
            catch { }
        }

        /// <summary>
        /// `logging/setLevel`. Un livello che non si riconosce **non si
        /// applica**: cambiarlo a caso vorrebbe dire far sparire messaggi senza
        /// che nessuno l'abbia chiesto. Torna `true` se e' stato accettato.
        /// </summary>
        public static bool ImpostaIlLivello(string livello)
        {
            if (string.IsNullOrWhiteSpace(livello)) return false;
            var l = livello.Trim().ToLowerInvariant();
            if (Array.IndexOf(Livelli, l) < 0) return false;
            _livello = l;
            return true;
        }

        public static void Nota(string testo)
        {
            Console.Error.WriteLine(testo);
            Console.Error.Flush();
            MandaComeNotifica(testo, "info");
        }

        private static void MandaComeNotifica(string testo, string livello)
        {
            var canale = _canale;
            if (canale == null) return;
            if (Array.IndexOf(Livelli, livello) < Array.IndexOf(Livelli, _livello)) return;

            // TUTTO DENTRO UN try CHE NON RILANCIA, e non e' pigrizia: se
            // scrivere la notifica fallisse e l'errore risalisse, si
            // perderebbe la RISPOSTA a cui la notifica era accessoria. Un
            // racconto che fa fallire la cosa raccontata e' peggio del silenzio.
            // E non si chiama `Nota` qui dentro: sarebbe ricorsione infinita.
            try
            {
                Manda(canale, new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/message",
                    ["params"] = new Dictionary<string, object>
                    {
                        ["level"] = livello,
                        ["logger"] = "oce-solid-agent",
                        ["data"] = testo,
                    },
                });
            }
            catch { }
        }

        public static Dictionary<string, object> Risposta(object id, object risultato)
        {
            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["result"] = risultato,
            };
        }

        /// <summary>
        /// Un errore di PROTOCOLLO: la richiesta era malformata o il metodo non
        /// esiste. NON si usa per un'operazione di disegno che fallisce — quello
        /// e' un risultato, e va nel contenuto con `isError`, altrimenti chi
        /// chiama non puo' distinguere «non ti ho capito» da «il pezzo non e'
        /// venuto».
        /// </summary>
        public static Dictionary<string, object> Errore(object id, int codice, string messaggio)
        {
            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = codice,
                    ["message"] = messaggio,
                },
            };
        }

        /// <summary>Il risultato di uno strumento: testo, con o senza errore.</summary>
        public static Dictionary<string, object> Contenuto(string testo, bool eUnErrore = false)
        {
            return new Dictionary<string, object>
            {
                ["content"] = new object[]
                {
                    new Dictionary<string, object> { ["type"] = "text", ["text"] = testo },
                },
                ["isError"] = eUnErrore,
            };
        }

        /// <summary>
        /// Lo stesso contenuto, PIU' i dati in forma leggibile da un programma
        /// (voce W2.4).
        ///
        /// ── COSA CAMBIA PER CHI LEGGE ───────────────────────────────────
        ///
        /// Fino alla 0.57.0 tutto usciva come **testo**: lo stato del documento,
        /// le misure di un piano, il catalogo. Un modello linguistico lo legge
        /// benissimo; un programma no — deve ri-analizzare del JSON che era gia'
        /// JSON prima di essere messo dentro una stringa. `structuredContent` e'
        /// il campo che MCP prevede apposta, e costa una riga.
        ///
        /// ── PERCHE' IL TESTO RESTA ──────────────────────────────────────
        ///
        /// Non si sostituisce: si affianca. I client che leggono solo `content`
        /// sono la maggioranza, e togliergli il testo vorrebbe dire rompere
        /// tutti per far comodo a qualcuno. Lo dice anche lo spec: i due campi
        /// convivono, e il testo e' quello che si mostra a una persona.
        ///
        /// ── E PERCHE' NON SI DICHIARA UN `outputSchema` ─────────────────
        ///
        /// Lo spec e' netto: uno strumento che dichiara `outputSchema` **deve**
        /// restituire uno `structuredContent` conforme. Lo stato di un documento
        /// cambia forma con quello che c'e' dentro — un assieme ha
        /// `components`, una tavola ha `sheets`, una parte ha `faces` — e ogni
        /// campo nuovo aggiunto qui diventerebbe una promessa da mantenere.
        ///
        /// Uno schema **parziale e' peggio di nessuno schema**: un client che
        /// valida rifiuterebbe risposte giuste. Quindi qui si da' il dato senza
        /// promettere la forma, e lo schema si dichiara il giorno in cui la
        /// forma e' stabile per davvero — che e' una decisione, non una riga di
        /// codice. Scritto qui perche' il prossimo che passa non lo aggiunga
        /// «perche' mancava».
        ///
        /// UN NOME DIVERSO invece di un parametro in piu' su `Contenuto`: la
        /// stessa lezione gia' pagata due volte, l'ultima con
        /// `ContenutoConImmagini`.
        /// </summary>
        public static Dictionary<string, object> ContenutoConDati(
            string testo, object dati, bool eUnErrore = false)
        {
            var fuori = Contenuto(testo, eUnErrore);
            // NON si scrive il campo quando non c'e' niente da metterci: un
            // `structuredContent: null` e' una promessa disattesa, e un client
            // che lo trova puo' solo dedurre che qualcosa e' andato storto.
            if (dati != null) fuori["structuredContent"] = dati;
            return fuori;
        }

        /// <summary>
        /// Testo PIU' immagini, che e' la sola forma in cui un modello puo'
        /// VEDERE il pezzo invece di leggerne l'elenco delle facce.
        ///
        /// MCP prevede un contenuto di tipo `image`: i byte in base64 e il tipo
        /// MIME accanto. Fino alla 0.31.0 questo protocollo sapeva fare solo
        /// `text`, e il ponte non aveva nessuna strada per far uscire
        /// un'immagine — mentre la capacita' di scattarla esisteva, provata, nel
        /// banco.
        ///
        /// IL TESTO VA PER PRIMO, e non e' una questione di gusto: descrive cosa
        /// si sta guardando. Un'immagine senza didascalia in mezzo a una
        /// conversazione e' un indovinello.
        ///
        /// UN NOME DIVERSO invece di un parametro in piu' su `Contenuto`:
        /// cambiare l'insieme delle firme di un metodo rompe chi lo chiama per
        /// riflessione anche quando il compilatore tace. E' la lezione della
        /// 0.25.1, applicata prima di ricascarci.
        /// </summary>
        public static Dictionary<string, object> ContenutoConImmagini(
            string testo, IEnumerable<KeyValuePair<string, byte[]>> immagini)
        {
            var pezzi = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "text", ["text"] = testo },
            };

            if (immagini != null)
                foreach (var i in immagini)
                {
                    if (i.Value == null || i.Value.Length == 0) continue;
                    pezzi.Add(new Dictionary<string, object>
                    {
                        ["type"] = "image",
                        ["data"] = Convert.ToBase64String(i.Value),
                        ["mimeType"] = string.IsNullOrEmpty(i.Key) ? "image/png" : i.Key,
                    });
                }

            return new Dictionary<string, object>
            {
                ["content"] = pezzi.ToArray(),
                ["isError"] = false,
            };
        }

        public const int ErroreParsing = -32700;
        public const int RichiestaNonValida = -32600;
        public const int MetodoSconosciuto = -32601;
        public const int ParametriNonValidi = -32602;
        public const int ErroreInterno = -32603;
    }
}
