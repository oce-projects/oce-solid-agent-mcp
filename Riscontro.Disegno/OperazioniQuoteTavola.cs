using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// QUOTE SU UNA VISTA DI TAVOLA, CREATE — non importate (voce K10).
    ///
    /// ── PERCHE' ESISTE, E PERCHE' NON SI CHIAMA «IMPORTA» ───────────────
    ///
    /// `quote_dal_modello` **importa** le quote che il modello ha gia', con
    /// `InsertModelAnnotations*`. Misurato il 18/9/2026: porta **zero**, su un
    /// pezzo con sette quote tutte marcate per il disegno, su **otto** modelli
    /// di tavola diversi (sette di serie piu' quello dell'officina), con cinque
    /// strade di import e il controllo positivo verde nello stesso giro
    /// (`InsertNote` sulla stessa tavola funziona). Quella strada e' chiusa.
    ///
    /// Ma il fine non e' «le quote del modello in tavola»: e' **una tavola
    /// quotata**. *Un nome che descrive il MEZZO invece del fine restringe le
    /// soluzioni prima che qualcuno le cerchi* — e per due giorni il nome
    /// dell'altra operazione ha deciso la strada al posto mio.
    ///
    /// ── LA MISURA CHE HA APERTO QUESTA STRADA ───────────────────────────
    ///
    ///     spigoli visibili nella vista: 7
    ///       spigolo 0..3: AddDimension -> un oggetto,  quote 0 -> 4
    ///
    ///     COSA MISURANO (pezzo 120 x 80 x 10, smusso 5):
    ///       RD1  120 mm     RD2  120 mm
    ///       RD3    5 mm     RD4    5 mm
    ///
    /// Quattro su quattro, **fuori processo**, e con i valori giusti. Sono
    /// quote di riferimento (`RD`), che e' il tipo giusto per una tavola.
    ///
    /// **E il valore si rilegge, non si presume.** Il primo giro della sonda
    /// rispondeva `?` al valore, e con quel `?` la strada sarebbe sembrata
    /// aperta lo stesso: *quattro quote comparse non sono quattro quote
    /// giuste*, e una quota che misura un'altra cosa e' peggio di una quota che
    /// manca — sulla carta si legge uguale.
    ///
    /// ── LE DUE TRAPPOLE, TUTTE E DUE GIA' PAGATE DA QUESTO PROGETTO ─────
    ///
    /// 1. `swInputDimValOnCreate` **era acceso** su questa installazione.
    ///    Lasciato cosi', `AddDimension` apre la finestra «Modifica» e la
    ///    chiamata COM **non torna piu'**: il ponte si appende, e il client
    ///    dietro con lui. Si spegne prima e si **rimette com'era in un
    ///    `finally`** — una preferenza lasciata spenta cambia SOLIDWORKS per
    ///    chi lo usa dopo, e nessuno collegherebbe la cosa a questa operazione.
    /// 2. Le coordinate di `AddDimension` sono del **FOGLIO**, non del modello.
    ///    Sbagliarle mette la quota fuori dal foglio **senza dare errore**.
    ///    Qui si prendono da `IView.GetOutline()`, che e' l'ingombro della
    ///    vista sul foglio, e si scalano in metri.
    ///
    /// ── GLI SPIGOLI SI CHIEDONO ALLA VISTA ──────────────────────────────
    ///
    ///     Object IView.GetVisibleEntities2(Component2 comp, Int32 entityType)
    ///
    /// con `swViewEntityType_e.swViewEntityType_Edge`. Non si indovinano
    /// coordinate su cui cliccare: si prende l'elenco che la vista stessa
    /// espone e si seleziona con `IEntity.Select4`.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito QuotaInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: le quote stanno su una " +
                    "vista di disegno. Crea prima la tavola con «crea_disegno».");

            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0)
                return Fallito(
                    "La tavola non ha viste: le quote non avrebbero dove attaccarsi. " +
                    "Metti prima le viste con «viste_standard» o «vista_modello».");

            string voluta = Str(a, "vista", null);
            if (!string.IsNullOrWhiteSpace(voluta) && !NomeDiVistaConosciuto(nomi, voluta))
                return Fallito(
                    "Non c'e' nessuna vista che si chiami «" + voluta + "». Ci sono: " +
                    string.Join(", ", nomi) + ".");

            IView vista = VistaPerNome(disegno, voluta);
            if (vista == null)
                return Fallito("Non riesco a prendere la vista su cui quotare.");

            string nomeVista = "";
            try { nomeVista = vista.GetName2() ?? ""; } catch { }

            // ── GLI SPIGOLI CHE LA VISTA MOSTRA ─────────────────────────
            object[] spigoli = null;
            try
            {
                spigoli = vista.GetVisibleEntities2(
                    null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            }
            catch (Exception ex)
            {
                return Fallito("Non riesco a chiedere gli spigoli alla vista: " + ex.Message);
            }

            int disponibili = spigoli == null ? 0 : spigoli.Length;
            if (disponibili == 0)
                return Fallito(
                    "La vista «" + nomeVista + "» non mostra nessuno spigolo quotabile. " +
                    "Su una vista vuota, o su una vista in cui il modello non e' " +
                    "risolto, non c'e' niente da quotare — e mettere zero quote " +
                    "rispondendo «fatto» sarebbe peggio.");

            // QUALI SPIGOLI. Senza dirlo si quotano i primi, e quanti si dice.
            var scelti = new List<int>();
            var elencoChiesto = Lista(a, "spigoli");
            if (elencoChiesto.Count > 0)
            {
                foreach (var o in elencoChiesto)
                {
                    int i;
                    if (!int.TryParse((o ?? "").Trim(), out i))
                        return Fallito("«spigoli» vuole numeri, e «" + o + "» non lo e'.");
                    if (i < 0 || i >= disponibili)
                        return Fallito(
                            "Lo spigolo " + i + " non c'e': la vista «" + nomeVista +
                            "» ne mostra " + disponibili + " (da 0 a " + (disponibili - 1) + ").");
                    if (!scelti.Contains(i)) scelti.Add(i);
                }
            }
            else
            {
                int quante = (int)Num(a, "quante", 4);
                if (quante <= 0)
                    return Fallito("«quante» dev'essere almeno 1: zero quote non sono una quota.");
                for (int i = 0; i < Math.Min(quante, disponibili); i++) scelti.Add(i);
            }

            // ── DOVE METTERLE, in coordinate FOGLIO ─────────────────────
            double[] contorno = null;
            try { contorno = vista.GetOutline() as double[]; } catch { }
            if (contorno == null || contorno.Length < 4)
                return Fallito(
                    "Non riesco a leggere l'ingombro della vista sul foglio, e senza " +
                    "quello le quote finirebbero in un punto qualunque — fuori dal " +
                    "foglio, se va male, e senza nessun errore.");

            double xMezzo = (contorno[0] + contorno[2]) / 2;
            double sottoLaVista = contorno[1] - Num(a, "distacco_mm", 12) / 1000.0;
            double passo = Num(a, "passo_mm", 10) / 1000.0;

            int prima = ContaQuote(disegno);

            // ── LA TRAPPOLA DEL MODALE, spenta e rimessa ────────────────
            bool chiedevaIlValore = true;
            bool spenta = false;
            var nate = new List<Dictionary<string, object>>();
            var falliti = new List<string>();
            try
            {
                try
                {
                    chiedevaIlValore = _sw.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                    _sw.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                    spenta = true;
                }
                catch { /* se non si puo' spegnere lo dira' il primo che si appende */ }

                for (int k = 0; k < scelti.Count; k++)
                {
                    int i = scelti[k];
                    int quotePrima = ContaQuote(disegno);
                    try
                    {
                        modello.ClearSelection2(true);
                        var ent = spigoli[i] as IEntity;
                        if (ent == null) { falliti.Add(i + " (non e' un'entita')"); continue; }
                        if (!ent.Select4(false, null)) { falliti.Add(i + " (non si seleziona)"); continue; }

                        double y = sottoLaVista - k * passo;
                        var natura = modello.Extension.AddDimension(xMezzo, y, 0, 0)
                                     as IDisplayDimension;
                        try { modello.EditRebuild3(); } catch { }

                        if (ContaQuote(disegno) <= quotePrima)
                        {
                            // NON SI GUARDA SE `AddDimension` HA RISPOSTA: si
                            // guarda se la quota c'e'. E' la regola di casa —
                            // il valore che torna da chi ha fatto la cosa non
                            // e' la misura della cosa.
                            falliti.Add(i + " (chiamata andata, quota non nata)");
                            continue;
                        }
                        nate.Add(DescriviQuota(natura, i));
                    }
                    catch (Exception ex)
                    {
                        var m = ex.Message ?? "";
                        falliti.Add(i + " (" + (m.Length > 90 ? m.Substring(0, 90) + "..." : m) + ")");
                    }
                }
            }
            finally
            {
                if (spenta)
                    try
                    {
                        _sw.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swInputDimValOnCreate, chiedevaIlValore);
                    }
                    catch { }
                try { modello.ClearSelection2(true); } catch { }
            }

            int dopo = ContaQuote(disegno);
            int messe = dopo - prima;

            if (messe <= 0)
                return Fallito(
                    "Nessuna quota e' nata (erano " + prima + ", sono " + dopo + "). " +
                    "La vista «" + nomeVista + "» mostra " + disponibili + " spigoli e ne " +
                    "ho provati " + scelti.Count +
                    (falliti.Count == 0 ? "" : ": " + string.Join(", ", falliti)) + ". " +
                    "Se la finestra «Modifica» e' rimasta accesa, la chiamata si appende " +
                    "invece di fallire: qui non e' successo, quindi e' altro.");

            var misure = new Dictionary<string, object>
            {
                ["vista"] = nomeVista,
                ["spigoli_disponibili"] = disponibili,
                ["spigoli_provati"] = scelti.Count,
                ["quote_prima"] = prima,
                ["quote_dopo"] = dopo,
                ["quote_messe"] = messe,
                // OGNI QUOTA COL SUO VALORE RILETTO, in millimetri. E' il pezzo
                // che distingue questa operazione da una che disegna numeri.
                ["quote"] = nate.ToArray(),
                // E LE STESSE MISURE IN FILA, SENZA L'ORDINE.
                //
                // `quote[0].mm` costringe chi prova a sapere **in che ordine**
                // la vista elenca i suoi spigoli — un ordine che nessuno
                // dichiara, e che cambia col pezzo. Il 18/9/2026 un caso di
                // banco e' stato scritto su `quote[0]` e `quote[2]` prendendo i
                // numeri da una sonda fatta su un ALTRO pezzo: sette spigoli
                // invece di quattro, e l'indice non voleva piu' dire niente.
                //
                // Qui ci sono i millimetri e basta: si chiede «c'e' il 120?»
                // invece di «il terzo e' 5?», e la domanda resta vera anche se
                // SOLIDWORKS cambia l'ordine.
                // *Un'attesa che dipende da un ordine non dichiarato misura
                // l'ordine, non la cosa.*
                ["quote_mm"] = MillimetriInFila(nate),
                ["spigoli_non_quotati"] = falliti.ToArray(),
                ["dove_le_ho_messe"] =
                    "sotto l'ingombro della vista, in coordinate FOGLIO: le quote " +
                    "non si spostano da sole, e se si sovrappongono al disegno vanno " +
                    "spostate a mano o con un «distacco_mm» piu' grande.",
                ["cosa_non_guarda"] =
                    "Che le quote siano LEGGIBILI: non guardo se si accavallano fra " +
                    "loro o col disegno, ne' se la quota scelta e' quella che serve " +
                    "in officina. Quotare bene e' un mestiere; questa operazione mette " +
                    "quote giuste, non una tavola bella.",
            };

            _log("[agente] " + messe + " quote sulla vista «" + nomeVista + "»: " +
                 ValoriInRiga(nate));
            return SenzaFeature(misure);
        }

        /// <summary>
        /// Nome e VALORE di una quota appena nata, il valore riletto da
        /// SOLIDWORKS e non quello che credevo di aver chiesto.
        ///
        /// `GetSystemValue2("")` torna **metri**, sempre, qualunque siano le
        /// unita' del documento: e' l'unica lettura che non dipende da come e'
        /// impostata la tavola. `IDimension.Value` invece segue le unita' del
        /// documento, quindi si riporta accanto ma non si usa per confrontare.
        /// </summary>
        private static Dictionary<string, object> DescriviQuota(IDisplayDimension d, int spigolo)
        {
            var fuori = new Dictionary<string, object> { ["spigolo"] = spigolo };
            if (d == null)
            {
                // La quota c'e' — il conteggio e' salito — ma l'oggetto non e'
                // tornato. Si dice, invece di far sparire la riga: un elenco
                // piu' corto del conteggio farebbe cercare un difetto che non
                // c'e'.
                fuori["nome"] = "(nata, ma AddDimension non ha risposto l'oggetto)";
                return fuori;
            }
            try
            {
                var dim = d.GetDimension2(0) as IDimension;
                if (dim != null)
                {
                    try { fuori["nome"] = dim.FullName; } catch { }
                    try { fuori["mm"] = Math.Round(dim.GetSystemValue2("") * 1000, 3); } catch { }
                }
            }
            catch { }
            if (!fuori.ContainsKey("mm"))
                fuori["mm"] = null;   // «non l'ho potuto leggere», non «zero»
            return fuori;
        }

        /// <summary>
        /// I millimetri delle quote nate, in fila e senza l'indice dello
        /// spigolo. Le quote che non si e' potuto rileggere **non entrano**:
        /// un `null` in mezzo a dei numeri si confronterebbe come stringa
        /// vuota e passerebbe qualunque «non contiene».
        /// </summary>
        private static object[] MillimetriInFila(List<Dictionary<string, object>> quote)
        {
            var fuori = new List<object>();
            foreach (var q in quote)
            {
                object v;
                if (q.TryGetValue("mm", out v) && v != null) fuori.Add(v);
            }
            return fuori.ToArray();
        }

        private static string ValoriInRiga(List<Dictionary<string, object>> quote)
        {
            var pezzi = new List<string>();
            foreach (var q in quote)
            {
                object v;
                pezzi.Add(q.TryGetValue("mm", out v) && v != null ? v + " mm" : "?");
            }
            return string.Join(", ", pezzi);
        }

        /// <summary>La vista chiesta per nome, o la prima vera se non si dice.</summary>
        private static IView VistaPerNome(IDrawingDoc disegno, string voluta)
        {
            try
            {
                // La prima «vista» e' il FOGLIO: le viste vere cominciano dopo.
                var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                if (string.IsNullOrWhiteSpace(voluta)) return v;
                while (v != null)
                {
                    string suo = "";
                    try { suo = v.GetName2() ?? ""; } catch { }
                    if (StessoNome(suo, voluta)) return v;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
        }
    }
}
