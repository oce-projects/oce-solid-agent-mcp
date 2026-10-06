#!/usr/bin/env node
// =============================================================================
//  window-6-catalogue.js - the descriptions of the catalogue (W3.1-bis.9)
// =============================================================================
//
//  Translates into English the texts that DECIDE WHICH OPERATION THE MODEL
//  CHOOSES, and proves it: the summaries, descriptions and schemas of
//  Oce.Solid.Agent.Core/Catalog/Operations.cs, the family lines, the exporter's
//  own messages, the titles and descriptions of the MCP resources
//  (Mcp/Risorse.cs), the descriptions of the tools and of their parameters
//  (Tools/McpTools.cs), and the lexicons of the three contracts that read those
//  descriptions (DescriptionsTests, DescriptionValuesContract, LimitsTests).
//  Tools/catalog.json is NOT translated: build.ps1 regenerates it from the C#
//  catalogue, and this tool proves what the regeneration did (--catalog-proof).
//
//  WHY IT IS NOT WINDOW 5 AGAIN. A message is read when something goes wrong; a
//  description is read BEFORE every call, and it is the only thing the model
//  has to choose between ninety-nine operations: «use X instead of Y», «I
//  REFUSE IF», every limit, every distinction that sets one operation apart
//  from its sister is the text. A translation that keeps the words and loses
//  one of those makes the model choose worse, and nothing in the bench sees it
//  (the bench proves the operation works, not that the model asks for it). So
//  the proof here is of two kinds: what a machine can prove (below), and what
//  only a reader can (the reviewer's question is «what does the description
//  TEACH», not «what does it say»; and the autonomous round of D9).
//
//  IT IS THE SAME MACHINE AS window-5-messages.js, with another PROFILE (see
//  setProfile there): the parsers, the units, the invariants of a translation
//  (numbers, references, backticks, file names, guillemets...), the assertions,
//  the probes and the consumers of the inventory of consumed strings, the
//  token stream outside string literals, and --check against the tree before
//  the window all come from it. What is different:
//
//   - THE SCOPE is the files above. Everything else is deferred: its Italian is
//     not an item, but a program that looks for the text of a description (a
//     contract, a probe of the bench, a consumer of the inventory) is still
//     linked to it and changes in the same run.
//   - THE UNIT IS A FAMILY OF OPERATIONS, not a file. Operations.cs is one file
//     of 5,700 lines; a chunk is the operations of one family (a family over
//     3,300 Italian words is cut by operation, in source order), the small
//     ones joined up to 1,600 words. The family of a unit is read from the
//     OperationSpec that contains it.
//   - A TEXT WITHOUT HOLES IS TRANSLATED WHOLE. A description is a chain of
//     literals, one per source line, and a translator that has to give back the
//     same number of pieces writes worse text. The chunk offers `text` (the
//     pieces joined); the translator gives `text`; the tool cuts it back into
//     as many pieces as the source has, at a space or at a paragraph break,
//     near where the old pieces ended (the compiler folds the constants: the
//     IL is the same whatever the cut). `parts` is still accepted.
//   - THE REFUSAL KEYWORD is checked in every translation (extraProblems): a
//     paragraph that began with the Italian keyword (or with «I refuse») must come
//     out as a paragraph that begins exactly «I REFUSE IF », as many as there
//     were, so the declaration of a limit keeps one shape across the catalogue
//     (and Tests/RefusalKeywordsContract.cs holds it there).
//   - THE CATALOGUE PROOF (--catalog-proof): Tools/catalog.json now against the
//     one at the tree before the window: the same operations in the same
//     order, the same families, the same keys, every schema the same tree
//     except the text of its descriptions, `family`, `precondition`,
//     `document` and `example` byte for byte; and, with --exporter, that the
//     file is what the built exporter writes (a catalogue edited by hand
//     would differ).
//
//  USAGE (all of it needs node --expose-internals unless acorn is on the path;
//  the script starts itself that way)
//    node Checks/language/window-6-catalogue.js --extract <dir>
//        chunks and plan.json; the plan names every chunk (cat-<families>),
//        its Italian words, and what it requires
//    node Checks/language/window-6-catalogue.js --apply <dir> [--chunk a,b] [--dry]
//        the translations <id>.en.json: { id, items: [{ id, text }] } (or
//        `parts`), refused as window 5 refuses, plus the refusal keyword
//    node Checks/language/window-6-catalogue.js --check [--rev <rev>] [--zero]
//        the static check against <rev> (the tree before the window), then
//        the catalogue proof and the refusal keyword count
//    node Checks/language/window-6-catalogue.js --catalog-proof [--rev <rev>] [--exporter <exe>]
//    node Checks/language/window-6-catalogue.js --ratchet
//        writes Tests/data/refusal-keyword-ceiling.txt: the number of declarations of a limit that are not in the
//        one form, exact (the contract reads it; --check holds it too). Run it after the catalogue is regenerated
//    node Checks/language/window-6-catalogue.js --survey
//    node Checks/language/window-6-catalogue.js --self-test
//    --root <dir>        another checkout (default: the repository of this file)
//
//  EXIT CODES
//    0  done, nothing refused
//    1  a refusal or a difference outside the texts, a proof that fails, or --self-test red
//    2  cannot run
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');
const crypto = require('crypto');

const SCAN = require('./window-5-scan.js');
const DET = require('./detect-italian.js');
const W5 = require('./window-5-messages.js');

// the script starts itself with the flag that lets token-stream.js reach the acorn inside Node
if (require.main === module && !SCAN.hasAcorn() && !process.execArgv.includes('--expose-internals')) {
  const r = cp.spawnSync(process.execPath, ['--expose-internals', __filename].concat(process.argv.slice(2)), { stdio: 'inherit' });
  process.exit(r.status === null ? 2 : r.status);
}

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const OPERATIONS = 'Oce.Solid.Agent.Core/Catalog/Operations.cs';
const CATALOG_JSON = 'Tools/catalog.json';

// The Italian this tool has to read (the old forms of the refusal keyword) and that its fixtures need is written in ROT13 and undone
// here: the file is judged by the language gate, and a gate that counted the tool's own Italian would ask for a baseline it has no right to.
const rot13 = (s) => s.replace(/[a-z]/gi, (c) => { const b = c <= 'Z' ? 65 : 97; return String.fromCharCode((c.charCodeAt(0) - b + 13) % 26 + b); });

// What belongs to this window. A file outside it is deferred: its Italian is not an item here.
const SCOPE = [
  /^Oce\.Solid\.Agent\.Core\/Catalog\//,               // the catalogue and its exporter's messages
  /^Oce\.Solid\.Agent\.Catalog\//,                     // the exporter's program
  /^Mcp\/Risorse\.cs$/,                                // the titles and descriptions of the MCP resources
  /^Tools\/McpTools\.cs$/,                             // the descriptions of the tools and of their parameters
  /^Tests\/(DescriptionsTests|DescriptionValuesContract|LimitsTests)\.cs$/,   // the lexicons that read the descriptions
];
const inScope = (rel) => SCOPE.some(re => re.test(rel));
const GENERATED = [/^Tools\/catalog\.json$/];

const PART_WORDS = 3300;      // a family over this many Italian words is cut by operation
const MAX_CHUNK_WORDS = 1600; // small atoms are joined up to this

class Stop extends Error {}
function cannot(msg) { throw new Stop(msg); }

// ── the operations of Operations.cs, and the family of a line ────────────────
// An OperationSpec starts at a line `new OperationSpec` and ends at the closing brace of the same indentation.
function opIndex(text) {
  const lines = text.replace(/\r\n/g, '\n').split('\n');
  const ops = [];
  for (let i = 0; i < lines.length; i++) {
    const m = /^(\s*)new OperationSpec\s*$/.exec(lines[i]);
    if (!m) continue;
    const indent = m[1];
    let end = lines.length;
    for (let j = i + 1; j < lines.length; j++) {
      if (lines[j].startsWith(indent + '}') && !lines[j].startsWith(indent + ' ')) { end = j + 1; break; }
    }
    const op = { start: i + 1, end, name: null, family: null };
    for (let j = i + 1; j < end; j++) {
      let x;
      if (!op.name && (x = /^\s*Name = "([^"]+)"/.exec(lines[j]))) op.name = x[1];
      if (!op.family && (x = /^\s*Family = "([^"]+)"/.exec(lines[j]))) op.family = x[1];
      if (op.name && op.family) break;
    }
    ops.push(op);
  }
  return ops;
}

