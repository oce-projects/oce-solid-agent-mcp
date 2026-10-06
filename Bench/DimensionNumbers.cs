using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// IS THE NUMBER THAT IS PRINTED THE MODEL'S? (entry Z-C2)
    ///
    /// «The dimension comes from the model, it cannot lie» holds as long as nobody
    /// writes over the number. The drawing's state counts, from library
    /// 0.118.0, the dimensions whose printed number is not the model's
    /// (`dimensions_with_number_not_from_model`), with the two readings measured on
    /// 25/9/2026: `GetOverride` and `ShowDimensionValue`.
    ///
    /// A declared case cannot do it: no catalog operation
    /// overwrites a dimension, and it must not. Here three dimensions are falsified by hand,
    /// one per road — overwritten number, text in place of the value, value
    /// turned off — and the state must report three. Before, on the intact drawing, zero.
    /// </summary>
    internal static partial class Program
    {
        private static void TestDimensionNumbers()
        {
            Console.WriteLine();
            Console.WriteLine("=== numbers: is the printed number the model's? (Z-C2) ===");
            IModelDoc2 part = null, drawing = null;
            string file = Path.Combine(Expectations.Work, "numbers_bench.sldprt");
            try
            {
                part = NewPart();
                if (Extrusion(part, 0, 0, 0.120, 0.080, 0.010) == null)
                { BenchError("numbers", "the plate did not come out"); return; }
                try { if (File.Exists(file)) File.Delete(file); } catch { }
                int e = 0, w = 0;
                part.Extension.SaveAs(file, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
                if (!File.Exists(file)) { BenchError("numbers", "the part cannot be saved (e=" + e + ")"); return; }

                string error;
                Run(part, "create_drawing", new Dictionary<string, object> { ["paper_size"] = "A3" },
                       out error, out drawing);
                if (drawing == null) { BenchError("numbers", "create_drawing: " + error); return; }
                if (Run(drawing, "standard_views", new Dictionary<string, object> { ["model"] = file }, out error) == null)
                { BenchError("numbers", "standard_views: " + error); return; }
                foreach (var view in new[] { "V1", "V2" })
                    if (Run(drawing, "drawing_dimension", new Dictionary<string, object> { ["view"] = view }, out error) == null)
                    { BenchError("numbers", "drawing_dimension " + view + ": " + error); return; }

                var before = ReadState(drawing);
                object zero = before.ContainsKey("dimensions_with_number_not_from_model_total")
                    ? before["dimensions_with_number_not_from_model_total"] : "(missing)";
                Check("numbers: on the freshly dimensioned drawing no dimension prints a number that is not the model's",
                      Convert.ToString(zero) == "0" && !before.ContainsKey("warning_numbers"),
                      "dimensions_with_number_not_from_model_total " + Convert.ToString(zero));

                // THREE DIMENSIONS, ONE PER ROAD. The first three of the sheet are taken,
                // view after view: which ones they are does not matter, what matters is that there are three.
                var dims = new List<IDisplayDimension>();
                var v = (((IDrawingDoc)drawing).GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null && dims.Count < 3)
                {
                    var q = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (q != null && dims.Count < 3) { dims.Add(q); q = q.GetNext5() as IDisplayDimension; }
                    v = v.GetNextView() as IView;
                }
                if (dims.Count < 3) { BenchError("numbers", "the drawing has " + dims.Count + " dimensions, three are needed"); return; }

                // THE OVERWRITTEN NUMBER MATCHES THE MODEL, on purpose: it is the case
                // that the entry feared most — a comparison of values does not see it.
                double its = dims[0].GetDimension2(0).GetSystemValue2("");
                dims[0].SetOverride(true, its);
                dims[1].SetText((int)swDimensionTextParts_e.swDimensionTextAll, "99");
                dims[2].ShowDimensionValue = false;
                drawing.ForceRebuild3(false);

                var after = ReadState(drawing);
                object three = after.ContainsKey("dimensions_with_number_not_from_model_total")
                    ? after["dimensions_with_number_not_from_model_total"] : "(missing)";
                var list = after.ContainsKey("dimensions_with_number_not_from_model")
                    ? after["dimensions_with_number_not_from_model"] as IEnumerable : null;
                int overwritten = 0, withText = 0, off = 0;
                if (list != null)
                    foreach (var o in list)
                    {
                        var entry = o as Dictionary<string, object>;
                        string come = entry != null && entry.ContainsKey("how") ? Convert.ToString(entry["how"]) : "";
                        if (come.StartsWith("overwritten number", StringComparison.Ordinal)) overwritten++;
                        else if (come.Contains("«99»")) withText++;
                        else if (come.StartsWith("value turned off", StringComparison.Ordinal)) off++;
                    }
                Check("numbers: three dimensions falsified (overwritten with its own value, text, turned off), the state reports three and warns",
                      Convert.ToString(three) == "3" && overwritten == 1 && withText == 1 && off == 1 &&
                      after.ContainsKey("warning_numbers"),
                      "total " + Convert.ToString(three) + ": overwritten " + overwritten + ", with text " + withText +
                      ", turned off " + off + ", warning " + (after.ContainsKey("warning_numbers") ? "yes" : "no"));
            }
            catch (Exception ex) { BenchError("numbers", Why(ex)); }
            finally
            {
                try { if (drawing != null) _sw.CloseDoc(drawing.GetTitle()); } catch { }
                Close(part);
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }
    }
}
