#!/usr/bin/env node
// =============================================================================
//  detect-italian.js - the language gate (entry W3.1-bis.1)
// =============================================================================
//
//  WHAT IT MEASURES
//
//  Every file of `git ls-files` (or of a commit, --rev), minus binaries
//  decided BY EXTENSION ONLY (a closed list, below). Never `git grep -I` and
//  never git's own binary guess: `Controlli/domande-senza-risposta.js` carried
//  a real NUL byte, git called it binary, and `controlla-pubblicabilita.ps1`
//  (which uses `git grep -I`) has been skipping it without saying so. A file
//  that git thinks is binary but has a text extension is READ here, and the
//  report lists every file read.
//
//  NINE CATEGORIES, NEVER SUMMED
//
//    comments     prose inside comments (C#, JS, PowerShell, shell, YAML, XML)
//    strings      prose inside string literals (also inside the holes of an
//                 interpolated string, and the words of `echo` and of a
//                 here-document in a shell script)
//    markdown     prose in documents (.md, .txt, LICENSE, NOTICE...)
//    json_values  prose in JSON string values
//    identifiers  code identifiers split on camelCase / snake_case, checked
//                 against the lexicon (also names quoted inside prose)
//    json_keys    JSON keys (a key with spaces is one key, kept whole)
//    protocol     name-like values: string literals and JSON values without
//                 spaces once selectors (@kind(...)) and placeholders in
//                 braces are set aside; a name joined by hyphens or dots
//                 is registered whole as well as word by word
//    paths        every segment of every tracked path
//    transition   Italian that is allowed only until the tag: whatever an
//                 exception of class "transition" moves here (aliases, map
//                 rows, bilingual branches). It must be 0 at a v* tag
//                 (--zero transition, run by the CI step on a tag).
//
//  The first four and `transition` count ITALIAN WORDS PER FILE (the signals
//  of the prose check below): they may not grow in any file, so Italian
//  appended to a line that is already Italian is growth too. The other four
//  are a REPOSITORY-WIDE SET of whole names: no Italian name may appear that
//  is not already there. A new case may still use the Italian case keys until
//  they are renamed, but a new identifier glued together from Italian words
//  that already exist separately is a new element, and is blocked. A name
//  that was a tracked path segment is not new when it shows up in text after
//  the file is renamed.
//
//  THE PROSE CHECK, in this order:
//    1. the spans matched by an exception are blanked;
//    2. `code spans`, URLs, file paths and name-like words (snake_case,
//       camelCase) are taken out and sent to the identifier check;
//    3. the words are scored. STRONG signals: an Italian function word, an
//       Italian content word (the lexicon, plus workshop words the repository
//       does not use yet), a word with an Italian-only ending (-zione,
//       -mento...), the ASCII accent trick of this repository (a
//       vowel and an apostrophe, typographic or not, before a space or
//       punctuation), an elided article, a word-final accented vowel. WEAK
//       signals: Italian words that English also has (weak_words in
//       lexicon.json). A line is Italian with one
//       strong signal or two weak ones; its score is the number of signals.
//
//  EXCEPTIONS (exceptions.json) are patterns, never files. A json_path is
//  allowed only for the class detector-lexicon on a file of Controlli/lingua/;
//  a pattern may not match an Italian function word on its own, nor take away
//  a third of the Italian of a sentinel sentence; one without "file" may not
//  match an Italian content word on its own; and an exception that suppresses
//  too much on the tree (exception_limits in lexicon.json) is refused.
//
//  USAGE
//    node Controlli/lingua/detect-italian.js              compare with baseline.json
//    node Controlli/lingua/detect-italian.js --json       full report as JSON
//    node Controlli/lingua/detect-italian.js --file <p>   hits of one file
//    node Controlli/lingua/detect-italian.js --write-baseline
//    node Controlli/lingua/detect-italian.js --baseline <file>   compare with another baseline
//    node Controlli/lingua/detect-italian.js --baseline git:<rev>   ... the one committed there
//                                            (git:A+B merges two: the parents of a merge)
//    node Controlli/lingua/detect-italian.js --rev <rev>  read the tree of a commit, not the disk
//    node Controlli/lingua/detect-italian.js --config-rev <rev>   lexicon and exceptions of that commit
//    node Controlli/lingua/detect-italian.js --renames <file>     { "new/path": "old/path" }
//    node Controlli/lingua/detect-italian.js --zero <category>    that category must be 0
//    node Controlli/lingua/detect-italian.js --tighten    rewrite the baseline if the tree went down
//    node Controlli/lingua/detect-italian.js --compare-baselines <old> <new>   (files or git:<rev>)
//    node Controlli/lingua/detect-italian.js --report <file>   also write the comparison as JSON
//    node Controlli/lingua/detect-italian.js --root <dir> run on another checkout
//
//  The CI step (.github/passi/language-gate.ps1) judges every commit of a
//  range against its parent with these options.
//
//  EXIT CODES
//    0  nothing grew
//    1  a category grew (the offending lines are printed), or --zero failed
//    2  the gate cannot run (bad exceptions file, unreadable tracked file...)
//    3  --tighten only: the tree went down; baseline.json was rewritten
//       (working tree) or has to be (--rev): commit it
//    4  the baseline asked for does not exist (a revision before the gate)
// =============================================================================

'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const CATEGORIES = ['comments', 'strings', 'markdown', 'json_values', 'identifiers',
  'json_keys', 'protocol', 'paths', 'transition'];
const COUNT_CATEGORIES = ['comments', 'strings', 'markdown', 'json_values', 'transition'];
const SET_CATEGORIES = ['identifiers', 'json_keys', 'protocol', 'paths'];

// The closed list of binary extensions. A tracked file is skipped ONLY if its
// extension is here: git's binary heuristic is never consulted.
const BINARY_EXTENSIONS = new Set([
  '.png', '.jpg', '.jpeg', '.gif', '.bmp', '.ico', '.tif', '.tiff', '.webp',
  '.sldprt', '.sldasm', '.slddrw', '.sldlfp', '.sldmat', '.sldblk', '.slddrt', '.prtdot', '.asmdot', '.drwdot',
  '.step', '.stp', '.igs', '.iges', '.stl', '.x_t', '.x_b', '.dxf', '.dwg',
  '.dll', '.exe', '.pdb', '.lib', '.obj', '.snk', '.pfx',
  '.pdf', '.zip', '.7z', '.gz', '.tar', '.rar', '.nupkg',
  '.ttf', '.otf', '.woff', '.woff2', '.eot',
]);

// The closed list of exception classes (TODO, entry W3.1-bis, section 1, with
// decision D2 of 28/9/2026: no "changelog history" class).
const EXCEPTION_CLASSES = new Set([
  'solidworks-localized', 'workshop-lexicon', 'quote', 'data-becomes-geometry',
  'todo-reader', 'detector-lexicon', 'transition',
]);

// Built from escapes so that this file carries no accented letter of its own.
const ACCENTED = '\u00e0\u00e8\u00e9\u00ec\u00f2\u00f9\u00c0\u00c8\u00c9\u00cc\u00d2\u00d9';
const LETTER = 'A-Za-z\u00c0-\u00ff';

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const LANG_DIR = path.join('Controlli', 'lingua');

// -----------------------------------------------------------------------------
//  Configuration
// -----------------------------------------------------------------------------

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\ufeff/, ''));
}

// The configuration comes from the working tree, or from a commit
// (`--config-rev`): the CI judges a commit with the lexicon and the
// exceptions of its PARENT, so a commit cannot loosen the rules it is judged by.
function readConfigFile(root, rev, name) {
  if (!rev) return readJson(path.join(root, LANG_DIR, name));
  const buf = cp.execFileSync('git', ['-C', root, 'show', rev + ':' + LANG_DIR.replace(/\\/g, '/') + '/' + name],
    { maxBuffer: 256 << 20, stdio: ['ignore', 'pipe', 'pipe'] });
  return JSON.parse(buf.toString('utf8').replace(/^\ufeff/, ''));
}

function loadConfig(root, configRev) {
  const lex = readConfigFile(root, configRev, 'lexicon.json');
  const exc = readConfigFile(root, configRev, 'exceptions.json');
  const lower = (a) => (a || []).map(w => w.toLowerCase());
  const config = {
    root,
    functionWords: new Set(lower(lex.function_words)),
    contentWords: new Set(lower(lex.content_words).concat(lower(lex.workshop_words))),
    weakWords: new Set(lower(lex.weak_words)),
    identifierWords: new Set(lower(lex.content_words).concat(lower(lex.workshop_words))
      .concat(lower(lex.identifier_only_words)).concat(lower(lex.function_words))),
    identifierStop: new Set(lower(lex.identifier_function_stop)),
    englishAllow: new Set(lower(lex.english_allow)),
    suffixes: (lex.italian_suffixes || []).map(s => new RegExp(s.pattern + '$', 'i')),
    proseSuffixes: (lex.italian_suffixes || []).filter(s => s.prose).map(s => new RegExp(s.pattern + '$', 'i')),
    suffixMinLength: lex.suffix_min_length || 6,
    elided: new RegExp('(?:^|[^' + LETTER + '])(?:' + lex.elided_forms.join('|') + ")'(?=[" + LETTER + '])', 'gi'),
    protocolKeys: new Set(lex.protocol_keys || []),
    limits: lex.exception_limits || {},
  };
  config.exceptions = validateExceptions(exc, lex, config);
  return config;
}