const indexCache = new WeakMap();
// { ops, partOf: Map(op name -> part number inside its family), wordsOf: Map(op name -> Italian words) } for the model
function indexOf(model) {
  if (indexCache.has(model)) return indexCache.get(model);
  const f = model.files.get(OPERATIONS);
  const idx = { ops: [], partOf: new Map(), wordsOf: new Map() };
  if (f) {
    idx.ops = opIndex(f.text);
    for (const u of f.units) {
      if (!u.italian) continue;
      const op = idx.ops.find(o => o.start <= u.line && u.line <= o.end);
      if (op) idx.wordsOf.set(op.name, (idx.wordsOf.get(op.name) || 0) + u.words);
    }
    const perFamily = new Map();
    for (const op of idx.ops) {
      const st = perFamily.get(op.family) || { part: 1, acc: 0 };
      const w = idx.wordsOf.get(op.name) || 0;
      if (st.acc > 0 && st.acc + w > PART_WORDS) { st.part++; st.acc = 0; }
      st.acc += w;
      perFamily.set(op.family, st);
      idx.partOf.set(op.name, st.part);
    }
    idx.parts = new Map([...perFamily].map(([k, v]) => [k, v.part]));
  }
  indexCache.set(model, idx);
  return idx;
}

// The part of a file that is translated as one: the operations of one family (or one piece of a big one) in Operations.cs.
function atomOf(item, model) {
  if (item.file !== OPERATIONS) return item.file;
  const idx = indexOf(model);
  const op = idx.ops.find(o => o.start <= item.line && item.line <= o.end);
  if (!op) return 'ops/families';
  const parts = idx.parts.get(op.family) || 1;
  return 'ops/' + op.family + (parts > 1 ? '.' + idx.partOf.get(op.name) : '');
}

// a short name for a part of a file: the family, or the folder and the file (Catalog/Node.cs is catalog-node)
const shortOf = (atom) => (atom.startsWith('ops/') ? atom.slice(4) : atom.replace(/\.[a-z]+$/, '').split('/').slice(-2).join('-').replace(/^Oce\.Solid\.Agent\./, '').toLowerCase());
function nameChunk(chunk, chunks) {
  const atoms = [...new Set(chunk.items.map(i => i.atom || i.file))];
  if (chunk.tag === 'wide' || chunk.folder === 'linked') return chunk.id;   // the clusters keep the name the planner gave
  if (atoms.every(a => /^Tests\//.test(a))) return 'cat-lexicons';   // the lexicons that read the descriptions: the last chunk, when no Italian description is left
  const names = atoms.map(shortOf);
  let id = 'cat-' + (names.length > 3 ? names.slice(0, 2).join('+') + '+' + (names.length - 2) + 'more' : names.join('+'));
  id = id.replace(/[^A-Za-z0-9.+_-]/g, '_');
  const taken = new Set(chunks.filter(c => c !== chunk && c.id).map(c => c.id));
  let n = 1, out = id;
  while (taken.has(out)) out = id + '-' + (++n);
  return out;
}

// ── THE REFUSAL KEYWORD ─────────────────────────────────────────────────────
// A paragraph that declares a limit begins with a keyword; in Italian it was the first-person «I refuse» of that language in
// capitals and then «if» (sometimes in lowercase, sometimes a third-person form: written in ROT13 below, this file is judged by the
// language gate), and in the English that was already there «I refuse when...». The English keyword is one, exactly: «I REFUSE IF ».
// (Tests/RefusalKeywordsContract.cs holds the same one form, and reads the same old forms in ROT13.)
const KEYWORD = 'I REFUSE IF ';
const OLD_DECLARATION = new RegExp('^(?:' + rot13('ZV EVSVHGB') + '|' + rot13('EVSVHGN') + '|' + rot13('Evsvhgn') + '|I REFUSE|I refuse)\\b');
const paragraphsOf = (text) => text.split('\n\n');
function declarationsOf(text) { return paragraphsOf(text).filter(p => OLD_DECLARATION.test(p)); }
// the problems of one translation: the declarations of a limit are as many as they were, and every one begins with the keyword
function refusalProblems(oldText, newText) {
  const out = [];
  const was = declarationsOf(oldText), now = declarationsOf(newText);
  for (const p of now) if (!p.startsWith(KEYWORD)) out.push('a paragraph that declares a limit begins «' + p.slice(0, 24) + '...» and must begin «' + KEYWORD.trim() + '» (the keyword is one across the catalogue)');
  const itAnywhere = (oldText.match(new RegExp(rot13('ZV EVSVHGB FR'), 'gi')) || []).length;
  const enAnywhere = (newText.match(/I REFUSE IF/g) || []).length;
  if (now.length !== was.length) out.push('the declarations of a limit were ' + was.length + ' (paragraphs that begin with the Italian keyword or with «I refuse») and are ' + now.length + ': a limit was dropped or one was made up ("keywords_ok": true if it is meant)');
  if (enAnywhere < itAnywhere) out.push('the Italian keyword appears ' + itAnywhere + ' times and «I REFUSE IF» ' + enAnywhere + ': the keyword is not translated as it is');
  return out;
}

// a translation is about as long as the text it translates: Italian into English runs between a half and twice. A description that
// comes out much shorter lost a sentence (a limit, a «use X instead»); much longer, it made one up.
function lengthProblems(oldText, newText) {
  const out = [];
  if (oldText.trim() && !newText.trim()) return ['the translation is empty'];
  if (oldText.trim().length >= 40) {
    const ratio = newText.trim().length / oldText.trim().length;
    if (ratio < 0.5) out.push('the translation is ' + Math.round(ratio * 100) + '% of the length of the original: a sentence was dropped? ("length_ok": true if it is meant)');
    if (ratio > 2) out.push('the translation is ' + Math.round(ratio * 100) + '% of the length of the original: a sentence was made up? ("length_ok": true if it is meant)');
  }
  return out;
}

// ── what a description may not lose: the words the operation takes as they are ──
// A description teaches the model the values of an argument. When the protocol is still Italian (the values of the mate: the words
// for coincident and for aligned), the English description names them as they are: a translator that renders one of them in
// English teaches the model a value the operation refuses. Two guards:
//   - a name or a word between guillemets that is ONE token («grounded», «mode», «C1») is protocol: it stays, as many times;
//   - a single-token VALUE that a case of the bench passes in the arguments of THAT operation (the only truth about what it takes),
//     and that the old description names, is still named in the new one.
// A translator that has a reason says so in the translation: "quoted_ok": true, "protocol_ok": ["token", ...] (and
// "length_ok", "keywords_ok"), and the integrator copies what he accepts into window-6-integration.json ("overrides", by file and unit)
// so that --check holds it too.
const CTX = { root: null, ops: null, values: null, overrides: null };
function setRoot(root) {
  if (CTX.root === root) return;
  CTX.root = root; CTX.ops = null; CTX.values = null; CTX.overrides = null;
}
function walkJsonFiles(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) walkJsonFiles(full, out); else if (e.name.endsWith('.json')) out.push(full);
  }
  return out;
}
// operation -> Set of the single-token string values the cases of the bench pass to it
function operationValues(root) {
  const map = new Map();
  const dir = path.join(root, 'Bench', 'cases');
  if (!fs.existsSync(dir)) return map;
  const token = /^[A-Za-z][A-Za-z0-9_]{3,}$/;
  const collect = (set, v) => {
    if (typeof v === 'string') { if (token.test(v)) set.add(v); } else if (Array.isArray(v)) v.forEach(x => collect(set, x));
    else if (v && typeof v === 'object') Object.values(v).forEach(x => collect(set, x));
  };
  const walk = (o) => {
    if (Array.isArray(o)) { o.forEach(walk); return; }
    if (!o || typeof o !== 'object') return;
    if (typeof o.operation === 'string' && o.arguments && typeof o.arguments === 'object') {
      if (!map.has(o.operation)) map.set(o.operation, new Set());
      collect(map.get(o.operation), o.arguments);
    }
    Object.values(o).forEach(walk);
  };
  for (const f of walkJsonFiles(dir, [])) {
    let j;
    try { j = JSON.parse(fs.readFileSync(f, 'utf8').replace(/^\ufeff/, '')); } catch (e) { continue; }
    walk(j);
  }
  return map;
}
function context() {
  if (!CTX.root) cannot('window-6-catalogue: no root set for the checks of a translation');
  if (!CTX.ops) {
    try { CTX.ops = opIndex(fs.readFileSync(path.join(CTX.root, OPERATIONS), 'utf8')); } catch (e) { CTX.ops = []; }
    CTX.values = operationValues(CTX.root);
    CTX.overrides = new Map();
    try {
      const j = JSON.parse(fs.readFileSync(path.join(CTX.root, 'Checks', 'language', PROFILE.integration), 'utf8').replace(/^\ufeff/, ''));
      for (const o of j.overrides || []) CTX.overrides.set(o.file + '#' + o.unit, o);
    } catch (e) { /* no integration file */ }
  }
  return CTX;
}
const wordIn = (text, token) => new RegExp('(?<![A-Za-z0-9_])' + token.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '(?![A-Za-z0-9_])').test(text);
const quotedTokens = (s) => [...s.matchAll(/\u00ab([^\u00bb\s]+)\u00bb/g)].map(m => m[1]).sort();
function sameMultiset(a, b) {
  const count = (l) => { const m = new Map(); for (const x of l) m.set(x, (m.get(x) || 0) + 1); return m; };
  const A = count(a), B = count(b), missing = [], extra = [];
  for (const [k, n] of A) for (let i = B.get(k) || 0; i < n; i++) missing.push(k);
  for (const [k, n] of B) for (let i = A.get(k) || 0; i < n; i++) extra.push(k);
  return { missing, extra };
}
function protocolProblems(oldText, newText, opName, flags, values) {
  const out = [];
  if (!flags.quoted_ok) {
    const d = sameMultiset(quotedTokens(oldText), quotedTokens(newText));
    if (d.missing.length || d.extra.length) out.push('the names between guillemets changed' + (d.missing.length ? ', lost ' + JSON.stringify(d.missing.slice(0, 6)) : '') + (d.extra.length ? ', added ' + JSON.stringify(d.extra.slice(0, 6)) : '') + ' (a single word between guillemets is a name of the protocol; "quoted_ok": true if it is prose)');
  }
  const set = opName && values.get(opName);
  if (set) {
    const lost = [...set].filter(v => wordIn(oldText, v) && !wordIn(newText, v) && !(flags.protocol_ok || []).includes(v));
    if (lost.length) out.push('the value' + (lost.length > 1 ? 's ' : ' ') + lost.slice(0, 6).map(v => '«' + v + '»').join(', ') + ' of ' + opName + ' (a case of the bench passes it in the arguments, so the operation takes it as it is) is named in the original and not in the translation ("protocol_ok": [...] if it was prose)');
  }
  return out;
}

