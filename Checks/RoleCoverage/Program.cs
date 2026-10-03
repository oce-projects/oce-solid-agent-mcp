using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.CoperturaRuoli
{
    /// <summary>
    /// QUANTE FACCE HANNO UN RUOLO UNIVOCO? (misura che dimensiona la voce E7)
    ///
    /// E7 propone di nominare una faccia per SIGNIFICATO invece che per
    /// posizione: «il fondo della tasca fatta al passo 2» resta il fondo della
    /// tasca qualunque quota si cambi a monte.
    ///
    /// Meta' del meccanismo esiste gia' e funziona, in `Riferimenti.cs`, ma
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
    ///     Controlli\CoperturaRuoli\bin\CoperturaRuoli.exe
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
    internal static class Programma
    {
        /// <summary>
        /// Quanto una normale deve essere allineata alla direzione per contare
        /// come «perpendicolare al taglio». Stesso valore di `Riferimenti`.
        /// </summary>
        private const double Coseno = 0.99;

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

            var facce = TutteLeFacce(doc);
            Console.WriteLine("  facce totali: " + facce.Count);

            // ── a quale lavorazione appartiene ogni faccia ──────────────────
            var perLavorazione = new Dictionary<int, List<IFace2>>();
            var nomeLavorazione = new Dictionary<int, string>();
            var direzione = new Dictionary<int, double[]>();
            int senzaLavorazione = 0;

            foreach (var f in facce)
            {
                IFeature lav = null;
                try { lav = f.GetFeature() as IFeature; } catch { }
                if (lav == null) { senzaLavorazione++; continue; }

                int id;
                try { id = lav.GetID(); } catch { senzaLavorazione++; continue; }

                if (!perLavorazione.ContainsKey(id))
                {
                    perLavorazione[id] = new List<IFace2>();
                    try { nomeLavorazione[id] = lav.Name; } catch { nomeLavorazione[id] = "?"; }
                    direzione[id] = DirezioneDi(lav);
                }
                perLavorazione[id].Add(f);
            }

            Console.WriteLine("  senza lavorazione leggibile: " + senzaLavorazione);
            Console.WriteLine("  lavorazioni con facce: " + perLavorazione.Count);
            Console.WriteLine();

            // ── il ruolo di ogni faccia, lavorazione per lavorazione ────────
            int univoche = 0, ambigue = 0, senzaRuolo = 0, senzaDirezione = 0;
            var conteggioRuoli = new Dictionary<string, int>();

            foreach (var voce in perLavorazione.OrderBy(v => v.Key))
            {
                var suo = voce.Value;
                var dir = direzione[voce.Key];
                var ruoloDi = new Dictionary<IFace2, string>();

                // hole_face: l'unica che NON ha bisogno della direzione.
                var cilindriche = suo.Where(Cilindrica).ToList();
                if (cilindriche.Count == 1) ruoloDi[cilindriche[0]] = "hole_face";

                if (dir != null)
                {
                    var sopra = Estremo(suo, dir, +1);
                    var sotto = Estremo(suo, dir, -1);
                    if (sopra != null && !ruoloDi.ContainsKey(sopra))
                        ruoloDi[sopra] = "top_face";
                    if (sotto != null && !ruoloDi.ContainsKey(sotto) && !ReferenceEquals(sotto, sopra))
                        ruoloDi[sotto] = "bottom_face";
                }

                int quanteQui = 0;
                foreach (var f in suo)
                {
                    string r;
                    if (ruoloDi.TryGetValue(f, out r))
                    {
                        univoche++;
                        quanteQui++;
                        conteggioRuoli[r] = (conteggioRuoli.ContainsKey(r) ? conteggioRuoli[r] : 0) + 1;
                    }
                    else if (dir == null) senzaDirezione++;
                    else senzaRuolo++;
                }

                // PIU' DI UNA FACCIA CILINDRICA NON E' «NESSUN RUOLO»: e'
                // un'ambiguita', e in `Riferimenti` fa scattare un rifiuto
                // invece di una scelta. Contarla insieme alle facce che un
                // ruolo non ce l'hanno proprio confonderebbe due problemi
                // diversi: uno si risolve aggiungendo vocabolario, l'altro no.
                if (cilindriche.Count > 1) ambigue += cilindriche.Count;

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-34} {1,2} facce, {2} con ruolo{3}",
                    Corto(nomeLavorazione[voce.Key], 34), suo.Count, quanteQui,
                    dir == null ? "   [direzione non leggibile]" : ""));
            }

            // ── il numero che la voce E7 chiede ────────────────────────────
            Console.WriteLine();
            Console.WriteLine("=== Il numero");
            int totale = facce.Count;
            Console.WriteLine("  con ruolo univoco:            " + Riga(univoche, totale));
            Console.WriteLine("  ambigue (piu' cilindriche):   " + Riga(ambigue, totale));
            Console.WriteLine("  senza ruolo (geometria):      " + Riga(senzaRuolo, totale));
            Console.WriteLine("  senza ruolo (manca direzione):" + Riga(senzaDirezione, totale));
            Console.WriteLine();
            foreach (var r in conteggioRuoli.OrderByDescending(x => x.Value))
                Console.WriteLine("      " + r.Key.PadRight(20) + r.Value);

            Console.WriteLine();
            double quota = totale > 0 ? univoche * 100.0 / totale : 0;
            Console.WriteLine(quota >= 50
                ? "  Sopra meta': per questo pezzo la strada del ruolo regge."
                : "  SOTTO META': per questo pezzo il ruolo e' un complemento, non la strada.");

            return 0;
        }

        private static string Riga(int quante, int totale)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0,3}  ({1,5:0.0}%)",
                quante, totale > 0 ? quante * 100.0 / totale : 0);
        }

        private static string Corto(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Length <= n ? s : s.Substring(0, n - 3) + "...";
        }

        private static List<IFace2> TutteLeFacce(IModelDoc2 doc)
        {
            var fuori = new List<IFace2>();
            try
            {
                var parte = doc as IPartDoc;
                if (parte == null) return fuori;
                var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (corpi == null) return fuori;
                foreach (var c in corpi)
                {
                    var corpo = c as IBody2;
                    if (corpo == null) continue;
                    var f = corpo.GetFirstFace() as IFace2;
                    while (f != null)
                    {
                        fuori.Add(f);
                        f = f.GetNextFace() as IFace2;
                    }
                }
            }
            catch { }
            return fuori;
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
        private static double[] DirezioneDi(IFeature lavorazione)
        {
            try
            {
                var schizzi = lavorazione.GetFirstSubFeature() as IFeature;
                while (schizzi != null)
                {
                    var schizzo = schizzi.GetSpecificFeature2() as ISketch;
                    if (schizzo != null)
                    {
                        var m = schizzo.ModelToSketchTransform as IMathTransform;
                        // `Inverse()` risponde un `object`, non un
                        // `IMathTransform`: senza la conversione non compila, e
                        // con un `as` sbagliato tornerebbe null in silenzio.
                        var inverso = m == null ? null : m.Inverse() as IMathTransform;
                        if (inverso != null)
                        {
                            var dati = inverso.ArrayData as double[];
                            // La terza colonna della rotazione e' la normale
                            // del piano dello schizzo.
                            if (dati != null && dati.Length >= 9)
                                return Normalizza(new[] { dati[6], dati[7], dati[8] });
                        }
                    }
                    schizzi = schizzi.GetNextSubFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        private static IFace2 Estremo(List<IFace2> facce, double[] direzione, int verso)
        {
            IFace2 scelta = null;
            double migliore = 0;
            foreach (var f in facce)
            {
                var n = Normale(f);
                if (n == null) continue;
                if (Math.Abs(Prodotto(n, direzione)) <= Coseno) continue;
                double[] box;
                try { box = f.GetBox() as double[]; } catch { continue; }
                if (box == null || box.Length < 6) continue;
                var centro = new[]
                {
                    (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2,
                };
                double proiezione = Prodotto(centro, direzione) * verso;
                if (scelta == null || proiezione > migliore) { scelta = f; migliore = proiezione; }
            }
            return scelta;
        }

        private static bool Cilindrica(IFace2 f)
        {
            try
            {
                var s = f.IGetSurface();
                return s != null && s.IsCylinder();
            }
            catch { return false; }
        }

        private static double[] Normale(IFace2 f)
        {
            try
            {
                var s = f.IGetSurface();
                if (s == null || !s.IsPlane()) return null;
                var p = s.PlaneParams as double[];
                return p != null && p.Length >= 3 ? Normalizza(new[] { p[0], p[1], p[2] }) : null;
            }
            catch { return null; }
        }

        private static double[] Normalizza(double[] v)
        {
            double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (n < 1e-12) return null;
            return new[] { v[0] / n, v[1] / n, v[2] / n };
        }

        private static double Prodotto(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }
    }
}
