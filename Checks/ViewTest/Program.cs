using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.ViewTest
{
    /// <summary>
    /// COME SI RIMETTE A POSTO LA VISTA DI CHI LAVORA? (misura per la voce V1)
    ///
    /// `VistaDelPezzo` gira il pezzo per fotografarlo e **deve** rimetterlo
    /// com'era: il ponte si aggancia al documento che un progettista ha davanti,
    /// e lasciarglielo girato e' un danno vero fatto da un'operazione di sola
    /// lettura.
    ///
    /// Il 15/9/2026 tre strade sono state provate e bocciate, **una per giro di
    /// banco da diciassette minuti**:
    ///
    ///   1. tenere `IModelView.Transform` e riassegnarlo      -> non torna
    ///   2. `IModelDoc2.ViewOrientationUndo()`                -> non torna
    ///   3. la terna Orientation3 + Translation3 + Scale2     -> non torna
    ///
    /// **Questo attrezzo esiste perche' quel prezzo era assurdo.** La domanda
    /// dura tre secondi: apre una parte, ci mette un blocco, gira la vista,
    /// prova a rimetterla in tutti i modi che l'API offre, e stampa quali
    /// funzionano.
    ///
    ///     Checks\ViewTest\bin\ViewTest.exe
    ///
    /// Non tocca nessun documento gia' aperto: se ne crea uno suo e lo chiude.
    ///
    /// COSA CONFRONTA. I sedici numeri di `IModelView.Transform`, che e' l'unica
    /// descrizione leggibile di «come si vede il pezzo adesso» — ed e' gia'
    /// misurato che cambia quando la vista cambia, quindi il confronto ha un
    /// ingresso vero.
    /// </summary>
    internal static class Programma
    {
        private static int _falliti;

        private static int Main()
        {
            ISldWorks sw;
            try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SOLIDWORKS non agganciabile: " + ex.Message);
                return 2;
            }

            IModelDoc2 doc = null;
            try
            {
                string modello = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(modello, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Console.Error.WriteLine("parte non creata"); return 2; }

                // UN PEZZO CI VUOLE. Su un documento vuoto `ViewZoomtofit2` non
                // ha niente da inquadrare, e mezza prova non proverebbe niente.
                Blocco(doc, 0.2, 0.1, 0.02);
                doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
                doc.GraphicsRedraw2();
                doc.ViewZoomtofit2();
                doc.GraphicsRedraw2();

                var vista = doc.ActiveView as IModelView;
                if (vista == null) { Console.Error.WriteLine("nessuna vista attiva"); return 2; }

                Console.WriteLine();
                Console.WriteLine("=== come si rimette a posto la vista ===");
                Console.WriteLine();

                // ── ha senso confrontare? ────────────────────────────────
                double[] a = Numeri(vista);
                Gira(doc, swStandardViews_e.swFrontView);
                double[] b = Numeri(vista);
                Segna("ShowNamedView2 cambia davvero i sedici numeri",
                      a != null && !Uguali(a, b),
                      a == null ? "non leggibili" : "");

                // ── 1. riassegnare Transform ─────────────────────────────
                Prova(doc, vista, "riassegnare Transform", ripristina =>
                {
                    var tenuto = vista.Transform;
                    ripristina(() => { try { vista.Transform = tenuto; } catch { } });
                });

                // ── 2. Transform COPIATO (IInverse due volte) ────────────
                Prova(doc, vista, "Transform copiato con IInverse x2", ripristina =>
                {
                    var vivo = vista.Transform as IMathTransform;
                    var rov = vivo == null ? null : vivo.IInverse() as IMathTransform;
                    var copia = rov == null ? null : rov.IInverse();
                    ripristina(() => { try { if (copia != null) vista.Transform = copia; } catch { } });
                });

                // ── 3. ViewOrientationUndo ───────────────────────────────
                Prova(doc, vista, "ViewOrientationUndo()", ripristina =>
                {
                    ripristina(() => { try { doc.ViewOrientationUndo(); } catch { } });
                });

                // ── 4. la terna, per riferimento ─────────────────────────
                Prova(doc, vista, "terna Orientation3/Translation3/Scale2 (riferimento)", ripristina =>
                {
                    var o = vista.Orientation3;
                    var t = vista.Translation3;
                    double s = vista.Scale2;
                    ripristina(() =>
                    {
                        try { vista.Orientation3 = o; } catch { }
                        try { vista.Translation3 = t; } catch { }
                        try { vista.Scale2 = s; } catch { }
                    });
                });

                // ── 5. la terna, COPIATA ────────────────────────────────
                Prova(doc, vista, "terna COPIATA (IInverse x2 + IScale 1.0)", ripristina =>
                {
                    var o = vista.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var oCopia = rov == null ? null : rov.IInverse();
                    var t = vista.Translation3 as IMathVector;
                    var tCopia = t == null ? null : t.IScale(1.0);
                    double s = vista.Scale2;
                    ripristina(() =>
                    {
                        try { if (oCopia != null) vista.Orientation3 = oCopia; } catch { }
                        try { if (tCopia != null) vista.Translation3 = tCopia; } catch { }
                        try { if (s > 0) vista.Scale2 = s; } catch { }
                    });
                });

                // ── 6. la terna copiata, sulla vista RILETTA ────────────
                // Se `ShowNamedView2` lasciasse stantio l'oggetto `IModelView`,
                // le scritture andrebbero su qualcosa di scollegato mentre le
                // letture continuerebbero a funzionare. Si rilegge `ActiveView`
                // prima di scrivere e si guarda se cambia qualcosa.
                Prova(doc, vista, "terna copiata, su ActiveView riletta", ripristina =>
                {
                    var o = vista.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var oCopia = rov == null ? null : rov.IInverse();
                    var t = vista.Translation3 as IMathVector;
                    var tCopia = t == null ? null : t.IScale(1.0);
                    double s = vista.Scale2;
                    ripristina(() =>
                    {
                        var adesso = doc.ActiveView as IModelView;
                        if (adesso == null) return;
                        try { if (oCopia != null) adesso.Orientation3 = oCopia; } catch { }
                        try { if (tCopia != null) adesso.Translation3 = tCopia; } catch { }
                        try { if (s > 0) adesso.Scale2 = s; } catch { }
                    });
                });

                // ── 7. la terna copiata, in ordine O -> S -> T ──────────
                //
                // I numeri della prova 5 dicono dove sta il resto: la rotazione
                // (indici 0-8 della matrice) torna **esatta**, e sbaglia solo
                // l'indice 9, cioe' la traslazione, di 4,77. Mettendo la scala
                // per ultima si rimette una traslazione che vale per la scala
                // di prima. Qui si prova l'ordine opposto.
                Prova(doc, vista, "terna copiata, in ordine O -> S -> T", ripristina =>
                {
                    var o = vista.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var oCopia = rov == null ? null : rov.IInverse();
                    var t = vista.Translation3 as IMathVector;
                    var tCopia = t == null ? null : t.IScale(1.0);
                    double s = vista.Scale2;
                    ripristina(() =>
                    {
                        try { if (oCopia != null) vista.Orientation3 = oCopia; } catch { }
                        try { if (s > 0) vista.Scale2 = s; } catch { }
                        try { if (tCopia != null) vista.Translation3 = tCopia; } catch { }
                    });
                });

                // ── 8. la terna, applicata DUE volte ────────────────────
                // Se ogni scrittura si porta dietro un ricalcolo delle altre,
                // un secondo giro sugli stessi valori converge. Se non
                // converge, non e' un problema di ordine.
                Prova(doc, vista, "terna copiata, applicata due volte", ripristina =>
                {
                    var o = vista.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var oCopia = rov == null ? null : rov.IInverse();
                    var t = vista.Translation3 as IMathVector;
                    var tCopia = t == null ? null : t.IScale(1.0);
                    double s = vista.Scale2;
                    ripristina(() =>
                    {
                        for (int giro = 0; giro < 2; giro++)
                        {
                            try { if (oCopia != null) vista.Orientation3 = oCopia; } catch { }
                            try { if (s > 0) vista.Scale2 = s; } catch { }
                            try { if (tCopia != null) vista.Translation3 = tCopia; } catch { }
                        }
                    });
                });

                // ── 9. il nome della vista, rimesso ─────────────────────
                // Funziona SOLO se si partiva da una vista standard, e per
                // questo non e' una soluzione: l'orientamento di chi lavora
                // quasi mai ha un nome. Ma se e' l'unica che torna, dice che il
                // problema sta nelle scritture e non nelle letture.
                Prova(doc, vista, "ShowNamedView2 sulla vista di partenza", ripristina =>
                {
                    ripristina(() =>
                    {
                        try
                        {
                            doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
                            doc.ViewZoomtofit2();
                        }
                        catch { }
                    });
                });

                Console.WriteLine();
                Console.WriteLine(_falliti == 0
                    ? "Tutte le strade funzionano."
                    : _falliti + " strade su 9 non rimettono la vista.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("la prova si e' rotta: " + ex.GetType().Name +
                                        ": " + ex.Message);
                return 2;
            }
            finally
            {
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
            }
        }

        /// <summary>
        /// Una strada: si riparte SEMPRE dall'isometrica inquadrata, si tiene
        /// nota di com'e', si gira e si zooma, si prova a rimettere, si
        /// confronta.
        ///
        /// La partenza si rifa' a ogni giro perche' la strada precedente puo'
        /// aver lasciato la vista da un'altra parte, e allora la prova
        /// successiva partirebbe da un posto che non conosce.
        /// </summary>
        private static void Prova(
            IModelDoc2 doc, IModelView vista, string nome, Action<Action<Action>> preparaERimetti)
        {
            doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
            doc.GraphicsRedraw2();
            doc.ViewZoomtofit2();
            doc.GraphicsRedraw2();

            double[] partenza = Numeri(vista);
            Action rimessa = null;
            preparaERimetti(azione => { rimessa = azione; });

            Gira(doc, swStandardViews_e.swRightView);
            doc.ViewZoomtofit2();
            doc.GraphicsRedraw2();

            if (rimessa != null) rimessa();
            doc.GraphicsRedraw2();

            double[] finale = Numeri(vista);
            Segna(nome, Uguali(partenza, finale),
                  partenza == null ? "numeri non leggibili" : Scarto(partenza, finale));
        }

        private static void Gira(IModelDoc2 doc, swStandardViews_e quale)
        {
            doc.ShowNamedView2("", (int)quale);
            doc.GraphicsRedraw2();
        }

        private static double[] Numeri(IModelView vista)
        {
            try
            {
                var m = vista.Transform as IMathTransform;
                return m == null ? null : m.ArrayData as double[];
            }
            catch { return null; }
        }

        private static bool Uguali(double[] a, double[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
            return true;
        }

        /// <summary>Di quanto si e' sbagliato, sul numero che si e' scostato di piu'.</summary>
        private static string Scarto(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return "";
            double peggio = 0;
            int dove = -1;
            for (int i = 0; i < a.Length; i++)
            {
                double d = Math.Abs(a[i] - b[i]);
                if (d > peggio) { peggio = d; dove = i; }
            }
            if (peggio <= 1e-9) return "";
            return "scarto massimo " + peggio.ToString("0.####") + " sul numero " + dove;
        }

        private static void Segna(string nome, bool ok, string dettaglio)
        {
            if (!ok) _falliti++;
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + nome.PadRight(52) + dettaglio);
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