function extraProblems(item, newParts) {
  const ctx = context();
  const declared = ctx.overrides.get(item.file + '#' + item.unit) || {};
  const t = Object.assign({}, declared, item.tr || {});
  const oldText = item.parts.join(''), newText = newParts.join('');
  const out = [];
  if (!t.length_ok || !newText.trim()) out.push(...lengthProblems(oldText, newText));
  if (!t.keywords_ok) out.push(...refusalProblems(oldText, newText));
  const op = item.file === OPERATIONS ? (ctx.ops.find(o => o.start <= item.line && item.line <= o.end) || {}).name : null;
  out.push(...protocolProblems(oldText, newText, op, t, ctx.values));
  return out;
}

// ── THE TEXT, CUT BACK INTO ITS PIECES ──────────────────────────────────────
// `text` is the translation of a unit without holes as one piece; the unit has `oldParts.length` literals. A cut keeps where the old
// text was cut when the words around it are still there, unchanged (the English that was already in a description keeps its lines, and
// the diff shows only what was translated); a paragraph break of the old text is a paragraph break of the new when both have the same
// number of them; any other cut goes at a space or a paragraph break, as close as it can to where the old pieces ended.
function distribute(oldParts, text) {
  const n = oldParts.length;
  if (n === 1) return [text];
  const oldText = oldParts.join('');
  const bounds = [];
  let acc = 0;
  for (let i = 0; i < n - 1; i++) { acc += oldParts[i].length; bounds.push(acc); }
  const allowed = [];
  for (let i = 1; i < text.length; i++) {
    const a = text[i - 1], b = text[i];
    const ok = (a === ' ' && b !== ' ' && b !== '\n') || (a === '\n' && b !== '\n') || (b === '\n' && a !== '\n' && a !== ' ');
    if (ok) allowed.push(i);
  }
  if (allowed.length < n - 1) return null;
  const breaks = (s) => { const r = []; let p = -1; while ((p = s.indexOf('\n\n', p + 1)) >= 0) { r.push(p); while (s[p + 1] === '\n') p++; } return r; };
  const oldBreaks = breaks(oldText), newBreaks = breaks(text);
  const anchor = new Map();
  // 1. the words on both sides of the old cut are in the new text, once, as they were
  const SIDE = 24;
  const once = (s, x) => { const i = s.indexOf(x); return i >= 0 && i === s.lastIndexOf(x) ? i : -1; };
  bounds.forEach((b, j) => {
    const from = Math.max(0, b - SIDE), to = Math.min(oldText.length, b + SIDE);
    const ctx = oldText.slice(from, to);
    if (b - from < 8 || to - b < 8) return;
    if (once(oldText, ctx) < 0) return;
    const at = once(text, ctx);
    if (at >= 0) anchor.set(j, at + (b - from));
  });
  // 2. the paragraph breaks, when there are as many
  if (oldBreaks.length && oldBreaks.length === newBreaks.length) {
    bounds.forEach((b, j) => {
      if (anchor.has(j)) return;
      const k = oldBreaks.indexOf(b);
      if (k >= 0) { anchor.set(j, newBreaks[k]); return; }
      const k2 = oldBreaks.findIndex(p => { let q = p; while (oldText[q] === '\n') q++; return q === b; });
      if (k2 >= 0) { let q = newBreaks[k2]; while (text[q] === '\n') q++; anchor.set(j, q); }
    });
  }
  const cuts = [];
  let prev = 0;
  for (let j = 0; j < n - 1; j++) {
    const remaining = n - 2 - j;
    let target = anchor.has(j) ? anchor.get(j) : Math.round(bounds[j] * text.length / oldText.length);
    let best = -1, bestD = Infinity;
    if (anchor.has(j) && anchor.get(j) > prev && anchor.get(j) < text.length && text.length - anchor.get(j) >= 1 && allowed.filter(i => i > anchor.get(j)).length >= remaining) { cuts.push(anchor.get(j)); prev = anchor.get(j); continue; }
    for (let k = 0; k < allowed.length; k++) {
      const i = allowed[k];
      if (i <= prev) continue;
      if (allowed.length - 1 - k < remaining) break;       // not enough room left for the other cuts
      const d = Math.abs(i - target);
      if (d < bestD) { best = i; bestD = d; }
      if (i > target) break;
    }
    if (best < 0) return null;
    cuts.push(best);
    prev = best;
  }
  const out = [];
  let from = 0;
  for (const c of cuts) { out.push(text.slice(from, c)); from = c; }
  out.push(text.slice(from));
  return out;
}

// the translations of a run: an item given as `text` becomes its parts
function deriveParts(list) {
  const problems = [];
  for (const { id, chunk, translated } of list) {
    if (!translated) continue;
    const byId = new Map(chunk.items.map(i => [i.id, i]));
    for (const t of translated.items || []) {
      const it = byId.get(t.id);
      if (it) it.tr = t;   // what the translator said, for extraProblems
      if (t.text === undefined || t.parts !== undefined) continue;
      if (!it) continue;
      if (typeof t.text !== 'string') { problems.push({ where: id + ' ' + t.id, msg: '"text" is not a string' }); continue; }
      if (it.holes.length || it.fixed.length) { problems.push({ where: id + ' ' + it.file + '#' + it.unit, msg: 'this unit has holes or fixed text: give "parts", one per piece' }); continue; }
      const parts = distribute(it.parts, t.text);
      if (!parts || parts.join('') !== t.text) { problems.push({ where: id + ' ' + it.file + '#' + it.unit, msg: 'the text cannot be cut into the ' + it.parts.length + ' pieces of the source (too few places to cut)' }); continue; }
      t.parts = parts;
      t.edges_ok = true;
    }
  }
  return problems;
}

