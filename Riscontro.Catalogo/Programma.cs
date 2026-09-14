using System;
using System.IO;
using System.Text;
using Riscontro.Disegno.Catalogo;

namespace Riscontro.Catalogo
{
    /// <summary>
    /// Scrive `catalogo.json` dal catalogo C#.
    ///
    ///     Riscontro.Catalogo.exe                 stampa il JSON
    ///     Riscontro.Catalogo.exe file.json       lo scrive li'
    ///     Riscontro.Catalogo.exe --controlla f   dice solo se combacia (esce 1 se no)
    ///
    /// **Non serve ne' Python, ne' docker, ne' la rete.** Prima serviva tutto e
    /// tre: chi clonava il repository non poteva rigenerare il catalogo, e un
    /// file che non si puo' rigenerare e' un file che nessuno osa toccare.
    /// </summary>
    internal static class Programma
    {
        private static int Main(string[] args)
        {
            // UTF-8 SENZA BOM E A CAPO SOLO AVANZAMENTO RIGA.
            // Il file di prima era cosi', e il criterio con cui si e' verificata
            // la traduzione e' che il nuovo fosse identico byte per byte. Un
            // BOM o un CRLF renderebbero diverso un file che e' uguale.
            var senzaBom = new UTF8Encoding(false);

            string json = Esporta.Json();

            var vuote = Esporta.FamiglieSenzaOperazioni();
            var nonProvate = Esporta.NonProvate();
            foreach (var f in vuote)
                Console.Error.WriteLine(
                    "[catalogo] la famiglia «" + f + "» non ha nessuna operazione " +
                    "provata: resta fuori dal file.");
            foreach (var n in nonProvate)
                Console.Error.WriteLine(
                    "[catalogo] «" + n + "» non e' provata: resta fuori dal file.");

            if (args.Length >= 2 && args[0] == "--controlla")
            {
                if (!File.Exists(args[1]))
                {
                    Console.Error.WriteLine("non trovo " + args[1]);
                    return 2;
                }
                var vecchio = File.ReadAllText(args[1], Encoding.UTF8);
                if (vecchio == json)
                {
                    Console.Error.WriteLine(
                        "[catalogo] identico a " + args[1] + " (" + json.Length + " caratteri)");
                    return 0;
                }

                // SI DICE DOVE, NON SOLO CHE. Un «sono diversi» su un file da
                // settantamila caratteri manda a cercare a occhio.
                int i = 0;
                int fin = Math.Min(vecchio.Length, json.Length);
                while (i < fin && vecchio[i] == json[i]) i++;
                Console.Error.WriteLine(
                    "[catalogo] DIVERSO da " + args[1] + ": vecchio " + vecchio.Length +
                    " caratteri, nuovo " + json.Length + ", primo scostamento al " + i + ".");
                Console.Error.WriteLine("  vecchio: …" + Intorno(vecchio, i));
                Console.Error.WriteLine("  nuovo:   …" + Intorno(json, i));
                return 1;
            }

            if (args.Length >= 1 && !args[0].StartsWith("--"))
            {
                File.WriteAllText(args[0], json, senzaBom);
                Console.Error.WriteLine(
                    "[catalogo] scritto " + args[0] + " (" + json.Length + " caratteri)");
                return 0;
            }

            var uscita = Console.OpenStandardOutput();
            var byt = senzaBom.GetBytes(json);
            uscita.Write(byt, 0, byt.Length);
            uscita.Flush();
            return 0;
        }

        private static string Intorno(string s, int i)
        {
            int da = Math.Max(0, i - 30);
            int a = Math.Min(s.Length, i + 40);
            return s.Substring(da, a - da).Replace("\n", "\\n");
        }
    }
}
