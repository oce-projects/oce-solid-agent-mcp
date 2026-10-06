using System;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;

namespace Checks.DimensionApis
{
    /// <summary>
    /// WHICH APIs TALK ABOUT ANNOTATIONS AND DIMENSIONS, read from the interop.
    ///
    /// It is needed when a call answers «nothing» without saying why: before
    /// continuing to vary the parameters of the same one, it is worth knowing whether there
    /// is another. `InsertModelAnnotations3` answered zero with five
    /// source values, four type masks, the view activated and the view
    /// selected: at that point the question is no longer «which parameter» but
    /// «which method».
    ///
    /// IT DOES NOT OPEN SOLIDWORKS. It only reads the interop metadata, so it can
    /// be launched even with the program closed — and it is also the reason why
    /// what it finds must then be tried live: a signature that exists is not a
    /// call that works.
    ///
    ///     DimensionApis.exe [word]        default: annotation
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            string word = arguments.Length > 0 ? arguments[0] : "annotation";
            Console.WriteLine("Looking for «" + word + "» in the methods of the interop.");
            Console.WriteLine();

            // THE CONSTANTS, not only the methods. A system option that is not
            // known cannot be told apart from one that does not exist.
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
                if (t == null) { Console.WriteLine(itemName + ": not there"); continue; }

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