// ── the profile ─────────────────────────────────────────────────────────────
const PROFILE = {
  name: 'window 6', deferredLabel: 'what is outside the catalogue window', outsideLabel: 'the files of the catalogue window (strings, json_values)', pendingLabel: 'nothing pending',
  deferred: (rel) => !inScope(rel),
  notProducer: (rel) => GENERATED.some(re => re.test(rel)),
  notAssertionProducer: inScope, probesOfItemsOnly: true,
  integration: 'window-6-integration.json', generated: GENERATED,
  atomOf, perAtom: true, maxWords: MAX_CHUNK_WORDS, nameChunk, extraProblems,
};
function useProfile() { W5.setProfile(PROFILE); }

// ── --extract ───────────────────────────────────────────────────────────────
function runExtract(root, dir, opts) {
  useProfile();
  setRoot(root);
  const code = W5.runExtract(root, dir, { max: opts && opts.max });
  // a unit without holes and without fixed text is offered whole
  let offered = 0;
  for (const name of fs.readdirSync(dir)) {
    if (!name.endsWith('.json') || name === 'plan.json' || name.endsWith('.en.json')) continue;
    const file = path.join(dir, name);
    const c = JSON.parse(fs.readFileSync(file, 'utf8'));
    for (const it of c.items) {
      if (it.holes.length === 0 && it.fixed.length === 0 && it.class !== 'assertion') { it.text = it.parts.join(''); it.pieces = it.parts.length; offered++; }
    }
    c.about = 'A unit with "text" is translated whole: give { "id", "text" }; the tool cuts it into the "pieces" literals of the source. Any other unit: { "id", "parts" } with as many parts as it has.';
    fs.writeFileSync(file, JSON.stringify(c, null, 1) + '\n');
  }
  console.log('units offered whole (text): ' + offered);
  return code;
}

// ── --apply ─────────────────────────────────────────────────────────────────
function runApply(root, dir, opts) {
  useProfile();
  setRoot(root);
  const list = W5.loadChunks(dir, opts.only);
  if (!list.length) cannot('no chunk in ' + dir);
  for (const x of list) if (!x.translated) console.log(x.id + ': no translation yet (' + x.id + '.en.json)');
  const have = list.filter(x => x.translated);
  if (!have.length) return 0;
  console.log('apply' + (opts.dry ? ' (dry)' : '') + ': ' + have.map(x => x.id + ' (' + x.chunk.items.length + ' items)').join(', '));
  const cutProblems = deriveParts(have);
  const r = cutProblems.length ? { problems: cutProblems, files: [], warnings: [], wrote: false } : W5.applyRun(root, have, opts);
  for (const p of r.problems.slice(0, 80)) console.log('  REFUSED ' + p.where + ': ' + p.msg);
  if (r.problems.length > 80) console.log('  ... ' + (r.problems.length - 80) + ' more');
  for (const f of r.files) console.log('  ' + f.status + ' ' + f.file);
  for (const w of (r.warnings || []).filter(w => !/kept as it is/.test(w)).slice(0, 40)) console.log('      note: ' + w);
  const kept = (r.warnings || []).filter(w => /kept as it is/.test(w));
  if (kept.length) console.log('      note: ' + kept.length + ' items kept as they are');
  for (const d of r.rebound || []) console.log('  consumed strings: ' + d.id + (d.bound ? ' bound again (' + d.was + ' -> ' + d.now + ')' : ' NOT bound: ' + d.why));
  console.log('');
  if (r.problems.length) { console.log('apply: ' + r.problems.length + ' refusals; nothing was written.'); return 1; }
  if (!opts.dry && W5.checkConsumed(root)) return 1;
  console.log('apply' + (opts.dry ? ' (dry)' : '') + ': ' + r.files.filter(f => /appl/.test(f.status)).length + ' files ' + (opts.dry ? 'would be written' : 'written') + ', 0 refused.');
  return 0;
}

// ── THE CATALOGUE PROOF ─────────────────────────────────────────────────────
function gitShow(root, rev, rel) {
  try { return cp.execFileSync('git', ['-C', root, 'show', rev + ':' + rel], { maxBuffer: 1 << 28, stdio: ['ignore', 'pipe', 'pipe'] }); }
  catch (e) { cannot('git show ' + rev + ':' + rel + ' failed: ' + String(e.stderr || e.message).trim()); }
}
const parseJson = (buf) => JSON.parse(buf.toString('utf8').replace(/^﻿/, ''));
const typeOf = (v) => (v === null ? 'null' : Array.isArray(v) ? 'array' : typeof v);

// same tree, except the value of a "description" (and anything `free` says): returns the paths that differ
function sameTreeExceptText(a, b, p, free, out, stats) {
  if (typeOf(a) !== typeOf(b)) { out.push(p + ' (type ' + typeOf(a) + ' -> ' + typeOf(b) + ')'); return; }
  if (Array.isArray(a)) {
    if (a.length !== b.length) { out.push(p + ' (length ' + a.length + ' -> ' + b.length + ')'); return; }
    a.forEach((x, i) => sameTreeExceptText(x, b[i], p + '[' + i + ']', free, out, stats));
    return;
  }
  if (a && typeof a === 'object') {
    const ka = Object.keys(a), kb = Object.keys(b);
    if (ka.join('\u0001') !== kb.join('\u0001')) { out.push(p + ' (keys ' + ka.join(',') + ' -> ' + kb.join(',') + ')'); return; }
    for (const k of ka) sameTreeExceptText(a[k], b[k], p + '.' + k, free, out, stats);
    return;
  }
  if (a === b) { stats.same++; return; }
  if (typeof a === 'string' && free(p)) { stats.text++; return; }
  out.push(p + ' (' + JSON.stringify(a).slice(0, 40) + ' -> ' + JSON.stringify(b).slice(0, 40) + ')');
}

