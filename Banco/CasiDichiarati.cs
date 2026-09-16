using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
// IL VALUTATORE DELLE ATTESE E I SELETTORI STANNO IN `Attese.cs`, che e'
// compilato anche da `Prove.Contratti` — il progetto che gira su una macchina
// SENZA SOLIDWORKS (voce F7). `using static` li lascia scrivere come prima:
// spostarli non doveva costare la rilettura di duecento righe di chiamate.
using static ProvaSaldatura.Attese;

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
        /// <summary>
        /// LE CARTELLE DEI CASI SI NOMINANO, non si scoprono.
        ///
        /// In `casi/` vivono anche i piani del formato vecchio, che
        /// `corri_piani.js` esegue e che qui sarebbero tutti «il file non e' un
        /// oggetto JSON»: scoprire le cartelle da sole vorrebbe dire
        /// trasformare un file d'altri in un errore di banco a ogni giro.
        ///
        /// Una famiglia nuova si aggiunge qui, e chi la dimentica lo scopre
        /// subito: i suoi casi non girano e il conto delle attese non sale.
        /// </summary>
        private static readonly string[] Famiglie =
        {
            "lavorazioni", "coppie", "asola", "smusso", "rivoluzione", "piano",
            "specchia", "ripeti", "asse", "togliere", "frase", "verifica",
            "esporta", "misura", "corpi", "proprieta", "testo",
        };

        private static void ProvaCasiDichiarati()
        {
            Console.WriteLine();
            Console.WriteLine("=== i casi dichiarati, che sono file (F3) ===");

            string radiceCasi;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                radiceCasi = Path.Combine(banco.FullName, "casi");
            }
            catch (Exception ex)
            {
                ErroreDiBanco("casi dichiarati: trovo la cartella", Perche(ex));
                return;
            }

            var file = new List<string>();
            foreach (var famiglia in Famiglie)
            {
                var cartella = Path.Combine(radiceCasi, famiglia);
                if (!Directory.Exists(cartella))
                {
                    ErroreDiBanco("casi dichiarati: la cartella «" + famiglia + "» c'e'", cartella);
                    return;
                }
                file.AddRange(Directory.GetFiles(cartella, "*.json"));
            }
            file = file.OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).ToList();
            if (file.Count == 0)
            {
                ErroreDiBanco("casi dichiarati: ce n'e' almeno uno", "nessun file in " + radiceCasi);
                return;
            }

            // ── DUE GIRI, E IL SECONDO E' LA VOCE F5 ──────────────────────
            //
            // Prima si eseguono tutti i casi e si tiene da parte com'e' andata;
            // poi si guardano le gemelle. Non si puo' fare in un giro solo,
            // perche' «la gemella e' verde IN QUESTO GIRO» e' una domanda a cui
            // si puo' rispondere solo quando il giro e' finito.
            var comeEAndata = new Dictionary<string, bool>(StringComparer.Ordinal);
            var daControllare = new List<Gemellaggio>();
            var doveSta = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var f in file)
            {
                doveSta[Path.GetFileNameWithoutExtension(f)] = f;
                comeEAndata[Path.GetFileNameWithoutExtension(f)] = CorriUnCaso(f, daControllare);
            }

            GuardaLeGemelle(daControllare, comeEAndata, doveSta);
        }

        /// <summary>
        /// Un rifiuto e la prova che lo accompagna (voce F5).
        /// </summary>
        private sealed class Gemellaggio
        {
            public string Etichetta;     // «lim_… · passo 2»
            public string Caso;          // chi la nomina
            public string Gemella;       // chi deve riuscire
            public string Operazione;    // il passo che ha rifiutato
            public Dictionary<string, object> Argomenti;   // com'erano scritti nel file
        }

        /// <summary>
        /// LE PROVE IN COPPIA, IMPOSTE DAL BANCO (voce F5).
        ///
        /// ── PERCHE' ───────────────────────────────────────────────────────
        ///
        /// **Una prova che verifica solo il rifiuto non distingue «il controllo
        /// funziona» da «il controllo rifiuta tutto».** Il caso 1 di 80u — il
        /// guscio da 30 su una scatola alta 52, che sembrava impossibile ed era
        /// solo brutto — dice cosa succede senza: si trasforma una
        /// disattenzione in una regola del prodotto.
        ///
        /// Tre cose, e tutte e tre sono errori di banco quando non tornano,
        /// perche' riguardano il caso e non il pezzo:
        ///
        ///   1. la gemella **e' nominata**;
        ///   2. la gemella **e' verde in questo giro** — che esista non basta:
        ///      una gemella rossa e' una coppia che non prova niente;
        ///   3. la gemella **ha qualcosa a che vedere** col rifiuto: stessa
        ///      operazione, argomenti non identici, e almeno un argomento in
        ///      comune con lo stesso valore.
        /// </summary>
        private static void GuardaLeGemelle(
            List<Gemellaggio> coppie, Dictionary<string, bool> comeEAndata,
            Dictionary<string, string> doveSta)
        {
            Console.WriteLine();
            Console.WriteLine("=== le gemelle: un rifiuto non sta mai da solo (F5) ===");

            if (coppie.Count == 0)
            {
                Segna("gemelle: ce n'e' almeno una da controllare", false,
                      "nessun caso con «deve_fallire»");
                return;
            }

            int verdi = 0;
            foreach (var c in coppie)
            {
                bool andata;
                if (!comeEAndata.TryGetValue(c.Gemella, out andata))
                {
                    ErroreDiBanco(c.Etichetta, "la gemella «" + c.Gemella + "» non e' fra i casi eseguiti");
                    continue;
                }
                if (!andata)
                {
                    // UNA GEMELLA ROSSA E' UNA COPPIA CHE NON PROVA NIENTE: il
                    // rifiuto resta senza il suo contraltare, e non si sa piu'
                    // se il controllo funziona o rifiuta tutto.
                    ErroreDiBanco(c.Etichetta, "la gemella «" + c.Gemella + "» non e' verde in questo giro");
                    continue;
                }

                string perche;
                if (!SiSomigliano(c, doveSta, out perche))
                {
                    ErroreDiBanco(c.Etichetta, "gemella finta: " + perche);
                    continue;
                }

                verdi++;
                Segna("gemella di «" + c.Caso + "»", true, c.Gemella + " — " + perche);
            }

            Segna("gemelle " + verdi + "/" + coppie.Count + " verdi",
                  verdi == coppie.Count, "");
        }

        /// <summary>
        /// La gemella ha a che vedere col rifiuto?
        ///
        /// ── LA REGOLA SCRITTA NELLA VOCE NON REGGE ALLE SUE STESSE COPPIE ──
        ///
        /// La voce F5 proponeva «la gemella deve differire per UN solo
        /// argomento». Misurata sulle quattro coppie canoniche di 80v, quella
        /// regola **ne rifiuta tre su quattro**: il foro fuori dal materiale
        /// differisce per due coordinate, l'asola per lunghezza e larghezza. E'
        /// una regola che avrebbe buttato via gli esempi che la voce stessa
        /// porta come canonici.
        ///
        /// Quella che gira dice la stessa cosa senza il numero fisso: **stessa
        /// operazione, argomenti non identici, e almeno un argomento in comune
        /// con lo stesso valore.** Quanti ne differiscono si DICE, cosi' chi
        /// legge vede una coppia lasca senza che il banco debba decidere al
        /// posto suo.
        /// </summary>
        private static bool SiSomigliano(
            Gemellaggio c, Dictionary<string, string> doveSta, out string perche)
        {
            perche = "";
            string percorso;
            if (!doveSta.TryGetValue(c.Gemella, out percorso))
            { perche = "non trovo il file della gemella"; return false; }

            Dictionary<string, object> gemella;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                gemella = s.DeserializeObject(File.ReadAllText(percorso)) as Dictionary<string, object>;
            }
            catch (Exception ex) { perche = "JSON della gemella illeggibile: " + ex.Message; return false; }

            var passi = Elenco(gemella, "passi");
            if (passi == null) { perche = "la gemella non ha passi"; return false; }

            Dictionary<string, object> suoi = null;
            foreach (var p in passi)
            {
                var passo = p as Dictionary<string, object>;
                if (passo == null) continue;
                if (!string.Equals(Testo(passo, "operazione"), c.Operazione, StringComparison.Ordinal)) continue;
                if (passo.ContainsKey("deve_fallire")) continue;   // due rifiuti non sono una coppia
                suoi = (passo.ContainsKey("argomenti") ? passo["argomenti"] : null)
                       as Dictionary<string, object>;
                break;
            }
            if (suoi == null)
            {
                perche = "la gemella non ha nessun passo «" + c.Operazione + "» che riesce";
                return false;
            }

            // IL CONFRONTO VERO STA IN `Attese`, che e' una funzione pura su
            // due dizionari e la prova `Prove.Contratti` senza CAD. Qui resta
            // solo il lavoro che il CAD lo tocca: trovare il file e leggerlo.
            // `Attese.` PER FORZA: il metodo qui accanto si chiama uguale, e
            // senza il prefisso il compilatore sceglie quello — cioe' se
            // stesso, all'infinito.
            return Attese.SiSomigliano(c.Argomenti, suoi, out perche);
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
        // --muta: OGNI CASO DEVE POTER DIVENTARE ROSSO (voce F6)
        // ==================================================================
        /// <summary>
        /// PERTURBA IL PIANO, NON IL CODICE.
        ///
        /// Per ogni caso, un argomento numerico alla volta moltiplicato per
        /// 1,5 (i booleani invertiti), si riesegue e si **pretende che il caso
        /// diventi ROSSO**. Un caso che resta verde sotto ogni perturbazione e'
        /// **cieco**: le sue attese non vincolano i suoi argomenti.
        ///
        /// ── PERCHE' QUESTO E NON UN CAMPO SCRITTO A MANO ──────────────────
        ///
        /// E' **l'unico modo meccanico** di imporre che una prova sia gia'
        /// fallita almeno una volta. Un `provata_a_fallire` scritto a mano e'
        /// una dichiarazione, e una dichiarazione si copia dal caso accanto;
        /// una mutazione no. E costa poco proprio qui, perche' il piano e'
        /// dato: mutare un JSON e' banale, mutare il C# di SOLIDWORKS no.
        ///
        /// ── IL FALSO CIECO, E COME SI EVITA ──────────────────────────────
        ///
        /// Mutare un argomento che l'operazione **ignora** — `profondita_mm`
        /// su un taglio passante — resta giustamente verde e non e' cecita'.
        /// Quelle mutazioni si saltano **leggendo il catalogo**: lo schema di
        /// `profondita_mm` dice «solo se non passante», e quella riga si legge
        /// invece di ricopiarla qui.
        ///
        /// ── E SI MUTANO SOLO I PASSI CON LE ATTESE ───────────────────────
        ///
        /// Un passo con `deve_fallire` non ha attese da vincolare: un foro da
        /// 500 mutato a 750 copre il pezzo lo stesso, e il caso resterebbe
        /// verde senza che questo dica niente sulle sue attese.
        /// </summary>
        /// <remarks>
        /// NON SI CHIAMA `Prova…`, E NON E' UN VEZZO. `ProvaCheLaTabellaCopreTutto`
        /// (voce F13) pretende che ogni metodo `Prova…()` stia nella tabella dei
        /// gruppi, e questo non e' un gruppo: e' **un altro giro**, che i gruppi
        /// li sostituisce. Chiamandolo `ProvaMutazioni` il controllo e' uscito
        /// rosso — giustamente — al primo giro intero dopo averlo scritto.
        /// </remarks>
        private static void IlGiroDelleMutazioni()
        {
            Console.WriteLine();
            Console.WriteLine("=== --muta: ogni caso deve poter diventare rosso (F6) ===");

            var file = FileDeiCasi();
            if (file == null) return;
            var schemi = SchemiDelCatalogo();

            var righe = new List<string>();
            int ciechi = 0;

            foreach (var percorso in file)
            {
                string nome = Path.GetFileNameWithoutExtension(percorso);
                Dictionary<string, object> caso;
                try
                {
                    var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                    caso = s.DeserializeObject(File.ReadAllText(percorso)) as Dictionary<string, object>;
                }
                catch (Exception ex) { ErroreDiBanco(nome, "JSON illeggibile: " + Perche(ex)); continue; }

                var mutazioni = Mutazioni(caso, schemi);
                if (mutazioni.Count == 0)
                {
                    ciechi++;
                    righe.Add("| `" + nome + "` | 0 | 0 | **NESSUNA MUTAZIONE POSSIBILE**: " +
                              "nessun argomento numerico o booleano in un passo con attese |");
                    Segna("muta: " + nome, false, "nessuna mutazione possibile");
                    continue;
                }

                int rosse = 0;
                var sopravvissute = new List<string>();
                foreach (var m in mutazioni)
                {
                    bool diventataRossa = ProvaUnaMutazione(m.Caso);
                    if (diventataRossa) rosse++;
                    else sopravvissute.Add(m.Come);
                }

                bool cieco = rosse == 0;
                if (cieco) ciechi++;
                righe.Add("| `" + nome + "` | " + mutazioni.Count + " | " + rosse + " | " +
                          (cieco ? "**CIECO**: le attese non vincolano gli argomenti"
                                 : (sopravvissute.Count == 0
                                        ? "tutte rosse"
                                        : "verdi lo stesso: " + string.Join(", ", sopravvissute))) +
                          " |");

                // UN CASO CON QUALCHE MUTAZIONE SOPRAVVISSUTA NON E' ROSSO: e'
                // un caso le cui attese lasciano liberi alcuni argomenti, e la
                // riga del resoconto lo dice per nome. Rosso e' solo il CIECO.
                Segna("muta: " + nome, !cieco,
                      mutazioni.Count + " mutazioni, " + rosse + " rosse" +
                      (sopravvissute.Count > 0 ? " — verdi: " + string.Join(", ", sopravvissute) : ""));
            }

            ScriviMutazioni(righe, ciechi);
            Segna("muta: casi ciechi", ciechi == 0, ciechi + " ciechi");
        }

        private sealed class Mutazione
        {
            public string Come;                           // «passo 1 · spessore_mm 40 -> 60»
            public Dictionary<string, object> Caso;       // il caso intero, gia' mutato
        }

        /// <summary>
        /// Tutte le mutazioni di un caso: un argomento alla volta, e il caso
        /// intero ricostruito attorno. Si lavora su una COPIA profonda, perche'
        /// due mutazioni che si sommano non provano piu' niente di preciso.
        /// </summary>
        private static List<Mutazione> Mutazioni(
            Dictionary<string, object> caso, Dictionary<string, Dictionary<string, string>> schemi)
        {
            var fuori = new List<Mutazione>();
            var passi = Elenco(caso, "passi");
            if (passi == null) return fuori;

            for (int i = 0; i < passi.Count; i++)
            {
                var passo = passi[i] as Dictionary<string, object>;
                if (passo == null) continue;
                if (passo.ContainsKey("deve_fallire")) continue;
                var attese = Elenco(passo, "attese");
                if (attese == null || attese.Count == 0) continue;

                var argomenti = (passo.ContainsKey("argomenti") ? passo["argomenti"] : null)
                                as Dictionary<string, object>;
                if (argomenti == null) continue;
                string operazione = Testo(passo, "operazione") ?? "";

                foreach (var chiave in argomenti.Keys.ToList())
                {
                    object valore = argomenti[chiave];
                    if (valore == null) continue;
                    if (valore is string) continue;          // selettori e nomi di piano
                    if (Ignorato(operazione, chiave, argomenti, schemi)) continue;

                    object mutato;
                    string come;
                    if (valore is bool)
                    {
                        bool b = (bool)valore;
                        mutato = !b;
                        come = "passo " + (i + 1) + " · " + chiave + " " + b + "->" + (!b);
                    }
                    else
                    {
                        double d;
                        if (!double.TryParse(Convert.ToString(valore, CultureInfo.InvariantCulture),
                                             NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                            continue;
                        // ZERO PER 1,5 FA ZERO: una mutazione che non muta
                        // nulla resterebbe verde e verrebbe letta come cecita'.
                        // Si sposta di un millimetro, che su una coordinata e'
                        // una mutazione vera.
                        double n = Math.Abs(d) < 1e-9 ? 1.0 : d * 1.5;
                        mutato = n;
                        come = "passo " + (i + 1) + " · " + chiave + " " +
                               d.ToString("0.##", CultureInfo.InvariantCulture) + "->" +
                               n.ToString("0.##", CultureInfo.InvariantCulture);
                    }

                    var copia = Copia(caso) as Dictionary<string, object>;
                    var suoiPassi = Elenco(copia, "passi");
                    var suoPasso = suoiPassi[i] as Dictionary<string, object>;
                    ((Dictionary<string, object>)suoPasso["argomenti"])[chiave] = mutato;
                    fuori.Add(new Mutazione { Come = come, Caso = copia });
                }
            }
            return fuori;
        }

        /// <summary>
        /// L'operazione ignora quell'argomento, qui e adesso? Si legge dal
        /// CATALOGO: una descrizione che comincia con «solo se non X» dice che
        /// il campo conta solo quando `X` e' falso, e «solo se X» il contrario.
        /// Mutare un campo ignorato lascia il caso verde, e quel verde
        /// sembrerebbe cecita'.
        /// </summary>
        private static bool Ignorato(
            string operazione, string chiave, Dictionary<string, object> argomenti,
            Dictionary<string, Dictionary<string, string>> schemi)
        {
            Dictionary<string, string> suoi;
            if (schemi == null || !schemi.TryGetValue(operazione, out suoi)) return false;
            string descrizione;
            if (!suoi.TryGetValue(chiave, out descrizione) || descrizione == null) return false;

            const string prefisso = "solo se ";
            if (!descrizione.StartsWith(prefisso, StringComparison.OrdinalIgnoreCase)) return false;
            string resto = descrizione.Substring(prefisso.Length).Trim();
            bool negata = resto.StartsWith("non ", StringComparison.OrdinalIgnoreCase);
            if (negata) resto = resto.Substring(4).Trim();
            // Solo un nome di campo: «solo se non passante». Se la condizione e'
            // una frase, non la si interpreta — meglio una mutazione in piu' che
            // una saltata per un'ipotesi sulla lingua.
            if (resto.Contains(" ")) return false;

            object v;
            bool acceso = argomenti.TryGetValue(resto, out v) && v != null &&
                          Convert.ToString(v, CultureInfo.InvariantCulture)
                                 .Equals("True", StringComparison.OrdinalIgnoreCase);
            return negata ? acceso : !acceso;
        }

        /// <summary>Esegue un caso mutato in silenzio. True se e' diventato rosso.</summary>
        private static bool ProvaUnaMutazione(Dictionary<string, object> caso)
        {
            IModelDoc2 doc = null;
            _zitto = true;
            _zittoRosso = false;
            try
            {
                doc = NuovaParte();
                EseguiIPassi(Testo(caso, "nome") ?? "?", caso, doc, null, null);
            }
            catch { _zittoRosso = true; }
            finally { Chiudi(doc); _zitto = false; }
            return _zittoRosso;
        }

        private static object Copia(object x)
        {
            var mappa = x as Dictionary<string, object>;
            if (mappa != null)
            {
                var fuori = new Dictionary<string, object>();
                foreach (var v in mappa) fuori[v.Key] = Copia(v.Value);
                return fuori;
            }
            var lista = x as System.Collections.IList;
            if (lista != null)
            {
                var fuori = new List<object>();
                foreach (var v in lista) fuori.Add(Copia(v));
                return fuori;
            }
            return x;
        }

        /// <summary>
        /// Gli schemi del catalogo: operazione -> campo -> descrizione.
        ///
        /// Si leggono dalla LIBRERIA CARICATA, non da `bin/catalogo.json`: quel
        /// file lo scrive `build.ps1`, e leggerlo vorrebbe dire descrivere una
        /// libreria con le parole di un'altra compilazione.
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> SchemiDelCatalogo()
        {
            try
            {
                var tOperazioni = _addin.GetType("Riscontro.Disegno.Catalogo.Operazioni", false);
                var tNodo = _addin.GetType("Riscontro.Disegno.Catalogo.Nodo", false);
                if (tOperazioni == null || tNodo == null) return null;
                var tutte = tOperazioni.GetField("Tutte",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    as System.Collections.IEnumerable;
                var json = tNodo.GetMethod("Json", BindingFlags.Public | BindingFlags.Static);
                if (tutte == null || json == null) return null;

                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                var fuori = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                foreach (var o in tutte)
                {
                    var tipo = o.GetType();
                    string nome = Convert.ToString(tipo.GetField("Nome")?.GetValue(o));
                    var schema = tipo.GetField("Schema")?.GetValue(o);
                    if (nome == null || schema == null) continue;
                    var testo = json.Invoke(null, new[] { schema }) as string;
                    if (testo == null) continue;
                    var albero = s.DeserializeObject(testo) as Dictionary<string, object>;
                    var proprieta = (albero != null && albero.ContainsKey("properties")
                        ? albero["properties"] : null) as Dictionary<string, object>;
                    if (proprieta == null) continue;

                    var campi = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var p in proprieta)
                    {
                        var regola = p.Value as Dictionary<string, object>;
                        if (regola != null && regola.ContainsKey("description"))
                            campi[p.Key] = Convert.ToString(regola["description"]);
                    }
                    fuori[nome] = campi;
                }
                return fuori;
            }
            catch { return null; }
        }

        private static void ScriviMutazioni(List<string> righe, int ciechi)
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var cartella = Path.Combine(banco.FullName, "resoconto");
                Directory.CreateDirectory(cartella);
                var file = Path.Combine(cartella, "mutazioni.md");

                var testo = new List<string>
                {
                    "# Mutazioni — ogni caso deve poter diventare rosso (F6)",
                    "",
                    "Un argomento alla volta moltiplicato per 1,5 (i booleani invertiti),",
                    "il caso rieseguito, e si PRETENDE che diventi rosso. Un caso che resta",
                    "verde sotto ogni perturbazione e' **cieco**: le sue attese non vincolano",
                    "i suoi argomenti.",
                    "",
                    "Le mutazioni su argomenti che l'operazione ignora — `profondita_mm` su un",
                    "taglio passante — sono saltate leggendo il catalogo, non indovinate.",
                    "",
                    "| caso | mutazioni | rosse | |",
                    "|---|---|---|---|",
                };
                testo.AddRange(righe);
                testo.Add("");
                testo.Add(ciechi == 0
                    ? "**Nessun caso cieco.**"
                    : "**" + ciechi + " casi ciechi.** Un caso cieco e' verde e non prova niente.");
                File.WriteAllText(file, string.Join("\r\n", testo) + "\r\n",
                                  new System.Text.UTF8Encoding(false));
                Console.WriteLine("mutazioni: " + file);
            }
            catch (Exception ex) { Console.WriteLine("non ho potuto scrivere mutazioni.md: " + ex.Message); }
        }

        /// <summary>
        /// LE MISURE CHE SI PRENDONO DAL DOCUMENTO E NON DALLO STATO, in un
        /// posto solo. E' il ponte fra il valutatore — che di SOLIDWORKS non sa
        /// niente — e il documento aperto.
        ///
        /// Un elenco e non una catena di `if` per un motivo misurato: l'elenco
        /// dei nomi viveva in `Attese.Misura`, lontano da chi misura davvero, e
        /// due misure nuove aggiunte qui e dimenticate la' sono uscite come
        /// «non so misurare «ultimo_piano_mm»: i nomi sono …» con accanto
        /// l'elenco vecchio — un messaggio che diceva il falso su se stesso.
        /// Adesso i nomi sono le chiavi, e il messaggio le legge.
        ///
        /// ── PERCHE' E' PIGRA E NON UN CAMPO CON L'INIZIALIZZATORE ─────────
        ///
        /// Perche' scritta come `static readonly ... = new Dictionary...` NON
        /// PARTE PIU' IL BANCO:
        ///
        ///     Eccezione non gestita: System.TypeInitializationException
        ///     ---> System.IO.FileNotFoundException: SolidWorks.Interop.sldworks
        ///
        /// Un inizializzatore di campo statico finisce nel costruttore di
        /// classe di `Program`, che gira PRIMA della prima riga di `Main` — e
        /// quindi prima che `Main` installi l'`AssemblyResolve` che sa dove
        /// stanno gli interop. Le lambda nominano `IModelDoc2`, il caricamento
        /// parte troppo presto, e la cosa non somiglia per niente alla sua
        /// causa: l'eseguibile muore prima di stampare una riga, e il banco
        /// lanciato da `banco.ps1` esce con un `NativeCommandError` che
        /// sembra un guaio di PowerShell.
        ///
        /// Il campo senza inizializzatore non entra nel costruttore di classe:
        /// la tabella nasce alla prima misura, a resolver gia' in piedi.
        /// </summary>
        private static Dictionary<string,
            Func<IModelDoc2, Dictionary<string, object>, object>> _misureDelDocumento;

        private static Dictionary<string,
            Func<IModelDoc2, Dictionary<string, object>, object>> MisureDelDocumento
        {
            get { return _misureDelDocumento ?? (_misureDelDocumento = CostruisciMisureDelDocumento()); }
        }

        private static Dictionary<string,
            Func<IModelDoc2, Dictionary<string, object>, object>> CostruisciMisureDelDocumento()
        {
            return new Dictionary<string, Func<IModelDoc2, Dictionary<string, object>, object>>
        {
            { "volume_mm3",  (doc, stato) => VolumeDelDocumento(doc) },
            { "corpi",       (doc, stato) => ContaCorpi(doc) },
            { "ingombro_mm", (doc, stato) => IngombroDelDocumento(doc) },

            // ── DOVE STANNO I FORI, E NON QUANTO MATERIALE MANCA ─────────
            //
            // E' l'invariante della voce F8: **la posizione del cilindro, mai
            // il volume da solo**. Un caso che «regge» perche' il volume torna,
            // mentre il foro e' finito dall'altra parte, e' il difetto che
            // quella suite esiste per misurare.
            //
            // Ordinati, perche' l'ordine in cui SOLIDWORKS elenca le facce non
            // e' una cosa su cui un caso possa contare.
            { "centri_x_mm", (doc, stato) => CentriDeiCilindri(doc, 0) },
            { "centri_y_mm", (doc, stato) => CentriDeiCilindri(doc, 1) },
            { "centri_z_mm", (doc, stato) => CentriDeiCilindri(doc, 2) },
            { "diametri_mm", (doc, stato) => DiametriDeiCilindri(doc) },

            // ── E PER CIO' CHE CILINDRI NON NE LASCIA ────────────────────
            //
            // Una tasca rettangolare non ha un centro che si possa leggere
            // dalle facce, ma **sposta il baricentro del pezzo**, e di quanto
            // si calcola a mano. E' l'invariante di posizione per tutto quello
            // che non e' un foro: il volume da solo non distingue una tasca
            // ferma da una tasca che ha seguito il bordo.
            { "centro_massa_mm", (doc, stato) => CentroDiMassa(doc) },

            // ── DOVE E' FINITO L'ULTIMO PIANO CREATO ─────────────────────
            //
            // `crea_piano` risponde con la distanza CHIESTA, non con quella
            // ottenuta: un'attesa su `misure.distanza_mm` verificherebbe che il
            // ponte sa ricopiare un numero. Il segno lo si vede solo misurando
            // il piano, ed e' il difetto che «sembra giusto» — l'add-in passa
            // il valore assoluto piu' `OptionFlip`, ma circolano macro che
            // passano il segno, e con quelle il piano a -100 finisce a +100
            // senza che niente fallisca.
            { "ultimo_piano_mm", (doc, stato) => TraslazioneUltimoPiano(doc) },

            // Quanti spigoli curvi ha il pezzo. Serve all'attesa «nessuno
            // spigolo curvo e' offerto al modello»: il bordo di un foro e' un
            // cerchio, e come direzione di una ripetizione viene rifiutato.
            { "spigoli_curvi", (doc, stato) => SpigoliCurvi(stato) },

            // Quanti assi di riferimento ci sono. `crea_asse` non torna una
            // feature — torna un booleano — e l'unica prova che l'asse esiste
            // davvero e' che lo stato lo elenchi al passo dopo.
            { "assi_creati", (doc, stato) => QuantiNellElenco(stato, "assi") },

            // Quante lavorazioni ha l'albero. Serve a `elimina_lavorazione`:
            // il volume dice che il materiale e' tornato, questo dice che la
            // lavorazione non c'e' piu'. Sono due cose diverse — una feature
            // soppressa lascia il volume com'e' e resta nell'elenco.
            { "quante_lavorazioni", (doc, stato) => QuanteFeature(stato) },

            // QUANTI NOMI DI CONFIGURAZIONE DICHIARA IL PEZZO.
            //
            // I NOMI NON SI POSSONO SCRIVERE IN UN CASO. Quella predefinita si
            // chiama «Default<A macchina>»: il pezzo fra parentesi angolari e'
            // lo stato di visualizzazione ed e' localizzato, quindi un caso
            // che lo scrivesse sarebbe verde su questa installazione e rosso
            // sulla prossima. Il CONTO invece e' un fatto: se
            // `crea_configurazione` non creasse niente resterebbe quello di
            // prima mentre l'operazione risponde che e' andata bene.
            //
            // «NOMI» E NON «CONFIGURAZIONI», e la distinzione e' costata un
            // rosso: un pezzo appena nato da questo modello dichiara DUE nomi
            // -- «Default<A macchina>» e «Default<Saldato>» -- e un caso che
            // dava per scontato «un pezzo nuovo, una configurazione» ha
            // trovato tre dove ne aspettava due. Il conto vero, quello che
            // `GetConfigurationCount()` risponde, e' la misura qui sotto.
            { "quanti_nomi_di_configurazione", (doc, stato) => QuantiNellElenco(stato, "configurazioni") },

            // QUANTE NE CONTA SOLIDWORKS, che e' un'altra chiamata e puo'
            // rispondere un altro numero. Sta nello stato, letta da
            // `GetConfigurationCount()`.
            { "quante_configurazioni", (doc, stato) => DalloStato(stato, "quante_configurazioni") },
        };
        }

        /// <summary>
        /// Un campo dello stato preso com'e'. Null quando non c'e' — e chi
        /// chiama lo fa diventare un rosso, perche' un campo assente non e'
        /// uno zero.
        /// </summary>
        private static object DalloStato(Dictionary<string, object> stato, string campo)
        {
            return stato != null && stato.ContainsKey(campo) ? stato[campo] : null;
        }

        /// <summary>Quante voci ha una famiglia dello stato.</summary>
        private static int QuantiNellElenco(Dictionary<string, object> stato, string famiglia)
        {
            var voci = (stato != null && stato.ContainsKey(famiglia)
                ? stato[famiglia] : null) as System.Collections.IList;
            return voci == null ? 0 : voci.Count;
        }

        /// <summary>
        /// Una misura del documento, o un rifiuto che dice quali esistono.
        ///
        /// Tornava `null` per un nome sconosciuto, e un `null` confrontato con
        /// un numero e' un ROSSO: la prova falliva dicendo «atteso 100, trovato
        /// (niente)», che somiglia a un difetto del ponte e non lo e'. Un nome
        /// che non esiste e' un errore di banco, e va detto come tale.
        /// </summary>
        private static object DalDocumento(string cosa, IModelDoc2 doc,
                                           Dictionary<string, object> stato)
        {
            Func<IModelDoc2, Dictionary<string, object>, object> misura;
            if (!MisureDelDocumento.TryGetValue(cosa, out misura))
                throw new NonHoPotutoMisurare(
                    "non so misurare «" + cosa + "»: i nomi sono " +
                    string.Join(", ", MisureDelDocumento.Keys.ToArray()) +
                    ", stato.… e misure.…");
            return misura(doc, stato);
        }

        /// <summary>Quanti spigoli lo stato dichiara curvi.</summary>
        private static int SpigoliCurvi(Dictionary<string, object> stato)
        {
            var spigoli = (stato != null && stato.ContainsKey("spigoli")
                ? stato["spigoli"] : null) as System.Collections.IList;
            if (spigoli == null) return 0;
            int n = 0;
            foreach (var s in spigoli)
            {
                var voce = s as Dictionary<string, object>;
                if (voce != null && voce.ContainsKey("curvo") &&
                    Convert.ToString(voce["curvo"]).Equals("True", StringComparison.OrdinalIgnoreCase))
                    n++;
            }
            return n;
        }

        private static double[] CentroDiMassa(IModelDoc2 doc)
        {
            var parte = doc as IPartDoc;
            if (parte == null) return null;
            var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (corpi == null || corpi.Length == 0) return null;
            double vx = 0, vy = 0, vz = 0, volume = 0;
            foreach (var c in corpi)
            {
                var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length < 4) continue;
                double v = mp[3] * 1e9;
                if (v <= 0) continue;
                volume += v;
                vx += mp[0] * 1000.0 * v;
                vy += mp[1] * 1000.0 * v;
                vz += mp[2] * 1000.0 * v;
            }
            if (volume <= 0) return null;
            return new[]
            {
                Math.Round(vx / volume, 3), Math.Round(vy / volume, 3), Math.Round(vz / volume, 3),
            };
        }

        /// <summary>
        /// Le coordinate dei centri delle facce cilindriche lungo un asse, in
        /// mm, ordinate. Un foro passante lascia piu' facce sullo stesso asse:
        /// si conta il FORO, non la faccia.
        /// </summary>
        private static double[] CentriDeiCilindri(IModelDoc2 doc, int asse)
        {
            return Cilindri(doc).Select(c => Math.Round(c[asse], 2)).OrderBy(x => x).ToArray();
        }

        private static double[] DiametriDeiCilindri(IModelDoc2 doc)
        {
            return Cilindri(doc).Select(c => Math.Round(c[3] * 2, 2)).OrderBy(x => x).ToArray();
        }

        /// <summary>{x, y, z, raggio} di ogni cilindro distinto, in mm.</summary>
        private static List<double[]> Cilindri(IModelDoc2 doc)
        {
            var fuori = new List<double[]>();
            var parte = doc as IPartDoc;
            if (parte == null) return fuori;
            var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (corpi == null) return fuori;
            foreach (var c in corpi)
            {
                var facce = (c as IBody2)?.GetFaces() as object[];
                if (facce == null) continue;
                foreach (var o in facce)
                {
                    double[] par;
                    try
                    {
                        var su = (o as IFace2)?.IGetSurface();
                        if (su == null || !su.IsCylinder()) continue;
                        par = su.CylinderParams as double[];
                    }
                    catch { continue; }
                    if (par == null || par.Length < 7) continue;
                    var q = new[] { par[0] * 1000, par[1] * 1000, par[2] * 1000, par[6] * 1000 };
                    bool gia = fuori.Any(p =>
                        Math.Abs(p[0] - q[0]) < 0.05 && Math.Abs(p[1] - q[1]) < 0.05 &&
                        Math.Abs(p[2] - q[2]) < 0.05 && Math.Abs(p[3] - q[3]) < 0.05);
                    if (!gia) fuori.Add(q);
                }
            }
            return fuori;
        }

        /// <summary>I file dei casi, o null (dopo aver gia' detto perche').</summary>
        private static List<string> FileDeiCasi()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var radiceCasi = Path.Combine(banco.FullName, "casi");
                var file = new List<string>();
                foreach (var famiglia in Famiglie)
                {
                    var cartella = Path.Combine(radiceCasi, famiglia);
                    if (!Directory.Exists(cartella))
                    {
                        ErroreDiBanco("casi: la cartella «" + famiglia + "» c'e'", cartella);
                        return null;
                    }
                    file.AddRange(Directory.GetFiles(cartella, "*.json"));
                }
                return file.OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) { ErroreDiBanco("casi: trovo la cartella", Perche(ex)); return null; }
        }

        // ==================================================================
        // UN CASO
        // ==================================================================
        private static bool CorriUnCaso(string percorso, List<Gemellaggio> gemelle)
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
            catch (Exception ex) { ErroreDiBanco(nome, "JSON illeggibile: " + Perche(ex)); return false; }
            if (caso == null) { ErroreDiBanco(nome, "il file non e' un oggetto JSON"); return false; }

            // Il nome del caso e' quello DENTRO il file, e deve combaciare col
            // nome del file: due nomi diversi per la stessa cosa fanno cercare
            // nel posto sbagliato quando una prova diventa rossa.
            string dichiarato = Testo(caso, "nome");
            if (dichiarato == null) { ErroreDiBanco(nome, "manca «nome»"); return false; }
            if (dichiarato != nome)
            {
                ErroreDiBanco(nome, "il file si chiama «" + nome + "» e dentro dice «" +
                                    dichiarato + "»");
                return false;
            }
            if (Testo(caso, "perche") == null)
            {
                ErroreDiBanco(nome, "manca «perche»: un caso senza il dubbio che scioglie " +
                                    "non si sa piu' perche' esiste");
                return false;
            }

            IModelDoc2 doc = null;
            bool verde;
            try
            {
                doc = NuovaParte();
                verde = EseguiIPassi(nome, caso, doc, gemelle, percorso);
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); return false; }
            finally { Chiudi(doc); }

            return SuQuestaLibreria(nome, caso, verde);
        }

        /// <summary>
        /// `rossa_prima_di` — LA PROVA PIU' FORTE CHE UNA PROVA CUSTODISCA
        /// DAVVERO IL SUO DIFETTO (voce F10).
        ///
        /// Un caso puo' dichiarare la versione della libreria **prima della
        /// quale doveva essere rossa**. Lanciando il banco con `--libreria` su
        /// una versione piu' vecchia, quel caso **deve** fallire: se passa, non
        /// stava custodendo niente.
        ///
        /// Un `provata_a_fallire` scritto a mano e' una dichiarazione, e una
        /// dichiarazione si copia dal caso accanto. Questo no: lo decide la
        /// libreria che gira.
        /// </summary>
        private static bool SuQuestaLibreria(string nome, Dictionary<string, object> caso, bool verde)
        {
            string prima = Testo(caso, "rossa_prima_di");
            if (prima == null) return verde;

            Version soglia, adesso;
            if (!Version.TryParse(prima, out soglia))
            {
                ErroreDiBanco(nome, "«rossa_prima_di» non e' una versione: «" + prima + "»");
                return false;
            }
            adesso = _addin.GetName().Version;
            if (adesso >= soglia) return verde;   // libreria riparata: vale l'esito vero

            // Libreria piu' vecchia della riparazione: il caso DEVE essere rosso.
            Segna(nome + " · rossa prima della " + prima, !verde,
                  verde
                      ? "su " + adesso + " e' passata, e non doveva: questa prova non " +
                        "custodisce il difetto che dice di custodire"
                      : "su " + adesso + " e' rossa, come deve");
            return !verde;
        }

        /// <summary>
        /// I passi di un caso, su un documento gia' aperto.
        ///
        /// Sta in un metodo suo perche' `--muta` (voce F6) riesegue lo STESSO
        /// caso con un argomento cambiato, e quel caso non viene da un file:
        /// viene da una copia in memoria. `gemelle` e `percorso` possono essere
        /// nulli — li' non c'e' nessuna coppia da registrare e nessun file
        /// accanto a cui guardare.
        /// </summary>
        private static bool EseguiIPassi(
            string nome, Dictionary<string, object> caso, IModelDoc2 doc,
            List<Gemellaggio> gemelle, string percorso)
        {
            var passi = Elenco(caso, "passi");
            if (passi == null || passi.Count == 0) { ErroreDiBanco(nome, "manca «passi»"); return false; }

            {
                var stato = LeggiStato(doc);

                for (int i = 0; i < passi.Count; i++)
                {
                    var passo = passi[i] as Dictionary<string, object>;
                    if (passo == null) { ErroreDiBanco(nome, "il passo " + (i + 1) + " non e' un oggetto"); return false; }
                    string etichetta = nome + " · passo " + (i + 1);

                    string operazione = Testo(passo, "operazione");
                    if (operazione == null) { ErroreDiBanco(etichetta, "manca «operazione»"); return false; }

                    object grezziArg;
                    passo.TryGetValue("argomenti", out grezziArg);
                    Dictionary<string, object> argomenti;
                    // UN SELETTORE CHE NON RISOLVE NON E' UNA PROVA ROSSA:
                    // il caso non si e' potuto ESEGUIRE, e dirlo «fallito»
                    // farebbe credere che si sia misurato qualcosa (voce F4).
                    try { argomenti = Risolvi(grezziArg, stato) as Dictionary<string, object>; }
                    catch (NonHoPotutoMisurare ex) { ErroreDiBanco(etichetta, ex.Message); return false; }
                    catch (Exception ex) { Segna(etichetta, false, ex.Message); return false; }
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
                            return false;
                        // LA COPPIA SI REGISTRA QUI E SI GUARDA ALLA FINE:
                        // «la gemella e' verde in questo giro» si sa solo dopo
                        // che il giro e' finito (voce F5).
                        if (gemelle != null)
                            gemelle.Add(new Gemellaggio
                            {
                                Etichetta = etichetta,
                                Caso = nome,
                                Gemella = Testo(deveFallire, "gemella"),
                                Operazione = operazione,
                                Argomenti = (passo.ContainsKey("argomenti") ? passo["argomenti"] : null)
                                            as Dictionary<string, object>,
                            });
                        continue;
                    }

                    if (misure == null) { Segna(etichetta, false, "fallito: " + Corto(errore)); return false; }

                    var attese = Elenco(passo, "attese");
                    if (attese == null || attese.Count == 0)
                    {
                        // UN PASSO SENZA ATTESE E' UN PASSO CIECO. Passa e non
                        // misura niente, ed e' il modo piu' comodo di avere una
                        // prova verde che non prova. Si dice, e si dice che il
                        // caso e' scritto male, non che il pezzo e' sbagliato.
                        ErroreDiBanco(etichetta,
                              "nessuna attesa: un passo che non misura niente non e' una prova");
                        return false;
                    }

                    foreach (var a in attese)
                    {
                        _attese++;
                        var attesa = a as Dictionary<string, object>;
                        if (attesa == null)
                        { ErroreDiBanco(etichetta, "un'attesa non e' un oggetto"); return false; }
                        string dettaglio;
                        bool ok;
                        // LE TRE MISURE DEL DOCUMENTO gliele passa chi il
                        // documento ce l'ha: il valutatore non sa cosa sia un
                        // `IModelDoc2`, ed e' quello che lo rende provabile
                        // senza SOLIDWORKS (voce F7).
                        var qui = doc;
                        try { ok = Combacia(attesa, c => DalDocumento(c, qui, stato), stato, misure, out dettaglio); }
                        catch (NonHoPotutoMisurare ex)
                        { ErroreDiBanco(etichetta + " · " + Testo(attesa, "cosa"), ex.Message); return false; }
                        catch (Exception ex) { Segna(etichetta, false, ex.Message); return false; }
                        // VERIFICATA vuol dire «ho misurato e ho confrontato»,
                        // non «e' andata bene»: una rossa e' verificata eccome.
                        _atteseVerificate++;
                        Segna(etichetta + " · " + Testo(attesa, "cosa"), ok, dettaglio);
                        if (!ok) return false;
                    }
                }
            }

            // TUTTI I PASSI, TUTTE LE ATTESE. Ci si arriva solo se non e'
            // uscito niente per strada: e' questo che la voce F5 chiama «verde
            // in questo giro».
            return true;
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

            // LA GEMELLA E' OBBLIGATORIA (voce F5).
            //
            // Una prova che verifica solo il rifiuto non distingue «il
            // controllo funziona» da «il controllo rifiuta tutto». Il caso 1 di
            // 80u — il guscio da 30 su una scatola alta 52, che sembrava
            // impossibile ed era solo brutto — dice cosa succede senza.
            string gemella = Testo(regole, "gemella");
            if (gemella == null)
            {
                ErroreDiBanco(etichetta,
                    "caso non ammesso: un «deve_fallire» deve nominare in «gemella» " +
                    "il caso che RIESCE con argomenti quasi uguali. Senza, questo " +
                    "rifiuto non distingue un controllo che funziona da uno che " +
                    "rifiuta tutto.");
                return false;
            }
            // Senza il percorso — cioe' sotto `--muta`, dove il caso viene da
            // una copia in memoria — il file accanto non si puo' guardare, e
            // non e' quello che si sta provando.
            if (percorsoDelCaso == null) { Segna(etichetta, true, "rifiutato: " + Corto(errore)); return true; }
            var accanto = Path.Combine(Path.GetDirectoryName(percorsoDelCaso), gemella + ".json");
            if (!File.Exists(accanto))
            {
                // «gemella mancante» e' uno dei tre errori di banco che la
                // voce F2 nominava per nome, insieme a «caso non ammesso» e
                // «campo assente».
                ErroreDiBanco(etichetta, "la gemella «" + gemella + "» non esiste");
                return false;
            }

            Segna(etichetta, true, "rifiutato: " + Corto(errore));
            return true;
        }

    }
}
