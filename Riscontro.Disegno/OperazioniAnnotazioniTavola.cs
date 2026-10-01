using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// RUGOSITA', DATUM E TOLLERANZE GEOMETRICHE IN TAVOLA (voci GT6 e GT8).
    ///
    /// ── MISURATE E GUARDATE PRIMA DI SCRIVERLE, 26/9/2026 ────────────────────
    ///
    /// `prova0089 annotazioni` e `annotazioni2`, tavola della piastra 120x80x10:
    ///
    ///  - `IModelDocExtension.InsertSurfaceFinishSymbol3` torna l'oggetto e il Ra
    ///    si rilegge (`GetText(swSFSymbolMaximumRoughness)` = «3.2»). MA LE
    ///    COORDINATE PASSATE SI IGNORANO: senza nient'altro il simbolo e' finito
    ///    nell'angolo del foglio, fuori cornice — visto nel PNG, nessun controllo
    ///    lo diceva.
    ///  - `IModelDoc2.InsertDatumTag2()` (zero argomenti) lavora sullo spigolo
    ///    SELEZIONATO nella vista, e non apre finestre. Lasciato dov'era, la
    ///    bandierina stava dalla parte opposta dello spigolo e la linea
    ///    attraversava il pezzo.
    ///  - `IModelDoc2.InsertGtol()` + `SetFrameSymbols` (codici di
    ///    `swGtolGeomCharSymbol_e`) + `SetFrameValues`: la cornice nasce e si
    ///    rilegge (`GetFrameSymbols` torna `Int16[]`, `GetFrameValues` le stringhe).
    ///    Senza selezione e' libera; con lo spigolo selezionato ha la freccia.
    ///
    /// La cura, vista nel PNG: si SELEZIONA lo spigolo di bordo del lato chiesto
    /// nella vista, si inserisce, e poi si SPOSTA l'annotazione fuori dal pezzo
    /// con `IAnnotation.SetPosition2`, dalla parte di quel lato. Il datum ha il
    /// triangolo sullo spigolo e la bandierina fuori; la rugosita' e la cornice
    /// hanno la linea di richiamo sullo spigolo.
    /// </summary>
    public partial class OperazioniSw
    {
        private sealed class LatoDellaVista
        {
            public IEdge Spigolo;
            public double[] Medio;      // sul foglio, metri
            public double[] Fuori;      // direzione verso l'esterno del pezzo
            public double[] Lungo;      // direzione lungo lo spigolo
        }

        /// <summary>
        /// Lo spigolo di bordo della vista sul lato chiesto (alto, basso, sinistra,
        /// destra): fra gli spigoli dritti orizzontali o verticali, quello piu' in
        /// fuori da quella parte. Null col motivo.
        /// </summary>
        private LatoDellaVista SpigoloDelLato(IView vista, string lato, out string motivo)
        {
            motivo = null;
            double fx = 0, fy = 0;
            switch (lato)
            {
                case "top": fy = 1; break;
                case "bottom": fy = -1; break;
                case "left": fx = -1; break;
                case "right": fx = 1; break;
                default:
                    motivo = "«side» e' uno fra alto, basso, sinistra, destra, non «" + lato + "».";
                    return null;
            }
            IMathUtility mu = null; MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = vista.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) { motivo = "Non riesco a leggere come la vista porta il modello sul foglio."; return null; }
            object[] spigoli = null;
            try { spigoli = vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            LatoDellaVista meglio = null; double punteggio = double.MinValue;
            foreach (var o in spigoli ?? new object[0])
            {
                var e = o as IEdge;
                try
                {
                    var p = ApiHelpers.EdgeParameters(e, out ICurve curva);
                    if (curva == null || !curva.IsLine()) continue;
                    var a = ((IMathPoint)((IMathPoint)mu.CreatePoint(p.StartPoint)).MultiplyTransform(tr)).ArrayData as double[];
                    var b = ((IMathPoint)((IMathPoint)mu.CreatePoint(p.EndPoint)).MultiplyTransform(tr)).ArrayData as double[];
                    bool orizzontale = Math.Abs(a[1] - b[1]) < 1e-6, verticale = Math.Abs(a[0] - b[0]) < 1e-6;
                    if (fy != 0 && !orizzontale || fx != 0 && !verticale) continue;
                    if (Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) < 1e-4) continue;   // troppo corto per attaccarci qualcosa
                    double s = (a[0] + b[0]) / 2 * fx + (a[1] + b[1]) / 2 * fy;
                    if (s > punteggio + 1e-9)
                    {
                        punteggio = s;
                        meglio = new LatoDellaVista
                        {
                            Spigolo = e,
                            Medio = new[] { (a[0] + b[0]) / 2, (a[1] + b[1]) / 2 },
                            Fuori = new[] { fx, fy },
                            Lungo = new[] { Math.Abs(fy), Math.Abs(fx) },
                        };
                    }
                }
                catch { }
            }
            if (meglio == null)
                motivo = "La vista non mostra nessuno spigolo dritto sul lato «" + lato + "» a cui attaccare l'annotazione.";
            return meglio;
        }

        /// <summary>
        /// Quanto e' gia' occupato, in mm dallo spigolo verso l'esterno, dalle
        /// annotazioni della vista che stanno da quella parte e all'altezza dello
        /// spigolo (le quote messe da `drawing_dimension`, i datum, le cornici). Zero
        /// se non c'e' niente.
        ///
        /// NATO GUARDANDO IL PNG (26/9/2026): la bandierina del datum 12 mm sotto
        /// lo spigolo cadeva SULLA linea della quota del 120, e la cornice 15 mm a
        /// destra sul testo della quota 10. Un'annotazione va oltre la fascia che
        /// c'e' gia'.
        /// </summary>
        private static double FasciaOccupata(IView vista, LatoDellaVista lato)
        {
            double massimo = 0;   // ZERO-E-ZERO: nessuna annotazione da quella parte
            try
            {
                double mezzo = 0;
                try
                {
                    var p = ApiHelpers.EdgeParameters(lato.Spigolo);
                    var s = p.StartPoint as double[]; var e = p.EndPoint as double[];
                    double scala = 1; try { scala = vista.ScaleDecimal; } catch { }
                    mezzo = Math.Sqrt(Math.Pow(e[0] - s[0], 2) + Math.Pow(e[1] - s[1], 2) + Math.Pow(e[2] - s[2], 2)) * (scala > 0 ? scala : 1) / 2;
                }
                catch { }
                var an = vista.GetFirstAnnotation3() as IAnnotation;
                while (an != null)
                {
                    var q = an.GetPosition() as double[];
                    if (q != null && q.Length >= 2)
                    {
                        double dx = q[0] - lato.Medio[0], dy = q[1] - lato.Medio[1];
                        double fuori = dx * lato.Fuori[0] + dy * lato.Fuori[1];
                        double lungo = Math.Abs(dx * lato.Lungo[0] + dy * lato.Lungo[1]);
                        if (fuori > 0 && lungo <= mezzo + 0.03) massimo = Math.Max(massimo, fuori);
                    }
                    an = an.GetNext3() as IAnnotation;
                }
            }
            catch { }
            return massimo * 1000.0;
        }

        /// <summary>La vista, risolta come le altre operazioni di tavola; null col motivo.</summary>
        private IView VistaPerAnnotare(IModelDoc2 modello, Dictionary<string, object> a, out IDrawingDoc disegno, out string motivo)
        {
            motivo = null;
            disegno = modello as IDrawingDoc;
            if (disegno == null)
            {
                motivo = "Il documento attivo non e' una tavola: le annotazioni stanno su una vista. Crea prima la " +
                         "tavola con «create_drawing».";
                return null;
            }
            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0)
            {
                motivo = "La tavola non ha viste: metti prima le viste con «standard_views».";
                return null;
            }
            var vista = RisolviVista(disegno, nomi, Str(a, "view", null), out motivo);
            if (motivo == null && vista == null) motivo = "Non riesco a prendere la vista.";
            return vista;
        }

        /// <summary>Seleziona lo spigolo nella vista (la selezione che le tre API usano).</summary>
        private static bool SelezionaNellaVista(IModelDoc2 modello, IView vista, IEdge spigolo)
        {
            try
            {
                modello.ClearSelection2(true);
                var sel = modello.SelectionManager as ISelectionMgr;
                var sd = sel?.CreateSelectData() as SelectData;
                if (sd == null) return false;
                sd.View = (View)vista;
                return (spigolo as IEntity).Select4(false, sd);
            }
            catch { return false; }
        }

        /// <summary>Quante annotazioni di quel tipo ha la tavola, vista per vista; -1 se non si legge.</summary>
        private static int AnnotazioniDellaTavola(IDrawingDoc disegno, string tipo)
        {
            try
            {
                int n = 0;
                var v = disegno.GetFirstView() as IView;
                while (v != null)
                {
                    n += tipo == "surface_finish" ? v.GetSFSymbolCount() : tipo == "datum" ? v.GetDatumTagCount() : v.GetGTolCount();
                    v = v.GetNextView() as IView;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Removes an annotation this operation has just placed, and says it is
        /// gone only when a recount agrees (Z-C21.8, finding M38). The deletion
        /// is DeleteSelection2, not EditDelete (the second can open a
        /// confirmation window, and a COM call waiting on a window does not
        /// come back). Its answer alone is not enough: true is claimed only
        /// when <paramref name="count"/>, read again, is back to
        /// <paramref name="countBefore"/>, the count taken before the
        /// annotation was inserted. With either count unreadable the answer is
        /// false, and the caller asks the user to remove it.
        /// </summary>
        private static bool RemoveNewAnnotation(IModelDoc2 model, IAnnotation annotation, Func<int> count, int countBefore)
        {
            if (annotation == null || countBefore < 0) return false;
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (annotation.Select3(false, null))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            if (!deleted) return false;
            int now = count();
            return now >= 0 && now == countBefore;
        }

        /// <summary>
        /// What the diameter flag read back from a tolerance frame says is
        /// wrong, or null (Z-C21.8, finding M38). <paramref name="read"/> is
        /// the first value of IGtol.GetFrameDiameterSymbols: 1 for a Ø in front
        /// of the first tolerance value, 0 for none, -1 when it could not be
        /// read. An unreadable flag is wrong only when the Ø was asked: that is
        /// the claim the answer would make (a cylindrical zone) without having
        /// seen it.
        /// </summary>
        internal static string GtolDiameterWrong(bool asked, int read)
        {
            if (read < 0)
                return asked ? "the Ø in front of the value cannot be read back (IGtol.GetFrameDiameterSymbols)" : null;
            if ((read == 1) != asked)
                return asked ? "the value has no Ø in front, and it was asked" : "the value has a Ø in front, and it was not asked";
            return null;
        }

        /// <summary>Le lettere dei datum gia' in tavola.</summary>
        private static List<string> LettereDeiDatum(IDrawingDoc disegno)
        {
            var fuori = new List<string>();
            try
            {
                var v = disegno.GetFirstView() as IView;
                while (v != null)
                {
                    var d = v.GetFirstDatumTag() as IDatumTag;
                    while (d != null)
                    {
                        string l = null;
                        try { l = d.GetLabel(); } catch { }
                        if (!string.IsNullOrWhiteSpace(l)) fuori.Add(l.Trim().ToUpperInvariant());
                        d = d.GetNext() as IDatumTag;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>
        /// DOVE METTERE UN'ANNOTAZIONE, fra alcuni posti in ordine: fuori dallo
        /// spigolo alla sua altezza; poi oltre gli angoli della vista (sopra e
        /// sotto per uno spigolo verticale, a destra e a sinistra per uno
        /// orizzontale). Si prende il primo il cui ingombro stimato non cade
        /// dentro un'ALTRA vista del foglio (con 5 mm di margine) e sta nel foglio.
        ///
        /// NATO GUARDANDO IL PNG (26/9/2026): la cornice messa oltre la quota 10,
        /// a destra della vista frontale, cadeva sopra il bordo della vista
        /// laterale — fra le due c'e' poco spazio. L'ingombro e' stimato
        /// (larghezza e altezza date da chi chiama), non letto: l'annotazione non
        /// dice quanto e' grande.
        /// </summary>
        private static double[] ScegliPosto(IDrawingDoc disegno, IView vista, LatoDellaVista lato,
                                            double fuoriMm, double lungoMm, double larghezzaMm, double altezzaMm)
        {
            var altre = new List<double[]>();
            double[] propria = null;
            try
            {
                var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    var o = v.GetOutline() as double[];
                    if (o != null && o.Length >= 4)
                    {
                        if (ReferenceEquals(v, vista) || v.GetName2() == vista.GetName2()) propria = o;
                        else altre.Add(o);
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            double f = fuoriMm / 1000.0, l = lungoMm / 1000.0, w = larghezzaMm / 1000.0, h = altezzaMm / 1000.0;
            var candidati = new List<double[]>
            {
                new[] { lato.Medio[0] + lato.Fuori[0] * f + lato.Lungo[0] * l, lato.Medio[1] + lato.Fuori[1] * f + lato.Lungo[1] * l },
            };
            if (propria != null)
            {
                double margine = 0.012 + h;
                if (lato.Fuori[0] != 0)   // spigolo verticale: sopra e sotto la vista, dalla parte del lato
                {
                    double x = lato.Medio[0] + lato.Fuori[0] * 0.008;
                    candidati.Add(new[] { x, propria[3] + margine });
                    candidati.Add(new[] { x, propria[1] - margine });
                }
                else                      // spigolo orizzontale: oltre gli angoli, dalla parte del lato
                {
                    double y = lato.Medio[1] + lato.Fuori[1] * f;
                    candidati.Add(new[] { propria[2] + 0.012, y });
                    candidati.Add(new[] { propria[0] - 0.012 - w, y });
                }
            }
            foreach (var c in candidati)
            {
                double x0 = c[0], x1 = c[0] + w, y0 = c[1] - h / 2, y1 = c[1] + h / 2;
                bool libero = true;
                foreach (var o in altre)
                    if (x1 > o[0] - 0.005 && x0 < o[2] + 0.005 && y1 > o[1] - 0.005 && y0 < o[3] + 0.005) { libero = false; break; }
                if (libero) return c;
            }
            return candidati[0];
        }

        /// <summary>
        /// Sposta un'annotazione nel posto scelto da `ScegliPosto`. Torna la
        /// posizione riletta in mm, o null.
        /// </summary>
        private static double[] Sposta(IAnnotation an, IDrawingDoc disegno, IView vista, LatoDellaVista lato,
                                       double fuoriMm, double lungoMm, double larghezzaMm, double altezzaMm)
        {
            if (an == null) return null;
            try
            {
                var posto = ScegliPosto(disegno, vista, lato, fuoriMm, lungoMm, larghezzaMm, altezzaMm);
                double x = posto[0], y = posto[1];
                an.SetPosition2(x, y, 0);
                var p = an.GetPosition() as double[];
                return p == null ? null : new[] { Math.Round(p[0] * 1000, 2), Math.Round(p[1] * 1000, 2) };
            }
            catch { return null; }
        }

        // ──────────────────────────────────────────────────────────────────
        // surface_finish
        // ──────────────────────────────────────────────────────────────────
        private Esito RugositaInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string motivo;
            IDrawingDoc disegno;
            var vista = VistaPerAnnotare(modello, a, out disegno, out motivo);
            if (vista == null) return Fallito(motivo);
            double ra;
            if (!NumObbligatorio(a, "ra_um", out ra, out motivo)) return Fallito(motivo);
            if (ra <= 0 || ra > 100)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "«ra_um» e' la rugosita' Ra in micrometri, fra 0 e 100: {0:0.###} non lo e'.", ra));
            string lavorazione = Str(a, "process", "material_removal");
            int tipo = lavorazione == "material_removal" ? (int)swSFSymType_e.swSFMachining_Req
                     : lavorazione == "any" ? (int)swSFSymType_e.swSFBasic
                     : lavorazione == "no_removal" ? (int)swSFSymType_e.swSFDont_Machine : -1;
            if (tipo < 0)
                return Fallito("«process» e' asportazione, qualsiasi o no_removal, non «" + lavorazione + "».");
            var lato = SpigoloDelLato(vista, Str(a, "side", "top"), out motivo);
            if (lato == null) return Fallito(motivo);

            string testoRa = ra.ToString("0.###", CultureInfo.InvariantCulture);
            double occupata = FasciaOccupata(vista, lato);
            int prima = AnnotazioniDellaTavola(disegno, "surface_finish");
            if (!SelezionaNellaVista(modello, vista, lato.Spigolo))
                return Fallito("Non riesco a selezionare nella vista lo spigolo del lato «" + Str(a, "side", "top") + "».");
            object grezzo;
            try
            {
                grezzo = modello.Extension.InsertSurfaceFinishSymbol3(tipo, (int)swLeaderStyle_e.swSTRAIGHT,
                    lato.Medio[0], lato.Medio[1], 0, (int)swSFLaySym_e.swSFNone, (int)swArrowStyle_e.swCLOSED_ARROWHEAD,
                    "", "", "", "", testoRa, "", "");
            }
            catch (Exception ex) { return Fallito("Il simbolo di rugosita' non e' nato: " + ex.Message); }
            modello.ClearSelection2(true);
            var simbolo = grezzo as ISFSymbol;
            if (simbolo == null) return Fallito("SOLIDWORKS non ha creato il simbolo di rugosita' e non ha detto perche'.");
            int dopo = AnnotazioniDellaTavola(disegno, "surface_finish");
            string letto = null;
            try { letto = simbolo.GetText((int)swSurfaceFinishSymbolText_e.swSFSymbolMaximumRoughness); } catch { }
            var an = simbolo.GetAnnotation() as IAnnotation;
            // FUORI DAL PEZZO: il simbolo, lasciato dove SOLIDWORKS lo mette, non sta
            // dove si chiede (misurato). 12 mm verso l'esterno, 20 lungo lo spigolo.
            var pos = Sposta(an, disegno, vista, lato, Math.Max(12, occupata + 10), 20, 16, 12);
            int attaccate = -1;
            try { attaccate = an.GetAttachedEntityCount3(); } catch { }

            if (!string.Equals((letto ?? "").Trim(), testoRa, StringComparison.Ordinal))
                return Fallito("Il simbolo e' nato ma dice Ra «" + letto + "» invece di «" + testoRa + "»: toglilo — " +
                               Frasi.CosiSiRitira + ".");
            var misure = new Dictionary<string, object>
            {
                ["ra_um"] = testoRa,
                ["ra_reread"] = letto,
                ["feature"] = lavorazione,
                ["side"] = Str(a, "side", "top"),
                ["symbols_before"] = NumeroOppureNull(prima),
                ["symbols_after"] = NumeroOppureNull(dopo),
                ["position_mm"] = pos,
                ["attached_to_edge"] = attaccate > 0,
            };
            if (prima >= 0 && dopo >= 0 && dopo != prima + 1)
                misure["warning"] = "I simboli di rugosita' della tavola sono passati da " + prima + " a " + dopo +
                                       ": ne aspettavo uno in piu'.";
            return SenzaFeature(misure);
        }

        // ──────────────────────────────────────────────────────────────────
        // datum
        // ──────────────────────────────────────────────────────────────────
        private Esito DatumInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string motivo;
            IDrawingDoc disegno;
            var vista = VistaPerAnnotare(modello, a, out disegno, out motivo);
            if (vista == null) return Fallito(motivo);
            string lettera = (Str(a, "letter", "A") ?? "").Trim().ToUpperInvariant();
            if (lettera.Length != 1 || lettera[0] < 'A' || lettera[0] > 'Z')
                return Fallito("«letter» e' una lettera sola, dalla A alla Z, non «" + Str(a, "letter", "") + "».");
            // DUE DATUM CON LA STESSA LETTERA non danno errore, e ogni tolleranza che
            // cita quella lettera diventa ambigua (trappola scritta in Z-E6).
            var gia = LettereDeiDatum(disegno);
            if (gia.Contains(lettera))
                return Fallito("In tavola c'e' gia' un datum «" + lettera + "»: due riferimenti con la stessa lettera " +
                               "rendono ambigua ogni tolleranza che lo cita. Usa un'altra lettera (ci sono gia': " +
                               string.Join(", ", gia) + ").");
            var lato = SpigoloDelLato(vista, Str(a, "side", "bottom"), out motivo);
            if (lato == null) return Fallito(motivo);

            double occupata = FasciaOccupata(vista, lato);
            int prima = AnnotazioniDellaTavola(disegno, "datum");
            if (!SelezionaNellaVista(modello, vista, lato.Spigolo))
                return Fallito("Non riesco a selezionare nella vista lo spigolo del lato «" + Str(a, "side", "bottom") + "».");
            object grezzo;
            try { grezzo = modello.InsertDatumTag2(); }
            catch (Exception ex) { return Fallito("Il datum non e' nato: " + ex.Message); }
            modello.ClearSelection2(true);
            var datum = grezzo as IDatumTag;
            if (datum == null) return Fallito("SOLIDWORKS non ha creato il datum e non ha detto perche'.");
            bool etichettato = false;
            try { etichettato = datum.SetLabel(lettera); } catch { }
            string letta = null;
            try { letta = datum.GetLabel(); } catch { }
            var an = datum.GetAnnotation() as IAnnotation;
            // La bandierina FUORI, dalla parte dello spigolo: lasciata dove nasce stava
            // dall'altra parte e la linea attraversava il pezzo (visto nel PNG).
            var pos = Sposta(an, disegno, vista, lato, Math.Max(12, occupata + 10), -30, 8, 8);
            int attaccate = -1;
            try { attaccate = an.GetAttachedEntityCount3(); } catch { }
            int dopo = AnnotazioniDellaTavola(disegno, "datum");
            if (!string.Equals((letta ?? "").Trim(), lettera, StringComparison.OrdinalIgnoreCase))
                return Fallito("Il datum e' nato ma dice «" + letta + "» invece di «" + lettera + "»" +
                               (etichettato ? "" : " (SetLabel ha risposto no)") + ": toglilo — " + Frasi.CosiSiRitira + ".");
            var misure = new Dictionary<string, object>
            {
                ["letter"] = lettera,
                ["letter_reread"] = letta,
                ["side"] = Str(a, "side", "bottom"),
                ["datum_before"] = NumeroOppureNull(prima),
                ["datum_after"] = NumeroOppureNull(dopo),
                ["position_mm"] = pos,
                ["attached_to_edge"] = attaccate > 0,
            };
            return SenzaFeature(misure);
        }

        // ──────────────────────────────────────────────────────────────────
        // geometric_tolerance
        // ──────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, int> Caratteristiche = new Dictionary<string, int>
        {
            ["straightness"] = (int)swGtolGeomCharSymbol_e.swGcsSTRAIGHT,
            ["flatness"] = (int)swGtolGeomCharSymbol_e.swGcsFLAT,
            ["circularity"] = (int)swGtolGeomCharSymbol_e.swGcsROUND,
            ["cylindricity"] = (int)swGtolGeomCharSymbol_e.swGcsCYL,
            ["profile_of_line"] = (int)swGtolGeomCharSymbol_e.swGcsLINEPROF,
            ["profile_of_surface"] = (int)swGtolGeomCharSymbol_e.swGcsSURFPROF,
            ["angularity"] = (int)swGtolGeomCharSymbol_e.swGcsANG,
            ["perpendicularity"] = (int)swGtolGeomCharSymbol_e.swGcsPERP,
            ["parallelism"] = (int)swGtolGeomCharSymbol_e.swGcsPARALLEL,
            ["position"] = (int)swGtolGeomCharSymbol_e.swGcsPOSITION,
            ["concentricity"] = (int)swGtolGeomCharSymbol_e.swGcsCONC,
            ["symmetry"] = (int)swGtolGeomCharSymbol_e.swGcsSYMMETRY,
            ["circular_runout"] = (int)swGtolGeomCharSymbol_e.swGcsCIRCRUNOUT,
            ["total_runout"] = (int)swGtolGeomCharSymbol_e.swGcsTOTALRUNOUT,
        };

        /// <summary>Le tolleranze di FORMA non hanno riferimenti (ISO 1101); le altre ne vogliono.</summary>
        private static bool EDiForma(string c)
        {
            return c == "straightness" || c == "flatness" || c == "circularity" || c == "cylindricity";
        }

        private Esito TolleranzaGeometricaInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string motivo;
            IDrawingDoc disegno;
            var vista = VistaPerAnnotare(modello, a, out disegno, out motivo);
            if (vista == null) return Fallito(motivo);
            string caratteristica = Str(a, "characteristic", "");
            int codice;
            if (!Caratteristiche.TryGetValue(caratteristica, out codice))
                return Fallito("«characteristic» e' una fra " + string.Join(", ", Caratteristiche.Keys) +
                               ", non «" + caratteristica + "».");
            double valore;
            if (!NumObbligatorio(a, "value_mm", out valore, out motivo)) return Fallito(motivo);
            if (valore <= 0) return Fallito("«value_mm» e' l'ampiezza della zona di tolleranza: deve essere maggiore di zero.");
            string testoValore = valore.ToString("0.###", CultureInfo.InvariantCulture);

            var riferimenti = new List<string>();
            foreach (var r in Lista(a, "references")) if (!string.IsNullOrWhiteSpace(r)) riferimenti.Add(r.Trim().ToUpperInvariant());
            if (riferimenti.Count > 3)
                return Fallito("Una cornice cita al massimo tre riferimenti (primario, secondario, terziario).");
            if (EDiForma(caratteristica) && riferimenti.Count > 0)
                return Fallito("La tolleranza di " + caratteristica + " e' di FORMA: non si riferisce a nessun datum " +
                               "(ISO 1101). Togli «references», oppure scegli una tolleranza di orientamento o di " +
                               "posizione.");
            if (!EDiForma(caratteristica) && riferimenti.Count == 0)
                return Fallito("La tolleranza di " + caratteristica + " si misura rispetto a un riferimento: dai almeno una lettera " +
                               "in «references» (un datum messo con «datum»).");
            var gia = LettereDeiDatum(disegno);
            foreach (var r in riferimenti)
                if (!gia.Contains(r))
                    return Fallito("La tolleranza cita il datum «" + r + "» e in tavola non c'e'" +
                                   (gia.Count == 0 ? " nessun datum" : " (ci sono: " + string.Join(", ", gia) + ")") +
                                   ". Mettilo prima con «datum».");
            bool diametro = a != null && a.ContainsKey("diameter") && Convert.ToBoolean(a["diameter"], CultureInfo.InvariantCulture);
            var lato = SpigoloDelLato(vista, Str(a, "side", "right"), out motivo);
            if (lato == null) return Fallito(motivo);

            double occupata = FasciaOccupata(vista, lato);
            int prima = AnnotazioniDellaTavola(disegno, "gtol");
            if (!SelezionaNellaVista(modello, vista, lato.Spigolo))
                return Fallito("Non riesco a selezionare nella vista lo spigolo del lato «" + Str(a, "side", "right") + "».");
            object grezzo;
            try { grezzo = modello.InsertGtol(); }
            catch (Exception ex) { return Fallito("La cornice di tolleranza non e' nata: " + ex.Message); }
            modello.ClearSelection2(true);
            var gtol = grezzo as IGtol;
            if (gtol == null) return Fallito("SOLIDWORKS non ha creato la cornice di tolleranza e non ha detto perche'.");
            string r1 = riferimenti.Count > 0 ? riferimenti[0] : "", r2 = riferimenti.Count > 1 ? riferimenti[1] : "",
                   r3 = riferimenti.Count > 2 ? riferimenti[2] : "";
            try
            {
                gtol.SetFrameSymbols(1, (short)codice, diametro, (short)swGtolMatCondition_e.swMcNONE, false,
                    (short)swGtolMatCondition_e.swMcNONE, (short)swGtolMatCondition_e.swMcNONE,
                    (short)swGtolMatCondition_e.swMcNONE, (short)swGtolMatCondition_e.swMcNONE);
                gtol.SetFrameValues(1, testoValore, "", r1, r2, r3);
            }
            catch (Exception ex)
            {
                // A FRAME THAT CANNOT BE FILLED IS NOT LEFT ON THE SHEET (M38),
                // as a wrong one below.
                IAnnotation emptyFrame = null;
                try { emptyFrame = gtol.GetAnnotation() as IAnnotation; } catch { }
                bool removedEmpty = RemoveNewAnnotation(modello, emptyFrame,
                                                        () => AnnotazioniDellaTavola(disegno, "gtol"), prima);
                return Fallito("The tolerance frame was born but does not let itself be filled (" + ex.Message + ")" +
                               (removedEmpty ? ": I removed it." : ": remove it — " + Frasi.CosiSiRitira + "."));
            }
            // LA RILETTURA: il simbolo come codice, i valori come stringhe.
            int simboloLetto = -1;
            var valoriLetti = new List<string>();
            try
            {
                var fs = gtol.GetFrameSymbols(1) as System.Collections.IEnumerable;
                if (fs != null) foreach (var x in fs) { simboloLetto = Convert.ToInt32(x, CultureInfo.InvariantCulture); break; }
                var fv = gtol.GetFrameValues(1) as System.Collections.IEnumerable;
                if (fv != null) foreach (var x in fv) valoriLetti.Add(Convert.ToString(x, CultureInfo.InvariantCulture) ?? "");
            }
            catch { }
            // THE Ø IS READ BACK TOO (Z-C21.8, finding M38). IGtol~GetFrameSymbols2.html:
            // «Use this method with IGtol::GetFrameDiameterSymbols, which determines
            // whether diameter symbols are displayed»; GetFrameDiameterSymbols.html:
            // «an array of two Booleans ... retval [0] = True if the first tolerance
            // value has a diameter symbol». Until library 0.144.0 the answer
            // echoed the argument there, not the frame.
            int frameDiameterFlag = -1;
            try
            {
                var fd = gtol.GetFrameDiameterSymbols(1) as System.Collections.IEnumerable;
                if (fd != null) foreach (var x in fd) { frameDiameterFlag = Convert.ToBoolean(x, CultureInfo.InvariantCulture) ? 1 : 0; break; }
            }
            catch { }
            var an = gtol.GetAnnotation() as IAnnotation;
            var pos = Sposta(an, disegno, vista, lato, Math.Max(15, occupata + 12), 0, 10 * (2 + riferimenti.Count), 8);
            int attaccate = -1;
            try { attaccate = an.GetAttachedEntityCount3(); } catch { }
            int dopo = AnnotazioniDellaTavola(disegno, "gtol");

            string valoreLetto = valoriLetti.Count > 0 ? valoriLetti[0] : "";
            var riferimentiLetti = new List<string>();
            for (int i = 2; i < valoriLetti.Count && i < 5; i++) if (!string.IsNullOrWhiteSpace(valoriLetti[i])) riferimentiLetti.Add(valoriLetti[i].Trim());
            string diameterWrong = GtolDiameterWrong(diametro, frameDiameterFlag);
            if (simboloLetto != codice || valoreLetto.Trim() != testoValore ||
                string.Join(",", riferimentiLetti) != string.Join(",", riferimenti) || diameterWrong != null)
            {
                // A WRONG FRAME IS NOT LEFT ON THE SHEET (Z-C21.8, finding M38): it
                // would be printed and read in the shop. Removed as drawing_weld_symbol
                // removes a wrong weld symbol, and said removed only after a recount.
                bool removed = RemoveNewAnnotation(modello, an, () => AnnotazioniDellaTavola(disegno, "gtol"), prima);
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "The tolerance frame was born but does not say what I asked: symbol {0} (asked {1}), value «{2}» " +
                    "(asked «{3}»), datums «{4}» (asked «{5}»){6}. {7}",
                    simboloLetto, codice, valoreLetto, testoValore, string.Join(",", riferimentiLetti),
                    string.Join(",", riferimenti), diameterWrong == null ? "" : "; " + diameterWrong,
                    removed ? "I removed it." : "Remove it — " + Frasi.CosiSiRitira + "."));
            }
            var misure = new Dictionary<string, object>
            {
                ["characteristic"] = caratteristica,
                ["symbol_reread"] = simboloLetto,
                ["value_mm"] = testoValore,
                ["value_reread"] = valoreLetto,
                ["references"] = riferimenti.ToArray(),
                ["references_reread"] = riferimentiLetti.ToArray(),
                ["diameter"] = diametro,
                // The Ø as read back from the frame (M38): null when it could not be
                // read and was not asked, which GtolDiameterWrong lets through.
                ["diameter_read"] = frameDiameterFlag < 0 ? null : (object)(frameDiameterFlag == 1),
                ["side"] = Str(a, "side", "right"),
                ["frames_before"] = NumeroOppureNull(prima),
                ["frames_after"] = NumeroOppureNull(dopo),
                ["position_mm"] = pos,
                ["attached_to_edge"] = attaccate > 0,
            };
            return SenzaFeature(misure);
        }
    }
}