// what the proof compares: the catalogue at `rev` and the one now. Returns { problems, stats }.
function catalogProof(root, rev, opts) {
  opts = opts || {};
  const was = parseJson(gitShow(root, rev, CATALOG_JSON));
  const nowBuf = fs.readFileSync(path.join(root, CATALOG_JSON));
  const now = parseJson(nowBuf);
  const problems = [];
  const stats = { operations: 0, families: 0, same: 0, text: 0, texts_of: {} };
  const keys = (o) => Object.keys(o).join(',');
  if (keys(was) !== keys(now)) problems.push('the top level keys changed: ' + keys(was) + ' -> ' + keys(now));
  // the families: the same keys in the same order, the line of each is text
  const fw = Object.keys(was.families || {}), fn = Object.keys(now.families || {});
  if (fw.join(',') !== fn.join(',')) problems.push('the families changed: ' + fw.join(',') + ' -> ' + fn.join(','));
  stats.families = fn.length;
  for (const k of fw) if (typeof was.families[k] !== 'string' || typeof now.families[k] !== 'string') problems.push('family ' + k + ' is not a line of text');
  // the operations: the same names in the same order
  const ow = (was.operations || []).map(o => o.name), on = (now.operations || []).map(o => o.name);
  if (ow.join(',') !== on.join(',')) {
    const gone = ow.filter(x => !on.includes(x)), added = on.filter(x => !ow.includes(x));
    problems.push('the operations changed' + (gone.length ? ', gone: ' + gone.join(',') : '') + (added.length ? ', new: ' + added.join(',') : '') + (!gone.length && !added.length ? ' (the order)' : ''));
  }
  stats.operations = on.length;
  const byName = new Map((now.operations || []).map(o => [o.name, o]));
  const exampleDeclared = new Set(opts.examples || []);
  for (const a of was.operations || []) {
    const b = byName.get(a.name);
    if (!b) continue;
    if (keys(a) !== keys(b)) { problems.push(a.name + ': the keys of the operation changed: ' + keys(a) + ' -> ' + keys(b)); continue; }
    for (const k of ['name', 'family', 'document'])
      if (a[k] !== b[k]) problems.push(a.name + ': ' + k + ' changed (' + JSON.stringify(a[k]) + ' -> ' + JSON.stringify(b[k]) + ')');
    // the precondition is a code (document_without_body) or, for some operations, a sentence: only a sentence is translated
    if (a.precondition !== b.precondition && !(typeof a.precondition === 'string' && /\s/.test(a.precondition) && typeof b.precondition === 'string' && b.precondition.trim()))
      problems.push(a.name + ': precondition changed (' + JSON.stringify(a.precondition) + ' -> ' + JSON.stringify(b.precondition) + '); only a precondition written as a sentence is translated, a code stays');
    if (typeof a.summary !== 'string' || typeof b.summary !== 'string') problems.push(a.name + ': summary is not text');
    if (typeof a.details !== 'string' || typeof b.details !== 'string') problems.push(a.name + ': details is not text');
    // the schema: the same tree except the text of the descriptions
    const out = [];
    const st = { same: 0, text: 0 };
    sameTreeExceptText(a.schema, b.schema, 'schema', (p) => /\.description$/.test(p), out, st);
    for (const o of out) problems.push(a.name + ': ' + o);
    stats.same += st.same; stats.text += st.text;
    // the example: a JSON object written as text. The same keys, the same values (a name of the tree changes only if declared)
    if (a.example !== b.example) {
      if (exampleDeclared.has(a.name)) stats.text++;
      else {
        let ea, eb;
        try { ea = JSON.parse(a.example); eb = JSON.parse(b.example); } catch (e) { ea = undefined; }
        const out2 = [];
        const st2 = { same: 0, text: 0 };
        if (ea === undefined) out2.push('example (not JSON)'); else sameTreeExceptText(ea, eb, 'example', () => false, out2, st2);
        problems.push(a.name + ': the example changed' + (out2.length ? ' (' + out2.slice(0, 2).join('; ') + ')' : ' only in spelling') + '; a name of the SOLIDWORKS tree in an example changes only if declared in ' + PROFILE.integration + ' ("examples")');
      }
    }
    if (a.summary !== b.summary) stats.texts_of[a.name] = (stats.texts_of[a.name] || 0) + 1;
    if (a.details !== b.details) stats.texts_of[a.name] = (stats.texts_of[a.name] || 0) + 1;
  }
  // the exporter writes this file, byte for byte
  if (opts.exporter) {
    const tmp = path.join(os.tmpdir(), 'catalog-proof-' + process.pid + '.json');
    const r = cp.spawnSync(opts.exporter, [tmp], { encoding: 'utf8' });
    if (r.status !== 0 || !fs.existsSync(tmp)) problems.push('the exporter ' + opts.exporter + ' did not write the catalogue (exit ' + r.status + ')');
    else {
      const written = fs.readFileSync(tmp);
      fs.unlinkSync(tmp);
      if (!written.equals(nowBuf)) problems.push('Tools/catalog.json is not what ' + path.basename(opts.exporter) + ' writes: regenerate it with build.ps1, never by hand');
      else stats.regenerated = crypto.createHash('sha256').update(written).digest('hex');
    }
  }
  stats.rev = rev;
  return { problems, stats };
}

function runCatalogProof(root, rev, opts) {
  const r = catalogProof(root, rev, opts);
  for (const p of r.problems.slice(0, 40)) console.log('  DIFFERENT ' + p);
  if (r.problems.length > 40) console.log('  ... ' + (r.problems.length - 40) + ' more');
  const changed = Object.keys(r.stats.texts_of).length;
  console.log('catalogue proof against ' + rev + ': ' + r.stats.operations + ' operations and ' + r.stats.families + ' families in the same order, ' + r.stats.same + ' unchanged values and ' + r.stats.text + ' texts of schemas that changed, ' +
    changed + ' operations with a summary or description that changed; ' + r.problems.length + ' differences outside the text' + (r.stats.regenerated ? '; the exporter writes it byte for byte (sha256 ' + r.stats.regenerated.slice(0, 12) + '...)' : ''));
  return r.problems.length ? 1 : 0;
}

// ── --check ─────────────────────────────────────────────────────────────────
// How many paragraphs declare a limit, and how many of them have the keyword (counted over the catalogue the bridge serves)
function keywordCount(root) {
  const cat = parseJson(fs.readFileSync(path.join(root, CATALOG_JSON)));
  const texts = [];
  for (const o of cat.operations) texts.push(o.summary, o.details);
  const stat = { declarations: 0, keyword: 0, other: [] };
  for (const t of texts) for (const p of declarationsOf(t)) { stat.declarations++; if (p.startsWith(KEYWORD)) stat.keyword++; else stat.other.push(p.slice(0, 40)); }
  return stat;
}

const CEILING_FILE = 'Tests/data/refusal-keyword-ceiling.txt';
// writes the count of declarations that are not in the one form, where Tests/RefusalKeywordsContract.cs reads it
function runRatchet(root) {
  const k = keywordCount(root);
  const now = k.declarations - k.keyword;
  const file = path.join(root, CEILING_FILE);
  let was = null;
  try { was = parseInt(fs.readFileSync(file, 'utf8').trim(), 10); } catch (e) { /* new */ }
  if (was !== null && now > was) { console.log('ratchet: the catalogue has ' + now + ' declarations out of form and the file says ' + was + ': a ratchet only goes down'); return 1; }
  fs.writeFileSync(file, now + '\n');
  console.log('ratchet: ' + CEILING_FILE + ' is ' + now + (was === null ? ' (new)' : ' (was ' + was + ')'));
  return 0;
}

function runCheck(root, rev, opts) {
  useProfile();
  setRoot(root);
  let code = W5.runCheck(root, rev, { zero: opts.zero });
  const k = keywordCount(root);
  {
    let written = null;
    try { written = parseInt(fs.readFileSync(path.join(root, CEILING_FILE), 'utf8').trim(), 10); } catch (e) { /* none */ }
    const real = k.declarations - k.keyword;
    console.log('the ratchet of the refusal keyword (' + CEILING_FILE + '): ' + written + ', the catalogue has ' + real + ' out of form' + (written === real ? '' : ' -- NOT the same: --ratchet writes it'));
    if (written !== real) code = 1;
  }
  console.log('declarations of a limit in the catalogue served: ' + k.declarations + ', ' + k.keyword + ' with «' + KEYWORD.trim() + '», ' + (k.declarations - k.keyword) + ' still in another form' + (opts.zero && k.declarations - k.keyword ? ' (' + k.other.slice(0, 3).join(' | ') + ')' : ''));
  if (opts.zero && k.declarations !== k.keyword) code = 1;
  const rc = runCatalogProof(root, rev, { exporter: opts.exporter, examples: opts.examples });
  return code || rc;
}

function runSurvey(root) {
  useProfile();
  const model = W5.buildModel(root, null);
  const coll = W5.collectItems(model);
  const idx = indexOf(model);
  const perAtom = new Map();
  for (const it of coll.items.values()) { const a = atomOf(it, model); perAtom.set(a, (perAtom.get(a) || 0) + it.words); }
  console.log('items ' + coll.items.size + ', operations ' + idx.ops.length + ', families ' + new Set(idx.ops.map(o => o.family)).size);
  for (const [a, w] of [...perAtom].sort((x, y) => x[0].localeCompare(y[0]))) console.log('  ' + a.padEnd(60) + String(w).padStart(6) + ' words');
  return 0;
}

Object.assign(module.exports, { opIndex, atomOf, distribute, refusalProblems, lengthProblems, protocolProblems, operationValues, setRoot, runRatchet, declarationsOf, catalogProof, keywordCount, runExtract, runApply, runCheck, runCatalogProof, runSurvey, PROFILE, SCOPE, inScope, KEYWORD });

// ── --self-test ─────────────────────────────────────────────────────────────
// Fixtures need Italian text, and this file is judged by the language gate: the Italian of the fixtures is written in ROT13.
const IT = rot13;

