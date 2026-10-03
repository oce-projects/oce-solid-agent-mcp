using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// I FOGLI DI UNA TAVOLA (voce GT1).
    ///
    /// ── PERCHE' E' LA VOCE PIU' SEMPLICE E NON LA MENO UTILE ────────────
    ///
    /// `export_pdf` esporta gia' tutti i fogli e **conta gia' le pagine del
    /// PDF**: e' la guardia scritta perche' un PDF di un foglio solo, su una
    /// tavola di tre, esce senza nessun errore e arriva in officina incompleto.
    /// Quel conteggio pero' oggi vale sempre 1, **perche' non c'e' modo di fare
    /// un secondo foglio**. Una guardia che non puo' mai scattare non e' una
    /// guardia: e' una riga di codice che consola.
    ///
    /// ── IL DIEDRO, CHE E' TUTTA LA DIFFICOLTA' ──────────────────────────
    ///
    ///     Boolean NewSheet4(String Name, Int32 PaperSize, Int32 TemplateIn,
    ///                       Double Scale1, Double Scale2, Boolean FirstAngle,
    ///                       String TemplateName, Double Width, Double Height,
    ///                       String PropertyViewName, ...)
    ///
    /// **`FirstAngle` e' un parametro obbligatorio, in sesta posizione: non c'e'
    /// niente da ereditare.** Scrivere `false` perche' e' il valore di serie
    /// darebbe **la stessa tavola con due fogli specchiati in modo diverso**, e
    /// nessuno dei due darebbe errore: il foglio 1 in primo diedro, il foglio 2
    /// in terzo, e chi taglia il pezzo del foglio 2 lo taglia rovesciato.
    ///
    /// Si fa quello che `set_sheet` fa gia': **si legge il diedro del
    /// foglio corrente e lo si ripassa**. Il modello di tavola dell'officina
    /// l'ha gia' deciso una volta, e questa e' l'unica risposta che non inventa
    /// niente.
    ///
    /// ── E NON SI CREDE AL `Boolean` ─────────────────────────────────────
    ///
    /// *Il valore che torna da chi ha fatto la cosa non e' la misura della
    /// cosa.* Il foglio si conta da `GetSheetNames().Length` prima e dopo, e il
    /// diedro del foglio nuovo si **rilegge** dal foglio nuovo.
    /// </summary>
    public partial class OperazioniSw
    {
        // ─────────────────────────────────────────────────────────────────
        // new_sheet
        // ─────────────────────────────────────────────────────────────────
        private Esito NuovoFoglio(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: i fogli stanno in una " +
                    "tavola. Creala prima con «create_drawing».");

            var nomiPrima = NomiDeiFogli(disegno);
            if (nomiPrima.Count == 0)
                return Fallito("Questa tavola non dichiara nessun foglio: non so da cosa partire.");

            string nome = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(nome)) nome = NomeLibero(nomiPrima);
            if (nomiPrima.Any(n => string.Equals(n, nome, StringComparison.OrdinalIgnoreCase)))
                return Fallito(
                    "C'e' gia' un foglio che si chiama «" + nome + "». I nomi dei fogli " +
                    "sono come si chiamano le pagine di una tavola: due uguali e non si " +
                    "sa piu' quale si sta guardando. Ci sono: " +
                    string.Join(", ", nomiPrima) + ".");

            // ── IL DIEDRO SI LEGGE DAL FOGLIO CORRENTE ──────────────────
            ISheet corrente;
            try { corrente = disegno.GetCurrentSheet() as ISheet; }
            catch (Exception ex) { return Fallito("Non riesco a leggere il foglio corrente: " + ex.Message); }
            if (corrente == null) return Fallito("La tavola non ha un foglio attivo.");

            var proprieta = ProprietaDelFoglio(corrente);
            if (proprieta == null || proprieta.Length <= 4)
                return Fallito(
                    "Non riesco a leggere il diedro del foglio corrente, e senza quello " +
                    "il foglio nuovo nascerebbe con un diedro deciso da me. Un secondo " +
                    "foglio specchiato rispetto al primo non da' nessun errore e manda " +
                    "in officina un pezzo rovesciato: preferisco fermarmi.");
            bool primoDiedro = proprieta[4] != 0;

            // La scala: se non me la dicono, quella del foglio corrente. Due
            // fogli della stessa tavola a scale diverse si possono volere, ma
            // non per distrazione.
            var scalaCorrente = LeggiScala(corrente);
            double scalaNum = Num(a, "scale_numerator",
                scalaCorrente != null && scalaCorrente.Length > 0 ? scalaCorrente[0] : 1);
            double scalaDen = Num(a, "scale_denominator",
                scalaCorrente != null && scalaCorrente.Length > 1 ? scalaCorrente[1] : 1);
            if (scalaNum <= 0 || scalaDen <= 0)
                return Fallito("La scala deve avere due numeri positivi, per esempio 1:2.");

            string nomeFormato = Str(a, "paper_size", null);
            int formato = string.IsNullOrWhiteSpace(nomeFormato)
                ? FormatoDelFoglio(corrente, proprieta)
                : FormatoFoglio(nomeFormato);

            bool risposta;
            try
            {
                risposta = disegno.NewSheet4(
                    nome,
                    formato,
                    (int)swDwgTemplates_e.swDwgTemplateNone,
                    scalaNum, scalaDen,
                    primoDiedro,               // ← RIPASSATO, non deciso qui
                    "",                        // TemplateName: nessuno
                    0, 0,                      // Width/Height: contano solo col formato «custom»
                    "",                        // PropertyViewName
                    0, 0, 0, 0, 0, 0);         // zone: non si usano
            }
            catch (Exception ex) { return Fallito("Foglio non creato: " + ex.Message); }

            // ── SI CONTA, NON SI CREDE ──────────────────────────────────
            var nomiDopo = NomiDeiFogli(disegno);
            if (nomiDopo.Count <= nomiPrima.Count)
                return Fallito(
                    "Mi e' stato risposto «" + (risposta ? "si'" : "no") + "» e i fogli sono " +
                    "sempre " + nomiPrima.Count + ". Non lo chiamo fatto.");

            // E IL DIEDRO DEL FOGLIO NUOVO SI RILEGGE DAL FOGLIO NUOVO.
            // E' la difesa vera di questa operazione: passarlo non e' averlo.
            ISheet nuovo = null;
            try { nuovo = disegno.GetCurrentSheet() as ISheet; }
            catch { }
            bool diedroDopo = primoDiedro;
            var propDopo = nuovo == null ? null : ProprietaDelFoglio(nuovo);
            if (propDopo != null && propDopo.Length > 4) diedroDopo = propDopo[4] != 0;

            if (diedroDopo != primoDiedro)
                return Fallito(
                    "Il foglio e' nato ma col diedro sbagliato: ho chiesto il " +
                    (primoDiedro ? "PRIMO" : "TERZO") + " e il foglio dice il " +
                    (diedroDopo ? "PRIMO" : "TERZO") + ". Due fogli con diedri diversi " +
                    "nella stessa tavola sono un pezzo rovesciato che non da' nessun " +
                    "errore: non dico che e' fatto.");

            var scalaDopo = nuovo == null ? null : LeggiScala(nuovo);
            var misureFoglio = nuovo == null ? null : MisureFoglio(nuovo);

            var misure = new Dictionary<string, object>
            {
                ["name"] = nome,
                ["sheets_before"] = nomiPrima.Count,
                ["sheets_after"] = nomiDopo.Count,
                ["sheets"] = nomiDopo.ToArray(),
                ["active"] = NomeDelFoglio(nuovo) ?? nome,
                ["projection_angle"] = diedroDopo ? "first" : "third",
                ["projection_angle_source"] = "riletto dal foglio nuovo, dopo averlo copiato dal foglio " +
                                     "corrente: non e' un valore scelto qui",
                ["scale"] = scalaDopo != null && scalaDopo.Length > 1
                    ? scalaDopo[0] + ":" + scalaDopo[1] : null,
                ["width_mm"] = misureFoglio != null ? (object)Math.Round(misureFoglio[0], 1) : null,
                ["height_mm"] = misureFoglio != null ? (object)Math.Round(misureFoglio[1], 1) : null,
            };

            if (!diedroDopo)
                _log("[agente] ATTENZIONE: questa tavola e' in TERZO diedro, e anche il foglio nuovo");
            _log("[agente] foglio «" + nome + "»: i fogli sono " + nomiDopo.Count);
            return SenzaFeature(misure);
        }

        // ─────────────────────────────────────────────────────────────────
        // activate_sheet
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// `ActivateSheet` prende **il NOME**, non un indice — letto nella
        /// firma, non ricordato. Un indice sarebbe stato la solita occasione
        /// per attivare il foglio sbagliato senza nessun errore.
        /// </summary>
        private Esito AttivaFoglio(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null) return Fallito("Il documento attivo non e' una tavola.");

            var nomi = NomiDeiFogli(disegno);
            string voluto = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(voluto))
                return Fallito(
                    "Dimmi quale foglio: «name», come lo riporta lo stato. Ci sono: " +
                    string.Join(", ", nomi) + ".");

            var suo = nomi.FirstOrDefault(n => string.Equals(n, voluto, StringComparison.OrdinalIgnoreCase));
            if (suo == null)
                return Fallito(
                    "Non c'e' nessun foglio che si chiami «" + voluto + "». Ci sono: " +
                    string.Join(", ", nomi) + ".");

            string prima = NomeDelFoglio(FoglioCorrente(disegno));

            bool risposta;
            try { risposta = disegno.ActivateSheet(suo); }
            catch (Exception ex) { return Fallito("Foglio non attivato: " + ex.Message); }

            // NON SI CREDE ALLA RISPOSTA: si richiede al documento quale foglio
            // ha davanti adesso.
            string dopo = NomeDelFoglio(FoglioCorrente(disegno));
            if (!string.Equals(dopo, suo, StringComparison.OrdinalIgnoreCase))
                return Fallito(
                    "Mi e' stato risposto «" + (risposta ? "si'" : "no") + "» ma il foglio " +
                    "attivo e' «" + (dopo ?? "(non leggibile)") + "», non «" + suo + "». " +
                    "Non lo chiamo fatto: i passi dopo lavorerebbero sul foglio sbagliato.");

            return SenzaFeature(new Dictionary<string, object>
            {
                ["active"] = dopo,
                ["previous"] = prima,
                ["sheets"] = nomi.ToArray(),
                ["changed"] = !string.Equals(prima, dopo, StringComparison.OrdinalIgnoreCase),
            });
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// I nomi dei fogli. `GetSheetNames()` torna `Object`: va castato, e un
        /// cast fallito e' una lista vuota, **non** zero fogli — chi chiama deve
        /// distinguere «non ce ne sono» da «non li ho potuti leggere», ed e' per
        /// questo che qui si rifiuta quando la lista e' vuota invece di andare
        /// avanti.
        /// </summary>
        private static List<string> NomiDeiFogli(IDrawingDoc disegno)
        {
            try
            {
                var nomi = disegno.GetSheetNames() as string[];
                return nomi == null ? new List<string>() : nomi.ToList();
            }
            catch { return new List<string>(); }
        }

        private static ISheet FoglioCorrente(IDrawingDoc disegno)
        {
            try { return disegno.GetCurrentSheet() as ISheet; }
            catch { return null; }
        }

        private static string NomeDelFoglio(ISheet foglio)
        {
            if (foglio == null) return null;
            try { return foglio.GetName(); } catch { return null; }
        }

        /// <summary>
        /// Il formato del foglio corrente, per darlo uguale al foglio nuovo.
        /// `GetProperties2()` mette il formato in posizione 0.
        /// </summary>
        private static int FormatoDelFoglio(ISheet foglio, double[] proprieta)
        {
            try
            {
                if (proprieta != null && proprieta.Length > 0)
                    return (int)Math.Round(proprieta[0]);
            }
            catch { }
            return (int)swDwgPaperSizes_e.swDwgPaperA3size;
        }

        /// <summary>
        /// Un nome che non c'e' gia'. «Foglio2», «Foglio3»... e se per caso
        /// esistono, si va avanti a contare invece di rifiutare: chi chiede un
        /// foglio nuovo senza dargli un nome non vuole sapere che il nome di
        /// serie era occupato.
        /// </summary>
        private static string NomeLibero(List<string> esistenti)
        {
            for (int i = esistenti.Count + 1; i < esistenti.Count + 100; i++)
            {
                var tentativo = "Foglio" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!esistenti.Any(n => string.Equals(n, tentativo, StringComparison.OrdinalIgnoreCase)))
                    return tentativo;
            }
            return "Foglio" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}
