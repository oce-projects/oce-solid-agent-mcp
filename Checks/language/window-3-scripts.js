#!/usr/bin/env node
// =============================================================================
//  window-3-scripts.js - the scripts batch of rename window 3 (W3.1-bis.6)
// =============================================================================
//
//  Called by window-3-identifiers.js --identifiers scripts. The C# batches
//  (window-3-renamer.js) rename what binds to a symbol of the code that
//  ships; this one renames the names the JavaScript and PowerShell files of
//  the repository DECLARE and use (the `identifier` rows of the map: a row is
//  one Italian word, whatever language its note was made from), file by file: a name of a script
//  is private to the script, and the map's own note says that two files never
//  share a scope.
//
//  JavaScript (acorn, the copy inside Node): every Identifier token that is not
//  a member (`a.name`), not an object key and not a method name, with the
//  name of a row. A shorthand property (`{ name }`, `const { name } = r`)
//  keeps its KEY and gets the new name as the value (`{ name: itemName }`), so
//  the JSON a script writes or reads is the same; only the names the scripts
//  export to each other (`module.exports = { findTheTodo }` read by
//  `const { findTheTodo } = require(...)`) are renamed on both sides, key
//  included, because both sides are renamed.
//
//  PowerShell (its own parser, in one process for all the files): every
//  variable (also inside an expandable string, `${name}` and `$script:name`),
//  every function definition and its calls by bare name, and every named
//  parameter (`-Name`) that the file declares. A script parameter is also a
//  flag that other texts pass (`.\Bench\bench.ps1 -Gruppo viste`): the flag is
//  renamed everywhere it is written with its hyphen (code, strings, comments,
//  documents), because a flag is a name and not a word.
//
//  Before writing, every new name is checked for a clash in the file: a
//  name already there, an automatic or preference variable of PowerShell, a
//  JavaScript reserved word, and, for a PowerShell function, an alias or a
//  command that PowerShell would resolve first. A clash stops the batch; the
//  name is decided in the map (en column) or in OVERRIDES below.
//
//  The proof is the same as for the C#: the build and the contracts run the
//  scripts that matter, token-stream.js --mode renames explains every token
//  that changed, and the reverse proof of window-3-identifiers.js reads the
//  pairs of OVERRIDES too.
//
//  USAGE (through window-3-identifiers.js)
//    node Checks/language/window-3-identifiers.js --identifiers scripts [--dry-run] [--list]
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');

// A name whose English one, for the scripts, differs from its row because
// PowerShell would resolve the row's name to something else (Write is an
// alias of Write-Output, Type of Get-Content). [Italian, English, why].
// The reverse proof reads these pairs next to the map's.
const OVERRIDES = [
  ['Scrivi', 'WriteLine', 'powershell', 'Write is an alias of Write-Output in Windows PowerShell: a function of that name is never called'],
  ['Tipo', 'TypeName', 'powershell', 'Type is an alias of Get-Content in Windows PowerShell: a function of that name is never called'],
  ['argomenti', 'args', 'javascript', 'arguments is a reserved word of JavaScript: as a parameter it is an error in strict code and hides the real one elsewhere'],
];
// One file whose name would clash in that file: [file, Italian, English, why]
const FILE_OVERRIDES = [
  ['Bench/run_plans.js', 'argomenti', 'cliArgs', 'the file already has a function parameter called args, and the command line arguments of the script are a constant of the top level'],
];

// files the batch never touches
const SKIP = [
  /^CHANGELOG\.md$/,
  /^Checks\/language\/(name-map\.tsv|baseline\.json)$/,
  /^Checks\/language\/window-3-(identifiers|renamer|scripts)\.js$/,
];

const JS_RESERVED = new Set(('break case catch class const continue debugger default delete do else enum export extends false finally for ' +
  'function if import in instanceof new null return super switch this throw true try typeof var void while with yield let static ' +
  'implements interface package private protected public await async arguments eval undefined NaN Infinity').split(' '));

// PowerShell automatic and preference variables (case-insensitive)
const PS_AUTOMATIC = new Set(('$ ? ^ _ args confirmpreference consolefilename debugpreference error erroractionpreference errorview ' +
  'event eventargs eventsubscriber executioncontext false foreach formatenumerationlimit home host input lastexitcode matches ' +
  'myinvocation nestedpromptlevel null ofs pid profile progresspreference psboundparameters pscmdletpath psculture psdebugcontext ' +
  'pshome psitem psscriptroot pssenderinfo psuiculture psversiontable pwd sender shellid stacktrace switch this true ' +
  'verbosepreference warningpreference whatifpreference').split(' '));

