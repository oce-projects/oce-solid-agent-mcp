using System;
using System.Collections.Generic;
using System.Globalization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>I tre esiti possibili di un controllo sulle posizioni.</summary>
    public enum PositionsOutcome
    {
        /// <summary>Guardate e giuste.</summary>
        Verified,

        /// <summary>Non e' stato possibile guardarle. NON vuol dire che vanno bene.</summary>
        NotVerifiable,

        /// <summary>Guardate, e almeno una e' fuori tolleranza.</summary>
        Wrong
    }

    /// <summary>
    /// Il giudizio sulle posizioni dei fori, separato da SOLIDWORKS apposta.
    ///
    /// PERCHE' ESISTE QUESTA CLASSE: IL SESTO DIFETTO CHE NON DAVA ERRORE
    ///
    /// Fino alla 0.25.1 il controllo era una funzione sola, `ForiMancanti`, che
    /// tornava `null` per dire «tutto a posto». Era onesta: tornava `null`
    /// anche quando non aveva potuto guardare niente, e uno dei suoi `return
    /// null` aveva perfino il commento
    ///
    ///     if (diameterMm &lt;= 0) return null;   // niente da confrontare:
    ///                                          // non si inventa un verdetto
    ///
    /// Le strade in cui si asteneva erano quattro: diametro ignoto, `GetFaces()`
    /// nullo, l'eccezione, e — la piu' frequente — NESSUNA FACCIA CILINDRICA
    /// del diametro atteso, cioe' esattamente il caso in cui i fori potrebbero
    /// non esserci affatto.
    ///
    /// Chi la chiamava scriveva poi, senza condizione:
    ///
    ///     misure["positions_checked"] = true;
    ///
    /// LA FUNZIONE SI RIFIUTAVA DI INVENTARE IL VERDETTO, E IL CHIAMANTE LO
    /// INVENTAVA AL POSTO SUO.
    ///
    /// E il banco, che di quel campo si fidava per decidere il pass, lo leggeva
    /// con `misure.ContainsKey("positions_checked")`: una chiave scritta
    /// sempre. Il ramo che stampava «POSIZIONI NON VERIFICATE» era codice morto
    /// — non poteva scattare. Un controllo mai visto scattare non e' un
    /// controllo, e questo lo era due volte: nel ponte e nel banco.
    ///
    /// LA RIPARAZIONE ha due meta'.
    ///
    ///  1. Tre esiti invece di due. «Non ho potuto guardare» e' una risposta
    ///     diversa da «ho guardato e va bene», e va detta. Non e' un rifiuto:
    ///     l'operazione e' riuscita, e' il controllo che non c'e' stato. Si
    ///     misura per SPIEGARE, non per rifiutare — e un rifiuto sbagliato
    ///     resta peggio di una spiegazione sbagliata.
    ///  2. Il giudizio sta QUI, senza SOLIDWORKS. Raccogliere le facce
    ///     cilindriche richiede il COM; decidere cosa farne no. Separati, il
    ///     secondo pezzo si puo' provare a tavolino — compreso il caso
    ///     «zero assi trovati», che con SOLIDWORKS acceso non si sa come
    ///     costringere a capitare. `Checks\PositionsTest` lo fa.
    ///
    /// La chiave nell'esito e' DIVERSA, non `false`: `positions_not_checked`
    /// col motivo dentro. Un `positions_checked = false` sarebbe stato letto
    /// come «verificate» da qualunque chiamante che guarda se la chiave c'e' —
    /// che e' precisamente l'errore che si sta riparando.
    /// </summary>
    public static class PositionsVerdict
    {
        /// <summary>
        /// Quanto puo' distare un foro da dove e' stato chiesto, in millimetri.
        ///
        /// Non e' una tolleranza di lavorazione: e' il margine entro cui si
        /// riconosce che SOLIDWORKS ha messo il foro dove gli era stato detto.
        /// Uno scostamento piu' grande di cosi' non e' imprecisione, e' un'altra
        /// posizione.
        /// </summary>
        public const double ToleranceMm = 0.1;

        /// <summary>
        /// Giudica le posizioni dei fori confrontando gli assi dei cilindri
        /// trovati sulla lavorazione con i punti che erano stati chiesti.
        ///
        /// <param name="foundAxes">
        /// I `CylinderParams` delle facce cilindriche del diametro giusto: sette
        /// double ciascuno, i primi tre il punto sull'asse, i tre dopo la
        /// direzione, il settimo il raggio. Puo' essere null o vuoto.
        /// </param>
        /// <param name="cannot">
        /// Il motivo per cui il chiamante non ha potuto raccogliere gli assi, se
        /// non ha potuto. Se e' valorizzato, vince su tutto il resto: si esce
        /// con NotVerifiable e questo motivo.
        /// </param>
        /// <param name="axes">
        /// Quale coordinata del modello corrisponde alla prima e alla seconda
        /// del piano, e quale e' la normale: tre indici fra 0, 1 e 2.
        /// </param>
        /// <param name="offsetM">La quota lungo la normale, in metri.</param>
        /// <param name="positions">I punti chiesti, in millimetri, nel piano.</param>
        /// <param name="reason">
        /// Con Sbagliate, la frase da dare a chi legge. Con NotVerifiable,
        /// perche' non si e' potuto guardare. Con Verificate, null.
        /// </param>
        /// </summary>
        public static PositionsOutcome Judge(
            List<double[]> foundAxes,
            string cannot,
            double[] axes,
            double offsetM,
            List<double[]> positions,
            out string reason)
        {
            if (!string.IsNullOrEmpty(cannot))
            {
                reason = cannot;
                return PositionsOutcome.NotVerifiable;
            }

            if (positions == null || positions.Count == 0)
            {
                reason = "non e' stata chiesta nessuna posizione, non c'e' niente da confrontare";
                return PositionsOutcome.NotVerifiable;
            }

            // IL CASO CHE PRIMA PASSAVA PER BUONO.
            // Nessuna faccia cilindrica del diametro atteso sulla lavorazione:
            // non vuol dire che i fori sono al posto giusto, vuol dire che non
            // si sa nemmeno se ci sono.
            if (foundAxes == null || foundAxes.Count == 0)
            {
                reason = "sulla lavorazione non c'e' nessuna faccia cilindrica " +
                         "del diametro atteso: non so dire dove siano finiti i fori";
                return PositionsOutcome.NotVerifiable;
            }

            if (axes == null || axes.Length < 3)
            {
                reason = "non so su quale piano guardare";
                return PositionsOutcome.NotVerifiable;
            }

            double worse = 0;
            int far = -1;
            for (int i = 0; i < positions.Count; i++)
            {
                var expected = new double[3];
                expected[(int)axes[0]] = Units.M(positions[i][0]);
                expected[(int)axes[1]] = Units.M(positions[i][1]);
                expected[(int)axes[2]] = offsetM;

                double near = double.MaxValue;
                foreach (var par in foundAxes)
                    near = Math.Min(near, DistanceFromAxis(expected, par));
                if (near > worse) { worse = near; far = i; }
            }

            if (worse * 1000.0 > ToleranceMm)
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Il foro {0}, chiesto a ({1:0.##}, {2:0.##}), e' finito a {3:0.##} mm " +
                    "da li'. Non te lo lascio passare per buono: ritiralo — " +
                    Sentences.HowToWithdrawIt + ".",
                    far + 1, positions[far][0], positions[far][1], worse * 1000.0);
                return PositionsOutcome.Wrong;
            }

            reason = null;
            return PositionsOutcome.Verified;
        }

        /// <summary>Distanza fra un punto e la retta di un cilindro, in metri.</summary>
        public static double DistanceFromAxis(double[] point, double[] cylinder)
        {
            if (point == null || cylinder == null || point.Length < 3 || cylinder.Length < 6)
                return double.MaxValue;
            double vx = point[0] - cylinder[0];
            double vy = point[1] - cylinder[1];
            double vz = point[2] - cylinder[2];
            double ax = cylinder[3], ay = cylinder[4], az = cylinder[5];
            double standard = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (standard < 1e-12) return double.MaxValue;
            ax /= standard; ay /= standard; az /= standard;
            double along = vx * ax + vy * ay + vz * az;
            double dx = vx - along * ax, dy = vy - along * ay, dz = vz - along * az;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
