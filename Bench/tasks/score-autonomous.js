#!/usr/bin/env node
// =============================================================================
//  The score of the autonomous run (decision D9, entry W3.1-bis.5)
// =============================================================================
//
//  Reads what run-autonomous.ps1 kept and the table "How it is measured" of
//  each task, and says for every session RIGHT, WRONG or NOT DONE (the three
//  outcomes of Banco/tasks/README.md, never summed), and for every task the
//  median of its repeats.
//
//  Where a row is read:
//    volume_mm3, bodies, components, bounding_box_mm, diameters_mm
//        on the FINISHED part: <task>-<n>.state.json, the state of the
//        document in front when the session ended (bodies is bodies_total,
//        components the length of the list, diameters_mm the diameters of
//        the cylindrical faces, smallest first);
//    measurements.<field> (of the `<operation>` step)
//        in the transcript: the answer of the last step with that operation
//        that succeeded; without an operation, the last step whose answer has
//        the field.
//  A row with a qualifier the scorer does not read ("volume_mm3 (base)", one
//  component of an assembly) is listed BY HAND, with the session: it is not
//  counted as held, and a session with such a row is RIGHT only once a person
//  has read it (the score says "right, N rows by hand").
//
//      node Banco/tasks/score-autonomous.js <run folder>
//      node Banco/tasks/score-autonomous.js --compare <run before> <run after>
//      node Banco/tasks/score-autonomous.js --read-state <bridge.exe> <out.json>
//      node Banco/tasks/score-autonomous.js --bridge-info <bridge.exe>
//      node Banco/tasks/score-autonomous.js --self-test
//
//  --read-state asks the bridge for its tools first and reads the state with
//  the tool that bridge has (inspect_part, or guarda_il_pezzo before the
//  rename); --bridge-info prints its version and protocol (en or it), which
//  run-autonomous.ps1 records in run.json.
//
//  --compare is the threshold of D9: the median per task (1 = right, 0 =
//  not) of the run after against the run before; the run holds when at most
//  one task of fifteen is worse.
//
//  THE RUN BEFORE SPEAKS THE PROTOCOL OF BEFORE. D9 compares the bridge before
//  the rename (9384b2c: `esiti`, `riuscita`, `misure`, `corpi_totali`) with the
//  bridge after it; a scorer that read only the English keys would find no
//  outcome in the run before, call every session NOT DONE, and the comparison
//  «at most one task worse» could not fail. So every answer and every state is
//  read through the name map first (Controlli/lingua/name-map.tsv, the rows
//  response_field, operation, enum_value and response_value, with the sites of
//  Controlli/lingua/window-2-surface.js where a field has two English names):
//  an answer of the bridge before becomes the answer the bridge after would
//  write, and an English answer goes through unchanged. score.json records
//  which protocol each session spoke, and the scorer refuses to score a run in
//  which no session gave a single outcome, and --compare refuses a score.json
//  written by a scorer that did not read the protocol of before.
//
//  Exit 0 on a score written (or a comparison that holds), 1 when the
//  comparison does not hold or the self-test fails, 2 on a bad argument, a run
//  with no outcome read, or a score.json of an older scorer.
// =============================================================================
'use strict';
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const SCORER_VERSION = 2;

function fail(m) { console.error('score-autonomous: ' + m); process.exit(2); }

