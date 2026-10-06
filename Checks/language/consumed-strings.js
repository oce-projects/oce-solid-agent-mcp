#!/usr/bin/env node
// =============================================================================
//  consumed-strings.js - text that one program looks for in another's output
//  (entry W3.1-bis.2, item 6)
// =============================================================================
//
//  WHY IT EXISTS
//
//  Some programs of this repository find out what another one did by reading
//  its output: the installer decides that the bridge started when the
//  bridge's own sentence shows up in what it printed; `Bench/bench.ps1`
//  builds the bench report out of the lines `Oce.Solid.Agent.Bench` writes; the
//  release script reads the summary line of the contract suite. The two
//  sides are compiled separately and nothing ties them together: translate
//  the producer's sentence and the consumer keeps looking for the old one,
//  finds nothing, and in most places says nothing either. Neither the bench,
//  nor the IL comparison, nor the contracts saw it.
//
//  So the pairs are written down in `consumed-strings.json`, and this script
//  checks, for each of them, that the text the consumer looks for is still
//  produced. Producer and consumer are translated in the same commit.
//
//  WHY THE INVENTORY HOLDS NO TEXT
//
//  The strings are Italian today, and a new file of the repository may not
//  carry Italian (the language gate). So an entry points at its consumer by
//  file and by the SHA-1 of the literal (first 12 hex digits): the literal is
//  read back from the consumer at check time, never copied. When the consumer
//  text changes - because it was translated - the hash no longer matches and
//  the entry goes red until it is bound again (--rebind), which is the moment
//  somebody has to look at the producer too. `--find` prints the texts.
//
//  WHAT "PRODUCED" MEANS
//
//  The consumed literal becomes one or more VARIANTS (a regular expression
//  with alternatives gives one variant per alternative) and each variant a
//  list of RUNS, the fixed text between the special characters. Every run of
//  every variant must be inside a string literal of one of the producer files
//  - a literal, not a comment: a sentence that survives only in a comment is
//  not printed by anyone. The holes of interpolated strings and PowerShell
//  variables split a literal, so a run never spans a value computed at run
//  time. Case follows the consumer: `-match` and `-like` ignore it, `.Contains`
//  does not, `IgnoreCase` nearby switches it off.
//
//  USAGE
//    node Checks/language/consumed-strings.js              check the inventory (default)
//    node Checks/language/consumed-strings.js --self-test  the check, on fake files: every fake must end as expected
//    node Checks/language/consumed-strings.js --find       candidates: consumer literals produced by another program
//    node Checks/language/consumed-strings.js --find --all ... also the ones already in the inventory
//    node Checks/language/consumed-strings.js --rebind <id> <line>   bind an entry to the consumer literal on that line
//    node Checks/language/consumed-strings.js --root <dir>   run on another checkout
//
//  EXIT CODES
//    0  every entry is produced (check), every fake ended as expected (self-test)
//    1  an entry is broken, or a fake did not end as expected
//    2  the script cannot run (no inventory, unreadable file, bad entry)
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const crypto = require('crypto');

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const INVENTORY = 'Checks/language/consumed-strings.json';
// a hole: the place of a value computed at run time inside a literal
const HOLE = '\u0000';
const MIN_RUN = 3;

// -----------------------------------------------------------------------------
//  Lexers with offsets: string literals (decoded) and comment spans
// -----------------------------------------------------------------------------

function langOf(file) {
  const ext = path.extname(file).toLowerCase();
  if (ext === '.cs') return 'cs';
  if (ext === '.js' || ext === '.mjs' || ext === '.cjs') return 'js';
  if (ext === '.ps1' || ext === '.psm1') return 'ps';
  return null;
}

function lineOf(text, offset) {
  let n = 1;
  for (let i = 0; i < offset && i < text.length; i++) if (text.charCodeAt(i) === 10) n++;
  return n;
}

// C#: // and /* */ comments, char literals, "..." @"..." $"..." $@"..." @$"...".
function lexCs(text, base, out) {
  const n = text.length;
  let i = 0;
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === '/' && d === '/') {
      let j = text.indexOf('\n', i); if (j < 0) j = n;
      out.comments.push([base + i, base + j]); i = j; continue;
    }
    if (c === '/' && d === '*') {
      let j = text.indexOf('*/', i + 2); j = j < 0 ? n : j + 2;
      out.comments.push([base + i, base + j]); i = j; continue;
    }
    if (c === "'") {
      let j = i + 1;
      while (j < n && text[j] !== "'" && text[j] !== '\n') { if (text[j] === '\\') j++; j++; }
      i = j + 1; continue;
    }
    if ((c === '@' || c === '$') && (d === '"' || ((d === '@' || d === '$') && text[i + 2] === '"'))) {
      const verbatim = c === '@' || d === '@';
      const interp = c === '$' || d === '$';
      const q = d === '"' ? i + 1 : i + 2;
      const s = csString(text, q + 1, verbatim, interp);
      out.strings.push({ start: base + i, end: base + s.end + 1, value: s.value, regex: false });
      for (const [a, b] of s.holes) lexCs(text.slice(a, b), base + a, out);
      i = s.end + 1; continue;
    }
    if (c === '"') {
      const s = csString(text, i + 1, false, false);
      out.strings.push({ start: base + i, end: base + s.end + 1, value: s.value, regex: false });
      i = s.end + 1; continue;
    }
    if (/[A-Za-z0-9_]/.test(c)) { // a whole word: an identifier never opens a literal
      while (i < n && /[A-Za-z0-9_]/.test(text[i])) i++;
      continue;
    }
    i++;
  }
  return out;
}

const CS_ESCAPES = { n: '\n', r: '\r', t: '\t', '0': '\0', a: '\x07', b: '\b', f: '\f', v: '\v', '\\': '\\', '"': '"', "'": "'" };

