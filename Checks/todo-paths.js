// =============================================================================
//  todo-paths.js - do the files cited in the OPEN entries still exist?
// =============================================================================
//
//  WHY IT EXISTS
//
//  The TODO names files all the time. They are references written even months
//  ago, and **a file is renamed without the TODO noticing**. An open
//  entry that sends to a file that is no longer there is not a detail: whoever
//  reads it tries, does not find it, and concludes that the entry is old.
//
//  It is the twin of `Checks/todo-signatures.ps1`, which does the same thing with
//  the APIs: we verify the thing, not what describes it.
//
//  ── TWO THINGS LEARNED WRITING IT, AND BOTH COUNT ───────────────────────────
//
//  1. **The paths are written from where one works.** The TODO writes
//     `cases/drawing/...` meaning `Bench/cases/drawing/...`, and `Catalogo/...`
//     meaning `Oce.Solid.Agent.Core/Catalog/...`. The first draft
//     flagged them all as missing: thirty-three alarms, nearly all false.
//     Now the folders from which one really works are tried.
//
//  2. **An old path inside a CLOSED entry is history, not a typo.**
//     `Agente/OperazioniSw.cs` was the name of the project before the rename:
//     citing it in an entry of 14 September is correct, and it is the same rule
//     for which the CHANGELOG is not rewritten. Only the **open** entries are looked at
//     and the halfway ones: they are the only ones that somebody will follow.
//
//  USAGE
//      node Checks/todo-paths.js
//      node Checks/todo-paths.js --all      also the closed entries
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const all = process.argv.includes('--all');
// THE FILE CAN BE PASSED, as in `count-todo.js`: it serves to make this
// probe trigger on a broken copy, which is the only way to believe it.
const { findTheTodo } = require('./where-is-the-todo.js');
const fileTodo = findTheTodo(root, process.argv);
if (!fileTodo) process.exit(0);
const lines = fs.readFileSync(fileTodo, 'utf8').split(/\r?\n/);

// The folders from which the TODO writes the paths.
const BASES = ['', 'Bench', 'Bench/cases', 'Oce.Solid.Agent.Core', 'Tools',
              'Checks', 'Tests', 'Mcp', 'Bridge'];

// ── FOLDERS OF ANOTHER REPOSITORY ───────────────────────────────────────────
//
// The TODO also cites the OpenPDM add-in, which sits in a different repository:
// `Agente/` and `Services/` are ITS folders. The table of the extracted files
// («the 11 files, 9,923 lines») lists them with the names from there, and it is correct:
// it says where they come from.
//
// The first draft flagged them as missing -- twelve alarms, all
// false, because **the probe looked in one repository only and the TODO
// cites two**. It is the third time in a day that a probe of this kind
// over-reports: the way to be wrong is not the rule, it is the boundary of
// what the rule can see.
// And `scratchpad/` is not versioned **by choice** (it is written in
// Bench/README.md): a file cited from there is not a broken reference, it is
// a trace of how it was measured. Flagging it every time would teach
// to ignore the list.
// And `docs/` belongs to OpenPDM, where the integration notes live (for example
// the inventory of the modules of the SOLIDWORKS licence): in this repository a
// `docs/` folder is not there. Added on 24/9/2026, when the TODO began
// to cite that inventory.
const ELSEWHERE = [/^Agente\//, /^Services\//, /^client-windows\//, /^scratchpad\//, /^docs\//];
// ── TRANSIENT FILES, and they are a third category ──────────────────────────
//
// The hand plans of a past session (`piani/...`) were never
// versioned: the citation in the TODO says HOW something was measured, it does not
// point to a file to open. A different category from «other repository» and from
// «closed entry», and keeping them distinct is the point: three different reasons why
// a path is not found, and only one is a defect.
// A name made at run time is not a file to open either: the bench writes its
// report of a run as `Bench/report/<date>_<time>.md`, one per date. The TODO
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
    if (fs.existsSync(full)) return b === '' ? '(root)' : b;
  }
  return null;
}

// ── THE FILES TO BE CREATED, declared (28/9/2026) ───────────────────────────
//
// An open entry that designs some work names the files that this work will
// create: `Tools/coverage.json` is not there because the entry is not yet
// done, not because somebody renamed it. Sections T and U (the two
// workflows of 27/9) cite seven of them, and the hook stopped the push.
//
// The rule does NOT exempt a file by name: it wants the entry to SAY it, by writing
// «(da creare)» right after the path, at least once. A file announced
// and never written stays visible: when the entry closes, the path ends up
// among those of the closed entries, and `--all` lists it.
const TO_CREATE = /^\s*\(da creare\)/;
const announced = new Set();

// ── THE RENAME ENTRY KEEPS THE NAMES OF BEFORE (decision D10) ───────────────
//
// The entry W3.1-bis is the record of the rename into English: it quotes the
// folders and files by the names they had when each window was planned, on
// purpose, as the CHANGELOG does. Window 3 (W3.1-bis.6) moved the folders,
// and its rewrite of the names touched every open entry except this one. A
// path of that entry that does not exist is a name of before, not a broken
// reference (the name map that translated it into the new name is archived,
// outside the repository, since W3.1-bis.11). Only inside that entry:
// anywhere else an old name is a stale pointer and stays red.
const RENAME_ENTRY = /^(#{2,5}) (?:\[[ x~]\] )?W3\.1-bis\b/;

let stateEntry = null;          // ' ', 'x' or '~' of the entry we are in
let renameLevel = 0;            // heading level of the entry W3.1-bis while inside it
const citations = new Map();   // path -> state of the entry
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
  else if (/^#{1,4} /.test(line)) stateEntry = null;   // a title without a box

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
  if (b) { ok++; if (all) console.log('  ok   ' + p + (b === '(root)' ? '' : '   [under ' + b + ']')); continue; }
  if (docState === 'x') historical.push(p);      // closed entry: it is a name from back then
  else if (inRenameEntry.has(p) && !outsideRenameEntry.has(p)) renamedBefore.push(p);
  else if (announced.has(p)) toDo.push(p);   // open entry that says «(da creare)»
  else missingItems.push(p + (docState === null ? '   (outside an entry)' : ''));
}

console.log('cited paths:      ' + citations.size);
console.log('that exist:       ' + ok);
console.log('in CLOSED entries, and not touched: ' + historical.length);
console.log('elsewhere or transient:           ' + otherRepo.length);
console.log('announced «(da creare)»:          ' + toDo.length);
console.log('names of before, in the entry W3.1-bis: ' + renamedBefore.length);
console.log('');
if (missingItems.length === 0) {
  console.log('No open entry points to a file that is not there.');
} else {
  console.log('OPEN ENTRIES that point to a nonexistent file: ' + missingItems.length);
  for (const p of missingItems) console.log('  !  ' + p);
  console.log('');
  console.log('It may be a renamed file, one never written, or a folder');
  console.log('not yet in the BASES list. It is something to look at, not a fault.');
  // EXITS 1, because the pre-push hook reads it. A probe that prints and
  // then exits 0 is a probe that no hook can use.
  process.exitCode = 1;
}
if (all && toDo.length) {
  console.log('');
  console.log('files announced by the open entries, to be created:');
  for (const p of toDo) console.log('  +  ' + p);
}
if (all && historical.length) {
  console.log('');
  console.log('names of then, inside closed entries:');
  for (const p of historical) console.log('  ~  ' + p);
}
