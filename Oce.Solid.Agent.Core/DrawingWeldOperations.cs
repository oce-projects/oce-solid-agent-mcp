using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// IL SIMBOLO DI SALDATURA IN TAVOLA (voce GT7).
    ///
    /// ── MISURATO PRIMA DI SCRIVERLO, 26/9/2026 (`prova0089 saldtav`) ────────
    ///
    /// `IDrawingDoc.InsertWeldSymbol(Dim1, Symbol, Dim2, Symmetric, FieldWeld,
    /// ShowOtherSide, DashOnTop, Peripheral, HasProcess, ProcessValue)` torna
    /// `Void` e lavora sullo spigolo SELEZIONATO nella vista: il simbolo nasce
    /// con la freccia sullo spigolo e la linea tratteggiata ISO sotto.
    ///
    /// **Il codice del simbolo e' quello della libreria `gtol.sym`**,
    /// `<SEZIONE-VOCE>`: il cordone d'angolo ISO e' `<WELD-FILL>` (sezione
    /// `#WELD`, voce `*FILL`). La prima sonda aveva provato `<IGWS-FILLET>`,
    /// `<GWS-FILLET>` e «Fillet»: il simbolo NASCEVA lo stesso, con quel testo
    /// riletto uguale — e senza triangolo. Solo il PNG lo diceva. Con
    /// `<WELD-FILL>` il triangolo c'e', e `GetTextCount` passa da 1 a 2: il
    /// codice riconosciuto e' un testo in piu' (il simbolo disegnato).
    ///
    /// Lo spigolo non lo sceglie chi chiama: e' quello, fra gli spigoli visibili
    /// della vista, in cui un corpo tocca l'altro — uno spigolo di un corpo i cui
    /// estremi e punto medio stanno su una faccia di un ALTRO corpo. Il piu'
    /// lungo. Una vista senza giunti visibili si rifiuta: un simbolo attaccato a
    /// uno spigolo qualunque manderebbe il saldatore nel posto sbagliato.
    /// </summary>
    public partial class OperazioniSw
    {
        private static readonly Dictionary<string, string> SimboliDiSaldatura = new Dictionary<string, string>
        {
            ["fillet"] = "<WELD-FILL>",
            ["v"] = "<WELD-BUSV>",
            ["half_v"] = "<WELD-BUSB>",
            ["butt"] = "<WELD-BUSQ>",
        };

        private Esito SaldaturaInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string motivo;
            IDrawingDoc disegno;
            var vista = VistaPerAnnotare(modello, a, out disegno, out motivo);
            if (vista == null) return Fallito(motivo);

            string tipo = Str(a, "variant", "fillet");
            string simbolo;
            if (!SimboliDiSaldatura.TryGetValue(tipo, out simbolo))
                return Fallito("«variant» e' uno fra " + string.Join(", ", SimboliDiSaldatura.Keys) + ", non «" + tipo + "».");
            double gola;
            if (!NumObbligatorio(a, "throat_mm", out gola, out motivo)) return Fallito(motivo);
            if (gola <= 0 || gola > 50)
                return Fallito("«throat_mm» (la gola del cordone) sta fra 0 e 50 mm, non " +
                               gola.ToString("0.###", CultureInfo.InvariantCulture) + ".");
            double lunghezza = Num(a, "length_mm", 0);
            if (lunghezza < 0) return Fallito("«length_mm» non puo' essere negativa.");
            bool tuttoAttorno = Bool(a, "all_around", false);
            bool inOpera = Bool(a, "field_weld", false);
            bool dueLati = Bool(a, "both_sides", false);
            string latoAnnotazione = Str(a, "side", "top");

            // ── IL GIUNTO VISIBILE ──────────────────────────────────────────
            var pezzo = vista.ReferencedDocument as IModelDoc2;
            var parte = pezzo as IPartDoc;
            if (parte == null)
                return Fallito("Dietro la vista non c'e' una parte: il simbolo di saldatura va su un giunto fra i " +
                               "corpi di un saldato.");
            object[] corpi = null;
            try { corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[]; } catch { }
            if (corpi == null)
                return Fallito("Non riesco a leggere i corpi del pezzo dietro la vista: senza, non so dire dove sia il " +
                               "giunto. Guarda il pezzo e riprova.");
            if (corpi.Length < 2)
                return Fallito("Il pezzo dietro la vista ha " + corpi.Length + " corpi: un " +
                               "giunto saldato e' fra DUE corpi, e un simbolo su un corpo solo non indica nessuna saldatura.");
            IEdge giunto; double lungoGiunto;
            if (!GiuntoVisibile(vista, corpi, out giunto, out lungoGiunto))
                return Fallito("Nella vista «" + (vista.GetName2() ?? "") + "» non si vede nessuno spigolo in cui un corpo " +
                               "tocca l'altro: il simbolo va sulla linea del giunto. Prova la vista in cui il giunto si vede " +
                               "di fianco.");

            var lato = SpigoloDelLato(vista, latoAnnotazione, out motivo);
            if (lato == null) return Fallito(motivo);
            double occupata = FasciaOccupata(vista, lato);

            int prima = SimboliDiSaldaturaInTavola(disegno);
            if (!SelezionaNellaVista(modello, vista, giunto))
                return Fallito("Non riesco a selezionare nella vista lo spigolo del giunto.");
            string dim1 = "a" + gola.ToString("0.##", CultureInfo.InvariantCulture);
            string dim2 = lunghezza > 0 ? lunghezza.ToString("0.##", CultureInfo.InvariantCulture) : "";
            try { disegno.InsertWeldSymbol(dim1, simbolo, dim2, dueLati, inOpera, false, false, tuttoAttorno, false, ""); }
            catch (Exception ex) { modello.ClearSelection2(true); return Fallito("Il simbolo di saldatura non e' nato: " + ex.Message); }
            modello.ClearSelection2(true);
            int dopo = SimboliDiSaldaturaInTavola(disegno);
            IWeldSymbol ws = UltimoSimboloDiSaldatura(disegno, vista);
            // IL SIMBOLO NUOVO SI CONTA: senza i due conteggi letti, l'ultimo
            // simbolo della vista potrebbe essere uno che c'era gia' (e il ramo
            // «sbagliato» lo toglierebbe). Revisione del 26/9/2026.
            if (prima < 0 || dopo < 0)
                return Fallito("Non riesco a contare i simboli di saldatura della tavola (prima " + prima + ", dopo " +
                               dopo + "): senza, non so se e' nato quello chiesto. Guarda la tavola.");
            if (ws == null || dopo != prima + 1)
                return Fallito("SOLIDWORKS non ha creato il simbolo di saldatura e non ha detto perche' (simboli in " +
                               "tavola: " + prima + " prima, " + dopo + " dopo).");

            // ── RILETTO ─────────────────────────────────────────────────────
            string simboloLetto = null, sinistraLetta = null, destraLetta = null;
            int testi = -1, frecce = -1, attaccate = -1;
            bool perimetroLetto = false; int campoLetto = -1, simmetricoLetto = -1;
            try { simboloLetto = ws.GetText((int)swWeldSymbolTextTypes_e.swWeldSymbolTextAbove); } catch { }
            try { sinistraLetta = ws.GetText((int)swWeldSymbolTextTypes_e.swWeldLeftTextAbove); } catch { }
            try { destraLetta = ws.GetText((int)swWeldSymbolTextTypes_e.swWeldRightTextAbove); } catch { }
            try { testi = ws.GetTextCount(); } catch { }
            try { frecce = ws.GetLeaderCount(); } catch { }
            try { perimetroLetto = ws.GetPeripheral(); } catch { }
            try { campoLetto = ws.GetFieldWeld(); } catch { }
            try { simmetricoLetto = ws.GetSymmetric(); } catch { }
            var an = ws.GetAnnotation() as IAnnotation;
            try { attaccate = an.GetAttachedEntityCount3(); } catch { }
            var pos = Sposta(an, disegno, vista, lato, Math.Max(15, occupata + 12), 20, 25, 10);

            var sbagliato = new List<string>();
            if (!string.Equals((simboloLetto ?? "").Trim(), simbolo, StringComparison.OrdinalIgnoreCase))
                sbagliato.Add("il simbolo dice «" + simboloLetto + "» invece di «" + simbolo + "»");
            if (!string.Equals((sinistraLetta ?? "").Trim(), dim1, StringComparison.OrdinalIgnoreCase))
                sbagliato.Add("la gola dice «" + sinistraLetta + "» invece di «" + dim1 + "»");
            if (dim2.Length > 0 && !string.Equals((destraLetta ?? "").Trim(), dim2, StringComparison.OrdinalIgnoreCase))
                sbagliato.Add("la lunghezza dice «" + destraLetta + "» invece di «" + dim2 + "»");
            if (tuttoAttorno != perimetroLetto) sbagliato.Add("«tutto attorno» non e' come chiesto");
            bool campoOk = campoLetto == (int)swWeldSymbolField_e.swFieldWeldUp || campoLetto == (int)swWeldSymbolField_e.swFieldWeldDown;
            if (campoLetto < 0) sbagliato.Add("«in opera» non si rilegge");
            else if (inOpera != campoOk) sbagliato.Add("«in opera» non e' come chiesto");
            if (simmetricoLetto < 0 && dueLati) sbagliato.Add("«entrambi i lati» non si rilegge");
            else if (dueLati != (simmetricoLetto == (int)swWeldSymbolSymmetric_e.swWeldSymmetric)) sbagliato.Add("«entrambi i lati» non e' come chiesto");
            if (attaccate == 0) sbagliato.Add("non e' attaccato a nessuno spigolo");
            if (attaccate < 0) sbagliato.Add("non si rilegge a cosa e' attaccato");
            string textsWrong = WeldSymbolTextsWrong(testi);
            if (textsWrong != null) sbagliato.Add(textsWrong);
            if (sbagliato.Count > 0)
            {
                // UN SIMBOLO SBAGLIATO NON SI LASCIA IN TAVOLA: si stampa e manda il
                // saldatore a fare un'altra saldatura. Removed through
                // RemoveNewAnnotation, next to the tolerance frame: «removed» is
                // said only when the weld symbols of the drawing, counted again,
                // are back to the count before the insertion (Z-C21.8, M38).
                bool tolto = RemoveNewAnnotation(modello, an, () => SimboliDiSaldaturaInTavola(disegno), prima);
                return Fallito("Il simbolo di saldatura e' nato ma " + string.Join("; ", sbagliato) +
                               (tolto ? ": l'ho tolto." : ": toglilo — " + Frasi.CosiSiRitira + "."));
            }

            return SenzaFeature(new Dictionary<string, object>
            {
                ["kind"] = tipo,
                ["symbol"] = simbolo,
                ["symbol_reread"] = simboloLetto,
                ["throat_reread"] = sinistraLetta,
                ["length_reread"] = string.IsNullOrEmpty(destraLetta) ? null : destraLetta,
                ["all_around"] = perimetroLetto,
                ["field_weld"] = campoLetto == (int)swWeldSymbolField_e.swFieldWeldUp || campoLetto == (int)swWeldSymbolField_e.swFieldWeldDown,
                ["both_sides"] = simmetricoLetto == (int)swWeldSymbolSymmetric_e.swWeldSymmetric,
                ["texts"] = NumeroOppureNull(testi),
                ["arrows"] = NumeroOppureNull(frecce),
                ["attached_to_edge"] = attaccate > 0,
                ["joint_mm"] = Math.Round(lungoGiunto, 2),
                ["symbols_before"] = NumeroOppureNull(prima),
                ["symbols_after"] = NumeroOppureNull(dopo),
                ["position_mm"] = pos,
            });
        }

        /// <summary>
        /// What the text count of a new weld symbol says is wrong with it, or
        /// null when the symbol was drawn (Z-C21.8, finding L16-1).
        ///
        /// The discriminant is the one measured when the operation was written
        /// (26/9/2026, see the header): with a code the gtol.sym library does
        /// not know, the symbol is born all the same, its code reads back equal,
        /// and nothing is drawn — IWeldSymbol.GetTextCount gives 1, the throat
        /// alone. With a known code the drawn symbol is one text more, 2. The
        /// bench case of this operation asserts that count, but until library
        /// 0.144.0 the operation itself never looked at it, so a
        /// symbol with no triangle was answered as done. A count that cannot be
        /// read is wrong too, as an attached count that cannot be read is: the
        /// one check that sees the missing symbol did not run.
        /// </summary>
        internal static string WeldSymbolTextsWrong(int texts)
        {
            if (texts < 0)
                return "its texts cannot be read back (IWeldSymbol.GetTextCount), so I cannot tell whether the " +
                       "symbol was drawn";
            if (texts < 2)
                return "it has " + texts + (texts == 1 ? " text" : " texts") + " where a drawn symbol makes 2: " +
                       "SOLIDWORKS kept the code as text and drew no symbol";
            return null;
        }

        /// <summary>
        /// Lo spigolo visibile piu' lungo in cui un corpo tocca l'altro: inizio,
        /// meta' e fine su una faccia di un ALTRO corpo, entro un millesimo di mm.
        /// </summary>
        private static bool GiuntoVisibile(IView vista, object[] corpi, out IEdge giunto, out double lunghezzaMm)
        {
            giunto = null; lunghezzaMm = 0;
            object[] spigoli = null;
            try { spigoli = vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            foreach (var o in spigoli ?? new object[0])
            {
                try
                {
                    var e = o as IEdge;
                    var suo = e?.GetBody() as IBody2;
                    var p = ApiHelpers.EdgeParameters(e, out ICurve curva);
                    if (suo == null || curva == null || p == null) continue;
                    double u0 = p.UMinValue, u1 = p.UMaxValue;
                    var punti = new List<double[]>();
                    foreach (double t in new[] { u0, (u0 + u1) / 2, u1 })
                    {
                        var v = curva.Evaluate2(t, 0) as double[];
                        if (v != null) punti.Add(new[] { v[0], v[1], v[2] });
                    }
                    if (punti.Count < 3) continue;
                    double l = curva.GetLength3(u0, u1) * 1000.0;
                    if (l <= lunghezzaMm) continue;
                    foreach (var oc in corpi)
                    {
                        var altro = oc as IBody2;
                        if (altro == null || altro.Name == suo.Name) continue;
                        bool sta = false;
                        foreach (var of in (altro.GetFaces() as object[]) ?? new object[0])
                        {
                            var f = of as IFace2;
                            if (f == null) continue;
                            bool tutti = true;
                            foreach (var q in punti)
                            {
                                var c = f.GetClosestPointOn(q[0], q[1], q[2]) as double[];
                                if (c == null || Math.Sqrt(Math.Pow(c[0] - q[0], 2) + Math.Pow(c[1] - q[1], 2) + Math.Pow(c[2] - q[2], 2)) > 1e-6)
                                { tutti = false; break; }
                            }
                            if (tutti) { sta = true; break; }
                        }
                        if (sta) { giunto = e; lunghezzaMm = l; break; }
                    }
                }
                catch { }
            }
            return giunto != null;
        }

        /// <summary>Quanti simboli di saldatura ha la tavola, vista per vista; -1 se non si legge.</summary>
        private static int SimboliDiSaldaturaInTavola(IDrawingDoc disegno)
        {
            try
            {
                int n = 0;
                var v = disegno.GetFirstView() as IView;
                while (v != null) { n += v.GetWeldSymbolCount(); v = v.GetNextView() as IView; }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// L'ultimo simbolo di saldatura della vista (quello appena messo); se la
        /// vista non ne ha, l'ultimo di tutta la tavola, foglio compreso — la
        /// sonda li ha trovati cercando in tutte le viste, e dove SOLIDWORKS lo
        /// registri non e' misurato caso per caso. Null se non ce n'e'.
        /// </summary>
        private static IWeldSymbol UltimoSimboloDiSaldatura(IDrawingDoc disegno, IView vista)
        {
            try
            {
                var suoi = vista.GetWeldSymbols() as object[];
                if (suoi != null && suoi.Length > 0) return suoi[suoi.Length - 1] as IWeldSymbol;
                IWeldSymbol ultimo = null;
                var v = disegno.GetFirstView() as IView;
                while (v != null)
                {
                    var o = v.GetWeldSymbols() as object[];
                    if (o != null && o.Length > 0) ultimo = o[o.Length - 1] as IWeldSymbol;
                    v = v.GetNextView() as IView;
                }
                return ultimo;
            }
            catch { return null; }
        }
    }
}
