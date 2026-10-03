using System;
using System.Collections.Generic;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Dove sta un punto rispetto al bordo di una sagoma.
    ///
    /// Il bordo e' una risposta a se': un punto che ci sta sopra non e' ne'
    /// dentro ne' fuori, e costringerlo a essere uno dei due fa dipendere il
    /// verdetto dal lato del poligono su cui capita. Vedi
    /// <see cref="FormaChiusa.DoveSta"/>.
    /// </summary>
    public enum DoveSiTrova
    {
        /// <summary>Dentro il materiale, bordo escluso.</summary>
        Dentro,

        /// <summary>Fuori dal materiale, bordo escluso.</summary>
        Fuori,

        /// <summary>Sul bordo, entro <see cref="FormaChiusa.SulBordoMm"/>.</summary>
        SulBordo
    }

    /// <summary>
    /// LA GEOMETRIA DI UNA SAGOMA PIANA, senza SOLIDWORKS di mezzo.
    ///
    /// Serve a rispondere a due domande che SOLIDWORKS **non** fa:
    ///
    ///   * questo contorno si incrocia da solo?
    ///   * questo vuoto sta dentro il bordo esterno?
    ///
    /// Nessuna delle due da' errore. Un contorno che si incrocia viene
    /// costruito lo stesso: SOLIDWORKS estrude le regioni che riesce a
    /// chiudere e tira fuori un pezzo plausibile e sbagliato. Un vuoto che
    /// sborda **taglia** il pezzo invece di forarlo, e il risultato e' un
    /// solido perfettamente valido che non e' quello che si e' chiesto.
    ///
    /// Sta in un file suo, e non dentro l'esecutore, per un motivo pratico:
    /// cosi' si prova senza aprire SOLIDWORKS. Un controllo geometrico che si
    /// puo' provare solo a CAD aperto viene provato poco.
    ///
    /// CONVENZIONE SUI PUNTI. Un anello si passa **senza ripetere il primo
    /// punto in coda**: l'ultimo lato e' sempre da `punti[n-1]` a `punti[0]`.
    /// Ripeterlo aggiungerebbe un lato di lunghezza zero, che manda in
    /// confusione sia il conto dell'area sia quello degli incroci. Da qui
    /// <see cref="Anello"/>, che lo toglie se c'e'.
    /// </summary>
    public static class FormaChiusa
    {
        /// <summary>Due punti coincidono, entro la tolleranza data in mm.</summary>
        public static bool Vicini(double[] a, double[] b, double tolleranza = 1e-6)
        {
            return Math.Abs(a[0] - b[0]) < tolleranza
                && Math.Abs(a[1] - b[1]) < tolleranza;
        }

        /// <summary>
        /// L'anello pulito: via i punti ripetuti di fila e via il primo
        /// ripetuto in coda.
        ///
        /// I doppioni non sono un caso di scuola. I contorni compositi si
        /// scrivono tratto per tratto e la fine di un tratto **e'** l'inizio
        /// del successivo, quindi ogni giunzione porta un punto doppio; e chi
        /// scrive una spezzata a mano ripete spesso il primo vertice in fondo
        /// per «chiuderla».
        /// </summary>
        public static List<double[]> Anello(IEnumerable<double[]> punti, double tolleranza = 1e-6)
        {
            var fuori = new List<double[]>();
            foreach (var p in punti)
            {
                if (fuori.Count > 0 && Vicini(fuori[fuori.Count - 1], p, tolleranza)) continue;
                fuori.Add(p);
            }
            while (fuori.Count > 1 && Vicini(fuori[0], fuori[fuori.Count - 1], tolleranza))
                fuori.RemoveAt(fuori.Count - 1);
            return fuori;
        }

        /// <summary>
        /// L'area con segno secondo Gauss, in valore assoluto. In mm quadri.
        /// </summary>
        public static double Area(IList<double[]> anello)
        {
            double somma = 0;
            int n = anello.Count;
            for (int i = 0; i < n; i++)
            {
                var a = anello[i];
                var b = anello[(i + 1) % n];
                somma += a[0] * b[1] - b[0] * a[1];
            }
            return Math.Abs(somma) / 2.0;
        }

        /// <summary>
        /// Dove sta un punto rispetto a un poligono: dentro, fuori, o sul suo
        /// bordo.
        ///
        /// PERCHE' TRE RISPOSTE E NON DUE.
        ///
        /// La parita' degli attraversamenti non sa rispondere sul bordo, e la
        /// risposta che da' DIPENDE DAL LATO: col confronto `x &lt; taglio` un
        /// vertice sul bordo destro risulta fuori, e lo stesso vertice sul
        /// bordo sinistro risulta dentro.
        ///
        /// Sarebbe un dettaglio se restasse un punto solo. Ma il controllo dei
        /// contorni classifica TUTTI i vertici e decide da quelli. Misurato il
        /// 14/9/2026 su una piastra 100x100: una cava a filo del bordo DESTRO —
        /// (30,-10)(50,-10)(50,10)(30,10) — ha due vertici «fuori» e due
        /// «dentro», e veniva rifiutata come «a cavallo del bordo esterno». La
        /// stessa cava specchiata a SINISTRA ha tutti e quattro i vertici
        /// «dentro», e passava.
        ///
        /// Stessa geometria, verdetto opposto secondo il lato. Uno dei due era
        /// sbagliato — e SOLIDWORKS ha detto quale.
        ///
        /// LA PRIMA RIPARAZIONE ANDAVA NEL VERSO SBAGLIATO, e vale la pena
        /// scriverlo. Il ragionamento era «una cava a filo del bordo e'
        /// un'asola aperta, si fa tutti i giorni, quindi vanno accettate tutte
        /// e due». E' vero del PEZZO e falso dello SCHIZZO: due contorni che
        /// condividono un tratto di bordo non delimitano una regione, e
        /// l'asola aperta si disegna come UN contorno solo con otto punti, non
        /// come due che si toccano.
        ///
        /// Misurato, tre geometrie, un solo risultato — «L'estrusione non e'
        /// stata creata»:
        ///
        ///   * cava a filo del bordo destro;
        ///   * la stessa specchiata a sinistra;
        ///   * un rombo che tocca il bordo in UN SOLO VERTICE.
        ///
        /// Quindi il verdetto giusto per tutt'e due i lati e' RIFIUTARE, e
        /// quello che era sbagliato era l'accettazione a sinistra. Il valore di
        /// «sul bordo» non e' far passare di piu': e' far dare **la stessa
        /// risposta** ai due lati, e dare un motivo migliore di quello che da'
        /// SOLIDWORKS.
        ///
        /// > Il verso di una riparazione non si ricava dal ragionamento
        /// > sull'officina: si chiede al pezzo. «E' lavoro legittimo» diceva
        /// > come DEVE andare, non come VA.
        /// </summary>
        public static DoveSiTrova DoveSta(double[] punto, IList<double[]> poligono)
        {
            if (punto == null || poligono == null || poligono.Count < 3)
                return DoveSiTrova.Fuori;
            if (DistanzaDalBordo(punto, poligono) <= SulBordoMm)
                return DoveSiTrova.SulBordo;
            return Dentro(punto, poligono) ? DoveSiTrova.Dentro : DoveSiTrova.Fuori;
        }

        /// <summary>
        /// Quanto un punto puo' distare dal bordo e contare ancora come «sul
        /// bordo», in millimetri.
        ///
        /// Un micrometro. Misurato il divario da tutt'e due le parti: il rumore
        /// del calcolo su coordinate in millimetri e' dell'ordine di 1e-13,
        /// dieci milioni di volte sotto; la piu' piccola quota che abbia senso
        /// su un pezzo e' un centesimo di millimetro, diecimila volte sopra.
        /// Nessuno dei due puo' essere confuso con l'altro.
        /// </summary>
        public const double SulBordoMm = 1e-6;

        /// <summary>
        /// La distanza minima fra un punto e il bordo del poligono, in mm.
        /// </summary>
        public static double DistanzaDalBordo(double[] punto, IList<double[]> poligono)
        {
            double minima = double.MaxValue;
            int n = poligono.Count;
            for (int i = 0; i < n; i++)
            {
                double d = DistanzaDalSegmento(punto, poligono[i], poligono[(i + 1) % n]);
                if (d < minima) minima = d;
            }
            return minima;
        }

        /// <summary>Distanza fra un punto e un segmento, estremi compresi.</summary>
        private static double DistanzaDalSegmento(double[] p, double[] a, double[] b)
        {
            double vx = b[0] - a[0], vy = b[1] - a[1];
            double wx = p[0] - a[0], wy = p[1] - a[1];
            double lunghezza2 = vx * vx + vy * vy;
            // Segmento degenere: e' un punto, e la distanza e' da quel punto.
            if (lunghezza2 < 1e-24) return Math.Sqrt(wx * wx + wy * wy);
            double t = (wx * vx + wy * vy) / lunghezza2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double dx = wx - t * vx, dy = wy - t * vy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Il punto sta dentro il poligono? Parita' degli attraversamenti.
        ///
        /// NON RISPONDE SUL BORDO, e la risposta che da' li' dipende dal lato:
        /// usare <see cref="DoveSta"/> ogni volta che un punto puo' trovarcisi.
        /// Questa resta per i casi in cui il bordo e' escluso per costruzione.
        /// </summary>
        public static bool Dentro(double[] punto, IList<double[]> poligono)
        {
            double x = punto[0], y = punto[1];
            bool dentro = false;
            int n = poligono.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poligono[i];
                var b = poligono[(i + 1) % n];
                if ((a[1] > y) != (b[1] > y))
                {
                    double taglio = a[0] + (y - a[1]) * (b[0] - a[0]) / (b[1] - a[1]);
                    if (x < taglio) dentro = !dentro;
                }
            }
            return dentro;
        }

        /// <summary>
        /// Due segmenti si incrociano DAVVERO, cioe' si attraversano. Toccarsi
        /// a un estremo non conta: e' quello che fanno due lati consecutivi, ed
        /// e' normale.
        /// </summary>
        public static bool Incrociano(double[] p1, double[] p2, double[] p3, double[] p4)
        {
            double d1 = Lato(p3, p4, p1), d2 = Lato(p3, p4, p2);
            double d3 = Lato(p1, p2, p3), d4 = Lato(p1, p2, p4);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
        }

        private static double Lato(double[] a, double[] b, double[] c)
        {
            return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
        }

        /// <summary>
        /// L'anello si incrocia da solo? Se si', torna l'indice del primo lato
        /// coinvolto; -1 se e' pulito.
        ///
        /// Si torna l'indice e non un booleano perche' il messaggio deve poter
        /// dire DOVE: «si incrocia» su una sagoma da duecento vertici non aiuta
        /// nessuno a ripararla.
        ///
        /// COSTO. E' un confronto di tutti i lati con tutti: n²/2. Su una ruota
        /// da 127 denti sono circa 3.300 punti e 5,4 milioni di confronti, che
        /// in C# sono meno di un secondo. Sopra <see cref="TroppiPerIlControllo"/>
        /// punti si rinuncia e lo si dice, invece di far aspettare in silenzio.
        /// </summary>
        public static int DoveSiIncrocia(IList<double[]> anello)
        {
            int n = anello.Count;
            if (n < 4 || n > TroppiPerIlControllo) return -1;
            for (int i = 0; i < n; i++)
            {
                var a1 = anello[i];
                var a2 = anello[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    // Il primo e l'ultimo lato condividono un vertice.
                    if (i == 0 && j == n - 1) continue;
                    if (Incrociano(a1, a2, anello[j], anello[(j + 1) % n])) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Oltre questi punti il controllo degli incroci si salta.
        ///
        /// 6.000 punti sono 18 milioni di confronti, ancora sotto il secondo.
        /// Il tetto sta piu' in alto di qualunque sagoma che il ponte accetti
        /// (4.000 punti per i contorni generati dalle ricette), quindi in
        /// pratica non si salta mai: e' una rete, non una scorciatoia.
        /// </summary>
        public const int TroppiPerIlControllo = 6000;
    }
}
