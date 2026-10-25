using System;
using System.IO;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE USAGE REPORT OF 24/10/2026, AS SOURCE CHECKS.
    ///
    /// These checks read the sources and look for the anchor lines of the fixes. They do not
    /// run the behaviour with SOLIDWORKS: that is what the bench is for.
    /// </summary>
    internal static class UsageFeedbackContract
    {
        internal static void RunAll()
        {
            Group("the usage report of 24/10/2026 (R1, R2, R3, R6, R9)");

            var assemblyStatePath = Path.Combine(Root(), "Oce.Solid.Agent.Core", "AssemblyState.cs");
            var assemblyState = Read(assemblyStatePath);
            Check("the assembly state lists the operations that now exist (R6)",
                  Has(assemblyState, "are insert_component, mate, fix_component, replace_component and mirror_component")
                      && !Has(assemblyState, "are not operations yet"),
                  assemblyState == null ? assemblyStatePath
                      : Has(assemblyState, "are insert_component, mate, fix_component, replace_component and mirror_component")
                          ? "new list found, old wording gone"
                          : "new list missing or old wording still there");

            var executorPath = Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs");
            var executor = Read(executorPath);
            Check("the executor says when the window in front changed during a plan (R1)",
                  Has(executor, "[agent] the window in front changed during the plan: step "),
                  executor == null ? executorPath
                      : Has(executor, "[agent] the window in front changed during the plan: step ")
                          ? "anchor found" : "anchor missing from PlanExecutor.cs");

            Check("the executor logs the time of each step (R2)",
                  Has(executor, "[agent] step timing: step "),
                  executor == null ? executorPath
                      : Has(executor, "[agent] step timing: step ")
                          ? "anchor found" : "anchor missing from PlanExecutor.cs");

            var toolsPath = Path.Combine(Root(), "Tools", "McpTools.cs");
            var tools = Read(toolsPath);
            bool refusedAnchors =
                Has(tools, "bool refusedBeforeWriting = !executorThrew && (planRun == null || planRun.StoppedBeforeWriting);")
                && Has(tools, "[\"state_after\"] = refusedBeforeWriting ? null : after,")
                && Has(tools, "if (refusedBeforeWriting && frontIsThePlans)");
            bool declaredBeforeBranch = refusedAnchors
                && tools.IndexOf("bool refusedBeforeWriting = ", StringComparison.Ordinal)
                    < tools.IndexOf("if (refusedBeforeWriting && frontIsThePlans)", StringComparison.Ordinal);
            Check("a refusal before writing keeps the state out of the answer (R3)",
                  refusedAnchors && declaredBeforeBranch,
                  tools == null ? toolsPath
                      : refusedAnchors && declaredBeforeBranch
                          ? "the refused plan answers without state_after and adopts no reading"
                          : "an anchor is missing from McpTools.cs or the declaration comes after the branch");

            Check("a part with no solid body says so in faces_note, a part with bodies is refused (R9)",
                  Has(tools, "[\"faces_note\"]") && Has(tools, "This part has no solid body yet")
                      && Has(tools, "docState.ContainsKey(\"bodies\")"),
                  tools == null ? toolsPath
                      : Has(tools, "[\"faces_note\"]") && Has(tools, "This part has no solid body yet")
                        && Has(tools, "docState.ContainsKey(\"bodies\")")
                          ? "all anchors found" : "an anchor is missing from McpTools.cs");
        }

        private static string Read(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static bool Has(string text, string anchor)
        {
            return text != null && text.IndexOf(anchor, StringComparison.Ordinal) >= 0;
        }
    }
}
