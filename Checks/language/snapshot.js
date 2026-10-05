#!/usr/bin/env node
// =============================================================================
//  snapshot.js - the picture every step of the translation is compared with
//  (entry W3.1-bis.2, item 11)
// =============================================================================
//
//  WHY IT EXISTS
//
//  The steps of W3.1-bis that come after the safety nets rename the case
//  keys (.4), the MCP surface (.5), the namespaces (.6), and translate the
//  comments (.7), the messages and the cases that assert on them (.8). Each
//  of them must prove that nothing ELSE moved: the same catalogue, the same
//  tools, the same refusals, every textual assertion of a case still
//  satisfied by a literal of the sources, the same outcome for every bench
//  test (by file:method:ordinal, never by the label, which is translated
//  too), the same number of expectations checked per case, and the same exit
//  code and counts from every check of Checks/ and .github/steps/. This
//  tool takes that picture once and compares any later tree with it.
//
//  Two reference bench runs, not one: a case whose outcome or number of
//  expectations differs between them is UNSTABLE (body numbers, a bench that
//  hung). The comparison lists the unstable cases apart, with their values,
//  and never counts them as a difference nor drops them in silence.
//
//  THE SIX SECTIONS
//
//    catalogue   Tools/catalog.json copied as it is, its sha256, the
//                sha256 of its normalized JSON (keys sorted), and one hash
//                per operation and per family, to say WHAT changed.
//    tools       the answers of the built bridge to initialize and
//                tools/list: every tool with its schema. Over stdio, as a
//                client talks to it. BUT started as a program the bridge
//                attaches to a running SOLIDWORKS at once (GetActiveObject),
//                so when SOLIDWORKS is running (tasklist says so, or cannot
//                say) the bridge is never started: its assembly is loaded by
//                a helper and the method that answers the protocol is called
//                directly (--tools-from reflection, the default in that
//                case). The source used is recorded and printed.
//    literals    every call of a refusal method (the CALLEES below) in the
//                sources of the library and of the bridge (the <Compile>
//                items of their two projects), named file:method:ordinal, with
//                the text of its arguments; and every string unit of those
//                sources (a literal, or a chain of + joined, the other
//                operands being holes), named file:member:sN. Read through
//                Roslyn (the copy inside MSBuild, compiled by token-stream.js),
//                so a comment is never a literal.
//    assertions  every textual assertion of the bench cases (the
//                ASSERTION_KEYS below: a must-fail message, contains, not
//                contains, a textual equals), with its case, path, step and
//                operation, and EVERY string unit of the sources whose
//                constant text contains it, ignoring case as the bench does.
//                An assertion with no candidate is listed.
//    tests       the identity, label, group and outcome of every test of
//                the two reference ledgers (Bench/report/tests.json of two
//                full bench runs), and per case the outcome and the
//                expectations attempted and checked in each; the unstable
//                cases and tests.
//    scripts     the exit code and the counts (every number of the output,
//                line by line, once paths, dates, durations, sizes, versions
//                and hashes are masked) of every script the registry
//                snapshot-scripts.json says to run. The registry names every
//                script of the folders it covers, run or skipped WITH the
//                reason: a script it does not name stops the tool.
//
//  The format of a snapshot directory is in snapshot.md, next to this file.
//
//  USAGE
//    node Checks/language/snapshot.js take --ledger <tests.json> --ledger <tests.json> [options]
//        writes a snapshot directory (default `note-interne/lingua/snapshots/<date>`,
//        a folder git ignores: the snapshot is not versioned, its format is)
//    node Checks/language/snapshot.js compare <snapshot dir> [--ledger <tests.json>] [options]
//        the current tree (and the ledger of a new run, if given) against it
//    node Checks/language/snapshot.js --self-test
//        a fake snapshot of a fake tree: every way of differing seen red,
//        the same tree seen green, an unstable case listed apart
//  Options:
//    --root <dir>          the tree (default: the repository of this file)
//    --out <dir>           take: where to write (must not exist, or be empty)
//    --todo <TODO.md>      the TODO the TODO probes read (default: $TODO_MD,
//                          then `<root>/note-interne/TODO.md`)
//    --no-todo             record the scripts that read the TODO as skipped
//    --bridge <exe>        the bridge (default <root>/bin/oce-solid-agent-mcp.exe;
//                          a .js file is a test double, spoken to over stdio)
//    --tools-from <how>    auto (default), stdio or reflection
//    --tools-timeout <s>   how long the bridge may take to answer over stdio
//                          (default 90; the reflection helper gets twice that)
//    --skip <section>      do not take, or do not compare, that section (said)
//    --skip-script <id>    record that registry entry as skipped by the caller
//    --with-probes         also run the probes the registry marks "probe"
//    --show <n>            differences printed per category (default 25)
//    --window <n>          compare: the snapshot was taken before rename window
//                          <n> of W3.1-bis; it is read through that window's
//                          renames (and the windows before it), so a renamed
//                          case, operation or field is the same one, not one
//                          gone and one new. 1: case names, folders and keys
//                          (Checks/language/window-1-cases.js); 2: also the
//                          operations, paths and values of the assertions, the
//                          catalogue and the tools (window-2-surface.js)
//
//  EXIT CODES
//    0  take: the snapshot is written; compare: no difference anywhere
//    1  compare: a difference outside the unstable list; self-test: a check
//       not as expected
//    2  cannot run: a missing or unreadable input, a script the registry
//       does not name, a source that does not parse, a bad argument
//    3  compare: no difference in what was compared, but a section was not
//       compared (skipped by the caller, absent from the snapshot, or no
//       ledger given for the tests)
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');
const crypto = require('crypto');
const tokenStream = require('./token-stream.js');

// 2 since 1/10/2026: every refusal call carries `inner`, the literals inside
// its arguments. A snapshot of another format is refused, never read half.
const FORMAT = 2;
const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const SECTIONS = ['catalogue', 'tools', 'literals', 'assertions', 'tests', 'scripts'];

// Where things are in a tree, relative to its root. The rename windows
// (W3.1-bis.4 to .6) change these paths together with the tree.
const LAYOUT = {
  catalogue: 'Tools/catalog.json',
  projects: ['Oce.Solid.Agent.Core/Oce.Solid.Agent.Core.csproj', 'Oce.Solid.Agent.csproj'],
  bridge: 'bin/oce-solid-agent-mcp.exe',
  caseKeys: 'Bench/case-keys.json',
  cases: 'Bench/cases',
  regressionFolder: 'regression',
  registry: path.relative(ROOT_DEFAULT, path.join(__dirname, 'snapshot-scripts.json')).replace(/\\/g, '/'),
  todo: 'note-interne/TODO.md',
  snapshots: 'note-interne/lingua/snapshots',
  bridgeVersion: 'VERSION.txt',
  libraryVersion: 'Oce.Solid.Agent.Core/Properties/AssemblyInfo.cs',
};

// The methods whose calls are the refusals of the library.
const CALLEES = ['Fallito'];

// The keys of the catalogue this tool reads (renamed in W3.1-bis.5).
const CATALOGUE_KEYS = { operations: 'operations', families: 'families', name: 'name' };

// The textual assertions, by context of Bench/case-keys.json (renamed in
// W3.1-bis.4 together with the cases): key -> kind. The kinds keep the names
// of the snapshot of 1/10/2026, so that a snapshot taken before the window
// and a tree after it name the same kind the same way.
const ASSERTION_KEYS = {
  must_fail: { message_includes: 'message_contains' },
  expectation: { includes: 'contains', does_not_include: 'not_contains', equal_to: 'equals' },
  regression_expectation: { includes: 'contains', does_not_include: 'not_contains', equal_to: 'equals' },
};
// Other keys of a case read for the record: the case name, the operation of a
// step, the measure of an expectation.
const CASE_KEYS = { name: 'name', operation: 'operation', what: 'what' };

const SCRIPT_EXTENSIONS = ['.js', '.cjs', '.mjs', '.ps1', '.psm1', '.sh', '.cmd', '.bat', '.py'];
const SKIP_DIRS = ['bin', 'obj', 'node_modules', '.git', '.vs'];

class CannotRun extends Error {}
function cannot(msg) { throw new CannotRun(msg); }

// ── small helpers ───────────────────────────────────────────────────────────
const sha256 = (data) => crypto.createHash('sha256').update(data).digest('hex');
const short = (h) => (h || '').slice(0, 12);

// JSON with the keys of every object sorted: two files that differ only in
// the order of their keys or in their spacing have the same normal form.
function stable(v) {
  if (Array.isArray(v)) return '[' + v.map(stable).join(',') + ']';
  if (v && typeof v === 'object') return '{' + Object.keys(v).sort().map(k => JSON.stringify(k) + ':' + stable(v[k])).join(',') + '}';
  return JSON.stringify(v === undefined ? null : v);
}

function relOf(root, abs) {
  const r = path.relative(root, abs);
  return (r.startsWith('..') || path.isAbsolute(r) ? abs : r).replace(/\\/g, '/');
}

function readText(file, what) {
  if (!fs.existsSync(file)) cannot(what + ' is missing: ' + file);
  return fs.readFileSync(file, 'utf8').replace(/^\ufeff/, '');
}

function readJson(file, what) {
  const text = readText(file, what);
  try { return JSON.parse(text); } catch (e) { cannot(what + ' is not valid JSON (' + file + '): ' + e.message); }
}

// With rowsKey, the elements of that one big list (or object) are written one
// per line: megabytes stay readable, and two snapshots compare with a plain
// diff as well as with this tool.
function writeJson(file, value, rowsKey) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  let text = JSON.stringify(value, null, 1) + '\n';
  const big = rowsKey ? value[rowsKey] : null;
  if (big && typeof big === 'object') {
    const head = Object.keys(value).filter(k => k !== rowsKey).map(k => ' ' + JSON.stringify(k) + ': ' + JSON.stringify(value[k]));
    const rows = Array.isArray(big) ? big.map(r => '  ' + JSON.stringify(r))
      : Object.keys(big).map(k => '  ' + JSON.stringify(k) + ': ' + JSON.stringify(big[k]));
    const open = Array.isArray(big) ? '[' : '{', close = Array.isArray(big) ? ']' : '}';
    text = '{\n' + head.concat([' ' + JSON.stringify(rowsKey) + ': ' + open + '\n' + rows.join(',\n') + '\n ' + close]).join(',\n') + '\n}\n';
  }
  fs.writeFileSync(file, text, 'utf8');
}

function isObject(v) { return v !== null && typeof v === 'object' && !Array.isArray(v); }

function clip(s, n) {
  s = String(s === undefined ? '' : s).replace(/\s+/g, ' ');
  return s.length > n ? s.slice(0, n - 3) + '...' : s;
}

function git(root, args) {
  const r = cp.spawnSync('git', ['-C', root].concat(args), { encoding: 'utf8', maxBuffer: 64 << 20, env: childEnv() });
  return r.status === 0 ? r.stdout : null;
}

function interopFolder() {
  const places = [];
  if (process.env.SW_INSTALL_PATH) places.push(path.join(process.env.SW_INSTALL_PATH.trim(), 'api', 'redist'));
  if (process.env.SW_REDIST) places.push(process.env.SW_REDIST.trim());
  places.push('C:\\Program Files\\SOLIDWORKS Corp\\SOLIDWORKS\\api\\redist');
  return places.find(p => fs.existsSync(path.join(p, 'SolidWorks.Interop.sldworks.dll'))) || '';
}

// true, false, or null when the process list cannot be read.
function solidworksRunning() {
  if (process.platform !== 'win32') return false;
  const r = cp.spawnSync('tasklist', ['/FI', 'IMAGENAME eq SLDWORKS.exe', '/FO', 'CSV', '/NH'], { encoding: 'utf8' });
  if (r.status !== 0 || r.error) return null;
  return /^"SLDWORKS\.exe"/im.test(r.stdout || '');
}

// The environment of a child: the catalogue variables of the bridge are not
// passed on, so that the bridge serves the catalogue next to it; and neither
// are the variables that point git elsewhere. Seen on 1/10/2026: with
// GIT_INDEX_FILE set, a script that builds a throw-away repository to test
// itself (expected-keys-test.js) wrote ITS index over the one named there,
// and every script after it read a tree of two files.
const GIT_POINTERS = ['GIT_INDEX_FILE', 'GIT_DIR', 'GIT_WORK_TREE', 'GIT_OBJECT_DIRECTORY', 'GIT_COMMON_DIR', 'GIT_PREFIX'];

function childEnv(extra) {
  const env = Object.assign({}, process.env, extra || {});
  delete env.RISCONTRO_CATALOGO;
  delete env.OPENPDM_CATALOGO;
  for (const k of GIT_POINTERS) delete env[k];
  return env;
}

// ── output: what is printed, and what is counted ────────────────────────────
class Report {
  constructor(show) {
    this.show = show;
    this.differences = {};
    this.notCompared = [];
    this.unstable = [];
    this.current = null;
    this.printed = 0;
  }

  section(title, key) {
    console.log('');
    console.log('== ' + title);
    this.current = key;
    this.printed = 0;
    if (!(key in this.differences)) this.differences[key] = 0;
  }

  line(text) { console.log('   ' + text); }

  // One difference. Only the first --show are printed per section; all count.
  diff(text, detail) {
    this.differences[this.current]++;
    if (this.printed < this.show) {
      console.log('   DIFF ' + text);
      for (const d of [].concat(detail || [])) console.log('        ' + d);
    }
    this.printed++;
  }

  endSection() {
    if (this.printed > this.show) console.log('   ... ' + (this.printed - this.show) + ' more differences in this section');
  }

  skip(key, why) {
    this.notCompared.push(key + ' (' + why + ')');
    console.log('');
    console.log('== ' + key + ': NOT COMPARED, ' + why);
  }

  total() { return Object.values(this.differences).reduce((a, b) => a + b, 0); }
}

// ── catalogue ───────────────────────────────────────────────────────────────
function catalogueOf(root) {
  const file = path.join(root, LAYOUT.catalogue);
  if (!fs.existsSync(file)) cannot('the catalogue is missing: ' + file);
  return catalogueRecord(fs.readFileSync(file));
}

// The record of a catalogue from its bytes (the tree's, or the snapshot's
// copy read through the renames of a window).
function catalogueRecord(raw) {
  let json;
  try { json = JSON.parse(raw.toString('utf8').replace(/^\ufeff/, '')); } catch (e) { cannot('the catalogue is not valid JSON: ' + e.message); }
  const K = CATALOGUE_KEYS;
  if (!isObject(json) || !Array.isArray(json[K.operations])) cannot('the catalogue has no array "' + K.operations + '": a renamed key is renamed here too (CATALOGUE_KEYS)');
  if (!isObject(json[K.families])) cannot('the catalogue has no object "' + K.families + '" (CATALOGUE_KEYS)');
  const operations = {};
  json[K.operations].forEach((op, i) => {
    if (!isObject(op) || typeof op[K.name] !== 'string' || !op[K.name]) cannot('operation ' + i + ' of the catalogue has no "' + K.name + '"');
    if (operations[op[K.name]]) cannot('two operations of the catalogue are named ' + op[K.name]);
    operations[op[K.name]] = sha256(stable(op));
  });
  const families = {};
  for (const [k, v] of Object.entries(json[K.families])) families[k] = sha256(stable(v));
  return {
    raw, json,
    record: {
      file: LAYOUT.catalogue, bytes: raw.length, sha256_raw: sha256(raw), sha256_normalized: sha256(stable(json)),
      top_keys: Object.keys(json).sort(), operations, families,
    },
  };
}

