using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// VARIABILI GLOBALI ED EQUAZIONI (voce Z-F1): non ripetere un numero,
    /// dargli un nome. `parete = 3mm`, e la quota guidata da `parete` sopravvive a
    /// una modifica a monte per costruzione — e' il sistema di identita' che
    /// SOLIDWORKS ha gia', e un modello parametrico e' l'unico che chi non
    /// progetta puo' cambiare senza rompere niente.
    ///
    /// Misurato il 27/9/2026 (`prova0089 zf1`, `zf1b`, piastra 120x80x10):
    ///
    ///  * `IEquationMgr.Add3(-1, "\"parete\" = 12mm", true, swAllConfiguration,
    ///    null)` torna l'indice (0); `"\"D1@Estrusione-Estrusione1\" = \"parete\""`
    ///    lega lo spessore: volume 96000 -> 115200. `set_Equation` a 15mm, poi
    ///    `EvaluateAll` e ricostruzione: 144000, e la quota dice 15.
    ///  * Le espressioni vanno: `"doppio" = 2 * "parete" + 1mm` vale 31.
    ///  * **«spessore» e «Spessore» sono RIFIUTATI** (Add3 -1) anche col pezzo
    ///    senza proprieta': per il SOLIDWORKS in italiano e' un nome gia' preso, e
    ///    un'equazione che lo usa vale 3. Un nome rifiutato si dice, e si propone
    ///    un altro nome.
    ///  * `get_Status` NON e' affidabile: -1 anche dopo un EvaluateAll riuscito,
    ///    1 dopo un set_Equation riuscito. L'esito si legge dai VALORI: quello
    ///    dell'equazione e quello della quota, riletti.
    ///  * Il nome di una quota per l'equazione e' «D1@Estrusione-Estrusione1»:
    ///    il `FullName` senza il pezzo in coda (`@Parte1089.Part`).
    /// </summary>
    public partial class OperazioniSw
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>L'indice dell'equazione che assegna «name», o -1.</summary>
        private static int EquazioneDi(IEquationMgr em, string nome)
        {
            string testa = "\"" + nome + "\"";
            for (int i = 0; i < em.GetCount(); i++)
            {
                string eq = "";
                try { eq = em.get_Equation(i) ?? ""; } catch { }
                int uguale = eq.IndexOf('=');
                if (uguale > 0 && string.Equals(eq.Substring(0, uguale).Trim(), testa, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>Scrive (o riscrive) l'equazione «name» = destra; l'indice, o -1 se SOLIDWORKS la rifiuta.</summary>
        private static int ScriviEquazione(IEquationMgr em, string nome, string destra)
        {
            string eq = "\"" + nome + "\" = " + destra;
            int i = EquazioneDi(em, nome);
            if (i >= 0)
            {
                string vecchia = em.get_Equation(i);
                try { em.set_Equation(i, eq); } catch { return -1; }
                // SI RILEGGE: set_Equation non risponde, e Status non si legge.
                if (!string.Equals(em.get_Equation(i), eq, StringComparison.Ordinal))
                {
                    try { em.set_Equation(i, vecchia); } catch { }
                    return -1;
                }
                return i;
            }
            try { return em.Add3(-1, eq, true, (int)swInConfigurationOpts_e.swAllConfiguration, null); }
            catch { return -1; }
        }

        private static bool NomeDiVariabileBuono(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome) || nome.Length > 40) return false;
            if (!char.IsLetter(nome[0])) return false;
            foreach (var ch in nome) if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
            return true;
        }

        /// <summary>Z-F1: crea o cambia una variabile globale, e rilegge cosa guida.</summary>
        private Esito VariabileGlobale(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Le variabili globali le metto su un pezzo (le parti le quotano). Aprimi il pezzo.");
            string nome = (Str(a, "name", "") ?? "").Trim();
            if (!NomeDiVariabileBuono(nome))
                return Fallito("«name» e' il nome della variabile: lettere, cifre e «_», comincia con una lettera " +
                               "(per esempio «parete», «gioco», «raggio_angolo»), non «" + nome + "».");
            double valore; string errore;
            string espressione = Str(a, "expression", null);
            bool numero = a != null && a.ContainsKey("value_mm");
            if (numero == !string.IsNullOrWhiteSpace(espressione))
                return Fallito("Dimmi O «value_mm» (un numero in millimetri) O «expression» (per esempio " +
                               "«2 * \"parete\" + 1mm»), uno dei due.");
            string destra;
            if (numero)
            {
                if (!NumObbligatorio(a, "value_mm", out valore, out errore)) return Fallito(errore);
                destra = valore.ToString("0.######", Inv) + "mm";
            }
            else { valore = double.NaN; destra = espressione.Trim(); }

            var em = modello.GetEquationMgr() as IEquationMgr;
            if (em == null) return Fallito("Non leggo le equazioni di questo pezzo (GetEquationMgr non risponde).");
            // LE GUARDIE DI set_dimension (revisione avversariale del 27/9/2026):
            // una variabile che guida delle quote cambia la geometria come una
            // quota, e una quota legata set_dimension non la cambia piu'.
            var foto = FotografaIlPezzo(modello);
            if (foto.Corpi < 0)
                return Fallito("Non riesco a contare i corpi del pezzo prima di cambiare la variabile: senza, non " +
                               "saprei dirti se la modifica lo spezza. Non ho toccato niente.");
            double? volumePrima = foto.Volume;
            int esistente = EquazioneDi(em, nome);
            bool nuova = esistente < 0;
            string vecchia = null;
            if (!nuova) { try { vecchia = em.get_Equation(esistente); } catch { } }
            int i = ScriviEquazione(em, nome, destra);
            if (i < 0)
                return Fallito(
                    "SOLIDWORKS non accetta «\"" + nome + "\" = " + destra + "». Se il nome e' gia' preso — per il " +
                    "SOLIDWORKS in italiano «spessore» lo e' (misurato) — scegline un altro, per esempio «parete»; " +
                    "se e' un'espressione, controlla che i nomi fra virgolette esistano.");
            try { em.EvaluateAll(); } catch { }
            bool ricostruito = false;
            try { ricostruito = modello.ForceRebuild3(false); } catch { }
            i = EquazioneDi(em, nome);
            double letto = double.NaN;
            try { if (i >= 0) letto = em.get_Value(i); } catch { }
            string cosa = nuova ? "le equazioni senza «" + nome + "»" : "«" + vecchia + "»";
            if (i < 0 || double.IsNaN(letto))
                return Fallito("La variabile «" + nome + "» non si rilegge dopo averla scritta: non so che valore ha." +
                               ETornatoComEra(modello, foto, RimettiLEquazione(modello, em, nome, vecchia, null), cosa));
            // IL VALORE E' NELLE UNITA' DEL DOCUMENTO: in millimetri si confronta.
            double fattore = MmPerUnitaDelDocumento(modello);
            double lettoMm = double.IsNaN(fattore) ? double.NaN : letto * fattore;
            if (numero && !double.IsNaN(lettoMm) && Math.Abs(lettoMm - valore) > 1e-6)
                return Fallito("Ho scritto «" + nome + "» = " + valore.ToString("0.###", Inv) + " mm e rileggendola vale " +
                               lettoMm.ToString("0.###", Inv) + " mm: non e' la variabile che volevi." +
                               ETornatoComEra(modello, foto, RimettiLEquazione(modello, em, nome, vecchia, null), cosa));
            string rotto = CosaSiERotto(modello, foto, ricostruito, true);
            if (rotto != null)
                return Fallito("Con «" + nome + "» = " + destra + " " + rotto + "." +
                               ETornatoComEra(modello, foto, RimettiLEquazione(modello, em, nome, vecchia, null), cosa) +
                               " Se vuoi proprio quel valore, sistema prima la lavorazione che non ci sta, poi cambia la variabile.");
            double? volumeDopo = VolumeMm3(modello);
            var misure = new Dictionary<string, object>
            {
                ["name"] = nome,
                ["equation"] = em.get_Equation(i),
                ["value_mm"] = double.IsNaN(lettoMm) ? null : (object)Math.Round(lettoMm, 6),
                ["is_new"] = nuova,
                ["driven_dimensions"] = QuoteGuidateDa(em, nome),
                ["volume_before_mm3"] = NumeroOppureNull(volumePrima, 2),
                ["volume_after_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["units"] = "millimetri (il valore di una variabile di lunghezza, riletto nelle unita' del documento e portato in mm)",
            };
            if (double.IsNaN(lettoMm))
                misure["warning"] = "Non so in che unita' e' il documento: la variabile vale " + letto.ToString("0.######", Inv) +
                                       " nelle sue unita', e non l'ho confrontata col valore chiesto.";
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>Z-F1: lega una quota a un'espressione (una variabile, un conto), e la rilegge.</summary>
        private Esito LegaQuota(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Lego le quote di un pezzo. Aprimi il pezzo.");
            string nomeQuota = (Str(a, "dimension", "") ?? "").Trim();
            string espressione = (Str(a, "expression", "") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nomeQuota))
                return Fallito("Dimmi «dimension»: il nome della quota dallo stato del documento (per esempio «D1@Schizzo1»).");
            if (string.IsNullOrWhiteSpace(espressione))
                return Fallito("Dimmi «expression»: a cosa legarla, per esempio «\"parete\"» o «2 * \"parete\"».");
            var quota = TrovaQuota(modello, nomeQuota, out string dove);
            if (quota == null)
                return Fallito("Non trovo la quota «" + nomeQuota + "» in questo documento." + (dove == null ? "" : " Ci sono: " + dove + "."));
            // IL NOME PER L'EQUAZIONE: il FullName senza il pezzo in coda.
            string completo = "";
            try { completo = quota.FullName ?? ""; } catch { }
            var pezzi = completo.Split('@');
            if (pezzi.Length < 2)
                return Fallito("Non leggo il nome completo della quota «" + nomeQuota + "» («" + completo + "»): senza, " +
                               "l'equazione non saprei scriverla.");
            string nomeEq = pezzi[0] + "@" + pezzi[1];
            var em = modello.GetEquationMgr() as IEquationMgr;
            if (em == null) return Fallito("Non leggo le equazioni di questo pezzo (GetEquationMgr non risponde).");
            // UNA QUOTA ANGOLARE si legge in gradi, una lineare in mm: SystemValue e'
            // in radianti o in metri, e il confronto con l'equazione (nelle unita'
            // del documento) si fa con GetValue3, che e' nelle stesse unita'.
            int tipoQuota = -1;
            try { tipoQuota = quota.GetType(); } catch { }
            bool angolare = tipoQuota == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular;
            double primaSistema = ApiHelpers.ReadDimension(quota);
            var foto = FotografaIlPezzo(modello);
            if (foto.Corpi < 0)
                return Fallito("Non riesco a contare i corpi del pezzo prima di legare la quota: senza, non saprei " +
                               "dirti se il legame lo spezza. Non ho toccato niente.");
            double? volumePrima = foto.Volume;
            int esistente = EquazioneDi(em, nomeEq);
            string vecchia = null;
            if (esistente >= 0) { try { vecchia = em.get_Equation(esistente); } catch { } }
            int i = ScriviEquazione(em, nomeEq, espressione);
            if (i < 0)
                return Fallito("SOLIDWORKS non accetta «\"" + nomeEq + "\" = " + espressione + "». Controlla che le " +
                               "variabili fra virgolette esistano (le crei con «global_variable»).");
            try { em.EvaluateAll(); } catch { }
            bool ricostruito = false;
            try { ricostruito = modello.ForceRebuild3(false); } catch { }
            i = EquazioneDi(em, nomeEq);
            double atteso = double.NaN;
            try { if (i >= 0) atteso = em.get_Value(i); } catch { }
            double dopoDoc = double.NaN;
            try { dopoDoc = ((double[])quota.GetValue3((int)swInConfigurationOpts_e.swThisConfiguration, null))[0]; } catch { }
            // Rimettere: l'equazione di prima, o nessuna — e allora anche il valore di
            // prima, perche' una quota slegata tiene l'ultimo valore che l'equazione le ha dato.
            Func<bool> rimetti = () => RimettiLEquazione(modello, em, nomeEq, vecchia,
                vecchia != null ? null : new Action(() => quota.SetSystemValue3(primaSistema, 2, null)));
            string cosa = vecchia != null ? "«" + vecchia + "»" : "la quota slegata, col suo valore di prima";
            if (double.IsNaN(atteso) || double.IsNaN(dopoDoc) || Math.Abs(dopoDoc - atteso) > 0.001)
                return Fallito("Ho legato «" + nomeEq + "» a «" + espressione + "», ma la quota dice " +
                               (double.IsNaN(dopoDoc) ? "?" : dopoDoc.ToString("0.###", Inv)) + " e l'equazione " +
                               (double.IsNaN(atteso) ? "non si rilegge" : atteso.ToString("0.###", Inv)) +
                               " (nelle unita' del documento): il legame non ha preso." +
                               ETornatoComEra(modello, foto, rimetti(), cosa));
            string rotto = CosaSiERotto(modello, foto, ricostruito, true);
            if (rotto != null)
                return Fallito("Legando «" + nomeEq + "» a «" + espressione + "» " + rotto + "." +
                               ETornatoComEra(modello, foto, rimetti(), cosa));
            double? volumeDopo = VolumeMm3(modello);
            double dopoSistema = ApiHelpers.ReadDimension(quota);
            var misure = new Dictionary<string, object>
            {
                ["dimension"] = nomeEq,
                ["equation"] = em.get_Equation(i),
                ["volume_before_mm3"] = NumeroOppureNull(volumePrima, 2),
                ["volume_after_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["verdict"] = "La quota «" + nomeEq + "» adesso la guida «" + espressione + "»: cambiando la variabile " +
                               "cambia lei. Con «set_dimension» non si cambia piu' (e' guidata).",
            };
            if (angolare)
            {
                misure["value_before_deg"] = Math.Round(primaSistema * 180.0 / Math.PI, 4);
                misure["value_after_deg"] = Math.Round(dopoSistema * 180.0 / Math.PI, 4);
            }
            else
            {
                misure["value_before_mm"] = Math.Round(Unita.Mm(primaSistema), 4);
                misure["value_after_mm"] = Math.Round(Unita.Mm(dopoSistema), 4);
            }
            // UN LEGAME CHE NE SOSTITUISCE UN ALTRO SI DICE (revisione del 27/9/2026):
            // prima si riscriveva in silenzio.
            if (vecchia != null) misure["link_replaced"] = vecchia;
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Rimette le equazioni com'erano: la vecchia riscritta, o la nuova tolta
        /// (con «after», per rimettere il valore di una quota slegata); poi ricalcola
        /// e ricostruisce. Vero se RILEGGENDO le equazioni sono quelle di prima —
        /// il numero che Delete risponde non si crede.
        /// </summary>
        private static bool RimettiLEquazione(IModelDoc2 modello, IEquationMgr em, string nome, string vecchia, Action dopo)
        {
            bool fatto = false;
            try
            {
                int i = EquazioneDi(em, nome);
                if (vecchia != null)
                {
                    if (i >= 0)
                    {
                        em.set_Equation(i, vecchia);
                        fatto = string.Equals(em.get_Equation(i), vecchia, StringComparison.Ordinal);
                    }
                }
                else
                {
                    if (i >= 0) em.Delete(i);
                    fatto = EquazioneDi(em, nome) < 0;
                }
                try { em.EvaluateAll(); } catch { }
                if (dopo != null) dopo();
                modello.ForceRebuild3(false);
            }
            catch { }
            return fatto;
        }

        /// <summary>Le equazioni che usano «name» a destra, con la quota che guidano e il suo valore.</summary>
        private static object[] QuoteGuidateDa(IEquationMgr em, string nome)
        {
            var fuori = new List<object>();
            string cercato = "\"" + nome + "\"";
            for (int i = 0; i < em.GetCount(); i++)
            {
                string eq = "";
                try { eq = em.get_Equation(i) ?? ""; } catch { }
                int uguale = eq.IndexOf('=');
                if (uguale < 0) continue;
                string sinistra = eq.Substring(0, uguale).Trim().Trim('"');
                if (eq.Substring(uguale + 1).IndexOf(cercato, StringComparison.OrdinalIgnoreCase) < 0) continue;
                double v = double.NaN;
                try { v = em.get_Value(i); } catch { }
                fuori.Add(new Dictionary<string, object>
                {
                    ["driven"] = sinistra,
                    ["equation"] = eq,
                    ["value_text"] = double.IsNaN(v) ? null : (object)Math.Round(v, 6),
                });
            }
            return fuori.ToArray();
        }

        /// <summary>
        /// Per lo stato: le equazioni del documento (le prime venti), o null se non ce
        /// ne sono; in «total» quante sono davvero, perche' chi legge sappia che
        /// l'elenco e' tagliato (revisione del 27/9/2026: le ultime quote guidate
        /// sparivano senza dirlo). Il «value_text» e' nelle unita' del documento.
        /// </summary>
        internal static object[] EquazioniDelDocumento(IModelDoc2 modello, out int totale)
        {
            totale = 0;
            try
            {
                var em = modello.GetEquationMgr() as IEquationMgr;
                if (em == null) return null;
                int n = em.GetCount();
                totale = n;
                if (n <= 0) return null;
                var fuori = new List<object>();
                for (int i = 0; i < n && i < 20; i++)
                {
                    double v = double.NaN; bool g = false; string eq = "";
                    try { eq = em.get_Equation(i) ?? ""; } catch { }
                    try { v = em.get_Value(i); } catch { }
                    try { g = em.get_GlobalVariable(i); } catch { }
                    fuori.Add(new Dictionary<string, object>
                    {
                        ["equation"] = eq,
                        ["value_text"] = double.IsNaN(v) ? null : (object)Math.Round(v, 6),
                        ["global_variable"] = g,
                    });
                }
                return fuori.ToArray();
            }
            catch { return null; }
        }
    }
}
