using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ProvaSaldatura
{
    /// <summary>
    /// I CASI CHE SONO UN FILE, NON UNA RIGA DI C# (voce F3).
    ///
    /// ── PERCHE' ESISTE ────────────────────────────────────────────────────
    ///
    /// E' la richiesta letterale del progetto — «i numeri attesi accanto a ogni
    /// passo» — ed e' quello che rende una prova leggibile a chi il C# non lo
    /// scrive. Un caso oggi e' un metodo: per sapere che cosa verifica bisogna
    /// leggere il codice che lo verifica, e chi disegna per mestiere non lo fa.
    ///
    /// ── LE TOLLERANZE SONO ASSOLUTE, E PORTANO IL PERCHE' ─────────────────
    ///
    /// In mm o mm³, mai in percentuale, e ogni tolleranza diversa da zero deve
    /// dire **perche' quella**. `Guscio()` usava `volumeAtteso * 0.02`: sui
    /// 240.000 mm³ della piastra sono **4.800 mm³, piu' di un foro M12
    /// passante**. Una prova che accetta un foro di scarto non prova il foro.
    ///
    /// Una tolleranza senza il suo `perche_questa_tolleranza` **e' un caso
    /// rotto**, non un caso permissivo: il file esce rosso e dice quale campo
    /// manca. Un campo assente non diventa mai un verde.
    ///
    /// ── I SELETTORI, E PERCHE' DEVONO RIFIUTARE ──────────────────────────
    ///
    /// Gli identificativi dello stato sono POSIZIONALI — le facce si rinumerano
    /// per area a ogni lettura — quindi un caso non puo' scrivere «F3». Scrive
    /// che cosa vuole (`@faccia(verso=davanti)`) e lo si risolve sullo stato
    /// del momento.
    ///
    /// **Un selettore che trova ZERO o PIU' DI UNA cosa fallisce, e non sceglie
    /// la prima.** E' il difetto silenzioso che questa voce nomina: un caso che
    /// gira su una faccia diversa da quella che il nome del file promette, e
    /// che magari torna lo stesso volume. L'unica eccezione e' `n=` di
    /// `@feature`, dove l'ambiguita' la scioglie chi scrive il caso.
    /// </summary>
    internal static partial class Program
    {
        private static void ProvaCasiDichiarati()
        {
            Console.WriteLine();
            Console.WriteLine("=== i casi dichiarati, che sono file (F3) ===");

            string cartella;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                cartella = Path.Combine(banco.FullName, "casi", "lavorazioni");
            }
            catch (Exception ex)
            {
                Segna("casi dichiarati: trovo la cartella", false, Perche(ex));
                return;
            }

            if (!Directory.Exists(cartella))
            {
                // NON «saltata»: la cartella dei casi che non c'e' e' un banco
                // che ha perso i suoi casi, e va visto.
                Segna("casi dichiarati: la cartella c'e'", false, cartella);
                return;
            }

            var file = Directory.GetFiles(cartella, "*.json").OrderBy(x => x).ToList();
            if (file.Count == 0)
            {
                Segna("casi dichiarati: ce n'e' almeno uno", false, "cartella vuota: " + cartella);
                return;
            }

            foreach (var f in file) CorriUnCaso(f);
        }

        // ==================================================================
        // I ROSSI DEL RUNNER
        //
        // I CASI SONO FILE, MA CHI LI LEGGE E' C#, e i suoi rifiuti si provano
        // qui: dentro un caso non si possono provare, perche' un selettore che
        // rifiuta fa diventare rosso il caso — ed e' giusto cosi'.
        //
        // Tutto quello che segue e' una funzione pura su un dizionario: non
        // apre SOLIDWORKS, non crea documenti, dura un millesimo di secondo.
        // ==================================================================
        private static void ProvaCheIlSelettoreRifiuta()
        {
            Console.WriteLine();
            Console.WriteLine("=== i selettori dei casi: rifiutano invece di indovinare (F3) ===");

            Func<string, string, Dictionary<string, object>> faccia =
                (uid, verso) => new Dictionary<string, object>
                    { ["uid"] = uid, ["verso"] = verso, ["area_mm2"] = 6000.0 };

            // ── UNA SOLA: si prende ────────────────────────────────────────
            var unaSola = new Dictionary<string, object>
            {
                ["facce"] = new List<object> { faccia("F1", "davanti"), faccia("F2", "dietro") },
            };
            Segna("F3: una faccia sola, si prende",
                  Prova(() => Seleziona("@faccia(verso=davanti)", unaSola)) == "F1", "");

            // ── ZERO: si rifiuta ───────────────────────────────────────────
            Segna("F3 ROSSO: nessuna faccia «alto», si rifiuta",
                  Perche(() => Seleziona("@faccia(verso=alto)", unaSola)).Contains("non l'ho trovata"),
                  Perche(() => Seleziona("@faccia(verso=alto)", unaSola)));

            // ── DUE: si rifiuta, NON si prende la prima ────────────────────
            //
            // E' il difetto silenzioso che la voce F3 nomina: un caso che gira
            // su una faccia diversa da quella che il nome del file promette, e
            // che magari torna lo stesso volume. Senza questo rosso, «il caso
            // e' verde» non vorrebbe dire «ha lavorato dove diceva».
            var dueUguali = new Dictionary<string, object>
            {
                ["facce"] = new List<object> { faccia("F1", "davanti"), faccia("F7", "davanti") },
            };
            Segna("F3 ROSSO: due facce «davanti», si rifiuta invece di indovinare",
                  Perche(() => Seleziona("@faccia(verso=davanti)", dueUguali)).Contains("ne ho trovate 2"),
                  Perche(() => Seleziona("@faccia(verso=davanti)", dueUguali)));

            // ── LO SPIGOLO, per asse e lunghezza ───────────────────────────
            var spigoli = new Dictionary<string, object>
            {
                ["spigoli"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "E1", ["lungo"] = "Z", ["lunghezza_mm"] = 40.0 },
                    new Dictionary<string, object> { ["uid"] = "E2", ["lungo"] = "Z", ["lunghezza_mm"] = 100.0 },
                    new Dictionary<string, object> { ["uid"] = "E3", ["lungo"] = "X", ["lunghezza_mm"] = 100.0 },
                },
            };
            Segna("F3: lo spigolo si sceglie per asse e lunghezza",
                  Prova(() => Seleziona("@spigolo(asse=Z,lungo~=100)", spigoli)) == "E2", "");
            Segna("F3 ROSSO: senza la lunghezza, due spigoli su Z sono ambigui",
                  Perche(() => Seleziona("@spigolo(asse=Z)", spigoli)).Contains("ne ho trovate 2"),
                  "");

            // ── LA FEATURE ENNESIMA: l'ordine lo decide chi scrive il caso ──
            var feature = new Dictionary<string, object>
            {
                ["feature"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "Foro1", ["tipo"] = "HoleWzd" },
                    new Dictionary<string, object> { ["uid"] = "Foro2", ["tipo"] = "HoleWzd" },
                    new Dictionary<string, object> { ["uid"] = "Estrusione1", ["tipo"] = "Extrusion" },
                },
            };
            Segna("F3: @feature(n=2) prende la seconda",
                  Prova(() => Seleziona("@feature(tipo=HoleWzd,n=2)", feature)) == "Foro2", "");
            Segna("F3 ROSSO: @feature(n=3) su due, si rifiuta",
                  Perche(() => Seleziona("@feature(tipo=HoleWzd,n=3)", feature)).Contains("ne ho trovate 2"),
                  "");

            // ── LA QUOTA: il titolo del documento lo mette il banco ─────────
            //
            // `piani/cambia_diametro.json` scrive «D1@Schizzo1@Parte76.Part», e
            // quel «Parte76» cambia a ogni giro: quel piano non si puo'
            // rieseguire. Il caso nomina la quota, il titolo lo risolve qui.
            var conQuote = new Dictionary<string, object>
            {
                ["feature"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["uid"] = "Estrusione1",
                        ["quote"] = new List<object>
                        {
                            new Dictionary<string, object> { ["nome"] = "D1@Schizzo1@Parte99.Part" },
                            new Dictionary<string, object> { ["nome"] = "D2@Schizzo1@Parte99.Part" },
                        },
                    },
                },
            };
            Segna("F3: @quota torna il nome col documento di ADESSO",
                  Prova(() => Seleziona("@quota(feature=Schizzo1,nome=D1)", conQuote))
                      == "D1@Schizzo1@Parte99.Part", "");

            // ── UNA TOLLERANZA SENZA IL PERCHE' E' UN CASO ROTTO ────────────
            //
            // Non un caso permissivo: rotto. Un campo assente non diventa mai
            // un verde.
            var senzaPerche = new Dictionary<string, object>
            {
                ["cosa"] = "volume_mm3", ["uguale"] = 100.0, ["tolleranza"] = 5.0,
            };
            string detto;
            string motivo = Perche(() => { string d; Combacia(senzaPerche, null, null, null, out d); return d; });
            Segna("F3 ROSSO: tolleranza senza «perche_questa_tolleranza»",
                  motivo.Contains("perche_questa_tolleranza"), Corto(motivo));

            // E con il perche', la stessa attesa si valuta (qui fallisce per il
            // valore, non per il formato: il punto e' che ci ARRIVA).
            var colPerche = new Dictionary<string, object>
            {
                ["cosa"] = "misure.x", ["uguale"] = 100.0, ["tolleranza"] = 5.0,
                ["perche_questa_tolleranza"] = "si misura con un righello",
            };
            var misureFinte = new Dictionary<string, object> { ["x"] = 102.0 };
            bool valutata = false;
            try { valutata = Combacia(colPerche, null, null, misureFinte, out detto); }
            catch (Exception ex) { detto = ex.Message; }
            Segna("F3: col perche', la tolleranza si applica", valutata, Corto(detto));
        }

        // ==================================================================
        // F4. un campo che non c'e' non e' uno zero, ed e' uscita 2
        // ==================================================================
        /// <summary>
        /// LA TRAPPOLA, NELLA SUA FORMA LETTERALE.
        ///
        /// Una prova che verificava un difetto smette di verificarlo quando il
        /// difetto viene chiuso altrove, e **resta verde**. Nel novanta per
        /// cento dei casi il modo in cui smette e' che il campo che leggeva e'
        /// stato rinominato: un dizionario che non trova la chiave torna
        /// `null`, e `null` confrontato con `null` passa.
        ///
        /// Qui un campo assente alza, il messaggio lo nomina **e nomina i campi
        /// che c'erano** — la stessa forma del messaggio sulle quote
        /// inesistenti — e chi chiama lo fa diventare un ERRORE DI BANCO, che
        /// non e' una prova rossa e porta il giro a **uscita 2**.
        /// </summary>
        private static void ProvaCheUnCampoAssenteNonEUnoZero()
        {
            Console.WriteLine();
            Console.WriteLine("=== un campo che non c'e' non e' uno zero (F4) ===");

            // Le misure che `applica_materiale` riporta davvero. Non c'e'
            // «materiale_tolto_mm3»: quell'operazione non toglie niente.
            var misure = new Dictionary<string, object>
            {
                ["materiale"] = "1.0037 (S235JR)",
                ["archivio"] = "solidworks materials",
                ["classe"] = "Acciaio",
            };

            string detto = Perche(() =>
            {
                string d;
                Combacia(new Dictionary<string, object>
                { ["cosa"] = "misure.materiale_tolto_mm3", ["uguale"] = 0.0 },
                         null, null, misure, out d);
                return d;
            });
            Segna("F4 ROSSO: campo assente, e dice quali c'erano",
                  detto.Contains("campo assente: misure.materiale_tolto_mm3") &&
                  detto.Contains("ci sono: archivio, classe, materiale"),
                  Corto(detto));

            // ── E UN NULL DICHIARATO NON E' UN CAMPO ASSENTE ───────────────
            //
            // Lo stato scrive `materiale: null` APPOSTA, per dire «quel posto
            // esiste ed e' vuoto»: e' l'unico modo perche' il modello si accorga
            // di doverlo riempire. Confonderlo con un campo che manca
            // toglierebbe di mezzo proprio quella distinzione.
            var statoSenzaMateriale = new Dictionary<string, object> { ["materiale"] = null };
            string d2;
            bool ok = false;
            try
            {
                ok = Combacia(new Dictionary<string, object>
                { ["cosa"] = "stato.materiale", ["uguale"] = "" },
                              null, statoSenzaMateriale, null, out d2);
            }
            catch (Exception ex) { d2 = ex.Message; }
            Segna("F4: «materiale: null» si misura, non e' un campo assente", ok, Corto(d2));

            // ── IL CONTO DELLE ATTESE ──────────────────────────────────────
            //
            // Tentate e verificate: se divergono, qualcosa sta misurando il
            // nulla. Qui si guarda solo che il conto ci sia e non sia fermo a
            // zero — il numero vero lo scrive il riepilogo del giro.
            Segna("F4: le attese si contano", _attese > 0 && _atteseVerificate > 0,
                  _atteseVerificate + " su " + _attese);
        }

        /// <summary>Il risultato, oppure null se ha alzato.</summary>
        private static string Prova(Func<string> f)
        {
            try { return f(); } catch { return null; }
        }

        /// <summary>Il messaggio dell'eccezione, oppure «non ha rifiutato».</summary>
        private static string Perche(Func<string> f)
        {
            try { f(); return "NON HA RIFIUTATO"; }
            catch (Exception ex) { return ex.Message; }
        }

        // ==================================================================
        // UN CASO
        // ==================================================================
        private static void CorriUnCaso(string percorso)
        {
            string nome = Path.GetFileNameWithoutExtension(percorso);
            Dictionary<string, object> caso;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                caso = s.DeserializeObject(File.ReadAllText(percorso)) as Dictionary<string, object>;
            }
            // UN CASO SCRITTO MALE E' UN ERRORE DI BANCO, NON UNA PROVA ROSSA
            // (voce F4): «una prova e' fallita» dice che si e' misurato
            // qualcosa, e qui non si e' misurato niente.
            catch (Exception ex) { ErroreDiBanco(nome, "JSON illeggibile: " + Perche(ex)); return; }
            if (caso == null) { ErroreDiBanco(nome, "il file non e' un oggetto JSON"); return; }

            // Il nome del caso e' quello DENTRO il file, e deve combaciare col
            // nome del file: due nomi diversi per la stessa cosa fanno cercare
            // nel posto sbagliato quando una prova diventa rossa.
            string dichiarato = Testo(caso, "nome");
            if (dichiarato == null) { ErroreDiBanco(nome, "manca «nome»"); return; }
            if (dichiarato != nome)
            {
                ErroreDiBanco(nome, "il file si chiama «" + nome + "» e dentro dice «" +
                                    dichiarato + "»");
                return;
            }
            if (Testo(caso, "perche") == null)
            {
                ErroreDiBanco(nome, "manca «perche»: un caso senza il dubbio che scioglie " +
                                    "non si sa piu' perche' esiste");
                return;
            }

            var passi = Elenco(caso, "passi");
            if (passi == null || passi.Count == 0) { ErroreDiBanco(nome, "manca «passi»"); return; }

            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                var stato = LeggiStato(doc);

                for (int i = 0; i < passi.Count; i++)
                {
                    var passo = passi[i] as Dictionary<string, object>;
                    if (passo == null) { ErroreDiBanco(nome, "il passo " + (i + 1) + " non e' un oggetto"); return; }
                    string etichetta = nome + " · passo " + (i + 1);

                    string operazione = Testo(passo, "operazione");
                    if (operazione == null) { ErroreDiBanco(etichetta, "manca «operazione»"); return; }

                    object grezziArg;
                    passo.TryGetValue("argomenti", out grezziArg);
                    Dictionary<string, object> argomenti;
                    // UN SELETTORE CHE NON RISOLVE NON E' UNA PROVA ROSSA:
                    // il caso non si e' potuto ESEGUIRE, e dirlo «fallito»
                    // farebbe credere che si sia misurato qualcosa (voce F4).
                    try { argomenti = Risolvi(grezziArg, stato) as Dictionary<string, object>; }
                    catch (NonHoPotutoMisurare ex) { ErroreDiBanco(etichetta, ex.Message); return; }
                    catch (Exception ex) { Segna(etichetta, false, ex.Message); return; }
                    if (argomenti == null) argomenti = new Dictionary<string, object>();

                    double volumePrima = VolumeDelDocumento(doc);
                    int featurePrima = QuanteFeature(stato);

                    string errore;
                    var misure = Esegui(doc, operazione, argomenti, out errore, stato);
                    stato = LeggiStato(doc);

                    var deveFallire = passo.ContainsKey("deve_fallire")
                        ? passo["deve_fallire"] as Dictionary<string, object> : null;

                    if (deveFallire != null)
                    {
                        if (!GuardaIlRifiuto(etichetta, deveFallire, misure, errore,
                                             doc, stato, volumePrima, featurePrima, percorso))
                            return;
                        continue;
                    }

                    if (misure == null) { Segna(etichetta, false, "fallito: " + Corto(errore)); return; }

                    var attese = Elenco(passo, "attese");
                    if (attese == null || attese.Count == 0)
                    {
                        // UN PASSO SENZA ATTESE E' UN PASSO CIECO. Passa e non
                        // misura niente, ed e' il modo piu' comodo di avere una
                        // prova verde che non prova. Si dice, e si dice che il
                        // caso e' scritto male, non che il pezzo e' sbagliato.
                        ErroreDiBanco(etichetta,
                              "nessuna attesa: un passo che non misura niente non e' una prova");
                        return;
                    }

                    foreach (var a in attese)
                    {
                        _attese++;
                        var attesa = a as Dictionary<string, object>;
                        if (attesa == null)
                        { ErroreDiBanco(etichetta, "un'attesa non e' un oggetto"); return; }
                        string dettaglio;
                        bool ok;
                        try { ok = Combacia(attesa, doc, stato, misure, out dettaglio); }
                        catch (NonHoPotutoMisurare ex)
                        { ErroreDiBanco(etichetta + " · " + Testo(attesa, "cosa"), ex.Message); return; }
                        catch (Exception ex) { Segna(etichetta, false, ex.Message); return; }
                        // VERIFICATA vuol dire «ho misurato e ho confrontato»,
                        // non «e' andata bene»: una rossa e' verificata eccome.
                        _atteseVerificate++;
                        Segna(etichetta + " · " + Testo(attesa, "cosa"), ok, dettaglio);
                        if (!ok) return;
                    }
                }
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // IL PASSO CHE DEVE FALLIRE
        // ==================================================================
        /// <summary>
        /// Un rifiuto non e' «non e' successo niente»: si guarda **che cosa ha
        /// detto** e **che il pezzo sia rimasto com'era**. Un'operazione che
        /// rifiuta dopo aver gia' tolto materiale e' peggio di una che riesce.
        /// </summary>
        private static bool GuardaIlRifiuto(
            string etichetta, Dictionary<string, object> regole,
            Dictionary<string, object> misure, string errore,
            IModelDoc2 doc, Dictionary<string, object> stato,
            double volumePrima, int featurePrima, string percorsoDelCaso)
        {
            if (misure != null)
            {
                Segna(etichetta, false, "doveva fallire e invece e' riuscito");
                return false;
            }

            var pezzi = Elenco(regole, "il_messaggio_contiene");
            if (pezzi == null || pezzi.Count == 0)
            {
                ErroreDiBanco(etichetta,
                      "«deve_fallire» senza «il_messaggio_contiene»: qualunque errore " +
                      "andrebbe bene, anche quello sbagliato");
                return false;
            }
            foreach (var p in pezzi)
            {
                string cerco = Convert.ToString(p);
                if ((errore ?? "").IndexOf(cerco, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Segna(etichetta, false, "ha rifiutato ma non dice «" + cerco + "»: " + Corto(errore));
                    return false;
                }
            }

            if (Vero(regole, "volume_invariato"))
            {
                double adesso = VolumeDelDocumento(doc);
                if (Math.Abs(adesso - volumePrima) > 0.001)
                {
                    Segna(etichetta, false, string.Format(CultureInfo.InvariantCulture,
                        "ha rifiutato ma il volume e' cambiato: {0:0.###} -> {1:0.###}",
                        volumePrima, adesso));
                    return false;
                }
            }
            if (Vero(regole, "nessuna_feature_in_piu"))
            {
                int adesso = QuanteFeature(stato);
                if (adesso > featurePrima)
                {
                    Segna(etichetta, false, "ha rifiutato ma ha lasciato " +
                                            (adesso - featurePrima) + " lavorazioni in piu'");
                    return false;
                }
            }

            // LA GEMELLA: il caso che fa la stessa cosa e RIESCE. Qui si guarda
            // solo che esista — imporla a tutti e' la voce F5 — perche' un
            // rimando a un file che non c'e' e' un rimando che nessuno seguira'.
            string gemella = Testo(regole, "gemella");
            if (gemella != null)
            {
                var accanto = Path.Combine(Path.GetDirectoryName(percorsoDelCaso), gemella + ".json");
                if (!File.Exists(accanto))
                {
                    // «gemella mancante» e' uno dei tre errori di banco che la
                    // voce F2 nominava per nome, insieme a «caso non ammesso» e
                    // «campo assente».
                    ErroreDiBanco(etichetta, "la gemella «" + gemella + "» non esiste");
                    return false;
                }
            }

            Segna(etichetta, true, "rifiutato: " + Corto(errore));
            return true;
        }

        // ==================================================================
        // LE ATTESE
        // ==================================================================
        private static bool Combacia(
            Dictionary<string, object> attesa, IModelDoc2 doc,
            Dictionary<string, object> stato, Dictionary<string, object> misure,
            out string dettaglio)
        {
            dettaglio = "";
            string cosa = Testo(attesa, "cosa");
            if (cosa == null) throw new NonHoPotutoMisurare("un'attesa senza «cosa»");
            if (!attesa.ContainsKey("uguale")) throw new NonHoPotutoMisurare("«" + cosa + "» senza «uguale»");

            double tolleranza = 0;
            if (attesa.ContainsKey("tolleranza") && attesa["tolleranza"] != null)
            {
                tolleranza = Convert.ToDouble(attesa["tolleranza"], CultureInfo.InvariantCulture);
                // IL PERCHE' E' OBBLIGATORIO, e non e' burocrazia: una
                // tolleranza senza motivo e' il posto in cui un giorno qualcuno
                // allarghera' il numero invece di guardare il pezzo.
                if (Math.Abs(tolleranza) > 1e-12 && Testo(attesa, "perche_questa_tolleranza") == null)
                    throw new NonHoPotutoMisurare("«" + cosa + "» ha tolleranza " +
                                        tolleranza.ToString(CultureInfo.InvariantCulture) +
                                        " e non dice «perche_questa_tolleranza»");
            }

            object misurato = Misura(cosa, doc, stato, misure);
            object atteso = attesa["uguale"];

            // Un elenco di numeri: l'ingombro, un punto, una terna.
            var attesoLista = atteso as System.Collections.IEnumerable;
            if (attesoLista != null && !(atteso is string))
            {
                var attesi = Numeri(attesoLista);
                var misurati = Numeri(misurato as System.Collections.IEnumerable);
                if (misurati == null)
                {
                    dettaglio = cosa + " non e' un elenco di numeri: " + CosaEra(misurato);
                    return false;
                }
                dettaglio = cosa + " " + Insieme(misurati) + " (atteso " + Insieme(attesi) +
                            (tolleranza > 0 ? " ±" + tolleranza.ToString(CultureInfo.InvariantCulture) : "") + ")";
                if (attesi.Length != misurati.Length) return false;
                for (int i = 0; i < attesi.Length; i++)
                    if (Math.Abs(attesi[i] - misurati[i]) > tolleranza) return false;
                return true;
            }

            // Un numero.
            double numeroAtteso;
            if (atteso != null && !(atteso is string) && !(atteso is bool) &&
                double.TryParse(Convert.ToString(atteso, CultureInfo.InvariantCulture),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out numeroAtteso))
            {
                double numeroMisurato;
                if (misurato == null ||
                    !double.TryParse(Convert.ToString(misurato, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out numeroMisurato))
                {
                    dettaglio = cosa + " non e' un numero: " + CosaEra(misurato);
                    return false;
                }
                dettaglio = string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:0.###} (atteso {2:0.###}{3})", cosa, numeroMisurato, numeroAtteso,
                    tolleranza > 0 ? " ±" + tolleranza.ToString(CultureInfo.InvariantCulture) : "");
                return Math.Abs(numeroMisurato - numeroAtteso) <= tolleranza;
            }

            // Tutto il resto si confronta come testo: «materiale», un booleano.
            string a = Convert.ToString(atteso, CultureInfo.InvariantCulture);
            string m = Convert.ToString(misurato, CultureInfo.InvariantCulture);
            dettaglio = cosa + " «" + m + "» (atteso «" + a + "»)";
            return string.Equals(a, m, StringComparison.Ordinal);
        }

        /// <summary>
        /// Da dove si legge un `cosa`. Tre sorgenti, e il prefisso lo dice:
        /// niente = quello che misura il banco per conto suo, `stato.` = lo
        /// stato dell'add-in, `misure.` = quello che l'operazione ha riportato.
        /// </summary>
        private static object Misura(
            string cosa, IModelDoc2 doc,
            Dictionary<string, object> stato, Dictionary<string, object> misure)
        {
            if (cosa == "volume_mm3") return VolumeDelDocumento(doc);
            if (cosa == "corpi") return ContaCorpi(doc);
            if (cosa == "ingombro_mm") return IngombroDelDocumento(doc);
            if (cosa.StartsWith("stato.", StringComparison.Ordinal))
                return Dentro(stato, cosa.Substring(6), cosa);
            if (cosa.StartsWith("misure.", StringComparison.Ordinal))
                return Dentro(misure, cosa.Substring(7), cosa);
            throw new NonHoPotutoMisurare(
                "non so misurare «" + cosa + "»: i nomi sono volume_mm3, " +
                "corpi, ingombro_mm, stato.… e misure.…");
        }

        /// <summary>
        /// Un percorso dentro un dizionario: `facce[0].area_mm2`.
        /// Null quando il percorso non porta da nessuna parte — e chi chiama lo
        /// fa diventare un rosso, perche' un campo assente non e' uno zero.
        /// </summary>
        private static object Dentro(object radice, string percorso, string nomeIntero)
        {
            object qui = radice;
            foreach (var pezzo in percorso.Split('.'))
            {
                string chiave = pezzo;
                int indice = -1;
                int quadra = pezzo.IndexOf('[');
                if (quadra >= 0 && pezzo.EndsWith("]", StringComparison.Ordinal))
                {
                    chiave = pezzo.Substring(0, quadra);
                    int.TryParse(pezzo.Substring(quadra + 1, pezzo.Length - quadra - 2), out indice);
                }

                var mappa = qui as Dictionary<string, object>;
                if (mappa == null)
                    throw new NonHoPotutoMisurare(
                        "campo assente: " + nomeIntero + "; «" + chiave +
                        "» andrebbe cercato dentro qualcosa che non e' un oggetto");

                // LA CHIAVE CHE NON C'E' E UN VALORE NULL NON SONO LA STESSA
                // COSA, ed e' tutta la voce F4. `materiale: null` vuol dire
                // «quel posto esiste ed e' vuoto»; una chiave che manca vuol
                // dire che il caso sta leggendo un campo che non esiste piu' —
                // e un `null` confrontato con un `null` passerebbe.
                if (!mappa.ContainsKey(chiave))
                    throw new NonHoPotutoMisurare(
                        "campo assente: " + nomeIntero + "; ci sono: " +
                        string.Join(", ", mappa.Keys.OrderBy(k => k, StringComparer.Ordinal)));
                qui = mappa[chiave];

                if (indice >= 0)
                {
                    var lista = qui as System.Collections.IList;
                    if (lista == null)
                        throw new NonHoPotutoMisurare(
                            "campo assente: " + nomeIntero + "; «" + chiave + "» non e' un elenco");
                    if (indice >= lista.Count)
                        throw new NonHoPotutoMisurare(
                            "campo assente: " + nomeIntero + "; «" + chiave + "» ha " +
                            lista.Count + " voci e il caso chiede la numero " + indice);
                    qui = lista[indice];
                }
            }
            return qui;
        }

        // ==================================================================
        // I SELETTORI
        // ==================================================================
        private static object Risolvi(object grezzo, Dictionary<string, object> stato)
        {
            var mappa = grezzo as Dictionary<string, object>;
            if (mappa != null)
            {
                var fuori = new Dictionary<string, object>();
                foreach (var v in mappa) fuori[v.Key] = Risolvi(v.Value, stato);
                return fuori;
            }
            var lista = grezzo as System.Collections.IList;
            if (lista != null)
            {
                var fuori = new List<object>();
                foreach (var v in lista) fuori.Add(Risolvi(v, stato));
                return fuori;
            }
            var testo = grezzo as string;
            if (testo != null && testo.StartsWith("@", StringComparison.Ordinal))
                return Seleziona(testo, stato);
            return grezzo;
        }

        private static string Seleziona(string espressione, Dictionary<string, object> stato)
        {
            int apre = espressione.IndexOf('(');
            if (apre < 0 || !espressione.EndsWith(")", StringComparison.Ordinal))
                throw new NonHoPotutoMisurare("selettore storto: «" + espressione + "»");
            string tipo = espressione.Substring(1, apre - 1);
            var condizioni = Condizioni(espressione.Substring(apre + 1, espressione.Length - apre - 2));

            switch (tipo)
            {
                case "faccia":  return UnoSolo(espressione, "facce", stato, condizioni, "uid", null);
                case "spigolo": return UnoSolo(espressione, "spigoli", stato, condizioni, "uid", null);
                case "feature": return UnaFeature(espressione, stato, condizioni);
                case "quota":   return UnaQuota(espressione, stato, condizioni);
                default:
                    throw new NonHoPotutoMisurare("selettore sconosciuto «@" + tipo + "»: ci sono " +
                                        "@faccia, @spigolo, @feature, @quota");
            }
        }

        /// <summary>
        /// `verso=davanti, lungo~=100`: il `~` vuol dire «circa», e serve per le
        /// lunghezze, che non sono mai intere quando il pezzo e' stato ricostruito.
        /// </summary>
        private sealed class Condizione
        {
            public string Campo;
            public string Valore;
            public bool Circa;
        }

        private static List<Condizione> Condizioni(string dentro)
        {
            var fuori = new List<Condizione>();
            foreach (var pezzo in dentro.Split(','))
            {
                var t = pezzo.Trim();
                if (t.Length == 0) continue;
                bool circa = t.Contains("~=");
                int uguale = circa ? t.IndexOf("~=", StringComparison.Ordinal) : t.IndexOf('=');
                if (uguale < 0) throw new NonHoPotutoMisurare("condizione senza «=»: «" + t + "»");
                fuori.Add(new Condizione
                {
                    Campo = t.Substring(0, uguale).Trim(),
                    Valore = t.Substring(uguale + (circa ? 2 : 1)).Trim(),
                    Circa = circa,
                });
            }
            return fuori;
        }

        // I NOMI DEL CASO E I NOMI DELLO STATO NON SONO GLI STESSI, e la
        // traduzione sta qui e in un punto solo. `@spigolo(asse=Z)` si legge
        // bene; il campo dello stato si chiama `lungo`, che da solo vorrebbe
        // dire un'altra cosa.
        private static string CampoDelloStato(string nomeNelCaso)
        {
            switch (nomeNelCaso)
            {
                case "asse":  return "lungo";
                case "lungo": return "lunghezza_mm";
                case "area":  return "area_mm2";
                default:      return nomeNelCaso;
            }
        }

        private static string UnoSolo(
            string espressione, string famiglia, Dictionary<string, object> stato,
            List<Condizione> condizioni, string campoDaTornare, int? ennesimo)
        {
            var voci = (stato != null && stato.ContainsKey(famiglia)
                ? stato[famiglia] : null) as System.Collections.IList;
            if (voci == null)
                throw new NonHoPotutoMisurare("lo stato non ha «" + famiglia + "»: " + espressione);

            var trovate = new List<Dictionary<string, object>>();
            foreach (var v in voci)
            {
                var voce = v as Dictionary<string, object>;
                if (voce == null) continue;
                bool va = true;
                foreach (var c in condizioni)
                {
                    if (c.Campo == "n") continue;
                    object valore;
                    if (!voce.TryGetValue(CampoDelloStato(c.Campo), out valore) || valore == null)
                    { va = false; break; }
                    if (c.Circa)
                    {
                        double a, b;
                        if (!double.TryParse(Convert.ToString(valore, CultureInfo.InvariantCulture),
                                             NumberStyles.Any, CultureInfo.InvariantCulture, out a) ||
                            !double.TryParse(c.Valore, NumberStyles.Any,
                                             CultureInfo.InvariantCulture, out b) ||
                            Math.Abs(a - b) > 0.5)
                        { va = false; break; }
                    }
                    else if (!string.Equals(
                        Convert.ToString(valore, CultureInfo.InvariantCulture), c.Valore,
                        StringComparison.OrdinalIgnoreCase))
                    { va = false; break; }
                }
                if (va) trovate.Add(voce);
            }

            if (ennesimo.HasValue)
            {
                if (ennesimo.Value < 1 || ennesimo.Value > trovate.Count)
                    throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                        ", e il caso chiede la numero " + ennesimo.Value);
                return Convert.ToString(trovate[ennesimo.Value - 1][campoDaTornare]);
            }

            // ZERO O PIU' D'UNA: SI RIFIUTA, non si sceglie la prima.
            // E' il difetto silenzioso che questa voce nomina: un caso che gira
            // su una faccia diversa da quella che il nome del file promette, e
            // che magari torna lo stesso volume.
            if (trovate.Count == 0)
                throw new NonHoPotutoMisurare(espressione + ": non l'ho trovata");
            if (trovate.Count > 1)
                throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                    ". Sceglierne una sarebbe indovinare: aggiungi una " +
                                    "condizione, oppure «n=» se l'ordine lo decidi tu");
            return Convert.ToString(trovate[0][campoDaTornare]);
        }

        private static string UnaFeature(
            string espressione, Dictionary<string, object> stato, List<Condizione> condizioni)
        {
            int? ennesimo = null;
            var n = condizioni.FirstOrDefault(c => c.Campo == "n");
            if (n != null)
            {
                int k;
                if (!int.TryParse(n.Valore, out k)) throw new NonHoPotutoMisurare(espressione + ": «n» non e' un numero");
                ennesimo = k;
            }
            return UnoSolo(espressione, "feature", stato, condizioni, "uid", ennesimo);
        }

        /// <summary>
        /// `@quota(feature=Schizzo1,nome=D1)` -> il nome ESTESO della quota, come
        /// lo scrive lo stato: `D1@Schizzo1@Parte286.Part`.
        ///
        /// E' il selettore che la voce chiamava «non un lusso»:
        /// `piani/cambia_diametro.json` scrive il nome col titolo del documento
        /// dentro, e quel `Parte76` cambia a ogni giro — **quel piano oggi non
        /// si puo' rieseguire.** Risolvendolo sullo stato, il caso nomina la
        /// quota e il titolo lo mette il banco.
        /// </summary>
        private static string UnaQuota(
            string espressione, Dictionary<string, object> stato, List<Condizione> condizioni)
        {
            string dentroA = condizioni.FirstOrDefault(c => c.Campo == "feature")?.Valore;
            string chiamata = condizioni.FirstOrDefault(c => c.Campo == "nome")?.Valore;
            if (chiamata == null) throw new NonHoPotutoMisurare(espressione + ": serve «nome»");

            var feature = (stato != null && stato.ContainsKey("feature")
                ? stato["feature"] : null) as System.Collections.IList;
            if (feature == null) throw new NonHoPotutoMisurare("lo stato non ha «feature»: " + espressione);

            var trovate = new List<string>();
            foreach (var f in feature)
            {
                var voce = f as Dictionary<string, object>;
                if (voce == null || !voce.ContainsKey("quote")) continue;
                var quote = voce["quote"] as System.Collections.IList;
                if (quote == null) continue;
                foreach (var q in quote)
                {
                    var quota = q as Dictionary<string, object>;
                    if (quota == null || !quota.ContainsKey("nome")) continue;
                    string esteso = Convert.ToString(quota["nome"]);
                    // «D1@Schizzo1@Parte286.Part» -> corto «D1», dentro «Schizzo1»
                    var pezzi = esteso.Split('@');
                    if (pezzi.Length < 2) continue;
                    if (!string.Equals(pezzi[0], chiamata, StringComparison.OrdinalIgnoreCase)) continue;
                    if (dentroA != null &&
                        !string.Equals(pezzi[1], dentroA, StringComparison.OrdinalIgnoreCase)) continue;
                    trovate.Add(esteso);
                }
            }

            if (trovate.Count == 0) throw new NonHoPotutoMisurare(espressione + ": non l'ho trovata");
            if (trovate.Count > 1)
                throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                    " (" + string.Join(", ", trovate) + ")");
            return trovate[0];
        }

        // ==================================================================
        // MINUTERIA
        // ==================================================================
        private static string Testo(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return null;
            var s = Convert.ToString(v);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private static bool Vero(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        private static List<object> Elenco(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return null;
            var lista = v as System.Collections.IList;
            if (lista == null) return null;
            var fuori = new List<object>();
            foreach (var x in lista) fuori.Add(x);
            return fuori;
        }

        private static double[] Numeri(System.Collections.IEnumerable lista)
        {
            if (lista == null) return null;
            var fuori = new List<double>();
            foreach (var x in lista)
            {
                double d;
                if (!double.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return null;
                fuori.Add(d);
            }
            return fuori.ToArray();
        }

        private static string Insieme(double[] n)
        {
            return "[" + string.Join(", ", n.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) + "]";
        }

        /// <summary>
        /// Che cos'e' quel valore, per un messaggio. Si chiama cosi' e non
        /// `Descrivi` perche' in `ProvaNuove.cs` un `Descrivi` c'e' gia': due
        /// metodi con lo stesso nome nella stessa classe parziale non
        /// compilano, ed e' la fine migliore possibile per un nome ripetuto.
        /// </summary>
        private static string CosaEra(object x)
        {
            if (x == null) return "(non c'e')";
            return Corto(Convert.ToString(x, CultureInfo.InvariantCulture));
        }

        private static int QuanteFeature(Dictionary<string, object> stato)
        {
            var f = (stato != null && stato.ContainsKey("feature")
                ? stato["feature"] : null) as System.Collections.IList;
            return f == null ? 0 : f.Count;
        }
    }
}
