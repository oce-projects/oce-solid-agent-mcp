using System;
using System.Collections.Generic;
using System.Globalization;
using Riscontro.Disegno;

namespace Controlli.ProvaRuota
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
    ///     Controlli\ProvaRuota\bin\ProvaRuota.exe
    ///
    /// Esce 0 se torna tutto, 1 se no. Non apre SOLIDWORKS e non ne ha bisogno.
    /// </summary>
    internal static class Programma
    {
        private static int guai;

        /// <summary>
        /// Fitti apposta: i punti servono a misurare, non a disegnare. Con
        /// l'interpolazione fra due punti che scavalcano la primitiva l'errore
        /// residuo scende sotto il micron, cosi' una tolleranza stretta accusa
        /// la formula e non la discretizzazione.
        /// </summary>
        private const int PuntiPerFianco = 200;

        private static int Main()
        {
            Console.WriteLine("La ruota dentata, contro le formule ISO");
            Console.WriteLine();

            // Ruote diverse: il caso classico, il modulo grosso, il limite di
            // intaglio, il modulo fine. Una formula sbagliata puo' tornare per
            // caso su un caso solo.
            Ruota(20, 2.0, 20.0);
            Ruota(24, 3.0, 20.0);
            Ruota(17, 2.0, 20.0);
            Ruota(40, 1.5, 20.0);
            Ruota(127, 1.0, 20.0);

            // ANGOLI DI PRESSIONE DIVERSI. Serve perche' inv(20 gradi) vale
            // 0,0149 ed e' piccolo: una formula che lo dimentica del tutto
            // sbaglia lo spessore di 0,6 mm su m=2 — si vede. Ma una che lo
            // usa col segno sbagliato, a 20 gradi sbaglia poco e a 25 il doppio.
            Ruota(20, 2.0, 25.0);
            // A 14,5 gradi il limite di intaglio sale a 31 denti: chiederne 20
            // era un errore MIO, non del generatore, e il rifiuto era giusto.
            Ruota(32, 2.0, 14.5);

            // I DUE CASI ATTORNO AL SALTO. Il fondo supera la base a z=42 con
            // 20 gradi: 41 passa dalla parte vecchia del codice, 42 dalla nuova.
            Ruota(41, 2.0, 20.0);
            Ruota(42, 2.0, 20.0);

            // QUELLO CHE VA RIFIUTATO, e il motivo per cui.
            //
            // Il fondo si chiude quando 2,5*tan(alfa) supera pi/2, cioe' oltre
            // 32,15 gradi di angolo di pressione: il dente diventa cosi' largo
            // alla base che non resta vano fra un dente e l'altro. A 35 gradi
            // con 60 denti mancano 0,14 mm.
            Rifiutata(60, 1.0, 35.0, "si toccherebbero alla base");
            // Sotto il limite di intaglio.
            Rifiutata(16, 2.0, 20.0, "intaglio del piede");
            // IL DENTE A PUNTA. A 20 gradi non si arriva mai: il limite di
            // intaglio (17 denti) scatta prima, e chiedendo z=5 m=4 si viene
            // rifiutati da QUELLO — il caso sembrava provare la punta e non la
            // provava. A 45 gradi il limite di intaglio scende a 4 denti e la
            // strada si apre.
            Rifiutata(5, 4.0, 45.0, "si chiuderebbe a punta");

            Console.WriteLine();
            if (guai == 0) { Console.WriteLine("Tutto torna."); return 0; }
            Console.WriteLine(guai + " scostamenti.");
            return 1;
        }

        private static void Ruota(int z, double m, double alfaGradi)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "z={0}  m={1}  alfa={2} gradi", z, m, alfaGradi));

            string motivo;
            var contorni = RuotaDentata.Contorni(
                z, m, alfaGradi, 0, false, PuntiPerFianco, out motivo);
            if (contorni == null) { Male("rifiutata: " + motivo); return; }

            var punti = PuntiDelPrimoContorno(contorni);
            if (punti.Count < z * 4) { Male("solo " + punti.Count + " punti: il contorno non e' quello che credo"); return; }

            double r = m * z / 2.0;
            double passo = Math.PI * m;

            // 1. I RAGGI ESTREMI. Non provano l'evolvente, ma se sbagliano
            //    questi non vale la pena guardare il resto.
            double rMin = double.MaxValue, rMax = 0;
            foreach (var p in punti)
            {
                double raggio = Math.Sqrt(p[0] * p[0] + p[1] * p[1]);
                if (raggio < rMin) rMin = raggio;
                if (raggio > rMax) rMax = raggio;
            }
            Confronta("raggio di testa", rMax, r + m, 0.001);
            Confronta("raggio di fondo", rMin, r - 1.25 * m, 0.001);

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
            double rumore = 3e-4 / (r - 1.25 * m);
            double indietro = PassoIndietroPiuGrande(punti);
            if (indietro > rumore)
                Male(string.Format(CultureInfo.InvariantCulture,
                    "il contorno torna indietro di {0:0.000000} rad: si taglia da solo",
                    indietro));
            else
                Console.WriteLine("  ok  il contorno gira sempre nello stesso verso");

            // 2. GLI ATTRAVERSAMENTI DELLA PRIMITIVA. Il contorno gira, e ogni
            //    dente la attraversa due volte: una salendo, una scendendo.
            //    Se non sono 2z, il conto dei denti non e' quello dichiarato.
            var salite = new List<double>();
            var discese = new List<double>();
            Attraversamenti(punti, r, salite, discese);
            if (salite.Count != z || discese.Count != z)
            {
                Male(string.Format(CultureInfo.InvariantCulture,
                    "attraversamenti della primitiva: {0} salite e {1} discese, " +
                    "attesi {2} e {2}", salite.Count, discese.Count, z));
                return;
            }

            // 3. LO SPESSORE E IL VANO, dente per dente. Si guardano il piu'
            //    stretto e il piu' largo: una ruota con un dente storto passa,
            //    se si guarda solo la media.
            double sMin = double.MaxValue, sMax = 0, vMin = double.MaxValue, vMax = 0;
            for (int k = 0; k < z; k++)
            {
                double spessore = (discese[k] - salite[k]) * r;
                double vano = (salite[(k + 1) % z] + (k + 1 == z ? 2 * Math.PI : 0)
                               - discese[k]) * r;
                if (spessore < sMin) sMin = spessore;
                if (spessore > sMax) sMax = spessore;
                if (vano < vMin) vMin = vano;
                if (vano > vMax) vMax = vano;
            }
            Confronta("spessore minimo sulla primitiva", sMin, passo / 2, 0.002);
            Confronta("spessore massimo sulla primitiva", sMax, passo / 2, 0.002);
            Confronta("vano minimo sulla primitiva", vMin, passo / 2, 0.002);
            Confronta("vano massimo sulla primitiva", vMax, passo / 2, 0.002);
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
        private static double PassoIndietroPiuGrande(List<double[]> punti)
        {
            double peggio = 0, precedente = 0;
            bool primo = true;
            foreach (var p in punti)
            {
                double ang = Math.Atan2(p[1], p[0]);
                if (primo) { primo = false; precedente = ang; continue; }
                while (ang - precedente > Math.PI) ang -= 2 * Math.PI;
                while (precedente - ang > Math.PI) ang += 2 * Math.PI;
                double passo = ang - precedente;
                if (-passo > peggio) peggio = -passo;
                precedente = ang;
            }
            return peggio;
        }

        /// <summary>
        /// Una ruota che DEVE essere rifiutata, e per il motivo giusto.
        ///
        /// Il motivo conta quanto il rifiuto: una ruota rifiutata dal controllo
        /// sbagliato e' un controllo che non e' mai stato provato. Con
        /// <paramref name="frammento"/> vuoto basta il rifiuto.
        /// </summary>
        private static void Rifiutata(int z, double m, double alfaGradi, string frammento)
        {
            string motivo;
            var contorni = RuotaDentata.Contorni(
                z, m, alfaGradi, 0, false, PuntiPerFianco, out motivo);
            string capo = string.Format(CultureInfo.InvariantCulture,
                "z={0}  m={1}  alfa={2} gradi  (deve rifiutare)", z, m, alfaGradi);
            Console.WriteLine(capo);
            if (contorni != null) { Male("accettata invece"); Console.WriteLine(); return; }
            if (frammento.Length > 0 && motivo.IndexOf(frammento, StringComparison.Ordinal) < 0)
            {
                Male("rifiutata per un altro motivo: " + motivo);
                Console.WriteLine();
                return;
            }
            Console.WriteLine("  ok  " + motivo);
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
        private static void Attraversamenti(
            List<double[]> punti, double r, List<double> salite, List<double> discese)
        {
            double precedente = 0;
            bool primo = true;
            var angoli = new List<double>();
            var raggi = new List<double>();
            foreach (var p in punti)
            {
                double ang = Math.Atan2(p[1], p[0]);
                if (primo) { primo = false; }
                else
                {
                    while (ang - precedente > Math.PI) ang -= 2 * Math.PI;
                    while (precedente - ang > Math.PI) ang += 2 * Math.PI;
                }
                precedente = ang;
                angoli.Add(ang);
                raggi.Add(Math.Sqrt(p[0] * p[0] + p[1] * p[1]));
            }

            for (int i = 1; i < raggi.Count; i++)
            {
                double a = raggi[i - 1] - r, b = raggi[i] - r;
                if (a == 0 || b == 0 || (a < 0) == (b < 0)) continue;
                double f = a / (a - b);                       // fra 0 e 1
                double ang = angoli[i - 1] + f * (angoli[i] - angoli[i - 1]);
                if (a < 0) salite.Add(ang); else discese.Add(ang);
            }
        }

        private static List<double[]> PuntiDelPrimoContorno(List<object> contorni)
        {
            var fuori = new List<double[]>();
            var d = contorni[0] as Dictionary<string, object>;
            if (d == null) return fuori;
            object grezzo;
            if (!d.TryGetValue("tratti", out grezzo)) return fuori;
            foreach (var t in (object[])grezzo)
            {
                var tratto = t as Dictionary<string, object>;
                if (tratto == null) continue;
                object pp;
                if (!tratto.TryGetValue("punti", out pp)) continue;
                foreach (var p in (object[])pp)
                {
                    var c = p as object[];
                    if (c == null || c.Length < 2) continue;
                    double x = Convert.ToDouble(c[0], CultureInfo.InvariantCulture);
                    double y = Convert.ToDouble(c[1], CultureInfo.InvariantCulture);
                    // I tratti si toccano agli estremi: il punto ripetuto non
                    // aggiunge niente e falserebbe il conto se si contassero i
                    // punti invece degli attraversamenti.
                    if (fuori.Count > 0)
                    {
                        var u = fuori[fuori.Count - 1];
                        if (u[0] == x && u[1] == y) continue;
                    }
                    fuori.Add(new[] { x, y });
                }
            }
            return fuori;
        }

        private static void Confronta(string cosa, double avuto, double atteso, double entro)
        {
            double scarto = Math.Abs(avuto - atteso);
            string riga = string.Format(CultureInfo.InvariantCulture,
                "{0,-34} {1,10:0.#####}  atteso {2,10:0.#####}  scarto {3:0.000000}",
                cosa, avuto, atteso, scarto);
            if (double.IsNaN(avuto) || scarto > entro) Male(riga);
            else Console.WriteLine("  ok  " + riga);
        }

        private static void Male(string riga)
        {
            guai++;
            Console.WriteLine("  XX  " + riga);
        }
    }
}
