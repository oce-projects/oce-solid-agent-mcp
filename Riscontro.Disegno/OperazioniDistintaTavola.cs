using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA DISTINTA DI TAGLIO IN TAVOLA (voce GT2).
    ///
    /// ── PERCHE' E' LA VOCE COL RAPPORTO MIGLIORE ────────────────────────
    ///
    /// Il calcolo difficile — lunghezze e quantita' dei profili di una
    /// carpenteria — **e' gia' fatto e misurato** da `distinta_taglio`. Qui si
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
    public partial class OperazioniSw
    {
        private Esito DistintaTaglioInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: la distinta di taglio in " +
                    "tavola vuole una tavola con dentro una vista del saldato.");

            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0)
                return Fallito(
                    "La tavola non ha viste: una distinta di taglio si aggancia a una " +
                    "vista del pezzo. Mettine una con «viste_standard».");

            // LA VISTA COME LE ALTRE OPERAZIONI DI TAVOLA: per uid (V1, V2...) o
            // per nome. Fino alla 0.124.0 solo per nome — «V1», come la chiama lo
            // stato, rispondeva «non c'e'» (visto dal primo caso di banco che
            // l'ha chiamata, 26/9/2026).
            string motivo;
            var vista = RisolviVista(disegno, nomi, Str(a, "vista", null), out motivo);
            if (motivo != null) return Fallito(motivo);
            if (vista == null)
                return Fallito("Non riesco a prendere la vista della tavola.");

            string nomeVista = null;
            try { nomeVista = vista.Name; } catch { }

            // ── IL PEZZO DIETRO LA VISTA, E LA SUA DISTINTA ─────────────
            //
            // Si legge PRIMA di inserire: e' il numero con cui si confrontera'
            // la tabella, e prenderlo dopo vorrebbe dire prenderlo da un
            // documento che l'inserimento potrebbe aver toccato.
            int corpiNelPezzo = -1;
            string percorsoPezzo = null;
            try
            {
                var rif = vista.ReferencedDocument as IModelDoc2;
                if (rif != null)
                {
                    percorsoPezzo = rif.GetPathName();
                    corpiNelPezzo = CorpiNelleCartelleDiDistinta(rif);
                }
            }
            catch { }

            if (corpiNelPezzo == 0)
                return Fallito(
                    "Il pezzo dietro la vista non ha nessuna cartella di distinta di " +
                    "taglio: la tabella nascerebbe vuota. Chiedi prima «distinta_taglio» " +
                    "sul pezzo.");

            double x = Unita.M(Num(a, "x_mm", 250));
            double y = Unita.M(Num(a, "y_mm", 200));
            string configurazione = Str(a, "configurazione", "");
            string template = Str(a, "template", "");

            object grezza;
            try
            {
                grezza = vista.InsertWeldmentTable(
                    false,              // UseAnchorPoint: si mette dove si dice
                    x, y,
                    (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft,
                    configurazione ?? "",
                    template ?? "");
            }
            catch (Exception ex)
            {
                return Fallito("Distinta di taglio non inserita in tavola: " + ex.Message);
            }
            if (grezza == null)
                return Fallito(
                    "SOLIDWORKS non ha creato nessuna tabella e non ha detto perche'. " +
                    "Di solito vuol dire che il pezzo dietro la vista non e' un saldato " +
                    "con una distinta di taglio.");

            // Lo stesso oggetto COM implementa tutt'e due le interfacce: quella
            // della distinta (sette metodi, nessuno sulle celle) e quella della
            // tabella (le celle). Si prendono tutt'e due.
            var distinta = grezza as IWeldmentCutListAnnotation;
            var tabella = grezza as ITableAnnotation;
            if (tabella == null)
                return Fallito(
                    "La tabella e' nata ma non si lascia leggere come tabella: senza " +
                    "leggerla non posso dire cosa c'e' dentro, e non lo chiamo fatto.");

            int righe = 0, colonne = 0;
            try { righe = tabella.RowCount; } catch { }
            try { colonne = tabella.ColumnCount; } catch { }
            if (righe <= 1)
                return Fallito(
                    "La tabella e' nata con " + righe + " righe: e' vuota o ha solo " +
                    "l'intestazione. Una distinta vuota in tavola e' peggio di nessuna " +
                    "distinta, perche' si stampa e sembra che la carpenteria non abbia " +
                    "barre.");

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
            // fa gia' ColonneEPosto qui sotto. Il nome «QUANTITY» resta il
            // ripiego: il modello dell'officina ha le QTY come proprieta'.
            int colonnaQuantita = -1;
            string comeTrovata = null;
            var proprietaDiColonna = new List<string>();
            for (int c = 0; c < colonne; c++)
            {
                string p = null;
                try { if (distinta != null) p = distinta.GetColumnCustomProperty(c); }
                catch { }
                proprietaDiColonna.Add(p ?? "");
                int tipoColonna = -1;
                try { tipoColonna = tabella.GetColumnType2(c, true); } catch { }
                if (colonnaQuantita < 0 &&
                    tipoColonna == (int)swTableColumnTypes_e.swWeldTableColumnType_Quantity)
                {
                    colonnaQuantita = c;
                    comeTrovata = "tipo";
                }
            }
            for (int c = 0; colonnaQuantita < 0 && c < colonne; c++)
            {
                string p = proprietaDiColonna[c];
                if (!string.IsNullOrWhiteSpace(p) &&
                    p.IndexOf("QUANTITY", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    colonnaQuantita = c;
                    comeTrovata = "nome";
                }
            }

            var intestazioni = new List<string>();
            for (int c = 0; c < colonne; c++) intestazioni.Add(Cella(tabella, 0, c));

            // Le righe, come testo. Non si interpreta niente: si riporta cosa
            // c'e' scritto, che e' quello che finisce sul foglio.
            var voci = new List<object>();
            int quantitaTotale = 0;
            bool quantitaLetta = colonnaQuantita >= 0;
            for (int r = 1; r < righe; r++)
            {
                var celle = new List<string>();
                for (int c = 0; c < colonne; c++) celle.Add(Cella(tabella, r, c));
                voci.Add(celle.ToArray());

                if (colonnaQuantita >= 0)
                {
                    int n;
                    if (int.TryParse(Cella(tabella, r, colonnaQuantita).Trim(),
                                     NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                        quantitaTotale += n;
                    else quantitaLetta = false;
                }
            }

            var misure = new Dictionary<string, object>
            {
                ["vista"] = nomeVista ?? "(senza nome)",
                ["righe"] = righe,
                ["colonne"] = colonne,
                ["voci"] = righe - 1,
                ["intestazioni"] = intestazioni.ToArray(),
                ["proprieta_delle_colonne"] = proprietaDiColonna.ToArray(),
                ["righe_lette"] = voci.ToArray(),
            };
            if (!string.IsNullOrWhiteSpace(percorsoPezzo)) misure["pezzo"] = percorsoPezzo;
            if (corpiNelPezzo >= 0) misure["corpi_nella_distinta_del_pezzo"] = corpiNelPezzo;
            if (quantitaLetta) misure["quantita_in_tabella"] = quantitaTotale;
            // CAMPI DEDICATI, non una frase in fondo (rilievo L16-8). Nomi nuovi,
            // in inglese come i prossimi del ponte: come si e' trovata la colonna
            // delle quantita' («type», «name» o null) e se il confronto con la
            // distinta del pezzo e' stato fatto.
            misure["quantity_column_found_by"] = comeTrovata == "tipo" ? "type" : comeTrovata == "nome" ? "name" : null;
            misure["comparison_done"] = quantitaLetta && corpiNelPezzo > 0;

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
            string motivoPosto;
            if (!ColonneEPosto(disegno, tabella, colonne, proprietaDiColonna, intestazioni, a, misure, out motivoPosto))
            {
                bool tolta = TogliLaTabellaDiTaglio(modello, tabella);
                return Fallito(motivoPosto + (tolta ? " Ho tolto la tabella." : " Togli la tabella: " + Frasi.CosiSiRitira + "."));
            }

            // ── IL CONFRONTO ───────────────────────────────────────────
            //
            // E' la ragione per cui questa voce valeva la pena: la seconda
            // lettura esisteva gia'. Non si sceglie quale delle due credere —
            // si dice che non combaciano.
            if (quantitaLetta && corpiNelPezzo > 0)
            {
                misure["combaciano"] = quantitaTotale == corpiNelPezzo;
                if (quantitaTotale != corpiNelPezzo)
                {
                    misure["giudizio"] =
                        "ATTENZIONE: la tabella in tavola somma " + quantitaTotale +
                        (quantitaTotale == 1 ? " barra" : " barre") + ", e la distinta " +
                        "del pezzo ne ha " + corpiNelPezzo + ". **Le due non combaciano.** " +
                        "Non scelgo quale credere: una tavola che dice un numero diverso " +
                        "dal modello manda in officina la quantita' sbagliata, e nessuno " +
                        "dei due lato da' errore. Di solito vuol dire che il pezzo e' " +
                        "cambiato dopo l'ultimo aggiornamento della distinta: rifai " +
                        "«distinta_taglio» sul pezzo e poi rimetti la tabella.";
                    _log("[agente] " + misure["giudizio"]);
                    return RiuscitoSenzaCreare(misure);
                }
            }

            misure["giudizio"] =
                "Distinta di taglio in tavola sulla vista «" + (nomeVista ?? "?") + "»: " +
                (righe - 1) + (righe - 1 == 1 ? " voce" : " voci") +
                (quantitaLetta
                    ? ", " + quantitaTotale + (quantitaTotale == 1 ? " barra" : " barre")
                    : "") +
                (quantitaLetta && corpiNelPezzo > 0
                    ? ", e combacia con la distinta del pezzo (" + corpiNelPezzo + ")."
                    : ". Il confronto con la distinta del pezzo non si e' potuto fare.") +
                (colonnaQuantita < 0
                    ? " NOTA: non ho trovato la colonna delle quantita' ne' per tipo ne' fra le " +
                      "proprieta' di colonna, quindi non ho sommato niente (comparison_done: false)."
                    : "");
            _log("[agente] " + misure["giudizio"]);
            return RiuscitoSenzaCreare(misure);
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Una cella come testo. `get_Text` puo' alzare su una cella unita o
        /// fuori misura: vuoto, e chi legge vede una cella vuota invece di
        /// perdere tutta la tabella.
        /// </summary>
        private static string Cella(ITableAnnotation t, int riga, int colonna)
        {
            try { return t.Text[riga, colonna] ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// Le proprieta' di colonna che in officina servono per tagliare: la
        /// lunghezza, i due angoli, la descrizione (il profilo). Col numero di voce
        /// e la quantita', che sono colonne di TIPO (`swWeldTableColumnType_*`).
        /// </summary>
        private static readonly HashSet<string> ColonneDiOfficina = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LUNGHEZZA", "LENGTH", "ANGOLO1", "ANGOLO2", "ANGLE1", "ANGLE2", "DESCRIZIONE", "DESCRIPTION",
        };

        /// <summary>
        /// Nasconde le colonne che non servono (o tiene quelle chieste con
        /// «colonne»), misura la tabella e la mette in un posto libero del
        /// foglio: dentro la cornice, fuori dalle viste e dal cartiglio. False
        /// col motivo se il posto non c'e' o se una colonna chiesta non esiste.
        /// </summary>
        private static bool ColonneEPosto(IDrawingDoc disegno, ITableAnnotation tabella, int colonne,
                                          List<string> proprieta, List<string> intestazioni,
                                          Dictionary<string, object> a, Dictionary<string, object> misure, out string motivo)
        {
            motivo = null;
            // ── 1. QUALI COLONNE ────────────────────────────────────────────
            var chieste = Lista(a, "colonne");
            bool tutte = chieste.Any(c => string.Equals((c ?? "").Trim(), "tutte", StringComparison.OrdinalIgnoreCase));
            var tenere = new bool[colonne];
            if (tutte)
                for (int c = 0; c < colonne; c++) tenere[c] = true;
            else if (chieste.Count > 0)
            {
                foreach (var ch in chieste)
                {
                    int i = intestazioni.FindIndex(t => string.Equals((t ?? "").Trim(), (ch ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                    if (i < 0)
                    {
                        motivo = "La tabella non ha una colonna «" + ch + "». Ha: " + string.Join(", ", intestazioni) +
                                 " (oppure chiedi «tutte»).";
                        return false;
                    }
                    tenere[i] = true;
                }
            }
            else
            {
                int tenute = 0;
                for (int c = 0; c < colonne; c++)
                {
                    int tipo = -1;
                    try { tipo = tabella.GetColumnType2(c, true); } catch { }
                    tenere[c] = tipo == (int)swTableColumnTypes_e.swWeldTableColumnType_ItemNumber ||
                                tipo == (int)swTableColumnTypes_e.swWeldTableColumnType_Quantity ||
                                ColonneDiOfficina.Contains((proprieta[c] ?? "").Trim());
                    if (tenere[c]) tenute++;
                }
                // Un modello di tabella che non si riconosce resta com'e': meglio
                // tutte le colonne che una tabella senza lunghezze.
                if (tenute < 3) for (int c = 0; c < colonne; c++) tenere[c] = true;
            }
            var visibili = new List<string>();
            var nascoste = new List<string>();
            for (int c = colonne - 1; c >= 0; c--)   // dall'ultima: nascondere non sposta quelle prima
            {
                if (tenere[c]) continue;
                try { tabella.ColumnHidden[c] = true; } catch { }
            }
            // SI RILEGGONO (revisione del 26/9/2026): un'assegnazione ignorata
            // lascerebbe in tavola tutte e 17 le colonne con la risposta che dice
            // il contrario.
            for (int c = 0; c < colonne; c++)
            {
                bool nascosta = false;
                try { nascosta = tabella.ColumnHidden[c]; } catch { }
                (nascosta ? nascoste : visibili).Add(intestazioni[c]);
                if (nascosta == tenere[c])
                {
                    motivo = "La colonna «" + intestazioni[c] + "» doveva essere " + (tenere[c] ? "visibile" : "nascosta") +
                             " e non lo e': la tabella non e' quella che ti direi.";
                    return false;
                }
            }
            misure["colonne_visibili"] = visibili.ToArray();
            misure["colonne_nascoste"] = nascoste.ToArray();

            // ── 2. QUANTO E' GRANDE ─────────────────────────────────────────
            double w = 0, h = 0;
            try
            {
                int nc = tabella.ColumnCount, nr = tabella.RowCount;
                for (int c = 0; c < nc; c++) w += tabella.GetColumnWidth(c);
                for (int r = 0; r < nr; r++) h += tabella.GetRowHeight(r);
            }
            catch { }
            if (w <= 0 || h <= 0)
            {
                motivo = "La tabella e' nata ma non riesco a misurarla: senza misura non so se sta nel foglio.";
                return false;
            }
            misure["ingombro_tabella_mm"] = new object[] { Math.Round(w * 1000, 1), Math.Round(h * 1000, 1) };

            // ── 3. DOVE (OperazioniPostoSulFoglio) ──────────────────────────
            string nomeTabella = null;
            try { nomeTabella = (tabella.GetAnnotation() as IAnnotation)?.GetName(); } catch { }
            var foglio = LeggiIlFoglio(disegno, null, nomeTabella);
            misure["cartiglio_stimato"] = foglio.CartiglioStimato;
            bool chiesta = a.ContainsKey("x_mm") || a.ContainsKey("y_mm");
            double px, py; string dove;
            if (chiesta)
            {
                // x_mm/y_mm sono l'angolo in ALTO a sinistra della tabella.
                px = Unita.M(Num(a, "x_mm", 250)); py = Unita.M(Num(a, "y_mm", 200));
                string perche = foglio.Tocca(px, py - h, px + w, py);
                if (perche != null)
                {
                    motivo = string.Format(CultureInfo.InvariantCulture,
                        "La tabella ({0:0} x {1:0} mm) messa a {2:0}/{3:0} {4}. Togli «x_mm» e «y_mm» e la metto io in " +
                        "un posto libero, oppure nascondi colonne con «colonne».", w * 1000, h * 1000, px * 1000, py * 1000, perche);
                    return false;
                }
                dove = "dove chiesto";
            }
            else
            {
                var basso = PostoLibero(foglio, w, h, out dove);
                if (basso == null)
                {
                    motivo = string.Format(CultureInfo.InvariantCulture,
                        "Sul foglio non c'e' un posto libero per la tabella ({0:0} x {1:0} mm) fuori dalle viste e dal " +
                        "cartiglio. Nascondi colonne con «colonne», oppure usa un foglio piu' grande.", w * 1000, h * 1000);
                    return false;
                }
                px = basso[0]; py = basso[1] + h;   // l'ancora e' in alto a sinistra
            }
            double[] letta = null;
            try
            {
                var an = tabella.GetAnnotation() as IAnnotation;
                an?.SetPosition2(px, py, 0);
                letta = an?.GetPosition() as double[];
            }
            catch { }
            if (letta == null || Math.Abs(letta[0] - px) > 0.0005 || Math.Abs(letta[1] - py) > 0.0005)
            {
                motivo = "Ho provato a spostare la tabella " + dove + " ma non si e' spostata (riletta " +
                         (letta == null ? "niente" : string.Format(CultureInfo.InvariantCulture, "{0:0.#}/{1:0.#} mm", letta[0] * 1000, letta[1] * 1000)) + ").";
                return false;
            }
            misure["posizione_tabella_mm"] = new object[] { Math.Round(px * 1000, 1), Math.Round(py * 1000, 1) };
            misure["dove_sta_la_tabella"] = dove;
            misure["dentro_il_foglio"] = true;
            return true;
        }

        /// <summary>
        /// Toglie la tabella appena messa. `DeleteSelection2` e non `EditDelete`
        /// (la seconda puo' aprire una finestra di conferma).
        /// </summary>
        private static bool TogliLaTabellaDiTaglio(IModelDoc2 modello, ITableAnnotation tabella)
        {
            try
            {
                modello.ClearSelection2(true);
                var an = tabella.GetAnnotation() as IAnnotation;
                bool tolta = an != null && an.Select3(false, null) &&
                             modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                modello.ClearSelection2(true);
                return tolta;
            }
            catch { return false; }
        }

        /// <summary>
        /// Quanti corpi stanno nelle cartelle di distinta di taglio del pezzo.
        /// `-1` quando non si e' potuto leggere — che non e' zero.
        /// </summary>
        private static int CorpiNelleCartelleDiDistinta(IModelDoc2 pezzo)
        {
            try
            {
                int n = 0;
                bool trovata = false;
                var f = pezzo.FirstFeature() as IFeature;
                while (f != null)
                {
                    string tipo = null;
                    try { tipo = f.GetTypeName2(); } catch { }
                    if (tipo == "CutListFolder")
                    {
                        var cartella = f.GetSpecificFeature2() as IBodyFolder;
                        if (cartella != null)
                        {
                            trovata = true;
                            try { n += cartella.GetBodyCount(); } catch { }
                        }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
                return trovata ? n : 0;
            }
            catch { return -1; }
        }
    }
}
