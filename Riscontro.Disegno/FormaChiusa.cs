using System;
using System.Collections.Generic;

namespace Riscontro.Disegno
{
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
        /// Il punto sta dentro il poligono? Parita' degli attraversamenti.
        ///
        /// Un punto ESATTAMENTE sul bordo puo' cadere da una parte o
        /// dall'altra: e' il limite del metodo, e per questo il controllo sui
        /// vuoti non si fida di un punto solo ma li guarda tutti.
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
