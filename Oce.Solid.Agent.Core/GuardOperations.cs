using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE GUARDS AFTER A CHANGE THAT GOES THROUGH THE REBUILD, in one place
    /// only (adversarial review of 27/9/2026).
    ///
    /// `set_dimension` had them all — the rebuild, the features in
    /// error, the bodies, the openings that leave the material, the chamfer or
    /// fillet that no longer fits — and the operations that came later, which change
    /// the geometry by another route (`global_variable`, `link_dimension`,
    /// `edit_extrusion`), had one or none. A variable that drives
    /// the thickness taken to 1 under a chamfer of 2 rebuilds clean and
    /// changes the shape (measured by case r05 for `set_dimension`): the
    /// answer was «succeeded», and the variable stayed written. A dimension
    /// linked to an equation `set_dimension` no longer changes (it is driven),
    /// so `global_variable` had become the shortcut that skipped
    /// all the guards.
    ///
    /// Here: the snapshot of the part before, and after the sentence about what
    /// broke, or null. The caller puts things back as they were and says so.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class PartPhoto
        {
            public double? Volume;
            public int Bodies;
            public int OpenCount;
            // The NUMBER of features in error (not an error code: the
            // Z-C7 guard forbids comparing a code with a digit, and this
            // is not one). -1 if it cannot be read.
            public int Broken;
            public Dictionary<string, int> Features;
        }

        private static PartPhoto SnapshotPart(IModelDoc2 model)
        {
            return new PartPhoto
            {
                Volume = VolumeMm3(model),
                Bodies = CountSolidBodies(model),
                OpenCount = ClosedOpenings(model),
                Broken = FeaturesInError(model),
                Features = FacesPerFeature(model),
            };
        }

        /// <summary>
        /// What broke compared with the snapshot, in a sentence («2 features
        /// went into error»), or null if nothing. A count that cannot be
        /// read (-1) does not accuse: «I do not know» is not «broken».
        ///
        /// «aperture» says whether it makes sense to count the closed openings. It makes sense when
        /// a dimension of the SKETCH changes (a slot that moves can leave the
        /// edge: case r16 of set_dimension). It makes no sense when only
        /// the DEPTH of a feature changes (edit_extrusion): a through cut
        /// that goes back to blind removes the hole from the underside face, the openings drop
        /// from 2 to 1 and the part is exactly the one asked for — measured on 28/9/2026 on
        /// case blind_becomes_through, which the guard wrongly refused.
        /// </summary>
        private static string WhatBroke(IModelDoc2 model, PartPhoto before, bool rebuilt, bool aperture)
        {
            int broken = FeaturesInError(model);
            if (before.Broken >= 0 && broken > before.Broken)
            {
                int newOnes = broken - before.Broken;
                return newOnes + (newOnes > 1 ? " features" : " feature") +
                       " went into error (" + broken + " instead of " + before.Broken + ")";
            }
            // A REBUILD THAT ANSWERS FALSE on a part that was sound (no
            // broken feature, counted: not «I do not know»). On an already broken part the
            // answer is false anyway, and the number of broken ones counts, above.
            bool wasHealthy = before.Broken >= 0 && before.Broken < 1;
            if (!rebuilt && wasHealthy)
                return "the rebuild did not succeed";
            int bodies = CountSolidBodies(model);
            if (before.Bodies >= 0 && bodies >= 0 && bodies != before.Bodies)
                return "the part has " + bodies + " bodies instead of " + before.Bodies + ": something split or merged";
            int open = aperture ? ClosedOpenings(model) : -1;
            if (aperture && before.OpenCount >= 0 && open >= 0 && open < before.OpenCount)
                return "an opening goes out of the material (the closed openings on the planar faces go from " + before.OpenCount +
                       " to " + open + ": a hole or a slot now opens the border, or two openings merged)";
            string chamfer = ChamferThatDoesNotFit(model, before.Features, FacesPerFeature(model));
            if (chamfer != null)
                return chamfer + ": it no longer fits (SOLIDWORKS does not report it as an error, it changes the shape by itself)";
            return null;
        }

        /// <summary>
        /// The tail of the refusal sentence, after putting things back: has the volume
        /// returned to that of the snapshot? It is read back, not believed.
        /// </summary>
        private static string IsBackAsItWas(IModelDoc2 model, PartPhoto before, bool restored, string what)
        {
            if (!restored) return " And I could not restore " + what + ": " + Sentences.HowToWithdrawIt + ".";
            double? now = VolumeMm3(model);
            if (now == null || before.Volume == null)
                return " I restored " + what + ", but I could not measure the volume again to check that the " +
                       "part is as it was: look at it.";
            if (Math.Abs(now.Value - before.Volume.Value) < 0.01)
                return " I did not leave it like this: I restored " + what + " and the part is as it was.";
            return " I restored " + what + ", but the volume is now " + now.Value.ToString("0.##") + " mm3 instead of " +
                   before.Volume.Value.ToString("0.##") + ": look at the part before going on.";
        }

        /// <summary>
        /// How many millimetres one length unit of the document is worth, or NaN if it
        /// is not known. The value of an equation (`IEquationMgr.get_Value`) is in the
        /// units of the DOCUMENT: the «0.2» of «"gioco" = 0.2mm» (82zs) in a
        /// document in millimetres. In one in inches the same 12mm would read back
        /// 0.472, and the comparison with the 12 asked for refused a right variable.
        /// </summary>
        private static double MmPerDocumentUnit(IModelDoc2 model)
        {
            int u = -1;
            try
            {
                u = model.Extension.GetUserPreferenceInteger(
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
