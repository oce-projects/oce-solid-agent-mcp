#!/usr/bin/env node
// =============================================================================
//  see-it-fail.js - every check of LanguageGateTests, seen red (W3.1-bis.1, W3.1-bis.11)
// =============================================================================
//
//  A check never seen failing is not a check. For each repair of the review
//  of W3.1-bis.1, and for each rule of the gate of zero (W3.1-bis.11: an entry
//  counts only when signed, the scope of the changelog, the closed list of
//  classes), this script switches the repair off in the working tree (one
//  textual mutation of the detector, the lexicon or the CI step), runs the
//  contract suite (Tests\bin\Oce.Solid.Agent.ContractTests.exe, with OCE_PRETEND_NO_INTEROP=1)
//  and expects the checks that guard that repair to go red. The file is put
//  back after every run, whatever happens.
//
//      node Checks/language/see-it-fail.js            all mutations (about a minute each)
//      node Checks/language/see-it-fail.js --check    only verify that every mutation applies
//      node Checks/language/see-it-fail.js D4 G2      only these
//
//  Needs Tests\bin\Oce.Solid.Agent.ContractTests.exe built from this tree. Exit 0 when every
//  mutation turned all its checks red, 1 otherwise.
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..');
const DET = 'Checks/language/detect-italian.js';
const LEX = 'Checks/language/lexicon.json';
const GATE = '.github/steps/language-gate.ps1';
// escapes are built, so that no editing tool turns them into characters
const U = (hex) => String.fromCharCode(92) + 'u' + hex;
const TYPO = '/[' + U('2019') + U('02bc') + ']/g';

const MUTATIONS = [
  { id: 'D1', what: 'holes of interpolated strings are blanked, not lexed', file: DET,
    edits: [['      const inner = lexCLike(text.slice(a, b), lang);', '      const inner = [];']],
    red: ['a string inside the hole of a C# interpolated string is found', 'a string inside the hole of a JavaScript template is found'] },
  { id: 'D2', what: 'no whole compound names', file: DET,
    edits: [['function compoundOf(name, cfg) {', 'function compoundOf(name, cfg) { return null;']],
    red: ['a protocol name joined by hyphens, found as a whole'] },
  { id: 'D3', what: 'a value after a placeholder is a name again', file: DET,
    edits: [['    if (!/\\S\\s+\\S/.test(sp.rest.trim())) {', '    if (true) {']],
    red: ['prose after a placeholder is found as prose'] },
  { id: 'D4', what: 'a JSON key with spaces has no element', file: DET,
    edits: [["line: it.line, element: it.key.trim(), text: it.key.trim() });", "line: it.line, text: it.key.trim() });"]],
    red: ['a JSON key with spaces is found as one key'] },
  { id: 'D5', what: 'no -atura ending', file: LEX,
    edits: [['"pattern": "(?:at|it|ut)ura"', '"pattern": "(?:at|it|ut)urX"']],
    red: ['an identifier with a workshop word the repository does not use is found', 'a folder named with a workshop word the repository does not use is found'] },
  { id: 'D6', what: 'content words do not count in prose', file: DET,
    edits: [['    if (w.length >= 4 && cfg.contentWords.has(lower) && !cfg.englishAllow.has(lower)) { strong++; continue; }', '']],
    red: ['a line of Italian content words only is found'] },
  { id: 'D7', what: 'echo words are code', file: DET,
    edits: [['    if (em && !/^\\s*#/.test(l)) {', '    if (false) {']],
    red: ['the words of an echo in a shell script are found'] },
  { id: 'D8', what: 'no here-documents', file: DET,
    edits: [['    if (hd) heredoc = hd[2];', '']],
    red: ['the body of a here-document is found'] },
  { id: 'D10', what: 'the typographic apostrophe is not an apostrophe', file: DET,
    edits: [[TYPO, '/[]/g', 'all']],
    red: ['the typographic apostrophe counts as the accent'] },
  { id: 'D11', what: 'a homograph alone is Italian', file: DET,
    edits: [['    if (cfg.weakWords.has(lower)) { weak++; continue; }', '    if (cfg.weakWords.has(lower)) { strong++; continue; }']],
    red: ['English lines with one Italian homograph each do not count'] },
  { id: 'D12', what: 'json_path for any class', file: DET,
    edits: [["      if (e.class !== 'detector-lexicon') fail(where + ': json_path is allowed only for the class detector-lexicon');", '']],
    red: ['a json_path outside the class detector-lexicon is refused'] },
  { id: 'D13', what: 'json_path on any file', file: DET,
    edits: [["fail(where + ': a json_path exception must name exactly one file of Checks/language/ (^Checks/language/<name>$)');", '{}']],
    red: ['a json_path over a folder of cases is refused'] },
  { id: 'D14', what: 'a pattern may match a function word', file: DET,
    edits: [["if (out.full.test(form)) fail(where + ': pattern matches the Italian function word", "if (false) fail(where + ': pattern matches the Italian function word"]],
    red: ['a pattern made of Italian function words is refused'] },
  { id: 'S1', what: 'an entry counts although it is not signed', file: DET,
    edits: [['      signed: sb !== null };', '      signed: true };']],
    red: ['an UNSIGNED entry suppresses nothing: the gate fails and says the entry waits for the signature', 'the changelog: the history is covered only when the entry is signed'] },
  { id: 'S2', what: 'a signature needs no date', file: DET,
    edits: [["typeof so === 'string' && /^\\d{4}-\\d{2}-\\d{2}$/.test(so)", 'true']],
    red: ['a signature without its date is refused', 'a date that is not a date is refused'] },
  { id: 'S3', what: 'the scope of the changelog is ignored', file: DET,
    edits: [['  const excsAt = (line) => all.filter(e => !e.scopeFrom || line >= scopeStart.get(e.index));', '  const excsAt = (line) => all;']],
    red: ['the changelog: Italian above the history and outside guillemets is found'] },
  { id: 'S4', what: 'an entry with no pattern is accepted', file: DET,
    edits: [["    if (!e.pattern && !e.json_path) fail(where + ': an exception needs", "    if (false) fail(where + ': an exception needs"]],
    red: ['an exception that covers a whole file is refused'] },
  { id: 'S5', what: 'the list of classes is open', file: DET,
    edits: [["    if (!EXCEPTION_CLASSES.has(e.class)) fail(where + ': class \"' + e.class + '\" is not in the closed list');", '']],
    red: ['a class that is not one of the six (transition) is refused', 'a class that is not one of the six (quote) is refused'] },
  { id: 'G1', what: 'a change to the gate needs no trailer', file: GATE,
    edits: [["            $signed = @(Get-Trailer $c 'Language-Gate-Config-Change')", "            $signed = @('x')"]],
    red: ['a change to the exceptions without its trailer is refused', 'the signing words in the body, not as a trailer'] },
  { id: 'G2', what: 'the verdict of the detector is not read', file: GATE,
    edits: [['    if ($code -ne 0) { $failed = $true }', '    if ($false) { $failed = $true }']],
    red: ['an Italian line fails and is listed outside every exception', 'Italian covered by an entry that is not signed fails'] },
];

