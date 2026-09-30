// =============================================================================
//  domande-senza-risposta.js - cosa e' stato chiesto al ponte e non c'era
// =============================================================================
//
//  PERCHE' ESISTE (voce Z-G7)
//
//  Le cinque operazioni del catalogo senza nessun caso di banco --
//  `ripeti_circolare`, `cordone_saldatura`, `fazzoletto`, `taglia_membri`,
//  `tappo_estremita` -- le abbiamo scelte NOI. Quali servano davvero non lo sa
//  nessuno, e chiederlo alle persone non lo fa sapere: chi usa il ponte non ha
//  l'elenco di cio' che non c'e'.
//
//  Il ponte scrive una riga ogni volta che gli chiedono un nome che non
//  esiste (`Strumenti/RegistroDelleDomande.cs`). Questo attrezzo la legge.
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
//      node Controlli/domande-senza-risposta.js
//      node Controlli/domande-senza-risposta.js <file.jsonl>
//      node Controlli/domande-senza-risposta.js --righe    tutte, in ordine di tempo
// =============================================================================
const fs = require('fs');
const path = require('path');

const radice = path.join(__dirname, '..');
const NOME = 'domande-senza-risposta.jsonl';

const perRighe = process.argv.includes('--righe');
const dato = process.argv.slice(2).find(a => !a.startsWith('--'));

// Gli stessi posti in cui il ponte lo scriverebbe: la variabile, poi accanto
// all'eseguibile. Se un domani cambiasse l'ordine la' e non qui, questo
// attrezzo leggerebbe un file vecchio senza dirlo -- quindi si stampa QUALE.
function dove() {
  if (dato) return dato;
  const dalla = process.env.RISCONTRO_REGISTRO;
  if (dalla !== undefined) return dalla === '' ? null : dalla;
  return path.join(radice, 'bin', NOME);
}

const file = dove();

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

const righe = fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(r => r.trim());
const voci = [];
let illeggibili = 0;

for (const r of righe) {
  try { voci.push(JSON.parse(r)); }
  catch { illeggibili++; }
}

// EXPECTED KEYS (W3.1-bis.2, item 5). Every record is written by
// `Strumenti/RegistroDelleDomande.cs` with `quando`, `tipo` and `nome`
// (`simili` only when there are neighbours). A record without one of them is
// a producer that renamed its keys: read with a fallback, every question
// would count as "?" with an empty name, and the report would look like one.
const withoutKeys = voci.filter(v => v === null || typeof v !== 'object' || Array.isArray(v) ||
  ['quando', 'tipo', 'nome'].some(k => typeof v[k] !== 'string') ||
  (v.simili !== undefined && !Array.isArray(v.simili)));
if (withoutKeys.length) {
  console.log('EXPECTED KEYS MISSING: ' + withoutKeys.length + ' of ' + voci.length +
              ' records lack "quando", "tipo" or "nome" (strings). The first:');
  console.log('    ' + JSON.stringify(withoutKeys[0]).slice(0, 200));
  console.log('If the bridge renamed them, this reader is renamed in the same commit.');
  process.exit(1);
}

if (perRighe) {
  for (const v of voci) {
    console.log('  ' + (v.quando || '?') + '  ' + (v.tipo || '?') + '  ' +
                JSON.stringify(v.nome) +
                (v.simili ? '   vicini: ' + v.simili.join(', ') : ''));
  }
  console.log('');
}

const conti = new Map();
for (const v of voci) {
  const chiave = (v.tipo || '?') + '\u0000' + (v.nome || '');
  let c = conti.get(chiave);
  if (!c) {
    c = { tipo: v.tipo || '?', nome: v.nome || '', quante: 0,
          prima: v.quando, ultima: v.quando, vicini: new Set() };
    conti.set(chiave, c);
  }
  c.quante++;
  if (v.quando) {
    if (!c.prima || v.quando < c.prima) c.prima = v.quando;
    if (!c.ultima || v.quando > c.ultima) c.ultima = v.quando;
  }
  for (const s of (v.simili || [])) c.vicini.add(s);
}

const tutte = [...conti.values()].sort((a, b) => b.quante - a.quante ||
                                                 a.nome.localeCompare(b.nome));

console.log('domande registrate:        ' + voci.length);
console.log('nomi distinti:             ' + tutte.length);
if (illeggibili) console.log('righe illeggibili:         ' + illeggibili);
console.log('');

if (tutte.length === 0) {
  console.log('Nessuno ha chiesto niente che non ci fosse.');
  process.exit(0);
}

function stampa(titolo, elenco, coda) {
  if (elenco.length === 0) return;
  console.log(titolo);
  for (const c of elenco) {
    const v = c.vicini.size ? '   vicini: ' + [...c.vicini].join(', ') : '';
    console.log('  ' + String(c.quante).padStart(4) + '  [' + c.tipo + '] ' +
                c.nome + v);
    console.log('        dal ' + (c.prima || '?') + ' al ' + (c.ultima || '?'));
  }
  console.log('');
  console.log('  ' + coda);
  console.log('');
}

stampa('DA COSTRUIRE — nessun nome vicino nel catalogo:',
       tutte.filter(c => c.vicini.size === 0),
       'Il catalogo non aveva niente da proporre: chi ha chiesto voleva una ' +
       'cosa che non c\'e\'.');

stampa('FORSE SOLO UN SINONIMO — il catalogo aveva qualcosa di vicino:',
       tutte.filter(c => c.vicini.size > 0),
       'Se il nome vicino e\' davvero la stessa cosa, costa una riga di ' +
       'sinonimo invece di un\'operazione nuova.');

console.log('Un nome chiesto da PIU\' modelli diversi vale piu\' dello stesso');
console.log('nome chiesto dieci volte da uno solo, e questo file non lo');
console.log('distingue: non scrive chi ha chiesto, e non deve. Lo si distingue');
console.log('guardando le date -- sessioni diverse, giorni diversi.');
