// =============================================================================
//  todo-lines.js - i `file.cs:NNN` citati puntano ancora a qualcosa?
// =============================================================================
//
//  PERCHE' ESISTE
//
//  Il TODO cita punti precisi del codice — `SwOperations.cs:6186`,
//  `McpTools.cs:1100`, `PlanExecutor.cs:512` — ed e' il modo giusto di
//  scrivere: un'affermazione con l'indirizzo si puo' controllare.
//
//  Ma **i numeri di riga scivolano a ogni modifica**, e nessuno se ne accorge.
//  Il 23/9/2026 la voce Z-A7 mandava a `SwOperations.cs:6186` per una chiamata
//  che sta alla 6198: dodici righe, cioe' abbastanza per finire in un altro
//  metodo. Chi ci va, non trova, e non sa se la voce e' vecchia o se ha letto
//  male.
//
//  ── COSA PUO' DIRE, E COSA NO ───────────────────────────────────────────────
//
//  Puo' dire con certezza che una riga **non esiste**: il file e' piu' corto.
//  Quella e' una citazione morta, e va corretta.
//
//  NON puo' dire se la riga giusta e' un'altra: per saperlo bisognerebbe
//  capire di cosa parla la voce. Un controllo che ci provasse indovinerebbe, e
//  qui indovinare e' proibito.
//
//  **Quindi questo attrezzo prende poco, e quel poco e' certo.** Meglio di un
//  attrezzo che prende molto e ha ragione a meta'.
//
//  ── SECONDA PASSATA: I RIMANDI DENTRO IL TODO STESSO ───────────────────────
//
//  Il 23/9/2026 la voce Z-C14 mandava a `TODO.md:7497-7530` e a
//  `TODO.md:~7753` per parlare di `GetRemainingDOFs`. Tutt'e due le righe
//  esistono -- il file ne ha sedicimila -- e tutt'e due parlano d'altro: la
//  prima di `IFace2.Normal` e dei sottosquadri, la seconda dell'origine di
//  un'estrusione. Gli indirizzi veri erano trecento e quattrocento righe piu'
//  giu'. **La prima passata non poteva vederle**, e lo dichiara: guarda solo
//  se la riga esiste.
//
//  Il controllo NON indovina di cosa parla la voce. Prende **il soggetto che
//  la frase si e' scritta da sola**: gli identificatori fra backtick sulla
//  stessa riga del rimando (`GetRemainingDOFs`, `ReplaceComponents2`). Se
//  NESSUNO di quelli compare nelle righe citate, il rimando e' rotto. E'
//  la stessa tecnica con cui `todo-references.js` ha sciolto `vedi C8`.
//
//  Basta che UNO compaia: pretendere tutti punirebbe una frase che nomina
//  due cose e ne cita una. Se la riga non ha nessun identificatore fra
//  backtick, il rimando **si salta** -- senza soggetto si indovinerebbe.
//
//  USO
//      node Checks/todo-lines.js
//      node Checks/todo-lines.js --tutti    anche quelle che vanno
//      node Checks/todo-lines.js --provami  fa fallire il controllo apposta
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const all = process.argv.includes('--tutti');
const { findTheTodo } = require('./where-is-the-todo.js');
const fileTodo = findTheTodo(root, process.argv);
if (!fileTodo) process.exit(0);
const lines = fs.readFileSync(fileTodo, 'utf8').split(/\r?\n/);

// Le stesse cartelle di `todo-paths.js`: il TODO scrive i nomi da dove
// si lavora.
const BASES = ['', 'Banco', 'Oce.Solid.Agent.Core', 'Strumenti', 'Controlli',
              'Tests', 'Mcp', 'Bridge', 'Oce.Solid.Agent.Core/Catalog'];

