#!/usr/bin/env node
// =============================================================================
//  see-it-fail.js - every check of LanguageGateTests, seen red (W3.1-bis.1)
// =============================================================================
//
//  A check never seen failing is not a check. For each repair of the review
//  of W3.1-bis.1 this script switches the repair off in the working tree (one
//  textual mutation of the detector, the lexicon or the CI step), runs the
//  contract suite (Tests\bin\Oce.Solid.Agent.ContractTests.exe, with PROVE_SENZA_INTEROP=1)
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
    red: ['hole of a C# interpolated string', 'hole of a JavaScript template'] },
  { id: 'D2', what: 'no whole compound names', file: DET,
    edits: [['function compoundOf(name, cfg) {', 'function compoundOf(name, cfg) { return null;']],
    red: ['protocol name joined by hyphens'] },
  { id: 'D3', what: 'a value after a placeholder is a name again', file: DET,
    edits: [['    if (!/\\S\\s+\\S/.test(sp.rest.trim())) {', '    if (true) {']],
    red: ['prose after a placeholder'] },
  { id: 'D4', what: 'a JSON key with spaces has no element', file: DET,
    edits: [["line: it.line, element: it.key.trim(), text: it.key.trim() });", "line: it.line, text: it.key.trim() });"],
            ['const element = h.element !== undefined && h.element !== null ? h.element : h.text;', 'const element = h.element;']],
    red: ['a JSON key with spaces is one new key', 'after a rewritten baseline the injected tree holds'] },
  { id: 'D5', what: 'no -atura ending', file: LEX,
    edits: [['"pattern": "(?:at|it|ut)ura"', '"pattern": "(?:at|it|ut)urX"']],
    red: ['identifier with a workshop word', 'folder named with a workshop word'] },
  { id: 'D6', what: 'content words do not count in prose', file: DET,
    edits: [['    if (w.length >= 4 && cfg.contentWords.has(lower) && !cfg.englishAllow.has(lower)) { strong++; continue; }', '']],
    red: ['a line of Italian content words only'] },
  { id: 'D7', what: 'echo words are code', file: DET,
    edits: [['    if (em && !/^\\s*#/.test(l)) {', '    if (false) {']],
    red: ['the words of an echo'] },
  { id: 'D8', what: 'no here-documents', file: DET,
    edits: [['    if (hd) heredoc = hd[2];', '']],
    red: ['the body of a here-document'] },
  { id: 'D9', what: 'lines, not words, per file', file: DET,
    edits: [['counts[h.category] = (counts[h.category] || 0) + (h.score || 1);', 'counts[h.category] = (counts[h.category] || 0) + 1;']],
    red: ['Italian appended to a line that is already Italian'] },
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
    edits: [["          if (out.full.test(form)) fail(where + ': pattern matches the Italian function word \"' + form + '\" on its own: too wide');", '']],
    red: ['a pattern made of Italian function words is refused'] },
  { id: 'G1', what: 'a signature anywhere in the range, as a line of the body', file: GATE,
    edits: [["        $raise = @(Get-Trailer $c 'Language-Baseline-Raise')",
             "        $raise = @((Invoke-Git log --format=%B \"$baseSha..$head\").Out | Where-Object { $_ -match '^\\s*Language-Baseline-Raise:\\s*\\S' })"]],
    red: ['growth in an earlier commit, signature on a later one', 'the signature on a different commit than the raise', 'the signing words in the body'] },
  { id: 'G2', what: 'a commit is judged with its own lexicon and exceptions', file: GATE,
    edits: [["@('--rev', $c, '--config-rev', $p1,", "@('--rev', $c, '--config-rev', $c,"]],
    red: ['a loosened lexicon does not judge its own commit'] },
  { id: 'G3', what: 'a change to the gate needs no trailer', file: GATE,
    edits: [['        if ($changed.Count -gt 0) {', '        if ($false) {']],
    red: ['an exception for the exact sentence, unsigned', 'the gate script switched off, judged by the base'] },
  { id: 'G4', what: 'renames are not followed', file: GATE,
    edits: [["'--renames', $renamesFile, '--quiet')", "'--quiet')", 'all']],
    red: ['a file renamed by hand, baseline rewritten'] },
  { id: 'G5', what: 'a merge is judged against its first parent only', file: GATE,
    edits: [["        $parentBaseline = 'git:' + ($gated -join '+')", "        $parentBaseline = 'git:' + $p1"]],
    red: ['a merge of a branch whose raise is signed'] },
  { id: 'G6', what: 'no check at a release tag', file: GATE,
    edits: [['    if ($tags.Count -gt 0 -and', '    if ($false -and']],
    red: ['a release tag with transition left'] },
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
  const r = cp.spawnSync(exe, [], { cwd: ROOT, encoding: 'utf8', env: Object.assign({}, process.env, { PROVE_SENZA_INTEROP: '1' }), maxBuffer: 64 << 20 });
  const lines = (r.stdout || '').split(/\r?\n/);
  return lines.filter(l => /^\s+NO\s/.test(l));
}

function main() {
  const args = process.argv.slice(2);
  const only = args.filter(a => /^[DG]\d+$/.test(a));
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
