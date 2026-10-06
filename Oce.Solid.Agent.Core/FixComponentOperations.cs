using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// FIXES OR FREES A COMPONENT (entry Z-D5).
    ///
    /// It is the only one of the state commands of a component that really changes how
    /// the assembly is rebuilt: hidden and transparent do not change the manufactured
    /// part, fixed does. A fixed component does not move because someone
    /// nailed it down, not because it is mated.
    ///
    /// Measured on 27/9/2026 (`probe0089 zd345`): the selected component
    /// (`IComponent2.Select4`) and `IAssemblyDoc.FixComponent()` /
    /// `UnfixComponent()` — both `void`: the outcome is read back with
    /// `IsFixed`. It works in both directions, EVEN on the first component, the one
    /// SOLIDWORKS fixes by itself at insertion (lesson GA5: it behaves like
    /// one fixed by hand).
    /// </summary>
    public partial class SwOperations
    {
        private Outcome FixComponent(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed("This is not an assembly: fixing or freeing applies to a component of an assembly. " +
                               "Open the assembly for me.");
            object rawFixed;
            if (a == null || !a.TryGetValue("grounded", out rawFixed) || !(rawFixed is bool))
                return Failed("Tell me «grounded»: true to fix it (it does not move any more), false to free it. There " +
                               "is no default direction: they are two opposite things.");
            bool want = (bool)rawFixed;
            object[] rawItems = null;
            try { rawItems = assemblyDoc.GetComponents(false) as object[]; } catch { }
            var components = DocumentState.ComponentsInOrder(rawItems);
            string uid = Str(a, "component", null);
            var c = PerUid(components, uid);
            if (c == null) return Failed(NotFound(uid, "component", components));
            // TOP LEVEL ONLY (finding L11-3, lot Z-C21.5). Up to library 0.145.0
            // a nested component was read with IsFixed from the root, which the
            // guide says does not apply to it, and could come back «it was
            // already free: nothing to change».
            string notTopLevel = NotTopLevel(assemblyDoc, c, uid, components,
                "fixing or freeing works on top-level components only: IsFixed «only applies to the top " +
                "level of components» (IComponent2~IsFixed.html), and from the root «all of the " +
                "components in the subassembly are allowed to move»");
            if (notTopLevel != null) return Failed(notTopLevel);
            bool before = IsFixed(c);
            if (before == want)
                return SucceededWithoutCreating(new Dictionary<string, object>
                {
                    ["component"] = uid,
                    ["name"] = NameOf(c),
                    ["grounded_before"] = before,
                    ["grounded_after"] = before,
                    ["verdict"] = "«" + NameOf(c) + "» was already " + (want ? "fixed" : "free") + ": nothing to change.",
                });
            bool taken = false;
            try
            {
                model.ClearSelection2(true);
                taken = c.Select4(false, null, false);
                if (taken)
                {
                    if (want) assemblyDoc.FixComponent(); else assemblyDoc.UnfixComponent();
                }
            }
            catch (Exception ex) { try { model.ClearSelection2(true); } catch { } return Failed("I did not manage: " + ex.Message); }
            try { model.ClearSelection2(true); } catch { }
            if (!taken) return Failed("«" + NameOf(c) + "» cannot be selected: I did not touch it.");
            try { model.EditRebuild3(); } catch { }
            // IT IS READ BACK: FixComponent and UnfixComponent answer nothing.
            bool after = IsFixed(c);
            if (after != want)
                return Failed("I asked to " + (want ? "fix" : "free") + " «" + NameOf(c) + "», and " +
                               "reading it back it is still " + (after ? "fixed" : "free") + ". Nothing happened.");
            int acc = MatesOf(c);
            var measurements = new Dictionary<string, object>
            {
                ["component"] = uid,
                ["name"] = NameOf(c),
                ["grounded_before"] = before,
                ["grounded_after"] = after,
                ["mates"] = acc >= 0 ? (object)acc : null,
                ["verdict"] = "«" + NameOf(c) + "» is now " + (after ? "FIXED: it does not move any more, whatever mates it has." :
                    "FREE: only its mates hold it" + (acc == 0 ? " — and it has none: it is laid down, it moves." : ".")),
            };
            _log("[agent] " + NameOf(c) + (after ? " fixed" : " freed"));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// THE TOP-LEVEL CHECK shared by fix_component (L11-3) and
        /// replace_component (L11-5), lot Z-C21.5: null when
        /// <paramref name="component"/> is a top-level component of
        /// <paramref name="assembly"/>, otherwise the refusal, which names the
        /// top-level sub-assembly that holds it and its uid.
        ///
        /// The C1..Cn list is built from GetComponents(false), every level
        /// (the state lists them all on purpose), while both SOLIDWORKS calls
        /// behind these operations work on the top level only. «Top level» is
        /// decided by DocumentState.TopLevelNames, the same rule the state
        /// uses for «top_level». A top-level list that cannot be read refuses
        /// too: acting on a component that may be nested is the defect itself.
        /// </summary>
        private static string NotTopLevel(
            IAssemblyDoc assembly, IComponent2 component, string uid,
            List<KeyValuePair<string, IComponent2>> components, string why)
        {
            object[] topLevel = null;
            try { topLevel = assembly.GetComponents(true) as object[]; } catch { }
            var names = DocumentState.TopLevelNames(topLevel);
            string name = NameOf(component);
            if (names == null || string.IsNullOrEmpty(name))
                return "I could not tell whether «" + uid + "» is a top-level component (" +
                       (names == null ? "IAssemblyDoc.GetComponents(true) did not answer" : "its name did not answer") +
                       "), and " + why + ". Nothing was changed.";
            if (names.Contains(name)) return null;

            // Name2 of a nested component is the whole path, «subAssem1-2/Part1-1»
            // (IComponent2~Name2.html): its first segment is the top-level
            // sub-assembly that holds it.
            int slash = name.IndexOf('/');
            string holder = slash > 0 ? name.Substring(0, slash) : null;
            string holderUid = null;
            if (holder != null)
                for (int i = 0; i < components.Count; i++)
                    if (string.Equals(components[i].Key, holder, StringComparison.Ordinal)) { holderUid = "C" + (i + 1); break; }
            return "«" + uid + "» («" + name + "») is not a top-level component: it sits inside the sub-assembly «" +
                   (holder ?? "?") + "»" + (holderUid == null ? "" : " (" + holderUid + ")") + ", and " + why +
                   ". Nothing was changed. Open that sub-assembly and do it there, or act on the sub-assembly " +
                   "itself" + (holderUid == null ? "" : ", " + holderUid) + ".";
        }
    }
}
