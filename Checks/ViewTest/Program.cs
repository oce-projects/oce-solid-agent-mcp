using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.ViewTest
{
    /// <summary>
    /// COME SI RIMETTE A POSTO LA VISTA DI CHI LAVORA? (misura per la voce V1)
    ///
    /// `PartView` gira il pezzo per fotografarlo e **deve** rimetterlo
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
    internal static class Program
    {
        private static int _failed;

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
                string model = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Console.Error.WriteLine("parte non creata"); return 2; }

                // UN PEZZO CI VUOLE. Su un documento vuoto `ViewZoomtofit2` non
                // ha niente da inquadrare, e mezza prova non proverebbe niente.
                Block(doc, 0.2, 0.1, 0.02);
                doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
                doc.GraphicsRedraw2();
                doc.ViewZoomtofit2();
                doc.GraphicsRedraw2();

                var view = doc.ActiveView as IModelView;
                if (view == null) { Console.Error.WriteLine("nessuna vista attiva"); return 2; }

                Console.WriteLine();
                Console.WriteLine("=== come si rimette a posto la vista ===");
                Console.WriteLine();

                // ── ha senso confrontare? ────────────────────────────────
                double[] a = Numbers(view);
                Turn(doc, swStandardViews_e.swFrontView);
                double[] b = Numbers(view);
                Check("ShowNamedView2 cambia davvero i sedici numeri",
                      a != null && !Equal(a, b),
                      a == null ? "non leggibili" : "");

                // ── 1. riassegnare Transform ─────────────────────────────
                Test(doc, view, "riassegnare Transform", restore =>
                {
                    var kept = view.Transform;
                    restore(() => { try { view.Transform = kept; } catch { } });
                });

                // ── 2. Transform COPIATO (IInverse due volte) ────────────
                Test(doc, view, "Transform copiato con IInverse x2", restore =>
                {
                    var alive = view.Transform as IMathTransform;
                    var rov = alive == null ? null : alive.IInverse() as IMathTransform;
                    var copy = rov == null ? null : rov.IInverse();
                    restore(() => { try { if (copy != null) view.Transform = copy; } catch { } });
                });

                // ── 3. ViewOrientationUndo ───────────────────────────────
                Test(doc, view, "ViewOrientationUndo()", restore =>
                {
                    restore(() => { try { doc.ViewOrientationUndo(); } catch { } });
                });

                // ── 4. la terna, per riferimento ─────────────────────────
                Test(doc, view, "terna Orientation3/Translation3/Scale2 (riferimento)", restore =>
                {
                    var o = view.Orientation3;
                    var t = view.Translation3;
                    double s = view.Scale2;
                    restore(() =>
                    {
                        try { view.Orientation3 = o; } catch { }
                        try { view.Translation3 = t; } catch { }
                        try { view.Scale2 = s; } catch { }
                    });
                });

                // ── 5. la terna, COPIATA ────────────────────────────────
                Test(doc, view, "terna COPIATA (IInverse x2 + IScale 1.0)", restore =>
                {
                    var o = view.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var orCopy = rov == null ? null : rov.IInverse();
                    var t = view.Translation3 as IMathVector;
                    var tCopy = t == null ? null : t.IScale(1.0);
                    double s = view.Scale2;
                    restore(() =>
                    {
                        try { if (orCopy != null) view.Orientation3 = orCopy; } catch { }
                        try { if (tCopy != null) view.Translation3 = tCopy; } catch { }
                        try { if (s > 0) view.Scale2 = s; } catch { }
                    });
                });

                // ── 6. la terna copiata, sulla vista RILETTA ────────────
                // Se `ShowNamedView2` lasciasse stantio l'oggetto `IModelView`,
                // le scritture andrebbero su qualcosa di scollegato mentre le
                // letture continuerebbero a funzionare. Si rilegge `ActiveView`
                // prima di scrivere e si guarda se cambia qualcosa.
                Test(doc, view, "terna copiata, su ActiveView riletta", restore =>
                {
                    var o = view.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var orCopy = rov == null ? null : rov.IInverse();
                    var t = view.Translation3 as IMathVector;
                    var tCopy = t == null ? null : t.IScale(1.0);
                    double s = view.Scale2;
                    restore(() =>
                    {
                        var now = doc.ActiveView as IModelView;
                        if (now == null) return;
                        try { if (orCopy != null) now.Orientation3 = orCopy; } catch { }
                        try { if (tCopy != null) now.Translation3 = tCopy; } catch { }
                        try { if (s > 0) now.Scale2 = s; } catch { }
                    });
                });

                // ── 7. la terna copiata, in ordine O -> S -> T ──────────
                //
                // I numeri della prova 5 dicono dove sta il resto: la rotazione
                // (indici 0-8 della matrice) torna **esatta**, e sbaglia solo
                // l'indice 9, cioe' la traslazione, di 4,77. Mettendo la scala
                // per ultima si rimette una traslazione che vale per la scala
                // di prima. Qui si prova l'ordine opposto.
                Test(doc, view, "terna copiata, in ordine O -> S -> T", restore =>
                {
                    var o = view.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var orCopy = rov == null ? null : rov.IInverse();
                    var t = view.Translation3 as IMathVector;
                    var tCopy = t == null ? null : t.IScale(1.0);
                    double s = view.Scale2;
                    restore(() =>
                    {
                        try { if (orCopy != null) view.Orientation3 = orCopy; } catch { }
                        try { if (s > 0) view.Scale2 = s; } catch { }
                        try { if (tCopy != null) view.Translation3 = tCopy; } catch { }
                    });
                });

                // ── 8. la terna, applicata DUE volte ────────────────────
                // Se ogni scrittura si porta dietro un ricalcolo delle altre,
                // un secondo giro sugli stessi valori converge. Se non
                // converge, non e' un problema di ordine.
                Test(doc, view, "terna copiata, applicata due volte", restore =>
                {
                    var o = view.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var orCopy = rov == null ? null : rov.IInverse();
                    var t = view.Translation3 as IMathVector;
                    var tCopy = t == null ? null : t.IScale(1.0);
                    double s = view.Scale2;
                    restore(() =>
                    {
                        for (int benchRun = 0; benchRun < 2; benchRun++)
                        {
                            try { if (orCopy != null) view.Orientation3 = orCopy; } catch { }
                            try { if (s > 0) view.Scale2 = s; } catch { }
                            try { if (tCopy != null) view.Translation3 = tCopy; } catch { }
                        }
                    });
                });

                // ── 9. il nome della vista, rimesso ─────────────────────
                // Funziona SOLO se si partiva da una vista standard, e per
                // questo non e' una soluzione: l'orientamento di chi lavora
                // quasi mai ha un nome. Ma se e' l'unica che torna, dice che il
                // problema sta nelle scritture e non nelle letture.
                Test(doc, view, "ShowNamedView2 sulla vista di partenza", restore =>
                {
                    restore(() =>
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
                Console.WriteLine(_failed == 0
                    ? "Tutte le strade funzionano."
                    : _failed + " strade su 9 non rimettono la vista.");
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
        private static void Test(
            IModelDoc2 doc, IModelView view, string itemName, Action<Action<Action>> prepareAndPutBack)
        {
            doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
            doc.GraphicsRedraw2();
            doc.ViewZoomtofit2();
            doc.GraphicsRedraw2();

            double[] start = Numbers(view);
            Action restored = null;
            prepareAndPutBack(action => { restored = action; });

            Turn(doc, swStandardViews_e.swRightView);
            doc.ViewZoomtofit2();
            doc.GraphicsRedraw2();

            if (restored != null) restored();
            doc.GraphicsRedraw2();

            double[] final = Numbers(view);
            Check(itemName, Equal(start, final),
                  start == null ? "numeri non leggibili" : Deviation(start, final));
        }

        private static void Turn(IModelDoc2 doc, swStandardViews_e which)
        {
            doc.ShowNamedView2("", (int)which);
            doc.GraphicsRedraw2();
        }

        private static double[] Numbers(IModelView view)
        {
            try
            {
                var m = view.Transform as IMathTransform;
                return m == null ? null : m.ArrayData as double[];
            }
            catch { return null; }
        }

        private static bool Equal(double[] a, double[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
            return true;
        }

        /// <summary>Di quanto si e' sbagliato, sul numero che si e' scostato di piu'.</summary>
        private static string Deviation(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return "";
            double worse = 0;
            int location = -1;
            for (int i = 0; i < a.Length; i++)
            {
                double d = Math.Abs(a[i] - b[i]);
                if (d > worse) { worse = d; location = i; }
            }
            if (worse <= 1e-9) return "";
            return "scarto massimo " + worse.ToString("0.####") + " sul numero " + location;
        }

        private static void Check(string itemName, bool ok, string detail)
        {
            if (!ok) _failed++;
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + itemName.PadRight(52) + detail);
        }

        /// <summary>Un blocco, in metri: SOLIDWORKS lavora in metri.</summary>
        private static void Block(IModelDoc2 doc, double l, double w, double h)
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
