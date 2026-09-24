using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// UNA VISTA DI DETTAGLIO SU UNA TAVOLA (voce GT4, seconda meta').
    ///
    /// ── PERCHE' ESISTE ──────────────────────────────────────────────────
    ///
    /// L'altra meta' di cio' che manca a una tavola d'officina vera. La
    /// sezione fa vedere cosa c'e' dentro; il dettaglio fa vedere cio' che e'
    /// **troppo piccolo per leggersi alla scala del disegno** — una gola, uno
    /// smusso, un raccordo con la sua quota.
    ///
    /// ── «PER ANALOGIA» NON E' BASTATO, E ERA SCRITTO ────────────────────
    ///
    /// La misura 0.6 aveva chiuso la sezione e lasciato fuori il dettaglio con
    /// una nota onesta: *«per analogia dovrebbe funzionare uguale — ma "per
    /// analogia" e' proprio il salto che qui e' gia' costato una correzione»*.
    ///
    /// Quella prudenza si e' guadagnata la paga due volte nello stesso
    /// pomeriggio. Sulla sezione, l'analogia col contatore del tratteggio era
    /// **sbagliata**. E qui, delle tre cose che si sarebbero date per scontate,
    /// due erano diverse:
    ///
    ///   - `Showtype` non vuole l'enum delle viste di dettaglio: vuole
    ///     `swDetCircleShowType_e`. L'altro nome non esiste in questa interop.
    ///   - `GetDetailCircleCount2(out int Size)` ha due uscite, e **quella
    ///     giusta e' il valore di ritorno**. Misurato il 23/9/2026:
    ///
    ///         circonferenze PRIMA: ritorno=0  out=1
    ///         circonferenze DOPO : ritorno=1  out=35
    ///
    ///     L'`out` e' la misura di un array, e passa da 1 a 35 per UNA
    ///     circonferenza. Chi lo leggesse come un conto direbbe che la vista
    ///     ne aveva gia' una prima di averne.
    ///
    /// Una cosa invece era davvero uguale: il cerchio e' un `ISketchSegment`
    /// come la linea, e `Select4(false, null)` basta.
    ///
    /// ── IL CONTROLLO NON E' IL TRATTEGGIO ───────────────────────────────
    ///
    /// Sulla sezione la campitura dice che il taglio ha incontrato materiale.
    /// Un dettaglio non taglia niente: mostra la stessa geometria ingrandita, e
    /// infatti `GetFaceHatchCount` sul dettaglio risponde **0**. Copiare il
    /// controllo della sezione avrebbe prodotto un'operazione che rifiuta
    /// sempre — che e' esattamente il difetto che la sezione ha avuto al primo
    /// uso, ripetuto per analogia.
    ///
    /// Qui la prova che il cerchio e' **diventato** un dettaglio e' che la
    /// vista di partenza ha una circonferenza di dettaglio in piu': 0 -> 1.
    /// Senza, sul foglio resterebbe un cerchio disegnato a mano.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito DettaglioInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: una vista di dettaglio sta " +
                    "su un foglio di tavola. Crea prima la tavola con «crea_disegno».");

            // ── LA VISTA DA DETTAGLIARE ─────────────────────────────────
            //
            // La PRIMA «vista» del foglio e' il foglio stesso, e si salta.
            string chiesta = Str(a, "vista", null);
            IView bersaglio = null;
            var nomiViste = new List<string>();
            int vistePrima = 0;
            try
            {
                var v = disegno.GetFirstView() as IView;
                while (v != null && vistePrima < 200)
                {
                    vistePrima++;
                    if (v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    {
                        string nome = "";
                        try { nome = v.GetName2() ?? ""; } catch { }
                        nomiViste.Add(nome);
                        if (bersaglio == null &&
                            (chiesta == null ||
                             string.Equals(nome, chiesta, StringComparison.OrdinalIgnoreCase)))
                            bersaglio = v;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch (Exception ex)
            {
                return Fallito("Non riesco a scorrere le viste del foglio: " + ex.Message);
            }

            if (bersaglio == null)
                return Fallito(chiesta == null
                    ? "Sul foglio non c'e' nessuna vista da dettagliare: mettici prima " +
                      "le viste con «viste_standard»."
                    : "Non trovo la vista «" + chiesta + "». Sul foglio ci sono: " +
                      (nomiViste.Count == 0 ? "nessuna vista" : string.Join(", ", nomiViste)) + ".");

            string nomeBersaglio = "";
            try { nomeBersaglio = bersaglio.GetName2() ?? ""; } catch { }

            var sagoma = bersaglio.GetOutline() as double[];
            if (sagoma == null || sagoma.Length < 4)
                return Fallito(
                    "Non riesco a leggere l'ingombro della vista «" + nomeBersaglio +
                    "», e senza quello non so dove mettere il cerchio del dettaglio.");

            double xMin = sagoma[0], yMin = sagoma[1], xMax = sagoma[2], yMax = sagoma[3];
            double largo = xMax - xMin, alto = yMax - yMin;

            // ── DOVE, E QUANTO GRANDE ───────────────────────────────────
            //
            // In millimetri di FOGLIO, come per la sezione: sul foglio la vista
            // e' in scala, e mescolare le due misure e' il modo piu' rapido di
            // ingrandire il posto sbagliato. Di serie al centro, raggio un
            // quarto del lato minore.
            double xM = Num(a, "x_mm", double.NaN);
            double yM = Num(a, "y_mm", double.NaN);
            double cx = double.IsNaN(xM) ? (xMin + xMax) / 2.0 : (xMin + xMax) / 2.0 + xM / 1000.0;
            double cy = double.IsNaN(yM) ? (yMin + yMax) / 2.0 : (yMin + yMax) / 2.0 + yM / 1000.0;

            double raggioMm = Num(a, "raggio_mm", Math.Min(largo, alto) / 4.0 * 1000.0);
            if (raggioMm <= 0)
                return Fallito("«raggio_mm» dev'essere maggiore di zero: ho letto " + raggioMm + ".");
            double raggio = raggioMm / 1000.0;

            // IL CERCHIO DEVE STARE DENTRO LA VISTA. Uno che sborda ingrandisce
            // del vuoto, e il vuoto ingrandito e' comunque una vista valida.
            if (cx - raggio < xMin || cx + raggio > xMax ||
                cy - raggio < yMin || cy + raggio > yMax)
                return Fallito(
                    "Il cerchio del dettaglio non sta dentro la vista «" + nomeBersaglio +
                    "»: centro (" + Math.Round((cx - (xMin + xMax) / 2.0) * 1000, 1) + ", " +
                    Math.Round((cy - (yMin + yMax) / 2.0) * 1000, 1) + ") mm dal centro, raggio " +
                    Math.Round(raggioMm, 1) + " mm, su una vista " +
                    Math.Round(largo * 1000, 1) + " x " + Math.Round(alto * 1000, 1) +
                    " mm di foglio. Un cerchio che sborda ingrandisce del vuoto, e il " +
                    "vuoto ingrandito e' comunque una vista che sembra buona.");

            double scala1 = Num(a, "scala", 2);
            if (scala1 <= 0)
                return Fallito("«scala» dev'essere maggiore di zero: ho letto " + scala1 + ".");
            string etichetta = Str(a, "etichetta", "B");
            if (string.IsNullOrWhiteSpace(etichetta)) etichetta = "B";

            // Quante circonferenze aveva PRIMA. Vedi il docstring: il conto e'
            // il valore di RITORNO, non l'`out`.
            int cerchiPrima = -1;
            try { int quanti; cerchiPrima = bersaglio.GetDetailCircleCount2(out quanti); }
            catch { }

            // ── IL CERCHIO, SULLO SCHIZZO DEL FOGLIO ────────────────────
            try { disegno.EditSheet(); } catch { }

            ISketchSegment cerchio = null;
            try
            {
                modello.ClearSelection2(true);
                cerchio = modello.SketchManager.CreateCircleByRadius(cx, cy, 0.0, raggio)
                          as ISketchSegment;
            }
            catch (Exception ex)
            {
                return Fallito("Non sono riuscito a tracciare il cerchio del dettaglio: " + ex.Message);
            }
            if (cerchio == null)
                return Fallito(
                    "«CreateCircleByRadius» non ha prodotto nessun cerchio sul foglio, e " +
                    "senza il cerchio non c'e' niente da selezionare: " +
                    "«CreateDetailViewAt4» non prende la zona in firma, la prende dalla " +
                    "selezione.");

            // Lo schizzo si chiude prima, come per la sezione.
            try { modello.SketchManager.InsertSketch(true); } catch { }

            bool presa = false;
            try
            {
                modello.ClearSelection2(true);
                presa = cerchio.Select4(false, null);
            }
            catch (Exception ex)
            {
                return Fallito("Non sono riuscito a selezionare il cerchio: " + ex.Message);
            }
            if (!presa)
                return Fallito(
                    "Il cerchio c'e' ma non si lascia selezionare, e «CreateDetailViewAt4» " +
                    "prende la zona dalla selezione: senza, farebbe qualcosa di diverso da " +
                    "quello che hai chiesto.");

            // ── IL DETTAGLIO, A FIANCO ──────────────────────────────────
            double xDettaglio = xMax + largo * 0.8;
            double yDettaglio = (yMin + yMax) / 2.0;

            object nata = null;
            string esplosa = null;
            try
            {
                nata = disegno.CreateDetailViewAt4(
                    xDettaglio, yDettaglio, 0.0,
                    (int)swDetViewStyle_e.swDetViewSTANDARD, scala1, 1.0, etichetta,
                    (int)swDetCircleShowType_e.swDetCircleCIRCLE,
                    false, false, false, 0);
            }
            catch (Exception ex) { esplosa = ex.Message; }

            int visteDopo = 0;
            IView ultima = null;
            try
            {
                var w = disegno.GetFirstView() as IView;
                while (w != null && visteDopo < 200)
                {
                    visteDopo++;
                    ultima = w;
                    w = w.GetNextView() as IView;
                }
            }
            catch { }

            if (visteDopo <= vistePrima)
                return Fallito(
                    "Nessuna vista in piu': il dettaglio NON e' nato (erano " + vistePrima +
                    ", sono " + visteDopo + "). «CreateDetailViewAt4» " +
                    (esplosa != null ? "e' andata in errore: " + esplosa
                                     : nata == null ? "non ha risposto un oggetto"
                                                    : "ha risposto un oggetto") + ".");

            string nomeDettaglio = "";
            int tipoDettaglio = -1;
            try { nomeDettaglio = ultima.GetName2() ?? ""; } catch { }
            try { tipoDettaglio = ultima.Type; } catch { }

            int cerchiDopo = -1;
            try { int quanti; cerchiDopo = bersaglio.GetDetailCircleCount2(out quanti); }
            catch { }

            // ── LA PROVA CHE IL CERCHIO E' DIVENTATO UN DETTAGLIO ───────
            //
            // Non il tratteggio: un dettaglio non taglia niente e la campitura
            // li' e' zero (misurato). La prova e' che la vista di PARTENZA ha
            // una circonferenza di dettaglio in piu'. Senza, sul foglio
            // resterebbe un cerchio disegnato a mano accanto a una vista in
            // piu' che non gli e' legata.
            if (cerchiPrima >= 0 && cerchiDopo >= 0 && cerchiDopo <= cerchiPrima)
                return Fallito(
                    "La vista «" + nomeDettaglio + "» e' nata, ma su «" + nomeBersaglio +
                    "» le circonferenze di dettaglio sono ancora " + cerchiDopo +
                    ": il cerchio non e' diventato un dettaglio, e sul foglio resta " +
                    "disegnato a mano accanto a una vista che non gli e' legata.");

            var fuori = new Dictionary<string, object>
            {
                ["vista_dettagliata"] = nomeBersaglio,
                ["viste_prima"] = vistePrima,
                ["viste_dopo"] = visteDopo,
                ["vista_dettaglio"] = nomeDettaglio,
                ["tipo_vista"] = tipoDettaglio,
                ["e_un_dettaglio"] =
                    tipoDettaglio == (int)swDrawingViewTypes_e.swDrawingDetailView,
                ["etichetta"] = etichetta,
                ["scala"] = scala1,
                ["cerchio_mm"] = new object[]
                {
                    Math.Round((cx - (xMin + xMax) / 2.0) * 1000, 2),
                    Math.Round((cy - (yMin + yMax) / 2.0) * 1000, 2),
                    Math.Round(raggioMm, 2),
                },
                ["circonferenze_prima"] = cerchiPrima,
                ["circonferenze_dopo"] = cerchiDopo,
                ["cosa_non_guarda"] =
                    "SE QUELLO E' IL PARTICOLARE CHE SERVIVA. So dirti che il cerchio e' " +
                    "diventato un dettaglio e che sta dentro la vista, non che dentro ci " +
                    "sia la gola che volevi mostrare: un cerchio spostato di dieci " +
                    "millimetri ingrandisce un pezzo di piano liscio, ed e' altrettanto " +
                    "valido.\n" +
                    "Non guardo se la scala scelta rende il particolare leggibile, ne' se " +
                    "il dettaglio si accavalla con le altre viste o col cartiglio. E non " +
                    "decido se un dettaglio serva: quello dipende da quanto e' piccolo " +
                    "cio' che va quotato.",
            };

            _log("[agente] dettaglio «" + nomeDettaglio + "» da «" + nomeBersaglio +
                 "»: circonferenze " + cerchiPrima + " -> " + cerchiDopo +
                 ", viste " + vistePrima + " -> " + visteDopo);
            return SenzaFeature(fuori);
        }
    }
}
