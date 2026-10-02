#!/usr/bin/env node
// =============================================================================
//  window-2-surface.js - rename window 2 of the translation: the MCP surface
//  (entry W3.1-bis.5)
// =============================================================================
//
//  WHAT IT DOES
//
//  Driven by Controlli/lingua/name-map.tsv (types tool, sub_action, operation,
//  catalogue_family, argument, enum_value, catalogue_key, response_field,
//  response_value and selector), it renames the surface a client of the
//  bridge saves and calls again:
//
//    - Strumenti/catalogo.json, token by token: its keys, the families, the
//      operation names, every schema property, required name, enum value and
//      default, the precondition and document values, the example (a JSON
//      text inside a string, rewritten the same way), and the code names a
//      description quotes. The catalogue is a product of
//      Riscontro.Disegno/Catalogo/Operazioni.cs: the C# pass below renames
//      the source, and Riscontro.Catalogo.exe must write back exactly what
//      this pass wrote (`--check` compares them);
//    - every bench case and plan (Banco/cases, Banco/pending-cases): the
//      operation of each step, the keys and values of its arguments (an enum
//      value is renamed by the argument it belongs to, a selector by the
//      selector rows, a value the case chose and compares by the
//      response_value rows), the response path of each expectation and the
//      values it compares, and the plan keys of a plan file;
//    - the C#, JavaScript and PowerShell sources: string literals whose whole
//      content is a name of the map (by its code context when the map gives
//      the word two English names), dotted paths and selectors, the plan and
//      catalogue keys the JavaScript and PowerShell readers use as members,
//      and the code names quoted inside messages and comments;
//    - the documents (*.md) and any other tracked text: only code names.
//
//  WHERE A NAME IS RENAMED IN PROSE. Italian prose stays Italian (decision
//  D10 of the entry W3.1-bis): a common word used as a name (asola, guscio,
//  specchia) is never replaced as a word. The map is applied only
//    - to a span between backticks, guillemets or double quotes whose whole
//      content is a code name (`profondita_mm`, «nuovo», "piano": "frontale",
//      `@faccia(verso=alto)`, `misure.fori`, «prova: true»);
//    - to an exact snake_case token outside such spans (profondita_mm);
//    - to a selector (@faccia(...)) anywhere.
//  The names an Italian SOLIDWORKS writes (the class solidworks-localized of
//  Controlli/lingua/exceptions.json: Schizzo1, Taglio-Estrusione2, «Piano
//  frontale») are masked first and never touched.
//
//  A JSON file is rewritten token by token (the method of window 1): only
//  the strings that change are replaced, so indentation, line ends, key order
//  and the hand-made layout of each case stay byte for byte. Every other file
//  is rewritten inside its string literals, comments and prose only: a line
//  that carries the marker `window-2: keep` is never rewritten, in either
//  direction.
//
//  A second forward run on its own output does NOT run: it stops with exit 2
//  (the catalogue already says «operations»), because it would read the
//  English names as the names before the window and write a ledger of
//  nonsense; with --dry-run too. The second run is --check: it writes
//  nothing, runs the window in memory and says which files a forward run
//  would still change (none, on the tree after the window). And the window
//  runs backwards (--reverse): the map read from English to Italian. The proof
//  of the window is that the reverse run on the new tree gives back the
//  catalogue and every case and plan of the old tree, byte for byte after
//  JSON normalization (--reverse-proof <rev>).
//
//  THE TABLES BELOW, AND WHY THEY ARE HERE AND NOT IN THE MAP. The map holds
//  one English name per Italian name and type; its notes say per site where
//  a second sense applies (a_mm, formato, misure), and no script reads notes.
//  The sites are written here, each with the note it comes from, and the
//  forward run checks that each table still matches the tree.
//
//  USAGE
//    node Controlli/lingua/window-2-surface.js             the window, on this repository
//    node Controlli/lingua/window-2-surface.js --reverse   the window backwards
//    node Controlli/lingua/window-2-surface.js --check
//        writes nothing: is the tree after the window? Every Italian name of
//        the window still in the catalogue, the cases, the plans and the code
//        literals; the catalogue written by the C# against the catalogue this
//        script would write; the alias dictionary. Exit 1 if anything is left.
//    node Controlli/lingua/window-2-surface.js --reverse-proof <rev>
//        extracts HEAD into a temporary folder, runs --reverse there, and
//        compares the catalogue and the cases and plans of <rev> with what
//        came back: same paths, same bytes, same JSON.
//    node Controlli/lingua/window-2-surface.js --forward-proof <rev>
//        the other way: extracts <rev>, runs the window there, and compares
//        the catalogue and the cases and plans with HEAD.
//    node Controlli/lingua/window-2-surface.js --todo <TODO.md>
//        only that file, outside its closed entries and outside the entry
//        W3.1-bis (the rename itself, whose tables quote both names): code
//        names only (D10).
//    node Controlli/lingua/window-2-surface.js --notes <file|folder>...
//        the same rewrite on explicit notes (memories, RAG notes, CLAUDE.md,
//        INSTALL-FOR-AI.md): code names only. Used by
//        Controlli/lingua/update-names-in-notes.js. In both, a line that
//        already names the English name it would write is left whole (a
//        table of the rename), and a name with two English names (`misure`,
//        `piano`, `passo`, `quota` alone) or of another subject (`modello`)
//        is left as it is; --list prints both. --only-mentioning <regex>
//        skips the notes whose text does not match (the RAG holds the notes
//        of OpenPDM too, whose `stato` or `errore` are not the bridge's).
//    options:
//      --root <dir>     another tree (default: the repository of this file)
//      --no-git         a walk of the file system instead of git ls-files
//      --dry-run        say what would change, write nothing
//      --list           print every file rewritten and every name left alone
//      --worktree       the proofs read the working tree instead of HEAD
//      --allow-conflicts, --dump <file>   (to write the tables) go on past a
//                       literal with two senses, and write every such site and
//                       every file rewritten into <file>
//
//  ALSO, AFTER A FORWARD RUN: Controlli/lingua/window-2-reverse.json, the
//  ledger of the reverse run (see below), and the consumed strings of
//  Controlli/lingua/consumed-strings.json whose consumer literal the window
//  renamed, bound again to their new spelling (consumed-strings.js --rebind,
//  done here).
//
//  EXIT CODES
//    0  done (or nothing to do), --check clean, a proof identical
//    1  --check found Italian names of the window, or a proof differs
//    2  cannot run: a bad map, an unresolved name with two senses, a table
//       that does not match the tree, a JSON file that does not parse
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const MAP_REL = 'Controlli/lingua/name-map.tsv';
const EXCEPTIONS_REL = 'Controlli/lingua/exceptions.json';
const CATALOGUE_REL = 'Strumenti/catalogo.json';
const CASE_KEYS_REL = 'Banco/case-keys.json';
const CASE_TOPS = ['Banco/cases', 'Banco/pending-cases'];
const TASKS_DIR = 'Banco/tasks';
const OPERATIONS_CS = 'Riscontro.Disegno/Catalogo/Operazioni.cs';
const KEEP_MARK = 'window-2: keep';

const TYPES = ['tool', 'sub_action', 'operation', 'catalogue_family', 'argument', 'enum_value', 'catalogue_key',
  'response_field', 'response_value', 'selector'];

// Files that keep their Italian on purpose, or history: never rewritten.
const EXCLUDED = new Set([
  'CHANGELOG.md',                                // history is not rewritten; the window adds its own entry
  'Controlli/lingua/name-map.tsv',               // the map: Italian on purpose
  'Controlli/lingua/name-map-extract.js',        // its extractor: Italian on purpose
  'Controlli/lingua/baseline.json',              // rewritten by detect-italian.js --write-baseline
  'Controlli/lingua/lexicon.json',               // the Italian words the gate looks for
  'Controlli/lingua/exceptions.json',            // the patterns name the Italian they cover
  'Controlli/lingua/detect-italian.js',          // the gate: its exception classes are not protocol
  'Riscontro.Disegno/Alias.cs',                  // the aliases (W3.1-bis.5, D4): they name the old names
  'Prove/AliasLayerContract.cs',                 // and their contract
  '.github/passi/language-gate.ps1',             // (`quote` is a class there, not the family)
  'Controlli/lingua/measure-labels.json',        // labelled Italian samples of the gate's measure
  'Controlli/lingua/measure-labels-blind.json',
  'Controlli/lingua/measure-labels-blind-english.json',
  'Controlli/lingua/measure-labels-stress.json',
  'Controlli/lingua/consumed-strings.json',      // hashes, not names
  'Controlli/lingua/window-1-cases.js',          // window 1: it names the Italian keys it renamed
  'Controlli/lingua/window-2-surface.js',        // this file
  'Controlli/lingua/update-names-in-notes.js',   // its driver for the notes outside the repository
  'Banco/tasks/score-autonomous.js',             // reads the protocol the bridge of before speaks (W3.1-bis.5, review finding 1): it names the old names on purpose
]);
// The D9 tasks keep the words a person types: only the measurement paths of
// their JSON (come_si_misura[].cosa) are renamed, never the prompt.
const TASK_PATH_KEY = 'cosa';

// ── tables of sites (each from a note of the map) ─────────────────────────
// ARGUMENT_BY_OPERATION: an argument whose English name depends on the
// operation (Italian names). Map note of `a_mm`: «at catalogo.json:2499 (the
// weld symbol of a drawing) ... W3.1-bis.5 renames that one site to
// throat_mm; ... section_view ... W3.1-bis.5 renames that site to offset_mm,
// and the cases of section_view with it».
const ARGUMENT_BY_OPERATION = {
  saldatura_in_tavola: { a_mm: 'throat_mm' },
  sezione_in_tavola: { a_mm: 'offset_mm' },
};
// FIELD_BY_OPERATION: a response field whose English name depends on the
// operation that writes it. Map note of `formato`: «the paper size echoed by
// create_drawing and set_sheet (OperazioniTavola.cs:144,234) is paper_size, as
// the argument; the sheet format name of the template (OperazioniCartiglio.cs:134)
// is sheet_format».
const FIELD_BY_OPERATION = {
  crea_disegno: { formato: 'paper_size' },
  imposta_foglio: { formato: 'paper_size' },
  cartiglio: { formato: 'sheet_format' },
};
// FIELD_INSIDE: a response field whose English name depends on the field it
// sits in. Map note of `misure`: «the inner key of the frame answer
// (OperazioniSaldatura.cs:836,2808 ...) is the echo of the frame argument,
// measured_as».
const FIELD_INSIDE = { misure: { misure: 'measured_as' } };
// FACING: the values of the face field `verso` of the document state, which
// the selector `@faccia(verso=...)` compares. The selector rows of the map
// name them (verso=alto -> normal=up ...); the state writes them and the
// bench reads them, so producer and readers take the selector's English.
// They are values, not an enum of the catalogue: lato=alto is `top`.
const FACING = { alto: 'up', basso: 'down', destra: 'right', sinistra: 'left', davanti: 'front', dietro: 'back' };
// The selector field whose state field is `verso`: the bench translates
// `normal` (the selector) to `facing` (the state field) in one place.
// The arguments whose values are facings: accoppia names a face by where it
// looks (faccia_a: "alto"), with the vocabulary of the state.
const FACING_ARGUMENTS = ['faccia_a', 'faccia_b'];
const SUFFIXES = { _rif: '_ref', _gradi: '_deg' };
// Response values merged by the map (two Italian forms, one English word):
// the reverse run needs to know which form each site had. Default first.
const MERGED_REVERSE = {
  response_field: { how_many: 'quanti', attached_to_edge: 'attaccato_allo_spigolo' },
  response_value: { outer: 'esterno', inner: 'interno' },
};

// ── small helpers ─────────────────────────────────────────────────────────
class Stop extends Error {}
function cannot(msg) { throw new Stop(msg); }
function escapeRx(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }
function isObject(v) { return v !== null && typeof v === 'object' && !Array.isArray(v); }
function eolOf(text) { return text.includes('\r\n') ? '\r\n' : '\n'; }

function parseArgs(argv) {
  const a = { reverse: false, root: ROOT_DEFAULT, git: true, dry: false, todo: null, notes: null, check: false,
    proof: null, forwardProof: null, list: false };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    if (x === '--reverse') a.reverse = true;
    else if (x === '--root') a.root = path.resolve(argv[++i] || cannot('--root needs a folder'));
    else if (x === '--no-git') a.git = false;
    else if (x === '--dry-run') a.dry = true;
    else if (x === '--list') a.list = true;
    else if (x === '--worktree') a.worktree = true;
    else if (x === '--allow-conflicts') a.allowConflicts = true;
    else if (x === '--dump') a.dump = path.resolve(argv[++i] || cannot('--dump needs a file'));
    else if (x === '--todo') a.todo = path.resolve(argv[++i] || cannot('--todo needs a file'));
    else if (x === '--notes') { a.notes = []; while (argv[i + 1] && !argv[i + 1].startsWith('--')) a.notes.push(path.resolve(argv[++i])); if (!a.notes.length) cannot('--notes needs at least one path'); }
    else if (x === '--only-mentioning') { const r = argv[++i] || cannot('--only-mentioning needs a regular expression'); try { a.onlyMentioning = new RegExp(r, 'i'); } catch (e) { cannot('--only-mentioning: ' + e.message); } }
    else if (x === '--check') a.check = true;
    else if (x === '--reverse-proof') a.proof = argv[++i] || cannot('--reverse-proof needs a revision');
    else if (x === '--forward-proof') a.forwardProof = argv[++i] || cannot('--forward-proof needs a revision');
    else if (x === '--help' || x === '-h') a.help = true;
    else cannot('unknown argument ' + x);
  }
  return a;
}

// ── the map ───────────────────────────────────────────────────────────────
function loadMap(root) {
  const file = path.join(root, MAP_REL);
  if (!fs.existsSync(file)) cannot('no name map at ' + file);
  const m = {};
  for (const t of TYPES) m[t] = new Map();
  for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    if (!line || line.startsWith('#')) continue;
    const c = line.split('\t');
    if (c[0] === 'type' || !m[c[0]]) continue;
    if (!c[1] || !c[2]) cannot('a row of type ' + c[0] + ' without its English name: ' + line);
    if (m[c[0]].has(c[1])) cannot('the map has ' + c[0] + ' «' + c[1] + '» twice');
    m[c[0]].set(c[1], c[2]);
  }
  return m;
}

function invert(type, map, merged) {
  const inv = new Map();
  const seen = new Map();
  for (const [it, en] of map) {
    if (seen.has(en) && seen.get(en) !== it) {
      const pick = merged && merged[en];
      if (!pick) cannot('the ' + type + ' rows «' + seen.get(en) + '» and «' + it + '» share the English name «' + en + '»: the window would not come back (add it to MERGED_REVERSE)');
      inv.set(en, pick);
      continue;
    }
    seen.set(en, it);
    inv.set(en, it);
  }
  return inv;
}

// One direction of the window: every table read from the source language of
// the run to its target language.
function direction(map, reverse, catEnums) {
  const d = { reverse };
  const pick = (t) => (reverse ? invert(t, map[t], MERGED_REVERSE[t]) : new Map(map[t]));
  d.tool = pick('tool');
  d.sub = pick('sub_action');
  d.op = pick('operation');
  // the Italian name of an operation from either language: the tables of
  // sites are keyed by it
  d.opItalian = (op) => (op && map.operation.has(op) ? op : [...map.operation].find(([, en]) => en === op)?.[0] || op);
  d.family = pick('catalogue_family');
  d.arg = pick('argument');
  d.ckey = pick('catalogue_key');
  d.field = pick('response_field');
  d.rvalue = pick('response_value');
  // enum values, by the argument they belong to (both in the source language)
  d.enumByArg = new Map();
  const enumRows = reverse ? [...map.enum_value].map(([a, b]) => [b, a]) : [...map.enum_value];
  for (const [src, dst] of enumRows) {
    const [sa, sv] = src.split('=');
    const [, dv] = dst.split('=');
    if (!d.enumByArg.has(sa)) d.enumByArg.set(sa, new Map());
    const m = d.enumByArg.get(sa);
    if (m.has(sv) && m.get(sv) !== dv) cannot('the enum value ' + src + ' has two targets');
    m.set(sv, dv);
  }
  // selectors: kinds (@faccia), fields (verso) and field=value pairs
  d.selKind = new Map(); d.selField = new Map(); d.selPair = new Map();
  for (const [it, en] of map.selector) {
    const [s, t] = reverse ? [en, it] : [it, en];
    if (s.startsWith('@')) d.selKind.set(s, t);
    else if (s.includes('=')) d.selPair.set(s, t);
    else d.selField.set(s, t);
  }
  // arguments that depend on the operation
  d.argByOp = new Map();
  for (const [op, m] of Object.entries(ARGUMENT_BY_OPERATION)) {
    const opS = reverse ? map.operation.get(op) : op;
    const mm = new Map();
    for (const [a, en] of Object.entries(m)) mm.set(reverse ? en : a, reverse ? a : en);
    d.argByOp.set(opS, mm);
  }
  d.fieldByOp = new Map();
  for (const [op, m] of Object.entries(FIELD_BY_OPERATION)) {
    const opS = reverse ? map.operation.get(op) : op;
    const mm = new Map();
    for (const [f, en] of Object.entries(m)) mm.set(reverse ? en : f, reverse ? f : en);
    d.fieldByOp.set(opS, mm);
  }
  d.fieldInside = new Map();
  for (const [outer, m] of Object.entries(FIELD_INSIDE)) {
    const o = reverse ? map.response_field.get(outer) : outer;
    const mm = new Map();
    for (const [f, en] of Object.entries(m)) mm.set(reverse ? en : f, reverse ? f : en);
    d.fieldInside.set(o, mm);
  }
  d.facing = new Map(Object.entries(FACING).map(([a, b]) => (reverse ? [b, a] : [a, b])));
  // the arguments whose values are facings (accoppia: faccia_a, faccia_b)
  d.facingArgs = FACING_ARGUMENTS.map(it => (reverse ? map.argument.get(it) : it));
  for (const a of d.facingArgs) d.enumByArg.set(a, d.facing);
  d.suffix = new Map(Object.entries(SUFFIXES).map(([a, b]) => (reverse ? [b, a] : [a, b])));
  // prefixes: X of the argument pairs X_uid / X_rif, when both agree
  d.prefix = new Map();
  for (const [it, en] of map.argument) {
    const m = /^(.+)_(uid|rif)$/.exec(it);
    if (!m) continue;
    const e = /^(.+)_(uid|ref)$/.exec(en);
    if (!e) continue;
    const [s, t] = reverse ? [e[1], m[1]] : [m[1], e[1]];
    if (map.argument.has(m[1]) || d.prefix.has(s) && d.prefix.get(s) !== t) continue;
    d.prefix.set(s, t);
  }
  // the universe of single names, for a literal or a span whose whole
  // content is a name: every sense the map gives it
  d.universe = new Map();
  const add = (s, type, t) => { if (!d.universe.has(s)) d.universe.set(s, []); d.universe.get(s).push({ type, to: t }); };
  for (const [s, t] of d.tool) add(s, 'tool', t);
  for (const [s, t] of d.sub) add(s, 'sub_action', t);
  for (const [s, t] of d.op) add(s, 'operation', t);
  for (const [s, t] of d.family) add(s, 'catalogue_family', t);
  for (const [s, t] of d.arg) add(s, 'argument', t);
  for (const [s, t] of d.ckey) add(s, 'catalogue_key', t);
  for (const [s, t] of d.field) add(s, 'response_field', t);
  for (const [s, t] of d.rvalue) add(s, 'response_value', t);
  for (const [a, m] of d.enumByArg) for (const [s, t] of m) add(s, 'enum:' + a, t);
  for (const [s, t] of d.selKind) add(s, 'selector_kind', t);
  for (const [s, t] of d.selField) add(s, 'selector_field', t);
  for (const [s, t] of d.facing) add(s, 'facing', t);
  for (const [s, t] of d.prefix) if (!d.universe.has(s) && /[0-9_]/.test(s)) add(s, 'prefix', t);
  for (const [s, t] of d.suffix) add(s, 'suffix', t);
  // a value of an enum of the catalogue that the map leaves as it is (`fine`,
  // a quality of esporta_stl) is a second sense of the same word: without the
  // argument in sight, nothing can say which one a literal is
  if (catEnums) {
    for (const [a, vals] of catEnums) {
      const m = d.enumByArg.get(a);
      for (const v of vals) if (!(m && m.has(v)) && d.universe.has(v) && !d.universe.get(v).some(x => x.type === 'enum:' + a)) add(v, 'enum:' + a, v);
    }
  }
  return d;
}

// The enums of the catalogue of a tree, by argument (in the language of
// that tree): { arg -> Set of values }.
function catalogueEnums(root) {
  const file = path.join(root, CATALOGUE_REL);
  if (!fs.existsSync(file)) return null;
  const c = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\ufeff/, ''));
  const ops = c.operazioni || c.operations || [];
  const out = new Map();
  for (const o of ops) for (const [a, vals] of opContext(o.schema).enums) {
    if (!out.has(a)) out.set(a, new Set());
    for (const v of vals) out.get(a).add(v);
  }
  return out;
}

// The arguments of an operation and the values of its enums, from its schema
// (in the language of the schema).
function opContext(schema) {
  const args = new Set();
  const enums = new Map();
  const walk = (s, prop) => {
    if (!isObject(s)) return;
    if (Array.isArray(s.enum) && prop) { if (!enums.has(prop)) enums.set(prop, new Set()); for (const v of s.enum) if (typeof v === 'string') enums.get(prop).add(v); }
    if (isObject(s.properties)) for (const [k, v] of Object.entries(s.properties)) { args.add(k); walk(v, k); }
    if (isObject(s.items)) walk(s.items, prop);
  };
  walk(schema, null);
  return { args, enums };
}

// The English of a name when every sense agrees; CONFLICT when they do not;
// null when the name is not in the window.
const CONFLICT = { conflict: true };
function unanimous(d, s) {
  const l = d.universe.get(s);
  if (!l) return null;
  const to = new Set(l.map(x => x.to));
  if (to.size === 1) return l[0].to;
  return CONFLICT;
}
function sensesOf(d, s) { return (d.universe.get(s) || []).map(x => x.type + '=' + x.to).join(', '); }

// A name in a given sense. `sense` is one of: argument, response_field,
// selector_field, facing, enum:<arg>, operation, tool, catalogue_family,
// catalogue_key, response_value, sub_action.
function inSense(d, s, sense, op) {
  if (sense === 'argument') {
    if (op && d.argByOp.has(op) && d.argByOp.get(op).has(s)) return d.argByOp.get(op).get(s);
    return d.arg.get(s) || null;
  }
  if (sense === 'response_field') return d.field.get(s) || null;
  if (sense === 'selector_field') return d.selField.get(s) || null;
  if (sense === 'facing') return d.facing.get(s) || null;
  if (sense === 'operation') return d.op.get(s) || null;
  if (sense === 'tool') return d.tool.get(s) || null;
  if (sense === 'sub_action') return d.sub.get(s) || null;
  if (sense === 'catalogue_family') return d.family.get(s) || null;
  if (sense === 'catalogue_key') return d.ckey.get(s) || null;
  if (sense === 'response_value') return d.rvalue.get(s) || null;
  if (sense.startsWith('enum:')) { const m = d.enumByArg.get(sense.slice(5)); return (m && m.get(s)) || null; }
  cannot('internal: unknown sense ' + sense);
}

