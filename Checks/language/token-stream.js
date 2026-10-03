#!/usr/bin/env node
// =============================================================================
//  token-stream.js - the same program under other comments? (W3.1-bis.2, item 9)
// =============================================================================
//
//  WHY IT EXISTS
//
//  The translation of W3.1-bis rewrites comments (.7), messages (.8, .10) and
//  names (.4-.6) in hundreds of files. A reviewer reading a diff of comments
//  cannot see a quote that moved, a `<` that became `<=` or a string that
//  lost a letter. This tool can: it reads both versions of a file as the
//  compiler or the interpreter reads them, a stream of tokens WITHOUT
//  comments and whitespace, and compares the two streams.
//
//    C#          Roslyn, the copy that ships with MSBuild (Bin\Roslyn). A
//                small helper is compiled once with the csc.exe of that
//                folder into the temp directory and reused. Comments,
//                whitespace and documentation comments are trivia and are
//                dropped; preprocessor directives and the text of a disabled
//                region are code and stay.
//    JavaScript  acorn, the copy that ships INSIDE Node (internal module,
//                reached with --expose-internals; a local `acorn` package or
//                ACORN_PATH is preferred when there is one). Nothing is
//                installed from the network.
//    PowerShell  the parser of PowerShell itself
//                ([System.Management.Automation.Language.Parser]::ParseFile).
//                Comments are dropped (except #requires, which is code), and
//                a run of line ends counts as one: a comment on a line of its
//                own leaves a line end behind.
//
//  Literals are compared by VALUE, not by spelling: "\u00e8" and the letter
//  itself are the same string, 0x10 and 16 the same number. Line ends are
//  normalized to LF on both sides first (git hands out CRLF on Windows and
//  LF from a blob).
//
//  THREE MODES, as same-il.ps1 has for the compiled assemblies:
//
//    identical   (default) the two streams are the same token for token.
//                This is what a translation of COMMENTS must give (.7).
//    strings     only string literals changed: same number of tokens, and
//                every difference is a string literal against a string
//                literal of the same kind. An interpolated string (C# $"..",
//                a JS template, a PowerShell "..$x..") may change its text
//                but not its holes. For messages (.8, .10).
//    renames     every difference is a name or a string that the name map
//                explains: the old one becomes the new one by replacing whole
//                words (or dotted runs of words) with pairs of the map. A
//                dotted name (A.B.C) is compared as one token. For the rename
//                windows (.4-.6).
//
//  The name map is a TSV. With a header naming its columns (old/new,
//  from/to or it/en) those columns are used; otherwise the first two. Lines
//  that start with # are comments.
//
//  USAGE
//    node Controlli/lingua/token-stream.js [--rev <rev>] [--mode <m>] [--map <tsv>] [paths...]
//        each path in the working tree against the same path at <rev>
//        (default HEAD); without paths, every changed .cs/.js/.ps1 file
//        (git diff -M against <rev>, renames followed)
//    node Controlli/lingua/token-stream.js --all [--rev <rev>]
//        every tracked .cs/.js/.ps1 file, changed or not
//    node Controlli/lingua/token-stream.js --pair <before> <after> [--mode <m>] [--map <tsv>]
//    node Controlli/lingua/token-stream.js --dump <file>
//    node Controlli/lingua/token-stream.js --self-test
//        fake pairs in the temp directory, one per way of failing, in all
//        three languages: every check is seen red and seen green
//    --root <dir>     another checkout (default: the repository of this file)
//    --show <n>       differences printed per file (default 5)
//    --allow-empty    exit 0 when there is no file to compare (a caller that
//                     loops over commits, some of which touch no code)
//
//  EXIT CODES
//    0  every file compared is the same under the mode
//    1  a difference outside the mode (printed), nothing compared (without
//       --allow-empty), or --self-test not as expected
//    2  cannot run: no parser, a file that does not parse, a bad argument
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');
const crypto = require('crypto');

// ── acorn: a local package if there is one, else the copy inside Node ───────
function loadAcorn() {
  const tries = [];
  if (process.env.ACORN_PATH) tries.push(process.env.ACORN_PATH);
  tries.push('acorn', 'internal/deps/acorn/acorn/dist/acorn');
  for (const t of tries) {
    try { return require(t); } catch (e) { /* next */ }
  }
  return null;
}
let acorn = loadAcorn();
// Restarted with --expose-internals only when run as a program: required as a
// module (snapshot.js uses the Roslyn helpers), it must not restart its
// caller. A JavaScript stream asked for without a parser then says so.
if (!acorn && require.main === module && !process.execArgv.includes('--expose-internals')) {
  const r = cp.spawnSync(process.execPath, ['--expose-internals', __filename].concat(process.argv.slice(2)), { stdio: 'inherit' });
  process.exit(r.status === null ? 2 : r.status);
}

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const LANGUAGES = {
  '.cs': 'csharp', '.js': 'javascript', '.cjs': 'javascript', '.mjs': 'javascript',
  '.ps1': 'powershell', '.psm1': 'powershell', '.psd1': 'powershell',
};
const HOLE = '\u0000{}\u0000';

class GateError extends Error {}
function cannot(msg) { throw new GateError(msg); }

// ── token classes, per language ─────────────────────────────────────────────
const CLASSES = {
  csharp: {
    string: (k) => (/StringLiteral/.test(k) || /^Interp:/.test(k)) && !/^Disabled/.test(k),
    name: (k) => k === 'IdentifierToken',
    dot: 'DotToken',
  },
  javascript: {
    string: (k) => k === 'string' || k === 'interp:template',
    name: (k) => k === 'name' || k === 'privateId',
    dot: '.',
  },
  powershell: {
    string: (k) => k === 'StringLiteral' || k === 'StringExpandable' || k === 'HereStringLiteral' || k === 'HereStringExpandable',
    name: (k) => ['Identifier', 'Generic', 'Variable', 'SplattedVariable', 'Parameter', 'Label'].includes(k),
    dot: 'Dot',
  },
};

// ── reading a file as the parser does ───────────────────────────────────────
function normalizeText(text) {
  return text.replace(/^\ufeff/, '').replace(/\r\n?/g, '\n');
}

