using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using static ProvaSaldatura.Attese;

namespace ProvaSaldatura
{
    /// <summary>
    /// LA REGRESSIONE SUI RIFERIMENTI (voce F8).
    ///
    /// ── IL RISULTATO E' UNA DI TRE COSE, NON DUE ──────────────────────────
    ///
    ///   * **REGGE** — la ricostruzione va, nessuna lavorazione in errore, e la
    ///     geometria a valle sta dove l'intento dichiara;
    ///   * **SI STACCA E LO DICE** — la ricostruzione fallisce, oppure una
    ///     lavorazione porta un codice d'errore. **Il progettista lo vede**, e
    ///     un difetto che si vede e' un problema, non una trappola;
    ///   * **SI STACCA IN SILENZIO** — ricostruzione a posto, nessun errore,
    ///     e la geometria a valle **e' finita da un'altra parte**.
    ///
    /// La terza riga e' l'unico numero che conta, ed e' quello che nessuno
    /// misura. Il progetto piu' avanzato del campo confronta due documenti
    /// interi a fine lavoro con tolleranza **1%**: su un pezzo da 200 mm sono
    /// 2 mm, cioe' non vede un foro che si e' spostato di uno spessore di
    /// parete. Qui il criterio e' **per feature e per posizione**.
    ///
    /// ── L'INTENTO, perche' senza non esiste risposta giusta ───────────────
    ///
    /// Un foro a 20 mm dal bordo, su una piastra che si allarga, deve restare a
    /// 20 mm dal bordo oppure restare dov'era? **Dipende da cosa voleva chi ha
    /// disegnato**, e finche' non lo si dichiara non c'e' niente da misurare.
    ///
    ///   * `ancorato_a_assoluto` — «il foro resta a x = 80 nel modello»;
    ///   * `ancorato_a_bordo` — «il foro resta a 20 mm dal bordo destro».
    ///
    /// **Oggi il sistema sa esprimere solo il primo:** `x_mm: 80` e' assoluto
    /// per costruzione. Quindi i casi `ancorato_a_bordo` **devono fallire
    /// adesso**, e non sono un difetto della suite: sono la misura di partenza
    /// di E6/E7. Dichiararli attesi-verdi sarebbe barare; dichiararli attesi-
    /// rossi sarebbe rendere permanente un limite. Si contano e basta.
    ///
    /// ── E LA RIGA DEI SILENZIOSI SI SCRIVE ANCHE SE VALE ZERO ─────────────
    ///
    /// Un conteggio che sparisce quando e' zero e' un conteggio che nessuno
    /// controlla.
    /// </summary>
    internal static partial class Program
    {
        internal enum Esito { Regge, SiStaccaELoDice, SiStaccaInSilenzio }

        internal sealed class EsitoCaso
        {
            public string Nome;
            public string Intento;
            public Esito Come;
            public string Perche;
        }

        internal static void IlGiroDellaRegressione()
        {
            Console.WriteLine();
            Console.WriteLine("=== la regressione sui riferimenti (F8) ===");

            var cartella = CartellaDellaRegressione();
            if (cartella == null) return;
            var file = Directory.GetFiles(cartella, "*.json")
                                .OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).ToList();
            if (file.Count == 0)
            {
                ErroreDiBanco("regressione: c'e' almeno un caso", "cartella vuota: " + cartella);
                return;
            }

            var esiti = new List<EsitoCaso>();
            foreach (var f in file) esiti.Add(UnCaso(f));

            int regge = esiti.Count(e => e.Come == Esito.Regge);
            int rumoroso = esiti.Count(e => e.Come == Esito.SiStaccaELoDice);
            int silenzioso = esiti.Count(e => e.Come == Esito.SiStaccaInSilenzio);

            Console.WriteLine();
            foreach (var e in esiti)
                Console.WriteLine("  " + Simbolo(e.Come) + "  " + e.Nome.PadRight(34) +
                                  e.Intento.PadRight(22) + Corto(e.Perche));