// ── the protected names of an Italian SOLIDWORKS ─────────────────────────
let PROTECTED = null;
function protectedPatterns(root) {
  if (PROTECTED) return PROTECTED;
  const file = path.join(root, EXCEPTIONS_REL);
  const list = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\ufeff/, ''));
  PROTECTED = list.filter(e => e.class === 'solidworks-localized' && e.pattern).map(e => ({
    file: e.file ? new RegExp(e.file) : null, rx: new RegExp(e.pattern, 'g'),
  }));
  return PROTECTED;
}
// Ranges of a text that are names of an Italian SOLIDWORKS.
function protectedRanges(root, rel, text) {
  const out = [];
  for (const p of protectedPatterns(root)) {
    if (p.file && !p.file.test(rel)) continue;
    p.rx.lastIndex = 0;
    let m;
    while ((m = p.rx.exec(text))) { if (m[0].length === 0) { p.rx.lastIndex++; continue; } out.push([m.index, m.index + m[0].length]); }
  }
  return out;
}
function insideAny(ranges, a, b) { return ranges.some(([x, y]) => a < y && b > x); }

// ── names inside text: selectors, paths, spans ───────────────────────────
// A selector: @kind(field=value, field~=value, n=2). The value of a field
// is renamed only by a selector row (verso=alto -> normal=up).
function rewriteSelector(d, s) {
  const m = /^@([A-Za-z_]+)(?:\(([\s\S]*)\))?$/.exec(s);
  if (!m) return null;
  const kind = '@' + m[1];
  const kTo = d.selKind.get(kind) || kind;
  if (m[2] === undefined) return kTo === kind ? null : kTo;
  // split on commas at depth 0 (a body name carries its own parentheses)
  const parts = [];
  let depth = 0, cur = '';
  for (const ch of m[2]) {
    if (ch === '(') depth++;
    if (ch === ')') depth--;
    if (ch === ',' && depth === 0) { parts.push(cur); cur = ''; continue; }
    cur += ch;
  }
  parts.push(cur);
  const out = parts.map((p) => {
    const c = /^(\s*)([A-Za-z_][A-Za-z0-9_]*)(\s*~?=\s*)([\s\S]*)$/.exec(p);
    if (!c) return p;
    const f = c[2];
    const fTo = d.selField.get(f) || f;
    let v = c[4];
    const pair = d.selPair.get(f + '=' + v.trim());
    if (pair) v = v.replace(v.trim(), pair.split('=')[1]);
    return c[1] + fTo + c[3] + v;
  });
  const res = kTo + '(' + out.join(',') + ')';
  return res === s ? null : res;
}

// A response path of an expectation: `misure.fori`, `facce[0].verso`,
// `stato.proprieta.Descrizione`. Each segment is a response field; the first
// may depend on the operation (FIELD_BY_OPERATION applies to the segment
// right under `misure`, where the operation writes), and a segment may
// depend on the field it sits in (FIELD_INSIDE).
function rewritePath(d, p, op) {
  if (typeof p !== 'string' || p === '') return p;
  const segs = p.split('.');
  let prev = null;
  const out = segs.map((seg, i) => {
    const m = /^([^[\]]*)((?:\[\d+\])*)$/.exec(seg);
    if (!m) return seg;
    const name = m[1];
    let to = null;
    if (prev !== null && d.fieldInside.has(prev) && d.fieldInside.get(prev).has(name)) to = d.fieldInside.get(prev).get(name);
    else if (op && d.fieldByOp.has(op) && d.fieldByOp.get(op).has(name) && i > 0) to = d.fieldByOp.get(op).get(name);
    else if (d.field.has(name)) to = d.field.get(name);
    prev = name;
    return (to || name) + m[2];
  });
  return out.join('.');
}

// A value an expectation compares, or a value of an argument: a selector, an
// enum value of the argument (or field) it belongs to, a facing value, or a
// value the case chose and compares (response_value).
function rewriteValue(d, v, key, op, where) {
  if (typeof v !== 'string') return v;
  if (v.startsWith('@')) { const s = rewriteSelector(d, v); return s === null ? v : s; }
  if (key) {
    const ka = key.replace(/\[\d+\]$/, '');
    const m = d.enumByArg.get(ka);
    if (m && m.has(v)) return m.get(v);
    if ((ka === 'verso' || ka === 'facing' && d.reverse) && where === 'state' && d.facing.has(v)) return d.facing.get(v);
  }
  if (d.rvalue.has(v)) return d.rvalue.get(v);
  return v;
}

// The name expressions a span of prose may hold, whole.
const NAME = '[A-Za-z_][A-Za-z0-9_]*';
const RX_NAME = new RegExp('^' + NAME + '$');
const RX_PREFIX = new RegExp('^[a-z][A-Za-z0-9_]*(?:\\.' + NAME + ')*\\.$');
const RX_DOTTED = new RegExp('^' + NAME + '(?:\\[\\d+\\])*(?:\\.' + NAME + '(?:\\[\\d+\\])*)+$');
const RX_ASSIGN = new RegExp('^(' + NAME + ')=([^\\s=,()]+)$');
const RX_COLON = new RegExp('^(' + NAME + ')(\\s*:\\s*)(.+)$');
const FILE_EXT = /\.(json|cs|js|md|exe|dll|ps1|tsv|csproj|txt|xml|yml|sldprt|sldasm|slddrw|sldlfp|step|stp|pdf|dxf|stl|igs|log)$/i;

// A whole name expression, renamed; null when it is not one, or not in the
// window. ctx = { op, args: Set of the arguments of the operation in
// context, sense: a default sense for a name with two English names,
// file, conflicts: [] }.
function rewriteExpr(d, expr, ctx) {
  if (expr.startsWith('@')) return rewriteSelector(d, expr);
  // a path prefix: `misure.`, `stato.…`
  const pm = /^([a-z][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)(\.(?:\u2026|\.\.)?)$/.exec(expr);
  if (pm) { const r = rewritePath(d, pm[1], ctx.op); return r === pm[1] ? null : r + pm[2]; }
  if (RX_NAME.test(expr)) return rewriteName(d, expr, ctx);
  let m = RX_ASSIGN.exec(expr);
  if (m) {
    const pair = d.selPair.get(expr);
    if (pair) return pair;
    const em = d.enumByArg.get(m[1]);
    if (em && em.has(m[2])) return (inSense(d, m[1], 'argument', ctx.op) || m[1]) + '=' + em.get(m[2]);
    const f = d.selField.get(m[1]);
    if (f) return f + '=' + m[2];
    // name=value with a value the map does not know (dove=... of a CI
    // output, x=1): not a name of the window
    return null;
  }
  m = RX_COLON.exec(expr);
  if (m && !/\s/.test(m[1])) {
    const k = rewriteName(d, m[1], Object.assign({}, ctx, d.arg.has(m[1]) ? { sense: 'argument' } : {}));
    let v = m[3];
    const q = /^"([^"]*)"$/.exec(v.trim());
    const bare = q ? q[1] : v.trim();
    const em = d.enumByArg.get(m[1]);
    let vTo = null;
    if (/^\s*\{/.test(v)) { const o = rewriteObjectish(d, v, ctx); if (o !== v) vTo = o; if (vTo !== null) { return (k || m[1]) + m[2] + vTo; } }
    if (em && em.has(bare)) vTo = em.get(bare);
    else if (q && RX_NAME.test(bare)) { const u = rewriteName(d, bare, Object.assign({}, ctx, { sense: undefined, args: null, enums: null })); if (u !== null && (d.universe.get(bare) || []).some(x => x.type === 'sub_action' || x.type === 'response_value' || x.type.startsWith('enum:') || x.type === 'operation' || x.type === 'tool')) vTo = u; }
    if (vTo !== null) v = v.replace(bare, vTo);
    if (k === null && vTo === null) return null;
    return (k || m[1]) + m[2] + v;
  }
  if (RX_DOTTED.test(expr) && !FILE_EXT.test(expr) && !/^[A-Z]/.test(expr)) {
    const segs = expr.split('.');
    // operation.argument (the decisions of ProveICampiObbligatori, the
    // anchors of the catalogue)
    const first = segs[0].replace(/\[\d+\]$/, '');
    if (segs.length === 2 && d.op.has(first)) {
      const a = segs[1];
      const aTo = inSense(d, a, 'argument', first);
      return d.op.get(first) + '.' + (aTo || a);
    }
    // an argument path (faccia_rif.passo) or a response path (misure.fori)
    const allArgs = segs.every(s => d.arg.has(s.replace(/\[\d+\]$/, '')) || /^\d+$/.test(s));
    if (ctx.sense === 'argument' && allArgs) return segs.map(s => { const b = s.replace(/\[\d+\]$/, ''); return (d.arg.get(b) || b) + s.slice(b.length); }).join('.');
    const r = rewritePath(d, expr, ctx.op);
    return r === expr ? null : r;
  }
  return null;
}

// An object written inline in prose: {piano: "destro", spessore_mm: 5}. Its
// keys are arguments, a quoted value is renamed by the key it belongs to.
function rewriteObjectish(d, text, ctx) {
  return text.replace(/([A-Za-z_][A-Za-z0-9_]*)(\s*:\s*)("([^"]*)"|[A-Za-z_][A-Za-z0-9_]*)?/g, (all, key, colon, val, qval) => {
    const kTo = d.arg.has(key) ? inSense(d, key, 'argument', ctx.op) : null;
    let v = val || '';
    if (val) {
      const bare = qval !== undefined ? qval : val;
      const em = d.enumByArg.get(key);
      if (em && em.has(bare)) v = val.replace(bare, em.get(bare));
    }
    return (kTo || key) + colon + v;
  });
}

function rewriteName(d, s, ctx) {
  if (ctx.keep && ctx.keep.has(s)) return null;
  if (ctx.file && CASE_FREE_FILES.has(ctx.file) && s !== s.toLowerCase() && ctx.prose) { const to = caseFree(d, s); if (to !== null) return to; }
  // the operation in context decides first: its own argument, then a value
  // of one of its enums (renamed by that argument, or kept when the map has
  // no row for it: `fine` is a quality of esporta_stl and stays)
  if (ctx.args && ctx.args.has(s) && !ctx.sense) {
    const to = inSense(d, s, 'argument', ctx.op);
    if (to) return to === s ? null : to;
  }
  if (ctx.enums && !ctx.sense) {
    const tos = new Set();
    for (const [a, vals] of ctx.enums) if (vals.has(s)) { const m = d.enumByArg.get(a); tos.add(m && m.has(s) ? m.get(s) : s); }
    if (tos.size === 1) { const to = [...tos][0]; return to === s ? null : to; }
  }
  // a name of one or two letters (a, da, su) is a name in prose only when it
  // is an argument of the operation in context: «a» is also the throat of a
  // weld, «da» and «a» the ends of anything
  if (ctx.prose && s.length <= 2 && !d.reverse) return null;
  // in a note a word with two English names has no code around it to decide:
  // it is left as it is and listed (a note that says `misure` alone may mean
  // the field of an answer or the argument of a frame); so are the words a
  // note uses for another subject (NOTES_KEEP)
  if (ctx.notes && NOTES_KEEP.has(s) && d.universe.has(s)) { if (ctx.left) ctx.left.push(s + ' (another subject in the notes)'); return null; }
  const u = unanimous(d, s);
  if (u === null) return null;
  if (u !== CONFLICT) return u === s ? null : u;
  if (ctx.notes && !ctx.sense) { if (ctx.left) ctx.left.push(s + ' (' + sensesOf(d, s) + ')'); return null; }
  // two senses: the context decides
  let sense = ctx.sense;
  if (!sense && ctx.args && ctx.args.has(s)) sense = 'argument';
  if (!sense && ctx.prose) sense = (PROSE_SENSE_BY_FILE[ctx.file] || {})[s] || null;
  if (!sense && ctx.prose && ctx.defaultSense) sense = typeof ctx.defaultSense === 'function' ? ctx.defaultSense(s) : ctx.defaultSense;
  if (!sense && ctx.prose) sense = PROSE_SENSE_DEFAULT[s] || null;
  if (sense === 'keep') return null;
  if (sense) {
    const to = inSense(d, s, sense, ctx.op);
    if (to) return to === s ? null : to;
    // the sense does not know it: if one of the other senses is the only one
    // the context allows, take it; otherwise it is a conflict
  }
  if (ctx.conflicts) ctx.conflicts.push({ name: s, senses: sensesOf(d, s), file: ctx.file, at: ctx.at, op: ctx.op, text: ctx.text });
  return null;
}

