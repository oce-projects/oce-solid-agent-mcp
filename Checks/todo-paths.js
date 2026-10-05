// =============================================================================
//  todo-paths.js - i file citati nelle voci APERTE esistono ancora?
// =============================================================================
//
//  PERCHE' ESISTE
//
//  Il TODO nomina file in continuazione. Sono riferimenti scritti anche mesi
//  fa, e **un file si rinomina senza che il TODO se ne accorga**. Una voce
//  aperta che manda a un file che non c'e' piu' non e' un dettaglio: chi la
//  legge prova, non trova, e conclude che la voce sia vecchia.
//
//  E' il gemello di `Checks/todo-signatures.ps1`, che fa la stessa cosa con
//  le API: si verifica la cosa, non cio' che la descrive.
//
//  ── DUE COSE IMPARATE SCRIVENDOLO, E TUTT'E DUE CONTANO ─────────────────────
//
//  1. **I percorsi sono scritti da dove si lavora.** Il TODO scrive
//     `cases/drawing/...` intendendo `Bench/cases/drawing/...`, e `Catalogo/...`
//     intendendo `Oce.Solid.Agent.Core/Catalog/...`. La prima stesura li
//     segnalava tutti come mancanti: trentatre' allarmi, quasi tutti falsi.
//     Adesso si provano le cartelle da cui si lavora davvero.
//
//  2. **Un percorso vecchio dentro una voce CHIUSA e' storia, non un refuso.**
//     `Agente/OperazioniSw.cs` era il nome del progetto prima della rinomina:
//     citarlo in una voce del 14 settembre e' corretto, ed e' la stessa regola
//     per cui il CHANGELOG non si riscrive. Si guardano solo le voci **aperte**
//     e quelle a meta': sono le uniche che qualcuno seguira'.
//
//  USO
//      node Checks/todo-paths.js
//      node Checks/todo-paths.js --tutti      anche le voci chiuse
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const all = process.argv.includes('--tutti');
// IL FILE SI PUO' PASSARE, come in `count-todo.js`: serve a far scattare
// questa sonda su una copia rotta, che e' l'unico modo di crederle.
const { findTheTodo } = require('./where-is-the-todo.js');
const fileTodo = findTheTodo(root, process.argv);
if (!fileTodo) process.exit(0);
const lines = fs.readFileSync(fileTodo, 'utf8').split(/\r?\n/);

// Le cartelle da cui il TODO scrive i percorsi.
const BASES = ['', 'Bench', 'Bench/cases', 'Oce.Solid.Agent.Core', 'Tools',
              'Checks', 'Tests', 'Mcp', 'Bridge'];

