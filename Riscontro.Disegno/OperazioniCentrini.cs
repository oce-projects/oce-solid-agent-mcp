using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// CROCI DI CENTRO SUI FORI IN TAVOLA (voce W2.11).
    ///
    /// ── PERCHE' E' UN'OPERAZIONE E NON UNA RIFINITURA ───────────────────
    ///
    /// Una tavola con dodici fori e nessuna croce di centro esce, si stampa, e
    /// sembra completa. In officina manca la cosa che serve per forare: **dove
    /// sta il centro.** Nessun errore, risultato inutilizzabile.
    ///
    /// ── COSA SI E' MISURATO, DAL 17/9 AL 26/9/2026 ─────────────────────
    ///
    /// Il 17/9 tre sonde: `AutoInsertCenterMarks2` (25 combinazioni),
    /// `InsertCenterMark2/3`: «risponde True e non mette niente», contando con
    /// `GetCenterMarkCount`. Il 26/9 (`prova0089 croci2..croci7`):
    ///
    ///   1. **LE CROCI C'ERANO.** SOLIDWORKS le mette da se' sui fori quando crea
    ///      la vista (opzione del documento `swDetailingAutoInsertCenterMarksForHoles`,
    ///      accesa nel modello di casa). Si vedono nel PNG.
    ///   2. **`GetCenterMarkCount` NON LE CONTA**: 0 su una vista con tre croci.
    ///      `GetFirstCenterMark`/`GetNext` le vede tutte (`ContaCroci`).
    ///   3. **DA CODICE NON NE NASCE NESSUNA.** `AddCenterMark` sul cerchio
    ///      selezionato nella vista faceva salire `GetCenterMarkCount` 0->3 —
    ///      ma su fori che la croce l'avevano gia'; tolte le tre croci
    ///      automatiche, `AddCenterMark` non ne rimette nessuna. Sugli archi dei
    ///      raccordi, niente (4 su 4).
    ///
    /// ── QUINDI E' UN CONTROLLO ──────────────────────────────────────────
    ///
    /// Per ogni foro visibile si guarda se ha la croce (una croce entro mezzo
    /// millimetro dal centro). Se ne manca qualcuna si prova `AddCenterMark`
    /// (qui non nasce, altrove chissa'), e se resta senza si RIFIUTA dicendo la
    /// strada che funziona: l'opzione del documento accesa e la vista rifatta.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito CrociDiCentro(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: le croci di centro stanno " +
                    "su una vista di disegno. Crea prima la tavola con «crea_disegno».");

            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0)
                return Fallito(
                    "La tavola non ha viste: le croci non avrebbero dove attaccarsi. " +
                    "Metti prima le viste con «viste_standard» o «vista_modello».");

            string voluta = VistaChiestaPerNome(disegno, Str(a, "vista", null));
            if (!string.IsNullOrWhiteSpace(voluta) && !NomeDiVistaConosciuto(nomi, voluta))
                return Fallito(
                    "Non c'e' nessuna vista «" + voluta + "». Ci sono: " + string.Join(", ", nomi) +
                    " (o V1..V" + nomi.Count + ").");

            // SOLO I FORI (cerchi interi). Misurato il 26/9/2026 (`prova0089
            // croci6`): `AddCenterMark` su un ARCO di raccordo selezionato nella
            // vista non fa nascere niente, 4 archi su 4. Le asole hanno due archi
            // e una croce sola in mezzo. Tutt'e due si rifiutano, con la misura.
            string su = (Str(a, "su", "fori") ?? "fori").Trim().ToLowerInvariant();
            bool interi = true, archi = false;
            switch (su)
            {
                case "": case "fori": break;
                case "raccordi":
                case "tutti":
                    return Fallito("Sui raccordi la croce da codice non nasce: misurato, AddCenterMark su un arco " +
                                   "selezionato nella vista non mette niente (4 archi su 4). Chiedi «fori»; i centri " +
                                   "dei raccordi, se servono, vanno messi a mano in SOLIDWORKS.");
                case "asole":
                    return Fallito("Le asole non le so: un'asola ha due archi e una croce sola in mezzo, e una croce " +
                                   "per arco sarebbe sbagliata. Chiedi «fori».");
                default:
                    return Fallito("«su» puo' essere solo «fori», non «" + su + "».");
            }

            // SENZA `dimensione_mm` la dimensione del documento: e' una
            // convenzione di studio, non un numero mio.
            bool suaDimensione = a != null && a.ContainsKey("dimensione_mm");
            double dimensione = Unita.M(Num(a, "dimensione_mm", 2.5));
            if (!suaDimensione)
            {
                try
                {
                    double doc = modello.Extension.GetUserPreferenceDouble(
                        (int)swUserPreferenceDoubleValue_e.swDetailingCenterMarkSize,
                        (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);
                    if (doc > 0) dimensione = doc;
                }
                catch { }
            }
            if (dimensione <= 0) return Fallito("«dimensione_mm» deve essere maggiore di zero.");
            bool linee = Bool(a, "linee_estese", false);

            IMathUtility mu = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; } catch { }
            var selMgr = modello.SelectionManager as ISelectionMgr;

            var perVista = new List<object>();
            int totalePrima = 0, totaleDopo = 0, gia = 0;
            var nonNate = new List<string>();

            var foglio = disegno.GetFirstView() as IView;      // il primo e' IL FOGLIO
            var vista = foglio == null ? null : foglio.GetNextView() as IView;
            while (vista != null)
            {
                string nome = null;
                try { nome = vista.Name; } catch { }
                if (!string.IsNullOrWhiteSpace(voluta) && !StessoNome(nome, voluta))
                {
                    vista = vista.GetNextView() as IView;
                    continue;
                }

                int prima = QuanteCroci(vista);
                if (prima < 0)
                    return Fallito(
                        "Non riesco a contare le croci di centro della vista «" + (nome ?? "?") +
                        "» prima di cominciare: senza quel numero non saprei dirti quali ho " +
                        "messo io. Non ho toccato niente.");

                MathTransform tr = null;
                try { tr = vista.ModelToViewTransform; } catch { }
                var centriGia = CentriDelleCroci(vista);
                int cerchiQui = 0, messeQui = 0, giaQui = 0, nonFori = 0;
                int conto = prima;
                foreach (var o in (vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]) ?? new object[0])
                {
                    var e = o as IEdge;
                    ICurve c = null;
                    try { c = e?.GetCurve() as ICurve; } catch { }
                    if (c == null || !c.IsCircle()) continue;
                    bool intero = false;
                    try { intero = e.GetStartVertex() == null; } catch { }
                    if (intero ? !interi : !archi) continue;
                    // UN FORO VERO (revisione del 26/9/2026): il cerchio guarda chi legge
                    // (il suo asse, portato sul foglio, e' perpendicolare al foglio) ed e'
                    // un anello INTERNO di una faccia piana. Il bordo di un disco o la
                    // testa di un albero sono cerchi interi ma non fori: prima diventavano
                    // «fori senza croce» e l'operazione rifiutava un pezzo giusto.
                    if (!EUnForoCheSiVede(e, c, mu, tr)) { nonFori++; continue; }
                    cerchiQui++;

                    // Il centro sul foglio, per non mettere due croci sullo stesso foro.
                    double[] centro = null;
                    try
                    {
                        var cp = c.CircleParams as double[];
                        if (cp != null && mu != null && tr != null)
                            centro = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { cp[0], cp[1], cp[2] })).MultiplyTransform(tr)).ArrayData as double[];
                    }
                    catch { }
                    if (centro != null && centriGia.Exists(q => StessoCentro(vista, q, centro)))
                    { giaQui++; continue; }

                    bool presa = false;
                    try
                    {
                        modello.ClearSelection2(true);
                        var sd = selMgr?.CreateSelectData() as SelectData;
                        if (sd != null) { sd.View = (View)vista; presa = (e as IEntity).Select4(false, sd); }
                    }
                    catch { }
                    if (presa)
                        // OBSOLETE-MEASURED: le_croci_di_centro_sui_fori — IDrawingDoc.AddCenterMark is
                        // obsolete; its replacements InsertCenterMark2/3 answer True and add nothing
                        // (probes of 17/9 and 26/9/2026, see the summary of this file).
                        try { disegno.AddCenterMark(dimensione, linee); } catch { }
                    modello.ClearSelection2(true);
                    int adesso = QuanteCroci(vista);
                    if (adesso == conto + 1)
                    {
                        messeQui++;
                        conto = adesso;
                        if (centro != null) centriGia.Add(RelativoAllaVista(vista, centro));
                    }
                    else
                    {
                        nonNate.Add((nome ?? "?") + ": un cerchio" + (presa ? "" : " (non si e' lasciato selezionare)"));
                        if (adesso >= 0) conto = adesso;
                    }
                }

                int dopo = QuanteCroci(vista);
                totalePrima += prima;
                totaleDopo += dopo < 0 ? prima : dopo;
                gia += giaQui;
                perVista.Add(new Dictionary<string, object>
                {
                    ["vista"] = nome ?? "(senza nome)",
                    ["cerchi"] = cerchiQui,
                    ["cerchi_che_non_sono_fori"] = nonFori,
                    ["croci_prima"] = prima,
                    ["croci_dopo"] = NumeroOppureNull(dopo),
                    ["croci_aggiunte"] = messeQui,
                    ["cerchi_che_avevano_gia_la_croce"] = giaQui,
                });
                vista = vista.GetNextView() as IView;
            }

            try { modello.EditRebuild3(); } catch { }

            int aggiunte = Math.Max(0, totaleDopo - totalePrima);
            var misure = new Dictionary<string, object>
            {
                ["su"] = su,
                ["viste_guardate"] = perVista.Count,
                ["croci_prima"] = totalePrima,
                ["croci_dopo"] = totaleDopo,
                ["croci_aggiunte"] = aggiunte,
                ["cerchi_che_avevano_gia_la_croce"] = gia,
                ["per_vista"] = perVista.ToArray(),
                ["dimensione_mm"] = Math.Round(dimensione * 1000, 3),
                ["dimensione_da"] = suaDimensione ? "chiesta" : "documento",
            };
            if (nonNate.Count > 0)
            {
                misure["cerchi_senza_croce"] = nonNate.ToArray();
                return Fallito(nonNate.Count + (nonNate.Count == 1 ? " foro e'" : " fori sono") +
                               " senza croce di centro (" + string.Join("; ", nonNate) + ") e da codice la croce non " +
                               "nasce (misurato: AddCenterMark non la mette). La mette SOLIDWORKS creando la vista, se " +
                               "l'opzione del documento «Croci di centro sui fori» e' accesa: accendila nel modello di " +
                               "tavola e rifai la vista. Una tavola con qualche foro senza centro sembra finita e non lo e'.");
            }

            // ── ZERO CROCI NON E' UN SUCCESSO SILENZIOSO ────────────────
            if (aggiunte == 0)
            {
                misure["giudizio"] =
                    "NESSUNA CROCE AGGIUNTA. " +
                    (gia > 0
                        ? gia + (gia == 1 ? " cerchio aveva" : " cerchi avevano") + " gia' la croce: niente di nuovo da mettere."
                        : "Su " + perVista.Count + (perVista.Count == 1 ? " vista" : " viste") +
                          " non c'e' nessun " + (interi ? "foro" : "arco") + " visibile: e' la vista sbagliata " +
                          "(i fori si vedono come cerchi dall'alto, di solito V2), oppure il pezzo non ne ha.") +
                    " Il numero e' zero e te lo dico.";
                _log("[agente] " + misure["giudizio"]);
                return RiuscitoSenzaCreare(misure);
            }

            misure["giudizio"] =
                "Messe " + aggiunte + (aggiunte == 1 ? " croce di centro" : " croci di centro") +
                " su " + perVista.Count + (perVista.Count == 1 ? " vista" : " viste") +
                " (" + totalePrima + " -> " + totaleDopo + "), una per cerchio, contate sulla tavola.";
            _log("[agente] " + misure["giudizio"]);
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Il cerchio e' un foro che in questa vista si vede come cerchio: asse
        /// perpendicolare al foglio, e anello interno di una faccia piana.
        /// </summary>
        private static bool EUnForoCheSiVede(IEdge e, ICurve c, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var cp = c.CircleParams as double[];
                if (cp == null || cp.Length < 6 || mu == null || tr == null) return false;
                var asse = ((IMathVector)((IMathVector)mu.CreateVector(new[] { cp[3], cp[4], cp[5] })).MultiplyTransform(tr)).ArrayData as double[];
                double l = Math.Sqrt(asse[0] * asse[0] + asse[1] * asse[1] + asse[2] * asse[2]);
                if (l < 1e-12 || Math.Abs(asse[2]) / l < 0.99) return false;
                // IL CERCHIO E' UN ANELLO INTERNO DI UNA FACCIA PIANA: il bordo di un
                // foro sta dentro la faccia che attraversa; il bordo di un disco o la
                // testa di un albero e' l'anello ESTERNO della sua faccia.
                // Misurato il 26/9/2026 (\`prova0089 croci8\`): FaceInSurfaceSense
                // NON distingue — foro vero, disco falso — perche' il verso della
                // superficie dipende da come e' nata; l'anello no.
                foreach (var oc in (e.GetCoEdges() as object[]) ?? new object[0])
                {
                    var anello = (oc as ICoEdge)?.GetLoop() as ILoop2;
                    var f = anello?.GetFace() as IFace2;
                    var s = f?.GetSurface() as ISurface;
                    if (s == null || !s.IsPlane()) continue;
                    if (!anello.IsOuter()) return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// I centri delle croci che la vista ha gia', come li dice
        /// `ICenterMark.GetPosition(0)`: RISPETTO AL CENTRO DELLA VISTA, non sul
        /// foglio. Misurato il 26/9/2026 (`prova0089 croci4`): un cerchio a
        /// 54,5/110,69 sul foglio, in una vista centrata a 94,5/110,69, ha la
        /// croce a -40/0. Il primo codice li confrontava col foglio, e la seconda
        /// chiamata metteva tre croci doppie.
        /// </summary>
        private static List<double[]> CentriDelleCroci(IView vista)
        {
            var fuori = new List<double[]>();
            try
            {
                var cm = vista.GetFirstCenterMark() as ICenterMark;
                while (cm != null)
                {
                    try
                    {
                        var p = cm.GetPosition(0) as double[];
                        if (p != null && p.Length >= 2) fuori.Add(new[] { p[0], p[1] });
                    }
                    catch { }
                    cm = cm.GetNext() as ICenterMark;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>Un punto del foglio portato nel riferimento della vista (come GetPosition).</summary>
        private static double[] RelativoAllaVista(IView vista, double[] foglio)
        {
            double[] p = null;
            try { p = vista.Position as double[]; } catch { }
            if (p == null || p.Length < 2) return new[] { foglio[0], foglio[1] };
            return new[] { foglio[0] - p[0], foglio[1] - p[1] };
        }

        /// <summary>
        /// La croce `q` (riferimento della vista) sta sul centro `foglio`? Si prova
        /// con e senza la scala: a scala 1 non si e' potuto dire se GetPosition
        /// la applichi, e mezzo millimetro di tolleranza vale nelle due letture.
        /// </summary>
        private static bool StessoCentro(IView vista, double[] q, double[] foglio)
        {
            var r = RelativoAllaVista(vista, foglio);
            double scala = 1;
            try { scala = vista.ScaleDecimal; } catch { }
            if (scala <= 0) scala = 1;
            if (Math.Abs(q[0] - r[0]) < 0.0005 && Math.Abs(q[1] - r[1]) < 0.0005) return true;
            return Math.Abs(q[0] - r[0] / scala) < 0.0005 && Math.Abs(q[1] - r[1] / scala) < 0.0005;
        }

        /// <summary>
        /// Quante croci di centro ha questa vista, o **-1 se non si e' potuto
        /// leggere** (voce Z-C8).
        ///
        /// Tornava zero, e il commento diceva che era «il verso prudente». Non
        /// lo era: se falliva la lettura di PRIMA, qualunque croce dopo
        /// sembrava nuova e il messaggio diceva «messe N croci» anche su una
        /// vista che le aveva gia'; se falliva quella di DOPO, il ciclo provava
        /// tutti e tre gli stili (croci doppie) e poi diceva «quella vista non
        /// ha fori». Un numero che non si e' letto non si confronta.
        /// </summary>
        private static int QuanteCroci(IView vista)
        {
            return ContaCroci.DellaVista(vista);
        }

        private static bool StessoNome(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static bool NomeDiVistaConosciuto(List<string> nomi, string voluta)
        {
            foreach (var n in nomi) if (StessoNome(n, voluta)) return true;
            return false;
        }
    }

    /// <summary>
    /// LE CROCI DI CENTRO SI CONTANO ENUMERANDOLE (26/9/2026, `prova0089 croci5`).
    ///
    /// `IView.GetCenterMarkCount()` rispondeva **0** su una vista con tre croci:
    /// quelle che SOLIDWORKS mette da se' sui fori quando crea la vista (opzione
    /// del documento `swDetailingAutoInsertCenterMarksForHoles`, accesa qui) non
    /// le conta; conta solo quelle aggiunte dopo. `GetFirstCenterMark` /
    /// `GetNext` le vede tutte. Il «blocco» di W2.11 del 17/9 — «l'API dice si'
    /// e non mette niente» — era in gran parte questo contatore: le croci
    /// c'erano, e nessuno le vedeva. Lo usano l'operazione e lo stato.
    /// </summary>
    internal static class ContaCroci
    {
        /// <summary>Quante croci di centro ha la vista, enumerate; -1 se non si legge.</summary>
        internal static int DellaVista(IView vista)
        {
            try
            {
                int n = 0;
                var cm = vista.GetFirstCenterMark() as ICenterMark;
                while (cm != null && n < 10000)
                {
                    n++;
                    cm = cm.GetNext() as ICenterMark;
                }
                return n;
            }
            catch { return -1; }
        }
    }
}