// ── the protocol of before, read through the map ─────────────────────────
// The direction Italian -> English of window 2 (its tables of sites included).
let DIRECTION = null;
function direction() {
  if (DIRECTION) return DIRECTION;
  const root = path.resolve(__dirname, '..', '..');
  const w = require(path.join(root, 'Controlli', 'lingua', 'window-2-surface.js'));
  DIRECTION = w.direction(w.loadMap(root), false, null);
  return DIRECTION;
}
// Keys only the bridge before the rename writes: an answer or a state with one
// of them speaks the Italian protocol.
const ITALIAN_MARKS = ['esiti', 'riuscita', 'misure', 'corpi_totali', 'facce', 'stato_dopo', 'operazione'];
function speaksItalian(v) {
  if (Array.isArray(v)) return v.some(speaksItalian);
  if (!v || typeof v !== 'object') return false;
  return Object.keys(v).some(k => ITALIAN_MARKS.includes(k) || speaksItalian(v[k]));
}
// An answer (or a state) of the bridge before, as the bridge after writes it:
// every key by the response_field rows (a field that depends on the field it
// sits in, or on the operation that writes it, by the sites of the window),
// the operation of a step by the operation rows, a value by the enum of its
// key or by the response_value rows. English keys and values have no row and
// go through as they are.
function toEnglish(v, op, parent) {
  const d = direction();
  if (Array.isArray(v)) return v.map(x => toEnglish(x, op, parent));
  if (!v || typeof v !== 'object') return v;
  const opHere = typeof v.operazione === 'string' ? v.operazione : op;
  const out = {};
  for (const [k, val] of Object.entries(v)) {
    let key = d.field.get(k) || k;
    if (parent && d.fieldInside.has(parent) && d.fieldInside.get(parent).has(k)) key = d.fieldInside.get(parent).get(k);
    else if (parent === 'misure' && opHere && d.fieldByOp.has(opHere) && d.fieldByOp.get(opHere).has(k)) key = d.fieldByOp.get(opHere).get(k);
    let now;
    if (k === 'operazione' && typeof val === 'string') now = d.op.get(val) || val;
    else if (typeof val === 'string') {
      const e = d.enumByArg.get(k);
      now = e && e.has(val) ? e.get(val) : d.rvalue.get(val) || val;
    } else now = toEnglish(val, opHere, k);
    out[key] = now;
  }
  return out;
}
function readAnswer(a) { return speaksItalian(a) ? { answer: toEnglish(a, null, null), italian: true } : { answer: a, italian: false }; }

