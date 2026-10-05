#!/usr/bin/env node
// =============================================================================
//  window-3-mentions.js - the mentions of renamed identifiers (W3.1-bis.6)
// =============================================================================
//
//  Called by window-3-identifiers.js --identifiers mentions, after the C#
//  batches and the scripts batch. The code renames bind to symbols, so a name
//  that a COMMENT, a DOCUMENT or a SCRIPT STRING writes (`OperazioniSw.Esegui`
//  in a remark, `NomiEseguibili` in a message of a check, a type in the
//  README) is left behind. The translation of the comments (.7) keeps the
//  names between backticks as they are, so they must already be the English
//  ones: this is the pass that makes them so.
//
//  WHAT IS A MENTION. Only a name that cannot be a word of the prose:
//    - a compound (two or more parts: `ContaFacce`, `daPortare`, `RACCORDO_NON_E_FORO`),
//      anywhere in a comment, in a document, or in a string of a script;
//    - a single word with a capital (`Esegui`, `Segna`) between backticks, when
//      the backticks hold a name (`Esegui`, `OperazioniSw.Esegui`, `Segna(false)`),
//      never a phrase.
//  A single lowercase word (`modello`, `nome`) is never touched here: it may be
//  a local of the code, a key of a JSON or a word of the sentence, and the
//  reader cannot tell. A name followed by a file extension is a file name (the
//  references pass owns those). A line that carries the marker `window-3: keep`
//  is left whole.
//
//  WHERE. Comments of C#, JavaScript, PowerShell, YAML, shell and XML files;
//  the whole text of the documents (.md, .txt); the string literals of the
//  JavaScript and PowerShell files (a check that looks for a type by its name
//  in a string names the English one now). Never: code tokens (the renamers
//  did them), JSON, the map, the baseline, the changelog, this window's
//  tools, and the tools of the earlier windows (their fixtures quote the
//  sources as they were).
//
//  USAGE (through window-3-identifiers.js)
//    node Checks/language/window-3-identifiers.js --identifiers mentions [--dry-run] [--list]
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');

const SKIP = [
  /^CHANGELOG\.md$/,
  /^Checks\/language\/(name-map\.tsv|baseline\.json|lexicon\.json|exceptions\.json)$/,
  /^Checks\/language\/(window-[0-9]-[a-z-]+|name-map-extract|snapshot|token-stream|detect-italian|measure|consumed-strings|update-names-in-notes|see-it-fail)\.js$/,
  /^Checks\/language\/(same-il|ci-proof)\.ps1$/,
  /^Checks\/language\/(snapshot|measure-labels[^/]*)\.(md|json)$/,
  /^Bench\/report\//,
  /^Bench\/cases\//,           // the cases are JSON: their values are window 8
  /^Tools\/catalog\.json$/,
  /^Checks\/language\/window-2-reverse\.json$/,
];
const KEEP = 'window-3: keep';
const NO_EXT = /^\.(cs|js|ps1|psm1|md|json|csproj|yml|yaml|txt|tsv|exe|dll|config|manifest|sln|sh|bat|cmd|xml)(?![A-Za-z0-9_])/;

function git(root, args) {
  return cp.execFileSync('git', ['-C', root].concat(args), { encoding: 'utf8', maxBuffer: 1 << 30 });
}

function loadAcorn() {
  const tries = [];
  if (process.env.ACORN_PATH) tries.push(process.env.ACORN_PATH);
  tries.push('acorn', 'internal/deps/acorn/acorn/dist/acorn');
  for (const t of tries) { try { return require(t); } catch (e) { /* next */ } }
  return null;
}

// ── the map ────────────────────────────────────────────────────────────────
function readRows(root, scripts) {
  const text = fs.readFileSync(path.join(root, 'Checks', 'language', 'name-map.tsv'), 'utf8').replace(/^﻿/, '');
  const all = new Map(), js = new Map(), ps = new Map();
  const over = scripts ? scripts.OVERRIDES : [];
  for (const line of text.split(/\r?\n/)) {
    if (!line || line.startsWith('#')) continue;
    const c = line.split('\t');
    if (c[0] !== 'identifier' || !c[2] || c[2] === c[1]) continue;
    const note = c[5] || '';
    all.set(c[1], c[2]);
    const oj = over.find(o => o[0] === c[1] && o[2] === 'javascript'), op = over.find(o => o[0] === c[1] && o[2] === 'powershell');
    if (/(^|;\s*)JavaScript:/.test(note)) js.set(c[1], oj ? oj[1] : c[2]);
    if (/(^|;\s*)PowerShell:/.test(note)) ps.set(c[1], op ? op[1] : c[2]);
  }
  return { all, js, ps };
}

