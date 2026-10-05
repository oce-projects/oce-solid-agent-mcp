using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// CARICARE IL PONTE SENZA AVERE SOLIDWORKS.
    ///
    /// Il ponte e' un `.exe` che referenzia `Oce.Solid.Agent.Core.dll` e l'interop
    /// di Dassault. Qui dentro servono due classi che di SOLIDWORKS non sanno
    /// niente — `Catalog` e `Session.SameDocument` — e .NET carica le
    /// dipendenze **una alla volta, quando servono**: finche' non si tocca un
    /// tipo che ha bisogno dell'interop, l'interop non serve.
    ///
    /// Quello che serve e' dire a .NET dove stanno i vicini di casa: un `.exe`
    /// caricato **dai byte** non ha una cartella, e le sue dipendenze non le
    /// trova da solo. `LoadFrom` non si puo' usare perche' questo repository
    /// sta su un'unita' di rete, e li' il caricatore risponde
    /// `NotSupportedException` (0x80131515).
    ///
    /// Se un giorno una di queste due classi cominciasse a toccare l'interop,
    /// il caricamento fallirebbe **qui**, con un messaggio che lo dice — che e'
    /// esattamente cio' che si vuole sapere.
    /// </summary>
    internal static class Load
    {
        private static Assembly _bridge;
        private static bool _attached;

        internal static Assembly Bridge(string filePath)
        {
            if (_bridge != null) return _bridge;
            if (!_attached)
            {
                var nextTo = Path.GetDirectoryName(filePath);
                var its = Path.GetFileNameWithoutExtension(filePath);
                AppDomain.CurrentDomain.AssemblyResolve += (sender, argument) =>
                {
                    var itemName = new AssemblyName(argument.Name).Name;

                    // SE CHIEDE SE STESSO, GLIELO SI RIDA'.
                    //
                    // Un assembly caricato DAI BYTE non ha un percorso, e .NET
                    // non lo ritrova per nome: appena un tipo di quell'assembly
                    // ne nomina un altro dello stesso assembly, parte una
                    // richiesta di risoluzione che senza questa riga finisce in
                    // «non e' stato possibile caricare oce-solid-agent-mcp,
                    // Version=0.0.0.0» — un messaggio che sembra un file
                    // mancante e invece e' un file gia' in memoria.
                    if (_bridge != null &&
                        string.Equals(itemName, its, StringComparison.OrdinalIgnoreCase))
                        return _bridge;

                    var file = Path.Combine(nextTo, itemName + ".dll");
                    // SI RISPONDE NULL PER QUELLO CHE NON C'E', e va bene: se
                    // e' l'interop di SOLIDWORKS, vuol dire che qualcuno ha
                    // toccato un tipo che qui non si puo' toccare, e
                    // l'eccezione che ne esce lo dice per nome.
                    if (!File.Exists(file)) return null;
                    try { return Assembly.Load(File.ReadAllBytes(file)); } catch { return null; }
                };
                _attached = true;
            }
            _bridge = Assembly.Load(File.ReadAllBytes(filePath));
            return _bridge;
        }

        internal static Type Class(Assembly asm, string itemName)
        {
            // `GetTypes()` su un assembly con dipendenze mancanti alza
            // `ReflectionTypeLoadException` e porta con se' i tipi che E'
            // riuscito a caricare: quelli bastano, e buttarli via per un tipo
            // che non c'entra sarebbe pignoleria.
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            var found = types.FirstOrDefault(t => t.Name == itemName);
            // Every lookup by name is logged, and a null one turns
            // ReflectionTests red at the end of the run (W3.1-bis.2).
            if (found == null) ReflectionLog.Global.MissingType(asm, itemName);
            else ReflectionLog.Global.Found();
            return found;
        }
    }
}
