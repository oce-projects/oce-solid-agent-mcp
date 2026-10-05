#!/usr/bin/env node
// =============================================================================
//  measure.js - precision and recall of detect-italian.js on real lines
// =============================================================================
//
//  The lines are drawn AT RANDOM, with a fixed seed, from the tree of a fixed
//  commit (so the draw can be repeated forever), and labelled BY HAND:
//
//    it        the line holds Italian the gate must count: Italian prose, or
//              an Italian name (identifier, key, value, path) outside the
//              closed exception classes
//    en        no Italian, and no English prose either: code, SOLIDWORKS API
//              names, numbers, proper names, localized SOLIDWORKS names
//              covered by an exception
//    en-prose  English prose (a comment, a message, a document line)
//    skip      no natural-language content at all (never used to hide a miss)
//
//  A candidate is a line with at least three words of two letters or more.
//  The labels are keyed by a hash of file, line number and text, so the label
//  file carries no text of the repository. The report walks the shuffled
//  candidates IN ORDER and stops at the first one without a label: the
//  measured set is always a prefix of the draw, never a chosen subset.
//
//  FOUR DRAWS, all from commit 4d7b84a (the tree the gate was built on):
//    measure-labels.json          every line with three words or more, seed
//                                 20260928: precision and recall (labelled
//                                 while the detector was being written)
//    measure-labels-stress.json   only lines with two English function words,
//                                 seed 928, labelled after the first freeze
//    measure-labels-blind.json    every line, seed 5150, labelled BLIND (from
//                                 --next, before the detector ever ran on the
//                                 draw), after the review of W3.1-bis.1
//    measure-labels-blind-english.json   two English function words, seed
//                                 4747, labelled blind: false alarms
//  The first two were drawn from the tree the lexicon came from and helped to
//  tune it; the blind ones did not, and are the numbers to quote.
//
//  WHAT A HIT MEANS. A line is "found" if any category has a hit on it. The
//  report also says how: "counted" (a hit in a counted category - the same
//  words added anywhere make a file grow, so the gate blocks them) or "names
//  only" (every hit is an identifier, key, protocol value or path - blocked
//  only when that name is new to the repository).
//
//  USAGE
//    node Checks/language/measure.js                  report on the main draw
//    node Checks/language/measure.js --labels <file>  report on another draw
//    node Checks/language/measure.js --summary        the four draws, and the pooled false alarms
//    node Checks/language/measure.js --next 50        print the next 50 lines to label
//    node Checks/language/measure.js --record "iep.." record the labels of those lines, in order
//                                                      (i = it, e = en, p = en-prose, s = skip)
//    node Checks/language/measure.js --show 0 100     the labelled lines with their label
//    node Checks/language/measure.js --drop-last 20   forget the last 20 labels
//    node Checks/language/measure.js --set 285 i      correct one label (after reading the whole draw back)
//
//  Exit 0 if (uniform draws) balanced precision >= 0.98 and recall >= 0.95 on
//  at least 100 Italian and 100 English lines, or (English draws) a
//  false-alarm rate <= 0.02 on at least 100 English lines; 1 otherwise.
//  --summary exits 0 only if every draw passes.
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const crypto = require('crypto');
const D = require('./detect-italian.js');

const ROOT = path.resolve(__dirname, '..', '..');
const args = process.argv.slice(2);
const opt = (name, dflt) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : dflt; };
const DRAWS = ['measure-labels.json', 'measure-labels-stress.json', 'measure-labels-blind.json', 'measure-labels-blind-english.json'];

function mulberry32(a) {
  return function () {
    a |= 0; a = a + 0x6D2B79F5 | 0;
    let t = Math.imul(a ^ a >>> 15, 1 | a);
    t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
    return ((t ^ t >>> 14) >>> 0) / 4294967296;
  };
}

const git = (a) => cp.execFileSync('git', ['-C', ROOT].concat(a), { encoding: 'buffer', maxBuffer: 256 << 20 });

// every blob of the commit in one `git cat-file --batch` call
function readBlobs(commit, paths) {
  const out = cp.spawnSync('git', ['-C', ROOT, 'cat-file', '--batch'],
    { input: paths.map(p => commit + ':' + p).join('\n') + '\n', maxBuffer: 1 << 30 }).stdout;
  const blobs = new Map();
  let pos = 0;
  for (const p of paths) {
    const nl = out.indexOf(10, pos);
    const header = out.slice(pos, nl).toString('utf8');
    const m = / blob (\d+)$/.exec(header);
    if (!m) throw new Error('cannot read ' + p + ' at ' + commit + ': ' + header);
    const size = parseInt(m[1], 10);
    blobs.set(p, out.slice(nl + 1, nl + 1 + size));
    pos = nl + 1 + size + 1;
  }
  return blobs;
}

