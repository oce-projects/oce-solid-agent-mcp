using System;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;

namespace Checks.DimensionApis
{
    /// <summary>
    /// QUALI API PARLANO DI ANNOTAZIONI E QUOTE, lette dall'interop.
    ///
    /// Serve quando una chiamata risponde «niente» senza dire perche': prima di
    /// continuare a variare i parametri della stessa, conviene sapere se ce
    /// n'e' un'altra. `InsertModelAnnotations3` ha risposto zero con cinque
    /// valori di fonte, quattro maschere di tipo, la vista attivata e la vista
    /// selezionata: a quel punto la domanda non e' piu' «quale parametro» ma
    /// «quale metodo».
    ///
    /// NON APRE SOLIDWORKS. Legge solo i metadati dell'interop, quindi si puo'
    /// lanciare anche con il programma chiuso — ed e' anche il motivo per cui
    /// cio' che trova va poi provato vivo: una firma che esiste non e' una
    /// chiamata che funziona.
    ///
    ///     DimensionApis.exe [parola]      predefinito: annotation
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            string word = arguments.Length > 0 ? arguments[0] : "annotation";
            Console.WriteLine("Cerco «" + word + "» nei metodi dell'interop.");
            Console.WriteLine();

            // LE COSTANTI, non solo i metodi. Un'opzione di sistema che non si
            // conosce e' indistinguibile da una che non esiste.
            var constants = typeof(SolidWorks.Interop.swconst.swUserPreferenceToggle_e).Assembly;
            foreach (var e in new[] { "swUserPreferenceToggle_e", "swInsertAnnotation_e", "swFeatureError_e",
                                      "swImportModelItemsSource_e", "swUserPreferenceIntegerValue_e" })
            {
                var te = constants.GetType("SolidWorks.Interop.swconst." + e);
                if (te == null) continue;
                var foundItems = Enum.GetNames(te)
                    .Where(n => n.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(n => n).ToList();
                if (foundItems.Count == 0) continue;
                Console.WriteLine("=== " + e);
                foreach (var n in foundItems)
                    Console.WriteLine("  " + n + " = " + Convert.ToInt64(Enum.Parse(te, n)));
                Console.WriteLine();
            }

            var assembly = typeof(ISldWorks).Assembly;
            var types = new[]
            {
                "IDrawingDoc", "IView", "IModelDocExtension", "IModelDoc2",
                "IAnnotation", "IDisplayDimension", "ISheet",
                "IFace2", "IBody2", "ISurface", "IEdge",
            };

            foreach (var itemName in types)
            {
                var t = assembly.GetType("SolidWorks.Interop.sldworks." + itemName);
                if (t == null) { Console.WriteLine(itemName + ": non c'e'"); continue; }

                var methods = t.GetMethods()
                    .Where(m => m.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(m => m.Name)
                    .ToList();
                var property = t.GetProperties()
                    .Where(p => p.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(p => p.Name)
                    .ToList();
                if (methods.Count == 0 && property.Count == 0) continue;

                Console.WriteLine("=== " + itemName);
                foreach (var m in methods)
                {
                    var par = string.Join(", ", m.GetParameters()
                        .Select(p => Short(p.ParameterType.Name) + " " + p.Name));
                    Console.WriteLine("  " + Short(m.ReturnType.Name) + " " + m.Name + "(" + par + ")");
                }
                foreach (var p in property)
                    Console.WriteLine("  [prop] " + Short(p.PropertyType.Name) + " " + p.Name);
                Console.WriteLine();
            }
            return 0;
        }

        private static string Short(string itemName)
        {
            if (itemName == "Object") return "obj";
            if (itemName == "Boolean") return "bool";
            if (itemName == "Int32") return "int";
            if (itemName == "Double") return "double";
            if (itemName == "String") return "string";
            if (itemName == "Void") return "void";
            return itemName;
        }
    }
}
