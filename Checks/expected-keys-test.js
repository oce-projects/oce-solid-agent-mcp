#!/usr/bin/env node
// =============================================================================
//  expected-keys-test.js - every JSON reader of the checks, seen failing
//  (W3.1-bis.2, item 5)
// =============================================================================
//
//  WHY IT EXISTS
//
//  The translation of W3.1-bis renames the keys of the catalogue (.5), of the
//  cases (.4) and of what the bridge writes. A reader that says
//  `catalogo.operazioni || []` or `$cat.operazioni` (a missing property is
//  $null in PowerShell, without an error) keeps running after the rename and
//  prints zeros that look like a measure. Every such reader of `Controlli/`
//  now exits non-zero when an expected key is missing; this script feeds each
//  one a fake input without the key and expects the failure, and feeds it the
//  real input (or a well-formed fake) and expects it to pass the key check.
//  A check never seen failing is not a check.
//
//  The readers, and the grep that found them (`|| []`, `?.`, properties read
//  from ConvertFrom-Json objects, every JSON.parse and ConvertFrom-Json in
//  `Controlli/` and `.github/passi/`):
//
//    Controlli/conta-provate.js             catalogue and cases
//    Controlli/controlla-catalogo.ps1       catalogue
//    Controlli/domande-senza-risposta.js    the question registry of the bridge
//    Controlli/prova-x2.js                  the answers of the bridge (--self-test)
//    Controlli/lingua/detect-italian.js     lexicon.json and baseline.json
//
//  Readers looked at and left as they are, with the reason: measure.js reads
//  `labels` with a fallback on purpose (a new draw has none yet, and an
//  empty draw fails its own "not enough lines" check); prova-installatore.js
//  reads a client configuration whose missing `mcpServers` already fails its
//  checks; ci-proof.ps1 turns a missing step into "no step", which fails the
//  expectation.
//
//      node Controlli/expected-keys-test.js
//
//  Exit 0 when every case is as expected, 1 otherwise. About 10 seconds (it
//  starts Windows PowerShell for the catalogue check).
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');
const cp = require('child_process');

const ROOT = path.resolve(__dirname, '..');
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'expected-keys-'));

// The readers, and the keys they expect (the names are the protocol of today:
// when a window renames a key, the reader and this table change together).
const COUNT_TESTED = 'Controlli/conta-provate.js';
const CATALOGUE_CHECK = 'Controlli/controlla-catalogo.ps1';
const QUESTIONS = 'Controlli/domande-senza-risposta.js';
const PROBE_X2 = 'Controlli/prova-x2.js';
const GATE = 'Controlli/lingua/detect-italian.js';
const KEY = {
  operations: 'operations', families: 'families', name: 'name', family: 'family',
  when: 'timestamp', kind: 'kind', similar: 'similar',
};
// The keys of a bench case, renamed by window 1 (W3.1-bis.4), and of a step of
// a plan, which is the bridge's protocol (window 2). A case written with the
// names of before the window is the wrong key each check feeds the reader.
const CASE = { name: 'name', family: 'family', steps: 'steps', build: 'build', operation: 'operation', args: 'arguments' };
const PLAN = { operation: 'operation', args: 'arguments' };
const BEFORE = { name: 'nome', steps: 'passi', operation: 'operazione', operations: 'operazioni', families: 'famiglie', family: 'famiglia', kind: 'tipo' };
const label = (script) => path.basename(script).replace(/\.(js|ps1)$/, '');
const q = (s) => '"' + s + '"';

function write(rel, content) {
  const f = path.join(work, rel);
  fs.mkdirSync(path.dirname(f), { recursive: true });
  fs.writeFileSync(f, typeof content === 'string' ? content : JSON.stringify(content, null, 1), 'utf8');
  return f;
}

