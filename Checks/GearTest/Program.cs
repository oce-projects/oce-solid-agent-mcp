using System;
using System.Collections.Generic;
using System.Globalization;
using Oce.Solid.Agent.Core;

namespace Checks.GearTest
{
    /// <summary>
    /// LA RUOTA DENTATA, CONTROLLATA SENZA SOLIDWORKS.
    ///
    /// E' matematica, e la matematica si verifica da sola: i punti che escono
    /// dal generatore si confrontano con le formule ISO, e l'errore si vede
    /// prima che arrivi nel CAD, non dopo.
    ///
    /// PERCHE' SERVE, VISTO CHE C'E' GIA' LA MISURA SUL PEZZO.
    /// Perche' la misura sul pezzo prende il DIAMETRO ESTERNO, e il diametro
    /// esterno viene da ra = r + m: una formula che si puo' azzeccare avendo
    /// sbagliato tutto il resto. L'evolvente sta nei FIANCHI, e i fianchi non
    /// si misurano con una scatola di ingombro.
    ///
    /// Peggio: si e' misurato che l'ingombro coincide col diametro esterno
    /// SOLO quando un dente capita sull'asse. Con z=20 (denti ogni 18 gradi)
    /// capita; con z=17 (ogni 21,18 gradi) no, e la scatola legge 37,79 invece
    /// di 38. Non e' un errore della ruota, e' il limite della misura.
    ///
    /// COSA CONTROLLA QUESTO, CHE QUELLO NON PUO'.
    /// Lo SPESSORE DEL DENTE SUL CERCHIO PRIMITIVO, che su una ruota senza
    /// correzione vale esattamente pi*m/2, e il VANO fra un dente e l'altro,
    /// che vale altrettanto. Sono due misure diverse:
    ///
    ///   * lo spessore sbaglia se sbaglia psi oppure inv(alfa);
    ///   * il vano sbaglia se sbaglia il passo angolare.
    ///
    /// Un solo numero giusto non basta a dire che la formula e' giusta; due
    /// numeri che vengono da pezzi diversi della formula, si'.
    ///
    ///     Checks\GearTest\bin\GearTest.exe
    ///
    /// Esce 0 se torna tutto, 1 se no. Non apre SOLIDWORKS e non ne ha bisogno.
    /// </summary>
    internal static class Program
    {
        private static int trouble;

        /// <summary>
        /// Fitti apposta: i punti servono a misurare, non a disegnare. Con
        /// l'interpolazione fra due punti che scavalcano la primitiva l'errore
        /// residuo scende sotto il micron, cosi' una tolleranza stretta accusa
        /// la formula e non la discretizzazione.
        /// </summary>
        private const int PointsPerSide = 200;

