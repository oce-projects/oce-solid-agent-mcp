#!/usr/bin/env node
// =============================================================================
//  The prerequisites of the autonomous run (decision D9, entry W3.1-bis.5)
// =============================================================================
//
//  Seven of the fifteen tasks start from the result of an earlier one: «the
//  L-bracket of task 01», «the tube stand of task 07». Each session runs in an
//  empty folder, so the model found a box or nothing, said «the bracket is not
//  there» and stopped: the run of 6/10 gave NOT DONE on tasks 09-15 for that
//  reason and for no reason about the model.
//
//  THE FAIR FIX, AND WHAT IT LEAVES UNMEASURED. The runner builds the part the
//  task talks about BEFORE the session, through the same bridge the session
//  uses, from a reference plan written in the format of the bench cases
//  (Bench/tasks/prerequisites/), saves it in the folder of the session and
//  leaves it open in front. The text of the task is not changed and the
//  measure of the task is not changed: the model is still asked for exactly
//  what it was asked for, it only finds what the task already says is there.
//  What is NOT measured any more is whether the model can build the earlier
//  part: tasks 01-08 measure that.
//
//  The reference plan is checked against the numbers of its task (the
//  `expect` entries) BEFORE the session starts. A prerequisite that is not
//  the part the task describes would make the session measure another part,
//  so the session is not run, and the run says why.
//
//      node Bench/tasks/prerequisite.js --check [--bridge <exe>] [--catalogue-before <file>] [plan...]
//      node Bench/tasks/prerequisite.js --build <bridge.exe> <plan> <folder> [--out <result.json>] [--dir <plans folder>]
//      node Bench/tasks/prerequisite.js --self-test
//
//  --check needs no SOLIDWORKS: the plans against the catalogue (every
//  operation, every argument, every enum value, every reference between
//  steps, every expectation path), the tasks against the index, and, with
//  --bridge, the dry run of the bridge itself with OCE_NO_ATTACH set (it does
//  not attach to SOLIDWORKS). A bridge whose folder holds the catalogue of before
//  the rename (the bridge of 9384b2c) is never started: the plans translated to
//  its protocol are checked against that catalogue (--catalogue-before names it).
//
//  --build NEEDS SOLIDWORKS. It runs ONE bridge from the first step to the
//  last (the identifiers of faces live in the process that read them), builds
//  every component of the plan, saves it in <folder>, builds the assembly if
//  the plan has one, saves the document of the plan as <folder>/<plan>.sldprt
//  or .sldasm, reads the state and evaluates the expectations. It exits 0 when
//  everything held, 1 when a step or an expectation did not.
//
//  THE PLAN FORMAT is the one of the cases (Bench/case-keys.json): `start`, or
//  `assembly.components[]` with `name`, `position_mm` and `steps`, and `steps`
//  with `operation`, `arguments`, `expect`. What the runner adds, none of it a
//  new key:
//    - a selector (`@face(normal=down)`, `@body(name=...)`) is resolved on the
//      state read just before the step, with the grammar of the bench
//      (Bench/Expectations.cs, Select: `=` compares as text without case,
//      `~=` is «within 0.5», `n=` takes the n-th, x, y and z read point_mm);
//    - the bridge takes the steps of a plan in ONE call and a reference
//      (`face_ref: {step: 1, ...}`) names a step of the same call, so the steps
//      go in groups: a group starts at a step that has a selector (its state
//      must be read first), and a reference may only name a step of its own
//      group (it is renumbered inside the call, and a reference to an earlier
//      group is refused before anything runs);
//    - `{PROFILES}` is the folder of the weld profile library: the variable
//      OCE_WELDMENT_PROFILES of the bench, else the folder of the profile the bridge
//      lists for «TUBO STRUTTURALE QUADRO»; `{WORK}` is the folder of the session.
//      Any other `{NAME}` in a plan is a problem (--check), not a literal: the
//      plans of before the rename carried {PROFILI}, and it would have reached the
//      bridge as a path;
//    - the expectations are a subset of the bench ones: volume_mm3, bodies,
//      bounding_box_mm, diameters_mm, state.<path> read on the state AFTER the
//      group, measurements.<path> read on the answer of the step itself. A
//      state path on a step that is not the last of its group would read the
//      state after later steps too: --check refuses it.
//
//  THE PROTOCOL OF BEFORE. D9 runs the bridge of before the rename too. The
//  plans are written in English; for that bridge every step is translated by
//  the reverse run of window 2 (Checks/language/window-2-surface.js, the map
//  read from English to Italian) and every answer is read back through the
//  same map, as the scorer does. The only Italian written in this file is the
//  table MATE_VALUES_BEFORE: the values of a mate (`variant` and `alignment`) are
//  not enum values of the catalogue and the name map has no row for them (the
//  schema describes them in prose), so a plan translated by the map alone would
//  have reached that bridge with «distance» where it wants the word of before,
//  and it would have refused it. The table is explicit, a value that is not in
//  it stops the translation, and it leaves the autonomous run together with the
//  bridge of before (W3.1-bis.11, class `transition`).
//  The bridge of today takes English values since W3.1-bis.10b and refuses the
//  others; --check reads them (a plan with a value of before is a problem).
//
//  PREREQ_TRACE=<file> appends every request to the bridge and every answer to
//  that file, one JSON line each: what a failed build read is then on the disk.
//
//  Exit 0 on success, 1 when a check or a build failed, 2 on a bad argument or
//  a bridge that cannot run here.
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..');
const PLANS_DIR = path.join(__dirname, 'prerequisites');

function fail(m) { console.error('prerequisite: ' + m); process.exit(2); }
function readJson(file) { return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^﻿/, '')); }
const isObject = (v) => v !== null && typeof v === 'object' && !Array.isArray(v);

// ── the plans ──────────────────────────────────────────────────────────────
function loadIndex(dir) { return readJson(path.join(dir || PLANS_DIR, 'index.json')); }
function listPlans(dir) {
  return fs.readdirSync(dir || PLANS_DIR).filter(f => /^\d\d_.+\.json$/.test(f)).map(f => f.replace(/\.json$/, '')).sort();
}
function loadPlan(name, dir) {
  const p = readJson(path.join(dir || PLANS_DIR, name + '.json'));
  if (p.name !== name) throw new Error('the file is «' + name + '» and inside it says «' + p.name + '»');
  return p;
}

// The step lists of a plan, in the order they run: each component, then the
// document of the plan itself.
function stepLists(plan) {
  const lists = [];
  const comps = plan.assembly && Array.isArray(plan.assembly.components) ? plan.assembly.components : [];
  for (const c of comps) {
    if (Array.isArray(c.components)) throw new Error('component «' + c.name + '» is a sub-assembly: the runner builds parts only');
    lists.push({ kind: 'component', name: c.name, steps: c.steps || [], component: c });
  }
  lists.push({ kind: 'main', name: plan.name, steps: plan.steps || [] });
  return lists;
}

// ── selectors: the grammar of Bench/Expectations.cs ───────────────────────
const STATE_FIELD = { axis: 'along', length: 'length_mm', area: 'area_mm2', normal: 'facing' };
const FAMILY = { face: 'faces', edge: 'edges', body: 'bodies', feature: 'feature' };

function parseSelector(expression) {
  const opens = expression.indexOf('(');
  if (opens < 0 || !expression.endsWith(')')) throw new Error('crooked selector «' + expression + '»');
  const kind = expression.slice(1, opens);
  const conditions = [];
  for (const part of expression.slice(opens + 1, -1).split(',')) {
    const t = part.trim();
    if (!t) continue;
    const approx = t.includes('~=');
    const at = approx ? t.indexOf('~=') : t.indexOf('=');
    if (at < 0) throw new Error('condition without «=»: «' + t + '»');
    conditions.push({ field: t.slice(0, at).trim(), value: t.slice(at + (approx ? 2 : 1)).trim(), approx });
  }
  return { kind, conditions };
}

function fieldValue(entry, field) {
  const axis = { x: 0, y: 1, z: 2 }[field];
  if (axis !== undefined) return Array.isArray(entry.point_mm) && entry.point_mm.length >= 3 ? entry.point_mm[axis] : undefined;
  return entry[STATE_FIELD[field] || field];
}

// The uid a selector names on a state, or an Error saying why not (zero, or
// more than one: choosing would be guessing, as in the bench).
function resolveSelector(expression, state) {
  const { kind, conditions } = parseSelector(expression);
  const family = FAMILY[kind];
  if (!family) throw new Error('«@' + kind + '» is not a selector the runner reads (it reads ' + Object.keys(FAMILY).map(k => '@' + k).join(', ') + ')');
  const entries = state && Array.isArray(state[family]) ? state[family] : null;
  if (!entries) throw new Error('the state has no «' + family + '»: ' + expression);
  const nth = conditions.find(c => c.field === 'n');
  const found = entries.filter(entry => conditions.every(c => {
    if (c.field === 'n') return true;
    const v = fieldValue(entry, c.field);
    if (v === undefined || v === null) return false;
    if (c.approx) {
      const a = Number(v), b = Number(c.value);
      return Number.isFinite(a) && Number.isFinite(b) && Math.abs(a - b) <= 0.5;
    }
    return String(v).toLowerCase() === c.value.toLowerCase();
  }));
  if (nth) {
    const k = Number(nth.value);
    if (!Number.isInteger(k) || k < 1 || k > found.length) throw new Error(expression + ': found ' + found.length + ', the plan asks for number ' + nth.value);
    return found[k - 1].uid;
  }
  const listed = family === 'faces' && facesCut(state) ? ' (the state lists only the ' + state.faces.length + ' largest faces of ' + state.faces_total + ': the face may not be listed)' : '';
  if (found.length === 0) throw new Error(expression + ': not found' + listed);
  if (found.length > 1) throw new Error(expression + ': found ' + found.length + '; choosing one would be guessing, add a condition');
  return found[0].uid;
}