function compareCatalogue(snapDir, cur, rep, ren) {
  let old = readJson(path.join(snapDir, 'catalogue.json'), 'the catalogue section of the snapshot');
  let oldJson = readJson(path.join(snapDir, 'catalog.json'), 'the copy of the catalogue in the snapshot');
  if (ren && ren.catalogue) {
    // the snapshot's copy, rewritten as the window rewrote the catalogue
    const renamed = catalogueRecord(Buffer.from(ren.catalogue(readText(path.join(snapDir, 'catalog.json'), 'the copy of the catalogue in the snapshot')), 'utf8'));
    old = renamed.record;
    oldJson = renamed.json;
  }
  const now = cur.record;
  rep.section('catalogue (' + LAYOUT.catalogue + ')', 'catalogue');
  if (ren && ren.catalogue) rep.line('the catalogue of the snapshot is read through the renames of window ' + ren.window + ' (--window)');
  if (old.sha256_normalized === now.sha256_normalized) {
    rep.line('same: ' + Object.keys(now.operations).length + ' operations, ' + Object.keys(now.families).length +
             ' families, normalized sha256 ' + short(now.sha256_normalized) +
             (old.sha256_raw === now.sha256_raw ? '' : ' (the bytes differ: spacing or key order only)'));
    return;
  }
  if (stable(old.top_keys) !== stable(now.top_keys)) rep.diff('top-level keys: ' + old.top_keys.join(', ') + ' -> ' + now.top_keys.join(', '));
  const K = CATALOGUE_KEYS;
  const oldOps = new Map((Array.isArray(oldJson[K.operations]) ? oldJson[K.operations] : []).map(o => [o[K.name], o]));
  const curOps = new Map(cur.json[K.operations].map(o => [o[K.name], o]));
  for (const name of Object.keys(old.operations)) {
    if (!(name in now.operations)) { rep.diff('operation removed: ' + name); continue; }
    if (old.operations[name] === now.operations[name]) continue;
    const paths = [];
    jsonDiffPaths(oldOps.get(name), curOps.get(name), '', paths, 6);
    rep.diff('operation changed: ' + name, paths);
  }
  for (const name of Object.keys(now.operations)) if (!(name in old.operations)) rep.diff('operation added: ' + name);
  for (const f of Object.keys(old.families)) {
    if (!(f in now.families)) rep.diff('family removed: ' + f);
    else if (old.families[f] !== now.families[f]) rep.diff('family changed: ' + f);
  }
  for (const f of Object.keys(now.families)) if (!(f in old.families)) rep.diff('family added: ' + f);
  if (rep.differences.catalogue === 0) rep.diff('the normalized catalogue differs, outside operations and families');
  rep.endSection();
}

// Paths where two JSON values differ, at most `limit` of them.
function jsonDiffPaths(a, b, base, out, limit) {
  if (out.length >= limit || stable(a) === stable(b)) return;
  if (a && b && typeof a === 'object' && typeof b === 'object' && Array.isArray(a) === Array.isArray(b)) {
    const keys = Array.from(new Set(Object.keys(a).concat(Object.keys(b)))).sort();
    for (const k of keys) {
      const p = Array.isArray(a) ? base + '[' + k + ']' : (base ? base + '.' : '') + k;
      if (!(k in a)) out.push(p + ': added');
      else if (!(k in b)) out.push(p + ': removed');
      else jsonDiffPaths(a[k], b[k], p, out, limit);
      if (out.length >= limit) return;
    }
    return;
  }
  out.push((base || '(whole)') + ': ' + clip(JSON.stringify(a), 70) + ' -> ' + clip(JSON.stringify(b), 70));
}

// ── tools ───────────────────────────────────────────────────────────────────
const TOOLS_HELPER = String.raw`// The answers of the bridge to initialize and tools/list, read WITHOUT starting
// it (snapshot.js). Started as a program, the bridge attaches at once to a
// running SOLIDWORKS (GetActiveObject); here its assembly is loaded and the
// method that answers the protocol is called with the same objects the entry
// point would build, but the entry point never runs, so nothing attaches.
// Everything is found by SHAPE, not by name: the type of the entry point, the
// one static method that takes (string, Dictionary, A, B, C, object) and
// returns a Dictionary, and the three types A, B, C it asks for. A shape
// found twice, or not at all, stops the helper: it never guesses.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

static class SnapshotTools
{
    // args: [0] the Roslyn folder (the convention of the compiled helpers,
    // unused here), [1] the bridge executable, [2] the folder of the
    // SOLIDWORKS interop assemblies ("" when none), [3] the output file.
    static int Main(string[] args)
    {
        var exe = Path.GetFullPath(args[1]);
        var interop = args[2];
        var folder = Path.GetDirectoryName(exe);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            foreach (var dir in new[] { folder, interop })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var p = Path.Combine(dir, name + ".dll");
                // The bridge folder is a local copy (byReflection), so LoadFrom
                // is allowed and the assemblies keep their location.
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };
        try { return Run(exe, args[3]); }
        catch (Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            Console.Error.WriteLine("snapshot tools helper: " + ex);
            return 2;
        }
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine("snapshot tools helper: " + message);
        return 2;
    }

    static bool IsDispatcher(MethodInfo m)
    {
        if (m.ReturnType != typeof(Dictionary<string, object>)) return false;
        var p = m.GetParameters();
        return p.Length == 6 && p[0].ParameterType == typeof(string) &&
               p[1].ParameterType == typeof(Dictionary<string, object>) &&
               p[2].ParameterType.IsClass && p[3].ParameterType.IsClass && p[4].ParameterType.IsClass &&
               p[5].ParameterType == typeof(object);
    }

    static int Run(string exe, string outFile)
    {
        // A local copy of the bridge folder (byReflection): LoadFrom refuses a
        // network drive, and an assembly loaded from its bytes has no location
        // for the bridge to find its catalogue next to it.
        var asm = Assembly.LoadFrom(exe);
        var entry = asm.EntryPoint;
        if (entry == null) return Fail("the bridge has no entry point: " + exe);
        var program = entry.DeclaringType;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var found = program.GetMethods(statics).Where(IsDispatcher).ToList();
        if (found.Count != 1)
            return Fail("the type of the entry point (" + program.FullName + ") should have exactly one method that answers " +
                        "the protocol, (string, Dictionary, A, B, C, object) -> Dictionary; found " + found.Count +
                        (found.Count > 0 ? ": " + string.Join(", ", found.Select(m => m.Name)) : ""));
        var dispatch = found[0];
        var ps = dispatch.GetParameters();
        Type toolsType = ps[2].ParameterType, sessionType = ps[3].ParameterType, catalogueType = ps[4].ParameterType;

        object session = Create(sessionType);
        object catalogue = Create(catalogueType);
        if (session == null || catalogue == null)
            return Fail("no parameterless constructor on " + (session == null ? sessionType : catalogueType).FullName);
        object tools = CreateTools(toolsType, sessionType, catalogueType, session, catalogue);
        if (tools == null)
            return Fail("no constructor of " + toolsType.FullName + " takes (" + sessionType.Name + ", " + catalogueType.Name +
                        "[, a registry built from a null path])");

        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
        var initParams = json.Deserialize<Dictionary<string, object>>(
            "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"snapshot\",\"version\":\"1\"}}");
        var init = dispatch.Invoke(null, new object[] { "initialize", initParams, tools, session, catalogue, 1 });
        var list = dispatch.Invoke(null, new object[] { "tools/list", new Dictionary<string, object>(), tools, session, catalogue, 2 });

        var result = new Dictionary<string, object>
        {
            { "initialize", init },
            { "tools_list", list },
            { "dispatcher", program.FullName + "." + dispatch.Name },
            { "catalogue_object", Readable(catalogue) },
        };
        File.WriteAllText(outFile, json.Serialize(result), new UTF8Encoding(false));
        return 0;
    }

    static object Create(Type t)
    {
        var c = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        return c == null ? null : c.Invoke(new object[0]);
    }

    // The constructor with a registry of questions built from a null path
    // (one that writes nothing) when there is one, as the contracts build
    // it; otherwise the one with the session and the catalogue only.
    static object CreateTools(Type toolsType, Type sessionType, Type catalogueType, object session, object catalogue)
    {
        var ctors = toolsType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var c in ctors)
        {
            var p = c.GetParameters();
            if (p.Length != 3 || p[0].ParameterType != sessionType || p[1].ParameterType != catalogueType) continue;
            var silent = p[2].ParameterType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                                            null, new[] { typeof(string) }, null);
            if (silent == null) continue;
            return c.Invoke(new[] { session, catalogue, silent.Invoke(new object[] { null }) });
        }
        foreach (var c in ctors)
        {
            var p = c.GetParameters();
            if (p.Length == 2 && p[0].ParameterType == sessionType && p[1].ParameterType == catalogueType)
                return c.Invoke(new[] { session, catalogue });
        }
        return null;
    }

    // The public string and number properties of an object, for the record
    // (where the catalogue was read from, and whether it was).
    static Dictionary<string, object> Readable(object o)
    {
        var d = new Dictionary<string, object>();
        foreach (var p in o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (p.PropertyType != typeof(string) && p.PropertyType != typeof(int)) continue;
            try { d[p.Name] = p.GetValue(o, null); } catch (Exception ex) { d[p.Name] = "(" + ex.GetType().Name + ")"; }
        }
        return d;
    }
}
`;

