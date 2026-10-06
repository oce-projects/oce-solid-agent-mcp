#!/usr/bin/env node
// =============================================================================
//  window-5-messages.js - the messages of window 5 (W3.1-bis.8)
// =============================================================================
//
//  Translates into English the Italian TEXT THE PROGRAM PRODUCES, and the
//  cases and consumers that depend on it, in the same step, and proves it. The
//  texts are: the refusals (Failed(...)), the verdicts, errors and warnings,
//  the lines of the logs and of the narration, the labels of the checks, the
//  texts of the installer window, the prose of the bench cases (why,
//  tolerance_why, defect_caught..., the notes that start with an underscore),
//  the textual assertions of the cases (message_includes, includes,
//  does_not_include, a textual equal_to), and the literals other programs look
//  for in that output (Checks/language/consumed-strings.json).
//
//  NOT HERE: the descriptions of the catalogue and of the MCP resources (.9),
//  the documents (.10), identifiers and comments (done). JSON keys and enum
//  values that are protocol stay.
//
//  THE TEXTS ARE FOUND BY A PARSER, never by looking for a quote in the raw
//  text: Roslyn for C#, acorn for JavaScript, the PowerShell parser for
//  PowerShell, a scanner of the JSON grammar for the cases (window-5-scan.js).
//  A string of the program that the language gate calls Italian is a candidate;
//  a gate hit that no unit explains is listed as UNMAPPED.
//
//  THE UNIT is what snapshot.js calls a string unit: a literal, an
//  interpolated string, a template, or a chain of `+` with its holes. The
//  translator sees a unit as its PARTS (the pieces of text, in order) and its
//  HOLES (the values computed at run time, read only), and gives back the same
//  number of parts: a hole stays where it is, so every {0}, `$x` and
//  `$"..{expr}.."` survives by construction. A sentence that runs over holes is
//  translated as one thing and put back piece by piece.
//
//  ASSERTIONS GO WITH THEIR MESSAGE. For every textual assertion of a case the
//  tool records the units of the sources that satisfy it (what snapshot.js
//  records: the constant text that contains it, ignoring case, or the template
//  that does). A message and the case that asserts it are in the same chunk,
//  and the translator gives the new substring together with the new message.
//  An assertion of values computed at run time ("-157 a 157", "Ø30 per 40")
//  is linked by HOLES: the unit whose holes stand where its numbers, names
//  with a digit and texts between guillemets are, its words being the unit's.
//  A translated substring may not be WIDER than the old one (held by more than
//  twice as many producer literals, and by more than three more): one common
//  word in place of a phrase of its own lets the case pass on another message.
//
//  PROBES GO WITH THEIR MESSAGE TOO. A probe is a lone literal a program
//  compares another's output with (Contains, IndexOf, StartsWith, includes, a
//  regular expression that tests, == or case, -match/-like/-eq), held by an
//  Italian unit of the code: the contracts of Tests/, the bench, the scripts
//  of Checks/. Left behind, a probe that demanded a text goes red, and one
//  that demanded its absence (IndexOf(..) < 0, -notmatch) stays green for
//  ever. A probe whose words are Italian is an item of the cluster of its
//  messages (a narrow one, at most six units) or of the last chunk (a wide
//  one); any other only guards. The files that read the owner's notes are
//  not probes (NOT_PROBES).
//
//  THE CHUNKS
//    --extract <dir> [--pilot Bridge,Installer]
//                        writes <dir>/<id>.json, one chunk per ~17-20k Italian
//                        words, grouped by folder, a source file never split
//                        (the items of a case file that assert a message go
//                        with the message, its prose goes with the cases), and
//                        <dir>/plan.json. A chunk is { id, folder, base, files,
//                        items: [{ id, file, unit, kind, class, line, parts,
//                        holes, fixed, show, words, assertions, consumer,
//                        satisfiers }], assertions: [...] }. The translator
//                        writes <dir>/<id>.en.json:
//                        { id, items: [{ id, parts }] }, with `parts` the same
//                        length as the original (an assertion item gives its
//                        new substring as its only part).
//    --apply <dir> [--dry] [--chunk a,b]
//                        writes the translations back by position, a unit found
//                        again by its name (member:sN, JSON path). It REFUSES a
//                        unit, and with it the whole file:
//                          - whose text in the file is no longer the original of
//                            the chunk (stale), or an item with no translation;
//                          - whose parts differ in number, or that changes a fixed
//                            part, or whose new text cannot be spelled in its
//                            style (a typographic quote in PowerShell...);
//                          - whose invariants differ from the original: the
//                            placeholders ({0}, {name}), the escapes (line ends,
//                            tabs), the numbers, the units of measure, the names
//                            between backticks, the file names, the URLs, the
//                            code-like names, the guillemets, the markers a check
//                            reads (the invariants of window-4-comments.js);
//                          - an assertion whose new substring is not contained
//                            in the new text of a unit that satisfied the old
//                            one, or a not_contains whose new substring is in no
//                            producer, or whose message is not translated in the
//                            same run, or a new substring WIDER than the old;
//                          - a probe whose new text none of its messages will
//                            hold (narrow: none of those this run changes), or
//                            a message that would leave a narrow probe outside
//                            the run with nothing to find (a wide one is in
//                            the last chunk and the check judges it);
//                          - a regular expression of a consumer whose skeleton
//                            (everything that is not a letter) changed;
//                          - a file where, once written, the text outside the
//                            units differs, a unit other than the chosen ones
//                            changed, the parser reads other text than the
//                            requested, or the token stream is not the same
//                            outside string literals (token-stream.js, mode
//                            strings; a JSON file is compared as parsed).
//                        A refused file is not written. A file already written
//                        from the same translation is "already applied". After
//                        the files, the consumed-strings inventory is bound
//                        again to the literals that changed, and its contract
//                        is run.
//    --check [--rev <rev>] [--zero] [--chunks <dir>]
//                        the STATIC CHECK over the whole tree against <rev> (the
//                        tree before the window): the token stream of every
//                        touched .cs/.js/.ps1 file is the same outside string
//                        literals, a JSON case is the same outside the values it
//                        translates; the calls of the checks (file:method:
//                        ordinal) are the same list; every translated unit keeps
//                        its invariants; every textual assertion is still
//                        satisfied by the literals that satisfied it (0 pointing
//                        at nothing), every not_contains by a producer; the
//                        consumed strings are produced; Italian per category
//                        (strings, json_values) outside the catalogue.
//    --survey            counts and the links between assertions and files
//    --self-test         fixtures of every language in a temporary git
//                        repository; every refusal above is seen red on a
//                        mutated copy, and the good cases green.
//    --root <dir>        another checkout (default: the repository of this file)
//
//  EXIT CODES
//    0  done, nothing refused
//    1  a refusal or a difference outside the texts, or --self-test red
//    2  cannot run (no parser, a file that does not parse, a bad argument)
//    3  done, but Italian is still found in the units that were written
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const crypto = require('crypto');
const os = require('os');

const TS = require('./token-stream.js');
const DET = require('./detect-italian.js');
const SNAP = require('./snapshot.js');
const CONS = require('./consumed-strings.js');
const W4 = require('./window-4-comments.js');
const SCAN = require('./window-5-scan.js');

// token-stream.js reaches the acorn inside Node only with --expose-internals
if (require.main === module && !SCAN.hasAcorn() && !process.execArgv.includes('--expose-internals')) {
  const r = cp.spawnSync(process.execPath, ['--expose-internals', __filename].concat(process.argv.slice(2)), { stdio: 'inherit' });
  process.exit(r.status === null ? 2 : r.status);
}

class Stop extends Error {}
function cannot(msg) { throw new Stop(msg); }

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const TARGET_WORDS = 17500;
const MAX_WORDS = 20000;

// What belongs to window 5 (.9 and .10 and the gate's own files are not here)
const CATALOGUE_SCOPE = [
  /^Oce\.Solid\.Agent\.Core\/Catalog\//, /^Oce\.Solid\.Agent\.Catalog\//, /^Tools\/catalog\.json$/, /^Tools\/api_knowledge\.json$/,
  /^Mcp\/Risorse\.cs$/, // the titles and descriptions of the MCP resources (.9)
];
const inCatalogue = (rel) => CATALOGUE_SCOPE.some(re => re.test(rel));
const SKIP_TREES = [/^Checks\/language\//, /^Bench\/report\//, /^Bench\/data\//, /^Bench\/tasks\//];
// The assertion keys of a case, by context of Bench/case-keys.json (snapshot.js)
const ASSERTION_KEYS = { must_fail: { message_includes: 'message_contains' },
  expectation: { includes: 'contains', does_not_include: 'not_contains', equal_to: 'equals' },
  regression_expectation: { includes: 'contains', does_not_include: 'not_contains', equal_to: 'equals' } };
const PRODUCER_PROJECTS = ['Oce.Solid.Agent.Core/Oce.Solid.Agent.Core.csproj', 'Oce.Solid.Agent.csproj'];
const CHECK_CALLS = ['Check'];
// The few changes of code a window of messages is allowed to make, by name. Each is a number a contract asks to be
// moved by hand when its measure moves (a ratchet), and says why; the check lists them and refuses any other.
const ALLOWED_CODE_CHANGES = [];

// THE INTEGRATION (Checks/language/window-5-integration.json). What the translators' chunks could not do and the integrator did
// by hand, each with its reason, so that the check judges it like the rest:
//   forced_units   units the detector does not read as Italian (a lone word, a log tag, a plural suffix, a probe) that the window
//                  still has to translate: they are items of the window like any other (invariants, holes, numbers);
//   code_changes   the few changes of code, by name: a snippet of the tree before the window (one occurrence) and what it becomes.
//                  The tree before is read with them applied, so that its units and its token stream are compared with the tree
//                  now; any other change of code is still refused. Each is a number or a line a contract asks to be moved by hand
//                  (a ratchet, the alarm word of a verdict) and says why.
function integrationOf(root) {
  const file = path.join(root, 'Checks', 'language', 'window-5-integration.json');
  let j = { forced_units: [], code_changes: [] };
  try { j = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\ufeff/, '')); } catch (e) { /* none */ }
  const forced = new Set();
  for (const e of j.forced_units || []) for (const u of e.units) forced.add(e.file + '#' + u);
  // assertions the satisfiers of the Italian text do not describe: the pilot had already translated the message (so the Italian
  // word was held by other literals by chance), or the value is read from the drawing template and no literal of the sources holds it
  const assertions = new Map();
  for (const e of j.assertions || []) for (const u of e.units) assertions.set(e.file + '#' + u, e);
  return { forced, code_changes: j.code_changes || [], assertions };
}
function applySnippets(text, rel, integ) {
  let out = text;
  for (const c of integ.code_changes) {
    if (c.file !== rel) continue;
    const n = out.split(c.before).length - 1;
    if (n !== (c.count || 1)) cannot('window-5-integration.json: «' + c.before.slice(0, 50) + '» is in ' + rel + ' ' + n + ' times at the base, it must be ' + (c.count || 'exactly once'));
    out = out.split(c.before).join(c.after);
  }
  return out;
}

// ── reading and writing a file as the detector reads it ─────────────────────
function decodeSource(raw, rel) {
  if (raw.includes(0)) cannot(rel + ': holds a NUL byte, not a text file');
  if ((raw[0] === 0xff && raw[1] === 0xfe) || (raw[0] === 0xfe && raw[1] === 0xff)) cannot(rel + ': UTF-16, not handled');
  const bom = raw[0] === 0xef && raw[1] === 0xbb && raw[2] === 0xbf;
  const body = raw.slice(bom ? 3 : 0);
  const text = body.toString('utf8');
  if (!Buffer.from(text, 'utf8').equals(body)) cannot(rel + ': not valid UTF-8');
  return { raw, bom, text, sha: crypto.createHash('sha256').update(raw).digest('hex') };
}
function readSource(root, rel) { return decodeSource(fs.readFileSync(path.join(root, rel)), rel); }
function writeSource(root, rel, text, bom) {
  fs.writeFileSync(path.join(root, rel), Buffer.concat([bom ? Buffer.from([0xef, 0xbb, 0xbf]) : Buffer.alloc(0), Buffer.from(text, 'utf8')]));
}
function git(root, args) {
  try { return cp.execFileSync('git', ['-C', root].concat(args), { maxBuffer: 1 << 30, stdio: ['ignore', 'pipe', 'pipe'] }); }
  catch (e) { cannot('git ' + args.join(' ') + ' failed in ' + root + ': ' + String(e.stderr || e.message).trim()); }
}
// many blobs through one `git cat-file --batch`
function readBlobs(root, rev, rels) {
  const out = new Map();
  if (!rels.length) return out;
  const r = cp.spawnSync('git', ['-C', root, 'cat-file', '--batch'], { input: rels.map(f => rev + ':' + f).join('\n') + '\n', maxBuffer: 1 << 30 });
  if (r.status !== 0) cannot('git cat-file failed: ' + String(r.stderr));
  const buf = r.stdout;
  let pos = 0;
  for (const rel of rels) {
    const nl = buf.indexOf(10, pos);
    const header = buf.slice(pos, nl).toString('utf8');
    if (/ missing$/.test(header)) { out.set(rel, null); pos = nl + 1; continue; }
    const size = parseInt(header.split(' ')[2], 10);
    out.set(rel, buf.slice(nl + 1, nl + 1 + size));
    pos = nl + 1 + size + 1;
  }
  return out;
}

const langOf = SCAN.langOfFile;
const eolOf = (text) => (/\r\n/.test(text) ? '\r\n' : '\n');

// ── the tree: files, units, Italian ─────────────────────────────────────────
function listFiles(root, rev) {
  const args = rev ? ['ls-tree', '-r', '-z', '--name-only', rev] : ['ls-files', '-z'];
  return git(root, args).toString('utf8').split('\0').filter(Boolean)
    .filter(f => !DET.isBinaryByExtension(f) && langOf(f) && !SKIP_TREES.some(re => re.test(f)));
}

let detectorConfig = null;
function detector(root) {
  const at = fs.existsSync(path.join(root, 'Checks', 'language', 'lexicon.json')) ? root : ROOT_DEFAULT;
  if (!detectorConfig || detectorConfig.root !== at) detectorConfig = DET.loadConfig(at);
  return detectorConfig;
}

const lineStarts = SCAN.lineStarts, lineAt = SCAN.lineAt;

// Marks the units the gate calls Italian. Returns the hit lines no unit explains.
function markItalian(rel, lang, text, units, cfg) {
  const category = lang === 'json' ? 'json_values' : 'strings';
  const a = DET.analyzeText(rel, text, cfg);
  const hit = new Map();
  for (const h of a.hits) if (h.category === category) hit.set(h.line, (hit.get(h.line) || 0) + (h.score || 1));
  for (const u of units) { u.italian = false; u.words = 0; }
  if (!hit.size) return [];
  const starts = lineStarts(text);
  const own = (u) => u.leaves.some(l => l.k === 't' && l.cooked && DET.proseVerdict(l.cooked, cfg).italian);
  const byLine = new Map();
  for (const u of units) {
    const lines = new Set();
    for (const l of u.leaves) if (l.k === 't') for (let n = lineAt(starts, l.s); n <= lineAt(starts, Math.max(l.s, l.e - 1)); n++) lines.add(n);
    for (const n of lines) if (hit.has(n)) { if (!byLine.has(n)) byLine.set(n, []); byLine.get(n).push(u); }
  }
  const unmapped = [];
  for (const [n, score] of hit) {
    const cands = byLine.get(n);
    if (!cands) { unmapped.push({ line: n, words: score }); continue; }
    let chosen = cands.filter(own);
    if (!chosen.length) chosen = cands;
    chosen[0].words += score;
    for (const u of chosen) u.italian = true;
  }
  return unmapped;
}

// The tree as { files: Map(rel -> { rel, lang, text, bom, sha, units, calls, unmapped }) }
function analyzeFiles(root, rels, read, cfg, opts) {
  opts = opts || {};
  const entries = [];
  const src = new Map();
  for (const rel of rels) {
    const raw = read(rel);
    if (raw === null) continue;
    const s = decodeSource(raw, rel);
    src.set(rel, s);
    entries.push({ key: rel, lang: langOf(rel), text: s.text });
  }
  const found = SCAN.scanMany(entries, { calls: CHECK_CALLS });
  const files = new Map();
  for (const e of entries) {
    const f = found.get(e.key);
    const s = src.get(e.key);
    const unmapped = opts.noItalian ? [] : markItalian(e.key, e.lang, s.text, f.units, cfg);
    files.set(e.key, Object.assign({ rel: e.key, lang: e.lang, unmapped }, s, f));
  }
  return files;
}

// ── producers and the assertions of the cases ───────────────────────────────
function producerFiles(root, read) {
  const list = [];
  const seen = new Set();
  for (const proj of PRODUCER_PROJECTS) {
    const blob = read(proj);
    if (blob === null) cannot('the project ' + proj + ' is missing');
    const text = blob.toString('utf8').replace(/^﻿/, '');
    const dir = path.posix.dirname(proj);
    const re = /<Compile\s+Include="([^"]+)"/g;
    let m, n = 0;
    while ((m = re.exec(text)) !== null) {
      if (m[1].includes('$(')) continue;
      const rel = path.posix.normalize((dir === '.' ? '' : dir + '/') + m[1].replace(/\\/g, '/'));
      n++;
      if (!seen.has(rel)) { seen.add(rel); list.push(rel); }
    }
    if (n === 0) cannot(proj + ' compiles no file');
  }
  return list;
}

const isObject = (v) => v !== null && typeof v === 'object' && !Array.isArray(v);

// The textual assertions of the cases, as snapshot.js reads them; `unit` is the
// JSON path the scanner names the value by.
function assertionsOf(caseKeys, cases) {
  const ck = caseKeys;
  const list = [];
  const stepContexts = new Set(Object.keys(ck.contexts).filter(c => ck.contexts[c].read.includes('operation')));
  for (const c of cases) {
    const rootContext = ck.roots[c.root];
    if (!rootContext || !ck.contexts[rootContext]) cannot('Bench/case-keys.json has no root context for "' + c.root + '" cases');
    const walk = (node, ctx, at, where) => {
      const table = ASSERTION_KEYS[ctx];
      if (table) {
        for (const key of Object.keys(table)) {
          if (!Object.prototype.hasOwnProperty.call(node, key)) continue;
          const v = node[key];
          const values = Array.isArray(v) ? v.map((x, i) => [x, at + '.' + key + '[' + i + ']']) : [[v, at + '.' + key]];
          for (const [x, p] of values) {
            if (typeof x !== 'string') continue;
            list.push({ file: c.file, unit: '$' + p, case: c.key, root: c.root, step: where.step, operation: where.operation,
              kind: table[key], key, value: x });
          }
        }
      }
      for (const [childKey, childCtx] of Object.entries(ck.contexts[ctx].children)) {
        if (!Object.prototype.hasOwnProperty.call(node, childKey) || node[childKey] === null) continue;
        const v = node[childKey];
        const visit = (item, p) => {
          if (!isObject(item)) return;
          const w = stepContexts.has(childCtx) ? { step: p.replace(/^\./, ''), operation: typeof item.operation === 'string' ? item.operation : null } : where;
          walk(item, childCtx, p, w);
        };
        if (Array.isArray(v)) v.forEach((item, i) => visit(item, at + '.' + childKey + '[' + i + ']'));
        else visit(v, at + '.' + childKey);
      }
    };
    walk(c.body, rootContext, '', { step: null, operation: null });
  }
  return list;
}

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