function fixtureOperations() {
  const q = (s) => JSON.stringify(s);
  return [
    'using System.Collections.Generic;',
    'namespace Oce.Solid.Agent.Core.Catalog',
    '{',
    '    public static class Operations',
    '    {',
    '        public static readonly List<KeyValuePair<string, string>> Families =',
    '            new List<KeyValuePair<string, string>>',
    '            {',
    '                new KeyValuePair<string, string>(',
    '                    "alpha",',
    '                    ' + q(IT("pbfn fv snaab vafvrzr")) + '),',
    '                new KeyValuePair<string, string>(',
    '                    "beta",',
    '                    ' + q(IT("pbfn fv pbaftaan")) + '),',
    '            };',
    '        public static readonly List<OperationSpec> All =',
    '            new List<OperationSpec>',
    '            {',
    '                new OperationSpec',
    '                {',
    '                    Name = "one",',
    '                    Family = "alpha",',
    '                    Summary =',
    '                        ' + q(IT("Hana pbfn cvra.")) + ',',
    '                    Description =',
    '                        ' + q(IT("Pern vy fbyvqb: vy pvepuvb fhy cvnab, rfgehfb. ")),
    '                        + ' + q(IT("R' y'bcrenmvbar cre ceribarg 12 zz.")),
    '                        + ' + q(IT("\n\nZV EVSVHGB FR ya qvnzrgeb abe r' cbfvgvib.")),
    '                        + ' + q(IT("\n\nHfn pbfn hfn fr ihbv hn nygeb.")) + ',',
    '                },',
    '                new OperationSpec',
    '                {',
    '                    Name = "two",',
    '                    Family = "beta",',
    '                    Summary =',
    '                        ' + q(IT("Qhr pbfr.")) + ',',
    '                    Description =',
    '                        ' + q(IT("Snaab qhr pbfr, abe hana fbyn. ")),
    '                        + ' + q(IT("Ihbv hfner `jvqgu_zz` b `urvtug_zz`. Pba «zbqr».")) + ',',
    '                },',
    '            };',
    '    }',
    '}',
  ].join('\n') + '\n';
}

function fixtureFiles() {
  const lf = (a) => a.join('\n') + '\n';
  const files = {};
  files['Oce.Solid.Agent.csproj'] = lf(['<Project>', '  <ItemGroup>', '    <Compile Include="Bridge\\A.cs" />', '  </ItemGroup>', '</Project>']);
  files['Oce.Solid.Agent.Core/Oce.Solid.Agent.Core.csproj'] = lf(['<Project>', '  <ItemGroup>', '    <Compile Include="Catalog\\Operations.cs" />', '  </ItemGroup>', '</Project>']);
  files['Bridge/A.cs'] = lf(['namespace Demo', '{', '    class A', '    {', '        static string Failed(string m) { return m; }', '        string Go() { return Failed("done"); }', '    }', '}']);
  files[OPERATIONS] = fixtureOperations();
  files['Bench/case-keys.json'] = JSON.stringify({ format: 1, roots: { declared: 'case' }, contexts: { case: { read: ['name'], documented: {}, children: {}, frozen_free: {} } } }, null, 1) + '\n';
  files['Checks/language/consumed-strings.json'] = JSON.stringify({ about: 'fixture', entries: [] }, null, 2) + '\n';
  files[CATALOG_JSON] = JSON.stringify(fixtureCatalog(false), null, 2) + '\n';
  files['Tests/data/refusal-keyword-ceiling.txt'] = '0\n';
  return files;
}

// the catalogue the fixture's exporter would write; `english` gives the translated texts
function fixtureCatalog(english) {
  const T = (it, en) => (english ? en : IT(it));
  return {
    families: { alpha: T('pbfn fv snaab vafvrzr', 'what is done together'), beta: T('pbfn fv pbaftaan', 'what is delivered') },
    operations: [
      { name: 'one', family: 'alpha', summary: T('Hana pbfn cvra.', 'A solid thing.'),
        details: T("Pern vy fbyvqb: vy pvepuvb fhy cvnab, rfgehfb. R' y'bcrenmvbar cre ceribarg 12 zz.\n\nZV EVSVHGB FR ya qvnzrgeb abe r' cbfvgvib.\n\nHfn pbfn hfn fr ihbv hn nygeb.",
          "Makes the solid: the circle on the plane, extruded. It is the operation for pins, 12 mm.\n\nI REFUSE IF the diameter is not positive.\n\nUse the other one if you want another."),
        schema: { type: 'object', properties: { plane: { type: 'string', enum: ['front', 'top'], description: T('cvnab qv fgnepb', 'the plane of work') } }, required: ['plane'] },
        precondition: 'document_without_body', document: 'part', example: '{"plane":"front"}' },
      { name: 'two', family: 'beta', summary: T('Qhr pbfr.', 'Two things.'), details: T('Snaab qhr pbfr, abe hana fbyn. Ihbv hfner `jvqgu_zz` b `urvtug_zz`. Pba «zbqr».', 'Makes two things, not just one. You can use `width_mm` or `height_mm`. With «mode».'),
        schema: { type: 'object', properties: { width_mm: { type: 'number', description: T('yneturmmn', 'the width') } } }, precondition: 'any', document: 'part', example: '{"width_mm":4}' },
    ],
  };
}

