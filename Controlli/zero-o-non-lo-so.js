// =============================================================================
//  zero-o-non-lo-so.js - «zero» e «non ho potuto leggere» sono la stessa cosa?
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
//  Non e' teoria. `OperazioniSw.cs` porta le due regole opposte a duemila
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
//  Prende due forme, tutt'e due sintattiche e quindi certe:
//
//      catch { return 0; }          un errore che diventa un numero
//      x == null ? 0 : x.Length     un «non c'e'» che diventa un conteggio
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
//  ── QUESTO NON E' UN GANCIO ─────────────────────────────────────────────────
//
//  Esce sempre 0. Ripararli vuol dire cambiare la firma di funzioni della
//  libreria e rilanciare il banco: e' lavoro, non un divieto. Questo attrezzo
//  dice **quanti sono e dove**, che e' cio' che la voce Z-C8 chiama «misura».
//
//  USO
//      node Controlli/zero-o-non-lo-so.js
//      node Controlli/zero-o-non-lo-so.js --tutti      anche i dichiarati
//      node Controlli/zero-o-non-lo-so.js --provami    lo fa sbagliare apposta
// =============================================================================
const fs = require('fs');
const path = require('path');

const radice = path.join(__dirname, '..');
const tutti = process.argv.includes('--tutti');

const CARTELLE = ['Riscontro.Disegno', 'Strumenti', 'Ponte', 'Condiviso', 'Mcp'];

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
const TETTO = 80;

function finestraDelMembro(righe, i) {
  let da = i;
  for (let k = i; k >= 0 && i - k < TETTO; k--) {
    da = k;
    if (/\/\/\/\s*<summary>/.test(righe[k])) break;
  }
  return righe.slice(da, i + 1).join('\n');
}

const MARCATORE = 'ZERO-E-ZERO:';

function sorgenti(dir, dentro) {
  const p = path.join(radice, dir);
  if (!fs.existsSync(p)) return;
  for (const n of fs.readdirSync(p)) {
    const q = path.join(p, n);
    const s = fs.statSync(q);
    if (s.isDirectory()) { sorgenti(path.join(dir, n), dentro); continue; }
    if (n.endsWith('.cs')) dentro.push(path.join(dir, n));
  }
}

// Le due forme, e il perche' di ciascuna sta nel cappello.
const FORME = [
  { nome: 'catch che torna un numero', re: /catch\s*\{\s*return\s+0(\.0)?\s*;/ },
  { nome: 'un «non c\'e\'» che diventa un conteggio', re: /==\s*null\s*\?\s*0\s*:/ },
];

// Le conversioni di ingresso: si sta leggendo un argomento, non il CAD.
const INGRESSO = /Convert\.To(Int32|Double|Single|Int64)\s*\(/;

function guarda(righe, nomeFile) {
  const presi = [];
  for (let i = 0; i < righe.length; i++) {
    const r = righe[i];
    const forma = FORME.find(f => f.re.test(r));
    if (!forma) continue;
    if (INGRESSO.test(r)) continue;

    const finestra = finestraDelMembro(righe, i);
    const dichiarato = finestra.indexOf(MARCATORE) >= 0;
    presi.push({
      dove: nomeFile + ':' + (i + 1),
      riga: r.trim().slice(0, 96),
      forma: forma.nome,
      dichiarato,
    });
  }
  return presi;
}

function giro(elenco, comeSiChiama) {
  const presi = [];
  for (const f of elenco) {
    const testo = typeof f === 'string'
      ? fs.readFileSync(path.join(radice, f), 'utf8')
      : f.testo;
    const nome = typeof f === 'string' ? f.replace(/\\/g, '/') : f.nome;
    presi.push(...guarda(testo.split(/\r?\n/), nome));
  }

  const nudi = presi.filter(p => !p.dichiarato);
  const detti = presi.filter(p => p.dichiarato);

  console.log('');
  console.log(comeSiChiama + ':');
  console.log('  punti che rispondono zero su una strada di errore: ' + presi.length);
  console.log('  di cui DICHIARATI (zero e zero sono la stessa cosa li\'): ' + detti.length);
  if (tutti) for (const d of detti) console.log('    ok  ' + d.dove + '  ' + d.riga);

  if (nudi.length === 0) {
    console.log('  Nessun punto scambia «zero» per «non lo so» senza dirlo.');
    return 0;
  }
  console.log('  DA GUARDARE: ' + nudi.length);
  for (const n of nudi) console.log('    !  ' + n.dove + '  ' + n.riga);
  return nudi.length;
}

// ── la prova, prima di tutto il resto ────────────────────────────────────────
if (process.argv.includes('--provami')) {
  console.log('=== PROVA DEL CONTROLLO: lo stesso punto, nudo e dichiarato ===');
  const nudo = [
    'private static double VolumeMm3(IModelDoc2 modello)',
    '{',
    '    try { return Leggi(modello); }',
    '    catch { return 0; }',
    '}',
  ].join('\n');
  // IL DICHIARATO E' LONTANO APPOSTA: venti righe di commento fra il
  // marcatore e il punto. E' il caso su cui questa sonda ha sbagliato la
  // prima volta, ed e' quello che deve restare verde.
  const detto = ['/// <summary>',
    '/// ZERO-E-ZERO: qui zero e "non lo so" coincidono davvero, perche\'',
    '/// `GetMates` torna null quando sono zero -- misurato il 17/9/2026.'];
  for (let k = 0; k < 20; k++) detto.push('/// ' + k + ' riga di spiegazione.');
  detto.push('/// </summary>',
    'private static int Quanti(IComponent2 c)',
    '{',
    '    try { return Leggi(c); }',
    '    catch { return 0; }',
    '}');
  const dettoTesto = detto.join('\n');
  const soloIngresso = [
    'private static int Intero(object v)',
    '{',
    '    try { return Convert.ToInt32(v); } catch { return 0; }',
    '}',
  ].join('\n');

  const a = giro([{ nome: 'FINTO-nudo', testo: nudo }], 'il punto NUDO');
  const b = giro([{ nome: 'FINTO-detto', testo: dettoTesto }], 'lo stesso punto DICHIARATO, a venti righe di distanza');
  const c = giro([{ nome: 'FINTO-ingresso', testo: soloIngresso }], 'una conversione di INGRESSO');
  console.log('');
  if (a === 1 && b === 0 && c === 0) {
    console.log('IL CONTROLLO prende il punto nudo, TACE su quello dichiarato,');
    console.log('e non confonde una conversione di argomento con una lettura. Provato.');
  } else {
    console.log('IL CONTROLLO NON SI COMPORTA COME DICE: nudo=' + a +
                ' (atteso 1), dichiarato=' + b + ' (atteso 0), ingresso=' + c + ' (atteso 0)');
    process.exitCode = 1;
  }
  console.log('');
}

const files = [];
for (const c of CARTELLE) sorgenti(c, files);
console.log('sorgenti guardati: ' + files.length);
giro(files, 'nel repository');

console.log('');
console.log('Zero non e\' «non lo so»: zero e\' un\'AFFERMAZIONE — «questo pezzo ha');
console.log('zero corpi», «da questo foro sono stati tolti zero mm3». Chi legge non');
console.log('ha modo di accorgersi che la lettura e\' fallita.');
console.log('');
console.log('Ripararli cambia la firma di funzioni della libreria e vuole un giro di');
console.log('banco: per questo qui non c\'e\' nessun exit 1. Questo attrezzo dice');
console.log('quanti sono e dove, che e\' cio\' che la voce Z-C8 chiama «misura».');
