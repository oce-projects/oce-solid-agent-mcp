// =============================================================================
//  questions-without-answer.js - what was asked of the bridge and was not there
// =============================================================================
//
//  WHY IT EXISTS (entry Z-G7)
//
//  The five operations of the catalogue without any bench case --
//  `circular_pattern`, `weld_bead`, `gusset`, `trim_members`,
//  `end_cap` -- were chosen by US. Which ones are really needed nobody knows, and
//  asking people does not make it known: whoever uses the bridge does not have
//  the list of what is not there.
//
//  The bridge writes a line every time it is asked for a name that does not
//  exist (`Tools/QuestionRegistry.cs`). This tool reads it.
//
//  ── THE DISTINCTION THAT COUNTS, AND IT IS NOT THE COUNT ────────────────────
//
//  A name asked ten times **with a close name in the catalogue** is a
//  synonym to add: the operation is already there, it is called something else,
//  and it costs one line.
//
//  A name asked ten times **with nothing close** is an operation to
//  build. They are two different jobs and a single count would mix them, which is
//  why the list is split in two.
//
//  ── THIS IS NOT A GUARD ─────────────────────────────────────────────────────
//
//  It always exits 0. There is nothing to get wrong in a registry: if it is empty
//  it means nobody asked for anything unknown, and it is not a problem.
//  It does not go into the pre-push hook.
//
//  USAGE
//      node Checks/questions-without-answer.js
//      node Checks/questions-without-answer.js <file.jsonl>
//      node Checks/questions-without-answer.js --lines    all of them, in time order
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const NAME = 'domande-senza-risposta.jsonl';

const perLines = process.argv.includes('--lines');
const given = process.argv.slice(2).find(a => !a.startsWith('--'));

// The same places in which the bridge would write it: the variable, then next
// to the executable. If one day the order changed there and not here, this
// tool would read an old file without saying so -- so WHICH one is printed.
function location() {
  if (given) return given;
  const fromEnv = process.env.RISCONTRO_REGISTRO;
  if (fromEnv !== undefined) return fromEnv === '' ? null : fromEnv;
  return path.join(root, 'bin', NAME);
}

const file = location();

if (file === null) {
  console.log('RISCONTRO_REGISTRO is set to an empty string: the bridge does not');
  console.log('record anything, and there is nothing to read' + '.');
  process.exit(0);
}

console.log('registro: ' + file);
console.log('');

if (!fs.existsSync(file)) {
  console.log('The file is not there yet.');
  console.log('');
  console.log('That means one of two things, and they are told apart by looking whether the bridge');
  console.log('has ever been used from here: either nobody asked for anything that');
  console.log('did not exist, or the bridge cannot write where it thinks (and in');
  console.log('that case it stays silent on purpose -- it is rule 2 of the log).');
  process.exit(0);
}

const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(r => r.trim());
const entries = [];
let unreadable = 0;

for (const r of lines) {
  try { entries.push(JSON.parse(r)); }
  catch { unreadable++; }
}

// EXPECTED KEYS (W3.1-bis.2, item 5). Every record is written by
// `Tools/QuestionRegistry.cs` with `timestamp`, `kind` and `name`
// (`similar` only when there are neighbours). A record without one of them is
// a producer that renamed its keys: read with a fallback, every question
// would count as "?" with an empty name, and the report would look like one.
const withoutKeys = entries.filter(v => v === null || typeof v !== 'object' || Array.isArray(v) ||
  ['timestamp', 'kind', 'name'].some(k => typeof v[k] !== 'string') ||
  (v.similar !== undefined && !Array.isArray(v.similar)));
if (withoutKeys.length) {
  console.log('EXPECTED KEYS MISSING: ' + withoutKeys.length + ' of ' + entries.length +
              ' records lack "timestamp", "kind" or "name" (strings). The first:');
  console.log('    ' + JSON.stringify(withoutKeys[0]).slice(0, 200));
  console.log('If the bridge renamed them, this reader is renamed in the same commit.');
  process.exit(1);
}

if (perLines) {
  for (const v of entries) {
    console.log('  ' + (v.timestamp || '?') + '  ' + (v.kind || '?') + '  ' +
                JSON.stringify(v.name) +
                (v.similar ? '   vicini: ' + v.similar.join(', ') : ''));
  }
  console.log('');
}

const counts = new Map();
for (const v of entries) {
  const key = (v.kind || '?') + '\u0000' + (v.name || '');
  let c = counts.get(key);
  if (!c) {
    c = { kind: v.kind || '?', name: v.name || '', quante: 0,
          prima: v.timestamp, ultima: v.timestamp, vicini: new Set() };
    counts.set(key, c);
  }
  c.quante++;
  if (v.timestamp) {
    if (!c.prima || v.timestamp < c.prima) c.prima = v.timestamp;
    if (!c.ultima || v.timestamp > c.ultima) c.ultima = v.timestamp;
  }
  for (const s of (v.similar || [])) c.vicini.add(s);
}

const all = [...counts.values()].sort((a, b) => b.quante - a.quante ||
                                                 a.name.localeCompare(b.name));

console.log('questions recorded:        ' + entries.length);
console.log('distinct names:            ' + all.length);
if (unreadable) console.log('unreadable lines:          ' + unreadable);
console.log('');

if (all.length === 0) {
  console.log('Nobody asked for anything that was not there.');
  process.exit(0);
}

function print(docTitle, list, queue) {
  if (list.length === 0) return;
  console.log(docTitle);
  for (const c of list) {
    const v = c.vicini.size ? '   vicini: ' + [...c.vicini].join(', ') : '';
    console.log('  ' + String(c.quante).padStart(4) + '  [' + c.kind + '] ' +
                c.name + v);
    console.log('        dal ' + (c.prima || '?') + ' al ' + (c.ultima || '?'));
  }
  console.log('');
  console.log('  ' + queue);
  console.log('');
}

print('TO BUILD — no near name in the catalog:',
       all.filter(c => c.vicini.size === 0),
       'The catalog had nothing to suggest: whoever asked wanted something ' +
       'that does not exist.');

print('MAYBE JUST A SYNONYM — the catalog had something close:',
       all.filter(c => c.vicini.size > 0),
       'If the near name really is the same thing, it costs one line of ' +
       'synonym instead of a new operation.');

console.log('A name asked for by MORE than one model is worth more than the same');
console.log('name asked ten times by a single one, and this file does not');
console.log('tell them apart: it does not write who asked, and it must not. They are told apart');
console.log('by looking at the dates -- different sessions, different days.');
