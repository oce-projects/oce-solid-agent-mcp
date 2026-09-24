// =============================================================================
//  righe-del-todo.js - i `file.cs:NNN` citati puntano ancora a qualcosa?
// =============================================================================
//
//  PERCHE' ESISTE
//
//  Il TODO cita punti precisi del codice — `OperazioniSw.cs:6186`,
//  `Attrezzi.cs:1100`, `EsecutorePiano.cs:512` — ed e' il modo giusto di
//  scrivere: un'affermazione con l'indirizzo si puo' controllare.
//
//  Ma **i numeri di riga scivolano a ogni modifica**, e nessuno se ne accorge.
//  Il 23/9/2026 la voce Z-A7 mandava a `OperazioniSw.cs:6186` per una chiamata
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
//  la stessa tecnica con cui `rimandi-del-todo.js` ha sciolto `vedi C8`.
//
//  Basta che UNO compaia: pretendere tutti punirebbe una frase che nomina
//  due cose e ne cita una. Se la riga non ha nessun identificatore fra
//  backtick, il rimando **si salta** -- senza soggetto si indovinerebbe.
//
//  USO
//      node Controlli/righe-del-todo.js
//      node Controlli/righe-del-todo.js --tutti    anche quelle che vanno
//      node Controlli/righe-del-todo.js --provami  fa fallire il controllo apposta
// =============================================================================
const fs = require('fs');
const path = require('path');

const radice = path.join(__dirname, '..');
const tutti = process.argv.includes('--tutti');
const { trovaIlTodo } = require('./dov-e-il-todo.js');
const fileTodo = trovaIlTodo(radice, process.argv);
if (!fileTodo) process.exit(0);
const righe = fs.readFileSync(fileTodo, 'utf8').split(/\r?\n/);

// Le stesse cartelle di `percorsi-del-todo.js`: il TODO scrive i nomi da dove
// si lavora.
const BASI = ['', 'Banco', 'Riscontro.Disegno', 'Strumenti', 'Controlli',
              'Prove', 'Mcp', 'Ponte', 'Riscontro.Disegno/Catalogo'];

// LA CARTELLA SCRITTA NELLA CITAZIONE COMANDA, e la prima scrittura la
// buttava via.
//
// Cercava per NOME BASE fra le cartelle note. Il 24/9/2026 la citazione
// `Ponte/Sessione.cs:824` e' finita su `Banco/Sessione.cs` -- che esiste,
// si chiama uguale, e ha 392 righe invece di 832. La sonda ha dichiarato
// morta una citazione giusta, **dentro il gancio pre-push**: avrebbe
// fermato una consegna per un errore suo.
//
// Adesso: se la citazione dice una cartella, si guarda LI'. Il giro per
// nome base resta per le citazioni che la cartella non ce l'hanno, e
// quando trova piu' di un file con lo stesso nome **si astiene** invece di
// scegliere: due file omonimi sono esattamente il caso in cui indovinare
// produce il rosso sbagliato.
function dove(citato) {
  // 1. con la cartella: e' un indirizzo, non un suggerimento.
  if (citato.indexOf('/') >= 0) {
    const p = path.join(radice, citato);
    return fs.existsSync(p) ? p : null;
  }

  // 2. senza: si cerca, e si conta quanti ne trova.
  const nome = path.basename(citato);
  const trovati = [];
  for (const b of BASI) {
    const p = path.join(radice, b, nome);
    if (fs.existsSync(p)) trovati.push(p);
  }
  if (trovati.length === 1) return trovati[0];
  if (trovati.length > 1) { omonimi.add(nome); return null; }
  return null;
}

// I nomi che stanno in piu' di una cartella: non si sceglie per loro, e lo
// si dice — se no la prossima citazione senza cartella sbaglia in silenzio.
const omonimi = new Set();

let statoVoce = null;
const citate = new Map();   // "file:riga" -> stato della voce