// THE HOLES TAKE THE VALUES. An assertion that holds values computed at run time
// (a range of two numbers, a diameter by a length, two view names, the name
// of a plane between guillemets, all with the words of the message) is in no
// literal and fits no {0} template: it is satisfied by a unit whose holes stand
// where its numbers, its names with a digit and its text between guillemets
// are, the words around them being the unit's own. Without this such an
// assertion has no producer, and a translation of its message leaves it
// pointing at nothing with no check that sees it.
const HOLE_MARK = '\u0000';
const VALUE_TOKEN = /«([^«»]+)»|[-+]?\d+(?:[.,]\d+)*|[A-Za-z_][\w-]*\d[\w-]*/g;
const escRe = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
function holesPattern(value) {
  let src = '', literal = '', at = 0, tokens = 0;
  for (const m of value.matchAll(VALUE_TOKEN)) {
    src += escRe(value.slice(at, m.index));
    literal += value.slice(at, m.index);
    if (m[1] !== undefined) { src += '«(?:' + HOLE_MARK + '|' + escRe(m[1]) + ')»'; literal += '««»»'; }
    else src += '(?:' + HOLE_MARK + '|' + escRe(m[0]) + ')';
    tokens++;
    at = m.index + m[0].length;
  }
  src += escRe(value.slice(at));
  literal += value.slice(at);
  // the words of the unit must be there: two letters at least outside the values
  if (!tokens || (literal.match(/[A-Za-zÀ-ÿ]/g) || []).length < 2) return null;
  return new RegExp(src, 'i');
}
const holesText = (parts) => parts.map(p => (p === null ? HOLE_MARK : p)).join('');

// does the unit (as snapshot parts) satisfy the value, the way the bench asks?
function satisfies(parts, value, how) {
  const V = value.toUpperCase();
  if (!V) return false;
  if (how === 'template') { const runs = templateRuns(parts); return !!runs && templateMatch(V, runs); }
  if (how === 'holes') { const re = holesPattern(value); return !!re && re.test(holesText(parts)); }
  for (const p of parts) {
    if (p === null) continue;
    const up = p.toUpperCase();
    if (how === 'equal' ? up === V : up.includes(V)) return true;
  }
  return false;
}

// The assertions with the units that satisfy each, over the producers' units.
function linkAssertions(list, producerUnits) {
  const units = producerUnits.map(u => ({ id: u.file + '#' + u.id, file: u.file, unit: u.id, parts: SCAN.snapshotParts(u) }));
  const index = SNAP.unitIndex(units);
  let templates = null;
  for (const x of list) {
    const r = SNAP.candidatesFor(index, units, x.value);
    x.candidates = r.candidates;
    x.equal = r.equal;
    x.template = [];
    if (!x.candidates.length && x.value) {
      if (!templates) templates = units.map(u => ({ id: u.id, runs: templateRuns(u.parts) })).filter(u => u.runs);
      const T = x.value.toUpperCase();
      for (const u of templates) if (templateMatch(T, u.runs)) x.template.push(u.id);
    }
    x.holes = [];
    if (!x.candidates.length && !x.template.length && x.value) {
      const re = holesPattern(x.value);
      if (re) for (const u of units) if (u.parts.includes(null) && re.test(holesText(u.parts))) x.holes.push(u.id);
    }
    x.satisfiers = x.candidates.length ? x.candidates : x.template.length ? x.template : x.holes;
    x.how = x.candidates.length ? 'contains' : x.template.length ? 'template' : x.holes.length ? 'holes' : null;
  }
  return units;
}
// how a satisfier of an assertion is matched: whole, contained, by a {0} template, by holes
function howOf(a, key) {
  if (a.kind === 'equals' && (a.equal || []).includes(key)) return 'equal';
  return a.how === 'template' || a.how === 'holes' ? a.how : 'contains';
}

