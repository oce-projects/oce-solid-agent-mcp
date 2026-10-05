#!/usr/bin/env node
// =============================================================================
//  window-5-scan.js - the text literals of a program, found by its parser
//  (W3.1-bis.8; the reading half of window-5-messages.js)
// =============================================================================
//
//  Finds, for a C#, JavaScript, PowerShell or JSON file, every STRING UNIT with
//  the position of the text inside it. The units are found by the parser of
//  the language (Roslyn for C#, acorn for JavaScript, the PowerShell parser
//  for PowerShell), never by looking for a quote in the raw text; JSON has a
//  small scanner written to its grammar.
//
//  A UNIT is one piece of text as the program builds it, the way
//  snapshot.js counts it: a literal alone, an interpolated string, a template
//  literal, or a chain of `+` whose text operands are joined and whose other
//  operands are HOLES. A unit is a list of LEAVES in source order:
//
//    t  a piece of text the translation may change: a literal, the text of an
//       interpolated string between two holes, a quasi of a template, the
//       text between two variables of a PowerShell string, a JSON value
//       { s, e   the whole token, delimiters included,
//         is, ie the text inside the delimiters, as the source spells it,
//         style  how it is spelled (regular, verbatim, interp-regular,
//                interp-verbatim, single, double, template, json, regex...),
//         cooked what the program sees (escapes resolved) }
//    f  a piece of text that is part of the unit but that a translation never
//       changes (a character literal in a chain)
//    h  a hole: a value computed at run time (src is its source)
//
//  Offsets are in UTF-16 units of the text WITHOUT its byte order mark, line
//  ends as the file has them. Every leaf is checked to start where the parser
//  said: a helper that counted wrongly is stopped here.
//
//  A unit is named `member:sN` (N counts the units of that member in the file,
//  in source order, as snapshot.js counts them for C#) and a JSON value by its
//  path. The names do not change when only text inside literals does, which
//  is what lets a chunk made before a translation find its unit after it.
//
//  THE ENCODERS turn the new text of a leaf back into the source spelling of
//  its style (`encodeInner`): the quote, the escapes, the doubled braces of an
//  interpolated string, the backtick of PowerShell. window-5-messages.js
//  reads the file again after writing and compares what the parser sees.
//
//  USAGE (a tool for the other script, and for looking)
//    node --expose-internals Checks/language/window-5-scan.js --units <file>
//    node --expose-internals Checks/language/window-5-scan.js --self-test
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');

const TS = require('./token-stream.js');

class ScanError extends Error {}
function cannot(msg) { throw new ScanError(msg); }

// ── acorn, as token-stream.js reaches it ────────────────────────────────────
function loadAcorn() {
  const tries = [];
  if (process.env.ACORN_PATH) tries.push(process.env.ACORN_PATH);
  tries.push('acorn', 'internal/deps/acorn/acorn/dist/acorn');
  for (const t of tries) {
    try { return require(t); } catch (e) { /* next */ }
  }
  return null;
}
const acorn = loadAcorn();

const LANG_OF = { cs: 'csharp', js: 'javascript', ps: 'powershell' };

function lineStarts(text) {
  const s = [0];
  for (let i = 0; i < text.length; i++) if (text.charCodeAt(i) === 10) s.push(i + 1);
  return s;
}
function lineAt(starts, offset) {
  let lo = 0, hi = starts.length - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (starts[mid] <= offset) lo = mid; else hi = mid - 1;
  }
  return lo + 1;
}

