using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA DISTINTA BASE E I PALLONCINI IN TAVOLA (voce GT9).
    ///
    /// `IView.InsertBomTable4(UseAnchorPoint, X, Y, AnchorType, BomType,
    /// Configuration, TableTemplate, Hidden, IndentedNumberingType,
    /// DetailedCutList)` sta sulla VISTA; `IDrawingDoc.AutoBalloon5` prende un
    /// `AutoBalloonOptions` costruito con `CreateAutoBalloonOptions` (la forma con
    /// l'oggetto: un errore di posizione fra nove argomenti non da' eccezione, da'
    /// un disegno diverso).
    ///
    /// ── IL CONFRONTO ────────────────────────────────────────────────────
    ///
    /// La distinta dice quali componenti e quanti. L'assieme lo sa per conto suo
    /// (`IAssemblyDoc.GetComponents(true)`, il primo livello, raggruppato per file
    /// e configurazione): **due letture dello stesso fatto**, e se non combaciano
    /// lo si dice. I palloncini si contano DOPO, sulla vista.
    ///
    /// Tipi di distinta (swBomType_e parte da 1, a differenza di altri enum):
    /// «primo_livello» (TopLevelOnly = 2, predefinito), «solo_parti» (1),
    /// «indentata» (3). Si scelgono per nome: un intero passato da fuori e' il
    /// modo perfetto di mettere il tipo sbagliato senza errore.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito DistintaBaseInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito("Il documento attivo non e' una tavola: la distinta base sta su una vista di un assieme.");
            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0) return Fallito("La tavola non ha viste: metti prima le viste dell'assieme con «viste_standard».");
            string motivo;
            var vista = RisolviVista(disegno, nomi, Str(a, "vista", null), out motivo);
            if (motivo != null) return Fallito(motivo);
            if (vista == null) return Fallito("Non riesco a prendere la vista.");

            var assieme = vista.ReferencedDocument as IAssemblyDoc;
            if (assieme == null)
                return Fallito("Dietro la vista «" + (vista.GetName2() ?? "") + "» non c'e' un assieme: la distinta base e' " +
                               "l'elenco dei componenti di un assieme. Per un saldato usa «distinta_taglio_in_tavola».");

            string tipoChiesto = (Str(a, "tipo", "primo_livello") ?? "primo_livello").Trim().ToLowerInvariant();
            int tipoBom;
            switch (tipoChiesto)
            {
                case "primo_livello": tipoBom = (int)swBomType_e.swBomType_TopLevelOnly; break;
                case "solo_parti": tipoBom = (int)swBomType_e.swBomType_PartsOnly; break;
                case "indentata": tipoBom = (int)swBomType_e.swBomType_Indented; break;
                default:
                    return Fallito("«tipo» e' uno fra «primo_livello» (predefinito), «solo_parti», «indentata», non «" + tipoChiesto + "».");
            }

            // ── L'ASSIEME, LETTO PRIMA ──────────────────────────────────────
            var attese = ComponentiPerVoce(assieme, tipoBom == (int)swBomType_e.swBomType_TopLevelOnly);
            if (attese == null) return Fallito("Non riesco a leggere i componenti dell'assieme: senza, non posso confrontare la distinta.");
            if (attese.Count == 0) return Fallito("L'assieme non ha componenti: la distinta sarebbe vuota.");

            string modelloTabella = Str(a, "template", null);
            if (string.IsNullOrWhiteSpace(modelloTabella))
                modelloTabella = PrimoModelloDiTabella((int)swUserPreferenceStringValue_e.swFileLocationsBOMTemplates, ".sldbomtbt");
            string configurazione = "";
            try { configurazione = vista.ReferencedConfiguration ?? ""; } catch { }

            int tabellePrima = TabelleDellaTavolaTutte(disegno);
            IBomTableAnnotation bom = null;
            try
            {
                bom = vista.InsertBomTable4(false, 0.02, 0.28, (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft,
                                            tipoBom, configurazione, modelloTabella ?? "", false,
                                            (int)swNumberingType_e.swNumberingType_Detailed, false) as IBomTableAnnotation;
            }
            catch (Exception ex) { return Fallito("La distinta base non e' nata: " + ex.Message); }
            var tabella = bom as ITableAnnotation;
            if (bom == null || tabella == null)
                return Fallito("SOLIDWORKS non ha creato la distinta base e non ha detto perche' (modello di tabella «" +
                               (modelloTabella ?? "") + "»).");

            // ── LE RIGHE, LETTE ─────────────────────────────────────────────
            int righe = 0, colonne = 0;
            try { righe = tabella.RowCount; colonne = tabella.ColumnCount; } catch { }
            int colQuantita = -1, colNumero = -1;
            var intestazioni = new List<string>();
            for (int c = 0; c < colonne; c++)
            {
                intestazioni.Add(Cella(tabella, 0, c));
                int t = -1;
                try { t = tabella.GetColumnType2(c, false); } catch { }
                if (t == (int)swTableColumnTypes_e.swBomTableColumnType_Quantity && colQuantita < 0) colQuantita = c;
                if (t == (int)swTableColumnTypes_e.swBomTableColumnType_ItemNumber && colNumero < 0) colNumero = c;
            }
            var righeLette = new List<object>();
            int quantitaTot = 0; bool quantitaLetta = colQuantita >= 0;
            for (int r = 1; r < righe; r++)
            {
                var celle = new List<string>();
                for (int c = 0; c < colonne; c++) celle.Add(Cella(tabella, r, c));
                righeLette.Add(celle.ToArray());
                if (colQuantita >= 0)
                {
                    int q;
                    if (int.TryParse(Cella(tabella, r, colQuantita).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out q)) quantitaTot += q;
                    else quantitaLetta = false;
                }
            }
            int voci = righe - 1;
            int istanze = attese.Values.Sum();
            var misure = new Dictionary<string, object>
            {
                ["vista"] = vista.GetName2(),
                ["tipo"] = tipoChiesto,
                ["righe"] = righe,
                ["voci"] = voci,
                ["intestazioni"] = intestazioni.ToArray(),
                ["righe_lette"] = righeLette.ToArray(),
                ["quantita_in_tabella"] = quantitaLetta ? (object)quantitaTot : null,
                ["componenti_distinti_nell_assieme"] = attese.Count,
                ["istanze_nell_assieme"] = istanze,
                ["tabelle_prima"] = NumeroOppureNull(tabellePrima),
            };

            string storto = null;
            if (voci != attese.Count) storto = "la distinta ha " + voci + " voci e l'assieme " + attese.Count + " componenti distinti";
            else if (!quantitaLetta) storto = "la colonna delle quantita' non si legge";
            else if (quantitaTot != istanze) storto = "la distinta somma " + quantitaTot + " pezzi e l'assieme ne ha " + istanze;
            if (storto != null)
            {
                bool tolta = TogliLaTabellaDiTaglio(modello, tabella);
                return Fallito("La distinta base non combacia con l'assieme: " + storto + ". " +
                               (tolta ? "L'ho tolta." : "Toglila: " + Frasi.CosiSiRitira + "."));
            }
            misure["combaciano"] = true;

            // ── IL POSTO ────────────────────────────────────────────────────
            double w = 0, h = 0;
            try
            {
                for (int c = 0; c < tabella.ColumnCount; c++) w += tabella.GetColumnWidth(c);
                for (int r = 0; r < tabella.RowCount; r++) h += tabella.GetRowHeight(r);
            }
            catch { }
            string nomeTabella = null;
            try { nomeTabella = (tabella.GetAnnotation() as IAnnotation)?.GetName(); } catch { }
            var foglio = LeggiIlFoglio(disegno, null, nomeTabella);
            string dove = null;
            var basso = w > 0 && h > 0 ? PostoLibero(foglio, w, h, out dove) : null;
            if (basso == null)
            {
                bool tolta = TogliLaTabellaDiTaglio(modello, tabella);
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "Sul foglio non c'e' un posto libero per la distinta base ({0:0} x {1:0} mm) fuori dalle viste e dal cartiglio. " +
                    "Usa un foglio piu' grande. {2}", w * 1000, h * 1000, tolta ? "L'ho tolta." : ""));
            }
            try { (tabella.GetAnnotation() as IAnnotation)?.SetPosition2(basso[0], basso[1] + h, 0); } catch { }
            misure["ingombro_tabella_mm"] = new object[] { Math.Round(w * 1000, 1), Math.Round(h * 1000, 1) };
            misure["dove_sta_la_tabella"] = dove;
            misure["dentro_il_foglio"] = true;
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// I componenti per voce di distinta: chiave = file + configurazione,
        /// valore = quante istanze. Solo il primo livello se `primoLivello`, se no
        /// le sole parti di tutti i livelli. Null se non si legge.
        /// </summary>
        private static Dictionary<string, int> ComponentiPerVoce(IAssemblyDoc assieme, bool primoLivello)
        {
            try
            {
                var fuori = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var comp = assieme.GetComponents(primoLivello) as object[];
                foreach (var o in comp ?? new object[0])
                {
                    var c = o as IComponent2;
                    if (c == null) continue;
                    bool soppresso = false, escluso = false;
                    try { soppresso = c.IsSuppressed(); } catch { }
                    try { escluso = c.ExcludeFromBOM; } catch { }
                    if (soppresso || escluso) continue;
                    if (!primoLivello)
                    {
                        // solo parti: un sottoassieme non e' una voce
                        var m = c.GetModelDoc2() as IModelDoc2;
                        if (m is IAssemblyDoc) continue;
                    }
                    string chiave = (c.GetPathName() ?? "") + "|" + (c.ReferencedConfiguration ?? "");
                    fuori[chiave] = fuori.TryGetValue(chiave, out int n) ? n + 1 : 1;
                }
                return fuori;
            }
            catch { return null; }
        }

        private static int TabelleDellaTavolaTutte(IDrawingDoc disegno)
        {
            try
            {
                int n = 0;
                var v = disegno.GetFirstView() as IView;
                while (v != null) { n += ((v.GetTableAnnotations() as object[]) ?? new object[0]).Length; v = v.GetNextView() as IView; }
                return n;
            }
            catch { return -1; }
        }

        // ──────────────────────────────────────────────────────────────────
        // palloncini
        // ──────────────────────────────────────────────────────────────────
        private Esito Palloncini(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null) return Fallito("Il documento attivo non e' una tavola: i palloncini stanno su una vista di un assieme.");
            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0) return Fallito("La tavola non ha viste: metti prima le viste dell'assieme.");
            string motivo;
            var vista = RisolviVista(disegno, nomi, Str(a, "vista", null), out motivo);
            if (motivo != null) return Fallito(motivo);
            if (vista == null) return Fallito("Non riesco a prendere la vista.");
            var assieme = vista.ReferencedDocument as IAssemblyDoc;
            if (assieme == null)
                return Fallito("Dietro la vista non c'e' un assieme: i palloncini numerano i componenti di un assieme.");
            if (TabelleDellaTavolaTutte(disegno) <= 0 || !HaDistintaBase(disegno))
                return Fallito("In tavola non c'e' una distinta base: i palloncini portano il numero di voce della distinta. " +
                               "Metti prima «distinta_base_in_tavola».");

            string disposizione = (Str(a, "disposizione", "quadrato") ?? "quadrato").Trim().ToLowerInvariant();
            int layout;
            switch (disposizione)
            {
                case "quadrato": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Square; break;
                case "cerchio": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Circle; break;
                case "sopra": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Top; break;
                case "sotto": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Bottom; break;
                default: return Fallito("«disposizione» e' una fra «quadrato», «cerchio», «sopra», «sotto», non «" + disposizione + "».");
            }

            int prima = PalloncinidellaVista(vista);
            object nati = null;
            try
            {
                modello.ClearSelection2(true);
                modello.Extension.SelectByID2(vista.GetName2(), "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                var opt = disegno.CreateAutoBalloonOptions() as AutoBalloonOptions;
                opt.Layout = layout;
                opt.IgnoreMultiple = true;
                opt.Style = (int)swBalloonStyle_e.swBS_Circular;
                opt.Size = (int)swBalloonFit_e.swBF_2Chars;
                opt.UpperTextContent = (int)swBalloonTextContent_e.swBalloonTextItemNumber;
                nati = disegno.AutoBalloon5(opt);
            }
            catch (Exception ex) { modello.ClearSelection2(true); return Fallito("I palloncini non sono nati: " + ex.Message); }
            modello.ClearSelection2(true);
            int dopo = PalloncinidellaVista(vista);
            var visibili = ComponentiVisibili(vista);
            var misure = new Dictionary<string, object>
            {
                ["vista"] = vista.GetName2(),
                ["palloncini_prima"] = NumeroOppureNull(prima),
                ["palloncini_dopo"] = NumeroOppureNull(dopo),
                ["palloncini_messi"] = prima >= 0 && dopo >= 0 ? (object)(dopo - prima) : null,
                ["componenti_visibili"] = NumeroOppureNull(visibili),
                ["numeri"] = NumeriDeiPalloncini(vista).ToArray(),
            };
            if (prima < 0 || dopo < 0)
                return Fallito("Non riesco a contare i palloncini della vista: non so se sono nati. Guarda la tavola.");
            if (dopo <= prima)
                return Fallito("SOLIDWORKS non ha messo nessun palloncino (erano " + prima + ", sono " + dopo + ").");
            if (visibili > 0 && dopo - prima != visibili)
                misure["attenzione"] = "Palloncini messi " + (dopo - prima) + ", componenti che la vista mostra " + visibili +
                                       ": con «ignora i multipli» un componente ripetuto ha un palloncino solo — controlla.";
            return RiuscitoSenzaCreare(misure);
        }

        private static bool HaDistintaBase(IDrawingDoc disegno)
        {
            try
            {
                var v = disegno.GetFirstView() as IView;
                while (v != null)
                {
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                        if (o is IBomTableAnnotation) return true;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return false;
        }

        /// <summary>I palloncini della vista: le note che sono palloncini di distinta; -1 se non si legge.</summary>
        private static int PalloncinidellaVista(IView vista)
        {
            try
            {
                int n = 0;
                var nota = vista.GetFirstNote() as INote;
                while (nota != null)
                {
                    if (nota.IsBomBalloon()) n++;
                    nota = nota.GetNext() as INote;
                }
                return n;
            }
            catch { return -1; }
        }

        private static List<string> NumeriDeiPalloncini(IView vista)
        {
            var fuori = new List<string>();
            try
            {
                var nota = vista.GetFirstNote() as INote;
                while (nota != null)
                {
                    if (nota.IsBomBalloon()) fuori.Add(nota.GetText() ?? "");
                    nota = nota.GetNext() as INote;
                }
            }
            catch { }
            return fuori;
        }

        private static int ComponentiVisibili(IView vista)
        {
            try { return ((vista.GetVisibleComponents() as object[]) ?? new object[0]).Length; }
            catch { return -1; }
        }
    }
}
