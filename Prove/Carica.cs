using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Prove.Contratti
{
    /// <summary>
    /// CARICARE IL PONTE SENZA AVERE SOLIDWORKS.
    ///
    /// Il ponte e' un `.exe` che referenzia `Riscontro.Disegno.dll` e l'interop
    /// di Dassault. Qui dentro servono due classi che di SOLIDWORKS non sanno
    /// niente — `Catalogo` e `Sessione.StessoDocumento` — e .NET carica le
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
    internal static class Carica
    {
        private static Assembly _ponte;
        private static bool _agganciato;

        internal static Assembly Ponte(string percorso)
        {
            if (_ponte != null) return _ponte;
            if (!_agganciato)
            {
                var accanto = Path.GetDirectoryName(percorso);
                var suo = Path.GetFileNameWithoutExtension(percorso);
                AppDomain.CurrentDomain.AssemblyResolve += (mittente, argomento) =>
                {
                    var nome = new AssemblyName(argomento.Name).Name;

                    // SE CHIEDE SE STESSO, GLIELO SI RIDA'.
                    //
                    // Un assembly caricato DAI BYTE non ha un percorso, e .NET
                    // non lo ritrova per nome: appena un tipo di quell'assembly
                    // ne nomina un altro dello stesso assembly, parte una
                    // richiesta di risoluzione che senza questa riga finisce in
                    // «non e' stato possibile caricare oce-solid-agent-mcp,
                    // Version=0.0.0.0» — un messaggio che sembra un file
                    // mancante e invece e' un file gia' in memoria.
                    if (_ponte != null &&
                        string.Equals(nome, suo, StringComparison.OrdinalIgnoreCase))
                        return _ponte;

                    var file = Path.Combine(accanto, nome + ".dll");
                    // SI RISPONDE NULL PER QUELLO CHE NON C'E', e va bene: se
                    // e' l'interop di SOLIDWORKS, vuol dire che qualcuno ha
                    // toccato un tipo che qui non si puo' toccare, e
                    // l'eccezione che ne esce lo dice per nome.
                    if (!File.Exists(file)) return null;
                    try { return Assembly.Load(File.ReadAllBytes(file)); } catch { return null; }
                };
                _agganciato = true;
            }
            _ponte = Assembly.Load(File.ReadAllBytes(percorso));
            return _ponte;
        }

        internal static Type Classe(Assembly asm, string nome)
        {
            // `GetTypes()` su un assembly con dipendenze mancanti alza
            // `ReflectionTypeLoadException` e porta con se' i tipi che E'
            // riuscito a caricare: quelli bastano, e buttarli via per un tipo
            // che non c'entra sarebbe pignoleria.
            Type[] tipi;
            try { tipi = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { tipi = ex.Types.Where(t => t != null).ToArray(); }
            return tipi.FirstOrDefault(t => t.Name == nome);
        }
    }
}