function csString(text, i, verbatim, interp) {
  let value = '';
  const holes = [];
  const n = text.length;
  for (let j = i; j < n; j++) {
    const c = text[j];
    if (interp && c === '{') {
      if (text[j + 1] === '{') { value += '{'; j++; continue; }
      let depth = 1, k = j + 1;
      for (; k < n && depth > 0; k++) {
        const e = text[k];
        if (e === '{') depth++;
        else if (e === '}') { depth--; if (depth === 0) break; }
        else if (e === '"') { k = csString(text, k + 1, false, false).end; }
        else if ((e === '@' || e === '$') && text[k + 1] === '"') { k = csString(text, k + 2, e === '@', e === '$').end; }
      }
      holes.push([j + 1, k]);
      value += HOLE; j = k; continue;
    }
    if (interp && c === '}' && text[j + 1] === '}') { value += '}'; j++; continue; }
    if (verbatim) {
      if (c === '"') { if (text[j + 1] === '"') { value += '"'; j++; continue; } return { end: j, value, holes }; }
      value += c; continue;
    }
    if (c === '\\') {
      const e = text[j + 1];
      if (e === 'u' || e === 'x') {
        const m = /^[0-9A-Fa-f]{1,4}/.exec(text.slice(j + 2, j + 6));
        if (m) { value += String.fromCharCode(parseInt(m[0], 16)); j += 1 + m[0].length; continue; }
      }
      value += e in CS_ESCAPES ? CS_ESCAPES[e] : e; j++; continue;
    }
    if (c === '"' || c === '\n') return { end: j, value, holes };
    value += c;
  }
  return { end: n, value, holes };
}

// JavaScript: // and /* */ comments, '...' "..." `...${}...`, /regex/flags.
function lexJs(text, base, out) {
  const n = text.length;
  let i = 0, last = '';
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === '/' && d === '/') {
      let j = text.indexOf('\n', i); if (j < 0) j = n;
      out.comments.push([base + i, base + j]); i = j; continue;
    }
    if (c === '/' && d === '*') {
      let j = text.indexOf('*/', i + 2); j = j < 0 ? n : j + 2;
      out.comments.push([base + i, base + j]); i = j; continue;
    }
    if (c === '"' || c === "'") {
      const s = jsString(text, i + 1, c);
      out.strings.push({ start: base + i, end: base + s.end + 1, value: s.value, regex: false });
      i = s.end + 1; last = '"'; continue;
    }
    if (c === '`') {
      const s = jsTemplate(text, i + 1);
      out.strings.push({ start: base + i, end: base + s.end + 1, value: s.value, regex: false });
      for (const [a, b] of s.holes) lexJs(text.slice(a, b), base + a, out);
      i = s.end + 1; last = '`'; continue;
    }
    if (c === '/' && regexAllowed(last)) {
      const end = regexEnd(text, i + 1);
      if (end > 0) {
        let k = end + 1; while (k < n && /[a-z]/.test(text[k])) k++;
        out.strings.push({ start: base + i, end: base + k, value: text.slice(i + 1, end), regex: true,
          flags: text.slice(end + 1, k) });
        i = k; last = '/'; continue;
      }
    }
    if (/[A-Za-z_$]/.test(c)) {
      let j = i; while (j < n && /[A-Za-z0-9_$]/.test(text[j])) j++;
      last = text.slice(i, j); i = j; continue;
    }
    if (!/\s/.test(c)) last = c;
    i++;
  }
  return out;
}

