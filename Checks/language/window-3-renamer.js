// =============================================================================
//  window-3-renamer.js - the identifier step of rename window 3 (W3.1-bis.6)
// =============================================================================
//
//  Called by window-3-identifiers.js --identifiers <batch>. A batch is the
//  folder where a name is DECLARED; every reference to it, in any project,
//  is renamed with it, so that each batch builds on its own:
//
//    core      Oce.Solid.Agent.Core/          (the library)
//    shared    Shared/                        (linked into the library, the bench, the contracts)
//    bridge    Program.cs, Mcp/, Bridge/, Tools/
//    tools     Oce.Solid.Agent.Catalog/, Installer/
//    bench     Bench/ (C#)
//    tests     Tests/
//    probes    Checks/<probe>/Program.cs
//    scripts   the JavaScript and PowerShell files (per file)
//
//  C#: through Roslyn with a semantic model of every project (the compile
//  items of its .csproj, its project references, the .NET Framework reference
//  assemblies and the SOLIDWORKS interop of the installation), so a name is
//  renamed where it BINDS to a symbol declared in the batch, never where the
//  same word means something else (a member of the SOLIDWORKS API, a string,
//  a comment, a local of another method). Declarations, references,
//  `nameof`, named arguments, object initializers and `cref`s in the
//  documentation comments. Members of anonymous types are left alone (they
//  are the keys of the JSON a serializer writes). A string literal is
//  renamed only where it names a renamed type or member for reflection: an
//  argument of GetType, GetMethod, GetProperty, GetField, GetNestedType,
//  GetEvent, GetMember, InvokeMember, of the reflection logs of the bench and
//  of the contracts, and of the helpers listed in REFLECTION_HELPERS.
//
//  Before writing, every renamed declaration is checked for a clash: a member
//  of the same type (or a base or derived type) or a type of the same
//  namespace that already has the English name, or a local, parameter or
//  member visible where the renamed local is used. A clash stops the batch.
//
//  The edits are written by position, each checked against the text it
//  replaces; the build, same-il.ps1 -Mode Renames and the contracts are the
//  proof that nothing but names changed.
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');

const BATCHES = {
  // the two batches of the window: the code that ships, then the code that
  // proves it (the bench, the contracts and the probes read the shipped code
  // by reflection and by its text, so they follow it)
  production: ['Oce.Solid.Agent.Core/', 'Shared/', 'Program.cs', 'Mcp/', 'Bridge/', 'Tools/', 'Oce.Solid.Agent.Catalog/', 'Installer/'],
  harness: ['Bench/', 'Tests/', 'Checks/'],
  // finer batches, for a look at one folder
  core: ['Oce.Solid.Agent.Core/'],
  shared: ['Shared/'],
  bridge: ['Program.cs', 'Mcp/', 'Bridge/', 'Tools/'],
  tools: ['Oce.Solid.Agent.Catalog/', 'Installer/'],
  bench: ['Bench/'],
  tests: ['Tests/'],
  probes: ['Checks/'],
};

// One symbol whose English name differs from its row, because the row's name
// would clash where that symbol lives (found by the binding proof; the map's
// note of the row says so): [file, kind, Italian name, English name].
// A local or a parameter is named with the member that declares it (5th).
const SYMBOL_OVERRIDES = [
  ['Oce.Solid.Agent.Core/DrawingDimensionOperations.cs', 'NamedType', 'Spigolo', 'ProjectedEdge'],
  // the undo tool already has the English locals before, now and after for
  // the snapshots of the memory; the states it reads get their own names
  ['Tools/McpTools.cs', 'Local', 'prima', 'stateBefore', 'Annulla'],
  ['Tools/McpTools.cs', 'Local', 'adesso', 'identityNow', 'Annulla'],
  ['Tools/McpTools.cs', 'Local', 'dopo', 'stateAfter', 'Annulla'],
  // SelfTestCodes already has a local bridge (the codes of the bridge's source)
  ['Tests/H19InstallerContract.cs', 'Local', 'ponte', 'bridgeExe', 'SelfTestCodes'],
  // the contracts import Shared.McpClient.Short too: the helper that cuts
  // a text at 110 characters for a message is Truncated
  ['Tests/Program.cs', 'Method', 'Breve', 'Truncated'],
];

// methods whose string arguments name a type or a member by reflection
const REFLECTION_METHODS = ['GetType', 'GetMethod', 'GetProperty', 'GetField', 'GetNestedType', 'GetEvent', 'GetMember',
  'InvokeMember', 'TypeIn', 'Method', 'Property', 'Field', 'Nested', 'Missing', 'MissingType', 'Classe'];

