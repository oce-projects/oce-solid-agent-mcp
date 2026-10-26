using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// EYES ON THE ASSEMBLY (entry GA1).
    ///
    /// ── WHAT IT WAS BEFORE, AND WHY IT WAS NOT ENOUGH ────────────────────
    ///
    /// Up to 0.91.0 `DocumentState.Read` had an `if (part == null)` that
    /// answered, for ANY assembly:
    ///
    ///     solid_body: false
    ///     note: "assembly document: the assistant does not modify it yet"
    ///
    /// and returned. It was honest — no assembly operation existed — but it
    /// left the model **blind**: the state is the only thing it sees, and from
    /// that line it cannot know how many components there are, what they are called,
    /// which ones are suppressed. It could not even REFUSE knowingly, because
    /// it knew nothing of what was in front of it.
    ///
    /// ── THE DIFFERENCE THAT MATTERS WITH FACES ───────────────────────────
    ///
    /// The uids of faces and edges are **POSITIONAL**: they are renumbered
    /// by decreasing area at every reading, and after an edit they must be read again.
    /// It is the most expensive trap of the project.
    ///
    /// **For components it is not so, and this must be exploited.** Every component has a
    /// `Name2` unique inside the assembly («base-1», «base-2», «cover-1»):
    /// SOLIDWORKS guarantees it, because it is the name with which it tells them apart in the
    /// structure. So here the order can be made **stable** instead of
    /// positional: it is sorted by `Name2` and `C1..Cn` stays the same between two
    /// readings, provided nobody added or removed components.
    ///
    /// It is not an aesthetic detail: a uid that changes by itself forces you to
    /// read again before every step, and the day someone forgets
    /// the operation goes on the wrong component **without giving an error**.
    ///
    /// ── THE RULE ON TRY/CATCH, which here counts double ──────────────────
    ///
    /// A single `try` around everything would make the whole list disappear because of
    /// **one** component with a broken reference — and broken references, in
    /// a real assembly, are the normality, not the exception. Each field has its
    /// own `try`, as in `DrawingState`: what cannot be read is left unsaid, the
    /// rest arrives.
    ///
    /// And above all: if this function threw, it would break
    /// `DocumentState.Read` **for normal parts too**, which go through the
    /// same call.
    /// </summary>
    public partial class DocumentState
    {
        /// <summary>How many components are described in full, at most.</summary>
        ///
        /// Same reasoning as the ceiling on faces (entry E12): a real
        /// steelwork assembly has hundreds of them, and writing them all would drive out of the
        /// context the things that are needed. Whoever has more KNOWS it, because the
        /// total count is written anyway.
        private const int MaxComponents = 40;

        /// <summary>
        /// The components in the order that decides the uids `C1..Cn`.
        ///
        /// ── WHY IT IS IN A METHOD AND NOT INSIDE `DescribeAssembly` ───────
        ///
        /// This order **is the meaning of `C1`**. The state uses it to
        /// number, and `mate` (entry GA4) uses it to find again the component
        /// that the user named. If the two lists were built in two
        /// places, it would be enough for one to change for **`C1` to mean two
        /// different things** — and then the wrong component would be mated
        /// without anything giving an error. It is the defect that gives no error in
        /// its most banal and most expensive form: two copies of the same rule.
        ///
        /// The order is by `Name2`, which inside an assembly is unique: so
        /// `C1..Cn` stays the same between two readings, unlike the uids
        /// of faces, which are positional and are renumbered.
        /// </summary>
        internal static List<KeyValuePair<string, IComponent2>> ComponentsInOrder(object[] all)
        {
            var entries = new List<KeyValuePair<string, IComponent2>>();
            if (all == null) return entries;
            foreach (var o in all)
            {
                var c = o as IComponent2;
                if (c == null) continue;
                string itemName = null;
                try { itemName = c.Name2; } catch { }
                entries.Add(new KeyValuePair<string, IComponent2>(itemName ?? "", c));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return entries;
        }

        /// <summary>
        /// THE NAMES OF THE TOP-LEVEL COMPONENTS (findings L11-3 and L11-5,
        /// lot Z-C21.5), from IAssemblyDoc.GetComponents(true); null when the
        /// list cannot be read, which is «I do not know», not «none».
        ///
        /// It is the one rule for «top level» in the library, for the same
        /// reason as <see cref="ComponentsInOrder"/>: the state publishes
        /// «top_level» from it, and fix_component and replace_component
        /// refuse a nested component with it. Two copies of the rule could
        /// disagree, and a component the state calls top-level would be
        /// refused, or the other way round.
        ///
        /// Name2 is unique inside an assembly, and for a component inside a
        /// sub-assembly it carries the whole path («subAssem1-2/Part1-1»,
        /// IComponent2~Name2.html, Remarks), so a nested component never
        /// shares its name with a top-level one.
        /// </summary>
        internal static HashSet<string> TopLevelNames(object[] topLevel)
        {
            if (topLevel == null) return null;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in topLevel)
            {
                var component = o as IComponent2;
                if (component == null) continue;
                string name = null;
                try { name = component.Name2; } catch { }
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names;
        }

        /// <summary>Components, counts and configuration of an assembly.</summary>
        private static void DescribeAssembly(IAssemblyDoc assemblyDoc, Dictionary<string, object> data)
        {
            if (assemblyDoc == null)
            {
                data["note"] = "unreadable assembly";
                return;
            }

            // `false` = ALL the components, also inside the subassemblies.
            //
            // The choice is deliberate and must be stated: with `true` only the
            // first level would be seen, and a subassembly would appear as **one** component
            // with nothing inside. The model would read «2 components» on an
            // assembly that has thirty, and every reasoning of its would be
            // plausible and wrong. The count of the first level is written
            // all the same, separately, because it is the other real question.
            object[] all = null;
            try { all = assemblyDoc.GetComponents(false) as object[]; } catch { }
            if (all == null)
            {
                data["note"] = "the assembly did not answer the list of components";
                return;
            }

            // The top-level names serve twice: the count, and «top_level» on
            // each component below (L11-3, Z-C21.5).
            HashSet<string> topLevelNames = null;
            try
            {
                var first = assemblyDoc.GetComponents(true) as object[];
                if (first != null) data["components_top_level"] = first.Length;
                topLevelNames = TopLevelNames(first);
            }
            catch { }

            data["how_many_components"] = all.Length;

            // ── THE STABLE ORDER ─────────────────────────────────────────
            //
            // It is sorted by `Name2`, which inside an assembly is unique. The order in
            // which `GetComponents` returns them is not declared anywhere:
            // trusting it would mean uids that change when SOLIDWORKS
            // decides to change it, that is, the trap of the faces, repeated where
            // it could be avoided.
            var entries = ComponentsInOrder(all);

            var list = new List<Dictionary<string, object>>();
            int suppressedOnes = 0, light = 0, grounded = 0;
            int counted = 0, resting = 0;
            int nested = 0;
            bool degreesReadFailed = false;
            var free = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var c = entries[i].Value;
                var entry = new Dictionary<string, object>
                {
                    ["uid"] = "C" + (i + 1),
                    ["name"] = entries[i].Key,
                };

                // TOP LEVEL OR INSIDE A SUB-ASSEMBLY (L11-3, Z-C21.5). The list
                // above holds every level, and some answers only hold for the
                // top one: IsFixed «only applies to the top level of components»
                // (IComponent2~IsFixed.html), and ReplaceComponents2 wants «a
                // top-level component». Null when GetComponents(true) did not
                // answer: «I do not know», not «no».
                bool? topLevel = topLevelNames == null ? (bool?)null : topLevelNames.Contains(entries[i].Key);
                entry["top_level"] = topLevel;
                if (topLevel == false) nested++;

                try
                {
                    var p = c.GetPathName();
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        entry["path"] = p;
                        // IS THE FILE THERE? (entry Z-B3) A component whose file is missing
                        // on disk is shown suppressed by the assembly, and nothing else
                        // says so. It is written only when missing: it is the rare case.
                        if (!System.IO.File.Exists(p)) entry["file_missing"] = true;
                    }
                }
                catch { }

                try
                {
                    var cfg = c.ReferencedConfiguration;
                    if (!string.IsNullOrWhiteSpace(cfg)) entry["configuration"] = cfg;
                }
                catch { }

                // THE STATE IS WRITTEN WITH THE NAME, NOT THE NUMBER.
                // `GetSuppression2` answers an integer of `swComponentSuppressionState_e`:
                // «2» says nothing to whoever reads, «resolved» does. It is the same
                // rule as the defects of `Check3`, already paid for elsewhere.
                try
                {
                    int docState = c.GetSuppression2();
                    entry["state"] = StateName(docState);
                    if (docState == (int)swComponentSuppressionState_e.swComponentSuppressed) suppressedOnes++;
                    if (docState == (int)swComponentSuppressionState_e.swComponentLightweight ||
                        docState == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
                        light++;
                }
                catch { }

                // FIXED OR FREE. It is the first real question about an assembly — «is it
                // constrained?» — and its cheapest half: a fixed component
                // does not move because someone nailed it down, not
                // because it is mated. Confusing the two makes an assembly that is not
                // constrained be believed constrained.
                //
                // NOT ON A NESTED COMPONENT (L11-3): the guide says IsFixed does
                // not apply there («To get the actual state of sub-assemblies,
                // you must get the IModelDoc2 object of the subassembly»), so
                // its answer from the root is not published: the fixed flag is
                // null, «not known from here», and the component is not counted.
                if (topLevel == false) entry["grounded"] = null;
                else
                {
                    try
                    {
                        bool f = c.IsFixed();
                        entry["grounded"] = f;
                        if (f) grounded++;
                    }
                    catch { }
                }

                // ── WHERE THE ORIGIN IS, IN MM (part of GA2) ────────────
                //
                // THE FIELD IS CALLED `origin_mm` AND NOT `position_mm`, AND IT IS
                // THE REPAIR OF A DEFECT THAT GAVE NO ERROR.
                //
                // Measured on 17/9/2026: **`AddComponent5(x, y, z)` places the
                // CENTRE of the bounding box of the component, not its origin.** A
                // part extruded in one direction only — which is what
                // `extrude_rectangle` does — has its origin on a face, not at half
                // thickness: inserted at `(0, 0, 30)`, its origin ends up at
                // `(0, -2, 30)` if it is 4 thick, at `(0, -5, 0)` if it is 10 thick and
                // it is asked at zero. Predicted before measuring it, and measured.
                //
                // Calling this field «position» invited comparing it with
                // the point ASKED, and the two almost never coincide: neither
                // of the two numbers is wrong, they are two different things, and nothing
                // said so. The name now says it.
                //
                //
                // `GetTotalTransform(false)`: the parameter is called
                // `IncludePresentationXform`, and `true` would give the position
                // **in the exploded view** — a plausible and wrong number
                // every time an exploded view exists.
                //
                // THE INDEX OF THE TRANSLATION IS NOT ASSUMED, IT IS PROVEN. The
                // classic shape of `MathTransform.ArrayData` is 9 numbers of
                // rotation, 3 of translation, 1 of scale — so 9, 10, 11.
                // «Classic» however is exactly the kind of thing that in
                // this project has already proved false (the normal of the
                // plane was in the third ROW, not in the third column, entry
                // GP9). So the bench case inserts a component at
                // **z = 30 mm** and demands to read back `[0, 0, 30]`: if the
                // indices were others, it turns red here instead of in the
                // workshop.
                var location = ComponentPositionMm(c);
                if (location != null) entry["origin_mm"] = location;

                // ── HOW IT IS TURNED (brief R12) ────────────────────────
                // The other half of the placement: without it a component
                // turned over reads like one that is not, and «place_component»
                // could not be checked from the state. Same transform as
                // origin_mm; rows = the component's axes in the assembly, the
                // layout the direction test of Z-C14 measured
                // (SwOperations.AxesValue: «+X -Y -Z» when every axis lies on an
                // axis of the assembly).
                var axes = ComponentAxesValue(c);
                if (axes != null) entry["axes"] = axes;

                // The mates: for now the COUNT. The format of what
                // `GetMates()` returns is `Object` and must be established before
                // reading inside it (entry GA4); the number instead is counted
                // at once, and it is already something: zero mates on a
                // non-fixed component means resting.
                // `GetMates()` answers **null when they are zero**, not an empty
                // array: the count goes through `SwOperations.MatesOf`,
                // which separates «zero» from «I could not read» by looking at whether the
                // component still answers to its own name.
                int itsAcc = SwOperations.MatesOf(c);
                if (itsAcc >= 0) entry["mates"] = itsAcc;

                // ── WHICH MATES (entry Z-D3) ────────────────────────────
                // Measured on 27/9/2026 (`probe0089 zd345`): `GetMates` gives IMate2
                // objects (and IFeature); `Type` is `swMateType_e`. They are written by
                // NAME, in the order in which SOLIDWORKS gives them. «How is this
                // part mounted, hinged or resting?» is read here.
                // Zero mates (counted above) is an empty list; a GetMates
                // null with mates counted, or unreadable, is «I do not know».
                if (itsAcc == 0) entry["mates_kinds"] = new string[0];
                else if (itsAcc > 0)
                {
                    var types = MateTypes(c);
                    entry["mates_kinds"] = types;   // null if they cannot be read
                }

                // ── SUBASSEMBLY, VISIBLE, TRANSPARENT (entry Z-D4) ──────
                // A subassembly is a block that hides others: it is said.
                // `GetModelDoc2` of a suppressed one is null: «I do not know», not «no».
                try
                {
                    var its = c.GetModelDoc2();
                    entry["is_sub_assembly"] = its == null ? null : (object)(its is IAssemblyDoc);
                }
                catch { entry["is_sub_assembly"] = null; }
                // `Visible` is swComponentVisibilityState_e: 1 visible, 0 hidden.
                try
                {
                    int vis = c.Visible;
                    entry["visible"] = vis == 1 ? (object)true : vis == 0 ? (object)false : null;
                }
                catch { entry["visible"] = null; }
                // TRANSPARENCY IS NOT A FIELD OF THE COMPONENT (established on
                // 23/9): it is in `MaterialPropertyValues`, position 7, and it is there only
                // if the component has an appearance of its own (measured: 0.75 after
                // `SetComponentTransparent`). Transparent and visible are two different
                // states, not alternatives.
                try
                {
                    if (!c.HasMaterialPropertyValues()) entry["transparent"] = false;
                    else
                    {
                        var mpv = c.MaterialPropertyValues as double[];
                        entry["transparent"] = mpv != null && mpv.Length > 7 ? (object)(mpv[7] > 0) : null;
                    }
                }
                catch { entry["transparent"] = null; }

                // ── THE RESIDUAL DEGREES OF FREEDOM (entry GA5) ──────────
                //
                // It is the measure that tells an assembly from four parts in the
                // same room, and it is read with no extra call
                // compared with those that this run already makes.
                //
                // FIXED ONES ARE NOT COUNTED. A fixed component has zero degrees
                // **by definition**, not because someone mated it:
                // summing it would make an assembly **look constrained** that is so
                // only because the first part does not move. The first component
                // inserted is normally fixed, so the error would trigger
                // always, on every assembly.
                var deg = SwOperations.DegreesOfFreedom(c);
                entry["degrees_of_freedom_state"] = deg.State;
                if (deg.Available) entry["degrees_of_freedom_remaining"] = deg.HowMany;
                else degreesReadFailed = true;

                // «IS IT JUST RESTING?» IS ANSWERED WITH TWO THINGS THAT WORK.
                //
                // The degrees of freedom cannot be had (see
                // `SwOperations.DegreesOfFreedom`). What is known for certain is that:
                // a **non-fixed** component with **zero mates** is
                // just resting there. How much it is free is not known, and is not
                // said: *an invented number is worse than an absent number*.
                bool isFixed = entry.ContainsKey("grounded") &&
                              Convert.ToString(entry["grounded"]).Equals(
                                  "True", StringComparison.OrdinalIgnoreCase);
                int itsMates = itsAcc;
                // A NESTED COMPONENT IS NOT JUDGED HERE (L11-3): whether it is
                // fixed is not known from the root (null above), and it moves
                // with its sub-assembly, which is judged as one top-level
                // component. Counting it as «not fixed» would call the second
                // part of every rigid sub-assembly loose.
                if (!isFixed && topLevel != false)
                {
                    counted++;
                    if (itsMates == 0)
                    {
                        entry["resting"] = true;
                        resting++;
                        if (free.Count < 8) free.Add(entries[i].Key);
                    }
                    else if (itsMates > 0) entry["resting"] = false;
                }

                if (list.Count < MaxComponents) list.Add(entry);
            }

            data["components"] = list;
            if (entries.Count > MaxComponents)
                data["components_not_listed"] = entries.Count - MaxComponents;

            // THE COUNTS ARE WRITTEN EVEN WHEN THEY ARE ZERO.
            // «suppressed: 0» is information; the absence of the field is read
            // as «I did not look at it», and they are two different things. It is the same
            // line that the README defends for regression: *a count that
            // disappears when it is zero is a count that nobody checks.*
            data["components_suppressed"] = suppressedOnes;
            data["components_lightweight"] = light;
            data["components_grounded"] = grounded;
            // How many components sit inside a sub-assembly (L11-3): they are
            // left out of the fixed count above and of the count below.
            // Written only when the top-level list was read, zero included.
            if (topLevelNames != null) data["nested_components"] = nested;

            // ── «IS THIS ASSEMBLY CONSTRAINED?» IN ONE LINE (entry GA5) ──
            //
            // The total translates the technical measure into something that whoever confirms
            // reads in three seconds. And it is the real question: an assembly with
            // twelve residual degrees is not assembled, it is resting.
            //
            // IT ALSO SAYS HOW MANY COMPONENTS WENT INTO THE COUNT, and whether
            // the reading failed on any. A total that silently covers
            // only a part is worse than no total: *a number that does not say
            // what it is computed on is a number to trust by mistake.*
            if (counted > 0)
            {
                data["components_not_grounded"] = counted;
                data["components_resting"] = resting;
                data["assembly_constrained"] = resting == 0;
                if (free.Count > 0) data["components_still_free"] = free;

                data["verdict_constraints"] = resting == 0
                    ? "All " + counted + (counted == 1 ? " non-fixed component has" : " non-fixed components have") +
                      " at least one mate. HOW MUCH freedom they still have I do not " +
                      "know: SOLIDWORKS does not give the remaining degrees of freedom from here."
                    : "It is NOT assembled: " + resting + " of " + counted +
                      (counted == 1 ? " non-fixed component" : " non-fixed components") +
                      " has no mate" +
                      (free.Count == 0 ? "" : " (" + string.Join(", ", free) + ")") +
                      ". They are just laid down there: they move if someone drags them, and " +
                      "the assembly looks assembled without being so.";
                if (nested > 0)
                    data["verdict_constraints"] += " The " + nested + " component(s) inside sub-assemblies are " +
                                                "not in this count: each moves with its sub-assembly, counted " +
                                                "as one top-level component.";
            }

            // THE THING THAT IS NOT KNOWN, SAID. It is not a technical detail: whoever
            // reads «assembly_constrained: true» must know that it means «every
            // component has at least one mate», NOT «it no longer moves».
            // One coincident mate leaves three of the six directions free (probe
            // zc14, 30/9/2026: TX, TZ and RY stay free), and this state does not
            // see them; «mate» measures them when it makes a mate (Z-C14).
            if (degreesReadFailed)
                data["what_is_not_known_about_constraints"] =
                    "SOLIDWORKS does not give the remaining degrees of freedom " +
                    "(it answers «unavailable» on every component, fixed or free). So " +
                    "«constrained» here means «has at least one mate», not " +
                    "«does not move any more»: one coincident mate leaves three of the six " +
                    "directions free (measured). «mate» says which directions each mate blocks.";

            // ── THE EXPLODED VIEW, AND IT IS NOT A WHIM (entry GA8) ──────
            //
            // `IComponent2.GetTotalTransform(true)` **includes the explode
            // transformation**. If the assembly is shown exploded, a position
            // read with `true` is the position IN THE EXPLODED VIEW: plausible,
            // and wrong by ten centimetres. Without this field there is no way
            // to notice — neither for whoever reads, nor for a bench case.
            //
            // It is ALWAYS written, even when it is false: *a field that appears
            // only when things went wrong teaches not to look for it.*
            DescribeExplodedView(assemblyDoc, data);

            // ── THE MATES BY NAME (brief R22) ────────────────────────────
            //
            // On 25/10/2026 a wrong distance mate could not be taken out: the
            // state had the count of mates of each component and their kinds,
            // and no name to cite. «suppress_feature "Distanza1"» was refused
            // because the name was not in the reading.
            DescribeMates(assemblyDoc as IModelDoc2, entries, data);

            // ── THE ACTIVE CONFIGURATION ─────────────────────────────────
            // An assembly has one just like a part, and getting it wrong means measuring
            // a different variant from the one that was confirmed.
            try
            {
                var doc = assemblyDoc as IModelDoc2;
                var cfg = doc == null ? null : doc.GetActiveConfiguration() as IConfiguration;
                if (cfg != null)
                {
                    data["configuration"] = cfg.Name;
                    DescribeBom(cfg, data);
                }
            }
            catch { }

            // ── WHAT STILL CANNOT BE DONE, SAID HERE ─────────────────────
            //
            // The note stays, but it is no longer the same: before it said «does not
            // modify it yet» and that was all, that is, it closed the discussion. Now the
            // state is there, and the note says **what is missing**, which is another thing.
            // A model that reads «I see four components but I do not know how to
            // mate them» knows what it can ask; one that reads «I do not modify it»
            // can only stop.
            data["solid_body"] = false;
            data["what_cannot_be_done_yet"] =
                "Of this assembly I can say how it is made, not change it: inserting " +
                "components, mating them and reading their remaining degrees of " +
                "freedom are not operations yet. The positions of the components " +
                "are not in this state.";
        }

        /// <summary>
        /// Where the component is, in mm, or `null` if it could not be read.
        ///
        /// It does NOT use `GetXform`, which entry GA2 named: **it does not exist
        /// on `IComponent2`** (verified by reflection on 16/9/2026; there are
        /// `get_Transform`, `get_Transform2`, `get_PresentationTransform` and
        /// `GetSpecificTransform`). It was a remembered name, not a read one.
        /// </summary>
        private static object[] ComponentPositionMm(IComponent2 c)
        {
            try
            {
                var t = c.GetTotalTransform(false) as IMathTransform;
                if (t == null) return null;
                var d = t.ArrayData as double[];
                // Fewer than twelve numbers means that the shape is NOT what
                // is believed: better to write nothing than to write three
                // numbers taken from the wrong place. An absent field shows;
                // a wrong coordinate goes unseen.
                if (d == null || d.Length < 12) return null;
                return new object[]
                {
                    Math.Round(Units.Mm(d[9]), 3),
                    Math.Round(Units.Mm(d[10]), 3),
                    Math.Round(Units.Mm(d[11]), 3),
                };
            }
            catch { return null; }
        }

        /// <summary>The axes of the component in the assembly (SwOperations.AxesValue), from GetTotalTransform(false) as origin_mm; null when unreadable.</summary>
        private static object ComponentAxesValue(IComponent2 c)
        {
            try
            {
                var t = c.GetTotalTransform(false) as IMathTransform;
                var d = t == null ? null : t.ArrayData as double[];
                return d == null || d.Length < 12 ? null : SwOperations.AxesValue(d);
            }
            catch { return null; }
        }

        /// <summary>
        /// HOW THIS CONFIGURATION WILL GO INTO THE BOM (entry GA6).
        ///
        /// THERE ARE SIX FIELDS, NOT FOUR. The TODO entry listed four;
        /// the reflection on the interop of this installation finds six, and
        /// the two that were missing are exactly the ones that decide whether a
        /// subassembly opens in the bill of materials:
        ///
        ///   BOMPartNoSource          where the item code comes from
        ///   UseAlternateNameInBOM    use a name different from the file
        ///   AlternateName            which one
        ///   UseDescriptionInBOM      use the description instead of the name
        ///   ShowChildComponentsInBOM show the children of a subassembly
        ///   ChildComponentDisplayInBOM how to show them
        ///
        /// WHY THEY COUNT HERE AND NOT ONLY IN THE DRAWING. A wrong bill of materials
        /// gives no error: a plausible list comes out, with pieces too
        /// many or too few in it, and whoever reads it in the workshop has no way to
        /// notice. The last two are the most dangerous: a subassembly
        /// that opens when it should not adds rows that nobody asked for.
        ///
        /// They are only READ. Writing them would be an operation, with its
        /// confirmation: here it is said how the configuration is set, so that whoever
        /// looks can notice before printing.
        /// </summary>
        private static void DescribeBom(IConfiguration cfg, Dictionary<string, object> data)
        {
            var d = new Dictionary<string, object>();

            // Each field its own try: on a real configuration it is enough for one
            // not to answer for a single try to make them all disappear.
            try { d["part_number_source"] = SourceCodeName(cfg.BOMPartNoSource); } catch { }
            try { d["use_alternative_name"] = cfg.UseAlternateNameInBOM; } catch { }
            try
            {
                var n = cfg.AlternateName;
                if (!string.IsNullOrWhiteSpace(n)) d["alternative_name"] = n;
            }
            catch { }
            try { d["use_details_in_bom"] = cfg.UseDescriptionInBOM; } catch { }
            try { d["shows_children"] = cfg.ShowChildComponentsInBOM; } catch { }
            try { d["child_display_mode"] = cfg.ChildComponentDisplayInBOM; } catch { }

            if (d.Count > 0) data["bom"] = d;
        }

        /// <summary>
        /// Where the item code in the bill of materials comes from, with the name.
        ///
        /// The values are of `swBOMPartNumberSource_e`. As for suppression:
        /// those that are not recognised are written with the number instead of
        /// being translated at random — **an invented name is worse than an honest
        /// number**, because whoever reads the name believes it.
        /// </summary>
        private static string SourceCodeName(int source)
        {
            switch (source)
            {
                case (int)swBOMPartNumberSource_e.swBOMPartNumber_DocumentName:
                    return "name of the document";
                case (int)swBOMPartNumberSource_e.swBOMPartNumber_ConfigurationName:
                    return "name of the configuration";
                case (int)swBOMPartNumberSource_e.swBOMPartNumber_ParentName:
                    return "name of the parent";
                case (int)swBOMPartNumberSource_e.swBOMPartNumber_UserSpecified:
                    return "written by hand";
                default:
                    return "unknown (" +
                           source.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>
        /// The suppression state, with the name.
        ///
        /// The values are of `swComponentSuppressionState_e`. Those that are not
        /// recognised are written with the number in brackets instead of being
        /// translated at random: **an invented name is worse than an honest number**,
        /// because whoever reads the name believes it.
        /// </summary>
        /// <summary>The types of the mates of a component, by name; null if they cannot be read.</summary>
        /// <summary>
        /// Every mate of the assembly, in the order of the tree: its uid («M1» is
        /// the first, positional like «C1» and renumbered when a mate goes), its
        /// name (SOLIDWORKS's, in the language of the session: «Coincidente1» in
        /// Italian, «Coincident1» in English), both taken by «suppress_mate» and
        /// «delete_mate»; its kind, whether it is
        /// suppressed, and the components it holds by their uid. The mates are
        /// the sub-features of the MateGroup feature, as in
        /// <c>OnlyStandardMates</c>; IMate2~MateEntity.html gives each entity and
        /// IMateEntity2~ReferenceComponent.html its component. A component of a
        /// sub-assembly, or one the list did not name, is given by its name.
        /// Written as an empty list when there is no mate; left out with
        /// «mates_note» when the tree could not be read.
        /// </summary>
        private static void DescribeMates(
            IModelDoc2 model, List<KeyValuePair<string, IComponent2>> entries, Dictionary<string, object> data)
        {
            if (model == null) return;
            var uidOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++) uidOf[entries[i].Key] = "C" + (i + 1);

            var list = new List<Dictionary<string, object>>();
            int total = 0, suppressed = 0;
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
                    var mate = sub.GetSpecificFeature2() as IMate2;
                    if (mate != null)
                    {
                        total++;
                        var entry = new Dictionary<string, object> { ["uid"] = "M" + total };
                        string name = null;
                        try { name = sub.Name; } catch { }
                        entry["name"] = name;
                        try { entry["kind"] = TypeName(mate.Type); } catch { entry["kind"] = null; }
                        bool? off = null;
                        try { off = ApiHelpers.IsSuppressedHere(sub); } catch { }
                        entry["suppressed"] = off;
                        if (off == true) suppressed++;
                        var held = new List<string>();
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
                        entry["components"] = held;
                        if (list.Count < MaxMates) list.Add(entry);
                    }
                    sub = sub.GetNextSubFeature() as IFeature;
                }
            }
            catch (Exception ex)
            {
                data["mates_note"] = "The list of mates could not be read: " + ex.Message;
                return;
            }
            data["mates_list"] = list;
            data["mates_total"] = total;
            data["mates_suppressed"] = suppressed;
            if (total > MaxMates) data["mates_not_listed"] = total - MaxMates;
        }

        /// <summary>How many mates the state lists by name.</summary>
        private const int MaxMates = 60;

        private static string[] MateTypes(IComponent2 c)
        {
            try
            {
                var rawItems = c.GetMates() as object[];
                if (rawItems == null) return null;   // the caller already knows there are some: null is «I do not know»
                var outside = new List<string>();
                foreach (var o in rawItems)
                {
                    var m = o as IMate2;
                    if (m == null) { outside.Add("?"); continue; }
                    outside.Add(TypeName(m.Type));
                }
                return outside.ToArray();
            }
            catch { return null; }
        }

        private static string TypeName(int t)
        {
            switch (t)
            {
                case (int)swMateType_e.swMateCOINCIDENT: return "coincident";
                case (int)swMateType_e.swMateCONCENTRIC: return "concentric";
                case (int)swMateType_e.swMatePERPENDICULAR: return "perpendicular";
                case (int)swMateType_e.swMatePARALLEL: return "parallel";
                case (int)swMateType_e.swMateTANGENT: return "tangent";
                case (int)swMateType_e.swMateDISTANCE: return "distance";
                case (int)swMateType_e.swMateANGLE: return "angle";
                case (int)swMateType_e.swMateSYMMETRIC: return "symmetric";
                case (int)swMateType_e.swMateWIDTH: return "width";
                case (int)swMateType_e.swMateLOCK: return "lock";
                case (int)swMateType_e.swMateHINGE: return "hinge";
                case (int)swMateType_e.swMateSLOT: return "slot";
                case (int)swMateType_e.swMateGEAR: return "gear";
                case (int)swMateType_e.swMateSCREW: return "screw";
                default:
                    var n = Enum.GetName(typeof(swMateType_e), t);
                    return n == null ? "type " + t : n.Replace("swMate", "").ToLowerInvariant();
            }
        }

        private static string StateName(int docState)
        {
            switch (docState)
            {
                case (int)swComponentSuppressionState_e.swComponentSuppressed:
                    return "suppressed";
                case (int)swComponentSuppressionState_e.swComponentLightweight:
                    return "lightweight";
                case (int)swComponentSuppressionState_e.swComponentFullyResolved:
                    return "resolved";
                case (int)swComponentSuppressionState_e.swComponentResolved:
                    return "resolved";
                case (int)swComponentSuppressionState_e.swComponentFullyLightweight:
                    return "lightweight (all)";
                case (int)swComponentSuppressionState_e.swComponentInternalIdMismatch:
                    return "identifier does not match";
                default:
                    return "unknown (" +
                           docState.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>
        /// THE EXPLODED VIEW: whether there is one, whether it is SHOWN, and how much it really moves (GA8).
        ///
        /// ── WHY IT IS IN THE STATE AND NOT ONLY IN AN OPERATION ─────────
        ///
        /// `IComponent2.GetTotalTransform(true)` includes the explode
        /// transformation. An assembly shown exploded answers, to that call,
        /// **the position in the exploded view**: plausible and wrong by
        /// ten centimetres, without any error. Whoever reads the state must
        /// be able to know it before believing a position.
        ///
        /// ── AND `exploded_view` ALONE IS NOT ENOUGH ───────────────────────────
        ///
        /// Measured on 17/9/2026, four probes: `CreateExplodedView()` answers
        /// `True`, `IsExploded` becomes `si'`, and **nothing moves** — the step
        /// that is born is called «Catena1» and has distance 0 mm. An exploded view in which
        /// no component moves is an exploded view that is of no use, and
        /// `exploded_view: true` alone would make it look done.
        ///
        /// So it also writes **how far the component that
        /// moved the most has moved**, in millimetres: it is the difference between the two
        /// transformations, and zero means zero. *A boolean field that can
        /// be true while the thing did not happen is not a measure: it is a
        /// promise.*
        /// </summary>
        private static void DescribeExplodedView(
            IAssemblyDoc assemblyDoc, Dictionary<string, object> data)
        {
            var doc = assemblyDoc as IModelDoc2;
            if (doc == null) return;

            bool exploded = false;
            string which = null;
            try { exploded = doc.Extension.IsExploded(out which); }
            catch { return; }   // if it cannot be read, no falsehood is invented

            data["exploded_view"] = exploded;
            if (exploded && !string.IsNullOrWhiteSpace(which)) data["view_exploded"] = which;

            try
            {
                var cfg = doc.ConfigurationManager.ActiveConfiguration as IConfiguration;
                if (cfg != null)
                {
                    int views = assemblyDoc.GetExplodedViewCount2(cfg.Name);
                    if (views > 0) data["views_exploded"] = views;
                    int steps = cfg.GetNumberOfExplodeSteps();
                    if (steps > 0) data["exploded_view_steps"] = steps;
                }
            }
            catch { }

            if (!exploded) return;

            // HOW MUCH IT REALLY MOVES. The maximum over all components is looked at:
            // it is enough for ONE to have moved for a position read with
            // `true` to be different from the true one.
            double maxValue = 0;
            bool read = false;
            try
            {
                var all = assemblyDoc.GetComponents(true) as object[];
                if (all != null)
                    foreach (var o in all)
                    {
                        var c = o as IComponent2;
                        if (c == null) continue;
                        double d;
                        if (!ExplodedOffsetMm(c, out d)) continue;
                        read = true;
                        if (d > maxValue) maxValue = d;
                    }
            }
            catch { }

            if (!read) return;
            data["exploded_view_max_move_mm"] = Math.Round(maxValue, 3);
            if (maxValue < 0.001)
                data["what_is_not_known_about_exploded_view"] =
                    "The exploded view is there and shown, but NO component " +
                    "moves: the maximum offset is zero. An explosion that " +
                    "separates nothing is of no use to an assembly drawing. " +
                    "On this installation no way was found to add explode steps " +
                    "from code (entry GA8).";
        }

        /// <summary>
        /// By how much the exploded view moves this component, in mm. `false` if it
        /// could not be read — which is **not** the same thing as zero.
        /// </summary>
        private static bool ExplodedOffsetMm(IComponent2 c, out double mm)
        {
            mm = 0;
            try
            {
                var conE = c.GetTotalTransform(true) as IMathTransform;
                var without = c.GetTotalTransform(false) as IMathTransform;
                if (conE == null || without == null) return false;
                var a = conE.ArrayData as double[];
                var b = without.ArrayData as double[];
                if (a == null || b == null || a.Length < 12 || b.Length < 12) return false;
                double dx = a[9] - b[9], dy = a[10] - b[10], dz = a[11] - b[11];
                mm = Units.Mm(Math.Sqrt(dx * dx + dy * dy + dz * dz));
                return true;
            }
            catch { return false; }
        }
    }
}