function hasSelector(v) {
  if (typeof v === 'string') return v.startsWith('@');
  if (Array.isArray(v)) return v.some(hasSelector);
  if (isObject(v)) return Object.values(v).some(hasSelector);
  return false;
}
function mapStrings(v, fn) {
  if (typeof v === 'string') return fn(v);
  if (Array.isArray(v)) return v.map(x => mapStrings(x, fn));
  if (isObject(v)) { const o = {}; for (const [k, x] of Object.entries(v)) o[k] = mapStrings(x, fn); return o; }
  return v;
}

// ── groups of steps and references between steps ──────────────────────────
// A group starts at a step that has a selector, unless it is the first of its
// list: the state must be read before that step, so it cannot share a call
// with the steps before it.
function groupsOf(steps) {
  const groups = [];
  steps.forEach((step, i) => {
    if (groups.length === 0 || hasSelector(step.arguments)) groups.push({ first: i, steps: [] });
    groups[groups.length - 1].steps.push({ index: i, step });
  });
  return groups;
}

// A reference names a step by its number in the list (from 1); inside the
// call of its group it is the number in the group, and it must be a step
// BEFORE the one that names it (ownIndex, from 0, when it is known).
function rebaseRefs(args, groupFirst, label, ownIndex) {
  const walk = (v, key) => {
    if (Array.isArray(v)) return v.map(x => walk(x, key));
    if (!isObject(v)) return v;
    const o = {};
    for (const [k, x] of Object.entries(v)) o[k] = walk(x, k);
    if (key && /_ref$/.test(key) && Number.isInteger(v.step)) {
      const n = v.step - groupFirst;
      if (n < 1) throw new Error(label + ': «' + key + '» names step ' + v.step + ', which is in an earlier group (a selector starts a new call): move the step or drop the reference');
      if (ownIndex !== undefined && v.step > ownIndex) throw new Error(label + ': «' + key + '» names step ' + v.step + ', which is not before this step (' + (ownIndex + 1) + ')');
      o.step = n;
    }
    return o;
  };
  return walk(args, null);
}

// The faces of a state are cut to the largest ones when faces_total is there and
// larger than the list (the edges, the features and the equations have caps of
// their own, and any of them sets `truncated`).
function facesCut(state) {
  return typeof state.faces_total === 'number' && Array.isArray(state.faces) && state.faces_total > state.faces.length;
}

function stateRoot(what) { return /^(volume_mm3|bodies|bounding_box_mm|diameters_mm|state\.)/.test(what); }

// ── expectations ──────────────────────────────────────────────────────────
function walkPath(root, p) {
  let v = root;
  const re = /([^.\[\]]+)|\[(\d+)\]/g;
  let m;
  while ((m = re.exec(p)) !== null) {
    if (v === undefined || v === null) return undefined;
    v = m[2] !== undefined ? v[Number(m[2])] : v[m[1]];
  }
  return v;
}
function lookup(what, ctx) {
  if (what === 'volume_mm3') return ctx.state ? ctx.state.volume_mm3 : undefined;
  if (what === 'bodies') return ctx.state ? ctx.state.bodies_total : undefined;
  if (what === 'bounding_box_mm') return ctx.state ? ctx.state.bounding_box_mm : undefined;
  if (what === 'diameters_mm') {
    // Only a cut list of FACES hides a hole. `truncated` is the flag of the whole
    // state, and the 30 longest edges of the slotted plate (36 in all) set it
    // with every face listed: the live proof of 6/10 read «undefined» there on
    // both bridges. The faces are cut when the bridge writes faces_total (both
    // bridges do, DocumentState.MaxFaces).
    if (!ctx.state || !Array.isArray(ctx.state.faces) || facesCut(ctx.state)) return undefined;
    return ctx.state.faces.filter(f => typeof f.diameter_mm === 'number').map(f => f.diameter_mm).sort((a, b) => a - b);
  }
  if (what.startsWith('state.')) return walkPath(ctx.state, what.slice(6));
  if (what.startsWith('measurements.')) return walkPath(ctx.measurements, what.slice(13));
  return undefined;
}
function sameWithin(got, want, tol) {
  if (typeof want === 'number') return typeof got === 'number' && Math.abs(got - want) <= tol + 1e-9;
  if (Array.isArray(want)) return Array.isArray(got) && got.length === want.length && want.every((w, i) => sameWithin(got[i], w, tol));
  return got === want;
}
function evaluate(expectation, ctx) {
  const got = lookup(expectation.what, ctx);
  const tol = typeof expectation.tolerance === 'number' ? expectation.tolerance : 0;
  const out = { what: expectation.what, got, ok: false };
  if ('equal_to' in expectation) { out.expected = expectation.equal_to; out.tolerance = tol; out.ok = got !== undefined && sameWithin(got, expectation.equal_to, tol); }
  else if ('includes' in expectation || 'does_not_include' in expectation) {
    const has = (piece) => Array.isArray(got) ? got.some(x => String(x) === String(piece)) : typeof got === 'string' && got.includes(String(piece));
    const need = [].concat(expectation.includes || []), refuse = [].concat(expectation.does_not_include || []);
    out.expected = { includes: need, does_not_include: refuse };
    out.ok = got !== undefined && got !== null && need.every(has) && refuse.every(p => !has(p));
  } else { out.ok = false; out.error = 'the expectation has neither equal_to nor includes'; }
  return out;
}

// ── the catalogue ─────────────────────────────────────────────────────────
// The catalogue of a bridge: the English one (Tools/catalog.json, the one next to
// the bridge), or the one of the bridge of before the rename, whose keys are read
// from the map (the catalogue_key rows) so that no Italian name is written here.
function loadCatalogue(file, before) {
  const c = readJson(file);
  let kOperations = 'operations', kName = 'name';
  if (before) { const d = surface().toBefore; kOperations = d.ckey.get('operations'); kName = d.ckey.get('name'); }
  const by = new Map();
  for (const o of c[kOperations] || []) by.set(o[kName], o);
  return { by, file };
}

// Which catalogue sits next to a bridge, without starting it: catalog.json is the
// English one (and its bridge can dry-run with OCE_NO_ATTACH); another JSON file
// whose operations are under the key of before is the catalogue of before (and its
// bridge is never started here: it has no way to stay away from SOLIDWORKS).
function catalogueNextTo(exe) {
  const dir = path.dirname(exe);
  if (fs.existsSync(path.join(dir, 'catalog.json'))) return { kind: 'en', file: path.join(dir, 'catalog.json') };
  const kOperations = surface().toBefore.ckey.get('operations');
  for (const f of fs.readdirSync(dir).filter(x => /.json$/i.test(x))) {
    try { if (Array.isArray(readJson(path.join(dir, f))[kOperations])) return { kind: 'before', file: path.join(dir, f) }; } catch (e) { /* not a catalogue */ }
  }
  return null;
}
// The problems of ONE step against a catalogue (top-level arguments only; the
// dry run of the bridge looks at the rest).
function stepProblems(step, catalogue, label) {
  const out = [];
  const op = catalogue.by.get(step.operation);
  if (!op) return [label + ': the operation «' + step.operation + '» is not in the catalogue'];
  const props = (op.schema && op.schema.properties) || {};
  const args = step.arguments || {};
  for (const [k, v] of Object.entries(args)) {
    if (!(k in props)) { out.push(label + ': «' + k + '» is not an argument of «' + step.operation + '» (the bridge would ignore it)'); continue; }
    const e = props[k].enum;
    if (Array.isArray(e) && typeof v === 'string' && !v.startsWith('@') && !e.includes(v)) out.push(label + ': «' + k + '» = «' + v + '» is not one of ' + e.join(', '));
  }
  for (const r of (op.schema && op.schema.required) || []) {
    if (!(r in args)) out.push(label + ': «' + step.operation + '» needs «' + r + '»');
  }
  return out;
}

const EXPECTATION_KEYS = ['what', 'equal_to', 'includes', 'does_not_include', 'tolerance', 'tolerance_why', 'why'];

