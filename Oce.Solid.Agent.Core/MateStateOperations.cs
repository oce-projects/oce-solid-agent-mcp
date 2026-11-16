using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A MATE TAKEN OUT BY ITS NAME (brief R22).
    ///
    /// On 25/10/2026 a distance mate put the second half of a channel under the
    /// first instead of on top, and there was no way to take it out:
    /// «suppress_feature "Distanza1"» was refused because the state of an assembly
    /// listed no names, and «delete_feature» asked for a confirmation the client
    /// never showed. The state now lists the mates by name («mates_list»,
    /// AssemblyState.DescribeMates), and these two operations take that name.
    ///
    /// SUPPRESSING IS THE ROAD BACK: it can be undone with «reactivate», so it is
    /// not behind a confirmation. DELETING cannot, and the bridge asks for it
    /// (McpTools.CannotBeUndone).
    ///
    /// The mates are the sub-features of the MateGroup feature, read the same way
    /// as MateOperations.OnlyStandardMates. Suppression with
    /// IFeature.SetSuppression2 in all the configurations, read back with
    /// ApiHelpers.IsSuppressedHere, as suppress_feature does for a part.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome SuppressMate(IModelDoc2 model, Dictionary<string, object> a)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed("This is not an assembly: mates belong to an assembly. Open the assembly for me.");
            string name = (Str(a, "mate", "") ?? "").Trim();
            if (name.Length == 0)
                return Failed("Tell me «mate»: the uid or the name of the mate, from «mates_list» in the state " +
                               "of the assembly (for example «M1» or «Distanza1»).");
            bool reactivate = Bool(a, "reactivate", false);

            string names;
            var feature = MateFeature(model, name, out names);
            if (feature == null) return Failed(NoSuchMate(name, names));
            // From here on, the name SOLIDWORKS gives it: a uid («M1») would point
            // to the next mate once this one is gone.
            try { name = feature.Name ?? name; } catch { }

            bool was = false;
            try { was = ApiHelpers.IsSuppressedHere(feature); } catch { }
            if (was == !reactivate)
                return Failed("«" + name + "» is already " + (was ? "suppressed" : "active") +
                               ": I changed nothing. If you wanted the opposite, " +
                               (was ? "pass «reactivate»: true." : "remove «reactivate»."));

            string kind;
            var held = HeldComponents(assemblyDoc, feature, out kind);

            // IN ALL THE CONFIGURATIONS, as suppress_feature: a mate off in one
            // configuration only would leave the assembly different depending on
            // how it is looked at.
            const int inAllConfigurations = 2;
            int action = reactivate
                ? (int)swFeatureSuppressionAction_e.swUnSuppressFeature
                : (int)swFeatureSuppressionAction_e.swSuppressFeature;
            bool done;
            try { done = feature.SetSuppression2(action, inAllConfigurations, null); }
            catch (Exception ex)
            {
                return Failed("SOLIDWORKS did not " + (reactivate ? "reactivate" : "suppress") + " «" + name +
                               "»: " + ex.Message + ". Read the assembly again with «inspect_part» and try again.");
            }
            if (!done)
                return Failed("SOLIDWORKS did not " + (reactivate ? "reactivate" : "suppress") + " «" + name +
                               "», and did not say why. Read the assembly again with «inspect_part» and try again.");
            try { RebuildKeepingUndo(model); } catch { }

            // READ BACK: SetSuppression2 answering true is not a mate turned off.
            bool now = was;
            try { now = ApiHelpers.IsSuppressedHere(feature); } catch { }
            if (now == was)
                return Failed("I asked to " + (reactivate ? "reactivate" : "suppress") + " «" + name +
                               "», and reading it back it is still " + (now ? "suppressed" : "active") +
                               ". Nothing happened.");

            int problems = HowManyProblems(model);
            var measurements = new Dictionary<string, object>
            {
                ["mate"] = name,
                ["kind"] = kind,
                ["suppressed"] = now,
                ["components"] = held,
                ["problems_after"] = problems < 0 ? null : (object)problems,
                ["note"] = reactivate
                    ? "The mate holds its components again."
                    : "The components it held stay where they are until something moves them, but the " +
                      "directions it blocked are free now. «reactivate»: true turns it back on.",
            };
            _log("[agent] mate " + name + (reactivate ? " reactivated" : " suppressed"));
            return SucceededWithoutCreating(measurements);
        }

        private Outcome DeleteMate(IModelDoc2 model, Dictionary<string, object> a)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed("This is not an assembly: mates belong to an assembly. Open the assembly for me.");
            string name = (Str(a, "mate", "") ?? "").Trim();
            if (name.Length == 0)
                return Failed("Tell me «mate»: the uid or the name of the mate, from «mates_list» in the state " +
                               "of the assembly (for example «M1» or «Distanza1»).");

            string names;
            var feature = MateFeature(model, name, out names);
            if (feature == null) return Failed(NoSuchMate(name, names));
            // From here on, the name SOLIDWORKS gives it: a uid («M1») would point
            // to the next mate once this one is gone.
            try { name = feature.Name ?? name; } catch { }
            int before = MateNames(model).Count;

            string kind;
            var held = HeldComponents(assemblyDoc, feature, out kind);

            // DeleteSelection2 and not EditDelete, for the reason DeleteFeature
            // gives: EditDelete opens a confirmation window, and a COM call that
            // opens a modal does not return.
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                if (!feature.Select2(false, 0))
                    return Failed("«" + name + "» cannot be selected: I did not touch it. Read the assembly " +
                                   "again with «inspect_part» and try again.");
                removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                try { model.ClearSelection2(true); } catch { }
                return Failed("SOLIDWORKS did not delete «" + name + "»: " + ex.Message +
                               ". Read the assembly again with «inspect_part» and try again.");
            }
            try { model.ClearSelection2(true); } catch { }
            try { RebuildKeepingUndo(model); } catch { }

            // READ BACK: the name is gone and there is one mate fewer.
            string ignored;
            bool stillThere = MateFeature(model, name, out ignored) != null;
            int after = MateNames(model).Count;
            if (stillThere || after != before - 1)
                return Failed("I asked to delete «" + name + "» (DeleteSelection2 answered " + removed +
                               "), and reading it back " + (stillThere ? "it is still there" : "the mates went from " +
                               before + " to " + after) + ". I did not consider it deleted.");

            int problems = HowManyProblems(model);
            var measurements = new Dictionary<string, object>
            {
                ["mate"] = name,
                ["kind"] = kind,
                ["components"] = held,
                ["mates_before"] = before,
                ["mates_after"] = after,
                ["problems_after"] = problems < 0 ? null : (object)problems,
                ["note"] = "Deleted: only «undo» brings it back. To turn a mate off without losing it, " +
                           "«suppress_mate» is the road that can be walked back.",
            };
            _log("[agent] mate " + name + " deleted");
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// The mate called <paramref name="name"/>: first by the name SOLIDWORKS
        /// shows in the tree (compared without case), then by the uid of the state
        /// («M1» is the first mate of the tree), so a case reads the same in an
        /// Italian and an English session. Null when there is none;
        /// <paramref name="names"/> lists the mates there are, for the refusal.
        /// </summary>
        internal static IFeature MateFeature(IModelDoc2 model, string name, out string names)
        {
            names = "";
            IFeature found = null;
            var mates = Mates(model).ToList();
            var all = new List<string>();
            for (int i = 0; i < mates.Count; i++)
            {
                IFeature mateFeature = mates[i];
                string its = null;
                try { its = mateFeature.Name; } catch { }
                all.Add("M" + (i + 1) + " «" + (its ?? "?") + "»");
                if (found == null && its != null && string.Equals(its, name, StringComparison.OrdinalIgnoreCase))
                    found = mates[i];
            }
            if (found == null && name != null && name.Length > 1 && (name[0] == 'M' || name[0] == 'm'))
            {
                int k;
                if (int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out k) &&
                    k >= 1 && k <= mates.Count)
                    found = mates[k - 1];
            }
            names = string.Join(", ", all.Take(20)) + (all.Count > 20 ? ", ..." : "");
            return found;
        }

        private static List<string> MateNames(IModelDoc2 model)
        {
            var list = new List<string>();
            foreach (IFeature sub in Mates(model))
            {
                string its = null;
                try { its = sub.Name; } catch { }
                if (its != null) list.Add(its);
            }
            return list;
        }

        /// <summary>The sub-features of the MateGroup that are mates (they answer IMate2).</summary>
        private static IEnumerable<IFeature> Mates(IModelDoc2 model)
        {
            var list = new List<IFeature>();
            if (model == null) return list;
            try
            {
                IFeature group = null;
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == "MateGroup") { group = f; break; }
                    f = f.GetNextFeature() as IFeature;
                }
                var sub = group == null ? null : group.GetFirstSubFeature() as IFeature;
                while (sub != null)
                {
                    if (sub.GetSpecificFeature2() is IMate2) list.Add(sub);
                    sub = sub.GetNextSubFeature() as IFeature;
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// The components a mate holds, by the uid of the state («C1») when the
        /// component is in the list, by its name otherwise; and its kind.
        /// </summary>
        private static List<string> HeldComponents(IAssemblyDoc assemblyDoc, IFeature feature, out string kind)
        {
            kind = null;
            var held = new List<string>();
            var mate = feature == null ? null : feature.GetSpecificFeature2() as IMate2;
            if (mate == null) return held;
            try { kind = MateKindName(mate.Type); } catch { }
            object[] rawItems = null;
            try { rawItems = assemblyDoc.GetComponents(false) as object[]; } catch { }
            var components = DocumentState.ComponentsInOrder(rawItems);
            var uidOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < components.Count; i++) uidOf[components[i].Key] = "C" + (i + 1);
            try
            {
                int n = mate.GetMateEntityCount();
                for (int k = 0; k < n; k++)
                {
                    var e = mate.MateEntity(k) as IMateEntity2;
                    var c = e == null ? null : e.ReferenceComponent as IComponent2;
                    string cn = null;
                    try { cn = c == null ? null : c.Name2; } catch { }
                    string uid;
                    held.Add(cn == null ? "?" : uidOf.TryGetValue(cn, out uid) ? uid : cn);
                }
            }
            catch { }
            return held;
        }

        private static string MateKindName(int type)
        {
            string n = Enum.GetName(typeof(swMateType_e), type);
            return n == null ? type.ToString(CultureInfo.InvariantCulture) : n.Replace("swMate", "").ToLowerInvariant();
        }

        private static string NoSuchMate(string name, string names)
        {
            return "There is no mate «" + name + "» in this assembly." +
                   (names.Length > 0 ? " There are: " + names + ". Use one of those uids or names." :
                                       " It has no mates.") +
                   " The names are in «mates_list» of the state.";
        }
    }
}
