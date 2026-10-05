#!/usr/bin/env node
// =============================================================================
//  window-4-comments.js - the comments of window 4 (W3.1-bis.7)
// =============================================================================
//
//  Translates every Italian COMMENT of the repository into English without
//  touching anything that is not a comment, and proves it. The comments are
//  C# (//, ///, /* */), JavaScript, PowerShell (#, <# #>), and the comment
//  syntaxes of .yml, .csproj/.config/.manifest (<!-- -->), .gitignore,
//  .gitattributes and the shell-like files (CODEOWNERS, hooks).
//
//  THE COMMENTS ARE FOUND BY A PARSER, never by looking for `//` in the raw
//  text: Roslyn for C#, acorn for JavaScript, the PowerShell parser for
//  PowerShell (token-stream.js --comments shows what each finds). A `//` in a
//  string, a regular expression or a here-string is not a comment; the `#!`
//  line and #requires are not comments. The formats without a parser here
//  (YAML, XML, ignore files, shell) have a small scanner each, written to the
//  rules of the format (quotes, block scalars, here-documents, CDATA), and
//  every line the language gate calls Italian is checked to lie inside a
//  comment found: a hit that falls outside every one is listed as UNMAPPED.
//
//  THE UNIT IS THE COMMENT BLOCK: the consecutive standalone line comments
//  with the same delimiter and the same indentation, or one block comment, or
//  one trailing comment. A block is a list of LINES; of every line the tool
//  keeps only the BODY, the text after the delimiter (`//`, `///`, `#`, `/*`,
//  the ` *` decoration of a block comment) and before the closing delimiter.
//  The delimiters, the decoration and the line ends are never in the text a
//  translator sees, so they cannot be changed by mistake: they stay in the
//  file.
//
//  A block is Italian when the detector (detect-italian.js) finds Italian in
//  one of its lines; only those blocks go to a translator. A translator keeps
//  THE NUMBER OF LINES of every block (it rewraps inside the block), so every
//  file:line citation of the TODO stays valid.
//
//  THE CHUNKS
//    --extract <dir>     writes <dir>/<id>.json, one chunk per ~15-20k
//                        Italian words, grouped by folder, a file never split,
//                        and <dir>/plan.json (chunks, words, files). A chunk is
//                        { id, folder, base, files: [{ file, sha256, ... }],
//                          blocks: [{ id, file, start, end, kind, open, close,
//                                     words, text: [the body of every line] }] }.
//                        The translator writes <dir>/<id>.en.json: the same
//                        blocks, the same fields, and `text` translated, with
//                        as many lines as the original.
//    --apply <dir> [--dry] [--chunk a,b]
//                        writes the translations back by position. It REFUSES,
//                        for a file (the other files of the chunk still go in):
//                          - a block whose text in the file is no longer the
//                            original of the chunk (stale), or a block of the
//                            chunk with no translation;
//                          - a block whose delimiters, position or kind differ
//                            from the original, or whose NUMBER OF LINES
//                            changed, or whose text holds a line end or the
//                            closing delimiter of its comment;
//                          - an invariant of the text that differs between the
//                            original and the translation of a block: the
//                            numbers (every run of digits), the references
//                            (W3.1-bis.6, Z-C20, F9-bis, D11...), the names
//                            between backticks, the file names, the URLs, the
//                            XML doc tags and their attributes (<see cref>,
//                            <param name>), the code-like names (camelCase,
//                            snake_case, Name(), the .KEYWORD lines of a
//                            PowerShell help comment), the guillemets, and the
//                            shape of the block (which lines are empty, the
//                            indentation and the bullet marker of each line);
//                          - a file where, once written, the text outside the
//                            comments differs in any way, or the comments are
//                            not the same blocks, or the token stream of the
//                            parser (token-stream.js) is not identical.
//                        A refused file is not written. A file already
//                        written from the same translation is "already
//                        applied" (apply is idempotent). Italian the detector
//                        still finds in a written block is printed.
//    --check [--rev <rev>] [--zero]
//                        for every file that differs from <rev> (default
//                        HEAD): the token stream is identical, the text
//                        outside the comments is identical, the blocks are the
//                        same and keep the invariants above, and the Italian
//                        comment count per file, before and after. --zero also
//                        fails while any touched file has Italian comments.
//    --self-test         a fixture of every language in a temporary git
//                        repository; every refusal above is seen red on a
//                        mutated copy, and the good cases green.
//    --root <dir>        another checkout (default: the repository of this file)
//
//  EXIT CODES
//    0  done, nothing refused
//    1  a refusal or a difference outside the comments, or --self-test red
//    2  cannot run (no parser, a file that does not parse, a bad argument)
//    3  done, but Italian is still found in comments that were written
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const crypto = require('crypto');
const os = require('os');

const TS = require('./token-stream.js');
const DET = require('./detect-italian.js');

// token-stream.js reaches the acorn inside Node only with --expose-internals
if (require.main === module && !TS.hasAcorn() && !process.execArgv.includes('--expose-internals')) {
  const r = cp.spawnSync(process.execPath, ['--expose-internals', __filename].concat(process.argv.slice(2)), { stdio: 'inherit' });
  process.exit(r.status === null ? 2 : r.status);
}

class Stop extends Error {}
function cannot(msg) { throw new Stop(msg); }

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const PARSED = { cs: 'csharp', js: 'javascript', ps: 'powershell' };
const KINDS = new Set(['cs', 'js', 'ps', 'yaml', 'xml', 'ignore', 'hash']);
const TARGET_WORDS = 17500;
const MAX_WORDS = 20000;

// ── reading and writing a file as the detector reads it ─────────────────────
function readSource(root, rel) {
  const raw = fs.readFileSync(path.join(root, rel));
  return decodeSource(raw, rel);
}

function decodeSource(raw, rel) {
  if (raw.includes(0)) cannot(rel + ': holds a NUL byte, not a text file');
  if ((raw[0] === 0xff && raw[1] === 0xfe) || (raw[0] === 0xfe && raw[1] === 0xff)) cannot(rel + ': UTF-16, not handled');
  const bom = raw[0] === 0xef && raw[1] === 0xbb && raw[2] === 0xbf;
  const body = raw.slice(bom ? 3 : 0);
  const text = body.toString('utf8');
  if (!Buffer.from(text, 'utf8').equals(body)) cannot(rel + ': not valid UTF-8');
  return { raw, bom, text, sha: crypto.createHash('sha256').update(raw).digest('hex') };
}

function writeSource(root, rel, text, bom) {
  fs.writeFileSync(path.join(root, rel), Buffer.concat([bom ? Buffer.from([0xef, 0xbb, 0xbf]) : Buffer.alloc(0), Buffer.from(text, 'utf8')]));
}

function git(root, args) {
  try {
    return cp.execFileSync('git', ['-C', root].concat(args), { maxBuffer: 1 << 30, stdio: ['ignore', 'pipe', 'pipe'] });
  } catch (e) {
    cannot('git ' + args.join(' ') + ' failed in ' + root + ': ' + String(e.stderr || e.message).trim());
  }
}

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

// the physical lines of a text: [text without its CR, offset of the line]
function eachLine(text, fn) {
  let pos = 0;
  for (;;) {
    const nl = text.indexOf('\n', pos);
    let end = nl < 0 ? text.length : nl;
    if (end > pos && text[end - 1] === '\r') end--;
    fn(text.slice(pos, end), pos);
    if (nl < 0) break;
    pos = nl + 1;
  }
}