// ── the table of a task ─────────────────────────────────────────────────
function tableOf(md) {
  const lines = md.split(/\r?\n/);
  const at = lines.findIndex(l => /^##\s+How it is measured/i.test(l));
  if (at < 0) return [];
  const rows = [];
  for (let i = at + 1; i < lines.length; i++) {
    const l = lines[i];
    if (/^##\s/.test(l)) break;
    if (!/^\|/.test(l) || /^\|\s*-/.test(l) || /^\|\s*what\s*\|/i.test(l)) continue;
    const c = l.split('|').slice(1, -1).map(x => x.trim());
    if (c.length < 3) continue;
    rows.push({ what: c[0], expected: c[1], tolerance: c[2], why: c[3] || '' });
  }
  return rows;
}

function number(s) {
  const t = String(s).trim().replace(/,(?=\d{3}\b)/g, '').replace(/\s/g, '');
  return /^-?\d+(\.\d+)?$/.test(t) ? Number(t) : null;
}

// What a row asks: { path, op, kind: 'number'|'list'|'listAnyOrder'|'bool'|'text'|'present'|'absent'|'gt'|'lt', value, tol }
function readRow(r) {
  const m = /^(\S+?)(?:\s+\((.+)\))?$/.exec(r.what.replace(/`/g, ''));
  if (!m) return { byHand: 'cannot read «' + r.what + '»' };
  const p = m[1];
  const qual = m[2] || '';
  const opm = /^of the (\S+) step$/.exec(qual);
  const op = opm ? opm[1] : null;
  // «(the height, ...)», «(the length of the bracket)»: one of the three sides
  // of the bounding box, whichever it is
  const oneSide = !op && /^the /.test(qual) && p === 'bounding_box_mm';
  if (qual && !op && !oneSide) return { byHand: 'qualifier «' + qual + '»' };
  const tol = number(String(r.tolerance).replace(/^±\s*/, '')) || 0;
  const e = r.expected.trim().replace(/^"(.*)"$/, '$1');
  let kind, value;
  let mm;
  if ((mm = /^\[(.*)\](\s+in some order)?$/.exec(e))) { kind = mm[2] ? 'listAnyOrder' : 'list'; value = mm[1].split(',').map(x => number(x)); }
  else if (e === 'true' || e === 'false') { kind = 'bool'; value = e === 'true'; }
  else if (e === 'present' || e === 'absent') { kind = e; }
  else if ((mm = /^([<>])\s*(.+)$/.exec(e))) { kind = mm[1] === '>' ? 'gt' : 'lt'; value = number(mm[2]); }
  else if (number(e) !== null) { kind = 'number'; value = number(e); }
  else { kind = 'text'; value = e; }
  return { path: p, op, kind, value, tol, oneSide };
}

// ── the transcript ───────────────────────────────────────────────────────
// Every outcome of every step the bridge answered, in order, in the English
// protocol (an answer of the bridge before is read through the map:
// `out.italian` says how many were).
function outcomesOf(jsonl) {
  const out = [];
  out.italian = 0;
  for (const line of jsonl.split(/\r?\n/)) {
    if (!line.trim()) continue;
    let o;
    try { o = JSON.parse(line); } catch (e) { continue; }
    const content = o && o.message && Array.isArray(o.message.content) ? o.message.content : [];
    for (const c of content) {
      if (c.type !== 'tool_result') continue;
      const parts = Array.isArray(c.content) ? c.content : [{ type: 'text', text: String(c.content || '') }];
      for (const part of parts) {
        if (part.type !== 'text') continue;
        let raw;
        try { raw = JSON.parse(part.text); } catch (e) { continue; }
        const r = readAnswer(raw);
        const a = r.answer;
        if (a && Array.isArray(a.outcomes)) { if (r.italian) out.italian++; for (const s of a.outcomes) out.push(s); }
      }
    }
  }
  return out;
}

function stateValue(state, p) {
  if (!state) return undefined;
  if (p === 'bodies') return state.bodies_total;
  if (p === 'components') return Array.isArray(state.components) ? state.components.length : undefined;
  if (p === 'diameters_mm') {
    if (!Array.isArray(state.faces) || state.truncated) return undefined;
    return state.faces.filter(f => typeof f.diameter_mm === 'number').map(f => f.diameter_mm).sort((a, b) => a - b);
  }
  return state[p];
}

function holds(row, got) {
  const near = (a, b) => typeof a === 'number' && Math.abs(a - b) <= row.tol + 1e-9;
  switch (row.kind) {
    case 'number': return row.oneSide && Array.isArray(got) ? got.some(x => near(x, row.value)) : near(got, row.value);
    case 'list': return Array.isArray(got) && got.length === row.value.length && got.every((x, i) => near(x, row.value[i]));
    case 'listAnyOrder': {
      if (!Array.isArray(got) || got.length !== row.value.length) return false;
      const a = got.slice().sort((x, y) => x - y), b = row.value.slice().sort((x, y) => x - y);
      return a.every((x, i) => near(x, b[i]));
    }
    case 'bool': return got === row.value;
    case 'present': return got !== undefined && got !== null && got !== '';
    case 'absent': return got === undefined || got === null;
    case 'gt': return typeof got === 'number' && got > row.value;
    case 'lt': return typeof got === 'number' && got < row.value;
    default: return String(got) === row.value;
  }
}

function scoreSession(rows, outcomes, state) {
  if (!outcomes.some(s => s.succeeded)) return { outcome: 'NOT DONE', rows: [] };
  const done = [];
  for (const r of rows) {
    const row = readRow(r);
    if (row.byHand) { done.push({ what: r.what, byHand: row.byHand }); continue; }
    let got;
    if (row.path.startsWith('measurements.')) {
      const f = row.path.slice('measurements.'.length);
      const s = outcomes.filter(x => x.succeeded && x.measurements && (row.op ? x.operation === row.op : f in x.measurements)).pop();
      got = s ? s.measurements[f] : undefined;
      if (row.kind !== 'absent' && !s) { done.push({ what: r.what, held: false, got: '(no step ' + (row.op || 'with this field') + ')' }); continue; }
    } else got = stateValue(state, row.path);
    if (got === undefined && !row.path.startsWith('measurements.')) {
      done.push({ what: r.what, byHand: state ? 'not in the state of the finished part' : 'no state of the finished part' });
      continue;
    }
    done.push({ what: r.what, held: holds(row, got), got });
  }
  const wrong = done.some(d => d.held === false);
  const byHand = done.filter(d => d.byHand).length;
  return { outcome: wrong ? 'WRONG' : byHand ? 'RIGHT, ' + byHand + ' rows by hand' : 'RIGHT', rows: done };
}

function median(xs) { const s = xs.slice().sort((a, b) => a - b); return s.length ? s[Math.floor((s.length - 1) / 2)] : null; }

function scoreRun(dir) {
  const runFile = path.join(dir, 'run.json');
  if (!fs.existsSync(runFile)) fail('no run.json in ' + dir);
  const run = JSON.parse(fs.readFileSync(runFile, 'utf8').replace(/^﻿/, ''));
  // the tables are the ones of this folder, whatever the language the task
  // was asked in: the measure of a task is the same in both
  const tasksDir = __dirname;
  const perTask = {};
  for (const f of fs.readdirSync(dir).filter(x => /^\d\d_.+-\d+\.jsonl$/.test(x)).sort()) {
    const m = /^(.+)-(\d+)\.jsonl$/.exec(f);
    const task = m[1];
    const mdFile = path.join(tasksDir, task + '.md');
    const rows = fs.existsSync(mdFile) ? tableOf(fs.readFileSync(mdFile, 'utf8')) : [];
    const stateFile = path.join(dir, task + '-' + m[2] + '.state.json');
    const rawState = fs.existsSync(stateFile) ? JSON.parse(fs.readFileSync(stateFile, 'utf8').replace(/^﻿/, '')) : null;
    const st = readAnswer(rawState);
    const outcomes = outcomesOf(fs.readFileSync(path.join(dir, f), 'utf8'));
    const s = scoreSession(rows, outcomes, st.answer);
    const protocol = outcomes.italian > 0 || st.italian ? 'it (read through the name map)' : outcomes.length || rawState ? 'en' : 'nothing read';
    (perTask[task] = perTask[task] || []).push(Object.assign({ n: Number(m[2]), outcomes_read: outcomes.length, protocol }, s));
  }
  // A run in which not one session gave an outcome is not a run of NOT DONE:
  // it is a run the scorer cannot read (another protocol, a bridge that did
  // not answer). Scoring it would make every comparison hold.
  const sessions = [].concat(...Object.values(perTask));
  if (sessions.length && sessions.every(x => x.outcomes_read === 0))
    fail('no session of ' + dir + ' gave a single outcome of a step: the answers are not in a protocol this scorer reads, or the bridge never answered. Not scored.');
  const summary = {};
  for (const [task, list] of Object.entries(perTask)) {
    summary[task] = { sessions: list.map(x => x.outcome), median_right: median(list.map(x => x.outcome === 'RIGHT' ? 1 : 0)),
      protocols: [...new Set(list.map(x => x.protocol))] };
    console.log(task + ': ' + list.map(x => '#' + x.n + ' ' + x.outcome).join(', ') + '  (median ' + summary[task].median_right + '; ' + summary[task].protocols.join(', ') + ')');
    for (const x of list) for (const r of x.rows) if (r.byHand || r.held === false) console.log('    #' + x.n + ' ' + r.what + ': ' + (r.byHand ? 'BY HAND (' + r.byHand + ')' : 'got ' + JSON.stringify(r.got)));
  }
  fs.writeFileSync(path.join(dir, 'score.json'), JSON.stringify({ scorer: SCORER_VERSION, run, summary, detail: perTask }, null, 1));
  console.log('score written: ' + path.join(dir, 'score.json'));
  return summary;
}

function compare(before, after) {
  const read = (dir) => {
    const j = JSON.parse(fs.readFileSync(path.join(dir, 'score.json'), 'utf8'));
    if (j.scorer !== SCORER_VERSION) fail(path.join(dir, 'score.json') + ' was written by a scorer that did not read the protocol of before the rename (scorer ' + (j.scorer || 1) + '): score the run again');
    return j;
  };
  const ja = read(before), jb = read(after);
  const protocols = (j) => [...new Set([].concat(...Object.values(j.summary).map(t => t.protocols || [])))].join(', ');
  console.log('before: ' + protocols(ja) + '; after: ' + protocols(jb) + ' (both scored on the English protocol)');
  const a = ja.summary;
  const b = jb.summary;
  const worse = Object.keys(a).filter(t => b[t] && b[t].median_right < a[t].median_right);
  const missing = Object.keys(a).filter(t => !b[t]);
  for (const t of Object.keys(a).sort()) console.log(t + ': ' + a[t].median_right + ' -> ' + (b[t] ? b[t].median_right : '(not run)'));
  const ok = worse.length <= 1 && missing.length === 0;
  console.log((ok ? 'D9 HOLDS' : 'D9 DOES NOT HOLD') + ': ' + worse.length + ' task(s) worse' + (worse.length ? ' (' + worse.join(', ') + ')' : '') +
              (missing.length ? '; not run after: ' + missing.join(', ') : '') + '; at most 1 of 15 allowed');
  return ok;
}

// A fresh bridge over stdio: initialize, tools/list and, when asked, the tool
// that reads the state, by the name THAT bridge gives it (inspect_part after
// the rename, guarda_il_pezzo before it: the run before uses the bridge of
// before, and asking it for inspect_part would read no state at all).
const INSPECT = { en: 'inspect_part', it: 'guarda_il_pezzo' };
function askBridge(bridge, withState) {
  const req = (id, method, params) => JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n';
  let input = req(1, 'initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'score-autonomous', version: '1' } }) +
    JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n' + req(2, 'tools/list', {});
  if (withState) input += req(3, 'tools/call', { name: INSPECT.en, arguments: {} }) + req(4, 'tools/call', { name: INSPECT.it, arguments: {} });
  const r = cp.spawnSync(bridge, [], { input, encoding: 'utf8', timeout: 240000, cwd: path.dirname(bridge), windowsHide: true, maxBuffer: 64 << 20 });
  const by = {};
  for (const line of String(r.stdout || '').split(/\r?\n/)) {
    let o;
    try { o = JSON.parse(line); } catch (e) { continue; }
    if (o && o.id !== undefined) by[o.id] = o;
  }
  const info = by[1] && by[1].result && by[1].result.serverInfo ? by[1].result.serverInfo : {};
  const names = by[2] && by[2].result && Array.isArray(by[2].result.tools) ? by[2].result.tools.map(t => t.name) : [];
  const protocol = names.includes(INSPECT.en) ? 'en' : names.includes(INSPECT.it) ? 'it' : 'unknown';
  let state = null;
  const answer = by[protocol === 'it' ? 4 : 3];
  if (withState && answer && answer.result && !answer.result.isError) {
    state = answer.result.structuredContent || null;
    if (!state && Array.isArray(answer.result.content) && answer.result.content[0]) { try { state = JSON.parse(answer.result.content[0].text); } catch (e) { /* text only */ } }
  }
  return { version: info.version || null, protocol, tool: protocol === 'unknown' ? null : INSPECT[protocol], state };
}

// The state of the document in front, read by a fresh bridge over stdio.
function readState(bridge, outFile) {
  const b = askBridge(bridge, true);
  fs.writeFileSync(outFile, JSON.stringify(b.state, null, 1));
  console.log('    state of the finished part: ' + (b.state ? outFile + ' (' + b.tool + ', protocol ' + b.protocol + ')' : 'not read (bridge protocol ' + b.protocol + '; see the bridge)'));
}

function selfTest() {
  let bad = 0;
  const check = (name, ok) => { console.log((ok ? '  ok  ' : '  XX  ') + name); if (!ok) bad++; };
  const md = '## How it is measured\n\n| what | expected value | tolerance | why |\n|---|---|---|---|\n' +
    '| volume_mm3 | 67,782 | ± 200 | x |\n| bodies | 1 | — | x |\n| diameters_mm | [9, 9] | ± 0.02 | x |\n' +
    '| measurements.kind (of the `hem` step) | closed | — | x |\n| measurements.exported_anyway (of the `export_stl` step) | absent | — | x |\n' +
    '| measurements.byte (of the `export_stl` step) | > 0 | — | x |\n| volume_mm3 (base) | 5 | — | x |\n\n## What this measurement does not see\n';
  const rows = tableOf(md);
  check('the table has its seven rows', rows.length === 7);
  check('a number with a thousands comma is read', readRow(rows[0]).value === 67782 && readRow(rows[0]).tol === 200);
  check('a qualifier the scorer does not read is by hand', !!readRow(rows[6]).byHand);
  const state = { volume_mm3: 67700, bodies_total: 1, faces: [{ diameter_mm: 9.01 }, { normal: [0, 0, 1] }, { diameter_mm: 8.99 }] };
  const outcomes = [{ operation: 'hem', succeeded: true, measurements: { kind: 'closed' } },
                    { operation: 'export_stl', succeeded: true, measurements: { byte: 684 } }];
  const s = scoreSession(rows.slice(0, 6), outcomes, state);
  check('a session that holds every row is RIGHT', s.outcome === 'RIGHT');
  const s2 = scoreSession(rows.slice(0, 6), outcomes, Object.assign({}, state, { bodies_total: 2 }));
  check('two bodies make it WRONG', s2.outcome === 'WRONG');
  const s3 = scoreSession(rows, outcomes, state);
  check('a row by hand keeps it from a plain RIGHT', s3.outcome === 'RIGHT, 1 rows by hand');
  check('no successful step is NOT DONE', scoreSession(rows, [{ operation: 'hem', succeeded: false }], state).outcome === 'NOT DONE');
  const jsonl = JSON.stringify({ type: 'user', message: { content: [{ type: 'tool_result', content: [{ type: 'text', text: JSON.stringify({ outcomes }) }] }] } });
  check('the outcomes are read from a transcript', outcomesOf(jsonl).length === 2);
  // the run before: the bridge of 9384b2c answers and describes the part in
  // the Italian protocol, and the same session must score the same
  const outcomesIt = [{ operazione: 'orlo', riuscita: true, misure: { tipo: 'chiuso' } },
                      { operazione: 'esporta_stl', riuscita: true, misure: { byte: 684 } }];
  const jsonlIt = JSON.stringify({ type: 'user', message: { content: [{ type: 'tool_result', content: [{ type: 'text', text: JSON.stringify({ esiti: outcomesIt }) }] }] } });
  const stateIt = { volume_mm3: 67700, corpi_totali: 1, facce: [{ diametro_mm: 9.01 }, { normale: [0, 0, 1] }, { diametro_mm: 8.99 }] };
  const readIt = outcomesOf(jsonlIt);
  check('the outcomes of the bridge before the rename are read, through the map', readIt.length === 2 && readIt.italian === 1 &&
        readIt[0].operation === 'hem' && readIt[0].succeeded === true && readIt[0].measurements.kind === 'closed');
  check('the state of before is read through the map', readAnswer(stateIt).italian && readAnswer(stateIt).answer.bodies_total === 1);
  check('a session of the bridge before that holds every row is RIGHT, not NOT DONE',
        scoreSession(rows.slice(0, 6), readIt, readAnswer(stateIt).answer).outcome === 'RIGHT');
  check('an English answer goes through the reading unchanged', !readAnswer({ outcomes }).italian && readAnswer({ outcomes }).answer.outcomes === outcomes);
  check('the median of 1, 0, 1 is 1', median([1, 0, 1]) === 1);
  console.log(bad ? 'self-test: ' + bad + ' failed' : 'self-test: all held');
  return bad === 0;
}

const argv = process.argv.slice(2);
if (argv[0] === '--self-test') process.exit(selfTest() ? 0 : 1);
else if (argv[0] === '--read-state') { if (argv.length !== 3) fail('--read-state <bridge> <out.json>'); readState(argv[1], argv[2]); }
else if (argv[0] === '--bridge-info') { if (argv.length !== 2) fail('--bridge-info <bridge>'); const b = askBridge(argv[1], false); console.log(JSON.stringify({ version: b.version, protocol: b.protocol })); process.exit(b.protocol === 'unknown' ? 2 : 0); }
else if (argv[0] === '--compare') { if (argv.length !== 3) fail('--compare <run before> <run after>'); process.exit(compare(argv[1], argv[2]) ? 0 : 1); }
else if (argv.length === 1) scoreRun(argv[0]);
else fail('usage: <run folder> | --compare <before> <after> | --read-state <bridge> <out> | --self-test');
