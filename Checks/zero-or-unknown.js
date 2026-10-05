// =============================================================================
//  zero-or-unknown.js - are «zero» and «non ho potuto leggere» the same thing?
// =============================================================================
//
//  WHY IT EXISTS (entry Z-C8)
//
//  It has already been paid for once. `IComponent2.GetMates()` returns **`null`
//  when there are zero**, not an empty array, and the bridge answered «non ho potuto
//  leggere» on a component that had zero.
//
//  The same exchange also runs the other way round, and it is the most dangerous:
//  a function that READS from the CAD and answers `0` from its `catch`. Zero is not
//  «I do not know»: zero is **a statement**. «This part has zero bodies»,
//  «zero mm3 were removed from this hole». Whoever reads has no way
//  to notice that in reality the reading failed.
//
//  It is not theory. `SwOperations.cs` carries the two opposite rules two thousand
//  lines apart:
//
//      :2122  «A volume that could not be read does not become a zero:
//              a zero would mean "it removed nothing", which is
//              a statement.»
//      :4837  «The volume of all the solid bodies, in mm3. Zero if it
//              cannot be read.»
//
//  The first is the comment of whoever found the defect; the second is the
//  function that produces it. The call site defended itself, the root did not.
//
//  ── WHAT IT TAKES, AND WHAT IT LEAVES ALONE ─────────────────────────────────
//
//  It takes three forms, all syntactic:
//
//      catch { return 0; }          an error that becomes a number
//      x == null ? 0 : x.Length     a «not there» that becomes a count
//      int n = 0; try { ... }       a MUTE catch that leaves the count
//      catch { } return n;          where it was, that is at zero or halfway
//
//  The third was found by an agent on 25/9/2026 while classifying the first two:
//  `CountFaces`, `CountDimensions`, `CountViews` had the same defect with
//  another syntax, and the probe did not see them.
//
//  It leaves alone:
//
//   1. the INPUT conversions (`Convert.ToInt32`, `Convert.ToDouble`):
//      there one is reading a badly written JSON argument, not the CAD, and zero
//      is a fallback declared elsewhere;
//   2. the points that carry the marker `ZERO-E-ZERO:` with the reason
//      next to it. **The reason is mandatory**: a marker alone would be
//      a way of silencing the probe, and this repository has seen
//      enough of those.
//
//  ── SINCE 25/9/2026 IT IS A HOOK ────────────────────────────────────────────
//
//  Up to library 0.116.0 it always exited 0: repairing them meant changing
//  the signature of library functions and rerunning the bench, and it was work,
//  not a prohibition. With all of them repaired (entry Z-C8), now **it exits 1 if a new
//  one appears** without a marker: it is in the pre-push hook. A finished work list
//  that does not become a guard fills up again.
//
//  USAGE
//      node Checks/zero-or-unknown.js
//      node Checks/zero-or-unknown.js --tutti      also the declared ones
//      node Checks/zero-or-unknown.js --provami    makes it fail on purpose
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const all = process.argv.includes('--tutti');

const FOLDERS = ['Oce.Solid.Agent.Core', 'Tools', 'Bridge', 'Shared', 'Mcp'];

// WHERE THE MARKER IS SEARCHED FOR, and the first draft was wrong.
//
// It counted fourteen lines above the point. The reason however sits in the
// comment of the METHOD, and the comment that triggered this error has
// twenty: the marker was there, written in the right place, and the probe did not
// see it.
//
// **It is the second time in two days.** The guard of entry Z-A1 was born
// with the exact same defect — it counted raw lines and accused a block
// separated by twenty-five lines of comment. *Counting lines punishes whoever
// explains, in a repository in which explaining is the rule.*
//
// Now we go back up to the start of the member's comment (`/// <summary>`)
// instead of counting. The ceiling of 80 lines is not the rule: it is only so as not to
// go back up the whole file if a `<summary>` is not there.
const CAP = 80;

function memberWindow(lines, i) {
  let da = i;
  for (let k = i; k >= 0 && i - k < CAP; k--) {
    da = k;
    if (/\/\/\/\s*<summary>/.test(lines[k])) break;
  }
  return lines.slice(da, i + 1).join('\n');
}

const MARKER = 'ZERO-E-ZERO:';

function sources(dir, inside) {
  const p = path.join(root, dir);
  if (!fs.existsSync(p)) return;
  for (const n of fs.readdirSync(p)) {
    const q = path.join(p, n);
    const s = fs.statSync(q);
    if (s.isDirectory()) { sources(path.join(dir, n), inside); continue; }
    if (n.endsWith('.cs')) inside.push(path.join(dir, n));
  }
}

