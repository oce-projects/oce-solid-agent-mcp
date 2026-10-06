using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// api_search AND api_signature (entry U1.1).
    ///
    /// ── WHAT IS CHECKED, AND AGAINST WHAT ──────────────────────────────
    ///
    ///   * ApiSignatureMatchesInventory: the C# formatter of
    ///     Oce.Solid.Agent.Core\Api\ApiCatalog.cs against the inventory that
    ///     Checks\api-inventory.ps1 writes from PowerShell, reading the
    ///     same assembly with a different engine. 13,945 exposed members plus
    ///     983 hidden, zero differences. It checks the FORMATTER (overloads,
    ///     `&amp;` parameters, properties), not the truth of the API.
    ///   * the truth comes from the point checks of the entry's "done when":
    ///     Check3 is a property, InsertOffsetSurface is flagged Void, the two
    ///     real SewRefSurface signatures, AddMate5's AlignFromEnum with the
    ///     ambiguous value 1, CreateMateData's Type not tabulated,
    ///     swFeatureNameID_e from 0 to 118, IModelDoc.EditDelete refused.
    ///   * api_knowledge.json names only members that exist — and the check
    ///     is seen biting on an invented name, here, at every run.
    ///   * every hidden interface has all its names on its sisters — and the
    ///     check is seen biting on IMassProperty, which must stay exposed.
    ///   * the two tools answer through the real bridge, on stdio, with
    ///     SOLIDWORKS closed.
    ///
    /// ── WHY THE LIBRARY IS LOADED HERE AND THE INTEROP IS NOT REFERENCED ─
    ///
    /// This project must not reference SolidWorks.Interop (its first check).
    /// The library is loaded from its bytes and the interop is resolved at
    /// run time from the SOLIDWORKS installation, the same way the bridge does.
    /// Where SOLIDWORKS is not installed the group says so ("not runnable
    /// here") instead of pretending.
    /// </summary>
    internal static class ApiCatalogContracts
    {
        // The counts of the interop these contracts were written against
        // (SOLIDWORKS 2021 SP5.1, sldworks 29.5.1.1). Another SOLIDWORKS
        // changes them: read the difference before updating the numbers.
        private const int ExpectedExposed = 13945;
        private const int ExpectedHidden = 983;

        private static Type _catalog;
        private static bool _resolverOn;

        internal static void Run()
        {
            Group("api_search and api_signature: the whole API by reflection (U1.1)");

            var redist = WhereTheInteropIs();
            if (redist == null)
            {
                NotHere("the API catalog matches the interop",
                       "SOLIDWORKS is not installed here: without api\\redist\\SolidWorks.Interop.* " +
                       "there is no API to reflect on.");
                return;
            }

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                Check("the library is built", false,
                      library + " is missing, and SOLIDWORKS is installed (" + redist + "): build it");
                return;
            }

            try { _catalog = LoadCatalog(library, redist); }
            catch (Exception ex)
            {
                Check("ApiCatalog loads", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }
            if (_catalog == null)
            {
                Check("ApiCatalog exists in the library", false,
                      "no type Oce.Solid.Agent.Core.Api.ApiCatalog in " + library);
                return;
            }

            InventoryMatches(redist);
            PointChecks();
            Knowledge();
            HiddenInterfaces();
            ThroughTheBridge();
        }

        // ─────────────────────────────────────────────────────────────────

        private static Type LoadCatalog(string library, string redist)
        {
            if (!_resolverOn)
            {
                AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                {
                    var name = new AssemblyName(a.Name).Name;
                    if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                    var file = Path.Combine(redist, name + ".dll");
                    // LoadFrom and not Load(bytes): the interop is on the
                    // local disk, and a Location is what ApiCatalog uses to
                    // find swmotionstudy next to sldworks.
                    return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                };
                _resolverOn = true;
            }
            // From the bytes: the repository is on a network drive, where
            // LoadFrom answers NotSupportedException (see Load.cs).
            var asm = Assembly.Load(File.ReadAllBytes(library));
            return asm.GetType("Oce.Solid.Agent.Core.Api.ApiCatalog", false);
        }

        private static object Call(string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(_catalog, method, BindingFlags.Public | BindingFlags.Static);
            if (m == null) throw new MissingMethodException("ApiCatalog." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static string Field(object answer, string name)
        {
            // A missing field reads as null, and null reads as «not an error»:
            // the log turns that silence red at the end of the run (W3.1-bis.2).
            var f = ReflectionLog.Global.Field(answer.GetType(), name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            return f == null ? null : Convert.ToString(f.GetValue(answer));
        }

        private static string KnowledgeJson()
        {
            var file = Path.Combine(Root(), "Tools", "api_knowledge.json");
            return File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : null;
        }

        private static string Sig(string name, out bool error)
        {
            var a = Call("Signature", name, KnowledgeJson());
            error = string.Equals(Field(a, "IsError"), "True", StringComparison.OrdinalIgnoreCase);
            return Field(a, "Text") ?? "";
        }

        private static string Search(string query, int limit, out bool error)
        {
            var a = Call("Search", query, limit, KnowledgeJson());
            error = string.Equals(Field(a, "IsError"), "True", StringComparison.OrdinalIgnoreCase);
            return Field(a, "Text") ?? "";
        }

        private static string Const(string name)
        {
            var f = ReflectionLog.Global.Field(_catalog, name, BindingFlags.Public | BindingFlags.Static);
            return f == null ? null : Convert.ToString(f.GetValue(null));
        }

        // ─────────────────────────────────────────────────────────────────
        //  ApiSignatureMatchesInventory
        // ─────────────────────────────────────────────────────────────────

        private static void InventoryMatches(string redist)
        {
            var script = Path.Combine(Root(), "Checks", "api-inventory.ps1");
            var dir = Path.Combine(Path.GetTempPath(), "api_inventory_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var members = Path.Combine(dir, "members.txt");
            var enums = Path.Combine(dir, "enums.txt");
            try
            {
                string output;
                int code = RunPowerShell(script, "-Members \"" + members + "\" -Enums \"" + enums +
                                                 "\" -Redist \"" + redist + "\"", out output);
                if (code != 0 || !File.Exists(members) || !File.Exists(enums))
                {
                    Check("ApiSignatureMatchesInventory", false,
                          "api-inventory.ps1 exited " + code + ": " + Truncated(output));
                    return;
                }

                var ps = File.ReadAllLines(members, Encoding.UTF8).Where(l => l.Length > 0).ToArray();
                var cs = (string[])Call("InventoryLines");
                Compare("ApiSignatureMatchesInventory: the C# formatter and PowerShell agree on every member",
                        cs, ps);

                int exposed = (int)Call("ExposedMemberCount");
                int hidden = (int)Call("HiddenMemberCount");
                Check("exposed + hidden = the whole inventory, " + ExpectedExposed + " + " + ExpectedHidden,
                      exposed == ExpectedExposed && hidden == ExpectedHidden && exposed + hidden == ps.Length,
                      exposed + " exposed + " + hidden + " hidden = " + (exposed + hidden) +
                      ", PowerShell inventory " + ps.Length + " lines");

                var psE = File.ReadAllLines(enums, Encoding.UTF8).Where(l => l.Length > 0).ToArray();
                var csE = (string[])Call("EnumInventoryLines");
                Compare("the swconst enums: the C# formatter and PowerShell agree", csE, psE);

                // THE COMPARISON HAS TEETH: one character changed in one line
                // must make it fail. A comparison that cannot fail compares
                // nothing (a check never seen firing is not a check).
                var broken = (string[])cs.Clone();
                int i = Array.FindIndex(broken, l => l.StartsWith("IEdge.IGetTwoAdjacentFaces2(", StringComparison.Ordinal));
                if (i >= 0) broken[i] = broken[i].Replace("Face2& Face1", "Face2 Face1");
                Check("...and it fails when one `&` is lost (seen biting)",
                      i >= 0 && Differences(broken, ps).Count == 2,
                      i < 0 ? "IEdge.IGetTwoAdjacentFaces2 not in the C# output" :
                              Differences(broken, ps).Count + " differences on the tampered copy");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void Compare(string name, string[] cs, string[] ps)
        {
            var diff = Differences(cs, ps);
            Check(name, diff.Count == 0,
                  diff.Count == 0 ? cs.Length + " lines, zero differences"
                                  : diff.Count + " differences, first: " + string.Join(" | ", diff.Take(4)));
        }

        private static List<string> Differences(string[] cs, string[] ps)
        {
            var a = new HashSet<string>(cs, StringComparer.Ordinal);
            var b = new HashSet<string>(ps, StringComparer.Ordinal);
            var diff = new List<string>();
            diff.AddRange(a.Where(l => !b.Contains(l)).Select(l => "only C#: " + l));
            diff.AddRange(b.Where(l => !a.Contains(l)).Select(l => "only PowerShell: " + l));
            if (cs.Length != a.Count) diff.Add("C# has duplicate lines");
            if (ps.Length != b.Count) diff.Add("PowerShell has duplicate lines");
            return diff;
        }

        private static int RunPowerShell(string script, string args, out string output)
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " + args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                var outp = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(180000)) { try { p.Kill(); } catch { } output = "timeout"; return -1; }
                output = outp + err.Result;
                return p.ExitCode;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  The point checks of "done when"
        // ─────────────────────────────────────────────────────────────────

        private static void PointChecks()
        {
            bool err;

            var fid = Sig("swFeatureNameID_e", out err);
            Check("swFeatureNameID_e goes from swFmChamfer=0 to swFmSMGusset=118",
                  !err && fid.Contains("from swFmChamfer=0 to swFmSMGusset=118"), FirstLine(fid));

            var check3 = Sig("IBody2.Check3", out err);
            Check("api_signature(IBody2.Check3) says property and -> FaultEntity",
                  !err && FirstLine(check3).Contains("property") &&
                  check3.Contains("IBody2.get_Check3() -> FaultEntity"), Truncated(check3));

            var offset = Sig("IModelDoc2.InsertOffsetSurface", out err);
            Check("api_signature(IModelDoc2.InsertOffsetSurface) carries the Void flag",
                  !err && offset.Contains(Const("VoidFlag")), Truncated(offset));

            // Exactly the two real signatures, and the old interface named, not printed.
            var sew = Search("SewRefSurface", 0, out err);
            var sigs = SignatureLines(sew);
            var expected = new[]
            {
                "IFeatureManager.InsertSewRefSurface(Boolean UseGapFilters, Boolean TryToFormSolid, " +
                "Boolean MergeEntities, Double KnitTolerance, Double MaxValueForGapRange) -> Feature",
                "IModelDoc2.InsertSewRefSurface() -> Void",
            };
            Check("api_search(SewRefSurface) gives the two real signatures, and only those",
                  !err && sigs.Count == 2 && expected.All(x => sigs.Any(s => s.StartsWith(x, StringComparison.Ordinal))),
                  sigs.Count + " signatures: " + string.Join(" | ", sigs));
            Check("...and names IModelDoc as an old interface without printing its signature",
                  sew.Contains("IModelDoc.InsertSewRefSurface — " + Const("OldInterface") + "IModelDoc2.InsertSewRefSurface") &&
                  !sew.Contains("IModelDoc.InsertSewRefSurface() ->"),
                  Truncated(sew.Substring(Math.Max(0, sew.IndexOf("old interfaces", StringComparison.Ordinal)))));

            var mate = Sig("IAssemblyDoc.AddMate5", out err);
            var align = Block(mate, "    AlignFromEnum  ");
            var one = align.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("1 = ", StringComparison.Ordinal)) ?? "";
            Check("api_signature(IAssemblyDoc.AddMate5) binds AlignFromEnum to swMateAlign_e",
                  !err && align.Contains("enum swMateAlign_e"), Truncated(align));
            Check("...and value 1 shows swAlignSAME and swMateAlignANTI_ALIGNED, marked ambiguous",
                  one.Contains("swAlignSAME") && one.Contains("swMateAlignANTI_ALIGNED") && one.Contains("AMBIGUOUS"),
                  one.Trim());

            var mateData = Sig("IAssemblyDoc.CreateMateData", out err);
            var type = Block(mateData, "    Type  ");
            Check("api_signature(IAssemblyDoc.CreateMateData) says enum not tabulated for Type",
                  !err && type.Contains(Const("EnumNotTabulated")), Truncated(type));

            var old = Sig("IModelDoc.EditDelete", out err);
            Check("api_signature(IModelDoc.EditDelete) answers old interface: use IModelDoc2, not the signature",
                  old.Contains(Const("OldInterface") + "IModelDoc2") && !old.Contains("IModelDoc.EditDelete() ->"),
                  Truncated(old));

            // From the "what" of the entry: the typed variant, the out
            // parameters by name, the numbered family, the indices.
            var edge = Sig("IEdge.GetTwoAdjacentFaces2", out err);
            Check("the typed I-variant stands next to the untyped one",
                  !err && edge.Contains("typed variant: IEdge.IGetTwoAdjacentFaces2(Face2& Face1, Face2& Face2) -> Void"),
                  Truncated(edge));
            var iedge = Sig("IEdge.IGetTwoAdjacentFaces2", out err);
            Check("the out (&) parameters are named",
                  !err && iedge.Contains("out parameters (&), by name: Face1, Face2"), Truncated(iedge));

            var fam = Sig("IFeatureManager.FeatureExtruRefSurface", out err);
            Check("a numbered family marks the last one «latest by number, not verified»",
                  !err && fam.Contains("family: FeatureExtruRefSurface, FeatureExtruRefSurface2, FeatureExtruRefSurface3") &&
                  fam.Contains("FeatureExtruRefSurface3: " + Const("LatestByNumber")), Truncated(fam));

            var tr = Sig("IMathTransform.ArrayData", out err);
            var poly = Sig("IView.GetPolylines7", out err);
            Check("measured indices are shown (ArrayData 9-11), unmeasured ones say index not tabulated",
                  tr.Contains("9-11 = translation") && poly.Contains(Const("IndexNotTabulated")),
                  Truncated(Block(tr, "  return ")) + " / " + Truncated(Block(poly, "    Polylines  ")));

            var unknown = Sig("IBody2.NoSuchMemberAnywhere", out err);
            Check("a member that does not exist is an error, not an empty card", err, Truncated(unknown));
        }

        private static string FirstLine(string s)
        {
            var i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        /// <summary>Signature lines of a search answer (the "signatures:" block).</summary>
        private static List<string> SignatureLines(string text)
        {
            var list = new List<string>();
            bool inside = false;
            foreach (var raw in text.Split('\n'))
            {
                var l = raw.TrimEnd('\r');
                if (l == "signatures:") { inside = true; continue; }
                if (inside && !l.StartsWith("  ", StringComparison.Ordinal)) { inside = false; continue; }
                if (inside) list.Add(l.Trim());
            }
            return list;
        }

        /// <summary>The line that starts with <paramref name="head"/> and the deeper-indented lines under it.</summary>
        private static string Block(string text, string head)
        {
            var lines = text.Split('\n');
            var sb = new StringBuilder();
            int depth = -1;
            foreach (var l in lines)
            {
                int ind = l.Length - l.TrimStart().Length;
                if (depth < 0)
                {
                    if (l.StartsWith(head, StringComparison.Ordinal)) { depth = ind; sb.Append(l).Append('\n'); }
                    continue;
                }
                if (ind <= depth) break;
                sb.Append(l).Append('\n');
            }
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────
        //  api_knowledge.json
        // ─────────────────────────────────────────────────────────────────

        private static void Knowledge()
        {
            var json = KnowledgeJson();
            if (json == null)
            {
                Check("Tools\\api_knowledge.json exists", false, "missing");
                return;
            }
            var problems = (string[])Call("KnowledgeProblems", json);
            Check("api_knowledge.json names only members, parameters and enums that exist",
                  problems.Length == 0,
                  problems.Length == 0 ? "clean" : problems.Length + ": " + string.Join(" | ", problems.Take(5)));

            // SEEN BITING, AT EVERY RUN: the same file with one invented
            // member must be refused, and by name.
            const string invented = "IModelDoc2.InsertInventedFeatureForTheContract";
            var i = json.IndexOf("\"members\": {", StringComparison.Ordinal);
            var tampered = i < 0 ? null : json.Insert(i + "\"members\": {".Length,
                "\n    \"" + invented + "\": { \"state\": \"probed_read\", \"source\": \"nobody\" },");
            var bite = tampered == null ? new string[0] : (string[])Call("KnowledgeProblems", tampered);
            Check("...and it refuses a member that does not exist (seen biting on an invented name)",
                  bite.Any(p => p.StartsWith(invented + ":", StringComparison.Ordinal)),
                  tampered == null ? "could not find \"members\": { in the file" :
                                     string.Join(" | ", bite.Where(p => p.Contains(invented))));
        }

        // ─────────────────────────────────────────────────────────────────
        //  The four old interfaces
        // ─────────────────────────────────────────────────────────────────

        private static void HiddenInterfaces()
        {
            var gaps = (string[])Call("HiddenInterfaceGaps");
            var hidden = (Dictionary<string, string[]>)Call("HiddenInterfaces");
            Check("every hidden interface has all its member names on its sisters",
                  gaps.Length == 0 && hidden.Count == 4,
                  gaps.Length == 0
                      ? string.Join(", ", hidden.Select(h => h.Key + " -> " + string.Join("+", h.Value)))
                      : gaps.Length + " names would be lost: " + string.Join(", ", gaps.Take(8)));

            // THE CHECK CAN SAY NO: IMassProperty has names IMassProperty2
            // lacks, which is why it stays exposed.
            var mass = (string[])Call("InterfaceGaps", "IMassProperty", new[] { "IMassProperty2" });
            Check("...and it says no for IMassProperty -> IMassProperty2 (seen biting)",
                  mass.Length > 0 && !mass[0].StartsWith("(", StringComparison.Ordinal),
                  mass.Length + " names only on IMassProperty, e.g. " + string.Join(", ", mass.Take(4)));
        }

        // ─────────────────────────────────────────────────────────────────
        //  Through the real bridge
        // ─────────────────────────────────────────────────────────────────

        private static void ThroughTheBridge()
        {
            var exe = BridgeOrNotHere("api_signature and api_search through the bridge");
            if (exe == null) return;
            var p = Open(exe);
            if (p == null) { Check("the bridge starts", false, "it did not start"); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");
                var sig = Inside(Obj(Ask(p, 40,
                    "{\"jsonrpc\":\"2.0\",\"id\":40,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"api_signature\",\"arguments\":{\"name\":\"IModelDocExtension.IsSamePersistentID\"}}}")),
                    "result") as Dictionary<string, object>;
                var text = TextOf(sig);
                Check("api_signature answers through the bridge, with the embedded knowledge",
                      text.Contains("IModelDocExtension.IsSamePersistentID(Object PersistentID1, Object PersistentID2) -> Int32") &&
                      text.Contains("swObjectEquality"),
                      Short(text));

                var search = Inside(Obj(Ask(p, 41,
                    "{\"jsonrpc\":\"2.0\",\"id\":41,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"api_search\",\"arguments\":{\"query\":\"GetTwoAdjacentFaces2\"}}}")),
                    "result") as Dictionary<string, object>;
                var st = TextOf(search);
                Check("api_search answers through the bridge",
                      st.Contains("IEdge.IGetTwoAdjacentFaces2(Face2& Face1, Face2& Face2) -> Void") &&
                      !string.Equals(Convert.ToString(Inside(search, "isError")), "True", StringComparison.OrdinalIgnoreCase),
                      Short(st));
            }
            finally { Close(p); }
        }
    }
}
