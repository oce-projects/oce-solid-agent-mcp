#!/usr/bin/env node
// =============================================================================
//  window-1-cases.js - rename window 1 of the translation: the bench cases
//  (entry W3.1-bis.4)
// =============================================================================
//
//  WHAT IT DOES
//
//  Driven by Checks/language/name-map.tsv (types case_key, free_case_key,
//  case_family, case_name, folder, file and summary_key), in this order:
//
//    1. moves the folders and files of the bench this window owns, with
//       `git mv`: Banco/casi (the case folders under it included),
//       Banco/casi-in-attesa, Banco/compiti and Banco/resoconto. A case file
//       takes the English case name, any other file the English file name.
//       Bench/ itself and the files at its root are renamed in window 3 (.6);
//    2. rewrites every case: the keys of the case schema (walked through the
//       contexts of Bench/case-keys.json, so an argument name is never taken
//       for a case key), the free notes to the "_" convention, the family,
//       the case name, the twin of a must-fail block, and every case name
//       and bench path written inside a value. Operations, arguments and
//       selectors stay as they are: they are the MCP surface, window 2 (.5).
//       A plan (a file whose root is an array) is the bridge's protocol: only
//       its free notes and the names inside its values are rewritten;
//    3. rewrites Bench/case-keys.json (the read keys, the documented keys,
//       the children; the frozen free keys are gone, they now start with "_");
//    4. rewrites the keys of the bench summary (Banco/resoconto/ultimo.json)
//       and the case names in it;
//    5. rewrites the bench paths, the case names and the `--famiglia <folder>`
//       values in every other tracked text file, except the files that hold
//       Italian on purpose or history (EXCLUDED below). A line that carries
//       the marker `window-1: keep` names both paths on purpose and is left
//       as it is, in either direction.
//
//  A JSON file is rewritten token by token: only the strings that change are
//  replaced, so indentation, line ends, the trailing newline, the key order
//  and the layout of each file stay as they were. case-keys.json is written
//  back as JSON.stringify with two spaces, the way it is kept.
//
//  It is re-runnable: a second run on its own output changes nothing. And it
//  runs backwards (--reverse): the map read from English to Italian, the
//  moves undone. The proof of the window is that the reverse run on the new
//  tree gives back the cases, the case keys and the summary of the old tree
//  byte for byte (--reverse-proof).
//
//  WHY A REVERSE NEEDS TWO SMALL TABLES
//
//  The map holds Italian names only. Two facts of the old tree are not in it,
//  and the reverse run needs them to put back what the forward run moved:
//    - ENGLISH_FROZEN_FREE: free notes frozen before the "_" convention that
//      were already written in English. Forward gives them the prefix; one of
//      them, the_numbers, becomes _the_numbers like the Italian note that
//      means the same, so
//      the table says in which files the English one was;
//    - ENGLISH_CASE_KEYS: keys of the case schema that were English and are
//      also the English name of an Italian key in another context ("name" of
//      an assembly configuration, while the case's "nome" becomes "name").
//  The forward run checks both tables against the tree and stops if the tree
//  says something else, so a rerun on a later main cannot lose the way back.
//
//  USAGE
//    node Checks/language/window-1-cases.js              the window, on this repository
//    node Checks/language/window-1-cases.js --reverse    the window backwards
//    node Checks/language/window-1-cases.js --todo <TODO.md>
//        only that file: bench paths, case names and --famiglia values, outside
//        the closed entries (the rule of Checks/todo-paths.js: an old
//        path inside a closed entry is history). Prints what it changed.
//    node Checks/language/window-1-cases.js --check
//        writes nothing: is the tree after the window? Counts the cases, the
//        plans, the twin references and whether they resolve, the frozen free
//        keys, and every Italian name of the window still in the bench. Exit 1
//        if anything is left.
//    node Checks/language/window-1-cases.js --reverse-proof <rev>
//        extracts HEAD into a temporary folder, runs --reverse there, and
//        compares the bench files of <rev> (cases, plans, case keys, summary,
//        tasks) with what came back: same paths, same bytes, same JSON.
//    node Checks/language/window-1-cases.js --load-proof <rev>
//        writes nothing: reads the cases of <rev> and of this tree the way the
//        bench's loader reads them and compares them position by position
//        (shape, keys only as the map says, strings only by renamed names,
//        the loader's child contexts); prints how the run order changes.
//    options:
//      --root <dir>     another tree (default: the repository of this file)
//      --no-git         plain file renames and a walk of the file system
//                       instead of git mv and git ls-files (a copy without .git)
//      --step moves|content|all   only the moves, only the rewrites, or both
//                       (default all); the moves alone keep git's rename
//                       detection exact, file by file
//      --dry-run        say what would change, write nothing
//
//  EXIT CODES
//    0  done (or nothing to do), --check clean, --reverse-proof identical,
//       --load-proof without differences
//    1  --check found Italian names of the window, --reverse-proof or
//       --load-proof differs
//    2  cannot run: a bad map, a name the map does not have, a collision, a
//       table that does not match the tree, a JSON file that does not parse
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const MAP_REL = 'Checks/language/name-map.tsv';

// ── what this window owns, in Italian (the names of the tree before it) ─────
const BENCH = 'Bench';                                   // renamed in .6
const TOPS = ['casi', 'casi-in-attesa', 'compiti', 'resoconto'];
const CASE_TOPS = ['casi', 'casi-in-attesa'];
const REGRESSION_FOLDER = 'regressione';
const CASE_KEYS_REL = 'Bench/case-keys.json';
const SUMMARY_REL = 'Banco/resoconto/ultimo.json';
// The folders of Banco/casi at 1ccb18a. `forma` and `pezzi` are not tracked
// (plans of hand runs, ignored by git): a path through them keeps its name.
const CASE_FOLDERS = ['asola', 'asse', 'assiemi', 'coppie', 'corpi', 'esporta', 'estrusioni', 'frase',
  'lamiera', 'lavorazioni', 'limiti', 'misura', 'piano', 'ponte', 'proprieta', 'quote', 'regressione',
  'ripeti', 'ritrovare', 'rivoluzione', 'saldature', 'smusso', 'specchia', 'tavola', 'telai', 'testo',
  'togliere', 'verifica', 'viste'];
// Files of the moved folders whose name is distinctive enough to be
// rewritten when a text names them alone (README.md is not: Bench/ keeps one).
const LONE_FILE_NAMES = ['ultimo.json', 'E2-i-due-casi.md', 'COME_SI_CONTA.md', 'staffa_a_elle.json',
  'telaio_da_un_metro.json'];
// Case names that are also plain words of the prose and of the code: written
// alone they are not rewritten (in a path, a twin or a case name they are).
const NOT_A_NAME_ALONE = new Set(['cilindro']);

// Files that keep their Italian on purpose, or history: never rewritten.
const EXCLUDED = new Set([
  'CHANGELOG.md',                               // history is not rewritten
  'Checks/language/name-map.tsv',              // the map: Italian on purpose
  'Checks/language/name-map-extract.js',       // its extractor: Italian on purpose
  'Checks/language/baseline.json',             // rewritten by detect-italian.js --write-baseline
  'Checks/language/window-1-cases.js',         // this file
]);

// ENGLISH_FROZEN_FREE: the frozen free notes of 1ccb18a written in English.
// Forward gives each the prefix "_"; reverse takes it off in these files
// (paths of the old tree) and puts the key back in the frozen list of its
// context. Checked against the tree by every forward run.
const ENGLISH_FROZEN_FREE = {
  'case/on_a_machine_with_mbd': ['Banco/casi/esporta/lim_step_ap242_needs_the_mbd_licence.json'],
  'case/shared_argument_with_the_twin': ['Banco/casi/esporta/lim_step_ap242_needs_the_mbd_licence.json',
    'Banco/casi/esporta/lim_step_ap242_wants_a_stp_path.json'],
  'case/the_body_by_order': ['Banco/casi/saldature/the_end_cap_skips_a_drilled_wall.json'],
  'case/the_numbers': ['Banco/casi/ripeti/the_circular_pattern_turns_around_a_straight_edge.json',
    'Banco/casi/ripeti/the_linear_pattern_follows_a_reference_axis.json'],
  'case/the_reply': ['Banco/casi/lavorazioni/the_state_counts_selected_edges_and_faces.json'],
  'case/what_it_catches': ['Banco/casi/ripeti/the_circular_pattern_turns_around_a_straight_edge.json',
    'Banco/casi/ripeti/the_linear_pattern_follows_a_reference_axis.json'],
  'step/the_signature': ['Banco/casi/ripeti/the_linear_pattern_follows_a_reference_axis.json'],
};
// ENGLISH_CASE_KEYS: English keys of the case schema at 1ccb18a that are also
// the English name of an Italian case key. Reverse leaves them alone in their
// context. Checked against Bench/case-keys.json by every forward run.
const ENGLISH_CASE_KEYS = { assembly_configuration: ['name'] };

