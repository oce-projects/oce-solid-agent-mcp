// =============================================================================
//  Quante operazioni «provate» hanno davvero qualcosa che le prova  (voce Z-C6)
// =============================================================================
//
//  `Provata = true` nel catalogo vuol dire due cose diverse che nel file si
//  leggono uguali:
//
//    - «qualcuno l'ha misurata una volta»   <- una firma nel changelog
//    - «qualcosa la controlla a ogni giro»  <- un caso che gira
//
//  Questo conta la seconda. Per ogni operazione a catalogo guarda se esiste
//  qualcosa che la esercita, e distingue TRE modi, perche' non valgono uguale:
//
//    caso dichiarato   un file in `Bench\cases\` con dentro delle attese.
//                      Gira a ogni giro, e se il comportamento cambia diventa
//                      rosso. E' l'unico che vale del tutto.
//
//    C# del banco      un gruppo scritto a mano. Gira a ogni giro e asserisce
//                      in C#: vale, ed e' scritto qui perche' non si legga
//                      come un buco.
//
//    piano a mano      un ARRAY in `cases\drawing\`, che lancia `run_plans.js`.
//                      **Non gira in nessun giro e non asserisce niente**: e'
//                      un appunto di come si e' misurato, non una prova.
//
//  PERCHE' NON E' ANCORA UN CONTROLLO DI CONTRATTO. Misurato il 23/9/2026:
//  cinque operazioni a catalogo non sono esercitate da niente. Una guardia che
//  nasce con cinque rossi permanenti e' un elenco che qualcuno silenziera'; si
//  sistemano prima quelle, poi la guardia si accende su un campo pulito.
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

const RADICE = path.resolve(__dirname, '..');
function optionValue(name, fallback) {
  const i = process.argv.indexOf(name);
  return i >= 0 && i + 1 < process.argv.length ? path.resolve(process.argv[i + 1]) : fallback;
}
const CATALOGO = optionValue('--catalogue', path.join(RADICE, 'Tools', 'catalog.json'));
const CASI = optionValue('--cases', path.join(RADICE, 'Bench', 'cases'));
const BANCO = optionValue('--bench', path.join(RADICE, 'Bench'));

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

// ── LE OPERAZIONI CHE UN CLIENT VEDE ─────────────────────────────────────
//
// Si legge il catalogo esportato e non il C#: e' quello che arriva al client,
// ed e' quello di cui `Provata = true` e' la promessa. L'esportatore lascia
// gia' fuori le `Provata = false` e lo dice a voce mentre scrive il file.
let catalogo;
try { catalogo = JSON.parse(fs.readFileSync(CATALOGO, 'utf8').replace(/^\ufeff/, '')); }
catch (e) {
  console.log('cannot read the catalogue ' + CATALOGO + ': ' + e.message);
  console.log('it is written by  .\\bin\\Oce.Solid.Agent.Catalog.exe Tools\\catalog.json');
  process.exit(1);
}
if (expectKey(catalogo, 'operations', 'array', 'catalogue')) {
  if (catalogo.operations.length === 0) missingKeys.push('catalogue: "operations" is empty');
  catalogo.operations.forEach((o, i) => expectKey(o, 'name', 'string', 'catalogue, operation ' + i));
}
stopIfMissing();
const operazioni = catalogo.operations.map((o) => o.name).sort();

// ── CHI LE ESERCITA ──────────────────────────────────────────────────────
const daCasi = new Map();     // operazione -> [casi]
const daRegressione = new Map();   // operazione -> [casi di regressione]
let testoPiani = '';
let quantiCasi = 0;

