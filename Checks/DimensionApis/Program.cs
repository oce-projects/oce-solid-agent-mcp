using System;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;

namespace Controlli.ApiQuote
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
    ///     ApiQuote.exe [parola]      predefinito: annotation
    /// </summary>
    internal static class Programma
    {
        private static int Main(string[] argomenti)
        {
            string parola = argomenti.Length > 0 ? argomenti[0] : "annotation";
            Console.WriteLine("Cerco «" + parola + "» nei metodi dell'interop.");
            Console.WriteLine();

            // LE COSTANTI, non solo i metodi. Un'opzione di sistema che non si
            // conosce e' indistinguibile da una che non esiste.
            var costanti = typeof(SolidWorks.Interop.swconst.swUserPreferenceToggle_e).Assembly;
            foreach (var e in new[] { "swUserPreferenceToggle_e", "swInsertAnnotation_e", "swFeatureError_e",
                                      "swImportModelItemsSource_e", "swUserPreferenceIntegerValue_e" })
            {
                var te = costanti.GetType("SolidWorks.Interop.swconst." + e);
                if (te == null) continue;
                var trovati = Enum.GetNames(te)
                    .Where(n => n.IndexOf(parola, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(n => n).ToList();
                if (trovati.Count == 0) continue;
                Console.WriteLine("=== " + e);
                foreach (var n in trovati)
                    Console.WriteLine("  " + n + " = " + Convert.ToInt64(Enum.Parse(te, n)));
                Console.WriteLine();
            }

            var assembly = typeof(ISldWorks).Assembly;
            var tipi = new[]
            {
                "IDrawingDoc", "IView", "IModelDocExtension", "IModelDoc2",
                "IAnnotation", "IDisplayDimension", "ISheet",
                "IFace2", "IBody2", "ISurface", "IEdge",
            };

            foreach (var nome in tipi)
            {
                var t = assembly.GetType("SolidWorks.Interop.sldworks." + nome);
                if (t == null) { Console.WriteLine(nome + ": non c'e'"); continue; }

                var metodi = t.GetMethods()
                    .Where(m => m.Name.IndexOf(parola, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(m => m.Name)
                    .ToList();
                var proprieta = t.GetProperties()
                    .Where(p => p.Name.IndexOf(parola, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(p => p.Name)
                    .ToList();
                if (metodi.Count == 0 && proprieta.Count == 0) continue;

                Console.WriteLine("=== " + nome);
                foreach (var m in metodi)
                {
                    var par = string.Join(", ", m.GetParameters()
                        .Select(p => Corto(p.ParameterType.Name) + " " + p.Name));
                    Console.WriteLine("  " + Corto(m.ReturnType.Name) + " " + m.Name + "(" + par + ")");
                }
                foreach (var p in proprieta)
                    Console.WriteLine("  [prop] " + Corto(p.PropertyType.Name) + " " + p.Name);
                Console.WriteLine();
            }
            return 0;
        }

        private static string Corto(string nome)
        {
            if (nome == "Object") return "obj";
            if (nome == "Boolean") return "bool";
            if (nome == "Int32") return "int";
            if (nome == "Double") return "double";
            if (nome == "String") return "string";
            if (nome == "Void") return "void";
            return nome;
        }
    }
}