const trees = new Map();
function treeOf(commit) {
  if (trees.has(commit)) return trees.get(commit);
  const files = git(['ls-tree', '-r', '-z', '--name-only', commit]).toString('utf8').split('\0').filter(Boolean);
  const textFiles = files.filter(f => !D.isBinaryByExtension(f)).sort();
  const blobs = readBlobs(commit, textFiles);
  const texts = new Map();
  for (const f of textFiles) texts.set(f, D.decode(blobs.get(f)));
  const t = { files, textFiles, texts };
  trees.set(commit, t);
  return t;
}

const WORD = /[A-Za-z\u00c0-\u00ff]{2,}/g;
// The English draws ("filter": "english-cue") keep only lines with at least
// two English function words: English prose, and Italian lines that mix in
// English. Not a uniform sample: it is where false alarms would hide.
const ENGLISH_CUE = /\b(?:the|and|of|to|is|that|it|with|for|as|on|was|by|not|are|be|this|from|an|or|which|when|has|have)\b/g;

function candidatesOf(doc) {
  const tree = treeOf(doc.commit);
  const minWords = doc.min_words || 3;
  const candidates = [];
  for (const f of tree.textFiles) {
    tree.texts.get(f).split('\n').forEach((raw, i) => {
      const l = raw.replace(/\r$/, '');
      if ((l.match(WORD) || []).length < minWords) return;
      if (doc.filter === 'english-cue' && (l.match(ENGLISH_CUE) || []).length < 2) return;
      const id = crypto.createHash('sha1').update(f + ':' + (i + 1) + ':' + l).digest('hex').slice(0, 16);
      candidates.push({ id, file: f, line: i + 1, text: l });
    });
  }
  const rnd = mulberry32(doc.seed);
  for (let i = candidates.length - 1; i > 0; i--) {
    const j = Math.floor(rnd() * (i + 1));
    [candidates[i], candidates[j]] = [candidates[j], candidates[i]];
  }
  return candidates;
}

// The upper end of a one-sided 95% interval for a rate seen k times in n
// (exact binomial; with k = 0 it is the "rule of three", about 3/n).
function upper95(k, n) {
  if (n === 0) return 1;
  let lo = k / n, hi = 1;
  const cdf = (p) => { let s = 0, term = Math.pow(1 - p, n); for (let i = 0; i <= k; i++) { s += term; term *= (n - i) / (i + 1) * p / (1 - p); } return s; };
  for (let it = 0; it < 60; it++) { const mid = (lo + hi) / 2; if (cdf(mid) > 0.05) lo = mid; else hi = mid; }
  return hi;
}

// The detector, with the configuration of the working tree, on the files of
// the draw's commit: the categories of the hits of every line.
const cfg = D.loadConfig(ROOT);
const hitCache = new Map();
function hitsOf(commit, file, line) {
  const key = commit + ':' + file;
  if (!hitCache.has(key)) {
    const tree = treeOf(commit);
    cfg.pathSegments = D.pathSegmentsOf(tree.files);
    const a = D.analyzeText(file, tree.texts.get(file), cfg);
    const byLine = new Map();
    for (const h of a.hits) { if (!byLine.has(h.line)) byLine.set(h.line, new Set()); byLine.get(h.line).add(h.category); }
    hitCache.set(key, byLine);
  }
  return hitCache.get(key).get(line) || new Set();
}

function measure(labelsFile, quiet) {
  const doc = JSON.parse(fs.readFileSync(labelsFile, 'utf8'));
  const candidates = candidatesOf(doc);
  const labels = doc.labels || {};
  let prefix = 0;
  while (prefix < candidates.length && labels[candidates[prefix].id]) prefix++;
  let tp = 0, fp = 0, tn = 0, fn = 0, skipped = 0, prose = 0, proseFp = 0;
  const misses = [], falseAlarms = [];
  const how = {}; // category -> { counted, namesOnly }
  let counted = 0, namesOnly = 0;
  for (const c of candidates.slice(0, prefix)) {
    const lab = labels[c.id];
    if (lab === 'skip') { skipped++; continue; }
    const cats = hitsOf(doc.commit, c.file, c.line);
    const f = cats.size > 0;
    if (lab === 'it') {
      if (!f) { fn++; misses.push(c); continue; }
      tp++;
      const isCounted = [...cats].some(x => D.COUNT_CATEGORIES.includes(x));
      if (isCounted) counted++; else namesOnly++;
      for (const x of cats) {
        how[x] = how[x] || { counted: 0, namesOnly: 0 };
        if (isCounted) how[x].counted++; else how[x].namesOnly++;
      }
    } else if (lab === 'en' || lab === 'en-prose') {
      if (lab === 'en-prose') { prose++; if (f) proseFp++; }
      if (f) { fp++; falseAlarms.push(c); } else tn++;
    } else throw new Error('bad label ' + lab + ' for ' + c.id);
  }
  // Italian lines outnumber English ones in the uniform draws (the repository
  // is mostly Italian today), and plain precision would ride on that. The
  // criterion therefore uses BALANCED precision, the precision on a 1:1 mix:
  //   recall = TP / (TP + FN)      false-alarm rate = FP / (FP + TN)
  //   balanced precision = recall / (recall + false-alarm rate)
  // which is what a sample of 100 Italian + 100 English lines estimates.
  const recall = tp / (tp + fn);
  const falseAlarmRate = fp / (fp + tn);
  const precision = recall / (recall + falseAlarmRate);
  const doesUpper = upper95(fp, fp + tn);
  const precisionLower = recall / (recall + doesUpper);
  const r = { file: path.basename(labelsFile), doc, prefix, total: candidates.length, tp, fp, tn, fn, skipped, prose, proseFp,
    recall, falseAlarmRate, precision, faUpper: doesUpper, precisionLower, counted, namesOnly, how, misses, falseAlarms };
  if (doc.filter === 'english-cue') {
    r.enough = (tn + fp) >= 100;
    r.ok = r.enough && falseAlarmRate <= 0.02;
  } else {
    r.enough = (tp + fn) >= 100 && (tn + fp) >= 100;
    r.ok = r.enough && precision >= 0.98 && recall >= 0.95;
  }
  if (!quiet) report(r);
  return r;
}