// a name that cannot be a word of the prose
function distinctive(n) {
  if (n.length < 6) return false;
  if (/^[a-z]+(?:[A-Z][a-z0-9]*)+$/.test(n)) return true;          // camelCase with a hump
  if (/^[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]*)+$/.test(n)) return true;  // PascalCase of two parts or more
  if (/^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+$/.test(n)) return true;      // UPPER_SNAKE
  return false;
}
// words that are also the names SOLIDWORKS gives in an Italian installation (the custom
// properties of the templates the bridge was measured with): a mention of those names is a
// word of the data, not of the code
const DATA_WORDS = new Set(['Descrizione', 'Materiale']);

// ── the text of the segments ───────────────────────────────────────────────
function makeReplacer(rows) {
  const dist = [...rows].filter(([n]) => distinctive(n)).sort((a, b) => b[0].length - a[0].length);
  const rx = new RegExp('(?<![A-Za-z0-9_$\\-])(' + dist.map(([n]) => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|') + ')(?![A-Za-z0-9_])', 'g');
  const distMap = new Map(dist);
  const spanRx = /`([^`\n]+)`/g;
  const isName = (s) => /^[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*(?:\(.*\))?(?:\.[A-Za-z_][\w]*)*$/.test(s.trim()) && !/\s{2,}/.test(s) && s.split(/\s+/).length <= 3;
  return function replace(text, count) {
    // compounds, anywhere
    let out = text.replace(rx, (m, name, offset) => {
      const after = text.slice(offset + m.length, offset + m.length + 8);
      if (NO_EXT.test(after)) return m;                     // a file name
      count.n++; count.names.add(name);
      return distMap.get(name);
    });
    // single capitalised words between backticks
    out = out.replace(spanRx, (span, inner) => {
      if (!isName(inner)) return span;
      const re = /(?<![A-Za-z0-9_$\-])([A-Z][a-z]+)(?![A-Za-z0-9_])(?!\.(?:cs|js|ps1|md|json)\b)/g;
      let changed = false;
      const innerOut = inner.replace(re, (m, name, at) => {
        if (!/^[A-Z][a-z]+$/.test(name) || !rows.has(name)) return m;
        // a word of the data (a property of an Italian template) is a name only as a call
        if (DATA_WORDS.has(name) && inner[at + name.length] !== '(') return m;
        changed = true; count.n++; count.names.add(name);
        return rows.get(name);
      });
      return changed ? '`' + innerOut + '`' : span;
    });
    return out;
  };
}

function applyToSegments(text, segments, replace, count) {
  let out = '', pos = 0;
  for (const [s, e, kind] of segments.sort((a, b) => a[0] - b[0])) {
    if (s < pos) continue;
    out += text.slice(pos, s);
    const seg = text.slice(s, e);
    // a line with the marker is left whole; a string that is only a name is a key or a reflection
    // target, decided by hand (listed)
    if (kind === 's' && /^(['"`])[A-Za-z_][w.]*$/.test(seg) && count.rows.has(seg.slice(1, -1).split('.')[0]) ) { count.review.push(seg); out += seg; pos = e; continue; }
    out += seg.includes(KEEP) ? seg : replace(seg, count);
    pos = e;
  }
  return out + text.slice(pos);
}

