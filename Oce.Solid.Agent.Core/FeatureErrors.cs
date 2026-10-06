using System;
using System.Collections.Generic;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE ERROR CODES OF THE FEATURES, stated in Italian.
    ///
    /// WHY IT IS NEEDED. `IFeature.GetErrorCode2()` answers a number. A number
    /// in the hands of whoever reads the state is not information: it is a task.
    ///
    /// WHAT IS NOT KNOWN STAYS A NUMBER. The table covers the codes whose
    /// meaning is known; for all the others the name of the enum and
    /// the number come out, and nothing else. **A name invented for a code is worse than the
    /// bare number, because it looks like an explanation** — and whoever reads builds
    /// a decision on it.
    ///
    /// THE SENTENCES SAY THE THING, NOT THE VERDICT. «il raggio del raccordo non ci
    /// sta» says what to look at; «errore nel raccordo» does not. It is the same rule
    /// as the refusal messages: the criterion is explained.
    /// </summary>
    public static class FeatureErrors
    {
        private static readonly Dictionary<int, string> Sentences = new Dictionary<int, string>
        {
            // The fillets, which are the most numerous family because they are the
            // feature that breaks most often when a dimension changes.
            [10] = "the fillet cannot find a closed contour to rest on",
            [11] = "the fillet can no longer find the face it was on",
            [12] = "the radius of the fillet is not right for this geometry: " +
                   "usually it means that it is bigger than what fits",
            [13] = "the fillet can no longer find the edges it was on",
            [14] = "the geometry of the model does not allow this fillet",
            [15] = "the radius of the fillet is too small",
            [16] = "the fillet cannot extend as far as the neighboring faces",
            [17] = "with this radius the fillet would make a piece of geometry disappear",
            [18] = "the radius of the fillet is too big for the chosen edges",
            [19] = "the radius of the fillet is too big for the chosen edges",

            // The extrusions.
            [30] = "the extrusion would produce bodies detached from each other",
            [31] = "the extrusion cannot find where to stop: the end condition " +
                   "no longer exists",
            [32] = "the geometric conditions of the extrusion do not add up",
            [33] = "the contour of the cut is partly open and partly closed",
            [34] = "the contour of the cut is not valid",
            [35] = "the open contour of the cut is not valid",
            [36] = "the contour of the extrusion is partly open and partly closed",
            [37] = "the contour of the extrusion is not valid",

            // The dangling reference: the sketch was attached to a
            // geometry that is no longer there.
            [51] = "an external reference of the sketch no longer resolves: " +
                   "the geometry it was attached to is not there",

            [49] = "the feature uses a function that is no longer supported",
            [50] = "the feature uses an obsolete function",

            [1] = "SOLIDWORKS does not say what the problem is",
        };

        /// <summary>
        /// The sentence for a code, or null if the code is zero — that is if
        /// there is no error.
        /// </summary>
        public static string Sentence(int code)
        {
            if (code == 0) return null;
            string sentence;
            if (Sentences.TryGetValue(code, out sentence)) return sentence;

            // The name of the enum when there is one: it says at least which family it
            // is, and it can be looked up in the documentation.
            if (Enum.IsDefined(typeof(swFeatureError_e), code))
                return "SOLIDWORKS reports «" + (swFeatureError_e)code +
                       "» (code " + code + ")";
            return "SOLIDWORKS reports the error code " + code;
        }
    }
}
