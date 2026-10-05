using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA DISTINTA DI TAGLIO IN TAVOLA (voce GT2).
    ///
    /// ── PERCHE' E' LA VOCE COL RAPPORTO MIGLIORE ────────────────────────
    ///
    /// Il calcolo difficile — lunghezze e quantita' dei profili di una
    /// carpenteria — **e' gia' fatto e misurato** da `cut_list`. Qui si
    /// porta quel dato in tavola. E soprattutto: **la fonte di verita'
    /// indipendente esiste gia'**, quindi la tabella non si dichiara inserita,
    /// si CONFRONTA.
    ///
    /// ── LA FIRMA CHE LA VOCE CHIEDEVA DI TROVARE ────────────────────────
    ///
    /// La voce diceva: *«`InsertTableAnnotation2` con TableTemplate di tipo
    /// WeldmentCutList, **oppure** `IWeldmentCutListAnnotation` se espone un
    /// inserimento diretto — da verificare quale»*. **Nessuna delle due.**
    ///
    ///     IView :: WeldmentCutListAnnotation InsertWeldmentTable(
    ///         Boolean UseAnchorPoint, Double X, Double Y, Int32 AnchorType,
    ///         String Configuration, String TableTemplate)
    ///
    /// Sta su `IView` — quarta volta in due giorni che una tabella sta su
    /// un'interfaccia diversa da quella che il nome suggerisce (distinta su
    /// `IView`, tabella fori su `IView`, tabella revisioni su `ISheet`).
    /// `IWeldmentCutListAnnotation` ha **sette metodi e nessuno tocca le
    /// celle**: per leggere il contenuto si passa da `ITableAnnotation`, che lo
    /// stesso oggetto implementa.
    ///
    /// ── LA COLONNA DELLE QUANTITA' NON SI INDOVINA ──────────────────────
    ///
    /// `GetColumnCustomProperty(i)` dice **quale proprieta' sta dietro a ogni
    /// colonna**. Cercare la colonna «QTY» per posizione, o per intestazione,
    /// vorrebbe dire sommare la colonna sbagliata su un modello di tabella
    /// diverso da quello di casa — e una distinta con le quantita' della
    /// colonna «lunghezza» e' plausibile, si stampa, e in officina si taglia
    /// il numero sbagliato di barre.
    ///
    /// ── E IL CONFRONTO, CHE E' IL PUNTO ────────────────────────────────
    ///
    /// La tabella dice quante barre ci sono. Il pezzo lo sa gia', per conto
    /// suo, dalle cartelle di distinta (`GetBodyCount`). **Due letture
    /// indipendenti dello stesso fatto**: se divergono, una delle due mente e
    /// non si sa quale — e lo si dice invece di scegliere la piu' comoda.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DrawingCutList(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "Il documento attivo non e' una tavola: la distinta di taglio in " +
                    "tavola vuole una tavola con dentro una vista del saldato.");

            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed(
                    "La tavola non ha viste: una distinta di taglio si aggancia a una " +
                    "vista del pezzo. Mettine una con «standard_views».");

            // LA VISTA COME LE ALTRE OPERAZIONI DI TAVOLA: per uid (V1, V2...) o
            // per nome. Fino alla 0.124.0 solo per nome — «V1», come la chiama lo
            // stato, rispondeva «non c'e'» (visto dal primo caso di banco che
            // l'ha chiamata, 26/9/2026).
            string reason;
            var view = ResolveView(drawing, names, Str(a, "view", null), out reason);
            if (reason != null) return Failed(reason);
            if (view == null)
                return Failed("Non riesco a prendere la vista della tavola.");

            string viewName = null;
            try { viewName = view.Name; } catch { }

            // ── IL PEZZO DIETRO LA VISTA, E LA SUA DISTINTA ─────────────
            //
            // Si legge PRIMA di inserire: e' il numero con cui si confrontera'
            // la tabella, e prenderlo dopo vorrebbe dire prenderlo da un
            // documento che l'inserimento potrebbe aver toccato.
            int bodiesInPart = -1;
            string partPath = null;
            try
            {
                var refObj = view.ReferencedDocument as IModelDoc2;
                if (refObj != null)
                {
                    partPath = refObj.GetPathName();
                    bodiesInPart = BodiesInCutListFolders(refObj);
                }
            }
            catch { }

            if (bodiesInPart == 0)
                return Failed(
                    "Il pezzo dietro la vista non ha nessuna cartella di distinta di " +
                    "taglio: la tabella nascerebbe vuota. Chiedi prima «cut_list» " +
                    "sul pezzo.");

            double x = Units.M(Num(a, "x_mm", 250));
            double y = Units.M(Num(a, "y_mm", 200));
            // THE CONFIGURATION IS STILL THE CALLER'S, OR EMPTY (Z-C21.4, finding
            // M32, open). IView~InsertWeldmentTable.html: «Configuration Name of
            // the "As Welded" configuration for the weldment cut-list table».
            // The view's own configuration (IView.ReferencedConfiguration) is NOT
            // passed in its place: the view shows the configuration that was
            // active when it was made, and a part of the company template is born
            // with the machined one active, not the as-welded one (Z-C21.3 probe
            // of L8-5); what InsertWeldmentTable does with it was never measured, while
            // the empty name makes the table of the bench case
            // cut_list_in_the_drawing. Which configuration SOLIDWORKS
            // takes for the empty name is not measured either. A probe decides
            // it first; the count it is compared with (BodiesInCutListFolders)
            // is read in the part's active configuration.
            string configuration = Str(a, "configuration", "");
            string template = Str(a, "template", "");

            object raw;
            try
            {
                raw = view.InsertWeldmentTable(
                    false,              // UseAnchorPoint: si mette dove si dice
                    x, y,
                    (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft,
                    configuration ?? "",
                    template ?? "");
            }
            catch (Exception ex)
            {
                return Failed("Distinta di taglio non inserita in tavola: " + ex.Message);
            }
            if (raw == null)
                return Failed(
                    "SOLIDWORKS non ha creato nessuna tabella e non ha detto perche'. " +
                    "Di solito vuol dire che il pezzo dietro la vista non e' un saldato " +
                    "con una distinta di taglio.");

            // Lo stesso oggetto COM implementa tutt'e due le interfacce: quella
            // della distinta (sette metodi, nessuno sulle celle) e quella della
            // tabella (le celle). Si prendono tutt'e due.
            var cutList = raw as IWeldmentCutListAnnotation;
            var table = raw as ITableAnnotation;
            if (table == null)
                return Failed(
                    "La tabella e' nata ma non si lascia leggere come tabella: senza " +
                    "leggerla non posso dire cosa c'e' dentro, e non lo chiamo fatto.");

            int lines = 0, columns = 0;
            try { lines = table.RowCount; } catch { }
            try { columns = table.ColumnCount; } catch { }
            if (lines <= 1)
                return Failed(
                    "La tabella e' nata con " + lines + " righe: e' vuota o ha solo " +
                    "l'intestazione. Una distinta vuota in tavola e' peggio di nessuna " +
                    "distinta, perche' si stampa e sembra che la carpenteria non abbia " +
                    "bars.");

            // ── LA COLONNA DELLE QUANTITA', CHIESTA E NON INDOVINATA ────
            //
            // PRIMA PER TIPO, POI PER NOME (voce Z-C21.2, rilievo L16-8).
            // IWeldmentCutListAnnotation~GetColumnCustomProperty.html, Remarks:
            // «This method returns an empty string if the column is not a
            // user-defined column». La QTY del modello di tabella predefinito
            // (template "", cioe' «cut list.sldwldtbt») puo' essere una colonna di
            // TIPO quantita', non definita dall'utente: cercandola solo per nome
            // della proprieta' restava -1, il confronto con la distinta del pezzo
            // — la ragione per cui l'operazione esiste — saltava, e la risposta
            // era un successo con una nota in fondo. Il tipo si legge con
            // ITableAnnotation~GetColumnType2.html («Type of column as defined by
            // swTableColumnTypes_e»; swWeldTableColumnType_Quantity = 402), come
            // fa gia' ColumnsAndPosition qui sotto. Il nome «QUANTITY» resta il
            // ripiego: il modello dell'officina ha le QTY come proprieta'.
            int quantityColumn = -1;
            string asFound = null;
            var columnProperty = new List<string>();
            for (int c = 0; c < columns; c++)
            {
                string p = null;
                try { if (cutList != null) p = cutList.GetColumnCustomProperty(c); }
                catch { }
                columnProperty.Add(p ?? "");
                int columnType = -1;
                try { columnType = table.GetColumnType2(c, true); } catch { }
                if (quantityColumn < 0 &&
                    columnType == (int)swTableColumnTypes_e.swWeldTableColumnType_Quantity)
                {
                    quantityColumn = c;
                    asFound = "tipo";
                }
            }
            for (int c = 0; quantityColumn < 0 && c < columns; c++)
            {
                string p = columnProperty[c];
                if (!string.IsNullOrWhiteSpace(p) &&
                    p.IndexOf("QUANTITY", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    quantityColumn = c;
                    asFound = "nome";
                }
            }

            var headers = new List<string>();
            for (int c = 0; c < columns; c++) headers.Add(Cell(table, 0, c));

            // Le righe, come testo. Non si interpreta niente: si riporta cosa
            // c'e' scritto, che e' quello che finisce sul foglio.
            var entries = new List<object>();
            int totalQuantity = 0;
            bool quantityRead = quantityColumn >= 0;
            for (int r = 1; r < lines; r++)
            {
                var cells = new List<string>();
                for (int c = 0; c < columns; c++) cells.Add(Cell(table, r, c));
                entries.Add(cells.ToArray());

                if (quantityColumn >= 0)
                {
                    int n;
                    if (int.TryParse(Cell(table, r, quantityColumn).Trim(),
                                     NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                        totalQuantity += n;
                    else quantityRead = false;
                }
            }

            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName ?? "(senza nome)",
                ["rows"] = lines,
                ["columns"] = columns,
                ["entries"] = lines - 1,
                ["headers"] = headers.ToArray(),
                ["column_properties"] = columnProperty.ToArray(),
                ["rows_read"] = entries.ToArray(),
            };
            if (!string.IsNullOrWhiteSpace(partPath)) measurements["part"] = partPath;
            if (bodiesInPart >= 0) measurements["bodies_in_part_cut_list"] = bodiesInPart;
            if (quantityRead) measurements["quantity_in_table"] = totalQuantity;
            // CAMPI DEDICATI, non una frase in fondo (rilievo L16-8). Nomi nuovi,
            // in inglese come i prossimi del ponte: come si e' trovata la colonna
            // delle quantita' («type», «name» o null) e se il confronto con la
            // distinta del pezzo e' stato fatto.
            measurements["quantity_column_found_by"] = asFound == "tipo" ? "type" : asFound == "nome" ? "name" : null;
            measurements["comparison_done"] = quantityRead && bodiesInPart > 0;

            // ── LE COLONNE CHE SERVONO, E UN POSTO SUL FOGLIO ───────────
            //
            // VISTO NEL PNG il 26/9/2026, col banco verde: il modello di tabella
            // dell'officina ha 17 colonne (autore, codice, trattamento, stato di
            // fornitura, QUANTITY e TOTAL LENGTH doppioni...), e la tabella messa
            // a 250/200 usciva dal foglio dopo la quarta. Le righe si leggevano
            // tutte — il controllo guardava le celle, non dove cadevano.
            // Si leggono prima (sopra) con tutte le colonne; poi si nascondono
            // quelle che in officina non servono, si misura la tabella e la si
            // mette in un posto libero dentro la cornice.
            string placeReason;
            if (!ColumnsAndPosition(drawing, table, columns, columnProperty, headers, a, measurements, out placeReason))
            {
                bool removed = RemoveCutTable(model, table);
                return Failed(placeReason + (removed ? " Ho tolto la tabella." : " Togli la tabella: " + Sentences.HowToWithdrawIt + "."));
            }

            // ── IL CONFRONTO ───────────────────────────────────────────
            //
            // E' la ragione per cui questa voce valeva la pena: la seconda
            // lettura esisteva gia'. Non si sceglie quale delle due credere —
            // si dice che non combaciano.
            if (quantityRead && bodiesInPart > 0)
            {
                measurements["match"] = totalQuantity == bodiesInPart;
                if (totalQuantity != bodiesInPart)
                {
                    measurements["verdict"] =
                        "ATTENZIONE: la tabella in tavola somma " + totalQuantity +
                        (totalQuantity == 1 ? " barra" : " barre") + ", e la distinta " +
                        "del pezzo ne ha " + bodiesInPart + ". **Le due non combaciano.** " +
                        "Non scelgo quale credere: una tavola che dice un numero diverso " +
                        "dal modello manda in officina la quantita' sbagliata, e nessuno " +
                        "dei due lato da' errore. Di solito vuol dire che il pezzo e' " +
                        "cambiato dopo l'ultimo aggiornamento della distinta: rifai " +
                        "«cut_list» sul pezzo e poi rimetti la tabella.";
                    _log("[agente] " + measurements["verdict"]);
                    return SucceededWithoutCreating(measurements);
                }
            }

            measurements["verdict"] =
                "Distinta di taglio in tavola sulla vista «" + (viewName ?? "?") + "»: " +
                (lines - 1) + (lines - 1 == 1 ? " voce" : " voci") +
                (quantityRead
                    ? ", " + totalQuantity + (totalQuantity == 1 ? " barra" : " barre")
                    : "") +
                (quantityRead && bodiesInPart > 0
                    ? ", e combacia con la distinta del pezzo (" + bodiesInPart + ")."
                    : ". Il confronto con la distinta del pezzo non si e' potuto fare.") +
                (quantityColumn < 0
                    ? " NOTA: non ho trovato la colonna delle quantita' ne' per tipo ne' fra le " +
                      "proprieta' di colonna, quindi non ho sommato niente (comparison_done: false)."
                    : "");
            _log("[agente] " + measurements["verdict"]);
            return SucceededWithoutCreating(measurements);
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Una cella come testo. `get_Text` puo' alzare su una cella unita o
        /// fuori misura: vuoto, e chi legge vede una cella vuota invece di
        /// perdere tutta la tabella.
        /// </summary>
        private static string Cell(ITableAnnotation t, int line, int column)
        {
            try { return t.Text[line, column] ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// Le proprieta' di colonna che in officina servono per tagliare: la
        /// lunghezza, i due angoli, la descrizione (il profilo). Col numero di voce
        /// e la quantita', che sono colonne di TIPO (`swWeldTableColumnType_*`).
        /// </summary>
        private static readonly HashSet<string> WorkshopColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LUNGHEZZA", "LENGTH", "ANGOLO1", "ANGOLO2", "ANGLE1", "ANGLE2", "DESCRIZIONE", "DESCRIPTION",
        };

        /// <summary>
        /// Nasconde le colonne che non servono (o tiene quelle chieste con
        /// «columns»), misura la tabella e la mette in un posto libero del
        /// foglio: dentro la cornice, fuori dalle viste e dal cartiglio. False
        /// col motivo se il posto non c'e' o se una colonna chiesta non esiste.
        /// </summary>
        private static bool ColumnsAndPosition(IDrawingDoc drawing, ITableAnnotation table, int columns,
                                          List<string> property, List<string> headers,
                                          Dictionary<string, object> a, Dictionary<string, object> measurements, out string reason)
        {
            reason = null;
            // ── 1. QUALI COLONNE ────────────────────────────────────────────
            var requestedItems = StringList(a, "columns");
            bool all = requestedItems.Any(c => string.Equals((c ?? "").Trim(), "all", StringComparison.OrdinalIgnoreCase));
            var keep = new bool[columns];
            if (all)
                for (int c = 0; c < columns; c++) keep[c] = true;
            else if (requestedItems.Count > 0)
            {
                foreach (var ch in requestedItems)
                {
                    int i = headers.FindIndex(t => string.Equals((t ?? "").Trim(), (ch ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                    if (i < 0)
                    {
                        reason = "La tabella non ha una colonna «" + ch + "». Ha: " + string.Join(", ", headers) +
                                 " (oppure chiedi «all»).";
                        return false;
                    }
                    keep[i] = true;
                }
            }
            else
            {
                int held = 0;
                for (int c = 0; c < columns; c++)
                {
                    int typ = -1;
                    try { typ = table.GetColumnType2(c, true); } catch { }
                    keep[c] = typ == (int)swTableColumnTypes_e.swWeldTableColumnType_ItemNumber ||
                                typ == (int)swTableColumnTypes_e.swWeldTableColumnType_Quantity ||
                                WorkshopColumns.Contains((property[c] ?? "").Trim());
                    if (keep[c]) held++;
                }
                // Un modello di tabella che non si riconosce resta com'e': meglio
                // tutte le colonne che una tabella senza lunghezze.
                if (held < 3) for (int c = 0; c < columns; c++) keep[c] = true;
            }
            var visible = new List<string>();
            var hiddenOnes = new List<string>();
            for (int c = columns - 1; c >= 0; c--)   // dall'ultima: nascondere non sposta quelle prima
            {
                if (keep[c]) continue;
                try { table.ColumnHidden[c] = true; } catch { }
            }
            // SI RILEGGONO (revisione del 26/9/2026): un'assegnazione ignorata
            // lascerebbe in tavola tutte e 17 le colonne con la risposta che dice
            // il contrario.
            for (int c = 0; c < columns; c++)
            {
                bool hidden = false;
                try { hidden = table.ColumnHidden[c]; } catch { }
                (hidden ? hiddenOnes : visible).Add(headers[c]);
                if (hidden == keep[c])
                {
                    reason = "La colonna «" + headers[c] + "» doveva essere " + (keep[c] ? "visibile" : "nascosta") +
                             " e non lo e': la tabella non e' quella che ti direi.";
                    return false;
                }
            }
            measurements["columns_visible"] = visible.ToArray();
            measurements["columns_hidden"] = hiddenOnes.ToArray();

            // ── 2. QUANTO E' GRANDE ─────────────────────────────────────────
            double w = 0, h = 0;
            try
            {
                int nc = table.ColumnCount, nr = table.RowCount;
                for (int c = 0; c < nc; c++) w += table.GetColumnWidth(c);
                for (int r = 0; r < nr; r++) h += table.GetRowHeight(r);
            }
            catch { }
            if (w <= 0 || h <= 0)
            {
                reason = "La tabella e' nata ma non riesco a misurarla: senza misura non so se sta nel foglio.";
                return false;
            }
            measurements["bounding_box_table_mm"] = new object[] { Math.Round(w * 1000, 1), Math.Round(h * 1000, 1) };

            // ── 3. DOVE (OperazioniPostoSulFoglio) ──────────────────────────
            string tableName = null;
            try { tableName = (table.GetAnnotation() as IAnnotation)?.GetName(); } catch { }
            var sheet = ReadSheet(drawing, null, tableName);
            measurements["title_block_estimated"] = sheet.EstimatedTitleBlock;
            bool requested = a.ContainsKey("x_mm") || a.ContainsKey("y_mm");
            double px, py; string location;
            if (requested)
            {
                // x_mm/y_mm sono l'angolo in ALTO a sinistra della tabella.
                px = Units.M(Num(a, "x_mm", 250)); py = Units.M(Num(a, "y_mm", 200));
                string why = sheet.Touches(px, py - h, px + w, py);
                if (why != null)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "La tabella ({0:0} x {1:0} mm) messa a {2:0}/{3:0} {4}. Togli «x_mm» e «y_mm» e la metto io in " +
                        "un posto libero, oppure nascondi colonne con «columns».", w * 1000, h * 1000, px * 1000, py * 1000, why);
                    return false;
                }
                location = "dove chiesto";
            }
            else
            {
                var down = FreePlace(sheet, w, h, out location);
                if (down == null)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "Sul foglio non c'e' un posto libero per la tabella ({0:0} x {1:0} mm) fuori dalle viste e dal " +
                        "cartiglio. Nascondi colonne con «columns», oppure usa un foglio piu' grande.", w * 1000, h * 1000);
                    return false;
                }
                px = down[0]; py = down[1] + h;   // l'ancora e' in alto a sinistra
            }
            double[] read = null;
            try
            {
                var an = table.GetAnnotation() as IAnnotation;
                an?.SetPosition2(px, py, 0);
                read = an?.GetPosition() as double[];
            }
            catch { }
            if (read == null || Math.Abs(read[0] - px) > 0.0005 || Math.Abs(read[1] - py) > 0.0005)
            {
                reason = "Ho provato a spostare la tabella " + location + " ma non si e' spostata (riletta " +
                         (read == null ? "niente" : string.Format(CultureInfo.InvariantCulture, "{0:0.#}/{1:0.#} mm", read[0] * 1000, read[1] * 1000)) + ").";
                return false;
            }
            measurements["position_table_mm"] = new object[] { Math.Round(px * 1000, 1), Math.Round(py * 1000, 1) };
            measurements["table_location"] = location;
            measurements["inside_sheet"] = true;
            return true;
        }

        /// <summary>
        /// Toglie la tabella appena messa. `DeleteSelection2` e non `EditDelete`
        /// (la seconda puo' aprire una finestra di conferma).
        /// </summary>
        private static bool RemoveCutTable(IModelDoc2 model, ITableAnnotation table)
        {
            try
            {
                model.ClearSelection2(true);
                var an = table.GetAnnotation() as IAnnotation;
                bool removed = an != null && an.Select3(false, null) &&
                             model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
                return removed;
            }
            catch { return false; }
        }

        /// <summary>
        /// Quanti corpi stanno nelle cartelle di distinta di taglio del pezzo.
        /// `-1` quando non si e' potuto leggere — che non e' zero.
        ///
        /// ONE WELDMENT ONLY (Z-C21.4, finding M32, open). The walk is
        /// FirstFeature / GetNextFeature, the top of the tree: the cut-list
        /// folders of a sub-weldment are not descended into, so on a part with
        /// sub-weldments their bodies are not counted and the comparison of
        /// drawing_cut_list can report a mismatch that is this count's.
        /// Descending (GetFirstSubFeature of the sub-weldment folders) waits for
        /// a probe on a part with a sub-weldment: whether those folders also
        /// show up in the top-level walk, and would be counted twice, is not
        /// known, and no bench case can build a sub-weldment yet.
        /// </summary>
        private static int BodiesInCutListFolders(IModelDoc2 part)
        {
            try
            {
                int n = 0;
                bool found = false;
                var f = part.FirstFeature() as IFeature;
                while (f != null)
                {
                    string typ = null;
                    try { typ = f.GetTypeName2(); } catch { }
                    if (typ == "CutListFolder")
                    {
                        var folder = f.GetSpecificFeature2() as IBodyFolder;
                        if (folder != null)
                        {
                            found = true;
                            try { n += folder.GetBodyCount(); } catch { }
                        }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
                return found ? n : 0;
            }
            catch { return -1; }
        }
    }
}
