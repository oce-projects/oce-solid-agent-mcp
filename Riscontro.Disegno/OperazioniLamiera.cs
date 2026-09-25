using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA LAMIERA (voce GP18): la flangia base e lo sviluppo per il taglio.
    ///
    /// ── LA MISURA CHE L'HA RESA COSTRUIBILE, 24/9/2026 ─────────────────────
    ///
    /// `Controlli/Lamiera`: una L da 50 + 30, profonda 40, T 2, R 2, K 0,5
    /// riletti dalla lamiera. Lo sviluppo esce 80,712 mm, e il conto a mano con
    /// lo schizzo sulle facce INTERNE da' 80,712 — scarto zero; con le facce
    /// esterne 76,712. Volume piegato e sviluppato uguali. **La convenzione e'
    /// misurata, non letta**: i punti dati qui sono le facce interne, e lo
    /// spessore va fuori.
    ///
    /// ── TRE TRAPPOLE, tutte senza errore ──────────────────────────────────
    ///
    ///  1. `SetBendState(sviluppata)` risponde 0 e `GetBendState` dice
    ///     «sviluppata», e il corpo resta PIEGATO. Lo sviluppo si legge togliendo
    ///     la soppressione alla lavorazione «FlatPattern» in fondo all'albero, e
    ///     rimettendola.
    ///  2. `ExportFlatPatternView` su un pezzo MAI SALVATO apre «Salva con nome»
    ///     e la chiamata COM non torna. `sviluppo_dxf` rifiuta un pezzo senza
    ///     file, prima di chiamare.
    ///  3. Lo schizzo si disegna con `AddToDB` a vero: le relazioni che
    ///     SOLIDWORKS indovina da solo sovravincolano alla prima modifica
    ///     (misurato con `Controlli/SeguiIlBordo`).
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito FlangiaBase(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string errore;
            double spessore, raggio, profondita;
            if (!NumObbligatorio(a, "spessore_mm", out spessore, out errore)) return Fallito(errore);
            if (!NumObbligatorio(a, "raggio_mm", out raggio, out errore)) return Fallito(errore);
            if (!NumObbligatorio(a, "profondita_mm", out profondita, out errore)) return Fallito(errore);
            if (spessore <= 0 || profondita <= 0)
                return Fallito("«spessore_mm» e «profondita_mm» devono essere positivi.");
            if (raggio < 0)
                return Fallito("«raggio_mm» non puo' essere negativo: e' il raggio INTERNO della piega.");

            var punti = PuntiDelProfilo(a, out errore);
            if (punti == null) return Fallito(errore);

            // UN TRATTO PIU' CORTO DEL RAGGIO NON SI PIEGA: la piega mangia R da
            // ogni lato interno. Il limite si dichiara prima di chiamare, perche'
            // dopo SOLIDWORKS darebbe una lavorazione rossa senza dire quale lato.
            for (int i = 0; i < punti.Count - 1; i++)
            {
                double l = Math.Sqrt(Math.Pow(punti[i + 1][0] - punti[i][0], 2) +
                                     Math.Pow(punti[i + 1][1] - punti[i][1], 2));
                if (l <= 0.001)
                    return Fallito("Il tratto " + (i + 1) + " e' lungo zero: due punti uguali di fila.");
                bool ha2Pieghe = i > 0 && i < punti.Count - 2;
                double serve = ha2Pieghe ? 2 * raggio : raggio;
                if (punti.Count > 2 && l <= serve)
                    return Fallito(string.Format(CultureInfo.InvariantCulture,
                        "Il tratto {0} e' lungo {1:0.###} mm e le pieghe col raggio {2:0.###} ne " +
                        "consumano {3:0.###}: non resta niente di dritto. Allunga il tratto o " +
                        "stringi il raggio.", i + 1, l, raggio, serve));
            }

            string piano = Str(a, "piano", "frontale");
            if (!SelezionaPiano(modello, piano))
                return Fallito("Non riesco a selezionare il piano «" + piano + "».");

            double volumePrima = VolumeMm3(modello);
            modello.SketchManager.InsertSketch(true);
            bool addPrima = modello.SketchManager.AddToDB;
            try
            {
                modello.SketchManager.AddToDB = true;
                for (int i = 0; i < punti.Count - 1; i++)
                    modello.SketchManager.CreateLine(
                        Unita.M(punti[i][0]), Unita.M(punti[i][1]), 0,
                        Unita.M(punti[i + 1][0]), Unita.M(punti[i + 1][1]), 0);
            }
            finally { modello.SketchManager.AddToDB = addPrima; }
            modello.SketchManager.InsertSketch(true);

            IFeature flangia;
            try
            {
                flangia = modello.FeatureManager.InsertSheetMetalBaseFlange2(
                    Unita.M(spessore), false, Unita.M(raggio), Unita.M(profondita), 0.0, false,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind, 0,
                    null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular,
                    0.0, 0.0, 0.5, true, false, false, false) as IFeature;
            }
            catch (Exception ex) { return Fallito("La flangia base non e' nata: " + ex.Message + LasciatoSchizzo); }
            if (flangia == null)
                return Fallito("La flangia base non e' nata, e SOLIDWORKS non dice perche'." + LasciatoSchizzo);
            modello.EditRebuild3();

            // ── T, R, K RILETTI dalla lamiera, non ripetuti dagli argomenti ──
            var dati = DatiDellaLamiera(modello);
            if (dati == null)
                return Fallito("La flangia c'e' ma non trovo la lavorazione «SheetMetal»: non so " +
                               "con che spessore e che fattore K e' nata. Ritirala — " + Frasi.CosiSiRitira + ".");
            double t = Unita.Mm(dati.Thickness), r = Unita.Mm(dati.BendRadius), k = dati.KFactor;
            if (Math.Abs(t - spessore) > 0.001 || Math.Abs(r - raggio) > 0.001)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "Ho chiesto spessore {0} e raggio {1}, e la lamiera dice {2:0.###} e {3:0.###}. " +
                    "Ritirala — {4}.", spessore, raggio, t, r, Frasi.CosiSiRitira));

            double volumePiegato = VolumeMm3(modello);
            double sviluppo = LunghezzaSviluppata(modello, out double volumeSviluppato);

            // IL CONTO A MANO, nelle due convenzioni. Misurato: con la L data
            // dall'alto verso destra lo spessore va a DESTRA del verso di
            // percorrenza e i punti sono le facce interne. Dato al contrario,
            // lo spessore va dall'altra parte e i punti diventano le facce
            // ESTERNE — lo sviluppo cambia di 2T per piega. Non si indovina: si
            // calcolano tutte e due e si dice quale combacia con SOLIDWORKS.
            double somma = 0;
            for (int i = 0; i < punti.Count - 1; i++)
                somma += Math.Sqrt(Math.Pow(punti[i + 1][0] - punti[i][0], 2) +
                                   Math.Pow(punti[i + 1][1] - punti[i][1], 2));
            int pieghe = punti.Count - 2;
            double angoli = 0;
            for (int i = 1; i < punti.Count - 1; i++) angoli += AngoloDiPiega(punti[i - 1], punti[i], punti[i + 1]);
            double interne = somma - 2 * r * pieghe + angoli * (r + k * t);
            double esterne = somma - 2 * (r + t) * pieghe + angoli * (r + k * t);
            string convenzione = double.IsNaN(sviluppo) ? "non misurata"
                : Math.Abs(sviluppo - interne) <= 0.5 ? "interne"
                : Math.Abs(sviluppo - esterne) <= 0.5 ? "esterne" : "nessuna delle due";
            double atteso = convenzione == "esterne" ? esterne : interne;

            var misure = new Dictionary<string, object>
            {
                ["spessore_mm"] = Math.Round(t, 3),
                ["raggio_mm"] = Math.Round(r, 3),
                ["fattore_k"] = Math.Round(k, 4),
                ["pieghe"] = pieghe,
                ["volume_mm3"] = Math.Round(volumePiegato - volumePrima, 2),
                ["lunghezza_sviluppo_mm"] = double.IsNaN(sviluppo) ? null : (object)Math.Round(sviluppo, 3),
                ["lunghezza_sviluppo_a_mano_mm"] = Math.Round(atteso, 3),
                ["i_punti_sono_le_facce"] = convenzione,
            };

            // IL CONTROLLO CHE DA' SENSO ALLA FAMIGLIA: lo sviluppo di SOLIDWORKS
            // contro il conto a mano. Se divergono, il DXF che andra' al laser
            // non e' quello che uno si aspetta, e lo si dice adesso.
            if (convenzione == "nessuna delle due")
                misure["attenzione"] = string.Format(CultureInfo.InvariantCulture,
                    "Lo sviluppo di SOLIDWORKS e' {0:0.###} mm e il conto a mano ne da' {1:0.###}: " +
                    "prima di mandare il DXF al taglio, controlla la tabella delle pieghe del " +
                    "modello di documento.", sviluppo, atteso);
            if (!double.IsNaN(volumeSviluppato) && Math.Abs(volumeSviluppato - volumePiegato) > 0.005 * volumePiegato)
                misure["attenzione_volume"] = "Il volume sviluppato non e' quello piegato: lo sviluppo non e' affidabile.";

            return Riuscito(flangia, misure);
        }

        private Esito SviluppoDxf(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return Fallito("Lo sviluppo si fa su una parte.");
            if (DatiDellaLamiera(modello) == null)
                return Fallito("Questo pezzo non e' di lamiera: non ha una lavorazione «SheetMetal» da sviluppare.");

            // TRAPPOLA 2 della nota in testa: senza file, «Salva con nome» e una
            // chiamata che non torna.
            string suo = modello.GetPathName() ?? "";
            if (string.IsNullOrWhiteSpace(suo))
                return Fallito("Il pezzo non e' mai stato salvato, e su un pezzo senza file " +
                               "SOLIDWORKS apre «Salva con nome» e resta ad aspettare. Salvalo " +
                               "prima («salva_documento»).");

            string dove = Str(a, "percorso", null);
            if (string.IsNullOrWhiteSpace(dove)) dove = Path.ChangeExtension(suo, ".dxf");
            if (!string.Equals(Path.GetExtension(dove), ".dxf", StringComparison.OrdinalIgnoreCase))
                return Fallito("«percorso» deve finire in .dxf.");
            try { if (File.Exists(dove)) File.Delete(dove); }
            catch (Exception ex) { return Fallito("C'e' gia' un DXF li' e non riesco a sostituirlo: " + ex.Message); }

            bool ok;
            try
            {
                ok = parte.ExportFlatPatternView(dove,
                    (int)swExportFlatPatternViewOptions_e.swExportFlatPatternOption_RemoveBends);
            }
            catch (Exception ex) { return Fallito("Lo sviluppo non e' uscito: " + ex.Message); }
            if (!ok || !File.Exists(dove) || new FileInfo(dove).Length == 0)
                return Fallito("SOLIDWORKS dice di aver esportato lo sviluppo, ma il file «" + dove +
                               "» " + (File.Exists(dove) ? "e' vuoto." : "non c'e'."));

            // SI RILEGGE IL DXF: l'ingombro scritto nel file contro lo sviluppo
            // misurato sul pezzo. Un DXF di un'altra cosa si apre uguale.
            double sviluppo = LunghezzaSviluppata(modello, out double _);
            var ingombro = IngombroDelDxf(dove);
            var misure = new Dictionary<string, object>
            {
                ["percorso"] = dove,
                ["byte"] = new FileInfo(dove).Length,
                ["ingombro_dxf_mm"] = ingombro,
                ["lunghezza_sviluppo_mm"] = double.IsNaN(sviluppo) ? null : (object)Math.Round(sviluppo, 3),
            };
            if (ingombro != null && !double.IsNaN(sviluppo))
            {
                double lato = Math.Max(ingombro[0], ingombro[1]);
                if (Math.Abs(lato - sviluppo) > 0.5)
                    return Fallito(string.Format(CultureInfo.InvariantCulture,
                        "Il DXF e' uscito ({0}) ma il suo lato lungo e' {1:0.###} mm e lo sviluppo " +
                        "del pezzo {2:0.###}: non e' il disegno di questo pezzo, o non e' sviluppato.",
                        dove, lato, sviluppo));
            }
            return SenzaFeature(misure);
        }

        // =================================================================

        /// <summary>
        /// Lo spessore, il raggio e il fattore K della lamiera del pezzo, dalla
        /// lavorazione «SheetMetal». Null se il pezzo non e' di lamiera.
        /// </summary>
        private static ISheetMetalFeatureData DatiDellaLamiera(IModelDoc2 modello)
        {
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == "SheetMetal") return f.GetDefinition() as ISheetMetalFeatureData;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Il lato lungo del pezzo SVILUPPATO, in mm, e il suo volume. NaN se non
        /// si riesce. Si toglie la soppressione alla lavorazione «FlatPattern»,
        /// si misura, e la si rimette — `SetBendState` non fa niente (nota in
        /// testa, trappola 1).
        /// </summary>
        private static double LunghezzaSviluppata(IModelDoc2 modello, out double volume)
        {
            volume = double.NaN;
            IFeature sviluppo = null;
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null && sviluppo == null)
                {
                    if (f.GetTypeName2() == "FlatPattern") sviluppo = f;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            if (sviluppo == null) return double.NaN;
            try
            {
                sviluppo.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                modello.EditRebuild3();
                volume = VolumeMm3(modello);
                var corpi = (modello as IPartDoc)?.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                var box = corpi == null || corpi.Length == 0 ? null : (corpi[0] as IBody2)?.GetBodyBox() as double[];
                if (box == null) return double.NaN;
                return Math.Max(Math.Abs(box[3] - box[0]), Math.Max(Math.Abs(box[4] - box[1]), Math.Abs(box[5] - box[2]))) * 1000.0;
            }
            catch { return double.NaN; }
            finally
            {
                try
                {
                    sviluppo.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                        (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    modello.EditRebuild3();
                }
                catch { }
            }
        }

        /// <summary>
        /// L'ingombro in pianta del DXF, in mm, calcolato dalle ENTITA' — non
        /// dall'intestazione. Null se il file non ha entita' leggibili.
        ///
        /// MISURATO il 25/9/2026, alla prima prova: `$EXTMIN/$EXTMAX`
        /// dell'intestazione dicevano 279,4 mm per uno sviluppo di 80,712 —
        /// 279,4 sono 11 pollici, il foglio del modello DXF, non il pezzo. Un
        /// controllo costruito sull'intestazione avrebbe rifiutato ogni DXF
        /// giusto, o peggio accettato quelli sbagliati del formato giusto.
        /// Qui si leggono le coppie (codice, valore) della sezione ENTITIES:
        /// punti 10/20 e 11/21, e per archi e cerchi il centro piu' il raggio.
        /// </summary>
        private static double[] IngombroDelDxf(string percorso)
        {
            try
            {
                var r = File.ReadAllLines(percorso);
                int inizio = -1;
                for (int i = 0; i + 1 < r.Length; i++)
                    if (r[i].Trim() == "2" && r[i + 1].Trim() == "ENTITIES") { inizio = i + 2; break; }
                if (inizio < 0) return null;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                double cx = double.NaN, cy = double.NaN;
                bool arco = false;
                for (int i = inizio; i + 1 < r.Length; i += 2)
                {
                    string codice = r[i].Trim(), valore = r[i + 1].Trim();
                    if (codice == "0")
                    {
                        if (valore == "ENDSEC") break;
                        arco = valore == "ARC" || valore == "CIRCLE";
                        cx = cy = double.NaN;
                        continue;
                    }
                    double v;
                    if (!double.TryParse(valore, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                    if (codice == "10" || codice == "11") { if (arco) cx = v; else { minX = Math.Min(minX, v); maxX = Math.Max(maxX, v); } }
                    else if (codice == "20" || codice == "21") { if (arco) cy = v; else { minY = Math.Min(minY, v); maxY = Math.Max(maxY, v); } }
                    else if (codice == "40" && arco && !double.IsNaN(cx) && !double.IsNaN(cy))
                    {
                        minX = Math.Min(minX, cx - v); maxX = Math.Max(maxX, cx + v);
                        minY = Math.Min(minY, cy - v); maxY = Math.Max(maxY, cy + v);
                    }
                }
                if (minX == double.MaxValue || minY == double.MaxValue) return null;
                return new[] { Math.Round(maxX - minX, 3), Math.Round(maxY - minY, 3) };
            }
            catch { return null; }
        }

        private static List<double[]> PuntiDelProfilo(Dictionary<string, object> a, out string errore)
        {
            errore = null;
            const string comeSiScrive =
                "«punti» vuole almeno due punti [x, y] in mm sul piano: il profilo APERTO " +
                "della lamiera. Una L: [[0,30],[0,0],[50,0]].";
            // UN ELENCO E' UN IEnumerable, non un `object[]`. Misurato al primo
            // giro di banco (25/9/2026): gli argomenti arrivano come elenchi del
            // serializzatore di chi chiama, e un `as object[]` dava null su una L
            // scritta giusta — rifiutata con «vuole almeno due punti». E' la
            // stessa regola di `HaElenco`, che c'era gia'.
            object grezzo;
            var elenco = a != null && a.TryGetValue("punti", out grezzo) && !(grezzo is string)
                ? grezzo as System.Collections.IEnumerable : null;
            if (elenco == null) { errore = comeSiScrive; return null; }
            var fuori = new List<double[]>();
            foreach (var o in elenco)
            {
                var coppia = new List<double>();
                var p = o is string ? null : o as System.Collections.IEnumerable;
                if (p != null)
                    foreach (var c in p) coppia.Add(Convert.ToDouble(c, CultureInfo.InvariantCulture));
                if (coppia.Count != 2)
                {
                    errore = "Ogni punto e' una coppia [x, y] in mm.";
                    return null;
                }
                fuori.Add(coppia.ToArray());
            }
            if (fuori.Count < 2) { errore = comeSiScrive; return null; }
            return fuori;
        }

        /// <summary>L'angolo di piega al punto b, in radianti (0 = dritto).</summary>
        private static double AngoloDiPiega(double[] a, double[] b, double[] c)
        {
            double ux = b[0] - a[0], uy = b[1] - a[1], vx = c[0] - b[0], vy = c[1] - b[1];
            double cos = (ux * vx + uy * vy) / (Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy));
            return Math.Acos(Math.Max(-1, Math.Min(1, cos)));
        }
    }
}
