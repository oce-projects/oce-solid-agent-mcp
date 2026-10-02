using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ProvaSaldatura
{
    /// <summary>
    /// LA GIUNTURA CHE NON ERA MAI STATA PROVATA.
    ///
    /// Questo progetto ha due banchi, e tutti e due misurano mezza cosa:
    ///   - `banco0300.py` prova il PIANO — cosa il modello propone, dato uno
    ///     stato del documento scritto a mano. Non disegna niente.
    ///   - `ProvaSaldatura` prova il DISEGNO — se un'operazione costruisce il
    ///     pezzo giusto, dati gli argomenti. Non chiede niente al modello.
    ///
    /// In mezzo c'e' il giro vero: il modello guarda il pezzo, propone, si
    /// esegue, il pezzo CAMBIA, e il modello guarda di nuovo. E' li' che vive
    /// «fammi una piastra» seguito da «adesso falle quattro fori», ed e' li'
    /// che nessuno ha mai misurato niente.
    ///
    /// COME FUNZIONA. Questo modo tiene UN documento aperto e aspetta i piani
    /// su file. Chi pianifica (dall'altra parte, sul server) scrive
    /// `passo_1.json`; qui si esegue, si rilegge lo stato e si scrive
    /// `stato_1.json`; l'altro lato legge quello, chiede la cosa dopo al
    /// modello, e scrive `passo_2.json`.
    ///
    /// Il documento resta APERTO fra un passo e l'altro. E' l'unico modo per
    /// misurare quello che conta davvero: se il modello, guardando cio' che ha
    /// appena fatto, riesce a lavorarci sopra.
    /// </summary>
    internal static partial class Program
    {
        private static int Sessione(string cartella, string daAprire = null)
        {
            Directory.CreateDirectory(cartella);
            Console.WriteLine("=== sessione: " + cartella + " ===");

            IModelDoc2 doc = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(daAprire))
                {
                    doc = ApriDocumento(daAprire);
                    if (doc == null)
                    {
                        Console.WriteLine("non riesco ad aprire " + daAprire);
                        return 1;
                    }
                    Console.WriteLine("  ripreso: " + daAprire);
                }
                else
                {
                    // Il controllo su null non c'e' piu': `NuovaParte()` non
                    // torna null, alza un'eccezione che DICE cos'e' mancato, e
                    // il `catch` qui sotto la stampa (voce F12). Un controllo
                    // irraggiungibile e' peggio di nessun controllo: si legge
                    // come una difesa e non difende da niente.
                    doc = NuovaParte();
                }

                // IL DOCUMENTO DELLA SESSIONE VA MESSO DAVANTI.
                //
                // «Aperto» e «active» non sono la stessa cosa, e da quando
                // le operazioni lavorano sul documento ATTIVO la differenza
                // si paga: un disegno rimasto aperto da una sessione
                // precedente restava davanti, e il primo passo lavorava su
                // quello. Lo stato iniziale, che invece guardava il
                // documento giusto, diceva il contrario — due descrizioni
                // dello stesso istante che non combaciavano.
                Davanti(doc);

                // Lo stato di partenza. Chi pianifica parte da qui,
                // esattamente come farebbe l'add-in vero.
                ScriviStato(cartella, 0, Attivo() ?? doc, new List<object>());
                Console.WriteLine("  stato_0 scritto (documento vuoto)");

                for (int k = 1; k <= 40; k++)
                {
                    string richiesta = Path.Combine(cartella, "passo_" + k + ".json");
                    string fine = Path.Combine(cartella, "fine.json");

                    // Si aspetta il piano. Mezz'ora: un giro del modello con il
                    // ragionamento acceso ne ha gia' presi nove da solo, e chi
                    // sta dall'altra parte spesso deve anche diagnosticare
                    // prima di mandare il passo dopo. Una sessione che scade
                    // mentre si guarda il difetto costringe a ricominciare il
                    // pezzo da capo.
                    var scadenza = DateTime.UtcNow.AddMinutes(30);
                    while (!File.Exists(richiesta) && !File.Exists(fine))
                    {
                        if (DateTime.UtcNow > scadenza)
                        {
                            Console.WriteLine("  nessun passo_" + k + " entro mezz'ora: chiudo");
                            return 0;
                        }
                        Thread.Sleep(500);
                    }
                    if (File.Exists(fine)) { Console.WriteLine("  fine richiesta"); return 0; }

                    // Il file puo' essere ancora in scrittura quando lo vediamo.
                    Thread.Sleep(300);
                    var piano = LeggiPiano(richiesta);
                    Console.WriteLine("  passo " + k + ": " + piano.Count + " operazioni");

                    // LE FIRME LE ATTACCA IL BANCO, COME FA IL SERVER.
                    //
                    // Gli uid sono POSIZIONALI: le facce si rinumerano a ogni
                    // lettura, in ordine di area. Basta un taglio perche'
                    // «F13» non sia piu' la faccia che era un momento prima.
                    // Misurato il 10 settembre 2026 sulle asole di fissaggio
                    // del contenitore: quattro tagli uguali su quattro alette
                    // uguali, il primo giusto e il secondo sulla faccia
                    // sbagliata.
                    //
                    // Il primo tentativo fu di congelare lo stato per tutto
                    // il passo. Non bastava: il puntatore alla faccia scade
                    // comunque appena il modello cambia, e la seconda asola
                    // falliva lo stesso.
                    //
                    // La difesa vera e' quella che gira in produzione: il
                    // piano porta con se' la FIRMA di ogni entita' che nomina
                    // — un punto sulla faccia e la sua normale — e chi esegue
                    // ci ritrova la faccia anche se ha cambiato numero. Qui i
                    // piani si scrivono a mano e la firma non c'e': la mette
                    // il banco, adesso, leggendola dallo stato di inizio passo.
                    // Senza, il banco proverebbe una strada che in produzione
                    // non percorre nessuno.
                    var statoDelPasso = LeggiStato(Attivo() ?? doc);

                    var esiti = new List<object>();
                    foreach (var passo in piano)
                    {
                        string nome = Convert.ToString(passo.Item1);
                        var argomenti = passo.Item2;
                        AttaccaFirme(argomenti, statoDelPasso);
                        string errore;
                        Dictionary<string, object> misure = null;
                        try
                        {
                            // IL DOCUMENTO ATTIVO PUO' CAMBIARE SOTTO I PIEDI.
                            //
                            // Finche' si facevano solo pezzi, il documento
                            // era uno e restava quello. `create_drawing` ne
                            // apre un secondo e lo mette davanti: da li' in
                            // poi lavorare sul primo vorrebbe dire mettere
                            // le viste dentro il pezzo.
                            var suCui = Attivo() ?? doc;
                            misure = Esegui(suCui, nome, argomenti, out errore);
                        }
                        catch (Exception ex) { errore = ex.Message; }

                        bool ok = misure != null;
                        Console.WriteLine("     " + (ok ? "OK  " : "NO  ") + nome +
                                          (ok ? "" : "  -> " + Corto(errore)));
                        esiti.Add(new Dictionary<string, object>
                        {
                            ["operation"] = nome,
                            ["succeeded"] = ok,
                            ["error"] = ok ? null : errore,
                            ["measurements"] = misure,
                        });
                        // NON si va avanti dopo un fallimento: un piano e' una
                        // sequenza, e il passo dopo quasi sempre nomina cio' che
                        // il passo fallito avrebbe dovuto creare.
                        if (!ok) break;
                    }

                    ScriviStato(cartella, k, Attivo() ?? doc, esiti);
                    Console.WriteLine("  stato_" + k + " scritto");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("sessione interrotta: " + ex.Message);
                return 1;
            }
            finally
            {
                // Il pezzo NON si chiude: chi guarda vuole vederlo.
                Console.WriteLine("=== il documento resta aperto ===");
            }
        }

        /// <summary>
        /// Mette in `_signatures` la descrizione delle entita' che il piano nomina.
        ///
        /// E' quello che il server fa per ogni piano vero prima di mandarlo:
        /// senza, chi esegue non ha modo di accorgersi che una faccia si e'
        /// spostata, ne' di ritrovarla.
        /// </summary>
        private static void AttaccaFirme(
            Dictionary<string, object> argomenti, Dictionary<string, object> stato)
        {
            if (argomenti == null || stato == null) return;
            var firme = new Dictionary<string, object>();

            // I CORPI COME LE FACCE (voce Z-C21.2, rilievo L6-1). Il ponte manda
            // le firme dei corpi da tempo (`Attrezzi.FirmeDelloStato`, «center_mm»
            // e «bounding_box_mm»); il banco no, e con «uid_dal_passo» un corpo
            // nominato prima di un taglio arrivava SENZA firma: `FirmaCombacia`
            // non aveva niente da confrontare e diceva si'. La voce E4 — il corpo
            // rinumerato da un taglio — dal banco non si poteva vedere.
            var dove = new[]
            {
                Tuple.Create("faces", new[] { "face_uid", "faces_uid" }),
                Tuple.Create("edges", new[] { "edges_uid", "edge_uid" }),
                Tuple.Create("bodies", new[]
                {
                    "bodies_uid", "body_uid", "main_body_uid", "tool_bodies_uid",
                    "bodies_to_trim_uid", "trim_boundary_uid",
                }),
            };
            foreach (var coppia in dove)
            {
                object grezzo;
                if (!stato.TryGetValue(coppia.Item1, out grezzo)) continue;
                var elenco = grezzo as System.Collections.IEnumerable;
                if (elenco == null) continue;

                // GLI SPIGOLI DELL'ANCORA (voce E18) stanno un livello piu' giu',
                // in `anchor.x` e `anchor.y`: il ponte manda le firme di TUTTO lo
                // stato, e la libreria rifiuta un'ancora il cui spigolo non ha
                // firma in un piano che ne porta. Senza questo, il banco vedrebbe
                // un rifiuto che in produzione non c'e'.
                var sorgenti = new List<object>();
                foreach (var campo in coppia.Item2)
                {
                    object valore;
                    if (argomenti.TryGetValue(campo, out valore) && valore != null) sorgenti.Add(valore);
                }
                if (coppia.Item1 == "edges") sorgenti.AddRange(SpigoliDellAncora(argomenti));

                foreach (var valore in sorgenti)
                {
                    var voluti = new List<string>();
                    if (valore is string) voluti.Add((string)valore);
                    else
                    {
                        var lista = valore as System.Collections.IEnumerable;
                        if (lista == null) continue;
                        foreach (var v in lista) voluti.Add(Convert.ToString(v));
                    }

                    foreach (var uid in voluti)
                    {
                        foreach (var o in elenco)
                        {
                            var voce = o as Dictionary<string, object>;
                            if (voce == null) continue;
                            object u;
                            if (!voce.TryGetValue("uid", out u)) continue;
                            if (!string.Equals(Convert.ToString(u), uid,
                                    StringComparison.OrdinalIgnoreCase)) continue;
                            var firma = new Dictionary<string, object>();
                            foreach (var campoFirma in
                                     new[] { "point_mm", "normal", "area_mm2", "length_mm",
                                             "center_mm", "bounding_box_mm" })
                            {
                                object x;
                                if (voce.TryGetValue(campoFirma, out x) && x != null)
                                    firma[campoFirma] = x;
                            }
                            // LA SCATOLA DEL PEZZO, come fa il ponte: senza, il
                            // banco leggerebbe messaggi di rifiuto piu' corti
                            // di quelli veri, ed e' esattamente la differenza
                            // che un banco non deve avere.
                            if (firma.Count > 0)
                            {
                                var scatola = ScatolaDelPezzo(stato);
                                if (scatola != null) firma["box_part_mm"] = scatola;
                            }
                            if (firma.Count > 0) firme[uid] = firma;
                            break;
                        }
                    }
                }
            }

            if (firme.Count > 0) argomenti["_signatures"] = firme;
        }

        /// <summary>Gli uid degli spigoli nominati da `anchor` (edge_uid e centered_between, per asse).</summary>
        private static List<object> SpigoliDellAncora(Dictionary<string, object> argomenti)
        {
            var fuori = new List<object>();
            object grezza;
            var ancora = argomenti.TryGetValue("anchor", out grezza) ? grezza as Dictionary<string, object> : null;
            if (ancora == null) return fuori;
            foreach (var asse in ancora.Values)
            {
                var voce = asse as Dictionary<string, object>;
                if (voce == null) continue;
                object v;
                if (voce.TryGetValue("edge_uid", out v) && v != null) fuori.Add(v);
                if (voce.TryGetValue("centered_between", out v) && v != null) fuori.Add(v);
            }
            return fuori;
        }

        /// <summary>
        /// {minX,minY,minZ,maxX,maxY,maxZ} in mm dai due campi dello stato:
        /// l'angolo e i lati. Null se uno dei due manca — l'angolo non si
        /// inventa (vedi il ponte, `Attrezzi.ScatolaDelPezzo`).
        /// </summary>
        private static double[] ScatolaDelPezzo(Dictionary<string, object> stato)
        {
            double[] da = TreNumeri(stato, "bounding_box_from_mm");
            double[] lati = TreNumeri(stato, "bounding_box_mm");
            if (da == null || lati == null) return null;
            return new[]
            {
                da[0], da[1], da[2],
                da[0] + lati[0], da[1] + lati[1], da[2] + lati[2],
            };
        }

        private static double[] TreNumeri(Dictionary<string, object> stato, string chiave)
        {
            object v;
            if (stato == null || !stato.TryGetValue(chiave, out v) || v == null) return null;
            var elenco = v as System.Collections.IEnumerable;
            if (elenco == null) return null;
            var fuori = new List<double>();
            foreach (var x in elenco)
            {
                try
                {
                    fuori.Add(Convert.ToDouble(
                        x, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { return null; }
            }
            return fuori.Count == 3 ? fuori.ToArray() : null;
        }

        /// <summary>Apre un documento gia' salvato e lo mette davanti.</summary>
        private static IModelDoc2 ApriDocumento(string percorso)
        {
            try
            {
                if (!File.Exists(percorso)) return null;
                int tipo = percorso.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase)
                    ? (int)swDocumentTypes_e.swDocDRAWING
                    : percorso.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)
                        ? (int)swDocumentTypes_e.swDocASSEMBLY
                        : (int)swDocumentTypes_e.swDocPART;
                int errori = 0, avvisi = 0;
                var d = _sw.OpenDoc6(
                    percorso, tipo,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "", ref errori, ref avvisi) as IModelDoc2;
                return d;
            }
            catch { return null; }
        }

        /// <summary>Mette un documento davanti a tutti gli altri.</summary>
        private static void Davanti(IModelDoc2 doc)
        {
            try
            {
                var titolo = doc?.GetTitle();
                if (string.IsNullOrWhiteSpace(titolo)) return;
                int errori = 0;
                _sw.ActivateDoc3(titolo, true,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errori);
            }
            catch { }
        }

        /// <summary>Il documento che SolidWorks ha davanti adesso.</summary>
        private static IModelDoc2 Attivo()
        {
            try { return _sw.IActiveDoc2; } catch { return null; }
        }

        /// <summary>Il piano: un elenco di {operazione, argomenti}.</summary>
        private static List<Tuple<string, Dictionary<string, object>>> LeggiPiano(string file)
        {
            var fuori = new List<Tuple<string, Dictionary<string, object>>>();
            var testo = File.ReadAllText(file);
            var s = new System.Web.Script.Serialization.JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            var radice = s.DeserializeObject(testo);
            var elenco = radice as object[];
            if (elenco == null)
            {
                var d = radice as Dictionary<string, object>;
                object p;
                if (d != null && d.TryGetValue("steps", out p)) elenco = p as object[];
            }
            if (elenco == null) return fuori;
            foreach (var voce in elenco)
            {
                var d = voce as Dictionary<string, object>;
                if (d == null) continue;
                object nome, arg;
                d.TryGetValue("operation", out nome);
                d.TryGetValue("arguments", out arg);
                fuori.Add(Tuple.Create(
                    Convert.ToString(nome),
                    (arg as Dictionary<string, object>) ?? new Dictionary<string, object>()));
            }
            return fuori;
        }

        private static void ScriviStato(string cartella, int k, IModelDoc2 doc, List<object> esiti)
        {
            var stato = LeggiStato(doc);
            var fuori = new Dictionary<string, object>
            {
                ["step"] = k,
                ["state"] = stato,
                ["outcomes"] = esiti,
                ["bodies"] = ContaCorpi(doc),
                ["volume_mm3"] = Math.Round(VolumeDelDocumento(doc), 1),
            };
            // Si scrive su un file temporaneo e poi si rinomina: chi legge
            // dall'altra parte non deve mai vedere mezzo JSON.
            string finale = Path.Combine(cartella, "stato_" + k + ".json");
            string temporaneo = finale + ".parziale";
            File.WriteAllText(temporaneo, Json(fuori));
            if (File.Exists(finale)) File.Delete(finale);
            File.Move(temporaneo, finale);
        }
    }
}
