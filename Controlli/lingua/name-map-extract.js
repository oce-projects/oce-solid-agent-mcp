#!/usr/bin/env node
// =============================================================================
//  name-map-extract.js - the name map of the translation (entry W3.1-bis.3)
// =============================================================================
//
//  WHY IT EXISTS
//
//  The rename windows of W3.1-bis (.4 the bench cases, .5 the MCP surface,
//  .6 namespaces, folders and identifiers) are GENERATED from one map: the
//  Roslyn renames, the second pass on the literals, the case keys, the
//  aliases with an expiry and the rename table of the CHANGELOG all read
//  Controlli/lingua/name-map.tsv. Two maps would diverge, so the map is
//  extracted from the tree by this script, never written by hand; only the
//  English column is.
//
//  Italian is decided by the language gate itself: the lexicon, the
//  identifier rule (isItalianName) and the exceptions of detect-italian.js,
//  so that every name the gate counts has a row. For a name of two words
//  or more the gate's prose rule is asked too, on the name read as words: a
//  phrase glued with underscores or capitals is still a phrase
//  (schema_di_quote, daSelezionare), even where the identifier rule stops at
//  a short function word. A name covered by an exception (a name generated
//  by an Italian SOLIDWORKS, the engraved text of a case) gets no row: it is
//  never translated.
//
//  A name the gate does not see as Italian (quote, the family of the
//  dimensions, is an English word too) may be given a row BY HAND, with a
//  note that says so: the script keeps that row and refreshes its count and
//  where, and the check applies to it like to any other.
//
//  THE COLUMNS (tab separated, one header row, lines starting with # are
//  comments):
//
//    type    one of the types below
//    it      the Italian name, exactly as the tree writes it
//    en      the English name (filled by hand, never by this script)
//    count   how many times the extractor met the name in that role (below)
//    where   up to three places, file:line (or the path, for paths)
//    note    context; written by the script when it creates the row, then
//            left alone (a note says, for instance, that two rows share an
//            English name on purpose: "merged")
//
//  One row per Italian name per TYPE: the same word in two roles is two rows,
//  and its two English names are decided apart (a word with two senses has
//  two rows; different enumerations stay different).
//
//  THE TYPES, and what count counts:
//
//    tool              the MCP tools (the tool list of Strumenti/, found by
//                      the English tool api_search it also holds)
//    sub_action        the values of the action argument of a tool
//    operation         the operations of the catalogue
//    catalogue_family  the families of the catalogue (12, one of which,
//                      quote, the gate reads as English: a row by hand)
//    case_family       the values of the family key of the cases (their
//                      own vocabulary, composite values included)
//    argument          argument names: catalogue schemas, tool schemas and
//                      the arguments the cases pass
//    enum_value        argument=value, per enum block (catalogue and tools,
//                      and the values the cases pass), and key=value for
//                      the name-like values of two catalogue keys
//    catalogue_key     keys of Strumenti/catalogo.json outside the schemas
//    response_field    keys of the answers: every segment of the paths the
//                      cases check, and the keys the library and the bridge
//                      write into a Dictionary<string, object> (Roslyn, with
//                      a semantic model: schema dictionaries are left out)
//    response_value    name-like values of the answers: the textual equals
//                      of the cases, and the literals written as values
//                      into those dictionaries
//    case_key          keys of the cases (Banco/case-keys.json contexts)
//    free_case_key     the frozen free notes of Banco/case-keys.json and the
//                      notes that start with an underscore
//    summary_key       keys of Banco/resoconto/ultimo.json (and of the test
//                      ledger, when there is one)
//    selector          selector kinds (@kind), their fields, and
//                      field=value for the name-like values
//    folder            every Italian folder name of git ls-files
//    file              every Italian file name of git ls-files (the cases
//                      are case_name)
//    case_name         the file names of Banco/casi and Banco/casi-in-attesa
//                      (cases, plans, waiting cases)
//    project           namespaces, root namespaces and assembly names
//    identifier        names declared in C# (Roslyn: types, members, enum
//                      members, locals, parameters...), JavaScript (acorn)
//                      and PowerShell (functions and variables)
//
//    For the protocol types count adds the structured places (catalogue,
//    schemas, cases, Banco/case-keys.json, the summary) and the string
//    literals of the C#, JavaScript and PowerShell sources whose text is
//    exactly the name: a word used in two roles is counted in both. A
//    selector counts its uses in selector strings; folder the folders with
//    that name; file the files; case_name the file and the references to it
//    (twin, name); project the declarations; identifier the tokens.
//
//  WRITING, AND WHAT IS KEPT. Rows found keep their en and note and get a
//  fresh count and where; so do the rows added by hand. Rows the tree no
//  longer has are kept with count 0 (the rename windows remove the Italian
//  names; their rows stay until the map leaves the tree, before the tag).
//
//  THE RENAME WINDOWS LEAVE THIS FILE AND THE MAP ALONE: both name Italian
//  names on purpose (the map in its second column, this script in the keys
//  it reads and in the fixtures of its self-test, which are names the tree
//  already has). The second column of the map is covered by an exception of
//  class transition in exceptions.json: the gate counts it apart, and it must
//  be 0 at the tag.
//
//  THE CHECK (--check), each failure listed, exit 1 if any:
//    a. every Italian name the extractor finds has a row with an English name;
//    b. no two rows of one type share an English name (ignoring case for
//       folders and files) unless every one of them says "merged" in its note;
//    c. no English name, or part of one between dots, equals signs, commas,
//       slashes or brackets, is a C# keyword (contextual ones included) or a
//       JSON Schema keyword;
//    d. the English name is ASCII and has the shape of the Italian one:
//       snake_case for snake_case, PascalCase for PascalCase, camelCase for
//       camelCase, the same extension for a file, a leading underscore kept;
//       unit suffixes stay (_mm, _mm2, _mm3, _kg...) and degrees become _deg;
//       a free note of a case starts with an underscore (W3.1-bis.4);
//    e. a namespace that extends another extends its English name too;
//    f. the gate does not judge the English name Italian (after the rename
//       it would be a new Italian name, and the gate would stop it); an
//       English name equal to the Italian one is only a warning;
//    and the map itself: known types, no row twice, six columns.
//
//  USAGE
//    node Controlli/lingua/name-map-extract.js            extract and rewrite the map
//    node Controlli/lingua/name-map-extract.js --counts   extract, print the counts, write nothing
//    node Controlli/lingua/name-map-extract.js --check    the checks above, on the map as it is
//    node Controlli/lingua/name-map-extract.js --self-test
//        fake rows, one per way of failing the check: every check seen red,
//        and a good map seen green
//    --root <dir>        another checkout (default: the repository of this file)
//    --map <file>        another map (default Controlli/lingua/name-map.tsv)
//    --ledger <file>     a test ledger (Banco/resoconto/tests.json of a bench
//                        run) for summary keys; repeatable. The one in the
//                        tree is read when it is there (git ignores it)
//    --coverage          also list the names of the gate's sets (identifiers,
//                        JSON keys, protocol, paths) that no row covers
//    --show <n>          entries listed per failure (default 40)
//
//  Needs Roslyn (the copy inside MSBuild, compiled by token-stream.js), the
//  .NET Framework reference assemblies (for the semantic model of the
//  answer dictionaries), and Windows PowerShell for the PowerShell scripts.
//
//  EXIT CODES
//    0  written, counted, or the check passed
//    1  --check failed (the failures are listed); --self-test not as expected
//    2  cannot run: no Roslyn or reference assemblies, a source that does not
//       parse, a bad map file, a bad argument
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');

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
const acorn = loadAcorn();
if (!acorn && require.main === module && !process.execArgv.includes('--expose-internals')) {
  const r = cp.spawnSync(process.execPath, ['--expose-internals', __filename].concat(process.argv.slice(2)), { stdio: 'inherit' });
  process.exit(r.status === null ? 2 : r.status);
}

const detector = require('./detect-italian.js');
const tokenStream = require('./token-stream.js');

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const MAP_REL = 'Controlli/lingua/name-map.tsv';
const COLUMNS = ['type', 'it', 'en', 'count', 'where', 'note'];
const TYPES = ['tool', 'sub_action', 'operation', 'catalogue_family', 'case_family', 'argument', 'enum_value',
  'catalogue_key', 'response_field', 'response_value', 'case_key', 'free_case_key', 'summary_key', 'selector',
  'folder', 'file', 'case_name', 'project', 'identifier'];
const WHERE_MAX = 3;

// Where things are, and the Italian keys this script reads. The rename
// windows (W3.1-bis.4 to .6) rename them; after them the map is complete and
// this script has done its work.
const LAYOUT = {
  catalogue: 'Strumenti/catalogo.json',
  caseKeys: 'Banco/case-keys.json',
  // the cases, and the cases that wait for a field of the runner
  cases: ['Banco/casi/', 'Banco/casi-in-attesa/'],
  regressionFolder: 'regressione',
  summary: ['Banco/resoconto/ultimo.json'],
  ledger: 'Banco/resoconto/tests.json',
  toolFolder: 'Strumenti/',
  // the tool list is the string array of Strumenti/ that holds this tool
  toolAnchor: 'api_search',
  // the argument of a tool whose values are its sub-actions
  subActionArgument: 'azione',
  // the code that writes the answers (semantic model of its dictionaries)
  answerGroups: ['library', 'bridge'],
};
const CATALOGUE = {
  operations: 'operazioni', families: 'famiglie', name: 'nome', family: 'famiglia', schema: 'schema',
  // keys whose name-like values are catalogue enumerations of their own
  valueKeys: ['precondizione', 'documento'],
};
const CASE = {
  family: 'famiglia', name: 'nome', twin: 'gemella', what: 'cosa', equals: 'uguale',
  operation: 'operazione', args: 'argomenti',
};

