using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.RedTree
{
    /// <summary>
    /// WHAT SOLIDWORKS SAYS ABOUT A BROKEN TREE, and whether it says it.
    ///
    /// `inspect_part` today describes as normal a part whose tree is
    /// red: the model looks at a broken part and sees a healthy part. It is a
    /// defect that raises no error **inside the glasses**, and it is entry D6.
    ///
    /// Before adding a field to the state we need to know what can be
    /// put inside it, and here we look:
    ///
    ///   * `IModelDocExtension.NeedsRebuild2` — does the document need rebuilding?
    ///   * `GetWhatsWrongCount()` / `GetWhatsWrong()` — the official list of
    ///     troubles, the one that SOLIDWORKS shows in the window;
    ///   * `IFeature.GetErrorCode2()` and `IsWarning` — feature by feature.
    ///
    /// THE ERROR WINDOW IS TURNED OFF (`ShowFeatureErrorDialog`). It is the
    /// same lesson as `swInputDimValOnCreate`: an API that opens a modal
    /// blocks the COM call forever, and an unattended bench stays there
    /// all night. It is put back as it was in any case.
    ///
    ///     RedTree.exe        looks at the active part
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("No active document."); return 2; }
            Console.WriteLine("documento: " + doc.GetTitle());

            bool showed = true;
            try
            {
                showed = sw.GetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild);
                sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild, false);
                Console.WriteLine("errors window: it was " + showed + ", turned off");
            }
            catch (Exception e) { Console.WriteLine("errors window: " + e.Message); }

            try
            {
                Console.WriteLine("NeedsRebuild2: " + doc.Extension.NeedsRebuild2);
            }
            catch (Exception e) { Console.WriteLine("NeedsRebuild2: " + e.Message); }

            // THE OFFICIAL LIST OF TROUBLES.
            try
            {
                int howMany = doc.Extension.GetWhatsWrongCount();
                Console.WriteLine("GetWhatsWrongCount: " + howMany);
                if (howMany > 0)
                {
                    // The real signature of this installation has THREE outputs,
                    // not five: `GetWhatsWrong(out Features, out
                    // ErrorCodes, out Warnings)`. Read with ControlliApiQuote,
                    // not remembered.
                    object names = null, errors = null, warnings = null;
                    doc.Extension.GetWhatsWrong(out names, out errors, out warnings);
                    var vn = names as object[];
                    var ve = errors as int[];
                    var goes = warnings as bool[];
                    for (int i = 0; vn != null && i < vn.Length; i++)
                        Console.WriteLine("  guaio " + i + ": «" + vn[i] + "»" +
                            (ve != null && i < ve.Length
                                ? ", error " + ve[i] + " (" + Explain(ve[i]) + ")" : "") +
                            (goes != null && i < goes.Length ? ", warning " + goes[i] : ""));
                }
            }
            catch (Exception e) { Console.WriteLine("GetWhatsWrong: " + e.Message); }

            // FEATURE PER FEATURE.
            Console.WriteLine();
            Console.WriteLine("=== The tree");
            try
            {
                var f = doc.FirstFeature() as IFeature;
                int n = 0;
                while (f != null && n < 100)
                {
                    n++;
                    int code = -1;
                    bool warning = false;
                    try { code = f.GetErrorCode2(out warning); } catch { }
                    string errorName = code == 0 ? "" : "  <-- " + Explain(code);
                    Console.WriteLine("  " + f.Name + " [" + f.GetTypeName2() + "]" +
                        " errore=" + code + " avviso=" + warning + errorName);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch (Exception e) { Console.WriteLine("tree loop: " + e.Message); }

            try
            {
                sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild, showed);
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// The translated code, when it is known. What is not known is left
        /// as a number instead of inventing a name for it: a wrong name on a
        /// code is worse than the bare number, because it looks like an explanation.
        /// </summary>
        private static string Explain(int code)
        {
            if (Enum.IsDefined(typeof(swFeatureError_e), code))
                return ((swFeatureError_e)code).ToString();
            return "code " + code + ", not in the enum";
        }
    }
}