function mutate(text, edits) {
  let t = text;
  for (const [from, to, all] of edits) {
    const n = t.split(from).length - 1;
    if (n === 0 || (!all && n !== 1)) throw new Error('the mutation does not apply (' + n + ' matches): ' + from.slice(0, 80));
    t = t.split(from).join(to);
  }
  return t;
}

function runSuite() {
  const exe = path.join(ROOT, 'Tests', 'bin', 'Oce.Solid.Agent.ContractTests.exe');
  const r = cp.spawnSync(exe, [], { cwd: ROOT, encoding: 'utf8', env: Object.assign({}, process.env, { OCE_PRETEND_NO_INTEROP: '1' }), maxBuffer: 64 << 20 });
  const lines = (r.stdout || '').split(/\r?\n/);
  return lines.filter(l => /^\s+NO\s/.test(l));
}

function main() {
  const args = process.argv.slice(2);
  const only = args.filter(a => /^[DGS]\d+$/.test(a));
  const list = MUTATIONS.filter(m => only.length === 0 || only.includes(m.id));
  for (const m of list) mutate(fs.readFileSync(path.join(ROOT, m.file), 'utf8'), m.edits);
  console.log(list.length + ' mutations apply.');
  if (args.includes('--check')) return 0;

  let ok = true;
  for (const m of list) {
    const file = path.join(ROOT, m.file);
    const original = fs.readFileSync(file);
    let red;
    try {
      fs.writeFileSync(file, mutate(original.toString('utf8'), m.edits));
      red = runSuite();
    } finally {
      fs.writeFileSync(file, original);
    }
    const missing = m.red.filter(name => !red.some(l => l.includes(name)));
    const mark = missing.length === 0 ? 'ok' : 'XX';
    if (missing.length) ok = false;
    console.log(mark + '  ' + m.id.padEnd(4) + m.what + ': ' + (m.red.length - missing.length) + ' of ' + m.red.length +
      ' expected checks red, ' + red.length + ' red in all');
    for (const name of missing) console.log('      STILL GREEN: ' + name);
  }
  return ok ? 0 : 1;
}

process.exitCode = main();