// C# compilation groups: the first that names a file wins. Semantic groups
// get a compilation (the BCL and the groups they reference); the others are
// parsed only.
const GROUPS = [
  { name: 'library', semantic: true, refs: [], match: (f) => f.startsWith('Riscontro.Disegno/') },
  { name: 'bridge', semantic: true, refs: ['library'],
    match: (f) => /^(Mcp|Ponte|Strumenti)\//.test(f) || f === 'Programma.cs' },
  { name: 'other', semantic: false, refs: [], match: () => true },
];
const BCL = ['mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Drawing.dll',
  'System.Web.Extensions.dll', 'Microsoft.CSharp.dll', 'System.Windows.Forms.dll', 'System.Data.dll'];

// ── keywords that an English name may not be (check c) ──────────────────────
const CSHARP_KEYWORDS = ['abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch', 'char', 'checked', 'class',
  'const', 'continue', 'decimal', 'default', 'delegate', 'do', 'double', 'else', 'enum', 'event', 'explicit', 'extern',
  'false', 'finally', 'fixed', 'float', 'for', 'foreach', 'goto', 'if', 'implicit', 'in', 'int', 'interface', 'internal',
  'is', 'lock', 'long', 'namespace', 'new', 'null', 'object', 'operator', 'out', 'override', 'params', 'private',
  'protected', 'public', 'readonly', 'ref', 'return', 'sbyte', 'sealed', 'short', 'sizeof', 'stackalloc', 'static',
  'string', 'struct', 'switch', 'this', 'throw', 'true', 'try', 'typeof', 'uint', 'ulong', 'unchecked', 'unsafe',
  'ushort', 'using', 'virtual', 'void', 'volatile', 'while'];
const CSHARP_CONTEXTUAL = ['add', 'allows', 'alias', 'and', 'ascending', 'args', 'async', 'await', 'by', 'descending',
  'dynamic', 'equals', 'extension', 'field', 'file', 'from', 'get', 'global', 'group', 'init', 'into', 'join', 'let',
  'managed', 'nameof', 'nint', 'not', 'notnull', 'nuint', 'on', 'or', 'orderby', 'partial', 'record', 'remove',
  'required', 'scoped', 'select', 'set', 'unmanaged', 'value', 'var', 'when', 'where', 'with', 'yield'];
const JSON_SCHEMA_KEYWORDS = ['$schema', '$id', '$ref', '$defs', '$anchor', '$dynamicRef', '$dynamicAnchor',
  '$vocabulary', '$comment', '$recursiveRef', '$recursiveAnchor', 'definitions', 'type', 'enum', 'const', 'multipleOf',
  'maximum', 'exclusiveMaximum', 'minimum', 'exclusiveMinimum', 'maxLength', 'minLength', 'pattern', 'maxItems',
  'minItems', 'uniqueItems', 'maxContains', 'minContains', 'maxProperties', 'minProperties', 'required',
  'dependentRequired', 'properties', 'patternProperties', 'additionalProperties', 'unevaluatedProperties',
  'propertyNames', 'items', 'prefixItems', 'additionalItems', 'unevaluatedItems', 'contains', 'allOf', 'anyOf', 'oneOf',
  'not', 'if', 'then', 'else', 'dependentSchemas', 'dependencies', 'format', 'contentEncoding', 'contentMediaType',
  'contentSchema', 'title', 'description', 'default', 'deprecated', 'readOnly', 'writeOnly', 'examples'];
const RESERVED = new Map();
for (const k of CSHARP_KEYWORDS) RESERVED.set(k, 'a C# keyword');
for (const k of CSHARP_CONTEXTUAL) if (!RESERVED.has(k)) RESERVED.set(k, 'a contextual C# keyword');
for (const k of JSON_SCHEMA_KEYWORDS) RESERVED.set(k, RESERVED.has(k) ? RESERVED.get(k) + ' and a JSON Schema keyword' : 'a JSON Schema keyword');

// Unit suffixes that stay as they are, and the one that changes (TODO,
// entry W3.1-bis, section 2: `_mm` stays, the Italian suffix for degrees
// becomes `_deg`).
const KEPT_SUFFIXES = ['_mm', '_mm2', '_mm3', '_kg', '_kg_m3', '_um', '_s', '_px', '_deg'];
const CHANGED_SUFFIXES = [['_gradi', '_deg']];

class CannotRun extends Error {}
function cannot(msg) { throw new CannotRun(msg); }

// ── small helpers ───────────────────────────────────────────────────────────
function readText(file) { return detector.decode(fs.readFileSync(file)); }
function readJson(file) { return JSON.parse(readText(file)); }
function isObject(v) { return v !== null && typeof v === 'object' && !Array.isArray(v); }
function git(root, args) {
  return cp.execFileSync('git', ['-C', root].concat(args), { encoding: 'utf8', maxBuffer: 256 << 20 });
}

// Line numbers of the keys and values of a JSON text, by path (the paths of
// detect-italian.js: a.b for keys, a[0] for array elements).
function jsonLines(text) {
  const keys = new Map(), values = new Map();
  let items = [];
  try { items = detector.parseJsonItems(text); } catch (e) { /* JSON.parse read it: only the line numbers are lost */ }
  for (const it of items) {
    if (it.type === 'key') { if (!keys.has(it.path)) keys.set(it.path, it.line); }
    else if (!values.has(it.path)) values.set(it.path, it.line);
  }
  return { keys, values };
}
const childPath = (p, k) => (p ? p + '.' + k : k);

// A value that is a name, or a short phrase of words (a value an answer or
// a case compares as a whole), not prose.
function nameLike(v) { return typeof v === 'string' && /^[A-Za-z_][A-Za-z0-9_]*$/.test(v); }
function shortValue(v) {
  if (typeof v !== 'string') return false;
  if (!/^[A-Za-z_][A-Za-z0-9_/ -]*[A-Za-z0-9_]$|^[A-Za-z_]$/.test(v)) return false;
  return v.split(/\s+/).length <= 3;
}

// ── exceptions: names the translator never touches ──────────────────────────
function loadExceptions(root) {
  const list = readJson(path.join(root, 'Controlli', 'lingua', 'exceptions.json'));
  return list.filter(e => e.class !== 'transition' && e.pattern && (e.applies_to || []).includes('translator')).map(e => ({
    full: new RegExp('^(?:' + e.pattern + ')$'),
    file: e.file ? new RegExp(e.file) : null,
  }));
}
function exceptedIn(excs, name, file) {
  return excs.some(e => (!e.file || (file && e.file.test(file))) && e.full.test(name));
}

// ── the result of an extraction ─────────────────────────────────────────────
// Every name met in a role is recorded, Italian or not: the Italian ones are
// the rows the map must have; the others are kept only to refresh a row
// added by hand for a name the gate does not see as Italian (quote, the
// family of the dimensions, is an English word too).
class Found {
  constructor(cfg, excs) {
    this.cfg = cfg;
    this.excs = excs;
    this.all = new Map();      // type \t it -> { type, it, italian, count, where: [], notes: Set }
    this.skipped = {};         // type -> names covered by an exception
    this.literals = new Map(); // exact literal text -> [file:line]
    this.english = {};         // type -> Set of names met in that role that are not Italian
    for (const t of TYPES) { this.skipped[t] = new Set(); this.english[t] = new Set(); }
  }
  // The rows the map must have.
  get rows() {
    const out = new Map();
    for (const [k, r] of this.all) if (r.italian) out.set(k, r);
    return out;
  }
  // The identifier rule of the gate; then its prose rule on the name read as
  // words, for a name of two words or more: a phrase glued with underscores
  // or capitals is still a phrase (schema_di_quote, daSelezionare), even
  // where the identifier rule stops at a short function word.
  italian(name) {
    if (detector.isItalianName(name, this.cfg)) return true;
    const words = detector.splitName(name);
    return words.length >= 2 && detector.proseScore(words.join(' '), this.cfg).italian;
  }
  // Record one place of a name in a role. `judge` is the text whose Italian
  // decides (the name itself by default; the value of an enum row). Returns
  // true when the name is Italian.
  add(type, it, where, opts) {
    const o = opts || {};
    if (typeof it !== 'string' || !it || /[\t\r\n]/.test(it)) return false;
    const judge = o.judge === undefined ? it : o.judge;
    const file = where ? where.replace(/:\d+$/, '') : null;
    if (exceptedIn(this.excs, judge, file)) { this.skipped[type].add(it); return false; }
    const key = type + '\t' + it;
    let r = this.all.get(key);
    if (!r) {
      r = { type, it, italian: this.italian(judge), count: 0, where: [], notes: new Set() };
      this.all.set(key, r);
      if (!r.italian) this.english[type].add(it);
    }
    r.count += o.weight === undefined ? 1 : o.weight;
    if (where && r.where.length < WHERE_MAX && !r.where.includes(where)) r.where.push(where);
    if (o.note) r.notes.add(o.note);
    return r.italian;
  }
  literal(value, where) {
    if (!this.literals.has(value)) this.literals.set(value, []);
    this.literals.get(value).push(where);
  }
  // The string literals of the sources whose text is exactly the name.
  addLiteralSites(types) {
    for (const r of this.all.values()) {
      if (!types.includes(r.type)) continue;
      const sites = this.literals.get(r.it) || [];
      r.count += sites.length;
      for (const s of sites) { if (r.where.length >= WHERE_MAX) break; if (!r.where.includes(s)) r.where.push(s); }
    }
  }
}

// =============================================================================
//  C# through Roslyn
// =============================================================================

const CSHARP_HELPER = String.raw`// Declarations, identifier counts, string literals, answer dictionary writes
// and tool schemas of C# sources, through Roslyn (name-map-extract.js).
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

static class NameMapEntry
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
        return NameMapRoslyn.Run(args[1], args[2]);
    }
}

static class NameMapRoslyn
{
    static readonly HashSet<string> SchemaTypes = new HashSet<string> { "object", "string", "number", "integer", "boolean", "array", "null" };
    static readonly HashSet<string> SchemaKeywords = new HashSet<string> {
        "type", "description", "items", "properties", "required", "enum", "minimum", "maximum", "default",
        "minItems", "maxItems", "additionalProperties", "oneOf", "anyOf", "allOf", "not", "title", "format",
        "pattern", "minLength", "maxLength", "const", "examples", "exclusiveMinimum", "exclusiveMaximum",
        "uniqueItems", "$schema", "$ref", "definitions" };

    class Group { public string Name; public bool Semantic; public List<string> Refs = new List<string>(); public List<string> Files = new List<string>(); public Compilation Compilation; }

    public static int Run(string manifest, string outFile)
    {
        var refs = new List<MetadataReference>();
        var groups = new List<Group>();
        var byName = new Dictionary<string, Group>();
        var toolNames = new HashSet<string>();
        foreach (var raw in File.ReadAllLines(manifest, Encoding.UTF8))
        {
            if (raw.Length == 0) continue;
            var f = raw.Split('\t');
            if (f[0] == "R") refs.Add(MetadataReference.CreateFromFile(f[1]));
            else if (f[0] == "G")
            {
                var g = new Group { Name = f[1], Semantic = f[2] == "1" };
                if (f.Length > 3 && f[3].Length > 0) g.Refs.AddRange(f[3].Split(','));
                groups.Add(g); byName[g.Name] = g;
            }
            else if (f[0] == "F") byName[f[1]].Files.Add(f[2]);
        }
        var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);
        using (var w = new StreamWriter(outFile, false, new UTF8Encoding(false)))
        {
            foreach (var g in groups)
            {
                var trees = new List<SyntaxTree>();
                foreach (var file in g.Files)
                    trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file)), options, file));
                if (g.Semantic)
                {
                    var all = new List<MetadataReference>(refs);
                    foreach (var r in g.Refs)
                        if (byName.ContainsKey(r) && byName[r].Compilation != null) all.Add(byName[r].Compilation.ToMetadataReference());
                    g.Compilation = CSharpCompilation.Create(g.Name, trees, all,
                        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
                }
                foreach (var tree in trees)
                {
                    var model = g.Compilation != null ? g.Compilation.GetSemanticModel(tree) : null;
                    w.Write(FileJson(tree, model, g.Name));
                    w.Write('\n');
                }
            }
        }
        return 0;
    }

    // ── one file ────────────────────────────────────────────────────────────
    static string FileJson(SyntaxTree tree, SemanticModel model, string group)
    {
        var root = tree.GetRoot();
        var decls = new List<string>();
        var namespaces = new List<string>();
        var members = new List<string>();
        var calls = new List<string>();
        foreach (var node in root.DescendantNodes())
        {
            string kind = null;
            SyntaxToken id = default(SyntaxToken);
            if (node is BaseTypeDeclarationSyntax) { kind = "type"; id = ((BaseTypeDeclarationSyntax)node).Identifier; }
            else if (node is DelegateDeclarationSyntax) { kind = "type"; id = ((DelegateDeclarationSyntax)node).Identifier; }
            else if (node is MethodDeclarationSyntax) { kind = "method"; id = ((MethodDeclarationSyntax)node).Identifier; }
            else if (node is LocalFunctionStatementSyntax) { kind = "local function"; id = ((LocalFunctionStatementSyntax)node).Identifier; }
            else if (node is PropertyDeclarationSyntax) { kind = "property"; id = ((PropertyDeclarationSyntax)node).Identifier; }
            else if (node is EventDeclarationSyntax) { kind = "event"; id = ((EventDeclarationSyntax)node).Identifier; }
            else if (node is VariableDeclaratorSyntax)
            {
                var v = (VariableDeclaratorSyntax)node;
                var holder = v.Parent == null ? null : v.Parent.Parent;
                var fd = holder as FieldDeclarationSyntax;
                if (fd != null) kind = fd.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)) ? "constant" : "field";
                else if (holder is EventFieldDeclarationSyntax) kind = "event";
                else kind = "local";
                id = v.Identifier;
            }
            else if (node is EnumMemberDeclarationSyntax) { kind = "enum member"; id = ((EnumMemberDeclarationSyntax)node).Identifier; }
            else if (node is ParameterSyntax) { kind = "parameter"; id = ((ParameterSyntax)node).Identifier; }
            else if (node is TypeParameterSyntax) { kind = "type parameter"; id = ((TypeParameterSyntax)node).Identifier; }
            else if (node is ForEachStatementSyntax) { kind = "local"; id = ((ForEachStatementSyntax)node).Identifier; }
            else if (node is CatchDeclarationSyntax) { kind = "local"; id = ((CatchDeclarationSyntax)node).Identifier; }
            else if (node is SingleVariableDesignationSyntax) { kind = "local"; id = ((SingleVariableDesignationSyntax)node).Identifier; }
            else if (node is FromClauseSyntax) { kind = "local"; id = ((FromClauseSyntax)node).Identifier; }
            else if (node is LetClauseSyntax) { kind = "local"; id = ((LetClauseSyntax)node).Identifier; }
            else if (node is JoinClauseSyntax) { kind = "local"; id = ((JoinClauseSyntax)node).Identifier; }
            else if (node is JoinIntoClauseSyntax) { kind = "local"; id = ((JoinIntoClauseSyntax)node).Identifier; }
            else if (node is QueryContinuationSyntax) { kind = "local"; id = ((QueryContinuationSyntax)node).Identifier; }
            else if (node is LabeledStatementSyntax) { kind = "label"; id = ((LabeledStatementSyntax)node).Identifier; }
            else if (node is AnonymousObjectMemberDeclaratorSyntax)
            {
                var a = (AnonymousObjectMemberDeclaratorSyntax)node;
                if (a.NameEquals != null) { kind = "anonymous member"; id = a.NameEquals.Name.Identifier; }
            }
            else if (node is TupleElementSyntax) { kind = "tuple element"; id = ((TupleElementSyntax)node).Identifier; }
            else if (node is ArgumentSyntax)
            {
                var a = (ArgumentSyntax)node;
                if (a.NameColon != null && a.Parent is TupleExpressionSyntax) { kind = "tuple element"; id = a.NameColon.Name.Identifier; }
            }
            else if (node is UsingDirectiveSyntax)
            {
                var u = (UsingDirectiveSyntax)node;
                if (u.Alias != null) { kind = "alias"; id = u.Alias.Name.Identifier; }
            }
            else if (node is NamespaceDeclarationSyntax)
            {
                var n = (NamespaceDeclarationSyntax)node;
                namespaces.Add(Arr(Line(n.Name.GetFirstToken()), J(n.Name.ToString())));
            }
            if (kind != null && id.ValueText.Length > 0) decls.Add(Arr(Line(id), J(kind), J(id.ValueText)));

            if (node is FieldDeclarationSyntax) Member((FieldDeclarationSyntax)node, members);
            else if (node is PropertyDeclarationSyntax) Member((PropertyDeclarationSyntax)node, members);
            else if (node is InvocationExpressionSyntax) Call((InvocationExpressionSyntax)node, calls);
        }

        var idents = new Dictionary<string, int>();
        var strings = new List<string>();
        foreach (var t in root.DescendantTokens())
        {
            if (t.IsKind(SyntaxKind.IdentifierToken))
            {
                int n; idents.TryGetValue(t.ValueText, out n); idents[t.ValueText] = n + 1;
            }
            else if (t.Kind().ToString().Contains("StringLiteral") && !t.Kind().ToString().Contains("Interpolated"))
                strings.Add(Arr(Line(t), J(t.ValueText)));
        }

        var writes = model != null ? Writes(root, model) : new List<string>();
        var errors = tree.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        var sb = new StringBuilder();
        sb.Append("{\"file\":").Append(J(tree.FilePath)).Append(",\"group\":").Append(J(group));
        sb.Append(",\"errors\":").Append(errors.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"decls\":[").Append(string.Join(",", decls)).Append(']');
        sb.Append(",\"namespaces\":[").Append(string.Join(",", namespaces)).Append(']');
        sb.Append(",\"idents\":[").Append(string.Join(",", idents.Select(kv => Arr(J(kv.Key), kv.Value.ToString(CultureInfo.InvariantCulture))))).Append(']');
        sb.Append(",\"strings\":[").Append(string.Join(",", strings)).Append(']');
        sb.Append(",\"writes\":[").Append(string.Join(",", writes)).Append(']');
        sb.Append(",\"members\":[").Append(string.Join(",", members)).Append(']');
        sb.Append(",\"calls\":[").Append(string.Join(",", calls)).Append(']');
        sb.Append('}');
        return sb.ToString();
    }

    // ── keys written into a Dictionary<string, object> ─────────────────────
    // An initializer [key] = value or { key, value } of a new dictionary, an
    // assignment dict[key] = value, a call dict.Add(key, value): the key a
    // string literal, the dictionary an IDictionary<string, object> by the
    // semantic model. A member that builds a JSON Schema node (["type"] =
    // "object" or another schema type) writes schemas, not answers: its
    // writes are marked.
    static List<string> Writes(SyntaxNode root, SemanticModel model)
    {
        var schemaMembers = new HashSet<SyntaxNode>();
        foreach (var oc in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            if (oc.Initializer != null && IsSchemaNode(oc.Initializer)) { var m = MemberOf(oc); if (m != null) schemaMembers.Add(m); }
        var list = new List<string>();
        foreach (var node in root.DescendantNodes())
        {
            var oc = node as ObjectCreationExpressionSyntax;
            if (oc != null && oc.Initializer != null)
            {
                if (!IsStringObjectDictionary(model.GetTypeInfo(oc).Type)) continue;
                foreach (var x in oc.Initializer.Expressions)
                {
                    var a = x as AssignmentExpressionSyntax;
                    if (a != null && a.Left is ImplicitElementAccessSyntax)
                    {
                        var key = KeyOf(((ImplicitElementAccessSyntax)a.Left).ArgumentList);
                        if (key != null) list.Add(Write(x, key, "initializer", a.Right, schemaMembers));
                        continue;
                    }
                    var ce = x as InitializerExpressionSyntax;
                    if (ce != null && ce.Expressions.Count == 2)
                    {
                        var key = Str(ce.Expressions[0]);
                        if (key != null) list.Add(Write(x, key, "collection", ce.Expressions[1], schemaMembers));
                    }
                }
                continue;
            }
            var asg = node as AssignmentExpressionSyntax;
            if (asg != null && asg.Left is ElementAccessExpressionSyntax)
            {
                var ea = (ElementAccessExpressionSyntax)asg.Left;
                var key = KeyOf(ea.ArgumentList);
                if (key != null && IsStringObjectDictionary(model.GetTypeInfo(ea.Expression).Type))
                    list.Add(Write(node, key, "indexer", asg.Right, schemaMembers));
                continue;
            }
            var inv = node as InvocationExpressionSyntax;
            if (inv != null)
            {
                var access = inv.Expression as MemberAccessExpressionSyntax;
                if (access == null || access.Name.Identifier.ValueText != "Add" || inv.ArgumentList.Arguments.Count != 2) continue;
                var key = Str(inv.ArgumentList.Arguments[0].Expression);
                if (key != null && IsStringObjectDictionary(model.GetTypeInfo(access.Expression).Type))
                    list.Add(Write(node, key, "add", inv.ArgumentList.Arguments[1].Expression, schemaMembers));
            }
        }
        return list;
    }

    static string Write(SyntaxNode at, string key, string form, ExpressionSyntax value, HashSet<SyntaxNode> schemaMembers)
    {
        var m = MemberOf(at);
        var values = new List<string>();
        Literals(value, values);
        return Arr(Line(at.GetFirstToken()), J(key), J(form), J(MemberName(m)), schemaMembers.Contains(m) ? "1" : "0",
                   "[" + string.Join(",", values.Select(J)) + "]");
    }

    static bool IsSchemaNode(InitializerExpressionSyntax init)
    {
        foreach (var x in init.Expressions)
        {
            var a = x as AssignmentExpressionSyntax;
            if (a == null || !(a.Left is ImplicitElementAccessSyntax)) continue;
            if (KeyOf(((ImplicitElementAccessSyntax)a.Left).ArgumentList) != "type") continue;
            var v = Str(a.Right);
            if (v != null && SchemaTypes.Contains(v)) return true;
        }
        return false;
    }

    static bool IsStringObjectDictionary(ITypeSymbol t)
    {
        var n = t as INamedTypeSymbol;
        if (n == null || n.TypeKind == TypeKind.Error) return false;
        if (IsIDictionaryStringObject(n)) return true;
        foreach (var i in n.AllInterfaces) if (IsIDictionaryStringObject(i)) return true;
        return false;
    }

    static bool IsIDictionaryStringObject(INamedTypeSymbol n)
    {
        if (!n.IsGenericType || n.TypeArguments.Length != 2) return false;
        if (n.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.IDictionary<TKey, TValue>") return false;
        return n.TypeArguments[0].SpecialType == SpecialType.System_String && n.TypeArguments[1].SpecialType == SpecialType.System_Object;
    }

    static void Literals(ExpressionSyntax e, List<string> into)
    {
        if (e == null) return;
        var s = Str(e);
        if (s != null) { into.Add(s); return; }
        var c = e as ConditionalExpressionSyntax;
        if (c != null) { Literals(c.WhenTrue, into); Literals(c.WhenFalse, into); return; }
        var b = e as BinaryExpressionSyntax;
        if (b != null && b.IsKind(SyntaxKind.CoalesceExpression)) { Literals(b.Right, into); return; }
        var p = e as ParenthesizedExpressionSyntax;
        if (p != null) Literals(p.Expression, into);
    }

    // ── members that are fixed lists of strings, or point at one ───────────
    // Used to resolve the enum of a tool schema built from a member
    // (Nomi(Type.Member)): a string array or list, the keys of a dictionary
    // initializer, or a getter that returns another member (or its .Keys).
    static void Member(FieldDeclarationSyntax f, List<string> into)
    {
        foreach (var v in f.Declaration.Variables)
            if (v.Initializer != null) MemberValue(f, v.Identifier, v.Initializer.Value, into);
    }

    static void Member(PropertyDeclarationSyntax p, List<string> into)
    {
        ExpressionSyntax e = null;
        if (p.ExpressionBody != null) e = p.ExpressionBody.Expression;
        else if (p.Initializer != null) e = p.Initializer.Value;
        else if (p.AccessorList != null)
        {
            var get = p.AccessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (get != null && get.ExpressionBody != null) e = get.ExpressionBody.Expression;
            else if (get != null && get.Body != null && get.Body.Statements.Count == 1 && get.Body.Statements[0] is ReturnStatementSyntax)
                e = ((ReturnStatementSyntax)get.Body.Statements[0]).Expression;
        }
        if (e != null) MemberValue(p, p.Identifier, e, into);
    }

    static void MemberValue(SyntaxNode decl, SyntaxToken id, ExpressionSyntax e, List<string> into)
    {
        var type = decl.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        if (type == null) return;
        var strings = new List<string>();
        var keys = new List<string>();
        string alias = null;
        InitializerExpressionSyntax init = null;
        if (e is ArrayCreationExpressionSyntax) init = ((ArrayCreationExpressionSyntax)e).Initializer;
        else if (e is ImplicitArrayCreationExpressionSyntax) init = ((ImplicitArrayCreationExpressionSyntax)e).Initializer;
        else if (e is InitializerExpressionSyntax) init = (InitializerExpressionSyntax)e;
        else if (e is ObjectCreationExpressionSyntax) init = ((ObjectCreationExpressionSyntax)e).Initializer;
        else if (e is MemberAccessExpressionSyntax || e is IdentifierNameSyntax) alias = e.ToString();
        if (init != null)
        {
            foreach (var x in init.Expressions)
            {
                var s = Str(x);
                if (s != null) { strings.Add(s); continue; }
                var ce = x as InitializerExpressionSyntax;
                if (ce != null && ce.Expressions.Count >= 1) { var k = Str(ce.Expressions[0]); if (k != null) keys.Add(k); continue; }
                var a = x as AssignmentExpressionSyntax;
                if (a != null && a.Left is ImplicitElementAccessSyntax) { var k = KeyOf(((ImplicitElementAccessSyntax)a.Left).ArgumentList); if (k != null) keys.Add(k); }
            }
        }
        if (strings.Count == 0 && keys.Count == 0 && alias == null) return;
        into.Add(Arr(Line(id), J(type.Identifier.ValueText), J(id.ValueText),
            "[" + string.Join(",", strings.Select(J)) + "]", "[" + string.Join(",", keys.Select(J)) + "]", alias == null ? "null" : J(alias)));
    }

    // ── calls whose first argument is a string literal: tool declarations ──
    // Every such call with three arguments or more is reported with its
    // schema: the argument names, and the enum blocks (literal values, or the
    // member they are built from). Which calls declare tools is decided by
    // the caller, against the tool list.
    static void Call(InvocationExpressionSyntax inv, List<string> into)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count < 3) return;
        var name = Str(args[0].Expression);
        if (name == null) return;
        var props = new List<string>();
        var enums = new List<string>();
        for (int i = 1; i < args.Count; i++) SchemaWalk(args[i].Expression, null, props, enums);
        if (props.Count == 0 && enums.Count == 0) return;
        into.Add(Arr(Line(args[0].Expression.GetFirstToken()), J(name), "[" + string.Join(",", props) + "]", "[" + string.Join(",", enums) + "]"));
    }

    // A dictionary whose keys are all JSON Schema keywords is a schema node;
    // any other dictionary is a map of properties (its keys are argument
    // names, its values schema nodes). Calls are walked through.
    static void SchemaWalk(ExpressionSyntax e, string prop, List<string> props, List<string> enums)
    {
        if (e == null) return;
        var oc = e as ObjectCreationExpressionSyntax;
        if (oc != null && oc.Initializer != null)
        {
            var pairs = new List<KeyValuePair<string, ExpressionSyntax>>();
            foreach (var x in oc.Initializer.Expressions)
            {
                var a = x as AssignmentExpressionSyntax;
                if (a == null || !(a.Left is ImplicitElementAccessSyntax)) continue;
                var k = KeyOf(((ImplicitElementAccessSyntax)a.Left).ArgumentList);
                if (k != null) pairs.Add(new KeyValuePair<string, ExpressionSyntax>(k, a.Right));
            }
            if (pairs.Count == 0) return;
            bool node = pairs.All(kv => SchemaKeywords.Contains(kv.Key));
            foreach (var kv in pairs)
            {
                if (node)
                {
                    if (kv.Key == "enum") enums.Add(EnumOf(kv.Value, prop));
                    else if (kv.Key == "items") SchemaWalk(kv.Value, prop, props, enums);
                    else if (kv.Key == "properties") SchemaWalk(kv.Value, null, props, enums);
                }
                else if (!SchemaKeywords.Contains(kv.Key))
                {
                    props.Add(Arr(Line(kv.Value.GetFirstToken()), J(kv.Key)));
                    SchemaWalk(kv.Value, kv.Key, props, enums);
                }
            }
            return;
        }
        var inv = e as InvocationExpressionSyntax;
        if (inv != null) foreach (var a in inv.ArgumentList.Arguments) SchemaWalk(a.Expression, prop, props, enums);
    }

    static string EnumOf(ExpressionSyntax v, string prop)
    {
        var values = new List<string>();
        string reference = null;
        InitializerExpressionSyntax init = null;
        if (v is ArrayCreationExpressionSyntax) init = ((ArrayCreationExpressionSyntax)v).Initializer;
        else if (v is ImplicitArrayCreationExpressionSyntax) init = ((ImplicitArrayCreationExpressionSyntax)v).Initializer;
        if (init != null) foreach (var x in init.Expressions) { var s = Str(x); if (s != null) values.Add(s); }
        var inv = v as InvocationExpressionSyntax;
        if (inv != null && inv.ArgumentList.Arguments.Count == 1) reference = inv.ArgumentList.Arguments[0].Expression.ToString();
        else if (v is MemberAccessExpressionSyntax || v is IdentifierNameSyntax) reference = v.ToString();
        return Arr(Line(v.GetFirstToken()), prop == null ? "null" : J(prop), "[" + string.Join(",", values.Select(J)) + "]", reference == null ? "null" : J(reference));
    }

    // ── small helpers ───────────────────────────────────────────────────────
    static string KeyOf(BracketedArgumentListSyntax args)
    {
        if (args == null || args.Arguments.Count != 1) return null;
        return Str(args.Arguments[0].Expression);
    }

    static string Str(ExpressionSyntax e)
    {
        var l = e as LiteralExpressionSyntax;
        return l != null && l.IsKind(SyntaxKind.StringLiteralExpression) ? l.Token.ValueText : null;
    }

    static SyntaxNode MemberOf(SyntaxNode n)
    {
        return n.Ancestors().FirstOrDefault(a => a is BaseMethodDeclarationSyntax || a is PropertyDeclarationSyntax ||
            a is FieldDeclarationSyntax || a is IndexerDeclarationSyntax || a is EventDeclarationSyntax);
    }

    static string MemberName(SyntaxNode m)
    {
        if (m == null) return "";
        var type = m.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        string name = m is MethodDeclarationSyntax ? ((MethodDeclarationSyntax)m).Identifier.ValueText
            : m is PropertyDeclarationSyntax ? ((PropertyDeclarationSyntax)m).Identifier.ValueText
            : m is ConstructorDeclarationSyntax ? ".ctor"
            : m is FieldDeclarationSyntax ? string.Join(",", ((FieldDeclarationSyntax)m).Declaration.Variables.Select(v => v.Identifier.ValueText))
            : m.Kind().ToString();
        return (type == null ? "" : type.Identifier.ValueText + ".") + name;
    }

    static int Line(SyntaxToken t) { return t.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }

    static string Arr(params object[] parts)
    {
        return "[" + string.Join(",", parts.Select(p => p is int ? ((int)p).ToString(CultureInfo.InvariantCulture) : (string)p)) + "]";
    }

    static string J(string s)
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

function referenceAssemblies() {
  const pf86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
  for (const v of ['v4.8.1', 'v4.8', 'v4.7.2']) {
    const d = path.join(pf86, 'Reference Assemblies', 'Microsoft', 'Framework', '.NETFramework', v);
    if (fs.existsSync(path.join(d, 'mscorlib.dll'))) return d;
  }
  return null;
}

function scanCSharp(root, files) {
  const refDir = referenceAssemblies();
  if (!refDir) cannot('no .NET Framework reference assemblies (v4.8): the answer dictionaries need a semantic model');
  let helper;
  try { helper = tokenStream.compileRoslynProgram(CSHARP_HELPER, 'NameMapRoslyn'); }
  catch (e) { cannot(e.message); }
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'name-map-'));
  try {
    const lines = [];
    for (const r of BCL) { const p = path.join(refDir, r); if (fs.existsSync(p)) lines.push('R\t' + p); }
    const byGroup = new Map(GROUPS.map(g => [g.name, []]));
    for (const f of files) byGroup.get(GROUPS.find(g => g.match(f)).name).push(f);
    for (const g of GROUPS) {
      lines.push('G\t' + g.name + '\t' + (g.semantic ? '1' : '0') + '\t' + g.refs.join(','));
      for (const f of byGroup.get(g.name)) lines.push('F\t' + g.name + '\t' + path.join(root, f));
    }
    const manifest = path.join(dir, 'manifest.txt');
    const out = path.join(dir, 'scan.jsonl');
    fs.writeFileSync(manifest, lines.join('\n') + '\n', 'utf8');
    const r = cp.spawnSync(helper.exe, [helper.roslyn, manifest, out], { encoding: 'utf8', maxBuffer: 64 << 20 });
    if (r.error) cannot('the C# helper does not start: ' + r.error.message);
    if (r.status !== 0 || !fs.existsSync(out)) cannot('the C# helper failed (exit ' + r.status + '):\n' + (r.stdout || '') + (r.stderr || ''));
    const result = new Map();
    for (const line of fs.readFileSync(out, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      const o = JSON.parse(line);
      const rel = path.relative(root, o.file).replace(/\\/g, '/');
      if (o.errors) cannot(rel + ' does not parse as C# (' + o.errors + ' errors)');
      result.set(rel, o);
    }
    for (const f of files) if (!result.has(f)) cannot('the C# helper gave nothing for ' + f);
    return result;
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

// =============================================================================
//  JavaScript (acorn) and PowerShell (its own parser, through token-stream.js)
// =============================================================================

function parseJs(text, file) {
  if (!acorn) cannot('no JavaScript parser: neither a local acorn package nor the copy inside Node (node --expose-internals)');
  const src = text.replace(/^\ufeff/, '');
  let last;
  for (const sourceType of ['script', 'module']) {
    try {
      return acorn.parse(src, { ecmaVersion: 'latest', sourceType, allowHashBang: true, allowReturnOutsideFunction: true, locations: true });
    } catch (e) { last = e; }
  }
  cannot(file + ' does not parse as JavaScript: ' + last.message);
}

// Declared names (functions, classes, variables, parameters, catch
// parameters, labels), every identifier token outside member and key
// position, and the string literals.
function scanJs(ast) {
  const decls = [], idents = new Map(), strings = [];
  const declare = (pattern, kind) => {
    if (!pattern) return;
    if (pattern.type === 'Identifier') decls.push([pattern.loc.start.line, kind, pattern.name]);
    else if (pattern.type === 'ObjectPattern') pattern.properties.forEach(p => declare(p.type === 'RestElement' ? p.argument : p.value, kind));
    else if (pattern.type === 'ArrayPattern') pattern.elements.forEach(e => declare(e, kind));
    else if (pattern.type === 'AssignmentPattern') declare(pattern.left, kind);
    else if (pattern.type === 'RestElement') declare(pattern.argument, kind);
  };
  const walk = (node, parent, key) => {
    if (!node || typeof node.type !== 'string') return;
    switch (node.type) {
      case 'FunctionDeclaration': case 'FunctionExpression': case 'ArrowFunctionExpression':
        if (node.id) declare(node.id, 'function');
        node.params.forEach(p => declare(p, 'parameter'));
        break;
      case 'ClassDeclaration': case 'ClassExpression': if (node.id) declare(node.id, 'class'); break;
      case 'VariableDeclarator': declare(node.id, 'variable'); break;
      case 'CatchClause': declare(node.param, 'variable'); break;
      case 'LabeledStatement': declare(node.label, 'label'); break;
      case 'MethodDefinition': if (!node.computed && node.key.type === 'Identifier') decls.push([node.key.loc.start.line, 'method', node.key.name]); break;
      case 'Identifier': {
        const memberProperty = parent && parent.type === 'MemberExpression' && key === 'property' && !parent.computed;
        const objectKey = parent && parent.type === 'Property' && key === 'key' && !parent.computed && !parent.shorthand;
        const methodKey = parent && parent.type === 'MethodDefinition' && key === 'key';
        if (!memberProperty && !objectKey && !methodKey) idents.set(node.name, (idents.get(node.name) || 0) + 1);
        break;
      }
      case 'Literal': if (typeof node.value === 'string') strings.push([node.loc.start.line, node.value]); break;
      case 'TemplateLiteral':
        if (node.expressions.length === 0 && node.quasis[0].value.cooked !== null) strings.push([node.loc.start.line, node.quasis[0].value.cooked]);
        break;
      default: break;
    }
    for (const k of Object.keys(node)) {
      if (k === 'loc' || k === 'start' || k === 'end') continue;
      const v = node[k];
      if (Array.isArray(v)) v.forEach(x => { if (x && typeof x.type === 'string') walk(x, node, k); });
      else if (v && typeof v.type === 'string') walk(v, node, k);
    }
  };
  walk(ast, null, null);
  return { decls, idents, strings };
}

const PS_STRINGS = ['StringLiteral', 'StringExpandable', 'HereStringLiteral', 'HereStringExpandable'];
function scanPowerShell(tokens) {
  const decls = [], idents = new Map(), strings = [];
  const seenVariables = new Set();
  const visit = (list) => {
    for (let i = 0; i < list.length; i++) {
      const t = list[i];
      if (t.kind === 'Variable' || t.kind === 'SplattedVariable') {
        let name = t.value.replace(/^(?:script|global|local|private|using):/i, '');
        if (/:/.test(name)) continue; // env:, a drive or a provider: not a name of the script
        idents.set(name, (idents.get(name) || 0) + 1);
        if (!seenVariables.has(name)) { seenVariables.add(name); decls.push([t.line, 'variable', name]); }
      } else if ((t.kind === 'Function' || t.kind === 'Filter' || t.kind === 'Workflow') && list[i + 1]) {
        const n = list[i + 1];
        decls.push([n.line, 'function', n.value]);
      } else if ((t.kind === 'Generic' || t.kind === 'Identifier') && t.value) {
        idents.set(t.value, (idents.get(t.value) || 0) + 1);
      }
      if (PS_STRINGS.includes(t.kind) && !(t.holes && t.holes.length)) strings.push([t.line, t.value]);
      if (t.holes) t.holes.forEach(visit);
    }
  };
  visit(tokens);
  return { decls, idents, strings };
}

// =============================================================================
//  The extraction
// =============================================================================

function extract(root, opts) {
  const cfg = detector.loadConfig(root);
  const excs = loadExceptions(root);
  const found = new Found(cfg, excs);
  const files = detector.listFiles(root);
  const tracked = new Set(files);
  const exists = (f) => tracked.has(f) && fs.existsSync(path.join(root, f));

  // ── sources: C#, JavaScript, PowerShell ─────────────────────────────────
  const csFiles = files.filter(f => f.endsWith('.cs') && fs.existsSync(path.join(root, f)));
  const cs = scanCSharp(root, csFiles);
  const jsFiles = files.filter(f => /\.(js|cjs|mjs)$/.test(f) && fs.existsSync(path.join(root, f)));
  const js = new Map();
  for (const f of jsFiles) js.set(f, scanJs(parseJs(readText(path.join(root, f)), f)));
  const psFiles = files.filter(f => /\.(ps1|psm1|psd1)$/.test(f) && fs.existsSync(path.join(root, f)));
  const ps = new Map();
  if (psFiles.length) {
    let tokens;
    try { tokens = tokenStream.tokenizeAll(psFiles.map(f => ({ key: f, lang: 'powershell', text: readText(path.join(root, f)) }))); }
    catch (e) { cannot(e.message); }
    for (const f of psFiles) ps.set(f, scanPowerShell(tokens.get(f)));
  }

  // the literal index: every string literal of the sources, by exact text
  for (const [f, o] of cs) for (const [line, v] of o.strings) found.literal(v, f + ':' + line);
  for (const [f, o] of js) for (const [line, v] of o.strings) found.literal(v, f + ':' + line);
  for (const [f, o] of ps) for (const [line, v] of o.strings) found.literal(v, f + ':' + line);

  // ── the catalogue ───────────────────────────────────────────────────────
  const enumsByArgument = new Map(); // argument -> Set of values (catalogue and tools)
  const addEnum = (arg, value) => { if (!enumsByArgument.has(arg)) enumsByArgument.set(arg, new Set()); enumsByArgument.get(arg).add(value); };
  if (!exists(LAYOUT.catalogue)) cannot('no catalogue at ' + LAYOUT.catalogue);
  {
    const text = readText(path.join(root, LAYOUT.catalogue));
    const cat = JSON.parse(text);
    const lines = jsonLines(text);
    const at = (p, kind) => LAYOUT.catalogue + ':' + ((kind === 'key' ? lines.keys : lines.values).get(p) || 1);
    if (!isObject(cat) || !Array.isArray(cat[CATALOGUE.operations]) || !isObject(cat[CATALOGUE.families]))
      cannot(LAYOUT.catalogue + ': no "' + CATALOGUE.operations + '" list or no "' + CATALOGUE.families + '" object');
    for (const k of Object.keys(cat)) found.add('catalogue_key', k, at(k, 'key'));
    for (const fam of Object.keys(cat[CATALOGUE.families])) {
      found.add('catalogue_family', fam, at(childPath(CATALOGUE.families, fam), 'key'), { note: 'catalogue family, distinct from the case families' });
    }
    cat[CATALOGUE.operations].forEach((op, i) => {
      const base = CATALOGUE.operations + '[' + i + ']';
      if (!isObject(op) || typeof op[CATALOGUE.name] !== 'string') cannot(LAYOUT.catalogue + ': operation ' + i + ' has no "' + CATALOGUE.name + '"');
      for (const k of Object.keys(op)) found.add('catalogue_key', k, at(childPath(base, k), 'key'));
      found.add('operation', op[CATALOGUE.name], at(childPath(base, CATALOGUE.name), 'value'));
      if (typeof op[CATALOGUE.family] === 'string') found.add('catalogue_family', op[CATALOGUE.family], at(childPath(base, CATALOGUE.family), 'value'));
      for (const k of CATALOGUE.valueKeys) {
        const v = op[k];
        if (typeof v === 'string' && /^[A-Za-z_][A-Za-z0-9_/]*$/.test(v))
          found.add('enum_value', k + '=' + v, at(childPath(base, k), 'value'), { judge: v, note: 'value of a catalogue key' });
      }
      if (op[CATALOGUE.schema] !== undefined) schemaArguments(op[CATALOGUE.schema], childPath(base, CATALOGUE.schema), null);
    });
    // a JSON Schema: the keys of every "properties" object are argument
    // names; an "enum" belongs to the property that holds it (or its items)
    function schemaArguments(node, p, prop) {
      if (Array.isArray(node)) { node.forEach((x, i) => schemaArguments(x, p + '[' + i + ']', prop)); return; }
      if (!isObject(node)) return;
      for (const [k, v] of Object.entries(node)) {
        const kp = childPath(p, k);
        if (k === 'properties' && isObject(v)) {
          for (const [name, sub] of Object.entries(v)) {
            found.add('argument', name, at(childPath(kp, name), 'key'), { note: 'catalogue' });
            schemaArguments(sub, childPath(kp, name), name);
          }
        } else if (k === 'enum' && Array.isArray(v)) {
          v.forEach((x, i) => {
            if (typeof x !== 'string' || prop === null) return;
            addEnum(prop, x);
            found.add('enum_value', prop + '=' + x, at(kp + '[' + i + ']', 'value'), { judge: x, note: 'catalogue enum' });
          });
        } else if (typeof v === 'object') schemaArguments(v, kp, prop);
      }
    }
  }

  // ── the tools (Strumenti/) ──────────────────────────────────────────────
  {
    const members = [];
    for (const [f, o] of cs) for (const m of o.members) members.push({ file: f, line: m[0], type: m[1], name: m[2], strings: m[3], keys: m[4], alias: m[5] });
    const lists = members.filter(m => m.file.startsWith(LAYOUT.toolFolder) && m.strings.includes(LAYOUT.toolAnchor));
    if (lists.length !== 1) cannot('expected one string list in ' + LAYOUT.toolFolder + ' that holds the tool ' + LAYOUT.toolAnchor + ', found ' + lists.length);
    const toolNames = new Set(lists[0].strings);
    // a place in C# is a string literal too, counted once by the literal
    // pass at the end: here it only gives the first places of where
    const once = { weight: 0 };
    for (const t of toolNames) found.add('tool', t, lists[0].file + ':' + lists[0].line, once);
    const resolve = (type, expr, hops) => {
      if (!expr || hops > 6) return [];
      const parts = expr.split('.');
      let keys = false;
      if (parts[parts.length - 1] === 'Keys') { keys = true; parts.pop(); }
      const [t, n] = parts.length >= 2 ? [parts[parts.length - 2], parts[parts.length - 1]] : [type, parts[0]];
      const m = members.find(x => x.type === t && x.name === n);
      if (!m) return [];
      if (keys && m.keys.length) return m.keys;
      if (m.strings.length) return m.strings;
      if (m.keys.length) return m.keys;
      return resolve(m.type, m.alias, hops + 1);
    };
    let declared = 0;
    for (const [f, o] of cs) {
      if (!f.startsWith(LAYOUT.toolFolder)) continue;
      for (const [line, name, props, enums] of o.calls) {
        if (!toolNames.has(name)) continue;
        declared++;
        found.add('tool', name, f + ':' + line, once);
        for (const [pline, prop] of props) found.add('argument', prop, f + ':' + pline, { note: 'tool', weight: 0 });
        for (const [eline, prop, values, ref] of enums) {
          const vals = values.length ? values : resolve(null, ref, 0);
          if (prop === null) continue;
          if (vals.length === 0) cannot(f + ':' + eline + ': the enum of the tool argument "' + prop + '" could not be resolved (' + ref + ')');
          for (const v of vals) {
            addEnum(prop, v);
            if (prop === LAYOUT.subActionArgument) found.add('sub_action', v, f + ':' + eline, { note: 'value of the action argument of a tool', weight: 0 });
            else found.add('enum_value', prop + '=' + v, f + ':' + eline, { judge: v, note: 'tool enum' });
          }
        }
      }
    }
    if (declared === 0) cannot('no tool declaration found in ' + LAYOUT.toolFolder);
  }

  // ── the case keys (Banco/case-keys.json) ────────────────────────────────
  if (!exists(LAYOUT.caseKeys)) cannot('no case keys at ' + LAYOUT.caseKeys);
  const ckText = readText(path.join(root, LAYOUT.caseKeys));
  const ck = JSON.parse(ckText);
  if (!isObject(ck.roots) || !isObject(ck.contexts)) cannot(LAYOUT.caseKeys + ' has no "roots" or "contexts"');
  const contextOfKey = new Map(); // case key -> contexts it was met in
  const inContext = (k, ctx) => { if (!contextOfKey.has(k)) contextOfKey.set(k, new Set()); contextOfKey.get(k).add(ctx); };
  {
    const lines = jsonLines(ckText);
    for (const [name, c] of Object.entries(ck.contexts)) {
      const base = 'contexts.' + name;
      (c.read || []).forEach((k, i) => {
        found.add('case_key', k, LAYOUT.caseKeys + ':' + (lines.values.get(base + '.read[' + i + ']') || 1));
        inContext(k, name);
      });
      for (const part of ['documented', 'children']) {
        for (const k of Object.keys(c[part] || {})) {
          found.add('case_key', k, LAYOUT.caseKeys + ':' + (lines.keys.get(base + '.' + part + '.' + k) || 1));
          inContext(k, name);
        }
      }
      for (const k of Object.keys(c.frozen_free || {})) found.add('free_case_key', k, LAYOUT.caseKeys + ':' + (lines.keys.get(base + '.frozen_free.' + k) || 1), { note: 'frozen free note' });
    }
  }

  // ── the cases ───────────────────────────────────────────────────────────
  const caseRoot = (f) => LAYOUT.cases.find(c => f.startsWith(c));
  const caseFiles = files.filter(f => caseRoot(f) && f.endsWith('.json') && fs.existsSync(path.join(root, f)));
  for (const f of caseFiles) {
    const text = readText(path.join(root, f));
    const body = JSON.parse(text);
    const lines = jsonLines(text);
    const kAt = (p) => f + ':' + (lines.keys.get(p) || 1);
    const vAt = (p) => f + ':' + (lines.values.get(p) || 1);
    const folder = f.slice(caseRoot(f).length).split('/')[0];
    const rootContext = Array.isArray(body) ? null : ck.roots[folder === LAYOUT.regressionFolder ? 'regression' : 'declared'];
    // the context of a step of a plan: the one that reads the operation
    const planStep = Object.keys(ck.contexts).find(c => (ck.contexts[c].read || []).includes(CASE.operation) && ck.contexts[c].children && Object.keys(ck.contexts[c].children).length) || null;

    const selectorsIn = (v, where) => selectors(found, v, where);
    // every string value anywhere: selectors
    const strings = (node, p) => {
      if (typeof node === 'string') { selectorsIn(node, vAt(p)); return; }
      if (Array.isArray(node)) { node.forEach((x, i) => strings(x, p + '[' + i + ']')); return; }
      if (isObject(node)) for (const [k, v] of Object.entries(node)) strings(v, childPath(p, k));
    };
    strings(body, '');

    const args = (node, p) => {
      if (Array.isArray(node)) { node.forEach((x, i) => args(x, p + '[' + i + ']')); return; }
      if (!isObject(node)) return;
      for (const [k, v] of Object.entries(node)) {
        const kp = childPath(p, k);
        found.add('argument', k, kAt(kp), { note: 'case' });
        const enumValue = (x, xp) => {
          if (typeof x === 'string' && enumsByArgument.has(k) && enumsByArgument.get(k).has(x)) {
            if (k === LAYOUT.subActionArgument) found.add('sub_action', x, vAt(xp));
            else found.add('enum_value', k + '=' + x, vAt(xp), { judge: x });
          }
        };
        if (Array.isArray(v)) v.forEach((x, i) => { enumValue(x, kp + '[' + i + ']'); if (typeof x === 'object') args(x, kp + '[' + i + ']'); });
        else if (isObject(v)) args(v, kp);
        else enumValue(v, kp);
      }
    };
    const step = (node, p) => {
      if (typeof node[CASE.operation] === 'string') found.add('operation', node[CASE.operation], vAt(childPath(p, CASE.operation)));
      if (node[CASE.args] !== undefined) args(node[CASE.args], childPath(p, CASE.args));
    };
    const walk = (node, ctxName, p) => {
      const ctx = ck.contexts[ctxName] || { read: [], documented: {}, children: {}, frozen_free: {} };
      for (const [k, v] of Object.entries(node)) {
        const kp = childPath(p, k);
        if (k.startsWith('_') || (ctx.frozen_free && Object.prototype.hasOwnProperty.call(ctx.frozen_free, k))) {
          found.add('free_case_key', k, kAt(kp), { note: k.startsWith('_') ? 'underscore note' : 'frozen free note' });
        } else {
          found.add('case_key', k, kAt(kp));
          inContext(k, ctxName);
        }
        if (k === CASE.family && typeof v === 'string' && (ctxName === ck.roots.declared || ctxName === ck.roots.regression)) {
          found.add('case_family', v, vAt(kp), { note: v.includes(',') ? 'composite value' : '' });
        }
        if (k === CASE.name && typeof v === 'string' && nameLike(v) && (ctxName === ck.roots.declared || ctxName === ck.roots.regression)) {
          found.add('case_name', v, vAt(kp));
        }
        if (k === CASE.twin && typeof v === 'string' && nameLike(v)) found.add('case_name', v, vAt(kp));
        if (k === CASE.what && typeof v === 'string') {
          for (const seg of v.split('.')) {
            const s = seg.replace(/\[\d+\]/g, '');
            if (s) found.add('response_field', s, vAt(kp), { note: 'path the cases check' });
          }
          if (typeof node[CASE.equals] === 'string' && shortValue(node[CASE.equals])) {
            found.add('response_value', node[CASE.equals], vAt(childPath(p, CASE.equals)), { note: 'value the cases compare' });
          }
        }
      }
      if ((ctx.read || []).includes(CASE.operation)) step(node, p);
      for (const [childKey, childCtx] of Object.entries(ctx.children || {})) {
        const v = node[childKey];
        const kp = childPath(p, childKey);
        if (Array.isArray(v)) v.forEach((x, i) => { if (isObject(x)) walk(x, childCtx, kp + '[' + i + ']'); });
        else if (isObject(v)) walk(v, childCtx, kp);
      }
    };
    if (Array.isArray(body)) {
      // a plan for corri_piani.js: a list of steps
      body.forEach((x, i) => { if (isObject(x)) walk(x, planStep, '[' + i + ']'); });
    } else if (isObject(body)) {
      if (!rootContext || !ck.contexts[rootContext]) cannot(LAYOUT.caseKeys + ' has no root context for ' + f);
      walk(body, rootContext, '');
    }
  }
  for (const r of found.all.values()) {
    if (r.type === 'case_key' && contextOfKey.has(r.it) && r.notes.size === 0) r.notes.add('contexts: ' + [...contextOfKey.get(r.it)].sort().join(', '));
  }

  // ── the summary of the bench ────────────────────────────────────────────
  {
    const sources = LAYOUT.summary.filter(exists).map(f => ({ file: path.join(root, f), label: f }));
    const ledger = path.join(root, LAYOUT.ledger);
    if (fs.existsSync(ledger)) sources.push({ file: ledger, label: LAYOUT.ledger });
    // a ledger outside the tree is read, but never named in where: the map
    // names tracked files only
    for (const l of opts.ledgers || []) sources.push({ file: path.resolve(l), label: null });
    for (const s of sources) {
      const text = readText(s.file);
      JSON.parse(text); // a broken summary stops here, with its own message
      for (const it of detector.parseJsonItems(text)) if (it.type === 'key') found.add('summary_key', it.key, s.label ? s.label + ':' + it.line : null);
    }
  }

  // ── the answers: keys and values written by the library and the bridge ──
  for (const [f, o] of cs) {
    if (!LAYOUT.answerGroups.includes(o.group)) continue;
    for (const [line, key, form, member, schema, values] of o.writes) {
      if (schema) continue;
      found.add('response_field', key, f + ':' + line, { note: 'key the ' + o.group + ' writes into an answer', weight: 0 });
      for (const v of values) if (shortValue(v)) found.add('response_value', v, f + ':' + line, { note: 'value the ' + o.group + ' writes into an answer', weight: 0 });
    }
  }

  // ── selectors in the catalogue and in the literals of the sources ───────
  {
    const text = readText(path.join(root, LAYOUT.catalogue));
    for (const it of detector.parseJsonItems(text)) if (it.type === 'value') selectors(found, it.value, LAYOUT.catalogue + ':' + it.line);
    for (const [value, sites] of found.literals) if (value.includes('@')) for (const s of sites) selectors(found, value, s);
  }

  // ── paths: folders, files, case names ───────────────────────────────────
  {
    const folders = new Map(); // folder path -> files below
    for (const f of files) {
      const segs = f.split('/');
      for (let i = 1; i < segs.length; i++) {
        const dir = segs.slice(0, i).join('/');
        folders.set(dir, (folders.get(dir) || 0) + 1);
      }
    }
    // where names a folder with its trailing slash, so that it reads as a path
    const below = new Map(); // folder name -> files below every folder of that name
    for (const [dir, n] of [...folders].sort()) {
      const seg = dir.split('/').pop();
      found.add('folder', seg, dir + '/');
      below.set(seg, (below.get(seg) || 0) + n);
    }
    for (const [seg, n] of below) { const r = found.all.get('folder\t' + seg); if (r) r.notes.add(n + (n === 1 ? ' file below' : ' files below')); }
    for (const f of files) {
      const base = f.split('/').pop();
      const stem = base.replace(/^\.+/, '').replace(/\.[^.]+$/, '');
      if (caseRoot(f) && f.endsWith('.json')) {
        let plan = false;
        try { plan = Array.isArray(JSON.parse(readText(path.join(root, f)))); } catch (e) { /* the parse above already passed */ }
        found.add('case_name', stem, f, { note: plan ? 'plan' : (f.startsWith(LAYOUT.cases[0] + LAYOUT.regressionFolder + '/') ? 'regression case' : caseRoot(f) !== LAYOUT.cases[0] ? 'waiting case' : '') });
      } else {
        found.add('file', base, f, { judge: stem });
      }
    }
  }

  // ── projects and namespaces ─────────────────────────────────────────────
  for (const [f, o] of cs) for (const [line, name] of o.namespaces) found.add('project', name, f + ':' + line, { note: 'namespace' });
  for (const f of files.filter(x => x.endsWith('.csproj'))) {
    const text = readText(path.join(root, f));
    text.split('\n').forEach((l, i) => {
      const m = /<(RootNamespace|AssemblyName)>([^<]+)<\//.exec(l);
      if (m) found.add('project', m[2].trim(), f + ':' + (i + 1), { note: m[1] === 'RootNamespace' ? 'root namespace' : 'assembly name' });
    });
  }

  // ── identifiers ─────────────────────────────────────────────────────────
  {
    const kinds = new Map(); // name -> Set of "language: kind"
    const tokens = new Map(); // name -> occurrences
    const declare = (lang, file, list, idents) => {
      for (const [line, kind, name] of list) {
        found.add('identifier', name, file + ':' + line, { weight: 0 });
        if (!kinds.has(name)) kinds.set(name, new Set());
        kinds.get(name).add(lang + ': ' + kind);
      }
      for (const [name, n] of (idents instanceof Map ? idents : new Map(idents))) tokens.set(name, (tokens.get(name) || 0) + n);
    };
    for (const [f, o] of cs) declare('C#', f, o.decls, o.idents);
    for (const [f, o] of js) declare('JavaScript', f, o.decls, o.idents);
    for (const [f, o] of ps) declare('PowerShell', f, o.decls, o.idents);
    for (const r of found.all.values()) {
      if (r.type !== 'identifier') continue;
      r.count = tokens.get(r.it) || 0;
      if (kinds.has(r.it)) r.notes.add(summarizeKinds(kinds.get(r.it)));
    }
  }

  found.addLiteralSites(['tool', 'sub_action', 'operation', 'catalogue_family', 'case_family', 'argument',
    'catalogue_key', 'response_field', 'response_value', 'case_key', 'summary_key']);
  return found;
}

// "C#: local, parameter; JavaScript: variable"
function summarizeKinds(set) {
  const by = new Map();
  for (const s of [...set].sort()) {
    const [lang, kind] = s.split(': ');
    if (!by.has(lang)) by.set(lang, []);
    by.get(lang).push(kind);
  }
  return [...by].map(([lang, ks]) => lang + ': ' + ks.join(', ')).join('; ');
}

// Selectors: @kind(field=value, field~=value, n=1). The kind is a row, each
// field is a row, and a name-like value is a row field=value.
const SELECTOR = /@([A-Za-z_][A-Za-z0-9_]*)\(((?:[^()]|\([^()]*\))*)\)/g;
function selectors(found, text, where) {
  if (typeof text !== 'string' || !text.includes('@')) return;
  SELECTOR.lastIndex = 0;
  let m;
  while ((m = SELECTOR.exec(text)) !== null) {
    found.add('selector', '@' + m[1], where, { judge: m[1], note: 'selector kind' });
    for (const part of m[2].split(',')) {
      const c = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(~=|<=|>=|!=|=|<|>)\s*(.*?)\s*$/.exec(part);
      if (!c) continue;
      found.add('selector', c[1], where, { note: 'selector field' });
      if (nameLike(c[3])) found.add('selector', c[1] + '=' + c[3], where, { judge: c[3], note: 'selector value' });
    }
  }
}

// =============================================================================
//  The map file
// =============================================================================

const HEADER_COMMENT = [
  '# Name map of the translation (entry W3.1-bis.3). Written by Controlli/lingua/name-map-extract.js:',
  '# one row per Italian name per type. By hand: the en column, a note, and a row for a name the gate',
  '# does not see as Italian; count and where are refreshed on every run. The script explains the types.',
];

function readMap(file) {
  const rows = [];
  const problems = [];
  if (!fs.existsSync(file)) return { rows, problems, missing: true };
  const lines = readText(file).replace(/\r\n?/g, '\n').split('\n');
  let header = null;
  lines.forEach((l, i) => {
    if (!l.trim() || l.startsWith('#')) return;
    const cells = l.split('\t');
    if (!header) {
      header = cells;
      if (cells.join('\t') !== COLUMNS.join('\t')) problems.push('line ' + (i + 1) + ': the header must be ' + COLUMNS.join(' | '));
      return;
    }
    if (cells.length !== COLUMNS.length) { problems.push('line ' + (i + 1) + ': ' + cells.length + ' columns, not ' + COLUMNS.length); return; }
    const [type, it, en, count, where, note] = cells;
    rows.push({ line: i + 1, type, it, en, count: Number(count) || 0, where, note });
  });
  if (!header) problems.push('no header row');
  return { rows, problems, missing: false };
}

function formatMap(rows) {
  const order = new Map(TYPES.map((t, i) => [t, i]));
  const sorted = rows.slice().sort((a, b) => (order.get(a.type) - order.get(b.type)) || (a.it < b.it ? -1 : a.it > b.it ? 1 : 0));
  const clean = (s) => String(s === undefined || s === null ? '' : s).replace(/[\t\r\n]+/g, ' ');
  return HEADER_COMMENT.join('\n') + '\n' + COLUMNS.join('\t') + '\n' +
    sorted.map(r => [r.type, r.it, r.en, r.count, r.where, r.note].map(clean).join('\t')).join('\n') + '\n';
}

// The rows to write: the Italian names found, with the English names and
// notes of the map as it is; the rows of the map for names the gate does not
// see as Italian (added by hand), refreshed; and the rows of the map that
// the tree no longer has, with count 0.
function mergeRows(found, old) {
  const byKey = new Map(old.map(r => [r.type + '\t' + r.it, r]));
  const out = [];
  for (const [key, r] of found.all) {
    const o = byKey.get(key);
    if (!r.italian && !o) continue;
    out.push({ type: r.type, it: r.it, en: o ? o.en : '', count: r.count, where: r.where.join(' '),
      note: o && o.note ? o.note : [...r.notes].filter(Boolean).sort().join('; ') });
    byKey.delete(key);
  }
  for (const o of byKey.values()) out.push({ type: o.type, it: o.it, en: o.en, count: 0, where: o.where, note: o.note });
  return out;
}

// =============================================================================
//  The check
// =============================================================================

// The shape of an atom: one name without separators.
function atomShape(s) {
  const lead = /^_+/.exec(s);
  const body = lead ? s.slice(lead[0].length) : s;
  let shape;
  if (/^[a-z][a-z0-9]*$/.test(body)) shape = 'lower';
  else if (/^[a-z][a-z0-9]*(?:_[a-z0-9]+)+_?$/.test(body)) shape = 'snake';
  else if (/^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$/.test(body)) shape = 'kebab';
  else if (/^[a-z][a-z0-9]*(?:[A-Z][a-z0-9]*)+$/.test(body)) shape = 'camel';
  else if (/^[A-Z][A-Za-z0-9]*$/.test(body) && /[a-z]/.test(body)) shape = 'Pascal';
  else if (/^[A-Z][A-Za-z0-9]*(?:-[A-Z][A-Za-z0-9]*)+$/.test(body)) shape = 'Pascal-kebab';
  else if (/^[A-Z][A-Z0-9_]*$/.test(body)) shape = 'UPPER';
  else if (/^[a-z][a-z0-9]*(?: [a-z0-9]+)+$/.test(body)) shape = 'words';
  else if (/^[A-Z][a-z0-9]*(?: [A-Za-z0-9]+)+$/.test(body)) shape = 'Words';
  else shape = 'mixed';
  return { lead: lead ? lead[0] : '', shape };
}

// Shapes that may stand for each other, by type: a one-word name may become
// a compound of the same family (`piano` -> `plane`, `cartiglio` -> `title_block`,
// `ingombro` -> `boundingBox` for an identifier).
const SNAKE_TYPES = new Set(['tool', 'sub_action', 'operation', 'catalogue_family', 'case_family', 'argument', 'enum_value',
  'catalogue_key', 'response_field', 'response_value', 'case_key', 'free_case_key', 'summary_key', 'selector', 'case_name']);
function compatible(a, b, type) {
  if (a === b) return true;
  const pair = (x, y) => (a === x && b === y) || (a === y && b === x);
  if (pair('lower', 'snake') && (SNAKE_TYPES.has(type) || type === 'folder' || type === 'file')) return true;
  if (pair('lower', 'kebab') && (type === 'folder' || type === 'file')) return true;
  if (pair('lower', 'camel') && type === 'identifier') return true;
  if (pair('words', 'lower') || pair('words', 'snake')) return SNAKE_TYPES.has(type);
  if (pair('Words', 'Pascal')) return SNAKE_TYPES.has(type);
  return false;
}

// Split a name into atoms and the separators between them.
function atoms(s) { return s.split(/[=,./@()\s]+/).filter(Boolean); }
function skeleton(s) { return s.replace(/[^=,./@()]+/g, 'x'); }

function shapeProblems(row) {
  const { type, it, en } = row;
  const out = [];
  if (!/^[\x20-\x7e]+$/.test(en)) out.push('not ASCII, or has a control character');
  if (en !== en.trim()) out.push('leading or trailing space');
  if (type === 'free_case_key') {
    if (!/^_[A-Za-z0-9_]+$/.test(en)) out.push('a free note of a case is written with a leading underscore (W3.1-bis.4)');
    return out;
  }
  let a = it, b = en;
  if (type === 'file') {
    const ea = path.extname(it).toLowerCase(), eb = path.extname(en).toLowerCase();
    if (ea !== eb) out.push('the extension changed (' + (ea || 'none') + ' -> ' + (eb || 'none') + ')');
    a = it.slice(0, it.length - path.extname(it).length);
    b = en.slice(0, en.length - path.extname(en).length);
  }
  const words = type === 'response_value' || (type === 'enum_value' && /\s/.test(it));
  // a namespace, a folder or a file name may gain or lose dotted parts
  // (Riscontro.Disegno -> Oce.Solid.Agent.Core): each English part has
  // the shape of the Italian parts
  const dotted = type === 'project' || type === 'folder' || type === 'file';
  if (dotted) {
    const shapes = atoms(a).map(atomShape);
    for (const y of atoms(b)) {
      const sb = atomShape(y);
      if (!shapes.some(shapeA => shapeA.lead === sb.lead && compatible(shapeA.shape, sb.shape, type)))
        out.push('"' + y + '" is ' + sb.shape + ', the Italian parts are ' + [...new Set(shapes.map(s => s.shape))].join(', '));
    }
    const loose = (x) => skeleton(x).replace(/\./g, '').replace(/x+/g, 'x');
    if (loose(a) !== loose(b)) out.push('the separators differ (' + skeleton(a) + ' -> ' + skeleton(b) + ')');
    return out;
  }
  if (!words && skeleton(a) !== skeleton(b)) {
    out.push('the separators differ (' + skeleton(a) + ' -> ' + skeleton(b) + ')');
    return out;
  }
  const A = words ? [a] : atoms(a), B = words ? [b] : atoms(b);
  A.forEach((x, i) => {
    const y = B[i];
    if (y === undefined) return;
    const shapeA = atomShape(x), sb = atomShape(y);
    if (shapeA.lead !== sb.lead) out.push('"' + x + '" -> "' + y + '": the leading underscore must stay as it is');
    if (!compatible(shapeA.shape, sb.shape, type)) out.push('"' + x + '" is ' + shapeA.shape + ', "' + y + '" is ' + sb.shape);
    for (const [from, to] of CHANGED_SUFFIXES) {
      if (x.endsWith(from) && !y.endsWith(to)) out.push('"' + x + '": the suffix ' + from + ' becomes ' + to);
    }
    for (const suf of KEPT_SUFFIXES) {
      if (x.endsWith(suf) && !y.endsWith(suf)) out.push('"' + x + '": the unit suffix ' + suf + ' stays');
    }
  });
  return out;
}

function reservedProblems(en) {
  const out = [];
  for (const a of atoms(en)) if (RESERVED.has(a)) out.push('"' + a + '" is ' + RESERVED.get(a));
  return out;
}

// Returns { failures: { a: [], b: [], c: [], d: [], e: [], map: [] }, warnings: [] }.
function check(found, map) {
  const f = { map: map.problems.slice(), a: [], b: [], c: [], d: [], e: [], f: [] };
  const warnings = [];
  const byKey = new Map();
  for (const r of map.rows) {
    if (!TYPES.includes(r.type)) f.map.push('line ' + r.line + ': unknown type "' + r.type + '"');
    const key = r.type + '\t' + r.it;
    if (byKey.has(key)) f.map.push('line ' + r.line + ': ' + r.type + ' ' + r.it + ' is also on line ' + byKey.get(key).line);
    else byKey.set(key, r);
  }
  // a. every Italian name found has a row with an English name
  for (const [key, r] of found.rows) {
    const m = byKey.get(key);
    if (!m) f.a.push(r.type + ' ' + r.it + ': no row (' + (r.where[0] || '') + ')');
    else if (!m.en.trim()) f.a.push(r.type + ' ' + r.it + ': no English name (line ' + m.line + ')');
  }
  const withEn = map.rows.filter(r => r.en.trim() && TYPES.includes(r.type));
  // b. collisions inside a type
  const groups = new Map();
  for (const r of withEn) {
    const k = r.type + '\t' + (r.type === 'folder' || r.type === 'file' ? r.en.toLowerCase() : r.en);
    if (!groups.has(k)) groups.set(k, []);
    groups.get(k).push(r);
  }
  for (const g of groups.values()) {
    const its = new Set(g.map(r => r.it));
    if (its.size < 2) continue;
    if (g.every(r => /\bmerged\b/i.test(r.note))) continue;
    f.b.push(g[0].type + ' ' + g[0].en + ' <- ' + g.map(r => r.it + ' (line ' + r.line + ')').join(', ') + ': say "merged" in the note of each if it is on purpose');
  }
  // c. keywords; d. shape
  for (const r of withEn) {
    for (const p of reservedProblems(r.en)) f.c.push(r.type + ' ' + r.it + ' -> ' + r.en + ': ' + p + ' (line ' + r.line + ')');
    for (const p of shapeProblems(r)) f.d.push(r.type + ' ' + r.it + ' -> ' + r.en + ': ' + p + ' (line ' + r.line + ')');
  }
  // e. namespaces that extend one another
  const projects = withEn.filter(r => r.type === 'project');
  for (const p of projects) {
    for (const q of projects) {
      if (q === p || !q.it.startsWith(p.it + '.')) continue;
      if (!q.en.startsWith(p.en + '.')) f.e.push(q.it + ' -> ' + q.en + ' does not extend ' + p.it + ' -> ' + p.en);
    }
  }
  // f. an English name the gate would count as Italian
  const judge = typeof found.italian === 'function' ? (n) => found.italian(n) : () => false;
  for (const r of withEn) {
    if (r.en === r.it) warnings.push(r.type + ' ' + r.it + ': kept as it is, and the gate keeps counting it');
    else if (judge(r.en)) f.f.push(r.type + ' ' + r.it + ' -> ' + r.en + ': the gate judges the English name Italian (line ' + r.line + ')');
  }
  // warnings: an English name that the same role already uses in the tree
  for (const r of withEn) {
    if (found.english[r.type] && found.english[r.type].has(r.en)) warnings.push(r.type + ' ' + r.it + ' -> ' + r.en + ': "' + r.en + '" is already a ' + r.type + ' in the tree');
  }
  return { failures: f, warnings };
}

const CHECK_TITLES = {
  map: 'the map file',
  a: 'a. Italian names without a row or without an English name',
  b: 'b. English names shared inside a type',
  c: 'c. English names that are C# or JSON Schema keywords',
  d: 'd. English names without the shape of the Italian one',
  e: 'e. namespaces whose English name does not extend the English name of their parent',
  f: 'f. English names the gate would still count as Italian',
};

function printCheck(result, show) {
  let n = 0;
  for (const k of Object.keys(CHECK_TITLES)) {
    const list = result.failures[k];
    if (!list.length) { console.log('name map check ' + CHECK_TITLES[k] + ': none'); continue; }
    n += list.length;
    console.log('NAME MAP CHECK ' + CHECK_TITLES[k] + ': ' + list.length);
    for (const x of list.slice(0, show)) console.log('    ' + x);
    if (list.length > show) console.log('    ... and ' + (list.length - show) + ' more');
  }
  if (result.warnings.length) {
    console.log('name map warnings (not failures): ' + result.warnings.length);
    for (const x of result.warnings.slice(0, show)) console.log('    ' + x);
    if (result.warnings.length > show) console.log('    ... and ' + (result.warnings.length - show) + ' more');
  }
  return n;
}

// =============================================================================
//  Counts and coverage
// =============================================================================

function printCounts(found) {
  const per = Object.fromEntries(TYPES.map(t => [t, 0]));
  for (const r of found.rows.values()) per[r.type]++;
  console.log('name map: Italian names per type (skipped: covered by exceptions.json)');
  let total = 0;
  for (const t of TYPES) {
    total += per[t];
    const skipped = found.skipped[t].size;
    console.log('    ' + t.padEnd(18) + String(per[t]).padStart(6) + (skipped ? '   (skipped ' + skipped + ')' : ''));
  }
  console.log('    ' + 'total'.padEnd(18) + String(total).padStart(6));
}

// The names of the gate's set categories that no row covers: a name is
// covered when it is the Italian name of a row or one of its words.
function coverage(root, found, show, list) {
  const cfg = detector.loadConfig(root);
  const report = detector.scanTree(root, cfg, null, null);
  const words = new Set();
  for (const r of found.rows.values()) {
    words.add(r.it);
    (r.it.match(/[A-Za-z_\u00c0-\u00ff][A-Za-z0-9_\u00c0-\u00ff]*/g) || []).forEach(w => words.add(w));
    if (r.type === 'file') words.add(r.it.replace(/\.[^.]+$/, ''));
  }
  console.log('name map coverage of the language gate sets (names no row covers):');
  for (const c of detector.SET_CATEGORIES) {
    const set = report.sets[c];
    const missing = [...set.keys()].filter(e => !words.has(e)).sort();
    console.log('    ' + c.padEnd(12) + String(missing.length).padStart(6) + ' of ' + set.size);
    if (list) for (const e of missing.slice(0, show)) console.log('        ' + e + '   (' + set.get(e) + ')');
    if (list && missing.length > show) console.log('        ... and ' + (missing.length - show) + ' more');
  }
}

// =============================================================================
//  Self-test: every check seen red on a fake row, a good map seen green
// =============================================================================

function selfTest() {
  // The fixtures use names the gate already knows from the tree (a row of the
  // map is a name of the tree): the self-test adds no Italian name of its own.
  const keyOf = (type, it) => type + '\t' + it;
  let real = null;
  try { real = new Found(detector.loadConfig(ROOT_DEFAULT), []); } catch (e) { real = null; }
  const fakeFound = (names) => {
    const f = { rows: new Map(), english: {}, italian: (n) => (real ? real.italian(n) : false) };
    for (const t of TYPES) f.english[t] = new Set();
    for (const [type, it] of names) f.rows.set(keyOf(type, it), { type, it, count: 1, where: ['x.js:1'], notes: new Set() });
    f.english.case_key.add('name');
    return f;
  };
  const row = (type, it, en, note, line) => ({ type, it, en, note: note || '', line: line || 1, count: 1, where: '' });
  const good = [
    row('tool', 'guarda_il_pezzo', 'inspect_part'),
    row('argument', 'lunghezza_mm', 'length_mm'),
    row('argument', 'angolo_gradi', 'angle_deg'),
    row('identifier', 'Esito', 'Outcome'),
    row('identifier', 'scadenzaMs', 'deadlineMs'),
    row('identifier', 'ingombro', 'boundingBox'),
    row('identifier', '_registro', '_registry'),
    row('catalogue_key', 'sommario', 'summary'),
    row('enum_value', 'piano=frontale', 'plane=front'),
    row('selector', '@faccia', '@face'),
    row('file', 'Attese.cs', 'Expectations.cs'),
    row('folder', 'casi', 'cases'),
    row('free_case_key', '_firme', '_signatures'),
    row('response_value', 'alto', 'up'),
    row('case_family', 'verifica', 'checks', 'merged with the plural'),
    row('case_family', 'verifiche', 'checks', 'merged with the singular'),
    row('project', 'Riscontro.Disegno', 'Oce.Solid.Agent.Core'),
    row('project', 'Riscontro.Disegno.Catalogo', 'Oce.Solid.Agent.Core.Catalog'),
    row('case_key', 'nome', 'name'),
  ];
  const goodNames = good.map(r => [r.type, r.it]);
  const cases = [
    { title: 'a good map is green', rows: good, names: goodNames, expect: [] },
    { title: 'a name found without a row is red (a)', rows: good, names: goodNames.concat([['operation', 'estrudi_cerchio']]), expect: ['a'] },
    { title: 'a row without an English name is red (a)', rows: good.concat([row('operation', 'estrudi_cerchio', '')]), names: goodNames.concat([['operation', 'estrudi_cerchio']]), expect: ['a'] },
    { title: 'two Italian names on one English name are red (b)', rows: good.concat([row('tool', 'annulla', 'inspect_part')]), names: goodNames, expect: ['b'] },
    { title: '... unless every row says merged', rows: good.concat([row('tool', 'annulla', 'inspect_part', 'merged')]).map(r => r.it === 'guarda_il_pezzo' ? Object.assign({}, r, { note: 'merged' }) : r), names: goodNames, expect: [] },
    { title: 'two files that differ only in case are red (b)', rows: good.concat([row('file', 'archivio.cs', 'expectations.cs')]), names: goodNames, expect: ['b'] },
    { title: 'a C# keyword is red (c)', rows: good.concat([row('argument', 'archivio', 'object')]), names: goodNames, expect: ['c'] },
    { title: 'a contextual C# keyword is red (c)', rows: good.concat([row('case_key', 'uguale', 'equals')]), names: goodNames, expect: ['c'] },
    { title: 'a JSON Schema keyword is red (c)', rows: good.concat([row('selector', 'tipo', 'type')]), names: goodNames, expect: ['c'] },
    { title: 'a keyword inside an enum row is red (c)', rows: good.concat([row('enum_value', 'tipo=parte', 'type=part')]), names: goodNames, expect: ['c'] },
    { title: 'PascalCase for snake_case is red (d)', rows: good.concat([row('operation', 'estrudi_cerchio', 'ExtrudeCircle')]), names: goodNames, expect: ['d'] },
    { title: 'snake_case for PascalCase is red (d)', rows: good.concat([row('identifier', 'Sentinella', 'sentinel')]), names: goodNames, expect: ['d'] },
    { title: 'a lost unit suffix is red (d)', rows: good.concat([row('argument', 'larghezza_mm', 'width')]), names: goodNames, expect: ['d'] },
    { title: 'a degree suffix not turned into _deg is red (d)', rows: good.concat([row('argument', 'scarto_gradi', 'deviation_degrees')]), names: goodNames, expect: ['d'] },
    { title: 'a letter outside ASCII is red (d)', rows: good.concat([row('folder', 'archivi', 'arch' + String.fromCharCode(0xee) + 'ves')]), names: goodNames, expect: ['d'] },
    { title: 'a changed extension is red (d)', rows: good.concat([row('file', 'Sessione.cs', 'Session.js')]), names: goodNames, expect: ['d'] },
    { title: 'a lost leading underscore is red (d)', rows: good.concat([row('identifier', '_guinzaglio', 'leash')]), names: goodNames, expect: ['d'] },
    { title: 'a free note without an underscore is red (d)', rows: good.concat([row('free_case_key', '_guardato', 'seen')]), names: goodNames, expect: ['d'] },
    { title: 'changed separators in an enum row are red (d)', rows: good.concat([row('enum_value', 'verso=alto', 'normal_up')]), names: goodNames, expect: ['d'] },
    { title: 'a namespace that does not extend its parent is red (e)', rows: good.concat([row('project', 'Riscontro.Disegno.Api', 'Oce.Solid.Agent.Api')]), names: goodNames, expect: ['e'] },
    { title: 'an unknown type is red (map)', rows: good.concat([row('thing', 'cosa', 'thing')]), names: goodNames, expect: ['map'] },
    { title: 'a row twice is red (map)', rows: good.concat([row('folder', 'casi', 'cases', '', 2)]), names: goodNames, expect: ['map'] },
    { title: 'an English name the gate judges Italian is red (f)', rows: good.concat([row('argument', 'larghezza_mm', 'ingombro_mm')]), names: goodNames, expect: ['f'] },
    { title: 'an English name equal to the Italian one is no failure', rows: good.concat([row('argument', 'larghezza_mm', 'larghezza_mm')]), names: goodNames, expect: [] },
  ];
  let ok = 0;
  for (const c of cases) {
    const result = check(fakeFound(c.names), { rows: c.rows.map((r, i) => Object.assign({}, r, { line: r.line > 1 ? r.line : i + 2 })), problems: [] });
    const red = Object.keys(result.failures).filter(k => result.failures[k].length);
    const as = red.length === c.expect.length && c.expect.every(k => red.includes(k));
    if (as) ok++;
    console.log((as ? 'ok      ' : 'NOT OK  ') + c.title + (as ? '' : '   (red: ' + (red.join(', ') || 'none') + '; expected: ' + (c.expect.join(', ') || 'none') + ')'));
    if (!as) for (const k of red) for (const x of result.failures[k].slice(0, 3)) console.log('            ' + x);
  }
  // the warning: an English name the role already has
  const w = check(fakeFound(goodNames), { rows: good.map((r, i) => Object.assign({}, r, { line: i + 2 })), problems: [] }).warnings;
  const warned = w.length === 1 && /already a case_key/.test(w[0]);
  if (warned) ok++;
  console.log((warned ? 'ok      ' : 'NOT OK  ') + 'an English name the role already uses is a warning');

  // merging: a row added by hand for a name the gate judges English is kept
  // and refreshed, a row the tree no longer has is kept with count 0, an
  // English name without a row gets none, en and note survive
  const fakeAll = { all: new Map([
    [keyOf('catalogue_family', 'quote'), { type: 'catalogue_family', it: 'quote', italian: false, count: 5, where: ['a.js:1'], notes: new Set() }],
    [keyOf('catalogue_family', 'lavorazioni'), { type: 'catalogue_family', it: 'lavorazioni', italian: true, count: 7, where: ['b.js:2'], notes: new Set(['n']) }],
    [keyOf('tool', 'api_search'), { type: 'tool', it: 'api_search', italian: false, count: 3, where: ['c.js:3'], notes: new Set() }],
  ]) };
  const merged = mergeRows(fakeAll, [
    { type: 'catalogue_family', it: 'quote', en: 'dimensions', count: 0, where: '', note: 'added by hand' },
    { type: 'catalogue_family', it: 'lavorazioni', en: 'features', count: 1, where: 'old.js:1', note: '' },
    { type: 'operation', it: 'accoppia', en: 'old_one', count: 4, where: 'x.js:9', note: 'gone' },
  ]);
  const get = (t, it) => merged.find(r => r.type === t && r.it === it) || {};
  const mergedOk = merged.length === 3 &&
    get('catalogue_family', 'quote').count === 5 && get('catalogue_family', 'quote').en === 'dimensions' && get('catalogue_family', 'quote').note === 'added by hand' &&
    get('catalogue_family', 'lavorazioni').en === 'features' && get('catalogue_family', 'lavorazioni').where === 'b.js:2' && get('catalogue_family', 'lavorazioni').note === 'n' &&
    get('operation', 'accoppia').count === 0 && get('operation', 'accoppia').en === 'old_one' && get('operation', 'accoppia').where === 'x.js:9';
  if (mergedOk) ok++;
  console.log((mergedOk ? 'ok      ' : 'NOT OK  ') + 'merging keeps en and note, refreshes a row added by hand, keeps a row gone from the tree, adds no English name');

  // the decision on Italian, with the lexicon and exceptions of this tree
  let decided = false;
  try {
    const f = new Found(detector.loadConfig(ROOT_DEFAULT), loadExceptions(ROOT_DEFAULT));
    const want = [['guarda_il_pezzo', true], ['schema_di_quote', true], ['daSelezionare', true],
      ['offset_mm', false], ['on_a_machine_with_mbd', false], ['api_search', false]];
    decided = want.every(([n, v]) => f.italian(n) === v) &&
      exceptedIn(f.excs, 'Schizzo1', 'Banco/casi/x.json') && !exceptedIn(f.excs, 'guarda_il_pezzo', 'Banco/casi/x.json');
  } catch (e) { decided = false; }
  if (decided) ok++;
  console.log((decided ? 'ok      ' : 'NOT OK  ') + 'Italian is decided by the gate (identifier rule, prose rule on a phrase) and a localized SOLIDWORKS name is skipped');
  const total = cases.length + 3;
  console.log(ok + ' of ' + total + ' cases as expected');
  return ok === total ? 0 : 1;
}

// =============================================================================
//  Command line
// =============================================================================

function parseArgs(argv) {
  const a = { root: null, map: null, check: false, counts: false, selfTest: false, coverage: false, ledgers: [], show: 40 };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    if (x === '--root') a.root = argv[++i];
    else if (x === '--map') a.map = argv[++i];
    else if (x === '--check') a.check = true;
    else if (x === '--counts') a.counts = true;
    else if (x === '--self-test') a.selfTest = true;
    else if (x === '--coverage') a.coverage = true;
    else if (x === '--ledger') a.ledgers.push(argv[++i]);
    else if (x === '--show') a.show = parseInt(argv[++i], 10) || 40;
    else if (x === '--help' || x === '-h') a.help = true;
    else a.unknown = x;
  }
  return a;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    const lines = fs.readFileSync(__filename, 'utf8').split('\n');
    const end = lines.findIndex((l, i) => i > 3 && l.startsWith('// ====='));
    console.log(lines.slice(1, end + 1).join('\n'));
    return 0;
  }
  if (args.unknown) { console.log('name-map-extract: unknown argument ' + args.unknown); return 2; }
  if (args.selfTest) return selfTest();
  const root = path.resolve(args.root || ROOT_DEFAULT);
  const mapFile = path.resolve(root, args.map || MAP_REL);

  const found = extract(root, { ledgers: args.ledgers });
  printCounts(found);
  if (args.coverage) coverage(root, found, args.show, true);

  if (args.check) {
    const map = readMap(mapFile);
    if (map.missing) { console.log('NAME MAP CHECK: no map at ' + mapFile); return 1; }
    const n = printCheck(check(found, map), args.show);
    if (n) { console.log('NAME MAP CHECK: FAILED (' + n + ' failures).'); return 1; }
    console.log('name map check: passed (' + map.rows.length + ' rows).');
    return 0;
  }
  if (args.counts) return 0;

  const old = readMap(mapFile);
  if (old.problems.length && !old.missing) {
    console.log('name-map-extract: the map ' + mapFile + ' cannot be merged:');
    old.problems.forEach(p => console.log('    ' + p));
    return 2;
  }
  const rows = mergeRows(found, old.rows);
  fs.writeFileSync(mapFile, formatMap(rows), 'utf8');
  const kept = rows.filter(r => !found.all.has(r.type + '\t' + r.it)).length;
  const byHand = rows.filter(r => found.all.has(r.type + '\t' + r.it) && !found.all.get(r.type + '\t' + r.it).italian).length;
  console.log('name map written to ' + path.relative(root, mapFile).replace(/\\/g, '/') + ': ' + rows.length + ' rows' +
    (kept ? ' (' + kept + ' no longer found, kept)' : '') +
    (byHand ? ' (' + byHand + ' added by hand for names the gate does not see as Italian)' : '') + ', ' + rows.filter(r => r.en).length + ' with an English name.');
  return 0;
}

module.exports = { extract, check, readMap, formatMap, mergeRows, shapeProblems, reservedProblems, TYPES, COLUMNS };

if (require.main === module) {
  let code;
  try { code = main(); }
  catch (err) {
    if (err instanceof CannotRun || err instanceof tokenStream.GateError) { console.log('name-map-extract: cannot run: ' + err.message); code = 2; }
    else { console.log('name-map-extract: crashed: ' + (err.stack || err)); code = 2; }
  }
  process.exitCode = code;
}
