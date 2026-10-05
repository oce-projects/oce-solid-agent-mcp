// =============================================================================
//  How many «tested» operations really have something that tests them  (entry Z-C6)
// =============================================================================
//
//  `Provata = true` in the catalogue means two different things that in the file
//  read the same:
//
//    - «somebody measured it once»          <- a signature in the changelog
//    - «something checks it on every run»   <- a case that runs
//
//  This counts the second. For every operation in the catalogue it looks whether there is
//  something that exercises it, and distinguishes THREE ways, because they are not worth the same:
//
//    caso dichiarato   a file in `Bench\cases\` with expectations inside.
//                      It runs on every run, and if the behaviour changes it turns
//                      red. It is the only one that is worth it fully.
//
//    C# del banco      a group written by hand. It runs on every run and asserts
//                      in C#: it is worth it, and it is written here so that it is not
//                      read as a hole.
//
//    piano a mano      an ARRAY in `cases\drawing\`, which launches `run_plans.js`.
//                      **It runs in no run and asserts nothing**: it is
//                      a note of how it was measured, not a check.
//
//  WHY IT IS NOT A CONTRACT CHECK YET. Measured on 23/9/2026:
//  five operations in the catalogue are exercised by nothing. A guard that
//  is born with five permanent reds is a list that somebody will silence; those
//  are fixed first, then the guard is turned on over a clean field.
//
//      node Checks\count-tested.js
//      node Checks\count-tested.js --solo-orfane
//      node Checks\count-tested.js --catalogue <file> --cases <folder> [--bench <folder>]
//
//  EXIT CODES (W3.1-bis.2, item 5). It exits 0 once it has measured: the
//  numbers are a measure, not a verdict. It exits 1 when what it reads is
//  not there: an unreadable catalogue, a case file that does not parse, or
//  an expected key that is missing (`operations` and `name` in the
//  catalogue; `name`, `steps` or `build`, and `operation` in every step of
//  a case; `operation` in every step of a plan, which is the bridge's
//  protocol and is renamed with it in W3.1-bis.5). The translation of W3.1-bis renames those
//  keys, and a reader that falls back to an empty list prints zeros that
//  look like a measure. A count read from nothing is not zero.
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
function optionValue(name, fallback) {
  const i = process.argv.indexOf(name);
  return i >= 0 && i + 1 < process.argv.length ? path.resolve(process.argv[i + 1]) : fallback;
}
const CATALOG = optionValue('--catalogue', path.join(ROOT, 'Tools', 'catalog.json'));
const CASES = optionValue('--cases', path.join(ROOT, 'Bench', 'cases'));
const BENCH = optionValue('--bench', path.join(ROOT, 'Bench'));

const soloOrfane = process.argv.indexOf('--solo-orfane') >= 0;

// Every expected key that is missing, with where. Printed all together, and
// then the script exits 1 without printing the counts.
const missingKeys = [];
function expectKey(obj, key, kind, where) {
  const ok = obj !== null && typeof obj === 'object' && !Array.isArray(obj) && Object.prototype.hasOwnProperty.call(obj, key) &&
    (kind === 'array' ? Array.isArray(obj[key]) : typeof obj[key] === kind && obj[key] !== '');
  if (!ok) missingKeys.push(where + ': no ' + kind + ' "' + key + '"');
  return ok;
}
function stopIfMissing() {
  if (missingKeys.length === 0) return;
  console.log('EXPECTED KEYS MISSING (' + missingKeys.length + '):');
  for (const m of missingKeys.slice(0, 40)) console.log('    ' + m);
  if (missingKeys.length > 40) console.log('    ... ' + (missingKeys.length - 40) + ' more');
  console.log('');
  console.log('No counts are printed: they would be zeros read from nothing. If a key was');
  console.log('renamed, this reader is renamed in the same commit (W3.1-bis.4 and .5).');
  process.exit(1);
}

