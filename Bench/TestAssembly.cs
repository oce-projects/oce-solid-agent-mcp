using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
// Same reason as `DeclaredCases.cs`: `Text`, `Items`, `Resolve` and `Short`
// live in `Expectations`, which does not touch SOLIDWORKS (entry F7). `using static` lets
// them be written as if they were here.
using static Oce.Solid.Agent.Bench.Expectations;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE BENCH LEARNS TO PREPARE AN ASSEMBLY (entry GA0).
    ///
    /// ── WHY THIS ENTRY COMES BEFORE THE WHOLE G-A SECTION ────────────────
    ///
    /// Up to 16 September 2026 `NewPart()` was the ONLY way in which the
    /// bench prepared a document, and it used `swDefaultTemplatePart`. No
    /// `NewAssembly`, and `"start": { "new_part": true }` was the only form of
    /// preparation that the 270 cases knew how to express.
    ///
    /// Consequence: **no assembly operation could have a case.** All nine
    /// G-A entries could have been written, made to work by hand, and nothing
    /// would have noticed when they break — the condition that this
    /// project refuses for parts, accepted for assemblies
    /// only because the opposite was inconvenient.
    ///
    /// *An operation without a case is not an operation: it is a demonstration.*
    ///
    /// ── WHY AN ASSEMBLY IS NOT AN EMPTY DOCUMENT ─────────────────────────
    ///
    /// `AddComponent5` takes a **path on disk**: a document that was never saved
    /// cannot be inserted. So preparing an assembly is a chain of six
    /// steps, and each one can fail in its own way:
    ///
    ///   1. create the component part (the same operations as the cases),
    ///   2. SAVE IT in `Bench\work`,
    ///   3. close it,
    ///   4. create the assembly document,
    ///   5. reopen the components and insert them,
    ///   6. rebuild.
    ///
    /// And then clean up, which is the seventh: `CloseLeftovers` closes the documents, it does not
    /// delete the files.
    ///
    /// ── THE RULE THAT IS NOT BROKEN ──────────────────────────────────────
    ///
    /// Every step that does not succeed calls `BenchError`, **not** `Check(false)`.
    /// It is not a red check: it is the bench that did not manage to prepare the
    /// part, and they are two different things (entry F12). A failed preparation
    /// declared «failed check» would make one believe that something was measured.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>
        /// THE CHECK THAT THIS CAPABILITY REALLY WORKS (entry GA0).
        ///
        /// There is no assembly `.json` case yet, and the reason is
        /// honest: **there is no assembly operation to execute**, and
        /// `RunSteps` wants at least one step. A fake case, with a
        /// part operation put in just to fill, would prove something else.
        ///
        /// So the capability is tested as the bench tests itself (entries F1,
        /// F12, F13): an assembly is built, counted, and **the cleanup is
        /// looked at** — which is the half that nobody ever checks.
        ///
        /// The three checks, and why there are three:
        ///   1. **the assembly is born with two components.** If it had one or three,
        ///      every future case would run on an assembly different from the declared
        ///      one, with believable and wrong measurements.
        ///   2. **the component files exist, while they are needed.** `AddComponent5`
        ///      wants a path: if the file is not there, the component enters
        ///      «empty» or does not enter, and nobody would say so later on.
        ///   3. **the files NO LONGER exist afterwards.** It is the check that nobody
        ///      would write, and it is the one that protects from the worst defect:
        ///      *a case that succeeds because a file from the run before was left over
        ///      is not a case, it is a memento.* Without this line, the broken cleanup
        ///      would be discovered months later, looking at a full folder.
        /// </summary>
        private static void TestTheTestAssembly()
        {
            Console.WriteLine();
            Console.WriteLine("=== the bench can prepare an assembly (GA0) ===");

            var testCase = new Dictionary<string, object>
            {
                ["name"] = "ga0_assembly_of_two",
                ["assembly"] = new Dictionary<string, object>
                {
                    ["components"] = new List<object>
                    {
                        APlate("base", 100, 60, 10, null),
                        APlate("cover", 100, 60, 4, new object[] { 0.0, 0.0, 20.0 }),
                    },
                },
            };

            Preparation p = null;
            var file = new List<string>();
            try
            {
                p = PrepareDocument("ga0_assembly_of_two", testCase);
                if (p == null || p.Document == null)
                {
                    // `PrepareDocument` has already called `BenchError` and
                    // said at which of the seven steps it stopped: repeating here
                    // would add a line that says the same thing worse.
                    return;
                }
                file.AddRange(p.FilesToDelete);

                var ass = p.Document as IAssemblyDoc;
                int howMany = ass == null ? -1 : HowManyComponents(ass);
                Check("the assembly is born with two components", howMany == 2,
                      howMany + " components");

                int thereAre = 0;
                foreach (var f in file) if (File.Exists(f)) thereAre++;
                Check("the component files are there, while they are needed",
                      thereAre == file.Count, thereAre + " of " + file.Count);
            }
            finally { ThrowAwayPreparation(p); }

            int remained = 0;
            foreach (var f in file) if (File.Exists(f)) remained++;
            Check("afterwards, nothing is left in «work»", remained == 0,
                  remained == 0
                      ? file.Count + " file cancellati"
                      : remained + " files left: the next run would already find them there");
        }

        /// <summary>A component: a plate, with its position.</summary>
        private static Dictionary<string, object> APlate(
            string itemName, double length, double width, double thickness, object[] position)
        {
            var c = new Dictionary<string, object>
            {
                ["name"] = itemName,
                ["steps"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["operation"] = "extrude_rectangle",
                        ["arguments"] = new Dictionary<string, object>
                        {
                            ["plane"] = "top",
                            ["length_mm"] = length,
                            ["width_mm"] = width,
                            ["thickness_mm"] = thickness,
                            ["centered"] = true,
                        },
                    },
                },
            };
            if (position != null) c["position_mm"] = position;
            return c;
        }

        /// <summary>
        /// The document on which a case runs, plus the files to delete afterwards.
        ///
        /// It is a CLASS and not a tuple because whoever receives it must be able to
        /// pass it to `ThrowAwayPreparation` without remembering the order of two
        /// values: forgetting to delete the components leaves files that the next
        /// run finds already there, and a case that succeeds because a file
        /// from the run before was left over is not a case, it is a memento.
        /// </summary>
        internal sealed class Preparation
        {
            public IModelDoc2 Document;
            public readonly List<string> FilesToDelete = new List<string>();

            /// <summary>
            /// Documents to close indicated by NAME. It is fine for the
            /// components of an assembly: they are saved to disk before
            /// being opened, and their name no longer changes.
            /// </summary>
            public readonly List<string> TitlesToClose = new List<string>();

            /// <summary>
            /// Documents to close indicated by REFERENCE (entry K12-bis).
            ///
            /// ── WHY THE NAME IS NOT ENOUGH ──────────────────────────────
            ///
            /// A drawing is born as `Drawing38 - Sheet1` and is saved
            /// afterwards, becoming `drawing_dimensions_itself - Sheet1`. The bench
            /// marked the title **at the moment the document
            /// appeared**, that is the old one, and at the end of the case
            /// `CloseDoc("Drawing38 - Sheet1")` closed nothing **and did not
            /// say so**. The drawing stayed open, and an open drawing
            /// keeps its part open: two documents per case, fourteen
            /// at the end of the run.
            ///
            /// **The identity of a document is not its title.** It is the
            /// same lesson as the active document: the reference is kept,
            /// and the title is asked for at the moment it is needed.
            /// </summary>
            public readonly List<IModelDoc2> DocumentsToClose = new List<IModelDoc2>();
        }

        /// <summary>
        /// Prepares the document that a case asks for: a new part, or an
        /// assembly with its components inside.
        ///
        /// Returns `null` when the preparation did not succeed — and in that case
        /// it has already said why with `BenchError`.
        /// </summary>
        internal static Preparation PrepareDocument(
            string itemName, Dictionary<string, object> testCase)
        {
            var outside = new Preparation();
            var assemblyDoc = testCase != null && testCase.ContainsKey("assembly")
                ? testCase["assembly"] as Dictionary<string, object>
                : null;

            if (assemblyDoc == null)
            {
                outside.Document = NewPart();
                return outside;
            }

            var components = Items(assemblyDoc, "components");
            if (components == null || components.Count == 0)
            {
                BenchError(itemName, "«assembly» carries no component: an " +
                                    "empty assembly proves nothing");
                return null;
            }

            // ── 0. the keys of Z-C21.11, read before anything is built ────
            //
            // «envelope» and «hidden» on a component, «reopen_lightweight» on
            // the assembly: the three states in which a component answers the
            // component readers differently (lightweight, envelope, hidden),
            // measured by the probe zc2111/probe_alleggeriti (30/9/2026) and the
            // Z-C21.3 probe of envelopes and BOM children (28/9/2026). No operation of the catalogue
            // makes a component an envelope, hides it or loads it lightweight,
            // so without them none of the three could be written as a case.
            // They are read here, before the first file is written: a key
            // written wrong is a bench error that leaves nothing behind. A key
            // that is not true or false is refused, never read as false: a case
            // that asked for an envelope and ran on a normal part would measure
            // another assembly and look right.
            bool reopenLightweight;
            if (!FlagOf(itemName, assemblyDoc, "reopen_lightweight", out reopenLightweight)) return null;
            var envelopeNames = new List<string>();
            var hiddenNames = new List<string>();
            for (int i = 0; i < components.Count; i++)
            {
                var one = components[i] as Dictionary<string, object>;
                if (one == null) continue;   // said by the loop below, with its number
                string itsName = Text(one, "name") ?? ("comp" + (i + 1));
                bool envelope, hidden;
                if (!FlagOf(itemName, one, "envelope", out envelope) || !FlagOf(itemName, one, "hidden", out hidden))
                    return null;
                bool isSubassembly = Items(one, "components") != null;
                if (envelope && isSubassembly)
                {
                    BenchError(itemName, "«envelope» on the sub-assembly «" + itsName + "»: an envelope was " +
                                        "measured on a part only (probe of 28/9/2026)");
                    return null;
                }
                if ((envelope || hidden) && !ToInsert(one))
                {
                    BenchError(itemName, "«" + itsName + "» asks «envelope» or «hidden» with «to_insert» false: " +
                                        "the preparation does not insert it, so it cannot make it either");
                    return null;
                }
                if (reopenLightweight && !ToInsert(one))
                {
                    BenchError(itemName, "«reopen_lightweight» closes every document of the components, and «" +
                                        itsName + "» («to_insert» false) is there to be inserted by a step, " +
                                        "which needs its document open");
                    return null;
                }
                if (envelope) envelopeNames.Add(itsName);
                if (hidden) hiddenNames.Add(itsName);
            }

            // ── 1-3. the components, one by one, built and saved ──────────
            //
            // `to_insert: false` BUILDS BUT DOES NOT INSERT, and it is the way in
            // which a case tests `insert_component`: the file is there, it is open,
            // and the insertion is done by the STEP, not by the preparation. Without this
            // line a case could never test the operation that inserts —
            // it would find the component already inside, and would measure the work
            // of the preparation believing it measured the operation.
            //
            // The case names the path with `{WORK}\<case>_<component>.sldprt`:
            // the placeholder already exists and is valid on any machine. *A case
            // that names a path of this machine is not a check, it is a
            // memento.*
            var paths = new List<string>();
            var positions = new List<double[]>();
            // The file of each component by its name in the case, for the
            // «configurations» of step 7 (Z-C21.4), which name components.
            var pathByComponentName = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < components.Count; i++)
            {
                var c = components[i] as Dictionary<string, object>;
                if (c == null)
                {
                    BenchError(itemName, "component " + (i + 1) + " is not an object");
                    return null;
                }
                string ownName = Text(c, "name") ?? ("comp" + (i + 1));
                // A component with a list of components of its own is a
                // SUB-ASSEMBLY (Z-C21.5, L11-3/L11-5): see BuildSubassembly.
                var children = Items(c, "components");
                string filePath = Path.Combine(
                    Expectations.Work, FileName(itemName) + "_" + FileName(ownName) +
                                   (children != null ? ".sldasm" : ".sldprt"));
                pathByComponentName[ownName] = filePath;

                if (children != null)
                {
                    if (!BuildSubassembly(itemName + " · sub-assembly «" + ownName + "»",
                                          FileName(itemName) + "_" + FileName(ownName), children, filePath, outside))
                        return null;
                }
                else if (!BuildAndSave(itemName + " · component «" + ownName + "»", c, filePath))
                    return null;

                Register(outside, filePath);

                if (!ToInsert(c)) continue;
                paths.Add(filePath);
                positions.Add(PositionMm(c));
            }

            // ── 4. the assembly document ─────────────────────────────────
            IModelDoc2 doc;
            try { doc = NewAssembly(); }
            catch (Exception ex)
            {
                BenchError(itemName, "the new assembly to test on: " + Why(ex));
                ThrowAwayFiles(outside);
                return null;
            }
            outside.Document = doc;

            var ass = doc as IAssemblyDoc;
            if (ass == null)
            {
                BenchError(itemName, "the new document is not an IAssemblyDoc: " +
                                    "the default assembly template is not an assembly");
                return null;
            }

            // ── 5a. ALL the components are reopened, even those that are not
            //        inserted: `insert_component` wants the document
            //        open, and whoever calls it from the case finds it this way.
            foreach (var f in new List<string>(outside.FilesToDelete))
                if (!Open(f, itemName)) return null;

            // ── 5b. reopen the components and insert them ────────────────
            //
            // THEY ARE REOPENED, and it is not a formality: `AddComponent5` wants a
            // document ALREADY OPEN, otherwise it answers null without saying
            // why. It is the same precondition as the drawing views, where it
            // had already cost a run.
            for (int i = 0; i < paths.Count; i++)
            {
                var p = positions[i];
                IComponent2 inserted = null;
                try
                {
                    inserted = ass.AddComponent5(
                        paths[i], 0, "", false, "",
                        p[0] / 1000.0, p[1] / 1000.0, p[2] / 1000.0) as IComponent2;
                }
                catch (Exception ex)
                {
                    BenchError(itemName, "AddComponent5 su «" +
                                        Path.GetFileName(paths[i]) + "»: " + Why(ex));
                    return null;
                }
                if (inserted == null)
                {
                    BenchError(itemName, "AddComponent5 answered null on «" +
                                        Path.GetFileName(paths[i]) + "»: the component " +
                                        "did not get in, and nobody would notice later");
                    return null;
                }
            }

            // ── 6. rebuild, and COUNT ────────────────────────────────────
            //
            // The count is not a courtesy: `AddComponent5` answered an
            // IComponent2 for each one, and *the value that comes back from whoever did the
            // thing is not the measurement of the thing*. If the components in the assembly
            // are not as many as were inserted, the case would run on an
            // assembly different from the declared one — and all its measurements
            // would be believable and wrong.
            try { doc.EditRebuild3(); } catch { }
            int howMany = HowManyComponents(ass);
            if (howMany != paths.Count)
            {
                BenchError(itemName, "I inserted " + paths.Count + " components and " +
                                    "the assembly has " + howMany);
                return null;
            }

            // ── 6b. envelopes and hidden components, when the case asks ──
            //
            // After the count and before «configurations»: each is set in the
            // configuration active now, and read back (Z-C21.11). A failure here
            // closes and deletes what the preparation made (see Abandon).
            if (!MakeEnvelopes(itemName, doc, ass, envelopeNames, pathByComponentName) ||
                !HideComponents(itemName, ass, hiddenNames, pathByComponentName))
            {
                ThrowAwayPreparation(outside);
                return null;
            }

            // ── 7. the configurations of the assembly, when the case asks ──
            var configurations = Items(assemblyDoc, "configurations");
            if (configurations != null &&
                !PrepareConfigurations(itemName, doc, ass, configurations, pathByComponentName))
                return null;

            // ── 8. saved, closed and reopened lightweight, when the case asks ──
            //
            // Last, so that the file holds everything above (envelopes, hidden
            // components, configurations: the last one made stays the active
            // one, and the assembly reopens on it). From here the case runs on
            // the REOPENED document: the one built above is closed.
            if (reopenLightweight)
            {
                IModelDoc2 reopened = ReopenLightweight(itemName, doc, paths.Count, outside);
                if (reopened == null) return null;
                outside.Document = reopened;
            }

            return outside;
        }

        /// <summary>
        /// A TRUE/FALSE KEY OF A CASE OBJECT (Z-C21.11): absent is false, a JSON
        /// Boolean is itself, anything else is a bench error (never read as
        /// false: the case would run on another assembly and look right).
        /// </summary>
        private static bool FlagOf(string itemName, Dictionary<string, object> map, string key, out bool value)
        {
            value = false;
            object raw;
            if (map == null || !map.TryGetValue(key, out raw) || raw == null) return true;
            if (raw is bool flag) { value = flag; return true; }
            BenchError(itemName, "«" + key + "» is «" + Convert.ToString(raw, CultureInfo.InvariantCulture) +
                                "», not true or false");
            return false;
        }

        /// <summary>
        /// THE COMPONENTS A CASE ASKS AS ENVELOPES (Z-C21.11, finding N27):
        /// «envelope»: true on a component (a part; see the checks at step 0).
        ///
        /// As the Z-C21.3 probe of envelopes and BOM children (28/9/2026, T_env,
        /// the component «F»): the component is selected with IComponent2.Select4,
        /// then IAssemblyDoc.CompConfigProperties6(swComponentFullyResolved,
        /// swComponentRigidSolving, Visibility true, UseNamedConfiguration true,
        /// the configuration it references, ExcludeFromBOM false, IsEnvelope
        /// true, swAlwaysInclude) acts on the selection; the probe read
        /// IsEnvelope true after it, and CreateMassProperty of the assembly left
        /// it out (16000 mm3 of 26000). Here the component is found again after a
        /// rebuild, by its file, and IsEnvelope is READ BACK: an envelope that
        /// did not take is a bench error, never a case run on a normal part.
        /// </summary>
        private static bool MakeEnvelopes(string itemName, IModelDoc2 doc, IAssemblyDoc ass, List<string> names,
                                          Dictionary<string, string> pathByComponentName)
        {
            foreach (var componentName in names)
            {
                IComponent2 component = ComponentOfCase(itemName, ass, componentName, pathByComponentName, "«envelope»");
                if (component == null) return false;
                string configuration = null;
                try { configuration = component.ReferencedConfiguration; } catch { }
                if (string.IsNullOrEmpty(configuration))
                {
                    BenchError(itemName, "«envelope»: I could not read which configuration «" + componentName +
                                        "» references (ReferencedConfiguration), and CompConfigProperties6 is given it");
                    return false;
                }
                bool selected = false;
                try
                {
                    doc.ClearSelection2(true);
                    selected = component.Select4(false, null, false);
                }
                catch (Exception ex)
                {
                    BenchError(itemName, "«envelope»: selecting «" + componentName + "»: " + Why(ex));
                    return false;
                }
                if (!selected)
                {
                    BenchError(itemName, "«envelope»: Select4 did not select «" + componentName + "», and " +
                                        "CompConfigProperties6 acts on the selected component");
                    return false;
                }
                bool answer = false;
                try
                {
                    answer = ass.CompConfigProperties6(
                        (int)swComponentSuppressionState_e.swComponentFullyResolved,
                        (int)swComponentSolvingOption_e.swComponentRigidSolving,
                        true, true, configuration, false, true,
                        (int)swASMSLDPRTCompPref_e.swAlwaysInclude);
                }
                catch (Exception ex)
                {
                    try { doc.ClearSelection2(true); } catch { }
                    BenchError(itemName, "CompConfigProperties6 on «" + componentName + "»: " + Why(ex));
                    return false;
                }
                try { doc.ClearSelection2(true); } catch { }
                try { doc.EditRebuild3(); } catch { }
                IComponent2 again = ComponentOfCase(itemName, ass, componentName, pathByComponentName, "«envelope»");
                if (again == null) return false;
                bool isEnvelope = false;
                try { isEnvelope = again.IsEnvelope(); } catch { }
                if (!isEnvelope)
                {
                    BenchError(itemName, "«" + componentName + "» is not an envelope after CompConfigProperties6 " +
                                        "(it answered " + answer + ", and IsEnvelope reads false or cannot be read)");
                    return false;
                }
                Console.WriteLine("       | component «" + componentName + "» made an envelope (IsEnvelope read back)");
            }
            return true;
        }

        /// <summary>
        /// THE COMPONENTS A CASE ASKS HIDDEN (Z-C21.11): «hidden»: true on a
        /// component. As the probe zc2111/probe_alleggeriti (30/9/2026, T_hid):
        /// IComponent2.Visible = swComponentHidden (0); the probe read Visible 0
        /// and IsHidden(false) true, CreateMassProperty of the assembly weighed
        /// it and the STEP did not write it. Here IsHidden(false) is READ BACK
        /// on the component found again by its file (false, so that a
        /// lightweight component is not taken for hidden: IComponent2~IsHidden.html).
        /// </summary>
        private static bool HideComponents(string itemName, IAssemblyDoc ass, List<string> names,
                                           Dictionary<string, string> pathByComponentName)
        {
            foreach (var componentName in names)
            {
                IComponent2 component = ComponentOfCase(itemName, ass, componentName, pathByComponentName, "«hidden»");
                if (component == null) return false;
                try { component.Visible = (int)swComponentVisibilityState_e.swComponentHidden; }
                catch (Exception ex)
                {
                    BenchError(itemName, "«hidden»: IComponent2.Visible on «" + componentName + "»: " + Why(ex));
                    return false;
                }
                IComponent2 again = ComponentOfCase(itemName, ass, componentName, pathByComponentName, "«hidden»");
                if (again == null) return false;
                bool hiddenNow = false;
                try { hiddenNow = again.IsHidden(false); } catch { }
                if (!hiddenNow)
                {
                    BenchError(itemName, "«" + componentName + "» is not hidden after IComponent2.Visible = " +
                                        "swComponentHidden (IsHidden(false) reads false or cannot be read)");
                    return false;
                }
                Console.WriteLine("       | component «" + componentName + "» hidden (IsHidden(false) read back)");
            }
            return true;
        }

        /// <summary>
        /// THE ASSEMBLY REOPENED LIGHTWEIGHT (Z-C21.11, finding N5):
        /// «reopen_lightweight»: true next to the list of components.
        ///
        /// Measured by the probe zc2111/probe_alleggeriti (30/9/2026): an
        /// assembly reopened with OpenDoc6 options Silent | OverrideDefaultLoadLightweight
        /// | LoadLightweight (1 | 64 | 128 = 193), after EVERY document of it was
        /// closed, came back with all its components lightweight
        /// (GetLightWeightComponentCount 4 of 4); a part still loaded as a
        /// document came back resolved. So:
        ///   1. the assembly is saved as «&lt;case&gt;.sldasm» under Bench\work
        ///      and registered like the parts (deleted at the end, closed by
        ///      title);
        ///   2. it is closed by reference, then every loaded document whose FILE
        ///      is one of the case's is closed, twice (a part stays loaded while a
        ///      sub-assembly that holds it is open, as in ThrowAwayPreparation);
        ///   3. nothing of the case may still be loaded, looked at by path: a
        ///      leftover would come back resolved and the case would measure a
        ///      resolved assembly believing it lightweight;
        ///   4. it is reopened; a warning «already open» is a bench error for
        ///      the same reason, and so are a count of lightweight components
        ///      that is not above zero and a count of top-level components that
        ///      is not the one built.
        /// The documents of the components stay closed: a step that needs one
        /// open (insert_component) cannot follow, which step 0 refuses. Any
        /// failure closes and deletes what the preparation made.
        /// </summary>
        private static IModelDoc2 ReopenLightweight(string itemName, IModelDoc2 doc, int topLevelCount, Preparation outside)
        {
            string path = Path.Combine(Expectations.Work, FileName(itemName) + ".sldasm");
            int e = 0, w = 0;
            try { SaveTo(doc, path, out e, out w); }
            catch (Exception ex)
            {
                BenchError(itemName, "«reopen_lightweight»: saving the assembly: " + Why(ex));
                return Abandon(outside);
            }
            if (!File.Exists(path))
            {
                BenchError(itemName, "«reopen_lightweight»: saved without errors (e=" + e + ", w=" + w + ") but «" +
                                    path + "» is not there");
                return Abandon(outside);
            }
            Register(outside, path);

            Close(doc);
            for (int round = 0; round < 2; round++)
                foreach (IModelDoc2 loaded in LoadedDocumentsOf(outside.FilesToDelete) ?? new List<IModelDoc2>())
                    Close(loaded);
            // As the probe, before looking: the COM wrappers of the closed
            // documents are let go, so that nothing of this process keeps one.
            // The finalizer gate open first (Shared.FinalizerGate): closed, the wait below
            // would never end.
            Shared.FinalizerGate.Open();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            List<IModelDoc2> still = LoadedDocumentsOf(outside.FilesToDelete);
            if (still == null || still.Count > 0)
            {
                BenchError(itemName, "«reopen_lightweight»: " + (still == null
                    ? "I could not read which documents are loaded (GetFirstDocument, GetNext, GetPathName)"
                    : "still loaded after closing: " + string.Join(", ", FileNamesOf(still))) +
                    ". Reopened now, those would come back resolved.");
                return Abandon(outside);
            }

            const int options = (int)swOpenDocOptions_e.swOpenDocOptions_Silent |
                                (int)swOpenDocOptions_e.swOpenDocOptions_OverrideDefaultLoadLightweight |
                                (int)swOpenDocOptions_e.swOpenDocOptions_LoadLightweight;   // 193, as the probe
            int errors = 0, warnings = 0;
            IModelDoc2 reopened;
            try
            {
                reopened = _sw.OpenDoc6(path, (int)swDocumentTypes_e.swDocASSEMBLY, options, "",
                                        ref errors, ref warnings) as IModelDoc2;
            }
            catch (Exception ex)
            {
                BenchError(itemName, "«reopen_lightweight»: OpenDoc6: " + Why(ex));
                return Abandon(outside);
            }
            if (reopened == null)
            {
                BenchError(itemName, "«reopen_lightweight»: OpenDoc6 answered null (e=" + errors + ", w=" + warnings + ")");
                return Abandon(outside);
            }
            // From here the cleanup closes the reopened document.
            outside.Document = reopened;
            if ((warnings & (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen) != 0)
            {
                BenchError(itemName, "«reopen_lightweight»: OpenDoc6 warned that the assembly was already open " +
                                    "(w=" + warnings + "): it was not reopened, so it is not lightweight");
                return Abandon(outside);
            }
            IAssemblyDoc reopenedAssembly = reopened as IAssemblyDoc;
            if (reopenedAssembly == null)
            {
                BenchError(itemName, "«reopen_lightweight»: the reopened document is not an IAssemblyDoc");
                return Abandon(outside);
            }
            int lightweight = -1;
            try { lightweight = reopenedAssembly.GetLightWeightComponentCount(); } catch { }
            if (lightweight <= 0)
            {
                BenchError(itemName, "«reopen_lightweight»: after OpenDoc6 (options " + options + ") " +
                                    "GetLightWeightComponentCount " + (lightweight < 0 ? "could not be read" : "answered 0") +
                                    ": the case would run on a resolved assembly");
                return Abandon(outside);
            }
            int count = HowManyComponents(reopenedAssembly);
            if (count != topLevelCount)
            {
                BenchError(itemName, "«reopen_lightweight»: the assembly was built with " + topLevelCount +
                                    " top-level components and reopens with " + count);
                return Abandon(outside);
            }
            Console.WriteLine("       | assembly saved as «" + Path.GetFileName(path) + "», closed with its components " +
                              "and reopened lightweight: " + lightweight + " lightweight component(s)");
            return reopened;
        }

        /// <summary>
        /// The loaded documents (ISldWorks.GetFirstDocument, then IModelDoc2.GetNext)
        /// whose file is one of <paramref name="paths"/>, compared as full paths.
        /// Null when the list cannot be read, or a document does not say its path:
        /// «I could not look» is not «nothing is loaded».
        /// </summary>
        private static List<IModelDoc2> LoadedDocumentsOf(List<string> paths)
        {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths)
            {
                try { wanted.Add(Path.GetFullPath(p)); }
                catch (ArgumentException) { }
            }
            var found = new List<IModelDoc2>();
            IModelDoc2 d;
            try { d = _sw.GetFirstDocument() as IModelDoc2; }
            catch (Exception) { return null; }
            for (int guard = 0; d != null; guard++)
            {
                if (guard > 10000) return null;
                string own;
                try { own = d.GetPathName(); }
                catch (Exception) { return null; }
                // A document never saved has no path, and none of the case's is
                // one: every file of the case is on disk before it is opened.
                if (!string.IsNullOrWhiteSpace(own))
                {
                    try { if (wanted.Contains(Path.GetFullPath(own))) found.Add(d); }
                    catch (ArgumentException) { return null; }
                }
                IModelDoc2 next;
                try { next = d.GetNext() as IModelDoc2; }
                catch (Exception) { return null; }
                d = next;
            }
            return found;
        }

        /// <summary>The file names of some documents, for a message.</summary>
        private static List<string> FileNamesOf(List<IModelDoc2> docs)
        {
            var names = new List<string>();
            foreach (IModelDoc2 one in docs)
            {
                string own = "?";
                try { own = Path.GetFileName(one.GetPathName()); } catch { }
                names.Add(own);
            }
            return names;
        }

        /// <summary>
        /// A preparation that stopped half-way (Z-C21.11): what it made is closed
        /// and deleted here, since the caller gets null and has nothing to hand
        /// to ThrowAwayPreparation. A document left open would be found by the
        /// next case, and a file left in Bench\work would be found by the next
        /// round.
        /// </summary>
        private static IModelDoc2 Abandon(Preparation p)
        {
            ThrowAwayPreparation(p);
            return null;
        }

        /// <summary>
        /// THE CONFIGURATIONS A CASE ASKS OF ITS ASSEMBLY (Z-C21.4, findings
        /// L16-2 and L16-3): «configurations»: [{ "name": ..., "suppressed":
        /// [component names], "excluded_from_bom": [component names] }], made in
        /// this order after the components are in. No operation of the catalogue
        /// suppresses a component or excludes it from the BOM in one
        /// configuration, and without this a BOM read in the wrong configuration
        /// could not be written as a case.
        ///
        /// IConfigurationManager.AddConfiguration2 ACTIVATES the new
        /// configuration (measured on parts by create_configuration, read back
        /// here on the assembly), so each one is set while it is the active one:
        /// IComponent2.SetSuppression2 acts on the active configuration, and
        /// SetExcludeFromBOM2 is given swThisConfiguration. Every setting is READ
        /// BACK (IsSuppressed, GetExcludeFromBOM2) before the case runs. The last
        /// configuration made stays active; a step that needs another one active
        /// asks for it with «activate_before».
        /// </summary>
        private static bool PrepareConfigurations(
            string itemName, IModelDoc2 doc, IAssemblyDoc ass, List<object> configurations,
            Dictionary<string, string> pathByComponentName)
        {
            var manager = doc.ConfigurationManager;
            if (manager == null)
            {
                BenchError(itemName, "«configurations»: the assembly gives no ConfigurationManager");
                return false;
            }
            for (int i = 0; i < configurations.Count; i++)
            {
                var one = configurations[i] as Dictionary<string, object>;
                string name = one == null ? null : Text(one, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    BenchError(itemName, "«configurations» " + (i + 1) + " has no «name»");
                    return false;
                }
                IConfiguration made = null;
                try { made = manager.AddConfiguration2(name, "", "", 0, "", "", true) as IConfiguration; }
                catch (Exception ex)
                {
                    BenchError(itemName, "AddConfiguration2(«" + name + "»): " + Why(ex));
                    return false;
                }
                string active = null;
                try
                {
                    IConfiguration activeNow = manager.ActiveConfiguration;
                    active = activeNow == null ? null : activeNow.Name;
                }
                catch { }
                if (made == null || !string.Equals(active, name, StringComparison.Ordinal))
                {
                    BenchError(itemName, "AddConfiguration2(«" + name + "») answered " +
                                        (made == null ? "null" : "«" + made.Name + "»") + " and the active " +
                                        "configuration is «" + (active ?? "?") + "»: the settings of this " +
                                        "configuration would land on another one");
                    return false;
                }

                foreach (var componentName in TextsOf(one, "suppressed"))
                {
                    var component = ComponentOfCase(itemName, ass, componentName, pathByComponentName, "«configurations»");
                    if (component == null) return false;
                    int answer = -1;
                    try { answer = component.SetSuppression2((int)swComponentSuppressionState_e.swComponentSuppressed); }
                    catch (Exception ex)
                    {
                        BenchError(itemName, "SetSuppression2 on «" + componentName + "»: " + Why(ex));
                        return false;
                    }
                    bool suppressed = false;
                    try { suppressed = component.IsSuppressed(); } catch { }
                    if (!suppressed)
                    {
                        BenchError(itemName, "«" + componentName + "» is not suppressed in «" + name +
                                            "» after SetSuppression2 (answer " + answer + ")");
                        return false;
                    }
                }

                foreach (var componentName in TextsOf(one, "excluded_from_bom"))
                {
                    var component = ComponentOfCase(itemName, ass, componentName, pathByComponentName, "«configurations»");
                    if (component == null) return false;
                    int answer = -1;
                    object readBack = null;
                    try
                    {
                        answer = component.SetExcludeFromBOM2(
                            true, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                        readBack = component.GetExcludeFromBOM2(
                            (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    }
                    catch (Exception ex)
                    {
                        BenchError(itemName, "SetExcludeFromBOM2 on «" + componentName + "»: " + Why(ex));
                        return false;
                    }
                    if (!OnlyTrue(readBack))
                    {
                        BenchError(itemName, "«" + componentName + "» is not excluded from the BOM of «" + name +
                                            "» after SetExcludeFromBOM2 (answer " + answer + ")");
                        return false;
                    }
                }
                Console.WriteLine("       | assembly configuration «" + name + "» made" +
                                  (TextsOf(one, "suppressed").Count + TextsOf(one, "excluded_from_bom").Count > 0
                                      ? ": suppressed [" + string.Join(", ", TextsOf(one, "suppressed")) +
                                        "], out of the BOM [" + string.Join(", ", TextsOf(one, "excluded_from_bom")) + "]"
                                      : ""));
            }
            try { doc.EditRebuild3(); } catch { }
            return true;
        }

        /// <summary>The texts of a list field of a case object; empty when the field is absent.</summary>
        private static List<string> TextsOf(Dictionary<string, object> one, string field)
        {
            var list = new List<string>();
            var raw = one == null ? null : Items(one, field);
            if (raw == null) return list;
            foreach (var x in raw) if (x != null) list.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            return list;
        }

        /// <summary>Whether a GetExcludeFromBOM2 answer is one Boolean, and true.</summary>
        private static bool OnlyTrue(object answer)
        {
            var flags = answer as bool[];
            if (flags != null) return flags.Length == 1 && flags[0];
            var boxed = answer as object[];
            return boxed != null && boxed.Length == 1 && boxed[0] is bool b && b;
        }

        /// <summary>
        /// The top-level component of the assembly made from the case component
        /// called <paramref name="componentName"/>, found by its FILE, never by
        /// its position; null (after BenchError) when there is none.
        /// <paramref name="askedBy"/> is the key of the case that names it
        /// («configurations», «envelope», «hidden»), for the message.
        /// </summary>
        private static IComponent2 ComponentOfCase(
            string itemName, IAssemblyDoc ass, string componentName, Dictionary<string, string> pathByComponentName,
            string askedBy)
        {
            string path;
            if (!pathByComponentName.TryGetValue(componentName, out path))
            {
                BenchError(itemName, askedBy + " names «" + componentName + "», which is not a component of the case");
                return null;
            }
            object[] all = null;
            try { all = ass.GetComponents(true) as object[]; } catch { }
            foreach (var o in all ?? new object[0])
            {
                var c = o as IComponent2;
                string own = null;
                try { own = c?.GetPathName(); } catch { }
                if (!string.IsNullOrWhiteSpace(own) &&
                    string.Equals(Path.GetFullPath(own), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            BenchError(itemName, askedBy + " names «" + componentName + "», and no component of the assembly " +
                                "uses its file (it has «to_insert» false?)");
            return null;
        }

        /// <summary>
        /// Closes and deletes what the preparation left lying around.
        ///
        /// THE FILES ARE DELETED TOO, not only the documents. `CloseLeftovers`
        /// closes windows; these are `.sldprt` on disk, and staying there
        /// would mean that the next run finds a file already made. **A case
        /// that succeeds because a file from the run before was left over is not a
        /// case, it is a memento** — and the defect would be noticed only by whoever
        /// empties the folder by hand.
        /// </summary>
        internal static void ThrowAwayPreparation(Preparation p)
        {
            if (p == null) return;

            // ── TWO PASSES, AND THE ORDER IS NOT PRETENDED TO BE KNOWN (entry GT0) ──
            //
            // Before GT0 the list held only the components of an assembly,
            // and the assembly was `Document`: the assembly was closed and then the parts,
            // that is from the top. Now the list can also hold a DRAWING,
            // and there the above and the below are reversed — the drawing is above the
            // part, which is `Document`.
            //
            // **An order that is good for both is not chosen**, because there
            // is none: one tries, and tries again. `CloseDoc` on a document that has
            // already closed does nothing, so the second pass costs one
            // empty attempt and picks up what the first left behind.
            // *When one does not know in what order two things must be taken apart, they are
            // taken apart twice — it is less code than deducing the order, and it
            // does not go wrong when the deduction was wrong.*
            for (int benchRun = 0; benchRun < 2; benchRun++)
            {
                Close(p.Document);
                // By reference FIRST: it is the drawing, and as long as it is open
                // it keeps the part open. See `DocumentsToClose`.
                foreach (var d in p.DocumentsToClose) Close(d);
                foreach (var t in p.TitlesToClose)
                {
                    try { _sw.CloseDoc(t); } catch { }
                }
            }

            // AND ONE LOOKS WHETHER THEY REALLY WENT AWAY (entry K12-bis).
            //
            // `CloseDoc` returns nothing: the defect of K12-bis lived
            // seven days precisely because closing a document that does not
            // exist under that name **is not an error**. Here a red cannot
            // be made — the teardown also runs inside a failed case,
            // and a second red would cover the first — but it can be
            // SAID, and a line that appears only when something did not
            // go well is the minimum so as not to rediscover it in a week.
            int remain = 0;
            try
            {
                var d = (IModelDoc2)_sw.GetFirstDocument();
                while (d != null) { remain++; d = (IModelDoc2)d.GetNext(); }
            }
            catch { remain = -1; }
            if (remain > 0)
                Console.WriteLine("       | teardown: " + remain +
                                  " documents remain open");

            ThrowAwayFiles(p);
        }

        private static void ThrowAwayFiles(Preparation p)
        {
            foreach (var f in p.FilesToDelete)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
            p.FilesToDelete.Clear();
        }

        // =================================================================

        /// <summary>
        /// Builds a part by executing the component's steps and saves it.
        ///
        /// The steps are the SAME operations as the cases, executed with the same
        /// `Run`: a component has no language of its own, otherwise it would be
        /// a second implementation to keep aligned by hand.
        /// </summary>
        private static bool BuildAndSave(
            string label, Dictionary<string, object> component, string filePath)
        {
            var steps = Items(component, "steps");
            if (steps == null || steps.Count == 0)
            {
                BenchError(label, "«steps» is missing: a component without " +
                                         "features would be an empty document");
                return false;
            }

            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                var docState = ReadState(doc);
                for (int i = 0; i < steps.Count; i++)
                {
                    var planStep = steps[i] as Dictionary<string, object>;
                    if (planStep == null)
                    {
                        BenchError(label, "step " + (i + 1) + " is not an object");
                        return false;
                    }
                    string operation = Text(planStep, "operation");
                    if (operation == null)
                    {
                        BenchError(label, "step " + (i + 1) + " is missing «operation»");
                        return false;
                    }
                    object rawItems;
                    planStep.TryGetValue("arguments", out rawItems);
                    var arguments = Resolve(rawItems, docState) as Dictionary<string, object>
                                    ?? new Dictionary<string, object>();

                    string error;
                    Run(doc, operation, arguments, out error, docState);
                    if (!string.IsNullOrEmpty(error))
                    {
                        // A COMPONENT THAT CANNOT BE BUILT IS NOT A RED
                        // CHECK. The case wanted to measure an assembly, and the assembly
                        // never existed: calling it «failed» would make one look
                        // for the defect in the assembly operation, which was not
                        // even called.
                        BenchError(label, "step " + (i + 1) + " («" + operation +
                                                 "») was refused: " + Expectations.Short(error));
                        return false;
                    }
                    docState = ReadState(doc);
                }

                int e, w;
                SaveTo(doc, filePath, out e, out w);
                // THE FILE IS LOOKED AT, NOT THE RETURN CODE. `SaveAs` writes
                // two integers that tell what SOLIDWORKS thinks it has
                // done; the only proof that the file is there is that it is there.
                if (!File.Exists(filePath))
                {
                    BenchError(label, "saved without errors (e=" + e + ", w=" + w +
                                             ") but «" + filePath + "» is not there");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                BenchError(label, Why(ex));
                return false;
            }
            finally { Close(doc); }
        }

        /// <summary>
        /// The one SaveAs of this file (IModelDocExtension.SaveAs, frozen for
        /// Z-C21.7 as L13-4/L13-8): a part component and a sub-assembly are
        /// saved the same way, and the caller looks at the FILE, not at the
        /// two numbers.
        /// </summary>
        private static void SaveTo(IModelDoc2 doc, string path, out int errors, out int warnings)
        {
            int e = 0, w = 0;
            doc.Extension.SaveAs(path, 0, 1, null, ref e, ref w);
            errors = e;
            warnings = w;
        }

        /// <summary>
        /// A file the preparation made: deleted at the end, and its document
        /// closed by title. The components and the parts of a sub-assembly
        /// (Z-C21.5) are registered here, in one place: the two lists are the
        /// whole cleanup, and a file missing from them is the file the next
        /// round finds already there.
        /// </summary>
        private static void Register(Preparation p, string path)
        {
            p.FilesToDelete.Add(path);
            p.TitlesToClose.Add(Path.GetFileName(path));
        }

        /// <summary>
        /// A SUB-ASSEMBLY COMPONENT (Z-C21.5, findings L11-3 and L11-5): a
        /// component of the case with a list of components of its own instead
        /// of steps. Each child is a part built and saved like any component, as
        /// «&lt;stem&gt;_&lt;child&gt;.sldprt», inserted into a new assembly at its
        /// «position_mm», and that assembly is saved as
        /// <paramref name="path"/> («&lt;stem&gt;.sldasm»), which the caller then
        /// registers and inserts like a part. Without it no case could hold a
        /// component inside a sub-assembly, and fix_component and
        /// replace_component, which work on the top level only, could not
        /// be seen refusing one.
        ///
        /// The children are registered here, BEFORE the caller registers the
        /// sub-assembly: the files are reopened in that order, parts first. The
        /// sub-assembly is closed by its title at the end like a part: it is
        /// registered after it is saved, so the title no longer changes. If
        /// this fails before the save, the new assembly is closed here, since
        /// nothing else knows it. One level only: a sub-assembly inside a
        /// sub-assembly is refused, no case needs it yet.
        /// Two children with the same name are two instances of ONE part
        /// (Z-C21.11): it is built from the first, and the second brings only
        /// its «position_mm».
        /// </summary>
        private static bool BuildSubassembly(
            string label, string stem, List<object> children, string path, Preparation outside)
        {
            if (children.Count == 0)
            {
                BenchError(label, "the list of components is empty: a sub-assembly with nothing inside proves nothing");
                return false;
            }
            var childPaths = new List<string>();
            var childPositions = new List<double[]>();
            // The child names already built here (Z-C21.11, finding M23).
            var builtHere = new HashSet<string>(StringComparer.Ordinal);
            for (int k = 0; k < children.Count; k++)
            {
                var child = children[k] as Dictionary<string, object>;
                if (child == null)
                {
                    BenchError(label, "component " + (k + 1) + " is not an object");
                    return false;
                }
                if (Items(child, "components") != null)
                {
                    BenchError(label, "a sub-assembly inside a sub-assembly is not supported by the bench");
                    return false;
                }
                // Z-C21.11: «envelope» and «hidden» act on top-level components
                // only (they are found by file among GetComponents(true)); inside
                // a sub-assembly they would be read by nobody, and the case
                // would run on another assembly.
                if (child.ContainsKey("envelope") || child.ContainsKey("hidden"))
                {
                    BenchError(label, "«envelope» and «hidden» act on top-level components only, and component " +
                                         (k + 1) + " is inside a sub-assembly");
                    return false;
                }
                string childName = Text(child, "name") ?? ("comp" + (k + 1));
                string childPath = Path.Combine(Expectations.Work, stem + "_" + FileName(childName) + ".sldprt");
                // A NAME ALREADY BUILT HERE IS ONE MORE INSTANCE OF THE SAME PART
                // (Z-C21.11, finding M23): the indented BOM of «A + 3 x S(2 B) + B»
                // needs two instances of ONE file inside S, and the file name comes
                // from the child name. The part is built and registered once, from
                // the first child with that name; a later one brings only its
                // «position_mm», and steps of its own are refused rather than
                // ignored.
                if (builtHere.Contains(childName))
                {
                    if (Items(child, "steps") != null)
                    {
                        BenchError(label, "component «" + childName + "» repeats a name already built in this " +
                                             "sub-assembly and brings steps of its own: a repeated name is one more " +
                                             "instance of the same part, built once from the first");
                        return false;
                    }
                }
                else
                {
                    if (!BuildAndSave(label + " · component «" + childName + "»", child, childPath))
                        return false;
                    Register(outside, childPath);
                    builtHere.Add(childName);
                }
                childPaths.Add(childPath);
                childPositions.Add(PositionMm(child));
            }

            IModelDoc2 sub;
            try { sub = NewAssembly(); }
            catch (Exception ex)
            {
                BenchError(label, "the new sub-assembly: " + Why(ex));
                return false;
            }
            string failure = FillAndSave(sub, childPaths, childPositions, path, label);
            if (failure == null) return true;
            BenchError(label, failure);
            Close(sub);
            return false;
        }

        /// <summary>
        /// The body of <see cref="BuildSubassembly"/> once the new assembly
        /// exists: the parts opened and inserted, counted, the assembly saved
        /// and its file looked at. Null when it all went, otherwise what did
        /// not (an error of the bench, not a red test).
        /// </summary>
        private static string FillAndSave(
            IModelDoc2 sub, List<string> childPaths, List<double[]> childPositions, string path, string label)
        {
            var subAssembly = sub as IAssemblyDoc;
            if (subAssembly == null) return "the new document is not an IAssemblyDoc";
            // AddComponent5 wants the document open, as in the main assembly.
            foreach (var p in childPaths)
                if (!Open(p, label)) return "a part of the sub-assembly did not open again (the line above says which)";
            for (int k = 0; k < childPaths.Count; k++)
            {
                var at = childPositions[k];
                IComponent2 inserted = null;
                try
                {
                    inserted = subAssembly.AddComponent5(
                        childPaths[k], 0, "", false, "",
                        at[0] / 1000.0, at[1] / 1000.0, at[2] / 1000.0) as IComponent2;
                }
                catch (Exception ex)
                {
                    return "AddComponent5 on «" + Path.GetFileName(childPaths[k]) + "»: " + Why(ex);
                }
                if (inserted == null)
                    return "AddComponent5 answered null on «" + Path.GetFileName(childPaths[k]) +
                           "»: the component did not go in";
            }
            try { sub.EditRebuild3(); } catch { }
            int count = HowManyComponents(subAssembly);
            if (count != childPaths.Count)
                return "inserted " + childPaths.Count + " components and the sub-assembly has " + count;

            int e, w;
            SaveTo(sub, path, out e, out w);
            if (!File.Exists(path))
                return "saved without errors (e=" + e + ", w=" + w + ") but «" + path + "» is not there";
            return null;
        }

        private static IModelDoc2 NewAssembly()
        {
            // Entry K19: the local copy applies to the assembly too.
            string tpl = Program.LocalModel(_sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly));
            // The sentinel (entry K17) applies here too: a test assembly
            // begins from components that are new parts, and if they are no longer
            // born it is not the case that is wrong.
            if (Program.Sentinel.HasGone)
                throw new Shared.PreparationFailed(Program.Sentinel.Report());

            var born = _sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
            if (born != null) Program.Sentinel.Succeeded();
            else if (Program.Sentinel.Failed())
                throw new Shared.PreparationFailed(Program.Sentinel.Report());

            return OrSayIt(born,
                               "the new assembly to test on");
        }

        private static bool Open(string filePath, string itemName)
        {
            try
            {
                int e = 0, w = 0;
                // A sub-assembly (Z-C21.5) opens as an assembly, a component as a part.
                int type = filePath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase)
                    ? (int)swDocumentTypes_e.swDocASSEMBLY
                    : (int)swDocumentTypes_e.swDocPART;
                var d = _sw.OpenDoc6(
                    filePath, type,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref e, ref w);
                if (d == null)
                {
                    BenchError(itemName, "cannot reopen «" +
                                        Path.GetFileName(filePath) + "» (e=" + e + ", w=" + w +
                                        "): AddComponent5 needs a document that is already open");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                BenchError(itemName, "reopening «" + Path.GetFileName(filePath) + "»: " + Why(ex));
                return false;
            }
        }

        /// <summary>How many components there really are in the assembly.</summary>
        private static int HowManyComponents(IAssemblyDoc ass)
        {
            try
            {
                // `true` = first level only. A subassembly counts one, which
                // is what the case inserted.
                var v = ass.GetComponents(true) as object[];
                return v == null ? 0 : v.Length;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Is the component to be inserted by the preparation? Absent = yes.
        ///
        /// The default is `true` and not `false` on purpose: the normal case
        /// is «I want this assembly, make it for me», and asking `to_insert`
        /// of every component would be noise. `false` is written only when one
        /// wants to test the insertion, which is the rare case.
        /// </summary>
        private static bool ToInsert(Dictionary<string, object> component)
        {
            object v;
            if (component == null ||
                !component.TryGetValue("to_insert", out v) || v == null) return true;
            return Convert.ToString(v, CultureInfo.InvariantCulture)
                          .Equals("True", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Where the component goes, in mm. Absent = at the origin.
        ///
        /// **IT IS WHERE THE CENTER OF THE BOUNDING BOX GOES, not where the origin goes.**
        /// `AddComponent5` positions the center: a part with the origin on a
        /// face — that is anything extruded in one direction only — finds
        /// its origin moved by half the bounding box. Measured on 17/9/2026,
        /// and predicted before measuring it: thickness 4 asked at z=30 → origin at
        /// (0, -2, 30); thickness 10 asked at 0 → (0, -5, 0).
        ///
        /// The field is called `position_mm` and not `position`: it is the same
        /// convention as the catalog, where the unit is in the name because a
        /// number without a unit is a number that somebody will interpret in
        /// meters.
        /// </summary>
        private static double[] PositionMm(Dictionary<string, object> component)
        {
            var v = new double[3];
            object raw;
            if (component == null || !component.TryGetValue("position_mm", out raw) ||
                raw == null)
                return v;
            var itemList = raw as System.Collections.IEnumerable;
            if (itemList == null) return v;
            int i = 0;
            foreach (var x in itemList)
            {
                if (i >= 3) break;
                try { v[i++] = Convert.ToDouble(x, CultureInfo.InvariantCulture); }
                catch { i++; }
            }
            return v;
        }

        /// <summary>
        /// A case name made fit to be in a file name.
        ///
        /// The name is not used as it is: the cases are called with words
        /// separated by underscore and that is fine, but one strange character in a
        /// future case is enough for `SaveAs` to fail with a message that talks about
        /// permissions instead of the name.
        /// </summary>
        private static string FileName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "x";
            var b = new System.Text.StringBuilder();
            foreach (var c in s)
                b.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return b.ToString();
        }
    }
}
