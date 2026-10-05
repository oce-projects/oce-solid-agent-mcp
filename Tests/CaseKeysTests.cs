using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Oce.Solid.Agent.Bench;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// EVERY CASE CARRIES ONLY KEYS THE BENCH READS (entry W3.1-bis.2, item 4),
    /// checked here before the bench runs.
    ///
    /// ── WHY ─────────────────────────────────────────────────────────────────
    ///
    /// The bench refuses a case with an unknown key as a bench error, and a
    /// bench error is found after a hundred minutes. The same rule, from the
    /// same code (`Bench/CaseKeys.cs`, compiled here too), runs on every case
    /// of the tree in a second: the bench is the last defence, not the first.
    ///
    /// ── WHAT IS SEEN FAILING, EVERY RUN ─────────────────────────────────────
    ///
    ///   * a made-up key injected into a real case, in every context of the
    ///     list (the case, a step, an expectation, a must-fail block, an
    ///     assembly, a component...): refused, with its path;
    ///   * a read key renamed in a real case, as a wrong translation would
    ///     leave it: refused;
    ///   * a frozen free key outside its own context, and a key of the
    ///     regression expectations inside a declared one: refused;
    ///   * malformed lists (an unknown key of the list itself, a child that
    ///     leads nowhere, a key both read and frozen...): refused;
    ///   * a frozen entry no case uses, and a read key no source reads, each
    ///     injected into a copy of the real list: reported.
    ///
    /// And a free note with the prefix is admitted everywhere: rejecting
    /// what is only unusual would be worse than not checking.
    ///
    /// The Italian keys these tests use are taken from the list at run time,
    /// so this source carries none.
    /// </summary>
    internal static class CaseKeysTests
    {
        /// <summary>
        /// THE FROZEN LIST ONLY SHRINKS. The free keys written before this net
        /// existed were frozen in Bench/case-keys.json; when one is renamed with
        /// the prefix (or moved into the prose of the case), its entry goes and
        /// this number goes down in the same commit. A new free key without the
        /// prefix would need both files changed, which is the point.
        /// 184 pairs (context, key) on 30/9/2026: 182 distinct keys in 302 cases.
        /// 0 since window 1 of the translation (W3.1-bis.4): every free note
        /// starts with the prefix, and the list stays empty.
        /// </summary>
        internal const int FrozenFreeCeiling = 0;

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        internal static void All()
        {
            Group("the bench refuses unknown case keys (W3.1-bis.2, item 4)");

            var root = Root();
            var listPath = Path.Combine(root, "Bench", CaseKeys.FileName);
            string listText;
            CaseKeys keys;
            try
            {
                listText = File.ReadAllText(listPath);
                keys = CaseKeys.Parse(listText);
            }
            catch (Exception ex) { Check("the list of case keys loads", false, listPath + ": " + ex.Message); return; }
            Check("the list of case keys loads", keys.Contexts.Count >= 10,
                  keys.Contexts.Count + " contexts, " + keys.FrozenCount + " frozen free keys");

            TheListRefusesWhatItDoesNotKnow();

            var unreadable = new List<string>();
            var cases = CasesOfTheTree(root, keys, unreadable);
            var dirty = new List<string>(unreadable);
            foreach (var c in cases)
            {
                var unknown = keys.Unknown(c.Body, c.Root);
                if (unknown.Count > 0) dirty.Add(c.Name + ": " + string.Join(", ", unknown));
            }
            Check("every case of the tree carries only keys the bench reads",
                  cases.Count >= 250 && dirty.Count == 0,
                  dirty.Count == 0
                      ? cases.Count + " cases, " + cases.Count(c => c.Root == keys.RegressionRoot) + " of them regression"
                      : dirty.Count + " cases refused, unreadable or with unknown keys (a free note starts with «" +
                        CaseKeys.FreePrefix + "»): " + string.Join(" | ", dirty.Take(6)));

            EveryContextRefusesAMadeUpKey(keys, cases);
            TheContextDecides(keys, cases);
            TheFrozenListOnlyShrinks(keys, listText, cases);
            EveryReadKeyHasAReader(keys, listText, root);
        }

        internal sealed class OneCase
        {
            public string Name;
            public string Root;
            public Dictionary<string, object> Body;
        }

        /// <summary>
        /// Every JSON object under Bench/cases/&lt;folder&gt;/: the regression folder
        /// with its own root, every other folder as declared cases. Arrays are
        /// plans for run_plans.js, and the bench skips them too.
        /// </summary>
        internal static List<OneCase> CasesOfTheTree(string root, CaseKeys keys, List<string> unreadable)
        {
            var all = new List<OneCase>();
            var folder = Path.Combine(root, "Bench", "cases");
            if (!Directory.Exists(folder)) return all;
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(x => x, StringComparer.Ordinal))
            {
                bool regression = string.Equals(Path.GetFileName(dir), "regression", StringComparison.Ordinal);
                foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal))
                {
                    object parsed;
                    try { parsed = Json.DeserializeObject(File.ReadAllText(f)); }
                    catch (Exception ex)
                    {
                        // said, never skipped: the bench would stop on it too
                        unreadable.Add(Path.GetFileName(dir) + "/" + Path.GetFileName(f) + ": not valid JSON, " + ex.Message);
                        continue;
                    }
                    var body = parsed as Dictionary<string, object>;
                    if (body == null) continue;
                    all.Add(new OneCase
                    {
                        Name = Path.GetFileName(dir) + "/" + Path.GetFileNameWithoutExtension(f),
                        Root = regression ? keys.RegressionRoot : keys.DeclaredRoot,
                        Body = body,
                    });
                }
            }
            return all;
        }

        /// <summary>The objects of one context inside a case, as references into it.</summary>
        internal static List<Dictionary<string, object>> ObjectsOf(
            CaseKeys keys, Dictionary<string, object> node, string context, string wanted)
        {
            var found = new List<Dictionary<string, object>>();
            Collect(keys, node, context, wanted, found);
            return found;
        }

        private static void Collect(CaseKeys keys, Dictionary<string, object> node, string context, string wanted,
                                    List<Dictionary<string, object>> found)
        {
            if (context == wanted) found.Add(node);
            var c = keys.Contexts[context];
            foreach (var child in c.Children)
            {
                object v;
                if (!node.TryGetValue(child.Key, out v) || v == null) continue;
                var one = v as Dictionary<string, object>;
                if (one != null) { Collect(keys, one, child.Value, wanted, found); continue; }
                var list = v as System.Collections.IList;
                if (list == null) continue;
                foreach (var item in list)
                {
                    var d = item as Dictionary<string, object>;
                    if (d != null) Collect(keys, d, child.Value, wanted, found);
                }
            }
        }

        private static Dictionary<string, object> DeepCopy(Dictionary<string, object> body)
        {
            return (Dictionary<string, object>)Json.DeserializeObject(Json.Serialize(body));
        }

        /// <summary>
        /// A made-up key in every context, and a read key renamed as a wrong
        /// translation would leave it: both refused, in a copy of a real case
        /// that uses that context. A free note with the prefix: admitted.
        /// </summary>
        private static void EveryContextRefusesAMadeUpKey(CaseKeys keys, List<OneCase> cases)
        {
            var notTried = new List<string>();
            var missed = new List<string>();
            var renamedMissed = new List<string>();
            var noteRefused = new List<string>();
            int tried = 0, renamed = 0;

            foreach (var context in keys.Contexts.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                // A CLEAN host: a case that is already refused would make every
                // count below say something about that case, not about the rule.
                OneCase host = null;
                foreach (var c in cases)
                    if (ObjectsOf(keys, c.Body, c.Root, context).Count > 0 && keys.Unknown(c.Body, c.Root).Count == 0)
                    { host = c; break; }
                if (host == null) { notTried.Add(context); continue; }
                tried++;

                var copy = DeepCopy(host.Body);
                var target = ObjectsOf(keys, copy, host.Root, context)[0];
                target["made_up_key"] = true;
                var unknown = keys.Unknown(copy, host.Root);
                if (unknown.Count != 1 || !unknown[0].EndsWith("made_up_key", StringComparison.Ordinal))
                    missed.Add(context + " (" + host.Name + ": " + string.Join(", ", unknown) + ")");

                target.Remove("made_up_key");
                target[CaseKeys.FreePrefix + "made_up_note"] = "a free note";
                if (keys.Unknown(copy, host.Root).Count != 0) noteRefused.Add(context);
                target.Remove(CaseKeys.FreePrefix + "made_up_note");

                var readHere = target.Keys.FirstOrDefault(k => keys.Contexts[context].Read.Contains(k));
                if (readHere == null) continue;
                renamed++;
                var value = target[readHere];
                target.Remove(readHere);
                target[readHere + "_translated"] = value;
                var afterRename = keys.Unknown(copy, host.Root);
                if (!afterRename.Any(p => p.EndsWith(readHere + "_translated", StringComparison.Ordinal)))
                    renamedMissed.Add(context + "/" + readHere);
            }

            Check("every context of the list is used by some case", notTried.Count == 0,
                  notTried.Count == 0 ? tried + " contexts" :
                  "no case uses: " + string.Join(", ", notTried) + " (a context nobody uses is a rule nobody checks)");
            Check("a made-up key is refused in every context, with its path", tried > 0 && missed.Count == 0,
                  missed.Count == 0 ? tried + " contexts, each refused exactly the injected key"
                                    : "NOT REFUSED: " + string.Join(" | ", missed));
            Check("a read key renamed as a wrong translation is refused", renamed > 0 && renamedMissed.Count == 0,
                  renamedMissed.Count == 0 ? renamed + " contexts, each with one of its read keys renamed"
                                           : "NOT REFUSED: " + string.Join(", ", renamedMissed));
            Check("a free note with the prefix is admitted in every context", noteRefused.Count == 0,
                  noteRefused.Count == 0 ? tried + " contexts" : "REFUSED in: " + string.Join(", ", noteRefused));
        }

        /// <summary>
        /// The context decides, not the key alone: a key of the case root (a
        /// documented one, or a frozen free key while the list had any)
        /// is refused inside an expectation, and a key that only the regression
        /// reads (the expectation compared with the measure before the change)
        /// is refused in a declared case, where nobody would read it.
        /// </summary>
        private static void TheContextDecides(CaseKeys keys, List<OneCase> cases)
        {
            CaseKeys.Context declared, regression;
            keys.Contexts.TryGetValue("expectation", out declared);
            keys.Contexts.TryGetValue("regression_expectation", out regression);
            var host = declared == null ? null
                : cases.FirstOrDefault(c => c.Root == keys.DeclaredRoot &&
                                            ObjectsOf(keys, c.Body, c.Root, declared.ContextName).Count > 0 &&
                                            keys.Unknown(c.Body, c.Root).Count == 0);
            // Since window 1 (W3.1-bis.4) the frozen list is empty: a documented
            // key of the root, admitted there and nowhere below, does the same job.
            var rootContext = keys.Contexts[keys.DeclaredRoot];
            var frozenOfTheCase = declared == null ? null
                : rootContext.FrozenFree.Concat(rootContext.Documented).Where(k => !declared.Admits(k))
                      .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
            var onlyRegression = declared == null || regression == null ? null
                : regression.Read.Where(k => !declared.Admits(k))
                      .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();

            if (host == null || frozenOfTheCase == null || onlyRegression == null)
            {
                Check("the context decides, not the key alone", false,
                      "nothing to try it on: a declared case with an expectation " + (host != null) +
                      ", a key of the case root " + (frozenOfTheCase != null) +
                      ", a key read only by the regression " + (onlyRegression != null));
                return;
            }

            var copy = DeepCopy(host.Body);
            copy[frozenOfTheCase] = "admitted at the root";
            bool rootAdmits = keys.Unknown(copy, host.Root).Count == 0;
            var expectation = ObjectsOf(keys, copy, host.Root, declared.ContextName)[0];
            expectation[frozenOfTheCase] = "refused in an expectation";
            bool frozenRefused = keys.Unknown(copy, host.Root).Count == 1;
            expectation.Remove(frozenOfTheCase);
            expectation[onlyRegression] = true;
            bool regressionRefused = keys.Unknown(copy, host.Root).Count == 1;

            Check("the context decides, not the key alone", rootAdmits && frozenRefused && regressionRefused,
                  "a key of the case root («" + frozenOfTheCase + "»): admitted at the root " + rootAdmits + ", refused in an expectation " +
                  frozenRefused + "; a key read only by the regression, refused in a declared expectation " +
                  regressionRefused);
        }

        /// <summary>
        /// The frozen list: exactly as long as the ceiling above, and every entry
        /// still used by a case (an unused entry is a place left free for a new
        /// key without the prefix). Seen failing on a copy of the real list with
        /// one entry more.
        /// </summary>
        private static void TheFrozenListOnlyShrinks(CaseKeys keys, string listText, List<OneCase> cases)
        {
            Check("the frozen list of free keys has the length written here", keys.FrozenCount == FrozenFreeCeiling,
                  keys.FrozenCount == FrozenFreeCeiling
                      ? FrozenFreeCeiling + " pairs (context, key)"
                      : keys.FrozenCount < FrozenFreeCeiling
                          ? "the list went down to " + keys.FrozenCount + ": lower FrozenFreeCeiling in Tests/CaseKeysTests.cs to match"
                          : "the list grew to " + keys.FrozenCount + " from " + FrozenFreeCeiling +
                            ": a new free note starts with «" + CaseKeys.FreePrefix + "», it is not frozen");

            var dead = DeadFrozenEntries(keys, cases);
            Check("every frozen free key is still used by a case", dead.Count == 0,
                  dead.Count == 0 ? keys.FrozenCount + " entries, all used"
                                  : "used by no case, remove them and lower the ceiling: " + string.Join(", ", dead.Take(10)));

            CaseKeys widened;
            try { widened = CaseKeys.Parse(WithExtraKey(listText, keys.DeclaredRoot, "frozen_free", "made_up_frozen_key")); }
            catch (Exception ex) { Check("a frozen entry no case uses is reported", false, "the widened list: " + ex.Message); return; }
            var deadThere = DeadFrozenEntries(widened, cases);
            Check("a frozen entry no case uses is reported", deadThere.Count == dead.Count + 1 &&
                                                              deadThere.Contains(keys.DeclaredRoot + "/made_up_frozen_key"),
                  "on a copy of the list with one entry more: " + string.Join(", ", deadThere.Take(3)));
        }

        internal static List<string> DeadFrozenEntries(CaseKeys keys, List<OneCase> cases)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in cases) keys.CollectFrozenUse(c.Body, c.Root, used);
            var dead = new List<string>();
            foreach (var ctx in keys.Contexts.Values.OrderBy(x => x.ContextName, StringComparer.Ordinal))
                foreach (var k in ctx.FrozenFree.OrderBy(x => x, StringComparer.Ordinal))
                    if (!used.Contains(ctx.ContextName + "/" + k)) dead.Add(ctx.ContextName + "/" + k);
            return dead;
        }

        /// <summary>
        /// Every read key appears as a literal in the bench sources. A key the
        /// cases and the list call one way and the reader another is the rename
        /// done on one side only; the loader would still admit the case, and
        /// the key would be read by nobody. Seen failing with a made-up read key.
        /// </summary>
        private static void EveryReadKeyHasAReader(CaseKeys keys, string listText, string root)
        {
            var sources = BenchSources(root);
            var missing = keys.ReadKeysWithoutReader(sources);
            Check("every read key of the list is read by the bench sources", sources.Count >= 5 && missing.Count == 0,
                  missing.Count == 0 ? sources.Count + " sources, " + keys.Contexts.Values.Sum(c => c.Read.Count) + " read keys"
                                     : "READ BY NOBODY (renamed on one side only?): " + string.Join(", ", missing));

            CaseKeys widened;
            try { widened = CaseKeys.Parse(WithExtraKey(listText, "step", "read", "made_up_read_key")); }
            catch (Exception ex) { Check("a read key without a reader is reported", false, "the widened list: " + ex.Message); return; }
            var missingThere = widened.ReadKeysWithoutReader(sources);
            Check("a read key without a reader is reported",
                  missingThere.Count == missing.Count + 1 && missingThere.Contains("step/made_up_read_key"),
                  "on a copy of the list with one read key more: " + string.Join(", ", missingThere));

            // A KEY THAT SURVIVES ONLY IN A COMMENT OR IN A WRITE IS READ BY
            // NOBODY. Seen on 1/10/2026: the reader of «document» renamed alone
            // stayed green, because a doc comment still quoted the key.
            const char Q = '"';
            Func<string, bool> readIn = src => CaseKeys.IsReadIn(ReflectionTests.WithoutComments(src), "fake_key");
            var shapes = new[]
            {
                Tuple.Create("/// { " + Q + "fake_key" + Q + ": a path }", false),
                Tuple.Create("// Testo(x, " + Q + "fake_key" + Q + ")", false),
                Tuple.Create("/* Testo(x, " + Q + "fake_key" + Q + ") */", false),
                Tuple.Create("var c = new Dictionary<string, object> { [" + Q + "fake_key" + Q + "] = 5.0 };", false),
                Tuple.Create("var c = new Dictionary<string, object> { { " + Q + "fake_key" + Q + ", 5.0 } };", false),
                Tuple.Create("string v = Testo(x, " + Q + "fake_key" + Q + ");", true),
                Tuple.Create("if (x.ContainsKey(" + Q + "fake_key" + Q + ")) y = x[" + Q + "fake_key" + Q + "];", true),
                Tuple.Create("bool same = x[" + Q + "fake_key" + Q + "] == null;", true),
                Tuple.Create("internal const string Field = " + Q + "fake_key" + Q + ";", true),
            };
            var wrongShapes = shapes.Where(s => readIn(s.Item1) != s.Item2)
                                    .Select(s => "«" + s.Item1 + "» " + (s.Item2 ? "not seen as a read" : "seen as a read")).ToList();
            Check("a key quoted only in a comment or written only in an initializer has no reader (fake sources)",
                  wrongShapes.Count == 0,
                  wrongShapes.Count == 0 ? shapes.Length + " shapes as expected" : string.Join("; ", wrongShapes));
        }

        /// <summary>
        /// The sources of the bench that read cases, without their comments:
        /// every Bench/*.cs but the two of this net.
        /// </summary>
        internal static List<string> BenchSources(string root)
        {
            var list = new List<string>();
            var folder = Path.Combine(root, "Bench");
            if (!Directory.Exists(folder)) return list;
            foreach (var f in Directory.GetFiles(folder, "*.cs").OrderBy(x => x, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(f);
                if (name == "CaseKeys.cs" || name == "TestLedger.cs") continue;
                list.Add(ReflectionTests.WithoutComments(File.ReadAllText(f)));
            }
            return list;
        }

        /// <summary>
        /// The list with one more key in one category of one context: appended
        /// to the strings of "read", added as a key elsewhere.
        /// </summary>
        private static string WithExtraKey(string listText, string context, string category, string key)
        {
            var top = (Dictionary<string, object>)Json.DeserializeObject(listText);
            var ctx = (Dictionary<string, object>)((Dictionary<string, object>)top["contexts"])[context];
            object v;
            ctx.TryGetValue(category, out v);
            if (category == "read")
            {
                var strings = new List<object>();
                var old = v as System.Collections.IList;
                if (old != null) foreach (var x in old) strings.Add(x);
                strings.Add(key);
                ctx[category] = strings;
            }
            else
            {
                var map = v as Dictionary<string, object>;
                if (map == null) { map = new Dictionary<string, object>(); ctx[category] = map; }
                map[key] = true;
            }
            return Json.Serialize(top);
        }

        /// <summary>
        /// The list follows its own rule: what it does not know, it refuses.
        /// A small valid list loads (seen green), and each broken copy of it
        /// throws (seen red).
        /// </summary>
        private static void TheListRefusesWhatItDoesNotKnow()
        {
            const string valid =
                "{\"format\":1,\"roots\":{\"declared\":\"top\",\"regression\":\"other\"}," +
                "\"contexts\":{\"top\":{\"read\":[\"items\"],\"documented\":{\"note\":true}," +
                "\"children\":{\"items\":\"item\"},\"frozen_free\":{\"old_note\":true}}," +
                "\"item\":{\"read\":[\"size\"]},\"other\":{\"read\":[\"steps\"]}}}";
            string whyValid = null;
            try
            {
                var small = CaseKeys.Parse(valid);
                var sample = (Dictionary<string, object>)Json.DeserializeObject(
                    "{\"items\":[{\"size\":1},{\"size\":2,\"colour\":3}],\"old_note\":\"x\",\"_free\":1,\"stray\":2}");
                var unknown = small.Unknown(sample, "top");
                if (unknown.Count != 2 || unknown[0] != "items[2].colour" || unknown[1] != "stray")
                    whyValid = "the small list found " + string.Join(", ", unknown);
            }
            catch (Exception ex) { whyValid = ex.Message; }
            Check("a small list loads and names each unknown key by path", whyValid == null,
                  whyValid ?? "items[2].colour and stray, and nothing else");

            var broken = new Dictionary<string, string>
            {
                { "an unknown key of the list", valid.Replace("\"format\":1,", "\"format\":1,\"comment\":\"x\",") },
                { "an unknown key of a context", valid.Replace("\"read\":[\"size\"]", "\"reads\":[\"size\"]") },
                { "a child that leads to no context", valid.Replace("\"items\":\"item\"", "\"items\":\"nowhere\"") },
                { "a key both read and frozen", valid.Replace("\"old_note\":true", "\"items\":true") },
                { "a frozen key with the free prefix", valid.Replace("\"old_note\":true", "\"_old_note\":true") },
                { "a child neither read nor documented", valid.Replace("\"items\":\"item\"", "\"items\":\"item\",\"size\":\"item\"") },
                { "another format", valid.Replace("\"format\":1", "\"format\":2") },
                { "a documented key set to false", valid.Replace("\"note\":true", "\"note\":false") },
                { "a read key that is not a string", valid.Replace("\"read\":[\"size\"]", "\"read\":[3]") },
                { "a read key named twice", valid.Replace("\"read\":[\"size\"]", "\"read\":[\"size\",\"size\"]") },
                { "read written as an object", valid.Replace("\"read\":[\"size\"]", "\"read\":{\"size\":true}") },
                { "a root that is not a context", valid.Replace("\"regression\":\"other\"", "\"regression\":\"missing\"") },
            };
            var accepted = new List<string>();
            foreach (var b in broken)
            {
                try { CaseKeys.Parse(b.Value); accepted.Add(b.Key); }
                catch (FormatException) { }
            }
            Check("a malformed list of case keys is refused, not emptied", accepted.Count == 0,
                  accepted.Count == 0 ? broken.Count + " broken lists, each refused"
                                      : "ACCEPTED: " + string.Join(", ", accepted));
        }
    }

    /// <summary>
    /// THE BENCH REPORT NAMES EVERY TEST BY FILE:METHOD:ORDINAL (entry W3.1-bis.2,
    /// item 7), checked without SOLIDWORKS.
    ///
    /// The identity is computed by `Bench/TestLedger.cs` from the caller
    /// information of the compiler and a scan of the source. Three things are
    /// proved here, every run:
    ///
    ///   * the scan puts each recording call in the member the COMPILER names:
    ///     calls made from this very file (a method, a lambda, a local
    ///     function, a property, a constructor, a field initializer) are
    ///     resolved on this file's source, and must agree;
    ///   * the refusals fire: a call in a comment or a string is not a call, a
    ///     line without a call, a member named otherwise and an unreadable
    ///     source leave the test UNRESOLVED with the reason, never guessed;
    ///   * every recording call of the bench sources sits in a named member,
    ///     one per line, and the bench builds its ledger with exactly the
    ///     recording methods its sources declare.
    /// </summary>
    internal static class BenchReportTests
    {
        private sealed class Marked
        {
            public string Label;
            public string File;
            public string Member;
            public int Line;
        }

        private static readonly List<Marked> Marks = new List<Marked>();

        /// <summary>The recording method of these tests: the same attributes as the bench recorders.</summary>
        private static void Mark(string label,
                                 [CallerFilePath] string file = "",
                                 [CallerMemberName] string member = "",
                                 [CallerLineNumber] int line = 0)
        {
            Marks.Add(new Marked { Label = label, File = file, Member = member, Line = line });
        }

        private static void ThreeMarks()
        {
            Mark("first");
            Mark("second");
            Mark("third");
        }

        private static void MarksInALambdaAndALocalFunction()
        {
            Action inLambda = () => Mark("lambda");
            inLambda();
            void Local() { Mark("local"); }
            Local();
        }

        private static int MarkedProperty
        {
            get { Mark("getter"); return 1; }
        }

        private sealed class Marker
        {
            internal Marker() { Mark("constructor"); }
        }

        private static readonly Action MarkedField = () => Mark("field");

        internal static void All()
        {
            Group("the bench report names every test by file:method:ordinal (W3.1-bis.2, item 7)");
            TheScanOfAWrittenSource();
            TheScanAgreesWithTheCompiler();
            TheLedger();
            TheBenchSources();
        }

        private const string Probe = "Probe.cs";

        /// <summary>A source written on purpose, with every shape the scan must know.</summary>
        private static readonly string[] ProbeLines =
        {
            "namespace N",
            "{",
            "    internal sealed class Probe",
            "    {",
            "        private int _field = 1;",
            "        internal Probe() { Mark(\"c1\"); }",
            "        static Probe() { Mark(\"s1\"); }",
            "        private static void One()",
            "        {",
            "            Mark(\"a\");",
            "            // Mark(\"in a comment\");",
            "            /* Mark(\"in a block comment\"); */",
            "            var s = \"Mark(\\\"in a string\\\")\";",
            "            var v = @\"Mark(\"\"in a verbatim string\"\")\";",
            "            var q = '\"';",
            "            Mark(\"b\");",
            "            System.Action l = () => Mark(\"lambda\");",
            "            void Local() { Mark(\"local\"); }",
            "            var h = $\"{Mark(\"in a hole\")} and {s}\";",
            "        }",
            "        private static int Prop { get { Mark(\"p\"); return 1; } }",
            "        private static readonly System.Action Field = () => Mark(\"f\");",
            "        private static T Generic<T>(T x) { Mark(\"g\"); return x; }",
            "        private static void Arrow() => Mark(\"arrow\");",
            "        private int this[int i] { get { Mark(\"indexer\"); return i; } }",
            "        private static System.Collections.Generic.Dictionary<string,",
            "            System.Collections.Generic.List<int>> Multi()",
            "        {",
            "            Mark(\"m1\"); Mark(\"m2\");",
            "            return null;",
            "        }",
            "        private sealed class Inner",
            "        {",
            "            private static void One() { Mark(\"inner one\"); }",
            "        }",
            "        private static void Mark(string label,",
            "            [System.Runtime.CompilerServices.CallerMemberName] string member = \"\") { }",
            "    }",
            "}",
        };

        private static int LineOf(string fragment)
        {
            for (int i = 0; i < ProbeLines.Length; i++)
                if (ProbeLines[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) return i + 1;
            return -1;
        }

        /// <summary>
        /// Member and ordinal of every call of the written source, and the calls
        /// that are not calls. The call inside an interpolation hole is a real
        /// call the scan does not see: it is listed here so that the limit is
        /// written down, and a test recorded from such a line is unresolved.
        /// </summary>
        private static void TheScanOfAWrittenSource()
        {
            var scan = SourceScan.Of(string.Join("\n", ProbeLines), new[] { "Mark" });
            var expected = new[]
            {
                new { Fragment = "Mark(\"c1\")", Member = ".ctor", Ordinal = 1 },
                new { Fragment = "Mark(\"s1\")", Member = ".cctor", Ordinal = 1 },
                new { Fragment = "Mark(\"a\")", Member = "One", Ordinal = 1 },
                new { Fragment = "Mark(\"b\")", Member = "One", Ordinal = 2 },
                new { Fragment = "Mark(\"lambda\")", Member = "One", Ordinal = 3 },
                new { Fragment = "Mark(\"local\")", Member = "One", Ordinal = 4 },
                new { Fragment = "Mark(\"p\")", Member = "Prop", Ordinal = 1 },
                new { Fragment = "Mark(\"f\")", Member = "Field", Ordinal = 1 },
                new { Fragment = "Mark(\"g\")", Member = "Generic", Ordinal = 1 },
                new { Fragment = "Mark(\"arrow\")", Member = "Arrow", Ordinal = 1 },
                new { Fragment = "Mark(\"m1\")", Member = "Multi", Ordinal = 1 },
                // another method One, in a nested class: the count is shared by name
                new { Fragment = "Mark(\"inner one\")", Member = "One", Ordinal = 5 },
            };
            var wrong = new List<string>();
            foreach (var e in expected)
            {
                var call = scan.CallAt(LineOf(e.Fragment));
                var got = call == null ? "no call" : (call.In == null ? "no member" : call.In.Identifier) + ":" + call.Ordinal;
                if (got != e.Member + ":" + e.Ordinal) wrong.Add(e.Fragment + " -> " + got + ", expected " + e.Member + ":" + e.Ordinal);
            }
            Check("the scan names member and ordinal of every written call", wrong.Count == 0,
                  wrong.Count == 0 ? expected.Length + " calls: constructor, static constructor, lambda, local function, " +
                                     "property, field, generic method, expression body, a declaration over two lines, " +
                                     "and a second method of the same name sharing the count"
                                   : string.Join(" | ", wrong));

            var notCalls = new[] { "in a comment", "in a block comment", "in a string", "in a verbatim string", "in a hole" };
            var seen = notCalls.Where(f => scan.CallAt(LineOf(f)) != null).ToList();
            var indexer = scan.CallAt(LineOf("Mark(\"indexer\")"));
            var twice = scan.LinesWithTwoCalls();
            var declared = scan.RecordersDeclared();
            bool ok = seen.Count == 0 && indexer != null && indexer.In == null &&
                      twice.Count == 1 && twice[0] == LineOf("Mark(\"m1\")") &&
                      declared.Count == 1 && declared[0] == "Mark" && scan.Calls.Count == 14;
            Check("non-calls are not counted, unknown shapes are said", ok,
                  "counted in comments, strings or holes: " + (seen.Count == 0 ? "none" : string.Join(", ", seen)) +
                  "; the indexer call in no member: " + (indexer != null && indexer.In == null) +
                  "; lines with two calls: " + string.Join(",", twice) +
                  "; recorders declared: " + string.Join(",", declared) + "; calls: " + scan.Calls.Count);
        }

        /// <summary>The source next to this build first (Tests/ of the checkout), then the path the compiler gave.</summary>
        private static string ReadSource(string callerFile)
        {
            if (string.IsNullOrEmpty(callerFile)) return null;
            var here = Path.Combine(Root(), "Tests", Path.GetFileName(callerFile));
            if (File.Exists(here)) return File.ReadAllText(here);
            return File.Exists(callerFile) ? File.ReadAllText(callerFile) : null;
        }

        /// <summary>
        /// The calls made from this file, resolved on this file: the scan must
        /// put each in the member the compiler named, with ordinals in order.
        /// Then a wrong member and a line without a call: unresolved, seen red.
        /// </summary>
        private static void TheScanAgreesWithTheCompiler()
        {
            Marks.Clear();
            ThreeMarks();
            MarksInALambdaAndALocalFunction();
            var p = MarkedProperty;
            var m = new Marker();
            MarkedField();

            var ledger = new TestLedger(new[] { nameof(Mark) }, ReadSource);
            var results = Marks.Select(x => ledger.Record(x.File, x.Member, x.Line, TestLedger.Ok, x.Label, null)).ToList();
            var unresolved = results.Where(r => r.Unresolved != null).Select(r => r.Caption + ": " + r.Unresolved).ToList();
            var ids = string.Join(", ", results.Select(r => r.Method + ":" + r.Ordinal));
            const string expectedIds = "ThreeMarks:1, ThreeMarks:2, ThreeMarks:3, " +
                                       "MarksInALambdaAndALocalFunction:1, MarksInALambdaAndALocalFunction:2, " +
                                       "MarkedProperty:1, .ctor:1, MarkedField:1";
            Check("the scan puts every call in the member the compiler names", results.Count == 8 &&
                                                                               unresolved.Count == 0 && ids == expectedIds,
                  unresolved.Count > 0 ? string.Join(" | ", unresolved) : ids);

            var first = Marks[0];
            var wrongMember = ledger.Record(first.File, "SomeOtherMethod", first.Line, TestLedger.Ok, "wrong member", null);
            var noCall = ledger.Record(first.File, first.Member, first.Line - 1, TestLedger.Ok, "no call", null);
            Check("a wrong member or a line without a call is unresolved",
                  wrongMember.Unresolved != null && wrongMember.Unresolved.Contains("compiler says") &&
                  noCall.Unresolved != null && noCall.Unresolved.Contains("no recording call") &&
                  !wrongMember.Ordinal.HasValue && !noCall.Ordinal.HasValue,
                  (wrongMember.Unresolved ?? "RESOLVED") + " | " + (noCall.Unresolved ?? "RESOLVED"));
        }

        /// <summary>
        /// Identities, occurrences, subjects, the counts of a case and the JSON,
        /// on the written source.
        /// </summary>
        private static void TheLedger()
        {
            var text = string.Join("\n", ProbeLines);
            var ledger = new TestLedger(new[] { "Mark" }, f => Path.GetFileName(f) == Probe ? text : null);
            var file = Path.Combine("C:\\anywhere", Probe);
            int a = LineOf("Mark(\"a\")"), b = LineOf("Mark(\"b\")");

            var one = ledger.Record(file, "One", a, TestLedger.Ok, "a", "group");
            var again = ledger.Record(file, "One", a, TestLedger.Ok, "a again", "group");
            TestLedger.Test inCase, inCaseError, otherCase, aboutIt;
            bool hadErrors;
            using (var scope = ledger.OpenCase("case_one", "folder", "declared"))
            {
                inCase = ledger.Record(file, "One", b, TestLedger.Ok, "b", "group");
                inCaseError = ledger.Record(file, "One", a, TestLedger.BenchError, "a, a bench error", "group");
                ledger.ExpectationAttempted();
                ledger.ExpectationAttempted();
                ledger.ExpectationChecked();
                hadErrors = scope.HasBenchErrors;
                scope.Outcome = "bench_error";
            }
            using (var scope = ledger.OpenCase("case_two", "folder", "declared"))
            {
                otherCase = ledger.Record(file, "One", b, TestLedger.Failed, "b", "group");
                scope.Outcome = "red";
            }
            using (ledger.About("the_subject")) aboutIt = ledger.Record(file, "One", b, TestLedger.Ok, "b", "group");
            ledger.ExpectationChecked();   // outside a case: counted nowhere
            var hole = ledger.Record(file, "One", LineOf("in a hole"), TestLedger.Ok, "hole", "group");
            var missing = ledger.Record("C:\\anywhere\\Missing.cs", "One", 1, TestLedger.Ok, "missing", "group");

            var got = string.Join(" ", new[] { one, again, inCase, inCaseError, otherCase, aboutIt }.Select(t => t.Id));
            const string expected = "Probe.cs:One:1 Probe.cs:One:1#2 Probe.cs:One:2@case_one Probe.cs:One:1@case_one " +
                                    "Probe.cs:One:2@case_two Probe.cs:One:2@the_subject";
            Check("an identity carries ordinal, subject and occurrence", got == expected, got);

            var c1 = ledger.Cases[0];
            var c2 = ledger.Cases[1];
            bool counts = ledger.Cases.Count == 2 && c1.ExpectationsAttempted == 2 && c1.ExpectationsChecked == 1 &&
                          c1.Tests[TestLedger.Ok] == 1 && c1.Tests[TestLedger.BenchError] == 1 && hadErrors &&
                          c1.Outcome == "bench_error" && c2.Tests[TestLedger.Failed] == 1 && c2.Outcome == "red" &&
                          c2.ExpectationsAttempted == 0;
            Check("a case counts its expectations and tests, no leaks", counts,
                  "case_one: " + c1.ExpectationsAttempted + " attempted, " + c1.ExpectationsChecked + " checked, " +
                  c1.Tests[TestLedger.Ok] + " ok, " + c1.Tests[TestLedger.BenchError] + " bench errors, " + c1.Outcome +
                  "; case_two: " + c2.Tests[TestLedger.Failed] + " failed, " + c2.Outcome);

            Check("unseen calls and unreadable sources count as unresolved",
                  hole.Unresolved != null && missing.Unresolved != null && missing.Unresolved.Contains("cannot be read") &&
                  ledger.UnresolvedCount == 2,
                  (hole.Unresolved ?? "RESOLVED") + " | " + (missing.Unresolved ?? "RESOLVED") +
                  " | unresolved " + ledger.UnresolvedCount);

            string why = null;
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                    .DeserializeObject(ledger.Json("full", "0.0.0.0", new DateTime(2026, 9, 30))) as Dictionary<string, object>;
                var tests = json == null ? null : json["tests"] as object[];
                var cases = json == null ? null : json["cases"] as object[];
                if (tests == null || tests.Length != ledger.Tests.Count) why = "tests: " + (tests == null ? "absent" : tests.Length.ToString());
                else if (cases == null || cases.Length != 2) why = "cases: " + (cases == null ? "absent" : cases.Length.ToString());
                else if (Convert.ToInt32(json["identities_unresolved"]) != 2) why = "identities_unresolved: " + json["identities_unresolved"];
                else if (((Dictionary<string, object>)tests[0])["id"] as string != "Probe.cs:One:1") why = "the first id";
                else if (Convert.ToInt32(((Dictionary<string, object>)cases[0])["expectations_checked"]) != 1) why = "expectations_checked";
            }
            catch (Exception ex) { why = ex.Message; }
            Check("the ledger is valid JSON with every test and every case", why == null,
                  why ?? ledger.Tests.Count + " tests, 2 cases, 2 unresolved");
        }

        /// <summary>
        /// The bench sources: every recording call in a named member and alone
        /// on its line, and the ledger of Program.cs built with the recording
        /// methods the sources declare, no more and no fewer.
        /// </summary>
        private static void TheBenchSources()
        {
            var folder = Path.Combine(Root(), "Bench");
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.cs").OrderBy(x => x, StringComparer.Ordinal).ToList()
                                                 : new List<string>();
            var texts = files.ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f));
            var declared = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var t in texts.Values)
                foreach (var r in SourceScan.Of(t, new string[0]).RecordersDeclared()) declared.Add(r);

            int calls = 0;
            var outside = new List<string>();
            var twice = new List<string>();
            var sameIdentity = new List<string>();
            foreach (var t in texts)
            {
                var scan = SourceScan.Of(t.Value, declared);
                calls += scan.Calls.Count;
                outside.AddRange(scan.Calls.Where(c => c.In == null).Select(c => t.Key + ":" + c.Line));
                twice.AddRange(scan.LinesWithTwoCalls().Select(l => t.Key + ":" + l));
                sameIdentity.AddRange(scan.Calls.Where(c => c.In != null)
                    .GroupBy(c => c.In.Identifier + ":" + c.Ordinal).Where(g => g.Count() > 1).Select(g => t.Key + ":" + g.Key));
            }
            Check("every bench recording call: named member, own line",
                  declared.Count >= 4 && calls >= 200 && outside.Count == 0 && twice.Count == 0 && sameIdentity.Count == 0,
                  declared.Count + " recording methods (" + string.Join(", ", declared) + "), " + calls + " calls" +
                  (outside.Count > 0 ? "; IN NO MEMBER: " + string.Join(", ", outside.Take(5)) : "") +
                  (twice.Count > 0 ? "; TWO ON ONE LINE: " + string.Join(", ", twice.Take(5)) : "") +
                  (sameIdentity.Count > 0 ? "; ONE IDENTITY, TWO CALLS: " + string.Join(", ", sameIdentity.Take(5)) : ""));

            string program;
            texts.TryGetValue("Program.cs", out program);
            var built = program == null ? null
                : Regex.Match(program, @"new\s+TestLedger\s*\(\s*new\s*\[\]\s*\{([^}]*)\}");
            var named = new SortedSet<string>(StringComparer.Ordinal);
            if (built != null && built.Success)
                foreach (Match n in Regex.Matches(built.Groups[1].Value, @"nameof\(\s*(\w+)\s*\)")) named.Add(n.Groups[1].Value);
            Check("the bench ledger uses exactly the declared recorders",
                  named.Count > 0 && named.SetEquals(declared),
                  built == null || !built.Success
                      ? "the construction of the ledger is not in Bench/Program.cs any more: this check must follow it"
                      : "built with " + string.Join(", ", named) + "; declared " + string.Join(", ", declared));
        }
    }
}
