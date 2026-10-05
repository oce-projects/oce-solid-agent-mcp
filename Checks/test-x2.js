// =============================================================================
//  La sonda della voce X2: «il pezzo e' cambiato da quando l'hai guardato»
// =============================================================================
//
//  Le prove di contratto provano `WhatChanged` — una funzione pura su due
//  dizionari — e vanno bene per il criterio. Non provano il CABLAGGIO: che la
//  firma si prenda in `inspect_part`, che si aggiorni dopo un `build`
//  riuscito, e che il rifiuto esca davvero dalla porta del cliente.
//
//  Quel cablaggio ha gia' avuto un difetto, mezz'ora dopo essere stato scritto:
//  la firma non si aggiornava dopo `build`, quindi `guarda -> disegna ->
//  disegna` si fermava alla seconda accusando un pezzo che aveva cambiato il
//  ponte stesso. E' il falso positivo che la voce X2 metteva fra i rischi, e
//  nessuna prova di contratto poteva vederlo.
//
//  QUESTA SONDA PROVA LE DUE META', E LA SECONDA CONTA QUANTO LA PRIMA:
//
//    1. `guarda -> disegna -> disegna` passa. Nessun falso positivo.
//    2. si tocca il FILE da fuori, e il `build` dopo si ferma dicendo cosa
//       e' cambiato.
//
//  CHI CAMBIA IL PEZZO DA FUORI: `Checks\ChangeByHand`, un eseguibile che
//  si aggancia a SOLIDWORKS ed estrude un blocchetto nel documento attivo.
//  Serve un secondo attore per forza: finche' la modifica arriva dal ponte,
//  il ponte lo sa e aggiorna la sua firma, e la difesa non deve scattare.
//
//  LA PRIMA STESURA SPOSTAVA LA DATA DEL FILE, e non funzionava. Misurato il
//  23/9/2026: dopo `save_document` il documento aperto continua a
//  rispondere `path: ""`. Il file su disco c'e', ma la sessione non ci
//  si lega, quindi nella firma non c'e' nessuna data da confrontare - e la
//  sonda diceva «non rileva» su una meta' che non stava nemmeno guardando.
//  *Una sonda che prova la cosa sbagliata e' peggio di nessuna sonda: da' un
//  rosso che manda a cercare il difetto dove non c'e'.*
//
//      node Checks\test-x2.js
//
//  Vuole SOLIDWORKS aperto. Esce 0 se le due meta' passano, 1 altrimenti.
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
const WORK = path.join(ROOT, 'Bench', 'lavoro');
const PART = path.join(WORK, 'prova_x2.SLDPRT');

let trouble = 0;
function ok(itemName, real, detail) {
  if (!real) trouble++;
  console.log((real ? '  OK   ' : '  NO   ') + itemName.padEnd(56) + (detail || ''));
}

// ── il ponte, sullo stdio ────────────────────────────────────────────────
function openBridge() {
  const p = spawn(BRIDGE, [], { stdio: ['pipe', 'pipe', 'pipe'] });
  p.stderr.on('data', () => {});
  let buffer = '';
  const expectedValues = new Map();     // id -> chi aspetta
  const arrived = new Map();   // id -> risposta arrivata prima che la si aspettasse
  p.stdout.on('data', (d) => {
    buffer += d.toString('utf8');
    let i;
    while ((i = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, i).trim();
      buffer = buffer.slice(i + 1);
      if (!line) continue;
      let o = null;
      try { o = JSON.parse(line); } catch (e) { continue; }
      // Senza `id` e' una notifica: non e' la risposta di nessuno.
      if (o.id === undefined || o.id === null) continue;
      const chi = expectedValues.get(o.id);
      if (chi) { expectedValues.delete(o.id); chi(o); } else arrived.set(o.id, o);
    }
  });
  return {
    processo: p,
    chiedi(body, waitMs) {
      return new Promise((resolve, refuse) => {
        const id = body.id;
        const tempo = setTimeout(() => {
          expectedValues.delete(id);
          refuse(new Error('niente risposta alla ' + id + ' entro ' +
                            (waitMs || 120000) + ' ms'));
        }, waitMs || 120000);
        const take = (o) => { clearTimeout(tempo); resolve(o); };
        if (arrived.has(id)) { const o = arrived.get(id); arrived.delete(id); take(o); }
        else expectedValues.set(id, take);
        p.stdin.write(JSON.stringify(body) + '\n');
      });
    },
    chiudi() { try { p.stdin.end(); } catch (e) {} try { p.kill(); } catch (e) {} },
  };
}

let sequentialId = 0;
function call(itemName, args) {
  return {
    jsonrpc: '2.0', id: ++sequentialId, method: 'tools/call',
    params: { name: itemName, arguments: args || {} },
  };
}