function run(cmd, args) {
  const r = cp.spawnSync(cmd, args, { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 << 20 });
  return { code: r.status, out: (r.stdout || '') + (r.stderr || '') };
}
const node = (script, args) => run(process.execPath, [path.join(ROOT, script)].concat(args));
// The catalogue check takes its arguments by position (catalogue, library):
// they survive the rename of its parameters in W3.1-bis.6.
const pwsh = (script, args) => run(process.platform === 'win32' ? 'powershell.exe' : 'pwsh',
  ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', path.join(ROOT, script)].concat(args));

// An object with one key renamed: the value moves to the new name.
function renamed(obj, from, to) {
  const out = {};
  for (const [k, v] of Object.entries(obj)) out[k === from ? to : k] = v;
  return out;
}

// ── fake inputs ─────────────────────────────────────────────────────────────
const realCataloguePath = path.join(ROOT, 'Strumenti', 'catalogo.json');
const realCatalogue = JSON.parse(fs.readFileSync(realCataloguePath, 'utf8'));
const operations = realCatalogue[KEY.operations];
const firstOp = operations[0];
const withFirst = (op) => Object.assign({}, realCatalogue, { [KEY.operations]: [op].concat(operations.slice(1)) });

const catalogueNoOps = write('catalogue-no-ops.json', renamed(realCatalogue, KEY.operations, BEFORE.operations));
const catalogueNoName = write('catalogue-no-name.json', withFirst(renamed(firstOp, KEY.name, BEFORE.name)));
const catalogueNoFamilies = write('catalogue-no-families.json', renamed(realCatalogue, KEY.families, BEFORE.families));
const catalogueNoFamily = write('catalogue-no-family.json', withFirst(renamed(firstOp, KEY.family, BEFORE.family)));
const catalogueEmpty = write('catalogue-empty.json', Object.assign({}, realCatalogue, { [KEY.operations]: [] }));
const catalogueNoDeclared = write('catalogue-no-declared-families.json', Object.assign({}, realCatalogue, { [KEY.families]: {} }));

const step = { [CASE.operation]: firstOp[KEY.name], [CASE.args]: {} };
const planStep = { [PLAN.operation]: firstOp[KEY.name], [PLAN.args]: {} };
const goodCase = { [CASE.name]: 'a_fake_case', [CASE.family]: 'x', [CASE.steps]: [step] };
const casesGood = path.dirname(write('cases-good/a.json', goodCase));
write('cases-good/plan.json', [planStep]);
const casesNoSteps = path.dirname(write('cases-no-steps/a.json', renamed(goodCase, CASE.steps, BEFORE.steps)));
const casesNoOperation = path.dirname(write('cases-no-operation/a.json',
  Object.assign({}, goodCase, { [CASE.steps]: [renamed(step, CASE.operation, BEFORE.operation)] })));
const casesNoName = path.dirname(write('cases-no-name/a.json', renamed(goodCase, CASE.name, BEFORE.name)));
const casesBroken = path.dirname(write('cases-broken/a.json', '{ ' + q(CASE.name) + ': "a_fake_case", '));
const casesPlanNoOperation = path.dirname(write('cases-plan-no-operation/a.json', goodCase));
write('cases-plan-no-operation/plan.json', [renamed(planStep, PLAN.operation, BEFORE.operation)]);
const casesNone = path.join(work, 'cases-none');
fs.mkdirSync(casesNone, { recursive: true });

const record = { [KEY.when]: '2026-09-30T10:00:00', [KEY.kind]: 'x', [KEY.name]: 'y', [KEY.similar]: ['z'] };
const registryGood = write('registry-good.jsonl', JSON.stringify(record) + '\n');
const registryRenamed = write('registry-renamed.jsonl', JSON.stringify(renamed(renamed(record, KEY.kind, BEFORE.kind), KEY.name, BEFORE.name)) + '\n');

// the gate reads its lexicon from a checkout: a tiny git repository
function gateRoot(name, mutateLexicon) {
  const dir = path.join(work, name);
  fs.mkdirSync(path.join(dir, 'Controlli', 'lingua'), { recursive: true });
  const lex = JSON.parse(fs.readFileSync(path.join(ROOT, 'Controlli', 'lingua', 'lexicon.json'), 'utf8'));
  mutateLexicon(lex);
  fs.writeFileSync(path.join(dir, 'Controlli', 'lingua', 'lexicon.json'), JSON.stringify(lex, null, 1), 'utf8');
  fs.copyFileSync(path.join(ROOT, 'Controlli', 'lingua', 'exceptions.json'), path.join(dir, 'Controlli', 'lingua', 'exceptions.json'));
  fs.writeFileSync(path.join(dir, 'notes.md'), 'A line of English.\n', 'utf8');
  cp.execFileSync('git', ['init', '-q', dir], { stdio: 'ignore' });
  cp.execFileSync('git', ['-C', dir, '-c', 'core.autocrlf=false', 'add', '-A'], { stdio: 'ignore' });
  return dir;
}
const lexiconGood = gateRoot('gate-good', () => {});
const lexiconNoContent = gateRoot('gate-no-content-words', (lex) => { delete lex.content_words; });
const lexiconEmptyFunction = gateRoot('gate-empty-function-words', (lex) => { lex.function_words = []; });
const baselineNoSets = write('baseline-no-sets.json', { format: 2, files: {} });
const baselineGood = write('baseline-good.json', { format: 2, files: {}, sets: { identifiers: [], json_keys: [], protocol: [], paths: [] } });

// ── the cases: [name, run, expected exit (a number, 'any' or 'not 2'), what the output must show]
const CP = label(COUNT_TESTED), CC = label(CATALOGUE_CHECK), QS = label(QUESTIONS), X2 = label(PROBE_X2), GT = label(GATE);
const cases = [
  [CP + ': the real tree', () => node(COUNT_TESTED, []), 0, /\(\S+ = true\): \d+/],
  [CP + ': a well-formed fake', () => node(COUNT_TESTED, ['--catalogue', realCataloguePath, '--cases', casesGood]), 0, /^\S+ \S+: 1\r?$/m],
  [CP + ': catalogue without ' + q(KEY.operations), () => node(COUNT_TESTED, ['--catalogue', catalogueNoOps, '--cases', casesGood]), 1, 'no array ' + q(KEY.operations)],
  [CP + ': an operation without ' + q(KEY.name), () => node(COUNT_TESTED, ['--catalogue', catalogueNoName, '--cases', casesGood]), 1, 'operation 0: no string ' + q(KEY.name)],
  [CP + ': an empty catalogue', () => node(COUNT_TESTED, ['--catalogue', catalogueEmpty, '--cases', casesGood]), 1, 'is empty'],
  [CP + ': an unreadable catalogue', () => node(COUNT_TESTED, ['--catalogue', path.join(work, 'nothing.json'), '--cases', casesGood]), 1, 'cannot read the catalogue'],
  [CP + ': a case without ' + q(CASE.steps) + ' or ' + q(CASE.build), () => node(COUNT_TESTED, ['--cases', casesNoSteps]), 1, 'neither ' + q(CASE.steps) + ' nor ' + q(CASE.build)],
  [CP + ': a step without ' + q(CASE.operation), () => node(COUNT_TESTED, ['--cases', casesNoOperation]), 1, 'no string ' + q(CASE.operation)],
  [CP + ': a case without ' + q(CASE.name), () => node(COUNT_TESTED, ['--cases', casesNoName]), 1, 'no string ' + q(CASE.name)],
  [CP + ': a case that does not parse', () => node(COUNT_TESTED, ['--cases', casesBroken]), 1, 'does not parse'],
  [CP + ': a plan step without ' + q(PLAN.operation), () => node(COUNT_TESTED, ['--cases', casesPlanNoOperation]), 1, 'step 1: no string ' + q(PLAN.operation)],
  [CP + ': an empty folder of cases', () => node(COUNT_TESTED, ['--cases', casesNone]), 1, 'no case at all'],
  [CP + ': a bench folder without C# sources', () => node(COUNT_TESTED, ['--bench', casesNone]), 1, 'no C# source of the bench'],
  [CC + ': the real catalogue passes the key check', () => pwsh(CATALOGUE_CHECK, [realCataloguePath]), 'any', /the catalogue has the expected keys .*, [1-9][0-9]* declared families/],
  [CC + ': ' + q(KEY.families) + ' that declares no family', () => pwsh(CATALOGUE_CHECK, [catalogueNoDeclared]), 1, "'" + KEY.families + "' declares no family"],
  [CC + ': catalogue without ' + q(KEY.operations), () => pwsh(CATALOGUE_CHECK, [catalogueNoOps]), 1, "the catalogue has no '" + KEY.operations + "'"],
  [CC + ': catalogue without ' + q(KEY.families), () => pwsh(CATALOGUE_CHECK, [catalogueNoFamilies]), 1, "the catalogue has no '" + KEY.families + "'"],
  [CC + ': an operation without ' + q(KEY.name), () => pwsh(CATALOGUE_CHECK, [catalogueNoName]), 1, "operation 0 has no '" + KEY.name + "'"],
  [CC + ': an operation without ' + q(KEY.family), () => pwsh(CATALOGUE_CHECK, [catalogueNoFamily]), 1, "operation 0 has no '" + KEY.family + "'"],
  [QS + ': a well-formed registry', () => node(QUESTIONS, [registryGood]), 0, /:\s+1\r?$/m],
  [QS + ': a registry with renamed keys', () => node(QUESTIONS, [registryRenamed]), 1, 'EXPECTED KEYS MISSING'],
  [X2 + ': the answers of the bridge', () => node(PROBE_X2, ['--self-test']), 0, 'The reading fails on every answer without the expected keys.'],
  [GT + ': a well-formed lexicon runs', () => node(GATE, ['--root', lexiconGood, '--baseline', baselineGood, '--quiet']), 'not 2', null],
  [GT + ': lexicon without "content_words"', () => node(GATE, ['--root', lexiconNoContent, '--baseline', baselineGood, '--quiet']), 2, 'no list "content_words"'],
  [GT + ': lexicon with no function words', () => node(GATE, ['--root', lexiconEmptyFunction, '--baseline', baselineGood, '--quiet']), 2, 'the list "function_words" is empty'],
  [GT + ': baseline without "sets"', () => node(GATE, ['--root', lexiconGood, '--baseline', baselineNoSets, '--quiet']), 2, 'no "sets" object'],
];

// With a built library, the catalogue check runs to its end: a declared
// family that no operation uses must turn it red (the old reading of
// `families` passed it), and the real catalogue must pass. Without the
// library these two are skipped, and the run says so.
const libraryIndex = process.argv.indexOf('--library');
const library = libraryIndex >= 0 ? path.resolve(process.argv[libraryIndex + 1])
  : path.join(ROOT, 'Riscontro.Disegno', 'bin', 'Release', 'Riscontro.Disegno.dll');
const skipped = [];
if (fs.existsSync(library)) {
  const extraFamily = 'an_unused_family';
  const catalogueExtraFamily = write('catalogue-extra-family.json', Object.assign({}, realCatalogue,
    { [KEY.families]: Object.assign({}, realCatalogue[KEY.families], { [extraFamily]: 'declared and empty' }) }));
  cases.push([CC + ': with the library, the real catalogue', () => pwsh(CATALOGUE_CHECK, [realCataloguePath, library]), 0, null]);
  cases.push([CC + ': with the library, a declared family no operation uses', () => pwsh(CATALOGUE_CHECK, [catalogueExtraFamily, library]), 1, extraFamily]);
} else {
  skipped.push('the two runs of the catalogue check to its end: no library at ' + library + ' (build.ps1 builds it; or --library <dll>)');
}

let wrong = 0;
try {
  for (const [name, fn, expected, text] of cases) {
    const r = fn();
    const codeOk = expected === 'any' ? true : expected === 'not 2' ? r.code !== 2 && r.code !== null : r.code === expected;
    const textOk = !text || (text instanceof RegExp ? text.test(r.out) : r.out.includes(text));
    const ok = codeOk && textOk;
    if (!ok) wrong++;
    console.log((ok ? '  ok  ' : '  XX  ') + name + ': exit ' + r.code + (textOk ? '' : ', ' + String(text) + ' not in the output'));
    if (!ok) console.log('        ' + r.out.trim().split(/\r?\n/).slice(-6).join('\n        '));
  }
} finally {
  fs.rmSync(work, { recursive: true, force: true });
}
for (const s of skipped) console.log('  --  skipped: ' + s);
console.log('');
console.log(wrong === 0
  ? 'Every reader fails when an expected key is missing, and passes on what is there (' + cases.length + ' cases).'
  : wrong + ' of ' + cases.length + ' cases NOT as expected.');
process.exitCode = wrong === 0 ? 0 : 1;