function makeFixtureRepo() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'window6-selftest-'));
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
  const quiet = (fn) => { const log = console.log; console.log = () => {}; try { return fn(); } finally { console.log = log; } };
  const { dir: root, g } = makeFixtureRepo();
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'window6-chunks-'));
  try {
    // 1. the operations and the families
    const text = fs.readFileSync(path.join(root, OPERATIONS), 'utf8');
    const ops = opIndex(text);
    say(ops.length === 2 && ops[0].name === 'one' && ops[0].family === 'alpha' && ops[1].family === 'beta' && ops[0].end < ops[1].start, 'operations: two OperationSpec blocks found, each with its name and family, one after the other');
    // 2. the extraction cuts by family
    quiet(() => runExtract(root, tmp, {}));
    const plan = JSON.parse(fs.readFileSync(path.join(tmp, 'plan.json'), 'utf8'));
    const ids = plan.chunks.map(c => c.id);
    say(ids.length >= 1 && ids.every(i => i.startsWith('cat-')), 'extract: every chunk is named for its families (' + ids.join(', ') + ')');
    const chunkFiles = fs.readdirSync(tmp).filter(f => f.endsWith('.json') && f !== 'plan.json');
    const chunks = chunkFiles.map(f => JSON.parse(fs.readFileSync(path.join(tmp, f), 'utf8')));
    const items = [].concat(...chunks.map(c => c.items));
    const long = items.find(i => i.pieces === 4);
    say(!!long && typeof long.text === 'string' && long.text.includes('\n\n') && long.parts.join('') === long.text, 'extract: a description of four literals is offered whole, with its four pieces');
    say(items.filter(i => i.file === OPERATIONS).length === 6, 'extract: the units of the fixture catalogue are the six Italian ones (two family lines, two summaries, two descriptions) (' + items.filter(i => i.file === OPERATIONS).length + ')');
    // 3. the pieces: the cut
    {
      const parts = ['Makes a solid. ', 'It is for pins and discs.', '\n\nI refuse x.', '\n\nUse the other.'];
      const t = 'Makes a solid thing, in one go. It is for pins, discs and bushes.\n\nI REFUSE IF x is zero.\n\nUse the other one.';
      const d = distribute(parts, t);
      say(!!d && d.length === 4 && d.join('') === t && d[2].startsWith('\n\nI REFUSE IF') && d[3].startsWith('\n\nUse'), 'distribute: the paragraph breaks stay where the old pieces broke, and the pieces join into the text');
      say(distribute(['a ', 'b ', 'c ', 'd'], 'one two') === null, 'distribute: a text with too few places to cut is refused (null), not cut in the middle of a word');
      const e = distribute(['aaa bbb ', 'ccc ddd ', 'eee'], 'one two three four five six');
      say(!!e && e.length === 3 && e.every(p => p.length > 0 && !/^\s/.test(p) || p.startsWith('\n')) && e.join('') === 'one two three four five six', 'distribute: every piece is not empty, none starts with a space, they join back');
    }
    // 4. the refusal keyword
    say(refusalProblems(IT('ZV EVSVHGB FR ya qvnzrgeb abe r\' cbfvgvib.'), 'I REFUSE IF the diameter is not positive.').length === 0, 'keyword: the Italian keyword translated «I REFUSE IF» is accepted');
    say(refusalProblems(IT('ZV EVSVHGB FR ya qvnzrgeb abe r\' cbfvgvib.'), 'I refuse when the diameter is not positive.').length === 2, 'keyword: «I refuse when» is refused twice (not the keyword, and the keyword is not there)');
    say(refusalProblems(IT('ABG'), 'I REFUSE IF x.').length === 1, 'keyword: a declaration made up is refused');
    say(refusalProblems(IT('\n\nZV EVSVHGB FR n.\n\nEVSVHGN fr o.'), 'I REFUSE IF a.').length === 1, 'keyword: two declarations that become one are refused');
    say(refusalProblems('A text with «' + IT('ZV EVSVHGB FR') + '» in the middle', 'A text with I REFUSE IF in the middle').length === 0 && refusalProblems('A text with ' + IT('ZV EVSVHGB FR') + ' in the middle', 'A text that says nothing').length === 1, 'keyword: the keyword in the middle of a paragraph is translated too');
    // 4b. the protocol words and the length
    {
      const coincident = IT('pbvapvqragr');
      const values = new Map([['mate', new Set([coincident, 'front'])]]);
      const plane = IT('vy cvnab ') + coincident + IT(' pby onfr');
      say(protocolProblems(plane, 'a plane that coincides', 'mate', {}, values).length === 1, 'protocol: a value the bench passes to the operation, named in the original and gone from the translation, is refused');
      say(protocolProblems(plane, 'a plane that coincides', 'mate', { protocol_ok: [coincident] }, values).length === 0, 'protocol: "protocol_ok" lets a value that was prose go');
      say(protocolProblems(plane, 'a plane that coincides', 'other', {}, values).length === 0, 'protocol: the values of another operation belong to that operation only');
      say(protocolProblems('the «grounded» and «C1»', 'the «grounded» and «C1»', null, {}, values).length === 0 && protocolProblems('the «grounded» and «C1»', 'the grounded and C1', null, {}, values).length === 1, 'protocol: a word between guillemets stays between them, as many times');
      say(protocolProblems('the «' + IT('snppvn va onffb') + '»', 'the «lower face»', null, {}, values).length === 0, 'protocol: a phrase between guillemets (with spaces) is prose and is translated');
      say(lengthProblems('x'.repeat(80), 'short').length === 1 && lengthProblems('x'.repeat(80), 'x'.repeat(200)).length === 1 && lengthProblems('x'.repeat(80), 'x'.repeat(70)).length === 0 && lengthProblems('word', '').length === 1, 'length: a translation a fraction of the original, or much longer, or empty, is refused');
      // a family over PART_WORDS is cut by operation, in source order
      const op = (n, fam) => ['                new OperationSpec', '                {', '                    Name = "' + n + '",', '                    Family = "' + fam + '",', '                    Summary = "x",', '                },'];
      const bigText = [].concat(op('a', 'big'), op('b', 'big'), op('c', 'big'), op('d', 'small')).join('\n') + '\n';
      const ops2 = opIndex(bigText);
      const unitAt = (o) => ({ italian: true, line: o.start + 4, words: 2000 });
      const fake = { files: new Map([[OPERATIONS, { text: bigText, units: ops2.map(unitAt) }]]) };
      const atoms = ops2.map(o => atomOf({ file: OPERATIONS, line: o.start + 4 }, fake));
      say(atoms.join(' ') === 'ops/big.1 ops/big.2 ops/big.3 ops/small', 'atoms: a family over ' + PART_WORDS + ' words is cut by operation (' + atoms.join(' ') + '), a small one stays whole');
    }
    // 5. the apply: the good translation, and the refusals
    // the English of every Italian text of the fixture, by the Italian text: from the two catalogues, not typed again
    const table = {};
    {
      const itc = fixtureCatalog(false), enc = fixtureCatalog(true);
      for (const k of Object.keys(itc.families)) table[itc.families[k]] = enc.families[k];
      itc.operations.forEach((o, i) => { table[o.summary] = enc.operations[i].summary; table[o.details] = enc.operations[i].details; });
    }
    const goodText = (it) => table[it.text !== undefined ? it.text : it.parts.join('')];
    const writeTranslations = (mutate) => {
      for (const c of chunks) {
        const t = { id: c.id, items: c.items.map(it => { const x = { id: it.id, text: goodText(it) }; if (mutate) mutate(it, x); return x; }) };
        fs.writeFileSync(path.join(tmp, c.id + '.en.json'), JSON.stringify(t, null, 1));
      }
    };
    const run = (dry) => { let code; const lines = []; const log = console.log; console.log = (s) => lines.push(String(s)); try { code = runApply(root, tmp, { dry: !!dry }); } finally { console.log = log; } return { code, lines }; };
    // the schema descriptions of the fixture are not in Operations.cs (the fixture has none there): the table above holds what is
    writeTranslations();
    const missing = chunks.flatMap(c => c.items).filter(it => goodText(it) === undefined);
    say(missing.length === 0, 'apply: the fixture translation covers every item (' + missing.map(i => i.unit).join(', ') + ')');
    let r = run(true);
    say(r.code === 0, 'apply (dry): the good translation is accepted' + (r.code ? ' -- ' + r.lines.filter(l => /REFUSED/.test(l)).join(' | ') : ''));
    writeTranslations((it, x) => { if (it.text && it.text.includes(IT('ZV EVSVHGB'))) x.text = x.text.replace('I REFUSE IF', 'I refuse when'); });
    r = run(true);
    say(r.code === 1 && r.lines.some(l => /begins/.test(l) && /REFUSE IF/.test(l)), 'apply: a translation that breaks the keyword is refused, and nothing is written');
    writeTranslations((it, x) => { if (x.text && x.text.includes('12 mm')) x.text = x.text.replace('12 mm', '14 mm'); });
    r = run(true);
    say(r.code === 1 && r.lines.some(l => /numbers changed/.test(l)), 'apply: a number that changed is refused (the invariants of window 5 apply)');
    writeTranslations((it, x) => { if (x.text && x.text.includes('`width_mm`')) x.text = x.text.replace('`width_mm`', '`width`'); });
    r = run(true);
    say(r.code === 1 && r.lines.some(l => /backticks changed/.test(l)), 'apply: a name between backticks that changed is refused');
    writeTranslations((it, x) => { if (x.text && x.text.includes('With «mode»')) x.text = x.text.replace('«mode»', '«' + IT('zbqb') + '»'); });
    r = run(true);
    say(r.code === 1 && r.lines.some(l => /names between guillemets changed/.test(l)), 'apply: a single word between guillemets that changed (a name of the protocol) is refused');
    writeTranslations((it, x) => { if (x.text && x.text.includes('With «mode»')) { x.text = x.text.replace('«mode»', '«' + IT('zbqb') + '»'); x.quoted_ok = true; } });
    r = run(true);
    say(r.code === 0, 'apply: the same change declared with "quoted_ok" is accepted' + (r.code ? ' -- ' + r.lines.filter(l => /REFUSED/.test(l)).join(' | ') : ''));
    writeTranslations((it, x) => { if (x.text && x.text.includes('Makes the solid') ) x.text = 'Makes a thing, in a short way, ok yes. 12 mm.\n\nI REFUSE IF d.\n\nUse it.'; });
    r = run(true);
    say(r.code === 1 && r.lines.some(l => /of the length of the original/.test(l)), 'apply: a description that comes out a fraction of its length (a sentence dropped) is refused' + (!(r.code === 1 && r.lines.some(l => /of the length of the original/.test(l))) ? ' -- ' + r.lines.join(' / ').slice(0, 600) : ''));
    writeTranslations((it, x) => { if (x.text && x.text.includes('Makes the solid')) { x.text = 'Makes a thing, in a short way, ok yes. 12 mm.\n\nI REFUSE IF d.\n\nUse it.'; x.length_ok = true; } });
    r = run(true);
    say(r.code === 0, 'apply: the same, declared with "length_ok" and "keywords_ok", is accepted');
    writeTranslations((it, x) => { if (x.text && x.text.includes('Two things')) x.text = ''; });
    r = run(true);
    say(r.code === 1, 'apply: an empty translation is refused');
    writeTranslations();
    r = run(false);
    say(r.code === 0 && r.lines.some(l => /applied/.test(l)), 'apply: the good translation is written' + (r.code ? ' -- ' + r.lines.filter(l => /REFUSED/.test(l)).join(' | ') : ''));
    const after = fs.readFileSync(path.join(root, OPERATIONS), 'utf8');
    say(after.includes('I REFUSE IF the diameter is not positive.') && after.includes('Name = "one"') && !new RegExp(IT('EVSVHGB'), 'i').test(after), 'apply: the file holds the English text and the code around it is the same');
    const stream = (s) => s.replace(/"(?:[^"\\]|\\.)*"/g, '""').replace(/\s+/g, ' ');
    say(stream(after) === stream(text), 'apply: outside the string literals the file is the same');
    // 6. the check against the tree before the window
    g('add', '-A');
    g('commit', '-q', '-m', 'window');
    // the catalogue regenerated
    fs.writeFileSync(path.join(root, CATALOG_JSON), JSON.stringify(fixtureCatalog(true), null, 2) + '\n');
    g('add', '-A');
    g('commit', '-q', '-m', 'catalogue');
    const chk = () => { const log = console.log; const lines = []; console.log = (s) => lines.push(String(s)); let code; try { code = runCheck(root, 'HEAD~2', { zero: true }); } finally { console.log = log; } return { code, lines }; };
    const c1 = chk();
    say(c1.code === 0, 'check: the translated fixture passes against the tree before it' + (c1.code ? ' -- ' + c1.lines.filter(l => /DIFFERENT|problems|still in another|difference/.test(l)).join(' | ') : ''));
    // the catalogue proof, red on each kind of change
    const mut = (fn) => { const c = fixtureCatalog(true); fn(c); fs.writeFileSync(path.join(root, CATALOG_JSON), JSON.stringify(c, null, 2) + '\n'); const r2 = catalogProof(root, 'HEAD~2'); return r2; };
    say(mut(c => { c.operations[0].schema.properties.plane.enum = ['front', 'top', 'right']; }).problems.some(p => /enum/.test(p)), 'catalogue proof: a value added to an enum is a difference');
    say(mut(c => { delete c.operations[1].schema.properties.width_mm.description; }).problems.some(p => /keys/.test(p)), 'catalogue proof: a description that disappears (a key) is a difference');
    say(mut(c => { c.operations[0].schema.required = ['other']; }).problems.some(p => /required/.test(p)), 'catalogue proof: a changed required field is a difference');
    say(mut(c => { c.operations.reverse(); }).problems.some(p => /operations changed/.test(p)), 'catalogue proof: the operations in another order are a difference');
    say(mut(c => { c.operations[0].family = 'beta'; }).problems.some(p => /family changed/.test(p)), 'catalogue proof: a changed family is a difference');
    say(mut(c => { c.operations[0].example = '{"plane":"top"}'; }).problems.some(p => /example changed/.test(p)), 'catalogue proof: a changed example is a difference unless declared');
    say(mut(c => { c.operations[1].precondition = 'other'; }).problems.some(p => /precondition changed/.test(p)), 'catalogue proof: a changed precondition is a difference');
    say(mut(c => { c.operations.pop(); }).problems.some(p => /gone: two/.test(p)), 'catalogue proof: an operation that disappears is a difference');
    say(mut(c => { delete c.families.beta; }).problems.some(p => /families changed/.test(p)), 'catalogue proof: a family that disappears is a difference');
    say(mut(() => {}).problems.length === 0, 'catalogue proof: the translated catalogue and nothing else is green');
    // the check red on a text that was not Italian
    fs.writeFileSync(path.join(root, CATALOG_JSON), JSON.stringify(fixtureCatalog(true), null, 2) + '\n');
    const ops2 = fs.readFileSync(path.join(root, OPERATIONS), 'utf8');
    fs.writeFileSync(path.join(root, OPERATIONS), ops2.replace('Name = "two"', 'Name = "deux"'));
    const c2 = chk();
    say(c2.code === 1, 'check: a name of an operation that changed in the code is red (the token stream or the units differ)');
    fs.writeFileSync(path.join(root, OPERATIONS), ops2);
    fs.writeFileSync(path.join(root, OPERATIONS), ops2.replace('I REFUSE IF the diameter', 'I refuse when the diameter'));
    fs.writeFileSync(path.join(root, CATALOG_JSON), JSON.stringify(fixtureCatalog(true), null, 2).replace('I REFUSE IF the diameter', 'I refuse when the diameter') + '\n');
    const c3 = chk();
    say(c3.code === 1 && c3.lines.some(l => /another form/.test(l)), 'check: with --zero a declaration of a limit that is not in the one form is red');
    say(c3.lines.some(l => /ratchet of the refusal keyword.*NOT the same/.test(l)), 'check: the number of the ratchet that is not the real count is red');
    // the ratchet only goes down
    const ratchet = () => { const log = console.log; console.log = () => {}; try { return runRatchet(root); } finally { console.log = log; } };
    const ceilingFile = path.join(root, 'Tests', 'data', 'refusal-keyword-ceiling.txt');
    say(ratchet() === 1 && fs.readFileSync(ceilingFile, 'utf8').trim() === '0', 'ratchet: a count above the number written is refused, and the file stays');
    fs.writeFileSync(ceilingFile, '5\n');
    say(ratchet() === 0 && fs.readFileSync(ceilingFile, 'utf8').trim() === '1', 'ratchet: a count below the number written lowers it to the real count');
  } finally {
    try { fs.rmSync(root, { recursive: true, force: true }); fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) { /* the temp folder stays */ }
  }
  console.log('');
  console.log('self-test: ' + ok + ' of ' + total + ' as expected' + (bad.length ? ', ' + bad.length + ' NOT: ' + bad.join(' | ') : ''));
  return bad.length === 0 ? 0 : 1;
}