// ── C#: the Roslyn helper ───────────────────────────────────────────────────
const CSHARP_HELPER = String.raw`// String units of C# files through Roslyn (window-5-scan.js).
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

static class W5Entry
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
        return W5Scan.Run(args[1], args[2], args[3]);
    }
}

static class W5Scan
{
    class Leaf { public string K; public int S, E, IS, IE; public string Style, Cooked, Src; }

    public static int Run(string listFile, string outFile, string callList)
    {
        var calls = new HashSet<string>(callList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);
        var sb = new StringBuilder();
        foreach (var file in File.ReadAllLines(listFile, Encoding.UTF8))
        {
            if (file.Length == 0) continue;
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(SourceText.From(text), options);
            var root = tree.GetRoot();
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Take(5).ToList();
            var units = new List<string>();
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in root.DescendantNodes())
            {
                if (!IsUnitRoot(node)) continue;
                var leaves = new List<Leaf>();
                Collect((ExpressionSyntax)node, leaves);
                if (!leaves.Any(l => l.K != "h")) continue;
                var member = MemberOf(node);
                int n;
                ordinals.TryGetValue(member, out n);
                n++;
                ordinals[member] = n;
                var ctx = new List<string>();
                string to = Context(node, ctx);
                var ls = node.GetLocation().GetLineSpan();
                units.Add("{\"member\":" + Json(member) + ",\"n\":" + n.ToString(CultureInfo.InvariantCulture) +
                          ",\"line\":" + (ls.StartLinePosition.Line + 1).ToString(CultureInfo.InvariantCulture) +
                          ",\"endLine\":" + (ls.EndLinePosition.Line + 1).ToString(CultureInfo.InvariantCulture) +
                          ",\"ctx\":[" + string.Join(",", ctx.Select(Json)) + "],\"to\":" + (to == null ? "null" : Json(to)) + ",\"inv\":" + FirstArgument(node) +
                          ",\"leaves\":[" + string.Join(",", leaves.Select(LeafJson)) + "]}");
            }
            var found = new List<string>();
            var callOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = CalleeName(inv.Expression);
                if (name == null || !calls.Contains(name)) continue;
                var member = MemberOf(inv);
                int n;
                var key = name + "\u0001" + member;
                callOrdinals.TryGetValue(key, out n);
                n++;
                callOrdinals[key] = n;
                found.Add("{\"callee\":" + Json(name) + ",\"member\":" + Json(member) + ",\"n\":" + n.ToString(CultureInfo.InvariantCulture) +
                          ",\"line\":" + (inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1).ToString(CultureInfo.InvariantCulture) + "}");
            }
            sb.Append("{\"file\":").Append(Json(file)).Append(",\"errors\":[").Append(string.Join(",", errors.Select(Json)))
              .Append("],\"units\":[").Append(string.Join(",", units)).Append("],\"calls\":[").Append(string.Join(",", found)).Append("]}\n");
        }
        File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        return 0;
    }

    static ExpressionSyntax StripParens(ExpressionSyntax e)
    {
        while (e is ParenthesizedExpressionSyntax) e = ((ParenthesizedExpressionSyntax)e).Expression;
        return e;
    }

    static bool InsideChain(SyntaxNode n)
    {
        var p = n.Parent;
        while (p is ParenthesizedExpressionSyntax) p = p.Parent;
        return p != null && p.IsKind(SyntaxKind.AddExpression);
    }

    static bool IsUnitRoot(SyntaxNode node)
    {
        bool candidate = node.IsKind(SyntaxKind.AddExpression) || node.IsKind(SyntaxKind.StringLiteralExpression) ||
                         node is InterpolatedStringExpressionSyntax;
        return candidate && !InsideChain(node);
    }

    static void Collect(ExpressionSyntax e, List<Leaf> leaves)
    {
        e = StripParens(e);
        if (e.IsKind(SyntaxKind.AddExpression))
        {
            var b = (BinaryExpressionSyntax)e;
            Collect(b.Left, leaves);
            Collect(b.Right, leaves);
            return;
        }
        if (e.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var tok = ((LiteralExpressionSyntax)e).Token;
            var t = tok.Text;
            var leaf = new Leaf { K = "t", S = tok.Span.Start, E = tok.Span.End, Cooked = tok.ValueText };
            if (t.StartsWith("@\"")) { leaf.Style = "verbatim"; leaf.IS = leaf.S + 2; leaf.IE = leaf.E - 1; }
            else if (t.StartsWith("\"\"\"")) { leaf.Style = "raw"; leaf.IS = leaf.S; leaf.IE = leaf.E; }
            else if (t.EndsWith("u8") || t.EndsWith("U8")) { leaf.Style = "u8"; leaf.IS = leaf.S; leaf.IE = leaf.E; }
            else { leaf.Style = "regular"; leaf.IS = leaf.S + 1; leaf.IE = leaf.E - 1; }
            leaves.Add(leaf);
            return;
        }
        if (e.IsKind(SyntaxKind.CharacterLiteralExpression))
        {
            var tok = ((LiteralExpressionSyntax)e).Token;
            leaves.Add(new Leaf { K = "f", S = tok.Span.Start, E = tok.Span.End, Cooked = tok.ValueText });
            return;
        }
        var interpolated = e as InterpolatedStringExpressionSyntax;
        if (interpolated != null)
        {
            var start = interpolated.StringStartToken.Text;
            var style = start.Contains("\"\"\"") ? "interp-raw" : (start.Contains("@") ? "interp-verbatim" : "interp-regular");
            foreach (var c in interpolated.Contents)
            {
                var t = c as InterpolatedStringTextSyntax;
                if (t != null)
                    leaves.Add(new Leaf { K = "t", S = t.TextToken.Span.Start, E = t.TextToken.Span.End, IS = t.TextToken.Span.Start, IE = t.TextToken.Span.End, Style = style, Cooked = t.TextToken.ValueText });
                else
                    leaves.Add(new Leaf { K = "h", S = c.Span.Start, E = c.Span.End, Src = Clip(c.ToString()) });
            }
            return;
        }
        leaves.Add(new Leaf { K = "h", S = e.Span.Start, E = e.Span.End, Src = Clip(e.ToString()) });
    }

    // [start,end] of the argument list when the node is an argument of a call (a format string, with its values), else null
    static string FirstArgument(SyntaxNode node)
    {
        SyntaxNode p = node.Parent;
        while (p is ParenthesizedExpressionSyntax) p = p.Parent;
        var arg = p as ArgumentSyntax;
        if (arg == null) return "null";
        var list = arg.Parent as ArgumentListSyntax;
        if (list == null || !(list.Parent is InvocationExpressionSyntax)) return "null";
        return "[" + list.Span.Start.ToString(CultureInfo.InvariantCulture) + "," + list.Span.End.ToString(CultureInfo.InvariantCulture) + "]";
    }

    static string Clip(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 90 ? s.Substring(0, 87) + "..." : s;
    }

    static string LeafJson(Leaf l)
    {
        var i = CultureInfo.InvariantCulture;
        if (l.K == "t") return "[\"t\"," + l.S.ToString(i) + "," + l.E.ToString(i) + "," + l.IS.ToString(i) + "," + l.IE.ToString(i) + "," + Json(l.Style) + "," + Json(l.Cooked) + "]";
        if (l.K == "f") return "[\"f\"," + l.S.ToString(i) + "," + l.E.ToString(i) + "," + Json(l.Cooked) + "]";
        return "[\"h\"," + l.S.ToString(i) + "," + l.E.ToString(i) + "," + Json(l.Src) + "]";
    }

    // The enclosing calls, nearest first, as "callee:argument index", and the
    // name the text is assigned to, when there is one.
    static string Context(SyntaxNode node, List<string> ctx)
    {
        string to = null;
        SyntaxNode child = node;
        for (var p = node.Parent; p != null && ctx.Count < 4; child = p, p = p.Parent)
        {
            var arg = p as ArgumentSyntax;
            if (arg != null)
            {
                var list = arg.Parent as ArgumentListSyntax;
                if (list != null)
                {
                    int idx = list.Arguments.IndexOf(arg);
                    var inv = list.Parent as InvocationExpressionSyntax;
                    if (inv != null) ctx.Add((CalleeName(inv.Expression) ?? "?") + ":" + idx);
                    var oc = list.Parent as ObjectCreationExpressionSyntax;
                    if (oc != null) ctx.Add("new " + oc.Type.ToString() + ":" + idx);
                }
                continue;
            }
            if (to == null)
            {
                var asg = p as AssignmentExpressionSyntax;
                if (asg != null && asg.Right == child) to = Clip(asg.Left.ToString());
                var decl = p as VariableDeclaratorSyntax;
                if (decl != null) to = decl.Identifier.ValueText;
                var prop = p as PropertyDeclarationSyntax;
                if (prop != null) to = prop.Identifier.ValueText;
                var ret = p as ReturnStatementSyntax;
                if (ret != null && to == null) to = "return";
            }
            if (p is StatementSyntax || p is MemberDeclarationSyntax) break;
        }
        return to;
    }

    static string CalleeName(ExpressionSyntax e)
    {
        var id = e as IdentifierNameSyntax;
        if (id != null) return id.Identifier.ValueText;
        var g = e as GenericNameSyntax;
        if (g != null) return g.Identifier.ValueText;
        var m = e as MemberAccessExpressionSyntax;
        if (m != null) return CalleeName(m.Name);
        var mb = e as MemberBindingExpressionSyntax;
        if (mb != null) return CalleeName(mb.Name);
        return null;
    }

    static string MemberOf(SyntaxNode node)
    {
        VariableDeclaratorSyntax declarator = null;
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is VariableDeclaratorSyntax) declarator = (VariableDeclaratorSyntax)p;
            var method = p as MethodDeclarationSyntax;
            if (method != null) return method.Identifier.ValueText;
            var ctor = p as ConstructorDeclarationSyntax;
            if (ctor != null) return ctor.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor";
            if (p is DestructorDeclarationSyntax) return "Finalize";
            var property = p as PropertyDeclarationSyntax;
            if (property != null) return property.Identifier.ValueText;
            if (p is IndexerDeclarationSyntax) return "this[]";
            var op = p as OperatorDeclarationSyntax;
            if (op != null) return "operator" + op.OperatorToken.ValueText;
            var conversion = p as ConversionOperatorDeclarationSyntax;
            if (conversion != null) return "operator " + conversion.ImplicitOrExplicitKeyword.ValueText + " " + conversion.Type;
            var ev = p as EventDeclarationSyntax;
            if (ev != null) return ev.Identifier.ValueText;
            var field = p as BaseFieldDeclarationSyntax;
            if (field != null)
                return (declarator ?? field.Declaration.Variables.First()).Identifier.ValueText;
            var enumMember = p as EnumMemberDeclarationSyntax;
            if (enumMember != null) return enumMember.Identifier.ValueText;
            var delegateDeclaration = p as DelegateDeclarationSyntax;
            if (delegateDeclaration != null) return delegateDeclaration.Identifier.ValueText;
            var type = p as BaseTypeDeclarationSyntax;
            if (type != null) return "<" + type.Identifier.ValueText + ">";
        }
        return "<file>";
    }

    static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s ?? "")
        {
            if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
            else if (ch < 0x20 || ch > 0x7e) sb.Append("\\u").Append(((int)ch).ToString("x4"));
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}
`;

