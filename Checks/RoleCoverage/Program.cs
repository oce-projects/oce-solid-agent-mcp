using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.RoleCoverage
{
    /// <summary>
    /// QUANTE FACCE HANNO UN RUOLO UNIVOCO? (misura che dimensiona la voce E7)
    ///
    /// E7 propone di nominare una faccia per SIGNIFICATO invece che per
    /// posizione: «il fondo della tasca fatta al passo 2» resta il fondo della
    /// tasca qualunque quota si cambi a monte.
    ///
    /// Meta' del meccanismo esiste gia' e funziona, in `References.cs`, ma
    /// vale solo DENTRO un piano: risolve «la faccia superiore di quello che
    /// hai fatto al passo 1» perche' quel passo e' ancora in mano, con la sua
    /// direzione di estrusione. Per una faccia che esiste GIA' nell'albero
    /// quella direzione non c'e'.
    ///
    /// La voce E7 chiede espressamente un numero prima di costruire:
    ///
    ///   «Copertura da dichiarare come numero: su venti facce citate a caso in
    ///   dieci pezzi di prova, quante hanno un ruolo univoco. Se e' sotto meta',
    ///   questa e' un complemento, non la strada principale, E VA SCRITTO.»
    ///
    /// Questo attrezzo misura quel numero sul documento aperto, senza cambiare
    /// niente. Si lancia su un pezzo per volta.
    ///
    ///     Checks\RoleCoverage\bin\RoleCoverage.exe
    ///
    /// I QUATTRO RUOLI, e cosa serve a ciascuno per essere deciso:
    ///
    ///   hole_face     la sola geometria: c'e' UNA faccia cilindrica?
    ///   top_face    la DIREZIONE della lavorazione, piu' la geometria
    ///   bottom_face    la DIREZIONE
    ///   pocket_floor     la DIREZIONE
    ///
    /// Tre su quattro dipendono dalla direzione. Questo attrezzo prova a
    /// ricavarla dalla definizione della lavorazione e DICE quando non ci
    /// riesce, invece di contare quella faccia come «senza ruolo» e far
    /// sembrare il problema geometrico quando e' di informazione mancante.
    ///
    /// Esce sempre 0: e' una misura, non un controllo. Un numero basso non e'
    /// un fallimento, e' la risposta.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Quanto una normale deve essere allineata alla direzione per contare
        /// come «perpendicolare al taglio». Stesso valore di `References`.
        /// </summary>
        private const double Cosine = 0.99;

        private static int Main()
        {
            ISldWorks sw;
            try { sw = Marshal.GetActiveObject("SldWorks.Application") as ISldWorks; }
            catch { Console.Error.WriteLine("SolidWorks non e' aperto."); return 0; }
            if (sw == null) { Console.Error.WriteLine("SolidWorks non risponde."); return 0; }

            var doc = sw.ActiveDoc as IModelDoc2;
            if (doc == null) { Console.Error.WriteLine("Nessun documento aperto."); return 0; }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                Console.Error.WriteLine("Il documento attivo non e' una parte.");
                return 0;
            }

            Console.WriteLine("Copertura dei ruoli su: " + doc.GetTitle());
            Console.WriteLine();

            var faces = AllFaces(doc);
            Console.WriteLine("  facce totali: " + faces.Count);

            // ── a quale lavorazione appartiene ogni faccia ──────────────────
            var perFeature = new Dictionary<int, List<IFace2>>();
            var featureName = new Dictionary<int, string>();
            var direction = new Dictionary<int, double[]>();
            int withoutFeature = 0;

            foreach (var f in faces)
            {
                IFeature feat = null;
                try { feat = f.GetFeature() as IFeature; } catch { }
                if (feat == null) { withoutFeature++; continue; }

                int id;
                try { id = feat.GetID(); } catch { withoutFeature++; continue; }

                if (!perFeature.ContainsKey(id))
                {
                    perFeature[id] = new List<IFace2>();
                    try { featureName[id] = feat.Name; } catch { featureName[id] = "?"; }
                    direction[id] = DirectionOf(feat);
                }
                perFeature[id].Add(f);
            }

            Console.WriteLine("  senza lavorazione leggibile: " + withoutFeature);
            Console.WriteLine("  lavorazioni con facce: " + perFeature.Count);
            Console.WriteLine();

            // ── il ruolo di ogni faccia, lavorazione per lavorazione ────────
            int unique = 0, ambiguous = 0, withoutRole = 0, withoutDirection = 0;
            var roleCount = new Dictionary<string, int>();

            foreach (var entry in perFeature.OrderBy(v => v.Key))
            {
                var its = entry.Value;
                var dir = direction[entry.Key];
                var roleOf = new Dictionary<IFace2, string>();

                // hole_face: l'unica che NON ha bisogno della direzione.
                var cylindrical = its.Where(Cylindrical).ToList();
                if (cylindrical.Count == 1) roleOf[cylindrical[0]] = "hole_face";

                if (dir != null)
                {
                    var above = End(its, dir, +1);
                    var below = End(its, dir, -1);
                    if (above != null && !roleOf.ContainsKey(above))
                        roleOf[above] = "top_face";
                    if (below != null && !roleOf.ContainsKey(below) && !ReferenceEquals(below, above))
                        roleOf[below] = "bottom_face";
                }

                int howManyHere = 0;
                foreach (var f in its)
                {
                    string r;
                    if (roleOf.TryGetValue(f, out r))
                    {
                        unique++;
                        howManyHere++;
                        roleCount[r] = (roleCount.ContainsKey(r) ? roleCount[r] : 0) + 1;
                    }
                    else if (dir == null) withoutDirection++;
                    else withoutRole++;
                }

                // PIU' DI UNA FACCIA CILINDRICA NON E' «NESSUN RUOLO»: e'
                // un'ambiguita', e in `References` fa scattare un rifiuto
                // invece di una scelta. Contarla insieme alle facce che un
                // ruolo non ce l'hanno proprio confonderebbe due problemi
                // diversi: uno si risolve aggiungendo vocabolario, l'altro no.
                if (cylindrical.Count > 1) ambiguous += cylindrical.Count;

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-34} {1,2} facce, {2} con ruolo{3}",
                    Short(featureName[entry.Key], 34), its.Count, howManyHere,
                    dir == null ? "   [direzione non leggibile]" : ""));
            }

            // ── il numero che la voce E7 chiede ────────────────────────────
            Console.WriteLine();
            Console.WriteLine("=== Il numero");
            int total = faces.Count;
            Console.WriteLine("  con ruolo univoco:            " + Line(unique, total));
            Console.WriteLine("  ambigue (piu' cilindriche):   " + Line(ambiguous, total));
            Console.WriteLine("  senza ruolo (geometria):      " + Line(withoutRole, total));
            Console.WriteLine("  senza ruolo (manca direzione):" + Line(withoutDirection, total));
            Console.WriteLine();
            foreach (var r in roleCount.OrderByDescending(x => x.Value))
                Console.WriteLine("      " + r.Key.PadRight(20) + r.Value);

            Console.WriteLine();
            double dimension = total > 0 ? unique * 100.0 / total : 0;
            Console.WriteLine(dimension >= 50
                ? "  Sopra meta': per questo pezzo la strada del ruolo regge."
                : "  SOTTO META': per questo pezzo il ruolo e' un complemento, non la strada.");

            return 0;
        }

        private static string Line(int howMany, int total)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0,3}  ({1,5:0.0}%)",
                howMany, total > 0 ? howMany * 100.0 / total : 0);
        }

        private static string Short(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Length <= n ? s : s.Substring(0, n - 3) + "...";
        }

        private static List<IFace2> AllFaces(IModelDoc2 doc)
        {
            var outside = new List<IFace2>();
            try
            {
                var partDoc = doc as IPartDoc;
                if (partDoc == null) return outside;
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null) return outside;
                foreach (var c in bodies)
                {
                    var body = c as IBody2;
                    if (body == null) continue;
                    var f = body.GetFirstFace() as IFace2;
                    while (f != null)
                    {
                        outside.Add(f);
                        f = f.GetNextFace() as IFace2;
                    }
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// La direzione di una lavorazione, se si riesce a leggerla.
        ///
        /// E' IL DATO CHE DECIDE TRE RUOLI SU QUATTRO, e dentro un piano arriva
        /// gratis perche' il passo se lo porta dietro. Qui va ricostruito dalla
        /// definizione, e non sempre c'e': una rivoluzione non ha una direzione
        /// di estrusione, un raccordo nemmeno.
        ///
        /// Si legge dalla normale del PIANO DELLO SCHIZZO, che e' il dato piu'
        /// stabile: la direzione di estrusione e' perpendicolare al profilo.
        /// </summary>
        private static double[] DirectionOf(IFeature featureItem)
        {
            try
            {
                var sketches = featureItem.GetFirstSubFeature() as IFeature;
                while (sketches != null)
                {
                    var sketch = sketches.GetSpecificFeature2() as ISketch;
                    if (sketch != null)
                    {
                        var m = sketch.ModelToSketchTransform as IMathTransform;
                        // `Inverse()` risponde un `object`, non un
                        // `IMathTransform`: senza la conversione non compila, e
                        // con un `as` sbagliato tornerebbe null in silenzio.
                        var reverse = m == null ? null : m.Inverse() as IMathTransform;
                        if (reverse != null)
                        {
                            var data = reverse.ArrayData as double[];
                            // La terza colonna della rotazione e' la normale
                            // del piano dello schizzo.
                            if (data != null && data.Length >= 9)
                                return Normalize(new[] { data[6], data[7], data[8] });
                        }
                    }
                    sketches = sketches.GetNextSubFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        private static IFace2 End(List<IFace2> faces, double[] direction, int facing)
        {
            IFace2 choice = null;
            double best = 0;
            foreach (var f in faces)
            {
                var n = Normal(f);
                if (n == null) continue;
                if (Math.Abs(Product(n, direction)) <= Cosine) continue;
                double[] box;
                try { box = f.GetBox() as double[]; } catch { continue; }
                if (box == null || box.Length < 6) continue;
                var center = new[]
                {
                    (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2,
                };
                double projection = Product(center, direction) * facing;
                if (choice == null || projection > best) { choice = f; best = projection; }
            }
            return choice;
        }

        private static bool Cylindrical(IFace2 f)
        {
            try
            {
                var s = f.IGetSurface();
                return s != null && s.IsCylinder();
            }
            catch { return false; }
        }

        private static double[] Normal(IFace2 f)
        {
            try
            {
                var s = f.IGetSurface();
                if (s == null || !s.IsPlane()) return null;
                var p = s.PlaneParams as double[];
                return p != null && p.Length >= 3 ? Normalize(new[] { p[0], p[1], p[2] }) : null;
            }
            catch { return null; }
        }

        private static double[] Normalize(double[] v)
        {
            double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (n < 1e-12) return null;
            return new[] { v[0] / n, v[1] / n, v[2] / n };
        }

        private static double Product(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }
    }
}