// LA CARTELLA SCRITTA NELLA CITAZIONE COMANDA, e la prima scrittura la
// buttava via.
//
// Cercava per NOME BASE fra le cartelle note. Il 24/9/2026 la citazione
// `Bridge/Session.cs:824` e' finita su `Bench/Session.cs` -- che esiste,
// si chiama uguale, e ha 392 righe invece di 832. La sonda ha dichiarato
// morta una citazione giusta, **dentro il gancio pre-push**: avrebbe
// fermato una consegna per un errore suo.
//
// Adesso: se la citazione dice una cartella, si guarda LI'. Il giro per
// nome base resta per le citazioni che la cartella non ce l'hanno, e
// quando trova piu' di un file con lo stesso nome **si astiene** invece di
// scegliere: due file omonimi sono esattamente il caso in cui indovinare
// produce il rosso sbagliato.
function location(cited) {
  // 1. con la cartella: e' un indirizzo, non un suggerimento.
  if (cited.indexOf('/') >= 0) {
    const p = path.join(root, cited);
    return fs.existsSync(p) ? p : null;
  }

  // 2. senza: si cerca, e si conta quanti ne trova.
  const itemName = path.basename(cited);
  const foundItems = [];
  for (const b of BASES) {
    const p = path.join(root, b, itemName);
    if (fs.existsSync(p)) foundItems.push(p);
  }
  if (foundItems.length === 1) return foundItems[0];
  if (foundItems.length > 1) { namesakes.add(itemName); return null; }
  return null;
}

// I nomi che stanno in piu' di una cartella: non si sceglie per loro, e lo
// si dice — se no la prossima citazione senza cartella sbaglia in silenzio.
const namesakes = new Set();

let stateEntry = null;
const citedOnes = new Map();   // "file:riga" -> stato della voce

