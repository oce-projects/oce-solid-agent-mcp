using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE OPERAZIONI CHE NON DISEGNANO: dicono com'e' venuto. (voce GP1)
    ///
    /// ── PERCHE' ESISTONO ─────────────────────────────────────────────────
    ///
    /// Fino a qui nessuna delle operazioni del catalogo sapeva rispondere alla
    /// domanda «com'e' venuto?». Le misure c'erano — `rectangular_cut`
    /// conta il materiale tolto, `rectangular_frame` confronta l'ingombro
    /// col promesso, `hole_wizard` misura dove e' finito l'asse — ma erano
    /// **private e interne alle singole operazioni**: il modello non poteva
    /// chiamarne nessuna.
    ///
    /// `cut_list` e' il precedente: un'operazione di catalogo che LEGGE
    /// invece di disegnare. Questa fa lo stesso su una domanda diversa.
    ///
    /// ── LA DOMANDA A CUI RISPONDE, E PERCHE' NON E' OVVIA ────────────────
    ///
    /// «Questo corpo si puo' stampare?» In pratica: **e' un solido solo,
    /// chiuso, senza difetti topologici**. Le quattro chiamate che
    /// SOLIDWORKS offre per saperlo non dicono la stessa cosa, e due di loro
    /// tacciono proprio sul caso che conta.
    ///
    /// Misurato il 15/9/2026 con `Checks\BrokenBody`, su SOLIDWORKS 2021:
    ///
    ///                             Check()  Check2()  Check3.Count  fessure
    ///   solido sano                  1        0           0           0
    ///   due solidi che si toccano    1        0           0           0
    ///   CORPO DI FOGLIO (aperto)     1        0           0           1
    ///
    /// Due cose, e tutte e due cambiano come si scrive questa operazione:
    ///
    /// 1. **`Check()` e `Check2()` hanno convenzioni OPPOSTE.** Sullo stesso
    ///    corpo sano il primo dice 1 e il secondo dice 0. Chi legge lo zero di
    ///    `Check2()` come «falso, cioe' rotto» ottiene un risultato coerente,
    ///    stabile e rovesciato.
    ///
    /// 2. **Un corpo APERTO passa tre controlli su quattro.** Il foglio non e'
    ///    rotto: e' un corpo valido che non racchiude niente. `Check`, `Check2`
    ///    e `Check3` lo dichiarano sano tutti e tre, e **solo
    ///    `Diagnose().GetGapsCount()` lo vede**. Un'operazione costruita su
    ///    `Check3` — la via che la voce GP1 nominava per prima — direbbe
    ///    «nessun problema» esattamente sul caso per cui esiste.
    ///
    /// Quindi qui si guardano **tutte e quattro**, e il giudizio le richiede
    /// tutte d'accordo.
    ///
    /// ── FIRME VERIFICATE sull'interop 29.5.1.1 ──────────────────────────
    ///
    ///   IBody2.Check()    -> Int32
    ///   IBody2.Check2()   -> Int32
    ///   IBody2.Check3     -> FaultEntity      ** PROPRIETA', NON METODO **
    ///   IBody2.Diagnose() -> DiagnoseResult
    ///   IFaultEntity.Count, get_ErrorCode(i)
    ///   IDiagnoseResult.GetGapsCount()
    ///
    /// `Check3` proprieta' e non metodo e' una trappola da late binding:
    /// scritto `Check3()` non da' errore di compilazione, da' un fallimento a
    /// runtime dentro un `catch` che lo ingoia.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// Il valore che `Check()` da' su un corpo sano. **Uno, non zero**, e
        /// il nome esiste perche' la costante nuda in mezzo a un `if` sarebbe
        /// indistinguibile da un refuso.
        /// </summary>
        private const int CheckSaysHealthy = 1;

        /// <summary>
        /// Il valore che `Check2()` da' su un corpo sano. **Zero, non uno.**
        /// Sullo stesso corpo, con l'altra convenzione.
        /// </summary>
        private const int Check2SaysHealthy = 0;

        private Outcome CheckBodyGeometry(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("Questo documento non e' un pezzo: non ha corpi da verificare.");

            // TUTTI I TIPI, non solo i solidi. Un pezzo che ha per errore un
            // corpo di foglio e' proprio il caso che questa operazione esiste
            // per dire, e chiedendo solo i solidi non lo si vedrebbe mai.
            //
            // THE «C» NUMBERS STILL MATCH THE STATE'S FOR THE SOLIDS (Z-C21.9,
            // finding L12-3, measured not real). The state numbers the bodies of
            // GetBodies2(swSolidBody); here they come from swAllBodies. The
            // Z-C21.3 probe of L12-3 (28/9/2026, SOLIDWORKS 29.5.1) built
            // four parts, solid and sheet bodies in the orders S F, F S, S F S and
            // F S F: after ForceRebuild3 swAllBodies gave the solids FIRST and in
            // the order of swSolidBody every time, and only the tail (the sheets)
            // is beyond the state's numbers. Before the rebuild the probe could
            // not read the bodies at all (RPC_E_SERVERFAULT on every body), which
            // is the «unstable order» its verdict names. This operation does not
            // rebuild before reading, and the probe measured the order only after
            // ForceRebuild3: on a part not rebuilt since its bodies were made, the
            // order is not measured.
            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swAllBodies, false) as object[];
            if (rawItems == null || rawItems.Length == 0)
                return Failed(
                    "Il pezzo non ha nessun corpo: non c'e' niente da verificare. " +
                    "Fai prima un'estrusione.");

            string thisOnly = Str(a, "body_uid", null);

            // THE BODIES OF THE PART, counted before the «body_uid» filter
            // (Z-C21.10, finding N61). IPartDoc~GetBodies2.html: «false gets
            // all of the bodies in the part». With one body asked for,
            // «bodies_total» is 1 on a part of two, and the verdict read «Un solo
            // corpo solido ... si puo' esportare»: a sentence about the PART,
            // which has two bodies and would give an STL of two shells.
            int bodiesInPart = rawItems.Count(o => o is IBody2);

            var list = new List<object>();
            int solids = 0, sheets = 0, others = 0;
            int withDefects = 0, withGaps = 0;
            var reasons = new List<string>();

            // WHICH BODIES (Z-C21.5, scheme S5). Every body of the part, each
            // labelled by its place in the list (the L12-3 note above is about
            // those labels); or the ONE body «body_uid» names, resolved through
            // the document state and its signature (BodyAskedFor: BodiesFromUid and
            // SignatureMatches), never as the body in that place of a GetBodies2
            // made now. A uid is an identity, and a cut that renumbers the
            // bodies must not move the check onto another body.
            var labels = new List<string>();
            var bodies = new List<IBody2>();
            if (!string.IsNullOrWhiteSpace(thisOnly))
            {
                IBody2 asked = BodyAskedFor(docState, a, thisOnly.Trim(), out string refusal);
                if (asked == null) return Failed(refusal);
                labels.Add(thisOnly.Trim().ToUpperInvariant());
                bodies.Add(asked);
            }
            else
            {
                for (int i = 0; i < rawItems.Length; i++)
                {
                    IBody2 inList = rawItems[i] as IBody2;
                    if (inList == null) continue;
                    labels.Add("C" + (i + 1));
                    bodies.Add(inList);
                }
            }

            for (int k = 0; k < bodies.Count; k++)
            {
                IBody2 body = bodies[k];
                string uid = labels[k];

                int typ = Integer(() => body.GetType(), -1);
                string whatItIsCalled = BodyTypeName(typ);
                if (typ == (int)swBodyType_e.swSolidBody) solids++;
                else if (typ == (int)swBodyType_e.swSheetBody) sheets++;
                else others++;

                int check = Integer(() => body.Check(), int.MinValue);
                int check2 = Integer(() => body.Check2(), int.MinValue);
                // null: Check3 could not be read (Z-C21.8, finding N16).
                var defects = BodyFaultNames(body);
                int gaps = Integer(() => Gaps(body), -1);

                if (defects != null && defects.Count > 0) withDefects++;
                if (gaps > 0) withGaps++;

                var entry = new Dictionary<string, object>
                {
                    ["body"] = uid,
                    ["kind"] = whatItIsCalled,
                    ["faces"] = Integer(() => body.GetFaceCount(), -1),
                    ["closed"] = gaps == 0,
                    ["gaps"] = gaps,
                    ["defects"] = defects == null ? null : (object)defects.Count,
                    // I NUMERI GREZZI RESTANO, e non e' ridondanza: quando un
                    // giorno una versione di SOLIDWORKS cambiera' una delle due
                    // convenzioni, e' da qui che lo si vedra' invece che da un
                    // giudizio girato al contrario.
                    ["check_raw"] = check,
                    ["check2_raw"] = check2,
                };
                if (defects != null && defects.Count > 0) entry["defects_list"] = defects.ToArray();
                if (defects == null) entry["defects_not_read"] = DefectsNotRead;
                list.Add(entry);

                if (typ != (int)swBodyType_e.swSolidBody)
                    reasons.Add("il corpo " + uid + " e' " + ABodyOf(whatItIsCalled) +
                               ", non un solido: non racchiude un volume");
                if (gaps > 0)
                    reasons.Add("il corpo " + uid + " ha " + gaps +
                               (gaps == 1 ? " fessura" : " fessure") + ": non e' chiuso");
                // NOT COUNTED IS NOT CLOSED (Z-C21.8, with N16): `Gaps` answers
                // -1 when Diagnose gives nothing, and the closed field was already
                // false, but no reason was added and the verdict could stay «can
                // be printed».
                if (gaps < 0)
                    reasons.Add("the gaps of body " + uid + " could not be counted (Diagnose gave no answer): " +
                               "not counted is not closed");
                if (defects == null)
                    reasons.Add("the defects of body " + uid + " could not be read: " + DefectsNotRead);
                else if (defects.Count > 0)
                    reasons.Add("il corpo " + uid + " ha " + defects.Count +
                               (defects.Count == 1 ? " difetto: " : " difetti: ") +
                               string.Join(", ", defects));
                if (check != CheckSaysHealthy)
                    reasons.Add("il corpo " + uid + " ha Check() = " + check +
                               " invece di " + CheckSaysHealthy);
                if (check2 != Check2SaysHealthy)
                    reasons.Add("il corpo " + uid + " ha Check2() = " + check2 +
                               " invece di " + Check2SaysHealthy);
            }

            if (list.Count == 0)
                return Failed(
                    "Non trovo il corpo «" + thisOnly + "». Gli identificativi dei " +
                    "corpi stanno nello stato del documento, dentro «bodies».");

            // PIU' DI UN CORPO NON E' UN DIFETTO, ed e' un'altra cosa da
            // «rotto»: un saldato ne ha dieci per costruzione. Ma per una
            // stampa lo e', perche' uno STL di due corpi e' due gusci
            // separati, e la stampante non sa quale dei due e' il pezzo.
            bool justOne = list.Count == 1;
            if (!justOne && string.IsNullOrEmpty(thisOnly))
                reasons.Add("il pezzo ha " + list.Count + " corpi: per una stampa dev'essere uno solo");

            bool printable = reasons.Count == 0;

            // ONE BODY OF SEVERAL: THE VERDICT SPEAKS FOR THAT BODY (N61).
            // «printable» stays the verdict on the body asked for — the
            // question «is this body sound» — and `verdict_about` says which.
            // The sentence that said «si puo' esportare» of the part is
            // replaced by one that names the body and the bodies of the part;
            // the refusal already names the body in each of its reasons and
            // stays as it was. On a part of one body the verdict on the body
            // is the verdict on the part, and nothing changes.
            string verdictValue = printable
                ? "Un solo corpo solido, chiuso e senza difetti: come geometria si puo' esportare."
                : "Non lo esporterei cosi': " + string.Join("; ", reasons) + ".";
            string verdictAbout = "part";
            if (!string.IsNullOrEmpty(thisOnly) && bodiesInPart > 1)
            {
                string which = Convert.ToString(((Dictionary<string, object>)list[0])["body"]);
                verdictAbout = "body " + which;
                if (printable)
                    verdictValue =
                        "Body " + which + " is a closed solid with no defects. This verdict is about " +
                        "body " + which + " only: the part has " + bodiesInPart + " bodies, and an STL " +
                        "of the part would be " + bodiesInPart + " separate shells. Ask again without " +
                        "«body_uid» for the verdict on the whole part.";
            }

            var measurements = new Dictionary<string, object>
            {
                ["bodies"] = list.ToArray(),
                ["bodies_total"] = list.Count,
                // Every body of the part, whatever «body_uid» asked, and what
                // «printable» and «verdict» speak for: «part» or «body C2» (N61).
                ["bodies_in_part"] = bodiesInPart,
                ["verdict_about"] = verdictAbout,
                ["solids"] = solids,
                ["sheets"] = sheets,
                ["other_bodies"] = others,
                ["bodies_with_defects"] = withDefects,
                ["bodies_not_closed"] = withGaps,
                ["printable"] = printable,
                // UNA FRASE, NON UN NUMERO. Chi legge deve sapere COSA fare, e
                // «printable: false» da solo non lo dice.
                ["verdict"] = verdictValue,
                // IL LIMITE DICHIARATO, e va detto ogni volta: questa
                // operazione guarda la TOPOLOGIA, non lo spessore delle pareti
                // ne' le sovrasporgenze. Un guscio da 0,3 mm passa di qui
                // senza una parola.
                ["what_it_does_not_check"] =
                    "Lo spessore delle pareti e le sovrasporgenze non li guarda nessuno " +
                    "ancora: qui si vede se il corpo e' chiuso e sano, non se e' stampabile " +
                    "dalla macchina che hai.",
            };
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// I difetti che `Check3` elenca, col NOME e non col numero.
        ///
        /// I codici hanno un enum — `swFaultEntityErrorCode_e`, 36 valori da
        /// `swBodyCorrupt = 1` a `swEdgeTouchEdge = 36` — e lo zero NON e' fra
        /// quelli: un codice che l'enum non conosce si dice cosi', invece di
        /// scriverlo come se fosse un nome.
        ///
        /// NULL WHEN CHECK3 CANNOT BE READ (Z-C21.8, finding N16). Until library
        /// 0.144.0 an exception, or a Check3 that gave no IFaultEntity, returned
        /// the EMPTY list, the same answer as a body with no fault: a body whose
        /// check could not be read was declared sound. The sound solid of
        /// Checks\BrokenBody (15/9/2026) answers an IFaultEntity with Count 0,
        /// not null, so null is «not read» and becomes a reason of the refusal.
        /// A fault list that breaks off half way is not read either.
        /// </summary>
        private static List<string> BodyFaultNames(IBody2 body)
        {
            var outside = new List<string>();
            try
            {
                var f = body.Check3 as IFaultEntity;
                if (f == null) return null;
                int n = f.Count;
                for (int k = 0; k < n; k++)
                {
                    int code = f.get_ErrorCode(k);
                    outside.Add(DefectName(code));
                }
            }
            catch { return null; }
            return outside;
        }

        /// <summary>The reason given when Check3 cannot be read (finding N16).</summary>
        private const string DefectsNotRead =
            "Check3 gave no fault list for this body, and a check that could not be read is not a sound body";

        private static string DefectName(int code)
        {
            try
            {
                if (Enum.IsDefined(typeof(swFaultEntityErrorCode_e), code))
                    return ((swFaultEntityErrorCode_e)code).ToString() + " (" + code + ")";
            }
            catch { }
            return "codice " + code + ", che swFaultEntityErrorCode_e non conosce";
        }

        private static int Gaps(IBody2 body)
        {
            var d = body.Diagnose() as IDiagnoseResult;
            return d == null ? -1 : d.GetGapsCount();
        }

        private static string BodyTypeName(int typ)
        {
            if (typ == (int)swBodyType_e.swSolidBody) return "solid";
            if (typ == (int)swBodyType_e.swSheetBody) return "foglio";
            if (typ == (int)swBodyType_e.swWireBody) return "filo";
            if (typ == (int)swBodyType_e.swEmptyBody) return "vuoto";
            return "tipo " + typ;
        }

        private static string ABodyOf(string itemName)
        {
            return itemName == "foglio" ? "una superficie" :
                   itemName == "filo" ? "un filo" :
                   itemName == "vuoto" ? "un corpo vuoto" : "un corpo «" + itemName + "»";
        }

        /// <summary>
        /// Un intero da una chiamata COM, o il valore di ripiego. Non si
        /// ingoia il fallimento: il ripiego e' un numero che NON puo' essere
        /// scambiato per «sano», ed e' per questo che e' `int.MinValue` e non
        /// zero.
        /// </summary>
        private static int Integer(Func<int> f, int ifItCannot)
        {
            try { return f(); }
            catch { return ifItCannot; }
        }
    }
}