// Everything that can be said about a plan without SOLIDWORKS.
function planProblems(plan, catalogue, translate) {
  const problems = [];
  let lists;
  try { lists = stepLists(plan); } catch (e) { return [plan.name + ': ' + e.message]; }
  for (const list of lists) {
    const where = plan.name + ' · ' + (list.kind === 'component' ? 'component «' + list.name + '»' : 'document');
    if (list.steps.length === 0) problems.push(where + ': no steps');
    let steps;
    try { steps = translate ? translate(list.steps) : list.steps; } catch (e) { problems.push(where + ': ' + e.message); continue; }
    steps.forEach((s, i) => {
      for (const p of stepProblems(s, catalogue, where + ' · step ' + (i + 1))) problems.push(p);
    });
    // the values of a mate and the placeholders are read on the plan as written, once
    if (!translate) list.steps.forEach((s, i) => {
      for (const p of mateValueProblems(s, where + ' · step ' + (i + 1))) problems.push(p);
      for (const p of placeholderProblems(s, where + ' · step ' + (i + 1))) problems.push(p);
    });
    // references and expectations are read on the plan as written
    let groups;
    try { groups = groupsOf(list.steps); } catch (e) { problems.push(where + ': ' + e.message); continue; }
    for (const g of groups) {
      for (const { index, step } of g.steps) {
        const label = where + ' · step ' + (index + 1);
        try { rebaseRefs(step.arguments || {}, g.first, label, index); } catch (e) { problems.push(e.message); }
        for (const x of step.expect || []) {
          for (const k of Object.keys(x)) if (!EXPECTATION_KEYS.includes(k) && !k.startsWith('_')) problems.push(label + ': the expectation key «' + k + '» is not a key of the cases');
          if (typeof x.what !== 'string' || !(stateRoot(x.what) || /^measurements\./.test(x.what))) problems.push(label + ': «' + x.what + '» is not a path the runner reads');
          if (typeof x.what === 'string' && stateRoot(x.what) && index !== g.first + g.steps.length - 1)
            problems.push(label + ': «' + x.what + '» is read on the state after the whole group, so it must sit on the last step of its group (step ' + (g.first + g.steps.length) + '), or the state would include later steps');
          if (!('equal_to' in x) && !('includes' in x) && !('does_not_include' in x)) problems.push(label + ': an expectation with neither equal_to nor includes');
          if (typeof x.tolerance === 'number' && x.tolerance > 0 && !x.tolerance_why) problems.push(label + ': a tolerance without its tolerance_why (a tolerance with no reason is an expectation that widens until it passes)');
        }
        if ('must_fail' in step) problems.push(label + ': «must_fail» is a case of refusals, not a way to build a part');
      }
    }
    if (list.kind === 'main' && !list.steps.some(s => (s.expect || []).length)) problems.push(where + ': no step carries an expectation: a prerequisite that is not checked against its task is a guess');
  }
  return problems;
}

// The bracket of the pair is the bracket of task 01, step by step: one number
// changed in one place would otherwise leave the other behind.
function consistencyProblems(dir) {
  const problems = [];
  const names = listPlans(dir);
  if (names.includes('01_l_bracket') && names.includes('09_bracket_pair')) {
    const a = loadPlan('01_l_bracket', dir).steps.map(s => ({ operation: s.operation, arguments: s.arguments }));
    const pair = loadPlan('09_bracket_pair', dir);
    const c = pair.assembly.components.find(x => x.name === 'bracket');
    const b = c ? c.steps.map(s => ({ operation: s.operation, arguments: s.arguments })) : null;
    if (!b || JSON.stringify(a) !== JSON.stringify(b)) problems.push('the «bracket» of 09_bracket_pair is not, step by step, the plan 01_l_bracket');
  }
  return problems;
}

// The tasks that name another task («task 07») against the index: a task that
// depends on another and has no prerequisite is the defect this folder exists
// to remove, and it would come back the day a task is added.
function taskReferences(tasksDir) {
  const refs = {};
  for (const f of fs.readdirSync(tasksDir).filter(x => /^\d\d_.+\.md$/.test(x)).sort()) {
    const lines = fs.readFileSync(path.join(tasksDir, f), 'utf8').split(/\r?\n/);
    const quote = [];
    for (const l of lines) { const m = /^>\s?(.*)$/.exec(l); if (m) quote.push(m[1]); else if (quote.length) break; }
    const text = quote.join(' ');
    const found = [...text.matchAll(/\btask\s+(\d\d)\b/gi)].map(m => m[1]);
    if (found.length) refs[f.slice(0, 2)] = [...new Set(found)];
  }
  return refs;
}
function indexProblems(dir, tasksDir) {
  const problems = [];
  const index = loadIndex(dir);
  const plans = listPlans(dir);
  const refs = taskReferences(tasksDir);
  for (const [task, entry] of Object.entries(index.tasks || {})) {
    if (!plans.includes(entry.plan)) problems.push('task ' + task + ': the plan «' + entry.plan + '» is not in ' + dir);
    const named = refs[task] || [];
    if (!named.includes(entry.from_task)) problems.push('task ' + task + ' says it starts from task ' + entry.from_task + ' and its text names ' + (named.join(', ') || 'no other task'));
    const lastTask = Object.values(index.tasks).length;
    void lastTask;
  }
  for (const [task, named] of Object.entries(refs)) {
    if (!index.tasks || !index.tasks[task]) problems.push('task ' + task + ' names task ' + named.join(', ') + ' and has no prerequisite in the index');
    else if (named.length !== 1) problems.push('task ' + task + ' names ' + named.length + ' other tasks: the index holds one prerequisite per task');
  }
  const used = new Set(Object.values(index.tasks || {}).map(e => e.plan));
  for (const p of plans) if (!used.has(p)) problems.push('the plan «' + p + '» is used by no task');
  return problems;
}

// ── the bridge over stdio ─────────────────────────────────────────────────
class Bridge {
  constructor(exe, options) {
    this.exe = exe;
    this.options = options || {};
    this.next = 1;
    this.pending = new Map();
    this.buffer = '';
    this.stderr = '';
  }
  start() {
    const env = Object.assign({}, process.env, this.options.env || {});
    const script = /\.js$/i.test(this.exe);   // the self-test plays the bridge with a script
    this.child = cp.spawn(script ? process.execPath : this.exe, script ? [this.exe] : [], { stdio: ['pipe', 'pipe', 'pipe'], env, cwd: path.dirname(this.exe), windowsHide: true });
    this.child.stdout.on('data', (d) => {
      this.buffer += d.toString('utf8');
      let i;
      while ((i = this.buffer.indexOf('\n')) >= 0) {
        const line = this.buffer.slice(0, i).trim();
        this.buffer = this.buffer.slice(i + 1);
        if (!line) continue;
        let m;
        try { m = JSON.parse(line); } catch (e) { continue; }
        if (m.id !== undefined && this.pending.has(m.id)) { const p = this.pending.get(m.id); this.pending.delete(m.id); clearTimeout(p.timer); p.resolve(m); }
      }
    });
    this.child.stderr.on('data', (d) => { this.stderr += d.toString('utf8'); });
    this.child.on('exit', (code) => {
      for (const [, p] of this.pending) { clearTimeout(p.timer); p.reject(new Error('the bridge exited (' + code + ') before answering. ' + this.stderr.slice(-400))); }
      this.pending.clear();
    });
    this.child.on('error', (e) => { for (const [, p] of this.pending) { clearTimeout(p.timer); p.reject(e); } this.pending.clear(); });
  }
  request(method, params, timeoutMs) {
    return new Promise((resolve, reject) => {
      const id = this.next++;
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error('the bridge did not answer «' + method + '» in ' + Math.round((timeoutMs || 600000) / 1000) + ' s')); }, timeoutMs || 600000);
      const trace = process.env.PREREQ_TRACE;
      const settle = (fn) => (m) => { if (trace) { try { fs.appendFileSync(trace, JSON.stringify({ id, answer: m }) + '\n'); } catch (e) { /* the trace is a help, not a duty */ } } fn(m); };
      this.pending.set(id, { resolve: settle(resolve), reject, timer });
      if (trace) { try { fs.appendFileSync(trace, JSON.stringify({ id, method, params }) + '\n'); } catch (e) { /* idem */ } }
      this.child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    });
  }
  async init() {
    const r = await this.request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'prerequisite', version: '1' } }, 120000);
    this.version = r.result && r.result.serverInfo ? r.result.serverInfo.version : null;
    this.child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
    const t = await this.request('tools/list', {}, 120000);
    this.toolNames = t.result && Array.isArray(t.result.tools) ? t.result.tools.map(x => x.name) : [];
  }
  async tool(name, args, timeoutMs) {
    const r = await this.request('tools/call', { name, arguments: args }, timeoutMs);
    if (r.error) return { isError: true, text: JSON.stringify(r.error), json: null };
    const res = r.result || {};
    const text = Array.isArray(res.content) ? res.content.map(c => c.text || '').join('\n') : '';
    let json = res.structuredContent || null;
    if (!json) { try { json = JSON.parse(text); } catch (e) { json = null; } }
    return { isError: !!res.isError, text, json };
  }
  // Ends the conversation and waits for the process to be gone (a bridge that
  // is still alive keeps its folder busy), killing it after a few seconds.
  close() {
    return new Promise((resolve) => {
      if (!this.child || this.child.exitCode !== null) return resolve();
      const timer = setTimeout(() => { try { this.child.kill(); } catch (e) { /* gone */ } }, 5000);
      this.child.once('exit', () => { clearTimeout(timer); resolve(); });
      try { this.child.stdin.end(); } catch (e) { /* gone */ }
    });
  }
}

// ── the values of a mate, and the placeholders ────────────────────────────
// Since W3.1-bis.10b the bridge takes the mate values below and refuses any other
// (a mate with an alignment it does not know used to be taken for «aligned»; now it
// is refused). The catalogue describes them in prose only, so neither the enum
// check of stepProblems nor the dry run of the bridge sees a wrong one: this does.
// MateOperations.cs is the source of both tables (the self-test reads it).
const MATE_VALUES = { variant: ['coincident', 'parallel', 'perpendicular', 'distance'], alignment: ['aligned', 'opposite'] };
// The same values as the bridge of before the rename (9384b2c) takes them. The name
// map has no row for them, so they are written here, explicitly.
const MATE_VALUES_BEFORE = {
  variant: { coincident: 'coincidente', parallel: 'parallelo', perpendicular: 'perpendicolare', distance: 'distanza' },
  alignment: { aligned: 'allineato', opposite: 'opposto' },
};
const PLACEHOLDERS = ['WORK', 'PROFILES'];

