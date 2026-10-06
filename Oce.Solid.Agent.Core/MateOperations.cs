using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// MATING (entry GA4).
    ///
    /// ── THE ENTRY WAS BLOCKED ON SOMETHING THAT WAS NOT THERE ────────────
    ///
    /// GA4 declared an unknown: *«which selection marks need to be used, the
    /// reflection does not say»*, and attached the estimate of the hours to it.
    ///
    /// **No mark is needed.** Measured on 17/9/2026 with a probe written
    /// for another question: the two entities are selected with
    /// `IEntity.Select4(append: true, null)` and `AddMate5` is called. Answer:
    /// `swAddMateError_NoError`. The marks serve other families — the
    /// draft, the fill — not this one.
    ///
    /// *The unknown that blocked an entry of 8-10 hours fell inside a probe
    /// that was looking for something else. It is worth writing down: probes must be read in full,
    /// not only for the line one went to look for.*
    ///
    /// ── HOW THE TWO FACES ARE NAMED ──────────────────────────────────────
    ///
    /// The state of the assembly gives users `C1..Cn` for the components, and
    /// **no identifier for their faces**. Building one would mean
    /// numbering the faces of every component — hundreds on a real assembly — and
    /// they would be **positional**, that is to be reread at every change.
    ///
    /// Here the vocabulary the project already has for parts is used instead: the
    /// **direction**. «the top face of C1», «the bottom one of C2». It does not cover
    /// all the cases — an oblique face has no direction — but it covers what
    /// is needed to lay a lid on a box, and it **says so when it is
    /// not enough** instead of guessing.
    ///
    /// ── THE MEASUREMENT, WHICH IS NOT THE RETURN ─────────────────────────
    ///
    /// `AddMate5` returns a `Mate2` **and** writes an `ErrorStatus`. Neither of
    /// the two is the measurement: the mates of the two components are counted before and
    /// after, and it is required that they have gone up. A non-null `Mate2` with the
    /// counts unchanged is the defect that gives no error, carried into
    /// assemblies.
    ///
    /// **And `swAddMateError_NoError` IS WORTH 1, not 0** (`ErrorUknown` is worth 0): an
    /// `if (error != 0)` would declare a successful mate failed.
    ///
    /// ── THE SECOND NUMBER: WHAT THE MATE BLOCKS (TODO entry Z-C14) ───────
    ///
    /// The count before/after does not see a REDUNDANT mate: the same
    /// coincident twice adds a mate, and blocks nothing. Nothing else in
    /// SOLIDWORKS sees it either (probe zc14/probe_mate_dof, 30/9/2026):
    /// AddMate5 answers NoError, GetWhatsWrongCount stays 0, the mate feature's
    /// error is 0 and GetConstrainedStatus answers 2 as before; and
    /// GetRemainingDOFs answers Unavailable in every state (RAG note 82d, §7).
    ///
    /// What the probe found to work is a PERTURBATION TEST: each non-fixed
    /// component of the pair is moved 5 mm along X, Y and Z and turned 5
    /// degrees about X, Y and Z of the assembly, one move at a time, by
    /// setting IComponent2.Transform2 and rebuilding; a direction the mates
    /// put back is blocked, one where the component stays is free. Two blocks
    /// with the first fixed read 0 blocked with no mate, 3 (TY, RX, RZ) with a
    /// coincident, 3 again with the same coincident repeated, 4 with a
    /// parallel added, 6 fully mated. The test runs before AddMate5 and after
    /// it (see ReadBlockedDirections), and a mate that blocks nothing new is
    /// removed and refused.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome Mate(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed(
                    "This is not an assembly, it is a document of type «" +
                    DocumentTypeName(TypeOf(model)) + "». Mates are between " +
                    "components of an assembly.");

            object[] rawItems = null;
            try { rawItems = assemblyDoc.GetComponents(false) as object[]; } catch { }
            var components = DocumentState.ComponentsInOrder(rawItems);
            if (components.Count < 2)
                return Failed(
                    "To mate you need two components, and here there" +
                    (components.Count == 1 ? " is only one" : " is none") +
                    ". Insert them with «insert_component».");

            string uidA = Str(a, "component_a", null);
            string uidB = Str(a, "component_b", null);
            var compA = PerUid(components, uidA);
            var compB = PerUid(components, uidB);
            if (compA == null) return Failed(NotFound(uidA, "component_a", components));
            if (compB == null) return Failed(NotFound(uidB, "component_b", components));
            if (ReferenceEquals(compA, compB))
                return Failed(
                    "«" + uidA + "» and «" + uidB + "» are the same component. A part " +
                    "is not mated with itself: it constrains nothing and the " +
                    "assembly would stay free as before.");

            // ── CONCENTRIC AND TANGENT CANNOT SUCCEED HERE (entry Z-C21.2,
            // finding M17) ────────────────────────────────────────────────────
            //
            // The faces are chosen by DIRECTION, with IFace2.Normal (FaceNormalOf), and
            // IFace2~Normal.html says: «Gets the unit normal vector for any planar
            // face» and, in the Remarks, «If the face is not a planar face, then this
            // property returns 0,0,0». So the faces that arrive here are
            // always PLANAR, and between two planar faces a concentric or a tangent
            // do not exist: the catalogue had two types that could never succeed,
            // and they failed with a SOLIDWORKS error that sent one looking
            // elsewhere. The why is stated before touching anything.
            string requested = (Str(a, "variant", "coincident") ?? "").Trim().ToLowerInvariant();
            if (requested == "concentric" || requested == "tangent")
                return Failed(
                    "«" + requested + "» wants at least one curved face (a cylinder, a hole), and here " +
                    "the faces are chosen by direction — up, down, front… — which only works for planar " +
                    "faces: between two planar faces a «" + requested + "» does not exist. With planar " +
                    "faces there are coincident, parallel, perpendicular and distance. I touched " +
                    "nothing.");

            string facingA = Str(a, "face_a", null);
            string facingB = Str(a, "face_b", null);
            // THE DIRECTIONS ARE THE ASSEMBLY'S (finding M1, lot Z-C21.5): the
            // faces come from the bodies in the PART's coordinates, so each
            // normal is turned by the component's Transform2 before it is
            // compared with the direction asked for. ComponentAxes is the
            // reading of Transform2 that finding N26 measured (the axes of a
            // mirrored instance, MirrorComponentOperations.cs).
            var faceA = FaceNormalOf(compA, facingA, ComponentAxes(compA), out string why1);
            if (faceA == null) return Failed("On «" + uidA + "»: " + why1);
            var faceB = FaceNormalOf(compB, facingB, ComponentAxes(compB), out string why2);
            if (faceB == null) return Failed("On «" + uidB + "»: " + why2);

            string typeName = Str(a, "variant", "coincident");
            int typ = MateType(typeName);
            if (typ < 0)
                return Failed(
                    "Mate «" + typeName + "» unknown. There are: " +
                    string.Join(", ", KnownTypes) + ".");

            double distance = Num(a, "distance_mm", double.NaN);
            if (typ == (int)swMateType_e.swMateDISTANCE && double.IsNaN(distance))
                return Failed(
                    "A «distance» mate without «distance_mm» has no " +
                    "reasonable default value: choosing one would mean " +
                    "deciding for you how far apart two faces are.");
            if (double.IsNaN(distance)) distance = 0;

            // ONLY THE TWO NAMES (W3.1-bis.10b). Anything that was not «opposite» used
            // to mean aligned: after the rename an old or misspelt value would have built
            // the other mate in silence, and the volume does not see an alignment.
            string alignment = (Str(a, "alignment", "aligned") ?? "aligned").Trim();
            if (!alignment.Equals("aligned", StringComparison.OrdinalIgnoreCase) &&
                !alignment.Equals("opposite", StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "Alignment «" + alignment + "» unknown: you can write aligned (the default) " +
                    "or opposite. I touched nothing.");
            int align = alignment.Equals("opposite", StringComparison.OrdinalIgnoreCase)
                ? (int)swMateAlign_e.swMateAlignANTI_ALIGNED
                : 0;   // swAlignNONE = aligned

            // ── IT IS COUNTED FIRST ─────────────────────────────────────
            int priorA = MatesOf(compA), priorB = MatesOf(compB);

            // ── WHAT IS BLOCKED BEFORE THE MATE (Z-C14) ─────────────────
            //
            // After the faces are found, so that a direction that names no
            // face is refused before any rebuild is spent (the test costs 13
            // per component). A fixed component is not moved: it is fully
            // blocked by definition.
            string nameA = NameOf(compA), nameB = NameOf(compB);
            var topLevel = TopLevelNamesOf(assemblyDoc);
            var beforeA = ReadBlockedDirections(model, assemblyDoc, nameA, topLevel);
            var beforeB = ReadBlockedDirections(model, assemblyDoc, nameB, topLevel);

            // The test moved the components and rebuilt: the handles and the
            // faces are taken again, as after the mate below.
            var testedA = ComponentByName(assemblyDoc, nameA) ?? compA;
            var testedB = ComponentByName(assemblyDoc, nameB) ?? compB;
            faceA = FaceNormalOf(testedA, facingA, ComponentAxes(testedA), out why1);
            if (faceA == null)
                return Failed("On «" + uidA + "», after the test of its directions: " + why1 +
                               DriftAfterTest(uidA, beforeA) + DriftAfterTest(uidB, beforeB) +
                               " Nothing was mated. Read the assembly state again and ask for the mate once more.");
            faceB = FaceNormalOf(testedB, facingB, ComponentAxes(testedB), out why2);
            if (faceB == null)
                return Failed("On «" + uidB + "», after the test of its directions: " + why2 +
                               DriftAfterTest(uidA, beforeA) + DriftAfterTest(uidB, beforeB) +
                               " Nothing was mated. Read the assembly state again and ask for the mate once more.");

            object mate;
            int error = 0;
            try
            {
                model.ClearSelection2(true);
                // THE TWO SELECTIONS ARE READ (Z-C21.8, «Select4 in mate»).
                // IEntity~Select4.html, Return Value: «True if entity is
                // selected, false if not». Until library 0.144.0 both answers
                // were dropped, and AddMate5 ran on whatever the selection held:
                // one face only, or a face left selected by an earlier step or
                // by hand. The finally below clears the selection on this exit
                // too.
                bool selectedA = (faceA as IEntity).Select4(true, null);
                bool selectedB = selectedA && (faceB as IEntity).Select4(true, null);
                if (!selectedA || !selectedB)
                    return Failed(
                        "I could not select the «" + (selectedA ? facingB : facingA) + "» face of " +
                        (selectedA ? uidB : uidA) + " (IEntity.Select4 answered false), so AddMate5 was " +
                        "not called: it would have mated whatever else was selected. Nothing was " +
                        "changed. Read the assembly state again and ask for the mate once more.");
                // THE DISTANCE MATE ENDS ON THE SIDE ASKED FOR WITH Flip = false
                // (finding N10, NOT REAL). The Z-C21.3 probe of N10 (28/9/2026)
                // mated a 10 mm block at 30 mm starting from above (s = +90 mm)
                // and from below (s = -110 mm), anti-aligned (and aligned, from
                // above only), with mark 0 and with the guide's mark 1: every
                // Flip = false variant
                // ended at s = +30 mm, the requested side, IMate2.Flipped False;
                // Flip = true sent it to s = -30 mm. So Flip stays false, and
                // Transform2 is not reread against the side.
                mate = assemblyDoc.AddMate5(
                    typ, align, false,
                    Units.M(distance), Units.M(distance), Units.M(distance),
                    0, 0, 0, 0, 0, false, false, 0, out error);
            }
            catch (Exception ex)
            {
                return Failed("I could not mate: " + ex.Message);
            }
            finally { try { model.ClearSelection2(true); } catch { } }

            // `NoError` IS WORTH 1. Zero is `ErrorUknown`.
            if (error != (int)swAddMateError_e.swAddMateError_NoError)
                return Failed(
                    "SOLIDWORKS refused the mate: " + MateErrorName(error) +
                    ". The two faces are the «" + facingA + "» of " + uidA + " and the «" +
                    facingB + "» of " + uidB + "; the mate requested is «" +
                    typeName + "»" +
                    (align == 0 ? " aligned" : " opposite") + ".");

            try { model.EditRebuild3(); } catch { }

            // ── AND IT IS COUNTED AFTER ─────────────────────────────────
            //
            // The handles may have been invalidated by the rebuild:
            // they are reread by name, as `insert_component` does.
            var afterComp = DocumentState.ComponentsInOrder(
                assemblyDoc.GetComponents(false) as object[]);
            // By the names read before the test of the directions (Z-C14):
            // thirteen rebuilds later the first handles are the least
            // trustworthy thing in this method.
            var riA = ByName(afterComp, nameA) ?? testedA;
            var riB = ByName(afterComp, nameB) ?? testedB;
            int laterA = MatesOf(riA), laterB = MatesOf(riB);

            if (laterA <= priorA && laterB <= priorB)
                return Failed(
                    "It answered yes (" + (mate == null ? "without an object" : "with the object") +
                    ") but the mates did not increase: " + uidA + " had " +
                    priorA + " and has " + laterA + ", " + uidB + " had " + priorB +
                    " and has " + laterB + ". I did not consider it done.");

            // ── WHAT IS BLOCKED AFTER IT, AND WHAT THE MATE REMOVED (Z-C14) ─
            //
            // SOLIDWORKS's own answer is read first, to be quoted: on a
            // redundant mate it says nothing is wrong (probe zc14, 30/9/2026).
            int whatsWrong = HowManyProblems(model);
            var afterA = beforeA.Status == ReadingMeasured ? ReadBlockedDirections(model, assemblyDoc, nameA, topLevel) : beforeA;
            var afterB = beforeB.Status == ReadingMeasured ? ReadBlockedDirections(model, assemblyDoc, nameB, topLevel) : beforeB;
            string verdict = MateVerdict(
                BothReadings(beforeA, afterA), beforeA.Blocked, afterA.Blocked,
                BothReadings(beforeB, afterB), beforeB.Blocked, afterB.Blocked);
            // THE ONE-WAY TEST AND LIMIT MATES (review of Z-C14): the test moves +5 mm
            // and +5 degrees only, so a component resting at the end of a limit mate,
            // or held by a mechanical mate, can read «blocked» in a direction that is
            // free the other way, and a real mate added later would then read as
            // removing nothing. «redundant» is kept only when every mate of the
            // assembly is a standard one without limits; otherwise the mate stays.
            string notStandard = null;
            if (verdict == MateRedundant && !OnlyStandardMates(model, out notStandard))
                verdict = MateNotJudged;
            string mateSaid =
                "«" + facingA + "» of " + uidA + " on «" + facingB + "» of " + uidB + " (" + typeName +
                (typ == (int)swMateType_e.swMateDISTANCE
                    ? " " + distance.ToString("0.###", CultureInfo.InvariantCulture) + " mm" : "") + ")";
            string directions =
                DirectionSentence(uidA, beforeA, afterA) + " " + DirectionSentence(uidB, beforeB, afterB);

            // A MATE THAT BLOCKS NOTHING NEW IS TAKEN OUT AND REFUSED. It is
            // removed by the object AddMate5 returned, never looked for by
            // name, and the mate count is read again (RemoveTheMateJustMade).
            if (verdict == MateRedundant)
            {
                string refusal =
                    "The mate " + mateSaid + " is REDUNDANT: it blocks nothing that was not already " +
                    "blocked. " + directions + " " +
                    (whatsWrong == 0
                        ? "SOLIDWORKS itself reports nothing wrong (AddMate5 answered NoError, " +
                          "GetWhatsWrongCount is 0): only the directions show it. "
                        : "AddMate5 answered NoError, and GetWhatsWrongCount reads " +
                          (whatsWrong < 0 ? "nothing (unreadable)" : whatsWrong.ToString(CultureInfo.InvariantCulture)) +
                          "; the directions show that this mate adds nothing. ") +
                    HowDirectionsAreMeasured +
                    RemoveTheMateJustMade(model, assemblyDoc, mate, nameA, nameB, uidA, uidB, priorA, priorB);
                _log("[agent] " + refusal);
                return Failed(refusal);
            }

            var measurements = new Dictionary<string, object>
            {
                ["kind"] = typeName,
                ["mates_before"] = new object[] { priorA, priorB },
                ["mates_after"] = new object[] { laterA, laterB },
            };
            if (typ == (int)swMateType_e.swMateDISTANCE) measurements["distance_mm"] = distance;

            bool fixedA = IsFixed(riA), fixedB = IsFixed(riB);
            measurements["resting_a"] = !fixedA && laterA == 0;
            measurements["resting_b"] = !fixedB && laterB == 0;

            // Z-C14: per component, in the order of mates_before/after.
            measurements["blocked_directions_before"] = new object[] { beforeA.Blocked, beforeB.Blocked };
            measurements["blocked_directions_after"] = new object[] { afterA.Blocked, afterB.Blocked };
            measurements["degrees_removed"] = new object[] { DegreesRemoved(beforeA, afterA), DegreesRemoved(beforeB, afterB) };
            measurements["direction_test"] = new object[] { DescribeTest(beforeA, afterA), DescribeTest(beforeB, afterB) };
            measurements["residual_drift_mm"] = new object[] { LargestDrift(beforeA, afterA, false), LargestDrift(beforeB, afterB, false) };
            measurements["residual_drift_deg"] = new object[] { LargestDrift(beforeA, afterA, true), LargestDrift(beforeB, afterB, true) };
            measurements["mate_verdict"] = verdict;

            string verdictValue =
                "Mated " + mateSaid + ". Now " + uidA + " has " + laterA + (laterA == 1 ? " mate" : " mates") +
                " and " + uidB + " has " + laterB + ". " + directions +
                (notStandard != null
                    ? " This mate blocks nothing new by the test, but the assembly has " + notStandard + ": the test " +
                      "moves one way only, so a direction held at the end of a limit reads blocked, and I do not call " +
                      "the mate redundant. The mate stays."
                    : VerdictTail(verdict)) + " " +
                HowDirectionsAreMeasured;
            if (notStandard != null) measurements["not_judged_because"] = "the assembly has " + notStandard;
            measurements["verdict"] = verdictValue;
            _log("[agent] " + verdictValue);

            return SucceededWithoutCreating(measurements);
        }

        // =================================================================
        // Z-C14 — THE PERTURBATION TEST OF A COMPONENT'S DIRECTIONS
        // =================================================================

        /// <summary>The six directions of the test, in the order the test moves them.</summary>
        internal static readonly string[] DirectionNames = { "TX", "TY", "TZ", "RX", "RY", "RZ" };

        /// <summary>
        /// The moves and the rule of the probe (zc14/probe_mate_dof_2.out,
        /// 30/9/2026): 5 mm and 5 degrees out, back within 0.01 mm or 0.01
        /// degree is blocked.
        /// </summary>
        internal const double TestMoveMm = 5.0, TestMoveDeg = 5.0, BackWithinMm = 0.01, BackWithinDeg = 0.01;

        /// <summary>The state of one reading of one component.</summary>
        internal const string ReadingFixed = "fixed", ReadingMeasured = "measured", ReadingUnknown = "unknown";

        /// <summary>
        /// Whether every mate of the assembly is a standard one without limits
        /// (review of Z-C14). <paramref name="notStandard"/> names the first one
        /// that is not, or says the mates could not be read (an unreadable mate
        /// counts as not standard). The mates are the sub-features of the
        /// MateGroup feature; each answers IMate2 through GetSpecificFeature2
        /// (IMate2~Type.html: «Gets the type of mate as defined in
        /// swMateType_e»), and a distance or angle mate is a LIMIT mate when its
        /// definition says IsAdvancedMate (IDistanceMateFeatureData~IsAdvancedMate.html:
        /// «Gets or sets whether this distance mate is a limit distance mate»;
        /// IAngleMateFeatureData~IsAdvancedMate.html, the same for angles).
        /// </summary>
        private static bool OnlyStandardMates(IModelDoc2 model, out string notStandard)
        {
            notStandard = null;
            try
            {
                IFeature group = null;
                var f = model.FirstFeature() as IFeature;
                while (f != null) { if (f.GetTypeName2() == "MateGroup") group = f; f = f.GetNextFeature() as IFeature; }
                if (group == null) return true;
                var sub = group.GetFirstSubFeature() as IFeature;
                while (sub != null)
                {
                    var mate = sub.GetSpecificFeature2() as IMate2;
                    if (mate != null)
                    {
                        int type = mate.Type;
                        bool? advanced = null;
                        if (type == (int)swMateType_e.swMateDISTANCE)
                        {
                            var d = sub.GetDefinition() as IDistanceMateFeatureData;
                            if (d != null) advanced = d.IsAdvancedMate;
                        }
                        else if (type == (int)swMateType_e.swMateANGLE)
                        {
                            var d = sub.GetDefinition() as IAngleMateFeatureData;
                            if (d != null) advanced = d.IsAdvancedMate;
                        }
                        if (!StandardMate(type, advanced))
                        {
                            string name = "?";
                            try { name = sub.Name; } catch (Exception) { }
                            notStandard = "a mate that is a limit or mechanical one («" + name + "», " +
                                          (Enum.GetName(typeof(swMateType_e), type) ?? type.ToString(CultureInfo.InvariantCulture)) +
                                          (advanced == true ? ", limit" : "") + ")";
                            return false;
                        }
                    }
                    sub = sub.GetNextSubFeature() as IFeature;
                }
                return true;
            }
            catch (Exception ex)
            {
                notStandard = "mates I could not read (" + ex.Message + ")";
                return false;
            }
        }

        /// <summary>
        /// The pure rule of <see cref="OnlyStandardMates"/>: coincident,
        /// concentric, perpendicular, parallel, tangent and lock are standard;
        /// distance and angle only when they are not limit mates (an unread
        /// flag is not taken as «no limit»); every other type (gear, cam, slot,
        /// path, width, screw...) is not.
        /// </summary>
        internal static bool StandardMate(int type, bool? advanced)
        {
            switch (type)
            {
                case (int)swMateType_e.swMateCOINCIDENT:
                case (int)swMateType_e.swMateCONCENTRIC:
                case (int)swMateType_e.swMatePERPENDICULAR:
                case (int)swMateType_e.swMatePARALLEL:
                case (int)swMateType_e.swMateTANGENT:
                case (int)swMateType_e.swMateLOCK:
                    return true;
                case (int)swMateType_e.swMateDISTANCE:
                case (int)swMateType_e.swMateANGLE:
                    return advanced == false;
                default:
                    return false;
            }
        }

        /// <summary>What <see cref="MateVerdict"/> answers.</summary>
        internal const string MateRemoves = "removes", MateRedundant = "redundant",
                              MateUnknown = "unknown", MateNotJudged = "not_judged";

        private const string HowDirectionsAreMeasured =
            "(Directions measured by moving each non-fixed component of the pair 5 mm along X, Y and Z and " +
            "turning it 5 degrees about X, Y and Z of the assembly, one move at a time with a rebuild after " +
            "each: a direction the mates put back is blocked.)";

        /// <summary>What the test read on one component, once.</summary>
        private sealed class DirectionReading
        {
            public string Status = ReadingUnknown;
            public string Why;                 // ReadingUnknown: what could not be set, read or rebuilt
            public string[] Blocked;           // the names blocked; all six when fixed; null when unknown
            public double DriftMm = double.NaN, DriftDeg = double.NaN;   // from where it started, after the last restore
            public int Rebuilds;
            public readonly List<string> Slides = new List<string>();   // what a restore along the way left
            public readonly List<string> Others = new List<string>();   // other components the test left away from where they were
        }

        /// <summary>
        /// THE PERTURBATION TEST on one component, exactly as the probe
        /// measured it (zc14/probe_mate_dof.cs, decisive run
        /// probe_mate_dof_2.out, 30/9/2026).
        ///
        /// For each of the six moves (5 mm along X, Y, Z of the assembly, the
        /// translation part [9..11] of ArrayData; 5 degrees about X, Y, Z
        /// through the component's origin): IComponent2.Transform2 is set to the
        /// moved transform built with IMathUtility.CreateTransform, EditRebuild3,
        /// Transform2 read again. IComponent2~Transform2.html, Remarks: «This
        /// property lets you violate existing mate relationships. If you place a
        /// component at an invalid location based on the mate definitions, then
        /// IModelDoc2::Rebuild recalculates existing mate relationships and moves
        /// your components to the closest valid location», and the transform is
        /// «in relation to the root component». IMathUtility~CreateTransform.html:
        /// the array is the «Sixteen (16) components of the transform», the same
        /// shape IMathTransform.ArrayData answers, which is what is handed back.
        ///
        /// ONLY THE MOVED DIRECTION IS JUDGED (<see cref="DirectionBlocked"/>):
        /// the solver can put a rotation back by turning about another point and
        /// slide the component 1.7-2.4 mm along a FREE direction (probe, S3); a
        /// rule on the whole transform read those rotations free.
        ///
        /// After each move Transform2 goes back to where the move started and the
        /// assembly is rebuilt; that can leave a 0.2 mm slide along a free
        /// direction (probe, S3 after RY), so the next move starts from where the
        /// component really is and the slide is noted. At the END the component
        /// is set back to the transform it had before the test, rebuilt and read
        /// again, and what is left is reported (DriftMm, DriftDeg), not hidden.
        /// The other top-level components that are not fixed are read before
        /// the test too: a rebuild can carry a component mated to this one
        /// along, so any that is away from where it was at the end is set back
        /// in the same final rebuild, and what is still left is reported
        /// (Others).
        ///
        /// NEVER A GUESS: a transform that cannot be set or read, a set that did
        /// not move the component by the step asked, a rebuild that answers false
        /// (EditRebuild3 «only works in-context of the active document»,
        /// IModelDoc2~EditRebuild3.html, Remarks), a move that after the rebuild
        /// is neither back nor where it was moved (the probe's «ELSEWHERE») or an
        /// orientation that does not come back makes the reading ReadingUnknown,
        /// with the reason. A fixed
        /// component is not moved (fully blocked by definition); a component
        /// inside a sub-assembly is not moved either: setting its Transform2 moves
        /// the sub-assembly («The destination component, or any component above
        /// it in the assembly tree, is moved», same Remarks), whose fixed flag is
        /// not known from the root (IComponent2~IsFixed.html).
        ///
        /// COST: two rebuilds per direction and one to put it back, 13 per
        /// component per reading.
        /// </summary>
        private DirectionReading ReadBlockedDirections(
            IModelDoc2 model, IAssemblyDoc assemblyDoc, string name, HashSet<string> topLevel)
        {
            var reading = new DirectionReading();
            var component = ComponentByName(assemblyDoc, name);
            if (component == null)
            {
                reading.Why = "the component does not answer to its name, so it could not be moved";
                return reading;
            }
            if (topLevel == null)
            {
                reading.Why = "the top-level components could not be read (GetComponents(true)), so I cannot tell " +
                              "whether it sits inside a sub-assembly";
                return reading;
            }
            if (!topLevel.Contains(name))
            {
                reading.Why = "it is inside a sub-assembly: setting its Transform2 moves the sub-assembly, whose " +
                              "fixed flag is not known from here, so it was not moved";
                return reading;
            }
            if (IsFixed(component))
            {
                reading.Status = ReadingFixed;
                reading.Blocked = (string[])DirectionNames.Clone();
                return reading;
            }

            IMathUtility math = null;
            try { math = _sw.GetMathUtility() as IMathUtility; } catch { }
            var original = ReadTransform(component);
            if (math == null || original == null)
            {
                reading.Why = math == null ? "IMathUtility did not answer, so no moved transform could be built"
                                           : "its Transform2 could not be read";
                return reading;
            }

            // THE OTHER COMPONENTS, AS THEY WERE: the rebuild after a move can
            // carry along a component mated to this one (the solver moves what
            // it needs to satisfy every mate), and putting this one back does
            // not promise to bring that one back too.
            var others = OtherPositions(assemblyDoc, name, topLevel);

            var blocked = new List<string>();
            var slides = new List<string>();
            var reference = original;
            string why = null;
            for (int k = 0; k < DirectionNames.Length && why == null; k++)
            {
                string d = DirectionNames[k];
                var moved = MovedForTest(reference, k);
                if (moved == null || !SetTransform(assemblyDoc, name, math, moved))
                { why = "Transform2 could not be set for the move " + d; break; }
                var afterSet = ReadTransform(ComponentByName(assemblyDoc, name));
                if (!MoveTookEffect(reference, afterSet, k))
                { why = "setting Transform2 did not move it by the step asked (" + d + ")"; break; }
                reading.Rebuilds++;
                if (!RebuildForTest(model))
                { why = "EditRebuild3 did not answer true after the move " + d + RebuildFalseHint; break; }
                var afterRebuild = ReadTransform(ComponentByName(assemblyDoc, name));
                bool? isBlocked = DirectionBlocked(reference, afterRebuild, k);
                if (isBlocked == null)
                { why = "its Transform2 could not be read after the move " + d; break; }
                // NEITHER BACK NOR WHERE IT WAS MOVED: the probe's third reading
                // («ELSEWHERE», zc14/probe_mate_dof.cs). None of its rows fell
                // there, and «free» for it would be a guess: a face up to 8
                // degrees off the axis (FaceNormalOf takes a dot above 0.99)
                // puts a translation back only in part, and a direction read
                // free both before and after a mate that holds it is exactly a
                // mate called redundant and taken out.
                if (isBlocked == false && !MoveTookEffect(reference, afterRebuild, k))
                {
                    why = "after the move " + d + " and the rebuild it neither came back nor stayed where it was moved (" +
                          MovementOf(reference, afterRebuild, k).ToString("0.###", CultureInfo.InvariantCulture) +
                          (k < 3 ? " mm of " + TestMoveMm.ToString(CultureInfo.InvariantCulture) + " along the axis"
                                 : " degrees of " + TestMoveDeg.ToString(CultureInfo.InvariantCulture)) +
                          "), so that direction is neither free nor blocked";
                    break;
                }
                if (isBlocked == true) blocked.Add(d);

                // Back to where this move started, and a slide left by the
                // solver along a free direction is noted, not added up.
                if (!SetTransform(assemblyDoc, name, math, reference))
                { why = "Transform2 could not be set to put it back after the move " + d; break; }
                reading.Rebuilds++;
                if (!RebuildForTest(model))
                { why = "EditRebuild3 did not answer true after putting it back from " + d + RebuildFalseHint; break; }
                var back = ReadTransform(ComponentByName(assemblyDoc, name));
                double turned = AngleBetweenDeg(back, reference);
                if (double.IsNaN(turned))
                { why = "its Transform2 could not be read after putting it back from " + d; break; }
                if (turned > BackWithinDeg)
                {
                    why = "after the move " + d + " it did not come back to its orientation (" +
                          turned.ToString("0.###", CultureInfo.InvariantCulture) + " degrees off)";
                    break;
                }
                double slid = DistanceMm(back, reference);
                if (slid > BackWithinMm)
                    slides.Add("after " + d + " it came back " +
                               slid.ToString("0.###", CultureInfo.InvariantCulture) + " mm away");
                // As the probe: the next move starts from where it really is.
                reference = back;
            }

            // AT THE END, WHERE IT STARTED: the transform it had before the
            // test, a rebuild, and what is left read and kept. Every other
            // component the test carried along is set back in the same
            // rebuild: they all were where they started together, a state the
            // mates already accepted.
            var setBack = SetOthersBack(assemblyDoc, math, others);
            if (SetTransform(assemblyDoc, name, math, original))
            {
                reading.Rebuilds++;
                if (RebuildForTest(model))
                {
                    var final = ReadTransform(ComponentByName(assemblyDoc, name));
                    reading.DriftMm = DistanceMm(final, original);
                    reading.DriftDeg = AngleBetweenDeg(final, original);
                }
            }
            reading.Others.AddRange(OthersLeftMoved(assemblyDoc, others, setBack));

            reading.Slides.AddRange(slides);
            if (why != null) { reading.Why = why; return reading; }
            reading.Status = ReadingMeasured;
            reading.Blocked = blocked.ToArray();
            return reading;
        }

        /// <summary>The component of that Name2, read again from the assembly, or null.</summary>
        private static IComponent2 ComponentByName(IAssemblyDoc assemblyDoc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try { return ByName(DocumentState.ComponentsInOrder(assemblyDoc.GetComponents(false) as object[]), name); }
            catch { return null; }
        }

        /// <summary>
        /// The top-level components other than <paramref name="name"/> that
        /// are not fixed, by Name2, with the transform each has before the
        /// test. A fixed one is not moved by a rebuild; a nested one moves with
        /// its top-level parent, which is in the list.
        /// </summary>
        private static Dictionary<string, double[]> OtherPositions(IAssemblyDoc assemblyDoc, string name, HashSet<string> topLevel)
        {
            var positions = new Dictionary<string, double[]>(StringComparer.Ordinal);
            var byName = TopLevelByName(assemblyDoc);
            foreach (string other in byName.Keys)
            {
                IComponent2 component = byName[other];
                if (other == name || !topLevel.Contains(other) || IsFixed(component)) continue;
                var transform = ReadTransform(component);
                if (transform != null) positions[other] = transform;
            }
            return positions;
        }

        /// <summary>The top-level components by Name2, read once; empty when they cannot be read.</summary>
        private static Dictionary<string, IComponent2> TopLevelByName(IAssemblyDoc assemblyDoc)
        {
            var byName = new Dictionary<string, IComponent2>(StringComparer.Ordinal);
            object[] all = null;
            try { all = assemblyDoc.GetComponents(true) as object[]; } catch { }
            if (all == null) return byName;
            foreach (object o in all)
            {
                IComponent2 component = o as IComponent2;
                string other = NameOf(component);
                if (!string.IsNullOrEmpty(other) && !byName.ContainsKey(other)) byName[other] = component;
            }
            return byName;
        }

        /// <summary>Whether a component is away from a transform by more than the test's two thresholds; true when either is unreadable.</summary>
        private static bool AwayFrom(double[] then, double[] now)
        {
            double mm = DistanceMm(then, now), deg = AngleBetweenDeg(then, now);
            return double.IsNaN(mm) || double.IsNaN(deg) || mm > BackWithinMm || deg > BackWithinDeg;
        }

        /// <summary>
        /// Every other component the test left away from where it was is set
        /// back to its transform from before the test; the names that could
        /// not be set. Called just before the final rebuild of the test.
        /// </summary>
        private static HashSet<string> SetOthersBack(IAssemblyDoc assemblyDoc, IMathUtility math, Dictionary<string, double[]> others)
        {
            var notSet = new HashSet<string>(StringComparer.Ordinal);
            if (others.Count == 0) return notSet;
            var now = TopLevelByName(assemblyDoc);
            foreach (string other in others.Keys)
            {
                double[] then = others[other];
                IComponent2 component;
                var transform = now.TryGetValue(other, out component) ? ReadTransform(component) : null;
                if (transform != null && !AwayFrom(then, transform)) continue;
                if (!SetTransform(assemblyDoc, other, math, then)) notSet.Add(other);
            }
            return notSet;
        }

        /// <summary>What the final rebuild left of the other components: one line for each still away from where it was, or unreadable.</summary>
        private static List<string> OthersLeftMoved(IAssemblyDoc assemblyDoc, Dictionary<string, double[]> others, HashSet<string> notSet)
        {
            var notes = new List<string>();
            if (others.Count == 0) return notes;
            var now = TopLevelByName(assemblyDoc);
            foreach (string other in others.Keys)
            {
                double[] then = others[other];
                IComponent2 component;
                var transform = now.TryGetValue(other, out component) ? ReadTransform(component) : null;
                if (transform == null) { notes.Add("«" + other + "» could not be read again: look at where it is"); continue; }
                if (!AwayFrom(then, transform)) continue;
                notes.Add("«" + other + "» " + (notSet.Contains(other) ? "could not be set back and " : "") + "sits " +
                          DistanceMm(then, transform).ToString("0.###", CultureInfo.InvariantCulture) + " mm and " +
                          AngleBetweenDeg(then, transform).ToString("0.###", CultureInfo.InvariantCulture) +
                          " degrees from where it was");
            }
            return notes;
        }

        /// <summary>The names of the top-level components (the one rule, DocumentState.TopLevelNames), or null.</summary>
        private static HashSet<string> TopLevelNamesOf(IAssemblyDoc assemblyDoc)
        {
            try { return DocumentState.TopLevelNames(assemblyDoc.GetComponents(true) as object[]); }
            catch { return null; }
        }

        /// <summary>Transform2.ArrayData, copied; null when it cannot be read or is shorter than 12 numbers.</summary>
        private static double[] ReadTransform(IComponent2 component)
        {
            try
            {
                var transform = component == null ? null : component.Transform2 as IMathTransform;
                var data = transform == null ? null : transform.ArrayData as double[];
                if (data == null || data.Length < 12) return null;
                return (double[])data.Clone();
            }
            catch { return null; }
        }

        /// <summary>IComponent2.Transform2 set to a transform built from <paramref name="data"/>; false when it could not be.</summary>
        private static bool SetTransform(IAssemblyDoc assemblyDoc, string name, IMathUtility math, double[] data)
        {
            try
            {
                var component = ComponentByName(assemblyDoc, name);
                var transform = math.CreateTransform(data) as MathTransform;
                if (component == null || transform == null) return false;
                component.Transform2 = transform;
                return true;
            }
            catch { return false; }
        }

        /// <summary>The reason a false rebuild is not rare, added to the step's words.</summary>
        private const string RebuildFalseHint =
            " (a feature or mate in error makes every rebuild answer false)";

        /// <summary>
        /// EditRebuild3, whose answer counts: a rebuild that did not happen would
        /// read every direction free. The price is known: a document with a
        /// feature in error answers false on every rebuild (DocumentByPath,
        /// RebuildIfActivationAsked), so on such an assembly the test reads
        /// unknown and the mate is kept, never judged.
        /// </summary>
        private static bool RebuildForTest(IModelDoc2 model)
        {
            try { return model.EditRebuild3(); }
            catch { return false; }
        }

        /// <summary>«fixed», «measured» or «unknown» for the pair of readings of one component.</summary>
        private static string BothReadings(DirectionReading before, DirectionReading after)
        {
            return before.Status == after.Status ? before.Status : ReadingUnknown;
        }

        /// <summary>The degrees the mate removed on one component; null unless both readings were measured.</summary>
        private static object DegreesRemoved(DirectionReading before, DirectionReading after)
        {
            if (before.Status == ReadingFixed && after.Status == ReadingFixed) return 0;
            if (before.Status != ReadingMeasured || after.Status != ReadingMeasured) return null;
            return NewlyBlocked(before.Blocked, after.Blocked).Length;
        }

        /// <summary>The larger drift the two readings left, in mm or degrees, rounded; null when neither was read.</summary>
        private static object LargestDrift(DirectionReading before, DirectionReading after, bool degrees)
        {
            double largest = double.NaN;
            foreach (var reading in new[] { before, after })
            {
                double v = degrees ? reading.DriftDeg : reading.DriftMm;
                if (!double.IsNaN(v) && (double.IsNaN(largest) || v > largest)) largest = v;
            }
            if (double.IsNaN(largest)) return null;
            return Math.Round(largest, 3);
        }

        /// <summary>One line on how a component was read, for «direction_test».</summary>
        private static string DescribeTest(DirectionReading before, DirectionReading after)
        {
            if (before.Status == ReadingFixed) return "fixed: not moved, a fixed component is fully blocked by definition";
            if (before.Status != ReadingMeasured) return "unknown: " + before.Why;
            if (after.Status != ReadingMeasured) return "unknown after the mate: " + after.Why;
            var slides = before.Slides.Concat(after.Slides).ToList();
            return "measured before and after the mate (" + (before.Rebuilds + after.Rebuilds) + " rebuilds)" +
                   (slides.Count == 0 ? "" : "; " + string.Join("; ", slides));
        }

        /// <summary>What the test says about one component, for the answer and for the refusal.</summary>
        private static string DirectionSentence(string uid, DirectionReading before, DirectionReading after)
        {
            if (before.Status == ReadingFixed)
                return uid + " is fixed: it was not moved, a fixed component is fully blocked by definition.";
            if (before.Status != ReadingMeasured || after.Status != ReadingMeasured)
                return uid + ": which directions are blocked I cannot tell, " +
                       (before.Status != ReadingMeasured ? before.Why : "after the mate " + after.Why) + "." +
                       DriftAfterTest(uid, before) + (ReferenceEquals(before, after) ? "" : DriftAfterTest(uid, after));
            var added = NewlyBlocked(before.Blocked, after.Blocked);
            var free = DirectionNames.Where(n => !after.Blocked.Contains(n)).ToArray();
            return uid + ": before the mate blocked in " + Directions(before.Blocked) + "; this mate blocked " +
                   (added.Length == 0 ? "nothing new" : Directions(added)) + " (" + added.Length +
                   (added.Length == 1 ? " degree" : " degrees") + " removed); now blocked in " +
                   Directions(after.Blocked) +
                   (free.Length == 0 ? " and it no longer moves." : " and still free in " + Directions(free) + ".") +
                   DriftAfterTest(uid, before) + DriftAfterTest(uid, after);
        }

        private static string Directions(string[] names)
        {
            return names == null || names.Length == 0 ? "no direction" : string.Join(", ", names);
        }

        /// <summary>A sentence when a test left the component away from where it started; empty otherwise.</summary>
        private static string DriftAfterTest(string uid, DirectionReading reading)
        {
            if (reading.Status == ReadingFixed || reading.Rebuilds == 0) return "";
            string others = reading.Others.Count == 0 ? ""
                : " The test of " + uid + " carried other components along, and after setting them back " +
                  string.Join("; ", reading.Others) + ".";
            if (double.IsNaN(reading.DriftMm) || double.IsNaN(reading.DriftDeg))
                return " After the test " + uid + " could not be set back and read again where it started: look at " +
                       "where it is before going on." + others;
            if (reading.DriftMm <= BackWithinMm && reading.DriftDeg <= BackWithinDeg) return others;
            return " After the test " + uid + " sits " + reading.DriftMm.ToString("0.###", CultureInfo.InvariantCulture) +
                   " mm and " + reading.DriftDeg.ToString("0.###", CultureInfo.InvariantCulture) +
                   " degrees from where it started: the rebuild left it there along a free direction." + others;
        }

        private static string VerdictTail(string verdict)
        {
            if (verdict == MateNotJudged)
                return " Neither component is fixed or already fully blocked, so I did not judge whether this mate " +
                       "adds anything: moving one of them can drag the other along, and a mate that ties the two " +
                       "together can read as blocking nothing on either. The mate stays.";
            if (verdict == MateUnknown)
                return " Whether this mate blocks anything new I cannot tell from these readings, so it stays: a mate " +
                       "alone can leave a part free to slide or turn.";
            return "";
        }

        /// <summary>
        /// Takes out the mate this step has just made, by the object AddMate5
        /// returned (never by looking for a name), and reads the mate counts
        /// again: the tail of the refusal. IMate2.html, Remarks: the IMate2
        /// object is got «as a IFeature object and then [with]
        /// IFeature::GetSpecificFeature2 or QueryInterface» the IMate2 interface,
        /// so the same object answers IFeature (measured on the objects GetMates
        /// gives, probe zd345 of 27/9/2026). DeleteSelection2 and not EditDelete,
        /// which can open a confirmation window (RemoveMirror).
        /// </summary>
        private static string RemoveTheMateJustMade(
            IModelDoc2 model, IAssemblyDoc assemblyDoc, object mate, string nameA, string nameB,
            string uidA, string uidB, int priorA, int priorB)
        {
            var feature = mate as IFeature;
            string featureName = SafeName(feature);
            bool deleted = false;
            if (feature != null)
            {
                try
                {
                    model.ClearSelection2(true);
                    if (feature.Select2(false, 0))
                        deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    model.ClearSelection2(true);
                    model.EditRebuild3();
                }
                catch { }
            }
            int nowA = MatesOf(ComponentByName(assemblyDoc, nameA));
            int nowB = MatesOf(ComponentByName(assemblyDoc, nameB));
            return MateRemovalNote(feature != null, featureName, deleted, uidA, uidB, priorA, priorB, nowA, nowB);
        }

        // ── the pure rules of Z-C14: no interop type in a signature, so that
        //    Oce.Solid.Agent.ContractTests calls them by reflection on the built library ──

        /// <summary>
        /// The transform moved by one step of the test: direction 0..2 adds
        /// 5 mm to the translation [9 + k] (ArrayData is in metres); 3..5 turns
        /// it 5 degrees about the assembly's X, Y or Z through the component's
        /// origin. ArrayData holds the rotation by rows, the component's axes in
        /// the assembly (the reading ComponentAxes and InAssemblySpace use), so
        /// a turn Q of the assembly makes each row r into r Q^T: R' = R Q^T, the
        /// probe's formula. Null when the input is not at least 12 numbers.
        /// </summary>
        internal static double[] MovedForTest(double[] start, int direction)
        {
            if (!ReadableTransform(start) || direction < 0 || direction >= DirectionNames.Length) return null;
            var moved = (double[])start.Clone();
            if (direction < 3)
            {
                moved[9 + direction] += TestMoveMm / 1000.0;
                return moved;
            }
            double angle = TestMoveDeg * Math.PI / 180.0, c = Math.Cos(angle), s = Math.Sin(angle);
            int axis = direction - 3;
            double[,] q = axis == 0 ? new double[,] { { 1, 0, 0 }, { 0, c, -s }, { 0, s, c } }
                        : axis == 1 ? new double[,] { { c, 0, s }, { 0, 1, 0 }, { -s, 0, c } }
                        : new double[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double v = 0;
                    for (int k = 0; k < 3; k++) v += start[i * 3 + k] * q[j, k];
                    moved[i * 3 + j] = v;
                }
            return moved;
        }

        /// <summary>
        /// How far a component went in ONE direction between two transforms:
        /// for 0..2 the displacement ALONG that axis, in mm; for 3..5 the angle
        /// between the two rotation parts, in degrees. NaN when either is
        /// unreadable. Along the axis only: a slide along another, free,
        /// direction says nothing about the one tested (probe, S3).
        /// </summary>
        internal static double MovementOf(double[] from, double[] to, int direction)
        {
            if (!ReadableTransform(from) || !ReadableTransform(to) || direction < 0 || direction >= DirectionNames.Length)
                return double.NaN;
            if (direction < 3) return Math.Abs(to[9 + direction] - from[9 + direction]) * 1000.0;
            return AngleBetweenDeg(from, to);
        }

        /// <summary>
        /// THE RULE OF THE PROBE: after the move and the rebuild, a translation
        /// is BLOCKED when it is back within 0.01 mm along its axis, a rotation
        /// when the rotation is back within 0.01 degree; otherwise it is free.
        /// Null when a transform is unreadable, never «free» or «blocked».
        /// </summary>
        internal static bool? DirectionBlocked(double[] start, double[] afterRebuild, int direction)
        {
            double moved = MovementOf(start, afterRebuild, direction);
            if (double.IsNaN(moved)) return null;
            return moved < (direction < 3 ? BackWithinMm : BackWithinDeg);
        }

        /// <summary>
        /// Whether the component is away from <paramref name="start"/> by the
        /// step asked in that direction. Before the rebuild: the set took
        /// effect (a set that did nothing would read every direction blocked
        /// after the rebuild). After it: the direction is free, and a move that
        /// is neither this nor <see cref="DirectionBlocked"/> is the probe's
        /// «ELSEWHERE», read as unknown.
        /// </summary>
        internal static bool MoveTookEffect(double[] start, double[] afterSet, int direction)
        {
            double moved = MovementOf(start, afterSet, direction);
            if (double.IsNaN(moved)) return false;
            return direction < 3 ? Math.Abs(moved - TestMoveMm) < BackWithinMm : Math.Abs(moved - TestMoveDeg) < BackWithinDeg;
        }

        /// <summary>The angle of the relative rotation between two transforms, in degrees; NaN when unreadable.</summary>
        internal static double AngleBetweenDeg(double[] a, double[] b)
        {
            if (!ReadableTransform(a) || !ReadableTransform(b)) return double.NaN;
            double trace = 0;
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 3; k++) trace += a[i * 3 + k] * b[i * 3 + k];   // trace(A B^T)
            double cos = Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) / 2.0));
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        /// <summary>The distance between the two translations, in mm; NaN when unreadable.</summary>
        internal static double DistanceMm(double[] a, double[] b)
        {
            if (!ReadableTransform(a) || !ReadableTransform(b)) return double.NaN;
            double dx = a[9] - b[9], dy = a[10] - b[10], dz = a[11] - b[11];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000.0;
        }

        private static bool ReadableTransform(double[] t)
        {
            if (t == null || t.Length < 12) return false;
            for (int i = 0; i < 12; i++)
                if (double.IsNaN(t[i]) || double.IsInfinity(t[i])) return false;
            return true;
        }

        /// <summary>The directions blocked after and not before, in the order of the test.</summary>
        internal static string[] NewlyBlocked(string[] before, string[] after)
        {
            var had = new HashSet<string>(before ?? new string[0], StringComparer.Ordinal);
            var has = new HashSet<string>(after ?? new string[0], StringComparer.Ordinal);
            return DirectionNames.Where(n => has.Contains(n) && !had.Contains(n)).ToArray();
        }

        /// <summary>
        /// THE REDUNDANT DECISION, from the two components' readings (status:
        /// «fixed», «measured» or anything else for unknown; the blocked names
        /// before and after the mate):
        ///  - a non-fixed component not measured both times, or one that reads a
        ///    direction free after the mate that was blocked before, makes it
        ///    «unknown»: never «redundant» from a reading that could not be made
        ///    or that contradicts itself;
        ///  - any direction newly blocked: «removes»;
        ///  - nothing removed, and neither component fixed nor fully blocked
        ///    before: «not_judged». The probe measured a pair with a fixed
        ///    component; with two free ones, moving one can drag the other
        ///    along, and a mate that ties them together could read as removing
        ///    nothing on either;
        ///  - nothing removed, and a measured component blocked in no direction
        ///    after the mate: «unknown». A real mate to a fixed or fully blocked
        ///    partner blocks at least one direction, so an empty reading says
        ///    the rebuilds did not bring anything back, not that the mate is
        ///    redundant;
        ///  - otherwise «redundant»: two fixed components included, which no
        ///    mate can move.
        /// </summary>
        internal static string MateVerdict(string statusA, string[] beforeA, string[] afterA,
                                           string statusB, string[] beforeB, string[] afterB)
        {
            var sides = new[] { Tuple.Create(statusA, beforeA, afterA), Tuple.Create(statusB, beforeB, afterB) };
            int removed = 0;
            bool grounded = false, emptyAfter = false;
            foreach (var side in sides)
            {
                if (side.Item1 == ReadingFixed) { grounded = true; continue; }
                if (side.Item1 != ReadingMeasured || side.Item2 == null || side.Item3 == null) return MateUnknown;
                if (side.Item2.Except(side.Item3, StringComparer.Ordinal).Any()) return MateUnknown;
                removed += NewlyBlocked(side.Item2, side.Item3).Length;
                if (DirectionNames.All(n => side.Item2.Contains(n))) grounded = true;
                if (side.Item3.Length == 0) emptyAfter = true;
            }
            if (removed > 0) return MateRemoves;
            if (!grounded) return MateNotJudged;
            if (emptyAfter) return MateUnknown;
            return MateRedundant;
        }

        /// <summary>
        /// The words of <see cref="RemoveTheMateJustMade"/>: «removed» only when
        /// DeleteSelection2 answered true AND both mate counts, read again, are
        /// back to what they were before the step. A count of -1 is «not read».
        /// </summary>
        internal static string MateRemovalNote(bool isFeature, string featureName, bool deleted,
                                               string uidA, string uidB, int beforeA, int beforeB, int nowA, int nowB)
        {
            string which = "the mate I had just made" + (string.IsNullOrEmpty(featureName) ? "" : " («" + featureName + "»)");
            if (!isFeature)
                return " I could not take out " + which + ": the object AddMate5 returned does not answer as a " +
                       "feature, and I do not look for a mate by its name. It is still in the assembly: remove the " +
                       "newest mate yourself.";
            if (!deleted)
                return " DeleteSelection2 did not take out " + which + ": it is still in the assembly, remove it yourself.";
            if (nowA < 0 || nowB < 0 || beforeA < 0 || beforeB < 0)
                return " DeleteSelection2 answered true for " + which + ", but I could not count the mates again to " +
                       "check that it is gone: look at the assembly before going on.";
            if (nowA == beforeA && nowB == beforeB)
                return " I took out " + which + ": " + uidA + " has " + nowA + (nowA == 1 ? " mate" : " mates") +
                       " and " + uidB + " has " + nowB + " again, as before.";
            return " DeleteSelection2 answered true for " + which + ", but " + uidA + " has " + nowA + " mates (" +
                   beforeA + " before) and " + uidB + " has " + nowB + " (" + beforeB + " before): look at the " +
                   "assembly before going on.";
        }

        // =================================================================

        // Concentric and tangent are no longer there (entry Z-C21.2, M17): with the faces
        // chosen by direction they cannot succeed, and the refusal sits in Mate.
        private static readonly string[] KnownTypes =
            { "coincident", "parallel", "perpendicular", "distance" };

        private static int MateType(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return -1;
            switch (itemName.Trim().ToLowerInvariant())
            {
                case "coincident": return (int)swMateType_e.swMateCOINCIDENT;
                case "parallel": return (int)swMateType_e.swMatePARALLEL;
                case "perpendicular": return (int)swMateType_e.swMatePERPENDICULAR;
                case "distance": return (int)swMateType_e.swMateDISTANCE;
                default: return -1;
            }
        }

        /// <summary>
        /// The error of `AddMate5` with its name.
        ///
        /// **`NoError` is worth 1 and `ErrorUknown` is worth 0**, which is the opposite of
        /// what anyone expects: here the names remove the ambiguity even
        /// for whoever reads the message.
        /// </summary>
        private static string MateErrorName(int e)
        {
            switch (e)
            {
                case (int)swAddMateError_e.swAddMateError_NoError: return "no error";
                case (int)swAddMateError_e.swAddMateError_IncorrectMateType:
                    return "mate type not suitable for these two faces";
                case (int)swAddMateError_e.swAddMateError_IncorrectAlignment:
                    return "wrong alignment: try «opposite»";
                case (int)swAddMateError_e.swAddMateError_IncorrectSelections:
                    return "the two faces are not right for this mate";
                case (int)swAddMateError_e.swAddMateError_OverDefinedAssembly:
                    return "the assembly would become over-defined: this constraint is already implied by the others";
                case (int)swAddMateError_e.swAddMateError_IncorrectGearRatios:
                    return "wrong gear ratios";
                default:
                    return "unknown error (" + e.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        private static IComponent2 PerUid(
            List<KeyValuePair<string, IComponent2>> components, string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return null;
            var u = uid.Trim();
            if (!u.StartsWith("C", StringComparison.OrdinalIgnoreCase)) return null;
            int n;
            if (!int.TryParse(u.Substring(1), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out n)) return null;
            if (n < 1 || n > components.Count) return null;
            return components[n - 1].Value;
        }

        private static IComponent2 ByName(
            List<KeyValuePair<string, IComponent2>> components, string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;
            foreach (var v in components)
                if (string.Equals(v.Key, itemName, StringComparison.Ordinal)) return v.Value;
            return null;
        }

        private static string NameOf(IComponent2 c)
        {
            try { return c == null ? null : c.Name2; } catch { return null; }
        }

        private static bool IsFixed(IComponent2 c)
        {
            try { return c != null && c.IsFixed(); } catch { return false; }
        }

        private static string NotFound(
            string uid, string theField, List<KeyValuePair<string, IComponent2>> components)
        {
            var exist = string.Join(", ",
                components.Select((v, i) => "C" + (i + 1) + " («" + v.Key + "»)"));
            return string.IsNullOrWhiteSpace(uid)
                ? "You did not tell me which component: «" + theField + "» wants an " +
                  "identifier from the state. There are: " + exist + "."
                : "I cannot find the component «" + uid + "». There are: " + exist + ".";
        }

        /// <summary>
        /// The face of a component that looks in a direction OF THE ASSEMBLY
        /// (finding M1, lot Z-C21.5). IComponent2.GetBodies3 hands the bodies
        /// in the space of the part (RAG 82zq: the lid at y 0..4 in the part,
        /// 5..9 in the assembly), so IFace2.Normal is a direction of the part
        /// file. Up to library 0.145.0 it was compared as it came, and on a
        /// turned component «up» took the face that looks up in the part
        /// file, wherever it looks in the assembly: the mate went on another
        /// face, with no error. Now each normal is turned into the assembly
        /// with the component's axes (<paramref name="axes"/>, Transform2
        /// applied to X, Y and Z with IMathVector.MultiplyTransform, see
        /// ComponentAxes): n0 X' + n1 Y' + n2 Z' is what MultiplyTransform
        /// gives for the vector n, read once per component instead of once
        /// per face. Transform2 is given in relation to the root component
        /// (IComponent2~Transform2.html, Remarks), so this holds for a
        /// component inside a sub-assembly too. No axes, no face: guessing
        /// the part's directions would be the old defect again.
        ///
        /// Among several faces with the same direction **the largest** wins: it is the
        /// same criterion the state uses for parts, and on a lid or
        /// a plate the large face is the one that is wanted. If there were
        /// two equally wide ones the criterion does not tell them apart — and then it is better
        /// to know it, because *a case in which two different values give the same
        /// result does not tabulate anything*.
        /// </summary>
        private static IFace2 FaceNormalOf(IComponent2 c, string facing, double[][] axes, out string why)
        {
            why = null;
            if (string.IsNullOrWhiteSpace(facing))
            {
                why = "you did not tell me which face. The directions are: " +
                         string.Join(", ", KnownFacings) + ".";
                return null;
            }
            var wanted = Direction(facing);
            if (wanted == null)
            {
                why = "«" + facing + "» is not a direction I know. There are: " +
                         string.Join(", ", KnownFacings) + ".";
                return null;
            }
            if (axes == null)
            {
                why = "I cannot read how the component is turned in the assembly (IComponent2.Transform2 " +
                         "did not answer), so I cannot tell which of its faces looks «" + facing + "» in the " +
                         "assembly. Nothing was changed.";
                return null;
            }

            object[] bodies = null;
            try { bodies = c.GetBodies3((int)swBodyType_e.swSolidBody, out _) as object[]; }
            catch { }
            if (bodies == null || bodies.Length == 0)
            {
                why = "the component has no solid bodies to take a face from.";
                return null;
            }

            IFace2 wins = null;
            double maxArea = -1;
            int howMany = 0;
            foreach (var o in bodies)
            {
                var b = o as IBody2;
                if (b == null) continue;
                var f = b.GetFirstFace() as IFace2;
                while (f != null)
                {
                    // S17: null, not 0,0,0, on a curved face. Part space in,
                    // assembly space out (M1).
                    var n = InAssemblySpace(ApiHelpers.NormalIfPlanar(f), axes);
                    if (n != null &&
                        n[0] * wanted[0] + n[1] * wanted[1] + n[2] * wanted[2] > 0.99)
                    {
                        howMany++;
                        double area = 0;
                        try { area = f.GetArea(); } catch { }
                        if (area > maxArea) { maxArea = area; wins = f; }
                    }
                    f = f.GetNextFace() as IFace2;
                }
            }

            if (wins == null)
            {
                why = "no flat face looks «" + facing + "» in the assembly. The directions are " +
                         "the assembly's: on a turned component a face is judged by where it looks " +
                         "after the component's rotation, not by where it looks in the part file.";
                return null;
            }
            return wins;
        }

        /// <summary>
        /// M1: a direction of the part file turned into the assembly, with
        /// the component's axes (X', Y', Z' as ComponentAxes reads them):
        /// v0 X' + v1 Y' + v2 Z', normalised. Null if an input is not three
        /// numbers, three vectors of three numbers, or the result has no
        /// length. Pure, so that the arithmetic can be checked without CAD.
        /// </summary>
        internal static double[] InAssemblySpace(double[] partVector, double[][] axes)
        {
            if (partVector == null || partVector.Length < 3 || axes == null || axes.Length < 3) return null;
            var turned = new double[3];
            for (int k = 0; k < 3; k++)
            {
                if (axes[k] == null || axes[k].Length < 3) return null;
                for (int i = 0; i < 3; i++) turned[i] += partVector[k] * axes[k][i];
            }
            double length = Math.Sqrt(turned[0] * turned[0] + turned[1] * turned[1] + turned[2] * turned[2]);
            if (length < 1e-12 || double.IsNaN(length)) return null;
            for (int i = 0; i < 3; i++) turned[i] /= length;
            return turned;
        }

        private static readonly string[] KnownFacings =
            { "up", "down", "right", "left", "front", "back" };

        private static double[] Direction(string facing)
        {
            switch (facing.Trim().ToLowerInvariant())
            {
                case "right": return new double[] { 1, 0, 0 };
                case "left": return new double[] { -1, 0, 0 };
                case "up": return new double[] { 0, 1, 0 };
                case "down": return new double[] { 0, -1, 0 };
                case "front": return new double[] { 0, 0, 1 };
                case "back": return new double[] { 0, 0, -1 };
                default: return null;
            }
        }
    }
}