function report(r) {
  const { doc } = r;
  console.log(r.file + ': commit ' + doc.commit + ', seed ' + doc.seed + (doc.filter ? ', filter ' + doc.filter : '') +
    ': first ' + r.prefix + ' of ' + r.total + ' candidate lines labelled');
  console.log('Italian lines ' + (r.tp + r.fn) + ', English lines ' + (r.tn + r.fp) + ' (of which English prose ' + r.prose +
    '), skipped ' + r.skipped);
  console.log('TP=' + r.tp + ' FP=' + r.fp + ' TN=' + r.tn + ' FN=' + r.fn + '   false alarms on English prose: ' + r.proseFp + ' of ' + r.prose);
  if (r.tp + r.fn > 0) {
    console.log('precision (balanced, 1:1)=' + r.precision.toFixed(3) + '  recall=' + r.recall.toFixed(3) +
      '  false-alarm rate=' + r.falseAlarmRate.toFixed(3) + ' (95% upper bound ' + r.faUpper.toFixed(3) +
      ', so balanced precision >= ' + r.precisionLower.toFixed(3) + ')');
    console.log('found Italian lines: counted ' + r.counted + ', names only ' + r.namesOnly + ' (blocked only when the name is new)');
    for (const [c, v] of Object.entries(r.how).sort()) console.log('    ' + c.padEnd(12) + ' counted ' + v.counted + ', names only ' + v.namesOnly);
  } else {
    console.log('false-alarm rate=' + r.falseAlarmRate.toFixed(3) + ' (95% upper bound ' + r.faUpper.toFixed(3) + ')');
  }
  for (const c of r.falseAlarms) console.log('  FALSE ALARM  ' + c.file + ':' + c.line + '  ' + c.text.trim().slice(0, 160));
  for (const c of r.misses) console.log('  MISSED       ' + c.file + ':' + c.line + '  ' + c.text.trim().slice(0, 160));
  if (!r.enough) console.log(doc.filter ? 'NOT ENOUGH LINES: at least 100 English lines are needed.' : 'NOT ENOUGH LINES: at least 100 Italian and 100 English are needed.');
  if (doc.filter) console.log(r.ok ? 'ENGLISH DRAW PASSED (false-alarm rate <= 0.02)' : 'ENGLISH DRAW NOT PASSED');
  else console.log(r.ok ? 'MEASURE PASSED (precision >= 0.98, recall >= 0.95)' : 'MEASURE NOT PASSED');
}

// Workshop vocabulary the repository does not use (holdout_workshop_terms in
// lexicon.json, kept out of every list the detector loads): how many a new
// identifier, key or folder would be caught on, by the Italian endings alone,
// and how many a prose line made of that word alone.
function holdout(quiet) {
  const lex = JSON.parse(fs.readFileSync(path.join(__dirname, 'lexicon.json'), 'utf8'));
  const terms = lex.holdout_workshop_terms || [];
  const asName = terms.filter(t => D.isItalianName('probe' + t[0].toUpperCase() + t.slice(1), cfg));
  const asProse = terms.filter(t => D.proseVerdict(t[0].toUpperCase() + t.slice(1), cfg).italian);
  if (!quiet) {
    console.log('holdout workshop terms: ' + terms.length + '; caught in a new name ' + asName.length + ', as a one-word prose line ' + asProse.length);
    console.log('    missed as a name: ' + terms.filter(t => !asName.includes(t)).join(' '));
  }
  return { terms: terms.length, asName: asName.length, asProse: asProse.length };
}

