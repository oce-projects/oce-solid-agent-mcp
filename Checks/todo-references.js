// =============================================================================
//  todo-references.js - does «vedi la voce K14» point to an entry that exists?
// =============================================================================
//
//  WHY IT EXISTS
//
//  The TODO refers to itself all the time: «voce K17», «vedi Z-C4»,
//  «come la GT0». They are the seams of the file: it is how an entry says why it
//  exists without repeating what is already written elsewhere.
//
//  **A broken cross-reference is worse than no cross-reference**: whoever reads goes to look,
//  does not find it, and does not know whether the entry has vanished, been renamed, or whether they
//  looked badly. It is the same disease as the path that is no longer there
//  (`todo-paths.js`) and the signature that does not exist
//  (`todo-signatures.ps1`), applied to the internal cross-references.
//
//  The twin of this check already exists and is in the contract checks:
//  «in the TODO two entries are not called the same way». That one prevents
//  one name from indicating two; this one, that a name indicates none.
//
//  USAGE
//      node Checks/todo-references.js
//      node Checks/todo-references.js --tutti    also those that are fine
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const all = process.argv.includes('--tutti');
// THE FILE CAN BE PASSED, as in `count-todo.js`: it serves to make this
// probe trigger on a broken copy, which is the only way to believe it.
const { findTheTodo } = require('./where-is-the-todo.js');
const fileTodo = findTheTodo(root, process.argv);
if (!fileTodo) process.exit(0);
const lines = fs.readFileSync(fileTodo, 'utf8').split(/\r?\n/);

// ── THE NAMES THAT EXIST ───────────────────────────────────────────────
//
// They are read from the headings, which are the only place where an entry name
// is defined. `#### [x] K17 — ...` or `### [ ] W3.1 — ...`.
// AND THE SUB-ENTRIES, with five hashes (28/9/2026): `##### [ ] U1.1 — ...`,
// `##### [ ] Z-C21.3 — ...`. The big entries of Sections T and U, of W3.1-bis and
// of Z-C21 split up like this, and a cross-reference to a sub-entry that exists was
// taken as broken.
const exist = new Set();
for (const r of lines) {
  const m = r.match(/^#{2,5} \[[ x~]\] ([A-Z][A-Za-z0-9.\-]*)\b/);
  if (m) exist.add(m[1]);
  // also the sections without a box: `## W2 — ...`, `### Z-C — ...`
  const s = r.match(/^#{2,4} ([A-Z][A-Za-z0-9.\-]*)\s+—/);
  if (s) exist.add(s[1]);
}

// ── THE CROSS-REFERENCES ───────────────────────────────────────────────
//
// Only the explicit forms: «voce X», «voci X e Y», «vedi X». Searching for every
// acronym that looks like a name would collect half of the prose -- and a
// list that fishes up the false ones is not read.
const NAME = '([A-Z][A-Za-z0-9]*(?:[.\\-][A-Za-z0-9]+)*)';
const SHAPES = [
  new RegExp('\\bvoc[ei]\\s+' + NAME, 'g'),
  new RegExp('\\bvedi\\s+(?:anche\\s+)?(?:la\\s+voce\\s+)?' + NAME, 'g'),
];

const brokenOnes = new Map();
let ok = 0;
for (let i = 0; i < lines.length; i++) {
  for (const f of SHAPES) {
    f.lastIndex = 0;
    let m;
    while ((m = f.exec(lines[i])) !== null) {
      const itemName = m[1];
      // Italian words that begin with a capital letter and end up in the net.
      if (/^(Il|La|Le|Lo|Un|Una|Questa|Questo|Quella|Quello|Non|Che|Come|Perche|E|Ma|Si|No|Sono|Ha|Era|Anche|Tutte|Tutti|Due|Tre|Adesso|Oggi|Nel|Nella|Dal|Della|Con|Per|Senza|Dove|Quando|Cosa|Chi|Piu|Meno|Prima|Dopo|Sopra|Sotto)$/i.test(itemName)) continue;
      if (!/[0-9]/.test(itemName)) continue;   // an entry name always has a number
      if (exist.has(itemName)) { ok++; if (all) console.log('  ok   ' + itemName); continue; }
      if (!brokenOnes.has(itemName)) brokenOnes.set(itemName, i + 1);
    }
  }
}

console.log('nomi di voce definiti:  ' + exist.size);
console.log('rimandi verificati:     ' + ok);
console.log('');
if (brokenOnes.size === 0) {
  console.log('Tutti i rimandi del TODO puntano a una voce che esiste.');
} else {
  console.log('RIMANDI A VOCI CHE NON ESISTONO: ' + brokenOnes.size);
  for (const [itemName, line] of brokenOnes) console.log('  !  ' + itemName + '   (riga ' + line + ')');
  console.log('');
  console.log('Puo\' essere una voce rinominata, una mai scritta, o una sigla che');
  console.log('assomiglia a un nome. E\' una cosa da guardare, non una colpa.');
  // EXITS 1: see the twin `todo-paths.js`.
  process.exitCode = 1;
}