const CSHARP = String.raw`// The identifier renamer of rename window 3 (window-3-renamer.js).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

static class RenamerEntry
{
    static string roslynDir;
    static int Main(string[] args)
    {
        roslynDir = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var p = Path.Combine(roslynDir, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        return Renamer.Run(args[1], args[2]);
    }
}

static class Renamer
{
    class Group { public string Name; public List<string> Refs = new List<string>(); public List<string> Metadata = new List<string>(); public List<string[]> Files = new List<string[]>(); public CSharpCompilation Compilation; }

    static Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
    static List<string> batch = new List<string>();
    static HashSet<string> reflectionMethods = new HashSet<string>(StringComparer.Ordinal);
    static Dictionary<string, string> relOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // one edit per file and position
    static Dictionary<string, SortedDictionary<int, string[]>> edits = new Dictionary<string, SortedDictionary<int, string[]>>(StringComparer.OrdinalIgnoreCase);
    static List<string> problems = new List<string>();
    static List<string> notes = new List<string>();
    static HashSet<string> renamedSymbols = new HashSet<string>(StringComparer.Ordinal);
    static Dictionary<string, ISymbol> renamedByKey = new Dictionary<string, ISymbol>(StringComparer.Ordinal);

    public static int Run(string manifest, string outFile)
    {
        var globalRefs = new List<MetadataReference>();
        var groups = new List<Group>();
        var byName = new Dictionary<string, Group>();
        foreach (var raw in File.ReadAllLines(manifest, Encoding.UTF8))
        {
            if (raw.Length == 0) continue;
            var f = raw.Split('\t');
            if (f[0] == "R") globalRefs.Add(MetadataReference.CreateFromFile(f[1]));
            else if (f[0] == "M") map[f[1]] = f[2];
            else if (f[0] == "B") batch.Add(f[1]);
            else if (f[0] == "X") reflectionMethods.Add(f[1]);
            else if (f[0] == "O") overrides[f[1] + "|" + f[2] + "|" + f[3] + (f.Length > 5 && f[5].Length > 0 ? "|" + f[5] : "")] = f[4];
            else if (f[0] == "G")
            {
                var g = new Group { Name = f[1] };
                if (f.Length > 2 && f[2].Length > 0) g.Refs.AddRange(f[2].Split(','));
                if (f.Length > 3 && f[3].Length > 0) g.Metadata.AddRange(f[3].Split('|'));
                groups.Add(g); byName[g.Name] = g;
            }
            else if (f[0] == "F") { byName[f[1]].Files.Add(new[] { f[2], f[3] }); relOf[f[2]] = f[3]; }
        }
        var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular);
        foreach (var g in groups)
        {
            var trees = new List<SyntaxTree>();
            foreach (var file in g.Files)
                trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file[0]), Encoding.UTF8), options, file[0]));
            var refs = new List<MetadataReference>(globalRefs);
            foreach (var m in g.Metadata) refs.Add(MetadataReference.CreateFromFile(m));
            foreach (var r in g.Refs) if (byName.ContainsKey(r) && byName[r].Compilation != null) refs.Add(byName[r].Compilation.ToMetadataReference());
            g.Compilation = CSharpCompilation.Create(g.Name.Replace(':', '_'), trees, refs, new CSharpCompilationOptions(
                g.Name.StartsWith("probe:") ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var errors = g.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                problems.Add("the compilation " + g.Name + " has " + errors.Count + " errors before the rename (first: " + errors[0] + ")");
        }
        if (problems.Count == 0)
        {
            DetectHelpers(groups.SelectMany(g => g.Compilation.SyntaxTrees).ToList());
            foreach (var g in groups)
                foreach (var tree in g.Compilation.SyntaxTrees)
                    Visit(tree, g.Compilation.GetSemanticModel(tree));
            foreach (var g in groups) CheckClashes(g);
            RenameReflectionLiterals();
            patternTrees.AddRange(groups.Where(g => g.Name == "tests" || g.Name == "bench").SelectMany(g => g.Compilation.SyntaxTrees)
                .Where(x => RelPath(x.FilePath).StartsWith("Tests/") || RelPath(x.FilePath).StartsWith("Bench/")));
            RenamePatternLiterals();
            if (problems.Count == 0) VerifyBindings(groups, globalRefs, byName, options);
        }
        using (var w = new StreamWriter(outFile, false, new UTF8Encoding(false)))
        {
            foreach (var p in problems) w.WriteLine("P\t" + p.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '));
            foreach (var n in notes) w.WriteLine("N\t" + n.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '));
            foreach (var kv in edits)
                foreach (var e in kv.Value)
                    w.WriteLine("E\t" + relOf[kv.Key] + "\t" + e.Key.ToString(CultureInfo.InvariantCulture) + "\t" + e.Value[0] + "\t" + e.Value[1] + "\t" + e.Value[2]);
        }
        return 0;
    }

    static string RelPath(string file) { string r; return relOf.TryGetValue(file, out r) ? r : file; }

    static Dictionary<string, string> overrides = new Dictionary<string, string>(StringComparer.Ordinal);

    // the English name of a symbol: its row, or the override for that symbol
    static string EnglishOf(ISymbol s)
    {
        var loc = s.Locations.FirstOrDefault(l => l.IsInSource);
        string o;
        if (loc != null && (s.Kind == SymbolKind.Local || s.Kind == SymbolKind.Parameter || s.Kind == SymbolKind.RangeVariable) &&
            s.ContainingSymbol != null &&
            overrides.TryGetValue(RelPath(loc.SourceTree.FilePath) + "|" + s.Kind + "|" + s.Name + "|" + s.ContainingSymbol.Name, out o)) return o;
        if (loc != null && overrides.TryGetValue(RelPath(loc.SourceTree.FilePath) + "|" + s.Kind + "|" + s.Name, out o)) return o;
        string en;
        return map.TryGetValue(s.Name, out en) ? en : null;
    }

    // ── the proof before writing: every token keeps its symbol ─────────────
    // The edits are applied in memory and every project compiled again. Each
    // token that was renamed, and each token that already carried one of the
    // English names, must bind after the rename to the same declaration as
    // before (a renamed one to its own declaration under its new name): a
    // member that would hide a type of the namespace, a nested type that would
    // hide an interop type, a local that would capture a field, all show here.
    static Dictionary<string, List<int[]>> shifts = new Dictionary<string, List<int[]>>(StringComparer.OrdinalIgnoreCase);

    static int MapPos(string file, int pos)
    {
        List<int[]> list;
        if (!shifts.TryGetValue(file, out list)) return pos;
        int d = 0;
        foreach (var s in list) { if (s[0] < pos) d += s[2] - s[1]; else break; }
        return pos + d;
    }

    static string KeyOf(ISymbol s, bool after)
    {
        if (s == null) return "(none)";
        var loc = s.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc == null) return "meta:" + s.Kind + ":" + s.ToDisplayString();
        var file = loc.SourceTree.FilePath;
        var pos = after ? loc.SourceSpan.Start : MapPos(file, loc.SourceSpan.Start);
        return RelPath(file) + ":" + pos.ToString(CultureInfo.InvariantCulture) + ":" + s.Kind;
    }

    static ISymbol BindToken(SemanticModel model, SyntaxToken t)
    {
        var node = t.Parent;
        if (node == null) return null;
        try
        {
            if (IsDeclarationIdentifier(node, t))
            {
                var d = model.GetDeclaredSymbol(node);
                if (node is ConstructorDeclarationSyntax || node is DestructorDeclarationSyntax) return d == null ? null : d.ContainingType;
                return Owner(d);
            }
            if (node is IdentifierNameSyntax || node is GenericNameSyntax)
            {
                var info = model.GetSymbolInfo(node);
                if (info.Symbol != null) return Owner(info.Symbol);
                var owners = info.CandidateSymbols.Select(Owner).Where(x => x != null).Distinct().ToList();
                if (owners.Count >= 1) return owners[0];
                var group = model.GetMemberGroup(node);
                if (group.Length > 0) return Owner(group[0]);
            }
        }
        catch { }
        return null;
    }

    static void VerifyBindings(List<Group> groups, List<MetadataReference> globalRefs, Dictionary<string, Group> byName, CSharpParseOptions options)
    {
        var enNames = new HashSet<string>(map.Values.Concat(overrides.Values), StringComparer.Ordinal);
        var newText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in edits)
        {
            var text = File.ReadAllText(kv.Key);
            var list = new List<int[]>();
            var sb = new StringBuilder(text);
            foreach (var e in kv.Value.Reverse())
            {
                sb.Remove(e.Key, e.Value[0].Length);
                sb.Insert(e.Key, e.Value[1]);
            }
            foreach (var e in kv.Value) list.Add(new[] { e.Key, e.Value[0].Length, e.Value[1].Length });
            shifts[kv.Key] = list;
            newText[kv.Key] = sb.ToString();
        }
        var newComps = new Dictionary<string, CSharpCompilation>();
        int checkedTokens = 0, changed = 0;
        foreach (var g in groups)
        {
            var trees = new List<SyntaxTree>();
            foreach (var file in g.Files)
            {
                string t;
                if (!newText.TryGetValue(file[0], out t)) t = File.ReadAllText(file[0]);
                trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(t, Encoding.UTF8), options, file[0]));
            }
            var refs = new List<MetadataReference>(globalRefs);
            foreach (var m in g.Metadata) refs.Add(MetadataReference.CreateFromFile(m));
            foreach (var r in g.Refs) if (newComps.ContainsKey(r)) refs.Add(newComps[r].ToMetadataReference());
            var comp = CSharpCompilation.Create(g.Name.Replace(':', '_'), trees, refs, g.Compilation.Options);
            newComps[g.Name] = comp;
            var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            foreach (var err in errors.Take(20)) problems.Add("after the rename, " + g.Name + ": " + err);
            var newByFile = comp.SyntaxTrees.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
            foreach (var oldTree in g.Compilation.SyntaxTrees)
            {
                var oldModel = g.Compilation.GetSemanticModel(oldTree);
                var newTree = newByFile[oldTree.FilePath];
                var newModel = comp.GetSemanticModel(newTree);
                var newRoot = newTree.GetRoot();
                SortedDictionary<int, string[]> fileEdits;
                edits.TryGetValue(oldTree.FilePath, out fileEdits);
                foreach (var t in oldTree.GetRoot().DescendantTokens(descendIntoTrivia: true))
                {
                    if (!t.IsKind(SyntaxKind.IdentifierToken)) continue;
                    var renamed = fileEdits != null && (fileEdits.ContainsKey(t.SpanStart) || fileEdits.ContainsKey(t.SpanStart + 1));
                    if (!renamed && !enNames.Contains(t.ValueText)) continue;
                    var before = BindToken(oldModel, t);
                    // a name of a documentation comment that did not bind before (the
                    // comment already used the English name) and binds now: better
                    if (before == null && t.IsPartOfStructuredTrivia()) continue;
                    var nt = newRoot.FindToken(MapPos(oldTree.FilePath, t.SpanStart), findInsideTrivia: true);
                    var after = BindToken(newModel, nt);
                    checkedTokens++;
                    var kb = KeyOf(before, false);
                    var ka = KeyOf(after, true);
                    if (kb == ka) continue;
                    // a symbol of a project bound through another project's metadata copy
                    if (before != null && after != null && before.Locations.All(l => !l.IsInSource) && after.Locations.All(l => !l.IsInSource) &&
                        before.Kind == after.Kind) continue;
                    changed++;
                    if (changed <= 200)
                        problems.Add("binding changed: " + RelPath(oldTree.FilePath) + ":" + Line(t) + ": «" + t.ValueText + "» was " + kb + ", now «" + nt.ValueText + "» " + ka);
                }
            }
        }
        notes.Add("bindings checked after the rename: " + checkedTokens + " tokens, " + changed + " changed");
    }

    static bool InBatch(string file)
    {
        var rel = RelPath(file).Replace('\\', '/');
        foreach (var b in batch) if (b.EndsWith("/") ? rel.StartsWith(b, StringComparison.Ordinal) : rel == b) return true;
        return false;
    }

    // the symbol whose name a token carries, reduced to the declaration that owns the name
    static ISymbol Owner(ISymbol s)
    {
        if (s == null) return null;
        var m = s as IMethodSymbol;
        if (m != null)
        {
            if (m.ReducedFrom != null) m = m.ReducedFrom;
            if (m.MethodKind == MethodKind.Constructor || m.MethodKind == MethodKind.StaticConstructor || m.MethodKind == MethodKind.Destructor)
                return m.ContainingType == null ? null : m.ContainingType.OriginalDefinition;
            if (m.MethodKind == MethodKind.PropertyGet || m.MethodKind == MethodKind.PropertySet) return m.AssociatedSymbol;
            return m.OriginalDefinition;
        }
        return s.OriginalDefinition;
    }

    static bool Renameable(ISymbol s)
    {
        switch (s.Kind)
        {
            case SymbolKind.NamedType: case SymbolKind.Method: case SymbolKind.Property: case SymbolKind.Field:
            case SymbolKind.Event: case SymbolKind.Local: case SymbolKind.Parameter: case SymbolKind.TypeParameter:
            case SymbolKind.Label: case SymbolKind.RangeVariable:
                break;
            default: return false;
        }
        if (s.ContainingType != null && s.ContainingType.IsAnonymousType) return false;
        if (s.Kind == SymbolKind.NamedType && ((INamedTypeSymbol)s).IsAnonymousType) return false;
        if (!map.ContainsKey(s.Name)) return false;
        var loc = s.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc == null) return false;
        if (InBatch(loc.SourceTree.FilePath)) return true;
        // an override or an implementation of a member the batch renames is
        // renamed with it, wherever it is declared
        foreach (var r in Related(s)) if (r != null && !SymbolEqualityComparer.Default.Equals(r, s) && Renameable(r)) return true;
        return false;
    }

    static IEnumerable<ISymbol> Related(ISymbol s)
    {
        var m = s as IMethodSymbol;
        if (m != null && m.OverriddenMethod != null) yield return Owner(m.OverriddenMethod);
        var p = s as IPropertySymbol;
        if (p != null && p.OverriddenProperty != null) yield return p.OverriddenProperty.OriginalDefinition;
        var e = s as IEventSymbol;
        if (e != null && e.OverriddenEvent != null) yield return e.OverriddenEvent.OriginalDefinition;
        if (m != null) foreach (var x in m.ExplicitInterfaceImplementations) yield return Owner(x);
        if (p != null) foreach (var x in p.ExplicitInterfaceImplementations) yield return x.OriginalDefinition;
        if (s.ContainingType != null && (m != null || p != null || e != null))
            foreach (var iface in s.ContainingType.AllInterfaces)
                foreach (var im in iface.GetMembers(s.Name))
                {
                    var impl = s.ContainingType.FindImplementationForInterfaceMember(im);
                    if (impl != null && SymbolEqualityComparer.Default.Equals(Owner(impl), Owner(s))) yield return Owner(im);
                }
    }

    static string Key(ISymbol s)
    {
        var loc = s.Locations.FirstOrDefault(l => l.IsInSource);
        return RelPath(loc.SourceTree.FilePath) + ":" + loc.SourceSpan.Start.ToString(CultureInfo.InvariantCulture) + ":" + s.Name;
    }

    static void Edit(SyntaxToken t, ISymbol owner, string kind)
    {
        var text = t.Text;
        var verbatim = text.StartsWith("@");
        var name = verbatim ? text.Substring(1) : text;
        if (name != owner.Name) return;   // a token that names it differently (an alias)
        string en;
        en = EnglishOf(owner);
        if (en == null) return;
        var file = t.SyntaxTree.FilePath;
        SortedDictionary<int, string[]> list;
        if (!edits.TryGetValue(file, out list)) { list = new SortedDictionary<int, string[]>(); edits[file] = list; }
        var start = t.SpanStart + (verbatim ? 1 : 0);
        list[start] = new[] { name, en, kind + ":" + owner.Kind };
        var k = Key(owner);
        if (renamedSymbols.Add(k)) renamedByKey[k] = owner;
    }

    static void Visit(SyntaxTree tree, SemanticModel model)
    {
        var root = tree.GetRoot();
        foreach (var t in root.DescendantTokens(descendIntoTrivia: true))
        {
            if (!t.IsKind(SyntaxKind.IdentifierToken)) continue;
            var name = t.ValueText;
            if (!map.ContainsKey(name)) continue;
            var node = t.Parent;
            if (node == null) continue;
            ISymbol sym = null;
            string kind = "ref";
            try
            {
                if (IsDeclarationIdentifier(node, t))
                {
                    kind = "decl";
                    var declNode = node is AnonymousObjectMemberDeclaratorSyntax ? node : node;
                    sym = model.GetDeclaredSymbol(node);
                    if (node is ConstructorDeclarationSyntax || node is DestructorDeclarationSyntax) sym = sym == null ? null : sym.ContainingType;
                }
                else if (node is IdentifierNameSyntax || node is GenericNameSyntax)
                {
                    var info = model.GetSymbolInfo(node);
                    sym = info.Symbol;
                    if (sym == null && info.CandidateSymbols.Length > 0)
                    {
                        var owners = info.CandidateSymbols.Select(Owner).Where(x => x != null).Distinct().ToList();
                        if (owners.Count == 1) sym = owners[0];
                        else if (owners.Count > 1 && owners.All(o => o.Name == owners[0].Name && Renameable(o)))
                        {
                            foreach (var o in owners) { var k = Key(o); if (renamedSymbols.Add(k)) renamedByKey[k] = o; }
                            sym = owners[0];
                        }
                    }
                    if (sym == null)
                    {
                        // a name that does not bind (inside a cref that fails, or nameof of a group)
                        var group = model.GetMemberGroup(node);
                        if (group.Length > 0) sym = group[0];
                    }
                }
            }
            catch (Exception ex) { notes.Add(RelPath(tree.FilePath) + ":" + Line(t) + ": " + name + ": binding failed (" + ex.Message + ")"); continue; }
            var owner = Owner(sym);
            if (owner == null)
            {
                if (kind == "decl") notes.Add(RelPath(tree.FilePath) + ":" + Line(t) + ": " + name + ": declaration without a symbol");
                continue;
            }
            if (kind == "decl" && IsTypeOrMember(owner))
            {
                HashSet<string> keys;
                if (!declaredNames.TryGetValue(owner.Name, out keys)) { keys = new HashSet<string>(StringComparer.Ordinal); declaredNames[owner.Name] = keys; }
                keys.Add(Key(owner));
                declaredEnglish[Key(owner)] = EnglishOf(owner);
            }
            if (!Renameable(owner)) continue;
            Edit(t, owner, kind);
        }
        // string literals that name a renamed type or member for reflection
        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            var value = lit.Token.ValueText;
            if (value.Length == 0) continue;
            var parts = value.Split('.', '+');
            if (!parts.Any(p => map.ContainsKey(p))) continue;
            var inv = lit.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (inv == null) continue;
            string method = null;
            var access = inv.Expression as MemberAccessExpressionSyntax;
            if (access != null) method = access.Name.Identifier.ValueText;
            var id = inv.Expression as IdentifierNameSyntax;
            if (id != null) method = id.Identifier.ValueText;
            var binding = inv.Expression as MemberBindingExpressionSyntax;   // type?.GetMethod("...")
            if (binding != null) method = binding.Name.Identifier.ValueText;
            if (method == null || !reflectionMethods.Contains(method)) continue;
            literalCandidates.Add(Tuple.Create(lit, method));
        }
    }

    static List<Tuple<LiteralExpressionSyntax, string>> literalCandidates = new List<Tuple<LiteralExpressionSyntax, string>>();
    static Dictionary<string, string> literalEn = new Dictionary<string, string>(StringComparer.Ordinal);

    // ── the helpers of reflection ──────────────────────────────────────────
    // A method, a local function or a lambda that hands one of its parameters
    // to a reflection lookup (or to another helper) as the name: its call
    // sites name types and members by literals too (the helpers of
    // ReflectionTests: Call, Invoke, the lambdas of the registry and of the
    // progress contracts). A literal compared with that parameter inside the
    // helper is a name as well.
    static void DetectHelpers(List<SyntaxTree> trees)
    {
        var bodies = new List<Tuple<string, List<string>, SyntaxNode>>();
        foreach (var tree in trees)
            foreach (var n in tree.GetRoot().DescendantNodes())
            {
                var md = n as MethodDeclarationSyntax;
                if (md != null) { bodies.Add(Tuple.Create(md.Identifier.ValueText, md.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToList(), (SyntaxNode)md)); continue; }
                var lf = n as LocalFunctionStatementSyntax;
                if (lf != null) { bodies.Add(Tuple.Create(lf.Identifier.ValueText, lf.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToList(), (SyntaxNode)lf)); continue; }
                var vd = n as VariableDeclaratorSyntax;
                if (vd != null && vd.Initializer != null)
                {
                    var pl = vd.Initializer.Value as ParenthesizedLambdaExpressionSyntax;
                    if (pl != null) bodies.Add(Tuple.Create(vd.Identifier.ValueText, pl.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToList(), (SyntaxNode)pl));
                }
            }
        bool grew = true;
        var helpers = new HashSet<string>(StringComparer.Ordinal);
        while (grew)
        {
            grew = false;
            foreach (var b in bodies)
            {
                if (helpers.Contains(b.Item1) || reflectionMethods.Contains(b.Item1) || b.Item2.Count == 0) continue;
                foreach (var inv in b.Item3.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var callee = CalleeName(inv);
                    if (callee == null || !(reflectionMethods.Contains(callee) || helpers.Contains(callee))) continue;
                    if (inv.ArgumentList.Arguments.Any(a => a.Expression is IdentifierNameSyntax && b.Item2.Contains(((IdentifierNameSyntax)a.Expression).Identifier.ValueText)))
                    { helpers.Add(b.Item1); grew = true; break; }
                }
            }
        }
        foreach (var h in helpers) reflectionMethods.Add(h);
        notes.Add("helpers of reflection: " + string.Join(", ", helpers.OrderBy(x => x)));
        // literals compared with the name parameter of a helper
        foreach (var b in bodies.Where(x => helpers.Contains(x.Item1)))
            foreach (var be in b.Item3.DescendantNodes().OfType<BinaryExpressionSyntax>())
            {
                if (!be.IsKind(SyntaxKind.EqualsExpression) && !be.IsKind(SyntaxKind.NotEqualsExpression)) continue;
                var id = (be.Left as IdentifierNameSyntax) ?? (be.Right as IdentifierNameSyntax);
                var lit = (be.Left as LiteralExpressionSyntax) ?? (be.Right as LiteralExpressionSyntax);
                if (id != null && lit != null && lit.IsKind(SyntaxKind.StringLiteralExpression) && b.Item2.Contains(id.Identifier.ValueText))
                    literalCandidates.Add(Tuple.Create(lit, "=="));
            }
    }

    static string CalleeName(InvocationExpressionSyntax inv)
    {
        var access = inv.Expression as MemberAccessExpressionSyntax;
        if (access != null) return access.Name.Identifier.ValueText;
        var id = inv.Expression as IdentifierNameSyntax;
        if (id != null) return id.Identifier.ValueText;
        var binding = inv.Expression as MemberBindingExpressionSyntax;
        if (binding != null) return binding.Name.Identifier.ValueText;
        var gn = inv.Expression as GenericNameSyntax;
        if (gn != null) return gn.Identifier.ValueText;
        return null;
    }
    static List<SyntaxTree> patternTrees = new List<SyntaxTree>();

    // ── the contracts and the bench read the sources ───────────────────────
    // A literal of Tests/ or Bench/ that is a pattern over the code (a regular
    // expression, a piece of code searched with Contains, a code span «...» or
    // a back-quoted span inside a message) names the code by its identifiers: those the
    // batch renames are renamed there too. A label (the first argument of
    // the recording methods listed below) is prose and is left alone.
    static readonly HashSet<string> LabelMethods = new HashSet<string>(StringComparer.Ordinal) { "Segna", "Gruppo", "Saltata", "ErroreDiBanco", "SegnaAttesa" };

    static void RenamePatternLiterals()
    {
        // eligible: the type and member names of literalEn; a local or parameter
        // name the batch renames, unless a type or member of that name goes to
        // another English name
        var eligible = new Dictionary<string, string>(literalEn, StringComparer.Ordinal);
        foreach (var s in renamedByKey.Values)
        {
            if (IsTypeOrMember(s) || eligible.ContainsKey(s.Name)) continue;
            var en = EnglishOf(s);
            HashSet<string> keys;
            if (declaredNames.TryGetValue(s.Name, out keys) && keys.Any(k => declaredEnglish[k] != en)) continue;
            if (en != null) eligible[s.Name] = en;
        }
        foreach (var tree in patternTrees)
        {
            SortedDictionary<int, string[]> fileEdits;
            edits.TryGetValue(tree.FilePath, out fileEdits);
            foreach (var tok in tree.GetRoot().DescendantTokens())
            {
                if (!tok.IsKind(SyntaxKind.StringLiteralToken) && !tok.IsKind(SyntaxKind.InterpolatedStringTextToken)) continue;
                if (fileEdits != null && fileEdits.ContainsKey(tok.SpanStart)) continue;
                var text = tok.Text;
                if (LiteralKeep.Any(k => RelPath(tree.FilePath) == k[0] && text.Contains(k[1]))) continue;
                if (!eligible.Keys.Any(k => text.Contains(k))) continue;
                string use = CodeUse(tok) ?? (IsLabel(tok) ? null : LooksLikeRegex(text) ? "regex" :
                             FixtureLike(tok.ValueText) && !SyntheticFixtures.Contains(RelPath(tree.FilePath)) ? "fixture" : "message");
                if (use == null) continue;
                bool regex = use == "regex";
                // a literal that is only names of the code, PascalCase, one or
                // more separated by spaces ("SchizzoAppenaCreato", a list split
                // with Split(' ')): the names of types and members in it are code
                bool nameList = System.Text.RegularExpressions.Regex.IsMatch(tok.ValueText, @"^[A-Z][A-Za-z0-9_]*( [A-Z][A-Za-z0-9_]*)*$");
                var sb = new StringBuilder();
                int i = 0; bool changed = false;
                while (i < text.Length)
                {
                    if (char.IsLetter(text[i]) || text[i] == '_')
                    {
                        int j = i;
                        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                        var w = text.Substring(i, j - i);
                        string en;
                        // in a regex: a type or member name, not as a word of an
                        // alternation of words; a local only glued to code
                        if (eligible.TryGetValue(w, out en) &&
                            (regex ? !InAlternation(text, i, j) && (literalEn.ContainsKey(w) || GluedRegex(text, i, j))
                             : use == "message" ? (nameList && literalEn.ContainsKey(w)) ||
                                                  (InCodeSpan(text, i, j) && (literalEn.ContainsKey(w) || Glued(text, i, j, false)))
                             : Glued(text, i, j, true)))
                        { sb.Append(en); changed = true; }
                        // a regex escape (\b, \s, \w, \d) glued before the word is not a letter of it
                        else if (regex && w.Length > 1 && i >= 1 && text[i - 1] == '\\' && "bBswdSWD".IndexOf(w[0]) >= 0 &&
                                 eligible.TryGetValue(w.Substring(1), out en))
                        { sb.Append(w[0]).Append(en); changed = true; }
                        else sb.Append(w);
                        i = j; continue;
                    }
                    sb.Append(text[i]); i++;
                }
                if (!changed) continue;
                if (fileEdits == null) { fileEdits = new SortedDictionary<int, string[]>(); edits[tree.FilePath] = fileEdits; }
                fileEdits[tok.SpanStart] = new[] { text, sb.ToString(), "pattern:" + use };
            }
        }
    }

    // how a literal is used on the code: "regex" (an argument of Regex), "search"
    // (an argument of Contains, IndexOf, StartsWith... on a text), or null
    static readonly HashSet<string> SearchMethods = new HashSet<string>(StringComparer.Ordinal)
        { "Contains", "IndexOf", "LastIndexOf", "StartsWith", "EndsWith", "Replace", "Split", "Count" };
    static string CodeUse(SyntaxToken tok)
    {
        SyntaxNode n = tok.Parent;
        while (n != null && (n is LiteralExpressionSyntax || n is BinaryExpressionSyntax || n is ParenthesizedExpressionSyntax ||
                             n is InterpolatedStringExpressionSyntax || n is InterpolatedStringTextSyntax || n is InterpolationSyntax))
            n = n.Parent;
        var arg = n as ArgumentSyntax;
        if (arg == null) return null;
        var call = arg.Parent == null ? null : arg.Parent.Parent;
        var oc = call as ObjectCreationExpressionSyntax;
        if (oc != null) return oc.Type.ToString().EndsWith("Regex") ? "regex" : null;
        var inv = call as InvocationExpressionSyntax;
        if (inv == null) return null;
        var access = inv.Expression as MemberAccessExpressionSyntax;
        var binding = inv.Expression as MemberBindingExpressionSyntax;
        if (binding != null) return SearchMethods.Contains(binding.Name.Identifier.ValueText) ? "search" : null;
        if (access == null) return null;
        if (access.Expression.ToString().EndsWith("Regex")) return "regex";
        if (SearchMethods.Contains(access.Name.Identifier.ValueText)) return "search";
        return null;
    }

    // a verbatim literal with the escapes of a regular expression
    static bool LooksLikeRegex(string raw)
    {
        return raw.StartsWith("@\"") && System.Text.RegularExpressions.Regex.IsMatch(raw, @"\\[bswd.(]");
    }

    static bool InAlternation(string text, int i, int j)
    {
        char before = i > 0 ? text[i - 1] : ' ', after = j < text.Length ? text[j] : ' ';
        return before == '|' || after == '|' || ((before == ':' || before == '(') && after == ')');
    }

    // in a regex: a member access (x\.Name, Name\.x) or a call (Name\()
    static bool GluedRegex(string text, int i, int j)
    {
        Func<char, bool> id = c => char.IsLetterOrDigit(c) || c == '_';
        int k = j;
        // optional white space: \s* or \s+
        if (k + 2 < text.Length && text[k] == '\\' && text[k + 1] == 's' && (text[k + 2] == '*' || text[k + 2] == '+')) k += 3;
        if (k + 1 < text.Length && text[k] == '\\' && (text[k + 1] == '(' || text[k + 1] == '.')) return true;
        if (j + 1 < text.Length && text[j] == '.' && id(text[j + 1])) return true;
        if (i >= 2 && text[i - 1] == '.' && (text[i - 2] == '\\' || id(text[i - 2]))) return true;
        return false;
    }

    // a word glued to code: a call (Name(), a member access (x.Name, Name.x)
    // A word of a literal glued to code. statement: the literal is a piece
    // of code (searched in a source, or written as a fake source), so the
    // operators around a name count too.
    static bool Glued(string text, int i, int j, bool statement)
    {
        Func<char, bool> id = c => char.IsLetterOrDigit(c) || c == '_';
        // a segment of a path, a file name or a URL is not code
        if (i > 0 && "\\/:-".IndexOf(text[i - 1]) >= 0) return false;
        // a call: Name(
        if (j < text.Length && text[j] == '(') return true;
        // a member of something that reads as code: _x.Name, Type.Name, this.Name
        if (i >= 2 && text[i - 1] == '.' && id(text[i - 2]))
        {
            int k = i - 2; while (k > 0 && id(text[k - 1])) k--;
            var recv = text.Substring(k, i - 1 - k);
            if (statement || recv.StartsWith("_") || char.IsUpper(recv[0]) || recv == "this" || recv == "base") return true;
        }
        // a name with a member that reads as code: name.Member
        if (j + 2 < text.Length && text[j] == '.' && char.IsUpper(text[j + 1]) && char.IsLower(text[j + 2])) return true;
        // an index: name[
        if (j < text.Length && text[j] == '[') return true;
        // a field by its convention (_name)
        if (text[i] == '_') return true;
        // after a type or a keyword: bool name, new Name, return name
        if (KeywordLead.IsMatch(text.Substring(0, i))) return true;
        if (!statement) return false;
        // around an assignment, a comparison, a logical operator, a semicolon
        if (OperatorLead.IsMatch(text.Substring(0, i))) return true;
        if (OperatorTail.IsMatch(text.Substring(j))) return true;
        return false;
    }

    // a literal that is a piece of code: a statement (a semicolon, a brace,
    // an arrow) or a call with its argument list
    // literals that look like code and are data: [file, a piece of the literal]
    static readonly string[][] LiteralKeep =
    {
        new[] { "Tests/SessionTests.cs", "-Value preso;" },   // the text a PowerShell child writes into a file
    };

    // files whose fake sources test a detector and copy no real code
    static readonly HashSet<string> SyntheticFixtures = new HashSet<string>(StringComparer.Ordinal) { "Tests/ErrorEnumTests.cs" };

    static bool FixtureLike(string value)
    {
        return value.Contains(";") || value.Contains("=>") ||
               System.Text.RegularExpressions.Regex.IsMatch(value, @"\w\([^()]*\)|\w\($|\{(?![\d}])");
    }

    // inside a code span of a message: «...» or a back-quoted span
    static bool InCodeSpan(string text, int i, int j)
    {
        var before = text.Substring(0, i);
        if (before.Count(c => c == '«') > before.Count(c => c == '»') && text.IndexOf('»', j) >= 0) return true;
        if (before.Count(c => c == (char)96) % 2 == 1 && text.IndexOf((char)96, j) >= 0) return true;
        return false;
    }

    static readonly System.Text.RegularExpressions.Regex KeywordLead = new System.Text.RegularExpressions.Regex(
        @"\b(?:bool|int|long|double|string|var|object|new|return|out|ref|static|private|public|internal|void|class|typeof|nameof)\s+$");
    static readonly System.Text.RegularExpressions.Regex OperatorLead = new System.Text.RegularExpressions.Regex(
        @"(?:(?<![=!<>])=(?!=)|(?<!=)==(?!=)|!=|\|\||&&|\(|,)\s*$");
    static readonly System.Text.RegularExpressions.Regex OperatorTail = new System.Text.RegularExpressions.Regex(
        @"^\s*(?:=(?!=)|==(?!=)|!=|\|\||&&|;|\+\+|--|\)|,)");

    static bool IsLabel(SyntaxToken tok)
    {
        var arg = tok.Parent == null ? null : tok.Parent.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
        if (arg == null) return false;
        var list = arg.Parent as ArgumentListSyntax;
        if (list == null || list.Arguments.IndexOf(arg) != 0) return false;
        var inv = list.Parent as InvocationExpressionSyntax;
        if (inv == null) return false;
        var name = inv.Expression is IdentifierNameSyntax ? ((IdentifierNameSyntax)inv.Expression).Identifier.ValueText
                 : inv.Expression is MemberAccessExpressionSyntax ? ((MemberAccessExpressionSyntax)inv.Expression).Name.Identifier.ValueText : null;
        return name != null && LabelMethods.Contains(name);
    }

    static bool IsRegexArgument(SyntaxToken tok)
    {
        SyntaxNode n = tok.Parent;
        while (n != null && (n is LiteralExpressionSyntax || n is BinaryExpressionSyntax || n is ParenthesizedExpressionSyntax ||
                             n is InterpolatedStringExpressionSyntax || n is InterpolatedStringTextSyntax || n is InterpolationSyntax))
            n = n.Parent;
        var arg = n as ArgumentSyntax;
        if (arg == null) return false;
        var call = arg.Parent == null ? null : arg.Parent.Parent;
        var oc = call as ObjectCreationExpressionSyntax;
        if (oc != null) return oc.Type.ToString().EndsWith("Regex");
        var inv = call as InvocationExpressionSyntax;
        if (inv == null) return false;
        var access = inv.Expression as MemberAccessExpressionSyntax;
        return access != null && access.Expression.ToString().EndsWith("Regex");
    }
    // every type or member declared in the tree with a name of the map, by name
    static Dictionary<string, HashSet<string>> declaredNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    static Dictionary<string, string> declaredEnglish = new Dictionary<string, string>(StringComparer.Ordinal);

    static bool IsTypeOrMember(ISymbol s)
    {
        return s.Kind == SymbolKind.NamedType || s.Kind == SymbolKind.Method || s.Kind == SymbolKind.Property ||
               s.Kind == SymbolKind.Field || s.Kind == SymbolKind.Event;
    }

    // A literal handed to reflection that names a type or a member this batch
    // renames: each dotted or nested part that is such a name is renamed.
    static void RenameReflectionLiterals()
    {
        // a name is renamed in a literal when the batch renames a type or member
        // that has it and EVERY type or member of the tree that has it goes to
        // the same English name (now or in a later batch): a literal does not
        // say which of them it means
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in declaredNames)
        {
            if (!kv.Value.Any(k => renamedSymbols.Contains(k))) continue;
            var ens = kv.Value.Select(k => declaredEnglish[k]).Distinct().ToList();
            if (ens.Count == 1 && ens[0] != null) { names.Add(kv.Key); literalEn[kv.Key] = ens[0]; }
            else notes.Add("name with two English names, not renamed in literals: " + kv.Key + " -> " + string.Join(", ", ens));
        }
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in literalCandidates)
        {
            var tok = c.Item1.Token;
            var file = tok.SyntaxTree.FilePath;
            var key = RelPath(file) + ":" + tok.SpanStart.ToString(CultureInfo.InvariantCulture);
            if (!done.Add(key)) continue;
            var text = tok.Text;
            var sb = new StringBuilder();
            int i = 0; bool changed = false;
            while (i < text.Length)
            {
                if (char.IsLetter(text[i]) || text[i] == '_')
                {
                    int j = i;
                    while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                    var w = text.Substring(i, j - i);
                    bool boundary = (i == 0 || "\".+@".IndexOf(text[i - 1]) >= 0) && (j == text.Length || "\".+".IndexOf(text[j]) >= 0);
                    if (boundary && names.Contains(w)) { sb.Append(literalEn[w]); changed = true; }
                    else sb.Append(w);
                    i = j; continue;
                }
                sb.Append(text[i]); i++;
            }
            if (!changed) { notes.Add("literal not renamed: " + RelPath(file) + ":" + Line(tok) + ": " + text + " (" + c.Item2 + ")"); continue; }
            SortedDictionary<int, string[]> list;
            if (!edits.TryGetValue(file, out list)) { list = new SortedDictionary<int, string[]>(); edits[file] = list; }
            list[tok.SpanStart] = new[] { text, sb.ToString(), "literal:" + c.Item2 };
        }
    }

    static int Line(SyntaxToken t) { return t.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }

    static bool IsDeclarationIdentifier(SyntaxNode node, SyntaxToken t)
    {
        if (node is BaseTypeDeclarationSyntax) return ((BaseTypeDeclarationSyntax)node).Identifier == t;
        if (node is DelegateDeclarationSyntax) return ((DelegateDeclarationSyntax)node).Identifier == t;
        if (node is MethodDeclarationSyntax) return ((MethodDeclarationSyntax)node).Identifier == t;
        if (node is ConstructorDeclarationSyntax) return ((ConstructorDeclarationSyntax)node).Identifier == t;
        if (node is DestructorDeclarationSyntax) return ((DestructorDeclarationSyntax)node).Identifier == t;
        if (node is PropertyDeclarationSyntax) return ((PropertyDeclarationSyntax)node).Identifier == t;
        if (node is EventDeclarationSyntax) return ((EventDeclarationSyntax)node).Identifier == t;
        if (node is VariableDeclaratorSyntax) return ((VariableDeclaratorSyntax)node).Identifier == t;
        if (node is EnumMemberDeclarationSyntax) return ((EnumMemberDeclarationSyntax)node).Identifier == t;
        if (node is ParameterSyntax) return ((ParameterSyntax)node).Identifier == t;
        if (node is TypeParameterSyntax) return ((TypeParameterSyntax)node).Identifier == t;
        if (node is LocalFunctionStatementSyntax) return ((LocalFunctionStatementSyntax)node).Identifier == t;
        if (node is ForEachStatementSyntax) return ((ForEachStatementSyntax)node).Identifier == t;
        if (node is CatchDeclarationSyntax) return ((CatchDeclarationSyntax)node).Identifier == t;
        if (node is SingleVariableDesignationSyntax) return ((SingleVariableDesignationSyntax)node).Identifier == t;
        if (node is FromClauseSyntax) return ((FromClauseSyntax)node).Identifier == t;
        if (node is LetClauseSyntax) return ((LetClauseSyntax)node).Identifier == t;
        if (node is JoinClauseSyntax) return ((JoinClauseSyntax)node).Identifier == t;
        if (node is JoinIntoClauseSyntax) return ((JoinIntoClauseSyntax)node).Identifier == t;
        if (node is QueryContinuationSyntax) return ((QueryContinuationSyntax)node).Identifier == t;
        if (node is LabeledStatementSyntax) return ((LabeledStatementSyntax)node).Identifier == t;
        return false;
    }

    // ── clashes ─────────────────────────────────────────────────────────────
    static void CheckClashes(Group g)
    {
        foreach (var kv in renamedByKey)
        {
            var s = kv.Value;
            if (s.Locations.All(l => !l.IsInSource || !g.Compilation.SyntaxTrees.Contains(l.SourceTree))) continue;
            var en = EnglishOf(s);
            if (s.Kind == SymbolKind.NamedType)
            {
                var container = (INamespaceOrTypeSymbol)s.ContainingType ?? s.ContainingNamespace;
                foreach (var other in container.GetTypeMembers(en))
                    if (!Same(other, s) && !WillBe(other, en)) problems.Add("clash: type " + Describe(s) + " -> " + en + ", which " + Describe(other) + " already is");
                continue;
            }
            if (s.ContainingType != null && (s.Kind == SymbolKind.Method || s.Kind == SymbolKind.Property || s.Kind == SymbolKind.Field || s.Kind == SymbolKind.Event))
            {
                for (var t = s.ContainingType; t != null; t = t.BaseType)
                    foreach (var other in t.GetMembers(en))
                    {
                        if (Same(other, s) || WillBe(other, en)) continue;
                        if (s.Kind == SymbolKind.Method && other.Kind == SymbolKind.Method && DifferentSignature((IMethodSymbol)s, (IMethodSymbol)other))
                        { notes.Add("overload: " + Describe(s) + " -> " + en + " sits next to " + Describe(other)); continue; }
                        problems.Add("clash: member " + Describe(s) + " -> " + en + ", which " + Describe(other) + " already is");
                    }
                // a nested type of the same name
                foreach (var other in s.ContainingType.GetTypeMembers(en)) problems.Add("clash: member " + Describe(s) + " -> " + en + ", a nested type");
                continue;
            }
            if (s.Kind == SymbolKind.Local || s.Kind == SymbolKind.Parameter || s.Kind == SymbolKind.RangeVariable || s.Kind == SymbolKind.TypeParameter)
            {
                // every use of the name en inside the member that declares s must
                // not be another symbol (it would be captured by the rename), and
                // en must not name something s would hide at its uses
                var decl = s.DeclaringSyntaxReferences.FirstOrDefault();
                if (decl == null) continue;
                var member = decl.GetSyntax().AncestorsAndSelf().FirstOrDefault(n => n is MemberDeclarationSyntax || n is AccessorDeclarationSyntax) ?? decl.GetSyntax();
                var model = g.Compilation.GetSemanticModel(decl.SyntaxTree);
                foreach (var t in member.DescendantTokens())
                {
                    if (!t.IsKind(SyntaxKind.IdentifierToken) || t.ValueText != en) continue;
                    ISymbol other = null;
                    try { other = t.Parent is IdentifierNameSyntax ? model.GetSymbolInfo(t.Parent).Symbol : model.GetDeclaredSymbol(t.Parent); } catch { }
                    other = Owner(other);
                    if (other == null || Same(other, s) || WillBe(other, en)) continue;
                    problems.Add("clash: " + Describe(s) + " -> " + en + " in " + RelPath(decl.SyntaxTree.FilePath) + ", where " + en + " already names " + Describe(other));
                }
                // en visible at the declaration (a field, a property, a type): the
                // renamed local would hide it; a use of it in the scope would bind to the local
                var visible = model.LookupSymbols(decl.Span.Start, null, en);
                foreach (var v in visible)
                {
                    if (Same(v, s) || WillBe(v, en)) continue;
                    if (v.Kind == SymbolKind.Local || v.Kind == SymbolKind.Parameter || v.Kind == SymbolKind.RangeVariable)
                        problems.Add("clash: " + Describe(s) + " -> " + en + " in " + RelPath(decl.SyntaxTree.FilePath) + ", where the local " + en + " is in scope");
                    else notes.Add("hides: " + Describe(s) + " -> " + en + " in " + RelPath(decl.SyntaxTree.FilePath) + " hides " + Describe(v) + " (no use of it in the member)");
                }
            }
        }
    }

    static bool Same(ISymbol a, ISymbol b)
    {
        a = Owner(a); b = Owner(b);
        if (a == null || b == null) return false;
        if (SymbolEqualityComparer.Default.Equals(a, b)) return true;
        var locA = a.Locations.FirstOrDefault(l => l.IsInSource); var locB = b.Locations.FirstOrDefault(l => l.IsInSource);
        return locA != null && locB != null && RelPath(locA.SourceTree.FilePath) == RelPath(locB.SourceTree.FilePath) && locA.SourceSpan == locB.SourceSpan;
    }

    // another symbol that this batch also renames to the same English name
    // (the map merges inflections: no two of them share a scope by design)
    static bool WillBe(ISymbol other, string en)
    {
        other = Owner(other);
        if (other == null) return false;
        string otherEn;
        if (!map.TryGetValue(other.Name, out otherEn) || otherEn != en) return false;
        var loc = other.Locations.FirstOrDefault(l => l.IsInSource);
        return loc != null && InBatch(loc.SourceTree.FilePath) && other.Name != en;
    }

    static bool DifferentSignature(IMethodSymbol a, IMethodSymbol b)
    {
        if (a.Parameters.Length != b.Parameters.Length) return true;
        for (int i = 0; i < a.Parameters.Length; i++)
            if (!SymbolEqualityComparer.Default.Equals(a.Parameters[i].Type, b.Parameters[i].Type)) return true;
        return false;
    }

    static string Describe(ISymbol s)
    {
        var loc = s.Locations.FirstOrDefault(l => l.IsInSource);
        var where = loc == null ? "(metadata)" : RelPath(loc.SourceTree.FilePath) + ":" + (loc.GetLineSpan().StartLinePosition.Line + 1).ToString(CultureInfo.InvariantCulture);
        return s.Kind + " " + s.ToDisplayString() + " " + where;
    }
}
`;