function regexAllowed(prev) {
  if (prev === '') return true;
  if (/^[(,=:[!&|?{};+\-*%<>~^]$/.test(prev)) return true;
  return /^(return|typeof|case|do|else|in|of|new|delete|void|throw)$/.test(prev);
}

function regexEnd(text, i) {
  let inClass = false;
  for (let j = i; j < text.length; j++) {
    const c = text[j];
    if (c === '\n') return -1;
    if (c === '\\') { j++; continue; }
    if (c === '[') inClass = true;
    else if (c === ']') inClass = false;
    else if (c === '/' && !inClass) return j;
  }
  return -1;
}

const JS_ESCAPES = { n: '\n', r: '\r', t: '\t', '0': '\0', b: '\b', f: '\f', v: '\v' };

function jsString(text, i, q) {
  let value = '';
  for (let j = i; j < text.length; j++) {
    const c = text[j];
    if (c === '\\') {
      const e = text[j + 1];
      if (e === 'u') {
        const m = /^\{([0-9A-Fa-f]+)\}|^([0-9A-Fa-f]{4})/.exec(text.slice(j + 2, j + 10));
        if (m) { value += String.fromCodePoint(parseInt(m[1] || m[2], 16)); j += 1 + m[0].length; continue; }
      }
      if (e === 'x') {
        const m = /^[0-9A-Fa-f]{2}/.exec(text.slice(j + 2, j + 4));
        if (m) { value += String.fromCharCode(parseInt(m[0], 16)); j += 3; continue; }
      }
      value += e in JS_ESCAPES ? JS_ESCAPES[e] : e; j++; continue;
    }
    if (c === q || c === '\n') return { end: j, value };
    value += c;
  }
  return { end: text.length, value };
}

function jsTemplate(text, i) {
  let value = '';
  const holes = [];
  for (let j = i; j < text.length; j++) {
    const c = text[j];
    if (c === '\\') { const e = text[j + 1]; value += e in JS_ESCAPES ? JS_ESCAPES[e] : e; j++; continue; }
    if (c === '`') return { end: j, value, holes };
    if (c === '$' && text[j + 1] === '{') {
      let depth = 1, k = j + 2;
      for (; k < text.length && depth > 0; k++) {
        const e = text[k];
        if (e === '{') depth++;
        else if (e === '}') { depth--; if (depth === 0) break; }
        else if (e === '`') k = jsTemplate(text, k + 1).end;
        else if (e === '"' || e === "'") k = jsString(text, k + 1, e).end;
      }
      holes.push([j + 2, k]);
      value += HOLE; j = k; continue;
    }
    value += c;
  }
  return { end: text.length, value, holes };
}

// PowerShell: # and <# #> comments, '...', "..." with `escapes, $var and $(...)
// holes, and the here-strings @"..."@ and @'...'@.
function lexPs(text, base, out) {
  const n = text.length;
  let i = 0;
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === '<' && d === '#') {
      let j = text.indexOf('#>', i + 2); j = j < 0 ? n : j + 2;
      out.comments.push([base + i, base + j]); i = j; continue;
    }
    if (c === '#' && (i === 0 || /[\s;({|]/.test(text[i - 1]))) {
      let j = text.indexOf('\n', i); if (j < 0) j = n;
      out.comments.push([base + i, base + j]); i = j; continue;
    }
    if (c === '@' && (d === '"' || d === "'") && /^[ \t]*\r?\n/.test(text.slice(i + 2, i + 40))) {
      const open = text.indexOf('\n', i) + 1;
      const close = text.indexOf('\n' + d + '@', open - 1);
      const stop = close < 0 ? n : close;
      const raw = text.slice(open, stop).replace(/\r$/, '');
      const value = d === '"' ? psExpand(raw, open, base, out) : raw;
      out.strings.push({ start: base + i, end: base + (close < 0 ? n : close + 3), value, regex: false });
      i = close < 0 ? n : close + 3; continue;
    }
    if (c === "'") {
      let j = i + 1, value = '';
      for (; j < n; j++) {
        if (text[j] === "'") { if (text[j + 1] === "'") { value += "'"; j++; continue; } break; }
        value += text[j];
      }
      out.strings.push({ start: base + i, end: base + j + 1, value, regex: false });
      i = j + 1; continue;
    }
    if (c === '"') {
      const s = psDouble(text, i + 1, base, out);
      out.strings.push({ start: base + i, end: base + s.end + 1, value: s.value, regex: false });
      i = s.end + 1; continue;
    }
    if (/[A-Za-z0-9_$-]/.test(c)) { // a whole word or a variable: never opens a literal
      while (i < n && /[A-Za-z0-9_$:-]/.test(text[i])) i++;
      continue;
    }
    i++;
  }
  return out;
}

const PS_ESCAPES = { n: '\n', r: '\r', t: '\t', '0': '\0', a: '\x07', b: '\b', f: '\f', v: '\v', e: '\x1b' };

// The body of a double-quoted string: returns the index of the closing quote.
function psDouble(text, i, base, out) {
  let value = '';
  const n = text.length;
  for (let j = i; j < n; j++) {
    const c = text[j];
    if (c === '`') { const e = text[j + 1]; value += e in PS_ESCAPES ? PS_ESCAPES[e] : e; j++; continue; }
    if (c === '"') { if (text[j + 1] === '"') { value += '"'; j++; continue; } return { end: j, value }; }
    if (c === '$') {
      const h = psHole(text, j, base, out);
      if (h > j) { value += HOLE; j = h - 1; continue; }
    }
    value += c;
  }
  return { end: n, value };
}

// A here-string body: only the holes are special.
function psExpand(raw, offset, base, out) {
  let value = '';
  for (let j = 0; j < raw.length; j++) {
    if (raw[j] === '`') { const e = raw[j + 1]; value += e in PS_ESCAPES ? PS_ESCAPES[e] : e; j++; continue; }
    if (raw[j] === '$') {
      const h = psHole(raw, j, base + offset, out);
      if (h > j) { value += HOLE; j = h - 1; continue; }
    }
    value += raw[j];
  }
  return value;
}

// $name, ${name}, $(...) and $name.Member: returns the offset after the hole,
// or the offset itself when the dollar is a plain character. The code inside
// $(...) is lexed again: strings in there are strings.
function psHole(text, j, base, out) {
  const d = text[j + 1];
  if (d === '(') {
    let depth = 1, k = j + 2;
    for (; k < text.length && depth > 0; k++) {
      const e = text[k];
      if (e === '(') depth++;
      else if (e === ')') { depth--; if (depth === 0) break; }
      else if (e === '"') k = psDouble(text, k + 1, base, { strings: [], comments: [] }).end;
      else if (e === "'") { k++; while (k < text.length && text[k] !== "'") k++; }
    }
    lexPs(text.slice(j + 2, k), base + j + 2, out);
    return k + 1;
  }
  if (d === '{') { const k = text.indexOf('}', j); return k < 0 ? j : k + 1; }
  const m = /^[A-Za-z_?][A-Za-z0-9_:]*/.exec(text.slice(j + 1, j + 80));
  return m ? j + 1 + m[0].length : j;
}

function lexFile(text, lang) {
  const out = { strings: [], comments: [] };
  if (lang === 'cs') lexCs(text, 0, out);
  else if (lang === 'js') lexJs(text, 0, out);
  else if (lang === 'ps') lexPs(text, 0, out);
  out.strings.sort((a, b) => a.start - b.start);
  return out;
}

// -----------------------------------------------------------------------------
//  Consumer sites: a literal that is the text or the pattern of a search
// -----------------------------------------------------------------------------

// Each rule looks at the code right before the literal. `kind` is literal,
// regex or like; `nocase` says whether the search ignores case by default.
const SITE_RULES = {
  cs: [
    { re: /\.(?:IndexOf|LastIndexOf|Contains|StartsWith|EndsWith)\s*\(\s*$/, kind: 'literal', nocase: false },
    { re: /\bRegex\.(?:IsMatch|Match|Matches|Replace|Split)\s*\([^;]*,\s*$/, kind: 'regex', nocase: false },
    { re: /\bnew\s+Regex\s*\(\s*$/, kind: 'regex', nocase: false },
  ],
  js: [
    { re: /\.(?:includes|indexOf|lastIndexOf|startsWith|endsWith)\s*\(\s*$/, kind: 'literal', nocase: false },
    { re: /\bnew\s+RegExp\s*\(\s*$/, kind: 'regex', nocase: false },
  ],
  ps: [
    { re: /-c(?:not)?(?:match|replace)\s*$/i, kind: 'regex', nocase: false },
    { re: /-[i]?(?:not)?(?:match|replace)\s*$/i, kind: 'regex', nocase: true },
    { re: /-c(?:not)?like\s*$/i, kind: 'like', nocase: false },
    { re: /-[i]?(?:not)?like\s*$/i, kind: 'like', nocase: true },
    { re: /Select-String\b[^|;\n]*-SimpleMatch[^|;\n]*-Pattern\s+$/i, kind: 'literal', nocase: true },
    { re: /Select-String\s+(?:-Pattern\s+)?$/i, kind: 'regex', nocase: true },
    { re: /\[regex\]::(?:Match|IsMatch|Matches|Replace|Split)\s*\([^;\n]*,\s*$/i, kind: 'regex', nocase: false },
    { re: /\.(?:IndexOf|LastIndexOf|Contains|StartsWith|EndsWith)\s*\(\s*$/, kind: 'literal', nocase: false },
  ],
};

function inside(spans, offset) {
  for (const [a, b] of spans) if (offset >= a && offset < b) return true;
  return false;
}

// `via`, when given, is a helper of the consumer that does the search with
// the literal it receives (bench.ps1 has `Take "<regex>"`): its literal
// arguments are sites too, of the kind and case the entry declares.
function sitesOf(text, lang, via) {
  const lexed = lexFile(text, lang);
  const sites = [];
  const rules = (SITE_RULES[lang] || []).slice();
  if (via && via.name) {
    rules.unshift({ re: new RegExp('\\b' + via.name.replace(/[^A-Za-z0-9_]/g, '') + '\\s*\\(?\\s*$'),
      kind: via.kind || 'regex', nocase: !!via.nocase });
  }
  for (const s of lexed.strings) {
    if (inside(lexed.comments, s.start)) continue;
    let kind = null, nocase = false;
    if (s.regex) { kind = 'regex'; nocase = /i/.test(s.flags || ''); }
    else {
      const before = text.slice(Math.max(0, s.start - 240), s.start);
      for (const r of rules) if (r.re.test(before)) { kind = r.kind; nocase = r.nocase; break; }
      if (!kind) continue;
      if (lang !== 'ps') {
        const after = text.slice(s.end, s.end + 240).split(';')[0];
        if (/IgnoreCase/.test(after)) nocase = true;
      }
    }
    sites.push({ kind, nocase, value: s.value, line: lineOf(text, s.start), start: s.start });
  }
  return { sites, lexed };
}

function viaOf(consumer) {
  return consumer && consumer.via ? { name: consumer.via, kind: consumer.kind, nocase: consumer.nocase } : null;
}

function hashOf(value) {
  return crypto.createHash('sha1').update(value, 'utf8').digest('hex').slice(0, 12);
}

// -----------------------------------------------------------------------------
//  Variants and runs: the fixed text a search needs to find
// -----------------------------------------------------------------------------

// A regular expression becomes the list of its variants (its alternatives,
// expanded, at most 16), each variant the list of its fixed runs: the text
// between the special characters. An optional or repeated part, a class, a
// dot, an anchor or a lookaround cut the run: the text on the two sides of
// them is not contiguous in what the pattern matches.
function regexVariants(src) {
  const MAX = 16;
  const CUT = null; // a variant is a list of text pieces and cuts
  let i = 0;
  const cutAll = (vs) => vs.map(v => v.concat([CUT]));
  const addText = (vs, t) => vs.map(v => v.concat([t]));
  const product = (vs, inner) => {
    const out = [];
    for (const v of vs) for (const w of inner) {
      if (out.length >= MAX) break;
      out.push(v.concat(w));
    }
    return out.length ? out : vs;
  };
  const runsOf = (v) => {
    const runs = [];
    let run = '';
    for (const p of v) { if (p === CUT) { runs.push(run); run = ''; } else run += p; }
    runs.push(run);
    return runs.filter(r => r.length > 0);
  };
  function alternatives() { // up to the closing parenthesis or the end
    const alts = [];
    let cur = [[]];
    while (i < src.length && src[i] !== ')') {
      if (src[i] === '|') { alts.push(...cur); cur = [[]]; i++; continue; }
      cur = atom(cur);
    }
    alts.push(...cur);
    return alts.slice(0, MAX);
  }
  function atom(cur) {
    const c = src[i];
    let piece = null; // { text } | { cut } | { variants }
    if (c === '\\') {
      const e = src[i + 1] || '';
      i += 2;
      if (/[dDwWsSbBAzZG0-9]/.test(e)) piece = { cut: true };
      else if (e === 'p' || e === 'P' || e === 'k') { if (src[i] === '{' || src[i] === '<') { const close = src.indexOf(src[i] === '{' ? '}' : '>', i); i = close < 0 ? src.length : close + 1; } piece = { cut: true }; }
      else if (e === 'n') piece = { text: '\n' };
      else if (e === 't') piece = { text: '\t' };
      else if (e === 'r') piece = { text: '\r' };
      else if (e === 'f' || e === 'v' || e === 'e' || e === 'a') piece = { cut: true };
      else if (e === 'u' && /^[0-9A-Fa-f]{4}/.test(src.slice(i))) { piece = { text: String.fromCharCode(parseInt(src.slice(i, i + 4), 16)) }; i += 4; }
      else if (e === 'x' && /^[0-9A-Fa-f]{2}/.test(src.slice(i))) { piece = { text: String.fromCharCode(parseInt(src.slice(i, i + 2), 16)) }; i += 2; }
      else piece = { text: e };
    } else if (c === '[') {
      let j = i + 1;
      if (src[j] === '^') j++;
      if (src[j] === ']') j++;
      while (j < src.length && src[j] !== ']') { if (src[j] === '\\') j++; j++; }
      i = j + 1;
      piece = { cut: true };
    } else if (c === '(') {
      i++;
      let look = false;
      if (src[i] === '?') {
        if (src[i + 1] === ':') i += 2;
        else if (src[i + 1] === '=' || src[i + 1] === '!') { i += 2; look = true; }
        else if (src[i + 1] === '<' && (src[i + 2] === '=' || src[i + 2] === '!')) { i += 3; look = true; }
        else if ((src[i + 1] === '<' || src[i + 1] === "'" || src[i + 1] === 'P') && /[A-Za-z<]/.test(src[i + 2])) {
          const close = src.indexOf(src[i + 1] === "'" ? "'" : '>', i + 2); i = close < 0 ? src.length : close + 1;
        } else { // inline options such as (?i) or (?i:...)
          let j = i + 1; while (j < src.length && /[a-zA-Z-]/.test(src[j])) j++;
          if (src[j] === ')') { i = j + 1; return cur; }
          i = j + 1;
        }
      }
      const inner = alternatives();
      if (src[i] === ')') i++;
      piece = look ? { cut: true } : { variants: inner };
    } else if (c === '^' || c === '$' || c === '.') {
      i++;
      piece = { cut: true };
    } else {
      i++;
      piece = { text: c };
    }
    const q = /^(?:[?*+]|\{(\d+)(?:,(\d*))?\})\??/.exec(src.slice(i));
    if (q) {
      i += q[0].length;
      const kind = q[0][0];
      const optional = kind === '?' || kind === '*' || (kind === '{' && q[1] === '0');
      if (!optional && piece.text !== undefined) return cutAll(addText(cur, piece.text)); // at least once
      return cutAll(cur);
    }
    if (piece.cut) return cutAll(cur);
    if (piece.text !== undefined) return addText(cur, piece.text);
    return product(cur, piece.variants);
  }
  const all = alternatives();
  return all.map(runsOf);
}

function likeVariants(src) {
  // * ? and [...] are wildcards; ` escapes
  const runs = [''];
  for (let i = 0; i < src.length; i++) {
    const c = src[i];
    if (c === '`') { runs[runs.length - 1] += src[i + 1] || ''; i++; continue; }
    if (c === '*' || c === '?') { runs.push(''); continue; }
    if (c === '[') { const j = src.indexOf(']', i); i = j < 0 ? src.length : j; runs.push(''); continue; }
    runs[runs.length - 1] += c;
  }
  return [runs.filter(r => r.length > 0)];
}

function variantsOf(site) {
  let variants;
  if (site.kind === 'regex') variants = regexVariants(site.value);
  else if (site.kind === 'like') variants = likeVariants(site.value);
  else variants = [[site.value]];
  // a hole in the consumer literal (a variable in the pattern) splits a run
  return variants.map(v => [].concat(...v.map(r => r.split(HOLE))).filter(worthLooking));
}

// A run is worth looking for when it has two visible characters, or three
// characters with one visible: «  OK   » is the bench's marker and counts,
// a lone parenthesis or «, » does not.
function worthLooking(run) {
  const visible = run.replace(/\s/g, '').length;
  return visible >= 2 || (visible >= 1 && run.length >= MIN_RUN);
}

// -----------------------------------------------------------------------------
//  The check
// -----------------------------------------------------------------------------

function producedIn(run, literals, nocase) {
  const needle = nocase ? run.toLowerCase() : run;
  for (const lit of literals) {
    for (const piece of lit.value.split(HOLE)) {
      const hay = nocase ? piece.toLowerCase() : piece;
      if (hay.includes(needle)) return lit;
    }
  }
  return null;
}

// `read(file)` returns the text of a file of the tree, or null.
function checkEntry(entry, read) {
  const problems = [];
  const c = entry.consumer || {};
  const lang = langOf(c.file || '');
  const consumerText = c.file ? read(c.file) : null;
  if (!lang || consumerText == null) return { ok: false, problems: ['consumer ' + c.file + ' cannot be read'] };
  const { sites } = sitesOf(consumerText, lang, viaOf(c));
  const site = sites.find(s => hashOf(s.value) === c.sha1 && (!c.kind || s.kind === c.kind));
  if (!site) {
    return { ok: false, problems: ['the consumed literal ' + c.sha1 + ' (' + (c.kind || 'any kind') + ') is no longer in ' + c.file +
      (c.line_hint ? ' (was near line ' + c.line_hint + ')' : '') +
      ': if it was translated, translate its producer in the same commit and bind the entry again with --rebind ' + entry.id + ' <line>'] };
  }
  const variants = variantsOf(site);
  if (variants.length === 0 || variants.every(v => v.length === 0)) {
    problems.push('the consumed literal at ' + c.file + ':' + site.line + ' has no fixed text of ' + MIN_RUN + ' characters or more: nothing to look for in a producer');
  }
  const literals = [];
  for (const p of entry.producers || []) {
    const t = read(p);
    const pl = langOf(p);
    if (t == null || !pl) { problems.push('producer ' + p + ' cannot be read'); continue; }
    for (const s of lexFile(t, pl).strings) if (!s.regex) literals.push(Object.assign({ file: p, line: lineOf(t, s.start) }, s));
  }
  if ((entry.producers || []).length === 0) problems.push('no producer listed');
  variants.forEach((runs, k) => {
    for (const run of runs) {
      if (!producedIn(run, literals, site.nocase)) {
        problems.push('variant ' + (k + 1) + ' of ' + c.file + ':' + site.line + ': «' + run + '» is in no string literal of ' +
          (entry.producers || []).join(', ') + (site.nocase ? ' (case ignored)' : ''));
      }
    }
  });
  for (const q of entry.quoted_in || []) {
    const t = read(q);
    if (t == null) { problems.push('quoted_in ' + q + ' cannot be read'); continue; }
    for (const runs of variants) for (const run of runs) {
      const hay = site.nocase ? t.toLowerCase() : t, needle = site.nocase ? run.toLowerCase() : run;
      if (!hay.includes(needle)) problems.push(q + ' no longer quotes «' + run + '» (consumed at ' + c.file + ':' + site.line + ')');
    }
  }
  return { ok: problems.length === 0, problems, site, variants };
}

function validateInventory(inv) {
  const errors = [];
  if (!inv || !Array.isArray(inv.entries)) return ['the inventory has no "entries" array'];
  const ids = new Set();
  for (const e of inv.entries) {
    if (!e.id || typeof e.id !== 'string') errors.push('an entry without id');
    else if (ids.has(e.id)) errors.push('two entries with id ' + e.id);
    else ids.add(e.id);
    if (!e.consumer || !e.consumer.file || !/^[0-9a-f]{12}$/.test(e.consumer.sha1 || '')) errors.push((e.id || '?') + ': consumer needs file and a 12-digit sha1');
    if (!e.what) errors.push((e.id || '?') + ': "what" says in English what the string is for');
    for (const k of Object.keys(e)) if (!['id', 'what', 'consumer', 'producers', 'quoted_in', 'why'].includes(k)) errors.push((e.id || '?') + ': unknown key ' + k);
    for (const k of Object.keys(e.consumer || {})) if (!['file', 'sha1', 'kind', 'line_hint', 'via', 'nocase'].includes(k)) errors.push((e.id || '?') + ': unknown consumer key ' + k);
    if (e.consumer && e.consumer.via && !e.consumer.kind) errors.push((e.id || '?') + ': a consumer with "via" declares its kind');
  }
  return errors;
}

function runCheck(root, inv) {
  const read = (f) => { try { return fs.readFileSync(path.join(root, f), 'utf8'); } catch (err) { return null; } };
  const errors = validateInventory(inv);
  if (errors.length) { console.log('CONSUMED STRINGS: bad inventory:\n  ' + errors.join('\n  ')); return 2; }
  let broken = 0, runs = 0;
  for (const e of inv.entries) {
    const r = checkEntry(e, read);
    if (r.variants) for (const v of r.variants) runs += v.length;
    if (!r.ok) { broken++; console.log('BROKEN ' + e.id + ' (' + e.what + ')'); for (const p of r.problems) console.log('    ' + p); }
  }
  console.log('consumed strings: ' + inv.entries.length + ' entries, ' + runs + ' runs checked, ' + broken + ' broken');
  return broken === 0 ? 0 : 1;
}

// -----------------------------------------------------------------------------
//  --find: consumer literals that another program produces
// -----------------------------------------------------------------------------

// The program a file belongs to: a C# project folder, a probe folder, or the
// script itself. A string produced and consumed inside one program is not a
// contract between two programs.
function unitOf(file) {
  const parts = file.split('/');
  if (langOf(file) !== 'cs') return file;
  if (parts.length === 1 || ['Mcp', 'Bridge', 'Tools'].includes(parts[0])) return 'bridge';
  if (parts[0] === 'Checks' && parts.length > 2) return parts.slice(0, 2).join('/');
  return parts[0];
}

function listFiles(root) {
  return cp.execFileSync('git', ['-C', root, 'ls-files', '-z'], { encoding: 'utf8', maxBuffer: 256 << 20 })
    .split('\0').filter(Boolean);
}

function find(root, inv, showAll) {
  const files = listFiles(root).filter(f => langOf(f));
  const bound = new Set((inv && inv.entries || []).map(e => e.consumer && e.consumer.file + '#' + e.consumer.sha1));
  const texts = new Map();
  const literals = [];
  for (const f of files) {
    let t; try { t = fs.readFileSync(path.join(root, f), 'utf8'); } catch (err) { continue; }
    texts.set(f, t);
    for (const s of lexFile(t, langOf(f)).strings) if (!s.regex) literals.push(Object.assign({ file: f, unit: unitOf(f), line: lineOf(t, s.start) }, s));
  }
  let shown = 0;
  for (const f of files) {
    if (!texts.has(f)) continue;
    const { sites } = sitesOf(texts.get(f), langOf(f));
    const others = literals.filter(l => l.unit !== unitOf(f));
    for (const site of sites) {
      const variants = variantsOf(site);
      if (!variants.length || !variants.some(v => v.length && v.some(r => r.trim().length >= 6 && /[A-Za-z]/.test(r)))) continue;
      const where = [];
      let all = true;
      for (const runs of variants) for (const run of runs) {
        const p = producedIn(run, others, site.nocase);
        if (!p) { all = false; break; }
        where.push(p.file + ':' + p.line);
      }
      if (!all) continue;
      const key = f + '#' + hashOf(site.value);
      if (bound.has(key) && !showAll) continue;
      shown++;
      console.log((bound.has(key) ? '[in inventory] ' : '') + f + ':' + site.line + '  ' + site.kind + (site.nocase ? '/nocase' : '') +
        '  sha1 ' + hashOf(site.value) + '  ' + JSON.stringify(site.value));
      console.log('      produced at ' + Array.from(new Set(where)).join(', '));
    }
  }
  console.log(shown + ' candidates');
  return 0;
}

// -----------------------------------------------------------------------------
//  --rebind: point an entry at the consumer literal on a line
// -----------------------------------------------------------------------------

function rebind(root, invFile, inv, id, line) {
  const e = (inv.entries || []).find(x => x.id === id);
  if (!e) { console.log('no entry ' + id); return 2; }
  const t = fs.readFileSync(path.join(root, e.consumer.file), 'utf8');
  const sites = sitesOf(t, langOf(e.consumer.file), viaOf(e.consumer)).sites.filter(s => s.line === line);
  if (sites.length !== 1) { console.log(sites.length + ' consumer literals on ' + e.consumer.file + ':' + line + ': need exactly one'); return 2; }
  e.consumer.sha1 = hashOf(sites[0].value);
  e.consumer.kind = sites[0].kind;
  e.consumer.line_hint = line;
  fs.writeFileSync(invFile, JSON.stringify(inv, null, 2) + '\n');
  console.log(id + ' bound to ' + e.consumer.file + ':' + line + ' ' + JSON.stringify(sites[0].value) + ': now run the check');
  return 0;
}

// -----------------------------------------------------------------------------
//  --self-test: the check, seen red on fakes
// -----------------------------------------------------------------------------

function selfTest() {
  const Q = String.fromCharCode(34); // a double quote, built, so the fakes read plainly
  const fake = (files) => (f) => (f in files ? files[f] : null);
  const producerCs = 'class P { void M() { Console.WriteLine(' + Q + 'the bridge is standing.' + Q + '); } }';
  const consumerCs = 'class C { bool M(string o) { return o.IndexOf(' + Q + 'the bridge is standing' + Q + ', StringComparison.OrdinalIgnoreCase) >= 0; } }';
  const bench = 'class B { void M(int n, int m) { Console.WriteLine($' + Q + '{n} checks, {m} failed' + Q + '); Console.WriteLine(' + Q + 'PARTIAL RUN' + Q + '); } }';
  const reader = '$last = $lines | Where-Object { $_ -match ' + Q + '^(PARTIAL RUN|[0-9]+ checks,)' + Q + ' }';
  const entry = (file, value, kind, producers, quoted) => ({ id: 'fake', what: 'a fake',
    consumer: { file, sha1: hashOf(value), kind }, producers, quoted_in: quoted });
  const cases = [
    { name: 'the text is produced: green', expect: true,
      files: { 'C.cs': consumerCs, 'P.cs': producerCs },
      entry: entry('C.cs', 'the bridge is standing', 'literal', ['P.cs']) },
    { name: 'the consumed string taken out of the producer: red', expect: false,
      files: { 'C.cs': consumerCs, 'P.cs': producerCs.replace('the bridge is standing.', 'the bridge is up.') },
      entry: entry('C.cs', 'the bridge is standing', 'literal', ['P.cs']) },
    { name: 'the text survives only in a comment of the producer: red', expect: false,
      files: { 'C.cs': consumerCs, 'P.cs': '// prints ' + Q + 'the bridge is standing' + Q + '\nclass P { void M() { Console.WriteLine(' + Q + 'up' + Q + '); } }' },
      entry: entry('C.cs', 'the bridge is standing', 'literal', ['P.cs']) },
    { name: 'the consumer literal changed (translated) and the entry was not bound again: red', expect: false,
      files: { 'C.cs': consumerCs.replace('the bridge is standing', 'the bridge stands'), 'P.cs': producerCs },
      entry: entry('C.cs', 'the bridge is standing', 'literal', ['P.cs']) },
    { name: 'a regular expression with two alternatives, both produced: green', expect: true,
      files: { 'bench.ps1': reader, 'B.cs': bench },
      entry: entry('bench.ps1', '^(PARTIAL RUN|[0-9]+ checks,)', 'regex', ['B.cs']) },
    { name: 'a regular expression with two alternatives, one no longer produced: red', expect: false,
      files: { 'bench.ps1': reader, 'B.cs': bench.replace('PARTIAL RUN', 'INCOMPLETE RUN') },
      entry: entry('bench.ps1', '^(PARTIAL RUN|[0-9]+ checks,)', 'regex', ['B.cs']) },
    { name: '-match ignores case: a producer in another case is green', expect: true,
      files: { 'bench.ps1': reader, 'B.cs': bench.replace('PARTIAL RUN', 'Partial run') },
      entry: entry('bench.ps1', '^(PARTIAL RUN|[0-9]+ checks,)', 'regex', ['B.cs']) },
    { name: '.Contains does not ignore case: a producer in another case is red', expect: false,
      files: { 'C.ps1': 'if ($out.Contains(' + Q + 'Bridge Ready' + Q + ')) { }', 'P.cs': producerCs.replace('the bridge is standing.', 'bridge ready') },
      entry: entry('C.ps1', 'Bridge Ready', 'literal', ['P.cs']) },
    { name: 'a document that quoted the text lost it: red', expect: false,
      files: { 'C.cs': consumerCs, 'P.cs': producerCs, 'README.md': 'The bridge prints a line when it is up.' },
      entry: entry('C.cs', 'the bridge is standing', 'literal', ['P.cs'], ['README.md']) },
    { name: 'a run never spans the hole of an interpolated producer: red', expect: false,
      files: { 'C.ps1': 'if ($l -match ' + Q + 'checks, 0 failed' + Q + ') { }', 'B.cs': bench },
      entry: entry('C.ps1', 'checks, 0 failed', 'regex', ['B.cs']) },
    { name: 'a search that is only in a comment of the consumer is not a consumer: red', expect: false,
      files: { 'C.cs': '// return o.IndexOf(' + Q + 'the bridge is standing' + Q + ') >= 0;', 'P.cs': producerCs },
      entry: entry('C.cs', 'the bridge is standing', 'literal', ['P.cs']) },
    { name: 'a -like pattern whose words are produced: green', expect: true,
      files: { 'C.ps1': 'if ($out -like ' + Q + '*bridge is*' + Q + ') { }', 'P.cs': producerCs },
      entry: entry('C.ps1', '*bridge is*', 'like', ['P.cs']) },
    { name: 'a pattern handed to a helper of the consumer (via), produced: green', expect: true,
      files: { 'r.ps1': 'function Take($re) { $lines | Where-Object { $_ -match $re } }\n$v = Take ' + Q + '^library ([0-9.]+)' + Q, 'B.cs': bench.replace('PARTIAL RUN', 'library ') },
      entry: Object.assign(entry('r.ps1', '^library ([0-9.]+)', 'regex', ['B.cs']), { consumer: { file: 'r.ps1', sha1: hashOf('^library ([0-9.]+)'), kind: 'regex', via: 'Take', nocase: true } }) },
    { name: 'a pattern handed to a helper of the consumer (via), no longer produced: red', expect: false,
      files: { 'r.ps1': 'function Take($re) { $lines | Where-Object { $_ -match $re } }\n$v = Take ' + Q + '^library ([0-9.]+)' + Q, 'B.cs': bench },
      entry: Object.assign(entry('r.ps1', '^library ([0-9.]+)', 'regex', ['B.cs']), { consumer: { file: 'r.ps1', sha1: hashOf('^library ([0-9.]+)'), kind: 'regex', via: 'Take', nocase: true } }) },
    { name: 'a JavaScript consumer and a PowerShell producer: green', expect: true,
      files: { 'c.js': 'if (out.includes(' + Q + 'reinstalling the' + Q + ')) {}', 'p.ps1': 'Write-Host ' + Q + 'reinstalling the bridge in $dir' + Q },
      entry: entry('c.js', 'reinstalling the', 'literal', ['p.ps1']) },
  ];
  let bad = 0;
  for (const k of cases) {
    const r = checkEntry(k.entry, fake(k.files));
    const good = r.ok === k.expect;
    if (!good) bad++;
    console.log((good ? '  ok   ' : '  BAD  ') + k.name + (good ? '' : '  -> ' + (r.ok ? 'green' : r.problems.join('; '))));
  }
  // the fixed text of the pattern shapes the real inventory uses
  const B = String.fromCharCode(92); // a backslash, built for the same reason
  const shapes = [
    ['^executable: ([0-9]+) operations ' + B + '(in the catalogue: ([0-9]+)' + B + ')',
      [['executable: ', ' operations (in the catalogue: ', ')']]],
    ['^(PARTIAL RUN|[0-9]+ checks,)', [['PARTIAL RUN'], [' checks,']]],
    ['^  OK   ', [['  OK   ']]],
    ['IT DID.? NOT CHANGE', [['IT DID', ' NOT CHANGE']]],
    ['install|update', [['install'], ['update']]],
    ['a(b|c)d', [['abd'], ['acd']]],
    ['(?i)ready', [['ready']]],
    [B + 'd+ failed', [[' failed']]],
    ['checks, ' + B + 'd+ failed', [['checks, ', ' failed']]],
  ];
  for (const [src, want] of shapes) {
    const got = regexVariants(src);
    const same = JSON.stringify(got) === JSON.stringify(want);
    if (!same) bad++;
    console.log((same ? '  ok   ' : '  BAD  ') + 'the fixed text of /' + src + '/' + (same ? '' : ': ' + JSON.stringify(got)));
  }
  // and of a -like pattern
  const like = JSON.stringify(likeVariants('*bridge*standing?'));
  const likeOk = like === JSON.stringify([['bridge', 'standing']]);
  if (!likeOk) bad++;
  console.log((likeOk ? '  ok   ' : '  BAD  ') + 'the fixed text of -like *bridge*standing?' + (likeOk ? '' : ': ' + like));
  console.log('self-test: ' + (cases.length + shapes.length + 1) + ' fakes, ' + bad + ' ended otherwise');
  return bad === 0 ? 0 : 1;
}

// -----------------------------------------------------------------------------

function main(argv) {
  let root = ROOT_DEFAULT;
  const i = argv.indexOf('--root');
  if (i >= 0) root = path.resolve(argv[i + 1]);
  if (argv.includes('--self-test')) return selfTest();
  const invFile = path.join(root, INVENTORY);
  let inv = null;
  try { inv = JSON.parse(fs.readFileSync(invFile, 'utf8')); }
  catch (err) { if (!argv.includes('--find')) { console.log('CONSUMED STRINGS: cannot read ' + INVENTORY + ': ' + err.message); return 2; } }
  if (argv.includes('--find')) return find(root, inv, argv.includes('--all'));
  const r = argv.indexOf('--rebind');
  if (r >= 0) return rebind(root, invFile, inv, argv[r + 1], parseInt(argv[r + 2], 10));
  return runCheck(root, inv);
}

module.exports = { lexFile, sitesOf, regexVariants, likeVariants, variantsOf, checkEntry, hashOf, unitOf };

if (require.main === module) {
  let code;
  try { code = main(process.argv.slice(2)); }
  catch (err) { console.log('CONSUMED STRINGS: crashed: ' + (err.stack || err)); code = 2; }
  process.exitCode = code;
}
