using System;
using System.Collections.Generic;
using static Prove.Contratti.Programma;
using static Condiviso.ClienteMcp;

namespace Prove.Contratti
{
    /// <summary>
    /// SBAGLIARE SENZA TOCCARE IL PEZZO (voce Z-G6).
    ///
    /// ── PERCHE' QUESTE PROVE VALGONO PIU' DELLE ALTRE DI OGGI ────────────
    ///
    /// Quasi tutti i controlli scritti il 23/9/2026 guardano un file: il
    /// catalogo, un sorgente, un JSON. Questi **parlano col ponte vero**, su
    /// stdio, come farebbe un client — e lo fanno **con SOLIDWORKS chiuso**,
    /// che e' esattamente la condizione in cui la prova a vuoto deve servire.
    ///
    /// Non e' una prova di struttura: e' una prova di comportamento.
    ///
    /// ── E LA RIGA CHE CONTA DI PIU' ──────────────────────────────────────
    ///
    /// Che la risposta dica **cosa non ha guardato**. Una prova a vuoto che
    /// tace sui limiti fa credere che il piano sia a posto, e allora il guaio
    /// arriva un passo dopo invece che subito — con in piu' la fiducia di chi
    /// l'aveva chiesta.
    /// </summary>
    internal static class ProveLaProvaAVuoto
    {
        internal static void Tutte()
        {
            Gruppo("la prova a vuoto: sbagliare senza toccare il pezzo (Z-G6)");

            var eseguibile = PonteOppureNonQui("la prova a vuoto sul ponte vero");
            if (eseguibile == null) return;

            var p = Apri(eseguibile);
            if (p == null) { Segna("il ponte parte", false, "non si e' avviato"); return; }
            try
            {
                Chiedi(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                // ── UN PIANO SBAGLIATO IN QUATTRO MODI DIVERSI ───────────
                //
                // 1. un'operazione che non esiste
                // 2. un argomento obbligatorio che manca
                // 3. un argomento SCONOSCIUTO, che verrebbe ignorato in silenzio
                // 4. un rimando a un passo che non c'e' ancora
                var brutta = Ogg(Chiedi(p, 20,
                    "{\"jsonrpc\":\"2.0\",\"id\":20,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"dry_run\":true,\"steps\":[" +
                    "{\"operation\":\"estrudi_rettangolino\"}," +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{\"length_mm\":100}}," +
                    "{\"operation\":\"extrude_circle\",\"arguments\":" +
                    "{\"diameter_mm\":40,\"height_mm\":10,\"depth_mm\":5}}," +
                    "{\"operation\":\"shell\",\"arguments\":{\"thickness_mm\":3," +
                    "\"face_ref\":{\"step\":9,\"kind\":\"top_face\"}}}" +
                    "]}}}"));

                var risultato = Dentro(brutta, "result") as Dictionary<string, object>;
                var testo = TestoDi(risultato);

                Segna("la prova a vuoto risponde anche con SOLIDWORKS chiuso",
                      !string.IsNullOrEmpty(testo), Corto(testo));

                Segna("e dice che e' una prova a vuoto",
                      testo.IndexOf("PROVA A VUOTO", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(testo));

                Segna("prende l'operazione che non esiste",
                      testo.Contains("estrudi_rettangolino") && testo.Contains("non esiste"),
                      "e prova a suggerire nomi simili");

                Segna("prende l'argomento obbligatorio che manca",
                      testo.Contains("width_mm") || testo.Contains("thickness_mm"),
                      "su «extrude_rectangle» manca piu' di un obbligatorio");

                // IL PIU' PREZIOSO DEI QUATTRO: un argomento scritto male non
                // da' nessun errore, viene ignorato, e il pezzo esce diverso
                // da come lo si e' chiesto. E' il difetto che non si vede.
                Segna("prende l'argomento SCONOSCIUTO, che verrebbe ignorato",
                      testo.Contains("depth_mm") &&
                      testo.IndexOf("IGNORATO", StringComparison.OrdinalIgnoreCase) >= 0,
                      "il difetto che non da' errore");

                Segna("prende il rimando a un passo che non c'e'",
                      testo.Contains("passo 9") ||
                      testo.IndexOf("non e' ancora stato fatto", StringComparison.Ordinal) >= 0,
                      "face_ref verso il passo 9, in un piano di quattro");

                Segna("e la risposta e' marcata come errore",
                      risultato != null && risultato.ContainsKey("isError") &&
                      Convert.ToString(risultato["isError"]).Equals(
                          "True", StringComparison.OrdinalIgnoreCase),
                      "un piano che non regge non e' una risposta neutra");

                Segna("e dichiara cosa NON ha potuto controllare",
                      testo.Contains("NON controllati") && testo.Contains("uid"),
                      "tacere sui limiti farebbe credere che il piano sia a posto");

                // ── UN PIANO BUONO NON VIENE BOCCIATO ────────────────────
                //
                // La meta' che di solito non si prova. Un controllo che boccia
                // anche cio' che va bene si smette di usarlo, e allora non
                // protegge piu' niente.
                var buona = Ogg(Chiedi(p, 21,
                    "{\"jsonrpc\":\"2.0\",\"id\":21,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"dry_run\":true,\"steps\":[" +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":" +
                    "{\"plane\":\"front\",\"length_mm\":120,\"width_mm\":80," +
                    "\"thickness_mm\":20}}]}}}"));
                var suoBuona = Dentro(buona, "result") as Dictionary<string, object>;
                var testoBuona = TestoDi(suoBuona);

                Segna("un piano buono NON viene bocciato",
                      testoBuona.Contains("il piano regge"), Corto(testoBuona));

                Segna("ma anche li' dice cosa non ha guardato",
                      testoBuona.Contains("NON controllati"),
                      "soprattutto quando regge");

                // ── E `dry_run` E' DICHIARATO NELLO SCHEMA ─────────────────
                //
                // Una capacita' che c'e' e non si vede in `tools/list` e' una
                // capacita' che nessun modello usera' mai.
                // LA RIGA GREZZA, non il dizionario: `Convert.ToString` su un
                // `Dictionary` da' il nome del tipo, e questa riga era nata
                // rossa proprio cosi' — cercava «dry_run» dentro
                // «System.Collections.Generic.Dictionary...».
                var rigaStrumenti = Chiedi(p, 22,
                    "{\"jsonrpc\":\"2.0\",\"id\":22,\"method\":\"tools/list\",\"params\":{}}");
                Segna("«dry_run» e' dichiarato nello schema di «build»",
                      rigaStrumenti != null &&
                      rigaStrumenti.Contains("\"dry_run\"") &&
                      rigaStrumenti.Contains("PROVA A VUOTO"),
                      "una capacita' che non si vede in tools/list non la usa nessuno");
            }
            finally { Chiudi(p); }
        }
    }
}