// ── the formats without a parser here ───────────────────────────────────────
// YAML: whole-line # comments and trailing ones (` #` outside quotes); the
// lines of a block scalar (key: |) are text, not comments, as in the detector.
// A quote opens a quoted scalar only where a scalar starts (the start of the
// line, after "key: ", after "- ", after [ { , in a flow, after an anchor or a
// tag); a quote in the middle of a plain scalar (run: echo 'a # b') is a
// character of it, and the # after a blank there IS a comment. A quoted
// scalar may run over several lines: its lines are text, not comments.
function yamlComments(text) {
  const out = [];
  let block = -1;
  let q = null;        // the quote of a quoted scalar still open from a line before
  let flow = 0;        // the depth of [ { still open
  eachLine(text, (l, at) => {
    const indent = l.length - l.trimStart().length;
    const t = l.trim();
    if (q === null && block >= 0) {
      if (!t || indent > block) return;
      block = -1;
    }
    if (q === null && !t) return;
    if (q === null && flow === 0 && t.startsWith('#')) { out.push([at + indent, at + l.length]); return; }
    let cut = -1;
    let start = q === null;  // a scalar may start here
    let i = 0;
    for (; i < l.length; i++) {
      const c = l[i];
      if (q) {
        if (c === q) { if (q === "'" && l[i + 1] === "'") i++; else { q = null; start = false; } } else if (c === '\\' && q === '"') i++;
        continue;
      }
      const blankBefore = i === 0 || /\s/.test(l[i - 1]);
      if (c === '#' && blankBefore) { cut = i; break; }
      if (/\s/.test(c)) continue;
      if (start) {
        if (c === '"' || c === "'") { q = c; continue; }
        if (c === '-' && (i + 1 === l.length || /\s/.test(l[i + 1]))) continue;   // a sequence entry
        if (c === '?' && (i + 1 === l.length || /\s/.test(l[i + 1]))) continue;   // a complex key
        if (c === '[' || c === '{') { flow++; continue; }
        if (c === '&' || c === '!') { while (i + 1 < l.length && !/\s/.test(l[i + 1])) i++; continue; }  // an anchor or a tag
        start = false;
      }
      if (c === ':' && (i + 1 === l.length || /\s/.test(l[i + 1]) || (flow && /[,\]}]/.test(l[i + 1])))) { start = true; continue; }
      if (flow && (c === '[' || c === '{')) { flow++; start = true; continue; }
      if (flow && (c === ']' || c === '}')) { flow--; start = false; continue; }
      if (flow && c === ',') { start = true; continue; }
    }
    if (cut >= 0) out.push([at + cut, at + l.length]);
    if (q !== null) return;
    const code = (cut >= 0 ? l.slice(0, cut) : l).trim();
    const m = /^(?:-\s+)?(?:(?:[A-Za-z0-9_.-]+|"[^"]*"|'[^']*'):\s+)?([|>][-+0-9]*)$/.exec(code);
    if (m && (code.length > m[1].length)) block = indent;
  });
  return out;
}

// XML: <!-- ... -->; a comment-looking text inside a CDATA section, a
// processing instruction, or an attribute value is not a comment.
function xmlComments(text, file) {
  const out = [];
  const n = text.length;
  let i = 0;
  while (i < n) {
    if (text.startsWith('<!--', i)) {
      const j = text.indexOf('-->', i + 4);
      if (j < 0) cannot(file + ': a comment is never closed');
      out.push([i, j + 3]);
      i = j + 3;
    } else if (text.startsWith('<![CDATA[', i)) {
      const j = text.indexOf(']]>', i);
      i = j < 0 ? n : j + 3;
    } else if (text.startsWith('<?', i)) {
      const j = text.indexOf('?>', i);
      i = j < 0 ? n : j + 2;
    } else if (text[i] === '<' && /[A-Za-z_\/!]/.test(text[i + 1] || '')) {
      let q = null;
      i++;
      for (; i < n; i++) {
        const c = text[i];
        if (q) { if (c === q) q = null; continue; }
        if (c === '"' || c === "'") { q = c; continue; }
        if (c === '>') { i++; break; }
      }
    } else i++;
  }
  return out;
}

// .gitignore and .gitattributes: a comment is a line that starts with #.
function ignoreComments(text) {
  const out = [];
  eachLine(text, (l, at) => {
    const t = l.trimStart();
    if (t.startsWith('#')) out.push([at + (l.length - t.length), at + l.length]);
  });
  return out;
}

