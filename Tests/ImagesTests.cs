using System;
using System.IO;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// WHAT GOES WITH A PHOTOGRAPH (entry Z-C10).
    ///
    /// ── TWO MEASURED ERRORS, NEITHER NOTICES ITSELF ───────────────────────
    ///
    /// - a ring of holes **Ø90 read as Ø79** from the isometric alone, with
    ///   full confidence;
    /// - a wall of **12.5 read as «6»** and a bottom of 12.5 read as «8»,
    ///   wrong by 40-50%, because in isometric a thin thickness is
    ///   foreshortened and looks thicker.
    ///
    /// The number that comes out is **plausible**, and ends up in the next step. There is
    /// nothing, in the number itself, that says «I come from a photograph».
    ///
    /// ── AND THIS CHECK LOOKS ONLY AT THE TEXT ─────────────────────────────
    ///
    /// It must be said, because it is half of the entry and not the other half: the bridge **cannot
    /// know** where a model took a number from. The entry asked that a
    /// number read from an image «ask for the geometric confirmation before
    /// being used in a step», and that piece is not implementable — there is
    /// nothing to intercept.
    ///
    /// What can be done is **to say it where the photograph arrives**, with the
    /// true numbers next to it, and to name the operations that give the
    /// authoritative measurement. A generic warning — «the measurements are in the state» —
    /// was already there and was not enough: it did not say that those from the image are
    /// *wrong*.
    /// </summary>
    internal static class ImagesTests
    {
        internal static void All()
        {
            Group("what goes with a photograph (Z-C10)");

            var file = Path.Combine(Root(), "Tools", "McpTools.cs");
            if (!File.Exists(file))
            {
                Check("the bridge source is there", false, "missing " + file);
                return;
            }
            var text = File.ReadAllText(file);

            Check("the caption says that the numbers read from the image are not authoritative",
                  text.Contains("are not authoritative"),
                  "Tools/McpTools.cs, next to the views");

            // THE NUMBERS, NOT THE ADJECTIVE. «It may be imprecise» does not change
            // anybody's behaviour; «90 read as 79» does.
            Check("and it carries the measured errors, not an adjective",
                  text.Contains("90") && text.Contains("79") &&
                  text.Contains("12.5") && text.Contains("read as 6"),
                  "Ø90 read as 79; wall 12.5 read as 6");

            // AND THE WAY TO THE TRUE MEASUREMENT. A warning without an alternative
            // leaves the model with the same problem and the doubt on top.
            Check("and names how to take the real measurement",
                  text.Contains("measure_between") &&
                  text.Contains("minimum_distance") &&
                  text.Contains("body_bounding_box"),
                  "three named operations, plus the state");

            // ── AND THE REASON, which is what makes the notice memorable ──
            Check("and says WHY the isometric view is wrong",
                  text.Contains("foreshortened"),
                  "in isometric the thicknesses are foreshortened and look bigger");
        }
    }
}