function mateValueProblems(step, label) {
  const out = [];
  if (step.operation !== 'mate') return out;
  const args = step.arguments || {};
  for (const [name, allowed] of Object.entries(MATE_VALUES)) {
    if (name in args && !allowed.includes(args[name])) out.push(label + ': «' + name + '» = «' + args[name] + '» is not a value of a mate (it takes ' + allowed.join(', ') + '; the bridge refuses the others)');
  }
  return out;
}
function placeholderProblems(step, label) {
  const out = [];
  mapStrings(step.arguments || {}, (t) => {
    for (const m of t.matchAll(/\{([A-Z][A-Z_]*)\}/g)) {
      if (!PLACEHOLDERS.includes(m[1])) out.push(label + ': «{' + m[1] + '}» is not a placeholder the runner fills (it fills ' + PLACEHOLDERS.map(p => '{' + p + '}').join(', ') + ')');
    }
    return t;
  });
  return out;
}

// ── the two protocols ─────────────────────────────────────────────────────
// English: the names as they are. Before the rename: every name through the
// map read from English to Italian, and every answer through the map read the
// other way (the scorer's reading).
let SURFACE = null;
function surface() {
  if (SURFACE) return SURFACE;
  const w = require(path.join(ROOT, 'Checks', 'language', 'window-2-surface.js'));
  const map = w.loadMap(ROOT);
  const toBefore = w.direction(map, true, w.catalogueEnums(ROOT));
  const caseKeys = readJson(path.join(ROOT, 'Bench', 'case-keys.json'));
  SURFACE = { w, map, toBefore, caseKeys };
  return SURFACE;
}
// The steps in the form the bridge of before takes them (its own keys for the
// operation and the arguments of a step, read from the map).
function stepsToBefore(steps) {
  const s = surface();
  const text = JSON.stringify(steps.map(x => ({ operation: x.operation, arguments: x.arguments || {} })));
  return mateValuesToBefore(steps, JSON.parse(s.w.rewriteCase(s.toBefore, s.caseKeys, text, 'Bench/tasks/prerequisites/plan.json', {})));
}
// The map renames the keys of a mate and leaves its values as they are. Here the
// values of every mate go to the words of before, from MATE_VALUES_BEFORE, read on
// the English step of the same index; a value that is not in the table stops the
// translation (a pass-through would be the bridge's refusal, found at the build).
function mateValuesToBefore(english, translated) {
  const d = surface().toBefore;
  const kArguments = d.arg.get('arguments');
  return translated.map((t, i) => {
    if (english[i].operation !== 'mate') return t;
    const args = Object.assign({}, t[kArguments]);
    for (const name of Object.keys(MATE_VALUES_BEFORE)) {
      const want = (english[i].arguments || {})[name];
      if (want === undefined) continue;
      const word = MATE_VALUES_BEFORE[name][want];
      const key = d.arg.get(name);
      if (word === undefined) throw new Error('mate: «' + name + '» = «' + want + '» has no value of before in MATE_VALUES_BEFORE');
      if (!key || !(key in args)) throw new Error('mate: the key of «' + name + '» of before is not in the translated step');
      args[key] = word;
    }
    return Object.assign({}, t, { [kArguments]: args });
  });
}
// The same, as {operation, arguments} again, to be read against the catalogue
// of before (which names the same things by its own names).
function stepsToBeforePlain(steps) {
  const d = surface().toBefore;
  const kOperation = d.arg.get('operation'), kArguments = d.arg.get('arguments');
  return stepsToBefore(steps).map(t => ({ operation: t[kOperation], arguments: t[kArguments] }));
}

function makeProtocol(kind) {
  const score = require('./score-autonomous.js');
  if (kind === 'en') {
    return {
      kind,
      tool: (n) => n,
      steps: (steps) => steps.map(x => ({ operation: x.operation, arguments: x.arguments || {} })),
      document: (action, variant) => (variant ? { action, variant } : { action }),
      build: (steps, extra) => Object.assign({ steps }, extra || {}),
      answer: (a) => a,
    };
  }
  const d = surface().toBefore;
  const key = (k) => d.arg.get(k) || k;
  return {
    kind,
    tool: (n) => d.tool.get(n) || n,
    steps: stepsToBefore,
    document: (action, variant) => {
      const o = {};
      o[key('action')] = d.sub.get(action) || action;
      if (variant) o[key('variant')] = (d.enumByArg.get('variant') && d.enumByArg.get('variant').get(variant)) || variant;
      return o;
    },
    build: (steps, extra) => {
      const o = {};
      o[key('steps')] = steps;
      for (const [k, v] of Object.entries(extra || {})) o[key(k)] = v;
      return o;
    },
    answer: (a) => score.readAnswer(a).answer,
  };
}

// ── dry run of the bridge (needs no SOLIDWORKS) ───────────────────────────
async function dryRunProblems(exe, plan, dir) {
  const problems = [];
  const bridge = new Bridge(exe, { env: { OCE_NO_ATTACH: '1' } });
  bridge.start();
  try {
    await bridge.init();
    const protocol = makeProtocol(bridge.toolNames.includes('inspect_part') ? 'en' : 'it');
    for (const list of stepLists(plan)) {
      for (const g of groupsOf(list.steps)) {
        const steps = g.steps.map(({ index, step }) => ({
          operation: step.operation,
          arguments: rebaseRefs(step.arguments || {}, g.first, plan.name + ' step ' + (index + 1), index),
        }));
        const where = plan.name + ' · ' + (list.kind === 'component' ? list.name : 'document') + ' · steps ' + (g.first + 1) + '-' + (g.first + steps.length);
        const r = await bridge.tool(protocol.tool('build'), protocol.build(protocol.steps(steps), { dry_run: true }), 120000);
        // the bridge marks a dry run that found something as an error and one that holds as a plain answer
        if (r.isError) problems.push(where + ': the dry run of the bridge says: ' + r.text.replace(/\s+/g, ' ').slice(0, 600));
      }
    }
  } finally { await bridge.close(); }
  void dir;
  return problems;
}

// ── the build ─────────────────────────────────────────────────────────────
class Failed extends Error {}