// ── PowerShell: the parser's AST in one process for all the files ───────────
const POWERSHELL_HELPER = String.raw`param([string]$ListFile, [string]$OutFile)
# String units of PowerShell files through the PowerShell parser (window-5-scan.js).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web
$enc = [System.Web.HttpUtility]
function J($s) { return $enc::JavaScriptStringEncode([string]$s, $true) }
$AstNs = 'System.Management.Automation.Language'
function IsPlus($n) { return ($n -is [System.Management.Automation.Language.BinaryExpressionAst]) -and ([string]$n.Operator -eq 'Plus') }
function IsText($n) {
    if ($n -is [System.Management.Automation.Language.StringConstantExpressionAst]) { return ([string]$n.StringConstantType -ne 'BareWord') }
    return ($n -is [System.Management.Automation.Language.ExpandableStringExpressionAst])
}
function Flatten($n, $out) {
    if (IsPlus $n) { Flatten $n.Left $out; Flatten $n.Right $out; return }
    if ($n -is [System.Management.Automation.Language.StringConstantExpressionAst] -and ([string]$n.StringConstantType -ne 'BareWord')) {
        [void]$out.Add('["s",' + $n.Extent.StartOffset + ',' + $n.Extent.EndOffset + ',' + (J $n.StringConstantType) + ',' + (J $n.Value) + ']')
        return
    }
    if ($n -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
        $holes = @($n.NestedExpressions | ForEach-Object { '[' + $_.Extent.StartOffset + ',' + $_.Extent.EndOffset + ']' })
        [void]$out.Add('["e",' + $n.Extent.StartOffset + ',' + $n.Extent.EndOffset + ',' + (J $n.StringConstantType) + ',[' + ($holes -join ',') + ']]')
        return
    }
    $t = $n.Extent.Text -replace '\s+', ' '
    if ($t.Length -gt 90) { $t = $t.Substring(0, 87) + '...' }
    [void]$out.Add('["h",' + $n.Extent.StartOffset + ',' + $n.Extent.EndOffset + ',' + (J $t) + ']')
}
$sb = New-Object System.Text.StringBuilder
foreach ($f in [IO.File]::ReadAllLines($ListFile, [Text.Encoding]::UTF8)) {
    if (-not $f) { continue }
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$tokens, [ref]$errors)
    $errs = @($errors | Select-Object -First 5 | ForEach-Object { J ($_.Extent.StartLineNumber.ToString() + ': ' + $_.Message) })
    $units = New-Object System.Collections.Generic.List[string]
    $ordinals = @{}
    $all = $ast.FindAll({ param($n) $true }, $true)
    foreach ($n in $all) {
        if (-not ((IsText $n) -or (IsPlus $n))) { continue }
        if (IsPlus $n.Parent) { continue }
        $leaves = New-Object System.Collections.Generic.List[string]
        Flatten $n $leaves
        $hasText = $false
        foreach ($l in $leaves) { if (-not $l.StartsWith('["h"')) { $hasText = $true; break } }
        if (-not $hasText) { continue }
        $member = '<file>'
        for ($p = $n.Parent; $p -ne $null; $p = $p.Parent) {
            if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { $member = $p.Name; break }
        }
        if ($ordinals.ContainsKey($member)) { $ordinals[$member] = $ordinals[$member] + 1 } else { $ordinals[$member] = 1 }
        $ctx = New-Object System.Collections.Generic.List[string]
        $child = $n
        for ($p = $n.Parent; $p -ne $null -and $ctx.Count -lt 3; $child = $p, $p = $p.Parent) {
            if ($p -is [System.Management.Automation.Language.CommandAst]) {
                $idx = 0
                for ($i = 0; $i -lt $p.CommandElements.Count; $i++) { if ($p.CommandElements[$i] -eq $child) { $idx = $i; break } }
                $nm = $p.GetCommandName()
                if (-not $nm) { $nm = '?' }
                [void]$ctx.Add((J ($nm + ':' + $idx)))
            }
            if ($p -is [System.Management.Automation.Language.StatementAst] -and -not ($p -is [System.Management.Automation.Language.PipelineAst])) { break }
        }
        $to = 'null'
        for ($p = $n.Parent; $p -ne $null; $p = $p.Parent) {
            if ($p -is [System.Management.Automation.Language.AssignmentStatementAst]) { $to = J ($p.Left.Extent.Text -replace '\s+', ' '); break }
            if ($p -is [System.Management.Automation.Language.StatementAst] -and -not ($p -is [System.Management.Automation.Language.PipelineAst])) { break }
        }
        [void]$units.Add('{"member":' + (J $member) + ',"n":' + $ordinals[$member] + ',"line":' + $n.Extent.StartLineNumber + ',"endLine":' + $n.Extent.EndLineNumber + ',"ctx":[' + ($ctx -join ',') + '],"to":' + $to + ',"leaves":[' + ($leaves -join ',') + ']}')
    }
    [void]$sb.Append('{"file":' + (J $f) + ',"errors":[' + ($errs -join ',') + '],"units":[' + ($units -join ',') + ']}' + [char]10)
}
[IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
`;