// segments to process, per language
function csharpSegments(t) {
  const segs = [];
  let i = 0; const n = t.length;
  while (i < n) {
    const c = t[i], d = t[i + 1];
    if (c === '/' && d === '/') { const e = t.indexOf('\n', i); const end = e < 0 ? n : e; segs.push([i, end, 'c']); i = end; continue; }
    if (c === '/' && d === '*') { const e = t.indexOf('*/', i + 2); const end = e < 0 ? n : e + 2; segs.push([i, end, 'c']); i = end; continue; }
    if ((c === '$' && d === '@' && t[i + 2] === '"') || (c === '@' && d === '$' && t[i + 2] === '"') || (c === '@' && d === '"')) {
      let j = i + (c === '@' && d === '"' ? 2 : 3);
      while (j < n) { if (t[j] === '"' && t[j + 1] === '"') { j += 2; continue; } if (t[j] === '"') break; j++; }
      i = j + 1; continue;
    }
    if (c === '$' && d === '"') { let j = i + 2; while (j < n && t[j] !== '"') { if (t[j] === '\\') j++; j++; } i = j + 1; continue; }
    if (c === '"') { let j = i + 1; while (j < n && t[j] !== '"') { if (t[j] === '\\') j++; if (t[j] === '\n') break; j++; } i = j + 1; continue; }
    if (c === "'") { let j = i + 1; while (j < n && t[j] !== "'") { if (t[j] === '\\') j++; if (t[j] === '\n') break; j++; } i = j + 1; continue; }
    i++;
  }
  return segs;
}
function hashSegments(t) {            // YAML, shell, hooks: from # to the end of the line, outside quotes
  const segs = [];
  let i = 0; const n = t.length;
  let quote = null;
  while (i < n) {
    const c = t[i];
    if (quote) { if (c === quote) quote = null; else if (c === '\n') quote = null; i++; continue; }
    if (c === '"' || c === "'") { quote = c; i++; continue; }
    if (c === '#' && (i === 0 || /[\s]/.test(t[i - 1]))) { const e = t.indexOf('\n', i); const end = e < 0 ? n : e; segs.push([i, end, 'c']); i = end; continue; }
    i++;
  }
  return segs;
}
function xmlSegments(t) {
  const segs = [];
  const rx = /<!--[\s\S]*?-->/g; let m;
  while ((m = rx.exec(t))) segs.push([m.index, m.index + m[0].length, 'c']);
  return segs;
}
function jsSegments(acorn, t, rel) {
  const segs = [];
  const comments = [];
  let tokens;
  try {
    const tk = acorn.tokenizer(t, { ecmaVersion: 'latest', allowHashBang: true, locations: false, onComment: (block, text, start, end) => comments.push([start, end]) });
    tokens = [];
    for (const tok of tk) tokens.push(tok);
  } catch (e) { throw new Error(rel + ': cannot tokenize as JavaScript: ' + e.message); }
  for (const [s, e] of comments) segs.push([s, e, 'c']);
  for (const tok of tokens) {
    const label = tok.type.label;
    if (label === 'string' || label === 'regexp' || label === 'template') segs.push([tok.start, tok.end, 's']);
  }
  return segs;
}

const PS_HELPER = String.raw`param([string]$ListFile, [string]$OutFile)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web
$enc = [System.Web.HttpUtility]
$sb = New-Object System.Text.StringBuilder
function Walk($list, $acc) {
    foreach ($t in $list) {
        $k = [string]$t.Kind
        if ($k -eq 'Comment' -or $k -eq 'StringLiteral' -or $k -eq 'StringExpandable' -or $k -eq 'HereStringLiteral' -or $k -eq 'HereStringExpandable') {
            $kind = 's'; if ($k -eq 'Comment') { $kind = 'c' }
            $acc.Add('[' + $t.Extent.StartOffset + ',' + $t.Extent.EndOffset + ',"' + $kind + '"]')
        }
    }
}
foreach ($f in [IO.File]::ReadAllLines($ListFile, [Text.Encoding]::UTF8)) {
    if (-not $f) { continue }
    $tokens = $null; $errors = $null
    $src = [IO.File]::ReadAllText($f, (New-Object System.Text.UTF8Encoding($false)))
    if ($src.Length -gt 0 -and $src[0] -eq [char]0xFEFF) { $src = $src.Substring(1) }
    [void][System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$tokens, [ref]$errors)
    $acc = New-Object System.Collections.Generic.List[string]
    Walk $tokens $acc
    $errs = @($errors | Select-Object -First 3 | ForEach-Object { $enc::JavaScriptStringEncode($_.Extent.StartLineNumber.ToString() + ': ' + $_.Message, $true) })
    [void]$sb.Append('{"file":').Append($enc::JavaScriptStringEncode($f, $true)).Append(',"errors":[').Append(($errs -join ',')).Append('],"segs":[').Append(($acc -join ',')).Append(']}').Append([char]10)
}
[IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
`;
function psSegments(files) {
  const out = new Map();
  if (!files.length) return out;
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'w3mentions-'));
  try {
    const list = path.join(dir, 'files.txt'), res = path.join(dir, 'segs.jsonl'), script = path.join(dir, 'segs.ps1');
    fs.writeFileSync(list, files.join('\n') + '\n', 'utf8');
    fs.writeFileSync(script, '﻿' + PS_HELPER, 'utf8');
    const r = cp.spawnSync(process.platform === 'win32' ? 'powershell.exe' : 'pwsh',
      ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script, '-ListFile', list, '-OutFile', res], { encoding: 'utf8', maxBuffer: 1 << 28 });
    if (r.status !== 0 || !fs.existsSync(res)) throw new Error('the PowerShell helper failed (exit ' + r.status + '): ' + (r.stdout || '') + (r.stderr || ''));
    for (const line of fs.readFileSync(res, 'utf8').split('\n')) {
      if (!line.trim()) continue;
      const o = JSON.parse(line);
      if (o.errors.length) throw new Error(o.file + ' does not parse as PowerShell: ' + o.errors.join(' | '));
      out.set(path.resolve(o.file), o.segs);
    }
    return out;
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function textOf(file) {
  const raw = fs.readFileSync(file);
  const bom = raw.length >= 3 && raw[0] === 0xef && raw[1] === 0xbb && raw[2] === 0xbf;
  return { text: raw.toString('utf8', bom ? 3 : 0), bom };
}

