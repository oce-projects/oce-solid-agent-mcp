// Builds, through the MCP bridge, the part the three probes of D9Defects read:
// the short bars of the tube stand of task 07 (Bench/tasks/07_tube_stand.md), the four
// feet drawn 20 short of the frame and extended to it by trim_members, capped at the
// bottom by end_cap on the step of the feet. It LEAVES THE PART OPEN and prints its
// title, which Checks\D9Defects\bin\D9Defects.exe (tree DOC, gusset DOC X Z) takes to
// read it by name from inside the same SOLIDWORKS, never as the document in front.
//
//   node Checks/D9Defects/build-stand.js <bridge.exe> <log-folder> [feet|one-gusset]
//
//   feet         the short bars, the feet and their caps (10 bodies); no gusset
//   one-gusset   the same plus ONE gusset, at the corner that sits at the START of
//                its bar segment (the corner that works), plus the cut list: 12 bodies
//
// Why through the bridge and not by COM: the probes have to look at the part the way
// the bridge makes it, with the same operations in the same order (the order of the
// bodies, the names, the folders of the cut list are products of those operations).
// This script only builds; every measurement is in D9Defects.exe, by COM.
//
// SOLIDWORKS may be shared: check that nobody else is using the session
// before running it, and close what this leaves open (CloseLeftovers).
'use strict';
const fs = require('fs'), path = require('path'), cp = require('child_process');
const exe = process.argv[2];
const out = process.argv[3];
const mode = process.argv[4] || 'feet';
if (!exe || !out) { console.error('usage: node build-stand.js <bridge.exe> <log-folder> [feet|one-gusset]'); process.exit(2); }
fs.mkdirSync(out, { recursive: true });
const log = path.join(out, 'build.jsonl');
if (!process.env.PROFILI_SALDATURA) { console.error('PROFILI_SALDATURA is not set (the folder of the .SLDLFP profiles)'); process.exit(2); }
const PROF = process.env.PROFILI_SALDATURA + '\\TUBO STRUTTURALE QUADRO.SLDLFP';
const child = cp.spawn(exe, [], { stdio: ['pipe', 'pipe', 'pipe'], cwd: path.dirname(exe), windowsHide: true });
let buf = '', next = 1; const pend = new Map();
child.stdout.on('data', d => { buf += d.toString('utf8'); let i; while ((i = buf.indexOf('\n')) >= 0) { const l = buf.slice(0, i).trim(); buf = buf.slice(i + 1); if (!l) continue; let m; try { m = JSON.parse(l); } catch (e) { continue; } if (pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } } });
child.stderr.on('data', () => {});
const req = (method, params) => new Promise(r => { const id = next++; pend.set(id, r); child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n'); });
const tool = async (name, args) => { const r = await req('tools/call', { name, arguments: args }); const res = r.result || {}; const text = (res.content || []).map(c => c.text || '').join(''); let j = null; try { j = JSON.parse(text); } catch (e) {} fs.appendFileSync(log, JSON.stringify({ name, args, isError: !!res.isError, json: j, text: j ? undefined : text.slice(0, 2000) }) + '\n'); return { j, text, isError: !!res.isError }; };
const member = (segs, corner) => ({ operation: 'structural_member', arguments: { profile_path: PROF, configuration: '40x40x3', corner_treatment: corner || 'none', segments: segs } });
const say = (m) => console.log(m);
const near = (a, b) => Math.abs(a - b) <= 0.5;
const outcome = (r) => { const o = r.j && r.j.outcomes ? r.j.outcomes : []; if (!o.length) return 'NO ANSWER ' + (r.text || '').slice(0, 300); return o.map(x => x.operation + ':' + (x.succeeded ? 'OK' : 'NO ' + String(x.error).slice(0, 220)) + ' ' + JSON.stringify(x.measurements || {}).slice(0, 600)).join(' | '); };
(async () => {
  await req('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'd9defects', version: '1' } });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  await req('tools/list', {});
  await tool('document', { action: 'create', variant: 'part' });
  await tool('inspect_part', {});
  let r = await tool('build', { steps: [
    member([{ start: [-480, 0, -280], end: [-480, 0, 280] }, { start: [480, 0, -280], end: [480, 0, 280] }]),
    member([{ start: [-480, -40, -280], end: [-480, -170, -280] }, { start: [480, -40, -280], end: [480, -170, -280] },
            { start: [-480, -40, 280], end: [-480, -170, 280] }, { start: [480, -40, 280], end: [480, -170, 280] }]),
    { operation: 'trim_members', arguments: { bodies_to_trim_ref: { step: 2, kind: 'bodies' }, trim_boundary_ref: { step: 1, kind: 'bodies' } } },
    { operation: 'end_cap', arguments: { ends_ref: { step: 2, kind: 'free_ends' }, thickness_mm: 3 } },
  ] });
  say('feet: ' + outcome(r));
  if (mode === 'one-gusset') {
    const st = (await tool('inspect_part', {})).j;
    const x = -480, z = -280;                      // the corner at the START of its bar segment
    const wz = z + 20, sx = x + 16;
    const wall = st.faces.filter(f => f.area_mm2 === 4800 && near(f.point_mm[0], sx) && near(f.point_mm[2], wz));
    const under = st.faces.filter(f => near(f.point_mm[0], sx) && near(f.point_mm[1], -20) && near(f.point_mm[2], 0) && f.area_mm2 > 10000);
    say('faces wall ' + wall.length + ' under ' + under.length);
    if (wall.length === 1 && under.length === 1) {
      r = await tool('build', { steps: [{ operation: 'gusset', arguments: { faces_uid: [wall[0].uid, under[0].uid], thickness_mm: 3, leg1_mm: 40, leg2_mm: 40, position: 'center' } }] });
      say('gusset: ' + outcome(r));
    }
    r = await tool('build', { steps: [{ operation: 'cut_list', arguments: {} }] });
    say('cut_list: ' + outcome(r));
  }
  const st2 = (await tool('inspect_part', {})).j;
  say('bodies ' + st2.bodies_total + ' title ' + st2.document_title);
  child.stdin.end();
  setTimeout(() => process.exit(0), 2000);
})().catch(e => { say('error ' + e.stack); process.exit(1); });
