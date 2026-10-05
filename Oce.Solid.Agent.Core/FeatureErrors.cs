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
            [10] = "il raccordo non trova un contorno chiuso su cui appoggiarsi",
            [11] = "il raccordo non trova piu' la faccia su cui stava",
            [12] = "il raggio del raccordo non va bene per questa geometria: " +
                   "di solito vuol dire che e' piu' grande di quanto ci stia",
            [13] = "il raccordo non trova piu' gli spigoli su cui stava",
            [14] = "la geometria del modello non permette questo raccordo",
            [15] = "il raggio del raccordo e' troppo piccolo",
            [16] = "il raccordo non riesce a estendersi fino alle facce vicine",
            [17] = "con questo raggio il raccordo farebbe sparire un pezzo di geometria",
            [18] = "il raggio del raccordo e' troppo grande per gli spigoli scelti",
            [19] = "il raggio del raccordo e' troppo grande per gli spigoli scelti",

            // The extrusions.
            [30] = "l'estrusione produrrebbe corpi staccati fra loro",
            [31] = "l'estrusione non trova dove fermarsi: la condizione di fine " +
                   "non esiste piu'",
            [32] = "le condizioni geometriche dell'estrusione non tornano",
            [33] = "il contorno del taglio e' in parte aperto e in parte chiuso",
            [34] = "il contorno del taglio non e' valido",
            [35] = "il contorno aperto del taglio non e' valido",
            [36] = "il contorno dell'estrusione e' in parte aperto e in parte chiuso",
            [37] = "il contorno dell'estrusione non e' valido",

            // The dangling reference: the sketch was attached to a
            // geometry that is no longer there.
            [51] = "un riferimento esterno dello schizzo non si risolve piu': " +
                   "la geometria a cui era agganciato non c'e'",

            [49] = "la lavorazione usa una funzione non piu' supportata",
            [50] = "la lavorazione usa una funzione obsoleta",

            [1] = "SOLIDWORKS non dice quale sia il problema",
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
                return "SOLIDWORKS segnala «" + (swFeatureError_e)code +
                       "» (codice " + code + ")";
            return "SOLIDWORKS segnala il codice di errore " + code;
        }
    }
}
