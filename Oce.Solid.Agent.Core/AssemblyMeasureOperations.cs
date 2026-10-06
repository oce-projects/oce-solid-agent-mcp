using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// HOW MUCH THE ASSEMBLY WEIGHS, WHAT BOX IT FITS IN, AND THE ASSEMBLY'S STEP
    /// (entries Z-D1 and Z-D6).
    ///
    /// «How much does it weigh» and «what bounding box does it fit in» are the first two questions about a
    /// watertight box, and `mass_properties` / `body_bounding_box` refused an
    /// assembly with «this document is not a part». Measured on 27/9/2026
    /// (`probe0089 zd1`, AISI 304 base + S235JR lid + a third instance
    /// SUPPRESSED):
    ///
    ///  * `CreateMassProperty()` on the assembly, with no bodies added: 1.06752 kg,
    ///    EXACTLY the sum of the two parts (0.768 + 0.29952); the suppressed
    ///    component weighs nothing.
    ///  * `IComponent2.GetBodies3` gives the bodies in the space of the PART (the
    ///    lid at y 0..4 instead of 5..9): for the bounding box they must be brought
    ///    into the assembly with `Transform2` — a copy of the body and `ApplyTransform`.
    ///  * The assembly's STEP is written (`SaveAs3`, as for the part) and in the
    ///    text it has one `MANIFOLD_SOLID_BREP` per body. READING IT BACK with `LoadFile4`
    ///    opens the modal «New SOLIDWORKS Document» and the call does not return:
    ///    the check is the count of the solids in the text, not the reopening.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The components of an assembly as <see cref="AssemblyComponents"/> reads them,
        /// or a refusal: null list, or a component whose load state (GetSuppression2)
        /// cannot be read. Z-C21.11: this replaced the old components helper, which asked
        /// IsSuppressed — TRUE on a lightweight component (measured) — and so called
        /// lightweight parts «suppressed» and left them out.
        /// </summary>
        private static List<AssemblyComponent> ComponentsOrRefusal(IAssemblyDoc assemblyDoc, string what, out string refusal)
        {
            refusal = null;
            var all = AssemblyComponents.Read(assemblyDoc);
            if (all == null)
            {
                refusal = "I could not read the components of the assembly (GetComponents): no " + what + ".";
                return null;
            }
            var unknown = all.Where(c => c.Load == ComponentLoad.Unknown).ToList();
            if (unknown.Count > 0)
            {
                refusal = "I could not read whether " + string.Join(", ", AssemblyComponents.Names(unknown)) +
                          " is suppressed, lightweight or resolved (GetSuppression2): without it I cannot tell what " +
                          "counts, so no " + what + ".";
                return null;
            }
            // Neither a part nor a sub-assembly, by path or by document (review of
            // Z-C21.11): skipping it would give a partial answer as a success.
            var kindless = all.Where(AssemblyComponents.KindUnknown).ToList();
            if (kindless.Count > 0)
            {
                refusal = "I could not tell whether " + string.Join(", ", AssemblyComponents.Names(kindless)) +
                          " is a part or a sub-assembly: its saved path (GetPathName) ends neither in .sldprt nor in " +
                          ".sldasm and its document is not loaded. Resolve it in SOLIDWORKS and ask again; no " + what + ".";
                return null;
            }
            return all;
        }

        private static double? PartMass(IModelDoc2 doc)
        {
            try
            {
                // A PART document (a component's): CreateMassProperty2 since
                // Z-C21.7 (ApiHelpers.NewPartMassProperty; the probe's whole-part
                // reading N1 equals the obsolete call's M0). The assembly itself
                // stays on the obsolete call below.
                var mp = ApiHelpers.NewPartMassProperty(doc, null, out _);
                if (mp == null) return null;
                return mp.Mass;
            }
            catch { return null; }
        }

        /// <summary>
        /// Z-D1: the mass of the whole assembly, checked against the sum of its parts.
        ///
        /// Z-C21.11, MEASURED ON 30/9/2026 (probe zc2111/probe_alleggeriti, parts in AISI 304,
        /// density read back 8000 kg/m3; control A + E accepted at 24000 mm3):
        ///  - ENVELOPES are left out of the assembly mass: A + envelope E weighed the
        ///    volume of A only (16000 mm3), while the old sum counted E and refused a
        ///    right assembly with a gap of 0.5 («a component counted twice?», finding
        ///    N27). The sum leaves them out too, and the answer names them;
        ///  - HIDDEN components are weighed (A + hidden B: 17000 mm3): they stay in;
        ///  - LIGHTWEIGHT parts are weighed by SOLIDWORKS (A resolved + N lightweight:
        ///    18000 mm3, N at water density because it has no material) but their
        ///    material cannot be read: GetModelDoc2 is null, and neither
        ///    IComponent2.GetMaterialUserName, GetMaterialIdName nor
        ///    IBody2.GetMaterialPropertyName answered anything even on resolved parts.
        ///    On an assembly where every part is lightweight, CreateMassProperty gave
        ///    nothing at all. So a lightweight part refuses the step, unless
        ///    «resolve_lightweight» asks to resolve them first
        ///    (IAssemblyDoc.ResolveAllLightWeightComponents(false): swResolveOk on the
        ///    probe, and GetLightWeightComponentCount 0 after it).
        /// </summary>
        private Outcome AssemblyMassProperties(IModelDoc2 model, Dictionary<string, object> a)
        {
            if (!string.IsNullOrWhiteSpace(Str(a, "body_uid", null)))
                return Failed(
                    "On an assembly «body_uid» does not apply: I weigh the whole assembly, that is the components that " +
                    "are not suppressed. For a single part, open it and ask me there.");
            var assemblyDoc = model as IAssemblyDoc;
            string refusal;
            var all = ComponentsOrRefusal(assemblyDoc, "mass", out refusal);
            if (all == null) return Failed(refusal);

            // ── LIGHTWEIGHT PARTS: resolved on request, otherwise a refusal ──
            string resolvedOnRequest = null;
            var lightweight = all.Where(c => c.IsPart && c.Load == ComponentLoad.Lightweight && !c.Envelope).ToList();
            if (lightweight.Count > 0)
            {
                if (!Bool(a, "resolve_lightweight", false))
                    return Failed(
                        lightweight.Count + " part(s) of the assembly are lightweight (" +
                        string.Join(", ", AssemblyComponents.Names(lightweight)) + "). SOLIDWORKS weighs them, but " +
                        "their material cannot be read while they are lightweight (they have no document), so a part " +
                        "left without material would weigh as water without anyone seeing it, and the sum of the " +
                        "parts cannot read them either. Resolve them in SOLIDWORKS, or ask me again with " +
                        "«resolve_lightweight»: true and I resolve them first (they stay resolved).");
                int status = -1;
                string thrown = null;
                try { status = assemblyDoc.ResolveAllLightWeightComponents(false); }
                catch (Exception ex) { thrown = ex.Message; }
                all = ComponentsOrRefusal(assemblyDoc, "mass", out refusal);
                if (all == null) return Failed("After ResolveAllLightWeightComponents: " + refusal);
                var still = all.Where(c => c.IsPart && c.Load == ComponentLoad.Lightweight && !c.Envelope).ToList();
                string answered = thrown != null ? "threw: " + thrown :
                    Enum.GetName(typeof(swComponentResolveStatus_e), status) ?? status.ToString();
                if (still.Count > 0)
                    return Failed(
                        "I asked SOLIDWORKS to resolve the lightweight parts (ResolveAllLightWeightComponents " + answered +
                        ") and these are still lightweight: " + string.Join(", ", AssemblyComponents.Names(still)) +
                        ". No mass.");
                resolvedOnRequest = lightweight.Count + " lightweight part(s) resolved on request " +
                                    "(ResolveAllLightWeightComponents " + answered + "); they stay resolved.";
                _log("[agent] assembly mass: " + resolvedOnRequest);
            }

            var suppressedOnes = AssemblyComponents.Names(all.Where(c => c.Load == ComponentLoad.Suppressed));
            var envelopes = all.Where(c => c.IsPart && c.Load != ComponentLoad.Suppressed && c.Envelope).ToList();
            var parts = all.Where(c => c.IsPart && c.Load == ComponentLoad.Resolved && !c.Envelope).ToList();
            if (parts.Count == 0)
                return RefuseAfterResolving(resolvedOnRequest,
                    "The assembly has no part to weigh" +
                    (suppressedOnes.Length > 0 ? ": suppressed " + string.Join(", ", suppressedOnes) : "") +
                    (envelopes.Count > 0 ? "; envelopes (SOLIDWORKS leaves them out of the mass) " +
                                           string.Join(", ", AssemblyComponents.Names(envelopes)) : "") + ".");

            var withoutMaterial = new List<string>();
            var noDocument = new List<string>();
            var otherConfiguration = new List<string>();
            double sum = 0;
            bool readSum = true;
            foreach (AssemblyComponent item in parts)
            {
                var c = item.Component;
                var doc = item.Document;
                if (doc == null) { noDocument.Add(item.Name); continue; }
                // THE CONFIGURATION OF THE COMPONENT, NOT THE ACTIVE ONE OF THE PART
                // (adversarial review of 27/9/2026). A Toolbox screw — one file,
                // one configuration per size — used as M10x40 in an assembly and saved
                // active on M6x16: the part weighed alone is the other one, the sum does not
                // add up, and the operation stopped with a false cause. That part
                // stays out of the check sum (the mass of the assembly is
                // right: SOLIDWORKS weighs the configuration the component uses), and
                // the material is asked of the configuration used.
                string used = null, activate = null;
                try { used = c.ReferencedConfiguration; } catch { }
                try { activate = (doc.GetActiveConfiguration() as IConfiguration)?.Name; } catch { }
                bool other = !string.IsNullOrEmpty(used) && !string.IsNullOrEmpty(activate) &&
                             !string.Equals(used, activate, StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(PartMaterial(doc, other ? used : ""))) withoutMaterial.Add(item.Name);
                if (other)
                {
                    otherConfiguration.Add(item.Name + " (uses «" + used + "», the part is open in configuration «" + activate + "»)");
                    readSum = false;
                    continue;
                }
                var m = PartMass(doc);
                if (m == null) readSum = false; else sum += m.Value;
            }

            if (noDocument.Count > 0)
                return RefuseAfterResolving(resolvedOnRequest,
                    "I could not read the document of " + string.Join(", ", noDocument) + " (resolved, but GetModelDoc2 " +
                    "answered null): its material is not known, so no mass.");

            // ONE OBJECT PER NUMBER, as for the part: IMassProperty changes its
            // answers depending on what it was asked before (MeasureOperations).
            var take = new Func<Func<IMassProperty, object>, object>(read =>
            {
                try
                {
                    var mp = ApiHelpers.NewMassProperty(model, null, out _);
                    if (mp == null) return null;
                    return read(mp);
                }
                catch { return null; }
            });
            object m0 = take(p => p.Mass);
            object v0 = take(p => p.Volume);
            object c0 = take(p => p.CenterOfMass);
            if (m0 == null || v0 == null)
                return RefuseAfterResolving(resolvedOnRequest, "SOLIDWORKS did not give the mass properties of the assembly. I do not give you half numbers.");
            double mass = Convert.ToDouble(m0);

            var measurements = new Dictionary<string, object>
            {
                ["document"] = "assembly",
                ["components_weighed"] = parts.Count,
                ["components_suppressed"] = suppressedOnes,
                ["volume_mm3"] = Math.Round(Convert.ToDouble(v0) * 1e9, 2),
                ["center_of_mass_mm"] = Scale(c0 as double[], 1000.0),
                ["units"] = "millimeter kilogram",
            };
            if (envelopes.Count > 0) measurements["envelope_components"] = AssemblyComponents.Names(envelopes);
            var hidden = parts.Where(c => c.Hidden).ToList();
            if (hidden.Count > 0) measurements["hidden_components_weighed"] = AssemblyComponents.Names(hidden);
            if (resolvedOnRequest != null) measurements["lightweight_resolved_on_request"] = resolvedOnRequest;
            if (otherConfiguration.Count > 0)
                measurements["sum_not_checked_for_configs"] = otherConfiguration.ToArray();

            if (withoutMaterial.Count > 0)
            {
                // MASS WITHOUT A MATERIAL IS NOT ZERO, IT IS «UNKNOWN» — as for the
                // part: SOLIDWORKS would answer with the density of water.
                measurements["mass_kg"] = null;
                measurements["without_material"] = withoutMaterial.ToArray();
                measurements["why_no_mass"] =
                    "For " + string.Join(", ", withoutMaterial) + " no material was given: the mass of the " +
                    "assembly is not known. SOLIDWORKS would answer all the same with the default density " +
                    "(1000 kg/m3, water): a believable and wrong number. Give the material to the parts " +
                    "(«apply_material» on each one) and ask me again.";
            }
            else
            {
                measurements["mass_kg"] = Math.Round(mass, 4);
                // THE SUM REDONE: the mass of the assembly against the sum of its
                // parts read one by one. If they diverge, one of the two readings is not
                // what it seems (a material set on the component, different from
                // that of the part; a component that counts twice).
                if (readSum)
                {
                    measurements["sum_of_parts_kg"] = Math.Round(sum, 4);
                    double deviation = mass > 0 ? Math.Abs(sum - mass) / mass : double.NaN;
                    measurements["deviation_on_sum"] = double.IsNaN(deviation) ? null : (object)Math.Round(deviation, 6);
                    if (double.IsNaN(deviation) || deviation > 0.001)
                        return RefuseAfterResolving(resolvedOnRequest,
                            "The mass of the assembly (" + mass.ToString("0.####") + " kg) and the sum of its parts (" +
                            sum.ToString("0.####") + " kg) do not match: one of the two readings is not what it " +
                            "seems (a material put on the component in the assembly? a component counted twice?). I do " +
                            "not give you a number.");
                }
                else measurements["sum_of_parts_kg"] = null;   // a part did not let itself be weighed, or uses another configuration: no check
            }
            measurements["what_it_does_not_check"] =
                "Suppressed components do not weigh, and they are listed. Envelopes do not weigh either: SOLIDWORKS " +
                "leaves them out of the assembly mass (measured), and so does the sum. Hidden components weigh. A " +
                "material set on the COMPONENT in the assembly, different from the part's, is not in the sum of the " +
                "parts: if there is one, the gap says so and I stop.";
            _log("[agent] mass of the assembly: " + parts.Count + " parts, " +
                 (withoutMaterial.Count > 0 ? "without material" : mass.ToString("0.###") + " kg"));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// A refusal of the assembly mass that comes AFTER «resolve_lightweight»
        /// resolved parts (review of Z-C21.11): the parts stay resolved, and the
        /// refusal says so as the success does, instead of changing the assembly
        /// without a word.
        /// </summary>
        private Outcome RefuseAfterResolving(string resolvedOnRequest, string message)
        {
            string said = resolvedOnRequest == null ? message : message + " " + resolvedOnRequest;
            return Failed(said);
        }

        /// <summary>
        /// Z-D1: the true box of the assembly, part by part, in its coordinates.
        ///
        /// Z-C21.11 (probe zc2111/probe_alleggeriti, 30/9/2026): LIGHTWEIGHT parts are
        /// measured too, since GetBodies3 gives their bodies with the volume of the file
        /// and Transform2 is there (IComponent2~GetBodies3.html: «Supports lightweight
        /// components»). Up to library 0.146.0 they were taken for suppressed and left
        /// out: a partial box answered as a success, or no box at all on an assembly
        /// opened lightweight. Envelopes and hidden components are in the box, and the
        /// answer names them.
        /// </summary>
        private Outcome AssemblyBoundingBox(IModelDoc2 model, Dictionary<string, object> a)
        {
            if (!string.IsNullOrWhiteSpace(Str(a, "body_uid", null)))
                return Failed(
                    "On an assembly «body_uid» does not apply: I measure the bounding box of the whole assembly. For a " +
                    "single part, open it and ask me there.");
            var assemblyDoc = model as IAssemblyDoc;
            string refusal;
            var all = ComponentsOrRefusal(assemblyDoc, "box", out refusal);
            if (all == null) return Failed(refusal);
            var suppressedOnes = AssemblyComponents.Names(all.Where(c => c.Load == ComponentLoad.Suppressed));
            var parts = all.Where(c => c.IsPart && c.Load != ComponentLoad.Suppressed).ToList();
            if (parts.Count == 0)
                return Failed("The assembly has no part to measure" +
                               (suppressedOnes.Length > 0 ? ": every part is suppressed" : "") + ".");
            var list = new List<object>();
            double[] everything = null;
            foreach (AssemblyComponent item in parts)
            {
                var c = item.Component;
                object info;
                object[] bodies = null;
                bool bodiesUnread = false;
                try { bodies = c.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[]; } catch { bodiesUnread = true; }
                // A PART LEFT OUT IN SILENCE IS A PARTIAL BOX ANSWERED AS A SUCCESS
                // (finding N5): a throw refuses, and so does a lightweight part that
                // answers no solid body (the probe read the bodies of every
                // lightweight part; once resolved, a part without solids is skipped
                // below as before).
                if (bodiesUnread)
                    return Failed("I could not read the bodies of «" + item.Name + "» (GetBodies3 threw): its box " +
                                   "would be missing from the assembly's. No box.");
                if (bodies == null || bodies.Length == 0)
                {
                    if (item.Load == ComponentLoad.Lightweight)
                        return Failed("The lightweight part «" + item.Name + "» answered no solid body (GetBodies3): " +
                                       "its box would be missing from the assembly's. Resolve it and ask again.");
                    continue;   // a resolved part without solid bodies (surfaces only): nothing to box
                }
                MathTransform tr = null;
                try { tr = c.Transform2; } catch { }
                if (tr == null)
                    return Failed("I cannot read where «" + item.Name + "» is in the assembly (Transform2): without " +
                                   "it, its box would be in the wrong place. I do not give you a bounding box.");
                double[] its = null;
                foreach (var ob in bodies)
                {
                    // THE BODIES COME IN THE SPACE OF THE PART (measured): a copy,
                    // brought into the assembly, and the box of that.
                    IBody2 copy = null;
                    try { copy = (ob as IBody2)?.Copy() as IBody2; } catch { }
                    bool brought = false;
                    try { brought = copy != null && copy.ApplyTransform(tr); } catch { }
                    var sc = brought ? BodyBox(copy) : null;
                    if (sc == null)
                        return Failed("I cannot bring a body of «" + item.Name + "» into " +
                                       "the assembly or read its box: I do not give you half a bounding box.");
                    if (its == null) its = (double[])sc.Clone();
                    else { for (int k = 0; k < 3; k++) its[k] = Math.Min(its[k], sc[k]); for (int k = 3; k < 6; k++) its[k] = Math.Max(its[k], sc[k]); }
                }
                if (its == null) continue;
                list.Add(new Dictionary<string, object>
                {
                    ["component"] = item.Name,
                    ["from_mm"] = new[] { Round(its[0]), Round(its[1]), Round(its[2]) },
                    ["to_mm"] = new[] { Round(its[3]), Round(its[4]), Round(its[5]) },
                });
                if (everything == null) everything = (double[])its.Clone();
                else { for (int k = 0; k < 3; k++) everything[k] = Math.Min(everything[k], its[k]); for (int k = 3; k < 6; k++) everything[k] = Math.Max(everything[k], its[k]); }
            }
            if (everything == null) return Failed("No component of the assembly has a solid body to measure.");
            var measurements = new Dictionary<string, object>
            {
                ["document"] = "assembly",
                ["components"] = list.ToArray(),
                ["components_suppressed"] = suppressedOnes,
                ["from_mm"] = new[] { Round(everything[0]), Round(everything[1]), Round(everything[2]) },
                ["to_mm"] = new[] { Round(everything[3]), Round(everything[4]), Round(everything[5]) },
                ["bounding_box_mm"] = new[] { Round(everything[3] - everything[0]), Round(everything[4] - everything[1]), Round(everything[5] - everything[2]) },
                ["units"] = "millimeters, in the coordinate system of the assembly",
                ["what_it_does_not_check"] =
                    "It is the box aligned to the assembly axes, not the smallest one. Suppressed components are " +
                    "not in it (they are listed). Lightweight parts are in it (their bodies are read without " +
                    "resolving them), and so are envelopes and hidden components, each listed.",
            };
            var light = parts.Where(c => c.Load == ComponentLoad.Lightweight).ToList();
            if (light.Count > 0) measurements["lightweight_components_measured"] = AssemblyComponents.Names(light);
            var envelopes = parts.Where(c => c.Envelope).ToList();
            if (envelopes.Count > 0) measurements["envelope_components"] = AssemblyComponents.Names(envelopes);
            var hidden = parts.Where(c => c.Hidden).ToList();
            if (hidden.Count > 0) measurements["hidden_components"] = AssemblyComponents.Names(hidden);
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// Z-D6: the solid bodies that the STEP of this assembly must contain.
        ///
        /// PER PART, NOT PER INSTANCE (adversarial review of 27/9/2026). The
        /// STEP writes the structure of the assembly: the shape of a part appears ONCE
        /// and the instances are occurrences with their transformation. Measured
        /// on 28/9/2026 (case assemblies/two_equal_bases_one_step: the base twice
        /// and the lid once) — in the file 2 solids. The count per instance
        /// expected 3, and refused a right STEP on every assembly with repeated
        /// screws or feet. A part is its file PLUS the configuration: two
        /// sizes of the same screw are two shapes.
        ///
        /// AN INSTANCE CUT IN THE ASSEMBLY IS A SHAPE OF ITS OWN (finding C4-2,
        /// measured on 28/9/2026 by the Z-C21.3 probe of the user bodies in a STEP:
        /// part P 40 x 40 x 10 inserted twice, an assembly cut through the first
        /// instance only). IComponent2~GetBodies3.html, Remarks: «Returns an array
        /// in BodiesInfo containing information about bodies that indicates whether
        /// they are normal or user bodies. User bodies are original component bodies
        /// that have been modified in an assembly». Measured: a 10 x 10 hole gave
        /// the cut instance one swUserBody_e body (15000 mm3) and the other one
        /// swNormalBody_e (16000 mm3), and the STEP held 2 solids where this count
        /// said 1; a slot splitting the instance gave 2 user bodies (7200 + 7200)
        /// and 3 solids in the STEP against 2 here. So a STEP that had lost the cut
        /// instance would have passed. Now every instance with a user body adds
        /// ALL its bodies, and only the instances with normal bodies are counted
        /// once per file and configuration: 2 and 3, as in the file (and 1 and 2
        /// for the uncut assembly and two configurations of the same file, as before).
        ///
        /// WHICH COMPONENTS (Z-C21.11, probe zc2111/probe_alleggeriti, 30/9/2026):
        ///  - HIDDEN components are NOT written: A + hidden B gave 1 solid, and the
        ///    old count asked 2 and refused a right file. They are left out here and
        ///    named in <paramref name="hiddenLeftOut"/> (a child of a hidden
        ///    sub-assembly too: not measured, and on the safe side, since leaving a
        ///    component out only lowers what the file must hold);
        ///  - LIGHTWEIGHT parts are written: the export resolved them (every one read
        ///    resolved after SaveAs3), and this count, made after the export, sees
        ///    them; up to library 0.146.0 they were taken for suppressed;
        ///  - ENVELOPES are written (A + envelope E: 2 solids, as on 28/9) and count.
        /// </summary>
        private static int ExpectedSolidsInAssembly(IAssemblyDoc assemblyDoc, List<string> hiddenLeftOut)
        {
            var keys = new List<string>();
            var bodies = new List<int>();
            var userBodies = new List<int>();
            var all = AssemblyComponents.Read(assemblyDoc);
            if (all == null) return -1;
            foreach (AssemblyComponent item in all)
            {
                if (item.Load == ComponentLoad.Suppressed) continue;
                if (item.Load == ComponentLoad.Unknown) return -1;
                // A sub-assembly holds no solid of its own (its parts are in the
                // list); a component of unknown kind makes the count unknown.
                if (!item.IsPart) { if (item.IsSubAssembly) continue; return -1; }
                if (item.Hidden) { if (hiddenLeftOut != null) hiddenLeftOut.Add(item.Name); continue; }
                var c = item.Component;
                string key;
                try { key = (c.GetPathName() ?? "") + "|" + (c.ReferencedConfiguration ?? ""); }
                catch { return -1; }
                object info;
                object[] found;
                try { found = c.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[]; }
                catch { return -1; }
                keys.Add(key);
                // ZERO-E-ZERO: a real zero. Suppressed components are left out above,
                // an unreadable load state and a failed call return -1; GetBodies3
                // answers null for a component that has no solid body (a
                // surface-only or empty part), which is 0 solids in the STEP too.
                bodies.Add(found == null ? 0 : found.Length);
                userBodies.Add(UserBodiesIn(info));
            }
            return ExpectedStepSolids(keys.ToArray(), bodies.ToArray(), userBodies.ToArray());
        }

        /// <summary>
        /// How many components of the assembly read lightweight now
        /// (IAssemblyDoc.GetLightWeightComponentCount), -1 when it cannot be read.
        /// The STEP export compares it before and after: SaveAs3 resolved the
        /// lightweight components of the probe's assemblies, and a change the user
        /// did not ask for is said.
        /// </summary>
        private static int LightweightCount(IAssemblyDoc assemblyDoc)
        {
            try { return assemblyDoc == null ? -1 : assemblyDoc.GetLightWeightComponentCount(); }
            catch { return -1; }
        }

        /// <summary>
        /// C4-2: how many entries of GetBodies3's BodiesInfo are swUserBody_e (0;
        /// swNormalBody_e is 1, swconst page swBodyInfo_e.html). The probe read an
        /// Int32[]; an object[] of numbers is read the same way. Nothing readable
        /// is 0: the instance is then counted as before, once per file.
        /// </summary>
        private static int UserBodiesIn(object info)
        {
            int users = 0;
            var numbers = info as int[];
            if (numbers != null)
            {
                foreach (int k in numbers) if (k == (int)swBodyInfo_e.swUserBody_e) users++;
                return users;
            }
            var boxed = info as object[];
            if (boxed == null) return 0;
            foreach (object o in boxed)
            {
                try { if (Convert.ToInt32(o) == (int)swBodyInfo_e.swUserBody_e) users++; } catch { }
            }
            return users;
        }

        /// <summary>
        /// C4-2, the rule without SOLIDWORKS (the contract group in Prove calls it
        /// with the probe's numbers): per non-suppressed part instance its file and
        /// configuration key, its solid bodies and how many of them are user
        /// bodies. An instance with a user body was changed in the assembly and is
        /// a shape of its own in the STEP: all its bodies count. The others count
        /// once per key. -1 if the arrays do not line up or a count is negative.
        /// </summary>
        internal static int ExpectedStepSolids(string[] keys, int[] bodies, int[] userBodies)
        {
            if (keys == null || bodies == null || userBodies == null ||
                bodies.Length != keys.Length || userBodies.Length != keys.Length) return -1;
            int n = 0;
            var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Length; i++)
            {
                if (bodies[i] < 0 || userBodies[i] < 0) return -1;
                if (userBodies[i] > 0) { n += bodies[i]; continue; }
                if (counted.Add(keys[i] ?? "")) n += bodies[i];
            }
            return n;
        }

        /// <summary>
        /// How many solids there are in the text of the STEP; -1 if it cannot be read. It counts
        /// per INSTANCE of entity (the text between two «;»): a solid with a cavity is
        /// written BREP_WITH_VOIDS, which does not contain the word MANIFOLD_SOLID_BREP, and
        /// a complex entity can name both — counted only once
        /// (adversarial review of 27/9/2026).
        /// </summary>
        private static int SolidsInStep(string filePath)
        {
            try
            {
                int n = 0;
                foreach (var instance in File.ReadAllText(filePath).Split(';'))
                    if (instance.IndexOf("MANIFOLD_SOLID_BREP", StringComparison.Ordinal) >= 0 ||
                        instance.IndexOf("BREP_WITH_VOIDS", StringComparison.Ordinal) >= 0) n++;
                return n;
            }
            catch { return -1; }
        }
    }
}
