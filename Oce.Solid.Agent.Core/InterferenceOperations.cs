using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// DO THE PARTS OF THE ASSEMBLY TOUCH? (entry GA10)
    ///
    /// ── WHY THIS ENTRY IS HIGH ──────────────────────────────────────────
    ///
    /// The first part this bridge was written for is a watertight box in two
    /// halves, to be mounted outdoors. **«Does the lid touch the
    /// base?»** and **«does the screw pass inside the wall?»** are the questions that
    /// decide whether the part can be made.
    ///
    /// Until here the bridge did not know how to answer: the state of the assembly says
    /// how many components there are, whether they are fixed, whether they
    /// are mated — **not whether they occupy the same space.** Two
    /// interpenetrating parts open, rebuild, export, print: **no error
    /// anywhere**, and in the workshop they do not fit. It is the defect that
    /// gives no error in the most expensive form of all, because it is discovered when the part is made.
    ///
    /// ── THE MEASURE IS THE VOLUME, NOT THE COUNT ────────────────────────
    ///
    /// `GetInterferenceCount()` says how many there are. It is not enough: **two interferences
    /// can be two tenths of a millimetre of tolerance or half a
    /// lid inside the base**, and the number is the same.
    ///
    /// `IInterference.GetInterferenceBody()` returns the **solid body
    /// of the intersection**: from there `GetMassProperties` gives the volume. It is what
    /// distinguishes a problem from a detail.
    ///
    /// *A count without a magnitude does not distinguish a problem from a
    /// detail.*
    ///
    /// ── `ToolsCheckInterference2` IS NOT CALLED ────────────────────────
    ///
    /// It returns `Void`: it is the button. Same rule measured on GA8 — **an API
    /// answers, a button does not**. The manager (`InterferenceDetectionManager`)
    /// is the API.
    ///
    /// ── DO FACES THAT TOUCH COUNT AS AN INTERFERENCE? IT DEPENDS ───────
    ///
    /// And there is no right answer, which is why it is an argument:
    ///
    ///   - a lid **resting** on the base shares a face with it.
    ///     If coincidence counts as interference, **every well-mounted
    ///     assembly turns out broken**;
    ///   - a shaft in a hole of the **same diameter** shares the cylindrical
    ///     surface. There the coincidence is exactly the problem:
    ///     that part does not go in.
    ///
    /// By default `false`, and which of the two was used is written in the
    /// answer. *A setting that changes the verdict and does not appear in the verdict
    /// is a verdict that cannot be reread.*
    /// </summary>
    public partial class SwOperations
    {
        private Outcome InterferenceCheck(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed(
                    "This is not an assembly, it is a document of type «" +
                    DocumentTypeName(TypeOf(model)) + "». Interferences are " +
                    "looked for between the components of an assembly: in a single part " +
                    "there is nothing that can penetrate something else.");

            object[] rawItems = null;
            try { rawItems = assemblyDoc.GetComponents(false) as object[]; } catch { }
            var components = DocumentState.ComponentsInOrder(rawItems);
            if (components.Count < 2)
                return Failed(
                    "The assembly has " + components.Count +
                    (components.Count == 1 ? " component" : " components") +
                    ": at least two are needed for one to touch the other.");

            bool touchCount = Bool(a, "touching_counts", false);
            bool insideTheParts = Bool(a, "inside_parts", false);

            IInterferenceDetectionMgr handler;
            try { handler = assemblyDoc.InterferenceDetectionManager as IInterferenceDetectionMgr; }
            catch (Exception ex)
            {
                return Failed("I cannot open the interference detection: " + ex.Message);
            }
            if (handler == null)
                return Failed(
                    "SOLIDWORKS does not give me the interference detection on this " +
                    "document, and does not say why.");

            var foundOnes = new List<object>();
            int howMany = 0;
            int interferencesRead = 0;
            string listProblem = null;
            double totalVolume = 0;
            bool readVolumes = true;
            var involved = new List<string>();
            // N38: how many of the interferences read are only POSSIBLE, and
            // their volume; null when the flag of one of them could not be read.
            int? possibleCount = 0;
            double possibleVolume = 0;
            // N18: the three settings as SOLIDWORKS reads them back.
            bool? coincidenceRead = null, multibodyRead = null, ignoredShownRead = null;

            try
            {
                // The settings BEFORE counting: they change the verdict, not
                // its presentation.
                //
                // AND THEY ARE READ BACK (finding N18). The three are get/set
                // properties (IInterferenceDetectionMgr~TreatCoincidenceAsInterference.html,
                // ~IncludeMultibodyPartInterferences.html, ~ShowIgnoredInterferences.html:
                // «Gets or sets ...»). Until library 0.144.0 each setter sat in an
                // empty catch and the answer repeated the values asked for: a
                // setter that failed gave a verdict on another question, signed
                // with the question asked. Now the answer gives what SOLIDWORKS
                // reads back, and a value that reads back different stops the
                // count (InterferenceSettingsProblem).
                try { handler.TreatCoincidenceAsInterference = touchCount; } catch { }
                try { handler.IncludeMultibodyPartInterferences = insideTheParts; } catch { }
                try { handler.ShowIgnoredInterferences = false; } catch { }
                try { coincidenceRead = handler.TreatCoincidenceAsInterference; } catch { }
                try { multibodyRead = handler.IncludeMultibodyPartInterferences; } catch { }
                try { ignoredShownRead = handler.ShowIgnoredInterferences; } catch { }
                string settingsProblem = InterferenceSettingsProblem(
                    touchCount, coincidenceRead, insideTheParts, multibodyRead, ignoredShownRead);
                if (settingsProblem != null)
                    return Failed(
                        "SOLIDWORKS did not take the settings of the interference check: " + settingsProblem +
                        ". The count would answer another question than the one asked, so I did not count. " +
                        "Ask again; if it repeats, check with Interference Detection in SOLIDWORKS.");

                try { howMany = handler.GetInterferenceCount(); }
                catch (Exception ex) { return Failed("I cannot count the interferences: " + ex.Message); }

                // THE LIST MUST BE AS LONG AS THE COUNT (Z-C21.10, finding N34).
                //
                // IInterferenceDetectionMgr~GetInterferenceCount.html, Remarks:
                // «Call this method before calling
                // IInterferenceDetectionMgr::IGetInterferences to determine the
                // size of the array for the interferences»; and
                // ~GetInterferences.html, Return Value: «Array of the
                // interferences». The count IS the size of the array. With a null
                // list, a shorter one, or an item that is not an IInterference,
                // `readVolumes` stayed true: the partial sum went into
                // volume_total_mm3 and into the verdict («2 INTERFERENZE, per 0
                // mm3 in tutto», riuscita:true when GetInterferences threw). Now
                // any of the three makes the volume unknown
                // (`InterferenceListProblem`), and `interferences_read` says how
                // many were actually read.
                object[] list = null;
                string whyNoList = null;
                try { list = handler.GetInterferences() as object[]; }
                catch (Exception ex) { whyNoList = "GetInterferences threw: " + ex.Message; }

                int notInterferences = 0;
                if (list != null)
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        var interference = list[i] as IInterference;
                        if (interference == null) { notInterferences++; continue; }
                        interferencesRead++;

                        double volume;
                        bool read = InterferenceVolumeMm3(interference, out volume);
                        if (!read) readVolumes = false; else totalVolume += volume;

                        bool? maybe = null;
                        try { maybe = interference.IsPossibleInterference; } catch { }
                        // N38: counted apart, not rounded up to a certain one.
                        // IInterference~IsPossibleInterference.html: «True if
                        // this is a possible interference, false if not».
                        if (maybe == null) possibleCount = null;
                        else if (maybe == true)
                        {
                            if (possibleCount.HasValue) possibleCount++;
                            if (read) possibleVolume += volume;
                        }

                        var entry = new Dictionary<string, object>
                        {
                            ["n"] = i + 1,
                            ["volume_mm3"] = read ? (object)Math.Round(volume, 3) : null,
                        };
                        // «Possible» means that SOLIDWORKS is not sure:
                        // it is reported as it is instead of rounding it to a yes.
                        if (maybe == true) entry["only_possible"] = true;
                        else if (maybe == null) entry["possible_not_read"] = true;
                        foundOnes.Add(entry);
                    }
                }
                listProblem = InterferenceListProblem(
                    howMany, list == null ? (int?)null : list.Length, notInterferences, whyNoList);
                if (listProblem != null) readVolumes = false;

                try
                {
                    var comp = handler.GetInterferenceComponents() as object[];
                    if (comp != null)
                        foreach (var o in comp)
                        {
                            var c = o as IComponent2;
                            if (c == null) continue;
                            string n = null;
                            try { n = c.Name2; } catch { }
                            if (!string.IsNullOrWhiteSpace(n) && !involved.Contains(n)) involved.Add(n);
                        }
                }
                catch { }
            }
            finally
            {
                // IT IS ALWAYS SWITCHED OFF, AND IN `finally`.
                //
                // The detection is a STATE of the document: leaving it on
                // leaves SOLIDWORKS in interference mode, with the bodies
                // highlighted, for whoever opens that document afterwards. And the
                // `finally` is not pedantry: the exception arrives exactly when
                // the analysis goes badly (same lesson already written for
                // `IThicknessAnalysis.Close()`, entry W2.14).
                try { handler.Done(); } catch { }
            }

            // N18: the components the check looks at are the ones that are not
            // suppressed. Until library 0.144.0 the count of components looked
            // at took every component, suppressed ones included. GetSuppression2
            // answers swComponentSuppressionState_e, as in StatoAssieme.
            // The components are read from the array GetComponents gave, the
            // same ones the list above was built from.
            var states = new List<int?>();
            foreach (var o in rawItems ?? new object[0])
            {
                var component = o as IComponent2;
                if (component == null) continue;
                int? state = null;
                try { state = component.GetSuppression2(); } catch { }
                states.Add(state);
            }
            int suppressedComponents;
            int lookedAt = ComponentsLookedAt(states.ToArray(), out suppressedComponents);

            // N38: certain and possible apart, only when every counted
            // interference was read and each said which it is.
            int? possible = interferencesRead == howMany ? possibleCount : null;
            object certainOut = null, possibleOut = null;
            if (possible is int possibleKnown)
            {
                certainOut = howMany - possibleKnown;
                possibleOut = possibleKnown;
            }

            var measurements = new Dictionary<string, object>
            {
                ["how_many"] = howMany,
                // How many of the counted interferences the array actually gave:
                // equal to «how_many» when the list and the count agree (N34).
                ["interferences_read"] = interferencesRead,
                ["certain_interferences"] = certainOut,
                ["possible_interferences"] = possibleOut,
                ["components_checked"] = NumberOrNull(lookedAt),
                ["suppressed_components"] = NumberOrNull(suppressedComponents),
                // N18: as SOLIDWORKS reads them back after setting them (the
                // check above stops when they differ from the request).
                ["touching_counts"] = coincidenceRead,
                ["inside_parts"] = multibodyRead,
            };
            if (suppressedComponents > 0)
                measurements["suppressed_note"] =
                    suppressedComponents + (suppressedComponents == 1 ? " component is" : " components are") +
                    " suppressed and not in the check: SOLIDWORKS does not look at them. Unsuppress them to " +
                    "have them checked.";
            if (coincidenceRead == null || multibodyRead == null || ignoredShownRead == null)
                measurements["settings_not_read_back"] =
                    "I could not read back every setting of the check (TreatCoincidenceAsInterference, " +
                    "IncludeMultibodyPartInterferences, ShowIgnoredInterferences): those were set as asked " +
                    "but not confirmed, and the answer gives null for them.";
            if (foundOnes.Count > 0) measurements["interferences"] = foundOnes.ToArray();
            if (involved.Count > 0) measurements["components_involved"] = involved.ToArray();
            if (howMany > 0 && readVolumes) measurements["volume_total_mm3"] = Math.Round(totalVolume, 3);
            if (listProblem != null)
                measurements["what_is_not_known"] =
                    "SOLIDWORKS did not give me the whole list of interferences (" + listProblem +
                    "): I read " + interferencesRead + " of " + howMany + ". The count comes from " +
                    "GetInterferenceCount; the total volume would be the sum of a part of the list, " +
                    "so I left it out instead of writing one smaller than the truth.";
            else if (howMany > 0 && !readVolumes)
                measurements["what_is_not_known"] =
                    "Of at least one interference I could not read the volume: the " +
                    "count is right, the sum is not. I did not write it, instead of " +
                    "writing one smaller than the true one.";

            // A ZERO COUNT WITH INTERFERENCES IN THE LIST is not «none» (N34):
            // the two calls disagree, and «Nessuna interferenza» with
            // succeeded:true is the one answer that sends a part that does not
            // fit to the shop.
            if (howMany == 0 && interferencesRead > 0)
                return Failed(
                    "SOLIDWORKS counted 0 interferences and then listed " + interferencesRead +
                    " (" + listProblem + "): I cannot tell you that the parts do not interfere. " +
                    "Ask me again; if it repeats, check with Interference Detection in SOLIDWORKS.");

            // ── THE VERDICT ─────────────────────────────────────────────
            if (howMany == 0)
            {
                measurements["verdict"] =
                    "No interference between the " + (lookedAt >= 0 ? lookedAt : components.Count) + " components" +
                    (touchCount
                        ? ", and the faces that touch were counted as interference."
                        : ". WARNING about what it means: the faces that TOUCH were not " +
                          "counted. A shaft in a hole of the same diameter comes " +
                          "out fine here, and in the workshop it does not go in: if that " +
                          "is what interests you, ask me again with «touching_counts».");
                _log("[agent] " + measurements["verdict"]);
                return SucceededWithoutCreating(measurements);
            }

            measurements["verdict"] = InterferenceVerdict(
                howMany, possible, readVolumes, totalVolume, possibleVolume, involved);
            _log("[agent] " + measurements["verdict"]);
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// The verdict when SOLIDWORKS counted interferences (a count above
        /// zero), with the certain and the POSSIBLE ones apart (finding N38).
        ///
        /// IInterference~IsPossibleInterference.html: «True if this is a
        /// possible interference, false if not». Until library 0.144.0 the flag
        /// was written next to each interference, but the count and the volume
        /// of the verdict took the possible ones as certain: one possible
        /// interference gave the alarm of a certain one. Now the head counts
        /// the certain ones, the possible ones follow in a sentence of their
        /// own, and when every one is only possible the verdict says that
        /// instead. `possible` is null when it is not known (a flag unread, or
        /// a list shorter than the count): the head then counts them all, as
        /// before, and says it could not tell. No interop type in the
        /// signature: Oce.Solid.Agent.ContractTests calls it by reflection.
        /// </summary>
        internal static string InterferenceVerdict(
            int counted, int? possible, bool volumesRead, double totalVolume, double possibleVolume,
            List<string> involved)
        {
            string among = involved != null && involved.Count > 0 ? string.Join(", ", involved) : null;
            int possibleN = possible.GetValueOrDefault();
            int certain = counted - possibleN;
            double certainVolume = possible.HasValue ? totalVolume - possibleVolume : totalVolume;
            string possibleVolumeText = volumesRead
                ? ", " + Math.Round(possibleVolume, 2).ToString(CultureInfo.InvariantCulture) + " mm3"
                : "";

            if (possible.HasValue && certain <= 0)
                return "SOLIDWORKS found " + counted + (counted == 1 ? " POSSIBLE interference" : " POSSIBLE interferences") +
                       possibleVolumeText + (among != null ? " among: " + among : "") + ", and no certain one: " +
                       "it is not sure that these parts occupy the same space (IsPossibleInterference). Look at " +
                       "them with Interference Detection in SOLIDWORKS before you decide.";

            string head =
                "WARNING: " + certain + (certain == 1 ? " INTERFERENCE" : " INTERFERENCES") +
                (volumesRead
                    ? ", for " + Math.Round(certainVolume, 2).ToString(CultureInfo.InvariantCulture) +
                      " mm3 in total"
                    : "") +
                (among != null ? " between: " + among : "") +
                ". The parts occupy the same space: the assembly opens, rebuilds and exports " +
                "all the same — **there is no error anywhere**, and in the workshop they " +
                "do not fit. Look at the volume: a few hundredths of mm3 are two faces " +
                "that graze each other, thousands are one part inside the other.";
            if (!possible.HasValue)
                return head + " Whether some of them are only POSSIBLE interferences I could not read " +
                       "(IsPossibleInterference): they are all counted as certain here.";
            if (possibleN > 0)
                return head + " Besides these, " + possibleN +
                       (possibleN == 1 ? " POSSIBLE interference" : " POSSIBLE interferences") +
                       possibleVolumeText + ": SOLIDWORKS is not sure about " +
                       (possibleN == 1 ? "it" : "them") + " (IsPossibleInterference), look before " +
                       "counting " + (possibleN == 1 ? "it" : "them") + ".";
            return head;
        }

        /// <summary>
        /// How many components the interference check looks at: the ones that
        /// are not suppressed (finding N18). `states` holds the
        /// swComponentSuppressionState_e answer of GetSuppression2 for each
        /// component, null where it could not be read; then the count is -1,
        /// «not known», and so is `suppressed`. No interop type in the
        /// signature: Oce.Solid.Agent.ContractTests calls it by reflection.
        /// </summary>
        internal static int ComponentsLookedAt(int?[] states, out int suppressed)
        {
            suppressed = 0;
            if (states == null) { suppressed = -1; return -1; }
            int looked = 0;
            foreach (var s in states)
            {
                if (!(s is int state)) { suppressed = -1; return -1; }
                if (state == (int)swComponentSuppressionState_e.swComponentSuppressed) suppressed++;
                else looked++;
            }
            return looked;
        }

        /// <summary>
        /// What is wrong with the settings of the check as SOLIDWORKS reads them
        /// back (finding N18), or null. A value that reads back DIFFERENT from
        /// the request is a problem: the count would answer another question.
        /// A value that cannot be read back (null) is not: the setter did not
        /// throw, and the answer says it is unconfirmed. Ignored interferences
        /// are asked NOT shown. No interop type in the signature.
        /// </summary>
        internal static string InterferenceSettingsProblem(
            bool coincidenceAsked, bool? coincidenceRead, bool multibodyAsked, bool? multibodyRead,
            bool? ignoredShownRead)
        {
            var problems = new List<string>();
            if (coincidenceRead is bool coincidence && coincidence != coincidenceAsked)
                problems.Add("TreatCoincidenceAsInterference reads " + coincidence.ToString().ToLowerInvariant() +
                             " where the request asked " + coincidenceAsked.ToString().ToLowerInvariant());
            if (multibodyRead is bool multibody && multibody != multibodyAsked)
                problems.Add("IncludeMultibodyPartInterferences reads " + multibody.ToString().ToLowerInvariant() +
                             " where the request asked " + multibodyAsked.ToString().ToLowerInvariant());
            if (ignoredShownRead == true)
                problems.Add("ShowIgnoredInterferences reads true where it was set to false");
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>
        /// What is wrong with the array GetInterferences returned, measured
        /// against the count (N34); null when it is whole. The array must be as
        /// long as the count (GetInterferenceCount.html: «to determine the size
        /// of the array for the interferences») and every item an
        /// IInterference. `arrayLength` is null when there was no array,
        /// `whyNoList` the exception that took it, if any.
        ///
        /// No interop type in the signature or the body, on purpose: the
        /// branches a bench case cannot build (no array, a short one, an item
        /// of another type) can be tested without SOLIDWORKS if this is moved
        /// to a file Oce.Solid.Agent.ContractTests compiles.
        /// </summary>
        internal static string InterferenceListProblem(
            int counted, int? arrayLength, int notInterferences, string whyNoList)
        {
            var problems = new List<string>();
            if (arrayLength == null)
            {
                if (counted > 0)
                    problems.Add((whyNoList ?? "GetInterferences returned no array") +
                                 ", after GetInterferenceCount counted " + counted);
            }
            else if ((int)arrayLength != counted)
                problems.Add("GetInterferenceCount counted " + counted +
                             " and GetInterferences returned an array of " + (int)arrayLength);
            if (notInterferences > 0)
                problems.Add(notInterferences + (notInterferences == 1 ? " item" : " items") +
                             " of the array not an IInterference");
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>
        /// The volume of the interpenetration, in mm3. `false` when it could not be
        /// read — which is **not** zero: zero would mean «they just touch
        /// and nothing more», and it would be an answer, not a gap.
        /// </summary>
        private static bool InterferenceVolumeMm3(IInterference interference, out double mm3)
        {
            mm3 = 0;
            try
            {
                var body = interference.GetInterferenceBody() as IBody2;
                if (body == null) return false;
                var mp = body.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length < 4) return false;
                // `GetMassProperties` gives the volume in cubic metres at position 3.
                mm3 = mp[3] * 1e9;
                return true;
            }
            catch { return false; }
        }
    }
}
