using System;
using System.Collections.Generic;
using static Prove.Contratti.Programma;
using static Condiviso.ClienteMcp;

namespace Prove.Contratti
{
    /// <summary>
    /// CHIEDERE ALL'UTENTE, NON AL MODELLO (voce W2.5).
    ///
    /// ── PERCHE' NON BASTA UNA FRASE ──────────────────────────────────────
    ///
    /// Le conferme di questo ponte erano frasi che il modello rilegge:
    /// «attenzione, questa operazione non si annulla». Una conferma che passa
    /// dal modello e' una conferma **che il modello puo' interpretare** — e
    /// interpretare una richiesta di permesso vuol dire, prima o poi,
    /// concedersela da solo.
    ///
    /// `elicitation/create` la fa arrivare a chi sta davanti allo schermo.
    ///
    /// ── E QUESTE PROVE SONO DI COMPORTAMENTO ─────────────────────────────
    ///
    /// Il client finto **dichiara** `elicitation`, riceve la domanda, e
    /// risponde `decline`. Il ponte deve fermarsi e **non toccare niente** —
    /// e lo si vede senza SOLIDWORKS, perche' la conferma si chiede PRIMA di
    /// qualunque conversazione col CAD.
    /// </summary>
    internal static class ProveLaConferma
    {
        internal static void Tutte()
        {
            Gruppo("la conferma all'utente, non al modello (W2.5)");

            var eseguibile = PonteOppureNonQui("la conferma sul ponte vero");
            if (eseguibile == null) return;

            // ── 1. UN CLIENT CHE SA CHIEDERE, E DICE DI NO ───────────────
            var p = Apri(eseguibile);
            if (p == null) { Segna("il ponte parte", false, ""); return; }
            try
            {
                Chiedi(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{\"elicitation\":{}}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                // Si manda il piano SENZA aspettare la risposta: quello che
                // deve arrivare per primo e' la DOMANDA del ponte.
                Scrivi(p,
                    "{\"jsonrpc\":\"2.0\",\"id\":30,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"delete_feature\",\"arguments\":" +
                    "{\"feature_uid\":\"Taglio-Estrusione1\"}}]}}}");

                // La domanda deve arrivare prima di qualunque risposta.
                Dictionary<string, object> domanda = null;
                object idDomanda = null;
                for (int i = 0; i < 8 && domanda == null; i++)
                {
                    var riga = Riga(p, 15000);
                    if (riga == null) break;
                    var o = Ogg(riga);
                    if (o == null) continue;
                    if (Convert.ToString(Dentro(o, "method")) == "elicitation/create")
                    { domanda = o; idDomanda = Dentro(o, "id"); }
                }

                Segna("il ponte CHIEDE prima di eliminare", domanda != null,
                      domanda == null ? "nessuna elicitation/create" : "elicitation/create");

                if (domanda != null)
                {
                    var parametri = Dentro(domanda, "params") as Dictionary<string, object>;
                    var messaggio = Convert.ToString(Dentro(parametri, "message"));

                    // LA DOMANDA DEVE DIRE COSA SI STA PER PERDERE. «Confermi?»
                    // da solo e' un pulsante, non una domanda.
                    Segna("e la domanda nomina l'operazione",
                          messaggio != null && messaggio.Contains("delete_feature"),
                          Corto(messaggio));
                    Segna("e dice che non si annulla",
                          messaggio != null &&
                          messaggio.IndexOf("non si annulla", StringComparison.OrdinalIgnoreCase) >= 0,
                          Corto(messaggio));

                    // ── SI RISPONDE DI NO ────────────────────────────────
                    Scrivi(p,
                        "{\"jsonrpc\":\"2.0\",\"id\":" + Convert.ToString(idDomanda) +
                        ",\"result\":{\"action\":\"decline\"}}");

                    string testo = null;
                    for (int i = 0; i < 8 && testo == null; i++)
                    {
                        var riga = Riga(p, 15000);
                        if (riga == null) break;
                        var o = Ogg(riga);
                        if (o == null) continue;
                        if (Convert.ToString(Dentro(o, "id")) != "30") continue;
                        testo = TestoDi(Dentro(o, "result") as Dictionary<string, object>);
                    }

                    Segna("e a un «no» SI FERMA", testo != null && testo.Contains("Fermato"),
                          Corto(testo));

                    // LA RIGA PIU' IMPORTANTE: non basta fermarsi, bisogna
                    // dire che non si e' toccato niente. Chi legge «fermato»
                    // senza quella frase deve andare a controllare il pezzo.
                    Segna("e dice di non aver toccato niente",
                          testo != null &&
                          testo.IndexOf("non ho toccato niente", StringComparison.OrdinalIgnoreCase) >= 0,
                          Corto(testo));
                }
            }
            finally { Chiudi(p); }

            // ── 2. UN CLIENT CHE NON SA CHIEDERE: IL RIPIEGO E' DICHIARATO ─
            //
            // Qui il ponte va avanti come prima — non e' un peggioramento, e'
            // il comportamento di sempre — ma **lo dice**. Un ripiego taciuto
            // e' il modo in cui una difesa sparisce senza che nessuno se ne
            // accorga.
            var q = Apri(eseguibile);
            if (q == null) { Segna("il ponte riparte", false, ""); return; }
            try
            {
                Chiedi(q, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                var risposta = Ogg(Chiedi(q, 31,
                    "{\"jsonrpc\":\"2.0\",\"id\":31,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"delete_feature\",\"arguments\":" +
                    "{\"feature_uid\":\"Taglio-Estrusione1\"}}]}}}"));

                // Senza SOLIDWORKS il piano si ferma subito dopo, e va bene: la
                // cosa da provare e' che **non sia rimasto appeso** ad aspettare
                // una conferma che questo client non sa dare.
                Segna("con un client che non sa chiedere, NON resta appeso",
                      risposta != null,
                      "risponde invece di aspettare una conferma impossibile");
            }
            finally { Chiudi(q); }
        }
    }
}
