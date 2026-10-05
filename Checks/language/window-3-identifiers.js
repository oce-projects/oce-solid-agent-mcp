#!/usr/bin/env node
// =============================================================================
//  window-3-identifiers.js - rename window 3 of the translation: root folders,
//  projects, namespaces, files and identifiers (entry W3.1-bis.6)
// =============================================================================
//
//  WHAT IT DOES
//
//  Driven by Checks/language/name-map.tsv (types folder, file, project and
//  identifier), in steps, each its own commit so that git sees every move as
//  a rename:
//
//    --moves       every tracked path whose folder segments are `folder` rows
//                  or whose file name is a `file` row moves with `git mv`
//                  (nothing else changes: the commit is 100% renames);
//    --references  the text that names those paths and those projects:
//                  dotted project and namespace names, file names, path
//                  segments, assembly and executable names. Nothing else;
//    --identifiers <batch>   the `identifier` rows declared in one folder
//                  (C# through Roslyn, by symbol; JavaScript and PowerShell
//                  per file), with every reference to them in the tree.
//
//  HOW A PATH SEGMENT IS RECOGNISED. A word is renamed as a folder only where
//  it is a folder: a path token (words joined by `/` or `\`) whose run of
//  segments exists in the tree before the window, either from the root
//  (`Banco/casi/limiti`) or as a contiguous piece of a real path
//  (`casi/limiti/x.json`, `lingua/name-map.tsv`). An Italian sentence that
//  uses a slash for «or» (`piano/asse`) is not a path of the tree and stays.
//  The arguments of `Path.Combine`, `path.join` and `Join-Path` are read as
//  one path. A file name of the map (it always has an extension) is renamed
//  wherever it stands as a name, alone or at the end of a path - except at
//  the end of a path that is not this repository's (a folder the tree never
//  had, `OpenPDM.SwAddin/Programma.cs`). A dotted project name of the map
//  (`Riscontro.Disegno`, `Prove.Contratti`, `Controlli.AlberoRosso`) cannot
//  be prose and is renamed everywhere, also written for a regular expression
//  (`Riscontro\.Disegno`). The bare namespace segments (`Condiviso.Guinzaglio`,
//  `Ponte.Documenti`) are renamed only as qualifications in C# code. A
//  string, backtick or guillemet span whose whole content is a root folder
//  (`Banco`) or a probe of two words or more (`ProvaRuota`) is a name too;
//  a probe of one word only in the probe list of a PowerShell script; any
//  other folder word alone (`casi`, `Annulla`) is never renamed by rule, and
//  `--check` lists every one left so that each is judged by eye.
//
//  HISTORY AND OUTPUT FOLDERS. A path that the history before the window had
//  and the tree before the window no longer has (`Banco/casi/...`, renamed in
//  window 1) is history: the token is left whole. A folder that git does not
//  track (`Banco/viste/`, written by the scripts and ignored) keeps its own
//  name; only its tracked parents move.
//
//  WHAT IS NEVER REWRITTEN (history, or Italian on purpose): CHANGELOG.md,
//  the map, the language baseline (written by detect-italian.js
//  --write-baseline), the bench summary, this script; the lexicon and the
//  labelled samples of the gate's measure have only their path tokens
//  rewritten. A line that carries the marker `window-3: keep` is never
//  rewritten: it names the old and the new name on purpose.
//
//  THE PROOF (--reverse-proof <rev>) does not trust the forward run. It pairs
//  every file of <rev> with its path after the window (by the map), and every
//  line of a hunk of `git diff` with the line of the same rank: each word
//  that differs must be explained by pairs of the map (a whole word, or
//  dotted, dashed or path-separated parts of it, a file name's stem). The
//  hand edits of the window are written below with their reason, and listed
//  when used; any other difference is printed.
//
//  USAGE
//    node Checks/language/window-3-identifiers.js --moves [--dry-run]
//    node Checks/language/window-3-identifiers.js --references [--from <rev>] [--dry-run] [--list]
//    node Checks/language/window-3-identifiers.js --identifiers <batch> [--dry-run]
//    node Checks/language/window-3-identifiers.js --check [--from <rev>] [--list]
//        writes nothing: no path of the tree has an Italian segment of the
//        map; a forward run would change nothing (idempotent); the identifier
//        rows still written in the code; and, for judging by eye, every
//        folder word of the map left alone as a whole literal or span. Exit 1
//        if anything but the list for the eye is left.
//    node Checks/language/window-3-identifiers.js --reverse-proof <rev> [--worktree]
//        every difference between <rev> and HEAD (or the working tree)
//        explained by the map, as above.
//    node Checks/language/window-3-identifiers.js --todo <TODO.md> [--out <file>] [--dry-run] [--list]
//        only that file: the code names of paths and projects (decision D10),
//        outside the closed entries and the entry W3.1-bis itself; prose
//        stays Italian.
//    options: --root <dir> (another checkout), --from <rev> (the tree before
//    the window, for the path resolution; default 4c9e465, the base of the
//    window).
//
//  EXIT CODES
//    0  done, --check clean, the proof explained every difference
//    1  --check found something left, or the proof found a difference the
//       map does not explain
//    2  cannot run: a bad map, a collision of two moves, a bad argument
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const os = require('os');

const ROOT_DEFAULT = path.resolve(__dirname, '..', '..');
const BASE_REV = '4c9e465';
const KEEP_MARK = 'window-3: keep';