// ── THE OPERATIONS THAT A CLIENT SEES ────────────────────────────────────
//
// We read the exported catalogue and not the C#: it is what reaches the client,
// and it is what `Provata = true` is the promise of. The exporter already leaves
// out the `Provata = false` ones and says so out loud while writing the file.
let catalog;
try { catalog = JSON.parse(fs.readFileSync(CATALOG, 'utf8').replace(/^\ufeff/, '')); }
catch (e) {
  console.log('cannot read the catalogue ' + CATALOG + ': ' + e.message);
  console.log('it is written by  .\\bin\\Oce.Solid.Agent.Catalog.exe Tools\\catalog.json');
  process.exit(1);
}
if (expectKey(catalog, 'operations', 'array', 'catalogue')) {
  if (catalog.operations.length === 0) missingKeys.push('catalogue: "operations" is empty');
  catalog.operations.forEach((o, i) => expectKey(o, 'name', 'string', 'catalogue, operation ' + i));
}
stopIfMissing();
const operations = catalog.operations.map((o) => o.name).sort();

// ── WHO EXERCISES THEM ───────────────────────────────────────────────────
const fromCases = new Map();     // operation -> [cases]
const fromRegression = new Map();   // operation -> [regression cases]
let plansText = '';
let howManyCases = 0;

function scroll(folder) {
  let entries;
  try { entries = fs.readdirSync(folder, { withFileTypes: true }); }
  catch (e) { missingKeys.push(folder + ': cannot read the folder (' + e.message + ')'); return; }
  for (const v of entries) {
    const p = path.join(folder, v.name);
    // The cases of the aliases (W3.1-bis.5, decision D4) exercise the
    // dictionary of the old names, not an operation, and two of them name
    // their operation in Italian on purpose: they are not counted here.
    // Removed with the aliases in W3.1-bis.11.
    if (v.isDirectory() && v.name === 'aliases' && path.resolve(folder) === path.resolve(CASES)) continue;
    if (v.isDirectory()) { scroll(p); continue; }
    if (!v.name.endsWith('.json')) continue;
    const where = path.relative(ROOT, p).replace(/\\/g, '/');
    let raw, j;
    try { raw = fs.readFileSync(p, 'utf8'); j = JSON.parse(raw.replace(/^\ufeff/, '')); }
    catch (e) { missingKeys.push(where + ': does not parse (' + e.message + ')'); continue; }

    // A PLAN IS AN ARRAY, A CASE IS AN OBJECT. The difference is
    // structural, as already written in `DeclaredCases.cs`: it cannot be guessed
    // from the file name.
    if (Array.isArray(j)) {
      // a plan is read as text below, but its steps carry the same key
      j.forEach((step, i) => expectKey(step, 'operation', 'string', where + ', step ' + (i + 1)));
      plansText += raw;
      continue;
    }

    howManyCases++;
    expectKey(j, 'name', 'string', where);
    const itemName = typeof j.name === 'string' ? j.name : path.basename(v.name, '.json');
    // A case builds its part with `steps` (a normal run) or `build` (a
    // regression case): with neither, its operations would be counted as none.
    const lists = ['steps', 'build'].filter((k) => Object.prototype.hasOwnProperty.call(j, k));
    if (lists.length === 0) missingKeys.push(where + ': neither "steps" nor "build"');
    // THE REGRESSION CASES ARE NOT CASES OF A NORMAL RUN (25/9/2026). Their
    // `costruisci` runs only with `-Regression`, and this script
    // counted them as «caso dichiarato»: the recount of 24/9 said 72 out of 73,
    // and the contract check on the GREEN cases of the last run found
    // three operations (hole_wizard, extrude_on_face, fillet_edges) that in
    // a normal run nobody touched. Now they sit in a separate way.
    for (const key of lists) {
      if (!expectKey(j, key, 'array', where)) continue;
      const target = key === 'steps' ? fromCases : fromRegression;
      j[key].forEach((planStep, i) => {
        if (!expectKey(planStep, 'operation', 'string', where + ', ' + key + ' ' + (i + 1))) return;
        const op = planStep.operation;
        if (!target.has(op)) target.set(op, []);
        if (target.get(op).indexOf(itemName) < 0) target.get(op).push(itemName);
      });
    }
  }
}
scroll(CASES);
if (howManyCases === 0) missingKeys.push(path.relative(ROOT, CASES) + ': no case at all');
stopIfMissing();

