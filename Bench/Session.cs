using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// LA GIUNTURA CHE NON ERA MAI STATA PROVATA.
    ///
    /// Questo progetto ha due banchi, e tutti e due misurano mezza cosa:
    ///   - `banco0300.py` prova il PIANO — cosa il modello propone, dato uno
    ///     stato del documento scritto a mano. Non disegna niente.
    ///   - `Oce.Solid.Agent.Bench` prova il DISEGNO — se un'operazione costruisce il
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
        private static int Session(string folder, string toOpen = null)
        {
            Directory.CreateDirectory(folder);
            Console.WriteLine("=== sessione: " + folder + " ===");

            IModelDoc2 doc = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(toOpen))
                {
                    doc = OpenDocument(toOpen);
                    if (doc == null)
                    {
                        Console.WriteLine("non riesco ad aprire " + toOpen);
                        return 1;
                    }
                    Console.WriteLine("  ripreso: " + toOpen);
                }
                else
                {
                    // Il controllo su null non c'e' piu': `NewPart()` non
                    // torna null, alza un'eccezione che DICE cos'e' mancato, e
                    // il `catch` qui sotto la stampa (voce F12). Un controllo
                    // irraggiungibile e' peggio di nessun controllo: si legge
                    // come una difesa e non difende da niente.
                    doc = NewPart();
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
                Front(doc);

                // Lo stato di partenza. Chi pianifica parte da qui,
                // esattamente come farebbe l'add-in vero.
                WriteState(folder, 0, Active() ?? doc, new List<object>());
                Console.WriteLine("  stato_0 scritto (documento vuoto)");

                for (int k = 1; k <= 40; k++)
                {
                    string request = Path.Combine(folder, "passo_" + k + ".json");
                    string fine = Path.Combine(folder, "fine.json");

                    // Si aspetta il piano. Mezz'ora: un giro del modello con il
                    // ragionamento acceso ne ha gia' presi nove da solo, e chi
                    // sta dall'altra parte spesso deve anche diagnosticare
                    // prima di mandare il passo dopo. Una sessione che scade
                    // mentre si guarda il difetto costringe a ricominciare il
                    // pezzo da capo.
                    var expiry = DateTime.UtcNow.AddMinutes(30);
                    while (!File.Exists(request) && !File.Exists(fine))
                    {
                        if (DateTime.UtcNow > expiry)
                        {
                            Console.WriteLine("  nessun passo_" + k + " entro mezz'ora: chiudo");
                            return 0;
                        }
                        Thread.Sleep(500);
                    }
                    if (File.Exists(fine)) { Console.WriteLine("  fine richiesta"); return 0; }

                    // Il file puo' essere ancora in scrittura quando lo vediamo.
                    Thread.Sleep(300);
                    var plane = ReadPlane(request);
                    Console.WriteLine("  passo " + k + ": " + plane.Count + " operazioni");

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
                    var stepState = ReadState(Active() ?? doc);

                    var outcomes = new List<object>();
                    foreach (var planStep in plane)
                    {
                        string itemName = Convert.ToString(planStep.Item1);
                        var arguments = planStep.Item2;
                        AttachSignatures(arguments, stepState);
                        string error;
                        Dictionary<string, object> measurements = null;
                        try
                        {
                            // IL DOCUMENTO ATTIVO PUO' CAMBIARE SOTTO I PIEDI.
                            //
                            // Finche' si facevano solo pezzi, il documento
                            // era uno e restava quello. `create_drawing` ne
                            // apre un secondo e lo mette davanti: da li' in
                            // poi lavorare sul primo vorrebbe dire mettere
                            // le viste dentro il pezzo.
                            var onWhich = Active() ?? doc;
                            measurements = Run(onWhich, itemName, arguments, out error);
                        }
                        catch (Exception ex) { error = ex.Message; }

                        bool ok = measurements != null;
                        Console.WriteLine("     " + (ok ? "OK  " : "NO  ") + itemName +
                                          (ok ? "" : "  -> " + Short(error)));
                        outcomes.Add(new Dictionary<string, object>
                        {
                            ["operation"] = itemName,
                            ["succeeded"] = ok,
                            ["error"] = ok ? null : error,
                            ["measurements"] = measurements,
                        });
                        // NON si va avanti dopo un fallimento: un piano e' una
                        // sequenza, e il passo dopo quasi sempre nomina cio' che
                        // il passo fallito avrebbe dovuto creare.
                        if (!ok) break;
                    }

                    WriteState(folder, k, Active() ?? doc, outcomes);
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
        private static void AttachSignatures(
            Dictionary<string, object> arguments, Dictionary<string, object> docState)
        {
            if (arguments == null || docState == null) return;
            var signatures = new Dictionary<string, object>();

            // I CORPI COME LE FACCE (voce Z-C21.2, rilievo L6-1). Il ponte manda
            // le firme dei corpi da tempo (`McpTools.StateSignatures`, «center_mm»
            // e «bounding_box_mm»); il banco no, e con «uid_dal_passo» un corpo
            // nominato prima di un taglio arrivava SENZA firma: `SignatureMatches`
            // non aveva niente da confrontare e diceva si'. La voce E4 — il corpo
            // rinumerato da un taglio — dal banco non si poteva vedere.
            var location = new[]
            {
                Tuple.Create("faces", new[] { "face_uid", "faces_uid" }),
                Tuple.Create("edges", new[] { "edges_uid", "edge_uid" }),
                Tuple.Create("bodies", new[]
                {
                    "bodies_uid", "body_uid", "main_body_uid", "tool_bodies_uid",
                    "bodies_to_trim_uid", "trim_boundary_uid",
                }),
            };
            foreach (var pair in location)
            {
                object raw;
                if (!docState.TryGetValue(pair.Item1, out raw)) continue;
                var list = raw as System.Collections.IEnumerable;
                if (list == null) continue;

                // GLI SPIGOLI DELL'ANCORA (voce E18) stanno un livello piu' giu',
                // in `anchor.x` e `anchor.y`: il ponte manda le firme di TUTTO lo
                // stato, e la libreria rifiuta un'ancora il cui spigolo non ha
                // firma in un piano che ne porta. Senza questo, il banco vedrebbe
                // un rifiuto che in produzione non c'e'.
                var sources = new List<object>();
                foreach (var theField in pair.Item2)
                {
                    object val;
                    if (arguments.TryGetValue(theField, out val) && val != null) sources.Add(val);
                }
                if (pair.Item1 == "edges") sources.AddRange(AnchorEdges(arguments));

                foreach (var val in sources)
                {
                    var wanted = new List<string>();
                    if (val is string) wanted.Add((string)val);
                    else
                    {
                        var itemList = val as System.Collections.IEnumerable;
                        if (itemList == null) continue;
                        foreach (var v in itemList) wanted.Add(Convert.ToString(v));
                    }

                    foreach (var uid in wanted)
                    {
                        foreach (var o in list)
                        {
                            var entry = o as Dictionary<string, object>;
                            if (entry == null) continue;
                            object u;
                            if (!entry.TryGetValue("uid", out u)) continue;
                            if (!string.Equals(Convert.ToString(u), uid,
                                    StringComparison.OrdinalIgnoreCase)) continue;
                            var signature = new Dictionary<string, object>();
                            foreach (var signatureField in
                                     new[] { "point_mm", "normal", "area_mm2", "length_mm",
                                             "center_mm", "bounding_box_mm" })
                            {
                                object x;
                                if (entry.TryGetValue(signatureField, out x) && x != null)
                                    signature[signatureField] = x;
                            }
                            // LA SCATOLA DEL PEZZO, come fa il ponte: senza, il
                            // banco leggerebbe messaggi di rifiuto piu' corti
                            // di quelli veri, ed e' esattamente la differenza
                            // che un banco non deve avere.
                            if (signature.Count > 0)
                            {
                                var box = PartBox(docState);
                                if (box != null) signature["box_part_mm"] = box;
                            }
                            if (signature.Count > 0) signatures[uid] = signature;
                            break;
                        }
                    }
                }
            }

            if (signatures.Count > 0) arguments["_signatures"] = signatures;
        }

        /// <summary>Gli uid degli spigoli nominati da `anchor` (edge_uid e centered_between, per asse).</summary>
        private static List<object> AnchorEdges(Dictionary<string, object> arguments)
        {
            var outside = new List<object>();
            object raw;
            var anchor = arguments.TryGetValue("anchor", out raw) ? raw as Dictionary<string, object> : null;
            if (anchor == null) return outside;
            foreach (var axis in anchor.Values)
            {
                var entry = axis as Dictionary<string, object>;
                if (entry == null) continue;
                object v;
                if (entry.TryGetValue("edge_uid", out v) && v != null) outside.Add(v);
                if (entry.TryGetValue("centered_between", out v) && v != null) outside.Add(v);
            }
            return outside;
        }

        /// <summary>
        /// {minX,minY,minZ,maxX,maxY,maxZ} in mm dai due campi dello stato:
        /// l'angolo e i lati. Null se uno dei due manca — l'angolo non si
        /// inventa (vedi il ponte, `McpTools.PartBox`).
        /// </summary>
        private static double[] PartBox(Dictionary<string, object> docState)
        {
            double[] da = ThreeNumbers(docState, "bounding_box_from_mm");
            double[] sides = ThreeNumbers(docState, "bounding_box_mm");
            if (da == null || sides == null) return null;
            return new[]
            {
                da[0], da[1], da[2],
                da[0] + sides[0], da[1] + sides[1], da[2] + sides[2],
            };
        }

        private static double[] ThreeNumbers(Dictionary<string, object> docState, string key)
        {
            object v;
            if (docState == null || !docState.TryGetValue(key, out v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try
                {
                    outside.Add(Convert.ToDouble(
                        x, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { return null; }
            }
            return outside.Count == 3 ? outside.ToArray() : null;
        }

        /// <summary>Apre un documento gia' salvato e lo mette davanti.</summary>
        private static IModelDoc2 OpenDocument(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                int typ = filePath.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase)
                    ? (int)swDocumentTypes_e.swDocDRAWING
                    : filePath.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)
                        ? (int)swDocumentTypes_e.swDocASSEMBLY
                        : (int)swDocumentTypes_e.swDocPART;
                int errors = 0, warnings = 0;
                var d = _sw.OpenDoc6(
                    filePath, typ,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "", ref errors, ref warnings) as IModelDoc2;
                return d;
            }
            catch { return null; }
        }

        /// <summary>Mette un documento davanti a tutti gli altri.</summary>
        private static void Front(IModelDoc2 doc)
        {
            try
            {
                var docTitle = doc?.GetTitle();
                if (string.IsNullOrWhiteSpace(docTitle)) return;
                int errors = 0;
                _sw.ActivateDoc3(docTitle, true,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            }
            catch { }
        }

        /// <summary>Il documento che SolidWorks ha davanti adesso.</summary>
        private static IModelDoc2 Active()
        {
            try { return _sw.IActiveDoc2; } catch { return null; }
        }

        /// <summary>Il piano: un elenco di {operazione, argomenti}.</summary>
        private static List<Tuple<string, Dictionary<string, object>>> ReadPlane(string file)
        {
            var outside = new List<Tuple<string, Dictionary<string, object>>>();
            var text = File.ReadAllText(file);
            var s = new System.Web.Script.Serialization.JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            var root = s.DeserializeObject(text);
            var list = root as object[];
            if (list == null)
            {
                var d = root as Dictionary<string, object>;
                object p;
                if (d != null && d.TryGetValue("steps", out p)) list = p as object[];
            }
            if (list == null) return outside;
            foreach (var entry in list)
            {
                var d = entry as Dictionary<string, object>;
                if (d == null) continue;
                object itemName, arg;
                d.TryGetValue("operation", out itemName);
                d.TryGetValue("arguments", out arg);
                outside.Add(Tuple.Create(
                    Convert.ToString(itemName),
                    (arg as Dictionary<string, object>) ?? new Dictionary<string, object>()));
            }
            return outside;
        }

        private static void WriteState(string folder, int k, IModelDoc2 doc, List<object> outcomes)
        {
            var docState = ReadState(doc);
            var outside = new Dictionary<string, object>
            {
                ["step"] = k,
                ["state"] = docState,
                ["outcomes"] = outcomes,
                ["bodies"] = CountBodies(doc),
                ["volume_mm3"] = Math.Round(DocumentVolume(doc), 1),
            };
            // Si scrive su un file temporaneo e poi si rinomina: chi legge
            // dall'altra parte non deve mai vedere mezzo JSON.
            string final = Path.Combine(folder, "stato_" + k + ".json");
            string temporary = final + ".parziale";
            File.WriteAllText(temporary, Json(outside));
            if (File.Exists(final)) File.Delete(final);
            File.Move(temporary, final);
        }
    }
}
