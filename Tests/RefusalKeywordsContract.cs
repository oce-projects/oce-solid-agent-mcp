using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE DECLARATION OF A LIMIT HAS ONE KEYWORD, ACROSS THE WHOLE CATALOGUE (W3.1-bis.9).
    ///
    /// An operation tells the model, before the call, where it will refuse: a paragraph
    /// that begins with the keyword and goes on with the condition. The model learns the
    /// shape from the ninety-nine descriptions it reads, and it uses it the way it uses
    /// any other: to find the limits of an operation without calling it. In Italian the
    /// keyword was the first-person «I refuse» of that language, in capitals, then «if»
    /// (sometimes in lowercase, sometimes as a third-person form: the patterns below, in
    /// ROT13), and the English written beside it said «I refuse when», «I REFUSE when»,
    /// «I refuse the frame...»: five ways of saying one thing, and a model that has to
    /// guess which paragraphs are limits.
    ///
    /// SO: a paragraph of a description that declares a limit in the first person begins
    /// exactly «I REFUSE IF », in capitals, followed by the condition. «I REFUSE», alone or
    /// with another word, is not that.
    ///
    /// WHAT IS A DECLARATION. A paragraph (the text between two blank lines) that begins
    /// with one of the forms the keyword has had: «I REFUSE» and «I refuse», written by
    /// hand with another word after them. A refusal told in the middle of a sentence («the step
    /// is refused and AddMate5 is not called») is prose, not a declaration, and is not
    /// read here. A heading in capitals about a refusal that is not in the first person
    /// («A REDUNDANT MATE IS REFUSED:») is not a declaration either.
    ///
    /// THE RATCHET. The count of declarations that are NOT in the form, kept in
    /// Tests/data/refusal-keyword-ceiling.txt, is exact: more is red, and fewer is red until
    /// the number is lowered (a ceiling above the real count would let a description go
    /// back unseen). It reached zero with the last family translated (W3.1-bis.9), and
    /// the Italian forms left the pattern below in W3.1-bis.11.
    ///
    /// Written like the other contracts of the descriptions: first seen red on planted
    /// text, then read on the catalogue the bridge serves (Tools/catalog.json).
    /// </summary>
    internal static class RefusalKeywordsContract
    {
        /// <summary>The keyword, with the space that follows it.</summary>
        internal const string Keyword = "I REFUSE IF ";

        /// <summary>
        /// Where the number of declarations that are not in the one form is kept (the
        /// paragraphs written by hand with another verb). **Zero.**
        /// </summary>
        private static readonly string CeilingFile = Path.Combine("Tests", "data", "refusal-keyword-ceiling.txt");

        private static readonly Regex DeclarationStart = new Regex(
            "^(?:I REFUSE|I refuse)\\b",
            RegexOptions.CultureInvariant);

        /// <summary>The paragraphs of a description that declare a limit in the first person.</summary>
        internal static List<string> Declarations(string text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;
            foreach (var p in text.Split(new[] { "\n\n" }, StringSplitOptions.None))
                if (DeclarationStart.IsMatch(p)) found.Add(p);
            return found;
        }

        /// <summary>The declarations of a description that do not begin with the keyword.</summary>
        internal static List<string> OutOfForm(string text)
        {
            return Declarations(text).Where(p => !p.StartsWith(Keyword, StringComparison.Ordinal)).ToList();
        }

        internal static void RunAll()
        {
            Group("the declarations of a limit begin with one keyword (W3.1-bis.9)");

            // 1. The detector, red on what it must find and green on what it must leave.
            var planted = string.Join("\n\n", new[]
            {
                "A paragraph before.",
                "I refuse when the volume does not change.",
                "I REFUSE when SOLIDWORKS does not accept it.",
                "I refuse the frame, and draw nothing, when the profile is missing.",
                "I REFUSE IF the diameter is not positive.",
                "A REDUNDANT MATE IS REFUSED: when the mate blocks nothing.",
                "Prose that says the step is refused and AddMate5 is not called.",
            });
            var all = Declarations(planted);
            var out_ = OutOfForm(planted);
            Check("the detector finds the old forms of the keyword and leaves the right one and the prose alone",
                  all.Count == 4 && out_.Count == 3 && !out_.Any(p => p.StartsWith(Keyword, StringComparison.Ordinal)),
                  "declarations " + all.Count + "/4, out of form " + out_.Count + "/3");

            // 2. The catalogue the bridge serves.
            var file = Path.Combine(Root(), "bin", "catalog.json");
            if (!File.Exists(file)) file = Path.Combine(Root(), "Tools", "catalog.json");
            if (!File.Exists(file)) { Check("the served catalogue is there", false, "missing " + file); return; }
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var catalogue = ser.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
            var ops = catalogue != null && catalogue.ContainsKey("operations") ? catalogue["operations"] as object[] : null;
            if (ops == null) { Check("the catalogue has its operations", false, "no «operations»"); return; }

            int declarations = 0, inForm = 0;
            var wrong = new List<string>();
            foreach (var o in ops.OfType<Dictionary<string, object>>())
            {
                var name = Convert.ToString(o.ContainsKey("name") ? o["name"] : null);
                var text = Convert.ToString(o.ContainsKey("details") ? o["details"] : null);
                var d = Declarations(text);
                declarations += d.Count;
                foreach (var p in d)
                {
                    if (p.StartsWith(Keyword, StringComparison.Ordinal)) inForm++;
                    else wrong.Add(name + ": «" + (p.Length > 30 ? p.Substring(0, 30) : p) + "...»");
                }
            }
            Check("the catalogue declares limits in the first person (the reading is not empty)",
                  declarations >= 30 && inForm >= 1,
                  declarations + " declarations, " + inForm + " in the form «" + Keyword.Trim() + "»");
            int ceiling;
            var ceilingPath = Path.Combine(Root(), CeilingFile);
            if (!File.Exists(ceilingPath) || !int.TryParse(File.ReadAllText(ceilingPath).Trim(), out ceiling))
            {
                Check("the number of declarations out of form is written down", false, "missing or not a number: " + ceilingPath);
                return;
            }
            Check("the declarations that are not in the form «" + Keyword.Trim() + "» have not increased",
                  wrong.Count <= ceiling,
                  wrong.Count + " out of form (ceiling " + ceiling + ")" +
                      (wrong.Count > ceiling ? ": " + string.Join(", ", wrong.Take(6)) + (wrong.Count > 6 ? " ..." : "") : ""));
            Check("and the ceiling is the real one",
                  wrong.Count == ceiling,
                  wrong.Count == ceiling ? "the number written down is the count" : "the count is " + wrong.Count + " and the file says " + ceiling + ": lower it in Tests/data/refusal-keyword-ceiling.txt");
        }
    }
}