function scorri(cartella) {
  let voci;
  try { voci = fs.readdirSync(cartella, { withFileTypes: true }); }
  catch (e) { missingKeys.push(cartella + ': cannot read the folder (' + e.message + ')'); return; }
  for (const v of voci) {
    const p = path.join(cartella, v.name);
    // The cases of the aliases (W3.1-bis.5, decision D4) exercise the
    // dictionary of the old names, not an operation, and two of them name
    // their operation in Italian on purpose: they are not counted here.
    // Removed with the aliases in W3.1-bis.11.
    if (v.isDirectory() && v.name === 'aliases' && path.resolve(cartella) === path.resolve(CASI)) continue;
    if (v.isDirectory()) { scorri(p); continue; }
    if (!v.name.endsWith('.json')) continue;
    const where = path.relative(RADICE, p).replace(/\\/g, '/');
    let grezzo, j;
    try { grezzo = fs.readFileSync(p, 'utf8'); j = JSON.parse(grezzo.replace(/^\ufeff/, '')); }
    catch (e) { missingKeys.push(where + ': does not parse (' + e.message + ')'); continue; }

    // UN PIANO E' UN ARRAY, UN CASO E' UN OGGETTO. La differenza e'
    // strutturale, come gia' scritto in `DeclaredCases.cs`: non si indovina
    // dal nome del file.
    if (Array.isArray(j)) {
      // a plan is read as text below, but its steps carry the same key
      j.forEach((step, i) => expectKey(step, 'operation', 'string', where + ', step ' + (i + 1)));
      testoPiani += grezzo;
      continue;
    }

    quantiCasi++;
    expectKey(j, 'name', 'string', where);
    const nome = typeof j.name === 'string' ? j.name : path.basename(v.name, '.json');
    // A case builds its part with `steps` (a normal run) or `build` (a
    // regression case): with neither, its operations would be counted as none.
    const lists = ['steps', 'build'].filter((k) => Object.prototype.hasOwnProperty.call(j, k));
    if (lists.length === 0) missingKeys.push(where + ': neither "steps" nor "build"');
    // I CASI DI REGRESSIONE NON SONO CASI DI UN GIRO NORMALE (25/9/2026). Il
    // loro `costruisci` gira solo con `-Regressione`, e questo script li
    // contava come «caso dichiarato»: il riconteggio del 24/9 diceva 72 su 73,
    // e il controllo di contratto sui casi VERDI dell'ultimo giro ha trovato
    // tre operazioni (hole_wizard, extrude_on_face, fillet_edges) che in
    // un giro normale non toccava nessuno. Adesso stanno in un modo a parte.
    for (const key of lists) {
      if (!expectKey(j, key, 'array', where)) continue;
      const target = key === 'steps' ? daCasi : daRegressione;
      j[key].forEach((passo, i) => {
        if (!expectKey(passo, 'operation', 'string', where + ', ' + key + ' ' + (i + 1))) return;
        const op = passo.operation;
        if (!target.has(op)) target.set(op, []);
        if (target.get(op).indexOf(nome) < 0) target.get(op).push(nome);
      });
    }
  }
}
scorri(CASI);
if (quantiCasi === 0) missingKeys.push(path.relative(RADICE, CASI) + ': no case at all');
stopIfMissing();

let testoCs = '';
try {
  for (const f of fs.readdirSync(BANCO))
    if (f.endsWith('.cs')) testoCs += fs.readFileSync(path.join(BANCO, f), 'utf8');
} catch (e) { missingKeys.push(path.relative(RADICE, BANCO) + ': cannot read the bench sources (' + e.message + ')'); }
// Read from nothing, the bench's own C# would count as touching no operation
// (W3.1-bis.2, item 5: the folder is renamed in W3.1-bis.6).
if (testoCs === '') missingKeys.push(path.relative(RADICE, BANCO) + ': no C# source of the bench');
stopIfMissing();

function comeEProvata(op) {
  const virgolette = '"' + op + '"';
  if (daCasi.has(op)) return { come: 'caso dichiarato', dettaglio: daCasi.get(op).slice(0, 3).join(', ') };
  if (daRegressione.has(op))
    return { come: 'solo regressione', dettaglio: 'gira solo con -Regressione: ' + daRegressione.get(op).slice(0, 3).join(', ') };
  if (testoCs.indexOf(virgolette) >= 0) return { come: 'C# del banco', dettaglio: '' };
  if (testoPiani.indexOf(virgolette) >= 0)
    return { come: 'solo piano a mano', dettaglio: 'non gira in nessun giro, non asserisce niente' };
  return { come: 'NIENTE', dettaglio: 'a catalogo, e nessun giro la tocca' };
}

const perModo = { 'caso dichiarato': [], 'solo regressione': [], 'C# del banco': [], 'solo piano a mano': [], 'NIENTE': [] };
for (const op of operazioni) perModo[comeEProvata(op).come].push(op);

if (!soloOrfane) {
  console.log('catalogo: ' + CATALOGO);
  console.log('operazioni a catalogo (Provata = true): ' + operazioni.length);
  console.log('casi dichiarati: ' + quantiCasi);
  console.log('');
  for (const modo of ['caso dichiarato', 'solo regressione', 'C# del banco', 'solo piano a mano', 'NIENTE']) {
    console.log(String(perModo[modo].length).padStart(4) + '  ' + modo);
  }
  console.log('');
  if (perModo['solo regressione'].length > 0) {
    console.log('SOLO IN REGRESSIONE — girano solo con -Regressione, non in un giro normale:');
    for (const o of perModo['solo regressione']) console.log('    ' + o);
    console.log('');
  }
  console.log('(La guardia vera e\' il controllo di contratto «ogni operazione a catalogo ha un');
  console.log(' caso VERDE nell\'ultimo giro», che legge i casi verdi da Bench\\report\\last.json.)');
  console.log('');
  if (perModo['solo piano a mano'].length > 0) {
    console.log('SOLO UN PIANO A MANO — un appunto di come si e\' misurato, non una prova:');
    for (const o of perModo['solo piano a mano']) console.log('    ' + o);
    console.log('');
  }
}

if (perModo['NIENTE'].length === 0) {
  console.log('Nessuna operazione orfana: ogni voce del catalogo ha qualcosa che la esercita.');
} else {
  console.log('ORFANE — a catalogo, e nessun giro le tocca:');
  for (const o of perModo['NIENTE']) console.log('    ' + o);
  console.log('');
  console.log('Per ognuna: o un caso dichiarato in Bench\\cases\\, oppure Provata = false,');
  console.log('che la toglie dal catalogo come gia\' succede a center_marks.');
}