// Shell-like files (hooks, CODEOWNERS): a # at the start of a line or after
// blanks, outside quotes, not in a here-document; the #! line is not one.
function hashComments(text) {
  const out = [];
  let heredoc = null;
  let first = true;
  eachLine(text, (l, at) => {
    const isFirst = first;
    first = false;
    if (isFirst && l.startsWith('#!')) return;
    if (heredoc !== null) { if (l.trim() === heredoc) heredoc = null; return; }
    const hd = /<<-?\s*(['"]?)([A-Za-z_][A-Za-z0-9_]*)\1/.exec(l);
    if (hd) heredoc = hd[2];
    let q = null;
    for (let i = 0; i < l.length; i++) {
      const c = l[i];
      if (q) { if (c === '\\' && q === '"') { i++; continue; } if (c === q) q = null; continue; }
      if (c === '"' || c === "'") { q = c; continue; }
      if (c === '#' && (i === 0 || /\s/.test(l[i - 1]))) { out.push([at + i, at + l.length]); break; }
    }
  });
  return out;
}

// The comments of a set of { key, kind, text }: Map(key -> [[start, end]]).
function findElementsBatch(entries) {
  const out = new Map();
  const parsed = [];
  for (const e of entries) {
    if (PARSED[e.kind]) parsed.push({ key: e.key, lang: PARSED[e.kind], text: e.text });
    else if (e.kind === 'yaml') out.set(e.key, yamlComments(e.text));
    else if (e.kind === 'xml') out.set(e.key, xmlComments(e.text, e.key));
    else if (e.kind === 'ignore') out.set(e.key, ignoreComments(e.text));
    else if (e.kind === 'hash') out.set(e.key, hashComments(e.text));
    else cannot('no comment finder for ' + e.key + ' (' + e.kind + ')');
  }
  if (parsed.length) for (const [k, v] of TS.findComments(parsed)) out.set(k, v);
  return out;
}

// ── blocks: the comments of a file as lists of lines ────────────────────────
// A line comment is [delimiter][body] up to its line end. A block comment is
// cut at its line ends: the first line is [open][body], a middle line is
// [indentation and decoration][body], the last is [..][body][close].
function describe(text, s, e, kind) {
  const raw = text.slice(s, e);
  let style = null;
  if (kind === 'cs' || kind === 'js') style = raw.startsWith('/*') ? 'c' : raw.startsWith('//') ? 'slash' : null;
  else if (kind === 'ps') style = raw.startsWith('<#') ? 'ps' : raw.startsWith('#') ? 'hash' : null;
  else if (kind === 'xml') style = raw.startsWith('<!--') ? 'xml' : null;
  else style = raw.startsWith('#') ? 'hash' : null;
  if (!style) cannot('not a comment of a ' + kind + ' file: "' + raw.slice(0, 20) + '"');
  if (style === 'slash' || style === 'hash') {
    const open = (style === 'slash' ? /^\/+/ : /^#+/).exec(raw)[0];
    let be = e;
    while (be > s + open.length && text[be - 1] === '\r') be--;
    return { type: 'line', open, close: '', lines: [{ bs: s + open.length, be }] };
  }
  let open, close, openLen;
  if (style === 'c') {
    close = '*/';
    const run = /^\/\*+/.exec(raw)[0].length;
    openLen = Math.max(2, Math.min(run, raw.length - 2));
  } else if (style === 'ps') { close = '#>'; openLen = 2; } else { close = '-->'; openLen = 4; }
  open = raw.slice(0, openLen);
  if (!raw.endsWith(close) || raw.length < openLen + close.length) cannot('a block comment is not closed: "' + raw.slice(0, 20) + '"');
  const segs = [];
  let from = s;
  for (let i = s; i < e; i++) {
    if (text[i] === '\n') {
      let end = i;
      if (end > from && text[end - 1] === '\r') end--;
      segs.push([from, end]);
      from = i + 1;
    }
  }
  segs.push([from, e]);
  const lines = segs.map(([a, b], k) => {
    let bs;
    if (k === 0) bs = a + openLen;
    else bs = a + new RegExp(style === 'c' ? '^\\s*(?:\\*(?!/))?' : '^\\s*').exec(text.slice(a, b))[0].length;
    const be = k === segs.length - 1 ? e - close.length : b;
    return { bs: Math.min(bs, be), be };
  });
  return { type: 'block', open, close, lines };
}

function buildBlocks(text, elements, kind) {
  const starts = lineStarts(text);
  const blocks = [];
  let prev = null;
  for (const [s, e] of elements) {
    const el = describe(text, s, e, kind);
    const before = text.slice(starts[lineAt(starts, s) - 1], s);
    const standalone = /^\s*$/.test(before);
    const startLine = lineAt(starts, s);
    const endLine = lineAt(starts, e - 1);
    if (el.type === 'line' && standalone && prev && prev.kind === 'line' && prev.standalone && prev.open === el.open &&
        prev.indent === before && prev.end + 1 === startLine) {
      prev.lines.push(el.lines[0]);
      prev.end = endLine;
      continue;
    }
    prev = { kind: el.type, open: el.open, close: el.close, start: startLine, end: endLine, standalone, indent: before, lines: el.lines };
    blocks.push(prev);
  }
  for (const b of blocks) for (const l of b.lines) l.body = text.slice(l.bs, l.be);
  return blocks;
}

const blockKey = (b) => b.start + '-' + b.end + '-' + b.kind + '-' + b.open + '-' + b.close;

function bodiesOf(block) { return block.lines.map(l => l.body); }

// the text with every body removed: what must not change
function masked(text, blocks) {
  const cuts = [];
  for (const b of blocks) for (const l of b.lines) cuts.push([l.bs, l.be]);
  cuts.sort((a, b) => a[0] - b[0]);
  let out = '';
  let at = 0;
  for (const [a, b] of cuts) { out += text.slice(at, a); at = b; }
  return out + text.slice(at);
}

// ── the invariants of a text ────────────────────────────────────────────────
const FILE_EXT = 'cs|js|mjs|cjs|ps1|psm1|psd1|json|md|csproj|sln|yml|yaml|txt|exe|dll|pdb|config|manifest|bat|sh|png|jpg|jpeg|gif|svg|sldprt|sldasm|slddrw|sldlfp|step|stp|stl|igs|iges|pdf|xml|xaml|resx|tsv|csv|html|zip|log|py|ps';
const HELP_KEYWORD = /^\s*\.(SYNOPSIS|DESCRIPTION|PARAMETER|EXAMPLE|INPUTS|OUTPUTS|NOTES|LINK|COMPONENT|ROLE|FUNCTIONALITY|FORWARDHELPTARGETNAME|FORWARDHELPCATEGORY|REMOTEHELPRUNSPACE|EXTERNALHELP)\b(.*)$/i;
const HELP_WITH_ARGUMENT = new Set(['PARAMETER', 'LINK', 'FORWARDHELPTARGETNAME', 'FORWARDHELPCATEGORY', 'REMOTEHELPRUNSPACE', 'EXTERNALHELP']);
const STRUCTURAL_TAGS =new Set(['summary', 'remarks', 'returns', 'param', 'typeparam', 'exception', 'value', 'example', 'list',
  'listheader', 'item', 'term', 'description', 'para', 'code', 'inheritdoc', 'seealso', 'include', 'permission', 'completionlist']);

function sorted(a) { return a.slice().sort(); }

function factsOf(lines) {
  const text = lines.join('\n');
  const squash = (s) => s.replace(/\s+/g, ' ').trim();
  const trimEnd = (s) => s.replace(/[.,;:!?)\]]+$/, '');
  const f = {};
  f.numbers = sorted(text.match(/\d+/g) || []);
  // an English ordinal (14th) or a "64-bit" is a number with its word: the number is kept by the digits check
  f.references = sorted((text.match(/[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?/g) || [])
    .filter(w => /\d/.test(w) && /[A-Za-z]/.test(w) && !/^\d+(?:st|nd|rd|th|-bit)$/i.test(w)));
  f.backticks = sorted([...text.matchAll(/`([^`]*)`/g)].map(m => squash(m[1])));
  f.files = sorted((text.match(new RegExp('[\\w.\\\\/-]+\\.(?:' + FILE_EXT + ')\\b', 'gi')) || []).map(s => s.toLowerCase()));
  f.urls = sorted((text.match(/https?:\/\/[^\s<>)"'\]`]+/g) || []).map(trimEnd));
  // an XML tag as XML writes it: a name and attributes in name="value" form. A placeholder such as
  // <lowercase hash> is prose in angle brackets, and is translated with the rest.
  const tags = (text.match(/<\/?[A-Za-z][\w:.-]*(?:\s+[\w:.-]+\s*=\s*(?:"[^"]*"|'[^']*'))*\s*\/?>/g) || []).map(squash);
  f.tags = sorted(tags);
  f.structure = tags.filter(t => STRUCTURAL_TAGS.has(/^<\/?([A-Za-z]+)/.exec(t)[1].toLowerCase()));
  const names = [];
  for (const m of text.matchAll(/[A-Za-z_][A-Za-z0-9_]*/g)) {
    const w = m[0];
    if (/_/.test(w) || /[a-z][A-Z]/.test(w)) names.push(w);
    else if (text[m.index + w.length] === '(') names.push(w + '(');
  }
  f.names = sorted(names);
  // a marker that a check of the repository reads in the comments: it is code for that check.
  // ZERO-E-ZERO: is read by Checks/zero-or-unknown.js; OBSOLETE-MEASURED: <case> by
  // Tests/ObsoleteCallScanner.cs; Guide: <page>.html, UNDOCUMENTED: and MEASURED: <case> by
  // Tests/SelectionMarksContract.cs (with the value inside the quote between guillemets, below)
  f.markers = sorted((text.match(/ZERO-E-ZERO:?|OBSOLETE-MEASURED:\s*[A-Za-z0-9_-]+|(?<![\w-])MEASURED:\s*[A-Za-z0-9_-]+|UNDOCUMENTED:|Guide:\s*\w+(?:~\w+)?\.html/g) || [])
    .map(m => m.replace(/\s+/g, ' ')));
  f.guillemets = [(text.match(/\u00ab/g) || []).length, (text.match(/\u00bb/g) || []).length].join(',');
  // the numbers inside each quote between guillemets stay inside it (a contract reads the value
  // of a constant inside the quote of its guide page)
  f.quoteNumbers = sorted([...text.matchAll(/\u00ab([^\u00bb]*)\u00bb/g)].map(m => (m[1].match(/\d+/g) || []).join(',')));
  // the keywords of a PowerShell help comment (.SYNOPSIS ...): only the keywords
  // that PowerShell reads, with the argument of those that take one (a line that
  // starts with ".NET" is prose)
  f.helpKeywords = lines.map(l => HELP_KEYWORD.exec(l)).filter(Boolean).map(m => m[1].toUpperCase() + (HELP_WITH_ARGUMENT.has(m[1].toUpperCase()) ? ' ' + m[2].trim() : ''));
  return f;
}

function diffList(a, b) {
  const count = (list) => { const m = new Map(); for (const x of list) m.set(x, (m.get(x) || 0) + 1); return m; };
  const A = count(a), B = count(b);
  const missing = [], extra = [];
  for (const [k, n] of A) for (let i = B.get(k) || 0; i < n; i++) missing.push(k);
  for (const [k, n] of B) for (let i = A.get(k) || 0; i < n; i++) extra.push(k);
  return { missing, extra };
}

const BULLET = /^(?:[-*+\u2022]\s|\d+[.)]\s|\([a-z0-9]\)\s|[a-z]\)\s)/;
const NEWLINES = /[\r\n\u0085\u2028\u2029]/;

// The problems between the bodies of a block before and after a translation.
function compareBodies(orig, now, block, lang) {
  const problems = [];
  if (now.length !== orig.length) {
    problems.push('the number of lines changed: ' + orig.length + ' before, ' + now.length + ' after (the block keeps its lines and rewraps inside them)');
    return problems;
  }
  now.forEach((b, i) => {
    if (typeof b !== 'string') problems.push('line ' + (i + 1) + ' is not a string');
    else if (NEWLINES.test(b)) problems.push('line ' + (i + 1) + ' holds a line end');
  });
  if (problems.length) return problems;
  if (block.close && now.some(b => b.includes(block.close))) problems.push('a line holds the closing delimiter ' + block.close + ' of its comment');
  if (block.close === '-->' && now.some((b, i) => (b.match(/--/g) || []).length > (orig[i].match(/--/g) || []).length))
    problems.push('a line holds a double hyphen, not allowed inside an XML comment');
  // a body that ends with a hyphen right before --> writes --->, which is not well-formed XML
  if (block.close === '-->' && /-$/.test(now[now.length - 1]) && !/-$/.test(orig[orig.length - 1]))
    problems.push('the last line ends with a hyphen, and a hyphen before --> is not allowed in an XML comment');
  // a C# documentation comment is XML: a stray < or & (or one lost) breaks it, or changes an entity
  if (lang === 'cs' && (block.open === '///' || block.open === '/**')) {
    for (const ch of ['<', '>', '&']) {
      const count = (list) => list.reduce((n, s) => n + s.split(ch).length - 1, 0);
      if (count(orig) !== count(now)) problems.push('the count of "' + ch + '" in a documentation comment changed (' + count(orig) + ' before, ' + count(now) + ' after): its XML would break');
    }
  }
  orig.forEach((o, i) => {
    const b = now[i];
    const oEmpty = o.trim() === '', bEmpty = b.trim() === '';
    if (oEmpty !== bEmpty) { problems.push('line ' + (i + 1) + (oEmpty ? ' was empty and is not' : ' was not empty and is now')); return; }
    if (oEmpty) return;
    const oLead = /^\s*/.exec(o)[0], bLead = /^\s*/.exec(b)[0];
    if (oLead !== bLead) problems.push('line ' + (i + 1) + ': the indentation changed (' + JSON.stringify(oLead) + ' to ' + JSON.stringify(bLead) + ')');
    const om = BULLET.exec(o.slice(oLead.length)), bm = BULLET.exec(b.slice(bLead.length));
    if ((om ? om[0] : '') !== (bm ? bm[0] : '')) problems.push('line ' + (i + 1) + ': the bullet marker changed (' + JSON.stringify(om ? om[0] : '') + ' to ' + JSON.stringify(bm ? bm[0] : '') + ')');
  });
  const fo = factsOf(orig), fn = factsOf(now);
  for (const k of Object.keys(fo)) {
    if (k === 'guillemets') { if (fo[k] !== fn[k]) problems.push('the guillemets changed (opening,closing counts ' + fo[k] + ' before, ' + fn[k] + ' after)'); continue; }
    if (k === 'structure' || k === 'helpKeywords') {
      if (JSON.stringify(fo[k]) !== JSON.stringify(fn[k])) problems.push('the ' + k + ' changed: ' + JSON.stringify(fo[k]) + ' before, ' + JSON.stringify(fn[k]) + ' after');
      continue;
    }
    const d = diffList(fo[k], fn[k]);
    if (d.missing.length || d.extra.length)
      problems.push('the ' + k + ' changed' + (d.missing.length ? ', lost ' + JSON.stringify(d.missing.slice(0, 6)) : '') + (d.extra.length ? ', added ' + JSON.stringify(d.extra.slice(0, 6)) : ''));
  }
  return problems;
}

