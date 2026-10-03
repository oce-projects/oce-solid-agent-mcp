using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// CHIEDERE ALL'UTENTE, NON AL MODELLO (voce W2.5).
    ///
    /// ── IL PROBLEMA ──────────────────────────────────────────────────────
    ///
    /// Le conferme di questo ponte sono **frasi che il modello rilegge**:
    /// «attenzione, questa operazione non si annulla». Una conferma che passa
    /// dal modello e' una conferma che il modello puo' interpretare — e
    /// interpretare una richiesta di conferma vuol dire, prima o poi,
    /// concedersela da solo.
    ///
    /// Il protocollo ha un modo di chiedere **all'utente**: `elicitation/create`
    /// (MCP 2025-06-18). Il server manda una richiesta, il client la mostra a
    /// chi sta davanti allo schermo, e risponde `accept`, `decline` o `cancel`.
    ///
    /// ── LA PARTE DIFFICILE: IL GIRO E' SINCRONO ──────────────────────────
    ///
    /// Questo ponte legge una riga, la serve, risponde. Per chiedere qualcosa
    /// **nel mezzo** di una chiamata bisogna scrivere una richiesta e poi
    /// leggere da `stdin` finche' non arriva la risposta con quell'id.
    ///
    /// E mentre si aspetta puo' arrivare **altro**: un'altra richiesta del
    /// client, una notifica. Buttarla sarebbe un difetto silenzioso — il client
    /// aspetterebbe per sempre una risposta che nessuno gli dara'. Quindi si
    /// mette da parte, e il giro principale la serve appena puo'.
    ///
    /// ── E SE IL CLIENT NON SA CHIEDERE ───────────────────────────────────
    ///
    /// Non tutti i client supportano l'elicitation. Il ripiego e' **dichiarato,
    /// non silenzioso**: si va avanti come prima — che e' il comportamento di
    /// sempre, non un peggioramento — ma la risposta **dice** che la conferma
    /// non si e' potuta chiedere. Un ripiego taciuto e' il modo in cui una
    /// difesa sparisce senza che nessuno se ne accorga.
    /// </summary>
    public static class Elicitazione
    {
        /// <summary>
        /// Le righe arrivate mentre si aspettava una risposta, che il giro
        /// principale deve ancora servire. Vedi il commento in testa.
        /// </summary>
        private static readonly ConcurrentQueue<string> _arretrati =
            new ConcurrentQueue<string>();

        /// <summary>Il client ha dichiarato di saper chiedere all'utente?</summary>
        public static bool IlClientSaChiedere { get; private set; }

        /// <summary>
        /// Si legge da `initialize`: `capabilities.elicitation`. Una capacita'
        /// non dichiarata **non si presume**: chiedere a un client che non sa
        /// rispondere vorrebbe dire appendere la chiamata fino alla scadenza.
        /// </summary>
        public static void LeggiLeCapacita(Dictionary<string, object> parametri)
        {
            IlClientSaChiedere = false;
            if (parametri == null) return;
            object c;
            if (!parametri.TryGetValue("capabilities", out c)) return;
            var capacita = c as Dictionary<string, object>;
            if (capacita == null) return;
            IlClientSaChiedere = capacita.ContainsKey("elicitation");
        }

        /// <summary>La prossima riga messa da parte, o null.</summary>
        public static string Arretrato()
        {
            string riga;
            return _arretrati.TryDequeue(out riga) ? riga : null;
        }

        private static int _prossimoId = 9000;

        /// <summary>
        /// Chiede conferma all'utente e torna **vero solo se ha detto di si'**.
        ///
        /// Torna falso per `decline`, per `cancel`, per una risposta che non si
        /// capisce e per la scadenza. *Il dubbio vale no*: su un'operazione che
        /// non si annulla, interpretare un silenzio come un si' sarebbe il
        /// difetto peggiore di tutti.
        /// </summary>
        public static bool Conferma(TextWriter uscita, TextReader ingresso,
                                    string messaggio, int scadenzaMs = 300000)
        {
            if (uscita == null || ingresso == null) return false;

            int id = System.Threading.Interlocked.Increment(ref _prossimoId);
            Protocollo.Manda(uscita, new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = "elicitation/create",
                ["params"] = new Dictionary<string, object>
                {
                    ["message"] = messaggio,
                    // UNO SCHEMA VUOTO, e non e' pigrizia: qui non si chiede un
                    // dato, si chiede un permesso. La risposta che conta e'
                    // `action`, e chiedere un campo in piu' vorrebbe dire far
                    // compilare un modulo per dire di si'.
                    ["requestedSchema"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>(),
                    },
                },
            });

            var fine = Environment.TickCount + scadenzaMs;
            while (Environment.TickCount < fine)
            {
                string riga;
                try { riga = ingresso.ReadLine(); }
                catch { return false; }
                if (riga == null) return false;          // il client se n'e' andato
                if (string.IsNullOrWhiteSpace(riga)) continue;

                Dictionary<string, object> m;
                try { m = Protocollo.DaJson(riga); }
                catch { _arretrati.Enqueue(riga); continue; }
                if (m == null) continue;

                object suo;
                if (!m.TryGetValue("id", out suo) ||
                    Convert.ToString(suo) != id.ToString())
                {
                    // NON E' LA MIA RISPOSTA: si mette da parte. Buttarla
                    // lascerebbe il client ad aspettare per sempre.
                    _arretrati.Enqueue(riga);
                    continue;
                }

                var risultato = (m.TryGetValue("result", out var r)
                    ? r as Dictionary<string, object> : null);
                if (risultato == null) return false;      // un `error` e' un no
                object azione;
                if (!risultato.TryGetValue("action", out azione)) return false;
                return string.Equals(Convert.ToString(azione), "accept",
                                     StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}
