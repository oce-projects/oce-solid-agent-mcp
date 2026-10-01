#!/usr/bin/env node
// =============================================================================
//  update-names-in-notes.js - the code names of the rename windows in the
//  notes that live outside the repository (entry W3.1-bis.5, decision D10)
// =============================================================================
//
//  WHAT IT DOES
//
//  The rename windows rename the code; the notes that talk about the code
//  (note-interne/TODO.md, the CLAUDE.md of the projects, the memories of the
//  assistant, the notes of the RAG) keep their prose, which stays Italian
//  (D10), and get only the code names they cite renamed:
//
//    - a span between backticks whose whole content is a name of the window
//      (`profondita_mm`, `misure.fori`, `@faccia(verso=alto)`, `disegna`);
//    - an exact snake_case token of the window (profondita_mm);
//    - a selector anywhere (@faccia(...));
//    - the URIs of the catalogue resources (catalogo://operazioni).
//
//  A word between guillemets or double quotes is NOT renamed in a note: in
//  the notes the names are written between backticks, and a word between
//  guillemets is a quote of what somebody said or of a message. A common word
//  used as a name (asola, guscio, specchia) is never replaced as a word. The
//  names an Italian SOLIDWORKS writes (Schizzo1, «Piano frontale») are never
//  touched. In a TODO (--todo) the closed entries ([x]) and the (com'era)
//  sections are history and stay as they are, as Controlli/percorsi-del-todo.js
//  reads them.
//
//  It takes EXPLICIT paths only (files, or folders whose *.md files it
//  walks), never a default: the owner runs it on the real files at the merge
//  of the window. With --dry-run it writes nothing and prints every line it
//  would change.
//
//  USAGE
//    node Controlli/lingua/update-names-in-notes.js [--dry-run] [--list] <path>...
//    node Controlli/lingua/update-names-in-notes.js [--dry-run] --todo <TODO.md>
//    node Controlli/lingua/update-names-in-notes.js --self-test
//        fake notes in a temporary folder: every rule seen doing what it says
//
//  EXIT CODES
//    0  done (or nothing to do), self-test as expected
//    1  self-test not as expected
//    2  cannot run: no path, a path that does not exist
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');
const cp = require('child_process');

const WINDOW = path.join(__dirname, 'window-2-surface.js');
const ROOT = path.resolve(__dirname, '..', '..');

function usage() {
  console.log(fs.readFileSync(__filename, 'utf8').split('\n').slice(1, 46).join('\n'));
}

function run(args) {
  const r = cp.spawnSync(process.execPath, [WINDOW, '--root', ROOT].concat(args), { encoding: 'utf8', maxBuffer: 1 << 26 });
  process.stdout.write(r.stdout || '');
  process.stderr.write(r.stderr || '');
  return r.status === null ? 2 : r.status;
}

function selfTest() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'notes-'));
  const BT = String.fromCharCode(96);
  const note = [
    '# Una nota',
    'Il passo usa ' + BT + 'estrudi_rettangolo' + BT + ' con profondita_mm 20 e ' + BT + 'misure.fori' + BT + '.',
    'La faccia si sceglie con @faccia(verso=davanti) e lo strumento ' + BT + 'guarda_il_pezzo' + BT + '.',
    'Una asola e un guscio restano parole: «asola» e "guscio" sono citazioni.',
    'Il nome dell albero resta Schizzo1, e la risorsa e\' catalogo://operazioni.',
  ].join('\n') + '\n';
  const todo = [
    '## Le voci',
    '#### [ ] Una voce aperta',
    'Si chiama ' + BT + 'disegna' + BT + '.',
    '#### [x] Una voce chiusa',
    'Si chiamava ' + BT + 'disegna' + BT + '.',
  ].join('\n') + '\n';
  const a = path.join(dir, 'nota.md'), b = path.join(dir, 'TODO.md');
  fs.writeFileSync(a, note);
  fs.writeFileSync(b, todo);
  const checks = [];
  const check = (what, ok) => { checks.push([what, ok]); console.log('  ' + (ok ? 'ok' : 'XX') + '  ' + what); };
  // the dry run writes nothing
  run(['--notes', a, '--dry-run']);
  check('--dry-run writes nothing', fs.readFileSync(a, 'utf8') === note);
  run(['--notes', a]);
  const n = fs.readFileSync(a, 'utf8');
  check('a name between backticks is renamed', n.includes(BT + 'extrude_rectangle' + BT) && n.includes(BT + 'inspect_part' + BT));
  check('a snake_case token is renamed', n.includes('depth_mm 20'));
  check('a path of a response is renamed', n.includes(BT + 'measurements.holes' + BT));
  check('a selector is renamed', n.includes('@face(normal=front)'));
  check('a common word stays a word, also quoted', n.includes('Una asola e un guscio') && n.includes('«asola»') && n.includes('"guscio"'));
  check('a name of an Italian SOLIDWORKS is not touched', n.includes('Schizzo1'));
  check('the resource URI is renamed', n.includes('catalog://operations'));
  run(['--notes', a]);
  check('a second run changes nothing', fs.readFileSync(a, 'utf8') === n);
  run(['--todo', b]);
  const t = fs.readFileSync(b, 'utf8');
  check('an open entry of the TODO is renamed', t.includes('Si chiama ' + BT + 'build' + BT));
  check('a closed entry of the TODO is history', t.includes('Si chiamava ' + BT + 'disegna' + BT));
  try { fs.rmSync(dir, { recursive: true, force: true }); } catch (e) { /* left */ }
  const bad = checks.filter(c => !c[1]).length;
  console.log((checks.length - bad) + ' of ' + checks.length + ' checks as expected.');
  return bad ? 1 : 0;
}

function main(argv) {
  if (argv.includes('--help') || argv.includes('-h')) { usage(); return 0; }
  if (argv.includes('--self-test')) return selfTest();
  const dry = argv.includes('--dry-run');
  const list = argv.includes('--list');
  const ti = argv.indexOf('--todo');
  if (ti >= 0) {
    const todo = argv[ti + 1];
    if (!todo) { console.error('--todo needs a file'); return 2; }
    return run(['--todo', path.resolve(todo)].concat(dry ? ['--dry-run'] : []).concat(list ? ['--list'] : []));
  }
  const paths = argv.filter(a => !a.startsWith('--'));
  if (!paths.length) { console.error('no path: the notes are named one by one (see --help)'); return 2; }
  for (const p of paths) if (!fs.existsSync(p)) { console.error('no such file or folder: ' + p); return 2; }
  return run(['--notes'].concat(paths.map(p => path.resolve(p))).concat(dry ? ['--dry-run'] : []).concat(list ? ['--list'] : []));
}

if (require.main === module) process.exitCode = main(process.argv.slice(2));