// An exception is a pattern with a class, a reason, a signature and a date.
// Never a whole file, directly or by a side door:
//   * an entry with only a `file` is refused;
//   * a `json_path` (which can cover every value of a file) is allowed only
//     for the class detector-lexicon, and only on a file of Controlli/lingua/;
//   * a pattern that matches one Italian function word on its own, or that
//     takes away a third of the Italian of any sentinel sentence, is refused;
//   * a pattern without `file` that matches one Italian content word on its
//     own is refused (a localized SOLIDWORKS feature name needs its file);
//   * after the scan, an exception that suppresses too much is refused
//     (`checkExceptionReach`, limits in lexicon.json).
function validateExceptions(list, lex, cfg) {
  if (!Array.isArray(list)) fail('exceptions.json must be an array');
  const sentinels = lex.exception_sentinels || [];
  if (sentinels.length < 2) fail('lexicon.json: exception_sentinels needs Italian and English sentences');
  const functionWords = (lex.function_words || []).map(w => w.toLowerCase());
  const contentWords = (lex.content_words || []).concat(lex.workshop_words || []).map(w => w.toLowerCase());
  return list.map((e, i) => {
    const where = 'exceptions.json entry ' + i;
    if (!EXCEPTION_CLASSES.has(e.class)) fail(where + ': class "' + e.class + '" is not in the closed list');
    for (const k of ['reason', 'signed_by', 'date']) if (!e[k] || typeof e[k] !== 'string') fail(where + ': missing "' + k + '"');
    if (!/^\d{4}-\d{2}-\d{2}$/.test(e.date)) fail(where + ': date must be YYYY-MM-DD');
    if (!Array.isArray(e.applies_to) || e.applies_to.length === 0) fail(where + ': missing "applies_to"');
    if (!e.pattern && !e.json_path) fail(where + ': an exception needs "pattern" or "json_path" - never a whole file');
    const out = { index: i, class: e.class, appliesToDetector: e.applies_to.includes('detector'), hasFile: !!e.file };
    if (e.pattern) {
      try { out.pattern = new RegExp(e.pattern, 'g'); out.full = new RegExp('^(?:' + e.pattern + ')$'); }
      catch (err) { fail(where + ': pattern does not compile: ' + err.message); }
      if (out.full.test('')) fail(where + ': pattern matches the empty string');
      for (const w of functionWords) {
        for (const form of [w, w[0].toUpperCase() + w.slice(1), w.toUpperCase()]) {
          if (out.full.test(form)) fail(where + ': pattern matches the Italian function word "' + form + '" on its own: too wide');
        }
      }
      if (!e.file) {
        for (const w of contentWords) {
          for (const form of [w, w[0].toUpperCase() + w.slice(1)]) {
            if (out.full.test(form)) fail(where + ': pattern without "file" matches the Italian word "' + form + '" on its own: name the file');
          }
        }
      }
      for (const s of sentinels) {
        out.pattern.lastIndex = 0;
        const m = s.match(out.pattern);
        if (m && m.join('').replace(/\s/g, '').length >= s.replace(/\s/g, '').length / 3)
          fail(where + ': pattern swallows the sentinel sentence "' + s + '": too wide');
        if (cfg) {
          const before = proseScore(s, cfg).score;
          out.pattern.lastIndex = 0;
          const after = proseScore(s.replace(out.pattern, m2 => ' '.repeat(m2.length)), cfg).score;
          if (before > 0 && before - after >= before / 3)
            fail(where + ': pattern takes away ' + (before - after) + ' of the ' + before + ' Italian words of the sentinel "' + s + '": too wide');
        }
      }
    }
    if (e.json_path) {
      if (e.class !== 'detector-lexicon') fail(where + ': json_path is allowed only for the class detector-lexicon');
      if (!e.file || !/^\^Controlli\/lingua\/[A-Za-z0-9_.\\-]+\$$/.test(e.file))
        fail(where + ': a json_path exception must name exactly one file of Controlli/lingua/ (^Controlli/lingua/<name>$)');
      if (!e.json_path.startsWith('^')) fail(where + ': json_path must be anchored with ^');
      try { out.jsonPath = new RegExp(e.json_path); } catch (err) { fail(where + ': json_path does not compile: ' + err.message); }
      if (out.jsonPath.test('') || out.jsonPath.test('x')) fail(where + ': json_path matches everything');
    }
    if (e.file) {
      try { out.file = new RegExp(e.file); } catch (err) { fail(where + ': file does not compile: ' + err.message); }
    }
    return out;
  });
}

// How much each exception takes away, measured on the tree it runs on. An
// exception that suppresses more than `per_file` hits in one file, or more
// than half of the Italian of a file once it suppresses at least
// `min_for_share` hits there, or more than `total` hits in the repository,
// covers a file (or the repository) in practice: refused.
function checkExceptionReach(report, cfg) {
  const lim = Object.assign({ per_file: 40, min_for_share: 10, total: 300 }, cfg.limits);
  const reach = report.exceptionReach || {};
  for (const [idx, perFile] of Object.entries(reach)) {
    const e = cfg.exceptions[idx];
    if (e.class === 'detector-lexicon') continue; // json_path on the gate's own files, checked by name above
    let total = 0;
    for (const [f, n] of Object.entries(perFile)) {
      total += n;
      const left = (report.hits[f] || []).length;
      if (n > lim.per_file) fail('exceptions.json entry ' + idx + ' suppresses ' + n + ' hits in ' + f + ' (limit ' + lim.per_file + '): it covers the file');
      if (n >= lim.min_for_share && n > left) fail('exceptions.json entry ' + idx + ' suppresses ' + n + ' hits in ' + f + ', more than the ' + left + ' left: it covers the file');
    }
    if (total > lim.total) fail('exceptions.json entry ' + idx + ' suppresses ' + total + ' hits in the repository (limit ' + lim.total + ')');
  }
}

function fail(msg) {
  const err = new Error(msg);
  err.gate = true;
  throw err;
}

// -----------------------------------------------------------------------------
//  Word tests
// -----------------------------------------------------------------------------

// Split a name on snake_case, kebab, dots, digits and camelCase boundaries
// (acronyms kept whole: IModelDoc2 -> I, Model, Doc).
function splitName(name) {
  return name
    .replace(/([a-z\u00e0-\u00ff])([A-Z\u00c0-\u00de])/g, '$1 $2')
    .replace(/([A-Z\u00c0-\u00de]+)([A-Z\u00c0-\u00de][a-z\u00e0-\u00ff])/g, '$1 $2')
    .split(/[^A-Za-z\u00c0-\u00ff]+/)
    .filter(Boolean);
}

function isItalianPart(part, cfg) {
  const p = part.toLowerCase();
  if (p.length < 2) return false;
  if (cfg.englishAllow.has(p)) return false;
  if (cfg.identifierStop.has(p)) return false;
  if (cfg.identifierWords.has(p)) return true;
  if (p.length >= cfg.suffixMinLength && /^[a-z]+$/.test(p)) {
    for (const s of cfg.suffixes) if (s.test(p)) return true;
  }
  return false;
}

function isItalianName(name, cfg) {
  // a hash or a hex number is not a name: its letters are noise
  if (/^(?=[0-9a-f]*\d)[0-9a-f]{6,}$/i.test(name)) return false;
  const parts = splitName(name);
  for (const p of parts) {
    // a part of one or two letters glued to a digit (ce in 264ce319) says nothing
    if (p.length <= 2 && new RegExp('\\d' + p + '|' + p + '\\d').test(name)) continue;
    if (isItalianPart(p, cfg)) return true;
  }
  return false;
}

// A "name-like" word inside prose: snake_case, or camelCase with an inner
// capital (fooBar), or a dotted member (Type.Member counts as two).
const NAME_LIKE = /^(?:[A-Za-z]+_[A-Za-z0-9_]*|_[A-Za-z0-9_]+|[a-z]+[A-Z][A-Za-z0-9]*|[A-Z][a-z0-9]+[A-Z][A-Za-z0-9]*)$/;

