namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE SENTENCES THAT RECUR, written once only.
    ///
    /// WHY THIS FILE EXISTS. Up to 0.31.0 the way of going back
    /// was written **twenty-one times** in almost the same words, and always said the
    /// same thing: «undo it with Ctrl+Z». Then the `undo` tool arrived
    /// (entry R1) and those twenty-one sentences became twenty-one places
    /// to correct — which is exactly the count that a repeated sentence
    /// presents on the day it changes.
    ///
    /// Here there is only one, and the three files that use it say the same thing
    /// by construction.
    /// </summary>
    internal static class Sentences
    {
        /// <summary>
        /// How to withdraw what has just been done.
        ///
        /// ── WHY IT SAYS TWO THINGS AND NOT ONE ───────────────────────────
        ///
        /// This library has **two different readers**, and a sentence true for only
        /// one of the two is a false sentence:
        ///
        ///   * the **model**, which comes from the MCP bridge. It has no keyboard:
        ///     «press Ctrl+Z» is a repair it cannot take. For it
        ///     the `undo` tool exists.
        ///   * the **person**, who uses the same operations from the OpenPDM
        ///     add-in or who reads this line in a log. For them `undo` does not
        ///     exist: the gesture is `Ctrl+Z`.
        ///
        /// Naming only the tool would have sent the designer looking for
        /// a command that is not in their menu. Naming only the key is the
        /// defect that entry R3 closes. Both are said, in the order in
        /// which they serve whoever reads.
        ///
        /// It begins with a space: it is attached at the tail of a message.
        /// </summary>
        public const string HowToWithdrawIt =
            "«undo» if you are talking to the bridge, Ctrl+Z if you are in front of the screen";

        /// <summary>
        /// WHERE IT WAS AND WHERE IT IS, as a fraction of the bounding box — **and only in
        /// words** (entry E10).
        ///
        /// ── WHY A SENTENCE COMES BACK AND NOT A NUMBER ────────────────────
        ///
        /// The fraction of the bounding box — `u = (x − minX) / (maxX − minX)` — is the
        /// explanation that the messages of this library lack: when a
        /// part grows, «the face moved by 60 mm» is true and says
        /// nothing, while «stava al 100% del lato e ci sta ancora» says
        /// **what happened**.
        ///
        /// It is also, as an IDENTITY CRITERION, the only one of the proposed roads
        /// that turns an «I do not know» into a wrong «yes»:
        ///
        ///   * a hole 20 mm from the edge, on a plate going from 100 to
        ///     300, **stays 20 mm from the edge**: that is what it means in the
        ///     workshop. The fraction says it went to 60 mm, and since there
        ///     it finds a face, **it CONFIRMS instead of refusing**;
        ///   * the bounding box is unstable: a boss in one corner is enough to
        ///     change the denominator of **all** the faces together.
        ///
        /// That is why the return type is `string` and not `double`, and it is not
        /// a detail: a sentence does not go into an `if`. Whoever wanted to promote
        /// this number to a criterion — two months from now, «anyway the calculation is already there»
        /// — would have to re-read the sentence to extract the numbers, and it
        /// would be noticed. A search for «fraction» inside `SignatureMatches` or inside
        /// the finding loop must come back **empty**: it is the true closing
        /// criterion of entry E10.
        ///
        /// Null when there is nothing to say: missing data, degenerate
        /// box, or the face did not move.
        /// </summary>
        /// <param name="uid">What the entity is called in the message.</param>
        /// <param name="before">The point that the plan carried with it, in mm.</param>
        /// <param name="now">The point measured now, in mm.</param>
        /// <param name="boxBefore">{minX,minY,minZ,maxX,maxY,maxZ} of then.</param>
        /// <param name="boxNow">{minX,minY,minZ,maxX,maxY,maxZ} of now.</param>
        public static string WhereItWasAndWhereItIs(
            string uid, double[] before, double[] now,
            double[] boxBefore, double[] boxNow)
        {
            if (before == null || now == null ||
                boxBefore == null || boxNow == null) return null;
            if (before.Length < 3 || now.Length < 3 ||
                boxBefore.Length < 6 || boxNow.Length < 6) return null;

            // THE AXIS THAT MOVED, only one. Saying all three would mean
            // writing two lines about two directions in which nothing
            // happened: a longer message that reads worse.
            int axis = -1;
            double howMuch = 0;
            for (int i = 0; i < 3; i++)
            {
                double d = System.Math.Abs(now[i] - before[i]);
                if (d > howMuch) { howMuch = d; axis = i; }
            }
            if (axis < 0 || howMuch <= 0.05) return null;

            double sideBefore = boxBefore[axis + 3] - boxBefore[axis];
            double sideNow = boxNow[axis + 3] - boxNow[axis];
            // Under a tenth of a millimetre the division no longer means
            // anything: a side of 0.01 mm produces percentages in the thousands.
            if (sideBefore < 0.1 || sideNow < 0.1) return null;

            double uBefore = (before[axis] - boxBefore[axis]) / sideBefore;
            double uNow = (now[axis] - boxNow[axis]) / sideNow;

            string itemName = axis == 0 ? "X" : (axis == 1 ? "Y" : "Z");
            var c = System.Globalization.CultureInfo.InvariantCulture;

            // ONE PERCENTAGE POINT. Narrower would be a comparison between
            // roundings; wider would say «it still fits» of a face
            // that really moved with respect to the part.
            if (System.Math.Abs(uBefore - uNow) <= 0.01)
                return string.Format(c,
                    " For what it is worth: «{0}» was at {1:0.#}% of side {2} and is still " +
                    "there — the side went from {3:0.##} to {4:0.##} mm. It may be the " +
                    "same face grown with the part, but I do not decide that: " +
                    "identity is measured in millimeters.",
                    uid, uBefore * 100.0, itemName, sideBefore, sideNow);

            return string.Format(c,
                " For what it is worth: «{0}» was at {1:0.#}% of the side {2} and now it is " +
                "at {3:0.#}% — the side went from {4:0.##} to {5:0.##} mm.",
                uid, uBefore * 100.0, itemName, uNow * 100.0, sideBefore, sideNow);
        }
    }
}
