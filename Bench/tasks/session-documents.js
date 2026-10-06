#!/usr/bin/env node
// =============================================================================
//  The documents between two sessions of the autonomous run (D9, W3.1-bis.5)
// =============================================================================
//
//  EVERY SESSION STARTS WITH NO DOCUMENT OPEN, except the ones that were open
//  before the run started (they are the person's, never ours to close).
//
//  The run of 6/10 closed between two tasks only the documents never saved
//  (`CloseLeftovers --close`). Everything task 08 saved in its temporary
//  folder (the assembly and its two parts) stayed open for every later
//  session, and a weld profile (.SLDLFP) of the library stayed open after
//  tasks 06-07: the model of the next session saw a box and a profile in
//  front of it instead of an empty SOLIDWORKS, and the document it found was
//  not the one the task was about.
//
//  THE RULE, in one function that needs no SOLIDWORKS (documentsToClose):
//  close EVERY open document that is not in the list taken before the run,
//  whatever its state: never saved, saved in the folder of a session, a file
//  of a library, a drawing. Drawings first, then assemblies, then parts: a
//  drawing keeps its model loaded and an assembly keeps its parts, so closing
//  in the other order leaves them behind. Documents are matched by title, as
//  the bench does with --spare.
//
//  The decision is made here, where it has a self-test; the closing itself is
//  done by Checks/CloseLeftovers (`--list-json`, `--close-titles-file`), which
//  discards unsaved changes: it never saves anything. A document that is still
//  open after three passes is a failure the run reports (exit 1): the session
//  that follows would not start from an empty SOLIDWORKS and would measure
//  something else.
//
//      node Bench/tasks/session-documents.js --list
//      node Bench/tasks/session-documents.js --snapshot <file.json>
//      node Bench/tasks/session-documents.js --close-all --spare <file.json>
//      node Bench/tasks/session-documents.js --self-test
//
//  Set CLOSE_LEFTOVERS_EXE to another program (or to a .js file, run by node)
//  to use something else than Checks/CloseLeftovers/bin/CloseLeftovers.exe:
//  the self-test does, with a program that plays SOLIDWORKS from a file.
//
//  Exit 0 when no document that must go is left open (or the self-test held),
//  1 when one is left or the self-test failed, 2 when the program cannot run
//  here (no SOLIDWORKS, a CloseLeftovers without the two modes: rebuild it with
//  Checks\build-checks.ps1).
// =============================================================================
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const cp = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..');
const PASSES = 3;
const ORDER = { drawing: 0, assembly: 1, part: 2 };

function fail(m, code) { console.error('session-documents: ' + m); process.exit(code === undefined ? 2 : code); }
const key = (t) => String(t).toLowerCase();

// ── the rule ──────────────────────────────────────────────────────────────
// What to close, in the order to close it: every open document whose title is
// not among the spared ones.
function documentsToClose(open, spared) {
  const keep = new Set((spared || []).map(key));
  return open
    .filter(d => !keep.has(key(d.title)))
    .map((d, i) => ({ d, i }))
    .sort((a, b) => (ORDER[a.d.type] !== undefined ? ORDER[a.d.type] : 3) - (ORDER[b.d.type] !== undefined ? ORDER[b.d.type] : 3) || a.i - b.i)
    .map(x => x.d);
}

