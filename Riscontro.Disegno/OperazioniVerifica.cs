using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LE OPERAZIONI CHE NON DISEGNANO: dicono com'e' venuto. (voce GP1)
    ///
    /// ── PERCHE' ESISTONO ─────────────────────────────────────────────────
    ///
    /// Fino a qui nessuna delle operazioni del catalogo sapeva rispondere alla
    /// domanda «com'e' venuto?». Le misure c'erano — `taglio_rettangolare`
    /// conta il materiale tolto, `telaio_rettangolare` confronta l'ingombro
    /// col promesso, `foro_guidato` misura dove e' finito l'asse — ma erano
    /// **private e interne alle singole operazioni**: il modello non poteva
    /// chiamarne nessuna.
    ///
    /// `distinta_taglio` e' il precedente: un'operazione di catalogo che LEGGE
    /// invece di disegnare. Questa fa lo stesso su una domanda diversa.
    ///
    /// ── LA DOMANDA A CUI RISPONDE, E PERCHE' NON E' OVVIA ────────────────
    ///
    /// «Questo corpo si puo' stampare?» In pratica: **e' un solido solo,
    /// chiuso, senza difetti topologici**. Le quattro chiamate che
    /// SOLIDWORKS offre per saperlo non dicono la stessa cosa, e due di loro
    /// tacciono proprio sul caso che conta.
    ///
    /// Misurato il 15/9/2026 con `Controlli\CorpoRotto`, su SOLIDWORKS 2021:
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
    public partial class OperazioniSw
    {
        /// <summary>
        /// Il valore che `Check()` da' su un corpo sano. **Uno, non zero**, e
        /// il nome esiste perche' la costante nuda in mezzo a un `if` sarebbe
        /// indistinguibile da un refuso.
        /// </summary>
        private const int CheckDiceSano = 1;

        /// <summary>
        /// Il valore che `Check2()` da' su un corpo sano. **Zero, non uno.**
        /// Sullo stesso corpo, con l'altra convenzione.
        /// </summary>
        private const int Check2DiceSano = 0;

        private Esito VerificaGeometriaCorpo(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito("Questo documento non e' un pezzo: non ha corpi da verificare.");

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
            // is the «unstable order» its verdict names.
            var grezzi = parte.GetBodies2((int)swBodyType_e.swAllBodies, false) as object[];
            if (grezzi == null || grezzi.Length == 0)
                return Fallito(
                    "Il pezzo non ha nessun corpo: non c'e' niente da verificare. " +
                    "Fai prima un'estrusione.");

            string soloQuesto = Str(a, "corpo_uid", null);

            // THE BODIES OF THE PART, counted before the «corpo_uid» filter
            // (Z-C21.10, finding N61). IPartDoc~GetBodies2.html: «false gets
            // all of the bodies in the part». With one body asked for,
            // «corpi_totali» is 1 on a part of two, and the verdict read «Un solo
            // corpo solido ... si puo' esportare»: a sentence about the PART,
            // which has two bodies and would give an STL of two shells.
            int bodiesInPart = grezzi.Count(o => o is IBody2);

            var elenco = new List<object>();
            int solidi = 0, fogli = 0, altri = 0;
            int conDifetti = 0, conFessure = 0;
            var motivi = new List<string>();

            for (int i = 0; i < grezzi.Length; i++)
            {
                var corpo = grezzi[i] as IBody2;
                if (corpo == null) continue;

                string uid = "C" + (i + 1);
                if (!string.IsNullOrEmpty(soloQuesto) &&
                    !string.Equals(uid, soloQuesto, StringComparison.OrdinalIgnoreCase))
                    continue;

                int tipo = Intero(() => corpo.GetType(), -1);
                string comeSiChiama = NomeDelTipoDiCorpo(tipo);
                if (tipo == (int)swBodyType_e.swSolidBody) solidi++;
                else if (tipo == (int)swBodyType_e.swSheetBody) fogli++;
                else altri++;

                int check = Intero(() => corpo.Check(), int.MinValue);
                int check2 = Intero(() => corpo.Check2(), int.MinValue);
                // null: Check3 could not be read (Z-C21.8, finding N16).
                var difetti = BodyFaultNames(corpo);
                int fessure = Intero(() => Fessure(corpo), -1);

                if (difetti != null && difetti.Count > 0) conDifetti++;
                if (fessure > 0) conFessure++;

                var voce = new Dictionary<string, object>
                {
                    ["corpo"] = uid,
                    ["tipo"] = comeSiChiama,
                    ["facce"] = Intero(() => corpo.GetFaceCount(), -1),
                    ["chiuso"] = fessure == 0,
                    ["fessure"] = fessure,
                    ["difetti"] = difetti == null ? null : (object)difetti.Count,
                    // I NUMERI GREZZI RESTANO, e non e' ridondanza: quando un
                    // giorno una versione di SOLIDWORKS cambiera' una delle due
                    // convenzioni, e' da qui che lo si vedra' invece che da un
                    // giudizio girato al contrario.
                    ["check_grezzo"] = check,
                    ["check2_grezzo"] = check2,
                };
                if (difetti != null && difetti.Count > 0) voce["difetti_elenco"] = difetti.ToArray();
                if (difetti == null) voce["defects_not_read"] = DefectsNotRead;
                elenco.Add(voce);

                if (tipo != (int)swBodyType_e.swSolidBody)
                    motivi.Add("il corpo " + uid + " e' " + UnCorpoDi(comeSiChiama) +
                               ", non un solido: non racchiude un volume");
                if (fessure > 0)
                    motivi.Add("il corpo " + uid + " ha " + fessure +
                               (fessure == 1 ? " fessura" : " fessure") + ": non e' chiuso");
                // NOT COUNTED IS NOT CLOSED (Z-C21.8, with N16): `Fessure` answers
                // -1 when Diagnose gives nothing, and the closed field was already
                // false, but no reason was added and the verdict could stay «can
                // be printed».
                if (fessure < 0)
                    motivi.Add("the gaps of body " + uid + " could not be counted (Diagnose gave no answer): " +
                               "not counted is not closed");
                if (difetti == null)
                    motivi.Add("the defects of body " + uid + " could not be read: " + DefectsNotRead);
                else if (difetti.Count > 0)
                    motivi.Add("il corpo " + uid + " ha " + difetti.Count +
                               (difetti.Count == 1 ? " difetto: " : " difetti: ") +
                               string.Join(", ", difetti));
                if (check != CheckDiceSano)
                    motivi.Add("il corpo " + uid + " ha Check() = " + check +
                               " invece di " + CheckDiceSano);
                if (check2 != Check2DiceSano)
                    motivi.Add("il corpo " + uid + " ha Check2() = " + check2 +
                               " invece di " + Check2DiceSano);
            }

            if (elenco.Count == 0)
                return Fallito(
                    "Non trovo il corpo «" + soloQuesto + "». Gli identificativi dei " +
                    "corpi stanno nello stato del documento, dentro «corpi».");

            // PIU' DI UN CORPO NON E' UN DIFETTO, ed e' un'altra cosa da
            // «rotto»: un saldato ne ha dieci per costruzione. Ma per una
            // stampa lo e', perche' uno STL di due corpi e' due gusci
            // separati, e la stampante non sa quale dei due e' il pezzo.
            bool unoSolo = elenco.Count == 1;
            if (!unoSolo && string.IsNullOrEmpty(soloQuesto))
                motivi.Add("il pezzo ha " + elenco.Count + " corpi: per una stampa dev'essere uno solo");

            bool stampabile = motivi.Count == 0;

            // ONE BODY OF SEVERAL: THE VERDICT SPEAKS FOR THAT BODY (N61).
            // «stampabile» stays the verdict on the body asked for — the
            // question «is this body sound» — and `verdict_about` says which.
            // The sentence that said «si puo' esportare» of the part is
            // replaced by one that names the body and the bodies of the part;
            // the refusal already names the body in each of its reasons and
            // stays as it was. On a part of one body the verdict on the body
            // is the verdict on the part, and nothing changes.
            string giudizio = stampabile
                ? "Un solo corpo solido, chiuso e senza difetti: come geometria si puo' esportare."
                : "Non lo esporterei cosi': " + string.Join("; ", motivi) + ".";
            string verdictAbout = "part";
            if (!string.IsNullOrEmpty(soloQuesto) && bodiesInPart > 1)
            {
                string which = Convert.ToString(((Dictionary<string, object>)elenco[0])["corpo"]);
                verdictAbout = "body " + which;
                if (stampabile)
                    giudizio =
                        "Body " + which + " is a closed solid with no defects. This verdict is about " +
                        "body " + which + " only: the part has " + bodiesInPart + " bodies, and an STL " +
                        "of the part would be " + bodiesInPart + " separate shells. Ask again without " +
                        "«corpo_uid» for the verdict on the whole part.";
            }

            var misure = new Dictionary<string, object>
            {
                ["corpi"] = elenco.ToArray(),
                ["corpi_totali"] = elenco.Count,
                // Every body of the part, whatever «corpo_uid» asked, and what
                // «stampabile» and «giudizio» speak for: «part» or «body C2» (N61).
                ["bodies_in_part"] = bodiesInPart,
                ["verdict_about"] = verdictAbout,
                ["solidi"] = solidi,
                ["fogli"] = fogli,
                ["altri_corpi"] = altri,
                ["corpi_con_difetti"] = conDifetti,
                ["corpi_non_chiusi"] = conFessure,
                ["stampabile"] = stampabile,
                // UNA FRASE, NON UN NUMERO. Chi legge deve sapere COSA fare, e
                // «stampabile: false» da solo non lo dice.
                ["giudizio"] = giudizio,
                // IL LIMITE DICHIARATO, e va detto ogni volta: questa
                // operazione guarda la TOPOLOGIA, non lo spessore delle pareti
                // ne' le sovrasporgenze. Un guscio da 0,3 mm passa di qui
                // senza una parola.
                ["cosa_non_guarda"] =
                    "Lo spessore delle pareti e le sovrasporgenze non li guarda nessuno " +
                    "ancora: qui si vede se il corpo e' chiuso e sano, non se e' stampabile " +
                    "dalla macchina che hai.",
            };
            return RiuscitoSenzaCreare(misure);
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
        /// Controlli\CorpoRotto (15/9/2026) answers an IFaultEntity with Count 0,
        /// not null, so null is «not read» and becomes a reason of the refusal.
        /// A fault list that breaks off half way is not read either.
        /// </summary>
        private static List<string> BodyFaultNames(IBody2 corpo)
        {
            var fuori = new List<string>();
            try
            {
                var f = corpo.Check3 as IFaultEntity;
                if (f == null) return null;
                int n = f.Count;
                for (int k = 0; k < n; k++)
                {
                    int codice = f.get_ErrorCode(k);
                    fuori.Add(NomeDelDifetto(codice));
                }
            }
            catch { return null; }
            return fuori;
        }

        /// <summary>The reason given when Check3 cannot be read (finding N16).</summary>
        private const string DefectsNotRead =
            "Check3 gave no fault list for this body, and a check that could not be read is not a sound body";

        private static string NomeDelDifetto(int codice)
        {
            try
            {
                if (Enum.IsDefined(typeof(swFaultEntityErrorCode_e), codice))
                    return ((swFaultEntityErrorCode_e)codice).ToString() + " (" + codice + ")";
            }
            catch { }
            return "codice " + codice + ", che swFaultEntityErrorCode_e non conosce";
        }

        private static int Fessure(IBody2 corpo)
        {
            var d = corpo.Diagnose() as IDiagnoseResult;
            return d == null ? -1 : d.GetGapsCount();
        }

        private static string NomeDelTipoDiCorpo(int tipo)
        {
            if (tipo == (int)swBodyType_e.swSolidBody) return "solido";
            if (tipo == (int)swBodyType_e.swSheetBody) return "foglio";
            if (tipo == (int)swBodyType_e.swWireBody) return "filo";
            if (tipo == (int)swBodyType_e.swEmptyBody) return "vuoto";
            return "tipo " + tipo;
        }

        private static string UnCorpoDi(string nome)
        {
            return nome == "foglio" ? "una superficie" :
                   nome == "filo" ? "un filo" :
                   nome == "vuoto" ? "un corpo vuoto" : "un corpo «" + nome + "»";
        }

        /// <summary>
        /// Un intero da una chiamata COM, o il valore di ripiego. Non si
        /// ingoia il fallimento: il ripiego e' un numero che NON puo' essere
        /// scambiato per «sano», ed e' per questo che e' `int.MinValue` e non
        /// zero.
        /// </summary>
        private static int Intero(Func<int> f, int seNonSiPuo)
        {
            try { return f(); }
            catch { return seNonSiPuo; }
        }
    }
}
