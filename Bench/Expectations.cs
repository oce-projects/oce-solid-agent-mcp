using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// IL VALUTATORE DELLE ATTESE E I SELETTORI, SENZA SOLIDWORKS (voce F7).
    ///
    /// ── PERCHE' STA IN UNA CLASSE SUA ────────────────────────────────────
    ///
    /// Questo file lo compilano DUE progetti: il banco, che apre il CAD, e
    /// `Oce.Solid.Agent.ContractTests`, che gira su una macchina qualunque in pochi secondi e
    /// **non referenzia l'interop di SOLIDWORKS**. Finche' il valutatore stava
    /// in una classe parziale che tocca `IModelDoc2`, il secondo progetto non
    /// poteva nemmeno compilare.
    ///
    /// E deve poter girare li': **se e' rotto il valutatore, tutta la suite col
    /// CAD diventa verde gratis.** E' l'ultimo posto che ci si puo' permettere
    /// di non provare.
    ///
    /// Le misure che vengono dal DOCUMENTO — volume, corpi, ingombro — arrivano
    /// da un `Func&lt;string, object&gt;`: chi ha il documento in mano lo passa, chi
    /// non ce l'ha passa null e prova tutto il resto.
    /// </summary>
    internal static class Expectations
    {
        /// <summary>
        /// Quello che una prova non ha potuto misurare: un campo assente, un
        /// selettore che non risolve, un caso scritto male (voce F4).
        /// </summary>
        internal sealed class CouldNotMeasure : Exception
        {
            public CouldNotMeasure(string message) : base(message) { }
        }

        /// <summary>
        /// Dove sta la libreria dei profili saldati. Si dice con
        /// `PROFILI_SALDATURA`, come nel banco e come nella libreria.
        ///
        /// QUI C'ERA IL PERCORSO DI UNA POSTAZIONE SOLA, come ripiego. Non ha
        /// mai aiutato nessun altro - su ogni altra macchina quella cartella
        /// non esiste - e intanto raccontava com'e' fatto quel disco. Adesso
        /// vuoto e' vuoto, e chi chiama lo deve dire.
        /// </summary>
        internal static string Profiles
        {
            get
            {
                var its = Environment.GetEnvironmentVariable("PROFILI_SALDATURA");
                return string.IsNullOrWhiteSpace(its) ? null : its;
            }
        }

        /// <summary>
        /// DOVE UN CASO PUO' SCRIVERE. `Bench\lavoro\`, la stessa cartella in
        /// cui `CloseLeftovers` butta i documenti di scarto: e' ignorata da git e
        /// si puo' svuotare in qualunque momento.
        ///
        /// Serve da quando ci sono operazioni che producono FILE —
        /// `export_stl`, `export_pdf`, `save_document`. Un caso che scrive
        /// in una cartella sua la riempirebbe di roba che nessuno pulisce; un
        /// caso che scrive accanto al repository sporcherebbe `git status`.
        /// La cartella si crea se non c'e': un caso non deve fallire perche'
        /// qualcuno ha svuotato il cestino.
        /// </summary>
        internal static string Work
        {
            get
            {
                // A LOCAL folder, not `Bench\lavoro` next to the repository: the
                // main checkout sits on a network drive, and on 3/10/2026 a
                // SaveAs3 there answered e=1 and left no file, a bench error
                // that cost a whole run. OCE_BENCH_WORK names another folder.
                var its = Environment.GetEnvironmentVariable("OCE_BENCH_WORK");
                var location = Path.GetFullPath(!string.IsNullOrWhiteSpace(its)
                    ? its
                    : Path.Combine(Path.GetTempPath(), "oce-bench-work"));
                try { if (!Directory.Exists(location)) Directory.CreateDirectory(location); } catch { }
                return location;
            }
        }

        /// <summary>
        /// LA CARTELLA DI INSTALLAZIONE DI SOLIDWORKS, per i file di serie che
        /// un caso deve poter nominare senza copiarli nel repository (voce
        /// Z-C21.2, revisione: il modello di distinta di taglio «cut
        /// list.sldwldtbt», che e' di Dassault e non si versiona). Si dice con
        /// `SW_INSTALL_PATH`, la stessa di build.ps1; senza, si cerca dove la mette l'installazione
        /// predefinita, riconoscendola da SLDWORKS.exe. Se non si trova, il caso
        /// non si puo' misurare — non e' rosso.
        /// </summary>
        internal static string SolidWorksFolder
        {
            get
            {
                var its = Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
                if (!string.IsNullOrWhiteSpace(its)) return its;
                foreach (var pf in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetEnvironmentVariable("ProgramW6432"),
                })
                {
                    if (string.IsNullOrWhiteSpace(pf)) continue;
                    var d = Path.Combine(pf, "SOLIDWORKS Corp", "SOLIDWORKS");
                    try { if (File.Exists(Path.Combine(d, "SLDWORKS.exe"))) return d; } catch { }
                }
                return null;
            }
        }

        /// <summary>
        /// I FILE CHE UN CASO LEGGE E NON SCRIVE: `Bench\data\`, versionata (voce
        /// Z-F5, 27/9/2026: l'immagine da ricalcare). `{LAVORO}` si svuota a ogni
        /// giro e non e' nel repository, quindi un file d'ingresso non puo' starci.
        /// </summary>
        internal static string Data
        {
            get
            {
                var here = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetEntryAssembly() != null
                        ? System.Reflection.Assembly.GetEntryAssembly().Location
                        : System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.GetFullPath(Path.Combine(here ?? ".", "..", "data"));
            }
        }

        /// <summary>Un testo lungo, accorciato per stare su una riga di esito.</summary>
        internal static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(nessun motivo)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        // ==================================================================
        // LE ATTESE
        // ==================================================================
        internal static bool Matches(
            Dictionary<string, object> expectation, Func<string, object> fromDocument,
            Dictionary<string, object> docState, Dictionary<string, object> measurements,
            out string detail)
        {
            detail = "";
            string what = Text(expectation, "what");
            if (what == null) throw new CouldNotMeasure("un'attesa senza «what»");

            // ── QUANDO LA MISURA E' UNA FRASE ─────────────────────────────
            //
            // `state.shape` non e' un numero: e' la descrizione che il pezzo
            // da' di se stesso — «una rivoluzione del profilo Ø30 per 40, Ø20
            // per 30, Ø34 per 20». Pretenderla UGUALE vorrebbe dire riscrivere
            // il caso a ogni virgola sistemata; quello che conta e' che i
            // pezzi che contano ci siano, e che quelli che NON devono esserci
            // non ci siano — la seconda meta' e' la piu' importante, perche'
            // e' come si prende una frase che dichiara un profilo che il pezzo
            // non ha piu'.
            bool includes = expectation.ContainsKey("includes");
            bool doesNotContain = expectation.ContainsKey("does_not_include");
            if (includes || doesNotContain)
            {
                if (expectation.ContainsKey("equal_to"))
                    throw new CouldNotMeasure("«" + what + "» ha «equal_to» insieme a " +
                                        "«includes»/«does_not_include»: decidi quale dei due");
                return Sentence(what, expectation, fromDocument, docState, measurements, out detail);
            }

            if (!expectation.ContainsKey("equal_to")) throw new CouldNotMeasure("«" + what + "» senza «equal_to»");

            double tolerance = 0;
            if (expectation.ContainsKey("tolerance") && expectation["tolerance"] != null)
            {
                tolerance = Convert.ToDouble(expectation["tolerance"], CultureInfo.InvariantCulture);
                // IL PERCHE' E' OBBLIGATORIO, e non e' burocrazia: una
                // tolleranza senza motivo e' il posto in cui un giorno qualcuno
                // allarghera' il numero invece di guardare il pezzo.
                if (Math.Abs(tolerance) > 1e-12 && Text(expectation, "tolerance_why") == null)
                    throw new CouldNotMeasure("«" + what + "» ha tolleranza " +
                                        tolerance.ToString(CultureInfo.InvariantCulture) +
                                        " e non dice «tolerance_why»");
            }

            object measured = Measure(what, fromDocument, docState, measurements);
            object expected = expectation["equal_to"];

            // Un elenco di numeri: l'ingombro, un punto, una terna.
            var expectedList = expected as System.Collections.IEnumerable;
            if (expectedList != null && !(expected is string))
            {
                var expectedValues = Numbers(expectedList);

                // ── UN ELENCO CHE NUMERI NON NE HA ───────────────────────
                //
                // `["C2"]`, i corpi su cui una booleana ha lavorato. Fino alla
                // 0.81.0 finiva in «non e' un elenco di numeri:
                // System.String[]» — un messaggio vero e inutile: il caso
                // chiedeva una cosa sensata e il valutatore non sapeva
                // confrontarla. Un elenco di TESTO si confronta a uno a uno,
                // con la stessa scrittura che serve alle gemelle.
                if (expectedValues == null)
                {
                    var measuredList = measured as System.Collections.IEnumerable;
                    if (measuredList == null || measured is string)
                    {
                        detail = what + " non e' un elenco: " + WhatWas(measured);
                        return false;
                    }
                    var q = new List<string>();
                    foreach (var x in expectedList) q.Add(HowItIsWritten(x));
                    var r = new List<string>();
                    foreach (var x in measuredList) r.Add(HowItIsWritten(x));
                    detail = what + " [" + string.Join(", ", r) + "] (atteso [" +
                                string.Join(", ", q) + "])";
                    if (q.Count != r.Count) return false;
                    for (int i = 0; i < q.Count; i++)
                        if (!string.Equals(q[i], r[i], StringComparison.Ordinal)) return false;
                    return true;
                }

                var measuredValues = Numbers(measured as System.Collections.IEnumerable);
                if (measuredValues == null)
                {
                    detail = what + " non e' un elenco di numeri: " + WhatWas(measured);
                    return false;
                }
                detail = what + " " + Set(measuredValues) + " (atteso " + Set(expectedValues) +
                            (tolerance > 0 ? " ±" + tolerance.ToString(CultureInfo.InvariantCulture) : "") + ")";
                if (expectedValues.Length != measuredValues.Length) return false;
                for (int i = 0; i < expectedValues.Length; i++)
                    if (Math.Abs(expectedValues[i] - measuredValues[i]) > tolerance) return false;
                return true;
            }

            // Un numero.
            double expectedNumber;
            if (expected != null && !(expected is string) && !(expected is bool) &&
                double.TryParse(Convert.ToString(expected, CultureInfo.InvariantCulture),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out expectedNumber))
            {
                double measuredNumber;
                if (measured == null ||
                    !double.TryParse(Convert.ToString(measured, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out measuredNumber))
                {
                    detail = what + " non e' un numero: " + WhatWas(measured);
                    return false;
                }
                detail = string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:0.###} (atteso {2:0.###}{3})", what, measuredNumber, expectedNumber,
                    tolerance > 0 ? " ±" + tolerance.ToString(CultureInfo.InvariantCulture) : "");
                return Math.Abs(measuredNumber - expectedNumber) <= tolerance;
            }

            // Tutto il resto si confronta come testo: «material», un booleano.
            string a = Convert.ToString(expected, CultureInfo.InvariantCulture);
            string m = Convert.ToString(measured, CultureInfo.InvariantCulture);
            detail = what + " «" + m + "» (atteso «" + a + "»)";
            return string.Equals(a, m, StringComparison.Ordinal);
        }

        /// <summary>
        /// UN'ATTESA SU UNA FRASE: quali pezzi ci devono essere, e quali no.
        ///
        /// `does_not_include` non e' la comodita' simmetrica di `includes`: e' il
        /// motivo per cui questo esiste. Una frase che dichiara «il profilo
        /// Ø30 per 40» su un pezzo che quel profilo non ce l'ha piu' e' il
        /// difetto che non da' errore, e si prende solo pretendendo che una
        /// parola SPARISCA.
        ///
        /// Il confronto e' esatto — maiuscole e accenti compresi — perche' una
        /// frase che il modello legge cambia di senso con una lettera.
        /// </summary>
        private static bool Sentence(
            string what, Dictionary<string, object> expectation, Func<string, object> fromDocument,
            Dictionary<string, object> docState, Dictionary<string, object> measurements,
            out string detail)
        {
            object measured = Measure(what, fromDocument, docState, measurements);
            if (measured == null)
            {
                detail = what + " non c'e': una frase assente non e' una frase vuota";
                return false;
            }

            // ── UN ELENCO NON E' «System.String[]» ───────────────────────
            //
            // `measurements.profiles[0].measurements` e' un elenco di misure: «80X80X5»,
            // «100X100X6». Chiedere che CONTENGA una voce e' la cosa piu'
            // naturale che ci sia, e fino alla 0.91.0 falliva con
            //
            //     non dice «80X80X5»: System.String[]
            //
            // — un messaggio vero e inutile, perche' `Convert.ToString` su un
            // array da' il nome del tipo. Per `equal_to` gli elenchi di testo
            // erano gia' stati insegnati (voce 0.81.0); per `includes` no, e il
            // difetto e' rimasto finche' un caso non l'ha ripreso.
            //
            // Su un elenco «includes» vuol dire **una delle voci lo contiene**,
            // non «la scrittura dell'elenco lo contiene»: cosi' cercare
            // «80X80X5» trova la voce, e cercare «80X80» trova la stessa voce
            // per prefisso, che e' quello che chi scrive il caso si aspetta.
            var entries = Items(measured);
            bool isList = entries != null;
            string sentence = isList
                ? string.Join(", ", entries)
                : Convert.ToString(measured, CultureInfo.InvariantCulture);

            var areMissing = new List<string>();
            foreach (var p in Parts(expectation, "includes"))
                if (!Inside(entries, sentence, p)) areMissing.Add("«" + p + "»");

            var tooMuch = new List<string>();
            foreach (var p in Parts(expectation, "does_not_include"))
                if (Inside(entries, sentence, p)) tooMuch.Add("«" + p + "»");

            if (areMissing.Count == 0 && tooMuch.Count == 0)
            {
                detail = what + (isList ? " [" + Short(sentence) + "]" : ": " + Short(sentence));
                return true;
            }
            detail = what +
                (areMissing.Count > 0 ? " non dice " + string.Join(", ", areMissing) : "") +
                (areMissing.Count > 0 && tooMuch.Count > 0 ? ", e" : "") +
                (tooMuch.Count > 0 ? " dice ancora " + string.Join(", ", tooMuch) : "") +
                ": " + Short(sentence);
            return false;
        }

        /// <summary>
        /// Le voci di un elenco, gia' scritte. Null quando cio' che si e'
        /// misurato non e' un elenco — e una stringa NON e' un elenco, anche
        /// se in C# e' enumerabile carattere per carattere.
        /// </summary>
        private static List<string> Items(object measured)
        {
            if (measured == null || measured is string) return null;
            var e = measured as System.Collections.IEnumerable;
            if (e == null) return null;
            var outside = new List<string>();
            foreach (var x in e) outside.Add(HowItIsWritten(x));
            return outside;
        }

        /// <summary>
        /// Il pezzo cercato c'e'? Su un elenco basta che lo contenga UNA voce;
        /// su una frase, la frase.
        /// </summary>
        /// <summary>
        /// SENZA GUARDARE LE MAIUSCOLE, e il perche' e' costato tre volte.
        ///
        /// ── I TRE INCIDENTI ──────────────────────────────────────────────
        ///
        ///   21/9  un'attesa cercava «vero», il testo diceva «VERO»
        ///   23/9  cercava «accavallano», il testo dice «si accavalla»
        ///   23/9  cercava «che serviva», il testo dice «CHE SERVIVA»
        ///
        /// Il secondo era una desinenza, e si e' riparato cercando la radice.
        /// Il primo e il terzo sono la stessa cosa, e non erano difetti dei
        /// casi: `includes` guarda PROSA, scritta per una persona, e in questo
        /// repository l'enfasi si fa alzando la voce — cioe' cambiando le
        /// maiuscole. Un'attesa che si rompe quando una frase viene riscritta
        /// con piu' enfasi non sta misurando cio' che dice: sta misurando come
        /// e' scritta.
        ///
        /// *Tre incidenti dalla stessa causa non sono tre casi da riparare: e'
        /// un operatore da riparare.*
        ///
        /// ── E `does_not_include` DIVENTA PIU' SEVERO ─────────────────────────
        ///
        /// Cambiano insieme, ed e' il verso scomodo: un «non dice X» che
        /// passava solo perche' il testo scriveva «x» adesso diventa rosso. E'
        /// giusto che lo diventi — quel verde era un caso fortunato, non una
        /// misura — ed e' il motivo per cui questa modifica si e' fatta con un
        /// giro di banco intero addosso invece che a occhio.
        ///
        /// ── CHI VUOLE DAVVERO LE MAIUSCOLE ───────────────────────────────
        ///
        /// Non ha un operatore, e va detto invece di lasciarlo scoprire.
        /// Nessun caso oggi ne ha bisogno: si e' guardato prima di togliere il
        /// confronto esatto. Il giorno che servisse, si aggiunge
        /// `contiene_esatto` — un nome diverso, non un booleano in piu' su
        /// questo, perche' un interruttore che cambia il senso di un operatore
        /// e' un operatore travestito.
        /// </summary>
        private static bool Inside(List<string> entries, string sentence, string part)
        {
            if (entries != null)
                return entries.Any(v => v.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
            return sentence.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>I pezzi di frase di un campo: una stringa sola o un elenco.</summary>
        private static List<string> Parts(Dictionary<string, object> expectation, string theField)
        {
            var outside = new List<string>();
            object v;
            if (!expectation.TryGetValue(theField, out v) || v == null) return outside;
            if (v is string) { outside.Add((string)v); return outside; }
            var list = v as System.Collections.IEnumerable;
            if (list == null)
            { outside.Add(Convert.ToString(v, CultureInfo.InvariantCulture)); return outside; }
            foreach (var x in list)
                outside.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            if (outside.Count == 0)
                throw new CouldNotMeasure("«" + theField + "» e' un elenco vuoto: " +
                                    "non chiede niente, e passerebbe sempre");
            return outside;
        }

        /// <summary>
        /// Da dove si legge un `what`. Tre sorgenti, e il prefisso lo dice:
        /// niente = quello che misura il banco per conto suo, `state.` = lo
        /// stato dell'add-in, `measurements.` = quello che l'operazione ha riportato.
        /// </summary>
        internal static object Measure(
            string what, Func<string, object> fromDocument,
            Dictionary<string, object> docState, Dictionary<string, object> measurements)
        {
            // LE TRE MISURE DEL DOCUMENTO ARRIVANO DA FUORI, e chi non ha un
            // documento in mano passa null: e' quello che rende questo file
            // compilabile dove SOLIDWORKS non c'e' (voce F7).
            // NON C'E' PIU' UN ELENCO DI NOMI QUI.
            //
            // Ce n'era uno, e si e' scollato: una misura nuova aggiunta a chi
            // misura, e dimenticata qui, usciva «non so misurare» con l'elenco
            // vecchio accanto — un messaggio che dice il falso su se stesso.
            // Tutto cio' che non ha un prefisso lo si chiede a chi il documento
            // ce l'ha, ed e' lui a dire se quel nome non lo conosce.
            if (!what.StartsWith("state.", StringComparison.Ordinal) &&
                !what.StartsWith("measurements.", StringComparison.Ordinal))
            {
                if (fromDocument == null)
                    throw new CouldNotMeasure(
                        "«" + what + "» si misura sul documento, e qui non ce n'e' uno");
                return fromDocument(what);
            }
            if (what.StartsWith("state.", StringComparison.Ordinal))
                return Inside(docState, what.Substring("state.".Length), what);
            return Inside(measurements, what.Substring("measurements.".Length), what);
        }

        /// <summary>
        /// UN VALORE COME STRINGA, ANCHE QUANDO E' UN ELENCO.
        ///
        /// `Convert.ToString` su un `object[]` torna «System.Object[]»: DUE
        /// PROFILI DIVERSI SI SCRIVONO UGUALI. Il confronto fra un caso che
        /// deve fallire e la sua gemella si faceva cosi', e diventava cieco
        /// proprio sull'argomento che li distingue. La cecita' va in tutte e
        /// due le direzioni: qui ha dato un errore di banco onesto — «e'
        /// identica al rifiuto» su una coppia che differiva SOLO nei contorni —
        /// ma la stessa riga avrebbe detto «differisce per 1 argomento»
        /// guardando due liste che non aveva letto.
        ///
        /// Sta qui, e non accanto a chi la usa, perche' e' una funzione pura
        /// su un dizionario: `Oce.Solid.Agent.ContractTests` la prova in un millesimo di
        /// secondo, senza CAD (voce F7).
        /// </summary>
        internal static string HowItIsWritten(object v)
        {
            if (v == null) return "(niente)";
            if (v is string) return (string)v;

            var dictionary = v as Dictionary<string, object>;
            if (dictionary != null)
            {
                var entries = new List<string>();
                foreach (var k in dictionary.Keys.OrderBy(x => x, StringComparer.Ordinal))
                    entries.Add(k + "=" + HowItIsWritten(dictionary[k]));
                return "{" + string.Join(", ", entries) + "}";
            }

            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                var parts = new List<string>();
                foreach (var x in list) parts.Add(HowItIsWritten(x));
                return "[" + string.Join(", ", parts) + "]";
            }

            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// LA GEMELLA HA A CHE VEDERE COL RIFIUTO? (voce F5)
        ///
        /// La regola: **argomenti non identici, e almeno un argomento in comune
        /// con lo stesso valore.** La voce proponeva «differisce per UN solo
        /// argomento»; misurata sulle coppie canoniche ne rifiutava tre su
        /// quattro, e chi chiama dice quanti ne differiscono invece di
        /// decidere al posto di chi legge.
        ///
        /// ── L'ECCEZIONE DELL'ARGOMENTO UNICO, CHE E' ARITMETICA ───────────
        ///
        /// `create_axis` prende `face_uid` e basta. Due casi che differiscono
        /// per quell'unico argomento NON POSSONO averne uno in comune: la
        /// regola li rifiuterebbe sempre, e non perche' siano scollegati ma
        /// perche' l'operazione ha un argomento solo. Rifiutarli vorrebbe dire
        /// vietare la coppia a meta' del catalogo. Quando le chiavi in gioco
        /// sono una sola, «niente in comune» non e' un indizio: e' l'arita'.
        /// </summary>
        internal static bool Resemble(
            Dictionary<string, object> mine, Dictionary<string, object> itsItems,
            out string why)
        {
            mine = mine ?? new Dictionary<string, object>();
            itsItems = itsItems ?? new Dictionary<string, object>();

            var keys = new HashSet<string>(mine.Keys, StringComparer.Ordinal);
            foreach (var k in itsItems.Keys) keys.Add(k);

            int different = 0, equal = 0;
            var whichOnes = new List<string>();
            foreach (var k in keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                string a = mine.ContainsKey(k) ? HowItIsWritten(mine[k]) : null;
                string b = itsItems.ContainsKey(k) ? HowItIsWritten(itsItems[k]) : null;
                if (string.Equals(a, b, StringComparison.Ordinal)) equal++;
                else { different++; whichOnes.Add(k); }
            }

            if (different == 0)
            { why = "e' identica al rifiuto: non e' una gemella, e' una copia"; return false; }

            if (equal == 0 && keys.Count > 1)
            {
                why = "non ha nemmeno un argomento in comune col rifiuto: " +
                         "sono due operazioni che non si parlano";
                return false;
            }

            why = "stessa operazione, differisce per " + different +
                     (different == 1 ? " argomento (" : " argomenti (") +
                     string.Join(", ", whichOnes) + ")";
            if (equal == 0)
                why += ", che e' l'unico che questa operazione prende";
            return true;
        }

        /// <summary>
        /// Il campo di «must_fail» che dichiara una gemella con gli STESSI
        /// argomenti su un ALTRO pezzo. Il valore e' la frase che dice cosa
        /// cambia nel pezzo: vuota non vale.
        /// </summary>
        internal const string DifferenceInPartField = "difference_is_the_part";

        /// <summary>
        /// LA GEMELLA CON GLI STESSI ARGOMENTI, SU UN ALTRO PEZZO (voce F5,
        /// 28/9/2026, nervatura W1.2).
        ///
        /// Ci sono rifiuti che non dipendono da un argomento ma dal PEZZO: la
        /// stessa nervatura (stessa linea, stesso piano, stesso spessore) nasce
        /// nella scatola svuotata e non nasce sulla piastra senza pareti. La
        /// regola di `Resemble` pretende un argomento diverso, e prima di
        /// questa funzione l'unico modo di rispettarla era scriverne uno che non
        /// fa niente (un `distance_mm: 0` che e' il predefinito): F5 verde senza
        /// provare niente.
        ///
        /// Qui la differenza si DICHIARA (<see cref="DifferenceInPartField"/>)
        /// e si CONTROLLA, in tutti e due i versi:
        ///   1. la frase c'e';
        ///   2. gli argomenti sono IDENTICI — se differiscono, la regola normale
        ///      basta e la dichiarazione e' di troppo (o nasconde una coppia
        ///      lasca);
        ///   3. i passi PRIMA (il pezzo) differiscono — se sono uguali, stesso
        ///      pezzo e stessi argomenti: e' una copia, come prima.
        ///
        /// I passi si confrontano come si scrivono (operazione e argomenti),
        /// senza CAD: e' aritmetica sul JSON, e la prova Oce.Solid.Agent.ContractTests.
        /// </summary>
        internal static bool ResembleInPart(
            Dictionary<string, object> mine, Dictionary<string, object> itsItems,
            IList<object> myPart, IList<object> itsPart, string declaration,
            out string why)
        {
            if (string.IsNullOrWhiteSpace(declaration))
            {
                why = "«" + DifferenceInPartField + "» e' vuoto: deve dire cosa cambia nel pezzo " +
                         "fra il rifiuto e la gemella";
                return false;
            }

            string onArguments;
            if (Resemble(mine, itsItems, out onArguments))
            {
                why = "dichiara che la differenza e' il pezzo, ma gli argomenti differiscono gia' (" +
                         onArguments + "): la regola normale basta, togli «" + DifferenceInPartField + "»";
                return false;
            }
            if (onArguments.IndexOf("copia", StringComparison.Ordinal) < 0)
            {
                why = "dichiara che la differenza e' il pezzo, ma gli argomenti non sono gli stessi: " +
                         onArguments;
                return false;
            }

            var myOwn = (myPart ?? new List<object>()).Select(HowAStepIsWritten).ToList();
            var its = (itsPart ?? new List<object>()).Select(HowAStepIsWritten).ToList();
            int firstDifferent = -1;
            for (int i = 0; i < Math.Max(myOwn.Count, its.Count); i++)
            {
                string a = i < myOwn.Count ? myOwn[i] : null, b = i < its.Count ? its[i] : null;
                if (!string.Equals(a, b, StringComparison.Ordinal)) { firstDifferent = i; break; }
            }
            if (firstDifferent < 0)
            {
                why = "stessi argomenti E stesso pezzo (i " + myOwn.Count + " passi prima sono uguali): " +
                         "non e' una gemella, e' una copia";
                return false;
            }

            why = "stessi argomenti, pezzo diverso dal passo " + (firstDifferent + 1) + " (" +
                     declaration.Trim() + ")";
            return true;
        }

        /// <summary>Un passo di un caso come si scrive: operazione e argomenti.</summary>
        private static string HowAStepIsWritten(object planStep)
        {
            var p = planStep as Dictionary<string, object>;
            if (p == null) return HowItIsWritten(planStep);
            object op, arg;
            p.TryGetValue("operation", out op);
            p.TryGetValue("arguments", out arg);
            return HowItIsWritten(op) + " " + HowItIsWritten(arg);
        }

        /// <summary>
        /// Un percorso dentro un dizionario: `faces[0].area_mm2`.
        /// Null quando il percorso non porta da nessuna parte — e chi chiama lo
        /// fa diventare un rosso, perche' un campo assente non e' uno zero.
        /// </summary>
        internal static object Inside(object root, string filePath, string fullName)
        {
            object here = root;
            foreach (var part in filePath.Split('.'))
            {
                string key = part;
                int index = -1;
                int bracket = part.IndexOf('[');
                if (bracket >= 0 && part.EndsWith("]", StringComparison.Ordinal))
                {
                    key = part.Substring(0, bracket);
                    int.TryParse(part.Substring(bracket + 1, part.Length - bracket - 2), out index);
                }

                var map = here as Dictionary<string, object>;
                if (map == null)
                    throw new CouldNotMeasure(
                        "campo assente: " + fullName + "; «" + key +
                        "» andrebbe cercato dentro qualcosa che non e' un oggetto");

                // LA CHIAVE CHE NON C'E' E UN VALORE NULL NON SONO LA STESSA
                // COSA, ed e' tutta la voce F4. `material: null` vuol dire
                // «quel posto esiste ed e' vuoto»; una chiave che manca vuol
                // dire che il caso sta leggendo un campo che non esiste piu' —
                // e un `null` confrontato con un `null` passerebbe.
                if (!map.ContainsKey(key))
                    throw new CouldNotMeasure(
                        "campo assente: " + fullName + "; ci sono: " +
                        string.Join(", ", map.Keys.OrderBy(k => k, StringComparer.Ordinal)));
                here = map[key];

                if (index >= 0)
                {
                    var itemList = here as System.Collections.IList;
                    if (itemList == null)
                        throw new CouldNotMeasure(
                            "campo assente: " + fullName + "; «" + key + "» non e' un elenco");
                    if (index >= itemList.Count)
                        throw new CouldNotMeasure(
                            "campo assente: " + fullName + "; «" + key + "» ha " +
                            itemList.Count + " voci e il caso chiede la numero " + index);
                    here = itemList[index];
                }
            }
            return here;
        }

        // ==================================================================
        // I SELETTORI
        // ==================================================================
        internal static object Resolve(object raw, Dictionary<string, object> docState)
        {
            var map = raw as Dictionary<string, object>;
            if (map != null)
            {
                var outside = new Dictionary<string, object>();
                foreach (var v in map) outside[v.Key] = Resolve(v.Value, docState);
                return outside;
            }
            var itemList = raw as System.Collections.IList;
            if (itemList != null)
            {
                var outside = new List<object>();
                foreach (var v in itemList) outside.Add(Resolve(v, docState));
                return outside;
            }
            var text = raw as string;
            if (text != null && text.StartsWith("@", StringComparison.Ordinal))
                return Select(text, docState);

            // I SEGNAPOSTO: un caso che nomina un percorso di QUESTA macchina
            // non e' una prova, e' un ricordo. La libreria dei profili saldati
            // sta dove la mette ogni installazione, e il caso dice «{PROFILI}»
            // invece di sapere dov'e'.
            if (text != null && text.Contains("{PROFILI}"))
            {
                // `String.Replace(a, null)` solleva `ArgumentNullException`, e
                // da qui uscirebbe come un guasto del banco invece che come la
                // cosa che e': una precondizione che manca. Si dice.
                if (Profiles == null)
                    throw new CouldNotMeasure(
                        "Questo caso nomina {PROFILI}, ma PROFILI_SALDATURA non e' " +
                        "impostata e non c'e' un percorso predefinito. La libreria " +
                        "dei profili e' di chi lavora, non del repository: dille dov'e'.");
                return text.Replace("{PROFILI}", Profiles);
            }
            // `{LAVORO}`: dove un caso puo' scrivere un file senza sporcare
            // ne' il repository ne' il disco di chi lavora.
            if (text != null && text.Contains("{LAVORO}"))
                return text.Replace("{LAVORO}", Work);
            // `{DATI}`: i file d'ingresso versionati (Bench\data\).
            if (text != null && text.Contains("{DATI}"))
                return text.Replace("{DATI}", Data);
            // `{SOLIDWORKS}`: i file DI SERIE dell'installazione (i modelli di
            // tabella), che non sono ne' del repository ne' di chi lavora.
            if (text != null && text.Contains("{SOLIDWORKS}"))
            {
                var sw = SolidWorksFolder;
                if (sw == null)
                    throw new CouldNotMeasure(
                        "Questo caso nomina {SOLIDWORKS}, ma non trovo la cartella di " +
                        "installazione di SOLIDWORKS: dilla con SW_INSTALL_PATH.");
                return text.Replace("{SOLIDWORKS}", sw);
            }
            return raw;
        }

        /// <summary>
        /// THE SELECTORS OF BEFORE W3.1-bis.5 (decision D4): a selector written with
        /// the names of before the rename is read as its English spelling. Set by the bench when it loads the
        /// library (Oce.Solid.Agent.Core.Alias.Selector, the name map itself); the
        /// contracts leave it null and read selectors as written. Each name it
        /// translates goes into <see cref="DeprecatedSelectors"/>, which the
        /// runner of the cases adds to the step's `measurements.deprecated`.
        /// Removed in W3.1-bis.11.
        /// </summary>
        internal static Func<string, List<string>, string> SelectorAlias;
        internal static readonly List<string> DeprecatedSelectors = new List<string>();

        /// <summary>
        /// How many Italian names the library has translated since the bench
        /// started (Oce.Solid.Agent.Core.Alias.Uses): every step, a failed one
        /// too, every selector of a step or of an expectation, every step that
        /// prepares the components of an assembly. The bench counts the
        /// difference over each case. Null with a library from before the
        /// aliases. Removed in W3.1-bis.11.
        /// </summary>
        internal static Func<int> AliasUsesNow;

        internal static string Select(string expression, Dictionary<string, object> docState)
        {
            if (SelectorAlias != null) expression = SelectorAlias(expression, DeprecatedSelectors);
            int opens = expression.IndexOf('(');
            if (opens < 0 || !expression.EndsWith(")", StringComparison.Ordinal))
                throw new CouldNotMeasure("selettore storto: «" + expression + "»");
            string typ = expression.Substring(1, opens - 1);
            var conditions = Conditions(expression.Substring(opens + 1, expression.Length - opens - 2));

            switch (typ)
            {
                // `n=` VALE PER TUTTI, non solo per le feature: fra quattro
                // spigoli lunghi uguali non esiste nessun criterio, e chi
                // scrive il caso l'ordine puo' deciderlo — purche' lo DICA.
                // Senza `n=` l'ambiguita' resta un rifiuto.
                case "face":  return OnlyOne(expression, "faces", docState, conditions, "uid", Nth(expression, conditions));
                case "edge": return OnlyOne(expression, "edges", docState, conditions, "uid", Nth(expression, conditions));
                case "feature": return AFeature(expression, docState, conditions);
                case "dimension":   return ADimension(expression, docState, conditions);
                // I CORPI SI CHIAMANO PER NOME, NON PER NUMERO. Misurato il
                // 25/9/2026 su tre membri strutturali creati nello stesso
                // ordine in due giri: una volta lo stato dice C1 = la barra
                // corta, l'altra C1 = la lunga. «C1» scritto in un caso vuol
                // dire un corpo diverso a seconda del giro; il nome («... (2)»)
                // segue invece l'ordine di creazione, ed e' quello che il caso
                // intende. Il nome ha le parentesi dentro, e il selettore le
                // regge: si apre alla PRIMA e si chiude all'ULTIMA.
                case "body":   return OnlyOne(expression, "bodies", docState, conditions, "uid", Nth(expression, conditions));
                default:
                    throw new CouldNotMeasure("selettore sconosciuto «@" + typ + "»: ci sono " +
                                        "@face, @edge, @feature, @dimension, @body");
            }
        }

        /// <summary>
        /// `verso=davanti, lungo~=100`: il `~` vuol dire «circa», e serve per le
        /// lunghezze, che non sono mai intere quando il pezzo e' stato ricostruito.
        /// </summary>
        internal sealed class Condition
        {
            public string Field;
            public string Value;
            public bool Circa;
        }

        internal static List<Condition> Conditions(string inside)
        {
            var outside = new List<Condition>();
            foreach (var part in inside.Split(','))
            {
                var t = part.Trim();
                if (t.Length == 0) continue;
                bool circa = t.Contains("~=");
                int equal = circa ? t.IndexOf("~=", StringComparison.Ordinal) : t.IndexOf('=');
                if (equal < 0) throw new CouldNotMeasure("condizione senza «=»: «" + t + "»");
                outside.Add(new Condition
                {
                    Field = t.Substring(0, equal).Trim(),
                    Value = t.Substring(equal + (circa ? 2 : 1)).Trim(),
                    Circa = circa,
                });
            }
            return outside;
        }

        // I NOMI DEL CASO E I NOMI DELLO STATO NON SONO GLI STESSI, e la
        // traduzione sta qui e in un punto solo. `@edge(axis=Z)` si legge
        // bene; il campo dello stato si chiama `along`, che da solo vorrebbe
        // dire un'altra cosa.
        internal static string StateField(string nameInCase)
        {
            switch (nameInCase)
            {
                case "axis":   return "along";
                case "length": return "length_mm";
                case "area":   return "area_mm2";
                case "normal": return "facing";
                default:      return nameInCase;
            }
        }

        /// <summary>
        /// Il valore che una condizione del selettore confronta.
        ///
        /// `x`, `y`, `z` SONO LE COORDINATE DI `point_mm` (25/9/2026, voce E18).
        /// Fra i quattro spigoli lunghi 120 di una piastra nessun campo dello
        /// stato distingueva il fianco sinistro davanti da quello dietro, e
        /// `n=` sceglie per ORDINE — lo stesso ordine che per i corpi si e'
        /// visto cambiare fra un giro e l'altro (voce Z-C16). Con le
        /// coordinate lo spigolo si nomina per DOVE sta:
        /// `@edge(axis=Y,x~=-100,z~=20)`.
        /// </summary>
        internal static bool FieldValue(Dictionary<string, object> entry, string theField, out object val)
        {
            int index = theField == "x" ? 0 : theField == "y" ? 1 : theField == "z" ? 2 : -1;
            if (index >= 0)
            {
                val = null;
                object point;
                if (!entry.TryGetValue("point_mm", out point)) return false;
                var list = point as System.Collections.IList;
                if (list == null || list.Count < 3) return false;
                val = list[index];
                return true;
            }
            return entry.TryGetValue(StateField(theField), out val);
        }

        internal static string OnlyOne(
            string expression, string family, Dictionary<string, object> docState,
            List<Condition> conditions, string fieldToReturn, int? nth)
        {
            var entries = (docState != null && docState.ContainsKey(family)
                ? docState[family] : null) as System.Collections.IList;
            if (entries == null)
                throw new CouldNotMeasure("lo stato non ha «" + family + "»: " + expression);

            var foundOnes = new List<Dictionary<string, object>>();
            foreach (var v in entries)
            {
                var entry = v as Dictionary<string, object>;
                if (entry == null) continue;
                bool goes = true;
                foreach (var c in conditions)
                {
                    if (c.Field == "n") continue;
                    object val;
                    if (!FieldValue(entry, c.Field, out val) || val == null)
                    { goes = false; break; }
                    if (c.Circa)
                    {
                        double a, b;
                        if (!double.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture),
                                             NumberStyles.Any, CultureInfo.InvariantCulture, out a) ||
                            !double.TryParse(c.Value, NumberStyles.Any,
                                             CultureInfo.InvariantCulture, out b) ||
                            Math.Abs(a - b) > 0.5)
                        { goes = false; break; }
                    }
                    else if (!string.Equals(
                        Convert.ToString(val, CultureInfo.InvariantCulture), c.Value,
                        StringComparison.OrdinalIgnoreCase))
                    { goes = false; break; }
                }
                if (goes) foundOnes.Add(entry);
            }

            if (nth.HasValue)
            {
                if (nth.Value < 1 || nth.Value > foundOnes.Count)
                    throw new CouldNotMeasure(expression + ": ne ho trovate " + foundOnes.Count +
                                        ", e il caso chiede la numero " + nth.Value);
                return Convert.ToString(foundOnes[nth.Value - 1][fieldToReturn]);
            }

            // ZERO O PIU' D'UNA: SI RIFIUTA, non si sceglie la prima.
            // E' il difetto silenzioso che questa voce nomina: un caso che gira
            // su una faccia diversa da quella che il nome del file promette, e
            // che magari torna lo stesso volume.
            if (foundOnes.Count == 0)
                throw new CouldNotMeasure(expression + ": non l'ho trovata");
            if (foundOnes.Count > 1)
                throw new CouldNotMeasure(expression + ": ne ho trovate " + foundOnes.Count +
                                    ". Sceglierne una sarebbe indovinare: aggiungi una " +
                                    "condizione, oppure «n=» se l'ordine lo decidi tu");
            return Convert.ToString(foundOnes[0][fieldToReturn]);
        }

        internal static string AFeature(
            string expression, Dictionary<string, object> docState, List<Condition> conditions)
        {
            return OnlyOne(expression, "feature", docState, conditions, "uid",
                           Nth(expression, conditions));
        }

        /// <summary>Il `n=` di un selettore, se c'e'.</summary>
        internal static int? Nth(string expression, List<Condition> conditions)
        {
            var n = conditions.FirstOrDefault(c => c.Field == "n");
            if (n == null) return null;
            int k;
            if (!int.TryParse(n.Value, out k))
                throw new CouldNotMeasure(expression + ": «n» non e' un numero");
            return k;
        }

        /// <summary>
        /// `@dimension(feature=Schizzo1,name=D1)` -> il nome ESTESO della quota, come
        /// lo scrive lo stato: `D1@Schizzo1@Parte286.Part`.
        ///
        /// E' il selettore che la voce chiamava «non un lusso»:
        /// `piani/cambia_diametro.json` scrive il nome col titolo del documento
        /// dentro, e quel `Parte76` cambia a ogni giro — **quel piano oggi non
        /// si puo' rieseguire.** Risolvendolo sullo stato, il caso nomina la
        /// quota e il titolo lo mette il banco.
        ///
        /// `angular=true` (voce Z-C21.2, rilievo M3) tiene solo le quote che lo
        /// stato scrive in `deg`, e allora «name» si puo' omettere: i nomi che
        /// SOLIDWORKS da' alle quote di uno smusso (D1, D2) e l'ordine in cui le
        /// elenca non sono misurati, mentre «l'unica quota angolare del pezzo» e'
        /// un fatto che lo stato dice.
        /// </summary>
        internal static string ADimension(
            string expression, Dictionary<string, object> docState, List<Condition> conditions)
        {
            string insideA = conditions.FirstOrDefault(c => c.Field == "feature")?.Value;
            string call = conditions.FirstOrDefault(c => c.Field == "name")?.Value;
            string angularText = conditions.FirstOrDefault(c => c.Field == "angular")?.Value;
            bool? angular = null;
            if (angularText != null)
            {
                bool a;
                if (!bool.TryParse(angularText, out a))
                    throw new CouldNotMeasure(expression + ": «angular» e' true o false");
                angular = a;
            }
            if (call == null && angular == null)
                throw new CouldNotMeasure(expression + ": serve «name» (oppure «angular»)");

            var feature = (docState != null && docState.ContainsKey("feature")
                ? docState["feature"] : null) as System.Collections.IList;
            if (feature == null) throw new CouldNotMeasure("lo stato non ha «feature»: " + expression);

            var foundOnes = new List<string>();
            foreach (var f in feature)
            {
                var entry = f as Dictionary<string, object>;
                if (entry == null || !entry.ContainsKey("dimensions")) continue;
                var quote = entry["dimensions"] as System.Collections.IList;
                if (quote == null) continue;
                foreach (var q in quote)
                {
                    var dimension = q as Dictionary<string, object>;
                    if (dimension == null || !dimension.ContainsKey("name")) continue;
                    string extended = Convert.ToString(dimension["name"]);
                    // «D1@Schizzo1@Parte286.Part» -> corto «D1», dentro «Schizzo1»
                    var parts = extended.Split('@');
                    if (parts.Length < 2) continue;
                    if (call != null &&
                        !string.Equals(parts[0], call, StringComparison.OrdinalIgnoreCase)) continue;
                    if (angular.HasValue && dimension.ContainsKey("deg") != angular.Value) continue;
                    if (insideA != null &&
                        !string.Equals(parts[1], insideA, StringComparison.OrdinalIgnoreCase)) continue;
                    foundOnes.Add(extended);
                }
            }

            if (foundOnes.Count == 0) throw new CouldNotMeasure(expression + ": non l'ho trovata");
            if (foundOnes.Count > 1)
                throw new CouldNotMeasure(expression + ": ne ho trovate " + foundOnes.Count +
                                    " (" + string.Join(", ", foundOnes) + ")");
            return foundOnes[0];
        }

        // ==================================================================
        // MINUTERIA
        // ==================================================================
        internal static string Text(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return null;
            var s = Convert.ToString(v);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        internal static bool True(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        internal static List<object> Items(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return null;
            var itemList = v as System.Collections.IList;
            if (itemList == null) return null;
            var outside = new List<object>();
            foreach (var x in itemList) outside.Add(x);
            return outside;
        }

        internal static double[] Numbers(System.Collections.IEnumerable itemList)
        {
            if (itemList == null) return null;
            var outside = new List<double>();
            foreach (var x in itemList)
            {
                double d;
                if (!double.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return null;
                outside.Add(d);
            }
            return outside.ToArray();
        }

        internal static string Set(double[] n)
        {
            return "[" + string.Join(", ", n.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) + "]";
        }

        /// <summary>
        /// Che cos'e' quel valore, per un messaggio. Si chiama cosi' e non
        /// `Describe` perche' in `NewOperationsTests.cs` un `Describe` c'e' gia': due
        /// metodi con lo stesso nome nella stessa classe parziale non
        /// compilano, ed e' la fine migliore possibile per un nome ripetuto.
        /// </summary>
        internal static string WhatWas(object x)
        {
            if (x == null) return "(non c'e')";
            return Short(Convert.ToString(x, CultureInfo.InvariantCulture));
        }

        internal static int HowManyFeatures(Dictionary<string, object> docState)
        {
            var f = (docState != null && docState.ContainsKey("feature")
                ? docState["feature"] : null) as System.Collections.IList;
            return f == null ? 0 : f.Count;
        }
    }
}
