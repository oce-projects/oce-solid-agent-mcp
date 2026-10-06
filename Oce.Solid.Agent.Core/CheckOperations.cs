using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE OPERATIONS THAT DO NOT DRAW: they say how it turned out. (entry GP1)
    ///
    /// ── WHY THEY EXIST ───────────────────────────────────────────────────
    ///
    /// Up to here none of the operations of the catalog could answer the
    /// question «how did it turn out?». The measurements were there — `rectangular_cut`
    /// counts the material removed, `rectangular_frame` compares the bounding box
    /// with the promised one, `hole_wizard` measures where the axis ended up — but they were
    /// **private and internal to the single operations**: the model could not
    /// call any of them.
    ///
    /// `cut_list` is the precedent: a catalog operation that READS
    /// instead of drawing. This one does the same on a different question.
    ///
    /// ── THE QUESTION IT ANSWERS, AND WHY IT IS NOT OBVIOUS ───────────────
    ///
    /// «Can this body be printed?» In practice: **is it one solid only,
    /// closed, with no topological defects**. The four calls that
    /// SOLIDWORKS offers to find out do not say the same thing, and two of them
    /// keep quiet exactly on the case that counts.
    ///
    /// Measured on 15/9/2026 with `Checks\BrokenBody`, on SOLIDWORKS 2021:
    ///
    ///                             Check()  Check2()  Check3.Count  gaps
    ///   sound solid                  1        0           0           0
    ///   two solids that touch        1        0           0           0
    ///   SHEET BODY (open)            1        0           0           1
    ///
    /// Two things, and both change how this operation is written:
    ///
    /// 1. **`Check()` and `Check2()` have OPPOSITE conventions.** On the same
    ///    sound body the first says 1 and the second says 0. Whoever reads the zero of
    ///    `Check2()` as «false, that is, broken» gets a coherent,
    ///    stable and inverted result.
    ///
    /// 2. **An OPEN body passes three checks out of four.** The sheet is not
    ///    broken: it is a valid body that encloses nothing. `Check`, `Check2`
    ///    and `Check3` declare it sound, all three, and **only
    ///    `Diagnose().GetGapsCount()` sees it**. An operation built on
    ///    `Check3` — the way that entry GP1 named first — would say
    ///    «nessun problema» exactly on the case it exists for.
    ///
    /// So here **all four** are looked at, and the verdict requires them
    /// all to agree.
    ///
    /// ── SIGNATURES VERIFIED on the interop 29.5.1.1 ─────────────────────
    ///
    ///   IBody2.Check()    -> Int32
    ///   IBody2.Check2()   -> Int32
    ///   IBody2.Check3     -> FaultEntity      ** PROPERTY, NOT METHOD **
    ///   IBody2.Diagnose() -> DiagnoseResult
    ///   IFaultEntity.Count, get_ErrorCode(i)
    ///   IDiagnoseResult.GetGapsCount()
    ///
    /// `Check3` being a property and not a method is a late-binding trap:
    /// written `Check3()` it gives no compilation error, it gives a failure at
    /// runtime inside a `catch` that swallows it.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The value that `Check()` gives on a sound body. **One, not zero**, and
        /// the name exists because the bare constant in the middle of an `if` would be
        /// indistinguishable from a typo.
        /// </summary>
        private const int CheckSaysHealthy = 1;

        /// <summary>
        /// The value that `Check2()` gives on a sound body. **Zero, not one.**
        /// On the same body, with the other convention.
        /// </summary>
        private const int Check2SaysHealthy = 0;

        private Outcome CheckBodyGeometry(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("This document is not a part: it has no bodies to check.");

            // ALL THE TYPES, not only the solids. A part that by mistake has a
            // sheet body is exactly the case that this operation exists
            // to report, and asking only for the solids it would never be seen.
            //
            // THE «C» NUMBERS STILL MATCH THE STATE'S FOR THE SOLIDS (Z-C21.9,
            // finding L12-3, measured not real). The state numbers the bodies of
            // GetBodies2(swSolidBody); here they come from swAllBodies. The
            // Z-C21.3 probe of L12-3 (28/9/2026, SOLIDWORKS 29.5.1) built
            // four parts, solid and sheet bodies in the orders S F, F S, S F S and
            // F S F: after ForceRebuild3 swAllBodies gave the solids FIRST and in
            // the order of swSolidBody every time, and only the tail (the sheets)
            // is beyond the state's numbers. Before the rebuild the probe could
            // not read the bodies at all (RPC_E_SERVERFAULT on every body), which
            // is the «unstable order» its verdict names. This operation does not
            // rebuild before reading, and the probe measured the order only after
            // ForceRebuild3: on a part not rebuilt since its bodies were made, the
            // order is not measured.
            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swAllBodies, false) as object[];
            if (rawItems == null || rawItems.Length == 0)
                return Failed(
                    "The part has no body: there is nothing to check. First " +
                    "make an extrusion.");

            string thisOnly = Str(a, "body_uid", null);

            // THE BODIES OF THE PART, counted before the «body_uid» filter
            // (Z-C21.10, finding N61). IPartDoc~GetBodies2.html: «false gets
            // all of the bodies in the part». With one body asked for,
            // «bodies_total» is 1 on a part of two, and the verdict read «Un solo
            // corpo solido ... si puo' esportare»: a sentence about the PART,
            // which has two bodies and would give an STL of two shells.
            int bodiesInPart = rawItems.Count(o => o is IBody2);

            var list = new List<object>();
            int solids = 0, sheets = 0, others = 0;
            int withDefects = 0, withGaps = 0;
            var reasons = new List<string>();

            // WHICH BODIES (Z-C21.5, scheme S5). Every body of the part, each
            // labelled by its place in the list (the L12-3 note above is about
            // those labels); or the ONE body «body_uid» names, resolved through
            // the document state and its signature (BodyAskedFor: BodiesFromUid and
            // SignatureMatches), never as the body in that place of a GetBodies2
            // made now. A uid is an identity, and a cut that renumbers the
            // bodies must not move the check onto another body.
            var labels = new List<string>();
            var bodies = new List<IBody2>();
            if (!string.IsNullOrWhiteSpace(thisOnly))
            {
                IBody2 asked = BodyAskedFor(docState, a, thisOnly.Trim(), out string refusal);
                if (asked == null) return Failed(refusal);
                labels.Add(thisOnly.Trim().ToUpperInvariant());
                bodies.Add(asked);
            }
            else
            {
                for (int i = 0; i < rawItems.Length; i++)
                {
                    IBody2 inList = rawItems[i] as IBody2;
                    if (inList == null) continue;
                    labels.Add("C" + (i + 1));
                    bodies.Add(inList);
                }
            }

            for (int k = 0; k < bodies.Count; k++)
            {
                IBody2 body = bodies[k];
                string uid = labels[k];

                int typ = Integer(() => body.GetType(), -1);
                string whatItIsCalled = BodyTypeName(typ);
                if (typ == (int)swBodyType_e.swSolidBody) solids++;
                else if (typ == (int)swBodyType_e.swSheetBody) sheets++;
                else others++;

                int check = Integer(() => body.Check(), int.MinValue);
                int check2 = Integer(() => body.Check2(), int.MinValue);
                // null: Check3 could not be read (Z-C21.8, finding N16).
                var defects = BodyFaultNames(body);
                int gaps = Integer(() => Gaps(body), -1);

                if (defects != null && defects.Count > 0) withDefects++;
                if (gaps > 0) withGaps++;

                var entry = new Dictionary<string, object>
                {
                    ["body"] = uid,
                    ["kind"] = whatItIsCalled,
                    ["faces"] = Integer(() => body.GetFaceCount(), -1),
                    ["closed"] = gaps == 0,
                    ["gaps"] = gaps,
                    ["defects"] = defects == null ? null : (object)defects.Count,
                    // THE RAW NUMBERS STAY, and it is not redundancy: when one
                    // day a version of SOLIDWORKS changes one of the two
                    // conventions, it is from here that it will be seen instead of from a
                    // verdict turned the wrong way round.
                    ["check_raw"] = check,
                    ["check2_raw"] = check2,
                };
                if (defects != null && defects.Count > 0) entry["defects_list"] = defects.ToArray();
                if (defects == null) entry["defects_not_read"] = DefectsNotRead;
                list.Add(entry);

                if (typ != (int)swBodyType_e.swSolidBody)
                    reasons.Add("body " + uid + " is " + ABodyOf(whatItIsCalled) +
                               ", not a solid: it does not enclose a volume");
                if (gaps > 0)
                    reasons.Add("body " + uid + " has " + gaps +
                               (gaps == 1 ? " gap" : " gaps") + ": it is not closed");
                // NOT COUNTED IS NOT CLOSED (Z-C21.8, with N16): `Gaps` answers
                // -1 when Diagnose gives nothing, and the closed field was already
                // false, but no reason was added and the verdict could stay «can
                // be printed».
                if (gaps < 0)
                    reasons.Add("the gaps of body " + uid + " could not be counted (Diagnose gave no answer): " +
                               "not counted is not closed");
                if (defects == null)
                    reasons.Add("the defects of body " + uid + " could not be read: " + DefectsNotRead);
                else if (defects.Count > 0)
                    reasons.Add("body " + uid + " has " + defects.Count +
                               (defects.Count == 1 ? " defect: " : " defects: ") +
                               string.Join(", ", defects));
                if (check != CheckSaysHealthy)
                    reasons.Add("body " + uid + " has Check() = " + check +
                               " instead of " + CheckSaysHealthy);
                if (check2 != Check2SaysHealthy)
                    reasons.Add("body " + uid + " has Check2() = " + check2 +
                               " instead of " + Check2SaysHealthy);
            }

            if (list.Count == 0)
                return Failed(
                    "I cannot find the body «" + thisOnly + "». The identifiers of the " +
                    "bodies are in the state of the document, inside «bodies».");

            // MORE THAN ONE BODY IS NOT A DEFECT, and it is another thing from
            // «broken»: a weldment has ten by construction. But for a
            // print it is, because an STL of two bodies is two separate
            // shells, and the printer does not know which of the two is the part.
            bool justOne = list.Count == 1;
            if (!justOne && string.IsNullOrEmpty(thisOnly))
                reasons.Add("the part has " + list.Count + " bodies: for a print there must be only one");

            bool printable = reasons.Count == 0;

            // ONE BODY OF SEVERAL: THE VERDICT SPEAKS FOR THAT BODY (N61).
            // «printable» stays the verdict on the body asked for — the
            // question «is this body sound» — and `verdict_about` says which.
            // The sentence that said «si puo' esportare» of the part is
            // replaced by one that names the body and the bodies of the part;
            // the refusal already names the body in each of its reasons and
            // stays as it was. On a part of one body the verdict on the body
            // is the verdict on the part, and nothing changes.
            string verdictValue = printable
                ? "A single solid body, closed and with no defects: as geometry it can be exported."
                : "I would not export it like this: " + string.Join("; ", reasons) + ".";
            string verdictAbout = "part";
            if (!string.IsNullOrEmpty(thisOnly) && bodiesInPart > 1)
            {
                string which = Convert.ToString(((Dictionary<string, object>)list[0])["body"]);
                verdictAbout = "body " + which;
                if (printable)
                    verdictValue =
                        "Body " + which + " is a closed solid with no defects. This verdict is about " +
                        "body " + which + " only: the part has " + bodiesInPart + " bodies, and an STL " +
                        "of the part would be " + bodiesInPart + " separate shells. Ask again without " +
                        "«body_uid» for the verdict on the whole part.";
            }

            var measurements = new Dictionary<string, object>
            {
                ["bodies"] = list.ToArray(),
                ["bodies_total"] = list.Count,
                // Every body of the part, whatever «body_uid» asked, and what
                // «printable» and «verdict» speak for: «part» or «body C2» (N61).
                ["bodies_in_part"] = bodiesInPart,
                ["verdict_about"] = verdictAbout,
                ["solids"] = solids,
                ["sheets"] = sheets,
                ["other_bodies"] = others,
                ["bodies_with_defects"] = withDefects,
                ["bodies_not_closed"] = withGaps,
                ["printable"] = printable,
                // A SENTENCE, NOT A NUMBER. Whoever reads must know WHAT to do, and
                // «printable: false» alone does not say it.
                ["verdict"] = verdictValue,
                // THE DECLARED LIMIT, and it must be said every time: this
                // operation looks at the TOPOLOGY, not at the thickness of the walls
                // nor at the overhangs. A 0.3 mm shell passes here
                // without a word.
                ["what_it_does_not_check"] =
                    "Wall thickness and overhanging parts are not looked at by anyone yet: " +
                    "here you see whether the body is closed and sound, not whether it is printable " +
                    "on the machine you have.",
            };
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// The defects that `Check3` lists, with the NAME and not the number.
        ///
        /// The codes have an enum — `swFaultEntityErrorCode_e`, 36 values from
        /// `swBodyCorrupt = 1` to `swEdgeTouchEdge = 36` — and zero is NOT among
        /// them: a code that the enum does not know is said so, instead of
        /// being written as if it were a name.
        ///
        /// NULL WHEN CHECK3 CANNOT BE READ (Z-C21.8, finding N16). Until library
        /// 0.144.0 an exception, or a Check3 that gave no IFaultEntity, returned
        /// the EMPTY list, the same answer as a body with no fault: a body whose
        /// check could not be read was declared sound. The sound solid of
        /// Checks\BrokenBody (15/9/2026) answers an IFaultEntity with Count 0,
        /// not null, so null is «not read» and becomes a reason of the refusal.
        /// A fault list that breaks off half way is not read either.
        /// </summary>
        private static List<string> BodyFaultNames(IBody2 body)
        {
            var outside = new List<string>();
            try
            {
                var f = body.Check3 as IFaultEntity;
                if (f == null) return null;
                int n = f.Count;
                for (int k = 0; k < n; k++)
                {
                    int code = f.get_ErrorCode(k);
                    outside.Add(DefectName(code));
                }
            }
            catch { return null; }
            return outside;
        }

        /// <summary>The reason given when Check3 cannot be read (finding N16).</summary>
        private const string DefectsNotRead =
            "Check3 gave no fault list for this body, and a check that could not be read is not a sound body";

        private static string DefectName(int code)
        {
            try
            {
                if (Enum.IsDefined(typeof(swFaultEntityErrorCode_e), code))
                    return ((swFaultEntityErrorCode_e)code).ToString() + " (" + code + ")";
            }
            catch { }
            return "code " + code + ", which swFaultEntityErrorCode_e does not know";
        }

        private static int Gaps(IBody2 body)
        {
            var d = body.Diagnose() as IDiagnoseResult;
            return d == null ? -1 : d.GetGapsCount();
        }

        private static string BodyTypeName(int typ)
        {
            if (typ == (int)swBodyType_e.swSolidBody) return "solid";
            if (typ == (int)swBodyType_e.swSheetBody) return "sheet";
            if (typ == (int)swBodyType_e.swWireBody) return "wire";
            if (typ == (int)swBodyType_e.swEmptyBody) return "empty";
            return "type " + typ;
        }

        private static string ABodyOf(string itemName)
        {
            return itemName == "sheet" ? "a surface" :
                   itemName == "wire" ? "a wire" :
                   itemName == "empty" ? "an empty body" : "a body «" + itemName + "»";
        }

        /// <summary>
        /// An integer from a COM call, or the fallback value. The failure is not
        /// swallowed: the fallback is a number that can NOT be
        /// mistaken for «sound», and this is why it is `int.MinValue` and not
        /// zero.
        /// </summary>
        private static int Integer(Func<int> f, int ifItCannot)
        {
            try { return f(); }
            catch { return ifItCannot; }
        }
    }
}
