using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Oce.Solid.Agent.Mcp;
using Oce.Solid.Agent.Ponte;
using Oce.Solid.Agent.Strumenti;

namespace Oce.Solid.Agent
{
    /// <summary>
    /// Il giro principale: legge una riga, risponde una riga.
    ///
    /// STDOUT E' SOLO DEL PROTOCOLLO. Qualunque altra cosa scritta li' rompe la
    /// conversazione senza dire perche'. Tutto il resto — l'aggancio, gli
    /// errori, quello che l'add-in racconta — va su stderr.
    ///
    /// SI PARTE ANCHE SE SOLIDWORKS NON C'E'. Un server MCP che muore
    /// all'avvio non fa in tempo a spiegarsi: chi lo lancia vede solo un
    /// processo finito. Qui si parte comunque e il motivo si legge chiamando
    /// uno strumento — che e' il posto dove chi usa il ponte sta guardando.
    /// </summary>
    internal static class Programma
    {
        private const string Versione = "0.2.0";

        private static int Main(string[] argomenti)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);

            var sessione = new Sessione();
            var motivo = sessione.Aggancia();
            if (motivo != null) Protocollo.Nota("[ponte] " + motivo);
            else
                Protocollo.Nota(
                    "[ponte] SolidWorks " + sessione.VersioneSolidWorks() +
                    ", libreria di disegno " + sessione.VersioneLibreria());

            var catalogo = new Catalogo();
            if (catalogo.ErroreDiCaricamento != null)
                Protocollo.Nota("[ponte] " + catalogo.ErroreDiCaricamento);
            else
                Protocollo.Nota("[ponte] catalogo: " + catalogo.Quante + " operazioni provate");

            if (argomenti != null && argomenti.Length > 0 && argomenti[0] == "--prova")
                return Prova(sessione, catalogo);

            var attrezzi = new Attrezzi(sessione, catalogo);
            var uscita = Console.Out;

            // DA QUI IN POI IL PONTE RACCONTA ANCHE AL CLIENT (voce W2.8).
            // Non prima: il ciclo dei messaggi non e' ancora partito, e una riga
            // su stdout senza nessuno che legga righe JSON e' solo rumore che
            // rompe chi arrivera' dopo.
            Protocollo.AccendiIlCanale(uscita);

            string riga;
            while ((riga = Console.In.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(riga)) continue;

                Dictionary<string, object> messaggio;
                try { messaggio = Protocollo.DaJson(riga); }
                catch (Exception ex)
                {
                    Protocollo.Manda(uscita, Protocollo.Errore(
                        null, Protocollo.ErroreParsing, "JSON non valido: " + ex.Message));
                    continue;
                }
                if (messaggio == null) continue;

                object id = messaggio.TryGetValue("id", out var i) ? i : null;
                var metodo = messaggio.TryGetValue("method", out var m)
                    ? Convert.ToString(m) : null;
                var parametri = messaggio.TryGetValue("params", out var p)
                    ? p as Dictionary<string, object> : null;

                // Le NOTIFICHE non hanno id e non vogliono risposta. Rispondere
                // a una notifica e' il modo piu' rapido di confondere chi
                // ascolta: si ritroverebbe una risposta che non aspettava.
                bool eNotifica = !messaggio.ContainsKey("id");

                try
                {
                    var risposta = Serve(metodo, parametri, attrezzi, sessione, catalogo, id);
                    if (risposta != null && !eNotifica) Protocollo.Manda(uscita, risposta);
                }
                catch (Exception ex)
                {
                    Protocollo.Nota("[ponte] " + ex);
                    if (!eNotifica)
                        Protocollo.Manda(uscita, Protocollo.Errore(
                            id, Protocollo.ErroreInterno, ex.Message));
                }
            }