const BINARY = new Set(['.png', '.jpg', '.jpeg', '.gif', '.bmp', '.ico', '.tif', '.tiff', '.webp',
  '.sldprt', '.sldasm', '.slddrw', '.sldlfp', '.sldmat', '.sldblk', '.slddrt', '.prtdot', '.asmdot', '.drwdot',
  '.step', '.stp', '.igs', '.iges', '.stl', '.x_t', '.x_b', '.dxf', '.dwg', '.dll', '.exe', '.pdb', '.lib',
  '.obj', '.snk', '.pfx', '.pdf', '.zip', '.7z', '.gz', '.tar', '.rar', '.nupkg', '.ttf', '.otf', '.woff',
  '.woff2', '.eot']);
const WALK_SKIP = new Set(['.git', 'bin', 'obj', 'dist', 'note-interne', 'node_modules', 'packages', '.vs']);

// ── small helpers ────────────────────────────────────────────────────────────
class Stop extends Error {}
function cannot(msg) { throw new Stop(msg); }
function escapeRx(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }
function eolOf(text) { return text.includes('\r\n') ? '\r\n' : '\n'; }
function isObject(v) { return v !== null && typeof v === 'object' && !Array.isArray(v); }

function parseArgs(argv) {
  const a = { reverse: false, root: ROOT_DEFAULT, git: true, step: 'all', dry: false, todo: null, check: false,
    proof: null, loadProof: null };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    if (x === '--reverse') a.reverse = true;
    else if (x === '--root') a.root = path.resolve(argv[++i] || cannot('--root needs a folder'));
    else if (x === '--no-git') a.git = false;
    else if (x === '--step') { a.step = argv[++i]; if (!['moves', 'content', 'all'].includes(a.step)) cannot('--step: moves, content or all'); }
    else if (x === '--dry-run') a.dry = true;
    else if (x === '--todo') a.todo = path.resolve(argv[++i] || cannot('--todo needs a file'));
    else if (x === '--check') a.check = true;
    else if (x === '--reverse-proof') a.proof = argv[++i] || cannot('--reverse-proof needs a revision');
    else if (x === '--load-proof') a.loadProof = argv[++i] || cannot('--load-proof needs a revision');
    else if (x === '--help' || x === '-h') { a.help = true; }
    else cannot('unknown argument ' + x);
  }
  return a;
}

// ── the map ──────────────────────────────────────────────────────────────────
function loadMap(root) {
  const file = path.join(root, MAP_REL);
  if (!fs.existsSync(file)) cannot('no name map at ' + file);
  const want = new Set(['case_key', 'free_case_key', 'case_family', 'case_name', 'folder', 'file', 'summary_key']);
  const m = {};
  for (const t of want) m[t] = new Map();
  for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    if (!line || line.startsWith('#')) continue;
    const c = line.split('\t');
    if (c[0] === 'type' || !want.has(c[0])) continue;
    if (!c[1] || !c[2]) cannot('a row of type ' + c[0] + ' without its English name: ' + line);
    if (m[c[0]].has(c[1])) cannot('the map has ' + c[0] + ' «' + c[1] + '» twice');
    m[c[0]].set(c[1], c[2]);
  }
  return m;
}

// One direction of the window: every table read from the source language to
// the target language. Forward is the map as written; reverse is its inverse,
// which must be a function (two Italian names on one English name would not
// come back).
function direction(map, reverse) {
  const pick = (t) => {
    if (!reverse) return new Map(map[t]);
    const inv = new Map();
    for (const [it, en] of map[t]) {
      if (inv.has(en) && inv.get(en) !== it) cannot('the ' + t + ' rows «' + inv.get(en) + '» and «' + it + '» share the English name «' + en + '»: the window would not come back');
      inv.set(en, it);
    }
    return inv;
  };
  const d = { reverse, key: pick('case_key'), free: pick('free_case_key'), family: pick('case_family'),
    name: pick('case_name'), folder: pick('folder'), file: pick('file'), summary: pick('summary_key') };
  const fwdFolder = map.folder;
  const need = (it) => { if (!fwdFolder.has(it)) cannot('the map has no folder row for «' + it + '»'); return fwdFolder.get(it); };
  // the names of this window, in the source language of the run
  const src = (it) => (reverse ? need(it) : it);
  d.bench = BENCH;
  d.tops = TOPS.map(src);
  d.caseTops = CASE_TOPS.map(src);
  d.caseFolders = CASE_FOLDERS.map(src);
  d.regressionFolder = src(REGRESSION_FOLDER);
  d.summaryRel = reverse ? 'Banco/' + need('resoconto') + '/' + map.file.get('ultimo.json') : SUMMARY_REL;  // window-3: keep (the folder of the bench before window 1)
  d.loneFiles = LONE_FILE_NAMES.map(f => (reverse ? (map.file.get(f) || cannot('no file row for ' + f)) : f));
  d.notAlone = new Set([...NOT_A_NAME_ALONE].map(n => (reverse ? map.case_name.get(n) : n)));
  for (const f of CASE_FOLDERS.concat(TOPS)) need(f);
  return d;
}

// ── paths ────────────────────────────────────────────────────────────────────
// The new path of a file of this window, or null when the file is not one.
function mapRel(d, rel) {
  const seg = rel.split('/');
  if (seg[0] !== d.bench || seg.length < 3 || !d.tops.includes(seg[1])) return null;
  const out = [seg[0], d.folder.get(seg[1])];
  const caseTop = d.caseTops.includes(seg[1]);
  for (let i = 2; i < seg.length; i++) {
    const s = seg[i];
    const last = i === seg.length - 1;
    if (!last) {
      if (caseTop && i === 2) {
        if (!d.caseFolders.includes(s)) cannot('a folder of the cases the window does not know: ' + rel + ' (add it to CASE_FOLDERS, with its row in the map)');
        out.push(d.folder.get(s));
      } else out.push(s);
      continue;
    }
    out.push(mapFileName(d, s, caseTop));
  }
  return out.join('/');
}
function mapFileName(d, name, caseTop) {
  if (caseTop && name.endsWith('.json')) {
    const stem = name.slice(0, -5);
    if (d.name.has(stem)) return d.name.get(stem) + '.json';
  }
  if (d.file.has(name)) return d.file.get(name);
  return name;
}

