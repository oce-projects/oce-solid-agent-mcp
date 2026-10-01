using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LE GUARDIE DOPO UNA MODIFICA CHE PASSA DALLA RICOSTRUZIONE, in un posto
    /// solo (revisione avversariale del 27/9/2026).
    ///
    /// `set_dimension` le aveva tutte — la ricostruzione, le lavorazioni in
    /// errore, i corpi, le aperture che escono dal materiale, lo smusso o il
    /// raccordo che non ci sta piu' — e le operazioni venute dopo, che cambiano
    /// la geometria per un'altra strada (`global_variable`, `link_dimension`,
    /// `edit_extrusion`), ne avevano una o nessuna. Una variabile che guida
    /// lo spessore portata a 1 sotto uno smusso da 2 ricostruisce pulita e
    /// cambia la forma (lo ha misurato il caso r05 per `set_dimension`): la
    /// risposta era «riuscito», e la variabile restava scritta. Una quota
    /// legata a un'equazione `set_dimension` non la cambia piu' (e' guidata),
    /// quindi `global_variable` era diventata la scorciatoia che saltava
    /// tutte le guardie.
    ///
    /// Qui: la fotografia del pezzo prima, e dopo la frase di cio' che si e'
    /// rotto, o null. Chi chiama rimette com'era e lo dice.
    /// </summary>
    public partial class OperazioniSw
    {
        private sealed class FotoDelPezzo
        {
            public double? Volume;
            public int Corpi;
            public int Aperte;
            // Il NUMERO di lavorazioni in errore (non un codice d'errore: la
            // guardia Z-C7 vieta di confrontare un codice con una cifra, e questo
            // non lo e'). -1 se non si legge.
            public int Rotte;
            public Dictionary<string, int> Lavorazioni;
        }

        private static FotoDelPezzo FotografaIlPezzo(IModelDoc2 modello)
        {
            return new FotoDelPezzo
            {
                Volume = VolumeMm3(modello),
                Corpi = ContaCorpiSolidi(modello),
                Aperte = ApertureChiuse(modello),
                Rotte = LavorazioniInErrore(modello),
                Lavorazioni = FaccePerLavorazione(modello),
            };
        }

        /// <summary>
        /// Cosa si e' rotto rispetto alla fotografia, in una frase («2 lavorazioni
        /// sono andate in errore»), o null se niente. Un conteggio che non si
        /// legge (-1) non accusa: «non lo so» non e' «rotto».
        ///
        /// «aperture» dice se ha senso contare le aperture chiuse. Ha senso quando
        /// cambia una quota dello SCHIZZO (un'asola che si sposta puo' uscire dal
        /// bordo: il caso r16 di set_dimension). Non ha senso quando cambia solo
        /// la PROFONDITA' di una lavorazione (edit_extrusion): un taglio passante
        /// che torna cieco toglie il foro dalla faccia di sotto, le aperture calano
        /// da 2 a 1 e il pezzo e' proprio quello chiesto — misurato il 28/9/2026 sul
        /// caso blind_becomes_through, che la guardia rifiutava a torto.
        /// </summary>
        private static string CosaSiERotto(IModelDoc2 modello, FotoDelPezzo prima, bool ricostruito, bool aperture)
        {
            int rotte = LavorazioniInErrore(modello);
            if (prima.Rotte >= 0 && rotte > prima.Rotte)
            {
                int nuove = rotte - prima.Rotte;
                return nuove + (nuove > 1 ? " lavorazioni sono andate" : " lavorazione e' andata") +
                       " in errore (" + rotte + " invece di " + prima.Rotte + ")";
            }
            // UNA RICOSTRUZIONE CHE RISPONDE FALSE su un pezzo che era sano (nessuna
            // lavorazione rotta, contata: non «non lo so»). Su un pezzo gia' rotto la
            // risposta e' false comunque, e conta il numero delle rotte, qui sopra.
            bool eraSano = prima.Rotte >= 0 && prima.Rotte < 1;
            if (!ricostruito && eraSano)
                return "la ricostruzione non e' riuscita";
            int corpi = ContaCorpiSolidi(modello);
            if (prima.Corpi >= 0 && corpi >= 0 && corpi != prima.Corpi)
                return "il pezzo ha " + corpi + " corpi invece di " + prima.Corpi + ": qualcosa si e' spezzato o unito";
            int aperte = aperture ? ApertureChiuse(modello) : -1;
            if (aperture && prima.Aperte >= 0 && aperte >= 0 && aperte < prima.Aperte)
                return "un'apertura esce dal materiale (le aperture chiuse sulle facce piane passano da " + prima.Aperte +
                       " a " + aperte + ": un foro o un'asola adesso apre il bordo, o due aperture si sono fuse)";
            string smusso = SmussoCheNonCiSta(modello, prima.Lavorazioni, FaccePerLavorazione(modello));
            if (smusso != null)
                return smusso + ": non ci sta piu' (SOLIDWORKS non lo segnala come errore, cambia la forma da solo)";
            return null;
        }

        /// <summary>
        /// La coda della frase di rifiuto, dopo aver rimesso le cose: il volume e'
        /// tornato quello della fotografia? Si rilegge, non si crede.
        /// </summary>
        private static string ETornatoComEra(IModelDoc2 modello, FotoDelPezzo prima, bool rimesso, string cosa)
        {
            if (!rimesso) return " E non sono riuscito a rimettere " + cosa + ": " + Frasi.CosiSiRitira + ".";
            double? adesso = VolumeMm3(modello);
            if (adesso == null || prima.Volume == null)
                return " Ho rimesso " + cosa + ", ma non ho potuto rimisurare il volume per controllare che il " +
                       "pezzo sia com'era: guardalo.";
            if (Math.Abs(adesso.Value - prima.Volume.Value) < 0.01)
                return " Non l'ho lasciato cosi': ho rimesso " + cosa + " e il pezzo e' com'era.";
            return " Ho rimesso " + cosa + ", ma il volume adesso e' " + adesso.Value.ToString("0.##") + " mm3 invece di " +
                   prima.Volume.Value.ToString("0.##") + ": guarda il pezzo prima di andare avanti.";
        }

        /// <summary>
        /// Quanti millimetri vale un'unita' di lunghezza del documento, o NaN se non
        /// si sa. Il valore di un'equazione (`IEquationMgr.get_Value`) e' nelle
        /// unita' del DOCUMENTO: lo «0.2» di «"gioco" = 0.2mm» (82zs) in un
        /// documento in millimetri. In uno in pollici lo stesso 12mm rileggerebbe
        /// 0,472, e il confronto col 12 chiesto rifiutava una variabile giusta.
        /// </summary>
        private static double MmPerUnitaDelDocumento(IModelDoc2 modello)
        {
            int u = -1;
            try
            {
                u = modello.Extension.GetUserPreferenceInteger(
                    (int)swUserPreferenceIntegerValue_e.swUnitsLinear,
                    (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);
            }
            catch { }
            return MmPerLengthUnit(u);
        }

        /// <summary>
        /// Millimetres per unit of the swLengthUnit_e value <paramref name="unit"/>,
        /// as IEquationMgr.get_Value answers in a document set to that unit, or
        /// NaN when that is not known (Z-C21.13, finding L17-7). Kept apart from
        /// the document, with no interop in the signature, so that the contract
        /// group of Z-C21.13 in Prove can call it without SOLIDWORKS.
        ///
        /// THE FACTORS ARE MEASURED, not taken from the name of the unit. The
        /// probe probe_feet_inches (rerun on 29/9/2026, SOLIDWORKS 2021 SP5.1): a
        /// throwaway part with the global variables x = 12mm and y = 1000mm, the
        /// unit system set to Custom and swUnitsLinear set in turn to every value
        /// of swLengthUnit_e, then EvaluateAll, ForceRebuild3 and get_Value of the
        /// two; the factor is 12 / x and 1000 / y, and the two must agree. The
        /// control read back the factors already used here (mm 1, cm 10, m 1000,
        /// in 25.4, ft 304.8, mil 0.0254). New from that run:
        ///
        ///   swANGSTROM   x 120000000, y 10000000000      1E-07 from both
        ///   swNANOMETER  x 12000000,  y 1000000000       1E-06 from both
        ///   swUIN        x 472440.94, y 39370078.74      2.54E-05 from both
        ///
        /// NaN ON PURPOSE, for two units the same run could not settle:
        ///
        ///   swFEETINCHES  x 0.47, y 39.37: 25.53 from x and 25.40 from y. They
        ///                 look like inches cut to two decimals (the exact inches
        ///                 are 0.4724 and 39.370), and a value cut that way does
        ///                 not go back to the millimetres that were written: a
        ///                 comparison to a millionth of a millimetre would refuse
        ///                 every correct variable.
        ///   swMICRON      x 12000000, y 1000000000: 1E-06 from both, the factor
        ///                 of the nanometre and a thousand times smaller than the
        ///                 micrometre. Until library 0.143.0 this switch said
        ///                 0.001: with the reading of the probe, a correct 12 mm
        ///                 comes back as 12000 mm and the variable is refused (a
        ///                 consequence worked out from the probe, not run through
        ///                 the global-variable operation on a micron document). The
        ///                 number the probe read contradicts the unit it names:
        ///                 until a second measurement explains it, «I do not
        ///                 know» is the honest answer, not either factor.
        ///
        /// NaN makes the caller skip the comparison and say so, which is what a
        /// unit it cannot convert deserves.
        /// </summary>
        private static double MmPerLengthUnit(int unit)
        {
            switch (unit)
            {
                case (int)swLengthUnit_e.swMM: return 1.0;
                case (int)swLengthUnit_e.swCM: return 10.0;
                case (int)swLengthUnit_e.swMETER: return 1000.0;
                case (int)swLengthUnit_e.swINCHES: return 25.4;
                case (int)swLengthUnit_e.swFEET: return 304.8;
                case (int)swLengthUnit_e.swMIL: return 0.0254;
                case (int)swLengthUnit_e.swANGSTROM: return 1E-07;
                case (int)swLengthUnit_e.swNANOMETER: return 1E-06;
                case (int)swLengthUnit_e.swUIN: return 2.54E-05;
                case (int)swLengthUnit_e.swFEETINCHES: return double.NaN;
                case (int)swLengthUnit_e.swMICRON: return double.NaN;
                default: return double.NaN;
            }
        }
    }
}