// The literal pass is done here, after the C# helper says which symbols it
// renamed, by the JS side (see renameReflectionLiterals).

function findReferenceAssemblies() {
  const pf86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
  for (const v of ['v4.8.1', 'v4.8', 'v4.7.2']) {
    const d = path.join(pf86, 'Reference Assemblies', 'Microsoft', 'Framework', '.NETFramework', v);
    if (fs.existsSync(path.join(d, 'mscorlib.dll'))) return d;
  }
  return null;
}
function findSwRedist() {
  for (const d of [process.env.SW_REDIST, process.env.SW_INSTALL_PATH && path.join(process.env.SW_INSTALL_PATH, 'api', 'redist'),
    'C:\\Program Files\\SOLIDWORKS Corp\\SOLIDWORKS\\api\\redist']) {
    if (d && fs.existsSync(path.join(d, 'SolidWorks.Interop.sldworks.dll'))) return d;
  }
  return null;
}

// The C# projects: their compile items and references, read from the .csproj
// files; the probes from Checks/build-checks.ps1.
function projects(root) {
  const refDir = findReferenceAssemblies();
  if (!refDir) throw new Error('no .NET Framework reference assemblies (v4.8)');
  const sw = findSwRedist();
  if (!sw) throw new Error('no SOLIDWORKS interop (api\\redist) on this machine');
  const facades = path.join(refDir, 'Facades');
  const out = [];
  const csprojs = [
    ['library', 'Oce.Solid.Agent.Core/Oce.Solid.Agent.Core.csproj', []],
    ['bridge', 'Oce.Solid.Agent.csproj', ['library']],
    ['catalog', 'Oce.Solid.Agent.Catalog/Oce.Solid.Agent.Catalog.csproj', ['library']],
    ['installer', 'Installer/Installer.csproj', []],
    ['bench', 'Bench/Oce.Solid.Agent.Bench.csproj', []],
    ['tests', 'Tests/Oce.Solid.Agent.ContractTests.csproj', []],
  ];
  for (const [name, rel, refs] of csprojs) {
    const full = path.join(root, rel);
    const text = fs.readFileSync(full, 'utf8');
    const dir = path.dirname(full);
    const files = [];
    for (const m of text.matchAll(/<Compile Include="([^"$]+)"/g)) files.push(path.resolve(dir, m[1].replace(/\\/g, '/')));
    const meta = [];
    for (const m of text.matchAll(/<Reference Include="([^",]+)[^"]*"(?:\s*\/>|>([\s\S]*?)<\/Reference>)/g)) {
      const hint = m[2] && /<HintPath>([^<]+)<\/HintPath>/.exec(m[2]);
      if (hint) meta.push(path.join(sw, path.basename(hint[1].replace(/\\/g, '/'))));
      else { const p = path.join(refDir, m[1] + '.dll'); if (fs.existsSync(p)) meta.push(p); }
    }
    out.push({ name, refs, meta, files });
  }
  // the probes: Checks/<probe>/Program.cs, with the references build-checks.ps1 gives them
  const list = fs.readFileSync(path.join(root, 'Checks', 'build-checks.ps1'), 'utf8');
  for (const m of list.matchAll(/@\{\s*nome\s*=\s*'([^']+)'([^}]*)\}/g)) {
    const probe = m[1], flags = m[2];
    const meta = ['System.dll', 'System.Core.dll'].map(x => path.join(refDir, x));
    if (/sw\s*=\s*\$true/.test(flags)) meta.push(path.join(sw, 'SolidWorks.Interop.sldworks.dll'), path.join(sw, 'SolidWorks.Interop.swconst.dll'));
    if (/sim\s*=\s*\$true/.test(flags)) meta.push(path.join(sw, 'SolidWorks.Interop.cosworks.dll'));
    if (/util\s*=\s*\$true/.test(flags)) meta.push(path.join(sw, 'SolidWorks.Interop.gtswutilities.dll'));
    if (/dimx\s*=\s*\$true/.test(flags)) meta.push(path.join(sw, 'SolidWorks.Interop.swdimxpert.dll'));
    if (/disegno\s*=\s*\$true/.test(flags)) meta.push(path.join(refDir, 'System.Drawing.dll'));
    const refs = /lib\s*=\s*\$true/.test(flags) ? ['library'] : [];
    out.push({ name: 'probe:' + probe, refs, meta: meta.filter(p => fs.existsSync(p)), files: [path.join(root, 'Checks', probe, 'Program.cs')] });
  }
  const global = ['mscorlib.dll', 'System.dll', 'System.Core.dll', path.join('Facades', 'netstandard.dll')].map(x => path.join(refDir, x)).filter(p => fs.existsSync(p));
  void facades;
  return { list: out, global };
}