async function build(options) {
  const { exe, planName, folder, dir } = options;
  const log = options.log || (() => {});
  const plan = loadPlan(planName, dir);
  const result = { plan: planName, ok: false, folder, files: [], checks: [], notes: [], steps: [] };
  const bridge = new Bridge(exe);
  bridge.start();
  const failures = [];
  let protocol;
  let profilesDir = null;

  const documentsOpen = (state) => (state && Array.isArray(state.documents_open) ? state.documents_open : []);
  const inspect = async (what) => {
    const r = await bridge.tool(protocol.tool('inspect_part'), {}, 300000);
    if (r.isError || !r.json) throw new Failed((what || 'reading the document') + ': ' + r.text.slice(0, 500));
    return protocol.answer(r.json);
  };
  const newDocument = async (variant) => {
    const r = await bridge.tool(protocol.tool('document'), protocol.document('create', variant), 300000);
    if (r.isError) throw new Failed('creating a ' + variant + ': ' + r.text.slice(0, 500));
  };
  const profilesFolder = async () => {
    if (profilesDir) return profilesDir;
    if (process.env.OCE_WELDMENT_PROFILES) { profilesDir = process.env.OCE_WELDMENT_PROFILES; return profilesDir; }
    const r = await bridge.tool(protocol.tool('build'), protocol.build(protocol.steps([{ operation: 'weldment_profiles', arguments: { search: 'TUBO STRUTTURALE QUADRO' } }])), 300000);
    const a = r.json ? protocol.answer(r.json) : null;
    const o = a && Array.isArray(a.outcomes) ? a.outcomes[0] : null;
    const first = o && o.measurements && Array.isArray(o.measurements.profiles) ? o.measurements.profiles[0] : null;
    if (!first || !first.profile_path) throw new Failed('{PROFILES}: OCE_WELDMENT_PROFILES is not set and the bridge lists no profile «TUBO STRUTTURALE QUADRO»: ' + r.text.slice(0, 300));
    profilesDir = path.dirname(first.profile_path);
    return profilesDir;
  };
  const placeholders = async (steps) => {
    const needs = JSON.stringify(steps).includes('{PROFILES}');
    const p = needs ? await profilesFolder() : null;
    return steps.map(s => ({ operation: s.operation, arguments: mapStrings(s.arguments || {}, (t) => t.split('{PROFILES}').join(p || '').split('{WORK}').join(folder)) }));
  };

  // One group: the state first, selectors resolved on it, references
  // renumbered, one call of the bridge, then the outcomes and the expectations.
  const runGroup = async (list, group, label) => {
    const state = await inspect(label + ': reading the document before the group');
    const resolved = [];
    for (const { index, step } of group.steps) {
      const here = label + ' · step ' + (index + 1) + ' (' + step.operation + ')';
      let args;
      try {
        args = rebaseRefs(step.arguments || {}, group.first, here, index);
        args = mapStrings(args, (t) => (t.startsWith('@') ? resolveSelector(t, state) : t));
      } catch (e) { throw new Failed(here + ': ' + e.message); }
      resolved.push({ operation: step.operation, arguments: args });
    }
    const steps = await placeholders(resolved);
    const r = await bridge.tool(protocol.tool('build'), protocol.build(protocol.steps(steps)), 1800000);
    const a = r.json ? protocol.answer(r.json) : null;
    const outcomes = a && Array.isArray(a.outcomes) ? a.outcomes : [];
    const stateAfter = a && a.state_after ? a.state_after : null;
    outcomes.forEach((o, k) => { result.steps.push({ where: label + ' · step ' + (group.steps[k].index + 1), operation: o.operation, succeeded: !!o.succeeded, error: o.error || null }); });
    for (let k = 0; k < group.steps.length; k++) {
      const { index, step } = group.steps[k];
      const here = label + ' · step ' + (index + 1) + ' (' + step.operation + ')';
      const o = outcomes[k];
      if (!o || !o.succeeded) {
        // The first insert into an empty assembly is counted -1 before and
        // refused by the bridge although the component came in (seen on the run
        // of 6/10, both bridges): the assembly is read, not the answer.
        if (o && step.operation === 'insert_component' && options.afterInsert) {
          const ok = await options.afterInsert(state);
          if (ok) { result.notes.push(here + ': the bridge refused the insert (' + String(o.error).slice(0, 160) + ') and the assembly holds the component: accepted on the state'); continue; }
        }
        throw new Failed(here + ': ' + (o ? String(o.error) : 'no answer (' + r.text.slice(0, 400) + ')'));
      }
      for (const x of step.expect || []) {
        const e = evaluate(x, { state: stateAfter, measurements: o.measurements || {} });
        result.checks.push(Object.assign({ where: here }, e));
        if (!e.ok) failures.push(here + ': ' + x.what + ' is ' + JSON.stringify(e.got) + ', the task says ' + JSON.stringify(e.expected) + (e.tolerance ? ' +- ' + e.tolerance : ''));
      }
    }
    return stateAfter;
  };

  const save = async (list, filePath, label) => {
    const group = { first: 0, steps: [{ index: 0, step: { operation: 'save_document', arguments: { path: filePath } } }] };
    await runGroup(list, group, label + ' · save');
    let bytes = -1;
    try { bytes = fs.statSync(filePath).size; } catch (e) { bytes = -1; }
    // the file is read on the disk: the bridge says it saved, the disk says it did
    if (bytes <= 0) throw new Failed(label + ': the bridge saved «' + filePath + '» and the file is not on the disk');
    result.files.push({ path: filePath, bytes });
  };

  try {
    await bridge.init();
    result.bridge_version = bridge.version;
    const kind = bridge.toolNames.includes('inspect_part') ? 'en' : 'it';
    protocol = makeProtocol(kind);
    if (kind === 'it' && !bridge.toolNames.includes(protocol.tool('inspect_part'))) throw new Failed('the bridge answers neither inspect_part nor its name before the rename: ' + bridge.toolNames.join(', '));
    result.protocol = kind;
    fs.mkdirSync(folder, { recursive: true });
    const lists = stepLists(plan);
    const comps = lists.filter(l => l.kind === 'component');
    const main = lists.find(l => l.kind === 'main');
    const isAssembly = comps.length > 0;
    const saved = {};

    for (const list of comps) {
      log('component «' + list.name + '»');
      await newDocument('part');
      let last = null;
      for (const g of groupsOf(list.steps)) last = await runGroup(list, g, plan.name + ' · component «' + list.name + '»');
      const file = path.join(folder, list.name + '.sldprt');
      await save(list, file, plan.name + ' · component «' + list.name + '»');
      saved[list.name] = file;
      void last;
    }

    let inserted = 0;
    if (isAssembly) {
      log('assembly');
      await newDocument('assembly');
      for (const list of comps) {
        if (list.component.to_insert === false) continue;
        const p = list.component.position_mm || [0, 0, 0];
        const before = inserted;
        const group = { first: 0, steps: [{ index: 0, step: { operation: 'insert_component', arguments: { path: saved[list.name], x_mm: p[0], y_mm: p[1], z_mm: p[2] } } }] };
        const countNow = async () => { const st = await inspect('counting the components'); return typeof st.how_many_components === 'number' ? st.how_many_components : (Array.isArray(st.components) ? st.components.length : -1); };
        options.afterInsert = async () => (await countNow()) === before + 1;
        await runGroup(list, group, plan.name + ' · insert «' + list.name + '»');
        options.afterInsert = null;
        inserted++;
        const now = await countNow();
        if (now !== inserted) throw new Failed(plan.name + ': after inserting «' + list.name + '» the assembly holds ' + now + ' components, the plan has inserted ' + inserted);
      }
    } else {
      log('part');
      await newDocument('part');
    }

    for (const g of groupsOf(main.steps)) await runGroup(main, g, plan.name);

    const ext = isAssembly ? '.sldasm' : '.sldprt';
    await save(main, path.join(folder, plan.name + ext), plan.name);

    const finalState = await inspect('reading the finished document');
    const open = documentsOpen(finalState);
    const front = open.find(d => d.active) || null;
    result.document_in_front = front ? front.document_title : null;
    result.documents_open = open.map(d => d.document_title);
    result.bodies = finalState.bodies_total;
    result.volume_mm3 = finalState.volume_mm3;
    const expectedTitle = (plan.name + ext).toLowerCase();
    if (!front || String(front.document_title).toLowerCase() !== expectedTitle) failures.push('the document in front is «' + (front ? front.document_title : 'none') + '», not «' + plan.name + ext + '»');
    if (failures.length) throw new Failed(failures.length + ' expectation(s) did not hold:\n  ' + failures.join('\n  '));
    result.ok = true;
  } catch (e) {
    result.error = e.message;
    result.ok = false;
  } finally { await bridge.close(); }
  return result;
}

// ── the commands ──────────────────────────────────────────────────────────
function takeOption(args, name) {
  const i = args.indexOf(name);
  if (i < 0) return null;
  const v = args[i + 1];
  args.splice(i, 2);
  return v;
}

async function commandCheck(args) {
  let bridge = takeOption(args, '--bridge');
  if (bridge) bridge = path.resolve(bridge);   // the bridge runs in its own folder: a relative path would not find it there
  let before = takeOption(args, '--catalogue-before');
  const dir = takeOption(args, '--dir') || PLANS_DIR;
  if (bridge) {
    const next = catalogueNextTo(bridge);
    if (next && next.kind === 'before') { before = before || next.file; bridge = null; }
    else if (!next) { console.log('  --  no catalogue next to ' + bridge + ': its dry run is not made'); bridge = null; }
  }
  const names = args.length ? args : listPlans(dir);
  let bad = 0;
  const say = (m) => { console.log('  XX  ' + m); bad++; };
  const english = loadCatalogue(path.join(ROOT, 'Tools', 'catalog.json'));
  for (const p of indexProblems(dir, __dirname)) say(p);
  for (const p of consistencyProblems(dir)) say(p);
  for (const name of names) {
    let plan;
    try { plan = loadPlan(name, dir); } catch (e) { say(name + ': ' + e.message); continue; }
    const problems = planProblems(plan, english, null);
    if (before) for (const p of planProblems(plan, loadCatalogue(before, true), stepsToBeforePlain)) problems.push('(protocol of before) ' + p);
    if (bridge) { try { for (const p of await dryRunProblems(bridge, plan, dir)) problems.push(p); } catch (e) { problems.push('dry run: ' + e.message); } }
    if (problems.length) problems.forEach(say);
    else console.log('  ok  ' + name + ' (' + stepLists(plan).map(l => l.steps.length).reduce((a, b) => a + b, 0) + ' steps' + (bridge ? ', dry run of the bridge' : '') + (before ? ', protocol of before' : '') + ')');
  }
  console.log(bad ? 'check: ' + bad + ' problem(s)' : 'check: every plan holds');
  return bad === 0 ? 0 : 1;
}

async function commandBuild(args) {
  const out = takeOption(args, '--out');
  const dir = takeOption(args, '--dir') || PLANS_DIR;
  if (args.length !== 3) fail('--build <bridge.exe> <plan> <folder> [--out <result.json>] [--dir <plans folder>]');
  const [exe, planName, folder] = args;
  const result = await build({ exe: path.resolve(exe), planName, folder: path.resolve(folder), dir: path.resolve(dir), log: (m) => console.log('  ' + m) });
  if (out) fs.writeFileSync(out, JSON.stringify(result, null, 1));
  if (result.ok) console.log('prerequisite «' + planName + '»: built and checked (' + result.checks.length + ' expectations), in front: ' + result.document_in_front);
  else console.log('prerequisite «' + planName + '» FAILED: ' + result.error);
  return result.ok ? 0 : 1;
}