if (args.includes('--summary')) {
  const rs = DRAWS.map(d => measure(path.join(__dirname, d), false));
  console.log('');
  holdout(false);
  let fp = 0, n = 0;
  for (const r of rs) { fp += r.fp; n += r.fp + r.tn; }
  const blind = rs.filter(r => /blind/.test(r.file));
  let bfp = 0, bn = 0, btp = 0, bfn = 0;
  for (const r of blind) { bfp += r.fp; bn += r.fp + r.tn; btp += r.tp; bfn += r.fn; }
  const brecall = btp / (btp + bfn);
  console.log('');
  console.log('POOLED, all draws: false alarms ' + fp + ' of ' + n + ' English lines (95% upper bound ' + upper95(fp, n).toFixed(3) + ')');
  console.log('POOLED, blind draws only: false alarms ' + bfp + ' of ' + bn + ' (95% upper bound ' + upper95(bfp, bn).toFixed(3) +
    '), recall ' + brecall.toFixed(3) + ' on ' + (btp + bfn) + ' Italian lines, balanced precision ' +
    (brecall / (brecall + bfp / bn)).toFixed(3) + ' (>= ' + (brecall / (brecall + upper95(bfp, bn))).toFixed(3) + ' at 95%)');
  process.exit(rs.every(r => r.ok) ? 0 : 1);
}

const labelsFile = path.resolve(opt('--labels', path.join(__dirname, 'measure-labels.json')));
const doc = JSON.parse(fs.readFileSync(labelsFile, 'utf8'));

// --record, --drop-last, --show and --next work on the draw without running
// the detector: labelling stays blind.
if (['--record', '--drop-last', '--show', '--next', '--set'].some(a => args.includes(a))) {
  const candidates = candidatesOf(doc);
  const labels = doc.labels || {};
  let prefix = 0;
  while (prefix < candidates.length && labels[candidates[prefix].id]) prefix++;
  const save = () => { doc.labels = labels; fs.writeFileSync(labelsFile, JSON.stringify(doc, null, 1) + '\n', 'utf8'); };
  const record = opt('--record', null);
  if (record !== null) {
    const letters = record.replace(/\s+/g, '');
    const map = { i: 'it', e: 'en', p: 'en-prose', s: 'skip' };
    for (let k = 0; k < letters.length; k++) {
      if (!map[letters[k]]) { console.error('bad letter ' + letters[k]); process.exit(2); }
      labels[candidates[prefix + k].id] = map[letters[k]];
    }
    save();
    console.log('recorded ' + letters.length + ' labels, ' + (prefix + letters.length) + ' in all');
    process.exit(0);
  }
  // --set INDEX LETTER: correct one label (a slip found by reading the
  // labels back against the notes; the whole draw is read back, never only
  // the lines where the detector disagrees)
  const setAt = opt('--set', null);
  if (setAt !== null) {
    const k = parseInt(setAt, 10);
    const letter = args[args.indexOf('--set') + 2];
    const map = { i: 'it', e: 'en', p: 'en-prose', s: 'skip' };
    if (!(k < prefix) || !map[letter]) { console.error('--set INDEX LETTER, on a labelled line'); process.exit(2); }
    console.log(k + ': ' + labels[candidates[k].id] + ' -> ' + map[letter] + '   ' + candidates[k].text.trim().slice(0, 100));
    labels[candidates[k].id] = map[letter];
    save();
    process.exit(0);
  }
  const drop = opt('--drop-last', null);
  if (drop !== null) {
    const n = parseInt(drop, 10);
    for (let k = Math.max(0, prefix - n); k < prefix; k++) delete labels[candidates[k].id];
    save();
    console.log('dropped ' + Math.min(n, prefix) + ' labels, ' + Math.max(0, prefix - n) + ' left');
    process.exit(0);
  }
  const show = opt('--show', null);
  if (show !== null) {
    const from = parseInt(show, 10);
    const count = parseInt(args[args.indexOf('--show') + 2] || '100', 10);
    for (let k = from; k < Math.min(prefix, from + count); k++) {
      const c = candidates[k];
      console.log(k + '\t' + labels[c.id] + '\t' + c.file + ':' + c.line + '\t' + c.text.trim().slice(0, 150));
    }
    process.exit(0);
  }
  const n = parseInt(opt('--next', '50'), 10);
  candidates.slice(prefix, prefix + n).forEach((c, k) => console.log((prefix + k) + '\t' + c.file + ':' + c.line + '\t' + c.text.trim().slice(0, 220)));
  console.log('(' + prefix + ' labelled so far, ' + candidates.length + ' candidates)');
  process.exit(0);
}

process.exit(measure(labelsFile, false).ok ? 0 : 1);
