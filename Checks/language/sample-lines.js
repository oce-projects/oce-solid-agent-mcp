#!/usr/bin/env node
// =============================================================================
//  sample-lines.js - the random sample of release condition 8 (entry W3.1-bis.11)
// =============================================================================
//
//  Condition 8 of the English release: «a sample of 100 lines drawn at random
//  from the tree, judged by a model other than the translator and then by the
//  owner on the doubtful lines, finds no Italian outside the exceptions». The
//  gate counts what its lexicon knows; the sample is read by a person, and it
//  is what finds the Italian the lexicon does not know.
//
//  THE DRAW, so that anyone can repeat it:
//    - the files are `git ls-files` of the commit, minus the binary
//      extensions of detect-italian.js (the same closed list, read from it);
//    - each file is read from the commit (`git show <rev>:<path>`), never from
//      the disk, and split on \n; blank lines are dropped;
//    - every non-blank line of the tree has the same chance: mulberry32 seeded
//      with --seed picks indexes into the list of all lines, without repeats.
//  The same commit and the same seed give the same lines on every machine.
//
//  USAGE
//    node Checks/language/sample-lines.js [--rev <commit>] [--seed <n>] [--count <n>]
//      defaults: --rev HEAD, --seed 20261006, --count 100
//    Prints one line per pick: number, path:line, text (cut at 240 characters).
//
//  EXIT CODES
//    0  the sample was printed
//    2  cannot run (no git, a bad commit, a bad argument)
// =============================================================================

'use strict';

const cp = require('child_process');
const fs = require('fs');
const path = require('path');

function fail(message) { console.error('sample-lines: ' + message); process.exit(2); }

const opts = { rev: 'HEAD', seed: 20261006, count: 100 };
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
  const a = argv[i], v = argv[i + 1];
  if (a === '--rev' && v) { opts.rev = v; i++; }
  else if (a === '--seed' && /^\d+$/.test(v || '')) { opts.seed = Number(v); i++; }
  else if (a === '--count' && /^\d+$/.test(v || '')) { opts.count = Number(v); i++; }
  else fail('unknown argument ' + a + ' (use --rev, --seed, --count)');
}

const root = path.resolve(__dirname, '..', '..');
const git = (args) => cp.execFileSync('git', ['-C', root, ...args], { encoding: 'utf8', maxBuffer: 1 << 30 });

// the binary extensions: the closed list of the detector, never a copy of it
const detector = fs.readFileSync(path.join(__dirname, 'detect-italian.js'), 'utf8');
const list = /const BINARY_EXTENSIONS = new Set\(\[([\s\S]*?)\]\);/.exec(detector);
if (!list) fail('cannot read BINARY_EXTENSIONS from detect-italian.js');
const binary = new Set((list[1].match(/'\.[^']+'/g) || []).map(s => s.slice(1, -1)));
if (binary.size < 10) fail('BINARY_EXTENSIONS of detect-italian.js looks empty');

let commit;
try { commit = git(['rev-parse', '--short', opts.rev + '^{commit}']).trim(); } catch (e) { fail('no commit ' + opts.rev); }

const files = git(['ls-tree', '-r', '-z', '--name-only', commit]).split('\0').filter(Boolean).sort()
  .filter(f => { const m = /\.[^./]+$/.exec(f.toLowerCase()); return !(m && binary.has(m[0])); });
const all = [];
for (const f of files) {
  const lines = git(['show', commit + ':' + f]).split('\n');
  lines.forEach((l, i) => { if (l.trim()) all.push([f, i + 1, l.replace(/\r$/, '')]); });
}
if (opts.count > all.length) fail('the tree has only ' + all.length + ' non-blank lines');

let state = opts.seed >>> 0;
function mulberry32() {
  state = (state + 0x6D2B79F5) >>> 0;
  let t = state;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}
const picked = new Set();
while (picked.size < opts.count) picked.add(Math.floor(mulberry32() * all.length));

console.log('# ' + files.length + ' files, ' + all.length + ' non-blank lines, seed ' + opts.seed + ', commit ' + commit);
[...picked].sort((x, y) => x - y).forEach((k, n) => {
  const [f, line, text] = all[k];
  console.log(String(n + 1).padStart(3) + ' ' + f + ':' + line + ': ' + text.trim().slice(0, 240));
});