// ── the self-test ─────────────────────────────────────────────────────────
// A script that plays the bridge over stdio: documents, a part that has one
// block and a hole, an assembly that refuses the first insert the way the real
// one does on an empty assembly, and a save that writes a file.
// With FAKE_SW_STATE set it shares its documents with the fake CloseLeftovers of
// session-documents.js (a file of {title, path, type}), so that what a
// session leaves open is what the closing has to close.
const FAKE_BRIDGE = [
  "const fs = require('fs'), path = require('path');",
  "const docs = []; let active = null; const counter = { part: 0, assembly: 0 };",
  "const shared = process.env.FAKE_SW_STATE;",
  "const toShared = () => { if (!shared) return; const s = JSON.parse(fs.readFileSync(shared, 'utf8')); const mine = new Set(docs.map(d => d.shareKey)); s.docs = s.docs.filter(x => !x.shareKey || mine.has(x.shareKey)).concat(docs.filter(d => !s.docs.some(x => x.shareKey === d.shareKey)).map(d => ({ shareKey: d.shareKey, title: d.title, path: d.path, type: d.kind }))); for (const x of s.docs) { const d = docs.find(y => y.shareKey === x.shareKey); if (d) { x.title = d.title; x.path = d.path; } } fs.writeFileSync(shared, JSON.stringify(s)); };",
  "const fromShared = () => { if (!shared) return; const s = JSON.parse(fs.readFileSync(shared, 'utf8')); const alive = new Set(s.docs.map(x => x.shareKey)); for (let i = docs.length - 1; i >= 0; i--) if (!alive.has(docs[i].shareKey)) { if (docs[i] === active) active = null; docs.splice(i, 1); } if (!active && docs.length) active = docs[docs.length - 1]; };",
  "let keys = 0;",
  "const mk = (kind) => { const d = { kind, shareKey: 'b' + process.pid + '-' + (++keys), title: (kind === 'part' ? 'Part' : 'Assembly') + (++counter[kind]), path: '', bodies: 0, volume: 0, bbox: [0, 0, 0], faces: [], components: [] }; docs.push(d); active = d; toShared(); return d; };",
  "const stateOf = (d) => ({ document_title: d.title, kind: d.kind, path: d.path, documents_open: docs.map(x => Object.assign({ document_title: x.title, kind: x.kind }, x.path ? { path: x.path } : {}, x === active ? { active: true } : {})),",
  "  bodies_total: d.bodies, volume_mm3: d.volume, bounding_box_mm: d.bbox, faces: d.faces, truncated: false, how_many_components: d.components.length, components: d.components });",
  "const text = (o) => ({ content: [{ type: 'text', text: JSON.stringify(o) }] });",
  "function step(d, s) {",
  "  const a = s.arguments || {};",
  "  switch (s.operation) {",
  "    case 'extrude_rectangle': d.bodies = 1; d.volume = a.length_mm * a.width_mm * a.thickness_mm; d.bbox = [a.length_mm, a.thickness_mm, a.width_mm];",
  "      d.faces = [{ uid: 'F1', area_mm2: a.length_mm * a.width_mm, point_mm: [0, 0, 0], normal: [0, -1, 0], facing: 'down' }, { uid: 'F2', area_mm2: a.length_mm * a.width_mm, point_mm: [0, a.thickness_mm, 0], normal: [0, 1, 0], facing: 'up' }]; return { measurements: { length_mm: a.length_mm } };",
  "    case 'hole_wizard': if (!d.faces.some(f => f.uid === a.face_uid)) return { error: 'the face ' + a.face_uid + ' is not in this document' }; if (!(a.positions || []).length) return { error: 'no positions' };",
  "      d.volume -= a.positions.length; return { measurements: {} };",
  "    case 'save_document': { fs.mkdirSync(path.dirname(a.path), { recursive: true }); fs.writeFileSync(a.path, 'fake'); d.path = a.path; d.title = path.basename(a.path, path.extname(a.path)) + path.extname(a.path).toUpperCase(); return { measurements: { path: a.path, byte: 4 } }; }",
  "    case 'insert_component': { const empty = d.components.length === 0; d.components.push({ uid: 'C' + (d.components.length + 1), path: a.path }); if (empty) return { error: 'I asked to insert it and the assembly had -1 components and now it has 1. Not considered inserted.' }; return { measurements: { components_after: d.components.length } }; }",
  "    case 'mate': return { measurements: { mates_after: [1, 1] } };",
  "    case 'interference_check': return { measurements: { how_many: 0 } };",
  "    default: return { error: 'the fake does not know ' + s.operation };",
  "  }",
  "}",
  "const rl = require('readline').createInterface({ input: process.stdin });",
  "rl.on('line', (line) => {",
  "  let m; try { m = JSON.parse(line); } catch (e) { return; }",
  "  if (m.id === undefined) return;",
  "  const reply = (result) => process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id, result }) + '\\n');",
  "  fromShared();",
  "  if (m.method === 'initialize') return reply({ protocolVersion: '2025-06-18', capabilities: {}, serverInfo: { name: 'fake', version: '0.0.0' } });",
  "  if (m.method === 'tools/list') return reply({ tools: ['inspect_part', 'build', 'document'].map(name => ({ name })) });",
  "  const t = m.params.name, a = m.params.arguments || {};",
  "  if (t === 'inspect_part') return reply(active ? text(stateOf(active)) : { isError: true, content: [{ type: 'text', text: 'no document' }] });",
  "  if (t === 'document') { if (a.action === 'create') { mk(a.variant || 'part'); return reply(text({ document_title: active.title })); } return reply({ isError: true, content: [{ type: 'text', text: 'not played' }] }); }",
  "  if (t === 'build') {",
  "    const outcomes = []; let failed = false;",
  "    for (const s of a.steps) { const r = step(active, s); outcomes.push({ operation: s.operation, succeeded: !r.error, error: r.error || null, measurements: r.measurements || null }); if (r.error) { failed = true; break; } }",
  "    toShared();",
  "    const body = text({ document: active.title, outcomes, state_after: stateOf(active) }); if (failed) body.isError = true; return reply(body);",
  "  }",
  "  reply({ isError: true, content: [{ type: 'text', text: 'unknown tool ' + t }] });",
  "});",
].join('\n');

