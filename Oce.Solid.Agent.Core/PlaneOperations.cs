using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// UN PIANO PER TRE PUNTI. (voce GP9)
    ///
    /// `create_plane` sa fare solo il piano SFALSATO da uno dei tre standard:
    /// serve a tutto cio' che e' allineato agli assi, e a niente altro. Un foro
    /// inclinato, una faccia obliqua, una staffa a 30 gradi non hanno nessun
    /// piano su cui disegnare.
    ///
    /// ── PERCHE' TRE COORDINATE E NON TRE VERTICI ──────────────────────
    ///
    ///   IModelDoc2.CreatePlaneThru3Points3(AutoSize)  -> tre VERTICI selezionati
    ///   IModelDoc2.CreatePlaneFixed2(P1,P2,P3,UseGlobal) -> tre COORDINATE
    ///
    /// Si usa la seconda, e non e' una preferenza: **lo stato del documento non
    /// elenca i vertici**. Facce e spigoli hanno un identificativo, i vertici
    /// no — quindi «tre vertici selezionati» e' una cosa che il modello
    /// linguistico non ha modo di chiedere, mentre tre terne di numeri si'.
    ///
    /// ── LA NORMALE STA NELLA TERZA RIGA, E SI SAREBBE POTUTO SBAGLIARE ──
    ///
    /// La normale del piano si legge da `IRefPlane.Transform.ArrayData`, che ha
    /// sedici numeri: i primi nove sono la rotazione. **La normale e' la terza
    /// RIGA** (indici 6,7,8), non la terza colonna. Misurato il 16/9/2026 su
    /// tre piani di normale nota:
    ///
    ///   punti                                normale attesa      terza riga
    ///   (0,0,20) (100,0,20) (0,100,20)       (0, 0, 1)           (0, 0, 1)
    ///   (0,0,0)  (100,0,0)  (0,0,100)        (0, -1, 0)          (0, -1, 0)
    ///   (0,0,0)  (100,0,0)  (0,100,100)      (0, -.7071, .7071)  (0, -.7071, .7071)
    ///
    /// Sui primi due la terza riga e la terza colonna **danno lo stesso
    /// risultato**, e una sonda fatta solo con quelli avrebbe scelto a caso fra
    /// le due con una probabilita' su due di sbagliare per sempre. Sul terzo la
    /// colonna da' `(0, 0.7071, 0.7071)` — girata. *Un caso in cui due valori
    /// sbagliati danno lo stesso risultato non tabella niente*: il piano
    /// obliquo sta nella sonda apposta.
    ///
    /// ── `UseGlobal` DOES NOT MOVE THE NORMAL; IT TURNS THE PLANE'S X AXIS ──
    ///
    /// Tried `true` and `false` on the same three points: the same transform,
    /// number for number. That measure does not decide what UseGlobal does:
    /// all three point sets above have P1->P2 = (100, 0, 0), along the global
    /// X axis, and on those the two branches coincide by construction (audit
    /// finding L8-3). IModelDoc2~CreatePlaneFixed2.html, Remarks: «The x-axis
    /// of the planes' coordinate system are a vector from P1 to P2 or a vector
    /// projected from the x-axis of the global coordinate system onto the
    /// plane», and with true «This does not reorient the plane. Instead, it
    /// rotates the plane coordinate system about P1 until the x-axis of the
    /// plane aligns with the projected vector». So the NORMAL, the only thing
    /// read back here, is the same either way; the plane's own X axis is not.
    /// Nothing in the catalogue draws on a plane made here, so today the
    /// difference cannot be seen: `true` stays, unexposed, and the probe with
    /// P1->P2 along Y is due the day an operation uses the plane's own axes.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito PianoPerTrePunti(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var punti = new List<double[]>();
            foreach (var chiave in new[] { "point1_mm", "point2_mm", "point3_mm" })
            {
                var p = TernaMm(a, chiave);
                if (p == null)
                    return Fallito(
                        "«" + chiave + "» dev'essere tre numeri in millimetri, per esempio " +
                        "[0, 0, 20]. Servono tutti e tre i punti: un piano per due punti non " +
                        "e' uno, sono infiniti.");
                punti.Add(p);
            }

            // ── TRE PUNTI IN FILA NON FANNO UN PIANO ────────────────────
            //
            // SOLIDWORKS risponde null e non dice perche'. Qui si guarda prima,
            // perche' il perche' si sa: il prodotto vettoriale e' zero.
            var u = Meno(punti[1], punti[0]);
            var v = Meno(punti[2], punti[0]);
            var n = Vettoriale(u, v);
            double modulo = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            double lu = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
            double lv = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (lu < 1e-9 || lv < 1e-9)
                return Fallito("Due dei tre punti sono lo stesso punto: cosi' non fanno un piano.");
            if (modulo / (lu * lv) < 1e-6)
                return Fallito(string.Format(
                    CultureInfo.InvariantCulture,
                    "I tre punti sono in fila: ({0}), ({1}) e ({2}) stanno sulla stessa " +
                    "retta, e per una retta passano infiniti piani. Spostane uno fuori.",
                    Scrivi(punti[0]), Scrivi(punti[1]), Scrivi(punti[2])));

            var attesa = new[] { n[0] / modulo, n[1] / modulo, n[2] / modulo };

            int pianiPrima = ContaPiani(modello);
            var namesBefore = FeatureNamesNow(modello);
            object grezzo;
            try
            {
                modello.ClearSelection2(true);
                grezzo = modello.CreatePlaneFixed2(
                    punti[0].Select(Unita.M).ToArray(),
                    punti[1].Select(Unita.M).ToArray(),
                    punti[2].Select(Unita.M).ToArray(),
                    // UseGlobal: same normal either way; it turns only the
                    // plane's own X axis (see the note at the top, L8-3).
                    true);
            }
            catch (Exception ex) { return Fallito("Il piano non e' stato creato: " + ex.Message); }

            int pianiDopo = ContaPiani(modello);
            // «Non e' comparso nessun piano» si dice di due conteggi letti; con
            // uno illeggibile resta l'oggetto restituito (voce Z-C8).
            if ((pianiPrima >= 0 && pianiDopo >= 0 && pianiDopo == pianiPrima) ||
                ((pianiPrima < 0 || pianiDopo < 0) && grezzo == null))
                return Fallito(
                    "Il piano non e' stato creato, e nell'albero non ne e' comparso nessuno. " +
                    "SOLIDWORKS non dice perche'.");

            // THE PLANE IS WHAT THE CALL RETURNED, or else the ONE RefPlane born
            // since the call (FeatureBorn, scheme S7, Z-C21.5). Until then the
            // feature reported was the last one of the tree: on a sheet-metal
            // part the flat pattern, answered as the plane just made (finding
            // M9), while the normal came from the returned object.
            var feature = grezzo as IFeature;
            if (feature == null)
                feature = FeatureBorn(modello, namesBefore, "RefPlane", out _);
            var piano = grezzo as IRefPlane;
            if (piano == null && feature != null)
                try { piano = feature.GetSpecificFeature2() as IRefPlane; } catch { }

            // ── E ADESSO SI RILEGGE LA NORMALE ──────────────────────────
            //
            // Il conto a mano l'abbiamo gia' fatto qui sopra, e serviva
            // comunque per rifiutare i punti in fila. Confrontarlo con quello
            // che il piano dice di essere e' l'unica prova che il piano sia
            // QUELLO: «una feature in piu' nell'albero» lo direbbe anche di un
            // piano sbagliato.
            double[] letta = NormaleDelPiano(piano);
            var misure = new Dictionary<string, object>
            {
                ["points_mm"] = punti.Select(p => p.Select(x => Math.Round(x, 4)).ToArray()).ToArray(),
                ["normal_computed"] = attesa.Select(x => Math.Round(x, 6)).ToArray(),
                ["planes_before"] = NumeroOppureNull(pianiPrima),
                ["planes_now"] = NumeroOppureNull(pianiDopo),
            };

            if (letta == null)
            {
                misure["normal_reread"] = null;
                misure["warning"] =
                    "Il piano c'e' ma non sono riuscito a rileggerne l'orientamento, quindi " +
                    "non ti posso garantire che sia quello dei tre punti che mi hai dato.";
            }
            else
            {
                // Il verso puo' essere l'uno o l'altro: un piano non ha un
                // sopra e un sotto. Si confronta la DIREZIONE.
                double coseno = Math.Abs(
                    letta[0] * attesa[0] + letta[1] * attesa[1] + letta[2] * attesa[2]);
                if (coseno > 1) coseno = 1;
                double scarto = Math.Acos(coseno) * 180.0 / Math.PI;
                misure["normal_reread"] = letta.Select(x => Math.Round(x, 6)).ToArray();
                misure["deviation_deg"] = Math.Round(scarto, 4);
                if (scarto > 0.01)
                    return Fallito(string.Format(
                        CultureInfo.InvariantCulture,
                        "Il piano e' nato ma non e' quello che ho chiesto: dai tre punti la " +
                        "normale viene ({0}), e il piano dice ({1}) — {2:0.###} gradi di " +
                        "scarto. Non ti confermo un piano su cui disegneresti storto. " +
                        "Ritiralo — " + Frasi.CosiSiRitira + ".",
                        Scrivi(attesa), Scrivi(letta), scarto));
            }

            _log("[agente] piano per tre punti, normale (" + Scrivi(attesa) + ")");

            return feature != null
                ? Riuscito(feature, misure, attesa)
                : RiuscitoSenzaCreare(misure);
        }

        // =================================================================

        /// <summary>
        /// La normale di un piano di riferimento.
        ///
        /// `IRefPlane.Transform.ArrayData` ha sedici numeri; i primi nove sono
        /// la rotazione. **La normale e' la terza RIGA**, indici 6, 7 e 8 —
        /// misurato, e la terza colonna su un piano obliquo da' un vettore
        /// girato. Vedi la nota in testa alla classe.
        ///
        /// One reading for the whole library since Z-C21.5:
        /// Riferimenti.PlaneFrame, which the base planes are recognised by too
        /// (scheme S19).
        /// </summary>
        private static double[] NormaleDelPiano(IRefPlane piano)
        {
            double[] normal, origin;
            return Riferimenti.PlaneFrame(piano, out normal, out origin) ? normal : null;
        }

        private static double[] TernaMm(Dictionary<string, object> a, string chiave)
        {
            object v;
            if (a == null || !a.TryGetValue(chiave, out v) || v == null) return null;
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is string) return null;
            var n = new List<double>();
            foreach (var x in e)
            {
                try { n.Add(Convert.ToDouble(x, CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            return n.Count == 3 ? n.ToArray() : null;
        }

        private static double[] Meno(double[] a, double[] b)
        {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] Vettoriale(double[] u, double[] v)
        {
            return new[]
            {
                u[1] * v[2] - u[2] * v[1],
                u[2] * v[0] - u[0] * v[2],
                u[0] * v[1] - u[1] * v[0],
            };
        }

        private static string Scrivi(double[] v)
        {
            return string.Join(", ", v.Select(x =>
                x.ToString("0.####", CultureInfo.InvariantCulture)));
        }
    }
}