class Stop extends Error {}
function cannot(msg) { throw new Stop(msg); }
function escapeRx(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

function git(root, args, opts) {
  return cp.execFileSync('git', ['-C', root].concat(args), Object.assign({ encoding: 'utf8', maxBuffer: 1 << 30 }, opts || {}));
}
function trackedFiles(root) { return git(root, ['ls-files', '-z']).split('\0').filter(Boolean); }
function treeFiles(root, rev) { return git(root, ['ls-tree', '-r', '-z', '--name-only', rev]).split('\0').filter(Boolean); }
// every path that the history up to rev ever tracked
function historyFiles(root, rev) {
  return [...new Set(git(root, ['log', '--format=', '--name-only', rev]).split(/\r?\n/).filter(Boolean))];
}

// ── the map ────────────────────────────────────────────────────────────────
function mapPath(root) {
  for (const rel of ['Checks/language/name-map.tsv', 'Controlli/lingua/name-map.tsv']) {
    const p = path.join(root, rel);
    if (fs.existsSync(p)) return p;
  }
  cannot('name-map.tsv not found under ' + root);
}
function loadMap(root) {
  const text = fs.readFileSync(mapPath(root), 'utf8').replace(/^\ufeff/, '');
  const m = { folder: new Map(), file: new Map(), project: new Map(), identifier: new Map(), notes: new Map() };
  let header = true;
  for (const line of text.split(/\r?\n/)) {
    if (!line || line.startsWith('#')) continue;
    const c = line.split('\t');
    if (header && c[0] === 'type') { header = false; continue; }
    const [type, it, en, , , note] = c;
    if (!m[type] || !en || en === it) continue;
    m[type].set(it, en);
    if (type === 'identifier') m.notes.set(it, note || '');
  }
  if (!m.folder.size || !m.file.size || !m.project.size) cannot('the map has no folder, file or project rows');
  return m;
}

// Derived names: a probe folder compiles to <folder>.exe in its bin\, and the
// project names compile to <name>.dll/.exe (covered by the dotted names); the
// installer's assembly is `Installa`, a single word that is also an Italian
// verb: only its file names and its XML values are renamed.
function derivedFiles(map, oldFiles) {
  const out = new Map();
  for (const f of oldFiles) {
    const m = /^Controlli\/([^/]+)\/Programma\.cs$/.exec(f);
    if (m && map.folder.has(m[1])) {
      for (const ext of ['.exe', '.pdb', '.exe.config']) out.set(m[1] + ext, map.folder.get(m[1]) + ext);
    }
  }
  const inst = map.project.get('Installa');
  if (inst) for (const ext of ['.exe', '.pdb', '.exe.sha256', '.exe.config']) out.set('Installa' + ext, inst + ext);
  return out;
}

// ── moves ──────────────────────────────────────────────────────────────────
function newPathFor(rel, map) {
  const segs = rel.split('/');
  let changed = false;
  const out = segs.map((s, i) => {
    const t = i === segs.length - 1 ? map.file.get(s) : map.folder.get(s);
    if (t !== undefined) { changed = true; return t; }
    return s;
  });
  return changed ? out.join('/') : null;
}
function planMoves(files, map) {
  const moves = [];
  const target = new Map();
  for (const f of files) {
    const n = newPathFor(f, map);
    if (!n) continue;
    if (target.has(n.toLowerCase())) cannot('two paths move to ' + n + ': ' + target.get(n.toLowerCase()) + ' and ' + f);
    target.set(n.toLowerCase(), f);
    moves.push([f, n]);
  }
  for (const f of files) {
    if (!newPathFor(f, map) && target.has(f.toLowerCase())) cannot(f + ' already exists and ' + target.get(f.toLowerCase()) + ' would move onto it');
  }
  return moves;
}
function runMoves(opts) {
  const map = loadMap(opts.root);
  const files = trackedFiles(opts.root);
  const moves = planMoves(files, map);
  if (!moves.length) { console.log('window 3, moves: nothing to do (no tracked path has an Italian segment of the map)'); return 0; }
  if (opts.dry) { for (const [a, b] of moves) console.log('git mv ' + a + ' ' + b); console.log(moves.length + ' moves (dry run)'); return 0; }
  for (const [a, b] of moves) {
    fs.mkdirSync(path.dirname(path.join(opts.root, b)), { recursive: true });
    git(opts.root, ['mv', a, b]);
  }
  // folders left empty by the moves (git does not track them)
  const dirs = new Set();
  for (const [a] of moves) { let d = path.dirname(a); while (d && d !== '.') { dirs.add(d); d = path.dirname(d); } }
  for (const d of [...dirs].sort((x, y) => y.length - x.length)) {
    const full = path.join(opts.root, d);
    try { if (fs.existsSync(full) && !fs.readdirSync(full).length) fs.rmdirSync(full); } catch (e) { /* not empty: build outputs */ }
  }
  console.log('window 3, moves: ' + moves.length + ' paths moved with git mv');
  return 0;
}

// ── a tree of paths, for resolving path tokens ─────────────────────────────
function resolver(files) {
  const anch = new Set();       // every prefix of every path (anchored at the root)
  const sub = new Set();        // every contiguous piece of 2 or more segments
  const dirNames = new Set();   // every folder name
  const roots = new Set();      // the root folders
  for (const f of files) {
    const s = f.split('/');
    for (let i = 1; i <= s.length; i++) anch.add(s.slice(0, i).join('/'));
    for (let i = 0; i < s.length; i++) for (let j = i + 2; j <= s.length; j++) sub.add(s.slice(i, j).join('/'));
    for (let i = 0; i < s.length - 1; i++) dirNames.add(s[i]);
    if (s.length > 1) roots.add(s[0]);
  }
  return { anch, sub, dirNames, roots };
}

// ── excluded files ─────────────────────────────────────────────────────────
// never touched at all
const NEVER = [
  /^CHANGELOG\.md$/,
  /^(Checks\/language|Controlli\/lingua)\/name-map\.tsv$/,
  /^(Checks\/language|Controlli\/lingua)\/baseline\.json$/,
  /^(Bench|Banco)\/report\/[^/]+\.json$/,             // the bench writes it; the next bench rewrites it
  /^Checks\/language\/window-3-identifiers\.js$/,     // this file: it quotes both names
];
// only path tokens (their word lists and samples are Italian on purpose)
const PATHS_ONLY = [
  /^(Checks\/language|Controlli\/lingua)\/measure-labels[^/]*\.json$/,   // the samples are hashed; only the prose about them
  /^(Checks\/language|Controlli\/lingua)\/lexicon\.json$/,
];
const BINARY = /\.(png|jpe?g|gif|ico|exe|dll|pdb|zip|sldprt|sldasm|slddrw|step|stp|pdf|bmp)$/i;
function isNever(rel) { return BINARY.test(rel) || NEVER.some(r => r.test(rel)); }
function isPathsOnly(rel) { return PATHS_ONLY.some(r => r.test(rel)); }
const CODE_EXT = /\.(cs|js|ps1|psm1|csproj|sln|yml|yaml|config|manifest|bat|cmd|sh)$|(^|\/)pre-push$|(^|\/)[^/.]+$/i;

// ── the rewrite of one text ────────────────────────────────────────────────
// A path token: segments of name characters joined by separators. A
// backslash before a dot is a regular expression escape, not a separator.
const SEG_CH = "A-Za-z0-9_.{}$%~@+*?\\-";
const TOKEN_RX = new RegExp('[' + SEG_CH + '\\\\/]+', 'g');
function splitToken(tok) {
  // returns [{ sep, raw }] where sep is the separator before raw ('' for the first)
  const out = [];
  let i = 0, sep = '', raw = '';
  while (i < tok.length) {
    const c = tok[i];
    if (c === '/' || (c === '\\' && !/^\\+\./.test(tok.slice(i)))) {
      let j = i;
      while (j < tok.length && (tok[j] === '/' || (tok[j] === '\\' && !/^\\+\./.test(tok.slice(j))))) j++;
      out.push({ sep, raw }); sep = tok.slice(i, j); raw = ''; i = j; continue;
    }
    if (c === '\\') { const m = /^\\+\./.exec(tok.slice(i)); raw += m[0]; i += m[0].length; continue; }
    raw += c; i++;
  }
  out.push({ sep, raw });
  return out;
}
// the plain name of a raw segment: regex escapes and decorations off
function plain(raw) {
  return raw.replace(/\\+\./g, '.').replace(/^[\^]+/, '').replace(/[$.]+$/, '');
}

function makeRewriter(map, oldFiles, histFiles) {
  const R = resolver(oldFiles);
  // The paths that the history before the window had and the tree before
  // the window no longer has (`Banco/casi/...`, renamed in window 1): a text
  // that writes one of them is history and is left whole.
  const H = resolver(histFiles || []);
  const oldBases = new Set(oldFiles.map(f => f.slice(f.lastIndexOf('/') + 1)));
  // only the file names the tree before the window HAS (`ultimo.json` was
  // renamed in window 1: a text that still writes it is history)
  const files = new Map([...[...map.file].filter(([it]) => oldBases.has(it)), ...derivedFiles(map, oldFiles)]);
  const goneNames = new Set([...[...map.file.keys()].filter(it => !oldBases.has(it)),
    ...[...map.folder.keys()].filter(it => !R.dirNames.has(it))]);
  // true when the segments, read as a path, are one the history had and the
  // tree before the window does not
  function isHistoric(names) {
    const segs = names.filter(Boolean);
    if (!segs.some(nm => goneNames.has(nm))) return false;
    for (let i = 0; i < segs.length; i++) {
      for (let j = i; j < segs.length; j++) {
        const piece = segs.slice(i, j + 1);
        if (!piece.some(nm => goneNames.has(nm))) continue;
        const run = piece.join('/');
        if ((i === 0 && H.anch.has(run) && !R.anch.has(run)) || (j > i && H.sub.has(run) && !R.sub.has(run))) return true;
      }
    }
    // a gone FILE name alone at the end (ultimo.json) is history too
    const last = segs[segs.length - 1];
    return !!last && map.file.has(last) && !oldBases.has(last);
  }
  const fileNames = [...files.keys()].sort((a, b) => b.length - a.length);
  // dotted project names (free), longest first; `Installa` and the bare
  // segments are handled apart
  const free = [...map.project.entries()].filter(([it]) => /[.]/.test(it) || /^[A-Z][a-z]+[A-Z]/.test(it));
  free.sort((a, b) => b[0].length - a[0].length);
  const freeRx = new RegExp('(?<![A-Za-z0-9_.])(' + free.map(([it]) => it.split('.').map(escapeRx).join('(?:\\.|\\\\+\\.)')).join('|') + ')(?![A-Za-z0-9_])', 'g');
  const freeByPlain = new Map(free);
  const probeWords = new Set();      // probes of two words or more (ProvaRuota)
  const singleProbes = new Set();    // probes of one word (also words of SOLIDWORKS)
  for (const f of oldFiles) {
    const m = /^Controlli\/([^/]+)\/Programma\.cs$/.exec(f);
    if (!m || !map.folder.has(m[1])) continue;
    if (/^[A-Z][a-z]+[A-Z]/.test(m[1])) probeWords.add(m[1]); else singleProbes.add(m[1]);
  }
  const rootNames = new Set([...R.roots].filter(r => map.folder.has(r)));
  const fileRx = new RegExp('(?<![A-Za-z0-9_.\\-\\\\/])(' + fileNames.map(n => {
    const dot = n.indexOf('.');
    return escapeRx(n.slice(0, dot)) + '(?:\\.|\\\\+\\.)' + n.slice(dot + 1).split('.').map(escapeRx).join('(?:\\.|\\\\+\\.)');
  }).join('|') + ')(?![A-Za-z0-9_\\-])', 'g');

  function mapFileRaw(raw) {
    // a raw segment whose plain form is a file name of the map: the stem is
    // renamed in place, escapes and decorations kept
    const p = plain(raw);
    if (!files.has(p)) return null;
    const en = files.get(p);
    const itStem = p.slice(0, p.indexOf('.')), enStem = en.slice(0, en.indexOf('.'));
    const itExt = p.slice(p.indexOf('.')), enExt = en.slice(en.indexOf('.'));
    if (itExt !== enExt) return null;   // a map row keeps the extension (name-map check d)
    const k = raw.indexOf(itStem);
    if (k < 0) return null;
    return raw.slice(0, k) + enStem + raw.slice(k + itStem.length);
  }

  // the path token: resolve runs of segments on the tree before the window
  function rewriteToken(tok, stats) {
    // keep a trailing run of dots (end of a sentence) outside
    const tail = /\.+$/.exec(tok);
    const body = tail && !/\.\w+\.+$/.test(tok) ? tok.slice(0, -tail[0].length) : tok;
    const suffix = body === tok ? '' : tail[0];
    if (!/[\\/]/.test(body.replace(/\\+\./g, ''))) {
      // no separator: a file name alone, or nothing
      const r = mapFileRaw(body);
      if (r !== null) { stats.files++; return r + suffix; }
      return tok;
    }
    const parts = splitToken(body);
    const names = parts.map(p => plain(p.raw));
    if (isHistoric(names)) { stats.historic.push(tok); return tok; }
    const n = names.length;
    const renamed = new Array(n).fill(null);
    const resolved = new Array(n).fill(false);
    // an anchor: the first segment, or one after something that is not a
    // name of the tree (`$radice`, `{RADICE}`, `..`, `.`, `%TEMP%`)
    const isAnchor = (i) => i === 0 || names[i - 1] === '' || names[i - 1] === '.' || names[i - 1] === '..' ||
      /^[{$%]/.test(names[i - 1]) || /[}%]$/.test(names[i - 1]);
    for (let i = 0; i < n; i++) {
      if (!names[i]) continue;
      let best = -1;
      for (let j = i; j < n; j++) {
        const run = names.slice(i, j + 1).join('/');
        if ((isAnchor(i) && R.anch.has(run)) || (j > i && R.sub.has(run))) best = j;
      }
      if (best < 0) continue;
      // a folder the tree does not track (Banco/viste/, written by a script
      // and ignored by git) keeps its name: only its tracked parents move
      for (let k = i; k <= best; k++) resolved[k] = true;
      i = best;
    }
    for (let k = 0; k < n; k++) {
      if (!resolved[k]) continue;
      const isLast = k === n - 1 || !names[k + 1];
      const fr = mapFileRaw(parts[k].raw);
      if (fr !== null && isLast) { renamed[k] = fr; stats.files++; continue; }
      const en = map.folder.get(names[k]);
      if (en !== undefined) { renamed[k] = parts[k].raw.replace(names[k], en); stats.folders++; }
    }
    // a file name at the end of a path that did not resolve: renamed when
    // its folder is a placeholder or a variable, or unknown to every repo
    // (the file name of the map is ours); not when the folder is a real
    // folder name of another tree that this repository never had
    const last = n - 1;
    if (!resolved[last] && names[last]) {
      const fr = mapFileRaw(parts[last].raw);
      if (fr !== null) {
        const parent = last > 0 ? names[last - 1] : '';
        const foreign = parent && /^[A-Za-z][A-Za-z0-9_.-]*$/.test(parent) && !R.dirNames.has(parent) &&
          !/^(bin|obj|Release|Debug|dist|tmp|temp|Payload)$/i.test(parent);
        if (!foreign) { renamed[last] = fr; stats.files++; } else stats.foreignFiles.push(tok);
      }
    }
    if (!renamed.some(x => x !== null)) return tok;
    return parts.map((p, k) => p.sep + (renamed[k] !== null ? renamed[k] : p.raw)).join('') + suffix;
  }

  // Path.Combine(a, "Banco", "casi"), path.join(ROOT, 'Controlli', 'lingua'),
  // Join-Path $radice 'Banco\casi': the literals of one call are one path
  function rewriteCombines(text, stats) {
    const callRx = /\b(?:Path\.(?:Combine|Join)|path\.(?:join|resolve))\s*\(/g;
    let out = '', last = 0, m;
    while ((m = callRx.exec(text))) {
      let depth = 1, i = callRx.lastIndex, q = null;
      for (; i < text.length && depth > 0; i++) {
        const c = text[i];
        if (q) { if (c === '\\') { i++; continue; } if (c === q) q = null; continue; }
        if (c === '"' || c === "'" || c === '`') { q = c; continue; }
        if (c === '(') depth++; else if (c === ')') depth--;
      }
      if (depth) continue;
      const a = callRx.lastIndex, b = i - 1;
      out += text.slice(last, a) + rewriteArgs(text.slice(a, b), stats);
      last = b; callRx.lastIndex = b;
    }
    out += text.slice(last);
    // PowerShell: Join-Path <expr> <literal>
    out = out.replace(/(\bJoin-Path\s+(?:\$[\w:.]+|\([^()\n]*\)|"[^"\n]*"|'[^'\n]*')\s+(?:-ChildPath\s+)?)(['"])([^'"\n]*)\2/g,
      (all, head, q, lit) => head + q + rewriteLiteralChain([lit], stats)[0] + q);
    return out;
  }
  function rewriteArgs(args, stats) {
    // the literals of the call, in order
    const lits = [];
    const rx = /@?"((?:[^"\\\n]|\\.)*)"|'((?:[^'\\\n]|\\.)*)'/g;
    let m;
    while ((m = rx.exec(args))) lits.push({ at: m.index, len: m[0].length, q: m[0][0] === '@' ? '@"' : m[0][0], val: m[1] !== undefined ? m[1] : m[2] });
    if (!lits.length) return args;
    const vals = rewriteLiteralChain(lits.map(l => l.val), stats);
    let out = '', last = 0;
    lits.forEach((l, k) => {
      const close = l.q === '@"' ? '"' : l.q;
      out += args.slice(last, l.at) + l.q + vals[k] + close;
      last = l.at + l.len;
    });
    return out + args.slice(last);
  }
  function rewriteLiteralChain(vals, stats) {
    // the literals as one path: every segment a folder name of the old tree,
    // a folder row, or a file name, in a run that the tree had
    const segs = [];
    vals.forEach((v, k) => splitToken(v).forEach((p, j) => segs.push({ k, j, p, name: plain(p.raw) })));
    const names = segs.map(s => s.name);
    if (isHistoric(names)) { stats.historic.push(vals.join(', ')); return vals; }
    const known = (nm) => R.dirNames.has(nm) || map.folder.has(nm);
    const ok = names.every(nm => !nm || known(nm) || files.has(nm) || /\.\w+$/.test(nm) || /^[{$%.]/.test(nm));
    if (!ok) return vals;
    const segsOnly = names.filter(Boolean);
    const run = segsOnly.join('/');
    const real = R.anch.has(run) || R.sub.has(run) || (segsOnly.length === 1 && (R.dirNames.has(run) || files.has(run))) ||
      segsOnly.every(nm => R.dirNames.has(nm)) ||
      (segsOnly.length > 1 && R.roots.has(segsOnly[0]) && segsOnly.slice(1).every(nm => known(nm) || /\.\w+$/.test(nm)));
    if (!real) return vals;
    const outParts = vals.map(v => splitToken(v));
    segs.forEach(s => {
      const fr = mapFileRaw(s.p.raw);
      if (fr !== null) { outParts[s.k][s.j] = { sep: s.p.sep, raw: fr }; stats.files++; return; }
      // a folder the tree tracks; one it does not (an output folder) keeps its name
      const en = R.dirNames.has(s.name) ? map.folder.get(s.name) : undefined;
      if (en !== undefined) { outParts[s.k][s.j] = { sep: s.p.sep, raw: s.p.raw.replace(s.name, en) }; stats.folders++; }
    });
    return outParts.map(ps => ps.map(p => p.sep + p.raw).join(''));
  }

  function rewriteFree(text, stats) {
    return text.replace(freeRx, (m) => {
      const sep = (/\\+\./.exec(m) || ['.'])[0];
      const en = freeByPlain.get(m.replace(/\\+\./g, '.'));
      if (!en) return m;
      stats.projects++;
      return en.split('.').join(sep);
    });
  }

  // C#: the bare segments of a namespace qualification and the `using`
  // aliases; csproj, manifests and the installer's assembly name
  function rewriteBareNamespaces(text, rel, stats) {
    let out = text;
    const ext = path.extname(rel).toLowerCase();
    if (ext === '.cs') {
      const bare = [];
      for (const [it, en] of map.project) {
        const segs = it.split('.'), es = en.split('.');
        // the last segment alone, as Oce.Solid.Agent.Ponte is reached from
        // Oce.Solid.Agent as `Ponte.X`
        if (segs.length === es.length && segs.length >= 2 && segs.slice(0, -1).join('.') === es.slice(0, -1).join('.')) bare.push([segs[segs.length - 1], es[es.length - 1]]);
        if (!/[.]/.test(it) && it !== 'Installa' && !/^[A-Z][a-z]+[A-Z]/.test(it)) bare.push([it, en]);
      }
      for (const [it, en] of bare) {
        // also written for a regular expression (`new Condiviso\.Guinzaglio`)
        const rx = new RegExp('(?<![A-Za-z0-9_."\\\\])' + escapeRx(it) + '(?=(?:\\.|\\\\+\\.)[A-Z][A-Za-z0-9_]*\\b)', 'g');
        out = out.replace(rx, () => { stats.projects++; return en; });
        out = out.replace(new RegExp('(\\b(?:namespace|using)\\s+)' + escapeRx(it) + '(?=\\s*[;{\\r\\n])', 'g'), (m0, h) => { stats.projects++; return h + en; });
        out = out.replace(new RegExp('(\\busing\\s+\\w+\\s*=\\s*)' + escapeRx(it) + '(?=\\.)', 'g'), (m0, h) => { stats.projects++; return h + en; });
      }
    }
    if (/\.(csproj|manifest|config)$/i.test(rel)) {
      const inst = map.project.get('Installa');
      out = out.replace(/(<(?:RootNamespace|AssemblyName)>)Installa(<\/)/g, (m0, a, b) => { stats.projects++; return a + inst + b; });
      out = out.replace(/(name=")Oce\.Solid\.Agent\.Installa(")/g, (m0, a, b) => { stats.projects++; return a + 'Oce.Solid.Agent.' + inst + b; });
    }
    return out;
  }

  // spans whose whole content is a root folder or a probe of two words
  function rewriteWholeSpans(text, rel, stats) {
    const names = new Set([...rootNames, ...probeWords]);
    // a probe of one word (the undo and the thickness probes) is also a word SOLIDWORKS
    // writes; only the probe list of a PowerShell script names the folder
    if (/\.ps1$/i.test(rel)) for (const p of singleProbes) names.add(p);
    return text.replace(/`([^`\n]+)`|"([^"\n\\]+)"|'([^'\n\\]+)'|«([^»\n]+)»/g, (m, a, b, c, d) => {
      const v = a !== undefined ? a : b !== undefined ? b : c !== undefined ? c : d;
      if (!names.has(v)) return m;
      stats.spans++;
      return m[0] + map.folder.get(v) + m[m.length - 1];
    });
  }

  // A line that carries the marker `window-3: keep` is never rewritten: it
  // names the old and the new name on purpose (the gate reads the commits on
  // both sides of the move).
  function rewrite(text, rel, stats) {
    if (!text.includes(KEEP_MARK)) return rewriteAll(text, rel, stats);
    const kept = [];
    const masked = text.split('\n').map(l => {
      if (!l.includes(KEEP_MARK)) return l;
      kept.push(l);
      return '\uE000' + (kept.length - 1) + '\uE001';
    }).join('\n');
    return rewriteAll(masked, rel, stats).replace(/\uE000(\d+)\uE001/g, (m, k) => kept[+k]);
  }
  function rewriteAll(text, rel, stats) {
    const pathsOnly = isPathsOnly(rel);
    let out = text;
    // 1. path tokens (and file names alone)
    out = out.replace(TOKEN_RX, (tok) => {
      if (pathsOnly && !/[\\/]/.test(tok)) return tok;
      return rewriteToken(tok, stats);
    });
    if (pathsOnly) return out;
    // 2. the arguments of path calls
    out = rewriteCombines(out, stats);
    // 3. file names in regex-escaped forms the tokenizer splits
    out = out.replace(fileRx, (m) => { const r = mapFileRaw(m); if (r === null) return m; stats.files++; return r; });
    // 4. dotted project names
    out = rewriteFree(out, stats);
    // 5. bare namespace segments and the installer's assembly name
    out = rewriteBareNamespaces(out, rel, stats);
    // 6. whole spans: root folders and probes of two words
    out = rewriteWholeSpans(out, rel, stats);
    // 7. a regular expression's alternation of folders right before a path
    //    separator, as in /^(Mcp|<bridge>|<tools>)\//
    out = out.replace(/\((\?:)?((?:[A-Za-z0-9_.]+\|)+[A-Za-z0-9_.]+)\)(?=\\?\/|\\\\)/g, (m, nc, alt) => {
      const members = alt.split('|');
      if (!members.every(x => R.dirNames.has(x.replace(/\\\./g, '.')) || /^[A-Z][A-Za-z]+$/.test(x))) return m;
      let hit = false;
      const outM = members.map(x => { const en = map.folder.get(x); if (en && R.roots.has(x)) { hit = true; return en; } return x; });
      if (!hit) return m;
      stats.folders++;
      return '(' + (nc || '') + outM.join('|') + ')';
    });
    return out;
  }

  return { rewrite, R, files, rootNames, probeWords, free };
}

function newStats() { return { files: 0, folders: 0, projects: 0, spans: 0, foreignFiles: [], historic: [] }; }

function readText(full) {
  const buf = fs.readFileSync(full);
  if (buf.includes(0)) return null;
  return buf.toString('utf8');
}

function runReferences(opts) {
  const map = loadMap(opts.root);
  const oldFiles = treeFiles(opts.root, opts.from);
  const rw = makeRewriter(map, oldFiles, historyFiles(opts.root, opts.from));
  const files = trackedFiles(opts.root);
  const total = newStats();
  let changed = 0;
  const foreign = [];
  for (const rel of files) {
    if (isNever(rel)) continue;
    const full = path.join(opts.root, rel);
    if (!fs.existsSync(full)) continue;
    const text = readText(full);
    if (text === null) continue;
    const st = newStats();
    const out = rw.rewrite(text, rel, st);
    for (const k of ['files', 'folders', 'projects', 'spans']) total[k] += st[k];
    for (const f of st.foreignFiles) foreign.push(rel + ': ' + f);
    total.historic.push(...st.historic.map(h => rel + ': ' + h));
    if (out === text) continue;
    changed++;
    if (opts.list) console.log('rewrote ' + rel + ' (files ' + st.files + ', folders ' + st.folders + ', projects ' + st.projects + ', spans ' + st.spans + ')');
    if (!opts.dry) fs.writeFileSync(full, out);
  }
  console.log('window 3, references' + (opts.dry ? ' (dry run)' : '') + ': ' + changed + ' files; ' + total.files + ' file names, ' + total.folders +
    ' folder segments, ' + total.projects + ' project and namespace names, ' + total.spans + ' whole spans');
  if (total.historic.length) {
    console.log('paths left whole because they name a folder or file that an earlier window had already renamed (history): ' + total.historic.length);
    if (opts.list) for (const h of total.historic) console.log('    ' + h);
  }
  if (foreign.length) {
    console.log('file names of the map left at the end of a path of another tree (' + foreign.length + '):');
    for (const f of foreign) console.log('    ' + f);
  }
  return 0;
}

// ── --check ────────────────────────────────────────────────────────────────
function runCheck(opts) {
  const map = loadMap(opts.root);
  const files = trackedFiles(opts.root);
  let bad = 0;
  const say = (k, s) => { bad++; if (bad <= opts.show) console.log('  ' + k + ': ' + s); };
  // a. paths
  for (const f of files) if (newPathFor(f, map)) say('path still Italian', f);
  // b. a forward run would change nothing
  const oldFiles = treeFiles(opts.root, opts.from);
  const rw = makeRewriter(map, oldFiles, historyFiles(opts.root, opts.from));
  const review = [];
  const reviewNames = new Set([...map.folder.keys()].filter(n => !rw.rootNames.has(n) && !rw.probeWords.has(n)));
  const reviewRx = /`([^`\n]+)`|"([^"\n\\]+)"|'([^'\n\\]+)'|«([^»\n]+)»/g;
  for (const rel of files) {
    if (isNever(rel)) continue;
    const full = path.join(opts.root, rel);
    if (!fs.existsSync(full)) continue;
    const text = readText(full);
    if (text === null) continue;
    const out = rw.rewrite(text, rel, newStats());
    if (out !== text) {
      const a = text.split('\n'), b = out.split('\n');
      for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) say('a forward run would rewrite', rel + ':' + (i + 1) + ': ' + a[i].trim().slice(0, 140));
    }
    if (isPathsOnly(rel)) continue;
    if (CODE_EXT.test(rel) || /\.md$/i.test(rel)) {
      text.split('\n').forEach((l, i) => {
        let m; reviewRx.lastIndex = 0;
        while ((m = reviewRx.exec(l))) {
          const v = m[1] || m[2] || m[3] || m[4];
          if (reviewNames.has(v)) review.push(rel + ':' + (i + 1) + ': ' + m[0] + '   ' + l.trim().slice(0, 120));
        }
      });
    }
  }
  // c. identifiers still written in the code (by name, in the tokens of the code)
  const idLeft = identifierTokensLeft(opts.root, files, map);
  console.log('window 3, check: ' + (bad ? bad + ' things left (first ' + Math.min(bad, opts.show) + ' above)' : 'paths, references and project names clean'));
  console.log('identifier rows of the map still written in the code: ' + idLeft.length + (idLeft.length ? ' (' + idLeft.slice(0, 12).join(', ') + (idLeft.length > 12 ? ', ...' : '') + ')' : ''));
  if (opts.list) {
    console.log('folder words of the map left alone as a whole literal or span, to judge by eye: ' + review.length);
    for (const r of review) console.log('    ' + r);
  } else console.log('folder words of the map left alone as a whole literal or span, to judge by eye: ' + review.length + ' (--list prints them)');
  return bad || (opts.identifiersDone && idLeft.length) ? 1 : 0;
}