function scratchDir() {
  const d = fs.mkdtempSync(path.join(os.tmpdir(), 'token-stream-'));
  return d;
}

// JavaScript, in process.
function jsTokens(text) {
  if (!acorn) cannot('no JavaScript parser: neither a local acorn package nor the copy inside Node (node --expose-internals)');
  let last;
  for (const sourceType of ['script', 'module']) {
    try {
      const out = [];
      const opts = { ecmaVersion: 'latest', sourceType, allowHashBang: true, allowReturnOutsideFunction: true, locations: true };
      for (const t of acorn.tokenizer(text, opts)) {
        const label = t.type.label;
        let value;
        if (label === 'string' || label === 'name' || label === 'privateId') value = String(t.value);
        else if (label === 'num') value = typeof t.value + ':' + String(t.value);
        else if (label === 'regexp') value = '/' + t.value.pattern + '/' + t.value.flags;
        else if (label === 'template' || label === 'invalidTemplate') value = t.value === null || t.value === undefined ? text.slice(t.start, t.end) : String(t.value);
        else value = t.type.keyword ? t.type.keyword : (t.value === undefined ? label : String(t.value));
        out.push({ line: t.loc.start.line, kind: label, value });
      }
      return groupTemplates(out);
    } catch (e) { last = e; }
  }
  cannot('JavaScript does not tokenize: ' + last.message);
}

// A template literal becomes one token, text parts around hole marks, and
// the tokens of every hole kept apart (same shape as the C# helper gives).
function groupTemplates(toks) {
  let i = 0;
  function readTemplate() {
    const line = toks[i].line;
    i++;
    const parts = [];
    const holes = [];
    let cur = '';
    while (i < toks.length) {
      const t = toks[i];
      if (t.kind === '`') { i++; break; }
      if (t.kind === '${') { i++; parts.push(cur); cur = ''; holes.push(readHole()); continue; }
      cur += t.value;
      i++;
    }
    parts.push(cur);
    return { line, kind: 'interp:template', value: parts.join(HOLE), holes };
  }
  function readHole() {
    const seq = [];
    let depth = 0;
    while (i < toks.length) {
      const t = toks[i];
      if (t.kind === '`') { seq.push(readTemplate()); continue; }
      if (t.kind === '{') depth++;
      if (t.kind === '}') {
        if (depth === 0) { i++; return seq; }
        depth--;
      }
      seq.push(t);
      i++;
    }
    return seq;
  }
  const out = [];
  while (i < toks.length) {
    if (toks[i].kind === '`') out.push(readTemplate());
    else { out.push(toks[i]); i++; }
  }
  return out;
}