function powershellExe() { return process.platform === 'win32' ? 'powershell.exe' : 'pwsh'; }

function scratch() { return fs.mkdtempSync(path.join(os.tmpdir(), 'window5-scan-')); }

function runHelper(lang, files, extraArg) {
  const result = new Map();
  if (!files.length) return result;
  const dir = scratch();
  try {
    const list = path.join(dir, 'files.txt');
    const out = path.join(dir, 'scan.jsonl');
    fs.writeFileSync(list, files.join('\n') + '\n', 'utf8');
    let r;
    if (lang === 'cs') {
      const h = TS.compileRoslynProgram(CSHARP_HELPER, 'Window5Scan');
      r = cp.spawnSync(h.exe, [h.roslyn, list, out, extraArg || ''], { encoding: 'utf8', maxBuffer: 256 << 20, windowsHide: true });
    } else {
      const script = path.join(dir, 'scan.ps1');
      fs.writeFileSync(script, '﻿' + POWERSHELL_HELPER, 'utf8');
      r = cp.spawnSync(powershellExe(), ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script, '-ListFile', list, '-OutFile', out],
        { encoding: 'utf8', maxBuffer: 256 << 20, windowsHide: true });
    }
    if (r.error) cannot('the ' + lang + ' scanner does not start: ' + r.error.message);
    if (r.status !== 0 || !fs.existsSync(out)) cannot('the ' + lang + ' scanner failed (exit ' + r.status + '):\n' + (r.stdout || '') + (r.stderr || ''));
    for (const line of fs.readFileSync(out, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      const o = JSON.parse(line);
      if (o.errors.length) cannot(o.file + ' does not parse as ' + lang + ':\n    ' + o.errors.join('\n    '));
      result.set(path.resolve(o.file), o);
    }
    for (const f of files) if (!result.has(path.resolve(f))) cannot('the ' + lang + ' scanner gave nothing for ' + f);
    return result;
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

// ── building units from what the helpers say ────────────────────────────────
function mkUnit(file, lang, raw, leaves, extra) {
  return Object.assign({ file, lang, member: raw.member, n: raw.n, id: raw.member + ':s' + raw.n, line: raw.line, endLine: raw.endLine,
    ctx: raw.ctx || [], to: raw.to || null, inv: raw.inv || null, leaves }, extra || {});
}

function csLeaves(raw, text) {
  return raw.leaves.map(a => {
    if (a[0] === 't') return { k: 't', s: a[1], e: a[2], is: a[3], ie: a[4], style: a[5], cooked: a[6] };
    if (a[0] === 'f') return { k: 'f', s: a[1], e: a[2], cooked: a[3] };
    return { k: 'h', s: a[1], e: a[2], src: a[3] };
  });
}

// PowerShell, double-quoted: the text as the program sees it
function psDecodeDouble(raw, here) {
  let out = '';
  for (let i = 0; i < raw.length; i++) {
    const c = raw[i];
    if (c === '`') {
      const d = raw[i + 1];
      if (d === undefined) { out += c; continue; }
      i++;
      const map = { n: '\n', r: '\r', t: '\t', '0': '\0', a: '\x07', b: '\b', f: '\f', v: '\v' };
      out += Object.prototype.hasOwnProperty.call(map, d) ? map[d] : d;
      continue;
    }
    if (c === '"' && raw[i + 1] === '"' && !here) { out += '"'; i++; continue; }
    out += c;
  }
  return out;
}
function psDecodeSingle(raw, here) { return here ? raw : raw.replace(/''/g, "'"); }

function psLeaves(rawUnit, text) {
  const leaves = [];
  for (const a of rawUnit.leaves) {
    if (a[0] === 'h') { leaves.push({ k: 'h', s: a[1], e: a[2], src: a[3] }); continue; }
    const s = a[1], e = a[2], type = a[3];
    const here = /HereString/.test(type);
    const single = /^SingleQuoted/.test(type);
    let is, ie;
    if (here) {
      const open = /^@['"]\r?\n/.exec(text.slice(s, s + 8));
      if (!open) cannot('a here-string without its opening line at ' + s);
      is = s + open[0].length;
      const close = /\r?\n['"]@$/.exec(text.slice(s, e));
      ie = close ? s + close.index : e - 2;
      if (ie < is) ie = is;
    } else { is = s + 1; ie = e - 1; }
    if (a[0] === 's') {
      const rawInner = text.slice(is, ie);
      const cooked = single ? psDecodeSingle(rawInner, here) : psDecodeDouble(rawInner, here);
      const norm = (x) => x.replace(/\r\n/g, '\n');
      leaves.push({ k: 't', s, e, is, ie, style: type, cooked: norm(cooked), exotic: norm(cooked) !== norm(a[4]) });
    } else {
      // an expandable string: the text between its nested expressions
      const holes = a[4].slice().sort((x, y) => x[0] - y[0]);
      let at = is;
      const pieces = [];
      for (const [hs, he] of holes) {
        if (hs < at || he > ie + 1) { pieces.length = 0; break; }
        pieces.push({ k: 't', s: at, e: hs, is: at, ie: hs, style: type });
        pieces.push({ k: 'h', s: hs, e: he, src: text.slice(hs, he).replace(/\s+/g, ' ').slice(0, 90) });
        at = he;
      }
      pieces.push({ k: 't', s: at, e: ie, is: at, ie, style: type });
      for (const p of pieces) {
        if (p.k === 't') {
          p.cooked = psDecodeDouble(text.slice(p.is, p.ie), here).replace(/\r\n/g, '\n');
          // a backtick at the end of a part would escape the first character of the next piece
          p.exotic = /(^|[^`])(``)*`$/.test(text.slice(p.is, p.ie)) && p.ie < ie;
        }
        leaves.push(p);
      }
    }
  }
  // empty text pieces at the edges of an expandable string are not units of text
  return leaves;
}

// ── JavaScript: acorn ───────────────────────────────────────────────────────
function jsParse(text) {
  if (!acorn) cannot('no JavaScript parser: neither a local acorn package nor the copy inside Node (node --expose-internals)');
  let last;
  for (const sourceType of ['script', 'module']) {
    try { return acorn.parse(text, { ecmaVersion: 'latest', sourceType, allowHashBang: true, allowReturnOutsideFunction: true }); } catch (e) { last = e; }
  }
  cannot('JavaScript does not parse: ' + last.message);
}

function jsScan(file, text) {
  const ast = jsParse(text);
  const starts = lineStarts(text);
  const units = [];
  const ordinals = new Map();
  const clip = (s) => { s = s.replace(/\s+/g, ' '); return s.length > 90 ? s.slice(0, 87) + '...' : s; };
  const isPlus = (n) => n && n.type === 'BinaryExpression' && n.operator === '+';
  const leavesOf = (e, out) => {
    if (isPlus(e)) { leavesOf(e.left, out); leavesOf(e.right, out); return; }
    if (e.type === 'Literal' && typeof e.value === 'string') {
      out.push({ k: 't', s: e.start, e: e.end, is: e.start + 1, ie: e.end - 1, style: text[e.start] === '"' ? 'double' : 'single', cooked: e.value });
      return;
    }
    if (e.type === 'TemplateLiteral') {
      e.quasis.forEach((q, i) => {
        if (q.value.cooked === null || q.value.cooked === undefined) out.push({ k: 't', s: q.start, e: q.end, is: q.start, ie: q.end, style: 'template-bad', cooked: q.value.raw, exotic: true });
        else out.push({ k: 't', s: q.start, e: q.end, is: q.start, ie: q.end, style: 'template', cooked: q.value.cooked });
        if (i < e.expressions.length) out.push({ k: 'h', s: e.expressions[i].start, e: e.expressions[i].end, src: clip(text.slice(e.expressions[i].start, e.expressions[i].end)) });
      });
      return;
    }
    out.push({ k: 'h', s: e.start, e: e.end, src: clip(text.slice(e.start, e.end)) });
  };
  const nameOf = (n, parent) => {
    if (n.type === 'FunctionDeclaration' && n.id) return n.id.name;
    if (parent && parent.type === 'VariableDeclarator' && parent.id && parent.id.name) return parent.id.name;
    if (parent && (parent.type === 'MethodDefinition' || parent.type === 'Property') && parent.key) return parent.key.name || String(parent.key.value);
    if (n.id && n.id.name) return n.id.name;
    return null;
  };
  const calleeText = (c) => clip(text.slice(c.start, c.end));
  const visit = (node, parent, anc) => {
    if (!node || typeof node.type !== 'string') return;
    const here = anc;
    let isRoot = false, kind = 'string';
    if (node.type === 'Literal' && node.regex) { isRoot = true; kind = 'regex'; }
    else if (node.type === 'Literal' && typeof node.value === 'string') {
      isRoot = !isPlus(parent) && !(parent && parent.type === 'ExpressionStatement' && parent.directive) &&
        !(parent && (parent.type === 'ImportDeclaration' || parent.type === 'ExportAllDeclaration' || (parent.type === 'ExportNamedDeclaration' && parent.source === node)));
    } else if (node.type === 'TemplateLiteral') isRoot = !isPlus(parent) && !(parent && parent.type === 'TaggedTemplateExpression');
    else if (isPlus(node)) isRoot = !isPlus(parent);
    if (isRoot) {
      let leaves = [];
      if (kind === 'regex') {
        const pat = node.regex.pattern;
        leaves = [{ k: 't', s: node.start, e: node.end, is: node.start + 1, ie: node.start + 1 + pat.length, style: 'regex', cooked: pat }];
      } else leavesOf(node, leaves);
      if (leaves.some(l => l.k !== 'h')) {
        let member = '<file>';
        for (let i = anc.length - 1; i >= 0; i--) {
          const a = anc[i];
          if (/^(FunctionDeclaration|FunctionExpression|ArrowFunctionExpression)$/.test(a.node.type)) {
            const nm = nameOf(a.node, a.parent);
            if (nm) { member = nm; break; }
          }
        }
        const n = (ordinals.get(member) || 0) + 1;
        ordinals.set(member, n);
        const ctx = [];
        let to = null;
        let child = node;
        for (let i = anc.length - 1; i >= 0 && ctx.length < 3; i--) {
          const p = anc[i].node;
          if (p.type === 'CallExpression' || p.type === 'NewExpression') {
            const idx = p.arguments.indexOf(child);
            if (idx >= 0) ctx.push(calleeText(p.callee) + ':' + idx);
          } else if (!to && p.type === 'AssignmentExpression' && p.right === child) to = clip(text.slice(p.left.start, p.left.end));
          else if (!to && p.type === 'VariableDeclarator' && p.init === child && p.id.name) to = p.id.name;
          else if (!to && p.type === 'Property' && p.value === child && p.key) to = p.key.name || String(p.key.value);
          if (/Statement$|Declaration$/.test(p.type)) break;
          child = p;
        }
        units.push({ file, lang: 'js', member, n, id: member + ':s' + n, line: lineAt(starts, node.start), endLine: lineAt(starts, Math.max(node.start, node.end - 1)), ctx, to, kind, leaves });
      }
    }
    const next = anc.concat([{ node, parent }]);
    for (const key of Object.keys(node)) {
      if (key === 'type' || key === 'start' || key === 'end' || key === 'loc' || key === 'range') continue;
      const v = node[key];
      if (Array.isArray(v)) { for (const c of v) if (c && typeof c.type === 'string') visit(c, node, next); }
      else if (v && typeof v.type === 'string') visit(v, node, next);
    }
  };
  visit(ast, null, []);
  return units;
}

// ── JSON: a scanner with offsets ────────────────────────────────────────────
function jsonScan(file, text) {
  const starts = lineStarts(text);
  const units = [];
  const n = text.length;
  let i = text.charCodeAt(0) === 0xfeff ? 1 : 0;
  const ws = () => { while (i < n && /[ \t\r\n]/.test(text[i])) i++; };
  const str = () => {
    if (text[i] !== '"') cannot(file + ': a string was expected at offset ' + i);
    const s = i;
    let j = i + 1;
    while (j < n && text[j] !== '"') { if (text[j] === '\\') j++; j++; }
    if (j >= n) cannot(file + ': a string is never closed');
    i = j + 1;
    return { s, e: j + 1, is: s + 1, ie: j };
  };
  const decode = (raw) => JSON.parse('"' + raw + '"');
  const value = (p, key) => {
    ws();
    const c = text[i];
    if (c === '{') {
      i++; ws();
      if (text[i] === '}') { i++; return; }
      for (;;) {
        ws();
        const k = str();
        const kn = decode(text.slice(k.is, k.ie));
        ws();
        if (text[i] !== ':') cannot(file + ': ":" expected at offset ' + i);
        i++;
        value(p ? p + '.' + kn : kn, kn);
        ws();
        if (text[i] === ',') { i++; continue; }
        if (text[i] === '}') { i++; return; }
        cannot(file + ': "," or "}" expected at offset ' + i);
      }
    }
    if (c === '[') {
      i++; ws();
      if (text[i] === ']') { i++; return; }
      let idx = 0;
      for (;;) {
        value(p + '[' + idx + ']', key);
        idx++;
        ws();
        if (text[i] === ',') { i++; continue; }
        if (text[i] === ']') { i++; return; }
        cannot(file + ': "," or "]" expected at offset ' + i);
      }
    }
    if (c === '"') {
      const t = str();
      const cooked = decode(text.slice(t.is, t.ie));
      units.push({ file, lang: 'json', member: p, n: 1, id: p, line: lineAt(starts, t.s), endLine: lineAt(starts, t.e - 1), ctx: [], to: key, key,
        leaves: [{ k: 't', s: t.s, e: t.e, is: t.is, ie: t.ie, style: 'json', cooked }] });
      return;
    }
    const m = /^(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null)/.exec(text.slice(i, i + 40));
    if (!m) cannot(file + ': a value was expected at offset ' + i);
    i += m[0].length;
  };
  value('$', null);
  ws();
  if (i < n) cannot(file + ': trailing content at offset ' + i);
  return units;
}

// The code of a hole without the text of its string literals: a hole that holds a literal
// ("passo") keeps its skeleton when that literal is translated.
function holeSkeleton(src) {
  return src
    .replace(/(?:\$@?|@\$?)?"(?:[^"\\]|\\.)*"/g, '""')
    .replace(/'(?:[^'\\]|\\.|'')*'/g, "''")
    .replace(/`(?:[^`\\]|\\.)*`/g, '``')
    .replace(/\s+/g, ' ')
    .trim();
}

// ── scanning many files ─────────────────────────────────────────────────────
// entries: [{ key, lang: cs|js|ps|json, text }] -> Map(key -> { units, calls })
function scanMany(entries, opts) {
  opts = opts || {};
  const out = new Map();
  const dir = scratch();
  try {
    const per = { cs: [], ps: [] };
    entries.forEach((it, idx) => {
      const text = it.text.replace(/^﻿/, '');
      if (it.lang === 'js') { out.set(it.key, { units: jsScan(it.key, text), calls: [] }); return; }
      if (it.lang === 'json') { out.set(it.key, { units: jsonScan(it.key, text), calls: [] }); return; }
      if (!per[it.lang]) cannot('no scanner for ' + it.key + ' (' + it.lang + ')');
      const file = path.join(dir, 'f' + idx + (it.lang === 'cs' ? '.cs' : '.ps1'));
      fs.writeFileSync(file, '﻿' + text, 'utf8');
      per[it.lang].push({ key: it.key, file, text });
    });
    for (const lang of ['cs', 'ps']) {
      if (!per[lang].length) continue;
      const got = runHelper(lang, per[lang].map(x => x.file), (opts.calls || []).join(','));
      for (const x of per[lang]) {
        const o = got.get(path.resolve(x.file));
        const units = o.units.map(u => mkUnit(x.key, lang, u, lang === 'cs' ? csLeaves(u, x.text) : psLeaves(u, x.text), { kind: 'string' }));
        out.set(x.key, { units, calls: (o.calls || []).map(c => Object.assign({ file: x.key }, c)) });
      }
    }
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
  // every leaf must start where its parser said
  for (const it of entries) {
    const text = it.text.replace(/^﻿/, '');
    for (const u of out.get(it.key).units) {
      for (const l of u.leaves) {
        if (l.k === 'h') { l.sk = holeSkeleton(text.slice(l.s, l.e)); continue; }
        if (!(l.s >= 0 && l.e >= l.s && l.e <= text.length)) cannot('a leaf out of the file in ' + it.key + ' (' + u.id + ')');
        if (l.k === 't' && l.style !== 'raw' && l.style !== 'u8') {
          if (!(l.is >= l.s && l.ie <= l.e && l.ie >= l.is)) cannot('a leaf with its text outside its token in ' + it.key + ' (' + u.id + ')');
        }
        if (it.lang === 'cs' && l.k === 't' && l.style === 'regular' && text[l.s] !== '"') cannot('a C# literal does not start at its quote in ' + it.key + ' (' + u.id + ')');
        if (it.lang === 'cs' && l.k === 't' && l.style === 'verbatim' && text.slice(l.s, l.s + 2) !== '@"') cannot('a C# verbatim literal does not start at its @" in ' + it.key + ' (' + u.id + ')');
        if (it.lang === 'js' && l.k === 't' && l.style !== 'regex' && !/^['"`]/.test(text[l.s]) && l.style !== 'template' && l.style !== 'template-bad') cannot('a JS literal does not start at its quote in ' + it.key + ' (' + u.id + ')');
      }
    }
  }
  return out;
}