function loadAcorn() {
  const tries = [];
  if (process.env.ACORN_PATH) tries.push(process.env.ACORN_PATH);
  tries.push('acorn', 'internal/deps/acorn/acorn/dist/acorn');
  for (const t of tries) { try { return require(t); } catch (e) { /* next */ } }
  return null;
}

function git(root, args) {
  return cp.execFileSync('git', ['-C', root].concat(args), { encoding: 'utf8', maxBuffer: 1 << 30 });
}

function readRows(root) {
  const text = fs.readFileSync(path.join(root, 'Checks', 'language', 'name-map.tsv'), 'utf8').replace(/^﻿/, '');
  const js = new Map(), ps = new Map(), lang = new Map();
  for (const line of text.split(/\r?\n/)) {
    if (!line || line.startsWith('#')) continue;
    const c = line.split('\t');
    if (c[0] !== 'identifier' || !c[2] || c[2] === c[1]) continue;
    const note = c[5] || '';
    const overJs = OVERRIDES.find(o => o[0] === c[1] && o[2] === 'javascript');
    const overPs = OVERRIDES.find(o => o[0] === c[1] && o[2] === 'powershell');
    // every row: a row made from the C# locals still names the script's variable of the same word
    js.set(c[1], overJs ? overJs[1] : c[2]);
    ps.set(c[1], overPs ? overPs[1] : c[2]);
    lang.set(c[1], c[5] || '');
  }
  return { js, ps };
}

function textOf(file) {
  const raw = fs.readFileSync(file);
  const bom = raw.length >= 3 && raw[0] === 0xef && raw[1] === 0xbb && raw[2] === 0xbf;
  return { text: raw.toString('utf8', bom ? 3 : 0), bom };
}
function writeText(file, text, bom) {
  fs.writeFileSync(file, Buffer.concat([bom ? Buffer.from([0xef, 0xbb, 0xbf]) : Buffer.alloc(0), Buffer.from(text, 'utf8')]));
}

// edits: [{ at, len, to }] over `text`; the old text of each is checked
function apply(text, edits, what) {
  const seen = new Set();
  const list = edits.filter(e => { const k = e.at + ':' + e.len; if (seen.has(k)) return false; seen.add(k); return true; })
    .sort((a, b) => b.at - a.at);
  for (let i = 1; i < list.length; i++) {
    if (list[i].at + list[i].len > list[i - 1].at) throw new Error(what + ': two edits overlap at ' + list[i].at);
  }
  for (const e of list) {
    if (e.from !== undefined && text.substr(e.at, e.len) !== e.from)
      throw new Error(what + ': expected «' + e.from + '» at ' + e.at + ', found «' + text.substr(e.at, e.len) + '»');
    text = text.slice(0, e.at) + e.to + text.slice(e.at + e.len);
  }
  return text;
}

// ── JavaScript ─────────────────────────────────────────────────────────────
function parseJs(acorn, src, file) {
  let last;
  for (const sourceType of ['script', 'module']) {
    try { return acorn.parse(src, { ecmaVersion: 'latest', sourceType, allowHashBang: true, allowReturnOutsideFunction: true, ranges: true }); }
    catch (e) { last = e; }
  }
  throw new Error(file + ' does not parse as JavaScript: ' + last.message);
}

// the names a file exports by shorthand (`module.exports = { name }`)
function exportedShorthands(ast) {
  const out = new Set();
  const walk = (n) => {
    if (!n || typeof n.type !== 'string') return;
    if (n.type === 'AssignmentExpression' && n.right && n.right.type === 'ObjectExpression') {
      const l = n.left;
      const isExports = l.type === 'MemberExpression' && ((l.object.type === 'Identifier' && l.object.name === 'module' && l.property.name === 'exports') ||
        (l.object.type === 'Identifier' && l.object.name === 'exports'));
      if (isExports) for (const p of n.right.properties) if (p.type === 'Property' && p.shorthand && p.key.type === 'Identifier') out.add(p.key.name);
    }
    for (const k of Object.keys(n)) {
      if (k === 'range' || k === 'start' || k === 'end') continue;
      const v = n[k];
      if (Array.isArray(v)) v.forEach(walk); else if (v && typeof v.type === 'string') walk(v);
    }
  };
  walk(ast);
  return out;
}

