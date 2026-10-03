using System;
using System.Collections.Generic;
using System.Globalization;

namespace Riscontro.Disegno
{
    /// <summary>I tre esiti possibili di un controllo sulle posizioni.</summary>
    public enum EsitoPosizioni
    {
        /// <summary>Guardate e giuste.</summary>
        Verificate,

        /// <summary>Non e' stato possibile guardarle. NON vuol dire che vanno bene.</summary>
        NonVerificabile,

        /// <summary>Guardate, e almeno una e' fuori tolleranza.</summary>
        Sbagliate
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
    ///     if (diametroMm &lt;= 0) return null;   // niente da confrontare:
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
    ///     costringere a capitare. `Controlli\ProvaPosizioni` lo fa.
    ///
    /// La chiave nell'esito e' DIVERSA, non `false`: `positions_not_checked`
    /// col motivo dentro. Un `positions_checked = false` sarebbe stato letto
    /// come «verificate» da qualunque chiamante che guarda se la chiave c'e' —
    /// che e' precisamente l'errore che si sta riparando.
    /// </summary>
    public static class GiudizioPosizioni
    {
        /// <summary>
        /// Quanto puo' distare un foro da dove e' stato chiesto, in millimetri.
        ///
        /// Non e' una tolleranza di lavorazione: e' il margine entro cui si
        /// riconosce che SOLIDWORKS ha messo il foro dove gli era stato detto.
        /// Uno scostamento piu' grande di cosi' non e' imprecisione, e' un'altra
        /// posizione.
        /// </summary>
        public const double TolleranzaMm = 0.1;

        /// <summary>
        /// Giudica le posizioni dei fori confrontando gli assi dei cilindri
        /// trovati sulla lavorazione con i punti che erano stati chiesti.
        ///
        /// <param name="assiTrovati">
        /// I `CylinderParams` delle facce cilindriche del diametro giusto: sette
        /// double ciascuno, i primi tre il punto sull'asse, i tre dopo la
        /// direzione, il settimo il raggio. Puo' essere null o vuoto.
        /// </param>
        /// <param name="nonPosso">
        /// Il motivo per cui il chiamante non ha potuto raccogliere gli assi, se
        /// non ha potuto. Se e' valorizzato, vince su tutto il resto: si esce
        /// con NonVerificabile e questo motivo.
        /// </param>
        /// <param name="axes">
        /// Quale coordinata del modello corrisponde alla prima e alla seconda
        /// del piano, e quale e' la normale: tre indici fra 0, 1 e 2.
        /// </param>
        /// <param name="offsetM">La quota lungo la normale, in metri.</param>
        /// <param name="positions">I punti chiesti, in millimetri, nel piano.</param>
        /// <param name="reason">
        /// Con Sbagliate, la frase da dare a chi legge. Con NonVerificabile,
        /// perche' non si e' potuto guardare. Con Verificate, null.
        /// </param>
        /// </summary>
        public static EsitoPosizioni Giudica(
            List<double[]> assiTrovati,
            string nonPosso,
            double[] assi,
            double offsetM,
            List<double[]> posizioni,
            out string motivo)
        {
            if (!string.IsNullOrEmpty(nonPosso))
            {
                motivo = nonPosso;
                return EsitoPosizioni.NonVerificabile;
            }

            if (posizioni == null || posizioni.Count == 0)
            {
                motivo = "non e' stata chiesta nessuna posizione, non c'e' niente da confrontare";
                return EsitoPosizioni.NonVerificabile;
            }

            // IL CASO CHE PRIMA PASSAVA PER BUONO.
            // Nessuna faccia cilindrica del diametro atteso sulla lavorazione:
            // non vuol dire che i fori sono al posto giusto, vuol dire che non
            // si sa nemmeno se ci sono.
            if (assiTrovati == null || assiTrovati.Count == 0)
            {
                motivo = "sulla lavorazione non c'e' nessuna faccia cilindrica " +
                         "del diametro atteso: non so dire dove siano finiti i fori";
                return EsitoPosizioni.NonVerificabile;
            }

            if (assi == null || assi.Length < 3)
            {
                motivo = "non so su quale piano guardare";
                return EsitoPosizioni.NonVerificabile;
            }

            double peggiore = 0;
            int lontano = -1;
            for (int i = 0; i < posizioni.Count; i++)
            {
                var atteso = new double[3];
                atteso[(int)assi[0]] = Unita.M(posizioni[i][0]);
                atteso[(int)assi[1]] = Unita.M(posizioni[i][1]);
                atteso[(int)assi[2]] = offsetM;

                double vicino = double.MaxValue;
                foreach (var par in assiTrovati)
                    vicino = Math.Min(vicino, DistanzaDallAsse(atteso, par));
                if (vicino > peggiore) { peggiore = vicino; lontano = i; }
            }

            if (peggiore * 1000.0 > TolleranzaMm)
            {
                motivo = string.Format(
                    CultureInfo.InvariantCulture,
                    "Il foro {0}, chiesto a ({1:0.##}, {2:0.##}), e' finito a {3:0.##} mm " +
                    "da li'. Non te lo lascio passare per buono: ritiralo — " +
                    Frasi.CosiSiRitira + ".",
                    lontano + 1, posizioni[lontano][0], posizioni[lontano][1], peggiore * 1000.0);
                return EsitoPosizioni.Sbagliate;
            }

            motivo = null;
            return EsitoPosizioni.Verificate;
        }

        /// <summary>Distanza fra un punto e la retta di un cilindro, in metri.</summary>
        public static double DistanzaDallAsse(double[] punto, double[] cilindro)
        {
            if (punto == null || cilindro == null || punto.Length < 3 || cilindro.Length < 6)
                return double.MaxValue;
            double vx = punto[0] - cilindro[0];
            double vy = punto[1] - cilindro[1];
            double vz = punto[2] - cilindro[2];
            double ax = cilindro[3], ay = cilindro[4], az = cilindro[5];
            double norma = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (norma < 1e-12) return double.MaxValue;
            ax /= norma; ay /= norma; az /= norma;
            double lungo = vx * ax + vy * ay + vz * az;
            double dx = vx - lungo * ax, dy = vy - lungo * ay, dz = vz - lungo * az;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