function identifierTokensLeft(root, files, map) {
  const seen = new Set();
  for (const rel of files) {
    if (!/\.(cs|js|ps1)$/i.test(rel) || isNever(rel)) continue;
    if (/^(Checks\/language|Controlli\/lingua)\//.test(rel)) continue;
    const text = readText(path.join(root, rel));
    if (text === null) continue;
    const code = stripCommentsAndStrings(text, path.extname(rel).toLowerCase());
    for (const m of code.matchAll(/[A-Za-z_][A-Za-z0-9_]*/g)) if (map.identifier.has(m[0])) seen.add(m[0]);
  }
  return [...seen].sort();
}

// a rough lexer: code without comments and string contents
function stripCommentsAndStrings(t, ext) {
  let out = '', i = 0;
  const n = t.length;
  while (i < n) {
    const c = t[i], d = t[i + 1];
    if (ext === '.ps1') {
      if (c === '<' && d === '#') { const e = t.indexOf('#>', i + 2); i = e < 0 ? n : e + 2; continue; }
      if (c === '#') { const e = t.indexOf('\n', i); i = e < 0 ? n : e; continue; }
      if (c === "'") { const e = t.indexOf("'", i + 1); i = e < 0 ? n : e + 1; out += ' '; continue; }
      if (c === '"') { let j = i + 1; while (j < n && t[j] !== '"') { if (t[j] === '`') j++; j++; } out += ' '; i = j + 1; continue; }
    } else {
      if (c === '/' && d === '/') { const e = t.indexOf('\n', i); i = e < 0 ? n : e; continue; }
      if (c === '/' && d === '*') { const e = t.indexOf('*/', i + 2); i = e < 0 ? n : e + 2; continue; }
      if (c === '@' && d === '"') { let j = i + 2; while (j < n) { if (t[j] === '"' && t[j + 1] === '"') { j += 2; continue; } if (t[j] === '"') break; j++; } out += ' '; i = j + 1; continue; }
      if (c === '"' || c === "'" || (c === '`' && ext === '.js')) { let j = i + 1; while (j < n && t[j] !== c) { if (t[j] === '\\') j++; if (t[j] === '\n' && c !== '`') break; j++; } out += ' '; i = j + 1; continue; }
    }
    out += c; i++;
  }
  return out;
}

// ── --reverse-proof ────────────────────────────────────────────────────────
// Every difference between <rev> and the tree after the window, explained by
// the map. A word that differs must be reachable from the old word by
// replacing whole parts (at word boundaries) with pairs of the map.
function pairsOf(map, oldFiles) {
  const p = new Map();
  const add = (a, b) => { if (!p.has(a)) p.set(a, new Set()); p.get(a).add(b); };
  for (const t of ['folder', 'file', 'project', 'identifier']) for (const [a, b] of map[t]) add(a, b);
  for (const [a, b] of derivedFiles(map, oldFiles)) add(a, b);
  // the names decided apart from the map: the symbols the C# renamer names otherwise where the
  // row would clash (SYMBOL_OVERRIDES), and the scripts' own (window-3-scripts.js)
  try {
    for (const o of require('./window-3-renamer.js').SYMBOL_OVERRIDES) add(o[2], o[3]);
    for (const [a, b] of require('./window-3-scripts.js').extraPairs()) add(a, b);
  } catch (e) { if (e.code !== 'MODULE_NOT_FOUND') throw e; }
  for (const [a, b] of map.file) { const x = a.slice(0, a.indexOf('.')), y = b.slice(0, b.indexOf('.')); if (x !== y) add(x, y); }
  for (const [a, b] of map.project) { const x = a.split('.'), y = b.split('.'); if (x.length === y.length) x.forEach((s, i) => { if (s !== y[i]) add(s, y[i]); }); }
  // a dotted name written for a regular expression (Riscontro\.Disegno)
  for (const t of ['project', 'file']) for (const [a, b] of map[t]) if (a.includes('.')) add(a.split('.').join('\\.'), b.split('.').join('\\.'));
  return p;
}
function explains(pairs, a, b) {
  if (a === b) return true;
  // DP over a: at each position copy one character, or replace a key of the
  // map that starts here (at a word boundary) with one of its values
  const memo = new Map();
  const boundary = (s, i) => i === 0 || i === s.length || !/[A-Za-z0-9_]/.test(s[i - 1]) || !/[A-Za-z0-9_]/.test(s[i]) ||
    (/[a-z0-9]/.test(s[i - 1]) && /[A-Z]/.test(s[i]));
  const keysAt = new Map();
  const go = (i, j) => {
    if (i === a.length) return j === b.length;
    const k = i * 100003 + j;
    if (memo.has(k)) return memo.get(k);
    let ok = false;
    if (boundary(a, i)) {
      let list = keysAt.get(i);
      if (!list) {
        list = [];
        for (let e = a.length; e > i; e--) { if (!boundary(a, e)) continue; const w = a.slice(i, e); if (pairs.has(w)) list.push(w); }
        keysAt.set(i, list);
      }
      for (const w of list) {
        for (const v of pairs.get(w)) if (b.startsWith(v, j) && go(i + w.length, j + v.length)) { ok = true; break; }
        if (ok) break;
      }
    }
    if (!ok && j < b.length && a[i] === b[j]) ok = go(i + 1, j + 1);
    memo.set(k, ok);
    return ok;
  };
  return go(0, 0);
}

// Hand edits of the window that no pair of the map explains, each one
// written here with its reason, so the proof names them instead of hiding
// them: [file after the window, text before, text after].
const HAND_EDITS = [
  // SwOperations.cs named its sibling files under Agente/, the folder of
  // the library before Riscontro.Disegno: the file names of today
  ['Oce.Solid.Agent.Core/SwOperations.cs', 'Agente/OperazioniFori.cs', 'HoleOperations.cs'],
  ['Oce.Solid.Agent.Core/SwOperations.cs', 'Agente/OperazioniMateriale.cs', 'MaterialOperations.cs'],
  ['Oce.Solid.Agent.Core/SwOperations.cs', 'Agente/OperazioniTavola.cs', 'DrawingOperations.cs'],
  ['Oce.Solid.Agent.Core/SwOperations.cs', 'Agente/OperazioniSaldatura.cs', 'WeldmentOperations.cs'],
  // the gate's message names the folder the gate now has, alone
  ['Checks/language/detect-italian.js', "Controlli/lingua/ (^Controlli/lingua/<name>$; ' +\n          'Checks/language/ once rename window 3 has moved the folder)');", "Checks/language/ (^Checks/language/<name>$)');"],
  // exceptions.json: the transition patterns of the gate's preparation go
  // back to one path each, the path after the window
  ['Checks/language/exceptions.json', '^(?:Controlli/lingua|Checks/language)/', '^Checks/language/'],
  ['Checks/language/exceptions.json', '^(?:Banco/CasiDichiarati|Bench/DeclaredCases)\\\\.cs$', '^Bench/DeclaredCases\\\\.cs$'],
  ['Checks/language/exceptions.json', '^(?:Banco|Bench)/', '^Bench/'],
  ['Checks/language/exceptions.json', '^(?:Controlli/percorsi-del-todo|Checks/todo-paths)\\\\.js$', '^Checks/todo-paths\\\\.js$'],
  ['Checks/language/exceptions.json', '^(?:Controlli/rimandi-del-todo|Checks/todo-references)\\\\.js$', '^Checks/todo-references\\\\.js$'],
  ['Checks/language/exceptions.json', '^(?:Controlli/conta-todo|Checks/count-todo)\\\\.js$', '^Checks/count-todo\\\\.js$'],
  ['Checks/language/exceptions.json', '^(?:Prove/Programma|Tests/Program)\\\\.cs$', '^Tests/Program\\\\.cs$'],
  ['Checks/language/exceptions.json', '^(?:Prove|Tests)/', '^Tests/'],
  // identifiers commit: two contracts already had a method Check(Assembly) and the bench's recorder becomes
  // Check too, so theirs is called CheckLibrary first (they check the library assembly)
  ['Tests/StateChamferContract.cs', 'Check(asm);', 'CheckLibrary(asm);'],
  ['Tests/StateChamferContract.cs', 'internal static void Check(Assembly asm)', 'internal static void CheckLibrary(Assembly asm)'],
  ['Tests/WeldmentConfigurationZc212terContract.cs', 'Check(asm);', 'CheckLibrary(asm);'],
  ['Tests/WeldmentConfigurationZc212terContract.cs', 'internal static void Check(Assembly asm)', 'internal static void CheckLibrary(Assembly asm)'],
  // a regular expression of a contract names the variable of the code it reads, as the code writes it now
  ['Tests/ForbiddenSignaturesTests.cs', '\\bmodello', '\\bmodel'],
  // ExtrudeProfile now writes the same line as the spline helper (sketches.AddToDB = addToDbBefore;), so the
  // change of that contract names its line by the indentation only the helper has
  ['Tests/Zc2113SplineCentreMarksContract.cs', '"sketches.AddToDB = addToDbBefore;", ""',
    '"                    try { sketches.AddToDB = addToDbBefore;", "                    try {"'],
  // review of the identifiers commit: the report had a path between W3.1-bis.4 and this window too; without it the
  // contract that looks for the commit that moved the report found none (and passed on nothing)
  ['Tests/BenchTests.cs', '{ "Banco/report/last.json", ', '{ "Bench/report/last.json", "Banco/report/last.json", '],
  // same-il.ps1: the addresses of static data blobs are not compared (the hunk is the one of HAND_HUNKS below)
  ['Checks/language/same-il.ps1', 'lines[i] = Literal(lines[i]);', 'lines[i] = DataLabel.Replace(Literal(lines[i]), "I_<data>");'],
];
// Hunks rewritten by hand as a whole, named by the file and a piece of their
// new text: [file after the window, new text, why]. Listed by the proof.
const HAND_HUNKS = [
  ['Bench/Program.cs', 'The historical comparison starts at library 0.150.1', 'decision D11: the message of the bench'],
  ['Checks/library-of-the-time.ps1', 'THE HISTORICAL COMPARISON STARTS AT LIBRARY 0.150.1', 'decision D11: the header of the script'],
  ['Checks/language/same-il.ps1', 'A row that keeps the name (old = new) is kept too', 'same-il: a row that keeps a name makes the word optional'],
  ['Checks/language/same-il.ps1', 'The address of a static data blob', 'same-il: the addresses of static data blobs are not compared'],
  ['Checks/language/same-il.ps1', '// Offset: 0x[0-9A-F]+ Length: 0x[0-9A-F]+$', 'same-il: the offset comment of a resource is not compared'],
  // identifiers commit, by hand (each says why in the commit message and in the CHANGELOG entry)
  ['Tests/RefusalsTests.cs', '**216, rename window 3', 'a refusal is judged on its string literals, not on the names of the code around them (216 of 937 before and after)'],
  ['Tests/RefusalsTests.cs', 'private const int Floor = 216;', 'the floor on the literals: 216 of 937, the same on the tree before the window'],
  ['Tests/RefusalsTests.cs', 'ShareFloorWithRemedy = 216;', 'the floor on the literals: 216 of 937, the same on the tree before the window'],
  ['Tests/RefusalsTests.cs', 'only what the message says, its string literals', 'the remedy words are looked for in the literals of the message, not in the names of the code'],
  ['Tests/SafetyNetSelfTests.cs', 'The fakes: rows of the real map', 'the name-map self-test takes the name it removes from the names the tree still has (--found)'],
  ['Tests/SafetyNetSelfTests.cs', '.Take(3).ToList();', 'the name-map self-test takes the name it removes from the names the tree still has (--found)'],
  ['Tests/SafetyNetSelfTests.cs', 'lines[leftOut.Index] = null;', 'the name-map self-test takes the name it removes from the names the tree still has (--found)'],
  ['Tests/SafetyNetSelfTests.cs', '"exit 1, red on a (" + leftOut', 'the name-map self-test takes the name it removes from the names the tree still has (--found)'],
  ['Checks/language/name-map-extract.js', 'extract, print each Italian name found', 'name-map-extract.js --found: the Italian names the tree has now'],
  ['Checks/language/name-map-extract.js', "else if (x === '--found') a.found = true;", 'name-map-extract.js --found: the Italian names the tree has now'],
  ['Checks/language/name-map-extract.js', 'if (args.found) {', 'name-map-extract.js --found: the Italian names the tree has now'],
  ['Tests/BenchTests.cs', 'Every path the report has had, the newest first', 'review: the comment of the three report paths'],
];
// same-il.ps1 learns what the identifiers need (the identifiers commit): a changed line is judged on the original
// text of the before side, the rewrite leaves the string literals alone, an accessor is a prefix and a name, a
// name ildasm quotes is the same name, the runs of nested classes are put in the order of their names (Roslyn
// writes them sorted), and the lines not explained are counted by the method they sit in.
for (const text of [
  'the lines not explained, by the method they sit in',
  'an accessor is its prefix and the name of the member',
  'ildasm quotes a name that is an IL keyword',
  'The order of the lines of a disassembly in which every run of nested classes',
  'int show, string[] originalBefore)',
  'int hunkStart = 0;',
  'Judge(removed, added, originalBefore, hunkStart, mode, map, show, res);',
  'static readonly Regex MethodLine',
  'var where = Where(res, originalBefore, hunkStart, Math.Max',
  '" (in " + where + "):"',
  ': ExplainedByMap(originalBefore != null',
  'var where = Where(res, originalBefore, hunkStart + i, 1);',
  '"not only a literal (in " + where',
  '$original = $ta',
  'the runs of nested classes in the order of their names, on both sides',
  '$r = [SameIlCore]::Compare($fa, $fb, $mode, $nameMap, $show, $original)',
  'not explained, by method (',
  // the self-test sees what the mode learned: a message that keeps a word of the map, an iterator that moves, an accessor
  'a message that keeps two words of the map',
  'Alpha`tZulu`nLabel`tTag',
  "Replace('IEnumerable<int> Alpha()'",
]) HAND_HUNKS.push(['Checks/language/same-il.ps1', text, 'same-il: Renames mode learns the identifiers (see the comment above this list)']);
// Files the window writes by hand as a whole (judged elsewhere, listed):
// the version, the changelog entry, the language baseline, this script.
// (the name map: the English column and the note of the rows the binding proof of the window decided apart;
// the frozen undecided calls of the obsolete-calls contract: its header says why the counts are what they are)
const HAND_FILES = /^(CHANGELOG\.md|VERSION\.txt|Checks\/language\/baseline\.json|Checks\/language\/name-map\.tsv|Checks\/language\/lexicon\.json|Checks\/language\/window-3-(identifiers|renamer|scripts|mentions)\.js|Tests\/data\/obsolete-undecided-frozen\.tsv)$/;
const VERSION_LINE = /\bAssembly(?:File|Informational)?Version\("[0-9.]+"\)/;

// line hunks between two texts: [{ a: [old lines], b: [new lines], at }]
function lineHunks(a, b) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'w3proof-'));
  const fa = path.join(tmp, 'a'), fb = path.join(tmp, 'b');
  fs.writeFileSync(fa, a); fs.writeFileSync(fb, b);
  const r = cp.spawnSync('git', ['diff', '--no-index', '--no-color', '-U0', fa, fb], { encoding: 'utf8', maxBuffer: 1 << 30 });
  fs.rmSync(tmp, { recursive: true, force: true });
  const out = [];
  let cur = null;
  for (const l of (r.stdout || '').split('\n')) {
    const h = /^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@/.exec(l);
    if (h) { cur = { a: [], b: [], at: +h[2] }; out.push(cur); continue; }
    if (!cur) continue;
    if (l.startsWith('-')) cur.a.push(l.slice(1)); else if (l.startsWith('+')) cur.b.push(l.slice(1));
  }
  return out;
}