// ── Italian, as the language gate sees it ───────────────────────────────────
let detectorConfig = null;
function detector(root) {
  // a temporary repository (the self-test) has no lexicon: this checkout's is used
  const at = fs.existsSync(path.join(root, 'Checks', 'language', 'lexicon.json')) ? root : ROOT_DEFAULT;
  if (!detectorConfig || detectorConfig.root !== at) detectorConfig = DET.loadConfig(at);
  return detectorConfig;
}

// Map(line -> Italian words) of the comments of a file, from the detector.
function italianLines(root, rel, text) {
  const a = DET.analyzeText(rel, text, detector(root));
  const m = new Map();
  for (const h of a.hits) if (h.category === 'comments') m.set(h.line, (m.get(h.line) || 0) + (h.score || 1));
  return m;
}

function listTracked(root) {
  return git(root, ['ls-files', '-z']).toString('utf8').split('\0').filter(Boolean)
    .filter(f => !DET.isBinaryByExtension(f) && KINDS.has(DET.kindOf(f)));
}

// ── --extract ───────────────────────────────────────────────────────────────
function folderOf(rel) { return rel.includes('/') ? rel.split('/')[0] : '(root)'; }
function slugOf(folder) { return folder === '(root)' ? 'root' : folder.toLowerCase().replace(/^\.+/, '').replace(/[^a-z0-9]+/g, '-'); }

