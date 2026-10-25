using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE MATERIAL SEARCH MATCHES WHOLE WORDS (R4).
    ///
    /// "PLA" used to match "Plastic..." and "PVC ... plasticized" by substring. The matching
    /// lives in Oce.Solid.Agent.Core\MaterialNameMatch.cs, a pure class that this project
    /// compiles through a link, so it is tested here without SOLIDWORKS.
    /// </summary>
    internal static class MaterialNameMatchTests
    {
        internal static void All()
        {
            Group("the material is found by whole words (R4)");

            var plastics = new List<string> { "Custom Plastic", "Plastic Blend", "PVC 0.007 plasticized" };
            var steels = new List<string> { "1.0037 (S235JR)", "1.0116 (S235J2G3)", "1.0036 (S235JRG1)" };
            var alloys = new List<string> { "Alloy Steel", "Alloy Steel (SS)", "Cast Carbon Steel", "Stainless Steelwork" };

            var pla = Oce.Solid.Agent.Core.MaterialNameMatch.Candidates(plastics, "PLA");
            Check("PLA finds no plastic (a substring is not a word)", pla.Count == 0, Describe(pla));

            var exactName = Oce.Solid.Agent.Core.MaterialNameMatch.Candidates(plastics, "Custom Plastic");
            Check("the exact name finds only itself", Exactly(exactName, 0), Describe(exactName));

            var compact = Oce.Solid.Agent.Core.MaterialNameMatch.Candidates(plastics, "customplastic");
            Check("a name without spaces, in any case, finds the same material", Exactly(compact, 0), Describe(compact));

            var designation = Oce.Solid.Agent.Core.MaterialNameMatch.Candidates(steels, "S235JR");
            Check("the designation inside brackets is found exactly", Exactly(designation, 0), Describe(designation));

            var steel = Oce.Solid.Agent.Core.MaterialNameMatch.Candidates(alloys, "Steel");
            Check("Steel finds the three alloys with the word Steel, not Steelwork", Exactly(steel, 0, 1, 2), Describe(steel));

            var blank = Oce.Solid.Agent.Core.MaterialNameMatch.Candidates(plastics, "  ");
            Check("a blank search finds nothing", blank.Count == 0, Describe(blank));

            var stainless = new List<string> { "AISI 304 Stainless Steel", "AISI 316 Stainless Steel", "Plastic Blend" };
            var joined = Oce.Solid.Agent.Core.MaterialNameMatch.Candidates(stainless, "AISI304");
            Check("AISI304 finds AISI 304 Stainless Steel (adjacent words without the space)", Exactly(joined, 0), Describe(joined));

            var sourcePath = Path.Combine(Root(), "Oce.Solid.Agent.Core", "SolidWorksMaterials.cs");
            bool present = File.Exists(sourcePath);
            string source = present ? File.ReadAllText(sourcePath) : "";
            bool usesIt = source.IndexOf("MaterialNameMatch.Candidates(", StringComparison.Ordinal) >= 0;
            bool oldWording = source.IndexOf("not the same steel", StringComparison.Ordinal) >= 0;
            Check("the material search of SOLIDWORKS calls the whole-word match",
                  present && usesIt && !oldWording,
                  present ? "calls MaterialNameMatch: " + usesIt + ", old substring wording gone: " + !oldWording
                          : sourcePath);
        }

        private static bool Exactly(List<int> found, params int[] expected)
        {
            return found.SequenceEqual(expected);
        }

        private static string Describe(List<int> found)
        {
            return "[" + string.Join(", ", found) + "]";
        }
    }
}