        private static int Main()
        {
            Console.WriteLine("La ruota dentata, contro le formule ISO");
            Console.WriteLine();

            // Ruote diverse: il caso classico, il modulo grosso, il limite di
            // intaglio, il modulo fine. Una formula sbagliata puo' tornare per
            // caso su un caso solo.
            Gear(20, 2.0, 20.0);
            Gear(24, 3.0, 20.0);
            Gear(17, 2.0, 20.0);
            Gear(40, 1.5, 20.0);
            Gear(127, 1.0, 20.0);

            // ANGOLI DI PRESSIONE DIVERSI. Serve perche' inv(20 gradi) vale
            // 0,0149 ed e' piccolo: una formula che lo dimentica del tutto
            // sbaglia lo spessore di 0,6 mm su m=2 — si vede. Ma una che lo
            // usa col segno sbagliato, a 20 gradi sbaglia poco e a 25 il doppio.
            Gear(20, 2.0, 25.0);
            // A 14,5 gradi il limite di intaglio sale a 31 denti: chiederne 20
            // era un errore MIO, non del generatore, e il rifiuto era giusto.
            Gear(32, 2.0, 14.5);

            // I DUE CASI ATTORNO AL SALTO. Il fondo supera la base a z=42 con
            // 20 gradi: 41 passa dalla parte vecchia del codice, 42 dalla nuova.
            Gear(41, 2.0, 20.0);
            Gear(42, 2.0, 20.0);

            // QUELLO CHE VA RIFIUTATO, e il motivo per cui.
            //
            // Il fondo si chiude quando 2,5*tan(alfa) supera pi/2, cioe' oltre
            // 32,15 gradi di angolo di pressione: il dente diventa cosi' largo
            // alla base che non resta vano fra un dente e l'altro. A 35 gradi
            // con 60 denti mancano 0,14 mm.
            Refused(60, 1.0, 35.0, "si toccherebbero alla base");
            // Sotto il limite di intaglio.
            Refused(16, 2.0, 20.0, "intaglio del piede");
            // IL DENTE A PUNTA. A 20 gradi non si arriva mai: il limite di
            // intaglio (17 denti) scatta prima, e chiedendo z=5 m=4 si viene
            // rifiutati da QUELLO — il caso sembrava provare la punta e non la
            // provava. A 45 gradi il limite di intaglio scende a 4 denti e la
            // strada si apre.
            Refused(5, 4.0, 45.0, "si chiuderebbe a punta");

            Console.WriteLine();
            if (trouble == 0) { Console.WriteLine("Tutto torna."); return 0; }
            Console.WriteLine(trouble + " scostamenti.");
            return 1;
        }