// The shape snapshot.js reads: constant parts joined, holes as null.
function snapshotParts(unit) {
  const parts = [];
  const holes = [];
  for (const l of unit.leaves) {
    if (l.k === 'h') {
      if (holes.length && holes[holes.length - 1]) continue;
      parts.push(null); holes.push(true);
    } else {
      if (holes.length && !holes[holes.length - 1]) { parts[parts.length - 1] += l.cooked; continue; }
      parts.push(l.cooked); holes.push(false);
    }
  }
  return parts;
}

// ── encoders: new text -> the source spelling of its style ──────────────────
// The non-ASCII characters the original spelled as escapes keep their escape.
function escapesUsed(origInner) {
  const m = new Map();
  for (const x of origInner.matchAll(/\\u([0-9a-fA-F]{4})|\\U([0-9a-fA-F]{8})|\\x([0-9a-fA-F]{1,4})/g)) {
    const cp = parseInt(x[1] || x[2] || x[3], 16);
    if (cp > 0x7e) m.set(String.fromCodePoint(cp), x[0]);
  }
  return m;
}

function hex4(c) { return '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'); }

// returns the source text for `cooked`; throws ScanError for a text the style cannot hold
function encodeInner(lang, style, cooked, origInner, opts) {
  opts = opts || {};
  const esc = escapesUsed(origInner || '');
  const eol = opts.eol || '\n';
  const nl = (s) => s.replace(/\r\n/g, '\n').replace(/\n/g, eol);
  let out = '';
  const outsideAscii = (ch) => (esc.has(ch) ? esc.get(ch) : ch);
  if (lang === 'cs') {
    if (style === 'regular' || style === 'interp-regular') {
      const interp = style === 'interp-regular';
      for (const ch of cooked) {
        if (ch === '\\') out += '\\\\';
        else if (ch === '"') out += '\\"';
        else if (ch === '\n') out += '\\n';
        else if (ch === '\r') out += '\\r';
        else if (ch === '\t') out += '\\t';
        else if (ch === '\0') out += '\\0';
        else if (ch < ' ') out += hex4(ch);
        else if (interp && ch === '{') out += '{{';
        else if (interp && ch === '}') out += '}}';
        else if (ch > '~') out += outsideAscii(ch);
        else out += ch;
      }
      return out;
    }
    if (style === 'verbatim' || style === 'interp-verbatim') {
      const interp = style === 'interp-verbatim';
      for (const ch of nl(cooked)) {
        if (ch === '"') out += '""';
        else if (interp && ch === '{') out += '{{';
        else if (interp && ch === '}') out += '}}';
        else out += ch;
      }
      return out;
    }
    cannot('a C# literal of style ' + style + ' cannot be rewritten by this tool');
  }
  if (lang === 'js') {
    if (style === 'single' || style === 'double') {
      const q = style === 'single' ? "'" : '"';
      for (const ch of cooked) {
        if (ch === '\\') out += '\\\\';
        else if (ch === q) out += '\\' + q;
        else if (ch === '\n') out += '\\n';
        else if (ch === '\r') out += '\\r';
        else if (ch === '\t') out += '\\t';
        else if (ch === ' ') out += '\\u2028';
        else if (ch === ' ') out += '\\u2029';
        else if (ch < ' ') out += hex4(ch);
        else if (ch > '~') out += outsideAscii(ch);
        else out += ch;
      }
      return out;
    }
    if (style === 'template') {
      const rawNewlines = /\n/.test(origInner || '');
      const chars = Array.from(cooked);
      for (let k = 0; k < chars.length; k++) {
        const ch = chars[k];
        if (ch === '\\') out += '\\\\';
        else if (ch === '`') out += '\\`';
        else if (ch === '$' && chars[k + 1] === '{') out += '\\$';
        else if (ch === '\n') out += rawNewlines ? eol : '\\n';
        else if (ch === '\r') out += '\\r';
        else if (ch < ' ' && ch !== '\t') out += hex4(ch);
        else if (ch > '~') out += outsideAscii(ch);
        else out += ch;
      }
      return out;
    }
    if (style === 'regex') return cooked;   // the caller checks the regular expression
    cannot('a JavaScript literal of style ' + style + ' cannot be rewritten by this tool');
  }
  if (lang === 'json') {
    for (const ch of cooked) {
      if (ch === '\\') out += '\\\\';
      else if (ch === '"') out += '\\"';
      else if (ch === '\n') out += '\\n';
      else if (ch === '\r') out += '\\r';
      else if (ch === '\t') out += '\\t';
      else if (ch < ' ') out += hex4(ch);
      else if (ch > '~') out += outsideAscii(ch);
      else out += ch;
    }
    return out;
  }
  if (lang === 'ps') {
    if (/[‘-‛“-„]/.test(cooked)) cannot('a typographic quote inside a PowerShell string is a quote to PowerShell: not written by this tool');
    const here = /HereString/.test(style);
    if (/^SingleQuoted/.test(style)) {
      if (here) return nl(cooked);
      return nl(cooked).replace(/'/g, "''");
    }
    if (/^DoubleQuoted/.test(style)) {
      const doubled = /""/.test(origInner || '') && !/`"/.test(origInner || '');
      const rawNewlines = /\n/.test(origInner || '');
      const chars = Array.from(cooked);
      for (let k = 0; k < chars.length; k++) {
        const ch = chars[k];
        if (ch === '`') out += '``';
        else if (ch === '$') out += '`$';
        else if (ch === '"') out += here ? '"' : (doubled ? '""' : '`"');
        else if (ch === '\n') out += rawNewlines ? eol : '`n';
        else if (ch === '\r') out += '`r';
        else if (ch === '\t') out += '`t';
        else if (ch === '\0') out += '`0';
        else out += ch;
      }
      return out;
    }
    cannot('a PowerShell literal of style ' + style + ' cannot be rewritten by this tool');
  }
  cannot('no encoder for ' + lang);
}