// ── CloseLeftovers ────────────────────────────────────────────────────────
function program() {
  const exe = process.env.CLOSE_LEFTOVERS_EXE || path.join(ROOT, 'Checks', 'CloseLeftovers', 'bin', 'CloseLeftovers.exe');
  if (!fs.existsSync(exe)) throw new Error('no ' + exe + ' (build it with Checks\\build-checks.ps1)');
  return /\.js$/i.test(exe) ? { file: process.execPath, lead: [exe] } : { file: exe, lead: [] };
}
function run(args) {
  const p = program();
  const r = cp.spawnSync(p.file, p.lead.concat(args), { encoding: 'utf8', timeout: 240000, windowsHide: true });
  return r;
}
function listOpen() {
  const r = run(['--list-json']);
  if (r.error) throw new Error('CloseLeftovers did not run: ' + r.error.message);
  const out = String(r.stdout || '').trim();
  let list;
  try { list = JSON.parse(out); } catch (e) {
    throw new Error('CloseLeftovers did not answer --list-json with a JSON array' + (r.status === 2 ? ' (SOLIDWORKS is not open)' : ' (a build from before the two modes? rebuild it with Checks\\build-checks.ps1)') + ': ' + (out || String(r.stderr || '')).slice(0, 200));
  }
  if (!Array.isArray(list)) throw new Error('CloseLeftovers answered --list-json with something that is not an array');
  return list;
}
function closeTitles(titles) {
  const file = path.join(os.tmpdir(), 'close-titles-' + process.pid + '-' + Date.now() + '.txt');
  fs.writeFileSync(file, titles.join('\n') + '\n', 'utf8');
  try { return run(['--close-titles-file', file]); } finally { try { fs.unlinkSync(file); } catch (e) { /* gone */ } }
}

// Closes everything that is not spared, up to PASSES times, and says what is
// left. Never throws on a document that stays: that is the answer.
function closeAll(spared) {
  const closed = [];
  let left = [];
  for (let pass = 1; pass <= PASSES; pass++) {
    const toClose = documentsToClose(listOpen(), spared);
    if (toClose.length === 0) { left = []; break; }
    for (const d of toClose) if (!closed.includes(d.title)) closed.push(d.title);
    closeTitles(toClose.map(d => d.title));
    left = documentsToClose(listOpen(), spared);
    if (left.length === 0) break;
  }
  return { closed, left, spared: spared || [] };
}

function readSpared(file) {
  const j = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\ufeff/, ''));
  return (j.documents || []).map(d => d.title);
}

// ── the commands ──────────────────────────────────────────────────────────
function commandList() { console.log(JSON.stringify(listOpen())); return 0; }

function commandSnapshot(args) {
  if (args.length !== 1) fail('--snapshot <file.json>');
  const open = listOpen();
  fs.writeFileSync(args[0], JSON.stringify({ taken: new Date().toISOString(), documents: open }, null, 1));
  console.log('documents open before the run, spared for good: ' + (open.length ? open.map(d => d.title).join(', ') : 'none'));
  return 0;
}

function commandCloseAll(args) {
  const i = args.indexOf('--spare');
  if (i < 0 || !args[i + 1]) fail('--close-all --spare <file.json>');
  const r = closeAll(readSpared(args[i + 1]));
  console.log('closed: ' + (r.closed.length ? r.closed.join(', ') : 'nothing') + (r.spared.length ? '; spared (open before the run): ' + r.spared.join(', ') : ''));
  if (r.left.length) {
    console.log('STILL OPEN after ' + PASSES + ' passes: ' + r.left.map(d => d.title + (d.path ? ' (' + d.path + ')' : '')).join(', '));
    return 1;
  }
  return 0;
}

// ── the self-test ─────────────────────────────────────────────────────────
// A program that plays SOLIDWORKS from a file, with the two modes of
// CloseLeftovers: the documents are in the state file, a document with
// "stubborn" true stays open the first time it is asked to close, one with
// "stuck" true never goes.
const FAKE = [
  "const fs = require('fs');",
  "const state = process.env.FAKE_SW_STATE;",
  "const s = JSON.parse(fs.readFileSync(state, 'utf8'));",
  "const a = process.argv.slice(2);",
  "if (a[0] === '--list-json') { console.log(JSON.stringify(s.docs.map(d => ({ title: d.title, path: d.path, type: d.type })))); process.exit(0); }",
  "if (a[0] === '--close-titles-file') {",
  "  const titles = fs.readFileSync(a[1], 'utf8').split(/\\r?\\n/).filter(Boolean);",
  "  s.log.push(titles);",
  "  for (const t of titles) {",
  "    const d = s.docs.find(x => x.title === t); if (!d) continue;",
  "    if (d.stuck) continue;",
  "    if (d.stubborn && !d.tried) { d.tried = true; continue; }",
  "    s.docs = s.docs.filter(x => x !== d);",
  "  }",
  "  fs.writeFileSync(state, JSON.stringify(s));",
  "  console.log('close asked for ' + titles.length + '; documents open now: ' + s.docs.length);",
  "  process.exit(0);",
  "}",
  "process.exit(2);",
].join('\n');