            Protocollo.Nota("[ponte] chiuso");
            return 0;
        }

        private static Dictionary<string, object> Serve(
            string metodo,
            Dictionary<string, object> parametri,
            Attrezzi attrezzi,
            Sessione sessione,
            Catalogo catalogo,
            object id)
        {
            switch (metodo)
            {
                case "initialize":
                    return Protocollo.Risposta(id, new Dictionary<string, object>
                    {
                        // La versione del protocollo si rimanda com'e' arrivata
                        // quando la si riconosce: dichiararne una sola vuol
                        // dire rompersi al primo aggiornamento del client.
                        ["protocolVersion"] = VersioneProtocollo(parametri),
                        ["capabilities"] = new Dictionary<string, object>
                        {
                            ["tools"] = new Dictionary<string, object>(),
                            // Il catalogo si puo' anche ATTACCARE invece che
                            // chiedere (W2.12). Senza questa riga un client non
                            // prova nemmeno `resources/list`, e la porta c'e'
                            // ma nessuno la vede. `subscribe` e `listChanged`
                            // restano fuori apposta: **il catalogo non cambia
                            // mentre il ponte gira** — e' un file letto
                            // all'avvio — e dichiarare una capacita' che non si
                            // esercita e' una promessa che qualcuno aspettera'.
                            ["resources"] = new Dictionary<string, object>(),
                            // Il ponte racconta cosa fa, e lo dichiara: senza
                            // questa riga un client non sa che puo' chiedere
                            // `logging/setLevel`, e le notifiche gli arrivano
                            // senza che le abbia mai potute regolare (W2.8).
                            ["logging"] = new Dictionary<string, object>(),
                        },
                        ["serverInfo"] = new Dictionary<string, object>
                        {
                            ["name"] = "oce-solid-agent",
                            ["version"] = Versione,
                        },
                        ["instructions"] =
                            "Un ponte verso SOLIDWORKS. Prima di proporre " +
                            "qualunque cosa chiama «guarda_il_pezzo»: gli " +
                            "identificativi delle facce e degli spigoli nascono " +
                            "li' e valgono per quella lettura. Dopo ogni " +
                            "modifica il pezzo cambia e vanno riletti.\n\n" +
                            "Se la forma e' in dubbio, «guarda_il_pezzo» con " +
                            "«viste» ti fa VEDERE il pezzo: l'elenco delle facce " +
                            "dice quante sono e quanto misurano, non com'e' " +
                            "fatto.\n\n" +
                            "Se quello che hai disegnato non va, «annulla» ritira " +
                            "l'ultimo piano tutto intero: non serve rifare il " +
                            "pezzo da capo.",
                    });

                case "notifications/initialized":
                    return null;

                case "ping":
                    return Protocollo.Risposta(id, new Dictionary<string, object>());

                // Il client decide quanto vuole sentire (voce W2.8).
                //
                // UN LIVELLO CHE NON SI RICONOSCE E' UN ERRORE, non un silenzio:
                // accettarlo e ignorarlo lascerebbe il client convinto di aver
                // alzato la soglia mentre i messaggi continuano ad arrivare —
                // oppure, peggio, convinto di sentire tutto mentre non sente
                // niente.
                case "logging/setLevel":
                {
                    var voluto = parametri != null && parametri.TryGetValue("level", out var lv)
                        ? Convert.ToString(lv) : null;
                    if (!Protocollo.ImpostaIlLivello(voluto))
                        return Protocollo.Errore(
                            id, Protocollo.ParametriNonValidi,
                            "Livello «" + (voluto ?? "") + "» sconosciuto. Ci sono: " +
                            "debug, info, notice, warning, error, critical, alert, emergency.");
                    return Protocollo.Risposta(id, new Dictionary<string, object>());
                }

                case "tools/list":
                    return Protocollo.Risposta(id, new Dictionary<string, object>
                    {
                        ["tools"] = attrezzi.Elenco(),
                    });

                // ── LE RISORSE (voce W2.12) ─────────────────────────────
                //
                // Stesso catalogo degli strumenti, presa da un'altra porta:
                // `Risorse` riceve l'oggetto `Catalogo` che e' gia' in mano e
                // non ne costruisce un altro. Costruirne un altro vorrebbe dire
                // rileggere il file, e due letture dello stesso file in momenti
                // diversi sono due cataloghi che possono differire.
                case "resources/list":
                    return Protocollo.Risposta(id, new Dictionary<string, object>
                    {
                        ["resources"] = new Risorse(catalogo).Elenco(),
                    });

                case "resources/templates/list":
                    return Protocollo.Risposta(id, new Dictionary<string, object>
                    {
                        ["resourceTemplates"] = new Risorse(catalogo).Modelli(),
                    });

                case "resources/read":
                {
                    var uri = parametri != null && parametri.TryGetValue("uri", out var u)
                        ? Convert.ToString(u) : null;
                    if (string.IsNullOrWhiteSpace(uri))
                        return Protocollo.Errore(
                            id, Protocollo.ParametriNonValidi, "manca «uri»");

                    var risorse = new Risorse(catalogo);
                    var contenuto = risorse.Leggi(uri);
                    if (contenuto != null)
                        return Protocollo.Risposta(id, contenuto);

                    // UN URI CHE NON ESISTE DICE COSA ESISTE. Un «non trovato»
                    // nudo lascia a chi chiede l'unico rimedio che non
                    // funziona: riprovare uguale.
                    var simili = risorse.Simili(uri);
                    return Protocollo.Errore(
                        id, Protocollo.ParametriNonValidi,
                        "Non conosco la risorsa «" + uri + "». C'e' «" +
                        Risorse.UriElenco + "» (tutte le operazioni in breve) e " +
                        "«" + Risorse.PrefissoOperazione + "<nome>» per una sola." +
                        (simili.Count == 0
                            ? ""
                            : " Forse cercavi: " + string.Join(", ", simili) + "."));
                }

                case "tools/call":
                {
                    var nome = parametri != null && parametri.TryGetValue("name", out var n)
                        ? Convert.ToString(n) : null;
                    var suoi = parametri != null && parametri.TryGetValue("arguments", out var a)
                        ? a as Dictionary<string, object> : null;
                    if (string.IsNullOrWhiteSpace(nome))
                        return Protocollo.Errore(
                            id, Protocollo.ParametriNonValidi, "manca «name»");
                    return Protocollo.Risposta(id, attrezzi.Chiama(nome, suoi));
                }

                default:
                    return Protocollo.Errore(
                        id, Protocollo.MetodoSconosciuto,
                        "Metodo non gestito: " + metodo);
            }
        }

        private static string VersioneProtocollo(Dictionary<string, object> parametri)
        {
            try
            {
                if (parametri != null &&
                    parametri.TryGetValue("protocolVersion", out var v) &&
                    v != null)
                {
                    var chiesta = Convert.ToString(v);
                    if (!string.IsNullOrWhiteSpace(chiesta)) return chiesta;
                }
            }
            catch { }
            return "2024-11-05";
        }

        /// <summary>
        /// `--prova`: un giro secco senza client, per sapere se il ponte sta in
        /// piedi su questa macchina. Stampa su stderr e non tocca il pezzo.
        /// </summary>
        private static int Prova(Sessione sessione, Catalogo catalogo)
        {
            Protocollo.Nota("[prova] agganciata: " + sessione.Agganciata);
            Protocollo.Nota("[prova] libreria di disegno: " + sessione.PercorsoLibreria);
            Protocollo.Nota("[prova] catalogo: " + catalogo.Percorso);

            if (catalogo.Quante > 0)
            {
                var qualcuna = string.Join(", ",
                    new List<string>(catalogo.Nomi).GetRange(0, Math.Min(5, catalogo.Quante)));
                Protocollo.Nota("[prova] prime operazioni: " + qualcuna);
            }

            if (!sessione.Agganciata) return 1;

            var stato = sessione.Stato(out var errore);
            if (stato == null) { Protocollo.Nota("[prova] " + errore); return 1; }

            foreach (var chiave in new[] { "titolo", "tipo", "volume_mm3", "corpi_totali" })
                if (stato.TryGetValue(chiave, out var v))
                    Protocollo.Nota("[prova] " + chiave + " = " + v);

            Protocollo.Nota("[prova] il ponte sta in piedi.");
            return 0;
        }
    }
}
