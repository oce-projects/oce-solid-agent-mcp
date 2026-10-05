using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// REMOVING A COPY FROM A PATTERN. (entry GP14)
    ///
    /// A grid of holes that skips one where a rib passes, a row
    /// of slots that stops before the bend: in the workshop it is normal. In the
    /// catalogue it was missing, and it could not be done any other way — a
    /// pattern is either regular or it is not.
    ///
    /// ── THERE IS NO METHOD: AN ARRAY IS WRITTEN ───────────────────────
    ///
    /// There is no «SkipInstance». The data of the feature is taken, the
    /// `SkippedItemArray` is written, and it is put back:
    ///
    ///     var dati = ripetizione.GetDefinition();
    ///     dati.AccessSelections(modello, null);
    ///     dati.SkippedItemArray = new int[] { 2 };
    ///     ripetizione.ModifyDefinition(dati, modello, null);
    ///
    /// It is a write AFTER creation, not a parameter: so the
    /// pattern is made first, with the operations that already exist, and then
    /// what must not be there is removed.
    ///
    /// ── THE INDICES: MEASURED, NOT DEDUCED ────────────────────────────
    ///
    /// Plate 200x100, hole at x=-50, pattern of 4 with pitch 25 towards -X.
    /// The positions are -50 (the seed), -75, -100, -125.
    ///
    ///     SkippedItemArray = { 2 }   ->  the one at -100 disappears
    ///
    /// That is **index 2 is the SECOND COPY**: the seed does not count as a
    /// copy, and the count starts at 1. It is also the way whoever asks
    /// would count («remove the second one»), so the number that arrives
    /// from outside is passed as it is.
    ///
    /// ── THE DEFECT: A COPY THAT IS NOT THERE CAN BE REMOVED ───────────
    ///
    /// And this is the point. In the same test, the FOURTH position (-125)
    /// fell outside the plate: SOLIDWORKS had built three copies out of
    /// four and had answered «succeeded» (it is note 80y, and it is still true).
    ///
    /// Asking to skip a copy that never existed **succeeds**:
    /// `ModifyDefinition` answers `true`, the array is written, and in the part
    /// nothing changes. Whoever reads the answer believes they removed a hole that
    /// was not there.
    ///
    /// Therefore here the FACES of the pattern are counted before and after, and if
    /// they have not dropped it refuses, putting the array back as it was. It is the same
    /// count as `MissingCopies`: the faces the pattern owns divided
    /// by those of the repeated feature are the copies that really
    /// exist.
    ///
    /// ── AND `ModifyDefinition` ANSWERS `true` TO THINGS IT DOES NOT DO ──
    ///
    /// It is not a suspicion: of three ways of emptying the list, **two answer
    /// `true` without changing anything** — and one of the two even raises
    /// a COM exception before answering yes. The detail is in
    /// `ToWrite`, and it is the reason why here the return value is
    /// never the criterion.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome SuppressInstance(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string reason;
            var repetition = FeatureToTouch(model, a, out reason);
            if (repetition == null) return Failed(reason);
            string uid = Str(a, "feature_uid", "");

            string typ = "";
            try { typ = repetition.GetTypeName2(); } catch { }

            var copies = Integers(a, "copies");
            if (copies.Count == 0)
                return Failed(
                    "Manca «copies»: quali copie togliere, per numero. La prima copia " +
                    "dopo l'originale e' la 1.");
            if (copies.Any(c => c < 1))
                // ZERO IS NOT A COPY, AND IT IS WORSE THAN AN OUT-OF-RANGE
                // NUMBER: for SOLIDWORKS `{0}` means «no copy
                // removed», so passed over there as it is it WOULD ERASE
                // all the copies removed before, silently and answering that it
                // went well. See `ToWrite`.
                return Failed(
                    "Le copie si contano da 1, e lo ZERO non e' una copia: e' " +
                    "l'originale, quello da cui la ripetizione e' nata. E dall'altra " +
                    "parte lo zero vuol dire «rimettile tutte», per cui passarlo qui " +
                    "cancellerebbe le copie tolte prima invece di toglierne una. " +
                    "L'originale non si toglie da qui — si sopprime la lavorazione, " +
                    "oppure si cancella la ripetizione.");

            bool reactivate = Bool(a, "reactivate", false);

            var data = ReadDefinition(repetition, out ISkipInstances port, out string why);
            if (data == null)
                return Failed(
                    "I do not remove copies from «" + uid + "»" +
                    (string.IsNullOrEmpty(typ) ? "" : " (type " + typ + ")") + ": " + why);

            // ── A GRID IS NOT COUNTED LIKE A ROW (entry Z-C21.2, M6) ──
            //
            // ILinearPatternFeatureData~SkippedItemArray.html, Remarks: «For a
            // bi-directional pattern: I = n2 * ( i - 1) + ( j - 1)», with n2 the
            // instances in direction 2 and [i, j] the indices in the two directions.
            // Here «copy 2» was written as it was: on a 3 x 2 grid it removed
            // the instance [2, 1], not the second copy of any row, and
            // answered succeeded. A single number does not say a position on a
            // grid: it refuses instead of guessing.
            // (IsDirection2Specified.html: «True if direction 2 is specified».)
            // It holds also for the circular and the curve-driven one, which may
            // have a second direction: see their `Grid` below.
            string grid = port.Grid(data);
            if (grid != null)
                return Failed(
                    "«" + uid + "» e' una griglia a due direzioni (" + grid + "): un numero di copia " +
                    "solo non dice quale posizione togliere, perche' SOLIDWORKS le numera riga per " +
                    "riga. Non indovino. Se ti serve togliere una posizione, fai la ripetizione con " +
                    "una direzione sola e togli la copia da quella fila. Non ho toccato niente.");

            // THE SEEDS, to know how many faces a copy is worth (finding L6-5).
            // Read before AccessSelections; if they cannot be read, the check
            // below is not done instead of inventing a number.
            int seedFaces = 0;
            try
            {
                var semi = port.Semi(data) as object[];
                if (semi != null)
                    foreach (var s in semi)
                    {
                        int n = FeatureFaces(s as IFeature);
                        if (n < 0) { seedFaces = 0; break; }
                        seedFaces += n;
                    }
            }
            catch { seedFaces = 0; }

            int[] alreadySkipped;
            try { alreadySkipped = port.Read(data) ?? new int[0]; }
            catch (Exception ex) { return Failed("Non riesco a leggere le copie gia' tolte: " + ex.Message); }

            var newOnes = reactivate
                ? alreadySkipped.Where(x => !copies.Contains(x)).ToArray()
                : alreadySkipped.Union(copies).OrderBy(x => x).ToArray();

            if (newOnes.Length == alreadySkipped.Length)
                // ALREADY AS ASKED. It is not an error but it is not «done»:
                // same rule as `suppress_feature`.
                return Failed(
                    "Le copie " + Items(copies) + " di «" + uid + "» sono gia' " +
                    (reactivate ? "attive" : "tolte") + ": non ho cambiato niente. " +
                    "Quelle tolte adesso sono " +
                    (alreadySkipped.Length == 0 ? "nessuna" : Items(alreadySkipped)) + ".");

            int facesBefore = FeatureFaces(repetition);
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);

            bool done;
            try
            {
                // `AccessSelections` before touching the data, and in case of
                // trouble `ReleaseSelectionAccess`: without it, the feature stays
                // locked and the operations after fail for a reason that
                // has nothing to do with this one any more.
                //
                // THE ACCESS IS READ (Z-C21.8, the rule of L5-5): each family's
                // AccessSelections.html, «True if the selections are
                // successfully accessed, false if not». A refused access stops
                // before anything is written, with nothing to release.
                if (!port.Open(data, model))
                    return Failed("SOLIDWORKS did not give access to the definition of «" + uid +
                                   "» (AccessSelections answered false): nothing was written, the pattern is as it was.");
                port.Write(data, ToWrite(newOnes));
                done = ApiHelpers.ModifyAndVerify(repetition, data, model).Applied;
                if (!done) port.Close(data);
            }
            catch (Exception ex)
            {
                try { port.Close(data); } catch { }
                return Failed("SOLIDWORKS non ha accettato la modifica: " + ex.Message);
            }
            if (!done)
                return Failed(
                    "SOLIDWORKS ha rifiutato di togliere le copie " + Items(copies) +
                    " da «" + uid + "», e non dice perche'.");

            int facesAfter = FeatureFaces(repetition);
            double? volumeAfter = VolumeMm3(model);
            int howMany = Math.Abs(newOnes.Length - alreadySkipped.Length);

            // ── IN ERROR AFTER THE MODIFICATION? (entry Z-C21.2, finding L6-5) ────
            //
            // IFeature~ModifyDefinition.html, Remarks: «this method may leave
            // the feature in an unpredictable state due to errors. After calling
            // this method, call IFeature::GetErrorCode2 to ascertain whether the
            // feature has rebuild errors». Only the bool was looked at, which here —
            // the comment at the top already says so — is never the criterion.
            // Read again here, after faces and volume, where it was read before
            // the helper existed (ApiHelpers.RebuildError, same rule).
            string repetitionInError = ApiHelpers.RebuildError(repetition);
            if (repetitionInError != null)
            {
                string restored = PutBack(model, repetition, port, alreadySkipped);
                return Failed("Dopo aver tolto le copie " + Items(copies) + " «" + uid + "» e' in errore (" +
                               repetitionInError + "). " + restored);
            }

            // ── A PATTERN LEFT WITHOUT FACES HAS NOT «REMOVED A COPY» ──
            //
            // (finding L6-5.) The check below asked only that the faces
            // dropped: a pattern gone into error that loses ALL of them
            // (GetFaces null gives 0) passed it. And the loss must be worth the
            // faces of one copy times the copies removed: the same arithmetic as
            // MissingCopies (SwOperations.cs), which measures the copies that really
            // exist by dividing the faces of the pattern by those of the seed.
            if (!reactivate && facesBefore > 0 && facesAfter == 0)
            {
                string restored = PutBack(model, repetition, port, alreadySkipped);
                return Failed("Dopo aver tolto le copie " + Items(copies) + " «" + uid + "» non ha piu' " +
                               "nessuna faccia: e' sparita tutta la ripetizione, non una copia. " + restored);
            }
            if (seedFaces > 0 && facesBefore >= 0 && facesAfter >= 0 &&
                Math.Abs(facesAfter - facesBefore) != 0 &&
                Math.Abs(facesAfter - facesBefore) != seedFaces * howMany)
            {
                string restored = PutBack(model, repetition, port, alreadySkipped);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The faces of the pattern went from {0} to {1}, but {2} {3} of «{4}» are " +
                    "worth {5} ({6} per copy): {7} Look at «copies_done» on this pattern. {8}",
                    facesBefore, facesAfter, howMany, howMany == 1 ? "copy" : "copies", uid,
                    seedFaces * howMany, seedFaces,
                    reactivate ? "some restored copy did not come back." : "some removed copy was not there.",
                    restored));
            }

            // ── AND HERE WE LOOK AT WHETHER ANYTHING HAPPENED ────────────
            //
            // `ModifyDefinition` answers `true` even when asked to
            // remove a copy that never existed -- because it fell
            // outside the material, and SOLIDWORKS builds those and says nothing
            // (note 80y). The array is written, the part does not change,
            // and whoever reads believes they removed a hole.
            bool wentInTheRightDirection = reactivate
                ? facesAfter > facesBefore
                : facesAfter < facesBefore;
            if (facesBefore > 0 && facesAfter >= 0 && !wentInTheRightDirection)
            {
                string restored = PutBack(model, repetition, port, alreadySkipped);
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "I wrote the change and SOLIDWORKS said yes, but the part did " +
                    "not change: the faces of the pattern were {0} and are {1}. Almost " +
                    "always it means that, for {2}, {3}: a pattern builds the copies " +
                    "that lie in the material and answers «succeeded» all the same, so the " +
                    "copy {4} may never have been there. Look at «copies_done» on this " +
                    "pattern before removing. {5}",
                    facesBefore, facesAfter,
                    copies.Count == 1 ? "that copy" : "those copies",
                    reactivate ? "it was not missing" : "it was not there",
                    Items(copies),
                    restored));
            }

            int bodiesAfter = CountSolidBodies(model);
            var measurements = new Dictionary<string, object>
            {
                ["copies_removed_now"] = reactivate ? new int[0] : copies.ToArray(),
                ["copies_restored_now"] = reactivate ? copies.ToArray() : new int[0],
                ["copies_removed_total"] = newOnes,
                ["pattern_faces"] = NumberOrNull(facesAfter),
                ["faces_before"] = NumberOrNull(facesBefore),
                // HOW MANY FACES A COPY IS WORTH, or null if the seeds were not read
                // (review of Z-C21.2): with null the check «loss = faces
                // of the seed x copies removed» above was NOT done, and whoever reads
                // must be able to see it. New name, in English.
                ["faces_per_copy"] = seedFaces > 0 ? (object)seedFaces : null,
                ["bodies"] = NumberOrNull(bodiesAfter),
                [reactivate ? "material_removed_mm3" : "material_returned_mm3"] =
                    NumberOrNull(volumeAfter.HasValue && volumeBefore.HasValue
                        ? Math.Abs(volumeAfter.Value - volumeBefore.Value) : (double?)null, 2),
            };
            // «Broken» is said of two counts READ (entry Z-C8): before, the
            // comparison was between an int and an object, and with a failed read
            // the zero said the part had broken.
            if (bodiesBefore >= 0 && bodiesAfter >= 0 && bodiesBefore != bodiesAfter)
                measurements["warning_bodies"] =
                    "I corpi sono passati da " + bodiesBefore + " a " + bodiesAfter +
                    ": togliere una copia ha spezzato il pezzo.";

            _log("[agente] «" + uid + "»: copie " + (reactivate ? "rimesse " : "tolte ") +
                 Items(copies) + ", facce " + facesBefore + " -> " + facesAfter);

            return Succeeded(repetition, measurements);
        }

        // =================================================================

        /// <summary>
        /// Puts the array back as it was. Returns the sentence to append to the refusal:
        /// whoever reads must know whether the part was left in order or not.
        /// </summary>
        private static string PutBack(
            IModelDoc2 model, IFeature repetition, ISkipInstances port, int[] comEra)
        {
            try
            {
                var d = repetition.GetDefinition();
                if (d == null) return "Non sono riuscito a rimettere le cose com'erano.";
                // The access is read (Z-C21.8, rule of L5-5): refused, nothing
                // can be written back.
                if (!port.Open(d, model))
                    return "I could not put the skipped copies back: AccessSelections answered false. Check the " +
                           "pattern, and undo the step (" + Sentences.HowToWithdrawIt + ").";
                port.Write(d, ToWrite(comEra));
                // S11: Applied alone is not the verdict (IFeature~ModifyDefinition.html,
                // Remarks: it can return true and leave a new rebuild error). The
                // sentence "put back as it was" is said only when the feature is
                // known to be sound.
                var change = ApiHelpers.ModifyAndVerify(repetition, d, model);
                if (change.Applied && change.InError)
                    return "I put the pattern definition back, but the pattern is now in a rebuild error (" +
                           change.ErrorPhrase + "): check the pattern.";
                if (change.Applied && !change.ErrorCodeKnown)
                    return "I put the pattern definition back, but I could not read whether it rebuilds " +
                           "without errors: check the pattern.";
                if (change.Applied)
                {
                    // «Put back» after a reread (Z-C21.8, finding M42's rule): the
                    // skipped copies read from a fresh definition must be the ones
                    // read before the change.
                    int[] now = null;
                    try { now = port.Read(repetition.GetDefinition()); } catch { now = null; }
                    if (now != null && now.OrderBy(x => x).SequenceEqual((comEra ?? new int[0]).OrderBy(x => x)))
                        return "Ho rimesso la ripetizione com'era.";
                    return "I put the pattern definition back, but reading it back the skipped copies are " +
                           (now == null ? "not readable" : "[" + Items(now) + "]") + " instead of [" +
                           Items(comEra ?? new int[0]) + "]: check the pattern.";
                }
                port.Close(d);
            }
            catch { }
            return "Non sono riuscito a rimettere le cose com'erano: guarda la ripetizione.";
        }

        /// <summary>
        /// How the skipped copies of ONE family of patterns are read and
        /// written.
        ///
        /// It is needed because `SkippedItemArray` exists on six different interfaces and
        /// **there is no common interface**. It is not a supposition: with
        /// `IFeatureData` written as the type of the parameter, the compiler answers
        /// «the type name could not be found». `ILinearPatternFeatureData` and
        /// `ICircularPatternFeatureData` have nothing in between — not even
        /// `AccessSelections`, which both of them have with the same signature.
        ///
        /// So the data travels as `object` and every family knows how to do its own
        /// four things. They are tried one at a time, and the one that answers wins.
        /// </summary>
        private interface ISkipInstances
        {
            int[] Read(object data);
            void Write(object data, int[] copies);
            // The answer of AccessSelections (Z-C21.8, rule of L5-5).
            bool Open(object data, IModelDoc2 model);
            void Close(object data);
            // Entry Z-C21.2: the repeated features (PatternFeatureArray), and
            // whether the pattern is a two-direction grid (null if not).
            object Semi(object data);
            string Grid(object data);
        }

        private sealed class SkipLinear : ISkipInstances
        {
            public int[] Read(object d) { return Numbers(((ILinearPatternFeatureData)d).SkippedItemArray); }
            public void Write(object d, int[] c) { ((ILinearPatternFeatureData)d).SkippedItemArray = c; }
            public bool Open(object d, IModelDoc2 m) { return ((ILinearPatternFeatureData)d).AccessSelections(m, null); }
            public void Close(object d) { ((ILinearPatternFeatureData)d).ReleaseSelectionAccess(); }
            public object Semi(object d) { return ((ILinearPatternFeatureData)d).PatternFeatureArray; }
            public string Grid(object d)
            {
                var l = (ILinearPatternFeatureData)d;
                bool due = false; int n1 = 0, n2 = 0;
                try { due = l.IsDirection2Specified(); n1 = l.D1TotalInstances; n2 = l.D2TotalInstances; }
                catch { return null; }
                return due && n2 > 1 ? n1 + " x " + n2 : null;
            }
        }

        private sealed class SkipCircular : ISkipInstances
        {
            public int[] Read(object d) { return Numbers(((ICircularPatternFeatureData)d).SkippedItemArray); }
            public void Write(object d, int[] c) { ((ICircularPatternFeatureData)d).SkippedItemArray = c; }
            public bool Open(object d, IModelDoc2 m) { return ((ICircularPatternFeatureData)d).AccessSelections(m, null); }
            public void Close(object d) { ((ICircularPatternFeatureData)d).ReleaseSelectionAccess(); }
            public object Semi(object d) { return ((ICircularPatternFeatureData)d).PatternFeatureArray; }
            // THE CIRCULAR ONE CAN HAVE TWO DIRECTIONS TOO (review of Z-C21.2,
            // M6 corrected halfway). ICircularPatternFeatureData~SkippedItemArray.html
            // carries the same formula as the linear one: «For a bi-directional
            // pattern: I = n2 * ( i - 1) + ( j - 1)». Direction2.html: «True to
            // create a bidirectional circular pattern feature»; TotalInstances2.html:
            // «only available when Direction2 is true, and Symmetric is false» —
            // with Symmetric the second direction repeats the number of the first
            // (Symmetric.html: «symmetric or asymmetric circular pattern feature
            // in Direction 2»).
            public string Grid(object d)
            {
                var c = (ICircularPatternFeatureData)d;
                bool due = false, symmetric = false; int n1 = 0, n2 = 0;
                try
                {
                    due = c.Direction2;
                    if (!due) return null;
                    n1 = c.TotalInstances;
                    symmetric = c.Symmetric;
                    n2 = symmetric ? n1 : c.TotalInstances2;
                }
                catch { return null; }
                return n2 > 1 ? n1 + " x " + n2 + (symmetric ? ", simmetrica" : "") : null;
            }
        }

        // AND OF THE NINE PATTERN FAMILIES, THREE DO NOT HAVE THE ARRAY.
        //
        // Measured by reflection on the interop, not supposed:
        //
        //   ILinearPatternFeatureData        SkippedItemArray  yes
        //   ICircularPatternFeatureData      yes
        //   ICurveDrivenPatternFeatureData   yes
        //   IFillPatternFeatureData          yes
        //   ITablePatternFeatureData         yes
        //   IDerivedPatternFeatureData       yes   (an assembly pattern: never here, C7-3)
        //   ISketchPatternFeatureData        NO
        //   IChainPatternFeatureData         NO
        //   IDimPatternFeatureData           NO
        //
        // All nine have `AccessSelections`, which is the reason why
        // one would be tempted to believe they are interchangeable. They are not, and
        // writing it made the compilation fail — which is the best way
        // to find out: a forgotten reflection would have become a
        // «SkippedItemArray does not exist» at runtime, on somebody's part.

        private sealed class SkipCurve : ISkipInstances
        {
            public int[] Read(object d) { return Numbers(((ICurveDrivenPatternFeatureData)d).SkippedItemArray); }
            public void Write(object d, int[] c) { ((ICurveDrivenPatternFeatureData)d).SkippedItemArray = c; }
            public bool Open(object d, IModelDoc2 m) { return ((ICurveDrivenPatternFeatureData)d).AccessSelections(m, null); }
            public void Close(object d) { ((ICurveDrivenPatternFeatureData)d).ReleaseSelectionAccess(); }
            public object Semi(object d) { return ((ICurveDrivenPatternFeatureData)d).PatternFeatureArray; }
            // AND THE CURVE-DRIVEN ONE (review of Z-C21.2): Dir2Specified.html,
            // «whether Direction 2 is specified for this curve-driven pattern
            // feature»; D2InstanceCount.html, «the number of instances in
            // Direction 2 of this bidirectional curve-driven pattern feature».
            public string Grid(object d)
            {
                var c = (ICurveDrivenPatternFeatureData)d;
                bool due = false; int n1 = 0, n2 = 0;
                try { due = c.Dir2Specified; n1 = c.D1InstanceCount; n2 = c.D2InstanceCount; }
                catch { return null; }
                return due && n2 > 1 ? n1 + " x " + n2 : null;
            }
        }

        private static object ReadDefinition(
            IFeature repetition, out ISkipInstances port, out string why)
        {
            port = null;
            why = null;
            object raw;
            try { raw = repetition.GetDefinition(); }
            catch (Exception ex) { why = "I cannot read its definition: " + ex.Message; return null; }
            if (raw == null)
            {
                why = "SOLIDWORKS gives no definition for it.";
                return null;
            }

            if (raw is ILinearPatternFeatureData) port = new SkipLinear();
            else if (raw is ICircularPatternFeatureData) port = new SkipCircular();
            else if (raw is ICurveDrivenPatternFeatureData) port = new SkipCurve();

            if (port == null) why = NotANumberedPattern(raw is IFillPatternFeatureData, raw is ITablePatternFeatureData);
            return port == null ? null : raw;
        }

        /// <summary>
        /// Why the copies of this pattern are not removed here, by family
        /// (Z-C21.8, finding C7-3). The refusal stays right; what it said was
        /// wrong. «Not a pattern with numbered copies» was said of every family
        /// but linear, circular and curve-driven, and a FILL pattern and a
        /// TABLE pattern do skip instances: IFillPatternFeatureData~SkippedItemArray.html,
        /// «contains the (row_id, column_id) of each pattern instance to skip»,
        /// and ITablePatternFeatureData~SkippedItemArray.html, «the skipped items
        /// for this table-driven pattern feature». They are not numbered as one
        /// row of copies, and a copy number is not translated into their
        /// positions here. The derived pattern also has the array, but it is an
        /// assembly pattern and suppress_instance runs on parts (RequestedDocument
        /// «part»), so it is not named. Kept apart from the document so that
        /// Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// </summary>
        internal static string NotANumberedPattern(bool fill, bool table)
        {
            const string instead =
                " Copies are removed here from «linear_pattern» and «circular_pattern» patterns " +
                "(and curve-driven ones), whose copies are counted 1, 2, 3 along one row.";
            if (fill)
                return "it is a FILL pattern: SOLIDWORKS skips its instances by (row, column), not by one copy " +
                       "number, and I do not translate a copy number into a row and a column." + instead +
                       " Remove the instance of the fill pattern by hand.";
            if (table)
                return "it is a TABLE-driven pattern: its instances are the rows of its table, not copies " +
                       "counted along one row." + instead + " Remove the row of the table by hand.";
            return "it is not a pattern with numbered copies: patterns driven by a sketch, a chain or a " +
                   "dimension keep no list of skipped instances, and a mirror has one copy only, removed by " +
                   "deleting it." + instead;
        }

        /// <summary>
        /// The array as SOLIDWORKS wants to receive it.
        ///
        /// ── EMPTYING THE LIST IS WRITTEN `{0}`, NOT `{}` ─────────────────
        ///
        /// Measured on 16/9/2026, three ways on three new documents, after having
        /// removed copy 2 from a row of four:
        ///
        ///   SkippedItemArray = new int[0]   ModifyDefinition -> TRUE
        ///                                   faces 2 (were 2)    reread {2}
        ///                                   -> IGNORED SILENTLY
        ///   SkippedItemArray = null         the WRITE raises
        ///                                   RPC_E_SERVERFAULT (0x80010105),
        ///                                   then ModifyDefinition -> TRUE
        ///                                   and nothing changed
        ///   SkippedItemArray = new int[]{0} ModifyDefinition -> TRUE
        ///                                   faces 2 -> 3, volume back
        ///                                   reread: int[0] {}
        ///
        /// **Zero is the way of saying «none», not an index.** And it is written
        /// one way and read back another: `{0}` is sent, `{}` comes back.
        ///
        /// It is also the real reason why `copies: [0]` is refused from
        /// outside. Not because «zero is the original» — because passed over there
        /// as it is it **would erase all the copies removed before**, silently,
        /// answering that it went well.
        ///
        /// Of the three answers, two are `true` on something that did not
        /// happen: the return value here is worth nothing, and the only proof
        /// is the count of the faces.
        /// </summary>
        private static int[] ToWrite(int[] copies)
        {
            return copies == null || copies.Length == 0 ? new[] { 0 } : copies;
        }

        private static int[] Numbers(object raw)
        {
            if (raw == null) return new int[0];
            var i = raw as int[];
            if (i != null) return i;
            var o = raw as System.Collections.IEnumerable;
            if (o == null) return new int[0];
            var outside = new List<int>();
            foreach (var v in o)
            {
                try { outside.Add(Convert.ToInt32(v, CultureInfo.InvariantCulture)); }
                catch { }
            }
            return outside.ToArray();
        }

        /// <summary>A list of integers from an argument, which can also be a single number.</summary>
        private static List<int> Integers(Dictionary<string, object> a, string key)
        {
            var outside = new List<int>();
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return outside;
            if (v is string)
            {
                int one;
                if (int.TryParse((string)v, NumberStyles.Any, CultureInfo.InvariantCulture, out one))
                    outside.Add(one);
                return outside;
            }
            var list = v as System.Collections.IEnumerable;
            if (list == null)
            {
                try { outside.Add(Convert.ToInt32(v, CultureInfo.InvariantCulture)); }
                catch { }
                return outside;
            }
            foreach (var x in list)
            {
                try { outside.Add(Convert.ToInt32(x, CultureInfo.InvariantCulture)); }
                catch { }
            }
            return outside;
        }

        private static string Items(IEnumerable<int> numbers)
        {
            var v = numbers.ToArray();
            if (v.Length == 0) return "none";
            if (v.Length == 1) return v[0].ToString(CultureInfo.InvariantCulture);
            return string.Join(", ", v.Take(v.Length - 1).Select(x => x.ToString(CultureInfo.InvariantCulture))) +
                   " e " + v[v.Length - 1].ToString(CultureInfo.InvariantCulture);
        }
    }
}