function byReflection(bridge, timeoutS) {
  const helper = tokenStream.compileRoslynProgram(TOOLS_HELPER, 'SnapshotTools', ['System.Web.Extensions.dll']);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'snapshot-tools-'));
  try {
    const out = path.join(dir, 'tools.json');
    // THE BRIDGE IS READ FROM A LOCAL COPY OF ITS FOLDER (1/10/2026). On the
    // network drive S: Assembly.LoadFrom refuses it, and loaded from its bytes it
    // has no location, so its catalogue (catalog.json next to the executable)
    // cannot be found. The files at the top of its folder are copied to a local
    // temporary folder, which is removed with the rest.
    const copy = path.join(dir, 'bridge');
    fs.mkdirSync(copy);
    for (const n of fs.readdirSync(path.dirname(bridge))) {
      const from = path.join(path.dirname(bridge), n);
      if (fs.statSync(from).isFile()) fs.copyFileSync(from, path.join(copy, n));
    }
    const localBridge = path.join(copy, path.basename(bridge));
    const r = cp.spawnSync(helper.exe, [helper.roslyn, localBridge, interopFolder(), out],
      { encoding: 'utf8', env: childEnv(), maxBuffer: 64 << 20, timeout: timeoutS * 1000, windowsHide: true });
    if (r.error && r.error.code === 'ETIMEDOUT') cannot('the reflection helper did not finish within ' + timeoutS + ' s (stopped)');
    if (r.status !== 0 || !fs.existsSync(out)) {
      cannot('the tools of the bridge cannot be read by reflection (exit ' + r.status + '):\n' + (r.stdout || '') + (r.stderr || ''));
    }
    const o = JSON.parse(fs.readFileSync(out, 'utf8'));
    return { initialize: o.initialize, tools_list: o.tools_list, stderr: r.stderr || '', detail: { dispatcher: o.dispatcher, catalogue_object: o.catalogue_object } };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

// initialize and tools/list over stdio, as a client asks them.
function overStdio(bridge, fake, timeoutS) {
  return new Promise((resolve, reject) => {
    const p = cp.spawn(fake ? process.execPath : bridge, fake ? [bridge] : [],
      { cwd: path.dirname(bridge), stdio: ['pipe', 'pipe', 'pipe'], env: childEnv(), windowsHide: true });
    let buffer = '';
    let stderr = '';
    let done = false;
    const arrived = new Map();
    const waiting = new Map();
    const finish = (err, value) => {
      if (done) return;
      done = true;
      clearTimeout(timer);
      if (err) { try { p.kill(); } catch (e) { /* already gone */ } reject(err); } else resolve(value);
    };
    const timer = setTimeout(() => finish(new CannotRun('the bridge did not answer over stdio within ' + timeoutS + ' s (stopped); stderr:\n' + stderr)), timeoutS * 1000);
    p.on('error', e => finish(new CannotRun('the bridge does not start: ' + e.message)));
    // a bridge that is gone before it reads: the write fails (EPIPE), and an
    // unhandled stream error would end this tool with exit 1, "a difference"
    p.stdin.on('error', e => finish(new CannotRun('the bridge does not read its input (' + e.code + '); stderr:\n' + stderr)));
    p.on('exit', code => { if (!done && waiting.size) finish(new CannotRun('the bridge ended (exit ' + code + ') before answering; stderr:\n' + stderr)); });
    p.stderr.on('data', d => { stderr += d.toString('utf8'); });
    p.stdout.on('data', d => {
      buffer += d.toString('utf8');
      let i;
      while ((i = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, i).trim();
        buffer = buffer.slice(i + 1);
        if (!line) continue;
        let o;
        try { o = JSON.parse(line); } catch (e) { continue; }
        // a notification, or a request of the bridge to the client (it has
        // a method, and an id of its own that may equal one of ours)
        if (o.id === undefined || o.id === null || o.method !== undefined) continue;
        const w = waiting.get(o.id);
        if (w) { waiting.delete(o.id); w(o); } else arrived.set(o.id, o);
      }
    });
    const ask = (id, method, params) => new Promise(res => {
      if (arrived.has(id)) { res(arrived.get(id)); return; }
      waiting.set(id, res);
      p.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    });
    (async () => {
      const init = await ask(1, 'initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'snapshot', version: '1' } });
      p.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
      const list = await ask(2, 'tools/list', {});
      p.stdin.end();
      const killer = setTimeout(() => { try { p.kill(); } catch (e) { /* gone */ } }, 10000);
      p.on('close', () => { clearTimeout(killer); finish(null, { initialize: init, tools_list: list, stderr }); });
    })().catch(e => finish(e));
  });
}

async function toolsOf(root, opts) {
  const bridge = opts.bridge ? path.resolve(opts.bridge) : path.join(root, LAYOUT.bridge);
  if (!fs.existsSync(bridge)) cannot('the bridge is missing: ' + bridge + ' (build it with .\\build.ps1 -BridgeOnly, or pass --bridge)');
  const fake = /\.js$/i.test(bridge);
  let source = opts.toolsFrom;
  let why;
  if (fake) {
    if (source === 'reflection') cannot('a .js bridge is a test double: it has no assembly to reflect on');
    source = 'stdio';
    why = 'a .js bridge is a test double, spoken to over stdio';
  } else {
    const sw = solidworksRunning();
    if (source === 'auto') {
      source = sw === false ? 'stdio' : 'reflection';
      why = sw === false ? 'SOLIDWORKS is not running'
        : sw ? 'SOLIDWORKS is running: started as a program, the bridge would attach to it'
          : 'cannot tell whether SOLIDWORKS is running (tasklist failed): the bridge is not started';
    } else if (source === 'stdio' && sw !== false) {
      cannot('SOLIDWORKS is running (or tasklist cannot tell): started as a program, the bridge attaches to it at once. ' +
             'Read the tools with --tools-from reflection, or when SOLIDWORKS is closed.');
    } else {
      why = 'asked with --tools-from ' + source;
    }
  }
  console.log('tools: from ' + source + ' (' + why + ')');
  const answers = source === 'stdio' ? await overStdio(bridge, fake, opts.toolsTimeout) : byReflection(bridge, opts.toolsTimeout * 2);
  const init = answers.initialize;
  const list = answers.tools_list;
  if (!isObject(init) || !isObject(init.result)) cannot('the bridge did not answer initialize with a result: ' + clip(JSON.stringify(init), 300));
  if (!isObject(list) || !isObject(list.result) || !Array.isArray(list.result.tools)) cannot('the bridge did not answer tools/list with result.tools: ' + clip(JSON.stringify(list), 300));
  const tools = list.result.tools;
  const names = new Set();
  tools.forEach((t, i) => {
    if (!isObject(t) || typeof t.name !== 'string' || !t.name) cannot('tool ' + i + ' of tools/list has no name');
    if (names.has(t.name)) cannot('two tools are named ' + t.name);
    names.add(t.name);
  });
  const serverInfo = isObject(init.result.serverInfo) ? init.result.serverInfo : {};
  const initialize = Object.assign({}, init.result);
  delete initialize.serverInfo;
  return {
    source, why,
    bridge: relOf(root, bridge),
    bridge_sha256: sha256(fs.readFileSync(bridge)),
    server: { name: serverInfo.name === undefined ? null : serverInfo.name, version: serverInfo.version === undefined ? null : serverInfo.version },
    detail: answers.detail || null,
    initialize,
    initialize_sha256: sha256(stable(initialize)),
    tools,
    tools_sha256: sha256(stable(tools)),
    stderr: answers.stderr,
  };
}

function compareTools(snapDir, cur, rep, ren) {
  const old = readJson(path.join(snapDir, 'tools.json'), 'the tools section of the snapshot');
  if (ren && ren.tools) {
    // the tools of the snapshot, renamed as the window renamed Tools/McpTools.cs
    old.tools = ren.tools(old.tools);
    old.tools_sha256 = sha256(stable(old.tools));
  }
  rep.section('tools (initialize and tools/list of the bridge; snapshot from ' + old.source + ', now from ' + cur.source + ')', 'tools');
  if (ren && ren.tools) rep.line('the tools of the snapshot are read through the renames of window ' + ren.window + ' (--window)');
  if (old.server.version !== cur.server.version) rep.line('bridge version ' + old.server.version + ' -> ' + cur.server.version + ' (recorded, not a difference)');
  if (old.server.name !== cur.server.name) rep.diff('server name ' + old.server.name + ' -> ' + cur.server.name);
  if (old.initialize_sha256 !== cur.initialize_sha256) {
    const paths = [];
    jsonDiffPaths(old.initialize, cur.initialize, '', paths, 8);
    rep.diff('the answer to initialize changed (server version aside)', paths);
  }
  if (old.tools_sha256 === cur.tools_sha256) {
    rep.line('same ' + cur.tools.length + ' tools: ' + cur.tools.map(t => t.name).join(', ') + ' (normalized sha256 ' + short(cur.tools_sha256) + ')');
  } else {
    const oldBy = new Map(old.tools.map(t => [t.name, t]));
    const curBy = new Map(cur.tools.map(t => [t.name, t]));
    for (const [name, t] of oldBy) {
      if (!curBy.has(name)) { rep.diff('tool removed: ' + name); continue; }
      if (stable(t) === stable(curBy.get(name))) continue;
      const paths = [];
      jsonDiffPaths(t, curBy.get(name), '', paths, 8);
      rep.diff('tool changed: ' + name, paths);
    }
    for (const name of curBy.keys()) if (!oldBy.has(name)) rep.diff('tool added: ' + name);
    const oldOrder = old.tools.map(t => t.name).join(','), curOrder = cur.tools.map(t => t.name).join(',');
    if (rep.differences.tools === 0 && oldOrder !== curOrder) rep.diff('the order of the tools changed: ' + oldOrder + ' -> ' + curOrder);
  }
  rep.endSection();
}

// ── literals: the sources through Roslyn ────────────────────────────────────
const SCAN_HELPER = String.raw`// String literals and refusal calls of C# sources, through Roslyn (snapshot.js).
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

static class SnapshotScanEntry
{
    static string roslynDir;

    // Roslyn is loaded from the MSBuild folder, as token-stream.js does.
    static int Main(string[] args)
    {
        roslynDir = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var p = Path.Combine(roslynDir, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        return SnapshotScan.Run(args[1], args[2], args[3]);
    }
}

static class SnapshotScan
{
    public static int Run(string listFile, string outFile, string calleeList)
    {
        var callees = new HashSet<string>(calleeList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);
        var sb = new StringBuilder();
        foreach (var file in File.ReadAllLines(listFile, Encoding.UTF8))
        {
            if (file.Length == 0) continue;
            var tree = CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file)), options);
            var root = tree.GetRoot();
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
                             .Select(d => d.ToString()).Take(5).ToList();

            // A UNIT is one piece of constant text as the program builds it: a
            // literal alone, or a chain of + whose literals are joined, the
            // other operands being holes. Units are numbered per member, in
            // source order.
            var units = new List<string>();
            var unitSpans = new List<TextSpan>();
            var unitOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in root.DescendantNodes())
            {
                if (!IsUnitRoot(node)) continue;
                var parts = new List<string>();
                var holes = new List<bool>();
                Flatten((ExpressionSyntax)node, parts, holes);
                if (!holes.Contains(false)) continue;   // an addition of numbers
                var member = MemberOf(node);
                int n;
                unitOrdinals.TryGetValue(member, out n);
                n++;
                unitOrdinals[member] = n;
                unitSpans.Add(node.Span);
                units.Add("{\"member\":" + Json(member) + ",\"ordinal\":" + n.ToString(CultureInfo.InvariantCulture) +
                          ",\"line\":" + Line(node).ToString(CultureInfo.InvariantCulture) +
                          ",\"parts\":" + PartsJson(parts, holes) + "}");
            }

            // The calls whose name is one of the callees, numbered per member.
            var calls = new List<string>();
            var callOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = CalleeName(inv.Expression);
                if (name == null || !callees.Contains(name)) continue;
                var member = MemberOf(inv);
                int n;
                callOrdinals.TryGetValue(member, out n);
                n++;
                callOrdinals[member] = n;
                var args = new List<string>();
                foreach (var a in inv.ArgumentList.Arguments)
                {
                    var parts = new List<string>();
                    var holes = new List<bool>();
                    Flatten(a.Expression, parts, holes);
                    args.Add(PartsJson(parts, holes));
                }
                var inside = new List<string>();
                for (int i = 0; i < unitSpans.Count; i++)
                    if (inv.ArgumentList.Span.Contains(unitSpans[i])) inside.Add(i.ToString(CultureInfo.InvariantCulture));
                calls.Add("{\"member\":" + Json(member) + ",\"ordinal\":" + n.ToString(CultureInfo.InvariantCulture) +
                          ",\"line\":" + Line(inv).ToString(CultureInfo.InvariantCulture) + ",\"callee\":" + Json(name) +
                          ",\"args\":[" + string.Join(",", args) + "],\"units\":[" + string.Join(",", inside) + "]}");
            }

            sb.Append("{\"file\":").Append(Json(file)).Append(",\"errors\":[").Append(string.Join(",", errors.Select(Json)))
              .Append("],\"units\":[").Append(string.Join(",", units)).Append("],\"calls\":[").Append(string.Join(",", calls))
              .Append("]}\n");
        }
        File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        return 0;
    }

    static ExpressionSyntax StripParens(ExpressionSyntax e)
    {
        while (e is ParenthesizedExpressionSyntax) e = ((ParenthesizedExpressionSyntax)e).Expression;
        return e;
    }

    // An operand of a + chain (through parentheses) belongs to the unit of the chain.
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

    // Parts of constant text; holes[i] is true where parts[i] is a value
    // computed at run time. Adjacent texts are joined, adjacent holes too.
    static void Flatten(ExpressionSyntax e, List<string> parts, List<bool> holes)
    {
        e = StripParens(e);
        if (e.IsKind(SyntaxKind.AddExpression))
        {
            var b = (BinaryExpressionSyntax)e;
            Flatten(b.Left, parts, holes);
            Flatten(b.Right, parts, holes);
            return;
        }
        if (e.IsKind(SyntaxKind.StringLiteralExpression) || e.IsKind(SyntaxKind.CharacterLiteralExpression))
        {
            Text(((LiteralExpressionSyntax)e).Token.ValueText, parts, holes);
            return;
        }
        var interpolated = e as InterpolatedStringExpressionSyntax;
        if (interpolated != null)
        {
            foreach (var c in interpolated.Contents)
            {
                var t = c as InterpolatedStringTextSyntax;
                if (t != null) Text(t.TextToken.ValueText, parts, holes);
                else Hole(parts, holes);
            }
            return;
        }
        Hole(parts, holes);
    }

    static void Text(string s, List<string> parts, List<bool> holes)
    {
        if (holes.Count > 0 && !holes[holes.Count - 1]) { parts[parts.Count - 1] += s; return; }
        parts.Add(s);
        holes.Add(false);
    }

    static void Hole(List<string> parts, List<bool> holes)
    {
        if (holes.Count > 0 && holes[holes.Count - 1]) return;
        parts.Add(null);
        holes.Add(true);
    }

    static string PartsJson(List<string> parts, List<bool> holes)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(holes[i] ? "null" : Json(parts[i]));
        }
        return sb.Append(']').ToString();
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

    // The member a node belongs to, named as the compiler names it for
    // [CallerMemberName]: a lambda or a local function belongs to the member
    // that contains it, an accessor to its property, a constructor is .ctor.
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

    static int Line(SyntaxNode n) { return n.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }

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

// The <Compile> items of the library and the bridge, in project order.
function sourceFiles(root) {
  const files = [];
  const seen = new Set();
  for (const proj of LAYOUT.projects) {
    const abs = path.join(root, proj);
    const text = readText(abs, 'the project ' + proj);
    const re = /<Compile\s+Include="([^"]+)"/g;
    let m;
    let n = 0;
    while ((m = re.exec(text)) !== null) {
      if (m[1].includes('$(')) continue;   // written at build time (the version attributes)
      const f = path.resolve(path.dirname(abs), m[1].replace(/\\/g, path.sep));
      if (!fs.existsSync(f)) cannot(proj + ' compiles ' + m[1] + ', which is not there');
      n++;
      const rel = relOf(root, f);
      if (seen.has(rel)) continue;
      seen.add(rel);
      files.push({ abs: f, rel, project: proj });
    }
    if (n === 0) cannot(proj + ' compiles no file: a project read as empty would give no literal at all');
  }
  return files;
}

function renderParts(parts) { return parts.map(p => (p === null ? '{*}' : p)).join(''); }

function scanSources(root) {
  const files = sourceFiles(root);
  const helper = tokenStream.compileRoslynProgram(SCAN_HELPER, 'SnapshotScan');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'snapshot-scan-'));
  try {
    const list = path.join(dir, 'files.txt');
    const out = path.join(dir, 'scan.jsonl');
    fs.writeFileSync(list, files.map(f => f.abs).join('\n') + '\n', 'utf8');
    const r = cp.spawnSync(helper.exe, [helper.roslyn, list, out, CALLEES.join(',')], { encoding: 'utf8', maxBuffer: 64 << 20, windowsHide: true });
    if (r.status !== 0 || !fs.existsSync(out)) cannot('the source scan failed (exit ' + r.status + '):\n' + (r.stdout || '') + (r.stderr || ''));
    const byFile = new Map();
    for (const line of fs.readFileSync(out, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      const o = JSON.parse(line);
      if (o.errors.length) cannot(o.file + ' does not parse as C#:\n    ' + o.errors.join('\n    '));
      byFile.set(path.resolve(o.file), o);
    }
    const calls = [];
    const units = [];
    const sources = [];
    for (const f of files) {
      const o = byFile.get(path.resolve(f.abs));
      if (!o) cannot('the source scan gave nothing for ' + f.rel);
      sources.push({ file: f.rel, project: f.project, sha256: sha256(fs.readFileSync(f.abs)), units: o.units.length, calls: o.calls.length });
      const here = o.units.map(u => ({ id: f.rel + ':' + u.member + ':s' + u.ordinal, file: f.rel, member: u.member, line: u.line, parts: u.parts, call: null }));
      for (const c of o.calls) {
        const id = f.rel + ':' + c.member + ':' + c.ordinal;
        for (const i of c.units) here[i].call = id;
        // `inner` is the text of every string unit inside the arguments. The
        // text of an argument shows a value computed at run time as a hole, so
        // the literals of `cond ? "a" : "b"`, of `string.Format("...", x)` or
        // of a helper called inside the call are only here: comparing `text`
        // alone would let them be translated or deleted unseen.
        calls.push({ id, file: f.rel, member: c.member, ordinal: c.ordinal, line: c.line, callee: c.callee,
          text: c.args.map(renderParts).join(' | '), args: c.args, units: c.units.map(i => here[i].id),
          inner: c.units.map(i => renderParts(o.units[i].parts)) });
      }
      units.push(...here);
    }
    return { sources, calls, units };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

function compareLiterals(snapDir, cur, rep) {
  const old = readJson(path.join(snapDir, 'literals.json'), 'the literals section of the snapshot');
  rep.section('literals of ' + CALLEES.join('/') + '(...), by file:method:ordinal', 'literals');
  if (old.calls.some(c => !Array.isArray(c.inner))) cannot('literals.json has calls without "inner": a snapshot of an older format, take it again');
  const oldBy = new Map(old.calls.map(c => [c.id, c]));
  const curBy = new Map(cur.calls.map(c => [c.id, c]));
  let same = 0;
  for (const [id, c] of oldBy) {
    const n = curBy.get(id);
    if (!n) { rep.diff('vanished  ' + id + ' (line ' + c.line + ')', ['was ' + clip(c.text, 150)]); continue; }
    const innerSame = stable(n.inner) === stable(c.inner);
    if (n.text === c.text && innerSame) { same++; continue; }
    const detail = n.text === c.text ? [] : ['was ' + clip(c.text, 150), 'now ' + clip(n.text, 150)];
    if (!innerSame) {
      const gone = c.inner.filter(t => !n.inner.includes(t)), added = n.inner.filter(t => !c.inner.includes(t));
      detail.push('a literal inside the call ' + (gone.length || added.length ? 'changed:' : 'moved (order or count)'));
      for (const t of gone.slice(0, 3)) detail.push('  was «' + clip(t, 140) + '»');
      for (const t of added.slice(0, 3)) detail.push('  now «' + clip(t, 140) + '»');
    }
    rep.diff('changed   ' + id + ' (line ' + c.line + ' -> ' + n.line + ')', detail);
  }
  for (const [id, n] of curBy) if (!oldBy.has(id)) rep.diff('new       ' + id + ' (line ' + n.line + ')', ['now ' + clip(n.text, 150)]);
  rep.line(old.calls.length + ' in the snapshot, ' + cur.calls.length + ' now, ' + same + ' the same; ' +
           old.sources.length + ' -> ' + cur.sources.length + ' source files');
  rep.endSection();
}

// ── assertions ──────────────────────────────────────────────────────────────
function caseKeysOf(root) {
  const ck = readJson(path.join(root, LAYOUT.caseKeys), 'the case keys');
  if (!isObject(ck.roots) || !isObject(ck.contexts)) cannot(LAYOUT.caseKeys + ' has no "roots" or "contexts"');
  for (const [name, ctx] of Object.entries(ck.contexts)) {
    if (!Array.isArray(ctx.read) || !isObject(ctx.children)) cannot(LAYOUT.caseKeys + ': the context "' + name + '" has no "read" list or no "children" object');
  }
  for (const [ctx, keys] of Object.entries(ASSERTION_KEYS)) {
    const c = ck.contexts[ctx];
    if (!c) cannot(LAYOUT.caseKeys + ' has no context "' + ctx + '": ASSERTION_KEYS of snapshot.js follows the case keys, and is renamed with them');
    for (const k of Object.keys(keys)) {
      if (!c.read.includes(k)) cannot(LAYOUT.caseKeys + ' no longer reads "' + k + '" in "' + ctx + '": ASSERTION_KEYS of snapshot.js is renamed with the case keys');
    }
  }
  return ck;
}

function casesOf(root) {
  const folder = path.join(root, LAYOUT.cases);
  if (!fs.existsSync(folder)) cannot('the cases folder is missing: ' + folder);
  const cases = [];
  let plans = 0;
  for (const d of fs.readdirSync(folder).sort()) {
    const dir = path.join(folder, d);
    if (!fs.statSync(dir).isDirectory()) continue;
    for (const f of fs.readdirSync(dir).filter(x => x.endsWith('.json')).sort()) {
      const body = readJson(path.join(dir, f), 'the case ' + d + '/' + f);
      // an array is a plan for run_plans.js: the bench does not run it either
      if (Array.isArray(body)) { plans++; continue; }
      if (!isObject(body)) cannot('the case ' + d + '/' + f + ' is neither an object nor a plan');
      cases.push({ key: d + '/' + f.replace(/\.json$/, ''), folder: d, root: d === LAYOUT.regressionFolder ? 'regression' : 'declared', body });
    }
  }
  if (cases.length === 0) cannot('no case under ' + folder);
  return { cases, plans };
}

function assertionsOf(root) {
  const ck = caseKeysOf(root);
  const { cases, plans } = casesOf(root);
  const list = [];
  let notText = 0;
  const stepContexts = new Set(Object.keys(ck.contexts).filter(c => ck.contexts[c].read.includes(CASE_KEYS.operation)));
  for (const c of cases) {
    const rootContext = ck.roots[c.root];
    if (!rootContext || !ck.contexts[rootContext]) cannot(LAYOUT.caseKeys + ' has no root context for "' + c.root + '" cases');
    const name = typeof c.body[CASE_KEYS.name] === 'string' ? c.body[CASE_KEYS.name] : null;
    const walk = (node, ctx, at, where) => {
      const table = ASSERTION_KEYS[ctx];
      if (table) {
        for (const key of Object.keys(table)) {
          if (!Object.prototype.hasOwnProperty.call(node, key)) continue;
          const v = node[key];
          const values = Array.isArray(v) ? v.map((x, i) => [x, at + '.' + key + '[' + i + ']']) : [[v, at + '.' + key]];
          for (const [x, p] of values) {
            if (typeof x !== 'string') { notText++; continue; }
            const pathText = p.replace(/^\./, '');
            list.push({ id: c.key + '#' + pathText, case: c.key, case_name: name, root: c.root, path: pathText,
              step: where.step, operation: where.operation,
              what: typeof node[CASE_KEYS.what] === 'string' ? node[CASE_KEYS.what] : null,
              kind: table[key], key, value: x });
          }
        }
      }
      for (const [childKey, childCtx] of Object.entries(ck.contexts[ctx].children)) {
        if (!Object.prototype.hasOwnProperty.call(node, childKey) || node[childKey] === null) continue;
        if (!ck.contexts[childCtx]) cannot(LAYOUT.caseKeys + ': the child "' + childKey + '" of "' + ctx + '" leads to an unknown context "' + childCtx + '"');
        const v = node[childKey];
        const visit = (item, p) => {
          if (!isObject(item)) return;
          const w = stepContexts.has(childCtx)
            ? { step: p.replace(/^\./, ''), operation: typeof item[CASE_KEYS.operation] === 'string' ? item[CASE_KEYS.operation] : null }
            : where;
          walk(item, childCtx, p, w);
        };
        if (Array.isArray(v)) v.forEach((item, i) => visit(item, at + '.' + childKey + '[' + i + ']'));
        else visit(v, at + '.' + childKey);
      }
    };
    walk(c.body, rootContext, '', { step: null, operation: null });
  }
  return { list, cases: cases.length, plans, notText };
}

// One upper-cased text of every constant part of every unit, so that each
// assertion is one indexOf loop, not a loop over twenty thousand units. The
// bench compares ignoring case (OrdinalIgnoreCase); so does this.
function unitIndex(units) {
  const pieces = [];
  const owners = [];
  const starts = [];
  let pos = 0;
  units.forEach((u, ui) => {
    for (const part of u.parts) {
      if (part === null || part === '') continue;
      const up = part.toUpperCase();
      starts.push(pos);
      owners.push(ui);
      pieces.push(up);
      pos += up.length + 1;
    }
  });
  return { text: pieces.join('\u0001'), starts, owners, pieces };
}

// The units whose constant text contains the value: { candidates, equal },
// equal being those where a whole constant part is the value.
function candidatesFor(index, units, value) {
  const v = value.toUpperCase();
  const found = new Map();
  if (!v) return { candidates: [], equal: [] };
  for (let at = index.text.indexOf(v); at >= 0; at = index.text.indexOf(v, at + 1)) {
    let lo = 0, hi = index.starts.length - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (index.starts[mid] <= at) lo = mid; else hi = mid - 1; }
    const ui = index.owners[lo];
    const equal = at === index.starts[lo] && v.length === index.pieces[lo].length;
    found.set(ui, found.get(ui) || equal);
  }
  const ids = Array.from(found.keys());
  return { candidates: ids.map(ui => units[ui].id), equal: ids.filter(ui => found.get(ui)).map(ui => units[ui].id) };
}

// A message that carries a value computed at run time, "the hole is " + w +
// " mm wide", holds the assertion "the hole is 9 mm wide" in none of its
// constant parts, only in its TEMPLATE. Such an assertion is satisfied by the
// template when it starts inside a constant run, ends inside a later one, the
// runs between are whole, each hole stands for any text, and the constant
// text covers more than half of the assertion (so that a short assertion does
// not ride on holes: "(C2)" is not satisfied by "(" + x + ")"). A format
// placeholder ({0}, {1:F2}) inside a literal is a hole too. Only for the
// assertions no constant part satisfies.
const PLACEHOLDER = /\{\d+(?:,[^}]*)?(?::[^}]*)?\}/;

function templateRuns(parts) {
  const runs = [];
  let open = false;
  for (const p of parts) {
    if (p === null) { open = false; continue; }
    const pieces = p.toUpperCase().split(PLACEHOLDER);
    pieces.forEach((piece, k) => {
      if (k > 0) open = false;
      if (piece === '') return;
      if (open) runs[runs.length - 1] += piece; else runs.push(piece);
      open = true;
    });
  }
  return runs.length >= 2 ? runs : null;
}

function templateMatch(T, runs) {
  const rest = (pos, j, literal) => {
    if (j >= runs.length) return false;
    const r = runs[j];
    for (let q = pos; q < T.length; q++) {
      const tail = T.length - q;
      if (tail <= r.length && r.startsWith(T.slice(q)) && (literal + tail) * 2 > T.length) return true;
      if (q + r.length < T.length && T.startsWith(r, q) && rest(q + r.length, j + 1, literal + r.length)) return true;
    }
    return false;
  };
  for (let i = 0; i < runs.length - 1; i++) {
    const r = runs[i];
    for (let k = Math.min(r.length, T.length - 1); k >= 1; k--) {
      if (T.startsWith(r.slice(r.length - k)) && rest(k, i + 1, k)) return true;
    }
  }
  return false;
}

function assertionsSection(root, scan) {
  const a = assertionsOf(root);
  const index = unitIndex(scan.units);
  let templates = null;
  for (const x of a.list) {
    Object.assign(x, candidatesFor(index, scan.units, x.value));
    x.template = [];
    if (x.candidates.length || !x.value) continue;
    if (!templates) templates = scan.units.map(u => ({ id: u.id, runs: templateRuns(u.parts) })).filter(u => u.runs);
    const T = x.value.toUpperCase();
    for (const u of templates) if (templateMatch(T, u.runs)) x.template.push(u.id);
  }
  return a;
}

const satisfiers = (x) => (x.candidates.length ? x.candidates : x.template);
// `file:member:sN` -> `file:member`
const memberOfUnit = (id) => id.replace(/:s\d+$/, '');

// The identities of a snapshot taken before a rename window, read through
// the renames of that window (--window). Window 1 (W3.1-bis.4, the bench
// cases): the case folders, the case names and the case keys of the name
// map. Window 2 (W3.1-bis.5, the MCP surface) reads through window 1 first,
// then through its own renames: the operation of each assertion, its path
// and the value it compares, the catalogue (the snapshot's copy rewritten as
// the window rewrites Tools/catalog.json) and the list of tools. The
// script of each window does the reading, so that this comparison and the
// window cannot disagree on a name. A snapshot taken after window 1 reads the
// same way: window 1 on English names changes nothing.
function windowRenames(n, root, snapDir) {
  if (n === null) return null;
  if (n !== 1 && n !== 2) cannot('--window: only windows 1 (W3.1-bis.4, the bench cases) and 2 (W3.1-bis.5, the MCP surface) are known');
  const one = windowOneRenames(root);
  if (n === 1) return one;
  const catText = snapDir && fs.existsSync(path.join(snapDir, 'catalog.json')) ? readText(path.join(snapDir, 'catalog.json'), 'the copy of the catalogue in the snapshot') : null;
  const two = require('./window-2-surface.js').snapshotRenames(root, catText);
  return {
    window: n,
    caseKey: one.caseKey, name: one.name, folder: one.folder,
    testId: one.testId,
    catalogue: catText === null ? null : two.catalogue,
    tools: two.tools,
    assertion: (x0) => {
      const x = one.assertion(x0);
      return Object.assign({}, x, { operation: two.operation(x.operation), what: two.what(x.what, x.operation),
        value: two.value(x.key, x.what, x.value, x.operation) });
    },
  };
}

function windowOneRenames(root) {
  const n = 1;
  const w = require('./window-1-cases.js');
  const d = w.direction(w.loadMap(root), false);
  const text = w.textRewriter(d);
  const name = (s) => (typeof s === 'string' && d.name.has(s) ? d.name.get(s) : s);
  const folder = (s) => (d.caseFolders.includes(s) ? d.folder.get(s) : s);
  const key = (k) => (d.key.has(k) ? d.key.get(k) : k);
  const caseKey = (k) => { const i = k.indexOf('/'); return i < 0 ? k : folder(k.slice(0, i)) + '/' + name(k.slice(i + 1)); };
  const keyPath = (p) => (typeof p === 'string' ? p.split('.').map(s => s.replace(/^[^[]+/, key)).join('.') : p);
  return {
    window: n,
    caseKey, name, folder,
    testId: (id) => id.replace(/@([^#]+)/, (m, s) => '@' + name(s)),
    assertion: (x) => {
      const c = caseKey(x.case);
      const p = keyPath(x.path);
      return Object.assign({}, x, { id: c + '#' + p, case: c, case_name: name(x.case_name), path: p, step: keyPath(x.step),
        key: key(x.key), value: typeof x.value === 'string' ? text(x.value) : x.value });
    },
  };
}

function compareAssertions(snapDir, cur, rep, ren) {
  const old = readJson(path.join(snapDir, 'assertions.json'), 'the assertions section of the snapshot');
  if (ren) old.assertions = old.assertions.map(ren.assertion);
  const oldUnits = new Map(readJson(path.join(snapDir, 'strings.json'), 'the string units of the snapshot').units.map(u => [u.id, u]));
  rep.section('textual assertions of the cases, and the literals that satisfy them', 'assertions');
  if (ren) rep.line('the assertions of the snapshot are read through the renames of window ' + ren.window + ' (--window)');
  const oldBy = new Map(old.assertions.map(x => [x.id, x]));
  const curBy = new Map(cur.list.map(x => [x.id, x]));
  const textOf = (u) => clip(renderParts((oldUnits.get(u) || { parts: ['?'] }).parts), 90);
  let stillWithout = 0;
  for (const [id, x] of oldBy) {
    const n = curBy.get(id);
    if (!n) { rep.diff('assertion gone: ' + id + ' «' + clip(x.value, 80) + '»'); continue; }
    if (n.value !== x.value) rep.diff('assertion changed: ' + id, ['was «' + clip(x.value, 100) + '»', 'now «' + clip(n.value, 100) + '»']);
    const was = satisfiers(x), now = satisfiers(n);
    if (now.length === 0) {
      if (was.length === 0) { stillWithout++; continue; }
      rep.diff('without a satisfying literal now: ' + id + ' (' + x.kind + ' «' + clip(n.value, 80) + '»)',
        ['satisfied in the snapshot by ' + was.length + (x.candidates.length ? '' : ' template(s)') + ':']
          .concat(was.slice(0, 3).map(u => u + ' «' + textOf(u) + '»')));
      continue;
    }
    // EVERY member that satisfied it still does. "Some literal still
    // contains it" is not enough: the words a case asserts on the refusal of
    // the slot are also in three descriptions of the catalogue, and with the
    // refusal translated the descriptions would keep the assertion
    // "satisfied" while the bench goes red (seen on 1/10/2026). A unit is matched by its file and member, not by
    // its ordinal, so a literal added before it in the same member does not
    // count as a loss.
    const nowMembers = new Set(now.map(memberOfUnit));
    const lost = Array.from(new Set(was.map(memberOfUnit))).filter(m => !nowMembers.has(m));
    if (lost.length) {
      rep.diff('lost ' + lost.length + ' of the members whose literals satisfied it: ' + id + ' (' + x.kind + ' «' + clip(n.value, 80) + '»)',
        lost.slice(0, 3).map(m => 'no longer in ' + m + ': ' + was.filter(u => memberOfUnit(u) === m).slice(0, 2).map(u => '«' + textOf(u) + '»').join(', '))
          .concat(['still satisfied by ' + now.length + ': ' + now.slice(0, 3).join(', ')]));
    }
  }
  for (const [id, n] of curBy) {
    if (oldBy.has(id)) continue;
    if (satisfiers(n).length === 0) rep.diff('new assertion without a satisfying literal: ' + id + ' «' + clip(n.value, 80) + '»');
    else rep.line('new assertion, satisfied: ' + id + ' «' + clip(n.value, 80) + '»');
  }
  const without = cur.list.filter(x => satisfiers(x).length === 0).length;
  rep.line(old.assertions.length + ' in the snapshot, ' + cur.list.length + ' now; ' + without + ' without a satisfying literal now (' +
           stillWithout + ' of them already without one in the snapshot: listed in assertions.json, not a difference)');
  rep.endSection();
}

// ── tests: the ledgers of the bench ─────────────────────────────────────────
function readLedger(file) {
  const abs = path.resolve(file);
  if (!fs.existsSync(abs)) cannot('the ledger is missing: ' + abs);
  const raw = fs.readFileSync(abs);
  let L;
  try { L = JSON.parse(raw.toString('utf8').replace(/^\ufeff/, '')); } catch (e) { cannot('the ledger is not valid JSON (' + abs + '): ' + e.message); }
  if (!isObject(L) || L.format !== 1) cannot('the ledger ' + abs + ' is not of format 1 (Bench/TestLedger.cs)');
  if (!Array.isArray(L.tests) || !Array.isArray(L.cases)) cannot('the ledger ' + abs + ' has no "tests" or "cases" list');
  const tests = new Map();
  L.tests.forEach((t, i) => {
    if (!isObject(t) || typeof t.id !== 'string' || typeof t.outcome !== 'string') cannot('test ' + i + ' of ' + abs + ' has no "id" or "outcome"');
    if (tests.has(t.id)) cannot('the ledger ' + abs + ' has the test ' + t.id + ' twice');
    tests.set(t.id, { label: typeof t.label === 'string' ? t.label : null, group: t.group === undefined ? null : t.group,
      subject: t.subject === undefined ? null : t.subject, outcome: t.outcome });
  });
  const cases = new Map();
  L.cases.forEach((c, i) => {
    if (!isObject(c) || typeof c.name !== 'string' || typeof c.folder !== 'string' || typeof c.outcome !== 'string' ||
        typeof c.expectations_checked !== 'number' || typeof c.expectations_attempted !== 'number') {
      cannot('case ' + i + ' of ' + abs + ' lacks name, folder, outcome, expectations_attempted or expectations_checked');
    }
    const key = c.folder + '/' + c.name;
    if (cases.has(key)) cannot('the ledger ' + abs + ' has the case ' + key + ' twice');
    cases.set(key, { name: c.name, folder: c.folder, kind: c.kind === undefined ? null : c.kind, outcome: c.outcome,
      attempted: c.expectations_attempted, checked: c.expectations_checked });
  });
  return {
    meta: { file: abs, sha256: sha256(raw), mode: L.mode === undefined ? null : L.mode, library: L.library === undefined ? null : L.library,
      written: L.written === undefined ? null : L.written, identities_unresolved: L.identities_unresolved === undefined ? null : L.identities_unresolved,
      tests: tests.size, cases: cases.size },
    tests, cases,
  };
}

function testsSection(fileA, fileB) {
  const A = readLedger(fileA);
  const B = readLedger(fileB);
  for (const L of [A, B]) {
    if (L.meta.mode !== 'full') cannot('a reference ledger is a full bench run; ' + L.meta.file + ' is mode ' + L.meta.mode);
    if (L.meta.identities_unresolved !== 0) cannot(L.meta.file + ' has ' + L.meta.identities_unresolved + ' tests without an identity: they cannot be compared by file:method:ordinal');
  }
  const sameTwice = A.meta.sha256 === B.meta.sha256;
  const tests = {};
  const unstableTests = [];
  for (const id of new Set(Array.from(A.tests.keys()).concat(Array.from(B.tests.keys())))) {
    const a = A.tests.get(id), b = B.tests.get(id);
    const t = a || b;
    tests[id] = { label: t.label, group: t.group, subject: t.subject, outcome: [a ? a.outcome : null, b ? b.outcome : null] };
    if (!a || !b) unstableTests.push({ id, why: 'in one reference run only' });
    else if (a.outcome !== b.outcome) unstableTests.push({ id, why: 'outcome ' + a.outcome + ' / ' + b.outcome });
  }
  const cases = {};
  const unstableCases = [];
  for (const key of new Set(Array.from(A.cases.keys()).concat(Array.from(B.cases.keys())))) {
    const a = A.cases.get(key), b = B.cases.get(key);
    const c = a || b;
    cases[key] = { name: c.name, folder: c.folder, kind: c.kind,
      outcome: [a ? a.outcome : null, b ? b.outcome : null],
      attempted: [a ? a.attempted : null, b ? b.attempted : null],
      checked: [a ? a.checked : null, b ? b.checked : null] };
    const why = [];
    if (!a || !b) why.push('in one reference run only');
    else {
      if (a.outcome !== b.outcome) why.push('outcome ' + a.outcome + ' / ' + b.outcome);
      if (a.checked !== b.checked) why.push('expectations checked ' + a.checked + ' / ' + b.checked);
      if (a.attempted !== b.attempted) why.push('expectations attempted ' + a.attempted + ' / ' + b.attempted);
    }
    if (why.length) unstableCases.push({ case: key, name: c.name, why: why.join(', ') });
  }
  if (A.meta.library !== B.meta.library) console.log('tests: the two reference runs are of different libraries: ' + A.meta.library + ' and ' + B.meta.library);
  return {
    ledgers: [A.meta, B.meta],
    same_ledger_twice: sameTwice,
    reference: sameTwice
      ? 'ONE run given twice: the unstable list is empty by construction, not measured. Take the snapshot again with two runs.'
      : 'two runs',
    tests, cases, unstable_cases: unstableCases, unstable_tests: unstableTests,
  };
}

function compareTests(snapDir, ledgerFile, rep, ren) {
  const old = readJson(path.join(snapDir, 'tests.json'), 'the tests section of the snapshot');
  if (ren) {
    const tests = {};
    for (const [id, x] of Object.entries(old.tests)) tests[ren.testId(id)] = Object.assign({}, x, { subject: ren.name(x.subject) });
    const cases = {};
    for (const [k, x] of Object.entries(old.cases)) cases[ren.caseKey(k)] = Object.assign({}, x, { name: ren.name(x.name), folder: ren.folder(x.folder) });
    old.tests = tests;
    old.cases = cases;
    old.unstable_cases = old.unstable_cases.map(u => Object.assign({}, u, { case: ren.caseKey(u.case), name: ren.name(u.name) }));
    old.unstable_tests = old.unstable_tests.map(u => Object.assign({}, u, { id: ren.testId(u.id) }));
  }
  const N = readLedger(ledgerFile);
  rep.section('bench tests by file:method:ordinal, and expectations per case (ledger ' + N.meta.file + ', mode ' + N.meta.mode + ', library ' + N.meta.library + ')', 'tests');
  if (ren) rep.line('the tests and cases of the snapshot are read through the renames of window ' + ren.window + ' (--window)');
  if (old.same_ledger_twice) rep.line('NOTE: the snapshot was taken with one run given twice; its unstable list is empty by construction');
  // A run that is not full, or whose tests are not all named, is not the
  // run a step is judged by: a difference in itself, even when every test
  // it lacks happens to be missing from the comparison below as well.
  if (N.meta.mode !== 'full') rep.diff('the new run is not a full bench run (mode ' + N.meta.mode + '): every test and case it did not run is a difference below too');
  if (N.meta.identities_unresolved !== 0) rep.diff('the new run has ' + N.meta.identities_unresolved + ' test(s) without an identity (identities_unresolved): they cannot be matched by file:method:ordinal');
  const unstableCases = new Map(old.unstable_cases.map(u => [u.case, u]));
  const unstableNames = new Set(old.unstable_cases.map(u => u.name));
  const unstableTests = new Map(old.unstable_tests.map(u => [u.id, u]));
  const apart = (id, subject) => unstableTests.has(id) || (subject !== null && unstableNames.has(subject));
  let same = 0, labels = 0;
  for (const [id, t] of Object.entries(old.tests)) {
    const n = N.tests.get(id);
    if (apart(id, t.subject)) {
      rep.unstable.push('test ' + id + ': reference ' + t.outcome.join(' / ') + ', now ' + (n ? n.outcome : 'not run') +
                        (unstableTests.has(id) ? ' (' + unstableTests.get(id).why + ')' : ' (its case is unstable)'));
      continue;
    }
    if (!n) { rep.diff('test not in the new run: ' + id + ' «' + clip(t.label, 70) + '» (was ' + t.outcome[0] + ')'); continue; }
    if (n.outcome !== t.outcome[0]) { rep.diff('outcome ' + t.outcome[0] + ' -> ' + n.outcome + ': ' + id, ['label then «' + clip(t.label, 90) + '»', 'label now  «' + clip(n.label, 90) + '»']); continue; }
    same++;
    if (n.label !== t.label) labels++;
  }
  for (const [id, n] of N.tests) {
    if (id in old.tests) continue;
    if (apart(id, n.subject)) { rep.unstable.push('test ' + id + ': not in the reference runs, now ' + n.outcome + ' (its case is unstable)'); continue; }
    rep.diff('new test, not in the snapshot: ' + id + ' «' + clip(n.label, 70) + '» (' + n.outcome + ')');
  }
  let casesSame = 0;
  for (const [key, c] of Object.entries(old.cases)) {
    const n = N.cases.get(key);
    if (unstableCases.has(key)) {
      rep.unstable.push('case ' + key + ': reference outcome ' + c.outcome.join(' / ') + ', checked ' + c.checked.join(' / ') +
                        ', attempted ' + c.attempted.join(' / ') + '; now ' +
                        (n ? n.outcome + ', checked ' + n.checked + ', attempted ' + n.attempted : 'not run') + ' (' + unstableCases.get(key).why + ')');
      continue;
    }
    if (!n) { rep.diff('case not in the new run: ' + key); continue; }
    const why = [];
    if (n.outcome !== c.outcome[0]) why.push('outcome ' + c.outcome[0] + ' -> ' + n.outcome);
    if (n.checked !== c.checked[0]) why.push('expectations checked ' + c.checked[0] + ' -> ' + n.checked);
    if (n.attempted !== c.attempted[0]) why.push('expectations attempted ' + c.attempted[0] + ' -> ' + n.attempted);
    if (why.length) rep.diff('case ' + key + ': ' + why.join(', '));
    else casesSame++;
  }
  for (const key of N.cases.keys()) if (!(key in old.cases)) rep.diff('new case, not in the snapshot: ' + key);
  rep.line(Object.keys(old.tests).length + ' tests in the snapshot, ' + N.tests.size + ' in the new run, ' + same + ' with the same outcome' +
           (labels ? ' (' + labels + ' of them with another label: the label is not the identity)' : '') + '; ' +
           Object.keys(old.cases).length + ' cases in the snapshot, ' + N.cases.size + ' now, ' + casesSame + ' with the same outcome and expectations');
  rep.endSection();
}

// ── scripts ─────────────────────────────────────────────────────────────────
function entryId(e) { return e.script + (e.args && e.args.length ? ' ' + e.args.join(' ') : ''); }

function loadRegistry(root) {
  const file = path.join(root, LAYOUT.registry);
  const reg = readJson(file, 'the script registry');
  if (reg.format !== 1 || !Array.isArray(reg.folders) || !Array.isArray(reg.entries)) cannot(file + ': expected format 1 with "folders" and "entries"');
  const ids = new Set();
  reg.entries.forEach((e, i) => {
    const where = file + ', entry ' + i;
    if (!isObject(e) || typeof e.script !== 'string' || !e.script) cannot(where + ': no "script"');
    if ((e.run === true) === (typeof e.skip === 'string')) cannot(where + ' (' + e.script + '): exactly one of "run": true or "skip": "<reason>"');
    if (typeof e.skip === 'string' && e.skip.trim().length < 10) cannot(where + ' (' + e.script + '): a skip needs its reason');
    for (const k of ['args', 'needs', 'varies_with', 'volatile_lines']) if (e[k] !== undefined && !Array.isArray(e[k])) cannot(where + ': "' + k + '" is a list');
    for (const v of e.volatile_lines || []) {
      if (!isObject(v) || typeof v.pattern !== 'string' || typeof v.why !== 'string' || !v.why) cannot(where + ': a volatile line needs "pattern" and "why"');
      try { new RegExp(v.pattern); } catch (x) { cannot(where + ': bad pattern ' + v.pattern + ': ' + x.message); }
    }
    if (e.run && /\/$/.test(e.script) && typeof e.program !== 'string') cannot(where + ' (' + e.script + '): a program folder that runs names its "program"');
    const id = entryId(e);
    if (ids.has(id)) cannot(where + ': ' + id + ' is in the registry twice');
    ids.add(id);
    if (!fs.existsSync(path.join(root, e.script))) cannot(where + ': ' + e.script + ' does not exist (a stale entry)');
  });
  const named = new Set(reg.entries.map(e => e.script));
  const unnamed = scriptsIn(root, reg.folders).filter(s => !named.has(s));
  if (unnamed.length) {
    cannot('the registry ' + LAYOUT.registry + ' does not name ' + unnamed.length + ' script(s): ' + unnamed.join(', ') +
           '. Every script of ' + reg.folders.join(', ') + ' is run or skipped with its reason, never left out.');
  }
  return reg;
}

// Every script of the folders: a file with a script extension, a file with
// no extension that starts with #! (a git hook), and a folder that holds C#
// sources (a probe program), written with a trailing slash. Tracked files
// only, when the tree is a git work tree.
function scriptsIn(root, folders) {
  let files = null;
  const listed = git(root, ['ls-files', '-z', '--'].concat(folders));
  // The root of the work tree when git sees no prefix from it. Not a comparison
  // of paths: on a mapped network drive (S:) git answers --show-toplevel with
  // the UNC path of the share (//server/share/...), which never equals «S:\...» (1/10/2026).
  const prefix = git(root, ['rev-parse', '--show-prefix']);
  if (listed !== null && prefix !== null && prefix.trim() === '') {
    files = listed.split('\0').filter(Boolean);
  } else {
    // not a git work tree (the fake tree of the self-test): the files on disk
    if (fs.existsSync(path.join(root, '.git'))) cannot('git cannot list the tracked files of ' + root + ': the registry check would read untracked ones');
    files = [];
    const walk = (dir) => {
      for (const n of fs.readdirSync(path.join(root, dir))) {
        const r = dir + '/' + n;
        if (fs.statSync(path.join(root, r)).isDirectory()) { if (!SKIP_DIRS.includes(n)) walk(r); } else files.push(r);
      }
    };
    for (const f of folders) if (fs.existsSync(path.join(root, f))) walk(f);
  }
  const out = new Set();
  for (const f of files) {
    const ext = path.extname(f).toLowerCase();
    if (ext === '.cs') { out.add(f.slice(0, f.lastIndexOf('/') + 1)); continue; }
    if (SCRIPT_EXTENSIONS.includes(ext)) { out.add(f); continue; }
    if (ext === '' && fs.existsSync(path.join(root, f)) && fs.readFileSync(path.join(root, f), 'utf8').startsWith('#!')) out.add(f);
  }
  return Array.from(out).sort();
}

// The output as it is compared: line ends, paths, dates, times, durations,
// sizes, versions, hashes and line numbers of file references masked.
function normalizeOutput(text) {
  return String(text)
    .replace(/\u001b\[[0-9;]*m/g, '')
    .replace(/\r\n?/g, '\n')
    .replace(/Program Files \(x86\)/g, 'ProgramFilesX')
    .replace(/Program Files/g, 'ProgramFiles')
    .replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, '<guid>')
    .replace(/\b[\w.\-]+\.(cs|js|ps1|psm1|json|md|yml|yaml|csproj|txt|tsv|xml):\d+(-\d+)?/g, (m) => m.replace(/:\d+(-\d+)?$/, ':<line>'))
    .replace(/[A-Za-z]:[\\/][^\s"'<>|,;()\u00ab\u00bb]*/g, '<path>')
    .replace(/(^|[\s"'(])\/[a-z]\/[^\s"'<>|,;()]*/g, '$1<path>')
    .replace(/\b\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?Z?)?\b/g, '<date>')
    .replace(/\b\d{1,2}\/\d{1,2}\/\d{4}\b/g, '<date>')
    .replace(/\b\d{1,2}:\d{2}(:\d{2})?\b/g, '<time>')
    .replace(/\bv?\d+\.\d+\.\d+(\.\d+)?\b/g, '<version>')
    .replace(/\b\d+([.,]\d+)?\s?(ms|s|sec|secs|seconds|second|min|minutes|minute)\b/gi, '<duration>')
    .replace(/\b\d+([.,]\d+)?\s?(kB|KB|MB|GB|bytes|byte)\b/g, '<size>')
    .replace(/\b(?=[0-9a-f]*[0-9])(?=[0-9a-f]*[a-f])[0-9a-f]{7,64}\b/gi, '<hex>')
    .split('\n').map(l => l.replace(/\s+$/, '')).join('\n');
}

const NUMBER = /\d+(?:[.,]\d+)*/g;

// The first row is the number of non-empty lines of the output: a script that
// prints a list without numbers (the probes it built, the files it found)
// would otherwise have nothing to count, and a list that empties would pass.
const LINES_ROW = '(non-empty lines of the output)';

function countsOf(text, volatile) {
  const res = (volatile || []).map(v => new RegExp(v.pattern));
  const rows = [];
  let set = 0;
  let lines = 0;
  for (const line of normalizeOutput(text).split('\n')) {
    if (res.some(r => r.test(line))) { set++; continue; }
    if (line.trim()) lines++;
    const n = line.match(NUMBER);
    if (!n) continue;
    rows.push({ n, text: clip(line.replace(NUMBER, '#').trim(), 160) });
  }
  rows.unshift({ n: [String(lines)], text: LINES_ROW });
  return { rows, volatileLines: set };
}

const isZero = (s) => /^0+(?:[.,]0+)*$/.test(s);

function runEntry(root, e, ctx) {
  const id = entryId(e);
  const base = { id, script: e.script, args: e.args || [] };
  if (e.skip) return Object.assign(base, { skipped: e.skip });
  if (ctx.skipScripts.has(id)) return Object.assign(base, { skipped: 'skipped by the caller (--skip-script)' });
  if (e.probe && !ctx.withProbes) return Object.assign(base, { skipped: 'a probe: run only with --with-probes' });
  if (e.todo && !ctx.todo) return Object.assign(base, { skipped: 'reads the TODO, and no TODO was given (--no-todo)' });
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snapshot-script-'));
  try {
    const args = (e.args || []).map(a => a.split('{TEMP}').join(temp));
    const abs = path.join(root, e.program || e.script);
    const ext = path.extname(abs).toLowerCase();
    let cmd, argv;
    if (e.program) { cmd = abs; argv = args; }
    else if (['.js', '.cjs', '.mjs'].includes(ext)) { cmd = process.execPath; argv = [abs].concat(args); }
    else if (['.ps1', '.psm1'].includes(ext)) { cmd = 'powershell.exe'; argv = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', abs].concat(args); }
    else if (ext === '.sh' || ext === '') { cmd = 'sh'; argv = [abs].concat(args); }
    else cannot('no way to run ' + e.script);
    const extra = ctx.todo ? { TODO_MD: ctx.todo } : {};
    const t0 = Date.now();
    const r = cp.spawnSync(cmd, argv, { cwd: root, env: childEnv(extra), encoding: 'utf8', maxBuffer: 256 << 20,
      timeout: (e.timeout_s || 900) * 1000, windowsHide: true });
    const seconds = Math.round((Date.now() - t0) / 100) / 10;
    let exit;
    if (r.error && r.error.code === 'ETIMEDOUT') exit = 'timeout';
    else if (r.error) exit = 'not started: ' + r.error.message;
    else exit = r.status === null ? 'signal ' + r.signal : r.status;
    const output = (r.stdout || '') + (r.stderr ? '\n--- stderr ---\n' + r.stderr : '');
    const counted = countsOf(output, e.volatile_lines);
    return Object.assign(base, { exit, seconds, lines: output.split('\n').length, counts: counted.rows, volatile_lines: counted.volatileLines, output });
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
}

// What a script's counts depend on, outside the tree: recorded so that the
// comparison can tell "the input changed" from "the script changed".
function variesWith(root, e, ctx) {
  const out = {};
  for (const v of e.varies_with || []) {
    if (v === 'todo') out.todo = ctx.todo ? sha256(fs.readFileSync(ctx.todo)) : 'none';
    else if (v === 'head') out.head = (git(root, ['rev-parse', 'HEAD']) || 'unknown').trim();
    else { const f = path.join(root, v); out[v] = fs.existsSync(f) ? sha256(fs.readFileSync(f)) : 'absent'; }
  }
  return out;
}

function safeName(id) { return id.replace(/[^A-Za-z0-9._-]+/g, '_').slice(0, 120); }

function scriptsSection(root, reg, ctx, outDir) {
  const missing = [];
  for (const e of reg.entries) {
    if (!e.run || ctx.skipScripts.has(entryId(e)) || (e.probe && !ctx.withProbes) || (e.todo && !ctx.todo)) continue;
    for (const n of e.needs || []) if (!fs.existsSync(path.join(root, n))) missing.push(entryId(e) + ' needs ' + n);
  }
  if (missing.length) cannot('missing inputs of the scripts (build them, or --skip-script <id>):\n    ' + missing.join('\n    '));
  const results = [];
  for (const e of reg.entries) {
    const res = runEntry(root, e, ctx);
    res.varies = variesWith(root, e, ctx);
    if (res.skipped) console.log('  skipped  ' + res.id + ': ' + res.skipped);
    else console.log('  ran      ' + res.id + ': exit ' + res.exit + ', ' + res.counts[0].n[0] + ' lines, ' + (res.counts.length - 1) + ' with numbers, ' + res.seconds + ' s');
    if (outDir && res.output !== undefined) {
      res.output_file = 'scripts/' + safeName(res.id) + '.txt';
      fs.mkdirSync(path.join(outDir, 'scripts'), { recursive: true });
      fs.writeFileSync(path.join(outDir, res.output_file), res.output, 'utf8');
    }
    delete res.output;
    results.push(res);
  }
  return { registry: LAYOUT.registry, folders: reg.folders, results };
}

function compareRows(oldAll, curAll) {
  const diffs = [];
  const drops = [];
  const key = (r) => r.n.join(',');
  // the line count first, on its own
  const oldLines = oldAll.length && oldAll[0].text === LINES_ROW ? oldAll[0] : null;
  const curLines = curAll.length && curAll[0].text === LINES_ROW ? curAll[0] : null;
  if (!oldLines || !curLines) cannot('a script result without its line count: a snapshot of another format');
  if (oldLines.n[0] !== curLines.n[0]) {
    diffs.push('non-empty lines of the output: ' + oldLines.n[0] + ' -> ' + curLines.n[0]);
    if (!isZero(oldLines.n[0]) && isZero(curLines.n[0])) drops.push('the output is empty now (it had ' + oldLines.n[0] + ' lines)');
  }
  const oldRows = oldAll.slice(1), curRows = curAll.slice(1);
  if (oldRows.length === curRows.length) {
    oldRows.forEach((o, i) => {
      const c = curRows[i];
      if (key(o) === key(c)) return;
      diffs.push('line ' + (i + 1) + ' of the counts: [' + o.n.join(', ') + '] -> [' + c.n.join(', ') + ']   «' + o.text + '» -> «' + c.text + '»');
      o.n.forEach((v, k) => { if (k < c.n.length && !isZero(v) && isZero(c.n[k])) drops.push('«' + o.text + '»: ' + v + ' -> ' + c.n[k]); });
    });
    return { diffs, drops };
  }
  // different number of lines: align the two sequences (longest common subsequence)
  const m = oldRows.length, n = curRows.length;
  const L = Array.from({ length: m + 1 }, () => new Int32Array(n + 1));
  for (let i = m - 1; i >= 0; i--) for (let j = n - 1; j >= 0; j--) {
    L[i][j] = key(oldRows[i]) === key(curRows[j]) ? L[i + 1][j + 1] + 1 : Math.max(L[i + 1][j], L[i][j + 1]);
  }
  let i = 0, j = 0;
  while (i < m || j < n) {
    if (i < m && j < n && key(oldRows[i]) === key(curRows[j])) { i++; j++; continue; }
    if (j < n && (i === m || L[i][j + 1] >= L[i + 1][j])) { diffs.push('line added:   [' + curRows[j].n.join(', ') + ']  «' + curRows[j].text + '»'); j++; }
    else { diffs.push('line gone:    [' + oldRows[i].n.join(', ') + ']  «' + oldRows[i].text + '»'); if (oldRows[i].n.some(v => !isZero(v))) drops.push('a line with counts is gone: «' + oldRows[i].text + '»'); i++; }
  }
  return { diffs, drops };
}

function compareScripts(snapDir, cur, rep) {
  const old = readJson(path.join(snapDir, 'scripts.json'), 'the scripts section of the snapshot');
  rep.section('scripts of ' + cur.folders.join(', ') + ': exit codes and counts', 'scripts');
  const oldBy = new Map(old.results.map(r => [r.id, r]));
  const curBy = new Map(cur.results.map(r => [r.id, r]));
  let same = 0, ran = 0;
  for (const [id, c] of curBy) {
    const o = oldBy.get(id);
    if (!o) { rep.diff('not in the snapshot: ' + id + (c.skipped ? ' (skipped: ' + c.skipped + ')' : ' (exit ' + c.exit + ')')); continue; }
    if (o.skipped || c.skipped) {
      if (o.skipped && c.skipped) { same++; continue; }
      rep.diff(id + ': ' + (o.skipped ? 'skipped in the snapshot (' + o.skipped + '), run now' : 'run in the snapshot, skipped now (' + c.skipped + ')'));
      continue;
    }
    ran++;
    // Only an input recorded THEN and NOW, with another value, excuses other
    // counts: a varies_with added to the registry after the snapshot has no
    // value of reference, and would otherwise switch the counts of that
    // script off in silence.
    const ov = o.varies || {}, cv = c.varies || {};
    const changedInputs = Object.keys(ov).filter(k => k in cv && ov[k] !== cv[k]);
    const unreferenced = Object.keys(cv).filter(k => !(k in ov));
    if (unreferenced.length) rep.line(id + ': varies_with ' + unreferenced.join(', ') + ' is not in the snapshot: it excuses nothing');
    const exitChanged = String(o.exit) !== String(c.exit);
    const rows = compareRows(o.counts, c.counts);
    if (!exitChanged && rows.diffs.length === 0) { same++; continue; }
    if (exitChanged) rep.diff(id + ': exit ' + o.exit + ' -> ' + c.exit);
    if (rows.drops.length) rep.diff(id + ': a count DROPPED TO 0', rows.drops.slice(0, 6));
    if (rows.diffs.length) {
      if (changedInputs.length && !rows.drops.length) {
        rep.line(id + ': counts differ, and so does its input outside the tree (' + changedInputs.join(', ') + '): reported, not counted');
        for (const d of rows.diffs.slice(0, 4)) rep.line('     ' + d);
      } else if (!rows.drops.length) {
        rep.diff(id + ': counts differ', rows.diffs.slice(0, 8).concat(rows.diffs.length > 8 ? ['... ' + (rows.diffs.length - 8) + ' more'] : []));
      } else {
        for (const d of rows.diffs.slice(0, 8)) rep.line('     ' + d);
      }
    }
  }
  for (const id of oldBy.keys()) if (!curBy.has(id)) rep.diff('gone from the registry: ' + id);
  rep.line(curBy.size + ' entries now (' + ran + ' run in both), ' + oldBy.size + ' in the snapshot, ' + same + ' the same');
  rep.endSection();
}

// ── the manifest ────────────────────────────────────────────────────────────
function versionsOf(root) {
  const out = {};
  const v = path.join(root, LAYOUT.bridgeVersion);
  out.bridge = fs.existsSync(v) ? fs.readFileSync(v, 'utf8').trim() : null;
  const a = path.join(root, LAYOUT.libraryVersion);
  const m = fs.existsSync(a) ? /AssemblyVersion\("([^"]+)"\)/.exec(fs.readFileSync(a, 'utf8')) : null;
  out.library = m ? m[1] : null;
  return out;
}

function gitOf(root) {
  const head = git(root, ['rev-parse', 'HEAD']);
  if (head === null) return { head: null, branch: null, dirty: null };
  const status = git(root, ['status', '--porcelain']) || '';
  return { head: head.trim(), branch: (git(root, ['rev-parse', '--abbrev-ref', 'HEAD']) || '').trim(),
    dirty: status.split('\n').filter(Boolean) };
}

function todoOf(root, opts) {
  if (opts.noTodo) return null;
  const candidates = [opts.todo, process.env.TODO_MD, path.join(root, LAYOUT.todo)].filter(Boolean);
  if (opts.todo && !fs.existsSync(opts.todo)) cannot('--todo ' + opts.todo + ' does not exist');
  // a TODO_MD that names nothing is not passed over for the default in silence
  if (!opts.todo && process.env.TODO_MD && !fs.existsSync(process.env.TODO_MD)) cannot('TODO_MD names ' + process.env.TODO_MD + ', which does not exist');
  return candidates.map(c => path.resolve(c)).find(c => fs.existsSync(c)) || null;
}

// ── take ────────────────────────────────────────────────────────────────────
async function take(opts) {
  const root = opts.root;
  const skip = opts.skip;
  const date = new Date();
  const out = path.resolve(opts.out || path.join(root, LAYOUT.snapshots, date.toISOString().slice(0, 10)));
  if (fs.existsSync(out) && fs.readdirSync(out).length) cannot('the snapshot folder exists and is not empty: ' + out + ' (name another with --out)');

  // Every input first, before anything slow: a missing one stops the tool.
  if (!skip.has('tests') && opts.ledgers.length !== 2) {
    cannot('two reference ledgers are needed (--ledger A --ledger B): the unstable cases are the ones that differ between them' +
           (opts.ledgers.length === 1 ? '. To prove the tool on one run, give the same file twice: the snapshot then says so.' : ''));
  }
  const tests = skip.has('tests') ? null : testsSection(opts.ledgers[0], opts.ledgers[1]);
  const reg = skip.has('scripts') ? null : loadRegistry(root);
  const todo = reg ? todoOf(root, opts) : null;
  if (reg && !todo && !opts.noTodo && reg.entries.some(e => e.todo && e.run)) {
    cannot('the registry runs scripts that read the TODO, and there is no TODO: pass --todo <TODO.md> (or set TODO_MD), or --no-todo to record them as skipped');
  }
  const catalogue = skip.has('catalogue') ? null : catalogueOf(root);
  if (!skip.has('tools')) {
    const bridge = opts.bridge ? path.resolve(opts.bridge) : path.join(root, LAYOUT.bridge);
    if (!fs.existsSync(bridge)) cannot('the bridge is missing: ' + bridge + ' (build it with .\\build.ps1 -BridgeOnly, or pass --bridge)');
  }

  // Written into a folder of its own and renamed at the end: a snapshot that
  // stopped half way never looks like a snapshot.
  const work = out + '.partial-' + process.pid;
  fs.rmSync(work, { recursive: true, force: true });
  try {
    await takeInto(work, { root, skip, date, tests, reg, todo, catalogue, opts });
    if (fs.existsSync(out)) fs.rmdirSync(out);
    fs.renameSync(work, out);
  } catch (e) {
    fs.rmSync(work, { recursive: true, force: true });
    throw e;
  }
  console.log('');
  console.log('snapshot written: ' + out);
  if (tests && tests.same_ledger_twice) console.log('NOTE: one reference run given twice. The unstable list is not measured; take the snapshot again with two runs.');
  return 0;
}

async function takeInto(out, { root, skip, date, tests, reg, todo, catalogue, opts }) {
  const manifest = {
    about: 'A snapshot of the repository for the translation of W3.1-bis (entry W3.1-bis.2, item 11), written by ' +
           'Checks/language/snapshot.js; the format is documented in Checks/language/snapshot.md.',
    format: FORMAT,
    taken: date.toISOString(),
    root,
    git: gitOf(root),
    versions: versionsOf(root),
    node: process.version,
    inputs: { ledgers: tests ? tests.ledgers : [], same_ledger_twice: tests ? tests.same_ledger_twice : null,
      todo: todo ? { file: todo, sha256: sha256(fs.readFileSync(todo)) } : null },
    sections: {},
    skipped: {},
  };
  for (const s of SECTIONS) if (skip.has(s)) manifest.skipped[s] = 'skipped by the caller (--skip ' + s + ')';
  if (manifest.git.dirty && manifest.git.dirty.length) console.log('take: the tree has ' + manifest.git.dirty.length + ' uncommitted change(s); they are recorded in snapshot.json');

  fs.mkdirSync(out, { recursive: true });
  if (catalogue) {
    fs.writeFileSync(path.join(out, 'catalog.json'), catalogue.raw);
    writeJson(path.join(out, 'catalogue.json'), catalogue.record);
    manifest.sections.catalogue = ['catalogue.json', 'catalog.json'];
    console.log('catalogue: ' + Object.keys(catalogue.record.operations).length + ' operations, normalized sha256 ' + short(catalogue.record.sha256_normalized));
  }
  if (tests) {
    writeJson(path.join(out, 'tests.json'), tests, 'tests');
    manifest.sections.tests = ['tests.json'];
    console.log('tests: ' + Object.keys(tests.tests).length + ' tests, ' + Object.keys(tests.cases).length + ' cases; unstable: ' +
                tests.unstable_cases.length + ' cases, ' + tests.unstable_tests.length + ' tests' +
                (tests.same_ledger_twice ? ' (ONE run given twice: empty by construction)' : ''));
    for (const u of tests.unstable_cases) console.log('  unstable case ' + u.case + ': ' + u.why);
  }
  let scan = null;
  if (!skip.has('literals') || !skip.has('assertions')) scan = scanSources(root);
  if (!skip.has('literals')) {
    writeJson(path.join(out, 'literals.json'), { callees: CALLEES, projects: LAYOUT.projects, sources: scan.sources, calls: scan.calls }, 'calls');
    manifest.sections.literals = ['literals.json'];
    console.log('literals: ' + scan.calls.length + ' calls of ' + CALLEES.join('/') + ' in ' + scan.sources.length + ' source files, ' + scan.units.length + ' string units');
  }
  if (!skip.has('assertions')) {
    const a = assertionsSection(root, scan);
    writeJson(path.join(out, 'strings.json'), { about: 'Every string unit of the sources: a literal, or a chain of + with its holes as null.', units: scan.units }, 'units');
    const without = a.list.filter(x => satisfiers(x).length === 0);
    const byTemplate = a.list.filter(x => !x.candidates.length && x.template.length);
    writeJson(path.join(out, 'assertions.json'), {
      keys: ASSERTION_KEYS, cases: a.cases, plans_not_cases: a.plans, values_not_text: a.notText,
      satisfied_by_a_template: byTemplate.map(x => x.id), without_candidate: without.map(x => x.id), assertions: a.list,
    }, 'assertions');
    manifest.sections.assertions = ['assertions.json', 'strings.json'];
    console.log('assertions: ' + a.list.length + ' textual assertions in ' + a.cases + ' cases (' + a.plans + ' plans are not cases, ' +
                a.notText + ' values are not text); ' + byTemplate.length + ' satisfied only by a template, ' + without.length + ' without a satisfying literal');
  }
  if (!skip.has('tools')) {
    const tools = await toolsOf(root, opts);
    writeJson(path.join(out, 'tools.json'), tools);
    manifest.sections.tools = ['tools.json'];
    console.log('tools: ' + tools.tools.length + ' tools (' + tools.tools.map(t => t.name).join(', ') + '), bridge ' + tools.server.version);
  }
  if (reg) {
    console.log('scripts:');
    const scripts = scriptsSection(root, reg, { todo, withProbes: opts.withProbes, skipScripts: opts.skipScripts }, out);
    writeJson(path.join(out, 'scripts.json'), scripts, 'results');
    manifest.sections.scripts = ['scripts.json', 'scripts/'];
    const ran = scripts.results.filter(r => !r.skipped);
    console.log('scripts: ' + ran.length + ' run, ' + (scripts.results.length - ran.length) + ' skipped with their reason');
  }
  writeJson(path.join(out, 'snapshot.json'), manifest);
}

// ── compare ─────────────────────────────────────────────────────────────────
async function compare(snapDir, opts) {
  const root = opts.root;
  snapDir = path.resolve(snapDir);
  const manifest = readJson(path.join(snapDir, 'snapshot.json'), 'the snapshot manifest');
  if (manifest.format !== FORMAT) cannot('the snapshot is of format ' + manifest.format + ', this tool reads ' + FORMAT);
  const has = (s) => isObject(manifest.sections) && Array.isArray(manifest.sections[s]);
  const rep = new Report(opts.show);
  console.log('snapshot compare: ' + snapDir + ' (taken ' + manifest.taken + ' at ' + (manifest.git.head || '?').slice(0, 10) + ') against ' + root);
  const todo = opts.skip.has('scripts') ? null : todoOf(root, opts);
  const want = (s) => {
    if (opts.skip.has(s)) { rep.skip(s, 'skipped by the caller'); return false; }
    if (!has(s)) { rep.skip(s, 'the snapshot has no ' + s + ' section' + (manifest.skipped && manifest.skipped[s] ? ': ' + manifest.skipped[s] : '')); return false; }
    return true;
  };
  // every input first, then the slow parts: a missing one stops the tool
  // before the sources are scanned and the scripts run
  const on = {};
  for (const s of SECTIONS) on[s] = want(s);
  if (on.tests && !opts.ledgers.length) { rep.skip('tests', 'no ledger given (--ledger <Bench/report/tests.json of the new run>)'); on.tests = false; }
  if (opts.ledgers.length > 1) cannot('compare takes one --ledger, the new run');
  for (const s of SECTIONS) {
    if (!on[s]) continue;
    for (const f of manifest.sections[s]) {
      if (!f.endsWith('/') && !fs.existsSync(path.join(snapDir, f))) cannot('the ' + s + ' section of the snapshot is missing ' + f);
    }
  }
  if (on.tests) readLedger(opts.ledgers[0]);
  if (on.tools) {
    const bridge = opts.bridge ? path.resolve(opts.bridge) : path.join(root, LAYOUT.bridge);
    if (!fs.existsSync(bridge)) cannot('the bridge is missing: ' + bridge + ' (build it with .\\build.ps1 -BridgeOnly, pass --bridge, or --skip tools)');
  }
  const reg = on.scripts ? loadRegistry(root) : null;
  if (reg && !todo && !opts.noTodo && reg.entries.some(e => e.todo && e.run)) {
    cannot('the registry runs scripts that read the TODO, and there is no TODO: pass --todo <TODO.md> (or set TODO_MD), or --no-todo');
  }
  if (on.catalogue) compareCatalogue(snapDir, catalogueOf(root), rep, windowRenames(opts.window, root, snapDir));
  if (on.literals || on.assertions) {
    const scan = scanSources(root);
    if (on.literals) compareLiterals(snapDir, scan, rep);
    if (on.assertions) compareAssertions(snapDir, assertionsSection(root, scan), rep, windowRenames(opts.window, root, snapDir));
  }
  if (on.tests) compareTests(snapDir, opts.ledgers[0], rep, windowRenames(opts.window, root, snapDir));
  if (on.tools) compareTools(snapDir, await toolsOf(root, opts), rep, windowRenames(opts.window, root, snapDir));
  if (reg) {
    console.log('');
    console.log('running the scripts:');
    const kept = fs.mkdtempSync(path.join(os.tmpdir(), 'snapshot-compare-'));
    compareScripts(snapDir, scriptsSection(root, reg, { todo, withProbes: opts.withProbes, skipScripts: opts.skipScripts }, kept), rep);
    rep.line('the outputs of this run are kept in ' + kept + ', those of the snapshot in ' + path.join(snapDir, 'scripts'));
  }

  console.log('');
  console.log('== UNSTABLE, from the two reference runs: listed apart, not counted as differences');
  if (manifest.inputs && manifest.inputs.same_ledger_twice) console.log('   (the snapshot was taken with ONE run given twice: no unstable case was measured)');
  if (rep.unstable.length === 0) console.log('   none' + (on.tests ? '' : ' compared (the tests were not compared)'));
  for (const u of rep.unstable) console.log('   ' + u);

  const total = rep.total();
  const per = Object.entries(rep.differences).filter(([, n]) => n > 0).map(([k, n]) => k + ' ' + n).join(', ');
  console.log('');
  console.log('snapshot compare: ' + total + ' difference(s)' + (per ? ' (' + per + ')' : '') +
              '; unstable listed apart: ' + rep.unstable.length +
              (rep.notCompared.length ? '; NOT COMPARED: ' + rep.notCompared.join('; ') : '') + '.');
  if (total > 0) return 1;
  return rep.notCompared.length ? 3 : 0;
}

// ── self-test ───────────────────────────────────────────────────────────────
// The fixtures are English on purpose: the tool does not care which language
// a message is in, and this file is judged by the language gate. The keys of
// the fake cases and of the fake catalogue are the protocol names of today,
// taken from the tables above.
const FAKE = {
  steps: 'steps', expectations: 'expect', mustFail: 'must_fail', why: 'why', args: 'arguments',
};

function fakeTree(dir, change) {
  change = change || {};
  const write = (rel, content) => {
    const f = path.join(dir, rel);
    fs.mkdirSync(path.dirname(f), { recursive: true });
    fs.writeFileSync(f, typeof content === 'string' ? content : JSON.stringify(content, null, 1), 'utf8');
  };
  const K = CATALOGUE_KEYS;
  write(LAYOUT.catalogue, {
    [K.families]: { solids: 'Solid bodies' },
    [K.operations]: [
      { [K.name]: 'make_block', famiglia: 'solids', description: change.catalogue ? 'Makes a box.' : 'Makes a block.' },
      { [K.name]: 'cut_hole', famiglia: 'solids', description: 'Cuts a hole.' },
    ],
  });
  const lib = LAYOUT.projects[0];
  const bridgeProject = LAYOUT.projects[1];
  write(lib, '<Project>\n  <ItemGroup>\n    <Compile Include="Ops.cs" />\n  </ItemGroup>\n</Project>\n');
  write(bridgeProject, '<Project>\n  <ItemGroup>\n    <Compile Include="Tools\\Notes.cs" />\n  </ItemGroup>\n</Project>\n');
  const callee = CALLEES[0];
  write(path.join(path.dirname(lib), 'Ops.cs'), [
    'class Ops',
    '{',
    '    object ' + callee + '(string why) { return why; }',
    '    // ' + callee + '("a refusal quoted in a comment is not a literal")',
    '    object MakeBlock(double width)',
    '    {',
    '        if (width <= 0) return ' + callee + '("The width must be ' + (change.zeroGone ? 'positive' : 'above zero') + ': give a positive width.");',
    '        if (width > 9) return ' + callee + '("The block is ' + (change.template ? 'wider than allowed' : 'too wide') + ' (" + width + " mm): the limit is ' + (change.literal ? '8' : '9') + ' mm.");',
    // a literal the text of the call shows as a hole: only `inner` holds it
    '        if (width == 5) return ' + callee + '("Five is " + (width > 4 ? "' + (change.ternary ? 'too even' : 'too round') + '" : "fine") + ".");',
    '        return null;',
    '    }',
    '}',
  ].join('\n'));
  write(path.join(path.dirname(bridgeProject), 'Tools', 'Notes.cs'), [
    'static class Notes',
    '{',
    '    internal const string Side = "outside";',
    // the words of a refusal, repeated in a description: a second candidate
    '    internal const string About = "make_block: the width must be above zero.";',
    change.noteGone ? '' : '    internal static string Refused() { return "Refused: the face is " + "not planar."; }',
    '}',
  ].join('\n'));
  // the case keys, with the contexts and keys this tool reads
  const ctx = (read, children) => ({ read, documented: {}, children: children || {}, frozen_free: {} });
  write(LAYOUT.caseKeys, {
    format: 1,
    roots: { declared: 'case', regression: 'regression_case' },
    contexts: {
      case: ctx([CASE_KEYS.name, FAKE.steps, FAKE.why], { [FAKE.steps]: 'step' }),
      step: ctx([CASE_KEYS.operation, FAKE.args, FAKE.expectations, FAKE.mustFail], { [FAKE.expectations]: 'expectation', [FAKE.mustFail]: 'must_fail' }),
      expectation: ctx([CASE_KEYS.what].concat(Object.keys(ASSERTION_KEYS.expectation))),
      must_fail: ctx(Object.keys(ASSERTION_KEYS.must_fail)),
      regression_case: ctx(['build'], {}),
      regression_expectation: ctx(Object.keys(ASSERTION_KEYS.regression_expectation)),
    },
  });
  const [messageKey] = Object.keys(ASSERTION_KEYS.must_fail);
  const [containsKey, , equalsKey] = Object.keys(ASSERTION_KEYS.expectation);
  const kase = (name, step) => ({ [CASE_KEYS.name]: name, [FAKE.why]: 'a fake case', [FAKE.steps]: [step] });
  write(LAYOUT.cases + '/blocks/too_wide.json', kase('too_wide', { [CASE_KEYS.operation]: 'make_block', [FAKE.args]: { width: 12 }, [FAKE.mustFail]: { [messageKey]: 'too wide' } }));
  write(LAYOUT.cases + '/blocks/too_wide_value.json', kase('too_wide_value', { [CASE_KEYS.operation]: 'make_block', [FAKE.args]: { width: 12 }, [FAKE.mustFail]: { [messageKey]: 'too wide (12 mm)' } }));
  write(LAYOUT.cases + '/blocks/zero_width.json', kase('zero_width', { [CASE_KEYS.operation]: 'make_block', [FAKE.args]: { width: 0 }, [FAKE.mustFail]: { [messageKey]: 'ABOVE ZERO' } }));
  write(LAYOUT.cases + '/blocks/note_case.json', kase('note_case', { [CASE_KEYS.operation]: 'cut_hole', [FAKE.args]: {},
    [FAKE.expectations]: [
      { [CASE_KEYS.what]: 'measurements.note', [containsKey]: 'not planar' },
      { [CASE_KEYS.what]: 'measurements.side', [equalsKey]: 'inside' },
      { [CASE_KEYS.what]: 'volume_mm3', [equalsKey]: 125 },
    ] }));
  write(LAYOUT.cases + '/blocks/a_plan.json', [{ operation: 'not a case' }]);
  // the scripts and their registry
  write('Checks/count.js', "console.log('checked " + (change.zero ? 0 : change.eight ? 8 : 7) + " items in 3 groups');\nconsole.log('took 12 ms');\n");
  write('Checks/cad.ps1', "Write-Output 'this one needs CAD'\n");
  if (change.extraScript) write('Checks/extra.js', "console.log('not in the registry');\n");
  // what the counts of count.js also depend on, outside the scripts
  write('data/input.txt', change.inputMoved ? 'b' : 'a');
  write(LAYOUT.registry, {
    format: 1,
    folders: ['Checks'],
    entries: [
      { script: 'Checks/count.js', run: true, varies_with: ['data/input.txt'].concat(change.variesAdded ? ['todo'] : []) },
      { script: 'Checks/cad.ps1', skip: 'a fake script that would need SOLIDWORKS' },
    ],
  });
  // the bridge, a test double over stdio
  write('bin/bridge.js', change.bridge === 'silent' ? "'use strict';\nprocess.stdin.resume();\n"
    : change.bridge === 'dies' ? "'use strict';\nprocess.exit(3);\n" : [
    "'use strict';",
    "const rl = require('readline').createInterface({ input: process.stdin });",
    "const send = (o) => process.stdout.write(JSON.stringify(o) + '\\n');",
    "rl.on('line', (line) => {",
    "  const m = JSON.parse(line);",
    "  if (m.method === 'initialize') send({ jsonrpc: '2.0', id: m.id, result: { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'fake', version: '1.0.0' }, instructions: 'Look first.' } });",
    "  else if (m.method === 'tools/list') send({ jsonrpc: '2.0', id: m.id, result: { tools: [",
    "    { name: 'look', description: '" + (change.tools ? 'Looks at the part, twice.' : 'Looks at the part.') + "', inputSchema: { type: 'object', properties: {} } },",
    "    { name: 'build', description: 'Builds a plan.', inputSchema: { type: 'object', properties: { steps: { type: 'array' } } } } ] } });",
    "});",
  ].join('\n'));
}

function fakeLedger(file, variant) {
  const t = (id, subject, outcome, label) => ({ id, file: 'Program.cs', method: 'Cases', ordinal: 1, line: 10, subject, occurrence: 1, outcome, group: 'casi', label });
  const c = (name, outcome, attempted, checked) => ({ name, folder: 'blocks', kind: 'declared', outcome, expectations_attempted: attempted, expectations_checked: checked, tests: {} });
  const wideFails = variant === 'wide_fails';
  const noteChecked = variant === 'B' || variant === 'note_moves' ? 1 : 2;
  const L = {
    format: 1, mode: variant === 'partial' ? 'partial' : 'full', library: '9.9.9.0', written: '2026-10-01T00:00:00',
    identities_unresolved: variant === 'unresolved' ? 2 : 0,
    tests: [
      t('Program.cs:Groups:1', null, 'ok', 'reads the catalogue'),
      t('Program.cs:Cases:1@too_wide', 'too_wide', wideFails ? 'failed' : 'ok', 'too_wide refuses'),
      t('Program.cs:Cases:1@zero_width', 'zero_width', 'ok', 'zero_width refuses'),
      t('Program.cs:Cases:2@note_case', 'note_case', 'ok', variant === 'relabelled' ? 'note_case, translated label' : 'note_case says it'),
    ],
    cases: [
      c('too_wide', wideFails ? 'red' : 'green', 1, 1),
      c('zero_width', 'green', 1, 1),
      c('note_case', 'green', 2, noteChecked),
    ],
  };
  fs.writeFileSync(file, JSON.stringify(L, null, 1), 'utf8');
}

function selfTest() {
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'snapshot-self-test-'));
  const me = __filename;
  const run = (args) => {
    const r = cp.spawnSync(process.execPath, [me].concat(args), { encoding: 'utf8', maxBuffer: 64 << 20, env: childEnv({ TODO_MD: '' }) });
    return { code: r.status, out: (r.stdout || '') + (r.stderr || '') };
  };
  const tree = (name, change) => { const d = path.join(work, name); fakeTree(d, change); return d; };
  const ledger = (name, variant) => { const f = path.join(work, name + '.json'); fakeLedger(f, variant); return f; };
  try {
    const base = tree('base');
    const A = ledger('A', 'A');
    const B = ledger('B', 'B');
    const snap = path.join(work, 'snap');
    const common = (root) => ['--root', root, '--bridge', path.join(root, 'bin', 'bridge.js'), '--no-todo'];
    const first = run(['take', '--out', snap].concat(common(base), ['--ledger', A, '--ledger', B]));
    if (first.code === 2 && /no Roslyn|does not compile|netstandard/.test(first.out)) {
      console.log('snapshot self-test: cannot run here (exit 2):\n' + first.out.split('\n').slice(-4).join('\n'));
      return 2;
    }
    const checks = [
      ['take: the snapshot of the fake tree is written, one case unstable between the two runs', first,
        0, [/unstable case blocks\/note_case: expectations checked 2 \/ 1/, /without a satisfying literal/, /snapshot written/]],
      ['take: a comment that quotes a refusal is not a literal (3 calls, not 4)', first, 0, [/literals: 3 calls of/]],
      ['take: an assertion across a value computed at run time is satisfied by the template of its message', first, 0, [/1 satisfied only by a template, 1 without a satisfying literal/]],
      ['compare, the same tree and run A: no difference, the unstable case listed apart', run(['compare', snap].concat(common(base), ['--ledger', A])),
        0, [/snapshot compare: 0 difference\(s\)/, /case blocks\/note_case: reference outcome green \/ green, checked 2 \/ 1/]],
      ['compare, a label translated: not a difference (the identity is file:method:ordinal)', run(['compare', snap].concat(common(base), ['--ledger', ledger('relabelled', 'relabelled')])),
        0, [/snapshot compare: 0 difference/]],
      ['compare, a changed literal of a refusal: red', run(['compare', snap].concat(common(tree('literal', { literal: true })), ['--ledger', A])),
        1, [/DIFF changed +Oce\.Solid\.Agent\.Core\/Ops\.cs:MakeBlock:2/, /literals 1/]],
      ['compare, a literal inside a refusal that the text of the call shows as a hole (a branch of ?:) changed: red',
        run(['compare', snap].concat(common(tree('ternary', { ternary: true })), ['--ledger', A])),
        1, [/DIFF changed +Oce\.Solid\.Agent\.Core\/Ops\.cs:MakeBlock:3/, /a literal inside the call changed/, /was «too round»/]],
      ['compare, one of the two members whose literals satisfied an assertion lost them, the other still has them: red',
        run(['compare', snap].concat(common(tree('zero-gone', { zeroGone: true })), ['--ledger', A, '--skip', 'literals'])),
        1, [/lost 1 of the members whose literals satisfied it: blocks\/zero_width#steps\[0\]\.must_fail\.message_includes/,
          /no longer in Oce\.Solid\.Agent\.Core\/Ops\.cs:MakeBlock/]],
      ['compare, the literal an assertion relied on is gone: red', run(['compare', snap].concat(common(tree('note-gone', { noteGone: true })), ['--ledger', A])),
        1, [/without a satisfying literal now: blocks\/note_case#steps\[0\]\.expect\[0\]\.includes/]],
      ['compare, the template an assertion relied on is gone: red', run(['compare', snap].concat(common(tree('template', { template: true })), ['--ledger', A])),
        1, [/without a satisfying literal now: blocks\/too_wide_value#steps\[0\]\.must_fail\.message_includes \(message_contains/, /satisfied in the snapshot by 1 template\(s\)/]],
      ['compare, a changed outcome: red', run(['compare', snap].concat(common(base), ['--ledger', ledger('wide', 'wide_fails')])),
        1, [/DIFF outcome ok -> failed: Program\.cs:Cases:1@too_wide/, /case blocks\/too_wide: outcome green -> red/]],
      ['compare, a run that is not full, though no test is missing from it: red',
        run(['compare', snap].concat(common(base), ['--ledger', ledger('partial', 'partial')])),
        1, [/DIFF the new run is not a full bench run \(mode partial\)/]],
      ['compare, a run with tests that have no identity: red',
        run(['compare', snap].concat(common(base), ['--ledger', ledger('unresolved', 'unresolved')])),
        1, [/DIFF the new run has 2 test\(s\) without an identity/]],
      ['compare, a script whose count drops to 0: red', run(['compare', snap].concat(common(tree('zero', { zero: true })), ['--ledger', A])),
        1, [/Checks\/count\.js: a count DROPPED TO 0/]],
      ['compare, other counts and another input outside the scripts (varies_with): reported, not counted',
        run(['compare', snap].concat(common(tree('input', { eight: true, inputMoved: true })), ['--ledger', A])),
        0, [/counts differ, and so does its input outside the tree \(data\/input\.txt\)/, /snapshot compare: 0 difference/]],
      ['compare, other counts and a varies_with the snapshot never recorded: red (it excuses nothing)',
        run(['compare', snap].concat(common(tree('varies', { eight: true, variesAdded: true })), ['--ledger', A])),
        1, [/varies_with todo is not in the snapshot: it excuses nothing/, /DIFF Checks\/count\.js: counts differ/]],
      ['compare, only the unstable case moved: no difference, listed apart', run(['compare', snap].concat(common(base), ['--ledger', ledger('moves', 'note_moves')])),
        0, [/snapshot compare: 0 difference/, /case blocks\/note_case: .*now green, checked 1/]],
      ['compare, the catalogue changed: red', run(['compare', snap].concat(common(tree('catalogue', { catalogue: true })), ['--ledger', A])),
        1, [/DIFF operation changed: make_block/]],
      ['compare, a tool changed: red', run(['compare', snap].concat(common(tree('tools', { tools: true })), ['--ledger', A])),
        1, [/DIFF tool changed: look/]],
      ['compare, no ledger and the scripts skipped: said, exit 3', run(['compare', snap].concat(common(base), ['--skip', 'scripts'])),
        3, [/NOT COMPARED: .*scripts \(skipped by the caller\)/, /NOT COMPARED: .*tests \(no ledger given/]],
      ['take, one ledger only: refused', run(['take', '--out', path.join(work, 'one')].concat(common(base), ['--ledger', A])),
        2, [/two reference ledgers are needed/]],
      ['take, the same ledger twice: no unstable case, and the snapshot says the list was not measured',
        run(['take', '--out', path.join(work, 'snap-twice')].concat(common(base), ['--ledger', A, '--ledger', A])),
        0, [/unstable: 0 cases, 0 tests \(ONE run given twice: empty by construction\)/, /NOTE: one reference run given twice/]],
      ['take, a bridge that never answers: stopped after --tools-timeout, refused',
        run(['take', '--out', path.join(work, 'snap-silent'), '--tools-timeout', '3', '--skip', 'literals', '--skip', 'assertions', '--skip', 'scripts']
          .concat(common(tree('silent', { bridge: 'silent' })), ['--ledger', A, '--ledger', B])),
        2, [/did not answer over stdio within 3 s \(stopped\)/]],
      ['take that stopped half way: no snapshot folder, and no partial one, is left',
        { code: fs.readdirSync(work).some(n => n.startsWith('snap-silent')) ? 1 : 0, out: fs.readdirSync(work).join('\n') }, 0, []],
      ['take, a bridge that ends at once: refused',
        run(['take', '--out', path.join(work, 'snap-dies'), '--skip', 'literals', '--skip', 'assertions', '--skip', 'scripts']
          .concat(common(tree('dies', { bridge: 'dies' })), ['--ledger', A, '--ledger', B])),
        2, [/ended \(exit 3\) before answering|does not read its input/]],
      ['take, a script the registry does not name: refused', run(['take', '--out', path.join(work, 'snap-extra')].concat(common(tree('extra', { extraScript: true })), ['--ledger', A, '--ledger', B])),
        2, [/does not name 1 script\(s\): Checks\/extra\.js/]],
      ['take, a ledger that is not there: refused', run(['take', '--out', path.join(work, 'nope')].concat(common(base), ['--ledger', A, '--ledger', path.join(work, 'missing.json')])),
        2, [/the ledger is missing/]],
      ['compare, a section file missing from the snapshot: refused', (() => {
        const broken = path.join(work, 'snap-broken');
        fs.cpSync(snap, broken, { recursive: true });
        fs.unlinkSync(path.join(broken, 'literals.json'));
        return run(['compare', broken].concat(common(base), ['--ledger', A]));
      })(), 2, [/the literals section of the snapshot is missing/]],
    ];
    let ok = 0;
    for (const [title, r, code, patterns] of checks) {
      const missing = patterns.filter(p => !p.test(r.out));
      const good = r.code === code && missing.length === 0;
      if (good) ok++;
      console.log((good ? '  ok  ' : '  XX  ') + title + (good ? '' : ': expected exit ' + code + ', got ' + r.code +
        (missing.length ? '; not printed: ' + missing.map(String).join(', ') : '')));
      if (!good) console.log(r.out.split('\n').map(l => '        | ' + l).slice(-25).join('\n'));
    }
    console.log('');
    console.log(ok + ' of ' + checks.length + ' checks as expected.');
    return ok === checks.length ? 0 : 1;
  } finally {
    fs.rmSync(work, { recursive: true, force: true });
  }
}

// ── main ────────────────────────────────────────────────────────────────────
function parseArgs(argv) {
  const a = { mode: null, snapshot: null, root: ROOT_DEFAULT, out: null, ledgers: [], todo: null, noTodo: false, bridge: null,
    toolsFrom: 'auto', toolsTimeout: 90, skip: new Set(), skipScripts: new Set(), withProbes: false, show: 25, window: null };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    const next = () => { if (i + 1 >= argv.length) cannot(x + ' needs a value'); return argv[++i]; };
    if (x === '--self-test') a.mode = 'self-test';
    else if (x === '--root') a.root = path.resolve(next());
    else if (x === '--out') a.out = next();
    else if (x === '--ledger') a.ledgers.push(next());
    else if (x === '--todo') a.todo = path.resolve(next());
    else if (x === '--no-todo') a.noTodo = true;
    else if (x === '--bridge') a.bridge = next();
    else if (x === '--tools-from') a.toolsFrom = next();
    else if (x === '--tools-timeout') { a.toolsTimeout = Number(next()); if (!(a.toolsTimeout > 0)) cannot('--tools-timeout needs a number of seconds'); }
    else if (x === '--skip') { const s = next(); if (!SECTIONS.includes(s)) cannot('--skip takes one of ' + SECTIONS.join(', ')); a.skip.add(s); }
    else if (x === '--skip-script') a.skipScripts.add(next());
    else if (x === '--with-probes') a.withProbes = true;
    else if (x === '--show') a.show = parseInt(next(), 10);
    else if (x === '--window') { a.window = parseInt(next(), 10); if (a.window !== 1 && a.window !== 2) cannot('--window: only windows 1 (W3.1-bis.4) and 2 (W3.1-bis.5) are known'); }
    else if (x.startsWith('--')) cannot('unknown option ' + x);
    else if (!a.mode && (x === 'take' || x === 'compare')) a.mode = x;
    else if (a.mode === 'compare' && !a.snapshot) a.snapshot = x;
    else cannot('unexpected argument ' + x);
  }
  if (!['auto', 'stdio', 'reflection'].includes(a.toolsFrom)) cannot('--tools-from is auto, stdio or reflection');
  if (!a.mode) cannot('say take, compare <snapshot> or --self-test');
  if (a.mode === 'compare' && !a.snapshot) cannot('compare needs the snapshot folder');
  if (!(a.show >= 0)) cannot('--show needs a number');
  return a;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.mode === 'self-test') return selfTest();
  if (args.mode === 'take') return take(args);
  return compare(args.snapshot, args);
}

if (require.main === module) {
  main().then(code => { process.exitCode = code; }, e => {
    if (e instanceof CannotRun || e instanceof tokenStream.GateError) console.log('snapshot: ' + e.message);
    else console.log('snapshot: ' + (e && e.stack ? e.stack : e));
    process.exitCode = 2;
  });
}

module.exports = { normalizeOutput, countsOf, compareRows, stable, candidatesFor, unitIndex };
