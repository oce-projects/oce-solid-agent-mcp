using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
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

        /// <summary>L'indice dell'equazione che assegna «nome», o -1.</summary>
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

        /// <summary>Scrive (o riscrive) l'equazione «nome» = destra; l'indice, o -1 se SOLIDWORKS la rifiuta.</summary>
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
            string nome = (Str(a, "nome", "") ?? "").Trim();
            if (!NomeDiVariabileBuono(nome))
                return Fallito("«nome» e' il nome della variabile: lettere, cifre e «_», comincia con una lettera " +
                               "(per esempio «parete», «gioco», «raggio_angolo»), non «" + nome + "».");
            double valore; string errore;
            string espressione = Str(a, "espressione", null);
            bool numero = a != null && a.ContainsKey("valore_mm");
            if (numero == !string.IsNullOrWhiteSpace(espressione))
                return Fallito("Dimmi O «valore_mm» (un numero in millimetri) O «espressione» (per esempio " +
                               "«2 * \"parete\" + 1mm»), uno dei due.");
            string destra;
            if (numero)
            {
                if (!NumObbligatorio(a, "valore_mm", out valore, out errore)) return Fallito(errore);
                destra = valore.ToString("0.######", Inv) + "mm";
            }
            else { valore = double.NaN; destra = espressione.Trim(); }

            var em = modello.GetEquationMgr() as IEquationMgr;
            if (em == null) return Fallito("Non leggo le equazioni di questo pezzo (GetEquationMgr non risponde).");
            double? volumePrima = VolumeMm3(modello);
            bool nuova = EquazioneDi(em, nome) < 0;
            int i = ScriviEquazione(em, nome, destra);
            if (i < 0)
                return Fallito(
                    "SOLIDWORKS non accetta «\"" + nome + "\" = " + destra + "». Se il nome e' gia' preso — per il " +
                    "SOLIDWORKS in italiano «spessore» lo e' (misurato) — scegline un altro, per esempio «parete»; " +
                    "se e' un'espressione, controlla che i nomi fra virgolette esistano.");
            try { em.EvaluateAll(); } catch { }
            try { modello.ForceRebuild3(false); } catch { }
            i = EquazioneDi(em, nome);
            double letto = double.NaN;
            try { if (i >= 0) letto = em.get_Value(i); } catch { }
            if (i < 0 || double.IsNaN(letto))
                return Fallito("La variabile «" + nome + "» non si rilegge dopo averla scritta: non so che valore ha.");
            if (numero && Math.Abs(letto - valore) > 1e-6)
                return Fallito("Ho scritto «" + nome + "» = " + valore.ToString("0.###", Inv) + " mm e rileggendola vale " +
                               letto.ToString("0.###", Inv) + ": non e' la variabile che volevi.");
            double? volumeDopo = VolumeMm3(modello);
            var misure = new Dictionary<string, object>
            {
                ["nome"] = nome,
                ["equazione"] = em.get_Equation(i),
                ["valore_mm"] = Math.Round(letto, 6),
                ["nuova"] = nuova,
                ["quote_guidate"] = QuoteGuidateDa(em, nome),
                ["volume_prima_mm3"] = NumeroOppureNull(volumePrima, 2),
                ["volume_dopo_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["unita"] = "millimetri (il valore di una variabile di lunghezza, nelle unita' del documento)",
            };
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>Z-F1: lega una quota a un'espressione (una variabile, un conto), e la rilegge.</summary>
        private Esito LegaQuota(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Lego le quote di un pezzo. Aprimi il pezzo.");
            string nomeQuota = (Str(a, "quota", "") ?? "").Trim();
            string espressione = (Str(a, "espressione", "") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nomeQuota))
                return Fallito("Dimmi «quota»: il nome della quota dallo stato del documento (per esempio «D1@Schizzo1»).");
            if (string.IsNullOrWhiteSpace(espressione))
                return Fallito("Dimmi «espressione»: a cosa legarla, per esempio «\"parete\"» o «2 * \"parete\"».");
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
            double prima = Unita.Mm(quota.SystemValue);
            double? volumePrima = VolumeMm3(modello);
            int i = ScriviEquazione(em, nomeEq, espressione);
            if (i < 0)
                return Fallito("SOLIDWORKS non accetta «\"" + nomeEq + "\" = " + espressione + "». Controlla che le " +
                               "variabili fra virgolette esistano (le crei con «variabile_globale») e che la quota " +
                               "non sia gia' guidata da un'altra equazione.");
            try { em.EvaluateAll(); } catch { }
            try { modello.ForceRebuild3(false); } catch { }
            i = EquazioneDi(em, nomeEq);
            double atteso = double.NaN;
            try { if (i >= 0) atteso = em.get_Value(i); } catch { }
            double dopo = Unita.Mm(quota.SystemValue);
            if (double.IsNaN(atteso) || Math.Abs(dopo - atteso) > 0.001)
                return Fallito("Ho legato «" + nomeEq + "» a «" + espressione + "», ma la quota dice " + dopo.ToString("0.###", Inv) +
                               " mm e l'equazione " + (double.IsNaN(atteso) ? "non si rilegge" : atteso.ToString("0.###", Inv)) +
                               ": il legame non ha preso.");
            double? volumeDopo = VolumeMm3(modello);
            var misure = new Dictionary<string, object>
            {
                ["quota"] = nomeEq,
                ["equazione"] = em.get_Equation(i),
                ["valore_prima_mm"] = Math.Round(prima, 4),
                ["valore_dopo_mm"] = Math.Round(dopo, 4),
                ["volume_prima_mm3"] = NumeroOppureNull(volumePrima, 2),
                ["volume_dopo_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["giudizio"] = "La quota «" + nomeEq + "» adesso la guida «" + espressione + "»: cambiando la variabile " +
                               "cambia lei. Con «modifica_quota» non si cambia piu' (e' guidata).",
            };
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>Le equazioni che usano «nome» a destra, con la quota che guidano e il suo valore.</summary>
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
                    ["guidata"] = sinistra,
                    ["equazione"] = eq,
                    ["valore"] = double.IsNaN(v) ? null : (object)Math.Round(v, 6),
                });
            }
            return fuori.ToArray();
        }

        /// <summary>Per lo stato: le equazioni del documento (le prime venti), o null se non ce ne sono.</summary>
        internal static object[] EquazioniDelDocumento(IModelDoc2 modello)
        {
            try
            {
                var em = modello.GetEquationMgr() as IEquationMgr;
                if (em == null) return null;
                int n = em.GetCount();
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
                        ["equazione"] = eq,
                        ["valore"] = double.IsNaN(v) ? null : (object)Math.Round(v, 6),
                        ["variabile_globale"] = g,
                    });
                }
                return fuori.ToArray();
            }
            catch { return null; }
        }
    }
}
