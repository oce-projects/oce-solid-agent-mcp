using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// REORDERING THE TREE (entry Z-F3): a shell put before a hole gives
    /// a different part from one put after, and noticing it and moving the entry is a
    /// weekly gesture.
    ///
    /// Measured on 27/9/2026 (`prova0089 zf3`, 100x60x40 box, Ø10 through
    /// hole, shell 3 opening the top): `IModelDocExtension.ReorderFeature
    /// (daSpostare, rispettoA, swMoveBefore=2 / swMoveAfter=3)` answers true and
    /// the volume changes — hole then shell 56485.7 mm³, shell before the hole
    /// 51952.38 — and put back as it was it returns 56485.7. An impossible move
    /// (the hole before the extrusion it sits on) answers FALSE and touches
    /// nothing; a name that is not there answers false.
    ///
    /// FEATURES ARE INDICATED BY NAME, as in the whole bridge, and the name is
    /// checked with the `feature_id` that the state gave (a name can be renamed, the
    /// ID cannot): `FeatureIsThatOne`, the same defense as `suppress_feature`.
    /// After the move it rebuilds and counts the features in error:
    /// if they increased, the feature goes back where it was and it refuses.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>The names of the features in tree order (after the origin).</summary>
        private static List<string> FeatureOrder(IModelDoc2 model)
        {
            var outside = new List<string>();
            try
            {
                var f = model.FirstFeature() as IFeature;
                bool afterOrigin = false;
                while (f != null)
                {
                    string t = "";
                    try { t = f.GetTypeName2(); } catch { }
                    if (t == "OriginProfileFeature") afterOrigin = true;
                    else if (afterOrigin) { try { outside.Add(f.Name); } catch { } }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>How many features are in error (not just warning); -1 if it cannot be read.</summary>
        private static int FeaturesInError(IModelDoc2 model)
        {
            try
            {
                int n = 0;
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    // S11: ApiHelpers.RebuildError; a code that cannot be read
                    // makes the count unknown (-1), as the exception did before.
                    bool known;
                    if (ApiHelpers.RebuildError(f, out known) != null) n++;
                    if (!known) return -1;
                    f = f.GetNextFeature() as IFeature;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// What differs between the tree read before a move and the tree read
        /// now, in words, or null when it is the same (Z-C21.8, finding M42): the
        /// order of the features and the volume. A volume that cannot be read,
        /// before or now, is not «the same»: the sentence that says nothing
        /// changed needs both. Kept apart from the document so that
        /// Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// </summary>
        internal static string TreeReadBackFault(
            IList<string> orderBefore, IList<string> orderNow, double? volumeBefore, double? volumeNow)
        {
            var off = new List<string>();
            bool sameOrder = orderBefore != null && orderNow != null && orderBefore.Count == orderNow.Count;
            for (int i = 0; sameOrder && i < orderBefore.Count; i++)
                if (!string.Equals(orderBefore[i], orderNow[i], StringComparison.Ordinal)) sameOrder = false;
            if (!sameOrder)
                off.Add("the order was «" + (orderBefore == null ? "not read" : string.Join(", ", orderBefore)) +
                        "» and is «" + (orderNow == null ? "not read" : string.Join(", ", orderNow)) + "»");
            if (volumeBefore == null || volumeNow == null)
                off.Add("the volume could not be read " + (volumeBefore == null ? "before" : "now"));
            else if (Math.Abs(volumeBefore.GetValueOrDefault() - volumeNow.GetValueOrDefault()) >= 0.01)
                off.Add("the volume was " + volumeBefore.GetValueOrDefault().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                        " mm3 and is " + volumeNow.GetValueOrDefault().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            return off.Count == 0 ? null : string.Join("; ", off);
        }

        private Outcome ReorderFeature(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("Riordino l'albero di un pezzo. Aprimi il pezzo.");
            string uid = (Str(a, "feature_uid", "") ?? "").Trim();
            string comparedTo = (Str(a, "relative_to_uid", "") ?? "").Trim();
            string location = (Str(a, "placement", "") ?? "").Trim().ToLowerInvariant();
            if (uid.Length == 0 || comparedTo.Length == 0)
                return Failed("Dimmi «feature_uid» (la lavorazione da spostare) e «relative_to_uid» (quella accanto a " +
                               "cui metterla): i nomi stanno nello stato, dentro «feature».");
            if (location != "before" && location != "after")
                return Failed("«placement» e' «before» o «after» di «" + comparedTo + "», non «" + location + "».");
            if (string.Equals(uid, comparedTo, StringComparison.OrdinalIgnoreCase))
                return Failed("«" + uid + "» non si sposta rispetto a se stessa: indica un'altra lavorazione.");
            string reason;
            if (!FeatureIsThatOne(model, uid, a, out reason)) return Failed(reason);
            if (!FeatureIsThatOne(model, comparedTo, a, out reason)) return Failed(reason);
            var f = FindFeature(model, uid);
            var g = FindFeature(model, comparedTo);
            if (f == null || g == null)
            {
                string ci = FeaturesThatExist(model);
                return Failed("Non trovo «" + (f == null ? uid : comparedTo) + "» in questo documento." +
                               (ci.Length > 0 ? " Ci sono: " + ci + "." : ""));
            }
            string nameF = f.Name, nameG = g.Name;
            var orderBefore = FeatureOrder(model);
            int posBefore = orderBefore.IndexOf(nameF);
            // Where to put it back if the move breaks something: before the one that
            // followed it, or after the one that preceded it if it was the last.
            string following = posBefore >= 0 && posBefore + 1 < orderBefore.Count ? orderBefore[posBefore + 1] : null;
            string previous = posBefore > 0 ? orderBefore[posBefore - 1] : null;
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            int errorsBefore = FeaturesInError(model);

            bool done;
            try
            {
                done = model.Extension.ReorderFeature(nameF, nameG,
                    location == "before" ? (int)swMoveLocation_e.swMoveBefore : (int)swMoveLocation_e.swMoveAfter);
            }
            catch (Exception ex) { return Failed("Lo spostamento non e' riuscito: " + ex.Message); }
            if (!done)
            {
                // «NOTHING TOUCHED» IS READ BACK FIRST (Z-C21.8, finding M42): the
                // order of the tree and the volume, against the ones read before
                // the call. Measured on 27/9/2026 that a refused move touches
                // nothing, and the sentence stays for that case only.
                string changed = TreeReadBackFault(orderBefore, FeatureOrder(model), volumeBefore, VolumeMm3(model));
                if (changed != null)
                    return Failed("SOLIDWORKS did not move «" + nameF + "» " + (location == "before" ? "before" : "after") + " «" + nameG +
                                   "» (ReorderFeature answered false), and reading the part back it is not as it was: " + changed +
                                   ". Look at the tree, and undo the step (" + Sentences.HowToWithdrawIt + ").");
                return Failed("SOLIDWORKS non sposta «" + nameF + "» " + location + " di «" + nameG + "» (misurato: succede " +
                               "quando la lavorazione dipende da una che verrebbe dopo, per esempio un foro sulla " +
                               "faccia di un'estrusione messo prima dell'estrusione). Non ho toccato niente: spostala " +
                               "rispetto a una lavorazione che viene dopo quelle da cui dipende.");
            }
            try { model.ForceRebuild3(false); } catch { }
            int errorsAfter = FeaturesInError(model);
            if (errorsBefore >= 0 && errorsAfter > errorsBefore)
            {
                // IT IS PUT BACK AS IT WAS: a broken tree is not handed over.
                bool restored = false;
                try
                {
                    if (following != null) restored = model.Extension.ReorderFeature(nameF, following, (int)swMoveLocation_e.swMoveBefore);
                    else if (previous != null) restored = model.Extension.ReorderFeature(nameF, previous, (int)swMoveLocation_e.swMoveAfter);
                    model.ForceRebuild3(false);
                }
                catch { }
                // «PUT BACK» IS SAID AFTER READING IT BACK (Z-C21.8, finding
                // M42): ReorderFeature answering true for the move back is not
                // the tree as it was. The order, the volume and the features in
                // error are read again.
                string backFault = null;
                if (restored)
                {
                    backFault = TreeReadBackFault(orderBefore, FeatureOrder(model), volumeBefore, VolumeMm3(model));
                    int errorsNow = FeaturesInError(model);
                    if (backFault == null && errorsNow != errorsBefore)
                        backFault = "features in error: " + (errorsNow < 0 ? "not counted" : errorsNow.ToString()) +
                                    " instead of " + errorsBefore;
                }
                string tail = !restored
                    ? "Non sono riuscito a rimetterla: " + Sentences.HowToWithdrawIt + "."
                    : backFault == null
                        ? "L'ho rimessa dov'era."
                        : "I moved it back, but reading the part back it is not as it was: " + backFault +
                          ". Undo the step (" + Sentences.HowToWithdrawIt + ").";
                return Failed("Spostata " + location + " di «" + nameG + "», «" + nameF + "» ha rotto " + (errorsAfter - errorsBefore) +
                               " lavorazioni (" + errorsAfter + " in errore invece di " + errorsBefore + "). " + tail +
                               " Di solito una lavorazione a valle si appoggiava a una faccia che adesso non c'e' ancora.");
            }
            var orderAfter = FeatureOrder(model);
            int iF = orderAfter.IndexOf(nameF), iG = orderAfter.IndexOf(nameG);
            bool inPlace = iF >= 0 && iG >= 0 && (location == "before" ? iF < iG : iF > iG);
            if (!inPlace)
                return Failed("SOLIDWORKS dice di aver spostato «" + nameF + "», ma nell'albero sta ancora " +
                               (iF < iG ? "prima" : "dopo") + " di «" + nameG + "». Non e' successo quello che volevi.");
            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            var measurements = new Dictionary<string, object>
            {
                ["moved"] = nameF,
                ["location"] = location,
                ["relative_to"] = nameG,
                ["order_before"] = orderBefore.ToArray(),
                ["order_after"] = orderAfter.ToArray(),
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["bodies_before"] = bodiesBefore >= 0 ? (object)bodiesBefore : null,
                ["bodies_after"] = bodiesAfter >= 0 ? (object)bodiesAfter : null,
                ["features_in_error"] = errorsAfter >= 0 ? (object)errorsAfter : null,
            };
            // THE ORDER CHANGED WITHOUT THE PART CHANGING is said: it usually means
            // that the two features do not touch each other, and moving them was not needed.
            if (volumeBefore != null && volumeAfter != null && Math.Abs(volumeBefore.Value - volumeAfter.Value) < 0.01 && bodiesBefore == bodiesAfter)
                measurements["warning"] = "The order changed but the part did not (same volume, same bodies): those two " +
                                       "features do not touch, and the order between them does not matter.";
            _log("[agente] " + nameF + " spostata " + location + " di " + nameG);
            return SucceededWithoutCreating(measurements);
        }
    }
}