function jsFile(acorn, rel, text, rows, exportedAnywhere, problems) {
  const ast = parseJs(acorn, text, rel);
  const edits = [];
  const idents = new Set();       // every identifier token the file writes (not a member, not a key)
  const hits = [];                // [node, kind]
  const handled = new Set();      // value identifiers of shorthand properties, done with their key
  const walk = (node, parent, key) => {
    if (!node || typeof node.type !== 'string') return;
    if (node.type === 'Property' && node.shorthand) {
      const val = node.value.type === 'AssignmentPattern' ? node.value.left : node.value;
      const name = val.name;
      idents.add(name);
      if (rows.has(name)) {
        const en = rows.get(name);
        // an exported name is renamed with its key, on both sides
        edits.push(exportedAnywhere.has(name) ? { at: node.key.start, len: node.key.end - node.key.start, from: name, to: en }
          : { at: node.key.start, len: node.key.end - node.key.start, from: name, to: name + ': ' + en });
        hits.push(name);
      }
      handled.add(val);
      if (node.value.type === 'AssignmentPattern') walk(node.value.right, node.value, 'right');
      return;
    }
    if (node.type === 'Identifier' && !handled.has(node)) {
      const member = parent && parent.type === 'MemberExpression' && key === 'property' && !parent.computed;
      const objectKey = parent && parent.type === 'Property' && key === 'key' && !parent.computed;
      const methodKey = parent && (parent.type === 'MethodDefinition' || parent.type === 'PropertyDefinition') && key === 'key' && !parent.computed;
      if (!member && !objectKey && !methodKey) {
        idents.add(node.name);
        if (rows.has(node.name)) { edits.push({ at: node.start, len: node.end - node.start, from: node.name, to: rows.get(node.name) }); hits.push(node.name); }
      }
    }
    for (const k of Object.keys(node)) {
      if (k === 'range' || k === 'start' || k === 'end') continue;
      const v = node[k];
      if (Array.isArray(v)) v.forEach(x => { if (x && typeof x.type === 'string') walk(x, node, k); });
      else if (v && typeof v.type === 'string') walk(v, node, k);
    }
  };
  walk(ast, null, null);
  // clashes: a new name already written in the file, or reserved, or two old names on one new one
  const renamed = new Map();
  for (const n of new Set(hits)) renamed.set(n, rows.get(n));
  const targets = new Map();
  for (const [from, to] of renamed) {
    if (JS_RESERVED.has(to)) problems.push(rel + ': ' + from + ' -> ' + to + ' is a reserved word');
    if (idents.has(to) && !renamed.has(to)) problems.push(rel + ': ' + from + ' -> ' + to + ' clashes with a name the file already has');
    if (targets.has(to)) problems.push(rel + ': ' + from + ' and ' + targets.get(to) + ' both become ' + to);
    targets.set(to, from);
  }
  return { edits, renamed, text: apply(text, edits, rel) };
}

// ── PowerShell ─────────────────────────────────────────────────────────────
const PS_HELPER = String.raw`param([string]$ListFile, [string]$OutFile)
# Positions of the names of a PowerShell file (window-3-scripts.js).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web
$enc = [System.Web.HttpUtility]
$L = 'System.Management.Automation.Language'
$sb = New-Object System.Text.StringBuilder
foreach ($f in [IO.File]::ReadAllLines($ListFile, [Text.Encoding]::UTF8)) {
    if (-not $f) { continue }
    $tokens = $null; $errors = $null
    $src = [IO.File]::ReadAllText($f, (New-Object System.Text.UTF8Encoding($false)))
    if ($src.Length -gt 0 -and $src[0] -eq [char]0xFEFF) { $src = $src.Substring(1) }
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$tokens, [ref]$errors)
    $items = New-Object System.Collections.Generic.List[string]
    if ($errors.Count -eq 0) {
        if ($ast.ParamBlock) { foreach ($pa in $ast.ParamBlock.Parameters) { $items.Add('["s",' + $pa.Extent.StartOffset + ',' + $pa.Extent.EndOffset + ',' + $enc::JavaScriptStringEncode($pa.Name.VariablePath.UserPath, $true) + ']') } }
        $nodes = $ast.FindAll({ param($a) $true }, $true)
        foreach ($n in $nodes) {
            $s = $n.Extent.StartOffset; $e = $n.Extent.EndOffset
            if ($n -is [System.Management.Automation.Language.VariableExpressionAst]) {
                $items.Add('["v",' + $s + ',' + $e + ',' + $enc::JavaScriptStringEncode($n.VariablePath.UserPath, $true) + ']')
            } elseif ($n -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
                $items.Add('["f",' + $s + ',' + $e + ',' + $enc::JavaScriptStringEncode($n.Name, $true) + ']')
            } elseif ($n -is [System.Management.Automation.Language.CommandAst]) {
                $first = $n.CommandElements[0]
                if ($first -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $first.StringConstantType -eq 'BareWord') {
                    $items.Add('["c",' + $first.Extent.StartOffset + ',' + $first.Extent.EndOffset + ',' + $enc::JavaScriptStringEncode($first.Value, $true) + ']')
                }
            } elseif ($n -is [System.Management.Automation.Language.CommandParameterAst]) {
                $items.Add('["p",' + $s + ',' + $e + ',' + $enc::JavaScriptStringEncode($n.ParameterName, $true) + ']')
            }
        }
    }
    $errs = @($errors | Select-Object -First 3 | ForEach-Object { $enc::JavaScriptStringEncode($_.Extent.StartLineNumber.ToString() + ': ' + $_.Message, $true) })
    [void]$sb.Append('{"file":').Append($enc::JavaScriptStringEncode($f, $true)).Append(',"errors":[').Append(($errs -join ',')).Append('],"items":[').Append(($items -join ',')).Append(']}').Append([char]10)
}
[IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
`;

