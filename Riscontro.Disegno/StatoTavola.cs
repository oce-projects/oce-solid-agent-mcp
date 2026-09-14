using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// GLI OCCHI SULLA TAVOLA.
    ///
    /// Lo stato del documento e' l'unica cosa che il modello vede: se una
    /// tavola gli arriva come «documento non di tipo parte», il modello puo'
    /// solo indovinare. Fino alla 0.39.0 era esattamente cosi' — una tavola
    /// era una riga di scuse.
    ///
    /// Qui si descrive quello su cui si lavora davvero: i FOGLI (formato,
    /// scala, quale e' attivo), le VISTE (nome, posizione, scala, quante quote
    /// portano, di quale modello sono) e i DOCUMENTI APERTI, che in questa
    /// famiglia non sono un dettaglio ma una precondizione.
    ///
    /// LA REGOLA GIA' PAGATA VALE ANCHE QUI: si scrive quello che AGGIUNGE.
    /// Non c'e' un elenco delle annotazioni una per una — sono centinaia su
    /// una tavola normale e nessuno le nomina; c'e' il loro CONTEGGIO, che e'
    /// quello che serve per accorgersi che una quota non e' arrivata.
    /// </summary>
    public partial class StatoDocumento
    {
        /// <summary>Quante viste si descrivono per esteso, al massimo.</summary>
        private const int MaxViste = 24;

        /// <summary>
        /// I documenti aperti adesso.
        ///
        /// Se `sw` e' null si torna almeno il documento che si sta leggendo:
        /// meglio una riga vera che un elenco assente, perche' l'assenza si
        /// legge come «non c'e' nient'altro» ed e' una bugia.
        /// </summary>
        private static List<Dictionary<string, object>> DocumentiAperti(
            ISldWorks sw, IModelDoc2 attivo)
        {
            var fuori = new List<Dictionary<string, object>>();
            string percorsoAttivo = "", titoloAttivo = "";
            try { percorsoAttivo = attivo?.GetPathName() ?? ""; } catch { }
            // IL TITOLO, NON SOLO IL PERCORSO. Un documento mai salvato
            // non ha percorso, e con il solo confronto sul percorso in un
            // elenco di documenti nuovi non risultava attivo NESSUNO.
            try { titoloAttivo = attivo?.GetTitle() ?? ""; } catch { }

            if (sw == null) return fuori;
            try
            {
                var d = sw.GetFirstDocument() as IModelDoc2;
                int guardia = 0;
                while (d != null && guardia++ < 200)
                {
                    string percorso = "", titolo = "";
                    int tipo = 0;
                    try { percorso = d.GetPathName() ?? ""; } catch { }
                    try { titolo = d.GetTitle() ?? ""; } catch { }
                    try { tipo = d.GetType(); } catch { }

                    var voce = new Dictionary<string, object>
                    {
                        ["titolo"] = titolo,
                        ["tipo"] = OperazioniSw.NomeTipoDocumento(tipo),
                    };
                    // Il percorso solo se c'e': un documento mai salvato non ne
                    // ha, e una stringa vuota in un elenco di percorsi e' un
                    // invito a usarla.
                    if (!string.IsNullOrWhiteSpace(percorso)) voce["percorso"] = percorso;
                    bool eLui =
                        !string.IsNullOrWhiteSpace(percorsoAttivo)
                            ? string.Equals(percorso, percorsoAttivo,
                                  StringComparison.OrdinalIgnoreCase)
                            : (!string.IsNullOrWhiteSpace(titoloAttivo) &&
                               string.Equals(titolo, titoloAttivo, StringComparison.Ordinal));
                    if (eLui) voce["attivo"] = true;

                    fuori.Add(voce);
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>Fogli, viste e conteggi di una tavola.</summary>
        private static void DescriviTavola(IDrawingDoc disegno, Dictionary<string, object> dati)
        {
            if (disegno == null)
            {
                dati["nota"] = "tavola illeggibile";
                return;
            }

            // ── i fogli ───────────────────────────────────────────────────
            string attivo = "";
            try { attivo = (disegno.GetCurrentSheet() as ISheet)?.GetName() ?? ""; } catch { }

            var fogli = new List<Dictionary<string, object>>();
            try
            {
                var nomi = disegno.GetSheetNames() as string[];
                if (nomi != null)
                {
                    for (int i = 0; i < nomi.Length; i++)
                    {
                        var voce = new Dictionary<string, object>
                        {
                            ["uid"] = "S" + (i + 1),
                            ["nome"] = nomi[i],
                        };
                        if (string.Equals(nomi[i], attivo, StringComparison.Ordinal))
                            voce["attivo"] = true;

                        // Le proprieta' del foglio si leggono solo da quello
                        // ATTIVO senza cambiare foglio, e cambiare foglio per
                        // leggere sarebbe una lettura che modifica: si legge
                        // quello che si puo' e si tace il resto.
                        if (voce.ContainsKey("attivo"))
                        {
                            var p = ProprietaFoglio(disegno);
                            if (p != null)
                            {
                                voce["formato_mm"] = new object[]
                                {
                                    Math.Round(Unita.Mm(p[5]), 1),
                                    Math.Round(Unita.Mm(p[6]), 1),
                                };
                                if (p[3] != 0)
                                    voce["scala"] = FormattaScala(p[2], p[3]);
                                // IL DIEDRO SI VEDE. E' l'unica cosa di una
                                // tavola che puo' essere sbagliata senza che
                                // nessuna misura se ne accorga: destra e
                                // sinistra scambiate, tavola plausibile,
                                // pezzo specchiato. Se non e' scritto qui, il
                                // modello non ha modo di guardarlo.
                                if (p.Length > 4)
                                    voce["diedro"] = p[4] != 0 ? "primo" : "terzo";
                            }
                        }
                        fogli.Add(voce);
                    }
                }
            }
            catch { }
            if (fogli.Count > 0) dati["fogli"] = fogli;

            // ── le viste ──────────────────────────────────────────────────
            //
            // LA PRIMA VISTA NON E' UNA VISTA: e' il FOGLIO. `GetFirstView()`
            // torna il foglio, e le viste vere cominciano dal `GetNextView()`.
            // Chi lo dimentica descrive un foglio come se fosse una vista, e
            // poi quota un oggetto che non ha geometria.
            var viste = new List<Dictionary<string, object>>();
            int quoteTotali = 0, visteTotali = 0;
            try
            {
                var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    visteTotali++;
                    int quote = 0;
                    try { quote = v.GetDimensionCount(); } catch { }
                    quoteTotali += quote;

                    if (viste.Count < MaxViste)
                    {
                        var voce = new Dictionary<string, object>
                        {
                            ["uid"] = "V" + visteTotali,
                        };
                        try { voce["nome"] = v.Name; } catch { }
                        try { voce["tipo"] = NomeTipoVista(v.Type); } catch { }

                        var pos = PosizioneVista(v);
                        if (pos != null) voce["posizione_mm"] = pos;

                        try
                        {
                            double s = v.ScaleDecimal;
                            if (s > 0) voce["scala"] = Math.Round(s, 4);
                        }
                        catch { }

                        // Zero quote non e' un dettaglio: e' la differenza fra
                        // una tavola e un disegnino. Si scrive sempre.
                        voce["quote"] = quote;

                        try
                        {
                            var rif = v.ReferencedDocument as IModelDoc2;
                            var p = rif?.GetPathName();
                            if (!string.IsNullOrWhiteSpace(p)) voce["modello"] = p;
                        }
                        catch { }

                        viste.Add(voce);
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }

            if (viste.Count > 0) dati["viste"] = viste;
            dati["viste_totali"] = visteTotali;
            dati["quote_totali"] = quoteTotali;
            if (visteTotali > viste.Count) dati["viste_troncate"] = true;

            // LA TAVOLA VUOTA VA DETTA, non lasciata dedurre da un elenco che
            // manca. E' lo stato in cui nasce ogni tavola, ed e' quello in cui
            // resta se `Create1stAngleViews2` ha detto «si'» senza fare niente.
            if (visteTotali == 0)
                dati["nota"] = "la tavola non ha ancora nessuna vista";
            else if (quoteTotali == 0)
                dati["nota"] = "le viste ci sono ma non c'e' nessuna quota";
        }

        private static double[] ProprietaFoglio(IDrawingDoc disegno)
        {
            try
            {
                var foglio = disegno.GetCurrentSheet() as ISheet;
                var p = foglio?.GetProperties2() as double[];
                return (p != null && p.Length >= 7) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>«1:2» invece di «0,5»: e' come si scrive su una tavola.</summary>
        private static string FormattaScala(double numeratore, double denominatore)
        {
            try
            {
                return Arrotonda(numeratore) + ":" + Arrotonda(denominatore);
            }
            catch { return null; }
        }

        private static string Arrotonda(double x)
        {
            double r = Math.Round(x, 4);
            return r == Math.Floor(r)
                ? ((long)r).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : r.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static object[] PosizioneVista(IView v)
        {
            try
            {
                var p = v.Position as double[];
                if (p == null || p.Length < 2) return null;
                return new object[]
                {
                    Math.Round(Unita.Mm(p[0]), 2),
                    Math.Round(Unita.Mm(p[1]), 2),
                };
            }
            catch { return null; }
        }

        private static string NomeTipoVista(int tipo)
        {
            switch (tipo)
            {
                case (int)swDrawingViewTypes_e.swDrawingSheet: return "foglio";
                case (int)swDrawingViewTypes_e.swDrawingProjectedView: return "proiezione";
                case (int)swDrawingViewTypes_e.swDrawingSectionView: return "sezione";
                case (int)swDrawingViewTypes_e.swDrawingDetailView: return "dettaglio";
                case (int)swDrawingViewTypes_e.swDrawingNamedView: return "vista con nome";
                case (int)swDrawingViewTypes_e.swDrawingAuxiliaryView: return "ausiliaria";
                case (int)swDrawingViewTypes_e.swDrawingAlternatePositionView: return "posizione alternativa";
                case (int)swDrawingViewTypes_e.swDrawingRelativeView: return "relativa";
                case (int)swDrawingViewTypes_e.swDrawingStandardView: return "standard";
                case (int)swDrawingViewTypes_e.swDrawingDetachedView: return "staccata";
                default: return "vista";
            }
        }
    }
}