// Bench paths and case names inside a text, in the source language of the
// run. Separators: `/`, `\` and `\\` (a backslash written in a C#, JSON or
// JavaScript string).
function textRewriter(d) {
  const SEP = '(?:\\\\\\\\|\\\\|/)';
  const SEG = '[A-Za-z0-9_.*<>\\-]+';
  const tops = d.tops.slice().sort((a, b) => b.length - a.length).map(escapeRx).join('|');
  const full = new RegExp('(?<![A-Za-z0-9_\\-])(' + escapeRx(d.bench) + ')(' + SEP + ')(' + tops + ')((?:' + SEP + SEG + ')*)(?![A-Za-z0-9_\\-])', 'g');
  // a case folder written from Bench/ (`casi/tavola/...`), or the pending
  // cases; `compiti/` and `resoconto/` alone only before one of their files
  const caseTopAlone = d.caseTops.slice().sort((a, b) => b.length - a.length).map(escapeRx).join('|');
  const bare = new RegExp('(?<![A-Za-z0-9_\\-./\\\\])(' + caseTopAlone + '|' + escapeRx(d.tops[2]) + '|' + escapeRx(d.tops[3]) + ')((?:' + SEP + SEG + ')+)(?![A-Za-z0-9_\\-])', 'g');
  // a case folder followed by a case name: `quote/le_quote_arrivano_dal_modello`
  const folders = d.caseFolders.slice().sort((a, b) => b.length - a.length).map(escapeRx).join('|');
  const pair = new RegExp('(?<![A-Za-z0-9_\\-./\\\\])(' + folders + ')(' + SEP + ')([A-Za-z0-9_]+)(?![A-Za-z0-9_])', 'g');
  // a case folder followed by a glob of its cases: `ponte/c2_*`, `assiemi/*`
  // (the folder changes; the glob is a prefix of names and stays)
  const pairGlob = new RegExp('(?<![A-Za-z0-9_\\-./\\\\])(' + folders + ')(' + SEP + ')([A-Za-z0-9_]*\\*)', 'g');
  // the option of the bench and the parameter of bench.ps1 that pick one case folder
  const famOpt = new RegExp('(--?famiglia)(\\s+)(' + folders + ')(?![A-Za-z0-9_\\-])', 'gi');
  const lone = new RegExp('`(' + tops + ')(' + SEP + ')`', 'g');
  const loneFile = new RegExp('(?<![A-Za-z0-9_\\-./\\\\])(' + d.loneFiles.map(escapeRx).join('|') + ')(?![A-Za-z0-9_\\-])', 'g');

  const mapSegs = (segText, caseTop, startsAtTop) => {
    // segText: (SEP SEG)* ; returns the rewritten text and the count
    const parts = segText.split(new RegExp('(' + SEP + ')'));
    // parts: ['', sep, seg, sep, seg ...]
    let changed = 0;
    let level = startsAtTop ? 2 : 3;
    for (let i = 2; i < parts.length; i += 2) {
      const isLast = i === parts.length - 1;
      // a path that ends a sentence keeps its full stop out of the name
      const tail = isLast ? /\.*$/.exec(parts[i])[0] : '';
      const s = parts[i].slice(0, parts[i].length - tail.length);
      let n = s;
      if (caseTop && level === 2 && d.caseFolders.includes(s)) n = d.folder.get(s);
      else if (isLast || /\.[A-Za-z0-9]+$/.test(s)) n = mapFileName(d, s, caseTop);
      if (n !== s) { parts[i] = n + tail; changed++; }
      level++;
    }
    return { text: parts.join(''), changed };
  };

  return function rewrite(text, counts) {
    const c = counts || {};
    const bump = (k, n) => { c[k] = (c[k] || 0) + (n === undefined ? 1 : n); };
    let t = text.replace(full, (all, bench, sep, top, rest) => {
      const r = mapSegs(rest, d.caseTops.includes(top), true);
      bump('paths');
      return bench + sep + d.folder.get(top) + r.text;
    });
    t = t.replace(bare, (all, top, rest) => {
      const caseTop = d.caseTops.includes(top);
      const first = rest.split(new RegExp(SEP)).filter(Boolean)[0] || '';
      const known = caseTop
        ? (top === d.caseTops[1] || d.caseFolders.includes(first) || first.includes('*') || /\.(json|md)$/.test(first) || ['forma', 'pezzi', '<nome>', '<famiglia>'].includes(first))
        : (first.includes('*') || mapFileName(d, first, false) !== first || /.[A-Za-z0-9]+$/.test(first));
      if (!known) return all;
      const r = mapSegs(rest, caseTop, true);
      bump('paths');
      return d.folder.get(top) + r.text;
    });
    t = t.replace(pair, (all, folder, sep, name) => {
      if (!d.name.has(name)) return all;
      bump('paths');
      return d.folder.get(folder) + sep + d.name.get(name);
    });
    t = t.replace(pairGlob, (all, folder, sep, glob) => { bump('paths'); return d.folder.get(folder) + sep + glob; });
    // a folder of the window alone, between backticks: `casi/`, `resoconto\`
    t = t.replace(lone, (all, top, sep) => { bump('paths'); return '`' + d.folder.get(top) + sep + '`'; });
    t = t.replace(famOpt, (all, opt, sp, folder) => {
      if (!d.folder.has(folder)) return all;
      bump('family_options');
      return opt + sp + d.folder.get(folder);
    });
    t = t.replace(loneFile, (all, f) => { bump('file_names'); return d.file.get(f); });
    t = t.replace(/[A-Za-z0-9_]+/g, (tok, at, whole) => {
      if (!d.name.has(tok) || d.notAlone.has(tok)) return tok;
      // `piani/` was the folder of the plans of hand runs, never tracked: a
      // file named there is a record of how something was measured, not a case
      if (/piani[\\/]$/.test(whole.slice(Math.max(0, at - 6), at))) return tok;
      bump('case_names');
      return d.name.get(tok);
    });
    return t;
  };
}

// ── a JSON file, token by token ──────────────────────────────────────────────
// A tree whose strings remember where they are in the text: objects are
// { type: 'object', entries: [{ key: { value, start, end }, value: node }] },
// arrays { type: 'array', items }, strings { type: 'string', value, start, end },
// anything else { type: 'other' }.
function parseJsonTree(text, file) {
  let i = 0;
  const ws = () => { while (i < text.length && /[\s﻿]/.test(text[i])) i++; };
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
    return { type: 'other' };
  };
  const tree = val(); ws();
  if (i !== text.length) fail('text after the value');
  JSON.parse(text.replace(/^﻿/, '')); // the same verdict as the runtime
  return tree;
}

// The new text of a string token: the standard JSON spelling, which is the
// spelling of every file of the window (checked: a file that escapes another
// way is refused, never respelled in silence).
function respell(node, value, file) {
  if (node.raw !== JSON.stringify(node.value)) cannot(file + ': the string at ' + node.start + ' is not written as JSON.stringify writes it; the window would respell it');
  return JSON.stringify(value);
}

function applyEdits(text, edits) {
  edits.sort((a, b) => b.start - a.start);
  let t = text;
  let last = Infinity;
  for (const e of edits) {
    if (e.end > last) cannot('two edits overlap (internal error)');
    t = t.slice(0, e.start) + e.text + t.slice(e.end);
    last = e.start;
  }
  return t;
}

