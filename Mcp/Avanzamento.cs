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
    /// scoprirlo rotto in un client, cioe' mai. Il <see cref="Filo"/> tiene
    /// l'ultimo valore mandato e **scarta** un passo che non cresce: una
    /// regola sola, in un posto solo, che non si puo' dimenticare in nessuno
    /// dei quattro punti che la usano.
    /// </summary>
    internal static class Avanzamento
    {
        /// <summary>
        /// Il gettone dentro `params._meta.progressToken`, o null se il
        /// client non ne ha messo uno.
        ///
        /// Una stringa vuota vale come «niente»: e' quasi sempre un campo
        /// riempito male, e rimandarla indietro non aiuterebbe nessuno a
        /// riconoscere la propria richiesta.
        /// </summary>
        public static object Gettone(Dictionary<string, object> parametri)
        {
            if (parametri == null) return null;
            object meta;
            if (!parametri.TryGetValue("_meta", out meta)) return null;
            var dentro = meta as Dictionary<string, object>;
            if (dentro == null) return null;
            object gettone;
            if (!dentro.TryGetValue("progressToken", out gettone)) return null;
            if (gettone == null) return null;
            var testo = gettone as string;
            if (testo != null && testo.Length == 0) return null;
            return gettone;
        }

        /// <summary>
        /// La notifica, staccata da chi la manda, cosi' si puo' guardare
        /// senza un canale aperto.
        ///
        /// `total` si omette quando non si sa quanti passi saranno: la
        /// specifica lo permette, e scrivere un totale inventato darebbe a
        /// chi legge una barra di avanzamento che mente.
        /// </summary>
        public static Dictionary<string, object> Notifica(
            object gettone, double fatti, double totale, string messaggio)
        {
            var parametri = new Dictionary<string, object>
            {
                ["progressToken"] = gettone,
                ["progress"] = fatti,
            };
            if (totale > 0) parametri["total"] = totale;
            if (!string.IsNullOrWhiteSpace(messaggio)) parametri["message"] = messaggio;

            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/progress",
                ["params"] = parametri,
            };
        }

        /// <summary>
        /// IL FILO DI UNA RICHIESTA: il gettone, e quanto e' gia' stato detto.
        ///
        /// Si costruisce sempre, anche senza gettone: cosi' i punti che lo
        /// usano non hanno un `if` ciascuno. Un `if (filo != null)` sparso in
        /// quattro posti e' il modo in cui il quinto se lo dimentica —
        /// e' la stessa ragione per cui `Attrezzi._registro` non e' mai null.
        /// </summary>
        internal sealed class Filo
        {
            private readonly object _gettone;
            private readonly Func<DateTime> _orologio;
            private readonly DateTime _inizio;
            private double _ultimo;

            /// <summary>Quante notifiche sono davvero partite.</summary>
            public int Mandate { get; private set; }

            /// <summary>Quanti passi sono stati scartati perche' non crescevano.</summary>
            public int Scartati { get; private set; }

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
            public double Totale { get; set; }

            /// <summary>
            /// L'ultimo valore mandato. Serve a chi riprende il conto dove
            /// un altro l'ha lasciato — l'esecuzione dopo il controllo —
            /// senza doversi ricordare quanti passi aveva l'altro.
            /// </summary>
            public double Ultimo { get { return _ultimo; } }

        /// <summary>
        /// L'OROLOGIO SI PASSA, NON SI LEGGE, e per la solita ragione: una
        /// prova che non puo' fissare l'ora non puo' guardare cosa viene
        /// scritto. E' la stessa scelta di `Guinzaglio` e `SentinellaSolidWorks`.
        /// </summary>
            public Filo(object gettone) : this(gettone, () => DateTime.Now) { }

            public Filo(object gettone, Func<DateTime> orologio)
            {
                _gettone = gettone;
                _orologio = orologio ?? (() => DateTime.Now);
                _inizio = _orologio();
            }

            /// <summary>Da quanto sta lavorando.</summary>
            public TimeSpan Trascorso { get { return _orologio() - _inizio; } }

            /// <summary>Il client ha chiesto di essere aggiornato?</summary>
            public bool Acceso { get { return _gettone != null; } }

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
            private string ColTempo(string messaggio)
            {
                var t = Trascorso;
                if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                var quanto = t.TotalMinutes >= 1
                    ? ((int)t.TotalMinutes) + "m" + t.Seconds.ToString("00") + "s"
                    : ((int)t.TotalSeconds) + "s";
                return string.IsNullOrWhiteSpace(messaggio)
                    ? quanto
                    : messaggio + " \u2014 " + quanto;
            }

            public void Passo(double fatti, string messaggio)
            {
                if (_gettone == null) return;
                if (fatti <= _ultimo) { Scartati++; return; }
                _ultimo = fatti;
                Mandate++;
                Protocollo.NotificaSulCanale(
                    Notifica(_gettone, fatti, Totale, ColTempo(messaggio)));
            }
        }
    }
}