Object.assign(module.exports, { selfTest });

// ── main ────────────────────────────────────────────────────────────────────
function parseArgs(argv) {
  const a = { root: ROOT_DEFAULT, extract: null, apply: null, check: false, proof: false, ratchet: false, survey: false, selfTest: false, dry: false, rev: null, only: null, zero: false, exporter: null, max: null };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    const next = () => { if (i + 1 >= argv.length) cannot(x + ' needs a value'); return argv[++i]; };
    if (x === '--extract') a.extract = path.resolve(next());
    else if (x === '--apply') a.apply = path.resolve(next());
    else if (x === '--check') a.check = true;
    else if (x === '--catalog-proof') a.proof = true;
    else if (x === '--ratchet') a.ratchet = true;
    else if (x === '--survey') a.survey = true;
    else if (x === '--self-test') a.selfTest = true;
    else if (x === '--dry') a.dry = true;
    else if (x === '--zero') a.zero = true;
    else if (x === '--rev') a.rev = next();
    else if (x === '--root') a.root = path.resolve(next());
    else if (x === '--chunk') a.only = next().split(',');
    else if (x === '--exporter') a.exporter = path.resolve(next());
    else if (x === '--max') a.max = parseInt(next(), 10);
    else cannot('unknown argument ' + x);
  }
  if (![a.extract, a.apply, a.check, a.proof, a.ratchet, a.survey, a.selfTest].some(Boolean)) cannot('one of --extract <dir>, --apply <dir>, --check, --catalog-proof, --ratchet, --survey, --self-test');
  return a;
}

function integrationExamples(root) {
  try { return (JSON.parse(fs.readFileSync(path.join(root, 'Checks', 'language', PROFILE.integration), 'utf8').replace(/^﻿/, '')).examples) || []; } catch (e) { return []; }
}

function main() {
  const a = parseArgs(process.argv.slice(2));
  if (a.selfTest) return selfTest();
  if (a.extract) return runExtract(a.root, a.extract, { max: a.max });
  if (a.apply) return runApply(a.root, a.apply, { dry: a.dry, only: a.only });
  if (a.survey) return runSurvey(a.root);
  if (a.ratchet) return runRatchet(a.root);
  const rev = a.rev || 'HEAD';
  if (a.proof) return runCatalogProof(a.root, rev, { exporter: a.exporter, examples: integrationExamples(a.root) });
  return runCheck(a.root, rev, { zero: a.zero, exporter: a.exporter, examples: integrationExamples(a.root) });
}

if (require.main === module) {
  try {
    process.exitCode = main();
  } catch (e) {
    if (e instanceof Stop || e instanceof W5.Stop) { console.log('window-6-catalogue: ' + e.message); process.exitCode = 2; }
    else { console.log('window-6-catalogue: ' + (e && e.stack ? e.stack : e)); process.exitCode = 2; }
  }
}