function runReverseProof(opts) {
  const map = loadMap(opts.root);
  const oldFiles = treeFiles(opts.root, opts.proof);
  const pairs = pairsOf(map, oldFiles);
  const nowFiles = opts.worktree ? trackedFiles(opts.root) : treeFiles(opts.root, 'HEAD');
  const nowSet = new Set(nowFiles);
  const readNow = (rel) => opts.worktree ? fs.readFileSync(path.join(opts.root, rel))
    : cp.execFileSync('git', ['-C', opts.root, 'show', 'HEAD:' + rel], { maxBuffer: 1 << 30 });
  const readOld = (rel) => cp.execFileSync('git', ['-C', opts.root, 'show', opts.proof + ':' + rel], { maxBuffer: 1 << 30 });
  let unexplained = 0, filesSeen = 0, linesDiff = 0, handUsed = 0, kept = 0;
  const hand = [], missing = [], extra = [];
  const expected = new Set();
  const say = (n, line, x, y) => {
    unexplained++;
    if (unexplained <= opts.show) console.log('  ' + n + ':' + line + '\n      - ' + (x === null ? '(none)' : x.trim().slice(0, 200)) + '\n      + ' + (y === null ? '(none)' : y.trim().slice(0, 200)));
  };
  const lineExplained = (n, x, y) => {
    // the marker of a line left whole on purpose is a comment added by the window
    if (y.includes(KEEP_MARK)) { const z = y.replace(/[ \t]*(?:\/\/|#)[ \t]*window-3: keep\b[^\n]*/g, ''); if (z !== y) { kept++; y = z; } }
    if (x === y) return true;
    for (const [file, from, to] of HAND_EDITS) {
      if (file === n && x.includes(from)) { const x2 = x.split(from).join(to); if (x2 === y || lineExplained(n, x2, y)) { handUsed++; return true; } }
    }
    if (VERSION_LINE.test(x) && VERSION_LINE.test(y) && x.replace(/[0-9.]+(?="\))/, '') === y.replace(/[0-9.]+(?="\))/, '')) return true;
    // a JavaScript shorthand property written out, so that its KEY stays what it was:
    // { name } -> { name: newName }, const { name } = r -> const { name: newName } = r
    if (/\.js$/.test(n)) {
      const y2 = y.replace(/\b([A-Za-z_$][\w$]*): ([A-Za-z_$][\w$]*)(?=[\s,})=;])/g,
        (m, k, v) => (k !== v && !x.includes(k + ': ') && x.includes(k) && explains(pairs, k, v)) ? k : m);
      if (y2 !== y && lineExplained(n, x, y2)) return true;
    }
    const wa = x.split(/([A-Za-z0-9_.\-\\/{}$%~@+*?]+)/), wb = y.split(/([A-Za-z0-9_.\-\\/{}$%~@+*?]+)/);
    if (wa.length !== wb.length) return false;
    for (let k = 0; k < wa.length; k++) if (wa[k] !== wb[k] && !((k % 2 === 1) && explains(pairs, wa[k], wb[k]))) return false;
    return true;
  };
  for (const f of oldFiles) {
    const n = newPathFor(f, map) || f;
    expected.add(n);
    if (!nowSet.has(n)) { missing.push(f + ' -> ' + n); continue; }
    if (BINARY.test(f)) { if (!readOld(f).equals(readNow(n))) say(n, 0, 'binary', 'binary changed'); continue; }
    // line ends as git stores them (a working tree may carry CRLF)
    const a = readOld(f).toString('utf8').replace(/\r\n/g, '\n'), b = readNow(n).toString('utf8').replace(/\r\n/g, '\n');
    filesSeen++;
    if (a === b) continue;
    if (HAND_FILES.test(n)) { hand.push(n); continue; }
    for (const h of lineHunks(a, b)) {
      if (h.a.length !== h.b.length) {
        // an added line is allowed only as the version comment of an
        // AssemblyInfo: a block of comment lines, the rest paired as usual
        if (/AssemblyInfo\.cs$/.test(n) && h.b.length > h.a.length) {
          const extra = h.b.length - h.a.length;
          let fit = -1;
          for (let k = 0; k + extra <= h.b.length && fit < 0; k++) {
            const block = h.b.slice(k, k + extra), rest = h.b.slice(0, k).concat(h.b.slice(k + extra));
            if (block.every(l => /^\s*\/\//.test(l)) && rest.every((y, i) => lineExplained(n, h.a[i], y))) fit = k;
          }
          if (fit >= 0) { hand.push(n + ' (+' + extra + ' comment lines at ' + (h.at + fit) + ')'); linesDiff += h.a.length; continue; }
        }
        const ja = h.a.join('\n'), jb = h.b.join('\n');
        if (HAND_EDITS.some(([file, from, to]) => file === n && ja.includes(from) && lineExplained(n, ja.split(from).join(to), jb))) { handUsed++; continue; }
        const hh = HAND_HUNKS.find(([file, text]) => file === n && jb.includes(text));
        if (hh) { hand.push(n + ':' + h.at + ' (' + hh[2] + ')'); continue; }
        say(n, h.at, h.a.join(' | '), h.b.join(' | '));
        continue;
      }
      h.a.forEach((x, i) => {
        linesDiff++;
        if (lineExplained(n, x, h.b[i])) return;
        const hh = HAND_HUNKS.find(([file, text]) => file === n && h.b.join('\n').includes(text));
        if (hh) { hand.push(n + ':' + (h.at + i) + ' (' + hh[2] + ')'); return; }
        say(n, h.at + i, x, h.b[i]);
      });
    }
  }
  for (const f of nowFiles) if (!expected.has(f)) extra.push(f);
  console.log('window 3, reverse proof against ' + opts.proof + ': ' + oldFiles.length + ' paths, ' + filesSeen + ' text files, ' + linesDiff +
    ' lines differ, ' + unexplained + ' not explained by the map (' + handUsed + ' lines by the hand edits listed in the script, ' + kept +
    ' keep markers); ' + missing.length + ' missing, ' + extra.length + ' new');
  for (const m of missing) console.log('  missing: ' + m);
  for (const e of extra) console.log('  new (not a move of the map): ' + e + (HAND_FILES.test(e) ? ' (written by the window)' : ''));
  if (hand.length) console.log('  written by hand, judged apart (' + hand.length + '): ' + hand.join(', '));
  const badExtra = extra.filter(e => !HAND_FILES.test(e));
  return unexplained || missing.length || badExtra.length ? 1 : 0;
}

// ── --todo ─────────────────────────────────────────────────────────────────
// The TODO is read in the names of before the window: only path tokens and
// dotted project names (decision D10), outside the closed entries, the
// history sections and the entry W3.1-bis itself.
const RX_RENAME_ENTRY = /^(#{2,5}) (?:\[[ x~]\] )?W3\.1-bis\b/;
function runTodo(opts) {
  const map = loadMap(opts.root);
  const oldFiles = treeFiles(opts.root, opts.from);
  const rw = makeRewriter(map, oldFiles, historyFiles(opts.root, opts.from));
  const text = fs.readFileSync(opts.todo, 'utf8');
  const eol = text.includes('\r\n') ? '\r\n' : '\n';
  const lines = text.split(/\r?\n/);
  let state = null, renameLevel = 0, changed = 0, closed = 0, inRename = 0;
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i];
    const hl = /^(#{1,5}) /.exec(l);
    if (hl && renameLevel && hl[1].length <= renameLevel && !RX_RENAME_ENTRY.test(l)) renameLevel = 0;
    const re = RX_RENAME_ENTRY.exec(l);
    if (re && !renameLevel) renameLevel = re[1].length;
    const n = rw.rewrite(l, 'TODO.md', newStats());
    if (renameLevel) { if (n !== l) inRename++; continue; }
    const h = l.match(/^#{2,5} \[([ x~])\]/);
    if (h) state = h[1];
    else if (/^#{2,5} \(com'era\)/.test(l)) state = 'x';
    else if (/^#{1,5} /.test(l)) state = null;
    if (state === 'x') { if (n !== l) closed++; continue; }
    if (n !== l) { changed++; lines[i] = n; if (opts.list) console.log('    ' + (i + 1) + ': ' + n.trim().slice(0, 170)); }
  }
  const outFile = opts.out || opts.todo;
  if (!opts.dry) fs.writeFileSync(outFile, lines.join(eol));
  console.log('window 3, code names in ' + opts.todo + (opts.dry ? ' (dry run)' : ' -> ' + outFile) + ': ' + changed + ' lines' +
    (closed ? ' (' + closed + ' left in closed entries)' : '') + (inRename ? ' (' + inRename + ' left in the entry W3.1-bis)' : ''));
  return 0;
}

// ── arguments ──────────────────────────────────────────────────────────────
function parseArgs(argv) {
  const a = { root: ROOT_DEFAULT, from: BASE_REV, dry: false, list: false, show: 60 };
  for (let i = 0; i < argv.length; i++) {
    const x = argv[i];
    if (x === '--moves') a.mode = 'moves';
    else if (x === '--references') a.mode = 'references';
    else if (x === '--identifiers') { a.mode = 'identifiers'; a.batch = argv[++i] || cannot('--identifiers needs a batch'); }
    else if (x === '--check') a.mode = 'check';
    else if (x === '--identifiers-done') a.identifiersDone = true;
    else if (x === '--reverse-proof') { a.mode = 'proof'; a.proof = argv[++i] || cannot('--reverse-proof needs a revision'); }
    else if (x === '--todo') { a.mode = 'todo'; a.todo = path.resolve(argv[++i] || cannot('--todo needs a file')); }
    else if (x === '--out') a.out = path.resolve(argv[++i] || cannot('--out needs a file'));
    else if (x === '--from') a.from = argv[++i] || cannot('--from needs a revision');
    else if (x === '--root') a.root = path.resolve(argv[++i] || cannot('--root needs a folder'));
    else if (x === '--worktree') a.worktree = true;
    else if (x === '--dry-run') a.dry = true;
    else if (x === '--list') a.list = true;
    else if (x === '--show') a.show = parseInt(argv[++i], 10) || 60;
    else if (x === '--help' || x === '-h') a.mode = 'help';
    else cannot('unknown argument ' + x);
  }
  return a;
}

function main() {
  let opts;
  try { opts = parseArgs(process.argv.slice(2)); } catch (e) { if (e instanceof Stop) { console.error(e.message); return 2; } throw e; }
  try {
    switch (opts.mode) {
      case 'moves': return runMoves(opts);
      case 'references': return runReferences(opts);
      case 'identifiers': return require('./window-3-renamer.js').run(opts, { loadMap, trackedFiles, isNever, cannot });
      case 'check': return runCheck(opts);
      case 'proof': return runReverseProof(opts);
      case 'todo': return runTodo(opts);
      case 'help': console.log(fs.readFileSync(__filename, 'utf8').split('\n').slice(1, 96).join('\n')); return 0;
      default: console.error('usage: --moves | --references | --identifiers <batch> | --check | --reverse-proof <rev> | --todo <file>  (--help)'); return 2;
    }
  } catch (e) {
    if (e instanceof Stop) { console.error('window-3-identifiers: ' + e.message); return 2; }
    throw e;
  }
}

if (require.main === module) process.exitCode = main();
module.exports = { loadMap, newPathFor, makeRewriter, explains, pairsOf, stripCommentsAndStrings, splitToken, plain };
