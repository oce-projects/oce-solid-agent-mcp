using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
// ALIAS E NON `using static`: il banco ha gia' un `Apri` e un `Chiudi` suoi,
// e i membri della classe vincono su quelli importati. Con l'alias si vede a
// colpo d'occhio quali chiamate escono da questo processo e vanno al ponte.
using Mcp = Condiviso.ClienteMcp;

namespace ProvaSaldatura
{
    /// <summary>
    /// UN GIRO DI PROVE CHE PASSA DALLA PORTA DEL CLIENTE (voce F14).
    ///
    /// ── PERCHE' ESISTE, E NON E' UNA SIMMETRIA ESTETICA ──────────────────
    ///
    /// Tutto il resto di questo banco chiama la LIBRERIA. Il 18/9/2026 il
    /// ponte rifiutava ogni assieme mentre il suo catalogo ne prometteva una
    /// famiglia intera, e il banco aveva 781 prove verdi — cinque delle quali
    /// sugli assiemi — e **nessuna poteva vederlo**: `EsecutorePiano` non
    /// compare in nessun file di `Banco/`.
    ///
    /// *Due misure vere di due cose diverse, e chi legge il numero grande crede
    /// che copra anche l'altra.*
    ///
    /// ── COSA PROVA CHE NIENT'ALTRO PROVA ─────────────────────────────────
    ///
    /// Tutto cio' che sta FRA il cliente e la libreria, con un documento vero
    /// davanti: i controlli preliminari, la traduzione fra JSON e argomenti, i
    /// rifiuti su operazioni che il documento aperto non ammette, e
    /// `guarda_il_pezzo`, che non passa nemmeno dall'esecutore.
    ///
    /// `Prove.Contratti` parla gia' al ponte sullo stdio, ma **senza
    /// SOLIDWORKS**: li' ogni piano muore su «non c'e' nessun documento
    /// aperto», che e' un rifiuto vero e non prova niente sul resto. Qui il
    /// documento c'e'. Il trasporto e' lo stesso — `Condiviso/ClienteMcp.cs` —
    /// perche' due trasporti sarebbero due posti in cui riparare il BOM.
    ///
    /// ── E UNO DEVE ESSERE ROSSO PER COSTRUZIONE ──────────────────────────
    ///
    /// Un piano che chiede un'operazione di `assiemi` con una PARTE aperta
    /// dev'essere rifiutato, e la risposta deve dire **su cosa si sta
    /// lavorando** — non una frase fissa. Un controllo mai visto scattare non
    /// e' un controllo, e un rifiuto che non nomina il documento non si
    /// distingue da uno che rifiuta tutto.
    /// </summary>
    internal static partial class Program
    {
        private static void LaPortaDelCliente()
        {
            var eseguibile = IlPonte();
            if (eseguibile == null)
            {
                ErroreDiBanco("la porta del cliente",
                              "non trovo bin\\oce-solid-agent-mcp.exe — si produce con .\\build.ps1");
                return;
            }

            // IL DOCUMENTO LO PREPARA IL BANCO, e il ponte lo trova davanti.
            // E' esattamente la differenza con `Prove.Contratti`: li' non c'e'
            // niente aperto, e ogni piano muore sul primo controllo.
            IModelDoc2 doc = null;
            Mcp.Ponte p = null;
            try
            {
                doc = NuovaParte();
                string titolo = doc.GetTitle();

                p = Mcp.Apri(eseguibile);
                if (p == null)
                {
                    ErroreDiBanco("la porta del cliente", "il ponte non e' partito");
                    return;
                }

                // ── initialize ───────────────────────────────────────────
                var r = Mcp.Chiedi(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"banco\",\"version\":\"1\"}}}");
                Segna("porta: initialize risponde", r != null,
                      r != null ? "" : "niente entro " + (Mcp.AttesaMs / 1000) + "s. Righe viste: " +
                                       string.Join(" | ", p.Viste.ToArray()));
                if (r == null) return;

                // ── 1. guarda_il_pezzo VEDE IL DOCUMENTO CHE HA APERTO IL BANCO ──
                //
                // Non «risponde qualcosa»: risponde di QUESTO pezzo. E' la
                // prova che il ponte e il banco stanno guardando la stessa
                // sessione di SOLIDWORKS, ed e' la premessa di tutto il resto.
                var testo = TestoDiRisposta(Mcp.Chiedi(p, 2, Chiamata(2, "guarda_il_pezzo", "{}")));
                Segna("porta: guarda_il_pezzo nomina il documento del banco",
                      testo.IndexOf(titolo, StringComparison.OrdinalIgnoreCase) >= 0,
                      titolo + " — " + Corto(testo));

                // ── 2. UN PIANO CHE RIESCE, E IL BANCO RILEGGE IL PEZZO ──
                //
                // La risposta del ponte dice «riuscita». Il banco non si fida
                // della parola: apre lo stesso documento con la COM e misura
                // il volume. E' l'unica cosa che questo gruppo puo' fare e la
                // suite di contratto no.
                double volumePrima = VolumeDiProva(doc);
                var fatto = TestoDiRisposta(Mcp.Chiedi(p, 3, Chiamata(3, "disegna",
                    "{\"passi\":[{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":" +
                    "{\"lunghezza_mm\":120,\"larghezza_mm\":80,\"spessore_mm\":10}}]}")));
                double volumeDopo = VolumeDiProva(doc);

                Segna("porta: il piano riesce", fatto.IndexOf("\"riuscita\":true",
                          StringComparison.OrdinalIgnoreCase) >= 0 ||
                      fatto.IndexOf("\"riuscita\": true", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(fatto));

                // 120 x 80 x 10 = 96.000 mm3, e il pezzo partiva vuoto.
                Segna("porta: e il pezzo e' cambiato davvero, misurato dal banco",
                      Math.Abs(volumeDopo - volumePrima - 96000) < 1,
                      volumePrima.ToString("0.#") + " -> " + volumeDopo.ToString("0.#") +
                      " mm3 (atteso +96000)");

                // ── 3. ROSSO PER COSTRUZIONE: un'operazione di assieme su una parte ──
                var rifiuto = TestoDiRisposta(Mcp.Chiedi(p, 4, Chiamata(4, "disegna",
                    "{\"passi\":[{\"operazione\":\"inserisci_componente\",\"argomenti\":" +
                    "{\"percorso\":\"C:\\\\non\\\\esiste.SLDPRT\"}}]}")));

                // SOLO `riuscita: false`, e niente `||` di scorta. La prima
                // stesura accettava anche una risposta che contenesse la
                // parola «assieme» da qualche parte: sarebbe passata pure
                // con un piano RIUSCITO che la nominasse. *Un `||` messo per
                // sicurezza e' un `||` che un giorno regge da solo.*
                bool rifiutato = rifiuto.IndexOf("\"riuscita\":false",
                                     StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 rifiuto.IndexOf("\"riuscita\": false",
                                     StringComparison.OrdinalIgnoreCase) >= 0;
                Segna("porta: un'operazione di assieme su una PARTE e' rifiutata",
                      rifiutato, Corto(rifiuto));

                // E NON DEVE ESSERE RIUSCITA. Sembra la stessa cosa detta
                // due volte e non lo e': `riuscita` potrebbe mancare del
                // tutto — una risposta malformata, un errore di protocollo —
                // e allora la riga qui sopra sarebbe rossa per il motivo
                // giusto ma per la ragione sbagliata. Questa dice che la
                // parola «true» accanto a «riuscita» non c'e'.
                Segna("porta: e non e' riuscita per sbaglio",
                      rifiuto.IndexOf("\"riuscita\":true",
                          StringComparison.OrdinalIgnoreCase) < 0 &&
                      rifiuto.IndexOf("\"riuscita\": true",
                          StringComparison.OrdinalIgnoreCase) < 0,
                      Corto(rifiuto));

                // E IL RIFIUTO DEVE DIRE SU COSA SI STA LAVORANDO.
                //
                // «Questa operazione vuole un assieme» e' una frase fissa: la
                // stessa risposta arriverebbe con qualunque documento davanti,
                // compreso quello sbagliato. Nominare il documento e' cio' che
                // distingue un controllo che guarda da uno che recita.
                // IL TITOLO, NON LA PAROLA «parte». «parte» sta in qualunque
                // frase fissa sui tipi di documento, e accettarla vorrebbe
                // dire promuovere proprio la risposta che questa riga esiste
                // per bocciare.
                Segna("porta: e il rifiuto dice su cosa sta lavorando",
                      rifiuto.IndexOf(titolo, StringComparison.OrdinalIgnoreCase) >= 0,
                      "cerco «" + titolo + "» in: " + Corto(rifiuto));

                // ── LA TRAPPOLA: zero righe lette aspettando zero righe ──
                //
                // Una notifica non riceve risposta, e «non e' arrivato niente»
                // e' vero anche quando il ponte e' morto. Si manda la notifica
                // e SUBITO DOPO una domanda vera: se la prima riga che torna e'
                // la risposta a quella, la notifica non ha risposto E il ponte
                // e' vivo.
                Mcp.Scrivi(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                var dopoLaNotifica = Mcp.Chiedi(p, 5, Chiamata(5, "guarda_il_pezzo", "{}"));
                Segna("porta: il ponte e' ancora vivo dopo una notifica",
                      dopoLaNotifica != null,
                      dopoLaNotifica != null ? "" : "niente risposta alla domanda dopo la notifica");
            }
            catch (Exception ex)
            {
                ErroreDiBanco("la porta del cliente", Perche(ex));
            }
            finally
            {
                if (p != null) { Mcp.Chiudi(p); p.Dispose(); }
                Chiudi(doc);
            }
        }

        /// <summary>L'eseguibile del ponte, o `null`.</summary>
        private static string IlPonte()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var radice = Directory.GetParent(Directory.GetParent(bin).FullName).FullName;
                var p = Path.Combine(radice, "bin", "oce-solid-agent-mcp.exe");
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>Una riga `tools/call` bell'e pronta.</summary>
        private static string Chiamata(int id, string nome, string argomenti)
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":" +
                   "{\"name\":\"" + nome + "\",\"arguments\":" + argomenti + "}}";
        }

        /// <summary>
        /// Il testo di `content[0].text`, o la riga intera se non si riesce a
        /// scendere fin li'.
        ///
        /// SI TORNA LA RIGA INTERA E NON UNA STRINGA VUOTA: se la risposta ha
        /// una forma che non mi aspetto, voglio vederla nel dettaglio della
        /// prova rossa. Una stringa vuota direbbe «non c'era niente», che e'
        /// un'altra cosa.
        /// </summary>
        private static string TestoDiRisposta(string riga)
        {
            if (riga == null) return "(nessuna risposta)";
            var o = Mcp.Ogg(riga);
            var risultato = Mcp.Dentro(o, "result") as Dictionary<string, object>;
            var contenuto = Mcp.Dentro(risultato, "content") as object[];
            if (contenuto == null || contenuto.Length == 0) return riga;
            var t = Convert.ToString(Mcp.Dentro(contenuto[0], "text"));
            return string.IsNullOrEmpty(t) ? riga : t;
        }

        /// <summary>Il volume in mm3 del documento, o 0.</summary>
        private static double VolumeDiProva(IModelDoc2 doc)
        {
            try
            {
                var m = doc.Extension.CreateMassProperty();
                return m == null ? 0 : m.Volume * 1e9;
            }
            catch { return 0; }
        }
    }
}
