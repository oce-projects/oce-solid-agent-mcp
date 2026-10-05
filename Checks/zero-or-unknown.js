// =============================================================================
//  zero-or-unknown.js - «zero» e «non ho potuto leggere» sono la stessa cosa?
// =============================================================================
//
//  PERCHE' ESISTE (voce Z-C8)
//
//  E' gia' stato pagato una volta. `IComponent2.GetMates()` torna **`null`
//  quando sono zero**, non un array vuoto, e il ponte rispose «non ho potuto
//  leggere» su un componente che ne aveva zero.
//
//  Lo stesso scambio gira anche nell'altro verso, ed e' il piu' pericoloso:
//  una funzione che LEGGE dal CAD e risponde `0` dal suo `catch`. Zero non e'
//  «non lo so»: zero e' **un'affermazione**. «Questo pezzo ha zero corpi»,
//  «da questo foro sono stati tolti zero mm3». Chi legge non ha modo di
//  accorgersi che in realta' la lettura e' fallita.
//
//  Non e' teoria. `SwOperations.cs` porta le due regole opposte a duemila
//  righe di distanza:
//
//      :2122  «Un volume che non si e' potuto leggere non diventa uno zero:
//              uno zero vorrebbe dire "non ha tolto niente", che e'
//              un'affermazione.»
//      :4837  «Il volume di tutti i corpi solidi, in mm3. Zero se non si
//              legge.»
//
//  La prima e' il commento di chi ha trovato il difetto; la seconda e' la
//  funzione che lo produce. Il punto di chiamata si e' difeso, la radice no.
//
//  ── COSA PRENDE, E COSA LASCIA STARE ────────────────────────────────────────
//
//  Prende tre forme, tutte sintattiche:
//
//      catch { return 0; }          un errore che diventa un numero
//      x == null ? 0 : x.Length     un «non c'e'» che diventa un conteggio
//      int n = 0; try { ... }       un catch MUTO che lascia il conteggio
//      catch { } return n;          dov'era, cioe' a zero o a meta'
//
//  La terza l'ha trovata un agente il 25/9/2026 classificando le prime due:
//  `CountFaces`, `CountDimensions`, `CountViews` avevano lo stesso difetto con
//  un'altra sintassi, e la sonda non li vedeva.
//
//  Lascia stare:
//
//   1. le conversioni di INGRESSO (`Convert.ToInt32`, `Convert.ToDouble`):
//      li' si sta leggendo un argomento JSON scritto male, non il CAD, e zero
//      e' un ripiego dichiarato altrove;
//   2. i punti che portano il marcatore `ZERO-E-ZERO:` con la ragione
//      accanto. **La ragione e' obbligatoria**: un marcatore da solo sarebbe
//      un modo di zittire la sonda, e questo repository ne ha gia' viste
//      abbastanza.
//
//  ── DAL 25/9/2026 E' UN GANCIO ──────────────────────────────────────────────
//
//  Fino alla libreria 0.116.0 usciva sempre 0: ripararli voleva dire cambiare
//  la firma di funzioni della libreria e rilanciare il banco, ed era lavoro,
//  non un divieto. Riparati tutti (voce Z-C8), adesso **esce 1 se ne compare
//  uno nuovo** senza marcatore: e' nel gancio pre-push. Un elenco di lavoro
//  finito che non diventa una guardia torna a riempirsi.
//
//  USO
//      node Checks/zero-or-unknown.js
//      node Checks/zero-or-unknown.js --tutti      anche i dichiarati
//      node Checks/zero-or-unknown.js --provami    lo fa sbagliare apposta
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const all = process.argv.includes('--tutti');

const FOLDERS = ['Oce.Solid.Agent.Core', 'Tools', 'Bridge', 'Shared', 'Mcp'];

// DOVE SI CERCA IL MARCATORE, e la prima scrittura era sbagliata.
//
// Contava quattordici righe sopra il punto. La ragione pero' sta nel
// commento del METODO, e il commento che ha fatto scattare questo errore ne
// ha venti: il marcatore c'era, scritto nel posto giusto, e la sonda non lo
// vedeva.
//
// **E' la seconda volta in due giorni.** La guardia della voce Z-A1 e' nata
// con lo stesso difetto esatto — contava righe grezze e accusava un blocco
// separato da venticinque righe di commento. *Contare righe punisce chi
// spiega, in un repository in cui spiegare e' la regola.*
//
// Adesso si risale fino all'inizio del commento del membro (`/// <summary>`)
// invece di contare. Il tetto di 80 righe non e' la regola: e' solo per non
// risalire tutto il file se un `<summary>` non c'e'.
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

// Le due forme, e il perche' di ciascuna sta nel cappello.
const SHAPES = [
  { nome: 'catch che torna un numero', re: /catch\s*\{\s*return\s+0(\.0)?\s*;/ },
  { nome: 'un «non c\'e\'» che diventa un conteggio', re: /==\s*null\s*\?\s*0\s*:/ },
];

// Le conversioni di ingresso: si sta leggendo un argomento, non il CAD.
const INPUT = /Convert\.To(Int32|Double|Single|Int64)\s*\(/;

// LA TERZA FORMA: un `catch { }` vuoto, e poco dopo `return x;` di una
// variabile nata a zero nello stesso membro.
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

// ── la prova, prima di tutto il resto ────────────────────────────────────────
if (process.argv.includes('--provami')) {
  console.log('=== PROVA DEL CONTROLLO: lo stesso punto, nudo e dichiarato ===');
  const bare = [
    'private static double VolumeMm3(IModelDoc2 modello)',
    '{',
    '    try { return Leggi(modello); }',
    '    catch { return 0; }',
    '}',
  ].join('\n');
  // IL DICHIARATO E' LONTANO APPOSTA: venti righe di commento fra il
  // marcatore e il punto. E' il caso su cui questa sonda ha sbagliato la
  // prima volta, ed e' quello che deve restare verde.
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
