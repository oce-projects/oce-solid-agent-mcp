using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>How a component is loaded, from IComponent2.GetSuppression2.</summary>
    internal enum ComponentLoad { Suppressed, Lightweight, Resolved, Unknown }

    /// <summary>One component of an assembly, read once (see <see cref="AssemblyComponents"/>).</summary>
    internal sealed class AssemblyComponent
    {
        public IComponent2 Component;
        public string Name = "?";
        public string Path = "";
        /// <summary>By the file extension, so that a lightweight part is still a part.</summary>
        public bool IsPart;
        /// <summary>By the file extension, so that a lightweight sub-assembly is still a sub-assembly.</summary>
        public bool IsSubAssembly;
        public ComponentLoad Load = ComponentLoad.Unknown;
        public bool Envelope;
        /// <summary>Hidden itself or under a hidden sub-assembly.</summary>
        public bool Hidden;
        /// <summary>GetModelDoc2, read only for a resolved component (null otherwise).</summary>
        public IModelDoc2 Document;
    }

    /// <summary>
    /// WHICH COMPONENTS OF AN ASSEMBLY COUNT, READ IN ONE PLACE (TODO entry
    /// Z-C21.11, findings N5, N27, M23). Up to library 0.146.0 the mass, the box
    /// and the STEP count of an assembly each asked IComponent2.IsSuppressed and
    /// GetModelDoc2, and the BOM asked them again in its own way.
    ///
    /// MEASURED ON 30/9/2026 (probe zc2111/probe_alleggeriti, SOLIDWORKS 2021 SP5.1):
    ///  - a LIGHTWEIGHT component answers IsSuppressed() TRUE, as the programming
    ///    guide says (Overview/Work_with_Lightweight_Components.htm: «IComponent2::IsHidden
    ///    and IComponent2::IsSuppressed return true if a component is flagged as
    ///    lightweight»), and GetModelDoc2 null. The bridge called it suppressed and
    ///    dropped it: on an assembly opened lightweight the mass refused «nothing to
    ///    weigh» and the box did not exist; with one part lightweight the box was
    ///    partial and answered as a success;
    ///  - GetSuppression2 tells them apart (IComponent2~GetSuppression2.html: «Use
    ///    this method to determine if the component is suppressed, lightweight, or
    ///    fully resolved»; swComponentSuppressionState_e: Suppressed 0,
    ///    Lightweight 1, FullyResolved 2, Resolved 3, FullyLightweight 4);
    ///  - GetBodies3 gives the bodies of a lightweight part with the volume of its
    ///    file, and Transform2 is there (IComponent2~GetBodies3.html: «Supports
    ///    lightweight components»);
    ///  - GetPathName is the saved path (IComponent2~GetPathName.html: «the As-Saved
    ///    file and path name»), so the extension says part or sub-assembly without
    ///    the document;
    ///  - IsHidden(false) is true on a hidden component and false on a lightweight
    ///    one that is not hidden (IComponent2~IsHidden.html: only ConsiderSuppressed
    ///    true turns a lightweight component into hidden).
    /// What each operation does with lightweight, envelope and hidden components is
    /// decided by the operation, on its own measurement: see OperazioniMisuraAssieme
    /// and OperazioniDistintaBase.
    /// </summary>
    internal static class AssemblyComponents
    {
        /// <summary>
        /// Every component at every level (IAssemblyDoc.GetComponents(false)).
        /// Null when the list cannot be read. Never throws.
        /// </summary>
        public static List<AssemblyComponent> Read(IAssemblyDoc assembly)
        {
            object[] raw;
            try { raw = assembly == null ? null : assembly.GetComponents(false) as object[]; }
            catch (Exception) { return null; }
            var list = new List<AssemblyComponent>();
            foreach (var o in raw ?? new object[0])
            {
                var c = o as IComponent2;
                if (c == null) continue;
                list.Add(Describe(c));
            }
            return list;
        }

        /// <summary>One component, read. Never throws; what cannot be read stays at its default.</summary>
        public static AssemblyComponent Describe(IComponent2 c)
        {
            var item = new AssemblyComponent { Component = c };
            try { item.Name = c.Name2 ?? "?"; } catch (Exception) { }
            try { item.Path = c.GetPathName() ?? ""; } catch (Exception) { }
            item.IsPart = IsPartFile(item.Path);
            item.IsSubAssembly = IsAssemblyFile(item.Path);
            int state = -1;
            bool stateRead = false;
            try { state = c.GetSuppression2(); stateRead = true; } catch (Exception) { }
            item.Load = stateRead ? LoadFromSuppression2(state) : ComponentLoad.Unknown;
            try { item.Envelope = c.IsEnvelope(); } catch (Exception) { }
            item.Hidden = HiddenHereOrAbove(c);
            if (item.Load == ComponentLoad.Resolved)
            {
                try { item.Document = c.GetModelDoc2() as IModelDoc2; } catch (Exception) { }
            }
            // A PATH THAT IS NEITHER .sldprt NOR .sldasm (review of Z-C21.11): the
            // kind comes from the document when it is loaded, as up to library
            // 0.146.0 (GetModelDoc2 is IPartDoc or IAssemblyDoc). Without this, a
            // resolved component whose saved path has another extension was
            // neither a part nor a sub-assembly and left the mass, the box and
            // the STEP count in silence. When neither the path nor the document
            // tells, both stay false and the readers refuse (see KindUnknown).
            if (!item.IsPart && !item.IsSubAssembly && item.Document != null)
            {
                item.IsPart = item.Document is IPartDoc;
                item.IsSubAssembly = item.Document is IAssemblyDoc;
            }
            return item;
        }

        /// <summary>
        /// swComponentSuppressionState_e to the four states; 5
        /// (swComponentInternalIdMismatch) and anything else is Unknown. Pure, for
        /// Prove.Contratti.
        /// </summary>
        internal static ComponentLoad LoadFromSuppression2(int state)
        {
            switch (state)
            {
                case (int)swComponentSuppressionState_e.swComponentSuppressed: return ComponentLoad.Suppressed;
                case (int)swComponentSuppressionState_e.swComponentLightweight:
                case (int)swComponentSuppressionState_e.swComponentFullyLightweight: return ComponentLoad.Lightweight;
                case (int)swComponentSuppressionState_e.swComponentFullyResolved:
                case (int)swComponentSuppressionState_e.swComponentResolved: return ComponentLoad.Resolved;
                default: return ComponentLoad.Unknown;
            }
        }

        internal static bool IsPartFile(string path)
        {
            return string.Equals(SafeExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsAssemblyFile(string path)
        {
            return string.Equals(SafeExtension(path), ".sldasm", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeExtension(string path)
        {
            try { return System.IO.Path.GetExtension(path ?? "") ?? ""; } catch (ArgumentException) { return ""; }
        }

        /// <summary>
        /// Hidden itself (IsHidden(false), so that a lightweight component is not
        /// taken for hidden) or under a hidden sub-assembly (IComponent2.GetParent,
        /// walked up). A child of a hidden sub-assembly was not measured; it is on
        /// the side that only weakens a check (see SolidiAttesiNellAssieme).
        /// </summary>
        private static bool HiddenHereOrAbove(IComponent2 c)
        {
            var current = c;
            for (int guard = 0; current != null && guard < 64; guard++)
            {
                bool hidden = false;
                try { hidden = current.IsHidden(false); } catch (Exception) { }
                if (hidden) return true;
                IComponent2 parent = null;
                try { parent = current.GetParent() as IComponent2; } catch (Exception) { }
                current = parent;
            }
            return false;
        }

        /// <summary>
        /// A component that is not suppressed and is neither a part nor a
        /// sub-assembly, by its saved path or by its document (review of
        /// Z-C21.11). Leaving it out would be the partial answer of finding N5
        /// again, so the readers that decide what counts refuse on it.
        /// </summary>
        internal static bool KindUnknown(AssemblyComponent item)
        {
            return item != null && item.Load != ComponentLoad.Suppressed && !item.IsPart && !item.IsSubAssembly;
        }

        /// <summary>The names of the components in the list, for an answer.</summary>
        public static string[] Names(IEnumerable<AssemblyComponent> items)
        {
            var names = new List<string>();
            foreach (AssemblyComponent i in items) names.Add(i.Name);
            return names.ToArray();
        }
    }
}