// Prose (a comment, a message, a description, a document): spans whose whole
// content is a name expression, snake_case tokens and selectors elsewhere.
// `mode` says how the text is spelled: 'plain' (a document, a comment),
// 'backslash' (a C#, JavaScript or JSON string: \" is a quote, \n a
// separator), 'verbatim' (a C# @"" string: "" is a quote).
// 'jsq' is a JavaScript string between single quotes or backticks, and a
// PowerShell string between single quotes: backslash escapes (none for
// PowerShell, which has no "), and a plain " is a quote.
function rewriteProse(d, raw, mode, ctx, prot) {
  // a view of the text where escape sequences are not letters
  let view = raw;
  if (mode === 'backslash') view = raw.replace(/\\(u[0-9a-fA-F]{4}|x[0-9a-fA-F]{1,4}|[\s\S])/g, (m) => (m === '\\"' ? '\u0002\u0002' : '\u0001'.repeat(m.length)));
  else if (mode === 'jsq') view = raw.replace(/\\(u[0-9a-fA-F]{4}|x[0-9a-fA-F]{1,4}|[\s\S])/g, (m) => '\u0001'.repeat(m.length));
  else if (mode === 'verbatim') view = raw.replace(/""/g, '\u0002\u0002');
  else if (mode === 'ps2') view = raw.replace(/`[\s\S]/g, (m) => (m === '`"' ? '\u0002\u0002' : '\u0001\u0001')).replace(/""/g, '\u0002\u0002');
  const QUOTE = mode === 'plain' || mode === 'jsq' ? '"' : '\u0002\u0002';
  const edits = [];
  const taken = [];
  const free = (a, b) => !taken.some(([x, y]) => a < y && b > x) && !(prot && insideAny(prot, a, b));
  const push = (a, b, text) => { edits.push({ start: a, end: b, text }); taken.push([a, b]); };
  // 0. the URIs of the catalogue resources (catalogo://operazioni,
  //    catalogo://operazione/{nome}), and their English forms on the way back
  const uriRx = d.reverse ? /catalog:\/\/(operations|operation\/(\{name\}|<name>|&lt;name&gt;)?)/g : /catalogo:\/\/(operazioni|operazione\/(\{nome\}|<nome>|&lt;nome&gt;)?)/g;
  let m;
  while ((m = uriRx.exec(view))) {
    const a = m.index, b = a + m[0].length;
    if (!free(a, b)) continue;
    push(a, b, rewriteUri(d, raw.slice(a, b)));
  }
  // 1. selectors anywhere
  const selRx = /@(?:faccia|spigolo|corpo|quota|face|edge|body|dimension)(?:\((?:[^()\n]|\([^()\n]*\))*\))?/g;
  while ((m = selRx.exec(view))) {
    const a = m.index, b = a + m[0].length;
    if (taken.some(([x, y]) => a < y && b > x)) continue;
    const s = raw.slice(a, b);
    if (s.includes('\\')) continue;
    // a selector may hold a name of an Italian SOLIDWORKS as the value of a
    // field (@quota(feature=Schizzo1,nome=D1)): the kind and the fields are
    // renamed, the protected name is not (rewriteSelector never touches a
    // value outside the selector rows, and this checks it)
    const inner = prot ? prot.filter(([x, y]) => x < b && y > a) : [];
    if (inner.some(([x, y]) => x < a || y > b)) continue;
    const to = rewriteSelector(d, s);
    if (to !== null && inner.every(([x, y]) => to.split(raw.slice(x, y)).length === s.split(raw.slice(x, y)).length)) push(a, b, to);
    else taken.push([a, b]);
  }
  // 1b. an object written inline in prose, before the spans:
  //     {passo: 1, genere: "faccia_superiore"} (the example of a description).
  //     Every key must be an argument of the map, or it is not one; the keys
  //     are renamed, and a quoted value by the enum of its key.
  if (!ctx.notes) {
    const objRx = /\{([^{}\n]{1,160})\}/g;
    while ((m = objRx.exec(view))) {
      const a = m.index + 1, b = a + m[1].length;
      if (!free(a - 1, b + 1)) continue;
      // inside a backtick span the span decides (`sezione: {piano: "destro"}`)
      const lineStart = view.lastIndexOf('\n', m.index) + 1;
      if ((view.slice(lineStart, m.index).split('`').length - 1) % 2 === 1) continue;
      const parts = [];
      let k = a;
      for (const piece of m[1].split(',')) { parts.push([k, k + piece.length]); k += piece.length + 1; }
      const fields = [];
      let ok = parts.length > 0;
      for (const [x, y] of parts) {
        const pm = /^(\s*)([a-z_][a-z0-9_]*)(\s*:\s*)(.*?)\s*$/.exec(view.slice(x, y));
        if (!pm || !d.arg.has(pm[2])) { ok = false; break; }
        const ks = x + pm[1].length;
        const vs = ks + pm[2].length + pm[3].length;
        fields.push({ key: pm[2], ks, vs, ve: vs + pm[4].length });
      }
      if (!ok) continue;
      const local = [];
      for (const f of fields) {
        const kTo = inSense(d, f.key, 'argument', ctx.op);
        if (kTo && kTo !== f.key) local.push([f.ks, f.ks + f.key.length, kTo]);
        const vView = view.slice(f.vs, f.ve);
        const q = vView.startsWith(QUOTE) && vView.endsWith(QUOTE) && vView.length > 2 * QUOTE.length ? QUOTE.length : 0;
        if (!q) continue;
        const inner = raw.slice(f.vs + q, f.ve - q);
        const em = d.enumByArg.get(f.key);
        if (RX_NAME.test(inner) && em && em.has(inner)) local.push([f.vs + q, f.ve - q, em.get(inner)]);
      }
      for (const [x, y, t] of local) push(x, y, t);
      taken.push([a - 1, b + 1]);
    }
  }
  // 2. spans: `...`, «...», "..."
  const openers = ctx.notes ? [['`', '`']] : [['`', '`'], ['\u00ab', '\u00bb'], [QUOTE, QUOTE]];
  const closers = new Set();
  for (let i = 0; i < view.length; i++) {
    for (const [o, c] of openers) {
      if (!view.startsWith(o, i)) continue;
      if (o === '`' && closers.has(i)) break;
      const startC = i + o.length;
      let j = view.indexOf(c, startC);
      const nl = view.indexOf('\n', startC);
      if (j < 0 || (nl >= 0 && nl < j) || j - startC > 200 || j === startC) break;
      const content = raw.slice(startC, j);
      const contentView = view.slice(startC, j);
      if (!/[\u0001\u0002]/.test(contentView) && free(startC, j)) {
        const lead = /^\s*/.exec(content)[0], trail = /\s*$/.exec(content)[0];
        const core = content.slice(lead.length, content.length - trail.length);
        // a span that is an inline object (`{passo: N, genere: ...}`): its
        // keys, when every key is an argument of the map
        const ob = /^\{([^{}]*)\}$/.exec(core);
        if (ob && o === '`' && ob[1].split(',').every(p => { const pm = /^\s*([a-z_][a-z0-9_]*)\s*:/.exec(p); return pm && d.arg.has(pm[1]); })) {
          const to = rewriteObjectish(d, core, Object.assign({}, ctx, { prose: true }));
          if (to !== core) { push(startC + lead.length, startC + lead.length + core.length, to); i = j + c.length - 1; break; }
        }
        if (core && /^[@A-Za-z_][\w.@=~()\-[\], :"\u2026{}]*$/.test(core) && !/\s{2,}/.test(core)) {
          const to = rewriteExpr(d, core, Object.assign({}, ctx, { prose: true, at: startC }));
          if (to !== null) { push(startC + lead.length, startC + lead.length + core.length, to); i = j + c.length - 1; break; }
          if (RX_NAME.test(core) || RX_DOTTED.test(core) || RX_ASSIGN.test(core)) { taken.push([startC, j]); i = j + c.length - 1; break; }
        }
      }
      // a span not taken is looked into (a quoted value inside a backtick
      // expression), but its closing backtick never opens another span:
      // `<Link>`/`navigate()` verso `/files` read « verso » as one
      if (o === '`') closers.add(j);
      break;
    }
  }
  // 3. snake_case tokens outside the spans already taken
  const snake = /(?<![A-Za-z0-9_@\-./\\])([a-z][a-z0-9]*(?:_[a-z0-9]+)+)(?![A-Za-z0-9_\-])/g;
  while ((m = snake.exec(view))) {
    const a = m.index, b = a + m[1].length;
    if (!free(a, b)) continue;
    // a path or a file name around it is not a name
    if (view[b] === '.' && /[A-Za-z]/.test(view[b + 1] || '')) continue;
    const to = rewriteName(d, m[1], Object.assign({}, ctx, { prose: true, at: a }));
    if (to !== null) push(a, b, to);
  }
  return applyEdits(raw, edits);
}

// The URIs of the resources of the catalogue (Mcp/Risorse.cs).
const URIS = [['catalogo://operazioni', 'catalog://operations'], ['catalogo://operazione/', 'catalog://operation/'],
  ['{nome}', '{name}'], ['<nome>', '<name>'], ['&lt;nome&gt;', '&lt;name&gt;']];
function rewriteUri(d, u) {
  let out = u;
  for (const [it, en] of URIS) { const [x, y] = d.reverse ? [en, it] : [it, en]; out = out.split(x).join(y); }
  return out;
}

function applyEdits(text, edits) {
  if (!edits.length) return text;
  edits.sort((a, b) => b.start - a.start);
  let t = text;
  let last = Infinity;
  for (const e of edits) {
    if (e.end > last) cannot('two edits overlap (internal error) at ' + e.start);
    t = t.slice(0, e.start) + e.text + t.slice(e.end);
    last = e.start;
  }
  return t;
}

// ── JSON, token by token (the reader of window 1) ────────────────────────
function parseJsonTree(text, file) {
  let i = 0;
  const ws = () => { while (i < text.length && /[\s\ufeff]/.test(text[i])) i++; };
  const fail = (m) => cannot(file + ': not valid JSON at ' + i + ' (' + m + ')');
  const str = () => {
    const start = i;
    if (text[i] !== '"') fail('a string expected');
    i++;
    while (i < text.length && text[i] !== '"') { if (text[i] === '\\') i++; i++; }
    if (text[i] !== '"') fail('an unterminated string');
    i++;
    const raw = text.slice(start, i);
    return { type: 'string', value: JSON.parse(raw), start, end: i, raw };
  };
  const val = () => {
    ws();
    const ch = text[i];
    if (ch === '{') {
      i++; const entries = []; ws();
      if (text[i] === '}') { i++; return { type: 'object', entries }; }
      for (;;) {
        ws(); const key = str(); ws();
        if (text[i] !== ':') fail('a colon expected'); i++;
        const value = val(); entries.push({ key, value }); ws();
        if (text[i] === ',') { i++; continue; }
        if (text[i] === '}') { i++; return { type: 'object', entries }; }
        fail('a comma or a brace expected');
      }
    }
    if (ch === '[') {
      i++; const items = []; ws();
      if (text[i] === ']') { i++; return { type: 'array', items }; }
      for (;;) {
        items.push(val()); ws();
        if (text[i] === ',') { i++; continue; }
        if (text[i] === ']') { i++; return { type: 'array', items }; }
        fail('a comma or a bracket expected');
      }
    }
    if (ch === '"') return str();
    const m = /^(-?\d+(\.\d+)?([eE][+-]?\d+)?|true|false|null)/.exec(text.slice(i, i + 40));
    if (!m) fail('a value expected');
    i += m[0].length;
    return { type: 'other', raw: m[0] };
  };
  const tree = val(); ws();
  if (i !== text.length) fail('text after the value');
  JSON.parse(text.replace(/^\ufeff/, ''));
  return tree;
}
// The new spelling of a string token: the raw text with the value's changes
// applied where the raw text can take them (names are ASCII and never need
// an escape), so a file that spells an accent or a control character its
// own way keeps it.
function respell(node, value, file) {
  if (node.raw === JSON.stringify(node.value)) return JSON.stringify(value);
  // the raw spelling differs from JSON.stringify: rewrite the raw content
  // with the same edits, found by diffing the two values
  const a = node.value, b = value;
  let p = 0;
  while (p < a.length && p < b.length && a[p] === b[p]) p++;
  let s = 0;
  while (s < a.length - p && s < b.length - p && a[a.length - 1 - s] === b[b.length - 1 - s]) s++;
  const oldMid = a.slice(p, a.length - s), newMid = b.slice(p, b.length - s);
  const rawInner = node.raw.slice(1, -1);
  const k = rawInner.indexOf(oldMid);
  if (oldMid && k >= 0 && rawInner.indexOf(oldMid, k + 1) < 0 && !/[\\"]/.test(newMid)) {
    const out = '"' + rawInner.slice(0, k) + newMid + rawInner.slice(k + oldMid.length) + '"';
    if (JSON.parse(out) === b) return out;
  }
  cannot(file + ': the string at ' + node.start + ' is not written as JSON.stringify writes it, and the window cannot respell it');
}
function stringEdit(edits, node, newValue, file) {
  if (newValue === node.value) return;
  edits.push({ start: node.start, end: node.end, text: respell(node, newValue, file) });
}
function keyEdit(edits, node, newKey, file) { stringEdit(edits, node, newKey, file); }

// ── the values of an argument named in its own description ───────────────
// A description that teaches the values of its argument as bare words
// («passaggio = foro liscio ...; Predefinito: passaggio», «piano di
// costruzione: frontale, superiore o destro», «Generi ammessi:\n- asse: ...»)
// would teach the model the Italian values the schema no longer lists. The
// words are renamed where the sentence uses them AS VALUES, never elsewhere
// («l'asse creato dal passo» stays prose):
//   R1  after Predefinito / default:          Predefinito: passaggio
//   R2  a definition, value =:                passaggio = foro liscio; lamato = ...
//   R3  a value glossed in parentheses:       chiuso (schiacciato) o aperto (con gioco)
//   R4  a list of values only:                : frontale, superiore o destro / prima o dopo
//   R5  a bullet of the kinds a reference takes, under «Generi ammessi:»
//   R6  the default said in words:            isometrica se non dici niente
// The words are the enum values of the argument the description belongs to
// (in the language of the run), and for R5 the values of `genere`. The same
// rules run on the catalogue and on its C# source, which must agree.
const ENUM_WORD_L = 'A-Za-zÀ-ÿ0-9_';
function enumWordEdits(text, words, bulletWords) {
  const edits = new Map();
  const add = (s, w, map) => { if (!edits.has(s) && map.has(w)) edits.set(s, [s, s + w.length, map.get(w)]); };
  const alt = (map) => [...map.keys()].sort((a, b) => b.length - a.length).map(escapeRx).join('|');
  const nb = '(?![' + ENUM_WORD_L + '])';
  if (words && words.size) {
    const W = alt(words);
    let m;
    const r1 = new RegExp('(?:[Pp]redefinit[oa]|[Dd]efault):? «?(' + W + ')' + nb, 'g');
    while ((m = r1.exec(text))) add(m.index + m[0].length - m[1].length, m[1], words);
    const r2 = new RegExp('(?:^|; |\\. )(' + W + ') = ', 'g');
    while ((m = r2.exec(text))) add(m.index + m[0].length - 3 - m[1].length, m[1], words);
    const r3 = new RegExp('(?:^|: |, | o | oppure )(' + W + ') \\(', 'g');
    while ((m = r3.exec(text))) { add(m.index + m[0].length - 2 - m[1].length, m[1], words); r3.lastIndex = m.index + m[0].length - 1; }
    const r4 = new RegExp('(?:^|: )((?:' + W + ')(?:, (?:' + W + '))*(?: o | oppure )(?:' + W + '))' + nb, 'g');
    while ((m = r4.exec(text))) {
      const base = m.index + m[0].length - m[1].length;
      const one = new RegExp('(?<![' + ENUM_WORD_L + '])(' + W + ')' + nb, 'g');
      let w;
      while ((w = one.exec(m[1]))) add(base + w.index, w[1], words);
    }
    const r6 = new RegExp('(?:^|[.;] )(' + W + ') se non dici niente', 'g');
    while ((m = r6.exec(text))) add(m.index + m[0].length - ' se non dici niente'.length - m[1].length, m[1], words);
  }
  if (bulletWords && bulletWords.size) {
    const h = /(?:Generi ammessi|Allowed kinds):/.exec(text);
    if (h) {
      const r5 = new RegExp('(?:\\n|\\\\n)- (' + alt(bulletWords) + '):', 'g');
      r5.lastIndex = h.index;
      let m;
      while ((m = r5.exec(text))) add(m.index + m[0].length - 1 - m[1].length, m[1], bulletWords);
    }
  }
  return [...edits.values()].sort((a, b) => a[0] - b[0]);
}
function applyWordEdits(text, edits) {
  let out = '', at = 0;
  for (const [s, e, to] of edits) { out += text.slice(at, s) + to; at = e; }
  return out + text.slice(at);
}
// The words of an argument of an operation: its enum values (from the
// catalogue of the tree, in the language of the run) that the map renames.
function enumWordsOf(d, opCtx, op, prop) {
  const out = new Map();
  const oc = opCtx && op ? opCtx.get(op) : null;
  const vals = oc && prop ? oc.enums.get(prop) : null;
  const m = prop ? d.enumByArg.get(prop) : null;
  if (!vals || !m) return out;
  for (const v of vals) if (m.has(v) && m.get(v) !== v) out.set(v, m.get(v));
  return out;
}
function bulletWordsOf(d) {
  const m = d.enumByArg.get(d.reverse ? 'kind' : 'genere');
  return m ? new Map([...m].filter(([a, b]) => a !== b)) : new Map();
}
// The catalogue: every description of a schema, with the argument it
// describes (items describe the argument of their array).
function enumWordsInCatalogue(d, text, file, report) {
  const tree = parseJsonTree(text, file);
  const raw = JSON.parse(text.replace(/^﻿/, ''));
  const opCtx = new Map();
  for (const o of raw.operazioni || raw.operations || []) opCtx.set(o.nome || o.name, opContext(o.schema));
  const bullets = bulletWordsOf(d);
  const edits = [];
  const walk = (node, op, prop) => {
    if (!node || node.type !== 'object') return;
    for (const e of node.entries) {
      const k = e.key.value;
      if (k === 'properties' && e.value.type === 'object') for (const p of e.value.entries) walk(p.value, op, p.key.value);
      else if (k === 'items') walk(e.value, op, prop);
      else if (k === 'description' && e.value.type === 'string') {
        const we = enumWordEdits(e.value.value, enumWordsOf(d, opCtx, op, prop), bullets);
        if (we.length) { stringEdit(edits, e.value, applyWordEdits(e.value.value, we), file); report.enum_words_described = (report.enum_words_described || 0) + we.length; }
      }
    }
  };
  for (const e of tree.entries) {
    if ((e.key.value !== 'operazioni' && e.key.value !== 'operations') || e.value.type !== 'array') continue;
    for (const o of e.value.items) {
      if (o.type !== 'object') continue;
      const nameNode = o.entries.find(x => x.key.value === 'nome' || x.key.value === 'name');
      const op = nameNode && nameNode.value.type === 'string' ? nameNode.value.value : null;
      const sch = o.entries.find(x => x.key.value === 'schema');
      if (sch) walk(sch.value, op, null);
    }
  }
  return applyEdits(text, edits);
}
// The C# source of the catalogue: a description is O("description", "..." +
// "..."), a run of literals joined by +; the rules run on the run as one
// text (a word split between two literals is not renamed, on either side).
function enumWordsInOperationsCs(d, text, opCtx) {
  const tokens = lexCs(text);
  const frames = callFrames(text, tokens);
  const opStarts = [];
  { const rx = /\bNome\s*=\s*"([a-z0-9_]+)"/g; let m; while ((m = rx.exec(text))) opStarts.push([m.index, m[1]]); }
  const opAt = (p) => { let op = null; for (const [i, o] of opStarts) { if (i > p) break; op = o; } return op; };
  const bullets = bulletWordsOf(d);
  const strs = tokens.filter(t => t.kind === 'str');
  const edits = [];
  for (let i = 0; i < strs.length; i++) {
    const f = frames.get(strs[i].start);
    if (!f || !f.length || f[0].name !== 'O' || f[0].index % 2 !== 1 || f[0].keys[f[0].index - 1] !== 'description') continue;
    const run = [strs[i]];
    while (i + 1 < strs.length && /^\s*\+\s*$/.test(text.slice(run[run.length - 1].end + 1, strs[i + 1].open))) run.push(strs[++i]);
    const segs = [];
    let joined = '';
    for (const t of run) { segs.push([joined.length, t.start]); joined += text.slice(t.start, t.end); }
    const we = enumWordEdits(joined, enumWordsOf(d, opCtx, opAt(run[0].start), propertyOf(f, 0)), bullets);
    for (const [s, e, to] of we) {
      let k = segs.length - 1;
      while (k > 0 && segs[k][0] > s) k--;
      const segEnd = k + 1 < segs.length ? segs[k + 1][0] : joined.length;
      if (e > segEnd) continue;
      edits.push({ start: segs[k][1] + (s - segs[k][0]), end: segs[k][1] + (e - segs[k][0]), text: to });
    }
  }
  return applyEdits(text, edits);
}

// ── the catalogue ─────────────────────────────────────────────────────────
// A schema node (a tree of parseJsonTree), rewritten in place into `edits`.
// `prop` is the property this node describes (source language).
function rewriteSchemaTree(d, node, op, prop, edits, file, ctx) {
  if (node.type !== 'object') return;
  for (const e of node.entries) {
    const k = e.key.value;
    if (k === 'properties' && e.value.type === 'object') {
      for (const p of e.value.entries) {
        const name = p.key.value;
        const to = inSense(d, name, 'argument', op);
        if (to) keyEdit(edits, p.key, to, file);
        rewriteSchemaTree(d, p.value, op, name, edits, file, ctx);
      }
    } else if (k === 'required' && e.value.type === 'array') {
      for (const it of e.value.items) if (it.type === 'string') { const to = inSense(d, it.value, 'argument', op); if (to) stringEdit(edits, it, to, file); }
    } else if ((k === 'enum') && e.value.type === 'array') {
      for (const it of e.value.items) if (it.type === 'string') stringEdit(edits, it, rewriteValue(d, it.value, prop, op), file);
    } else if (k === 'default' && e.value.type === 'string') {
      stringEdit(edits, e.value, rewriteValue(d, e.value.value, prop, op), file);
    } else if (k === 'description' && e.value.type === 'string') {
      const v = catalogueEdits(d, op, rewriteProse(d, e.value.value, 'plain', ctx, null));
      stringEdit(edits, e.value, v, file);
    } else if (k === 'items') {
      rewriteSchemaTree(d, e.value, op, prop, edits, file, ctx);
    }
  }
}

// The facing arguments of an operation take the facings as their enum.
function addFacing(d, oc) {
  for (const a of d.facingArgs || []) if (oc.args.has(a) && !oc.enums.has(a)) oc.enums.set(a, new Set(d.facing.keys()));
  return oc;
}
function catalogueEdits(d, op, v) {
  const it = d.opItalian(op);
  for (const [a, b] of CATALOGUE_EDITS[it] || []) { const [x, y] = d.reverse ? [b, a] : [a, b]; v = v.split(x).join(y); }
  return v;
}

// The arguments of a step or of an example: keys are arguments (nested
// objects too), values are renamed by the argument they belong to.
function rewriteArgsTree(d, node, op, key, edits, file) {
  if (node.type === 'object') {
    for (const e of node.entries) {
      const k = e.key.value;
      if (!k.startsWith('_')) { const to = inSense(d, k, 'argument', op); if (to) keyEdit(edits, e.key, to, file); }
      rewriteArgsTree(d, e.value, op, k, edits, file);
    }
  } else if (node.type === 'array') {
    for (const it of node.items) rewriteArgsTree(d, it, op, key, edits, file);
  } else if (node.type === 'string') {
    stringEdit(edits, node, rewriteValue(d, node.value, key, op, 'argument'), file);
  }
}

// D12: the examples of the catalogue that carry a name SOLIDWORKS gives
// (a feature, a sheet, a dimension) were copied from cases run on an Italian
// installation. They take the English name SOLIDWORKS gives, and the
// description of the argument says that the name is the installation's.
// [Italian text, English text], applied to the catalogue and to its source
// (Riscontro.Disegno/Catalogo/Operazioni.cs) as plain text: none of these
// texts is a name of the map, and none of the English ones was in the tree
// before the window, so the reverse run takes back only what this wrote.
const D12_SENTENCE = ' The name is the one SOLIDWORKS gives, in the language of its installation: ' +
  'the example is from an English one; copy the name from the state as it is.';
const D12_NAMES = [
  ['Taglio-Estrusione1', 'Cut-Extrude1'],
  ['Taglio-Estrusione2', 'Cut-Extrude2'],
  ['D1@Svuota1@Parte3.Part', 'D1@Shell1@Part3.Part'],
  ['Svuota1', 'Shell1'],
  ['D1@Schizzo1', 'D1@Sketch1'],
  ['D1@Schizzo2', 'D1@Sketch2'],
  ['\\"Foglio1\\"', '\\"Sheet1\\"'],
];
const D12_SENTENCES = [
  'la lavorazione da cambiare, col nome dallo stato («feature»).',
  'la lavorazione da spostare, col nome dallo stato («feature»).',
  'il nome della quota, copiato dallo stato del documento.',
  'il nome della quota, copiato dallo stato del documento',
  'il nome della quota, copiato dallo stato del documento (es. «D1@Sketch2»)',
  'il nome della lavorazione, copiato dallo stato del documento',
  'il nome esatto del foglio, come lo riporta lo stato',
].map(x => ['"' + x + '"', '"' + x + D12_SENTENCE + '"']);
// the names first, the longer before the shorter that is inside it, then the
// sentences (one quotes a renamed name); backwards the other way round
const D12_EDITS = D12_NAMES.concat(D12_SENTENCES);

function d12(text, toItalian, report) {
  let t = text;
  const list = toItalian ? D12_SENTENCES.concat(D12_NAMES) : D12_EDITS;
  for (const [it, en] of list) {
    const [x, y] = toItalian ? [en, it] : [it, en];
    const n = t.split(x).length - 1;
    if (n) { t = t.split(x).join(y); if (report) report.d12_examples = (report.d12_examples || 0) + n; }
  }
  return t;
}

function rewriteCatalogue(d, text, file, report) {
  if (d.reverse) return rewriteCatalogueNames(d, d12(text, true, report), file, report);
  return d12(rewriteCatalogueNames(d, text, file, report), false, report);
}

function rewriteCatalogueNames(d, text0, file, report) {
  const text = enumWordsInCatalogue(d, text0, file, report);
  const tree = parseJsonTree(text, file);
  if (tree.type !== 'object') cannot(file + ': the catalogue is not an object');
  const edits = [];
  const conflicts = [];
  const bump = (k) => { report[k] = (report[k] || 0) + 1; };
  const raw = JSON.parse(text.replace(/^\ufeff/, ''));
  const opsList = raw.operazioni || raw.operations || [];
  const ctxOf = new Map();
  for (const o of opsList) ctxOf.set(o.nome || o.name, opContext(o.schema));
  for (const e of tree.entries) {
    const k = e.key.value;
    const kTo = d.ckey.get(k);
    if (kTo) { keyEdit(edits, e.key, kTo, file); bump('catalogue_keys'); }
    if ((k === 'famiglie' || k === 'families') && e.value.type === 'object') {
      for (const f of e.value.entries) {
        const fTo = d.family.get(f.key.value);
        if (fTo) { keyEdit(edits, f.key, fTo, file); bump('families'); }
        if (f.value.type === 'string') stringEdit(edits, f.value, rewriteProse(d, f.value.value, 'plain', { file, conflicts, prose: true }, null), file);
      }
    }
    if ((k === 'operazioni' || k === 'operations') && e.value.type === 'array') {
      for (const o of e.value.items) {
        if (o.type !== 'object') continue;
        const nameNode = o.entries.find(x => x.key.value === 'nome' || x.key.value === 'name');
        const op = nameNode && nameNode.value.type === 'string' ? nameNode.value.value : null;
        const oc = addFacing(d, ctxOf.get(op) || { args: new Set(), enums: new Map() });
        const opIt = d.opItalian(op);
        const ctx = { file, conflicts, op, args: oc.args, enums: oc.enums, prose: true, defaultSense: 'response_field',
          keep: PROSE_KEEP[opIt] ? new Set(d.reverse ? PROSE_KEEP[opIt].map(w => (d.enumByArg.get('variant') || new Map()).get(w) || w) : PROSE_KEEP[opIt]) : null };
        for (const x of o.entries) {
          const xk = x.key.value;
          const xTo = d.ckey.get(xk);
          if (xTo) { keyEdit(edits, x.key, xTo, file); bump('catalogue_keys'); }
          if (x.value.type !== 'string' && xk !== 'schema' && xk !== 'schema') continue;
          if (xk === 'nome' || xk === 'name') { const to = d.op.get(x.value.value); if (to) { stringEdit(edits, x.value, to, file); bump('operations'); } }
          else if (xk === 'famiglia' || xk === 'family') { const to = d.family.get(x.value.value); if (to) stringEdit(edits, x.value, to, file); }
          else if (xk === 'precondizione' || xk === 'precondition') stringEdit(edits, x.value, rewriteValue(d, x.value.value, d.reverse ? 'precondition' : 'precondizione', op), file);
          else if (xk === 'documento' || xk === 'document') stringEdit(edits, x.value, rewriteValue(d, x.value.value, d.reverse ? 'document' : 'documento', op), file);
          else if (xk === 'esempio' || xk === 'example') stringEdit(edits, x.value, rewriteExample(d, x.value.value, op, file), file);
          else if (xk === 'sommario' || xk === 'summary' || xk === 'descrizione' || xk === 'details') stringEdit(edits, x.value, catalogueEdits(d, op, rewriteProse(d, x.value.value, 'plain', ctx, null)), file);
          else if (xk === 'schema') rewriteSchemaTree(d, x.value, op, null, edits, file, ctx);
        }
      }
    }
  }
  report.conflicts = (report.conflicts || []).concat(conflicts);
  return applyEdits(text, edits);
}

// An example: the JSON text of the arguments of one call.
function rewriteExample(d, text, op, file) {
  if (typeof text !== 'string' || !text.trim().startsWith('{')) return text;
  let tree;
  try { tree = parseJsonTree(text, file + ' (example of ' + op + ')'); } catch (e) { return text; }
  const edits = [];
  rewriteArgsTree(d, tree, op, null, edits, file);
  return applyEdits(text, edits);
}

// ── the cases and plans ───────────────────────────────────────────────────
function loadCaseKeys(root) {
  const ck = JSON.parse(fs.readFileSync(path.join(root, CASE_KEYS_REL), 'utf8').replace(/^\ufeff/, ''));
  return ck;
}

// The keys of a case are window 1's; this window renames, inside a case: the
// operation of every step, the arguments, the expectation paths and values,
// the twin-free texts that name a value the case chose (message_includes).
function rewriteCase(d, ck, text, file, report) {
  const tree = parseJsonTree(text, file);
  const edits = [];
  const bump = (k, n) => { report[k] = (report[k] || 0) + (n || 1); };
  if (tree.type === 'array') {
    // a plan: [{ operazione, argomenti, ... }] - the bridge's protocol
    bump('plans');
    for (const step of tree.items) {
      if (step.type !== 'object') continue;
      const opNode = step.entries.find(x => x.key.value === (d.reverse ? 'operation' : 'operazione'));
      const op = opNode && opNode.value.type === 'string' ? opNode.value.value : null;
      for (const e of step.entries) {
        const k = e.key.value;
        if (k.startsWith('_')) {
          if (e.value.type === 'string') { const to = rewriteProse(d, e.value.value, 'plain', { prose: true, op, file }, null); if (to !== e.value.value) { stringEdit(edits, e.value, to, file); bump('case_prose'); } }
          continue;
        }
        const kTo = inSense(d, k, 'argument', null);
        if (kTo) { keyEdit(edits, e.key, kTo, file); bump('plan_keys'); }
        if (k === (d.reverse ? 'operation' : 'operazione') && e.value.type === 'string') {
          const to = d.op.get(e.value.value); if (to) { stringEdit(edits, e.value, to, file); bump('operations'); }
        } else if (k === (d.reverse ? 'arguments' : 'argomenti')) rewriteArgsTree(d, e.value, op, null, edits, file);
      }
    }
    return applyEdits(text, edits);
  }
  if (tree.type !== 'object') cannot(file + ': neither a case nor a plan');
  bump('cases');
  const rootCtx = file.split('/')[2] === 'regression' ? ck.roots.regression : ck.roots.declared;
  const caseValues = new Map();   // values the case chose (arguments), renamed by response_value
  const walk = (node, cName, op) => {
    const c = ck.contexts[cName];
    if (!c) cannot(file + ': no context ' + cName);
    const opHere = (() => {
      const n = node.entries.find(x => x.key.value === 'operation');
      if (n && n.value.type === 'string') return n.value.value;
      return op;
    })();
    for (const e of node.entries) {
      const k = e.key.value;
      if (k === 'operation' && e.value.type === 'string') {
        const to = d.op.get(e.value.value); if (to) { stringEdit(edits, e.value, to, file); bump('operations'); }
        continue;
      }
      if (k === 'arguments') { rewriteArgsTree(d, e.value, opHere, null, edits, file); bump('argument_blocks'); continue; }
      if (k === 'modify' && e.value.type === 'object') {
        // a regression case changes one dimension with the arguments of
        // modifica_quota
        rewriteArgsTree(d, e.value, d.reverse ? d.op.get('edit_dimension') || null : 'modifica_quota', null, edits, file);
        continue;
      }
      if ((k === 'select_first' || k === 'hide_first') && e.value.type === 'array') {
        for (const it of e.value.items) if (it.type === 'string') stringEdit(edits, it, rewriteValue(d, it.value, null, opHere), file);
        continue;
      }
      if (k === 'what' && e.value.type === 'string') {
        const to = rewritePath(d, e.value.value, opHere);
        if (to !== e.value.value) { stringEdit(edits, e.value, to, file); bump('expectation_paths'); }
        continue;
      }
      if ((k === 'equal_to' || k === 'includes' || k === 'does_not_include') && (cName === 'expectation' || cName === 'regression_expectation')) {
        const what = (node.entries.find(x => x.key.value === 'what') || {}).value;
        const rewrite = (n) => {
          if (n.type !== 'string') return;
          const q = k !== 'equal_to' ? quotedName(d, file, n.value) : null;
          const to = q !== null ? q : expectationValue(d, k, what && what.type === 'string' ? what.value : null, n.value, opHere);
          if (to !== n.value) { stringEdit(edits, n, to, file); bump('expectation_values'); }
        };
        if (e.value.type === 'array') e.value.items.forEach(rewrite); else rewrite(e.value);
        continue;
      }
      if (k === 'message_includes' && e.value.type === 'array') {
        for (const it of e.value.items) {
          if (it.type !== 'string') continue;
          const to = expectationValue(d, 'message_includes', null, it.value, opHere);
          if (to !== it.value) { stringEdit(edits, it, to, file); bump('message_pieces'); }
        }
        continue;
      }
      const child = c.children && c.children[k];
      if (child) {
        if (e.value.type === 'object') walk(e.value, child, opHere);
        else if (e.value.type === 'array') e.value.items.forEach(it => { if (it.type === 'object') walk(it, child, opHere); });
        continue;
      }
      // the prose of the case (why, the free notes, defect_caught): code
      // names only, as in every other text; the name, family and twin of a
      // case are identities of window 1, not prose
      if (k.startsWith('_') || CASE_PROSE_KEYS.has(k)) prose(e.value, opHere);
    }
  };
  const prose = (n, op) => {
    if (n.type === 'string') {
      const to = rewriteProse(d, n.value, 'plain', { prose: true, op, file, conflicts: report.conflicts || (report.conflicts = []) }, null);
      if (to !== n.value) { stringEdit(edits, n, to, file); bump('case_prose'); }
    } else if (n.type === 'array') n.items.forEach(x => prose(x, op));
    else if (n.type === 'object') n.entries.forEach(x => prose(x.value, op));
  };
  walk(tree, rootCtx, null);
  void caseValues;
  return applyEdits(text, edits);
}

// A value a case compares, by the key that compares it and the path it
// reads: one function for the cases and for snapshot.js --window 2, so the two
// cannot disagree.
function expectationValue(d, key, what, v, op) {
  if (typeof v !== 'string') return v;
  if (key === 'message_includes') return rewriteMessagePiece(d, v, op);
  const segs = typeof what === 'string' ? what.split('.') : [];
  const leaf = segs.length ? segs[segs.length - 1].replace(/\[\d+\]$/, '') : null;
  const root = segs.length ? segs[0].replace(/\[\d+\]$/, '') : null;
  const where = (root === 'stato' || root === 'state') && (leaf === 'verso' || leaf === 'facing') ? 'state' : 'expectation';
  // the field's own enum first (misure.operazione = unisci is union), then
  // a value of an enum of the operation (a note that names the default it
  // took), then the values the map names
  const m = leaf ? d.enumByArg.get(leaf) : null;
  if (m && m.has(v)) return m.get(v);
  if (where === 'state') { const r = rewriteValue(d, v, leaf, op, where); if (r !== v) return r; }
  const e = opEnumValue(d, v, op);
  if (e !== null) return e;
  if (d.rvalue.has(v)) return d.rvalue.get(v);
  if (v.startsWith('@')) { const s = rewriteSelector(d, v); return s === null ? v : s; }
  // a name a text quotes (includes «toccarsi_conta»): a snake_case name or a
  // dotted path, whole
  if (key !== 'equal_to' && (/_/.test(v) || RX_DOTTED.test(v)) && (RX_NAME.test(v) || RX_DOTTED.test(v))) {
    const r = RX_NAME.test(v) ? rewriteName(d, v, { prose: true, op, defaultSense: 'argument' }) : rewriteExpr(d, v, { prose: true, op });
    if (r !== null) return r;
  }
  return v;
}

// A value of an enum of the operation in context, renamed by its argument
// (or kept when the map has no row for it); null when it is none.
function opEnumValue(d, v, op) {
  const oc = d.opCtx && op ? d.opCtx.get(op) : null;
  if (!oc) return null;
  const tos = new Set();
  for (const [a, vals] of oc.enums) if (vals.has(v)) { const m = d.enumByArg.get(a); tos.add(m && m.has(v) ? m.get(v) : v); }
  return tos.size === 1 ? [...tos][0] : null;
}

// The keys of a case whose value is prose (window 1's names).
const CASE_PROSE_KEYS = new Set(['why', 'defect_caught', 'tolerance_why', 'intent_why', 'difference_is_the_part']);

// A piece of a refusal a case expects. Refusals are Italian prose (window
// .8); a piece is renamed only when it is a code name the refusal quotes
// (an enum value, an argument, a value the case chose).
function rewriteMessagePiece(d, v, op) {
  if (RX_NAME.test(v) || /^[A-Za-z_][\w ]*$/.test(v) && d.rvalue.has(v)) {
    const e = opEnumValue(d, v, op);
    if (e !== null) return e;
    if (d.rvalue.has(v)) return d.rvalue.get(v);
    const u = unanimous(d, v);
    if (u && u !== CONFLICT) return u;
    if (u === CONFLICT) {
      // an enum value of an argument of the operation wins
      for (const [a, m] of d.enumByArg) if (m.has(v) && op) return m.get(v);
    }
    return v;
  }
  if (v.startsWith('@')) { const s = rewriteSelector(d, v); return s === null ? v : s; }
  // a quoted code name inside the piece
  return rewriteProse(d, v, 'plain', { prose: true, op, defaultSense: 'argument' }, null);
}

// ── the task files of D9 ──────────────────────────────────────────────────
function rewriteTaskJson(d, text, file, report) {
  const tree = parseJsonTree(text, file);
  const edits = [];
  const walk = (node) => {
    if (node.type === 'object') for (const e of node.entries) {
      if (e.key.value === TASK_PATH_KEY && e.value.type === 'string') {
        const to = rewritePath(d, e.value.value, null);
        if (to !== e.value.value) { stringEdit(edits, e.value, to, file); report.task_paths = (report.task_paths || 0) + 1; }
      } else walk(e.value);
    } else if (node.type === 'array') node.items.forEach(walk);
  };
  walk(tree);
  return applyEdits(text, edits);
}

// ── C# ────────────────────────────────────────────────────────────────────
// The string literals and comments of a C# source. A regular string is
// `str`, a verbatim one `vstr`, the text parts of an interpolated string
// `ipart`; holes are code and their strings are lexed too.
function lexCs(t) {
  const out = []; let i = 0; const n = t.length;
  const push = (kind, s, e, extra) => out.push(Object.assign({ kind, start: s, end: e }, extra || {}));
  function regular(s, contentStart) {
    let j = contentStart;
    while (j < n && t[j] !== '"') { if (t[j] === '\\') j++; if (t[j] === '\n') cannot('a newline inside a string at ' + s); j++; }
    return j;
  }
  function verbatim(contentStart) { let j = contentStart; for (;;) { if (j >= n) cannot('an unterminated verbatim string'); if (t[j] === '"') { if (t[j + 1] === '"') { j += 2; continue; } return j; } j++; } }
  function interp(s, contentStart, verb) {
    let j = contentStart, partStart = j;
    for (;;) {
      if (j >= n) cannot('an unterminated interpolated string at ' + s);
      const c = t[j];
      if (!verb && c === '\\') { j += 2; continue; }
      if (verb && c === '"' && t[j + 1] === '"') { j += 2; continue; }
      if (c === '"') { push('ipart', partStart, j, { verb, open: s }); return j + 1; }
      if (c === '{') {
        if (t[j + 1] === '{') { j += 2; continue; }
        push('ipart', partStart, j, { verb, open: s });
        let depth = 1; j++;
        while (depth > 0) {
          if (j >= n) cannot('an unterminated hole at ' + s);
          const dch = t[j];
          if (dch === '{') depth++;
          else if (dch === '}') { depth--; if (depth === 0) break; }
          else if (dch === '"' || dch === '@' || dch === '$' || dch === "'") { const r = lexOne(j); if (r) { j = r; continue; } }
          j++;
        }
        j++; partStart = j; continue;
      }
      if (c === '}' && t[j + 1] === '}') { j += 2; continue; }
      j++;
    }
  }
  function lexOne(j) {
    const c = t[j];
    if (c === '"') { const e = regular(j, j + 1); push('str', j + 1, e, { open: j }); return e + 1; }
    if (c === '@' && t[j + 1] === '"') { const e = verbatim(j + 2); push('vstr', j + 2, e, { open: j }); return e + 1; }
    if (c === '$' && t[j + 1] === '"') return interp(j, j + 2, false);
    if ((c === '$' && t[j + 1] === '@' && t[j + 2] === '"') || (c === '@' && t[j + 1] === '$' && t[j + 2] === '"')) return interp(j, j + 3, true);
    if (c === "'") { let k = j + 1; if (t[k] === '\\') k += 2; else k++; while (t[k] !== "'" && k < j + 12) k++; return k + 1; }
    return 0;
  }
  while (i < n) {
    const c = t[i];
    if (c === '/' && t[i + 1] === '/') { const e = t.indexOf('\n', i); const end = e < 0 ? n : e; push('comment', i, end); i = end; continue; }
    if (c === '/' && t[i + 1] === '*') { const e = t.indexOf('*/', i + 2); push('comment', i, e + 2); i = e + 2; continue; }
    if (c === '"' || c === '@' || c === '$' || c === "'") { const r = lexOne(i); if (r) { i = r; continue; } }
    i++;
  }
  return out;
}

// The calls around each string literal of a C# source: for O(...) and A(...)
// of the catalogue, the key a value belongs to; for an object initializer, the
// field being set. Returns, per literal token start, its frames (innermost
// first): { name, index, keys, field }.
function callFrames(t, tokens) {
  const tokAt = new Map(tokens.map(k => [k.kind === 'comment' ? k.start : (k.open !== undefined ? k.open : k.start), k]));
  const out = new Map();
  const stack = [];
  let i = 0;
  const n = t.length;
  const nameBefore = (p) => {
    let j = p - 1;
    while (j >= 0 && /\s/.test(t[j])) j--;
    if (t[j] === '>') {   // a generic type: KeyValuePair<string, string>(
      let depth = 0;
      for (; j >= 0; j--) { if (t[j] === '>') depth++; else if (t[j] === '<') { depth--; if (depth === 0) { j--; break; } } }
    }
    let e = j + 1;
    while (j >= 0 && /[A-Za-z0-9_]/.test(t[j])) j--;
    return t.slice(j + 1, e);
  };
  while (i < n) {
    const tok = tokAt.get(i);
    if (tok) {
      if (tok.kind !== 'comment') {
        const top = stack[stack.length - 1];
        out.set(tok.start, stack.slice().reverse().map(f => ({ name: f.name, index: f.index, keys: f.keys, field: f.field })));
        if (top && top.name === 'O' && top.index % 2 === 0 && tok.kind === 'str') top.keys[top.index] = t.slice(tok.start, tok.end);
      }
      // skip the whole literal (an interpolated string has several parts: skip to the last)
      let end = tok.kind === 'comment' ? tok.end : tok.end + 1;
      if (tok.kind === 'ipart') { const last = tokens.filter(k => k.kind === 'ipart' && k.open === tok.open).pop(); end = last.end + 1; for (const k of tokens) if (k.kind === 'ipart' && k.open === tok.open) out.set(k.start, stack.slice().reverse().map(f => ({ name: f.name, index: f.index, keys: f.keys, field: f.field }))); }
      i = end;
      continue;
    }
    const c = t[i];
    if (c === '(' || c === '{' || c === '[') stack.push({ name: c === '(' ? nameBefore(i) : c, index: 0, keys: {}, field: null });
    else if (c === ')' || c === '}' || c === ']') stack.pop();
    else if (c === ',') { const top = stack[stack.length - 1]; if (top) { top.index++; if (top.name === '{') top.field = null; } }
    else if (c === '=' && t[i + 1] !== '=' && t[i - 1] !== '=' && t[i - 1] !== '!' && t[i - 1] !== '<' && t[i - 1] !== '>' && t[i + 1] !== '>') {
      const top = stack[stack.length - 1];
      if (top && top.name === '{') { const nm = nameBefore(i); if (nm) top.field = nm; }
    }
    i++;
  }
  return out;
}

// The sense of a whole-name literal in the catalogue source, from its calls.
function operationsCsSense(frames) {
  if (!frames || !frames.length) return null;
  const f0 = frames[0];
  if (f0.name === 'O') {
    if (f0.index % 2 === 0) {
      // a key: an argument when the object is the value of "properties"
      const parent = frames[1];
      if (parent && parent.name === 'O' && parent.index % 2 === 1 && parent.keys[parent.index - 1] === 'properties') return { sense: 'argument' };
      return { sense: 'keep' };
    }
    const key = f0.keys[f0.index - 1];
    if (key === 'type') return { sense: 'keep' };
    if (key === 'description') return { sense: 'prose' };
    if (key === 'default') return { sense: 'enum', prop: propertyOf(frames, 0) };
    return { sense: 'keep' };
  }
  if (f0.name === 'A') {
    const parent = frames[1];
    const key = parent && parent.name === 'O' ? parent.keys[parent.index - 1] : null;
    if (key === 'enum') return { sense: 'enum', prop: propertyOf(frames, 1) };
    if (key === 'required') return { sense: 'argument' };
    return null;
  }
  if (f0.name === 'KeyValuePair') return { sense: f0.index === 0 ? 'catalogue_family' : 'prose' };
  if (f0.name === '{' && f0.field) {
    const fld = f0.field;
    if (fld === 'Nome') return { sense: 'operation' };
    if (fld === 'Famiglia') return { sense: 'catalogue_family' };
    if (fld === 'DocumentoRichiesto') return { sense: 'enum', prop: 'documento' };
    if (fld === 'Precondizione') return { sense: 'enum', prop: 'precondizione' };
    if (fld === 'Esempio') return { sense: 'example' };
    if (fld === 'Sommario' || fld === 'Descrizione') return { sense: 'prose' };
  }
  return null;
}
// The property an object of a schema describes: the key whose value it is,
// skipping "items".
function propertyOf(frames, k) {
  for (let i = k + 1; i < frames.length; i++) {
    const f = frames[i];
    if (f.name !== 'O' || f.index % 2 !== 1) continue;
    const key = f.keys[f.index - 1];
    if (key === 'items') continue;
    return key;
  }
  return null;
}

// The sense of a whole-name literal elsewhere, from the code around it.
const ARG_DICT = '(?:a|argomenti|args|arg)';
const RX_ARG_READ = new RegExp('\\b(?!Metti\\b)[A-Z][A-Za-z0-9]*\\(\\s*' + ARG_DICT + '\\s*,\\s*$');
const RX_ARG_MEMBER = new RegExp('\\b' + ARG_DICT + '\\s*\\.\\s*(?:TryGetValue|ContainsKey|Remove)\\(\\s*$');
const RX_ARG_INDEX = new RegExp('\\b' + ARG_DICT + '\\s*\\[\\s*$');
const RX_ARG_DEFAULT = new RegExp('\\b(?:Str|Num|Bool|NumObbligatorio|Testo|TestoDi|Intero)\\(\\s*' + ARG_DICT + '\\s*,\\s*"([A-Za-z0-9_]+)"\\s*,\\s*$');
const RX_RESPONSE_INDEX = /\b(?:misure|fuori|risposta|scheda|esito|misureTot|voce|chiuso|risultato|descritta|letto|riga)\s*\[\s*$/;
function codeSense(d, before, after) {
  if (RX_ARG_READ.test(before) || RX_ARG_MEMBER.test(before) || RX_ARG_INDEX.test(before)) return { sense: 'argument' };
  let m = RX_ARG_DEFAULT.exec(before);
  if (m) return { sense: 'enum', prop: m[1] };
  if (/\bMetti\(\s*\w+\s*,\s*$/.test(before)) return { sense: 'response_field' };
  if (/\[\s*$/.test(before) && /^\s*\]\s*=\s*new\s+Dictionary<string,\s*object>\s*\{\s*\[\s*"type"\s*\]/.test(after)) return { sense: 'argument' };
  if (RX_RESPONSE_INDEX.test(before) && /^\s*\]/.test(after)) return { sense: 'response_field' };
  if (/(?:^|[{,(]\s*)\[\s*$/.test(before) && /^\s*\]\s*=(?!=)/.test(after)) return { sense: 'response_field', initializer: true };
  return null;
}

// Where a literal stands: a KEY (an index, a lookup, the second argument of
// a reader, a key of O()), a VALUE (assigned, returned, compared, a case), an
// item of a LIST, or an argument of a call (ARG).
function positionClass(before, after) {
  const b = before.replace(/\s+$/, ''), a = after.replace(/^\s+/, '');
  if (/\[$/.test(b) && /^\]/.test(a)) return 'KEY';
  if (/\.(?:TryGetValue|ContainsKey|Remove|Add|GetValueOrDefault)\($/.test(b)) return 'KEY';
  if (/\b[A-Z]\w*\(\s*[a-z]\w*\s*,$/.test(b) && /^[,)]/.test(a)) return 'KEY';
  if (/(?:\breturn|[=!]=|[^=!<>]=|\?|\bcase)$/.test(b) || /:$/.test(b) && /^[;),]/.test(a)) return 'VALUE';
  if (/^:/.test(a) && /\bcase$/.test(b)) return 'VALUE';
  if (/(?:new\s*(?:string)?\s*\[\s*\]\s*\{|new\s+(?:List|HashSet)<string>\s*(?:\([^)]*\))?\s*\{|\{)$/.test(b) || (/,$/.test(b) && /^[,}]/.test(a))) return 'LIST';
  if (/[(,]$/.test(b)) return 'ARG';
  return 'OTHER';
}

// CS_SITES: literals whose sense the code around them does not show, by
// file, a piece of their line and the literal (`*`: every literal of the
// line). `to` is a sense (argument, response_field, selector_field, facing,
// enum:<arg>), `=<name>` for an explicit English name, or keep. Every entry
// must be used by a forward run on the tree it was written for.
const CS_SITES = [
  // bench groups and their labels: the names of `--gruppo`, not protocol
  { file: 'Banco/Program.cs', line: 'new Gruppo { Nome = ', literal: '*', before: /\bNome\s*=\s*$/, to: 'keep', why: 'the names of the bench groups (--gruppo), not the MCP surface; the label (Cosa) names the operation a group exercises and follows the map' },
  { file: 'Banco/NumeriDelleQuote.cs', line: 'ErroreDiBanco("numeri"', literal: 'numeri', to: 'keep', why: 'the label of a bench group' },
  // words searched in Italian prose
  { file: 'Banco/Attese.cs', line: 'suArgomenti.IndexOf("copia"', literal: 'copia', to: 'keep', why: 'a word of an Italian sentence of the bench' },
  { file: 'Prove/ProveAttese.cs', line: 'd2.IndexOf("copia"', literal: 'copia', to: 'keep', why: 'the same word, checked by the contract' },
  { file: 'Prove/ProveAttese.cs', line: 'frase(', literal: '*', to: 'keep', why: 'words of the Italian sentence that describes a shape' },
  { file: 'Prove/ProveSessione.cs', line: 'due.Contains("lavorazioni")', literal: 'lavorazioni', to: 'keep', why: 'a word of the Italian sentence the contract reads' },
  { file: 'Prove/ProveIRifiuti.cs', line: 'new Remedy("riattiva"', literal: 'riattiva', to: 'keep', why: 'the stem of an Italian refusal the remedy recognises' },
  { file: 'Prove/ProveCatalogo.cs', line: 'ItalianNoun = "operazioni"', literal: 'operazioni', to: 'keep', why: 'the Italian noun of the README count' },
  { file: 'Riscontro.Disegno/Sezioni.cs', line: 'PareteStaDentro(', literal: '*', to: 'keep', why: 'the word the refusal writes in its Italian sentence' },
  { file: 'Riscontro.Disegno/OperazioniSw.cs', line: 'string unita = angolare ? "gradi" : "mm";', literal: 'gradi', to: 'keep', why: 'the unit word of an Italian message' },
  // paths, titles and names that are not names of the surface
  { file: 'Prove/LanguageGateTests.cs', line: 'Path.Combine(repo, ".github", "passi"', literal: 'passi', to: 'keep', why: 'the folder .github/passi' },
  { file: 'Prove/ProveSessione.cs', line: '.sldprt", "prova");', literal: 'prova', to: 'keep', why: 'the title of a fake document' },
  { file: 'Controlli/Annulla/Programma.cs', line: 'Riga("prima"', literal: 'prima', to: 'keep', why: 'a label of the probe output' },
  { file: 'Controlli/Annulla/Programma.cs', line: 'Riga("dopo"', literal: 'dopo', to: 'keep', why: 'a label of the probe output' },
  { file: 'Riscontro.Disegno/OperazioniDistintaTavola.cs', line: 'comeTrovata', literal: '*', to: 'keep', why: 'an internal marker, answered in English' },
  { file: 'Riscontro.Disegno/OperazioniTondoTavola.cs', line: '"piano"', literal: 'piano', to: 'keep', why: 'an internal kind of edge' },
  // value sets of the state the map does not name: kept whole, not half translated
  { file: 'Riscontro.Disegno/StatoDocumento.cs', line: 'oggetto is ', literal: '*', to: 'keep', why: 'the kinds of a selected object (faccia, spigolo, corpo, vertice, lavorazione): not in the map' },
  { file: 'Riscontro.Disegno/StatoTavola.cs', line: 'swDrawingViewTypes_e.', literal: '*', to: 'keep', why: 'the kinds of a drawing view: not in the map' },
  { file: 'Riscontro.Disegno/StatoTavola.cs', line: 'default: return "vista";', literal: 'vista', to: 'keep', why: 'the kinds of a drawing view: not in the map' },
  { file: 'Riscontro.Disegno/StatoAssieme.cs', line: 'swMateType_e.', literal: '*', to: 'keep', why: 'the kinds of a mate: not in the map' },
  { file: 'Riscontro.Disegno/OperazioniVerifica.cs', line: 'swBodyType_e.swSheetBody) return "foglio"', literal: 'foglio', to: 'keep', why: 'the kinds of a body: not in the map (solido is, foglio is not)' },
  { file: 'Riscontro.Disegno/OperazioniVerifica.cs', line: 'nome == "foglio"', literal: 'foglio', to: 'keep', why: 'the same kind, read back' },
  { file: 'Controlli/CorpoRotto/Programma.cs', line: '"foglio"', literal: 'foglio', to: 'keep', why: 'the probe copy of the kinds of a body' },
  { file: 'Riscontro.Disegno/OperazioniIstanze.cs', line: 'quante == 1 ? "copia" : "copie"', literal: '*', to: 'keep', why: 'words of an Italian message' },
  { file: 'Riscontro.Disegno/OperazioniIstanze.cs', line: '(giaSaltate.Length == 0 ? "nessuna"', literal: 'nessuna', to: 'keep', why: 'a word of an Italian message' },
  { file: 'Riscontro.Disegno/OperazioniSw.cs', line: '(eraSoppressa ? "soppressa" : "attiva")', literal: '*', to: 'keep', why: 'words of an Italian message' },
  { file: 'Riscontro.Disegno/OperazioniSw.cs', line: '"materiale" + (fatte > 1 ?', literal: 'materiale', to: 'keep', why: 'a word of an Italian message' },
  { file: 'Riscontro.Disegno/OperazioniProprieta.cs', line: '(sullaConfigurazione ? configurazione : "documento")', literal: 'documento', to: 'keep', why: 'a word of a log line' },
  { file: 'Prove/ProveSessione.cs', line: 'nato.Contains("nessuno")', literal: 'nessuno', to: 'keep', why: 'a word of an Italian sentence the contract reads' },
  // words searched in a text that are names the text quotes
  { file: 'Prove/ProveIlRegistro.cs', line: 'Contains("simili")', literal: 'simili', to: 'response_field', why: 'a key of the JSON line of the registry' },
  { file: 'Prove/ProveProtocollo.cs', line: 'IndexOf("disegna"', literal: 'disegna', to: 'tool', why: 'the tool the refusal of misura names, between guillemets' },
  { file: 'Banco/Program.cs', line: 'perche.IndexOf("isometrica"', literal: 'isometrica', to: 'enum:viste', why: 'the view the refusal names' },
  // two senses, settled by the line
  { file: 'Banco/Attese.cs', line: 'c.Campo == "angolare"', literal: 'angolare', to: 'selector_field' },
  { file: 'Banco/Attese.cs', line: 'case "faccia":', literal: 'faccia', to: '=face', why: 'the kind of @faccia without its @' },
  { file: 'Banco/Program.cs', line: 'f["verso"]', literal: 'verso', to: 'response_field' },
  { file: 'Banco/Program.cs', line: 'f["verso"]', literal: 'alto', to: 'facing' },
  { file: 'Banco/Program.cs', line: 'Convert.ToString(f["tipo"])', literal: 'tipo', to: 'response_field' },
  { file: 'Banco/ProvaNuove.cs', line: 'TryGetValue("verso"', literal: 'verso', to: 'response_field' },
  { file: 'Banco/Sessione.cs', line: 'coppia.Item1 == "spigoli"', literal: 'spigoli', to: 'response_field', why: 'the state key of the tuple above' },
  { file: 'Ponte/PianoInMemoria.cs', line: 'TryGetValue("misure"', literal: 'misure', to: 'response_field' },
  { file: 'Programma.cs', line: 'new[] { "titolo", "tipo", "volume_mm3", "corpi_totali" }', literal: 'tipo', to: 'response_field' },
  { file: 'Prove/FormaSpellingContract.cs', line: 'sheet["formato"]', literal: 'formato', to: 'argument' },
  // the arguments the bench writes in place for an operation it calls (an
  // initializer reads as an answer unless the line says otherwise): the
  // argument of that operation, where the map has another name for the field
  { file: 'Banco/NumeriDelleQuote.cs', line: '{ ["formato"] = "A3" }', literal: 'formato', to: '=paper_size', why: 'an argument of crea_disegno' },
  { file: 'Banco/Orfane.cs', line: '{ ["formato"] = "A3" }', literal: 'formato', to: '=paper_size', why: 'an argument of crea_disegno' },
  { file: 'Banco/Program.cs', line: '["misure"] = "ingombro"', literal: 'misure', to: '=measured_as', why: 'the reading of the frame inside telaio_rettangolare, as the library reads it (OperazioniSaldatura.cs, FIELD_INSIDE)' },
  { file: 'Banco/Program.cs', line: '["da"] = angoli[i], ["a"] = angoli[(i + 1) % 4]', literal: 'da', to: '=start', why: 'the ends of a segment of telaio_rettangolare, read with Punto(mappa, "start")' },
  { file: 'Banco/Program.cs', line: '["da"] = angoli[i], ["a"] = angoli[(i + 1) % 4]', literal: 'a', to: '=end', why: 'the same segment' },
  { file: 'Banco/Program.cs', line: '["tipo"] = tipo,', literal: 'tipo', to: '=variant', why: 'an argument of foro_guidato' },
  { file: 'Banco/ProvaNuove.cs', line: '["tipo"] = "filettato"', literal: 'tipo', to: '=variant', why: 'an argument of foro_guidato' },
  { file: 'Banco/ProvaNuove.cs', line: '["tipo"] = "passaggio"', literal: 'tipo', to: '=variant', why: 'an argument of foro_guidato' },
  // FIELD_BY_OPERATION in the library: the paper size the drawing operations
  // echo, and the sheet format name of the title block
  { file: 'Riscontro.Disegno/OperazioniTavola.cs', line: '["formato"] = Str(a, "formato", "A3")', literal: 'formato', to: '=paper_size', why: 'the echo of the argument, as the map note says' },
  { file: 'Riscontro.Disegno/OperazioniTavola.cs', line: '["formato"] = nomeFormato', literal: 'formato', to: '=paper_size', why: 'the echo of the argument of imposta_foglio' },
  { file: 'Riscontro.Disegno/OperazioniCartiglio.cs', line: '["formato"] = ProvaTesto(() => foglio.GetSheetFormatName())', literal: 'formato', to: '=sheet_format', why: 'the sheet format name of the template' },
  { file: 'Prove/FormaSpellingContract.cs', line: 'frame["misure"]', literal: 'misure', to: 'argument' },
  { file: 'Prove/Programma.cs', line: '{ "saldature",', literal: 'saldature', to: 'catalogue_family' },
  { file: 'Prove/Programma.cs', line: '{ "proprieta",', literal: 'proprieta', to: 'catalogue_family' },
  { file: 'Prove/ProveAttese.cs', line: '["verso"] = "davanti"', literal: 'davanti', to: 'facing' },
  { file: 'Prove/ProveAttese.cs', line: 'elenco("misure"', literal: 'misure', to: 'response_field', why: 'the key of the fake measurements just above' },
  { file: 'Prove/ProveICampiObbligatori.cs', line: '"combina_corpi", "operazione", "unisci"', literal: 'unisci', to: 'enum:operazione' },
  { file: 'Riscontro.Disegno/FormaAParole.cs', line: 'Testo(f, "tipo")', literal: 'tipo', to: 'response_field' },
  { file: 'Riscontro.Disegno/OperazioniSaldatura.cs', line: 'Str(telaio, "misure", "ingombro")', literal: 'misure', to: 'argument' },
  { file: 'Riscontro.Disegno/OperazioniSaldatura.cs', line: 'if (telaio != null) misure["misure"] = comeSiMisura;', literal: 'misure', to: '=measured_as', why: 'map note of misure: the inner key of the frame answer' },
  { file: 'Riscontro.Disegno/OperazioniSaldatura.cs', line: '["misure"] = a.ContainsKey("misure") ? a["misure"] : "ingombro",', literal: 'misure', to: 'argument', why: 'the defaults of the frame arguments' },
  { file: 'Riscontro.Disegno/OperazioniSaldatura.cs', line: 'Punto(mappa, "da"', literal: 'da', to: 'argument' },
  { file: 'Riscontro.Disegno/OperazioniSaldatura.cs', line: 'Punto(mappa, "a"', literal: 'a', to: 'argument' },
  { file: 'Riscontro.Disegno/OperazioniSw.cs', line: 'voce.TryGetValue("tipo", out var t)', literal: 'tipo', to: '=kind', why: 'the kind of a contour (cerchio, poligonale): a key the case writes inside contorni' },
  { file: 'Riscontro.Disegno/OperazioniSw.cs', line: 'attesa.TryGetValue("lavorazione"', literal: 'lavorazione', to: 'response_field' },
  { file: 'Riscontro.Disegno/OperazioniTavola.cs', line: 'Convert.ToString(d["tipo"])', literal: 'tipo', to: 'response_field' },
  { file: 'Riscontro.Disegno/StatoDocumento.cs', line: 'dati["proprieta"]', literal: 'proprieta', to: 'response_field' },
  { file: 'Strumenti/Attrezzi.cs', line: 'firma["lavorazione"]', literal: 'lavorazione', to: 'response_field' },
  { file: 'Riscontro.Disegno/Sezioni.cs', line: '["angolare"] =', literal: 'angolare', to: 'enum:tipo' },
  { file: 'Riscontro.Disegno/Sezioni.cs', line: 'case "angolare":', literal: 'angolare', to: 'enum:tipo' },
];
function siteFor(rel, line, lit, before) {
  for (const s of CS_SITES) if (s.file === rel && (s.literal === lit || s.literal === '*') && line.includes(s.line) && (!s.before || s.before.test(before || ''))) { s.used = true; return s; }
  return null;
}

// VALUE_SENSE_BY_FILE: for a literal with two senses that the code around it
// does not settle, the sense a file gives a word.
const VALUE_SENSE_BY_FILE = {
  'Riscontro.Disegno/StatoDocumento.cs': { alto: 'facing', basso: 'facing', davanti: 'facing' },
  'Riscontro.Disegno/OperazioniAccoppia.cs': { alto: 'facing', basso: 'facing', davanti: 'facing' },
  'Banco/CasiDichiarati.cs': { davanti: 'facing' },
  'Banco/ProvaNuove.cs': { davanti: 'facing' },
  'Riscontro.Disegno/OperazioniAnnotazioniTavola.cs': { alto: 'enum:lato', basso: 'enum:lato' },
  'Riscontro.Disegno/OperazioniCorpi.cs': { unisci: 'enum:operazione' },
  'Riscontro.Disegno/OperazioniDistintaBase.cs': { sopra: 'enum:disposizione', sotto: 'enum:disposizione' },
  'Riscontro.Disegno/OperazioniEsporta.cs': { fine: 'enum:qualita' },
  'Riscontro.Disegno/OperazioniSaldatura.cs': { fine: 'enum:posizione' },
  'Controlli/domande-senza-risposta.js': { tipo: 'response_field' },
  'Controlli/expected-keys-test.js': { tipo: 'response_field' },
};

function rewriteCs(d, text00, rel, report, opts) {
  const text0 = rel === OPERATIONS_CS ? enumWordsInOperationsCs(d, text00, opts.opCtx) : text00;
  const { text, guarded } = applySourceEditsFirst(d, rel, text0);
  const tokens = lexCs(text);
  const isOps = rel === OPERATIONS_CS;
  const frames = isOps ? callFrames(text, tokens) : null;
  const prot = protectedRanges(opts.root, rel, text).concat(guarded);
  const lineOf = (p) => { const a = text.lastIndexOf('\n', p - 1) + 1; let b = text.indexOf('\n', p); if (b < 0) b = text.length; return text.slice(a, b); };
  // the operation in context in the catalogue source: the last Nome = "..."
  const opStarts = [];
  if (isOps) { const rx = /\bNome\s*=\s*"([a-z0-9_]+)"/g; let m; while ((m = rx.exec(text))) opStarts.push([m.index, m[1]]); }
  const opAt = (p) => { let op = null; for (const [i, o] of opStarts) { if (i > p) break; op = o; } return op; };
  const edits = [];
  const conflicts = report.conflicts || (report.conflicts = []);
  const bump = (k) => { report[k] = (report[k] || 0) + 1; };
  for (const tok of tokens) {
    const line = lineOf(tok.start);
    if (line.includes(KEEP_MARK)) continue;
    if (insideAny(guarded, tok.start, tok.end)) continue;
    // a literal that IS a name of an Italian SOLIDWORKS is left whole; one
    // that holds one (an example, a message) has it masked
    if (tok.kind !== 'comment' && prot.some(([x, y]) => x <= tok.start && y >= tok.end)) continue;
    const tokProt = shift(prot.filter(([x, y]) => x < tok.end && y > tok.start), tok.start);
    const op = isOps ? opAt(tok.start) : (FILE_OPERATION[rel] || null);
    const oc = op && opts.opCtx ? opts.opCtx.get(op) : null;
    const ctxBase = { file: rel, conflicts, op, args: oc ? oc.args : null, enums: oc ? oc.enums : null, at: tok.start, text: line.trim().slice(0, 120),
      keep: op && PROSE_KEEP[d.opItalian(op)] ? new Set(PROSE_KEEP[d.opItalian(op)]) : null, defaultSense: isOps && op ? 'response_field' : undefined };
    if (tok.kind === 'comment') {
      const raw = text.slice(tok.start, tok.end);
      const to = rewriteProse(d, raw, 'plain', Object.assign({ prose: true }, ctxBase), shift(prot, tok.start));
      if (to !== raw) { edits.push({ start: tok.start, end: tok.end, text: to }); bump('comments'); }
      continue;
    }
    const raw = text.slice(tok.start, tok.end);
    const mode = tok.kind === 'vstr' || (tok.kind === 'ipart' && tok.verb) ? 'verbatim' : 'backslash';
    let to = null;
    const before = text.slice(Math.max(0, (tok.open !== undefined ? tok.open : tok.start) - 200), tok.open !== undefined ? tok.open : tok.start);
    const site = tok.kind === 'str' ? siteFor(rel, line, raw, before) : null;
    if (site && site.to === 'keep') continue;
    if (tok.kind === 'str' && !site && isPathPart(before, 'cs')) continue;
    const after = text.slice(tok.end + 1, tok.end + 200);
    const opsSense = isOps && tok.kind === 'str' ? operationsCsSense(frames.get(tok.start)) : null;
    if (opsSense && opsSense.sense === 'enum' && d.enumByArg.get(opsSense.prop) && d.enumByArg.get(opsSense.prop).has(raw)) {
      to = d.enumByArg.get(opsSense.prop).get(raw);
      bump('literals');
    } else if (tok.kind === 'str' && raw && /^[@A-Za-z_][\w.@=~()\-[\], ]*$/.test(raw) && !/\s/.test(raw.replace(/\([^)]*\)/g, ''))) {
      // a whole name, a dotted path, a selector, name=value
      let s = site ? { sense: site.to } : null;
      if (!s && isOps) s = operationsCsSense(frames.get(tok.start));
      if (!s && isTextSearch(before, raw)) continue;
      if (!s) s = codeSense(d, before, after);
      if (s && s.initializer && DICT_SENSE_BY_FILE[rel]) s = { sense: DICT_SENSE_BY_FILE[rel] };
      if (!s && isProseOperand(before, line, raw)) continue;
      to = codeName(d, raw, s, Object.assign({}, ctxBase), rel, line);
      if (to === undefined) to = rewriteProse(d, raw, mode, Object.assign({ prose: true }, ctxBase), tokProt);
      if (to !== null && to !== raw) {
        bump('literals');
        if (report.trace) report.trace.push({ file: rel, raw, to, pos: positionClass(before, after), line: line.trim().slice(0, 150) });
      }
    } else if (isOps && tok.kind === 'str' && operationsCsSense(frames.get(tok.start)) && operationsCsSense(frames.get(tok.start)).sense === 'example') {
      const json = raw.replace(/\\(["\\])/g, '$1');
      const back = json.replace(/[\\"]/g, '\\$&');
      if (back !== raw) cannot(rel + ': an example the window cannot respell: ' + raw.slice(0, 60));
      const nj = rewriteExample(d, json, op, rel);
      to = nj.replace(/[\\"]/g, '\\$&');
      if (to !== raw) bump('examples');
    } else if (tok.kind === 'str' && CASE_FREE_FILES.has(rel) && /^\s*[A-Za-z_]+\s*$/.test(raw) && caseFree(d, raw.trim()) !== null) {
      // a value in another case, with spaces around it (« INTERASSE »)
      to = raw.replace(raw.trim(), caseFree(d, raw.trim()));
      bump('literals');
    } else if (tok.kind === 'str' && d.rvalue.has(raw) && / /.test(raw)) {
      // a value of several words the bridge writes («il documento»)
      to = d.rvalue.get(raw);
      bump('literals');
    } else {
      to = rewriteProse(d, raw, mode, Object.assign({ prose: true }, ctxBase), tokProt);
      if (to !== raw) bump('messages');
    }
    if (to !== null && to !== raw) edits.push({ start: tok.start, end: tok.end, text: to });
  }
  return applyEdits(text, edits);
}
function shift(ranges, by) { return ranges.map(([a, b]) => [a - by, b - by]); }

// A plain word that is an operand of a conditional inside an Italian
// message is a word of the message, not a name: (n == 0 ? "nessuno" : ...),
// (voglio ? "fisso" : "libero") + "...".
function isProseOperand(before, line, raw) {
  if (/[_.@]/.test(raw)) return false;
  const b = before.replace(/\s+$/, '');
  const ternary = /\?$/.test(b) || (/:$/.test(b) && !/\bcase\s+"[^"]*"\s*:$/.test(b) && !/\b\w+\s*:$/.test(b.replace(/\?\s*"[^"]*"\s*:$/, '')) ) || /\?\s*"[^"]*"\s*:$/.test(b);
  if (!ternary) return false;
  // another literal of the line with a space in it, joined with +
  const others = line.match(/"(?:[^"\\]|\\.)*"/g) || [];
  return others.some(x => x !== '"' + raw + '"' && /\s/.test(x)) && /\+/.test(line);
}

// A whole-name literal of code, in its sense. Returns the new text, null to
// keep it, or undefined when it is prose after all.
function codeName(d, raw, s, ctx, rel, line) {
  if (s && s.sense === 'keep') return null;
  if (s && s.sense === 'prose') return undefined;
  if (s && s.sense && s.sense.startsWith('=')) return s.sense.slice(1) === raw ? null : (d.reverse ? null : s.sense.slice(1));
  if (raw.startsWith('@')) return rewriteSelector(d, raw);
  if (RX_PREFIX.test(raw)) { const base = raw.replace(/\.+$/, ''); const r = rewritePath(d, base, ctx.op); return r === base ? null : r + raw.slice(base.length); }
  if (RX_DOTTED.test(raw) || RX_ASSIGN.test(raw)) {
    return rewriteExpr(d, raw, Object.assign({}, ctx, { sense: s && s.sense === 'argument' ? 'argument' : undefined }));
  }
  if (!RX_NAME.test(raw)) {
    if (d.rvalue.has(raw)) return d.rvalue.get(raw);
    return undefined;
  }
  if (s && s.sense) {
    let to = null;
    if (s.sense === 'enum' || s.sense.startsWith('enum:')) {
      const prop = s.sense === 'enum' ? s.prop : s.sense.slice(5);
      const m = prop ? d.enumByArg.get(prop) : null;
      if (m && m.has(raw)) to = m.get(raw);
      else if (prop && d.catEnums && d.catEnums.get(prop) && d.catEnums.get(prop).has(raw)) return null;
    } else if (s.sense === 'example') return undefined;
    else to = inSense(d, raw, s.sense, ctx.op);
    if (to) return to === raw ? null : to;
  }
  // a literal of one or two letters (a, da, su) is a name only where the code
  // says so: elsewhere it is the ending of an Italian word ("rott" + "a")
  if (raw.length <= 2 && !d.reverse) return null;
  if (CASE_FREE_FILES.has(rel) && raw !== raw.toLowerCase()) { const to = caseFree(d, raw); if (to !== null) return to; }
  const u = unanimous(d, raw);
  if (u === null) return null;
  if (u !== CONFLICT) return u === raw ? null : u;
  const fileSense = VALUE_SENSE_BY_FILE[rel] && VALUE_SENSE_BY_FILE[rel][raw];
  if (fileSense) {
    // the file's decision; a word it does not rename stays as it is
    let to = null;
    if (fileSense.startsWith('enum:')) { const m = d.enumByArg.get(fileSense.slice(5)); if (m && m.has(raw)) to = m.get(raw); }
    else to = inSense(d, raw, fileSense, ctx.op);
    return to && to !== raw ? to : null;
  }
  ctx.conflicts.push({ name: raw, senses: sensesOf(d, raw), file: rel, at: ctx.at, op: ctx.op, text: line.trim().slice(0, 140), code: true });
  return null;
}

// FILE_OPERATION: the operation a source implements, when the names it reads
// depend on it (a_mm is the throat of a weld symbol and the offset of a
// section line; the map note of a_mm).
const FILE_OPERATION = {
  'Riscontro.Disegno/OperazioniSaldaturaTavola.cs': 'saldatura_in_tavola',
  'Riscontro.Disegno/OperazioniSezioneTavola.cs': 'sezione_in_tavola',
};
// PROSE_SENSE_BY_FILE and PROSE_SENSE_DEFAULT: the sense of a word with two
// English names when prose quotes it, by file and then for every file; keep
// leaves it as it is.
const PROSE_SENSE_BY_FILE = {
  'README.md': { saldature: 'catalogue_family', proprieta: 'catalogue_family' },
  'Banco/CasiDichiarati.cs': { saldature: 'keep' },
  'Banco/Orfane.cs': { alto: 'keep' },
  'Controlli/RaccordoDiFaccia/Programma.cs': { alto: 'keep' },
  'Banco/Attese.cs': { angolare: 'selector_field' },
  'Banco/pending-cases/README.md': { unisci: 'enum:operazione' },
  'Riscontro.Disegno/StatoDocumento.cs': { angolare: 'keep', proprieta: 'keep' },
  'Riscontro.Disegno/OperazioniProfili.cs': { misure: 'response_field' },
  'Controlli/domande-senza-risposta.js': { tipo: 'response_field' },
  'Prove/ProveIlRegistro.cs': { tipo: 'response_field' },
  'Riscontro.Disegno/OperazioniSw.cs': { unisci: 'argument' },
  'Riscontro.Disegno/OperazioniCorpi.cs': { tipo: 'keep' },
  'Riscontro.Disegno/OperazioniEsporta.cs': { fine: 'keep' },
  'Riscontro.Disegno/OperazioniSaldatura.cs': { fine: 'enum:posizione' },
  'Prove/ProveLAvanzamento.cs': { fine: 'keep' },
  'Controlli/lingua/snapshot.js': { fine: 'keep' },
  'Banco/tasks/14_plate_3d_print.md': { fine: 'keep' },
  'Prove/ProveEnumDiErrore.cs': { lavorazione: 'keep' },
  'Strumenti/Attrezzi.cs': { lavorazione: 'response_field' },
  'Riscontro.Disegno/OperazioniInterferenze.cs': { quante: 'response_field' },
};
const PROSE_SENSE_DEFAULT = {
  lungo: 'response_field', alto: 'facing', basso: 'facing', davanti: 'facing',
  misure: 'argument', tipo: 'argument', dove: 'argument', unisci: 'enum:operazione', lavorazione: 'argument',
  quante: 'argument', sopra: 'enum:disposizione', sotto: 'enum:disposizione', da_mm: 'argument', a_mm: 'argument',
  verso: 'argument', classe: 'argument', saldature: 'keep', proprieta: 'keep', angolare: 'keep', fine: 'keep',
};

// DICT_SENSE_BY_FILE: the sense of the keys of a dictionary written in place
// ({ ["formato"] = ... }) when a file writes arguments, not answers.
const DICT_SENSE_BY_FILE = { 'Prove/FormaSpellingContract.cs': 'argument' };
// CASE_FREE_FILES: files that spell enum values in other cases on purpose
// («Superiore», «DESTRO», «Faccia_Superiore»: the Forma accepts any case).
// There a value is renamed whatever its case, and keeps the case it had.
const CASE_FREE_FILES = new Set(['Prove/FormaSpellingContract.cs', 'Prove/Zc2110HelpersContract.cs']);
function caseFree(d, raw) {
  const lower = raw.toLowerCase();
  const u = unanimous(d, lower);
  if (!u || u === CONFLICT || !(d.universe.get(lower) || []).some(x => x.type.startsWith('enum:'))) return null;
  if (raw === raw.toUpperCase()) return u.toUpperCase();
  return u.split('_').map(p => p.charAt(0).toUpperCase() + p.slice(1)).join('_');
}

// PROSE_KEEP: words a description quotes that are data, not names, by
// operation: «tubo», «IPE», «angolare» are pieces of the names of the
// profile files of an Italian library that profili_saldatura searches.
const PROSE_KEEP = { profili_saldatura: ['tubo', 'angolare'] };

// QUOTED_NAMES: a piece an expectation looks for that is the name of an
// operation the answer quotes («revolve»), where the same word elsewhere is
// prose (the shape of a part says «pezzo di rivoluzione», and that stays).
// By case file: [Italian, English], the whole piece.
const QUOTED_NAMES = {
  'Banco/cases/features/loft_is_not_a_cone.json': [['rivoluzione', 'revolve']],
};

function quotedName(d, file, v) {
  const rows = QUOTED_NAMES[String(file).split(path.sep).join('/')];
  if (!rows) return null;
  for (const [it, en] of rows) {
    if (!d.reverse && v === it) return en;
    if (d.reverse && v === en) return it;
  }
  return null;
}

// SOURCE_EDITS: texts of a source the general rules cannot reach (a switch
// that maps the names of the selector to the fields of the state, a
// regular expression over names, a list of values written as plain words),
// by file: [Italian text, English text]. They are applied before the general
// pass, and the text they write is not rewritten again. Line ends are the
// file's own.
const SOURCE_EDITS = {
  'Banco/Attese.cs': [[
    '                case "asse":  return "lungo";\n' +
    '                case "lungo": return "lunghezza_mm";\n' +
    '                case "area":  return "area_mm2";\n',
    '                case "axis":   return "along";\n' +
    '                case "length": return "length_mm";\n' +
    '                case "area":   return "area_mm2";\n' +
    '                case "normal": return "facing";\n',
  ], [
    // the prefix is cut by its length, written as a number
    '            if (cosa.StartsWith("stato.", StringComparison.Ordinal))\n' +
    '                return Dentro(stato, cosa.Substring(6), cosa);\n' +
    '            return Dentro(misure, cosa.Substring(7), cosa);\n',
    '            if (cosa.StartsWith("state.", StringComparison.Ordinal))\n' +
    '                return Dentro(stato, cosa.Substring("state.".Length), cosa);\n' +
    '            return Dentro(misure, cosa.Substring("measurements.".Length), cosa);\n',
  ]],
  'Riscontro.Disegno/OperazioniTavola.cs': [[
    '                case "isometrica":\n' +
    '                case "isometrico":\n' +
    '                case "iso": return new[] { "isometri", "isometric" };\n' +
    '                case "dimetrica":\n' +
    '                case "dimetrico": return new[] { "dimetri", "dimetric" };\n' +
    '                case "trimetrica":\n' +
    '                case "trimetrico": return new[] { "trimetri", "trimetric" };\n' +
    '                case "fronte":\n' +
    '                case "anteriore":\n' +
    '                case "frontale": return new[] { "anterior", "front" };\n' +
    '                case "dietro":\n' +
    '                case "posteriore": return new[] { "posterior", "back" };\n' +
    '                case "sopra":\n' +
    '                case "superiore":\n' +
    '                case "alto": return new[] { "superior", "top" };\n' +
    '                case "sotto":\n' +
    '                case "inferiore":\n' +
    '                case "basso": return new[] { "inferior", "bottom" };\n' +
    '                case "destra": return new[] { "destra", "right" };\n' +
    '                case "sinistra": return new[] { "sinistr", "left" };\n',
    '                case "isometric": return new[] { "isometri", "isometric" };\n' +
    '                case "dimetric": return new[] { "dimetri", "dimetric" };\n' +
    '                case "trimetric": return new[] { "trimetri", "trimetric" };\n' +
    '                case "front": return new[] { "anterior", "front" };\n' +
    '                case "back": return new[] { "posterior", "back" };\n' +
    '                case "top": return new[] { "superior", "top" };\n' +
    '                case "bottom": return new[] { "inferior", "bottom" };\n' +
    '                case "right": return new[] { "destra", "right" };\n' +
    '                case "left": return new[] { "sinistr", "left" };\n',
  ]],
  'Riscontro.Disegno/Catalogo/Operazioni.cs': [[
    '"quale sua faccia, per verso: alto, basso, destra, "\n' +
    '                                    + "sinistra, davanti, dietro. Directions of the assembly: on a turned "',
    '"quale sua faccia, per verso: up, down, right, "\n' +
    '                                    + "left, front, back. Directions of the assembly: on a turned "',
  ]],
  'Mcp/Risorse.cs': [[
    // the parameter of the URI template, written apart from its prefix
    '["uriTemplate"] = PrefissoOperazione + "{nome}",',
    '["uriTemplate"] = PrefissoOperazione + "{name}",',
  ], [
    'PrefissoOperazione + "<nome>, oppure con lo strumento " +',
    'PrefissoOperazione + "<name>, oppure con lo strumento " +',
  ]],
  'Strumenti/Attrezzi.cs': [[
    // the families list_operations takes, as the catalogue names them: the
    // list was written by hand (and «corpi» was never a family)
    '                                    "solo una famiglia: estrusioni, lavorazioni, " +\n' +
    '                                    "riferimenti, corpi, saldature, proprieta\', tavola",\n',
    '                                    "solo una famiglia: extrusions, features, weldments, " +\n' +
    '                                    "references, assemblies, physical_properties, drawing, " +\n' +
    '                                    "checks, exports, identity, sheet_metal, dimensions",\n',
  ], [
    // the titles of the tools, which a client shows to the person
    '                case "guarda_il_pezzo": return "Guarda il pezzo aperto";\n' +
    '                case "elenco_operazioni": return "Elenco delle operazioni";\n' +
    '                case "descrivi_operazione": return "Descrivi una operazione";\n' +
    '                case "disegna": return "Disegna nel pezzo";\n' +
    '                case "misura": return "Misura il pezzo, senza toccarlo";\n' +
    '                case "annulla": return "Ritira l\'ultimo piano";\n' +
    '                case "documento": return "Apri, crea o scegli il documento";\n',
    '                case "inspect_part": return "Inspect the open part";\n' +
    '                case "list_operations": return "List the operations";\n' +
    '                case "describe_operation": return "Describe an operation";\n' +
    '                case "build": return "Build in the part";\n' +
    '                case "measure": return "Measure the part, without touching it";\n' +
    '                case "undo": return "Withdraw the last plan";\n' +
    '                case "document": return "Open, create or pick the document";\n',
  ]],
  'Prove/ProveICampiObbligatori.cs': [[
    // ^punto never matched punti; ^point would match points
    '@"_mm$|_gradi$|^denti$|^modulo|^segmenti$|^punto|_uid$|^facce$"',
    '@"_mm$|_deg$|^teeth$|^module|^segments$|^point(?!s)|_uid$|^faces$"',
  ]],
  'Prove/ProveAttese.cs': [[
    // a fake list of one: its name would be the only new Italian path of the window
    '                ["uno"] = new List<object> { "C2" },\n',
    '                ["one"] = new List<object> { "C2" },\n',
  ], [
    '                  Attese.Combacia(att("stato.uno", new List<object> { "C2" }),\n',
    '                  Attese.Combacia(att("state.one", new List<object> { "C2" }),\n',
  ], [
    // what the evaluator says about a missing field: the path as the case wrote it, the keys sorted
    '                       "campo assente: misure.non_esiste"));\n',
    '                       "campo assente: measurements.non_esiste"));\n',
  ], [
    '                       "facce_tolte, tolti_mm3, verso"));\n',
    '                       "faces_removed, facing, removed_mm3"));\n',
  ]],
  'Prove/ProveCatalogo.cs': [[
    // a name a little wrong finds the right one: the English names now
    '            Segna("«asole» trova «asola»", di("asole").Contains("asola"),\n' +
    '                  string.Join(", ", di("asole")));\n' +
    '            Segna("«foro» trova «foro_guidato»", di("foro").Contains("foro_guidato"),\n' +
    '                  string.Join(", ", di("foro")));\n',
    '            Segna("«slots» trova «slot»", di("slots").Contains("slot"),\n' +
    '                  string.Join(", ", di("slots")));\n' +
    '            Segna("«hole» trova «hole_wizard»", di("hole").Contains("hole_wizard"),\n' +
    '                  string.Join(", ", di("hole")));\n',
  ]],
};
// Documents: a word quoted as a word (the README count reads «N operazioni»),
// a name of before kept as history, a convention of names. An edit whose two
// sides are equal only keeps its text out of the general pass.
// the exceptions a test of the gate writes: `quote` is a class of the gate
// (detect-italian.js, left alone), not the catalogue family
SOURCE_EDITS['Prove/LanguageGateTests.cs'] = [
  [String.raw`                "{\"class\": \"quote\", \"file\": \"^README\\\\.md$\"}", "never a whole file",`,
   String.raw`                "{\"class\": \"quote\", \"file\": \"^README\\\\.md$\"}", "never a whole file",`],
  [String.raw`                "{\"class\": \"quote\", \"file\": \"^Banco/cases/x\\\\.json$\", \"json_path\": \"^nome$\"}", "only for the class detector-lexicon",`,
   String.raw`                "{\"class\": \"quote\", \"file\": \"^Banco/cases/x\\\\.json$\", \"json_path\": \"^nome$\"}", "only for the class detector-lexicon",`],
  [String.raw`                "{\"class\": \"quote\", \"pattern\": \"\\\\b(?:" + string.Join("|", functionWords) + ")\\\\b\"}", "function word",`,
   String.raw`                "{\"class\": \"quote\", \"pattern\": \"\\\\b(?:" + string.Join("|", functionWords) + ")\\\\b\"}", "function word",`],
  [String.raw`                ",\n {\"class\": \"quote\", \"pattern\": \"" + prose + " \\\\(again\\\\)\", \"applies_to\": [\"detector\"], " +`,
   String.raw`                ",\n {\"class\": \"quote\", \"pattern\": \"" + prose + " \\\\(again\\\\)\", \"applies_to\": [\"detector\"], " +`],
];
SOURCE_EDITS['README.md'] = [
  ["> «operazioni» e' indistinguibile, per chi legge in fretta, da quello che dice",
   "> «operazioni» e' indistinguibile, per chi legge in fretta, da quello che dice"],
];
// The timings of corri_piani.js are the runner's own record, one per plan
// file: `piano` there is the plan, not the plane of the map (the key stays
// for window 3, which renames the identifiers of the runner).
SOURCE_EDITS['Banco/corri_piani.js'] = [
  ['    tempi.push({ piano: path.basename(f), secondi });', '    tempi.push({ piano: path.basename(f), secondi });'],
  ['                  t.piano.padEnd(28) + t.secondi.toFixed(1) + " s"));', '                  t.piano.padEnd(28) + t.secondi.toFixed(1) + " s"));'],
];
// The message of an unknown resource names the template of the URI, written
// apart from its prefix as in Mcp/Risorse.cs.
SOURCE_EDITS['Programma.cs'] = [
  ['"«" + Risorse.PrefissoOperazione + "<nome>» per una sola." +', '"«" + Risorse.PrefissoOperazione + "<name>» per una sola." +'],
];
SOURCE_EDITS['Banco/pending-cases/README.md'] = [
  ['convenzione `nota_*` che il repository usava', 'convenzione `note_*` che il repository usava'],
  ['`misure.assunto` finché era una proposta', '`misure.assunto` finché era una proposta'],
];

// The extractor of the map reads the catalogue and the plans with the keys
// of the protocol; its Italian column is not touched.
SOURCE_EDITS['Controlli/lingua/name-map-extract.js'] = [
  ["  subActionArgument: 'azione',", "  subActionArgument: 'action',"],
  ["  operations: 'operazioni', families: 'famiglie', name: 'nome', family: 'famiglia', schema: 'schema',",
   "  operations: 'operations', families: 'families', name: 'name', family: 'family', schema: 'schema',"],
  ["  valueKeys: ['precondizione', 'documento'],", "  valueKeys: ['precondition', 'document'],"],
  ["const PLAN = { operation: 'operazione', args: 'argomenti' };", "const PLAN = { operation: 'operation', args: 'arguments' };"],
];

SOURCE_EDITS['Controlli/expected-keys-test.js'] = [
  // the wrong key each check feeds a reader is the name of before the window
  ["const BEFORE = { name: 'nome', steps: 'passi', operation: 'operazione' };",
   "const BEFORE = { name: 'nome', steps: 'passi', operation: 'operazione', operations: 'operazioni', families: 'famiglie', family: 'famiglia', kind: 'tipo' };"],
  ["renamed(realCatalogue, KEY.operations, 'operations')", "renamed(realCatalogue, KEY.operations, BEFORE.operations)"],
  ["withFirst(renamed(firstOp, KEY.name, 'name'))", "withFirst(renamed(firstOp, KEY.name, BEFORE.name))"],
  ["renamed(realCatalogue, KEY.families, 'families')", "renamed(realCatalogue, KEY.families, BEFORE.families)"],
  ["withFirst(renamed(firstOp, KEY.family, 'family'))", "withFirst(renamed(firstOp, KEY.family, BEFORE.family))"],
  ["[renamed(planStep, PLAN.operation, CASE.operation)]", "[renamed(planStep, PLAN.operation, BEFORE.operation)]"],
  ["renamed(renamed(record, KEY.kind, 'kind'), KEY.name, 'name')", "renamed(renamed(record, KEY.kind, BEFORE.kind), KEY.name, BEFORE.name)"],
];

// The titles of the tools (Strumenti/Attrezzi.cs, TitoloUmano), Italian to English.
const TOOL_TITLES = {
  'Guarda il pezzo aperto': 'Inspect the open part',
  'Elenco delle operazioni': 'List the operations',
  'Descrivi una operazione': 'Describe an operation',
  'Disegna nel pezzo': 'Build in the part',
  'Misura il pezzo, senza toccarlo': 'Measure the part, without touching it',
  "Ritira l'ultimo piano": 'Withdraw the last plan',
  'Apri, crea o scegli il documento': 'Open, create or pick the document',
};

// The same edits as the catalogue shows them, by operation (Italian name):
// [Italian text, English text].
const CATALOGUE_EDITS = {
  accoppia: [['per verso: alto, basso, destra, sinistra, davanti, dietro.', 'per verso: up, down, right, left, front, back.']],
};
function applySourceEditsFirst(d, rel, text) {
  const list = SOURCE_EDITS[rel];
  if (!list) return { text, guarded: [] };
  const eol = eolOf(text);
  let t = text;
  const spans = [];
  for (const [a, b] of list) {
    const [find, repl] = (d.reverse ? [b, a] : [a, b]).map(x => x.replace(/\n/g, eol));
    const k = t.indexOf(find);
    if (k < 0) {
      const done = t.indexOf(repl);
      if (done >= 0) { spans.push([done, done + repl.length]); continue; }   // a second run
      cannot(rel + ': SOURCE_EDITS cannot find «' + find.slice(0, 80) + '»: the table no longer matches the tree');
    }
    if (t.indexOf(find, k + 1) >= 0) cannot(rel + ': SOURCE_EDITS finds «' + find.slice(0, 80) + '» twice');
    t = t.slice(0, k) + repl + t.slice(k + find.length);
    for (const s of spans) if (s[0] > k) { s[0] += repl.length - find.length; s[1] += repl.length - find.length; }
    spans.push([k, k + repl.length]);
  }
  return { text: t, guarded: spans };
}

// ── a list of tools, as tools/list answers it (snapshot.js --window 2) ────
// The same rules as the C# pass gives Strumenti/Attrezzi.cs: the tool names,
// the titles, the arguments of each schema with their enum values (the
// action's values are sub-actions), and the names a description quotes.
function rewriteToolsList(d, tools) {
  const titleOf = (t) => { if (typeof t !== 'string') return t; for (const [it, en] of Object.entries(TOOL_TITLES)) { const [x, y] = d.reverse ? [en, it] : [it, en]; if (t === x) return y; } return t; };
  const schema = (sch, prop) => {
    if (!isObject(sch)) return sch;
    const out = {};
    for (const [k, v] of Object.entries(sch)) {
      if (k === 'properties' && isObject(v)) {
        const p = {};
        for (const [name, rule] of Object.entries(v)) p[inSense(d, name, 'argument', null) || name] = schema(rule, name);
        out[k] = p;
      } else if (k === 'required' && Array.isArray(v)) out[k] = v.map(x => (typeof x === 'string' ? inSense(d, x, 'argument', null) || x : x));
      else if (k === 'enum' && Array.isArray(v)) out[k] = v.map(x => toolValue(d, x, prop));
      else if (k === 'description' && typeof v === 'string') out[k] = rewriteProse(d, v, 'plain', { prose: true, file: 'Strumenti/Attrezzi.cs' }, null);
      else if (k === 'items') out[k] = schema(v, prop);
      else out[k] = v;
    }
    return out;
  };
  return tools.map((t) => {
    const o = {};
    for (const [k, v] of Object.entries(t)) {
      if (k === 'name') o[k] = d.tool.get(v) || v;
      else if (k === 'title') o[k] = titleOf(v);
      else if (k === 'description') o[k] = rewriteProse(d, v, 'plain', { prose: true, file: 'Strumenti/Attrezzi.cs' }, null);
      else if (k === 'inputSchema') o[k] = schema(v, null);
      else if (k === 'annotations' && isObject(v)) { o[k] = Object.assign({}, v); if ('title' in v) o[k].title = titleOf(v.title); }
      else o[k] = v;
    }
    return o;
  });
}
function toolValue(d, v, prop) {
  if (typeof v !== 'string') return v;
  const m = prop ? d.enumByArg.get(prop) : null;
  if (m && m.has(v)) return m.get(v);
  if (d.sub.has(v)) return d.sub.get(v);
  return rewriteValue(d, v, prop, null);
}

// What snapshot.js needs to read a snapshot taken before this window through
// its renames: the catalogue (text to text), the list of tools, and the
// values of the assertions of the cases.
function snapshotRenames(root, snapshotCatalogueText) {
  const map = loadMap(root);
  const opCtx = opContexts(snapshotCatalogueText);
  const d = direction(map, false, enumsOf(opCtx));
  d.catEnums = enumsOf(opCtx);
  for (const oc of opCtx.values()) addFacing(d, oc);
  d.opCtx = opCtx;
  return {
    d,
    catalogue: (text) => rewriteCatalogue(d, text, CATALOGUE_REL, {}),
    tools: (list) => rewriteToolsList(d, list),
    operation: (op) => (typeof op === 'string' ? d.op.get(op) || op : op),
    what: (w, op) => (typeof w === 'string' ? rewritePath(d, w, op) : w),
    value: (key, what, v, op) => expectationValue(d, key, what, v, op),
  };
}

// ── the ledger of the reverse run ─────────────────────────────────────────
// The forward run is not injective on this tree in two kinds of place: a
// name that was ALREADY English before the window (the cases and the
// descriptions written after 28/9/2026 say `orientation`, `volume_before_mm3`,
// `Right`), which the reverse would turn into the Italian name of the same
// English word; and a form the map merges (quante and quanti are both
// how_many). The forward run writes those places, word by word, into
// Controlli/lingua/window-2-reverse.json, and the reverse run reads it: so
// the reverse is the map read backwards PLUS a list of places where it must
// leave a word as it is (keep) or take another row of the map (row). The list
// holds no Italian: a kept word is English, a row is named by its type and
// its position among the rows with that English name.
const LEDGER_REL = 'Controlli/lingua/window-2-reverse.json';
function sha12(s) { return require('crypto').createHash('sha1').update(s, 'utf8').digest('hex').slice(0, 12); }
function words(s) {
  const parts = s.split(/([A-Za-z0-9_]+)/);
  // parts: [sep, word, sep, word, ..., sep]
  return parts;
}
function nodeAt(tree, at) {
  let n = tree;
  const steps = at.split('/');
  for (let i = 0; i < steps.length; i++) {
    const s = steps[i];
    if (s === 'k' || s === 'v') continue;
    const k = Number(s);
    if (n.type === 'object') {
      const e = n.entries[k];
      if (!e) return null;
      n = steps[i + 1] === 'k' ? e.key : e.value;
    } else if (n.type === 'array') { n = n.items[k]; if (!n) return null; } else return null;
  }
  return n;
}
function eachString(tree, fn) {
  const walk = (n, at) => {
    if (n.type === 'object') n.entries.forEach((e, i) => { fn(e.key, at + i + '/k'); walk(e.value, at + i + '/v/'); });
    else if (n.type === 'array') n.items.forEach((x, i) => walk(x, at + i + '/'));
    else if (n.type === 'string') fn(n, at.replace(/\/$/, ''));
  };
  walk(tree, '');
}
function computeLedger(map, oldText, newText, backText, file) {
  const o = parseJsonTree(oldText, file), n = parseJsonTree(newText, file), b = parseJsonTree(backText, file);
  const out = [];
  eachString(o, (on, at) => {
    const bn = nodeAt(b, at), nn = nodeAt(n, at);
    if (!bn || !nn || bn.type !== 'string' || nn.type !== 'string') cannot(file + ': the reverse run changed the shape at ' + at);
    if (bn.value === on.value) return;
    // a string the forward run left as it was, and the reverse would change
    // in its number of words: kept whole
    const wo = words(on.value), wn = words(nn.value), wb = words(bn.value);
    if (on.value === nn.value && wo.length !== wb.length) { out.push(at + '#all keep ' + sha12(nn.value)); return; }
    if (wo.length !== wn.length || wo.length !== wb.length) cannot(file + ': at ' + at + ' the window changed the number of words; no ledger can bring it back');
    for (let i = 1; i < wo.length; i += 2) {
      if (wb[i] === wo[i]) continue;
      if (wo[i] === wn[i]) { out.push(at + '#' + i + ' keep ' + wn[i]); continue; }
      // a word of two senses the reverse cannot settle (a merged form, or a
      // value several rows share): which row of the map it came from
      let found = null;
      for (const t of TYPES) {
        const rows = ledgerRows(map, t, wn[i]);
        const k = rows.findIndex(r => r.it === wo[i]);
        if (k >= 0) { found = t + ' ' + k; break; }
      }
      if (!found) cannot(file + ': at ' + at + ' the word «' + wo[i] + '» became «' + wn[i] + '» and the reverse gives «' + wb[i] + '»: the forward run changed a word it cannot bring back');
      out.push(at + '#' + i + ' row ' + found + ' ' + wn[i]);
    }
  });
  return out;
}
// The rows of a type whose English is a word: the whole English name, or the
// value of an enum_value or selector row (plane=right: right), in map order.
function ledgerRows(map, type, word) {
  const out = [];
  for (const [it, en] of map[type]) {
    if (en === word) out.push({ it });
    else if ((type === 'enum_value' || type === 'selector') && en.includes('=') && it.includes('=') && en.split('=')[1] === word) out.push({ it: it.split('=')[1] });
  }
  return out;
}

function applyLedger(map, entries, inputText, outText, file) {
  if (!entries || !entries.length) return outText;
  const inT = parseJsonTree(inputText, file), outT = parseJsonTree(outText, file);
  const byNode = new Map();
  for (const e of entries) {
    const m = /^(\S+)#(\d+|all) (keep|row) (?:(\S+) (\d+) )?(\S+)$/.exec(e);
    if (!m) cannot(LEDGER_REL + ': a bad entry for ' + file + ': ' + e);
    if (!byNode.has(m[1])) byNode.set(m[1], []);
    byNode.get(m[1]).push({ i: m[2] === 'all' ? 'all' : Number(m[2]), kind: m[3], type: m[4], n: m[5] === undefined ? null : Number(m[5]), token: m[6] });
  }
  const edits = [];
  for (const [at, list] of byNode) {
    const a = nodeAt(inT, at), b = nodeAt(outT, at);
    if (!a || !b || a.type !== 'string' || b.type !== 'string') cannot(LEDGER_REL + ': ' + file + ' has no string at ' + at);
    if (list.length === 1 && list[0].i === 'all') {
      if (sha12(a.value) !== list[0].token) cannot(LEDGER_REL + ': ' + file + ' at ' + at + ': the string kept whole is not the one the ledger names');
      stringEdit(edits, b, a.value, file);
      continue;
    }
    const wa = words(a.value), wb = words(b.value);
    if (wa.length !== wb.length) cannot(LEDGER_REL + ': ' + file + ' at ' + at + ': another number of words');
    for (const x of list) {
      if (wa[x.i] !== x.token) cannot(LEDGER_REL + ': ' + file + ' at ' + at + ' word ' + x.i + ' is «' + wa[x.i] + '», the ledger says «' + x.token + '»');
      if (x.kind === 'keep') wb[x.i] = wa[x.i];
      else {
        const rows = ledgerRows(map, x.type, x.token);
        if (!rows[x.n]) cannot(LEDGER_REL + ': no row ' + x.n + ' of ' + x.type + ' for «' + x.token + '»');
        wb[x.i] = rows[x.n].it;
      }
    }
    stringEdit(edits, b, wb.join(''), file);
  }
  return applyEdits(outText, edits);
}

// ── JavaScript ────────────────────────────────────────────────────────────
// Strings ('', "", the text parts of ``), comments and regular expressions
// (a slash after an operator or a keyword starts one).
function lexJs(t) {
  const out = []; let i = 0; const n = t.length;
  let last = '';   // the last significant token, to tell a regex from a division
  const regexAllowed = () => !last || /[(,=:[!&|?{};+\-*%<>~^]$/.test(last) || /^(?:return|typeof|case|in|of|delete|void|throw|new|else|do)$/.test(last);
  while (i < n) {
    const c = t[i];
    if (c === '/' && t[i + 1] === '/') { const e = t.indexOf('\n', i); const end = e < 0 ? n : e; out.push({ kind: 'comment', start: i, end }); i = end; continue; }
    if (c === '/' && t[i + 1] === '*') { const e = t.indexOf('*/', i + 2); out.push({ kind: 'comment', start: i, end: e + 2 }); i = e + 2; continue; }
    if (c === '"' || c === "'") {
      let j = i + 1;
      while (j < n && t[j] !== c) { if (t[j] === '\\') j++; if (t[j] === '\n') cannot('a newline inside a string at ' + i); j++; }
      out.push({ kind: 'str', q: c, start: i + 1, end: j }); i = j + 1; last = 'str'; continue;
    }
    if (c === '`') {
      let j = i + 1, part = j;
      for (;;) {
        if (j >= n) cannot('an unterminated template at ' + i);
        if (t[j] === '\\') { j += 2; continue; }
        if (t[j] === '`') { out.push({ kind: 'tpart', start: part, end: j }); j++; break; }
        if (t[j] === '$' && t[j + 1] === '{') {
          out.push({ kind: 'tpart', start: part, end: j });
          let depth = 1; j += 2;
          while (depth > 0 && j < n) {
            if (t[j] === '{') depth++; else if (t[j] === '}') depth--;
            else if (t[j] === '"' || t[j] === "'") { const q = t[j]; j++; while (j < n && t[j] !== q) { if (t[j] === '\\') j++; j++; } }
            if (depth > 0) j++;
          }
          j++; part = j; continue;
        }
        j++;
      }
      i = j; last = 'str'; continue;
    }
    if (c === '/' && regexAllowed()) {
      let j = i + 1, cls = false;
      while (j < n) { const ch = t[j]; if (ch === '\\') { j += 2; continue; } if (ch === '[') cls = true; else if (ch === ']') cls = false; else if (ch === '/' && !cls) break; else if (ch === '\n') break; j++; }
      out.push({ kind: 'regex', start: i, end: j + 1 }); i = j + 1; while (/[a-z]/.test(t[i] || '')) i++; last = 'regex'; continue;
    }
    if (/\s/.test(c)) { i++; continue; }
    if (/[A-Za-z0-9_$]/.test(c)) { let j = i; while (/[A-Za-z0-9_$]/.test(t[j] || '')) j++; last = t.slice(i, j); i = j; continue; }
    last = c; i++;
  }
  return out;
}

// The JavaScript and PowerShell files that read the bridge's protocol
// through members (catalogo.operazioni, passo.operazione, $op.nome): only
// these get their members renamed, and only names of the protocol: the
// names listed, or every name of the map but the members of the file's own
// objects (MEMBER_EXCLUDE: the wrapper a client builds around an answer).
const MEMBER_FILES = {
  'Controlli/conta-provate.js': ['operazioni', 'nome'],
  'Controlli/controlla-catalogo.ps1': ['operazioni', 'nome', 'famiglia', 'famiglie', 'sommario', 'descrizione', 'esempio', 'precondizione', 'documento'],
  'Controlli/domande-senza-risposta.js': ['nome', 'tipo', 'quando', 'simili'],
  'Controlli/prova-x2.js': 'protocol',
  'Banco/corri_piani.js': 'protocol', 'Banco/guarda.js': 'protocol', 'Banco/misura_accumulo.js': 'protocol',
  'Banco/prova_annulla.js': 'protocol', 'Banco/prova_faccia_foro.js': 'protocol', 'Banco/prova_forma.js': 'protocol',
  'Banco/prova_sezione.js': 'protocol', 'Banco/raccogli_viste.js': 'protocol',
};
const MEMBER_EXCLUDE = new Set(['testo', 'dati', 'errore', 'male', 'come', 'dettaglio']);
function memberName(d, s, rel) {
  const allowed = MEMBER_FILES[rel];
  if (!allowed || MEMBER_EXCLUDE.has(s)) return null;
  if (Array.isArray(allowed) && !allowed.includes(s)) return null;
  if (d.ckey.has(s) && (Array.isArray(allowed) || !d.arg.has(s))) return d.ckey.get(s);
  if (Array.isArray(allowed) && (s === 'tipo')) return d.field.get(s);
  const u = unanimous(d, s);
  if (u && u !== CONFLICT) return u;
  if (u === CONFLICT) return d.field.get(s) || d.arg.get(s) || null;
  return null;
}

function rewriteScriptCode(d, code, rel, isPs) {
  if (!MEMBER_FILES[rel]) return code;
  // members: .nome, $x.nome; object keys: { operazione: ... }
  let out = code.replace(/(\.)([A-Za-z_][A-Za-z0-9_]*)(?![A-Za-z0-9_(])/g, (all, dot, name, at, whole) => {
    if (/[0-9]/.test(whole[at - 1] || '')) return all;
    const to = memberName(d, name, rel);
    return to && to !== name ? dot + to : all;
  });
  if (!isPs) out = out.replace(/([{,]\s*)([A-Za-z_][A-Za-z0-9_]*)(\s*:)(?!:)/g, (all, pre, name, colon) => {
    const to = memberName(d, name, rel);
    return to && to !== name ? pre + to + colon : all;
  });
  return out;
}

// A plain word searched in a text (.Contains("copia"), .indexOf('lavorazioni'))
// is a word of a message, which stays Italian: kept unless a site says
// otherwise. A snake_case name searched in a text is a name the text quotes.
function isTextSearch(before, raw) {
  return !/[_.@]/.test(raw) && /\.(?:Contains|IndexOf|LastIndexOf|StartsWith|EndsWith|includes|indexOf|startsWith|endsWith)\(\s*$/.test(before);
}

// A literal handed to a call that builds a path (path.join, path.resolve,
// Path.Combine, Join-Path) is a folder or a file name, not a name of the
// protocol: the plans write {BANCO}/pezzo, the frames go to Banco/viste
// (which .gitignore names), and the folder is the one on disk. Folders and
// files are window 3 (W3.1-bis.6), never this one. The call must still be
// open where the literal stands (one level of nested calls inside it).
const RX_PATH_CALL_JS = /\bpath\.(?:join|resolve)\(\s*(?:[^()]|\([^()]*\))*$/;
const RX_PATH_CALL_CS = /\b(?:Path\.Combine|Path\.Join)\(\s*(?:[^()]|\([^()]*\))*$/;
const RX_PATH_CALL_PS = /\bJoin-Path\b[^\r\n|;]*$/;
function isPathPart(before, lang) {
  if (lang === 'js') return RX_PATH_CALL_JS.test(before);
  if (lang === 'cs') return RX_PATH_CALL_CS.test(before);
  return RX_PATH_CALL_PS.test(before);
}

function rewriteJs(d, text0, rel, report, opts) {
  const { text, guarded } = applySourceEditsFirst(d, rel, text0);
  const tokens = lexJs(text);
  const prot = protectedRanges(opts.root, rel, text);
  const edits = [];
  const conflicts = report.conflicts || (report.conflicts = []);
  const lineOf = (p) => { const a = text.lastIndexOf('\n', p - 1) + 1; let b = text.indexOf('\n', p); if (b < 0) b = text.length; return text.slice(a, b); };
  let pos = 0;
  const codeEdit = (a, b) => {
    if (b <= a) return;
    if (insideAny(guarded, a, b)) return;
    const seg = text.slice(a, b);
    if (lineOf(a).includes(KEEP_MARK)) return;
    const to = rewriteScriptCode(d, seg, rel, false);
    if (to !== seg) edits.push({ start: a, end: b, text: to });
  };
  for (const tok of tokens) {
    const tokStart = tok.kind === 'str' ? tok.start - 1 : tok.start;
    codeEdit(pos, tokStart);
    pos = tok.kind === 'str' ? tok.end + 1 : tok.end;
    if (tok.kind === 'regex') continue;
    if (insideAny(guarded, tok.start, tok.end)) continue;
    const line = lineOf(tok.start);
    if (line.includes(KEEP_MARK)) continue;
    const raw = text.slice(tok.start, tok.end);
    const ctx = { file: rel, conflicts, prose: true, at: tok.start, text: line.trim().slice(0, 120) };
    let to;
    if (tok.kind === 'comment') to = rewriteProse(d, raw, 'plain', ctx, shift(prot, tok.start));
    else {
      const mode = tok.kind === 'str' && tok.q === '"' ? 'backslash' : 'jsq';
      const before = text.slice(Math.max(0, tok.start - 200), tokStart);
      if (tok.kind === 'str' && isPathPart(before, 'js')) continue;
      const site = siteFor(rel, line, raw, before);
      if (site && site.to === 'keep') continue;
      if (raw && /^[@A-Za-z_][\w.@=~()\-[\]]*$/.test(raw)) {
        if (!site && isTextSearch(before, raw)) continue;
        const sense = site ? { sense: site.to } : codeSense(d, before, text.slice(tok.end + 1, tok.end + 200));
        to = codeName(d, raw, sense, Object.assign({}, ctx, { prose: false }), rel, line);
        if (to === undefined) to = rewriteProse(d, raw, mode, ctx, null);
        if (to === null) to = raw;
      } else to = rewriteProse(d, raw, mode, ctx, null);
    }
    if (to !== raw) edits.push({ start: tok.start, end: tok.end, text: to });
  }
  codeEdit(pos, text.length);
  return applyEdits(text, edits);
}

// ── PowerShell ────────────────────────────────────────────────────────────
function lexPs(t) {
  const out = []; let i = 0; const n = t.length;
  while (i < n) {
    const c = t[i];
    if (c === '<' && t[i + 1] === '#') { const e = t.indexOf('#>', i + 2); out.push({ kind: 'comment', start: i, end: e + 2 }); i = e + 2; continue; }
    if (c === '#') { const e = t.indexOf('\n', i); const end = e < 0 ? n : e; out.push({ kind: 'comment', start: i, end }); i = end; continue; }
    if (c === '@' && (t[i + 1] === "'" || t[i + 1] === '"') && /\r?\n/.test(t.slice(i + 2, i + 4))) {
      const q = t[i + 1];
      const close = new RegExp('\\r?\\n' + q + '@').exec(t.slice(i + 2));
      const end = i + 2 + close.index;
      out.push({ kind: q === "'" ? 'pstr1' : 'pstr2', start: i + 2, end }); i = end + close[0].length; continue;
    }
    if (c === "'") { let j = i + 1; for (;;) { if (j >= n) cannot('an unterminated string at ' + i); if (t[j] === "'") { if (t[j + 1] === "'") { j += 2; continue; } break; } j++; } out.push({ kind: 'pstr1', start: i + 1, end: j }); i = j + 1; continue; }
    if (c === '"') { let j = i + 1; for (;;) { if (j >= n) cannot('an unterminated string at ' + i); if (t[j] === '`') { j += 2; continue; } if (t[j] === '"') { if (t[j + 1] === '"') { j += 2; continue; } break; } j++; } out.push({ kind: 'pstr2', start: i + 1, end: j }); i = j + 1; continue; }
    i++;
  }
  return out;
}
function rewritePs(d, text, rel, report, opts) {
  const tokens = lexPs(text);
  const prot = protectedRanges(opts.root, rel, text);
  const edits = [];
  const conflicts = report.conflicts || (report.conflicts = []);
  const lineOf = (p) => { const a = text.lastIndexOf('\n', p - 1) + 1; let b = text.indexOf('\n', p); if (b < 0) b = text.length; return text.slice(a, b); };
  let pos = 0;
  const codeEdit = (a, b) => { if (b <= a) return; const seg = text.slice(a, b); const to = rewriteScriptCode(d, seg, rel, true); if (to !== seg) edits.push({ start: a, end: b, text: to }); };
  for (const tok of tokens) {
    const open = tok.kind === 'comment' ? tok.start : tok.start - 1;
    codeEdit(pos, open);
    pos = tok.kind === 'comment' ? tok.end : tok.end + 1;
    const line = lineOf(tok.start);
    if (line.includes(KEEP_MARK)) continue;
    const raw = text.slice(tok.start, tok.end);
    const ctx = { file: rel, conflicts, prose: true, at: tok.start, text: line.trim().slice(0, 120) };
    let to;
    if (tok.kind !== 'comment' && isPathPart(text.slice(Math.max(0, open - 200), open), 'ps')) continue;
    if (tok.kind === 'comment') to = rewriteProse(d, raw, 'plain', ctx, shift(prot, tok.start));
    else if (raw && /^[@A-Za-z_][\w.@=~()\-[\]]*$/.test(raw)) { to = codeName(d, raw, null, Object.assign({}, ctx, { prose: false }), rel, line); if (to === undefined || to === null) to = raw; }
    else to = rewriteProse(d, raw, tok.kind === 'pstr1' ? 'jsq' : 'ps2', ctx, null);
    if (to !== raw) edits.push({ start: tok.start, end: tok.end, text: to });
  }
  codeEdit(pos, text.length);
  return applyEdits(text, edits);
}

// ── any other text: documents, YAML ──────────────────────────────────────
function rewriteText(d, text0, rel, report, opts) {
  const { text, guarded } = applySourceEditsFirst(d, rel, text0);
  const prot = protectedRanges(opts.root, rel, text).concat(guarded);
  const conflicts = report.conflicts || (report.conflicts = []);
  if (!text.includes(KEEP_MARK)) return rewriteProse(d, text, 'plain', { file: rel, conflicts, prose: true }, prot);
  // line by line, keeping the marked ones
  let off = 0;
  return text.split('\n').map((l) => {
    const r = l.includes(KEEP_MARK) ? l : rewriteProse(d, l, 'plain', { file: rel, conflicts, prose: true }, shift(prot, off));
    off += l.length + 1;
    return r;
  }).join('\n');
}
// A JSON file that is neither the catalogue, a case nor a task: its string
// values are prose (keys stay).
function rewriteJsonValues(d, text, rel, report) {
  const tree = parseJsonTree(text, rel);
  const edits = [];
  const conflicts = report.conflicts || (report.conflicts = []);
  const walk = (n) => {
    if (n.type === 'object') n.entries.forEach(e => walk(e.value));
    else if (n.type === 'array') n.items.forEach(walk);
    else if (n.type === 'string') stringEdit(edits, n, rewriteProse(d, n.value, 'plain', { file: rel, conflicts, prose: true }, null), rel);
  };
  walk(tree);
  return applyEdits(text, edits);
}

// ── the files of a tree ───────────────────────────────────────────────────
const BINARY = new Set(['.png', '.jpg', '.jpeg', '.gif', '.bmp', '.ico', '.tif', '.tiff', '.webp',
  '.sldprt', '.sldasm', '.slddrw', '.sldlfp', '.sldmat', '.sldblk', '.slddrt', '.prtdot', '.asmdot', '.drwdot',
  '.step', '.stp', '.igs', '.iges', '.stl', '.x_t', '.x_b', '.dxf', '.dwg', '.dll', '.exe', '.pdb', '.lib',
  '.obj', '.snk', '.pfx', '.pdf', '.zip', '.7z', '.gz', '.tar', '.rar', '.nupkg', '.ttf', '.otf', '.woff',
  '.woff2', '.eot']);
const WALK_SKIP = new Set(['.git', 'bin', 'obj', 'dist', 'note-interne', 'node_modules', 'packages', '.vs']);
function listFiles(root, useGit) {
  if (useGit) {
    const r = cp.spawnSync('git', ['-c', 'core.quotepath=off', 'ls-files', '-z'], { cwd: root, encoding: 'utf8', maxBuffer: 1 << 28 });
    if (r.status !== 0) cannot('git ls-files failed in ' + root + ': ' + r.stderr);
    return r.stdout.split('\0').filter(Boolean).filter(f => fs.existsSync(path.join(root, f)));
  }
  const out = [];
  const walk = (dir, rel) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      if (e.isDirectory()) { if (!WALK_SKIP.has(e.name)) walk(path.join(dir, e.name), rel ? rel + '/' + e.name : e.name); }
      else out.push(rel ? rel + '/' + e.name : e.name);
    }
  };
  walk(root, '');
  return out.sort();
}
function git(root, args) {
  const r = cp.spawnSync('git', args, { cwd: root, encoding: 'utf8', maxBuffer: 1 << 28 });
  if (r.status !== 0) cannot('git ' + args.join(' ') + ': ' + (r.stderr || r.stdout));
  return r.stdout;
}
// The cases of the aliases (W3.1-bis.5, decision D4) are written with the
// names of before the window on purpose: the window and its proofs leave them
// alone. They go with the aliases in W3.1-bis.11.
const EXCLUDED_FOLDERS = ['Banco/cases/aliases/'];
const isCaseFile = (f) => CASE_TOPS.some(t => f.startsWith(t + '/')) && f.endsWith('.json') && !EXCLUDED_FOLDERS.some(x => f.startsWith(x));
const isTaskJson = (f) => f.startsWith(TASKS_DIR + '/') && f.endsWith('.json');
function kindOf(f) {
  if (EXCLUDED.has(f) || EXCLUDED_FOLDERS.some(x => f.startsWith(x))) return null;
  const ext = path.extname(f).toLowerCase();
  if (BINARY.has(ext)) return null;
  if (f === CATALOGUE_REL) return 'catalogue';
  if (isCaseFile(f)) return 'case';
  if (isTaskJson(f)) return 'task';
  if (ext === '.json') return 'json';
  if (ext === '.cs') return 'cs';
  if (ext === '.js') return 'js';
  if (ext === '.ps1' || ext === '.psm1') return 'ps';
  if (ext === '.md' || ext === '.yml' || ext === '.yaml' || ext === '.txt') return 'text';
  return null;
}
function opContexts(catText) {
  const out = new Map();
  if (!catText) return out;
  const c = JSON.parse(catText.replace(/^\ufeff/, ''));
  for (const o of c.operazioni || c.operations || []) out.set(o.nome || o.name, opContext(o.schema));
  return out;
}
function enumsOf(opCtx) {
  const out = new Map();
  for (const oc of opCtx.values()) for (const [a, v] of oc.enums) { if (!out.has(a)) out.set(a, new Set()); for (const x of v) out.get(a).add(x); }
  return out;
}

// ── the window ────────────────────────────────────────────────────────────
function runWindow(opts) {
  const map = loadMap(opts.root);
  const files = listFiles(opts.root, opts.git);
  const catPath = path.join(opts.root, CATALOGUE_REL);
  const catText = fs.existsSync(catPath) ? fs.readFileSync(catPath, 'utf8') : null;
  const opCtx = opContexts(catText);
  const d = direction(map, opts.reverse, enumsOf(opCtx));
  d.catEnums = enumsOf(opCtx);
  for (const oc of opCtx.values()) addFacing(d, oc);
  d.opCtx = opCtx;
  const ck = loadCaseKeys(opts.root);
  const report = {};
  const changed = [];
  const ledgerOut = {};
  // the catalogue first: the reverse direction of the ledger reads its enums
  // A forward run on a tree that is already after the window would read the
  // English names as the tree before it, and write a ledger of nonsense:
  // it stops (--check says whether a forward run would change anything).
  if (!opts.reverse && !opts.checkMode && catText !== null) {
    let english = false;
    try { english = Array.isArray(JSON.parse(catText).operations); } catch (e) { /* the run says why */ }
    if (english) cannot('the tree is already after the window (the catalogue has «operations»): nothing to do; --check says whether anything is left');
  }
  let newCat = catText;
  const writes = new Map();
  if (catText !== null && kindOf(CATALOGUE_REL)) newCat = rewriteCatalogue(d, catText, CATALOGUE_REL, report);
  const backOpCtx = opContexts(newCat);
  const back = direction(map, !opts.reverse, enumsOf(backOpCtx));
  back.catEnums = enumsOf(backOpCtx);
  for (const oc of backOpCtx.values()) addFacing(back, oc);
  back.opCtx = backOpCtx;
  const ledgerIn = opts.reverse ? readLedger(opts.root) : null;
  const sourceOpts = { root: opts.root, opCtx };
  for (const f of files) {
    const kind = kindOf(f);
    const full = path.join(opts.root, f);
    if (!kind) {
      // a file the window leaves alone (the map's extractor, Italian on
      // purpose) may still have lines that read the protocol: SOURCE_EDITS
      if (SOURCE_EDITS[f] && fs.existsSync(full)) {
        const text = fs.readFileSync(full, 'utf8');
        const out = applySourceEditsFirst(d, f, text).text;
        if (out !== text) { changed.push(f); writes.set(full, out); }
      }
      continue;
    }
    const buf = fs.readFileSync(full);
    if (buf.includes(0)) continue;
    const text = buf.toString('utf8');
    let out;
    try {
      if (kind === 'catalogue') out = newCat;
      else if (kind === 'case') out = rewriteCase(d, ck, text, f, report);
      else if (kind === 'task') out = rewriteTaskJson(d, text, f, report);
      else if (kind === 'json') out = rewriteJsonValues(d, text, f, report);
      else if (kind === 'cs' && f === OPERATIONS_CS) {
        out = opts.reverse ? rewriteCs(d, d12(text, true, report), f, report, sourceOpts)
          : d12(rewriteCs(d, text, f, report, sourceOpts), false, report);
      }
      else if (kind === 'cs') out = rewriteCs(d, text, f, report, sourceOpts);
      else if (kind === 'js') out = rewriteJs(d, text, f, report, sourceOpts);
      else if (kind === 'ps') out = rewritePs(d, text, f, report, sourceOpts);
      else out = rewriteText(d, text, f, report, sourceOpts);
    } catch (e) {
      if (e instanceof Stop) cannot(f + ': ' + e.message);
      throw e;
    }
    if (kind === 'catalogue' || kind === 'case' || kind === 'task') {
      if (!opts.reverse && !opts.checkMode) {
        // the ledger: where the reverse would not give this file back
        let b;
        if (kind === 'catalogue') b = rewriteCatalogue(back, out, f, {});
        else if (kind === 'case') b = rewriteCase(back, ck, out, f, {});
        else b = rewriteTaskJson(back, out, f, {});
        // the ledger counts the words of the names: the sentences D12 adds
        // to the catalogue are not part of it (the reverse run takes them
        // away before anything else)
        const named = kind === 'catalogue' ? d12(out, true) : out;
        const l = computeLedger(map, text, named, b, f);
        if (l.length) ledgerOut[f] = l;
        if (applyLedger(map, l, named, b, f) !== text) cannot(f + ': the reverse run with its ledger does not give the file back');
      } else if (ledgerIn && ledgerIn[f]) {
        out = applyLedger(map, ledgerIn[f], kind === 'catalogue' ? d12(text, true) : text, out, f);
      }
    }
    if (out !== text) { changed.push(f); writes.set(full, out); }
  }
  const unresolved = (report.conflicts || []).filter(c => c.code);
  const unusedSites = CS_SITES.filter(s => !s.used && (!opts.only || opts.only.has(s.file)));
  if (!opts.reverse && unresolved.length && !opts.allowConflicts) {
    for (const c of unresolved.slice(0, 80)) console.error('  ' + c.file + ': «' + c.name + '» (' + c.senses + ')' + (c.op ? ' in ' + c.op : '') + ': ' + c.text);
    cannot(unresolved.length + ' literal(s) with two senses and no context that decides: add them to CS_SITES');
  }
  if (!opts.reverse && !opts.checkMode && unusedSites.length && !opts.allowConflicts) cannot('CS_SITES names sites the tree does not have: ' + unusedSites.map(s => s.file + ' «' + s.literal + '» ' + s.line).join('; '));
  // The consumed strings (Controlli/lingua/consumed-strings.json): a consumer
  // literal the window renamed is bound again to its new spelling, at the same
  // position among the consumer's literals; its producer was renamed by the
  // same rules in the same run.
  const csFile = path.join(opts.root, 'Controlli/lingua/consumed-strings.json');
  let rebound = 0;
  if (fs.existsSync(csFile)) {
    const cs = require('./consumed-strings.js');
    const inv = JSON.parse(fs.readFileSync(csFile, 'utf8'));
    const langOf = (f) => (/\.cs$/i.test(f) ? 'cs' : /\.js$/i.test(f) ? 'js' : 'ps');
    for (const e of inv.entries || []) {
      const full = path.join(opts.root, e.consumer.file);
      if (!writes.has(full)) continue;
      const via = e.consumer.via ? { name: e.consumer.via, kind: e.consumer.kind, nocase: e.consumer.nocase } : null;
      const oldSites = cs.sitesOf(fs.readFileSync(full, 'utf8'), langOf(full), via).sites;
      const k = oldSites.findIndex(x => cs.hashOf(x.value) === e.consumer.sha1 && (!e.consumer.kind || x.kind === e.consumer.kind));
      if (k < 0) continue;
      const newSites = cs.sitesOf(writes.get(full), langOf(full), via).sites;
      if (newSites.length !== oldSites.length) cannot(e.consumer.file + ': the window changed the number of consumer literals; bind ' + e.id + ' by hand');
      const h = cs.hashOf(newSites[k].value);
      if (h === e.consumer.sha1) continue;
      e.consumer.sha1 = h;
      e.consumer.line_hint = newSites[k].line;
      rebound++;
    }
    if (rebound) writes.set(csFile, JSON.stringify(inv, null, 2) + '\n');
  }
  report.consumed_rebound = rebound;
  if (opts.dump) fs.writeFileSync(opts.dump, JSON.stringify({ conflicts: report.conflicts || [], changed }, null, 1));
  if (!opts.dry) {
    for (const [full, out] of writes) fs.writeFileSync(full, out);
    if (!opts.reverse) writeLedger(opts.root, ledgerOut);
  }
  const prose = (report.conflicts || []).filter(c => !c.code);
  if (opts.quiet) return { changed, report, writes, ledger: ledgerOut };
  console.log('window 2, the MCP surface: ' + (opts.reverse ? 'reverse (English to Italian)' : 'forward (Italian to English)') + (opts.dry ? ', dry run' : ''));
  const order = ['catalogue_keys', 'families', 'operations', 'cases', 'plans', 'argument_blocks', 'plan_keys', 'expectation_paths',
    'expectation_values', 'message_pieces', 'case_prose', 'task_paths', 'd12_examples', 'literals', 'examples', 'messages', 'comments', 'consumed_rebound'];
  for (const k of order) if (report[k] !== undefined) console.log('  ' + (k + ':').padEnd(28) + report[k]);
  console.log('  ' + 'files rewritten:'.padEnd(28) + changed.length);
  if (!opts.reverse) console.log('  ' + 'ledger entries:'.padEnd(28) + Object.values(ledgerOut).reduce((n, l) => n + l.length, 0) + ' in ' + Object.keys(ledgerOut).length + ' files');
  console.log('  ' + 'names left in prose, two senses:'.padEnd(28) + prose.length);
  if (opts.list) {
    for (const c of prose) console.log('    ' + c.file + (c.op ? ' (' + c.op + ')' : '') + ': «' + c.name + '» (' + c.senses + ')');
    for (const f of changed) console.log('  rewrote ' + f);
  }
  return { changed, report, writes, ledger: ledgerOut };
}

function readLedger(root) {
  const f = path.join(root, LEDGER_REL);
  if (!fs.existsSync(f)) return {};
  const j = JSON.parse(fs.readFileSync(f, 'utf8'));
  const out = {};
  for (const x of j.files || []) out[x.file] = x.entries;
  return out;
}
function writeLedger(root, files) {
  const sorted = Object.keys(files).sort().map(k => ({ file: k, entries: files[k] }));
  const j = {
    about: [
      'The ledger of rename window 2 (Controlli/lingua/window-2-surface.js, entry W3.1-bis.5), written by its forward run and read by its reverse run.',
      'Each entry is a word of a JSON file of the window (the catalogue, a case, a plan, a task) where the map read backwards would not give the tree before the window back: a name that was already English before the window (keep), or a form the map merges with another (row: the type and the position of the row among those with that English name).',
      'files: one item per JSON file, its path and its entries. An entry: <position of the string, as entry or item indices from the root, k for a key and v for a value>#<word index, or all> keep|row [<type> <n>] <the word now>.',
      'It goes with the map: archived in W3.1-bis.11.',
    ],
    format: 1,
    files: sorted,
  };
  fs.writeFileSync(path.join(root, LEDGER_REL), JSON.stringify(j, null, 1) + '\n');
}

module.exports = { snapshotRenames, rewriteToolsList, expectationValue, loadMap, direction, catalogueEnums, opContext, computeLedger, applyLedger, LEDGER_REL, runWindow, lexCs, lexJs, lexPs,
  rewriteCs, rewriteJs, rewritePs, rewriteText, opContexts, enumsOf, listFiles, rewriteSelector, rewritePath, rewriteValue, rewriteExpr, rewriteProse,
  rewriteCatalogue, rewriteCase, rewriteTaskJson, parseJsonTree, applyEdits, unanimous, CONFLICT, inSense, sensesOf,
  protectedRanges, EXCLUDED, KEEP_MARK, Stop, cannot };

// ── --check ───────────────────────────────────────────────────────────────
// The tree is after the window when a forward run on it changes nothing: no
// Italian name of the window is left where the window renames names (the
// catalogue, the cases, the plans, the code literals, the quoted names), and
// the ledger the run would write is the ledger in the tree.
function runCheck(opts) {
  const r = runWindow(Object.assign({}, opts, { dry: true, quiet: true, checkMode: true }));
  console.log('window 2, the MCP surface: check of the tree');
  console.log('  files a forward run would still change: ' + r.changed.length);
  for (const f of r.changed.slice(0, 40)) console.log('    ' + f);
  // every entry of the ledger names a word the tree has
  const map = loadMap(opts.root);
  const ledger = readLedger(opts.root);
  let entries = 0, bad = 0;
  for (const [file, list] of Object.entries(ledger)) {
    entries += list.length;
    const full = path.join(opts.root, file);
    if (!fs.existsSync(full)) { bad++; console.log('    the ledger names a file that is gone: ' + file); continue; }
    let t = fs.readFileSync(full, 'utf8');
    if (file === CATALOGUE_REL) t = d12(t, true);
    try { applyLedger(map, list, t, t, file); } catch (e) { bad++; console.log('    ' + e.message); }
  }
  console.log('  ledger of the reverse run:      ' + entries + ' entries in ' + Object.keys(ledger).length + ' files, ' + bad + ' that do not match the tree');
  return r.changed.length === 0 && bad === 0 ? 0 : 1;
}

// ── the proofs ────────────────────────────────────────────────────────────
function extractTree(root, rev, dir, tmp) {
  fs.mkdirSync(dir, { recursive: true });
  const tar = path.join(tmp, path.basename(dir) + '.tar');
  git(root, ['archive', '--format=tar', '-o', tar, rev]);
  // relative paths: GNU tar reads «C:» as a remote host
  const r = cp.spawnSync('tar', ['-xf', path.join('..', path.basename(tar))], { cwd: dir, encoding: 'utf8' });
  if (r.status !== 0) cannot('tar could not extract ' + rev + ': ' + r.stderr);
}
// The working tree as git would commit it: the tracked files and the ones
// not ignored, copied.
function copyWorkingTree(root, dir) {
  const r = cp.spawnSync('git', ['-c', 'core.quotepath=off', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], { cwd: root, encoding: 'utf8', maxBuffer: 1 << 28 });
  if (r.status !== 0) cannot('git ls-files failed: ' + r.stderr);
  for (const f of r.stdout.split('\0').filter(Boolean)) {
    const src = path.join(root, f);
    if (!fs.existsSync(src)) continue;
    const dst = path.join(dir, f);
    fs.mkdirSync(path.dirname(dst), { recursive: true });
    fs.copyFileSync(src, dst);
  }
}
const isProofFile = (f) => f === CATALOGUE_REL || isCaseFile(f) || isTaskJson(f);
function normJson(t) {
  const norm = (v) => { if (Array.isArray(v)) return v.map(norm); if (isObject(v)) { const o = {}; for (const k of Object.keys(v).sort()) o[k] = norm(v[k]); return o; } return v; };
  return JSON.stringify(norm(JSON.parse(t.replace(/^\ufeff/, ''))));
}
function compareTrees(a, b, label, onlyProof) {
  const fa = listFiles(a, false).filter(f => !onlyProof || isProofFile(f));
  const fb = new Set(listFiles(b, false).filter(f => !onlyProof || isProofFile(f)));
  const missing = [], differ = [], sameJson = [];
  let bytes = 0;
  for (const f of fa) {
    if (!fb.has(f)) { missing.push(f); continue; }
    fb.delete(f);
    const x = fs.readFileSync(path.join(a, f)), y = fs.readFileSync(path.join(b, f));
    if (x.equals(y)) { bytes++; continue; }
    if (f.endsWith('.json')) {
      let same = false;
      try { same = normJson(x.toString('utf8')) === normJson(y.toString('utf8')); } catch (e) { same = false; }
      if (same) { sameJson.push(f); continue; }
    }
    differ.push(f);
  }
  return { files: fa.length, bytes, sameJson, missing, extra: [...fb], differ, label };
}
function printComparison(c, what) {
  console.log('  ' + (what + ':').padEnd(34) + c.files + ' files, ' + c.bytes + ' identical bytes, ' + c.sameJson.length + ' identical JSON only, ' +
              c.missing.length + ' missing, ' + c.extra.length + ' extra, ' + c.differ.length + ' different');
  for (const f of c.missing.slice(0, 20)) console.log('    MISSING ' + f);
  for (const f of c.extra.slice(0, 20)) console.log('    EXTRA   ' + f);
  for (const f of c.differ.slice(0, 40)) console.log('    DIFFERS ' + f);
}

// --reverse-proof <rev>: the window backwards on HEAD (or, with --worktree,
// on the working tree) gives back the catalogue, the cases, the plans and
// the tasks of <rev>.
function runReverseProof(opts) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'window-2-'));
  const now = path.join(tmp, 'now'), old = path.join(tmp, 'old');
  if (opts.worktree) copyWorkingTree(opts.root, now); else extractTree(opts.root, 'HEAD', now, tmp);
  extractTree(opts.root, opts.proof, old, tmp);
  console.log((opts.worktree ? 'the working tree' : 'HEAD') + ' and ' + opts.proof + ' extracted under ' + tmp);
  const r = runWindow({ reverse: true, root: now, git: false, dry: false, quiet: true });
  console.log('the window backwards rewrote ' + r.changed.length + ' files, with the ledger of ' + Object.values(readLedger(now)).reduce((n, l) => n + l.length, 0) + ' entries');
  const c = compareTrees(old, now, 'reverse', true);
  console.log('');
  console.log('the reverse of ' + (opts.worktree ? 'the working tree' : 'HEAD') + ' against ' + opts.proof + ', the JSON files of the window (' + CATALOGUE_REL + ', ' + CASE_TOPS.join(', ') + ', ' + TASKS_DIR + '/*.json):');
  printComparison(c, 'catalogue, cases, plans, tasks');
  const all = compareTrees(old, now, 'all', false);
  console.log('  the other files (not part of the proof: comments and messages are not written to come back word for word):');
  console.log('    ' + all.differ.filter(f => !isProofFile(f)).length + ' of ' + all.files + ' differ after the reverse run');
  const ok = c.missing.length === 0 && c.extra.length === 0 && c.differ.length === 0;
  console.log(ok ? 'the window comes back: every path, and every JSON file of the window byte for byte' + (c.sameJson.length ? ' (' + c.sameJson.length + ' only after normalization)' : '')
                 : 'the window does NOT come back');
  try { fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) { /* left */ }
  return ok ? 0 : 1;
}

// --forward-proof <rev>: the window run on <rev> gives HEAD (or the working
// tree): the commit of the window is the script's output, and every other
// difference is listed (the hand-made part of the window).
function runForwardProof(opts) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'window-2-fwd-'));
  const now = path.join(tmp, 'now'), old = path.join(tmp, 'old');
  if (opts.worktree) copyWorkingTree(opts.root, now); else extractTree(opts.root, 'HEAD', now, tmp);
  extractTree(opts.root, opts.forwardProof, old, tmp);
  // the script and its map are this tree's: the window as it is now
  for (const f of [MAP_REL, 'Controlli/lingua/window-2-surface.js', 'Controlli/lingua/consumed-strings.js']) {
    if (fs.existsSync(path.join(now, f))) { fs.mkdirSync(path.dirname(path.join(old, f)), { recursive: true }); fs.copyFileSync(path.join(now, f), path.join(old, f)); }
  }
  const r = runWindow({ reverse: false, root: old, git: false, dry: false, quiet: true });
  console.log('the window run on ' + opts.forwardProof + ' rewrote ' + r.changed.length + ' files');
  const proof = compareTrees(old, now, 'forward', true);
  console.log('');
  console.log('the window on ' + opts.forwardProof + ' against ' + (opts.worktree ? 'the working tree' : 'HEAD') + ':');
  printComparison(proof, 'catalogue, cases, plans, tasks');
  const all = compareTrees(old, now, 'all', false);
  const others = all.differ.filter(f => !isProofFile(f));
  console.log('  every other file: ' + (all.files - others.length - all.missing.length) + ' of ' + all.files + ' as the script writes them; written by hand after it (' + others.length + '):');
  for (const f of others) console.log('    ' + f);
  for (const f of all.extra) console.log('    new: ' + f);
  for (const f of all.missing) console.log('    gone: ' + f);
  const ok = proof.missing.length === 0 && proof.extra.length === 0 && proof.differ.length === 0;
  console.log(ok ? 'the JSON files of the window are the script\'s output, byte for byte' : 'the JSON files of the window are NOT the script\'s output');
  try { fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) { /* left */ }
  return ok ? 0 : 1;
}

// ── --todo and --notes ────────────────────────────────────────────────────
// Code names only (decision D10): in notes the names are written between
// backticks, so only backtick spans, snake_case tokens and selectors are
// renamed; a word between guillemets in a note is a quote, not a name. In
// the TODO the closed entries and the (com'era) sections are history.
// Words the notes use for another subject than the argument or field of the
// map: `modello` in the memories is the model of graphify, not the template
// argument. Left as they are, and listed.
const NOTES_KEEP = new Set(['modello']);
// The entry of the TODO about the rename itself (W3.1-bis and its stages):
// its tables, decisions and rules quote the Italian names next to the
// English ones on purpose, and renaming them would make «`disegna` -> `build`,
// non `draw`» read «`build` -> `build`». Left whole, as history.
const RX_RENAME_ENTRY = /^(#{2,5}) (?:\[[ x~]\] )?W3\.1-bis\b/;
// A line that already has, as a word, the name a rename would write is a
// line about the rename (a table, an arrow, «X diventa Y»): left whole.
function namesBoth(line, out) {
  const a = words(line), b = words(out);
  const count = (list) => { const c = new Map(); for (let i = 1; i < list.length; i += 2) c.set(list[i], (c.get(list[i]) || 0) + 1); return c; };
  const ca = count(a), cb = count(b);
  for (const [w, n] of cb) if (n > (ca.get(w) || 0) && ca.has(w)) return w;
  return null;
}
function runNotes(opts) {
  const map = loadMap(opts.root);
  const catText = fs.existsSync(path.join(opts.root, CATALOGUE_REL)) ? fs.readFileSync(path.join(opts.root, CATALOGUE_REL), 'utf8') : null;
  // the notes are read in the Italian of before the window: the enums of the
  // catalogue of before decide the values (`fine`), so take them from the map
  const d = direction(map, !!opts.reverse, null);
  void catText;
  const files = [];
  for (const p of opts.notes) {
    if (!fs.existsSync(p)) cannot('no such file or folder: ' + p);
    if (fs.statSync(p).isDirectory()) {
      const walk = (dir) => { for (const e of fs.readdirSync(dir, { withFileTypes: true })) { const full = path.join(dir, e.name); if (e.isDirectory()) walk(full); else if (/\.md$/i.test(e.name)) files.push(full); } };
      walk(p);
    } else files.push(p);
  }
  let total = 0, filesChanged = 0;
  let notMentioning = 0;
  for (const file of files) {
    const text = fs.readFileSync(file, 'utf8');
    if (opts.onlyMentioning && !opts.onlyMentioning.test(text)) { notMentioning++; if (opts.list) console.log('skipped ' + file + ': it does not mention ' + opts.onlyMentioning.source); continue; }
    const eol = eolOf(text);
    const lines = text.split(/\r?\n/);
    let state = null, changed = 0, skipped = 0, inRename = 0, renameLevel = 0, both = 0;
    const shown = [], left = [], whole = [];
    for (let i = 0; i < lines.length; i++) {
      const l = lines[i];
      if (opts.todoRule) {
        const hl = /^(#{1,5}) /.exec(l);
        if (hl && renameLevel && hl[1].length <= renameLevel && !RX_RENAME_ENTRY.test(l)) renameLevel = 0;
        const re = RX_RENAME_ENTRY.exec(l);
        if (re && !renameLevel) renameLevel = re[1].length;
        if (renameLevel) { if (rewriteNoteLine(d, l) !== l) inRename++; continue; }
        const h = l.match(/^#{2,5} \[([ x~])\]/);
        if (h) state = h[1];
        else if (/^#{2,5} \(com'era\)/.test(l)) state = 'x';
        else if (/^#{1,5} /.test(l)) state = null;
        if (state === 'x') { if (rewriteNoteLine(d, l) !== l) skipped++; continue; }
      }
      if (l.includes(KEEP_MARK)) continue;
      const lineLeft = [];
      const n = rewriteNoteLine(d, l, lineLeft);
      for (const x of lineLeft) left.push('    ' + (i + 1) + ': `' + x.replace(/ \(.*$/, '') + '` left: ' + x.replace(/^\S+ /, ''));
      if (n === l) continue;
      const w = namesBoth(l, n);
      if (w) { both++; whole.push('    ' + (i + 1) + ': left whole, it already names `' + w + '`: ' + l.trim().slice(0, 150)); continue; }
      changed++; if (shown.length < (opts.list ? 1e9 : 8)) shown.push('    ' + (i + 1) + ': ' + n.trim().slice(0, 170)); lines[i] = n;
    }
    if (changed) filesChanged++;
    total += changed;
    console.log((changed ? (opts.dry ? 'would rewrite ' : 'rewrote ') : 'nothing in ') + file + (changed ? ': ' + changed + ' lines' : '') +
                (skipped ? ' (' + skipped + ' lines left in closed entries)' : '') +
                (inRename ? ' (' + inRename + ' lines left in the entry W3.1-bis, the rename itself)' : '') +
                (both ? ' (' + both + ' lines left whole: they name the old and the new name)' : '') +
                (left.length ? ' (' + left.length + ' names left: two senses or another subject; --list names them)' : ''));
    for (const s of shown) console.log(s);
    if (opts.list) { for (const s of whole) console.log(s); for (const s of left) console.log(s); }
    if (!opts.dry && changed) fs.writeFileSync(file, lines.join(eol));
  }
  console.log('window 2, code names in notes' + (opts.dry ? ' (dry run)' : '') + ': ' + total + ' lines in ' + filesChanged + ' of ' + files.length + ' files' +
              (notMentioning ? ' (' + notMentioning + ' skipped: they do not mention ' + opts.onlyMentioning.source + ')' : ''));
  return 0;
}
function rewriteNoteLine(d, line, left) {
  return rewriteProse(d, line, 'plain', { prose: true, notes: true, left: left || [] }, null);
}

function main() {
  let opts;
  try { opts = parseArgs(process.argv.slice(2)); } catch (e) { if (e instanceof Stop) { console.error(e.message); return 2; } throw e; }
  if (opts.help) { console.log(fs.readFileSync(__filename, 'utf8').split('\n').slice(1, 119).join('\n')); return 0; }
  try {
    if (opts.proof) return runReverseProof(opts);
    if (opts.forwardProof) return runForwardProof(opts);
    if (opts.check) return runCheck(opts);
    if (opts.todo) return runNotes(Object.assign({}, opts, { notes: [opts.todo], todoRule: true }));
    if (opts.notes) return runNotes(opts);
    runWindow(opts);
    return 0;
  } catch (e) {
    if (e instanceof Stop) { console.error('window-2-surface: ' + e.message); return 2; }
    throw e;
  }
}

if (require.main === module) process.exitCode = main();
