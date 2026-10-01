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
    /// Una tolleranza senza il suo `tolerance_why` **e' un caso
    /// rotto**, non un caso permissivo: il file esce rosso e dice quale campo
    /// manca. Un campo assente non diventa mai un verde.
    ///
    /// ── I SELETTORI, E PERCHE' DEVONO RIFIUTARE ──────────────────────────
    ///
    /// Gli identificativi dello stato sono POSIZIONALI — le facce si rinumerano
    /// per area a ogni lettura — quindi un caso non puo' scrivere «F3». Scrive
    /// che cosa vuole (`@face(normal=front)`) e lo si risolve sullo stato
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
        /// In `cases/` vivono anche i piani del formato vecchio, che
        /// `corri_piani.js` esegue e che qui sarebbero tutti «il file non e' un
        /// oggetto JSON»: scoprire le cartelle da sole vorrebbe dire
        /// trasformare un file d'altri in un errore di banco a ogni giro.
        ///
        /// Una famiglia nuova si aggiunge qui, e chi la dimentica lo scopre
        /// subito: i suoi casi non girano e il conto delle attese non sale.
        /// </summary>
        /// <summary>The Italian names each case was run with (W3.1-bis.5, D4), and whether it is a case of Banco/cases/aliases.</summary>
        private static readonly Dictionary<string, int> AliasUses = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> AliasCaseFolder = new Dictionary<string, bool>(StringComparer.Ordinal);

        private static readonly string[] Famiglie =
        {
            "features", "pairs", "slot", "chamfer", "revolve", "plane",
            "mirror", "patterns", "axis", "removal", "sentence", "check",
            "export", "measurement", "bodies", "document_properties", "text", "frames",
            "assemblies", "drawing",
            // «weldments» (then «saldature») e' entrata il 23/9/2026 con la voce Z-C6: cinque
            // operazioni a catalogo non erano esercitate da NIENTE — ne' da un
            // caso, ne' dal C#, ne' da un piano — e quattro delle cinque sono
            // di questa famiglia.
            //
            // QUESTO ELENCO E' ESPLICITO, ed e' un difetto in agguato: una
            // cartella di casi che non ci sta dentro e' un insieme di file che
            // sembrano prove e non girano mai. Misurato lo stesso giorno: le
            // altre cartelle fuori elenco (`bridge`, `limits`, `views`, `pezzi`,
            // `shape`) contengono solo PIANI, che e' giusto — ma e' un fatto
            // misurato, non una garanzia del meccanismo.
            "weldments",
            // «extrusions» (then «extrusions») e' entrata il 24/9/2026, ancora Z-C6: `extrude_profile`
            // ed `extrude_section` avevano solo piani a mano. Il controllo di
            // contratto che guarda questo elenco l'ha detta MUTA prima che il
            // banco girasse: e' il difetto in agguato del commento qui sopra,
            // preso stavolta da una prova e non da una lettura.
            "extrusions",
            // «sheet_metal» (voce GP18) e «dimensions» (voce Z-H3), le prime due famiglie
            // nate da una sonda che ha girato la sera stessa: la misura prima,
            // l'operazione dopo.
            "sheet_metal", "dimensions",
            // «reanchor» (voce E19, 25/9/2026): entita' che il piano nomina
            // con un uid letto PRIMA di un passo che le rinumera. I casi usano
            // «uid_from_step», l'unico modo di far fare al banco la strada del
            // ponte.
            "reanchor",
            // «aliases» (W3.1-bis.5, decision D4): one case per kind of name
            // of before the rename (operation, argument, value, selector),
            // written with that name on purpose. Removed in W3.1-bis.11 with
            // the aliases.
            "aliases",
        };

        /// <summary>
        /// The keys a case may carry (Banco/case-keys.json, W3.1-bis.2 item 4),
        /// loaded by the three runs that read cases: declared, mutations and
        /// regression.
        /// </summary>
        private static CaseKeys _caseKeys;

        /// <summary>
        /// Loads the list of case keys, or says why it cannot as a bench error
        /// and answers false: without the list no case is run, because a case
        /// that cannot be checked is not a case that passed the check.
        /// </summary>
        private static bool LoadCaseKeys()
        {
            if (_caseKeys != null) return true;
            var folder = BenchFolder();
            var path = folder == null ? CaseKeys.FileName : Path.Combine(folder, CaseKeys.FileName);
            try { _caseKeys = CaseKeys.Load(path); return true; }
            catch (Exception ex)
            {
                ErroreDiBanco("case keys: the list is readable", Perche(ex));
                return false;
            }
        }

        private static void ProvaCasiDichiarati()
        {
            Console.WriteLine();
            Console.WriteLine("=== i casi dichiarati, che sono file (F3) ===");

            string radiceCasi;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                radiceCasi = Path.Combine(banco.FullName, "cases");
            }
            catch (Exception ex)
            {
                ErroreDiBanco("casi dichiarati: trovo la cartella", Perche(ex));
                return;
            }

            var file = new List<string>();
            foreach (var famiglia in Famiglie)
            {
                if (_soloFamiglie != null && !_soloFamiglie.Contains(famiglia)) continue;
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
            if (!LoadCaseKeys()) return;

            // ── DUE GIRI, E IL SECONDO E' LA VOCE F5 ──────────────────────
            //
            // Prima si eseguono tutti i casi e si tiene da parte com'e' andata;
            // poi si guardano le gemelle. Non si puo' fare in un giro solo,
            // perche' «la gemella e' verde IN QUESTO GIRO» e' una domanda a cui
            // si puo' rispondere solo quando il giro e' finito.
            var comeEAndata = new Dictionary<string, bool>(StringComparer.Ordinal);
            var daControllare = new List<Gemellaggio>();
            var doveSta = new Dictionary<string, string>(StringComparer.Ordinal);

            // ── DUE FORME DI FILE NELLA STESSA CARTELLA, E SI DISTINGUONO ──
            //
            // `cases/drawing/` conteneva gia' i PIANI dei giri a mano — quelli
            // che si lanciano con `corri_piani.js` — e sono file di forma
            // diversa: un ARRAY di passi, senza nome, senza «why» e senza
            // attese. Un caso dichiarato e' un OGGETTO.
            //
            // La differenza e' STRUTTURALE, non un indizio: si guarda se la
            // radice del JSON e' un array. Un caso scritto male resta un
            // oggetto e continua a dare errore di banco come prima — **non si
            // sta allargando la maglia**, si sta distinguendo fra due cose che
            // non si somigliano.
            //
            // E QUANTI NE SALTA SI DICE. Un file saltato in silenzio e' come un
            // controllo che non scatta: il giorno in cui un caso vero viene
            // scritto come array, sparisce senza che nessuno se ne accorga, e
            // «tutto verde» conta un caso in meno.
            var piani = new List<string>();
            foreach (var f in file)
            {
                if (EUnPianoENonUnCaso(f)) { piani.Add(Path.GetFileName(f)); continue; }

                // One case, one entry of the test ledger: its tests carry its
                // name, and its expectations are counted on it (W3.1-bis.2, item 7).
                using (var scope = Ledger.OpenCase(Path.GetFileNameWithoutExtension(f),
                                                   Path.GetFileName(Path.GetDirectoryName(f)), "declared"))
                {
                    // SOLIDWORKS SE N'E' ANDATO: da qui in poi si dice cosa
                    // non si e' misurato, non si finge di averlo misurato
                    // (voce K17). `Saltata` non conta ne' come verde ne' come
                    // rosso, ed e' esattamente cio' che questi casi sono.
                    if (Sentinella.SeNEAndato)
                    {
                        Saltata(Path.GetFileNameWithoutExtension(f), Sentinella.Referto());
                        scope.Outcome = "skipped";
                        continue;
                    }

                    doveSta[Path.GetFileNameWithoutExtension(f)] = f;
                    bool green = CorriUnCaso(f, daControllare);
                    comeEAndata[Path.GetFileNameWithoutExtension(f)] = green;
                    // A bench error is not a red case (F4): nothing was measured.
                    scope.Outcome = green ? "green" : scope.HasBenchErrors ? "bench_error" : "red";
                }
            }
            Segna("i piani a mano si riconoscono e si saltano", true,
                  piani.Count == 0
                      ? "nessuno: tutti i file sono casi dichiarati"
                      : piani.Count + " saltati (sono array, non casi): " +
                        string.Join(", ", piani.Take(4)) + (piani.Count > 4 ? " ..." : ""));

            GuardaLeGemelle(daControllare, comeEAndata, doveSta);

            // QUALI CASI SONO VERDI, PER NOME (voce Z-C6, la seconda meta').
            //
            // `last.json` portava i totali e basta: «972 prove, 0 fallite» non
            // dice QUALI casi hanno girato, e quindi nessuno poteva controllare
            // che ogni `Provata = true` puntasse a un caso che gira davvero. Il
            // 25/9/2026, scrivendo i casi mancanti, due operazioni a catalogo si
            // sono scoperte mai funzionanti. Questa riga la raccoglie `banco.ps1`
            // in `green_cases`, e la suite di contratto la incrocia col catalogo.
            //
            // Esce PRIMA della riga di ====, e non comincia con uno spazio: non
            // e' una riga di esito, e non va contata come tale.
            Console.WriteLine("casi verdi: " + string.Join(" ",
                comeEAndata.Where(kv => kv.Value).Select(kv => kv.Key)
                           .OrderBy(k => k, StringComparer.Ordinal)));

            // THE ITALIAN NAMES THE RUN ACCEPTED (W3.1-bis.5): a line of its own,
            // like the green cases, which banco.ps1 writes into last.json as
            // «alias_uses». Outside the alias cases the number must be 0: a case
            // that still speaks the old names would hide behind the aliases until
            // W3.1-bis.11 removes them.
            var outside = AliasUses.Keys.Where(k => !AliasCaseFolder[k]).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Console.WriteLine("alias uses: " + AliasUses.Keys.Sum(k => AliasUses[k]) + " in " + AliasUses.Count +
                              " cases; outside the alias cases: " + outside.Sum(k => AliasUses[k]) +
                              (outside.Count > 0 ? " (" + string.Join(" ", outside) + ")" : ""));
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
            public List<object> PezzoPrima;               // i passi prima del rifiuto: il pezzo
            public string DifferenzaNelPezzo;             // «difference_is_the_part», o null
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
                      "nessun caso con «must_fail»");
                return;
            }

            int verdi = 0;
            foreach (var c in coppie)
            using (Ledger.About(c.Caso))
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

            var passi = Elenco(gemella, "steps");
            if (passi == null) { perche = "la gemella non ha passi"; return false; }

            Dictionary<string, object> suoi = null;
            int doveSuoi = -1;
            for (int k = 0; k < passi.Count; k++)
            {
                var passo = passi[k] as Dictionary<string, object>;
                if (passo == null) continue;
                if (!string.Equals(Testo(passo, "operation"), c.Operazione, StringComparison.Ordinal)) continue;
                if (passo.ContainsKey("must_fail")) continue;   // due rifiuti non sono una coppia
                suoi = (passo.ContainsKey("arguments") ? passo["arguments"] : null)
                       as Dictionary<string, object>;
                doveSuoi = k;
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
            //
            // LA DIFFERENZA DICHIARATA NEL PEZZO (28/9/2026): stessi argomenti,
            // passi prima diversi. Si controlla in `Attese.SiSomiglianoNelPezzo`,
            // e non si scrive un argomento finto per far passare la regola.
            if (c.DifferenzaNelPezzo != null)
                return Attese.SiSomiglianoNelPezzo(c.Argomenti, suoi, c.PezzoPrima,
                                                   passi.Take(doveSuoi).ToList(), c.DifferenzaNelPezzo, out perche);
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
                    { ["uid"] = uid, ["facing"] = verso, ["area_mm2"] = 6000.0 };

            // ── UNA SOLA: si prende ────────────────────────────────────────
            var unaSola = new Dictionary<string, object>
            {
                ["faces"] = new List<object> { faccia("F1", "front"), faccia("F2", "back") },
            };
            Segna("F3: una faccia sola, si prende",
                  Prova(() => Seleziona("@face(normal=front)", unaSola)) == "F1", "");

            // ── ZERO: si rifiuta ───────────────────────────────────────────
            Segna("F3 ROSSO: nessuna faccia «up», si rifiuta",
                  Perche(() => Seleziona("@face(normal=up)", unaSola)).Contains("non l'ho trovata"),
                  Perche(() => Seleziona("@face(normal=up)", unaSola)));

            // ── DUE: si rifiuta, NON si prende la prima ────────────────────
            //
            // E' il difetto silenzioso che la voce F3 nomina: un caso che gira
            // su una faccia diversa da quella che il nome del file promette, e
            // che magari torna lo stesso volume. Senza questo rosso, «il caso
            // e' verde» non vorrebbe dire «ha lavorato dove diceva».
            var dueUguali = new Dictionary<string, object>
            {
                ["faces"] = new List<object> { faccia("F1", "front"), faccia("F7", "front") },
            };
            Segna("F3 ROSSO: due facce «front», si rifiuta invece di indovinare",
                  Perche(() => Seleziona("@face(normal=front)", dueUguali)).Contains("ne ho trovate 2"),
                  Perche(() => Seleziona("@face(normal=front)", dueUguali)));

            // ── LO SPIGOLO, per asse e lunghezza ───────────────────────────
            var spigoli = new Dictionary<string, object>
            {
                ["edges"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "E1", ["along"] = "Z", ["length_mm"] = 40.0 },
                    new Dictionary<string, object> { ["uid"] = "E2", ["along"] = "Z", ["length_mm"] = 100.0 },
                    new Dictionary<string, object> { ["uid"] = "E3", ["along"] = "X", ["length_mm"] = 100.0 },
                },
            };
            Segna("F3: lo spigolo si sceglie per asse e lunghezza",
                  Prova(() => Seleziona("@edge(axis=Z,length~=100)", spigoli)) == "E2", "");
            Segna("F3 ROSSO: senza la lunghezza, due spigoli su Z sono ambigui",
                  Perche(() => Seleziona("@edge(axis=Z)", spigoli)).Contains("ne ho trovate 2"),
                  "");

            // ── LA FEATURE ENNESIMA: l'ordine lo decide chi scrive il caso ──
            var feature = new Dictionary<string, object>
            {
                ["feature"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "Foro1", ["kind"] = "HoleWzd" },
                    new Dictionary<string, object> { ["uid"] = "Foro2", ["kind"] = "HoleWzd" },
                    new Dictionary<string, object> { ["uid"] = "Estrusione1", ["kind"] = "Extrusion" },
                },
            };
            Segna("F3: @feature(n=2) prende la seconda",
                  Prova(() => Seleziona("@feature(kind=HoleWzd,n=2)", feature)) == "Foro2", "");
            Segna("F3 ROSSO: @feature(n=3) su due, si rifiuta",
                  Perche(() => Seleziona("@feature(kind=HoleWzd,n=3)", feature)).Contains("ne ho trovate 2"),
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
                        ["dimensions"] = new List<object>
                        {
                            new Dictionary<string, object> { ["name"] = "D1@Schizzo1@Parte99.Part" },
                            new Dictionary<string, object> { ["name"] = "D2@Schizzo1@Parte99.Part" },
                        },
                    },
                },
            };
            Segna("F3: @dimension torna il nome col documento di ADESSO",
                  Prova(() => Seleziona("@dimension(feature=Schizzo1,name=D1)", conQuote))
                      == "D1@Schizzo1@Parte99.Part", "");

            // ── IL CORPO, PER NOME: le parentesi del nome non rompono niente ─
            //
            // I numeri C1, C2... dello stato non seguono l'ordine di creazione
            // e cambiano fra un giro e l'altro (misurato il 25/9/2026); il nome
            // si'. Qui lo stato e' scritto apposta al contrario: C1 e' la barra
            // (2). Un selettore che tornasse «il secondo corpo» sbaglierebbe.
            var corpi = new Dictionary<string, object>
            {
                ["bodies"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "C1", ["name"] = "TUBO 80X80X5(2)" },
                    new Dictionary<string, object> { ["uid"] = "C2", ["name"] = "TUBO 80X80X5(1)" },
                },
            };
            Segna("F3: @body(name=...) prende il corpo per nome, parentesi comprese",
                  Prova(() => Seleziona("@body(name=TUBO 80X80X5(2))", corpi)) == "C1",
                  Prova(() => Seleziona("@body(name=TUBO 80X80X5(2))", corpi)));
            Segna("F3 ROSSO: @body con un nome che non c'e', si rifiuta",
                  Perche(() => Seleziona("@body(name=TUBO 80X80X5(3))", corpi)).Contains("non l'ho trovata"),
                  "");

            // ── UNA TOLLERANZA SENZA IL PERCHE' E' UN CASO ROTTO ────────────
            //
            // Non un caso permissivo: rotto. Un campo assente non diventa mai
            // un verde.
            var senzaPerche = new Dictionary<string, object>
            {
                ["what"] = "volume_mm3", ["equal_to"] = 100.0, ["tolerance"] = 5.0,
            };
            string detto;
            string motivo = Perche(() => { string d; Combacia(senzaPerche, null, null, null, out d); return d; });
            Segna("F3 ROSSO: tolleranza senza «tolerance_why»",
                  motivo.Contains("tolerance_why"), Corto(motivo));

            // E con il perche', la stessa attesa si valuta (qui fallisce per il
            // valore, non per il formato: il punto e' che ci ARRIVA).
            var colPerche = new Dictionary<string, object>
            {
                ["what"] = "measurements.x", ["equal_to"] = 100.0, ["tolerance"] = 5.0,
                ["tolerance_why"] = "si misura con un righello",
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

            // Le misure che `apply_material` riporta davvero. Non c'e'
            // «material_removed_mm3»: quell'operazione non toglie niente.
            var misure = new Dictionary<string, object>
            {
                ["material"] = "1.0037 (S235JR)",
                ["library"] = "solidworks materials",
                ["class_name"] = "Acciaio",
            };

            string detto = Perche(() =>
            {
                string d;
                Combacia(new Dictionary<string, object>
                { ["what"] = "measurements.material_removed_mm3", ["equal_to"] = 0.0 },
                         null, null, misure, out d);
                return d;
            });
            Segna("F4 ROSSO: campo assente, e dice quali c'erano",
                  detto.Contains("campo assente: misure.materiale_tolto_mm3") &&
                  detto.Contains("ci sono: archivio, classe, materiale"),
                  Corto(detto));

            // ── E UN NULL DICHIARATO NON E' UN CAMPO ASSENTE ───────────────
            //
            // Lo stato scrive `material: null` APPOSTA, per dire «quel posto
            // esiste ed e' vuoto»: e' l'unico modo perche' il modello si accorga
            // di doverlo riempire. Confonderlo con un campo che manca
            // toglierebbe di mezzo proprio quella distinzione.
            var statoSenzaMateriale = new Dictionary<string, object> { ["material"] = null };
            string d2;
            bool ok = false;
            try
            {
                ok = Combacia(new Dictionary<string, object>
                { ["what"] = "state.material", ["equal_to"] = "" },
                              null, statoSenzaMateriale, null, out d2);
            }
            catch (Exception ex) { d2 = ex.Message; }
            Segna("F4: «material: null» si misura, non e' un campo assente", ok, Corto(d2));

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
        /// Mutare un argomento che l'operazione **ignora** — `depth_mm`
        /// su un taglio passante — resta giustamente verde e non e' cecita'.
        /// Quelle mutazioni si saltano **leggendo il catalogo**: lo schema di
        /// `depth_mm` dice «solo se non passante», e quella riga si legge
        /// invece di ricopiarla qui.
        ///
        /// ── E SI MUTANO SOLO I PASSI CON LE ATTESE ───────────────────────
        ///
        /// Un passo con `must_fail` non ha attese da vincolare: un foro da
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
            if (!LoadCaseKeys()) return;
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
                // The same refusal as the normal run: a mutation of a case the
                // bench would not run proves nothing (W3.1-bis.2, item 4).
                var unknown = caso == null ? null : _caseKeys.Unknown(caso, _caseKeys.DeclaredRoot);
                if (unknown != null && unknown.Count > 0) { ErroreDiBanco(nome, CaseKeys.Message(unknown)); continue; }

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
            public string Come;                           // «passo 1 · thickness_mm 40 -> 60»
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
            var passi = Elenco(caso, "steps");
            if (passi == null) return fuori;

            for (int i = 0; i < passi.Count; i++)
            {
                var passo = passi[i] as Dictionary<string, object>;
                if (passo == null) continue;
                if (passo.ContainsKey("must_fail")) continue;
                var attese = Elenco(passo, "expect");
                if (attese == null || attese.Count == 0) continue;

                var argomenti = (passo.ContainsKey("arguments") ? passo["arguments"] : null)
                                as Dictionary<string, object>;
                if (argomenti == null) continue;
                string operazione = Testo(passo, "operation") ?? "";

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
                    var suoiPassi = Elenco(copia, "steps");
                    var suoPasso = suoiPassi[i] as Dictionary<string, object>;
                    ((Dictionary<string, object>)suoPasso["arguments"])[chiave] = mutato;
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
            // ANCHE LA MUTAZIONE PASSA DA `PreparaIlDocumento`: se il caso e'
            // un assieme, mutarlo su una parte vuota lo farebbe fallire per il
            // motivo sbagliato — e una mutazione che diventa rossa perche' il
            // documento non era quello giusto **non prova che il caso custodisca
            // il suo difetto**. Direbbe di si' mentendo, che e' peggio del
            // silenzio.
            Preparazione preparato = null;
            _zitto = true;
            _zittoRosso = false;
            try
            {
                preparato = PreparaIlDocumento(Testo(caso, "name") ?? "?", caso);
                if (preparato == null || preparato.Documento == null) return false;
                EseguiIPassi(Testo(caso, "name") ?? "?", caso, preparato.Documento, null, null);
            }
            catch { _zittoRosso = true; }
            finally { ButtaViaLaPreparazione(preparato); _zitto = false; }
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
                // Every lookup below goes through the reflection log: they fall
                // back to null, and a renamed target would give fewer mutations
                // in silence (W3.1-bis.2, item 3).
                var tOperazioni = ReflectionLog.Global.TypeIn(_addin, "Riscontro.Disegno.Catalogo.Operazioni");
                var tNodo = ReflectionLog.Global.TypeIn(_addin, "Riscontro.Disegno.Catalogo.Nodo");
                if (tOperazioni == null || tNodo == null) return null;
                var tutte = ReflectionLog.Global.Field(tOperazioni, "Tutte",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    as System.Collections.IEnumerable;
                var json = ReflectionLog.Global.Method(tNodo, "Json", BindingFlags.Public | BindingFlags.Static);
                if (tutte == null || json == null) return null;

                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                var fuori = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                foreach (var o in tutte)
                {
                    var tipo = o.GetType();
                    string nome = Convert.ToString(ReflectionLog.Global.Field(tipo, "Nome")?.GetValue(o));
                    var schema = ReflectionLog.Global.Field(tipo, "Schema")?.GetValue(o);
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
                var cartella = Path.Combine(banco.FullName, "report");
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
                    "Le mutazioni su argomenti che l'operazione ignora — `depth_mm` su un",
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
        /// «non so misurare «last_plane_mm»: i nomi sono …» con accanto
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
            { "bodies",       (doc, stato) => ContaCorpi(doc) },
            { "bounding_box_mm", (doc, stato) => IngombroDelDocumento(doc) },

            // ── DOVE STANNO I FORI, E NON QUANTO MATERIALE MANCA ─────────
            //
            // E' l'invariante della voce F8: **la posizione del cilindro, mai
            // il volume da solo**. Un caso che «regge» perche' il volume torna,
            // mentre il foro e' finito dall'altra parte, e' il difetto che
            // quella suite esiste per misurare.
            //
            // Ordinati, perche' l'ordine in cui SOLIDWORKS elenca le facce non
            // e' una cosa su cui un caso possa contare.
            { "centers_x_mm", (doc, stato) => CentriDeiCilindri(doc, 0) },
            { "centers_y_mm", (doc, stato) => CentriDeiCilindri(doc, 1) },
            { "centers_z_mm", (doc, stato) => CentriDeiCilindri(doc, 2) },
            { "diameters_mm", (doc, stato) => DiametriDeiCilindri(doc) },

            // ── E PER CIO' CHE CILINDRI NON NE LASCIA ────────────────────
            //
            // Una tasca rettangolare non ha un centro che si possa leggere
            // dalle facce, ma **sposta il baricentro del pezzo**, e di quanto
            // si calcola a mano. E' l'invariante di posizione per tutto quello
            // che non e' un foro: il volume da solo non distingue una tasca
            // ferma da una tasca che ha seguito il bordo.
            { "center_of_mass_mm", (doc, stato) => CentroDiMassa(doc) },

            // ── DOVE E' FINITO L'ULTIMO PIANO CREATO ─────────────────────
            //
            // `create_plane` risponde con la distanza CHIESTA, non con quella
            // ottenuta: un'attesa su `measurements.distance_mm` verificherebbe che il
            // ponte sa ricopiare un numero. Il segno lo si vede solo misurando
            // il piano, ed e' il difetto che «sembra giusto» — l'add-in passa
            // il valore assoluto piu' `OptionFlip`, ma circolano macro che
            // passano il segno, e con quelle il piano a -100 finisce a +100
            // senza che niente fallisca.
            { "last_plane_mm", (doc, stato) => TraslazioneUltimoPiano(doc) },

            // Quanti spigoli curvi ha il pezzo. Serve all'attesa «nessuno
            // spigolo curvo e' offerto al modello»: il bordo di un foro e' un
            // cerchio, e come direzione di una ripetizione viene rifiutato.
            { "edges_curved", (doc, stato) => SpigoliCurvi(stato) },

            // Quanti assi di riferimento ci sono. `create_axis` non torna una
            // feature — torna un booleano — e l'unica prova che l'asse esiste
            // davvero e' che lo stato lo elenchi al passo dopo.
            { "axes_created", (doc, stato) => QuantiNellElenco(stato, "axes") },

            // Quante lavorazioni ha l'albero. Serve a `delete_feature`:
            // il volume dice che il materiale e' tornato, questo dice che la
            // lavorazione non c'e' piu'. Sono due cose diverse — una feature
            // soppressa lascia il volume com'e' e resta nell'elenco.
            { "how_many_features", (doc, stato) => QuanteFeature(stato) },

            // QUANTI NOMI DI CONFIGURAZIONE DICHIARA IL PEZZO.
            //
            // I NOMI NON SI POSSONO SCRIVERE IN UN CASO. Quella predefinita si
            // chiama «Default<A macchina>»: il pezzo fra parentesi angolari e'
            // lo stato di visualizzazione ed e' localizzato, quindi un caso
            // che lo scrivesse sarebbe verde su questa installazione e rosso
            // sulla prossima. Il CONTO invece e' un fatto: se
            // `create_configuration` non creasse niente resterebbe quello di
            // prima mentre l'operazione risponde che e' andata bene.
            //
            // «NOMI» E NON «CONFIGURAZIONI», e la distinzione e' costata un
            // rosso: un pezzo appena nato da questo modello dichiara DUE nomi
            // -- «Default<A macchina>» e «Default<Saldato>» -- e un caso che
            // dava per scontato «un pezzo nuovo, una configurazione» ha
            // trovato tre dove ne aspettava due. Il conto vero, quello che
            // `GetConfigurationCount()` risponde, e' la misura qui sotto.
            { "how_many_configuration_names", (doc, stato) => QuantiNellElenco(stato, "configurations") },

            // QUANTE NE CONTA SOLIDWORKS, che e' un'altra chiamata e puo'
            // rispondere un altro numero. Sta nello stato, letta da
            // `GetConfigurationCount()`.
            { "how_many_configurations", (doc, stato) => DalloStato(stato, "how_many_configurations") },
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
            var spigoli = (stato != null && stato.ContainsKey("edges")
                ? stato["edges"] : null) as System.Collections.IList;
            if (spigoli == null) return 0;
            int n = 0;
            foreach (var s in spigoli)
            {
                var voce = s as Dictionary<string, object>;
                if (voce != null && voce.ContainsKey("curved") &&
                    Convert.ToString(voce["curved"]).Equals("True", StringComparison.OrdinalIgnoreCase))
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
                var radiceCasi = Path.Combine(banco.FullName, "cases");
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
        /// <summary>
        /// `true` se il file e' un PIANO a mano e non un caso dichiarato.
        ///
        /// La radice di un piano e' un array di passi; quella di un caso e' un
        /// oggetto con `name`, `why` e `steps`. **Un file illeggibile NON e'
        /// un piano**: si torna `false` e lo si lascia arrivare a `CorriUnCaso`,
        /// che lo dichiarera' errore di banco con il motivo giusto. *Un
        /// riconoscimento che inghiotte anche i guasti smette di riconoscere.*
        /// </summary>
        private static bool EUnPianoENonUnCaso(string percorso)
        {
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = int.MaxValue;
                return s.DeserializeObject(File.ReadAllText(percorso)) is object[];
            }
            catch { return false; }
        }

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

            // AN UNKNOWN KEY IS A CASE WRITTEN WRONG (W3.1-bis.2, item 4): the
            // reader would skip it in silence, and whatever it asked for would
            // never be checked. Refused before anything is built.
            var unknown = _caseKeys.Unknown(caso, _caseKeys.DeclaredRoot);
            if (unknown.Count > 0) { ErroreDiBanco(nome, CaseKeys.Message(unknown)); return false; }

            // Il nome del caso e' quello DENTRO il file, e deve combaciare col
            // nome del file: due nomi diversi per la stessa cosa fanno cercare
            // nel posto sbagliato quando una prova diventa rossa.
            string dichiarato = Testo(caso, "name");
            if (dichiarato == null) { ErroreDiBanco(nome, "manca «name»"); return false; }
            if (dichiarato != nome)
            {
                ErroreDiBanco(nome, "il file si chiama «" + nome + "» e dentro dice «" +
                                    dichiarato + "»");
                return false;
            }
            if (Testo(caso, "why") == null)
            {
                ErroreDiBanco(nome, "manca «why»: un caso senza il dubbio che scioglie " +
                                    "non si sa piu' perche' esiste");
                return false;
            }

            // LA PREPARAZIONE NON E' PIU' SOLO «UNA PARTE NUOVA» (voce GA0).
            // Un caso puo' chiedere un ASSIEME, e allora prepararlo vuol dire
            // costruire i componenti, salvarli su disco, riaprirli e inserirli.
            // `PreparaIlDocumento` torna null quando non ci e' riuscita, e ha
            // gia' detto perche' con `ErroreDiBanco`: qui non si aggiunge un
            // secondo messaggio che direbbe la stessa cosa peggio.
            Preparazione preparato = null;
            bool verde;
            try
            {
                preparato = PreparaIlDocumento(nome, caso);
                if (preparato == null || preparato.Documento == null) return false;
                verde = EseguiIPassi(nome, caso, preparato.Documento, gemelle, percorso, preparato);
            }
            // NON HO POTUTO MISURARE NON E' UNA PROVA ROSSA (voce K17).
            //
            // Prima questo `catch` segnava rosso qualunque cosa fosse
            // successa, e il 23/9/2026 ha dipinto di rosso nove casi
            // scritti bene mentre SOLIDWORKS non apriva piu' documenti.
            // Adesso la preparazione ha un tipo suo: se e' fallita, si
            // dice cosa NON si e' misurato.
            catch (Condiviso.PreparazioneFallita ex)
            {
                if (Sentinella.SeNEAndato) Saltata(nome, Perche(ex));
                else ErroreDiBanco(nome, Perche(ex));
                return false;
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); return false; }
            finally { ButtaViaLaPreparazione(preparato); }

            return SuQuestaLibreria(nome, caso, verde);
        }

        /// <summary>
        /// `red_before` — LA PROVA PIU' FORTE CHE UNA PROVA CUSTODISCA
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
            string prima = Testo(caso, "red_before");
            if (prima == null) return verde;

            Version soglia, adesso;
            if (!Version.TryParse(prima, out soglia))
            {
                ErroreDiBanco(nome, "«red_before» non e' una versione: «" + prima + "»");
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
        /// viene da una copia in memoria. `gemelle` e `path` possono essere
        /// nulli — li' non c'e' nessuna coppia da registrare e nessun file
        /// accanto a cui guardare.
        /// </summary>
        private static bool EseguiIPassi(
            string nome, Dictionary<string, object> caso, IModelDoc2 doc,
            List<Gemellaggio> gemelle, string percorso,
            Preparazione daRipulire = null)
        {
            var passi = Elenco(caso, "steps");
            if (passi == null || passi.Count == 0) { ErroreDiBanco(nome, "manca «steps»"); return false; }

            {
                // IL DOCUMENTO PUO' CAMBIARE A META' CASO (voce GT0).
                //
                // `create_drawing` apre una tavola, e da li' in poi i passi
                // lavorano su quella. Finche' questo era `doc` e basta, un caso
                // che creava una tavola metteva le viste dentro il pezzo — o
                // piu' spesso si fermava su «il documento attivo non e' una
                // tavola». E' il motivo per cui **nessuna delle dieci voci GT
                // ha mai avuto un caso di banco**: non e' mancata la voglia, e'
                // che il banco non sapeva seguire il secondo documento.
                //
                // Si segue solo cio' che l'operazione **dichiara**
                // (`Esito.DocumentoNuovo`), mai il documento attivo di
                // SOLIDWORKS: su questa macchina SOLIDWORKS e' condiviso, e
                // «active» e' cio' che ha davanti chiunque.
                var attuale = doc;
                var stato = LeggiStato(attuale);
                // Lo stato com'era PRIMA di ogni passo: serve a «uid_from_step».
                var statiPrima = new List<Dictionary<string, object>>();

                for (int i = 0; i < passi.Count; i++)
                {
                    statiPrima.Add(stato);
                    var passo = passi[i] as Dictionary<string, object>;
                    if (passo == null) { ErroreDiBanco(nome, "il passo " + (i + 1) + " non e' un oggetto"); return false; }
                    string etichetta = nome + " · passo " + (i + 1);

                    // GLI UID LETTI PRIMA, COME FA IL PONTE (voce E19).
                    //
                    // Un caso normale risolve i selettori sullo stato di ADESSO,
                    // cioe' con gli uid gia' aggiornati: e' comodo, ed e' una
                    // strada che in produzione non percorre nessuno. Chi scrive
                    // un piano legge lo stato UNA volta e poi scrive tre passi;
                    // dal secondo in poi gli uid che porta sono quelli di prima,
                    // e il ponte ci attacca le firme di prima. «uid_dal_passo: 2»
                    // fa lo stesso: selettori e firme dallo stato di PRIMA del
                    // passo 2. Senza questo la ricerca per firma degli spigoli
                    // non si poteva nemmeno scrivere come prova.
                    var statoDegliUid = stato;
                    object dalPasso;
                    if (passo.TryGetValue("uid_from_step", out dalPasso) && dalPasso != null)
                    {
                        int k;
                        if (!int.TryParse(Convert.ToString(dalPasso, CultureInfo.InvariantCulture),
                                          NumberStyles.Integer, CultureInfo.InvariantCulture, out k) ||
                            k < 1 || k > i + 1)
                        {
                            ErroreDiBanco(etichetta, "«uid_from_step» dev'essere il numero di un passo " +
                                          "fino a questo (1.." + (i + 1) + "), non «" + dalPasso + "»");
                            return false;
                        }
                        statoDegliUid = statiPrima[k - 1];
                    }

                    string operazione = Testo(passo, "operation");
                    if (operazione == null) { ErroreDiBanco(etichetta, "manca «operation»"); return false; }

                    object grezziArg;
                    passo.TryGetValue("arguments", out grezziArg);
                    Dictionary<string, object> argomenti;
                    // UN SELETTORE CHE NON RISOLVE NON E' UNA PROVA ROSSA:
                    // il caso non si e' potuto ESEGUIRE, e dirlo «fallito»
                    // farebbe credere che si sia misurato qualcosa (voce F4).
                    DeprecatedSelectors.Clear();
                    try { argomenti = Risolvi(grezziArg, statoDegliUid) as Dictionary<string, object>; }
                    catch (NonHoPotutoMisurare ex) { ErroreDiBanco(etichetta, ex.Message); return false; }
                    catch (Exception ex) { Segna(etichetta, false, ex.Message); return false; }
                    if (argomenti == null) argomenti = new Dictionary<string, object>();
                    if (!ReferenceEquals(statoDegliUid, stato)) AttaccaFirme(argomenti, statoDegliUid);

                    // «select_first» (voce Z-C21.2, rilievo L13-1): le entita'
                    // che restano SELEZIONATE quando il passo parte, come le
                    // lascerebbe un passo precedente o chi sta davanti a
                    // SOLIDWORKS. Serve ai difetti che dipendono dalla selezione
                    // e non dagli argomenti: `SaveAs3` esporta solo le facce
                    // selezionate (IModelDocExtension~SaveAs3.html, Remarks), e un
                    // caso senza questo campo non poteva nemmeno scriverlo.
                    object daSelezionare;
                    if (passo.TryGetValue("select_first", out daSelezionare) && daSelezionare != null)
                    {
                        string perche;
                        if (!SelezionaPrima(attuale, daSelezionare, statoDegliUid, out perche))
                        {
                            ErroreDiBanco(etichetta, "«select_first»: " + perche);
                            return false;
                        }
                    }
                    // «hide_first» (voce Z-C21.2, rilievo L12-1): i corpi
                    // nascosti quando il passo parte, come li nasconde chi vuole
                    // guardare dentro il pezzo. Nessuna operazione del catalogo
                    // nasconde un corpo, e senza questo campo il difetto «un
                    // corpo nascosto esce dal controllo» non si poteva scrivere.
                    object daNascondere;
                    if (passo.TryGetValue("hide_first", out daNascondere) && daNascondere != null)
                    {
                        string perche;
                        if (!NascondiPrima(attuale, daNascondere, statoDegliUid, out perche))
                        {
                            ErroreDiBanco(etichetta, "«hide_first»: " + perche);
                            return false;
                        }
                        // Lo stato si rilegge: il passo lo riceve, e un corpo
                        // nascosto e' un fatto che lo stato deve vedere.
                        stato = LeggiStato(attuale);
                    }
                    // «activate_before» (Z-C21.4, finding L16-2): a configuration of
                    // ANOTHER open document made active when the step starts, as the
                    // user leaves it by working on the model while the drawing is
                    // open. activate_configuration acts on the current document only,
                    // so without this field a drawing step could never find the
                    // model on a configuration other than the one its views show.
                    object toActivate;
                    if (passo.TryGetValue("activate_before", out toActivate) && toActivate != null)
                    {
                        string perche;
                        if (!ActivateBefore(toActivate, statoDegliUid, out perche))
                        {
                            ErroreDiBanco(etichetta, "«activate_before»: " + perche);
                            return false;
                        }
                    }

                    double volumePrima = VolumeDelDocumento(attuale);
                    int featurePrima = QuanteFeature(stato);

                    string errore;
                    IModelDoc2 nato;
                    var misure = Esegui(attuale, operazione, argomenti, out errore, out nato, stato);
                    // The Italian selectors this step was written with (Attese.SelectorAlias):
                    // the bench resolves selectors before the library sees the step, so it adds
                    // them to the library's own `deprecated` lines. Removed in W3.1-bis.11.
                    if (misure != null && DeprecatedSelectors.Count > 0)
                    {
                        object fromLibrary;
                        var library = misure.TryGetValue("deprecated", out fromLibrary) && fromLibrary is string[]
                            ? (string[])fromLibrary : new string[0];
                        misure["deprecated"] = DeprecatedSelectors.Concat(library).ToArray();
                    }
                    // Every Italian name a step was written with is counted per case:
                    // outside Banco/cases/aliases the count must be 0 (W3.1-bis.5,
                    // «alias uses = 0 in the report»). Removed in W3.1-bis.11.
                    object deprecatedLines;
                    if (misure != null && misure.TryGetValue("deprecated", out deprecatedLines) && deprecatedLines is string[])
                    {
                        int before;
                        AliasUses.TryGetValue(nome, out before);
                        AliasUses[nome] = before + ((string[])deprecatedLines).Length;
                        AliasCaseFolder[nome] = string.Equals(Path.GetFileName(Path.GetDirectoryName(percorso)), "aliases", StringComparison.Ordinal);
                    }

                    // SI CAMBIA SOLO SE IL PASSO E' RIUSCITO. Un `create_drawing`
                    // fallito a meta' puo' aver lasciato un documento a monitor
                    // e non e' quello su cui si continua: seguire un documento
                    // nato da un passo rosso vorrebbe dire misurare il relitto.
                    if (misure != null && nato != null && !ReferenceEquals(nato, attuale))
                    {
                        attuale = nato;
                        string comeSiChiama = null;
                        try { comeSiChiama = attuale.GetTitle(); } catch { }
                        // Lo si DICE. Un cambio di documento silenzioso e' la
                        // cosa che rende illeggibile un caso rosso tre passi
                        // piu' avanti: si legge «la vista non c'e'» e si va a
                        // cercare il difetto nell'operazione sbagliata.
                        Console.WriteLine("       | da qui si lavora su «" +
                                          (comeSiChiama ?? "(senza titolo)") + "»");

                        // E SI SEGNA PER CHIUDERLO DOPO.
                        //
                        // «Il banco sa seguire il secondo documento» non e'
                        // finito finche' non sa anche **richiuderlo**. Una
                        // tavola lasciata aperta resta li' per tutti i casi
                        // successivi, e non e' un fastidio estetico: sei
                        // documenti di troppo sono ~650 caratteri in piu' nello
                        // stato, e la prova «lo stato sta sotto i 16 KB» passa
                        // da 15.6k a 16.2k e **diventa rossa senza che il
                        // codice sia cambiato**. Mezz'ora a cercare una
                        // regressione che non c'e'.
                        // SI SEGNA IL RIFERIMENTO, NON IL NOME (voce K12-bis).
                        //
                        // Qui la tavola si chiama ancora `Disegno38 -
                        // Foglio1`; due passi dopo il caso la salva e diventa
                        // `drawing_dimensions_itself - Foglio1`. Segnando il nome di
                        // adesso, a fine caso si chiedeva a SOLIDWORKS di
                        // chiudere un documento che non esisteva piu' con quel
                        // nome — e `CloseDoc` su un nome sconosciuto non e' un
                        // errore, quindi non lo diceva nessuno.
                        if (daRipulire != null)
                            daRipulire.DocumentiDaChiudere.Add(attuale);
                    }

                    stato = LeggiStato(attuale);

                    var deveFallire = passo.ContainsKey("must_fail")
                        ? passo["must_fail"] as Dictionary<string, object> : null;

                    if (deveFallire != null)
                    {
                        if (!GuardaIlRifiuto(etichetta, deveFallire, misure, errore,
                                             attuale, stato, volumePrima, featurePrima, percorso))
                            return false;
                        // LA COPPIA SI REGISTRA QUI E SI GUARDA ALLA FINE:
                        // «la gemella e' verde in questo giro» si sa solo dopo
                        // che il giro e' finito (voce F5).
                        if (gemelle != null)
                            gemelle.Add(new Gemellaggio
                            {
                                Etichetta = etichetta,
                                Caso = nome,
                                Gemella = Testo(deveFallire, "twin"),
                                Operazione = operazione,
                                Argomenti = (passo.ContainsKey("arguments") ? passo["arguments"] : null)
                                            as Dictionary<string, object>,
                                PezzoPrima = passi.Take(i).ToList(),
                                DifferenzaNelPezzo = deveFallire.ContainsKey(CampoDifferenzaNelPezzo)
                                    ? Convert.ToString(deveFallire[CampoDifferenzaNelPezzo], CultureInfo.InvariantCulture)
                                    : null,
                            });
                        continue;
                    }

                    if (misure == null) { Segna(etichetta, false, "fallito: " + Corto(errore)); return false; }

                    var attese = Elenco(passo, "expect");
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
                        Ledger.ExpectationAttempted();
                        var attesa = a as Dictionary<string, object>;
                        if (attesa == null)
                        { ErroreDiBanco(etichetta, "un'attesa non e' un oggetto"); return false; }
                        string dettaglio;
                        bool ok;
                        // LE TRE MISURE DEL DOCUMENTO gliele passa chi il
                        // documento ce l'ha: il valutatore non sa cosa sia un
                        // `IModelDoc2`, ed e' quello che lo rende provabile
                        // senza SOLIDWORKS (voce F7).
                        var qui = attuale;
                        try { ok = Combacia(attesa, c => DalDocumento(c, qui, stato), stato, misure, out dettaglio); }
                        catch (NonHoPotutoMisurare ex)
                        { ErroreDiBanco(etichetta + " · " + Testo(attesa, "what"), ex.Message); return false; }
                        catch (Exception ex) { Segna(etichetta, false, ex.Message); return false; }
                        // VERIFICATA vuol dire «ho misurato e ho confrontato»,
                        // non «e' andata bene»: una rossa e' verificata eccome.
                        _atteseVerificate++;
                        Ledger.ExpectationChecked();
                        SegnaAttesa(etichetta + " · " + Testo(attesa, "what"), ok, dettaglio);
                        if (!ok) return false;
                    }
                }
            }

            // TUTTI I PASSI, TUTTE LE ATTESE. Ci si arriva solo se non e'
            // uscito niente per strada: e' questo che la voce F5 chiama «verde
            // in questo giro».
            return true;
        }

        /// <summary>
        /// Lascia selezionate le entita' di «select_first» (voce Z-C21.2).
        ///
        /// Il campo e' un elenco di uid o di selettori, risolti sullo STESSO
        /// stato degli argomenti del passo. L'entita' si prende dalle firme
        /// dello stato della libreria — le stesse che usano le operazioni per
        /// trovare «F3» — e si seleziona con `IEntity.Select4(true, null)`.
        /// Poi si conta: una selezione che non e' riuscita farebbe verde un
        /// caso che non ha provato niente, quindi e' un errore di banco.
        /// </summary>
        private static bool SelezionaPrima(
            IModelDoc2 doc, object grezzo, Dictionary<string, object> statoDegliUid,
            out string perche)
        {
            List<string> uid;
            List<object> entita;
            if (!EntitaDelloStato(doc, grezzo, statoDegliUid, out uid, out entita, out perche))
                return false;

            doc.ClearSelection2(true);
            for (int i = 0; i < uid.Count; i++)
            {
                var e = entita[i] as IEntity;
                if (e == null) { perche = "«" + uid[i] + "» non e' un'entita' che so selezionare"; return false; }
                if (!e.Select4(true, null)) { perche = "Select4 ha risposto no su «" + uid[i] + "»"; return false; }
            }
            int quante = ((ISelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1);
            if (quante != uid.Count)
            {
                perche = "chiesto di selezionare " + uid.Count + " entita', selezionate " + quante;
                return false;
            }
            Console.WriteLine("       | prima del passo restano selezionate: " + string.Join(", ", uid));
            return true;
        }

        /// <summary>
        /// «activate_before»: { "document": path of an open document, "configuration":
        /// its name } (Z-C21.4, L16-2). The document is found among the open ones
        /// by its full path, the configuration activated with ShowConfiguration2,
        /// and the active configuration READ BACK: ShowConfiguration2 answers
        /// false when the configuration is already the active one, so its answer
        /// decides nothing.
        /// </summary>
        private static bool ActivateBefore(object raw, Dictionary<string, object> statoDegliUid, out string perche)
        {
            perche = null;
            Dictionary<string, object> request;
            try { request = Risolvi(raw, statoDegliUid) as Dictionary<string, object>; }
            catch (Exception ex) { perche = ex.Message; return false; }
            string path = request == null ? null : Testo(request, "document");
            string configuration = request == null ? null : Testo(request, "configuration");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(configuration))
            {
                perche = "it needs «document» (a path) and «configuration» (a name)";
                return false;
            }
            IModelDoc2 target = null;
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                for (int guard = 0; d != null && guard < 500; guard++)
                {
                    string own = null;
                    try { own = d.GetPathName(); } catch { }
                    if (!string.IsNullOrWhiteSpace(own) &&
                        string.Equals(Path.GetFullPath(own), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    { target = d; break; }
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch (Exception ex) { perche = "walking the open documents: " + Perche(ex); return false; }
            if (target == null) { perche = "«" + path + "» is not open"; return false; }
            bool answer;
            try { answer = target.ShowConfiguration2(configuration); }
            catch (Exception ex) { perche = "ShowConfiguration2(«" + configuration + "»): " + Perche(ex); return false; }
            string active = null;
            try
            {
                IConfigurationManager targetManager = target.ConfigurationManager;
                IConfiguration activeNow = targetManager == null ? null : targetManager.ActiveConfiguration;
                active = activeNow == null ? null : activeNow.Name;
            }
            catch { }
            if (!string.Equals(active, configuration, StringComparison.Ordinal))
            {
                perche = "asked for «" + configuration + "» on «" + Path.GetFileName(path) + "» (ShowConfiguration2 " +
                         "answered " + answer + "), and reading it back the active configuration is «" + (active ?? "?") + "»";
                return false;
            }
            Console.WriteLine("       | before the step, «" + Path.GetFileName(path) + "» is on «" + active + "»");
            return true;
        }

        /// <summary>
        /// Nasconde i corpi di «hide_first» (voce Z-C21.2) con
        /// `IBody2.HideBody(true)` (IBody2~HideBody.html: «True to hide the
        /// body, false to show the body») e rilegge `IBody2.Visible`: un corpo
        /// che resta visibile farebbe verde un caso che non ha provato niente.
        /// </summary>
        private static bool NascondiPrima(
            IModelDoc2 doc, object grezzo, Dictionary<string, object> statoDegliUid,
            out string perche)
        {
            List<string> uid;
            List<object> entita;
            if (!EntitaDelloStato(doc, grezzo, statoDegliUid, out uid, out entita, out perche))
                return false;
            for (int i = 0; i < uid.Count; i++)
            {
                // UN CORPO, O IL CORPO DI UNA FACCIA. I nomi dei corpi dopo una
                // copia o una lavorazione non sono prevedibili, e i numeri C1, C2
                // cambiano fra un giro e l'altro (voce Z-C16); una faccia invece si
                // nomina per DOVE sta (`@face(normal=front,x~=150)`).
                var corpo = entita[i] as IBody2;
                var faccia = entita[i] as IFace2;
                if (corpo == null && faccia != null) corpo = faccia.GetBody() as IBody2;
                if (corpo == null) { perche = "«" + uid[i] + "» non e' ne' un corpo ne' una faccia"; return false; }
                corpo.HideBody(true);
                if (corpo.Visible) { perche = "«" + uid[i] + "» e' ancora visibile dopo HideBody(true)"; return false; }
            }
            try { doc.GraphicsRedraw2(); } catch { }
            Console.WriteLine("       | prima del passo restano nascosti: " + string.Join(", ", uid));
            return true;
        }

        /// <summary>
        /// Gli oggetti COM dietro a uid o selettori, dalle firme dello stato
        /// della libreria: le stesse che le operazioni usano per trovare «F3» o
        /// «C2».
        /// </summary>
        private static bool EntitaDelloStato(
            IModelDoc2 doc, object grezzo, Dictionary<string, object> statoDegliUid,
            out List<string> uid, out List<object> entita, out string perche)
        {
            perche = null;
            uid = new List<string>();
            entita = new List<object>();
            object risolto;
            try { risolto = Risolvi(grezzo, statoDegliUid); }
            catch (Exception ex) { perche = ex.Message; return false; }
            var elenco = risolto as System.Collections.IList;
            if (elenco != null) { foreach (var u in elenco) uid.Add(Convert.ToString(u)); }
            else if (risolto != null) uid.Add(Convert.ToString(risolto));
            if (uid.Count == 0) { perche = "l'elenco e' vuoto"; return false; }

            object statoOggetto;
            try { statoOggetto = LeggiStatoOggetto(doc); }
            catch (Exception ex) { perche = "non leggo lo stato: " + Perche(ex); return false; }
            var firme = ReflectionLog.Global.Property(_tStato, "Firme",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(statoOggetto) as System.Collections.IDictionary;
            if (firme == null) { perche = "lo stato della libreria non ha «Firme»"; return false; }
            foreach (var u in uid)
            {
                var firma = firme.Contains(u) ? firme[u] : null;
                var e = firma == null ? null : ReflectionLog.Global.Field(firma.GetType(), "Entita")?.GetValue(firma);
                if (e == null) { perche = "nello stato non c'e' «" + u + "»"; return false; }
                entita.Add(e);
            }
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

            var pezzi = Elenco(regole, "message_includes");
            if (pezzi == null || pezzi.Count == 0)
            {
                ErroreDiBanco(etichetta,
                      "«must_fail» senza «message_includes»: qualunque errore " +
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

            if (Vero(regole, "volume_unchanged"))
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
            if (Vero(regole, "no_extra_feature"))
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
            string gemella = Testo(regole, "twin");
            if (gemella == null)
            {
                ErroreDiBanco(etichetta,
                    "caso non ammesso: un «must_fail» deve nominare in «twin» " +
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
