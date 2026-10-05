// =============================================================================
//  questions-without-answer.js - cosa e' stato chiesto al ponte e non c'era
// =============================================================================
//
//  PERCHE' ESISTE (voce Z-G7)
//
//  Le cinque operazioni del catalogo senza nessun caso di banco --
//  `circular_pattern`, `weld_bead`, `gusset`, `trim_members`,
//  `end_cap` -- le abbiamo scelte NOI. Quali servano davvero non lo sa
//  nessuno, e chiederlo alle persone non lo fa sapere: chi usa il ponte non ha
//  l'elenco di cio' che non c'e'.
//
//  Il ponte scrive una riga ogni volta che gli chiedono un nome che non
//  esiste (`Tools/QuestionRegistry.cs`). Questo attrezzo la legge.
//
//  ── LA DISTINZIONE CHE VALE, E NON E' IL CONTEGGIO ──────────────────────────
//
//  Un nome chiesto dieci volte **con un nome vicino nel catalogo** e' un
//  sinonimo da aggiungere: l'operazione c'e' gia', si chiama in un altro modo,
//  e costa una riga.
//
//  Un nome chiesto dieci volte **senza niente di vicino** e' un'operazione da
//  costruire. Sono due lavori diversi e un conteggio solo li mescolerebbe, per
//  questo l'elenco e' diviso in due.
//
//  ── QUESTO NON E' UNA GUARDIA ───────────────────────────────────────────────
//
//  Esce sempre 0. Non c'e' niente da sbagliare in un registro: se e' vuoto
//  vuol dire che nessuno ha chiesto niente di sconosciuto, e non e' un guaio.
//  Non va nel gancio pre-push.
//
//  USO
//      node Checks/questions-without-answer.js
//      node Checks/questions-without-answer.js <file.jsonl>
//      node Checks/questions-without-answer.js --righe    tutte, in ordine di tempo
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const NAME = 'domande-senza-risposta.jsonl';

const perLines = process.argv.includes('--righe');
const given = process.argv.slice(2).find(a => !a.startsWith('--'));

// Gli stessi posti in cui il ponte lo scriverebbe: la variabile, poi accanto
// all'eseguibile. Se un domani cambiasse l'ordine la' e non qui, questo
// attrezzo leggerebbe un file vecchio senza dirlo -- quindi si stampa QUALE.
function location() {
  if (given) return given;
  const fromEnv = process.env.RISCONTRO_REGISTRO;
  if (fromEnv !== undefined) return fromEnv === '' ? null : fromEnv;
  return path.join(root, 'bin', NAME);
}

const file = location();

if (file === null) {
  console.log('RISCONTRO_REGISTRO e\' impostata a stringa vuota: il ponte non');
  console.log('registra niente, e non c\'e' + '\' niente da leggere.');
  process.exit(0);
}

console.log('registro: ' + file);
console.log('');

if (!fs.existsSync(file)) {
  console.log('Il file non c\'e\' ancora.');
  console.log('');
  console.log('Vuol dire una di due cose, e si distinguono guardando se il ponte');
  console.log('e\' mai stato usato da qui: o nessuno ha chiesto niente che non');
  console.log('esistesse, oppure il ponte non riesce a scrivere dove crede (e in');
  console.log('quel caso tace apposta -- e\' la regola 2 del registro).');
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

console.log('domande registrate:        ' + entries.length);
console.log('nomi distinti:             ' + all.length);
if (unreadable) console.log('righe illeggibili:         ' + unreadable);
console.log('');

if (all.length === 0) {
  console.log('Nessuno ha chiesto niente che non ci fosse.');
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

print('DA COSTRUIRE — nessun nome vicino nel catalogo:',
       all.filter(c => c.vicini.size === 0),
       'Il catalogo non aveva niente da proporre: chi ha chiesto voleva una ' +
       'cosa che non c\'e\'.');

print('FORSE SOLO UN SINONIMO — il catalogo aveva qualcosa di vicino:',
       all.filter(c => c.vicini.size > 0),
       'Se il nome vicino e\' davvero la stessa cosa, costa una riga di ' +
       'sinonimo invece di un\'operazione nuova.');

console.log('Un nome chiesto da PIU\' modelli diversi vale piu\' dello stesso');
console.log('nome chiesto dieci volte da uno solo, e questo file non lo');
console.log('distingue: non scrive chi ha chiesto, e non deve. Lo si distingue');
console.log('guardando le date -- sessioni diverse, giorni diversi.');
