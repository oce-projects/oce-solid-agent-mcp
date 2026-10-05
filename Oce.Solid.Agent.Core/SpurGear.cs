using System;
using System.Collections.Generic;
using System.Globalization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// IL PROFILO DI UNA RUOTA DENTATA CILINDRICA A DENTI DRITTI.
    ///
    /// E' matematica, e la matematica si verifica senza SOLIDWORKS: diametri,
    /// passo e spessore del dente sulla primitiva si confrontano con le formule
    /// ISO, e un errore si vede prima che arrivi in officina.
    ///
    /// LA GEOMETRIA, IN CHIARO
    ///   primitivo  d  = m·z            il diametro che «rotola»
    ///   base       db = d·cos(α)       da cui parte l'evolvente
    ///   esterno    da = d + 2m
    ///   fondo      df = d − 2,5m
    ///   passo      p  = π·m
    ///
    /// Il fianco del dente e' l'**evolvente del cerchio di base**. Un punto
    /// dell'evolvente, con parametro t:
    ///
    ///     raggio          = rb·√(1 + t²)
    ///     scarto angolare = t − atan(t)      (e' inv(α) a quel raggio)
    ///
    /// e la meta' angolare del dente sul cerchio di base vale
    /// ψ = π/(2z) + inv(α). Da li' i due fianchi sono simmetrici rispetto
    /// all'asse del dente: uno a ψ − scarto, l'altro a −ψ + scarto.
    ///
    /// IL CONTORNO SI PERCORRE IN SENSO ANTIORARIO, dente per dente: piede,
    /// fianco in salita, cresta, fianco in discesa, piede, e il fondo fino al
    /// dente dopo. Cosi' l'angolo cresce sempre, e un contorno che non torna
    /// mai indietro non si puo' incrociare da solo.
    ///
    /// LE SEMPLIFICAZIONI, DICHIARATE
    /// Il raccordo di fondo NON e' la troncoide vera: sotto il cerchio di base
    /// il fianco scende radiale fino al fondo. Per un pezzo da fresare o da
    /// tagliare al laser va bene; **per un ingranaggio di potenza verificato a
    /// fatica no**, e va detto invece che scoperto. Denti dritti soltanto:
    /// nessuna elica, nessuna correzione di profilo.
    ///
    /// PERCHE' QUESTO FILE E' DELICATO PIU' DEGLI ALTRI. Una traslitterazione
    /// sbagliata di `Inv()` o del segno di uno scarto non da' nessun errore:
    /// da' **una ruota che ingrana male**. E' il difetto che non da' errore in
    /// forma pura, e nessun controllo interno lo prende — solo la misura sul
    /// pezzo, piu' il confronto delle formule con i valori ISO.
    /// </summary>
    public static class SpurGear
    {
        /// <summary>Funzione a evolvente: inv(α) = tan(α) − α, con α in radianti.</summary>
        public static double Inv(double alpha)
        {
            return Math.Tan(alpha) - alpha;
        }

        /// <summary>
        /// Cave per linguetta nel MOZZO, serie normale (ISO/R 773, DIN 6885-1):
        /// albero fino a → larghezza b, profondita' t2 misurata dal foro.
        ///
        /// PERCHE' UNA TABELLA E NON UN ARCHIVIO DI NORME DA CONSULTARE. Per un
        /// testo normativo avrebbe senso; per una QUOTA no. Un archivio
        /// restituisce righe di testo che qualcuno deve interpretare, ed e' li'
        /// che nascono gli errori. Una cava e' una tabella — albero 20, b=6,
        /// t2=2,8 — e una tabella nel codice e' esatta, ripetibile e
        /// verificabile.
        /// </summary>
        private static readonly double[][] Keyways =
        {
            new[] { 8.0, 2.0, 1.0 },     new[] { 10.0, 3.0, 1.4 },
            new[] { 12.0, 4.0, 1.8 },    new[] { 17.0, 5.0, 2.3 },
            new[] { 22.0, 6.0, 2.8 },    new[] { 30.0, 8.0, 3.3 },
            new[] { 38.0, 10.0, 3.3 },   new[] { 44.0, 12.0, 3.3 },
            new[] { 50.0, 14.0, 3.8 },   new[] { 58.0, 16.0, 4.3 },
            new[] { 65.0, 18.0, 4.4 },   new[] { 75.0, 20.0, 4.9 },
            new[] { 85.0, 22.0, 5.4 },   new[] { 95.0, 25.0, 5.4 },
            new[] { 110.0, 28.0, 6.4 },  new[] { 130.0, 32.0, 7.4 },
        };

        /// <summary>Larghezza e profondita' della cava per quel diametro d'albero.</summary>
        public static bool KeywayForShaft(
            double diameterMm, out double b, out double t2, out string reason)
        {
            foreach (var line in Keyways)
            {
                if (diameterMm > line[0]) continue;
                b = line[1]; t2 = line[2]; reason = null;
                return true;
            }
            b = t2 = 0;
            reason = string.Format(CultureInfo.InvariantCulture,
                "Non ho la cava normalizzata per un albero da {0:0.###} mm: la tabella " +
                "che conosco arriva a {1:0.###} mm. Dimmi tu larghezza e profondita'.",
                diameterMm, Keyways[Keyways.Length - 1][0]);
            return false;
        }

        /// <summary>
        /// I contorni della ruota, o null con il motivo.
        /// </summary>
        public static List<object> Contours(
            int teeth, double module, double pressureAngleDeg,
            double holeMm, bool keyway, int pointsPerSide,
            out string reason)
        {
            reason = null;
            if (pointsPerSide < 2) pointsPerSide = 8;

            double alpha = pressureAngleDeg * Math.PI / 180.0;
            double r = module * teeth / 2.0;
            double rb = r * Math.Cos(alpha);
            double ra = r + module;
            double rf = r - 1.25 * module;

            if (rf <= 0)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Con z={0} e m={1:0.###} il fondo del dente cadrebbe nel centro " +
                    "della ruota: i due numeri non stanno insieme.", teeth, module);
                return null;
            }
            if (ra <= rb)
            {
                reason = "Con questi dati il dente non esce dal cerchio di base: " +
                         "non c'e' profilo da disegnare.";
                return null;
            }

            // SOTTO IL LIMITE DI INTAGLIO l'evolvente entrerebbe nel piede, e
            // questo generatore non fa la correzione di profilo che servirebbe.
            // E' una semplificazione NOSTRA, non una legge della meccanica: per
            // questo il messaggio dice quale delle due cose sta succedendo,
            // invece di far credere che quella ruota non si possa fare.
            //
            // 2/sin²(α) vale 17,097 per i 20 gradi standard, e la pratica
            // arrotonda PER DIFETTO: la ruota da 17 denti si fa tutti i giorni.
            // Con l'arrotondamento per eccesso l'avremmo rifiutata, che e' il
            // modo peggiore di sbagliare — dire di no a un pezzo normale.
            int minValue = (int)Math.Floor(2.0 / (Math.Sin(alpha) * Math.Sin(alpha)));
            if (teeth < minValue)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Con un angolo di pressione di {0:0.###} gradi, sotto {1} denti " +
                    "serve la correzione di profilo per evitare l'intaglio del piede, " +
                    "e questo generatore non la sa fare. Chiedi piu' denti, oppure un " +
                    "angolo di pressione maggiore.", pressureAngleDeg, minValue);
                return null;
            }

            double psi = Math.PI / (2 * teeth) + Inv(alpha);
            double tMax = Math.Sqrt((ra / rb) * (ra / rb) - 1.0);
            double deviationMax = tMax - Math.Atan(tMax);

            // DOVE COMINCIA IL FIANCO: al cerchio di base, oppure al fondo del
            // dente se il fondo sta piu' in alto.
            //
            // Non e' un caso raro. rb = r*cos(alfa) e rf = r*(1 - 2,5/z): il
            // fondo supera la base quando cos(alfa) < 1 - 2,5/z, cioe' da 42
            // denti in su con l'angolo standard di 20 gradi. Una ruota da 127
            // denti e' un ricambio normale, non un caso limite.
            //
            // Partendo comunque da rb il fianco scendeva SOTTO il fondo — su
            // z=127 m=1 di 2,58 mm — e il contorno si tagliava da solo. Non
            // dava nessun errore: dava una ruota sbagliata.
            //
            // Il tratto di raccordo che prima era radiale resta al suo posto e
            // diventa un pezzetto di fondo: i suoi due estremi stanno tutti e
            // due su rf, quindi il contorno si chiude come prima.
            double tMin = rf > rb
                ? Math.Sqrt((rf / rb) * (rf / rb) - 1.0)
                : 0.0;

            // IL SEMIANGOLO DEL DENTE AL FONDO, che non e' psi.
            //
            // psi e' il semiangolo sul CERCHIO DI BASE: e' li' che l'evolvente
            // comincia, e da li' in su il dente si stringe di (t - atan t).
            // Quando il fianco parte piu' in alto, al fondo il dente e' gia'
            // stretto di deviationMin, e il fondo va agganciato LI'.
            //
            // Usando psi anche al fondo, con z=127 il fondo partiva a 0,02727
            // rad e finiva a 0,02221: percorreva il giro ALL'INDIETRO. Lo
            // schizzo si tagliava da solo e SOLIDWORKS rifiutava l'estrusione
            // con «L'estrusione non e' stata creata» — un messaggio che non
            // dice niente di questo.
            double deviationMin = tMin - Math.Atan(tMin);
            double psiRoot = psi - deviationMin;

            // E SE AL FONDO I DENTI SI TOCCASSERO.
            // Il vano al fondo misura (angularPitch - 2*psiRoot)*rf. Se non
            // avanza niente, i denti si saldano fra loro e il pezzo non e' piu'
            // una ruota. Non e' un caso teorico: succede quando inv(alfa) cresce
            // rispetto al passo, cioe' con tanti denti e angoli di pressione
            // grandi.
            double cavityAtBottom = (2 * Math.PI / teeth - 2 * psiRoot) * rf;
            if (cavityAtBottom <= 0.01 * module)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Con z={0}, m={1:0.###} e un angolo di pressione di {2:0.###} gradi " +
                    "fra un dente e l'altro resterebbero {3:0.####} mm di fondo: i denti " +
                    "si toccherebbero alla base. Servono meno denti oppure un angolo di " +
                    "pressione piu' piccolo.", teeth, module, pressureAngleDeg, cavityAtBottom);
                return null;
            }
            if (deviationMax >= psi)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Con z={0} e m={1:0.###} il dente si chiuderebbe a punta prima del " +
                    "diametro esterno. Servono meno denti oppure un modulo diverso.",
                    teeth, module);
                return null;
            }
            double angularPitch = 2 * Math.PI / teeth;

            Func<double, double, object[]> point = (radius, angle) => new object[]
            {
                Math.Round(radius * Math.Cos(angle), 4),
                Math.Round(radius * Math.Sin(angle), 4),
            };

            // IL DENTE E' FATTO DI TRATTI, NON DI SEGMENTI.
            //
            // I fianchi sono SPLINE per i punti calcolati: l'evolvente e' una
            // curva, e approssimarla a segmenti dava un fianco sfaccettato e
            // uno schizzo da 528 entita' per una ruota da 20 denti. Con una
            // spline per fianco restano quaranta curve e la geometria e' quella
            // vera.
            //
            // Non e' in contraddizione con la regola «mai spline con punti
            // scelti da un modello»: qui i punti li calcola una formula esatta.
            // La spline pericolosa e' quella con i punti inventati, non quella
            // con i punti giusti.
            //
            // Il resto resta a linee, ed e' giusto: il tratto radiale sotto il
            // cerchio di base e il fondo fra due denti sono rette, non curve da
            // interpolare.
            var strokes = new List<object>();
            for (int k = 0; k < teeth; k++)
            {
                double center = k * angularPitch;

                var sideUp = new List<object>();
                for (int i = 0; i <= pointsPerSide; i++)
                {
                    double t = tMin + (tMax - tMin) * i / pointsPerSide;
                    double deviation = t - Math.Atan(t);
                    sideUp.Add(point(rb * Math.Sqrt(1 + t * t), center - psi + deviation));
                }

                var sideDown = new List<object>();
                for (int i = pointsPerSide; i >= 0; i--)
                {
                    double t = tMin + (tMax - tMin) * i / pointsPerSide;
                    double deviation = t - Math.Atan(t);
                    sideDown.Add(point(rb * Math.Sqrt(1 + t * t), center + psi - deviation));
                }

                // 1. il piede: la retta radiale dal fondo fino al cerchio di
                //    base. Esiste SOLO quando la base sta sotto il fondo: se
                //    sta sopra, il fianco comincia gia' sul fondo e questa
                //    retta sarebbe lunga zero. Su z=42 era lunga 0,9 micron —
                //    non zero, quindi nessun errore, ma un'entita' inutile
                //    dentro lo schizzo per ognuno dei 42 denti.
                if (tMin == 0)
                    strokes.Add(Lines(new List<object> { point(rf, center - psi), sideUp[0] }));
                // 2. il fianco che sale, a evolvente
                strokes.Add(Spline(sideUp));
                // 3. la cresta, sul diametro esterno
                double startAngle = center - psi + deviationMax;
                double endAngle = center + psi - deviationMax;
                var ridge = new List<object> { sideUp[sideUp.Count - 1] };
                for (int i = 1; i < 3; i++)
                    ridge.Add(point(ra, startAngle + (endAngle - startAngle) * i / 3.0));
                ridge.Add(sideDown[0]);
                strokes.Add(Lines(ridge));
                // 4. il fianco che scende, simmetrico
                strokes.Add(Spline(sideDown));
                // 5. il piede dall'altro lato e il fondo fino al dente successivo
                double bottomEnd = center + angularPitch - psiRoot;
                double fromBottom = center + psiRoot;
                var bottom = new List<object> { sideDown[sideDown.Count - 1] };
                // La discesa radiale dal cerchio di base al fondo, di nuovo
                // solo quando c'e' davvero da scendere: se il fianco finisce
                // gia' sul fondo, questo punto e' quello di prima.
                if (tMin == 0) bottom.Add(point(rf, fromBottom));
                for (int i = 1; i < 3; i++)
                    bottom.Add(point(rf, fromBottom + (bottomEnd - fromBottom) * i / 3.0));
                bottom.Add(point(rf, bottomEnd));
                strokes.Add(Lines(bottom));
            }

            var contours = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["kind"] = "composite",
                    ["strokes"] = strokes.ToArray(),
                },
            };

            if (holeMm > 0)
            {
                double holeRadius = holeMm / 2.0;
                if (holeRadius >= rf * 0.95)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "Un foro da {0:0.###} mm su una ruota con fondo dente a {1:0.#} mm " +
                        "di diametro non lascia materiale sotto i denti.", holeMm, 2 * rf);
                    return null;
                }
                if (keyway)
                {
                    var withKeyway = HoleWithKeyway(holeMm, out reason);
                    if (withKeyway == null) return null;
                    contours.Add(withKeyway);
                }
                else
                {
                    // UN CERCHIO VERO, non una poligonale con tanti lati. La
                    // prima versione lo approssimava con 48 segmenti INSCRITTI:
                    // su un foro da 20 mm il materiale invade la circonferenza
                    // di 0,021 mm e un albero h7 non entra.
                    contours.Add(new Dictionary<string, object>
                    {
                        ["kind"] = "circle",
                        ["center"] = new object[] { 0.0, 0.0 },
                        ["diameter_mm"] = holeMm,
                    });
                }
            }

            return contours;
        }

        /// <summary>
        /// Il foro con la cava per linguetta: una poligonale, non un cerchio.
        ///
        /// Un cerchio con un intaglio non e' piu' un cerchio, quindi qui la
        /// circonferenza va approssimata. Due accorgimenti, tutti e due per lo
        /// stesso motivo — **un foro non deve MAI venire piu' stretto del
        /// nominale**:
        ///
        ///   * 180 lati invece di 48: lo scarto scende da 2,1 centesimi a
        ///     1,5 micron;
        ///   * i vertici stanno su un cerchio CIRCOSCRITTO (r / cos(π/n)), non
        ///     inscritto, cosi' la poligonale contiene il cerchio nominale
        ///     invece di esserne contenuta.
        /// </summary>
        private static Dictionary<string, object> HoleWithKeyway(double holeMm, out string reason)
        {
            const int sides = 180;
            double r = holeMm / 2.0;
            double b, t2;
            if (!KeywayForShaft(holeMm, out b, out t2, out reason)) return null;

            double half = b / 2.0;
            if (half >= r)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "La cava da {0:0.###} mm non sta su un foro da {1:0.###} mm.", b, holeMm);
                return null;
            }

            double rPoly = r / Math.Cos(Math.PI / sides);
            double angle = Math.Asin(half / rPoly);   // dove la cava incontra il foro
            double bottom = r + t2;                   // il fondo della cava, sotto la superficie

            // Si parte dallo spigolo sinistro dell'apertura, si gira tutto
            // attorno in senso antiorario, si arriva allo spigolo destro, e si
            // risale nella cava.
            double start = Math.PI / 2 + angle;
            double benchRun = 2 * Math.PI - 2 * angle;
            var points = new List<object>();
            for (int i = 0; i <= sides; i++)
            {
                double a = start + benchRun * i / sides;
                points.Add(new object[]
                {
                    Math.Round(rPoly * Math.Cos(a), 4),
                    Math.Round(rPoly * Math.Sin(a), 4),
                });
            }
            // la cava: su per il fianco destro, attraverso il fondo, giu' per
            // il sinistro. L'ultimo punto torna da solo al primo.
            points.Add(new object[] { Math.Round(half, 4), Math.Round(bottom, 4) });
            points.Add(new object[] { Math.Round(-half, 4), Math.Round(bottom, 4) });

            reason = null;
            return new Dictionary<string, object>
            {
                ["kind"] = "polygonal",
                ["vertices"] = points.ToArray(),
            };
        }

        private static Dictionary<string, object> Lines(List<object> points)
        {
            return new Dictionary<string, object>
            {
                ["stroke"] = "lines",
                ["points"] = points.ToArray(),
            };
        }

        private static Dictionary<string, object> Spline(List<object> points)
        {
            return new Dictionary<string, object>
            {
                ["stroke"] = "spline",
                ["points"] = points.ToArray(),
            };
        }
    }
}