// everything: every block, Italian or not (the self-test has English fixtures)
function extractAll(root, dir, opts) {
  opts = opts || {};
  const files = opts.files || listTracked(root);
  const entries = [];
  const src = new Map();
  for (const rel of files) {
    const s = readSource(root, rel);
    src.set(rel, s);
    entries.push({ key: rel, kind: DET.kindOf(rel), text: s.text });
  }
  const found = findElementsBatch(entries);
  const perFile = [];
  const unmapped = [];
  for (const rel of files) {
    const s = src.get(rel);
    const kind = DET.kindOf(rel);
    const blocks = buildBlocks(s.text, found.get(rel), kind);
    const hits = opts.everything ? null : italianLines(root, rel, s.text);
    const chosen = [];
    for (const b of blocks) {
      let words = 0;
      if (hits) { for (let ln = b.start; ln <= b.end; ln++) words += hits.get(ln) || 0; if (!words) continue; } else words = b.lines.reduce((n, l) => n + (l.body.trim() ? l.body.trim().split(/\s+/).length : 0), 0);
      chosen.push({ file: rel, start: b.start, end: b.end, kind: b.kind, open: b.open, close: b.close, words, text: bodiesOf(b) });
    }
    if (hits) {
      for (const [ln, w] of hits) if (!blocks.some(b => ln >= b.start && ln <= b.end)) unmapped.push({ file: rel, line: ln, words: w });
    }
    if (chosen.length) perFile.push({ file: rel, sha256: s.sha, language: kind, bom: s.bom, words: chosen.reduce((n, b) => n + b.words, 0), blocks: chosen });
  }
  // grouped by folder, a file never split
  const byFolder = new Map();
  for (const f of perFile) {
    const folder = folderOf(f.file);
    if (!byFolder.has(folder)) byFolder.set(folder, []);
    byFolder.get(folder).push(f);
  }
  const max = opts.max || MAX_WORDS;
  const chunks = [];
  for (const [folder, list] of [...byFolder.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
    list.sort((a, b) => a.file.localeCompare(b.file));
    const groups = [];
    let cur = [], words = 0;
    for (const f of list) {
      if (cur.length && words + f.words > max) { groups.push(cur); cur = []; words = 0; }
      cur.push(f); words += f.words;
    }
    if (cur.length) groups.push(cur);
    // a small tail goes back into the chunk before it when both fit
    if (groups.length > 1) {
      const last = groups[groups.length - 1], before = groups[groups.length - 2];
      const w = (g) => g.reduce((n, f) => n + f.words, 0);
      if (w(last) < max / 4 && w(last) + w(before) <= max * 1.2) { before.push(...last); groups.pop(); }
    }
    groups.forEach((g, i) => {
      const id = slugOf(folder) + '-' + String(i + 1).padStart(2, '0');
      let n = 0;
      const blocks = [];
      for (const f of g) for (const b of f.blocks) blocks.push(Object.assign({ id: 'b' + String(++n).padStart(4, '0') }, b));
      chunks.push({
        id, folder, base: opts.base || null,
        files: g.map(f => ({ file: f.file, sha256: f.sha256, language: f.language, bom: f.bom, words: f.words, blocks: f.blocks.length })),
        blocks,
        words: g.reduce((n, f) => n + f.words, 0),
      });
    });
  }
  fs.mkdirSync(dir, { recursive: true });
  for (const c of chunks) fs.writeFileSync(path.join(dir, c.id + '.json'), JSON.stringify(c, null, 1) + '\n');
  const plan = {
    base: opts.base || null, created: new Date().toISOString(), target_words: TARGET_WORDS, max_words: max,
    totals: { words: chunks.reduce((n, c) => n + c.words, 0), files: perFile.length, blocks: chunks.reduce((n, c) => n + c.blocks.length, 0), chunks: chunks.length },
    chunks: chunks.map(c => ({ id: c.id, folder: c.folder, words: c.words, files: c.files.length, blocks: c.blocks.length, members: c.files.map(f => f.file) })),
    unmapped_italian_comment_lines: unmapped,
  };
  fs.writeFileSync(path.join(dir, 'plan.json'), JSON.stringify(plan, null, 1) + '\n');
  return { chunks, plan };
}

// ── --apply ─────────────────────────────────────────────────────────────────
// Returns [{ chunk, file, status, problems, warnings }]; writes unless dry.
function applyChunk(root, chunk, translated, opts) {
  const results = [];
  const byFile = new Map();
  for (const b of chunk.blocks) { if (!byFile.has(b.file)) byFile.set(b.file, []); byFile.get(b.file).push(b); }
  const tById = new Map(((translated && translated.blocks) || []).map(b => [b.id, b]));
  const jobs = [];
  for (const f of chunk.files) {
    const r = { chunk: chunk.id, file: f.file, status: 'refused', problems: [], warnings: [] };
    results.push(r);
    let cur;
    try { cur = readSource(root, f.file); } catch (e) { r.problems.push('cannot read the file: ' + e.message); continue; }
    jobs.push({ r, f, cur, kind: DET.kindOf(f.file), blocks: byFile.get(f.file) || [] });
  }
  const found = findElementsBatch(jobs.map(j => ({ key: j.f.file, kind: j.kind, text: j.cur.text })));
  const ready = [];
  for (const j of jobs) {
    const { r, cur, kind } = j;
    const now = buildBlocks(cur.text, found.get(j.f.file), kind);
    const byKey = new Map(now.map(b => [blockKey(b), b]));
    const edits = [];
    let already = 0;
    for (const ob of j.blocks) {
      const where = 'block ' + ob.id + ' (lines ' + ob.start + '-' + ob.end + ')';
      const tb = tById.get(ob.id);
      if (!tb) { r.problems.push(where + ': no translation in the translated chunk'); continue; }
      let bad = false;
      for (const k of ['file', 'start', 'end', 'kind', 'open', 'close']) {
        if (tb[k] !== ob[k]) { r.problems.push(where + ': the field "' + k + '" changed (' + JSON.stringify(ob[k]) + ' to ' + JSON.stringify(tb[k]) + ')'); bad = true; }
      }
      if (!Array.isArray(tb.text)) { r.problems.push(where + ': "text" is not a list of lines'); continue; }
      if (bad) continue;
      const cb = byKey.get(blockKey(ob));
      if (!cb) { r.problems.push(where + ': STALE, there is no such comment in the file now'); continue; }
      const cbBodies = bodiesOf(cb);
      // the candidate is the translation as given (trailing blanks dropped); the lines that were blank stay as they were
      const cand = tb.text.map(x => (typeof x === 'string' ? x.replace(/\s+$/, '') : x));
      const finalBodies = cand.map((x, i) => (i < ob.text.length && ob.text[i].trim() === '' ? ob.text[i] : x));
      const same = (a, b) => a.length === b.length && a.every((x, i) => x === b[i]);
      const isOriginal = same(cbBodies, ob.text);
      if (!isOriginal) {
        if (same(cbBodies, finalBodies) && compareBodies(ob.text, cand, ob, kind).length === 0) { already++; continue; }
        r.problems.push(where + ': STALE, the text in the file is no longer the original of the chunk');
        continue;
      }
      const p = compareBodies(ob.text, cand, ob, kind);
      if (p.length) { for (const x of p) r.problems.push(where + ': ' + x); continue; }
      cb.lines.forEach((l, i) => { if (finalBodies[i] !== l.body) edits.push({ bs: l.bs, be: l.be, to: finalBodies[i] }); });
    }
    if (r.problems.length) continue;
    if (edits.length === 0) { r.status = already ? 'already applied' : 'unchanged'; continue; }
    edits.sort((a, b) => b.bs - a.bs);
    for (let i = 1; i < edits.length; i++) if (edits[i].be > edits[i - 1].bs) { r.problems.push('two edits overlap'); break; }
    if (r.problems.length) continue;
    let text = cur.text;
    for (const e of edits) text = text.slice(0, e.bs) + e.to + text.slice(e.be);
    ready.push({ j, text, edits: edits.length, already });
  }
  // what is written must be the same file outside its comments
  const again = findElementsBatch(ready.map(x => ({ key: x.j.f.file, kind: x.j.kind, text: x.text })));
  const streams = [];
  for (const x of ready) {
    const { j } = x;
    const r = j.r;
    const before = buildBlocks(j.cur.text, found.get(j.f.file), j.kind);
    let after;
    try { after = buildBlocks(x.text, again.get(j.f.file), j.kind); } catch (e) { r.problems.push('the written text does not parse as comments: ' + e.message); continue; }
    if (after.length !== before.length || after.some((b, i) => blockKey(b) !== blockKey(before[i]) || b.lines.length !== before[i].lines.length))
      r.problems.push('once written, the comments are not the same blocks as before');
    else if (masked(x.text, after) !== masked(j.cur.text, before)) r.problems.push('once written, the text outside the comments differs');
    if (r.problems.length) continue;
    if (PARSED[j.kind]) streams.push(x);
  }
  if (streams.length) {
    const items = [];
    streams.forEach((x, i) => {
      items.push({ key: 'b' + i, lang: PARSED[x.j.kind], text: x.j.cur.text });
      items.push({ key: 'a' + i, lang: PARSED[x.j.kind], text: x.text });
    });
    const toks = TS.tokenizeAll(items);
    streams.forEach((x, i) => {
      const c = TS.compareStreams(toks.get('b' + i), toks.get('a' + i), 'identical', PARSED[x.j.kind], new Map());
      if (c.diffs.length) x.j.r.problems.push('the token stream changed: ' + c.diffs[0].why);
    });
  }
  for (const x of ready) {
    const { j } = x;
    if (j.r.problems.length) continue;
    const left = italianLines(root, j.f.file, x.text);
    const lines = [...left.keys()].filter(ln => j.blocks.some(b => ln >= b.start && ln <= b.end));
    if (lines.length) j.r.warnings.push('Italian still found in comments on lines ' + lines.slice(0, 12).join(', ') + (lines.length > 12 ? ' (+' + (lines.length - 12) + ')' : ''));
    j.r.status = opts.dry ? 'would apply (' + x.edits + ' lines)' : 'applied (' + x.edits + ' lines)';
    if (!opts.dry) writeSource(root, j.f.file, x.text, j.cur.bom);
  }
  return results;
}

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

function runApply(root, dir, opts) {
  const list = loadChunks(dir, opts.only);
  if (list.length === 0) cannot('no chunk in ' + dir);
  let refused = 0, applied = 0, warned = 0, missing = 0;
  for (const { id, chunk, translated } of list) {
    if (!translated) { console.log(id + ': no translation yet (' + id + '.en.json)'); missing++; continue; }
    if (translated.id !== chunk.id) { console.log(id + ': REFUSED, the translated chunk is ' + translated.id); refused++; continue; }
    console.log(id + ': ' + chunk.files.length + ' files, ' + chunk.blocks.length + ' blocks');
    for (const r of applyChunk(root, chunk, translated, opts)) {
      if (r.problems.length) {
        refused++;
        console.log('  REFUSED ' + r.file + ': ' + r.problems.length + ' problems');
        for (const p of r.problems.slice(0, 8)) console.log('      ' + p);
        if (r.problems.length > 8) console.log('      ... ' + (r.problems.length - 8) + ' more');
      } else {
        if (/applied|would/.test(r.status) && !/already/.test(r.status)) applied++;
        console.log('  ' + r.status + ' ' + r.file);
      }
      for (const w of r.warnings) { warned++; console.log('      ' + w); }
    }
  }
  console.log('');
  console.log('apply' + (opts.dry ? ' (dry)' : '') + ': ' + applied + ' files ' + (opts.dry ? 'would be written' : 'written') + ', ' + refused + ' refused, ' +
    missing + ' chunks without translation, ' + warned + ' files with Italian left in the blocks written.');
  return refused ? 1 : warned ? 3 : 0;
}

// ── --check ─────────────────────────────────────────────────────────────────
function changedFiles(root, rev) {
  const out = git(root, ['diff', '--name-status', '-M', '-z', rev, '--']).toString('utf8').split('\0');
  const list = [];
  for (let i = 0; i < out.length;) {
    const s = out[i];
    if (!s) { i++; continue; }
    if (s[0] === 'R' || s[0] === 'C') { list.push({ now: out[i + 2], was: out[i + 1] }); i += 3; }
    else { if (s[0] !== 'D') list.push({ now: out[i + 1], was: out[i + 1], added: s[0] === 'A' }); i += 2; }
  }
  return list.filter(x => !DET.isBinaryByExtension(x.now) && KINDS.has(DET.kindOf(x.now)));
}

function showAt(root, rev, rel) {
  try { return git(root, ['show', rev + ':' + rel]); } catch (e) { return null; }
}

function checkAll(root, rev, opts) {
  const list = changedFiles(root, rev);
  const rows = [];
  const items = [];
  const data = [];
  for (const x of list) {
    const blob = showAt(root, rev, x.was);
    if (blob === null) { rows.push({ file: x.now, note: 'new since ' + rev, problems: [], before: 0, after: null }); continue; }
    let cur;
    try { cur = readSource(root, x.now); } catch (e) { rows.push({ file: x.now, note: 'cannot read', problems: [e.message] }); continue; }
    const prev = decodeSource(blob, x.was);
    const kind = DET.kindOf(x.now);
    items.push({ key: 'b:' + x.now, kind, text: prev.text }, { key: 'a:' + x.now, kind, text: cur.text });
    data.push({ x, kind, prev, cur });
  }
  const found = findElementsBatch(items);
  const streams = [];
  for (const d of data) {
    const row = { file: d.x.now, problems: [], before: 0, after: 0 };
    rows.push(row);
    const bb = buildBlocks(d.prev.text, found.get('b:' + d.x.now), d.kind);
    const ab = buildBlocks(d.cur.text, found.get('a:' + d.x.now), d.kind);
    if (bb.length !== ab.length || ab.some((b, i) => blockKey(b) !== blockKey(bb[i]) || b.lines.length !== bb[i].lines.length))
      row.problems.push('the comments are not the same blocks as at ' + rev + ' (' + bb.length + ' before, ' + ab.length + ' now)');
    else {
      // the object store holds LF and a Windows checkout CRLF: line ends are not compared here
      const eol = (s) => s.replace(/\r\n/g, '\n');
      if (eol(masked(d.cur.text, ab)) !== eol(masked(d.prev.text, bb))) row.problems.push('the text outside the comments differs from ' + rev);
      ab.forEach((b, i) => { for (const p of compareBodies(bodiesOf(bb[i]), bodiesOf(b), b, d.kind)) row.problems.push('block at lines ' + b.start + '-' + b.end + ': ' + p); });
    }
    if (PARSED[d.kind]) streams.push({ d, row });
    row.before = [...italianLines(root, d.x.was, d.prev.text).values()].reduce((n, v) => n + v, 0);
    row.after = [...italianLines(root, d.x.now, d.cur.text).values()].reduce((n, v) => n + v, 0);
  }
  if (streams.length) {
    const its = [];
    streams.forEach((s, i) => {
      its.push({ key: 'b' + i, lang: PARSED[s.d.kind], text: s.d.prev.text }, { key: 'a' + i, lang: PARSED[s.d.kind], text: s.d.cur.text });
    });
    const toks = TS.tokenizeAll(its);
    streams.forEach((s, i) => {
      const c = TS.compareStreams(toks.get('b' + i), toks.get('a' + i), 'identical', PARSED[s.d.kind], new Map());
      if (c.diffs.length) s.row.problems.push('the token stream changed: ' + c.diffs[0].why);
    });
  }
  return rows;
}

function runCheck(root, rev, opts) {
  const rows = checkAll(root, rev, opts);
  let bad = 0, before = 0, after = 0, left = 0;
  console.log('window 4 check against ' + rev + ':');
  for (const r of rows) {
    if (r.problems.length) {
      bad++;
      console.log('  DIFFERENT ' + r.file + ': ' + r.problems.length + ' problems');
      for (const p of r.problems.slice(0, 6)) console.log('      ' + p);
      if (r.problems.length > 6) console.log('      ... ' + (r.problems.length - 6) + ' more');
    }
    if (r.after !== null && r.after !== undefined) {
      before += r.before; after += r.after;
      if (r.after > 0) { left++; if (opts.verbose || r.before !== r.after) console.log('  ' + r.file + ': Italian comment words ' + r.before + ' -> ' + r.after); }
    }
    if (r.note) console.log('  ' + r.file + ': ' + r.note);
  }
  console.log('');
  console.log('check: ' + rows.length + ' files touched, ' + bad + ' with a difference outside the comments or a broken invariant; ' +
    'Italian comment words in them ' + before + ' -> ' + after + ', ' + left + ' files still Italian.');
  if (rows.length === 0) { console.log('check: NOTHING TO COMPARE. Name the revision before the change (--rev).'); return 1; }
  if (bad) return 1;
  if (opts.zero && left) return 1;
  return 0;
}

// ── --self-test ─────────────────────────────────────────────────────────────
// English fixtures: the tool does not care which language a comment is in
// (the self-test asks for every comment, not only the Italian ones), and this
// file is judged by the language gate.
function fixtures() {
  const crlf = (a) => a.join('\r\n') + '\r\n';
  const lf = (a) => a.join('\n') + '\n';
  return {
    'a.cs': { bom: true, text: crlf([
      '// Reads the sheet and keeps `Sheet.Read` in sync, see W3.1-bis.6 on 5/10/2026.',
      '// Details are in notes.md and at https://example.com/docs/a.',
      'namespace Demo',
      '{',
      '    /// <summary>',
      '    /// Holds <see cref="Other"/> for 3 items, as the \u00abagreed rule\u00bb says.',
      '    /// </summary>',
      '    /// <param name="count">how many</param>',
      '    class Holder',
      '    {',
      '        string s = "a // not a comment"; /* one line block */',
      '        string t = @"verbatim // not a comment',
      'second line";',
      '        int x = 1; // trailing words here',
      '',
      '        /*',
      '         * A starred block',
      '         * of three lines.',
      '         */',
      '        void M() { }',
      '    }',
      '}']) },
    'b.js': { text: lf([
      '#!/usr/bin/env node',
      '// ZERO-E-ZERO: script header line one.',
      '// .NET Framework script header line two.',
      "'use strict';",
      "const url = 'http://example.com/x'; // trailing note",
      'const re = /a\\/\\/b/g;',
      'const t = `template // not a comment ${1 /* inside a hole */}`;',
      '/* block',
      '   comment */']) },
    'c.ps1': { bom: true, text: crlf([
      '#requires -Version 5',
      '<#',
      '.SYNOPSIS',
      'Does a thing.',
      '.PARAMETER Name',
      'The name to use.',
      '#>',
      'param([string]$Name)',
      '# A comment line',
      '$x = "text # not a comment"  # trailing words',
      '$h = @"',
      '# inside a here-string',
      '"@']) },
    'd.yml': { text: lf([
      '# Workflow notes line one.',
      '# Workflow notes line two.',
      'name: tests # trailing words',
      'on:',
      '  push:',
      '    branches: [main] # more words',
      'jobs:',
      '  build:',
      '    steps:',
      '      - run: |',
      '          # not a comment, a script line',
      '          echo hi',
      '      - name: "quoted # not a comment"',
      '      - run: "a quoted scalar',
      '          # over two lines, not a comment"',
      "      - run: echo 'a quote inside a plain scalar # IS a comment'"]) },
    'e.csproj': { text: crlf([
      '<Project>',
      '  <!-- First note about the build. -->',
      '  <PropertyGroup>',
      '    <!-- Multi',
      '         line note. -->',
      '    <Title>a # b</Title>',
      '  </PropertyGroup>',
      '</Project>']) },
    '.gitignore': { text: lf(['# Ignore build output.', 'bin/', 'obj/ # not a comment in an ignore file']) },
    'hook': { text: lf([
      '#!/bin/sh',
      '# - Hook header line.',
      '# - Second rule of the hook.',
      'cat <<EOF',
      '# not a comment inside a here-document',
      'EOF',
      'echo "a # b" # trailing words']) },
  };
}

// the expected blocks of the fixtures: [file, start, end, kind, open]
const EXPECTED_BLOCKS = [
  ['.gitignore', 1, 1, 'line', '#'],
  ['a.cs', 1, 2, 'line', '//'], ['a.cs', 5, 8, 'line', '///'], ['a.cs', 11, 11, 'block', '/*'], ['a.cs', 14, 14, 'line', '//'], ['a.cs', 16, 19, 'block', '/*'],
  ['b.js', 2, 3, 'line', '//'], ['b.js', 5, 5, 'line', '//'], ['b.js', 7, 7, 'block', '/*'], ['b.js', 8, 9, 'block', '/*'],
  ['c.ps1', 2, 7, 'block', '<#'], ['c.ps1', 9, 9, 'line', '#'], ['c.ps1', 10, 10, 'line', '#'],
  ['d.yml', 1, 2, 'line', '#'], ['d.yml', 3, 3, 'line', '#'], ['d.yml', 6, 6, 'line', '#'], ['d.yml', 16, 16, 'line', '#'],
  ['e.csproj', 2, 2, 'block', '<!--'], ['e.csproj', 4, 5, 'block', '<!--'],
  ['hook', 2, 3, 'line', '#'], ['hook', 7, 7, 'line', '#'],
];

function makeRepo(parent) {
  const dir = fs.mkdtempSync(path.join(parent || os.tmpdir(), 'window4-selftest-'));
  const g = (...a) => cp.execFileSync('git', ['-C', dir, '-c', 'user.name=t', '-c', 'user.email=t@t', '-c', 'core.autocrlf=false'].concat(a), { stdio: 'pipe' });
  g('init', '-q');
  for (const [name, f] of Object.entries(fixtures())) {
    const b = Buffer.from(f.text, 'utf8');
    fs.writeFileSync(path.join(dir, name), f.bom ? Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), b]) : b);
  }
  g('add', '-A');
  g('commit', '-q', '-m', 'fixtures');
  return { dir, g };
}