function proseVerdict(text, cfg) {
  // returns { italian: bool, score: Italian words, names: [names in the text], paths: [file paths in the text] }
  const names = [];
  const paths = [];
  // the typographic apostrophe is the same sign as the ASCII one
  let t = text.replace(/[\u2019\u02bc]/g, "'");
  // code spans
  t = t.replace(/`([^`]*)`/g, (m, inner) => {
    for (const piece of inner.split(/\s+/)) {
      if (/[\\/]|\.[A-Za-z0-9]{1,8}$/.test(piece)) paths.push(piece); else collectTokens(piece, names);
    }
    return ' ';
  });
  // URLs
  t = t.replace(/\b(?:https?|file):\/\/\S+/g, ' ');
  // file paths (a/b/c.js, a\b): names, not prose
  t = t.replace(/[A-Za-z0-9_.{}~*$-]*[A-Za-z0-9_{}~*$-][\\/][A-Za-z0-9_.{}~*$\\/-]*/g, (m) => { paths.push(m); return ' '; });
  // file names with a known extension (installa-ganci.ps1): names too
  t = t.replace(/\b[A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:cs|js|ps1|json|md|csproj|yml|yaml|txt|exe|dll|sln|config|bat|sh|png|sldprt|sldasm|slddrw|step|pdf)\b/gi,
    (m) => { paths.push(m); return ' '; });
  // XML doc tags inside comments
  t = t.replace(/<\/?[A-Za-z][^<>]*>/g, ' ');
  // name-like words
  t = t.replace(/[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*/g, (m) => {
    const pieces = m.split('.');
    if (pieces.length > 1 || NAME_LIKE.test(m)) {
      if (pieces.every(p => NAME_LIKE.test(p) || /^[A-Z]/.test(p)) || pieces.some(p => NAME_LIKE.test(p))) {
        for (const p of pieces) names.push(p);
        return ' ';
      }
    }
    return m;
  });
  const s = proseScore(t, cfg);
  return { italian: s.italian, score: s.score, names, paths };
}

// The ASCII accent of this repository: a word ending in a vowel and an
// apostrophe, the apostrophe being its only one (a word that OPENS with an
// apostrophe, 'name' or '$Base', is a quotation, not an accent).
const ACCENT_WORD = new RegExp("^[" + LETTER + "]*[aeiouAEIOU]'$");
const OPENING = new RegExp('^[(\\[{"\u00ab*_]+');
const CLOSING = new RegExp('[.,;:!?)\\]}"\u00bb*_-]+$');
function apostropheAccents(t) {
  if (t.indexOf("'") < 0) return 0;
  let n = 0;
  for (const raw of t.split(/\s+/)) {
    const w = raw.replace(OPENING, '').replace(CLOSING, '');
    if (ACCENT_WORD.test(w) && w.indexOf("'") === w.length - 1) n++;
  }
  return n;
}
const FINAL_ACCENT = new RegExp('[' + ACCENTED + '](?![' + LETTER + '])', 'g');
const WORD = new RegExp('[' + LETTER + ']+', 'g');

// The Italian of a piece of prose, as a number of signals (words). A line is
// Italian when it has one STRONG signal (an Italian function word, an Italian
// content word, a word with an Italian-only ending, the ASCII accent, an
// elided article, a word-final accented letter) or two WEAK ones. A weak word
// is an Italian word that English also uses (a musical term, an abbreviation,
// a brand; weak_words in lexicon.json): alone it proves nothing. The score
// is what the counted categories add up per file, so appending Italian to a line that is
// already Italian makes the file grow.
function proseScore(t, cfg) {
  t = t.replace(/[\u2019\u02bc]/g, "'");
  let strong = apostropheAccents(t);
  strong += (t.match(FINAL_ACCENT) || []).length;
  cfg.elided.lastIndex = 0;
  strong += (t.match(cfg.elided) || []).length;
  let weak = 0;
  WORD.lastIndex = 0;
  let m;
  while ((m = WORD.exec(t)) !== null) {
    const w = m[0];
    const before = t[m.index - 1] || ' ';
    const after = t[m.index + w.length] || ' ';
    // members (.Nome), variables ($lo), calls (lo(), assignments (lo=)
    if ('$@.\\/#%&='.includes(before) || after === '(' || after === '=') continue;
    // glued to a digit (utf-16le, 3ai): a unit or a code, not a word
    if (/\d/.test(before) || /\d/.test(after)) continue;
    // a word alone between quotes ("se", 'ai') is a quoted name
    if ((before === '"' || before === "'") && after === before) continue;
    // operators and flags (-le, --no-verify)
    const flag = before === '-' && (m.index < 2 || /[\s-]/.test(t[m.index - 2]));
    if (flag) continue;
    // inside a hyphenated compound only content words count: "non-empty" is
    // English, two Italian nouns joined by a hyphen are not
    const compound = before === '-' || after === '-';
    const lower = w.toLowerCase();
    const allCaps = w === w.toUpperCase() && w !== lower;
    const shape = w === lower || (w[0] !== lower[0] && w.slice(1) === lower.slice(1));
    if (!shape && !allCaps) continue;
    if (allCaps && w.length < 3) continue;
    if (cfg.weakWords.has(lower)) { weak++; continue; }
    if (!compound && cfg.functionWords.has(lower)) { strong++; continue; }
    if (w.length >= 4 && cfg.contentWords.has(lower) && !cfg.englishAllow.has(lower)) { strong++; continue; }
    if (w.length >= cfg.suffixMinLength && /^[a-z]+$/.test(lower) && !cfg.englishAllow.has(lower) &&
        cfg.proseSuffixes.some(s => s.test(lower))) { strong++; continue; }
  }
  const italian = strong > 0 || weak >= 2;
  return { italian, score: italian ? strong + weak : 0 };
}

function proseIsItalian(t, cfg) { return proseScore(t, cfg).italian; }

function collectTokens(text, out) {
  const re = /[A-Za-z_\u00c0-\u00ff][A-Za-z0-9_\u00c0-\u00ff]*/g;
  let m;
  while ((m = re.exec(text)) !== null) out.push(m[0]);
}

// -----------------------------------------------------------------------------
//  Lexers: they turn a file into units { kind, line, text }
//    kind: 'comment' | 'string' | 'code' | 'markdown'
// -----------------------------------------------------------------------------

function lineAt(starts, offset) {
  let lo = 0, hi = starts.length - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (starts[mid] <= offset) lo = mid; else hi = mid - 1;
  }
  return lo + 1;
}

function lineStarts(text) {
  const s = [0];
  for (let i = 0; i < text.length; i++) if (text.charCodeAt(i) === 10) s.push(i + 1);
  return s;
}

// Emit one unit per physical line of a span.
function emitSpan(units, kind, text, from, to, starts) {
  const chunk = text.slice(from, to);
  const first = lineAt(starts, from);
  const lines = chunk.split('\n');
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i].replace(/\r$/, '');
    if (l.trim()) units.push({ kind, line: first + i, text: l });
  }
}

function emitCode(units, text, from, to, starts) {
  const re = /@?[A-Za-z_\u00c0-\u00ff][A-Za-z0-9_\u00c0-\u00ff]*/g;
  const chunk = text.slice(from, to);
  let m;
  while ((m = re.exec(chunk)) !== null) {
    units.push({ kind: 'code', line: lineAt(starts, from + m.index), text: m[0].replace(/^@/, '') });
  }
}

// C# and JavaScript share one lexer; `js` switches on template literals and
// regular-expression literals, `cs` on verbatim and interpolated strings.
function lexCLike(text, lang) {
  const units = [];
  const starts = lineStarts(text);
  let i = 0, codeFrom = 0;
  const n = text.length;
  let lastSignificant = '';
  const flushCode = (to) => { if (to > codeFrom) emitCode(units, text, codeFrom, to, starts); };
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === '/' && d === '/') {
      flushCode(i);
      let j = text.indexOf('\n', i); if (j < 0) j = n;
      emitSpan(units, 'comment', text, i + 2, j, starts);
      i = j; codeFrom = i; continue;
    }
    if (c === '/' && d === '*') {
      flushCode(i);
      let j = text.indexOf('*/', i + 2); if (j < 0) j = n;
      emitSpan(units, 'comment', text, i + 2, j, starts);
      i = Math.min(n, j + 2); codeFrom = i; continue;
    }
    if (lang === 'cs' && c === '#' && /^\s*$/.test(text.slice(text.lastIndexOf('\n', i - 1) + 1, i))) {
      // preprocessor line: #region text is prose
      flushCode(i);
      let j = text.indexOf('\n', i); if (j < 0) j = n;
      const line = text.slice(i, j);
      const m = /^#\s*(region|endregion|warning|error)\b/.exec(line);
      if (m) emitSpan(units, 'comment', text, i + m[0].length, j, starts);
      else emitCode(units, text, i + 1, j, starts);
      i = j; codeFrom = i; continue;
    }
    if (lang === 'cs' && (c === '@' || c === '$') && (d === '"' || ((d === '@' || d === '$') && text[i + 2] === '"'))) {
      flushCode(i);
      const verbatim = c === '@' || d === '@';
      const interp = c === '$' || d === '$';
      const q = text.indexOf('"', i);
      const holes = interp ? [] : null;
      const end = scanCsString(text, q + 1, verbatim, interp, holes);
      emitString(units, text, q + 1, end, starts, holes, false, 'cs');
      i = Math.min(n, end + 1); codeFrom = i; lastSignificant = '"'; continue;
    }
    if (c === '"' || (c === "'" && lang === 'js')) {
      flushCode(i);
      const end = scanSimpleString(text, i + 1, c);
      emitString(units, text, i + 1, end, starts, null);
      i = Math.min(n, end + 1); codeFrom = i; lastSignificant = '"'; continue;
    }
    if (c === "'" && lang === 'cs') {
      // char literal
      flushCode(i);
      const end = scanSimpleString(text, i + 1, "'");
      i = Math.min(n, end + 1); codeFrom = i; lastSignificant = "'"; continue;
    }
    if (c === '`' && lang === 'js') {
      flushCode(i);
      const holes = [];
      const end = scanTemplate(text, i + 1, holes);
      emitString(units, text, i + 1, end, starts, holes, false, 'js');
      i = Math.min(n, end + 1); codeFrom = i; lastSignificant = '`'; continue;
    }
    if (c === '/' && lang === 'js' && regexAllowed(lastSignificant, text, i)) {
      const end = scanRegex(text, i + 1);
      if (end > 0) {
        flushCode(i);
        emitString(units, text, i + 1, end, starts, null, true);
        i = end + 1;
        while (i < n && /[a-z]/.test(text[i])) i++;
        codeFrom = i; lastSignificant = '/'; continue;
      }
    }
    if (!/\s/.test(c)) lastSignificant = c;
    if (/[A-Za-z_]/.test(c)) {
      // keep whole words together for the regex test
      let j = i; while (j < n && /[A-Za-z0-9_$]/.test(text[j])) j++;
      lastSignificant = text.slice(i, j);
      i = j; continue;
    }
    i++;
  }
  flushCode(n);
  return units;
}