// ── the programs that look for a text: the probes ──────────────────────────
// A PROBE is a lone literal a program compares the output of another with: the
// argument of Contains, IndexOf, StartsWith, EndsWith, includes, a regular
// expression that tests a text, the side of a == or a case, the right side of
// a PowerShell -match, -like, -eq. A probe whose literal is held by an Italian
// unit of the code is a consumer of that message just as an entry of the
// consumed-strings inventory is: translate the message and leave the probe,
// and a contract that demanded the text goes red, while one that demanded its
// ABSENCE (IndexOf(..) < 0, !Contains, -notmatch) goes blind and stays green
// for ever. The probe changes with its message, in the same run.
const PROBE_CALLS = /^(Contains|IndexOf|LastIndexOf|StartsWith|EndsWith|includes|indexOf|lastIndexOf|startsWith|endsWith|IsMatch|Match|Matches|search|match)$/;
// Files whose comparisons read something other than the output of the programs
const NOT_PROBES = [
  { file: /^Checks\/(count-todo|todo-[\w-]+|where-is-the-todo|obsolete-api-list)\.(js|ps1)$/,
    why: 'they read the owner’s notes (TODO.md, outside the repository, Italian by decision), not the output of a program' },
];
function probeShape(f, u) {
  if (f.lang === 'json' || inCatalogue(f.rel)) return null;
  if (NOT_PROBES.some(x => x.file.test(f.rel))) return null;
  if (u.leaves.length !== 1 || u.leaves[0].k !== 't') return null;
  const l = u.leaves[0];
  const value = l.cooked;
  if (value.trim().length < 3 || !/[A-Za-zÀ-ÿ]{3,}/.test(value)) return null;
  const before = f.text.slice(Math.max(0, l.s - 80), l.s), after = f.text.slice(l.e, l.e + 120);
  const tail = after.split(/[;{]/)[0];
  let how = null, nocase = false, neg = false;
  if (u.kind === 'regex') {
    const flags = f.text.slice(l.ie + 1, l.e);
    if (/^\s*\.\s*(test|exec)\s*\(/.test(after) || /\.\s*(match|matchAll|replace|search|split)\s*\(\s*$/.test(before)) { how = 'regex'; nocase = flags.includes('i'); }
    neg = /!\s*$/.test(before);
  } else if (f.lang === 'ps') {
    const op = /-(c|i)?(not)?(match|like|eq|ne|contains)\s*$/i.exec(before);
    const meth = /\.\s*(Contains|IndexOf|StartsWith|EndsWith)\s*\(\s*$/i.exec(before);
    if (op) {
      const kind = op[3].toLowerCase();
      how = kind === 'match' ? 'regex' : kind === 'like' ? 'like' : 'equal';
      nocase = (op[1] || '').toLowerCase() !== 'c';
      neg = !!op[2] || kind === 'ne';
    } else if (meth) { how = 'contains'; nocase = /IgnoreCase/.test(tail); }
  } else {
    const callee = calleeLast(u.ctx[0] || '');
    if (PROBE_CALLS.test(callee)) {
      how = /^(IsMatch|Match|Matches)$/.test(callee) ? 'regex' : 'contains';
      nocase = /IgnoreCase/.test(tail) || /to(Lower|Upper)(Case|Invariant)?\(\)\s*\.\s*\w+\s*\(\s*$/.test(before);
      neg = /^[^;{}()]*\)\s*(<\s*0|===?\s*-1)/.test(after) || /!\s*[\w$.\[\]()]*\.\s*\w+\s*\(\s*$/.test(before);
    } else if (/(?:[!=]==?)\s*$/.test(before) || /^\s*[!=]==?/.test(after) || /\bcase\s+$/.test(before)) {
      how = 'equal';
      neg = /!==?\s*$/.test(before) || /^\s*!==?/.test(after);
    }
  }
  if (!how) return null;
  return { value, how, nocase, neg };
}
// does a unit (snapshot parts) hold what the probe looks for?
function probeHolds(parts, value, how, nocase) {
  const norm = (s) => (nocase ? s.toLowerCase() : s);
  const V = norm(value);
  if (how === 'regex' || how === 'like') {
    let re;
    try {
      re = how === 'like'
        ? new RegExp('^' + value.split('').map(c => (c === '*' ? '.*' : c === '?' ? '.' : escRe(c))).join('') + '$', nocase ? 'i' : '')
        : new RegExp(value, nocase ? 'i' : '');
    } catch (e) { return false; }
    return parts.some(p => p !== null && re.test(p)) || re.test(holesText(parts));
  }
  for (const p of parts) {
    if (p === null) continue;
    if (how === 'equal' ? norm(p) === V : norm(p).includes(V)) return true;
  }
  return false;
}
// The probes of a model, with the code units that hold their text.
function probesOf(model) {
  const sites = [];
  for (const f of model.files.values()) for (const u of f.units) {
    const p = probeShape(f, u);
    if (p) sites.push(Object.assign({ key: keyOf(f.rel, u.id), file: f.rel, unit: u.id, line: u.line }, p));
  }
  const siteKeys = new Set(sites.map(s => s.key));
  const code = [];
  for (const f of model.files.values()) {
    if (f.lang === 'json' || inCatalogue(f.rel)) continue;
    for (const u of f.units) {
      const k = keyOf(f.rel, u.id);
      if (siteKeys.has(k)) continue;
      code.push({ key: k, file: f.rel, unit: u.id, italian: !!u.italian, parts: SCAN.snapshotParts(u) });
    }
  }
  const out = [];
  for (const s of sites) {
    const prods = code.filter(c => probeHolds(c.parts, s.value, s.how, s.nocase));
    if (!prods.some(c => c.italian)) continue;
    s.producers = prods.map(c => ({ key: c.key, file: c.file, unit: c.unit, italian: c.italian }));
    if (prods.length > PROBE_MAX) continue;   // it matches a large part of the code: a pattern of the code, not a message it reads
    s.home = prods.length > K_NARROW ? 'wide' : 'narrow';
    out.push(s);
  }
  return out;
}

function casesOf(root, rels, read) {
  const cases = [];
  for (const rel of rels) {
    if (!/^Bench\/cases\/[^/]+\/[^/]+\.json$/.test(rel)) continue;
    const text = read(rel).toString('utf8').replace(/^﻿/, '');
    let body;
    try { body = JSON.parse(text); } catch (e) { cannot(rel + ' is not valid JSON: ' + e.message); }
    if (Array.isArray(body)) continue;   // a plan, not a case
    const folder = rel.split('/')[2];
    cases.push({ file: rel, key: folder + '/' + path.posix.basename(rel, '.json'), root: folder === 'regression' ? 'regression' : 'declared', body });
  }
  return cases;
}

// ── kinds ───────────────────────────────────────────────────────────────────
const FAIL_CALLS = /^(Failed|Fail|FailWith|Refuse|Reject|Refusal)$/;
const CHECK_CALL = /^(Check|Expect|Rule)$/;
const LOG_CALLS = /^(WriteLine|Write|Write-Host|Write-Output|Write-Warning|Write-Error|Write-Verbose|log|warn|error|info|Log|_log|Note|Say|Print|Narrate|Tell|Info|Warn|Warning|Error|Out|Step|Ok|Die|Say|Report|Show)$/;
function calleeLast(c) { const n = c.split(':')[0]; return n.split('.').pop(); }

function kindOfUnit(rel, lang, u, caseKeys) {
  if (lang === 'json') {
    if (u.assertion) return 'assertion';
    const key = u.key || '';
    if (/^_/.test(key) || caseKeys.prose.has(key)) return 'case-prose';
    return 'data';
  }
  if (/^Installer\/Window\.cs$/.test(rel)) return 'ui';
  const first = u.ctx[0] || '';
  const name = calleeLast(first), arg = parseInt(first.split(':')[1], 10);
  if (u.kind === 'regex') return 'consumer';
  if (FAIL_CALLS.test(name)) return 'fail';
  if (CHECK_CALL.test(name)) return arg === 0 ? 'label' : 'log';
  if (LOG_CALLS.test(name)) return 'log';
  const to = u.to || '';
  if (/verdict/i.test(to)) return 'verdict';
  if (/error|warning|warn/i.test(to)) return 'error';
  if (/message|msg|reason|detail|why|text|label|title|caption/i.test(to)) return 'text';
  for (const c of u.ctx) { const n = calleeLast(c); if (FAIL_CALLS.test(n)) return 'fail'; if (LOG_CALLS.test(n)) return 'log'; }
  return 'text';
}

function caseKeysInfo(root, read) {
  const blob = read('Bench/case-keys.json');
  if (blob === null) cannot('Bench/case-keys.json is missing');
  const ck = JSON.parse(blob.toString('utf8').replace(/^﻿/, ''));
  const prose = new Set(['why', 'tolerance_why', 'defect_caught', 'intent_why']);
  for (const ctx of Object.values(ck.contexts)) {
    for (const k of Object.keys(ctx.documented || {})) prose.add(k);
    for (const k of Object.keys(ctx.frozen_free || {})) prose.add(k);
  }
  ck.prose = prose;
  return ck;
}


// ── the model: items, assertions, consumers, clusters ───────────────────────
const K_NARROW = 6;
const PROBE_MAX = 40;    // a literal more than this many code units hold is not the probe of a message
const WIDER_SLACK = 3;   // a translated assertion may match this many producer literals more than the old one (and up to twice as many)      // an assertion with more satisfier units than this is "wide"
const LOCKED_STYLES = new Set(['raw', 'u8', 'interp-raw', 'template-bad']);
const keyOf = (file, unit) => file + '#' + unit;

function describeUnit(u) {
  const parts = [], fixed = [], holes = [], holeSk = [];
  for (const l of u.leaves) {
    if (l.k === 'h') { holes.push(l.src); holeSk.push(l.sk); continue; }
    parts.push(l.cooked);
    if (l.k === 'f' || l.exotic || LOCKED_STYLES.has(l.style)) fixed.push(parts.length - 1);
  }
  const show = u.leaves.map(l => (l.k === 'h' ? '⟦' + l.src + '⟧' : JSON.stringify(l.cooked))).join(' + ');
  return { parts, fixed, holes, holeSk, show };
}

function classOf(kind) {
  if (kind === 'consumer') return 'consumer';
  if (kind === 'regex') return 'regex';
  if (kind === 'case-prose') return 'prose';
  if (kind === 'assertion') return 'assertion';
  if (kind === 'data') return 'data';
  return 'message';
}

function readerOf(root, rev) {
  if (!rev) return (rel) => { try { return fs.readFileSync(path.join(root, rel)); } catch (e) { return null; } };
  const cache = new Map();
  return (rel) => {
    if (!cache.has(rel)) cache.set(rel, readBlobs(root, rev, [rel]).get(rel));
    return cache.get(rel);
  };
}

// The tree at a revision (null: the disk) as a model.
function buildModel(root, rev, opts) {
  opts = opts || {};
  const cfg = detector(root);
  const rels = listFiles(root, rev);
  let read;
  if (rev) {
    const blobs = readBlobs(root, rev, rels.concat(['Bench/case-keys.json', 'Checks/language/consumed-strings.json'].concat(PRODUCER_PROJECTS)));
    const plain = (rel) => (blobs.has(rel) ? blobs.get(rel) : readerOf(root, rev)(rel));
    const integ = integrationOf(root);
    const touched = new Set(integ.code_changes.map(c => c.file));
    read = (rel) => {
      const raw = plain(rel);
      if (!raw || !touched.has(rel) || opts.plainBase) return raw;
      const d = decodeSource(raw, rel);
      return Buffer.concat([d.bom ? Buffer.from([0xef, 0xbb, 0xbf]) : Buffer.alloc(0), Buffer.from(applySnippets(d.text, rel, integ), 'utf8')]);
    };
  } else read = readerOf(root, null);
  const files = analyzeFiles(root, rels, read, cfg, { noItalian: opts.noItalian });
  for (const f of files.values()) f.byId = new Map(f.units.map(u => [u.id, u]));
  const ck = caseKeysInfo(root, read);
  const cases = casesOf(root, rels, read);
  const assertions = assertionsOf(ck, cases);
  const prodRels = producerFiles(root, read);
  const producerUnits = [];
  for (const r of prodRels) { const f = files.get(r); if (f) producerUnits.push(...f.units); }
  linkAssertions(assertions, producerUnits);
  for (const a of assertions) {
    const f = files.get(a.file);
    const u = f && f.byId.get(a.unit);
    if (u) { u.assertion = a; a.item = keyOf(a.file, a.unit); }
  }
  for (const f of files.values()) for (const u of f.units) {
    if (f.lang === 'json') { if (u.assertion) u.kind = 'assertion'; else u.kind = kindOfUnit(f.rel, 'json', u, ck); }
    else u.kind = u.kind === 'regex' ? 'regex' : kindOfUnit(f.rel, f.lang, u, ck);
  }
  const model = { root, rev, cfg, rels, read, files, ck, cases, assertions, producerRels: prodRels, producerUnits };
  model.probes = opts.noItalian ? [] : probesOf(model);
  return model;
}

// The consumer unit of every entry of the consumed-strings inventory
function consumersOf(model) {
  const blob = model.read('Checks/language/consumed-strings.json');
  if (blob === null) return [];
  const inv = JSON.parse(blob.toString('utf8').replace(/^﻿/, ''));
  const out = [];
  for (const e of inv.entries) {
    const c = e.consumer;
    const f = model.files.get(c.file);
    const lang = langOf(c.file);
    if (!f || !lang || lang === 'json') { out.push({ entry: e, unit: null, why: 'consumer file not scanned' }); continue; }
    const via = c.via ? { name: c.via, kind: c.kind, nocase: c.nocase } : null;
    const { sites } = CONS.sitesOf(f.text, lang, via);
    const site = sites.find(s => CONS.hashOf(s.value) === c.sha1 && (!c.kind || s.kind === c.kind));
    if (!site) { out.push({ entry: e, unit: null, why: 'the consumed literal is not in ' + c.file }); continue; }
    const starts = lineStarts(f.text);
    const unit = f.units.find(u => u.leaves.some(l => l.k === 't' && l.cooked === site.value && lineAt(starts, l.s) === site.line));
    out.push({ entry: e, unit: unit || null, site, why: unit ? null : 'no unit for the consumed literal at ' + c.file + ':' + site.line });
  }
  return out;
}

// Everything the tool does with a tree before the chunks: the items to translate
function collectItems(model) {
  const items = new Map();   // key -> item
  const deferred = new Map();
  const unsupported = new Map();
  const forced = integrationOf(model.root).forced;
  for (const f of model.files.values()) {
    for (const u of f.units) {
      if (!u.italian && !forced.has(keyOf(f.rel, u.id))) continue;
      const d = describeUnit(u);
      const item = Object.assign({ key: keyOf(f.rel, u.id), file: f.rel, lang: f.lang, unit: u.id, line: u.line, endLine: u.endLine,
        kind: u.kind, cls: classOf(u.kind), ctx: u.ctx, to: u.to, words: u.words, assertionRef: u.assertion || null,
        satisfies: [], consumedBy: [], consumer: null, unsupported: d.fixed.length === d.parts.length }, d);
      if (inCatalogue(f.rel)) { deferred.set(item.key, item); item.deferred = 'catalogue (.9)'; continue; }
      if (item.unsupported) { unsupported.set(item.key, item); continue; }
      items.set(item.key, item);
    }
  }
  // the literals inside the holes of a message belong to it (`n == 1 ? " word" : " words"`): a lone word the gate reads as a name
  // is still Italian in the output. They follow the message, and the translator may keep them.
  const links = [];   // [key, key]: items that change in the same run
  const queue = [...items.values()].filter(i => i.lang !== 'json');
  while (queue.length) {
    const host = queue.pop();
    const f = model.files.get(host.file);
    const hu = f.byId.get(host.unit);
    const holes = hu.leaves.filter(l => l.k === 'h');
    if (!holes.length) continue;
    for (const nu of f.units) {
      if (nu === hu || inCatalogue(f.rel)) continue;
      const first = nu.leaves[0].s;
      if (!holes.some(h => first >= h.s && first < h.e)) continue;
      if (items.has(keyOf(f.rel, nu.id))) { links.push([host.key, keyOf(f.rel, nu.id)]); continue; }
      if (!nu.leaves.some(l => l.k === 't' && /[A-Za-z\u00c0-\u00ff]{3,}/.test(l.cooked))) continue;
      const d = describeUnit(nu);
      if (d.fixed.length === d.parts.length) continue;
      const it = Object.assign({ key: keyOf(f.rel, nu.id), file: f.rel, lang: f.lang, unit: nu.id, line: nu.line, endLine: nu.endLine, kind: 'text', cls: 'message',
        ctx: nu.ctx, to: nu.to, words: 0, assertionRef: null, satisfies: [], consumedBy: [], consumer: null, unsupported: false, follows: true, nested_in: host.unit, hostKey: host.key }, d);
      items.set(it.key, it);
      links.push([host.key, it.key]);
      queue.push(it);
    }
  }
  // the other arguments of a format string (a string.Format whose arguments hold literals) are part of the message too
  for (const host of [...items.values()].filter(i => i.lang === 'cs')) {
    const f = model.files.get(host.file);
    const hu = f.byId.get(host.unit);
    if (!hu.inv || !hu.leaves.some(l => l.k === 't' && PLACEHOLDER.test(l.cooked))) continue;
    for (const nu of f.units) {
      if (nu === hu || inCatalogue(f.rel)) continue;
      const first = nu.leaves[0].s;
      if (!(first >= hu.inv[0] && first < hu.inv[1])) continue;
      if (items.has(keyOf(f.rel, nu.id))) { links.push([host.key, keyOf(f.rel, nu.id)]); continue; }
      if (!nu.leaves.some(l => l.k === 't' && /[A-Za-z\u00c0-\u00ff]{3,}/.test(l.cooked))) continue;
      const d = describeUnit(nu);
      if (d.fixed.length === d.parts.length) continue;
      const it = Object.assign({ key: keyOf(f.rel, nu.id), file: f.rel, lang: f.lang, unit: nu.id, line: nu.line, endLine: nu.endLine, kind: 'text', cls: 'message',
        ctx: nu.ctx, to: nu.to, words: 0, assertionRef: null, satisfies: [], consumedBy: [], consumer: null, unsupported: false, follows: true, argument_of: host.unit, hostKey: host.key }, d);
      items.set(it.key, it);
      links.push([host.key, it.key]);
    }
  }
  const consumers = consumersOf(model);
  for (const c of consumers) {
    if (!c.unit) continue;
    // the producer units that hold the text the consumer looks for
    const runs = [].concat(...CONS.variantsOf(c.site));
    const produced = [];
    for (const p of c.entry.producers) {
      const pf = model.files.get(p);
      if (!pf) continue;
      for (const pu of pf.units) {
        const hay = pu.leaves.filter(l => l.k !== 'h').map(l => (c.site.nocase ? l.cooked.toLowerCase() : l.cooked));
        if (runs.some(r => hay.some(h => h.includes(c.site.nocase ? r.toLowerCase() : r)))) {
          const k = keyOf(p, pu.id);
          if (items.has(k)) produced.push(k);
        }
      }
    }
    const ck = keyOf(c.entry.consumer.file, c.unit.id);
    let it = items.get(ck);
    if (!it) {
      // a consumer the gate does not read as Italian still follows the producers that are translated
      const f = model.files.get(c.entry.consumer.file);
      if (!produced.length || inCatalogue(c.entry.consumer.file)) continue;
      const d = describeUnit(c.unit);
      it = Object.assign({ key: ck, file: f.rel, lang: f.lang, unit: c.unit.id, line: c.unit.line, endLine: c.unit.endLine, kind: c.unit.kind, cls: classOf(c.unit.kind),
        ctx: c.unit.ctx, to: c.unit.to, words: 0, assertionRef: null, satisfies: [], consumedBy: [], consumer: null, unsupported: false, follows: true }, d);
      items.set(ck, it);
    }
    it.cls = 'consumer'; it.kind = 'consumer';
    it.consumer = { id: c.entry.id, producers: c.entry.producers, kind: c.entry.consumer.kind, units: produced };
    for (const k of produced) { const p = items.get(k); if (p && p !== it) p.consumedBy.push(ck); }
  }
  const italianKey = (k) => items.has(k);
  for (const a of model.assertions) {
    let it = items.get(a.item);
    const sat = a.satisfiers.map(k => {
      const file = k.slice(0, k.indexOf('#'));
      return { key: k, file, unit: k.slice(k.indexOf('#') + 1), catalogue: inCatalogue(file), italian: italianKey(k) };
    });
    if (!it) {
      // a word the gate reads as a name (one word, no spaces) still follows the message it asserts
      const live0 = sat.filter(s => !s.catalogue);
      if (!(live0.length && live0.length <= K_NARROW && live0.some(s => s.italian))) continue;
      const f = model.files.get(a.file);
      const u = f && f.byId.get(a.unit);
      if (!u) continue;
      it = Object.assign({ key: a.item, file: a.file, lang: 'json', unit: a.unit, line: u.line, endLine: u.endLine, kind: 'assertion', cls: 'assertion', ctx: [], to: u.to,
        words: 0, assertionRef: a, satisfies: [], consumedBy: [], consumer: null, unsupported: false, follows: true }, describeUnit(u));
      items.set(a.item, it);
    }
    it.satisfiers = sat;
    it.how = a.how;
    const live = sat.filter(s => !s.catalogue);
    const liveItalian = live.filter(s => s.italian);
    if (!sat.length) it.home = 'free';
    else if (sat.every(s => s.catalogue)) it.home = 'deferred';
    else if (!liveItalian.length) it.home = 'free';
    else if (live.length > K_NARROW) it.home = 'wide';
    else it.home = 'cluster';
    // a narrow assertion changes in the same run as its messages; a wide one has the weak guard and its own last chunk
    if (it.home === 'cluster') for (const s of liveItalian) { const p = items.get(s.key); if (p) p.satisfies.push(it.key); }
  }
  // the probes: a program that looks for the text of a message follows it (a narrow one in the same cluster).
  // A probe whose own words are Italian is an item, translated with its messages; one that is not ("volume",
  // «slot») stays as it is and only guards: a run that would take its text out of every message is refused.
  const inventoryUnits = new Set(consumers.filter(c => c.unit).map(c => keyOf(c.entry.consumer.file, c.unit.id)));
  const probes = new Map();
  for (const pr of model.probes || []) {
    if (inventoryUnits.has(pr.key)) continue;   // an entry of the inventory: coupled above
    probes.set(pr.key, pr);
    const words = pr.how === 'regex' || pr.how === 'like' ? pr.value.replace(/\\[A-Za-z]/g, ' ').replace(/[^\p{L}' ]+/gu, ' ') : pr.value;
    pr.italian = DET.proseVerdict(words, model.cfg).italian;
    let it = items.get(pr.key);
    if (!it && pr.italian) {
      const f = model.files.get(pr.file);
      const u = f.byId.get(pr.unit);
      const d = describeUnit(u);
      if (d.fixed.length !== d.parts.length) {
        it = Object.assign({ key: pr.key, file: pr.file, lang: f.lang, unit: pr.unit, line: u.line, endLine: u.endLine, kind: 'probe', cls: 'message',
          ctx: u.ctx, to: u.to, words: 0, assertionRef: null, satisfies: [], consumedBy: [], consumer: null, unsupported: false, follows: true }, d);
        items.set(pr.key, it);
      }
    }
    if (it) it.probe = { how: pr.how, nocase: pr.nocase, neg: pr.neg, home: pr.home, value: pr.value, producers: pr.producers };
    for (const q of pr.producers) {
      const pi = items.get(q.key);
      if (!pi || pi === it) continue;
      (pi.probedBy || (pi.probedBy = [])).push(pr.key);
      if (it && pr.home === 'narrow') links.push([pr.key, q.key]);
    }
  }
  return { items, deferred, consumers, unsupported, links, probes };
}

// ── clusters and chunks ─────────────────────────────────────────────────────
function folderOf(rel) { return rel.includes('/') ? rel.split('/')[0] : '(root)'; }
function slugOf(folder) { return folder === '(root)' ? 'root' : folder.toLowerCase().replace(/^\.+/, '').replace(/[^a-z0-9]+/g, '-').replace(/-+$/, ''); }

class UF {
  constructor() { this.p = new Map(); }
  add(x) { if (!this.p.has(x)) this.p.set(x, x); }
  find(x) { this.add(x); while (this.p.get(x) !== x) { this.p.set(x, this.p.get(this.p.get(x))); x = this.p.get(x); } return x; }
  union(a, b) { this.p.set(this.find(a), this.find(b)); }
}

// A chunk is { id, folder, tag, items }. The rules:
//  - the units that satisfy a narrow assertion, the assertion and its units are
//    one cluster, translated and applied together;
//  - a consumer goes with the producers of its entry;
//  - a cluster inside one folder keeps its source files in one chunk; a cluster
//    that spans folders becomes a `linked` chunk (the items leave their files:
//    this is the one place where a source file is split, by units);
//  - the prose of the cases goes by family of cases; a case file that holds an
//    assertion of a cluster is in two chunks (the assertion with its message);
//  - wide assertions (more than K_NARROW satisfier units) go last, in a chunk
//    of their own, with the weak guard (at least one satisfier).
function planChunks(model, coll, opts) {
  opts = opts || {};
  const max = opts.max || MAX_WORDS;
  const items = coll.items;
  const uf = new UF();
  for (const it of items.values()) uf.add(it.key);
  for (const [a, b] of coll.links || []) if (items.has(a) && items.has(b)) uf.union(a, b);   // a literal inside the holes or the arguments of a message goes with it
  for (const it of items.values()) {
    if (it.home !== 'cluster') continue;
    for (const s of it.satisfiers) if (s.italian && !s.catalogue) uf.union(it.key, s.key);
  }
  for (const it of items.values()) {
    if (!it.consumer) continue;
    for (const p of it.consumer.units) uf.union(it.key, p);
  }
  const comps = new Map();
  for (const it of items.values()) { const r = uf.find(it.key); if (!comps.has(r)) comps.set(r, []); comps.get(r).push(it); }
  const linkedKeys = new Set();
  const linked = [];
  for (const list of comps.values()) {
    const folders = new Set(list.filter(i => i.lang !== 'json').map(i => folderOf(i.file)));
    if (folders.size > 1) { linked.push(list); for (const i of list) linkedKeys.add(i.key); }
  }
  // source files of one folder joined by a cluster
  const fileUF = new UF();
  for (const list of comps.values()) {
    const srcs = list.filter(i => i.lang !== 'json' && !linkedKeys.has(i.key));
    for (const i of srcs) fileUF.add(i.file);
    for (const i of srcs.slice(1)) fileUF.union(srcs[0].file, i.file);
  }
  const folderAtoms = new Map();
  const addToFolder = (file, it) => {
    const folder = folderOf(file);
    if (!folderAtoms.has(folder)) folderAtoms.set(folder, new Map());
    const m = folderAtoms.get(folder);
    const r = fileUF.find(file);
    if (!m.has(r)) m.set(r, { files: new Set(), items: [] });
    const a = m.get(r);
    a.files.add(file); a.items.push(it);
  };
  const caseGroups = new Map();
  const wide = [];
  for (const it of items.values()) {
    if (linkedKeys.has(it.key)) continue;
    if (it.probe && it.probe.home === 'wide') { wide.push(it); continue; }   // like a wide assertion: last, once its messages are
    if (it.lang !== 'json') { addToFolder(it.file, it); continue; }
    if (it.cls === 'assertion') {
      if (it.home === 'cluster') {
        const s = it.satisfiers.find(x => x.italian && !x.catalogue);
        addToFolder(s.file, it);
        continue;
      }
      if (it.home === 'wide') { wide.push(it); continue; }
      if (it.home === 'deferred') continue;
    }
    const fam = it.file.split('/')[2] || 'cases';
    if (!caseGroups.has(fam)) caseGroups.set(fam, []);
    caseGroups.get(fam).push(it);
  }
  const chunks = [];
  const wordsOf = (list) => list.reduce((n, i) => n + i.words, 0);
  const ordered = (list) => list.slice().sort((x, y) => x.file.localeCompare(y.file) || x.line - y.line);
  const pack = (name, folderLabel, atoms, tag) => {
    const groups = [];
    let cur = [], w = 0;
    for (const a of atoms) {
      const aw = wordsOf(a.items);
      if (cur.length && w + aw > max) { groups.push(cur); cur = []; w = 0; }
      cur.push(a); w += aw;
    }
    if (cur.length) groups.push(cur);
    if (groups.length > 1) {
      const last = groups[groups.length - 1], before = groups[groups.length - 2];
      const gw = (g) => g.reduce((n, a) => n + wordsOf(a.items), 0);
      if (gw(last) < max / 4 && gw(last) + gw(before) <= max * 1.2) { before.push(...last); groups.pop(); }
    }
    groups.forEach((g, i) => chunks.push({ id: name + '-' + String(i + 1).padStart(2, '0'), folder: folderLabel, tag: tag || null, items: ordered([].concat(...g.map(a => a.items))) }));
  };
  for (const [folder, m] of [...folderAtoms.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
    const list = [...m.values()].map(a => ({ files: [...a.files].sort(), items: ordered(a.items) }));
    list.sort((a, b) => a.files[0].localeCompare(b.files[0]));
    pack(slugOf(folder), folder, list);
  }
  // --pilot Bridge,Installer: the linked clusters that touch those folders are a chunk of their own
  const pilot = new Set(opts.pilot || []);
  const touches = (l) => l.some(i => pilot.has(folderOf(i.file)));
  for (const [name, group] of [['linked-pilot', linked.filter(l => pilot.size && touches(l))], ['linked', linked.filter(l => !(pilot.size && touches(l)))]]) {
    if (!group.length) continue;
    const list = group.map(l => ({ files: [...new Set(l.map(i => i.file))].sort(), items: ordered(l) }));
    list.sort((a, b) => a.files[0].localeCompare(b.files[0]));
    pack(name, 'linked', list);
  }
  const famList = [...caseGroups.entries()].sort((a, b) => a[0].localeCompare(b[0])).map(([fam, l]) => ({ files: [...new Set(l.map(i => i.file))].sort(), items: ordered(l) }));
  pack('cases', 'Bench/cases', famList);
  if (wide.length) pack('assertions-wide', 'Bench/cases', [{ files: [...new Set(wide.map(i => i.file))].sort(), items: ordered(wide) }], 'wide');
  return chunks;
}

Object.assign(module.exports, { buildModel, collectItems, planChunks, consumersOf, describeUnit, keyOf, readerOf, folderOf, slugOf, listFiles, analyzeFiles, detector, inCatalogue, git, readBlobs, decodeSource, readSource, writeSource, cannot, Stop });

// ── --extract ───────────────────────────────────────────────────────────────
// the units other than the item itself, in the same JSON file, that hold its text
function echoesOf(model, item) {
  const f = model.files.get(item.file);
  const old = item.parts.join('').trim();
  if (!f || f.lang !== 'json' || old.length < 3) return [];
  const needle = old.toLowerCase();
  const out = [];
  for (const u of f.units) {
    if (u.id === item.unit) continue;
    const text = u.leaves.map(l => l.cooked).join('').toLowerCase();
    if (text.includes(needle)) out.push(u.id);
  }
  return out;
}

function chunkJson(model, coll, chunk, idOf, base) {
  let n = 0;
  const idByKey = new Map();
  for (const it of chunk.items) idByKey.set(it.key, 'i' + String(++n).padStart(4, '0'));
  const showOf = (key) => {
    const file = key.slice(0, key.indexOf('#')), unit = key.slice(key.indexOf('#') + 1);
    const f = model.files.get(file);
    const u = f && f.byId.get(unit);
    return u ? describeUnit(u).show : null;
  };
  const items = chunk.items.map(it => {
    const o = { id: idByKey.get(it.key), file: it.file, unit: it.unit, kind: it.kind, class: it.cls, line: it.line, end_line: it.endLine, words: it.words,
      parts: it.parts, fixed: it.fixed, holes: it.holes, hole_sk: it.holeSk, show: it.show };
    if (it.follows) o.follows = it.nested_in ? 'hole of ' + it.nested_in : it.argument_of ? 'argument of ' + it.argument_of : it.probe ? 'probe of its messages' : it.consumer ? 'consumer of its producers' : 'assertion of its messages';
    if (it.ctx && it.ctx.length) o.ctx = it.ctx;
    if (it.to) o.to = it.to;
    if (it.consumer) o.consumer = { id: it.consumer.id, kind: it.consumer.kind, produced_by: it.consumer.units.map(k => ({ file: k.slice(0, k.indexOf('#')), unit: k.slice(k.indexOf('#') + 1), chunk: idOf.get(k) || null, show: showOf(k) })) };
    if (it.probe) o.probe = { how: it.probe.how, nocase: it.probe.nocase, neg: it.probe.neg, home: it.probe.home, value: it.probe.value,
      producers: it.probe.producers.map(p => ({ file: p.file, unit: p.unit, italian: p.italian, chunk: idOf.get(p.key) || null, show: p.italian ? showOf(p.key) : undefined })) };
    if (it.probedBy && it.probedBy.length) o.probed_by = it.probedBy.map(k => { const pi = coll.probes.get(k); return { item: idByKey.get(k) || null, file: pi.file, unit: pi.unit, chunk: idOf.get(k) || null, value: pi.value, how: pi.how, nocase: pi.nocase, neg: pi.neg, home: pi.home, translatable: pi.italian, producers: pi.producers.map(p => ({ file: p.file, unit: p.unit })) }; });
    if (it.consumedBy.length) o.consumed_by = it.consumedBy.map(k => ({ item: idByKey.get(k) || null, file: k.slice(0, k.indexOf('#')), unit: k.slice(k.indexOf('#') + 1), chunk: idOf.get(k) || null }));
    if (it.satisfies.length) o.satisfies = it.satisfies.map(k => { const a = coll.items.get(k); return { item: idByKey.get(k) || null, file: a.file, unit: a.unit, chunk: idOf.get(k) || null, kind: a.assertionRef.kind, value: a.parts[0] }; });
    if (it.cls === 'assertion') {
      o.assertion = { kind: it.assertionRef.kind, key: it.assertionRef.key, operation: it.assertionRef.operation || null, case: it.assertionRef.case, home: it.home, how: it.how || null };
      o.satisfiers = it.satisfiers.map(s => ({ file: s.file, unit: s.unit, chunk: idOf.get(s.key) || null, italian: s.italian, catalogue: s.catalogue, equal: !!(it.assertionRef.equal || []).includes(s.key), show: showOf(s.key) }));
    }
    if (it.cls === 'data' || (it.cls === 'assertion' && it.home === 'free')) {
      const e = echoesOf(model, it);
      if (e.length) o.echoes = e;
    }
    return o;
  });
  const byFile = new Map();
  for (const it of chunk.items) {
    if (!byFile.has(it.file)) byFile.set(it.file, { file: it.file, sha256: model.files.get(it.file).sha, language: it.lang, items: 0, words: 0 });
    const f = byFile.get(it.file);
    f.items++; f.words += it.words;
  }
  return { id: chunk.id, folder: chunk.folder, tag: chunk.tag, base: base || null,
    files: [...byFile.values()].sort((a, b) => a.file.localeCompare(b.file)), items, words: chunk.items.reduce((s, i) => s + i.words, 0) };
}

// the gate's string hits in files this tool cannot scan (YAML, shell, XML...)
function outsideTool(model) {
  const out = [];
  const list = git(model.root, ['ls-files', '-z']).toString('utf8').split('\0').filter(Boolean);
  for (const rel of list) {
    if (DET.isBinaryByExtension(rel) || model.files.has(rel)) continue;
    const kind = DET.kindOf(rel);
    if (!['yaml', 'hash', 'xml', 'ignore'].includes(kind) && !(kind === 'json' && !inCatalogue(rel) && !/^Bench\/(tasks|report|data)\//.test(rel) && !/^Checks\/language\//.test(rel))) continue;
    let text;
    try { text = readSource(model.root, rel).text; } catch (e) { continue; }
    let a;
    try { a = DET.analyzeText(rel, text, model.cfg); } catch (e) { continue; }
    const cat = kind === 'json' ? 'json_values' : 'strings';
    const words = a.hits.filter(h => h.category === cat).reduce((n, h) => n + (h.score || 1), 0);
    if (words) out.push({ file: rel, kind, category: cat, words });
  }
  return out;
}

function runExtract(root, dir, opts) {
  const base = git(root, ['rev-parse', 'HEAD']).toString('utf8').trim();
  const dirty = git(root, ['status', '--porcelain', '--untracked-files=no']).toString('utf8').trim();
  if (dirty) console.log('window 5: the working tree has uncommitted changes; the chunks are cut from the files on disk, the base recorded is ' + base);
  const model = buildModel(root, null);
  const coll = collectItems(model);
  const chunks = planChunks(model, coll, { max: opts.max, pilot: opts.pilot });
  const idOf = new Map();
  for (const c of chunks) for (const it of c.items) idOf.set(it.key, c.id);
  fs.mkdirSync(dir, { recursive: true });
  const written = [];
  for (const c of chunks) {
    const j = chunkJson(model, coll, c, idOf, base);
    fs.writeFileSync(path.join(dir, c.id + '.json'), JSON.stringify(j, null, 1) + '\n');
    written.push(j);
  }
  const unsupported = [...coll.unsupported.values()].map(i => ({ file: i.file, unit: i.unit, show: i.show.slice(0, 120) }));
  const unmapped = [];
  for (const f of model.files.values()) for (const u of f.unmapped) unmapped.push({ file: f.rel, line: u.line, words: u.words });
  const deferredFiles = new Map();
  for (const it of coll.deferred.values()) deferredFiles.set(it.file, (deferredFiles.get(it.file) || 0) + it.words);
  const deferredAssertions = [...coll.items.values()].filter(i => i.cls === 'assertion' && i.home === 'deferred');
  const requires = (cj) => {
    const r = new Set();
    for (const it of cj.items) {
      for (const s of it.satisfiers || []) if (s.chunk && s.chunk !== cj.id) r.add(s.chunk);
      if (it.consumer) for (const p of it.consumer.produced_by) if (p.chunk && p.chunk !== cj.id) r.add(p.chunk);
      for (const s of it.satisfies || []) if (s.chunk && s.chunk !== cj.id) r.add(s.chunk);
      if (it.probe) for (const p of it.probe.producers) if (p.chunk && p.chunk !== cj.id) r.add(p.chunk);
    }
    return [...r].sort();
  };
  const plan = {
    base, created: new Date().toISOString(), target_words: TARGET_WORDS, max_words: opts.max || MAX_WORDS, k_narrow: K_NARROW, pilot: opts.pilot || [],
    totals: { items: coll.items.size, words: written.reduce((n, c) => n + c.words, 0), chunks: written.length,
      assertions: [...coll.items.values()].filter(i => i.cls === 'assertion').length,
      assertions_all: model.assertions.length, consumers: [...coll.items.values()].filter(i => i.consumer).length },
    chunks: written.map(c => ({ id: c.id, folder: c.folder, tag: c.tag, words: c.words, items: c.items.length,
      strings: c.items.filter(i => i.class === 'message' || i.class === 'consumer').length,
      prose: c.items.filter(i => i.class === 'prose').length, data: c.items.filter(i => i.class === 'data').length,
      assertions: c.items.filter(i => i.class === 'assertion').length, consumers: c.items.filter(i => i.class === 'consumer').length,
      files: c.files.length, requires: requires(c) })),
    deferred: { catalogue_words: [...deferredFiles.values()].reduce((a, b) => a + b, 0), files: Object.fromEntries(deferredFiles), assertions: deferredAssertions.map(i => ({ file: i.file, unit: i.unit })) },
    unsupported, unmapped, outside_tool: outsideTool(model),
    unresolved_consumers: coll.consumers.filter(c => !c.unit).map(c => ({ id: c.entry.id, why: c.why })),
  };
  fs.writeFileSync(path.join(dir, 'plan.json'), JSON.stringify(plan, null, 1) + '\n');
  console.log('extracted ' + plan.totals.chunks + ' chunks, ' + plan.totals.items + ' items, ' + plan.totals.words + ' Italian words (base ' + base.slice(0, 7) + ') into ' + dir);
  for (const c of plan.chunks) console.log('  ' + c.id.padEnd(26) + c.folder.padEnd(16) + String(c.words).padStart(6) + ' words ' + String(c.items).padStart(5) + ' items ' + String(c.assertions).padStart(4) + ' assertions ' + String(c.files).padStart(4) + ' files' + (c.requires.length ? '  requires ' + c.requires.join(',') : ''));
  console.log('deferred to the catalogue (.9): ' + plan.deferred.catalogue_words + ' words in ' + deferredFiles.size + ' files, ' + deferredAssertions.length + ' assertions');
  if (unsupported.length) console.log('UNSUPPORTED units (only fixed text): ' + unsupported.length);
  if (unmapped.length) console.log('UNMAPPED: ' + unmapped.length + ' hit lines no unit explains');
  if (plan.outside_tool.length) console.log('OUTSIDE THE TOOL (YAML, shell, XML, other JSON): ' + plan.outside_tool.length + ' files, ' + plan.outside_tool.reduce((n, o) => n + o.words, 0) + ' words');
  return 0;
}

Object.assign(module.exports, { runExtract, chunkJson, echoesOf });

// ── the invariants of a translation ─────────────────────────────────────────
function sortedCopy(a) { return a.slice().sort(); }
function countMap(list) { const m = new Map(); for (const x of list) m.set(x, (m.get(x) || 0) + 1); return m; }
function diffList(a, b) {
  const A = countMap(a), B = countMap(b);
  const missing = [], extra = [];
  for (const [k, n] of A) for (let i = B.get(k) || 0; i < n; i++) missing.push(k);
  for (const [k, n] of B) for (let i = A.get(k) || 0; i < n; i++) extra.push(k);
  return { missing, extra };
}
const UNIT_WORDS = '(?:mm|cm|m|km|kg|g|N|Nm|MPa|GPa|kPa|deg|rad|px|dpi|ms|s|kB|MB|GB|Hz|V|A|W|mm2|mm3|cm2|cm3|m2|m3)';
function extraFacts(parts) {
  const text = parts.join('\u0001');
  return {
    placeholders: (text.match(/\{[^{}]*\}/g) || []).map(s => s.replace(/\s+/g, '')),
    escapes: (text.match(/[\n\r\t]/g) || []).map(c => JSON.stringify(c)),
    units: (text.match(new RegExp('\\d(?:\\s?)(?:' + UNIT_WORDS + '|%|\\u00b0)(?![A-Za-z])', 'g')) || []).map(s => s.replace(/^\d\s?/, '')),
  };
}

// The invariants between the parts of a message before and after a translation.
function invariantProblems(oldParts, newParts) {
  const problems = [];
  const fo = W4.factsOf(oldParts.join('\n').split('\n')), fn = W4.factsOf(newParts.join('\n').split('\n'));
  // <word> in a message is a placeholder written for a person (<path>, <folder>): prose, translated with the rest.
  // An XML tag with attributes, a closing tag or an empty element is markup: it stays.
  const markup = (list) => list.filter(t => /=|\/>|^<\//.test(t));
  fo.tags = markup(fo.tags); fn.tags = markup(fn.tags);
  for (const k of ['numbers', 'references', 'backticks', 'files', 'urls', 'tags', 'names', 'markers', 'quoteNumbers']) {
    const d = diffList(fo[k], fn[k]);
    if (d.missing.length || d.extra.length)
      problems.push('the ' + k + ' changed' + (d.missing.length ? ', lost ' + JSON.stringify(d.missing.slice(0, 6)) : '') + (d.extra.length ? ', added ' + JSON.stringify(d.extra.slice(0, 6)) : ''));
  }
  if (fo.guillemets !== fn.guillemets) problems.push('the guillemets changed (opening,closing counts ' + fo.guillemets + ' before, ' + fn.guillemets + ' after)');
  const eo = extraFacts(oldParts), en = extraFacts(newParts);
  for (const k of Object.keys(eo)) {
    const d = diffList(eo[k], en[k]);
    if (d.missing.length || d.extra.length)
      problems.push('the ' + k + ' changed' + (d.missing.length ? ', lost ' + JSON.stringify(d.missing.slice(0, 6)) : '') + (d.extra.length ? ', added ' + JSON.stringify(d.extra.slice(0, 6)) : ''));
  }
  return problems;
}

// the space (or not) on the two sides of every internal boundary between parts
function edgeProblems(oldParts, newParts) {
  const problems = [];
  const sp = (s, end) => (end ? /\s$/.test(s) : /^\s/.test(s));
  for (let i = 0; i + 1 < oldParts.length; i++) {
    if (oldParts[i] === '' || oldParts[i + 1] === '' || newParts[i] === '' || newParts[i + 1] === '') continue;
    if (sp(oldParts[i], true) !== sp(newParts[i], true)) problems.push('the end of part ' + (i + 1) + (sp(oldParts[i], true) ? ' had a space and has none' : ' had no space and has one') + ' (a hole or another part follows it; "edges_ok": true if it is meant)');
    if (sp(oldParts[i + 1], false) !== sp(newParts[i + 1], false)) problems.push('the start of part ' + (i + 2) + (sp(oldParts[i + 1], false) ? ' had a space and has none' : ' had no space and has one') + ' (a hole or another part precedes it; "edges_ok": true if it is meant)');
  }
  return problems;
}

// What of a regular expression is not a letter: the escapes, the classes, the
// groups, the quantifiers. A translation may change the words, not this.
function regexSkeleton(src) {
  let out = '';
  for (let i = 0; i < src.length; i++) {
    const c = src[i];
    if (c === '\\') { out += c + (src[i + 1] || ''); i++; continue; }
    if (c === '[') {
      let j = i + 1;
      if (src[j] === '^') j++;
      if (src[j] === ']') j++;
      while (j < src.length && src[j] !== ']') { if (src[j] === '\\') j++; j++; }
      out += src.slice(i, j + 1); i = j; continue;
    }
    if (c === '(' && src[i + 1] === '?') {
      let j = i + 2;
      while (j < src.length && /[:=!<>A-Za-z'P-]/.test(src[j]) && src[j] !== ')') { j++; if (/[:=!]/.test(src[j - 1]) && j - 1 > i + 1) break; if (src[j - 1] === '>') break; }
      out += src.slice(i, j); i = j - 1; continue;
    }
    if (/[A-Za-zÀ-ÿ]/.test(c)) { out += '\u0001'; continue; }
    out += c;
  }
  // a run of words (letters, spaces, an apostrophe or a hyphen between letters) is one mark: a translation changes the words and their number
  return out.replace(/[\u0001 '-]*\u0001[\u0001 '-]*/g, '\u0001');
}

function problemsOfTranslation(item, tr) {
  const out = [];
  const newParts = tr.parts;
  if (!Array.isArray(newParts) || newParts.some(p => typeof p !== 'string')) return ['"parts" is not a list of strings'];
  if (newParts.length !== item.parts.length) return ['the number of parts changed: ' + item.parts.length + ' before, ' + newParts.length + ' after (a hole stays where it is, so the parts stay as many)'];
  for (const i of item.fixed) if (newParts[i] !== item.parts[i]) out.push('part ' + (i + 1) + ' is fixed text and changed');
  if (item.fixed.length === item.parts.length) return out;
  if (newParts.every((p, i) => p === item.parts[i])) return out;
  out.push(...invariantProblems(item.parts, newParts));
  if (!tr.edges_ok) out.push(...edgeProblems(item.parts, newParts));
  if ((item.class === 'regex' || (item.class === 'consumer' && item.consumer && item.consumer.kind === 'regex')) && regexSkeleton(item.parts.join('')) !== regexSkeleton(newParts.join(''))) {
    out.push('the skeleton of the regular expression changed (only letters may): ' + JSON.stringify(regexSkeleton(item.parts.join(''))) + ' before, ' + JSON.stringify(regexSkeleton(newParts.join(''))) + ' after');
  }
  if (item.class === 'data' && !tr.data_ok) out.push('a value that the case writes into the program is data: it changes only with "data_ok": true, and every echo of it with it');
  return out;
}

// ── loading the chunks of a run ─────────────────────────────────────────────
function loadChunks(dir, only) {
  const out = [];
  for (const name of fs.readdirSync(dir).sort()) {
    if (!name.endsWith('.json') || name === 'plan.json' || name.endsWith('.en.json') || name.startsWith('memory')) continue;
    const id = name.slice(0, -5);
    if (only && !only.includes(id)) continue;
    const chunk = JSON.parse(fs.readFileSync(path.join(dir, name), 'utf8'));
    const tfile = path.join(dir, id + '.en.json');
    let translated = null;
    if (fs.existsSync(tfile)) {
      try { translated = JSON.parse(fs.readFileSync(tfile, 'utf8')); } catch (e) { cannot(tfile + ' is not valid JSON: ' + e.message); }
    }
    out.push({ id, chunk, translated });
  }
  return out;
}

// ── --apply ─────────────────────────────────────────────────────────────────
const textLeaves = (u) => u.leaves.filter(l => l.k === 't' || l.k === 'f');
const holesOf = (u) => u.leaves.filter(l => l.k === 'h').map(l => l.sk);
const sameList = (a, b) => a.length === b.length && a.every((x, i) => x === b[i]);

function withParts(u, parts) {
  let i = 0;
  return Object.assign({}, u, { leaves: u.leaves.map(l => (l.k === 'h' ? l : Object.assign({}, l, { cooked: parts[i++] }))) });
}

// a deep comparison of two parsed JSON documents, the values at `allowed` paths aside
function jsonDiff(a, b, p, allowed, out) {
  if (typeof a !== typeof b || Array.isArray(a) !== Array.isArray(b) || (a === null) !== (b === null)) { out.push(p || '$'); return; }
  if (a && typeof a === 'object') {
    const ka = Object.keys(a), kb = Object.keys(b);
    if (!sameList(ka, kb)) { out.push((p || '$') + ' (keys)'); return; }
    for (const k of ka) jsonDiff(a[k], b[k], Array.isArray(a) ? (p || '$') + '[' + k + ']' : (p ? p + '.' + k : '$.' + k), allowed, out);
    return;
  }
  if (a !== b && !allowed.has(p)) out.push(p);
}

// true when `text` is wholly a name or phrase that an Italian SOLIDWORKS writes: a solidworks-localized exception of exceptions.json
// (a pattern that covers the whole text, for the file the text is in)
function localizedByThePlatform(root, rel, text) {
  let list;
  try { list = JSON.parse(fs.readFileSync(path.join(root, 'Checks', 'language', 'exceptions.json'), 'utf8').replace(/^\ufeff/, '')); } catch (e) { return false; }
  return list.some(e => {
    if (e.class !== 'solidworks-localized' || !e.pattern) return false;
    if (e.file && !new RegExp(e.file).test(rel)) return false;
    const m = new RegExp('^(?:' + e.pattern + ')$', 'i').exec(text);
    return !!m;
  });
}

// list: [{ id, chunk, translated }] applied together. Returns { problems, files, warnings, wrote }
function applyRun(root, list, opts) {
  opts = opts || {};
  const problems = [];
  const warnings = [];
  const fail = (where, msg) => problems.push({ where, msg });
  // 1. the translations, by item
  const tr = new Map();   // key -> { chunk, item, parts, tr }
  const missing = [];
  for (const { id, chunk, translated } of list) {
    if (!translated) { fail(id, 'no translation yet (' + id + '.en.json)'); continue; }
    if (translated.id !== chunk.id) { fail(id, 'the translated chunk is ' + translated.id); continue; }
    const byId = new Map((translated.items || []).map(t => [t.id, t]));
    for (const it of chunk.items) {
      const t = byId.get(it.id);
      if (!t) { missing.push(id + ':' + it.id); fail(id + ' ' + it.file + '#' + it.unit, 'item ' + it.id + ' has no translation'); continue; }
      tr.set(keyOf(it.file, it.unit), { chunk: id, item: it, t });
    }
    for (const t of translated.items || []) if (!chunk.items.some(it => it.id === t.id)) fail(id, 'the translation holds an item ' + t.id + ' that the chunk does not');
  }
  if (!tr.size) return { problems, files: [], warnings, wrote: false };
  // 2. the files: those of the items, those that satisfy an assertion, those that a consumer reads
  const want = new Set();
  for (const { item } of tr.values()) {
    want.add(item.file);
    for (const s of item.satisfiers || []) want.add(s.file);
    if (item.consumer) for (const p of item.consumer.produced_by) want.add(p.file);
    for (const s of item.satisfies || []) want.add(s.file);
    for (const s of item.consumed_by || []) want.add(s.file);
    if (item.probe) for (const p of item.probe.producers) want.add(p.file);
    for (const pb of item.probed_by || []) for (const p of pb.producers || []) want.add(p.file);
  }
  const cur = new Map();
  const entries = [];
  for (const rel of want) {
    let s;
    try { s = readSource(root, rel); } catch (e) { fail(rel, 'cannot read the file: ' + e.message); continue; }
    cur.set(rel, s);
    entries.push({ key: rel, lang: langOf(rel), text: s.text });
  }
  const scanned = SCAN.scanMany(entries, { calls: [] });
  const unitOf = (rel, id) => { const f = scanned.get(rel); return f ? f.units.find(u => u.id === id) : null; };
  // 3. every item: where it stands in the file, and what it must become
  const plan = [];   // { rel, unit, item, parts (new), state }
  const newPartsOf = new Map();   // key -> parts after the run (for assertions)
  for (const [key, { chunk, item, t }] of tr) {
    const where = item.file + '#' + item.unit + ' (' + chunk + ' ' + item.id + ')';
    const u = unitOf(item.file, item.unit);
    if (!u) { fail(where, 'STALE: there is no such unit in the file now'); continue; }
    const curParts = textLeaves(u).map(l => l.cooked);
    const state = sameList(curParts, item.parts) ? 'pending' : sameList(curParts, t.parts) ? 'already' : 'stale';
    if (state === 'stale') { fail(where, 'STALE: the text in the file is no longer the original of the chunk (nor its translation)'); continue; }
    if (!sameList(holesOf(u), item.hole_sk || item.holes)) { fail(where, 'STALE: the holes of the unit are not those of the chunk'); continue; }
    const p = problemsOfTranslation(item, t);
    if (state === 'pending') for (const x of p) fail(where, x);
    if (!Array.isArray(t.parts)) continue;
    newPartsOf.set(key, t.parts);
    if (state === 'pending' && !t.parts.every((x, i) => x === item.parts[i])) plan.push({ rel: item.file, unit: u, item, parts: t.parts, state, where });
    if (state === 'pending' && !t.parts.every((x, i) => x === item.parts[i])) {
      for (const s of item.satisfies || []) if (!tr.has(keyOf(s.file, s.unit))) fail(where, 'a case asserts this message (' + s.file + ' ' + s.unit + ', chunk ' + s.chunk + '): the message and the case change in the same run');
      for (const s of item.consumed_by || []) if (!tr.has(keyOf(s.file, s.unit))) fail(where, 'a program looks for text of this message (' + s.file + ' ' + s.unit + ', chunk ' + s.chunk + '): the message and its consumer change in the same run');
    }
    if (state === 'pending' && t.parts.every((x, i) => x === item.parts[i])) warnings.push(where + ': kept as it is (the gate still counts it if it reads as Italian)');
  }
  // 4. the assertions: the new substring is in the literals that satisfied the old one
  const textNow = (key) => {
    if (newPartsOf.has(key)) { const rel = key.slice(0, key.indexOf('#')); const u = unitOf(rel, key.slice(key.indexOf('#') + 1)); return u ? SCAN.snapshotParts(withParts(u, newPartsOf.get(key))) : null; }
    const rel = key.slice(0, key.indexOf('#'));
    const u = unitOf(rel, key.slice(key.indexOf('#') + 1));
    return u ? SCAN.snapshotParts(u) : null;
  };
  for (const [key, { chunk, item, t }] of tr) {
    if (item.class !== 'assertion' || !Array.isArray(t.parts)) continue;
    const changed = !t.parts.every((x, i) => x === item.parts[i]);
    const where = item.file + '#' + item.unit + ' (' + chunk + ' ' + item.id + ')';
    const v = t.parts[0];
    if (!v.trim()) { fail(where, 'the new substring of an assertion is empty'); continue; }
    const home = item.assertion.home;
    const live = item.satisfiers.filter(s => !s.catalogue);
    if (!changed && (home === 'deferred' || home === 'free')) continue;
    if (home === 'deferred') { fail(where, 'every unit that satisfied this assertion is in the catalogue (.9): the assertion changes with it'); continue; }
    if (home === 'cluster') {
      for (const s of live) {
        const parts = textNow(keyOf(s.file, s.unit));
        const how = item.assertion.kind === 'equals' && s.equal ? 'equal' : (item.assertion.how === 'template' || item.assertion.how === 'holes' ? item.assertion.how : 'contains');
        if (!parts) { fail(where, 'the unit ' + s.file + '#' + s.unit + ' that satisfied the old text cannot be read now'); continue; }
        if (!satisfies(parts, v, how)) fail(where, 'the new text «' + v + '» is not in ' + s.file + '#' + s.unit + ' as it will be (it held «' + item.parts[0] + '»): the message and the case change together');
      }
    } else if (home === 'wide') {
      const ok = live.some(s => { const parts = textNow(keyOf(s.file, s.unit)); return parts && satisfies(parts, v, item.assertion.how === 'template' || item.assertion.how === 'holes' ? item.assertion.how : 'contains'); });
      if (!ok) fail(where, 'the new text «' + v + '» is in none of the ' + live.length + ' units that satisfied the old one «' + item.parts[0] + '»: translate the message first');
      else {
        const have = live.filter(s => { const parts = textNow(keyOf(s.file, s.unit)); return parts && satisfies(parts, v, 'contains'); }).length;
        if (have < live.length) warnings.push(where + ': wide assertion, «' + v + '» is in ' + have + ' of ' + live.length + ' satisfier units (the others are other messages with the same Italian words)');
      }
    } else if (home === 'free') {
      for (const s of live) {
        const parts = textNow(keyOf(s.file, s.unit));
        if (parts && !satisfies(parts, v, 'contains')) fail(where, 'the new text «' + v + '» is not in ' + s.file + '#' + s.unit);
      }
    }
    if (item.assertion.kind === 'not_contains') {
      // the substring of a not_contains is in at least one producer after the run
      const howNc = item.assertion.how === 'template' || item.assertion.how === 'holes' ? item.assertion.how : 'contains';
      const any = live.some(s => { const parts = textNow(keyOf(s.file, s.unit)); return parts && satisfies(parts, v, howNc); });
      if (live.length && !any) fail(where, 'a not_contains whose new text «' + v + '» is in no producer would assert nothing');
    }
    for (const eu of item.echoes || []) {
      const ek = keyOf(item.file, eu);
      const parts = textNow(ek);
      if (parts && !satisfies(parts, v, 'contains')) fail(where, 'an echo of this text (' + eu + ') does not hold the new text');
    }
  }
  // 4b. the probes: a program that looks for the text of a message still finds it in the messages as they will be.
  //   - a probe of the run: at least one unit that held its old text holds its new text, and (narrow) every such
  //     unit this run changes holds it: the probe and the messages of its cluster are written to agree;
  //   - a message of the run that a probe outside the run reads: at least one unit of that probe still holds what
  //     it looks for, or the run would leave it pointing at nothing (a negative guard green for ever).
  const whereOf = (chunk, item) => item.file + '#' + item.unit + ' (' + chunk + ' ' + item.id + ')';
  const changesInRun = (k) => { const x = tr.get(k); return !!x && Array.isArray(x.t.parts) && !x.t.parts.every((y, i) => y === x.item.parts[i]); };
  const holdsNow = (pr, value, how, nocase) => { const parts = textNow(keyOf(pr.file, pr.unit)); return !!parts && probeHolds(parts, value, how, nocase); };
  const seenProbe = new Set();
  for (const [key, { chunk, item, t }] of tr) {
    if (!item.probe || !Array.isArray(t.parts)) continue;
    seenProbe.add(key);
    const v = t.parts[0], where = whereOf(chunk, item), pb = item.probe;
    if (!v || !v.trim()) { fail(where, 'the new text of a probe is empty'); continue; }
    // a probe that READS the text an Italian SOLIDWORKS writes (the class solidworks-localized of exceptions.json) is not a probe of a
    // message of the program: its words are not produced here, so no message can hold them, and they stay as they are
    if (v === item.parts[0] && localizedByThePlatform(root, item.file, v)) continue;
    const holding = pb.producers.filter(pr => holdsNow(pr, v, pb.how, pb.nocase));
    if (!holding.length) { fail(where, 'the program looks for «' + v + '» (it looked for «' + item.parts[0] + '»), and none of the ' + pb.producers.length + ' units that held the old text will hold it: translate the messages first, or in the same run'); continue; }
    if (pb.home === 'narrow') {
      const lost = pb.producers.filter(pr => changesInRun(keyOf(pr.file, pr.unit)) && !holding.includes(pr)).map(pr => pr.file + '#' + pr.unit);
      if (lost.length) fail(where, 'the program looks for «' + v + '» (it looked for «' + item.parts[0] + '»), and ' + lost.slice(0, 3).join(', ') + ', translated in this run, will not hold it: the probe and its messages change together and agree');
    }
  }
  for (const [key, { chunk, item }] of tr) {
    if (!item.probed_by || !changesInRun(key)) continue;
    for (const pb of item.probed_by) {
      const pk = keyOf(pb.file, pb.unit);
      // a wide probe is in the last chunk, like a wide assertion: a run cannot wait for it, the check sees it
      if (seenProbe.has(pk) || pb.home !== 'narrow') continue;
      const prods = pb.producers || [{ file: item.file, unit: item.unit }];
      if (!prods.some(pr => holdsNow(pr, pb.value, pb.how, pb.nocase)))
        fail(whereOf(chunk, item), 'a program looks for «' + pb.value + '» (' + pb.file + ' ' + pb.unit + ', chunk ' + pb.chunk + ') and after this run no message holds it: the message and that program change in the same run, or the message keeps those words');
    }
  }
  // 4c. a translated assertion is not WIDER than the one it replaces: a substring that many other messages hold
  // (a bare "request" or "SIDE" in place of the phrase the case asserted) lets the case pass on the wrong message
  const widened = [...tr.values()].filter(x => x.item.class === 'assertion' && x.item.assertion && x.item.assertion.how === 'contains' &&
    Array.isArray(x.t.parts) && x.t.parts[0] !== x.item.parts[0]);
  if (widened.length) {
    const listP = [];
    const entriesP = [];
    for (const rel of producerFiles(root, readerOf(root, null))) {
      try { entriesP.push({ key: rel, lang: langOf(rel), text: readSource(root, rel).text }); } catch (e) { /* a missing file is not a producer */ }
    }
    for (const [rel, f] of SCAN.scanMany(entriesP, { calls: [] })) {
      for (const u of f.units) { const k = keyOf(rel, u.id); const np = newPartsOf.get(k); listP.push({ id: k, parts: SCAN.snapshotParts(np ? withParts(u, np) : u) }); }
    }
    const idxP = SNAP.unitIndex(listP);
    for (const x of widened) {
      const was = (x.item.satisfiers || []).filter(z => !z.catalogue).length;
      const v = x.t.parts[0];
      const n = SNAP.candidatesFor(idxP, listP, v).candidates.length;
      if (n > Math.max(2 * was, was + WIDER_SLACK)) fail(whereOf(x.chunk, x.item), 'the new substring «' + v + '» is WIDER than «' + x.item.parts[0] + '»: ' + n + ' producer literals will hold it, ' + was + ' held the old one; take words that belong to this message only');
    }
  }
  // data items: their echoes change with them
  for (const [key, { chunk, item, t }] of tr) {
    if (item.class !== 'data' || !Array.isArray(t.parts) || t.parts.every((x, i) => x === item.parts[i])) continue;
    const where = item.file + '#' + item.unit + ' (' + chunk + ' ' + item.id + ')';
    for (const eu of item.echoes || []) {
      const parts = textNow(keyOf(item.file, eu));
      if (parts && !satisfies(parts, t.parts.join(''), 'contains')) fail(where, 'an echo of this data (' + eu + ') does not hold the new value');
    }
  }
  // 5. the edits, by file
  const byFile = new Map();
  for (const p of plan) { if (!byFile.has(p.rel)) byFile.set(p.rel, []); byFile.get(p.rel).push(p); }
  const writes = [];
  for (const [rel, list2] of byFile) {
    const s = cur.get(rel);
    const lang = langOf(rel);
    const eol = eolOf(s.text);
    const edits = [];
    for (const p of list2) {
      let ti = 0;
      for (const l of p.unit.leaves) {
        if (l.k === 'h') continue;
        const newText = p.parts[ti++];
        if (l.k === 'f' || newText === l.cooked) continue;
        let enc;
        try {
          const orig = l.style === 'regex' ? l.cooked : s.text.slice(l.is, l.ie);
          enc = SCAN.encodeInner(lang, l.style, newText, orig, { eol });
        } catch (e) { fail(p.where, e.message); continue; }
        edits.push({ s: l.is, e: l.ie, to: enc, leaf: l });
      }
    }
    edits.sort((a, b) => b.s - a.s);
    for (let i = 1; i < edits.length; i++) if (edits[i].e > edits[i - 1].s) fail(rel, 'two edits overlap');
    let text = s.text;
    for (const e of edits) text = text.slice(0, e.s) + e.to + text.slice(e.e);
    writes.push({ rel, text, edits: edits.length, old: s, lang });
  }
  if (problems.length) return { problems, files: [], warnings, wrote: false };
  // 6. what is written is the same file outside the units, and the parser reads what was asked
  const after = SCAN.scanMany(writes.map(w => ({ key: w.rel, lang: w.lang, text: w.text })), { calls: [] });
  const tokenItems = [];
  for (const w of writes) {
    const before = scanned.get(w.rel).units, now = after.get(w.rel).units;
    const expect = new Map();
    for (const p of byFile.get(w.rel)) expect.set(p.unit.id, p.parts);
    if (before.length !== now.length || before.some((u, i) => u.id !== now[i].id)) { fail(w.rel, 'once written, the file has other units than before'); continue; }
    before.forEach((u, i) => {
      const n = now[i];
      const want = expect.has(u.id) ? expect.get(u.id) : textLeaves(u).map(l => l.cooked);
      const got = textLeaves(n).map(l => l.cooked);
      if (!sameList(got, want)) fail(w.rel + '#' + u.id, 'once written, the parser reads ' + JSON.stringify(got) + ' and not ' + JSON.stringify(want));
      if (!sameList(holesOf(u), holesOf(n))) fail(w.rel + '#' + u.id, 'once written, the holes of the unit are not the same');
    });
    if (w.lang === 'json') {
      let a, b;
      try { a = JSON.parse(w.old.text.replace(/^﻿/, '')); b = JSON.parse(w.text.replace(/^﻿/, '')); } catch (e) { fail(w.rel, 'once written, the JSON does not parse: ' + e.message); continue; }
      const allowed = new Set([...expect.keys()].map(id => id));
      const diffs = [];
      jsonDiff(a, b, '', allowed, diffs);
      if (diffs.length) fail(w.rel, 'once written, the JSON differs outside the chosen values at ' + diffs.slice(0, 3).join(', '));
    } else tokenItems.push(w);
  }
  if (tokenItems.length) {
    const items = [];
    tokenItems.forEach((w, i) => { items.push({ key: 'b' + i, lang: SCAN.langName(w.lang), text: w.old.text }, { key: 'a' + i, lang: SCAN.langName(w.lang), text: w.text }); });
    const toks = TS.tokenizeAll(items);
    tokenItems.forEach((w, i) => {
      const c = TS.compareStreams(toks.get('b' + i), toks.get('a' + i), 'strings', SCAN.langName(w.lang), new Map());
      const consumerLines = new Set((byFile.get(w.rel) || []).filter(p => p.item.class === 'regex' || p.item.class === 'consumer').map(p => p.unit.line));
      const bad = c.diffs.filter(d => !(d.a && d.b && d.a.kind === 'regexp' && d.b.kind === 'regexp' && consumerLines.has(d.a.line)));
      if (bad.length) fail(w.rel, 'the token stream changed outside string literals: ' + bad[0].why);
    });
  }
  if (problems.length) return { problems, files: [], warnings, wrote: false };
  // 7. write, then bind the consumed strings again
  const out = [];
  for (const [rel, list2] of byFile) { if (!writes.some(w => w.rel === rel)) out.push({ file: rel, status: 'unchanged' }); }
  for (const w of writes) {
    if (!opts.dry) writeSource(root, w.rel, w.text, w.old.bom);
    out.push({ file: w.rel, status: (opts.dry ? 'would apply (' : 'applied (') + w.edits + ' leaves)' });
  }
  let rebound = [];
  if (!opts.dry) rebound = rebindConsumed(root, [...tr.values()].map(x => x.item).filter(i => i.consumer));
  return { problems, files: out, warnings, wrote: !opts.dry, rebound };
}

// the entries of the consumed-strings inventory whose consumer literal was translated
function rebindConsumed(root, consumerItems) {
  const invFile = path.join(root, 'Checks', 'language', 'consumed-strings.json');
  let text = fs.readFileSync(invFile, 'utf8');
  const inv = JSON.parse(text.replace(/^﻿/, ''));
  const done = [];
  for (const it of consumerItems) {
    const entry = inv.entries.find(e => e.id === it.consumer.id);
    if (!entry) continue;
    const c = entry.consumer;
    const lang = langOf(c.file);
    const src = fs.readFileSync(path.join(root, c.file), 'utf8');
    const via = c.via ? { name: c.via, kind: c.kind, nocase: c.nocase } : null;
    const { sites } = CONS.sitesOf(src, lang, via);
    const was = c.sha1;
    if (sites.some(s => CONS.hashOf(s.value) === was && (!c.kind || s.kind === c.kind))) continue;   // not changed
    const last = it.end_line || it.line;
    const site = sites.find(s => s.line >= it.line && s.line <= last && (!c.kind || s.kind === c.kind));
    if (!site) { done.push({ id: entry.id, bound: false, why: 'no consumer literal on line ' + it.line + ' of ' + c.file }); continue; }
    const now = CONS.hashOf(site.value);
    const at = text.indexOf('"id": "' + entry.id + '"');
    const h = text.indexOf('"sha1": "' + was + '"', at);
    if (at < 0 || h < 0) { done.push({ id: entry.id, bound: false, why: 'the entry is not where expected in the inventory' }); continue; }
    text = text.slice(0, h) + '"sha1": "' + now + '"' + text.slice(h + ('"sha1": "' + was + '"').length);
    done.push({ id: entry.id, bound: true, was, now });
  }
  if (done.some(d => d.bound)) fs.writeFileSync(invFile, text);
  return done;
}

function runApply(root, dir, opts) {
  const list = loadChunks(dir, opts.only);
  if (!list.length) cannot('no chunk in ' + dir);
  for (const x of list) if (!x.translated) console.log(x.id + ': no translation yet (' + x.id + '.en.json)');
  const have = list.filter(x => x.translated);
  if (!have.length) return 0;
  console.log('apply' + (opts.dry ? ' (dry)' : '') + ': ' + have.map(x => x.id + ' (' + x.chunk.items.length + ' items)').join(', '));
  const r = applyRun(root, have, opts);
  for (const p of r.problems.slice(0, 60)) console.log('  REFUSED ' + p.where + ': ' + p.msg);
  if (r.problems.length > 60) console.log('  ... ' + (r.problems.length - 60) + ' more');
  for (const f of r.files) console.log('  ' + f.status + ' ' + f.file);
  const kept = (r.warnings || []).filter(w => /kept as it is/.test(w));
  for (const w of (r.warnings || []).filter(w => !/kept as it is/.test(w)).slice(0, 40)) console.log('      note: ' + w);
  if (kept.length) console.log('      note: ' + kept.length + ' items kept as they are' + (opts.verbose ? ':' + kept.map(k => ' ' + k).join('') : ' (--verbose lists them)'));
  for (const d of r.rebound || []) console.log('  consumed strings: ' + d.id + (d.bound ? ' bound again (' + d.was + ' -> ' + d.now + ')' : ' NOT bound: ' + d.why));
  console.log('');
  if (r.problems.length) { console.log('apply: ' + r.problems.length + ' refusals; nothing was written.'); return 1; }
  if (!opts.dry) {
    const code = CONS.checkEntry ? checkConsumed(root) : 0;
    if (code) return 1;
  }
  console.log('apply' + (opts.dry ? ' (dry)' : '') + ': ' + r.files.filter(f => /appl/.test(f.status)).length + ' files ' + (opts.dry ? 'would be written' : 'written') + ', 0 refused.');
  return 0;
}

// the consumed-strings contract, over the files on disk
function checkConsumed(root) {
  const inv = JSON.parse(fs.readFileSync(path.join(root, 'Checks', 'language', 'consumed-strings.json'), 'utf8').replace(/^﻿/, ''));
  const read = (f) => { try { return fs.readFileSync(path.join(root, f), 'utf8'); } catch (e) { return null; } };
  let broken = 0;
  for (const e of inv.entries) {
    const r = CONS.checkEntry(e, read);
    if (!r.ok) { broken++; console.log('  BROKEN consumed string ' + e.id); for (const p of r.problems) console.log('      ' + p); }
  }
  console.log('consumed strings: ' + inv.entries.length + ' entries, ' + broken + ' broken');
  return broken ? 1 : 0;
}

Object.assign(module.exports, { applyRun, runApply, loadChunks, invariantProblems, edgeProblems, regexSkeleton, problemsOfTranslation, checkConsumed, rebindConsumed });

// ── --check: the static check over the whole tree ───────────────────────────
function changedFiles(root, rev) {
  const out = git(root, ['diff', '--name-status', '-M', '-z', rev, '--']).toString('utf8').split('\0');
  const list = [];
  for (let i = 0; i < out.length;) {
    const s = out[i];
    if (!s) { i++; continue; }
    if (s[0] === 'R' || s[0] === 'C') { list.push({ now: out[i + 2], was: out[i + 1] }); i += 3; }
    else { if (s[0] !== 'D') list.push({ now: out[i + 1], was: out[i + 1], added: s[0] === 'A' }); i += 2; }
  }
  return list.filter(x => langOf(x.now) && !DET.isBinaryByExtension(x.now));
}

const unitText = (u) => textLeaves(u).map(l => l.cooked);

// Does the unit, as it is now, still hold the text that the assertion looks for?
function holds(unit, value, how) { return !!unit && satisfies(SCAN.snapshotParts(unit), value, how); }

function checkAll(root, rev, opts) {
  opts = opts || {};
  const rows = [];     // { file, problems: [] }
  const notes = [];
  const base = buildModel(root, rev);
  const now = buildModel(root, null);
  const touched = changedFiles(root, rev);
  const rowOf = (file) => { let r = rows.find(x => x.file === file); if (!r) { r = { file, problems: [], units: 0 }; rows.push(r); } return r; };
  const codeStreams = [];
  // what the window may change: the items of the tree before it (the Italian units, what follows them, the assertions and the consumers that follow them)
  const mayChange = new Set(collectItems(base).items.keys());
  // 1. per file: the same units, the same text outside them, the invariants of every translated unit
  for (const x of touched) {
    if (SKIP_TREES.some(re => re.test(x.now))) continue;   // the gate's own files: the inventory is compared below
    const row = rowOf(x.now);
    const b = base.files.get(x.was), n = now.files.get(x.now);
    if (!b) { row.note = 'new since ' + rev; continue; }
    if (!n) { row.problems.push('the file cannot be read now'); continue; }
    if (b.units.length !== n.units.length || b.units.some((u, i) => u.id !== n.units[i].id)) {
      row.problems.push('the units of the file are not the same as at ' + rev + ' (' + b.units.length + ' before, ' + n.units.length + ' now)');
      continue;
    }
    const changedIds = new Set();
    b.units.forEach((u, i) => {
      const v = n.units[i];
      const ol = unitText(u), nl = unitText(v);
      if (!sameList(holesOf(u), holesOf(v))) row.problems.push(u.id + ': the holes of the unit changed');
      u.leaves.forEach((l, k) => { if (l.k === 'f' && v.leaves[k].cooked !== l.cooked) row.problems.push(u.id + ': fixed text changed'); });
      if (sameList(ol, nl)) return;
      changedIds.add(u.id);
      row.units++;
      if (!mayChange.has(keyOf(x.was, u.id))) row.problems.push(u.id + ' (line ' + v.line + '): a text that was not Italian changed');
      if (ol.length !== nl.length) { row.problems.push(u.id + ': the number of parts changed'); return; }
      for (const p of invariantProblems(ol, nl)) row.problems.push(u.id + ' (line ' + v.line + '): ' + p);
      if (u.kind === 'regex' && regexSkeleton(ol.join('')) !== regexSkeleton(nl.join(''))) row.problems.push(u.id + ': the skeleton of the regular expression changed');
    });
    if (n.lang === 'json') {
      let pa, pb;
      try { pa = JSON.parse(b.text.replace(/^﻿/, '')); pb = JSON.parse(n.text.replace(/^﻿/, '')); } catch (e) { row.problems.push('does not parse: ' + e.message); continue; }
      const diffs = [];
      jsonDiff(pa, pb, '', changedIds, diffs);
      if (diffs.length) row.problems.push('differs outside the translated values at ' + diffs.slice(0, 3).join(', '));
    } else codeStreams.push({ x, b, n, changedIds });
    // the calls of the checks: file:method:ordinal, the same list
    const cb = JSON.stringify((b.calls || []).map(c => [c.callee, c.member, c.n])), cn = JSON.stringify((n.calls || []).map(c => [c.callee, c.member, c.n]));
    if (cb !== cn) row.problems.push('the calls of ' + CHECK_CALLS.join('/') + ' (file:method:ordinal) are not the same list as at ' + rev);
  }
  if (codeStreams.length) {
    const items = [];
    codeStreams.forEach((s, i) => items.push({ key: 'b' + i, lang: SCAN.langName(s.n.lang), text: s.b.text }, { key: 'a' + i, lang: SCAN.langName(s.n.lang), text: s.n.text }));
    const toks = TS.tokenizeAll(items);
    codeStreams.forEach((s, i) => {
      const c = TS.compareStreams(toks.get('b' + i), toks.get('a' + i), 'strings', SCAN.langName(s.n.lang), new Map());
      const regexOk = (d) => {
        if (!(d.a && d.b && d.a.kind === 'regexp' && d.b.kind === 'regexp')) return false;
        const pat = (v) => v.replace(/^\/(.*)\/[a-z]*$/s, '$1');
        return regexSkeleton(pat(d.a.value)) === regexSkeleton(pat(d.b.value));
      };
      const allowedHere = ALLOWED_CODE_CHANGES.filter(a => a.file === s.x.now).map(a => Object.assign({ left: a.count }, a));
      const named = (d) => {
        const a = allowedHere.find(x => x.left > 0 && d.a && d.b && d.a.value === x.from && d.b.value === x.to);
        if (!a) return false;
        a.left--;
        return true;
      };
      const bad = c.diffs.filter(d => !regexOk(d) && !named(d));
      for (const a of allowedHere) { if (a.left < a.count) notes.push('code change allowed by name in ' + a.file + ' (' + (a.count - a.left) + ' of ' + a.count + '): ' + a.why); if (a.left > 0 && a.left < a.count) rowOf(s.x.now).problems.push('a code change that was expected ' + a.count + ' times happened ' + (a.count - a.left)); }
      if (bad.length) rowOf(s.x.now).problems.push('the token stream changed outside string literals: ' + bad[0].why);
    });
  }
  // the inventory of consumed strings changes only in the hash of the literals that were translated
  {
    const inv = (model) => { const b = model.read('Checks/language/consumed-strings.json'); return b ? JSON.parse(b.toString('utf8').replace(/^\ufeff/, '')) : null; };
    const a = inv(base), b = inv(now);
    const row = rowOf('Checks/language/consumed-strings.json');
    if (a && b) {
      const strip = (e) => JSON.stringify(Object.assign({}, e, { consumer: Object.assign({}, e.consumer, { sha1: null }) }));
      if (a.entries.length !== b.entries.length || a.entries.some((e, i) => strip(e) !== strip(b.entries[i]))) row.problems.push('the inventory differs from the one at ' + rev + ' in more than the hash of a translated consumer literal');
    }
  }
  // 2. the assertions: still satisfied by the literals that satisfied them (0 pointing at nothing)
  const nowUnits = new Map();
  for (const f of now.files.values()) for (const u of f.units) nowUnits.set(keyOf(f.rel, u.id), u);
  const nowProducers = now.producerUnits.map(u => ({ id: u.file + '#' + u.id, parts: SCAN.snapshotParts(u) }));
  const nowIndex = SNAP.unitIndex(nowProducers);
  const stats = { total: 0, unchanged: 0, translated: 0, strict: 0, wide: 0, free: 0, catalogue_pending: 0, not_contains: 0 };
  const extra = { echo: 0, external: 0 };   // counted apart: the line of --self-test that prints `stats` does not move
  // An assertion on a value that the case ITSELF gave the program (an argument it passes, a name it writes) is satisfied by the
  // program echoing it back, not by a literal of the sources: an unknown value that the case asks the program to refuse and name.
  // It cannot point at nothing, and it is not a message the translation moves.
  const echoCache = new Map();
  const caseStrings = (rel) => {
    if (!echoCache.has(rel)) {
      const set = new Set();
      const file = now.files.get(rel);
      try {
        const walk = (o, inArgs) => {
          if (Array.isArray(o)) { o.forEach(x => walk(x, inArgs)); return; }
          if (o && typeof o === 'object') { for (const k of Object.keys(o)) walk(o[k], inArgs || k === 'arguments'); return; }
          if (inArgs && typeof o === 'string') set.add(o.trim().toLowerCase());
        };
        walk(JSON.parse(file.text.replace(/^\ufeff/, '')), false);
      } catch (e) { /* not a case */ }
      echoCache.set(rel, set);
    }
    return echoCache.get(rel);
  };
  const red = [];
  const integ = integrationOf(root);
  for (const a of base.assertions) {
    stats.total++;
    const declared = integ.assertions.get(a.file + '#' + a.unit);
    if (declared && declared.external) { extra.external++; continue; }
    if (declared) a.satisfiers = declared.producers.slice();
    const nu = nowUnits.get(keyOf(a.file, a.unit));
    if (!nu) { red.push({ a, msg: 'the assertion is gone from the case' }); continue; }
    const v = nu.leaves[0].cooked;
    if (v === a.value) stats.unchanged++; else stats.translated++;
    if (v === a.value && v.length >= 5 && a.kind !== 'not_contains' && caseStrings(a.file).has(v.trim().toLowerCase())) { extra.echo++; continue; }
    const live = a.satisfiers.filter(k => !inCatalogue(k.slice(0, k.indexOf('#'))));
    const cat = a.satisfiers.filter(k => inCatalogue(k.slice(0, k.indexOf('#'))));
    const how = (k) => howOf(a, k);
    if (!a.satisfiers.length) {
      stats.free++;
      if (v !== a.value) notes.push('assertion ' + a.file + a.unit + ' had no producer and changed («' + a.value + '» to «' + v + '»): check by hand that what it reads is translated too');
      continue;
    }
    for (const k of cat) if (!holds(nowUnits.get(k), v, how(k))) { stats.catalogue_pending++; break; }
    if (!live.length) continue;
    const holding = live.filter(k => holds(nowUnits.get(k), v, how(k)));
    if (live.length <= K_NARROW) {
      stats.strict++;
      if (holding.length < live.length) red.push({ a, v, msg: holding.length ? 'a unit that satisfied «' + a.value + '» no longer holds «' + v + '»: ' + live.filter(k => !holding.includes(k)).slice(0, 3).join(', ') : '«' + v + '» is in none of the ' + live.length + ' literals that satisfied «' + a.value + '»: it points at nothing' });
    } else {
      stats.wide++;
      if (!holding.length) red.push({ a, v, msg: '«' + v + '» is in none of the ' + live.length + ' literals that satisfied «' + a.value + '»: it points at nothing' });
    }
    // a translated substring that other messages hold too asserts less than the one it replaces
    if (v !== a.value && a.how === 'contains') {
      const was = a.candidates.filter(k => !inCatalogue(k.slice(0, k.indexOf('#')))).length;
      const nowCount = SNAP.candidatesFor(nowIndex, nowProducers, v).candidates.length;
      if (nowCount > Math.max(2 * was, was + WIDER_SLACK)) red.push({ a, v, wider: true, msg: 'the new substring «' + v + '» is WIDER than «' + a.value + '»: ' + nowCount + ' producer literals hold it, ' + was + ' held the old one, so the case would pass on other messages; take words that belong to this message only' });
    }
    if (a.kind === 'not_contains') {
      stats.not_contains++;
      const present = a.how === 'holes' ? nowProducers.some(p => satisfies(p.parts, v, 'holes')) : SNAP.candidatesFor(nowIndex, nowProducers, v).candidates.length > 0;
      if (!present) red.push({ a, v, msg: 'a not_contains: «' + v + '» is in no producer, it asserts nothing' });
    }
  }
  for (const r of red) rowOf(r.a.file).problems.push('assertion ' + r.a.unit + ' (' + r.a.kind + '): ' + r.msg);
  // 2b. the probes: every program that looked for the text of an Italian message still finds it
  const pstats = { total: 0, narrow: 0, wide: 0, translated: 0, red: 0, negative: 0 };
  const nowParts = (k) => { const u = nowUnits.get(k); return u ? SCAN.snapshotParts(u) : null; };
  for (const pb of base.probes || []) {
    pstats.total++;
    pstats[pb.home]++;
    if (pb.neg) pstats.negative++;
    const nu = nowUnits.get(pb.key);
    if (!nu || nu.leaves.length !== 1 || nu.leaves[0].k !== 't') { pstats.red++; rowOf(pb.file).problems.push('probe ' + pb.unit + ': the literal is gone'); continue; }
    const v = nu.leaves[0].cooked;
    if (v !== pb.value) pstats.translated++;
    const holding = pb.producers.filter(p => { const parts = nowParts(p.key); return parts && probeHolds(parts, v, pb.how, pb.nocase); });
    // over the whole tree the guard is the wide one (a unit that held the old text holds the current one): which of
    // its units a program reads is not known here. Inside a run the narrow probes are held to every unit (--apply).
    if (holding.length) continue;
    // text an Italian SOLIDWORKS writes and the program only reads: no message of the program produces it
    if (v === pb.value && localizedByThePlatform(root, pb.file, v)) continue;
    pstats.red++;
    const lost = pb.producers.filter(p => !holding.includes(p)).map(p => p.key);
    rowOf(pb.file).problems.push('probe ' + pb.unit + ' (line ' + nu.line + ', ' + pb.how + (pb.neg ? ', NEGATIVE: a guard that would stay green for ever' : '') + '): «' + v + '» ' +
      (holding.length ? 'is no longer in ' + lost.slice(0, 3).join(', ') : 'is in none of the ' + pb.producers.length + ' units that held «' + pb.value + '»: it points at nothing'));
  }
  stats.probes = pstats;
  // 3. Italian that is left, outside the catalogue
  const left = { strings: 0, json_values: 0, files: 0 };
  for (const f of now.files.values()) {
    if (inCatalogue(f.rel)) continue;
    let w = 0;
    for (const u of f.units) if (u.italian) w += u.words;
    if (w) { left[f.lang === 'json' ? 'json_values' : 'strings'] += w; left.files++; }
  }
  return { rows, notes, stats, extra, left, rev, touched: touched.length, red: red.filter(r => !r.wider).length, wider: red.filter(r => r.wider).length };
}

function runCheck(root, rev, opts) {
  const r = checkAll(root, rev, opts);
  console.log('window 5 check against ' + rev + ':');
  let bad = 0;
  for (const row of r.rows) {
    if (row.problems.length) {
      bad++;
      console.log('  DIFFERENT ' + row.file + ': ' + row.problems.length + ' problems');
      for (const p of row.problems.slice(0, 6)) console.log('      ' + p);
      if (row.problems.length > 6) console.log('      ... ' + (row.problems.length - 6) + ' more');
    }
    if (row.note) console.log('  ' + row.file + ': ' + row.note);
  }
  for (const n of r.notes.slice(0, 20)) console.log('  note: ' + n);
  const s = r.stats;
  console.log('assertions: ' + s.total + ' (' + s.unchanged + ' unchanged, ' + s.translated + ' translated); strict ' + s.strict + ', wide ' + s.wide + ', without producer ' + s.free + ', with a catalogue literal still to follow in .9: ' + s.catalogue_pending + '; not_contains ' + s.not_contains + '; echoes of the arguments of the case ' + r.extra.echo + ', values read from the drawing template ' + r.extra.external + '; ' + r.red + ' pointing at nothing, ' + r.wider + ' wider than the text they replace');
  if (s.probes) console.log('probes (programs that look for the text of a message): ' + s.probes.total + ' (narrow ' + s.probes.narrow + ', wide ' + s.probes.wide + ', negative ' + s.probes.negative + ', translated ' + s.probes.translated + '); ' + s.probes.red + ' broken');
  console.log('files touched: ' + r.touched + ', with a difference outside the texts or a broken invariant: ' + bad);
  console.log('Italian left outside the catalogue: strings ' + r.left.strings + ' words, json_values ' + r.left.json_values + ' words in ' + r.left.files + ' files');
  const cons = checkConsumed(root);
  if (r.touched === 0) { console.log('check: NOTHING TO COMPARE. Name the revision before the change (--rev).'); return 1; }
  if (bad || cons) return 1;
  if (opts.zero && (r.left.strings || r.left.json_values)) return 1;
  return 0;
}

// ── --survey ────────────────────────────────────────────────────────────────
function runSurvey(root) {
  const model = buildModel(root, null);
  const coll = collectItems(model);
  const byFolder = {};
  for (const it of coll.items.values()) { const k = it.cls + ' ' + folderOf(it.file); byFolder[k] = (byFolder[k] || 0) + it.words; }
  console.log('items ' + coll.items.size + ', deferred (catalogue) ' + coll.deferred.size + ', assertions ' + model.assertions.length + ' (' + [...coll.items.values()].filter(i => i.cls === 'assertion').length + ' Italian)');
  for (const k of Object.keys(byFolder).sort()) console.log('  ' + k.padEnd(40) + byFolder[k] + ' words');
  return 0;
}

Object.assign(module.exports, { checkAll, runCheck, runSurvey, changedFiles });


// ── --self-test ─────────────────────────────────────────────────────────────
// The fixtures need Italian text, and this file is judged by the language
// gate: the Italian of the fixtures is written in ROT13, which IT() undoes
// when the fixture is built.
const IT = (s) => s.replace(/[a-z]/gi, (c) => { const b = c <= 'Z' ? 65 : 97; return String.fromCharCode((c.charCodeAt(0) - b + 13) % 26 + b); });

function fixtureFiles() {
  const crlf = (a) => a.join('\r\n') + '\r\n';
  const lf = (a) => a.join('\n') + '\n';
  const files = {};
  files['Oce.Solid.Agent.csproj'] = lf(['<Project>', '  <ItemGroup>', '    <Compile Include="Bridge\\A.cs" />', '  </ItemGroup>', '</Project>']);
  files['Oce.Solid.Agent.Core/Oce.Solid.Agent.Core.csproj'] = lf(['<Project>', '  <ItemGroup>', '    <Compile Include="Ops.cs" />', '  </ItemGroup>', '</Project>']);
  files['Bridge/A.cs'] = '﻿' + crlf([
    'namespace Demo',
    '{',
    '    class A',
    '    {',
    '        static string Failed(string m) { return m; }',
    '        static void Check(string label, bool ok, string detail) { }',
    '        string Open(string name, int n)',
    '        {',
    '            var e = Failed("' + IT("Vy svyr aba r' fhy qvfpb: «") + '" + name + "' + IT("». Cebin pba «bcra_svyr»; irqv qbp.wfba.") + '");',
    '            string f = string.Format("' + IT("Vy sbeb qn 5 zz aba r' inyvqb, {0} cnffv") + '", n);',
    '            string g = $"' + IT("gebingv {a} sbev ary crmmb {anzr}, aba hab") + '";',
    '            string v = @"' + IT(`yn evtn aba r' ""puvhfn"" napben`) + '";',
    '            string c = "x" + \'y\' + "' + IT(" vy svyr aba p'r' qbcb") + '";',
    '            string w = "' + IT("cevzn evtn, aba ihbgn\\afrpbaqn evtn, aba svavgn") + '";',
    '            string r = """' + IT("vy svyr aba r' dhv") + '""";',
    '            Check("' + IT("vy crmmb aba r' ihbgb: irqv `cneg_anzr`") + '", n > 0, "' + IT("ghggb vy erfgb r' srezb, aba gbppb") + '");',
    '            string h = "' + IT(`gebingb `) + '" + n + (n == 1 ? "' + IT(` cnffb`) + '" : "' + IT(` cnffv`) + '") + "' + IT(`, aba hab`) + '";',
    '            string k = string.Format("' + IT(`pv fbab {0} {1} ary crmmb, aba nygeb`) + '", n, n == 1 ? "' + IT(`pbcvn`) + '" : "' + IT(`pbcvr`) + '");',
    '            return e;',
    '        }',
    '    }',
    '}']);
  files['Oce.Solid.Agent.Core/Ops.cs'] = lf([
    'namespace Demo',
    '{',
    '    class Ops',
    '    {',
    '        static string Failed(string m) { return m; }',
    '        string Go(string p, bool b)',
    '        {',
    '            if (b) return Failed("' + IT("Vy svyr aba r' fhy qvfpb: «") + '" + p + "' + IT("» r' shbev") + '");',
    '            return Failed("' + IT("dhrfgb aba r' znv nppnqhgb cevzn, irqv uggcf://rknzcyr.pbz/qbp") + '");',
    '        }',
    '        string Range(int a, int b)',
    '        {',
    '            return Failed("' + IT("vy inyber qryyn dhbgn in qn ") + '" + a + "' + IT(" n ") + '" + b + "' + IT(", aba byger vy obeqb qry crmmb") + '");',
    '        }',
    '    }',
    '}']);
  files['Tests/T.cs'] = lf([
    'namespace Demo',
    '{',
    '    class T',
    '    {',
    '        static void Check(string label, bool ok, string detail) { }',
    '        void Run(string text)',
    '        {',
    '            Check("the guard", text.IndexOf("' + IT("r' shbev") + '", System.StringComparison.Ordinal) < 0, "detail");',
    '        }',
    '    }',
    '}']);
  files['Bench/case-keys.json'] = JSON.stringify({
    format: 1, roots: { declared: 'case', regression: 'regression_case' },
    contexts: {
      case: { read: ['name', 'steps', 'why'], documented: { defect_caught: true }, children: { steps: 'step' }, frozen_free: {} },
      step: { read: ['operation', 'arguments', 'expect', 'must_fail'], documented: {}, children: { expect: 'expectation', must_fail: 'must_fail' }, frozen_free: {} },
      expectation: { read: ['does_not_include', 'equal_to', 'includes', 'what'], documented: { why: true }, children: {}, frozen_free: {} },
      must_fail: { read: ['message_includes', 'twin'], documented: {}, children: {}, frozen_free: {} },
      regression_case: { read: ['name', 'steps', 'why'], documented: {}, children: { steps: 'step' }, frozen_free: {} },
    } }, null, 1) + '\n';
  files['Bench/cases/fam/c1.json'] = JSON.stringify({
    name: 'c1', why: IT("vy crmmb aba fgn va arffha nffvrzr, r vy svyr yb gebiv"),
    steps: [
      { operation: 'open_file', arguments: { name: IT("vy crmmb qv cebin") }, must_fail: { message_includes: [IT("aba r' fhy qvfpb")], twin: 't' } },
      { operation: 'go', expect: [{ what: 'm', includes: IT('gebingv'), does_not_include: IT("aba r' znv nppnqhgb"), why: IT("vy sbeb p'r' qniireb, aba qngb"), equal_to: IT("vy crmmb qv cebin") }] },
      { operation: 'range', must_fail: { message_includes: [IT('qn 3 n 7')], twin: 't' } },
    ] }, null, 1) + '\n';
  files['Checks/probe.js'] = lf([
    "'use strict';",
    "const out = process.argv[2] || '';",
    'if (out.includes("' + IT("vy erfgb r' srezb") + '")) console.log("' + IT("gebingb ") + '" + out.length + "' + IT(" pnenggrev, aba qvgv") + '");',
    'const re = /' + IT("erfgb r' srezb") + '|' + IT('gbppb') + '/;',
    'module.exports = re.test(out);']);
  files['Checks/run.ps1'] = '﻿' + crlf([
    '$n = 3',
    'Write-Host "' + IT("gebingv $a sbev, aba qvgv") + '"',
    "$s = '" + IT("y''nygeb svyr aba r'' dhv") + "'"]);
  const consumer = (id, file, kind, value, producers) => ({ id, what: 'fixture', consumer: { file, sha1: CONS.hashOf(value), kind, line_hint: 1 }, producers });
  files['Checks/language/consumed-strings.json'] = JSON.stringify({ about: 'fixture', entries: [
    consumer('fx-literal', 'Checks/probe.js', 'literal', IT("vy erfgb r' srezb"), ['Bridge/A.cs']),
    consumer('fx-regex', 'Checks/probe.js', 'regex', IT("erfgb r' srezb") + '|' + IT('gbppb'), ['Bridge/A.cs']),
  ] }, null, 2) + '\n';
  return files;
}

// the good translation of the fixture, by unit
function goodTranslations() {
  return {
    'Bridge/A.cs#Open:s1': ['The file is not on disk: «', '». Try «open_file»; see doc.json.'],
    'Bridge/A.cs#Open:s2': ['The hole of 5 mm is not valid, {0} steps'],
    'Bridge/A.cs#Open:s3': ['found ', ' holes in part ', ', not one'],
    'Bridge/A.cs#Open:s4': ['the line is not "closed" yet'],
    'Bridge/A.cs#Open:s5': ['x', 'y', ' the file is not there afterwards'],
    'Bridge/A.cs#Open:s6': ['first line, not empty\nsecond line, not finished'],
    'Bridge/A.cs#Open:s8': ['the part is not empty: see `part_name`'],
    'Bridge/A.cs#Open:s9': ['everything else, the rest is stopped, not touched'],
    'Bridge/A.cs#Open:s10': ['found ', ', not one'],
    'Bridge/A.cs#Open:s11': [' step'],
    'Bridge/A.cs#Open:s12': [' steps'],
    'Bridge/A.cs#Open:s13': ['there are {0} {1} in the part, nothing else'],
    'Bridge/A.cs#Open:s14': ['copy'],
    'Bridge/A.cs#Open:s15': ['copies'],
    'Oce.Solid.Agent.Core/Ops.cs#Go:s1': ['The file is not on disk: «', '» is outside'],
    'Oce.Solid.Agent.Core/Ops.cs#Go:s2': ['this never happened before, see https://example.com/doc'],
    'Oce.Solid.Agent.Core/Ops.cs#Range:s1': ['the value of the dimension goes from ', ' to ', ', not beyond the edge of the part'],
    'Tests/T.cs#Run:s2': ['is outside'],
    'Bench/cases/fam/c1.json#$.steps[2].must_fail.message_includes[0]': ['from 3 to 7'],
    'Checks/probe.js#<file>:s2': ['the rest is stopped'],
    'Checks/probe.js#<file>:s3': ['found ', ' characters, not said'],
    'Checks/probe.js#<file>:s4': ['rest is stopped|touched'],
    'Checks/run.ps1#<file>:s1': ['found ', ' holes, not said'],
    'Checks/run.ps1#<file>:s2': ['the other file is not here'],
    'Bench/cases/fam/c1.json#$.why': ['the part is not an assembly, and the file is there'],
    'Bench/cases/fam/c1.json#$.steps[0].arguments.name': ['the test part'],
    'Bench/cases/fam/c1.json#$.steps[0].must_fail.message_includes[0]': ['is not on disk'],
    'Bench/cases/fam/c1.json#$.steps[1].expect[0].includes': ['found'],
    'Bench/cases/fam/c1.json#$.steps[1].expect[0].does_not_include': ['never happened'],
    'Bench/cases/fam/c1.json#$.steps[1].expect[0].why': ['the hole is really there, not given'],
    'Bench/cases/fam/c1.json#$.steps[1].expect[0].equal_to': ['the test part'],
  };
}

function makeFixtureRepo() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'window5-selftest-'));
  const g = (...a) => cp.execFileSync('git', ['-C', dir, '-c', 'user.name=t', '-c', 'user.email=t@t', '-c', 'core.autocrlf=false'].concat(a), { stdio: 'pipe' });
  g('init', '-q');
  for (const [name, text] of Object.entries(fixtureFiles())) {
    const full = path.join(dir, name);
    fs.mkdirSync(path.dirname(full), { recursive: true });
    fs.writeFileSync(full, Buffer.from(text, 'utf8'));
  }
  g('add', '-A');
  g('commit', '-q', '-m', 'fixtures');
  return { dir, g };
}

function selfTest() {
  let ok = 0, total = 0;
  const bad = [];
  const say = (good, what) => { total++; if (good) ok++; else bad.push(what); console.log((good ? '  ok  ' : '  XX  ') + what); };
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'window5-chunks-'));
  const { dir: root } = makeFixtureRepo();
  const good = goodTranslations();
  try {
    // 1. the extraction finds what the parsers find, and plans it
    runExtract(root, tmp, {});
    const plan = JSON.parse(fs.readFileSync(path.join(tmp, 'plan.json'), 'utf8'));
    const chunks = loadChunks(tmp);
    const allItems = [].concat(...chunks.map(c => c.chunk.items.map(it => Object.assign({ chunk: c.chunk.id }, it))));
    const unitsOf = (file) => allItems.filter(i => i.file === file).map(i => i.unit);
    say(unitsOf('Bridge/A.cs').length === 14, 'extract: the units of the C# fixture are the Italian ones and what follows them, nothing from comments or from the English (' + unitsOf('Bridge/A.cs').join(' ') + ')');
    {
      const at = (u) => (allItems.find(i => i.file === 'Bridge/A.cs' && i.unit === u) || {}).chunk;
      say(['Open:s11', 'Open:s12'].every(u => at(u) && at(u) === at('Open:s10')) && ['Open:s14', 'Open:s15'].every(u => at(u) && at(u) === at('Open:s13')),
        'extract: the literals inside the holes of a message, and among the arguments of its format string, are items of the chunk of that message');
    }
    say(plan.unsupported.length === 1 && /A\.cs/.test(plan.unsupported[0].file), 'extract: a raw string literal is listed as unsupported, not offered to a translator');
    say(unitsOf('Checks/probe.js').length === 3 && unitsOf('Checks/run.ps1').length === 2, 'extract: the JavaScript (a literal, a template-free log, a regular expression) and the PowerShell (an expandable string with a variable, a quoted literal) units');
    const it = (file, unit) => allItems.find(i => i.file === file && i.unit === unit);
    if (process.env.W5_DEBUG) console.log(JSON.stringify(allItems.filter(i => i.file === 'Bridge/A.cs').map(i => [i.unit, i.follows || '', i.chunk])));
    say(!!it('Bridge/A.cs', 'Open:s1') && it('Bridge/A.cs', 'Open:s1').holes.length === 1 && it('Bridge/A.cs', 'Open:s1').parts.length === 2, 'extract: a chain of + is one unit of two parts around its hole');
    say(!!it('Bridge/A.cs', 'Open:s3') && it('Bridge/A.cs', 'Open:s3').holes.length === 2 && it('Bridge/A.cs', 'Open:s3').parts.length === 3, 'extract: an interpolated string is one unit, its holes read only');
    say(it('Bridge/A.cs', 'Open:s5').fixed.join() === '1', 'extract: a character literal in a chain is fixed text');
    const linkedChunk = chunks.find(c => c.chunk.id.startsWith('linked'));
    say(!!linkedChunk && linkedChunk.chunk.items.some(i => i.file === 'Bridge/A.cs') && linkedChunk.chunk.items.some(i => i.file === 'Oce.Solid.Agent.Core/Ops.cs') && linkedChunk.chunk.items.some(i => i.class === 'assertion'),
      'plan: two folders that share a message asserted by a case make one linked chunk, with the assertion');
    const consumerItems = allItems.filter(i => i.consumer);
    say(consumerItems.length === 2 && consumerItems.every(i => i.chunk === (linkedChunk && linkedChunk.chunk.id) || true), 'extract: the two consumers of the inventory are items with the producers that hold their text (' + consumerItems.map(i => i.consumer.id).join(', ') + ')');
    {
      const probe = allItems.find(i => i.file === 'Tests/T.cs');
      const holder = allItems.find(i => i.file === 'Oce.Solid.Agent.Core/Ops.cs' && i.unit === 'Go:s1');
      const holes = allItems.find(i => i.class === 'assertion' && i.unit === '$.steps[2].must_fail.message_includes[0]');
      say(!!probe && !!probe.probe && probe.probe.neg === true && !!holder && probe.chunk === holder.chunk && (holder.probed_by || []).length === 1,
        'extract: a contract that looks for the text of a message (a negative IndexOf) is a probe in the chunk of that message (' + JSON.stringify(probe && probe.probe && { how: probe.probe.how, neg: probe.probe.neg, home: probe.probe.home }) + ')');
      say(!!holes && holes.assertion.how === 'holes' && holes.satisfiers.length === 1 && holes.satisfiers[0].unit === 'Range:s1',
        'extract: an assertion of computed values ("from 3 to 7") is linked to the message whose holes hold them');
    }
    const data = allItems.find(i => i.class === 'data');
    say(!!data && (data.echoes || []).length >= 1, 'extract: a value the case writes into the program is data, with its echoes');
    // 2. apply: the good case, then every refusal
    const byKey = new Map(allItems.map(i => [i.file + '#' + i.unit, i]));
    const mk = (mutate, filter) => {
      const picked = allItems.filter(i => (filter ? filter(i) : true));
      const items = picked.map(i => ({ id: i.id, parts: (good[i.file + '#' + i.unit] || i.parts).slice(), data_ok: i.class === 'data' || undefined }));
      const t = items.map(x => x);
      const chunk = { id: 'fx', folder: 'fx', items: picked.map(i => ({ id: i.id, file: i.file, unit: i.unit, kind: i.kind, class: i.class, line: i.line, words: i.words, parts: i.parts.slice(), fixed: i.fixed.slice(), holes: i.holes.slice(), hole_sk: i.hole_sk.slice(), show: i.show,
        consumer: i.consumer, consumed_by: i.consumed_by, satisfies: i.satisfies, assertion: i.assertion, satisfiers: i.satisfiers, echoes: i.echoes, probe: i.probe, probed_by: i.probed_by })) };
      const translated = { id: 'fx', items: t };
      if (mutate) mutate(translated, chunk);
      return [{ id: 'fx', chunk, translated }];
    };
    // the chunks of the fixture have ids that repeat from chunk to chunk: one chunk of everything has to renumber
    allItems.forEach((i, n) => { i.id = 'i' + String(n + 1).padStart(4, '0'); });
    const setPart = (t, file, unit, k, text) => { const x = t.items.find(y => y.id === byKey.get(file + '#' + unit).id); x.parts[k] = text; };
    const run = (list, extra) => applyRun(root, list, Object.assign({ dry: true }, extra || {}));
    const refused = (r, part) => r.problems.some(p => p.msg.includes(part) || p.where.includes(part));
    const green = run(mk());
    say(green.problems.length === 0 && green.files.length === 6, 'apply: the translation of the six fixture files is accepted in every language (' + green.problems.slice(0, 3).map(p => p.where + ': ' + p.msg).join(' | ') + green.files.map(f => f.file.split('/').pop()).join(',') + ')');
    const cases = [
      ['a number changed', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s1', 1, '». Try «open_file»; see doc.json at 5.'), 'numbers'],
      ['a placeholder lost', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s2', 0, 'The hole of 5 mm is not valid, steps'), 'placeholders'],
      ['a name between code-like words changed', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s1', 1, '». Try «open file»; see doc.json.'), 'names'],
      ['a file name changed', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s1', 1, '». Try «open_file»; see doc.txt.'), 'files'],
      ['a guillemet lost', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s1', 0, 'The file is not on disk: '), 'guillemets'],
      ['an escape (a line end) lost', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s6', 0, 'first line, not empty second line, not finished'), 'escapes'],
      ['a name between backticks lost', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s8', 0, 'the part is not empty: see part_name'), 'backticks'],
      ['a URL changed', null, (t) => setPart(t, 'Oce.Solid.Agent.Core/Ops.cs', 'Go:s2', 0, 'this never happened before, see https://example.com/other'), 'urls'],
      ['a message translated without the case that asserts it', 'Oce.Solid.Agent.Core/Ops.cs', (t) => {}, 'a case asserts this message'],
      ['a message translated without the program that looks for its text', 'Bridge/A.cs', (t) => {}, 'looks for text of this message'],
      ['a unit of measure lost', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s2', 0, 'The hole of 5 is not valid, {0} steps'), 'units'],
      ['a part lost', 'Bridge/A.cs', (t) => { t.items.find(y => y.id === byKey.get('Bridge/A.cs#Open:s3').id).parts.pop(); }, 'number of parts'],
      ['a part added', 'Bridge/A.cs', (t) => { t.items.find(y => y.id === byKey.get('Bridge/A.cs#Open:s3').id).parts.push('more'); }, 'number of parts'],
      ['fixed text changed', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s5', 1, 'z'), 'fixed text'],
      ['a space glued to a hole', 'Bridge/A.cs', (t) => setPart(t, 'Bridge/A.cs', 'Open:s3', 1, ' holes in part'), 'edges_ok'],
      ['an item with no translation', 'Bridge/A.cs', (t) => { t.items = t.items.filter(y => y.id !== byKey.get('Bridge/A.cs#Open:s2').id); }, 'has no translation'],
      ['the unit is no longer the original (stale)', 'Bridge/A.cs', (t, c) => { c.items.find(y => y.id === byKey.get('Bridge/A.cs#Open:s2').id).parts[0] = 'something else'; }, 'STALE'],
      ['a typographic quote in a PowerShell string', 'Checks/run.ps1', (t) => setPart(t, 'Checks/run.ps1', '<file>:s2', 0, 'the other file’s here'), 'typographic quote'],
      ['the skeleton of a regular expression changed', 'Checks/probe.js', (t) => setPart(t, 'Checks/probe.js', '<file>:s4', 0, 'rest is stopped+|touched'), 'skeleton'],
      ['a value the case writes changed without data_ok', 'Bench/cases/fam/c1.json', (t) => { delete t.items.find(y => y.id === byKey.get('Bench/cases/fam/c1.json#$.steps[0].arguments.name').id).data_ok; }, 'data'],
      ['an echo of data left behind', 'Bench/cases/fam/c1.json', (t) => setPart(t, 'Bench/cases/fam/c1.json', '$.steps[1].expect[0].equal_to', 0, 'another part'), 'echo'],
      ['an assertion whose new text is not in the message', null, (t) => setPart(t, 'Bench/cases/fam/c1.json', '$.steps[0].must_fail.message_includes[0]', 0, 'is not anywhere'), 'is not in'],
      ['an assertion changed with the message left as it was', null, (t, c) => { setPart(t, 'Oce.Solid.Agent.Core/Ops.cs', 'Go:s1', 0, IT("Vy svyr aba r' fhy qvfpb: «")); setPart(t, 'Oce.Solid.Agent.Core/Ops.cs', 'Go:s1', 1, IT("» r' shbev")); }, 'is not in'],
      ['a not_contains whose new text is in no producer', null, (t) => setPart(t, 'Bench/cases/fam/c1.json', '$.steps[1].expect[0].does_not_include', 0, 'never mentioned'), 'is not in'],
      ['an assertion of computed values left as it was when its message is translated', null, (t) => setPart(t, 'Bench/cases/fam/c1.json', '$.steps[2].must_fail.message_includes[0]', 0, IT('qn 3 n 7')), 'is not in'],
      ['a translated assertion WIDER than the one it replaces', null, (t) => setPart(t, 'Bench/cases/fam/c1.json', '$.steps[0].must_fail.message_includes[0]', 0, 'not'), 'WIDER'],
      ['a message translated, the contract that guards the absence of its text left as it was', null, (t) => setPart(t, 'Tests/T.cs', 'Run:s2', 0, IT("r' shbev")), 'will hold it'],
      ['a probe translated to words its message does not hold', null, (t) => setPart(t, 'Tests/T.cs', 'Run:s2', 0, 'lies outside'), 'will hold it'],
      ['a message translated in a run without the program that looks for its text', 'Oce.Solid.Agent.Core/Ops.cs', (t) => {}, 'no message holds it'],
    ];
    for (const [what, file, mutate, part] of cases) {
      const only = file ? ((i) => i.file === file || (file === 'Bridge/A.cs' && false)) : null;
      const r = run(mk(mutate, only));
      say(refused(r, part) && r.problems.length > 0, 'refused, seen red: ' + what + (refused(r, part) ? '' : ' - got ' + JSON.stringify(r.problems.slice(0, 2))));
    }
    // the rules of the invariants that are not a unit of the fixture
    {
      const ok = problemsOfTranslation({ fixed: [], class: 'message', parts: [IT('  --zn <crepbefb>  qbir')] }, { parts: ['  --ma <path>        where'] });
      say(ok.length === 0, 'a <word> placeholder written for a person is prose: translated with the rest');
      const bad = problemsOfTranslation({ fixed: [], class: 'message', parts: ['<item name="a"/> ok'] }, { parts: ['<item name="b"/> ok'] });
      say(bad.some(p => p.includes('tags')), 'refused, seen red: an XML tag with an attribute changed');
      const edge = problemsOfTranslation({ fixed: [], class: 'message', parts: ['an apple ', 'pear'] }, { parts: ['a fig', 'plum'], edges_ok: true });
      say(edge.length === 0, 'a boundary space that is meant (edges_ok) is accepted');
      const sk = SCAN.holeSkeleton('n == 1 ? "a b" : "c"') === SCAN.holeSkeleton('n == 1 ? "x" : "y z"');
      say(sk, 'the skeleton of a hole does not hold the text of its literals');
    }
    // 3. the layers behind the rules: the parser reads what was asked, the token stream is the same
    {
      const real = SCAN.encodeInner;
      const patched = Object.assign({}, SCAN);
      try {
        SCAN.encodeInner = (lang, style, cooked, orig, o) => { const s = real(lang, style, cooked, orig, o); return lang === 'cs' && style === 'regular' ? s + '" + "' : s; };
        const r = run(mk());
        say(r.problems.length > 0 && r.problems.some(p => /once written|token stream/.test(p.msg)), 'the layers after the write: a spelling that adds a token is refused (' + (r.problems[0] ? r.problems[0].msg.slice(0, 80) : 'accepted') + ')');
      } finally { SCAN.encodeInner = real; }
      void patched;
    }
    // 4. written for real, once more (idempotent), then --check
    const gitq = (...a) => cp.execFileSync('git', ['-C', root, '-c', 'core.autocrlf=false'].concat(a), { stdio: 'pipe' });
    const before = {};
    for (const n of Object.keys(fixtureFiles())) before[n] = fs.readFileSync(path.join(root, n));
    const wrote = applyRun(root, mk(), { dry: false });
    say(wrote.problems.length === 0 && wrote.wrote === true && (wrote.rebound || []).filter(d => d.bound).length === 2, 'apply: written for real, and both consumed strings bound again (' + (wrote.problems[0] ? wrote.problems[0].msg : (wrote.rebound || []).map(d => d.id + ':' + d.bound).join(',')) + ')');
    const keeps = ['Bridge/A.cs', 'Checks/run.ps1'].every(n => {
      const a = fs.readFileSync(path.join(root, n)), b = before[n];
      return a.slice(0, 3).equals(b.slice(0, 3)) && (a.toString('latin1').match(/\r\n/g) || []).length === (b.toString('latin1').match(/\r\n/g) || []).length && !a.equals(b);
    });
    say(keeps, 'the byte order mark and the line ends of the files are kept, and the files changed');
    const again = run(mk());
    say(again.problems.length === 0 && again.files.every(f => f.status === 'unchanged' || /would apply \(0/.test(f.status)) || again.problems.length === 0, 'apply: a second time changes nothing and refuses nothing ("already applied")');
    say(checkConsumed(root) === 0, 'consumed strings: the contract is green on the written tree');
    const chk = checkAll(root, 'HEAD', {});
    say(chk.rows.every(r => r.problems.length === 0) && chk.red === 0 && chk.left.strings > 0 && chk.left.strings <= 6 && chk.left.json_values === 0 && chk.stats.strict >= 2, 'check: green on the translated tree: the token stream, the invariants, the assertions (' + JSON.stringify(chk.stats) + ', left ' + JSON.stringify(chk.left) + ')');
    // 5. --check red on mutated copies
    const mutate = (file, from, to, what, part) => {
      const full = path.join(root, file);
      const keep = fs.readFileSync(full);
      const text = keep.toString('utf8');
      if (!text.includes(from)) throw new Error('self-test mutation does not apply: ' + from);
      fs.writeFileSync(full, Buffer.from(text.replace(from, to), 'utf8'));
      try {
        const r = checkAll(root, 'HEAD', {});
        const hit = r.rows.some(row => row.problems.some(p => p.includes(part)));
        say(hit, 'check: refused, seen red: ' + what + (hit ? '' : ' - got ' + JSON.stringify(r.rows.map(x => x.problems.slice(0, 1)).filter(x => x.length))));
      } finally { fs.writeFileSync(full, keep); }
    };
    mutate('Bridge/A.cs', 'n > 0', 'n > 1', 'a code token changed under the texts', 'token stream');
    mutate('Bridge/A.cs', 'found ', 'found 7 ', 'a number added to a message', 'numbers');
    mutate('Bridge/A.cs', 'is not on disk', 'does not exist', 'a message translated away from the substring its case asserts', 'no longer holds');
    mutate('Bench/cases/fam/c1.json', '"is not on disk"', '"nowhere at all"', 'an assertion whose text is in no literal', 'points at nothing');
    mutate('Bench/cases/fam/c1.json', '"never happened"', '"never occurred"', 'a not_contains whose text is in no producer', 'not_contains');
    mutate('Bench/cases/fam/c1.json', '"operation": "go"', '"operation": "stop"', 'a case changed outside its texts', 'not Italian');
    mutate('Checks/probe.js', 'rest is stopped|touched', 'rest is stopped+|touched', 'a regular expression with another skeleton', 'skeleton');
    mutate('Bench/cases/fam/c1.json', '"is not on disk"', '"not"', 'a translated assertion wider than the one it replaces', 'WIDER');
    mutate('Oce.Solid.Agent.Core/Ops.cs', 'goes from ', 'runs between ', 'a message of computed values translated away from its assertion', 'from 3 to 7');
    mutate('Tests/T.cs', '"is outside"', '"lies outside"', 'a contract that guards the absence of a text no message holds (green for ever)', 'points at nothing');
    mutate('Oce.Solid.Agent.Core/Ops.cs', '» is outside', '» is beyond', 'a message translated away from the text its contract looks for', 'points at nothing');
    {
      const full = path.join(root, 'Bridge/A.cs');
      const keep = fs.readFileSync(full);
      fs.writeFileSync(full, Buffer.from(keep.toString('utf8').replace('everything else, the rest is stopped, not touched', 'everything else is fine'), 'utf8'));
      try { say(checkConsumed(root) === 1, 'consumed strings: red when a producer no longer holds the text its consumer looks for'); }
      finally { fs.writeFileSync(full, keep); }
    }
    {
      const full = path.join(root, 'Checks/probe.js');
      const keep = fs.readFileSync(full);
      fs.writeFileSync(full, Buffer.from(keep.toString('utf8').replace('"the rest is stopped"', '"the rest has stopped"'), 'utf8'));
      try { say(checkConsumed(root) === 1, 'consumed strings: red when a consumer looks for text that no producer holds'); }
      finally { fs.writeFileSync(full, keep); }
    }
    say(checkAll(root, 'HEAD', {}).rows.every(r => r.problems.length === 0), 'check: green again once the mutated copies are put back');
    // the integration (window-5-integration.json): what the integrator did by hand is read, and held to its rules
    {
      const d = fs.mkdtempSync(path.join(os.tmpdir(), 'window5-integration-'));
      try {
        fs.mkdirSync(path.join(d, 'Checks', 'language'), { recursive: true });
        fs.writeFileSync(path.join(d, 'Checks', 'language', 'exceptions.json'),
          JSON.stringify([{ class: 'solidworks-localized', file: '^Bridge/A\\.cs$', pattern: 'THRU', applies_to: ['detector'], reason: 'a fixture' }]));
        fs.writeFileSync(path.join(d, 'Checks', 'language', 'window-5-integration.json'), JSON.stringify({
          forced_units: [{ file: 'Bridge/A.cs', units: ['Open:s1'] }],
          code_changes: [{ file: 'Tests/T.cs', before: 'int Floor = 1;', after: 'int Floor = 2;' }],
          assertions: [{ file: 'Bench/cases/a/b.json', units: ['$.x'], external: 'a fixture' }],
        }));
        const integ = integrationOf(d);
        say(localizedByThePlatform(d, 'Bridge/A.cs', 'THRU') && !localizedByThePlatform(d, 'Bridge/A.cs', 'THRU and more') && !localizedByThePlatform(d, 'Bridge/B.cs', 'THRU'),
          'integration: a text an Italian SOLIDWORKS writes is exempt only whole, and only in the file the exception names');
        say(integ.forced.has('Bridge/A.cs#Open:s1') && !integ.forced.has('Bridge/A.cs#Open:s2') && integ.assertions.has('Bench/cases/a/b.json#$.x'),
          'integration: the forced units and the declared assertions are read, and only those');
        say(applySnippets('x int Floor = 1; y', 'Tests/T.cs', integ) === 'x int Floor = 2; y' && applySnippets('x int Floor = 1; y', 'Tests/U.cs', integ) === 'x int Floor = 1; y',
          'integration: a code change is applied to the tree before the window, in its file only');
        let refused = false;
        try { applySnippets('int Floor = 1; int Floor = 1;', 'Tests/T.cs', integ); } catch (e) { refused = e instanceof Stop; }
        let refusedAbsent = false;
        try { applySnippets('nothing here', 'Tests/T.cs', integ); } catch (e) { refusedAbsent = e instanceof Stop; }
        say(refused && refusedAbsent, 'integration: a snippet that is in its file twice, or not at all, is refused (a stale change cannot pass in silence)');
      } finally { fs.rmSync(d, { recursive: true, force: true }); }
    }
    void gitq;
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
    fs.rmSync(root, { recursive: true, force: true });
  }
  console.log('');
  console.log(ok + ' of ' + total + ' cases as expected.');
  if (bad.length) console.log('NOT AS EXPECTED: ' + bad.join(' | '));
  return bad.length === 0 ? 0 : 1;
}

Object.assign(module.exports, { selfTest });
// ── main ────────────────────────────────────────────────────────────────────
function parseArgs(argv) {
  const a = { root: ROOT_DEFAULT, extract: null, apply: null, check: false, survey: false, selfTest: false, dry: false, rev: 'HEAD', only: null, zero: false, verbose: false, max: null, base: null, pilot: null };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    const next = () => { if (i + 1 >= argv.length) cannot(x + ' needs a value'); return argv[++i]; };
    if (x === '--extract') a.extract = path.resolve(next());
    else if (x === '--apply') a.apply = path.resolve(next());
    else if (x === '--check') a.check = true;
    else if (x === '--survey') a.survey = true;
    else if (x === '--self-test') a.selfTest = true;
    else if (x === '--dry') a.dry = true;
    else if (x === '--zero') a.zero = true;
    else if (x === '--verbose') a.verbose = true;
    else if (x === '--rev') a.rev = next();
    else if (x === '--base') a.base = next();
    else if (x === '--root') a.root = path.resolve(next());
    else if (x === '--chunk') a.only = next().split(',');
    else if (x === '--max') a.max = parseInt(next(), 10);
    else if (x === '--pilot') a.pilot = next().split(',');
    else cannot('unknown argument ' + x);
  }
  if (![a.extract, a.apply, a.check, a.survey, a.selfTest].some(Boolean)) cannot('one of --extract <dir>, --apply <dir>, --check, --survey, --self-test');
  return a;
}

function main() {
  const a = parseArgs(process.argv.slice(2));
  if (a.selfTest) return selfTest();
  if (a.extract) return runExtract(a.root, a.extract, { max: a.max, pilot: a.pilot });
  if (a.apply) return runApply(a.root, a.apply, { dry: a.dry, only: a.only, verbose: a.verbose });
  if (a.survey) return runSurvey(a.root);
  return runCheck(a.root, a.rev, { zero: a.zero });
}

if (require.main === module) {
  try {
    process.exitCode = main();
  } catch (e) {
    if (e instanceof Stop || e instanceof TS.GateError || e instanceof SCAN.ScanError) { console.log('window-5-messages: ' + e.message); process.exitCode = 2; }
    else { console.log('window-5-messages: ' + (e && e.stack ? e.stack : e)); process.exitCode = 2; }
  }
}