for (const r of lines) {
  const h = r.match(/^#{2,4} \[([ x~])\]/);
  if (h) stateEntry = h[1];
  else if (/^#{1,4} /.test(r)) stateEntry = null;

  // `NomeFile.cs:123` oppure `Cartella/NomeFile.cs:123`
  for (const m of r.matchAll(/`?([A-Za-z0-9_.\/\\-]+\.(?:cs|js|ps1|json|md)):(\d+)/g)) {
    const key = m[1].replace(/\\/g, '/') + ':' + m[2];
    if (!citedOnes.has(key)) citedOnes.set(key, stateEntry);
  }
}

const death = [];
const notFound = new Set();
let ok = 0, historical = 0;

for (const [c, docState] of citedOnes) {
  const cut = c.lastIndexOf(':');
  const itemName = c.slice(0, cut);
  const n = parseInt(c.slice(cut + 1), 10);
  const p = location(itemName);
  if (!p) { notFound.add(itemName); continue; }
  const howMany = fs.readFileSync(p, 'utf8').split(/\r?\n/).length;
  if (n <= howMany) { ok++; if (all) console.log('  ok   ' + c + '  (il file ha ' + howMany + ' righe)'); continue; }
  if (docState === 'x') { historical++; continue; }   // voce chiusa: era vero allora
  death.push(c + '  — il file ha ' + howMany + ' righe');
}

console.log('citazioni `file:riga`:     ' + citedOnes.size);
console.log('che puntano dentro il file: ' + ok);
console.log('in voci chiuse, non si toccano: ' + historical);
// SI STAMPANO I NOMI, non solo quanti sono. Un «7» non si puo' controllare:
// per sapere se e' giusto bisogna rifare a mano il lavoro della sonda.
if (notFound.size) {
  console.log('file non trovati (altro repo, rinominati, o cartella sbagliata): ' +
              notFound.size);
  for (const n of notFound) console.log('    - ' + n);
}
if (namesakes.size) {
  console.log('nomi che stanno in piu\' cartelle, e senza cartella non si sceglie: ' +
              [...namesakes].join(', '));
  console.log('  (scrivere la cartella nella citazione li rende controllabili)');
}
console.log('');

if (death.length === 0) {
  console.log('Nessuna voce aperta manda a una riga che non esiste.');
} else {
  console.log('VOCI APERTE che mandano OLTRE LA FINE del file: ' + death.length);
  for (const m of death) console.log('  !  ' + m);
  console.log('');
  console.log('Quella riga non c\'e\' proprio: la citazione e\' morta, non spostata.');
  process.exitCode = 1;
}

// =============================================================================
//  SECONDA PASSATA - i rimandi interni: la riga c'e', ma parla d'altro?
// =============================================================================

// Quante righe intorno a un rimando a riga singola si guardano. Largo apposta:
// una finestra stretta accuserebbe chi cita l'inizio di un paragrafo. Questo
// attrezzo preferisce lasciar passare un rimando storto che accusarne uno buono.
const BEFORE = 5, AFTER = 20;

function subjects(line) {
  const outside = [];
  for (const m of line.matchAll(/`([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)`/g)) {
    const s = m[1];
    if (s.length < 6) continue;              // `uid`, `x`: non sono soggetti
    if (!/[A-Z]/.test(s)) continue;          // prosa fra backtick, non codice
    if (/\.(cs|js|ps1|json|md)$/i.test(s)) continue;   // e' un nome di file
    outside.push(s);
  }
  return outside;
}

function secondPass(lines, fileName) {
  const brokenOnes = [], skippedOnes = [];
  let good = 0;
  for (let i = 0; i < lines.length; i++) {
    const r = lines[i];
    for (const m of r.matchAll(/`?TODO\.md:~?(\d+)(?:\s*[-\u2013]\s*(\d+))?/g)) {
      const a = parseInt(m[1], 10);
      const b = m[2] ? parseInt(m[2], 10) : null;
      const da = b ? a : Math.max(1, a - BEFORE);
      const al = b ? b : a + AFTER;
      const location = 'riga ' + (i + 1) + ' → TODO.md:' + a + (b ? '-' + b : '');
      const subject = subjects(r);
      if (subject.length === 0) { skippedOnes.push(location + '  (nessun identificatore sulla riga)'); continue; }
      const window = lines.slice(da - 1, al).join('\n');
      const found = subject.filter(s => window.indexOf(s) >= 0);
      if (found.length) { good++; if (all) console.log('  ok   ' + location + '  (ci trova ' + found[0] + ')'); continue; }
      brokenOnes.push(location + '  — la' + (b ? ' zona' : ' zona intorno') + ' non nomina ' + subject.slice(0, 3).join(' ne\' '));
    }
  }
  console.log('');
  console.log('rimandi interni a ' + fileName + ':');
  console.log('  che trovano il loro soggetto: ' + good);
  if (skippedOnes.length) console.log('  saltati (senza soggetto da controllare): ' + skippedOnes.length);
  if (brokenOnes.length === 0) {
    console.log('  Nessun rimando interno manda a righe che parlano d\'altro.');
    return 0;
  }
  console.log('  RIMANDI CHE MANDANO NEL POSTO SBAGLIATO: ' + brokenOnes.length);
  for (const x of brokenOnes) console.log('    !  ' + x);
  console.log('');
  console.log('  La riga citata esiste — ma non parla di quello che la frase dice.');
  return brokenOnes.length;
}

if (secondPass(lines, path.basename(fileTodo)) > 0) process.exitCode = 1;

// ── e adesso lo si fa fallire apposta, in tutt'e due i versi ───────────────
if (process.argv.includes('--provami')) {
  console.log('');
  console.log('=== PROVA DEL CONTROLLO: lo stesso rimando, rotto e sano ===');
  const fake = [];
  for (let i = 0; i < 40; i++) fake.push('riempitivo');
  fake[29] = 'qui si parla di `IFace2.Normal` e di sottosquadri.';
  fake[34] = 'e qui invece `GetRemainingDOFs` risponde 2 = non disponibile.';

  const broken = fake.slice();
  broken.push('la strada e\' morta: `TODO.md:25-30` registra che `GetRemainingDOFs` non torna un conteggio.');
  const sound = fake.slice();
  sound.push('la strada e\' morta: `TODO.md:33-36` registra che `GetRemainingDOFs` non torna un conteggio.');

  const a = secondPass(broken, 'FINTO-rotto');
  const b = secondPass(sound, 'FINTO-sano');
  console.log('');
  if (a === 1 && b === 0) {
    console.log('IL CONTROLLO SCATTA sul rimando rotto e TACE su quello sano. Provato.');
  } else {
    console.log('IL CONTROLLO NON SI COMPORTA COME DICE: rotto=' + a + ' (atteso 1), sano=' + b + ' (atteso 0)');
    process.exitCode = 1;
  }
}