// Il testo della risposta, e se era un errore.
//
// EXPECTED KEYS (W3.1-bis.2, item 5). An answer to tools/call carries
// `result.content[0].text` (the MCP protocol). The first version read an
// answer without it (a JSON-RPC `error`, or a renamed field) as "no error,
// empty text", and every `!r.male` check passed on it. Now such an answer is
// a failure of its own, whatever the check that reads it expects.
function read(r) {
  const c = r && r.result && r.result.content;
  if (!Array.isArray(c) || c.length === 0 || !c[0] || typeof c[0].text !== 'string') {
    trouble++;
    console.log('  NO   the answer has no result.content[0].text: ' + String(JSON.stringify(r)).slice(0, 160));
    return { testo: '', male: true };
  }
  return { testo: c[0].text, male: !!r.result.isError };
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
    console.log('non trovo ' + BRIDGE + " - si produce con .\\build.ps1");
    process.exit(1);
  }

  // SCRIVANIA PULITA PRIMA, come fa `bench.ps1` al passo 3: una sonda che
  // eredita i documenti della corsa precedente misura anche quelli.
  try { fs.mkdirSync(WORK, { recursive: true }); } catch (e) {}
  try { execFileSync(CLOSE, ['--chiudi', WORK], { stdio: 'ignore' }); } catch (e) {}
  try { if (fs.existsSync(PART)) fs.unlinkSync(PART); } catch (e) {}
  try { execFileSync(NEW, { stdio: 'ignore' }); }
  catch (e) { console.log('NewPart non e\' partito: ' + e.message); process.exit(1); }

  const bridge = openBridge();
  try {
    await bridge.chiedi({
      jsonrpc: '2.0', id: ++sequentialId, method: 'initialize',
      params: { protocolVersion: '2024-11-05', capabilities: {},
                clientInfo: { name: 'prova-x2', version: '1' } },
    }, 120000);

    console.log('=== 1. guarda -> disegna -> disegna, senza falsi positivi');

    let r = read(await bridge.chiedi(call('inspect_part', {})));
    ok('inspect_part risponde', !r.male, r.male ? r.testo.slice(0, 110) : '');

    r = read(await bridge.chiedi(call('build', {
      steps: [{ operation: 'extrude_rectangle',
                arguments: { length_mm: 120, width_mm: 80, thickness_mm: 10 } }],
    }), 180000));
    ok('il primo disegna passa', !r.male, r.male ? r.testo.slice(0, 160) : '');

    // LA RIGA CHE CONTA. Il primo `build` ha cambiato il volume: se la firma
    // non si fosse aggiornata, questo secondo si fermerebbe accusando una
    // modifica a mano che non c'e' stata. E' il falso positivo che la voce X2
    // metteva fra i rischi, ed e' successo davvero per mezz'ora.
    r = read(await bridge.chiedi(call('build', {
      steps: [{ operation: 'extrude_rectangle',
                arguments: { length_mm: 30, width_mm: 30, thickness_mm: 5 } }],
    }), 180000));
    ok('il secondo disegna passa (nessun falso positivo)', !r.male,
       r.male ? r.testo.split('\n')[0].slice(0, 160) : '');

    console.log('');
    console.log('=== 2. qualcuno cambia il pezzo da fuori, e il disegna dopo si ferma');

    r = read(await bridge.chiedi(call('inspect_part', {})));
    ok('inspect_part prende la firma', !r.male, r.male ? r.testo.slice(0, 110) : '');

    // LA MODIFICA ESTERNA. Un altro processo, che il ponte non conosce,
    // estrude un blocchetto nel documento attivo.
    let saidByChange = "";
    try {
      saidByChange = execFileSync(CHANGE, ['15'], { encoding: 'utf8' }).trim();
    } catch (e) {
      saidByChange = "(esploso) " + (e.stdout || e.message || "").toString().trim();
    }
    ok('qualcuno ha cambiato il pezzo da fuori',
       saidByChange.indexOf('volume:') >= 0 &&
       saidByChange.indexOf("NON E' CAMBIATO") < 0,
       saidByChange.split('\n').pop());

    r = read(await bridge.chiedi(call('build', {
      steps: [{ operation: 'extrude_rectangle',
                arguments: { length_mm: 20, width_mm: 20, thickness_mm: 5 } }],
    }), 180000));
    ok('il disegna dopo si ferma', r.male, r.testo.split('\n')[0].slice(0, 100));
    ok('e dice CHE COSA e\' cambiato',
       r.testo.indexOf('il volume') >= 0 || r.testo.indexOf('lavorazioni') >= 0,
       r.testo.split('\n')[0].slice(0, 130));
    ok('e dice cosa fare', r.testo.indexOf('inspect_part') >= 0, '');

    // E DOPO AVER RILETTO, SI RIPARTE. Un rifiuto da cui non si esce non e'
    // una difesa, e' un muro: la voce X2 chiede di fermarsi COL CRITERIO, e
    // il criterio dice cosa fare per proseguire.
    r = read(await bridge.chiedi(call('inspect_part', {})));
    ok('si rilegge', !r.male, '');
    r = read(await bridge.chiedi(call('build', {
      steps: [{ operation: 'extrude_rectangle',
                arguments: { length_mm: 20, width_mm: 20, thickness_mm: 5 } }],
    }), 180000));
    ok('e dopo aver riletto si riparte', !r.male,
       r.male ? r.testo.split('\n')[0].slice(0, 130) : '');
  } finally {
    bridge.chiudi();
  }

  console.log('');
  console.log(trouble === 0 ? 'A posto.' : trouble + ' da sistemare.');
  process.exit(trouble === 0 ? 0 : 1);
}

main().catch((e) => { console.log('sonda esplosa: ' + e.message); process.exit(1); });