// ── C#: the Roslyn helper, compiled once into the temp directory ────────────
const CSHARP_HELPER = String.raw`// Token stream of C# files through Roslyn, without trivia (token-stream.js).
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

static class TokenStreamEntry
{
    static string roslynDir;

    // Roslyn is loaded from the MSBuild folder, not copied next to this
    // program: the resolver hands over whatever version sits there.
    static int Main(string[] args)
    {
        roslynDir = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var p = Path.Combine(roslynDir, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        return TokenStreamRoslyn.Run(args[1], args[2]);
    }
}

static class TokenStreamRoslyn
{
    public static int Run(string listFile, string outFile)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);
        var sb = new StringBuilder();
        foreach (var file in File.ReadAllLines(listFile, Encoding.UTF8))
        {
            if (file.Length == 0) continue;
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(SourceText.From(text), options);
            var list = new List<string>();
            EmitNode(tree.GetRoot(), list);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
                             .Select(d => d.ToString()).Take(5).ToList();
            sb.Append("{\"file\":").Append(Json(file)).Append(",\"errors\":[");
            sb.Append(string.Join(",", errors.Select(Json))).Append("],\"tokens\":[");
            sb.Append(string.Join(",", list)).Append("]}\n");
        }
        File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        return 0;
    }

    static void EmitNode(SyntaxNode node, List<string> list)
    {
        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.IsNode)
            {
                var n = child.AsNode();
                var interpolated = n as InterpolatedStringExpressionSyntax;
                if (interpolated != null) { EmitInterpolated(interpolated, list); continue; }
                EmitNode(n, list);
            }
            else EmitToken(child.AsToken(), list);
        }
    }

    static void EmitToken(SyntaxToken t, List<string> list)
    {
        EmitTrivia(t.LeadingTrivia, list);
        list.Add(Token(Line(t), t.Kind().ToString(), Value(t), null));
        EmitTrivia(t.TrailingTrivia, list);
    }

    // Comments, whitespace and line ends are dropped. What the preprocessor
    // reads is code, and stays: directives token by token, and the text of a
    // disabled region lexed as tokens of its own.
    static void EmitTrivia(SyntaxTriviaList trivia, List<string> list)
    {
        foreach (var tr in trivia)
        {
            if (tr.HasStructure)
            {
                var s = tr.GetStructure();
                if (s is DirectiveTriviaSyntax || s is SkippedTokensTriviaSyntax) EmitNode(s, list);
                continue;
            }
            if (tr.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                int line = tr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                foreach (var d in SyntaxFactory.ParseTokens(tr.ToFullString()))
                    if (!d.IsKind(SyntaxKind.EndOfFileToken))
                        list.Add(Token(line, "Disabled" + d.Kind(), Value(d), null));
            }
        }
    }

    // An interpolated string is one token: its text parts joined around
    // hole marks, and the tokens of every hole kept apart, so that a mode
    // that lets the text change can still ask for the same holes.
    static void EmitInterpolated(InterpolatedStringExpressionSyntax s, List<string> list)
    {
        EmitTrivia(s.StringStartToken.LeadingTrivia, list);
        var text = new StringBuilder();
        var holes = new List<string>();
        foreach (var c in s.Contents)
        {
            var part = c as InterpolatedStringTextSyntax;
            if (part != null) { text.Append(part.TextToken.ValueText); continue; }
            text.Append("\u0000{}\u0000");
            var hole = new List<string>();
            EmitNode(c, hole);
            holes.Add("[" + string.Join(",", hole) + "]");
        }
        list.Add(Token(Line(s.StringStartToken), "Interp:" + s.StringStartToken.Kind(),
                       s.StringStartToken.Text + text.ToString(), holes));
        EmitTrivia(s.StringEndToken.TrailingTrivia, list);
    }

    static int Line(SyntaxToken t) { return t.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }

    static string Value(SyntaxToken t)
    {
        switch (t.Kind())
        {
            case SyntaxKind.IdentifierToken:
            case SyntaxKind.StringLiteralToken:
            case SyntaxKind.CharacterLiteralToken:
            case SyntaxKind.InterpolatedStringTextToken:
                return t.ValueText;
            case SyntaxKind.NumericLiteralToken:
                var v = t.Value;
                if (v == null) return t.Text;
                var f = v as IFormattable;
                return v.GetType().Name + ":" + (v is double || v is float
                    ? ((IFormattable)v).ToString("R", CultureInfo.InvariantCulture)
                    : f != null ? f.ToString(null, CultureInfo.InvariantCulture) : v.ToString());
            default:
                // raw and UTF-8 string literals, whatever this Roslyn names them
                if (t.Kind().ToString().Contains("StringLiteral")) return t.ValueText;
                return t.Text;
        }
    }

    static string Token(int line, string kind, string value, List<string> holes)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(line.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Json(kind)).Append(',').Append(Json(value));
        if (holes != null) sb.Append(",[").Append(string.Join(",", holes)).Append(']');
        return sb.Append(']').ToString();
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

function findRoslyn() {
  const candidates = [];
  if (process.env.ROSLYN_DIR) candidates.push(process.env.ROSLYN_DIR);
  const pf86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
  const pf = process.env.ProgramFiles || 'C:\\Program Files';
  const vswhere = path.join(pf86, 'Microsoft Visual Studio', 'Installer', 'vswhere.exe');
  if (fs.existsSync(vswhere)) {
    try {
      const out = cp.execFileSync(vswhere, ['-latest', '-products', '*', '-requires', 'Microsoft.Component.MSBuild',
        '-find', 'MSBuild\\**\\Bin\\Roslyn\\csc.exe'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
      for (const l of out.split(/\r?\n/)) if (l.trim()) candidates.push(path.dirname(l.trim()));
    } catch (e) { /* the fixed places below */ }
  }
  for (const base of [pf86, pf]) {
    for (const ed of ['BuildTools', 'Community', 'Professional', 'Enterprise']) {
      candidates.push(path.join(base, 'Microsoft Visual Studio', '2022', ed, 'MSBuild', 'Current', 'Bin', 'Roslyn'));
    }
  }
  for (const c of candidates) {
    if (fs.existsSync(path.join(c, 'csc.exe')) && fs.existsSync(path.join(c, 'Microsoft.CodeAnalysis.CSharp.dll'))) return c;
  }
  return null;
}

function findFacades() {
  const pf86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
  for (const v of ['v4.8.1', 'v4.8', 'v4.7.2']) {
    const d = path.join(pf86, 'Reference Assemblies', 'Microsoft', 'Framework', '.NETFramework', v, 'Facades');
    if (fs.existsSync(path.join(d, 'netstandard.dll'))) return d;
  }
  const windir = process.env.WINDIR || 'C:\\Windows';
  const fw = path.join(windir, 'Microsoft.NET', 'Framework64', 'v4.0.30319');
  if (fs.existsSync(path.join(fw, 'netstandard.dll'))) return fw;
  return null;
}

// Compiles a C# program with the csc.exe of the Roslyn folder, against Roslyn
// itself, once: the executable is kept in the temp directory under a hash of
// its source and of that Roslyn, and reused. Returns { exe, roslyn }; the
// program is started with the Roslyn folder as its first argument, so that it
// can resolve the Roslyn assemblies from there. extraRefs are more assemblies
// to reference (a name alone is looked up by csc in the framework folder).
// Also used by snapshot.js.
function compileRoslynProgram(source, name, extraRefs) {
  const roslyn = findRoslyn();
  if (!roslyn) cannot('no Roslyn: MSBuild\\Current\\Bin\\Roslyn not found (set ROSLYN_DIR)');
  const stamp = fs.statSync(path.join(roslyn, 'Microsoft.CodeAnalysis.CSharp.dll'));
  const extra = extraRefs || [];
  const hash = crypto.createHash('sha256').update(source).update(roslyn).update(String(stamp.size) + ':' + stamp.mtimeMs)
    .update(extra.join('|')).digest('hex').slice(0, 16);
  const dir = path.join(os.tmpdir(), 'token-stream-roslyn', hash);
  const exe = path.join(dir, name + '.exe');
  if (!fs.existsSync(exe)) {
    const facades = findFacades();
    if (!facades) cannot('no netstandard.dll facade to compile the Roslyn helper against');
    fs.mkdirSync(dir, { recursive: true });
    const src = path.join(dir, name + '.cs');
    fs.writeFileSync(src, source, 'utf8');
    const refs = [path.join(roslyn, 'Microsoft.CodeAnalysis.dll'), path.join(roslyn, 'Microsoft.CodeAnalysis.CSharp.dll'),
      path.join(roslyn, 'System.Collections.Immutable.dll'), path.join(facades, 'netstandard.dll')];
    if (fs.existsSync(path.join(facades, 'System.Runtime.dll'))) refs.push(path.join(facades, 'System.Runtime.dll'));
    const tmpExe = exe + '.' + process.pid + '.tmp';
    const r = cp.spawnSync(path.join(roslyn, 'csc.exe'), ['-nologo', '-optimize+', '-target:exe', '-out:' + tmpExe]
      .concat(refs.concat(extra).map(x => '-r:' + x)).concat([src]), { encoding: 'utf8' });
    if (r.status !== 0) cannot('the helper ' + name + ' does not compile:\n' + (r.stdout || '') + (r.stderr || ''));
    try { fs.renameSync(tmpExe, exe); } catch (e) { if (!fs.existsSync(exe)) throw e; }
  }
  return { exe, roslyn };
}

let roslynHelper = null;
function csharpHelper() {
  if (!roslynHelper) roslynHelper = compileRoslynProgram(CSHARP_HELPER, 'TokenStreamRoslyn');
  return roslynHelper;
}

// ── PowerShell: its own parser, in one process for all the files ────────────
const POWERSHELL_HELPER = String.raw`param([string]$ListFile, [string]$OutFile)
# Token stream of PowerShell files through the PowerShell parser (token-stream.js).
# One loop and no function call per token: a call costs a tenth of a
# millisecond in Windows PowerShell, and a file has thousands of tokens.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web
$enc = [System.Web.HttpUtility]
$inv = [Globalization.CultureInfo]::InvariantCulture
function Tokens($list) {
    $out = New-Object System.Collections.Generic.List[string]
    $newLine = $true
    foreach ($t in $list) {
        $k = [string]$t.Kind
        # comments are dropped, except #requires, which PowerShell reads
        if ($k -eq 'Comment' -and -not $t.Text.StartsWith('#requires', [StringComparison]::OrdinalIgnoreCase)) { continue }
        # a run of line ends counts as one, and none at the start
        if ($k -eq 'NewLine') { if ($newLine) { continue }; $newLine = $true } else { $newLine = $false }
        $holes = $null
        if ($t -is [System.Management.Automation.Language.StringExpandableToken]) {
            $v = $t.Value
            $holes = '[[' + ((Tokens $t.NestedTokens) -join ',') + ']]'
        } elseif ($t -is [System.Management.Automation.Language.StringToken]) {
            $v = $t.Value
        } elseif ($t -is [System.Management.Automation.Language.NumberToken]) {
            $v = $t.Value.GetType().Name + ':' + [Convert]::ToString($t.Value, $inv)
        } elseif ($t -is [System.Management.Automation.Language.VariableToken]) {
            $v = $t.VariablePath.UserPath
        } elseif ($k -eq 'NewLine') {
            $v = 'newline'
        } else {
            $v = $t.Text
        }
        $s = '[' + $t.Extent.StartLineNumber + ',' + $enc::JavaScriptStringEncode($k, $true) + ',' + $enc::JavaScriptStringEncode($v, $true)
        if ($holes) { $s += ',' + $holes }
        $out.Add($s + ']')
    }
    return ,$out
}
$sb = New-Object System.Text.StringBuilder
foreach ($f in [IO.File]::ReadAllLines($ListFile, [Text.Encoding]::UTF8)) {
    if (-not $f) { continue }
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$tokens, [ref]$errors)
    $list = Tokens $tokens
    $errs = @($errors | Select-Object -First 5 | ForEach-Object { $enc::JavaScriptStringEncode($_.Extent.StartLineNumber.ToString() + ': ' + $_.Message, $true) })
    [void]$sb.Append('{"file":').Append($enc::JavaScriptStringEncode($f, $true)).Append(',"errors":[').Append(($errs -join ',')).Append('],"tokens":[').Append(($list -join ',')).Append(']}').Append([char]10)
}
[IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
`;

function powershellExe() {
  return process.platform === 'win32' ? 'powershell.exe' : 'pwsh';
}

// Runs a helper over a list of files and returns Map(file -> tokens).
function runHelper(lang, files) {
  const result = new Map();
  if (files.length === 0) return result;
  const dir = scratchDir();
  try {
    const list = path.join(dir, 'files.txt');
    const out = path.join(dir, 'tokens.jsonl');
    fs.writeFileSync(list, files.join('\n') + '\n', 'utf8');
    let r;
    if (lang === 'csharp') {
      const h = csharpHelper();
      r = cp.spawnSync(h.exe, [h.roslyn, list, out], { encoding: 'utf8', maxBuffer: 64 << 20 });
    } else {
      const script = path.join(dir, 'tokens.ps1');
      fs.writeFileSync(script, '\ufeff' + POWERSHELL_HELPER, 'utf8');
      r = cp.spawnSync(powershellExe(), ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script,
        '-ListFile', list, '-OutFile', out], { encoding: 'utf8', maxBuffer: 64 << 20 });
    }
    if (r.error) cannot('the ' + lang + ' tokenizer does not start: ' + r.error.message);
    if (r.status !== 0 || !fs.existsSync(out)) cannot('the ' + lang + ' tokenizer failed (exit ' + r.status + '):\n' + (r.stdout || '') + (r.stderr || ''));
    for (const line of fs.readFileSync(out, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      const o = JSON.parse(line);
      if (o.errors.length) cannot(o.file + ' does not parse as ' + lang + ':\n    ' + o.errors.join('\n    '));
      result.set(path.resolve(o.file), o.tokens.map(fromArray));
    }
    for (const f of files) if (!result.has(path.resolve(f))) cannot('the ' + lang + ' tokenizer gave nothing for ' + f);
    return result;
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

function fromArray(a) {
  const t = { line: a[0], kind: a[1], value: a[2] };
  if (a.length > 3) t.holes = a[3].map(h => h.map(fromArray));
  return t;
}

// Tokenizes a set of { key, lang, text } and returns Map(key -> tokens).
function tokenizeAll(items) {
  const out = new Map();
  const dir = scratchDir();
  try {
    const perLang = { csharp: [], powershell: [] };
    items.forEach((it, i) => {
      const text = normalizeText(it.text);
      if (it.lang === 'javascript') { out.set(it.key, jsTokens(text)); return; }
      const ext = it.lang === 'csharp' ? '.cs' : '.ps1';
      const file = path.join(dir, 'f' + i + ext);
      // UTF-8 with a BOM: Windows PowerShell reads a file without one in the
      // ANSI code page, and an accented letter would change on the way
      fs.writeFileSync(file, '\ufeff' + text, 'utf8');
      perLang[it.lang].push({ key: it.key, file });
    });
    for (const lang of Object.keys(perLang)) {
      if (perLang[lang].length === 0) continue;
      const got = runHelper(lang, perLang[lang].map(x => x.file));
      for (const x of perLang[lang]) out.set(x.key, got.get(path.resolve(x.file)));
    }
    return out;
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

// ── the name map, and what it explains ──────────────────────────────────────
function words(s) { return s.match(/[A-Za-z0-9_]+|[^A-Za-z0-9_]/g) || []; }

function loadMap(file) {
  const pairs = new Map();
  if (!file) return pairs;
  if (!fs.existsSync(file)) cannot('no map at ' + file);
  const rows = normalizeText(fs.readFileSync(file, 'utf8')).split('\n').filter(l => l.trim() && !l.startsWith('#')).map(l => l.split('\t'));
  if (rows.length === 0) cannot('the map ' + file + ' is empty');
  let from = 0, to = 1;
  const head = rows[0].map(c => c.trim().toLowerCase());
  for (const [a, b] of [['old', 'new'], ['from', 'to'], ['it', 'en'], ['italian', 'english']]) {
    if (head.includes(a) && head.includes(b)) { from = head.indexOf(a); to = head.indexOf(b); rows.shift(); break; }
  }
  for (const r of rows) {
    const a = (r[from] || '').trim(), b = (r[to] || '').trim();
    if (!a || !b || a === b) continue;
    const ow = words(a);
    if (!pairs.has(ow[0])) pairs.set(ow[0], []);
    pairs.get(ow[0]).push({ old: ow, new: words(b) });
  }
  return pairs;
}

// True when b is a, with whole words (or runs of words) replaced by pairs of
// the map; a word may also stay as it is. A search over positions, so that a
// word with two translations (two rows) is tried both ways.
function explained(a, b, pairs) {
  const A = words(a), B = words(b);
  const at = (X, i, w) => w.every((x, k) => X[i + k] === x);
  const seen = new Set();
  const stack = [[0, 0]];
  while (stack.length) {
    const [i, j] = stack.pop();
    if (i === A.length && j === B.length) return true;
    const key = i + ':' + j;
    if (seen.has(key)) continue;
    seen.add(key);
    if (i < A.length && j < B.length && A[i] === B[j]) stack.push([i + 1, j + 1]);
    if (i < A.length) {
      for (const p of pairs.get(A[i]) || []) {
        if (at(A, i, p.old) && at(B, j, p.new)) stack.push([i + p.old.length, j + p.new.length]);
      }
    }
  }
  return false;
}

// In renames mode a dotted name is one token: a namespace of two words may
// become one of four, and the map has one row for it.
function mergeDotted(tokens, cls) {
  const out = [];
  for (let i = 0; i < tokens.length; i++) {
    const t = tokens[i];
    if (cls.name(t.kind) && !t.holes) {
      let value = t.value;
      let j = i;
      while (j + 2 < tokens.length && tokens[j + 1].kind === cls.dot && cls.name(tokens[j + 2].kind)) {
        value += '.' + tokens[j + 2].value;
        j += 2;
      }
      if (j > i) { out.push({ line: t.line, kind: 'QualifiedName', value }); i = j; continue; }
    }
    out.push(t.holes ? Object.assign({}, t, { holes: t.holes.map(h => mergeDotted(h, cls)) }) : t);
  }
  return out;
}

// ── comparison ──────────────────────────────────────────────────────────────
function keyOf(t) {
  return t.kind + '\u0001' + t.value + (t.holes ? '\u0001' + t.holes.map(h => h.map(keyOf).join('\u0002')).join('\u0003') : '');
}

// Returns { diffs: [{ a, b, why }], changed: n } where changed counts the
// differences the mode explains.
function compareStreams(a, b, mode, lang, pairs) {
  const cls = CLASSES[lang];
  if (mode === 'renames') { a = mergeDotted(a, cls); b = mergeDotted(b, cls); }
  const res = { diffs: [], changed: 0 };
  if (a.length !== b.length) {
    let p = 0;
    while (p < a.length && p < b.length && keyOf(a[p]) === keyOf(b[p])) p++;
    let s = 0;
    while (s < a.length - p && s < b.length - p && keyOf(a[a.length - 1 - s]) === keyOf(b[b.length - 1 - s])) s++;
    res.diffs.push({ a: a[p] || null, b: b[p] || null,
      why: (a.length < b.length ? (b.length - a.length) + ' tokens more' : (a.length - b.length) + ' tokens fewer') +
        ' (' + (a.length - p - s) + ' before against ' + (b.length - p - s) + ' after, from here)' });
    return res;
  }
  for (let i = 0; i < a.length; i++) {
    const x = a[i], y = b[i];
    if (keyOf(x) === keyOf(y)) continue;
    const why = explain(x, y, mode, cls, lang, pairs);
    if (why === true) res.changed++;
    else res.diffs.push({ a: x, b: y, why });
  }
  return res;
}

// true when the mode allows this difference, else the reason it does not.
function explain(x, y, mode, cls, lang, pairs) {
  if (mode === 'identical') return 'differs';
  if (x.kind !== y.kind) return 'a ' + x.kind + ' became a ' + y.kind;
  const isString = cls.string(x.kind);
  const isName = cls.name(x.kind) || x.kind === 'QualifiedName';
  if (mode === 'strings' && !isString) return 'not a string literal';
  if (mode === 'renames' && !isString && !isName) return 'neither a name nor a string';
  if (mode === 'renames' && x.value !== y.value && !explained(x.value, y.value, pairs)) return 'the map does not explain it';
  if (x.holes || y.holes) {
    const hx = x.holes || [], hy = y.holes || [];
    if (hx.length !== hy.length) return 'the holes of the string changed: ' + hx.length + ' before, ' + hy.length + ' after';
    for (let k = 0; k < hx.length; k++) {
      const inner = compareStreams(hx[k], hy[k], mode, lang, pairs);
      if (inner.diffs.length) return 'hole ' + (k + 1) + ' of the string changed (' + inner.diffs[0].why + ')';
    }
  }
  return true;
}

function show(t) {
  if (!t) return '(nothing)';
  let v = t.value;
  if (v.length > 70) v = v.slice(0, 67) + '...';
  return 'line ' + t.line + ' ' + t.kind + ' ' + JSON.stringify(v) + (t.holes ? ' with ' + t.holes.length + ' holes' : '');
}

// ── git ─────────────────────────────────────────────────────────────────────
function git(root, args) {
  try {
    return cp.execFileSync('git', ['-C', root].concat(args), { maxBuffer: 256 << 20, stdio: ['ignore', 'pipe', 'pipe'] });
  } catch (e) {
    cannot('git ' + args.join(' ') + ' failed in ' + root + ': ' + String(e.stderr || e.message).trim());
  }
}

function langOf(file) { return LANGUAGES[path.extname(file).toLowerCase()] || null; }

// Many blobs through one `git cat-file --batch`: one git process per file
// costs a tenth of a second each on Windows. Returns Map(spec -> text|null).
function readBlobs(root, specs) {
  const out = new Map();
  if (specs.length === 0) return out;
  const r = cp.spawnSync('git', ['-C', root, 'cat-file', '--batch'], { input: specs.join('\n') + '\n', maxBuffer: 1 << 30 });
  if (r.status !== 0) cannot('git cat-file failed: ' + (r.stderr || '').toString());
  const buf = r.stdout;
  let pos = 0;
  for (const spec of specs) {
    const nl = buf.indexOf(10, pos);
    const header = buf.slice(pos, nl).toString('utf8');
    pos = nl + 1;
    if (/ missing$/.test(header)) { out.set(spec, null); continue; }
    const m = /^[0-9a-f]+ (\w+) (\d+)$/.exec(header);
    if (!m) cannot('git cat-file answered "' + header + '" for ' + spec);
    const size = parseInt(m[2], 10);
    out.set(spec, m[1] === 'blob' ? buf.slice(pos, pos + size).toString('utf8') : null);
    pos += size + 1;
  }
  return out;
}

// The pairs to compare: [{ label, lang, before, after }] with texts.
function pairsFromGit(root, rev, paths, all) {
  const out = [];
  const renames = new Map();
  let list;
  if (all) {
    list = git(root, ['ls-files', '-z']).toString('utf8').split('\0').filter(Boolean);
  } else if (paths.length) {
    list = paths.map(p => path.relative(root, path.resolve(p)).replace(/\\/g, '/'));
  } else {
    list = [];
  }
  const status = git(root, ['diff', '--name-status', '-M', '-z', rev, '--']).toString('utf8').split('\0');
  const changed = [];
  for (let i = 0; i < status.length;) {
    const s = status[i];
    if (!s) { i++; continue; }
    if (s[0] === 'R' || s[0] === 'C') { renames.set(status[i + 2], status[i + 1]); changed.push(status[i + 2]); i += 3; }
    else { if (s[0] !== 'D') changed.push(status[i + 1]); i += 2; }
  }
  if (!all && paths.length === 0) list = changed;
  list = list.filter(rel => langOf(rel));
  const blobs = readBlobs(root, list.map(rel => rev + ':' + (renames.get(rel) || rel)));
  for (const rel of list) {
    const lang = langOf(rel);
    const abs = path.join(root, rel);
    if (!fs.existsSync(abs)) { out.push({ label: rel, lang, missing: 'not in the working tree' }); continue; }
    const old = renames.get(rel) || rel;
    const before = blobs.get(rev + ':' + old);
    if (before === null || before === undefined) { out.push({ label: rel, lang, missing: 'new since ' + rev }); continue; }
    out.push({ label: rel + (old !== rel ? ' (was ' + old + ')' : ''), lang, before, after: fs.readFileSync(abs, 'utf8') });
  }
  return out;
}

function comparePairs(pairs, mode, mapPairs, showN) {
  const items = [];
  pairs.forEach((p, i) => {
    if (p.missing) return;
    items.push({ key: 'b' + i, lang: p.lang, text: p.before });
    items.push({ key: 'a' + i, lang: p.lang, text: p.after });
  });
  const toks = tokenizeAll(items);
  let same = 0, different = 0, skipped = 0, changedTotal = 0;
  pairs.forEach((p, i) => {
    if (p.missing) { skipped++; console.log('  ' + p.label + ': skipped, ' + p.missing); return; }
    const r = compareStreams(toks.get('b' + i), toks.get('a' + i), mode, p.lang, mapPairs);
    changedTotal += r.changed;
    if (r.diffs.length === 0) {
      same++;
      if (r.changed) console.log('  ' + p.label + ': ' + r.changed + (mode === 'strings' ? ' string literals changed' : ' names renamed') + ', nothing else');
      return;
    }
    different++;
    console.log('  ' + p.label + ': ' + r.diffs.length + ' differences outside the mode' + (r.changed ? ' (and ' + r.changed + ' inside it)' : ''));
    for (const d of r.diffs.slice(0, showN)) {
      console.log('      before ' + show(d.a));
      console.log('      after  ' + show(d.b));
      console.log('      ' + d.why);
    }
    if (r.diffs.length > showN) console.log('      ... ' + (r.diffs.length - showN) + ' more');
  });
  return { same, different, skipped, changed: changedTotal };
}

// ── the self-test: every way of failing, seen red, and the good cases green ─
function selfTest() {
  // The fixtures are English on purpose: the tool does not care which
  // language a comment is in, and this file is judged by the language gate.
  // "bow" has two rows, as a word with two senses has in the real map.
  const map = [
    'old\tnew',
    'Tally\tCount',
    'Acme.Legacy\tAcme.Modern.Core',
    'origin\troot',
    'label\tname',
    'bow\tbend',
    'bow\tprow',
  ].join('\n');
  const dir = scratchDir();
  const mapFile = path.join(dir, 'map.tsv');
  fs.writeFileSync(mapFile, map + '\n', 'utf8');
  const pairs = loadMap(mapFile);
  const NUL = String.fromCharCode(0);
  const cs = [
    '// a comment that will be rewritten',
    'using System;',
    'namespace Acme.Legacy',
    '{',
    '    /// <summary>documentation</summary>',
    '    class C',
    '    {',
    '        /* block */ int Tally(int origin) { return origin < 3 ? 1 : 0; }',
    '        string s = "10\\u00b0C";',
    '        string m = $"found {Tally(2),4:D2} of {s}";',
    '        string p = "bow";',
    '    }',
    '}',
  ].join('\n');
  const js = [
    '#!/usr/bin/env node',
    '// a comment',
    "'use strict';",
    "const origin = require('path').join(__dirname, '..');",
    "const sep = '" + NUL + "';",
    'const re = /ab+c/g;',
    'function Tally(n) { return n < 3 ? `n is ${n} of ${origin}` : "big"; }',
  ].join('\n');
  const ps = [
    '# a comment',
    '<# a block',
    '   comment #>',
    'param([string]$origin)',
    '$label = "text $origin here"',
    "Write-Host 'hello' -ForegroundColor Red",
    'if ($label -lt 3) { exit 1 }',
  ].join('\n');
  const edit = (text, from, to) => {
    if (text.split(from).length !== 2) throw new Error('self-test edit does not apply once: ' + from);
    return text.split(from).join(to);
  };
  const cases = [
    // C#
    ['C#: comments rewritten, lines added', 'csharp', cs, edit(edit(edit(cs, '// a comment that will be rewritten', '// one comment\n// that grew to two lines'), '/// <summary>documentation</summary>', '/// <summary>\n    /// the documentation, longer\n    /// </summary>'), '/* block */', '/* a longer\n   block */'), 'identical', 'pass'],
    ['C#: the same character, escaped or not', 'csharp', cs, edit(cs, '"10\\u00b0C"', '"10\u00b0C"'), 'identical', 'pass'],
    ['C#: a string literal changed', 'csharp', cs, edit(cs, '"10\\u00b0C"', '"50\u00b0F"'), 'identical', 'fail'],
    ['C#: a string literal changed, strings mode', 'csharp', cs, edit(cs, '"10\\u00b0C"', '"50\u00b0F"'), 'strings', 'pass'],
    ['C#: interpolated text changed, same holes', 'csharp', cs, edit(cs, '$"found {Tally(2),4:D2} of {s}"', '$"counted {Tally(2),4:D2} out of {s}"'), 'strings', 'pass'],
    ['C#: a hole of an interpolated string changed', 'csharp', cs, edit(cs, '{Tally(2),4:D2}', '{Tally(3),4:D2}'), 'strings', 'fail'],
    ['C#: an operator changed', 'csharp', cs, edit(cs, 'origin < 3', 'origin <= 3'), 'strings', 'fail'],
    ['C#: a number changed', 'csharp', cs, edit(cs, 'origin < 3', 'origin < 4'), 'identical', 'fail'],
    ['C#: renamed by the map', 'csharp', cs, edit(edit(edit(cs, 'int Tally(int origin) { return origin', 'int Count(int root) { return root'), '{Tally(2)', '{Count(2)'), 'namespace Acme.Legacy', 'namespace Acme.Modern.Core'), 'renames', 'pass'],
    ['C#: renamed by the map, identical mode', 'csharp', cs, edit(cs, 'namespace Acme.Legacy', 'namespace Acme.Modern.Core'), 'identical', 'fail'],
    ['C#: a rename the map does not have', 'csharp', cs, edit(cs, 'class C', 'class D'), 'renames', 'fail'],
    ['C#: a word with two rows, either one', 'csharp', cs, edit(cs, '"bow"', '"prow"'), 'renames', 'pass'],
    // JavaScript
    ['JS: comments rewritten', 'javascript', js, edit(js, '// a comment', '// one comment\n// on two lines'), 'identical', 'pass'],
    ['JS: a raw NUL and its escape are the same string', 'javascript', js, edit(js, "'" + NUL + "'", "'\\u0000'"), 'identical', 'pass'],
    ['JS: a string changed', 'javascript', js, edit(js, '"big"', '"large"'), 'identical', 'fail'],
    ['JS: a string changed, strings mode', 'javascript', js, edit(js, '"big"', '"large"'), 'strings', 'pass'],
    ['JS: template text changed, same holes', 'javascript', js, edit(js, '`n is ${n} of ${origin}`', '`n equals ${n} out of ${origin}`'), 'strings', 'pass'],
    ['JS: a template hole changed', 'javascript', js, edit(js, '${n} of', '${n + 1} of'), 'strings', 'fail'],
    ['JS: a regular expression changed', 'javascript', js, edit(js, '/ab+c/g', '/ab*c/g'), 'strings', 'fail'],
    ['JS: renamed by the map', 'javascript', js, edit(edit(edit(js, 'const origin =', 'const root ='), '${origin}', '${root}'), 'function Tally(', 'function Count('), 'renames', 'pass'],
    ['JS: a rename the map does not have', 'javascript', js, edit(js, 'const sep', 'const separator'), 'renames', 'fail'],
    // PowerShell
    ['PS: comments rewritten, a comment line added', 'powershell', ps, edit(edit(ps, '# a comment', '# one comment\n# and another'), '<# a block\n   comment #>', '<# a block comment\n   on three\n   lines #>'), 'identical', 'pass'],
    ['PS: a string changed', 'powershell', ps, edit(ps, "'hello'", "'good morning'"), 'identical', 'fail'],
    ['PS: a string changed, strings mode', 'powershell', ps, edit(ps, "'hello'", "'good morning'"), 'strings', 'pass'],
    ['PS: expandable text changed, same variable', 'powershell', ps, edit(ps, '"text $origin here"', '"the text $origin, here"'), 'strings', 'pass'],
    ['PS: the variable inside a string changed', 'powershell', ps, edit(ps, '"text $origin here"', '"text $label here"'), 'strings', 'fail'],
    ['PS: a parameter changed', 'powershell', ps, edit(ps, '-ForegroundColor Red', '-BackgroundColor Red'), 'strings', 'fail'],
    ['PS: an operator changed', 'powershell', ps, edit(ps, '-lt 3', '-le 3'), 'strings', 'fail'],
    ['PS: renamed by the map', 'powershell', ps, edit(edit(ps, 'param([string]$origin)', 'param([string]$root)'), '"text $origin here"', '"text $root here"'), 'renames', 'pass'],
    ['PS: a rename the map does not have', 'powershell', ps, edit(ps, 'Write-Host', 'Write-Output'), 'renames', 'fail'],
  ];
  const items = [];
  cases.forEach((c, i) => {
    items.push({ key: 'b' + i, lang: c[1], text: c[2] });
    items.push({ key: 'a' + i, lang: c[1], text: c[3] });
  });
  let toks;
  try { toks = tokenizeAll(items); } finally { fs.rmSync(dir, { recursive: true, force: true }); }
  let ok = 0;
  const bad = [];
  cases.forEach((c, i) => {
    const r = compareStreams(toks.get('b' + i), toks.get('a' + i), c[4], c[1], pairs);
    const got = r.diffs.length === 0 ? 'pass' : 'fail';
    const good = got === c[5];
    if (good) ok++; else bad.push(c[0]);
    console.log((good ? '  ok  ' : '  XX  ') + c[0] + ' [' + c[4] + ']: expected ' + c[5] + ', got ' + got +
      (r.diffs.length ? ' (' + r.diffs[0].why + ')' : r.changed ? ' (' + r.changed + ' explained)' : ''));
  });
  // nothing compared is not a pass
  const empty = { same: 0, different: 0 };
  const emptyOk = verdict(empty, false) === 1 && verdict(empty, true) === 0;
  if (emptyOk) ok++; else bad.push('nothing compared');
  console.log((emptyOk ? '  ok  ' : '  XX  ') + 'nothing compared: exit 1, or 0 with --allow-empty');
  // every mode must have been seen failing in every language
  const seenRed = new Set(cases.filter(c => c[5] === 'fail').map(c => c[1] + ':' + c[4]));
  const missing = [];
  for (const l of ['csharp', 'javascript', 'powershell']) {
    for (const m of ['identical', 'strings', 'renames']) if (!seenRed.has(l + ':' + m)) missing.push(l + ':' + m);
  }
  console.log('');
  if (missing.length) console.log('SELF-TEST INCOMPLETE: no failing case for ' + missing.join(', '));
  console.log(ok + ' of ' + (cases.length + 1) + ' cases as expected.');
  return bad.length === 0 && missing.length === 0 ? 0 : 1;
}

// ── main ────────────────────────────────────────────────────────────────────
function parseArgs(argv) {
  const a = { rev: 'HEAD', mode: 'identical', map: null, paths: [], all: false, pair: null, dump: null, selfTest: false, root: ROOT_DEFAULT, show: 5, allowEmpty: false };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    const next = () => { if (i + 1 >= argv.length) cannot(x + ' needs a value'); return argv[++i]; };
    if (x === '--rev') a.rev = next();
    else if (x === '--mode') a.mode = next();
    else if (x === '--map') a.map = next();
    else if (x === '--all') a.all = true;
    else if (x === '--pair') { a.pair = [next(), next()]; }
    else if (x === '--dump') a.dump = next();
    else if (x === '--self-test') a.selfTest = true;
    else if (x === '--root') a.root = path.resolve(next());
    else if (x === '--show') a.show = parseInt(next(), 10);
    else if (x === '--allow-empty') a.allowEmpty = true;
    else if (x.startsWith('--')) cannot('unknown option ' + x);
    else a.paths.push(x);
  }
  if (!['identical', 'strings', 'renames'].includes(a.mode)) cannot('--mode is identical, strings or renames');
  if (a.mode === 'renames' && !a.map && !a.selfTest) cannot('--mode renames needs --map <tsv>');
  return a;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.selfTest) return selfTest();
  if (args.dump) {
    const lang = langOf(args.dump);
    if (!lang) cannot('no tokenizer for ' + args.dump);
    const toks = tokenizeAll([{ key: 'x', lang, text: fs.readFileSync(args.dump, 'utf8') }]).get('x');
    const print = (list, indent) => {
      for (const t of list) {
        console.log(indent + t.line + '\t' + t.kind + '\t' + JSON.stringify(t.value));
        if (t.holes) t.holes.forEach((h, k) => { console.log(indent + '\thole ' + (k + 1) + ':'); print(h, indent + '\t\t'); });
      }
    };
    print(toks, '');
    return 0;
  }
  const mapPairs = loadMap(args.map);
  let pairs;
  if (args.pair) {
    const lang = langOf(args.pair[0]);
    if (!lang) cannot('no tokenizer for ' + args.pair[0]);
    pairs = [{ label: args.pair[0] + ' -> ' + args.pair[1], lang, before: fs.readFileSync(args.pair[0], 'utf8'), after: fs.readFileSync(args.pair[1], 'utf8') }];
  } else {
    pairs = pairsFromGit(args.root, args.rev, args.paths, args.all);
  }
  console.log('token stream, mode ' + args.mode + (args.pair ? '' : ', against ' + args.rev) + ':');
  if (pairs.length === 0) console.log('  no .cs, .js or .ps1 file to compare');
  const r = comparePairs(pairs, args.mode, mapPairs, args.show);
  console.log('');
  console.log('token stream: ' + (r.same + r.different) + ' files compared, ' + r.same + ' same under the mode, ' +
    r.different + ' with differences outside it' + (r.skipped ? ', ' + r.skipped + ' skipped' : '') +
    (r.changed ? '; ' + r.changed + ' differences explained by the mode' : '') + '.');
  const code = verdict(r, args.allowEmpty);
  if (r.same + r.different === 0 && code !== 0) {
    console.log('token stream: NOTHING WAS COMPARED. Name the revision before the change (--rev HEAD~1) or the paths.');
  }
  return code;
}

// Comparing nothing is not "the same": after a commit, --rev HEAD has no
// difference left to look at. --allow-empty is for a caller that loops over
// commits, some of which touch no code.
function verdict(r, allowEmpty) {
  if (r.same + r.different === 0) return allowEmpty ? 0 : 1;
  return r.different === 0 ? 0 : 1;
}

if (require.main === module) {
  try {
    process.exitCode = main();
  } catch (e) {
    if (e instanceof GateError) { console.log('token-stream: ' + e.message); process.exitCode = 2; }
    else { console.log('token-stream: ' + (e && e.stack ? e.stack : e)); process.exitCode = 2; }
  }
}

module.exports = { loadMap, explained, compareStreams, tokenizeAll, words, compileRoslynProgram, GateError };