async function selfTest() {
  let bad = 0;
  const check = (name, ok, more) => { console.log((ok ? '  ok  ' : '  XX  ') + name + (ok || !more ? '' : '  [' + more + ']')); if (!ok) bad++; };
  const throws = (fn) => { try { fn(); return null; } catch (e) { return e.message; } };

  // The faces of the stand of the run of 6/10, as the bridge listed them (the
  // foot at x = -480, z = -280, and the underside of the short bar next to it):
  // the selectors of the plan must pick exactly the two faces the gusset
  // needs, and not the inner walls of the same foot, which have the same area.
  const faces = [
    { uid: 'F49', area_mm2: 4800, point_mm: [-464, -20, -300], normal: [0, 0, -1], facing: 'back' },
    { uid: 'F50', area_mm2: 4800, point_mm: [-460, -20, -264], normal: [1, 0, 0], facing: 'right' },
    { uid: 'F51', area_mm2: 4800, point_mm: [-464, -20, -260], normal: [0, 0, 1], facing: 'front' },
    { uid: 'F52', area_mm2: 4800, point_mm: [-464, -20, -263], normal: [0, 0, -1], facing: 'back' },
    { uid: 'F53', area_mm2: 4800, point_mm: [-463, -20, -264], normal: [-1, 0, 0], facing: 'left' },
    { uid: 'F54', area_mm2: 4800, point_mm: [-464, -20, -297], normal: [0, 0, 1], facing: 'front' },
    { uid: 'F55', area_mm2: 4800, point_mm: [-497, -20, -264], normal: [1, 0, 0], facing: 'right' },
    { uid: 'F56', area_mm2: 4800, point_mm: [-500, -20, -264], normal: [-1, 0, 0], facing: 'left' },
    { uid: 'F7', area_mm2: 30720, point_mm: [0, -20, -264], normal: [0, -1, 0], facing: 'down' },
    { uid: 'F21', area_mm2: 17920, point_mm: [-464, -20, 0], normal: [0, -1, 0], facing: 'down' },
    { uid: 'F23', area_mm2: 17920, point_mm: [464, -20, 0], normal: [0, -1, 0], facing: 'down' },
    { uid: 'F26', area_mm2: 17920, point_mm: [-464, 17, 0], normal: [0, -1, 0], facing: 'down' },
  ];
  const state = { faces, bodies: [{ uid: 'C1', name: 'TUBO STRUTTURALE QUADRO 40x40x3(3)' }, { uid: 'C2', name: 'TUBO STRUTTURALE QUADRO 40x40x3(4)' }] };
  const stand = loadPlan('07_tube_stand');
  const gusset = stand.steps.find(s => s.operation === 'gusset');
  const picked = gusset.arguments.faces_uid.map(e => resolveSelector(e, state));
  check('the selectors of the first gusset of the stand pick the +z wall of the foot and the underside of the bar, and nothing else', picked[0] === 'F51' && picked[1] === 'F21', JSON.stringify(picked));
  check('«n=» takes the n-th, a selector that matches many is refused, one that matches none says so',
        resolveSelector('@face(area~=4800,n=3)', state) === 'F51' &&
        /found \d+; choosing one/.test(throws(() => resolveSelector('@face(area~=4800)', state)) || '') &&
        /not found/.test(throws(() => resolveSelector('@face(normal=up)', state)) || ''));
  check('a not found on a state whose faces are cut says the face may not be listed',
        /may not be listed/.test(throws(() => resolveSelector('@face(normal=up)', Object.assign({}, state, { truncated: true, faces_total: 99 }))) || ''));
  check('a state truncated by its edges only does not say the faces are cut',
        !/may not be listed/.test(throws(() => resolveSelector('@face(normal=up)', Object.assign({}, state, { truncated: true }))) || ''));
  check('a body is named by its name', resolveSelector('@body(name=TUBO STRUTTURALE QUADRO 40x40x3(4))', state) === 'C2');
  check('a selector the runner does not read is refused by its name', /not a selector the runner reads/.test(throws(() => resolveSelector('@dimension(name=D1)', state)) || ''));

  // groups and references
  const steps = [
    { operation: 'a', arguments: {} },
    { operation: 'b', arguments: { face_ref: { step: 1, kind: 'top_face' } } },
    { operation: 'c', arguments: { face_uid: '@face(normal=up)' } },
    { operation: 'd', arguments: { face_ref: { step: 3, kind: 'top_face' } } },
  ];
  const groups = groupsOf(steps);
  check('a selector starts a group, the other steps follow it', groups.length === 2 && groups[0].steps.length === 2 && groups[1].steps.length === 2 && groups[1].first === 2);
  check('a reference is renumbered inside its group', rebaseRefs(steps[3].arguments, 2, 'x').face_ref.step === 1 && rebaseRefs(steps[1].arguments, 0, 'x').face_ref.step === 1 && rebaseRefs({ face_ref: { step: 5 } }, 3, 'x').face_ref.step === 2);
  check('a reference to a step of an earlier group is refused before anything runs', /earlier group/.test(throws(() => rebaseRefs({ face_ref: { step: 1 } }, 2, 'x', 3)) || ''));
  check('a reference to a step that is not before the one that names it is refused', /not before this step/.test(throws(() => rebaseRefs({ face_ref: { step: 7 } }, 0, 'x', 1)) || ''));

  // expectations
  const ctx = { state: { volume_mm3: 67782.1, bodies_total: 1, bounding_box_mm: [120, 60, 50], faces: [{ diameter_mm: 9.01 }, { normal: [0, 1, 0] }, { diameter_mm: 8.99 }] }, measurements: { how_many: 0, kind: 'closed', list: ['a', 'b'] } };
  check('a number within its tolerance holds, outside it does not',
        evaluate({ what: 'volume_mm3', equal_to: 67782, tolerance: 200 }, ctx).ok && !evaluate({ what: 'volume_mm3', equal_to: 68800, tolerance: 200 }, ctx).ok);
  check('bodies, the bounding box and the diameters are read as the scorer reads them',
        evaluate({ what: 'bodies', equal_to: 1 }, ctx).ok && evaluate({ what: 'bounding_box_mm', equal_to: [120, 60, 50], tolerance: 0.01 }, ctx).ok &&
        evaluate({ what: 'diameters_mm', equal_to: [9, 9], tolerance: 0.02 }, ctx).ok && !evaluate({ what: 'diameters_mm', equal_to: [9], tolerance: 0.02 }, ctx).ok);
  // The slotted plate of 6/10: 36 edges, 30 listed, every face listed, `truncated` true.
  const edgesCut = { state: Object.assign({}, ctx.state, { truncated: true, edges_total: 36 }) };
  const facesCutCtx = { state: Object.assign({}, ctx.state, { truncated: true, faces_total: 61 }) };
  check('the diameters are read when only the edges are cut, and not when the faces are (seen red on 6/10: «undefined» on the plate)',
        evaluate({ what: 'diameters_mm', equal_to: [9, 9], tolerance: 0.02 }, edgesCut).ok && evaluate({ what: 'diameters_mm', equal_to: [9, 9], tolerance: 0.02 }, facesCutCtx).got === undefined);
  check('a measurement is read on the step, a text includes, a missing field is not a pass',
        evaluate({ what: 'measurements.how_many', equal_to: 0 }, ctx).ok && evaluate({ what: 'measurements.kind', includes: ['clos'] }, ctx).ok &&
        evaluate({ what: 'measurements.list', includes: ['b'] }, ctx).ok && !evaluate({ what: 'measurements.nothing', equal_to: 0 }, ctx).ok);

  // the plans against the catalogue
  const catalogue = loadCatalogue(path.join(ROOT, 'Tools', 'catalog.json'));
  const good = { name: 'x', steps: [{ operation: 'extrude_rectangle', arguments: { plane: 'front', length_mm: 1, width_mm: 1, thickness_mm: 1 }, expect: [{ what: 'bodies', equal_to: 1 }] }] };
  check('a plan that holds has no problem', planProblems(good, catalogue, null).length === 0, planProblems(good, catalogue, null).join('; '));
  const wrong = (patch) => planProblems(JSON.parse(JSON.stringify(Object.assign({}, good, patch))), catalogue, null);
  check('an operation that does not exist is a problem', wrong({ steps: [{ operation: 'extrude_rectangle_x', arguments: {}, expect: [{ what: 'bodies', equal_to: 1 }] }] }).some(p => /not in the catalogue/.test(p)));
  check('an argument the bridge would ignore is a problem', wrong({ steps: [{ operation: 'extrude_rectangle', arguments: { plane: 'front', length_mm: 1, width_mm: 1, thickness_mm: 1, depth_mm: 2 }, expect: [{ what: 'bodies', equal_to: 1 }] }] }).some(p => /would ignore/.test(p)));
  check('a missing required argument is a problem', wrong({ steps: [{ operation: 'extrude_rectangle', arguments: { plane: 'front' }, expect: [{ what: 'bodies', equal_to: 1 }] }] }).some(p => /needs/.test(p)));
  check('a value outside its enum is a problem', wrong({ steps: [{ operation: 'extrude_rectangle', arguments: { plane: 'sideways', length_mm: 1, width_mm: 1, thickness_mm: 1 }, expect: [{ what: 'bodies', equal_to: 1 }] }] }).some(p => /not one of/.test(p)));
  check('a plan with no expectation at all is a problem', wrong({ steps: [{ operation: 'extrude_rectangle', arguments: { plane: 'front', length_mm: 1, width_mm: 1, thickness_mm: 1 } }] }).some(p => /no step carries an expectation/.test(p)));
  check('a state path on a step that is not the last of its group is a problem',
        wrong({ steps: [{ operation: 'extrude_rectangle', arguments: { plane: 'front', length_mm: 1, width_mm: 1, thickness_mm: 1 }, expect: [{ what: 'bodies', equal_to: 1 }] },
                        { operation: 'extrude_rectangle', arguments: { plane: 'front', length_mm: 1, width_mm: 1, thickness_mm: 1 }, expect: [{ what: 'measurements.length_mm', equal_to: 1 }] }] }).some(p => /last step of its group/.test(p)));
  check('an expectation path the runner does not read is a problem', wrong({ steps: [{ operation: 'extrude_rectangle', arguments: { plane: 'front', length_mm: 1, width_mm: 1, thickness_mm: 1 }, expect: [{ what: 'mass_kg', equal_to: 1 }] }] }).some(p => /not a path the runner reads/.test(p)));

  // the values of a mate and the placeholders: neither the enum check nor the dry run of
  // the bridge sees them (the catalogue describes them in prose), so they are read here
  const mateStep = (extra) => ({ operation: 'mate', arguments: Object.assign({ component_a: 'C2', face_a: 'down', component_b: 'C1', face_b: 'up' }, extra), expect: [{ what: 'measurements.mates_after', equal_to: [1, 1] }] });
  const saveStep = (p) => ({ operation: 'save_document', arguments: { path: p }, expect: [{ what: 'bodies', equal_to: 1 }] });
  check('the mate values of today are not a problem', wrong({ steps: [mateStep({ variant: 'distance', distance_mm: 1, alignment: 'opposite' }), mateStep({ variant: 'coincident', alignment: 'aligned' })] }).length === 0,
        wrong({ steps: [mateStep({ variant: 'distance', distance_mm: 1, alignment: 'opposite' })] }).join('; '));
  check('a mate value of before the rename is a problem, the variant and the alignment alike (seen red)',
        wrong({ steps: [mateStep({ variant: 'distanza', distance_mm: 1 })] }).some(p => /«variant» = «distanza» is not a value of a mate/.test(p)) &&
        wrong({ steps: [mateStep({ alignment: 'opposto' })] }).some(p => /«alignment» = «opposto» is not a value of a mate/.test(p)));
  check('a placeholder of before the rename, or one nobody fills, is a problem; the two the runner fills are not (seen red)',
        wrong({ steps: [saveStep('{PROFILI}\\x.sldprt')] }).some(p => /«\{PROFILI\}» is not a placeholder/.test(p)) &&
        wrong({ steps: [saveStep('{WORK}\\x.sldprt'), saveStep('{PROFILES}\\x.sldprt')] }).every(p => !/placeholder/.test(p)));
  const mateSource = fs.readFileSync(path.join(ROOT, 'Oce.Solid.Agent.Core', 'MateOperations.cs'), 'utf8');
  const known = /KnownTypes\s*=\s*\{([^}]*)\}/.exec(mateSource);
  check('the mate values this script accepts are the ones the bridge takes (read in MateOperations.cs: KnownTypes, the default alignment and the opposite one)',
        !!known && JSON.stringify([...known[1].matchAll(/"([^"]+)"/g)].map(m => m[1])) === JSON.stringify(MATE_VALUES.variant) &&
        /Str\(a, "alignment", "aligned"\)/.test(mateSource) && /alignment\.Equals\("opposite"/.test(mateSource), known ? known[1] : 'no KnownTypes');

  // the real plans, the index and the tasks
  let realProblems = [];
  for (const name of listPlans()) realProblems = realProblems.concat(planProblems(loadPlan(name), catalogue, null));
  check('every plan of Bench/tasks/prerequisites holds against the catalogue', realProblems.length === 0, realProblems.slice(0, 3).join('; '));
  check('the bracket of the pair is the plan of task 01', consistencyProblems(PLANS_DIR).length === 0, consistencyProblems(PLANS_DIR).join('; '));
  const ix = indexProblems(PLANS_DIR, __dirname);
  check('the index covers every task that names another task, and every plan is used', ix.length === 0, ix.join('; '));
  const fakeDir = fs.mkdtempSync(path.join(require('os').tmpdir(), 'prereq-'));
  try {
    for (const f of fs.readdirSync(PLANS_DIR)) fs.copyFileSync(path.join(PLANS_DIR, f), path.join(fakeDir, f));
    const fakeIndex = loadIndex(fakeDir);
    delete fakeIndex.tasks['13'];
    fs.writeFileSync(path.join(fakeDir, 'index.json'), JSON.stringify(fakeIndex));
    check('a task that names another task and has no prerequisite is a problem (seen red)', indexProblems(fakeDir, __dirname).some(p => /task 13 names task 08 and has no prerequisite/.test(p)));
    fs.unlinkSync(path.join(fakeDir, '02_bolt_circle_flange.json'));
    check('an index that names a plan which is not there is a problem (seen red)', indexProblems(fakeDir, __dirname).some(p => /the plan «02_bolt_circle_flange» is not in/.test(p)));
  } finally { fs.rmSync(fakeDir, { recursive: true, force: true }); }

  // THE WHOLE BUILD, against a script that plays the bridge (the real one needs
  // SOLIDWORKS): groups, selectors read on the state, the save read on the disk,
  // the assembly, the insert the bridge refuses and the assembly holds, the
  // expectations, and the three ways to fail.
  const dir = fs.mkdtempSync(path.join(require('os').tmpdir(), 'prereq-flow-'));
  try {
    const fakeBridge = path.join(dir, 'fake-bridge.js');
    fs.writeFileSync(fakeBridge, FAKE_BRIDGE);
    const plans = path.join(dir, 'plans');
    fs.mkdirSync(plans);
    const rect = (l, w, t) => ({ operation: 'extrude_rectangle', arguments: { plane: 'top', length_mm: l, width_mm: w, thickness_mm: t, centered: true } });
    const testPlan = {
      name: '99_test', why: 'a plan for the self-test',
      assembly: { components: [
        { name: 'plate', steps: [rect(100, 100, 10)] },
        { name: 'block', position_mm: [0, 60, 0], steps: [rect(50, 50, 20),
          { operation: 'hole_wizard', arguments: { face_uid: '@face(normal=down)', variant: 'clearance', size: 'M8', positions: [{ x_mm: 0, y_mm: 0 }] } }] },
      ] },
      steps: [
        { operation: 'mate', arguments: { component_a: 'C2', face_a: 'down', component_b: 'C1', face_b: 'up' } },
        { operation: 'interference_check', arguments: {},
          expect: [{ what: 'measurements.how_many', equal_to: 0 }, { what: 'state.how_many_components', equal_to: 2 }] },
      ],
    };
    const write = (name, plan) => fs.writeFileSync(path.join(plans, name + '.json'), JSON.stringify(plan));
    write('99_test', testPlan);
    const part = { name: '98_part', why: 'a part for the self-test', steps: [rect(10, 10, 5), { operation: 'hole_wizard', arguments: { face_uid: '@face(normal=down)', variant: 'clearance', size: 'M8', positions: [{ x_mm: 0, y_mm: 0 }] },
      expect: [{ what: 'bodies', equal_to: 1 }, { what: 'volume_mm3', equal_to: 499, tolerance: 1, tolerance_why: 'the fake takes 1 mm3 for a hole' }] }] };
    write('98_part', part);
    const folderOf = (n) => path.join(dir, 'out-' + n);
    const ran = await build({ exe: fakeBridge, planName: '99_test', folder: folderOf(1), dir: plans });
    check('an assembly plan is built, saved and left in front: the components saved on the disk, the assembly in front, the expectations held',
          ran.ok && fs.existsSync(path.join(folderOf(1), 'plate.sldprt')) && fs.existsSync(path.join(folderOf(1), 'block.sldprt')) && fs.existsSync(path.join(folderOf(1), '99_test.sldasm')) &&
          ran.document_in_front === '99_test.SLDASM' && ran.checks.length === 2 && ran.checks.every(c => c.ok), JSON.stringify(ran).slice(0, 600));
    check('the first insert into an empty assembly, refused by the bridge although the component came in, is accepted on the state and said',
          ran.notes.some(n => /accepted on the state/.test(n)));
    check('a selector was read on the state of the part (the underside of the block), and a group per selector was sent',
          ran.steps.filter(s => s.operation === 'hole_wizard').length === 1 && ran.steps.filter(s => s.operation === 'hole_wizard').every(s => s.succeeded));
    const single = await build({ exe: fakeBridge, planName: '98_part', folder: folderOf(2), dir: plans });
    check('a part plan is built, saved as .sldprt and checked', single.ok && single.document_in_front === '98_part.SLDPRT' && single.checks.length === 2, JSON.stringify(single).slice(0, 400));

    write('97_wrong', Object.assign({}, part, { name: '97_wrong', steps: [part.steps[0], Object.assign({}, part.steps[1], { expect: [{ what: 'volume_mm3', equal_to: 12345, tolerance: 1, tolerance_why: 'test' }] })] }));
    const wrong = await build({ exe: fakeBridge, planName: '97_wrong', folder: folderOf(3), dir: plans });
    check('an expectation that does not hold makes the prerequisite fail and says which number (seen red)', !wrong.ok && /expectation\(s\) did not hold/.test(wrong.error) && /volume_mm3/.test(wrong.error), wrong.error);

    write('96_missing', Object.assign({}, part, { name: '96_missing', steps: [part.steps[0], { operation: 'hole_wizard', arguments: { face_uid: '@face(normal=sideways)', variant: 'clearance', size: 'M8', positions: [{ x_mm: 0, y_mm: 0 }] }, expect: [{ what: 'bodies', equal_to: 1 }] }] }));
    const missing = await build({ exe: fakeBridge, planName: '96_missing', folder: folderOf(4), dir: plans });
    check('a selector that finds no face fails before the step is sent, with the selector (seen red)', !missing.ok && /not found/.test(missing.error) && /normal=sideways/.test(missing.error) && !missing.steps.some(s => s.operation === 'hole_wizard'), missing.error);

    write('95_refused', Object.assign({}, part, { name: '95_refused', steps: [part.steps[0], { operation: 'hole_wizard', arguments: { face_uid: '@face(normal=down)', variant: 'clearance', size: 'M8', positions: [] }, expect: [{ what: 'bodies', equal_to: 1 }] }] }));
    const refused = await build({ exe: fakeBridge, planName: '95_refused', folder: folderOf(5), dir: plans });
    check('a step the bridge refuses makes the prerequisite fail and carries the refusal (seen red)', !refused.ok && /no positions/.test(refused.error), refused.error);
  } catch (e) { check('the build can be played against a fake bridge', false, e.message); }
  finally { fs.rmSync(dir, { recursive: true, force: true }); }

  // the protocol of before: every step translated, and back
  try {
    const s = surface();
    const back = s.w.direction(s.map, false, null);
    const sample = loadPlan('03_slotted_plate').steps.map(x => ({ operation: x.operation, arguments: x.arguments }));
    const there = stepsToBefore(sample);
    const plain = stepsToBeforePlain(sample);
    check('every step of a plan is translated to the protocol of before: another operation name, and arguments that are not the English ones',
          plain.every((x, i) => x.operation !== sample[i].operation) &&
          plain.every((x, i) => Object.keys(x.arguments).some(k => !(k in sample[i].arguments))), JSON.stringify(plain).slice(0, 200));
    const round = JSON.parse(s.w.rewriteCase(back, s.caseKeys, JSON.stringify(there), 'Bench/tasks/prerequisites/plan.json', {}));
    check('and back: the translation read the other way gives the plan again', JSON.stringify(round) === JSON.stringify(sample), JSON.stringify(round).slice(0, 200));
    // the values of a mate: the map renames the keys and leaves the values, so they go by the table
    const matePlan = loadPlan('08_box_and_lid').steps;
    const kArgs = s.toBefore.arg.get('arguments'), kVariant = s.toBefore.arg.get('variant'), kAlignment = s.toBefore.arg.get('alignment');
    const translated = stepsToBefore(matePlan);
    const mates = translated.filter((x, i) => matePlan[i].operation === 'mate').map(x => x[kArgs]);
    check('the values of a mate reach the bridge of before as its own words, not the English ones',
          mates.length === 3 && mates.every(a => Object.values(MATE_VALUES_BEFORE.variant).includes(a[kVariant]) && Object.values(MATE_VALUES_BEFORE.alignment).includes(a[kAlignment])) &&
          mates.some(a => a[kVariant] !== matePlan.find(x => x.operation === 'mate').arguments.variant), JSON.stringify(mates).slice(0, 300));
    check('the table of before has a word for every value of today and for no other',
          JSON.stringify(Object.keys(MATE_VALUES_BEFORE.variant)) === JSON.stringify(MATE_VALUES.variant) && JSON.stringify(Object.keys(MATE_VALUES_BEFORE.alignment)) === JSON.stringify(MATE_VALUES.alignment));
    check('a mate value the table does not have stops the translation instead of reaching the bridge (seen red)',
          /has no value of before/.test(throws(() => stepsToBefore([{ operation: 'mate', arguments: { component_a: 'C2', face_a: 'down', component_b: 'C1', face_b: 'up', variant: 'concentric' } }])) || ''));
    check('--check reads a plan whose mate value is not in the table as a problem on the protocol of before too',
          planProblems({ name: 'x', steps: [mateStep({ variant: 'concentric' })] }, loadCatalogue(path.join(ROOT, 'Tools', 'catalog.json')), () => { throw new Error('mate: «variant» = «concentric» has no value of before in MATE_VALUES_BEFORE'); }).some(p => /has no value of before/.test(p)));
    const p = makeProtocol('it');
    check('the tools and the document actions of before come from the map too', p.tool('build') !== 'build' && p.tool('inspect_part') !== 'inspect_part' && Object.keys(p.document('create', 'assembly')).length === 2);
  } catch (e) { check('the protocol of before can be read from the map', false, e.message); }

  console.log(bad ? 'self-test: ' + bad + ' failed' : 'self-test: all held');
  return bad === 0;
}

module.exports = { loadIndex, loadPlan, listPlans, stepLists, groupsOf, rebaseRefs, resolveSelector, evaluate, planProblems, indexProblems, consistencyProblems, taskReferences, build, selfTest, PLANS_DIR, FAKE_BRIDGE };
if (require.main !== module) return;

(async () => {
  const argv = process.argv.slice(2);
  if (argv[0] === '--self-test') process.exit((await selfTest()) ? 0 : 1);
  else if (argv[0] === '--check') process.exit(await commandCheck(argv.slice(1)));
  else if (argv[0] === '--build') process.exit(await commandBuild(argv.slice(1)));
  else fail('usage: --check [...] | --build <bridge> <plan> <folder> [--out file] | --self-test');
})().catch(e => { console.error(e); process.exit(2); });