// ── CARTELLE DI UN ALTRO REPOSITORY ─────────────────────────────────────────
//
// Il TODO cita anche l'add-in di OpenPDM, che sta in un repository diverso:
// `Agente/` e `Services/` sono cartelle SUE. La tabella dei file estratti
// («gli 11 file, 9.923 righe») li elenca coi nomi di la', ed e' corretto:
// dice da dove vengono.
//
// La prima stesura li segnalava come mancanti -- dodici allarmi, tutti
// falsi, perche' **la sonda guardava in un repository solo e il TODO ne
// cita due**. E' la terza volta in un giorno che una sonda di questo tipo
// sovra-segnala: il modo di sbagliare non e' la regola, e' il confine di
// cio' che la regola puo' vedere.
// E `scratchpad/` non e' versionata **per scelta** (sta scritto in
// Bench/README.md): un file citato di li' non e' un riferimento rotto, e'
// una traccia di come fu misurato. Segnalarlo ogni volta insegnerebbe a
// ignorare l'elenco.
// E `docs/` e' di OpenPDM, dove vivono le note di integrazione (per esempio
// l'inventario dei moduli della licenza SOLIDWORKS): in questo repository una
// cartella `docs/` non c'e'. Aggiunta il 24/9/2026, quando il TODO ha
// cominciato a citare quell'inventario.
const ELSEWHERE = [/^Agente\//, /^Services\//, /^client-windows\//, /^scratchpad\//, /^docs\//];
// ── FILE TRANSITORI, e sono una terza categoria ────────────────────────────
//
// I piani a mano di una sessione passata (`piani/...`) non sono mai stati
// versionati: la citazione nel TODO dice COME fu misurato qualcosa, non
// indica un file da aprire. Categoria diversa da «altro repository» e da
// «voce chiusa», e tenerle distinte e il punto: tre motivi diversi per cui
// un percorso non si trova, e solo uno e un difetto.
// A name made at run time is not a file to open either: the bench writes its
// report of a run as `Bench/report/AAAAMMGG_hhmm.md`, one per date. The TODO
// quotes the pattern; window 1 of W3.1-bis (.4) moved it from its Italian
// folder, and before that the pattern was found only because a closed entry
// quoted it first.
// The pattern is the shape of the placeholder: eight capitals for the date,
// four small letters for the time.
const TRANSIENTS = [/^piani\//, /\/[A-Z]{8}_[a-z]{4}\.[a-z]+$/];

const EXTENSIONS = ['.cs', '.json', '.md', '.ps1', '.js', '.csproj', '.yml', '.txt'];

function location(p) {
  for (const b of BASES) {
    const full = path.join(root, b, p);
    if (fs.existsSync(full)) return b === '' ? '(radice)' : b;
  }
  return null;
}

// ── I FILE DA CREARE, dichiarati (28/9/2026) ────────────────────────────────
//
// Una voce aperta che progetta del lavoro nomina i file che quel lavoro
// creera': `Tools/coverage.json` non c'e' perche' la voce non e' ancora
// fatta, non perche' qualcuno l'abbia rinominato. Le Sezioni T e U (i due
// workflow del 27/9) ne citano sette, e il gancio fermava il push.
//
// La regola NON esenta un file per nome: vuole che la voce lo DICA, scrivendo
// «(da creare)» subito dopo il percorso, almeno una volta. Un file annunciato
// e mai scritto resta visibile: quando la voce si chiude, il percorso finisce
// fra quelli delle voci chiuse, e il `--tutti` lo elenca.
const TO_CREATE = /^\s*\(da creare\)/;
const announced = new Set();

// ── THE RENAME ENTRY KEEPS THE NAMES OF BEFORE (decision D10) ───────────────
//
// The entry W3.1-bis is the record of the rename into English: it quotes the
// folders and files by the names they had when each window was planned, on
// purpose, as the CHANGELOG does. Window 3 (W3.1-bis.6) moved the folders,
// and its `--todo` rewrites every open entry except this one. A path of that
// entry that does not exist is judged in the names of after: if the name map
// turns it into a file that exists, it is a name of before, not a broken
// reference. Only inside that entry: anywhere else an old name is a stale
// pointer and stays red.
const RENAME_ENTRY = /^(#{2,5}) (?:\[[ x~]\] )?W3\.1-bis\b/;
let renameMap = null;
function nameOfAfter(p) {
  if (renameMap === null) {
    try { renameMap = require('./language/window-3-identifiers.js').loadMap(root); }
    catch (e) { renameMap = false; }
  }
  if (!renameMap) return null;
  const segs = p.split('/');
  const out = segs.map((s, i) => (i === segs.length - 1 ? renameMap.file.get(s) : renameMap.folder.get(s) || renameMap.project.get(s)) || s);
  const q = out.join('/');
  return q !== p && location(q) ? q : null;
}

let stateEntry = null;          // ' ', 'x' o '~' della voce in cui siamo
let renameLevel = 0;            // heading level of the entry W3.1-bis while inside it
const citations = new Map();   // percorso -> stato della voce
const inRenameEntry = new Set();
const outsideRenameEntry = new Set();

for (const line of lines) {
  const hl = /^(#{1,5}) /.exec(line);
  if (hl && renameLevel && hl[1].length <= renameLevel && !RENAME_ENTRY.test(line)) renameLevel = 0;
  const re = RENAME_ENTRY.exec(line);
  if (re && !renameLevel) renameLevel = re[1].length;
  const h = line.match(/^#{2,4} \[([ x~])\]/);
  if (h) stateEntry = h[1];
  // A `(com'era)` heading is the original wording of an entry, kept for
  // reference: history, like a closed entry (the rule at the top of the TODO).
  else if (/^#{2,4} \(com'era\)/.test(line)) stateEntry = 'x';
  else if (/^#{1,4} /.test(line)) stateEntry = null;   // un titolo senza casella

  for (const m of line.matchAll(/`([^`\n]+)`/g)) {
    let p = m[1].trim();
    if (p.startsWith('\\\\') || /^[A-Za-z]:[\\/]/.test(p)) continue;  // assoluti
    if (/^HK(LM|CU)/.test(p) || p.startsWith('http')) continue;
    p = p.replace(/\\/g, '/').replace(/^\.\//, '');
    if (!p.includes('/')) continue;
    if (/[ ()*]/.test(p)) continue;
    if (!EXTENSIONS.some(e => p.toLowerCase().endsWith(e))) continue;
    if (TO_CREATE.test(line.slice(m.index + m[0].length))) announced.add(p);
    if (!citations.has(p)) citations.set(p, stateEntry);
    // judged per citation: the same old path quoted again in another open
    // entry is a stale pointer there, whatever the rename entry says
    if (renameLevel) inRenameEntry.add(p);
    else if (stateEntry !== 'x') outsideRenameEntry.add(p);
  }
}

const missingItems = [];
const otherRepo = [];
const historical = [];
const toDo = [];
const renamedBefore = [];
let ok = 0;
for (const [p, docState] of citations) {
  if (ELSEWHERE.some(r => r.test(p)) || TRANSIENTS.some(r => r.test(p))) { otherRepo.push(p); continue; }
  const b = location(p);
  if (b) { ok++; if (all) console.log('  ok   ' + p + (b === '(radice)' ? '' : '   [sotto ' + b + ']')); continue; }
  if (docState === 'x') historical.push(p);      // voce chiusa: e' un nome di allora
  else if (inRenameEntry.has(p) && !outsideRenameEntry.has(p) && nameOfAfter(p)) renamedBefore.push(p);
  else if (announced.has(p)) toDo.push(p);   // voce aperta che dice «(da creare)»
  else missingItems.push(p + (docState === null ? '   (fuori da una voce)' : ''));
}

console.log('percorsi citati:  ' + citations.size);
console.log('che esistono:     ' + ok);
console.log('in voci CHIUSE, e non si toccano: ' + historical.length);
console.log('altrove o transitori:             ' + otherRepo.length);
console.log('annunciati «(da creare)»:         ' + toDo.length);
console.log('names of before, in the entry W3.1-bis: ' + renamedBefore.length);
console.log('');
if (missingItems.length === 0) {
  console.log('Nessuna voce aperta manda a un file che non c\'e\'.');
} else {
  console.log('VOCI APERTE che mandano a un file inesistente: ' + missingItems.length);
  for (const p of missingItems) console.log('  !  ' + p);
  console.log('');
  console.log('Puo\' essere un file rinominato, uno mai scritto, o una cartella');
  console.log('non ancora nell\'elenco BASI. E\' una cosa da guardare, non una colpa.');
  // ESCE 1, perche' il gancio pre-push lo legge. Una sonda che stampa e
  // poi esce 0 e' una sonda che nessun gancio puo' usare.
  process.exitCode = 1;
}
if (all && toDo.length) {
  console.log('');
  console.log('file annunciati dalle voci aperte, da creare:');
  for (const p of toDo) console.log('  +  ' + p);
}
if (all && historical.length) {
  console.log('');
  console.log('nomi di allora, dentro voci chiuse:');
  for (const p of historical) console.log('  ~  ' + p);
}
