using System;
using System.IO;
using System.Text;
using Oce.Solid.Agent.Core.Catalog;

namespace Oce.Solid.Agent.Catalog
{
    /// <summary>
    /// Scrive `catalog.json` dal catalogo C#.
    ///
    ///     Oce.Solid.Agent.Catalog.exe                 stampa il JSON
    ///     Oce.Solid.Agent.Catalog.exe file.json       lo scrive li'
    ///     Oce.Solid.Agent.Catalog.exe --controlla f   dice solo se combacia (esce 1 se no)
    ///
    /// **Non serve ne' Python, ne' docker, ne' la rete.** Prima serviva tutto e
    /// tre: chi clonava il repository non poteva rigenerare il catalogo, e un
    /// file che non si puo' rigenerare e' un file che nessuno osa toccare.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // UTF-8 SENZA BOM E A CAPO SOLO AVANZAMENTO RIGA.
            // Il file di prima era cosi', e il criterio con cui si e' verificata
            // la traduzione e' che il nuovo fosse identico byte per byte. Un
            // BOM o un CRLF renderebbero diverso un file che e' uguale.
            var withoutBom = new UTF8Encoding(false);

            string json = Export.Json();

            var empty = Export.FamiliesWithoutOperations();
            var notTested = Export.NotTested();
            foreach (var f in empty)
                Console.Error.WriteLine(
                    "[catalogo] la famiglia «" + f + "» non ha nessuna operazione " +
                    "provata: resta fuori dal file.");
            foreach (var n in notTested)
                Console.Error.WriteLine(
                    "[catalogo] «" + n + "» non e' provata: resta fuori dal file.");

            if (args.Length >= 2 && args[0] == "--controlla")
            {
                if (!File.Exists(args[1]))
                {
                    Console.Error.WriteLine("non trovo " + args[1]);
                    return 2;
                }
                var old = File.ReadAllText(args[1], Encoding.UTF8);
                if (old == json)
                {
                    Console.Error.WriteLine(
                        "[catalogo] identico a " + args[1] + " (" + json.Length + " caratteri)");
                    return 0;
                }

                // SI DICE DOVE, NON SOLO CHE. Un «sono diversi» su un file da
                // settantamila caratteri manda a cercare a occhio.
                int i = 0;
                int fin = Math.Min(old.Length, json.Length);
                while (i < fin && old[i] == json[i]) i++;
                Console.Error.WriteLine(
                    "[catalogo] DIVERSO da " + args[1] + ": vecchio " + old.Length +
                    " caratteri, nuovo " + json.Length + ", primo scostamento al " + i + ".");
                Console.Error.WriteLine("  vecchio: …" + Around(old, i));
                Console.Error.WriteLine("  nuovo:   …" + Around(json, i));
                return 1;
            }

            if (args.Length >= 1 && !args[0].StartsWith("--"))
            {
                File.WriteAllText(args[0], json, withoutBom);
                Console.Error.WriteLine(
                    "[catalogo] scritto " + args[0] + " (" + json.Length + " caratteri)");
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
