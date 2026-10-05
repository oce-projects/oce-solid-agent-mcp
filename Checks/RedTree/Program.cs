using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.RedTree
{
    /// <summary>
    /// COSA DICE SOLIDWORKS DI UN ALBERO ROTTO, e se lo dice.
    ///
    /// `inspect_part` oggi descrive come normale un pezzo il cui albero e'
    /// rosso: il modello guarda un pezzo rotto e vede un pezzo sano. E' un
    /// difetto che non da' errore **dentro gli occhiali**, ed e' la voce D6.
    ///
    /// Prima di aggiungere un campo allo stato bisogna sapere che cosa ci si
    /// puo' mettere dentro, e qui si guarda:
    ///
    ///   * `IModelDocExtension.NeedsRebuild2` — il documento va ricostruito?
    ///   * `GetWhatsWrongCount()` / `GetWhatsWrong()` — l'elenco ufficiale dei
    ///     guai, quello che SOLIDWORKS mostra nella finestra;
    ///   * `IFeature.GetErrorCode2()` e `IsWarning` — feature per feature.
    ///
    /// SI SPEGNE LA FINESTRA DEGLI ERRORI (`ShowFeatureErrorDialog`). E' la
    /// stessa lezione di `swInputDimValOnCreate`: un'API che apre un modale
    /// blocca la chiamata COM per sempre, e un banco non sorvegliato resta li'
    /// tutta la notte. Si rimette com'era in ogni caso.
    ///
    ///     RedTree.exe        guarda il pezzo attivo
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 2; }
            Console.WriteLine("documento: " + doc.GetTitle());

            bool showed = true;
            try
            {
                showed = sw.GetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild);
                sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild, false);
                Console.WriteLine("finestra degli errori: era " + showed + ", spenta");
            }
            catch (Exception e) { Console.WriteLine("finestra degli errori: " + e.Message); }

            try
            {
                Console.WriteLine("NeedsRebuild2: " + doc.Extension.NeedsRebuild2);
            }
            catch (Exception e) { Console.WriteLine("NeedsRebuild2: " + e.Message); }

            // L'ELENCO UFFICIALE DEI GUAI.
            try
            {
                int howMany = doc.Extension.GetWhatsWrongCount();
                Console.WriteLine("GetWhatsWrongCount: " + howMany);
                if (howMany > 0)
                {
                    // La firma vera di questa installazione e' a TRE uscite,
                    // non a cinque: `GetWhatsWrong(out Features, out
                    // ErrorCodes, out Warnings)`. Letta con ControlliApiQuote,
                    // non ricordata.
                    object names = null, errors = null, warnings = null;
                    doc.Extension.GetWhatsWrong(out names, out errors, out warnings);
                    var vn = names as object[];
                    var ve = errors as int[];
                    var goes = warnings as bool[];
                    for (int i = 0; vn != null && i < vn.Length; i++)
                        Console.WriteLine("  guaio " + i + ": «" + vn[i] + "»" +
                            (ve != null && i < ve.Length
                                ? ", errore " + ve[i] + " (" + Explain(ve[i]) + ")" : "") +
                            (goes != null && i < goes.Length ? ", avviso " + goes[i] : ""));
                }
            }
            catch (Exception e) { Console.WriteLine("GetWhatsWrong: " + e.Message); }

            // FEATURE PER FEATURE.
            Console.WriteLine();
            Console.WriteLine("=== L'albero");
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
            catch (Exception e) { Console.WriteLine("giro dell'albero: " + e.Message); }

            try
            {
                sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild, showed);
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Il codice tradotto, quando si sa. Quello che non si sa si lascia
        /// come numero invece di inventargli un nome: un nome sbagliato su un
        /// codice e' peggio del numero nudo, perche' sembra una spiegazione.
        /// </summary>
        private static string Explain(int code)
        {
            if (Enum.IsDefined(typeof(swFeatureError_e), code))
                return ((swFeatureError_e)code).ToString();
            return "codice " + code + ", non nell'enum";
        }
    }
}
