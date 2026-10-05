using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// The material of the part.
    ///
    /// THE DEFECT THIS FILE WAS BORN FROM
    /// On 9 September 2026, after the frame finally came out, the
    /// designer wrote: «also it did not apply the material». It was not
    /// an oversight of the model: in the catalogue there WAS NO operation
    /// that applied it, and in the state of the document the material did not appear
    /// — the materials folder was in fact deliberately excluded from the tree. The
    /// model could neither put it nor notice that it was missing.
    ///
    /// AND IT IS NOT A MATTER OF COLOUR
    /// Without material there is no density, without density there is no mass, without
    /// mass the cut list comes out with the material column empty and
    /// is no use for buying the steel. The chain downstream is already all
    /// built: it broke at the first link, and all the links after gave
    /// «null» and «0» without signalling anything.
    ///
    /// THE REREAD IS NOT A SCRUPLE
    /// `SetMaterialPropertyName2` returns void. With an archive or a name that
    /// do not exist it does not raise and changes nothing: the previous one is
    /// read back. Measured. Therefore here it is ALWAYS reread and compared: if the
    /// name that came back is not the one asked for, the step fails.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The material this workshop uses unless told otherwise
        /// (entry Z-G3). It lives here and not in the schema because the schema
        /// ANNOUNCES it and this APPLIES it: two places for the same value
        /// would diverge, and the catalogue is regenerated from `Operations.cs`.
        /// </summary>
        private const string DefaultMaterial = "S235JR";

        private Outcome ApplyMaterial(IModelDoc2 model, Dictionary<string, object> a)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("Il materiale si applica a una parte, e questo non e' un documento di parte.");

            // ── THE DEFAULT OF THIS WORKSHOP (entry Z-G3) ────────────────
            //
            // A metalwork shop works in S235JR unless told otherwise,
            // and asking every time for a field whose answer is
            // always the same makes the model write a line that
            // is not a choice.
            //
            // BUT THE DEFAULT IS ALWAYS DECLARED. If the bridge put in
            // S235JR and kept quiet, the model would believe it had chosen it itself,
            // the next time it would not even ask, and then a part would arrive
            // that was meant to be stainless without anybody knowing where the decision
            // was taken. *«Nobody thought of it» and «somebody
            // thought of it and decided» read the same, and only one of the
            // two can be improved.*
            string requested = Str(a, "material", "");
            bool assumed = string.IsNullOrWhiteSpace(requested);
            if (assumed) requested = DefaultMaterial;
            string requestedArchive = Str(a, "library", "");

            // The exact spelling is found by the workstation that executes, not the
            // model: «S235JR» in the archives is called «1.0037 (S235JR)», and
            // in the same library there are five different S235 grades.
            string why;
            var material = SolidWorksMaterials.Resolve(
                _sw, requested, requestedArchive, _log, out why);
            if (material == null) return Failed(why);

            // ── EVERY CONFIGURATION, EACH BY ITS NAME (Z-C21.3, finding M10) ──
            //
            // Until library 0.141.0 this was one SetMaterialPropertyName2("")
            // and one re-read of "". The Z-C21.3 probe of the material per
            // configuration (28/9/2026, SOLIDWORKS 2021 SP5.1, verdicts in the
            // TODO), on the company part template with «Default<A macchina>» and
            // its derived «Default<Saldato>», plus one plain configuration made
            // like create_configuration) measured what "" reaches:
            //   - with the first configuration active, Set("") reached it and
            //     the configurations DERIVED from it, and NOT the plain one:
            //     that one kept no material, density 1000 instead of 7800;
            //   - with the derived «Default<Saldato>» active, Set("") wrote on
            //     «Default<Saldato>» ONLY; «Default<A macchina>» stayed empty;
            //   - Set(explicit name) on a configuration that is NOT active was
            //     applied, and the active configuration did not move;
            //   - Set(explicit name) does NOT reach the derived ones.
            // The re-read of "" is the active configuration (L17-1, measured
            // not real), so the old check saw the one configuration that had
            // it and answered «applied» for the whole part.
            // IPartDoc~SetMaterialPropertyName2.html: «Sets the name of the
            // material property for the specified configuration», example with
            // an explicit name ("AddHoles"); IPartDoc~GetMaterialPropertyName2.html,
            // Remarks: «Only the default configuration exists for the part ->
            // Use "" for ConfigName» — "" is documented for that case only.
            // So: one Set per name of GetConfigurationNames, and one re-read per
            // name. "" stays only when the names cannot be read at all.
            // Arrays and counters, not List.Add: the obsolete-call scanner cannot
            // type the receiver of an Add and freezes it as undecided.
            string[] targets = ConfigurationNames(model).ToArray();
            bool namesRead = targets.Length > 0;
            if (!namesRead) targets = new[] { "" };

            // What each configuration had before: replacing a material without
            // saying so is not a courtesy.
            string[] before = new string[targets.Length];
            int beforeCount = 0;
            foreach (var configuration in targets)
            {
                string previous = "";
                try { string db; previous = partDoc.GetMaterialPropertyName2(configuration, out db) ?? ""; }
                catch { }
                if (!string.IsNullOrEmpty(previous) && previous != material.Name &&
                    Array.IndexOf(before, previous, 0, beforeCount) < 0)
                    before[beforeCount++] = previous;
            }

            foreach (var configuration in targets)
            {
                try
                {
                    partDoc.SetMaterialPropertyName2(configuration, material.Archive, material.Name);
                }
                catch (Exception ex)
                {
                    return Failed(
                        "Applying the material to the configuration «" + configuration +
                        "» failed: " + ex.Message);
                }
            }

            // The re-read, by name: SetMaterialPropertyName2 returns void and,
            // with a database or a name that does not exist, changes nothing
            // without raising (measured, see the summary of this file).
            string archiveAfter = "";
            string[] missing = new string[targets.Length];
            int missingCount = 0;
            foreach (var configuration in targets)
            {
                string after = "", database = "";
                try { after = partDoc.GetMaterialPropertyName2(configuration, out database) ?? ""; }
                catch (Exception ex)
                {
                    return Failed(
                        "I applied the material but cannot read it back from the " +
                        "configuration «" + configuration + "» to check it: " + ex.Message);
                }
                if (string.Equals(after, material.Name, StringComparison.Ordinal))
                {
                    if (string.IsNullOrEmpty(archiveAfter)) archiveAfter = database ?? "";
                }
                else
                    missing[missingCount++] = "«" + configuration + "» (" +
                        (string.IsNullOrEmpty(after) ? "empty" : "«" + after + "»") + ")";
            }

            if (missingCount > 0)
                return Failed(
                    "I asked SOLIDWORKS for the material «" + material.Name +
                    "» from the database «" + material.Archive + "» on every configuration " +
                    "of the part, and reading it back " + missingCount + " of " + targets.Length +
                    " do not have it: " + string.Join(", ", missing, 0, missingCount) +
                    ". It was not applied " +
                    "to the whole part; a part with a configuration without material has a " +
                    "false mass in that configuration.");

            var measurements = new Dictionary<string, object>
            {
                ["material"] = material.Name,
                ["library"] = archiveAfter,
                // Z-C21.3 M10: the configurations that now carry the material,
                // each set and read back by its own name.
                ["configurations"] = namesRead ? targets : new string[0],
                ["class_name"] = material.Class,
                // IT IS ALWAYS WRITTEN, even when the material was
                // asked for: a field that appears only in the odd case is a
                // field that whoever reads does not learn to look at.
                ["note_assumption"] = assumed
                    ? "material: «" + DefaultMaterial + "» — DEFAULT of " +
                      "this workshop, you did not request it. If you need something else, " +
                      "write «material» in the step."
                    : "material: «" + requested + "» — you requested it.",
            };
            if (beforeCount > 0)
                measurements["replaces"] = string.Join(", ", before, 0, beforeCount);
            if (!namesRead)
                measurements["configurations_note"] =
                    "The configuration names could not be read: the material was set with " +
                    "the empty name, which reaches the ACTIVE configuration (and the ones " +
                    "derived from it), not the others. Other configurations, if any, may " +
                    "still have no material.";

            // There is no feature: the material is a property of the
            // document. Succeeded() holds up a null reference, and the following
            // steps have nothing to cite here.
            return Succeeded(null, measurements);
        }
    }
}