        private static void Gear(int z, double m, double alphaDeg)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "z={0}  m={1}  alfa={2} gradi", z, m, alphaDeg));

            string reason;
            var contours = SpurGear.Contours(
                z, m, alphaDeg, 0, false, PointsPerSide, out reason);
            if (contours == null) { Male("rifiutata: " + reason); return; }

            var points = FirstContourPoints(contours);
            if (points.Count < z * 4) { Male("solo " + points.Count + " punti: il contorno non e' quello che credo"); return; }

            double r = m * z / 2.0;
            double planStep = Math.PI * m;

            // 1. I RAGGI ESTREMI. Non provano l'evolvente, ma se sbagliano
            //    questi non vale la pena guardare il resto.
            double rMin = double.MaxValue, rMax = 0;
            foreach (var p in points)
            {
                double radius = Math.Sqrt(p[0] * p[0] + p[1] * p[1]);
                if (radius < rMin) rMin = radius;
                if (radius > rMax) rMax = radius;
            }
            Compare("raggio di testa", rMax, r + m, 0.001);
            Compare("raggio di fondo", rMin, r - 1.25 * m, 0.001);

            // 1b. IL CONTORNO NON TORNA MAI INDIETRO.
            //
            // Questo e' il controllo che avrebbe preso da solo il difetto di
            // z=127: il fondo agganciato all'angolo sbagliato faceva percorrere
            // un pezzo di giro al contrario, lo schizzo si tagliava da se' e
            // SOLIDWORKS diceva soltanto «L'estrusione non e' stata creata».
            //
            // Un contorno chiuso che gira in un verso ha l'angolo srotolato
            // sempre crescente. Un passo negativo e' un'autointersezione, e si
            // vede senza aprire niente.
            // La soglia non e' zero, ed e' calcolata invece che scelta. I punti
            // escono arrotondati al decimillesimo di millimetro; sul raggio piu'
            // piccolo quell'arrotondamento vale 1e-4/rf radianti di rumore
            // tangenziale, e tre volte tanto copre il caso peggiore. Su z=20
            // fa 1,7e-5 contro i 6e-6 misurati; su z=127 fa 4,8e-6 contro i
            // 1,7e-3 del difetto. Fra rumore e difetto ci sono due ordini di
            // grandezza, quindi la soglia non e' un compromesso.
            double noise = 3e-4 / (r - 1.25 * m);
            double back = BiggerStepBack(points);
            if (back > noise)
                Male(string.Format(CultureInfo.InvariantCulture,
                    "il contorno torna indietro di {0:0.000000} rad: si taglia da solo",
                    back));
            else
                Console.WriteLine("  ok  il contorno gira sempre nello stesso verso");

            // 2. GLI ATTRAVERSAMENTI DELLA PRIMITIVA. Il contorno gira, e ogni
            //    dente la attraversa due volte: una salendo, una scendendo.
            //    Se non sono 2z, il conto dei denti non e' quello dichiarato.
            var upCrossings = new List<double>();
            var downCrossings = new List<double>();
            Crossings(points, r, upCrossings, downCrossings);
            if (upCrossings.Count != z || downCrossings.Count != z)
            {
                Male(string.Format(CultureInfo.InvariantCulture,
                    "attraversamenti della primitiva: {0} salite e {1} discese, " +
                    "attesi {2} e {2}", upCrossings.Count, downCrossings.Count, z));
                return;
            }

            // 3. LO SPESSORE E IL VANO, dente per dente. Si guardano il piu'
            //    stretto e il piu' largo: una ruota con un dente storto passa,
            //    se si guarda solo la media.
            double sMin = double.MaxValue, sMax = 0, vMin = double.MaxValue, vMax = 0;
            for (int k = 0; k < z; k++)
            {
                double thickness = (downCrossings[k] - upCrossings[k]) * r;
                double cavity = (upCrossings[(k + 1) % z] + (k + 1 == z ? 2 * Math.PI : 0)
                               - downCrossings[k]) * r;
                if (thickness < sMin) sMin = thickness;
                if (thickness > sMax) sMax = thickness;
                if (cavity < vMin) vMin = cavity;
                if (cavity > vMax) vMax = cavity;
            }
            Compare("spessore minimo sulla primitiva", sMin, planStep / 2, 0.002);
            Compare("spessore massimo sulla primitiva", sMax, planStep / 2, 0.002);
            Compare("vano minimo sulla primitiva", vMin, planStep / 2, 0.002);
            Compare("vano massimo sulla primitiva", vMax, planStep / 2, 0.002);
            Console.WriteLine();
        }

        /// <summary>
        /// Il passo all'indietro piu' grande, in radianti, srotolando gli
        /// angoli lungo il contorno. Zero se il giro e' sempre nello stesso
        /// verso.
        ///
        /// Il fondo e la cresta sono corde: fra due punti l'angolo cresce
        /// pochissimo, ma cresce. L'arrotondamento a 4 decimali puo' dare
        /// qualche nanoradiante di rumore, e per questo la soglia non e'
        /// esattamente zero.
        /// </summary>
        private static double BiggerStepBack(List<double[]> points)
        {
            double worse = 0, previous = 0;
            bool first = true;
            foreach (var p in points)
            {
                double angle = Math.Atan2(p[1], p[0]);
                if (first) { first = false; previous = angle; continue; }
                while (angle - previous > Math.PI) angle -= 2 * Math.PI;
                while (previous - angle > Math.PI) angle += 2 * Math.PI;
                double planStep = angle - previous;
                if (-planStep > worse) worse = -planStep;
                previous = angle;
            }
            return worse;
        }

        /// <summary>
        /// Una ruota che DEVE essere rifiutata, e per il motivo giusto.
        ///
        /// Il motivo conta quanto il rifiuto: una ruota rifiutata dal controllo
        /// sbagliato e' un controllo che non e' mai stato provato. Con
        /// <paramref name="fragment"/> vuoto basta il rifiuto.
        /// </summary>
        private static void Refused(int z, double m, double alphaDeg, string fragment)
        {
            string reason;
            var contours = SpurGear.Contours(
                z, m, alphaDeg, 0, false, PointsPerSide, out reason);
            string head = string.Format(CultureInfo.InvariantCulture,
                "z={0}  m={1}  alfa={2} gradi  (deve rifiutare)", z, m, alphaDeg);
            Console.WriteLine(head);
            if (contours != null) { Male("accettata invece"); Console.WriteLine(); return; }
            if (fragment.Length > 0 && reason.IndexOf(fragment, StringComparison.Ordinal) < 0)
            {
                Male("rifiutata per un altro motivo: " + reason);
                Console.WriteLine();
                return;
            }
            Console.WriteLine("  ok  " + reason);
            Console.WriteLine();
        }

        /// <summary>
        /// Dove il contorno taglia il cerchio primitivo, e in che verso.
        ///
        /// Si scorre il contorno NELL'ORDINE IN CUI E' SCRITTO, non per angolo:
        /// l'ordine e' quello del giro, e due punti consecutivi che stanno uno
        /// dentro e uno fuori la primitiva sono un attraversamento. Fra i due
        /// si interpola, perche' il punto esatto non e' quasi mai uno di quelli
        /// calcolati.
        ///
        /// Gli angoli si srotolano man mano (niente salto da +pi a -pi), cosi'
        /// il vano fra l'ultimo dente e il primo si misura come tutti gli altri.
        /// </summary>
        private static void Crossings(
            List<double[]> points, double r, List<double> upCrossings, List<double> downCrossings)
        {
            double previous = 0;
            bool first = true;
            var angles = new List<double>();
            var radii = new List<double>();
            foreach (var p in points)
            {
                double angle = Math.Atan2(p[1], p[0]);
                if (first) { first = false; }
                else
                {
                    while (angle - previous > Math.PI) angle -= 2 * Math.PI;
                    while (previous - angle > Math.PI) angle += 2 * Math.PI;
                }
                previous = angle;
                angles.Add(angle);
                radii.Add(Math.Sqrt(p[0] * p[0] + p[1] * p[1]));
            }

            for (int i = 1; i < radii.Count; i++)
            {
                double a = radii[i - 1] - r, b = radii[i] - r;
                if (a == 0 || b == 0 || (a < 0) == (b < 0)) continue;
                double f = a / (a - b);                       // fra 0 e 1
                double angle = angles[i - 1] + f * (angles[i] - angles[i - 1]);
                if (a < 0) upCrossings.Add(angle); else downCrossings.Add(angle);
            }
        }

        private static List<double[]> FirstContourPoints(List<object> contours)
        {
            var outside = new List<double[]>();
            var d = contours[0] as Dictionary<string, object>;
            if (d == null) return outside;
            object raw;
            if (!d.TryGetValue("strokes", out raw)) return outside;
            foreach (var t in (object[])raw)
            {
                var stroke = t as Dictionary<string, object>;
                if (stroke == null) continue;
                object pp;
                if (!stroke.TryGetValue("points", out pp)) continue;
                foreach (var p in (object[])pp)
                {
                    var c = p as object[];
                    if (c == null || c.Length < 2) continue;
                    double x = Convert.ToDouble(c[0], CultureInfo.InvariantCulture);
                    double y = Convert.ToDouble(c[1], CultureInfo.InvariantCulture);
                    // I tratti si toccano agli estremi: il punto ripetuto non
                    // aggiunge niente e falserebbe il conto se si contassero i
                    // punti invece degli attraversamenti.
                    if (outside.Count > 0)
                    {
                        var u = outside[outside.Count - 1];
                        if (u[0] == x && u[1] == y) continue;
                    }
                    outside.Add(new[] { x, y });
                }
            }
            return outside;
        }

        private static void Compare(string what, double had, double expected, double within)
        {
            double deviation = Math.Abs(had - expected);
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0,-34} {1,10:0.#####}  atteso {2,10:0.#####}  scarto {3:0.000000}",
                what, had, expected, deviation);
            if (double.IsNaN(had) || deviation > within) Male(line);
            else Console.WriteLine("  ok  " + line);
        }

        private static void Male(string line)
        {
            trouble++;
            Console.WriteLine("  XX  " + line);
        }
    }
}
