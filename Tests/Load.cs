using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// LOADING THE BRIDGE WITHOUT HAVING SOLIDWORKS.
    ///
    /// The bridge is an `.exe` that references `Oce.Solid.Agent.Core.dll` and the Dassault
    /// interop. In here two classes are needed that know nothing about SOLIDWORKS
    /// — `Catalog` and `Session.SameDocument` — and .NET loads the
    /// dependencies **one at a time, when needed**: as long as one does not touch a
    /// type that needs the interop, the interop is not needed.
    ///
    /// What is needed is to tell .NET where the neighbours live: an `.exe`
    /// loaded **from the bytes** has no folder, and cannot find its dependencies
    /// by itself. `LoadFrom` cannot be used because this repository
    /// lives on a network drive, and there the loader answers
    /// `NotSupportedException` (0x80131515).
    ///
    /// If one day one of these two classes began to touch the interop,
    /// the loading would fail **here**, with a message that says so — which is
    /// exactly what one wants to know.
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

                    // IF IT ASKS FOR ITSELF, IT IS GIVEN BACK.
                    //
                    // An assembly loaded FROM THE BYTES has no path, and .NET
                    // does not find it again by name: as soon as a type of that assembly
                    // names another one of the same assembly, a resolution
                    // request starts that without this line ends in
                    // «could not load file or assembly oce-solid-agent-mcp,
                    // Version=0.0.0.0» — a message that looks like a missing
                    // file and is instead a file already in memory.
                    if (_bridge != null &&
                        string.Equals(itemName, its, StringComparison.OrdinalIgnoreCase))
                        return _bridge;

                    var file = Path.Combine(nextTo, itemName + ".dll");
                    // NULL IS ANSWERED FOR WHAT IS NOT THERE, and that is fine: if
                    // it is the SOLIDWORKS interop, it means that somebody has
                    // touched a type that cannot be touched here, and
                    // the exception that comes out says so by name.
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
            // `GetTypes()` on an assembly with missing dependencies raises
            // `ReflectionTypeLoadException` and brings with it the types that IT
            // managed to load: those are enough, and throwing them away for a type
            // that is not relevant would be pedantry.
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
