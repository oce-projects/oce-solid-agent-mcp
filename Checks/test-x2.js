// =============================================================================
//  The probe of entry X2: «the part has changed since you looked at it»
// =============================================================================
//
//  The contract checks test `WhatChanged` — a pure function on two
//  dictionaries — and are fine for the criterion. They do not test the WIRING: that the
//  signature is taken in `inspect_part`, that it is updated after a successful
//  `build`, and that the refusal really comes out of the client's door.
//
//  That wiring has already had a defect, half an hour after being written:
//  the signature was not updated after `build`, so `look -> draw ->
//  draw` stopped at the second accusing a part that the bridge
//  itself had changed. It is the false positive that entry X2 put among the risks, and
//  no contract check could see it.
//
//  THIS PROBE TESTS THE TWO HALVES, AND THE SECOND COUNTS AS MUCH AS THE FIRST:
//
//    1. `look -> draw -> draw` passes. No false positive.
//    2. the FILE is touched from outside, and the following `build` stops saying what
//       has changed.
//
//  WHO CHANGES THE PART FROM OUTSIDE: `Checks\ChangeByHand`, an executable that
//  attaches to SOLIDWORKS and extrudes a small block in the active document.
//  A second actor is needed of necessity: as long as the change comes from the bridge,
//  the bridge knows it and updates its signature, and the defence must not trigger.
//
//  THE FIRST DRAFT MOVED THE DATE OF THE FILE, and it did not work. Measured on
//  23/9/2026: after `save_document` the open document keeps
//  answering `path: ""`. The file is on disk, but the session does not
//  bind to it, so in the signature there is no date to compare - and the
//  probe said «does not detect» on a half that it was not even looking at.
//  *A probe that tests the wrong thing is worse than no probe: it gives a
//  red that sends one looking for the defect where it is not.*
//
//      node Checks\test-x2.js
//
//  It wants SOLIDWORKS open. Exits 0 if the two halves pass, 1 otherwise.
// =============================================================================
'use strict';

