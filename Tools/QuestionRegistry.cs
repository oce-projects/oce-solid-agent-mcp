using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Oce.Solid.Agent.Tools
{
    /// <summary>
    /// THE REGISTRY OF UNANSWERED QUESTIONS (entry Z-G7)
    ///
    /// ── WHY IT EXISTS ────────────────────────────────────────────────────
    ///
    /// The five operations of the catalogue that at 23/9/2026 had no
    /// bench case — `circular_pattern`, `weld_bead`, `gusset`,
    /// `trim_members`, `end_cap` — **we chose ourselves**. (On
    /// 25/9, writing those cases, two turned out never to have worked and one
    /// left the catalogue: chosen at a desk, and not even measured.) Which ones
    /// are really needed nobody knows, and asking people does not make it
    /// known: whoever uses the bridge does not have the list of what is not there.
    ///
    /// In OpenPDM the registry of questions is what made the
    /// defect «folder library / state library» be found: it was not discovered
    /// by asking, it was read there. This bridge had the same data in hand and
    /// threw it away — the `default` case of `McpTools.Invoke` **knows** that
    /// a tool that does not exist was asked for, answers well, and does not
    /// write it anywhere.
    ///
    /// **Three different models inventing the same name are the next
    /// operation to build, and it is not opinion.**
    ///
    /// ── WHAT IT DOES NOT WRITE, AND IT IS NOT AN OVERSIGHT ───────────────
    ///
    /// Only the name asked for, what kind of question it was, and the nearby names that
    /// the catalogue had proposed. **No arguments, no paths,
    /// no document names.**
    ///
    /// A registry that copies the arguments of a call becomes in one
    /// afternoon a file full of network paths and job names, and
    /// this repository already has a personal data problem that
    /// prevents its publication. A name invented by a model is nobody's
    /// data; the path where the part sits is.
    ///
    /// The `similar` ones are written because they are **the useful half of the measure**:
    /// they distinguish «the model misspelled a name that exists» from
    /// «the model asks for something that does not exist». The first case is fixed
    /// with a synonym, the second is work to be done.
    ///
    /// ── AND IT CANNOT MAKE THE CALL IT OBSERVES FAIL ─────────────────────
    ///
    /// Two rules, both absolute:
    ///
    ///  1. **It never writes to stdout.** That is where JSON-RPC passes: one line
    ///     too many is not a dirty log, it is the protocol broken. This class
    ///     does not name `Console` anywhere, and there is a contract
    ///     check that demands it by reading the source.
    ///  2. **It never raises.** Installed in `Program Files` the folder is not
    ///     writable; on a machine with two bridges open the file may
    ///     be busy; the disk may be full. In all these cases
    ///     we stay silent and carry on. *A registry that makes the call it
    ///     was supposed to observe fail is worse than no registry.*
    ///
    /// The reason for the last silence stays in `WhyItWasSilent`, which nobody
    /// prints: it serves a check, so as not to have to take on trust that
    /// the silence was deliberate.
    ///
    /// ── THE FORM: ONE LINE PER QUESTION, NOT A COUNT ─────────────────────
    ///
    /// JSONL appended, not a JSON rewritten. Rewriting would mean
    /// read-modify-write, and two bridges open on the same machine
    /// — which happens here — would eat each other's lines without
    /// anyone noticing. Appending, the worst that happens is one line
    /// lost.
    ///
    /// Counting is the reader's problem: `Checks/questions-without-answer.js`.
    /// </summary>
    internal sealed class QuestionRegistry
    {
        public const string FileName = "domande-senza-risposta.jsonl";

        /// <summary>The file being written to, or null if no place was found.</summary>
        public string FilePath { get; }

        /// <summary>
        /// Why the last write did not happen, or null if it went through.
        /// Nobody prints it: it exists so that a check can verify
        /// that the silence was deliberate and not a dead branch.
        /// </summary>
        public string WhyItWasSilent { get; private set; }

        /// <summary>How many lines this instance really wrote.</summary>
        public int Written { get; private set; }

        public QuestionRegistry() : this(DefaultPath()) { }

        public QuestionRegistry(string filePath)
        {
            FilePath = filePath;
        }

        /// <summary>
        /// TWO VARIABLE NAMES AND A FALLBACK, as for the catalogue.
        ///
        /// `RISCONTRO_REGISTRO` gives the whole file. Without it, we write
        /// next to the executable — which on a development machine is
        /// writable and on an installed one almost never, and that is fine: rule
        /// 2 says that then we stay silent.
        ///
        /// Setting it to an empty string is **not** the same as not setting it: it is the
        /// way of saying «record nothing», and it must be respected without
        /// fallbacks, otherwise whoever empties it to silence the registry
        /// finds themselves writing next to the executable.
        /// </summary>
        public static string DefaultPath()
        {
            var fromEnv = Environment.GetEnvironmentVariable("RISCONTRO_REGISTRO");
            if (fromEnv != null) return fromEnv.Length == 0 ? null : fromEnv;

            try
            {
                var folder = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(folder)) return null;
                return Path.Combine(folder, FileName);
            }
            catch { return null; }
        }

        public void ToolThatDoesNotExist(string itemName)
        {
            Annotate("strumento", itemName, null);
        }

        public void OperationThatDoesNotExist(string itemName, IEnumerable<string> similar)
        {
            Annotate("operation", itemName, similar);
        }

        /// <summary>
        /// THE LINE, SEPARATED FROM WHOEVER WRITES IT.
        ///
        /// It takes the clock instead of reading it, so a check can fix
        /// the date and compare the whole line. And it serializes with the same
        /// `Protocol.InJson` as the rest of the bridge, instead of gluing
        /// quotes by hand: a model may very well invent
        /// an operation called `taglia"a"meta`, and a broken JSONL line
        /// makes the file unreadable from then on.
        /// </summary>
        public static string Line(
            DateTime timestamp, string typ, string itemName, IEnumerable<string> similar)
        {
            var entry = new Dictionary<string, object>
            {
                ["timestamp"] = timestamp.ToString("s"),
                ["kind"] = typ,
                ["name"] = itemName ?? "",
            };
            if (similar != null)
            {
                var neighbors = similar.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                if (neighbors.Length > 0) entry["similar"] = neighbors;
            }
            return Mcp.Protocol.InJson(entry);
        }

        private void Annotate(string typ, string itemName, IEnumerable<string> similar)
        {
            WhyItWasSilent = null;

            if (FilePath == null)
            {
                WhyItWasSilent = "there is no file to write to";
                return;
            }
            if (string.IsNullOrWhiteSpace(itemName))
            {
                // An empty name is not a question: `McpTools` already answers
                // «manca «operation»», and it is a form problem of the plan, not
                // something somebody wanted and was not there. Recording it
                // would fill the file with lines that teach nothing.
                WhyItWasSilent = "empty name: it is not a question";
                return;
            }

            try
            {
                // `Append` opens and closes at once: the file stays busy for the
                // time of one line, and two open bridges do not block each
                // other for the whole session.
                File.AppendAllText(
                    FilePath,
                    Line(DateTime.Now, typ, itemName, similar) + Environment.NewLine,
                    new UTF8Encoding(false));
                Written++;
            }
            catch (Exception ex)
            {
                // RULE 2. Here we stay silent, and the reason stays in the hands of whoever tests.
                WhyItWasSilent = ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
