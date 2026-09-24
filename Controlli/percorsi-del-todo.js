// =============================================================================
//  percorsi-del-todo.js - i file citati nelle voci APERTE esistono ancora?
// =============================================================================
//
//  PERCHE' ESISTE
//
//  Il TODO nomina file in continuazione. Sono riferimenti scritti anche mesi
//  fa, e **un file si rinomina senza che il TODO se ne accorga**. Una voce
//  aperta che manda a un file che non c'e' piu' non e' un dettaglio: chi la
//  legge prova, non trova, e conclude che la voce sia vecchia.
//
//  E' il gemello di `Controlli/firme-del-todo.ps1`, che fa la stessa cosa con
//  le API: si verifica la cosa, non cio' che la descrive.
//
//  ── DUE COSE IMPARATE SCRIVENDOLO, E TUTT'E DUE CONTANO ─────────────────────
//
//  1. **I percorsi sono scritti da dove si lavora.** Il TODO scrive
//     `casi/tavola/...` intendendo `Banco/casi/tavola/...`, e `Catalogo/...`
//     intendendo `Riscontro.Disegno/Catalogo/...`. La prima stesura li
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
//      node Controlli/percorsi-del-todo.js
//      node Controlli/percorsi-del-todo.js --tutti      anche le voci chiuse
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

// Le cartelle da cui il TODO scrive i percorsi.
const BASI = ['', 'Banco', 'Banco/casi', 'Riscontro.Disegno', 'Strumenti',
              'Controlli', 'Prove', 'Mcp', 'Ponte'];

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
// Banco/LEGGIMI.md): un file citato di li' non e' un riferimento rotto, e'
// una traccia di come fu misurato. Segnalarlo ogni volta insegnerebbe a
// ignorare l'elenco.
// E `docs/` e' di OpenPDM, dove vivono le note di integrazione (per esempio
// l'inventario dei moduli della licenza SOLIDWORKS): in questo repository una
// cartella `docs/` non c'e'. Aggiunta il 24/9/2026, quando il TODO ha
// cominciato a citare quell'inventario.
const ALTROVE = [/^Agente\//, /^Services\//, /^client-windows\//, /^scratchpad\//, /^docs\//];
// ── FILE TRANSITORI, e sono una terza categoria ────────────────────────────
//
// I piani a mano di una sessione passata (`piani/...`) non sono mai stati
// versionati: la citazione nel TODO dice COME fu misurato qualcosa, non
// indica un file da aprire. Categoria diversa da «altro repository» e da
// «voce chiusa», e tenerle distinte e il punto: tre motivi diversi per cui
// un percorso non si trova, e solo uno e un difetto.
const TRANSITORI = [/^piani\//];

const ESTENSIONI = ['.cs', '.json', '.md', '.ps1', '.js', '.csproj', '.yml', '.txt'];

function dove(p) {
  for (const b of BASI) {
    const pieno = path.join(radice, b, p);
    if (fs.existsSync(pieno)) return b === '' ? '(radice)' : b;
  }
  return null;
}

let statoVoce = null;          // ' ', 'x' o '~' della voce in cui siamo
const citazioni = new Map();   // percorso -> stato della voce

for (const riga of righe) {
  const h = riga.match(/^#{2,4} \[([ x~])\]/);
  if (h) statoVoce = h[1];
  else if (/^#{1,4} /.test(riga)) statoVoce = null;   // un titolo senza casella

  for (const m of riga.matchAll(/`([^`\n]+)`/g)) {
    let p = m[1].trim();
    if (p.startsWith('\\\\') || /^[A-Za-z]:[\\/]/.test(p)) continue;  // assoluti
    if (/^HK(LM|CU)/.test(p) || p.startsWith('http')) continue;
    p = p.replace(/\\/g, '/').replace(/^\.\//, '');
    if (!p.includes('/')) continue;
    if (/[ ()*]/.test(p)) continue;
    if (!ESTENSIONI.some(e => p.toLowerCase().endsWith(e))) continue;
    if (!citazioni.has(p)) citazioni.set(p, statoVoce);
  }
}

const mancanti = [];
const altroRepo = [];
const storici = [];
let ok = 0;
for (const [p, stato] of citazioni) {
  if (ALTROVE.some(r => r.test(p)) || TRANSITORI.some(r => r.test(p))) { altroRepo.push(p); continue; }
  const b = dove(p);
  if (b) { ok++; if (tutti) console.log('  ok   ' + p + (b === '(radice)' ? '' : '   [sotto ' + b + ']')); continue; }
  if (stato === 'x') storici.push(p);      // voce chiusa: e' un nome di allora
  else mancanti.push(p + (stato === null ? '   (fuori da una voce)' : ''));
}

console.log('percorsi citati:  ' + citazioni.size);
console.log('che esistono:     ' + ok);
console.log('in voci CHIUSE, e non si toccano: ' + storici.length);
console.log('altrove o transitori:             ' + altroRepo.length);
console.log('');
if (mancanti.length === 0) {
  console.log('Nessuna voce aperta manda a un file che non c\'e\'.');
} else {
  console.log('VOCI APERTE che mandano a un file inesistente: ' + mancanti.length);
  for (const p of mancanti) console.log('  !  ' + p);
  console.log('');
  console.log('Puo\' essere un file rinominato, uno mai scritto, o una cartella');
  console.log('non ancora nell\'elenco BASI. E\' una cosa da guardare, non una colpa.');
  // ESCE 1, perche' il gancio pre-push lo legge. Una sonda che stampa e
  // poi esce 0 e' una sonda che nessun gancio puo' usare.
  process.exitCode = 1;
}
if (tutti && storici.length) {
  console.log('');
  console.log('nomi di allora, dentro voci chiuse:');
  for (const p of storici) console.log('  ~  ' + p);
}