function regexAllowed(prev, text, i) {
  if (prev === '' ) return true;
  if (/^[(,=:[!&|?{};+\-*%<>~^]$/.test(prev)) return true;
  if (/^(return|typeof|case|do|else|in|of|new|delete|void|throw)$/.test(prev)) return true;
  return false;
}

function scanRegex(text, i) {
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

function scanSimpleString(text, i, q) {
  for (let j = i; j < text.length; j++) {
    const c = text[j];
    if (c === '\\') { j++; continue; }
    if (c === q) return j;
    if (c === '\n') return j; // unterminated: stop at the end of the line
  }
  return text.length;
}

// `holes`, when given, receives the [from, to) offsets of every interpolation
// hole: its content is code, and is lexed again as code (strings inside a hole
// are strings, names are names).
function scanCsString(text, i, verbatim, interp, holes) {
  let depth = 0, holeFrom = -1;
  for (let j = i; j < text.length; j++) {
    const c = text[j];
    if (interp && depth === 0 && c === '{') {
      if (text[j + 1] === '{') { j++; continue; }
      depth = 1; holeFrom = j + 1; continue;
    }
    if (depth > 0) {
      if (c === '{') depth++;
      else if (c === '}') { depth--; if (depth === 0 && holes) holes.push([holeFrom, j]); }
      else if (c === '"') { // nested string inside a hole
        const e = scanSimpleString(text, j + 1, '"'); j = e;
      }
      else if ((c === '$' || c === '@') && text[j + 1] === '"') { // nested interpolated or verbatim string
        const e = scanCsString(text, j + 2, c === '@', c === '$', null); j = e;
      }
      continue;
    }
    if (verbatim) {
      if (c === '"') { if (text[j + 1] === '"') { j++; continue; } return j; }
    } else {
      if (c === '\\') { j++; continue; }
      if (c === '"' || c === '\n') return j;
    }
  }
  return text.length;
}

function scanTemplate(text, i, holes) {
  for (let j = i; j < text.length; j++) {
    const c = text[j];
    if (c === '\\') { j++; continue; }
    if (c === '`') return j;
    if (c === '$' && text[j + 1] === '{') {
      let depth = 1; j += 2;
      const holeFrom = j;
      for (; j < text.length && depth > 0; j++) {
        if (text[j] === '{') depth++;
        else if (text[j] === '}') { depth--; if (depth === 0 && holes) holes.push([holeFrom, j]); }
        else if (text[j] === '`') j = scanTemplate(text, j + 1, null);
        else if (text[j] === '"' || text[j] === "'") j = scanSimpleString(text, j + 1, text[j]);
      }
      j--;
    }
  }
  return text.length;
}

// A string literal. The holes of an interpolated string are blanked in the
// literal and their content is lexed again as code of the same language: a
// string inside a hole is a string, a name inside a hole is an identifier.
function emitString(units, text, from, to, starts, holes, regex, lang) {
  let s = text.slice(from, to);
  if (holes) {
    const chars = s.split('');
    for (const [a, b] of holes) {
      const ha = lang === 'js' ? a - 2 : a - 1, hb = b + 1; // include ${ / { and }
      for (let k = Math.max(ha, from); k < Math.min(hb, to); k++) if (chars[k - from] !== '\n') chars[k - from] = ' ';
      const inner = lexCLike(text.slice(a, b), lang);
      const shift = lineAt(starts, a) - 1;
      for (const u of inner) { u.line += shift; units.push(u); }
    }
    s = chars.join('');
    if (lang === 'cs') s = s.replace(/\{\{|\}\}/g, '  ');
  }
  const first = lineAt(starts, from);
  const lines = s.split('\n');
  for (let k = 0; k < lines.length; k++) {
    const l = lines[k].replace(/\r$/, '');
    if (l.trim()) units.push({ kind: 'string', line: first + k, text: l, whole: lines.length === 1, regex: !!regex });
  }
}

// PowerShell: # and <# #> comments, '...' "..." and here-strings.
function lexPowerShell(text) {
  const units = [];
  const starts = lineStarts(text);
  let i = 0, codeFrom = 0;
  const n = text.length;
  const flushCode = (to) => { if (to > codeFrom) emitCode(units, text, codeFrom, to, starts); };
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === '<' && d === '#') {
      flushCode(i);
      let j = text.indexOf('#>', i + 2); if (j < 0) j = n;
      emitSpan(units, 'comment', text, i + 2, j, starts);
      i = Math.min(n, j + 2); codeFrom = i; continue;
    }
    if (c === '#' && (i === 0 || /[\s;({]/.test(text[i - 1]))) {
      flushCode(i);
      let j = text.indexOf('\n', i); if (j < 0) j = n;
      emitSpan(units, 'comment', text, i + 1, j, starts);
      i = j; codeFrom = i; continue;
    }
    if (c === '@' && (d === '"' || d === "'") && /^\r?\n/.test(text.slice(i + 2, i + 4))) {
      flushCode(i);
      const close = '\n' + d + '@';
      let j = text.indexOf(close, i + 2); if (j < 0) j = n;
      emitString(units, text, i + 2, j, starts, null);
      i = Math.min(n, j + 3); codeFrom = i; continue;
    }
    if (c === "'") {
      flushCode(i);
      let j = i + 1;
      for (; j < n; j++) { if (text[j] === "'") { if (text[j + 1] === "'") { j++; continue; } break; } }
      emitString(units, text, i + 1, j, starts, null);
      i = Math.min(n, j + 1); codeFrom = i; continue;
    }
    if (c === '"') {
      flushCode(i);
      let j = i + 1;
      for (; j < n; j++) {
        if (text[j] === '`') { j++; continue; }
        if (text[j] === '"') { if (text[j + 1] === '"') { j++; continue; } break; }
      }
      emitString(units, text, i + 1, j, starts, null);
      i = Math.min(n, j + 1); codeFrom = i; continue;
    }
    i++;
  }
  flushCode(n);
  // PowerShell subexpressions $( ... ) and $var inside "..." are left in the
  // text: their names are English or already counted as code.
  return units;
}

// Shell scripts, .gitignore, .gitattributes: # comments, quoted strings, and
// the rest as code.
// Two things in a shell script are prose even without quotes: the words of an
// `echo` or `printf` command, and the body of a here-document (<<EOF ... EOF).
// Both are read as strings; the rest of the line is code.
const SHELL_ECHO = /(^|[;&|]\s*|\b(?:then|do|else)\s+)(echo|printf)\s+/g;
function lexHash(text, { strings = true } = {}) {
  const units = [];
  const lines = text.split('\n');
  let heredoc = null; // terminator of an open here-document
  lines.forEach((raw, idx) => {
    const lineNo = idx + 1;
    const l = raw.replace(/\r$/, '');
    let k = 0, codeStart = 0;
    const pushCode = (from, to) => {
      const re = /[A-Za-z_][A-Za-z0-9_]*/g; const chunk = l.slice(from, to); let m;
      while ((m = re.exec(chunk)) !== null) units.push({ kind: 'code', line: lineNo, text: m[0] });
    };
    if (idx === 0 && l.startsWith('#!')) return;
    if (heredoc !== null) {
      if (l.trim() === heredoc) { heredoc = null; return; }
      if (l.trim()) units.push({ kind: 'string', line: lineNo, text: l });
      return;
    }
    const hd = /<<-?\s*(['"]?)([A-Za-z_][A-Za-z0-9_]*)\1/.exec(l);
    if (hd) heredoc = hd[2];
    // echo / printf: the words up to the next unquoted separator are prose
    SHELL_ECHO.lastIndex = 0;
    const em = SHELL_ECHO.exec(l);
    if (em && !/^\s*#/.test(l)) {
      const from = em.index + em[0].length;
      let j = from, q = null;
      for (; j < l.length; j++) {
        const c = l[j];
        if (q) { if (c === '\\' && q === '"') { j++; continue; } if (c === q) q = null; continue; }
        if (c === '"' || c === "'") { q = c; continue; }
        if (c === ';' || c === '|' || c === '&' || c === '>' || c === '<' || (c === '#' && /\s/.test(l[j - 1] || ' '))) break;
      }
      pushCode(0, from);
      const words = l.slice(from, j).replace(/(^|\s)-[a-z]+(?=\s)/g, ' ').replace(/["']/g, ' ');
      if (words.trim()) units.push({ kind: 'string', line: lineNo, text: words });
      k = j; codeStart = j;
    }
    while (k < l.length) {
      const c = l[k];
      if (c === '#' && (k === 0 || /\s/.test(l[k - 1]))) {
        pushCode(codeStart, k);
        const t = l.slice(k + 1);
        if (t.trim()) units.push({ kind: 'comment', line: lineNo, text: t });
        codeStart = l.length; k = l.length; break;
      }
      if (strings && (c === '"' || c === "'")) {
        pushCode(codeStart, k);
        let j = k + 1; while (j < l.length && l[j] !== c) { if (l[j] === '\\' && c === '"') j++; j++; }
        const t = l.slice(k + 1, j);
        if (t.trim()) units.push({ kind: 'string', line: lineNo, text: t, whole: true });
        k = j + 1; codeStart = k; continue;
      }
      k++;
    }
    pushCode(codeStart, l.length);
  });
  return units;
}

// .gitignore / .gitattributes: comments are prose, the rest are path patterns.
function lexIgnore(text) {
  const units = [];
  text.split('\n').forEach((raw, idx) => {
    const l = raw.replace(/\r$/, '');
    const t = l.trim();
    if (!t) return;
    if (t.startsWith('#')) { units.push({ kind: 'comment', line: idx + 1, text: t.slice(1) }); return; }
    units.push({ kind: 'string', line: idx + 1, text: t.split(/\s+/)[0], whole: true, token: true });
  });
  return units;
}

// YAML: # comments; "key: value" -> key is code, value is a string.
function lexYaml(text) {
  const units = [];
  const lines = text.split('\n');
  let block = -1; // indentation of an open block scalar
  lines.forEach((raw, idx) => {
    const lineNo = idx + 1;
    const l = raw.replace(/\r$/, '');
    const indent = l.length - l.trimStart().length;
    const t = l.trim();
    if (block >= 0) {
      if (!t || indent > block) { if (t) units.push({ kind: 'string', line: lineNo, text: t }); return; }
      block = -1;
    }
    if (!t) return;
    if (t.startsWith('#')) { units.push({ kind: 'comment', line: lineNo, text: t.slice(1) }); return; }
    let body = t.replace(/^-\s+/, '');
    // trailing comment
    const hash = body.search(/\s#/);
    if (hash >= 0) { units.push({ kind: 'comment', line: lineNo, text: body.slice(hash + 2) }); body = body.slice(0, hash); }
    const m = /^([A-Za-z0-9_.-]+):(\s+|$)(.*)$/.exec(body);
    let value = body;
    if (m) {
      units.push({ kind: 'code', line: lineNo, text: m[1] });
      value = m[3];
      if (/^[|>][-+]?$/.test(value.trim())) { block = indent; return; }
    }
    value = value.trim().replace(/^(['"])(.*)\1$/, '$2');
    if (value) units.push({ kind: 'string', line: lineNo, text: value, whole: true });
  });
  return units;
}

// XML (.csproj, .config, .manifest...): comments, text nodes, attribute values.
function lexXml(text) {
  const units = [];
  const starts = lineStarts(text);
  const re = /<!--([\s\S]*?)-->|<([A-Za-z_][\w:.-]*)((?:\s+[\w:.-]+\s*=\s*(?:"[^"]*"|'[^']*'))*)\s*\/?>|<\/([A-Za-z_][\w:.-]*)\s*>|<\?[\s\S]*?\?>|<!\[CDATA\[([\s\S]*?)\]\]>/g;
  let last = 0, m;
  const textNode = (from, to) => {
    const s = text.slice(from, to);
    if (s.trim()) emitSpan(units, 'string', text, from, to, starts);
  };
  while ((m = re.exec(text)) !== null) {
    textNode(last, m.index);
    last = re.lastIndex;
    if (m[1] !== undefined) { emitSpan(units, 'comment', text, m.index + 4, m.index + 4 + m[1].length, starts); continue; }
    if (m[2]) {
      units.push({ kind: 'code', line: lineAt(starts, m.index), text: m[2] });
      const attrs = m[3] || '';
      const are = /([\w:.-]+)\s*=\s*("([^"]*)"|'([^']*)')/g;
      let a;
      const base = m.index + 1 + m[2].length;
      while ((a = are.exec(attrs)) !== null) {
        const v = a[3] !== undefined ? a[3] : a[4];
        units.push({ kind: 'code', line: lineAt(starts, base + a.index), text: a[1] });
        if (v.trim()) units.push({ kind: 'string', line: lineAt(starts, base + a.index), text: v, whole: true });
      }
    }
  }
  textNode(last, text.length);
  return units;
}

function lexMarkdown(text) {
  const units = [];
  text.split('\n').forEach((raw, idx) => {
    const l = raw.replace(/\r$/, '');
    if (l.trim()) units.push({ kind: 'markdown', line: idx + 1, text: l });
  });
  return units;
}

// -----------------------------------------------------------------------------
//  JSON with line numbers and paths
// -----------------------------------------------------------------------------

function parseJsonItems(text) {
  // returns [{ type: 'key'|'value', path, key, value, line }]
  const items = [];
  let i = 0, line = 1;
  const n = text.length;
  const ws = () => {
    while (i < n) {
      const c = text[i];
      if (c === '\n') { line++; i++; }
      else if (c === ' ' || c === '\t' || c === '\r' || c === '\ufeff') i++;
      else break;
    }
  };
  const str = () => {
    if (text[i] !== '"') throw new Error('expected string at line ' + line);
    let j = i + 1, out = '';
    while (j < n && text[j] !== '"') {
      if (text[j] === '\\') {
        const e = text[j + 1];
        if (e === 'u') { out += String.fromCharCode(parseInt(text.slice(j + 2, j + 6), 16)); j += 6; continue; }
        out += ({ n: '\n', t: '\t', r: '\r', b: '\b', f: '\f' })[e] || e; j += 2; continue;
      }
      if (text[j] === '\n') throw new Error('newline in string at line ' + line);
      out += text[j]; j++;
    }
    if (j >= n) throw new Error('unterminated string at line ' + line);
    i = j + 1;
    return out;
  };
  const value = (p, key) => {
    ws();
    const c = text[i];
    if (c === '{') {
      i++; ws();
      if (text[i] === '}') { i++; return; }
      for (;;) {
        ws();
        const kl = line;
        const k = str();
        const kp = p ? p + '.' + k : k;
        items.push({ type: 'key', path: kp, key: k, value: k, line: kl });
        ws();
        if (text[i] !== ':') throw new Error('expected : at line ' + line);
        i++;
        value(kp, k);
        ws();
        if (text[i] === ',') { i++; continue; }
        if (text[i] === '}') { i++; return; }
        throw new Error('expected , or } at line ' + line);
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
        throw new Error('expected , or ] at line ' + line);
      }
    }
    if (c === '"') {
      const l = line;
      const v = str();
      items.push({ type: 'value', path: p, key, value: v, line: l });
      return;
    }
    const m = /^(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null)/.exec(text.slice(i, i + 40));
    if (!m) throw new Error('unexpected character at line ' + line);
    i += m[0].length;
  };
  value('', null);
  ws();
  if (i < n) throw new Error('trailing content at line ' + line);
  return items;
}

// -----------------------------------------------------------------------------
//  Analysis of one file
// -----------------------------------------------------------------------------

function kindOf(file) {
  const base = path.basename(file).toLowerCase();
  const ext = path.extname(base);
  if (ext === '.cs') return 'cs';
  if (ext === '.js' || ext === '.mjs' || ext === '.cjs') return 'js';
  if (ext === '.ps1' || ext === '.psm1' || ext === '.psd1') return 'ps';
  if (ext === '.json') return 'json';
  if (ext === '.md' || ext === '.txt' || base === 'license' || base === 'notice') return 'markdown';
  if (ext === '.yml' || ext === '.yaml') return 'yaml';
  if (['.csproj', '.config', '.manifest', '.xml', '.props', '.targets', '.resx', '.sln', '.xaml', '.nuspec'].includes(ext)) return 'xml';
  if (base === '.gitignore' || base === '.gitattributes') return 'ignore';
  if (ext === '.sh' || ext === '' ) return 'hash';
  return 'markdown';
}

function decode(buf) {
  if (buf[0] === 0xFF && buf[1] === 0xFE) return buf.slice(2).toString('utf16le');
  if (buf[0] === 0xFE && buf[1] === 0xFF) { const b = Buffer.from(buf.slice(2)); b.swap16(); return b.toString('utf16le'); }
  return buf.toString('utf8').replace(/^\ufeff/, '');
}

function exceptionsFor(file, cfg) {
  return cfg.exceptions.filter(e => e.appliesToDetector && (!e.file || e.file.test(file)));
}

// Blank the spans an exception covers. Returns { text, transition, matched }.
function applyTextExceptions(text, excs) {
  let t = text, transition = false;
  const matched = [];
  for (const e of excs) {
    if (!e.pattern) continue;
    e.pattern.lastIndex = 0;
    const replaced = t.replace(e.pattern, m => ' '.repeat(m.length));
    if (replaced !== t) {
      if (e.class === 'transition') transition = true;
      matched.push(e.index);
      t = replaced;
    }
  }
  return { text: t, transition, matched };
}

// Does an exception cover this whole element? Returns null or { kind: 'skip' | 'transition', index }.
function elementException(element, excs) {
  for (const e of excs) {
    if (e.full && e.full.test(element)) return { kind: e.class === 'transition' ? 'transition' : 'skip', index: e.index };
  }
  return null;
}

// Selectors (@kind(dir=+Z)) and placeholders ({NAME}) are names. Returns
// the names found, and the text with them blanked: if what is left still has
// words separated by spaces, it is prose.
const SELECTOR = /@[A-Za-z_]+\((?:[^()]|\([^()]*\))*\)|\{[^{}\s]*\}/g;
function splitSelectors(v) {
  const names = [];
  const rest = v.replace(SELECTOR, m => { names.push(m); return ' '.repeat(m.length); });
  return { names, rest };
}

// A name joined by hyphens (read-the-face) is ONE name: it is registered
// whole, besides its words, so a new compound of known words is a new
// element. Dots usually join a PATH of keys (the measures.volume of a case):
// each key is already a name, and the path is not registered whole unless a
// part is a short function word, an article or a preposition (read.the.face
// is a name glued with dots).
const KNOWN_EXTENSION = /\.(?:cs|js|mjs|cjs|ps1|psm1|psd1|json|md|csproj|sln|yml|yaml|txt|exe|dll|pdb|config|bat|sh|py|png|jpg|svg|sldprt|sldasm|slddrw|sldlfp|step|stp|stl|igs|iges|pdf|xml|xaml|resx|log|tsv|csv|html|zip)$/i;
function compoundOf(name, cfg) {
  const bare = name.replace(KNOWN_EXTENSION, '').replace(/^[^A-Za-z\u00c0-\u00ff]+|[^A-Za-z0-9\u00c0-\u00ff]+$/g, '');
  if (/^[A-Za-z\u00c0-\u00ff][A-Za-z0-9_\u00c0-\u00ff]*(?:-[A-Za-z0-9_\u00c0-\u00ff]+)+$/.test(bare)) return bare;
  if (/^[a-z\u00e0-\u00ff][a-z0-9_\u00e0-\u00ff]*(?:\.[a-z\u00e0-\u00ff][a-z0-9_\u00e0-\u00ff]*)+$/.test(bare) &&
      bare.split('.').some(p => p.length <= 3 && (cfg.functionWords.has(p) || cfg.weakWords.has(p)))) return bare;
  return null;
}

function analyzeText(file, text, cfg) {
  const kind = kindOf(file);
  const excs = exceptionsFor(file, cfg);
  const hits = []; // { category, line, text, element?, score? }
  const reach = {}; // exception index -> hits it suppressed
  const credit = (indices) => { for (const i of indices) reach[i] = (reach[i] || 0) + 1; };
  const addProse = (category, line, raw) => {
    const ex = applyTextExceptions(raw, excs);
    const v = proseVerdict(ex.text, cfg);
    if (ex.matched.length) {
      const whole = proseVerdict(raw, cfg);
      if (whole.score > v.score) {
        if (ex.transition) hits.push({ category: 'transition', line, text: raw.trim(), score: whole.score - v.score });
        credit(ex.matched.filter(i => cfg.exceptions[i].class !== 'transition'));
      }
    }
    if (v.italian) hits.push({ category, line, text: raw.trim(), score: v.score });
    for (const nm of v.names) addElement('identifiers', line, nm, raw);
    for (const p of v.paths) addPathValue('identifiers', line, p, raw);
  };
  // A path names files: a segment that is a tracked file or folder of today
  // is already counted under `paths`, and a reference to it is not a new
  // name. When that file is renamed, the stale reference becomes new here.
  const addPathValue = (category, line, value, context) => {
    for (const seg of value.split(/[\\/]+/)) {
      if (!seg) continue;
      const bare = seg.replace(/\.[A-Za-z0-9]{1,8}$/, '');
      if (cfg.pathSegments && (cfg.pathSegments.has(seg) || cfg.pathSegments.has(bare))) continue;
      addName(category, line, seg, context || value);
    }
  };
  const addElement = (category, line, element, context) => {
    if (!isItalianName(element, cfg)) return;
    const ex = elementException(element, excs);
    if (ex && ex.kind === 'skip') { credit([ex.index]); return; }
    hits.push({ category: ex ? 'transition' : category, line, element, text: (context || element).trim() });
  };
  // A name: its words one by one, and the whole compound when it is one.
  const addName = (category, line, name, context) => {
    const toks = [];
    collectTokens(name, toks);
    for (const t of toks) addElement(category, line, t, context);
    const whole = compoundOf(name, cfg);
    if (whole && toks.length >= 2) addElement(category, line, whole, context);
  };
  // A value without spaces is a name, not prose: its words go to `protocol`.
  const addTokenValue = (category, line, value) => {
    const ex = elementException(value, excs);
    if (ex && ex.kind === 'skip') { if (isItalianName(value, cfg)) credit([ex.index]); return; }
    if (ex) { if (isItalianName(value, cfg)) hits.push({ category: 'transition', line, element: value, text: value }); return; }
    // spans an exception covers inside a longer value (Taglio-Estrusione1
    // inside a JSON example) are blanked before the value is cut into names
    const blanked = applyTextExceptions(value, excs);
    const target = blanked.transition ? 'transition' : category;
    if (/[\\/]/.test(blanked.text)) { addPathValue(target, line, blanked.text, value); return; }
    for (const piece of blanked.text.split(/\s+/)) if (piece) addName(target, line, piece, value);
  };
  // A string value: a name if it has no spaces once its selectors and
  // placeholders are set aside; prose otherwise (and the selectors are names).
  const addValue = (tokenCategory, proseCategory, line, value) => {
    if (!/[A-Za-z\u00c0-\u00ff]/.test(value)) return;
    const t = value.trim();
    if (!/\s/.test(t)) { addTokenValue(tokenCategory, line, t); return; }
    const sp = splitSelectors(t);
    if (!/\S\s+\S/.test(sp.rest.trim())) {
      for (const nm of sp.names) addTokenValue(tokenCategory, line, nm);
      if (sp.rest.trim()) addTokenValue(tokenCategory, line, sp.rest.trim());
      return;
    }
    for (const nm of sp.names) addTokenValue(tokenCategory, line, nm);
    addProse(proseCategory, line, sp.rest);
  };

  if (kind === 'json') {
    let items = null;
    try { items = parseJsonItems(text); }
    catch (err) { items = null; }
    if (items) {
      for (const it of items) {
        const jex = excs.find(e => e.jsonPath && e.jsonPath.test(it.path));
        if (jex) { if (jex.class !== 'transition') continue; }
        if (it.type === 'key') {
          // a key with spaces is still ONE key: it is registered whole
          if (/\s/.test(it.key.trim())) {
            const ex = applyTextExceptions(it.key, excs);
            if (proseVerdict(ex.text, cfg).italian) hits.push({ category: jex || ex.transition ? 'transition' : 'json_keys', line: it.line, element: it.key.trim(), text: it.key.trim() });
          }
          else addElement(jex ? 'transition' : 'json_keys', it.line, it.key, it.key);
          continue;
        }
        const v = it.value;
        if (!v.trim()) continue;
        const parts = v.split('\n');
        if (parts.length > 1) { parts.forEach(part => { if (part.trim()) addProse(jex ? 'transition' : 'json_values', it.line, part); }); continue; }
        addValue(jex ? 'transition' : 'protocol', jex ? 'transition' : 'json_values', it.line, v);
      }
      return { kind, hits, reach, parseError: null };
    }
    // unparsable JSON is still read: line by line, as prose
    lexMarkdown(text).forEach(u => addProse('json_values', u.line, u.text));
    return { kind, hits, reach, parseError: 'not valid JSON: read as text' };
  }

  let units;
  if (kind === 'cs') units = lexCLike(text, 'cs');
  else if (kind === 'js') units = lexCLike(text.replace(/^#!.*/, ''), 'js');
  else if (kind === 'ps') units = lexPowerShell(text);
  else if (kind === 'yaml') units = lexYaml(text);
  else if (kind === 'xml') units = lexXml(text);
  else if (kind === 'ignore') units = lexIgnore(text);
  else if (kind === 'hash') units = lexHash(text);
  else units = lexMarkdown(text);

  for (const u of units) {
    if (u.kind === 'comment') addProse('comments', u.line, u.text);
    else if (u.kind === 'markdown') addProse('markdown', u.line, u.text);
    else if (u.kind === 'code') addElement('identifiers', u.line, u.text, u.text);
    else if (u.kind === 'string') {
      if (u.token) addTokenValue('protocol', u.line, u.text);
      else if (u.whole) addValue('protocol', 'strings', u.line, u.text);
      else addProse('strings', u.line, u.text);
    }
  }
  return { kind, hits, reach, parseError: null };
}

// -----------------------------------------------------------------------------
//  The whole tree
// -----------------------------------------------------------------------------

function listFiles(root) {
  const out = cp.execFileSync('git', ['-C', root, 'ls-files', '-z'], { encoding: 'utf8', maxBuffer: 256 << 20 });
  return out.split('\0').filter(Boolean);
}

// Where the files come from: the working tree (default), or a commit
// (`--rev`), read from the object store without a checkout. The CI judges
// every commit of a range this way, and the pre-push hook judges the commits
// being pushed rather than whatever is on disk.
function treeSource(root, rev) {
  if (!rev) {
    return {
      files: listFiles(root),
      // a tracked file deleted on disk but not staged yet is absent, not unreadable
      read: (f) => {
        try { return fs.readFileSync(path.join(root, f)); }
        catch (err) { if (err.code === 'ENOENT') return null; throw err; }
      },
    };
  }
  const files = cp.execFileSync('git', ['-C', root, 'ls-tree', '-r', '-z', '--name-only', rev],
    { encoding: 'utf8', maxBuffer: 256 << 20 }).split('\0').filter(Boolean);
  const wanted = files.filter(f => !isBinaryByExtension(f));
  const res = cp.spawnSync('git', ['-C', root, 'cat-file', '--batch'],
    { input: wanted.map(p => rev + ':' + p).join('\n') + '\n', maxBuffer: 1 << 30 });
  if (res.status !== 0) fail('git cat-file failed on ' + rev);
  const out = res.stdout;
  const blobs = new Map();
  let pos = 0;
  for (const p of wanted) {
    const nl = out.indexOf(10, pos);
    const header = out.slice(pos, nl).toString('utf8');
    const m = / blob (\d+)$/.exec(header);
    if (!m) {
      // a submodule or a symbolic link is not a blob: say so, never skip it silently
      pos = nl + 1;
      blobs.set(p, new Error('not a blob at ' + rev + ': ' + header));
      continue;
    }
    const size = parseInt(m[1], 10);
    blobs.set(p, out.slice(nl + 1, nl + 1 + size));
    pos = nl + 1 + size + 1;
  }
  return {
    files,
    read: (f) => { const b = blobs.get(f); if (b instanceof Error) throw b; return b; },
  };
}

function isBinaryByExtension(file) {
  return BINARY_EXTENSIONS.has(path.extname(file).toLowerCase());
}

// Every folder and file name of the tracked tree, with and without extension:
// a path in a string or in prose that names one of them is a reference.
function pathSegmentsOf(files) {
  const set = new Set();
  for (const f of files) {
    for (const seg of f.split('/')) {
      set.add(seg);
      set.add(seg.replace(/\.[A-Za-z0-9]{1,8}$/, ''));
    }
  }
  return set;
}

function scanTree(root, cfg, only, rev) {
  const src = treeSource(root, rev);
  const files = only ? only : src.files;
  cfg.pathSegments = pathSegmentsOf(src.files);
  const report = { files_listed: files.length, files_read: [], skipped_binary: [], unreadable: [], parse_errors: [], perFile: {}, hits: {}, exceptionReach: {} };
  for (const f of files) {
    // paths: every segment of every tracked path (binary files included)
    const segs = f.split('/');
    const baseNoExt = segs[segs.length - 1].replace(/^\.+/, '').replace(/\.[^.]+$/, '');
    const segments = segs.slice(0, -1).concat(baseNoExt.split('.'));
    const pathHits = [];
    const pexc = exceptionsFor(f, cfg);
    for (const s of segments) {
      if (!s || !isItalianName(s, cfg)) continue;
      const ex = elementException(s, pexc);
      if (ex && ex.kind === 'skip') continue;
      pathHits.push({ category: ex ? 'transition' : 'paths', line: 0, element: s, text: f });
    }
    if (isBinaryByExtension(f)) {
      report.skipped_binary.push(f);
      if (pathHits.length) report.hits[f] = pathHits;
      continue;
    }
    let buf;
    try { buf = src.read(f); }
    catch (err) { report.unreadable.push(f + ': ' + err.message); continue; }
    if (buf === null || buf === undefined) continue; // deleted in the working tree
    const text = decode(buf);
    report.files_read.push(f);
    const a = analyzeText(f, text, cfg);
    if (a.parseError) report.parse_errors.push(f + ': ' + a.parseError);
    for (const [idx, n] of Object.entries(a.reach)) {
      report.exceptionReach[idx] = report.exceptionReach[idx] || {};
      report.exceptionReach[idx][f] = n;
    }
    const all = pathHits.concat(a.hits);
    if (all.length) report.hits[f] = all;
  }
  return summarize(report);
}

// Counted categories add up the Italian WORDS of their hits, per file (an
// element hit that lands in `transition` counts one). Set categories collect
// whole names, repository-wide.
function summarize(report) {
  const totals = Object.fromEntries(CATEGORIES.map(c => [c, 0]));
  const files = {};
  const sets = Object.fromEntries(SET_CATEGORIES.map(c => [c, new Map()]));
  for (const [f, hits] of Object.entries(report.hits)) {
    const counts = {};
    for (const h of hits) {
      if (COUNT_CATEGORIES.includes(h.category)) counts[h.category] = (counts[h.category] || 0) + (h.score || 1);
      else {
        const element = h.element !== undefined && h.element !== null ? h.element : h.text;
        if (!sets[h.category].has(element)) sets[h.category].set(element, f + ':' + h.line);
      }
    }
    if (Object.keys(counts).length) files[f] = counts;
  }
  for (const c of COUNT_CATEGORIES) totals[c] = Object.values(files).reduce((s, x) => s + (x[c] || 0), 0);
  for (const c of SET_CATEGORIES) totals[c] = sets[c].size;
  report.totals = totals;
  report.files = files;
  report.sets = sets;
  return report;
}

function baselineOf(report) {
  const sortedFiles = {};
  for (const f of Object.keys(report.files).sort()) sortedFiles[f] = report.files[f];
  const sets = {};
  for (const c of SET_CATEGORIES) sets[c] = [...report.sets[c].keys()].sort();
  return {
    about: 'Language gate baseline (entry W3.1-bis.1). Written by Controlli/lingua/detect-italian.js --write-baseline. ' +
      'Counted categories are Italian words per file and may not grow in any file; set categories may not gain a new element. ' +
      'A raise needs the trailer "Language-Baseline-Raise: <reason>" on the commit that raises it.',
    format: 2,
    totals: report.totals,
    files: sortedFiles,
    sets,
  };
}

// Merge baselines (the parents of a merge commit): per file the larger count,
// per set the union.
function mergeBaselines(list) {
  if (list.length === 1) return list[0];
  const files = {};
  const sets = {};
  for (const b of list) {
    for (const [f, counts] of Object.entries(b.files || {})) {
      files[f] = files[f] || {};
      for (const [c, n] of Object.entries(counts)) files[f][c] = Math.max(files[f][c] || 0, n);
    }
    for (const c of SET_CATEGORIES) sets[c] = [...new Set((sets[c] || []).concat(b.sets[c] || []))];
  }
  return { files, sets };
}

// Compare a tree (or a newer baseline) with a baseline. Returns the list of
// growths, each { category, file?, from, to, elements? }. `renames` maps a new
// path to its old one (git's rename detection between the two commits): a
// renamed file is compared with the counts of its old path.
function compare(base, current, renames) {
  const growth = [];
  const allFiles = new Set(Object.keys(current.files));
  for (const f of allFiles) {
    const now = current.files[f] || {};
    const old = renames && renames[f] && !base.files[f] ? renames[f] : f;
    const before = base.files[old] || {};
    for (const c of COUNT_CATEGORIES) {
      const a = before[c] || 0, b = now[c] || 0;
      if (b > a) growth.push({ category: c, file: f, from: a, to: b, renamedFrom: old !== f ? old : undefined });
    }
  }
  for (const c of SET_CATEGORIES) {
    const baseSet = new Set(base.sets[c] || []);
    // a name that was a tracked path segment is not new when it shows up as
    // a reference in text after the file was renamed (the old file name
    // quoted in a comment of another file)
    const known = new Set();
    if (c === 'identifiers' || c === 'protocol') {
      for (const seg of base.sets.paths || []) { known.add(seg); const t = []; collectTokens(seg, t); t.forEach(x => known.add(x)); }
    }
    const cur = current.sets[c] instanceof Map ? [...current.sets[c].keys()] : (current.sets[c] || []);
    const fresh = cur.filter(e => !baseSet.has(e) && !known.has(e));
    if (fresh.length) growth.push({ category: c, from: baseSet.size, to: baseSet.size + fresh.length, elements: fresh });
  }
  return growth;
}


function lowered(base, current) {
  // true if the tree is strictly below the baseline somewhere
  for (const f of Object.keys(base.files)) {
    for (const c of COUNT_CATEGORIES) if ((base.files[f][c] || 0) > ((current.files[f] || {})[c] || 0)) return true;
  }
  for (const c of SET_CATEGORIES) {
    const cur = new Set(current.sets[c] instanceof Map ? [...current.sets[c].keys()] : current.sets[c]);
    for (const e of base.sets[c] || []) if (!cur.has(e)) return true;
  }
  return false;
}

// -----------------------------------------------------------------------------
//  Command line
// -----------------------------------------------------------------------------

function parseArgs(argv) {
  const a = { json: false, write: null, baseline: null, file: null, tighten: false, root: null, compareBaselines: null,
    quiet: false, rev: null, configRev: null, renames: null, zero: [] };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    if (x === '--json') a.json = true;
    else if (x === '--write-baseline') { a.write = (argv[i + 1] && !argv[i + 1].startsWith('--')) ? argv[++i] : ''; }
    else if (x === '--baseline') a.baseline = argv[++i];
    else if (x === '--file') a.file = argv[++i];
    else if (x === '--tighten') a.tighten = true;
    else if (x === '--root') a.root = argv[++i];
    else if (x === '--quiet') a.quiet = true;
    else if (x === '--report') a.report = argv[++i];
    else if (x === '--rev') a.rev = argv[++i];
    else if (x === '--config-rev') a.configRev = argv[++i];
    else if (x === '--renames') a.renames = argv[++i];
    else if (x === '--zero') a.zero.push(argv[++i]);
    else if (x === '--compare-baselines') { a.compareBaselines = [argv[i + 1], argv[i + 2]]; i += 2; }
    else if (x === '--help' || x === '-h') { a.help = true; }
    else { a.unknown = x; }
  }
  return a;
}

function printGrowth(growth, report, quiet) {
  // --quiet is for callers that decide themselves (the CI step, a signed
  // raise): the lines stay, the capitals and the verdict go.
  const lead = quiet ? 'language gate: ' : 'LANGUAGE GATE: ';
  for (const g of growth) {
    if (g.file) {
      console.log(lead + g.category + ' grew in ' + g.file + (g.renamedFrom ? ' (renamed from ' + g.renamedFrom + ')' : '') +
        ': ' + g.from + ' -> ' + g.to + ' Italian words');
      // the count is per file, so the gate cannot tell which line is new:
      // it shows the Italian lines of the file, the last ones first
      const hs = ((report && report.hits[g.file]) || []).filter(h => h.category === g.category).slice(-8).reverse();
      if (hs.length) console.log('    Italian lines of the file, last first (the count is per file, not per line):');
      for (const h of hs) console.log('    ' + g.file + ':' + h.line + ': ' + h.text.slice(0, 160));
    } else {
      console.log(lead + 'new Italian element in ' + g.category + ' (' + g.elements.length + '):');
      for (const e of g.elements.slice(0, 30)) {
        const where = report && report.sets[g.category] instanceof Map ? report.sets[g.category].get(e) : '';
        console.log('    ' + e + (where ? '   (' + where + ')' : ''));
      }
      if (g.elements.length > 30) console.log('    ... and ' + (g.elements.length - 30) + ' more');
    }
  }
}

// A baseline is a file, or git:<rev> for the committed baseline of a revision
// (read through git, byte for byte, so no shell decodes it on the way).
// Several revisions joined by "+" (git:A+B) are merged: the parents of a
// merge commit. Returns null, after saying why, when there is none.
function loadBaseline(spec, root) {
  if (spec.startsWith('git:')) {
    const list = [];
    for (const rev of spec.slice(4).split('+')) {
      let buf;
      try {
        buf = cp.execFileSync('git', ['-C', root, 'show', rev + ':Controlli/lingua/baseline.json'],
          { maxBuffer: 256 << 20, stdio: ['ignore', 'pipe', 'ignore'] });
      } catch (err) {
        console.log('language gate: no baseline.json in ' + rev);
        return null;
      }
      list.push(JSON.parse(buf.toString('utf8').replace(/^\ufeff/, '')));
    }
    return mergeBaselines(list);
  }
  const file = path.resolve(spec);
  if (!fs.existsSync(file)) { console.log('language gate: no baseline at ' + file); return null; }
  return readJson(file);
}

// The renames between two commits, as a JSON file { "new/path": "old/path" }
// (written by the CI step from `git diff -M --name-status`).
function loadRenames(file) {
  if (!file) return null;
  const r = readJson(path.resolve(file));
  if (typeof r !== 'object' || Array.isArray(r)) fail('--renames: expected an object { new: old }');
  return r;
}

function writeJson(file, obj) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(obj, null, 1) + '\n', 'utf8');
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    const lines = fs.readFileSync(__filename, 'utf8').split('\n');
    const end = lines.findIndex((l, i) => i > 3 && l.startsWith('// ====='));
    console.log(lines.slice(1, end + 1).join('\n'));
    return 0;
  }
  if (args.unknown) { console.error('detect-italian: unknown argument ' + args.unknown); return 2; }
  const root = path.resolve(args.root || ROOT_DEFAULT);
  const renames = loadRenames(args.renames);

  if (args.compareBaselines) {
    const [a, b] = args.compareBaselines.map(x => loadBaseline(x, root));
    if (!a || !b) return 4;
    const growth = compare(a, b, renames);
    if (growth.length) {
      console.log((args.quiet ? 'language gate: ' : 'LANGUAGE GATE: ') + 'the baseline itself was raised:');
      printGrowth(growth, null, args.quiet);
      return 1;
    }
    console.log('language gate: the new baseline is not above the old one.');
    return 0;
  }

  const cfg = loadConfig(root, args.configRev);
  const report = scanTree(root, cfg, args.file ? [args.file.replace(/\\/g, '/')] : null, args.rev);

  if (report.unreadable.length) {
    console.log('LANGUAGE GATE: tracked files that cannot be read (the gate would be blind on them):');
    report.unreadable.forEach(u => console.log('    ' + u));
    return 2;
  }
  // how far each exception reaches can only be judged on the whole tree
  if (!args.file) checkExceptionReach(report, cfg);

  if (args.json) {
    const out = baselineOf(report);
    out.files_listed = report.files_listed;
    out.files_read = report.files_read;
    out.skipped_binary = report.skipped_binary;
    out.parse_errors = report.parse_errors;
    out.exception_reach = report.exceptionReach;
    out.hits = report.hits;
    process.stdout.write(JSON.stringify(out, null, 1) + '\n');
    return 0;
  }

  if (args.file) {
    const hs = report.hits[report.files_read[0] || args.file] || [];
    for (const h of hs) console.log(h.category.padEnd(12) + ' ' + h.line + ': ' + (h.element ? '[' + h.element + '] ' : '') +
      (h.score ? '(' + h.score + ') ' : '') + h.text.slice(0, 160));
    console.log(report.files_read.length + ' file read, ' + hs.length + ' hits');
    return 0;
  }

  const baselineFile = path.join(root, LANG_DIR, 'baseline.json');
  if (args.write !== null) {
    if (args.rev) { console.log('LANGUAGE GATE: --write-baseline works on the working tree, not on --rev'); return 2; }
    const target = args.write ? path.resolve(args.write) : baselineFile;
    writeJson(target, baselineOf(report));
    console.log('language gate: baseline written to ' + target);
    printTotals(report);
    return 0;
  }

  // --zero <category>: the category must be empty in the whole tree (the
  // transition category at a release tag).
  for (const c of args.zero) {
    if (!CATEGORIES.includes(c)) { console.log('LANGUAGE GATE: --zero: unknown category ' + c); return 2; }
    if (report.totals[c] > 0) {
      console.log('LANGUAGE GATE: ' + c + ' must be 0 here and is ' + report.totals[c] + ':');
      for (const [f, hs] of Object.entries(report.hits)) {
        for (const h of hs.filter(x => x.category === c).slice(0, 5)) console.log('    ' + f + ':' + h.line + ': ' + h.text.slice(0, 160));
      }
      return 1;
    }
  }

  const against = args.baseline || baselineFile;
  const base = loadBaseline(against, root);
  if (!base) return 4;
  const growth = compare(base, report, renames);
  if (args.report) writeJson(path.resolve(args.report), {
    growth, totals: report.totals, files_read: report.files_read, skipped_binary: report.skipped_binary,
  });
  if (!args.quiet) printTotals(report, base);
  if (growth.length) {
    printGrowth(growth, report, args.quiet);
    if (args.quiet) return 1;
    console.log('');
    console.log('LANGUAGE GATE: FAILED. New text is written in English. If this growth is');
    console.log('intended (a rename window, an approved exception), rewrite the baseline with');
    console.log('--write-baseline and add the trailer "Language-Baseline-Raise: <reason>" to the');
    console.log('message of that commit; the reason goes into the CHANGELOG entry too.');
    return 1;
  }
  if (args.tighten && lowered(base, report)) {
    if (args.rev || against.startsWith('git:')) {
      console.log('language gate: the Italian went DOWN at ' + (args.rev || 'the working tree') +
        '; run node Controlli/lingua/detect-italian.js --tighten on the working tree and commit the baseline.');
      return 3;
    }
    writeJson(path.resolve(against), baselineOf(report));
    console.log('language gate: the tree went DOWN; ' + against + ' was rewritten. Commit it.');
    return 3;
  }
  console.log('language gate: nothing grew (' + report.files_read.length + ' files read, ' +
    report.skipped_binary.length + ' binary by extension).');
  return 0;
}


function printTotals(report, base) {
  const parts = CATEGORIES.map(c => c + '=' + report.totals[c] + (base && base.totals ? '/' + base.totals[c] : ''));
  console.log('language gate totals' + (base ? ' (tree/baseline)' : '') + ': ' + parts.join(' '));
}

module.exports = { loadConfig, analyzeText, scanTree, baselineOf, compare, lowered, splitName, isItalianName,
  proseVerdict, proseScore, listFiles, treeSource, isBinaryByExtension, decode, kindOf, parseJsonItems, pathSegmentsOf,
  mergeBaselines, checkExceptionReach,
  lexers: { lexCLike, lexPowerShell, lexHash, lexIgnore, lexYaml, lexXml, lexMarkdown },
  CATEGORIES, COUNT_CATEGORIES, SET_CATEGORIES, BINARY_EXTENSIONS };

if (require.main === module) {
  let code;
  try { code = main(); }
  catch (err) {
    if (err.gate) { console.log('LANGUAGE GATE: cannot run: ' + err.message); code = 2; }
    else { console.log('LANGUAGE GATE: crashed: ' + (err.stack || err)); code = 2; }
  }
  process.exitCode = code;
}