function langOfFile(file) {
  const ext = path.extname(file).toLowerCase();
  if (ext === '.cs') return 'cs';
  if (ext === '.js' || ext === '.mjs' || ext === '.cjs') return 'js';
  if (ext === '.ps1' || ext === '.psm1') return 'ps';
  if (ext === '.json') return 'json';
  return null;
}

const langName = (l) => LANG_OF[l] || l;

module.exports = { holeSkeleton, langName, scanMany, snapshotParts, encodeInner, langOfFile, lineStarts, lineAt, ScanError, hasAcorn: () => !!acorn, psDecodeDouble, psDecodeSingle };

if (require.main === module) {
  if (!acorn && !process.execArgv.includes('--expose-internals')) {
    const r = cp.spawnSync(process.execPath, ['--expose-internals', __filename].concat(process.argv.slice(2)), { stdio: 'inherit' });
    process.exit(r.status === null ? 2 : r.status);
  }
  const a = process.argv.slice(2);
  try {
    if (a[0] === '--units' && a[1]) {
      const f = path.resolve(a[1]);
      const lang = langOfFile(f);
      const text = fs.readFileSync(f).toString('utf8').replace(/^﻿/, '');
      const r = scanMany([{ key: a[1], lang, text }], { calls: ['Check'] }).get(a[1]);
      for (const u of r.units) {
        console.log(u.id + '  line ' + u.line + '  ctx ' + u.ctx.join(' < ') + (u.to ? '  to ' + u.to : '') + '\n    ' + u.leaves.map(l => l.k === 'h' ? '{' + l.src + '}' : JSON.stringify(l.cooked)).join(' + '));
      }
    } else { console.log('usage: --units <file>'); process.exitCode = 2; }
  } catch (e) { console.log('window-5-scan: ' + (e && e.message)); process.exitCode = 2; }
}