// ── the cases ────────────────────────────────────────────────────────────────
// Walks one case with the contexts of the case-keys list of the SOURCE tree.
// Collects edits (keys and values) and counts; `frozenSeen` gathers, in the
// reverse run, the frozen keys put back per context.
function rewriteCase(d, ctx, text, file, oldFile, rewriteText, counts, frozenSeen, englishFrozenSeen) {
  const tree = parseJsonTree(text, file);
  const edits = [];
  const bump = (k) => { counts[k] = (counts[k] || 0) + 1; };
  const stem = path.basename(file, '.json');
  const ownTarget = d.name.has(stem) ? d.name.get(stem) : null;

  // every string value: paths and names; plus the files of the components of
  // an assembly case, named after the case (`{LAVORO}\<case>_<component>`)
  const valueEdit = (node) => {
    let v = rewriteText(node.value, counts);
    if (ownTarget) v = v.replace(/[A-Za-z0-9_]+/g, (tok) => {
      if (tok.startsWith(stem + '_') && !d.name.has(tok)) { bump('own_name_prefix'); return ownTarget + tok.slice(stem.length); }
      return tok;
    });
    if (v !== node.value) edits.push({ start: node.start, end: node.end, text: respell(node, v, file) });
  };
  const values = (node) => {
    if (node.type === 'string') valueEdit(node);
    else if (node.type === 'array') node.items.forEach(values);
    else if (node.type === 'object') node.entries.forEach(e => values(e.value));
  };
  const keyEdit = (keyNode, to, kind) => {
    if (to === keyNode.value) return;
    edits.push({ start: keyNode.start, end: keyNode.end, text: respell(keyNode, to, file) });
    bump(kind);
  };
  const objectKeys = (entries, where) => {
    const seen = new Set();
    return (k) => { if (seen.has(k)) cannot(file + ': two keys «' + k + '» in ' + where + ' after the rename'); seen.add(k); };
  };

  if (tree.type === 'array') {
    // a plan: the bridge's protocol; only the free notes of its steps
    counts.plans = (counts.plans || 0) + 1;
    for (const step of tree.items) {
      if (step.type !== 'object') continue;
      const once = objectKeys(step.entries, 'a step');
      for (const e of step.entries) {
        const k = e.key.value;
        let to = k;
        if (k.startsWith('_') && d.free.has(k)) to = d.free.get(k);
        once(to);
        keyEdit(e.key, to, 'free_keys');
      }
    }
    values(tree);
    return { text: applyEdits(text, edits), changed: edits.length };
  }
  if (tree.type !== 'object') cannot(file + ': neither a case (an object) nor a plan (an array)');
  counts.cases = (counts.cases || 0) + 1;

  const folder = file.split('/')[2];
  const root = folder === d.regressionFolder ? ctx.roots.regression : ctx.roots.declared;
  const isRoot = (c) => c === ctx.roots.declared || c === ctx.roots.regression;
  const walk = (node, cName, where) => {
    const c = ctx.contexts[cName];
    if (!c) cannot(file + ': the context «' + cName + '» is not in the list of case keys');
    const once = objectKeys(node.entries, where);
    for (const e of node.entries) {
      const k = e.key.value;
      let to = k;
      let kind = 'case_keys';
      if (!d.reverse) {
        if (k.startsWith('_')) { if (d.free.has(k)) { to = d.free.get(k); kind = 'free_keys'; } }
        else if (c.frozen_free && Object.prototype.hasOwnProperty.call(c.frozen_free, k)) {
          kind = 'free_keys';
          if (d.free.has(k)) to = d.free.get(k);
          else {
            const id = cName + '/' + k;
            if (!ENGLISH_FROZEN_FREE[id] || !ENGLISH_FROZEN_FREE[id].includes(file))
              cannot(file + ': the frozen free key «' + id + '» has no row in the map and is not in ENGLISH_FROZEN_FREE');
            englishFrozenSeen.add(id + '@' + file);
            to = '_' + k;
          }
        } else if (d.key.has(k)) to = d.key.get(k);
      } else {
        const id = cName + '/' + k.slice(1);
        if (k.startsWith('_') && ENGLISH_FROZEN_FREE[id] && ENGLISH_FROZEN_FREE[id].includes(oldFile)) {
          to = k.slice(1); kind = 'free_keys';
          (frozenSeen[cName] = frozenSeen[cName] || new Set()).add(to);
        } else if (k.startsWith('_') && d.free.has(k)) {
          to = d.free.get(k); kind = 'free_keys';
          if (!to.startsWith('_')) (frozenSeen[cName] = frozenSeen[cName] || new Set()).add(to);
        } else if (!k.startsWith('_') && d.key.has(k) && !(ENGLISH_CASE_KEYS[cName] || []).includes(k)) to = d.key.get(k);
      }
      once(to);
      keyEdit(e.key, to, kind);
      // the family: a value of the map
      if (isRoot(cName) && (k === 'famiglia' || k === 'family') && d.key.get(k) !== undefined && e.value.type === 'string') {
        const v = e.value.value;
        if (d.family.has(v)) {
          edits.push({ start: e.value.start, end: e.value.end, text: respell(e.value, d.family.get(v), file) });
          bump('families');
          continue;
        }
      }
      if ((k === 'gemella' || k === 'twin') && d.key.has(k) && e.value.type === 'string') {
        bump('twin_references');
        if (!d.name.has(e.value.value)) bump('twin_references_unchanged');
      }
      if (isRoot(cName) && (k === 'nome' || k === 'name') && d.key.has(k) && e.value.type === 'string' && d.name.has(e.value.value)) bump('case_names_declared');
      // the children: another context
      const childCtx = c.children && c.children[k];
      if (childCtx) {
        if (e.value.type === 'object') { walk(e.value, childCtx, where + '.' + k); continue; }
        if (e.value.type === 'array') {
          e.value.items.forEach((it, n) => { if (it.type === 'object') walk(it, childCtx, where + '.' + k + '[' + (n + 1) + ']'); else values(it); });
          continue;
        }
      }
      values(e.value);
    }
  };
  walk(tree, root, 'the case');
  return { text: applyEdits(text, edits), changed: edits.length };
}

// The case-keys list, renamed. Forward: the read keys, documented keys and
// children by the map, the frozen free keys gone. Reverse: the same back, and
// the frozen lists rebuilt from the keys the reverse run met in the cases.
function rewriteCaseKeys(d, text, frozenSeen) {
  const j = JSON.parse(text);
  const eol = eolOf(text);
  const sortObj = (o) => { const r = {}; for (const k of Object.keys(o).sort()) r[k] = o[k]; return r; };
  for (const [cName, c] of Object.entries(j.contexts)) {
    const english = ENGLISH_CASE_KEYS[cName] || [];
    const tr = (k) => (d.reverse && english.includes(k)) ? k : (d.key.has(k) ? d.key.get(k) : k);
    c.read = c.read.map(tr).sort();
    const doc = {}; for (const k of Object.keys(c.documented)) doc[tr(k)] = c.documented[k];
    c.documented = sortObj(doc);
    const ch = {}; for (const k of Object.keys(c.children)) ch[tr(k)] = c.children[k];
    c.children = sortObj(ch);
    if (!d.reverse) c.frozen_free = {};
    else { const f = {}; for (const k of [...(frozenSeen[cName] || [])].sort()) f[k] = true; c.frozen_free = f; }
  }
  return (JSON.stringify(j, null, 2) + '\n').replace(/\n/g, eol);
}

// The checks of the tables against the old tree (forward only).
function checkTables(ctx, englishFrozenSeen) {
  const expected = new Set();
  for (const [id, files] of Object.entries(ENGLISH_FROZEN_FREE)) for (const f of files) expected.add(id + '@' + f);
  const missing = [...expected].filter(x => !englishFrozenSeen.has(x));
  if (missing.length) cannot('ENGLISH_FROZEN_FREE names uses the tree does not have: ' + missing.join(', '));
}
function checkEnglishCaseKeys(d, ctx) {
  const ens = new Set(d.key.values());
  for (const [cName, c] of Object.entries(ctx.contexts)) {
    const keys = c.read.concat(Object.keys(c.documented), Object.keys(c.children));
    for (const k of keys) {
      if (d.key.has(k)) continue;
      if (ens.has(k) && !(ENGLISH_CASE_KEYS[cName] || []).includes(k))
        cannot('the English case key «' + cName + '/' + k + '» is also the English name of an Italian key: add it to ENGLISH_CASE_KEYS, or reverse would rename it');
    }
    for (const k of Object.keys(c.frozen_free)) {
      if (d.free.has(k)) continue;
      if (!ENGLISH_FROZEN_FREE[cName + '/' + k]) cannot('the frozen free key «' + cName + '/' + k + '» has no row in the map and is not in ENGLISH_FROZEN_FREE');
    }
  }
}

// ── the summary of the bench ─────────────────────────────────────────────────
function rewriteSummary(d, text, file, rewriteText, counts) {
  const tree = parseJsonTree(text, file);
  if (tree.type !== 'object') cannot(file + ': the summary is not an object');
  const edits = [];
  for (const e of tree.entries) {
    const k = e.key.value;
    if (d.summary.has(k)) { edits.push({ start: e.key.start, end: e.key.end, text: respell(e.key, d.summary.get(k), file) }); counts.summary_keys = (counts.summary_keys || 0) + 1; }
  }
  const values = (node) => {
    if (node.type === 'string') { const v = rewriteText(node.value, counts); if (v !== node.value) edits.push({ start: node.start, end: node.end, text: respell(node, v, file) }); }
    else if (node.type === 'array') node.items.forEach(values);
    else if (node.type === 'object') node.entries.forEach(e => values(e.value));
  };
  values(tree);
  return applyEdits(text, edits);
}