function run(opts) {
  const root = opts.root;
  const acorn = loadAcorn();
  if (!acorn) throw new Error('no JavaScript parser: neither a local acorn package nor the copy inside Node (node --expose-internals)');
  const scripts = require('./window-3-scripts.js');
  const rows = readRows(root, scripts);
  const rAll = makeReplacer(rows.all), rJs = makeReplacer(new Map([...rows.all, ...rows.js])), rPs = makeReplacer(new Map([...rows.all, ...rows.ps]));
  const files = git(root, ['ls-files', '-z']).split('\0').filter(Boolean).filter(f => !SKIP.some(r => r.test(f)) && fs.existsSync(path.join(root, f)));
  const psFiles = files.filter(f => /\.(ps1|psm1)$/.test(f));
  const psSegs = psSegments(psFiles.map(f => path.join(root, f)));
  const changed = [];
  const count = { n: 0, names: new Set(), review: [], rows: rows.all };
  for (const f of files) {
    const ext = path.extname(f).toLowerCase();
    let kind = null;
    if (ext === '.cs') kind = 'cs';
    else if (/^\.(js|cjs|mjs)$/.test(ext)) kind = 'js';
    else if (/^\.(ps1|psm1)$/.test(ext)) kind = 'ps';
    else if (/^\.(md|txt)$/.test(ext) && f !== 'VERSION.txt') kind = 'doc';
    else if (/^\.(yml|yaml|sh)$/.test(ext) || /(^|\/)pre-push$/.test(f) || /(^|\/)\.git(ignore|attributes)$/.test(f)) kind = 'hash';
    else if (/^\.(csproj|config|manifest|props|targets|xml)$/.test(ext)) kind = 'xml';
    if (!kind) continue;
    const { text, bom } = textOf(path.join(root, f));
    let segs, replace = rAll;
    if (kind === 'cs') segs = csharpSegments(text);
    else if (kind === 'js') { segs = jsSegments(acorn, text, f); replace = rJs; }
    else if (kind === 'ps') { segs = psSegs.get(path.resolve(path.join(root, f))) || []; replace = rPs; }
    else if (kind === 'doc') segs = [[0, text.length, 'c']];
    else if (kind === 'hash') segs = hashSegments(text);
    else segs = xmlSegments(text);
    const before = count.n;
    const out = applyToSegments(text, segs.map(s => s.slice()), replace, count);
    if (out !== text) {
      changed.push(f + ': ' + (count.n - before));
      if (!opts.dry) fs.writeFileSync(path.join(root, f), Buffer.concat([bom ? Buffer.from([0xef, 0xbb, 0xbf]) : Buffer.alloc(0), Buffer.from(out, 'utf8')]));
    }
  }
  if (opts.list) for (const c of changed) console.log('  ' + c);
  if (count.review.length) console.log('strings that are only a name (a key or a reflection target), not renamed here: ' + [...new Set(count.review)].join(' '));
  console.log('window 3, identifiers mentions' + (opts.dry ? ' (dry run)' : '') + ': ' + count.n + ' mentions in ' + changed.length + ' files, ' + count.names.size + ' names');
  return 0;
}

module.exports = { run, distinctive };

if (require.main === module) {
  process.exitCode = run({ root: path.resolve(__dirname, '..', '..'), dry: process.argv.includes('--dry-run'), list: process.argv.includes('--list') });
}