            ScriviLaRegressione(esiti, regge, rumoroso, silenzioso);

            // I TRE NUMERI, e sommano al numero dei casi. Si scrivono come
            // esiti del banco perche' il resoconto li prenda da li'.
            Segna("regressione: reggono", true, regge + " su " + esiti.Count);
            Segna("regressione: si staccano e lo dicono", true, rumoroso + " su " + esiti.Count);
            Segna("regressione: SI STACCANO IN SILENZIO", true, silenzioso + " su " + esiti.Count);
            Segna("regressione: i tre numeri sommano ai casi",
                  regge + rumoroso + silenzioso == esiti.Count,
                  regge + " + " + rumoroso + " + " + silenzioso + " = " + esiti.Count);
        }

        private static string Simbolo(Esito e)
        {
            switch (e)
            {
                case Esito.Regge: return "REGGE   ";
                case Esito.SiStaccaELoDice: return "LO DICE ";
                default: return "SILENZIO";
            }
        }

        private static string CartellaDellaRegressione()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var c = Path.Combine(banco.FullName, "casi", "regressione");
                if (!Directory.Exists(c)) { ErroreDiBanco("regressione: la cartella c'e'", c); return null; }
                return c;
            }
            catch (Exception ex) { ErroreDiBanco("regressione: trovo la cartella", Perche(ex)); return null; }
        }

        // ==================================================================
        // UN CASO: costruisci, modifica, guarda
        // ==================================================================
        private static EsitoCaso UnCaso(string percorso)
        {
            string nome = Path.GetFileNameWithoutExtension(percorso);
            var fuori = new EsitoCaso { Nome = nome, Intento = "?", Come = Esito.SiStaccaInSilenzio };

            Dictionary<string, object> caso;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                caso = s.DeserializeObject(File.ReadAllText(percorso)) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                ErroreDiBanco(nome, "JSON illeggibile: " + Perche(ex));
                fuori.Perche = "JSON illeggibile"; return fuori;
            }

            fuori.Intento = Testo(caso, "intento") ?? "?";
            if (fuori.Intento != "ancorato_a_assoluto" && fuori.Intento != "ancorato_a_bordo")
            {
                // SENZA L'INTENTO NON ESISTE RISPOSTA GIUSTA, e un caso che non
                // lo dichiara non e' un caso: e' un errore di banco.
                ErroreDiBanco(nome, "«intento» dev'essere «ancorato_a_assoluto» oppure " +
                                    "«ancorato_a_bordo»: senza, non esiste risposta giusta");
                fuori.Perche = "intento non dichiarato"; return fuori;
            }
            if (Testo(caso, "perche_intento") == null)
            {
                ErroreDiBanco(nome, "manca «perche_intento»: l'intento senza il motivo " +
                                    "si copia dal caso accanto");
                fuori.Perche = "perche_intento assente"; return fuori;
            }

            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();

                // ── COSTRUISCI ───────────────────────────────────────────
                var stato = LeggiStato(doc);
                foreach (var p in Elenco(caso, "costruisci") ?? new List<object>())
                {
                    var passo = p as Dictionary<string, object>;
                    if (passo == null) { ErroreDiBanco(nome, "un passo non e' un oggetto"); fuori.Perche = "passo storto"; return fuori; }
                    var argomenti = Risolvi(
                        passo.ContainsKey("argomenti") ? passo["argomenti"] : null, stato)
                        as Dictionary<string, object> ?? new Dictionary<string, object>();
                    string errore;
                    var misure = Esegui(doc, Testo(passo, "operazione"), argomenti, out errore, stato);
                    stato = LeggiStato(doc);
                    if (misure == null)
                    {
                        // IL PEZZO DI PARTENZA DEVE VENIRE. Se non viene, non
                        // si sta misurando la modifica: si sta misurando un
                        // caso scritto male.
                        ErroreDiBanco(nome, "la costruzione e' fallita: " + Corto(errore));
                        fuori.Perche = "costruzione fallita"; return fuori;
                    }
                }

                // ── QUELLO CHE NON DEVE CAMBIARE SI MISURA PRIMA ─────────
                //
                // Molti invarianti di questa suite sono «com'era»: il passo di
                // una ripetizione, la simmetria di uno specchio, il raggio di
                // quattro raccordi. Scriverli come numeri vorrebbe dire
                // calcolare a mano cio' che SOLIDWORKS ha gia' deciso — e
                // sbagliare quel conto trasformerebbe un caso in un falso
                // silenzio. Un'attesa con `come_prima` si confronta con la
                // misura di PRIMA della modifica.
                var prima = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var a in Elenco(caso, "dopo") ?? new List<object>())
                {
                    var attesa = a as Dictionary<string, object>;
                    if (attesa == null || !Vero(attesa, "come_prima")) continue;
                    string quale = Testo(attesa, "cosa");
                    if (quale == null) continue;
                    var qui0 = doc;
                    try { prima[quale] = Misura(quale, c => DalDocumento(c, qui0, stato), stato, null); }
                    catch (NonHoPotutoMisurare ex)
                    {
                        ErroreDiBanco(nome, "«" + quale + "» non si misura prima della modifica: " + ex.Message);
                        fuori.Perche = ex.Message; return fuori;
                    }
                }

                // ── MODIFICA: UNA SOLA QUOTA, A MONTE ────────────────────
                var modifica = (caso.ContainsKey("modifica") ? caso["modifica"] : null)
                               as Dictionary<string, object>;
                if (modifica == null)
                {
                    ErroreDiBanco(nome, "manca «modifica»"); fuori.Perche = "modifica assente"; return fuori;
                }
                var suoi = Risolvi(modifica, stato) as Dictionary<string, object>;
                string erroreMod;
                var fatta = Esegui(doc, "modifica_quota", suoi, out erroreMod, stato);

                // ── LE TRE STRADE ────────────────────────────────────────
                //
                // Prima si guarda se il pezzo lo DICE: una modifica rifiutata,
                // una ricostruzione che non va, una lavorazione in errore.
                bool ricostruita = false;
                try { ricostruita = doc.EditRebuild3(); } catch { }
                stato = LeggiStato(doc);
                int inErrore = LavorazioniInErroreDelloStato(stato);

                if (fatta == null)
                {
                    fuori.Come = Esito.SiStaccaELoDice;
                    fuori.Perche = "la modifica e' stata rifiutata: " + Corto(erroreMod);
                    return fuori;
                }
                if (!ricostruita || inErrore > 0)
                {
                    fuori.Come = Esito.SiStaccaELoDice;
                    fuori.Perche = !ricostruita
                        ? "la ricostruzione non e' andata"
                        : inErrore + " lavorazioni in errore";
                    return fuori;
                }

                // Ricostruzione pulita: adesso la geometria dice la verita'.
                var rotte = new List<string>();
                foreach (var a in Elenco(caso, "dopo") ?? new List<object>())
                {
                    var attesa = a as Dictionary<string, object>;
                    if (attesa == null) continue;
                    _attese++;
                    string dettaglio;
                    bool ok;
                    var qui = doc;

                    // `come_prima`: l'atteso e' la misura di prima, e si mette
                    // nell'attesa adesso. Cosi' il confronto lo fa lo stesso
                    // codice di sempre, e il messaggio esce nello stesso
                    // formato: «centri_x_mm [...] (atteso [...])».
                    if (Vero(attesa, "come_prima"))
                    {
                        object era;
                        if (!prima.TryGetValue(Testo(attesa, "cosa") ?? "", out era) || era == null)
                        {
                            ErroreDiBanco(nome, "«come_prima» ma la misura di prima non c'e'");
                            fuori.Perche = "come_prima senza la misura di prima"; return fuori;
                        }
                        attesa["uguale"] = era;
                    }

                    try { ok = Combacia(attesa, c => DalDocumento(c, qui, stato), stato, fatta, out dettaglio); }
                    catch (NonHoPotutoMisurare ex)
                    {
                        ErroreDiBanco(nome, ex.Message);
                        fuori.Perche = ex.Message; return fuori;
                    }
                    _atteseVerificate++;
                    if (!ok) rotte.Add(dettaglio);
                }

                if (rotte.Count == 0)
                {
                    fuori.Come = Esito.Regge;
                    fuori.Perche = "ricostruita pulita, la geometria e' dove l'intento dice";
                }
                else
                {
                    // RICOSTRUZIONE A POSTO, NESSUN ERRORE, E LA GEOMETRIA E'
                    // ALTROVE. E' il numero che conta.
                    fuori.Come = Esito.SiStaccaInSilenzio;
                    fuori.Perche = string.Join(" · ", rotte.Take(2));
                }
                return fuori;
            }
            catch (Exception ex)
            {
                ErroreDiBanco(nome, Perche(ex));
                fuori.Perche = Perche(ex);
                return fuori;
            }
            finally { Chiudi(doc); }
        }

        private static int LavorazioniInErroreDelloStato(Dictionary<string, object> stato)
        {
            object v;
            if (stato == null || !stato.TryGetValue("lavorazioni_in_errore", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        // ==================================================================
        // IL RESOCONTO
        // ==================================================================
        private static void ScriviLaRegressione(
            List<EsitoCaso> esiti, int regge, int rumoroso, int silenzioso)
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var cartella = Path.Combine(banco.FullName, "resoconto");
                Directory.CreateDirectory(cartella);
                var file = Path.Combine(cartella, "regressione.md");

                var t = new List<string>
                {
                    "# La regressione sui riferimenti (F8)",
                    "",
                    "Ogni caso costruisce un pezzo, cambia **una sola quota a monte**, e",
                    "guarda dove e' finita la geometria a valle. Il risultato e' una di TRE",
                    "cose, non due.",
                    "",
                    "| | casi su " + esiti.Count + " | |",
                    "|---|---|---|",
                    "| **reggono** | " + regge + " | ricostruzione pulita, geometria dove l'intento dice |",
                    "| **si staccano e lo dicono** | " + rumoroso + " | il progettista lo vede |",
                    "| **SI STACCANO IN SILENZIO** | " + silenzioso + " | ricostruzione a posto, nessun errore, e la geometria e' altrove |",
                    "",
                    "> La terza riga si scrive **anche quando vale zero**: un conteggio che",
                    "> sparisce quando e' zero e' un conteggio che nessuno controlla.",
                    "",
                    "I casi `ancorato_a_bordo` **devono fallire adesso**: oggi il sistema sa",
                    "esprimere solo l'ancoraggio assoluto — `x_mm: 80` lo e' per costruzione.",
                    "Non sono un difetto della suite, sono la misura di partenza di E6/E7.",
                    "",
                    "| caso | intento | esito | |",
                    "|---|---|---|---|",
                };
                foreach (var e in esiti)
                    t.Add("| `" + e.Nome + "` | " + e.Intento + " | " +
                          (e.Come == Esito.Regge ? "regge"
                           : e.Come == Esito.SiStaccaELoDice ? "lo dice" : "**silenzio**") +
                          " | " + (e.Perche ?? "").Replace("|", "/") + " |");

                t.Add("");
                t.Add("Per intento:");
                t.Add("");
                t.Add("| intento | casi | reggono |");
                t.Add("|---|---|---|");
                foreach (var g in esiti.GroupBy(e => e.Intento))
                    t.Add("| " + g.Key + " | " + g.Count() + " | " +
                          g.Count(e => e.Come == Esito.Regge) + " |");

                File.WriteAllText(file, string.Join("\r\n", t) + "\r\n",
                                  new System.Text.UTF8Encoding(false));
                Console.WriteLine("regressione: " + file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("non ho potuto scrivere regressione.md: " + ex.Message);
            }
        }
    }
}