function psPositions(files) {
  const out = new Map();
  if (!files.length) return out;
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'w3scripts-'));
  try {
    const list = path.join(dir, 'files.txt'), res = path.join(dir, 'items.jsonl'), script = path.join(dir, 'positions.ps1');
    fs.writeFileSync(list, files.join('\n') + '\n', 'utf8');
    fs.writeFileSync(script, '﻿' + PS_HELPER, 'utf8');
    const r = cp.spawnSync(process.platform === 'win32' ? 'powershell.exe' : 'pwsh',
      ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script, '-ListFile', list, '-OutFile', res],
      { encoding: 'utf8', maxBuffer: 1 << 28 });
    if (r.status !== 0 || !fs.existsSync(res)) throw new Error('the PowerShell helper failed (exit ' + r.status + '): ' + (r.stdout || '') + (r.stderr || ''));
    for (const line of fs.readFileSync(res, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      const o = JSON.parse(line);
      if (o.errors.length) throw new Error(o.file + ' does not parse as PowerShell: ' + o.errors.join(' | '));
      out.set(path.resolve(o.file), o.items);
    }
    return out;
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

// the aliases and commands a function name would meet first in PowerShell
function psShadowed(names) {
  if (!names.length) return new Map();
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'w3scripts-'));
  try {
    const script = path.join(dir, 'shadow.ps1');
    fs.writeFileSync(script, '﻿' + 'foreach ($n in $args) { $c = @(Get-Command -Name $n -ErrorAction SilentlyContinue | Where-Object { $_.CommandType -in "Alias","Cmdlet","Function","Filter" }); if ($c.Count) { Write-Output ($n + "|" + $c[0].CommandType) } }', 'utf8');
    const r = cp.spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script].concat(names), { encoding: 'utf8' });
    const m = new Map();
    for (const l of (r.stdout || '').split(/\r?\n/)) { const k = l.indexOf('|'); if (k > 0) m.set(l.slice(0, k).toLowerCase(), l.slice(k + 1)); }
    return m;
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function psFile(rel, text, items, rows, problems) {
  const lower = new Map();
  for (const [k, v] of rows) { if (!lower.has(k.toLowerCase())) lower.set(k.toLowerCase(), []); lower.get(k.toLowerCase()).push([k, v]); }
  // the new spelling of an old name: the row of that exact spelling, else the row of another spelling with the case of the first letter
  const newName = (name) => {
    if (rows.has(name)) return rows.get(name);
    const c = lower.get(name.toLowerCase());
    if (!c) return null;
    const en = c[0][1];
    return (name[0] === name[0].toUpperCase() ? en[0].toUpperCase() : en[0].toLowerCase()) + en.slice(1);
  };
  const edits = [];
  const vars = new Set(), funcs = new Map(), paramsDeclared = new Set();
  // functions defined here
  for (const [t, s, e, n] of items) if (t === 'f') funcs.set(n.toLowerCase(), n);
  // parameters declared by a param block: the variables on a ParameterAst are found by position below
  const renamed = new Map();
  const note = (from, to) => { renamed.set(from.toLowerCase(), [from, to]); };
  for (const [t, s, e, n] of items) {
    if (t === 'v') {
      let user = n.replace(/^(?:script|global|local|private|using):/i, '');
      if (/:/.test(user)) continue;
      vars.add(user.toLowerCase());
      const to = newName(user);
      if (!to || to === user) continue;
      const ext = text.slice(s, e);
      const k = ext.toLowerCase().lastIndexOf(user.toLowerCase());
      if (k < 0) { problems.push(rel + ': cannot place $' + user + ' in «' + ext + '»'); continue; }
      edits.push({ at: s + k, len: user.length, from: user, to });
      note(user, to);
    } else if (t === 'f') {
      const to = newName(n);
      if (!to || to === n) continue;
      const m = /^\s*(function|filter|workflow)\s+((?:global:|script:|local:)?)([^\s({]+)/i.exec(text.slice(s, e));
      if (!m) { problems.push(rel + ': cannot place function ' + n); continue; }
      const at = s + m[0].length - m[3].length;
      edits.push({ at, len: m[3].length, from: m[3], to });
      note(n, to);
    }
  }
  // calls of the functions defined here, by bare name
  for (const [t, s, e, n] of items) {
    if (t !== 'c' || !funcs.has(n.toLowerCase())) continue;
    const to = newName(funcs.get(n.toLowerCase()));
    if (!to) continue;
    const spelled = n;
    edits.push({ at: s, len: e - s, from: text.slice(s, e), to: text.slice(s, e).replace(spelled, to) });
  }
  // the named parameters this file declares, written as -Name
  const declared = new Set();
  for (const [t, , , n] of items) if (t === 'v') declared.add(n.toLowerCase());
  const flagsRenamed = new Set();
  for (const [t, s, e, n] of items) {
    if (t !== 'p') continue;
    if (!declared.has(n.toLowerCase())) continue;           // a flag of a command outside this file
    const to = newName(n);
    if (!to || to === n) continue;
    const ext = text.slice(s, e);
    const k = ext.indexOf(n);
    if (k !== 1 && k !== 0) continue;
    edits.push({ at: s + k, len: n.length, from: n, to });
    flagsRenamed.add(n);
  }
  // clashes
  const newOf = new Map();
  for (const [low, [from, to]] of renamed) {
    const tl = to.toLowerCase();
    if (PS_AUTOMATIC.has(tl) && !funcs.has(low)) problems.push(rel + ': $' + from + ' -> $' + to + ' is an automatic or preference variable');
    if (vars.has(tl) && !renamed.has(tl) && !funcs.has(low)) problems.push(rel + ': $' + from + ' -> $' + to + ' clashes with a variable the file already has');
    if (funcs.has(low) && funcs.has(tl) && !renamed.has(tl)) problems.push(rel + ': function ' + from + ' -> ' + to + ' clashes with a function the file already has');
    const key = (funcs.has(low) ? 'f:' : 'v:') + tl;
    if (newOf.has(key) && newOf.get(key) !== low) problems.push(rel + ': ' + from + ' and ' + newOf.get(key) + ' both become ' + to);
    newOf.set(key, low);
  }
  const scriptParams = new Set();
  for (const [t, , , n] of items) if (t === 's') scriptParams.add(n);
  return { edits, renamed, scriptParams, funcs: [...funcs.values()].map(f => newName(f)).filter(Boolean), flags: flagsRenamed, text: apply(text, edits, rel) };
}

// ── the run ────────────────────────────────────────────────────────────────
function run(opts) {
  const root = opts.root;
  const acorn = loadAcorn();
  if (!acorn) throw new Error('no JavaScript parser: neither a local acorn package nor the copy inside Node (node --expose-internals)');
  const rows = readRows(root);
  const files = git(root, ['ls-files', '-z']).split('\0').filter(Boolean).filter(f => !SKIP.some(r => r.test(f)));
  const jsFiles = files.filter(f => /\.(js|cjs|mjs)$/.test(f) && fs.existsSync(path.join(root, f)));
  const psFilesList = files.filter(f => /\.(ps1|psm1)$/.test(f) && fs.existsSync(path.join(root, f)));
  const problems = [];
  const report = [];

  // the names exported by shorthand anywhere
  const exported = new Set();
  const parsed = new Map();
  for (const f of jsFiles) {
    const { text } = textOf(path.join(root, f));
    const ast = parseJs(acorn, text, f);
    parsed.set(f, ast);
    for (const n of exportedShorthands(ast)) exported.add(n);
  }

  const outputs = new Map();     // file -> { text, bom }
  let jsEdits = 0, psEdits = 0;
  const names = new Set();
  for (const f of jsFiles) {
    const { text, bom } = textOf(path.join(root, f));
    const mine = new Map(rows.js);
    for (const [file, it, en] of FILE_OVERRIDES) if (file === f && mine.has(it)) mine.set(it, en);
    const r = jsFile(acorn, f, text, mine, exported, problems);
    if (r.edits.length) { outputs.set(f, { text: r.text, bom }); jsEdits += r.edits.length; for (const n of r.renamed.keys()) names.add(n); report.push(f + ': ' + r.edits.length + ' edits, ' + r.renamed.size + ' names'); }
  }

  const positions = psPositions(psFilesList.map(f => path.join(root, f)));
  const flagNames = new Map();     // script parameter (old) -> new, to carry to the other texts
  const fnNames = new Set();
  for (const f of psFilesList) {
    const { text, bom } = textOf(path.join(root, f));
    const items = positions.get(path.resolve(path.join(root, f)));
    const r = psFile(f, text, items, rows.ps, problems);
    for (const fn of r.funcs) fnNames.add(fn.toLowerCase());
    for (const sp of r.scriptParams) if (rows.ps.has(sp)) flagNames.set(sp, rows.ps.get(sp));
    if (r.edits.length) { outputs.set(f, { text: r.text, bom }); psEdits += r.edits.length; for (const [, [from]] of r.renamed) names.add(from); report.push(f + ': ' + r.edits.length + ' edits, ' + r.renamed.size + ' names'); }
  }

  // a function name must not meet an alias or a command first
  const allNew = new Set();
  for (const f of psFilesList) {
    const o = outputs.get(f);
    if (!o) continue;
  }
  const shadow = psShadowed([...fnNames]);
  for (const n of fnNames) if (shadow.has(n)) problems.push('PowerShell function ' + n + ' would be shadowed by the ' + shadow.get(n) + ' of the same name');

  // the flags of the script parameters, in every text that writes them with a hyphen
  let flagEdits = 0;
  const textFiles = files.filter(f => !/\.(png|jpe?g|gif|ico|exe|dll|pdb|zip|sldprt|sldasm|slddrw|step|stp|pdf|bmp|snk)$/i.test(f) && fs.existsSync(path.join(root, f)));
  if (flagNames.size) {
    const rx = new RegExp('(?<![A-Za-z0-9_\\-])-(' + [...flagNames.keys()].sort((a, b) => b.length - a.length).join('|') + ')(?![A-Za-z0-9_])', 'g');
    for (const f of textFiles) {
      const cur = outputs.get(f);
      const read = cur || textOf(path.join(root, f));
      const t = read.text;
      if (!t.includes('-')) continue;
      let n = 0;
      const out = t.replace(rx, (m, name) => { n++; return '-' + flagNames.get(name); });
      if (n) { outputs.set(f, { text: out, bom: read.bom }); flagEdits += n; report.push(f + ': ' + n + ' flags'); }
    }
  }

  if (opts.list) for (const l of report) console.log('  ' + l);
  for (const p of problems) console.log('PROBLEM: ' + p);
  if (problems.length) { console.log('window 3, identifiers scripts: stopped, ' + problems.length + ' problems'); return 1; }
  if (!opts.dry) for (const [f, o] of outputs) writeText(path.join(root, f), o.text, o.bom);
  console.log('window 3, identifiers scripts' + (opts.dry ? ' (dry run)' : '') + ': ' + (jsEdits + psEdits + flagEdits) + ' edits in ' + outputs.size + ' files (' +
    jsEdits + ' JavaScript, ' + psEdits + ' PowerShell, ' + flagEdits + ' flags), ' + names.size + ' names');
  return 0;
}

module.exports = { run, OVERRIDES, FILE_OVERRIDES, extraPairs: () => OVERRIDES.map(o => [o[0], o[1]]).concat(FILE_OVERRIDES.map(o => [o[1], o[2]])) };

if (require.main === module) {
  const root = path.resolve(__dirname, '..', '..');
  process.exitCode = run({ root, dry: process.argv.includes('--dry-run'), list: process.argv.includes('--list') });
}
