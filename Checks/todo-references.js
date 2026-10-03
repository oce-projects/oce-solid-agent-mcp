// =============================================================================
//  rimandi-del-todo.js - «vedi la voce K14» punta a una voce che esiste?
// =============================================================================
//
//  PERCHE' ESISTE
//
//  Il TODO rimanda a se stesso in continuazione: «voce K17», «vedi Z-C4»,
//  «come la GT0». Sono le cuciture del file: e' cosi' che una voce dice perche'
//  esiste senza ripetere quello che sta gia' scritto altrove.
//
//  **Un rimando rotto e' peggio di nessun rimando**: chi legge va a cercare,
//  non trova, e non sa se la voce e' sparita, se e' stata rinominata, o se ha
//  cercato male. E' la stessa malattia del percorso che non c'e' piu'
//  (`percorsi-del-todo.js`) e della firma che non esiste
//  (`firme-del-todo.ps1`), applicata ai rimandi interni.
//
//  Il gemello di questo controllo c'e' gia' ed e' nelle prove di contratto:
//  «nel TODO due voci non si chiamano allo stesso modo». Quello impedisce che
//  un nome ne indichi due; questo, che un nome non ne indichi nessuna.
//
//  USO
//      node Controlli/rimandi-del-todo.js
//      node Controlli/rimandi-del-todo.js --tutti    anche quelli che vanno
// =============================================================================
const fs = require('fs');
const path = require('path');

const radice = path.join(__dirname, '..');
const tutti = process.argv.includes('--tutti');
// IL FILE SI PUO' PASSARE, come in `conta-todo.js`: serve a far scattare
// questa sonda su una copia rotta, che e' l'unico modo di crederle.
const { trovaIlTodo } = require('./dov-e-il-todo.js');
const fileTodo = trovaIlTodo(radice, process.argv);
if (!fileTodo) process.exit(0);
const righe = fs.readFileSync(fileTodo, 'utf8').split(/\r?\n/);

// ── I NOMI CHE ESISTONO ────────────────────────────────────────────────
//
// Si leggono dalle intestazioni, che sono l'unico posto dove un nome di voce
// e' definito. `#### [x] K17 — ...` oppure `### [ ] W3.1 — ...`.
// E LE SOTTO-VOCI, a cinque cancelletti (28/9/2026): `##### [ ] U1.1 — ...`,
// `##### [ ] Z-C21.3 — ...`. Le voci grandi delle Sezioni T e U, di W3.1-bis e
// di Z-C21 si dividono cosi', e un rimando a una sotto-voce che esiste veniva
// dato per rotto.
const esistono = new Set();
for (const r of righe) {
  const m = r.match(/^#{2,5} \[[ x~]\] ([A-Z][A-Za-z0-9.\-]*)\b/);
  if (m) esistono.add(m[1]);
  // anche le sezioni senza casella: `## W2 — ...`, `### Z-C — ...`
  const s = r.match(/^#{2,4} ([A-Z][A-Za-z0-9.\-]*)\s+—/);
  if (s) esistono.add(s[1]);
}

// ── I RIMANDI ──────────────────────────────────────────────────────────
//
// Solo le forme esplicite: «voce X», «voci X e Y», «vedi X». Cercare ogni
// sigla che assomigli a un nome raccoglierebbe meta' della prosa -- e un
// elenco che pesca i falsi non si legge.
const NOME = '([A-Z][A-Za-z0-9]*(?:[.\\-][A-Za-z0-9]+)*)';
const FORME = [
  new RegExp('\\bvoc[ei]\\s+' + NOME, 'g'),
  new RegExp('\\bvedi\\s+(?:anche\\s+)?(?:la\\s+voce\\s+)?' + NOME, 'g'),
];

const rotti = new Map();
let ok = 0;
for (let i = 0; i < righe.length; i++) {
  for (const f of FORME) {
    f.lastIndex = 0;
    let m;
    while ((m = f.exec(righe[i])) !== null) {
      const nome = m[1];
      // Parole italiane che cominciano per maiuscola e finiscono nella rete.
      if (/^(Il|La|Le|Lo|Un|Una|Questa|Questo|Quella|Quello|Non|Che|Come|Perche|E|Ma|Si|No|Sono|Ha|Era|Anche|Tutte|Tutti|Due|Tre|Adesso|Oggi|Nel|Nella|Dal|Della|Con|Per|Senza|Dove|Quando|Cosa|Chi|Piu|Meno|Prima|Dopo|Sopra|Sotto)$/i.test(nome)) continue;
      if (!/[0-9]/.test(nome)) continue;   // un nome di voce ha sempre un numero
      if (esistono.has(nome)) { ok++; if (tutti) console.log('  ok   ' + nome); continue; }
      if (!rotti.has(nome)) rotti.set(nome, i + 1);
    }
  }
}

console.log('nomi di voce definiti:  ' + esistono.size);
console.log('rimandi verificati:     ' + ok);
console.log('');
if (rotti.size === 0) {
  console.log('Tutti i rimandi del TODO puntano a una voce che esiste.');
} else {
  console.log('RIMANDI A VOCI CHE NON ESISTONO: ' + rotti.size);
  for (const [nome, riga] of rotti) console.log('  !  ' + nome + '   (riga ' + riga + ')');
  console.log('');
  console.log('Puo\' essere una voce rinominata, una mai scritta, o una sigla che');
  console.log('assomiglia a un nome. E\' una cosa da guardare, non una colpa.');
  // ESCE 1: vedi il gemello `percorsi-del-todo.js`.
  process.exitCode = 1;
}