// a "translation" that only changes plain words: it keeps every invariant
const REWORD = (s) => s.replace(/\b(the|of|and|with|for|line|lines|comment|first|holds|items|block|note|notes|about|build|words|details|reads|script|header|trailing|more|thing|output|hook|workflow)\b/gi, w => w.toUpperCase());

function selfTest() {
  let ok = 0, total = 0;
  const bad = [];
  const say = (good, what) => { total++; if (good) ok++; else bad.push(what); console.log((good ? '  ok  ' : '  XX  ') + what); };
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'window4-chunks-'));
  const { dir: root, g } = makeRepo();
  try {
    // 1. the comments found are the ones a parser finds
    const { chunks } = extractAll(root, tmp, { everything: true, base: 'HEAD', max: 1000000 });
    const got = [];
    for (const c of chunks) for (const b of c.blocks) got.push([b.file, b.start, b.end, b.kind, b.open]);
    got.sort((a, b) => a[0].localeCompare(b[0]) || a[1] - b[1]);
    const want = EXPECTED_BLOCKS.slice().sort((a, b) => a[0].localeCompare(b[0]) || a[1] - b[1]);
    say(JSON.stringify(got) === JSON.stringify(want), 'extract: the blocks are those of the parsers (' + got.length + ' of ' + want.length + ', nothing from strings, here-strings, regular expressions, #!, #requires, block scalars)');
    const chunk = chunks[0];
    const one = (mutate) => {
      const t = JSON.parse(JSON.stringify({ id: chunk.id, blocks: chunk.blocks }));
      t.blocks.forEach(b => { b.text = b.text.map(REWORD); });
      if (mutate) mutate(t);
      return t;
    };
    // only: the blocks of one file (a run costs a parser start per language)
    const run = (t, opts, only) => {
      const c = only ? Object.assign({}, chunk, { blocks: chunk.blocks.filter(b => b.file === only), files: chunk.files.filter(f => f.file === only) }) : chunk;
      return applyChunk(root, c, t, Object.assign({ dry: true }, opts || {}));
    };
    const refusedWith = (res, fileName, part) => res.some(r => r.file === fileName && r.problems.some(p => p.includes(part)));
    const blockOf = (t, file, start) => t.blocks.find(b => b.file === file && b.start === start);
    // 2. the good case, then the refusals
    const green = run(one());
    say(green.every(r => r.problems.length === 0 && /would apply/.test(r.status)), 'apply: a translation that only rewords is accepted in every language (' + green.map(r => r.file + ' ' + r.status).join('; ') + ')');
    const cases = [
      ['a number changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 5); b.text[1] = b.text[1].replace(/3 items/i, '4 items'); }, 'numbers'],
      ['a date changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 1); b.text[0] = b.text[0].replace('5/10/2026', '5/11/2026'); }, 'numbers'],
      ['a reference lost', 'a.cs', t => { const b = blockOf(t, 'a.cs', 1); b.text[0] = b.text[0].replace('W3.1-bis.6', 'W3.1'); }, 'references'],
      ['a reference reworded', 'a.cs', t => { const b = blockOf(t, 'a.cs', 1); b.text[0] = b.text[0].replace('W3.1-bis.6', 'W3.1-bis.7'); }, 'references'],
      ['a name between backticks changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 1); b.text[0] = b.text[0].replace('`Sheet.Read`', '`Sheet.Write`'); }, 'backticks'],
      ['a file name changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 1); b.text[1] = b.text[1].replace(/notes\.md/i, 'note.md'); }, 'files'],
      ['a URL changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 1); b.text[1] = b.text[1].replace('docs/a', 'docs/b'); }, 'urls'],
      ['a doc tag attribute changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 5); b.text[1] = b.text[1].replace('cref="Other"', 'cref="Another"'); }, 'tags'],
      ['a param name changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 5); b.text[3] = b.text[3].replace('name="count"', 'name="total"'); }, 'tags'],
      ['a doc tag lost', 'a.cs', t => { const b = blockOf(t, 'a.cs', 5); b.text[0] = b.text[0].replace('<summary>', ''); }, 'structure'],
      ['a code-like name changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 1); b.text[0] = b.text[0].replace('keeps `Sheet.Read` in sync', 'keeps `Sheet.Read` in syncMode'); }, 'names'],
      ['a guillemet lost', 'a.cs', t => { const b = blockOf(t, 'a.cs', 5); b.text[1] = b.text[1].replace('\u00bb', ''); }, 'guillemets'],
      ['the delimiter changed', 'a.cs', t => { blockOf(t, 'a.cs', 1).open = '///'; }, 'field "open"'],
      ['the kind changed', 'a.cs', t => { blockOf(t, 'a.cs', 14).kind = 'block'; }, 'field "kind"'],
      ['the position changed', 'a.cs', t => { blockOf(t, 'a.cs', 14).start = 15; }, 'field "start"'],
      ['the number of lines changed (fewer)', 'a.cs', t => { blockOf(t, 'a.cs', 1).text.pop(); }, 'number of lines'],
      ['the number of lines changed (more)', 'b.js', t => { blockOf(t, 'b.js', 2).text.push('one more'); }, 'number of lines'],
      ['the comment closed early in a block', 'a.cs', t => { const b = blockOf(t, 'a.cs', 16); b.text[1] = b.text[1] + ' */ int y = 2; /*'; }, 'closing delimiter'],
      ['the comment closed early in a PowerShell block', 'c.ps1', t => { const b = blockOf(t, 'c.ps1', 2); b.text[4] = 'The name to use. #> $z = 1 <#'; }, 'closing delimiter'],
      ['a double hyphen in an XML comment', 'e.csproj', t => { blockOf(t, 'e.csproj', 2).text[0] = ' First -- note about the build. '; }, 'double hyphen'],
      ['a hyphen right before --> in an XML comment', 'e.csproj', t => { blockOf(t, 'e.csproj', 2).text[0] = ' First note about the build-'; }, 'hyphen before -->'],
      ['a stray < in a documentation comment', 'a.cs', t => { const b = blockOf(t, 'a.cs', 5); b.text[1] = b.text[1].replace(/for 3 items/i, 'for < 3 items'); }, 'documentation comment'],
      ['a stray & in a documentation comment', 'a.cs', t => { const b = blockOf(t, 'a.cs', 5); b.text[1] = b.text[1].replace(/for 3 items/i, 'for 3 items & more'); }, 'documentation comment'],
      ['a line end inside a line', 'b.js', t => { blockOf(t, 'b.js', 5).text[0] = ' trailing\n note'; }, 'line end'],
      ['an empty line filled', 'a.cs', t => { const b = blockOf(t, 'a.cs', 16); b.text[0] = ' filled'; }, 'was empty'],
      ['the indentation changed', 'a.cs', t => { const b = blockOf(t, 'a.cs', 16); b.text[1] = '   ' + b.text[1].trimStart(); }, 'indentation'],
      ['a marker read by a check lost', 'b.js', t => { const b = blockOf(t, 'b.js', 2); b.text[0] = b.text[0].replace('ZERO-E-ZERO:', 'ZERO-AND-ZERO:'); }, 'markers'],
      ['a help keyword changed', 'c.ps1', t => { const b = blockOf(t, 'c.ps1', 2); b.text[1] = '.DESCRIPTION'; }, 'helpKeywords'],
      ['a block of the chunk left untranslated', 'd.yml', t => { t.blocks = t.blocks.filter(b => !(b.file === 'd.yml' && b.start === 3)); }, 'no translation'],
    ];
    for (const [what, file, mutate, part] of cases) {
      const res = run(one(mutate), null, file);
      const red = refusedWith(res, file, part);
      say(red, 'refused, seen red: ' + what + ' (' + file + ')' + (red ? '' : ' - got ' + JSON.stringify(res.filter(r => r.file === file).map(r => r.problems.slice(0, 2)))));
    }
    // a bullet marker, in a fixture of its own
    {
      const res = run(one(t => { const b = blockOf(t, 'hook', 2); b.text[0] = b.text[0].replace(' - ', ' '); }), null, 'hook');
      say(refusedWith(res, 'hook', 'bullet marker'), 'refused, seen red: a bullet marker lost (hook)');
    }
    // a placeholder in angle brackets is prose, an ordinal or a "64-bit" is a number with its word
    {
      const orig = [' format "<hash lower>  <file name>" and the 14th day, a 64-bit step'];
      const now = [' format "<hash upper>  <name of file>" and the 14 day, a 64 bit step'];
      say(compareBodies(orig, now, { close: '' }).length === 0, 'a placeholder in angle brackets, an ordinal and a "64-bit" are not references: accepted when reworded');
      const lost = [' format "<hash upper>  <name of file>" and the 15 day, a 64 bit step'];
      say(compareBodies(orig, lost, { close: '' }).some(p => p.includes('numbers')), 'refused, seen red: the number of an ordinal changed');
    }
    // the markers the contracts read in the comments, and the value inside a quote
    {
      const orig = [' OBSOLETE-MEASURED: probe-x, UNDOCUMENTED: then MEASURED: probe-y; Guide: ISketch~Add.html says «the value 4», and 5'];
      const ok = [' OBSOLETE-MEASURED: probe-x, UNDOCUMENTED: then MEASURED: probe-y; Guide: ISketch~Add.html SAYS «THE value 4», AND 5'];
      say(compareBodies(orig, ok, { close: '' }).length === 0, 'markers and quotes reworded around them: accepted');
      const red = (what, now, part) => say(compareBodies(orig, [now], { close: '' }).some(p => p.includes(part)), 'refused, seen red: ' + what);
      red('the case of an OBSOLETE-MEASURED: marker changed', orig[0].replace('OBSOLETE-MEASURED: probe-x', 'OBSOLETE-MEASURED: probe-z'), 'markers');
      red('a MEASURED: marker translated', orig[0].replace('then MEASURED:', 'then measured:'), 'markers');
      red('an UNDOCUMENTED: marker translated', orig[0].replace('UNDOCUMENTED:', 'undocumented:'), 'markers');
      red('a number moved out of the quote', orig[0].replace('the value 4», and 5', 'the value 5», and 4'), 'quoteNumbers');
    }
    // the text in the file is no longer the original
    {
      const file = path.join(root, 'a.cs');
      const keep = fs.readFileSync(file);
      fs.writeFileSync(file, Buffer.from(keep.toString('utf8').replace('Reads the sheet', 'Reads a sheet'), 'utf8'));
      const res = run(one(), null, 'a.cs');
      say(refusedWith(res, 'a.cs', 'STALE'), 'refused, seen red: the original no longer matches (stale chunk)');
      fs.writeFileSync(file, keep);
    }
    // the layers behind the block rules, each on its own: the file outside the
    // comments, and the token stream
    {
      const t = one();
      const c2 = JSON.parse(JSON.stringify(chunk));
      c2.blocks = c2.blocks.filter(b => b.file === 'b.js');
      c2.files = c2.files.filter(f => f.file === 'b.js');
      const tb = JSON.parse(JSON.stringify(t));
      tb.blocks = tb.blocks.filter(b => b.file === 'b.js');
      const res0 = applyChunk(root, c2, tb, { dry: true });
      say(res0.every(r => r.problems.length === 0), 'apply: JavaScript with a hashbang, a regular expression and a template hole is accepted');
      // a line comment may hold anything but a line end, so to see the layers red
      // the block rules are asked for a body that passes them and still changes
      // the file: a closing delimiter that the rules do not know about
      const text = fs.readFileSync(path.join(root, 'b.js'), 'utf8');
      const els = findElementsBatch([{ key: 'k', kind: 'js', text }]).get('k');
      const bl = buildBlocks(text, els, 'js');
      const mutated = text.replace('const re = /a\\/\\/b/g;', 'const re = /a\\/\\/c/g;');
      const mb = buildBlocks(mutated, findElementsBatch([{ key: 'k', kind: 'js', text: mutated }]).get('k'), 'js');
      say(masked(mutated, mb) !== masked(text, bl), 'the layer that compares the text outside the comments sees a regular expression changed');
      const toks = TS.tokenizeAll([{ key: 'b', lang: 'javascript', text }, { key: 'a', lang: 'javascript', text: mutated }]);
      const c = TS.compareStreams(toks.get('b'), toks.get('a'), 'identical', 'javascript', new Map());
      say(c.diffs.length > 0, 'the token stream layer sees the same change red');
    }
    // 3. the good case written for real, then --check
    const t = one();
    fs.writeFileSync(path.join(tmp, chunk.id + '.en.json'), JSON.stringify(t));
    const before = {};
    for (const name of Object.keys(fixtures())) before[name] = fs.readFileSync(path.join(root, name));
    const written = run(t, { dry: false });
    say(written.every(r => r.problems.length === 0 && /applied/.test(r.status)), 'apply: written for real (' + written.map(r => r.status).join(', ') + ')');
    const keeps = Object.keys(fixtures()).every(name => {
      const after = fs.readFileSync(path.join(root, name));
      const had = before[name];
      return after.slice(0, 3).equals(had.slice(0, 3)) && (after.toString('latin1').match(/\r\n/g) || []).length === (had.toString('latin1').match(/\r\n/g) || []).length && !after.equals(had);
    });
    say(keeps, 'the byte order mark and the line ends of every file are kept, and every file changed');
    const again = run(t, { dry: false });
    say(again.every(r => r.problems.length === 0 && r.status === 'already applied'), 'apply: a second time says "already applied" and changes nothing');
    const check = checkAll(root, 'HEAD', {});
    say(check.length === Object.keys(fixtures()).length && check.every(r => r.problems.length === 0), 'check: every touched file is the same outside its comments, the blocks keep their invariants (' + check.length + ' files)');
    // --check red on mutated copies
    const csFile = path.join(root, 'a.cs');
    const good = fs.readFileSync(csFile);
    const mutate = (from, to) => { const s = good.toString('utf8'); if (!s.includes(from)) throw new Error('self-test mutation does not apply: ' + from); fs.writeFileSync(csFile, Buffer.from(s.replace(from, to), 'utf8')); };
    mutate('int x = 1;', 'int x = 2;');
    say(checkAll(root, 'HEAD', {}).some(r => r.file === 'a.cs' && r.problems.some(p => p.includes('token stream'))), 'check: refused, seen red: a code token changed under the comments (a.cs)');
    fs.writeFileSync(csFile, good);
    mutate('FOR 3 ITEMS', 'FOR 4 ITEMS');
    say(checkAll(root, 'HEAD', {}).some(r => r.file === 'a.cs' && r.problems.some(p => p.includes('numbers'))), 'check: refused, seen red: a number changed in a comment');
    fs.writeFileSync(csFile, good);
    const yml = path.join(root, 'd.yml');
    const ygood = fs.readFileSync(yml);
    fs.writeFileSync(yml, Buffer.from(ygood.toString('utf8').replace('echo hi', 'echo bye'), 'utf8'));
    say(checkAll(root, 'HEAD', {}).some(r => r.file === 'd.yml' && r.problems.some(p => p.includes('outside the comments'))), 'check: refused, seen red: text outside the comments changed in a file without a parser (d.yml)');
    fs.writeFileSync(yml, ygood);
    say(checkAll(root, 'HEAD', {}).every(r => r.problems.length === 0), 'check: green again once the mutated copies are put back');
    // the Italian count: the detector sees what is Italian, per file
    const probe = path.join(root, 'it.js');
    // an Italian sentence, written with marks inside its words so that this file carries none of its own
    const sentence = 'Qu#esta ri#ga no#n e#\' sta#ta an#cora tradot#ta, e i#l mot#ivo rest#a qu#i.'.replace(/#/g, '');
    fs.writeFileSync(probe, '// ' + sentence + '\r\nconst a = 1;\r\n');
    const lines = italianLines(root, 'it.js', fs.readFileSync(probe, 'utf8'));
    say(lines.size === 1, 'the detector sees the Italian comment of a probe file (it is what --extract selects)');
    fs.rmSync(probe);
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
    fs.rmSync(root, { recursive: true, force: true });
  }
  console.log('');
  console.log(ok + ' of ' + total + ' cases as expected.');
  if (bad.length) console.log('NOT AS EXPECTED: ' + bad.join(' | '));
  return bad.length === 0 ? 0 : 1;
}

// ── main ────────────────────────────────────────────────────────────────────
function parseArgs(argv) {
  const a = { root: ROOT_DEFAULT, extract: null, apply: null, check: false, selfTest: false, dry: false, rev: 'HEAD', only: null, zero: false, verbose: false, max: null };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    const next = () => { if (i + 1 >= argv.length) cannot(x + ' needs a value'); return argv[++i]; };
    if (x === '--extract') a.extract = path.resolve(next());
    else if (x === '--apply') a.apply = path.resolve(next());
    else if (x === '--check') a.check = true;
    else if (x === '--self-test') a.selfTest = true;
    else if (x === '--dry') a.dry = true;
    else if (x === '--zero') a.zero = true;
    else if (x === '--verbose') a.verbose = true;
    else if (x === '--rev') a.rev = next();
    else if (x === '--root') a.root = path.resolve(next());
    else if (x === '--chunk') a.only = next().split(',');
    else if (x === '--max') a.max = parseInt(next(), 10);
    else cannot('unknown argument ' + x);
  }
  if (![a.extract, a.apply, a.check, a.selfTest].some(Boolean)) cannot('one of --extract <dir>, --apply <dir>, --check, --self-test');
  return a;
}

function main() {
  const a = parseArgs(process.argv.slice(2));
  if (a.selfTest) return selfTest();
  if (a.extract) {
    const base = git(a.root, ['rev-parse', 'HEAD']).toString('utf8').trim();
    const dirty = git(a.root, ['status', '--porcelain', '--untracked-files=no']).toString('utf8').trim();
    if (dirty) console.log('window 4: the working tree has uncommitted changes; the chunks are cut from the files on disk, the base recorded is ' + base);
    const { plan } = extractAll(a.root, a.extract, { base, max: a.max || MAX_WORDS });
    console.log('extracted ' + plan.totals.chunks + ' chunks, ' + plan.totals.words + ' Italian words in ' + plan.totals.files + ' files (' + plan.totals.blocks + ' blocks) into ' + a.extract);
    for (const c of plan.chunks) console.log('  ' + c.id + '  ' + c.folder + '  ' + c.words + ' words  ' + c.files + ' files  ' + c.blocks + ' blocks');
    if (plan.unmapped_italian_comment_lines.length) {
      console.log('UNMAPPED: ' + plan.unmapped_italian_comment_lines.length + ' lines the gate calls an Italian comment that lie in no comment found:');
      for (const u of plan.unmapped_italian_comment_lines.slice(0, 20)) console.log('  ' + u.file + ':' + u.line + ' (' + u.words + ' words)');
    }
    return 0;
  }
  if (a.apply) return runApply(a.root, a.apply, { dry: a.dry, only: a.only });
  return runCheck(a.root, a.rev, { zero: a.zero, verbose: a.verbose });
}

if (require.main === module) {
  try {
    process.exitCode = main();
  } catch (e) {
    if (e instanceof Stop || e instanceof TS.GateError) { console.log('window-4-comments: ' + e.message); process.exitCode = 2; }
    else { console.log('window-4-comments: ' + (e && e.stack ? e.stack : e)); process.exitCode = 2; }
  }
}

module.exports = { extractAll, applyChunk, checkAll, buildBlocks, findElementsBatch, compareBodies, factsOf, masked };
