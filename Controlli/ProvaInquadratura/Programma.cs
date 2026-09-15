using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.ProvaInquadratura
{
    /// <summary>
    /// IL PEZZO CI STA DENTRO AL FOTOGRAMMA? (voce V5)
    ///
    /// `VistaDelPezzo.Scatta` gira il pezzo, chiama `ViewZoomtofit2` e salva un
    /// PNG 1024x768. L'isometrica esce intera e centrata. **Le tre viste
    /// standard no.**
    ///
    /// Il rosso non l'ho trovato io: l'hanno scritto due modelli mentre
    /// rispondevano ad altro, durante la misura della voce V2 —
    /// «la terza dimensione non e' determinabile perche' **esce
    /// dall'inquadratura**», «la vista risulta **tagliata sul lato destro**».
    ///
    /// **Un pezzo tagliato e' un pezzo diverso.** Un'immagine che ne nasconde
    /// meta' e' peggio di nessuna immagine, perche' chi la guarda ci ragiona
    /// sopra lo stesso — ed e' esattamente cio' che e' successo.
    ///
    /// ── COSA MISURA ───────────────────────────────────────────────────────
    ///
    /// Scatta, poi conta i PIXEL. Lo sfondo di SOLIDWORKS e' una sfumatura
    /// chiara; il pezzo e' grigio. Si prende la scatola dei pixel che NON sono
    /// sfondo e si guarda:
    ///
    ///   * **tocca un bordo?**  -> il pezzo e' tagliato, ed e' il difetto
    ///   * **quanto riempie?**  -> pixel pagati che non mostrano niente
    ///
    /// Il primo criterio e' quello vero. Il secondo e' il di piu': un'immagine
    /// costa piu' di mille token, e meta' fotogramma di sfondo e' meta' di quel
    /// costo buttata.
    ///
    ///     Controlli\ProvaInquadratura\bin\ProvaInquadratura.exe
    ///
    /// Non tocca nessun documento gia' aperto: se ne crea uno suo e lo chiude.
    /// </summary>
    internal static class Programma
    {
        private static int _falliti;
        private static int _magre;
        private static Assembly _libreria;
        private static Type _vista;

        /// <summary>
        /// Quanto vicino al bordo e' «tocca il bordo».
        ///
        /// Non zero: l'antialiasing sbava di un pixel o due, e un pezzo che
        /// arriva a due pixel dal bordo e' comunque tagliato o sul punto di
        /// esserlo. Tre pixel di franchigia su 1024 sono lo 0,3%.
        /// </summary>
        private const int Franchigia = 3;

        /// <summary>Quanto deve riempire, almeno, nella direzione migliore.</summary>
        private const double RiempimentoMinimo = 0.60;

        private static int Main(string[] argomenti)
        {
            string percorsoLibreria = argomenti.Length > 0
                ? argomenti[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Riscontro.Disegno.dll");
            if (!File.Exists(percorsoLibreria))
            {
                Console.Error.WriteLine("non trovo la libreria: " + percorsoLibreria);
                return 2;
            }
            _libreria = Assembly.Load(File.ReadAllBytes(percorsoLibreria));
            _vista = _libreria.GetType("Riscontro.Disegno.VistaDelPezzo", false);
            if (_vista == null)
            {
                Console.Error.WriteLine("VistaDelPezzo non c'e' in " + percorsoLibreria);
                return 2;
            }

            ISldWorks sw;
            try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SOLIDWORKS non agganciabile: " + ex.Message);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("=== il pezzo ci sta dentro al fotogramma? ===");
            Console.WriteLine("libreria " + _libreria.GetName().Version);
            Console.WriteLine();

            // PRIMA SI PROVA IL RIGHELLO, POI SI MISURA.
            IlRighelloCiVedeBene(sw);
            Console.WriteLine();

            // ── QUANT'E' LARGO IL VIEWPORT, DAVVERO ──────────────────────
            //
            // Si taglia sempre e solo a destra, mai in verticale e mai a
            // sinistra. L'ipotesi e' che l'area grafica a schermo sia piu'
            // LARGA di 4:3, che `ViewZoomtofit2` inquadri dentro quella, e che
            // `SaveBMP` renda poi in 1024x768 perdendo i lati.
            //
            // Un'ipotesi in questa sezione ne ha gia' sbagliate tre di fila.
            // Questa riga la chiede.
            Aspetto(sw);
            Console.WriteLine();

            // TRE FORME, SCELTE PER ESSERE SCOMODE IN MODI DIVERSI:
            //   la staffa a L e' lunga e bassa -> la frontale e' larghissima;
            //   la piastra e' larga e piatta   -> la destra e' una striscia;
            //   l'albero e' lungo e stretto    -> la frontale e' una riga.
            // Un pezzo cubico non direbbe niente: ci sta sempre.
            Prova(sw, "staffa a L 120x60x58", doc =>
            {
                Blocco(doc, 0.120, 0.060, 0.008);
            });
            Prova(sw, "piastra 200x120x15", doc =>
            {
                Blocco(doc, 0.200, 0.120, 0.015);
            });
            Prova(sw, "barra 300x20x20", doc =>
            {
                Blocco(doc, 0.300, 0.020, 0.020);
            });

            Console.WriteLine();
            Console.WriteLine(_falliti == 0
                ? "Nessuna vista tagliata."
                : _falliti + " VISTE TAGLIATE.");
            if (_magre > 0)
                Console.WriteLine(_magre + " viste riempiono meno del " +
                                  (RiempimentoMinimo * 100).ToString("0") +
                                  "%: pixel pagati che non mostrano niente. " +
                                  "Non e' un difetto, e' un costo.");
            return _falliti == 0 ? 0 : 1;
        }

        private static readonly string[] Viste =
            { "isometrica", "frontale", "superiore", "destra" };

        private static void Prova(ISldWorks sw, string nome, Action<IModelDoc2> costruisci)
        {
            IModelDoc2 doc = null;
            try
            {
                string modello = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(modello, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Segna(nome, false, "parte non creata"); return; }
                costruisci(doc);

                Console.WriteLine("  " + nome);
                foreach (var quale in Viste)
                {
                    byte[] png = Scatta(doc, quale);
                    if (png == null || png.Length == 0)
                    {
                        Segna("    " + quale, false, "niente immagine");
                        continue;
                    }
                    int primaDeiFalliti = _falliti;
                    Misura(quale, png);

                    // UNO STRUMENTO CHE DICE «TAGLIATA» DEVE FAR VEDERE COSA.
                    // La prima versione di questa misura si sbagliava — la
                    // soglia dello sfondo era presa a occhio — e l'unico modo
                    // di accorgersene e' stato GUARDARE l'immagine. Adesso, se
                    // il verdetto e' rosso, il fotogramma resta su disco.
                    if (_falliti > primaDeiFalliti) Deponi(nome, quale, png);
                }
            }
            catch (Exception ex)
            {
                Segna(nome, false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
            }
        }

        private static byte[] Scatta(IModelDoc2 doc, string quale)
        {
            var metodo = _vista.GetMethod("Scatta", BindingFlags.Public | BindingFlags.Static);
            var p = new object[] { doc, quale, 1024, 768, null };
            return metodo.Invoke(null, p) as byte[];
        }

        /// <summary>
        /// La scatola dei pixel che non sono sfondo, e il verdetto.
        ///
        /// COS'E' SFONDO. Lo sfondo di SOLIDWORKS e' una sfumatura molto chiara
        /// e quasi neutra; il pezzo e' grigio medio. Si chiama sfondo un pixel
        /// **chiaro**: la somma dei tre canali sopra una soglia. Non si cerca un
        /// colore esatto — la sfumatura cambia da pixel a pixel, ed e' proprio
        /// per quello che un confronto con un colore fisso non funzionerebbe.
        /// </summary>
        private static void Misura(string quale, byte[] png)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            int larghezza, altezza;

            using (var dentro = new MemoryStream(png))
            using (var immagine = new Bitmap(dentro))
            {
                larghezza = immagine.Width;
                altezza = immagine.Height;

                for (int y = 0; y < altezza; y++)
                    for (int x = 0; x < larghezza; x++)
                    {
                        if (!EPezzo(immagine.GetPixel(x, y))) continue;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
            }

            if (maxX < 0)
            {
                Segna("    " + quale, false, "nessun pixel di pezzo: fotogramma vuoto");
                return;
            }

            bool tocca = minX <= Franchigia || minY <= Franchigia ||
                         maxX >= larghezza - 1 - Franchigia ||
                         maxY >= altezza - 1 - Franchigia;

            double riempieX = (maxX - minX + 1) / (double)larghezza;
            double riempieY = (maxY - minY + 1) / (double)altezza;
            double riempie = Math.Max(riempieX, riempieY);

            string dettaglio = string.Format(
                "scatola x {0}-{1} y {2}-{3}, riempie {4:0}% x {5:0}%",
                minX, maxX, minY, maxY, riempieX * 100, riempieY * 100);

            // ── I DUE CRITERI NON SONO DELLO STESSO GENERE ───────────────
            //
            // **Tagliare e' un difetto. Sprecare e' uno spreco.** Un pezzo
            // tagliato e' un pezzo diverso, e chi guarda l'immagine ci ragiona
            // sopra lo stesso; un pezzo piccolo dentro un fotogramma grande e'
            // vero, solo costoso.
            //
            // Quindi l'esito dipende SOLO dal taglio. Il riempimento si stampa,
            // si vede, e non fa fallire niente — ma si stampa **sempre**, per
            // non farlo sparire: e' quello che paga chi usa il ponte.
            if (tocca)
            {
                Segna("    " + quale, false, "TAGLIATA -- " + dettaglio);
                return;
            }
            Segna("    " + quale, true, dettaglio);
            if (riempie < RiempimentoMinimo)
            {
                _magre++;
                Console.WriteLine("         (magra: riempie " +
                                  (riempie * 100).ToString("0") + "%, sotto il " +
                                  (RiempimentoMinimo * 100).ToString("0") + "% che vorremmo)");
            }
        }

        /// <summary>
        /// Questo pixel e' pezzo, o e' sfondo?
        ///
        /// ── DUE SOGLIE SBAGLIATE PRIMA DI QUESTA ─────────────────────────
        ///
        /// La prima diceva «sfondo se R+G+B > 690». La seconda ricavava la
        /// soglia dai quattro angoli. Tutte e due hanno dichiarato TAGLIATE
        /// dodici viste su dodici, **con la stessa scatola su pezzi di forma
        /// diversa** — e una misura che da' lo stesso numero su ingressi
        /// diversi non sta misurando l'ingresso.
        ///
        /// A smascherarle e' stato guardare il fotogramma: su una barra che sta
        /// fra y 358 e 410, il righello diceva y 5-443. Prendeva per pezzo la
        /// zona azzurrina della sfumatura, che e' scura di suo e **non sta agli
        /// angoli** — per questo la seconda soglia, presa dagli angoli, non l'ha
        /// vista.
        ///
        /// ── QUELLA CHE C'E' ADESSO ───────────────────────────────────────
        ///
        /// Il grigio del pezzo sta sui 128-150 su tutti e tre i canali; la
        /// sfumatura di SOLIDWORKS non scende sotto ~215 su nessuno. 200 e' in
        /// mezzo, e ci sta largo da tutte e due le parti.
        ///
        /// **Il canale piu' basso, non la somma:** un grigio medio e un azzurro
        /// chiaro possono avere la stessa somma.
        ///
        /// E soprattutto: non ci si fida: `IlRighelloCiVedeBene` lo prova su un
        /// fotogramma in cui **si sa** che di pezzo non ce n'e' nemmeno uno.
        /// </summary>
        private static bool EPezzo(Color c)
        {
            return Math.Min(c.R, Math.Min(c.G, c.B)) < 200;
        }

        /// <summary>
        /// IL CASO ROSSO DEL RIGHELLO.
        ///
        /// Un documento vuoto, fotografato: il fotogramma e' tutto sfondo, e il
        /// conto dei pixel di pezzo **deve fare zero**. Se ne trova, la soglia
        /// sta prendendo per pezzo la sfumatura, e ogni numero che questo
        /// programma stampa dopo e' senza valore.
        ///
        /// Costa uno scatto. Le due soglie sbagliate prima di questa sarebbero
        /// morte qui invece che dopo tre giri e due letture a occhio.
        /// </summary>
        private static void IlRighelloCiVedeBene(ISldWorks sw)
        {
            IModelDoc2 doc = null;
            try
            {
                string modello = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(modello, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Segna("il righello ci vede bene", false, "parte non creata"); return; }

                byte[] png = Scatta(doc, "isometrica");
                if (png == null) { Segna("il righello ci vede bene", false, "niente immagine"); return; }

                int quanti = 0;
                using (var dentro = new MemoryStream(png))
                using (var immagine = new Bitmap(dentro))
                    for (int y = 0; y < immagine.Height; y++)
                        for (int x = 0; x < immagine.Width; x++)
                            if (EPezzo(immagine.GetPixel(x, y))) quanti++;

                Segna("il righello ci vede bene", quanti == 0,
                      quanti == 0
                          ? "su un documento vuoto non vede nessun pezzo"
                          : quanti + " pixel di «pezzo» su un documento VUOTO: la soglia mente");
                if (quanti != 0) Deponi("righello", "vuoto", png);
            }
            catch (Exception ex)
            {
                Segna("il righello ci vede bene", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
            }
        }

        /// <summary>Che forma ha l'area grafica in cui SOLIDWORKS inquadra.</summary>
        private static void Aspetto(ISldWorks sw)
        {
            IModelDoc2 doc = null;
            try
            {
                string modello = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(modello, 0, 0, 0) as IModelDoc2;
                var vista = doc == null ? null : doc.ActiveView as IModelView;
                if (vista == null) { Console.WriteLine("  (area grafica non leggibile)"); return; }

                double w = vista.FrameWidth, h = vista.FrameHeight;
                double aspettoVista = h > 0 ? w / h : 0;
                double aspettoChiesto = 1024.0 / 768.0;
                Console.WriteLine(string.Format(
                    "  area grafica {0:0} x {1:0}  ->  {2:0.###}   chiesto 1024x768 -> {3:0.###}",
                    w, h, aspettoVista, aspettoChiesto));
                Console.WriteLine(string.Format(
                    "  scala della vista: {0:0.#####}   rapporto fra i due aspetti: {1:0.###}",
                    vista.Scale2, aspettoVista > 0 ? aspettoVista / aspettoChiesto : 0));
            }
            catch (Exception ex) { Console.WriteLine("  (aspetto non leggibile: " + ex.Message + ")"); }
            finally { if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } } }
        }

        /// <summary>Il fotogramma di una prova rossa, lasciato da guardare.</summary>
        private static void Deponi(string forma, string quale, byte[] png)
        {
            try
            {
                string cartella = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "fotogrammi");
                Directory.CreateDirectory(cartella);
                string pulito = forma.Replace(' ', '_').Replace('x', 'X');
                string file = Path.Combine(cartella, pulito + "-" + quale + ".png");
                File.WriteAllBytes(file, png);
                Console.WriteLine("         guardalo: " + file);
            }
            catch { }
        }

        private static void Segna(string nome, bool ok, string dettaglio)
        {
            if (!ok) _falliti++;
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + nome.PadRight(20) + dettaglio);
        }

        /// <summary>Un blocco, in metri: SOLIDWORKS lavora in metri.</summary>
        private static void Blocco(IModelDoc2 doc, double l, double w, double h)
        {
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(-l / 2, -w / 2, 0, l / 2, w / 2, 0);
            doc.SketchManager.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, h, 0, false, false, false, false,
                0, 0, false, false, false, false, true, true, true, 0, 0, false);
        }
    }
}