const { spawn, execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const BRIDGE = path.join(ROOT, 'bin', 'oce-solid-agent-mcp.exe');
const NEW = path.join(ROOT, 'Checks', 'NewPart', 'bin', 'NewPart.exe');
const CHANGE = path.join(ROOT, 'Checks', 'ChangeByHand', 'bin', 'ChangeByHand.exe');
const CLOSE = path.join(ROOT, 'Checks', 'CloseLeftovers', 'bin', 'CloseLeftovers.exe');
const WORK = path.join(ROOT, 'Bench', 'work');
const PART = path.join(WORK, 'probe_x2.SLDPRT');

let trouble = 0;
function ok(itemName, real, detail) {
  if (!real) trouble++;
  console.log((real ? '  OK   ' : '  NO   ') + itemName.padEnd(56) + (detail || ''));
}

// ── the bridge, on stdio ─────────────────────────────────────────────────
function openBridge() {
  const p = spawn(BRIDGE, [], { stdio: ['pipe', 'pipe', 'pipe'] });
  p.stderr.on('data', () => {});
  let buffer = '';
  const expectedValues = new Map();     // id -> who is waiting
  const arrived = new Map();   // id -> answer that arrived before anybody was waiting for it
  p.stdout.on('data', (d) => {
    buffer += d.toString('utf8');
    let i;
    while ((i = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, i).trim();
      buffer = buffer.slice(i + 1);
      if (!line) continue;
      let o = null;
      try { o = JSON.parse(line); } catch (e) { continue; }
      // Without `id` it is a notification: it is nobody's answer.
      if (o.id === undefined || o.id === null) continue;
      const handler = expectedValues.get(o.id);
      if (handler) { expectedValues.delete(o.id); handler(o); } else arrived.set(o.id, o);
    }
  });
  return {
    proc: p,
    ask(body, waitMs) {
      return new Promise((resolve, refuse) => {
        const id = body.id;
        const deadline = setTimeout(() => {
          expectedValues.delete(id);
          refuse(new Error('no answer to the ' + id + ' within ' +
                            (waitMs || 120000) + ' ms'));
        }, waitMs || 120000);
        const take = (o) => { clearTimeout(deadline); resolve(o); };
        if (arrived.has(id)) { const o = arrived.get(id); arrived.delete(id); take(o); }
        else expectedValues.set(id, take);
        p.stdin.write(JSON.stringify(body) + '\n');
      });
    },
    close() { try { p.stdin.end(); } catch (e) {} try { p.kill(); } catch (e) {} },
  };
}

let sequentialId = 0;
function call(itemName, args) {
  return {
    jsonrpc: '2.0', id: ++sequentialId, method: 'tools/call',
    params: { name: itemName, arguments: args || {} },
  };
}

// The text of the answer, and whether it was an error.
//
// EXPECTED KEYS (W3.1-bis.2, item 5). An answer to tools/call carries
// `result.content[0].text` (the MCP protocol). The first version read an
// answer without it (a JSON-RPC `error`, or a renamed field) as "no error,
// empty text", and every `!r.failed` check passed on it. Now such an answer is
// a failure of its own, whatever the check that reads it expects.
function read(r) {
  const c = r && r.result && r.result.content;
  if (!Array.isArray(c) || c.length === 0 || !c[0] || typeof c[0].text !== 'string') {
    trouble++;
    console.log('  NO   the answer has no result.content[0].text: ' + String(JSON.stringify(r)).slice(0, 160));
    return { text: '', failed: true };
  }
  return { text: c[0].text, failed: !!r.result.isError };
}

// node Checks\test-x2.js --self-test: the reading above, seen failing on
// answers without the expected keys, without SOLIDWORKS or the bridge.
function selfTest() {
  const cases = [
    ['a normal answer', { result: { content: [{ type: 'text', text: 'ok' }] } }, false],
    ['a refusal (isError), which is an answer', { result: { content: [{ type: 'text', text: 'no' }], isError: true } }, false],
    ['a JSON-RPC error instead of a result', { error: { code: -32601, message: 'unknown tool' } }, true],
    ['result with its fields renamed', { result: { contents: [{ body: 'x' }] } }, true],
    ['an empty content', { result: { content: [] } }, true],
  ];
  let wrong = 0;
  for (const [name, answer, counted] of cases) {
    const before = trouble;
    read(answer);
    const got = trouble > before;
    if (got !== counted) wrong++;
    console.log((got === counted ? '  ok  ' : '  XX  ') + name + ': ' + (got ? 'counted as a failure' : 'read'));
  }
  console.log(wrong === 0 ? 'The reading fails on every answer without the expected keys.' : wrong + ' cases not as expected.');
  process.exit(wrong === 0 ? 0 : 1);
}
if (process.argv.includes('--self-test')) selfTest();

async function main() {
  if (!fs.existsSync(BRIDGE)) {
    console.log('cannot find ' + BRIDGE + " - build it with .\\build.ps1");
    process.exit(1);
  }

  // CLEAN DESK FIRST, as `bench.ps1` does at step 3: a probe that
  // inherits the documents of the previous run measures those too.
  try { fs.mkdirSync(WORK, { recursive: true }); } catch (e) {}
  try { execFileSync(CLOSE, ['--close', WORK], { stdio: 'ignore' }); } catch (e) {}
  try { if (fs.existsSync(PART)) fs.unlinkSync(PART); } catch (e) {}
  try { execFileSync(NEW, { stdio: 'ignore' }); }
  catch (e) { console.log('NewPart did not start: ' + e.message); process.exit(1); }

  const bridge = openBridge();
  try {
    await bridge.ask({
      jsonrpc: '2.0', id: ++sequentialId, method: 'initialize',
      params: { protocolVersion: '2024-11-05', capabilities: {},
                clientInfo: { name: 'probe-x2', version: '1' } },
    }, 120000);

    console.log('=== 1. inspect -> build -> build, without false positives');

    let r = read(await bridge.ask(call('inspect_part', {})));
    ok('inspect_part answers', !r.failed, r.failed ? r.text.slice(0, 110) : '');

    r = read(await bridge.ask(call('build', {
      steps: [{ operation: 'extrude_rectangle',
                arguments: { length_mm: 120, width_mm: 80, thickness_mm: 10 } }],
    }), 180000));
    ok('the first build passes', !r.failed, r.failed ? r.text.slice(0, 160) : '');

    // THE LINE THAT COUNTS. The first `build` changed the volume: if the signature
    // had not been updated, this second one would stop accusing a
    // hand modification that did not happen. It is the false positive that entry X2
    // put among the risks, and it really happened for half an hour.
    r = read(await bridge.ask(call('build', {
      steps: [{ operation: 'extrude_rectangle',
                arguments: { length_mm: 30, width_mm: 30, thickness_mm: 5 } }],
    }), 180000));
    ok('the second build passes (no false positive)', !r.failed,
       r.failed ? r.text.split('\n')[0].slice(0, 160) : '');

    console.log('');
    console.log('=== 2. someone changes the part from outside, and the next build stops');

    r = read(await bridge.ask(call('inspect_part', {})));
    ok('inspect_part takes the signature', !r.failed, r.failed ? r.text.slice(0, 110) : '');

    // THE EXTERNAL MODIFICATION. Another process, which the bridge does not know,
    // extrudes a small block in the active document.
    let saidByChange = "";
    try {
      saidByChange = execFileSync(CHANGE, ['15'], { encoding: 'utf8' }).trim();
    } catch (e) {
      saidByChange = "(exploded) " + (e.stdout || e.message || "").toString().trim();
    }
    ok('someone changed the part from outside',
       saidByChange.indexOf('volume:') >= 0 &&
       saidByChange.indexOf("NOTHING CHANGED") < 0,
       saidByChange.split('\n').pop());

    r = read(await bridge.ask(call('build', {
      steps: [{ operation: 'extrude_rectangle',
                arguments: { length_mm: 20, width_mm: 20, thickness_mm: 5 } }],
    }), 180000));
    ok('the next build stops', r.failed, r.text.split('\n')[0].slice(0, 100));
    ok('and says WHAT changed',
       r.text.indexOf('the volume') >= 0 || r.text.indexOf('features') >= 0,
       r.text.split('\n')[0].slice(0, 130));
    ok('and says what to do', r.text.indexOf('inspect_part') >= 0, '');

    // AND AFTER READING AGAIN, WE START OVER. A refusal that cannot be left is not
    // a defence, it is a wall: entry X2 asks to stop WITH THE CRITERION, and
    // the criterion says what to do to carry on.
    r = read(await bridge.ask(call('inspect_part', {})));
    ok('it reads again', !r.failed, '');
    r = read(await bridge.ask(call('build', {
      steps: [{ operation: 'extrude_rectangle',
                arguments: { length_mm: 20, width_mm: 20, thickness_mm: 5 } }],
    }), 180000));
    ok('and after reading again it goes on', !r.failed,
       r.failed ? r.text.split('\n')[0].slice(0, 130) : '');
  } finally {
    bridge.close();
  }

  console.log('');
  console.log(trouble === 0 ? 'All good.' : trouble + ' to fix.');
  process.exit(trouble === 0 ? 0 : 1);
}

main().catch((e) => { console.log('probe blew up: ' + e.message); process.exit(1); });
