// =============================================================================
//  todo-lines.js - do the cited `file.cs:NNN` still point at something?
// =============================================================================
//
//  WHY IT EXISTS
//
//  The TODO cites precise points of the code — `SwOperations.cs:6186`,
//  `McpTools.cs:1100`, `PlanExecutor.cs:512` — and it is the right way to
//  write: a statement with its address can be checked.
//
//  But **line numbers slip at every change**, and nobody notices.
//  On 23/9/2026 entry Z-A7 sent to `SwOperations.cs:6186` for a call
//  that sits at 6198: twelve lines, that is enough to end up in another
//  method. Whoever goes there does not find it, and does not know whether the entry is old or whether they read
//  wrong.
//
//  ── WHAT IT CAN SAY, AND WHAT IT CANNOT ─────────────────────────────────────
//
//  It can say with certainty that a line **does not exist**: the file is shorter.
//  That is a dead citation, and it must be corrected.
//
//  It CANNOT say whether the right line is another: to know it one would have to
//  understand what the entry is about. A check that tried would guess, and
//  here guessing is forbidden.
//
//  **So this tool takes little, and that little is certain.** Better than a
//  tool that takes a lot and is right half the time.
//
//  ── SECOND PASS: THE CROSS-REFERENCES INSIDE THE TODO ITSELF ───────────────
//
//  On 23/9/2026 entry Z-C14 sent to `TODO.md:7497-7530` and to
//  `TODO.md:~7753` to talk about `GetRemainingDOFs`. Both lines
//  exist -- the file has sixteen thousand -- and both talk about something else: the
//  first about `IFace2.Normal` and the undercuts, the second about the origin of
//  an extrusion. The real addresses were three and four hundred lines further
//  down. **The first pass could not see them**, and says so: it only looks at
//  whether the line exists.
//
//  The check does NOT guess what the entry is about. It takes **the subject that
//  the sentence wrote by itself**: the identifiers between backticks on the
//  same line as the cross-reference (`GetRemainingDOFs`, `ReplaceComponents2`). If
//  NONE of them appears in the cited lines, the cross-reference is broken. It is
//  the same technique with which `todo-references.js` resolved `vedi C8`.
//
//  It is enough that ONE appears: demanding all would punish a sentence that names
//  two things and cites one. If the line has no identifier between
//  backticks, the cross-reference **is skipped** -- without a subject it would guess.
//
//  USAGE
//      node Checks/todo-lines.js
//      node Checks/todo-lines.js --tutti    also those that are fine
//      node Checks/todo-lines.js --provami  makes the check fail on purpose
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const all = process.argv.includes('--tutti');
const { findTheTodo } = require('./where-is-the-todo.js');
const fileTodo = findTheTodo(root, process.argv);
if (!fileTodo) process.exit(0);
const lines = fs.readFileSync(fileTodo, 'utf8').split(/\r?\n/);

// The same folders as `todo-paths.js`: the TODO writes the names from where
// one works.
const BASES = ['', 'Banco', 'Oce.Solid.Agent.Core', 'Strumenti', 'Controlli',
              'Tests', 'Mcp', 'Bridge', 'Oce.Solid.Agent.Core/Catalog'];

// THE FOLDER WRITTEN IN THE CITATION RULES, and the first draft
// threw it away.
//
// It searched by BASE NAME among the known folders. On 24/9/2026 the citation
// `Bridge/Session.cs:824` ended up on `Bench/Session.cs` -- which exists,
// has the same name, and has 392 lines instead of 832. The probe declared
// dead a right citation, **inside the pre-push hook**: it would have
// stopped a delivery for a mistake of its own.
//
// Now: if the citation says a folder, we look THERE. The search by
// base name stays for the citations that do not have the folder, and
// when it finds more than one file with the same name **it abstains** instead of
// choosing: two files with the same name are exactly the case in which guessing
// produces the wrong red.
function location(cited) {
  // 1. with the folder: it is an address, not a suggestion.
  if (cited.indexOf('/') >= 0) {
    const p = path.join(root, cited);
    return fs.existsSync(p) ? p : null;
  }

  // 2. without: it is searched for, and we count how many it finds.
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

// The names that are in more than one folder: we do not choose for them, and it
// is said — otherwise the next citation without a folder is wrong in silence.
const namesakes = new Set();

let stateEntry = null;
const citedOnes = new Map();   // "file:line" -> state of the entry

for (const r of lines) {
  const h = r.match(/^#{2,4} \[([ x~])\]/);
  if (h) stateEntry = h[1];
  else if (/^#{1,4} /.test(r)) stateEntry = null;

  // `NomeFile.cs:123` or `Cartella/NomeFile.cs:123`
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
  if (docState === 'x') { historical++; continue; }   // closed entry: it was true then
  death.push(c + '  — il file ha ' + howMany + ' righe');
}

console.log('citazioni `file:riga`:     ' + citedOnes.size);
console.log('che puntano dentro il file: ' + ok);
console.log('in voci chiuse, non si toccano: ' + historical);
// THE NAMES ARE PRINTED, not only how many there are. A «7» cannot be checked:
// to know whether it is right one has to redo the work of the probe by hand.
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
//  SECOND PASS - the internal cross-references: the line is there, but is it about something else?
// =============================================================================

// How many lines around a single-line cross-reference are looked at. Wide on purpose:
// a narrow window would accuse whoever cites the start of a paragraph. This
// tool prefers to let a crooked cross-reference through than to accuse a good one.
const BEFORE = 5, AFTER = 20;

function subjects(line) {
  const outside = [];
  for (const m of line.matchAll(/`([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)`/g)) {
    const s = m[1];
    if (s.length < 6) continue;              // `uid`, `x`: they are not subjects
    if (!/[A-Z]/.test(s)) continue;          // prose between backticks, not code
    if (/\.(cs|js|ps1|json|md)$/i.test(s)) continue;   // it is a file name
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

// ── and now it is made to fail on purpose, in both directions ──────────────
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
