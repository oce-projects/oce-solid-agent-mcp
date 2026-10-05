using System;
using System.Collections.Generic;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// I CODICI DI ERRORE DELLE LAVORAZIONI, detti in italiano.
    ///
    /// PERCHE' SERVE. `IFeature.GetErrorCode2()` risponde un numero. Un numero
    /// in mano a chi legge lo stato non e' un'informazione: e' un compito.
    ///
    /// QUELLO CHE NON SI SA RESTA UN NUMERO. La tabella copre i codici di cui
    /// si conosce il significato; per tutti gli altri esce il nome dell'enum e
    /// il numero, e basta. **Un nome inventato su un codice e' peggio del
    /// numero nudo, perche' sembra una spiegazione** — e chi legge ci costruisce
    /// sopra una decisione.
    ///
    /// LE FRASI DICONO LA COSA, NON IL VERDETTO. «il raggio del raccordo non ci
    /// sta» dice cosa guardare; «errore nel raccordo» no. E' la stessa regola
    /// dei messaggi di rifiuto: si spiega il criterio.
    /// </summary>
    public static class FeatureErrors
    {
        private static readonly Dictionary<int, string> Sentences = new Dictionary<int, string>
        {
            // I raccordi, che sono la famiglia piu' numerosa perche' sono la
            // lavorazione che si rompe piu' spesso quando una quota cambia.
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

            // Le estrusioni.
            [30] = "l'estrusione produrrebbe corpi staccati fra loro",
            [31] = "l'estrusione non trova dove fermarsi: la condizione di fine " +
                   "non esiste piu'",
            [32] = "le condizioni geometriche dell'estrusione non tornano",
            [33] = "il contorno del taglio e' in parte aperto e in parte chiuso",
            [34] = "il contorno del taglio non e' valido",
            [35] = "il contorno aperto del taglio non e' valido",
            [36] = "il contorno dell'estrusione e' in parte aperto e in parte chiuso",
            [37] = "il contorno dell'estrusione non e' valido",

            // Il riferimento penzolante: lo schizzo era agganciato a una
            // geometria che non c'e' piu'.
            [51] = "un riferimento esterno dello schizzo non si risolve piu': " +
                   "la geometria a cui era agganciato non c'e'",

            [49] = "la lavorazione usa una funzione non piu' supportata",
            [50] = "la lavorazione usa una funzione obsoleta",

            [1] = "SOLIDWORKS non dice quale sia il problema",
        };

        /// <summary>
        /// La frase per un codice, oppure null se il codice e' zero — cioe' se
        /// non c'e' nessun errore.
        /// </summary>
        public static string Sentence(int code)
        {
            if (code == 0) return null;
            string sentence;
            if (Sentences.TryGetValue(code, out sentence)) return sentence;

            // Il nome dell'enum quando c'e': dice almeno di che famiglia si
            // tratta, e si puo' cercare nella documentazione.
            if (Enum.IsDefined(typeof(swFeatureError_e), code))
                return "SOLIDWORKS segnala «" + (swFeatureError_e)code +
                       "» (codice " + code + ")";
            return "SOLIDWORKS segnala il codice di errore " + code;
        }
    }
}
