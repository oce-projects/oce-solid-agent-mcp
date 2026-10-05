using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA PRIMA OPERAZIONE DI ASSIEME (voce GA3).
    ///
    /// ── IL DIFETTO CHE QUESTA OPERAZIONE DEVE NON AVERE ──────────────────
    ///
    /// Il censimento delle API di assieme (nota `80q`) lo dice gia', ed e' la
    /// frase da tenere davanti scrivendo questo file:
    ///
    ///     «AddComponent5 piazza senza accoppiare: quattro corpi liberi che
    ///      sembrano un assieme».
    ///
    /// Inserire riesce quasi sempre. Il pezzo appare, l'albero si riempie, la
    /// ricostruzione e' pulita, e **niente e' vincolato**. Un assieme di quattro
    /// componenti liberi si apre, si ruota, si esporta, e si monta sbagliato.
    /// Percio' questa operazione, oltre a inserire, **dice quanti gradi di
    /// liberta' restano**, e lo dice a voce alta nella sua risposta: non e' un
    /// dettaglio, e' l'unica misura che distingue un assieme da quattro pezzi
    /// nella stessa stanza.
    ///
    /// ── DUE PRECONDIZIONI CHE NON SI POSSONO INDOVINARE ──────────────────
    ///
    ///  1. **Il documento deve essere un assieme.** Chiamare questa su una
    ///     parte non e' un errore dell'utente da punire con un'eccezione: e' una
    ///     cosa che si dice, col tipo che si e' trovato davanti.
    ///  2. **Il componente dev'essere gia' APERTO.** `AddComponent5` prende un
    ///     percorso, ma vuole il documento aperto in SOLIDWORKS: con un file
    ///     chiuso risponde `null` **senza dire perche'**. E' la stessa
    ///     precondizione delle viste di tavola, dove era gia' costata un giro.
    ///     Qui si controlla PRIMA, e il rifiuto elenca cosa c'e' di aperto:
    ///     un rifiuto che non dice l'alternativa costringe a indovinare.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome InsertComponent(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed(
                    "Questo non e' un assieme, e' un documento di tipo «" +
                    DocumentTypeName(TypeOf(model)) + "». I componenti si " +
                    "inseriscono solo in un assieme: aprine uno, oppure crealo, " +
                    "e poi richiamami.");

            string filePath = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(filePath))
                return Failed(
                    "Non mi hai detto quale componente inserire: «path» e' il " +
                    "percorso completo del file, e dev'essere uno dei documenti " +
                    "gia' aperti. Li trovi in «documents_open» dello stato.");

            if (!File.Exists(filePath))
                return Failed(
                    "Il file «" + filePath + "» non esiste. " + WhatIsOpen());

            // ── E' APERTO? ───────────────────────────────────────────────
            //
            // Il controllo sta qui e non dopo, perche' dopo non ci sarebbe
            // niente da leggere: `AddComponent5` risponde null e basta, e un
            // null non dice se il file manca, se e' chiuso, o se il percorso e'
            // scritto in un altro modo.
            if (!IsOpen(filePath))
                return Failed(
                    "«" + Path.GetFileName(filePath) + "» non e' aperto in SOLIDWORKS, e " +
                    "l'inserimento vuole un documento gia' aperto: se lo chiamo su un " +
                    "file chiuso mi risponde «non fatto» senza dirmi perche'. " +
                    WhatIsOpen());

            double x = Num(a, "x_mm", 0), y = Num(a, "y_mm", 0), z = Num(a, "z_mm", 0);
            string configuration = Str(a, "configuration", "");
            bool configurationAsked = !string.IsNullOrWhiteSpace(configuration);

            // ── THE CONFIGURATION ASKED FOR IS CHECKED BEFORE INSERTING ──────
            //
            // Z-C21.4, finding L11-1. Until library 0.145.0 «configuration» went
            // to AddComponent5 with UseConfigForPartReferences false, so it was
            // never used (see the call below), and a name the file does not have
            // was not refused either. The names are read from the open document
            // itself (the insertion needs it open anyway) and compared whole:
            // the two configurations of the company template share their first
            // word and differ in the part in angle brackets, which belongs to
            // the name (see TrueConfigurationName). A refusal here has
            // inserted nothing.
            if (configurationAsked)
            {
                var source = OpenDocumentAtPath(filePath);
                var sourceConfigurations = source == null ? new List<string>() : ConfigurationNames(source);
                if (sourceConfigurations.Count == 0)
                    return Failed(
                        "I could not read the configurations of «" + Path.GetFileName(filePath) + "», so I " +
                        "cannot check «" + configuration + "» before inserting it. Nothing was inserted: " +
                        "insert it without a configuration name, or open the file again and retry.");
                if (!sourceConfigurations.Contains(configuration, StringComparer.Ordinal))
                    return Failed(
                        "«" + Path.GetFileName(filePath) + "» has no configuration «" + configuration +
                        "». It has: «" + string.Join("», «", sourceConfigurations) + "»" +
                        ". The name is compared whole, the part in angle brackets included. Nothing was inserted.");
            }

            int before = HowManyComponentsInAssembly(assemblyDoc);

            IComponent2 born;
            try
            {
                // THE NAMED CONFIGURATION IS ASKED FOR WITH ITS SWITCH (Z-C21.4,
                // L11-1). IAssemblyDoc~AddComponent5.html: «UseConfigForPartReferences
                // If true, the configuration specified in ExistingConfigName is
                // used» and «ExistingConfigName Name of the configuration of the
                // loaded component; valid only if UseConfigForPartReferences =
                // true». Up to library 0.145.0 the switch was false and the name
                // was ignored: the component came in with ConfigOption 0,
                // swAddComponentConfigOptions_CurrentSelectedConfig, which
                // swAddComponentConfigOptions_e.html describes as «Add a part or
                // assembly in its last saved configuration». Without a name that
                // is still what happens. With a name, what the component really
                // references is read back below and a different one is refused.
                born = assemblyDoc.AddComponent5(
                    filePath,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    "", configurationAsked, configurationAsked ? configuration : "",
                    x / 1000.0, y / 1000.0, z / 1000.0) as IComponent2;
            }
            catch (Exception ex)
            {
                return Failed("Non sono riuscito a inserire il componente: " + ex.Message);
            }

            // IL NOME SI PRENDE PRIMA DELLA RICOSTRUZIONE, e serve per ritrovare
            // il componente DOPO. Una ricostruzione puo' invalidare i puntatori
            // COM ai componenti: continuare a interrogare la maniglia che
            // `AddComponent5` ha restituito vorrebbe dire misurare su un
            // riferimento vecchio, e leggere numeri credibili e sbagliati.
            //
            // E' la regola di casa applicata un livello piu' in la': *il valore
            // che torna da chi ha fatto la cosa non e' la misura della cosa* —
            // e nemmeno l'OGGETTO che torna e' l'oggetto su cui misurare.
            string itemName = null;
            try { itemName = born == null ? null : born.Name2; } catch { }

            try { model.EditRebuild3(); } catch { }

            // Rilettura: si cerca per nome fra i componenti che l'assieme
            // dichiara ADESSO. Se non si ritrova, si tiene la maniglia vecchia
            // invece di rinunciare alla misura — dirlo e' meglio che tacere,
            // ma una misura sospetta e' meglio di nessuna misura solo finche'
            // si sa che e' sospetta, e infatti il caso di banco la sorveglia.
            var real = RereadComponent(assemblyDoc, itemName) ?? born;

            // ── LA MISURA STA SULL'ASSIEME, NON SULLA RISPOSTA ───────────
            //
            // `AddComponent5` ha risposto un `IComponent2`, e *il valore che
            // torna da chi ha fatto la cosa non e' la misura della cosa*: si
            // conta quanti componenti ci sono adesso.
            int after = HowManyComponentsInAssembly(assemblyDoc);
            if (after != before + 1)
                return Failed(
                    "Ho chiesto di inserire «" + Path.GetFileName(filePath) + "» e " +
                    (born == null ? "non mi e' stato risposto niente" : "mi e' stato risposto di si'") +
                    ", ma l'assieme aveva " + before + " componenti e adesso ne ha " + after +
                    ". Non l'ho considerato inserito.");

            // THE CONFIGURATION THE COMPONENT REALLY REFERENCES (Z-C21.4, L11-1),
            // read on the component found again after the rebuild. When one was
            // asked for, a different one (or one that cannot be read) is not the
            // component that was asked for: it is taken out again, and «taken
            // out» is said only after the components are counted again.
            string referenced = null;
            try { referenced = real == null ? null : real.ReferencedConfiguration; } catch { }
            if (configurationAsked && !string.Equals(referenced, configuration, StringComparison.Ordinal))
                return Failed(
                    "I asked SOLIDWORKS for «" + Path.GetFileName(filePath) + "» in configuration «" +
                    configuration + "», and the new component references " +
                    (string.IsNullOrWhiteSpace(referenced) ? "a configuration I could not read" : "«" + referenced + "»") +
                    ". It is not the component you asked for." +
                    TakeOutInsertedComponent(model, assemblyDoc, real, before));

            var measurements = new Dictionary<string, object>
            {
                ["components_before"] = before,
                ["components_after"] = after,
            };

            if (!string.IsNullOrWhiteSpace(itemName)) measurements["component"] = itemName;

            if (!string.IsNullOrWhiteSpace(referenced)) measurements["configuration"] = referenced;

            // ── E' VINCOLATO? LE MISURE CHE FUNZIONANO DAVVERO ──────────
            //
            // I gradi di liberta' NON si possono avere da questa API in questo
            // contesto: misurato il 16/9/2026, `GetRemainingDOFs` risponde
            // `Unavailable` su un assieme appena costruito, selezionando il
            // componente, dopo averlo salvato su disco, e perfino dopo un
            // accoppiamento coincidente vero (`AddMate5` con `NoError`).
            // Quattro condizioni, sempre lo stesso.
            //
            // Quello che si misura DAVVERO sono due cose, e bastano a dire la
            // verita': se il componente e' **fisso**, e **quanti accoppiamenti**
            // ha. `IsFixed()` funziona — distingue il primo componente
            // (inchiodato) dagli altri — e `GetMates()` si conta.
            //
            // Un componente NON fisso con ZERO accoppiamenti e' appoggiato, e
            // questo lo si sa per certo. Non si sa DI QUANTO sia libero, e
            // infatti non lo si dice.
            var deg = DegreesOfFreedom(real);
            measurements["degrees_of_freedom_state"] = deg.State;
            if (deg.Available) measurements["degrees_of_freedom_remaining"] = deg.HowMany;

            bool grounded = false;
            try { grounded = real != null && real.IsFixed(); } catch { }
            measurements["grounded"] = grounded;

            int mates = CountMates(real);
            if (mates >= 0) measurements["mates"] = mates;

            bool resting = !grounded && mates == 0;
            measurements["resting"] = resting;

            // DOVE E' FINITA L'ORIGINE, che NON e' il punto che hai chiesto.
            //
            // `AddComponent5(x, y, z)` posiziona il **centro dell'ingombro**.
            // Un pezzo con l'origine su una faccia — la norma, se e' stato
            // estruso a una direzione sola — si ritrova l'origine spostata di
            // meta' spessore. Misurato il 17/9/2026 e previsto prima: spesso 4
            // chiesto a z=30 → origine a (0, -2, 30); spesso 10 chiesto a 0 →
            // (0, -5, 0).
            //
            // Si riporta perche' e' cio' che il modello rileggera' nello stato,
            // e vedere due numeri diversi senza spiegazione e' peggio che
            // vederne uno solo.
            var origin = ComponentOriginMm(real);
            if (origin != null) measurements["origin_mm"] = origin;

            // IL GIUDIZIO E' UNA FRASE, NON UN NUMERO, e viaggia con le misure.
            //
            // «degrees_of_freedom_remaining: 6» da solo non dice cosa fare. E chi
            // legge questa risposta potrebbe non aver mai letto la descrizione
            // dell'operazione: l'avvertimento va qui, dove arriva comunque. E'
            // la stessa scelta di `check_body_geometry`, che accanto a
            // `printable` scrive `verdict`.
            string verdictValue =
                "Inserito «" + (itemName ?? Path.GetFileName(filePath)) + "» a " +
                AssemblyMm(x) + ", " + AssemblyMm(y) + ", " + AssemblyMm(z) + " mm. " +
                (grounded
                    ? "E' FISSO: non si muove perche' e' inchiodato, non perche' sia " +
                      "accoppiato. Sono due cose diverse."
                    : resting
                        ? "ATTENZIONE: non e' fisso e non ha nessun accoppiamento — e' " +
                          "appoggiato li'. Si sposta se qualcuno lo trascina, e l'assieme " +
                          "sembra montato senza esserlo."
                        : mates > 0
                            ? "Ha " + mates +
                              (mates == 1 ? " accoppiamento" : " accoppiamenti") +
                              ". Quanto sia ancora libero non te lo so dire: SOLIDWORKS " +
                              "non fornisce i gradi di liberta' residui da qui."
                            : "Non sono riuscito a dire se sia vincolato: non ho potuto " +
                              "leggere ne' gli accoppiamenti ne' lo stato.");
            measurements["verdict"] = verdictValue;
            _log("[agente] " + verdictValue);

            return SucceededWithoutCreating(measurements);
        }

        // =================================================================

        /// <summary>
        /// I gradi di liberta' residui di un componente, o `-1` se non si sono
        /// potuti leggere.
        ///
        /// `IComponent2.GetRemainingDOFs` ha **dodici parametri in uscita** —
        /// due punti e quattro direzioni con i rispettivi stati — e il numero
        /// che serve e' il **valore di ritorno**, non qualcosa da dedurre da
        /// quelli. I dodici servono per dire *quale* liberta' resta, ed e' una
        /// voce a se' (GA4): qui si conta e basta.
        ///
        /// Su un componente appena inserito e non accoppiato ci si aspetta
        /// **6**: tre traslazioni e tre rotazioni. Uno **zero** su un componente
        /// che nessuno ha accoppiato non vuol dire «vincolato bene»: vuol dire
        /// FISSO, che e' un'altra cosa — per questo lo stato scrive anche
        /// `grounded` a parte (voce GA1).
        /// </summary>
        /// <remarks>
        /// `internal` e non `private`: la legge anche `StatoAssieme` (voce GA5),
        /// e due copie della stessa lettura sono due cose da tenere allineate a
        /// mano.
        /// </remarks>
        /// <remarks>
        /// `internal`: la usa anche `StatoAssieme`, per la stessa ragione per
        /// cui e' internal `DegreesOfFreedom` — una sola lettura, un solo posto
        /// da riparare.
        /// </remarks>
        internal static int MatesOf(IComponent2 component)
        {
            return CountMates(component);
        }

        internal static DegreesOfFreedomRead DegreesOfFreedom(IComponent2 component)
        {
            var outside = new DegreesOfFreedomRead { State = "non letto", HowMany = -1 };
            if (component == null) return outside;
            try
            {
                int s1, s2, s3, s4, s5, s6;
                MathPoint p1, p2;
                MathVector d1, d2, t1, t2;
                int outcome = component.GetRemainingDOFs(
                    out s1, out p1, out s2, out d1,
                    out s3, out p2, out s4, out d2,
                    out s5, out t1, out s6, out t2);

                outside.State = DofStateName(outcome);

                // IL CONTEGGIO SI RICAVA DAGLI STATI, e SOLO se l'esito dice
                // che l'informazione c'e'. I sei `status` sono di
                // `swDofStatus_e`: `Unused` vuol dire «questo posto non
                // descrive niente», non «zero gradi».
                if (outcome == (int)swRemainingDofs_e.swRemainingDofs_Restricted ||
                    outcome == (int)swRemainingDofs_e.swRemainingDofs_Unrestricted)
                {
                    outside.HowMany = HowManyFree(s1, s2, s3, s4, s5, s6);
                }
            }
            catch { outside.State = "non letto"; }
            return outside;
        }

        /// <summary>
        /// Cosa ha risposto `GetRemainingDOFs`, e se ne e' uscito un numero.
        ///
        /// ── PERCHE' NON E' PIU' UN `int` ─────────────────────────────────
        ///
        /// Fino alla 0.58.0 questo metodo tornava direttamente il valore di
        /// `GetRemainingDOFs` e lo si pubblicava come
        /// `degrees_of_freedom_remaining`. **Era sbagliato: quel valore non e' un
        /// conteggio, e' uno stato** (`swRemainingDofs_e`). Misurato il
        /// 16/9/2026: su un assieme appena costruito risponde **2**, che vuol
        /// dire `Unavailable` — e lo risponde **identico su un componente fisso
        /// e su uno libero**.
        ///
        /// Il ponte stampava «ha ancora 2 gradi di liberta' su 6». Numero
        /// inventato, unita' inventata, e uguale per un pezzo inchiodato e uno
        /// che ballava. *Un caso in cui due valori diversi danno lo stesso
        /// risultato non tabella niente* — e questo lo pubblicava come misura.
        /// </summary>
        internal sealed class DegreesOfFreedomRead
        {
            /// <summary>Il nome dello stato: «non disponibile», «libero», …</summary>
            public string State;

            /// <summary>Quanti gradi, oppure `-1` se non si e' potuto sapere.</summary>
            public int HowMany;

            public bool Available { get { return HowMany >= 0; } }
        }

        private static int HowManyFree(params int[] states)
        {
            int n = 0;
            foreach (var s in states)
                if (s == (int)swDofStatus_e.swDofStatus_Free ||
                    s == (int)swDofStatus_e.swDofStatus_FreeNormal ||
                    s == (int)swDofStatus_e.swDofStatus_Instantaneous ||
                    s == (int)swDofStatus_e.swDofStatus_InstantaneousNormal) n++;
            return n;
        }

        /// <summary>
        /// `swRemainingDofs_e`, col nome. I valori sono
        /// Restricted=0, Unrestricted=1, **Unavailable=2**, Failed=3,
        /// RootComponent=4 — e il 2 e' quello che si incontra sempre.
        /// </summary>
        private static string DofStateName(int outcome)
        {
            switch (outcome)
            {
                case (int)swRemainingDofs_e.swRemainingDofs_Restricted: return "vincolato";
                case (int)swRemainingDofs_e.swRemainingDofs_Unrestricted: return "libero";
                case (int)swRemainingDofs_e.swRemainingDofs_Unavailable: return "unavailable";
                case (int)swRemainingDofs_e.swRemainingDofs_Failed: return "non riuscito";
                case (int)swRemainingDofs_e.swRemainingDofs_RootComponent: return "componente radice";
                default: return "sconosciuto (" + outcome.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>
        /// Ritrova un componente per nome fra quelli che l'assieme dichiara
        /// adesso, o `null` se non c'e'.
        ///
        /// `Name2` e' unico dentro un assieme — e' il nome con cui SOLIDWORKS
        /// stesso distingue le istanze — quindi cercarlo cosi' non e'
        /// ambiguo, nemmeno con due copie dello stesso file («base-1» e
        /// «base-2»).
        /// </summary>
        private static IComponent2 RereadComponent(IAssemblyDoc assemblyDoc, string itemName)
        {
            if (assemblyDoc == null || string.IsNullOrWhiteSpace(itemName)) return null;
            try
            {
                var v = assemblyDoc.GetComponents(false) as object[];
                if (v == null) return null;
                foreach (var o in v)
                {
                    var c = o as IComponent2;
                    if (c == null) continue;
                    string its = null;
                    try { its = c.Name2; } catch { }
                    if (string.Equals(its, itemName, StringComparison.Ordinal)) return c;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Quanti accoppiamenti ha il componente, o `-1` se non si e' potuto
        /// sapere.
        ///
        /// ── `null` VUOL DIRE ZERO, MA SOLO SE IL COMPONENTE E' VIVO ─────
        ///
        /// `GetMates()` risponde **null quando gli accoppiamenti sono zero**,
        /// non un array vuoto — misurato il 16/9/2026 su un componente appena
        /// inserito. E' la convenzione di mezza API di SOLIDWORKS, ed e' anche
        /// il modo in cui «non ce ne sono» e «non ho potuto leggere» diventano
        /// la stessa risposta.
        ///
        /// Qui le due cose si separano, e il discrimine e' se il componente
        /// **risponde ancora**: se `Name2` si legge, la maniglia e' viva e un
        /// `null` da `GetMates` significa davvero zero. Se non si legge
        /// nemmeno il nome, non si sa niente e si dice `-1`.
        ///
        /// La differenza non e' accademica: `accoppiamenti = 0` porta a
        /// «resting», che e' un **avvertimento**; `-1` porta a «non lo so»,
        /// che e' un'altra cosa. Confonderle vorrebbe dire o tacere un pericolo
        /// o gridarne uno che non c'e'.
        ///
        /// ZERO-E-ZERO: il `m == null ? 0` qui sotto e' uno zero VERO, non un
        /// ripiego, e il motivo e' le sei righe qui sopra — a quel punto la
        /// maniglia ha gia' risposto, quindi `null` vuol dire zero. E' l'unico
        /// punto del repository che la regola di Z-C8 la rispettava gia', ed e'
        /// il modello per gli altri venti: **`-1` per «non lo so», `0` solo
        /// quando lo zero e' un'affermazione che si puo' fare.**
        /// (`Checks/zero-or-unknown.js` legge questo marcatore.)
        /// </summary>
        private static int CountMates(IComponent2 component)
        {
            if (component == null) return -1;
            try
            {
                var its = component.Name2;      // se lancia, la maniglia e' morta
                if (its == null) return -1;
            }
            catch { return -1; }

            try
            {
                var m = component.GetMates() as object[];
                return m == null ? 0 : m.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Dove sta l'origine del componente, in mm, o `null`.
        ///
        /// Stessa lettura di `StatoAssieme`, e per la stessa ragione sta scritta
        /// una volta sola: `GetTotalTransform(false)`, indici 9-10-11 di
        /// `ArrayData`. Il `false` esclude la trasformazione di esploso — con
        /// `true` si leggerebbe la posizione **nella vista esplosa**.
        /// </summary>
        private static object[] ComponentOriginMm(IComponent2 c)
        {
            if (c == null) return null;
            try
            {
                var t = c.GetTotalTransform(false) as IMathTransform;
                if (t == null) return null;
                var d = t.ArrayData as double[];
                if (d == null || d.Length < 12) return null;
                return new object[]
                {
                    Math.Round(Units.Mm(d[9]), 3),
                    Math.Round(Units.Mm(d[10]), 3),
                    Math.Round(Units.Mm(d[11]), 3),
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// Quanti problemi dichiara il documento, o `-1` se non si sa.
        ///
        /// ── PERCHE' SERVE, E PERCHE' IL CONTEGGIO DEI VINCOLI NON BASTA ──
        ///
        /// Dopo una sostituzione di componente gli accoppiamenti **restano**
        /// (misurato). Restare non vuol dire funzionare: un vincolo che punta a
        /// una faccia del pezzo VECCHIO sopravvive come voce nell'albero e va in
        /// errore. Il conteggio lo vede ancora — e direbbe «due prima, due
        /// adesso», cioe' tutto a posto.
        ///
        /// **Due vincoli rotti sono peggio di zero vincoli**: zero si vede,
        /// rotti sembrano vincolare. `GetWhatsWrongCount` e' la domanda che
        /// separa le due cose, ed e' la stessa che il progettista si fa
        /// guardando l'albero rosso.
        /// </summary>
        internal static int HowManyProblems(IModelDoc2 model)
        {
            try
            {
                var e = model == null ? null : model.Extension;
                return e == null ? -1 : e.GetWhatsWrongCount();
            }
            catch { return -1; }
        }

        private static int HowManyComponentsInAssembly(IAssemblyDoc assemblyDoc)
        {
            try
            {
                var v = assemblyDoc.GetComponents(true) as object[];
                return v == null ? -1 : v.Length;
            }
            catch { return -1; }
        }

        private bool IsOpen(string filePath)
        {
            return OpenDocumentAtPath(filePath) != null;
        }

        /// <summary>
        /// The open document whose path is <paramref name="path"/>, or null.
        /// The same walk EAperto always made, by full path, so that the
        /// configuration check of L11-1 reads the very document the insertion
        /// will use.
        /// </summary>
        private IModelDoc2 OpenDocumentAtPath(string filePath)
        {
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 500)
                {
                    string its = "";
                    try { its = d.GetPathName() ?? ""; } catch { }
                    if (!string.IsNullOrWhiteSpace(its) &&
                        string.Equals(Path.GetFullPath(its), Path.GetFullPath(filePath),
                                      StringComparison.OrdinalIgnoreCase))
                        return d;
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Takes out the component this step inserted when it references another
        /// configuration than the one asked for (Z-C21.4, L11-1), and returns the
        /// tail of the refusal. «Taken out» only when the top-level components,
        /// counted again, are as many as before the insertion. DeleteSelection2
        /// and not EditDelete, which can open a confirmation window (the rule of
        /// RemoveMirror and RemoveCutTable); the component is selected
        /// with its own Select4 (SelectComponent says why).
        /// </summary>
        private static string TakeOutInsertedComponent(
            IModelDoc2 model, IAssemblyDoc assemblyDoc, IComponent2 component, int countBefore)
        {
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (SelectComponent(component))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
                model.EditRebuild3();
            }
            catch { }
            int countNow = HowManyComponentsInAssembly(assemblyDoc);
            if (!deleted)
                return " DeleteSelection2 did not take it out: it is still in the assembly, remove it yourself.";
            if (countBefore >= 0 && countNow == countBefore)
                return " I took it out: the assembly has " + countNow + " components again, as before.";
            if (countBefore < 0 || countNow < 0)
                return " DeleteSelection2 answered true, but I could not count the components again to check " +
                       "that it is gone: look at the assembly before going on.";
            return " DeleteSelection2 answered true, but the assembly has " + countNow + " components and had " +
                   countBefore + " before: look at it before going on.";
        }

        /// <summary>
        /// L'elenco di cosa e' aperto, per il rifiuto.
        ///
        /// UN RIFIUTO CHE NON DICE L'ALTERNATIVA COSTRINGE A INDOVINARE, e
        /// indovinare e' il modo in cui si sbaglia in silenzio. Qui costa una
        /// riga: si dicono i file aperti, che sono gli unici percorsi che
        /// l'inserimento accetta.
        /// </summary>
        private string WhatIsOpen()
        {
            var names = new List<string>();
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 500)
                {
                    string its = "";
                    try { its = d.GetPathName() ?? ""; } catch { }
                    if (!string.IsNullOrWhiteSpace(its)) names.Add(its);
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }

            if (names.Count == 0)
                return "Adesso non e' aperto nessun documento salvato su disco: " +
                       "un componente mai salvato non si puo' inserire, perche' " +
                       "l'inserimento lo cerca per percorso.";
            return "Adesso sono aperti: " + string.Join("; ", names) + ".";
        }

        private static int TypeOf(IModelDoc2 model)
        {
            try { return model == null ? -1 : model.GetType(); } catch { return -1; }
        }

        /// <summary>
        /// Millimetri per un messaggio.
        ///
        /// NOME DIVERSO DA QUALUNQUE ALTRO HELPER DI QUESTA CLASSE PARZIALE.
        /// `SwOperations` e' spalmata su una quindicina di file, e un metodo
        /// privato con lo stesso nome in due file e' un errore di compilazione
        /// che parla di «piu' definizioni», non del posto giusto. `AssemblyMm`
        /// e' brutto e non collide: e' la stessa riparazione di `TripletMm` e
        /// `DistanceBetweenPoints`.
        /// </summary>
        private static string AssemblyMm(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