let textCs = '';
try {
  for (const f of fs.readdirSync(BENCH))
    if (f.endsWith('.cs')) textCs += fs.readFileSync(path.join(BENCH, f), 'utf8');
} catch (e) { missingKeys.push(path.relative(ROOT, BENCH) + ': cannot read the bench sources (' + e.message + ')'); }
// Read from nothing, the bench's own C# would count as touching no operation
// (W3.1-bis.2, item 5: the folder is renamed in W3.1-bis.6).
if (textCs === '') missingKeys.push(path.relative(ROOT, BENCH) + ': no C# source of the bench');
stopIfMissing();

function howItIsTested(op) {
  const quotationMarks = '"' + op + '"';
  if (fromCases.has(op)) return { come: 'caso dichiarato', dettaglio: fromCases.get(op).slice(0, 3).join(', ') };
  if (fromRegression.has(op))
    return { come: 'solo regressione', dettaglio: 'gira solo con -Regression: ' + fromRegression.get(op).slice(0, 3).join(', ') };
  if (textCs.indexOf(quotationMarks) >= 0) return { come: 'C# del banco', dettaglio: '' };
  if (plansText.indexOf(quotationMarks) >= 0)
    return { come: 'solo piano a mano', dettaglio: 'non gira in nessun giro, non asserisce niente' };
  return { come: 'NIENTE', dettaglio: 'a catalogo, e nessun giro la tocca' };
}

const perMode = { 'caso dichiarato': [], 'solo regressione': [], 'C# del banco': [], 'solo piano a mano': [], 'NIENTE': [] };
for (const op of operations) perMode[howItIsTested(op).come].push(op);

if (!soloOrfane) {
  console.log('catalogo: ' + CATALOG);
  console.log('operazioni a catalogo (Provata = true): ' + operations.length);
  console.log('casi dichiarati: ' + howManyCases);
  console.log('');
  for (const mode of ['caso dichiarato', 'solo regressione', 'C# del banco', 'solo piano a mano', 'NIENTE']) {
    console.log(String(perMode[mode].length).padStart(4) + '  ' + mode);
  }
  console.log('');
  if (perMode['solo regressione'].length > 0) {
    console.log('SOLO IN REGRESSIONE — girano solo con -Regression, non in un giro normale:');
    for (const o of perMode['solo regressione']) console.log('    ' + o);
    console.log('');
  }
  console.log('(La guardia vera e\' il controllo di contratto «ogni operazione a catalogo ha un');
  console.log(' caso VERDE nell\'ultimo giro», che legge i casi verdi da Bench\\report\\last.json.)');
  console.log('');
  if (perMode['solo piano a mano'].length > 0) {
    console.log('SOLO UN PIANO A MANO — un appunto di come si e\' misurato, non una prova:');
    for (const o of perMode['solo piano a mano']) console.log('    ' + o);
    console.log('');
  }
}

if (perMode['NIENTE'].length === 0) {
  console.log('Nessuna operazione orfana: ogni voce del catalogo ha qualcosa che la esercita.');
} else {
  console.log('ORFANE — a catalogo, e nessun giro le tocca:');
  for (const o of perMode['NIENTE']) console.log('    ' + o);
  console.log('');
  console.log('Per ognuna: o un caso dichiarato in Bench\\cases\\, oppure Provata = false,');
  console.log('che la toglie dal catalogo come gia\' succede a center_marks.');
}
