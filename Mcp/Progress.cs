using System;
using System.Collections.Generic;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// L'AVANZAMENTO, COL MECCANISMO DEL PROTOCOLLO (voce W2.2)
    ///
    /// ── PERCHE' NON UN SECONDO STRUMENTO «A CHE PUNTO SEI» ──────────────
    ///
    /// Perche' esiste gia' nella specifica MCP 2025-06-18, e reinventarlo
    /// costerebbe di piu' per farsi capire da meno gente: un client generico
    /// sa leggere `notifications/progress`, e non sapra' mai leggere uno
    /// strumento inventato qui.
    ///
    /// Il client che vuole essere aggiornato mette un `progressToken` dentro
    /// `params._meta` della richiesta. Chi non lo mette non vuole essere
    /// aggiornato, e allora **non si manda niente**: una notifica che nessuno
    /// aspetta e' rumore sul canale del protocollo.
    ///
    /// ── IL GETTONE SI RIMANDA COM'E' ARRIVATO ───────────────────────────
    ///
    /// La specifica ammette una stringa o un intero. Questo codice non lo
    /// guarda: lo tiene come `object` e lo ricopia. Interpretarlo vorrebbe
    /// dire poterlo cambiare — un `42` che torna indietro come `"42"` non
    /// combacia piu' con niente dalla parte del client, e il client non ha
    /// modo di dire perche'.
    ///
    /// ── «progress» DEVE CRESCERE, E QUI NON PUO' NON CRESCERE ───────────
    ///
    /// La specifica lo pretende. Lasciarlo a chi chiama vorrebbe dire
    /// scoprirlo rotto in un client, cioe' mai. Il <see cref="Wire"/> tiene
    /// l'ultimo valore mandato e **scarta** un passo che non cresce: una
    /// regola sola, in un posto solo, che non si puo' dimenticare in nessuno
    /// dei quattro punti che la usano.
    /// </summary>
    internal static class Progress
    {
        /// <summary>
        /// Il gettone dentro `params._meta.progressToken`, o null se il
        /// client non ne ha messo uno.
        ///
        /// Una stringa vuota vale come «niente»: e' quasi sempre un campo
        /// riempito male, e rimandarla indietro non aiuterebbe nessuno a
        /// riconoscere la propria richiesta.
        /// </summary>
        public static object Token(Dictionary<string, object> parameters)
        {
            if (parameters == null) return null;
            object meta;
            if (!parameters.TryGetValue("_meta", out meta)) return null;
            var inside = meta as Dictionary<string, object>;
            if (inside == null) return null;
            object token;
            if (!inside.TryGetValue("progressToken", out token)) return null;
            if (token == null) return null;
            var text = token as string;
            if (text != null && text.Length == 0) return null;
            return token;
        }

        /// <summary>
        /// La notifica, staccata da chi la manda, cosi' si puo' guardare
        /// senza un canale aperto.
        ///
        /// `total` si omette quando non si sa quanti passi saranno: la
        /// specifica lo permette, e scrivere un totale inventato darebbe a
        /// chi legge una barra di avanzamento che mente.
        /// </summary>
        public static Dictionary<string, object> Notify(
            object token, double done, double total, string message)
        {
            var parameters = new Dictionary<string, object>
            {
                ["progressToken"] = token,
                ["progress"] = done,
            };
            if (total > 0) parameters["total"] = total;
            if (!string.IsNullOrWhiteSpace(message)) parameters["message"] = message;

            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/progress",
                ["params"] = parameters,
            };
        }

        /// <summary>
        /// IL FILO DI UNA RICHIESTA: il gettone, e quanto e' gia' stato detto.
        ///
        /// Si costruisce sempre, anche senza gettone: cosi' i punti che lo
        /// usano non hanno un `if` ciascuno. Un `if (filo != null)` sparso in
        /// quattro posti e' il modo in cui il quinto se lo dimentica —
        /// e' la stessa ragione per cui `McpTools._registro` non e' mai null.
        /// </summary>
        internal sealed class Wire
        {
            private readonly object _token;
            private readonly Func<DateTime> _clock;
            private readonly DateTime _start;
            private double _last;

            /// <summary>Quante notifiche sono davvero partite.</summary>
            public int Mandate { get; private set; }

            /// <summary>Quanti passi sono stati scartati perche' non crescevano.</summary>
            public int Discarded { get; private set; }

            /// <summary>
            /// Quanti passi in tutto, se si sa. Zero vuol dire «non lo so»,
            /// e allora `total` non viene mandato affatto.
            ///
            /// STA QUI E NON NEI PUNTI CHE MANDANO, e non e' comodita': un
            /// piano di cinque passi si annuncia due volte — una quando lo si
            /// controlla, una quando lo si esegue — e i due punti stanno in
            /// due file diversi. Se ciascuno si calcolasse il totale per conto
            /// suo, prima o poi direbbero numeri diversi e la barra di chi
            /// legge tornerebbe indietro.
            /// </summary>
            public double Total { get; set; }

            /// <summary>
            /// L'ultimo valore mandato. Serve a chi riprende il conto dove
            /// un altro l'ha lasciato — l'esecuzione dopo il controllo —
            /// senza doversi ricordare quanti passi aveva l'altro.
            /// </summary>
            public double Last { get { return _last; } }

        /// <summary>
        /// L'OROLOGIO SI PASSA, NON SI LEGGE, e per la solita ragione: una
        /// prova che non puo' fissare l'ora non puo' guardare cosa viene
        /// scritto. E' la stessa scelta di `Leash` e `SolidWorksSentinel`.
        /// </summary>
            public Wire(object token) : this(token, () => DateTime.Now) { }

            public Wire(object token, Func<DateTime> clock)
            {
                _token = token;
                _clock = clock ?? (() => DateTime.Now);
                _start = _clock();
            }

            /// <summary>Da quanto sta lavorando.</summary>
            public TimeSpan Elapsed { get { return _clock() - _start; } }

            /// <summary>Il client ha chiesto di essere aggiornato?</summary>
            public bool On { get { return _token != null; } }

            /// <summary>
            /// IL TEMPO ACCANTO AL PASSO (voce S0).
            ///
            /// Il criterio di S0 non chiede «un avanzamento»: chiede che chi
            /// guarda da fuori sappia distinguere **«sta lavorando» da «e'
            /// piantato» senza guardare la CPU**. Il numero del passo da solo
            /// non lo dice — «passo 3 di 5» e' identico se il passo 3 e'
            /// cominciato due secondi fa o undici minuti fa.
            ///
            /// Si scrive come lo legge una persona (`2m14s`, non `134`): il
            /// `message` e' l'unico campo della notifica che un umano legge,
            /// e metterci dei secondi crudi vorrebbe dire farglieli dividere.
            /// </summary>
            private string ColTempo(string message)
            {
                var t = Elapsed;
                if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                var howMuch = t.TotalMinutes >= 1
                    ? ((int)t.TotalMinutes) + "m" + t.Seconds.ToString("00") + "s"
                    : ((int)t.TotalSeconds) + "s";
                return string.IsNullOrWhiteSpace(message)
                    ? howMuch
                    : message + " \u2014 " + howMuch;
            }

            public void Step(double done, string message)
            {
                if (_token == null) return;
                if (done <= _last) { Discarded++; return; }
                _last = done;
                Mandate++;
                Protocol.NotifyOnChannel(
                    Notify(_token, done, Total, ColTempo(message)));
            }
        }
    }
}