function loadIdentifierMap(root) {
  const text = fs.readFileSync(path.join(root, 'Checks', 'language', 'name-map.tsv'), 'utf8');
  const m = new Map();
  for (const line of text.split(/\r?\n/)) {
    const c = line.split('\t');
    if (c[0] !== 'identifier' || !c[2] || c[2] === c[1]) continue;
    m.set(c[1], { en: c[2], note: c[5] || '' });
  }
  return m;
}

function runCSharp(opts, batch) {
  const root = opts.root;
  const tokenStream = require('./token-stream.js');
  const helper = tokenStream.compileRoslynProgram(CSHARP, 'Window3Renamer');
  const { list, global } = projects(root);
  const idMap = loadIdentifierMap(root);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'w3-renamer-'));
  try {
    const lines = [];
    for (const g of global) lines.push('R\t' + g);
    for (const [it, v] of idMap) lines.push('M\t' + it + '\t' + v.en);
    for (const b of BATCHES[batch]) lines.push('B\t' + b);
    for (const x of REFLECTION_METHODS) lines.push('X\t' + x);
    for (const o of SYMBOL_OVERRIDES) lines.push('O\t' + o.join('\t'));
    for (const p of list) {
      lines.push('G\t' + p.name + '\t' + p.refs.join(',') + '\t' + p.meta.filter(m => !global.includes(m)).join('|'));
      for (const f of p.files) lines.push('F\t' + p.name + '\t' + f + '\t' + path.relative(root, f).replace(/\\/g, '/'));
    }
    const manifest = path.join(dir, 'manifest.txt');
    const out = path.join(dir, 'edits.tsv');
    fs.writeFileSync(manifest, lines.join('\n') + '\n', 'utf8');
    const r = cp.spawnSync(helper.exe, [helper.roslyn, manifest, out], { encoding: 'utf8', maxBuffer: 1 << 28 });
    if (r.status !== 0 || !fs.existsSync(out)) throw new Error('the renamer failed (exit ' + r.status + '):\n' + (r.stdout || '') + (r.stderr || ''));
    const res = { problems: [], notes: [], edits: [] };
    for (const l of fs.readFileSync(out, 'utf8').split(/\r?\n/)) {
      if (!l) continue;
      const c = l.split('\t');
      if (c[0] === 'P') res.problems.push(c[1]);
      else if (c[0] === 'N') res.notes.push(c[1]);
      else if (c[0] === 'E') res.edits.push({ file: c[1], at: +c[2], from: c[3], to: c[4], kind: c[5] });
    }
    return res;
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

// apply edits by position, from the end, each checked against its text
function applyEdits(root, edits, dry) {
  const byFile = new Map();
  for (const e of edits) { if (!byFile.has(e.file)) byFile.set(e.file, []); byFile.get(e.file).push(e); }
  let n = 0;
  for (const [file, list] of byFile) {
    const full = path.join(root, file);
    let text = fs.readFileSync(full, 'utf8');
    // Roslyn positions count UTF-16 code units of the text it read (BOM excluded)
    const bom = text.charCodeAt(0) === 0xfeff ? 1 : 0;
    list.sort((a, b) => b.at - a.at);
    for (const e of list) {
      const at = e.at + bom;
      if (text.substr(at, e.from.length) !== e.from) throw new Error(file + ':' + e.at + ': expected «' + e.from + '», found «' + text.substr(at, e.from.length) + '»');
      text = text.slice(0, at) + e.to + text.slice(at + e.from.length);
      n++;
    }
    if (!dry) fs.writeFileSync(full, text);
  }
  return { files: byFile.size, edits: n };
}

function run(opts) {
  const batch = opts.batch;
  if (!BATCHES[batch] && batch !== 'scripts' && batch !== 'mentions') throw new Error('unknown batch ' + batch + ' (one of ' + Object.keys(BATCHES).concat(['scripts', 'mentions']).join(', ') + ')');
  if (batch === 'scripts') return require('./window-3-scripts.js').run(opts);
  if (batch === 'mentions') return require('./window-3-mentions.js').run(opts);
  const res = runCSharp(opts, batch);
  for (const p of res.problems) console.log('PROBLEM: ' + p);
  if (opts.list) for (const n of res.notes) console.log('note: ' + n);
  else if (res.notes.length) console.log(res.notes.length + ' notes (--list prints them)');
  if (res.problems.length) { console.log('window 3, identifiers ' + batch + ': stopped, ' + res.problems.length + ' problems'); return 1; }
  const names = new Set(res.edits.map(e => e.from));
  const a = applyEdits(opts.root, res.edits, opts.dry);
  console.log('window 3, identifiers ' + batch + (opts.dry ? ' (dry run)' : '') + ': ' + a.edits + ' edits in ' + a.files + ' files, ' + names.size + ' names');
  return 0;
}

module.exports = { run, runCSharp, projects, BATCHES, REFLECTION_METHODS, SYMBOL_OVERRIDES, applyEdits, loadIdentifierMap };