// The two forms, and the why of each is in the header.
const SHAPES = [
  { nome: 'catch che torna un numero', re: /catch\s*\{\s*return\s+0(\.0)?\s*;/ },
  { nome: 'un «non c\'e\'» che diventa un conteggio', re: /==\s*null\s*\?\s*0\s*:/ },
];

// The input conversions: an argument is being read, not the CAD.
const INPUT = /Convert\.To(Int32|Double|Single|Int64)\s*\(/;

// THE THIRD FORM: an empty `catch { }`, and shortly after `return x;` of a
// variable born at zero in the same member.
const MUTE_CATCH = /catch\s*\{\s*\}/;
function muteCounter(lines, i) {
  let variable = null;
  for (let k = i; k < Math.min(lines.length, i + 9); k++) {
    const m = /^\s*return\s+(\w+)\s*;/.exec(lines[k]);
    if (m) { variable = m[1]; break; }
  }
  if (!variable) return false;
  const born = new RegExp('\\b(int|long|double)\\s+' + variable + '\\s*=\\s*0(\\.0)?\\s*;');
  return born.test(memberWindow(lines, i));
}

function look(lines, fileName) {
  const takenOnes = [];
  for (let i = 0; i < lines.length; i++) {
    const r = lines[i];
    let shape = SHAPES.find(f => f.re.test(r));
    if (!shape && MUTE_CATCH.test(r) && muteCounter(lines, i))
      shape = { nome: 'un catch muto che lascia il conteggio dov\'era' };
    if (!shape) continue;
    if (INPUT.test(r)) continue;

    const window = memberWindow(lines, i);
    const declared = window.indexOf(MARKER) >= 0;
    takenOnes.push({
      dove: fileName + ':' + (i + 1),
      riga: r.trim().slice(0, 96),
      forma: shape.nome,
      dichiarato: declared,
    });
  }
  return takenOnes;
}

function benchRun(list, whatItIsCalled) {
  const takenOnes = [];
  for (const f of list) {
    const text = typeof f === 'string'
      ? fs.readFileSync(path.join(root, f), 'utf8')
      : f.testo;
    const itemName = typeof f === 'string' ? f.replace(/\\/g, '/') : f.nome;
    takenOnes.push(...look(text.split(/\r?\n/), itemName));
  }

  const bareOnes = takenOnes.filter(p => !p.dichiarato);
  const called = takenOnes.filter(p => p.dichiarato);

  console.log('');
  console.log(whatItIsCalled + ':');
  console.log('  punti che rispondono zero su una strada di errore: ' + takenOnes.length);
  console.log('  di cui DICHIARATI (zero e zero sono la stessa cosa li\'): ' + called.length);
  if (all) for (const d of called) console.log('    ok  ' + d.dove + '  ' + d.riga);

  if (bareOnes.length === 0) {
    console.log('  Nessun punto scambia «zero» per «non lo so» senza dirlo.');
    return 0;
  }
  console.log('  DA GUARDARE: ' + bareOnes.length);
  for (const n of bareOnes) console.log('    !  ' + n.dove + '  ' + n.riga);
  return bareOnes.length;
}

// ── the check, before everything else ────────────────────────────────────────
if (process.argv.includes('--provami')) {
  console.log('=== PROVA DEL CONTROLLO: lo stesso punto, nudo e dichiarato ===');
  const bare = [
    'private static double VolumeMm3(IModelDoc2 modello)',
    '{',
    '    try { return Leggi(modello); }',
    '    catch { return 0; }',
    '}',
  ].join('\n');
  // THE DECLARED ONE IS FAR ON PURPOSE: twenty lines of comment between the
  // marker and the point. It is the case on which this probe went wrong the
  // first time, and it is the one that must stay green.
  const said = ['/// <summary>',
    '/// ZERO-E-ZERO: qui zero e "non lo so" coincidono davvero, perche\'',
    '/// `GetMates` torna null quando sono zero -- misurato il 17/9/2026.'];
  for (let k = 0; k < 20; k++) said.push('/// ' + k + ' riga di spiegazione.');
  said.push('/// </summary>',
    'private static int Quanti(IComponent2 c)',
    '{',
    '    try { return Leggi(c); }',
    '    catch { return 0; }',
    '}');
  const saidText = said.join('\n');
  const mute = [
    'private static int CountFaces(IModelDoc2 m)',
    '{',
    '    int n = 0;',
    '    try { n = Leggi(m); }',
    '    catch { }',
    '    return n;',
    '}',
  ].join('\n');
  const inputOnly = [
    'private static int Intero(object v)',
    '{',
    '    try { return Convert.ToInt32(v); } catch { return 0; }',
    '}',
  ].join('\n');

  const a = benchRun([{ nome: 'FINTO-nudo', testo: bare }], 'il punto NUDO');
  const b = benchRun([{ nome: 'FINTO-detto', testo: saidText }], 'lo stesso punto DICHIARATO, a venti righe di distanza');
  const c = benchRun([{ nome: 'FINTO-ingresso', testo: inputOnly }], 'una conversione di INGRESSO');
  const e = benchRun([{ nome: 'FINTO-muto', testo: mute }], 'il catch MUTO (la terza forma)');
  console.log('');
  if (a === 1 && b === 0 && c === 0 && e === 1) {
    console.log('IL CONTROLLO prende il punto nudo, TACE su quello dichiarato,');
    console.log('e non confonde una conversione di argomento con una lettura. Provato.');
  } else {
    console.log('IL CONTROLLO NON SI COMPORTA COME DICE: nudo=' + a +
                ' (atteso 1), dichiarato=' + b + ' (atteso 0), ingresso=' + c + ' (atteso 0)' +
                ', muto=' + e + ' (atteso 1)');
    process.exitCode = 1;
  }
  console.log('');
}

const files = [];
for (const c of FOLDERS) sources(c, files);
console.log('sorgenti guardati: ' + files.length);
const discovered = benchRun(files, 'nel repository');
if (discovered > 0) process.exitCode = 1;

console.log('');
console.log('Zero non e\' «non lo so»: zero e\' un\'AFFERMAZIONE — «questo pezzo ha');
console.log('zero corpi», «da questo foro sono stati tolti zero mm3». Chi legge non');
console.log('ha modo di accorgersi che la lettura e\' fallita.');
console.log('');
console.log('Si ripara con -1 (o null) per «non lo so», oppure col marcatore');
console.log('ZERO-E-ZERO: e la ragione accanto quando lo zero e\' vero. Esce 1 se');
console.log('ne resta uno senza: dal 25/9/2026 e\' nel gancio pre-push (voce Z-C8).');
