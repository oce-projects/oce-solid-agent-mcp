using System;
using System.IO;
using System.Text;
using Oce.Solid.Agent.Core.Catalog;

namespace Oce.Solid.Agent.Catalog
{
    /// <summary>
    /// Writes `catalog.json` from the C# catalog.
    ///
    ///     Oce.Solid.Agent.Catalog.exe                 prints the JSON
    ///     Oce.Solid.Agent.Catalog.exe file.json       writes it there
    ///     Oce.Solid.Agent.Catalog.exe --check f   only says whether it matches (exits 1 if not)
    ///
    /// **It needs neither Python, nor docker, nor the network.** It used to need all
    /// three: whoever cloned the repository could not regenerate the catalog, and a
    /// file that cannot be regenerated is a file nobody dares to touch.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // UTF-8 WITHOUT BOM AND LINE FEED ONLY AS LINE END.
            // The previous file was like that, and the criterion by which the translation
            // was verified is that the new one is byte for byte identical. A
            // BOM or a CRLF would make a file that is equal come out different.
            var withoutBom = new UTF8Encoding(false);

            string json = Export.Json();

            var empty = Export.FamiliesWithoutOperations();
            var notTested = Export.NotTested();
            foreach (var f in empty)
                Console.Error.WriteLine(
                    "[catalog] the family «" + f + "» has no tested operation: " +
                    "it stays out of the file.");
            foreach (var n in notTested)
                Console.Error.WriteLine(
                    "[catalog] «" + n + "» is not tested: it stays out of the file.");

            if (args.Length >= 2 && args[0] == "--check")
            {
                if (!File.Exists(args[1]))
                {
                    Console.Error.WriteLine("cannot find " + args[1]);
                    return 2;
                }
                var old = File.ReadAllText(args[1], Encoding.UTF8);
                if (old == json)
                {
                    Console.Error.WriteLine(
                        "[catalog] identical to " + args[1] + " (" + json.Length + " characters)");
                    return 0;
                }

                // SAY WHERE, NOT ONLY THAT. A «they differ» on a file of
                // seventy thousand characters sends the reader looking by eye.
                int i = 0;
                int fin = Math.Min(old.Length, json.Length);
                while (i < fin && old[i] == json[i]) i++;
                Console.Error.WriteLine(
                    "[catalog] DIFFERENT from " + args[1] + ": old " + old.Length +
                    " characters, new " + json.Length + ", first difference at " + i + ".");
                Console.Error.WriteLine("  old: …" + Around(old, i));
                Console.Error.WriteLine("  new: …" + Around(json, i));
                return 1;
            }

            if (args.Length >= 1 && !args[0].StartsWith("--"))
            {
                File.WriteAllText(args[0], json, withoutBom);
                Console.Error.WriteLine(
                    "[catalog] written " + args[0] + " (" + json.Length + " characters)");
                return 0;
            }

            var output = Console.OpenStandardOutput();
            var byt = withoutBom.GetBytes(json);
            output.Write(byt, 0, byt.Length);
            output.Flush();
            return 0;
        }

        private static string Around(string s, int i)
        {
            int da = Math.Max(0, i - 30);
            int a = Math.Min(s.Length, i + 40);
            return s.Substring(da, a - da).Replace("\n", "\\n");
        }
    }
}