for (const r of righe) {
  const h = r.match(/^#{2,4} \[([ x~])\]/);
  if (h) statoVoce = h[1];
  else if (/^#{1,4} /.test(r)) statoVoce = null;

  // `NomeFile.cs:123` oppure `Cartella/NomeFile.cs:123`
  for (const m of r.matchAll(/`?([A-Za-z0-9_.\/\\-]+\.(?:cs|js|ps1|json|md)):(\d+)/g)) {
    const chiave = m[1].replace(/\\/g, '/') + ':' + m[2];
    if (!citate.has(chiave)) citate.set(chiave, statoVoce);
  }
}

const morte = [];
const nonTrovati = new Set();
let ok = 0, storiche = 0;

for (const [c, stato] of citate) {
  const taglio = c.lastIndexOf(':');
  const nome = c.slice(0, taglio);
  const n = parseInt(c.slice(taglio + 1), 10);
  const p = dove(nome);
  if (!p) { nonTrovati.add(nome); continue; }
  const quante = fs.readFileSync(p, 'utf8').split(/\r?\n/).length;
  if (n <= quante) { ok++; if (tutti) console.log('  ok   ' + c + '  (il file ha ' + quante + ' righe)'); continue; }
  if (stato === 'x') { storiche++; continue; }   // voce chiusa: era vero allora
  morte.push(c + '  — il file ha ' + quante + ' righe');
}

console.log('citazioni `file:riga`:     ' + citate.size);
console.log('che puntano dentro il file: ' + ok);
console.log('in voci chiuse, non si toccano: ' + storiche);
// SI STAMPANO I NOMI, non solo quanti sono. Un «7» non si puo' controllare:
// per sapere se e' giusto bisogna rifare a mano il lavoro della sonda.
if (nonTrovati.size) {
  console.log('file non trovati (altro repo, rinominati, o cartella sbagliata): ' +
              nonTrovati.size);
  for (const n of nonTrovati) console.log('    - ' + n);
}
if (omonimi.size) {
  console.log('nomi che stanno in piu\' cartelle, e senza cartella non si sceglie: ' +
              [...omonimi].join(', '));
  console.log('  (scrivere la cartella nella citazione li rende controllabili)');
}
console.log('');

if (morte.length === 0) {
  console.log('Nessuna voce aperta manda a una riga che non esiste.');
} else {
  console.log('VOCI APERTE che mandano OLTRE LA FINE del file: ' + morte.length);
  for (const m of morte) console.log('  !  ' + m);
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
const PRIMA = 5, DOPO = 20;

function soggetti(riga) {
  const fuori = [];
  for (const m of riga.matchAll(/`([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)`/g)) {
    const s = m[1];
    if (s.length < 6) continue;              // `uid`, `x`: non sono soggetti
    if (!/[A-Z]/.test(s)) continue;          // prosa fra backtick, non codice
    if (/\.(cs|js|ps1|json|md)$/i.test(s)) continue;   // e' un nome di file
    fuori.push(s);
  }
  return fuori;
}

function passataDue(righe, nomeFile) {
  const rotti = [], saltati = [];
  let buoni = 0;
  for (let i = 0; i < righe.length; i++) {
    const r = righe[i];
    for (const m of r.matchAll(/`?TODO\.md:~?(\d+)(?:\s*[-\u2013]\s*(\d+))?/g)) {
      const a = parseInt(m[1], 10);
      const b = m[2] ? parseInt(m[2], 10) : null;
      const da = b ? a : Math.max(1, a - PRIMA);
      const al = b ? b : a + DOPO;
      const dove = 'riga ' + (i + 1) + ' → TODO.md:' + a + (b ? '-' + b : '');
      const sogg = soggetti(r);
      if (sogg.length === 0) { saltati.push(dove + '  (nessun identificatore sulla riga)'); continue; }
      const finestra = righe.slice(da - 1, al).join('\n');
      const trovato = sogg.filter(s => finestra.indexOf(s) >= 0);
      if (trovato.length) { buoni++; if (tutti) console.log('  ok   ' + dove + '  (ci trova ' + trovato[0] + ')'); continue; }
      rotti.push(dove + '  — la' + (b ? ' zona' : ' zona intorno') + ' non nomina ' + sogg.slice(0, 3).join(' ne\' '));
    }
  }
  console.log('');
  console.log('rimandi interni a ' + nomeFile + ':');
  console.log('  che trovano il loro soggetto: ' + buoni);
  if (saltati.length) console.log('  saltati (senza soggetto da controllare): ' + saltati.length);
  if (rotti.length === 0) {
    console.log('  Nessun rimando interno manda a righe che parlano d\'altro.');
    return 0;
  }
  console.log('  RIMANDI CHE MANDANO NEL POSTO SBAGLIATO: ' + rotti.length);
  for (const x of rotti) console.log('    !  ' + x);
  console.log('');
  console.log('  La riga citata esiste — ma non parla di quello che la frase dice.');
  return rotti.length;
}

if (passataDue(righe, path.basename(fileTodo)) > 0) process.exitCode = 1;

// ── e adesso lo si fa fallire apposta, in tutt'e due i versi ───────────────
if (process.argv.includes('--provami')) {
  console.log('');
  console.log('=== PROVA DEL CONTROLLO: lo stesso rimando, rotto e sano ===');
  const finte = [];
  for (let i = 0; i < 40; i++) finte.push('riempitivo');
  finte[29] = 'qui si parla di `IFace2.Normal` e di sottosquadri.';
  finte[34] = 'e qui invece `GetRemainingDOFs` risponde 2 = non disponibile.';

  const rotto = finte.slice();
  rotto.push('la strada e\' morta: `TODO.md:25-30` registra che `GetRemainingDOFs` non torna un conteggio.');
  const sano = finte.slice();
  sano.push('la strada e\' morta: `TODO.md:33-36` registra che `GetRemainingDOFs` non torna un conteggio.');

  const a = passataDue(rotto, 'FINTO-rotto');
  const b = passataDue(sano, 'FINTO-sano');
  console.log('');
  if (a === 1 && b === 0) {
    console.log('IL CONTROLLO SCATTA sul rimando rotto e TACE su quello sano. Provato.');
  } else {
    console.log('IL CONTROLLO NON SI COMPORTA COME DICE: rotto=' + a + ' (atteso 1), sano=' + b + ' (atteso 0)');
    process.exitCode = 1;
  }
}
