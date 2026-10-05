using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// UN LIMITE MISURATO CHE LA DESCRIZIONE NON DICE COSTA UN TURNO A VUOTO
    /// (voci Z-G5 e Z-C11).
    ///
    /// ── LE DUE META' DELLA STESSA COSA ───────────────────────────────────
    ///
    /// Quando un'operazione rifiuta, qualcuno ha gia' misurato quel limite e
    /// ci ha scritto un caso di banco. Ma **il modello non legge i casi di
    /// banco**: legge il catalogo. Se la descrizione tace, l'unico modo che ha
    /// di scoprire il limite e' sbatterci contro — e un rifiuto costa un giro,
    /// mentre un limite dichiarato costa zero.
    ///
    /// *Dichiarare un limite prima di provare vale piu' di un rifiuto.*
    ///
    /// E l'altra meta' (Z-C11): **ogni rifiuto nasce con la sua gemella
    /// verde**. Il rischio speculare — rifiutare un pezzo legittimo — non lo
    /// rivede nessuno, perche' un rifiuto sbagliato *sembra* prudenza. La
    /// gemella e' cio' che distingue una guardia da un'operazione che dice
    /// sempre di no, ed e' capitato davvero: la prima versione di
    /// `section_view` rifiutava tutto, e il difetto e' uscito al primo
    /// uso vero.
    ///
    /// ── PERCHE' QUI E NON NEL BANCO ──────────────────────────────────────
    ///
    /// Il banco le gemelle le controlla gia', **ma dopo un'ora di giro e con
    /// SOLIDWORKS acceso**. Un caso di rifiuto scritto senza gemella e' un
    /// errore di scrittura, non di misura: si vede leggendo il JSON, in un
    /// decimo di secondo, e chi lo scrive deve saperlo prima di lanciare il
    /// banco, non dopo.
    /// </summary>
    internal static class LimitsTests
    {
        /// <summary>
        /// Quante operazioni hanno un rifiuto provato dal banco e **non lo
        /// dicono** nella loro descrizione. **Zero, dal 23/9/2026.**
        ///
        /// Erano dodici quella mattina — asola, activate_configuration,
        /// activate_sheet, create_configuration, drawing_dimension, linear_pattern,
        /// rivoluzione, chamfer_edges, suppress_feature, specchia,
        /// cylindrical_cut, rectangular_cut — e ognuna aveva un limite
        /// gia' misurato, con tanto di caso di banco che lo pretende, che il
        /// modello poteva scoprire **solo sbattendoci contro**.
        ///
        /// Le frasi nuove non sono inventate: sono il messaggio che il caso
        /// pretende, girato al futuro. *Un limite dichiarato costa zero turni;
        /// lo stesso limite scoperto da un rifiuto ne costa uno.*
        ///
        /// E' un TETTO, e da zero non puo' che salire — cioe' non puo'. Una
        /// guardia nuova senza la frase che la dichiara fa diventare rosso
        /// questo controllo **il giorno in cui la si scrive**, che e' l'unico
        /// giorno in cui qualcuno la sta guardando.
        /// </summary>
        private const int Mute = 0;

        /// <summary>
        /// Le parole con cui una descrizione dichiara un limite. Sono quelle
        /// che il catalogo usa davvero, non quelle che «dovrebbe» usare: un
        /// elenco inventato a tavolino direbbe che tutte tacciono.
        /// </summary>
        private static readonly string[] Telltales =
        {
            "rifiut", "non posso", "limite", "non guarda", "non sa",
            "non copre", "fallisc", "sbord", "piu' grande",
            // The catalogue is moving to English (Z-C21.10): these are the
            // English stems of the same declaration.
            "refus", "reject",
        };

        /// <summary>
        /// Quanti casi dichiarati citano un corpo PER NUMERO («C1», «C2») in un
        /// campo `_uid`. Misurato il 25/9/2026: l'ordine dei corpi nello stato
        /// viene da `GetBodies2`, **non segue la creazione e cambia fra un giro
        /// e l'altro** (tre membri strutturali: C1 = la corta in un giro, la
        /// lunga nell'altro). Molti di questi casi hanno un corpo solo quando
        /// lo citano, e sono innocui; gli altri sono l'elenco della voce Z-C16.
        /// Il banco ha `@body(name=...)`, che segue l'ordine di creazione.
        ///
        /// **12 al 25/9/2026**, visto rosso a tetto zero prima di fissarlo (i due
        /// rifiuti su `C9`, che non esiste in nessun ordine, restano fuori).
        /// </summary>
        private const int BodiesByNumberCap = 12;

        /// <summary>I «C\d+» scritti a mano nei campi `_uid` degli argomenti.</summary>
        private static IEnumerable<string> BodiesByNumber(object arguments)
        {
            var d = arguments as Dictionary<string, object>;
            if (d == null) yield break;
            foreach (var kv in d)
            {
                if (!kv.Key.EndsWith("_uid", StringComparison.Ordinal)) continue;
                var values = kv.Value as object[] ?? new[] { kv.Value };
                foreach (var v in values)
                {
                    var s = v as string;
                    if (s != null && System.Text.RegularExpressions.Regex.IsMatch(s, @"^C\d+$"))
                        yield return s;
                }
            }
        }

        internal static void All()
        {
            Group("i limiti misurati e le descrizioni che li dichiarano (Z-G5, Z-C11)");

            var folder = Path.Combine(Root(), "Bench", "cases");
            if (!Directory.Exists(folder))
            {
                Check("la cartella dei casi c'e'", false, "manca " + folder);
                return;
            }

            var serializer = new JavaScriptSerializer();
            int declared = 0, planes = 0;
            var unreadable = new List<string>();
            var withoutTwin = new List<string>();
            var absentTwins = new List<string>();
            var withoutMessage = new List<string>();
            var refusedOperations = new Dictionary<string, int>(StringComparer.Ordinal);

            var file = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);
            var names = new HashSet<string>(
                file.Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.Ordinal);
            // LA TOLLERANZA SENZA IL PERCHE'. Il banco la ferma come errore di
            // banco — ma dopo un'ora di giro. Il 24/9/2026 sera un caso nuovo
            // l'ha fatto: un giro intero perso per una riga che si vede leggendo
            // il JSON. Qui si vede prima di lanciare.
            var withoutWhy = new List<string>();
            var muteSteps = new List<string>();
            // LA GEMELLA FINTA: stessa operazione e argomenti IDENTICI al rifiuto.
            // Il banco la ferma come errore di banco (voce F5) — dopo un'ora. Il
            // 25/9/2026 e' successo con `flat_pattern_dxf`, dove il rifiuto dipende
            // dallo stato e non da un argomento. Qui si confronta prima.
            var refusalsToCompare = new List<Tuple<string, string, string, string>>(); // caso, operazione, argomenti, gemella
            var argumentsPerCase = new Dictionary<string, List<Tuple<string, string>>>(StringComparer.Ordinal);
            // LA GEMELLA COME LA GUARDA IL BANCO (25/9/2026): il primo passo
            // RIUSCITO della stessa operazione, e almeno un argomento in comune.
            var caseSteps = new Dictionary<string, List<Tuple<string, Dictionary<string, object>, bool>>>(StringComparer.Ordinal);
            var refusalsWithTwin = new List<Tuple<string, string, Dictionary<string, object>, string>>();
            // LA GEMELLA CON GLI STESSI ARGOMENTI SU UN ALTRO PEZZO (28/9/2026):
            // caso, operazione, argomenti, gemella, indice del passo, frase
            // dichiarata. Si guarda con `Expectations.ResembleInPart`, la stessa
            // funzione del banco, e non con le due regole qui sopra.
            var refusalsInPart = new List<Tuple<string, string, Dictionary<string, object>, string, int, string>>();
            var rawSteps = new Dictionary<string, object[]>(StringComparer.Ordinal);
            var js = new JavaScriptSerializer();
            var casesWithNumbers = new SortedSet<string>(StringComparer.Ordinal);
            // Z-C6: operazione -> casi dichiarati che la usano in un passo.
            var casesPerOp = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var f in file)
            {
                string itemName = Path.GetFileNameWithoutExtension(f);
                object root;
                try { root = serializer.DeserializeObject(File.ReadAllText(f)); }
                catch (Exception ex) { unreadable.Add(itemName + ": " + ex.Message); continue; }

                var testCase = root as Dictionary<string, object>;
                if (testCase == null) { planes++; continue; }
                declared++;

                // Le attese stanno nei passi («attese») e, per la regressione,
                // in «after». Si guardano tutte e due.
                Action<object, string> checkExpectations = (list, location) =>
                {
                    foreach (var at in (list as object[]) ?? new object[0])
                    {
                        var expectation = at as Dictionary<string, object>;
                        if (expectation == null || !expectation.ContainsKey("tolerance")) continue;
                        if (!expectation.ContainsKey("tolerance_why"))
                            withoutWhy.Add(itemName + " / " + location + " / " +
                                            (expectation.ContainsKey("what") ? Convert.ToString(expectation["what"]) : "?"));
                    }
                };
                object rawAfter;
                if (testCase.TryGetValue("after", out rawAfter)) checkExpectations(rawAfter, "after");

                object raw;
                if (!testCase.TryGetValue("steps", out raw)) continue;
                var steps = raw as object[];
                if (steps == null) continue;
                rawSteps[itemName] = steps;

                int number = 0;
                foreach (var p in steps)
                {
                    var planStep = p as Dictionary<string, object>;
                    number++;
                    if (planStep == null) continue;
                    object at0;
                    if (planStep.TryGetValue("expect", out at0)) checkExpectations(at0, "passo " + number);
                    // UN PASSO CHE NON MISURA NIENTE (25/9/2026): il banco lo
                    // ferma come errore di banco — dopo un'ora e mezza di giro, e
                    // fermando tutto il caso li'. E' successo col caso della
                    // tavola guardata: tre fori senza attese, e le attese della
                    // tavola non sono mai girate. Si vede leggendo il JSON.
                    else if (!planStep.ContainsKey("must_fail")) muteSteps.Add(itemName + " / passo " + number);
                    object opN, argN;
                    string opName = planStep.TryGetValue("operation", out opN) ? Convert.ToString(opN) : "";
                    string argText = planStep.TryGetValue("arguments", out argN) ? js.Serialize(argN) : "{}";
                    List<Tuple<string, string>> itsItems;
                    if (!argumentsPerCase.TryGetValue(itemName, out itsItems)) argumentsPerCase[itemName] = itsItems = new List<Tuple<string, string>>();
                    itsItems.Add(Tuple.Create(opName, argText));
                    List<Tuple<string, Dictionary<string, object>, bool>> stepsHere;
                    if (!caseSteps.TryGetValue(itemName, out stepsHere))
                        caseSteps[itemName] = stepsHere = new List<Tuple<string, Dictionary<string, object>, bool>>();
                    stepsHere.Add(Tuple.Create(opName,
                        argN as Dictionary<string, object> ?? new Dictionary<string, object>(),
                        planStep.ContainsKey("must_fail")));
                    HashSet<string> opCases;
                    if (!casesPerOp.TryGetValue(opName, out opCases))
                        casesPerOp[opName] = opCases = new HashSet<string>(StringComparer.Ordinal);
                    opCases.Add(itemName);
                    // IL CORPO PER NUMERO (Z-C16). Un «C2» scritto a mano in un
                    // campo `_uid`: si esclude solo il numero che il rifiuto
                    // stesso nomina nel messaggio atteso (`C9` che non esiste
                    // e' il punto del caso, e non dipende dall'ordine).
                    foreach (var cited in BodiesByNumber(argN))
                    {
                        object dvC; string expectedInMessage = "";
                        if (planStep.TryGetValue("must_fail", out dvC) && dvC is Dictionary<string, object>)
                            expectedInMessage = js.Serialize(dvC);
                        if (expectedInMessage.Contains("\"" + cited + "\"")) continue;
                        casesWithNumbers.Add(itemName);
                    }
                    object dv0;
                    if (planStep.TryGetValue("must_fail", out dv0) && dv0 is Dictionary<string, object>)
                    {
                        object g0;
                        var gem = ((Dictionary<string, object>)dv0).TryGetValue("twin", out g0) ? Convert.ToString(g0) : null;
                        object dich;
                        bool inPart = ((Dictionary<string, object>)dv0).TryGetValue(
                            Oce.Solid.Agent.Bench.Expectations.DifferenceInPartField, out dich);
                        if (!string.IsNullOrEmpty(gem) && inPart)
                            refusalsInPart.Add(Tuple.Create(itemName, opName,
                                argN as Dictionary<string, object> ?? new Dictionary<string, object>(), gem,
                                number - 1, Convert.ToString(dich)));
                        else if (!string.IsNullOrEmpty(gem))
                        {
                            refusalsToCompare.Add(Tuple.Create(itemName, opName, argText, gem));
                            refusalsWithTwin.Add(Tuple.Create(itemName, opName,
                                argN as Dictionary<string, object> ?? new Dictionary<string, object>(), gem));
                        }
                    }
                    object dv;
                    if (!planStep.TryGetValue("must_fail", out dv)) continue;
                    var refusal = dv as Dictionary<string, object>;
                    if (refusal == null) continue;

                    object op;
                    string operation = planStep.TryGetValue("operation", out op)
                        ? Convert.ToString(op) : "(senza nome)";
                    int howMany;
                    refusedOperations.TryGetValue(operation, out howMany);
                    refusedOperations[operation] = howMany + 1;

                    // ── LA GEMELLA (Z-C11) ───────────────────────────────
                    object g;
                    string twin = refusal.TryGetValue("twin", out g)
                        ? Convert.ToString(g) : null;
                    if (string.IsNullOrEmpty(twin)) withoutTwin.Add(itemName + " / " + operation);
                    else if (!names.Contains(twin))
                        absentTwins.Add(itemName + " -> «" + twin + "» non esiste");

                    // ── E IL MESSAGGIO DEV'ESSERE PRETESO ────────────────
                    //
                    // «deve fallire» senza dire con quale messaggio e' quasi
                    // niente: qualunque errore lo soddisfa, compreso quello
                    // sbagliato. Il banco lo pretende gia'; qui si vede subito.
                    object m;
                    var messages = refusal.TryGetValue("message_includes", out m)
                        ? m as object[] : null;
                    if (messages == null || messages.Length == 0)
                        withoutMessage.Add(itemName + " / " + operation);
                }
            }

            // IL TETTO CALA E NON SALE (Z-C16). I casi che li citano ancora
            // sono l'elenco da guardare uno per uno; un caso NUOVO che cita un
            // corpo per numero fa diventare rosso questo controllo il giorno in
            // cui lo si scrive: si usa `@body(name=...)`.
            Check("i casi che citano un corpo per numero non aumentano (Z-C16)",
                  casesWithNumbers.Count <= BodiesByNumberCap,
                  casesWithNumbers.Count + " casi (tetto " + BodiesByNumberCap + "): " +
                  string.Join(", ", casesWithNumbers));
            Check("e il tetto dei corpi per numero e' quello vero",
                  casesWithNumbers.Count == BodiesByNumberCap,
                  casesWithNumbers.Count == BodiesByNumberCap
                      ? "tetto " + BodiesByNumberCap
                      : "sono " + casesWithNumbers.Count + ": abbassa il tetto a questo numero");

            Check("ogni passo di un caso misura qualcosa (come pretende il banco)", muteSteps.Count == 0,
                  muteSteps.Count == 0 ? "nessun passo senza attese" : string.Join(" | ", muteSteps));

            Check("tutti i file di caso si leggono", unreadable.Count == 0,
                  unreadable.Count == 0
                      ? file.Length + " file (" + declared + " casi, " + planes + " piani)"
                      : string.Join(" | ", unreadable));

            var fake = new List<string>();
            foreach (var r in refusalsToCompare)
            {
                List<Tuple<string, string>> twinSteps;
                if (!argumentsPerCase.TryGetValue(r.Item4, out twinSteps)) continue;
                // UN CASO PUO' ESSERE GEMELLA DI SE STESSO (il passo verde e il
                // rifiuto nello stesso file): li' il rifiuto si trova da solo fra
                // i passi, e va contato una volta di piu'. Visto al primo giro di
                // questo controllo, su `two_sheets_and_the_same_projection_angle`.
                int equal = 0;
                foreach (var p in twinSteps)
                    if (p.Item1 == r.Item2 && p.Item2 == r.Item3) equal++;
                if (equal > (r.Item4 == r.Item1 ? 1 : 0))
                    fake.Add(r.Item1 + " -> " + r.Item4 + " (" + r.Item2 + " " + r.Item3 + ")");
            }
            // E NON BASTA CHE NON SIA IDENTICA: il banco la confronta col PRIMO
            // passo riuscito della stessa operazione e pretende almeno un
            // argomento in comune con lo stesso valore (`Expectations.Resemble`,
            // la stessa funzione che usa lui). Il 25/9/2026 il rifiuto su «V9»
            // aveva in comune «count» col SECONDO passo della gemella e niente
            // col primo: errore di banco dopo cento minuti di giro.
            var far = new List<string>();
            foreach (var r in refusalsWithTwin)
            {
                List<Tuple<string, Dictionary<string, object>, bool>> stepsG;
                if (!caseSteps.TryGetValue(r.Item4, out stepsG)) continue;   // gemella assente: si dice qui sotto
                Dictionary<string, object> itsItems = null;
                foreach (var p in stepsG)
                    if (p.Item1 == r.Item2 && !p.Item3) { itsItems = p.Item2; break; }
                if (itsItems == null)
                {
                    far.Add(r.Item1 + " -> " + r.Item4 + ": nessun passo «" + r.Item2 + "» che riesce");
                    continue;
                }
                string why;
                if (!Oce.Solid.Agent.Bench.Expectations.Resemble(r.Item3, itsItems, out why))
                    far.Add(r.Item1 + " -> " + r.Item4 + ": " + why);
            }
            // Le gemelle dichiarate sul pezzo: il primo passo riuscito della
            // stessa operazione nella gemella, e i passi prima di lui.
            foreach (var r in refusalsInPart)
            {
                object[] stepsG, mySteps;
                if (!rawSteps.TryGetValue(r.Item4, out stepsG) || !rawSteps.TryGetValue(r.Item1, out mySteps)) continue;
                int location = -1;
                for (int k = 0; k < stepsG.Length; k++)
                {
                    var pg = stepsG[k] as Dictionary<string, object>;
                    object opG;
                    if (pg == null || pg.ContainsKey("must_fail") || !pg.TryGetValue("operation", out opG) ||
                        Convert.ToString(opG) != r.Item2) continue;
                    location = k;
                    break;
                }
                if (location < 0)
                {
                    far.Add(r.Item1 + " -> " + r.Item4 + ": nessun passo «" + r.Item2 + "» che riesce");
                    continue;
                }
                object argG;
                ((Dictionary<string, object>)stepsG[location]).TryGetValue("arguments", out argG);
                string why;
                if (!Oce.Solid.Agent.Bench.Expectations.ResembleInPart(r.Item3,
                        argG as Dictionary<string, object> ?? new Dictionary<string, object>(),
                        mySteps.Take(r.Item5).ToList(), stepsG.Take(location).ToList(), r.Item6, out why))
                    far.Add(r.Item1 + " -> " + r.Item4 + ": " + why);
            }

            Check("ogni gemella somiglia al suo rifiuto come la vuole il banco (F5, vista prima del giro)",
                  far.Count == 0,
                  far.Count == 0
                      ? refusalsWithTwin.Count + " coppie guardate col criterio del banco, " +
                        refusalsInPart.Count + " con la differenza dichiarata nel pezzo"
                      : string.Join(" | ", far));

            Check("nessuna gemella e' identica al suo rifiuto (voce F5, vista prima del giro)",
                  fake.Count == 0,
                  fake.Count == 0 ? refusalsToCompare.Count + " rifiuti con gemella confrontati" : string.Join(" | ", fake));

            Check("ogni tolleranza dice perche' (la regola del banco, vista prima del giro)",
                  withoutWhy.Count == 0,
                  withoutWhy.Count == 0
                      ? "nessuna tolleranza muta"
                      : string.Join(" | ", withoutWhy));

            Check("ogni rifiuto dichiara la sua gemella", withoutTwin.Count == 0,
                  withoutTwin.Count == 0
                      ? refusedOperations.Values.Sum() + " rifiuti, tutti con gemella"
                      : string.Join(" | ", withoutTwin));

            Check("e la gemella esiste davvero", absentTwins.Count == 0,
                  absentTwins.Count == 0
                      ? "nessun rimando a un caso che non c'e'"
                      : string.Join(" | ", absentTwins));

            Check("ogni rifiuto pretende un messaggio", withoutMessage.Count == 0,
                  withoutMessage.Count == 0
                      ? "nessun «deve_fallire» che accetti qualunque errore"
                      : string.Join(" | ", withoutMessage));

            // ── E LE DESCRIZIONI? (Z-G5) ────────────────────────────────
            var catalog = ServedCatalog();
            if (!File.Exists(catalog))
            {
                Check("il catalogo servito c'e'", false, "manca " + catalog);
                return;
            }

            var catRoot = serializer.DeserializeObject(File.ReadAllText(catalog))
                            as Dictionary<string, object>;
            object rawEntries;
            var entries = catRoot != null && catRoot.TryGetValue("operations", out rawEntries)
                ? rawEntries as object[] : null;
            if (entries == null)
            {
                Check("il catalogo ha le operazioni", false, "«operations» non e' un elenco");
                return;
            }

            var textOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, d, s;
                o.TryGetValue("name", out n);
                o.TryGetValue("details", out d);
                o.TryGetValue("summary", out s);
                if (n == null) continue;
                textOf[Convert.ToString(n)] =
                    (Convert.ToString(d) + " " + Convert.ToString(s)).ToLowerInvariant();
            }

            var mute = new List<string>();
            var outsideCatalog = new List<string>();
            foreach (var op in refusedOperations.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                string text;
                if (!textOf.TryGetValue(op, out text)) { outsideCatalog.Add(op); continue; }
                bool speaks = false;
                foreach (var indicator in Telltales) if (text.Contains(indicator)) { speaks = true; break; }
                if (!speaks) mute.Add(op + " (" + refusedOperations[op] + ")");
            }

            Check("ogni operazione col rifiuto provato sta a catalogo",
                  outsideCatalog.Count == 0,
                  outsideCatalog.Count == 0
                      ? refusedOperations.Count + " operazioni con almeno un rifiuto provato"
                      : "fuori catalogo: " + string.Join(", ", outsideCatalog));

            // IL TETTO CALA E NON SALE. Una guardia nuova senza la frase che la
            // dichiara fa diventare rosso questo controllo, ed e' il momento in
            // cui serve: dopo, nessuno ci torna piu'.
            Check("le descrizioni mute non sono aumentate", mute.Count <= Mute,
                  mute.Count + " mute su " + refusedOperations.Count +
                  " (tetto " + Mute + "): " + string.Join(", ", mute));

            Check("e il tetto e' quello vero", mute.Count == Mute,
                  mute.Count == Mute
                      ? "zero: ogni operazione col rifiuto provato lo dichiara"
                      : "sono " + mute.Count + ": abbassa il tetto a questo numero");

            EveryOperationHasAGreenCase(
                new HashSet<string>(textOf.Keys, StringComparer.Ordinal), casesPerOp,
                Path.Combine(Root(), "Bench", "report", "last.json"));
        }

        /// <summary>
        /// Le operazioni a catalogo che oggi NON possono avere un caso verde, e
        /// perche'. Ognuna si ricontrolla: se esce dal catalogo o trova un caso
        /// che la usa, il permesso va tolto — come in Z-C7, dove un'eccezione per
        /// una riga che non c'e' piu' e' un buco che resta aperto.
        /// </summary>
        private static readonly Dictionary<string, string> WithoutGreenCase =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // (26/9/2026) `model_dimensions` non e' piu' qui: fuori processo
                // porta le quote (7 sullo stesso pezzo di Z-C1, due sonde), e ha la
                // sua coppia vera: `dimensions/dimensions_come_from_the_model`.
            };

        /// <summary>
        /// LA SECONDA META' DI Z-C6: `Provata = true` deve puntare a un caso che
        /// ha GIRATO, verde, nell'ultimo giro — non a un caso che esiste.
        ///
        /// Il 25/9/2026, scrivendo i casi che mancavano, due operazioni «provate»
        /// si sono scoperte mai funzionanti (`trim_members`, `weld_bead`)
        /// e una e' uscita dal catalogo (`gusset`). «Provata» voleva dire che
        /// qualcuno ci aveva scritto true. Adesso lo dice il banco: scrive i nomi
        /// dei casi verdi in `last.json` («green_cases»), e qui si incrociano
        /// col catalogo servito.
        /// </summary>
        private static void EveryOperationHasAGreenCase(
            HashSet<string> inCatalog, Dictionary<string, HashSet<string>> casesPerOp, string lastJson)
        {
            if (!File.Exists(lastJson))
            {
                Check("il banco dice quali casi sono verdi (Z-C6)", false, "manca " + lastJson);
                return;
            }
            var last = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(lastJson))
                         as Dictionary<string, object>;
            object rawItems;
            var list = last != null && last.TryGetValue("green_cases", out rawItems) ? rawItems as object[] : null;
            if (list == null)
            {
                Check("il banco dice quali casi sono verdi (Z-C6)", false,
                      "`last.json` non ha «green_cases»: l'ultimo giro e' di un banco che non " +
                      "li scriveva ancora. Rilancia il banco.");
                return;
            }
            var green = new HashSet<string>(list.Select(x => Convert.ToString(x)), StringComparer.Ordinal);
            Check("il banco dice quali casi sono verdi (Z-C6)", green.Count > 0,
                  green.Count + " casi verdi nell'ultimo giro");

            var without = new List<string>();
            foreach (var op in inCatalog.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (WithoutGreenCase.ContainsKey(op)) continue;
                HashSet<string> itsItems;
                if (!casesPerOp.TryGetValue(op, out itsItems)) { without.Add(op + " (nessun caso)"); continue; }
                if (!itsItems.Any(green.Contains))
                    without.Add(op + " (" + string.Join(", ", itsItems.OrderBy(x => x, StringComparer.Ordinal)) +
                              ": nessuno verde nell'ultimo giro)");
            }
            Check("ogni operazione a catalogo ha un caso VERDE nell'ultimo giro (Z-C6)",
                  without.Count == 0,
                  without.Count == 0
                      ? (inCatalog.Count - WithoutGreenCase.Count) + " su " + inCatalog.Count + ", piu' " +
                        WithoutGreenCase.Count + " eccezioni dichiarate col perche'"
                      : string.Join(" | ", without));

            var expired = new List<string>();
            foreach (var kv in WithoutGreenCase)
            {
                if (!inCatalog.Contains(kv.Key)) expired.Add(kv.Key + ": non e' piu' a catalogo, togli l'eccezione");
                else if (casesPerOp.ContainsKey(kv.Key)) expired.Add(kv.Key + ": adesso ha un caso, togli l'eccezione");
            }
            Check("e le eccezioni valgono ancora (Z-C6)", expired.Count == 0,
                  expired.Count == 0 ? string.Join(", ", WithoutGreenCase.Keys) : string.Join(" | ", expired));
        }
    }
}