function selfTest() {
  let bad = 0;
  const check = (name, ok, more) => { console.log((ok ? '  ok  ' : '  XX  ') + name + (ok || !more ? '' : '  [' + more + ']')); if (!ok) bad++; };
  const titles = (l) => l.map(d => d.title);

  // The night of 6/10, as the documents were: two documents the person had
  // open before the run, then what tasks 06-08 left behind.
  const before = [
    { title: 'Mine.SLDPRT', path: 'D:\\work\\Mine.SLDPRT', type: 'part' },
    { title: 'Draft1', path: '', type: 'part' },
  ];
  const left = [
    { title: 'base.SLDPRT', path: 'C:\\Temp\\d9-1\\base.SLDPRT', type: 'part' },
    { title: 'lid.SLDPRT', path: 'C:\\Temp\\d9-1\\lid.SLDPRT', type: 'part' },
    { title: 'box.SLDASM', path: 'C:\\Temp\\d9-1\\box.SLDASM', type: 'assembly' },
    { title: 'TUBE SQUARE.SLDLFP', path: 'D:\\library\\TUBE SQUARE.SLDLFP', type: 'part' },
    { title: 'Part23', path: '', type: 'part' },
    { title: 'Drawing1 - Sheet1', path: 'C:\\Temp\\d9-1\\box.SLDDRW', type: 'drawing' },
  ];
  const open = before.concat(left);
  const spared = titles(before);
  const toClose = documentsToClose(open, spared);

  // The rule of before (`CloseLeftovers --close` as the run called it, with no
  // folder): only the documents never saved. Written here to SHOW the defect.
  const ruleBefore = (d) => d.path === '';
  const keptByTheRuleBefore = left.filter(d => !ruleBefore(d)).map(d => d.title);
  check('the rule of before leaves open the saved files of the temporary folder and the library profile (the defect of 6/10)',
        ['base.SLDPRT', 'lid.SLDPRT', 'box.SLDASM', 'TUBE SQUARE.SLDLFP'].every(t => keptByTheRuleBefore.includes(t)));
  check('the rule closes EVERYTHING the run opened: saved files, a library file, a drawing, a never-saved document',
        left.every(d => titles(toClose).includes(d.title)) && toClose.length === left.length);
  check('and nothing that was open before the run, saved or not', !titles(toClose).includes('Mine.SLDPRT') && !titles(toClose).includes('Draft1'));
  check('drawings go first, then assemblies, then parts (a drawing keeps its model, an assembly its parts)',
        toClose[0].type === 'drawing' && toClose[1].type === 'assembly' && toClose.slice(2).every(d => d.type === 'part'), JSON.stringify(titles(toClose)));
  check('titles are matched without case', documentsToClose([{ title: 'MINE.sldprt', path: '', type: 'part' }], ['mine.SLDPRT']).length === 0);
  check('with nothing spared everything goes, and with nothing open nothing does', documentsToClose(open, []).length === open.length && documentsToClose([], spared).length === 0);

  // the whole flow against a program that plays SOLIDWORKS
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'session-documents-'));
  const fake = path.join(dir, 'fake-close-leftovers.js');
  const state = path.join(dir, 'state.json');
  const snapshot = path.join(dir, 'before.json');
  fs.writeFileSync(fake, FAKE);
  const saveEnv = { exe: process.env.CLOSE_LEFTOVERS_EXE, state: process.env.FAKE_SW_STATE };
  process.env.CLOSE_LEFTOVERS_EXE = fake;
  process.env.FAKE_SW_STATE = state;
  const write = (docs) => fs.writeFileSync(state, JSON.stringify({ docs, log: [] }));
  const read = () => JSON.parse(fs.readFileSync(state, 'utf8'));
  const quiet = (fn) => { const log = console.log; console.log = () => {}; try { return fn(); } finally { console.log = log; } };
  try {
    write(before);
    quiet(() => commandSnapshot([snapshot]));
    check('the documents open before the run are written down', JSON.stringify(readSpared(snapshot)) === JSON.stringify(spared));

    write(open.map(d => Object.assign({}, d)));
    const code = quiet(() => commandCloseAll(['--spare', snapshot]));
    const after = read();
    check('close-all leaves exactly the documents of before the run, and exits 0', code === 0 && JSON.stringify(titles(after.docs)) === JSON.stringify(spared), JSON.stringify(titles(after.docs)));
    check('and it asked for the drawing before the assembly before the parts', after.log.length === 1 && after.log[0][0] === 'Drawing1 - Sheet1' && after.log[0][1] === 'box.SLDASM');

    const again = quiet(() => commandCloseAll(['--spare', snapshot]));
    check('a second close-all with nothing to close does nothing and exits 0', again === 0 && read().log.length === 1);

    // a document that does not go the first time: the second pass takes it
    write(open.map(d => Object.assign({}, d, d.title === 'lid.SLDPRT' ? { stubborn: true } : {})));
    const second = quiet(() => commandCloseAll(['--spare', snapshot]));
    check('a document that stays after the first pass is closed by the second', second === 0 && read().docs.length === before.length && read().log.length === 2);

    // a document that never goes: the run must be told, with its name (seen red)
    write(open.map(d => Object.assign({}, d, d.title === 'TUBE SQUARE.SLDLFP' ? { stuck: true } : {})));
    const out = [];
    const keep = console.log;
    console.log = (m) => out.push(String(m));
    let stuck;
    try { stuck = commandCloseAll(['--spare', snapshot]); } finally { console.log = keep; }
    check('a document that never closes makes close-all exit 1 and names it (seen red)', stuck === 1 && out.some(l => /STILL OPEN/.test(l) && /TUBE SQUARE\.SLDLFP/.test(l)), out.join(' | '));
    check('and what could be closed was closed', read().docs.length === before.length + 1);

    // a CloseLeftovers that does not know the modes is a failure, not an empty list
    const old = path.join(dir, 'old-close-leftovers.js');
    fs.writeFileSync(old, "console.log('0 to close, 3 kept.'); process.exit(0);");
    process.env.CLOSE_LEFTOVERS_EXE = old;
    let message = '';
    try { listOpen(); } catch (e) { message = e.message; }
    check('a build of CloseLeftovers from before the modes is refused, not read as «nothing open»', /rebuild/.test(message), message);
  } finally {
    if (saveEnv.exe === undefined) delete process.env.CLOSE_LEFTOVERS_EXE; else process.env.CLOSE_LEFTOVERS_EXE = saveEnv.exe;
    if (saveEnv.state === undefined) delete process.env.FAKE_SW_STATE; else process.env.FAKE_SW_STATE = saveEnv.state;
    fs.rmSync(dir, { recursive: true, force: true });
  }

  // the runner uses it: before each session and after it, never after a pipe
  const runner = fs.readFileSync(path.join(__dirname, 'run-autonomous.ps1'), 'utf8');
  check('the runner snapshots the documents before the run and closes everything before and after each session',
        /session-documents\.js/.test(runner) && /--snapshot/.test(runner) && (runner.match(/--close-all/g) || []).length >= 1 && /CloseAll/.test(runner));

  console.log(bad ? 'self-test: ' + bad + ' failed' : 'self-test: all held');
  return bad === 0;
}

module.exports = { documentsToClose, closeAll, listOpen, selfTest, FAKE };
if (require.main !== module) return;

const argv = process.argv.slice(2);
try {
  if (argv[0] === '--self-test') process.exit(selfTest() ? 0 : 1);
  else if (argv[0] === '--list') process.exit(commandList());
  else if (argv[0] === '--snapshot') process.exit(commandSnapshot(argv.slice(1)));
  else if (argv[0] === '--close-all') process.exit(commandCloseAll(argv.slice(1)));
  else fail('usage: --list | --snapshot <file.json> | --close-all --spare <file.json> | --self-test');
} catch (e) { fail(e.message, 2); }