// ── the files of a tree ──────────────────────────────────────────────────────
function listFiles(root, git) {
  if (git) {
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
  const r = cp.spawnSync('git', args, { cwd: root, encoding: 'utf8' });
  if (r.status !== 0) cannot('git ' + args.join(' ') + ': ' + (r.stderr || r.stdout));
  return r.stdout;
}

function moveFile(root, from, to, useGit, dry) {
  if (dry) return;
  fs.mkdirSync(path.dirname(path.join(root, to)), { recursive: true });
  if (useGit) git(root, ['mv', from, to]);
  else fs.renameSync(path.join(root, from), path.join(root, to));
}

function removeEmptyDirs(root, rels) {
  const dirs = new Set();
  for (const r of rels) { let p = path.dirname(r); while (p && p !== '.' && p !== BENCH) { dirs.add(p); p = path.dirname(p); } }
  for (const dir of [...dirs].sort((a, b) => b.length - a.length)) {
    const full = path.join(root, dir);
    try { if (fs.existsSync(full) && fs.readdirSync(full).length === 0) fs.rmdirSync(full); } catch (e) { /* kept */ }
  }
}

// ── the window ───────────────────────────────────────────────────────────────
function runWindow(opts) {
  const map = loadMap(opts.root);
  const d = direction(map, opts.reverse);
  const rewriteText = textRewriter(d);
  const counts = {};
  const files = listFiles(opts.root, opts.git);
  const say = (s) => console.log(s);

  // 1. the moves
  const moves = [];
  for (const f of files) {
    const to = mapRel(d, f);
    if (to && to !== f) moves.push([f, to]);
  }
  const targets = new Set();
  for (const [, to] of moves) { if (targets.has(to)) cannot('two files would become ' + to); targets.add(to); }
  for (const [from, to] of moves) if (files.includes(to) && !moves.some(m => m[0] === to)) cannot(from + ' would overwrite ' + to);
  const newOf = new Map(moves);
  const oldOf = new Map(moves.map(([a, b]) => [b, a]));

  if (opts.step !== 'content') {
    for (const [from, to] of moves) moveFile(opts.root, from, to, opts.git, opts.dry);
    if (!opts.dry && !opts.git) removeEmptyDirs(opts.root, moves.map(m => m[0]));
    if (!opts.dry && opts.git) removeEmptyDirs(opts.root, moves.map(m => m[0]));
    counts.files_moved = moves.length;
  }
  if (opts.step === 'moves') { report(counts, opts); return 0; }

  // where a file is now (after the moves, or before them in a dry run)
  // Every file by its path in the source tree and where it is now. After the
  // moves of this run, a moved file is at its new path; with --step content
  // the moves were made by an earlier run, so a file already at a new path
  // is read as the source file it came from.
  const inverse = direction(map, !opts.reverse);
  const srcOf = new Map();   // now -> source path
  const nowOf = new Map();   // source path -> now
  for (const f of files) {
    let src = f, now = f;
    if (opts.step === 'content') { const back = mapRel(inverse, f); if (back) src = back; }
    else if (!opts.dry) now = newOf.get(f) || f;
    srcOf.set(now, src); nowOf.set(src, now);
  }
  const here = (src) => nowOf.get(src) || src;
  const nowFiles = files.map(f => (opts.step === 'content' ? f : here(f)));
  const sourceRelOf = (now) => srcOf.get(now) || now;
  const sourceFiles = [...nowOf.keys()];

  // 2. the cases (with the list of case keys of the source tree)
  const ckPath = path.join(opts.root, CASE_KEYS_REL);
  if (!fs.existsSync(ckPath)) cannot('no list of case keys at ' + CASE_KEYS_REL);
  const ckText = fs.readFileSync(ckPath, 'utf8');
  const ctx = JSON.parse(ckText);
  // The tables describe the old tree: they are checked when the list still
  // holds Italian keys, not on a tree the window already renamed.
  const oldTree = Object.values(ctx.contexts).some(c => c.read.concat(Object.keys(c.documented), Object.keys(c.children)).some(k => map.case_key.has(k)));
  if (!d.reverse && oldTree) checkEnglishCaseKeys(d, ctx);
  const frozenSeen = {};
  const englishFrozenSeen = new Set();
  // The case files, by their path in the source tree (the folders in the
  // language the run reads) and where they are now (after the moves).
  const isCaseFile = (f) => { const s = f.split('/'); return s[0] === BENCH && s.length >= 3 && d.caseTops.includes(s[1]) && f.endsWith('.json'); };
  const caseFiles = [];
  const changedFiles = [];
  for (const src of sourceFiles.filter(isCaseFile)) {
    const now = here(src);
    caseFiles.push(now);
    const full = path.join(opts.root, now);
    const text = fs.readFileSync(full, 'utf8');
    // the path of the file in the old (Italian) tree, which the tables name
    const oldTreePath = d.reverse ? reverseOldPath(map, src) : src;
    const r = rewriteCase(d, ctx, text, src, oldTreePath, rewriteText, counts, frozenSeen, englishFrozenSeen);
    if (r.text !== text) { changedFiles.push(now); if (!opts.dry) fs.writeFileSync(full, r.text); }
  }
  if (!d.reverse && oldTree) checkTables(ctx, englishFrozenSeen);

  // 3. the list of case keys
  const ckNew = rewriteCaseKeys(d, ckText, frozenSeen);
  if (ckNew !== ckText) { changedFiles.push(CASE_KEYS_REL); counts.case_keys_list = 1; if (!opts.dry) fs.writeFileSync(ckPath, ckNew); }

  // 4. the summary
  const sumNow = here(d.summaryRel);
  const sumFull = path.join(opts.root, sumNow);
  if (fs.existsSync(sumFull)) {
    const t = fs.readFileSync(sumFull, 'utf8');
    const n = rewriteSummary(d, t, sumNow, rewriteText, counts);
    if (n !== t) { changedFiles.push(sumNow); if (!opts.dry) fs.writeFileSync(sumFull, n); }
  }

  // 5. every other text file
  const done = new Set(caseFiles.concat([CASE_KEYS_REL, sumNow]));
  const textCounts = {};
  for (const now of nowFiles) {
    if (done.has(now) || EXCLUDED.has(now) || EXCLUDED.has(sourceRelOf(now))) continue;
    if (BINARY.has(path.extname(now).toLowerCase())) continue;
    const full = path.join(opts.root, now);
    if (!fs.existsSync(full)) continue;
    const buf = fs.readFileSync(full);
    if (buf.includes(0)) continue;
    const text = buf.toString('utf8');
    const c = {};
    const n = rewriteKeepingMarkedLines(rewriteText, text, c);
    if (n !== text) {
      changedFiles.push(now);
      for (const [k, v] of Object.entries(c)) textCounts[k] = (textCounts[k] || 0) + v;
      if (!opts.dry) fs.writeFileSync(full, n);
    }
  }
  for (const [k, v] of Object.entries(textCounts)) counts['text_' + k] = v;
  counts.files_rewritten = changedFiles.length;
  report(counts, opts);
  if (opts.dry) for (const f of changedFiles) say('  would rewrite ' + f);
  return 0;
}

// A line that names both the old and the new path on purpose (the contract
// that recognises bench runs on either side of the move) carries KEEP_MARK and
// is never rewritten, in either direction: without it a rerun of step 5 would
// turn the old path into a second copy of the new one.
const KEEP_MARK = 'window-1: keep';
function rewriteKeepingMarkedLines(rewriteText, text, counts) {
  if (!text.includes(KEEP_MARK)) return rewriteText(text, counts);
  const kept = [];
  const masked = text.replace(/[^\r\n]*window-1: keep[^\r\n]*/g, (line) => { kept.push(line); return '\u0001' + (kept.length - 1) + '\u0001'; });
  return rewriteText(masked, counts).replace(/\u0001(\d+)\u0001/g, (all, i) => kept[Number(i)]);
}

// The path a file of the new tree had in the old tree (for the tables).
function reverseOldPath(map, now) {
  const d = direction(map, true);
  return mapRel(d, now) || now;
}

function report(counts, opts) {
  const dir = opts.reverse ? 'reverse (English to Italian)' : 'forward (Italian to English)';
  console.log('window 1, the bench cases: ' + dir + (opts.dry ? ', dry run' : '') + (opts.step !== 'all' ? ', step ' + opts.step : ''));
  const order = ['files_moved', 'cases', 'plans', 'case_keys', 'free_keys', 'families', 'case_names_declared',
    'twin_references', 'twin_references_unchanged', 'own_name_prefix', 'case_names', 'paths', 'file_names',
    'family_options', 'summary_keys', 'case_keys_list', 'text_paths', 'text_case_names', 'text_file_names',
    'text_family_options', 'files_rewritten'];
  for (const k of order) if (counts[k] !== undefined) console.log('  ' + (k + ':').padEnd(28) + counts[k]);
  for (const k of Object.keys(counts)) if (!order.includes(k)) console.log('  ' + (k + ':').padEnd(28) + counts[k]);
}

// ── --todo ───────────────────────────────────────────────────────────────────
function runTodo(opts) {
  const map = loadMap(opts.root);
  const d = direction(map, opts.reverse);
  const rewriteText = textRewriter(d);
  if (!fs.existsSync(opts.todo)) cannot('no TODO at ' + opts.todo);
  const text = fs.readFileSync(opts.todo, 'utf8');
  const eol = eolOf(text);
  const lines = text.split(/\r?\n/);
  let state = null;
  const counts = {};
  let changed = 0, skippedClosed = 0;
  const shown = [];
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i];
    const h = l.match(/^#{2,4} \[([ x~])\]/);
    if (h) state = h[1];
    // a `(com'era)` section is the original wording of an entry: history,
    // like a closed entry (and so read by Checks/todo-paths.js)
    else if (/^#{2,4} \(com'era\)/.test(l)) state = 'x';
    else if (/^#{1,4} /.test(l)) state = null;
    if (state === 'x') { if (rewriteText(l, {}) !== l) skippedClosed++; continue; }
    const n = rewriteText(l, counts);
    if (n !== l) { changed++; if (shown.length < 12) shown.push('  ' + (i + 1) + ': ' + n.slice(0, 160)); lines[i] = n; }
  }
  console.log('window 1, the TODO: ' + opts.todo + (opts.reverse ? ' (reverse)' : '') + (opts.dry ? ' (dry run)' : ''));
  console.log('  lines rewritten:            ' + changed);
  for (const [k, v] of Object.entries(counts)) console.log('  ' + (k + ':').padEnd(28) + v);
  console.log('  lines left in closed entries (history): ' + skippedClosed);
  for (const s of shown) console.log(s);
  if (!opts.dry && changed) fs.writeFileSync(opts.todo, lines.join(eol));
  return 0;
}

// ── --check ──────────────────────────────────────────────────────────────────
function runCheck(opts) {
  const map = loadMap(opts.root);
  const fwd = direction(map, false);
  const files = listFiles(opts.root, opts.git);
  const left = [];
  const newTops = TOPS.map(t => map.folder.get(t));
  const newCaseTops = CASE_TOPS.map(t => map.folder.get(t));
  for (const f of files) {
    const s = f.split('/');
    if (s[0] === BENCH && TOPS.includes(s[1])) left.push('an old path: ' + f);
    if (s[0] === BENCH && newCaseTops.includes(s[1]) && s.length >= 4 && CASE_FOLDERS.includes(s[2])) left.push('an old folder: ' + f);
    if (s[0] === BENCH && newCaseTops.includes(s[1]) && f.endsWith('.json') && fwd.name.has(path.basename(f, '.json'))) left.push('an Italian case name: ' + f);
  }
  const ckRel = CASE_KEYS_REL;
  const ck = JSON.parse(fs.readFileSync(path.join(opts.root, ckRel), 'utf8'));
  let frozen = 0;
  for (const [cName, c] of Object.entries(ck.contexts)) {
    frozen += Object.keys(c.frozen_free).length;
    for (const k of c.read.concat(Object.keys(c.documented), Object.keys(c.children))) if (fwd.key.has(k)) left.push('an Italian key in ' + ckRel + ': ' + cName + '/' + k);
  }
  const caseFiles = files.filter(f => { const s = f.split('/'); return s[0] === BENCH && newCaseTops.includes(s[1]) && f.endsWith('.json'); });
  const names = new Set(caseFiles.map(f => path.basename(f, '.json')));
  let cases = 0, plans = 0, twins = 0, twinFiles = 0, resolved = 0, italianKeys = 0, italianFree = 0;
  const twinKey = map.case_key.get('gemella');
  const walk = (node, cName, f, onTwin) => {
    const c = ck.contexts[cName];
    for (const [k, v] of Object.entries(node)) {
      if (k.startsWith('_')) { if (fwd.free.has(k)) { italianFree++; left.push('an Italian free note: ' + f + ' ' + k); } continue; }
      if (fwd.key.has(k)) { italianKeys++; left.push('an Italian case key: ' + f + ' ' + cName + '/' + k); }
      if (c.frozen_free[k]) { italianFree++; left.push('a frozen free note: ' + f + ' ' + k); }
      if (k === twinKey && typeof v === 'string') onTwin(v);
      const child = c.children[k];
      if (!child) continue;
      if (Array.isArray(v)) v.forEach(x => { if (isObject(x)) walk(x, child, f, onTwin); });
      else if (isObject(v)) walk(v, child, f, onTwin);
    }
  };
  for (const f of caseFiles) {
    const j = JSON.parse(fs.readFileSync(path.join(opts.root, f), 'utf8').replace(/^﻿/, ''));
    if (Array.isArray(j)) {
      plans++;
      j.forEach(s => { for (const k of Object.keys(s)) if (k.startsWith('_') && fwd.free.has(k)) left.push('an Italian free note in a plan: ' + f + ' ' + k); });
      continue;
    }
    cases++;
    const folder = f.split('/')[2];
    const root = folder === map.folder.get(REGRESSION_FOLDER) ? ck.roots.regression : ck.roots.declared;
    let mine = 0;
    walk(j, root, f, (v) => { twins++; mine++; if (names.has(v)) resolved++; else left.push('a twin that resolves to no case: ' + f + ' -> ' + v); });
    if (mine) twinFiles++;
    const fam = j[map.case_key.get('famiglia')];
    if (typeof fam === 'string' && fwd.family.has(fam)) left.push('an Italian family: ' + f + ' ' + fam);
  }
  // The code that reads a free note by name. Step 5 rewrites paths and case
  // names in the code, not free notes: a reader of `_verita` kept reading it
  // after the plans said `_truth`, and wrote "not declared" in silence.
  let codeReaders = 0;
  const freeRx = new RegExp('(?<![A-Za-z0-9_])(' + [...fwd.free.keys()].map(escapeRx).join('|') + ')(?![A-Za-z0-9_])');
  for (const f of files) {
    if (!/\.(js|cs|ps1|psm1)$/i.test(f) || EXCLUDED.has(f)) continue;
    const lines = fs.readFileSync(path.join(opts.root, f), 'utf8').split('\n');
    lines.forEach((l, i) => {
      const m = freeRx.exec(l);
      if (m) { codeReaders++; left.push('code that names an Italian free note: ' + f + ':' + (i + 1) + ' ' + m[1]); }
    });
  }
  const sum = path.join(opts.root, BENCH, map.folder.get('resoconto'), map.file.get('ultimo.json'));
  if (fs.existsSync(sum)) {
    for (const k of Object.keys(JSON.parse(fs.readFileSync(sum, 'utf8').replace(/^﻿/, '')))) if (fwd.summary.has(k)) left.push('an Italian summary key: ' + k);
  } else left.push('no summary at ' + path.relative(opts.root, sum));
  console.log('window 1, the bench cases: check of the tree');
  console.log('  case files:                 ' + caseFiles.length + ' (' + cases + ' cases, ' + plans + ' plans)');
  console.log('  twin references:            ' + twins + ' in ' + twinFiles + ' files, ' + resolved + ' resolve to a case');
  console.log('  frozen free keys:           ' + frozen);
  console.log('  Italian case keys left:     ' + italianKeys);
  console.log('  Italian free notes left:    ' + italianFree);
  console.log('  code naming them:           ' + codeReaders);
  console.log('  names of the window left:   ' + left.length);
  for (const l of left.slice(0, 40)) console.log('    ' + l);
  void newTops;
  return left.length ? 1 : 0;
}

// ── --reverse-proof ──────────────────────────────────────────────────────────
function runReverseProof(opts) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'window-1-'));
  const extract = (rev, dir) => {
    fs.mkdirSync(dir, { recursive: true });
    const tar = path.join(tmp, path.basename(dir) + '.tar');
    git(opts.root, ['archive', '--format=tar', '-o', tar, rev]);
    // relative paths: GNU tar reads «C:» as a remote host
    const r = cp.spawnSync('tar', ['-xf', path.join('..', path.basename(tar))], { cwd: dir, encoding: 'utf8' });
    if (r.status !== 0) cannot('tar could not extract ' + rev + ': ' + r.stderr);
  };
  const head = path.join(tmp, 'head');
  const old = path.join(tmp, 'old');
  extract('HEAD', head);
  extract(opts.proof, old);
  console.log('HEAD and ' + opts.proof + ' extracted under ' + tmp);
  const r = runWindow({ reverse: true, root: head, git: false, step: 'all', dry: false });
  if (r !== 0) return r;
  const inScope = (f) => { const s = f.split('/'); return (s[0] === BENCH && TOPS.includes(s[1])) || f === CASE_KEYS_REL; };
  const oldFiles = listFiles(old, false).filter(inScope);
  const backFiles = listFiles(head, false).filter(inScope);
  const missing = oldFiles.filter(f => !backFiles.includes(f));
  const extra = backFiles.filter(f => !oldFiles.includes(f));
  let bytes = 0, normalized = 0;
  const differ = [];
  const norm = (v) => { if (Array.isArray(v)) return v.map(norm); if (isObject(v)) { const o = {}; for (const k of Object.keys(v).sort()) o[k] = norm(v[k]); return o; } return v; };
  const groups = {};
  for (const f of oldFiles) {
    if (!backFiles.includes(f)) continue;
    const a = fs.readFileSync(path.join(old, f));
    const b = fs.readFileSync(path.join(head, f));
    const g = f === CASE_KEYS_REL ? 'case keys' : f.endsWith('.json') ? (f.split('/')[1] === 'resoconto' ? 'summary' : f.split('/')[1] === 'compiti' ? 'task json' : 'cases and plans') : 'other files';
    groups[g] = groups[g] || { files: 0, bytes: 0, json: 0 };
    groups[g].files++;
    if (a.equals(b)) { bytes++; groups[g].bytes++; if (f.endsWith('.json')) { normalized++; groups[g].json++; } continue; }
    if (f.endsWith('.json')) {
      let same = false;
      try { same = JSON.stringify(norm(JSON.parse(a.toString('utf8').replace(/^﻿/, '')))) === JSON.stringify(norm(JSON.parse(b.toString('utf8').replace(/^﻿/, '')))); } catch (e) { same = false; }
      if (same) { normalized++; groups[g].json++; differ.push(f + ' (same JSON, other bytes)'); continue; }
    }
    differ.push(f);
  }
  console.log('');
  console.log('the reverse of HEAD against ' + opts.proof + ', the files of the window (' + TOPS.map(t => BENCH + '/' + t).join(', ') + ', ' + CASE_KEYS_REL + '):');
  console.log('  files in ' + opts.proof + ':            ' + oldFiles.length);
  console.log('  files back after --reverse: ' + backFiles.length);
  console.log('  missing / extra paths:      ' + missing.length + ' / ' + extra.length);
  console.log('  identical bytes:            ' + bytes);
  console.log('  identical JSON (normalized):' + ' ' + normalized + ' of ' + oldFiles.filter(f => f.endsWith('.json')).length + ' JSON files');
  for (const [g, v] of Object.entries(groups)) console.log('    ' + (g + ':').padEnd(18) + v.files + ' files, ' + v.bytes + ' identical bytes' + (g === 'other files' ? '' : ', ' + v.json + ' identical JSON'));
  for (const f of missing.slice(0, 20)) console.log('  MISSING ' + f);
  for (const f of extra.slice(0, 20)) console.log('  EXTRA   ' + f);
  for (const f of differ.slice(0, 40)) console.log('  DIFFERS ' + f);
  const jsonDiffer = differ.filter(f => f.endsWith('.json'));
  const ok = missing.length === 0 && extra.length === 0 && jsonDiffer.length === 0;
  console.log(ok ? 'the window comes back: every path, and every JSON file byte for byte' + (differ.length ? ' (' + differ.length + ' other files differ, listed)' : '')
                 : 'the window does NOT come back');
  try { fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) { /* left */ }
  return ok ? 0 : 1;
}

