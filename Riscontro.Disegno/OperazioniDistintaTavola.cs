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

            string voluta = Str(a, "vista", null);
            var vista = TrovaLaVista(disegno, voluta);
            if (vista == null)
                return Fallito(
                    voluta == null
                        ? "Non riesco a prendere la prima vista della tavola."
                        : "Non c'e' nessuna vista che si chiami «" + voluta + "». Ci sono: " +
                          string.Join(", ", nomi) + ".");

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
            int colonnaQuantita = -1;
            var proprietaDiColonna = new List<string>();
            for (int c = 0; c < colonne; c++)
            {
                string p = null;
                try { if (distinta != null) p = distinta.GetColumnCustomProperty(c); }
                catch { }
                proprietaDiColonna.Add(p ?? "");
                if (colonnaQuantita < 0 && !string.IsNullOrWhiteSpace(p) &&
                    p.IndexOf("QUANTITY", StringComparison.OrdinalIgnoreCase) >= 0)
                    colonnaQuantita = c;
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
                    ? " NOTA: non ho trovato la colonna delle quantita' fra le proprieta' " +
                      "di colonna, quindi non ho sommato niente."
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
        /// La vista chiesta per nome, o la prima della tavola.
        /// **Il foglio non e' una vista** e non va mai restituito: `GetFirstView`
        /// torna il FOGLIO, e le viste cominciano dal successivo.
        /// </summary>
        private static IView TrovaLaVista(IDrawingDoc disegno, string nome)
        {
            try
            {
                var foglio = disegno.GetFirstView() as IView;
                var v = foglio == null ? null : foglio.GetNextView() as IView;
                while (v != null)
                {
                    if (string.IsNullOrWhiteSpace(nome)) return v;
                    string suo = null;
                    try { suo = v.Name; } catch { }
                    if (string.Equals(suo, nome, StringComparison.OrdinalIgnoreCase)) return v;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
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