// ── --load-proof ─────────────────────────────────────────────────────────────
// The cases load the same before and after, read the way the bench's loader
// reads them (Bench/DeclaredCases.cs): every JSON file of the case folders,
// a root array is a plan and a root object a case, the declared name must be
// the file's name, and the keys are walked through the contexts of each
// tree's own Bench/case-keys.json. Old and new are compared position by
// position, independently of the rewriting code above:
//   - same shape (types, array lengths, number and order of keys);
//   - a key may change only as the map says (case_key, free_case_key, or the
//     "_" prefix of a frozen note), and only where the loader walks: inside
//     a value the loader does not walk (arguments, selectors) no key changes;
//   - the child context of every walked key is the same in both trees, so
//     the loader descends the same way;
//   - a string may change only by old names of the map (case names,
//     families, folders, files) replaced with their new names.
// It also prints how the run order changes (the loader sorts the files of all
// families together by file name, so renaming files reorders the run).
function runLoadProof(opts) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'window-1-load-'));
  const old = path.join(tmp, 'old');
  fs.mkdirSync(old, { recursive: true });
  const tar = path.join(tmp, 'old.tar');
  git(opts.root, ['archive', '--format=tar', '-o', tar, opts.loadProof]);
  const r = cp.spawnSync('tar', ['-xf', path.join('..', 'old.tar')], { cwd: old, encoding: 'utf8' });
  if (r.status !== 0) cannot('tar could not extract ' + opts.loadProof + ': ' + r.stderr);
  const map = loadMap(opts.root);
  const fwd = direction(map, false);
  const frozenEnglish = new Set(Object.keys(ENGLISH_FROZEN_FREE).map(k => k.split('/')[1]));
  const readJson = (root, f) => JSON.parse(fs.readFileSync(path.join(root, f), 'utf8').replace(/^﻿/, ''));
  const ckOld = readJson(old, CASE_KEYS_REL);
  const ckNew = readJson(opts.root, CASE_KEYS_REL);
  const oldTops = CASE_TOPS;
  const newTops = CASE_TOPS.map(t => map.folder.get(t));
  const caseFilesOf = (root, tops) => listFiles(root, root === old ? false : opts.git).filter(f => { const s = f.split('/'); return s[0] === BENCH && tops.includes(s[1]) && f.endsWith('.json'); });
  const oldFiles = caseFilesOf(old, oldTops);
  const newFiles = new Set(caseFilesOf(opts.root, newTops));
  // the old names that a string may carry, longest first: the case names,
  // and the folders and files of this window only (the map also holds the
  // names of later windows, which stay). A family is a whole value.
  const windowFolders = new Set(TOPS.concat(CASE_FOLDERS));
  const windowFiles = new Set(listFiles(old, false).filter(p => { const s = p.split('/'); return s[0] === BENCH && TOPS.includes(s[1]); }).map(p => p.split('/').pop()));
  const folderAlternatives = [...windowFolders].flatMap(x => [x, map.folder.get(x)]).map(escapeRx).join('|');
  const renames = [];
  for (const [it, en] of map.case_name) if (it !== en) renames.push([it, en, 'case name']);
  for (const [it, en] of map.folder) if (it !== en && windowFolders.has(it)) renames.push([it, en, 'folder']);
  for (const [it, en] of map.file) if (it !== en && windowFiles.has(it)) renames.push([it, en, 'file']);
  renames.sort((a, b) => b[0].length - a[0].length);
  const stringChanges = {};
  const problems = [];
  let keysRenamed = 0, keysKept = 0, keysInsideValues = 0, cases = 0, plans = 0, steps = 0, expectations = 0, paired = 0;
  const sameString = (a, b, where) => {
    if (a === b) return;
    if (map.case_family.get(a) === b) { stringChanges.family = (stringChanges.family || 0) + 1; return; }
    let t = a;
    const used = new Set();
    for (const [it, en, kind] of renames) {
      if (!t.includes(it)) continue;
      // a case name is a whole word (a component file adds
      // `_<component>` after the case name); a folder or a file only as a
      // segment of a path
      // (a plain word that is also a case name, NOT_A_NAME_ALONE, only in a
      // path); a folder after a separator or starting a relative path; a
      // file only inside a folder of this window
      const END = '(?=[\\\\/`\'" ),.;:]|$)';
      const rx = kind === 'case name'
        ? new RegExp((NOT_A_NAME_ALONE.has(it) ? '(?<=[\\\\/])' : '(?<![A-Za-z0-9_])') + escapeRx(it) + '(?![A-Za-z0-9])', 'g')
        : kind === 'folder'
          ? new RegExp('(?:(?<=[\\\\/])|(?<![A-Za-z0-9_\\-.])(?=' + escapeRx(it) + '[\\\\/]))' + escapeRx(it) + END, 'g')
          : new RegExp('(?<=(?:' + folderAlternatives + ')[\\\\/])' + escapeRx(it) + END, 'g');
      const n = t.replace(rx, en);
      if (n !== t) { t = n; used.add(kind); }
    }
    if (t !== b) {
      let i = 0;
      while (i < t.length && t[i] === b[i]) i++;
      const from = Math.max(0, i - 30);
      problems.push(where + ': expected «…' + t.slice(from, i + 50) + '» found «…' + b.slice(from, i + 50) + '»');
      return;
    }
    for (const k of used) stringChanges[k] = (stringChanges[k] || 0) + 1;
  };
  const same = (a, b, where) => {   // a value the loader does not walk: keys stay
    if (Array.isArray(a)) {
      if (!Array.isArray(b) || a.length !== b.length) { problems.push(where + ': another array'); return; }
      a.forEach((x, i) => same(x, b[i], where + '[' + i + ']'));
      return;
    }
    if (isObject(a)) {
      if (!isObject(b)) { problems.push(where + ': not an object any more'); return; }
      const ka = Object.keys(a), kb = Object.keys(b);
      if (ka.length !== kb.length) { problems.push(where + ': another number of keys'); return; }
      ka.forEach((k, i) => { if (k !== kb[i]) { keysInsideValues++; problems.push(where + ': key ' + k + ' became ' + kb[i] + ' where the loader does not walk'); } else same(a[k], b[kb[i]], where + '.' + k); });
      return;
    }
    if (typeof a === 'string' && typeof b === 'string') { sameString(a, b, where); return; }
    if (a !== b) problems.push(where + ': ' + JSON.stringify(a) + ' became ' + JSON.stringify(b));
  };
  const walk = (a, b, cName, where) => {   // an object the loader walks in context cName
    const co = ckOld.contexts[cName], cn = ckNew.contexts[cName];
    if (!co || !cn) { problems.push(where + ': no context ' + cName); return; }
    const ka = Object.keys(a), kb = Object.keys(b);
    if (ka.length !== kb.length) { problems.push(where + ': another number of keys'); return; }
    ka.forEach((k, i) => {
      const n = kb[i];
      const allowed = k === n || fwd.key.get(k) === n || fwd.free.get(k) === n || (frozenEnglish.has(k) && '_' + k === n);
      if (!allowed) { problems.push(where + ': key ' + k + ' became ' + n); return; }
      if (k === n) keysKept++; else keysRenamed++;
      const childOld = co.children[k], childNew = cn.children[n];
      if ((childOld || null) !== (childNew || null)) { problems.push(where + '.' + n + ': the loader would read it as ' + (childNew || 'a value') + ', it read ' + (childOld || 'a value')); return; }
      const goes = a[k], vb = b[n];
      if (!childOld) { same(goes, vb, where + '.' + n); return; }
      if (childOld === 'step' || childOld === 'regression_step') steps += Array.isArray(goes) ? goes.length : 0;
      if (childOld === 'expectation' || childOld === 'regression_expectation') expectations += Array.isArray(goes) ? goes.length : 1;
      if (Array.isArray(goes)) {
        if (!Array.isArray(vb) || goes.length !== vb.length) { problems.push(where + '.' + n + ': another list'); return; }
        goes.forEach((x, j) => { if (isObject(x) && isObject(vb[j])) walk(x, vb[j], childOld, where + '.' + n + '[' + j + ']'); else same(x, vb[j], where + '.' + n + '[' + j + ']'); });
      } else if (isObject(goes) && isObject(vb)) walk(goes, vb, childOld, where + '.' + n);
      else same(goes, vb, where + '.' + n);
    });
  };
  const nameKeyOld = 'nome', nameKeyNew = map.case_key.get('nome');
  for (const f of oldFiles) {
    const nf = mapRel(fwd, f);
    if (!nf || !newFiles.has(nf)) { problems.push('no new file for ' + f + (nf ? ' (expected ' + nf + ')' : '')); continue; }
    newFiles.delete(nf);
    paired++;
    const a = readJson(old, f), b = readJson(opts.root, nf);
    if (Array.isArray(a) !== Array.isArray(b)) { problems.push(nf + ': a plan in one tree and a case in the other'); continue; }
    if (Array.isArray(a)) {
      plans++;
      if (a.length !== b.length) { problems.push(nf + ': another number of plan steps'); continue; }
      a.forEach((s, i) => {
        if (!isObject(s) || !isObject(b[i])) { same(s, b[i], nf + '[' + i + ']'); return; }
        const ka = Object.keys(s), kb = Object.keys(b[i]);
        if (ka.length !== kb.length) { problems.push(nf + '[' + i + ']: another number of keys'); return; }
        ka.forEach((k, j) => {
          const n = kb[j];
          if (k !== n && fwd.free.get(k) !== n) { problems.push(nf + '[' + i + ']: key ' + k + ' became ' + n); return; }
          if (k !== n) keysRenamed++; else keysKept++;
          same(s[k], b[i][n], nf + '[' + i + '].' + n);
        });
      });
      continue;
    }
    cases++;
    const isRegression = f.split('/')[2] === REGRESSION_FOLDER;
    const root = isRegression ? ckOld.roots.regression : ckOld.roots.declared;
    if (root !== (isRegression ? ckNew.roots.regression : ckNew.roots.declared)) problems.push(nf + ': another root context');
    // the loader's rule: the declared name is the file's name, in both trees
    if (!isRegression) {
      if (a[nameKeyOld] !== path.basename(f, '.json')) problems.push(f + ': declares ' + a[nameKeyOld]);
      if (b[nameKeyNew] !== path.basename(nf, '.json')) problems.push(nf + ': declares ' + b[nameKeyNew]);
    }
    walk(a, b, root, nf);
  }
  for (const f of newFiles) problems.push('a new file with no old one: ' + f);
  // the run order (Bench/DeclaredCases.cs: all families together, by file name)
  // the list of the loader's families: the string array of DeclaredCases.cs
  // whose every item is a folder of the cases of that tree
  const familiesOf = (root, top) => {
    const s = fs.readFileSync(path.join(root, BENCH, 'DeclaredCases.cs'), 'utf8');
    for (const m of s.matchAll(/string\[\] \w+ =\s*\{([\s\S]*?)\};/g)) {
      const items = [...m[1].matchAll(/"([^"]+)"/g)].map(x => x[1]);
      if (items.length && items.every(x => fs.existsSync(path.join(root, BENCH, top, x)))) return items;
    }
    cannot('no list of families in ' + path.join(root, BENCH, 'DeclaredCases.cs'));
  };
  const runOrder = (root, top, files) => { const fam = familiesOf(root, top); const base = (p) => p.split('/').pop(); return files.filter(p => { const s = p.split('/'); return s.length === 4 && s[1] === top && fam.includes(s[2]); }).sort((x, y) => (base(x) < base(y) ? -1 : base(x) > base(y) ? 1 : 0)); };
  const orderOld = runOrder(old, CASE_TOPS[0], oldFiles).map(p => mapRel(fwd, p));
  const orderNew = runOrder(opts.root, newTops[0], caseFilesOf(opts.root, newTops));
  const posNew = new Map(orderNew.map((p, i) => [p, i]));
  let samePos = 0, samePred = 0;
  orderOld.forEach((p, i) => { const j = posNew.get(p); if (j === i) samePos++; if (i > 0 && j > 0 && orderNew[j - 1] === orderOld[i - 1]) samePred++; });
  console.log('window 1, the bench cases: the cases of ' + opts.loadProof + ' against this tree, read as the loader reads them');
  console.log('  files paired:               ' + paired + ' of ' + oldFiles.length + ' (' + cases + ' cases, ' + plans + ' plans)');
  console.log('  steps / expectations:       ' + steps + ' / ' + expectations + ' (the same in both trees by construction of the walk)');
  console.log('  keys renamed by the map:    ' + keysRenamed + ' (kept: ' + keysKept + ')');
  console.log('  keys changed inside values: ' + keysInsideValues);
  console.log('  strings changed by names:   ' + (Object.entries(stringChanges).map(([k, v]) => k + ' ' + v).join(', ') || 'none'));
  console.log('  run order:                  ' + orderOld.length + ' cases in the families of the loader; ' + samePos + ' keep their position, ' + samePred + ' keep the case before them');
  console.log('  differences:                ' + problems.length);
  for (const p of problems.slice(0, 40)) console.log('    ' + p);
  try { fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) { /* left */ }
  return problems.length || orderOld.length !== orderNew.length ? 1 : 0;
}

function main() {
  let opts;
  try { opts = parseArgs(process.argv.slice(2)); } catch (e) { if (e instanceof Stop) { console.error(e.message); return 2; } throw e; }
  if (opts.help) { console.log(fs.readFileSync(__filename, 'utf8').split('\n').slice(1, 90).join('\n')); return 0; }
  try {
    if (opts.proof) return runReverseProof(opts);
    if (opts.loadProof) return runLoadProof(opts);
    if (opts.check) return runCheck(opts);
    if (opts.todo) return runTodo(opts);
    return runWindow(opts);
  } catch (e) {
    if (e instanceof Stop) { console.error('window-1-cases: ' + e.message); return 2; }
    throw e;
  }
}

if (require.main === module) process.exitCode = main();
module.exports = { loadMap, direction, mapRel, textRewriter, parseJsonTree };
